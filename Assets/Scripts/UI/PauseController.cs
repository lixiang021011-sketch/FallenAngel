using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;
using FallenAngel.Audio;

namespace FallenAngel.UI
{
    /// <summary>
    /// 暂停控制器 - 自动查找所有引用，不依赖 Inspector 赋值
    /// 挂载位置：HUDPanel（始终激活）
    /// </summary>
    public class PauseController : MonoBehaviour
    {
        [Header("按钮（留空自动查找）")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button exitButton;

        [Header("面板（留空自动查找）")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject gamePanel;

        private void Awake()
        {
            if (pausePanel == null) pausePanel = transform.parent?.gameObject;
            if (pausePanel == null) pausePanel = gameObject;
        }

        private void Start()
        {
            FindReferences();
            BindButtons();

            if (pausePanel != null) pausePanel.SetActive(false);

            Debug.Log($"[PauseController] Start complete. pauseButton={pauseButton != null} resumeButton={resumeButton != null} retryButton={retryButton != null} exitButton={exitButton != null}");
        }

        private void FindReferences()
        {
            if (pausePanel == null)
            {
                Transform gamePanelT = transform.parent;
                if (gamePanelT != null)
                {
                    Transform pp = gamePanelT.Find("PausePanel");
                    if (pp != null) pausePanel = pp.gameObject;
                }
                if (pausePanel == null) pausePanel = gameObject;
            }

            if (gamePanel == null)
            {
                Transform gp = transform.parent;
                if (gp != null) gamePanel = gp.gameObject;
            }

            // 查找 pauseButton（HUD 上）
            if (pauseButton == null && gamePanel != null)
            {
                Transform hud = gamePanel.transform.Find("HUDPanel");
                if (hud != null)
                {
                    Transform pb = hud.Find("PauseButton");
                    if (pb != null) pauseButton = pb.GetComponent<Button>();
                }
            }

            // 查找 resume/retry/exit 按钮（在 PausePanel -> PauseRoot 下）
            if (pausePanel != null)
            {
                Transform pr = pausePanel.transform.Find("PauseRoot");
                if (pr != null)
                {
                    Transform rb = pr.Find("ResumeButton");
                    if (rb != null) resumeButton = rb.GetComponent<Button>();
                    Transform retb = pr.Find("RetryButton");
                    if (retb != null) retryButton = retb.GetComponent<Button>();
                    Transform eb = pr.Find("ExitButton");
                    if (eb != null) exitButton = eb.GetComponent<Button>();
                }
            }

            // 查找 MenuPanel
            if (menuPanel == null && gamePanel != null)
            {
                Transform canvas = gamePanel.transform.parent;
                if (canvas != null)
                {
                    Transform menu = canvas.Find("MenuPanel");
                    if (menu != null) menuPanel = menu.gameObject;
                }
            }
        }

        private void BindButtons()
        {
            if (pauseButton != null)
            {
                pauseButton.onClick.RemoveAllListeners();
                pauseButton.onClick.AddListener(OnPauseClicked);
            }
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveAllListeners();
                resumeButton.onClick.AddListener(OnResumeClicked);
            }
            if (retryButton != null)
            {
                retryButton.onClick.RemoveAllListeners();
                retryButton.onClick.AddListener(OnRetryClicked);
            }
            if (exitButton != null)
            {
                exitButton.onClick.RemoveAllListeners();
                exitButton.onClick.AddListener(OnExitClicked);
            }
        }

        private void OnPauseClicked()
        {
            if (GameManager.Instance == null) return;

            // 如果正在暂停，点击暂停按钮直接恢复
            if (GameManager.Instance.CurrentState == GameState.Paused)
            {
                OnResumeClicked();
                return;
            }

            try { AudioManager.Instance?.PlayButtonClick(); } catch { }
            if (GameManager.Instance.CurrentState == GameState.Playing)
            {
                GameManager.Instance.PauseGame();
                if (pausePanel != null) pausePanel.SetActive(true);
            }
        }

        private void OnResumeClicked()
        {
            try { AudioManager.Instance?.PlayButtonClick(); } catch { }

            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Paused)
            {
                GameManager.Instance.ResumeGame();
                if (pausePanel != null) pausePanel.SetActive(false);
                Debug.Log("[PauseController] Game resumed");
            }
        }

        private void OnRetryClicked()
        {
            try { AudioManager.Instance?.PlayButtonClick(); } catch { }

            if (pausePanel != null) pausePanel.SetActive(false);

            if (GameManager.Instance != null && GameManager.Instance.CurrentChart != null)
            {
                // 停止音频并重新播放
                AudioManager.Instance?.StopAll();
                AudioManager.Instance?.LoadAndPlayBGM(
                    GameManager.Instance.CurrentChart,
                    GameManager.Instance.CurrentChart.metadata.offset >= 0 ? GameManager.Instance.CurrentChart.metadata.offset : 0f);

                // 加载谱面（进入Loading状态）并开始倒计时
                GameManager.Instance.LoadChart(GameManager.Instance.CurrentChart);
                // 直接调用 StartCountdownAndPlay 开始倒计时
                GameManager.Instance.StartCountdownAndPlay();
            }
        }

        private void OnExitClicked()
        {
            try { AudioManager.Instance?.PlayButtonClick(); } catch { }
            if (pausePanel != null) pausePanel.SetActive(false);

            if (menuPanel != null) menuPanel.SetActive(true);
            if (gamePanel != null) gamePanel.SetActive(false);

            GameManager.Instance?.BackToMenu();
        }

        private void Update()
        {
            if (GameManager.Instance == null) return;

            GameState state = GameManager.Instance.CurrentState;

            if (state == GameState.Playing)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Space))
                    OnPauseClicked();
                else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                    OnExitClicked();
            }
            else if (state == GameState.Paused)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Space) ||
                    UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    OnResumeClicked();
                }
            }
        }
    }
}
