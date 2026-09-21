using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 校准「跟拍测量」的纯计算部分（可自检）。
    /// 玩家界面（UI/CalibrationController）负责节拍与点击，这里只算数：
    ///   · 每个样本 = 点击时刻 − 该拍时刻（秒，正 = 点得晚）；
    ///   · 剔除两端 25% 后取平均（点歪的那两下不该把结果带跑）；
    ///   · 半拍之外的点击不计（没跟上节拍）；
    ///   · 结果换算成**上游的帧口径**并夹到 ±20 帧（`LiveConfig.MinNoteTiming/MaxNoteTiming`）。
    /// 符号与上游一致：算出来的偏移直接写进 `CalibrationSettings.TimingAdjustData`，**正数 = 音符提前**。
    /// </summary>
    public static class CalibrationMeasure
    {
        /// <summary>极值剔除比例（两端各 25%）</summary>
        public const float TrimRatio = 0.25f;

        /// <summary>测量结果的建议值（**帧**，已夹到 ±20）</summary>
        public static float SuggestedFrames(IReadOnlyList<double> offsetsSeconds)
        {
            return CalibrationSettings.Clamp(SecondsToFrames(TrimmedMeanSeconds(offsetsSeconds)));
        }

        /// <summary>帧 ↔ 秒（与上游 `* 0.016667f` 同一口径）</summary>
        public static float SecondsToFrames(double seconds)
        {
            return (float)(seconds * 60.0);
        }

        public static double FramesToSeconds(float frames)
        {
            return frames / 60.0;
        }

        /// <summary>修剪平均（秒）：样本不足 4 个不剔</summary>
        public static double TrimmedMeanSeconds(IReadOnlyList<double> offsetsSeconds)
        {
            if (offsetsSeconds == null || offsetsSeconds.Count == 0) return 0.0;

            var sorted = new List<double>(offsetsSeconds);
            sorted.Sort();

            int trim = sorted.Count >= 4 ? (int)(sorted.Count * TrimRatio) : 0;
            int from = trim;
            int to = sorted.Count - trim;
            if (to <= from) { from = 0; to = sorted.Count; }

            double sum = 0.0;
            for (int i = from; i < to; i++) sum += sorted[i];
            return sum / (to - from);
        }

        /// <summary>把点击归到最近的一拍；超过半拍视为没跟上（返回 false）</summary>
        public static bool TryAssignToBeat(double tapTime, double beatTime, double beatInterval,
            out double offsetSeconds)
        {
            offsetSeconds = tapTime - beatTime;
            return System.Math.Abs(offsetSeconds) <= System.Math.Abs(beatInterval) * 0.5;
        }

        /// <summary>毫秒 ↔ 帧（界面用 ms 显示/微调，存储用上游的帧）</summary>
        public static int FramesToMs(float frames)
        {
            return Mathf.RoundToInt(frames * (1000f / 60f));
        }

        public static float MsToFrames(float ms)
        {
            return ms * (60f / 1000f);
        }
    }
}
