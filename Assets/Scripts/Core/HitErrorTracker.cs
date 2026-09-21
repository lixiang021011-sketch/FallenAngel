using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 命中误差 → 建议判定偏移。**照抄 osu!（MIT）的口径**，逐条对照如下：
    ///
    /// | osu!（`osu.Game/Configuration/SessionAverageHitErrorTracker.cs`、`.../Audio/AudioOffsetAdjustControl.cs`） | 我们 |
    /// | --- | --- |
    /// | 一局游玩后取该局 hit error 的**中位数**（抗离群） | 每次判定记 `note.JudgedErrorSeconds`，一局结束时取中位数 |
    /// | 要求 `HitEvents.Count >= 50`（完整曲目） | 门槛 `MinJudgeCountPerPlay = 15`（demo 谱面短，见下） |
    /// | `DataPoint.SuggestedGlobalAudioOffset => GlobalAudioOffset - AverageHitError` | `SuggestedFrames => OffsetFrames - MsToFrames(MedianErrorMs)` |
    /// | 历史最多保留 50 个样本点，超出丢最旧 | `MaxHistory = 50` 同上 |
    /// | `SuggestedOffset = Math.Round(history.Average(x => x.SuggestedGlobalAudioOffset))` | 取平均后夹到上游帧范围 ±20 |
    /// | `Math.Abs(SuggestedOffset - current) < 1` 视为「已经准确」 | `CorrectThresholdMs = 1f` 同上 |
    /// | 应用建议后 `hitErrorTracker.ClearHistory()` | 同样清空 |
    ///
    /// **与 osu! 的两处差异（都写明理由）**：
    ///   ① 单局门槛 50 → 15：osu! 是完整曲目；本工程 demo 谱面短、测试常中断，
    ///      但中位数本身抗离群，门槛降低不会明显失真（差的样本整局带偏 → 由"重新打一局"覆盖）。
    ///   ② 单位 ms → **帧**：上游 PJSK/OpenSekai 的 `TimingAdjustData` 是帧（±20 帧 = ±333 ms），
    ///      所以建议值保留小数帧、显示时按 ms 取整（osu! 是直接舍入到 1 ms）。
    ///
    /// **符号**：`JudgedErrorSeconds = 触摸时刻 − 音符时刻`（**正 = 打晚**，与 osu! 的 hit error 同向）。
    /// 上游口径**正 = 音符提前**，所以"打晚 → 要让音符晚到 → 偏移变小"：建议 = 当前 − 误差。
    /// </summary>
    public static class HitErrorTracker
    {
        /// <summary>一局至少判定这么多次才产生一个样本（osu! = 50，见类注释差异①）</summary>
        public const int MinJudgeCountPerPlay = 15;

        /// <summary>历史样本上限（osu! 同值）</summary>
        public const int MaxHistory = 50;

        /// <summary>建议值与当前值差在多少毫秒内算「已经准确」（osu! 同值）</summary>
        public const float CorrectThresholdMs = 1f;

        /// <summary>
        /// 一个样本 = 一局的（命中误差中位数 ms + 当时的偏移帧）。
        /// 对应 osu! 的 `SessionAverageHitErrorTracker.DataPoint`。
        /// </summary>
        public struct Sample
        {
            /// <summary>该局命中误差的中位数（毫秒，正 = 打晚）</summary>
            public float MedianErrorMs;

            /// <summary>产生该样本时的判定偏移（帧，正 = 音符提前）</summary>
            public float OffsetFrames;

            public Sample(float medianErrorMs, float offsetFrames)
            {
                MedianErrorMs = medianErrorMs;
                OffsetFrames = offsetFrames;
            }

            /// <summary>本样本给出的建议偏移（帧，已夹到上游范围）。对应 osu! 的 `SuggestedGlobalAudioOffset`。</summary>
            public float SuggestedFrames
            {
                get { return CalibrationSettings.Clamp(OffsetFrames - MsToFrames(MedianErrorMs)); }
            }
        }

        public static float MsToFrames(float ms)
        {
            return ms * 60f / 1000f;
        }

        public static float FramesToMs(float frames)
        {
            return frames * 1000f / 60f;
        }

        /// <summary>中位数（偶数个取中间两个的平均）。空表返回 0。</summary>
        public static float MedianMs(IReadOnlyList<float> valuesMs)
        {
            if (valuesMs == null || valuesMs.Count == 0) return 0f;

            var sorted = new List<float>(valuesMs);
            sorted.Sort();
            int count = sorted.Count;
            if ((count & 1) == 1) return sorted[count / 2];
            return 0.5f * (sorted[count / 2 - 1] + sorted[count / 2]);
        }

        /// <summary>一局结束时尝试生成样本（判定数不够 → false，对应 osu! 的 `Count < 50` 直接 return）。</summary>
        public static bool TryCreateSample(IReadOnlyList<float> errorsMs, float offsetFrames, out Sample sample)
        {
            sample = default(Sample);
            if (errorsMs == null || errorsMs.Count < MinJudgeCountPerPlay) return false;
            sample = new Sample(MedianMs(errorsMs), offsetFrames);
            return true;
        }

        /// <summary>历史 → 建议值（帧）。对应 osu! 的 `SuggestedOffset = history.Average(...)`。</summary>
        public static bool TrySuggestFrames(IReadOnlyList<Sample> history, out float frames)
        {
            frames = 0f;
            if (history == null || history.Count == 0) return false;

            double sum = 0.0;
            for (int i = 0; i < history.Count; i++) sum += history[i].SuggestedFrames;
            frames = CalibrationSettings.Clamp((float)(sum / history.Count));
            return true;
        }

        /// <summary>「已经准确」判定（osu! 的 `Math.Abs(SuggestedOffset - current) < 1`，单位 ms）。</summary>
        public static bool IsAlreadyCorrect(float suggestedFrames, float currentFrames)
        {
            return Mathf.Abs(FramesToMs(suggestedFrames - currentFrames)) < CorrectThresholdMs;
        }
    }

    /// <summary>
    /// 样本历史（跨局累计，PlayerPrefs 持久化）。对应 osu! 里那个常驻的
    /// `SessionAverageHitErrorTracker`（保留最近 50 局、可 ClearHistory）。
    /// 序列化格式：`median:offset;median:offset;...`（不变文化，逗号会被解析器吃掉）。
    /// </summary>
    public static class HitErrorHistory
    {
        private const string Key = "FA_HitErrorHistory";

        private static List<HitErrorTracker.Sample> cache;

        public static int Count { get { return All.Count; } }

        public static IReadOnlyList<HitErrorTracker.Sample> All
        {
            get
            {
                if (cache == null) cache = Load();
                return cache;
            }
        }

        /// <summary>追加一个样本；超出上限丢最旧（osu! 的 `RemoveAt(0)`）。</summary>
        public static void Add(HitErrorTracker.Sample sample)
        {
            if (cache == null) cache = Load();
            cache.Add(sample);
            while (cache.Count > HitErrorTracker.MaxHistory) cache.RemoveAt(0);
            Save(cache);
        }

        public static void Clear()
        {
            cache = new List<HitErrorTracker.Sample>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        public static bool TrySuggestFrames(out float frames)
        {
            return HitErrorTracker.TrySuggestFrames(All, out frames);
        }

        /// <summary>自检用：丢掉内存缓存，强制下次从 PlayerPrefs 重新读。</summary>
        public static void InvalidateCache()
        {
            cache = null;
        }

        private static List<HitErrorTracker.Sample> Load()
        {
            var list = new List<HitErrorTracker.Sample>();
            string raw = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return list;

            string[] chunks = raw.Split(';');
            for (int i = 0; i < chunks.Length; i++)
            {
                if (string.IsNullOrEmpty(chunks[i])) continue;
                string[] pair = chunks[i].Split(':');
                if (pair.Length != 2) continue;

                float median, offset;
                if (!float.TryParse(pair[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out median)) continue;
                if (!float.TryParse(pair[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out offset)) continue;

                list.Add(new HitErrorTracker.Sample(median, offset));
            }

            while (list.Count > HitErrorTracker.MaxHistory) list.RemoveAt(0);
            return list;
        }

        private static void Save(List<HitErrorTracker.Sample> list)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(list[i].MedianErrorMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(':');
                sb.Append(list[i].OffsetFrames.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }
    }
}
