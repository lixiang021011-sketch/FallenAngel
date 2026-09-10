using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// FX003/M06 获得闪光：事务成功后播放 320ms「亮边向中心收束」，不遮挡操作、播完自毁。
    /// 只在业务成功事件之后调用（禁止提前播放营造已获得的假象）。
    /// </summary>
    public sealed class DeepSeaSuccessFlash : MonoBehaviour
    {
        private DeepSeaGraphic frame;
        private RectTransform rect;
        private float elapsed;
        private const float Duration = .32f;

        public static void Play(RectTransform host)
        {
            if (host == null) return;
            var go = new GameObject("SuccessFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(DeepSeaGraphic));
            var flashRect = (RectTransform)go.transform;
            flashRect.SetParent(host, false);
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
            var graphic = go.GetComponent<DeepSeaGraphic>();
            graphic.shape = DeepSeaGraphic.Shape.RoundedFrame;
            graphic.variant = 1;
            graphic.color = DeepSeaTheme.Accent;
            var flash = go.AddComponent<DeepSeaSuccessFlash>();
            flash.frame = graphic;
            flash.rect = flashRect;
            go.transform.SetAsLastSibling();
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);
            if (frame != null)
            {
                var c = frame.color;
                c.a = 1f - t;
                frame.color = c;
            }
            if (rect != null)
            {
                float inset = Mathf.Lerp(0f, 26f, t);      // 亮边向中心收束
                rect.offsetMin = new Vector2(inset, inset);
                rect.offsetMax = new Vector2(-inset, -inset);
            }
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
