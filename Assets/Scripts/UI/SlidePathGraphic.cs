using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// Slide 路径折线图形（零纹理/精灵，顶点色程序化绘制）。
    /// 局部坐标点（相对音符头部中心，y 向下为负），每段绘制加宽矩形（2 三角/段），
    /// 顶点色沿路径从头到尾线性渐隐（头部实体 → 尾端透明）。
    /// </summary>
    public class SlidePathGraphic : Graphic
    {
        [SerializeField] private float lineWidth = 18f;
        [SerializeField] private Color headColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color tailColor = new Color(1f, 1f, 1f, 0f);

        private List<Vector2> localPoints = new List<Vector2>();

        public float LineWidth { get => lineWidth; set { lineWidth = value; SetVerticesDirty(); } }
        public Color HeadColor { get => headColor; set { headColor = value; SetVerticesDirty(); } }
        public Color TailColor { get => tailColor; set { tailColor = value; SetVerticesDirty(); } }

        /// <summary>
        /// 设置路径点（相对音符头部中心的局部坐标，起点应为原点——头部沿所画路径移动）。
        /// rect 同步为包围盒大小，仅供参考（raycastTarget=false 且无遮罩，
        /// 实际渲染以网格顶点为准，不对称路径不依赖 rect 定位）。
        /// </summary>
        public void SetLocalPoints(IList<Vector2> points)
        {
            localPoints.Clear();
            if (points == null || points.Count == 0) { SetVerticesDirty(); return; }

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in points)
            {
                localPoints.Add(p);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }

            RectTransform rt = rectTransform;
            if (rt != null)
                rt.sizeDelta = new Vector2(maxX - minX + lineWidth, maxY - minY + lineWidth);

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (localPoints.Count < 2) return;

            float halfW = lineWidth * 0.5f;
            float segCount = localPoints.Count - 1f;
            for (int i = 0; i < localPoints.Count - 1; i++)
            {
                Vector2 a = localPoints[i];
                Vector2 b = localPoints[i + 1];
                Vector2 dir = b - a;
                float len = dir.magnitude;
                if (len < 0.001f) continue;
                Vector2 n = new Vector2(-dir.y / len, dir.x / len) * halfW; // 段法线

                // UI 着色器为预乘 Alpha，顶点色必须预乘（见 GradientImage）
                Color ca = Premultiply(Color.Lerp(headColor, tailColor, i / segCount));
                Color cb = Premultiply(Color.Lerp(headColor, tailColor, (i + 1) / segCount));

                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x - n.x, a.y - n.y), ca, Vector2.zero); // 0
                vh.AddVert(new Vector3(a.x + n.x, a.y + n.y), ca, Vector2.zero); // 1
                vh.AddVert(new Vector3(b.x + n.x, b.y + n.y), cb, Vector2.zero); // 2
                vh.AddVert(new Vector3(b.x - n.x, b.y - n.y), cb, Vector2.zero); // 3
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static Color Premultiply(Color c)
        {
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }
    }
}
