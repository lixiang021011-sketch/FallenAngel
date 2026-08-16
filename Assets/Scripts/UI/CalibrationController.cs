using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FallenAngel.Audio;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 节拍校准控制器（菜单入口）：
    ///   1. 播放 16 声节拍滴答（程序合成）+ 视觉脉冲
    ///   2. 玩家跟着节拍点击，测量每次点击相对滴答的延迟
    ///   3. 平均延迟 → 推荐校准偏移（正 = 音符推迟，补偿设备音频延迟）
    ///   4. ±5ms 手动微调，写入 CalibrationSettings（PlayerPrefs 持久化）
    /// 注意：测试只反映"听+按"综合延迟，多次测试取平均更稳。
    /// 点击捕获：主路径为 IPointerDownHandler（OS 事件队列驱动，快速轻点不丢帧边沿）；
    /// Update 轮询为兜底（面板外/按钮区域的点击），两路径经 RegisterTap 去重。
    /// </summary>
    public class CalibrationController : MonoBehaviour, IPointerDownHandler
    {
        [Header("面板引用")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI statusText;    // 状态/结果文本
        [SerializeField] private TextMeshProUGUI offsetText;    // 当前偏移显示
        [SerializeField] private TextMeshProUGUI pulseText;     // 视觉脉冲（● 缩放）

        [Header("按钮")]
        [SerializeField] private Button startTestButton; // 开始测试
        [SerializeField] private Button applyButton;     // 应用推荐值
        [SerializeField] private Button plusButton;      // +5ms
        [SerializeField] private Button minusButton;     // -5ms
        [SerializeField] private Button closeButton;     // 关闭

        [Header("测试参数")]
        [SerializeField] private int tickCount = 16;
        [SerializeField] private float tickInterval = 0.6f;
        [Tooltip("点击与最近滴答偏差超过该值不计入（秒）")]
        [SerializeField] private float tapTolerance = 0.4f;
        [Tooltip("测试开始前的准备时长（秒），避免按下按钮瞬间第一声滴答就响")]
        [SerializeField] private float readySeconds = 1.5f;

        private AudioSource testSource;
        private AudioClip tickClip;
        private Coroutine testCoroutine;
        private bool testing;
        private readonly List<double> scheduledTicks = new List<double>(); // unscaledTime 基准
        private readonly List<float> tapDelays = new List<float>();
        private float recommendedMs;
        private Vector3 pulseOriginalScale;
        private float lastTapTime = -99f; // 双路径去重用
        private const float TapDedupeWindow = 0.05f;

        private void Awake()
        {
            // 注意：本组件挂在初始非激活的 CalibrationPanel 上，Awake 在面板
            // 首次激活时才执行（此时按钮监听才被接上——这正是面板打开的正确时机）。
            // 菜单上的入口按钮由 GameStarter 负责接线（GameStarter 始终激活）。
            if (startTestButton != null) startTestButton.onClick.AddListener(StartTest);
            if (applyButton != null) applyButton.onClick.AddListener(ApplyRecommended);
            if (plusButton != null) plusButton.onClick.AddListener(() => AdjustOffset(5));
            if (minusButton != null) minusButton.onClick.AddListener(() => AdjustOffset(-5));
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            testSource = gameObject.AddComponent<AudioSource>();
            testSource.playOnAwake = false;
            testSource.loop = false;
            tickClip = SynthesizedSfx.CreateHitClip(1200f, 0.05f, 28f); // 短促高频滴答

            if (pulseText != null)
            {
                pulseOriginalScale = pulseText.transform.localScale;
                pulseText.text = "";
            }
            // 面板初始状态由 SceneBuilder 控制（非激活），这里不要再关面板，
            // 否则 Open() 激活面板后会被 Start 立刻关掉
            RefreshOffsetText();
        }

        private void Update()
        {
            // ESC 随时可关闭面板（测试中也可退出）
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (!testing) return;

            // 兜底捕获：面板外/按钮区域的点击（主路径是 OnPointerDown，OS 事件驱动不丢帧）
            bool tapped = Input.GetMouseButtonDown(0);
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).phase == TouchPhase.Began) tapped = true;
            }
            if (tapped)
                RegisterTap((float)Time.unscaledTimeAsDouble);
        }

        /// <summary>
        /// 主点击捕获路径：EventSystem 指针按下（OS 事件队列驱动）。
        /// 快速轻点（按下+抬起在一帧内完成）不会被逐帧轮询的 GetMouseButtonDown 捕捉，
        /// 而 OS 事件会完整送达——这就是"轻点不识别、用力按才识别"的根因修复。
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            RegisterTap((float)eventData.clickTime);
        }

        /// <summary>
        /// 登记一次校准点击：与最近滴答关联并记录延迟（双路径 50ms 去重）
        /// </summary>
        private void RegisterTap(float tapTime)
        {
            if (!testing) return;
            if (tapTime - lastTapTime < TapDedupeWindow) return;
            lastTapTime = tapTime;

            double now = tapTime;
            if (scheduledTicks.Count == 0) return;

            // 关联到最近的已播放滴答
            double nearest = scheduledTicks[0];
            double bestDiff = double.MaxValue;
            foreach (double t in scheduledTicks)
            {
                double d = Mathf.Abs((float)(now - t));
                if (d < bestDiff) { bestDiff = d; nearest = t; }
            }
            if (bestDiff > tapTolerance) return;

            tapDelays.Add((float)(now - nearest));
            if (statusText != null)
                statusText.text = Loc.T("cal.collected", tapDelays.Count);
#if UNITY_EDITOR
            Debug.Log($"[CalibrationController] tap #{tapDelays.Count} at {tapTime:F3}, delay {(float)(now - nearest) * 1000f:F0}ms");
#endif
        }

        /// <summary>打开面板（菜单按钮）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshOffsetText();
            SetButtonsInteractable(true);
            if (applyButton != null) applyButton.interactable = false; // 新测试前不可应用旧结果
            if (statusText != null) statusText.text = Loc.T("cal.hint");
        }

        /// <summary>关闭面板</summary>
        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            StopTest();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>开始 16 声节拍测试</summary>
        public void StartTest()
        {
            AudioManager.Instance?.PlayButtonClick();
            StopTest();
            testing = true;
            scheduledTicks.Clear();
            tapDelays.Clear();
            if (statusText != null) statusText.text = Loc.T("cal.testing");
            // 测试期间只禁用"开始测试"与"应用推荐值"；关闭/±5ms 保持可用，避免被困
            if (startTestButton != null) startTestButton.interactable = false;
            if (applyButton != null) applyButton.interactable = false;
            testCoroutine = StartCoroutine(TickLoop());
        }

        private void StopTest()
        {
            testing = false;
            scheduledTicks.Clear();
            if (testCoroutine != null)
            {
                StopCoroutine(testCoroutine);
                testCoroutine = null;
            }
            if (pulseText != null) pulseText.text = "";
        }

        private System.Collections.IEnumerator TickLoop()
        {
            // 准备期：按下"开始测试"后稍等，避免第一声滴答在玩家没准备好时响起
            if (statusText != null)
                statusText.text = Loc.T("cal.ready", (int)Mathf.Ceil(readySeconds));
            double readyEnd = Time.unscaledTimeAsDouble + readySeconds;
            while (Time.unscaledTimeAsDouble < readyEnd)
                yield return null;

            for (int i = 0; i < tickCount; i++)
            {
                double t = Time.unscaledTimeAsDouble;
                scheduledTicks.Add(t);
                testSource.PlayOneShot(tickClip, 0.8f);
                if (pulseText != null)
                {
                    pulseText.text = "●";
                    pulseText.transform.localScale = pulseOriginalScale * 1.4f;
                    if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
                    pulseCoroutine = StartCoroutine(PulseShrink());
                }
                if (statusText != null)
                    statusText.text = Loc.T("cal.progress", i + 1, tickCount, tapDelays.Count);

                // 等待一个间隔（保持节拍稳定，不受渲染帧率影响）
                double next = t + tickInterval;
                while (Time.unscaledTimeAsDouble < next)
                    yield return null;
            }
            FinishTest();
        }

        private Coroutine pulseCoroutine;

        private System.Collections.IEnumerator PulseShrink()
        {
            Transform pt = pulseText.transform;
            float dur = 0.15f;
            float timer = 0f;
            while (timer < dur)
            {
                timer += Time.unscaledDeltaTime;
                pt.localScale = pulseOriginalScale * Mathf.Lerp(1.4f, 1f, timer / dur);
                yield return null;
            }
            pt.localScale = pulseOriginalScale;
            pulseCoroutine = null;
        }

        private void FinishTest()
        {
            testing = false;
            if (pulseText != null) pulseText.text = "";

            if (tapDelays.Count < 5)
            {
                if (statusText != null) statusText.text = Loc.T("cal.tooFew");
                SetButtonsInteractable(true);
                return;
            }

            float sum = 0f;
            foreach (float d in tapDelays) sum += d;
            recommendedMs = sum / tapDelays.Count * 1000f;

            if (statusText != null)
                statusText.text = Loc.T("cal.result", recommendedMs.ToString("F1"), tapDelays.Count);
            if (applyButton != null) applyButton.interactable = true;
            SetButtonsInteractable(true);
        }

        /// <summary>应用推荐偏移</summary>
        public void ApplyRecommended()
        {
            CalibrationSettings.OffsetMs = Mathf.RoundToInt(recommendedMs);
            AudioManager.Instance?.PlayButtonClick();
            RefreshOffsetText();
            if (statusText != null) statusText.text = Loc.T("cal.applied");
        }

        /// <summary>手动微调偏移</summary>
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

        private void SetButtonsInteractable(bool interactable)
        {
            if (startTestButton != null) startTestButton.interactable = interactable;
            if (plusButton != null) plusButton.interactable = interactable;
            if (minusButton != null) minusButton.interactable = interactable;
            if (closeButton != null) closeButton.interactable = interactable;
        }

        private void OnDisable()
        {
            StopTest();
        }
    }
}
