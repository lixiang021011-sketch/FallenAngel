using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// Cut-in 卡片（前端对照清单 E6/E7，形制对应上游 `CutinView`）。
    ///
    /// 竖屏适配：卡片从**右侧滑入**、停留、再淡出（上游是横屏从侧边划过，竖屏照搬会挡住音符）。
    /// 内容：图标（占位 = key 首字母）、主标题、副标题；文案全走 Loc key。
    /// 订阅照 AGENTS 规范：OnEnable 订阅、OnDisable 退订。
    /// </summary>
    public sealed class CutinView : MonoBehaviour
    {
        [SerializeField] private RectTransform card;
        [SerializeField] private Image iconPlate;
        [SerializeField] private TextMeshProUGUI iconText;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Tooltip("滑入/淡出各占时长的比例")]
        [SerializeField] private float edgeRatio = 0.22f;
        [Tooltip("卡片显示时距屏幕右侧的留白（像素，画布坐标）")]
        [SerializeField] private float rightPadding = 40f;

        private CutinRequest current;
        private float elapsed;
        private float duration;
        private bool playing;
        private float offscreenX;

        private void Awake()
        {
            if (card != null) offscreenX = card.anchoredPosition.x;
            SetVisible(false);
        }

        private void OnEnable()
        {
            CutinService.OnCutinStarted -= HandleStarted;
            CutinService.OnCutinStarted += HandleStarted;
            CutinService.OnCutinEnded -= HandleEnded;
            CutinService.OnCutinEnded += HandleEnded;

            // 追赶：请求可能发生在本层被激活之前（局内道具在菜单/结算期发放 → 演奏开始才 OnEnable）
            CutinRequest pending;
            if (CutinService.TryGetCurrent(out pending)) HandleStarted(pending);
        }

        private void OnDisable()
        {
            CutinService.OnCutinStarted -= HandleStarted;
            CutinService.OnCutinEnded -= HandleEnded;
        }

        private void Update()
        {
            if (card == null) return;

            // **时钟的唯一来源是 CutinService**（它只在演奏中推进）。
            // 早先这里自己用 unscaledDeltaTime 累加，结果"菜单期触发的 cut-in 在菜单里就把时长走完了"，
            // 真机上表现为卡片永远不出现 —— 所以动画进度一律读 service 的 CurrentProgress。
            if (!CutinService.IsPlaying)
            {
                if (playing)
                {
                    playing = false;
                    SetVisible(false);
                    if (card != null) card.anchoredPosition = new Vector2(offscreenX, card.anchoredPosition.y);
                }
                return;
            }

            if (!playing) playing = true;

            float t = CutinService.CurrentProgress;
            float edge = Mathf.Clamp(edgeRatio, 0.05f, 0.45f);

            // 滑入：0 → edge 用三次缓出；停留；淡出：1-edge → 1
            float slide = t < edge ? 1f - Mathf.Pow(1f - t / edge, 3f) : 1f;
            float alpha = t < edge ? Mathf.Clamp01(t / edge)
                : (t > 1f - edge ? Mathf.Clamp01((1f - t) / edge) : 1f);

            float targetX = -rightPadding;
            card.anchoredPosition = new Vector2(Mathf.Lerp(offscreenX, targetX, slide),
                card.anchoredPosition.y);
            SetAlpha(alpha);

        }

        private void HandleStarted(CutinRequest request)
        {
            current = request;
            elapsed = 0f;
            duration = CutinService.CurrentDuration > 0f
                ? CutinService.CurrentDuration
                : Mathf.Max(0.2f, request.duration);
            playing = true;

            if (titleText != null) titleText.text = Loc.T(request.titleKey);
            if (subtitleText != null)
                subtitleText.text = string.IsNullOrEmpty(request.subtitleKey) ? "" : Loc.T(request.subtitleKey);
            if (iconText != null)
            {
                string label = Loc.T(request.iconKey);
                iconText.text = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
            }
            if (iconPlate != null) iconPlate.color = new Color(0.44f, 0.88f, 0.84f, 0.85f);

            SetVisible(true);
            SetAlpha(0f);
            if (card != null) card.anchoredPosition = new Vector2(offscreenX, card.anchoredPosition.y);
            Debug.Log(string.Format("[Cutin] 播放：{0}（来源 {1}，时长 {2:0.0}s）",
                request.titleKey, request.sourceId, duration));
        }

        private void HandleEnded()
        {
            playing = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (card != null) card.gameObject.SetActive(visible);
        }

        private void SetAlpha(float alpha)
        {
            if (card == null) return;
            var group = card.GetComponent<CanvasGroup>();
            if (group == null) group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(alpha);
        }
    }
}
