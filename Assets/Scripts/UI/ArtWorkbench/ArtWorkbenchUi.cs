using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>工作台 UI 零件。零贴图，只用 Image 色块 + TMP。</summary>
    public static class ArtWorkbenchUi
    {
        public static TMP_FontAsset Font;

        public static RectTransform Root(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, bool raycast = false)
        {
            var rt = Root(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, int size, Color color, TextAlignmentOptions align, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Root(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        public static Button Button(Transform parent, string name, string text, Color fill, UnityAction click, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var img = Panel(parent, name, fill, anchorMin, anchorMax, offsetMin, offsetMax, true);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.7f, 0.78f, 0.8f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            Label(img.transform, "Label", text, 26, ArtWorkbenchTokens.Ink, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (click != null) btn.onClick.AddListener(click);
            return btn;
        }

        public static Toggle Toggle(Transform parent, string name, string text, bool on, UnityAction<bool> changed, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var img = Panel(parent, name, ArtWorkbenchTokens.Card, anchorMin, anchorMax, offsetMin, offsetMax, true);
            var toggle = img.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = img;
            toggle.isOn = on;
            Label(img.transform, "Label", text, 24, ArtWorkbenchTokens.Ink, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(8, 0), new Vector2(-8, 0));
            toggle.onValueChanged.AddListener(changed);
            return toggle;
        }

        public static Slider Slider(Transform parent, string name, float min, float max, float value, UnityAction<float> changed, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Root(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.1f, 0.14f, 1f);
            bg.raycastTarget = true;

            var fillRt = Root(rt, "Fill", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = ArtWorkbenchTokens.Accent;
            fill.raycastTarget = false;

            var handleRt = Root(rt, "Handle", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            handleRt.sizeDelta = new Vector2(28, 44);
            var handle = handleRt.gameObject.AddComponent<Image>();
            handle.color = ArtWorkbenchTokens.Ink;
            handle.raycastTarget = true;

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(changed);
            return slider;
        }
    }

    /// <summary>工作台切角条（tap）与长条身体。独立 Graphic，不改局内 Note 预制体。</summary>
    public sealed class ArtWorkbenchMark : Graphic
    {
        public bool holdBody;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width < 1f || r.height < 1f) return;
            if (holdBody)
            {
                AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMax,
                    Premultiply(new Color(color.r, color.g, color.b, color.a)),
                    Premultiply(new Color(color.r, color.g, color.b, 0f)));
                return;
            }

            float cut = Mathf.Min(r.width, r.height) * 0.22f;
            Color32 c = Premultiply(color);
            Vector2[] pts =
            {
                new Vector2(r.xMin + cut, r.yMin),
                new Vector2(r.xMax - cut, r.yMin),
                new Vector2(r.xMax, r.yMin + cut),
                new Vector2(r.xMax, r.yMax - cut),
                new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin + cut, r.yMax),
                new Vector2(r.xMin, r.yMax - cut),
                new Vector2(r.xMin, r.yMin + cut)
            };
            int start = vh.currentVertCount;
            for (int i = 0; i < pts.Length; i++)
                vh.AddVert(pts[i], c, Vector2.zero);
            for (int i = 1; i < pts.Length - 1; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }

        static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color bottom, Color top)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        static Color Premultiply(Color c)
        {
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }
    }
}
