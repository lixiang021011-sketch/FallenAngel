using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using FallenAngel.Core;

namespace FallenAngel.InputSystem
{
    /// <summary>
    /// 输入事件数据
    /// </summary>
    public struct LaneInputArgs
    {
        public int laneIndex;        // 音轨索引 0-4（按当前活动键数）
        /// <summary>本次按下时该轨累计的纵向滑动位移（屏幕坐标，正值 = 上滑；键盘/鼠标为 0）</summary>
        public float swipeY;
        /// <summary>滑动方向是否可信（触屏 true；键盘/编辑器鼠标 false，flick 方向判定据此放宽）</summary>
        public bool swipeKnown;
        public bool isPressed;       // true=按下, false=抬起
        public Vector2 touchPos;     // 触屏位置（屏幕坐标）
        /// <summary>
        /// 本次输入对应的**歌曲时间**（秒）。
        /// 触屏 = 事件自带的时间戳换算（同一帧里晚读到的触摸，按它真实发生的时刻算）；
        /// 键盘/编辑器鼠标 = 当前帧的歌曲时间。判定用它，而不是帧采样的 SongTime。
        /// </summary>
        public float musicTime;
        /// <summary>
        /// 输入来源的身份：触屏 = fingerId；键盘 = -(lane+2)（每轨一个）；编辑器鼠标 = -1。
        /// 判定护栏按它记账——"一次输入判一个音符"是**每根手指**的规则，不是每帧的规则。
        /// </summary>
        public int fingerId;
    }

    /// <summary>
    /// 输入管理器 - 统一处理触屏(手游)和键盘(PC调试)输入
    /// 将输入转换为音轨级别的按下/抬起事件
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [Header("键盘映射（PC调试用）")]
        [Tooltip("4 键谱（v1/鼓谱）：D F J K；5 键谱（v2/吉他谱）：D F G J K（SPACE 仍为暂停）")]
        [SerializeField] private KeyCode[] laneKeys4 = new KeyCode[]
        {
            KeyCode.D,      // 音轨0
            KeyCode.F,      // 音轨1
            KeyCode.J,      // 音轨2
            KeyCode.K        // 音轨3
        };

        [SerializeField] private KeyCode[] laneKeys5 = new KeyCode[]
        {
            KeyCode.D,      // 音轨0
            KeyCode.F,      // 音轨1
            KeyCode.G,      // 音轨2
            KeyCode.J,      // 音轨3
            KeyCode.K        // 音轨4
        };

        /// <summary>当前生效键表（按 LaneLayout.ActiveLaneCount 切换）</summary>
        private KeyCode[] ActiveKeys => LaneLayout.ActiveLaneCount == 5 ? laneKeys5 : laneKeys4;

        [Header("触屏判定区域底部高度（屏幕高度比例）")]
        [Tooltip("判定区为屏幕底部 touchBottomRatio 比例（0.6 = 底部60%），y=0 为屏幕底边")]
        [Range(0.1f, 0.9f)]
        public float touchBottomRatio = 0.6f;

        /// <summary>
        /// 音轨输入事件：按下/抬起时触发
        /// 参数: laneIndex, isPressed
        /// </summary>
        public event System.EventHandler<LaneInputArgs> OnLaneInput;

        /// <summary>
        /// 获取当前各音轨是否处于按下状态（长度 = 当前活动键数，由 EnsureLaneArrays 维护）
        /// </summary>
        public bool[] LanePressStates { get; private set; } = new bool[4];

        /// <summary>每轨当前按住它的手指 id（-1 = 无）；判定护栏按手指记账要用</summary>
        private int[] laneFinger = new int[4];

        /// <summary>取"当前按住这条轨的手指 id"；没有则 -1</summary>
        public int FingerIdForLane(int lane)
        {
            if (laneFinger == null || lane < 0 || lane >= laneFinger.Length) return -1;
            return LanePressStates != null && lane < LanePressStates.Length && LanePressStates[lane]
                ? laneFinger[lane] : -1;
        }

        /// <summary>这根手指现在是否还按在某个轨道上（手势是否由它维持）</summary>
        public bool IsFingerHeld(int fingerId)
        {
            if (fingerId < 0 || laneFinger == null || LanePressStates == null) return false;
            int n = Mathf.Min(laneFinger.Length, LanePressStates.Length);
            for (int i = 0; i < n; i++)
                if (LanePressStates[i] && laneFinger[i] == fingerId) return true;
            return false;
        }

        /// <summary>
        /// 取某根手指当前的屏幕坐标（判定层把它换算成"小数轨道"）。
        /// 键盘没有坐标：按 laneHint 返回该轨中点（等价于整数轨道）。找不到返回 false。
        /// </summary>
        public bool TryGetFingerScreenPosition(int fingerId, int laneHint, out Vector2 screenPos)
        {
            if (fingerScreenPos.TryGetValue(fingerId, out screenPos)) return true;

            if (laneHint >= 0)
            {
                int count = Mathf.Max(1, LanePressStates != null ? LanePressStates.Length : 4);
                screenPos = new Vector2(Screen.width * (laneHint + 0.5f) / count, Screen.height * 0.3f);
                return true;
            }

            screenPos = Vector2.zero;
            return false;
        }

        /// <summary>屏幕坐标 → Canvas 本地坐标（与 LaneLayout / 判定轨道同一坐标系）</summary>
        public bool TryGetCanvasPosition(Vector2 screenPos, out Vector2 canvasPos)
        {
            canvasPos = Vector2.zero;
            if (canvasForLaneMapping == null)
            {
                canvasForLaneMapping = FindObjectOfType<Canvas>();
                if (canvasForLaneMapping == null) return false;
            }

            RectTransform canvasRect = canvasForLaneMapping.transform as RectTransform;
            if (canvasRect == null) return false;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, null, out canvasPos);
        }

        // 用于跟踪触屏ID与音轨的对应关系
        private Dictionary<int, int> touchIdToLane = new Dictionary<int, int>();
        // 真实触摸事件时间戳（EnhancedTouch，装 Input System 后才有；没装则回退帧时间）
        private readonly Dictionary<int, double> touchEventTimes = new Dictionary<int, double>();
        // flick 方向判定用：每根手指上次位置 + 每轨累计纵向滑动（屏幕 y 向上为正 = 上滑）
        private Dictionary<int, Vector2> touchPrev = new Dictionary<int, Vector2>();
        private Dictionary<int, float> laneSwipeY = new Dictionary<int, float>();
        // 每根手指最近一次的屏幕坐标（判定层算小数轨道用）
        private readonly Dictionary<int, Vector2> fingerScreenPos = new Dictionary<int, Vector2>();
        // 上一帧的键盘按下状态
        private bool[] lastKeyStates = new bool[4];

        /// <summary>
        /// 确保轨道状态数组与当前活动键数一致（4K/5K 切换时重建）
        /// </summary>
        private void EnsureLaneArrays()
        {
            int count = LaneLayout.ActiveLaneCount;
            if (LanePressStates == null || LanePressStates.Length != count)
                LanePressStates = new bool[count];
            if (laneFinger == null || laneFinger.Length != count)
                laneFinger = new int[count];
            if (laneFinger.Length == count)
            {
                for (int i = 0; i < count; i++)
                    if (!LanePressStates[i]) laneFinger[i] = -1;
            }
            if (lastKeyStates == null || lastKeyStates.Length != count)
                lastKeyStates = new bool[count];
        }
        // 编辑器鼠标模拟触摸：当前按下的轨道（-1 = 未按下）
        private int editorMouseLane = -1;
        // Canvas 引用（懒解析）：屏幕坐标 → Canvas 本地坐标的轨道映射用
        private Canvas canvasForLaneMapping;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            EnableEnhancedTouch();
        }

        /// <summary>启用 EnhancedTouch（新输入系统的触摸时间戳）；未装包时是空操作</summary>
        private void EnableEnhancedTouch()
        {
#if ENABLE_INPUT_SYSTEM
            try
            {
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[InputManager] EnhancedTouch 启用失败，回退到帧时间戳：" + e.Message);
            }
#endif
        }

        /// <summary>每帧缓存"fingerId → 事件时刻"，供本帧的触摸事件换算音乐时间</summary>
        private void RefreshTouchEventTimes()
        {
            touchEventTimes.Clear();
#if ENABLE_INPUT_SYSTEM
            var active = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            for (int i = 0; i < active.Count; i++)
            {
                var t = active[i];
                touchEventTimes[t.touchId] = t.time;
            }
#endif
        }

        /// <summary>取这根手指本次事件的时间戳；拿不到返回 -1（调用方回退帧时间）</summary>
        private double TouchEventTime(int fingerId)
        {
            return touchEventTimes.TryGetValue(fingerId, out double t) ? t : -1.0;
        }

        private void OnEnable()
        {
            SubscribeStateChanged();
        }

        private void Start()
        {
            // 先退订再订阅，防止 Awake 执行顺序导致漏订/重订
            SubscribeStateChanged();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
        }

        private void SubscribeStateChanged()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnStateChanged -= OnGameStateChanged;
            GameManager.Instance.OnStateChanged += OnGameStateChanged;
        }

        private void Update()
        {
            // 轨道数可能随谱面切换（4K/5K），每帧对齐数组
            EnsureLaneArrays();

#if DEVELOPMENT_BUILD
            LogTouchForDeviceDebug();
#endif

            bool playing = GameManager.Instance != null &&
                           GameManager.Instance.CurrentState == GameState.Playing;

            // 键盘状态始终跟踪（暂停期间也同步 lastKeyStates，防止恢复后状态错位吞键）
            TrackKeyboardStates(playing);

            // 轨道触摸/点击输入：暂停期间不处理并清空跟踪
            if (playing)
            {
#if UNITY_EDITOR
                // 编辑器内以鼠标为准：Device Simulator 的触摸注入与鼠标同源，
                // 且旧输入系统下注入坐标可能偏移，直接读鼠标最可靠
                HandleEditorMouseInput();
#else
                HandleTouchInput();
#endif
            }
            else
            {
                // 暂停/离曲：先按 fingerId 释放轨道，再清映射。只 Clear 字典会让 LanePressStates 卡住。
                ReleaseTrackedTouches(Vector2.zero);
#if UNITY_EDITOR
                ReleaseEditorMouseLane();
#endif
            }
        }

#if DEVELOPMENT_BUILD
        // ── 真机排查用：任意界面都把"按下"的屏幕坐标记一行（每帧最多一条，基本无损）──
        // 为什么要它：MIUI 拦掉了 `adb shell input`，只剩 monkey 能注入，而 monkey 不打印坐标。
        // 有了这行日志，就能把"某个 monkey 种子"映射到"某个屏幕坐标"，从而精确点击按钮/判定区。
        private int lastTouchLogFrame = -10;

        private void LogTouchForDeviceDebug()
        {
            if (UnityEngine.Input.touchCount <= 0) return;

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                if (Time.frameCount == lastTouchLogFrame) continue;

                lastTouchLogFrame = Time.frameCount;
                Vector2 p = t.position;
                string zone = p.y <= Screen.height * touchBottomRatio ? "判定区内" : "上半屏";
                Debug.Log(string.Format("[InputManager] 触摸按下（真机）：屏 {0:0}×{1:0}（屏高 {2}，{3}）",
                    p.x, p.y, Screen.height, zone));
            }
        }
#endif

        /// <summary>
        /// 游戏恢复时按真实按键状态同步一次：
        /// 暂停期间发生的按下/抬起会补发对应事件，修复按键卡在各种状态的问题
        /// </summary>
        private void OnGameStateChanged(GameState state)
        {
            if (state != GameState.Playing) return;

            EnsureLaneArrays();
            int count = ActiveKeys.Length;
            for (int i = 0; i < count; i++)
            {
                bool actual = UnityEngine.Input.GetKey(ActiveKeys[i]);
                lastKeyStates[i] = actual;
                if (LanePressStates[i] != actual)
                {
                    Vector2 pos = new Vector2(Screen.width * (i + 0.5f) / count, Screen.height * 0.3f);
                    SetLaneState(i, actual, pos, false, -(i + 2));   // 键盘：每轨一个独立身份
                }
            }
            ReleaseTrackedTouches(Vector2.zero);
#if UNITY_EDITOR
            ReleaseEditorMouseLane();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器鼠标模拟触摸：按下/拖动/抬起映射为轨道输入。
        /// 与真实触摸同样的规则：仅下半部有效区、UI 上不响应、支持跨轨拖动。
        /// 修复：Device Simulator 在旧输入系统下的触摸注入坐标偏移导致无法操作。
        /// </summary>
        private void HandleEditorMouseInput()
        {
            bool held = UnityEngine.Input.GetMouseButton(0);
            Vector2 mousePos = UnityEngine.Input.mousePosition;

            if (!held)
            {
                ReleaseEditorMouseLane();
                return;
            }

            // 只处理底部判定区 / UI 上的点击不响应（与真实触摸一致）
            if (mousePos.y > Screen.height * touchBottomRatio || IsPointerOverUI(mousePos))
            {
                ReleaseEditorMouseLane();
                return;
            }

            int lane = GetLaneFromScreenPos(mousePos);
            if (lane < 0 || lane >= LaneLayout.ActiveLaneCount)
            {
                ReleaseEditorMouseLane();
                return;
            }

            if (editorMouseLane != lane)
            {
                // 按下或跨轨：释放旧轨道、按下新轨道（与触摸 Moved 行为一致）
                if (editorMouseLane >= 0)
                    SetLaneState(editorMouseLane, false, mousePos);
                SetLaneState(lane, true, mousePos);
                editorMouseLane = lane;
            }
            fingerScreenPos[-1] = mousePos;
        }

        /// <summary>
        /// 编辑器鼠标抬起/失效时释放当前按下的轨道
        /// </summary>
        private void ReleaseEditorMouseLane()
        {
            if (editorMouseLane < 0) return;
            SetLaneState(editorMouseLane, false, UnityEngine.Input.mousePosition);
            editorMouseLane = -1;
            fingerScreenPos.Remove(-1);
        }
#endif

        /// <summary>
        /// 处理移动端触屏输入。
        /// 抬起/取消必须按 fingerId 释放，即使手指已经滑出判定区或落到 UI 上；
        /// 已跟踪的手指滑出判定区也要释放（与编辑器鼠标离开底部一致）。
        /// </summary>
        private void HandleTouchInput()
        {
            if (UnityEngine.Input.touchCount <= 0) return;
            RefreshTouchEventTimes();

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                Touch touch = UnityEngine.Input.GetTouch(i);
                Vector2 touchPos = touch.position;
                int touchId = touch.fingerId;
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;

                if (ended)
                {
                    ReleaseTouchId(touchId, touchPos);
                    continue;
                }

                bool inHitZone = touchPos.y <= Screen.height * touchBottomRatio
                    && !IsPointerOverUI(touchPos);
                int lane = inHitZone ? GetLaneFromScreenPos(touchPos) : -1;
                if (lane < 0 || lane >= LaneLayout.ActiveLaneCount)
                {
                    if (touchIdToLane.ContainsKey(touchId))
                        ReleaseTouchId(touchId, touchPos);
                    continue;
                }

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        if (!touchIdToLane.ContainsKey(touchId))
                        {
                            touchIdToLane[touchId] = lane;
                            touchPrev[touchId] = touchPos;
                            fingerScreenPos[touchId] = touchPos;
                            laneSwipeY[lane] = 0f;                 // 新按下：该轨滑动量归零
                            SetLaneState(lane, true, touchPos, true, touchId, TouchEventTime(touchId));
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        fingerScreenPos[touchId] = touchPos;
                        // 累计纵向滑动（flick 方向判定用）：同一根手指跨轨时，位移记到当前轨
                        if (touchPrev.TryGetValue(touchId, out Vector2 prev))
                        {
                            if (touchIdToLane.TryGetValue(touchId, out int cur)) 
                                laneSwipeY[cur] = (laneSwipeY.TryGetValue(cur, out float acc) ? acc : 0f) + (touchPos.y - prev.y);
                            touchPrev[touchId] = touchPos;
                        }
                        if (touchIdToLane.TryGetValue(touchId, out int oldLane) && oldLane != lane)
                        {
                            touchIdToLane[touchId] = lane;
                            // 旧轨道仅当没有其他手指按住时才释放（支持同轨多指交替）
                            if (!IsLaneHeldByAnyTouch(oldLane))
                                SetLaneState(oldLane, false, touchPos, true, touchId, TouchEventTime(touchId));
                            // 跨轨按下：带该轨累计滑动量
                            SetLaneState(lane, true, touchPos, true, touchId, TouchEventTime(touchId));
                        }
                        break;
                }
            }
        }

        /// <summary>按 fingerId 释放一条触摸；同轨仍有其他手指时不抬起该轨。</summary>
        private void ReleaseTouchId(int touchId, Vector2 touchPos)
        {
            if (!touchIdToLane.TryGetValue(touchId, out int releaseLane)) return;
            touchIdToLane.Remove(touchId);
            fingerScreenPos.Remove(touchId);
            if (!IsLaneHeldByAnyTouch(releaseLane))
                SetLaneState(releaseLane, false, touchPos, false, touchId);
        }

        /// <summary>暂停/离曲时释放全部已跟踪触摸。键盘仍按住的轨道不强制抬起。</summary>
        private void ReleaseTrackedTouches(Vector2 touchPos)
        {
            if (touchIdToLane.Count == 0) return;
            var lanes = new HashSet<int>(touchIdToLane.Values);
            touchIdToLane.Clear();
            fingerScreenPos.Clear();
            foreach (int lane in lanes)
            {
                bool keyHeld = lane >= 0 && lane < lastKeyStates.Length && lastKeyStates[lane];
                if (!keyHeld)
                    SetLaneState(lane, false, touchPos);
            }
        }

        private bool IsLaneHeldByAnyTouch(int lane)
        {
            foreach (var kv in touchIdToLane)
            {
                if (kv.Value == lane) return true;
            }
            return false;
        }

        /// <summary>
        /// 处理PC端键盘输入：始终跟踪实际按键状态，仅在进行中发出轨道输入事件
        /// </summary>
        private void TrackKeyboardStates(bool emitEvents)
        {
            int count = ActiveKeys.Length;
            for (int i = 0; i < count; i++)
            {
                bool isKeyDown = UnityEngine.Input.GetKey(ActiveKeys[i]);
                bool wasDown = lastKeyStates[i];

                if (isKeyDown != wasDown)
                {
                    lastKeyStates[i] = isKeyDown;
                    if (emitEvents)
                    {
#if UNITY_EDITOR
                        if (isKeyDown)
                            Debug.Log($"[InputManager] Key DOWN lane={i} key={ActiveKeys[i]}");
#endif
                        Vector2 pos = new Vector2(Screen.width * (i + 0.5f) / count, Screen.height * 0.3f);
                        SetLaneState(i, isKeyDown, pos);
                    }
                }
            }
            // SPACE/ESC 由 PauseController 处理
        }

        /// <summary>
        /// 改变音轨按下状态并触发事件
        /// </summary>
        private void SetLaneState(int lane, bool pressed, Vector2 touchPos, bool fromTouch = false, int fingerId = -1,
                                  double eventTime = -1.0)
        {
            // 默认时间戳 = 本帧歌曲时间。触屏事件真正自带时间戳的路径见 SetLaneStateAt
            //（legacy Input 拿不到触摸事件时间，接 EnhancedTouch 后由那条路补上）
            float musicTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            if (eventTime >= 0.0) musicTime = InputClock.ToMusicTime(eventTime);
            SetLaneStateAt(lane, pressed, touchPos, fromTouch, musicTime, fingerId);
        }

        /// <summary>按指定歌曲时间触发音轨事件（自检注入事件时间戳用）</summary>
        private void SetLaneStateAt(int lane, bool pressed, Vector2 touchPos, bool fromTouch, float musicTime,
                                    int fingerId = -1)
        {
            if (lane < 0 || lane >= LanePressStates.Length) return;
            LanePressStates[lane] = pressed;
            if (laneFinger != null && lane < laneFinger.Length)
                laneFinger[lane] = pressed ? fingerId : -1;

            LaneInputArgs args = new LaneInputArgs
            {
                laneIndex = lane,
                isPressed = pressed,
                touchPos = touchPos,
                swipeKnown = fromTouch,
                swipeY = fromTouch && laneSwipeY.TryGetValue(lane, out float swipe) ? swipe : 0f,
                musicTime = musicTime,
                fingerId = fingerId
            };
            OnLaneInput?.Invoke(this, args);
        }

        /// <summary>
        /// 根据屏幕坐标计算所属音轨：
        /// 先把屏幕坐标转换为 Canvas 本地坐标，再按 LaneLayout 判定轨道。
        /// 修复：此前按屏幕四等分判定，与 CanvasScaler 缩放/裁切后的按键视觉错位
        /// （不同视图下按红色可能触发黄色）。
        /// </summary>
        private int GetLaneFromScreenPos(Vector2 screenPos)
        {
            if (canvasForLaneMapping == null)
            {
                canvasForLaneMapping = FindObjectOfType<Canvas>();
                if (canvasForLaneMapping == null) return -1;
            }

            RectTransform canvasRect = canvasForLaneMapping.transform as RectTransform;
            if (canvasRect == null) return -1;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPos, null, out Vector2 local))
                return -1;

            return LaneLayout.GetLaneFromCanvasX(local.x);
        }

        /// <summary>
        /// 检查是否点击在UI上
        /// </summary>
        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;

            PointerEventData ped = new PointerEventData(EventSystem.current)
            {
                position = screenPos
            };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);
            return results.Count > 0;
        }

        /// <summary>
        /// 手动强制触发音轨输入（用于UI按钮测试）
        /// </summary>
        public void SimulateLaneInput(int lane, bool pressed)
        {
            int count = Mathf.Max(1, LanePressStates.Length);
            Vector2 pos = new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.3f);
            SetLaneState(lane, pressed, pos, false, lane + 1);
        }

        /// <summary>
        /// 自检用：按**指定的歌曲时间**注入一次音轨输入，模拟"引擎晚读到触摸事件"
        /// （判定必须按事件时间戳算，而不是按当前帧的 SongTime）。
        /// </summary>
        public void SimulateLaneInputAt(int lane, bool pressed, float musicTime, int fingerId = -1)
        {
            int count = Mathf.Max(1, LanePressStates.Length);
            Vector2 pos = new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.3f);
            SetLaneStateAt(lane, pressed, pos, false, musicTime, fingerId < 0 ? lane + 1 : fingerId);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
