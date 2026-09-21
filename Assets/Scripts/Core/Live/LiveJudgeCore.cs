using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>触摸相位（与 EnhancedTouch 的 TouchPhase 同构）。</summary>
    public enum LiveTouchPhase
    {
        Began = 0,
        Moved = 1,
        Stationary = 2,
        Ended = 3,
        Canceled = 4
    }

    /// <summary>
    /// 一次输入（= 一根手指在某一时刻的状态）。对应 OpenSekai 的 LiveTouch：
    /// 带 touchId / fingerId / 相位 / **音乐时间戳** / 由屏幕坐标换算出的**小数轨道**。
    /// </summary>
    public struct LiveTouch
    {
        public int touchId;
        public int fingerId;
        public LiveTouchPhase phase;
        public float musicTime;
        public float lane;          // <0 = 落在判定区之外
        public Vector2 screenPosition;

        public LiveTouch(int touchId, int fingerId, LiveTouchPhase phase, float musicTime, float lane, Vector2 screenPosition)
        {
            this.touchId = touchId;
            this.fingerId = fingerId;
            this.phase = phase;
            this.musicTime = musicTime;
            this.lane = lane;
            this.screenPosition = screenPosition;
        }
    }

    /// <summary>
    /// 逐字对应 OpenSekai LiveLogic.InputTmp：本帧某个输入的候选音符表。
    /// </summary>
    public class LiveTouchInput
    {
        public LiveTouch Touch;
        public float Lane;
        public readonly List<LiveNoteBase> CandidateNotes = new List<LiveNoteBase>();

        public LiveTouchInput()
        {
        }

        public LiveTouchInput(LiveTouchInput source)
        {
            Reset(source.Touch, source.Lane);
        }

        public void Reset(LiveTouch touch, float lane)
        {
            Touch = touch;
            Lane = lane;
            CandidateNotes.Clear();
        }
    }

    /// <summary>
    /// 判定主循环。逐字移植 OpenSekai Sekai/Core/Live/LiveLogic.cs 的判定流水线：
    ///
    ///     Judgment()
    ///       ├─ AllocateInputJudgableEachNotes   每个输入 × 每个待判音符 → 候选
    ///       ├─ OverlapJudgmentSorting
    ///       │    └─ ResolveOverlappingCandidatesAcrossInputs  同一音符被多指竞争 → 按轨道距离归属
    ///       ├─ LeaveNotesFastGeneration         同一根手指多个候选 → 留"该判的那个"
    ///       ├─ PickUpNotBeginInputNotes         手指已按住、音符随后进窗 → 拾取（跟手的关键）
    ///       └─ EvaluateJudgment                 逐个应用；没判到东西的输入 → Unpicked 反馈
    ///
    /// 与 OpenSekai 的唯一结构差异：屏幕坐标 → 轨道的换算（他们读 LiveConfig.JudgmentPositions 折线，
    /// 我们通过 <see cref="LaneResolver"/> 注入），以及判定结果的输出（他们回调到 View，我们抛事件）。
    /// 判定规则本身完全一致，不做"自由发挥"。
    /// </summary>
    public class LiveJudgeCore
    {
        /// <summary>屏幕坐标 → 小数轨道。返回 &lt;0 表示落在判定区之外。由宿主注入（对应 GetJudgmentLane）。</summary>
        public Func<Vector2, bool, float> LaneResolver;

        /// <summary>某个音符被判掉时触发（供宿主计分/连击/特效）。</summary>
        public event Action<LiveNoteBase> OnJudged;

        /// <summary>有输入但没有判到任何音符（对应 LiveViewExt.Unpicked）。</summary>
        public event Action<int, LiveTouch> OnUnpicked;

        private readonly List<LiveTouchInput> activeInputList = new List<LiveTouchInput>();
        private readonly List<LiveNoteBase> connectionNoteList = new List<LiveNoteBase>();
        private readonly List<int> usedFingerList = new List<int>();

        /// <summary>
        /// 每根手指"上一次判掉普通音符"的音乐时间（对应 InputedNormalNoteTouch）。
        /// 用途：同一根手指 1/6 秒内不再判下一个普通音符，除非结果优于 Great。
        /// </summary>
        private readonly Dictionary<int, float> inputedNormalNoteTouch = new Dictionary<int, float>();

        /// <summary>上一帧的帧内数据（供 LaneDistance 等需要"当前帧"位置的口子；结构对齐上游）。</summary>
        public MusicScoreInfo CurrentFrameInfo;

        public int ActiveInputCount { get { return activeInputList.Count; } }

        public IReadOnlyList<LiveTouchInput> ActiveInputs { get { return activeInputList; } }

        public void Reset()
        {
            activeInputList.Clear();
            connectionNoteList.Clear();
            usedFingerList.Clear();
            inputedNormalNoteTouch.Clear();
        }

        /// <summary>登记本帧的一次输入（按下/移动/按住/抬起）。</summary>
        public LiveTouchInput PushInput(in LiveTouch touch)
        {
            var input = new LiveTouchInput();
            input.Reset(touch, touch.lane);
            activeInputList.Add(input);
            return input;
        }

        /// <summary>跑一次判定循环（对应 LiveLogic.Judgment()）。</summary>
        public void Judgment(List<LiveNoteBase> judgmentNotes)
        {
            if (judgmentNotes == null)
            {
                return;
            }

            connectionNoteList.Clear();
            AllocateInputJudgableEachNotes(judgmentNotes);
            OverlapJudgmentSorting();
            LeaveNotesFastGeneration();
            PickUpNotBeginInputNotes();
            EvaluateJudgment(judgmentNotes);
        }

        /// <summary>本帧结束后清理（对应 activeInputList.Clear() 之后的收尾）。</summary>
        public void EndFrame()
        {
            activeInputList.Clear();
        }

        // ── ① 候选分配 ────────────────────────────────────────────────
        private void AllocateInputJudgableEachNotes(List<LiveNoteBase> judgmentNotes)
        {
            for (int noteIndex = 0; noteIndex < judgmentNotes.Count; noteIndex++)
            {
                LiveNoteBase judgmentNote = judgmentNotes[noteIndex];
                if (judgmentNote == null)
                {
                    continue;
                }

                bool isConnection = true;
                bool isJudged = false;
                for (int inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
                {
                    LiveTouchInput activeInput = activeInputList[inputIndex];
                    LiveNoteBase note = judgmentNote;
                    float lane = ConvertLaneBy(ref note, ref activeInput);
                    if (lane >= 0f && note.IsJudgment(ref activeInput.Touch, lane))
                    {
                        isJudged = true;
                        int touchId = activeInput.Touch.touchId;
                        LiveNoteBase lastNote = GetLastNote(judgmentNote);
                        if (judgmentNote.NoteList != null && judgmentNote.NoteList.Count == 1)
                        {
                            AddCheckSingleInputedNormalNoteTouch(
                                ref judgmentNote, ref lastNote, ref activeInput,
                                ref isConnection, ref isJudged, ref touchId);
                        }
                        else
                        {
                            AddCheckLongInputedNormalNoteTouch(
                                ref judgmentNote, ref lastNote, ref activeInput,
                                ref isConnection, ref isJudged, ref touchId);
                        }

                        continue;
                    }

                    LiveNoteBase firstNote = GetFirstNote(judgmentNote);
                    if (firstNote != null && TryAddConnectionCandidate(firstNote, activeInput))
                    {
                        isConnection = true;
                        isJudged = true;
                    }
                }

                if (isJudged && isConnection)
                {
                    connectionNoteList.Add(judgmentNote);
                }
            }
        }

        private void AddCheckSingleInputedNormalNoteTouch(
            ref LiveNoteBase judgmentNote, ref LiveNoteBase note, ref LiveTouchInput activeInput,
            ref bool isConnection, ref bool isJudged, ref int touchId)
        {
            // 摩擦类：本身即时成立（不需要"按下边沿"），不进候选表。
            if (judgmentNote is LiveFrictionNote)
            {
                float lane2 = ConvertLaneBy(ref note, ref activeInput);
                if (judgmentNote.IsJudgment(ref activeInput.Touch, lane2))
                {
                    isConnection = true;
                    isJudged = true;
                    return;
                }
            }

            if (note is LiveFrictionNote)
            {
                float lane2 = ConvertLaneBy(ref note, ref activeInput);
                if (note.IsJudgment(ref activeInput.Touch, lane2))
                {
                    isConnection = true;
                    isJudged = true;
                    return;
                }
            }

            activeInput.CandidateNotes.Add(judgmentNote);
            isConnection = false;
            inputedNormalNoteTouch[touchId] = activeInput.Touch.musicTime;
        }

        private void AddCheckLongInputedNormalNoteTouch(
            ref LiveNoteBase judgmentNote, ref LiveNoteBase note, ref LiveTouchInput activeInput,
            ref bool isConnection, ref bool isJudged, ref int touchId)
        {
            float lane = ConvertLaneBy(ref note, ref activeInput);
            bool hadInput = inputedNormalNoteTouch.ContainsKey(touchId);
            bool isLastNoteJudgment = note != null && note.IsJudgment(ref activeInput.Touch, lane);
            if (hadInput)
            {
                if (isLastNoteJudgment)
                {
                    isConnection = false;
                    // 同一根手指 1/6 秒内不再判下一个普通音符，除非这次结果优于 Great。
                    if (note.CalcNoteResult(activeInput.Touch.musicTime) > NoteResult.Great
                        || activeInput.Touch.musicTime - inputedNormalNoteTouch[touchId] > 1f / 6f)
                    {
                        activeInput.CandidateNotes.Add(judgmentNote);
                        return;
                    }
                }
            }
            else if (isLastNoteJudgment)
            {
                activeInput.CandidateNotes.Add(judgmentNote);
                isConnection = false;
                return;
            }

            note = GetFirstNote(judgmentNote);
            lane = ConvertLaneBy(ref note, ref activeInput);

            if (note is LiveLongNote && note.IsJudgment(ref activeInput.Touch, lane))
            {
                isConnection = true;
                isJudged = true;
                return;
            }

            if (note != null && note.Result == NoteResult.None)
            {
                lane = ConvertLaneBy(ref note, ref activeInput);
                if (note.IsJudgment(ref activeInput.Touch, lane))
                {
                    activeInput.CandidateNotes.Add(judgmentNote);
                    isConnection = false;
                    return;
                }
            }
        }

        // ── ② 重叠归属 ────────────────────────────────────────────────
        private void OverlapJudgmentSorting()
        {
            ResolveOverlappingCandidatesAcrossInputs();
        }

        private void ResolveOverlappingCandidatesAcrossInputs()
        {
            for (int inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                LiveTouchInput input = activeInputList[inputIndex];
                if (input.CandidateNotes.Count == 0)
                {
                    continue;
                }

                for (int otherInputIndex = inputIndex - 1; otherInputIndex >= 0; otherInputIndex--)
                {
                    LiveTouchInput otherInput = activeInputList[otherInputIndex];
                    if (otherInput.CandidateNotes.Count == 0)
                    {
                        continue;
                    }

                    for (int candidateIndex = input.CandidateNotes.Count - 1; candidateIndex >= 0; candidateIndex--)
                    {
                        LiveNoteBase candidate = input.CandidateNotes[candidateIndex];
                        int otherCandidateIndex = otherInput.CandidateNotes.IndexOf(candidate);
                        if (candidate == null || otherCandidateIndex < 0)
                        {
                            continue;
                        }

                        LiveNoteBase inputNote = ResolveJudgmentNote(candidate, input);
                        LiveNoteBase otherNote = ResolveJudgmentNote(candidate, otherInput);
                        float inputDistance = inputNote != null
                            ? inputNote.LaneDistance(ConvertLaneBy(ref inputNote, ref input))
                            : float.MaxValue;
                        float otherDistance = otherNote != null
                            ? otherNote.LaneDistance(ConvertLaneBy(ref otherNote, ref otherInput))
                            : float.MaxValue;

                        if (otherDistance <= inputDistance)
                        {
                            input.CandidateNotes.RemoveAt(candidateIndex);
                        }
                        else
                        {
                            otherInput.CandidateNotes.RemoveAt(otherCandidateIndex);
                        }
                    }
                }
            }
        }

        // ── ③ 同指去重 ────────────────────────────────────────────────
        private void LeaveNotesFastGeneration()
        {
            for (int inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                LiveTouchInput input = activeInputList[inputIndex];
                for (int i = input.CandidateNotes.Count - 1; i >= 0; i--)
                {
                    if (input.CandidateNotes.Count < 2)
                    {
                        break;
                    }

                    LiveNoteBase first = ResolveFastGenerationNote(input.CandidateNotes[0], input);
                    LiveNoteBase current = ResolveFastGenerationNote(input.CandidateNotes[i], input);
                    if (first == null || current == null)
                    {
                        continue;
                    }

                    if (IsSameJudgmentTime(first, current))
                    {
                        float firstDistance = first.LaneDistance(ConvertLaneBy(ref first, ref input));
                        float currentDistance = current.LaneDistance(ConvertLaneBy(ref current, ref input));
                        if (firstDistance < currentDistance)
                        {
                            input.CandidateNotes.RemoveAt(i);
                        }
                        else
                        {
                            input.CandidateNotes.RemoveAt(0);
                        }
                    }
                    else if (current.CalcNoteResult(input.Touch.musicTime) >= NoteResult.Great
                        && first.CalcNoteResult(input.Touch.musicTime) <= NoteResult.Good)
                    {
                        input.CandidateNotes.RemoveAt(0);
                    }
                    else if (first.MusicScoreInfo.time < current.MusicScoreInfo.time)
                    {
                        input.CandidateNotes.RemoveAt(i);
                    }
                    else
                    {
                        input.CandidateNotes.RemoveAt(0);
                    }

                    i = input.CandidateNotes.Count;
                }
            }
        }

        // ── ④ 拾取（跟手的关键）────────────────────────────────────────
        private void PickUpNotBeginInputNotes()
        {
            for (int inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                LiveTouchInput input = activeInputList[inputIndex];
                for (int noteIndex = connectionNoteList.Count - 1; noteIndex >= 0; noteIndex--)
                {
                    LiveNoteBase note = connectionNoteList[noteIndex];
                    float lane = ConvertLaneBy(ref note, ref input);
                    if (note != null && lane >= 0f && note.IsJudgment(ref input.Touch, lane))
                    {
                        input.CandidateNotes.Add(note);
                        connectionNoteList.RemoveAt(noteIndex);
                    }
                }
            }
        }

        // ── ⑤ 应用 ────────────────────────────────────────────────────
        private void EvaluateJudgment(List<LiveNoteBase> judgmentNotes)
        {
            usedFingerList.Clear();
            for (int inputIndex = 0; inputIndex < activeInputList.Count; inputIndex++)
            {
                LiveTouchInput input = activeInputList[inputIndex];
                bool hasCandidate = input.CandidateNotes.Count > 0;
                for (int candidateIndex = 0; candidateIndex < input.CandidateNotes.Count; candidateIndex++)
                {
                    LiveNoteBase note = input.CandidateNotes[candidateIndex];
                    if (note == null)
                    {
                        continue;
                    }

                    float lane = ConvertLaneBy(ref note, ref input);
                    note.Judgment(ref input.Touch, lane);
                    if (note.Result == NoteResult.None || note.Result == NoteResult.Bad)
                    {
                        inputedNormalNoteTouch[input.Touch.touchId] = input.Touch.musicTime;
                    }

                    judgmentNotes.Remove(note);
                    OnJudged?.Invoke(note);
                }

                if (hasCandidate)
                {
                    input.CandidateNotes.Clear();
                    usedFingerList.Add(input.Touch.fingerId);
                }
            }

            for (int inputIndex = 0; inputIndex < activeInputList.Count; inputIndex++)
            {
                LiveTouchInput input = activeInputList[inputIndex];
                bool used = false;
                for (int i = usedFingerList.Count - 1; i >= 0; i--)
                {
                    used |= usedFingerList[i] == input.Touch.fingerId;
                }

                if (!used && input.Lane >= 0f)
                {
                    OnUnpicked?.Invoke(Mathf.RoundToInt(input.Lane), input.Touch);
                }

                if (IsEndedOrCanceled(input.Touch.phase))
                {
                    inputedNormalNoteTouch.Remove(input.Touch.touchId);
                }
            }
        }

        // ── 辅助 ──────────────────────────────────────────────────────
        private float ConvertLaneBy(ref LiveNoteBase note, ref LiveTouchInput input)
        {
            if (note == null || input == null)
            {
                return input == null ? -1f : input.Lane;
            }

            // 左向甩键：用"距判定线的实际位置"重新取轨道（对应 ConvertLaneBy 的 Flick 分支）。
            if ((note.Category == NoteCategory.Flick || note.Category == NoteCategory.FrictionFlick)
                && note.Direction == NoteDirection.Left)
            {
                float resolved = ResolveLaneFromPosition(input.Touch.screenPosition, true);
                return resolved >= 0f ? resolved : input.Lane;
            }

            return input.Lane;
        }

        private float ResolveLaneFromPosition(Vector2 position, bool checkJudgmentY)
        {
            if (LaneResolver == null)
            {
                return -1f;
            }

            return LaneResolver(position, checkJudgmentY);
        }

        private bool TryAddConnectionCandidate(LiveNoteBase note, LiveTouchInput input)
        {
            // 逐字对应 LiveLogic.TryAddConnectionCandidate：只有长按头能被"已按住的手指"拾取。
            if (!(note is LiveLongNote))
            {
                return false;
            }

            float lane = ConvertLaneBy(ref note, ref input);
            if (lane < 0f || !note.IsJudgment(ref input.Touch, lane))
            {
                return false;
            }

            input.CandidateNotes.Add(note);
            return true;
        }

        private LiveNoteBase ResolveJudgmentNote(LiveNoteBase note, LiveTouchInput input)
        {
            if (note == null || note.NoteList == null || note.NoteList.Count == 1)
            {
                return note;
            }

            if (note.Result != NoteResult.None || input.Touch.phase != LiveTouchPhase.Began)
            {
                return GetLastNote(note);
            }

            return GetFirstNote(note);
        }

        private LiveNoteBase ResolveFastGenerationNote(LiveNoteBase note, LiveTouchInput input)
        {
            if (note == null || note.NoteList == null || note.NoteList.Count == 1)
            {
                return note;
            }

            LiveNoteBase resolved = note.Result != NoteResult.None || input.Touch.phase != LiveTouchPhase.Began
                ? GetLastNote(note)
                : GetFirstNote(note);
            float lane = ConvertLaneBy(ref resolved, ref input);
            if (resolved == null || lane < 0f || !resolved.IsJudgment(ref input.Touch, lane))
            {
                return note;
            }

            return resolved;
        }

        private static LiveNoteBase GetFirstNote(LiveNoteBase note)
        {
            return note != null && note.NoteList != null && note.NoteList.Count > 0
                ? note.NoteList[0]
                : note;
        }

        private static LiveNoteBase GetLastNote(LiveNoteBase note)
        {
            return note != null && note.NoteList != null && note.NoteList.Count > 0
                ? note.NoteList[note.NoteList.Count - 1]
                : note;
        }

        private static bool IsSameJudgmentTime(LiveNoteBase a, LiveNoteBase b)
        {
            return a != null && b != null && a.MusicScoreInfo.time.Equals(b.MusicScoreInfo.time);
        }

        private static bool IsEndedOrCanceled(LiveTouchPhase phase)
        {
            return phase == LiveTouchPhase.Ended || phase == LiveTouchPhase.Canceled;
        }
    }
}
