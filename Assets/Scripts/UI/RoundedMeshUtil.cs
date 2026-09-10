using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 圆角条网格工具：音符头部 / 尾部件 / 同时押连线共用同一套圆角与三角化，
    /// 避免每加一个部件就复制一遍圆弧代码（三处形状漂移是必然的）。
    /// ① BuildOutline 生成圆角矩形外轮廓（逆时针，从右下角起）；
    /// ② AddFan 用外轮廓做中心扇形三角化，顶点色沿 X 或沿 Y 在 c0→c1 之间线性插值。
    /// 圆角矩形是凸多边形，扇形三角化不需要额外切分。
    /// </summary>
    public static class RoundedMeshUtil
    {
        /// <summary>生成圆角矩形外轮廓点（写入 points，会先清空）；radius ≤ 0 退化为四角矩形</summary>
        public static void BuildOutline(List<Vector2> points, Rect rect, float radius, int cornerSegments)
        {
            points.Clear();
            float w = rect.width, h = rect.height;
            if (w <= 0.01f || h <= 0.01f) return;

            float x0 = rect.xMin, y0 = rect.yMin, x1 = rect.xMax, y1 = rect.yMax;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(w, h) * 0.5f);
            int seg = Mathf.Clamp(cornerSegments, 1, 12);

            if (r <= 0.01f)
            {
                points.Add(new Vector2(x0, y0));
                points.Add(new Vector2(x1, y0));
                points.Add(new Vector2(x1, y1));
                points.Add(new Vector2(x0, y1));
                return;
            }

            AddArc(points, new Vector2(x1 - r, y0 + r), r, -90f, 0f, seg);    // 右下
            AddArc(points, new Vector2(x1 - r, y1 - r), r, 0f, 90f, seg);     // 右上
            AddArc(points, new Vector2(x0 + r, y1 - r), r, 90f, 180f, seg);   // 左上
            AddArc(points, new Vector2(x0 + r, y0 + r), r, 180f, 270f, seg);  // 左下
        }

        /// <summary>
        /// 扇形填充：中心点取中间色，外轮廓点按位置在 c0→c1 间插值
        /// （horizontal=true 沿 X 渐变，否则沿 Y 渐变）。
        /// </summary>
        public static void AddFan(VertexHelper vh, IList<Vector2> outline, Rect rect, Color c0, Color c1, bool horizontal)
        {
            if (outline == null || outline.Count < 3) return;

            int center = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.center.x, rect.center.y),
                       Premultiply(Color.Lerp(c0, c1, 0.5f)), Vector2.zero);

            int first = vh.currentVertCount;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 p = outline[i];
                float t = horizontal
                    ? Mathf.InverseLerp(rect.xMin, rect.xMax, p.x)
                    : Mathf.InverseLerp(rect.yMin, rect.yMax, p.y);
                vh.AddVert(new Vector3(p.x, p.y), Premultiply(Color.Lerp(c0, c1, t)), Vector2.zero);
            }

            for (int i = 0; i < outline.Count; i++)
            {
                int a = first + i;
                int b = first + (i + 1) % outline.Count;
                vh.AddTriangle(center, a, b);
            }
        }

        /// <summary>追加一段圆心 center、半径 r、角度 a0→a1（度）的圆弧点</summary>
        private static void AddArc(List<Vector2> pts, Vector2 center, float r, float a0Deg, float a1Deg, int seg)
        {
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0Deg, a1Deg, i / (float)seg) * Mathf.Deg2Rad;
                pts.Add(new Vector2(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r));
            }
        }

        /// <summary>UI 默认着色器为 premultiplied alpha，顶点色必须预乘</summary>
        public static Color Premultiply(Color c)
        {
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }
    }
}
