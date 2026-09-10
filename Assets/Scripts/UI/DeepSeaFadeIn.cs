using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>FX001/M01 页面淡入：240ms ease-out，非缩放时间；淡入期间屏蔽交互（防连点双操作）。</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DeepSeaFadeIn : MonoBehaviour
    {
        [SerializeField] private float duration = .24f;
        private CanvasGroup group;
        private float elapsed;
        private bool playing;

        public void Play()
        {
            group = GetComponent<CanvasGroup>();
            elapsed = 0f;
            playing = true;
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        private void Update()
        {
            if (!playing) return;
            if (group == null) group = GetComponent<CanvasGroup>();
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration));
            group.alpha = 1f - (1f - t) * (1f - t);   // ease-out
            if (t < 1f) return;
            playing = false;
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
    }
}
