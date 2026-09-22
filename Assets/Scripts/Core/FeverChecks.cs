#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Data;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// Fever 自检（前端对照清单 C4）。判据：
    ///   · 触发方式照抄上游 = **谱面事件** `fever.begin / fever.start / fever.end`
    ///     （上游 `Sekai/Live/FeverBeginEvent.cs` 等三个 EventBase）；
    ///   · 倍率走 modifier 口子（作者定案 Q3）：Active 时挂 `FEVER_SCORE`（ScoreMultiplier），End 时撤掉；
    ///   · 重复 start 幂等（不会挂出两倍）。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.FeverChecks.Run
    /// </summary>
    public static class FeverChecks
    {
        private static ChartEvent Ev(float t, string type)
        {
            return new ChartEvent { time = t, type = type, value = 0f };
        }

        [MenuItem("Tools/FallenAngel/Validate Fever")]
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
                Fever.Reset();

                // ① 事件派发器：按时间顺序、只派发一次、不到时间不派发
                var driver = new LiveEventDriver();
                var seen = new List<string>();
                driver.Load(new List<ChartEvent>
                {
                    Ev(10f, "fever.start"),
                    Ev(5f, "fever.begin"),
                    Ev(20f, "fever.end"),
                });
                driver.Register("fever.begin", e => seen.Add("begin@" + e.time));
                driver.Register("fever.start", e => seen.Add("start@" + e.time));
                driver.Register("fever.end", e => seen.Add("end@" + e.time));

                driver.Advance(4.9f);
                bool noneYet = seen.Count == 0 && driver.PendingCount == 3;
                driver.Advance(10f);
                bool twoDone = seen.Count == 2 && driver.PendingCount == 1;
                driver.Advance(10f);                       // 同一时间再推一次不应重复派发
                bool noRepeat = seen.Count == 2;
                driver.Advance(100f);
                bool allDone = seen.Count == 3 && driver.PendingCount == 0
                               && seen[0] == "begin@5" && seen[1] == "start@10" && seen[2] == "end@20";
                check("driver.orderAndOnce", noneYet && twoDone && noRepeat && allDone,
                    string.Format("派发顺序 {0}；未到时间不派发；同一时刻重推不重复",
                        string.Join(" → ", seen)));

                // ② 状态机：begin → start → end
                var feverDriver = new LiveEventDriver();
                feverDriver.Load(new List<ChartEvent>
                {
                    Ev(3f, Fever.BeginEvent),
                    Ev(6f, Fever.StartEvent),
                    Ev(12f, Fever.EndEvent),
                });
                Fever.Register(feverDriver);

                feverDriver.Advance(2f);
                bool idle = Fever.Phase == FeverPhase.Idle && !Fever.IsActive;
                feverDriver.Advance(3f);
                bool begin = Fever.Phase == FeverPhase.Begin && !Fever.IsActive;
                feverDriver.Advance(6f);
                bool active = Fever.Phase == FeverPhase.Active && Fever.IsActive;
                check("state.beginStartEnd", idle && begin && active,
                    string.Format("0s=Idle / 3s=Begin / 6s=Active（当前 {0}）", Fever.Phase));

                // ③ 倍率走 modifier 口子：Active 期间挂上了 FEVER_SCORE，PlayRules 生效 1.2×
                bool applied = mgr.Has(Fever.ScoreModifierId);
                bool ruleOk = Mathf.Abs(PlayRules.ScoreMultiplier - Fever.ScoreMultiplier) < 1e-3f;
                check("modifier.appliedOnStart", applied && ruleOk,
                    string.Format("Active 时挂 {0}，PlayRules.ScoreMultiplier = {1:0.00}",
                        Fever.ScoreModifierId, PlayRules.ScoreMultiplier));

                // ④ 重复 start 幂等：不叠成 1.44×
                feverDriver.Advance(7f);
                Fever.EnterActive();
                feverDriver.Load(new List<ChartEvent> { Ev(7f, Fever.StartEvent) });
                feverDriver.Advance(7f);
                int feverMods = 0;
                var activeList = mgr.Active;
                for (int i = 0; i < activeList.Count; i++)
                    if (activeList[i].def != null && activeList[i].def.id == Fever.ScoreModifierId) feverMods++;
                check("modifier.idempotent",
                    feverMods == 1 && Mathf.Abs(PlayRules.ScoreMultiplier - Fever.ScoreMultiplier) < 1e-3f,
                    string.Format("重复 start 后仍只有 {0} 条、倍率 {1:0.00}", feverMods, PlayRules.ScoreMultiplier));

                // ⑤ End：撤掉倍率、回到 Idle
                Fever.Exit();
                bool removed = !mgr.Has(Fever.ScoreModifierId);
                bool restored = Mathf.Abs(PlayRules.ScoreMultiplier - 1f) < 1e-3f;
                check("modifier.removedOnEnd",
                    removed && restored && Fever.Phase == FeverPhase.Idle,
                    string.Format("End 后倍率回到 {0:0.00}、阶段 {1}", PlayRules.ScoreMultiplier, Fever.Phase));

                // ⑥ 与局内道具叠加：道具再挂一个 0.5× → 1.2 × 0.5 = 0.6（乘法，符合 ModifierManager 规则）
                Fever.EnterActive();
                mgr.Apply(new ModifierDef("DBG_ITEM", ModifierKind.ScoreMultiplier, 0.5f, 0f));
                check("modifier.stacksWithItems",
                    Mathf.Abs(PlayRules.ScoreMultiplier - Fever.ScoreMultiplier * 0.5f) < 1e-3f,
                    "fever 1.2 × 道具 0.5 = " + PlayRules.ScoreMultiplier.ToString("0.00"));
                mgr.Remove("DBG_ITEM");

                // ⑦ 蓄力值（留给道具/技能）：夹在 0..1
                Fever.Reset();
                Fever.AddCharge(0.4f);
                Fever.AddCharge(0.9f);
                check("charge.clamped", Mathf.Abs(Fever.Charge - 1f) < 1e-3f,
                    "0.4 + 0.9 → " + Fever.Charge.ToString("0.00"));

                // ⑧ Reset 幂等：junk 状态清干净
                Fever.Reset();
                check("reset.clean",
                    Fever.Phase == FeverPhase.Idle && Fever.Charge == 0f && !mgr.Has(Fever.ScoreModifierId),
                    "Reset 后 Idle / charge 0 / 无倍率");
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-28} 自检异常：{1}", "执行", e));
            }
            finally
            {
                if (mgr != null) mgr.ClearAll();
                Fever.Reset();
            }

            string summary = string.Format(
                "Fever 自检：{0} 项通过 / {1} 项异常"
                + "（判定：谱面事件驱动 + begin/start/end 状态机 + 倍率走 modifier（幂等/可叠道具/End 撤销）+ 蓄力夹取）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Fever] " + line);
            Debug.Log("[Fever] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/fever_check.txt", lines.ToArray());
                File.AppendAllText("Logs/fever_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Fever] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
