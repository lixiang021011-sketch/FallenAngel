using UnityEngine;

namespace FallenAngel.Audio
{
    /// <summary>
    /// 程序合成占位音效（零资源依赖）。
    /// 项目暂无音频资源，用 AudioClip.Create 在运行期生成短促击打音，
    /// 保证手感闭环的听觉反馈立即可用（架构约定 §6 手感四闭环之听觉闭环）。
    /// 将来有真实采样后，直接给 AudioManager 的音效 clip 字段赋值即可自动替换，代码零改动。
    /// </summary>
    public static class SynthesizedSfx
    {
        /// <summary>
        /// 生成一个短促击打音：正弦基频 + 二倍频谐波 + 指数衰减包络。
        /// </summary>
        /// <param name="baseFreq">基频（Hz），决定音调高低</param>
        /// <param name="duration">时长（秒）</param>
        /// <param name="decayRate">衰减速率，越大声音越短促</param>
        public static AudioClip CreateHitClip(float baseFreq, float duration, float decayRate = 18f)
        {
            int sampleRate = 44100;
            int samples = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float env = Mathf.Exp(-decayRate * t);                      // 指数衰减包络
                float s = Mathf.Sin(2f * Mathf.PI * baseFreq * t);         // 基频
                float h = Mathf.Sin(2f * Mathf.PI * baseFreq * 2f * t) * 0.4f; // 二倍频谐波（增加"打击感"）
                data[i] = (s + h) * env * 0.6f;
            }

            AudioClip clip = AudioClip.Create("Sfx_Synthesized", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
