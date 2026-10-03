#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 安全区换算自检（异形屏适配）。只测纯函数，不需要真机也不需要场景：
    ///   ① 无刘海机型（safeArea 满屏）→ anchors 必须正好 0..1（适配器不能挪动画面）；
    ///   ② 典型挖孔机 1080×2400、上下各留 80px → anchors y = 0.0333..0.9667；
    ///   ③ 横屏（宽 2400、safeArea 左右留 100px）→ anchors x 正确、y 仍是 0..1；
    ///   ④ 极端窄长屏（1080×2560、safeArea 只留顶 120px）→ 不越界 [0,1]。
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.SafeAreaChecks.Run）两用。
    /// </summary>
    public static class SafeAreaChecks
    {
        [MenuItem("Tools/FallenAngel/Validation/Visuals/Validate Safe Area", false, 602)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-18} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            Vector2 min, max;

            SafeAreaFitter.ComputeAnchors(new Rect(0f, 0f, 1080f, 1920f), new Vector2(1080f, 1920f), out min, out max);
            check("满屏不挪动", Near(min.x, 0f) && Near(min.y, 0f) && Near(max.x, 1f) && Near(max.y, 1f),
                string.Format("{0}..{1}", min, max));

            SafeAreaFitter.ComputeAnchors(new Rect(0f, 80f, 1080f, 2240f), new Vector2(1080f, 2400f), out min, out max);
            check("挖孔机上下留白", Near(min.y, 80f / 2400f) && Near(max.y, 2320f / 2400f),
                string.Format("1080×2400 → {0}..{1}（y 期望 {2:0.####}..{3:0.####}）",
                    min, max, 80f / 2400f, 2320f / 2400f));

            SafeAreaFitter.ComputeAnchors(new Rect(100f, 0f, 2200f, 1080f), new Vector2(2400f, 1080f), out min, out max);
            check("横屏左右留白", Near(min.x, 100f / 2400f) && Near(max.x, 2300f / 2400f) && Near(min.y, 0f),
                string.Format("2400×1080 → {0}..{1}", min, max));

            SafeAreaFitter.ComputeAnchors(new Rect(0f, 120f, 1080f, 2440f), new Vector2(1080f, 2560f), out min, out max);
            check("极端长屏不越界", InRange(min) && InRange(max),
                string.Format("1080×2560 → {0}..{1}", min, max));

            string summary = string.Format("安全区自检：{0} 项通过 / {1} 项异常（判定：满屏不挪动 + 挖孔/横屏/长屏 anchors 正确且不越界）", pass, fail);
            foreach (string line in lines) Debug.Log("[SafeArea] " + line);
            Debug.Log("[SafeArea] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/safe_area_check.txt", lines.ToArray());
                File.AppendAllText("Logs/safe_area_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e) { Debug.LogWarning("[SafeArea] 报告写入失败：" + e.Message); }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static bool Near(float a, float b) { return Mathf.Abs(a - b) <= 0.0005f; }
        private static bool InRange(Vector2 v)
        {
            return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }
    }
}
#endif
