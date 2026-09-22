using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;
using FallenAngel.Data;   // ChartData（曲名/曲师）

namespace FallenAngel.UI
{
    /// <summary>
    /// 加载页（前端清单 E5）。上游 `LoadingView`：标题 + 说明 + 图 + 一条小贴士
    /// （`Setup(MasterTip tip)`），配合 `ScreenLayerLiveLoading` 演出。
    ///
    /// 我们的等价做法（视觉按定案不抄，只做流程与布局）：
    ///   · 标题 = 曲名，说明 = "准备演奏…"，底部一条**确定性小贴士**（`Core/LoadingTip`）；
    ///   · 中间一条**不确定进度条**（左右往返扫过）——本地读谱很快，用确定进度是假象；
    ///   · 只在 `GameState.Loading` 期间显示，进演奏前淡出。
    ///
    /// 由 `GameManager` 的开局流程保证加载页有 `LoadingTip.LoadingSeconds` 的展示窗口。
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI tipText;
        [SerializeField] private RectTransform sweepBar;     // 往返扫过的不确定进度条
        [SerializeField] private float sweepSeconds = 1.1f;

        private float timer;
        private bool visible;

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>显示加载页（GameManager 进 Loading 时调）</summary>
        public void Show()
        {
            visible = true;
            timer = 0f;
            if (panelRoot != null) panelRoot.SetActive(true);

            ChartData chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            if (titleText != null)
                titleText.text = chart != null && chart.metadata != null ? chart.metadata.songName : "";
            if (tipText != null && chart != null && chart.metadata != null)
            {
                int index = LoadingTip.TipIndex(chart.metadata.songName, chart.metadata.songArtist);
                tipText.text = Loc.T(LoadingTip.TipKey(index));
            }
        }

        public void Hide()
        {
            visible = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (!visible || sweepBar == null) return;
            timer += Time.unscaledDeltaTime;

            // 往返扫过：用正弦把"进度条"在 20%..80% 之间来回推（不确定进度，不假装知道百分比）
            float phase = Mathf.PingPong(timer / Mathf.Max(0.1f, sweepSeconds), 1f);
            RectTransform track = sweepBar.parent as RectTransform;
            float width = track != null ? track.sizeDelta.x : 600f;
            float barW = width * 0.35f;
            float x = Mathf.Lerp(-width * 0.5f + barW * 0.5f, width * 0.5f - barW * 0.5f, phase);
            sweepBar.sizeDelta = new Vector2(barW, sweepBar.sizeDelta.y);
            sweepBar.anchoredPosition = new Vector2(x, sweepBar.anchoredPosition.y);
        }
    }
}
