#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// D1 构筑 + D3 modifier 内容层的**口子级**自检（内容未铺也要能验）：
    ///   · 出货表为空 = 内容未铺，但校验器必须通过（表结构自洽）；
    ///   · 校验器必须抓得到"写错的表"——id 空/重复、倍率非正、秒表口径错、缺 Loc key、
    ///     价格负数、指向不存在的构筑项/内容行、同项重复挂同一条效果；
    ///   · 合法表要真的把效果挂到 ModifierManager 上：来源 = 构筑、作用域 = 表里写的那个；
    ///   · 功能型构筑项（换谱卡/复活币，没有 modifier 效果）不该报错，只记购买；
    ///   · 净化器能回收"构筑来源的 debuff"。
    ///
    /// 合成表只活在本进程里，不入库、不进玩法。菜单与 batchmode
    /// （-executeMethod FallenAngel.Core.BuildChecks.Run）两用。
    /// </summary>
    public static class BuildChecks
    {
        private const float Tol = 1e-4f;
        private const string Tag = "[BuildCatalog] ";
        // 批处理（非播放模式）不跑 Awake：单例要显式补上（BuildCatalog.Apply 走 ModifierManager.Instance）
        private static readonly PropertyInfo PModifierInstance = typeof(ModifierManager).GetProperty("Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        [MenuItem("Tools/FallenAngel/Validate Build Catalog")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            ModifierManager mgr = null;
            GameObject createdHost = null;
            bool injectedInstance = false;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                PlayRules.SetBaseWindows(new JudgeWindows());   // 0.08 / 0.16 / 0.18 / 0.20
                mgr = ModifierManager.Instance;
                if (mgr == null)
                {
                    createdHost = new GameObject("__BuildCheckHost");
                    mgr = createdHost.AddComponent<ModifierManager>();
                    PModifierInstance.SetValue(null, mgr);
                    injectedInstance = true;
                }
                mgr.ClearAll();

                // ---------- ① 出货表：口子就位、内容未铺 ----------
                // 内容表里已经有通用件第一批的行（CG01/CG06/CG08/CG12/CG13），构筑表仍为空。
                check("构筑表未铺内容（内容表已含通用件行）",
                    BuildCatalog.Entries.Count == 0 && BuildCatalog.Effects.Count == 0,
                    string.Format("内容 {0} 行（通用件）/ 构筑 {1} 项 / 关联 {2} 行",
                        ModifierCatalog.Rows.Count, BuildCatalog.Entries.Count, BuildCatalog.Effects.Count));

                // ---------- ② 出货表校验必须通过 ----------
                var shippedContent = ModifierCatalog.Validate(ModifierCatalog.Rows);
                var shippedBuild = BuildCatalog.Validate(BuildCatalog.Entries, BuildCatalog.Effects, ModifierCatalog.Rows);
                check("出货表校验通过", shippedContent.Count == 0 && shippedBuild.Count == 0,
                    string.Format("内容表错误 {0} 条 / 构筑表错误 {1} 条", shippedContent.Count, shippedBuild.Count));

                // ---------- 合成表（只在本进程内） ----------
                var content = new List<ModifierContentRow>
                {
                    new ModifierContentRow("MC_WIDE", ModifierKind.JudgeWindowScale, 1.3f, 20f,
                        ModifierScope.Song, false, "build.wide.name", "build.wide.desc"),
                    new ModifierContentRow("MC_GUARD", ModifierKind.LifeDamageScale, 0.5f, 0f,
                        ModifierScope.Run, false, "build.guard.name", "build.guard.desc"),
                    new ModifierContentRow("MC_FRAIL", ModifierKind.LifeDamageScale, 1.5f, 0f,
                        ModifierScope.Song, true, "build.frail.name", "build.frail.desc"),
                    new ModifierContentRow("MC_OFF", ModifierKind.ScoreMultiplier, 1.2f, 0f,
                        ModifierScope.Song, false, "build.off.name", "build.off.desc", false),
                };
                var entries = new List<BuildEntryRow>
                {
                    new BuildEntryRow("BU_WIDE", BuildLine.RiskReduction, 60,
                        BuildLimitScope.Song, 1, "build.wide.name", "build.wide.desc"),
                    new BuildEntryRow("BU_GUARD", BuildLine.RiskReduction, 80,
                        BuildLimitScope.Run, 1, "build.guard.name", "build.guard.desc"),
                    new BuildEntryRow("BU_FRAIL", BuildLine.Leverage, 40,
                        BuildLimitScope.Run, 1, "build.frail.name", "build.frail.desc"),
                    // 功能型：换谱卡——没有 modifier 效果，效果由专门功能接管
                    new BuildEntryRow("BU_SWAP", BuildLine.Leverage, 50,
                        BuildLimitScope.Run, 1, "build.swap.name", "build.swap.desc"),
                    new BuildEntryRow("BU_OFF", BuildLine.RiskReduction, 30,
                        BuildLimitScope.None, 0, "build.off.name", "build.off.desc"),
                };
                var effectRows = new List<BuildEffectRow>
                {
                    new BuildEffectRow("BU_WIDE", "MC_WIDE"),
                    new BuildEffectRow("BU_GUARD", "MC_GUARD"),
                    new BuildEffectRow("BU_FRAIL", "MC_FRAIL"),
                    new BuildEffectRow("BU_OFF", "MC_OFF"),
                };

                // ---------- ③ 内容表校验：每一类写错都要被点到名 ----------
                var badContent = new List<ModifierContentRow>
                {
                    new ModifierContentRow("", ModifierKind.JudgeWindowScale, 1.2f, 0f,
                        ModifierScope.Song, false, "k", "d"),
                    new ModifierContentRow("MC_DUP", ModifierKind.JudgeWindowScale, 1.2f, 0f,
                        ModifierScope.Song, false, "k", "d"),
                    new ModifierContentRow("MC_DUP", ModifierKind.JudgeWindowScale, 1.2f, 0f,
                        ModifierScope.Song, false, "k", "d"),
                    new ModifierContentRow("MC_ZERO", ModifierKind.ScoreMultiplier, 0f, 0f,
                        ModifierScope.Song, false, "k", "d"),
                    new ModifierContentRow("MC_RUN_SEC", ModifierKind.LifeRegen, 5f, 8f,
                        ModifierScope.Run, false, "k", "d"),
                    new ModifierContentRow("MC_NOKEY", ModifierKind.LifeRegen, 5f, 0f,
                        ModifierScope.Song, false, null, null),
                    new ModifierContentRow("MC_NEG_SEC", ModifierKind.LifeRegen, 0f, -1f,
                        ModifierScope.Song, false, "k", "d"),
                };
                List<string> contentErrors = ModifierCatalog.Validate(badContent);
                check("内容表校验抓错", Missing(contentErrors, "modifierId 为空", "重复",
                        "magnitude 必须 > 0", "行程作用域", "缺 nameKey", "duration 不能为负").Count == 0,
                    string.Format("{0} 条错误；未覆盖的类别：{1}", contentErrors.Count,
                        DescribeMissing(contentErrors, "modifierId 为空", "重复", "magnitude 必须 > 0",
                            "行程作用域", "缺 nameKey", "duration 不能为负")));

                // ---------- ④ 构筑表校验：同上 ----------
                var badEntries = new List<BuildEntryRow>
                {
                    new BuildEntryRow("BU_NEGPRICE", BuildLine.RiskReduction, -10,
                        BuildLimitScope.None, 0, "k", "d"),
                    new BuildEntryRow("BU_NOKEY", BuildLine.Leverage, 10,
                        BuildLimitScope.None, 0, null, null),
                    new BuildEntryRow("BU_LIMIT0", BuildLine.RiskReduction, 10,
                        BuildLimitScope.Run, 0, "k", "d"),
                    new BuildEntryRow("BU_DUPLICATE", BuildLine.RiskReduction, 10,
                        BuildLimitScope.None, 0, "k", "d"),
                    new BuildEntryRow("BU_DUPLICATE", BuildLine.Leverage, 20,
                        BuildLimitScope.None, 0, "k", "d"),
                    entries[0],   // 合法项：给效果表当正确引用
                };
                var badEffects = new List<BuildEffectRow>
                {
                    new BuildEffectRow("BU_MISSING", "MC_WIDE"),      // 构筑项不存在
                    new BuildEffectRow("BU_WIDE", "MC_GHOST"),        // 内容行不存在
                    new BuildEffectRow("BU_WIDE", null),              // 缺 modifierId
                    new BuildEffectRow("BU_WIDE", "MC_GUARD"),        // 合法
                    new BuildEffectRow("BU_WIDE", "MC_GUARD"),        // 同项重复挂
                };
                List<string> buildErrors = BuildCatalog.Validate(badEntries, badEffects, content);
                check("构筑表校验抓错", Missing(buildErrors, "重复", "价格不能为负",
                        "LimitCount 至少为 1", "不存在的构筑项", "不存在的内容行", "缺 modifierId",
                        "重复挂同一条内容").Count == 0,
                    string.Format("{0} 条错误；未覆盖的类别：{1}", buildErrors.Count,
                        DescribeMissing(buildErrors, "重复", "价格不能为负", "LimitCount 至少为 1",
                            "不存在的构筑项", "不存在的内容行", "缺 modifierId", "重复挂同一条内容")));

                // ---------- ⑤ 合法合成表必须通过 ----------
                var okContent = ModifierCatalog.Validate(content);
                var okBuild = BuildCatalog.Validate(entries, effectRows, content);
                check("合法合成表校验通过", okContent.Count == 0 && okBuild.Count == 0,
                    string.Format("内容表 {0} 条 / 构筑表 {1} 条错误（{2} 项构筑、{3} 条关联）",
                        okContent.Count, okBuild.Count, entries.Count, effectRows.Count));

                // ---------- ⑥ 构筑生效：来源 = 构筑、作用域照表 ----------
                mgr.ClearAll();
                int wideApplied = BuildCatalog.Apply(entries[0], effectRows, content, ModifierSource.Build, 100f);
                int guardApplied = BuildCatalog.Apply(entries[1], effectRows, content, ModifierSource.Build, 100f);
                ModifierRuntime wideRow = FindRuntime(mgr, "MC_WIDE");
                ModifierRuntime guardRow = FindRuntime(mgr, "MC_GUARD");
                check("构筑生效（来源/作用域）",
                    wideApplied == 1 && guardApplied == 1
                    && wideRow != null && guardRow != null
                    && wideRow.def.source == ModifierSource.Build && wideRow.def.scope == ModifierScope.Song
                    && guardRow.def.source == ModifierSource.Build && guardRow.def.scope == ModifierScope.Run
                    && Mathf.Abs(PlayRules.Windows.perfectWindow - 0.104f) <= Tol
                    && Mathf.Abs(PlayRules.LifeDamageScale - 0.5f) <= Tol,
                    string.Format("判定窗口 0.08×1.3={0:0.###}（本曲）、受伤 ×{1:0.00}（行程）",
                        PlayRules.Windows.perfectWindow, PlayRules.LifeDamageScale));

                // ---------- ⑦ 净化器回收"构筑来源的 debuff" ----------
                int frailApplied = BuildCatalog.Apply(entries[2], effectRows, content, ModifierSource.Build, 100f);
                bool frailOn = frailApplied == 1 && Mathf.Abs(PlayRules.LifeDamageScale - 0.75f) <= Tol;
                int cleansed = mgr.RemoveDebuffs();
                check("净化回收构筑 debuff",
                    frailOn && cleansed == 1 && !mgr.Has("MC_FRAIL")
                    && Mathf.Abs(PlayRules.LifeDamageScale - 0.5f) <= Tol,
                    string.Format("杠杆项挂上后受伤 ×0.75；净化移除 {0} 条 → 回到 ×{1:0.00}（抗揍项保留）",
                        cleansed, PlayRules.LifeDamageScale));

                // ---------- ⑧ 功能型构筑项（无 modifier 效果）不报错 ----------
                int before = mgr.Active.Count;
                int swapApplied = BuildCatalog.Apply(entries[3], effectRows, content, ModifierSource.Build, 100f);
                check("功能型构筑项不算错",
                    swapApplied == 0 && mgr.Active.Count == before,
                    string.Format("换谱卡（无 modifier 效果）施加 {0} 条、生效条数不变（{1}）",
                        swapApplied, mgr.Active.Count));

                // ---------- ⑨ 内容未启用 / 引用不到时跳过而不是崩 ----------
                int offApplied = BuildCatalog.Apply(entries[4], effectRows, content, ModifierSource.Build, 100f);
                int ghostApplied = BuildCatalog.Apply(new BuildEntryRow("BU_GHOST", BuildLine.Leverage, 10,
                    BuildLimitScope.None, 0, "k", "d"),
                    new List<BuildEffectRow> { new BuildEffectRow("BU_GHOST", "MC_NOWHERE") },
                    content, ModifierSource.Build, 100f);
                check("未启用/缺失内容安全跳过",
                    offApplied == 0 && ghostApplied == 0 && mgr.Active.Count == before
                    && Mathf.Abs(PlayRules.ScoreMultiplier - 1f) <= Tol,
                    string.Format("未启用内容 {0} 条、缺失内容 {1} 条；分数倍率仍为 {2:0.00}（只打警告不崩）",
                        offApplied, ghostApplied, PlayRules.ScoreMultiplier));
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }
            finally
            {
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                if (mgr != null) mgr.ClearAll();
                if (createdHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdHost);
                    if (injectedInstance) PModifierInstance.SetValue(null, null);
                }
            }

            string summary = string.Format(
                "D1 构筑与 modifier 内容层自检：{0} 项通过 / {1} 项异常"
                + "（判定：出货表未铺内容但校验通过、写错的表被抓到、合法表能挂上效果（来源=构筑、作用域照表）、"
                + "净化回收构筑 debuff、功能型构筑项不算错、内容缺失只警告不崩）",
                pass, fail);
            foreach (string line in lines) Debug.Log(Tag + line);
            Debug.Log(Tag + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/build_catalog_check.txt", lines.ToArray());
                File.AppendAllText("Logs/build_catalog_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>错误清单里没有点到名的关键词</summary>
        private static List<string> Missing(List<string> errors, params string[] keywords)
        {
            string all = errors == null ? string.Empty : string.Join("\n", errors.ToArray());
            var missing = new List<string>();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (all.IndexOf(keywords[i], StringComparison.Ordinal) < 0) missing.Add(keywords[i]);
            }
            return missing;
        }

        private static string DescribeMissing(List<string> errors, params string[] keywords)
        {
            List<string> missing = Missing(errors, keywords);
            return missing.Count == 0 ? "（无）" : string.Join("、", missing.ToArray());
        }

        /// <summary>按 id 取运行时条目（自检读字段用）</summary>
        private static ModifierRuntime FindRuntime(ModifierManager mgr, string id)
        {
            var list = mgr.Active;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].def != null && list[i].def.id == id) return list[i];
            }
            return null;
        }
    }
}
#endif
