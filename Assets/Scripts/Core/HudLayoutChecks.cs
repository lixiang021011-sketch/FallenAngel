#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏页 HUD **布局**自检（前端清单 B5 总装）。
    ///
    /// 为什么需要：以前每个元素的锚点散在 `SceneBuilder.CreateHUD` 里，元素一多就互相压 ——
    /// 本次实测出 6 处重叠（cut-in 压 combo、蓄力条压生命槽、800 宽的分数文本框把右上一整片占了）。
    /// 收成 `HudLayout` 一张表之后，这里按表做两件事：
    ///   ① **两两不重叠**（同一屏幕上同时出现的元素不能盖住对方）；
    ///   ② **不出画布**（含左右各 20px 的安全边距）。
    /// 以后谁挪元素忘了检查，这条自检会红。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.HudLayoutChecks.Run
    /// </summary>
    public static class HudLayoutChecks
    {
        private const float Margin = 20f;

        [MenuItem("Tools/FallenAngel/Validation/Visuals/Validate HUD Layout", false, 600)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                HudElement[] all = HudLayout.Elements;

                // ① 两两不重叠
                var overlaps = new List<string>();
                for (int i = 0; i < all.Length; i++)
                {
                    for (int j = i + 1; j < all.Length; j++)
                    {
                        if (all[i].Overlaps(all[j]))
                            overlaps.Add(all[i].name + "×" + all[j].name);
                    }
                }
                check("元素两两不重叠", overlaps.Count == 0,
                    overlaps.Count == 0
                        ? all.Length + " 个元素、两两矩形相交 0 处"
                        : "重叠：" + string.Join("；", overlaps));

                // ② 不出画布（含 20px 边距）
                var outside = new List<string>();
                for (int i = 0; i < all.Length; i++)
                {
                    HudElement e = all[i];
                    float left = e.x - e.w * 0.5f, right = e.x + e.w * 0.5f;
                    float top = e.y + e.h * 0.5f, bottom = e.y - e.h * 0.5f;
                    if (left < -HudLayout.CanvasWidth * 0.5f + Margin
                        || right > HudLayout.CanvasWidth * 0.5f - Margin
                        || top > HudLayout.CanvasHeight * 0.5f - Margin
                        || bottom < -HudLayout.CanvasHeight * 0.5f + Margin)
                    {
                        outside.Add(string.Format("{0}(L{1:0.#} R{2:0.#} T{3:0.#} B{4:0.#})",
                            e.name, left, right, top, bottom));
                    }
                }
                check("元素不出安全边距", outside.Count == 0,
                    outside.Count == 0 ? "全部在 " + Margin + "px 边距之内" : "越界：" + string.Join("、", outside));

                // ③ 判定反馈区位置没被这次总装动过（作者已过稿的位置）
                HudElement combo = HudLayout.Get("combo数字位");
                HudElement judge = HudLayout.Get("判定文字");
                HudElement bias = HudLayout.Get("早晚描述");
                check("判定反馈位置未变",
                    Mathf.Abs(combo.y - 250f) < 0.1f && Mathf.Abs(judge.y - 100f) < 0.1f
                    && Mathf.Abs(bias.y - 40f) < 0.1f,
                    string.Format("combo +{0:0} / 判定文字 +{1:0} / 早晚描述 +{2:0}（过稿位置）",
                        combo.y, judge.y, bias.y));

                // ④ 顶部三行顺序正确（第一行在第二行之上、第二行在第三行之上，且都不压进度条）
                check("顶部行序",
                    HudLayout.Row1Y > HudLayout.Row2Y && HudLayout.Row2Y > HudLayout.Row3Y
                    && HudLayout.Row1Y + 28f < 930f,
                    string.Format("第一行 +{0:0} > 第二行 +{1:0} > 第三行 +{2:0}；进度条下沿 +930",
                        HudLayout.Row1Y, HudLayout.Row2Y, HudLayout.Row3Y));

                // ⑤ combo 与 cut-in 的间距（本次修的重叠：卡片要在 combo 之上且留空隙）
                HudElement cutin = HudLayout.Get("cut-in卡片");
                float gap = (cutin.y - cutin.h * 0.5f) - (combo.y + combo.h * 0.5f);
                check("cut-in 不压 combo", gap > 0f,
                    string.Format("卡片下沿 +{0:0}，combo 上沿 +{1:0}，间隙 {2:0}px",
                        cutin.y - cutin.h * 0.5f, combo.y + combo.h * 0.5f, gap));
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }

            string summary = string.Format(
                "HUD 布局自检：{0} 项通过 / {1} 项异常"
                + "（判定：两两不重叠 + 不出安全边距 + 判定反馈位置未变 + 顶部行序 + cut-in 不压 combo）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[HudLayout] " + line);
            Debug.Log("[HudLayout] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/hud_layout_check.txt", lines.ToArray());
                File.AppendAllText("Logs/hud_layout_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HudLayout] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
