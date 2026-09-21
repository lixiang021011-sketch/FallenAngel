using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>
    /// 长按音符容器。逐字移植 OpenSekai Sekai/Live/LongNote.cs 的判定面：
    ///
    ///   · 头判：Playing/Last 状态下，**按下边沿**落在判定跨度内 + 头窗口（Long / Long_Critical）→
    ///     写 JudgeInfo，进入 InputBegan（"按住中"）；此后每次输入把状态刷成 Input 并刷新 lastInputFrame。
    ///   · 松手/掉指：MissMesh 帧没有输入 → Release。Release 不是终局：手指回来还能再次 InputBegan
    ///     （状态机里 `State = State == Release ? InputBegan : Input`）——这就是"滑条掉段不断整条"的来源。
    ///   · 尾判：子音符（长按尾）进窗后，由同一次输入的 IsJudgment + Judgment 结算
    ///     （Long_End 窗口，比头判更宽），尾判成立 → 整条 Done。
    ///   · 越过 JudgeTime（0.125s）仍按着 → Last；Last 上再执行一次 → Miss + Release + 伤害/连击/判定回调。
    ///   · 判定跨度沿子音符链推进：LiveUtility.CalcExcuteNoteLane 把 LaneStartF/LaneEndF 在
    ///     ViewNoteList 的 current → next 之间插值——滑条"跟手"的几何前提。
    ///
    /// 与上游唯一的接线级差别：`Time.frameCount` 换成可注入的 <see cref="LiveJudgeClock"/>。
    /// </summary>
    public class LiveLongNote : LiveNoteBase
    {
        protected int lastInputFrame;

        /// <summary>判定跨度的外扩（轨）：与 JudgeLaneStart/End 一起决定"手指算不算在条上"</summary>
        public float LaneOffset { get; set; }

        /// <summary>结束时手指是否还按着（ForceTerminate 的判据之一）</summary>
        public bool WasHoldingWhenTerminated { get; set; }

        /// <summary>
        /// 可选：判定跨度的"路径采样器"（本工程滑条用）。
        /// 输入 = 本条音符的进度 0..1，输出 = 该进度处的**小数轨道中心**。
        /// 为什么需要：上游用"连接点"（ConnectionNote）串出弯曲的判定跨度，我们的谱面自带 path，
        /// 直接按 path 采样，保证**判定跨度和玩家看到的引导线是同一条曲线**（否则弯道处会"不跟手"）。
        /// </summary>
        public Func<float, float> PathLaneSampler;

        /// <summary>
        /// 本工程产品语义：**按住到尾巴就算完成**（不必在 Long_End 窗口内正好松手）。
        ///
        /// 上游要求"在尾部窗口内抬起"：漏松手（一直按住到尾巴之后）会走成 Miss。
        /// 我们的鼓/吉他谱是"按住打到尾"，照搬会出现**按住不放反而判 Miss** 的 bug（真机已复现）。
        /// 开启后：到尾部时刻若仍按住 → 以尾部时刻结算该条（JustPerfect）；
        /// 窗口内的正常松手/提前松手/掉指重拾语义都不变。
        /// </summary>
        public bool CompleteTailWhileHolding { get; set; }

        public float JudgedLaneEnd
        {
            get { return LaneEndF + LaneOffset; }
        }

        public float JudgedLaneStart
        {
            get { return LaneStartF - LaneOffset; }
        }

        public override int LaneStart
        {
            get { return (int)Math.Round(LaneStartF, MidpointRounding.ToEven); }
            set
            {
                base.LaneStart = value;
                LaneStartF = value;
            }
        }

        public override int LaneEnd
        {
            get { return (int)Math.Round(LaneEndF, MidpointRounding.ToEven); }
            set
            {
                base.LaneEnd = value;
                LaneEndF = value;
            }
        }

        public override NoteState State
        {
            get { return state; }
            protected set
            {
                if ((state == NoteState.Playing && value == NoteState.InputBegan)
                    || (state == NoteState.Last && value == NoteState.InputBegan))
                {
                    onJudgment?.Invoke(this);
                    onUpdateCombo?.Invoke(this);
                    onUpdateScore?.Invoke(this);
                    onDamage?.Invoke(this);
                    state = value;
                    return;
                }

                if (value == NoteState.Done)
                {
                    WasHoldingWhenTerminated = state == NoteState.InputBegan || state == NoteState.Input;
                    state = value;
                    OnUnSpawnNote();
                    return;
                }

                state = value;
                if (value == NoteState.Playing)
                {
                    OnSpawnNote();
                }
            }
        }

        public override Action<LiveNoteBase> OnUpdateScore
        {
            set
            {
                onUpdateScore = value;
                ApplyToChildNotes(note => note.OnUpdateScore = value);
            }
        }

        public override Action<LiveNoteBase> OnUpdateCombo
        {
            set
            {
                onUpdateCombo = value;
                ApplyToChildNotes(note => note.OnUpdateCombo = value);
            }
        }

        public override Action<LiveNoteBase> OnDamage
        {
            set
            {
                onDamage = value;
                ApplyToChildNotes(note => note.OnDamage = value);
            }
        }

        public override Action<LiveNoteBase> OnSpawn
        {
            set
            {
                onSpawn = value;
                ApplyToChildNotes(note => note.OnSpawn = value);
            }
        }

        public override Action<LiveNoteBase> OnJudgment
        {
            set
            {
                onJudgment = value;
                ApplyToChildNotes(note => note.OnJudgment = value);
            }
        }

        public override Action<LiveNoteBase> OnUnspawn
        {
            set
            {
                onUnspawn = value;
                ApplyToChildNotes(note => note.OnUnspawn = value);
            }
        }

        public override Func<(NoteResult result, NoteResultDescription description),
            (NoteResult result, NoteResultDescription description)> OnUpdateResult
        {
            set
            {
                onUpdateResult = value;
                ApplyToChildNotes(note => note.OnUpdateResult = value);
            }
        }

        public LiveLongNote()
        {
            NoteList = new List<LiveNoteBase> { this };
            ViewNoteList = new List<INote> { this };
            Category = NoteCategory.Long;
            Type = NoteType.Default;
        }

        public LiveLongNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, NoteType type, float speedRatio, float laneOffset,
            NoteLineType lineType = NoteLineType.Linear)
            : base(info, id, laneStart, laneEnd, category, speedRatio, laneOffset, type)
        {
            LineType = lineType;
            LaneOffset = laneOffset;
        }

        /// <summary>逐字对应原作 LongNote.AddConnectionNote（连接点：滑条路径上的中间音符）</summary>
        public void AddConnectionNote(LiveNoteBase connectionNote)
        {
            if (connectionNote == null)
            {
                return;
            }

            NoteList ??= new List<LiveNoteBase> { this };
            ViewNoteList ??= new List<INote> { this };
            connectionNote.speedRatio = speedRatio;
            NoteList.Add(connectionNote);
            ViewNoteList.Add(connectionNote);
            connectionNote.SetParentNote(this);
        }

        /// <summary>逐字对应原作 LongNote.AddHoldCombo（连击点：按住期间持续计 combo 的点）</summary>
        public void AddHoldCombo(LiveLongHoldCombo longHoldCombo)
        {
            if (longHoldCombo == null)
            {
                return;
            }

            NoteList ??= new List<LiveNoteBase> { this };
            longHoldCombo.speedRatio = speedRatio;
            NoteList.Add(longHoldCombo);
            longHoldCombo.SetParentNote(this);
        }

        /// <summary>逐字对应原作 LongNote.AddNoteListAndSetupChildNote（登记"尾巴"）</summary>
        public void AddNoteListAndSetupChildNote(LiveNoteBase note)
        {
            if (note == null)
            {
                return;
            }

            note.speedRatio = speedRatio;
            childNote = note;
            NoteList ??= new List<LiveNoteBase> { this };
            ViewNoteList ??= new List<INote> { this };
            NoteList.Add(note);
            ViewNoteList.Add(note);
            // 上游在 SUS 转换器里显式补这一步（尾巴要知道自己挂在哪条长按上，才会走 Long_End 窗口）；
            // 本工程在容器里一并做掉，避免调用方漏挂。
            note.SetParentNote(this);
            SortNoteList();
        }

        public override void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            this.offsetTime = offsetTime;
            if (State == NoteState.Done)
            {
                return;
            }

            if (State == NoteState.Wait)
            {
                State = NoteState.Playing;
            }

            if (State == NoteState.Last)
            {
                JudgeInfo = (NoteResult.Miss, NoteResultDescription.None);
                State = NoteState.Release;
                onDamage?.Invoke(this);
                onUpdateCombo?.Invoke(this);
                onJudgment?.Invoke(this);
            }

            Progress = CalcProgress(currentFrameInfo, offsetTime);

            if (NoteList != null)
            {
                for (var i = 1; i < NoteList.Count; i++)
                {
                    var note = NoteList[i];
                    var childOffsetTime = CalcTimeOffset?.Invoke(note) ?? offsetTime;
                    if (currentFrameInfo.time <= note.MusicScoreInfo.time - childOffsetTime)
                    {
                        break;
                    }

                    note.Excute(currentFrameInfo, childOffsetTime);
                }
            }

            INote currentNote = this;
            INote nextNote = childNote;
            if (ViewNoteList != null)
            {
                for (var i = 1; i < ViewNoteList.Count - 1; i++)
                {
                    var viewNote = ViewNoteList[i];
                    if (viewNote == null || viewNote.IsSkip)
                    {
                        continue;
                    }

                    if (viewNote.State != NoteState.Done)
                    {
                        nextNote = viewNote;
                        break;
                    }

                    currentNote = viewNote;
                }
            }

            LiveUtility.CalcExcuteNoteLane(
                this, ref currentNote, ref nextNote, ref currentFrameInfo, MusicScoreInfo, childNote);

            // 滑条：跨度按谱面 path 采样（与视图引导线同一条曲线）
            if (PathLaneSampler != null)
            {
                float laneSpan = Mathf.Max(0f, LaneEndF - LaneStartF);
                float progress01 = Mathf.Clamp01(CalcProgress(currentFrameInfo, offsetTime));
                float center = PathLaneSampler(progress01);
                LaneStartF = center - laneSpan * 0.5f;
                LaneEndF = center + laneSpan * 0.5f;
            }

            // 按住到尾巴 = 完成（本工程语义；见 CompleteTailWhileHolding 注释）
            if (CompleteTailWhileHolding && childNote != null && State != NoteState.Done
                && (State == NoteState.InputBegan || State == NoteState.Input)
                && childNote.State != NoteState.Done
                && currentFrameInfo.time >= childNote.MusicScoreInfo.time)
            {
                var tailTouch = new LiveTouch(0, 0, Vector2.zero, Vector3.zero,
                    LiveTouchPhase.Ended, childNote.MusicScoreInfo.time,
                    (LaneStartF + LaneEndF) * 0.5f, Vector2.zero, false);
                float tailLane = (LaneStartF + LaneEndF) * 0.5f;
                if (childNote.IsJudgment(ref tailTouch, tailLane)
                    && childNote.Judgment(ref tailTouch, tailLane))
                {
                    State = NoteState.Done;
                }
            }

            if (State == NoteState.Playing && currentFrameInfo.time - MusicScoreInfo.time > LiveConfig.JudgeTime)
            {
                State = NoteState.Last;
            }

            if ((State == NoteState.InputBegan || State == NoteState.Input)
                && lastInputFrame + LiveConfig.noteTypeJudgeData.MissMesh < LiveJudgeClock.FrameCount)
            {
                State = NoteState.Release;
            }
        }

        /// <summary>
        /// 逐字对应原作 LongNote.ForceTerminate：被子音符收尾时决定整条的最终结果
        /// （子音符活着/已判过 → JustPerfect，否则 Miss）。
        /// </summary>
        public virtual void ForceTerminate()
        {
            if (State != NoteState.Done)
            {
                JudgeInfo = childNote != null
                    && (childNote.Result > NoteResult.Pass || childNote.State == NoteState.Input)
                        ? (NoteResult.JustPerfect, NoteResultDescription.None)
                        : (NoteResult.Miss, NoteResultDescription.None);
                State = NoteState.Done;
            }
        }

        protected override void OnUnSpawnNote()
        {
            ConnectionNoteTerminate();
            onUnspawn?.Invoke(this);
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if ((State == NoteState.Playing || State == NoteState.Last) && JudgeInfo.result == NoteResult.None)
            {
                var judgeInfo = LiveConfig.CalculateLongStartNoteResult(this, touch.musicTime);
                ApplyJudgeInfo(judgeInfo);
                State = NoteState.InputBegan;
            }
            else
            {
                State = State == NoteState.Release ? NoteState.InputBegan : NoteState.Input;
            }

            lastInputFrame = LiveJudgeClock.FrameCount;
            OffsetJudgeTime = 0f;
            ConnectionNoteJudgment(ref touch, lane);

            if (childNote != null
                && LiveConfig.IsLongEndJudgeTime(childNote.OffsetJudgeTime)
                && childNote.IsJudgment(ref touch, lane)
                && childNote.Judgment(ref touch, lane))
            {
                State = NoteState.Done;
            }

            return true;
        }

        protected void ConnectionNoteJudgment(ref LiveTouch touch, float lane)
        {
            if (State != NoteState.InputBegan && State != NoteState.Input && State != NoteState.Release)
            {
                return;
            }

            if (NoteList == null)
            {
                return;
            }

            for (var i = 1; i < NoteList.Count - 1; i++)
            {
                var note = NoteList[i];
                if (note != null)
                {
                    note.Judgment(ref touch, lane);
                }
            }
        }

        protected void ConnectionNoteTerminate()
        {
            if (NoteList == null)
            {
                return;
            }

            for (var i = 1; i < NoteList.Count - 1; i++)
            {
                var note = NoteList[i];
                if (note != null && note.State != NoteState.Done)
                {
                    note.Terminate();
                }
            }
        }

        public override float LaneDistance(ref MusicScoreInfo currentFrameInfo, float inputLane)
        {
            var centerLane = (LaneStartF + LaneEndF) * 0.5f;
            var distance = centerLane - inputLane;
            return distance * distance;
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            var left = JudgeInfo.result == NoteResult.None ? JudgeLaneStart : JudgedLaneStart;
            var right = JudgeInfo.result == NoteResult.None ? JudgeLaneEnd : JudgedLaneEnd;
            if (left > lane || right < lane)
            {
                return false;
            }

            return LiveUtility.IsJudgmentTiming(LiveUtility.GetINoteToJudgeFrameType(this), OffsetJudgeTime)
                && ((State != NoteState.Playing && State != NoteState.Last) || touch.phase == LiveTouchPhase.Began);
        }

        /// <summary>逐字对应原作 LongNote.IsJudgmentChild。</summary>
        public virtual bool IsJudgmentChild(ref LiveTouch touch, float lane)
        {
            return State != NoteState.Done && JudgedLaneStart <= lane && JudgedLaneEnd >= lane;
        }

        public override bool AutoJudgment(MusicScoreInfo currentFrameInfo)
        {
            if (State == NoteState.Done || MusicScoreInfo.time > currentFrameInfo.time)
            {
                return false;
            }

            lastInputFrame = LiveJudgeClock.FrameCount;
            JudgeInfo = (NoteResult.Auto, NoteResultDescription.None);
            State = State == NoteState.Playing || State == NoteState.Last || State == NoteState.Release
                ? NoteState.InputBegan
                : NoteState.Input;
            OffsetJudgeTime = 0f;
            ConnectionNoteAutoJudgment(currentFrameInfo);

            if (childNote != null && childNote.AutoJudgment(currentFrameInfo))
            {
                State = NoteState.Done;
            }

            return true;
        }

        private void ConnectionNoteAutoJudgment(MusicScoreInfo currentFrameInfo)
        {
            if (State != NoteState.InputBegan && State != NoteState.Input)
            {
                return;
            }

            if (NoteList == null)
            {
                return;
            }

            for (var i = 1; i < NoteList.Count - 1; i++)
            {
                NoteList[i]?.AutoJudgment(currentFrameInfo);
            }
        }

        public override NoteResult CalcNoteResult(float musicTime)
        {
            return LiveConfig.CalculateLongStartNoteResult(this, musicTime).Item1;
        }

        public override void ResetNote()
        {
            base.ResetNote();
            lastInputFrame = 0;
            WasHoldingWhenTerminated = false;
        }

        /// <summary>逐字对应原作 LongNote.SortNoteList（子音符按时间排序）。</summary>
        public void SortNoteList()
        {
            if (NoteList != null)
            {
                NoteList.Sort((a, b) => a.MusicScoreInfo.time.CompareTo(b.MusicScoreInfo.time));
            }

            if (ViewNoteList != null)
            {
                ViewNoteList = ViewNoteList.OrderBy(note => note.MusicScoreInfo.time).ToList();
            }
        }

        private void ApplyToChildNotes(Action<LiveNoteBase> apply)
        {
            if (NoteList == null || apply == null)
            {
                return;
            }

            foreach (var note in NoteList)
            {
                if (!ReferenceEquals(note, this))
                {
                    apply(note);
                }
            }
        }
    }
}
