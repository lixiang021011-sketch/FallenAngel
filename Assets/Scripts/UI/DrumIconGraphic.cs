using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 鼓件图标图形（零纹理/精灵，顶点色程序化绘制）。
    /// 与 Moonscraper / Rock Band 4-lane 鼓件语义对应：
    ///   Kick  底鼓 - 大圆环（大鼓鼓面）
    ///   Snare 军鼓 - 实心圆（军鼓鼓面）
    ///   HiHat 踩镲 - 上下双横线（镲片）
    ///   Crash 吊镲 - 菱形（镲片）
    /// 图标默认白色半透明，叠加在音符头部色块之上。
    /// </summary>
    public enum DrumIconType { Kick, Snare, HiHat, Crash }

    public class DrumIconGraphic : Graphic
    {
        private const int Segments = 24;

        [SerializeField] private DrumIconType iconType = DrumIconType.Snare;
        [SerializeField] private Color iconColor = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>图标形状，设置后重建网格</summary>
        public DrumIconType IconType
        {
            get => iconType;
            set { iconType = value; SetVerticesDirty(); }
        }

        /// <summary>图标颜色（含透明度），设置后重建网格</summary>
        public Color IconColor
        {
            get => iconColor;
            set { iconColor = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float cx = (rect.xMin + rect.xMax) * 0.5f;
            float cy = (rect.yMin + rect.yMax) * 0.5f;
            float r = Mathf.Min(rect.width, rect.height) * 0.5f;
            Color c = Premultiply(iconColor);

            switch (iconType)
            {
                case DrumIconType.Kick:  BuildRing(vh, cx, cy, r * 0.48f, r * 0.28f, c); break; // 底鼓：粗圆环
                case DrumIconType.Snare: BuildCircle(vh, cx, cy, r * 0.38f, c); break;         // 军鼓：实心圆
                case DrumIconType.HiHat: BuildBars(vh, cx, cy, r, c); break;                   // 踩镲：双横线
                case DrumIconType.Crash: BuildDiamond(vh, cx, cy, r * 0.44f, c); break;        // 吊镲：菱形
            }
        }

        /// <summary>圆环带（底鼓鼓面）</summary>
        private void BuildRing(VertexHelper vh, float cx, float cy, float outer, float inner, Color c)
        {
            for (int i = 0; i < Segments; i++)
            {
                float ang = Mathf.PI * 2f * i / Segments;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                vh.AddVert(new Vector3(cx + cos * outer, cy + sin * outer), c, Vector2.zero);
                vh.AddVert(new Vector3(cx + cos * inner, cy + sin * inner), c, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                int o0 = i * 2, o1 = next * 2, i0 = i * 2 + 1, i1 = next * 2 + 1;
                vh.AddTriangle(o0, o1, i0);
                vh.AddTriangle(o1, i1, i0);
            }
        }

        /// <summary>实心圆（军鼓鼓面）</summary>
        private void BuildCircle(VertexHelper vh, float cx, float cy, float radius, Color c)
        {
            vh.AddVert(new Vector3(cx, cy), c, Vector2.zero);
            for (int i = 0; i < Segments; i++)
            {
                float ang = Mathf.PI * 2f * i / Segments;
                vh.AddVert(new Vector3(cx + Mathf.Cos(ang) * radius, cy + Mathf.Sin(ang) * radius), c, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                vh.AddTriangle(0, 1 + i, 1 + next);
            }
        }

        /// <summary>双横线（踩镲上下片）</summary>
        private void BuildBars(VertexHelper vh, float cx, float cy, float r, Color c)
        {
            float halfW = r * 0.56f; // 横条半宽
            float halfH = r * 0.07f; // 横条半高
            float dy = r * 0.26f;    // 上下片中心偏移
            AddBar(vh, cx, cy - dy, halfW, halfH, c);
            AddBar(vh, cx, cy + dy, halfW, halfH, c);
        }

        private static void AddBar(VertexHelper vh, float cx, float cy, float halfW, float halfH, Color c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(cx - halfW, cy - halfH), c, Vector2.zero);
            vh.AddVert(new Vector3(cx - halfW, cy + halfH), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfW, cy + halfH), c, Vector2.zero);
            vh.AddVert(new Vector3(cx + halfW, cy - halfH), c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        /// <summary>菱形（吊镲镲片）</summary>
        private void BuildDiamond(VertexHelper vh, float cx, float cy, float radius, Color c)
        {
            vh.AddVert(new Vector3(cx, cy), c, Vector2.zero);
            vh.AddVert(new Vector3(cx, cy + radius), c, Vector2.zero); // 上
            vh.AddVert(new Vector3(cx + radius, cy), c, Vector2.zero); // 右
            vh.AddVert(new Vector3(cx, cy - radius), c, Vector2.zero); // 下
            vh.AddVert(new Vector3(cx - radius, cy), c, Vector2.zero); // 左
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
            vh.AddTriangle(0, 3, 4);
            vh.AddTriangle(0, 4, 1);
        }

        /// <summary>UI 默认着色器为 premultiplied alpha，顶点色必须预乘</summary>
        private static Color Premultiply(Color col)
        {
            return new Color(col.r * col.a, col.g * col.a, col.b * col.a, col.a);
        }
    }
}
