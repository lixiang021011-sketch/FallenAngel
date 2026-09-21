using System;
using System.Collections.Generic;

namespace FallenAngel.Live
{
    /// <summary>
    /// 谱面运行时。逐字移植 OpenSekai Sekai/Core/Live/LiveLogic.cs 的**时间推进部分**：
    ///   · SortNotes + noteStartIndex 扫描指针（已经是 Done 的音符不再回头）
    ///   · speedRatio != 1 的音符走 highSpeedNoteArray（原作的两条扫描轨道）
    ///   · 每个音符：算在屏时长 → 太远就 break（谱面有序，不必全表扫）→ Excute → 登记进待判表
    ///   · judgmentNoteList 交给 <see cref="LiveJudgeCore"/> 跑五步判定
    ///   · AutoJudgment（原作 OnAutoInput，自动演奏 / 演示用）
    ///
    /// 没有移植的部分（属于表现层，本工程的视图自己管）：LiveViewBase / LiveViewExt / 技能 / 分数槽 / 事件轨。
    /// </summary>
    public class LiveScoreRuntime
    {
        /// <summary>原作 LiveLogic.TargetFrameTime：判定"太远"时预留一帧</summary>
        public const float TargetFrameTime = 1f / 60f;

        private LiveNoteBase[] noteArray = Array.Empty<LiveNoteBase>();
        private LiveNoteBase[] highSpeedNoteArray = Array.Empty<LiveNoteBase>();
        private int noteStartIndex;
        private int highSpeedNoteStartIndex;

        private readonly List<LiveNoteBase> judgmentNoteList = new List<LiveNoteBase>();

        /// <summary>本帧待判音符表（判定五步的输入；结构对齐原作 LiveLogic.judgmentNoteList）</summary>
        public IReadOnlyList<LiveNoteBase> JudgmentNoteList
        {
            get { return judgmentNoteList; }
        }

        public LiveJudgeCore JudgeCore { get; } = new LiveJudgeCore();

        /// <summary>音符在屏时长（原作 noteDisplayTimeOffset：由下落速度设置算出）</summary>
        public float NoteDisplayTimeOffset = 2f;

        /// <summary>谱面根音符（视图按它生成/回收）</summary>
        public IReadOnlyList<LiveNoteBase> Notes
        {
            get { return noteArray; }
        }

        public MusicScoreInfo CurrentFrameInfo { get; private set; }

        public bool IsAllFinished
        {
            get { return noteStartIndex >= noteArray.Length && highSpeedNoteStartIndex >= highSpeedNoteArray.Length; }
        }

        /// <summary>逐字对应原作 LiveLogic.Setup 里的谱面初始化（排序 + 分高速表）。</summary>
        public void Setup(IReadOnlyList<LiveNoteBase> notes)
        {
            noteArray = SortNotes(notes);
            var highSpeed = new List<LiveNoteBase>();
            for (var i = 0; i < noteArray.Length; i++)
            {
                var note = noteArray[i];
                if (note != null && Math.Abs(note.speedRatio - 1f) > 0.0001f)
                {
                    highSpeed.Add(note);
                }
            }

            highSpeedNoteArray = highSpeed.ToArray();
            noteStartIndex = 0;
            highSpeedNoteStartIndex = 0;
            judgmentNoteList.Clear();

            // 子音符的在屏时长走同一个计算口（原作 LiveLogic 在每个音符上挂 CalcTimeOffset 委托）
            for (var i = 0; i < noteArray.Length; i++)
            {
                BindTimeOffset(noteArray[i]);
            }
        }

        /// <summary>给音符（含子音符链）挂上"在屏时长"计算口——原作 LongNote.Excute 会按它算子音符的进度。</summary>
        private void BindTimeOffset(LiveNoteBase note)
        {
            if (note == null)
            {
                return;
            }

            note.CalcTimeOffset = CalcTimeOffset;
            if (note.NoteList == null)
            {
                return;
            }

            for (var i = 0; i < note.NoteList.Count; i++)
            {
                var child = note.NoteList[i];
                if (child != null && !ReferenceEquals(child, note))
                {
                    child.CalcTimeOffset = CalcTimeOffset;
                }
            }
        }

        public void Reset()
        {
            noteStartIndex = 0;
            highSpeedNoteStartIndex = 0;
            judgmentNoteList.Clear();
            JudgeCore.Reset();
            LiveJudgeClock.Reset();
        }

        private static LiveNoteBase[] SortNotes(IReadOnlyList<LiveNoteBase> notes)
        {
            if (notes == null || notes.Count == 0)
            {
                return Array.Empty<LiveNoteBase>();
            }

            var array = new LiveNoteBase[notes.Count];
            for (var i = 0; i < notes.Count; i++)
            {
                array[i] = notes[i];
            }

            Array.Sort(array, (a, b) =>
            {
                if (a == null)
                {
                    return b == null ? 0 : 1;
                }

                if (b == null)
                {
                    return -1;
                }

                return a.MusicScoreInfo.time.CompareTo(b.MusicScoreInfo.time);
            });
            return array;
        }

        /// <summary>
        /// 推进一帧：帧号 +1、清空待判表、按谱面顺序 Excute 所有"还没走完"的音符。
        /// 对应原作 LiveLogic.OnUpdate 里的 UpdateNote（跳过事件轨/技能/分数槽）。
        /// </summary>
        public void Update(MusicScoreInfo currentFrameInfo)
        {
            CurrentFrameInfo = currentFrameInfo;
            LiveJudgeClock.Advance();
            judgmentNoteList.Clear();

            UpdateNormalNotes(currentFrameInfo);
            UpdateHighSpeedNotes(currentFrameInfo);
        }

        private void UpdateNormalNotes(MusicScoreInfo currentFrameInfo)
        {
            for (var i = noteStartIndex; i < noteArray.Length; i++)
            {
                var note = noteArray[i];
                if (note == null)
                {
                    if (i == noteStartIndex)
                    {
                        noteStartIndex++;
                    }
                    continue;
                }

                if (note.State == NoteState.Done)
                {
                    if (i == noteStartIndex)
                    {
                        noteStartIndex++;
                    }
                    continue;
                }

                if (Math.Abs(note.speedRatio - 1f) > 0.0001f)
                {
                    if (i == noteStartIndex)
                    {
                        noteStartIndex++;
                    }
                    continue;
                }

                var offsetTime = CalcTimeOffset(note);
                if (IsJudgeTimingTimeOffsetHighSpeedNote(offsetTime, note, currentFrameInfo))
                {
                    break;
                }
                if (offsetTime < 0f && currentFrameInfo.time >= note.MusicScoreInfo.time)
                {
                    offsetTime = -offsetTime;
                }

                note.Excute(currentFrameInfo, offsetTime);
                if (note.State == NoteState.Done && i == noteStartIndex)
                {
                    noteStartIndex++;
                }
                else if (note.State != NoteState.Done)
                {
                    UpdateJudgmentNoteArray(note);
                }
            }
        }

        private void UpdateHighSpeedNotes(MusicScoreInfo currentFrameInfo)
        {
            for (var i = highSpeedNoteStartIndex; i < highSpeedNoteArray.Length; i++)
            {
                var note = highSpeedNoteArray[i];
                if (note == null)
                {
                    if (i == highSpeedNoteStartIndex)
                    {
                        highSpeedNoteStartIndex++;
                    }
                    continue;
                }

                if (note.State == NoteState.Done)
                {
                    if (i == highSpeedNoteStartIndex)
                    {
                        highSpeedNoteStartIndex++;
                    }
                    continue;
                }

                var offsetTime = currentFrameInfo.time >= note.MusicScoreInfo.time
                    ? NoteDisplayTimeOffset
                    : CalcTimeOffset(note);
                if (IsJudgeTimingTimeOffsetNote(offsetTime, note, currentFrameInfo))
                {
                    break;
                }

                note.Excute(currentFrameInfo, offsetTime);
                if (note.State == NoteState.Done && i == highSpeedNoteStartIndex)
                {
                    highSpeedNoteStartIndex++;
                }
                else if (note.State != NoteState.Done)
                {
                    UpdateJudgmentNoteArray(note);
                }
            }
        }

        private bool IsJudgeTimingTimeOffsetHighSpeedNote(
            float timeOffset, LiveNoteBase note, MusicScoreInfo currentFrameInfo)
        {
            return note != null && note.State != NoteState.Playing
                && IsJudgeTimingTimeOffsetNote(timeOffset, note, currentFrameInfo);
        }

        private bool IsJudgeTimingTimeOffsetNote(
            float timeOffset, LiveNoteBase note, MusicScoreInfo currentFrameInfo)
        {
            if (note == null || Math.Abs(timeOffset) < 0.0001f)
            {
                return false;
            }

            return currentFrameInfo.time + timeOffset + TargetFrameTime <= note.MusicScoreInfo.time;
        }

        private void UpdateJudgmentNoteArray(LiveNoteBase note)
        {
            if (note != null && note.HasJudgment)
            {
                judgmentNoteList.Add(note);
            }
        }

        /// <summary>本帧输入的判定（对应原作 LiveLogic.OnInput 的输入登记 + Judgment 五步）。</summary>
        public void Judge(IReadOnlyList<LiveTouch> touches)
        {
            if (touches != null)
            {
                for (var i = 0; i < touches.Count; i++)
                {
                    JudgeCore.PushInput(touches[i]);
                }
            }

            JudgeCore.CurrentFrameInfo = CurrentFrameInfo;
            JudgeCore.Judgment(judgmentNoteList);
            JudgeCore.EndFrame();
        }

        /// <summary>逐字对应原作 LiveLogic.OnAutoInput：到点的音符自动判掉（演示/回归用）。</summary>
        public void AutoPlay(MusicScoreInfo currentFrameInfo)
        {
            var index = noteStartIndex;
            while (index < noteArray.Length)
            {
                var note = noteArray[index];
                if (note == null || note.State == NoteState.Done)
                {
                    index++;
                    continue;
                }

                if (note.MusicScoreInfo.time > currentFrameInfo.time)
                {
                    return;
                }

                note.AutoJudgment(currentFrameInfo);
                index++;
            }
        }

        /// <summary>音符在屏时长 = 下落时长 / 音符流速（对应原作 CalcTimeOffset 的速度比值部分）。</summary>
        public float CalcTimeOffset(LiveNoteBase note)
        {
            if (note == null)
            {
                return 0f;
            }

            var speedRatio = Math.Abs(note.speedRatio) < 0.0001f ? 1f : note.speedRatio;
            return NoteDisplayTimeOffset / speedRatio;
        }
    }
}
