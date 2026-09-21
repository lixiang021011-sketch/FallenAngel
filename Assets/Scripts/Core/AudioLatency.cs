using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 音频**输出延迟**补偿（真机"音画偏差非常大"的根因之一）。
    ///
    /// 为什么需要：`AudioSource.time` 反映的是"已经交给声卡去播的位置"，而声音真正被听到还要再晚
    /// 一个 **DSP 缓冲 + 设备输出延迟**。Android 上这一段常见 80~250ms——不补的话就是
    /// "音符和鼓点差了一大截"（而且越快的曲子越明显）。
    ///
    /// 做法（不需要 JNI）：
    ///   · DSP 缓冲 = bufferLength × numBuffers ÷ sampleRate（`AudioSettings.GetDSPBufferSize` 直接给）；
    ///   · 设备输出延迟量不到（Android 的 `AudioManager.getOutputLatency()` 要 JNI，工程没启用该模块），
    ///     取一个保守常量 <see cref="DeviceLatencyEstimateSeconds"/>，剩余误差交给玩家侧的
    ///     `CalibrationSettings`（校准面板）继续微调。
    ///
    /// 语义与 CalibrationSettings 一致：**正补偿 = SongTime 变小 = 音符整体推迟**，
    /// 用来对齐"声音比画面晚"。
    /// </summary>
    public static class AudioLatency
    {
        /// <summary>设备输出延迟的保守估计（秒）。Android 真机常见 20~60ms，取中值。</summary>
        public const float DeviceLatencyEstimateSeconds = 0.035f;

        /// <summary>DSP 缓冲长度（秒），由 <see cref="Measure"/> 得到</summary>
        public static float DspBufferSeconds { get; private set; }

        /// <summary>总补偿量（秒）；<see cref="Enabled"/> 关掉时返回 0（可回退对比）</summary>
        public static float CompensationSeconds
        {
            get { return Enabled && PlatformNeedsCompensation ? RawCompensationSeconds : 0f; }
        }

        /// <summary>未考虑平台/开关的原始补偿量（自检与日志用）</summary>
        public static float RawCompensationSeconds
        {
            get { return DspBufferSeconds + DeviceLatencyEstimateSeconds; }
        }

        /// <summary>总开关（关掉 = 不补偿，方便 A/B 对比）</summary>
        public static bool Enabled = true;

        /// <summary>平台是否值得补偿：编辑器里 DSP 缓冲代表不了真机，故只在 Android/iOS 生效</summary>
        public static bool PlatformNeedsCompensation
        {
            get
            {
#if UNITY_ANDROID || UNITY_IOS
                return !Application.isEditor;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 开局调一次：**先把 DSP 缓冲往小压**（真减延迟），再量实际生效值。
        ///
        /// 为什么先压：Unity Android 默认 DSP 缓冲常见 4×1024/48kHz = **85ms**——这一份延迟
        /// 靠"事后补偿"是补不回来的（补偿只让判定/画面等齐，声音本身还是慢）。
        /// 能用更小的缓冲就直接用小缓冲，补偿负责剩下那点设备延迟。
        /// </summary>
        public static void ConfigureForLowLatency()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var config = AudioSettings.GetConfiguration();
                int original = config.dspBufferSize;
                int originalSampleRate = config.sampleRate;
                foreach (int candidate in new[] { 512, 256, 1024 })
                {
                    if (candidate >= original) continue;
                    config.dspBufferSize = candidate;
                    config.sampleRate = originalSampleRate;
                    if (AudioSettings.Reset(config))
                    {
                        Debug.Log(string.Format("[AudioLatency] DSP 缓冲 {0} → {1}（低延迟）", original, candidate));
                        break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[AudioLatency] 压 DSP 缓冲失败（不影响运行）：" + e.Message);
            }
#endif
            Measure();
        }

        /// <summary>量一次 DSP 缓冲（开局/换曲时调用即可，值不会变）</summary>
        public static void Measure()
        {
            try
            {
                AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
                float sampleRate = AudioSettings.outputSampleRate;
                DspBufferSeconds = sampleRate > 0 && bufferLength > 0 && numBuffers > 0
                    ? bufferLength * numBuffers / sampleRate
                    : 0f;
            }
            catch
            {
                DspBufferSeconds = 0f;
            }

            Debug.Log(string.Format(
                "[AudioLatency] 输出延迟补偿：DSP {0:0.0}ms + 设备估计 {1:0.0}ms = {2:0.0}ms（生效={3}，平台需要={4}）",
                DspBufferSeconds * 1000f, DeviceLatencyEstimateSeconds * 1000f,
                CompensationSeconds * 1000f, Enabled, PlatformNeedsCompensation));
        }
    }
}
