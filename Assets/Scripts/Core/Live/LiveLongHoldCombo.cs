namespace FallenAngel.Live
{
    /// <summary>
    /// 长按链上的"连击点"。逐字对应 OpenSekai Sekai/Live/LongHoldCombo.cs：
    /// 手指还在长按链的跨度里（且子音符活着）→ JustPerfect；否则过线即 Miss。
    /// 滑条"跟手"的连击节奏就由这一串点构成。
    /// </summary>
    public class LiveLongHoldCombo : LiveNoteBase
    {
        private readonly float laneOffset;

        public LiveLongHoldCombo()
        {
            Category = NoteCategory.Combo;
        }

        public LiveLongHoldCombo(MusicScoreInfo info, NoteType type, float speedRatio, float laneOffset)
            : base(info, 0, 0, 0, NoteCategory.Combo, speedRatio, laneOffset, type)
        {
            this.laneOffset = laneOffset;
        }

        public LiveLongHoldCombo(MusicScoreInfo info, NoteType type, int id, float speedRatio, float laneOffset)
            : base(info, id, 0, 0, NoteCategory.Combo, speedRatio, laneOffset, type)
        {
            this.laneOffset = laneOffset;
        }

        public override void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            if (State == NoteState.Done)
            {
                return;
            }

            if (State == NoteState.Last)
            {
                JudgeInfo = (NoteResult.Miss, NoteResultDescription.None);
                State = NoteState.Done;
                return;
            }

            if (MusicScoreInfo.time > currentFrameInfo.time)
            {
                return;
            }

            // 本工程适配：连击点看"长条是否还在被按住"（头在 Input/InputBegan），
            // 而不是只看子音符状态——上游那边子音符是摩擦链的末尾点，语义不同；
            // 照抄会出现"按住长条时连击点全 Miss"（因为尾音符还没被判过）。
            var child = parentNote?.ChildNote;
            bool holding = parentNote != null
                && (parentNote.State == NoteState.Input || parentNote.State == NoteState.InputBegan);
            if (holding || (child != null && (child.Result > NoteResult.Pass || child.State == NoteState.Input)))
            {
                JudgeInfo = (NoteResult.JustPerfect, NoteResultDescription.None);
                State = NoteState.Done;
                return;
            }

            State = NoteState.Last;
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State != NoteState.Last || parentNote == null)
            {
                return false;
            }

            return parentNote.LaneStartF - laneOffset <= lane && parentNote.LaneEndF + laneOffset >= lane;
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Last)
            {
                JudgeInfo = (NoteResult.JustPerfect, NoteResultDescription.None);
                State = NoteState.Done;
            }

            return true;
        }

        public override void Terminate()
        {
            if (State == NoteState.Done)
            {
                return;
            }

            var child = parentNote?.ChildNote;
            JudgeInfo = parentNote != null
                && (parentNote.WasHoldingWhenTerminated
                    || (child != null && (child.Result > NoteResult.Pass || child.State == NoteState.Input)))
                        ? (NoteResult.JustPerfect, NoteResultDescription.None)
                        : (NoteResult.Miss, NoteResultDescription.None);
            State = NoteState.Done;
        }

        protected override void OnSpawnNote()
        {
        }

        protected override void OnUnSpawnNote()
        {
            onUpdateScore?.Invoke(this);
            onUpdateCombo?.Invoke(this);
            onDamage?.Invoke(this);
            onJudgment?.Invoke(this);
        }

        public override bool AutoJudgment(MusicScoreInfo currentFrameInfo)
        {
            if (State == NoteState.Done || MusicScoreInfo.time > currentFrameInfo.time)
            {
                return false;
            }

            JudgeInfo = (NoteResult.Auto, NoteResultDescription.None);
            State = NoteState.Done;
            return true;
        }
    }
}
