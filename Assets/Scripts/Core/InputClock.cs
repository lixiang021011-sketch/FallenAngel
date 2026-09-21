using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 输入事件时刻 → 歌曲时间的换算（PJSK 的"按事件时间戳判定"里最关键的一步）。
    ///
    /// 为什么要它：触摸事件带的是**事件发生时刻**，而引擎常常要到同帧稍后才读到它。
    /// 直接拿"读到它时的 SongTime"去判，等于把这段处理延迟算到玩家头上——
    /// 60fps 下就是一帧 16.7ms，30fps 下 33ms，正好是判定档位之间的差距。
    ///
    /// 做法：GameManager 每次重算 SongTime 时记下"当时的输入时钟读数"，
    /// 之后任何输入事件按 (事件时刻 − 采样时刻) 线性外推。音频与虚拟钟都是 1× 速率，
    /// 所以线性外推就是精确的；差值钳到 ±MaxDelta，万一时钟不同源也不会算出离谱值。
    /// </summary>
    public static class InputClock
    {
        /// <summary>允许的最大外推量（秒）：同帧内的事件换算远小于它，超出说明时钟不同源</summary>
        public const float MaxDelta = 0.25f;

        private static float songTimeAtSample;
        private static double clockAtSample;
        private static bool sampled;

        /// <summary>由 GameManager 在采样 SongTime 后调用（每帧一次）</summary>
        public static void Sample(float songTime)
        {
            // 触摸时间戳用哪套时钟，由 InputManager 决定（接 EnhancedTouch 后同源）；
            // 这里先用 Unity 实时钟，尚未采样时 ToMusicTime 直接回退。
            Sample(songTime, Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>自检/测试用：显式给时钟读数，避免依赖真实时间</summary>
        public static void Sample(float songTime, double clockNow)
        {
            songTimeAtSample = songTime;
            clockAtSample = clockNow;
            sampled = true;
        }

        /// <summary>输入事件时刻（与 Sample 的时钟同源）→ 歌曲时间</summary>
        public static float ToMusicTime(double eventTime)
        {
            if (!sampled) return songTimeAtSample;

            double delta = eventTime - clockAtSample;
            if (delta > MaxDelta) delta = MaxDelta;
            else if (delta < -MaxDelta) delta = -MaxDelta;

            float t = songTimeAtSample + (float)delta;
            return t < 0f ? 0f : t;
        }

        /// <summary>清空采样状态（换曲/重开时用；自检也用它保证用例互不影响）</summary>
        public static void Reset()
        {
            songTimeAtSample = 0f;
            clockAtSample = 0.0;
            sampled = false;
        }
    }
}
