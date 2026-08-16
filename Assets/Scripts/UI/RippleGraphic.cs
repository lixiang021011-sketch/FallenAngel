using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 触点涟漪图形：实心圆 + 径向透明度渐变（中心实、边缘虚）。
    /// 零纹理/精灵依赖，参考 GradientImage 的顶点色方案（UI 着色器 premultiplied alpha）。
    /// </summary>
    public class RippleGraphic : Graphic
    {
        private const int Segments = 32;

        [SerializeField] private Color centerColor = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>中心色（含透明度），设置后触发重建</summary>
        public Color CenterColor
        {
            get => centerColor;
            set
            {
                centerColor = value;
                SetAllDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float cx = (rect.xMin + rect.xMax) * 0.5f;
            float cy = (rect.yMin + rect.yMax) * 0.5f;
            float r = Mathf.Min(rect.width, rect.height) * 0.5f;

            // 中心顶点（alpha=1）+ 圆周顶点（alpha=0）
            UIVertex center = UIVertex.simpleVert;
            center.position = new Vector3(cx, cy);
            center.color = Premultiply(centerColor);
            vh.AddVert(center);

            UIVertex edge = UIVertex.simpleVert;
            edge.color = Premultiply(new Color(centerColor.r, centerColor.g, centerColor.b, 0f));
            for (int i = 0; i < Segments; i++)
            {
                float ang = Mathf.PI * 2f * i / Segments;
                edge.position = new Vector3(cx + Mathf.Cos(ang) * r, cy + Mathf.Sin(ang) * r);
                vh.AddVert(edge);
            }

            // 扇形三角形：中心-边i-边i+1
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                vh.AddTriangle(0, 1 + i, 1 + next);
            }
        }

        /// <summary>UI 默认着色器为 premultiplied alpha，顶点色必须预乘</summary>
        private static Color Premultiply(Color c)
        {
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }
    }
}
