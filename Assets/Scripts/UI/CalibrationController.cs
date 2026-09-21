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
        [SerializeField] private RectTransform lanePlayfield;   // 下落区（音符在这里从顶落到底）
        [SerializeField] private RectTransform laneNote;        // 下落音符（复用游戏透视曲线）
        [SerializeField] private float laneSpawnY = 300f;       // 下落区内的出生 Y（局部）
        [SerializeField] private float laneJudgeY = -300f;      // 下落区内的判定线 Y（局部）
        [SerializeField] private TextMeshProUGUI statusText;    // 提示 / 第 N 拍
        [SerializeField] private TextMeshProUGUI valueText;     // 当前偏移（ms + 帧）
        [SerializeField] private TextMeshProUGUI resultText;    // 本次测量结果
        [SerializeField] private Button startButton;            // 开始 / 再测一次
        [SerializeField] private Button applyButton;            // 应用建议值
        [SerializeField] private Button previewButton;          // 试听
        [SerializeField] private Button minusButton;            // −5ms
        [SerializeField] private Button plusButton;             // ＋5ms
        [SerializeField] private Button closeButton;

        [Header("osu! 式统计校准（实战命中误差 → 建议偏移）")]
        [Tooltip("三段提示：还没数据 / 已经准确 / 建议 X（见 Core/HitErrorTracker）")]
        [SerializeField] private TextMeshProUGUI suggestText;
        [Tooltip("应用系统建议值（对应 osu! 的 Apply suggested offset：应用后清空统计）")]
        [SerializeField] private Button applySuggestButton;
        [Tooltip("清空统计（对应 osu! 的 ClearHistory）")]
        [SerializeField] private Button clearStatsButton;

        [Header("节拍设置（每个参数的出处见下方注释，不是我随手定的）")]
        [Tooltip("节拍器 BPM。osu! 偏移向导用「最后选择的歌曲」的 BPM（osu-wiki: Client/Options/Offset_Wizard），" +
                 "我们优先用当前谱面 BPM，取不到才回落到这个默认值")]
        [SerializeField] private float bpm = 120f;
        [Tooltip("每小节拍数（4/4）")]
        [SerializeField] private int beatsPerBar = 4;
        [Tooltip("要采多少次（每小节重拍采一次）。8 = Cytoid GlobalCalibrator 的两轮 ×4")]
        [SerializeField] private int tapCount = 8;
        [Tooltip("细调步长（毫秒）")]
        [SerializeField] private int fineStepMs = 5;

        /// <summary>每拍秒数：由 BPM 推出来（osu! 向导的节拍器也是跟着 BPM 走）。</summary>
        private float BeatSeconds { get { return 60f / Mathf.Max(30f, bpm); } }

        private AudioSource tickSource;
        private AudioClip tickClipLow;      // 小节内其余拍：节拍器低音
        private AudioClip tickClipHigh;     // 重拍高音（PJSK：跟着这个声音敲）

        private readonly List<double> beatTimes = new List<double>();
        private readonly List<double> tapOffsets = new List<double>();
        private Coroutine loop;
        private bool measuring;
        private bool previewing;
        private float suggestedFrames;
        private bool hasSuggestion;
        private float pulseT = 1f;

        /// <summary>
        /// `Open()` 在激活面板**之前**把它置位。
        /// 为什么必须这样：面板在场景里是隐藏的，第一次 `SetActive(true)` 会立刻触发 `Awake`，
        /// 而 Awake 里那句"默认隐藏"会把刚打开的面板又关掉 —— 真机表现就是"点开没反应"。
        /// </summary>
        private bool openRequested;

        private void Awake()
        {
            tickSource = gameObject.AddComponent<AudioSource>();
            tickSource.playOnAwake = false;
            tickSource.loop = false;
            // 低音 / 高音两档：PJSK 的文案就是"高い音が鳴るタイミングに合わせて"，
            // 高音 = 击打点，低音 = 节拍器（两档音高差 ~1.5 倍，竖屏小喇叭也分得清）
            tickClipLow = SynthesizedSfx.CreateHitClip(880f, 0.05f, 28f);
            tickClipHigh = SynthesizedSfx.CreateHitClip(1320f, 0.05f, 28f);

            if (startButton != null) startButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); StartMeasure(); });
            if (applyButton != null) applyButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); ApplySuggestion(); });
            if (previewButton != null) previewButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); TogglePreview(); });
            if (minusButton != null) minusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(-fineStepMs); });
            if (plusButton != null) plusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(+fineStepMs); });
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (applySuggestButton != null) applySuggestButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); ApplySuggestedOffset(); });
            if (clearStatsButton != null) clearStatsButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); ClearStats(); });
            if (panelRoot != null && !openRequested) panelRoot.SetActive(false);
            RefreshValueText();
            RefreshSuggestion();
            if (resultText != null) resultText.text = "";
            if (statusText != null) statusText.text = Loc.T("cal.hint");
        }

        private void Update()
        {
            // 判定线闪光（音符到线 / 滴答时 pulseT 归零 → 放大淡出）
            if (pulseRing != null && pulseT < 1f)
            {
                pulseT = Mathf.Min(1f, pulseT + Time.unscaledDeltaTime / 0.35f);
                float s = Mathf.Lerp(0.75f, 1.15f, pulseT);
                pulseRing.localScale = new Vector3(s, s, 1f);
                Image img = pulseRing.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = 0.5f * (1f - pulseT);
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
            if (CalibrationMeasure.TryAssignToBeat(now, best, BeatSeconds, out offset))
            {
                tapOffsets.Add(offset);
                if (resultText != null)
                    resultText.text = Loc.T("cal.tapCount", tapOffsets.Count);
            }
        }

        public void Open()
        {
            openRequested = true;
            ApplyChartBpm();
            if (panelRoot != null) panelRoot.SetActive(true);
            // ESC 由本层处理（SettingsPanelController 不再拦截）
            RefreshValueText();
            RefreshSuggestion();
            if (resultText != null) resultText.text = "";
            if (statusText != null) statusText.text = Loc.T("cal.hint");
        }

        /// <summary>
        /// 节拍器 BPM 跟着**当前谱面**走：osu! 偏移向导就是"打开向导后播放最后选择的歌曲，
        /// 节拍器按这首歌的 BPM 滴答"（osu-wiki: Client/Options/Offset_Wizard）。
        /// 没有谱面（或谱面没写 BPM）时回落到 120。
        /// </summary>
        private void ApplyChartBpm()
        {
            float chartBpm = 0f;
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.CurrentChart != null && gm.CurrentChart.metadata != null)
                chartBpm = gm.CurrentChart.metadata.bpm;

            bpm = chartBpm >= 40f ? chartBpm : 120f;
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
            if (statusText != null) statusText.text = Loc.T("cal.tapAlong", tapCount, Mathf.RoundToInt(bpm));
            if (resultText != null) resultText.text = "";
            loop = StartCoroutine(MeasureLoop());
        }

        /// <summary>
        /// 跟拍测量。节奏形状不是我自己定的，逐条对得上来源：
        ///
        /// | 我们的做法 | 出处 |
        /// | --- | --- |
        /// | 4/4 节拍器，**每小节第一拍是重音（高音）**，玩家在重音上敲 | PJSK 原版文案 `MSG_ADJUST_WITH_TAP_BODY`：<br>「判定タイミングを調整できます。\n**高い音が鳴るタイミングに合わせて**\n下のボタンをタップしてください。」<br>（高音 = 击打点，低音只是节拍器） |
        /// | BPM 取**当前谱面**的 BPM，取不到回落 120 | osu! 偏移向导："打开设置向导后会出现一个滴答作响的节拍器…游戏将开始播放最后选择的歌曲"<br>（osu-wiki：`Client/Options/Offset_Wizard`），即节拍器跟歌走 |
        /// | 拍点**落到判定线时闪白**并出声，作为视觉参考 | 同一篇 osu-wiki："屏幕上滚动的绿色条是'拍点'。当它们通过屏幕中央的垂直线时，它们会闪烁白色并发出滴答声" |
        /// | 采 **8 次**（8 个小节的重音） | Cytoid `GlobalCalibrator.cs`：两轮 ×4 次采样，取平均 |
        /// | 剔除极值后取平均（现有 `CalibrationMeasure`） | 抗离群；Cytoid 用"单点偏差 &gt;80ms 整轮作废"，我们保留修剪平均（更宽容） |
        ///
        /// 手势：玩家敲的是**屏幕下半部的任意位置**（不是某个小按钮）——
        /// 竖屏手机上把敲击点限制成一个 60px 的按钮既不现实也不符合"跟着音乐敲"的直觉。
        /// </summary>
        private IEnumerator MeasureLoop()
        {
            float beat = BeatSeconds;
            int beatsPerBarLocal = Mathf.Max(1, beatsPerBar);
            double start = Time.unscaledTimeAsDouble + 1.0 + laneFallSeconds;   // 前导 1 秒 + 让第一个重拍落到线

            for (int bar = 0; bar < tapCount; bar++)
            {
                if (!measuring) yield break;

                double barStart = start + bar * beatsPerBarLocal * beat;
                for (int b = 0; b < beatsPerBarLocal; b++)
                {
                    if (!measuring) yield break;

                    double at = barStart + b * beat;
                    bool accent = b == 0;                       // 重拍 = 高音 = 击打点

                    if (accent)
                    {
                        // 重拍：音符从 laneSpawnY 沿**游戏的透视曲线**落到判定线，落到线的那一刻响高音。
                        double spawn = at - laneFallSeconds;
                        beatTimes.Add(at);
                        if (laneNote != null) laneNote.gameObject.SetActive(true);
                        while (Time.unscaledTimeAsDouble < at)
                        {
                            if (!measuring) yield break;
                            double p = (Time.unscaledTimeAsDouble - spawn) / laneFallSeconds;
                            SetNoteProgress(Mathf.Clamp01((float)p));
                            yield return null;
                        }
                        SetNoteProgress(1f);
                        PlayTick(true);
                        if (laneNote != null) laneNote.gameObject.SetActive(false);
                        if (statusText != null)
                            statusText.text = Loc.T("cal.beatOf", bar + 1, tapCount, tapOffsets.Count);
                    }
                    else
                    {
                        // 小节内的其余拍：只有节拍器低音（PJSK：跟着"高音"敲，低音只给节奏感）
                        while (Time.unscaledTimeAsDouble < at)
                        {
                            if (!measuring) yield break;
                            yield return null;
                        }
                        PlayTick(false);
                    }
                }
            }

            measuring = false;
            // 符号：测量值 Δ>0 = 你**打得晚**（音符到线之后才点）→ 要让音符晚点到线，
            // 即 `TimingAdjustData` 取负（上游口径：正数 = 音符提前）。所以建议值 = −Δ。
            suggestedFrames = -CalibrationMeasure.SuggestedFrames(tapOffsets);
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

        /// <summary>音符在落下区里的位置/缩放：与游戏同一套透视曲线（PlayPerspective）</summary>
        private void SetNoteProgress(float progress01)
        {
            if (laneNote == null) return;
            float v = PlayPerspective.ViewProgress01(progress01);
            float y = Mathf.Lerp(laneSpawnY, laneJudgeY, v);
            laneNote.anchoredPosition = new Vector2(laneNote.anchoredPosition.x, y);
            float s = PlayPerspective.SizeScale(progress01);
            laneNote.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>下落时长（秒）：音符从出生到判定线</summary>
        [Header("下落参数")]
        [SerializeField] private float laneFallSeconds = 1.0f;

        private void ApplySuggestion()
        {
            if (!hasSuggestion) return;
            CalibrationSettings.TimingAdjustData = suggestedFrames;
            RefreshValueText();
            RefreshSuggestion();
            Debug.Log(string.Format("[Calibration] 应用建议值：{0:0.1} 帧（{1}ms，正数 = 音符提前）",
                CalibrationSettings.TimingAdjustData,
                CalibrationMeasure.FramesToMs(CalibrationSettings.TimingAdjustData)));
        }

        private void Nudge(int deltaMs)
        {
            float frames = CalibrationSettings.TimingAdjustData + CalibrationMeasure.MsToFrames(deltaMs);
            CalibrationSettings.TimingAdjustData = frames;
            RefreshValueText();
            RefreshSuggestion();
        }

        // ── osu! 式统计校准（实战命中误差 → 建议偏移）────────────────────
        /// <summary>
        /// 三段提示（照抄 osu! `AudioOffsetAdjustControl.updateHintText` 的结构）：
        /// 没数据 → 提示去打歌；建议与当前差异 &lt; 1ms → 「已经准确」；否则给建议值。
        /// 同时控制「应用建议值」按钮的可用性（osu! 里是 `applySuggestion.Enabled`）。
        /// </summary>
        private void RefreshSuggestion()
        {
            float suggested;
            bool hasSuggestion = HitErrorHistory.TrySuggestFrames(out suggested);
            int samples = HitErrorHistory.Count;

            if (suggestText != null)
            {
                if (!hasSuggestion)
                {
                    suggestText.text = Loc.T("cal.suggest.none");
                }
                else if (HitErrorTracker.IsAlreadyCorrect(suggested, CalibrationSettings.TimingAdjustData))
                {
                    suggestText.text = Loc.T("cal.suggest.correct", samples);
                }
                else
                {
                    suggestText.text = Loc.T("cal.suggest.value",
                        HitErrorTracker.FramesToMs(suggested), samples);
                }
            }

            if (applySuggestButton != null)
            {
                applySuggestButton.interactable = hasSuggestion
                    && !HitErrorTracker.IsAlreadyCorrect(suggested, CalibrationSettings.TimingAdjustData);
            }
            if (clearStatsButton != null) clearStatsButton.interactable = samples > 0;
        }

        /// <summary>
        /// 应用系统建议值（osu! `applySuggestion.Action`：`current.Value = SuggestedOffset.Value.Value;`
        /// 且紧接着 `hitErrorTracker.ClearHistory()` —— 应用后重新累计，否则旧样本会反复"建议"同一个值）。
        /// </summary>
        private void ApplySuggestedOffset()
        {
            float suggested;
            if (!HitErrorHistory.TrySuggestFrames(out suggested)) return;

            CalibrationSettings.TimingAdjustData = suggested;
            HitErrorHistory.Clear();
            RefreshValueText();
            RefreshSuggestion();
            Debug.Log(string.Format("[Calibration] 应用系统建议偏移：{0:0.0} 帧（{1:0.0}ms，正 = 音符提前）；统计已清空",
                CalibrationSettings.TimingAdjustData,
                HitErrorTracker.FramesToMs(CalibrationSettings.TimingAdjustData)));
        }

        private void ClearStats()
        {
            HitErrorHistory.Clear();
            RefreshSuggestion();
            Debug.Log("[Calibration] 已清空命中误差统计（osu! 的 ClearHistory）");
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
            int beatIndex = 0;
            while (previewing)
            {
                // 偏移为正（音符提前）→ 声音也提前同样的量
                double at = next - CalibrationSettings.TimingAdjustSeconds;
                while (Time.unscaledTimeAsDouble < at) yield return null;
                // 试听也走 4/4：每小节第一拍高音，方便对照"重拍应该落在哪"
                PlayTick(beatIndex % Mathf.Max(1, beatsPerBar) == 0);
                beatIndex++;
                next += BeatSeconds;
                while (Time.unscaledTimeAsDouble < next) yield return null;
            }
        }

        private void PlayTick()
        {
            PlayTick(false);
        }

        /// <summary>响一声节拍器：重拍用高音（击打点），其余用低音。</summary>
        private void PlayTick(bool accent)
        {
            AudioClip clip = accent ? tickClipHigh : tickClipLow;
            if (tickSource != null && clip != null) tickSource.PlayOneShot(clip, accent ? 0.9f : 0.5f);
            if (accent) pulseT = 0f;         // 视觉脉冲只在重拍闪（与"高音 = 击打点"一致）
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
