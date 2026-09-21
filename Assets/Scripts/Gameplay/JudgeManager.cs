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
            }
        }

        private void Update()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevAutoStartIfRequested();
#endif

            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing) return;
            if (runtime == null || runtime.Notes.Count == 0) return;

            float songTime = GameManager.Instance.SongTime;
            // 局内 modifier 的判定窗口倍率（基础值 × 倍率；移植层查表时套用）
            LiveConfig.SetJudgeTimeScale(PlayRules.JudgeWindowScale);
            // 下落速度可能在开局后被改（设置面板/调速）；每帧同步一次，
            // 否则判定层还在用开局时的在屏时长 → "调整下落速度后的体验很奇怪"。
            runtime.NoteDisplayTimeOffset = Mathf.Max(0.1f, GameManager.Instance.ActualFallTime);

            runtime.Update(LiveNoteBuilder.Info(songTime));
            BuildFrameTouches(songTime);
            runtime.Judge(frameTouches);
            pendingInputs.Clear();
            extraInputs.Clear();

            UpdateSlideFollow(songTime);
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
                "[Perf] fps={0:0.0}（显示 {1}Hz / target {2}） 输入滞后 avg={3:0.0}ms max={4:0.0}ms（{5} 次事件输入）",
                fps, HighRefreshRate.SampleCurrentHz(), Application.targetFrameRate, avg, perfStaleMax, perfStaleCount));

            perfWindowStart = Time.unscaledTime;
            perfFrames = 0;
            perfStaleSum = 0f;
            perfStaleCount = 0;
            perfStaleMax = 0f;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
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
        /// 谱面 → 判定音符树 → 运行时。旋律时间统一取 SongTime，通过 MusicScoreInfo.time 传给判定簇。
        /// </summary>
        private void BuildScore()
        {
            ChartData chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
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
                    if (data.type == Data.NoteType.LongStart && data.longNoteId >= 0)
                    {
                        headByLongId[data.longNoteId] = live;
                    }
                    liveIndex++;
                }
            }

            float fallTime = Mathf.Max(0.1f, GameManager.Instance.ActualFallTime);
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
                BindLiveNote(live);
                if (live.Category == NoteCategory.Long && live.ParentNote == null)
                {
                    slideNotes.Add(live);   // 跟手规则用（长按与滑条同族：按住期间的有效跨度）
                }
            }

            Debug.Log(string.Format("[JudgeManager] 判定簇装配完成：谱面 {0} 条 → 判定音符树 {1} 棵（轨道 {2}，在屏 {3:0.##}s）",
                chart.notes.Count, notes.Count, laneCount, fallTime));
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
                phase = e.isPressed ? LiveTouchPhase.Began : LiveTouchPhase.Ended,
                musicTime = e.musicTime,
                screenPos = e.touchPos,
                fromTouch = e.swipeKnown,
                lane = e.laneIndex
            };

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

            float bias = BiasOf(note);
            Note view = ViewOf(note);

            GameManager.Instance?.Portfolio?.ReportJudge(ChartDataOf(note), result);

            if (counts)
            {
                if (result == JudgeResultType.Miss)
                {
                    ProcessMiss(lane);
                    view?.JudgeMiss();
                    OnJudgeResult?.Invoke(result, lane);
                    PlayAudioJudge(result);
                    return;
                }

                if (JudgeWindows.BreaksCombo(result))
                {
                    BreakCombo();
                }
                else
                {
                    AddScoreAndCombo(JudgeWindows.GetScore(result));
                }
                AddJudgeCount(result);
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
            Score += score;
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;

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
