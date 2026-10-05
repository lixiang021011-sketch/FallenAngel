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
        [Tooltip("倒计时表现层（E1：缩放弹出 + 淡出；未注入时退回纯文本）")]
        [SerializeField] private FallenAngel.UI.CountdownView countdownView;
        [Tooltip("加载页（E5；未注入时不显示加载页，流程照旧）")]
        [SerializeField] private FallenAngel.UI.LoadingScreen loadingScreen;
        private AudioClip countdownTickClip;
        private AudioClip countdownGoClip;
        private bool isLoadingScreen;

        /// <summary>当前游戏状态</summary>
        public GameState CurrentState { get; private set; }

        /// <summary>当前谱面数据</summary>
        public ChartData CurrentChart { get; private set; }

        /// <summary>可选的同曲声部演奏；普通单谱仍沿用原流程。</summary>
        public SongPartSession Parts { get; private set; }
        public ChartData ActiveChart => Parts?.ActiveChart ?? CurrentChart;
        public float SongDuration => Parts?.Definition.Duration ?? CurrentChart?.GetTotalDuration() ?? 0f;
        public event System.Action OnPartChanged;

        /// <summary>切换生效时机（每首歌可配：立即 / 下一拍 / 下一小节 + 最短间隔）</summary>
        public PartSwitchConfig SwitchConfig { get; private set; } = PartSwitchConfig.Immediate;

        /// <summary>已排队、尚未生效的声部（等拍点用）；没有则为 null</summary>
        public string PendingPart { get; private set; }
        /// <summary>排队切换的生效时刻（SongTime）</summary>
        public float PendingSwitchTime { get; private set; }
        /// <summary>排队事件：界面据此把芯片显示成"待生效"</summary>
        public event System.Action OnPartSwitchQueued;

        private float lastSwitchSongTime = -999f;

        /// <summary>
        /// 玩家发起的切换请求：按 <see cref="SwitchConfig"/> 决定**立刻生效**还是**等到拍点**。
        /// 不传名字 = 循环到谱面表里的下一件（键盘 Tab）；传名字 = 直接请求那一件（底部切换条用）。
        /// </summary>
        public bool RequestPartSwitch(string partName = null)
        {
            if (CurrentState != GameState.Playing || Parts == null) return false;
            string target = string.IsNullOrEmpty(partName) ? Parts.NextPartName() : partName;
            if (Parts.Definition.ChartOf(target) == null) return false;
            if (target == Parts.PartName)
            {
                PendingPart = null;   // 点当前声部 = 取消排队
                return false;
            }
            if (SwitchConfig.minIntervalSeconds > 0f &&
                SongTime - lastSwitchSongTime < SwitchConfig.minIntervalSeconds)
            {
                Debug.Log(string.Format("[GameManager] 切换被最短间隔挡住（{0:0.0}s < {1:0.0}s）",
                    SongTime - lastSwitchSongTime, SwitchConfig.minIntervalSeconds));
                return false;
            }

            float at = SwitchConfig.TargetTime(SongTime, CurrentBpm);
            if (at <= SongTime + 0.0001f) return ApplyPartSwitch(target);

            PendingPart = target;
            PendingSwitchTime = at;
            Debug.Log(string.Format("[GameManager] 切换排队：{0} 于 {1:0.00}s 生效（{2}）",
                target, at, SwitchConfig.Describe()));
            OnPartSwitchQueued?.Invoke();
            return true;
        }

        /// <summary>等拍点用：每帧推进一次（由 Update 调用；自检也可以直接调）</summary>
        public void TickPartSwitch()
        {
            if (CurrentState != GameState.Playing || Parts == null)
            {
                PendingPart = null;
                return;
            }
            if (string.IsNullOrEmpty(PendingPart)) return;
            if (SongTime + 0.0001f < PendingSwitchTime) return;
            ApplyPartSwitch(PendingPart);
        }

        /// <summary>改切换时机（原型面板/自检用；正式内容走谱面的 switchTiming）</summary>
        public void SetSwitchMode(PartSwitchMode mode)
        {
            PartSwitchConfig config = SwitchConfig;
            config.mode = mode;
            SetSwitchConfig(config);
        }

        /// <summary>整份替换切换配置（模式 / 最短间隔 / 每小节拍数）</summary>
        public void SetSwitchConfig(PartSwitchConfig config)
        {
            if (config.beatsPerBar <= 0f) config.beatsPerBar = 4f;
            SwitchConfig = config;
            PendingPart = null;      // 改完就清排队，避免"换了模式还在等上一拍的旧请求"
            PendingSwitchTime = 0f;
            Debug.Log("[GameManager] 切换时机改为：" + SwitchConfig.Describe());
        }

        /// <summary>
        /// 立刻换声部（不经过时机配置）：内部与自检用。
        /// 不传名字 = 循环到下一件。
        /// </summary>
        public bool SwitchPart(string partName = null)
        {
            if (CurrentState != GameState.Playing || Parts == null) return false;
            string target = string.IsNullOrEmpty(partName) ? Parts.NextPartName() : partName;
            return ApplyPartSwitch(target);
        }

        private bool ApplyPartSwitch(string partName)
        {
            if (!Parts.Select(partName, SongTime)) return false;
            lastSwitchSongTime = SongTime;
            PendingPart = null;
            PendingSwitchTime = 0f;
            AudioManager.Instance?.SetActiveStem(Parts.PartName);   // 分轨：当前轨推出来
            OnPartChanged?.Invoke();
            Debug.Log($"[GameManager] Part={Parts.PartName} SongTime={SongTime:F3} Energy={Parts.Energy:F1}");
            return true;
        }

        /// <summary>量化用的 BPM（谱面缺省按 120）</summary>
        private float CurrentBpm
        {
            get
            {
                return CurrentChart != null && CurrentChart.metadata != null && CurrentChart.metadata.bpm > 0.01f
                    ? CurrentChart.metadata.bpm : 120f;
            }
        }

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

            // 竖屏锁定（作者 2026-09-22 定案）：本作是竖屏下落式，不能跟着手机转。
            // 工程设置里已经改成 Portrait（见 BuildApk.ApplyPortraitLock），这里再补一道运行时保险 ——
            // 万一包体来自旧设置或某台机器不认 manifest，也不会横过来。
            // SafeAreaFitter 会跟着 Screen.orientation 变化重算安全区，所以这里改完它有感知。
            if (Application.isMobilePlatform && Screen.orientation != ScreenOrientation.Portrait)
            {
                Screen.orientation = ScreenOrientation.Portrait;
                Debug.Log("[GameManager] 竖屏锁定：Screen.orientation = Portrait");
            }
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
        public void LoadChart(ChartData chart, SongParts parts = null)
        {
            if (chart == null)
            {
                Debug.LogError("[GameManager] 谱面为空，无法加载");
                return;
            }

            // 同曲切谱：选中的可以是集合里的任意一声部（从它开始演奏），不要求一定是第一个
            if (parts != null && !parts.Contains(chart))
            {
                Debug.LogError("[GameManager] 声部集合与主谱不一致，无法加载");
                return;
            }
            // 原型重试/换歌是新演奏：上次本曲道具不能沿用旧的到期时刻。
            if (Parts != null) ModifierManager.Instance?.EndSong();
            CurrentChart = chart;
            Parts = parts == null ? null : new SongPartSession(parts, parts.PartNameOf(chart));
            // 切换时机是**每首歌**的属性：谱面没配就是"立即生效、不限间隔"
            PartSwitchConfig timing = chart.metadata != null ? chart.metadata.switchTiming : PartSwitchConfig.Immediate;
            if (timing.beatsPerBar <= 0f) timing.beatsPerBar = 4f;
            SwitchConfig = timing;
            PendingPart = null;
            PendingSwitchTime = 0f;
            lastSwitchSongTime = -999f;
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
            if (isCountingDown || isLoadingScreen) return;
            if (CurrentState != GameState.Loading)
            {
                Debug.LogWarning("[GameManager] 游戏未处于Loading状态，无法开始倒计时");
                return;
            }
            StartCoroutine(LoadingThenCountdown());
        }

        /// <summary>
        /// E5：加载页先亮 `LoadingTip.LoadingSeconds` 秒（上游是"资源真在加载"的时长；
        /// 我们本地读谱几乎瞬时，不给窗口的话加载页只会闪一帧、观感上等于没有），
        /// 然后关掉加载页进倒计时。
        /// </summary>
        private System.Collections.IEnumerator LoadingThenCountdown()
        {
            isLoadingScreen = true;
            if (loadingScreen != null)
            {
                loadingScreen.Show();
                Debug.Log(string.Format("[GameManager] 加载页显示 {0:0.##}s（E5）", LoadingTip.LoadingSeconds));
                yield return new WaitForSecondsRealtime(LoadingTip.LoadingSeconds);
                loadingScreen.Hide();
            }
            isLoadingScreen = false;
            StartCoroutine(CountdownCoroutine());
        }

        private System.Collections.IEnumerator CountdownCoroutine()
        {
            isCountingDown = true;

            // E1：3 → 2 → 1 各 1 秒、每步一声（上游 CountdownCore：逐个激活特效 + WaitForSeconds(1f)
            // + 每步 PlaySEOneShot(SE_LIVE_COUNT_DOWN)），最后 GO! 0.5 秒。
            FallenAngel.UI.CountdownView view = countdownView;
            if (view != null || countdownText != null)
            {
                int steps = CountdownTiming.From - CountdownTiming.To + 2;   // 3/2/1 + GO!
                for (int step = 0; step < steps; step++)
                {
                    if (!isCountingDown) break;

                    if (view != null) view.ShowStep(step);
                    else if (countdownText != null)
                    {
                        countdownText.gameObject.SetActive(true);
                        countdownText.text = CountdownTiming.LabelAt(step);
                    }
                    PlayCountdownTick(CountdownTiming.IsGo(step));

                    float wait = CountdownTiming.IsGo(step)
                        ? CountdownTiming.GoSeconds
                        : CountdownTiming.StepSeconds;
                    yield return new WaitForSecondsRealtime(wait);
                }

                if (view != null) view.Hide();
                else if (countdownText != null) countdownText.gameObject.SetActive(false);
            }
            else
            {
                yield return new WaitForSecondsRealtime(CountdownTiming.TotalSeconds);
            }

            isCountingDown = false;
            StartGame();
        }

        /// <summary>倒计时每步一声：3/2/1 用中音、"GO!" 用高音（与 HUD 的节拍器音色同一套合成器）</summary>
        private void PlayCountdownTick(bool isGo)
        {
            Audio.AudioManager am = Audio.AudioManager.Instance;
            if (am == null) return;
            if (countdownTickClip == null)
                countdownTickClip = Audio.SynthesizedSfx.CreateHitClip(880f, 0.06f, 30f);
            if (countdownGoClip == null)
                countdownGoClip = Audio.SynthesizedSfx.CreateHitClip(1320f, 0.10f, 24f);
            am.PlaySfx(isGo ? countdownGoClip : countdownTickClip, isGo ? 0.9f : 0.7f);
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

            // 等拍点的切换先推进（只看 SongTime，用量上一帧的时间即可）
            TickPartSwitch();

            // 唯一权威时间：从 AudioManager 读取（真实音频钟或虚拟钟），在此统一扣除谱面偏移
            if (AudioManager.Instance == null)
            {
                if (!warnedMissingAudioManager)
                {
                    warnedMissingAudioManager = true;
                    Debug.LogError("[GameManager] 场景中缺少 AudioManager，无法推进游戏时间！请用菜单 Tools > FallenAngel > Scenes and Assets > Build Default Game Scene 重建场景");
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
                (CurrentChart != null && SongTime >= SongDuration))
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
            PendingPart = null;   // 结算后不再补发排队中的切换
            // 曲终音乐淡出（音频长于谱面时结算页不再残留播放；音频已播完时为空操作）
            AudioManager.Instance?.FadeOutBGM();
            OnGameEnd?.Invoke();
            // 声部路线：曲终把最后一段收口并落一行可读日志（原型验收靠这行，不靠感觉）
            if (Parts != null)
            {
                Parts.Route.Close(SongTime);
                Debug.Log("[GameManager] " + Parts.Route.Summary().Replace("\n", " ‖ "));
                // 试玩数据落盘：手机/打包版拿不到 Console，一行 JSON 追加到 persistentDataPath
                LastRunLogPath = PartRunLog.Record(this, Parts);
            }
            Debug.Log("[GameManager] 游戏结束！");
        }

        /// <summary>最近一局试玩记录的落盘路径（原型面板显示；没有则 null）</summary>
        public string LastRunLogPath { get; private set; }

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
                LoadChart(CurrentChart, Parts?.Definition);
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
            Parts = null;
            PendingPart = null;
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
