#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 局内 modifier 自检：判定窗口缩放、判定线/谱面可见性、按歌曲时间失效、同 id 刷新不叠加。
    ///
    /// 只读 + 临时对象；不改场景与资源。菜单与 batchmode
    /// （-executeMethod FallenAngel.Core.PlayModifierChecks.Run）两用。
    /// </summary>
    public static class PlayModifierChecks
    {
        private const float Tol = 1e-4f;

        [MenuItem("Tools/FallenAngel/Validate Play Modifiers")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            GameObject host = null;
            JudgeWindows savedBase = null;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-22} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                savedBase = JudgeWindows.Default;
                var baseWindows = new JudgeWindows();   // 0.08 / 0.16 / 0.18 / 0.20
                PlayRules.SetBaseWindows(baseWindows);

                host = new GameObject("__ModifierCheckHost");
                host.SetActive(false);                  // 不让 Update 跑，时序由自检显式控制
                var mgr = host.AddComponent<ModifierManager>();

                check("基线窗口", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f) <= Tol
                        && Mathf.Abs(PlayRules.Windows.badWindow - 0.20f) <= Tol,
                    string.Format("perfect={0:0.###} / bad={1:0.###}", PlayRules.Windows.perfectWindow, PlayRules.Windows.badWindow));

                mgr.Apply(new ModifierDef("M_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f), 0f);
                check("判定窗口放宽 1.5×", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.12f) <= Tol,
                    string.Format("perfect 0.08 → {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_LOOSE2", ModifierKind.JudgeWindowScale, 2f, 0f), 0f);
                check("窗口倍率乘法叠加", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol,
                    string.Format("0.08 × 1.5 × 2 = {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f), 0f);
                check("同 id 刷新不叠加强度", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol,
                    string.Format("重复施加后仍为 {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_NOLINE", ModifierKind.JudgeLineHidden, 0f, 0f), 0f);
                check("判定线消失", !PlayRules.JudgeLineVisible, "JudgeLineVisible=false（判定逻辑不受影响）");

                mgr.Apply(new ModifierDef("M_NONOTES", ModifierKind.NotesHidden, 0f, 0f), 0f);
                check("谱面隐身", !PlayRules.NotesVisible, "NotesVisible=false（位置与判定照常）");

                // 到时失效：duration=1.0，从 t=40 起算。
                // 用窗口倍率测（此时常驻倍率为 1.5×2=0.24），避免被上面那条常驻的"判定线消失"干扰。
                mgr.Apply(new ModifierDef("M_TEMP", ModifierKind.JudgeWindowScale, 3f, 1.0f), 40f);
                mgr.Tick(40.5f);
                bool stillScaled = Mathf.Abs(PlayRules.Windows.perfectWindow - 0.72f) <= Tol;
                mgr.Tick(41.0f);
                bool reverted = Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol;
                check("按时失效", stillScaled && reverted,
                    string.Format("t=40.5 仍生效={0}（0.72）、t=41.0 已失效={1}（{2:0.###}）",
                        stillScaled, reverted, PlayRules.Windows.perfectWindow));

                mgr.ClearAll();
                check("清空效果回到基线",
                    Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f) <= Tol
                    && PlayRules.JudgeLineVisible && PlayRules.NotesVisible,
                    string.Format("perfect={0:0.###}、线可见={1}、音符可见={2}",
                        PlayRules.Windows.perfectWindow, PlayRules.JudgeLineVisible, PlayRules.NotesVisible));
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-22} 自检异常：{1}", "执行", e.Message));
            }
            finally
            {
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }

            string summary = string.Format(
                "局内 modifier 自检：{0} 项通过 / {1} 项异常（判定：基础值+修改器、倍率乘法叠加、同 id 刷新、按时失效、清空复位）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[PlayModifier] " + line);
            Debug.Log("[PlayModifier] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/play_modifier_check.txt", lines.ToArray());
                File.AppendAllText("Logs/play_modifier_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayModifier] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
