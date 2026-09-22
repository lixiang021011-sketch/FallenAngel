using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// Fever 的屏幕级表现（前端对照清单 C4）。
    ///
    /// 上游是 `ScreenEffectView` + `FeverBegin/Start/End` 事件 + `LiveConfig.FeverBundleName`
    /// （`effect_asset/live/fever/0`）的整屏特效；我们不用粒子包，用**程序化等价**：
    ///   · `overlay`：整屏薄色层 —— Begin 淡入并呼吸，Active 更亮脉动更快，End 淡出；
    ///   · `label`：顶部 "FEVER" 字样，Begin 闪烁、Active 常亮、Idle 隐藏；
    ///   · `chargeFill`：蓄力条（0..1，留给道具/技能攒；谱面事件直接触发时不走它）。
    ///
    /// 订阅按 AGENTS 规范：OnEnable 订阅、OnDisable 退订。
    /// </summary>
    public sealed class FeverView : MonoBehaviour
    {
        [SerializeField] private Image overlay;          // 整屏色层
        [SerializeField] private Image chargeFill;       // 蓄力条（可空）
        [SerializeField] private TextMeshProUGUI label;  // FEVER 字样（可空）
        [SerializeField] private Color overlayColor = new Color(1f, 0.45f, 0.75f, 0.10f);

        private FeverPhase phase = FeverPhase.Idle;
        private float timer;

        private void OnEnable()
        {
            Fever.OnPhaseChanged -= HandlePhase;
            Fever.OnPhaseChanged += HandlePhase;
            HandlePhase(Fever.Phase);
        }

        private void OnDisable()
        {
            Fever.OnPhaseChanged -= HandlePhase;
        }

        private void Update()
        {
            timer += Time.deltaTime;

            if (overlay != null)
            {
                float target;
                float speed;
                switch (phase)
                {
                    case FeverPhase.Active:
                        target = overlayColor.a * 1.6f;
                        speed = 8f;      // fever 中脉动更快
                        break;
                    case FeverPhase.Begin:
                        target = overlayColor.a * 0.7f;
                        speed = 4f;
                        break;
                    default:
                        target = 0f;
                        speed = 2f;
                        break;
                }

                float pulse = target > 0f ? target * (0.75f + 0.25f * Mathf.Sin(timer * speed)) : 0f;
                Color c = overlayColor;
                c.a = Mathf.MoveTowards(overlay.color.a, pulse, Time.deltaTime * 1.5f);
                overlay.color = c;
                overlay.enabled = overlay.color.a > 0.002f;
            }

            if (label != null)
            {
                bool show = phase != FeverPhase.Idle;
                label.enabled = show;
                if (show)
                {
                    // Begin 阶段闪烁等 start（上游 fever.begin → start 之间就是蓄势演出）
                    float blink = phase == FeverPhase.Begin
                        ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(timer * 6f))
                        : 1f;
                    Color lc = label.color;
                    lc.a = blink;
                    label.color = lc;
                }
            }

            if (chargeFill != null) chargeFill.fillAmount = Fever.Charge;
        }

        private void HandlePhase(FeverPhase next)
        {
            phase = next;
        }
    }
}
