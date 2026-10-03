using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>双声部最小样例：16 小节合成拨弦/鼓点，谱面和音频使用同一组节拍数据。</summary>
    public static class PartPrototype
    {
        public const string AudioName = "__part_prototype";
        /// <summary>分轨文件名前缀：原型把每件乐器各自合成一条轨（真实曲目用 Resources/Audio 下的四条文件）</summary>
        public const string StemPrefix = "__part_stem_";
        private static readonly System.Collections.Generic.Dictionary<string, AudioClip> stemClips =
            new System.Collections.Generic.Dictionary<string, AudioClip>();

        public static string StemName(string partName) { return StemPrefix + partName; }

        /// <summary>原型分轨解析：文件名形如 __part_stem_Guitar 时合成对应那条轨</summary>
        public static bool TryResolveStem(string fileName, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrEmpty(fileName) || !fileName.StartsWith(StemPrefix)) return false;
            string partName = fileName.Substring(StemPrefix.Length);
            if (!stemClips.TryGetValue(partName, out clip) || clip == null)
            {
                clip = Synthesize(StemName(partName), partName);
                stemClips[partName] = clip;
            }
            return clip != null;
        }
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
        ///   4) 鼓 Solo（26–34s）：Drums 从 8 分一路加难到 32 分爆点（宽跳 trill / 同轨连打 / 跑动 / 爆点），
        ///      Guitar 是同段的长音（稀疏但值钱）——理论产率最高、失误也最贵，且**真的打不动**。
        ///   另有一件 Bass 全程不变：每小节 4 颗八分、只走 0/3 两轨——**最稳也最低产**的第三选项。
        /// 为什么要非对称：均匀循环只能验"切换是否顺手"，验不了"熟悉歌曲后预判切换"。
        /// 为什么鼓 Solo 不能只是"密"：整段单击的话手快的玩家照样全 Perfect，
        /// Risk/Reward 就只活在数值表里；所以这里加的是手速门槛（同轨连打、宽跳、32 分）。
        /// 为什么要有第三件：两件乐器只能验"换不换"，三件才能验"换哪件"——
        /// 底部切换条、色相表、路线记录也才有 N 件的真实数据。
        /// </summary>
        public static SongParts Charts
        {
            get
            {
                if (charts != null) return charts;
                var guitar = NewChart();
                var drums = NewChart();
                var bass = NewChart();
                int longId = 0;
                for (int bar = 0; bar < 16; bar++)
                {
                    int section = bar / BarsPerSection;
                    float t0 = 2f + bar * BarSeconds;
                    // Bass：全程每小节 4 颗八分，只在 0/3 两轨交替——安全档的"稳定收益"
                    for (int i = 0; i < 4; i++)
                        bass.notes.Add(new NoteData((i + bar) % 2 == 0 ? 0 : 3, t0 + i * BarSeconds / 4f, NoteType.Normal));
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
                        AddLongNotes(guitar, bar, t0, 2, ref longId);
                        // 四颗单击落在长音的缝隙里（0.25/0.75/1.25/1.75s），不与长音头同刻同轨
                        for (int i = 0; i < 4; i++)
                            guitar.notes.Add(new NoteData((i + bar) % 4, t0 + .25f + i * .5f, NoteType.Normal));
                        AddSteps(drums, bar, t0, 4);
                    }
                    else
                    {
                        AddDrumSoloBar(drums, bar, t0);
                        AddLongNotes(guitar, bar, t0, 2, ref longId);
                    }
                }
                charts = new SongParts(new[] { "Guitar", "Drums", "Bass" }, new[] { guitar, drums, bass });
                return charts;
            }
        }

        /// <summary>在一小节里均匀放 steps 颗单击（lane 按小节错开，避免每小节同一轨）。</summary>
        private static void AddSteps(ChartData chart, int bar, float startTime, int steps)
        {
            for (int i = 0; i < steps; i++)
                chart.notes.Add(new NoteData((i + bar) % 4, startTime + i * BarSeconds / steps, NoteType.Normal));
        }

        /// <summary>每小节 count 条长音（时值 0.9 小节），尾巴单列一条 LongEnd 供视图用。</summary>
        private static void AddLongNotes(ChartData chart, int bar, float startTime, int count, ref int longId)
        {
            float span = BarSeconds / count;
            for (int i = 0; i < count; i++)
            {
                float start = startTime + i * span;
                chart.notes.Add(new NoteData(i * 2, start, NoteType.LongStart, span * 0.9f, longId));
                chart.notes.Add(new NoteData(i * 2, start + span * 0.9f, NoteType.LongEnd, 0f, longId));
                longId++;
            }
        }

        /// <summary>
        /// 鼓 Solo 的四小节墙（第 12–15 小节），难度是递进的，玩家能学到"这里该撤"：
        ///   12 小节：前半 8 分（4 颗）+ 后半 16 分宽跳 trill（轨 0 ↔ 3）
        ///   13 小节：16 分同轨连打（轨 1 八连 → 轨 2 八连）——一根手指要连点
        ///   14 小节：16 分跑动（上行 8 颗 → 下行 8 颗）
        ///   15 小节：32 分爆点（0.0625s 间隔，轨 0 ↔ 1）+ 后半 8 分收尾
        /// </summary>
        private static void AddDrumSoloBar(ChartData drums, int bar, float startTime)
        {
            float step16 = BarSeconds / 16f;
            switch (bar - 12)
            {
                case 0:
                    for (int i = 0; i < 4; i++)
                        drums.notes.Add(new NoteData(i % 4, startTime + i * BarSeconds / 4f, NoteType.Normal));
                    for (int i = 0; i < 8; i++)
                        drums.notes.Add(new NoteData(i % 2 == 0 ? 0 : 3, startTime + BarSeconds * 0.5f + i * step16, NoteType.Normal));
                    break;
                case 1:
                    for (int i = 0; i < 8; i++)
                        drums.notes.Add(new NoteData(1, startTime + i * step16, NoteType.Normal));
                    for (int i = 0; i < 8; i++)
                        drums.notes.Add(new NoteData(2, startTime + BarSeconds * 0.5f + i * step16, NoteType.Normal));
                    break;
                case 2:
                    for (int i = 0; i < 16; i++)
                    {
                        int lane = i < 8 ? i : 15 - i;
                        drums.notes.Add(new NoteData(lane % 4, startTime + i * step16, NoteType.Normal));
                    }
                    break;
                default:
                    for (int i = 0; i < 8; i++)
                        drums.notes.Add(new NoteData(i % 2, startTime + i * BarSeconds / 32f, NoteType.Normal));
                    for (int i = 0; i < 4; i++)
                        drums.notes.Add(new NoteData((i + 2) % 4, startTime + BarSeconds * 0.5f + i * BarSeconds / 8f, NoteType.Normal));
                    break;
            }
        }

        private static ChartData NewChart() => new ChartData
        {
            metadata = new ChartMetadata
            {
                songName = "Guitar / Drums / Bass Prototype", songArtist = "Synthesized demo",
                bpm = 120f, audioFileName = AudioName, difficulty = Difficulty.Normal, level = 3,
                // 三件乐器各一条轨同时播，当前演奏的那条增强（见 Core/StemMix）
                stems = new System.Collections.Generic.List<SongStem>
                {
                    new SongStem { partName = "Guitar", audioFileName = StemName("Guitar") },
                    new SongStem { partName = "Drums", audioFileName = StemName("Drums") },
                    new SongStem { partName = "Bass", audioFileName = StemName("Bass") },
                },
                switchTiming = PartSwitchConfig.Immediate,
            }
        };

        /// <summary>单条混音（旧路径/兜底）：三件乐器合在一起，不做分轨增强。</summary>
        public static AudioClip Audio
        {
            get
            {
                if (clip != null) return clip;
                clip = Synthesize(AudioName, null);
                return clip;
            }
        }

        /// <summary>
        /// 合成音频。onlyPart = null 时是整曲混音，否则只合成那一件乐器（分轨路径）。
        /// 三件乐器共用同一段节拍数据，所以分轨之间天然对齐。
        /// </summary>
        private static AudioClip Synthesize(string clipName, string onlyPart)
        {
            const int rate = 22050;
            var samples = new float[rate * 36];
            var random = new System.Random(713);
            if (onlyPart == null || onlyPart == "Guitar")
            {
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
                        float envelope = note.type == NoteType.LongStart
                            ? .12f + .10f * Mathf.Exp(-6f * t) : .22f * Mathf.Exp(-12f * t);
                        samples[start + j] += envelope *
                            (Mathf.Sin(2f * Mathf.PI * hz * t) + .3f * Mathf.Sin(4f * Mathf.PI * hz * t));
                    }
                }
            }
            if (onlyPart == null || onlyPart == "Drums")
            {
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
            }
            if (onlyPart == null || onlyPart == "Bass")
            {
                foreach (NoteData note in Charts.Bass.notes)
                {
                    if (note.type != NoteType.Normal) continue;
                    float hz = 82.41f * Mathf.Pow(2f, new[] { 0, 3, 7, 5 }[note.lane] / 12f);
                    int start = Mathf.RoundToInt(note.time * rate);
                    int length = Mathf.Min(samples.Length - start, rate / 2);
                    for (int j = 0; j < length; j++)
                    {
                        float t = j / (float)rate;
                        samples[start + j] += .20f * Mathf.Exp(-2.6f * t) * Mathf.Sin(2f * Mathf.PI * hz * t);
                    }
                }
            }
            AudioClip result = AudioClip.Create(clipName, samples.Length, 1, rate, false);
            result.SetData(samples, 0);
            return result;
        }
    }
}
