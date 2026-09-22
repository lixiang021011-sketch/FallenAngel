#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 生命值自检（前端对照清单 B4）。判据来自上游 `LiveConfig`：
    ///   `Life = 1000`、`Damages = { Bad: 60, Miss: 80 }`；
    /// 归零后果按作者定案 —— **不结算、继续打完**（练习模式口径，见 `PlayLife` 类注释里的出处说明）。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.LifeChecks.Run
    /// </summary>
    public static class LifeChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Life and Damage")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                // ① 数值照抄上游
                check("value.maxLife", Mathf.Abs(PlayLife.MaxLife - 1000f) < 1e-3f,
                    "MaxLife = " + PlayLife.MaxLife.ToString("0") + "（上游 LiveConfig.Life）");
                check("value.damageTable",
                    PlayLife.DamageBad == 60 && PlayLife.DamageMiss == 80,
                    string.Format("Bad −{0} / Miss −{1}（上游 LiveConfig.Damages）",
                        PlayLife.DamageBad, PlayLife.DamageMiss));

                // ② 开局满血
                PlayRules.ResetModifiers();
                PlayLife.Reset();
                check("reset.full", Mathf.Abs(PlayLife.Current - 1000f) < 1e-3f && Mathf.Abs(PlayLife.Rate - 1f) < 1e-3f,
                    "Reset 后 " + PlayLife.Current.ToString("0") + "（rate " + PlayLife.Rate.ToString("0.00") + "）");

                // ③ 单次伤害：Miss −80、Bad −60
                PlayLife.ApplyDamage(PlayLife.DamageMiss);
                bool missOk = Mathf.Abs(PlayLife.Current - 920f) < 1e-3f
                              && Mathf.Abs(PlayLife.Rate - 0.92f) < 1e-3f;
                PlayLife.ApplyDamage(PlayLife.DamageBad);
                bool badOk = Mathf.Abs(PlayLife.Current - 860f) < 1e-3f;
                check("damage.missAndBad", missOk && badOk,
                    string.Format("1000 → Miss → {0}（rate {1:0.00}）→ Bad → {2}",
                        920, 0.92f, PlayLife.Current.ToString("0")));

                // ④ 受伤倍率（modifier 口子）：0.5× 只吃一半
                PlayRules.ResetModifiers();
                PlayLife.Reset();
                PlayRules.SetModifierState(1f, true, true, 0.5f, 0f, 1f);   // 模拟 ModifierManager 汇总结果
                PlayLife.ApplyDamage(PlayLife.DamageMiss);
                check("modifier.damageScale",
                    Mathf.Abs(PlayLife.Current - 960f) < 1e-3f,
                    "受伤倍率 0.5× → Miss 只扣 40（" + PlayLife.Current.ToString("0") + "）");

                // ⑤ 每秒回血（modifier 口子）：+25/s 跑 2 秒 = +50
                PlayLife.Reset();
                PlayRules.SetModifierState(1f, true, true, 1f, 25f, 1f);
                PlayLife.ApplyDamage(PlayLife.DamageMiss);   // 920
                PlayLife.Tick(2f);
                check("modifier.regen",
                    Mathf.Abs(PlayLife.Current - 970f) < 1e-3f,
                    "+25/s × 2s → " + PlayLife.Current.ToString("0") + "（上限 1000）");

                PlayLife.Tick(100f);
                check("regen.clampToMax", Mathf.Abs(PlayLife.Current - PlayLife.MaxLife) < 1e-3f,
                    "长时间回血夹在 " + PlayLife.Current.ToString("0"));

                // ⑥ 归零：不结算、夹在 0、继续扣还是 0（作者定案 = 练习模式口径）
                PlayRules.ResetModifiers();
                PlayLife.Reset();
                for (int i = 0; i < 13; i++) PlayLife.ApplyDamage(PlayLife.DamageMiss);   // 13×80 = 1040 > 1000
                bool zeroOk = Mathf.Abs(PlayLife.Current) < 1e-3f && PlayLife.IsEmpty
                              && Mathf.Abs(PlayLife.Rate) < 1e-3f;
                PlayLife.ApplyDamage(PlayLife.DamageBad);
                check("zero.noFailAndClamp",
                    zeroOk && Mathf.Abs(PlayLife.Current) < 1e-3f,
                    "13 次 Miss → " + PlayLife.Current.ToString("0") + "（夹在 0，不触发结算，再扣还是 0）");

                // ⑦ 生命槽 UI 读的是 PlayLife.Rate（唯一数据源）——这里验 rate 与 current 同步
                PlayLife.Reset();
                PlayLife.ApplyDamage(PlayLife.DamageBad);   // 940
                check("rate.matchesCurrent",
                    Mathf.Abs(PlayLife.Rate - PlayLife.Current / PlayLife.MaxLife) < 1e-4f,
                    "rate = current/max = " + PlayLife.Rate.ToString("0.000"));

                // ⑧ 事件广播（UI 订阅用）
                int events = 0;
                Action<float, float> handler = (c, m) => events++;
                PlayLife.OnChanged += handler;
                PlayLife.Reset();
                PlayLife.ApplyDamage(PlayLife.DamageMiss);
                PlayLife.OnChanged -= handler;
                check("event.changed", events >= 2, "Reset + 受伤各广播一次（收到 " + events + " 次）");
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-28} 自检异常：{1}", "执行", e));
            }
            finally
            {
                PlayRules.ResetModifiers();
                PlayLife.Reset();
            }

            string summary = string.Format(
                "生命值自检：{0} 项通过 / {1} 项异常"
                + "（判定：数值照抄上游 1000/Bad−60/Miss−80 + 受伤倍率与回血的 modifier 口子 + 归零不结算）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Life] " + line);
            Debug.Log("[Life] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/life_check.txt", lines.ToArray());
                File.AppendAllText("Logs/life_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Life] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
