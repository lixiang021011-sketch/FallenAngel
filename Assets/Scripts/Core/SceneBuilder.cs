using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel; // GlyphRenderMode（TMP 字体图集渲染模式）
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
            // 从系统字体生成 TMP 字体资产；优先纯 TTF（simhei），TTC 集合字体（msyh/simsun）
            // 在部分环境下 CreateFontAsset 会生成空图集（m_AtlasTextures 为空），
            // 因此生成后必须校验图集，坏资产自动删除并尝试下一个候选。
            // 注意：系统字体仅限本机开发使用，正式发布前替换为可商用授权字体（如思源黑体）。
            string[] candidates =
            {
                "C:/Windows/Fonts/simhei.ttf",   // 黑体（纯 TTF，优先）
                "C:/Windows/Fonts/msyh.ttc",     // 微软雅黑（TTC 集合）
                "C:/Windows/Fonts/simsun.ttc",   // 宋体（TTC 集合）
            };

            if (!AssetDatabase.IsValidFolder("Assets/Fonts"))
                AssetDatabase.CreateFolder("Assets", "Fonts");

            const string assetPath = "Assets/Fonts/CJK_Font SDF.asset";

            // 已有可用资产则直接用；损坏资产删除重建
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null && !IsFontAssetUsable(existing))
            {
                Debug.LogWarning("[SceneBuilder] 检测到损坏的中文字体资产，删除重建");
                AssetDatabase.DeleteAsset(assetPath);
                existing = null;
            }

            TMP_FontAsset fa = existing;
            if (fa == null)
            {
                foreach (string src in candidates)
                {
                    if (!File.Exists(src)) continue;

                    string fontPath = "Assets/Fonts/" + Path.GetFileName(src);
                    if (!File.Exists(fontPath))
                    {
                        File.Copy(src, fontPath);
                        AssetDatabase.ImportAsset(fontPath);
                    }
                    Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                    if (font == null) continue;

                    TMP_FontAsset candidate = TMP_FontAsset.CreateFontAsset(
                        font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                        AtlasPopulationMode.Dynamic, true);
                    if (!IsFontAssetUsable(candidate))
                    {
                        Debug.LogWarning($"[SceneBuilder] {src} 生成图集失败（TTC 字体常见），尝试下一个候选");
                        Object.DestroyImmediate(candidate);
                        continue;
                    }
                    AssetDatabase.CreateAsset(candidate, assetPath);
                    // 关键：图集纹理与材质必须存为资产子对象，否则只保存壳文件（约3KB），
                    // 重新加载后 atlasTextures 为空 → 回退默认字体 → 中文方块
                    if (candidate.material != null)
                        AssetDatabase.AddObjectToAsset(candidate.material, candidate);
                    if (candidate.atlasTextures != null)
                        foreach (Texture2D tex in candidate.atlasTextures)
                            if (tex != null) AssetDatabase.AddObjectToAsset(tex, candidate);
                    AssetDatabase.SaveAssets();
                    // 从磁盘重新加载，校验持久化结果（内存校验通过不等于落盘成功）
                    fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                    if (!IsFontAssetUsable(fa))
                    {
                        Debug.LogWarning($"[SceneBuilder] {src} 持久化后图集丢失，尝试下一个候选");
                        AssetDatabase.DeleteAsset(assetPath);
                        fa = null;
                        continue;
                    }
                    Debug.Log($"[SceneBuilder] 中文字体资产生成成功: {src} -> {assetPath}");
                    break;
                }
            }

            if (fa == null)
            {
                Debug.LogError("[SceneBuilder] 所有候选系统字体均生成失败，中文 UI 将无法显示");
                EditorUtility.DisplayDialog("FallenAngel",
                    "中文字体生成失败。\n\n候选字体（simhei/msyh/simsun）均未能生成有效图集。", "OK");
                return;
            }

            cjkFontLoaded = false;
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("FallenAngel",
                "中文字体已就绪。\n\n请重新执行 Tools > FallenAngel > Build Default Game Scene 重建场景，中文 UI 即可正常显示。", "OK");
        }

        /// <summary>字体资产是否可用（图集必须有效，否则 TMP 赋值即抛异常）</summary>
        private static bool IsFontAssetUsable(TMP_FontAsset fa)
        {
            return fa != null && fa.atlasTextures != null &&
                   fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null;
        }

        [MenuItem("Tools/FallenAngel/Build Default Game Scene")]
        public static void BuildDefaultScene()
        {
            BuildDefaultScene(true);
        }

        /// <summary>confirm=false 供命令行打包（batchmode 下 DisplayDialog 返回 false 会中断流程）</summary>
        public static void BuildDefaultScene(bool confirm)
        {
            // 播放模式下 EditorApplication.NewScene 不可用（会抛 InvalidOperationException）
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("FallenAngel",
                    "请先退出播放模式（点播放按钮停止运行），再重建场景。", "OK");
                return;
            }

            // 先确认
            if (confirm && !EditorUtility.DisplayDialog("FallenAngel",
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
            managers.AddComponent<RunManager>();

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
            Transform pauseRootT = pausePanelGO.transform.Find("PauseRoot");
            // 下落速度调节控制器：挂 Canvas 根（始终激活）。
            // 此前挂 HUDPanel（GamePanel 子节点）时，Menu 状态 GamePanel 失活 → 设置页速度按钮监听不派发。
            FallSpeedController fsc = canvasGO.AddComponent<FallSpeedController>();
            if (pauseRootT != null)
                SetPrivateField(fsc, "pauseRoot", pauseRootT.gameObject);
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

            GameStarter starter = CreateGameStarter(canvasRect, menuPanel.transform, gamePanel);

            // 选项设置页：构建顺序在 CalibrationPanel 之前（校准面板后建，渲染盖在设置页上）
            SettingsPanelController settingsController = CreateSettingsPanel(canvasRect, starter, fsc);

            // 节拍校准面板：入口在设置页；反向注入控制器/面板引用给设置页（ESC 层叠与打开按钮）
            CreateCalibrationPanel(canvasRect, menuPanel.transform, starter, settingsController);

            // 正式存档选择面板（选中档后出现"进入游戏"/"天赋"按钮；盖在设置页之上）
            SaveSelectPanelController saveSelectController = CreateSaveSelectPanel(canvasRect);
            SetPrivateField(starter, "saveSelectPanelController", saveSelectController);

            // 天赋面板（后建，盖在存档选择面板上；反向注入给存档选择面板的"天赋"按钮与 ESC 层叠）
            CreateTalentPanel(canvasRect, saveSelectController, starter);

            // 语言选择面板（遍历 Language 枚举生成按钮，新增语言自动扩展）
            CreateLanguagePanel(canvasRect, menuPanel.transform, starter);

            // 选歌界面（卷帘滚动列表；入口按钮与面板注入 starter）
            CreateSongSelectPanel(canvasRect, menuPanel.transform, starter);

            // Roguelite 地图面板（MapPanel 根始终激活，内容随 GameState.Map 显隐）
            CreateMapPanel(canvasRect);

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

            // 恒建 5 轨（4K 谱运行时由 LanePanelController 隐藏第 5 轨并重定位）
            float[] laneX = LaneLayout.GetCentersX(LaneLayout.MaxLaneCount); // 轨道布局统一取自 LaneLayout（与输入判定同源）
            float laneWidth = LaneLayout.LaneWidth;

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

            // 5 个音轨条 + LaneKeyVisual（LanePanelController 按谱面键数启停/重定位）
            lanes.AddComponent<LanePanelController>();
            for (int i = 0; i < LaneLayout.MaxLaneCount; i++)
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
                Color laneC = LaneColors.GetLaneColor(i); laneC.a = 0.12f;
                lane.GetComponent<Image>().color = laneC;
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
                Color c = LaneColors.GetLaneColor(i); c.a = 0.3f;
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
                kiImg.color = LaneColors.GetLaneColor(i);
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
                Color gc = LaneColors.GetLaneColor(i); gc.a = 0f;
                gImg.color = gc;
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
                "hud.comboLabel", 40, TextAlignmentOptions.Center);

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

            // 判定统计（节点名保持英文，显示文本走语言表 key）
            string[] labels = { "PERFECT", "GREAT", "GOOD", "BAD", "MISS" };
            float startY = -1000f;
            TextMeshProUGUI perfectTxt = null, greatTxt = null, goodTxt = null, badTxt = null, missTxt = null;
            for (int i = 0; i < 5; i++)
            {
                CreateText($"Label_{labels[i]}", root.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-150, startY - i * 60), new Vector2(250, 40),
                    $"result.{labels[i].ToLower()}", 28, TextAlignmentOptions.Right);

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
                new Vector2(0.3f, 0.08f), new Vector2(300, 120), "result.retry", 48);
            GameObject btnBack = CreateButton("BackButton", root.transform,
                new Vector2(0.7f, 0.08f), new Vector2(300, 120), "result.back", 48);

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

        private static GameStarter CreateGameStarter(Transform canvasRoot, Transform menuParent, GameObject gamePanel)
        {
            // 菜单标题
            TextMeshProUGUI title = CreateText("GameTitle", menuParent,
                new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(900, 200),
                "menu.title", 120, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(0.7f, 0.85f, 1f);

            TextMeshProUGUI subtitle = CreateText("Subtitle", menuParent,
                new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f), Vector2.zero, new Vector2(600, 60),
                "menu.subtitle", 40, TextAlignmentOptions.Center);
            subtitle.color = new Color(1, 1, 1, 0.8f);

            TextMeshProUGUI hint = CreateText("Hint", menuParent,
                new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f), Vector2.zero, new Vector2(900, 160),
                "menu.hint", 32, TextAlignmentOptions.Center);
            hint.color = new Color(1, 1, 1, 0.7f);

            // 主菜单四按钮（依次：新游戏 / 选择存档 / 自选曲目 / 选项设置）
            GameObject newGameBtn = CreateButton("NewGameButton", menuParent,
                new Vector2(0.5f, 0.42f), new Vector2(500, 110), "menu.newGame", 48);
            GameObject saveSelectBtn = CreateButton("SaveSelectButton", menuParent,
                new Vector2(0.5f, 0.30f), new Vector2(500, 110), "menu.saveSelect", 48);
            GameObject songSelectBtn = CreateButton("SongSelectButton", menuParent,
                new Vector2(0.5f, 0.18f), new Vector2(500, 110), "menu.songSelect", 48);
            GameObject settingsBtn = CreateButton("SettingsButton", menuParent,
                new Vector2(0.5f, 0.06f), new Vector2(500, 110), "menu.settings", 48);

            // GameStarter 挂 Canvas 根（始终激活）：局中 MenuPanel 失活后，
            // Loading→倒计时与结算→菜单事件链仍有人接（修复：此前挂 MenuPanel 失活丢事件）。
            // 菜单 UI 仍建在 menuParent（MenuPanel）下，仅注入引用。
            GameStarter starter = canvasRoot.gameObject.AddComponent<GameStarter>();
            SetPrivateField(starter, "menuPanel", menuParent.gameObject);
            SetPrivateField(starter, "gamePanel", gamePanel);
            SetPrivateField(starter, "newGameButton", newGameBtn.GetComponent<Button>());
            SetPrivateField(starter, "saveSelectButton", saveSelectBtn.GetComponent<Button>());
            SetPrivateField(starter, "songSelectButton", songSelectBtn.GetComponent<Button>());
            SetPrivateField(starter, "settingsButton", settingsBtn.GetComponent<Button>());
            SetPrivateField(starter, "autoStartDemoOnAwake", false);
            return starter;
        }

        /// <summary>
        /// 选项设置页：语言直切（遍历枚举，当前置灰）/ 打开校准 / 下落速度（FallSpeedController 双组绑定）/
        /// 音效音量 ±0.1 / 按键特效占位开关。面板初始非激活；入口按钮（menu.settings）由
        /// CreateGameStarter 创建，GameStarter 接线打开本页。
        /// </summary>
        private static SettingsPanelController CreateSettingsPanel(RectTransform canvasRect, GameStarter starter, FallSpeedController fsc)
        {
            GameObject panel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            TextMeshProUGUI title = CreateText("SettingsTitle", panel.transform,
                new Vector2(0.5f, 0.90f), new Vector2(0.5f, 0.90f), Vector2.zero, new Vector2(700, 100),
                "settings.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 语言行：小标签 + 枚举按钮横排（当前语言置灰不可选）
            TextMeshProUGUI langLabel = CreateText("SettingsLangLabel", panel.transform,
                new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.80f), Vector2.zero, new Vector2(600, 60),
                "settings.language", 40, TextAlignmentOptions.Center);
            langLabel.color = new Color(1, 1, 1, 0.8f);

            string[] names = System.Enum.GetNames(typeof(Language));
            System.Collections.Generic.List<Button> langButtons =
                new System.Collections.Generic.List<Button>();
            const float langGap = 0.24f;
            for (int i = 0; i < names.Length; i++)
            {
                float x = 0.5f + (i - (names.Length - 1) * 0.5f) * langGap;
                GameObject b = CreateButton($"SettingsLangButton_{names[i]}", panel.transform,
                    new Vector2(x, 0.735f), new Vector2(200, 90), $"lang.{names[i]}", 38);
                langButtons.Add(b.GetComponent<Button>());
            }

            // 校准行：打开校准按钮（校准面板本体在 CreateCalibrationPanel，后建盖在本页上）
            GameObject calBtn = CreateButton("SettingsOpenCalibrationButton", panel.transform,
                new Vector2(0.5f, 0.625f), new Vector2(500, 90), "settings.openCalibration", 40);

            // 下落速度行：±按钮 + 动态数值（监听由 FallSpeedController 双组绑定）
            TextMeshProUGUI speedLabel = CreateText("SettingsFallSpeedLabel", panel.transform,
                new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f), Vector2.zero, new Vector2(600, 60),
                "settings.fallSpeed", 40, TextAlignmentOptions.Center);
            speedLabel.color = new Color(1, 1, 1, 0.8f);
            GameObject speedMinus = CreateButton("SettingsFallSpeedMinusButton", panel.transform,
                new Vector2(0.32f, 0.46f), new Vector2(160, 80), "−", 44);
            GameObject speedPlus = CreateButton("SettingsFallSpeedPlusButton", panel.transform,
                new Vector2(0.68f, 0.46f), new Vector2(160, 80), "＋", 44);
            TextMeshProUGUI speedText = CreateText("SettingsFallSpeedText", panel.transform,
                new Vector2(0.5f, 0.46f), new Vector2(0.5f, 0.46f), Vector2.zero, new Vector2(220, 80),
                "", 40, TextAlignmentOptions.Center);

            // 音效音量行：±按钮 + 百分比动态文本（监听由 SettingsPanelController 接）
            TextMeshProUGUI sfxLabel = CreateText("SettingsSfxLabel", panel.transform,
                new Vector2(0.5f, 0.37f), new Vector2(0.5f, 0.37f), Vector2.zero, new Vector2(600, 60),
                "settings.sfxVolume", 40, TextAlignmentOptions.Center);
            sfxLabel.color = new Color(1, 1, 1, 0.8f);
            GameObject sfxMinus = CreateButton("SettingsSfxMinusButton", panel.transform,
                new Vector2(0.32f, 0.30f), new Vector2(160, 80), "−", 44);
            GameObject sfxPlus = CreateButton("SettingsSfxPlusButton", panel.transform,
                new Vector2(0.68f, 0.30f), new Vector2(160, 80), "＋", 44);
            TextMeshProUGUI sfxText = CreateText("SettingsSfxVolumeText", panel.transform,
                new Vector2(0.5f, 0.30f), new Vector2(0.5f, 0.30f), Vector2.zero, new Vector2(220, 80),
                "", 40, TextAlignmentOptions.Center);

            // 按键特效行：占位开关（按钮 label 动态显示 开/关，美术资源到位后扩展）
            TextMeshProUGUI effectLabel = CreateText("SettingsHitEffectLabel", panel.transform,
                new Vector2(0.5f, 0.21f), new Vector2(0.5f, 0.21f), Vector2.zero, new Vector2(600, 60),
                "settings.hitEffect", 40, TextAlignmentOptions.Center);
            effectLabel.color = new Color(1, 1, 1, 0.8f);
            GameObject effectBtn = CreateButton("SettingsHitEffectButton", panel.transform,
                new Vector2(0.5f, 0.135f), new Vector2(400, 80), "", 38);
            TextMeshProUGUI effectLabelTxt =
                effectBtn.transform.Find("Label").GetComponent<TextMeshProUGUI>();

            GameObject closeBtn = CreateButton("SettingsCloseButton", panel.transform,
                new Vector2(0.5f, 0.045f), new Vector2(300, 70), "settings.close", 34);

            SettingsPanelController controller = panel.AddComponent<SettingsPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "languageButtons", langButtons);
            SetPrivateField(controller, "openCalibrationButton", calBtn.GetComponent<Button>());
            SetPrivateField(controller, "sfxMinusButton", sfxMinus.GetComponent<Button>());
            SetPrivateField(controller, "sfxPlusButton", sfxPlus.GetComponent<Button>());
            SetPrivateField(controller, "sfxVolumeText", sfxText);
            SetPrivateField(controller, "hitEffectButton", effectBtn.GetComponent<Button>());
            SetPrivateField(controller, "hitEffectLabel", effectLabelTxt);
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 设置页速度按钮组交给 FallSpeedController（Canvas 根，始终激活）双组绑定
            SetPrivateField(fsc, "settingsMinusButton", speedMinus.GetComponent<Button>());
            SetPrivateField(fsc, "settingsPlusButton", speedPlus.GetComponent<Button>());
            SetPrivateField(fsc, "settingsSpeedText", speedText);

            // 入口由 GameStarter（始终激活）接线
            SetPrivateField(starter, "settingsPanelController", controller);
            return controller;
        }

        /// <summary>
        /// 正式存档选择面板：档案列表（ScrollRect 卷帘）+ 新建档输入框 +
        /// 选中档后出现"进入游戏"与角落"天赋"按钮（天赋按钮接线 TalentPanel）。
        /// 入口按钮（menu.saveSelect）由 CreateGameStarter 创建，GameStarter 接线 session.EnterSaveSelect。
        /// </summary>
        private static SaveSelectPanelController CreateSaveSelectPanel(RectTransform canvasRect)
        {
            GameObject panel = new GameObject("SaveSelectPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            TextMeshProUGUI title = CreateText("SaveSelectTitle", panel.transform,
                new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), Vector2.zero, new Vector2(700, 100),
                "saveSelect.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 新建档：输入框 + 按钮
            GameObject fieldGO = new GameObject("SaveNameField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            fieldGO.transform.SetParent(panel.transform, false);
            RectTransform frt = (RectTransform)fieldGO.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.26f, 0.84f);
            frt.pivot = Vector2.zero;
            frt.sizeDelta = new Vector2(560, 80);
            fieldGO.GetComponent<Image>().color = new Color(0.075f, 0.095f, 0.14f, 1f);
            TMP_InputField input = fieldGO.GetComponent<TMP_InputField>();
            TextMeshProUGUI fieldText = CreateText("SaveNameFieldText", fieldGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540, 70),
                "", 28, TextAlignmentOptions.Left);
            TextMeshProUGUI fieldHint = CreateText("SaveNameFieldHint", fieldGO.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540, 70),
                "saveSelect.name", 24, TextAlignmentOptions.Left);
            fieldHint.color = new Color(0.5f, 0.57f, 0.66f);
            input.textViewport = frt;
            input.textComponent = fieldText;
            input.placeholder = fieldHint;
            input.characterLimit = 32;

            GameObject createBtn = CreateButton("SaveCreateButton", panel.transform,
                new Vector2(0.78f, 0.84f), new Vector2(300, 80), "saveSelect.create", 34);

            // 档案列表：ScrollRect 卷帘（同选歌面板模式）
            GameObject scrollGO = new GameObject("SaveScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel.transform, false);
            RectTransform srt = (RectTransform)scrollGO.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.47f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(920, 980);
            Image simg = scrollGO.GetComponent<Image>();
            simg.color = new Color(0, 0, 0, 0.35f);
            simg.raycastTarget = true;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGO.transform, false);
            RectTransform vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            Image vimg = viewport.GetComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0f);
            vimg.raycastTarget = true;

            GameObject content = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(880, 0);
            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 16;
            vlg.padding = new RectOffset(0, 0, 16, 16);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = vrt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;

            // 条目模板（非激活克隆源；label 动态文本）
            GameObject template = CreateButton("SaveEntryTemplate", panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(860, 100), "", 30);
            template.SetActive(false);

            // 选中档后出现：进入游戏 + 角落天赋
            GameObject enterBtn = CreateButton("SaveEnterGameButton", panel.transform,
                new Vector2(0.5f, 0.055f), new Vector2(500, 90), "saveSelect.enterGame", 40);
            enterBtn.SetActive(false);
            GameObject talentBtn = CreateButton("SaveTalentButton", panel.transform,
                new Vector2(0.93f, 0.93f), new Vector2(170, 80), "saveSelect.talents", 32);
            talentBtn.SetActive(false);

            GameObject closeBtn = CreateButton("SaveSelectCloseButton", panel.transform,
                new Vector2(0.86f, 0.055f), new Vector2(200, 90), "saveSelect.close", 34);

            SaveSelectPanelController controller = panel.AddComponent<SaveSelectPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "content", crt);
            SetPrivateField(controller, "entryTemplate", template);
            SetPrivateField(controller, "nameInput", input);
            SetPrivateField(controller, "createButton", createBtn.GetComponent<Button>());
            SetPrivateField(controller, "enterGameButton", enterBtn.GetComponent<Button>());
            SetPrivateField(controller, "talentButton", talentBtn.GetComponent<Button>());
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);
            return controller;
        }

        /// <summary>
        /// 天赋面板：SceneBuilder 建骨架（全屏背景 + 标题 + 右上角 X + 空内容容器），
        /// 树/详情/解锁确认由 TalentPanelController 运行时渲染（平移自旧 PortfolioCanvas 天赋页）。
        /// 后建于 SaveSelectPanel（渲染盖在其上）；反向注入控制器给存档选择面板与 GameStarter。
        /// 自带 Canvas 排序 251：地图页在独立 PortfolioCanvas（排序 250）上，天赋面板必须盖过它。
        /// </summary>
        private static void CreateTalentPanel(RectTransform canvasRect, SaveSelectPanelController saveSelectController, GameStarter starter)
        {
            GameObject panel = new GameObject("TalentPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            // 嵌套 Canvas（继承父 Canvas 的参考分辨率缩放）+ 独立排序，盖在 PortfolioCanvas(250) 之上；
            // 嵌套 Canvas 需要自己的 GraphicRaycaster 才能接收本子树射线
            Canvas panelCanvas = panel.AddComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 251;
            panel.AddComponent<GraphicRaycaster>();

            TextMeshProUGUI title = CreateText("TalentTitle", panel.transform,
                new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), Vector2.zero, new Vector2(700, 100),
                "talentPanel.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 右上角 X 关闭
            GameObject closeBtn = CreateButton("TalentCloseButton", panel.transform,
                new Vector2(0.93f, 0.93f), new Vector2(170, 80), "talentPanel.close", 32);

            // 空内容容器（控制器运行时重建其子树）
            GameObject content = new GameObject("TalentContent", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            TalentPanelController controller = panel.AddComponent<TalentPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "contentRoot", crt);
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 反向注入给存档选择面板（"天赋"按钮打开 + ESC 层叠判断）
            if (saveSelectController != null)
                SetPrivateField(saveSelectController, "talentPanel", controller);
            // 注入 GameStarter：运行时转交给 PortfolioPanelController（地图页"天赋"入口）
            SetPrivateField(starter, "talentPanelController", controller);
        }

        /// <summary>
        /// 节拍校准：校准面板本体（开始测试/应用推荐/±5ms/关闭）。
        /// 主菜单入口按钮已移除；由选项设置页的"打开校准"按钮进入。
        /// 反向注入控制器/面板引用给设置页（ESC 层叠判断与打开按钮）。
        /// </summary>
        private static void CreateCalibrationPanel(RectTransform canvasRect, Transform menuParent, GameStarter starter, SettingsPanelController settingsController)
        {
            GameObject panel = new GameObject("CalibrationPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            TextMeshProUGUI title = CreateText("CalTitle", panel.transform,
                new Vector2(0.5f, 0.86f), new Vector2(0.5f, 0.86f), Vector2.zero, new Vector2(700, 100),
                "cal.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            TextMeshProUGUI offsetTxt = CreateText("CalOffsetText", panel.transform,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(800, 90),
                "", 40, TextAlignmentOptions.Center);

            // 状态文本为运行时动态内容（控制器写入，含进度/结果），key 传空串保持"动态"，
            // 避免语言切换时 LocalizedText 把运行时状态覆盖回提示文案
            TextMeshProUGUI statusTxt = CreateText("CalStatusText", panel.transform,
                new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(900, 120),
                "", 32, TextAlignmentOptions.Center);

            // 下落式校准轨道（与核心游玩视觉一致）：轨道条 + 判定线
            // 判定线本地 y = (0.32-0.5)*1920 ≈ -346，音符从 -346+576=+230 处出生
            GameObject laneGO = new GameObject("CalLane", typeof(RectTransform), typeof(Image));
            laneGO.transform.SetParent(panel.transform, false);
            RectTransform laneRT = (RectTransform)laneGO.transform;
            laneRT.anchorMin = laneRT.anchorMax = new Vector2(0.5f, 0.47f);
            laneRT.sizeDelta = new Vector2(180, 576);
            laneGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
            laneGO.GetComponent<Image>().raycastTarget = false;

            GameObject judgeGO = new GameObject("CalJudgeLine", typeof(RectTransform), typeof(Image));
            judgeGO.transform.SetParent(panel.transform, false);
            RectTransform judgeRT = (RectTransform)judgeGO.transform;
            judgeRT.anchorMin = judgeRT.anchorMax = new Vector2(0.5f, 0.32f);
            judgeRT.sizeDelta = new Vector2(240, 6);
            judgeGO.GetComponent<Image>().color = new Color(1f, 0.45f, 0.45f, 0.9f);
            judgeGO.GetComponent<Image>().raycastTarget = false;

            // 开始/停止观察：label 为动态文本（key 空串），由 CalibrationController
            // 在观察/空闲状态切换文案（开始观察 ↔ 停止观察）
            GameObject startBtn = CreateButton("CalStartButton", panel.transform,
                new Vector2(0.5f, 0.22f), new Vector2(400, 100), "", 40);
            TextMeshProUGUI startBtnLabel =
                startBtn.transform.Find("Label").GetComponent<TextMeshProUGUI>();

            // ±5ms 是观察式校准的主操作，做大一点
            GameObject minusBtn = CreateButton("CalMinusButton", panel.transform,
                new Vector2(0.32f, 0.12f), new Vector2(300, 100), "cal.minus5", 40);
            GameObject plusBtn = CreateButton("CalPlusButton", panel.transform,
                new Vector2(0.68f, 0.12f), new Vector2(300, 100), "cal.plus5", 40);
            GameObject closeBtn = CreateButton("CalCloseButton", panel.transform,
                new Vector2(0.5f, 0.035f), new Vector2(300, 70), "cal.close", 34);

            CalibrationController cal = panel.AddComponent<CalibrationController>();
            SetPrivateField(cal, "panelRoot", panel);
            SetPrivateField(cal, "statusText", statusTxt);
            SetPrivateField(cal, "offsetText", offsetTxt);
            SetPrivateField(cal, "judgeLine", judgeRT);
            SetPrivateField(cal, "startStopButton", startBtn.GetComponent<Button>());
            SetPrivateField(cal, "startStopLabel", startBtnLabel);
            SetPrivateField(cal, "plusButton", plusBtn.GetComponent<Button>());
            SetPrivateField(cal, "minusButton", minusBtn.GetComponent<Button>());
            SetPrivateField(cal, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 反向注入给设置页（设置页先建、校准面板后建盖在其上）
            if (settingsController != null)
            {
                SetPrivateField(settingsController, "calibrationController", cal);
                SetPrivateField(settingsController, "calibrationPanel", panel);
            }
        }

        /// <summary>
        /// 语言选择面板：面板内遍历 Language 枚举生成按钮（原生名称，key=lang.{枚举名}），
        /// 当前语言置灰不可选。主菜单入口按钮已移除；设置页改为页内直切（阶段3接线），
        /// 此面板保留但不再接入入口。
        /// </summary>
        private static void CreateLanguagePanel(RectTransform canvasRect, Transform menuParent, GameStarter starter)
        {
            GameObject panel = new GameObject("LanguagePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);

            TextMeshProUGUI title = CreateText("LangTitle", panel.transform,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(600, 100),
                "lang.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 遍历语言枚举生成按钮（当前最多 4 个排布合理，更多语言时调整间距）
            string[] names = System.Enum.GetNames(typeof(Language));
            System.Collections.Generic.List<Button> langButtons =
                new System.Collections.Generic.List<Button>();
            float step = 0.14f;
            float startY = 0.56f;
            for (int i = 0; i < names.Length; i++)
            {
                GameObject b = CreateButton($"LangButton_{names[i]}", panel.transform,
                    new Vector2(0.5f, startY - i * step), new Vector2(500, 110),
                    $"lang.{names[i]}", 44);
                langButtons.Add(b.GetComponent<Button>());
            }

            GameObject closeBtn = CreateButton("LangCloseButton", panel.transform,
                new Vector2(0.5f, 0.08f), new Vector2(300, 80), "lang.close", 34);

            LanguagePanelController lpc = panel.AddComponent<LanguagePanelController>();
            SetPrivateField(lpc, "panelRoot", panel);
            SetPrivateField(lpc, "languageButtons", langButtons);
            SetPrivateField(lpc, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);
        }

        /// <summary>
        /// 选歌界面（卷帘形态）：主菜单入口按钮 + 全屏面板（初始非激活）。
        /// 列表为 ScrollRect（Viewport+RectMask2D 遮罩裁剪、Content+VerticalLayoutGroup
        /// +ContentSizeFitter 自动堆叠），条目由控制器克隆模板生成，点击直接开谱。
        /// </summary>
        private static void CreateSongSelectPanel(RectTransform canvasRect, Transform menuParent, GameStarter starter)
        {
            // 主菜单入口按钮已由 CreateGameStarter 统一创建（四按钮纵排），此处只建面板本体
            GameObject panel = new GameObject("SongSelectPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform prt = (RectTransform)panel.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            Image pbg = panel.GetComponent<Image>();
            pbg.color = new Color(0.05f, 0.06f, 0.10f, 1f);
            pbg.raycastTarget = true;

            TextMeshProUGUI title = CreateText("SongSelectTitle", panel.transform,
                new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.94f), Vector2.zero, new Vector2(700, 90),
                "songSelect.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 卷帘滚动区：ScrollRect + 透明遮罩 Viewport + 顶部对齐 Content
            GameObject scrollGO = new GameObject("SongScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel.transform, false);
            RectTransform srt = (RectTransform)scrollGO.transform;
            srt.anchorMin = new Vector2(0.5f, 0.5f);
            srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0, -40);
            srt.sizeDelta = new Vector2(700, 1200);
            Image simg = scrollGO.GetComponent<Image>();
            simg.color = new Color(0, 0, 0, 0.35f);
            simg.raycastTarget = true;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGO.transform, false);
            RectTransform vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            Image vimg = viewport.GetComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0f); // 透明遮罩：不挡视觉，仅接收拖动
            vimg.raycastTarget = true;

            GameObject content = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(660, 0);
            VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 24;
            vlg.padding = new RectOffset(0, 0, 24, 24);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false; // 条目高度由自身 sizeDelta 决定（Image 无 preferredHeight）
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.content = crt;
            sr.viewport = vrt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;

            // 条目模板（非激活克隆源；label 为动态文本，key 空串）
            GameObject template = CreateButton("SongEntryTemplate", panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(600, 110), "", 34);
            template.SetActive(false);

            GameObject closeBtn = CreateButton("SongSelectCloseButton", panel.transform,
                new Vector2(0.5f, 0.04f), new Vector2(300, 80), "lang.close", 34);

            SongSelectPanelController controller = panel.AddComponent<SongSelectPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "content", crt);
            SetPrivateField(controller, "entryTemplate", template);
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            SetPrivateField(controller, "gameStarter", starter);
            panel.SetActive(false);

            // 控制器交给 GameStarter（始终激活，Awake 时接线；入口按钮由 CreateGameStarter 统一创建）
            SetPrivateField(starter, "songSelectPanelController", controller);
        }

        /// <summary>
        /// Roguelite 地图面板：MapPanel 根（始终激活、无 Graphic，仅挂控制器）→
        /// MapContent（非激活：不透明背景+标题+节点容器+按钮模板）→
        /// MapInfoPopup（非激活说明弹窗）。不透明背景在 MapContent 内，
        /// 随内容隐藏，否则会盖住主菜单。
        /// </summary>
        private static void CreateMapPanel(RectTransform canvasRect)
        {
            GameObject mapPanel = CreatePanel("MapPanel", canvasRect);
            MapPanelController mpc = mapPanel.AddComponent<MapPanelController>();

            GameObject content = CreatePanel("MapContent", mapPanel.transform);
            Image bg = content.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.10f, 1f);
            bg.raycastTarget = false;
            content.SetActive(false);

            TextMeshProUGUI title = CreateText("MapTitle", content.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(700, 100),
                "map.title", 64, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            // 节点容器（按钮由 MapPanelController 克隆模板手动堆叠定位，不用 LayoutGroup）
            GameObject nodesContainer = new GameObject("NodesContainer", typeof(RectTransform));
            nodesContainer.transform.SetParent(content.transform, false);
            RectTransform ncRT = (RectTransform)nodesContainer.transform;
            ncRT.anchorMin = new Vector2(0.5f, 0.5f);
            ncRT.anchorMax = new Vector2(0.5f, 0.5f);
            ncRT.pivot = new Vector2(0.5f, 0.5f);
            ncRT.anchoredPosition = Vector2.zero;
            ncRT.sizeDelta = new Vector2(800, 1500);

            // 节点按钮模板（非激活，克隆源；label 为动态文本，key 空串）
            GameObject template = CreateButton("NodeButtonTemplate", content.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 90), "", 30);
            template.SetActive(false);

            // 说明弹窗（非激活；占位格/终点点击后显示）
            GameObject popup = CreatePanel("MapInfoPopup", mapPanel.transform);
            Image popupImg = popup.AddComponent<Image>();
            popupImg.color = new Color(0, 0, 0, 0.85f);
            popupImg.raycastTarget = true; // 弹窗期间拦截下层节点按钮

            TextMeshProUGUI popupTitle = CreateText("PopupTitle", popup.transform,
                new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f), Vector2.zero, new Vector2(700, 100),
                "", 52, TextAlignmentOptions.Center);
            popupTitle.fontStyle = FontStyles.Bold;

            TextMeshProUGUI popupDesc = CreateText("PopupDesc", popup.transform,
                new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(900, 300),
                "", 36, TextAlignmentOptions.Center);

            GameObject closeBtn = CreateButton("PopupCloseButton", popup.transform,
                new Vector2(0.5f, 0.3f), new Vector2(300, 90), "lang.close", 34);
            popup.SetActive(false);

            SetPrivateField(mpc, "mapContent", content);
            SetPrivateField(mpc, "nodesContainer", ncRT);
            SetPrivateField(mpc, "nodeButtonTemplate", template);
            SetPrivateField(mpc, "mapInfoPopup", popup);
            SetPrivateField(mpc, "popupTitleText", popupTitle);
            SetPrivateField(mpc, "popupDescText", popupDesc);
            SetPrivateField(mpc, "popupCloseButton", closeBtn.GetComponent<Button>());
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
                "pause.paused", 80, TextAlignmentOptions.Center);
            pausedText.fontStyle = FontStyles.Bold;
            pausedText.color = Color.white;

            // Resume 按钮
            GameObject resumeBtn = CreateButton("ResumeButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "pause.resume", 48);
            RectTransform rbt = (RectTransform)resumeBtn.transform;
            rbt.anchoredPosition = new Vector2(0, 180);

            // Retry 按钮
            GameObject retryBtn = CreateButton("RetryButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "pause.retry", 48);
            RectTransform retbt = (RectTransform)retryBtn.transform;
            retbt.anchoredPosition = new Vector2(0, 0);

            // Exit 按钮
            GameObject exitBtn = CreateButton("ExitButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(400, 140), "pause.exit", 40);
            RectTransform ebt = (RectTransform)exitBtn.transform;
            ebt.anchoredPosition = new Vector2(0, -180);

            // 下落速度调节行（label 在上，控制行在下；数值文本为动态字面量，不受语言刷新覆盖）
            CreateText("FallSpeedLabel", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -275), new Vector2(400, 60),
                "fallSpeed.title", 34, TextAlignmentOptions.Center);

            GameObject speedMinus = CreateButton("FallSpeedMinusButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(110, 70), "-", 40);
            ((RectTransform)speedMinus.transform).anchoredPosition = new Vector2(-160, -350);

            CreateText("FallSpeedText", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -350), new Vector2(160, 70),
                "1.0x", 36, TextAlignmentOptions.Center); // 字面量：FallSpeedController 动态更新

            GameObject speedPlus = CreateButton("FallSpeedPlusButton", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(110, 70), "+", 40);
            ((RectTransform)speedPlus.transform).anchoredPosition = new Vector2(160, -350);

            // 只创建 UI 元素（PauseRoot + 按钮），不挂 PauseController
            // 真正的控制器在 HUDPanel 上
            return null;
        }

        // ================ Text 创建辅助 ================

        // 中文字体资产缓存（由 Create Chinese TMP Font 菜单生成，CreateText 统一使用）
        private static TMP_FontAsset cjkFontAsset;
        private static bool cjkFontLoaded;

        /// <summary>获取中文字体资产（不存在或图集损坏则返回 null，回退 TMP 默认字体）</summary>
        private static TMP_FontAsset GetCjkFontAsset()
        {
            if (!cjkFontLoaded)
            {
                cjkFontLoaded = true;
                cjkFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/CJK_Font SDF.asset");
                if (cjkFontAsset != null && !IsFontAssetUsable(cjkFontAsset))
                {
                    Debug.LogWarning("[SceneBuilder] 中文字体资产图集损坏，回退 TMP 默认字体（请重跑 Create Chinese TMP Font）");
                    cjkFontAsset = null;
                }
            }
            return cjkFontAsset;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent,
            Vector2 aMin, Vector2 aMax, Vector2 anchoredPos, Vector2 size,
            string textKey, int fontSize, TextAlignmentOptions align)
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
            // 初始文本按当前语言解析；动态文本（字面量作 key、不在语言表）原样返回
            txt.text = Loc.T(textKey);
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = Color.white;
            txt.enableWordWrapping = true;

            // 使用中文字体（若已生成），否则回退 TMP 默认字体（中文会显示方块）
            TMP_FontAsset cjk = GetCjkFontAsset();
            if (cjk != null) txt.font = cjk;

            // 本地化刷新组件：语言切换时自动更新（动态文本经 Loc.HasKey 判断不受影响）
            LocalizedText lt = go.AddComponent<LocalizedText>();
            lt.SetKey(textKey);
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
