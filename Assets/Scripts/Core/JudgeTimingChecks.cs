#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using FallenAngel.InputSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定时序自检：**一次按下到底判掉几个音符**。
    ///
    /// 背景：一次按下会命中两条路径——① 按下事件（`HandlePress`）判轨道音符；
    /// ② 帧轮询（`Update`：drag 保持、曾经的宽键轮询）补刀。两条路径同帧各判一个，
    /// 表现为"两个音符挨得近时点一次判两个"。规则最终定为：
    ///   · **真同刻**（±20ms，`ChordPressTolerance`）：横条 + 普通音符 = **伪双押**，一次按下一起判；
    ///   · **只是挨得近**（不同刻）：一次按下只判一个，另一个要再按一次；
    ///   · 真双押（不同轨同刻）走两次按下事件，不受影响；
    ///   · 横条只认**按下边沿**——按住不放不再把进窗的横条"自动"判掉。
    ///
    /// 这几条都是 20ms 量级的时序规则，人力在引擎里点不准（用户口径：机器通过即可），
    /// 所以在这里锁死。首版修法曾一刀切"一次按下一个"，把正常伪双押也挡掉了——本自检负责防它回潮。
    ///
    /// 只读 + 临时对象（沙箱里现装 NoteSpawner/JudgeManager，不碰场景与资源）；
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
                lines.Add(string.Format("{0} {1,-24} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                // v2 五键谱：横条用 data.wide 标记（4K 谱里 lane 0 天生是 kick，语义不同）
                LaneLayout.SetActiveLaneCount(5);
                RunCases(check);
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-24} 自检异常：{1}", "执行", e));
            }
            finally
            {
                LaneLayout.SetActiveLaneCount(savedLanes);
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
            }

            string summary = string.Format(
                "判定时序自检：{0} 项通过 / {1} 项异常" +
                "（判定：伪双押同刻一起判 + 相邻音符分开判 + 容差边界 + 真双押 + 横条只认按下边沿 + " +
                "同帧不补刀 + 输入事件时间戳参与判定 + 输入时钟换算）" +
                "；注：真实触摸时间戳走 EnhancedTouch（装了 com.unity.inputsystem 才生效），未装时回退本帧歌曲时间",
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
            // ⓪ 沙箱接线：单例、输入事件、谱面条目都得真接上，否则后面的用例会"全 0 通过"
            using (var sb = new Sandbox())
            {
                Note probe = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));
                sb.SetSongTime(10.000f);
                sb.Press(2);
                bool judged = probe.IsJudged;

                check("沙箱接线",
                    judged && sb.InstancesMatch && sb.InputWired,
                    string.Format("单例={0}、输入事件={1}、按下后判定={2}（Perfect={3}）",
                        sb.InstancesMatch ? "接上" : "缺失", sb.InputWired ? "接上" : "缺失",
                        judged, sb.Judge.PerfectCount));
            }

            // ① 伪双押：同刻（0ms）的横条 + 普通音符，一次按下一起判
            using (var sb = new Sandbox())
            {
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));
                Note wide = sb.AddNote(new NoteData(0, 10.000f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true));

                sb.SetSongTime(10.000f);
                sb.Press(2);

                check("伪双押·同刻一起判",
                    tap.IsJudged && wide.IsJudged && sb.Judge.PerfectCount == 2,
                    string.Format("一次按下判掉 {0} 个（普通={1} 横条={2}），Perfect={3}",
                        sb.CountJudged(tap, wide), tap.IsJudged, wide.IsJudged, sb.Judge.PerfectCount));
            }

            // ② 容差边界内（+15ms < 20ms）：仍算同刻，一起判
            using (var sb = new Sandbox())
            {
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));
                Note wide = sb.AddNote(new NoteData(0, 10.015f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true));

                sb.SetSongTime(10.000f);
                sb.Press(2);

                check("同刻容差内 +15ms 一起判",
                    tap.IsJudged && wide.IsJudged,
                    string.Format("横条晚 15ms ≤ 容差 20ms → 一次按下判掉 {0} 个", sb.CountJudged(tap, wide)));
            }

            // ③ 容差边界外（+30ms > 20ms）：不是同刻，只判最近的那个；横条要再按一次
            using (var sb = new Sandbox())
            {
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));
                Note wide = sb.AddNote(new NoteData(0, 10.030f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true));

                sb.SetSongTime(10.000f);
                sb.Press(2);
                bool firstPressOne = tap.IsJudged && !wide.IsJudged;

                sb.Release(2);
                sb.SetSongTime(10.030f);
                sb.Press(0);
                bool secondPressWide = wide.IsJudged;

                check("容差外 +30ms 分开按",
                    firstPressOne && secondPressWide,
                    string.Format("第一次按下只判 {0} 个（横条未判={1}），横条时刻再按一次后判={2}",
                        firstPressOne ? 1 : 2, !wide.IsJudged, secondPressWide));
            }

            // ④ 真双押（不同轨、同一时刻）：两次按下事件，各自判一个
            using (var sb = new Sandbox())
            {
                Note left = sb.AddNote(new NoteData(1, 10.000f, NoteType.Normal));
                Note right = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));

                sb.SetSongTime(10.000f);
                sb.Press(1);
                int afterFirst = sb.CountJudged(left, right);
                sb.Press(2);
                int afterSecond = sb.CountJudged(left, right);

                check("真双押两次按下",
                    afterFirst == 1 && afterSecond == 2 && sb.Judge.PerfectCount == 2,
                    string.Format("按下 1 轨后判 {0} 个、再按 2 轨后判 {1} 个，Perfect={2}",
                        afterFirst, afterSecond, sb.Judge.PerfectCount));
            }

            // ⑤ 横条只认按下边沿：窗口外按住不判、走到线上仍按住也不判（旧实现用 anyHeld 轮询 → 提前判掉）
            using (var sb = new Sandbox())
            {
                Note wide = sb.AddNote(new NoteData(0, 10.000f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true));

                sb.SetSongTime(9.000f);      // 早 1s：远在判定窗（±200ms）外
                sb.Press(0);
                bool judgedTooEarly = wide.IsJudged;

                sb.SetSongTime(10.000f);     // 音符走到判定线上，手指仍按住
                sb.JudgeUpdate();
                bool judgedOnHold = wide.IsJudged;

                sb.Release(0);
                sb.Press(0);                 // 重新按下 → 这时才该判
                bool judgedOnPress = wide.IsJudged;

                check("横条只认按下边沿",
                    !judgedTooEarly && !judgedOnHold && judgedOnPress,
                    string.Format("窗口外按住判={0}、线上仍按住判={1}、重新按下判={2}",
                        judgedTooEarly, judgedOnHold, judgedOnPress));
            }

            // ⑥ 同一次按下不在同帧补刀：按住 drag 轨的同时点另一个音符，
            //    drag 轮询（Update）不得在这一次按下的帧里顺手把 drag 也判掉。
            //    注：护栏是**每根手指**的——"同一次按下"指的是同一根手指的那一次按下，
            //    另一根手指的按下不该封住我这根手指的手势（见 ⑧）。
            using (var sb = new Sandbox())
            {
                Note drag = sb.AddNote(new NoteData(2, 10.010f, NoteType.Drag));
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));

                sb.SetSongTime(10.000f);
                sb.Press(2);                 // 这一次按下判普通音符（最近的那个）
                int judgedByPress = sb.CountJudged(drag, tap);
                sb.JudgeUpdate();            // 同帧轮询：不得再补一刀
                int judgedAfterPoll = sb.CountJudged(drag, tap);

                check("同指同帧不补刀",
                    judgedByPress == 1 && judgedAfterPoll == 1 && tap.IsJudged && !drag.IsJudged,
                    string.Format("按下判 {0} 个、同帧轮询后仍 {1} 个（drag 未判={2}）",
                        judgedByPress, judgedAfterPoll, !drag.IsJudged));
            }

            // ⑧ 两指并行：一根手指**按住** drag 轨，另一根手指在同一帧点掉普通音符——
            //    两个都该判掉（PJSK 语义：护栏按手指记账，不是整帧封口）。
            using (var sb = new Sandbox())
            {
                Note drag = sb.AddNote(new NoteData(3, 10.000f, NoteType.Drag));
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));

                sb.SetSongTime(9.000f);
                sb.PressAs(3, 30);           // 手指 A 提前按住 drag 轨（此时不在窗口，只登记按住）
                sb.SetSongTime(10.000f);
                sb.PressAs(2, 20);           // 手指 B 同帧点掉普通音符
                sb.JudgeUpdate();            // 轮询：手指 A 按住的那条轨也该把 drag 判掉

                check("两指并行各判一个",
                    drag.IsJudged && tap.IsJudged && sb.Judge.PerfectCount == 2,
                    string.Format("按住轨的 drag={0}、点掉的 tap={1}，Perfect={2}",
                        drag.IsJudged, tap.IsJudged, sb.Judge.PerfectCount));
            }

            // ⑦ 事件时间戳参与判定：这一帧引擎读到触摸时歌曲时间已经走到 10.150，
            //    但触摸**真实发生**在 10.020。按帧时间判算成 +150ms（Great），
            //    按事件时间戳判才是 +20ms（Perfect）——这正是低帧/高刷下判定抖动的来源。
            using (var sb = new Sandbox())
            {
                Note tap = sb.AddNote(new NoteData(2, 10.000f, NoteType.Normal));

                sb.SetSongTime(10.150f);
                sb.PressAt(2, 10.020f);

                check("输入事件时间戳参与判定",
                    tap.IsJudged && sb.Judge.PerfectCount == 1 && sb.Judge.GreatCount == 0,
                    string.Format("帧时间 10.150、触摸时间 10.020 → 判 {0}（Perfect={1} Great={2}）",
                        tap.IsJudged ? tap.JudgeResult.ToString() : "未判",
                        sb.Judge.PerfectCount, sb.Judge.GreatCount));
            }

            // ⑨ 输入时钟换算：事件时刻 → 歌曲时间（"按事件时间戳判定"的地基）。
            //    采样点 SongTime=10.000 @ 输入时钟 100.000。
            InputClock.Reset();
            InputClock.Sample(10.000f, 100.000);
            float normal = InputClock.ToMusicTime(100.020);    // 事件发生在采样后 20ms
            float late = InputClock.ToMusicTime(100.180);      // 晚 180ms（跨档位的那一段）
            float clamped = InputClock.ToMusicTime(200.000);   // 时钟不同源 → 钳到 +0.25
            check("输入时钟换算",
                Mathf.Abs(normal - 10.020f) <= 0.0005f &&
                Mathf.Abs(late - 10.180f) <= 0.0005f &&
                Mathf.Abs(clamped - (10.000f + InputClock.MaxDelta)) <= 0.0005f,
                string.Format("+20ms→{0:0.###}、+180ms→{1:0.###}、异常时钟→{2:0.###}（钳到 +{3:0.##}s）",
                    normal, late, clamped, InputClock.MaxDelta));
        }

        /// <summary>
        /// 判定沙箱：现装一整套 NoteSpawner + JudgeManager + InputManager + GameManager，
        /// 歌曲时间与运行状态由自检显式控制（batchmode 下同步执行，不会真跑 Update）。
        /// </summary>
        private sealed class Sandbox : IDisposable
        {
            private static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
            private static readonly Vector2 SpawnPos = new Vector2(0f, 1200f);
            private static readonly Vector2 JudgePos = new Vector2(0f, PlayVisualSpec.JudgeLineY);

            private static readonly FieldInfo FActiveNotes = typeof(NoteSpawner).GetField("activeNotes", Priv);
            private static readonly FieldInfo FNotesContainer = typeof(NoteSpawner).GetField("notesContainer", Priv);
            private static readonly FieldInfo FNoteImage = typeof(Note).GetField("noteImage", Priv);
            private static readonly FieldInfo FLaneInputEvent = typeof(InputManager).GetField("OnLaneInput", Priv);
            private static readonly PropertyInfo PGameInstance =
                typeof(GameManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PInputInstance =
                typeof(InputManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PSpawnerInstance =
                typeof(NoteSpawner).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PJudgeInstance =
                typeof(JudgeManager).GetProperty("Instance", Priv | BindingFlags.Public | BindingFlags.Static);
            private static readonly PropertyInfo PSongTime = typeof(GameManager).GetProperty("SongTime", Priv | BindingFlags.Public);
            private static readonly PropertyInfo PState = typeof(GameManager).GetProperty("CurrentState", Priv | BindingFlags.Public);
            private static readonly PropertyInfo PChart = typeof(GameManager).GetProperty("CurrentChart", Priv | BindingFlags.Public);
            private static readonly MethodInfo MJudgeUpdate = typeof(JudgeManager).GetMethod("Update", Priv);
            private static readonly MethodInfo MJudgeOnEnable = typeof(JudgeManager).GetMethod("OnEnable", Priv);
            private static readonly MethodInfo MJudgeReset = typeof(JudgeManager).GetMethod("ResetStats", Priv);

            private readonly GameObject host;
            private readonly List<GameObject> notes = new List<GameObject>();

            public readonly NoteSpawner Spawner;
            public readonly JudgeManager Judge;
            public readonly InputManager Input;
            public readonly GameManager Game;

            public Sandbox()
            {
                host = new GameObject("__JudgeTimingSandbox", typeof(RectTransform));
                host.SetActive(false);                       // 先关掉：等字段接好再唤醒，避免 Awake 抓到半成品

                var notesGo = new GameObject("Notes", typeof(RectTransform));
                var notesRoot = (RectTransform)notesGo.transform;
                notesRoot.SetParent(host.transform, false);

                Input = host.AddComponent<InputManager>();
                Spawner = host.AddComponent<NoteSpawner>();
                FNotesContainer.SetValue(Spawner, notesRoot);
                Game = host.AddComponent<GameManager>();
                Judge = host.AddComponent<JudgeManager>();

                host.SetActive(true);

                // 批处理（非播放模式）不会自己跑 Awake/OnEnable：这里显式补上，
                // 否则单例全空、输入事件也没人订阅，"按下"根本到不了判定逻辑。
                // 若某个脚本将来改成 ExecuteInEditMode（唤醒会自己跑），下面的判断保证不会重复订阅。
                if (GameManager.Instance == null) PGameInstance.SetValue(null, Game);
                if (InputManager.Instance == null) PInputInstance.SetValue(null, Input);
                if (NoteSpawner.Instance == null) PSpawnerInstance.SetValue(null, Spawner);
                if (JudgeManager.Instance == null) PJudgeInstance.SetValue(null, Judge);

                PlayRules.SetBaseWindows(JudgeWindows.Default);

                if (FLaneInputEvent.GetValue(Input) == null && MJudgeOnEnable != null)
                    MJudgeOnEnable.Invoke(Judge, null);

                MJudgeReset.Invoke(Judge, null);     // 分配轨道状态数组 + 计数清零

                // 必须挂一张 v2（五键）谱：JudgeManager.Update 开头 `CurrentChart == null → return`，
                // 没谱面的话帧轮询路径整段被跳过，用例会"假通过"（测不到任何东西）。
                var chart = new ChartData();
                chart.metadata.formatVersion = 2;
                PChart.SetValue(Game, chart);

                SetState(GameState.Playing);
                SetSongTime(0f);
            }

            private float sandboxSongTime;

            public void SetSongTime(float t) { PSongTime.SetValue(Game, t); sandboxSongTime = t; }
            public void SetState(GameState s) { PState.SetValue(Game, s); }

            // 注入输入时显式带上"事件时刻 = 当前沙箱歌曲时间"：
            // 判定从此按输入自己的时间戳算，不再依赖真实时钟（batchmode 里没有 Update 在推进它）。
            public void Press(int lane) { Input.SimulateLaneInputAt(lane, true, sandboxSongTime); }
            public void Release(int lane) { Input.SimulateLaneInputAt(lane, false, sandboxSongTime); }

            /// <summary>按指定音乐时间注入一次按下（模拟"引擎晚读到的触摸事件"）</summary>
            public void PressAt(int lane, float musicTime) { Input.SimulateLaneInputAt(lane, true, musicTime); }

            /// <summary>指定手指 id 注入一次按下（模拟"多指同时输入"）</summary>
            public void PressAs(int lane, int fingerId) { Input.SimulateLaneInputAt(lane, true, sandboxSongTime, fingerId); }

            /// <summary>四个单例是否都指向本沙箱的实例（指向别人 = 后面测的不是这套对象）</summary>
            public bool InstancesMatch
            {
                get
                {
                    return GameManager.Instance == Game
                        && InputManager.Instance == Input
                        && NoteSpawner.Instance == Spawner
                        && JudgeManager.Instance == Judge;
                }
            }

            /// <summary>JudgeManager 是否订阅了输入事件（没订阅 = 按下事件到不了判定）</summary>
            public bool InputWired { get { return FLaneInputEvent.GetValue(Input) != null; } }

            /// <summary>谱面条目数（判定查询扫的就是这个列表）</summary>
            public int ActiveNoteCount { get { return ((List<Note>)FActiveNotes.GetValue(Spawner)).Count; } }

            /// <summary>显式跑一次 JudgeManager.Update（帧轮询路径）；batchmode 同步自检里不会自己跑</summary>
            public void JudgeUpdate() { MJudgeUpdate.Invoke(Judge, null); }

            public Note AddNote(NoteData data)
            {
                var go = new GameObject("JTC_Note", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(Spawner.transform, false);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(Note.DefaultNoteWidth, Note.DefaultNoteHeight);

                Note note = go.AddComponent<Note>();
                FNoteImage.SetValue(note, go.GetComponent<Image>());
                note.Initialize(data, SpawnPos, JudgePos);

                ((List<Note>)FActiveNotes.GetValue(Spawner)).Add(note);
                notes.Add(go);
                return note;
            }

            public int CountJudged(params Note[] targets)
            {
                int n = 0;
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null && targets[i].IsJudged) n++;
                return n;
            }

            public void Dispose()
            {
                for (int i = 0; i < notes.Count; i++)
                    if (notes[i] != null) UnityEngine.Object.DestroyImmediate(notes[i]);
                notes.Clear();

                if (Spawner != null) ((List<Note>)FActiveNotes.GetValue(Spawner)).Clear();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
#endif
