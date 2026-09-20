using System.Collections.Generic;
using FallenAngel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 音符「带子」：长按身体（竖直）与 slide 轨迹（折线）共用的梯形带，零纹理/精灵。
    ///
    /// 为什么要有它（作者 2026-09-20 要求"音符在任何时候都和轨道一样宽"）：
    /// 音符头是"矩形 × 透视缩放"，缩放比例取自它**所在高度**，所以头部天然贴合轨道；
    /// 而长按身体/slide 轨迹会横跨很多高度，用矩形画就会在轨道较窄的一头戳出去。
    /// 这里改成按高度采样画带子，每个采样点的水平半宽 =
    ///     LaneLayout.LaneWidth/2 × PlayPerspective.SizeScaleAtY(该点的画布 y)
    /// —— 正是"这个高度上轨道的一半宽"，于是带子边缘始终压在轨道边缘上（越界 0）。
    ///
    /// 坐标系（入参一律按**画布单位**，与 LaneLayout / PlayPerspective 同口径）：
    ///   层级约定 = 带子 → 音符（中心锚点）→ NotesContainer（全画布、无偏移、缩放 1）→ Canvas，
    ///   所以「画布 y = 音符.anchoredPosition.y + 自身.anchoredPosition.y + 局部偏移 × 缩放链」。
    ///   顶点再换算回局部单位（除以缩放链），带子因此不会被音符自身的透视缩放二次缩小。
    ///   PlayVisualChecks 会断言这条层级前提（NotesContainer 全画布无偏移）。
    ///
    /// 设计要点：位置的透视曲线与尺寸的透视曲线是同一条，所以尺寸随画布 y **在折点以下是线性的**
    /// （见 <see cref="PlayPerspective.FloorCornerY"/>）——带子只需在起点/折点/终点取点就完全贴合，
    /// 折点数 12 段只是留余量，没有折线误差。
    ///
    /// 仍然继承 Image（与 PerspectiveLaneGraphic 同款做法）：保持 Image 组件链路，
    /// 便于后续按 Image 取色的工具继续工作；sprite 为空时用内置白图，顶点色即成品色。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class NoteBandGraphic : Image
    {
        [Header("采样")]
        [Tooltip("带子分段数：每条折线段再细分成这么多小段（12 段在 1080 宽下每段 ≤ 220px）")]
        [SerializeField] private int segments = 12;

        [Header("颜色（沿带子：近端 → 远端）")]
        public Color bottomColor = new Color(1f, 1f, 1f, 0.9f);
        public Color topColor = new Color(1f, 1f, 1f, 0.18f);

        /// <summary>采样点：x/y 为画布单位（相对音符中心），t 是颜色参数（0 近端 → 1 远端）</summary>
        private struct BandSample
        {
            public float x;
            public float y;
            public float t;
        }

        /// <summary>折线点（画布单位，相对音符中心；竖直带子就是 (0,0)→(0,长度) 两点）</summary>
        private readonly List<Vector2> points = new List<Vector2>(16);
        private readonly List<BandSample> samples = new List<BandSample>(32);

        // 上次生成网格时用的「局部 → 画布」映射：音符一动就得重算（宽度按画布高度取值）
        private float builtOriginX = float.NaN;
        private float builtOriginY = float.NaN;
        private float builtScale = float.NaN;

        /// <summary>
        /// 竖直带子（长按身体）：从本节点原点向上 length，中心线在 x=0。
        /// length 按**画布单位**给（按住时随剩余量收缩，由 Note 逐帧调用）。
        /// </summary>
        public void SetStraightBand(float length)
        {
            float h = Mathf.Max(0f, length);
            points.Clear();
            if (h > 0.01f)   // 收缩到 0（长按即将释放）：直接空网格，不留一堆零面积四边形
            {
                points.Add(new Vector2(0f, 0f));
                points.Add(new Vector2(0f, h));
            }
            SetVerticesDirty();
        }

        /// <summary>
        /// 折线带子（slide 轨迹）：点按**画布单位**、相对音符中心给（第一个点是起点，通常为 0）。
        /// </summary>
        public void SetPathPoints(IList<Vector2> canvasPoints)
        {
            points.Clear();
            if (canvasPoints != null)
            {
                for (int i = 0; i < canvasPoints.Count; i++) points.Add(canvasPoints[i]);
            }
            SetVerticesDirty();
        }

        /// <summary>
        /// 音符每帧都在动，而带子宽度是按"当前画布高度"取的，所以位置/缩放一变就要重算网格。
        /// 这里只做"变了没"的判断，真正生成在 OnPopulateMesh（画布重建时调用）。
        /// </summary>
        private void LateUpdate()
        {
            if (rectTransform == null) return;
            float originX, originY, scale;
            ReadCanvasMapping(out originX, out originY, out scale);
            if (Mathf.Approximately(originX, builtOriginX) &&
                Mathf.Approximately(originY, builtOriginY) &&
                Mathf.Approximately(scale, builtScale)) return;
            SetVerticesDirty();
        }

        /// <summary>
        /// 局部 → 画布的映射：
        ///   origin* = 本节点原点在画布中心系里的位置；scale = 局部 1 单位对应多少画布单位。
        /// 约定见类注释（音符是中心锚点的直接父级，再往上是全画布容器）。
        /// </summary>
        private void ReadCanvasMapping(out float originX, out float originY, out float scale)
        {
            float ownScale = rectTransform != null ? rectTransform.localScale.x : 1f;
            float ownX = rectTransform != null ? rectTransform.anchoredPosition.x : 0f;
            float ownY = rectTransform != null ? rectTransform.anchoredPosition.y : 0f;

            float parentScale = 1f, parentX = 0f, parentY = 0f;
            RectTransform parent = transform.parent as RectTransform;
            if (parent != null && parent.anchorMin == parent.anchorMax)
            {
                // 音符：中心锚点，anchoredPosition 就是它相对容器的画布偏移
                parentScale = parent.localScale.x;
                parentX = parent.anchoredPosition.x;
                parentY = parent.anchoredPosition.y;
            }

            // 再往上只逐级累乘缩放，到画布为止（画布自身的缩放不算：画布内坐标本身就是画布单位）
            Transform t = parent != null ? parent.parent : null;
            while (t != null && t.GetComponent<Canvas>() == null)
            {
                parentScale *= t.localScale.x;
                t = t.parent;
            }

            scale = ownScale * parentScale;
            originX = parentX + ownX * parentScale;
            originY = parentY + ownY * parentScale;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;

            float originX, originY, scale;
            ReadCanvasMapping(out originX, out originY, out scale);
            builtOriginX = originX;
            builtOriginY = originY;
            builtScale = scale;
            if (scale <= 0.0001f) return;

            BuildSamples(originY);
            if (samples.Count < 2) return;

            float laneHalf = LaneLayout.LaneWidth * 0.5f;
            for (int i = 0; i < samples.Count - 1; i++)
            {
                BandSample a = samples[i];
                BandSample b = samples[i + 1];

                // 半宽取"该采样点画布高度上轨道的一半宽"，再换算回局部单位
                float halfA = HalfWidthAtCanvasY(originY + a.y, laneHalf) / scale;
                float halfB = HalfWidthAtCanvasY(originY + b.y, laneHalf) / scale;

                float ax = a.x / scale, ay = a.y / scale;
                float bx = b.x / scale, by = b.y / scale;
                Color ca = Premultiply(Color.Lerp(bottomColor, topColor, Mathf.Clamp01(a.t)));
                Color cb = Premultiply(Color.Lerp(bottomColor, topColor, Mathf.Clamp01(b.t)));

                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(ax - halfA, ay, 0f), ca, Vector2.zero);
                vh.AddVert(new Vector3(ax + halfA, ay, 0f), ca, Vector2.zero);
                vh.AddVert(new Vector3(bx + halfB, by, 0f), cb, Vector2.zero);
                vh.AddVert(new Vector3(bx - halfB, by, 0f), cb, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        /// <summary>轨道在某画布高度上的一半宽（与 PerspectiveLaneGraphic 同一口径）</summary>
        private static float HalfWidthAtCanvasY(float canvasY, float laneHalf)
        {
            return laneHalf * PlayPerspective.SizeScaleAtY(canvasY);
        }

        /// <summary>
        /// 按 points 生成采样点（画布单位）。每段细分成 segments 小段；
        /// 若尺寸下限的折点落在某段里，就在折点处断开分别细分——
        /// 不插这个点，折线会跨过折角向外鼓出（12 段时实测越界约 2px）。
        /// </summary>
        private void BuildSamples(float originY)
        {
            samples.Clear();
            int spanCount = points.Count - 1;
            if (spanCount < 1) return;

            int perSpan = Mathf.Max(1, Mathf.RoundToInt(segments / (float)spanCount));
            float cornerY = CornerCanvasY();

            for (int i = 0; i < spanCount; i++)
            {
                float ta = spanCount == 1 ? 0f : i / (float)spanCount;
                float tb = spanCount == 1 ? 1f : (i + 1) / (float)spanCount;
                AppendSpan(points[i], ta, points[i + 1], tb, perSpan, originY, cornerY);
            }
        }

        private void AppendSpan(Vector2 a, float ta, Vector2 b, float tb,
                                int perSpan, float originY, float cornerY)
        {
            float ya = originY + a.y;   // 画布 y（y 沿折线单调上升，故只需判断是否夹住折点）
            float yb = originY + b.y;
            if (yb - ya > 0.001f && cornerY > ya && cornerY < yb)
            {
                float u = Mathf.InverseLerp(ya, yb, cornerY);
                Vector2 mid = Vector2.Lerp(a, b, u);
                float tm = Mathf.Lerp(ta, tb, u);
                int first = Mathf.Clamp(Mathf.RoundToInt(perSpan * u), 1, perSpan - 1);
                AppendPiece(a, ta, mid, tm, first);
                AppendPiece(mid, tm, b, tb, perSpan - first);
                return;
            }
            AppendPiece(a, ta, b, tb, perSpan);
        }

        private void AppendPiece(Vector2 a, float ta, Vector2 b, float tb, int count)
        {
            for (int k = 0; k <= count; k++)
            {
                if (k == 0 && samples.Count > 0) continue;   // 与上一段共用的端点不重复
                float u = k / (float)count;
                samples.Add(new BandSample
                {
                    x = Mathf.Lerp(a.x, b.x, u),
                    y = Mathf.Lerp(a.y, b.y, u),
                    t = Mathf.Lerp(ta, tb, u),
                });
            }
        }

        /// <summary>尺寸下限折点在画布中心系里的 y（唯一来源：PlayPerspective）</summary>
        private static float CornerCanvasY()
        {
            return PlayPerspective.FloorCornerY;
        }

        private static Color Premultiply(Color c)
        {
            // UI 着色器为预乘 Alpha（Blend One OneMinusSrcAlpha），顶点色必须预乘，否则透明会失效
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            PlayPerspective.GeometryChanged += MarkDirty;
        }

        protected override void OnDisable()
        {
            PlayPerspective.GeometryChanged -= MarkDirty;
            base.OnDisable();
        }

        private void MarkDirty()
        {
            SetVerticesDirty();
        }

#if UNITY_EDITOR
        /// <summary>自检用：最近一次网格生成时用的「本节点原点在画布中心系里的位置」</summary>
        public Vector2 DebugCanvasOrigin
        {
            get { return new Vector2(builtOriginX, builtOriginY); }
        }

        /// <summary>自检用：最近一次网格生成时用的「局部 1 单位 = 画布多少单位」</summary>
        public float DebugCanvasScale
        {
            get { return builtScale; }
        }

        /// <summary>
        /// 自检用：按当前几何走一遍 OnPopulateMesh（与画布同一条路径），
        /// 输出每个采样点的画布中心与画布半宽，并返回顶点数与网格包围盒（局部单位）。
        /// </summary>
        public int BuildDebugGeometry(List<Vector2> canvasCenters, List<float> canvasHalves,
                                      out Rect meshBounds)
        {
            VertexHelper vh = new VertexHelper();
            OnPopulateMesh(vh);

            List<UIVertex> stream = new List<UIVertex>();
            vh.GetUIVertexStream(stream);
            meshBounds = new Rect();
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
                meshBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            }

            if (canvasCenters != null)
            {
                canvasCenters.Clear();
                for (int i = 0; i < samples.Count; i++)
                    canvasCenters.Add(new Vector2(builtOriginX + samples[i].x, builtOriginY + samples[i].y));
            }
            if (canvasHalves != null)
            {
                canvasHalves.Clear();
                float laneHalf = LaneLayout.LaneWidth * 0.5f;
                for (int i = 0; i < samples.Count; i++)
                    canvasHalves.Add(HalfWidthAtCanvasY(builtOriginY + samples[i].y, laneHalf));
            }

            int count = vh.currentVertCount;
            vh.Dispose();
            return count;
        }
#endif
    }
}
