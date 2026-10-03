#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FallenAngel.Data;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 房间内容框架的自检（内容未铺也要能验）：
    ///   · 三张空表必须通过校验（内容还没铺是正常状态）；
    ///   · 改路消耗的词表必须正好是 None / Gear / Money —— 防止有人把没实现的词标成已接线；
    ///   · 条件组 / 效果组：ALL 与 ANY、各判断口径、词表闸门、排序执行、附加条件跳过、
    ///     试算不落地、钱不够时整组放弃（原子性）；
    ///   · 写错的行要逐条点名：未接线的效果、缺文案、重复 id、指向不存在的器材/modifier/条件组、
    ///     内容行选项数不在 1–3、引用不存在的选项、把演奏房/商店塞进内容表、层号越界；
    ///   · 按层过滤（RowsFor）只挑当前层与"任意层"的行。
    ///
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.RoomContentChecks.Run）两用；报告写 Logs/room_content_check.txt。
    /// 房间选项的**结算链路**（进程内、临时存档）在 PortfolioChecks 里验，见 docs/condition-effect-spec.md。
    /// </summary>
    public static class RoomContentChecks
    {
        private const string Tag = "[RoomContent] ";

        [MenuItem("Tools/FallenAngel/Validation/Content/Validate Room Content Tables", false, 702)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-30} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                var shipped = RoomContentCatalog.Validate();
                check("占位房内容表通过校验", shipped.Count == 0,
                    shipped.Count == 0
                        ? string.Format("选项 {0} / 事件 {1} / 休息 {2} / 纪念品 {3}",
                            RoomContentCatalog.Options.Count, RoomContentCatalog.Events.Count,
                            RoomContentCatalog.Rests.Count, RoomContentCatalog.Keepsakes.Count)
                        : string.Join("；", shipped.ToArray()));

                check("占位房已铺：3 类事件占位 + 休息 + 落点",
                    RoomContentCatalog.Options.Count == 5 && RoomContentCatalog.Events.Count == 3
                    && RoomContentCatalog.Rests.Count == 1 && RoomContentCatalog.Keepsakes.Count == 1,
                    string.Format("选项 {0} / 事件 {1} / 休息 {2} / 落点 {3}",
                        RoomContentCatalog.Options.Count, RoomContentCatalog.Events.Count,
                        RoomContentCatalog.Rests.Count, RoomContentCatalog.Keepsakes.Count));

                var layerErrors = LayerCatalog.Validate();
                check("层过渡文案表通过（时刻 + 层名 + 引言 + 失败）",
                    layerErrors.Count == 0 && LayerCatalog.Rows.Count == ChapterCatalog.LayerCount + 1,
                    layerErrors.Count == 0
                        ? string.Join(" → ", LayerCatalog.Rows.Select(r => r.TimeLabel).ToArray())
                        : string.Join("；", layerErrors.ToArray()));

                var displayErrors = LayerDisplayCatalog.Validate();
                check("层显示档位表通过（越深信息越少）", displayErrors.Count == 0,
                    displayErrors.Count == 0
                        ? string.Join(" / ", LayerDisplayCatalog.Rows.Select(r =>
                            string.Format("L{0}:{1}", r.Layer, r.Gear)).ToArray())
                        : string.Join("；", displayErrors.ToArray()));

                var rerouteErrors = RerouteCatalog.Validate();
                check("改路消耗表通过（花什么走配表）", rerouteErrors.Count == 0,
                    rerouteErrors.Count == 0
                        ? string.Join(" / ", RerouteCatalog.Rows.Select(r =>
                            string.Format("L{0}:{1} {2}", r.LayerFilter, r.Kind, r.Amount)).ToArray())
                        : string.Join("；", rerouteErrors.ToArray()));

                var wired = RoomContentCatalog.WiredEffects.OrderBy(k => (int)k).ToArray();
                check("改路消耗词表仍是 None / Gear / Money",
                    wired.SequenceEqual(new[] { RoomEffectKind.None, RoomEffectKind.Gear, RoomEffectKind.Money }),
                    string.Join(" / ", wired.Select(k => k.ToString()).ToArray()));

                // ---------- 条件组 / 效果组：表结构与词表闸门 ----------
                var conditionErrors = ConditionCatalog.Validate();
                check("条件组表通过（还没有内容用到，空表也要过）", conditionErrors.Count == 0,
                    conditionErrors.Count == 0
                        ? string.Format("条件组 {0} / 条目 {1}",
                            ConditionCatalog.Groups.Count, ConditionCatalog.Entries.Count)
                        : string.Join("；", conditionErrors.ToArray()));

                var effectErrors = EffectCatalog.Validate();
                check("效果组表通过（引用与参数都合法）", effectErrors.Count == 0,
                    effectErrors.Count == 0
                        ? string.Format("效果组 {0} / 条目 {1}",
                            EffectCatalog.Groups.Count, EffectCatalog.Entries.Count)
                        : string.Join("；", effectErrors.ToArray()));

                var wiredEffects = EffectCatalog.WiredTypes.OrderBy(k => (int)k).ToArray();
                check("已接线效果 = 给钱 / 花钱 / 给器材 / 加效果 / 净化",
                    wiredEffects.SequenceEqual(new[] { EffectType.GiveCash, EffectType.TakeCash,
                        EffectType.GiveEquipment, EffectType.AddModifier, EffectType.ClearDebuff }),
                    string.Join(" / ", wiredEffects.Select(k => k.ToString()).ToArray()));

                // ---------- 条件组：ALL / ANY 与各口径的实际判断 ----------
                var condGroups = new List<ConditionGroupRow>
                {
                    new ConditionGroupRow("COND_BOTH", ConditionMatchMode.All),
                    new ConditionGroupRow("COND_EITHER", ConditionMatchMode.Any),
                    new ConditionGroupRow("COND_GEAR", ConditionMatchMode.All),
                };
                var condEntries = new List<ConditionEntryRow>
                {
                    new ConditionEntryRow("C_CASH", "COND_BOTH", 1, ConditionType.RunCash, ConditionCompareOp.Ge, "20"),
                    new ConditionEntryRow("C_LAYER", "COND_BOTH", 2, ConditionType.Layer, ConditionCompareOp.Eq, "1"),
                    new ConditionEntryRow("C_CASH_HI", "COND_EITHER", 1, ConditionType.RunCash, ConditionCompareOp.Ge, "99"),
                    new ConditionEntryRow("C_LAYER_ANY", "COND_EITHER", 2, ConditionType.Layer, ConditionCompareOp.Eq, "1"),
                    new ConditionEntryRow("C_GEAR", "COND_GEAR", 1, ConditionType.HeldEquipment,
                        ConditionCompareOp.Eq, "1", "CG06"),
                };
                var probe = new PortfolioGrowthRunData { runCash = 30, layer = 1 };
                var probeCtx = new FakeEffectContext(probe);
                string condWhy;
                bool bothHit = ConditionCatalog.Evaluate("COND_BOTH", probeCtx, condGroups, condEntries, out condWhy);
                bool eitherHit = ConditionCatalog.Evaluate("COND_EITHER", probeCtx, condGroups, condEntries, out condWhy);
                probe.layer = 2;
                bool bothMiss = ConditionCatalog.Evaluate("COND_BOTH", probeCtx, condGroups, condEntries, out condWhy);
                bool eitherMiss = ConditionCatalog.Evaluate("COND_EITHER", probeCtx, condGroups, condEntries, out condWhy);
                probe.layer = 1;
                probe.heldEquipmentIds.Add("CG06");
                bool gearHit = ConditionCatalog.Evaluate("COND_GEAR", probeCtx, condGroups, condEntries, out condWhy);
                probe.heldEquipmentIds.Clear();
                bool gearMiss = ConditionCatalog.Evaluate("COND_GEAR", probeCtx, condGroups, condEntries, out condWhy);
                check("条件组：ALL 全中才算过、ANY 一中就算过",
                    bothHit && eitherHit && !bothMiss && !eitherMiss,
                    string.Format("30 金第 1 层 ALL={0} ANY={1}；第 2 层 ALL={2} ANY={3}",
                        bothHit, eitherHit, bothMiss, eitherMiss));
                check("条件组：状态类口径（是否持有器材）",
                    gearHit && !gearMiss, string.Format("有 CG06={0} 没有={1}", gearHit, gearMiss));
                check("条件组：空 id = 不限制，缺组 = 不成立",
                    ConditionCatalog.Evaluate(null, probeCtx, out condWhy)
                    && !ConditionCatalog.Evaluate("COND_MISSING", probeCtx, condGroups, condEntries, out condWhy),
                    "空 = true / 不存在的组 = false");

                // ---------- 效果组：原子提交、附加条件、闸门 ----------
                var fxGroups = new List<EffectGroupRow>
                {
                    new EffectGroupRow("FX_BUY"),
                    new EffectGroupRow("FX_MIX"),
                    new EffectGroupRow("FX_MOD"),
                };
                var fxEntries = new List<EffectEntryRow>
                {
                    new EffectEntryRow("F_PAY", "FX_BUY", 1, EffectType.TakeCash, "50"),
                    new EffectEntryRow("F_GEAR", "FX_BUY", 2, EffectType.GiveEquipment, "1", "CG06"),
                    new EffectEntryRow("F_SKIP", "FX_MIX", 1, EffectType.GiveCash, "5", null, "COND_EITHER"),
                    new EffectEntryRow("F_KEEP", "FX_MIX", 2, EffectType.GiveCash, "7"),
                    new EffectEntryRow("F_MOD", "FX_MOD", 1, EffectType.AddModifier, null, "MC_BANDAGE"),
                };

                var poor = new PortfolioGrowthRunData { runCash = 10 };
                var poorOutcome = RoomEffectRunner.Run("FX_BUY", poor, new FakeEffectContext(poor),
                    true, fxEntries, condEntries);
                check("效果组：钱不够时整组放弃，状态一点不动",
                    !poorOutcome.Ok && poor.runCash == 10 && poor.heldEquipmentIds.Count == 0,
                    poorOutcome.Ok ? "竟然通过了" : poorOutcome.Reason);

                var rich = new PortfolioGrowthRunData { runCash = 100 };
                var richOutcome = RoomEffectRunner.Run("FX_BUY", rich, new FakeEffectContext(rich),
                    true, fxEntries, condEntries);
                check("效果组：按 sort 依次执行，花钱与给器材一起落地",
                    richOutcome.Ok && rich.runCash == 50 && rich.heldEquipmentIds.Contains("CG06")
                    && richOutcome.GrantedGearIds.Count == 1,
                    richOutcome.Ok ? richOutcome.Summary : richOutcome.Reason);

                var dryRun = new PortfolioGrowthRunData { runCash = 100 };
                var dryOutcome = RoomEffectRunner.Run("FX_BUY", dryRun, new FakeEffectContext(dryRun),
                    false, fxEntries, condEntries);
                check("效果组：试算不落地（面板预检与提交走同一段代码）",
                    dryOutcome.Ok && dryRun.runCash == 100 && dryRun.heldEquipmentIds.Count == 0,
                    dryRun.runCash + " 金 / " + dryRun.heldEquipmentIds.Count + " 件器材");

                var mix = new PortfolioGrowthRunData { runCash = 0, layer = 2 };
                var mixOutcome = RoomEffectRunner.Run("FX_MIX", mix, new FakeEffectContext(mix),
                    true, fxEntries, condEntries);
                check("效果组：附加条件不成立只跳过那一条，其他照常",
                    mixOutcome.Ok && mix.runCash == 7 && mixOutcome.SkippedEffectIds.Count == 1
                    && mixOutcome.SkippedEffectIds[0] == "F_SKIP",
                    mixOutcome.Ok ? "跳过 " + string.Join("、", mixOutcome.SkippedEffectIds.ToArray())
                        : mixOutcome.Reason);

                var noModRun = new PortfolioGrowthRunData { runCash = 0 };
                var noModCtx = new FakeEffectContext(noModRun) { modifiersAvailable = false };
                var noModOutcome = RoomEffectRunner.Run("FX_MOD", noModRun, noModCtx, true, fxEntries, condEntries);
                check("效果组：modifier 系统不可用时整组预检失败",
                    !noModOutcome.Ok, noModOutcome.Ok ? "竟然通过了" : noModOutcome.Reason);

                // ---------- 写错了的条件 / 效果行要点名 ----------
                var badCondGroups = new List<ConditionGroupRow>
                {
                    new ConditionGroupRow("COND_EMPTY", ConditionMatchMode.All),
                    new ConditionGroupRow("COND_LONELY", ConditionMatchMode.All),
                };
                var badCondEntries = new List<ConditionEntryRow>
                {
                    new ConditionEntryRow("C_NOTARGET", "COND_MISSING", 1, ConditionType.HeldEquipment,
                        ConditionCompareOp.Eq, "1"),
                    new ConditionEntryRow("C_NOTINT", "COND_EMPTY", 1, ConditionType.RunCash,
                        ConditionCompareOp.Ge, "很多"),
                    new ConditionEntryRow("C_EXTRA", "COND_EMPTY", 2, ConditionType.Layer,
                        ConditionCompareOp.Eq, "1", "CG06"),
                };
                var badCondErrors = ConditionCatalog.Validate(badCondGroups, badCondEntries);
                var missingCond = Missing(badCondErrors, "不存在的条件组", "必须写目标键",
                    "比较值不是整数", "却写了目标键", "是空组");
                check("写错的条件行要被点名", missingCond.Count == 0,
                    missingCond.Count == 0 ? string.Format("抓到 {0} 条", badCondErrors.Count)
                        : "没点到：" + string.Join("、", missingCond.ToArray()));

                var badFxGroups = new List<EffectGroupRow>
                {
                    new EffectGroupRow("FX_EMPTY"),
                    new EffectGroupRow("FX_LONELY"),
                };
                var badFxEntries = new List<EffectEntryRow>
                {
                    new EffectEntryRow("F_UNWIRED", "FX_MISSING", 1, (EffectType)99, "1"),
                    new EffectEntryRow("F_NOGEAR", "FX_EMPTY", 1, EffectType.GiveEquipment, "1", "CG99"),
                    new EffectEntryRow("F_NOMOD", "FX_EMPTY", 2, EffectType.AddModifier, null, "MC_NOPE"),
                    new EffectEntryRow("F_NEG", "FX_EMPTY", 3, EffectType.GiveCash, "-5"),
                    new EffectEntryRow("F_EXTRATARGET", "FX_EMPTY", 4, EffectType.TakeCash, "5", "CG06"),
                    new EffectEntryRow("F_BADCOND", "FX_EMPTY", 5, EffectType.GiveCash, "5", null, "COND_MISSING"),
                };
                var badFxErrors = EffectCatalog.Validate(badFxGroups, badFxEntries, condGroups);
                var missingFx = Missing(badFxErrors, "还没接线", "不存在的器材", "不存在的 modifier",
                    "参数必须为正", "却写了目标键", "不存在的条件组", "是空组");
                check("写错的效果行要被点名", missingFx.Count == 0,
                    missingFx.Count == 0 ? string.Format("抓到 {0} 条", badFxErrors.Count)
                        : "没点到：" + string.Join("、", missingFx.ToArray()));

                // ---------- 写错的行要点名 ----------
                var badOptions = new List<RoomOptionRow>
                {
                    new RoomOptionRow("OP_NOFX", "room.op.nofx", "EG_MISSING"),          // 效果组不存在
                    new RoomOptionRow("OP_NOCOND", "room.op.nocond", null, "COND_MISSING"), // 条件组不存在
                    new RoomOptionRow("OP_NOKEY", null),                                 // 缺文案
                    new RoomOptionRow("OP_CG06", "room.op.gear2", "EG_TAKE_HOTBAG"),     // 合法行，供内容行引用
                };
                var badEvents = new List<RoomContentRow>
                {
                    new RoomContentRow("EV_DUP", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),
                    new RoomContentRow("EV_DUP", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 重复 id
                    new RoomContentRow("EV_WRONGKIND", ChapterRoomKind.Performance, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 演奏房不该有内容行
                    new RoomContentRow("EV_NOOPT", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new string[0]),                                                                       // 选项数 0
                    new RoomContentRow("EV_MANY", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06", "OP_CG06", "OP_CG06", "OP_CG06" }),                                // 选项数 4
                    new RoomContentRow("EV_MISSINGOPT", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_NOPE" }),                                                                 // 引用不存在的选项
                    new RoomContentRow("EV_NOKEY", ChapterRoomKind.Event, 0, null, null, new[] { "OP_CG06" }), // 缺文案
                    new RoomContentRow("EV_BADLAYER", ChapterRoomKind.Event, 9, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 层号越界
                };
                var badErrors = RoomContentCatalog.Validate(badOptions, badEvents, new List<RoomContentRow>(), new List<RoomContentRow>());
                var missing = Missing(badErrors, "不存在的效果组", "不存在的条件组", "缺 labelKey",
                    "重复", "房间类型应为", "选项数应在 1–3", "引用了不存在的选项", "缺 sceneKey", "层号越界");
                check("写错的行要被点名", missing.Count == 0,
                    missing.Count == 0 ? string.Format("抓到 {0} 条", badErrors.Count)
                        : "没点到：" + string.Join("、", missing.ToArray()));

                // 合法的合成内容：一条事件 + 一个选项，按层过滤要能挑出来
                var goodOptions = new List<RoomOptionRow>
                {
                    new RoomOptionRow("OP_OK", "room.op.ok", "EG_LOOK"),
                };
                var goodEvents = new List<RoomContentRow>
                {
                    new RoomContentRow("EV_L1", ChapterRoomKind.Event, 1, "scene.ok", "text.ok", new[] { "OP_OK" }),
                    new RoomContentRow("EV_ANY", ChapterRoomKind.Event, 0, "scene.ok", "text.ok", new[] { "OP_OK" }),
                };
                var goodErrors = RoomContentCatalog.Validate(goodOptions, goodEvents, new List<RoomContentRow>(), new List<RoomContentRow>());
                var layer1 = RoomContentCatalog.RowsFor(ChapterRoomKind.Event, 1, goodEvents);
                var layer2 = RoomContentCatalog.RowsFor(ChapterRoomKind.Event, 2, goodEvents);
                check("合法内容能过校验并按层过滤",
                    goodErrors.Count == 0 && layer1.Count == 2 && layer2.Count == 1
                    && RoomContentCatalog.RowsFor(ChapterRoomKind.Rest, 1, goodEvents).Count == 0,
                    goodErrors.Count == 0
                        ? string.Format("第 1 层 {0} 条 / 第 2 层 {1} 条", layer1.Count, layer2.Count)
                        : string.Join("；", goodErrors.ToArray()));

                string summary = string.Format("{0}PASS {1} / FAIL {2}（房间内容：占位房 + 层过渡文案 + 层显示档位 + 改路消耗 + 条件组/效果组）",
                    Tag, pass, fail);
                for (int i = 0; i < lines.Count; i++) Debug.Log(Tag + lines[i]);
                Debug.Log(summary);
                try
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllLines("Logs/room_content_check.txt", lines.ToArray());
                    File.AppendAllText("Logs/room_content_check.txt", summary + Environment.NewLine);
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

        /// <summary>
        /// 自检用的假行程状态：条件读的是真的本局快照，modifier 只记账、不碰 Unity。
        /// 这样"条件 → 效果 → 原子提交"整条链路可以不建存档、不进播放态就验完。
        /// </summary>
        private sealed class FakeEffectContext : IRoomEffectContext
        {
            private readonly RunConditionContext inner;
            private readonly List<string> modifiers = new List<string>();

            public bool modifiersAvailable = true;
            public int clearedCount;

            public FakeEffectContext(PortfolioGrowthRunData run)
            {
                inner = new RunConditionContext(run);
            }

            public bool ModifiersAvailable { get { return modifiersAvailable; } }

            public void AddModifier(ModifierDef def)
            {
                if (def == null || string.IsNullOrEmpty(def.id)) return;
                if (!modifiers.Contains(def.id)) modifiers.Add(def.id);
            }

            public int ClearDebuffs()
            {
                clearedCount++;
                int n = modifiers.Count;
                modifiers.Clear();
                return n;
            }

            public int RunCash { get { return inner.RunCash; } }
            public int Layer { get { return inner.Layer; } }
            public int CompletedSongs { get { return inner.CompletedSongs; } }
            public int EquipmentCount { get { return inner.EquipmentCount; } }
            public bool HoldsEquipment(string equipmentId) { return inner.HoldsEquipment(equipmentId); }
            public bool HasModifier(string modifierId) { return modifiers.Contains(modifierId); }
        }
    }
}
#endif
