using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 生命槽（前端对照清单 B4，照上游 `Sekai/LifeView.cs` 的形制）：
    ///
    ///   · **双层槽**：`realFill` 是当前生命（瞬时），`ghostFill` 是"掉血残影"（缓慢追上）——
    ///     上游就是 `lifeDefault`（实槽）+ `lifeDamage`（残影）两个 gauge；
    ///   · `rate = life / maxLife` 直接读 `PlayLife.Rate`；
    ///   · 数值文本与槽同源，颜色按档位变（高=青绿 / 中=琥珀 / 低=红）。
    ///
    /// 归零不结算（作者口径）：槽空到底就停在那，演奏继续。
    /// 订阅规范照 AGENTS：OnEnable 订阅、OnDisable 退订。
    /// </summary>
    public sealed class LifeView : MonoBehaviour
    {
        [SerializeField] private Image realFill;      // 实槽（当前生命）
        [SerializeField] private Image ghostFill;     // 残影槽（掉血后缓慢追下来）
        [SerializeField] private TextMeshProUGUI valueText;   // 数值（可空）
        [Tooltip("残影追下来的速度（每秒比例）；越大越快")]
        [SerializeField] private float ghostCatchUpPerSecond = 0.55f;

        private float realRate = 1f;
        private float ghostRate = 1f;

        private void OnEnable()
        {
            PlayLife.OnChanged -= HandleLifeChanged;
            PlayLife.OnChanged += HandleLifeChanged;
            // 面板刚打开时补一次当前值（事件只在变化时发）
            HandleLifeChanged(PlayLife.Current, PlayLife.Max);
            ghostRate = Mathf.Max(ghostRate, realRate);
        }

        private void OnDisable()
        {
            PlayLife.OnChanged -= HandleLifeChanged;
        }

        private void Update()
        {
            float rate = PlayLife.Rate;
            realRate = rate;

            if (ghostRate > realRate + 0.0005f)
            {
                // 残影只能往下追（回血时直接跟上去，不留"假血"）
                ghostRate = Mathf.Max(realRate, ghostRate - ghostCatchUpPerSecond * Time.deltaTime);
            }
            else
            {
                ghostRate = realRate;
            }

            Apply(realFill, realRate);
            Apply(ghostFill, ghostRate);
            if (valueText != null)
            {
                valueText.text = Mathf.RoundToInt(PlayLife.Current).ToString();
                valueText.color = ColorForRate(realRate);
            }
        }

        private void HandleLifeChanged(float current, float max)
        {
            realRate = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        }

        private static void Apply(Image image, float rate)
        {
            if (image == null) return;
            image.fillAmount = Mathf.Clamp01(rate);
        }

        /// <summary>槽色档位：高=青绿、中=琥珀、低=红（掉血要一眼看出来）</summary>
        private static Color ColorForRate(float rate)
        {
            if (rate <= 0.25f) return new Color(1f, 0.42f, 0.42f);
            if (rate <= 0.6f) return new Color(1f, 0.82f, 0.38f);
            return new Color(0.45f, 1f, 0.65f);
        }

        /// <summary>给 SceneBuilder 之外的调用方一个刷新入口（例如复活/道具回血后立刻刷新）</summary>
        public void RefreshNow()
        {
            realRate = PlayLife.Rate;
            ghostRate = realRate;
            Apply(realFill, realRate);
            Apply(ghostFill, ghostRate);
        }
    }
}
