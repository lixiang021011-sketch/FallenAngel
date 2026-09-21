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
        Map,        // 未使用。旧随机格地图入口；行程地图是 Portfolio 覆盖层，不切此状态。
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
        public PortfolioSession Portfolio { get; private set; }
        /// <summary>场景拥有的成长入口，不增加全局单例。</summary>
        public void SetPortfolioSession(PortfolioSession session) { Portfolio = session; }

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
            // 适配高刷新率屏（120Hz 跑满；60Hz 屏自动锁 60，不产生副作用）
            // 帧率/延迟策略：**保持平台默认的 vSync**（=1）。
            // 真机实测"按键后的视觉反馈与触感不在同一瞬间"，而关掉 vSync 后 Unity 可能在
            // 刚错过一次 vsync 之后才呈现画面，白多出一整帧（16.6ms @60Hz）延迟——上一步为性能
            // 关 vSync 是错的取舍，这里还原。移动端 vSync 开启时 targetFrameRate 会被忽略，
            // 但桌面/编辑器仍需要它，所以保留。
            QualitySettings.vSyncCount = 1;
            // 呈现延迟：限制排队帧数。默认值会让引擎多攒一帧再呈现，直接多出一整帧输入→画面延迟；
            // 1 帧 = 算完就交，代价是更容易掉帧，所以与"每帧开销"一起看（FPS 读数已带 P95）。
            QualitySettings.maxQueuedFrames = 1;
            // 目标帧率写死 120（**不要**按 Screen.currentResolution 去 clamp：设备当前若是 60Hz 模式，
            // 读到的就是 60 → 自己把自己锁在 60，永远上不去）。真正的开关是显示模式，由下面这次请求决定。
            Application.targetFrameRate = 120;
            // 请求设备最高刷新率（Android；60Hz 设备上无副作用）。MIUI 默认把应用当 60Hz 内容，
            // 不主动请求就永远跑 60——"帧率不够"的根因就在这里。
            HighRefreshRate.Request();
            // 音频输出延迟（真机"音画偏差非常大"的根因之一）：先把 DSP 缓冲往小压（真减延迟），
            // 再量实际生效值并把剩余部分计入 SongTime 偏移，见 AudioLatency
            AudioLatency.ConfigureForLowLatency();
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
            // 键数驱动轨道布局（4K/5K 动态化唯一入口：LaneLayout/InputManager/NoteSpawner/JudgeManager 均读它）
            LaneLayout.SetActiveLaneCount(chart.LaneCount);
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

            if (Portfolio != null && !Portfolio.BeforeSongStart()) return;
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

            // 时间基 = 音频位置 **+ 判定偏移**（照抄上游 BaseLiveController：
            // currentAudioLatencyMusicTimeMs => Mathf.Max(timingAdjust + 音乐秒数, 0f)；
            // 正数 = 音符提前）**− 谱面偏移** − 音频输出延迟补偿
            // （AudioLatency：AudioSource.time 给的是"送去播的位置"，真正听到还要晚一个 DSP+设备延迟，正数 = 音符推迟。
            //  两档方向相反，别混——上游靠 fillerSec 垫音频解决后一档，我们靠时钟扣减。）
            SongTime = Mathf.Max(0f, AudioManager.Instance.CurrentTime
                + CalibrationSettings.TimingAdjustSeconds
                - (CurrentChart?.metadata.offset ?? 0f)
                - AudioLatency.CompensationSeconds);
            // 记下"这次 SongTime 采样时的输入时钟读数"：输入事件按自己的时间戳换算歌曲时间要用
            InputClock.Sample(SongTime);

            // 曲终检测：真实音频自然播完，或时间超过谱面总时长（虚拟钟模式兜底）。
            // 切后台时不要把暂停的 BGM 当成曲终。
            bool audioFinished = Application.isFocused && AudioManager.Instance.HasAudioFinished;
            if (audioFinished ||
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
            // 曲终音乐淡出（音频长于谱面时结算页不再残留播放；音频已播完时为空操作）
            AudioManager.Instance?.FadeOutBGM();
            OnGameEnd?.Invoke();
            Debug.Log("[GameManager] 游戏结束！");
        }

        /// <summary>
        /// 暂停/继续游戏
        /// </summary>
        public void TogglePause()
        {
            if (Portfolio?.HasConfirmation == true) return;
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
