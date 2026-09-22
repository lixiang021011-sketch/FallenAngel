using System;
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
        /// <summary>
        /// true = 这一条是"手指仍按住、位置发生变化"（同轨内的移动），**不是按下边沿**。
        /// 为什么要单独标：判定层把 `isPressed` 一律当 `Began`（新按下）用 —— 手指在轨内滑动
        /// 如果也走 Began，flick 的位移累计会被反复清零（永远判不出甩键）；
        /// 反过来不发事件的话，判定层根本收不到"滑动"这一帧（同样判不出）。
        /// 所以这里要区分"按下边沿"与"按住中移动"，判定层据此映射成 LiveTouchPhase.Moved。
        /// </summary>
        public bool isMove;
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
        [Tooltip("判定区为屏幕底部 touchBottomRatio 比例（1 = 整屏，参照 Phigros；y=0 为屏幕底边）。" +
                 "只影响「按下能不能判到」的 Y 范围，不影响轨道 X 映射；UI 上的触摸仍被 IsPointerOverUI 挡掉")]
        [Range(0.1f, 1f)]
        public float touchBottomRatio = 1f;

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
                // fingerId 一定要打：Unity 的第一根手指是 0，而判定层历史上拿 0 当"空槽"哨兵
                // （flick 因此永远判不出来）。这行日志是真机上唯一能证明"0 号手指也在参与"的证据。
                Debug.Log(string.Format("[InputManager] 触摸按下（真机）：id={4} 屏 {0:0}×{1:0}（屏高 {2}，{3}）",
                    p.x, p.y, Screen.height, zone, t.fingerId));
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
                ProcessTouchSample(touch.fingerId, touch.position, touch.phase, TouchEventTime(touch.fingerId));
            }
        }

        /// <summary>
        /// 处理**一条**触摸样本（Began / Moved / Stationary / Ended）。
        /// 真机由 `HandleTouchInput` 每帧逐条喂进来；自检直接喂合成样本 —— 这样
        /// "触屏样本 → 音轨事件"这段（含下方的 Moved 语义）在批处理里也能逐条断言，
        /// 不必依赖真机手指（MIUI 拦掉了 adb 注入触摸）。
        /// </summary>
        internal void ProcessTouchSample(int touchId, Vector2 touchPos, TouchPhase phase, double eventTime)
        {
            if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
            {
                ReleaseTouchId(touchId, touchPos);
                return;
            }

            int lane = InHitZone(touchPos) ? LaneOf(touchPos) : -1;
            if (lane < 0 || lane >= LaneLayout.ActiveLaneCount)
            {
                // 已按住的手指滑到 UI 上 / 滑出判定区：**保持它原来的轨**，只有真正抬起才释放。
                // 以前这里直接释放 → 手指越过 HUD、或滑条跟手时稍微出界就"断触"（作者反馈
                // "断触频繁、打得小心翼翼"）。滑条跟手有独立容差（JudgeManager 的 SlideFollowTolerance），
                // 不需要在这里靠"释放"来惩罚。
                return;
            }

            switch (phase)
            {
                case TouchPhase.Began:
                    if (!touchIdToLane.ContainsKey(touchId))
                    {
                        touchIdToLane[touchId] = lane;
                        touchPrev[touchId] = touchPos;
                        fingerScreenPos[touchId] = touchPos;
                        laneSwipeY[lane] = 0f;                 // 新按下：该轨滑动量归零
                        SetLaneState(lane, true, touchPos, true, touchId, eventTime);
                    }
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    fingerScreenPos[touchId] = touchPos;
                    bool crossedLane = false;
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
                        crossedLane = true;
                        // 旧轨道仅当没有其他手指按住时才释放（支持同轨多指交替）
                        if (!IsLaneHeldByAnyTouch(oldLane))
                            SetLaneState(oldLane, false, touchPos, true, touchId, eventTime);
                    }

                    // 真的移动了（或跨了轨）**必须发事件**，且要声明 isMove：
                    //  · 不发事件 → 判定层收不到"这一帧在滑动"（flick 的位移累计靠这些帧）；
                    //  · 不声明 isMove → 判定层按"新按下(Began)"处理，跨轨移动会**重置**甩键的累计幅度，
                    //    斜着甩 / 手抖过轨就再也判不出来（作者反馈"粉键识别不了"）。
                    // 静止(Stationary)不重复发事件：判定层本来就会为"仍按住的轨"补一条持续输入。
                    if (phase == TouchPhase.Moved || crossedLane)
                    {
                        SetLaneStateAt(lane, true, touchPos, true,
                            MusicTimeOf(eventTime), touchId, isMove: true);
                    }
                    break;
            }
        }

        /// <summary>
        /// 触摸事件时刻 → 歌曲时间。拿不到事件时间戳时（未装 EnhancedTouch 的 legacy 路径、
        /// 以及自检直接喂的合成样本）回退到**本帧歌曲时间** —— 绝不能把 -1 当事件时刻喂给
        /// <see cref="InputClock.ToMusicTime"/>，那会把判定时间戳拖到 250ms 之前（时钟外推钳位）。
        /// </summary>
        private float MusicTimeOf(double eventTime)
        {
            if (eventTime >= 0.0) return InputClock.ToMusicTime(eventTime);
            return GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
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
                                    int fingerId = -1, bool isMove = false)
        {
            if (lane < 0 || lane >= LanePressStates.Length) return;
            LanePressStates[lane] = pressed;
            if (laneFinger != null && lane < laneFinger.Length)
                laneFinger[lane] = pressed ? fingerId : -1;

            LaneInputArgs args = new LaneInputArgs
            {
                laneIndex = lane,
                isPressed = pressed,
                isMove = isMove,
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
        // ── 自检接缝（2026-09-22）──────────────────────────────────────────
        // 为什么需要：批处理自检里既没有 Canvas、也没有真实触摸，而"触屏样本 → 音轨事件"
        // 偏偏是 bug 的高发段（同轨移动不发事件、跨轨移动被当成新按下都是这里出的）。
        // 把"屏幕坐标 → 轨道 / 是否在判定区"做成两个可替换探针：运行时为空（走真实实现），
        // 自检填合成实现后就能逐样本断言事件序列。空委托不影响热路径。
        internal Func<Vector2, int> LaneProbe;
        internal Func<Vector2, bool> InHitZoneProbe;

        private int LaneOf(Vector2 screenPos)
        {
            return LaneProbe != null ? LaneProbe(screenPos) : GetLaneFromScreenPos(screenPos);
        }

        private bool InHitZone(Vector2 screenPos)
        {
            return InHitZoneProbe != null
                ? InHitZoneProbe(screenPos)
                : screenPos.y <= Screen.height * touchBottomRatio && !IsPointerOverUI(screenPos);
        }

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
            // 位置基线必须与 SimulateFingerAt / SimulateFingerMoveAt / FingerPositionOf **一致**（都在 0.5 屏高）：
            // 不一致时"按下 → 移动"之间的位移会掺进一段基线落差（0.2 屏高），判定层的甩键幅度判据
            // 会被这段落差直接顶过阈值 —— 那样测出来的就不是"手指滑了多少"。
            Vector2 pos = new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.5f);
            SetLaneStateAt(lane, pressed, pos, false, musicTime, fingerId < 0 ? lane + 1 : fingerId);
        }

        /// <summary>
        /// 自检用：把某根手指的**当前屏幕位置**设为「轨道中心 + (dx,dy)」（触屏语义，方向可信）。
        /// 判定层每帧用"这次位置 − 上次位置"算滑动增量，所以连续调用时传**累计位移**即可复现
        /// "每帧滑 5px 的慢甩 / 一次滑 40px 的快甩"，也用来验甩键方向。
        /// </summary>
        public void SimulateFingerAt(int lane, float musicTime, float dx, float dy, int fingerId = -1)
        {
            int count = Mathf.Max(1, LanePressStates.Length);
            Vector2 basePos = new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.5f);
            Vector2 pos = basePos + new Vector2(dx, dy);
            int finger = fingerId < 0 ? lane + 1 : fingerId;
            fingerScreenPos[finger] = pos;
            SetLaneStateAt(lane, true, pos, true, musicTime, finger);
        }

        /// <summary>
        /// 自检用：模拟"手指**仍按住**、这一帧移动了"——与真机同轨移动走**同一条路**（isMove 事件）。
        /// 与 <see cref="SimulateFingerAt"/> 的区别：那条是"按下(Began)"，这条是"移动(Moved)"。
        /// 作者反馈的"粉键识别不了"正是这条路断掉：移动不发事件 → 判定层拿不到滑动帧。
        /// </summary>
        public void SimulateFingerMoveAt(int lane, float musicTime, float dx, float dy, int fingerId = -1)
        {
            int count = Mathf.Max(1, LanePressStates.Length);
            Vector2 basePos = new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.5f);
            Vector2 pos = basePos + new Vector2(dx, dy);
            int finger = fingerId < 0 ? lane + 1 : fingerId;
            fingerScreenPos[finger] = pos;
            touchPrev[finger] = pos;
            SetLaneStateAt(lane, true, pos, true, musicTime, finger, isMove: true);
        }

        /// <summary>
        /// 自检用：**只更新**手指的屏幕位置，不产生按下/抬起事件（模拟"手指已经按住、这一帧移动了"）。
        /// 判定层每帧会为仍按住的轨道生成持续输入（Stationary），位置取自这里——甩键的累计幅度就是这么验的。
        /// </summary>
        public void SetFingerPosition(int fingerId, Vector2 screenPos)
        {
            fingerScreenPos[fingerId] = screenPos;
        }

        /// <summary>自检用：取"轨道中心 + (dx,dy)"的合成屏幕坐标（配合 <see cref="SetFingerPosition"/>）</summary>
        public Vector2 FingerPositionOf(int lane, float dx, float dy)
        {
            int count = Mathf.Max(1, LanePressStates.Length);
            return new Vector2(Screen.width * (lane + 0.5f) / count, Screen.height * 0.5f) + new Vector2(dx, dy);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
