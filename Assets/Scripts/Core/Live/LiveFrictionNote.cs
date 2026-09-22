namespace FallenAngel.Live
{
    /// <summary>
    /// 摩擦音符（= 本工程的 Drag）。逐字对应 OpenSekai Sekai/Live/FrictionNote.cs：
    /// **不要求按下边沿**——手指落在跨度内、时机在窗口内就是候选；被触到后按住到进度走完 → JustPerfect。
    /// 这条就是 PJSK"跟手"的核心语义。
    /// </summary>
    public class LiveFrictionNote : LiveConnectionNote
    {
        protected bool isTouched;

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

        protected override void OnUnSpawnNote()
        {
            onJudgment?.Invoke(this);
            onUpdateCombo?.Invoke(this);
            onUpdateScore?.Invoke(this);
            onDamage?.Invoke(this);
            onUnspawn?.Invoke(this);
        }

        public LiveFrictionNote()
        {
            Category = NoteCategory.Friction;
        }

        public LiveFrictionNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, NoteType type, float speedRatio, float laneOffset,
            NoteLineType lineType = NoteLineType.Linear)
            : base(info, id, laneStart, laneEnd, category, type, speedRatio, laneOffset, lineType)
        {
            isTouched = false;
        }

        public override void SetParentNote(LiveLongNote note)
        {
            base.SetParentNote(note);
            Type = (note != null && note.Type == NoteType.Critical) || Type == NoteType.Critical
                ? NoteType.Critical
                : NoteType.Default;
            Category = NoteCategory.FrictionLong;
        }

        /// <summary>是否已经被手指触到（用于视图"按住中"的显示）</summary>
        public bool IsTouched
        {
            get { return isTouched; }
        }

        public override void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            this.offsetTime = offsetTime;
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

            OffsetJudgeTime = currentFrameInfo.time - MusicScoreInfo.time;
            if (State == NoteState.Wait)
            {
                State = NoteState.Playing;
            }

            var progress = CalcProgress(currentFrameInfo, offsetTime);
            if (progress < 0f && state != NoteState.Playing)
            {
                return;
            }

            Progress = progress;
            if ((State == NoteState.Input || isTouched) && Progress >= 1f)
            {
                OffsetJudgeTime = 0f;
                Progress = 1f;
                JudgeInfo = (NoteResult.JustPerfect, NoteResultDescription.None);
                State = NoteState.Done;
                return;
            }

            if (OffsetJudgeTime > LiveConfig.JudgeTimeAfter)
            {
                State = NoteState.Last;
            }
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            return State != NoteState.Done
                && JudgeLaneStart <= lane
                && JudgeLaneEnd >= lane
                && LiveUtility.IsJudgmentTiming(LiveUtility.GetINoteToJudgeFrameType(this), OffsetJudgeTime);
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            // 上游语义（作者 2026-09-22 定案）：**手指在窗内碰到 = 认领（isTouched），
            // 结算仍然发生在音符过判定线那一刻**（Excute 里 Progress >= 1）。
            //
            // 为什么改回来：本工程一度让单点 drag "碰到即结算"（判定与手指同时发生），
            // 于是**提前按住**时音符还悬在判定线上方就被判掉（作者反馈"黄键如果提前按住
            // 会以最早的位置提前判掉"）。认领 + 过线结算同时满足两件事：
            //   · 早碰不吃亏（窗内碰到就锁定，不会漏判）；
            //   · 判定时刻永远落在判定线上 = 玩家听到/看到的时机正确；
            //   · 判定误差采样也不会被 drag 的"早碰量"污染（认领那帧 HasJudgedError 会被撤销）。
            if (State == NoteState.Last)
            {
                OffsetJudgeTime = 0f;
                Progress = 1f;
            }
            else if (Progress < 1f)
            {
                isTouched = true;
                State = NoteState.Input;
                return false;
            }

            JudgeInfo = (NoteResult.JustPerfect, NoteResultDescription.None);
            State = NoteState.Done;
            return true;
        }

        public override void ResetNote()
        {
            base.ResetNote();
            isTouched = false;
        }
    }
}
