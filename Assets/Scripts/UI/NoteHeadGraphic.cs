using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 音符头部/尾部部件（零纹理/精灵，顶点色程序化绘制；pjsk 风圆角条）。
    /// 三层同形叠加，后画的盖住先画的：
    ///   1) 外圈描边——顶亮底暗，勾出部件轮廓；
    ///   2) 内面——纵向渐变（顶亮底暗），颜色取轨道语义色；
    ///   3) 顶面高光带——上半部一条半透明白，做出玻璃质感。
    /// 尺寸实时取 RectTransform.rect，自适应普通音符（130×14）、kick 全宽条、尾部标记等任意宽高。
    /// 色调与透明度走 Graphic.color（顶点色再乘该色），外部只改 color 即可淡入淡出，不必知道内部层次。
    /// </summary>
    public class NoteHeadGraphic : Graphic
    {
        [Header("形状")]
        [Tooltip("圆角半径 = 部件高度 × 该比例（再受宽度/高度一半钳制）")]
        [SerializeField] private float radiusRatio = 0.42f;
        [SerializeField] private float minRadius = 2f;
        [SerializeField] private float maxRadius = 9f;
        [Tooltip("每个圆角的细分段数")]
        [SerializeField] private int cornerSegments = 4;

        [Header("描边")]
        [Tooltip("外圈描边厚度（像素）；自动不超过高度的 1/3")]
        [SerializeField] private float rimThickness = 2.4f;

        [Header("明暗（相对 Graphic.color 的亮度系数）")]
        [SerializeField] private float rimTopLighten = 0.55f;
        [SerializeField] private float rimBottomDarken = 0.45f;
        [SerializeField] private float faceTopLighten = 0.22f;
        [SerializeField] private float faceBottomDarken = 0.30f;
        [Tooltip("顶面高光带不透明度（相对部件 alpha）")]
        [SerializeField] private float glossAlpha = 0.30f;
        [Tooltip("顶面高光带高度占内面高度比例")]
        [SerializeField] private float glossHeightRatio = 0.38f;

        // 轮廓点复用缓冲（每帧重建网格时不分配内存）
        private readonly List<Vector2> outline = new List<Vector2>(64);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float w = rect.width, h = rect.height;
            if (w <= 0.5f || h <= 0.5f) return;

            float radius = Mathf.Clamp(h * radiusRatio, minRadius,
                                       Mathf.Min(maxRadius, Mathf.Min(w, h) * 0.5f));
            float rim = Mathf.Clamp(rimThickness, 0.5f, h * 0.34f);
            Color baseColor = color;

            // 1) 外圈描边：整块圆角条（顶亮底暗）
            AddRoundedBar(vh, rect.xMin, rect.yMin, rect.xMax, rect.yMax, radius,
                Shade(baseColor, -rimBottomDarken), Shade(baseColor, rimTopLighten), cornerSegments);

            // 2) 内面：向内缩一圈描边（圆角同步收缩），纵向渐变
            float fx0 = rect.xMin + rim, fx1 = rect.xMax - rim;
            float fy0 = rect.yMin + rim, fy1 = rect.yMax - rim;
            if (fx1 - fx0 <= 0.5f || fy1 - fy0 <= 0.5f) return;
            AddRoundedBar(vh, fx0, fy0, fx1, fy1, Mathf.Max(0.5f, radius - rim),
                Shade(baseColor, -faceBottomDarken), Shade(baseColor, faceTopLighten), cornerSegments);

            // 3) 顶面高光带：内面上部一条半透明白（玻璃质感）
            if (glossAlpha > 0.001f && h > 6f)
            {
                float gh = Mathf.Max(2f, (fy1 - fy0) * glossHeightRatio);
                float gx0 = fx0 + rim, gx1 = fx1 - rim;
                float gy1 = fy1 - rim * 0.5f;
                float gy0 = Mathf.Max(fy0, gy1 - gh);
                if (gx1 - gx0 > 0.5f && gy1 - gy0 > 0.5f)
                {
                    Color gloss = new Color(1f, 1f, 1f, Mathf.Clamp01(baseColor.a * glossAlpha));
                    AddRoundedBar(vh, gx0, gy0, gx1, gy1,
                        Mathf.Max(0.5f, radius - rim * 2f), gloss, gloss, cornerSegments);
                }
            }
        }

        /// <summary>
        /// 画一个圆角条：外轮廓扇形填充，顶点色按 y 在 cBottom→cTop 间线性插值
        /// （顶亮底暗的纵向渐变）。
        /// </summary>
        private void AddRoundedBar(VertexHelper vh, float x0, float y0, float x1, float y1,
                                   float radius, Color cBottom, Color cTop, int segments)
        {
            Rect rect = new Rect(x0, y0, x1 - x0, y1 - y0);
            if (rect.width <= 0.01f || rect.height <= 0.01f) return;
            RoundedMeshUtil.BuildOutline(outline, rect, radius, segments);
            RoundedMeshUtil.AddFan(vh, outline, rect, cBottom, cTop, horizontal: false);
        }

        /// <summary>按系数提亮（正）/压暗（负）RGB，保持 alpha</summary>
        private static Color Shade(Color c, float amount)
        {
            float t = Mathf.Clamp01(Mathf.Abs(amount));
            float tr = amount >= 0f ? Mathf.Lerp(c.r, 1f, t) : Mathf.Lerp(c.r, 0f, t);
            float tg = amount >= 0f ? Mathf.Lerp(c.g, 1f, t) : Mathf.Lerp(c.g, 0f, t);
            float tb = amount >= 0f ? Mathf.Lerp(c.b, 1f, t) : Mathf.Lerp(c.b, 0f, t);
            return new Color(tr, tg, tb, c.a);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 自检用：按当前矩形直接生成一次网格（不依赖 Canvas 重建），返回顶点数与墨迹包围盒。
        /// 与 DeepSeaGraphic.GetMeshBounds 同用途——把"网格到底有没有生成、有没有跑出矩形"量化。
        /// </summary>
        public int BuildDebugMesh(out Rect bounds)
        {
            VertexHelper vh = new VertexHelper();
            OnPopulateMesh(vh);

            List<UIVertex> stream = new List<UIVertex>();
            vh.GetUIVertexStream(stream);
            bounds = new Rect();
            if (stream.Count > 0)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < stream.Count; i++)
                {
                    Vector3 p = stream[i].position;
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            }

            int count = vh.currentVertCount;
            vh.Dispose();
            return count;
        }
#endif
    }
}
