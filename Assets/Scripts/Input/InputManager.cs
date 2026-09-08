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
        public bool isPressed;       // true=按下, false=抬起
        public Vector2 touchPos;     // 触屏位置（屏幕坐标）
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

        // 用于跟踪触屏ID与音轨的对应关系
        private Dictionary<int, int> touchIdToLane = new Dictionary<int, int>();
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
                touchIdToLane.Clear();
#if UNITY_EDITOR
                ReleaseEditorMouseLane();
#endif
            }
        }

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
                    SetLaneState(i, actual, pos);
                }
            }
            touchIdToLane.Clear();
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
        }

        /// <summary>
        /// 编辑器鼠标抬起/失效时释放当前按下的轨道
        /// </summary>
        private void ReleaseEditorMouseLane()
        {
            if (editorMouseLane < 0) return;
            SetLaneState(editorMouseLane, false, UnityEngine.Input.mousePosition);
            editorMouseLane = -1;
        }
#endif

        /// <summary>
        /// 处理移动端触屏输入
        /// </summary>
        private void HandleTouchInput()
        {
            if (UnityEngine.Input.touchCount <= 0) return;

            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                Touch touch = UnityEngine.Input.GetTouch(i);
                Vector2 touchPos = touch.position;
                int touchId = touch.fingerId;

                // 只处理底部判定区的触摸（y=0 在屏幕底部；超过底部区域高度比例的一律忽略）
                if (touch.position.y > Screen.height * touchBottomRatio)
                    continue;

                // 跳过UI上的触摸
                if (IsPointerOverUI(touchPos))
                    continue;

                int lane = GetLaneFromScreenPos(touchPos);
                if (lane < 0 || lane >= LaneLayout.ActiveLaneCount) continue;

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        if (!touchIdToLane.ContainsKey(touchId))
                        {
                            touchIdToLane[touchId] = lane;
                            SetLaneState(lane, true, touchPos);
                        }
                        break;

                    case TouchPhase.Moved:
                        // 触摸移动时，若跨音轨则切换
                        if (touchIdToLane.TryGetValue(touchId, out int oldLane) && oldLane != lane)
                        {
                            touchIdToLane[touchId] = lane;
                            // 旧轨道仅当没有其他手指按住时才释放（支持同轨多指交替）
                            bool oldStillHeld = false;
                            foreach (var kv in touchIdToLane)
                            {
                                if (kv.Value == oldLane) { oldStillHeld = true; break; }
                            }
                            if (!oldStillHeld)
                                SetLaneState(oldLane, false, touchPos);
                            SetLaneState(lane, true, touchPos);
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (touchIdToLane.TryGetValue(touchId, out int releaseLane))
                        {
                            touchIdToLane.Remove(touchId);
                            // 仅当该轨道没有其他手指按住时才置为松开（支持同轨双指交替）
                            bool stillHeld = false;
                            foreach (var kv in touchIdToLane)
                            {
                                if (kv.Value == releaseLane) { stillHeld = true; break; }
                            }
                            if (!stillHeld)
                                SetLaneState(releaseLane, false, touchPos);
                        }
                        break;
                }
            }
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
        private void SetLaneState(int lane, bool pressed, Vector2 touchPos)
        {
            if (lane < 0 || lane >= LanePressStates.Length) return;
            LanePressStates[lane] = pressed;

            LaneInputArgs args = new LaneInputArgs
            {
                laneIndex = lane,
                isPressed = pressed,
                touchPos = touchPos
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
            SetLaneState(lane, pressed, pos);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
