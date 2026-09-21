#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Live;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定簇（OpenSekai 逐字移植）的自检。
    ///
    /// 为什么需要：判定流水线是"手感"的全部来源，而它的语义（按下边沿 vs 跨度、
    /// 两指竞争归属、每指 1/6 秒、拾取）全都是**不可见规则**——错了只能靠玩家说
    /// "不跟手"，无法定位。这里把每条规则钉成一条断言。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.LiveJudgeChecks.Run
    /// </summary>
    public static class LiveJudgeChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Live Judge Core")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // ① 窗口表：PJSK 的帧数口径（2.5 帧 Perfect = 41.67ms）
            float perfect = LiveJudgeWindows.CalcFrameToTime(2.5f);
            var rIn = LiveJudgeWindows.CalculateDefaultNoteResult(JudgeFrameType.Normal, 0.040f);
            var rOut = LiveJudgeWindows.CalculateDefaultNoteResult(JudgeFrameType.Normal, 0.045f);
            var rLate = LiveJudgeWindows.CalculateDefaultNoteResult(JudgeFrameType.Normal, -0.045f);
            var rMiss = LiveJudgeWindows.CalculateDefaultNoteResult(JudgeFrameType.Normal, 0.130f);
            check("窗口表（帧口径）",
                Mathf.Abs(perfect - 0.0416667f) < 1e-4f
                && rIn.result == NoteResult.Perfect
                && rOut.result == NoteResult.Great && rOut.description == NoteResultDescription.Fast
                && rLate.result == NoteResult.Great && rLate.description == NoteResultDescription.Late
                && rMiss.result == NoteResult.Miss,
                string.Format("2.5帧={0:0.0000}s；40ms={1}、45ms={2}/{3}、-45ms={4}/{5}、130ms={6}",
                    perfect, rIn.result, rOut.result, rOut.description, rLate.result, rLate.description, rMiss.result));

            // ② 跨度判定：手指落在 JudgeLaneStart..JudgeLaneEnd 内即可判（小数轨道）
            var span = MakeNormal(2f, 3f, 0.5f, 1f, 1f);
            check("跨度判定（含小数轨道）",
                span.JudgeLaneStart == 1.5f && span.JudgeLaneEnd == 3.5f
                && Judged(span, LiveTouchPhase.Began, 3.2f, 1.0f)
                && !Judged(span, LiveTouchPhase.Began, 3.6f, 1.0f)
                && !Judged(span, LiveTouchPhase.Began, 1.4f, 1.0f),
                string.Format("跨度 {0:0.#}..{1:0.#}：3.2 内、3.6 外、1.4 外", span.JudgeLaneStart, span.JudgeLaneEnd));

            // ③ 按下边沿 vs 摩擦：普通音符只认 Began；摩擦音符"手指在跨度内"就成立（跟手核心）
            var normal = MakeNormal(2f, 3f, 0.5f, 1f, 1f);
            var friction = MakeFriction(2f, 3f, 0.5f, 1f, 1f);
            bool normalBegan = Judged(normal, LiveTouchPhase.Began, 2f, 1.0f);
            bool normalHeld = Judged(MakeNormal(2f, 3f, 0.5f, 1f, 1f), LiveTouchPhase.Moved, 2f, 1.0f);
            bool frictionHeld = Judged(friction, LiveTouchPhase.Moved, 2f, 1.0f);
            check("按下边沿 vs 摩擦跨度",
                normalBegan && !normalHeld && frictionHeld,
                string.Format("普通+Began={0}、普通+按住={1}、摩擦+按住={2}",
                    normalBegan, normalHeld, frictionHeld));

            // ④ 拾取：已经按住的手指，被"随后进窗"的摩擦音符拾到（PickUpNotBeginInputNotes）
            var pickupNote = MakeFriction(2f, 3f, 0.5f, 1f, 1f);
            int pickupJudged = RunFrame(new List<LiveNoteBase> { pickupNote }, new List<TestInput>
            {
                new TestInput(11, 11, LiveTouchPhase.Moved, 1.00f, 2f),
                new TestInput(12, 12, LiveTouchPhase.Began, 1.00f, 3.5f),
            });
            check("拾取（已按住的手指）",
                pickupJudged == 1 && pickupNote.IsTouched,
                string.Format("判定数={0}、isTouched={1}（另一根手指在 3.5 无音符）",
                    pickupJudged, pickupNote.IsTouched));

            // ⑤ 重叠归属：两指夹同一个音符 → 按轨道距离归属，只判一次
            var overlapNote = MakeNormal(1f, 3f, 0.5f, 1f, 1f);
            int overlapJudged = RunFrame(new List<LiveNoteBase> { overlapNote }, new List<TestInput>
            {
                new TestInput(21, 21, LiveTouchPhase.Began, 1.00f, 1.9f),
                new TestInput(22, 22, LiveTouchPhase.Began, 1.00f, 2.9f),
            });
            check("两指夹一音 → 只判一次",
                overlapJudged == 1,
                string.Format("判定数={0}（1.9 与 2.9 夹跨度 1..3，中心 2.0 → 归 1.9）", overlapJudged));

            // ⑥ 每指 1/6 秒：同一根手指在 1/6 秒内不补判下一个（除非结果优于 Great）
            var frictionForFinger = MakeFriction(2f, 3f, 0.5f, 1.00f, 1f);
            var longTail = MakeLongTail(2f, 2f, 0.5f, 1.25f);
            // 每指记账是**跨帧**的（InputedNormalNoteTouch 属于判定核心，不随帧清空），
            // 所以这三帧必须跑在同一个核心上。
            var fingerCore = new LiveJudgeCore();
            int f1 = RunFrameOn(fingerCore, new List<LiveNoteBase> { frictionForFinger }, new List<TestInput>
            {
                new TestInput(31, 31, LiveTouchPhase.Moved, 1.00f, 2f),
            });
            int blocked = RunFrameOn(fingerCore, new List<LiveNoteBase> { longTail }, new List<TestInput>
            {
                new TestInput(31, 31, LiveTouchPhase.Ended, 1.15f, 2f), // Δ=0.15s < 1/6，且只有 Good
            });
            check("每指 1/6 秒（封锁半边）",
                f1 == 1 && blocked == 0,
                string.Format("摩擦触达={0}；Δ0.15s 内补判={1}（应 0）", f1, blocked));

            // 【已知缺口·不计入 PASS/FAIL】1/6 秒规则的"放行半边"需要长按容器的判定语义：
            // 上游 LongNote.IsJudgment 要求"头判过之后才谈尾判"，且 OffsetJudgeTime 由 Excute 维护。
            // 本移植尚未包含 LiveLongNote.Judgment / IsJudgment 的容器状态机（对应上游 LongNote.cs 11.3KB）。
            // 缺口单列，不用假断言掩盖。
            lines.Add(string.Format("GAP  {0,-24} {1}",
                "长按容器判定",
                "待移植 LiveLongNote.Judgment/IsJudgment（头判→按住→尾判 + OffsetJudgeTime 由 Excute 维护）"));

            // ⑦ 每指独立：两根手指各自判一个音符，同帧都成立
            var left = MakeNormal(0f, 1f, 0f, 1f, 1f);
            var right = MakeNormal(3f, 4f, 0f, 1f, 1f);
            int bothJudged = RunFrame(new List<LiveNoteBase> { left, right }, new List<TestInput>
            {
                new TestInput(41, 41, LiveTouchPhase.Began, 1.00f, 0.5f),
                new TestInput(42, 42, LiveTouchPhase.Began, 1.00f, 3.5f),
            });
            check("每指独立（同帧双指）",
                bothJudged == 2,
                string.Format("判定数={0}", bothJudged));

            string summary = string.Format(
                "判定簇自检：{0} 项通过 / {1} 项异常（判定：窗口帧口径 + 跨度 + 边沿/摩擦 + 拾取 + 归属 + 每指计时）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[LiveJudge] " + line);
            Debug.Log("[LiveJudge] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/live_judge_check.txt", lines.ToArray());
                File.AppendAllText("Logs/live_judge_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LiveJudge] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private struct TestInput
        {
            public int touchId;
            public int fingerId;
            public LiveTouchPhase phase;
            public float musicTime;
            public float lane;

            public TestInput(int touchId, int fingerId, LiveTouchPhase phase, float musicTime, float lane)
            {
                this.touchId = touchId;
                this.fingerId = fingerId;
                this.phase = phase;
                this.musicTime = musicTime;
                this.lane = lane;
            }
        }

        /// <summary>跑一帧判定：返回这一帧真正判掉的音符数。</summary>
        private static int RunFrame(List<LiveNoteBase> notes, List<TestInput> inputs)
        {
            return RunFrameOn(new LiveJudgeCore(), notes, inputs);
        }

        /// <summary>在指定的判定核心上跑一帧（跨帧账本——每指 1/6 秒——挂在这个核心上）。</summary>
        private static int RunFrameOn(LiveJudgeCore core, List<LiveNoteBase> notes, List<TestInput> inputs)
        {
            int judged = 0;
            Action<LiveNoteBase> onJudged = _ => judged++;
            core.OnJudged += onJudged;

            foreach (TestInput i in inputs)
            {
                var touch = new LiveTouch(i.touchId, i.fingerId, i.phase, i.musicTime, i.lane, Vector2.zero);
                core.PushInput(touch);
            }

            core.Judgment(notes);
            core.EndFrame();
            core.OnJudged -= onJudged;
            return judged;
        }

        /// <summary>单音符、单输入：这一输入是否判掉了它。</summary>
        private static bool Judged(LiveNoteBase note, LiveTouchPhase phase, float lane, float musicTime)
        {
            return RunFrame(new List<LiveNoteBase> { note },
                new List<TestInput> { new TestInput(1, 1, phase, musicTime, lane) }) == 1;
        }

        private static MusicScoreInfo Info(float time)
        {
            return new MusicScoreInfo(0, 0f, time, 120f, 4f, 1f, 1f);
        }

        private static LiveNormalNote MakeNormal(float laneStart, float laneEnd, float laneOffset, float time, float id)
        {
            return new LiveNormalNote(Info(time), (int)id, (int)laneStart, (int)laneEnd,
                NoteCategory.Normal, NoteType.Default, laneOffset)
            {
                LaneStartF = laneStart,
                LaneEndF = laneEnd,
            };
        }

        private static LiveFrictionNote MakeFriction(float laneStart, float laneEnd, float laneOffset, float time, float id)
        {
            return new LiveFrictionNote(Info(time), (int)id, (int)laneStart, (int)laneEnd,
                NoteCategory.Friction, NoteType.Default, laneOffset)
            {
                LaneStartF = laneStart,
                LaneEndF = laneEnd,
            };
        }

        /// <summary>长按的尾巴（Category=Long，只认抬起/取消），用于验每指 1/6 秒规则。</summary>
        private static LiveNoteBase MakeLongTail(float laneStart, float laneEnd, float laneOffset, float time)
        {
            // 头放得足够远（-0.25s），保证测试输入只可能命中尾巴——否则头自己也会进窗。
            var head = new LiveLongNote(Info(time - 0.25f), 51, (int)laneStart, (int)laneEnd,
                NoteCategory.Long, NoteType.Default, laneOffset)
            {
                LaneStartF = laneStart,
                LaneEndF = laneEnd,
            };
            var tail = new LiveNormalNote(Info(time), 52, (int)laneStart, (int)laneEnd,
                NoteCategory.Long, NoteType.Default, laneOffset)
            {
                LaneStartF = laneStart,
                LaneEndF = laneEnd,
            };
            head.AddChild(tail);
            return head;
        }
    }
}
#endif
