#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using FallenAngel.Live;
using FallenAngel.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;
using NoteType = FallenAngel.Data.NoteType;

namespace FallenAngel.Core
{
    /// <summary>声部切换回归：真实判定簇、对象池、资源倍率和重试生命周期。</summary>
    public static class PartPrototypeChecks
    {
        private static int passed;
        private static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception("[PartPrototypeChecks] FAIL " + message);
            passed++;
            Debug.Log("[PartPrototypeChecks] PASS " + message);
        }

        [MenuItem("Tools/FallenAngel/Validation/Gameplay/Validate Guitar Drums Prototype", false, 500)]
        public static void Run()
        {
            if (Application.isPlaying || GameManager.Instance != null)
                throw new InvalidOperationException("请在空场景非播放模式执行自检");
            passed = 0;
            var host = new GameObject("__PartChecks");
            var views = new GameObject("__PartViews", typeof(RectTransform));
            ModifierManager modifiers = null;
            bool logWasEnabled = PartRunLog.Enabled;
            PartRunLog.Enabled = false;   // 自检不写真实试玩数据
            try
            {
                var gm = host.AddComponent<GameManager>();
                Set(typeof(GameManager), "Instance", null, gm);
                var judge = host.AddComponent<JudgeManager>();
                Invoke(judge, "Awake");
                Invoke(judge, "Subscribe");
                var spawner = views.AddComponent<NoteSpawner>();
                Invoke(spawner, "Awake");
                Invoke(spawner, "Start");
                modifiers = ModifierManager.EnsureInstance();
                modifiers.Subscribe();
                modifiers.ClearAll();

                var guitar = new ChartData();
                guitar.notes.Add(new NoteData(0, 2f, NoteType.Normal));
                guitar.notes.Add(new NoteData(1, 4f, NoteType.LongStart, 2f, 7));
                guitar.notes.Add(new NoteData(1, 6f, NoteType.LongEnd, 0f, 7));
                guitar.notes.Add(new NoteData(2, 8f, NoteType.Normal));
                var drums = new ChartData();
                drums.notes.Add(new NoteData(3, 1f, NoteType.Normal));
                drums.notes.Add(new NoteData(0, 3f, NoteType.Normal));
                drums.notes.Add(new NoteData(1, 7f, NoteType.Normal));
                drums.notes.Add(new NoteData(2, 10f, NoteType.Normal));
                var set = new SongParts(guitar, drums);
                gm.LoadChart(guitar, set);
                gm.StartGame();
                Check(gm.Parts.Energy == 0 && gm.SongDuration == 12f, "start empty, duration includes both parts");
                SetTime(gm, 1.95f);
                Invoke(spawner, "LateUpdate");
                Check(spawner.ActiveNotes.Count > 0, "initial part uses existing note pool");
                Auto(judge, 2f);
                int score = judge.Score;
                Check(gm.Parts.Energy == 1 && gm.Parts.HitCount == 1 && score > 0, "real tap judgment awards one energy");
                gm.SwitchPart();
                Check(spawner.ActiveNotes.Count == 0, "switch immediately recycles outgoing views");
                Check(gm.SongTime == 1.95f && judge.Score == score && judge.MissCount == 0,
                    "switch preserves clock/score and skips inactive past notes");
                Check(judge.Runtime.Notes.Count == 3, "new judge runtime contains only future target notes");
                gm.SwitchPart();
                Check(gm.ActiveChart.notes.TrueForAll(n => n.time != 2f), "early hit cannot be farmed by switching back");
                SetTime(gm, 4f);
                Auto(judge, 4f);
                Check(gm.Parts.Energy == 1, "hold head does not award resource");
                gm.SwitchPart();
                gm.SwitchPart();
                Check(gm.ActiveChart.notes.Count == 1 && gm.ActiveChart.notes[0].time == 8f,
                    "interrupted hold cancels silently, no orphan tail");
                Check(judge.MissCount == 0, "switch does not create ghost misses");
                Invoke(spawner, "LateUpdate");
                Check(spawner.ActiveNotes.Count == 0, "old part views returned to pool");
                modifiers.Apply(new ModifierDef("test_energy", ModifierKind.EnergyMultiplier, 2f, 2f));
                Check(PlayRules.EnergyMultiplier == 2f && PlayRules.ScoreMultiplier == 1f,
                    "energy modifier independent of score");
                gm.PauseGame();
                Check(!gm.SwitchPart() && gm.SongTime == 4f, "paused switch rejected, time frozen");
                gm.ResumeGame();
                modifiers.Tick(6.01f);
                Check(PlayRules.EnergyMultiplier == 1f, "timed modifier expires on song clock");
                modifiers.Apply(new ModifierDef("test_energy", ModifierKind.EnergyMultiplier, 2f, 0f));
                modifiers.Apply(new ModifierDef("test_energy_extra", ModifierKind.EnergyMultiplier, 1.5f, 0f));
                SetTime(gm, 8f);
                Auto(judge, 8f);
                Check(gm.Parts.Energy == 4 && gm.Parts.HitCount == 2, "multipliers stack, one award per tap");
                Auto(judge, 8f);
                Check(gm.Parts.Energy == 4, "repeat judgment cannot duplicate energy");
                gm.EndGame();
                Check(gm.Parts.Energy == 4 && PlayRules.EnergyMultiplier == 1f, "result retains energy and clears song modifiers");
                gm.RetryGame();
                Check(gm.Parts.Energy == 0 && !gm.Parts.IsDrums && gm.SongTime == 0f, "retry resets both part state and energy");
                gm.StartGame();
                SetTime(gm, 2f);
                Auto(judge, 2f);
                SetTime(gm, 4f);
                Auto(judge, 4f);
                SetTime(gm, 6f);
                Auto(judge, 6f);
                Check(gm.Parts.HitCount == 2 && gm.Parts.Energy == 3,
                    "completed long note rewarded once at tail (1 tap + long note at 2x weight)");
                gm.BackToMenu();
                gm.LoadChart(guitar);
                Check(gm.Parts == null && gm.ActiveChart == guitar, "ordinary single charts unchanged");

                // ── 原型增量（2026-10-03）：单位产率 / 声部道具 / 路线记录 ──
                Check(Mathf.Abs(PartEnergyRules.NoteWeight(NoteType.Normal) - 1f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.NoteWeight(NoteType.LongStart) - 2f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.NoteWeight(NoteType.Slide) - 2f) < 1e-4f,
                    "unit yield: tap 1x, long/slide 2x");
                Check(Mathf.Abs(PartEnergyRules.StayMultiplier(PartItem.StayRamp, 8f) - 1f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.StayMultiplier(PartItem.StayRamp, 12f) - 1.25f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.StayMultiplier(PartItem.StayRamp, 20f) - 1.5f) < 1e-4f,
                    "stay ramp: starts at 8s, caps at 1.5x by 16s");
                Check(Mathf.Abs(PartEnergyRules.SwitchMultiplier(PartItem.SwitchBonus, 0) - 1.5f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.SwitchMultiplier(PartItem.SwitchBonus, 10) - 1f) < 1e-4f,
                    "switch bonus covers only the first 10 notes");
                Check(Mathf.Abs(PartEnergyRules.PartMultiplier(PartItem.DrumContract, "Drums") - 1.3f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.PartMultiplier(PartItem.DrumContract, "Guitar") - 1f) < 1e-4f,
                    "drum contract applies to Drums only");

                gm.LoadChart(guitar, set);
                gm.StartGame();
                gm.Parts.Item = PartItem.DrumContract;
                SetTime(gm, 1.95f);
                gm.SwitchPart();
                SetTime(gm, 3f);
                Auto(judge, 3f);
                Check(Mathf.Abs(gm.Parts.Energy - 1.3f) < 1e-3f, "drum contract pays 1.3x on a drum note");
                SetTime(gm, 3.2f);   // 让刚打中的那颗落在段窗口内，便于核对"供给"
                gm.EndGame();
                Check(gm.Parts.Route.Segments.Count == 2 && gm.Parts.Route.SwitchCount == 1 &&
                    gm.Parts.Route.TotalEnergy > 0f && gm.Parts.Route.RouteLine().Contains("Drums"),
                    "route recorder: segments, switches and energy are readable");
                Check(gm.Parts.Route.Segments[0].part == "Guitar" && gm.Parts.Route.Segments[0].offered == 0 &&
                    gm.Parts.Route.Segments[1].part == "Drums" && gm.Parts.Route.Segments[1].offered == 1 &&
                    gm.Parts.Route.Segments[1].hits == 1 && gm.Parts.Route.Segments[1].HitRate == 1f,
                    "route recorder counts what each part actually offered");

                gm.BackToMenu();
                gm.LoadChart(guitar, set);
                gm.StartGame();
                gm.Parts.Item = PartItem.SwitchBonus;
                SetTime(gm, 1.95f);
                gm.SwitchPart();
                SetTime(gm, 3f);
                Auto(judge, 3f);
                SetTime(gm, 7f);
                Auto(judge, 7f);
                Check(Mathf.Abs(gm.Parts.Energy - 3f) < 1e-3f, "switch bonus pays 1.5x for the first notes");
                gm.BackToMenu();

                Check(PartPrototype.Charts.Guitar.notes.Count == 96 && PartPrototype.Charts.Drums.notes.Count == 120 &&
                    PartPrototype.Audio.length == 36f, "audible 36-second two-part demo available");
                var demoDrums = PartPrototype.Charts.Drums.notes;
                Check(PartPrototype.Charts.Guitar.notes.Exists(n => n.type == NoteType.LongStart) &&
                    demoDrums.FindAll(n => n.time >= 26f && n.time < 34f).Count == 56,
                    "demo has a long-note steady section and a 56-note drum solo");
                int trillBar = demoDrums.FindAll(n => n.time >= 26f && n.time < 28f).Count;
                int jackBar = demoDrums.FindAll(n => n.time >= 28f && n.time < 29f && n.lane == 1).Count;
                var burst = demoDrums.FindAll(n => n.time >= 32f && n.time < 32.6f);
                float shortestGap = float.MaxValue;
                for (int i = 1; i < burst.Count; i++)
                    shortestGap = Mathf.Min(shortestGap, burst[i].time - burst[i - 1].time);
                Check(trillBar == 12 && jackBar == 8 && burst.Count == 8 && shortestGap <= 0.063f,
                    "drum solo is a real wall: 16th trill, same-lane jack and a 32nd burst");
                Check(PartPrototype.Charts.Guitar.notes.FindAll(n => n.time >= 26f && n.type == NoteType.LongStart).Count == 8,
                    "guitar keeps a low-risk long-note retreat during the drum wall");

                // ── 声部视觉（2026-10-03 过稿：全色相 + G2 半场；占位，未来换商用素材）──
                Check(PartPalette.KnownParts.Length == 5 &&
                    PartPalette.HueGap(PartPalette.HueDrums, PartPalette.HueGuitar) >= 45f &&
                    PartPalette.HueGap(PartPalette.HueGuitar, PartPalette.HueKeys) >= 60f,
                    "part hues: five distinct instruments, drums/guitar at least 45 degrees apart");
                bool distinct = true;
                for (int i = 0; i < PartPalette.KnownParts.Length && distinct; i++)
                {
                    float hueA = PartPalette.HueOf(PartPalette.KnownParts[i]);
                    distinct = hueA >= 0f;
                    for (int j = i + 1; j < PartPalette.KnownParts.Length && distinct; j++)
                    {
                        float hueB = PartPalette.HueOf(PartPalette.KnownParts[j]);
                        distinct = PartPalette.HueGap(hueA, hueB) >= 45f;
                    }
                }
                Check(distinct, "part hues: every pair is at least 45 degrees apart");
                Color drumColor, guitarColor;
                Check(PartPalette.TryGetColor("Drums", out drumColor) &&
                    PartPalette.TryGetColor("Guitar", out guitarColor) &&
                    !PartPalette.TryGetColor("Unknown", out _) &&
                    PartPalette.TintPeak > 0f && PartPalette.TintPeak < 0.5f &&
                    PartPalette.TintSpan > 0f && PartPalette.TintSpan < 1f,
                    "G2 half-field tint constants present, unknown parts stay untinted");

                // ── 判定等级 × 单位产率：Good 不再等于 Perfect（设计稿第三节的最小模型）──
                Check(Mathf.Abs(PartEnergyRules.GradeWeight(JudgeResultType.Perfect) - 1f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.GradeWeight(JudgeResultType.Great) - 0.65f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.GradeWeight(JudgeResultType.Good) - 0.35f) < 1e-4f &&
                    PartEnergyRules.GradeWeight(JudgeResultType.Miss) == 0f,
                    "grade yield: perfect 1x / great 0.65x / good 0.35x / miss 0");
                Check(Mathf.Abs(PartEnergyRules.Yield(NoteType.Normal, JudgeResultType.Perfect, 1f, 1f) - 1f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.Yield(NoteType.LongStart, JudgeResultType.Perfect, 1f, 1f) - 2f) < 1e-4f &&
                    Mathf.Abs(PartEnergyRules.Yield(NoteType.Normal, JudgeResultType.Good, 2f, 1.3f) - 0.91f) < 1e-4f,
                    "yield = note weight x grade x modifier x item");

                // ── 试玩数据落盘：一行 JSON，能原样读回来 ──
                string checkPath = Path.Combine(Application.temporaryCachePath, "part_runs_check.jsonl");
                if (File.Exists(checkPath)) File.Delete(checkPath);
                gm.LoadChart(guitar, set);
                gm.StartGame();
                SetTime(gm, 1.95f);
                gm.SwitchPart();
                SetTime(gm, 3f);
                Auto(judge, 3f);
                SetTime(gm, 3.2f);   // 把 SongTime 推过那颗粒，段窗口才把它算进"供给"
                gm.EndGame();
                PartRunRecord record = PartRunLog.Build(gm, gm.Parts, new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
                Check(record != null && record.segments.Count == 2 && record.switches == 1 &&
                    record.segments[1].perfect == 1 && record.segments[1].great == 0 &&
                    record.segments[1].hits == 1,
                    "run record keeps switches and per-segment grade mix");
                Check(PartRunLog.Append(record, checkPath) && File.Exists(checkPath) &&
                    PartRunLog.Append(record, checkPath) &&
                    File.ReadAllLines(checkPath).Length == 2,
                    "run log appends one JSON line per play");
                string firstLine = File.ReadAllLines(checkPath)[0];
                Debug.Log("[PartPrototypeChecks] 落盘行：" + firstLine);
                PartRunRecord parsed = JsonUtility.FromJson<PartRunRecord>(firstLine);
                Check(parsed != null, "run log line parses at all");
                Check(parsed != null && parsed.segments != null && parsed.segments.Count == 2,
                    "run log line keeps both segments");
                Check(parsed != null && parsed.switches == 1 && parsed.segments.Count == 2 &&
                    parsed.segments[0].part == "Guitar" && parsed.segments[1].perfect == 1,
                    "run log line parses back into the same record");
                File.Delete(checkPath);
                gm.BackToMenu();

                CheckPartVisualLayers();
                Debug.Log($"[PartPrototypeChecks] ALL PASS ({passed})");
            }
            finally
            {
                PartRunLog.Enabled = logWasEnabled;
                if (modifiers != null) UnityEngine.Object.DestroyImmediate(modifiers.gameObject);
                // 自检创建的兜底 prefab 不属于场景资源。
                var prefab = views.GetComponent<NoteSpawner>()?.GetType().GetField("notePrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(views.GetComponent<NoteSpawner>()) as Note;
                UnityEngine.Object.DestroyImmediate(views);
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab.gameObject);
                UnityEngine.Object.DestroyImmediate(host);
                Time.timeScale = 1f;
            }
        }

        private static void Auto(JudgeManager judge, float time)
        {
            judge.Runtime.Update(LiveNoteBuilder.Info(time));
            judge.Runtime.AutoPlay(LiveNoteBuilder.Info(time));
        }

        /// <summary>
        /// 声部视觉层的**结构**自检：染色层必须在轨道/音符之下、演出层必须在音符之上、HUD 之下，
        /// 且都不吃触摸。层序错了在实机上只表现为"看起来不对"，很难在数值上发现，所以在这里卡住。
        /// </summary>
        private static void CheckPartVisualLayers()
        {
            var canvas = new GameObject("__PartVisualRoot", typeof(RectTransform));
            try
            {
                var gamePanel = new GameObject("GamePanel", typeof(RectTransform));
                gamePanel.transform.SetParent(canvas.transform, false);
                var background = new GameObject("GameplayBackground", typeof(RectTransform));
                background.transform.SetParent(gamePanel.transform, false);
                var lanes = new GameObject("LanesBG", typeof(RectTransform));
                lanes.transform.SetParent(gamePanel.transform, false);
                var judgeLine = new GameObject("JudgeLine", typeof(RectTransform), typeof(Image));
                judgeLine.transform.SetParent(lanes.transform, false);
                var notes = new GameObject("NotesContainer", typeof(RectTransform));
                notes.transform.SetParent(lanes.transform, false);
                var hud = new GameObject("HUDPanel", typeof(RectTransform));
                hud.transform.SetParent(gamePanel.transform, false);

                var view = gamePanel.AddComponent<PartTintView>();
                Invoke(view, "Start");

                Transform tint = lanes.transform.Find("PartTint");
                Transform fx = gamePanel.transform.Find("PartSwitchFX");
                Check(tint != null && tint.GetSiblingIndex() == 0 &&
                    tint.GetSiblingIndex() < notes.transform.GetSiblingIndex(),
                    "part tint sits under lanes/notes but above the panel backdrop");
                Check(fx != null && fx.GetSiblingIndex() < hud.transform.GetSiblingIndex(),
                    "switch FX sits above notes and below the HUD");
                Check(fx != null && fx.Find("RailLeft") != null && fx.Find("RailRight") != null &&
                    fx.Find("JudgeFlash") != null && fx.Find("PartBadge") != null,
                    "switch FX builds rails, judge-line flash and badge");
                Check(tint != null && tint.GetComponent<Image>() != null && !tint.GetComponent<Image>().raycastTarget &&
                    fx.Find("RailLeft").GetComponent<Image>() != null &&
                    !fx.Find("RailLeft").GetComponent<Image>().raycastTarget,
                    "part visuals never block touch input");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void SetTime(GameManager gm, float time) => Set(typeof(GameManager), "SongTime", gm, time);
        private static void Set(Type type, string name, object instance, object value) =>
            type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).SetValue(instance, value);
        private static void Invoke(object target, string method) => target.GetType().GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);

        /// <summary>复用现有场景构建 Windows 原型，不改场景或 PlayerSettings。</summary>
        public static void BuildWindows()
        {
            Run();
            string output = "Builds/PartPrototype/FallenAngel.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/FallenAngel.unity" }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Prototype build failed");
            Debug.Log("[PartPrototypeChecks] BUILD PASS " + output);
        }
    }
}
#endif
