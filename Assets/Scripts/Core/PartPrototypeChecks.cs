#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using FallenAngel.Live;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
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
                Check(gm.Parts.HitCount == 2 && gm.Parts.Energy == 2, "completed long note rewarded once at tail");
                gm.BackToMenu();
                gm.LoadChart(guitar);
                Check(gm.Parts == null && gm.ActiveChart == guitar, "ordinary single charts unchanged");
                Check(PartPrototype.Charts.Guitar.notes.Count == 96 && PartPrototype.Charts.Drums.notes.Count == 96 &&
                    PartPrototype.Audio.length == 36f, "audible 36-second two-part demo available");
                Debug.Log($"[PartPrototypeChecks] ALL PASS ({passed})");
            }
            finally
            {
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
