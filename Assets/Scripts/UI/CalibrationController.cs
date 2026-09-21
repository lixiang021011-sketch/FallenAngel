using System.Collections;
using System.Collections.Generic;
using FallenAngel.Audio;
using FallenAngel.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 节奏校准（判定偏移）**玩家界面**——玩家不敲数字，只点屏幕。
    ///
    /// 为什么长这样：上游（OpenSekai）没有玩家向的校准界面，只有图表编辑器里一行数字输入（工程界面）。
    /// 所以这一层按音游通行做法（也是 PJSK"タイミング調整"的做法）设计：
    ///   ① 主操作「跟着节拍点屏幕」：8 拍，节拍有声、中央脉冲有视觉，点任意位置即可；
    ///   ② 自动算建议值（`CalibrationMeasure`：剔极值平均、半拍外不计）；
    ///   ③ 大按钮细调 ±5ms + 「应用建议值」+「再测一次」+「试听」（用当前偏移循环播节拍，直接听结果）；
    ///   ④ 数值仍写进上游那套 `TimingAdjustData`（**帧**，±20，正数 = 音符提前）。
    /// 过稿稿：art_tools/review/calibration_screen/（变体 A 中央脉冲）。
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        [Header("引用（SceneBuilder 注入）")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform pulseRing;       // 中央脉冲（贝塞尔放大+淡出）
        [SerializeField] private TextMeshProUGUI statusText;    // 提示 / 第 N 拍
        [SerializeField] private TextMeshProUGUI valueText;     // 当前偏移（ms + 帧）
        [SerializeField] private TextMeshProUGUI resultText;    // 本次测量结果
        [SerializeField] private Button startButton;            // 开始 / 再测一次
        [SerializeField] private Button applyButton;            // 应用建议值
        [SerializeField] private Button previewButton;          // 试听
        [SerializeField] private Button minusButton;            // −5ms
        [SerializeField] private Button plusButton;             // ＋5ms
        [SerializeField] private Button closeButton;

        [Header("节拍设置（与上游无关，属界面参数）")]
        [Tooltip("每拍间隔（秒）")]
        [SerializeField] private float beatInterval = 0.60f;
        [Tooltip("测量拍数")]
        [SerializeField] private int beatCount = 8;
        [Tooltip("细调步长（毫秒）")]
        [SerializeField] private int fineStepMs = 5;

        private AudioSource tickSource;
        private AudioClip tickClip;

        private readonly List<double> beatTimes = new List<double>();
        private readonly List<double> tapOffsets = new List<double>();
        private Coroutine loop;
        private bool measuring;
        private bool previewing;
        private float suggestedFrames;
        private bool hasSuggestion;
        private float pulseT = 1f;

        private void Awake()
        {
            tickSource = gameObject.AddComponent<AudioSource>();
            tickSource.playOnAwake = false;
            tickSource.loop = false;
            tickClip = SynthesizedSfx.CreateHitClip(1200f, 0.05f, 28f);

            if (startButton != null) startButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartMeasure(); });
            if (applyButton != null) applyButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); ApplySuggestion(); });
            if (previewButton != null) previewButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); TogglePreview(); });
            if (minusButton != null) minusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(-fineStepMs); });
            if (plusButton != null) plusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(+fineStepMs); });
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (panelRoot != null) panelRoot.SetActive(false);
            RefreshValueText();
            if (resultText != null) resultText.text = "";
            if (statusText != null) statusText.text = Loc.T("cal.hint");
        }

        private void Update()
        {
            // 中央脉冲：t 0→1 放大淡出（纯视觉，随节拍刷新）
            if (pulseRing != null)
            {
                pulseT = Mathf.Min(1f, pulseT + Time.unscaledDeltaTime / 0.35f);
                float s = Mathf.Lerp(0.6f, 1.35f, pulseT);
                pulseRing.localScale = new Vector3(s, s, 1f);
                Image img = pulseRing.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = 0.55f * (1f - pulseT);
                    img.color = c;
                }
            }

            if (!measuring) return;

            // 收点击：面板是 UI，不走 InputManager 的轨道输入
            bool tapped = false;
#if UNITY_EDITOR
            tapped = UnityEngine.Input.GetMouseButtonDown(0);
#else
            for (int i = 0; i < UnityEngine.Input.touchCount; i++)
                if (UnityEngine.Input.GetTouch(i).phase == TouchPhase.Began) { tapped = true; break; }
#endif
            if (!tapped || beatTimes.Count == 0) return;

            double now = Time.unscaledTimeAsDouble;
            double best = beatTimes[0];
            foreach (double beat in beatTimes)
                if (System.Math.Abs(now - beat) < System.Math.Abs(now - best)) best = beat;

            double offset;
            if (CalibrationMeasure.TryAssignToBeat(now, best, beatInterval, out offset))
            {
                tapOffsets.Add(offset);
                if (resultText != null)
                    resultText.text = Loc.T("cal.tapCount", tapOffsets.Count);
            }
        }

        public void Open()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            // ESC 由本层处理（SettingsPanelController 不再拦截）
            RefreshValueText();
            if (resultText != null) resultText.text = "";
            if (statusText != null) statusText.text = Loc.T("cal.hint");
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            StopLoops();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void StartMeasure()
        {
            StopLoops();
            measuring = true;
            hasSuggestion = false;
            tapOffsets.Clear();
            beatTimes.Clear();
            if (statusText != null) statusText.text = Loc.T("cal.tapAlong", beatCount);
            if (resultText != null) resultText.text = "";
            loop = StartCoroutine(MeasureLoop());
        }

        private IEnumerator MeasureLoop()
        {
            double start = Time.unscaledTimeAsDouble + 1.0;      // 前导 1 秒
            for (int i = 0; i < beatCount; i++)
            {
                if (!measuring) yield break;
                double beat = start + i * beatInterval;
                beatTimes.Add(beat);
                while (Time.unscaledTimeAsDouble < beat - 0.05) yield return null;
                PlayTick();
                if (statusText != null)
                    statusText.text = Loc.T("cal.beatOf", i + 1, beatCount, tapOffsets.Count);
                while (Time.unscaledTimeAsDouble < beat + beatInterval - 0.05) yield return null;
            }

            measuring = false;
            suggestedFrames = CalibrationMeasure.SuggestedFrames(tapOffsets);
            hasSuggestion = tapOffsets.Count > 0;
            if (statusText != null) statusText.text = Loc.T("cal.hint");
            if (resultText != null)
            {
                resultText.text = hasSuggestion
                    ? Loc.T("cal.measured", tapOffsets.Count, CalibrationMeasure.FramesToMs(suggestedFrames))
                    : Loc.T("cal.noTap");
            }
            Debug.Log(string.Format("[Calibration] 跟拍测量：{0} 次点击 → 建议 {1:0.1} 帧（{2}ms）",
                tapOffsets.Count, suggestedFrames, CalibrationMeasure.FramesToMs(suggestedFrames)));
        }

        private void ApplySuggestion()
        {
            if (!hasSuggestion) return;
            CalibrationSettings.TimingAdjustData = suggestedFrames;
            RefreshValueText();
            Debug.Log(string.Format("[Calibration] 应用建议值：{0:0.1} 帧（{1}ms，正数 = 音符提前）",
                CalibrationSettings.TimingAdjustData,
                CalibrationMeasure.FramesToMs(CalibrationSettings.TimingAdjustData)));
        }

        private void Nudge(int deltaMs)
        {
            float frames = CalibrationSettings.TimingAdjustData + CalibrationMeasure.MsToFrames(deltaMs);
            CalibrationSettings.TimingAdjustData = frames;
            RefreshValueText();
        }

        /// <summary>试听：用**当前偏移**循环播节拍（把偏移直接听出来：正数 = 滴答提前）</summary>
        private void TogglePreview()
        {
            if (previewing) { StopLoops(); return; }
            StopLoops();
            previewing = true;
            loop = StartCoroutine(PreviewLoop());
        }

        private IEnumerator PreviewLoop()
        {
            double next = Time.unscaledTimeAsDouble + 0.3;
            while (previewing)
            {
                // 偏移为正（音符提前）→ 声音也提前同样的量
                double at = next - CalibrationSettings.TimingAdjustSeconds;
                while (Time.unscaledTimeAsDouble < at) yield return null;
                PlayTick();
                next += beatInterval;
                while (Time.unscaledTimeAsDouble < next) yield return null;
            }
        }

        private void PlayTick()
        {
            if (tickSource != null && tickClip != null) tickSource.PlayOneShot(tickClip, 0.8f);
            pulseT = 0f;                     // 视觉脉冲与滴答同步
        }

        private void StopLoops()
        {
            measuring = false;
            previewing = false;
            if (loop != null) { StopCoroutine(loop); loop = null; }
        }

        private void RefreshValueText()
        {
            if (valueText == null) return;
            float frames = CalibrationSettings.TimingAdjustData;
            valueText.text = Loc.T("cal.current",
                CalibrationMeasure.FramesToMs(frames), CalibrationSettings.Format(frames));
        }
    }
}
