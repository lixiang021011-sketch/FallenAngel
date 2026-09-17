using System.Collections.Generic;
using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// 音符形状几何：把 <see cref="NoteShapeKind"/> 变成"若干凸多边形"（坐标以矩形中心为原点）。
    /// 所有形状都**不超出 w×h 的包围盒**——这是需求的硬约束（不允许溢出音符矩形）。
    /// 纯函数，无副作用，便于自检逐形状验证。
    /// </summary>
    public static class NoteShapeGeometry
    {
        /// <summary>切角矩形（8 点，凸）</summary>
        public static Vector2[] ChamferRect(float x0, float y0, float x1, float y1, float cut)
        {
            cut = Mathf.Min(cut, Mathf.Min(x1 - x0, y1 - y0) * 0.49f);
            return new[]
            {
                new Vector2(x0 + cut, y0), new Vector2(x1 - cut, y0),
                new Vector2(x1, y0 + cut), new Vector2(x1, y1 - cut),
                new Vector2(x1 - cut, y1), new Vector2(x0 + cut, y1),
                new Vector2(x0, y1 - cut), new Vector2(x0, y0 + cut),
            };
        }

        /// <summary>按形状产出凸多边形列表（每个多边形可安全用扇形三角化）</summary>
        public static List<Vector2[]> Build(NoteShapeKind kind, float w, float h)
        {
            var polys = new List<Vector2[]>();
            float hw = w * 0.5f, hh = h * 0.5f;
            float cut = Mathf.Max(2f, h * 0.28f);

            switch (kind)
            {
                case NoteShapeKind.SplitBar:
                {
                    // 中间断开两截：语义 = 穿过去（与 tap 的实心条轮廓完全不同）
                    float gap = Mathf.Clamp(w * 0.16f, 6f, w * 0.3f);
                    float seg = (w - gap) * 0.5f;
                    polys.Add(ChamferRect(-hw, -hh, -hw + seg, hh, cut));
                    polys.Add(ChamferRect(hw - seg, -hh, hw, hh, cut));
                    break;
                }
                case NoteShapeKind.PointedBar:
                {
                    // 两端尖角六边形：语义 = 甩出去
                    float tip = Mathf.Min(w * 0.12f, hh);
                    polys.Add(new[]
                    {
                        new Vector2(-hw + tip, -hh), new Vector2(hw - tip, -hh),
                        new Vector2(hw, 0f), new Vector2(hw - tip, hh),
                        new Vector2(-hw + tip, hh), new Vector2(-hw, 0f),
                    });
                    break;
                }
                case NoteShapeKind.StepHead:
                {
                    // 左端加厚台阶：按住从这里开始
                    float block = Mathf.Clamp(w * 0.18f, 8f, w * 0.4f);
                    float thin = hh * 0.72f;
                    polys.Add(ChamferRect(-hw, -hh, -hw + block, hh, cut));
                    polys.Add(ChamferRect(-hw + block, -thin, hw, thin, cut * 0.7f));
                    break;
                }
                case NoteShapeKind.StepTail:
                {
                    float block = Mathf.Clamp(w * 0.18f, 8f, w * 0.4f);
                    float thin = hh * 0.72f;
                    polys.Add(ChamferRect(-hw, -thin, hw - block, thin, cut * 0.7f));
                    polys.Add(ChamferRect(hw - block, -hh, hw, hh, cut));
                    break;
                }
                case NoteShapeKind.SlantHead:
                {
                    // 右端斜切梯形：路径起点
                    float d = Mathf.Clamp(w * 0.22f, 8f, w * 0.45f);
                    polys.Add(new[]
                    {
                        new Vector2(-hw, -hh), new Vector2(hw - d, -hh),
                        new Vector2(hw, hh), new Vector2(-hw, hh),
                    });
                    break;
                }
                case NoteShapeKind.ArrowTail:
                {
                    // 右端箭头：路径终点
                    float d = Mathf.Clamp(w * 0.28f, 8f, w * 0.5f);
                    polys.Add(new[]
                    {
                        new Vector2(-hw, -hh), new Vector2(hw - d, -hh),
                        new Vector2(hw, 0f), new Vector2(hw - d, hh),
                        new Vector2(-hw, hh),
                    });
                    break;
                }
                case NoteShapeKind.WideBeam:
                {
                    // 横梁 + 中央凸块（块高 = 满高，梁 = 0.66 高，都不超出包围盒）
                    float barH = hh * 0.66f;
                    float bw = Mathf.Clamp(w * 0.05f, 10f, 60f);
                    polys.Add(new Vector2[]
                    {
                        new Vector2(-hw, -barH), new Vector2(hw, -barH),
                        new Vector2(hw, barH), new Vector2(-hw, barH),
                    });
                    polys.Add(new Vector2[]
                    {
                        new Vector2(-bw, -hh), new Vector2(bw, -hh),
                        new Vector2(bw, hh), new Vector2(-bw, hh),
                    });
                    break;
                }
                default:
                    polys.Add(ChamferRect(-hw, -hh, hw, hh, cut));
                    break;
            }
            return polys;
        }

        /// <summary>把一组多边形按比例缩放（用于生成内层高光：向中心缩，形状保持一致）</summary>
        public static List<Vector2[]> Scale(List<Vector2[]> polys, float kx, float ky)
        {
            var outList = new List<Vector2[]>(polys.Count);
            foreach (Vector2[] p in polys)
            {
                var q = new Vector2[p.Length];
                for (int i = 0; i < p.Length; i++) q[i] = new Vector2(p[i].x * kx, p[i].y * ky);
                outList.Add(q);
            }
            return outList;
        }
    }
}
