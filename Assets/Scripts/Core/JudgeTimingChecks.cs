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
                + "长按端到端 / 自动 Miss 收口 / modifier 倍率 / 空按不判）",
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
                    PlayRules.SetModifierState(scale, true, true);
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

            private readonly GameObject host;
            private readonly ChartData chart = new ChartData();
            private float sandboxSongTime;

            public readonly JudgeManager Judge;
            public readonly InputManager Input;
            public readonly GameManager Game;

            public Sandbox()
            {
                host = new GameObject("__JudgeTimingSandbox", typeof(RectTransform));
                host.SetActive(false);                       // 先关掉：等字段接好再唤醒，避免 Awake 抓到半成品
                Input = host.AddComponent<InputManager>();
                Game = host.AddComponent<GameManager>();
                Judge = host.AddComponent<JudgeManager>();
                host.SetActive(true);

                // 批处理（非播放模式）不会自己跑 Awake/OnEnable：这里显式补上
                if (GameManager.Instance == null) PGameInstance.SetValue(null, Game);
                if (InputManager.Instance == null) PInputInstance.SetValue(null, Input);
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

            /// <summary>四个单例是否都指向本沙箱的实例（指向别人 = 后面测的不是这套对象）</summary>
            public bool InstancesMatch
            {
                get
                {
                    return GameManager.Instance == Game
                        && InputManager.Instance == Input
                        && JudgeManager.Instance == Judge;
                }
            }

            /// <summary>JudgeManager 是否订阅了输入事件（没订阅 = 按下事件到不了判定）</summary>
            public bool InputWired { get { return FLaneInputEvent.GetValue(Input) != null; } }

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
