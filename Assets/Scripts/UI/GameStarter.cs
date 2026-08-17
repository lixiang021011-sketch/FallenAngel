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

        [Header("启动按钮（可选）")]
        [SerializeField] private Button startDemoButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button drumsButton;
        [SerializeField] private Button easyDrumsButton;
        [SerializeField] private Button bassButton;
        [SerializeField] private Button synthButton;
        [SerializeField] private Button calibrationButton;
        [SerializeField] private CalibrationController calibrationController;
        [SerializeField] private Button languageButton;
        [SerializeField] private TextMeshProUGUI languageButtonLabel;   // 动态显示当前语言名
        [SerializeField] private LanguagePanelController languagePanelController;
        [SerializeField] private InputField chartNameInput;

        [Header("自动启动Demo（无UI时使用）")]
        [SerializeField] private bool autoStartDemoOnAwake = false;
        [SerializeField] private float startDelay = 1.0f;

        private void Awake()
        {
            // 按钮监听必须在 Play 模式接（编辑模式添加的监听会在进 Play 时被序列化清空）
            if (startDemoButton != null) startDemoButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartDemoChart(); });
            if (pauseButton != null) pauseButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); GameManager.Instance?.TogglePause(); });
            if (drumsButton != null) drumsButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartChartFromResources("酸橙色信笺_双指版"); });
            if (easyDrumsButton != null) easyDrumsButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartChartFromResources("酸橙色信笺_Easy"); });
            if (bassButton != null) bassButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartChartFromResources("demo_bass"); });
            if (synthButton != null) synthButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartChartFromResources("demo_synth"); });
            // 校准面板挂的 CalibrationController 初始非激活（其 Awake 不执行），入口按钮由这里接
            if (calibrationButton != null) calibrationButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (calibrationController != null) calibrationController.Open();
            });
            // 语言面板入口：打开选择面板（面板内按钮由 LanguagePanelController 首次激活时接）
            if (languageButton != null) languageButton.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                if (languagePanelController != null) languagePanelController.Open();
            });

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
            Loc.OnLanguageChanged += RefreshLanguageLabel;
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
            }
            Loc.OnLanguageChanged -= RefreshLanguageLabel;
            Loc.OnLanguageChanged += RefreshLanguageLabel;
            RefreshLanguageLabel();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
            }
            Loc.OnLanguageChanged -= RefreshLanguageLabel;
        }

        /// <summary>菜单语言按钮 label 显示当前语言（原生名称，如 中文/English）</summary>
        private void RefreshLanguageLabel()
        {
            if (languageButtonLabel != null)
                languageButtonLabel.text = Loc.T($"lang.{Loc.CurrentLanguage}");
        }

        private void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Menu:
                    ShowMenu(true);
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

        public void OnLoadChartByName()
        {
            if (chartNameInput == null || string.IsNullOrWhiteSpace(chartNameInput.text))
            {
                StartDemoChart();
                return;
            }
            StartChartFromResources(chartNameInput.text.Trim());
        }
    }
}
