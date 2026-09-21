using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>
    /// 校准测量的数学部分（纯函数，便于自检）。
    ///
    /// 为什么单独抽出来：校准的"人味"在交互上，但**算错**必须在自检里能抓住——
    /// 玩家跟拍点 8 次，只要剔极值求平均这一句写错，结果就是随机的偏移量。
    ///
    /// 口径：每个样本 = 点击时刻 − 该拍的时刻（秒，正 = 点得晚）。
    /// 正偏移表示"玩家听到的声音比画面晚"，所以要把偏移量**加到 SongTime 的扣减项**上
    /// （与 CalibrationSettings / AudioLatency 同一个方向：正 = 音符整体推迟）。
    /// </summary>
    public static class CalibrationMath
    {
        /// <summary>校准值的允许范围（毫秒）</summary>
        public const int MinMs = -200;

        public const int MaxMs = 200;

        /// <summary>极值剔除比例（两端各剔 25%）</summary>
        public const float TrimRatio = 0.25f;

        /// <summary>
        /// 修剪平均：样本不足 4 个就不剔（太少的样本剔了就只剩两三个），
        /// 否则两端各剔 <see cref="TrimRatio"/>，再取平均并换算成毫秒。
        /// </summary>
        public static int TrimmedMeanMs(IReadOnlyList<double> offsetsSeconds)
        {
            if (offsetsSeconds == null || offsetsSeconds.Count == 0) return 0;

            var sorted = new List<double>(offsetsSeconds);
            sorted.Sort();

            int trim = sorted.Count >= 4 ? (int)(sorted.Count * TrimRatio) : 0;
            int from = trim;
            int to = sorted.Count - trim;           // [from, to)
            if (to <= from) { from = 0; to = sorted.Count; }

            double sum = 0.0;
            for (int i = from; i < to; i++) sum += sorted[i];
            double mean = sum / (to - from);

            return ClampMs((int)System.Math.Round(mean * 1000.0));
        }

        /// <summary>夹到允许范围（防止误点导致 ±几秒的离谱值）</summary>
        public static int ClampMs(int ms)
        {
            if (ms < MinMs) return MinMs;
            if (ms > MaxMs) return MaxMs;
            return ms;
        }

        /// <summary>把点击偏差归到最近的一拍（秒）；超出半拍视为没跟上，返回 false</summary>
        public static bool TryAssignToBeat(double tapTime, double beatTime, double beatInterval,
            out double offsetSeconds)
        {
            offsetSeconds = tapTime - beatTime;
            double half = System.Math.Abs(beatInterval) * 0.5;
            return System.Math.Abs(offsetSeconds) <= half;
        }
    }
}
