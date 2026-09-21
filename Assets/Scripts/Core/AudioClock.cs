using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 音频时钟平滑。`AudioSource.time` 是按音频缓冲粒度更新的，直接当音乐时间用会抖动
    /// （同一缓冲内可能重复、偶尔回跳），而它又是判定与视觉的唯一时间基。
    ///
    /// 这里做两件事：
    ///   · 平时按真实时间 **1× 推进**（单调、不跳变），再以很小的速度向原始采样值靠拢，消除累积漂移；
    ///   · 原始值出现大跳变（> SeekThreshold）时判定为跳转/重开，直接重同步（不做平滑）。
    /// 关掉 `Enabled` 即回到"直接用原始采样"，用于对比与回退。
    /// </summary>
    public static class AudioClock
    {
        /// <summary>超过这个跳变视为 seek/重新开始（秒）</summary>
        public const float SeekThreshold = 0.20f;

        /// <summary>向原始值靠拢的速度上限（每秒最多纠正播放时间的比例）</summary>
        public const float MaxDriftPerSecond = 0.05f;

        /// <summary>总开关（关掉 = 直接用原始采样，可回退对比）</summary>
        public static bool Enabled = true;

        private static float smoothed;
        private static double lastRealtime;
        private static bool initialized;

        /// <summary>重新同步（换曲、从头播、或检测到 seek）</summary>
        public static void Reset(float time, double realtime)
        {
            smoothed = time;
            lastRealtime = realtime;
            initialized = true;
        }

        /// <summary>喂入一次原始采样，返回平滑后的音乐时间（单调不减）</summary>
        public static float Update(float rawTime, double realtime)
        {
            if (!Enabled) return rawTime;
            if (!initialized) { Reset(rawTime, realtime); return smoothed; }

            double dt = realtime - lastRealtime;
            if (dt < 0.0) dt = 0.0;
            if (dt > 0.5) dt = 0.5;                                   // 卡帧/挂起：不让它一次跳太多
            lastRealtime = realtime;

            // 大跳变 = 用户 seek 或音频重开：信原始值，直接重同步
            if (Mathf.Abs(rawTime - smoothed) > SeekThreshold) { Reset(rawTime, realtime); return smoothed; }

            float advanced = smoothed + (float)dt;                     // 1× 推进
            float drift = rawTime - advanced;                          // 与原始采样的偏差
            float maxCorrect = MaxDriftPerSecond * (float)dt;
            float corrected = advanced + Mathf.Clamp(drift, -maxCorrect, maxCorrect);
            smoothed = Mathf.Max(smoothed, corrected);                  // 单调不减（判定时间不能倒流）
            return smoothed;
        }
    }
}
