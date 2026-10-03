using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 声部视觉参数（2026-10-03 作者过稿：**全色相 + G2 半场**；当前是测试用占位，
    /// 未来接商用素材）。
    ///
    /// 这是"换素材"的唯一入口，形状照 <see cref="FallenAngel.Data.NoteVisualConfig"/> 的思路：
    /// 色相、染色强度、动画时长都从这里读，绘制代码不写死数值。
    /// 将来换成商业素材时：把 <see cref="TryGetColor"/> 换成"读表 → 拿 Sprite/材质"，
    /// 调用方（PartTintView）不用改。
    ///
    /// 色相为什么这么排：上一稿琥珀(34°)与珊瑚(9°)只差 25°，而鼓↔吉是原型里切得最多的一对，
    /// 切过去背景几乎不变。现在按色相环排，**最小间隔 45°（鼓↔吉）**，其余 ≥60°。
    /// </summary>
    public static class PartPalette
    {
        /// <summary>五件乐器的色相（度）。顺序即色相环顺序，便于核对间隔。</summary>
        public const float HueDrums = 0f;     // 朱红（心跳）
        public const float HueGuitar = 45f;   // 琥珀（主音）
        public const float HueKeys = 105f;    // 黄绿（和声）
        public const float HueBass = 215f;    // 靛蓝（低音）
        public const float HueVocal = 295f;   // 紫（人声）
        public const float Saturation = 0.72f;
        public const float Value = 0.90f;

        // ---- G2「半场」：背景上半渐变到该色相，判定线以下留反射 ----
        /// <summary>染色最深处的混合量（0.34 ≈ 过稿时的采样 #433D21）</summary>
        public const float TintPeak = 0.34f;
        /// <summary>渐变覆盖到屏幕高度的比例，再往下回中性</summary>
        public const float TintSpan = 0.55f;
        /// <summary>判定线以下的反射量</summary>
        public const float BottomReflection = 0.12f;

        // ---- 切换演出（S1 换色 + S2 徽记）----
        /// <summary>全场换色时长：0.35s 内从旧色相淡到新色相</summary>
        public const float TintFadeSeconds = 0.35f;
        /// <summary>徽记（中央声部名）淡出时长</summary>
        public const float BadgeSeconds = 0.6f;
        /// <summary>判定线闪白时长</summary>
        public const float JudgeFlashSeconds = 0.3f;

        /// <summary>已知声部（自检与面板用；不在表里的声部不染色）</summary>
        public static readonly string[] KnownParts = { "Drums", "Guitar", "Keys", "Bass", "Vocal" };

        /// <summary>声部 → 色相（度）。未知声部返回 -1，调用方跳过染色。</summary>
        public static float HueOf(string partName)
        {
            switch (partName)
            {
                case "Drums": return HueDrums;
                case "Guitar": return HueGuitar;
                case "Keys": return HueKeys;
                case "Bass": return HueBass;
                case "Vocal": return HueVocal;
                default: return -1f;
            }
        }

        public static bool TryGetColor(string partName, out Color color)
        {
            float hue = HueOf(partName);
            if (hue < 0f)
            {
                color = Color.clear;
                return false;
            }
            color = Color.HSVToRGB(hue / 360f, Saturation, Value);
            return true;
        }

        /// <summary>色相环上的最短夹角（度）。自检用它卡"两个声部至少差 45°"。</summary>
        public static float HueGap(float hueA, float hueB)
        {
            return Mathf.Abs(Mathf.DeltaAngle(hueA, hueB));
        }
    }
}
