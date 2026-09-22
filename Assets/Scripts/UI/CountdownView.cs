using TMPro;
using UnityEngine;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 开局倒计时的**表现层**（前端清单 E1）：每步一个缩放弹出 + 淡出，GO! 用强调色。
    /// 节奏与曲线取 `Core/CountdownTiming`（照上游"3/2/1 各 1 秒、每步一声"的口径）。
    /// 上游每步是一个带 Animator 的特效对象（`fx_live_countdown_3/2/1`）——按作者定案**特效不抄**，
    /// 这里用程序化的缩放/透明度等价；换素材时只替换本层的绘制，不改节奏。
    /// </summary>
    public sealed class CountdownView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Color normalColor = new Color(0.98f, 0.99f, 1f);
        [SerializeField] private Color goColor = new Color(0.44f, 0.88f, 0.84f);

        private float tInStep;
        private bool running;

        public bool IsRunning { get { return running; } }

        /// <summary>开始某一步（step 从 0 起：3 / 2 / 1 / GO!）</summary>
        public void ShowStep(int step)
        {
            running = true;
            tInStep = 0f;
            if (label == null) return;
            label.gameObject.SetActive(true);
            label.text = CountdownTiming.LabelAt(step);
            label.color = CountdownTiming.IsGo(step) ? goColor : normalColor;
            label.transform.localScale = Vector3.one * CountdownTiming.ScaleAt(0f);
        }

        /// <summary>倒计时结束：隐藏</summary>
        public void Hide()
        {
            running = false;
            if (label != null) label.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!running || label == null) return;
            tInStep += Time.unscaledDeltaTime;      // 倒计时用未缩放时间：暂停也不影响它自己的节奏

            float s = CountdownTiming.ScaleAt(tInStep);
            label.transform.localScale = new Vector3(s, s, 1f);

            Color c = label.color;
            c.a = CountdownTiming.AlphaAt(tInStep);
            label.color = c;
        }
    }
}
