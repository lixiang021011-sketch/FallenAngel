using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        [Header("面板引用")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI statusText;    // 状态/结果文本
        [SerializeField] private TextMeshProUGUI offsetText;    // 当前偏移显示
        [SerializeField] private TextMeshProUGUI pulseText;     // 视觉脉冲（● 缩放）

        [Header("按钮")]
        [SerializeField] private Button openButton;      // 菜单上的"节拍校准"按钮
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

        private AudioSource testSource;
        private AudioClip tickClip;
        private Coroutine testCoroutine;
        private bool testing;
        private readonly List<double> scheduledTicks = new List<double>(); // unscaledTime 基准
        private readonly List<float> tapDelays = new List<float>();
        private float recommendedMs;
        private Vector3 pulseOriginalScale;

        private void Awake()
        {
            // 按钮监听必须在 Play 模式接（编辑模式添加的监听进 Play 会被清空）
            if (openButton != null) openButton.onClick.AddListener(Open);
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
            if (panelRoot != null) panelRoot.SetActive(false);
            RefreshOffsetText();
        }

        private void Update()
        {
            if (!testing) return;

            // 测试期间全屏点击计为一次校准点击（编辑器鼠标 + 真机触摸）
            bool tapped = false;
#if UNITY_EDITOR
            tapped = Input.GetMouseButtonDown(0);
#else
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).phase == TouchPhase.Began) tapped = true;
            }
#endif
            if (!tapped) return;

            double now = Time.unscaledTimeAsDouble;
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
                statusText.text = $"已采集 {tapDelays.Count} 次点击";
        }

        /// <summary>打开面板（菜单按钮）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshOffsetText();
            SetButtonsInteractable(true);
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
            if (statusText != null) statusText.text = "跟着滴答点击屏幕…";
            if (applyButton != null) applyButton.interactable = false;
            SetButtonsInteractable(false);
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
                if (statusText != null) statusText.text = "有效点击太少，请跟着滴答节奏重新测试";
                SetButtonsInteractable(true);
                return;
            }

            float sum = 0f;
            foreach (float d in tapDelays) sum += d;
            recommendedMs = sum / tapDelays.Count * 1000f;

            if (statusText != null)
                statusText.text = $"平均延迟 {recommendedMs:F1} ms（共 {tapDelays.Count} 次）";
            if (applyButton != null) applyButton.interactable = true;
            SetButtonsInteractable(true);
        }

        /// <summary>应用推荐偏移</summary>
        public void ApplyRecommended()
        {
            CalibrationSettings.OffsetMs = Mathf.RoundToInt(recommendedMs);
            AudioManager.Instance?.PlayButtonClick();
            RefreshOffsetText();
            if (statusText != null) statusText.text = "已应用，回到游戏试听对拍";
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
                offsetText.text = $"当前偏移: {(CalibrationSettings.OffsetMs >= 0 ? "+" : "")}{CalibrationSettings.OffsetMs} ms\n（正 = 音符推迟）";
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
