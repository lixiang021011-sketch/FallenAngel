using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Audio;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 节拍校准控制器（菜单入口）——下落式 + 观察式手动校准（Phigros 同款思路）：
    ///   1. 音符持续下落，到达判定线瞬间响滴答（音画在同一时钟时刻调度）
    ///   2. 玩家观察音画是否重合：声音比音符晚 → +5ms；声音比音符早 → -5ms
    ///   3. 偏移实时生效并持久化（CalibrationSettings，PlayerPrefs）
    /// 相比"点击自动校准"，观察式测量的是纯设备音频输出延迟
    /// （点击式会混入个人反应/预判习惯，且不依赖点击、更稳定）。
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        [Header("面板引用")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI statusText;    // 状态/提示文本（运行时动态）
        [SerializeField] private TextMeshProUGUI offsetText;    // 当前偏移显示
        [SerializeField] private RectTransform judgeLine;       // 判定线（音符到达位置）

        [Header("按钮")]
        [SerializeField] private Button startStopButton;        // 开始观察/停止观察
        [SerializeField] private TextMeshProUGUI startStopLabel; // 该按钮的动态 label
        [SerializeField] private Button plusButton;             // +5ms
        [SerializeField] private Button minusButton;            // -5ms
        [SerializeField] private Button closeButton;

        [Header("观察参数")]
        [SerializeField] private float tickInterval = 1.5f;     // 相邻音符间隔（秒）
        [SerializeField] private float fallTime = 1.2f;         // 音符下落时长（秒）

        [Header("音符外观")]
        [SerializeField] private Color noteColor = new Color(0.45f, 0.8f, 1f, 0.95f);
        [SerializeField] private Vector2 noteSize = new Vector2(60, 60);
        private const float FallDistance = 576f; // 出生点到判定线的面板本地距离（与 SceneBuilder 布局对应）

        private AudioSource testSource;
        private AudioClip tickClip;
        private Coroutine observeCoroutine;
        private bool observing;

        private readonly List<RectTransform> notePool = new List<RectTransform>();
        private const int PoolSize = 2;

        private float judgeY;

        private void Awake()
        {
            // 面板首次激活时执行（面板初始非激活；入口按钮由 GameStarter 接）
            if (startStopButton != null) startStopButton.onClick.AddListener(ToggleObserving);
            if (plusButton != null) plusButton.onClick.AddListener(() => AdjustOffset(5));
            if (minusButton != null) minusButton.onClick.AddListener(() => AdjustOffset(-5));
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            Loc.OnLanguageChanged += RefreshDynamicTexts;
        }

        private void Start()
        {
            testSource = gameObject.AddComponent<AudioSource>();
            testSource.playOnAwake = false;
            testSource.loop = false;
            tickClip = SynthesizedSfx.CreateHitClip(1200f, 0.05f, 28f); // 短促高频滴答

            judgeY = judgeLine != null ? judgeLine.localPosition.y : -346f;

            // 音符池
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject go = new GameObject($"CalNote_{i}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = noteSize;
                Image img = go.GetComponent<Image>();
                img.color = noteColor;
                img.raycastTarget = false;
                go.SetActive(false);
                notePool.Add(rt);
            }
            RefreshOffsetText();
            RefreshDynamicTexts();
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged += RefreshDynamicTexts;
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= RefreshDynamicTexts;
            StopObserving();
        }

        private void Update()
        {
            CollectTaps();
            // ESC 随时可关闭面板（观察中也可退出）
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
        }

        /// <summary>打开面板（菜单按钮，GameStarter 接线）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshOffsetText();
            RefreshDynamicTexts();
        }

        /// <summary>关闭面板</summary>
        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            StopObserving();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>开始/停止观察</summary>
        public void ToggleObserving()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (observing) StopObserving();
            else StartMeasuring();
        }

        // ── 跟拍点击测量（2026-09-21 重做；口径对齐判定簇 + 输出延迟补偿）──────────────
        // 旧版是"观察式"：音符下落 + 滴答按偏移提前/推迟，靠人耳听感 ±5ms 微调。
        // 判定改成"按事件时间戳"、音画又接上输出延迟补偿之后，那套闭环与游戏实际口径脱节，
        // 玩家怎么调都不对（作者反馈"校准变成摆设"）。
        // 现在改成**跟拍点击**：节拍响 8 次，玩家跟着点屏幕任意处；
        // 取"点击时刻 − 该拍时刻"的**修剪平均**（两端各剔 25%）作为建议值，直接写进 CalibrationSettings。
        private const float MeasureBeatInterval = 0.60f;
        private const int MeasureBeatCount = 8;

        private readonly List<double> tapOffsets = new List<double>();
        private readonly List<double> beatTimes = new List<double>();
        private Coroutine measureCoroutine;
        private bool measuring;

        private void StartMeasuring()
        {
            observing = true;                     // 复用状态位：按钮 label / Close 逻辑不变
            measuring = true;
            tapOffsets.Clear();
            beatTimes.Clear();
            if (startStopLabel != null) startStopLabel.text = Loc.T("cal.stop");
            if (statusText != null) statusText.text = Loc.T("cal.tapAlong");
            measureCoroutine = StartCoroutine(MeasureLoop());
        }

        /// <summary>节拍循环：每拍前后各留半拍，超出的点击不算</summary>
        private System.Collections.IEnumerator MeasureLoop()
        {
            double start = Time.unscaledTimeAsDouble + 1.0;      // 前导 1 秒让玩家准备
            for (int i = 0; i < MeasureBeatCount; i++)
            {
                if (!measuring) yield break;
                double beat = start + i * MeasureBeatInterval;
                beatTimes.Add(beat);

                while (Time.unscaledTimeAsDouble < beat - 0.05) yield return null;
                testSource.PlayOneShot(tickClip, 0.8f);
                if (statusText != null)
                    statusText.text = Loc.T("cal.tapping", i + 1, MeasureBeatCount, tapOffsets.Count);
                while (Time.unscaledTimeAsDouble < beat + MeasureBeatInterval - 0.05) yield return null;
            }

            FinishMeasuring();
        }

        /// <summary>收尾：修剪平均 → 应用 → 显示（并写明输出延迟是自动补偿的）</summary>
        private void FinishMeasuring()
        {
            measuring = false;
            observing = false;
            measureCoroutine = null;
            if (startStopLabel != null) startStopLabel.text = Loc.T("cal.start");

            int suggested = CalibrationMath.TrimmedMeanMs(tapOffsets);
            CalibrationSettings.OffsetMs = suggested;          // 直接应用，玩家还能 ±5ms 微调
            RefreshOffsetText();
            if (statusText != null)
            {
                statusText.text = tapOffsets.Count > 0
                    ? Loc.T("cal.measuredApplied", tapOffsets.Count, suggested, CalibrationSettings.OffsetMs)
                    : Loc.T("cal.noTap");
            }
            Debug.Log(string.Format(
                "[Calibration] 跟拍测量：{0} 次点击 → 建议 {1}ms，已应用 {2}ms（输出延迟补偿 {3:0}ms 自动加在时钟上）",
                tapOffsets.Count, suggested, CalibrationSettings.OffsetMs, AudioLatency.CompensationSeconds * 1000f));
        }

        /// <summary>测量期间收点击：面板是 UI，不走 InputManager 的轨道输入，所以自己读触摸/鼠标</summary>
        private void CollectTaps()
        {
            if (!measuring) return;

            bool tapped = false;
#if UNITY_EDITOR
            tapped = UnityEngine.Input.GetMouseButtonDown(0);
#else
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                if (UnityEngine.Input.GetTouch(i).phase == TouchPhase.Began) { tapped = true; break; }
            }
#endif
            if (!tapped || beatTimes.Count == 0) return;

            double tapTime = Time.unscaledTimeAsDouble;
            double best = beatTimes[0];
            foreach (double beat in beatTimes)
                if (System.Math.Abs(tapTime - beat) < System.Math.Abs(tapTime - best)) best = beat;

            double offset;
            if (CalibrationMath.TryAssignToBeat(tapTime, best, MeasureBeatInterval, out offset))
                tapOffsets.Add(offset);
        }

        private void StartObserving()
        {
            observing = true;
            if (statusText != null) statusText.text = Loc.T("cal.observing");
            observeCoroutine = StartCoroutine(ObserveLoop());
            RefreshDynamicTexts();
        }

        private void StopObserving()
        {
            observing = false;
            if (observeCoroutine != null)
            {
                StopCoroutine(observeCoroutine);
                observeCoroutine = null;
            }
            foreach (RectTransform note in notePool)
                if (note != null) note.gameObject.SetActive(false);
            RefreshDynamicTexts();
        }

        /// <summary>
        /// 持续下落音符：音符视觉到达判定线为参照，滴答提前 offset 秒播放。
        /// 校准偏移直接决定声音相对音符的早晚（+ = 声音提前 = 音符推迟），
        /// 玩家按 ±5ms 后下一次滴答立刻听感移位——这就是观察式校准的反馈闭环。
        /// </summary>
        private System.Collections.IEnumerator ObserveLoop()
        {
            int idx = 0;
            while (observing)
            {
                RectTransform note = notePool[idx % PoolSize];
                note.gameObject.SetActive(true);

                double spawn = Time.unscaledTimeAsDouble;
                double arrival = spawn + fallTime;
                bool ticked = false;
                while (Time.unscaledTimeAsDouble < arrival)
                {
                    float t = (float)((Time.unscaledTimeAsDouble - spawn) / fallTime);
                    SetNoteProgress(note, Mathf.Clamp01(t));

                    // 滴答在 到达时刻 - offset 播放（当前偏移实时生效）
                    if (!ticked && Time.unscaledTimeAsDouble >= arrival - CalibrationSettings.OffsetSeconds)
                    {
                        testSource.PlayOneShot(tickClip, 0.8f);
                        ticked = true;
                    }
                    yield return null;
                }
                // 负偏移（声音应晚于音符）时滴答时刻在到达之后，继续等待
                while (!ticked)
                {
                    if (Time.unscaledTimeAsDouble >= arrival - CalibrationSettings.OffsetSeconds)
                    {
                        testSource.PlayOneShot(tickClip, 0.8f);
                        ticked = true;
                    }
                    yield return null;
                }

                note.gameObject.SetActive(false); // 到线立即消失
                idx++;

                // 相邻音符间隔（interval > fallTime 时音符间有喘息间隙）
                double next = spawn + tickInterval;
                while (Time.unscaledTimeAsDouble < next)
                    yield return null;
            }
        }

        /// <summary>
        /// 音符下落位置：底边接触判定线即到达（中心目标 = 判定线 + 半个音符高）
        /// </summary>
        private void SetNoteProgress(RectTransform note, float t)
        {
            float half = noteSize.y * 0.5f;
            float y = Mathf.Lerp(judgeY + FallDistance, judgeY + half, t);
            note.localPosition = new Vector3(0f, y, 0f);
        }

        /// <summary>手动微调偏移（观察式校准的核心操作，实时生效）</summary>
        public void AdjustOffset(int deltaMs)
        {
            CalibrationSettings.OffsetMs += deltaMs;
            AudioManager.Instance?.PlayButtonClick();
            RefreshOffsetText();
        }

        private void RefreshOffsetText()
        {
            if (offsetText != null)
                offsetText.text = Loc.T("cal.offset",
                    $"{(CalibrationSettings.OffsetMs >= 0 ? "+" : "")}{CalibrationSettings.OffsetMs}");
        }

        /// <summary>动态文本刷新：开始/停止按钮 label（观察中与空闲不同文案）</summary>
        private void RefreshDynamicTexts()
        {
            if (startStopLabel != null)
                startStopLabel.text = Loc.T(observing ? "cal.observeStop" : "cal.observe");
            if (statusText != null && !observing)
                statusText.text = Loc.T("cal.observeHint");
        }
    }
}
