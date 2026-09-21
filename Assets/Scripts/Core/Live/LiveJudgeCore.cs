using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Live
{
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
            if (source == null)
            {
                return;
            }

            Reset(source.Touch, source.Lane);
            CandidateNotes.AddRange(source.CandidateNotes);
        }

        public void Reset(LiveTouch touch, float lane)
        {
            Touch = touch;
            Lane = lane;
            CandidateNotes.Clear();
        }
    }

    /// <summary>
    /// 判定主循环。逐字移植 OpenSekai Sekai/Core/Live/LiveLogic.cs 的 Judgment() 五步：
    ///
    ///     ① AllocateInputJudgableEachNotes   每个输入 × 每个待判音符 → 候选
    ///     ② OverlapJudgmentSorting           同一音符被多指竞争 → 按轨道距离归属
    ///     ③ LeaveNotesFastGeneration         同一根手指多个候选 → 留"该判的那个"
    ///     ④ PickUpNotBeginInputNotes         手指已按住、音符随后进窗 → 拾取（跟手的关键）
    ///     ⑤ EvaluateJudgment                 逐个应用；没判到东西的输入 → Unpicked 反馈
    ///
    /// 与上游的结构差异只有两处，都是接线：
    /// · 屏幕坐标 → 小数轨道由宿主算好（<see cref="LiveTouch.lane"/>），本类只读；
    ///   左向甩键的"重新取轨道"走注入的 <see cref="LaneResolver"/>（对应原作 ConvertLaneBy 的 Flick 分支）；
    /// · 应用判定时额外抛 <see cref="OnJudged"/>，供宿主/自检观察"这一帧判掉了哪些音符"。
    ///   音符自己的结果通知仍走原作的回调面（OnJudgment/OnUpdateCombo/OnUpdateScore/OnDamage）。
    /// </summary>
    public class LiveJudgeCore
    {
        /// <summary>屏幕/世界坐标 → 小数轨道（&lt;0 = 判定区之外）。对应原作 GetJudgmentLane。</summary>
        public Func<Vector3, bool, float> LaneResolver;

        /// <summary>某个音符被判掉时触发（宿主计分/连击/特效，或自检计数）</summary>
        public event Action<LiveNoteBase> OnJudged;

        /// <summary>有输入但没有判到任何音符（对应 LiveViewExt.Unpicked：按键反馈）</summary>
        public event Action<int, LiveTouch> OnUnpicked;

        private readonly List<LiveTouchInput> activeInputList = new List<LiveTouchInput>();
        private readonly List<LiveNoteBase> connectionNoteList = new List<LiveNoteBase>();
        private readonly List<int> usedFingerList = new List<int>();

        /// <summary>
        /// 每根手指"上一次判掉普通音符"的音乐时间（对应 InputedNormalNoteTouch）。
        /// 用途：同一根手指 1/6 秒内不再判下一个普通音符，除非结果优于 Great。
        /// </summary>
        private readonly Dictionary<int, float> inputedNormalNoteTouch = new Dictionary<int, float>();

        /// <summary>本帧的谱面时刻（LaneDistance 等需要"当前帧"位置的口子；结构对齐上游的 currentFrameInfo）</summary>
        public MusicScoreInfo CurrentFrameInfo;

        public int ActiveInputCount
        {
            get { return activeInputList.Count; }
        }

        public IReadOnlyList<LiveTouchInput> ActiveInputs
        {
            get { return activeInputList; }
        }

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

        /// <summary>跑一次判定循环（对应 LiveLogic.Judgment）。judgmentNotes 会被就地去重（判掉的移除）。</summary>
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

        /// <summary>本帧结束后清理输入表（对应 LiveLogic 的 activeInputList.Clear()）。</summary>
        public void EndFrame()
        {
            activeInputList.Clear();
        }

        // ── ① 候选分配 ────────────────────────────────────────────────
        private void AllocateInputJudgableEachNotes(List<LiveNoteBase> judgmentNotes)
        {
            for (var noteIndex = 0; noteIndex < judgmentNotes.Count; noteIndex++)
            {
                var judgmentNote = judgmentNotes[noteIndex];
                if (judgmentNote == null)
                {
                    continue;
                }

                var isConnection = true;
                var isJudged = false;
                for (var inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
                {
                    var activeInput = activeInputList[inputIndex];
                    var note = judgmentNote;
                    var lane = ConvertLaneBy(ref note, ref activeInput);
                    if (lane >= 0f && note.IsJudgment(ref activeInput.Touch, lane))
                    {
                        isJudged = true;
                        var touchId = activeInput.Touch.touchId;
                        var lastNote = GetLastNote(judgmentNote);
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

                    var firstNote = GetFirstNote(judgmentNote);
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
            if (judgmentNote is LiveFrictionNote)
            {
                var lane = ConvertLaneBy(ref note, ref activeInput);
                if (judgmentNote.IsJudgment(ref activeInput.Touch, lane))
                {
                    isConnection = true;
                    isJudged = true;
                    return;
                }
            }

            if (note is LiveFrictionNote)
            {
                var lane = ConvertLaneBy(ref note, ref activeInput);
                if (note.IsJudgment(ref activeInput.Touch, lane))
                {
                    isConnection = true;
                    isJudged = true;
                    return;
                }
            }

            if (note is LiveLongNote)
            {
                var lane = ConvertLaneBy(ref note, ref activeInput);
                if (note.IsJudgment(ref activeInput.Touch, lane))
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
            var lane = ConvertLaneBy(ref note, ref activeInput);
            var hadInput = inputedNormalNoteTouch.ContainsKey(touchId);
            var isLastNoteJudgment = note != null && note.IsJudgment(ref activeInput.Touch, lane);
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
            for (var inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                var input = activeInputList[inputIndex];
                if (input.CandidateNotes.Count == 0)
                {
                    continue;
                }

                for (var otherInputIndex = inputIndex - 1; otherInputIndex >= 0; otherInputIndex--)
                {
                    var otherInput = activeInputList[otherInputIndex];
                    if (otherInput.CandidateNotes.Count == 0)
                    {
                        continue;
                    }

                    for (var candidateIndex = input.CandidateNotes.Count - 1; candidateIndex >= 0; candidateIndex--)
                    {
                        var candidate = input.CandidateNotes[candidateIndex];
                        var otherCandidateIndex = otherInput.CandidateNotes.IndexOf(candidate);
                        if (candidate == null || otherCandidateIndex < 0)
                        {
                            continue;
                        }

                        var inputNote = ResolveJudgmentNote(candidate, input);
                        var otherNote = ResolveJudgmentNote(candidate, otherInput);
                        var inputDistance = inputNote != null
                            ? inputNote.LaneDistance(ref CurrentFrameInfo, ConvertLaneBy(ref inputNote, ref input))
                            : float.MaxValue;
                        var otherDistance = otherNote != null
                            ? otherNote.LaneDistance(ref CurrentFrameInfo, ConvertLaneBy(ref otherNote, ref otherInput))
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
            for (var inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                var input = activeInputList[inputIndex];
                for (var i = input.CandidateNotes.Count - 1; i >= 0; i--)
                {
                    if (input.CandidateNotes.Count < 2)
                    {
                        break;
                    }

                    var first = ResolveFastGenerationNote(input.CandidateNotes[0], input);
                    var current = ResolveFastGenerationNote(input.CandidateNotes[i], input);
                    if (first == null || current == null)
                    {
                        continue;
                    }

                    if (IsSameJudgmentTime(first, current))
                    {
                        var firstDistance = first.LaneDistance(
                            ref CurrentFrameInfo, ConvertLaneBy(ref first, ref input));
                        var currentDistance = current.LaneDistance(
                            ref CurrentFrameInfo, ConvertLaneBy(ref current, ref input));
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
            for (var inputIndex = activeInputList.Count - 1; inputIndex >= 0; inputIndex--)
            {
                var input = activeInputList[inputIndex];
                for (var noteIndex = connectionNoteList.Count - 1; noteIndex >= 0; noteIndex--)
                {
                    var note = connectionNoteList[noteIndex];
                    var lane = ConvertLaneBy(ref note, ref input);
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
            for (var inputIndex = 0; inputIndex < activeInputList.Count; inputIndex++)
            {
                var input = activeInputList[inputIndex];
                var hasCandidate = input.CandidateNotes.Count > 0;
                for (var candidateIndex = 0; candidateIndex < input.CandidateNotes.Count; candidateIndex++)
                {
                    var note = input.CandidateNotes[candidateIndex];
                    if (note == null)
                    {
                        continue;
                    }

                    var lane = ConvertLaneBy(ref note, ref input);
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

            for (var inputIndex = 0; inputIndex < activeInputList.Count; inputIndex++)
            {
                var input = activeInputList[inputIndex];
                var used = false;
                for (var i = usedFingerList.Count - 1; i >= 0; i--)
                {
                    used |= usedFingerList[i] == input.Touch.fingerId;
                }

                if (!used && input.Lane >= 0f)
                {
                    OnUnpicked?.Invoke(Mathf.RoundToInt(input.Lane), input.Touch);
                }

                if (input.Touch.IsEndedOrCanceled())
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
                var resolved = ResolveLaneFromPosition(input.Touch.worldPosition, !note.FinishFirstAction);
                return resolved >= 0f ? resolved : input.Lane;
            }

            return input.Lane;
        }

        private float ResolveLaneFromPosition(Vector3 position, bool checkJudgmentY)
        {
            return LaneResolver != null ? LaneResolver(position, checkJudgmentY) : -1f;
        }

        private bool TryAddConnectionCandidate(LiveNoteBase note, LiveTouchInput input)
        {
            // 逐字对应 LiveLogic.TryAddConnectionCandidate：只有长按（含摩擦长按）头能被"已按住的手指"拾取。
            if (!(note is LiveLongNote))
            {
                return false;
            }

            var lane = ConvertLaneBy(ref note, ref input);
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

            var resolved = note.Result != NoteResult.None || input.Touch.phase != LiveTouchPhase.Began
                ? GetLastNote(note)
                : GetFirstNote(note);
            var lane = ConvertLaneBy(ref resolved, ref input);
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
    }
}
