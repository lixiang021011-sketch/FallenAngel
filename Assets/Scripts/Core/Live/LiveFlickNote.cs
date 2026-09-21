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

        /// <summary>判定"够得上甩"的最小滑动距离（像素；由构造方按屏幕高度算）</summary>
        private readonly float flickDistance;

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
            Category = NoteCategory.Flick;
        }

        public LiveFlickNote(MusicScoreInfo info, int id, int laneStart, int laneEnd,
            NoteCategory category, float speedRatio, NoteType type, NoteDirection direction,
            float laneOffset, float flickDistance)
            : base(info, id, laneStart, laneEnd, category, speedRatio, laneOffset, type)
        {
            inputResults = new (int id, bool distance, bool trueDirection)[8];
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

            foreach (var result in inputResults)
            {
                if (result.id != 0 && result.id == touch.touchId)
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
                    AddInputResult(ref touch, ref isUpdatedDelta, ref isTrueDirection);
                    return false;
                }

                if (!HasInputResult(touch.touchId))
                {
                    return false;
                }

                UpdateDelta(touch.delta, ref isUpdatedDelta, ref isTrueDirection, touch.isDirectionTrusted);
                if (!isUpdatedDelta)
                {
                    return false;
                }

                AddInputResult(ref touch, ref isUpdatedDelta, ref isTrueDirection);
                return JudgmentFlick(touch.musicTime);
            }

            UpdateDelta(touch.delta, ref isUpdatedDelta, ref isTrueDirection, touch.isDirectionTrusted);
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

        private void AddInputResult(ref LiveTouch touch, ref bool isUpdatedDelta, ref bool isTrueDirection)
        {
            for (var i = 0; i < inputResults.Length; i++)
            {
                if (inputResults[i].id == 0 || inputResults[i].id == touch.touchId)
                {
                    inputResults[i] = (touch.touchId, isUpdatedDelta, isTrueDirection);
                    return;
                }
            }
        }

        private void UpdateDelta(Vector2 touchDelta, ref bool isUpdatedDelta, ref bool isTrueDirection,
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

            isUpdatedDelta = touchDelta.sqrMagnitude > flickDistance * flickDistance;
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

            var dot = Vector2.Dot(touchDelta.normalized, requiredDirection.normalized);
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
            lastInputFrame = 0;
        }

        private bool HasInputResult(int touchId)
        {
            foreach (var result in inputResults)
            {
                if (result.id != 0 && result.id == touchId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
