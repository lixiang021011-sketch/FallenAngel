using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>Subtle map-only luminance cue. Never moves the map or uses song timing.</summary>
    public sealed class DeepSeaBeaconPulse : MonoBehaviour
    {
        private Coroutine pulse;
        private void OnEnable() { pulse = StartCoroutine(Animate()); }
        private void OnDisable() { if (pulse != null) StopCoroutine(pulse); }
        private IEnumerator Animate()
        {
            var graphic = GetComponentInChildren<Graphic>();
            float elapsed = 0;
            while (graphic != null)
            {
                elapsed += Time.unscaledDeltaTime;
                // Keep the pointer readable at its darkest; no layout or scale changes.
                graphic.canvasRenderer.SetAlpha(.8f + .2f * Mathf.Sin(elapsed * Mathf.PI));
                yield return null;
            }
        }
    }
}
