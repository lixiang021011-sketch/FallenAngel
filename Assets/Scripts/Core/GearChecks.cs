#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 通用件第一批（CG01 折叠谱架 / CG06 热水袋 / CG08 耳塞 / CG12 喉糖 / CG13 创可贴）的自检：
    ///   · 表：16 件、稀有度 白 9 / 蓝 5 / 金 2、价格随稀有度、启用行必须有 Loc key + 三种来历 + 效果；
    ///   · 写错的表要点名（重复 id、缺 key、缺来历、没有效果、两型都填、关联指向不存在的东西、分布不对）；
    ///   · 运行：曲前把已持有件挂到 PlayRules（窗口 0.85×0.90、分数 1.10、受伤 0.85）；
    ///   · 作用域：Song 的随曲终清、Run 的留着；30 秒的秒表到点恢复；
    ///   · 新两类效果：判定线锁挡得住 JudgeLineHidden；生命加成进 PlayLife.Max 与开曲满血；
    ///   · 次数：本曲 3 次用满即拒，曲前重置，局终清空。
    ///
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.GearChecks.Run）两用；报告写 Logs/gear_check.txt。
    /// </summary>
    public static class GearChecks
    {
        private const float Tol = 1e-3f;
        private const string Tag = "[GearCatalog] ";

        [MenuItem("Tools/FallenAngel/Validation/Content/Validate Common Gear", false, 705)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            ModifierManager mgr = null;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-30} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                PlayRules.SetBaseWindows(new JudgeWindows());   // 0.08 / 0.16 / 0.18 / 0.20
                mgr = ModifierManager.EnsureInstance();
                mgr.ClearAll();
                CommonGearService.EndRun();

                // ---------- ① 表 ----------
                var gearErrors = GearCatalog.Validate();
                check("通用件表校验通过", gearErrors.Count == 0,
                    gearErrors.Count == 0
                        ? string.Format("{0} 件（启用 {1}）", GearCatalog.Items.Count, GearCatalog.EnabledCount)
                        : string.Join("；", gearErrors.ToArray()));

                var contentErrors = ModifierCatalog.Validate(ModifierCatalog.Rows);
                check("内容表校验通过", contentErrors.Count == 0,
                    contentErrors.Count == 0
                        ? string.Format("{0} 行", ModifierCatalog.Rows.Count)
                        : string.Join("；", contentErrors.ToArray()));

                int white = GearCatalog.Items.Count(i => i.Rarity == GearRarity.White);
                int blue = GearCatalog.Items.Count(i => i.Rarity == GearRarity.Blue);
                int gold = GearCatalog.Items.Count(i => i.Rarity == GearRarity.Gold);
                check("16 件、稀有度 白 9 / 蓝 5 / 金 2",
                    GearCatalog.Items.Count == 16 && white == 9 && blue == 5 && gold == 2,
                    string.Format("{0} 件；白 {1} / 蓝 {2} / 金 {3}", GearCatalog.Items.Count, white, blue, gold));

                check("价格随稀有度",
                    GearCatalog.Find("CG01").Price == 40 &&
                    GearCatalog.Find("CG08").Price == 80 &&
                    GearCatalog.Find("CG14").Price == 160 &&
                    GearCatalog.Find("CG14").Price == ChapterCatalog.PriceOf(GearRarity.Gold),
                    string.Format("CG01 {0} / CG08 {1} / CG14 {2}",
                        GearCatalog.Find("CG01").Price, GearCatalog.Find("CG08").Price,
                        GearCatalog.Find("CG14").Price));

                check("第一批启用 5 件",
                    GearCatalog.EnabledCount == 5 &&
                    new[] { "CG01", "CG06", "CG08", "CG12", "CG13" }.All(id => GearCatalog.Find(id).Enabled) &&
                    !GearCatalog.Find("CG02").Enabled,
                    "CG01 / CG06 / CG08 / CG12 / CG13");

                var cg08Links = GearCatalog.ModifierIdsOf("CG08");
                check("一件挂多条（CG08 窗口 + 分数）",
                    cg08Links.Count == 2 && cg08Links.Contains("MC_EARPLUG_TIGHT") && cg08Links.Contains("MC_EARPLUG_SCORE"),
                    string.Join(" + ", cg08Links.ToArray()));

                // ---------- ② 写错的表要被点名 ----------
                var badItems = GearCatalog.Items.ToList();
                badItems.Add(new GearItemRow("CG06", GearRarity.White, false));                     // id 重复
                badItems = badItems
                    .Select(i => i.GearId == "CG01" ? new GearItemRow("CG01", GearRarity.White, true) : i)   // 启用却什么都没有
                    .ToList();
                // 连 CG01 的效果行一起摘掉，才能验到"启用却既没效果也没 FeatureId"那一条
                var badLinks = GearCatalog.Effects.Where(l => l.GearId != "CG01").ToList();
                badLinks.Add(new GearEffectRow("CG99", "MC_HOTBAG"));                                // 关联不存在的件
                badLinks.Add(new GearEffectRow("CG06", "MC_NOT_THERE"));                             // 关联不存在的内容行
                var badErrors = GearCatalog.Validate(badItems, badLinks, ModifierCatalog.Rows);
                var missing = Missing(badErrors, "重复", "缺 nameKey", "缺来历", "既没有效果行", "稀有度分布",
                    "不存在的内容行");
                check("写错的表要被点名", missing.Count == 0,
                    missing.Count == 0 ? string.Format("抓到 {0} 条", badErrors.Count)
                        : "没点到：" + string.Join("、", missing.ToArray()));

                // 两型都填 / 关联指向未启用的件：各单列一例
                var mixed = GearCatalog.Items
                    .Select(i => i.GearId == "CG06"
                        ? new GearItemRow("CG06", GearRarity.White, true, "gear.cg06.name", "gear.cg06.desc",
                            "feature.something", "gear.cg06.origin.a", "gear.cg06.origin.b", "gear.cg06.origin.c")
                        : i)
                    .ToList();
                var mixedMissing = Missing(GearCatalog.Validate(mixed, GearCatalog.Effects, ModifierCatalog.Rows),
                    "两型二选一");
                check("两型都填要被点名", mixedMissing.Count == 0,
                    mixedMissing.Count == 0 ? "已点名" : "没点到：" + string.Join("、", mixedMissing.ToArray()));

                var disabledLink = new List<GearEffectRow> { new GearEffectRow("CG02", "MC_HOTBAG") };
                var disabledMissing = Missing(
                    GearCatalog.Validate(GearCatalog.Items, disabledLink, ModifierCatalog.Rows), "尚未启用");
                check("关联未启用的件要被点名", disabledMissing.Count == 0,
                    disabledMissing.Count == 0 ? "已点名" : "没点到：" + string.Join("、", disabledMissing.ToArray()));

                // ---------- ③ 持有与曲前挂效果 ----------
                CommonGearService.SetHeld(new[] { "CG06", "CG08", "CG13", "CG02", "E01" });
                check("持有只认已启用的通用件",
                    CommonGearService.Owns("CG06") && CommonGearService.Owns("CG08") &&
                    !CommonGearService.Owns("CG02") && !CommonGearService.Owns("E01"),
                    "持有 " + string.Join("/", CommonGearService.Held.ToArray()));

                mgr.ClearAll();
                CommonGearService.BeginSong(new[] { "CG06", "CG08", "CG13" });
                bool applied = Mathf.Abs(PlayRules.JudgeWindowScale - 0.85f * 0.90f) <= Tol &&
                               Mathf.Abs(PlayRules.ScoreMultiplier - 1.10f) <= Tol &&
                               Mathf.Abs(PlayRules.LifeDamageScale - 0.85f) <= Tol;
                check("曲前挂上效果：窗口 0.85×0.90、分数 1.10、受伤 0.85",
                    applied && CommonGearService.AppliedCount == 4,
                    string.Format("窗口 {0:0.###} / 分数 {1:0.##} / 受伤 {2:0.##} / 条数 {3}",
                        PlayRules.JudgeWindowScale, PlayRules.ScoreMultiplier,
                        PlayRules.LifeDamageScale, CommonGearService.AppliedCount));

                check("有效窗口真的缩了",
                    Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f * 0.85f * 0.90f) <= Tol,
                    string.Format("Perfect {0:0.####}（基础 0.08）", PlayRules.Windows.perfectWindow));

                // 热水袋 30 秒到期 → 只剩耳塞的 0.90；创可贴是 Run 作用域，不受曲终影响
                mgr.Tick(29f);
                bool stillWarm = Mathf.Abs(PlayRules.JudgeWindowScale - 0.85f * 0.90f) <= Tol;
                mgr.Tick(31f);
                bool cooled = Mathf.Abs(PlayRules.JudgeWindowScale - 0.90f) <= Tol;
                check("热水袋 30 秒到点恢复", stillWarm && cooled,
                    string.Format("29s {0:0.###} → 31s {1:0.###}", 0.85f * 0.90f, PlayRules.JudgeWindowScale));

                mgr.EndSong();
                check("Song 清、Run 留",
                    Mathf.Abs(PlayRules.JudgeWindowScale - 1f) <= Tol &&
                    Mathf.Abs(PlayRules.ScoreMultiplier - 1f) <= Tol &&
                    Mathf.Abs(PlayRules.LifeDamageScale - 0.85f) <= Tol,
                    string.Format("窗口 {0:0.##} / 分数 {1:0.##} / 受伤 {2:0.##}",
                        PlayRules.JudgeWindowScale, PlayRules.ScoreMultiplier, PlayRules.LifeDamageScale));

                // ---------- ④ CG01 判定线锁 ----------
                mgr.ClearAll();
                CommonGearService.BeginSong(new[] { "CG01" });
                mgr.Apply(new ModifierDef("CHK_NOLINE", ModifierKind.JudgeLineHidden, 0f, 3f,
                    scope: ModifierScope.Song, source: ModifierSource.Debug));
                bool locked = PlayRules.JudgeLineVisible;
                mgr.Tick(4f);
                check("折叠谱架：判定线不消失", locked && PlayRules.JudgeLineVisible,
                    "遮蔽 3 秒内/后都在：" + PlayRules.JudgeLineVisible);

                mgr.ClearAll();
                mgr.Apply(new ModifierDef("CHK_NOLINE2", ModifierKind.JudgeLineHidden, 0f, 3f,
                    scope: ModifierScope.Song, source: ModifierSource.Debug));
                check("不持有时照常消失（回归）", !PlayRules.JudgeLineVisible,
                    "判定线可见 = " + PlayRules.JudgeLineVisible);

                // ---------- ⑤ CG12 生命加成 ----------
                mgr.ClearAll();
                CommonGearService.BeginSong(new[] { "CG12" });
                bool bonus = Mathf.Abs(PlayRules.LifeBonus - 100f) <= Tol &&
                             Mathf.Abs(PlayLife.Max - 1100f) <= Tol &&
                             Mathf.Abs(PlayLife.Current - 1100f) <= Tol &&
                             Mathf.Abs(PlayLife.Rate - 1f) <= Tol;
                check("喉糖：开曲生命 1100 且满格", bonus,
                    string.Format("加成 {0:0.#} / 上限 {1:0} / 当前 {2:0} / rate {3:0.###}",
                        PlayRules.LifeBonus, PlayLife.Max, PlayLife.Current, PlayLife.Rate));

                mgr.EndSong();
                check("曲终回到 1000（回归）",
                    Mathf.Abs(PlayRules.LifeBonus) <= Tol && Mathf.Abs(PlayLife.Max - 1000f) <= Tol,
                    string.Format("上限 {0:0}", PlayLife.Max));

                // ---------- ⑥ 次数 ----------
                mgr.ClearAll();
                CommonGearService.BeginSong(new[] { "CG11" });   // CG11 未启用 → 不该持有
                bool disabledNoCharge = !CommonGearService.TryConsumeSongCharge("CG11", 3);
                CommonGearService.BeginSong(new[] { "CG06" });
                bool c1 = CommonGearService.TryConsumeSongCharge("CG06", 3);
                bool c2 = CommonGearService.TryConsumeSongCharge("CG06", 3);
                bool c3 = CommonGearService.TryConsumeSongCharge("CG06", 3);
                bool c4 = CommonGearService.TryConsumeSongCharge("CG06", 3);
                CommonGearService.BeginSong(new[] { "CG06" });   // 曲前重置
                bool afterReset = CommonGearService.TryConsumeSongCharge("CG06", 3);
                check("本曲 3 次用满即拒、曲前重置",
                    disabledNoCharge && c1 && c2 && c3 && !c4 && afterReset &&
                    CommonGearService.SongChargesUsed("CG06") == 1,
                    string.Format("已用 {0}", CommonGearService.SongChargesUsed("CG06")));

                bool r1 = CommonGearService.TryConsumeRunCharge("CG06", 1);
                CommonGearService.BeginSong(new[] { "CG06" });
                bool runKept = CommonGearService.RunChargesUsed("CG06") == 1 && !CommonGearService.TryConsumeRunCharge("CG06", 1);
                CommonGearService.EndRun();
                check("本局次数跨曲保留、局终清空",
                    r1 && runKept && !CommonGearService.Owns("CG06") && CommonGearService.AppliedCount == 0,
                    string.Format("跨曲已用 {0}；局终持有 {1}", CommonGearService.RunChargesUsed("CG06"),
                        CommonGearService.Held.Count));

                // ---------- 报告 ----------
                string summary = string.Format("{0}PASS {1} / FAIL {2}（通用件第一批；未接商店与掉落）", Tag, pass, fail);
                for (int i = 0; i < lines.Count; i++) Debug.Log(Tag + lines[i]);
                Debug.Log(summary);
                try
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllLines("Logs/gear_check.txt", lines.ToArray());
                    File.AppendAllText("Logs/gear_check.txt", summary + Environment.NewLine);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add("FAIL " + "自检异常".PadRight(30) + e.Message);
                Debug.LogError(Tag + "自检异常：" + e);
            }
            finally
            {
                if (mgr != null) mgr.ClearAll();
                CommonGearService.EndRun();
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
    }
}
#endif
