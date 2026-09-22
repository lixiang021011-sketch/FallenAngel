#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using FallenAngel.InputSystem;
using FallenAngel.Live;
using UnityEditor;
using UnityEngine;
using DataNoteType = FallenAngel.Data.NoteType;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定时序自检（play 级）：**一次按下到底判掉几个音符**，长按/拖键/甩键端到端是否成立。
    ///
    /// 背景：规则全部来自 Core/Live 的判定簇（OpenSekai 移植），本自检负责**在真实装配下**
    /// （GameManager + JudgeManager + InputManager + 谱面）验它的外部可见行为：
    ///   · 一次按下只判"该判的那个"（同一根手指多候选时按最近/最早的取舍）；
    ///   · 真双押（不同轨同刻、两根手指）各判一个；
    ///   · 普通音符只认**按下边沿**：按住不放不会把随后进窗的音符补判掉（超窗即 Miss）；
    ///   · 宽音符（跨度铺满所有轨）任意轨按下都能判；
    ///   · 输入事件时间戳参与判定（不是"这一帧读到输入时的歌曲时间"）；
    ///   · 长按：头不计分、按住期间不掉、松手在尾部时刻结算（一条长按 = 1 个音符）；
    ///   · 不按 = 自动 Miss，且长按只记 1 个 Miss（不会头尾各算一个）；
    ///   · 局内 modifier 的判定窗口倍率真的作用到移植层的窗口表。
    ///
    /// 只读 + 临时对象（沙箱里现装 Manager，不碰场景与资源）；
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.JudgeTimingChecks.Run）两用。
    /// </summary>
    public static class JudgeTimingChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Judge Timing")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            int savedLanes = LaneLayout.ActiveLaneCount;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                LaneLayout.SetActiveLaneCount(5);
                RunCases(check);
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }
            finally
            {
                LaneLayout.SetActiveLaneCount(savedLanes);
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                LiveConfig.SetJudgeTimeScale(1f);
            }

            string summary = string.Format(
                "判定时序自检：{0} 项通过 / {1} 项异常"
                + "（一次按下只判一个 / 真双押 / 只认按下边沿 / 宽音符任意轨 / 时间戳参与判定 / "
                + "长按端到端 / 自动 Miss 收口 / modifier 倍率 / 空按不判 / 视图接线 / "
                + "命中误差采样端到端 + 门槛）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[JudgeTiming] " + line);
            Debug.Log("[JudgeTiming] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/judge_timing_check.txt", lines.ToArray());
                File.AppendAllText("Logs/judge_timing_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[JudgeTiming] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static void RunCases(Action<string, bool, string> check)
        {
            // ⓪ 沙箱接线：单例、输入事件、谱面装配都得真接上，否则后面的用例会"全 0 通过"
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.BuildScore();
                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();

                var live = sb.Live(0);
                check("沙箱接线（端到端）",
                    live != null && live.State == NoteState.Done && live.Result == NoteResult.JustPerfect
                    && sb.Judge.PerfectCount == 1 && sb.InstancesMatch && sb.InputWired,
                    string.Format("单例={0}、输入事件={1}、按下后 状态={2} 结果={3}、Perfect={4}",
                        sb.InstancesMatch ? "接上" : "缺失", sb.InputWired ? "接上" : "缺失",
                        live != null ? live.State.ToString() : "无",
                        live != null ? live.Result.ToString() : "无", sb.Judge.PerfectCount));
            }

            // ① 相邻音符（相差 20ms）：一次按下只判最近的那个，第二个要再按一次
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.AddChartNote(new NoteData(2, 10.020f, DataNoteType.Normal));
                sb.BuildScore();

                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                bool firstOnly = sb.Live(0).State == NoteState.Done && sb.Live(1).State != NoteState.Done;

                sb.SetSongTime(10.020f);
                sb.PressAt(2, 10.020f);
                sb.JudgeUpdate();
                bool bothJudged = sb.Live(0).State == NoteState.Done && sb.Live(1).State == NoteState.Done;

                check("一次按下只判一个",
                    firstOnly && bothJudged && sb.Judge.PerfectCount == 2,
                    string.Format("第一次按下：{0}（10.000={1} / 10.020={2}）；第二次按下后两个都判={3}、Perfect={4}",
                        firstOnly ? "只判 10.000" : "判了多个",
                        sb.Live(0).Result, sb.Live(1).Result, bothJudged, sb.Judge.PerfectCount));
            }

            // ② 真双押（不同轨同刻，两根手指）：各判一个
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(0, 10.000f, DataNoteType.Normal));
                sb.AddChartNote(new NoteData(4, 10.000f, DataNoteType.Normal));
                sb.BuildScore();

                sb.SetSongTime(10.000f);
                sb.PressAtAs(0, 10.000f, 11);
                sb.PressAtAs(4, 10.000f, 12);
                sb.JudgeUpdate();

                check("真双押（双指同刻）",
                    sb.Live(0).State == NoteState.Done && sb.Live(1).State == NoteState.Done
                    && sb.Judge.PerfectCount == 2,
                    string.Format("轨0={0}、轨4={1}、Perfect={2}",
                        sb.Live(0).Result, sb.Live(1).Result, sb.Judge.PerfectCount));
            }

            // ③ 只认按下边沿：按住不放 → 随后进窗的普通音符不会被补判（超窗即 Miss）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.BuildScore();

                sb.SetSongTime(9.850f);
                sb.PressAt(2, 9.850f);      // 窗口外按下（±125ms 之外）
                sb.JudgeUpdate();
                bool judgeAtPress = sb.Live(0).State == NoteState.Done;

                for (float t = 9.900f; t <= 10.350f; t += 0.050f)
                {
                    sb.SetSongTime(t);
                    sb.JudgeUpdate();       // 手指仍按着 → 持续输入（Stationary）
                }

                var live = sb.Live(0);
                check("只认按下边沿（按住不补判）",
                    !judgeAtPress && live.State == NoteState.Done && live.Result == NoteResult.Miss
                    && sb.Judge.MissCount == 1,
                    string.Format("按下时判={0}；按住到过窗 → 状态={1} 结果={2}、Miss={3}",
                        judgeAtPress, live.State, live.Result, sb.Judge.MissCount));
            }

            // ④ 宽音符（跨度铺满所有轨）：任意轨按下都能判
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(0, 10.000f, DataNoteType.Normal, 0f, -1, FlickDirection.Up, null, true));
                sb.BuildScore();

                sb.SetSongTime(10.000f);
                sb.PressAt(3, 10.000f);
                sb.JudgeUpdate();

                var live = sb.Live(0);
                check("宽音符任意轨可判",
                    live.State == NoteState.Done && live.Result == NoteResult.JustPerfect,
                    string.Format("在轨 3 按下（跨度 {0:0.#}..{1:0.#}）→ {2}/{3}",
                        live.JudgeLaneStart, live.JudgeLaneEnd, live.State, live.Result));
            }

            // ⑤ 输入事件时间戳参与判定：帧时间 9.95、事件时间 10.045（晚 45ms）→ Great(Late)
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.BuildScore();

                sb.SetSongTime(9.950f);
                sb.PressAt(2, 10.045f);
                sb.JudgeUpdate();

                var live = sb.Live(0);
                check("输入时间戳参与判定",
                    live.State == NoteState.Done && live.Result == NoteResult.Great
                    && live.Description == NoteResultDescription.Late,
                    string.Format("帧 9.95s 注入事件时间 10.045s → {0}/{1}（按帧时间算会是 Perfect）",
                        live.Result, live.Description));
            }

            // ⑥ 长按端到端：头判不计分 → 按住保持 → 尾部时刻松手结算（一条长按 = 1 个音符）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.LongStart, 0.500f, 7));
                sb.AddChartNote(new NoteData(2, 10.500f, DataNoteType.LongEnd, 0f, 7));
                sb.BuildScore();

                sb.SetSongTime(9.500f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                var head = sb.Live(0);
                bool headHeld = (head.State == NoteState.InputBegan || head.State == NoteState.Input)
                    && sb.Judge.PerfectCount == 0;

                sb.SetSongTime(10.250f);
                sb.JudgeUpdate();               // 手指仍按着
                bool heldMidway = head.State == NoteState.InputBegan || head.State == NoteState.Input;

                sb.SetSongTime(10.500f);
                sb.ReleaseAt(2, 10.500f);
                sb.JudgeUpdate();
                var tail = head.NoteList[head.NoteList.Count - 1];

                check("长按端到端（头不计分·尾结算）",
                    headHeld && heldMidway && tail.State == NoteState.Done && head.State == NoteState.Done
                    && sb.Judge.PerfectCount == 1 && sb.Judge.Combo == 1
                    && (tail.Result == NoteResult.JustPerfect || tail.Result == NoteResult.Perfect),
                    string.Format("头判={0}（Perfect={1}）、按住中={2}、尾={3}、整条={4}、计分 {5} 个",
                        head.Result, headHeld ? 0 : sb.Judge.PerfectCount, heldMidway, tail.Result,
                        head.State, sb.Judge.PerfectCount));
            }

            // ⑦ 自动 Miss 收口：普通音符 1 个 Miss；长按同样只记 1 个 Miss（头尾不各算一个）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.AddChartNote(new NoteData(1, 11.000f, DataNoteType.LongStart, 0.400f, 9));
                sb.AddChartNote(new NoteData(1, 11.400f, DataNoteType.LongEnd, 0f, 9));
                sb.BuildScore();

                for (float t = 9.500f; t <= 12.000f; t += 0.100f)
                {
                    sb.SetSongTime(t);
                    sb.JudgeUpdate();
                }

                var tap = sb.Live(0);
                var hold = sb.Live(1);
                var tail = hold.NoteList[hold.NoteList.Count - 1];
                bool allDone = tap.State == NoteState.Done && hold.State == NoteState.Done
                    && tail.State == NoteState.Done;
                check("自动 Miss 收口",
                    allDone && sb.Judge.MissCount == 2 && sb.Judge.Combo == 0,
                    string.Format("普通={0}、长按头={1}/尾={2}、Miss 计数={3}（长按只记 1 个）",
                        tap.Result, hold.Result, tail.Result, sb.Judge.MissCount));
            }

            // ⑧ modifier 倍率真的作用到移植层窗口表：晚 90ms 基础=Good，放宽 1.5× 后=Great
            float baseResult = LateResultWithScale(1.0f);
            float scaledResult = LateResultWithScale(1.5f);
            check("modifier 倍率进窗口表",
                Mathf.Approximately(baseResult, (float)JudgeResultType.Good)
                && Mathf.Approximately(scaledResult, (float)JudgeResultType.Great),
                string.Format("晚 90ms：基础={0}、1.5× 放宽={1}",
                    (JudgeResultType)baseResult, (JudgeResultType)scaledResult));

            // ⑨ 空按不判：附近没有音符时按下 → 不判、不计分
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 20.000f, DataNoteType.Normal));
                sb.BuildScore();

                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();

                check("空按不判",
                    sb.Live(0).State != NoteState.Done
                    && sb.Judge.PerfectCount == 0 && sb.Judge.MissCount == 0,
                    string.Format("远处音符状态={0}、Perfect={1}、Miss={2}",
                        sb.Live(0).State, sb.Judge.PerfectCount, sb.Judge.MissCount));
            }

            // ⑩ 视图接线：NoteSpawner 作为纯视图——按谱面生成 → 判定结果落 FX → 收尾后回收（不泄漏）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.BuildScore();
                sb.StartSong();                       // NoteSpawner.OnGameStart：清空 + 开始生成

                sb.SetSongTime(8.500f);               // 生成阈值 = SongTime + 下落时长 → 音符该出生了
                sb.SpawnerView();
                var view = sb.Spawner.FindViewNote(sb.Live(0));
                bool spawned = sb.Spawner.ActiveNotes.Count == 1 && view != null;

                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                bool linked = sb.Spawner.FindViewNote(sb.Live(0)) == view;
                bool hitShown = view != null && view.IsJudged && view.JudgeResult == JudgeResultType.Perfect;

                sb.SpawnerView();
                bool keptDuringFx = sb.Spawner.FindViewNote(sb.Live(0)) == view;   // 反馈期间不回收

                sb.SetSongTime(12.000f);              // 收尾超过兜底保留时长 → 回池
                sb.SpawnerView();

                check("视图接线（纯视图·生成/反馈/回收）",
                    spawned && hitShown && linked && keptDuringFx && sb.Spawner.ActiveNotes.Count == 0,
                    string.Format("生成={0}、判定落到视图={1}、映射一致={2}、反馈期间保留={3}、收尾后活跃={4}",
                        spawned, hitShown, linked, keptDuringFx, sb.Spawner.ActiveNotes.Count));
            }

            // ── ⑪–⑯ 按音符类型过一遍"产品期望"（每类都有自己的语义，最容易错的就是这几条）──
            // ⑪ 普通点击：窗内 = 档位正确、计数 1
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                sb.BuildScore();
                sb.SetSongTime(9.950f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.060f);
                sb.PressAt(2, 10.060f);          // 晚 60ms
                sb.JudgeUpdate();
                var live = sb.Live(0);
                check("普通·晚 60ms = Great",
                    live.State == NoteState.Done && live.Result == NoteResult.Great
                    && sb.Judge.GreatCount == 1 && sb.Judge.MissCount == 0,
                    string.Format("{0}/{1}、Great={2}", live.Result, live.Description, sb.Judge.GreatCount));
            }

            // ⑫ drag（摩擦）：**认领在窗内、结算在判定线**（作者 2026-09-22 定案）
            //    作者反馈"黄键如果提前按住会以最早的位置提前判掉" —— 早碰不许提前结算，
            //    但早碰要锁定（不会因为"碰到时还没到线"而漏判），到线那一刻判 JustPerfect。
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Drag));
                sb.BuildScore();
                sb.SetSongTime(9.950f);
                sb.JudgeUpdate();
                sb.SetSongTime(9.920f);
                sb.PressAt(2, 9.920f);           // 音符前 80ms 按住（窗内）
                sb.JudgeUpdate();
                var live = sb.Live(0);
                bool notJudgedEarly = live.State != NoteState.Done && sb.Judge.PerfectCount == 0;

                for (int i = 0; i <= 9; i++)      // 一直按着走到 10.01s（过判定线）
                {
                    sb.SetSongTime(9.920f + i * 0.010f);
                    sb.JudgeUpdate();
                }

                check("drag·提前按住不提前判、到线才结算",
                    notJudgedEarly && live.State == NoteState.Done && live.Result == NoteResult.JustPerfect
                    && sb.Judge.PerfectCount == 1,
                    string.Format("早 80ms 按住时未判={0}；到线后 {1}/Perfect={2}",
                        notJudgedEarly, live.Result, sb.Judge.PerfectCount));
            }

            // ⑬ 甩键：**累计**滑动幅度够大就要算（慢甩 5px×6 帧 = 30px 也该判）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Flick, 0f, -1, FlickDirection.Up));
                sb.BuildScore();
                sb.SetSongTime(9.980f);
                sb.PressAt(2, 9.980f);
                sb.JudgeUpdate();
                for (int i = 1; i <= 6; i++)
                {
                    sb.SetSongTime(9.980f + i * 0.008f);
                    sb.SwipeAt(2, 9.980f + i * 0.008f, 0f, 5f * i);   // 每帧 5px 上滑（传累计位置）
                    sb.JudgeUpdate();
                }
                var live = sb.Live(0);
                check("甩键·慢甩累计幅度也算",
                    live.State == NoteState.Done && live.Result > NoteResult.Pass,
                    string.Format("6 帧累计 30px 上滑 → {0}/{1}", live.Result, live.Description));
            }

            // ⑭ 甩键方向错：不判 Miss，走 FlickMiss 分支（Great）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Flick, 0f, -1, FlickDirection.Down));
                sb.BuildScore();
                sb.SetSongTime(9.995f);
                sb.PressAt(2, 9.995f);
                sb.JudgeUpdate();
                for (int i = 1; i <= 4; i++)
                {
                    sb.SetSongTime(9.995f + i * 0.008f);
                    sb.SwipeAt(2, 9.995f + i * 0.008f, 0f, 10f * i);  // 上滑（与需要的下划相反）
                    sb.JudgeUpdate();
                }
                var live = sb.Live(0);
                check("甩键·方向错 = Great/FlickMiss",
                    live.State == NoteState.Done && live.Result == NoteResult.Great
                    && live.Description == NoteResultDescription.FlickMiss,
                    string.Format("需要下划、实际上滑 → {0}/{1}", live.Result, live.Description));
            }

            // ⑭b 甩键·**同轨移动**：真机上"手指在轨内滑动"过去**不发事件**，判定层拿不到滑动帧
            //     （作者反馈"粉键识别不了"）。这条断言走的是与真机同一条路（InputManager 的 Moved 事件）。
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Flick, 0f, -1, FlickDirection.Up));
                sb.BuildScore();
                float flickDist = LiveNoteBuilder.DefaultFlickDistancePixels;
                float step = flickDist * 0.6f;        // 单帧略小于阈值：只有"收到移动帧"才会判
                sb.SetSongTime(9.985f);
                sb.PressAtAs(2, 9.985f, 5);
                sb.JudgeUpdate();
                for (int i = 1; i <= 3; i++)
                {
                    sb.SetSongTime(9.985f + i * 0.005f);
                    sb.MoveAt(2, 9.985f + i * 0.005f, 0f, step * i, 5);   // 同轨上滑（Moved 相位）
                    sb.JudgeUpdate();
                }
                var live = sb.Live(0);
                check("甩键·同轨移动（Moved）能判",
                    live.State == NoteState.Done && live.Result > NoteResult.Pass,
                    string.Format("逐帧 {0:0.#}px 上滑（阈值 {1:0.#}px）→ {2}/{3}",
                        step, flickDist, live.Result, live.Description));
            }

            // ⑭c 甩键·**跨轨移动**不得重置累计幅度：跨轨在过去被当成"新按下(Began)"，
            //     累计量清零 → 斜着甩 / 手抖过轨就再也判不出来。这条用"两侧各半"的幅度做判别：
            //     单侧不够阈值、跨轨后累加才过阈值 —— 只要中途被清零，就当帧判不出来。
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Flick, 0f, -1, FlickDirection.Up));
                sb.BuildScore();
                // 判定层把每帧位移按 Time.deltaTime 归一化到 60fps 口径（慢甩累计的同一处），
                // 批处理里的 deltaTime 与真机不同 → 用同一个换算把"单侧不到阈值"做成**归一化后**的判据。
                float amp = Time.deltaTime > 0.0001f ? Time.deltaTime / 0.016667f : 1f;
                float half = LiveNoteBuilder.DefaultFlickDistancePixels * 0.75f * amp;   // 单侧不到阈值
                sb.SetSongTime(9.980f);
                sb.PressAtAs(2, 9.980f, 5);
                sb.JudgeUpdate();
                sb.SetSongTime(9.990f);
                sb.MoveAt(2, 9.990f, 0f, half, 5);                    // 轨 2 内上滑半程
                sb.JudgeUpdate();
                var live = sb.Live(0);
                bool notYet = live.State != NoteState.Done;
                sb.SetSongTime(10.000f);
                sb.MoveAt(1, 10.000f, 0f, half * 2f, 5);              // 跨到轨 1，继续上滑（累计 1.5×阈值）
                sb.JudgeUpdate();
                check("甩键·跨轨移动不重置累计",
                    notYet && live.State == NoteState.Done && live.Result > NoteResult.Pass,
                    string.Format("轨内半程未判={0}；跨轨后累计 {1:0.##}×阈值 → {2}",
                        notYet, (half * 2f) / Mathf.Max(1f, LiveNoteBuilder.DefaultFlickDistancePixels * amp), live.Result));
            }

            // ⑭d 甩键·**0 号手指**：Unity 的 fingerId 从 0 开始（判定簇内部拿 0 当"空槽"哨兵，
            //     上游 touchId 从 1 开始）。没做换算时，第一根手指甩粉键永远判不出来。
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Flick, 0f, -1, FlickDirection.Up));
                sb.BuildScore();
                float step = LiveNoteBuilder.DefaultFlickDistancePixels * 0.8f;
                sb.SetSongTime(9.985f);
                sb.PressAtAs(2, 9.985f, 0);       // fingerId = 0
                sb.JudgeUpdate();
                for (int i = 1; i <= 3; i++)
                {
                    sb.SetSongTime(9.985f + i * 0.005f);
                    sb.MoveAt(2, 9.985f + i * 0.005f, 0f, step * i, 0);
                    sb.JudgeUpdate();
                }
                var live = sb.Live(0);
                check("甩键·0 号手指（fingerId=0）能判",
                    live.State == NoteState.Done && live.Result > NoteResult.Pass,
                    string.Format("fingerId=0 上滑 → {0}/{1}", live.Result, live.Description));
            }

            // ⑮ 长按**按住到尾巴**（不松手）：产品语义 = 成功；绝不能因为"没在窗口内松手"判 Miss
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.LongStart, 0.500f, 21));
                sb.AddChartNote(new NoteData(2, 10.500f, DataNoteType.LongEnd, 0f, 21));
                sb.BuildScore();
                sb.SetSongTime(9.500f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                for (float t = 10.100f; t <= 10.800f; t += 0.100f)
                {
                    sb.SetSongTime(t);
                    sb.JudgeUpdate();            // 一直按着（Stationary），直到尾巴之后 0.3s 也不松
                }
                var head = sb.Live(0);
                var tail = head.NoteList[head.NoteList.Count - 1];
                check("长按·按住过尾 = 成功",
                    tail.State == NoteState.Done && tail.Result > NoteResult.Pass
                    && head.State == NoteState.Done && head.Result > NoteResult.Pass
                    && sb.Judge.MissCount == 0,
                    string.Format("尾={0}、整条={1}、Miss={2}（按住不松手到 10.8s）",
                        tail.Result, head.Result, sb.Judge.MissCount));
            }

            // ⑯ 长按掉指后重拾：中途松手再按回来，尾部仍能判（"掉段不断整条"）
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.LongStart, 0.500f, 22));
                sb.AddChartNote(new NoteData(2, 10.500f, DataNoteType.LongEnd, 0f, 22));
                sb.BuildScore();
                sb.SetSongTime(9.500f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.150f);
                sb.ReleaseAt(2, 10.150f);        // 中途松手（在尾部窗口之外）
                sb.JudgeUpdate();
                sb.SetSongTime(10.480f);
                sb.PressAt(2, 10.480f);          // 尾部窗口内按回来
                sb.JudgeUpdate();
                sb.SetSongTime(10.500f);
                sb.ReleaseAt(2, 10.500f);
                sb.JudgeUpdate();
                var head = sb.Live(0);
                var tail = head.NoteList[head.NoteList.Count - 1];
                check("长按·掉指重拾后尾部成功",
                    tail.State == NoteState.Done && tail.Result > NoteResult.Pass
                    && sb.Judge.MissCount == 0,
                    string.Format("（10.15 松手 → 10.48 按回 → 10.50 松手）尾={0}、Miss={1}",
                        tail.Result, sb.Judge.MissCount));
            }

            // ⑰ 滑条：沿路径跟到尾部再松手 = 成功；且跨度跟谱面 path（弯道不脱手）
            // ⑱ 长音持续反馈：按住期间 combo 持续增长（连击点每 0.4s 一个），不再"按住 2 秒没反应"
            using (var sb = new Sandbox())
            {
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.LongStart, 2.000f, 41));
                sb.AddChartNote(new NoteData(2, 12.000f, DataNoteType.LongEnd, 0f, 41));
                sb.BuildScore();
                sb.SetSongTime(9.500f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                int afterHead = sb.Judge.Combo;
                for (float t = 10.400f; t <= 11.700f; t += 0.100f)
                {
                    sb.SetSongTime(t);
                    sb.JudgeUpdate();      // 一直按着
                }
                int holding = sb.Judge.Combo;
                check("长音·按住期间持续涨连击",
                    afterHead == 0 && holding >= 3 && sb.Judge.MissCount == 0,
                    string.Format("头判后 combo={0}、按住 1.7s 后 combo={1}、Miss={2}（连击点每 0.4s 一个）",
                        afterHead, holding, sb.Judge.MissCount));
            }

            using (var sb = new Sandbox())
            {
                var path = new System.Collections.Generic.List<SlidePathPoint>
                {
                    new SlidePathPoint { t = 0f, x = 1f },
                    new SlidePathPoint { t = 0.3f, x = 2f },
                    new SlidePathPoint { t = 0.6f, x = 3f },
                };
                sb.AddChartNote(new NoteData(1, 10.000f, DataNoteType.Slide, 0.600f, 31,
                    FlickDirection.Up, path));
                sb.AddChartNote(new NoteData(3, 10.600f, DataNoteType.LongEnd, 0f, 31));
                sb.BuildScore();
                sb.SetSongTime(9.500f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.PressAt(1, 10.000f);
                sb.JudgeUpdate();
                sb.SetSongTime(10.300f);
                sb.SwipeAt(2, 10.300f, 0f, 0f);   // 跟到轨 2（路径中点）
                sb.JudgeUpdate();
                sb.SetSongTime(10.600f);
                sb.SwipeAt(3, 10.600f, 0f, 0f);   // 跟到轨 3（路径终点）
                sb.JudgeUpdate();
                sb.ReleaseAt(3, 10.600f);
                sb.JudgeUpdate();
                var head = sb.Live(0);
                var tail = head.NoteList[head.NoteList.Count - 1];
                check("滑条·沿路径跟到尾部 = 成功",
                    tail.State == NoteState.Done && tail.Result > NoteResult.Pass
                    && sb.Judge.MissCount == 0,
                    string.Format("尾={0}、Miss={1}", tail.Result, sb.Judge.MissCount));
            }

            // ⑫ 命中误差采样（osu! 式统计校准的数据源）：判定真的带上"打早/打晚"，曲终结算成样本。
            //    作者反馈"打了三局设置页还是没建议值" → 这一条就是防回归的端到端断言。
            string savedHistory = PlayerPrefs.GetString("FA_HitErrorHistory", string.Empty);
            float savedTiming = CalibrationSettings.TimingAdjustData;
            try
            {
                HitErrorHistory.Clear();
                HitErrorHistory.InvalidateCache();

                using (var sb = new Sandbox())
                {
                    const int notes = 16;                       // ≥ HitErrorTracker.MinJudgeCountPerPlay
                    for (int i = 0; i < notes; i++)
                        sb.AddChartNote(new NoteData(2, 10f + i * 0.4f, DataNoteType.Normal));
                    sb.BuildScore();
                    sb.StartSong();                             // 视图开演（真实演奏里 Manager 回调由视图回收触发）

                    bool everyNoteHasError = true;
                    int collectedAfterFirst = -1;
                    for (int i = 0; i < notes; i++)
                    {
                        float t = 10f + i * 0.4f;
                        sb.SetSongTime(t + 0.03f);              // 一律打晚 30ms
                        sb.PressAt(2, t + 0.03f);
                        sb.JudgeUpdate();
                        sb.SpawnerView();                       // 生成/回收（回调面在这里触发）
                        if (i == 0) collectedAfterFirst = sb.SessionHitErrorCount;
                        var live = sb.Live(i);
                        everyNoteHasError &= live != null && live.HasJudgedError
                                             && Mathf.Abs(live.JudgedErrorSeconds - 0.03f) < 0.005f;
                    }

                    sb.SetSongTime(10f + notes * 0.4f + 3f);    // 跑完最后一段，让视图把残留音符收干净
                    sb.SpawnerView();
                    int collected = sb.SessionHitErrorCount;    // 本局已收多少条误差
                    int before = HitErrorHistory.Count;
                    sb.SetState(GameState.Result);              // 曲终
                    sb.JudgeUpdate();
                    int after = HitErrorHistory.Count;

                    float suggested;
                    bool has = HitErrorHistory.TrySuggestFrames(out suggested);
                    check("命中误差采样（端到端）",
                        everyNoteHasError && after == before + 1 && has
                        && Mathf.Abs(HitErrorTracker.FramesToMs(suggested) + 30f) < 3f,
                        string.Format("16 次判定都带误差={0}、首判后收集={1}、曲终前收集 {2} 条、样本 {3}→{4}、建议 {5:0.0}ms",
                            everyNoteHasError, collectedAfterFirst, collected, before, after,
                            has ? HitErrorTracker.FramesToMs(suggested) : 0f));
                }

                HitErrorHistory.Clear();
                HitErrorHistory.InvalidateCache();
                using (var sb = new Sandbox())
                {
                    const int few = 5;                          // < 门槛
                    for (int i = 0; i < few; i++)
                        sb.AddChartNote(new NoteData(2, 10f + i * 0.4f, DataNoteType.Normal));
                    sb.BuildScore();
                    for (int i = 0; i < few; i++)
                    {
                        float t = 10f + i * 0.4f;
                        sb.SetSongTime(t);
                        sb.PressAt(2, t);
                        sb.JudgeUpdate();
                    }
                    sb.SetState(GameState.Result);
                    sb.JudgeUpdate();
                    check("命中误差采样·不足门槛不出样本",
                        HitErrorHistory.Count == 0,
                        string.Format("只判 {0} 次（< {1}）→ 样本 {2} 个",
                            few, HitErrorTracker.MinJudgeCountPerPlay, HitErrorHistory.Count));
                }
            }
            finally
            {
                if (string.IsNullOrEmpty(savedHistory)) PlayerPrefs.DeleteKey("FA_HitErrorHistory");
                else PlayerPrefs.SetString("FA_HitErrorHistory", savedHistory);
                CalibrationSettings.TimingAdjustData = savedTiming;
                HitErrorHistory.InvalidateCache();
                PlayerPrefs.Save();
            }

            // ⑲ 输入层·触摸样本 → 音轨事件（真机路径的回归）。
            //    作者反馈的"粉键识别不了"有两层：① 轨内滑动**不发事件**（判定层收不到滑动帧）；
            //    ② 跨轨滑动被当成"新按下(Began)"（甩键累计幅度被清零）。这条逐样本断言事件序列，
            //    批处理里也能跑（用探针替换掉 Canvas 与 Screen 尺寸这两个环境依赖）。
            {
                var host = new GameObject("__JudgeTimingTouchProbe");
                try
                {
                    var input = host.AddComponent<InputManager>();
                    input.LaneProbe = pos => Mathf.Clamp(Mathf.FloorToInt(pos.x / 100f), 0, 4);
                    input.InHitZoneProbe = pos => pos.x >= 0f;
                    typeof(InputManager)
                        .GetMethod("EnsureLaneArrays", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(input, null);

                    var log = new List<LaneInputArgs>();
                    input.OnLaneInput += (s, e) => log.Add(e);

                    input.ProcessTouchSample(3, new Vector2(150f, 10f), TouchPhase.Began, -1);
                    bool begun = log.Count == 1 && log[0].isPressed && !log[0].isMove && log[0].laneIndex == 1;

                    input.ProcessTouchSample(3, new Vector2(160f, 40f), TouchPhase.Moved, -1);
                    bool sameLaneMove = log.Count == 2 && log[1].isPressed && log[1].isMove && log[1].laneIndex == 1;

                    input.ProcessTouchSample(3, new Vector2(250f, 60f), TouchPhase.Moved, -1);
                    bool crossLane = log.Count == 4
                        && !log[2].isPressed && log[2].laneIndex == 1
                        && log[3].isPressed && log[3].isMove && log[3].laneIndex == 2;

                    input.ProcessTouchSample(3, new Vector2(250f, 60f), TouchPhase.Stationary, -1);
                    bool stationarySilent = log.Count == 4;

                    input.ProcessTouchSample(3, new Vector2(250f, 60f), TouchPhase.Ended, -1);
                    bool ended = log.Count == 5 && !log[4].isPressed && log[4].laneIndex == 2;

                    check("输入层·滑动事件序列（isMove）",
                        begun && sameLaneMove && crossLane && stationarySilent && ended,
                        string.Format("按下{0}/同轨移动{1}/跨轨(释放+移动){2}/静止不发{3}/抬起{4}，共 {5} 条事件",
                            begun, sameLaneMove, crossLane, stationarySilent, ended, log.Count));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            // ⑳ 长条真值几何（照抄上游 NoteLineView / LongNoteView：头尾两点由**时间**推，不做像素 clamp）：
            //    尾巴到判定线的距离必须 = 剩余时间 × 速度。旧实现把长度夹在 [300, 一屏]，长条尾巴只有
            //    音符 50% 的速度（作者报的"长条下落速度会变慢"）—— 这条断言在旧实现上必红。
            using (var sb = new Sandbox())
            {
                const float dur = 3.0f;
                const int holdId = 51;
                sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.LongStart, dur, holdId));
                sb.AddChartNote(new NoteData(2, 10.000f + dur, DataNoteType.LongEnd, 0f, holdId));
                sb.BuildScore();
                sb.StartSong();
                sb.SetSongTime(9.500f);
                sb.SpawnerView();
                sb.JudgeUpdate();
                sb.SetSongTime(10.000f);
                sb.SpawnerView();
                sb.PressAt(2, 10.000f);
                sb.JudgeUpdate();
                sb.SpawnerView();
                for (int i = 1; i <= 10; i++)      // 按住 1s（HoldProgress = 1/3）
                {
                    sb.SetSongTime(10.000f + i * 0.1f);
                    sb.JudgeUpdate();
                    sb.SpawnerView();
                }

                float fallDistance = Mathf.Abs(sb.Spawner.SpawnY - sb.Spawner.JudgeLineY);
                float fallTime = sb.Game.ActualFallTime;
                float trueLen = fallDistance * (dur / fallTime);
                float expectY = sb.Spawner.JudgeLineY + trueLen * (1f - 1f / 3f);
                float got = sb.LongEndViewY(holdId);
                bool overOneScreen = trueLen > fallDistance;    // 3s @ 2s 下落 = 1.5 个落距（旧实现会被夹成 1 个）
                check("长条·尾巴位置 = 剩余时间×速度",
                    overOneScreen && !float.IsNaN(got) && Mathf.Abs(got - expectY) < 2f,
                    string.Format("3s 长条 @ {0:0.##}s 下落：真长 {1:0.#}（落距 {2:0.#}）、按住 1/3 → 尾巴 y={3:0.#} 期望 {4:0.#}",
                        fallTime, trueLen, fallDistance, got, expectY));
            }

            // ㉑ 层级：对象池取出的音符必须排到容器最后 —— 等价上游"音符 sprite 恒在长条身体 mesh 之上"。
            //    池化后绘制顺序原本 = 对象最初创建顺序（与谱面顺序无关），长条身体（0.9 填充 + 0.95 边线）
            //    会盖住复用旧对象的音符（作者 2026-09-21 "长音符拖到后来的音符"）。
            using (var sb = new Sandbox())
            {
                Note first = sb.TakeFromPool();
                int lastAtFirst = sb.ContainerChildCount - 1;      // 取第一个时它就该已经在最后
                int idxFirst = first != null ? first.transform.GetSiblingIndex() : -1;
                Note second = sb.TakeFromPool();
                int idxSecond = second != null ? second.transform.GetSiblingIndex() : -1;
                int last = sb.ContainerChildCount - 1;
                check("层级·池取出的音符排到最后",
                    first != null && second != null && idxFirst == lastAtFirst && idxSecond == last && idxSecond > idxFirst,
                    string.Format("容器 {0} 个子节点：第 1 个取到时 sibling={1}（期望 {2}）、第 2 个 sibling={3}（期望 {4}）",
                        sb.ContainerChildCount, idxFirst, lastAtFirst, idxSecond, last));
                sb.ReturnToPool(first);
                sb.ReturnToPool(second);
            }
        }

        /// <summary>晚 90ms 按下（同一个音符），返回判定档——用来验窗口倍率是否进表</summary>
        private static float LateResultWithScale(float scale)
        {
            try
            {
                using (var sb = new Sandbox())
                {
                    sb.AddChartNote(new NoteData(2, 10.000f, DataNoteType.Normal));
                    sb.BuildScore();
                    // 注意顺序：沙箱构造会把 modifier 复位，倍率必须在沙箱就绪之后再施加
                    PlayRules.SetModifierState(scale, true, true, 1f, 0f, 1f);
                    sb.SetSongTime(9.910f);
                    sb.PressAt(2, 10.090f);   // 音符 10.000 → 晚 90ms
                    sb.JudgeUpdate();
                    var live = sb.Live(0);
                    return live.State == NoteState.Done ? (float)MapToJudgeType(live.Result) : -1f;
                }
            }
            finally
            {
                PlayRules.ResetModifiers();
            }
        }

        private static JudgeResultType MapToJudgeType(NoteResult result)
        {
            switch (result)
            {
                case NoteResult.JustPerfect:
                case NoteResult.Perfect: return JudgeResultType.Perfect;
                case NoteResult.Great: return JudgeResultType.Great;
                case NoteResult.Good: return JudgeResultType.Good;
                case NoteResult.Bad: return JudgeResultType.Bad;
                case NoteResult.Miss: return JudgeResultType.Miss;
                default: return JudgeResultType.None;
            }
        }

        /// <summary>
        /// 判定沙箱：现装一整套 GameManager + JudgeManager + InputManager（判定簇跑在真实壳里），
        /// 歌曲时间与运行状态由自检显式控制（batchmode 下同步执行，不会真跑 Update）。
        /// </summary>
        private sealed class Sandbox : IDisposable
        {
            private static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
            private static readonly FieldInfo FLaneInputEvent = typeof(InputManager).GetField("OnLaneInput", Priv);
            private static readonly PropertyInfo PGameInstance =
                typeof(GameManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PInputInstance =
                typeof(InputManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PJudgeInstance =
                typeof(JudgeManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PSongTime =
                typeof(GameManager).GetProperty("SongTime", Priv | BindingFlags.Public);
            private static readonly PropertyInfo PState =
                typeof(GameManager).GetProperty("CurrentState", Priv | BindingFlags.Public);
            private static readonly PropertyInfo PChart =
                typeof(GameManager).GetProperty("CurrentChart", Priv | BindingFlags.Public);
            private static readonly MethodInfo MJudgeUpdate = typeof(JudgeManager).GetMethod("Update", Priv);
            private static readonly MethodInfo MJudgeOnEnable = typeof(JudgeManager).GetMethod("OnEnable", Priv);
            private static readonly MethodInfo MJudgeBuildScore =
                typeof(JudgeManager).GetMethod("ResetScoreForNewSong", Priv);
            private static readonly MethodInfo MInputEnsureLanes =
                typeof(InputManager).GetMethod("EnsureLaneArrays", Priv);
            private static readonly PropertyInfo PSpawnerInstance =
                typeof(NoteSpawner).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly FieldInfo FSpawnerContainer =
                typeof(NoteSpawner).GetField("notesContainer", Priv);
            private static readonly FieldInfo FSpawnerPrefab =
                typeof(NoteSpawner).GetField("notePrefab", Priv);
            private static readonly FieldInfo FNoteImage = typeof(Note).GetField("noteImage", Priv);
            private static readonly MethodInfo MCreateDefaultPrefab =
                typeof(NoteSpawner).GetMethod("CreateDefaultNotePrefab", Priv);
            private static readonly MethodInfo MInitializePool =
                typeof(NoteSpawner).GetMethod("InitializePool", Priv);
            // 视图每帧工作在 LateUpdate（保证与判定同帧落到画面）
            private static readonly MethodInfo MSpawnerUpdate = typeof(NoteSpawner).GetMethod("LateUpdate", Priv);
            private static readonly MethodInfo MSpawnerOnGameStart = typeof(NoteSpawner).GetMethod("OnGameStart", Priv);
            private static readonly FieldInfo FSessionHitErrors =
                typeof(JudgeManager).GetField("sessionHitErrorsMs", Priv);
            private static readonly FieldInfo FNotePool = typeof(NoteSpawner).GetField("notePool", Priv);

            private readonly GameObject host;
            private readonly ChartData chart = new ChartData();
            private float sandboxSongTime;

            public readonly JudgeManager Judge;
            public readonly InputManager Input;
            public readonly GameManager Game;
            public readonly NoteSpawner Spawner;

            public Sandbox()
            {
                host = new GameObject("__JudgeTimingSandbox", typeof(RectTransform));
                host.SetActive(false);                       // 先关掉：等字段接好再唤醒，避免 Awake 抓到半成品
                Input = host.AddComponent<InputManager>();
                Game = host.AddComponent<GameManager>();
                Judge = host.AddComponent<JudgeManager>();

                // NoteSpawner 也是"纯视图"的一环：这里现装一个（容器 + 默认预制体 + 对象池）
                var notesGo = new GameObject("Notes", typeof(RectTransform));
                var notesRoot = (RectTransform)notesGo.transform;
                notesRoot.SetParent(host.transform, false);
                Spawner = host.AddComponent<NoteSpawner>();
                FSpawnerContainer.SetValue(Spawner, notesRoot);
                Note defaultNote = (Note)MCreateDefaultPrefab.Invoke(Spawner, null);
                FSpawnerPrefab.SetValue(Spawner, defaultNote);
                MInitializePool.Invoke(Spawner, null);

                host.SetActive(true);

                // 批处理（非播放模式）不会自己跑 Awake/OnEnable：这里显式补上
                if (GameManager.Instance == null) PGameInstance.SetValue(null, Game);
                if (InputManager.Instance == null) PInputInstance.SetValue(null, Input);
                if (NoteSpawner.Instance == null) PSpawnerInstance.SetValue(null, Spawner);
                if (JudgeManager.Instance == null) PJudgeInstance.SetValue(null, Judge);

                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                LiveConfig.SetJudgeTimeScale(1f);

                if (FLaneInputEvent.GetValue(Input) == null && MJudgeOnEnable != null)
                    MJudgeOnEnable.Invoke(Judge, null);

                // 输入层轨道数组按当前键数对齐（沙箱跑 5 键谱）
                MInputEnsureLanes.Invoke(Input, null);

                chart.metadata.formatVersion = 2;
                PChart.SetValue(Game, chart);
                SetState(GameState.Playing);
                SetSongTime(0f);
            }

            public void AddChartNote(NoteData data)
            {
                chart.notes.Add(data);
                chart.SortNotes();
            }

            /// <summary>建判定簇（JudgeManager 在 OnGameStart 走的就是这条路径）</summary>
            public void BuildScore()
            {
                MJudgeBuildScore.Invoke(Judge, null);
            }

            /// <summary>视图开演（NoteSpawner.OnGameStart：清空 + 允许生成）</summary>
            public void StartSong()
            {
                MSpawnerOnGameStart.Invoke(Spawner, null);
            }

            /// <summary>显式跑一次 NoteSpawner.Update（生成/定位/回收）</summary>
            public void SpawnerView()
            {
                MSpawnerUpdate.Invoke(Spawner, null);
            }

            /// <summary>判定音符（按谱面判定顺序：长按尾/身体条目没有独立判定，不在表里）</summary>
            public LiveNoteBase Live(int index)
            {
                var runtime = Judge.Runtime;
                if (runtime == null) return null;
                return index >= 0 && index < runtime.Notes.Count ? runtime.Notes[index] : null;
            }

            public void SetSongTime(float t) { PSongTime.SetValue(Game, t); sandboxSongTime = t; }
            public void SetState(GameState s) { PState.SetValue(Game, s); }

            // 注入输入时显式带上"事件时刻"：判定按输入自己的时间戳算
            public void PressAt(int lane, float musicTime) { Input.SimulateLaneInputAt(lane, true, musicTime); }
            public void ReleaseAt(int lane, float musicTime) { Input.SimulateLaneInputAt(lane, false, musicTime); }
            public void PressAtAs(int lane, float musicTime, int fingerId)
            {
                Input.SimulateLaneInputAt(lane, true, musicTime, fingerId);
            }

            /// <summary>
            /// 手指仍按住、这一帧**移动**了（Moved 事件）——真机同轨滑动走的就是这条路。
            /// 与 <see cref="SwipeAt"/> 的区别：那个只更新位置（靠判定层补的持续输入帧），
            /// 这个发真正的 isMove 事件，用来验"移动不发事件 / 跨轨被当成新按下"这两类 bug。
            /// </summary>
            public void MoveAt(int lane, float musicTime, float dx, float dy, int fingerId)
            {
                Input.SimulateFingerMoveAt(lane, musicTime, dx, dy, fingerId);
            }

            /// <summary>层级自检用：直接从对象池取一个音符（`Get` 必须把它排到容器最后 = 画在最上层）</summary>
            public Note TakeFromPool()
            {
                object pool = FNotePool.GetValue(Spawner);
                if (pool == null) return null;
                // 精确取无参重载：ObjectPool<T> 同时有 Get() 与 Get(out T)，按名字取会 AmbiguousMatch
                MethodInfo get = pool.GetType().GetMethod("Get", Type.EmptyTypes);
                return get != null ? (Note)get.Invoke(pool, null) : null;
            }

            public void ReturnToPool(Note note)
            {
                if (note == null) return;
                object pool = FNotePool.GetValue(Spawner);
                if (pool == null) return;
                MethodInfo release = pool.GetType().GetMethod("Release", new[] { typeof(Note) });
                release?.Invoke(pool, new object[] { note });
            }

            /// <summary>音符容器（NotesContainer）的子节点数</summary>
            public int ContainerChildCount
            {
                get
                {
                    var container = (RectTransform)FSpawnerContainer.GetValue(Spawner);
                    return container != null ? container.childCount : -1;
                }
            }

            /// <summary>长按尾标记（视图）的画布 y：按 longNoteId + type=LongEnd 在容器里找。找不到返回 NaN。</summary>
            public float LongEndViewY(int longNoteId)
            {
                var container = (RectTransform)FSpawnerContainer.GetValue(Spawner);
                if (container == null) return float.NaN;
                for (int i = 0; i < container.childCount; i++)
                {
                    Note note = container.GetChild(i).GetComponent<Note>();
                    if (note == null || note.Data == null) continue;
                    if (note.Data.type != DataNoteType.LongEnd || note.Data.longNoteId != longNoteId) continue;
                    if (!note.gameObject.activeSelf) continue;
                    return ((RectTransform)note.transform).anchoredPosition.y;
                }
                return float.NaN;
            }

            /// <summary>把手指移到「轨道中心 + (dx,dy)」（触屏语义；连续调用传累计位移）</summary>
            public void SwipeAt(int lane, float musicTime, float dx, float dy)
            {
                // 只更新位置、不发事件：判定层会为"仍按住"的轨道生成持续输入（Stationary），
                // 这样才会走进"手指已按住、这一帧移动"的分支（甩键累计幅度）。
                Input.SetFingerPosition(lane + 1, Input.FingerPositionOf(lane, dx, dy));
            }

            /// <summary>四个单例是否都指向本沙箱的实例（指向别人 = 后面测的不是这套对象）</summary>
            public bool InstancesMatch
            {
                get
                {
                    return GameManager.Instance == Game
                        && InputManager.Instance == Input
                        && JudgeManager.Instance == Judge
                        && NoteSpawner.Instance == Spawner;
                }
            }

            /// <summary>JudgeManager 是否订阅了输入事件（没订阅 = 按下事件到不了判定）</summary>
            public bool InputWired { get { return FLaneInputEvent.GetValue(Input) != null; } }

            /// <summary>本局已经收集的命中误差样本数（osu! 式统计校准的数据源）</summary>
            public int SessionHitErrorCount
            {
                get
                {
                    var list = FSessionHitErrors.GetValue(Judge) as System.Collections.ICollection;
                    return list != null ? list.Count : -1;
                }
            }

            /// <summary>显式跑一次 JudgeManager.Update（batchmode 同步自检里不会自己跑）</summary>
            public void JudgeUpdate() { MJudgeUpdate.Invoke(Judge, null); }

            public void Dispose()
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
#endif
