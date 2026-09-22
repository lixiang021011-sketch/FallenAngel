using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// Combo 数字位 + AP（全 Perfect）特效（前端对照清单 B3）。**常量逐条抄自上游**
    /// `Sekai/ComboView.cs`：
    ///
    /// | 上游 | 我们 |
    /// | --- | --- |
    /// | `x = (digitCount - 1) * 0.475f + i * -0.95f`（i 从个位起 → 数字从右往左排） | `DigitX(i, count)` |
    /// | 出现：`appear = clamp01(time / 0.1f)`，`scale = 0.6f + appear * 0.4f` | `AppearScale(t)` |
    /// | 爆发：`burst = clamp01((time - 0.1f) / 0.1f)`，`effectScale = 1 + burst * 0.4f`，`alpha = (1 - burst) * 0.5f` | `BurstScale/BurstAlpha` |
    /// | AP 呼吸：`alpha = (sin(Time.time * 4.1887903f) + 1f) * 0.5f` | `ApAlpha(t)` |
    /// | 同连击数不重播动画（`if (currentCombo == combo) return;`） | `SetCombo` 里的早退 |
    ///
    /// 差异（写明理由）：上游用 7 段数字 sprite + AP 专用 sprite 环；我们没有美术资源，
    /// 用 **TMP 数字 + 程序化圆角底板**，AP 的"外圈"用一块会呼吸的圆角光板等价
    /// （效果等价、配色一致；等美术资源到位只需换图，不改逻辑）。
    /// </summary>
    public sealed class ComboView : MonoBehaviour
    {
        /// <summary>AP 呼吸角速度（上游 `AP_EFFECT_SPEED`）</summary>
        public const float ApEffectSpeed = 4.1887903f;

        /// <summary>数字间距（上游 0.95 = 一个数字宽）</summary>
        public const float DigitGap = 0.95f;

        /// <summary>居中偏移系数（上游 0.475）</summary>
        public const float DigitCenterOffset = 0.475f;

        /// <summary>出现时长（秒）</summary>
        public const float AppearSeconds = 0.1f;

        /// <summary>爆发时长（秒）</summary>
        public const float BurstSeconds = 0.1f;

        [Header("引用（SceneBuilder 注入）")]
        [SerializeField] private RectTransform numberRoot;        // 数字位容器（整体缩放）
        [SerializeField] private RectTransform effectRoot;        // 爆发层（放大淡出）
        [SerializeField] private RectTransform apRoot;            // AP 呼吸框（等价上游 AP sprite 环）
        [SerializeField] private Image[] apParts;                 // 框的四条边（一起呼吸）
        [SerializeField] private TextMeshProUGUI[] digitTexts;    // 7 个数字位（低位在前）
        [SerializeField] private Image[] digitPlates;            // 数字底板（染色用）
        [SerializeField] private RectTransform[] effectPlates;   // 爆发层（与数字位同布局，放大淡出）
        [SerializeField] private TextMeshProUGUI comboLabel;     // "COMBO" 字样
        [SerializeField] private float digitWidth = 96f;          // 一个数字的宽（像素）

        [Header("配色")]
        [SerializeField] private Color normalColor = new Color(0.89f, 0.96f, 0.95f);
        [SerializeField] private Color normalPlate = new Color(0.12f, 0.24f, 0.26f);
        [SerializeField] private Color apColor = new Color(1f, 0.55f, 0.78f);
        [SerializeField] private Color apPlate = new Color(0.27f, 0.12f, 0.22f);

        private int currentCombo = -1;
        private bool allPerfect;
        private float time;

        // ── 纯函数区（自检直接调；与上游公式一一对应）────────────────────────

        /// <summary>位数（上游 `Mathf.FloorToInt(Mathf.Log10(combo)) + 1`，combo ≤ 0 时按 1 位）</summary>
        public static int DigitCount(int combo)
        {
            return combo > 0 ? Mathf.FloorToInt(Mathf.Log10(combo)) + 1 : 1;
        }

        /// <summary>第 i 位（i 从个位起）相对 combo 中心的横向偏移，单位 = 数字宽（上游公式）</summary>
        public static float DigitX(int index, int digitCount)
        {
            return (digitCount - 1) * DigitCenterOffset + index * -DigitGap;
        }

        /// <summary>出现动画的缩放（上游 `numberRoot.localScale`）</summary>
        public static float AppearScale(float t)
        {
            return 0.6f + Mathf.Clamp01(t / AppearSeconds) * 0.4f;
        }

        /// <summary>爆发层缩放（上游 `effectNumberRoot.localScale`；0.1s 之前不显示 = 0）</summary>
        public static float BurstScale(float t)
        {
            if (t < AppearSeconds) return 0f;
            return 1f + Mathf.Clamp01((t - AppearSeconds) / BurstSeconds) * 0.4f;
        }

        /// <summary>爆发层透明度（上游 0.5 → 0）</summary>
        public static float BurstAlpha(float t)
        {
            if (t < AppearSeconds) return 0f;
            return (1f - Mathf.Clamp01((t - AppearSeconds) / BurstSeconds)) * 0.5f;
        }

        /// <summary>AP 呼吸透明度（上游公式，角速度 4.1887903 rad/s）</summary>
        public static float ApAlpha(float t)
        {
            return (Mathf.Sin(t * ApEffectSpeed) + 1f) * 0.5f;
        }

        /// <summary>
        /// AP（全 Perfect）判定：至少要判过一次，且**没有** Great/Good/Bad/Miss。
        /// 对应上游 `LiveScore.IsAllPerfect`（我们有分档计数，所以自己算）。
        /// </summary>
        public static bool IsAllPerfectScore(int perfect, int great, int good, int bad, int miss)
        {
            return perfect > 0 && great == 0 && good == 0 && bad == 0 && miss == 0;
        }

        // ── 运行时 ─────────────────────────────────────────────────────────

        public void SetCombo(int combo, bool isAllPerfect)
        {
            if (currentCombo == combo && allPerfect == isAllPerfect) return;   // 上游：同连击不重播

            currentCombo = combo;
            allPerfect = isAllPerfect;
            time = 0f;

            if (numberRoot != null)
                numberRoot.localScale = combo > 0 ? Vector3.one : Vector3.zero;

            int count = DigitCount(combo);
            int value = Mathf.Max(0, combo);

            Color textColor = allPerfect ? apColor : normalColor;
            Color plateColor = allPerfect ? apPlate : normalPlate;

            if (digitTexts != null)
            {
                for (int i = 0; i < digitTexts.Length; i++)
                {
                    TextMeshProUGUI digit = digitTexts[i];
                    if (digit == null) continue;

                    bool visible = combo > 0 && i < count;
                    digit.gameObject.SetActive(visible);
                    if (!visible) { value /= 10; continue; }

                    digit.text = (value % 10).ToString();
                    digit.color = textColor;
                    RectTransform rt = (RectTransform)digit.transform;
                    // 数字位容器是居中布局：位置由"上游公式 × 数字宽"给出（i 从个位起 → 负数在左）
                    rt.anchoredPosition = new Vector2(DigitX(i, count) * digitWidth,
                        rt.anchoredPosition.y);
                    value /= 10;
                }
            }

            if (digitPlates != null)
            {
                for (int i = 0; i < digitPlates.Length; i++)
                {
                    if (digitPlates[i] == null) continue;
                    digitPlates[i].color = plateColor;
                }
            }

            if (effectPlates != null)
            {
                for (int i = 0; i < effectPlates.Length; i++)
                {
                    RectTransform plate = effectPlates[i];
                    if (plate == null) continue;
                    bool visible = combo > 0 && i < count;
                    plate.gameObject.SetActive(visible);
                    if (visible)
                    {
                        plate.anchoredPosition = new Vector2(DigitX(i, count) * digitWidth,
                            plate.anchoredPosition.y);
                    }
                }
            }

            if (comboLabel != null) comboLabel.color = allPerfect ? apColor : normalColor;
            if (effectRoot != null) effectRoot.gameObject.SetActive(combo > 0);
            if (apRoot != null) apRoot.gameObject.SetActive(allPerfect && combo > 0);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // 开发版自报布局：真机上直接核对"位数 / 各数字位偏移 / AP 标记"（像素取证太费劲）
            if (combo > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(DigitX(i, count).ToString("0.00"));
                }
                Debug.Log(string.Format("[ComboView] combo={0} 位数={1} AP={2} 数字位偏移(数字宽)={3} → 像素={4}",
                    combo, count, allPerfect, sb,
                    (DigitX(0, count) * digitWidth).ToString("0")));
            }
#endif
        }

        public void Clear()
        {
            SetCombo(0, false);
            currentCombo = -1;
        }

        private void Update()
        {
            if (numberRoot == null) return;
            if (currentCombo <= 0) return;

            time += Time.deltaTime;

            // 出现：0.1 秒内 0.6 → 1.0 倍
            numberRoot.localScale = Vector3.one * AppearScale(time);

            // 爆发层：0.1~0.2 秒放大到 1.4 倍并淡出
            if (effectRoot != null)
            {
                float burstScale = BurstScale(time);
                effectRoot.localScale = Vector3.one * burstScale;
                Image[] plates = effectRoot.GetComponentsInChildren<Image>(true);
                float alpha = BurstAlpha(time);
                for (int i = 0; i < plates.Length; i++)
                {
                    Color c = plates[i].color;
                    c.a = alpha;
                    plates[i].color = c;
                }
            }

            // AP：正弦呼吸（上游 4.1887903 rad/s）
            if (allPerfect && apParts != null)
            {
                float a = 0.25f + ApAlpha(time) * 0.55f;      // 呼吸：0.25 ~ 0.80
                for (int i = 0; i < apParts.Length; i++)
                {
                    if (apParts[i] == null) continue;
                    Color c = apColor;
                    c.a = a;
                    apParts[i].color = c;
                }
            }
        }
    }
}
