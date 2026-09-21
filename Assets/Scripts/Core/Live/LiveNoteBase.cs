using System;
using System.Collections.Generic;

namespace FallenAngel.Live
{
    /// <summary>
    /// 判定音符的行为基类。逐字对应 OpenSekai Sekai/Live/NoteBase.cs（判定面），
    /// 只把类型名换成 Live* 前缀（NoteBase → LiveNoteBase、LongNote → LiveLongNote、
    /// NoteBase[] 子表 → LiveNoteBase 子表），语义不变：
    ///
    /// · 状态机：Wait → Playing →（Last）→ Done，最后一步统一判 Miss（原作 Excute 的 State &gt;= Last 分支）；
    /// · 视图进度 Progress 由 offsetTime（音符在屏时长）算出，0 = 生成点、1 = 判定线；
    /// · 判定窗口一律查 LiveConfig/LiveUtility，音符自己不算窗口；
    /// · State 落到 Playing 触发 OnSpawnNote、落到 Done 触发 OnUnSpawnNote（原作回调面，
    ///   本工程的视图与计分层通过它拿到"音符开始/结束"事件）。
    ///
    /// 与上游的两处**接线级**差异（不是语义差异）：
    /// 1. CalcProgress 的当前时刻取 <see cref="MusicScoreInfo.time"/>（我们传进来的 currentFrameInfo），
    ///    与原作同一条公式；
    /// 2. 需要"当前帧号"的地方（长按 MissMesh / flick 的 lastInputFrame）读
    ///    <see cref="LiveJudgeClock.FrameCount"/> —— 由宿主每帧推进一步（原作直接读 Time.frameCount，
    ///    但批处理自检里帧号不推进，改成可注入的时钟才能既照抄语义又能无头验证）。
    /// </summary>
    public abstract class LiveNoteBase : INote
    {
        // ── 原作回调面（判定事件、计分、连击、伤害、生成/回收、结果改写）──
        protected Action<LiveNoteBase> onUpdateScore;
        protected Action<LiveNoteBase> onUpdateCombo;
        protected Action<LiveNoteBase> onDamage;
        protected Action<LiveNoteBase> onSpawn;
        protected Action<LiveNoteBase> onJudgment;
        protected Action<LiveNoteBase> onUnspawn;
        protected Func<(NoteResult result, NoteResultDescription description),
            (NoteResult result, NoteResultDescription description)> onUpdateResult;

        /// <summary>原作 CalcTimeOffset 委托：由宿主给出"这个音符应该在屏多久"</summary>
        public Func<LiveNoteBase, float> CalcTimeOffset;

        protected NoteState state;
        protected LiveLongNote parentNote;
        protected LiveNoteBase childNote;

        /// <summary>原作 NoteBase() 的空构造（派生类的默认构造走这里）</summary>
        protected LiveNoteBase()
        {
            NoteList = new List<LiveNoteBase> { this };
            ViewNoteList = new List<INote> { this };
        }

        protected LiveNoteBase(
            MusicScoreInfo info,
            int id,
            int laneStart,
            int laneEnd,
            NoteCategory category,
            float speedRatio,
            float laneOffset,
            NoteType type = NoteType.Default)
        {
            MusicScoreInfo = info;
            Id = id;
            DefaultLeftLane = laneStart;
            DefaultRightLane = laneEnd;
            LaneStart = laneStart;
            LaneEnd = laneEnd;
            LaneStartF = laneStart;
            LaneEndF = laneEnd;
            JudgeLaneStart = laneStart - laneOffset;
            JudgeLaneEnd = laneEnd + laneOffset;
            state = NoteState.Wait;
            Category = category;
            Type = type;
            this.speedRatio = speedRatio;
            NoteList = new List<LiveNoteBase> { this };
            ViewNoteList = new List<INote> { this };
        }

        public MusicScoreInfo MusicScoreInfo { get; set; }

        public float OffsetJudgeTime { get; set; }

        public int DefaultLeftLane { get; set; }

        public int DefaultRightLane { get; set; }

        public virtual int LaneStart { get; set; }

        public virtual int LaneEnd { get; set; }

        public virtual float LaneStartF { get; set; }

        public virtual float LaneEndF { get; set; }

        public virtual float JudgeLaneStart { get; set; }

        public virtual float JudgeLaneEnd { get; set; }

        public float Progress { get; protected set; }

        public virtual NoteState State
        {
            get { return state; }
            protected set
            {
                state = value;
                if (value == NoteState.Playing)
                {
                    OnSpawnNote();
                }
                else if (value == NoteState.Done)
                {
                    OnUnSpawnNote();
                }
            }
        }

        public (NoteResult result, NoteResultDescription description) JudgeInfo { get; protected set; }

        public NoteResult Result
        {
            get { return JudgeInfo.result; }
        }

        public NoteResultDescription Description
        {
            get { return JudgeInfo.description; }
        }

        public virtual NoteCategory Category { get; protected set; }

        public NoteType Type { get; set; }

        public NoteDirection Direction { get; set; }

        public NoteLineType LineType { get; set; }

        public INote ParentNote
        {
            get { return parentNote; }
        }

        public INote ChildNote
        {
            get { return childNote; }
        }

        public INote PairNote { get; private set; }

        public List<INote> ViewNoteList { get; set; }

        public List<LiveNoteBase> NoteList { get; set; }

        public virtual bool HasJudgment
        {
            get { return true; }
        }

        public virtual bool FinishFirstAction
        {
            get { return true; }
        }

        public virtual bool IsSkip { get; set; }

        public virtual int Id { get; set; }

        public float speedRatio { get; set; }

        public float offsetTime { get; set; }

        // ── 原作回调属性（长按子类会覆写以向下传播）──
        public virtual Action<LiveNoteBase> OnUpdateScore
        {
            set { onUpdateScore = value; }
        }

        public virtual Action<LiveNoteBase> OnUpdateCombo
        {
            set { onUpdateCombo = value; }
        }

        public virtual Action<LiveNoteBase> OnDamage
        {
            set { onDamage = value; }
        }

        public virtual Action<LiveNoteBase> OnJudgment
        {
            set { onJudgment = value; }
        }

        public virtual Action<LiveNoteBase> OnSpawn
        {
            set { onSpawn = value; }
        }

        public virtual Action<LiveNoteBase> OnUnspawn
        {
            set { onUnspawn = value; }
        }

        public virtual Func<(NoteResult result, NoteResultDescription description),
            (NoteResult result, NoteResultDescription description)> OnUpdateResult
        {
            set { onUpdateResult = value; }
        }

        /// <summary>原作 SetStateUnnotice：改状态但不触发回调（用于从存档/中途恢复）</summary>
        public void SetStateUnnotice(NoteState newState)
        {
            state = newState;
        }

        public virtual void SetParentNote(LiveLongNote note)
        {
            parentNote = note;
            if (note != null && note.Type == NoteType.Critical)
            {
                Type = note.Type;
            }
        }

        public void SetChildNote(LiveNoteBase note)
        {
            childNote = note;
        }

        public void SetPairNote(LiveNoteBase note)
        {
            PairNote = note;
        }

        public virtual void SetSkip(bool skip)
        {
            IsSkip = skip;
        }

        /// <summary>逐字对应原作 NoteBase.Excute（时间推进；过窗 → Last → 下一步判 Miss）。</summary>
        public virtual void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            this.offsetTime = offsetTime;
            if (State == NoteState.Done)
            {
                return;
            }

            if (State >= NoteState.Last)
            {
                JudgeInfo = (NoteResult.Miss, NoteResultDescription.None);
                State = NoteState.Done;
                return;
            }

            OffsetJudgeTime = currentFrameInfo.time - MusicScoreInfo.time;
            var progress = CalcProgress(currentFrameInfo, offsetTime);
            if (progress < 0f && state != NoteState.Playing)
            {
                return;
            }

            if (State == NoteState.Wait)
            {
                State = NoteState.Playing;
            }

            Progress = progress;
            if (OffsetJudgeTime > LiveConfig.JudgeTimeAfter)
            {
                State = NoteState.Last;
            }
        }

        /// <summary>逐字对应原作 NoteBase.CalcProgress：offsetTime = 音符在屏时长。</summary>
        protected float CalcProgress(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            if (offsetTime.Equals(0f))
            {
                return 1f;
            }

            return (currentFrameInfo.time + offsetTime - MusicScoreInfo.time) / offsetTime;
        }

        /// <summary>原作 Terminate：外部强制收尾（默认什么都不做，子类覆写）</summary>
        public virtual void Terminate()
        {
        }

        public virtual bool Judgment(ref LiveTouch touch, float lane)
        {
            return true;
        }

        protected virtual void OnSpawnNote()
        {
            onSpawn?.Invoke(this);
        }

        protected virtual void OnUnSpawnNote()
        {
            onJudgment?.Invoke(this);
            onUpdateCombo?.Invoke(this);
            onUpdateScore?.Invoke(this);
            onDamage?.Invoke(this);
            onUnspawn?.Invoke(this);
        }

        /// <summary>逐字对应原作 NoteBase.LaneDistance：到判定跨度中心的平方距离。</summary>
        public virtual float LaneDistance(ref MusicScoreInfo currentFrameInfo, float inputLane)
        {
            var centerLane = (LaneStartF + LaneEndF) * 0.5f;
            var distance = centerLane - inputLane;
            return distance * distance;
        }

        public virtual NoteResult CalcNoteResult(float musicTime)
        {
            return NoteResult.None;
        }

        public virtual bool IsJudgment(ref LiveTouch touch, float lane)
        {
            return true;
        }

        /// <summary>逐字对应原作 NoteBase.IsJudgmentTime。</summary>
        public virtual bool IsJudgmentTime()
        {
            return OffsetJudgeTime >= -LiveConfig.JudgeTimeBefore
                && OffsetJudgeTime <= LiveConfig.JudgeTimeAfter;
        }

        /// <summary>逐字对应原作 NoteBase.AutoJudgment（自动演奏用）。</summary>
        public virtual bool AutoJudgment(MusicScoreInfo currentFrameInfo)
        {
            if (State == NoteState.Done || MusicScoreInfo.time > currentFrameInfo.time)
            {
                return false;
            }

            JudgeInfo = (NoteResult.Auto, NoteResultDescription.None);
            State = NoteState.Done;
            return true;
        }

        public virtual void ResetNote()
        {
            State = NoteState.Wait;
            OffsetJudgeTime = 0f;
            Progress = 0f;
            JudgeInfo = (NoteResult.None, NoteResultDescription.None);
        }

        /// <summary>把判定结果写进 JudgeInfo（含原作的 OnUpdateResult 改写口）</summary>
        protected void ApplyJudgeInfo((NoteResult result, NoteResultDescription description) judgeInfo)
        {
            JudgeInfo = onUpdateResult != null ? onUpdateResult(judgeInfo) : judgeInfo;
        }
    }

    /// <summary>
    /// 判定用"帧号"来源。原作直接读 Time.frameCount；本工程把它抽成可注入的时钟：
    /// 宿主（LiveScoreRuntime/JudgeManager）每帧推进一步，自检也能显式推进，
    /// 语义与原作一致（长按的 MissMesh 释放判定、flick 的 lastInputFrame 都读它）。
    /// </summary>
    public static class LiveJudgeClock
    {
        private static int frameCount;

        public static int FrameCount
        {
            get { return frameCount; }
        }

        /// <summary>每帧开始时推进（等价于引擎的 Time.frameCount++）</summary>
        public static void Advance()
        {
            frameCount++;
        }

        public static void Reset()
        {
            frameCount = 0;
        }
    }
}
