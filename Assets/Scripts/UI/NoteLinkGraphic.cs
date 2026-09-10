using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 同时押横向连线（Sonolus SIMULTANEOUS_CONNECTION 语义，零纹理/精灵，顶点色程序化绘制）：
    /// 一条扁圆角带，颜色从左侧音符的轨道色渐变到右侧音符的轨道色，
    /// 画在两个音符头部之下——两端被各自头部盖住，读起来就是"一条横跨两轨的音符条"。
    /// 尺寸实时取 RectTransform.rect（宽度 = 两音符中心距，高度 = 音符头部高度的比例）。
    /// </summary>
    public class NoteLinkGraphic : Graphic
    {
        [SerializeField] private Color leftColor = new Color(1f, 1f, 1f, 0.75f);
        [SerializeField] private Color rightColor = new Color(1f, 1f, 1f, 0.75f);
        [Tooltip("圆角半径 = 带宽 × 该比例（再受半宽钳制）")]
        [SerializeField] private float radiusRatio = 0.5f;
        [SerializeField] private float maxRadius = 9f;
        [SerializeField] private int cornerSegments = 4;

        private readonly List<Vector2> outline = new List<Vector2>(64);

        /// <summary>左端（本音符一侧）颜色</summary>
        public Color LeftColor { get => leftColor; set { leftColor = value; SetVerticesDirty(); } }

        /// <summary>右端（伙伴音符一侧）颜色</summary>
        public Color RightColor { get => rightColor; set { rightColor = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0.5f || rect.height <= 0.5f) return;

            float radius = Mathf.Clamp(rect.height * radiusRatio, 1f, Mathf.Min(maxRadius, rect.width * 0.5f));
            RoundedMeshUtil.BuildOutline(outline, rect, radius, cornerSegments);
            RoundedMeshUtil.AddFan(vh, outline, rect, leftColor, rightColor, horizontal: true);
        }

#if UNITY_EDITOR
        /// <summary>自检用：按当前矩形直接生成一次网格，返回顶点数与墨迹包围盒（同 NoteHeadGraphic.BuildDebugMesh）</summary>
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
