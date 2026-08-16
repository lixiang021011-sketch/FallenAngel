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
    /// 节拍校准控制器（菜单入口）——下落式校准，与核心游玩界面一致：
    ///   1. 音符从上方下落至判定线，到达瞬间播放滴答
    ///   2. 玩家在音符到达判定线时点击（支持预判：全量时刻表预排，提前点击得负延迟）
    ///   3. 平均延迟 → 推荐校准偏移（正 = 音符推迟，补偿设备音频输出延迟）
    ///   4. ±5ms 手动微调，写入 CalibrationSettings（PlayerPrefs 持久化）
    /// 点击捕获：主路径为 IPointerDownHandler（OS 事件队列驱动，快速轻点不丢帧边沿）；
    /// Update 轮询为兜底，双路径经 RegisterTap 去重。
    /// </summary>
    public class CalibrationController : MonoBehaviour, IPointerDownHandler
    {
        [Header("面板引用")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI statusText;    // 状态/结果文本（运行时动态）
        [SerializeField] private TextMeshProUGUI offsetText;    // 当前偏移显示
        [SerializeField] private RectTransform judgeLine;       // 判定线（音符到达位置）

        [Header("按钮")]
        [SerializeField] private Button startTestButton;
        [SerializeField] private Button applyButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button minusButton;
        [SerializeField] private Button closeButton;

        [Header("测试参数")]
        [SerializeField] private int tickCount = 16;
        [SerializeField] private float tickInterval = 0.6f;
        [SerializeField] private float fallTime = 1.2f;     // 音符下落时长（秒）
        [Tooltip("点击与最近音符到达时刻偏差超过该值不计入（秒）")]
        [SerializeField] private float tapTolerance = 0.3f;
        [Tooltip("测试开始前的准备时长（秒）")]
        [SerializeField] private float readySeconds = 1.5f;

        [Header("音符外观")]
        [SerializeField] private Color noteColor = new Color(0.45f, 0.8f, 1f, 0.95f);
        [SerializeField] private Vector2 noteSize = new Vector2(110, 110);
        private const float FallDistance = 576f; // 出生点到判定线的面板本地距离（与 SceneBuilder 布局对应）

        private AudioSource testSource;
        private AudioClip tickClip;
        private Coroutine testCoroutine;
        private bool testing;

        // 全量预排时刻表（音符到达判定线的绝对时间）：预判点击可关联未来音符
        private readonly List<double> arrivals = new List<double>();
        private readonly bool[] noteTapped = new bool[32];
        private readonly List<float> tapDelays = new List<float>();

        // 音符池（与游玩音符同款方块，运行时创建）
        private readonly List<RectTransform> notePool = new List<RectTransform>();
        private const int PoolSize = 3;

        private float recommendedMs;
        private float lastTapTime = -99f;
        private const float TapDedupeWindow = 0.05f;
        private float judgeY;

        private void Awake()
        {
            // 面板首次激活时执行（同 CalibrationController 旧版模式；入口按钮由 GameStarter 接）
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
        /// 而 OS 事件会完整送达。
        /// </summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            RegisterTap((float)eventData.clickTime);
        }

        /// <summary>
        /// 登记一次校准点击：与最近音符到达时刻关联（预排全量表，预判得负延迟），
        /// 每个音符只收一次点击；双路径 50ms 去重。
        /// </summary>
        private void RegisterTap(float tapTime)
        {
            if (!testing) return;
            if (tapTime - lastTapTime < TapDedupeWindow) return;
            lastTapTime = tapTime;
            if (arrivals.Count == 0) return;

            int bestIdx = -1;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < arrivals.Count; i++)
            {
                double d = Mathf.Abs((float)(tapTime - arrivals[i]));
                if (d < bestDiff) { bestDiff = d; bestIdx = i; }
            }
            if (bestIdx < 0 || bestDiff > tapTolerance) return;
            if (bestIdx >= noteTapped.Length || noteTapped[bestIdx]) return; // 每音符一次
            noteTapped[bestIdx] = true;

            tapDelays.Add((float)(tapTime - arrivals[bestIdx]));
            if (statusText != null)
                statusText.text = Loc.T("cal.collected", tapDelays.Count);
#if UNITY_EDITOR
            Debug.Log($"[CalibrationController] tap #{tapDelays.Count} at {tapTime:F3}, delay {(float)(tapTime - arrivals[bestIdx]) * 1000f:F0}ms (note #{bestIdx + 1})");
#endif
        }

        /// <summary>打开面板（菜单按钮，GameStarter 接线）</summary>
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

        /// <summary>开始下落式校准测试</summary>
        public void StartTest()
        {
            AudioManager.Instance?.PlayButtonClick();
            StopTest();
            testing = true;
            arrivals.Clear();
            System.Array.Clear(noteTapped, 0, noteTapped.Length);
            tapDelays.Clear();
            if (statusText != null) statusText.text = Loc.T("cal.testing");
            // 测试期间只禁用"开始测试"与"应用推荐值"；关闭/±5ms 保持可用，避免被困
            if (startTestButton != null) startTestButton.interactable = false;
            if (applyButton != null) applyButton.interactable = false;
            testCoroutine = StartCoroutine(NoteLoop());
        }

        private void StopTest()
        {
            testing = false;
            arrivals.Clear();
            if (testCoroutine != null)
            {
                StopCoroutine(testCoroutine);
                testCoroutine = null;
            }
            foreach (RectTransform note in notePool)
                if (note != null) note.gameObject.SetActive(false);
        }

        private System.Collections.IEnumerator NoteLoop()
        {
            // 准备期：按下"开始测试"后稍等，第一颗音符给满下落时长
            if (statusText != null)
                statusText.text = Loc.T("cal.ready", (int)Mathf.Ceil(readySeconds));
            double readyEnd = Time.unscaledTimeAsDouble + readySeconds;
            while (Time.unscaledTimeAsDouble < readyEnd)
                yield return null;

            // 全量预排时刻表（首颗音符在准备期结束后从顶部出发）
            double start = Time.unscaledTimeAsDouble;
            for (int i = 0; i < tickCount; i++)
                arrivals.Add(start + fallTime + i * tickInterval);

            // 活动音符列表：note / 到达时刻 / 序号 / 是否已响滴答
            var actives = new List<(RectTransform note, double arrival, int index, bool ticked)>();

            int nextIdx = 0;
            while (nextIdx < tickCount || actives.Count > 0)
            {
                double now = Time.unscaledTimeAsDouble;

                // 到点出生
                while (nextIdx < tickCount && arrivals[nextIdx] - fallTime <= now)
                {
                    RectTransform note = notePool[nextIdx % PoolSize];
                    note.gameObject.SetActive(true);
                    SetNoteProgress(note, 0f);
                    actives.Add((note, arrivals[nextIdx], nextIdx, false));
                    if (statusText != null)
                        statusText.text = Loc.T("cal.progress", nextIdx + 1, tickCount, tapDelays.Count);
                    nextIdx++;
                }

                // 更新下落
                for (int i = actives.Count - 1; i >= 0; i--)
                {
                    var an = actives[i];
                    double t = (now - (an.arrival - fallTime)) / fallTime;
                    if (t >= 1.0)
                    {
                        if (!an.ticked)
                        {
                            testSource.PlayOneShot(tickClip, 0.8f); // 到达判定线瞬间响滴答
                            an.ticked = true;
                            actives[i] = an;
                        }
                        SetNoteProgress(an.note, 1f);
                        if (now - an.arrival > 0.15f) // 过线后短暂停留再消失
                        {
                            an.note.gameObject.SetActive(false);
                            actives.RemoveAt(i);
                        }
                    }
                    else
                    {
                        SetNoteProgress(an.note, (float)t);
                    }
                }
                yield return null;
            }
            FinishTest();
        }

        private void SetNoteProgress(RectTransform note, float t)
        {
            float y = Mathf.Lerp(judgeY + FallDistance, judgeY, t);
            note.localPosition = new Vector3(0f, y, 0f);
        }

        private void FinishTest()
        {
            testing = false;

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
