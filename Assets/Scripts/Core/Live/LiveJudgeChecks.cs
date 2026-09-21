#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Data;
using FallenAngel.Live;
using UnityEditor;
using UnityEngine;
using LiveNoteType = FallenAngel.Live.NoteType;
using DataNoteType = FallenAngel.Data.NoteType;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定簇（OpenSekai 逐字移植）的自检。
    ///
    /// 为什么需要：判定流水线是"手感"的全部来源，而它的语义（按下边沿 vs 跨度、两指竞争归属、
    /// 每指记账、拾取、长按容器的头/尾/掉段）全都是**不可见规则**——错了只能靠玩家说"不跟手"，
    /// 无法定位。这里把每条规则钉成一条断言。
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
                lines.Add(string.Format("{0} {1,-30} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // ① 窗口表：PJSK 的帧数口径（2.5 帧 Perfect = 41.67ms；JustPerfect = ±1/120s）
            float perfect = LiveConfig.CalcFrameToTime(2.5f);
            var rIn = DefaultResult(2.5f, 0.040f);
            var rOut = DefaultResult(2.5f, 0.045f);
            var rLate = DefaultResult(2.5f, -0.045f);
            var rMiss = DefaultResult(2.5f, 0.130f);
            check("窗口表（帧口径 / Normal）",
                Mathf.Abs(perfect - 0.0416667f) < 1e-4f
                && rIn.result == NoteResult.Perfect
                && rOut.result == NoteResult.Great && rOut.description == NoteResultDescription.Fast
                && rLate.result == NoteResult.Great && rLate.description == NoteResultDescription.Late
                && rMiss.result == NoteResult.Miss,
                string.Format("2.5帧={0:0.0000}s；40ms={1}、45ms={2}/{3}、-45ms={4}/{5}、130ms={6}",
                    perfect, rIn.result, rOut.result, rOut.description, rLate.result, rLate.description, rMiss.result));

            // ①b 前后不对称 + 全表最大后值（原作：Flick 后判更宽容、JudgeTimeAfter = 8.5 帧）
            var flickEarly = FlickResult(0.115f);
            var flickLate = FlickResult(-0.115f);
            check("窗口表（前后不对称）",
                flickEarly.result == NoteResult.Good && flickLate.result == NoteResult.Great
                && Mathf.Abs(LiveConfig.JudgeTimeAfter - 8.5f / 60f) < 1e-5f
                && Mathf.Abs(LiveConfig.JudgeTimeBefore - 7.5f / 60f) < 1e-5f,
                string.Format("Flick 早115ms={0} / 晚115ms={1}；过窗阈值 前{2:0.0000}s 后{3:0.0000}s",
                    flickEarly.result, flickLate.result, LiveConfig.JudgeTimeBefore, LiveConfig.JudgeTimeAfter));

            // ② 跨度判定：手指落在 JudgeLaneStart..JudgeLaneEnd 内即可判（小数轨道）
            var span = MakeNormal(2f, 3f, 0.5f, 1f, 1f);
            check("跨度判定（含小数轨道）",
                Mathf.Approximately(span.JudgeLaneStart, 1.5f) && Mathf.Approximately(span.JudgeLaneEnd, 3.5f)
                && Judged(span, LiveTouchPhase.Began, 3.2f, 1.0f)
                && !Judged(span, LiveTouchPhase.Began, 3.6f, 1.0f)
                && !Judged(span, LiveTouchPhase.Began, 1.4f, 1.0f),
                string.Format("跨度 {0:0.#}..{1:0.#}：3.2 内、3.6 外、1.4 外",
                    span.JudgeLaneStart, span.JudgeLaneEnd));

            // ③ 按下边沿 vs 摩擦：普通音符只认 Began；摩擦音符"手指在跨度内"就成立（跟手核心）
            bool normalBegan = Judged(MakeNormal(2f, 3f, 0.5f, 1f, 1f), LiveTouchPhase.Began, 2f, 1.0f);
            bool normalHeld = Judged(MakeNormal(2f, 3f, 0.5f, 1f, 1f), LiveTouchPhase.Moved, 2f, 1.0f);
            bool frictionHeld = Judged(MakeFriction(2f, 3f, 0.5f, 1f, 1f), LiveTouchPhase.Moved, 2f, 1.0f);
            check("按下边沿 vs 摩擦跨度",
                normalBegan && !normalHeld && frictionHeld,
                string.Format("普通+Began={0}、普通+按住={1}、摩擦+按住={2}",
                    normalBegan, normalHeld, frictionHeld));

            // ④ 拾取：手指已经按住，摩擦音符"随后进窗"时被它拾到（PickUpNotBeginInputNotes / 跟手核心）
            //    本工程协议：drag 碰即 Perfect → 拾到的当帧就结算（不等过线）
            var pickupNote = MakeFriction(2f, 3f, 0.5f, 1f, 1f);
            var pickupRunner = new FrameRunner(new List<LiveNoteBase> { pickupNote });
            int beforeWindow = pickupRunner.Step(0.80f, new TestInput(11, 11, LiveTouchPhase.Moved, 0.80f, 2f));
            pickupRunner.Step(0.90f, new TestInput(11, 11, LiveTouchPhase.Moved, 0.90f, 2f));
            bool judgedInWindow = pickupNote.State == NoteState.Done && pickupNote.Result == NoteResult.JustPerfect;
            pickupRunner.Step(1.00f, new TestInput(11, 11, LiveTouchPhase.Moved, 1.00f, 2f));
            check("拾取（已按住的手指）",
                beforeWindow == 0 && judgedInWindow && pickupNote.Result == NoteResult.JustPerfect,
                string.Format("0.80s 窗外判定数={0}；0.90s 进窗被拾取即结算={1}；结果={2}",
                    beforeWindow, judgedInWindow, pickupNote.Result));

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

            // ⑥ 同指去重：同一根手指同一条输入有多个候选 → 只判"该判的那个"
            var nearNote = MakeNormal(2f, 2f, 0.5f, 1.0f, 1f);
            var farNote = MakeNormal(2f, 2f, 0.5f, 1.02f, 2f);   // 与 nearNote 同轨、不同时刻
            int sameFingerJudged = RunFrame(new List<LiveNoteBase> { farNote, nearNote }, new List<TestInput>
            {
                new TestInput(31, 31, LiveTouchPhase.Began, 1.00f, 2f),
            });
            check("同指多候选 → 只判一个",
                sameFingerJudged == 1 && nearNote.State == NoteState.Done && farNote.State != NoteState.Done,
                string.Format("判定数={0}、1.00s 的音符={1}、1.02s 的音符={2}",
                    sameFingerJudged, nearNote.State, farNote.State));

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

            // ── ⑧ 长按容器（上游 Live/LongNote.cs 状态机）────────────────
            // ⑧a 头判：按下落在跨度内 → 头按 Long 窗口结算，进入"按住中"
            var holdA = MakeHold(2, 1.00f, 1.50f, 60);
            var runnerA = new FrameRunner(new List<LiveNoteBase> { holdA });
            runnerA.Step(0.90f);
            int headJudged = runnerA.Step(1.00f, new TestInput(1, 1, LiveTouchPhase.Began, 1.00f, 2f));
            check("长按·头判（进入按住）",
                headJudged == 1 && holdA.Result == NoteResult.JustPerfect && holdA.State == NoteState.InputBegan,
                string.Format("状态={0}、结果={1}", holdA.State, holdA.Result));

            // ⑧b 按住保持：连续帧有输入 → 不掉（Release）
            runnerA.Step(1.05f, new TestInput(1, 1, LiveTouchPhase.Moved, 1.05f, 2f));
            runnerA.Step(1.10f, new TestInput(1, 1, LiveTouchPhase.Stationary, 1.10f, 2f));
            bool stillHolding = holdA.State == NoteState.Input || holdA.State == NoteState.InputBegan;
            check("长按·按住保持（不掉指）",
                stillHolding,
                string.Format("0.15s 后状态={0}（有输入 → 不 Release）", holdA.State));

            // ⑧c 手指离开 → Release；手指回来 → 再次按住（掉段不断整条）
            runnerA.Step(1.15f);
            bool released = holdA.State == NoteState.Release;
            runnerA.Step(1.20f, new TestInput(1, 1, LiveTouchPhase.Began, 1.20f, 2f));
            bool regrabbed = holdA.State == NoteState.InputBegan || holdA.State == NoteState.Input;
            check("长按·掉段后能重拾（不断整条）",
                released && regrabbed && holdA.State != NoteState.Done,
                string.Format("离手后={0}、回手后={1}、最终={2}", released ? "Release" : "其它", regrabbed, holdA.State));

            // ⑧d 尾判：在尾部时刻松开 → 尾按 Long_End 窗口结算 → 整条 Done
            var holdB = MakeHold(2, 1.00f, 1.50f, 61);
            var runnerB = new FrameRunner(new List<LiveNoteBase> { holdB });
            runnerB.Step(0.90f);
            runnerB.Step(1.00f, new TestInput(2, 2, LiveTouchPhase.Began, 1.00f, 2f));
            runnerB.Step(1.30f, new TestInput(2, 2, LiveTouchPhase.Moved, 1.30f, 2f));
            int tailJudged = runnerB.Step(1.50f, new TestInput(2, 2, LiveTouchPhase.Ended, 1.50f, 2f));
            var tailB = holdB.NoteList[holdB.NoteList.Count - 1];
            check("长按·尾判（松手时刻结算）",
                tailJudged == 1 && holdB.State == NoteState.Done && tailB.State == NoteState.Done
                && (tailB.Result == NoteResult.JustPerfect || tailB.Result == NoteResult.Perfect)
                && holdB.Result > NoteResult.Pass,
                string.Format("判定数={0}、整条={1}/{2}、尾={3}、头={4}",
                    tailJudged, holdB.State, holdB.Result, tailB.Result, holdB.Result));

            // ⑧e 提前松手太多 → 尾过窗判 Miss；整条结果由 ForceTerminate 收口（头判过的分不保留）
            var holdC = MakeHold(2, 1.00f, 1.50f, 62);
            var runnerC = new FrameRunner(new List<LiveNoteBase> { holdC });
            runnerC.Step(0.90f);
            runnerC.Step(1.00f, new TestInput(3, 3, LiveTouchPhase.Began, 1.00f, 2f));
            runnerC.Step(1.10f, new TestInput(3, 3, LiveTouchPhase.Ended, 1.10f, 2f));
            runnerC.Step(1.60f);
            runnerC.Step(1.80f);
            runnerC.Step(1.90f);
            var tailC = holdC.NoteList[holdC.NoteList.Count - 1];
            check("长按·提前松手 → 尾 Miss 收口",
                tailC.State == NoteState.Done && tailC.Result == NoteResult.Miss
                && holdC.State == NoteState.Done && holdC.Result == NoteResult.Miss,
                string.Format("尾={0}/{1}、整条={2}/{3}（ForceTerminate：子音符没活着 → 整条 Miss）",
                    tailC.State, tailC.Result, holdC.State, holdC.Result));

            // ⑧f 完全不按 → 头 Miss、尾 Miss、整条 Done（不会永久堆积）
            var holdD = MakeHold(2, 1.00f, 1.50f, 63);
            var runnerD = new FrameRunner(new List<LiveNoteBase> { holdD });
            runnerD.Step(0.90f);
            runnerD.Step(1.20f);
            runnerD.Step(1.35f);
            bool headMissed = holdD.Result == NoteResult.Miss;
            runnerD.Step(1.65f);
            runnerD.Step(1.80f);
            var tailD = holdD.NoteList[holdD.NoteList.Count - 1];
            check("长按·全程不按 → 头尾双 Miss 收口",
                headMissed && holdD.State == NoteState.Done
                && tailD.State == NoteState.Done && tailD.Result == NoteResult.Miss,
                string.Format("头={0}/{1}、尾={2}/{3}",
                    holdD.Result, holdD.State, tailD.Result, tailD.State));

            // ⑧g 判定跨度沿子音符链推进：滑条的头→尾之间插值（跟手的几何前提）
            var slide = MakeHold(1, 1.00f, 1.40f, 70);
            slide.LaneEnd = 3;
            slide.LaneEndF = 3f;
            var slideTail = slide.NoteList[slide.NoteList.Count - 1];
            slideTail.DefaultLeftLane = 3;
            slideTail.DefaultRightLane = 3;
            slideTail.LaneStart = 3;
            slideTail.LaneEnd = 3;
            slideTail.LaneStartF = 3f;
            slideTail.LaneEndF = 3f;
            var runnerSlide = new FrameRunner(new List<LiveNoteBase> { slide });
            runnerSlide.Step(0.90f);
            runnerSlide.Step(1.00f, new TestInput(4, 4, LiveTouchPhase.Began, 1.00f, 1f));
            runnerSlide.Step(1.20f, new TestInput(4, 4, LiveTouchPhase.Moved, 1.20f, 2f));
            float midCenter = (slide.LaneStartF + slide.LaneEndF) * 0.5f;
            runnerSlide.Step(1.40f, new TestInput(4, 4, LiveTouchPhase.Moved, 1.40f, 3f));
            float endCenter = (slide.LaneStartF + slide.LaneEndF) * 0.5f;
            check("滑条·判定跨度沿路径推进",
                Mathf.Abs(midCenter - 2f) < 0.2f && Mathf.Abs(endCenter - 3f) < 0.2f,
                string.Format("1.20s 中心={0:0.##}、1.40s 中心={1:0.##}（轨 1 → 轨 3）", midCenter, endCenter));

            // ⑧h 甩键：幅度够 + 方向对 → 判掉；方向错 → Great + FlickMiss（不判 Miss）
            var flickUp = new LiveFlickNote(LiveNoteBuilder.Info(1.00f), 80, 2, 2,
                NoteCategory.Flick, 1f, LiveNoteType.Default, NoteDirection.Up, 0.5f, 20f);
            var runnerFlick = new FrameRunner(new List<LiveNoteBase> { flickUp });
            runnerFlick.Step(0.90f);
            runnerFlick.Step(1.00f, new TestInput(5, 5, LiveTouchPhase.Began, 1.00f, 2f));
            int flickJudged = runnerFlick.Step(1.03f,
                new TestInput(5, 5, LiveTouchPhase.Moved, 1.03f, 2f, new Vector2(0f, 40f)));
            check("甩键·方向对 → 判掉",
                flickJudged == 1 && flickUp.State == NoteState.Done && flickUp.Result == NoteResult.Perfect,
                string.Format("判定数={0}、结果={1}/{2}（上滑 40px，晚 30ms 仍在 Flick 完美窗）",
                    flickJudged, flickUp.Result, flickUp.Description));

            var flickWrong = new LiveFlickNote(LiveNoteBuilder.Info(1.00f), 81, 2, 2,
                NoteCategory.Flick, 1f, LiveNoteType.Default, NoteDirection.Down, 0.5f, 20f);
            var runnerWrong = new FrameRunner(new List<LiveNoteBase> { flickWrong });
            runnerWrong.Step(0.90f);
            runnerWrong.Step(1.00f, new TestInput(6, 6, LiveTouchPhase.Began, 1.00f, 2f));
            runnerWrong.Step(1.03f,
                new TestInput(6, 6, LiveTouchPhase.Moved, 1.03f, 2f, new Vector2(0f, 40f)));
            check("甩键·方向错 → Great + FlickMiss",
                flickWrong.State == NoteState.Done && flickWrong.Result == NoteResult.Great
                && flickWrong.Description == NoteResultDescription.FlickMiss,
                string.Format("需要下划、实际 40px 上滑 → {0}/{1}", flickWrong.Result, flickWrong.Description));

            // ── ⑨ 小数轨道解析器（上游 GetJudgmentLane）──────────────────
            var resolver = LiveLaneResolver.CreateFromLaneLayout(PlayVisualSpec.JudgeLineY, 4);
            var center0 = resolver.CenterXOfLane(0);
            var center1 = resolver.CenterXOfLane(1);
            float atCenter = resolver.Resolve(new Vector3(center1, PlayVisualSpec.JudgeLineY, 0f), true);
            float atMiddle = resolver.Resolve(new Vector3((center0 + center1) * 0.5f, PlayVisualSpec.JudgeLineY, 0f), true);
            float beforeLane0 = resolver.Resolve(new Vector3(center0 - 200f, PlayVisualSpec.JudgeLineY, 0f), true);
            float aboveLine = resolver.Resolve(new Vector3(center1, PlayVisualSpec.JudgeLineY + 120f, 0f), true);
            float belowLine = resolver.Resolve(new Vector3(center1, PlayVisualSpec.JudgeLineY - 200f, 0f), true);
            check("小数轨道解析（含越界/线上方）",
                Mathf.Abs(atCenter - 1f) < 1e-3f
                && Mathf.Abs(atMiddle - 0.5f) < 1e-3f
                && beforeLane0 < 0f && aboveLine < 0f && Mathf.Abs(belowLine - 1f) < 1e-3f,
                string.Format("轨1中心={0:0.###}、轨0/1 中点={1:0.###}、越左={2}、线上方={3}、线下={4:0.###}",
                    atCenter, atMiddle, beforeLane0, aboveLine, belowLine));

            // ⑨b 接线层的产品决定：**不启用**判定线的 Y 闸门（闸门交给输入层"屏幕下半部"），
            //     否则滑条/drag 的手指一越过判定线就整段失效（实机表现 = 不跟手）。
            var wired = LiveLaneResolver.CreateFromLaneLayout(PlayVisualSpec.JudgeLineY, 4);
            wired.EnforceJudgmentYGate = false;
            float aboveLineNoGate = wired.Resolve(new Vector3(center1, PlayVisualSpec.JudgeLineY + 900f, 0f), true);
            check("接线层关掉判定线 Y 闸门",
                Mathf.Abs(aboveLineNoGate - 1f) < 1e-3f,
                string.Format("判定线上方 900px 的触摸仍解析为轨 {0:0.###}（区域闸门由输入层负责）", aboveLineNoGate));

            // ── ⑩ 适配层：谱面数据 → 判定音符树 ───────────────────────────
            var chart = BuildTestChart();
            var built = LiveNoteBuilder.Build(chart, 4, 20f, false);
            // 谱面里的 LongEnd 条目只做视图（尾由头的 duration 生成）→ built 少一项，按生成顺序取
            var builtNormal = FindNote(built, 0);
            var builtHold = FindNote(built, 1);
            var builtDrag = FindNote(built, 2);
            var builtSlide = FindNote(built, 3);
            var builtWide = FindNote(built, 4);
            var builtFlick = FindNote(built, 5);
            bool holdShape = builtHold is LiveLongNote
                && builtHold.NoteList.Count == 2
                && Mathf.Abs(builtHold.NoteList[1].MusicScoreInfo.time - 1.75f) < 1e-3f;
            // 长按/滑条的判定跨度比普通音符更宽（LaneOffsetFor：long/slide 0.85 轨）——
            // 这是"长条很容易断"的修法：按住/跟手时手指一定会飘
            bool slideShape = builtSlide is LiveLongNote && builtSlide.NoteList.Count == 2
                && Mathf.Abs(builtSlide.NoteList[1].JudgeLaneEnd - (3f + LiveNoteBuilder.LaneOffsetFor(DataNoteType.Slide))) < 1e-3f
                && LiveNoteBuilder.LaneOffsetFor(DataNoteType.Slide) > LiveNoteBuilder.LaneOffsetFor(DataNoteType.Normal);
            bool wideShape = builtWide != null && builtWide.JudgeLaneStart <= -0.5f && builtWide.JudgeLaneEnd >= 3.5f;
            bool dragShape = builtDrag is LiveFrictionNote;
            bool flickShape = builtFlick is LiveFlickNote && builtFlick.Direction == NoteDirection.Down;
            check("适配层（谱面 → 判定簇）",
                built.Count == 6 && holdShape && slideShape && wideShape && dragShape && flickShape,
                string.Format("音符树 {0} 棵：长按(头+尾 {1})、滑条(头+尾 {2}，跨度 {3:0.#}..{4:0.#})、wide 跨度 {5:0.#}..{6:0.#}、drag={7}、flick={8}",
                    built.Count,
                    builtHold != null ? builtHold.NoteList.Count : -1,
                    builtSlide != null ? builtSlide.NoteList.Count : -1,
                    builtSlide != null ? builtSlide.NoteList[1].JudgeLaneStart : 0f,
                    builtSlide != null ? builtSlide.NoteList[1].JudgeLaneEnd : 0f,
                    builtWide != null ? builtWide.JudgeLaneStart : 0f,
                    builtWide != null ? builtWide.JudgeLaneEnd : 0f,
                    builtDrag != null ? builtDrag.GetType().Name : "无",
                    builtFlick != null ? builtFlick.Direction.ToString() : "无"));

            // ⑩b 适配后的音符能被判定掉（端到端：谱面宽音符 → 按下任意轨都判）
            var wideRun = new FrameRunner(new List<LiveNoteBase> { builtWide });
            wideRun.Step(1.55f);
            int wideJudged = wideRun.Step(1.75f, new TestInput(9, 9, LiveTouchPhase.Began, 1.75f, 3.4f));
            check("适配层·宽音符任意轨可判",
                wideJudged == 1 && builtWide.State == NoteState.Done,
                string.Format("在轨 3.4（跨度外沿）判定数={0}、状态={1}", wideJudged, builtWide.State));

            string summary = string.Format(
                "判定簇自检：{0} 项通过 / {1} 项异常"
                + "（窗口帧口径 / 跨度小数轨道 / 边沿与摩擦 / 拾取 / 归属 / 同指去重 / 每指独立 / "
                + "长按容器 7 项 / 小数轨道解析 / 适配层 2 项）",
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

        // ── 自检工具 ────────────────────────────────────────────────────

        private struct TestInput
        {
            public int touchId;
            public int fingerId;
            public LiveTouchPhase phase;
            public float musicTime;
            public float lane;
            public Vector2 delta;
            public bool directionTrusted;

            public TestInput(int touchId, int fingerId, LiveTouchPhase phase, float musicTime, float lane,
                Vector2 delta = default, bool directionTrusted = true)
            {
                this.touchId = touchId;
                this.fingerId = fingerId;
                this.phase = phase;
                this.musicTime = musicTime;
                this.lane = lane;
                this.delta = delta;
                this.directionTrusted = directionTrusted;
            }
        }

        /// <summary>跑一帧判定：返回这一帧真正判掉的音符数。</summary>
        private static int RunFrame(List<LiveNoteBase> notes, List<TestInput> inputs)
        {
            var core = new LiveJudgeCore();
            int judged = 0;
            Action<LiveNoteBase> onJudged = _ => judged++;
            core.OnJudged += onJudged;
            foreach (var i in inputs)
            {
                core.PushInput(ToTouch(i));
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

        private static LiveTouch ToTouch(TestInput i)
        {
            return new LiveTouch(i.fingerId, i.touchId, i.delta, new Vector3(i.lane, 0f, 0f),
                i.phase, i.musicTime, i.lane, Vector2.zero, i.directionTrusted);
        }

        /// <summary>谱面运行时帧驱动（真实 Update + 判定五步；帧号由 runtime 推进）。</summary>
        private class FrameRunner
        {
            private readonly LiveScoreRuntime runtime = new LiveScoreRuntime();
            private int judged;

            public FrameRunner(List<LiveNoteBase> notes)
            {
                runtime.NoteDisplayTimeOffset = 0.3f;
                runtime.Setup(notes);
                runtime.JudgeCore.OnJudged += _ => judged++;
            }

            /// <summary>推进到 time 并跑一帧判定，返回这一帧判掉的音符数。</summary>
            public int Step(float time, params TestInput[] inputs)
            {
                int before = judged;
                runtime.Update(LiveNoteBuilder.Info(time));
                var touches = new List<LiveTouch>(inputs.Length);
                for (int i = 0; i < inputs.Length; i++) touches.Add(ToTouch(inputs[i]));
                runtime.Judge(touches);
                return judged - before;
            }
        }

        private static (NoteResult result, NoteResultDescription description) DefaultResult(float time, float offset)
        {
            var note = MakeNormal(0f, 0f, 0f, time, 1f);
            return LiveConfig.CalculateDefaultNoteResult(note, time - offset);
        }

        private static (NoteResult result, NoteResultDescription description) FlickResult(float offset)
        {
            var note = new LiveFlickNote(LiveNoteBuilder.Info(1f), 1, 0, 0,
                NoteCategory.Flick, 1f, LiveNoteType.Default, NoteDirection.Up, 0.5f, 20f);
            return LiveConfig.CalculateFlickNoteResult(note, 1f - offset, true);
        }

        private static LiveNormalNote MakeNormal(float laneStart, float laneEnd, float laneOffset, float time, float id)
        {
            return new LiveNormalNote(LiveNoteBuilder.Info(time), (int)id, (int)laneStart, (int)laneEnd,
                NoteCategory.Normal, 1f, LiveNoteType.Default, laneOffset);
        }

        private static LiveFrictionNote MakeFriction(float laneStart, float laneEnd, float laneOffset, float time, float id)
        {
            return new LiveFrictionNote(LiveNoteBuilder.Info(time), (int)id, (int)laneStart, (int)laneEnd,
                NoteCategory.Friction, LiveNoteType.Default, 1f, laneOffset);
        }

        /// <summary>长按：头（Long）+ 尾（Normal/Category=Long，时刻 = tailTime）</summary>
        private static LiveLongNote MakeHold(int lane, float headTime, float tailTime, int id)
        {
            var head = new LiveLongNote(LiveNoteBuilder.Info(headTime), id, lane, lane,
                NoteCategory.Long, LiveNoteType.Default, 1f, 0.5f);
            var tail = new LiveNormalNote(LiveNoteBuilder.Info(tailTime), id, lane, lane,
                NoteCategory.Normal, 1f, LiveNoteType.Default, 0.5f);
            head.AddNoteListAndSetupChildNote(tail);
            return head;
        }

        private static List<NoteData> BuildTestChart()
        {
            var path = new List<SlidePathPoint>
            {
                new SlidePathPoint { t = 0f, x = 1f },
                new SlidePathPoint { t = 0.4f, x = 2f },
                new SlidePathPoint { t = 0.8f, x = 3f },
            };
            return new List<NoteData>
            {
                new NoteData(0, 1.00f, DataNoteType.Normal),
                new NoteData(1, 1.00f, DataNoteType.LongStart, 0.75f, 7),
                new NoteData(1, 1.75f, DataNoteType.LongEnd, 0f, 7),
                new NoteData(2, 1.20f, DataNoteType.Drag),
                new NoteData(1, 1.30f, DataNoteType.Slide, 0.8f, 8, FlickDirection.Up, path),
                new NoteData(0, 1.75f, DataNoteType.Normal, 0f, -1, FlickDirection.Up, null, true),
                new NoteData(3, 2.00f, DataNoteType.Flick, 0f, -1, FlickDirection.Down),
            };
        }

        private static LiveNoteBase FindNote(List<LiveNoteBase> notes, int index)
        {
            return index >= 0 && index < notes.Count ? notes[index] : null;
        }
    }
}
#endif
