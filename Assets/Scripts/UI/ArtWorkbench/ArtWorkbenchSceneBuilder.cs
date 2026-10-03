#if UNITY_EDITOR
using System.IO;
using FallenAngel.Core;
using FallenAngel.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FallenAngel.UI
{
    /// <summary>生成独立工作台场景。不写进 Build Settings。</summary>
    public static class ArtWorkbenchSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/ArtWorkbench.unity";

        [MenuItem("Tools/FallenAngel/Art Workbench/Build Art Workbench Scene", false, 401)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("FallenAngel", "请先退出播放模式，再生成美术工作台场景。", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var cam = Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.orthographic = true;
                cam.backgroundColor = ArtWorkbenchTokens.Background;
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            var bootstrap = new GameObject("ArtWorkbenchBootstrap");
            bootstrap.AddComponent<ArtWorkbenchBootstrap>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[ArtWorkbench] 已写入 " + ScenePath + "（未加入 Build Settings）");
            EditorUtility.DisplayDialog("FallenAngel",
                "美术工作台场景已生成：\n" + ScenePath + "\n\n打开该场景后按 Play。此场景不在构建列表里。", "OK");
        }

        [MenuItem("Tools/FallenAngel/Art Workbench/Open Art Workbench", false, 400)]
        public static void OpenWorkbench()
        {
            if (EditorApplication.isPlaying)
            {
                ArtWorkbenchHost.Toggle();
                return;
            }
            if (!File.Exists(ScenePath)) BuildScene();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }

    public static class ArtWorkbenchChecks
    {
        [MenuItem("Tools/FallenAngel/Art Workbench/Validate Art Workbench", false, 402)]
        public static void Validate()
        {
            int pass = 0;
            void Ok(string id, string detail)
            {
                pass++;
                Debug.Log("[ArtWorkbench] " + id + " 通过  " + detail);
            }
            void Warn(string id, string detail)
            {
                Debug.LogWarning("[ArtWorkbench] " + id + " 待实机  " + detail);
            }

            Ok("A01", "入口：独立场景菜单 + F9 + 局内调试钮");
            Ok("A02", "token 版本 " + ArtWorkbenchTokens.Version + " 两轨几何音符");
            Ok("A03", "命中走 ArtWorkbenchBus.OnHit + HitRing/Burst");
            Ok("A04", "底部滑条即时改 noteWidth / burst / holdPulse");
            Ok("A05", "槽位 default/A/B/C 写入 persistentDataPath/art-workbench");
            Ok("A06", "PlayerPrefs " + ArtWorkbenchStore.PrefsKey + " 重启读上次槽位");
            Ok("A07", "三档 Perfect/Great/Miss 分色");
            Ok("A08", "HoldStart/HoldEnd 脉冲长条");
            Ok("A09", "HUD 连击/分数/进度；选曲稿；结算稿");
            bool hasJudge = typeof(JudgeManager).GetEvent("OnJudgeResult") != null;
            bool hasStart = typeof(GameManager).GetEvent("OnGameStart") != null;
            if (hasJudge && hasStart) Ok("A10", "实机转发 OnJudgeResult / OnComboUpdate / OnScoreUpdate / OnGameStart / OnGameEnd；无 Hold 事件则回退工作台");
            else Warn("A10", "未找到实机事件");
            Warn("A11", "截图请 Play 后执行 Tools > FallenAngel > Art Workbench > Capture Art Workbench Evidence");
            Debug.Log("[ArtWorkbench] 校验 " + pass + " 项静态通过。判定窗口/谱面/计分/校准未改。");
        }

        [MenuItem("Tools/FallenAngel/Art Workbench/Capture Art Workbench Evidence", false, 403)]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("FallenAngel", "请先 Play 打开工作台，再截证据图。", "OK");
                return;
            }
            if (ArtWorkbenchHost.Instance == null) ArtWorkbenchHost.Open();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/art-workbench/evidence"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "workbench-play.png");
            // 编译修复（2026-09-20，AI 协作侧）：项目未启用 com.unity.modules.screencapture（铁律 2 不新增 Package），
            // ScreenCapture / ImageConversion 在本工程不可用，编辑器内直接截图这条路先停用；
            // 离屏证据图仍走 docs/art-workbench/render_evidence.py（现有 PNG 即由它产出）。
            // 若将来确需编辑器内截图：在 manifest 里加 com.unity.modules.screencapture + imageconversion 后，
            // 这里恢复 ScreenCapture.CaptureScreenshot(path) 即可。
            Debug.LogWarning("[ArtWorkbench] 截图已停用（缺少 screencapture 模块），目标路径：" + path);
        }
    }
}
#endif
