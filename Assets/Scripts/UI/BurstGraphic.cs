using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 命中闪光（放射状条）：由中心向外的锥形条，顶点色程序化绘制，零贴图依赖。
    /// 扩散与淡出由外部驱动 RectTransform.sizeDelta 与颜色 alpha（与 HitRingGraphic 同一套做法）。
    /// </summary>
    public class BurstGraphic : Graphic
    {
        [SerializeField] private int spokes = 8;              // 条数
        [SerializeField] private float innerRatio = 0.30f;    // 内半径 / 外半径
        [SerializeField] private float halfAngleDeg = 5.5f;   // 单条半角（度）

        /// <summary>条数（越少越像"闪"，越多越像"光环"）</summary>
        public int Spokes
        {
            get { return spokes; }
            set { spokes = Mathf.Max(3, value); SetVerticesDirty(); }
        }

        /// <summary>内半径比例（留出中心空档，避免糊成一团）</summary>
        public float InnerRatio
        {
            get { return innerRatio; }
            set { innerRatio = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        /// <summary>单条半角（度）</summary>
        public float HalfAngleDeg
        {
            get { return halfAngleDeg; }
            set { halfAngleDeg = Mathf.Clamp(value, 0.5f, 30f); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float outer = Mathf.Min(r.width, r.height) * 0.5f;
            if (outer <= 0.01f) return;

            float inner = outer * innerRatio;
            Vector2 c = new Vector2(r.center.x, r.center.y);
            Color32 col = color;
            float half = halfAngleDeg * Mathf.Deg2Rad;

            for (int i = 0; i < spokes; i++)
            {
                float a = (Mathf.PI * 2f / spokes) * i;
                Vector2 p0 = c + new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half)) * outer;
                Vector2 p1 = c + new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half)) * outer;
                Vector2 p2 = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * inner;

                int idx = vh.currentVertCount;
                vh.AddVert(p0, col, Vector2.zero);
                vh.AddVert(p1, col, Vector2.zero);
                vh.AddVert(p2, col, Vector2.zero);
                vh.AddTriangle(idx, idx + 1, idx + 2);
            }
        }
    }
}
