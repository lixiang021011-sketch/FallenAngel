using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FallenAngel.InputSystem
{
    /// <summary>
    /// 输入事件数据
    /// </summary>
    public struct LaneInputArgs
    {
        public int laneIndex;        // 音轨索引 0-3
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

        [Header("音轨输入区域配置（按屏幕宽度比例 0-1）")]
        [Tooltip("4个音轨的屏幕X轴划分，每段为一个 [左, 右) 区间")]
        [SerializeField] private float[] laneXRatios = new float[] { 0f, 0.25f, 0.5f, 0.75f, 1.0f };

        [Header("键盘映射（PC调试用）")]
        [SerializeField] private KeyCode[] laneKeys = new KeyCode[]
        {
            KeyCode.D,      // 音轨0
            KeyCode.F,      // 音轨1
            KeyCode.J,      // 音轨2
            KeyCode.K        // 音轨3
        };

        [Header("触屏判定区域底部高度（屏幕高度比例）")]
        [Tooltip("触屏时，只有屏幕下半部的触摸才会判定为音轨输入")]
        [Range(0.1f, 0.9f)]
        public float touchBottomRatio = 0.6f;

        /// <summary>
        /// 音轨输入事件：按下/抬起时触发
        /// 参数: laneIndex, isPressed
        /// </summary>
        public event System.EventHandler<LaneInputArgs> OnLaneInput;

        /// <summary>
        /// 获取当前各音轨是否处于按下状态
        /// </summary>
        public bool[] LanePressStates { get; private set; } = new bool[4];

        // 用于跟踪触屏ID与音轨的对应关系
        private Dictionary<int, int> touchIdToLane = new Dictionary<int, int>();
        // 上一帧的键盘按下状态
        private bool[] lastKeyStates = new bool[4];

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (FallenAngel.Core.GameManager.Instance != null &&
                FallenAngel.Core.GameManager.Instance.CurrentState != FallenAngel.Core.GameState.Playing)
                return;

            HandleTouchInput();
            HandleKeyboardInput();
        }

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

                // 只处理屏幕下半部的触摸
                if (touch.position.y < Screen.height * (1f - touchBottomRatio))
                    continue;

                // 跳过UI上的触摸
                if (IsPointerOverUI(touchPos))
                    continue;

                int lane = GetLaneFromX(touchPos.x);
                if (lane < 0 || lane > 3) continue;

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
                            SetLaneState(oldLane, false, touchPos);
                            touchIdToLane[touchId] = lane;
                            SetLaneState(lane, true, touchPos);
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (touchIdToLane.TryGetValue(touchId, out int releaseLane))
                        {
                            SetLaneState(releaseLane, false, touchPos);
                            touchIdToLane.Remove(touchId);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 处理PC端键盘输入
        /// </summary>
        private void HandleKeyboardInput()
        {
            for (int i = 0; i < 4; i++)
            {
                bool isKeyDown = UnityEngine.Input.GetKey(laneKeys[i]);
                bool wasDown = lastKeyStates[i];

                if (isKeyDown != wasDown)
                {
                    if (isKeyDown)
                        Debug.Log($"[InputManager] Key DOWN lane={i} key={laneKeys[i]}");
                    Vector2 pos = new Vector2(Screen.width * (i + 0.5f) / 4f, Screen.height * 0.3f);
                    SetLaneState(i, isKeyDown, pos);
                }
                lastKeyStates[i] = isKeyDown;
            }
            // SPACE/ESC 由 PauseController 处理
        }

        /// <summary>
        /// 改变音轨按下状态并触发事件
        /// </summary>
        private void SetLaneState(int lane, bool pressed, Vector2 touchPos)
        {
            if (lane < 0 || lane > 3) return;
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
        /// 根据屏幕X坐标计算所属音轨
        /// </summary>
        private int GetLaneFromX(float screenX)
        {
            float xRatio = Mathf.Clamp01(screenX / Screen.width);
            for (int i = 0; i < 4; i++)
            {
                if (xRatio >= laneXRatios[i] && xRatio < laneXRatios[i + 1])
                    return i;
            }
            // 边界值归属于最后一个音轨
            if (xRatio >= laneXRatios[4]) return 3;
            return -1;
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
            Vector2 pos = new Vector2(Screen.width * (lane + 0.5f) / 4f, Screen.height * 0.3f);
            SetLaneState(lane, pressed, pos);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
