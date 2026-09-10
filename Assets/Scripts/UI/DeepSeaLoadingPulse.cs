using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// UI008「准备演出」的不定进度指示：一根轨道 + 往复滑动的亮条。
    /// 只表示"正在加载"，不表示百分比（M10/页面 19：进度必须来自实际加载，无法计算时用不定进度）。
    /// 用非缩放时间，暂停/加载都不受 Time.timeScale 影响。
    /// </summary>
    public sealed class DeepSeaLoadingPulse : MonoBehaviour
    {
        [SerializeField] private RectTransform track;
        [SerializeField] private RectTransform fill;
        [SerializeField] private float speed = 1.1f;

        public void Configure(RectTransform trackRect, RectTransform fillRect)
        {
            track = trackRect;
            fill = fillRect;
        }

        private void Update()
        {
            if (track == null || fill == null) return;
            float span = Mathf.Max(1f, track.rect.width - fill.rect.width);
            float t = Mathf.PingPong(Time.unscaledTime * speed, 1f);
            float eased = t * t * (3f - 2f * t);
            fill.anchoredPosition = new Vector2(eased * span, fill.anchoredPosition.y);
        }
    }
}
