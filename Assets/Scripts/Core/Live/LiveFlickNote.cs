using System;
using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>
    /// 甩键（flick）音符。逐字对应 OpenSekai Sekai/Live/FlickNote.cs 的判定结构：
    /// 按下不判、**滑动幅度够大且方向正确**才判；方向不满足时走
    /// LiveConfig 的 FlickMiss 分支（Perfect 时机 + 方向错 → Great，不判 Miss）。
    ///
    /// 两处本工程适配（都在输入/方向口径上，判定结构不变）：
    /// 1. 谱面协议只有上/下两个方向（原作是左/右/Default），NoteDirection 增加 Up/Down 两个值；
    /// 2. 键盘/编辑器鼠标没有可信滑动向量（<see cref="LiveTouch.isDirectionTrusted"/> = false）时
    ///    不校验方向——PC 上仍能像单击一样判掉，与原工程"键盘放宽"的老行为一致。
    /// </summary>
    public class LiveFlickNote : LiveNoteBase
    {
        private int lastInputFrame;

        private readonly (int id, bool distance, bool trueDirection)[] inputResults;

        /// <summary>
        /// 每根手指从按下起的**累计**滑动量（按 60fps 归一的口径）。
        /// 为什么需要：上游只看"单帧位移够不够大"，慢甩（每帧几 px、累计几十 px）会被当成"没滑"——
        /// 实机就是"甩键时灵时不灵"。本工程按累计幅度判，方向也取累计向量（单帧抖动不会翻向）。
        /// </summary>
        private readonly Vector2[] accumulatedDeltas;

        /// <summary>判定"够得上甩"的最小滑动距离（像素；由构造方按屏幕高度算）</summary>
        private readonly float flickDistance;

        /// <summary>
        /// 记账用的手指键：**上游 touchId 从 1 开始**（0 是"槽位空"的哨兵），
        /// 而本工程的 touchId 就是 Unity 的 `Touch.fingerId` —— **第一根手指就是 0**。
        /// 直接把 0 存进 `inputResults` 会看起来"槽位是空的"：`HasInputResult` 永远返回 false
        /// （`Judgment` 在 `!HasInputResult` 处直接 return）、`JudgmentFlick` 的 `result.id != 0`
        /// 也永远过滤掉它 —— 真机表现就是**第一根手指甩不动粉键**（作者反馈"粉键识别不了"）。
        /// 所以内部统一 +1 再存，保持上游"0 = 空槽"的语义。
        /// </summary>
        private static int SlotKey(int touchId)
        {
            return touchId + 1;
        }

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

        public LiveFlickNote()
        {
            inputResults = new (int id, bool distance, bool trueDirection)[8];
            accumulatedDeltas = new Vector2[8];
            Category = NoteCategory.Flick;
        }

        public LiveFlickNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, float speedRatio, NoteType type, NoteDirection direction,
            float laneOffset, float flickDistance)
            : base(info, id, laneStart, laneEnd, category, speedRatio, laneOffset, type)
        {
            inputResults = new (int id, bool distance, bool trueDirection)[8];
            accumulatedDeltas = new Vector2[8];
            this.flickDistance = flickDistance;
            Direction = direction;
        }

        public override void Excute(MusicScoreInfo currentFrameInfo, float offsetTime)
        {
            base.Excute(currentFrameInfo, offsetTime);
            if (State == NoteState.Input
                && lastInputFrame + 1 < LiveJudgeClock.FrameCount
                && CalcProgress(currentFrameInfo, offsetTime) >= 1f)
            {
                JudgmentFlick(currentFrameInfo.time);
            }
        }

        private bool JudgmentFlick(float musicTime)
        {
            if (inputResults == null || inputResults.Length == 0)
            {
                return false;
            }

            var hasInput = false;
            var isDirection = false;
            foreach (var result in inputResults)
            {
                if (result.id != 0 && result.distance && result.trueDirection)
                {
                    hasInput = true;
                    isDirection = true;
                    break;
                }
            }

            if (!hasInput)
            {
                foreach (var result in inputResults)
                {
                    if (result.id != 0 && result.distance)
                    {
                        hasInput = true;
                        isDirection = result.trueDirection;
                        break;
                    }
                }
            }

            if (!hasInput)
            {
                return false;
            }

            var judgeInfo = parentNote != null
                ? LiveConfig.CalculateLongEndFlickNoteResult(this, musicTime, isDirection)
                : LiveConfig.CalculateFlickNoteResult(this, musicTime, isDirection);
            ApplyJudgeInfo(judgeInfo);
            State = NoteState.Done;
            return true;
        }

        public override bool IsJudgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if (IsLaneCheck(touch))
            {
                if (JudgeLaneStart > lane || JudgeLaneEnd < lane)
                {
                    return false;
                }

                var offset = MusicScoreInfo.time - touch.musicTime;
                if (!LiveUtility.IsJudgmentTiming(LiveUtility.GetINoteToJudgeFrameType(this), offset))
                {
                    return false;
                }
            }

            return parentNote != null || State != NoteState.Playing || touch.phase == LiveTouchPhase.Began;
        }

        private bool IsLaneCheck(LiveTouch touch)
        {
            if (State != NoteState.Input)
            {
                return true;
            }

            int key = SlotKey(touch.touchId);
            foreach (var result in inputResults)
            {
                if (result.id != 0 && result.id == key)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Judgment(ref LiveTouch touch, float lane)
        {
            if (State == NoteState.Done)
            {
                return false;
            }

            if (State == NoteState.Playing)
            {
                State = NoteState.Input;
            }

            if (State != NoteState.Input)
            {
                return false;
            }

            var isUpdatedDelta = false;
            var isTrueDirection = false;
            if (parentNote == null)
            {
                if (touch.phase == LiveTouchPhase.Began)
                {
                    int beganSlot = AddInputResult(ref touch, ref isUpdatedDelta, ref isTrueDirection);
                    if (beganSlot >= 0) accumulatedDeltas[beganSlot] = Vector2.zero;   // 新按下：累计量归零
                    return false;
                }

                if (!HasInputResult(touch.touchId))
                {
                    return false;
                }

                UpdateDelta(SlotOf(touch.touchId), touch.delta,
                    ref isUpdatedDelta, ref isTrueDirection, touch.isDirectionTrusted);
                if (!isUpdatedDelta)
                {
                    return false;
                }

                AddInputResult(ref touch, ref isUpdatedDelta, ref isTrueDirection);
                return JudgmentFlick(touch.musicTime);
            }

            UpdateDelta(SlotOf(touch.touchId), touch.delta,
                ref isUpdatedDelta, ref isTrueDirection, touch.isDirectionTrusted);
            if (isUpdatedDelta)
            {
                AddInputResult(ref touch, ref isUpdatedDelta, ref isTrueDirection);
            }

            lastInputFrame = LiveJudgeClock.FrameCount;
            if (MusicScoreInfo.time - touch.musicTime <= -LiveConfig.JustPerfectJudgeTimeHalf
                && isTrueDirection && isUpdatedDelta)
            {
                return JudgmentFlick(touch.musicTime);
            }

            return false;
        }

        /// <summary>写入该手指的输入结果，返回它占用的槽位（-1 = 槽满）</summary>
        private int AddInputResult(ref LiveTouch touch, ref bool isUpdatedDelta, ref bool isTrueDirection)
        {
            int key = SlotKey(touch.touchId);
            for (var i = 0; i < inputResults.Length; i++)
            {
                if (inputResults[i].id == 0 || inputResults[i].id == key)
                {
                    inputResults[i] = (key, isUpdatedDelta, isTrueDirection);
                    return i;
                }
            }

            return -1;
        }

        /// <summary>该手指占用的槽位（没有则 -1）</summary>
        private int SlotOf(int touchId)
        {
            int key = SlotKey(touchId);
            for (var i = 0; i < inputResults.Length; i++)
            {
                if (inputResults[i].id == key) return i;
            }
            return -1;
        }

        private void UpdateDelta(int slot, Vector2 touchDelta, ref bool isUpdatedDelta, ref bool isTrueDirection,
            bool isDirectionTrusted)
        {
            if (touchDelta == LiveUtility.Vector2Zero)
            {
                return;
            }

            var frameScale = Time.deltaTime / 0.016667f;
            if (frameScale > 0f)
            {
                touchDelta /= frameScale;
            }

            Vector2 accumulated = touchDelta;
            if (slot >= 0)
            {
                accumulatedDeltas[slot] += touchDelta;
                accumulated = accumulatedDeltas[slot];
            }

            // 幅度：单帧够大 **或** 累计够大（慢甩也算）
            isUpdatedDelta = touchDelta.sqrMagnitude > flickDistance * flickDistance
                             || accumulated.sqrMagnitude > flickDistance * flickDistance;
            if (!isUpdatedDelta)
            {
                isTrueDirection = false;
                return;
            }

            // 键盘/编辑器鼠标没有可信的滑动方向：与老行为一致，按"方向正确"处理
            if (!isDirectionTrusted)
            {
                isTrueDirection = true;
                return;
            }

            Vector2 requiredDirection = Direction switch
            {
                NoteDirection.Left => new Vector2(-1f, 1f),
                NoteDirection.Right => new Vector2(1f, 1f),
                NoteDirection.Down => Vector2.down,
                _ => Vector2.up
            };

            // 方向取累计向量（单帧抖动不会把方向翻过去）
            var directionVector = accumulated.sqrMagnitude > 0.0001f ? accumulated : touchDelta;
            var dot = Vector2.Dot(directionVector.normalized, requiredDirection.normalized);
            isTrueDirection = dot >= 0f || Direction == NoteDirection.Default;
        }

        public override NoteResult CalcNoteResult(float musicTime)
        {
            return (parentNote != null
                ? LiveConfig.CalculateLongEndFlickNoteResult(this, musicTime, true)
                : LiveConfig.CalculateFlickNoteResult(this, musicTime, true)).Item1;
        }

        public override void ResetNote()
        {
            base.ResetNote();
            Array.Clear(inputResults, 0, inputResults.Length);
            Array.Clear(accumulatedDeltas, 0, accumulatedDeltas.Length);
            lastInputFrame = 0;
        }

        private bool HasInputResult(int touchId)
        {
            int key = SlotKey(touchId);
            foreach (var result in inputResults)
            {
                if (result.id != 0 && result.id == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
