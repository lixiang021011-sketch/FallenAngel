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
        const string OflFontPath = "Assets/Fonts/SourceHanSansCN-Regular.otf";
        const string CjkFontAssetPath = "Assets/Fonts/CJK_Font SDF.asset";

        [MenuItem("Tools/FallenAngel/Create Chinese TMP Font")]
        public static void CreateChineseTmpFont()
        {
            EnsureCjkFont(true);
        }

        /// <summary>
        /// 用工程内 OFL 思源黑体生成 TMP 动态图集。不拷贝 Windows 微软字体。
        /// confirm=false 时不弹窗（供重建场景 / batchmode）。
        /// </summary>
        public static bool EnsureCjkFont(bool confirm)
        {
            // TMP 默认 Liberation Sans 无中文。图集必须校验：空 atlasTextures 赋值即抛异常。
            // CreateAsset 只存壳文件——材质与图集纹理必须 AddObjectToAsset。
            if (!AssetDatabase.IsValidFolder("Assets/Fonts"))
                AssetDatabase.CreateFolder("Assets", "Fonts");

            if (!File.Exists(OflFontPath))
            {
                Debug.LogError("[SceneBuilder] 缺少 OFL 字体: " + OflFontPath);
                if (confirm)
                    EditorUtility.DisplayDialog("FallenAngel",
                        "缺少可商用字体。\n\n请将 SourceHanSansCN-Regular.otf（思源黑体 CN，SIL OFL）放到 Assets/Fonts。", "OK");
                return false;
            }

            AssetDatabase.ImportAsset(OflFontPath);
            Font oflFont = AssetDatabase.LoadAssetAtPath<Font>(OflFontPath);
            if (oflFont == null)
            {
                Debug.LogError("[SceneBuilder] OFL 字体导入失败: " + OflFontPath);
                if (confirm)
                    EditorUtility.DisplayDialog("FallenAngel",
                        "思源黑体导入失败，Unity 未能识别 Assets/Fonts/SourceHanSansCN-Regular.otf。", "OK");
                return false;
            }

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetPath);
            bool wrongSource = existing != null && existing.sourceFontFile != oflFont;
            if (existing != null && (!IsFontAssetUsable(existing) || wrongSource))
            {
                Debug.LogWarning(wrongSource
                    ? "[SceneBuilder] 中文字体资产来源不是思源黑体，删除重建"
                    : "[SceneBuilder] 检测到损坏的中文字体资产，删除重建");
                AssetDatabase.DeleteAsset(CjkFontAssetPath);
                existing = null;
            }

            TMP_FontAsset fa = existing;
            if (fa == null)
            {
                TMP_FontAsset candidate = TMP_FontAsset.CreateFontAsset(
                    oflFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                if (!IsFontAssetUsable(candidate))
                {
                    Debug.LogError("[SceneBuilder] 思源黑体生成图集失败");
                    Object.DestroyImmediate(candidate);
                    if (confirm)
                        EditorUtility.DisplayDialog("FallenAngel",
                            "思源黑体未能生成有效 TMP 图集。", "OK");
                    return false;
                }
                AssetDatabase.CreateAsset(candidate, CjkFontAssetPath);
                if (candidate.material != null)
                    AssetDatabase.AddObjectToAsset(candidate.material, candidate);
                if (candidate.atlasTextures != null)
                    foreach (Texture2D tex in candidate.atlasTextures)
                        if (tex != null) AssetDatabase.AddObjectToAsset(tex, candidate);
                AssetDatabase.SaveAssets();
                fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetPath);
                if (!IsFontAssetUsable(fa))
                {
                    Debug.LogError("[SceneBuilder] 思源黑体持久化后图集丢失");
                    AssetDatabase.DeleteAsset(CjkFontAssetPath);
                    if (confirm)
                        EditorUtility.DisplayDialog("FallenAngel",
                            "思源黑体图集写入磁盘后丢失，请重试 Create Chinese TMP Font。", "OK");
                    return false;
                }
                Debug.Log("[SceneBuilder] 中文字体资产生成成功: " + OflFontPath + " -> " + CjkFontAssetPath);
            }

            cjkFontLoaded = false;
            AssetDatabase.SaveAssets();
            if (confirm)
                EditorUtility.DisplayDialog("FallenAngel",
                    "中文字体已就绪（思源黑体 CN，SIL OFL）。\n\n请重新执行 Tools > FallenAngel > Build Default Game Scene 重建场景。", "OK");
            return true;
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

            if (!EnsureCjkFont(false))
            {
                if (confirm)
                    EditorUtility.DisplayDialog("FallenAngel",
                        "中文字体未就绪。请将 SourceHanSansCN-Regular.otf 放到 Assets/Fonts 后重试。", "OK");
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
            // 判定区 = **整屏纵向**（参照 Phigros：哪里按都能判到最近那条轨）。
            // 只改逻辑范围，不改任何画面；UI 上的触摸仍由 IsPointerOverUI 拦掉。
            SetPrivateField(im, "touchBottomRatio", 1f);
            JudgeManager jm = managers.AddComponent<JudgeManager>();
            ModifierManager modifierManager = managers.AddComponent<ModifierManager>();   // 局内 modifier（docs/architecture.md §9）

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
            CreateGameplayBackground(gamePanel.transform);
            CreateLaneAndNotesUI(gamePanel.transform, out NoteSpawner spawner);

            // 触点涟漪层：盖在 GamePanel 之上、MenuPanel 之下（层级顺序即渲染顺序）
            // 2026-09-21 作者定案（5 键拆墙方案 B + "只保留命中反馈"）：**不挂触点涟漪**——
            // 涟漪是"按下"的反馈，本方案里按下不再给视觉反馈，命中反馈仍由音符自身的命中环/爆点承担。
            GameObject rippleLayer = CreatePanel("HitFeedbackLayer", canvasRect);
            // 判定特效（Miss 轨道压暗）：需要五条轨道柱的 Image，按 SceneBuilder 自己创建的命名取
            HitEffectController hitFx = rippleLayer.AddComponent<HitEffectController>();
            SetPrivateField(hitFx, "laneColumns", CollectLaneColumns(gamePanel.transform));

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


            // 正式存档选择面板（选中档后出现"进入游戏"/"天赋"按钮；盖在设置页之上）
            // 节奏校准面板：入口在设置页；反向注入控制器/面板引用给设置页（ESC 层叠与打开按钮）
            CreateCalibrationPanel(canvasRect, canvasRect, starter, settingsController);
            // 下落速度预览面板（同上：入口在设置页，后建盖在设置页上）
            CreateFallSpeedPreviewPanel(canvasRect, settingsController, fsc);

            SaveSelectPanelController saveSelectController = CreateSaveSelectPanel(canvasRect);
            SetPrivateField(starter, "saveSelectPanelController", saveSelectController);

            // 天赋面板（后建，盖在存档选择面板上；反向注入给存档选择面板的"天赋"按钮与 ESC 层叠）
            CreateTalentPanel(canvasRect, saveSelectController, starter);

            // 装备背包面板（嵌套 Canvas 排序 252，盖过天赋面板/地图页；入口是地图页"装备 n/20"按钮）
            CreateEquipmentPanel(canvasRect, starter);

            // 商店弹窗（嵌套 Canvas 排序 253；进店自动弹出，地图页"商店"按钮重开）
            CreateShopPanel(canvasRect, starter);

            // 新游戏覆盖确认弹窗（最后建，盖在最上层；按钮接线在 GameStarter）
            CreateNewGameConfirmPanel(canvasRect, starter);

            // 语言选择面板（遍历 Language 枚举生成按钮，新增语言自动扩展）
            CreateLanguagePanel(canvasRect, menuPanel.transform, starter);

            // 选歌界面（卷帘滚动列表；入口按钮与面板注入 starter）
            CreateSongSelectPanel(canvasRect, menuPanel.transform, starter);

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
                "5. Play 后从主菜单「新游戏」或「选择存档」进入行程地图",
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

        /// <summary>演奏背景位图路径（第一版美术定稿的符号化色块背景）</summary>
        private const string GameplayBgPath = "Assets/Art/bg/bg_gameplay.png";

        /// <summary>取演奏层的五条轨道柱 Image（供命中/Miss 特效改色）。命名由本文件自己创建，非外部约定。</summary>
        private static Image[] CollectLaneColumns(Transform gamePanel)
        {
            var result = new Image[LaneLayout.MaxLaneCount];
            Transform lanesRoot = gamePanel.Find("LanesBG");
            if (lanesRoot == null) return result;
            for (int i = 0; i < result.Length; i++)
            {
                Transform t = lanesRoot.Find("Lane" + i + "_BG");
                result[i] = t != null ? t.GetComponent<Image>() : null;
            }
            return result;
        }

        /// <summary>
        /// 演奏背景位图。插在 LanesBG 之前创建，所以渲染在轨道与音符之下；
        /// 运行时 DeepSeaPresentation 会把程序化 Backdrop 设为第一个子节点，位图正好盖在它上面。
        /// 素材缺失时只告警、不影响出图（回退到程序化背景）。
        /// </summary>
        private static void CreateGameplayBackground(Transform parent)
        {
            TextureImporter importer = AssetImporter.GetAtPath(GameplayBgPath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GameplayBgPath);
            if (sprite == null)
            {
                Debug.LogWarning("[SceneBuilder] 演奏背景缺失，回退到程序化背景：" + GameplayBgPath);
                return;
            }

            GameObject go = new GameObject("GameplayBackground", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            img.preserveAspect = false;   // 素材就是 1080×1920 基准分辨率，直接铺满
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
            keyCRect.sizeDelta = new Vector2(900, PlayVisualSpec.KeyAreaHeight);

            // 判定线
            GameObject judgeLine = new GameObject("JudgeLine", typeof(RectTransform), typeof(Image));
            judgeLine.transform.SetParent(lanes.transform, false);
            RectTransform jlRect = (RectTransform)judgeLine.transform;
            jlRect.anchorMin = new Vector2(0.5f, 0f);
            jlRect.anchorMax = new Vector2(0.5f, 0f);
            jlRect.pivot = new Vector2(0.5f, 0.5f);
            // 视觉线必须与 NoteSpawner 的判定位置同源（曾差 160px：视觉 400 / 判定 560）
            jlRect.anchoredPosition = new Vector2(0, PlayVisualSpec.JudgeLineAnchoredY);
            // 宽度跟着轨组走：2026-09-18 轨道加宽后轨组 = 可用宽（1080 − 两侧留白）
            jlRect.sizeDelta = new Vector2(LaneLayout.GetUsableWidth(LaneLayout.MaxLaneCount),
                                           PlayVisualSpec.JudgeLineThickness);
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
            // 谱面隐身用 alpha，不用 SetActive（后者会打断对象池与正在跑的协程）
            CanvasGroup notesGroup = notesContainer.AddComponent<CanvasGroup>();

            // NoteSpawner
            spawner = notesContainer.AddComponent<NoteSpawner>();
            SetPrivateField(spawner, "notesContainer", ncRect);
            SetPrivateField(spawner, "lanePositionsX", laneX);
            SetPrivateField(spawner, "judgeLineY", PlayVisualSpec.JudgeLineY);
            SetPrivateField(spawner, "spawnY", PlayVisualSpec.SpawnYFromCenter);

            // 5 个音轨条 + LaneKeyVisual（LanePanelController 按谱面键数启停/重定位）
            lanes.AddComponent<LanePanelController>();
            // 局内可见性：判定线消失 / 谱面隐身（只改渲染，判定与位置照常）
            PlayVisibilityController visibility = lanes.AddComponent<PlayVisibilityController>();
            SetPrivateField(visibility, "judgeLine", jlImg);
            SetPrivateField(visibility, "notesGroup", notesGroup);
            for (int i = 0; i < LaneLayout.MaxLaneCount; i++)
            {
                // 音轨背景条
                // 透视轨道条（上窄下宽）：宽度曲线与音符同源（PlayPerspective），保证严格对齐
                GameObject lane = new GameObject($"Lane{i}_BG", typeof(RectTransform), typeof(PerspectiveLaneGraphic));
                lane.transform.SetParent(lanes.transform, false);
                RectTransform lRT = (RectTransform)lane.transform;
                lRT.anchorMin = new Vector2(0.5f, 0f);
                lRT.anchorMax = new Vector2(0.5f, 1f);
                lRT.pivot = new Vector2(0.5f, 0.5f);
                lRT.anchoredPosition = new Vector2(laneX[i], 0);
                lRT.sizeDelta = new Vector2(laneWidth, 0);
                // 方案 B（全无墙）：轨道条不可见（alpha 0），只作 Miss 压暗特效的载体与键数重定位的锚点
                Color laneC = LaneColors.GetLaneColor(i); laneC.a = 0f;
                lane.GetComponent<Image>().color = laneC;
                lane.GetComponent<Image>().raycastTarget = false;

                // 按键区容器列（同样不可见：拆墙后下半屏不再画列）
                GameObject keyArea = new GameObject($"Lane{i}_Key", typeof(RectTransform), typeof(Image));
                keyArea.transform.SetParent(keyContainer.transform, false);
                RectTransform kRT = (RectTransform)keyArea.transform;
                kRT.anchorMin = new Vector2(0.5f, 0f);
                kRT.anchorMax = new Vector2(0.5f, 1f);
                kRT.pivot = new Vector2(0.5f, 0.5f);
                kRT.anchoredPosition = new Vector2(laneX[i], 0);
                kRT.sizeDelta = new Vector2(laneWidth, 0);
                Image keyImg = keyArea.GetComponent<Image>();
                Color c = LaneColors.GetLaneColor(i); c.a = 0f;
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
                Color kic = LaneColors.GetLaneColor(i); kic.a = 0f;   // 按键图标也不画（瞄准靠音符本身）
                kiImg.color = kic;
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

                // 2026-09-21 作者定案：取消按压反馈 → 不再挂 LaneKeyVisual（按键变亮/缩放/判定线闪光都去掉）
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

            // Fever 整屏色层（C4）：放在 HUD 的第一个子节点 → 渲染在所有 HUD 文本之下、演奏画面之上。
            // 上游是粒子特效包（LiveConfig.FeverBundleName），我们用程序化色层等价（见 UI/FeverView）。
            GameObject feverOverlay = new GameObject("FeverOverlay", typeof(RectTransform), typeof(Image));
            feverOverlay.transform.SetParent(hudGO.transform, false);
            RectTransform feverRT = (RectTransform)feverOverlay.transform;
            feverRT.anchorMin = Vector2.zero;
            feverRT.anchorMax = Vector2.one;
            feverRT.offsetMin = Vector2.zero;
            feverRT.offsetMax = Vector2.zero;
            Image feverImg = feverOverlay.GetComponent<Image>();
            feverImg.color = new Color(1f, 0.45f, 0.75f, 0f);
            feverImg.raycastTarget = false;
            feverImg.enabled = false;

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
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 165), new Vector2(400, 60),
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

            // 判定文字 + 描述（前端对照 OpenSekai：JudgmentView / JudgmentDescriptionView 的动画与门控照抄）
            JudgmentTextView judgmentText = hudGO.AddComponent<JudgmentTextView>();
            SetPrivateField(judgmentText, "resultText", judge);
            SetPrivateField(judgmentText, "descriptionText", judgeBias);
            SetPrivateField(judgmentText, "showDescription", true);

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

            // 生命槽（B4）：双层槽 = 残影 + 实槽（照上游 LifeView 的 lifeDamage / lifeDefault 两个 gauge）
            GameObject lifeBG = new GameObject("LifeBarBG", typeof(RectTransform), typeof(Image));
            lifeBG.transform.SetParent(hudGO.transform, false);
            RectTransform lifeBGRT = (RectTransform)lifeBG.transform;
            lifeBGRT.anchorMin = lifeBGRT.anchorMax = new Vector2(0f, 1f);
            lifeBGRT.pivot = new Vector2(0f, 1f);
            lifeBGRT.anchoredPosition = new Vector2(40, -112);
            lifeBGRT.sizeDelta = new Vector2(420, 30);
            Image lifeBGImg = lifeBG.GetComponent<Image>();
            lifeBGImg.color = new Color(0f, 0f, 0f, 0.55f);
            lifeBGImg.raycastTarget = false;

            GameObject ghostGO = new GameObject("LifeBarGhost", typeof(RectTransform), typeof(Image));
            ghostGO.transform.SetParent(lifeBG.transform, false);
            RectTransform ghostRT = (RectTransform)ghostGO.transform;
            ghostRT.anchorMin = Vector2.zero;
            ghostRT.anchorMax = Vector2.one;
            ghostRT.offsetMin = new Vector2(2, 2);
            ghostRT.offsetMax = new Vector2(-2, -2);
            Image ghostImg = ghostGO.GetComponent<Image>();
            ghostImg.color = new Color(1f, 0.35f, 0.35f, 0.85f);   // 残影：红
            ghostImg.type = Image.Type.Filled;
            ghostImg.fillMethod = Image.FillMethod.Horizontal;
            ghostImg.fillAmount = 1f;
            ghostImg.raycastTarget = false;

            GameObject lifeFillGO = new GameObject("LifeBarFill", typeof(RectTransform), typeof(Image));
            lifeFillGO.transform.SetParent(lifeBG.transform, false);
            RectTransform lifeFillRT = (RectTransform)lifeFillGO.transform;
            lifeFillRT.anchorMin = Vector2.zero;
            lifeFillRT.anchorMax = Vector2.one;
            lifeFillRT.offsetMin = new Vector2(2, 2);
            lifeFillRT.offsetMax = new Vector2(-2, -2);
            Image lifeFillImg = lifeFillGO.GetComponent<Image>();
            lifeFillImg.color = new Color(0.45f, 1f, 0.65f);
            lifeFillImg.type = Image.Type.Filled;
            lifeFillImg.fillMethod = Image.FillMethod.Horizontal;
            lifeFillImg.fillAmount = 1f;
            lifeFillImg.raycastTarget = false;

            TextMeshProUGUI lifeText = CreateText("LifeText", hudGO.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(474, -104), new Vector2(140, 40),
                "1000", 30, TextAlignmentOptions.Left);
            lifeText.color = new Color(0.45f, 1f, 0.65f);
            lifeText.fontStyle = FontStyles.Bold;

            // Fever 字样 + 蓄力条（C4）：Begin 闪烁、Active 常亮（逻辑在 UI/FeverView）
            TextMeshProUGUI feverLabel = CreateText("FeverLabel", hudGO.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -74), new Vector2(360, 54),
                "FEVER", 44, TextAlignmentOptions.Center);
            feverLabel.color = new Color(1f, 0.55f, 0.8f);
            feverLabel.fontStyle = FontStyles.Bold;
            feverLabel.enabled = false;

            GameObject feverChargeBG = new GameObject("FeverChargeBG", typeof(RectTransform), typeof(Image));
            feverChargeBG.transform.SetParent(hudGO.transform, false);
            RectTransform chargeBGRT = (RectTransform)feverChargeBG.transform;
            chargeBGRT.anchorMin = chargeBGRT.anchorMax = new Vector2(0.5f, 1f);
            chargeBGRT.pivot = new Vector2(0.5f, 1f);
            chargeBGRT.anchoredPosition = new Vector2(0, -120);
            chargeBGRT.sizeDelta = new Vector2(300, 12);
            Image chargeBGImg = feverChargeBG.GetComponent<Image>();
            chargeBGImg.color = new Color(0f, 0f, 0f, 0.5f);
            chargeBGImg.raycastTarget = false;

            GameObject feverChargeFill = new GameObject("FeverChargeFill", typeof(RectTransform), typeof(Image));
            feverChargeFill.transform.SetParent(feverChargeBG.transform, false);
            RectTransform chargeFillRT = (RectTransform)feverChargeFill.transform;
            chargeFillRT.anchorMin = Vector2.zero;
            chargeFillRT.anchorMax = Vector2.one;
            chargeFillRT.offsetMin = new Vector2(1, 1);
            chargeFillRT.offsetMax = new Vector2(-1, -1);
            Image chargeFillImg = feverChargeFill.GetComponent<Image>();
            chargeFillImg.color = new Color(1f, 0.55f, 0.8f);
            chargeFillImg.type = Image.Type.Filled;
            chargeFillImg.fillMethod = Image.FillMethod.Horizontal;
            chargeFillImg.fillAmount = 0f;
            chargeFillImg.raycastTarget = false;

            // Cut-in 卡片（E6/E7）：初始停在屏幕右侧之外，CutinView 负责滑入/停留/淡出
            GameObject cutinCard = new GameObject("CutinCard", typeof(RectTransform), typeof(Image),
                typeof(CanvasGroup));
            cutinCard.transform.SetParent(hudGO.transform, false);
            RectTransform cutinRT = (RectTransform)cutinCard.transform;
            cutinRT.anchorMin = cutinRT.anchorMax = new Vector2(0.5f, 0.62f);
            cutinRT.pivot = new Vector2(0.5f, 0.5f);
            cutinRT.sizeDelta = new Vector2(820, 170);
            cutinRT.anchoredPosition = new Vector2(980f, 0f);      // 屏外（右侧）
            Image cutinBg = cutinCard.GetComponent<Image>();
            cutinBg.color = new Color(0.05f, 0.10f, 0.13f, 0.92f);
            cutinBg.raycastTarget = false;

            GameObject cutinAccent = new GameObject("CutinAccent", typeof(RectTransform), typeof(Image));
            cutinAccent.transform.SetParent(cutinCard.transform, false);
            RectTransform accentRT = (RectTransform)cutinAccent.transform;
            accentRT.anchorMin = new Vector2(0f, 0f);
            accentRT.anchorMax = new Vector2(0f, 1f);
            accentRT.pivot = new Vector2(0f, 0.5f);
            accentRT.sizeDelta = new Vector2(10, 0);
            accentRT.anchoredPosition = Vector2.zero;
            Image accentImg = cutinAccent.GetComponent<Image>();
            accentImg.color = new Color(0.44f, 0.88f, 0.84f, 0.95f);
            accentImg.raycastTarget = false;

            GameObject iconPlate = new GameObject("CutinIcon", typeof(RectTransform), typeof(Image));
            iconPlate.transform.SetParent(cutinCard.transform, false);
            RectTransform iconRT = (RectTransform)iconPlate.transform;
            iconRT.anchorMin = iconRT.anchorMax = new Vector2(0f, 0.5f);
            iconRT.pivot = new Vector2(0f, 0.5f);
            iconRT.sizeDelta = new Vector2(110, 110);
            iconRT.anchoredPosition = new Vector2(40, 0);
            Image iconImg = iconPlate.GetComponent<Image>();
            iconImg.color = new Color(0.44f, 0.88f, 0.84f, 0.85f);
            iconImg.raycastTarget = false;

            TextMeshProUGUI cutinIconText = CreateText("CutinIconText", iconPlate.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 110),
                "", 56, TextAlignmentOptions.Center);
            cutinIconText.color = new Color(0.05f, 0.10f, 0.13f);
            cutinIconText.fontStyle = FontStyles.Bold;

            TextMeshProUGUI cutinTitle = CreateText("CutinTitle", cutinCard.transform,
                new Vector2(0f, 0.62f), new Vector2(0f, 0.62f), new Vector2(180, 0), new Vector2(600, 60),
                "", 40, TextAlignmentOptions.Left);
            cutinTitle.color = Color.white;
            cutinTitle.fontStyle = FontStyles.Bold;

            TextMeshProUGUI cutinSub = CreateText("CutinSubtitle", cutinCard.transform,
                new Vector2(0f, 0.30f), new Vector2(0f, 0.30f), new Vector2(180, 0), new Vector2(600, 44),
                "", 26, TextAlignmentOptions.Left);
            cutinSub.color = new Color(1f, 1f, 1f, 0.7f);

            HUDController hudC = hudGO.AddComponent<HUDController>();
            SetPrivateField(hudC, "scoreText", score);
            SetPrivateField(hudC, "comboText", combo);
            SetPrivateField(hudC, "comboLabelText", comboLabel);
            SetPrivateField(hudC, "judgeResultText", judge);
            SetPrivateField(hudC, "judgeBiasText", judgeBias);
            SetPrivateField(hudC, "songTitleText", title);
            SetPrivateField(hudC, "progressFillImage", fillImg);
            SetPrivateField(hudC, "progressText", progressTxt);

            LifeView lifeView = hudGO.AddComponent<LifeView>();
            SetPrivateField(lifeView, "realFill", lifeFillImg);
            SetPrivateField(lifeView, "ghostFill", ghostImg);
            SetPrivateField(lifeView, "valueText", lifeText);

            FeverView feverView = hudGO.AddComponent<FeverView>();
            SetPrivateField(feverView, "overlay", feverImg);
            SetPrivateField(feverView, "chargeFill", chargeFillImg);
            SetPrivateField(feverView, "label", feverLabel);

            CutinView cutinView = hudGO.AddComponent<CutinView>();
            SetPrivateField(cutinView, "card", cutinRT);
            SetPrivateField(cutinView, "iconPlate", iconImg);
            SetPrivateField(cutinView, "iconText", cutinIconText);
            SetPrivateField(cutinView, "titleText", cutinTitle);
            SetPrivateField(cutinView, "subtitleText", cutinSub);

            // Combo 数字位 + AP 特效（B3）：7 个数字位 + 爆发层 + AP 呼吸光板
            // 布局常量全部取上游 ComboView（见 UI/ComboView.cs 的对照表）；这里只建对象，位置由运行时按公式放。
            combo.gameObject.SetActive(false);      // 旧的整体 TMP 文本退场，数字位改由 ComboView 画

            GameObject comboRoot = new GameObject("ComboRoot", typeof(RectTransform));
            comboRoot.transform.SetParent(hudGO.transform, false);
            RectTransform comboRootRT = (RectTransform)comboRoot.transform;
            comboRootRT.anchorMin = comboRootRT.anchorMax = new Vector2(0.5f, 0.5f);
            comboRootRT.pivot = new Vector2(0.5f, 0.5f);
            comboRootRT.sizeDelta = new Vector2(900, 420);
            comboRootRT.anchoredPosition = new Vector2(0, 250);

            GameObject comboNumberRoot = new GameObject("ComboDigits", typeof(RectTransform));
            comboNumberRoot.transform.SetParent(comboRoot.transform, false);
            RectTransform numberRT = (RectTransform)comboNumberRoot.transform;
            numberRT.anchorMin = numberRT.anchorMax = new Vector2(0.5f, 0.5f);
            numberRT.pivot = new Vector2(0.5f, 0.5f);
            numberRT.sizeDelta = new Vector2(900, 200);
            numberRT.anchoredPosition = Vector2.zero;      // 与旧的整块连击文本同一高度（画布中心 +250）

            const int comboDigits = 7;              // 上游也是 7 段数字 renderer
            const float comboDigitW = 96f;
            var comboDigitTexts = new TextMeshProUGUI[comboDigits];
            var comboDigitPlates = new Image[comboDigits];
            for (int i = 0; i < comboDigits; i++)
            {
                GameObject plateGO = new GameObject($"ComboDigit{i}", typeof(RectTransform), typeof(Image));
                plateGO.transform.SetParent(comboNumberRoot.transform, false);
                RectTransform plateRT = (RectTransform)plateGO.transform;
                plateRT.anchorMin = plateRT.anchorMax = new Vector2(0.5f, 0.5f);
                plateRT.pivot = new Vector2(0.5f, 0.5f);
                plateRT.sizeDelta = new Vector2(comboDigitW, comboDigitW);
                Image plateImg = plateGO.GetComponent<Image>();
                plateImg.color = new Color(0.12f, 0.24f, 0.26f);
                plateImg.raycastTarget = false;
                comboDigitPlates[i] = plateImg;

                TextMeshProUGUI digitText = CreateText($"ComboDigitText{i}", plateGO.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(comboDigitW, comboDigitW), "", 68, TextAlignmentOptions.Center);
                digitText.color = new Color(0.89f, 0.96f, 0.95f);
                digitText.fontStyle = FontStyles.Bold;
                comboDigitTexts[i] = digitText;
                plateGO.SetActive(false);
            }

            GameObject comboEffectRoot = new GameObject("ComboEffect", typeof(RectTransform));
            comboEffectRoot.transform.SetParent(comboRoot.transform, false);
            RectTransform effectRootRT = (RectTransform)comboEffectRoot.transform;
            effectRootRT.anchorMin = effectRootRT.anchorMax = new Vector2(0.5f, 0.5f);
            effectRootRT.pivot = new Vector2(0.5f, 0.5f);
            effectRootRT.sizeDelta = new Vector2(900, 200);
            effectRootRT.anchoredPosition = Vector2.zero;

            var comboEffectPlates = new RectTransform[comboDigits];
            for (int i = 0; i < comboDigits; i++)
            {
                GameObject ghost = new GameObject($"ComboEffectDigit{i}", typeof(RectTransform), typeof(Image));
                ghost.transform.SetParent(comboEffectRoot.transform, false);
                RectTransform comboGhostRT = (RectTransform)ghost.transform;
                comboGhostRT.anchorMin = comboGhostRT.anchorMax = new Vector2(0.5f, 0.5f);
                comboGhostRT.pivot = new Vector2(0.5f, 0.5f);
                comboGhostRT.sizeDelta = new Vector2(comboDigitW, comboDigitW);
                Image comboGhostImg = ghost.GetComponent<Image>();
                comboGhostImg.color = new Color(0.44f, 0.88f, 0.84f, 0f);   // 爆发层（alpha 由 ComboView 控制）
                comboGhostImg.raycastTarget = false;
                comboEffectPlates[i] = comboGhostRT;
                ghost.SetActive(false);
            }

            // AP 呼吸框：四条边一起呼吸（上游是 AP 专用 sprite 环；我们没美术资源，用程序化描边等价）
            GameObject apRootGO = new GameObject("ComboApFrame", typeof(RectTransform));
            apRootGO.transform.SetParent(comboRoot.transform, false);
            RectTransform apRootRT = (RectTransform)apRootGO.transform;
            apRootRT.anchorMin = apRootRT.anchorMax = new Vector2(0.5f, 0.5f);
            apRootRT.pivot = new Vector2(0.5f, 0.5f);
            apRootRT.sizeDelta = new Vector2(720, 210);
            apRootRT.anchoredPosition = Vector2.zero;

            var apParts = new Image[4];
            string[] apNames = { "Top", "Bottom", "Left", "Right" };
            for (int i = 0; i < apParts.Length; i++)
            {
                GameObject bar = new GameObject("ComboApFrame" + apNames[i], typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(apRootGO.transform, false);
                RectTransform barRT = (RectTransform)bar.transform;
                barRT.pivot = new Vector2(0.5f, 0.5f);
                switch (i)
                {
                    case 0:   // 上边
                        barRT.anchorMin = new Vector2(0f, 1f);
                        barRT.anchorMax = new Vector2(1f, 1f);
                        barRT.sizeDelta = new Vector2(0f, 10f);
                        barRT.anchoredPosition = new Vector2(0f, -5f);
                        break;
                    case 1:   // 下边
                        barRT.anchorMin = new Vector2(0f, 0f);
                        barRT.anchorMax = new Vector2(1f, 0f);
                        barRT.sizeDelta = new Vector2(0f, 10f);
                        barRT.anchoredPosition = new Vector2(0f, 5f);
                        break;
                    case 2:   // 左边
                        barRT.anchorMin = new Vector2(0f, 0f);
                        barRT.anchorMax = new Vector2(0f, 1f);
                        barRT.sizeDelta = new Vector2(10f, 0f);
                        barRT.anchoredPosition = new Vector2(5f, 0f);
                        break;
                    default:  // 右边
                        barRT.anchorMin = new Vector2(1f, 0f);
                        barRT.anchorMax = new Vector2(1f, 1f);
                        barRT.sizeDelta = new Vector2(10f, 0f);
                        barRT.anchoredPosition = new Vector2(-5f, 0f);
                        break;
                }
                Image barImg = bar.GetComponent<Image>();
                barImg.color = new Color(1f, 0.55f, 0.78f, 0.4f);
                barImg.raycastTarget = false;
                apParts[i] = barImg;
            }
            apRootGO.SetActive(false);

            ComboView comboView = hudGO.AddComponent<ComboView>();
            SetPrivateField(comboView, "numberRoot", numberRT);
            SetPrivateField(comboView, "effectRoot", effectRootRT);
            SetPrivateField(comboView, "apRoot", apRootRT);
            SetPrivateField(comboView, "apParts", apParts);
            SetPrivateField(comboView, "digitTexts", comboDigitTexts);
            SetPrivateField(comboView, "digitPlates", comboDigitPlates);
            SetPrivateField(comboView, "effectPlates", comboEffectPlates);
            SetPrivateField(comboView, "comboLabel", comboLabel);
            SetPrivateField(comboView, "digitWidth", comboDigitW);
            SetPrivateField(hudC, "comboView", comboView);
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
            // 上半屏：航标（DeepSeaPresentation）+ 冷白大标题；说明沉底，不挡主行动
            TextMeshProUGUI title = CreateText("GameTitle", menuParent,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(960, 180),
                "menu.title", 120, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = DeepSeaTheme.Ink;

            TextMeshProUGUI subtitle = CreateText("Subtitle", menuParent,
                new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(960, 48),
                "menu.subtitle", DeepSeaTheme.CaptionSize, TextAlignmentOptions.Center);
            subtitle.color = DeepSeaTheme.Muted;

            const float barW = 1080f - DeepSeaTheme.SafeMargin * 2f;
            const float primaryH = 116f;
            const float rowH = 104f;
            const float gap = 24f;
            float halfW = (barW - gap) * 0.5f;

            // 通栏主行动 + 通栏存档 + 底行双次行动（对齐设计稿 01 热点）
            GameObject newGameBtn = CreateButton("NewGameButton", menuParent,
                new Vector2(0.5f, 0.307f), new Vector2(barW, primaryH), "menu.newGame", DeepSeaTheme.ActionSize);
            GameObject saveSelectBtn = CreateButton("SaveSelectButton", menuParent,
                new Vector2(0.5f, 0.230f), new Vector2(barW, primaryH), "menu.saveSelect", DeepSeaTheme.ActionSize);
            GameObject songSelectBtn = CreateButton("SongSelectButton", menuParent,
                new Vector2(0.5f, 0.137f), new Vector2(halfW, rowH), "menu.songSelect", DeepSeaTheme.ActionSize);
            ((RectTransform)songSelectBtn.transform).anchoredPosition = new Vector2(-(halfW + gap) * 0.5f, 0f);
            GameObject settingsBtn = CreateButton("SettingsButton", menuParent,
                new Vector2(0.5f, 0.137f), new Vector2(halfW, rowH), "menu.settings", DeepSeaTheme.ActionSize);
            ((RectTransform)settingsBtn.transform).anchoredPosition = new Vector2((halfW + gap) * 0.5f, 0f);

            TextMeshProUGUI hint = CreateText("Hint", menuParent,
                new Vector2(0.5f, 0.048f), new Vector2(0.5f, 0.048f), Vector2.zero, new Vector2(barW, 40),
                "menu.hint", DeepSeaTheme.CaptionSize, TextAlignmentOptions.Center);
            hint.color = DeepSeaTheme.Muted;

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
        /// <summary>
        /// 节奏校准面板（玩家界面；过稿稿 art_tools/review/calibration_screen/ 变体 A 中央脉冲）。
        /// 玩家只点屏幕：8 拍跟拍 → 自动建议值 → 大按钮细调/应用/试听。
        /// </summary>
        private static CalibrationController CreateCalibrationPanel(RectTransform canvasRect, Transform menuParent,
            GameStarter starter, SettingsPanelController settingsController)
        {
            GameObject panel = new GameObject("CalibrationPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.07f, 0.96f);

            TextMeshProUGUI title = CreateText("CalTitle", panel.transform,
                new Vector2(0.5f, 0.90f), new Vector2(0.5f, 0.90f), Vector2.zero, new Vector2(800, 90),
                "cal.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            TextMeshProUGUI status = CreateText("CalStatus", panel.transform,
                new Vector2(0.5f, 0.83f), new Vector2(0.5f, 0.83f), Vector2.zero, new Vector2(900, 60),
                "cal.hint", 30, TextAlignmentOptions.Center);
            status.color = new Color(1f, 1f, 1f, 0.75f);

            // 中央脉冲（节拍到达时放大淡出）
            GameObject ring = new GameObject("CalPulseRing", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(panel.transform, false);
            RectTransform rr = (RectTransform)ring.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.62f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(340, 340);
            Image ringImg = ring.GetComponent<Image>();
            ringImg.color = new Color(0.44f, 0.88f, 0.84f, 0.55f);
            ringImg.raycastTarget = false;

            // 下落区：竖轨 + 判定线（圆环中心）+ 下落音符（与真实演奏同一套透视曲线）
            GameObject playfield = new GameObject("CalLanePlayfield", typeof(RectTransform));
            playfield.transform.SetParent(panel.transform, false);
            RectTransform pf = (RectTransform)playfield.transform;
            pf.anchorMin = pf.anchorMax = new Vector2(0.5f, 0.62f);
            pf.pivot = new Vector2(0.5f, 0.5f);
            pf.sizeDelta = new Vector2(420, 1100);

            GameObject laneBG = new GameObject("CalLaneBG", typeof(RectTransform), typeof(Image));
            laneBG.transform.SetParent(playfield.transform, false);
            RectTransform lbr = (RectTransform)laneBG.transform;
            lbr.anchorMin = new Vector2(0.5f, 0f);
            lbr.anchorMax = new Vector2(0.5f, 1f);
            lbr.pivot = new Vector2(0.5f, 0.5f);
            lbr.sizeDelta = new Vector2(240, 0);
            Image lbg = laneBG.GetComponent<Image>();
            lbg.color = new Color(1f, 1f, 1f, 0.05f);
            lbg.raycastTarget = false;

            GameObject judgeBar = new GameObject("CalJudgeLine", typeof(RectTransform), typeof(Image));
            judgeBar.transform.SetParent(playfield.transform, false);
            RectTransform jbr = (RectTransform)judgeBar.transform;
            jbr.anchorMin = jbr.anchorMax = new Vector2(0.5f, 0.5f);
            jbr.pivot = new Vector2(0.5f, 0.5f);
            jbr.anchoredPosition = Vector2.zero;
            jbr.sizeDelta = new Vector2(420, 6);
            Image jbi = judgeBar.GetComponent<Image>();
            jbi.color = new Color(0.81f, 0.94f, 0.92f, 0.95f);
            jbi.raycastTarget = false;

            GameObject laneNoteGO = new GameObject("CalLaneNote", typeof(RectTransform), typeof(Image));
            laneNoteGO.transform.SetParent(playfield.transform, false);
            RectTransform lnr = (RectTransform)laneNoteGO.transform;
            lnr.anchorMin = lnr.anchorMax = new Vector2(0.5f, 0.5f);
            lnr.pivot = new Vector2(0.5f, 0.5f);
            lnr.sizeDelta = new Vector2(228, 40);
            Image lni = laneNoteGO.GetComponent<Image>();
            lni.color = new Color(0.44f, 0.88f, 0.84f, 0.95f);
            lni.raycastTarget = false;
            laneNoteGO.SetActive(false);

            TextMeshProUGUI tapHint = CreateText("CalTapHint", panel.transform,
                new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(400, 80),
                "cal.tap", 44, TextAlignmentOptions.Center);
            tapHint.color = new Color(0.89f, 0.96f, 0.95f, 0f);      // 只在需要时显示（当前恒隐藏）

            // 数值卡：当前值 / 本次测量结果
            GameObject card = new GameObject("CalCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(panel.transform, false);
            RectTransform cr = (RectTransform)card.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.40f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(940, 380);      // 加高：塞下 osu! 式的"建议偏移"三段提示
            card.GetComponent<Image>().color = new Color(0.055f, 0.10f, 0.125f, 1f);

            TextMeshProUGUI value = CreateText("CalValue", card.transform,
                new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.85f), Vector2.zero, new Vector2(880, 70),
                "", 36, TextAlignmentOptions.Center);
            value.color = new Color(0.44f, 0.88f, 0.84f, 1f);
            value.fontStyle = FontStyles.Bold;

            TextMeshProUGUI result = CreateText("CalResult", card.transform,
                new Vector2(0.5f, 0.63f), new Vector2(0.5f, 0.63f), Vector2.zero, new Vector2(880, 60),
                "", 28, TextAlignmentOptions.Center);
            result.color = new Color(1f, 1f, 1f, 0.7f);

            // osu! 式建议（三段：还没数据 / 已经准确 / 建议 X）+ 应用 + 清空统计
            TextMeshProUGUI suggest = CreateText("CalSuggestText", card.transform,
                new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), Vector2.zero, new Vector2(880, 120),
                "", 26, TextAlignmentOptions.Center);
            suggest.color = new Color(0.85f, 0.92f, 0.95f, 0.92f);
            GameObject applySuggest = CreateButton("CalApplySuggestButton", card.transform,
                new Vector2(0.28f, 0.07f), new Vector2(380, 76), "cal.applySuggest", 26);
            GameObject clearStats = CreateButton("CalClearStatsButton", card.transform,
                new Vector2(0.72f, 0.07f), new Vector2(380, 76), "cal.clearStats", 26);

            // 细调 ±5ms
            GameObject minus = CreateButton("CalMinusButton", panel.transform,
                new Vector2(0.28f, 0.28f), new Vector2(220, 100), "cal.minus", 40);
            GameObject plus = CreateButton("CalPlusButton", panel.transform,
                new Vector2(0.72f, 0.28f), new Vector2(220, 100), "cal.plus", 40);
            TextMeshProUGUI stepLabel = CreateText("CalStepLabel", panel.transform,
                new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(300, 60),
                "cal.step", 26, TextAlignmentOptions.Center);
            stepLabel.color = new Color(1f, 1f, 1f, 0.45f);

            // 主操作：开始测量 / 应用建议值 / 试听 / 关闭
            GameObject start = CreateButton("CalStartButton", panel.transform,
                new Vector2(0.5f, 0.185f), new Vector2(720, 110), "cal.start", 40);
            GameObject apply = CreateButton("CalApplyButton", panel.transform,
                new Vector2(0.5f, 0.115f), new Vector2(720, 100), "cal.apply", 36);
            GameObject preview = CreateButton("CalPreviewButton", panel.transform,
                new Vector2(0.5f, 0.048f), new Vector2(340, 88), "cal.preview", 32);
            GameObject close = CreateButton("CalCloseButton", panel.transform,
                new Vector2(0.5f, 0.048f), new Vector2(340, 88), "cal.close", 32);
            ((RectTransform)preview.transform).anchoredPosition = new Vector2(-190f, 0f);
            ((RectTransform)close.transform).anchoredPosition = new Vector2(190f, 0f);

            CalibrationController controller = panel.AddComponent<CalibrationController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "pulseRing", rr);
            SetPrivateField(controller, "lanePlayfield", pf);
            SetPrivateField(controller, "laneNote", lnr);
            SetPrivateField(controller, "laneSpawnY", 520f);
            SetPrivateField(controller, "laneJudgeY", 0f);
            SetPrivateField(controller, "laneFallSeconds", 1.0f);
            SetPrivateField(controller, "statusText", status);
            SetPrivateField(controller, "valueText", value);
            SetPrivateField(controller, "resultText", result);
            SetPrivateField(controller, "startButton", start.GetComponent<Button>());
            SetPrivateField(controller, "applyButton", apply.GetComponent<Button>());
            SetPrivateField(controller, "previewButton", preview.GetComponent<Button>());
            SetPrivateField(controller, "minusButton", minus.GetComponent<Button>());
            SetPrivateField(controller, "plusButton", plus.GetComponent<Button>());
            SetPrivateField(controller, "closeButton", close.GetComponent<Button>());
            SetPrivateField(controller, "suggestText", suggest);
            SetPrivateField(controller, "applySuggestButton", applySuggest.GetComponent<Button>());
            SetPrivateField(controller, "clearStatsButton", clearStats.GetComponent<Button>());
            panel.SetActive(false);

            SetPrivateField(settingsController, "calibrationController", controller);
            SetPrivateField(settingsController, "calibrationPanel", panel);
            return controller;
        }

        /// <summary>
        /// 下落速度预览面板（照 PJSK 前端：调速度时能直接看音符怎么落）。
        /// 用的就是游戏本体的下落曲线与判定线（`PlayPerspective` + `PlayVisualSpec.JudgeLineY`），
        /// 速度源与设置页/暂停页同一个 `FallSpeedController`（同源同步）。
        /// </summary>
        private static FallSpeedPreviewController CreateFallSpeedPreviewPanel(RectTransform canvasRect,
            SettingsPanelController settingsController, FallSpeedController fsc)
        {
            GameObject panel = new GameObject("FallSpeedPreviewPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.07f, 0.96f);

            TextMeshProUGUI title = CreateText("FallPreviewTitle", panel.transform,
                new Vector2(0.5f, 0.90f), new Vector2(0.5f, 0.90f), Vector2.zero, new Vector2(800, 90),
                "fallPreview.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            TextMeshProUGUI hint = CreateText("FallPreviewHint", panel.transform,
                new Vector2(0.5f, 0.845f), new Vector2(0.5f, 0.845f), Vector2.zero, new Vector2(900, 60),
                "fallPreview.hint", 28, TextAlignmentOptions.Center);
            hint.color = new Color(1f, 1f, 1f, 0.7f);

            // 下落区：与演奏页同源（音符沿透视曲线从出生点落到判定线）
            GameObject playfield = new GameObject("FallPreviewPlayfield", typeof(RectTransform));
            playfield.transform.SetParent(panel.transform, false);
            RectTransform pf = (RectTransform)playfield.transform;
            pf.anchorMin = pf.anchorMax = new Vector2(0.5f, 0.52f);
            pf.pivot = new Vector2(0.5f, 0.5f);
            pf.sizeDelta = new Vector2(420, 1300);

            GameObject laneBG = new GameObject("FallPreviewLaneBG", typeof(RectTransform), typeof(Image));
            laneBG.transform.SetParent(playfield.transform, false);
            RectTransform lbr = (RectTransform)laneBG.transform;
            lbr.anchorMin = new Vector2(0.5f, 0f);
            lbr.anchorMax = new Vector2(0.5f, 1f);
            lbr.pivot = new Vector2(0.5f, 0.5f);
            lbr.sizeDelta = new Vector2(240, 0);
            Image lbg = laneBG.GetComponent<Image>();
            lbg.color = new Color(1f, 1f, 1f, 0.05f);
            lbg.raycastTarget = false;

            GameObject judgeBar = new GameObject("FallPreviewJudgeLine", typeof(RectTransform), typeof(Image));
            judgeBar.transform.SetParent(playfield.transform, false);
            RectTransform jbr = (RectTransform)judgeBar.transform;
            jbr.anchorMin = jbr.anchorMax = new Vector2(0.5f, 0.5f);
            jbr.pivot = new Vector2(0.5f, 0.5f);
            jbr.anchoredPosition = Vector2.zero;
            jbr.sizeDelta = new Vector2(420, 6);
            Image jbi = judgeBar.GetComponent<Image>();
            jbi.color = new Color(0.81f, 0.94f, 0.92f, 0.95f);
            jbi.raycastTarget = false;

            // 4 个循环下落的音符（密度 = 一屏 4 个；真正的"快慢"由速度倍率决定）
            var notes = new RectTransform[4];
            for (int i = 0; i < notes.Length; i++)
            {
                GameObject noteGO = new GameObject($"FallPreviewNote{i}", typeof(RectTransform), typeof(Image));
                noteGO.transform.SetParent(playfield.transform, false);
                RectTransform nr = (RectTransform)noteGO.transform;
                nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 0.5f);
                nr.pivot = new Vector2(0.5f, 0.5f);
                nr.sizeDelta = new Vector2(228, 40);
                Image ni = noteGO.GetComponent<Image>();
                ni.color = new Color(0.44f, 0.88f, 0.84f, 0.95f);
                ni.raycastTarget = false;
                notes[i] = nr;
            }

            TextMeshProUGUI speedText = CreateText("FallPreviewSpeedText", panel.transform,
                new Vector2(0.5f, 0.255f), new Vector2(0.5f, 0.255f), Vector2.zero, new Vector2(600, 90),
                "", 56, TextAlignmentOptions.Center);
            speedText.color = new Color(0.44f, 0.88f, 0.84f, 1f);
            speedText.fontStyle = FontStyles.Bold;

            TextMeshProUGUI fallTimeText = CreateText("FallPreviewFallTimeText", panel.transform,
                new Vector2(0.5f, 0.205f), new Vector2(0.5f, 0.205f), Vector2.zero, new Vector2(900, 60),
                "", 28, TextAlignmentOptions.Center);
            fallTimeText.color = new Color(1f, 1f, 1f, 0.65f);

            GameObject minus = CreateButton("FallPreviewMinusButton", panel.transform,
                new Vector2(0.32f, 0.14f), new Vector2(180, 96), "−", 48);
            GameObject plus = CreateButton("FallPreviewPlusButton", panel.transform,
                new Vector2(0.68f, 0.14f), new Vector2(180, 96), "＋", 48);
            GameObject close = CreateButton("FallPreviewCloseButton", panel.transform,
                new Vector2(0.5f, 0.06f), new Vector2(560, 96), "fallPreview.close", 36);

            FallSpeedPreviewController controller = panel.AddComponent<FallSpeedPreviewController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "lanePlayfield", pf);
            SetPrivateField(controller, "previewNotes", notes);
            SetPrivateField(controller, "speedText", speedText);
            SetPrivateField(controller, "fallTimeText", fallTimeText);
            SetPrivateField(controller, "minusButton", minus.GetComponent<Button>());
            SetPrivateField(controller, "plusButton", plus.GetComponent<Button>());
            SetPrivateField(controller, "closeButton", close.GetComponent<Button>());
            SetPrivateField(controller, "fallSpeed", fsc);
            panel.SetActive(false);

            SetPrivateField(settingsController, "fallSpeedPreviewController", controller);
            SetPrivateField(settingsController, "fallPreviewPanel", panel);
            return controller;
        }

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

            // 判定偏移行（两条路并存，作者 2026-09-22 定案）：
            //   ① osu! 式统计校准：打歌 → 中位命中误差 → 建议值 → 一键应用（Core/HitErrorTracker）
            //   ② 独立的手动校准窗口：跟拍点击 + ± 微调 + 试听（CalibrationPanel，后建盖在本页上）
            // 上游 OpenSekai 只有工程向的数字输入，没有玩家界面（见 docs/live_front_checklist.md）
            TextMeshProUGUI offsetSummary = CreateText("SettingsOffsetSummary", panel.transform,
                new Vector2(0.5f, 0.665f), new Vector2(0.5f, 0.665f), Vector2.zero, new Vector2(960, 110),
                "", 26, TextAlignmentOptions.Center);
            offsetSummary.color = new Color(1f, 1f, 1f, 0.72f);

            GameObject calBtn = CreateButton("SettingsCalibrationButton", panel.transform,
                new Vector2(0.30f, 0.605f), new Vector2(430, 96), "settings.calibration", 34);
            GameObject applyOffsetBtn = CreateButton("SettingsApplyOffsetButton", panel.transform,
                new Vector2(0.70f, 0.605f), new Vector2(430, 96), "settings.applySuggest", 34);

            // 下落速度行：±按钮 + 动态数值（监听由 FallSpeedController 双组绑定）
            TextMeshProUGUI speedLabel = CreateText("SettingsFallSpeedLabel", panel.transform,
                new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f), Vector2.zero, new Vector2(600, 60),
                "settings.fallSpeed", 40, TextAlignmentOptions.Center);
            speedLabel.color = new Color(1, 1, 1, 0.8f);
            GameObject speedMinus = CreateButton("SettingsFallSpeedMinusButton", panel.transform,
                new Vector2(0.22f, 0.46f), new Vector2(150, 80), "−", 44);
            GameObject speedPlus = CreateButton("SettingsFallSpeedPlusButton", panel.transform,
                new Vector2(0.58f, 0.46f), new Vector2(150, 80), "＋", 44);
            TextMeshProUGUI speedText = CreateText("SettingsFallSpeedText", panel.transform,
                new Vector2(0.40f, 0.46f), new Vector2(0.40f, 0.46f), Vector2.zero, new Vector2(180, 80),
                "", 40, TextAlignmentOptions.Center);
            // 下落速度预览入口（照 PJSK：调速度时能直接看音符怎么落；见 FallSpeedPreviewController）
            GameObject fallPreviewBtn = CreateButton("SettingsFallPreviewButton", panel.transform,
                new Vector2(0.80f, 0.46f), new Vector2(300, 80), "settings.fallPreview", 32);

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
            SetPrivateField(controller, "applySuggestedOffsetButton", applyOffsetBtn.GetComponent<Button>());
            SetPrivateField(controller, "offsetSummaryText", offsetSummary);
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
            SetPrivateField(controller, "openFallPreviewButton", fallPreviewBtn.GetComponent<Button>());

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
        /// 装备背包面板：SceneBuilder 建骨架（全屏背景 + 标题 + 右上角 X + 空内容容器），
        /// 网格/说明条由 EquipmentPanelController 运行时渲染。自带嵌套 Canvas 排序 252
        /// （盖过 TalentPanel 251 / PortfolioCanvas 250）。
        /// </summary>
        private static void CreateEquipmentPanel(RectTransform canvasRect, GameStarter starter)
        {
            GameObject panel = new GameObject("EquipmentPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            Canvas panelCanvas = panel.AddComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 252;
            panel.AddComponent<GraphicRaycaster>();

            TextMeshProUGUI title = CreateText("EquipmentTitle", panel.transform,
                new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), Vector2.zero, new Vector2(700, 100),
                "equipmentPanel.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            GameObject closeBtn = CreateButton("EquipmentCloseButton", panel.transform,
                new Vector2(0.93f, 0.93f), new Vector2(170, 80), "equipmentPanel.close", 32);

            GameObject content = new GameObject("EquipmentContent", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            EquipmentPanelController controller = panel.AddComponent<EquipmentPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "contentRoot", crt);
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 注入 GameStarter：运行时转交给 PortfolioPanelController（地图页"装备 n/20"按钮）
            SetPrivateField(starter, "equipmentPanelController", controller);
        }

        /// <summary>
        /// 商店弹窗：SceneBuilder 建骨架（全屏背景 + 标题 + 右上角 X + 空内容容器），
        /// 候选网格/购买确认由 ShopPanelController 运行时渲染。嵌套 Canvas 排序 253（盖过背包 252）。
        /// </summary>
        private static void CreateShopPanel(RectTransform canvasRect, GameStarter starter)
        {
            GameObject panel = new GameObject("ShopPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.85f);
            Canvas panelCanvas = panel.AddComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 253;
            panel.AddComponent<GraphicRaycaster>();

            TextMeshProUGUI title = CreateText("ShopTitle", panel.transform,
                new Vector2(0.5f, 0.93f), new Vector2(0.5f, 0.93f), Vector2.zero, new Vector2(700, 100),
                "shopPanel.title", 56, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            GameObject closeBtn = CreateButton("ShopCloseButton", panel.transform,
                new Vector2(0.93f, 0.93f), new Vector2(170, 80), "shopPanel.close", 32);

            GameObject content = new GameObject("ShopContent", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            RectTransform crt = (RectTransform)content.transform;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            ShopPanelController controller = panel.AddComponent<ShopPanelController>();
            SetPrivateField(controller, "panelRoot", panel);
            SetPrivateField(controller, "contentRoot", crt);
            SetPrivateField(controller, "closeButton", closeBtn.GetComponent<Button>());
            panel.SetActive(false);

            // 注入 GameStarter：运行时转交给 PortfolioPanelController（地图页"商店"重开按钮）
            SetPrivateField(starter, "shopPanelController", controller);
        }

        /// <summary>
        /// 新游戏覆盖确认弹窗：全屏半透明遮罩 + 居中卡片（说明 + 确认/取消）。
        /// 初始非激活；显隐与按钮接线在 GameStarter（始终激活）。空白默认槽不弹（GameStarter 判定）。
        /// </summary>
        private static void CreateNewGameConfirmPanel(RectTransform canvasRect, GameStarter starter)
        {
            GameObject panel = new GameObject("NewGameConfirmPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRect, false);
            RectTransform rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.72f);

            // 居中卡片
            GameObject card = new GameObject("ConfirmCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(panel.transform, false);
            RectTransform crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(760, 560);
            card.GetComponent<Image>().color = DeepSeaTheme.Card;

            TextMeshProUGUI message = CreateText("NewGameConfirmMessage", card.transform,
                new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(680, 280),
                "menu.newGameConfirm", DeepSeaTheme.BodySize, TextAlignmentOptions.Center);
            message.color = DeepSeaTheme.Ink;

            GameObject confirmBtn = CreateButton("NewGameConfirmButton", card.transform,
                new Vector2(0.3f, 0.2f), new Vector2(280, 100), "portfolio.confirm", DeepSeaTheme.ActionSize);
            GameObject cancelBtn = CreateButton("NewGameCancelButton", card.transform,
                new Vector2(0.7f, 0.2f), new Vector2(280, 100), "portfolio.cancel", DeepSeaTheme.ActionSize);
            panel.SetActive(false);

            SetPrivateField(starter, "newGameConfirmPanel", panel);
            SetPrivateField(starter, "newGameConfirmButton", confirmBtn.GetComponent<Button>());
            SetPrivateField(starter, "newGameCancelButton", cancelBtn.GetComponent<Button>());
        }

        /// <summary>
        /// 节拍校准：校准面板本体（开始测试/应用推荐/±5ms/关闭）。
        /// 主菜单入口按钮已移除；由选项设置页的"打开校准"按钮进入。
        /// 反向注入控制器/面板引用给设置页（ESC 层叠判断与打开按钮）。
        /// </summary>

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
                cjkFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CjkFontAssetPath);
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
