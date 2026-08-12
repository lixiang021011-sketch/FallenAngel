using UnityEngine;
using TMPro;
using FallenAngel.Data;
using FallenAngel.Audio;

namespace FallenAngel.Core
{
    /// <summary>
    /// 游戏状态枚举
    /// </summary>
    public enum GameState
    {
        Menu,       // 菜单界面
        Loading,    // 加载中
        Playing,    // 游戏进行中
        Paused,     // 暂停
        Result      // 结算界面
    }

    /// <summary>
    /// 游戏全局管理器 - 单例模式
    /// 负责游戏状态流转、核心计时
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("游戏设置")]
        [Tooltip("音符从生成到到达判定线所需的时间（秒）")]
        public float noteFallTime = 2.0f;

        [Tooltip("游戏速度倍率，1.0为正常速度")]
        [Range(0.5f, 2.0f)]
        public float speedMultiplier = 1.0f;

        [Header("倒计时UI")]
        [SerializeField] private TextMeshProUGUI countdownText;

        /// <summary>当前游戏状态</summary>
        public GameState CurrentState { get; private set; }

        /// <summary>当前谱面数据</summary>
        public ChartData CurrentChart { get; private set; }

        /// <summary>歌曲当前播放时间（秒，含偏移补偿）</summary>
        public float SongTime { get; private set; }

        /// <summary>歌曲是否已开始播放</summary>
        public bool IsSongStarted { get; private set; }

        /// <summary>实际下落时间 = 基础下落时间 / 速度倍率</summary>
        public float ActualFallTime => noteFallTime / speedMultiplier;

        // 事件委托
        public System.Action<GameState> OnStateChanged;
        public System.Action OnGameStart;
        public System.Action OnGameEnd;

        private bool isInitialized;
        private bool isCountingDown;
        private bool warnedMissingAudioManager;

        private void Awake()
        {
            // 单例初始化
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CurrentState = GameState.Menu;
        }

        /// <summary>
        /// 加载指定谱面并准备开始
        /// </summary>
        public void LoadChart(ChartData chart)
        {
            if (chart == null)
            {
                Debug.LogError("[GameManager] 谱面为空，无法加载");
                return;
            }

            CurrentChart = chart;
            ChangeState(GameState.Loading);
            isInitialized = false;
            SongTime = 0f;
            IsSongStarted = false;
        }

        /// <summary>
        /// 设置倒计时文本引用（由SceneBuilder调用）
        /// </summary>
        public void SetCountdownText(TextMeshProUGUI text)
        {
            countdownText = text;
        }

        /// <summary>
        /// 开始倒计时并在倒计时结束后正式开始游戏
        /// </summary>
        public void StartCountdownAndPlay()
        {
            if (isCountingDown) return;
            if (CurrentState != GameState.Loading)
            {
                Debug.LogWarning("[GameManager] 游戏未处于Loading状态，无法开始倒计时");
                return;
            }
            StartCoroutine(CountdownCoroutine());
        }

        private System.Collections.IEnumerator CountdownCoroutine()
        {
            isCountingDown = true;

            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
                countdownText.text = "3";
                yield return new WaitForSecondsRealtime(1f);

                if (!isCountingDown) yield break;
                countdownText.text = "2";
                yield return new WaitForSecondsRealtime(1f);

                if (!isCountingDown) yield break;
                countdownText.text = "1";
                yield return new WaitForSecondsRealtime(1f);

                if (!isCountingDown) yield break;
                countdownText.text = "GO!";
                yield return new WaitForSecondsRealtime(0.5f);

                countdownText.gameObject.SetActive(false);
            }
            else
            {
                yield return new WaitForSecondsRealtime(3.5f);
            }

            isCountingDown = false;
            StartGame();
        }

        /// <summary>
        /// 取消倒计时（用于暂停时）
        /// </summary>
        public void CancelCountdown()
        {
            isCountingDown = false;
            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 开始游戏（在所有系统准备好后调用）
        /// </summary>
        public void StartGame()
        {
            if (CurrentState != GameState.Loading)
            {
                Debug.LogWarning("[GameManager] 游戏未处于Loading状态，无法开始");
                return;
            }

            Time.timeScale = 1f;
            ChangeState(GameState.Playing);
            isInitialized = true;
            IsSongStarted = true;
            OnGameStart?.Invoke();
            Debug.Log("[GameManager] 游戏开始！");
        }

        private void Update()
        {
            if (CurrentState != GameState.Playing || !isInitialized)
                return;

            // 唯一权威时间：从 AudioManager 读取（真实音频钟或虚拟钟），在此统一扣除谱面偏移
            if (AudioManager.Instance == null)
            {
                if (!warnedMissingAudioManager)
                {
                    warnedMissingAudioManager = true;
                    Debug.LogError("[GameManager] 场景中缺少 AudioManager，无法推进游戏时间！请用菜单 Tools > FallenAngel > Build Default Game Scene 重建场景");
                }
                return;
            }

            SongTime = Mathf.Max(0f, AudioManager.Instance.CurrentTime - (CurrentChart?.metadata.offset ?? 0f));

            // 曲终检测：真实音频播完，或时间超过谱面总时长（虚拟钟模式兜底）
            if (AudioManager.Instance.HasAudioFinished ||
                (CurrentChart != null && SongTime >= CurrentChart.GetTotalDuration()))
            {
                EndGame();
            }
        }

        /// <summary>
        /// 结束游戏并进入结算
        /// </summary>
        public void EndGame()
        {
            if (CurrentState == GameState.Result) return;
            ChangeState(GameState.Result);
            OnGameEnd?.Invoke();
            Debug.Log("[GameManager] 游戏结束！");
        }

        /// <summary>
        /// 暂停/继续游戏
        /// </summary>
        public void TogglePause()
        {
            if (CurrentState == GameState.Playing)
                PauseGame();
            else if (CurrentState == GameState.Paused)
                ResumeGame();
        }

        public void PauseGame()
        {
            if (CurrentState != GameState.Playing) return;
            ChangeState(GameState.Paused);
            Time.timeScale = 0f;
            // 暂停BGM（虚拟钟由状态门控自动冻结，无需处理）
            if (AudioManager.Instance != null)
                AudioManager.Instance.PauseBGM();
            Debug.Log("[GameManager] 游戏暂停");
        }

        public void ResumeGame()
        {
            if (CurrentState != GameState.Paused) return;
            ChangeState(GameState.Playing);
            Time.timeScale = 1f;
            // 恢复BGM（时间连续性由单一音频时钟自然保证）
            if (AudioManager.Instance != null)
                AudioManager.Instance.ResumeBGM();
            Debug.Log("[GameManager] 游戏继续");
        }

        /// <summary>
        /// 重试游戏
        /// </summary>
        public void RetryGame()
        {
            if (CurrentChart != null)
            {
                Time.timeScale = 1f;
                LoadChart(CurrentChart);
            }
        }

        /// <summary>
        /// 返回主菜单
        /// </summary>
        public void BackToMenu()
        {
            Time.timeScale = 1f;
            // 返回菜单时停止音乐（修复：此前音乐会继续在菜单播放）
            if (AudioManager.Instance != null)
                AudioManager.Instance.StopAll();
            CurrentChart = null;
            SongTime = 0f;
            IsSongStarted = false;
            ChangeState(GameState.Menu);
        }

        private void ChangeState(GameState newState)
        {
            if (CurrentState == newState) return;
            GameState oldState = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
            Debug.Log($"[GameManager] 状态切换: {oldState} -> {newState}");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Time.timeScale = 1f;
            }
        }
    }
}
