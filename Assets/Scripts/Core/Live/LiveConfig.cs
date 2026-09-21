using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>
    /// 判定配置表。逐字移植 OpenSekai Sekai/Live/LiveConfig.cs 的**判定相关子集**：
    ///   · noteTypeJudgeData（每类音符一套窗口，单位：秒）
    ///   · CalcFrameToTime（原始口径是 60fps 帧数）
    ///   · CalculateDefaultNoteResult / CalculateLongStartNoteResult / CalculateLongEndNoteResult /
    ///     CalculateFlickNoteResult / CalculateLongEndFlickNoteResult（偏移 → 档位 + 描述）
    ///   · IsLongEndJudgeTime / JudgeTime / JustPerfectJudgeTime(Half)
    ///   · GetNoteViewProgress / GetNoteLineParentProgress / GetNoteLineCenterProgress（音符线的插值）
    ///   · CalcLaneTransformX（原作 2D 视野下的轨道 → 世界 X）
    ///
    /// 未移植的部分（本次判定簇范围外，逐条对应原作同名成员，需要时再补）：
    ///   · 资源/AB 包名一族（NoteBundleName / GetNoteBundleName / SetNoteSkinAssetBundleName…）
    ///   · LiveSettingData 选项装载（LoadOptionData）、技能与分数槽（Life / Damages / ScoreGauge）
    ///   · SpawnPosition / JudgmentPositions（本工程的判定线几何走 Core/Live/LiveLaneResolver，读 LaneLayout）
    ///   · NoteTypeJudgeData.MissMesh 原作从 master data 读，这里按原作默认值 0
    /// </summary>
    public static class LiveConfig
    {
        /// <summary>
        /// 每类音符的判定窗口容器（对应原作 LiveConfig.NoteTypeJudgeData）。
        /// JudgeTimeBefore / JudgeTimeAfter 是**全表的最大前后值**（原作 UpdateJudgeTimeBeforeAndAfter 维护），
        /// 音符"过窗"与"长按尾是否可判"都读它。
        /// </summary>
        public struct NoteTypeJudgeData
        {
            /// <summary>原作：master data 的 MissMesh（连续多少帧没有输入就认为手指已放开）；默认 0</summary>
            public int MissMesh;

            public Dictionary<JudgeFrameType, JudgeTimeData> JudgeTimes;

            public float JudgeTimeBefore;

            public float JudgeTimeAfter;
        }

        /// <summary>JustPerfect 的半个窗口（±1/120s）：落在这里给 JustPerfect / "Just" 描述</summary>
        public static readonly float JustPerfectJudgeTimeHalf = 1f / 120f;

        /// <summary>JustPerfect 的窗口（±1/60s）</summary>
        public static readonly float JustPerfectJudgeTime = 1f / 60f;

        /// <summary>过线时间：音符超过这个时间还没结束时，长按进入 Last（原作 JudgeTime = 0.125f）</summary>
        public static readonly float JudgeTime = 0.125f;

        public static NoteTypeJudgeData noteTypeJudgeData;

        /// <summary>
        /// 全局窗口缩放（本工程扩展点，原作没有）：roguelike 的"判定变松/变严"按倍率作用于整张表。
        /// 倍率在**查表时**生效，不改表本身——和 Core/PlayRules 的"基础值 + 修改器"一致。
        /// </summary>
        private static float judgeTimeScale = 1f;

        /// <summary>设置全局窗口倍率（1 = 原作原值）。由 JudgeManager 在开局按 PlayRules 刷新。</summary>
        public static void SetJudgeTimeScale(float scale)
        {
            judgeTimeScale = scale > 0f ? scale : 1f;
        }

        public static float JudgeTimeScale { get { return judgeTimeScale; } }

        /// <summary>对应原作 LiveConfig.CalcFrameToTime：帧 → 秒（60fps 口径）。</summary>
        public static float CalcFrameToTime(float frame)
        {
            return frame * (1f / 60f);
        }

        static LiveConfig()
        {
            noteTypeJudgeData = new NoteTypeJudgeData
            {
                MissMesh = 0,
                JudgeTimes = new Dictionary<JudgeFrameType, JudgeTimeData>(),
                JudgeTimeBefore = 0f,
                JudgeTimeAfter = 0f
            };
            LoadMasterConfig();
        }

        /// <summary>
        /// 窗口表本体——逐字取自原作 LiveConfig.LoadMasterConfig（帧数、前后不对称、Critical 行都一致）。
        /// 这是"手感"的直接来源，改数值前先改 master data 口径。
        /// </summary>
        public static void LoadMasterConfig()
        {
            noteTypeJudgeData.JudgeTimes = new Dictionary<JudgeFrameType, JudgeTimeData>();
            noteTypeJudgeData.JudgeTimeBefore = 0f;
            noteTypeJudgeData.JudgeTimeAfter = 0f;

            AddJudgeFrame(JudgeFrameType.Normal, 2.5f, 2.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            AddJudgeFrame(JudgeFrameType.Flick, 2.5f, 2.5f, 6.5f, 7.5f, 7f, 8f, 7.5f, 8.5f);
            AddJudgeFrame(JudgeFrameType.Long, 2.5f, 2.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            AddJudgeFrame(JudgeFrameType.Long_End, 3.5f, 4f, 6.5f, 8f, 7.5f, 8.5f, 7.5f, 8.5f);
            AddJudgeFrame(JudgeFrameType.Long_End_Flick, 3.5f, 4f, 6.5f, 8f, 7.5f, 8.5f, 7.5f, 8.5f);
            AddJudgeFrame(JudgeFrameType.Normal_Critical, 3.3f, 3.3f, 4.5f, 4.5f, 6.5f, 6.5f, 7.5f, 7.5f);
            AddJudgeFrame(JudgeFrameType.Flick_Critical, 3.5f, 3.5f, 6.5f, 7.5f, 7f, 8f, 7.5f, 8.5f);
            AddJudgeFrame(JudgeFrameType.Long_Critical, 3.3f, 3.3f, 4.5f, 4.5f, 6.5f, 6.5f, 7.5f, 7.5f);
            AddJudgeFrame(JudgeFrameType.Long_End_Critical, 3.5f, 4f, 6.5f, 8f, 7.5f, 8.5f, 7.5f, 8.5f);
            AddJudgeFrame(JudgeFrameType.Long_End_Flick_Critical, 3.5f, 4f, 6.5f, 8f, 7.5f, 8.5f, 7.5f, 8.5f);

            // 原作的 TODO(original)：friction 专用窗口等 master data 到位再定，先按对应基础行复制。
            CopyJudgeFrame(JudgeFrameType.Friction, JudgeFrameType.Flick);
            CopyJudgeFrame(JudgeFrameType.Friction_Critical, JudgeFrameType.Flick_Critical);
            CopyJudgeFrame(JudgeFrameType.Friction_Flick, JudgeFrameType.Flick);
            CopyJudgeFrame(JudgeFrameType.Friction_Flick_Critical, JudgeFrameType.Flick_Critical);
            CopyJudgeFrame(JudgeFrameType.Friction_Long, JudgeFrameType.Long);
            CopyJudgeFrame(JudgeFrameType.Friction_Long_Critical, JudgeFrameType.Long_Critical);
            CopyJudgeFrame(JudgeFrameType.Friction_Long_End, JudgeFrameType.Long_End);
            CopyJudgeFrame(JudgeFrameType.Friction_Long_End_Critical, JudgeFrameType.Long_End_Critical);
        }

        private static void AddJudgeFrame(JudgeFrameType noteType,
            float perfectBefore, float perfectAfter, float greatBefore, float greatAfter,
            float goodBefore, float goodAfter, float badBefore, float badAfter)
        {
            var judgeTimeData = new JudgeTimeData(
                noteType,
                CalcFrameToTime(perfectBefore), CalcFrameToTime(perfectAfter),
                CalcFrameToTime(greatBefore), CalcFrameToTime(greatAfter),
                CalcFrameToTime(goodBefore), CalcFrameToTime(goodAfter),
                CalcFrameToTime(badBefore), CalcFrameToTime(badAfter));

            noteTypeJudgeData.JudgeTimes[noteType] = judgeTimeData;
            UpdateJudgeTimeBeforeAndAfter(judgeTimeData);
        }

        private static void CopyJudgeFrame(JudgeFrameType noteType, JudgeFrameType source)
        {
            var sourceData = noteTypeJudgeData.JudgeTimes[source];
            var judgeTimeData = new JudgeTimeData(noteType,
                sourceData.PerfectBeforeJudgeTime, sourceData.PerfectAfterJudgeTime,
                sourceData.GreatBeforeJudgeTime, sourceData.GreatAfterJudgeTime,
                sourceData.GoodBeforeJudgeTime, sourceData.GoodAfterJudgeTime,
                sourceData.BadBeforeJudgeTime, sourceData.BadAfterJudgeTime);

            noteTypeJudgeData.JudgeTimes[noteType] = judgeTimeData;
            UpdateJudgeTimeBeforeAndAfter(judgeTimeData);
        }

        private static void UpdateJudgeTimeBeforeAndAfter(JudgeTimeData judgeTimeData)
        {
            if (judgeTimeData.BadBeforeJudgeTime > noteTypeJudgeData.JudgeTimeBefore)
            {
                noteTypeJudgeData.JudgeTimeBefore = judgeTimeData.BadBeforeJudgeTime;
            }

            if (judgeTimeData.BadAfterJudgeTime > noteTypeJudgeData.JudgeTimeAfter)
            {
                noteTypeJudgeData.JudgeTimeAfter = judgeTimeData.BadAfterJudgeTime;
            }
        }

        /// <summary>取某类的窗口（未登记则回退 Normal），并套用全局倍率。</summary>
        public static JudgeTimeData GetJudgeTime(JudgeFrameType frameType)
        {
            if (noteTypeJudgeData.JudgeTimes == null ||
                !noteTypeJudgeData.JudgeTimes.TryGetValue(frameType, out var data))
            {
                if (noteTypeJudgeData.JudgeTimes == null ||
                    !noteTypeJudgeData.JudgeTimes.TryGetValue(JudgeFrameType.Normal, out data))
                {
                    return default;
                }
            }

            return Scale(data);
        }

        /// <summary>全表最大后值（套用倍率）——对应原作 noteTypeJudgeData.JudgeTimeAfter 的读取口径。</summary>
        public static float JudgeTimeAfter
        {
            get { return noteTypeJudgeData.JudgeTimeAfter * judgeTimeScale; }
        }

        /// <summary>全表最大前值（套用倍率）</summary>
        public static float JudgeTimeBefore
        {
            get { return noteTypeJudgeData.JudgeTimeBefore * judgeTimeScale; }
        }

        private static JudgeTimeData Scale(JudgeTimeData data)
        {
            if (Mathf.Approximately(judgeTimeScale, 1f))
            {
                return data;
            }

            var scaled = data;
            scaled.PerfectBeforeJudgeTime *= judgeTimeScale;
            scaled.PerfectAfterJudgeTime *= judgeTimeScale;
            scaled.GreatBeforeJudgeTime *= judgeTimeScale;
            scaled.GreatAfterJudgeTime *= judgeTimeScale;
            scaled.GoodBeforeJudgeTime *= judgeTimeScale;
            scaled.GoodAfterJudgeTime *= judgeTimeScale;
            scaled.BadBeforeJudgeTime *= judgeTimeScale;
            scaled.BadAfterJudgeTime *= judgeTimeScale;
            return scaled;
        }

        /// <summary>对应原作 LiveConfig.IsLongEndJudgeTime：长按尾当前是否还在可判范围内。</summary>
        public static bool IsLongEndJudgeTime(float offsetTime)
        {
            return -JudgeTimeAfter <= offsetTime && JudgeTimeBefore >= offsetTime;
        }

        // ── 判定结果计算 ────────────────────────────────────────────────

        public static (NoteResult, NoteResultDescription) CalculateDefaultNoteResult(INote note, float time)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Normal_Critical : JudgeFrameType.Normal,
                true);
        }

        public static (NoteResult, NoteResultDescription) CalculateLongStartNoteResult(INote note, float time)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Long_Critical : JudgeFrameType.Long,
                true);
        }

        public static (NoteResult, NoteResultDescription) CalculateLongEndNoteResult(INote note, float time)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Long_End_Critical : JudgeFrameType.Long_End,
                true);
        }

        public static (NoteResult, NoteResultDescription) CalculateFlickNoteResult(INote note, float time, bool isDirection)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Flick_Critical : JudgeFrameType.Flick,
                isDirection);
        }

        public static (NoteResult, NoteResultDescription) CalculateLongEndFlickNoteResult(INote note, float time, bool isDirection)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Long_End_Flick_Critical : JudgeFrameType.Long_End_Flick,
                isDirection);
        }

        public static (NoteResult, NoteResultDescription) CalculateFrictionFlickNoteResult(INote note, float time, bool isDirection)
        {
            return GetJudgeResultInternal(
                note.MusicScoreInfo.time - time,
                note.Type == NoteType.Critical ? JudgeFrameType.Friction_Flick_Critical : JudgeFrameType.Friction_Flick,
                isDirection);
        }

        /// <summary>逐字对应原作 LiveConfig.GetJudgeResultInternal（含 JustPerfect 与 FlickMiss 语义）。</summary>
        private static (NoteResult, NoteResultDescription) GetJudgeResultInternal(
            float offset, JudgeFrameType judgeFrameType, bool isDirection)
        {
            var data = GetJudgeTime(judgeFrameType);

            var perfectAfter = -data.PerfectAfterJudgeTime;
            if (isDirection)
            {
                if (-JustPerfectJudgeTimeHalf <= offset && JustPerfectJudgeTimeHalf >= offset)
                {
                    return (NoteResult.JustPerfect, GetNoteResultDescription(offset));
                }

                if (data.PerfectBeforeJudgeTime >= offset && perfectAfter <= offset)
                {
                    return (NoteResult.Perfect, GetNoteResultDescription(offset));
                }
            }
            else if (data.PerfectBeforeJudgeTime >= offset && perfectAfter <= offset)
            {
                // 方向不对但时机在 Perfect 内：原作给 Great + FlickMiss（不判 Miss）
                return (NoteResult.Great, NoteResultDescription.FlickMiss);
            }

            if (data.GreatBeforeJudgeTime >= offset && -data.GreatAfterJudgeTime <= offset)
            {
                return (NoteResult.Great, GetNoteResultDescription(offset));
            }

            if (data.GoodBeforeJudgeTime >= offset && -data.GoodAfterJudgeTime <= offset)
            {
                return (NoteResult.Good, GetNoteResultDescription(offset));
            }

            if (data.BadBeforeJudgeTime >= offset && -data.BadAfterJudgeTime <= offset)
            {
                return (NoteResult.Bad, GetNoteResultDescription(offset));
            }

            return (NoteResult.Miss, GetNoteResultDescription(offset));
        }

        private static NoteResultDescription GetNoteResultDescription(float judgeOffsetTime)
        {
            if (-JustPerfectJudgeTimeHalf <= judgeOffsetTime && JustPerfectJudgeTimeHalf >= judgeOffsetTime)
            {
                return NoteResultDescription.Just;
            }

            return judgeOffsetTime > JustPerfectJudgeTimeHalf
                ? NoteResultDescription.Fast
                : NoteResultDescription.Late;
        }

        // ── 音符线（长按/滑条）插值 ──────────────────────────────────────

        /// <summary>逐字对应原作 LiveConfig.GetNoteViewProgress（音符显示进度曲线，视图与生成同源）。</summary>
        public static float GetNoteViewProgress(float progress)
        {
            var value = Mathf.Pow(1.06f, (Mathf.Min(progress, 2f) - 1f) * 45f);
            if (value <= 0.04f || float.IsNaN(value))
            {
                return 0f;
            }

            return float.IsInfinity(value) ? 2f : value;
        }

        /// <summary>逐字对应原作 LiveConfig.GetNoteLineParentProgress：长按/滑条在两个音符之间的插值进度。</summary>
        public static float GetNoteLineParentProgress(float time, INote startNote, INote endNote)
        {
            var duration = endNote.MusicScoreInfo.time - startNote.MusicScoreInfo.time;
            if (Mathf.Approximately(duration, 0f))
            {
                return 1f;
            }

            var progress = Mathf.Clamp01((time - startNote.MusicScoreInfo.time) / duration);
            return GetNoteLineCenterProgress(progress, startNote);
        }

        public static float GetNoteLineCenterProgress(float t, INote startNote)
        {
            t = Mathf.Clamp01(t);
            return startNote?.LineType switch
            {
                NoteLineType.EaseOut => 1f - (1f - t) * (1f - t),
                NoteLineType.EaseIn => t * t,
                _ => t
            };
        }

        /// <summary>逐字对应原作 LiveUtility.CalcLaneTransformX：轨道（小数）→ 世界 X。</summary>
        public static float CalcLaneTransformX(int lane)
        {
            return lane * 0.84f - 4.62f;
        }

        public static float CalcLaneTransformX(float lane)
        {
            return lane * 0.84f - 4.62f;
        }

        /// <summary>原作 LiveUtility.GetLaneOffset 的等价物：本工程没有 AB 包级别的偏移资产，按类别给常量。</summary>
        public const float DefaultLaneOffset = 0.5f;
    }
}
