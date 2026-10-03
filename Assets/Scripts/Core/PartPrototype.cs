using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>双声部最小样例：16 小节合成拨弦/鼓点，谱面和音频使用同一组节拍数据。</summary>
    public static class PartPrototype
    {
        public const string AudioName = "__part_prototype";
        /// <summary>每小节秒数（120 BPM、4/4）</summary>
        public const float BarSeconds = 2f;
        /// <summary>段长：4 小节 = 8 秒（一届"资源地形"）</summary>
        public const int BarsPerSection = 4;
        private static SongParts charts;
        private static AudioClip clip;

        /// <summary>
        /// 16 小节分成四段，故意做成**非对称**的资源地形（原型验证 D 项）：
        ///   1) 主题 A（0–8s）   ：Guitar 密（每小节 8）、Drums 疏（4）——吉他肥
        ///   2) 鼓组 B（8–16s）  ：Drums 密（8）、Guitar 疏（4）——鼓肥
        ///   3) 稳段（16–24s）   ：Guitar 每小节 2 条长音（权重 2×）+ 4 颗单击，Drums 4——稀疏但值钱
        ///   4) 鼓 Solo（24–32s）：Drums 每小节 16 颗（8 音/秒）+ Guitar 极稀——理论产率最高、失误也最贵
        /// 为什么要非对称：均匀循环只能验"切换是否顺手"，验不了"熟悉歌曲后预判切换"。
        /// </summary>
        public static SongParts Charts
        {
            get
            {
                if (charts != null) return charts;
                var guitar = NewChart();
                var drums = NewChart();
                int longId = 0;
                for (int bar = 0; bar < 16; bar++)
                {
                    int section = bar / BarsPerSection;
                    float t0 = 2f + bar * BarSeconds;
                    if (section == 0)
                    {
                        AddSteps(guitar, bar, t0, 8);
                        AddSteps(drums, bar, t0, 4);
                    }
                    else if (section == 1)
                    {
                        AddSteps(guitar, bar, t0, 4);
                        AddSteps(drums, bar, t0, 8);
                    }
                    else if (section == 2)
                    {
                        // 稳段：两条 1 秒长音（尾判权重 2×）+ 四颗单击，六颗换来八点理论能量
                        for (int i = 0; i < 2; i++)
                        {
                            float start = t0 + i * 1f;
                            guitar.notes.Add(new NoteData(i * 2, start, NoteType.LongStart, 1f, longId));
                            guitar.notes.Add(new NoteData(i * 2, start + 1f, NoteType.LongEnd, 0f, longId));
                            longId++;
                        }
                        // 四颗单击落在长音的缝隙里（0.25/0.75/1.25/1.75s），不与长音头同刻同轨
                        for (int i = 0; i < 4; i++)
                            guitar.notes.Add(new NoteData((i + bar) % 4, t0 + .25f + i * .5f, NoteType.Normal));
                        AddSteps(drums, bar, t0, 4);
                    }
                    else
                    {
                        AddSteps(guitar, bar, t0, 2);
                        AddSteps(drums, bar, t0, 16);
                    }
                }
                charts = new SongParts(guitar, drums);
                return charts;
            }
        }

        /// <summary>在一小节里均匀放 steps 颗单击（lane 按小节错开，避免每小节同一轨）。</summary>
        private static void AddSteps(ChartData chart, int bar, float startTime, int steps)
        {
            for (int i = 0; i < steps; i++)
                chart.notes.Add(new NoteData((i + bar) % 4, startTime + i * BarSeconds / steps, NoteType.Normal));
        }

        private static ChartData NewChart() => new ChartData
        {
            metadata = new ChartMetadata { songName = "Guitar / Drums Prototype", songArtist = "Synthesized demo",
                bpm = 120f, audioFileName = AudioName, difficulty = Difficulty.Normal, level = 3 }
        };

        /// <summary>只在显式选择演示曲时生成；整曲混音只播放一次，切换不会改音源。</summary>
        public static AudioClip Audio
        {
            get
            {
                if (clip != null) return clip;
                const int rate = 22050;
                var samples = new float[rate * 36];
                var random = new System.Random(713);
                foreach (NoteData note in Charts.Guitar.notes)
                {
                    if (note.type == NoteType.LongBody || note.type == NoteType.LongEnd) continue;
                    float hz = 164.81f * Mathf.Pow(2f, new[] { 0, 3, 7, 10 }[note.lane] / 12f);
                    int start = Mathf.RoundToInt(note.time * rate);
                    // 长音持续发声（时值内不衰减到零），单击照旧短促
                    int length = note.type == NoteType.LongStart
                        ? Mathf.Min(samples.Length - start, Mathf.RoundToInt((note.duration + .3f) * rate))
                        : rate / 3;
                    for (int j = 0; j < length; j++)
                    {
                        float t = j / (float)rate;
                        float envelope = note.type == NoteType.LongStart ? .12f + .10f * Mathf.Exp(-6f * t) : .22f * Mathf.Exp(-12f * t);
                        samples[start + j] += envelope *
                            (Mathf.Sin(2f * Mathf.PI * hz * t) + .3f * Mathf.Sin(4f * Mathf.PI * hz * t));
                    }
                }
                foreach (NoteData note in Charts.Drums.notes)
                {
                    if (note.type != NoteType.Normal) continue;
                    int start = Mathf.RoundToInt(note.time * rate);
                    for (int j = 0; j < rate / 5; j++)
                    {
                        float t = j / (float)rate;
                        float noise = (float)random.NextDouble() * 2f - 1f;
                        samples[start + j] += note.lane % 2 == 0
                            ? .32f * Mathf.Exp(-24f * t) * Mathf.Sin(2f * Mathf.PI * (70f * t + 2f * (1f - Mathf.Exp(-35f * t))))
                            : .18f * Mathf.Exp(-45f * t) * noise;
                    }
                }
                clip = AudioClip.Create(AudioName, samples.Length, 1, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
        }
    }
}
