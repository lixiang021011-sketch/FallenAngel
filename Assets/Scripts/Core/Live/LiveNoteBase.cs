using System;
using System.Collections.Generic;

namespace FallenAngel.Live
{
    /// <summary>
    /// 判定音符的行为基类。对应 OpenSekai Sekai/Live/NoteBase.cs 的判定面
    /// （IsJudgment / Judgment / CalcNoteResult / LaneDistance / IsJudgmentTime），
    /// 加上各音符子类的覆盖点：
    ///
    /// - <see cref="LiveNormalNote"/>  = NormalNote：普通音符必须"按下边沿"（Began）；
    ///   作为长按尾（Category=Long）时只认"抬起/取消"。
    /// - <see cref="LiveFrictionNote"/> = FrictionNote：**不要求按下边沿**——手指落在跨度内、
    ///   时机在窗口内就是候选；正在进行中的手指会被 PickUpNotBeginInputNotes 拾取。
    ///   这条就是 PJSK"跟手"的核心语义。
    /// - <see cref="LiveLongNote"/>     = LongNote：头 + 子音符链，子音符挂在 NoteList 上。
    /// </summary>
    public abstract class LiveNoteBase : INote
    {
        protected NoteState state = NoteState.Wait;
        protected float offsetTime;

        private readonly List<INote> viewNoteList = new List<INote>();

        protected LiveNoteBase(
            MusicScoreInfo info,
            int id,
            int laneStart,
            int laneEnd,
            NoteCategory category,
            NoteType type,
            float laneOffset)
        {
            MusicScoreInfo = info;
            Id = id;
            LaneStart = laneStart;
            LaneEnd = laneEnd;
            LaneStartF = laneStart;
            LaneEndF = laneEnd;
            JudgeLaneStart = laneStart - laneOffset;
            JudgeLaneEnd = laneEnd + laneOffset;
            Category = category;
            Type = type;
            Direction = NoteDirection.Default;
            LineType = NoteLineType.Linear;
            NoteList = new List<LiveNoteBase>();
            // 上游的普通音符 NoteList 恒为 1 个元素（它自己）；判定循环按 Count==1 区分
            // "单个音符" 与 "长按/链"（见 LiveJudgeCore.AllocateInputJudgableEachNotes）。
            NoteList.Add(this);
        }

        public MusicScoreInfo MusicScoreInfo { get; protected set; }

        public float OffsetJudgeTime { get; protected set; }

        public int DefaultLeftLane { get; set; }

        public int DefaultRightLane { get; set; }

        public int LaneStart { get; set; }

        public int LaneEnd { get; set; }

        public float LaneStartF { get; set; }

        public float LaneEndF { get; set; }

        public float JudgeLaneStart { get; set; }

        public float JudgeLaneEnd { get; set; }

        public float Progress { get; protected set; }

        public virtual NoteState State
        {
            get { return state; }
            protected set { state = value; }
        }

        public (NoteResult result, NoteResultDescription description) JudgeInfo { get; protected set; }

        public NoteResult Result
        {
            get { return JudgeInfo.result; }
        }

        public NoteCategory Category { get; protected set; }

        public NoteType Type { get; protected set; }

        public NoteDirection Direction { get; set; }

        public NoteLineType LineType { get; protected set; }

        public INote ParentNote { get; protected set; }

        public INote ChildNote { get; protected set; }

        public INote PairNote { get; set; }

        public List<INote> ViewNoteList
        {
            get { return viewNoteList; }
            set
            {
                viewNoteList.Clear();
                if (value != null)
                {
                    viewNoteList.AddRange(value);
                }
            }
        }

        public bool IsSkip { get; protected set; }

        public int Id { get; protected set; }

        /// <summary>子音符链（长按/摩擦链用；普通音符只有一个元素或为空）。</summary>
        public List<LiveNoteBase> NoteList { get; private set; }

        public virtual bool HasJudgment
        {
            get { return !IsSkip; }
        }

        public virtual void SetParentNote(LiveNoteBase note)
        {
            ParentNote = note;
            if (note != null)
            {
                Type = note.Type == NoteType.Critical || Type == NoteType.Critical
                    ? NoteType.Critical
                    : NoteType.Default;
            }
        }

        public void SetSkip(bool skip)
        {
            IsSkip = skip;
        }

        public LiveNoteBase FirstNote
        {
            get { return NoteList != null && NoteList.Count > 0 ? NoteList[0] : this; }
        }

        public LiveNoteBase LastNote
        {
            get { return NoteList != null && NoteList.Count > 0 ? NoteList[NoteList.Count - 1] : this; }
        }

        /// <summary>逐字对应 NoteBase.IsJudgmentTime：OffsetJudgeTime 是否落在 Bad 窗口内。</summary>
        public virtual bool IsJudgmentTime()
        {
            var data = LiveJudgeWindows.GetJudgeTime(LiveJudgeWindows.GetINoteToJudgeFrameType(this));
            return OffsetJudgeTime >= -data.BadBeforeJudgeTime
                && OffsetJudgeTime <= data.BadAfterJudgeTime;
        }

        /// <summary>逐字对应 NoteBase.LaneDistance：到音符跨度中心的平方距离。</summary>
        public virtual float LaneDistance(float inputLane)
        {
            float centerLane = (LaneStartF + LaneEndF) * 0.5f;
            float distance = centerLane - inputLane;
            return distance * distance;
        }

        public virtual bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done || JudgeLaneStart > lane || JudgeLaneEnd < lane)
            {
                return false;
            }

            return LiveJudgeWindows.IsJudgmentTiming(
                LiveJudgeWindows.GetINoteToJudgeFrameType(this),
                MusicScoreInfo.time - touch.musicTime);
        }

        public virtual bool Judgment(ref LiveTouch touch, float lane)
        {
            return true;
        }

        public virtual NoteResult CalcNoteResult(float musicTime)
        {
            return LiveJudgeWindows
                .CalculateDefaultNoteResult(
                    LiveJudgeWindows.GetINoteToJudgeFrameType(this),
                    MusicScoreInfo.time - musicTime)
                .result;
        }

        /// <summary>判定推进（时间推进；Miss 在这里发生）。</summary>
        public virtual void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            this.offsetTime = offsetTime;
            if (State == NoteState.Done)
            {
                return;
            }

            OffsetJudgeTime = currentFrameInfo.time - MusicScoreInfo.time;
            if (State == NoteState.Wait)
            {
                State = NoteState.Playing;
            }

            if (State == NoteState.Last)
            {
                JudgeInfo = (NoteResult.Miss, NoteResultDescription.None);
                State = NoteState.Done;
                return;
            }

            var data = LiveJudgeWindows.GetJudgeTime(LiveJudgeWindows.GetINoteToJudgeFrameType(this));
            if (OffsetJudgeTime > data.BadAfterJudgeTime)
            {
                State = NoteState.Last;
            }
        }

        public void ResetNote()
        {
            state = NoteState.Wait;
            JudgeInfo = (NoteResult.None, NoteResultDescription.None);
            OffsetJudgeTime = 0f;
            Progress = 0f;
        }

        protected void ApplyResult((NoteResult result, NoteResultDescription description) judgeInfo)
        {
            JudgeInfo = judgeInfo;
        }
    }

    /// <summary>逐字对应 OpenSekai Sekai/Live/NormalNote.cs 的判定分支。</summary>
    public class LiveNormalNote : LiveNoteBase
    {
        public LiveNormalNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, NoteType type, float laneOffset)
            : base(info, id, laneStart, laneEnd, category, type, laneOffset)
        {
        }

        /// <summary>
        /// 逐字对应 NormalNote.SetParentNote：挂到长按之后变成"长按尾"（只认抬起/取消）。
        /// 必须是 override —— AddChild 是经由 NoteBase 引用调用它的。
        /// </summary>
        public override void SetParentNote(LiveNoteBase note)
        {
            base.SetParentNote(note);
            if (note is LiveLongNote)
            {
                Category = NoteCategory.Long;
            }
        }

        private static bool IsEndedOrCanceled(LiveTouchPhase phase)
        {
            return phase == LiveTouchPhase.Ended || phase == LiveTouchPhase.Canceled;
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done || JudgeLaneStart > lane || JudgeLaneEnd < lane)
            {
                return false;
            }

            float offset = MusicScoreInfo.time - touch.musicTime;
            if (!LiveJudgeWindows.IsJudgmentTiming(
                    LiveJudgeWindows.GetINoteToJudgeFrameType(this), offset))
            {
                return false;
            }

            // 普通音符只认按下边沿；作为长按尾（Category=Long）时只认抬起/取消。
            return (Category == NoteCategory.Normal && touch.phase == LiveTouchPhase.Began)
                || (Category == NoteCategory.Long && IsEndedOrCanceled(touch.phase));
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if ((Category == NoteCategory.Normal && touch.phase != LiveTouchPhase.Began)
                || (Category == NoteCategory.Long && !IsEndedOrCanceled(touch.phase)))
            {
                return false;
            }

            var frameType = LiveJudgeWindows.GetINoteToJudgeFrameType(this);
            var judgeInfo = ParentNote != null
                ? LiveJudgeWindows.CalculateLongEndNoteResult(frameType, MusicScoreInfo.time - touch.musicTime)
                : LiveJudgeWindows.CalculateDefaultNoteResult(frameType, MusicScoreInfo.time - touch.musicTime);

            ApplyResult(judgeInfo);
            State = NoteState.Done;
            return true;
        }

        public override NoteResult CalcNoteResult(float musicTime)
        {
            var frameType = LiveJudgeWindows.GetINoteToJudgeFrameType(this);
            return (ParentNote != null
                ? LiveJudgeWindows.CalculateLongEndNoteResult(frameType, MusicScoreInfo.time - musicTime)
                : LiveJudgeWindows.CalculateDefaultNoteResult(frameType, MusicScoreInfo.time - musicTime)).result;
        }
    }

    /// <summary>
    /// 逐字对应 OpenSekai Sekai/Live/FrictionNote.cs：**不要求按下边沿**。
    /// 手指落在跨度内 + 时机在窗口内即可成为候选/被拾取；<see cref="Judgment"/> 时标记 isTouched。
    /// </summary>
    public class LiveFrictionNote : LiveNoteBase
    {
        private bool isTouched;

        public LiveFrictionNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, NoteType type, float laneOffset)
            : base(info, id, laneStart, laneEnd, category, type, laneOffset)
        {
        }

        public LiveFrictionNote() : base(default, 0, 0, 0, NoteCategory.Friction, NoteType.Default, 0f)
        {
        }

        public bool IsTouched { get { return isTouched; } }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if (JudgeLaneStart > lane || JudgeLaneEnd < lane)
            {
                return false;
            }

            return LiveJudgeWindows.IsJudgmentTiming(
                LiveJudgeWindows.GetINoteToJudgeFrameType(this),
                MusicScoreInfo.time - touch.musicTime);
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Last)
            {
                OffsetJudgeTime = 0f;
                Progress = 1f;
            }
            else if (Progress < 1f)
            {
                // 只是"按上了"：继续按住，等进度走完 → JustPerfect。
                isTouched = true;
                State = NoteState.Input;
                return false;
            }

            ApplyResult((NoteResult.JustPerfect, NoteResultDescription.None));
            State = NoteState.Done;
            return true;
        }

        public override void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            base.Excute(currentFrameInfo, offsetTime);
            if (State == NoteState.Done)
            {
                return;
            }

            if ((State == NoteState.Input || isTouched) && Progress >= 1f)
            {
                OffsetJudgeTime = 0f;
                Progress = 1f;
                ApplyResult((NoteResult.JustPerfect, NoteResultDescription.None));
                State = NoteState.Done;
            }
        }
    }

    /// <summary>
    /// 逐字对应 OpenSekai Sekai/Live/LongNote.cs 的判定面：长按头 + 子音符链。
    /// 头判成功后子音符依次挂在 NoteList 上；尾判走 Long_End 窗口。
    /// </summary>
    public class LiveLongNote : LiveNoteBase
    {
        public LiveLongNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, NoteType type, float laneOffset)
            : base(info, id, laneStart, laneEnd, category, type, laneOffset)
        {
        }

        public void AddChild(LiveNoteBase child)
        {
            child.SetParentNote(this);
            NoteList.Add(child);
        }

        /// <summary>头判成功：头进入"按住中"状态。</summary>
        public void MarkHeadJudged()
        {
            ApplyResult((NoteResult.Perfect, NoteResultDescription.None));
            State = NoteState.Input;
        }
    }
}
