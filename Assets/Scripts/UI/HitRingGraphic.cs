using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 圆环图形（命中扩散特效）：空心环带，环宽 = 半径 * RingThickness。
    /// 零纹理/精灵，顶点色程序化绘制（UI premultiplied alpha）。
    /// 扩散动画由外部驱动 RingColor.alpha 与 RectTransform.sizeDelta。
    /// </summary>
    public class HitRingGraphic : Graphic
    {
        private const int Segments = 32;

        [SerializeField] private float ringThickness = 0.32f; // 环带宽度 = 半径 * 此比例
        [SerializeField] private Color ringColor = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>环带宽度比例（相对半径），设置后重建网格</summary>
        public float RingThickness
        {
            get => ringThickness;
            set { ringThickness = value; SetVerticesDirty(); }
        }

        /// <summary>环颜色（含透明度），设置后重建网格</summary>
        public Color RingColor
        {
            get => ringColor;
            set { ringColor = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float cx = (rect.xMin + rect.xMax) * 0.5f;
            float cy = (rect.yMin + rect.yMax) * 0.5f;
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
            float inner = outer * Mathf.Max(0.05f, 1f - ringThickness);
            Color c = Premultiply(ringColor);

            for (int i = 0; i < Segments; i++)
            {
                float ang = Mathf.PI * 2f * i / Segments;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                vh.AddVert(new Vector3(cx + cos * outer, cy + sin * outer), c, Vector2.zero);
                vh.AddVert(new Vector3(cx + cos * inner, cy + sin * inner), c, Vector2.zero);
            }
            // 环带三角形：外i-外i+1-内i / 外i+1-内i+1-内i
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                int o0 = i * 2, o1 = next * 2, i0 = i * 2 + 1, i1 = next * 2 + 1;
                vh.AddTriangle(o0, o1, i0);
                vh.AddTriangle(o1, i1, i0);
            }
        }

        /// <summary>UI 默认着色器为 premultiplied alpha，顶点色必须预乘</summary>
        private static Color Premultiply(Color col)
        {
            return new Color(col.r * col.a, col.g * col.a, col.b * col.a, col.a);
        }
    }
}
