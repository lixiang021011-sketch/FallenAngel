using System.Collections.Generic;

namespace FallenAngel.Live
{
    /// <summary>
    /// 判定窗口数据：某一类音符的 Perfect/Great/Good/Bad 各自的前后时间（秒）。
    /// 逐字对应 OpenSekai Sekai/Live/JudgeTimeData.cs——那边的单位是"帧"（1/60s，
    /// 见 LiveConfig.CalcFrameToTime），这里统一在构造时换算成秒，语义不变。
    /// </summary>
    public struct LiveJudgeTimeData
    {
        public JudgeFrameType NoteType;
        public float PerfectBeforeJudgeTime;
        public float PerfectAfterJudgeTime;
        public float GreatBeforeJudgeTime;
        public float GreatAfterJudgeTime;
        public float GoodBeforeJudgeTime;
        public float GoodAfterJudgeTime;
        public float BadBeforeJudgeTime;
        public float BadAfterJudgeTime;

        public LiveJudgeTimeData(
            JudgeFrameType noteType,
            float perfectBefore, float perfectAfter,
            float greatBefore, float greatAfter,
            float goodBefore, float goodAfter,
            float badBefore, float badAfter)
        {
            NoteType = noteType;
            PerfectBeforeJudgeTime = perfectBefore;
            PerfectAfterJudgeTime = perfectAfter;
            GreatBeforeJudgeTime = greatBefore;
            GreatAfterJudgeTime = greatAfter;
            GoodBeforeJudgeTime = goodBefore;
            GoodAfterJudgeTime = goodAfter;
            BadBeforeJudgeTime = badBefore;
            BadAfterJudgeTime = badAfter;
        }
    }

    /// <summary>
    /// 窗口表本体（对应 LiveConfig.noteTypeJudgeData.JudgeTimes）。数值按 PJSK 的帧数口径给出，
    /// 全部可通过 SetJudgeTime 覆盖——"数值可调"是这一层的前提，不写死在音符逻辑里。
    /// </summary>
    public static class LiveJudgeWindows
    {
        /// <summary>PJSK 的窗口以帧为单位（60fps 口径）。</summary>
        public const float FramesPerSecond = 60f;

        private static readonly Dictionary<JudgeFrameType, LiveJudgeTimeData> judgeTimes =
            new Dictionary<JudgeFrameType, LiveJudgeTimeData>();

        /// <summary>未查到的类别回退到这张表（对应 LiveUtility.IsJudgmentTiming 的兜底）。</summary>
        public static JudgeFrameType Fallback = JudgeFrameType.Normal;

        static LiveJudgeWindows()
        {
            // 默认（普通音符）：2.5 / 5 / 6.5 / 7.5 帧，前后对称。
            SetFrames(JudgeFrameType.Normal, 2.5f, 2.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Normal_Critical, 2.5f, 2.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);

            SetFrames(JudgeFrameType.Flick, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Flick_Critical, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);

            SetFrames(JudgeFrameType.Long, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Long_Critical, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);

            // 尾判比头判宽（PJSK 的相对关系：前 3.5 / 后 4 帧）。
            SetFrames(JudgeFrameType.Long_End, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Long_End_Critical, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Long_End_Flick, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Long_End_Flick_Critical, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);

            SetFrames(JudgeFrameType.Friction, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Critical, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Flick, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Flick_Critical, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Long, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Long_Critical, 3.5f, 3.5f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Long_End, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
            SetFrames(JudgeFrameType.Friction_Long_End_Critical, 3.5f, 4f, 5f, 5f, 6.5f, 6.5f, 7.5f, 7.5f);
        }

        /// <summary>按帧设置窗口（PJSK 的原始口径）。</summary>
        public static void SetFrames(
            JudgeFrameType type,
            float perfectBefore, float perfectAfter,
            float greatBefore, float greatAfter,
            float goodBefore, float goodAfter,
            float badBefore, float badAfter)
        {
            SetJudgeTime(new LiveJudgeTimeData(
                type,
                CalcFrameToTime(perfectBefore), CalcFrameToTime(perfectAfter),
                CalcFrameToTime(greatBefore), CalcFrameToTime(greatAfter),
                CalcFrameToTime(goodBefore), CalcFrameToTime(goodAfter),
                CalcFrameToTime(badBefore), CalcFrameToTime(badAfter)));
        }

        public static void SetJudgeTime(LiveJudgeTimeData data)
        {
            judgeTimes[data.NoteType] = data;
        }

        /// <summary>对应 OpenSekai LiveConfig.CalcFrameToTime：帧 → 秒。</summary>
        public static float CalcFrameToTime(float frame)
        {
            return frame / FramesPerSecond;
        }

        public static LiveJudgeTimeData GetJudgeTime(JudgeFrameType type)
        {
            if (judgeTimes.TryGetValue(type, out var data))
            {
                return data;
            }

            return judgeTimes.TryGetValue(Fallback, out var fallback)
                ? fallback
                : default;
        }

        /// <summary>逐字对应 LiveUtility.IsJudgmentTiming：offset 是否落在 Bad 窗口内。</summary>
        public static bool IsJudgmentTiming(JudgeFrameType frameType, float offsetJudgeTime)
        {
            var data = GetJudgeTime(frameType);
            return data.BadBeforeJudgeTime > offsetJudgeTime
                && -data.BadAfterJudgeTime < offsetJudgeTime;
        }

        /// <summary>
        /// 逐字对应 LiveConfig.CalculateDefaultNoteResult：偏移 → 判定等级。
        /// offset = 音符时间 - 输入时间（正=按早了）。
        /// </summary>
        public static (NoteResult result, NoteResultDescription description) CalculateDefaultNoteResult(
            JudgeFrameType frameType, float offsetJudgeTime)
        {
            var data = GetJudgeTime(frameType);
            return CalcNoteResult(
                offsetJudgeTime,
                data.PerfectBeforeJudgeTime, data.PerfectAfterJudgeTime,
                data.GreatBeforeJudgeTime, data.GreatAfterJudgeTime,
                data.GoodBeforeJudgeTime, data.GoodAfterJudgeTime,
                data.BadBeforeJudgeTime, data.BadAfterJudgeTime);
        }

        /// <summary>尾判（长按收尾）用同一套计算，只是窗口来自 Long_End 那一行。</summary>
        public static (NoteResult result, NoteResultDescription description) CalculateLongEndNoteResult(
            JudgeFrameType frameType, float offsetJudgeTime)
        {
            return CalculateDefaultNoteResult(frameType, offsetJudgeTime);
        }

        private static (NoteResult, NoteResultDescription) CalcNoteResult(
            float offset,
            float perfectBefore, float perfectAfter,
            float greatBefore, float greatAfter,
            float goodBefore, float goodAfter,
            float badBefore, float badAfter)
        {
            bool early = offset >= 0f;
            var description = early
                ? NoteResultDescription.Fast
                : NoteResultDescription.Late;

            float magnitude = early ? offset : -offset;

            if (magnitude <= (early ? perfectBefore : perfectAfter))
            {
                return (NoteResult.Perfect, NoteResultDescription.None);
            }

            if (magnitude <= (early ? greatBefore : greatAfter))
            {
                return (NoteResult.Great, description);
            }

            if (magnitude <= (early ? goodBefore : goodAfter))
            {
                return (NoteResult.Good, description);
            }

            if (magnitude <= (early ? badBefore : badAfter))
            {
                return (NoteResult.Bad, description);
            }

            return (NoteResult.Miss, description);
        }

        /// <summary>逐字对应 LiveUtility.GetINoteToJudgeFrameType：类别 × critical × 判头/判尾 → 窗口行。</summary>
        public static JudgeFrameType GetINoteToJudgeFrameType(INote note)
        {
            if (note == null)
            {
                return Fallback;
            }

            bool hasParent = note.ParentNote != null;
            bool isCritical = note.Type == NoteType.Critical;

            switch (note.Category)
            {
                case NoteCategory.Normal:
                    if (hasParent)
                    {
                        return isCritical ? JudgeFrameType.Long_End_Critical : JudgeFrameType.Long_End;
                    }
                    return isCritical ? JudgeFrameType.Normal_Critical : JudgeFrameType.Normal;

                case NoteCategory.Long:
                    if (hasParent)
                    {
                        return isCritical ? JudgeFrameType.Long_End_Critical : JudgeFrameType.Long_End;
                    }
                    return isCritical ? JudgeFrameType.Long_Critical : JudgeFrameType.Long;

                case NoteCategory.Flick:
                    if (hasParent)
                    {
                        return isCritical ? JudgeFrameType.Long_End_Flick_Critical : JudgeFrameType.Long_End_Flick;
                    }
                    return isCritical ? JudgeFrameType.Flick_Critical : JudgeFrameType.Flick;

                case NoteCategory.Friction:
                case NoteCategory.FrictionHide:
                    if (hasParent)
                    {
                        return isCritical
                            ? JudgeFrameType.Friction_Long_End_Critical
                            : JudgeFrameType.Friction_Long_End;
                    }
                    return isCritical ? JudgeFrameType.Friction_Critical : JudgeFrameType.Friction;

                case NoteCategory.FrictionLong:
                case NoteCategory.FrictionHideLong:
                    if (hasParent)
                    {
                        return isCritical
                            ? JudgeFrameType.Friction_Long_End_Critical
                            : JudgeFrameType.Friction_Long_End;
                    }
                    return isCritical ? JudgeFrameType.Friction_Long_Critical : JudgeFrameType.Friction_Long;

                case NoteCategory.FrictionFlick:
                    if (hasParent)
                    {
                        return isCritical ? JudgeFrameType.Long_End_Flick_Critical : JudgeFrameType.Long_End_Flick;
                    }
                    return isCritical ? JudgeFrameType.Friction_Flick_Critical : JudgeFrameType.Friction_Flick;

                default:
                    return Fallback;
            }
        }
    }
}
