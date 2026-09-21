using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>
    /// 判定簇的工具面。逐字移植 OpenSekai Sekai/Live/LiveUtility.cs 的**判定相关子集**：
    ///   · GetINoteToJudgeFrameType（类别 × critical × 判头/判尾 → 窗口行）
    ///   · IsJudgmentTiming（偏移是否落在 Bad 窗口内）
    ///   · CalcExcuteNoteLane（长按/滑条在子音符之间推进判定跨度：跟手的来源）
    ///   · 时间常量（NoteEndIgnoreFrameTime / OneFrameTime / HalfFrameTime）
    ///
    /// 未移植：屏幕显示进度与排序层（CalcNoteShowRate* / CalcNoteSortingOrder）、
    /// 同屏数量估算（CalculateMaxSimultaneousNotes 已由 Core/NotePoolChecks 侧覆盖）、
    /// 精灵尺寸换算（CalcSpriteRendererSize）。
    /// </summary>
    public static class LiveUtility
    {
        /// <summary>音符"结束后仍占位"的时间（原作：1/6 秒 —— 与每指 1/6 秒规则同一个口径）</summary>
        public const float NoteEndIgnoreFrameTime = 1f / 6f;

        public const float OneFrameTime = 1f / 60f;

        public const float HalfFrameTime = 1f / 120f;

        public const float JudgmentLifeTime = 0.3f;

        /// <summary>原作按 SUS 的 12 条 lane 线算；本工程是 4/5 键，运行时读 LaneLayout，此常量仅作上限</summary>
        public const int LaneCount = 11;

        public static readonly Vector2 Vector2Zero = Vector2.zero;

        public static readonly Vector3 Vector3Zero = Vector3.zero;

        public static readonly Vector3 Vector3One = Vector3.one;

        public static readonly Vector2 Vector2Left = Vector2.left;

        public static readonly Vector2 Vector2Right = Vector2.right;

        /// <summary>屏幕像素距离 → 英寸（flick 幅度用；原作 LiveUtility.ScreenDpiToInch）</summary>
        public static float ScreenDpiToInch(float distance)
        {
            return Screen.dpi / 2.54f * distance;
        }

        /// <summary>逐字对应原作 LiveUtility.GetINoteToJudgeFrameType。</summary>
        public static JudgeFrameType GetINoteToJudgeFrameType(INote note)
        {
            if (note == null)
            {
                return JudgeFrameType.Normal;
            }

            var hasParent = note.ParentNote != null;
            var isCritical = note.Type == NoteType.Critical;

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
                    return JudgeFrameType.Normal;
            }
        }

        /// <summary>逐字对应原作 LiveUtility.IsJudgmentTiming：偏移是否落在 Bad 窗口内。</summary>
        public static bool IsJudgmentTiming(JudgeFrameType frameType, float offsetJudgeTime)
        {
            var judgeTimeData = LiveConfig.GetJudgeTime(frameType);
            return judgeTimeData.BadBeforeJudgeTime > offsetJudgeTime
                && -judgeTimeData.BadAfterJudgeTime < offsetJudgeTime;
        }

        /// <summary>逐字对应原作 LiveUtility.GreaterEqual。</summary>
        public static bool GreaterEqual(ref MusicScoreInfo a, ref MusicScoreInfo b)
        {
            return a.bar < b.bar || (a.bar == b.bar && a.barProgress <= b.barProgress);
        }

        public static bool GreaterEqual(int bar, float barProgress, ref MusicScoreInfo b)
        {
            return b.bar > bar || (b.bar == bar && b.barProgress >= barProgress);
        }

        public static Vector2 EarlyVec2Lerp(Vector2 a, Vector2 b, float t)
        {
            return a + (b - a) * Mathf.Max(t, 0f);
        }

        /// <summary>
        /// 逐字对应原作 LiveUtility.CalcExcuteNoteLane：
        /// 把"判定跨度"沿子音符链推进（currentNote → nextNote 插值），长按/滑条的跟手判定就在这条跨度上。
        /// 同时把 OffsetJudgeTime 夹在"起点已过"与"终点已过"之间（长按越到中段越宽松的来源）。
        /// </summary>
        public static void CalcExcuteNoteLane(
            LiveNoteBase noteBase,
            ref INote currentNote,
            ref INote nextNote,
            ref MusicScoreInfo currentFrameInfo,
            MusicScoreInfo musicScoreInfo,
            INote childNote)
        {
            if (noteBase == null || currentNote == null || nextNote == null)
            {
                return;
            }

            var progress = Mathf.Clamp01(
                LiveConfig.GetNoteLineParentProgress(currentFrameInfo.time, currentNote, nextNote));
            noteBase.LaneStartF = Mathf.Lerp(currentNote.DefaultLeftLane, nextNote.DefaultLeftLane, progress);
            noteBase.LaneEndF = Mathf.Lerp(currentNote.DefaultRightLane, nextNote.DefaultRightLane, progress);

            if (childNote == null)
            {
                return;
            }

            var elapsedAfterEnd = Mathf.Max(currentFrameInfo.time - childNote.MusicScoreInfo.time, 0f);
            var elapsedFromStart = currentFrameInfo.time - musicScoreInfo.time;
            noteBase.OffsetJudgeTime = Mathf.Min(elapsedAfterEnd, elapsedFromStart);
        }
    }
}
