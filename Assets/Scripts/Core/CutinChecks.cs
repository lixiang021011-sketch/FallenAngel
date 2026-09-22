#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// Cut-in 自检（前端对照清单 E6/E7）。判据：
    ///   · 同时只显示一张、后来的排队（上游 CutinManager 也是一条演出队列）；
    ///   · 同一 sourceId 在显示期间重复请求被忽略（防刷屏）；
    ///   · 连击里程碑按 `CutinService.ComboMilestone` 触发且**不重复触发**同一档；
    ///   · 局内道具走同一条链：`DemoItems.UseEnergyDrink()` → modifier 生效 + 一次 cut-in。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.CutinChecks.Run
    /// </summary>
    public static class CutinChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Cutin and Demo Item")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            ModifierManager mgr = null;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                mgr = ModifierManager.EnsureInstance();
                mgr.ClearAll();
                CutinService.Reset();

                var started = new List<string>();
                CutinService.OnCutinStarted += r => started.Add(r.sourceId);

                // ① 一次请求 → 立即开始
                CutinService.Play(CutinRequest.Make("A", "icon", "title", null, 1.0f));
                check("queue.startsImmediately",
                    CutinService.IsPlaying && started.Count == 1 && started[0] == "A",
                    "请求 A → 立即播放（队列 " + CutinService.PendingCount + "）");

                // ② 播放期间再请求 → 排队，不打断当前
                CutinService.Play(CutinRequest.Make("B", "icon", "title", null, 1.0f));
                check("queue.waitsForCurrent",
                    started.Count == 1 && CutinService.PendingCount == 2,
                    "播放中请求 B → 排队（队列 " + CutinService.PendingCount + "）");

                // ③ 同一 sourceId 在播放期间重复请求 → 忽略
                CutinService.Play(CutinRequest.Make("A", "icon", "title", null, 1.0f));
                check("queue.dedupeSameSource",
                    CutinService.PendingCount == 2,
                    "重复请求 A → 忽略（队列仍为 " + CutinService.PendingCount + "）");

                // ④ 时长走完 → 自动接下一个
                CutinService.Tick(1.1f);
                bool secondStarted = started.Count == 2 && started[1] == "B";
                CutinService.Tick(1.1f);
                bool drained = !CutinService.IsPlaying && CutinService.PendingCount == 0;
                check("queue.advances", secondStarted && drained,
                    "1.0s 后接 B；再 1.0s 后队列清空（已播 " + string.Join(",", started) + "）");

                // ⑤ 连击里程碑：49 不触发、50 触发、51 不重复
                CutinService.Reset();
                int before = started.Count;
                CutinService.NotifyCombo(49);
                bool below = started.Count == before;
                CutinService.NotifyCombo(CutinService.ComboMilestone);
                bool hit = started.Count == before + 1 && started[started.Count - 1] == "combo";
                CutinService.NotifyCombo(CutinService.ComboMilestone + 1);
                bool notRepeated = started.Count == before + 1;
                check("combo.milestone", below && hit && notRepeated,
                    string.Format("{0} 连击触发一次、相邻连击不重复（已播 {1} 次）",
                        CutinService.ComboMilestone, started.Count - before));

                // ⑥ 局内道具：modifier 三档生效 + 一次 cut-in（这就是"道具→buff→演出"的整条链）
                CutinService.Reset();
                mgr.ClearAll();
                int beforeItem = started.Count;
                bool used = DemoItems.UseEnergyDrink();
                bool scoreOk = Mathf.Abs(PlayRules.ScoreMultiplier - 1.1f) < 1e-3f;
                bool hurtOk = Mathf.Abs(PlayRules.LifeDamageScale - 0.5f) < 1e-3f;
                bool regenOk = Mathf.Abs(PlayRules.LifeRegenPerSecond - 5f) < 1e-3f;
                bool cutinOk = started.Count == beforeItem + 1;
                check("item.energyDrink",
                    used && scoreOk && hurtOk && regenOk && cutinOk,
                    string.Format("道具生效：分 ×{0:0.00} / 受伤 ×{1:0.00} / 回血 {2:0.#}s；cut-in {3}",
                        PlayRules.ScoreMultiplier, PlayRules.LifeDamageScale,
                        PlayRules.LifeRegenPerSecond, cutinOk ? "已弹" : "没弹"));

                // ⑦ debuff 同链路：受伤 ×1.5
                mgr.ClearAll();
                DemoItems.ApplyFragileDebuff();
                check("item.fragileDebuff",
                    Mathf.Abs(PlayRules.LifeDamageScale - 1.5f) < 1e-3f,
                    "debuff 生效：受伤 ×" + PlayRules.LifeDamageScale.ToString("0.00"));

                // ⑧ 道具到期后回到原值（时间轴 = SongTime，由 ModifierManager 自己 Tick）
                mgr.Tick(DemoItems.EnergyDrinkDuration + 1f);
                check("item.expires",
                    Mathf.Abs(PlayRules.ScoreMultiplier - 1f) < 1e-3f,
                    "时长走完后分数倍率回到 " + PlayRules.ScoreMultiplier.ToString("0.00"));

                // ⑨ 迟到订阅也能追上（真机踩过：道具在菜单期触发，HUD 到演奏开始才 OnEnable）
                CutinService.Reset();
                CutinService.Play(CutinRequest.Make("LATE", "icon", "title", null, 1.0f));
                CutinRequest got;
                bool caught = CutinService.TryGetCurrent(out got) && got.sourceId == "LATE"
                              && CutinService.CurrentRemaining > 0f;
                check("lateSubscribe.catchUp", caught,
                    string.Format("TryGetCurrent → {0}（剩余 {1:0.0}s）", got.sourceId, CutinService.CurrentRemaining));
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-28} 自检异常：{1}", "执行", e));
            }
            finally
            {
                if (mgr != null) mgr.ClearAll();
                CutinService.Reset();
                PlayRules.ResetModifiers();
            }

            string summary = string.Format(
                "Cut-in 与局内道具自检：{0} 项通过 / {1} 项异常"
                + "（判定：队列先进先出 + 同源去重 + 连击里程碑不重复 + 道具三档 modifier 生效与到期 + debuff 同链路）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Cutin] " + line);
            Debug.Log("[Cutin] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/cutin_check.txt", lines.ToArray());
                File.AppendAllText("Logs/cutin_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cutin] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
