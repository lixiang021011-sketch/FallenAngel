using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;
using FallenAngel.InputSystem;

namespace FallenAngel.UI
{
    /// <summary>
    /// 单音轨按键视觉反馈控制器
    /// 按下时：按键区域变亮、缩放、判定线闪光
    /// 松开时：还原
    /// </summary>
    public class LaneKeyVisual : MonoBehaviour
    {
        [Header("音轨设置")]
        [SerializeField] private int laneIndex;

        [Header("引用")]
        [SerializeField] private Image keyAreaImage;        // 按键区域背景
        [SerializeField] private Image judgeLineGlow;      // 判定线发光效果
        [SerializeField] private RectTransform keyVisual;  // 按键图标RectTransform

        [Header("按下效果设置")]
        [SerializeField] private Color pressedColor = Color.white;
        [SerializeField] private float pressedColorStrength = 0.5f;
        [SerializeField] private float pressedScale = 0.92f;
        [SerializeField] private float glowMaxAlpha = 0.8f;
        [SerializeField] private float transitionSpeed = 15f;

        private Color normalColor;
        private Vector3 normalScale;
        private float targetColorLerp;
        private float targetGlowAlpha;

        private void Awake()
        {
            if (keyAreaImage != null) normalColor = keyAreaImage.color;
            if (keyVisual != null) normalScale = keyVisual.localScale;
        }

        private void OnEnable()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnLaneInput += HandleLaneInput;
        }

        private void Start()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
                InputManager.Instance.OnLaneInput += HandleLaneInput;
            }
        }

        private void OnDisable()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
        }

        private void HandleLaneInput(object sender, LaneInputArgs e)
        {
            if (e.laneIndex != laneIndex) return;

            if (e.isPressed)
            {
                targetColorLerp = 1f;
                targetGlowAlpha = glowMaxAlpha;
            }
            else
            {
                targetColorLerp = 0f;
                targetGlowAlpha = 0f;
            }
        }

        private void Update()
        {
            // 基准色与轨道配色单一来源（LaneColors）对齐，每帧刷新：
            // 轨道换板（4K/5K 切换，LanePanelController 重着色）后基准色即更新。
            // 修复：此前 Awake 一次性捕获构建期颜色——场景构建时按默认 4 键用鼓谱色板，
            // 5K 谱加载后轨道重刷为吉他色板，按键反馈基准色仍是旧板，按压闪光颜色错乱。
            if (keyAreaImage != null)
            {
                Color laneColor = LaneColors.GetLaneColor(laneIndex);
                laneColor.a = 0.3f; // 与 LanePanelController 的按键区透明度一致
                normalColor = laneColor;
            }

            float curLerp = 0f;
            if (keyAreaImage != null) curLerp = GetCurrentLerp();
            float lerp = Mathf.MoveTowards(curLerp, targetColorLerp, transitionSpeed * Time.unscaledDeltaTime);

            // 颜色插值
            if (keyAreaImage != null)
            {
                keyAreaImage.color = Color.Lerp(normalColor, pressedColor, lerp * pressedColorStrength);
            }

            // 缩放
            if (keyVisual != null)
            {
                float s = Mathf.Lerp(1f, pressedScale, lerp);
                keyVisual.localScale = normalScale * s;
            }

            // 发光（颜色通道同样取自 LaneColors；仅动画透明度）
            if (judgeLineGlow != null)
            {
                Color laneColor = LaneColors.GetLaneColor(laneIndex);
                Color c = judgeLineGlow.color;
                c.r = laneColor.r; c.g = laneColor.g; c.b = laneColor.b;
                c.a = Mathf.MoveTowards(c.a, targetGlowAlpha, transitionSpeed * Time.unscaledDeltaTime);
                judgeLineGlow.color = c;
            }
        }

        private float GetCurrentLerp()
        {
            if (normalColor == pressedColor) return 0f;
            float totalR = pressedColor.r - normalColor.r;
            float totalG = pressedColor.g - normalColor.g;
            float totalB = pressedColor.b - normalColor.b;
            float denom = totalR + totalG + totalB;
            if (Mathf.Abs(denom) < 0.001f) return 0f;

            Color cur = keyAreaImage.color;
            float curR = cur.r - normalColor.r;
            float curG = cur.g - normalColor.g;
            float curB = cur.b - normalColor.b;
            float numer = curR + curG + curB;
            if (Mathf.Abs(pressedColorStrength) < 0.001f) return 0f;
            return Mathf.Clamp01(numer / (denom * pressedColorStrength));
        }
    }
}
