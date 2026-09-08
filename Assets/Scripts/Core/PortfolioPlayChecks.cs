#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using FallenAngel.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Core
{
    /// <summary>编辑器集成检查：自动点击真实UI，驱动歌曲结束边界。不是人工节奏游玩测试。</summary>
    [InitializeOnLoad]
    public static class PortfolioPlayChecks
    {
        private const string ActiveKey = "FA.Portfolio.PlayChecks";
        private static int step;
        private static double deadline;
        private static PortfolioSession session;
        private static int assertions;
        static PortfolioPlayChecks()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.update += Tick;
            }
        }

        /// <summary>仅由独立batchmode进程运行，重建未保存的测试场景。</summary>
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this check in a dedicated batch process.");
            PortfolioChecks.Run();
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-portfolioCaptureDir");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing capture directory");
            Directory.CreateDirectory(args[index + 1]);
            SessionState.SetString("FA.Portfolio.Captures", args[index + 1]);
            SessionState.SetString("FA.Portfolio.TestStore", Path.Combine(Path.GetTempPath(), "FA_Play_" + Guid.NewGuid().ToString("N")));
            SceneBuilder.BuildDefaultScene(false);
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Playmode check timeout at step " + step);
                var gm = GameManager.Instance;
                if (gm == null || gm.Portfolio == null) return;
                session = gm.Portfolio;
                switch (step)
                {
                    case 0:
                        if (!HasButton("OpenGrowth")) return;
                        session.ConfigureForValidation(SessionState.GetString("FA.Portfolio.TestStore", ""));
                        Click("OpenGrowth"); step++; break;
                    case 1:
                        if (!HasButton("CreateProfile")) return;
                        UnityEngine.Object.FindObjectOfType<TMP_InputField>().text = "成长流程验证";
                        Click("CreateProfile"); step++; break;
                    case 2:
                        if (!HasButton("BeginGrowthRun")) return;
                        Require(session.Profile.growthPoints == 0, "New UI profile starts at zero");
                        Click("OpenTalents"); step = 40; break;
                    case 40:
                        if (!HasButton("BackToMap")) return;
                        Require(HasButton("Talent_A0") && !HasButton("Room_N00"), "Talents have a separate page");
                        Capture("01_talent_tree"); Click("BackToMap"); step = 41; break;
                    case 41:
                        if (!HasButton("BeginGrowthRun")) return;
                        Capture("00_journey_map"); Click("BeginGrowthRun"); step = 3; break;
                    case 3:
                        if (!PrepareBattle()) return;
                        if (!HasButton("ContinueGrowth")) return;
                        Require(!HasButton("BeginGrowthRun"), "Active profile has no new-run button");
                        Click("ContinueGrowth"); step++; break;
                    case 4:
                        if (gm.CurrentState != GameState.Playing) return;
                        Require(session.Run.phase == "PLAYING", "Persistent playing marker exists before performance");
                        gm.PauseGame(); step++; break;
                    case 5:
                        if (!HasButton("ResumePerformance")) return;
                        Capture("02_pause");
                        Require(HasButton("AbandonPerformance"), "Pause exposes resume and abandon");
                        Click("AbandonPerformance"); step = 50; break;
                    case 50:
                        if (!HasButton("Confirm")) return;
                        gm.TogglePause();
                        Require(gm.CurrentState == GameState.Paused, "Pause shortcut cannot bypass confirmation dialog");
                        Click("Cancel"); step = 51; break;
                    case 51:
                        if (HasButton("Confirm")) return;
                        Click("ResumePerformance");
                        // 自动驱动结算边界，绝不将这一步记录为打完了整首歌。
                        gm.EndGame(); step = 6; break;
                    case 6:
                        if (!HasButton("ContinueGrowth")) return;
                        Require(session.Run.earnedPoints == 100 && session.Profile.growthPoints == 0, "Real UI completion leaves points pending");
                        gm.OnGameEnd?.Invoke();
                        Require(session.Run.earnedPoints == 100, "Repeated end event does not duplicate pending reward");
                        Capture("03_song_result");
                        Click("ContinueGrowth"); step++; break;
                    case 7:
                        if (!PrepareBattle()) return;
                        if (session.Run.phase != "READY" || !HasButton("ContinueGrowth")) return;
                        Click("ContinueGrowth"); step++; break;
                    case 8:
                        if (gm.CurrentState != GameState.Playing) return;
                        gm.EndGame(); step++; break;
                    case 9:
                        if (session.Run.phase != "RESULT" || !HasButton("ContinueGrowth")) return;
                        Click("ContinueGrowth"); step++; break;
                    case 10:
                        if (!PrepareBattle()) return;
                        if (session.Run.phase != "READY" || !HasButton("ContinueGrowth")) return;
                        Click("ContinueGrowth"); step++; break;
                    case 11:
                        if (gm.CurrentState != GameState.Playing) return;
                        gm.EndGame(); step++; break;
                    case 12:
                        if (!HasButton("BeginGrowthRun")) return;
                        Require(session.Profile.growthPoints == 500 && session.Run.phase == "FINISHED", "End-to-end run credits 500");
                        Click("OpenTalents"); step = 42; break;
                    case 42:
                        if (!HasButton("Talent_A0")) return;
                        Click("Talent_A0"); step = 13; break;
                    case 13:
                        if (!HasButton("UnlockTalent")) return;
                        Click("UnlockTalent"); step++; break;
                    case 14:
                        if (!HasButton("Confirm")) return;
                        Capture("04_unlock_confirmation");
                        Click("Confirm"); step++; break;
                    case 15:
                        if (HasButton("Confirm")) return;
                        Require(session.Profile.growthPoints == 400 && session.Profile.unlockedNodeIds.Contains("A0"), "UI confirmation spends 100 and unlocks A0");
                        Capture("05_unlocked");
                        Click("BackToMap"); step = 43; break;
                    case 43:
                        if (!HasButton("BeginGrowthRun")) return;
                        Click("BeginGrowthRun"); step = 16; break;
                    case 16:
                        if (!PrepareBattle()) return;
                        if (session.Run.phase != "READY" || !HasButton("ContinueGrowth")) return;
                        Click("ContinueGrowth"); step++; break;
                    case 17:
                        if (gm.CurrentState != GameState.Playing) return;
                        session.ReportJudge(new NoteData(0, 0, NoteType.LongStart, 1, 42), JudgeResultType.Bad);
                        session.ReportJudge(new NoteData(0, 1, NoteType.LongEnd, 0, 42), JudgeResultType.Miss);
                        Require(session.Failures == 1, "One hold head/tail produces one failure");
                        for (int i = 1; i < session.Run.failureLimit; i++) session.ReportJudge(new NoteData(0, i), JudgeResultType.Miss);
                        step++; break;
                    case 18:
                        if (session.Run.phase != "FINISHED") return;
                        Require(session.Run.outcome == "FAILED" && session.Profile.growthPoints == 400, "Failure threshold ends run without unfinished song reward");
                        Capture("06_failure");
                        Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e); }
        }

        private static Button FindButton(string name) => UnityEngine.Object.FindObjectsOfType<Button>()
            .FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);
        private static bool PrepareBattle()
        {
            if (session.Run.phase == "MAP")
            {
                var edge = PortfolioConfig.MapEdges.Single(e => e.FromNodeId == session.Run.currentNodeId && e.RoutePrice == 0);
                if (!HasButton("Room_" + edge.ToNodeId)) return false;
                if (session.Run.currentNodeId == "N02") Capture("07_paid_fork");
                Click("Room_" + edge.ToNodeId);
                return false;
            }
            if (session.Run.phase == "ROOM")
            {
                if (!HasButton("ContinueGrowth")) return false;
                Click("ContinueGrowth"); return false;
            }
            return session.Run.phase == "READY";
        }
        private static bool HasButton(string name) => FindButton(name) != null;
        private static void Click(string name)
        {
            var button = FindButton(name);
            if (button == null || !button.interactable) throw new InvalidOperationException("Unavailable button: " + name);
            button.onClick.Invoke();
            if (session.Error != null) throw new InvalidOperationException(session.Error);
        }
        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            assertions++;
            Debug.Log("[PortfolioPlayChecks] PASS " + description);
        }

        private static void Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            var canvas = GameObject.Find("PortfolioCanvas").GetComponent<Canvas>();
            var go = new GameObject("CaptureCamera");
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .045f, .075f);
            var texture = new RenderTexture(1080, 1920, 24);
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply();
            // 工程未启用ImageConversion模块，输出无压缩PPM，不为测试新增Package。
            var colors = pixels.GetPixels32();
            var rgb = new byte[1080 * 1920 * 3];
            int offset = 0;
            for (int y = 1919; y >= 0; y--)
                for (int x = 0; x < 1080; x++)
                {
                    var c = colors[y * 1080 + x];
                    rgb[offset++] = c.r; rgb[offset++] = c.g; rgb[offset++] = c.b;
                }
            using (var output = new FileStream(Path.Combine(SessionState.GetString("FA.Portfolio.Captures", ""), name + ".ppm"), FileMode.Create))
            {
                var header = System.Text.Encoding.ASCII.GetBytes("P6\n1080 1920\n255\n");
                output.Write(header, 0, header.Length); output.Write(rgb, 0, rgb.Length);
            }
            RenderTexture.active = previous; camera.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            UnityEngine.Object.Destroy(pixels); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(go);
        }
        private static void Finish(Exception error)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            string result = error == null ? "PASS " + assertions + " Play Mode integration assertions" : "FAIL step " + step + ": " + error;
            File.WriteAllText(Path.Combine(SessionState.GetString("FA.Portfolio.Captures", ""), "play_checks.txt"),
                result + "\nReal UI, real countdown/audio start, synthetic song-end boundaries and judgement data. Not full rhythm playtesting.");
            Debug.Log("[PortfolioPlayChecks] " + result);
            string temporary = SessionState.GetString("FA.Portfolio.TestStore", "");
            if (!string.IsNullOrEmpty(temporary) && Path.GetFullPath(temporary).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(temporary).StartsWith("FA_Play_", StringComparison.Ordinal) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
#endif
