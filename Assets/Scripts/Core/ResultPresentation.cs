using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 结算页的**演出分级**（上游 `Sekai/Core/Live/LiveResultAnimationType.cs` 的六个档）
    /// 与"逐项入场 / 分数滚动"的纯计算部分（前端清单 E2）。
    ///
    /// 上游：`None / Failure / LifeZero / Clear / FullCombo / AllPerfect` —— 六种结算演出，
    /// `LiveResultView.Execute(type)` 按档播不同动画。我们照这个分级，但演出本身按作者定案
    /// **不抄特效**，用程序化的"分数滚动 + 逐项淡入"等价。
    ///
    /// 评价（S/A/B/C/D）阈值是**我们自己的**：上游阈值在 master data 里（未移植），
    /// 这里用通行分档并在 `FlowChecks` 里钉住，避免以后悄悄改。
    /// </summary>
    public enum ResultAnimationType
    {
        None = 0,
        Failure = 1,
        LifeZero = 2,
        Clear = 3,
        FullCombo = 4,
        AllPerfect = 5,
    }

    public static class ResultPresentation
    {
        /// <summary>分数滚动时长（秒）</summary>
        public const float ScoreRollSeconds = 1.1f;
        /// <summary>每一项入场间隔（秒）</summary>
        public const float RevealStagger = 0.15f;
        /// <summary>单项淡入时长（秒）</summary>
        public const float RevealFade = 0.25f;

        /// <summary>
        /// 结算演出分级（优先报"更特别"的档）：全 Perfect &gt; 全连 &gt; 生命归零 &gt; 普通通关。
        /// 生命归零在我们的口径里**不结算失败**（作者定案：练习模式），所以它只是一个演出档，不影响成绩。
        /// </summary>
        public static ResultAnimationType Classify(bool isAllPerfect, bool isFullCombo, bool lifeEmpty, bool failed)
        {
            if (failed) return ResultAnimationType.Failure;
            if (isAllPerfect) return ResultAnimationType.AllPerfect;
            if (isFullCombo) return ResultAnimationType.FullCombo;
            if (lifeEmpty) return ResultAnimationType.LifeZero;
            return ResultAnimationType.Clear;
        }

        /// <summary>评价（S/A/B/C/D）。阈值是我们自己的（上游在 master data 里）。</summary>
        public static string RankOf(float accuracyPercent)
        {
            if (accuracyPercent >= 98f) return "S";
            if (accuracyPercent >= 95f) return "A";
            if (accuracyPercent >= 90f) return "B";
            if (accuracyPercent >= 80f) return "C";
            return "D";
        }

        /// <summary>分数滚动：t ∈ [0, duration] 内从 0 涨到 final，三次缓出（前快后慢）</summary>
        public static int RollScore(int finalScore, float t)
        {
            if (finalScore <= 0) return 0;
            float p = Mathf.Clamp01(t / ScoreRollSeconds);
            float e = 1f - Mathf.Pow(1f - p, 3f);
            return Mathf.RoundToInt(finalScore * e);
        }

        /// <summary>第 index 项在 t 时刻的透明度（按 RevealStagger 依次亮起）</summary>
        public static float RevealAlpha(int index, float t)
        {
            float start = index * RevealStagger;
            if (t <= start) return 0f;
            return Mathf.Clamp01((t - start) / RevealFade);
        }

        /// <summary>全部项亮完所需时间（秒）</summary>
        public static float TotalSeconds(int elementCount)
        {
            if (elementCount <= 0) return ScoreRollSeconds;
            return Mathf.Max(ScoreRollSeconds, (elementCount - 1) * RevealStagger + RevealFade);
        }
    }
}
