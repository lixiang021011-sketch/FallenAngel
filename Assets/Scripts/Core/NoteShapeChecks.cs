#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Data;
using FallenAngel.UI;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 音符外观配表与形状几何自检。
    ///
    /// 为什么需要：外观已改成**配表驱动**（NoteVisualConfig）。配表与几何一旦对不上，
    /// 症状是"某类音符画不出来或画错"——要等那类音符出现才会被发现，代价高。
    /// 这里逐条静态校验，将来批量替换成美术素材前也能提前发现问题。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.NoteShapeChecks.Run
    /// </summary>
    public static class NoteShapeChecks
    {
        private static readonly string[] RequiredKeys =
        {
            "Normal", "Drag", "Flick", "LongStart", "LongEnd", "Slide", "SlideTail", "Wide",
        };

        [MenuItem("Tools/FallenAngel/Validate Note Shapes")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-20} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // ① 配表完整性：每种音符都必须有对应行
            var missing = new List<string>();
            foreach (string key in RequiredKeys)
            {
                if (NoteVisualConfig.Find(key) == null) missing.Add(key);
            }
            check("配表完整性", missing.Count == 0,
                missing.Count == 0
                    ? string.Format("{0} 类音符全部有外观行", RequiredKeys.Length)
                    : "缺少：" + string.Join("/", missing));

            // ② 每行形状都要能产出几何，且不超出包围盒 + 多边形为凸
            var shapeProblems = new List<string>();
            var shapeCount = new Dictionary<NoteShapeKind, int>();
            foreach (NoteVisualRow row in NoteVisualConfig.Rows)
            {
                if (!row.Enabled || row.Shape == NoteShapeKind.RoundedBar) continue;
                if (shapeCount.ContainsKey(row.Shape)) continue;
                shapeCount[row.Shape] = 0;

                List<Vector2[]> polys = NoteShapeGeometry.Build(row.Shape, row.Width, row.Height);
                if (polys.Count == 0) { shapeProblems.Add(row.Shape + ":空"); continue; }

                float hw = row.Width * 0.5f + 0.01f, hh = row.Height * 0.5f + 0.01f;
                foreach (Vector2[] p in polys)
                {
                    if (p.Length < 3) { shapeProblems.Add(row.Shape + ":顶点不足"); break; }
                    foreach (Vector2 v in p)
                    {
                        if (Mathf.Abs(v.x) > hw || Mathf.Abs(v.y) > hh)
                        {
                            shapeProblems.Add(string.Format("{0}:溢出({1:0.#},{2:0.#})", row.Shape, v.x, v.y));
                            break;
                        }
                    }
                    if (!IsConvex(p)) shapeProblems.Add(row.Shape + ":非凸");
                }
            }
            check("形状几何可用", shapeProblems.Count == 0,
                shapeProblems.Count == 0
                    ? string.Format("{0} 种形状：非空 / 不溢出包围盒 / 全为凸多边形", shapeCount.Count)
                    : "问题：" + string.Join("；", shapeProblems));

            // ③ 形状必须互不相同——否则类型区分失效（作者反馈过区分不明显）
            var signatures = new Dictionary<string, NoteShapeKind>();
            var dup = new List<string>();
            foreach (NoteVisualRow row in NoteVisualConfig.Rows)
            {
                if (!row.Enabled) continue;
                List<Vector2[]> polys = NoteShapeGeometry.Build(row.Shape, 130f, 28f);
                var sb = new System.Text.StringBuilder();
                foreach (Vector2[] p in polys)
                {
                    sb.Append(p.Length).Append(':');
                    foreach (Vector2 v in p)
                    {
                        sb.Append(Mathf.RoundToInt(v.x * 10f)).Append(',').Append(Mathf.RoundToInt(v.y * 10f)).Append(';');
                    }
                }
                string sig = sb.ToString();
                if (signatures.ContainsKey(sig)) dup.Add(row.NoteType + "≡" + signatures[sig]);
                else signatures[sig] = row.Shape;
            }
            check("类型形状互不相同", dup.Count == 0,
                dup.Count == 0
                    ? string.Format("{0} 类音符形状签名唯一", signatures.Count)
                    : "重复：" + string.Join("/", dup));

            // ④ 尺寸：厚度已按反馈 ×2（tap ≥28）
            NoteVisualRow tap = NoteVisualConfig.Find("Normal");
            check("厚度 ×2 已生效", tap != null && tap.Height >= 28f - 0.01f,
                tap != null ? string.Format("Normal = {0:0}×{1:0}", tap.Width, tap.Height) : "缺 Normal 行");

            string summary = string.Format(
                "音符外观配表自检：{0} 项通过 / {1} 项异常（判定：配表完整 + 几何非空/不溢出/凸 + 形状互异 + 厚度已加倍）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[NoteShape] " + line);
            Debug.Log("[NoteShape] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/note_shape_check.txt", lines.ToArray());
                File.AppendAllText("Logs/note_shape_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NoteShape] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>凸性：所有相邻边的叉积同号（允许共线）</summary>
        private static bool IsConvex(Vector2[] p)
        {
            int sign = 0;
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[(i + 1) % p.Length] - p[i];
                Vector2 b = p[(i + 2) % p.Length] - p[(i + 1) % p.Length];
                float cross = a.x * b.y - a.y * b.x;
                if (Mathf.Abs(cross) < 1e-4f) continue;
                int s = cross > 0f ? 1 : -1;
                if (sign == 0) sign = s;
                else if (sign != s) return false;
            }
            return true;
        }
    }
}
#endif
