using System;
using System.Collections.Generic;
using FallenAngel.Core;
using FallenAngel.Data;
using FallenAngel.InputSystem;
using FallenAngel.Live;
using UnityEngine;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 判定管理器 —— **薄壳**。判定规则本身全部在 <c>Assets/Scripts/Core/Live/</c>
    /// （OpenSekai 判定簇移植）。本类只做四件事：
    ///
    /// 1. **接线**：开局把谱面（<see cref="NoteData"/>）喂给 <see cref="LiveNoteBuilder"/> 建成判定音符树，
    ///    交给 <see cref="LiveScoreRuntime"/>；
    /// 2. **输入**：每帧把 <see cref="InputManager"/> 的按下状态 + 事件时间戳 + 手指坐标
    ///    装配成 <see cref="LiveTouch"/>（含 fingerId / musicTime / **小数轨道**），推给判定五步；
    /// 3. **口径**：把移植层的判定结果翻译成本工程的计分/连击/HUD 事件（Keep 原有事件签名，UI 不用改）；
    /// 4. **FallenAngel 自有规则**：滑条"跟手"（按住期间偏离判定跨度太久只断 combo、不作废整条）。
    ///    这是本工程的产品规则，不属于移植范围，单独放在 <see cref="UpdateSlideFollow"/>。
    ///
    /// 计分口径（与移植前保持一致，属于本工程的计分层而非判定层）：
    /// · 长按的**头**只做视觉与连击判断，不计分、不进判定计数；**尾**才计一首（所以一条长按 = 1 个音符）；
    /// · 连接点 / 连击点（滑条路径点）只影响 combo；
    /// · Drag 的判定完全走移植层（未触碰 → Miss），但按本工程协议"碰即 Perfect、永不 MISS"
    ///   在计分层被吸收为"只做视觉、不计 Miss"。
    /// </summary>
    public class JudgeManager : MonoBehaviour
    {
        public static JudgeManager Instance { get; private set; }

        [Header("判定窗口（基础值，局内 modifier 只改倍率，见 docs/architecture.md §9）")]
        [SerializeField] private JudgeWindows judgeWindows = new JudgeWindows();

        /// <summary>当前判定结果（供UI订阅显示）</summary>
        public event Action<JudgeResultType, int> OnJudgeResult; // (result, lane)

        /// <summary>判定偏差事件（秒，正=按早了，负=按晚了），供 HUD 显示"早/晚"指示</summary>
        public event Action<float> OnJudgeBias;

        /// <summary>
        /// 判定结果 + 描述（逐字对应上游 LiveFrontView.JudgmentNote → JudgmentView/DescriptionView.Excute）：
        /// **每个音符判完都会抛**（含长按头/连接点，与上游一致），前端只用它显示判定文字与 Fast/Late/FlickMiss。
        /// 计分/HUD 计数仍走 OnJudgeResult（那个才区分"计不计分"）。
        /// </summary>
        public event Action<NoteResult, NoteResultDescription> OnJudgeJudged;

        /// <summary>连击数更新事件</summary>
        public event Action<int, bool> OnComboUpdate; // (combo, isFullComboNow)

        /// <summary>分数更新事件</summary>
        public event Action<int> OnScoreUpdate; // (totalScore)

        /// <summary>各判定计数更新事件</summary>
        public event Action<int, int, int, int, int> OnJudgeCountsUpdate; // (perfect, great, good, bad, miss)

        // 判定计数
        public int PerfectCount { get; private set; }
        public int GreatCount { get; private set; }
        public int GoodCount { get; private set; }
        public int BadCount { get; private set; }
        public int MissCount { get; private set; }

        /// <summary>当前连击数</summary>
        public int Combo { get; private set; }
        /// <summary>最大连击数</summary>
        public int MaxCombo { get; private set; }
        /// <summary>当前总分</summary>
        public int Score { get; private set; }

        // ── 判定簇（移植层）──
        private LiveScoreRuntime runtime;
        private LiveLaneResolver laneResolver;
        private readonly Dictionary<NoteData, LiveNoteBase> liveByChartNote = new Dictionary<NoteData, LiveNoteBase>();
        private readonly Dictionary<LiveNoteBase, NoteData> chartNoteByLive = new Dictionary<LiveNoteBase, NoteData>();
        // 原有视图映射可能把长音头指向 LongEnd；资源账本必须使用真实头部身份。
        private readonly Dictionary<LiveNoteBase, NoteData> partRootByLive = new Dictionary<LiveNoteBase, NoteData>();
        private readonly List<LiveNoteBase> slideNotes = new List<LiveNoteBase>();

        // 本帧输入装配
        private readonly List<LiveTouch> frameTouches = new List<LiveTouch>(12);
        private readonly Dictionary<int, PendingInput> pendingInputs = new Dictionary<int, PendingInput>();
        private readonly List<KeyValuePair<int, PendingInput>> extraInputs = new List<KeyValuePair<int, PendingInput>>(4);
        private readonly Dictionary<int, Vector2> lastScreenPos = new Dictionary<int, Vector2>();

        // 滑条跟手（FallenAngel 自有规则）
        private readonly Dictionary<LiveNoteBase, float> slideOutOfRange = new Dictionary<LiveNoteBase, float>();
        private readonly HashSet<LiveNoteBase> slideSegmentBroken = new HashSet<LiveNoteBase>();
        private const float SlideFollowToleranceLanes = 1.4f;   // 与判定跨度中心的允许偏差（轨；放宽：拆墙后手指更容易飘）
        private const float SlideBreakSeconds = 0.32f;         // 连续脱离多久算断开（放宽：拆墙后手指更容易飘）

        // ── osu! 式命中误差采样（见 Core/HitErrorTracker）────────────────────────
        // 每一次"玩家真实计时输入"的判定都记一笔（note.JudgedErrorSeconds，正 = 打晚），
        // 曲终时取**中位数**作为一个样本存进跨局历史（osu! 的 SessionAverageHitErrorTracker）。
        private readonly List<float> sessionHitErrorsMs = new List<float>(256);
        private bool hitErrorSampleFlushed;

        // 谱面事件派发（v2 的 events 通道）：Fever 等子系统挂在这上面（见 Core/LiveEventDriver）
        private readonly LiveEventDriver liveEvents = new LiveEventDriver();

        private struct PendingInput
        {
            public LiveTouchPhase phase;
            public float musicTime;
            public Vector2 screenPos;
            public bool fromTouch;
            public int lane;
        }

        // ── 真机排查用的诊断日志（只在开发版打；release 静默）──
        private float lastOutsideLogTime = -10f;
        // 每秒一行的性能/延迟读数（帧率 + 显示模式 Hz + 输入滞后）
        private float perfWindowStart;
        private int perfFrames;
        private float perfStaleSum;
        private int perfStaleCount;
        private float perfStaleMax;

        /// <summary>判定簇的谱面运行时（只读；自检/视图要按音符查状态时用）</summary>
        public LiveScoreRuntime Runtime { get { return runtime; } }

        /// <summary>
        /// 谱面条目 → 判定音符（视图生成时登记用）。
        /// 长按尾/身体条目返回 null：它们没有独立判定（尾巴由头的 duration 生成），视图共用头的反馈。
        /// </summary>
        public LiveNoteBase LiveNoteFor(NoteData data)
        {
            if (data == null) return null;
            if (data.type == Data.NoteType.LongEnd || data.type == Data.NoteType.LongBody) return null;
            return liveByChartNote.TryGetValue(data, out LiveNoteBase live) ? live : null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            PlayRules.SetBaseWindows(judgeWindows);
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            // 先退订再订阅，防止 Awake 执行顺序导致漏订/重订
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
                InputManager.Instance.OnLaneInput += HandleLaneInput;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= ResetScoreForNewSong;
                GameManager.Instance.OnGameStart += ResetScoreForNewSong;
                GameManager.Instance.OnPartChanged -= OnPartChanged;
                GameManager.Instance.OnPartChanged += OnPartChanged;
            }
        }

        private void Unsubscribe()
        {
            if (InputManager.Instance != null)
            {
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= ResetScoreForNewSong;
                GameManager.Instance.OnPartChanged -= OnPartChanged;
            }
        }

        private void Update()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevAutoStartIfRequested();
#endif

            GameManager game = GameManager.Instance;
            if (game == null) return;

            // 曲终（Playing → Result）：把这一局的命中误差结算成一个样本（osu! 是一局结束记一个 DataPoint）
            if (game.CurrentState == GameState.Result && !hitErrorSampleFlushed) FlushHitErrorSample();
            if (game.CurrentState != GameState.Playing) return;
            if (runtime == null) return;

            float songTime = game.SongTime;
            // 局内 modifier 的判定窗口倍率（基础值 × 倍率；移植层查表时套用）
            LiveConfig.SetJudgeTimeScale(PlayRules.JudgeWindowScale);
            // 下落速度可能在开局后被改（设置面板/调速）；每帧同步一次，
            // 否则判定层还在用开局时的在屏时长 → "调整下落速度后的体验很奇怪"。
            runtime.NoteDisplayTimeOffset = Mathf.Max(0.1f, GameManager.Instance.ActualFallTime);

            runtime.Update(LiveNoteBuilder.Info(songTime));
            BuildFrameTouches(songTime);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (devAutoplay) runtime.AutoPlay(LiveNoteBuilder.Info(songTime));   // 开发版自测：自动演奏
#endif
            runtime.Judge(frameTouches);
            pendingInputs.Clear();
            extraInputs.Clear();

            liveEvents.Advance(songTime);    // 谱面事件（fever.* 等）按歌曲时间派发
            CutinService.Tick(Time.unscaledDeltaTime);   // cut-in 演出（未缩放时间：暂停时不流逝）
            UpdateSlideFollow(songTime);
            PlayLife.Tick(Time.deltaTime);   // 持续回血/掉血（来源：ModifierManager 的 LifeRegen）
            PerfTick(songTime);
        }

        /// <summary>真机排查：每秒一行"帧率 / 显示模式 Hz / 输入滞后(ms)"。</summary>
        private void PerfTick(float songTime)
        {
            if (!Debug.isDebugBuild) return;

            perfFrames++;
            if (perfWindowStart <= 0f) perfWindowStart = Time.unscaledTime;
            float elapsed = Time.unscaledTime - perfWindowStart;
            if (elapsed < 1f) return;

            float fps = perfFrames / elapsed;
            float avg = perfStaleCount > 0 ? perfStaleSum / perfStaleCount : 0f;
            Debug.Log(string.Format(
                "[Perf] fps={0:0.0}（显示 {1}Hz / target {2} / 帧 {3:0.0}ms） 输入滞后 avg={4:0.0}ms max={5:0.0}ms（{6} 次）"
                + " / 音频延迟补偿 {7:0.0}ms（DSP {8:0.0}ms）",
                fps, HighRefreshRate.SampleCurrentHz(), Application.targetFrameRate, Time.unscaledDeltaTime * 1000f,
                avg, perfStaleMax, perfStaleCount,
                AudioLatency.CompensationSeconds * 1000f, AudioLatency.DspBufferSeconds * 1000f));

            perfWindowStart = Time.unscaledTime;
            perfFrames = 0;
            perfStaleSum = 0f;
            perfStaleCount = 0;
            perfStaleMax = 0f;
        }

        /// <summary>
        /// 曲终结算一个命中误差样本（照抄 osu!：一局产生一个 DataPoint）。
        /// 判定数不足 `HitErrorTracker.MinJudgeCountPerPlay` 就直接丢弃 —— 对应 osu! 里
        /// `if (newScore.HitEvents.Count < 50) return;`（门槛来自配置，见 HitErrorTracker 类注释的差异①）。
        /// </summary>
        private void FlushHitErrorSample()
        {
            hitErrorSampleFlushed = true;

            HitErrorTracker.Sample sample;
            if (!HitErrorTracker.TryCreateSample(sessionHitErrorsMs, CalibrationSettings.TimingAdjustData, out sample))
            {
                if (Debug.isDebugBuild && sessionHitErrorsMs.Count > 0)
                {
                    Debug.Log(string.Format(
                        "[JudgeManager] 命中误差样本不足：本局 {0} 次判定 < 门槛 {1} → 不产生建议（osu! 口径）",
                        sessionHitErrorsMs.Count, HitErrorTracker.MinJudgeCountPerPlay));
                }
                return;
            }

            HitErrorHistory.Add(sample);
            Debug.Log(string.Format(
                "[JudgeManager] 命中误差样本：本局 {0} 次判定，中位误差 {1:0.0}ms（正 = 打晚）"
                + " → 建议偏移 {2:0.0} 帧（{3:0.0}ms）；历史样本 {4}/{5}",
                sessionHitErrorsMs.Count, sample.MedianErrorMs,
                sample.SuggestedFrames, HitErrorTracker.FramesToMs(sample.SuggestedFrames),
                HitErrorHistory.Count, HitErrorTracker.MaxHistory));
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>开发版自测：自动演奏（不开触摸也能把整首判成全 PERFECT → 验 AP 特效）</summary>
        private bool devAutoplay;

        /// <summary>开发版自测：开局 3 秒后使用局内道具（模拟"演奏中使用"这条真实路径）</summary>
        private System.Collections.IEnumerator DevUseItemInPlay(bool energyDrink)
        {
            while (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
                yield return null;
            while (GameManager.Instance.SongTime < 3f) yield return null;

            if (energyDrink) FallenAngel.Core.DemoItems.UseEnergyDrink();
            else FallenAngel.Core.DemoItems.ApplyFragileDebuff();
        }

        /// <summary>开发版自测：开局 3 秒后打开"暂停 + 退出确认"（E3）</summary>
        private System.Collections.IEnumerator DevShowRetireInPlay()
        {
            while (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing)
                yield return null;
            while (GameManager.Instance.SongTime < 3f) yield return null;

            var pause = FindObjectOfType<FallenAngel.UI.PauseController>(true);
            if (pause != null) pause.DevShowRetireConfirm();
            else Debug.LogWarning("[JudgeManager] 开发版自测：场景里没有 PauseController");
        }

        // ── 真机自测开关（只在开发版编译进来；release 包不含这段）──────────────────
        // 为什么需要：MIUI 拦了 `adb shell input`（只剩 monkey 能注入，而 monkey 只能随机点按），
        // 靠随机点按走不到演奏页，判定/输入的设备验证无法复现。
        // 用法：往 persistentDataPath/fa_autostart 写一行内容：
        //   "dense"（或空）→ 合成密集谱（5 轨每 120ms 一个音符、无声、虚拟钟），随便点都能判到
        //   其它内容      → 当作 Resources 谱面名（如 test_chart_silent / test_chart_5k）
        private bool devAutoStartDone;
        private float devHookStartTime = -1f;

        private void DevAutoStartIfRequested()
        {
            if (devAutoStartDone) return;
            if (devHookStartTime < 0f) devHookStartTime = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - devHookStartTime < 2.5f) return;
            devAutoStartDone = true;

            string path = System.IO.Path.Combine(Application.persistentDataPath, "fa_autostart");
            if (!System.IO.File.Exists(path)) return;

            string mode = System.IO.File.ReadAllText(path).Trim();
            var starter = FindObjectOfType<FallenAngel.UI.GameStarter>();

            // 校准界面自测模式：直接打开节奏校准面板（MIUI 拦 adb 注入，菜单点不准）
            if (mode == "calibration")
            {
                // 注意 includeInactive：校准面板初始 SetActive(false)，只找激活对象会找不到
                var cal = FindObjectOfType<FallenAngel.UI.CalibrationController>(true);
                if (cal != null)
                {
                    Debug.Log("[JudgeManager] 开发版自测：直接打开节奏校准面板");
                    cal.Open();
                }
                else
                {
                    Debug.LogWarning("[JudgeManager] 开发版自测：场景里没有 CalibrationController");
                }
                return;
            }

            // ── 下面三个是 2026-09-22 加的面板自测（MIUI 拦 adb 触摸注入，只能靠钩子把面板叫出来）──
            if (mode == "settings")
            {
                var panel = FindObjectOfType<FallenAngel.UI.SettingsPanelController>(true);
                if (panel != null) { Debug.Log("[JudgeManager] 开发版自测：打开设置页"); panel.Open(); }
                else Debug.LogWarning("[JudgeManager] 开发版自测：场景里没有 SettingsPanelController");
                return;
            }

            if (mode == "fallpreview")
            {
                var preview = FindObjectOfType<FallenAngel.UI.FallSpeedPreviewController>(true);
                if (preview != null) { Debug.Log("[JudgeManager] 开发版自测：打开下落速度预览"); preview.Open(); }
                else Debug.LogWarning("[JudgeManager] 开发版自测：场景里没有 FallSpeedPreviewController");
                return;
            }

            if (mode == "suggest")
            {
                // 只验"建议值能不能显示出来"这条界面链（真实采样由 JudgeTimingChecks 的端到端断言保证）：
                // 灌 3 个中位误差 +30ms 的样本 → 设置页应显示"系统建议 −30 ms（3 局统计）"
                for (int i = 0; i < 3; i++) HitErrorHistory.Add(new HitErrorTracker.Sample(30f, 0f));
                Debug.Log(string.Format("[JudgeManager] 开发版自测：灌入 {0} 个命中误差样本（中位 +30ms）", HitErrorHistory.Count));
                var panel = FindObjectOfType<FallenAngel.UI.SettingsPanelController>(true);
                if (panel != null) panel.Open();
                return;
            }

            // 局内道具链路自测（作者：cut-in / fever 都是为局内道具、buff/debuff 服务的）
            if (mode == "item" || mode == "debuff")
            {
                if (starter == null)
                {
                    Debug.LogWarning("[JudgeManager] 开发版自动开局失败：场景里没有 GameStarter");
                    return;
                }
                Debug.Log("[JudgeManager] 开发版自动开局：合成密集谱 + 局内道具（" + mode + "，开局 3 秒后使用）");
                starter.StartChart(BuildDenseDeviceTestChart());
                StartCoroutine(DevUseItemInPlay(mode == "item"));
                return;
            }

            // E3 自测：开局 3 秒后打开"暂停 + 退出确认"（弹窗类改动在 MIUI 上点不到，只能靠钩子）
            if (mode == "retire")
            {
                if (starter == null)
                {
                    Debug.LogWarning("[JudgeManager] 开发版自动开局失败：场景里没有 GameStarter");
                    return;
                }
                Debug.Log("[JudgeManager] 开发版自动开局：合成密集谱 + 3 秒后打开退出确认");
                starter.StartChart(BuildDenseDeviceTestChart());
                StartCoroutine(DevShowRetireInPlay());
                return;
            }

            // 自动演奏自测（作者 2026-09-22：手打现有谱面打不出 AP → 用自动脚本验 AP 特效）
            if (mode == "auto" || mode == "autotiny")
            {
                if (starter == null)
                {
                    Debug.LogWarning("[JudgeManager] 开发版自动开局失败：场景里没有 GameStarter");
                    return;
                }
                devAutoplay = true;
                string chartName = mode == "autotiny" ? "test_chart_tiny" : "test_chart_5k";
                Debug.Log("[JudgeManager] 开发版自动开局：自动演奏 " + chartName + "（应全程 Perfect → AP）");
                starter.StartChartFromResources(chartName);
                return;
            }

            if (starter == null)
            {
                Debug.LogWarning("[JudgeManager] 开发版自动开局失败：场景里没有 GameStarter");
                return;
            }

            if (mode == "dense2")
            {
                Debug.Log("[JudgeManager] 开发版自动开局：全类型密集谱（5 轨 / 各类型持续出现 / 无声）");
                starter.StartChart(BuildAllTypesDeviceTestChart());
            }
            else if (mode.Length == 0 || mode == "dense")
            {
                Debug.Log("[JudgeManager] 开发版自动开局：合成密集谱（5 轨 / 120ms / 无声）");
                starter.StartChart(BuildDenseDeviceTestChart());
            }
            else
            {
                Debug.Log("[JudgeManager] 开发版自动开局：" + mode);
                starter.StartChartFromResources(mode);
            }
        }

        /// <summary>合成密集谱：5 轨轮转、每 120ms 一个普通音符，共 300 个（约 36 秒）。</summary>
        private static ChartData BuildDenseDeviceTestChart()
        {
            var chart = new ChartData();
            chart.metadata.songName = "DEV dense（设备判定自测）";
            chart.metadata.songArtist = "device-test";
            chart.metadata.formatVersion = 2;      // ≥2 → 5 键
            chart.metadata.bpm = 120f;
            chart.metadata.audioFileName = "";     // 无音频 → 虚拟钟

            for (int i = 0; i < 300; i++)
            {
                chart.notes.Add(new NoteData(i % 5, 2f + i * 0.12f, Data.NoteType.Normal));
            }

            // Fever 也需要真机可验：给自测谱写一段 fever 事件（谱面事件驱动，见 Core/Fever.cs）
            chart.events = new List<ChartEvent>
            {
                new ChartEvent { time = 10f, type = Core.Fever.BeginEvent, value = 0f },
                new ChartEvent { time = 13f, type = Core.Fever.StartEvent, value = 0f },
                new ChartEvent { time = 23f, type = Core.Fever.EndEvent, value = 0f },
            };
            chart.SortNotes();
            return chart;
        }

        /// <summary>
        /// 全类型密集谱：普通/宽条/长按/滑条/drag/flick 各自在自己的轨道上持续出现，
        /// 让"随机手势 + 真机触摸"也能覆盖到每一种音符的判定分支。
        /// </summary>
        private static ChartData BuildAllTypesDeviceTestChart()
        {
            var chart = new ChartData();
            chart.metadata.songName = "DEV all-types（设备判定自测）";
            chart.metadata.songArtist = "device-test";
            chart.metadata.formatVersion = 2;
            chart.metadata.bpm = 120f;
            chart.metadata.audioFileName = "";

            const float start = 2f;
            for (int i = 0; i < 80; i++)
            {
                float t = start + i * 0.30f;
                chart.notes.Add(new NoteData(i % 5, t, Data.NoteType.Normal));          // 普通
                chart.notes.Add(new NoteData((i + 2) % 5, t + 0.15f, Data.NoteType.Drag)); // drag（摩擦）
            }

            for (int i = 0; i < 40; i++)
            {
                float t = start + i * 0.60f;
                // 甩键：上下交替（覆盖方向正确/错误两条分支）
                chart.notes.Add(new NoteData(i % 5, t + 0.05f, Data.NoteType.Flick, 0f, -1,
                    i % 2 == 0 ? FlickDirection.Up : FlickDirection.Down));
                // 宽条（任意轨按下都能判）
                if (i % 2 == 0)
                    chart.notes.Add(new NoteData(0, t + 0.25f, Data.NoteType.Normal, 0f, -1,
                        FlickDirection.Up, null, true));
            }

            for (int i = 0; i < 16; i++)
            {
                float t = start + i * 1.20f;
                int lane = i % 5;
                // 长按（头 + 视图尾条目）
                chart.notes.Add(new NoteData(lane, t + 0.40f, Data.NoteType.LongStart, 0.50f, 100 + i));
                chart.notes.Add(new NoteData(lane, t + 0.90f, Data.NoteType.LongEnd, 0f, 100 + i));

                // 滑条（路径 1 轨位移，头 + 视图尾条目）
                int slideLane = (i + 1) % 4;
                var path = new System.Collections.Generic.List<SlidePathPoint>
                {
                    new SlidePathPoint { t = 0f, x = slideLane },
                    new SlidePathPoint { t = 0.3f, x = slideLane + 0.5f },
                    new SlidePathPoint { t = 0.6f, x = slideLane + 1f },
                };
                chart.notes.Add(new NoteData(slideLane, t + 0.60f, Data.NoteType.Slide, 0.60f, 200 + i,
                    FlickDirection.Up, path));
                chart.notes.Add(new NoteData(slideLane + 1, t + 1.20f, Data.NoteType.LongEnd, 0f, 200 + i));
            }

            chart.SortNotes();
            return chart;
        }
#endif

        // ── 开局：建谱 + 挂回调 ─────────────────────────────────────────
        private void ResetScoreForNewSong()
        {
            // 新的一局：命中误差重新开始收（osu! 每个 ScoreInfo 独立累计）
            sessionHitErrorsMs.Clear();
            hitErrorSampleFlushed = false;
            PlayLife.Reset();          // 生命值回到 1000（上游 LiveConfig.Life）
            CutinService.Reset();      // 清掉上一局残留的 cut-in 队列与连击里程碑
            ResetStats();
            BuildScore();
        }

        private void ResetStats()
        {
            PerfectCount = GreatCount = GoodCount = BadCount = MissCount = 0;
            Combo = 0;
            MaxCombo = 0;
            Score = 0;
            pendingInputs.Clear();
            extraInputs.Clear();
            lastScreenPos.Clear();
            slideOutOfRange.Clear();
            slideSegmentBroken.Clear();

            OnScoreUpdate?.Invoke(0);
            OnComboUpdate?.Invoke(0, true);
            OnJudgeCountsUpdate?.Invoke(0, 0, 0, 0, 0);
        }

        /// <summary>
        /// 换声部只清输入和长音跟随状态；累计成绩、Fever 与歌曲事件游标保留。
        /// </summary>
        private void OnPartChanged()
        {
            pendingInputs.Clear();
            extraInputs.Clear();
            lastScreenPos.Clear();
            slideOutOfRange.Clear();
            slideSegmentBroken.Clear();
            BuildScore(false);
        }

        private void BuildScore(bool resetEvents = true)
        {
            ChartData chart = GameManager.Instance != null ? GameManager.Instance.ActiveChart : null;
            if (chart == null || chart.notes == null)
            {
                runtime = null;
                Debug.LogWarning("[JudgeManager] 谱面为空，判定簇未装配");
                return;
            }

            int laneCount = Mathf.Max(1, chart.LaneCount);
            laneResolver = LiveLaneResolver.CreateFromLaneLayout(PlayVisualSpec.JudgeLineY, laneCount);
            // 输入区闸门由 InputManager（屏幕下半部）负责；这里不再用"判定线"做第二道闸门，
            // 否则滑条/drag 的手指一越过判定线就整段失效（实机表现 = 不跟手）。
            laneResolver.EnforceJudgmentYGate = false;

            var notes = LiveNoteBuilder.Build(
                chart.notes, laneCount, LiveNoteBuilder.DefaultFlickDistancePixels, false);
            // 说明：以前 4 键谱的 lane 0 会被当成"全宽 kick（任意键可判）"，它的判定跨度铺满所有轨，
            // 于是双押时同刻另一轨的音符会被归属规则挤掉 → "双押有时候判定不上"（作者反馈）。
            // 现在只认谱面显式写的 wide 标记（v2 协议），lane 0 就是普通一条轨。

            liveByChartNote.Clear();
            chartNoteByLive.Clear();
            partRootByLive.Clear();
            slideNotes.Clear();

            var headByLongId = new Dictionary<int, LiveNoteBase>();
            int liveIndex = 0;
            foreach (NoteData data in chart.notes)
            {
                if (data == null) continue;
                if (data.type == Data.NoteType.LongEnd || data.type == Data.NoteType.LongBody)
                {
                    continue;   // 长按的尾/身体只是视图条目，第二个循环里挂到头
                }

                if (liveIndex < notes.Count)
                {
                    var live = notes[liveIndex];
                    liveByChartNote[data] = live;
                    chartNoteByLive[live] = data;
                    if (GameManager.Instance.Parts != null) partRootByLive[live] = data;
                    if (data.type == Data.NoteType.LongStart && data.longNoteId >= 0)
                    {
                        headByLongId[data.longNoteId] = live;
                    }
                    liveIndex++;
                }
            }

            float fallTime = Mathf.Max(0.1f, GameManager.Instance.ActualFallTime);
            if (runtime != null) runtime.JudgeCore.OnUnpicked -= HandleUnpicked;
            runtime = new LiveScoreRuntime { NoteDisplayTimeOffset = fallTime };
            runtime.Setup(notes);
            runtime.JudgeCore.LaneResolver = ResolveLaneFromCanvas;
            runtime.JudgeCore.OnUnpicked += HandleUnpicked;

            foreach (NoteData data in chart.notes)
            {
                if (data == null) continue;
                if (data.type == Data.NoteType.LongEnd || data.type == Data.NoteType.LongBody)
                {
                    if (headByLongId.TryGetValue(data.longNoteId, out var head))
                    {
                        liveByChartNote[data] = head;
                        chartNoteByLive[head] = data;
                    }
                    continue;
                }
            }

            // 判定回调（上游的 NoteBase 回调面）：头/尾/连接点都在这里汇总
            foreach (var live in notes)
            {
                // 顺序要紧：先把连击点挂进去，再挂回调（LongNote 的回调 setter 会向下传播到子音符，
                // 晚挂的子音符就收不到通知 → 连击点静默不涨 combo）
                if (live.Category == NoteCategory.Long && live.ParentNote == null)
                {
                    slideNotes.Add(live);   // 跟手规则用（长按与滑条同族：按住期间的有效跨度）
                    AddHoldComboTicks(live);   // 长音持续反馈（见方法注释）
                }
                BindLiveNote(live);
            }

        /// <summary>
        /// 给长音插"连击点"（上游 LongHoldCombo 的用法）：按住期间每 0.4s 一个点，
        /// 子音符还活着（= 手指还在条上）就 JustPerfect、掉指就 Miss。
        /// 计分层对这些点只涨 combo（不计分、不进判定计数），所以不会改变音符总数口径。
        /// 为什么需要：长条头按设计不计分，尾巴在最后才结算 —— 长音按住期间原来"什么都没发生"。
        /// </summary>
        // 局部函数（写在 BuildScore 内）：静态 + 无访问修饰符才合法
        static void AddHoldComboTicks(LiveNoteBase root)
        {
            var head = root as LiveLongNote;
            if (head == null || head.NoteList == null || head.NoteList.Count < 2) return;

            float start = head.MusicScoreInfo.time;
            float end = head.NoteList[head.NoteList.Count - 1].MusicScoreInfo.time;
            if (end - start < 1.2f) return;      // 短条本来就有及时反馈，不插点

            for (float t = start + 0.4f; t < end - 0.15f; t += 0.4f)
            {
                head.AddHoldCombo(new LiveLongHoldCombo(LiveNoteBuilder.Info(t),
                    FallenAngel.Live.NoteType.Default, 1f, LiveNoteBuilder.DefaultLaneOffset));
            }
        }

            Debug.Log(string.Format("[JudgeManager] 判定簇装配完成：谱面 {0} 条 → 判定音符树 {1} 棵（轨道 {2}，在屏 {3:0.##}s）",
                chart.notes.Count, notes.Count, laneCount, fallTime));

            // 谱面事件通道：Fever 等子系统从这里收事件（v2 events；v1 谱为 null → 空表）
            if (!resetEvents) return;
            liveEvents.Load(GameManager.Instance.CurrentChart.events);
            Fever.Register(liveEvents);
            Fever.Reset();
            Debug.Log(string.Format("[JudgeManager] 谱面事件通道：{0} 条事件（剩余待派发 {1}）",
                chart.events != null ? chart.events.Count : 0, liveEvents.PendingCount));
        }

        private void BindLiveNote(LiveNoteBase note)
        {
            if (note == null) return;
            note.OnJudgment = HandleLiveJudgment;
            note.OnDamage = _ => { };
            note.OnUpdateCombo = _ => { };
            note.OnUpdateScore = _ => { };
        }

        // ── 输入装配：InputManager → LiveTouch ─────────────────────────
        /// <summary>
        /// 输入事件先记账（带事件时间戳与手指坐标），Update 里统一装配成这一帧的输入表。
        /// 为什么不在事件里直接判：移植层的判定是"每帧一次"的批处理（候选 → 归属 → 去重 → 拾取 → 应用），
        /// 事件里判会丢掉"同一帧多指竞争"的信息。
        /// </summary>
        private void HandleLaneInput(object sender, LaneInputArgs e)
        {
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentState != GameState.Playing) return;

            var pending = new PendingInput
            {
                // 按下边沿 → Began；按住中移动 → Moved（flick 的位移累计靠它）；抬起 → Ended
                phase = !e.isPressed ? LiveTouchPhase.Ended
                    : (e.isMove ? LiveTouchPhase.Moved : LiveTouchPhase.Began),
                musicTime = e.musicTime,
                screenPos = e.touchPos,
                fromTouch = e.swipeKnown,
                lane = e.laneIndex
            };

            // 同一帧里"先按下、后移动"：只保留按下边沿（Moved 会覆盖掉 Began，
            // 而 Began 才是一次输入的起点 —— flick 也要求先有 Began 注册这一指）
            if (e.isMove && pendingInputs.TryGetValue(e.fingerId, out PendingInput held)
                && held.phase == LiveTouchPhase.Began)
            {
                return;
            }

            // 同一帧内"按下又抬起"（极短点按）：两条都得发，否则这次点按整条丢掉
            if (pendingInputs.TryGetValue(e.fingerId, out PendingInput existing)
                && existing.phase == LiveTouchPhase.Began && !e.isPressed)
            {
                extraInputs.Add(new KeyValuePair<int, PendingInput>(e.fingerId, pending));
                return;
            }

            pendingInputs[e.fingerId] = pending;
        }

        private void BuildFrameTouches(float songTime)
        {
            frameTouches.Clear();
            var input = InputManager.Instance;
            if (input == null) return;

            foreach (var pair in pendingInputs)
            {
                frameTouches.Add(MakeTouch(pair.Key, pair.Value.lane, pair.Value.phase, pair.Value.musicTime,
                    pair.Value.screenPos, pair.Value.fromTouch, songTime));
                RecordInputStaleness(songTime, pair.Value);
            }
            // 同帧的"按下 → 抬起"补发（顺序在按下之后，移植层才会先判头、再判尾）
            for (int i = 0; i < extraInputs.Count; i++)
            {
                var extra = extraInputs[i];
                frameTouches.Add(MakeTouch(extra.Key, extra.Value.lane, extra.Value.phase, extra.Value.musicTime,
                    extra.Value.screenPos, extra.Value.fromTouch, songTime));
                RecordInputStaleness(songTime, extra.Value);
            }

            // 仍按住、且本帧没有事件的轨道 → 持续输入（移植层靠这些帧维持长按/摩擦的"按住中"）
            bool[] press = input.LanePressStates;
            if (press == null) return;
            for (int lane = 0; lane < press.Length; lane++)
            {
                if (!press[lane]) continue;
                int finger = input.FingerIdForLane(lane);
                if (pendingInputs.ContainsKey(finger)) continue;

                input.TryGetFingerScreenPosition(finger, lane, out Vector2 pos);
                // 触屏手指（fingerId ≥ 0）仍然要按"触屏语义"装配：位置可信 → 走小数轨道解析、
                // 甩键方向校验也生效。键盘（-(lane+2)）/鼠标（-1）才是键鼠语义。
                bool isTouchFinger = finger >= 0;
                frameTouches.Add(MakeTouch(finger, lane, LiveTouchPhase.Stationary, songTime, pos,
                    isTouchFinger, songTime));
            }
        }

        private LiveTouch MakeTouch(int fingerId, int lane, LiveTouchPhase phase, float musicTime,
            Vector2 screenPos, bool fromTouch, float songTime)
        {
            // touchId：触屏用 fingerId（InputManager 的 fingerId 就是 touchId）；键盘/鼠标每轨一个合成 id
            int touchId = fingerId >= 0 ? fingerId : -(lane + 1) * 100 - 7;

            // 小数轨道：触屏按手指坐标解析（含"判定线上方不算"的闸门）；键盘/鼠标退化为整数轨道
            float laneCoord = lane;
            if (fromTouch && InputManager.Instance != null &&
                InputManager.Instance.TryGetCanvasPosition(screenPos, out Vector2 canvasPos))
            {
                float resolved = laneResolver != null
                    ? laneResolver.Resolve(new Vector3(canvasPos.x, canvasPos.y, 0f), true)
                    : -1f;
                laneCoord = resolved;   // <0 = 判定区之外（移植层会拒绝这次输入）

                // 真机排查：触摸被判到"判定区之外"时留痕（节流 0.25s，避免刷屏）
                if (laneCoord < 0f && Debug.isDebugBuild
                    && Time.realtimeSinceStartup - lastOutsideLogTime > 0.25f)
                {
                    lastOutsideLogTime = Time.realtimeSinceStartup;
                    Debug.Log(string.Format(
                        "[JudgeManager] 触摸在判定区外（线上方或越轨）：屏 {0:0}×{1:0} / 画布 {2:0}×{3:0} / 判定线画布 Y={4:0}",
                        screenPos.x, screenPos.y, canvasPos.x, canvasPos.y,
                        laneResolver != null ? laneResolver.JudgeLineY : 0f));
                }
            }

            // 真机排查：每次"按下"记一行落点（release 里不输出）
            if (Debug.isDebugBuild && phase == LiveTouchPhase.Began)
            {
                Debug.Log(string.Format(
                    "[JudgeManager] 触摸按下：屏 {0:0}×{1:0} → 轨道 {2:0.###}（{3}）",
                    screenPos.x, screenPos.y, laneCoord, fromTouch ? "触屏" : "键鼠"));
            }

            Vector2 delta = Vector2.zero;
            if (lastScreenPos.TryGetValue(touchId, out Vector2 previous))
            {
                delta = screenPos - previous;
            }
            lastScreenPos[touchId] = screenPos;
            if (phase == LiveTouchPhase.Ended || phase == LiveTouchPhase.Canceled)
            {
                lastScreenPos.Remove(touchId);
            }

            return new LiveTouch(fingerId, touchId, delta, new Vector3(laneCoord, PlayVisualSpec.JudgeLineY, 0f),
                phase, musicTime, laneCoord, screenPos, fromTouch);
        }

        /// <summary>
        /// 输入滞后 = 判定时刻的歌曲时间 − 该事件自己的时间戳（毫秒）。
        /// 这是"跟手"的直接指标：≈0 表示判定用的是触摸**发生**的时刻（上游的做法），
        /// 明显偏大说明我们读到的输入比手指晚（丢帧、或帧率不足）。
        /// </summary>
        private void RecordInputStaleness(float songTime, PendingInput input)
        {
            if (!Debug.isDebugBuild) return;
            if (input.phase != LiveTouchPhase.Began && input.phase != LiveTouchPhase.Ended) return;

            float ms = (songTime - input.musicTime) * 1000f;
            if (ms < 0f) ms = 0f;
            perfStaleSum += ms;
            perfStaleCount++;
            if (ms > perfStaleMax) perfStaleMax = ms;
        }

        /// <summary>左向甩键的"重新取轨道"入口（移植层 ConvertLaneBy 用得到）</summary>
        private float ResolveLaneFromCanvas(Vector3 position, bool checkJudgmentY)
        {
            return laneResolver != null ? laneResolver.Resolve(position, checkJudgmentY) : -1f;
        }

        private void HandleUnpicked(int lane, LiveTouch touch)
        {
            // 空按反馈（移植层 Unpicked）：留给 HUD 的按键涟漪，不参与计分
        }

        // ── 判定结果 → 计分/连击/HUD/视图 ──────────────────────────────
        /// <summary>
        /// 移植层的判定回调：音符产生结果时触发（输入判掉、过窗 Miss、长按收口都走这里）。
        /// </summary>
        private void HandleLiveJudgment(LiveNoteBase note)
        {
            if (note == null) return;

            bool isHoldHead = note.ParentNote == null && note.Category == NoteCategory.Long;
            bool isFriction = note.Category == NoteCategory.Friction || note.Category == NoteCategory.FrictionHide;
            JudgeResultType result = MapResult(note);
            int lane = Mathf.RoundToInt((note.LaneStartF + note.LaneEndF) * 0.5f);

            // 前端判定文字/描述：照上游"每个音符判完都显示"（在计分门控之前抛）
            OnJudgeJudged?.Invoke(note.Result, note.Description);

            if (Debug.isDebugBuild)
            {
                Debug.Log(string.Format(
                    "[JudgeManager] 判定 {0} lane={1} 类别={2} 头={3} 连击={4} 分={5} P/G/Gd/B/M={6}/{7}/{8}/{9}/{10}",
                    result, lane, note.Category, isHoldHead, Combo, Score,
                    PerfectCount, GreatCount, GoodCount, BadCount, MissCount));
            }

            // 本工程协议：drag（摩擦）碰即 Perfect、永不 MISS —— 判定层照上游给你结果，
            // 计分层把它的 Miss 吸收掉（不计 Miss、不出特效，交给视图静默回收）。
            if (isFriction && result == JudgeResultType.Miss) return;

            bool counts = !isHoldHead
                && note.Category != NoteCategory.Connection
                && note.Category != NoteCategory.Combo;

            var partSession = GameManager.Instance?.Parts;
            NoteData partRootData = null;
            if (partSession != null)
            {
                var root = note;
                while (root.ParentNote is LiveNoteBase parent) root = parent;
                partRootByLive.TryGetValue(root, out partRootData);
                partSession.Consume(partRootData);
                if (counts)
                    partSession.Reward(partRootData, result, PlayRules.EnergyMultiplier, GameManager.Instance.SongTime);
            }

            // osu! 式命中误差采样：只收"玩家真实计时输入（HasJudgedError）+ 计分音符 + 真判到、不是 Miss"。
            // 长按头/连接点/连击点都不进统计，否则它们的"零误差"会把中位数拽偏。
            if (counts && note.HasJudgedError && result != JudgeResultType.Miss)
            {
                sessionHitErrorsMs.Add(note.JudgedErrorSeconds * 1000f);
            }

            float bias = BiasOf(note);
            Note view = ViewOf(note);

            GameManager.Instance?.Portfolio?.ReportJudge(ChartDataOf(note), result);

            if (counts)
            {
                if (result == JudgeResultType.Miss)
                {
                    // 生命值（前端对照清单 B4）：Miss −80（上游 LiveConfig.Damages），归零不结算、继续打完
                    PlayLife.ApplyDamage(PlayLife.DamageMiss);
                    // 声部账本：漏掉的算在哪一段（原型验证 A 项；生命只在上面扣一次）
                    partSession?.Miss(partRootData, PlayLife.DamageMiss);
                    ProcessMiss(lane);
                    view?.JudgeMiss();
                    OnJudgeResult?.Invoke(result, lane);
                    PlayAudioJudge(result);
                    return;
                }

                if (JudgeWindows.BreaksCombo(result))
                {
                    // Bad −60（上游 LiveConfig.Damages）
                    if (result == JudgeResultType.Bad) PlayLife.ApplyDamage(PlayLife.DamageBad);
                    BreakCombo();
                    AddJudgeCount(result);
                }
                else
                {
                    // 顺序要紧：**先更新判定计数，再涨连击**。
                    // 因为 AddScoreAndCombo 内部会抛 OnComboUpdate → HUD/ComboView 在那里判断 AP
                    // （全 Perfect）；反过来写的话第一颗音符永远是 AP=False（真机日志抓到过：
                    // combo=1 AP=False，从第 2 颗起才 True）。
                    AddJudgeCount(result);
                    AddScoreAndCombo(JudgeWindows.GetScore(result));
                }
                ApplyViewResult(view, note, result);
                OnJudgeResult?.Invoke(result, lane);
                OnJudgeBias?.Invoke(bias);
                PlayAudioJudge(result);
                return;
            }

            // 长按头 / 连接点：只做视觉与连击（不计分、不进判定计数）
            if (result == JudgeResultType.Miss)
            {
                ApplyViewResult(view, note, result);
                return;
            }

            if (JudgeWindows.BreaksCombo(result))
            {
                BreakCombo();
            }
            else if (note.Category == NoteCategory.Combo)
            {
                // 长音上的连击点：只涨 combo（不计分、不进判定计数）—— 按住长音期间的持续反馈
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
                OnComboUpdate?.Invoke(Combo, MissCount == 0);
                CutinService.NotifyCombo(Combo);      // 连击里程碑 cut-in（对应上游 IngameComboCutin）
            }
            ApplyViewResult(view, note, result);
            OnJudgeBias?.Invoke(bias);
        }

        /// <summary>判定结果落到视图：长按尾走"收尾"特效，其余走命中/漏掉</summary>
        private static void ApplyViewResult(Note view, LiveNoteBase note, JudgeResultType result)
        {
            if (view == null) return;
            if (note.ParentNote != null && note.Category == NoteCategory.Long)
            {
                view.JudgeHoldEnd(result);
            }
            else if (result == JudgeResultType.Miss)
            {
                view.JudgeMiss();
            }
            else
            {
                view.JudgeHit(result);
            }
        }

        private Note ViewOf(LiveNoteBase note)
        {
            var spawner = NoteSpawner.Instance;
            if (spawner == null) return null;
            var root = note;
            while (root != null && root.ParentNote is LiveNoteBase parent)
            {
                root = parent;
            }
            return spawner.FindViewNote(root);
        }

        private NoteData ChartDataOf(LiveNoteBase note)
        {
            NoteData data;
            return note != null && chartNoteByLive.TryGetValue(note, out data) ? data : null;
        }

        /// <summary>移植层结果 → 本工程的判定档（JustPerfect 折成 Perfect；Pass/Auto 不当判定用）</summary>
        private static JudgeResultType MapResult(LiveNoteBase note)
        {
            switch (note.Result)
            {
                case NoteResult.JustPerfect:
                case NoteResult.Perfect:
                // 自动演奏（AutoJudgment）在原作里就是全 PERFECT —— 映射成 Perfect，
                // 这样"自动脚本跑一遍"就能真的触发 AP（All Perfect）表现，用于 B3 验收。
                case NoteResult.Auto:
                    return JudgeResultType.Perfect;
                case NoteResult.Great:
                    return JudgeResultType.Great;
                case NoteResult.Good:
                    return JudgeResultType.Good;
                case NoteResult.Bad:
                    return JudgeResultType.Bad;
                case NoteResult.Miss:
                    return JudgeResultType.Miss;
                default:
                    return JudgeResultType.None;
            }
        }

        /// <summary>早/晚指示：移植层的描述就是档位（Just/Fast/Late），幅度不可得时为 0</summary>
        private static float BiasOf(LiveNoteBase note)
        {
            switch (note.Description)
            {
                case NoteResultDescription.Fast:
                    return 0.02f;
                case NoteResultDescription.Late:
                    return -0.02f;
                default:
                    return 0f;
            }
        }

        // ── 滑条跟手（FallenAngel 自有规则：脱离不断整条，只断 combo）────────
        private void UpdateSlideFollow(float songTime)
        {
            if (slideNotes.Count == 0 || InputManager.Instance == null) return;
            bool[] press = InputManager.Instance.LanePressStates;
            if (press == null) return;

            for (int i = 0; i < slideNotes.Count; i++)
            {
                var slide = slideNotes[i];
                if (slide == null || slide.State == NoteState.Done) continue;
                if (slide.State != NoteState.InputBegan && slide.State != NoteState.Input
                    && slide.State != NoteState.Release) continue;

                // 判定跨度由移植层沿"头 → 尾"推进（LiveUtility.CalcExcuteNoteLane）
                float center = (slide.LaneStartF + slide.LaneEndF) * 0.5f;
                bool following = false;
                for (int lane = 0; lane < press.Length; lane++)
                {
                    if (press[lane] && Mathf.Abs(lane - center) <= SlideFollowToleranceLanes)
                    {
                        following = true;
                        break;
                    }
                }

                if (following)
                {
                    slideOutOfRange.Remove(slide);
                    continue;
                }

                if (!slideOutOfRange.ContainsKey(slide)) slideOutOfRange[slide] = songTime;
                if (songTime - slideOutOfRange[slide] < SlideBreakSeconds) continue;

                if (slideSegmentBroken.Add(slide))
                {
                    BreakCombo();
                    Debug.Log($"[JudgeManager] 滑条掉段（偏离 {songTime - slideOutOfRange[slide]:F2}s）：断 combo、不断整条");
                }
                slideOutOfRange.Remove(slide);   // 重置计时，给手指回到跨度内的机会
            }
        }

        // ── 计分/连击/计数 ─────────────────────────────────────────────
        private void ProcessMiss(int lane)
        {
            MissCount++;
            BreakCombo();
            OnJudgeCountsUpdate?.Invoke(PerfectCount, GreatCount, GoodCount, BadCount, MissCount);
        }

        /// <summary>断连击（Miss/Good/Bad 共用；不增加 Miss 计数）</summary>
        private void BreakCombo()
        {
            Combo = 0;
            OnComboUpdate?.Invoke(Combo, false);
        }

        private void AddScoreAndCombo(int score)
        {
            // 分数倍率：Fever 与局内道具都通过 ModifierManager 挂到 PlayRules.ScoreMultiplier（判定层不硬编码）
            score = Mathf.RoundToInt(score * Mathf.Max(0f, PlayRules.ScoreMultiplier));
            Score += score;
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            CutinService.NotifyCombo(Combo);          // 连击里程碑 cut-in

            // 连击加成（简化版）
            int comboBonus = Mathf.RoundToInt(score * (Mathf.Clamp01(Combo / 100f) * 0.2f));
            Score += comboBonus;

            OnScoreUpdate?.Invoke(Score);
            bool isFull = MissCount == 0;
            OnComboUpdate?.Invoke(Combo, isFull);
        }

        private void AddJudgeCount(JudgeResultType result)
        {
            switch (result)
            {
                case JudgeResultType.Perfect: PerfectCount++; break;
                case JudgeResultType.Great: GreatCount++; break;
                case JudgeResultType.Good: GoodCount++; break;
                case JudgeResultType.Bad: BadCount++; break;
            }
            OnJudgeCountsUpdate?.Invoke(PerfectCount, GreatCount, GoodCount, BadCount, MissCount);
        }

        private void PlayAudioJudge(JudgeResultType result)
        {
            if (Audio.AudioManager.Instance != null)
                Audio.AudioManager.Instance.PlayHitSfx(result);
        }

        /// <summary>
        /// 计算当前的达成率（百分比，0~100）。权重与计分语义一致（Phigros 对齐）：Great≈65%，Good/Bad 0
        /// </summary>
        public float CalculateAccuracy()
        {
            float total = PerfectCount + GreatCount + GoodCount + BadCount + MissCount;
            if (total <= 0) return 100f;

            float weightSum =
                PerfectCount * 100f +
                GreatCount * 65f +
                GoodCount * 0f +
                BadCount * 0f +
                MissCount * 0f;
            return weightSum / total;
        }

        /// <summary>获取评级字符串 (S~D)</summary>
        public string GetRank()
        {
            float acc = CalculateAccuracy();
            if (MissCount == 0 && PerfectCount >= GreatCount + GoodCount + BadCount) return "S";
            if (acc >= 95f) return "A";
            if (acc >= 85f) return "B";
            if (acc >= 70f) return "C";
            return "D";
        }

        /// <summary>兼容旧调用：已经由移植层自动 Miss，这里只做计分侧登记</summary>
        public void HandleAutoMiss(Note note)
        {
            if (note == null) return;
            ProcessMiss(note.Data != null ? note.Data.lane : -1);
            OnJudgeResult?.Invoke(JudgeResultType.Miss, note.Data != null ? note.Data.lane : -1);
            PlayAudioJudge(JudgeResultType.Miss);
        }

        private void OnDestroy()
        {
            if (runtime != null && runtime.JudgeCore != null)
                runtime.JudgeCore.OnUnpicked -= HandleUnpicked;
            if (Instance == this) Instance = null;
        }
    }
}
