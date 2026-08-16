using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FallenAngel.Core;
using FallenAngel.Data;
using FallenAngel.InputSystem;
using FallenAngel.Gameplay;
using FallenAngel.UI;
using FallenAngel.Audio;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace FallenAngel.Core
{
    /// <summary>
    /// 自动场景构建器（Editor Only）
    /// 通过菜单 Tools > FallenAngel > Build Game Scene 一键搭建场景
    /// 免去手动拖拽配置的繁琐过程
    /// </summary>
    public static class SceneBuilder
    {
#if UNITY_EDITOR
        [MenuItem("Tools/FallenAngel/Create Chinese TMP Font")]
        public static void CreateChineseTmpFont()
        {
            // TMP 默认字体（Liberation Sans）不含中文字形，中文 UI 会显示为方块。
            // 从 Windows 系统字体复制微软雅黑生成 TMP 字体资产，并设为全局默认。
            // 注意：微软雅黑仅限本机开发使用，正式发布前需替换为可商用授权字体（如思源黑体）。
            string[] candidates =
            {
                "C:/Windows/Fonts/msyh.ttc",     // 微软雅黑
                "C:/Windows/Fonts/simhei.ttf",   // 黑体
                "C:/Windows/Fonts/simsun.ttc",   // 宋体
            };
            string src = null;
            foreach (string c in candidates)
            {
                if (File.Exists(c)) { src = c; break; }
            }
            if (src == null)
            {
                Debug.LogError("[SceneBuilder] 未找到可用的系统中文字体（msyh/simhei/simsun）");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Fonts"))
                AssetDatabase.CreateFolder("Assets", "Fonts");

            string fontPath = "Assets/Fonts/" + Path.GetFileName(src);
            if (!File.Exists(fontPath))
            {
                File.Copy(src, fontPath);
                AssetDatabase.ImportAsset(fontPath);
            }
            Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError($"[SceneBuilder] 字体导入失败: {fontPath}");
                return;
            }

            string assetPath = "Assets/Fonts/CJK_Font SDF.asset";
            TMP_FontAsset fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fa == null)
            {
                fa = TMP_FontAsset.CreateFontAsset(font);
                AssetDatabase.CreateAsset(fa, assetPath);
            }

            // 设为全局默认字体（所有新建文本生效；已有场景需重建）
            // 注意：TMP 3.x 中 defaultFontAsset 是静态成员
            if (TMP_Settings.instance != null)
            {
                TMP_Settings.defaultFontAsset = fa;
                EditorUtility.SetDirty(TMP_Settings.instance);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneBuilder] 中文字体资产已就绪并设为默认: {assetPath}（请重新执行 Build Default Game Scene）");
            EditorUtility.DisplayDialog("FallenAngel",
                "中文字体已就绪。\n\n请重新执行 Tools > FallenAngel > Build Default Game Scene 重建场景，中文 UI 即可正常显示。", "OK");
        }

        [MenuItem("Tools/FallenAngel/Build Default Game Scene")]
        public static void BuildDefaultScene()
        {
            // 先确认
            if (!EditorUtility.DisplayDialog("FallenAngel",
                "这将清除当前场景并重建游戏场景结构。\n\n确定要继续吗？",
                "重建", "取消"))
                return;

            // 创建新场景
            EditorApplication.NewScene();

            GameObject root = new GameObject("___GameRoot");

            // ---- 1. EventSystem ----
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                es.transform.SetParent(root.transform);
            }

            // ---- 2. 管理器单例 ----
            GameObject managers = new GameObject("Managers");
            managers.transform.SetParent(root.transform);
            GameManager gm = managers.AddComponent<GameManager>();
            AudioManager am = managers.AddComponent<AudioManager>();
            InputManager im = managers.AddComponent<InputManager>();
            JudgeManager jm = managers.AddComponent<JudgeManager>();

            // ---- 3. Canvas ----
            GameObject canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(root.transform);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 竖屏手机分辨率
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // ---- 4. 构建UI层级 ----
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();

            // 游戏面板（包含音轨、音符、判定）
            GameObject gamePanel = CreatePanel("GamePanel", canvasRect);
            gamePanel.SetActive(false);
            CreateLaneAndNotesUI(gamePanel.transform, out NoteSpawner spawner);

            // 触点涟漪层：盖在 GamePanel 之上、MenuPanel 之下（层级顺序即渲染顺序）
            GameObject rippleLayer = CreatePanel("HitFeedbackLayer", canvasRect);
            rippleLayer.AddComponent<HitFeedbackController>();

            // 若工程里已有 Note.prefab，自动赋值给 NoteSpawner（否则使用默认Prefab）
            GameObject notePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Note.prefab");
            if (notePrefabAsset != null)
            {
                Note prefabNote = notePrefabAsset.GetComponent<Note>();
                if (prefabNote != null)
                    SetPrivateField(spawner, "notePrefab", prefabNote);
            }

            // HUD
            GameObject hudPanel = CreatePanel("HUDPanel", gamePanel.transform);
            HUDController hud = CreateHUD(hudPanel.transform);

            // 倒计时 UI（游戏开始前 3-2-1）
            GameObject countdownGO = new GameObject("CountdownText", typeof(RectTransform));
            countdownGO.transform.SetParent(gamePanel.transform, false);
            RectTransform cdrt = (RectTransform)countdownGO.transform;
            cdrt.anchorMin = new Vector2(0.5f, 0.5f);
            cdrt.anchorMax = new Vector2(0.5f, 0.5f);
            cdrt.pivot = new Vector2(0.5f, 0.5f);
            cdrt.anchoredPosition = Vector2.zero;
            cdrt.sizeDelta = new Vector2(500, 500);
            TextMeshProUGUI countdownText = countdownGO.AddComponent<TextMeshProUGUI>();
            countdownText.text = "";
            countdownText.alignment = TextAlignmentOptions.Center;
            countdownText.fontSize = 300;
            countdownText.fontStyle = FontStyles.Bold;
            countdownText.color = Color.white;
            countdownText.raycastTarget = false;
            countdownGO.SetActive(false);

            // 将倒计时文本引用传递给 GameManager
            gm.SetCountdownText(countdownText);

            // 暂停按钮（放右上角）
            GameObject pauseBtnGO = CreateButton("PauseButton", hudPanel.transform,
                new Vector2(1f, 1f), new Vector2(100, 80), "❚❚", 36);
            RectTransform pbt = (RectTransform)pauseBtnGO.transform;
            pbt.anchorMin = new Vector2(1f, 1f);
            pbt.anchorMax = new Vector2(1f, 1f);
            pbt.pivot = new Vector2(1f, 1f);
            pbt.anchoredPosition = new Vector2(-20, -20);
            pbt.sizeDelta = new Vector2(100, 80);

            // 菜单面板（先创建，后面要用）
            GameObject menuPanel = CreatePanel("MenuPanel", canvasRect);

            // 暂停面板 UI（PauseRoot + Resume/Exit 按钮）
            GameObject pausePanelGO = CreatePanel("PausePanel", gamePanel.transform);
            CreatePausePanel(pausePanelGO.transform);
            // 关键：PauseController 挂在 HUDPanel（始终激活），不是 PauseRoot（SetActive(false)）
            PauseController hudPC = hudPanel.AddComponent<PauseController>();
            SetPrivateField(hudPC, "pauseButton", pauseBtnGO.GetComponent<Button>());
            SetPrivateField(hudPC, "pausePanel", pausePanelGO);
            SetPrivateField(hudPC, "gamePanel", gamePanel);
            SetPrivateField(hudPC, "menuPanel", menuPanel);

            // 结算面板 —— ResultPanel 是始终激活的容器，ResultRoot 是内容（SetActive(false)）
            GameObject resultPanel = CreatePanel("ResultPanel", gamePanel.transform);
            CreateResultScreen(resultPanel.transform);
            // 关键：在 GamePanel 上挂 ResultScreen（始终激活）
            ResultScreen gamePanelRS = gamePanel.AddComponent<ResultScreen>();
            SetPrivateField(gamePanelRS, "rootPanel", resultPanel);
            SetPrivateField(gamePanelRS, "gamePanel", gamePanel);
            SetPrivateField(gamePanelRS, "menuPanel", menuPanel);

            GameStarter starter = CreateGameStarter(menuPanel.transform, gamePanel);

            // 节拍校准面板：入口按钮监听由 GameStarter.Awake 接（校准面板初始非激活，其自身 Awake 不执行）
            CreateCalibrationPanel(canvasRect, menuPanel.transform, starter);

            // ---- 5. 链接引用 ----
            // (多数引用通过Inspector面板拖入，这里尽量给默认值)
            Debug.Log("[SceneBuilder] 场景基本结构已创建。请在Inspector中补充:");
            Debug.Log("  - NoteSpawner.notePrefab: 需要手动创建Note Prefab后指定");
            Debug.Log("  - AudioManager 各种AudioClip: 音效资源");
            Debug.Log("  - HUDController / ResultScreen 中的Text/Button引用");
            Selection.activeGameObject = root;

            EditorUtility.DisplayDialog("FallenAngel",
                "场景结构已搭建完成！\n\n" +
                "还需要手动完成：\n" +
                "1. 创建 Note Prefab（包含 Note 脚本 + Image + 长按身体Image）\n" +
                "2. 把 Note Prefab 拖到 NoteSpawner 上\n" +
                "3. 拖拽 HUD / ResultScreen 中的 UI 文本引用\n" +
                "4. 在 Resources/Charts 放入谱面JSON，Resources/Audio放入音乐\n" +
                "5. 给 GameStarter 勾上 Auto Start Demo On Awake 可立即运行测试",
                "OK");
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static void CreateLaneAndNotesUI(Transform parent, out NoteSpawner spawner)
        {
            // 音轨背景
            GameObject lanes = CreatePanel("LanesBG", parent);
            Image lanesImg = lanes.AddComponent<Image>();
            lanesImg.color = new Color(0, 0, 0, 0.5f);
            lanesImg.raycastTarget = false; // 不拦截触摸

            // 画四个竖条
            Color[] laneColors = new Color[]
            {
                new Color(0.2f, 0.6f, 1f, 0.12f),
                new Color(0.2f, 1f, 0.4f, 0.12f),
                new Color(1f, 0.85f, 0.2f, 0.12f),
                new Color(1f, 0.3f, 0.3f, 0.12f)
            };
            float[] laneX = LaneLayout.CentersX; // 轨道布局统一取自 LaneLayout（与输入判定同源）
            float laneWidth = 140f;

            // 按键区域容器（下半屏）
            GameObject keyContainer = new GameObject("KeyArea", typeof(RectTransform));
            keyContainer.transform.SetParent(lanes.transform, false);
            RectTransform keyCRect = (RectTransform)keyContainer.transform;
            keyCRect.anchorMin = new Vector2(0.5f, 0f);
            keyCRect.anchorMax = new Vector2(0.5f, 0f);
            keyCRect.pivot = new Vector2(0.5f, 0f);
            keyCRect.anchoredPosition = Vector2.zero;
            keyCRect.sizeDelta = new Vector2(900, 500);

            // 判定线
            GameObject judgeLine = new GameObject("JudgeLine", typeof(RectTransform), typeof(Image));
            judgeLine.transform.SetParent(lanes.transform, false);
            RectTransform jlRect = (RectTransform)judgeLine.transform;
            jlRect.anchorMin = new Vector2(0.5f, 0f);
            jlRect.anchorMax = new Vector2(0.5f, 0f);
            jlRect.pivot = new Vector2(0.5f, 0.5f);
            jlRect.anchoredPosition = new Vector2(0, 400);
            jlRect.sizeDelta = new Vector2(1000, 6);
            Image jlImg = judgeLine.GetComponent<Image>();
            jlImg.color = Color.white;
            jlImg.raycastTarget = false;

            // 音符容器
            GameObject notesContainer = new GameObject("NotesContainer", typeof(RectTransform));
            notesContainer.transform.SetParent(lanes.transform, false);
            RectTransform ncRect = (RectTransform)notesContainer.transform;
            ncRect.anchorMin = Vector2.zero;
            ncRect.anchorMax = Vector2.one;
            ncRect.offsetMin = Vector2.zero;
            ncRect.offsetMax = Vector2.zero;

            // NoteSpawner
            spawner = notesContainer.AddComponent<NoteSpawner>();
            SetPrivateField(spawner, "notesContainer", ncRect);
            SetPrivateField(spawner, "lanePositionsX", laneX);
            SetPrivateField(spawner, "judgeLineY", -400f);
            SetPrivateField(spawner, "spawnY", 1200f);

            // 4个音轨条 + LaneKeyVisual
            for (int i = 0; i < 4; i++)
            {
                // 音轨背景条
                GameObject lane = new GameObject($"Lane{i}_BG", typeof(RectTransform), typeof(Image));
                lane.transform.SetParent(lanes.transform, false);
                RectTransform lRT = (RectTransform)lane.transform;
                lRT.anchorMin = new Vector2(0.5f, 0f);
                lRT.anchorMax = new Vector2(0.5f, 1f);
                lRT.pivot = new Vector2(0.5f, 0.5f);
                lRT.anchoredPosition = new Vector2(laneX[i], 0);
                lRT.sizeDelta = new Vector2(laneWidth, 0);
                lane.GetComponent<Image>().color = laneColors[i];
                lane.GetComponent<Image>().raycastTarget = false;

                // 按键视觉（下半屏按键区域）
                GameObject keyArea = new GameObject($"Lane{i}_Key", typeof(RectTransform), typeof(Image));
                keyArea.transform.SetParent(keyContainer.transform, false);
                RectTransform kRT = (RectTransform)keyArea.transform;
                kRT.anchorMin = new Vector2(0.5f, 0f);
                kRT.anchorMax = new Vector2(0.5f, 1f);
                kRT.pivot = new Vector2(0.5f, 0.5f);
                kRT.anchoredPosition = new Vector2(laneX[i], 0);
                kRT.sizeDelta = new Vector2(laneWidth, 0);
                Image keyImg = keyArea.GetComponent<Image>();
                Color c = laneColors[i]; c.a = 0.3f;
                keyImg.color = c;
                keyImg.raycastTarget = false;

                // 按键图标
                GameObject keyIcon = new GameObject($"KeyIcon", typeof(RectTransform), typeof(Image));
                keyIcon.transform.SetParent(keyArea.transform, false);
                RectTransform kiRT = (RectTransform)keyIcon.transform;
                kiRT.anchorMin = new Vector2(0.5f, 0.5f);
                kiRT.anchorMax = new Vector2(0.5f, 0.5f);
                kiRT.pivot = new Vector2(0.5f, 0.5f);
                kiRT.anchoredPosition = new Vector2(0, -100);
                kiRT.sizeDelta = new Vector2(100, 100);
                Image kiImg = keyIcon.GetComponent<Image>();
                kiImg.color = laneColors[i];
                kiImg.raycastTarget = false;

                // 判定线发光效果（放按键顶部）
                GameObject glow = new GameObject($"JudgeGlow", typeof(RectTransform), typeof(Image));
                glow.transform.SetParent(keyArea.transform, false);
                RectTransform gRT = (RectTransform)glow.transform;
                gRT.anchorMin = new Vector2(0.5f, 1f);
                gRT.anchorMax = new Vector2(0.5f, 1f);
                gRT.pivot = new Vector2(0.5f, 0f);
                gRT.anchoredPosition = Vector2.zero;
                gRT.sizeDelta = new Vector2(laneWidth + 30, 60);
                Image gImg = glow.GetComponent<Image>();
                gImg.color = new Color(laneColors[i].r, laneColors[i].g, laneColors[i].b, 0f);
                gImg.raycastTarget = false;

                LaneKeyVisual lkv = keyArea.AddComponent<LaneKeyVisual>();
                SetPrivateField(lkv, "laneIndex", i);
                SetPrivateField(lkv, "keyAreaImage", keyImg);
                SetPrivateField(lkv, "judgeLineGlow", gImg);
                SetPrivateField(lkv, "keyVisual", kiRT);
            }
        }

        private static HUDController CreateHUD(Transform parent)
        {
            GameObject hudGO = new GameObject("HUD", typeof(RectTransform));
            hudGO.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)hudGO.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // ScoreText
            TextMeshProUGUI score = CreateText("ScoreText", hudGO.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(800, 120),
                "0", 80, TextAlignmentOptions.TopRight);
            score.color = Color.white;
            score.fontStyle = FontStyles.Bold;

            // ComboText
            TextMeshProUGUI combo = CreateText("ComboText", hudGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(600, 100),
                "0", 90, TextAlignmentOptions.Center);
            combo.color = Color.white;
            combo.fontStyle = FontStyles.Bold;

            TextMeshProUGUI comboLabel = CreateText("ComboLabel", hudGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(400, 60),
                "COMBO", 40, TextAlignmentOptions.Center);

            // JudgeResult
            TextMeshProUGUI judge = CreateText("JudgeText", hudGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(800, 100),
                "", 72, TextAlignmentOptions.Center);
            judge.fontStyle = FontStyles.Bold;

            // 早/晚指示（Phigros 手感参考），显示在判定文本下方
            TextMeshProUGUI judgeBias = CreateText("JudgeBiasText", hudGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(400, 50),
                "", 36, TextAlignmentOptions.Center);
            judgeBias.fontStyle = FontStyles.Bold;

            // Song Title
            TextMeshProUGUI title = CreateText("SongTitle", hudGO.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -40), new Vector2(800, 60),
                "", 36, TextAlignmentOptions.TopLeft);

            // Progress Bar
            GameObject barBG = new GameObject("ProgressBarBG", typeof(RectTransform), typeof(Image));
            barBG.transform.SetParent(hudGO.transform, false);
            RectTransform barBGRT = (RectTransform)barBG.transform;
            barBGRT.anchorMin = new Vector2(0f, 1f);
            barBGRT.anchorMax = new Vector2(1f, 1f);
            barBGRT.pivot = new Vector2(0.5f, 1f);
            barBGRT.anchoredPosition = new Vector2(0, -10);
            barBGRT.sizeDelta = new Vector2(-40, 20);
            barBG.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
            barBG.GetComponent<Image>().raycastTarget = false;

            GameObject barFill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
            barFill.transform.SetParent(barBG.transform, false);
            RectTransform fillRT = (RectTransform)barFill.transform;
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(1f, 1f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            Image fillImg = barFill.GetComponent<Image>();
            fillImg.color = new Color(0.3f, 0.8f, 1f);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0f;
            fillImg.raycastTarget = false;

            TextMeshProUGUI progressTxt = CreateText("ProgressText", hudGO.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -40), new Vector2(400, 40),
                "00:00 / 00:00", 28, TextAlignmentOptions.TopRight);

            HUDController hudC = hudGO.AddComponent<HUDController>();
            SetPrivateField(hudC, "scoreText", score);
            SetPrivateField(hudC, "comboText", combo);
            SetPrivateField(hudC, "comboLabelText", comboLabel);
            SetPrivateField(hudC, "judgeResultText", judge);
            SetPrivateField(hudC, "judgeBiasText", judgeBias);
            SetPrivateField(hudC, "songTitleText", title);
            SetPrivateField(hudC, "progressFillImage", fillImg);
            SetPrivateField(hudC, "progressText", progressTxt);
            return hudC;
        }

        private static void CreateResultScreen(Transform parent)
        {
            // 半透明黑色背景 + 结算内容 UI
            GameObject root = new GameObject("ResultRoot", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            root.GetComponent<Image>().raycastTarget = false;

            TextMeshProUGUI songName = CreateText("SongName", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(900, 80),
                "Song Title", 54, TextAlignmentOptions.Center);
            songName.fontStyle = FontStyles.Bold;

            TextMeshProUGUI artist = CreateText("Artist", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -280), new Vector2(900, 50),
                "Artist", 32, TextAlignmentOptions.Center);

            TextMeshProUGUI diff = CreateText("Difficulty", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -340), new Vector2(500, 40),
                "NORMAL Lv.7", 28, TextAlignmentOptions.Center);

            TextMeshProUGUI rank = CreateText("Rank", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -500), new Vector2(400, 250),
                "S", 200, TextAlignmentOptions.Center);
            rank.fontStyle = FontStyles.Bold;
            rank.color = Color.yellow;

            TextMeshProUGUI score = CreateText("Score", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -720), new Vector2(800, 80),
                "1,000,000", 72, TextAlignmentOptions.Center);
            score.fontStyle = FontStyles.Bold;

            TextMeshProUGUI acc = CreateText("Accuracy", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -810), new Vector2(500, 50),
                "100.00%", 40, TextAlignmentOptions.Center);

            TextMeshProUGUI maxCombo = CreateText("MaxCombo", root.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -870), new Vector2(500, 40),
                "MAX COMBO: 999", 30, TextAlignmentOptions.Center);

            // 判定统计
            string[] labels = { "PERFECT", "GREAT", "GOOD", "BAD", "MISS" };
            float startY = -1000f;
            TextMeshProUGUI perfectTxt = null, greatTxt = null, goodTxt = null, badTxt = null, missTxt = null;
            for (int i = 0; i < 5; i++)
            {
                CreateText($"Label_{labels[i]}", root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-150, startY - i * 60), new Vector2(250, 40),
                    labels[i], 28, TextAlignmentOptions.Right);

                TextMeshProUGUI val = CreateText($"Val_{labels[i]}", root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(150, startY - i * 60), new Vector2(250, 40),
                    "0", 28, TextAlignmentOptions.Left);
                val.fontStyle = FontStyles.Bold;

                switch (i)
                {
                    case 0: perfectTxt = val; break;
                    case 1: greatTxt = val; break;
                    case 2: goodTxt = val; break;
                    case 3: badTxt = val; break;
                    case 4: missTxt = val; break;
                }
            }

            // 按钮
            GameObject btnRetry = CreateButton("RetryButton", root.transform,
                new Vector2(0.3f, 0.08f), new Vector2(300, 120), "RETRY", 48);
            GameObject btnBack = CreateButton("BackButton", root.transform,
                new Vector2(0.7f, 0.08f), new Vector2(300, 120), "BACK", 48);

            // 设置挂在 GamePanel 上的 ResultScreen 的所有引用
            // 通过 parent.parent 找到 GamePanel 上的 ResultScreen
            Transform gamePanelT = parent.parent;
            if (gamePanelT != null)
            {
                ResultScreen rs = gamePanelT.GetComponent<ResultScreen>();
                if (rs != null)
                {
                    SetPrivateField(rs, "songNameText", songName);
                    SetPrivateField(rs, "songArtistText", artist);
                    SetPrivateField(rs, "difficultyText", diff);
                    SetPrivateField(rs, "rankText", rank);
                    SetPrivateField(rs, "scoreText", score);
                    SetPrivateField(rs, "accuracyText", acc);
                    SetPrivateField(rs, "maxComboText", maxCombo);
                    SetPrivateField(rs, "perfectCountText", perfectTxt);
                    SetPrivateField(rs, "greatCountText", greatTxt);
                    SetPrivateField(rs, "goodCountText", goodTxt);
                    SetPrivateField(rs, "badCountText", badTxt);
                    SetPrivateField(rs, "missCountText", missTxt);
                    SetPrivateField(rs, "retryButton", btnRetry.GetComponent<Button>());
                    SetPrivateField(rs, "backButton", btnBack.GetComponent<Button>());
                    SetPrivateField(rs, "rootPanel", parent.gameObject);
                }
            }
        }

        private static GameStarter CreateGameStarter(Transform menuParent, GameObject gamePanel)
        {
            // 菜单标题
            TextMeshProUGUI title = CreateText("GameTitle", menuParent,
                new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(900, 200),
                "Fallen Angel", 120, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(0.7f, 0.85f, 1f);

            TextMeshProUGUI subtitle = CreateText("Subtitle", menuParent,
                new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f), Vector2.zero, new Vector2(600, 60),
                "4K Rhythm Game", 40, TextAlignmentOptions.Center);
            subtitle.color = new Color(1, 1, 1, 0.8f);

            TextMeshProUGUI hint = CreateText("Hint", menuParent,
                new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(900, 200),
                "PC: D F J K to hit notes  |  SPACE to pause  |  ESC to exit\nMobile: Tap the 4 areas at bottom screen",
                32, TextAlignmentOptions.Center);
            hint.color = new Color(1, 1, 1, 0.7f);

            GameObject startBtn = CreateButton("StartDemoButton", menuParent,
                new Vector2(0.5f, 0.3f), new Vector2(500, 160), "START DEMO", 56);

            GameStarter starter = menuParent.gameObject.AddComponent<GameStarter>();
            SetPrivateField(starter, "menuPanel", menuParent.gameObject);
            SetPrivateField(starter, "gamePanel", gamePanel);
            SetPrivateField(starter, "startDemoButton", startBtn.GetComponent<Button>());
            SetPrivateField(starter, "autoStartDemoOnAwake", false);

            // 自动生成谱的选择按钮：只创建按钮，监听在 GameStarter.Awake（Play 模式）统一接
            // （编辑模式 AddListener 会在进入 Play 时被序列化清空）
            GameObject drumsBtn = CreateButton("DemoDrumsButton", menuParent,
                new Vector2(0.28f, 0.16f), new Vector2(340, 100), "鼓谱", 40);
            GameObject bassBtn = CreateButton("DemoBassButton", menuParent,
                new Vector2(0.5f, 0.16f), new Vector2(340, 100), "贝斯谱", 40);
            GameObject synthBtn = CreateButton("DemoSynthButton", menuParent,
                new Vector2(0.72f, 0.16f), new Vector2(340, 100), "合成器谱", 40);
            SetPrivateField(starter, "drumsButton", drumsBtn.GetComponent<Button>());
            SetPrivateField(starter, "bassButton", bassBtn.GetComponent<Button>());
            SetPrivateField(starter, "synthButton", synthBtn.GetComponent<Button>());
            return starter;
        }

        /// <summary>
        /// 节拍校准：菜单入口按钮 + 校准面板（开始测试/应用推荐/±5ms/关闭）
        /// </summary>
        private static void CreateCalibrationPanel(RectTransform canvasRect, Transform menuParent, GameStarter starter)
        {
            GameObject openBtn = CreateButton("CalibrationButton", menuParent,
                new Vector2(0.5f, 0.055f), new Vector2(400, 90), "节拍校准", 40);

            GameObject panel = new GameObject("CalibrationPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            TextMeshProUGUI title = CreateText("CalTitle", panel.transform,
                new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(700, 100),
                "节拍校准", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            TextMeshProUGUI offsetTxt = CreateText("CalOffsetText", panel.transform,
                new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(800, 90),
                "", 40, TextAlignmentOptions.Center);

            TextMeshProUGUI statusTxt = CreateText("CalStatusText", panel.transform,
                new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(900, 160),
                "点击开始测试，跟着滴答节拍点击屏幕", 32, TextAlignmentOptions.Center);

            TextMeshProUGUI pulse = CreateText("CalPulseText", panel.transform,
                new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.36f), Vector2.zero, new Vector2(200, 200),
                "", 120, TextAlignmentOptions.Center);
            pulse.raycastTarget = false;

            GameObject startBtn = CreateButton("CalStartButton", panel.transform,
                new Vector2(0.5f, 0.22f), new Vector2(400, 100), "开始测试", 40);
            GameObject applyBtn = CreateButton("CalApplyButton", panel.transform,
                new Vector2(0.5f, 0.14f), new Vector2(400, 90), "应用推荐值", 36);
            applyBtn.GetComponent<Button>().interactable = false;
            GameObject minusBtn = CreateButton("CalMinusButton", panel.transform,
                new Vector2(0.35f, 0.085f), new Vector2(220, 80), "-5ms", 36);
            GameObject plusBtn = CreateButton("CalPlusButton", panel.transform,
                new Vector2(0.65f, 0.085f), new Vector2(220, 80), "+5ms", 36);
            GameObject closeBtn = CreateButton("CalCloseButton", panel.transform,
                new Vector2(0.5f, 0.025f), new Vector2(300, 70), "关闭", 34);

            CalibrationController cal = panel.AddComponent<CalibrationController>();
            SetPrivateField(cal, "panelRoot", panel);
            SetPrivateField(cal, "statusText", statusTxt);
            SetPrivateField(cal, "offsetText", offsetTxt);
            SetPrivateField(cal, "pulseText", pulse);
            SetPrivateField(cal, "startTestButton", startBtn.GetComponent<Button>());
            SetPrivateField(cal, "applyButton", applyBtn.GetComponent<Button>());
            SetPrivateField(cal, "plusButton", plusBtn.GetComponent<Button>());
            SetPrivateField(cal, "minusButton", minusBtn.GetComponent<Button>());
            SetPrivateField(cal, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 入口按钮与控制器交给 GameStarter（始终激活，Awake 时接线）
            SetPrivateField(starter, "calibrationButton", openBtn.GetComponent<Button>());
            SetPrivateField(starter, "calibrationController", cal);
        }

        private static PauseController CreatePausePanel(Transform parent)
        {
            // 半透明黑色背景
            GameObject root = new GameObject("PauseRoot", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.75f);
            bg.raycastTarget = false;
            root.SetActive(false);

            // PAUSED 文本
            TextMeshProUGUI pausedText = CreateText("PausedText", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 350), new Vector2(600, 120),
                "PAUSED", 80, TextAlignmentOptions.Center);
            pausedText.fontStyle = FontStyles.Bold;
            pausedText.color = Color.white;

            // Resume 按钮
            GameObject resumeBtn = CreateButton("ResumeButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "RESUME", 48);
            RectTransform rbt = (RectTransform)resumeBtn.transform;
            rbt.anchoredPosition = new Vector2(0, 180);

            // Retry 按钮
            GameObject retryBtn = CreateButton("RetryButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "RETRY", 48);
            RectTransform retbt = (RectTransform)retryBtn.transform;
            retbt.anchoredPosition = new Vector2(0, 0);

            // Exit 按钮
            GameObject exitBtn = CreateButton("ExitButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "EXIT TO MENU", 40);
            RectTransform ebt = (RectTransform)exitBtn.transform;
            ebt.anchoredPosition = new Vector2(0, -180);

            // 只创建 UI 元素（PauseRoot + 按钮），不挂 PauseController
            // 真正的控制器在 HUDPanel 上
            return null;
        }

        // ================ Text 创建辅助 ================
        private static TextMeshProUGUI CreateText(string name, Transform parent,
            Vector2 aMin, Vector2 aMax, Vector2 anchoredPos, Vector2 size,
            string text, int fontSize, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = Color.white;
            txt.enableWordWrapping = true;
            return txt;
        }

        private static GameObject CreateButton(string name, Transform parent,
            Vector2 anchor, Vector2 size, string labelText, int fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.8f, 0.9f);
            TextMeshProUGUI label = CreateText("Label", go.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size, labelText, fontSize, TextAlignmentOptions.Center);
            label.color = Color.white;
            label.fontStyle = FontStyles.Bold;
            return go;
        }

        // 反射设置私有字段
        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field != null)
                field.SetValue(obj, value);
            else
                Debug.LogWarning($"[SceneBuilder] 找不到字段: {obj.GetType().Name}.{fieldName}");
        }
#endif
    }
}
