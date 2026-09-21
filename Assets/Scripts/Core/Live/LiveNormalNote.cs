namespace FallenAngel.Live
{
    /// <summary>
    /// 普通音符。逐字对应 OpenSekai Sekai/Live/NormalNote.cs：
    /// 必须"按下边沿"（Began）才成立；挂到长按之后变成"长按尾"（Category = Long，只认抬起/取消）。
    /// </summary>
    public class LiveNormalNote : LiveNoteBase
    {
        public override NoteState State
        {
            get { return state; }
            protected set
            {
                if (value == NoteState.Done && State != NoteState.Done)
                {
                    parentNote?.ForceTerminate();
                    state = NoteState.Done;
                    OnUnSpawnNote();
                    return;
                }

                state = value;
                if (value == NoteState.Done)
                {
                    OnUnSpawnNote();
                }
                else if (value == NoteState.Playing)
                {
                    OnSpawnNote();
                }
            }
        }

        public LiveNormalNote()
        {
            Type = NoteType.Default;
            Category = NoteCategory.Normal;
        }

        public LiveNormalNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, float speedRatio, NoteType type, float laneOffset)
            : base(info, id, laneStart, laneEnd, category, speedRatio, laneOffset, type)
        {
        }

        public override void SetParentNote(LiveLongNote note)
        {
            base.SetParentNote(note);
            Type = (note != null && note.Type == NoteType.Critical) || Type == NoteType.Critical
                ? NoteType.Critical
                : NoteType.Default;
            Category = NoteCategory.Long;
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done || JudgeLaneStart > lane || JudgeLaneEnd < lane)
            {
                return false;
            }

            var offset = MusicScoreInfo.time - touch.musicTime;
            if (!LiveUtility.IsJudgmentTiming(LiveUtility.GetINoteToJudgeFrameType(this), offset))
            {
                return false;
            }

            return (Category == NoteCategory.Normal && touch.phase == LiveTouchPhase.Began)
                || (Category == NoteCategory.Long && touch.IsEndedOrCanceled());
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if ((Category == NoteCategory.Normal && touch.phase != LiveTouchPhase.Began)
                || (Category == NoteCategory.Long && !touch.IsEndedOrCanceled()))
            {
                return false;
            }

            var judgeInfo = parentNote != null
                ? LiveConfig.CalculateLongEndNoteResult(this, touch.musicTime)
                : LiveConfig.CalculateDefaultNoteResult(this, touch.musicTime);

            ApplyJudgeInfo(judgeInfo);
            State = NoteState.Done;
            return true;
        }

        public override NoteResult CalcNoteResult(float musicTime)
        {
            return (parentNote != null
                ? LiveConfig.CalculateLongEndNoteResult(this, musicTime)
                : LiveConfig.CalculateDefaultNoteResult(this, musicTime)).Item1;
        }
    }
}
