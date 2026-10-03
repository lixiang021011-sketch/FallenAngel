using System;
using UnityEngine;

namespace FallenAngel.Data
{
    /// <summary>
    /// 一条分轨（Stem）：某件乐器/声部对应的一条音频文件。
    ///
    /// 一首歌不再是一条混音，而是多条轨**同时播放**，当前演奏的那条被推出来
    /// （混音规则在 `Core/StemMix`）。这不是"重组编曲"——曲子本身没变，
    /// 只是把本来就存在的各层分开播、分开调音量。
    /// </summary>
    [Serializable]
    public sealed class SongStem
    {
        /// <summary>对应声部名（与 SongParts.PartNames 对齐）</summary>
        public string partName;
        /// <summary>音频文件名（不含扩展名，放 Resources/Audio 下；原型用合成轨的名字）</summary>
        public string audioFileName;
    }

    /// <summary>切换生效时机（每首歌可配，见 <see cref="PartSwitchConfig"/>）</summary>
    public enum PartSwitchMode
    {
        /// <summary>按下就换（原型当前行为）</summary>
        Immediate = 0,
        /// <summary>下一个拍点生效（音乐上更整齐，代价是按下后要等到拍点）</summary>
        NextBeat = 1,
        /// <summary>下一个小节生效（最整齐，等待最长）</summary>
        NextBar = 2,
    }

    /// <summary>
    /// "什么时候才真的换声部"的配置。**每首歌可以不一样**：
    /// 快歌/密集段可能想要 Immediate（跟着手感走），慢歌可能想要 NextBar（换得更像"编排"）。
    ///
    /// 三个可配项：mode（立即/下一拍/下一小节）、minInterval（两次切换最短间隔，0 = 不限）、
    /// beatsPerBar（量化用的小节拍数，3/4 拍歌写 3）。
    /// 缺省 = Immediate + 不限间隔，与加这个功能之前完全一样。
    /// </summary>
    [Serializable]
    public struct PartSwitchConfig
    {
        public PartSwitchMode mode;
        public float minIntervalSeconds;
        public float beatsPerBar;

        public static PartSwitchConfig Immediate
        {
            get
            {
                return new PartSwitchConfig
                {
                    mode = PartSwitchMode.Immediate,
                    minIntervalSeconds = 0f,
                    beatsPerBar = 4f,
                };
            }
        }

        /// <summary>拍长（秒）。bpm ≤ 0 时返回 0，调用方按"无法量化"处理。</summary>
        public static float BeatSeconds(float bpm)
        {
            return bpm > 0.01f ? 60f / bpm : 0f;
        }

        /// <summary>
        /// 该在哪一刻生效。返回 ≤ songTime 表示"立刻"。
        /// 量化取**严格下一个**拍/小节（正好踩在拍点上按下也等下一拍），
        /// 这样玩家不会感觉"有时等半拍、有时不等"。
        /// </summary>
        public static float TargetTime(PartSwitchMode mode, float songTime, float bpm, float beatsPerBar)
        {
            if (mode == PartSwitchMode.Immediate) return songTime;
            float beat = BeatSeconds(bpm);
            if (beat <= 0f) return songTime;
            float grid = mode == PartSwitchMode.NextBar ? beat * Mathf.Max(1f, beatsPerBar) : beat;
            const float epsilon = 0.0001f;
            return (Mathf.Floor((songTime + epsilon) / grid) + 1f) * grid;
        }

        public float TargetTime(float songTime, float bpm)
        {
            return TargetTime(mode, songTime, bpm, beatsPerBar <= 0f ? 4f : beatsPerBar);
        }

        public string Describe()
        {
            switch (mode)
            {
                case PartSwitchMode.NextBeat: return "下一拍生效";
                case PartSwitchMode.NextBar: return "下一小节生效";
                default: return "立即生效";
            }
        }
    }
}
