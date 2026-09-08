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
        [SerializeField] private Button pauseButton;
        [SerializeField] private SongSelectPanelController songSelectPanelController;

        [Header("自动启动Demo（无UI时使用）")]
        [SerializeField] private bool autoStartDemoOnAwake = false;
        [SerializeField] private float startDelay = 1.0f;

        private void Awake()
        {
            if (GetComponent<PortfolioSession>() == null) gameObject.AddComponent<PortfolioSession>();
            if (GetComponent<PortfolioPanelController>() == null) gameObject.AddComponent<PortfolioPanelController>();
            // 地图页"天赋"入口需要天赋面板引用（SceneBuilder 注入到本组件，运行时转交）
            if (talentPanelController != null && GetComponent<PortfolioPanelController>() != null)
                GetComponent<PortfolioPanelController>().TalentPanel = talentPanelController;
            // 文案回退统一注册（幂等，JSON 优先），防其他组件注册时序问题
            PortfolioText.Register();
            PortfolioSession session = GetComponent<PortfolioSession>();
            // 按钮监听必须在 Play 模式接（编辑模式添加的监听会在进 Play 时被序列化清空）
            if (newGameButton != null) newGameButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (session != null) session.StartNewGame();
                else Debug.LogError("[GameStarter] 缺少 PortfolioSession，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
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

        private void OnGameStateChanged(GameState state)
        {
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
    }
}
