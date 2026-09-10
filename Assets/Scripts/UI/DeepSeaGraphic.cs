using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 程序化几何：海洋底纹、面板/按钮圆角面与描边、UI005 图标集、房间图标、天赋语义符号。
    /// 视觉语言为 UI001「方向 A｜圆角几何」：笔画 0.055（归一化）、圆头收笔、棱角一律倒圆。
    /// 与 art_tools/export_ui_art.py 导出的 PNG/SVG 同源同形，改这里请同步那边。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DeepSeaGraphic : MaskableGraphic
    {
        /// <summary>新值一律追加在末尾，避免改动已有序列化取值。</summary>
        public enum Shape { Ocean, Frame, Beacon, Battle, Shop, Empty, Final, Equipment, Close, Pause, Position, Lock,
            CutFrame, Surface, Check, Chevron, Talent, Coin, Rule, Growth, Refresh, Play, Back, Info,
            Reward, Amplify, Floor, Upgrade, RoundedSurface, RoundedFrame }

        public Shape shape;
        public int variant;
        /// <summary>圆角半径（逻辑像素）；RoundedSurface/RoundedFrame 用，variant 只表示描边粗细/双层。</summary>
        public float cornerRadius = 26f;

        private const float Stroke = .055f;   // 方向 A 标准笔画（归一化）

        protected override void Awake() { base.Awake(); raycastTarget = false; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (shape == Shape.Ocean) { Ocean(vh, r); return; }

            Color c = color;
            if (shape == Shape.RoundedSurface || shape == Shape.RoundedFrame)
            {
                float radius = Mathf.Min(cornerRadius, Mathf.Min(r.width, r.height) * .28f);
                var box = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
                                  new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };
                if (shape == Shape.RoundedSurface) Fill(vh, Rounded(box, radius, 6), c);
                else
                {
                    StrokePath(vh, Rounded(box, radius, 6), variant == 1 ? 2.6f : 1.2f, c, true);
                    // variant>=2（终点/封顶节点）：内缩一圈细线，形成双层框类
                    if (variant >= 2)
                    {
                        float gap = 7f;
                        var inner = new[] { new Vector2(r.xMin + gap, r.yMin + gap), new Vector2(r.xMin + gap, r.yMax - gap),
                                            new Vector2(r.xMax - gap, r.yMax - gap), new Vector2(r.xMax - gap, r.yMin + gap) };
                        StrokePath(vh, Rounded(inner, Mathf.Max(0f, radius - gap), 6), 1.2f, c, true);
                    }
                }
                return;
            }
            if (shape == Shape.Surface || shape == Shape.CutFrame) { LegacyCut(vh, r, c); return; }
            if (shape == Shape.Frame)
            {
                StrokePath(vh, Rounded(new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
                                               new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) }, 0f, 2), 1.2f, c, true);
                return;
            }
            DrawIcon(vh, r, c);
            CenterInk(vh, r);   // 图标按实际墨迹居中，避免重心偏心看起来"错位"
        }

        /// <summary>图标绘制：一律在矩形内「居中的正方形」（边长 = 短边）里作图，保证任何宿主都不变形。</summary>
        private void DrawIcon(VertexHelper vh, Rect r, Color c)
        {
            float m = Mathf.Min(r.width, r.height);
            Vector2 C = r.center;
            float Radius(float normalized) => normalized * m;
            // 相对中心的偏移（0,0 = 正中；±.5 = 贴边）。曾经误写成绝对坐标语义，
            // 导致每个图标的内部零件整体偏移半格（靠 CenterInk 才看着居中，细节全是错的）。
            Vector2 Off(Vector2 offset) => new Vector2(offset.x * m, offset.y * m);
            Vector2[] Box(float x, float y, float w, float h)
            {
                float x0 = C.x - m * .5f + x * m, y0 = C.y - m * .5f + y * m;
                return new[] { new Vector2(x0, y0), new Vector2(x0, y0 + h * m),
                               new Vector2(x0 + w * m, y0 + h * m), new Vector2(x0 + w * m, y0) };
            }

            // ---- UI005 图标集 ----
            if (shape == Shape.Coin)
            {
                Circle(vh, C, Radius(.3125f), Stroke * m, c);
                Fill(vh, Rounded(Box(.429f, .429f, .142f, .142f), Radius(.047f), 5), c);
                return;
            }
            if (shape == Shape.Growth)
            {
                var diamond = new[] { C + Off(new Vector2(0, .3125f)), C + Off(new Vector2(.3125f, 0)),
                                      C + Off(new Vector2(0, -.3125f)), C + Off(new Vector2(-.3125f, 0)) };
                StrokePath(vh, Rounded(diamond, Radius(.156f), 6), Stroke * m, c, true);
                Fill(vh, Rounded(Box(.4375f, .4375f, .125f, .125f), Radius(.039f), 5), c);
                Caps(vh, C + Off(new Vector2(0, .328f)), C + Off(new Vector2(0, .406f)), Stroke * .94f, c);
                Caps(vh, C + Off(new Vector2(0, -.328f)), C + Off(new Vector2(0, -.406f)), Stroke * .94f, c);
                Caps(vh, C + Off(new Vector2(-.328f, 0)), C + Off(new Vector2(-.406f, 0)), Stroke * .94f, c);
                Caps(vh, C + Off(new Vector2(.328f, 0)), C + Off(new Vector2(.406f, 0)), Stroke * .94f, c);
                return;
            }
            if (shape == Shape.Refresh)
            {
                Arc(vh, C, Radius(.297f), 90, 360, Stroke * m, c);
                var head = new[] { C + Off(new Vector2(.266f, .359f)), C + Off(new Vector2(.4375f, .156f)),
                                   C + Off(new Vector2(.219f, .078f)) };
                Fill(vh, Rounded(head, Radius(.07f), 6), c);
                return;
            }
            if (shape == Shape.Position)
            {
                Circle(vh, C, Radius(.34f), Stroke * m, c);
                Disc(vh, C, Radius(.13f), c);
                return;
            }
            if (shape == Shape.Pause)
            {
                Fill(vh, Rounded(Box(.3125f, .234f, .125f, .531f), Radius(.0625f), 5), c);
                Fill(vh, Rounded(Box(.5625f, .234f, .125f, .531f), Radius(.0625f), 5), c);
                return;
            }
            if (shape == Shape.Play)
            {
                var tri = new[] { C + Off(new Vector2(-.172f, .297f)), C + Off(new Vector2(.297f, 0)),
                                  C + Off(new Vector2(-.172f, -.297f)) };
                Fill(vh, Rounded(tri, Radius(.125f), 6), c);
                return;
            }
            if (shape == Shape.Back)
            {
                Caps(vh, C + Off(new Vector2(.328f, 0)), C + Off(new Vector2(-.1875f, 0)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.1875f, 0)), C + Off(new Vector2(.016f, .1875f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.1875f, 0)), C + Off(new Vector2(.016f, -.1875f)), Stroke * m, c);
                return;
            }
            if (shape == Shape.Close)
            {
                Caps(vh, C + Off(new Vector2(-.219f, .219f)), C + Off(new Vector2(.219f, -.219f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(.219f, .219f)), C + Off(new Vector2(-.219f, -.219f)), Stroke * m, c);
                return;
            }
            if (shape == Shape.Lock)
            {
                StrokePath(vh, Rounded(Box(.266f, .188f, .469f, .359f), Radius(.109f), 6), Stroke * m, c, true);
                Arc(vh, C + Off(new Vector2(0, .047f)), Radius(.156f), 0, 180, Stroke * m, c);
                Fill(vh, Rounded(Box(.453f, .297f, .094f, .141f), Radius(.039f), 4), c);
                return;
            }
            if (shape == Shape.Check)
            {
                var pts = new List<Vector2>();
                pts.AddRange(Bezier(C + Off(new Vector2(-.266f, -.016f)), C + Off(new Vector2(-.203f, -.109f)),
                                    C + Off(new Vector2(-.125f, -.1875f)), 8));
                pts.AddRange(Bezier(C + Off(new Vector2(-.125f, -.1875f)), C + Off(new Vector2(.047f, 0)),
                                    C + Off(new Vector2(.266f, .25f)), 12));
                StrokePath(vh, pts, Stroke * 1.08f * m, c, false);
                return;
            }
            if (shape == Shape.Chevron)
            {
                Caps(vh, C + Off(new Vector2(-.125f, .266f)), C + Off(new Vector2(.094f, 0)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(.094f, 0)), C + Off(new Vector2(-.125f, -.266f)), Stroke * m, c);
                return;
            }
            if (shape == Shape.Info)
            {
                Circle(vh, C, Radius(.3125f), Stroke * m, c);
                Disc(vh, C + Off(new Vector2(0, .195f)), Radius(.047f), c);
                Caps(vh, C + Off(new Vector2(0, .0625f)), C + Off(new Vector2(0, -.203f)), Stroke * m, c);
                return;
            }

            // ---- MAP001 房间图标 ----
            if (shape == Shape.Beacon)
            {
                var diamond = new[] { C + Off(new Vector2(0, .375f)), C + Off(new Vector2(.375f, 0)),
                                      C + Off(new Vector2(0, -.375f)), C + Off(new Vector2(-.375f, 0)) };
                StrokePath(vh, Rounded(diamond, Radius(.172f), 6), Stroke * m, c, true);
                Disc(vh, C, Radius(.094f), c);
                return;
            }
            if (shape == Shape.Battle)
            {
                Caps(vh, C + Off(new Vector2(-.281f, .297f)), C + Off(new Vector2(.281f, -.266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(.281f, .297f)), C + Off(new Vector2(-.281f, -.266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.344f, .172f)), C + Off(new Vector2(-.172f, .344f)), Stroke * .86f * m, c);
                Caps(vh, C + Off(new Vector2(.172f, -.344f)), C + Off(new Vector2(.344f, -.172f)), Stroke * .86f * m, c);
                return;
            }
            if (shape == Shape.Shop)
            {
                var awning = new List<Vector2>(Bezier(C + Off(new Vector2(-.344f, .078f)), C + Off(new Vector2(-.234f, .297f)),
                                                      C + Off(new Vector2(.234f, .297f)), 10));
                awning.AddRange(Bezier(C + Off(new Vector2(.234f, .297f)), C + Off(new Vector2(.344f, .078f)),
                                       C + Off(new Vector2(.344f, .078f)), 6));
                StrokePath(vh, awning, Stroke * m, c, false);
                Caps(vh, C + Off(new Vector2(-.344f, .078f)), C + Off(new Vector2(.344f, .078f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.234f, -.016f)), C + Off(new Vector2(-.234f, -.297f)), Stroke * .94f * m, c);
                Caps(vh, C + Off(new Vector2(.234f, -.016f)), C + Off(new Vector2(.234f, -.297f)), Stroke * .94f * m, c);
                Caps(vh, C + Off(new Vector2(-.234f, -.297f)), C + Off(new Vector2(.234f, -.297f)), Stroke * .94f * m, c);
                Fill(vh, Rounded(Box(.328f, .266f, .125f, .1875f), Radius(.039f), 4), c);
                return;
            }
            if (shape == Shape.Empty)
            {
                var box = new[] { C + Off(new Vector2(0, -.281f)), C + Off(new Vector2(-.141f, -.281f)),
                                  C + Off(new Vector2(-.219f, -.203f)), C + Off(new Vector2(-.219f, .172f)),
                                  C + Off(new Vector2(-.141f, .25f)), C + Off(new Vector2(.141f, .25f)),
                                  C + Off(new Vector2(.219f, .172f)), C + Off(new Vector2(.219f, -.203f)),
                                  C + Off(new Vector2(.141f, -.281f)), C + Off(new Vector2(0, -.281f)) };
                StrokePath(vh, new List<Vector2>(box), Stroke * m, c, false);
                return;
            }
            if (shape == Shape.Final)
            {
                Caps(vh, C + Off(new Vector2(-.266f, .266f)), C + Off(new Vector2(-.031f, -.234f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(.266f, .266f)), C + Off(new Vector2(.031f, -.234f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.328f, .328f)), C + Off(new Vector2(.328f, .328f)), Stroke * .94f * m, c);
                Caps(vh, C + Off(new Vector2(0, .438f)), C + Off(new Vector2(0, .359f)), Stroke * .94f * m, c);
                return;
            }

            // ---- TAL002 效果语义符号 ----
            if (shape == Shape.Reward)
            {
                Caps(vh, C + Off(new Vector2(-.266f, .094f)), C + Off(new Vector2(.266f, .094f)), Stroke * m, c);
                var lid = new List<Vector2>(Bezier(C + Off(new Vector2(-.234f, .094f)), C + Off(new Vector2(-.1875f, .234f)),
                                                   C + Off(new Vector2(-.1875f, .234f)), 6));
                lid.AddRange(Bezier(C + Off(new Vector2(-.1875f, .234f)), C + Off(new Vector2(.1875f, .234f)),
                                    C + Off(new Vector2(.1875f, .234f)), 8));
                lid.AddRange(Bezier(C + Off(new Vector2(.1875f, .234f)), C + Off(new Vector2(.234f, .094f)),
                                    C + Off(new Vector2(.234f, .094f)), 6));
                StrokePath(vh, lid, Stroke * m, c, false);
                Caps(vh, C + Off(new Vector2(-.25f, .094f)), C + Off(new Vector2(-.25f, -.266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(.25f, .094f)), C + Off(new Vector2(.25f, -.266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.25f, -.266f)), C + Off(new Vector2(.25f, -.266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .234f)), C + Off(new Vector2(0, -.266f)), Stroke * .94f * m, c);
                return;
            }
            if (shape == Shape.Amplify)
            {
                Caps(vh, C + Off(new Vector2(-.203f, .0625f)), C + Off(new Vector2(0, .266f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .266f)), C + Off(new Vector2(.203f, .0625f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.203f, -.297f)), C + Off(new Vector2(0, -.094f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, -.094f)), C + Off(new Vector2(.203f, -.297f)), Stroke * m, c);
                return;
            }
            if (shape == Shape.Floor)
            {
                Caps(vh, C + Off(new Vector2(-.359f, -.266f)), C + Off(new Vector2(.359f, -.266f)), Stroke * 1.08f * m, c);
                Caps(vh, C + Off(new Vector2(-.1875f, .141f)), C + Off(new Vector2(0, .344f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .344f)), C + Off(new Vector2(.1875f, .141f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .344f)), C + Off(new Vector2(0, -.0625f)), Stroke * m, c);
                return;
            }
            if (shape == Shape.Upgrade)
            {
                Caps(vh, C + Off(new Vector2(-.1875f, -.047f)), C + Off(new Vector2(0, .156f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .156f)), C + Off(new Vector2(.1875f, -.047f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(0, .156f)), C + Off(new Vector2(0, -.281f)), Stroke * m, c);
                Caps(vh, C + Off(new Vector2(-.344f, .344f)), C + Off(new Vector2(.344f, .344f)), Stroke * .94f * m, c);
                return;
            }

            // 兜底：天赋/装备等遗留符号
            if (shape == Shape.Talent)
            {
                Caps(vh, C + Off(new Vector2(0, -.33f)), C + Off(new Vector2(0, .04f)), Stroke * .9f * m, c);
                Caps(vh, C + Off(new Vector2(0, .04f)), C + Off(new Vector2(-.3f, .3f)), Stroke * .9f * m, c);
                Caps(vh, C + Off(new Vector2(0, .04f)), C + Off(new Vector2(.3f, .3f)), Stroke * .9f * m, c);
                Caps(vh, C + Off(new Vector2(0, .04f)), C + Off(new Vector2(0, .38f)), Stroke * .9f * m, c);
                return;
            }
            if (shape == Shape.Equipment)
            {
                Circle(vh, C, Radius(.33f), Stroke * m, c);
                int spokes = 3 + Mathf.Abs(variant) % 5;
                for (int i = 0; i < spokes; i++)
                {
                    float a = (i * 360f / spokes + 90f) * Mathf.Deg2Rad;
                    Caps(vh, C, C + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius(.24f), Stroke * .9f * m, c);
                }
                return;
            }
        }

        /// <summary>把已绘制的顶点整体平移，使墨迹包围盒中心与矩形中心重合（图标视觉居中）。</summary>
        private static void CenterInk(VertexHelper vh, Rect r)
        {
            int n = vh.currentVertCount;
            if (n == 0) return;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            UIVertex v = default;
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Vector2.Min(min, (Vector2)v.position);
                max = Vector2.Max(max, (Vector2)v.position);
            }
            Vector2 delta = r.center - (min + max) * .5f;
            if (delta.sqrMagnitude < .01f) return;
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position += (Vector3)delta;
                vh.SetUIVertex(v, i);
            }
        }

        /// <summary>编辑器自检用：返回当前形状实际绘制内容的包围盒（调用会重建一次网格）。</summary>
        public Rect GetMeshBounds()
        {
            var vh = new VertexHelper();
            OnPopulateMesh(vh);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            UIVertex v = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Vector2.Min(min, (Vector2)v.position);
                max = Vector2.Max(max, (Vector2)v.position);
            }
            vh.Dispose();
            if (min.x > max.x) return new Rect();
            return new Rect(min, max - min);
        }

        // ---------- 画法辅助 ----------
        private static Vector2[] Square(Vector2 center, float half)
        {
            return new[] { new Vector2(center.x - half, center.y - half), new Vector2(center.x - half, center.y + half),
                           new Vector2(center.x + half, center.y + half), new Vector2(center.x + half, center.y - half) };
        }

        private static Vector2[] RectBox(Rect r, float x, float y, float w, float h)
        {
            float x0 = r.xMin + x * r.width, y0 = r.yMin + y * r.height;
            return new[] { new Vector2(x0, y0), new Vector2(x0, y0 + h * r.height),
                           new Vector2(x0 + w * r.width, y0 + h * r.height), new Vector2(x0 + w * r.width, y0) };
        }

        /// <summary>圆角多边形：顶点处用二次贝塞尔近似圆角。</summary>
        private static List<Vector2> Rounded(Vector2[] pts, float radius, int segments)
        {
            var outPts = new List<Vector2>();
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = pts[(i + n - 1) % n], cur = pts[i], next = pts[(i + 1) % n];
                Vector2 v1 = (prev - cur).normalized, v2 = (next - cur).normalized;
                float rr = Mathf.Min(radius, Mathf.Min((prev - cur).magnitude * .5f, (next - cur).magnitude * .5f));
                Vector2 t1 = cur + v1 * rr, t2 = cur + v2 * rr;
                outPts.Add(t1);
                for (int k = 1; k < segments; k++) outPts.Add(QuadBezier(t1, cur, t2, k / (float)segments));
                outPts.Add(t2);
            }
            return outPts;
        }

        private static List<Vector2> Bezier(Vector2 a, Vector2 control, Vector2 b, int segments)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= segments; i++) pts.Add(QuadBezier(a, control, b, i / (float)segments));
            return pts;
        }

        private static Vector2 QuadBezier(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        private static void Fill(VertexHelper vh, List<Vector2> pts, Color c)
        {
            if (pts.Count < 3) return;
            int start = vh.currentVertCount;
            vh.AddVert(pts[0], c, Vector2.zero);
            for (int i = 1; i < pts.Count; i++) vh.AddVert(pts[i], c, Vector2.zero);
            for (int i = 1; i < pts.Count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }

        private static void StrokePath(VertexHelper vh, List<Vector2> pts, float width, Color c, bool close)
        {
            for (int i = 0; i < pts.Count - 1; i++) Line(vh, pts[i], pts[i + 1], width, c);
            if (close && pts.Count > 2) Line(vh, pts[pts.Count - 1], pts[0], width, c);
        }

        /// <summary>圆头线段（端点补圆点，方向 A 的收笔一律为圆头）。</summary>
        private static void Caps(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            Line(vh, a, b, width, c);
            Disc(vh, a, width * .5f, c);
            Disc(vh, b, width * .5f, c);
        }

        private static void Circle(VertexHelper vh, Vector2 center, float radius, float width, Color c)
        {
            Arc(vh, center, radius, 0, 360, width, c);
        }

        private static void Arc(VertexHelper vh, Vector2 center, float radius, float a0, float a1, float width, Color c)
        {
            int seg = Mathf.Max(10, Mathf.RoundToInt(Mathf.Abs(a1 - a0) / 12f));
            Vector2 prev = center + Dir(a0) * radius;
            for (int i = 1; i <= seg; i++)
            {
                Vector2 now = center + Dir(Mathf.Lerp(a0, a1, i / (float)seg)) * radius;
                Line(vh, prev, now, width, c);
                prev = now;
            }
            Disc(vh, center + Dir(a0) * radius, width * .5f, c);
            Disc(vh, center + Dir(a1) * radius, width * .5f, c);
        }

        private static void Disc(VertexHelper vh, Vector2 center, float radius, Color c)
        {
            const int seg = 26;
            int start = vh.currentVertCount;
            vh.AddVert(center, c, Vector2.zero);
            for (int i = 0; i <= seg; i++) vh.AddVert(center + Dir(i * 360f / seg) * radius, c, Vector2.zero);
            for (int i = 0; i < seg; i++) vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        private static Vector2 Dir(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a, n = new Vector2(-d.y, d.x).normalized * width * .5f;
            Quad(vh, a - n, a + n, b + n, b - n, c, c);
        }
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color bottom, Color top)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, bottom, Vector2.zero); vh.AddVert(b, top, Vector2.zero);
            vh.AddVert(c, top, Vector2.zero); vh.AddVert(d, bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>旧版切角面/切角框：仅保留给尚未迁移的自定义调用，按钮已改用圆角。</summary>
        private static void LegacyCut(VertexHelper vh, Rect r, Color c)
        {
            float cut = Mathf.Min(14, Mathf.Min(r.width, r.height) * .16f);
            var pts = new List<Vector2> {
                new Vector2(r.xMin + 2, r.yMin + 2), new Vector2(r.xMin + 2, r.yMax - cut),
                new Vector2(r.xMin + cut, r.yMax - 2), new Vector2(r.xMax - 2, r.yMax - 2),
                new Vector2(r.xMax - 2, r.yMin + cut), new Vector2(r.xMax - cut, r.yMin + 2) };
            Fill(vh, pts, c);
        }

        private static void Ocean(VertexHelper vh, Rect r)
        {
            Vector2 P(float x, float y) => new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
            Quad(vh, P(0, 0), P(0, 1), P(1, 1), P(1, 0), new Color(0.063f, 0.102f, 0.133f), new Color(0.082f, 0.176f, 0.204f));
            for (int i = 0; i < 5; i++)
                Quad(vh, P(.05f + i * .19f, 1), P(.12f + i * .19f, 1), P(.52f + i * .14f, 0), P(.42f + i * .14f, 0),
                     new Color(.35f, .8f, .79f, .035f), new Color(.2f, .6f, .7f, 0));
            for (int band = 0; band < 12; band++)
            {
                Vector2 last = P(0, 0);
                for (int j = 0; j <= 56; j++)
                {
                    float x = j / 56f;
                    float y = .055f + band * .025f + .052f * Mathf.Sin(x * 7 + band * .15f) + .019f * Mathf.Sin(x * 17 + band * .26f);
                    Vector2 now = P(x, y);
                    if (j > 0) Line(vh, last, now, 1.3f, new Color(.29f, .62f, .65f, .13f));
                    last = now;
                }
            }
            for (int i = 0; i < 78; i++)
            {
                float x = Mathf.Repeat(i * .6180339f, 1), y = Mathf.Repeat(i * .381966f + .13f, 1);
                if (x > .22f && x < .78f) continue;
                Vector2 speck = P(x, y);
                float size = i % 4 == 0 ? 2 : 1;
                Line(vh, speck - Vector2.up * size, speck + Vector2.up * size, 1, new Color(.62f, .86f, .82f, .24f));
            }
        }
    }
}
