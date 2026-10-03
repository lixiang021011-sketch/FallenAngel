using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 分轨混音规则：当前演奏轨推出来、其余保持底噪；其余轨不静音（静音会让曲子缺一块）。
    /// 纯函数，自检可直接断言。
    /// </summary>
    public static class StemMix
    {
        /// <summary>当前演奏轨的音量倍率（相对歌曲总音量）</summary>
        public const float ActiveGain = 1.0f;
        /// <summary>其余轨的音量倍率：0.7 ≈ 相对 −3.1dB，听得出来但不会让曲子塌掉</summary>
        public const float InactiveGain = 0.7f;
        /// <summary>换声部时音量过渡时长（秒）——比硬切更耐听</summary>
        public const float RampSeconds = 0.35f;

        public static float GainFor(bool isActiveStem, float activeGain = ActiveGain, float inactiveGain = InactiveGain)
        {
            return isActiveStem ? activeGain : inactiveGain;
        }

        /// <summary>音量逼近目标（每帧调用；rampSeconds ≤ 0 表示直接到位）</summary>
        public static float Approach(float current, float target, float rampSeconds, float deltaSeconds)
        {
            if (rampSeconds <= 0f) return target;
            // 指数逼近：约一个 rampSeconds 后走到目标的 95%，耳朵听到的是"推上去"而不是硬切
            float k = Mathf.Clamp01(3f * deltaSeconds / rampSeconds);
            return Mathf.Lerp(current, target, k);
        }
    }
}
