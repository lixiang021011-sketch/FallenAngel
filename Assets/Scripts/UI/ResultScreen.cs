using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Gameplay;
using FallenAngel.Core;
using FallenAngel.Data;
using FallenAngel.Audio;

namespace FallenAngel.UI
{
    /// <summary>
    /// 结算界面控制器
    /// 显示分数、判定统计、准确率、评级、最大连击等
    /// </summary>
    public class ResultScreen : MonoBehaviour
    {
        [Header("根对象")]
        [SerializeField] private GameObject rootPanel;

        [Header("歌曲信息")]
        [SerializeField] private TextMeshProUGUI songNameText;
        [SerializeField] private TextMeshProUGUI songArtistText;
        [SerializeField] private TextMeshProUGUI difficultyText;
        [SerializeField] private Image difficultyColorBar;

        [Header("主要数值")]
        [SerializeField] private TextMeshProUGUI rankText;         // S/A/B/C/D
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI accuracyText;    // 98.45%
        [SerializeField] private TextMeshProUGUI maxComboText;

        [Header("判定统计")]
        [SerializeField] private TextMeshProUGUI perfectCountText;
        [SerializeField] private TextMeshProUGUI greatCountText;
        [SerializeField] private TextMeshProUGUI goodCountText;
        [SerializeField] private TextMeshProUGUI badCountText;
        [SerializeField] private TextMeshProUGUI missCountText;

        [Header("FC/PM标记")]
        [SerializeField] private GameObject fullComboMark;
        [SerializeField] private GameObject perfectMark;

        [Header("按钮")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button backButton;

        [Header("面板引用（留空自动查找）")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private GameObject gamePanel;

        [Header("评级颜色")]
        [SerializeField] private Color rankS = Color.yellow;
        [SerializeField] private Color rankA = Color.green;
        [SerializeField] private Color rankB = Color.cyan;
        [SerializeField] private Color rankC = new Color(1f, 0.5f, 0f);
        [SerializeField] private Color rankD = Color.red;

        [Header("难度颜色")]
        [SerializeField] private Color diffEasy = Color.green;
        [SerializeField] private Color diffNormal = Color.cyan;
        [SerializeField] private Color diffHard = Color.yellow;
        [SerializeField] private Color diffExpert = Color.red;

        private void Awake()
        {
            // Awake 时 rootPanel 可能为 null（稍后由 SceneBuilder 或 Start 设置）
            // 只在有 rootPanel 时才隐藏
            if (rootPanel != null) rootPanel.SetActive(false);
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameEnd += ShowResult;
        }

        private void Start()
        {
            // 重新订阅事件
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameEnd -= ShowResult;
                GameManager.Instance.OnGameEnd += ShowResult;
            }

            // 自动查找面板引用
            FindReferences();
            BindButtons();

            // 隐藏结算面板
            if (rootPanel != null) rootPanel.SetActive(false);

            Debug.Log($"[ResultScreen] Start complete. retryButton={retryButton != null} backButton={backButton != null} menuPanel={menuPanel != null} gamePanel={gamePanel != null}");
        }

        private void FindReferences()
        {
            // 如果 rootPanel 没指定，找 ResultPanel
            if (rootPanel == null)
            {
                Transform rp = transform.Find("ResultPanel");
                if (rp != null) rootPanel = rp.gameObject;
                else rootPanel = gameObject;
            }

            // 查找按钮（在 rootPanel/ResultRoot 下）
            if (retryButton == null && rootPanel != null)
            {
                Transform rr = rootPanel.transform.Find("ResultRoot");
                if (rr != null)
                {
                    Transform rb = rr.Find("RetryButton");
                    if (rb != null) retryButton = rb.GetComponent<Button>();
                }
                if (retryButton == null)
                {
                    Transform rb = rootPanel.transform.Find("RetryButton");
                    if (rb != null) retryButton = rb.GetComponent<Button>();
                }
            }
            if (backButton == null && rootPanel != null)
            {
                Transform rr = rootPanel.transform.Find("ResultRoot");
                if (rr != null)
                {
                    Transform bb = rr.Find("BackButton");
                    if (bb != null) backButton = bb.GetComponent<Button>();
                }
                if (backButton == null)
                {
                    Transform bb = rootPanel.transform.Find("BackButton");
                    if (bb != null) backButton = bb.GetComponent<Button>();
                }
            }

            // 查找 MenuPanel
            if (menuPanel == null)
            {
                Transform canvas = transform.parent;
                if (canvas != null)
                {
                    Transform menu = canvas.Find("MenuPanel");
                    if (menu != null) menuPanel = menu.gameObject;
                }
            }

            // gamePanel 就是自身所在
            if (gamePanel == null) gamePanel = gameObject;
        }

        private void BindButtons()
        {
            if (retryButton != null)
            {
                retryButton.onClick.RemoveAllListeners();
                retryButton.onClick.AddListener(OnRetryClicked);
            }
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(OnBackClicked);
            }
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameEnd -= ShowResult;
        }

        private void ShowResult()
        {
            if (rootPanel != null) rootPanel.SetActive(true);

            // 歌曲信息
            if (GameManager.Instance?.CurrentChart?.metadata != null)
            {
                var meta = GameManager.Instance.CurrentChart.metadata;
                if (songNameText != null) songNameText.text = meta.songName;
                if (songArtistText != null) songArtistText.text = meta.songArtist;
                if (difficultyText != null) difficultyText.text = $"{meta.difficulty} Lv.{meta.level}";
                if (difficultyColorBar != null) difficultyColorBar.color = GetDifficultyColor(meta.difficulty);
            }

            // 统计数据
            if (JudgeManager.Instance != null)
            {
                var j = JudgeManager.Instance;
                int totalJudge = j.PerfectCount + j.GreatCount + j.GoodCount + j.BadCount + j.MissCount;

                if (scoreText != null) scoreText.text = j.Score.ToString("N0");
                if (accuracyText != null) accuracyText.text = $"{j.CalculateAccuracy():F2}%";
                if (maxComboText != null) maxComboText.text = $"{j.MaxCombo}";
                if (perfectCountText != null) perfectCountText.text = j.PerfectCount.ToString();
                if (greatCountText != null) greatCountText.text = j.GreatCount.ToString();
                if (goodCountText != null) goodCountText.text = j.GoodCount.ToString();
                if (badCountText != null) badCountText.text = j.BadCount.ToString();
                if (missCountText != null) missCountText.text = j.MissCount.ToString();

                string rank = j.GetRank();
                if (rankText != null)
                {
                    rankText.text = rank;
                    rankText.color = GetRankColor(rank);
                }

                // Full Combo / Perfect
                bool isFullCombo = j.MissCount == 0;
                bool isPerfect = j.MissCount == 0 && j.GoodCount == 0 && j.BadCount == 0 && j.GreatCount == 0;
                if (fullComboMark != null) fullComboMark.SetActive(isFullCombo && !isPerfect);
                if (perfectMark != null) perfectMark.SetActive(isPerfect);
            }
        }

        private Color GetRankColor(string rank)
        {
            return rank switch
            {
                "S" => rankS,
                "A" => rankA,
                "B" => rankB,
                "C" => rankC,
                _ => rankD
            };
        }

        private Color GetDifficultyColor(Difficulty d)
        {
            return d switch
            {
                Difficulty.Easy => diffEasy,
                Difficulty.Normal => diffNormal,
                Difficulty.Hard => diffHard,
                Difficulty.Expert => diffExpert,
                _ => Color.gray
            };
        }

        private void OnRetryClicked()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (rootPanel != null) rootPanel.SetActive(false);

            if (GameManager.Instance != null && GameManager.Instance.CurrentChart != null)
            {
                // 重启音频
                AudioManager.Instance?.StopAll();
                AudioManager.Instance?.LoadAndPlayBGM(
                    GameManager.Instance.CurrentChart,
                    GameManager.Instance.CurrentChart.metadata.offset >= 0 ? GameManager.Instance.CurrentChart.metadata.offset : 0f);

                // 加载谱面并开始倒计时
                GameManager.Instance.LoadChart(GameManager.Instance.CurrentChart);
                GameManager.Instance.StartCountdownAndPlay();
            }
            // 确保面板状态正确
            if (menuPanel != null) menuPanel.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(true);
        }

        private void OnBackClicked()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (rootPanel != null) rootPanel.SetActive(false);

            // 直接切换面板（GameStarter在MenuPanel上，可能已被禁用）
            if (menuPanel != null) menuPanel.SetActive(true);
            if (gamePanel != null) gamePanel.SetActive(false);

            GameManager.Instance?.BackToMenu();
        }
    }
}
