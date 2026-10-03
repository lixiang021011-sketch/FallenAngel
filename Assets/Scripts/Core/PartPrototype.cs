using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>双声部最小样例：16 小节合成拨弦/鼓点，谱面和音频使用同一组节拍数据。</summary>
    public static class PartPrototype
    {
        public const string AudioName = "__part_prototype";
        private static SongParts charts;
        private static AudioClip clip;

        public static SongParts Charts
        {
            get
            {
                if (charts != null) return charts;
                var guitar = NewChart();
                var drums = NewChart();
                for (int bar = 0; bar < 16; bar++)
                {
                    bool guitarLead = (bar / 4) % 2 == 0;
                    int guitarSteps = guitarLead ? 8 : 4;
                    int drumSteps = guitarLead ? 4 : 8;
                    for (int i = 0; i < guitarSteps; i++)
                        guitar.notes.Add(new NoteData((i + bar) % 4, 2f + bar * 2f + i * 2f / guitarSteps, NoteType.Normal));
                    for (int i = 0; i < drumSteps; i++)
                        drums.notes.Add(new NoteData(i % 4, 2f + bar * 2f + i * 2f / drumSteps, NoteType.Normal));
                }
                charts = new SongParts(guitar, drums);
                return charts;
            }
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
                    float hz = 164.81f * Mathf.Pow(2f, new[] { 0, 3, 7, 10 }[note.lane] / 12f);
                    int start = Mathf.RoundToInt(note.time * rate);
                    for (int j = 0; j < rate / 3; j++)
                    {
                        float t = j / (float)rate;
                        samples[start + j] += .22f * Mathf.Exp(-12f * t) *
                            (Mathf.Sin(2f * Mathf.PI * hz * t) + .3f * Mathf.Sin(4f * Mathf.PI * hz * t));
                    }
                }
                foreach (NoteData note in Charts.Drums.notes)
                {
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
