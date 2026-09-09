using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Core;
using FallenAngel.Data;
using FallenAngel.Audio;
using FallenAngel.Gameplay;

namespace FallenAngel.UI
{
    public class GameStarter : MonoBehaviour
    {
        [Header("菜单/面板对象")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject gamePanel;

        [Header("主菜单按钮")]
        [SerializeField] private Button newGameButton;     // 新游戏 → 覆盖默认槽直接开局
        [SerializeField] private Button saveSelectButton;  // 选择存档 → 存档选择面板（阶段4改接 EnterSaveSelect）
        [SerializeField] private Button songSelectButton;  // 自选曲目 → 选歌面板
        [SerializeField] private Button settingsButton;    // 选项设置 → 设置面板
        [SerializeField] private SettingsPanelController settingsPanelController;
        [SerializeField] private SaveSelectPanelController saveSelectPanelController;
        [SerializeField] private TalentPanelController talentPanelController; // 交给 PortfolioPanelController（地图页天赋入口）
        [SerializeField] private EquipmentPanelController equipmentPanelController; // 交给 PortfolioPanelController（地图页背包入口）
        [SerializeField] private ShopPanelController shopPanelController; // 交给 PortfolioPanelController（地图页商店重开按钮）
        [Header("新游戏覆盖确认弹窗")]
        [SerializeField] private GameObject newGameConfirmPanel;
        [SerializeField] private Button newGameConfirmButton;
        [SerializeField] private Button newGameCancelButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private SongSelectPanelController songSelectPanelController;

        [Header("自动启动Demo（无UI时使用）")]
        [SerializeField] private bool autoStartDemoOnAwake = false;
        [SerializeField] private float startDelay = 1.0f;

        private void Awake()
        {
            if (GetComponent<PortfolioSession>() == null) gameObject.AddComponent<PortfolioSession>();
            if (GetComponent<PortfolioPanelController>() == null) gameObject.AddComponent<PortfolioPanelController>();
            // 地图页"天赋"/"装备"/"商店"入口需要面板引用（SceneBuilder 注入到本组件，运行时转交）
            if (GetComponent<PortfolioPanelController>() != null)
            {
                if (talentPanelController != null)
                    GetComponent<PortfolioPanelController>().TalentPanel = talentPanelController;
                if (equipmentPanelController != null)
                    GetComponent<PortfolioPanelController>().EquipmentPanel = equipmentPanelController;
                if (shopPanelController != null)
                    GetComponent<PortfolioPanelController>().ShopPanel = shopPanelController;
            }
            // 文案回退统一注册（幂等，JSON 优先），防其他组件注册时序问题
            PortfolioText.Register();
            PortfolioSession session = GetComponent<PortfolioSession>();
            // 按钮监听必须在 Play 模式接（编辑模式添加的监听会在进 Play 时被序列化清空）
            if (newGameButton != null) newGameButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (session == null)
                {
                    Debug.LogError("[GameStarter] 缺少 PortfolioSession，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
                    return;
                }
                // 覆盖默认槽是破坏性操作：有进度先弹确认，空白档直接开始
                if (session.DefaultProfileHasProgress()) ShowNewGameConfirm();
                else session.StartNewGame();
            });
            if (newGameConfirmButton != null) newGameConfirmButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                HideNewGameConfirm();
                if (session != null) session.StartNewGame();
                else Debug.LogError("[GameStarter] 缺少 PortfolioSession");
            });
            if (newGameCancelButton != null) newGameCancelButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                HideNewGameConfirm();
            });
            if (saveSelectButton != null) saveSelectButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                // 面板初始非激活、自身未订阅任何事件——打开必须由这里直接调（同其他面板模式）
                if (session != null) session.EnterSaveSelect();
                if (saveSelectPanelController != null) saveSelectPanelController.Open();
                else Debug.LogError("[GameStarter] 缺少 SaveSelectPanelController，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
            });
            if (songSelectButton != null) songSelectButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (songSelectPanelController != null) songSelectPanelController.Open();
            });
            if (settingsButton != null) settingsButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (settingsPanelController != null) settingsPanelController.Open();
                else Debug.LogError("[GameStarter] 缺少 SettingsPanelController，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
            });
            if (pauseButton != null) pauseButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); GameManager.Instance?.TogglePause(); });

            if (autoStartDemoOnAwake)
            {
                Invoke(nameof(StartDemoChart), startDelay);
            }
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
            }
        }

        private void Start()
        {
            if (GetComponent<DeepSeaPresentation>() == null) gameObject.AddComponent<DeepSeaPresentation>();
#if UNITY_EDITOR
            CreateTemporaryArtDebugOverlay();
#endif
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
            }
        }

        private void Update()
        {
            // ESC 关闭新游戏确认弹窗
            if (Input.GetKeyDown(KeyCode.Escape) && newGameConfirmPanel != null && newGameConfirmPanel.activeSelf)
                HideNewGameConfirm();
#if UNITY_EDITOR
            RefreshArtDebugButtons();
#endif
        }

        private void OnGameStateChanged(GameState state)
        {
            // 离开主菜单时收起确认弹窗（如超时进入其他状态）
            if (state != GameState.Menu && newGameConfirmPanel != null && newGameConfirmPanel.activeSelf)
                HideNewGameConfirm();
            switch (state)
            {
                case GameState.Menu:
                    ShowMenu(true);
                    ShowGame(false);
                    break;
                case GameState.Map:
                    ShowMenu(false);
                    ShowGame(false);
                    break;
                case GameState.Loading:
                    ShowMenu(false);
                    ShowGame(true);
                    // 通知 GameManager 开始倒计时（由其自身执行协程）
                    GameManager.Instance?.StartCountdownAndPlay();
                    break;
                case GameState.Playing:
                    ShowMenu(false);
                    ShowGame(true);
                    break;
                case GameState.Result:
                    break;
            }
        }

        private void ShowNewGameConfirm()
        {
            if (newGameConfirmPanel != null) newGameConfirmPanel.SetActive(true);
            Debug.Log("[GameStarter] 新游戏覆盖确认弹窗已打开（默认槽有进度）");
        }

        private void HideNewGameConfirm()
        {
            if (newGameConfirmPanel != null) newGameConfirmPanel.SetActive(false);
        }

        private void ShowMenu(bool show)
        {
            if (menuPanel != null) menuPanel.SetActive(show);
        }

        private void ShowGame(bool show)
        {
            if (gamePanel != null) gamePanel.SetActive(show);
        }

        [ContextMenu("Start Demo Chart")]
        public void StartDemoChart()
        {
            ChartData demo = ChartLoader.GenerateDemoChart();
            StartChart(demo);
        }

#if UNITY_EDITOR
        /// <summary>调试入口：无商店/掉落 UI 时验证装备持有链路（与 PortfolioSession 上同名入口等价，二选一）。打包不含。</summary>
        [ContextMenu("Debug: Acquire Next Equipment")]
        public void DebugAcquireNextEquipment()
        {
            PortfolioSession s = GetComponent<PortfolioSession>();
            if (s != null) s.DebugAcquireNextEquipment();
            else Debug.LogError("[GameStarter] 缺少 PortfolioSession，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
        }
#endif

        /// <summary>
        /// 核心玩法调试入口（Play 模式下 Inspector 右键本组件）：
        /// 战斗格临时占位期间（RunManager.BattleNodeAsPlaceholder=true），
        /// 用 ContextMenu 直接开谱测试游玩内容，与地图 UI 流程分开验证。
        /// </summary>
        [ContextMenu("Play Test Chart (5K)")]
        public void PlayTestChart5K()
        {
            StartChartFromResources("test_chart_5k");
        }

        [ContextMenu("Play Silent Chart (All Types)")]
        public void PlaySilentChart()
        {
            // 无声测试谱（5K 全类型，含宽音符/任意键判定）：无音频依赖，虚拟钟模式
            StartChartFromResources("test_chart_silent");
        }

        [ContextMenu("Play Drums Chart (4K)")]
        public void PlayDrumsChart()
        {
            StartChartFromResources("酸橙色信笺_双指版");
        }

        [ContextMenu("Play Drums Chart (Easy 4K)")]
        public void PlayDrumsEasyChart()
        {
            StartChartFromResources("酸橙色信笺_Easy");
        }

        public void StartChartFromResources(string chartJsonName)
        {
            ChartData chart = ChartLoader.LoadFromResources(chartJsonName);
            if (chart == null)
            {
                Debug.LogWarning($"[GameStarter] 找不到谱面 {chartJsonName}，回退到Demo谱面");
                chart = ChartLoader.GenerateDemoChart();
            }
            StartChart(chart);
        }

        public void StartChart(ChartData chart)
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[GameStarter] 场景中没有GameManager！");
                return;
            }

            AudioManager.Instance?.StopAll();
            GameManager.Instance.LoadChart(chart);

            // 装载BGM但不播放：倒计时结束后由 OnGameStart 统一触发播放，保证开局时间对齐
            if (AudioManager.Instance != null && chart != null)
            {
                AudioManager.Instance.LoadBGM(chart);
            }

            // 不直接 StartGame，而是通过 LoadChart 触发 Loading 状态
            // OnGameStateChanged 会调用 GameManager.StartCountdownAndPlay()

            ShowMenu(false);
            ShowGame(true);
        }

#if UNITY_EDITOR
        private Button artDebugEquipButton;
        private Button artDebugRefreshButton;
        private Button artDebugSkipButton;

        /// <summary>
        /// 临时验收悬浮条（后续与美术精修收尾一起删除）：
        /// 独立嵌套 Canvas 排序 300，悬浮于商店/背包/天赋/地图等全部 UI 之上，
        /// 只转发现有调试方法，不包含任何业务逻辑。
        /// </summary>
        private void CreateTemporaryArtDebugOverlay()
        {
            if (transform.Find("ArtDebugOverlay") != null) return;
            var session = GetComponent<PortfolioSession>();
            var font = TMP_Settings.defaultFontAsset;
            foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text.font != null) { font = text.font; break; }
            }

            var overlay = new GameObject("ArtDebugOverlay", typeof(RectTransform));
            overlay.transform.SetParent(transform, false);
            var rect = overlay.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = overlay.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 300;
            overlay.AddComponent<GraphicRaycaster>();

            // 三个竖排按钮放在屏幕左上角：不遮挡中央路线/轨道与下半屏触区。
            artDebugEquipButton = TemporaryDebugButton(overlay.transform, "DebugAcquireEquipment", "获取装备", 12, 12, 250, 54,
                font, new Color(.18f, .32f, .36f), () => session?.DebugAcquireNextEquipment());
            artDebugRefreshButton = TemporaryDebugButton(overlay.transform, "DebugGrantRefresh", "获取刷新次数", 12, 74, 250, 54,
                font, new Color(.18f, .32f, .36f), () => session?.DebugGrantRefreshBudget());
            artDebugSkipButton = TemporaryDebugButton(overlay.transform, "DebugSkipBattle", "自动通关", 12, 136, 250, 54,
                font, new Color(.45f, .24f, .16f), () => session?.DebugSkipBattle());
            RefreshArtDebugButtons();
        }

        private void RefreshArtDebugButtons()
        {
            var session = GetComponent<PortfolioSession>();
            var run = session?.Run;
            bool hasRun = run != null;
            if (artDebugEquipButton != null) artDebugEquipButton.interactable = hasRun;
            if (artDebugRefreshButton != null) artDebugRefreshButton.interactable = hasRun;
            // 跳过战斗只对“待开始演奏”的战斗房有效；FINISHED/结算等阶段置灰，避免无效点击。
            if (artDebugSkipButton != null) artDebugSkipButton.interactable = run != null && run.phase == "READY";
        }

        private Button TemporaryDebugButton(Transform parent, string name, string text, float x, float y,
            float w, float h, TMP_FontAsset font, Color color, System.Action action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            label.font = font;
            label.text = text;
            label.fontSize = 26;
            label.alignment = TextAlignmentOptions.Center;
            label.color = DeepSeaTheme.Ink;
            label.raycastTarget = false;

            button.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                action?.Invoke();
            });
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            return button;
        }
#endif
    }
}
