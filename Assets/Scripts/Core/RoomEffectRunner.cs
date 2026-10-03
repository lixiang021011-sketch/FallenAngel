using System;
using System.Collections.Generic;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>
    /// 结算一个效果组需要的运行时接口：左边是条件读口（<see cref="IConditionContext"/>），
    /// 右边是效果的两个出口。抽成接口是为了让自检不建存档、不碰 UnityEngine 也能验算整条链路。
    /// </summary>
    public interface IRoomEffectContext : IConditionContext
    {
        /// <summary>局内 modifier 系统能不能用；不能用时，含 modifier 的效果组在预检阶段整组失败。</summary>
        bool ModifiersAvailable { get; }

        /// <summary>施加一条 modifier（来源由调用方决定，房间走 <see cref="ModifierSource.Node"/>）。</summary>
        void AddModifier(ModifierDef def);

        /// <summary>清掉全部负面状态，返回清掉的条数。</summary>
        int ClearDebuffs();
    }

    /// <summary>把本局快照接到结算器上的默认上下文（modifier 走 ModifierManager 单例）。</summary>
    public sealed class RunEffectContext : IRoomEffectContext
    {
        private readonly RunConditionContext conditions;

        public RunEffectContext(PortfolioGrowthRunData run)
        {
            conditions = new RunConditionContext(run);
        }

        public bool ModifiersAvailable { get { return ModifierManager.Instance != null; } }

        public void AddModifier(ModifierDef def)
        {
            ModifierManager manager = ModifierManager.Instance;
            if (manager != null) manager.Apply(def, 0f);
        }

        public int ClearDebuffs()
        {
            ModifierManager manager = ModifierManager.Instance;
            return manager != null ? manager.RemoveDebuffs() : 0;
        }

        public int RunCash { get { return conditions.RunCash; } }
        public int Layer { get { return conditions.Layer; } }
        public int CompletedSongs { get { return conditions.CompletedSongs; } }
        public int EquipmentCount { get { return conditions.EquipmentCount; } }
        public bool HoldsEquipment(string equipmentId) { return conditions.HoldsEquipment(equipmentId); }
        public bool HasModifier(string modifierId) { return conditions.HasModifier(modifierId); }
    }

    /// <summary>
    /// 一次效果组结算的结果。预检失败时 <see cref="Ok"/> 为 false、<see cref="Reason"/> 给人看，
    /// 并且**运行态一个字段都没有变**。
    /// </summary>
    public sealed class RoomEffectOutcome
    {
        public bool Ok;
        public string Reason;
        public int CashDelta;
        public int DebuffsCleared;
        public readonly List<string> GrantedGearIds = new List<string>();
        public readonly List<string> AddedModifierIds = new List<string>();
        public readonly List<string> SkippedEffectIds = new List<string>();

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (CashDelta != 0) parts.Add("现金 " + (CashDelta > 0 ? "+" : "") + CashDelta);
                if (GrantedGearIds.Count > 0) parts.Add("器材 " + string.Join("、", GrantedGearIds.ToArray()));
                if (AddedModifierIds.Count > 0) parts.Add("效果 " + string.Join("、", AddedModifierIds.ToArray()));
                if (DebuffsCleared > 0) parts.Add("净化 " + DebuffsCleared + " 条");
                if (SkippedEffectIds.Count > 0) parts.Add("跳过 " + SkippedEffectIds.Count + " 条");
                return parts.Count == 0 ? "无事发生" : string.Join("；", parts.ToArray());
            }
        }
    }

    /// <summary>
    /// 效果组的结算器：把"条件组 + 效果组"落到本局快照上。
    ///
    /// **两段式，保证原子**：先按表把全部增量算出来并逐条校验（钱够不够、器材在不在、
    /// 是不是已经有一件、容量到没到、modifier 系统在不在），任一条不过就整组放弃、不动任何状态；
    /// 全过之后才一次性写入。代价与收益放在同一个效果组里时，两边用的是同一条影子状态，
    /// 所以"扣了钱没拿到东西"这种半成品状态不可能出现。
    ///
    /// 效果级附加条件按**提交前**的状态判断：同一组里前面的效果不影响后面的条件。
    /// </summary>
    public static class RoomEffectRunner
    {
        /// <summary>空的效果组 id = 什么都不做（"离开"这类选项就是这样）。</summary>
        public static RoomEffectOutcome Run(string effectGroupId, PortfolioGrowthRunData run,
            IRoomEffectContext context, bool commit,
            IReadOnlyList<EffectEntryRow> effectTable = null,
            IReadOnlyList<ConditionEntryRow> conditionTable = null)
        {
            var outcome = new RoomEffectOutcome { Ok = true };
            if (string.IsNullOrEmpty(effectGroupId)) return outcome;
            if (run == null)
            {
                outcome.Ok = false;
                outcome.Reason = "没有进行中的行程";
                return outcome;
            }

            List<EffectEntryRow> entries = EffectCatalog.EntriesOf(effectGroupId, effectTable);
            if (entries.Count == 0)
            {
                outcome.Ok = false;
                outcome.Reason = "效果组 " + effectGroupId + " 不存在或没有条目";
                return outcome;
            }

            // ── 影子状态：先全部算出来，确认能落地，再决定要不要写 ──
            int cash = run.runCash;
            var gear = new List<string>(run.heldEquipmentIds ?? new List<string>());
            var modifiers = new List<ModifierDef>();
            bool clearsDebuffs = false;

            for (int i = 0; i < entries.Count; i++)
            {
                EffectEntryRow row = entries[i];

                // 附加条件不成立 = 跳过这一条（不判整组失败）
                if (!string.IsNullOrEmpty(row.ConditionGroupId))
                {
                    string reason;
                    if (!ConditionCatalog.Evaluate(row.ConditionGroupId, context, null, conditionTable, out reason))
                    {
                        outcome.SkippedEffectIds.Add(row.EffectId);
                        continue;
                    }
                }

                switch (row.Type)
                {
                    case EffectType.GiveCash:
                    {
                        int amount;
                        if (!row.TryIntValue(out amount) || amount <= 0)
                        {
                            outcome.Ok = false;
                            outcome.Reason = row.EffectId + " 的金额不合法：" + row.Value;
                            return outcome;
                        }
                        cash = checked(cash + amount);
                        break;
                    }

                    case EffectType.TakeCash:
                    {
                        int cost;
                        if (!row.TryIntValue(out cost) || cost <= 0)
                        {
                            outcome.Ok = false;
                            outcome.Reason = row.EffectId + " 的金额不合法：" + row.Value;
                            return outcome;
                        }
                        if (cash < cost)
                        {
                            outcome.Ok = false;
                            outcome.Reason = string.Format("现金不够：需要 {0}，当前 {1}", cost, cash);
                            return outcome;
                        }
                        cash -= cost;
                        break;
                    }

                    case EffectType.GiveEquipment:
                    {
                        if (string.IsNullOrEmpty(row.TargetId))
                        {
                            outcome.Ok = false;
                            outcome.Reason = row.EffectId + " 没写器材 id";
                            return outcome;
                        }
                        if (gear.Contains(row.TargetId))
                        {
                            outcome.Ok = false;
                            outcome.Reason = "已经有一件 " + row.TargetId + " 了";
                            return outcome;
                        }
                        GearItemRow item = GearCatalog.Find(row.TargetId);
                        if (item == null)
                        {
                            outcome.Ok = false;
                            outcome.Reason = "没有这件器材：" + row.TargetId;
                            return outcome;
                        }
                        if (!item.Enabled)
                        {
                            outcome.Ok = false;
                            outcome.Reason = "器材还没做好：" + row.TargetId;
                            return outcome;
                        }
                        if (gear.Count >= PortfolioDefaults.EquipmentCapacity)
                        {
                            outcome.Ok = false;
                            outcome.Reason = "器材满了（上限 " + PortfolioDefaults.EquipmentCapacity + "）";
                            return outcome;
                        }
                        gear.Add(row.TargetId);
                        break;
                    }

                    case EffectType.AddModifier:
                    {
                        if (!context.ModifiersAvailable)
                        {
                            outcome.Ok = false;
                            outcome.Reason = "这条效果要用局内 modifier 系统，但现在没有";
                            return outcome;
                        }
                        ModifierDef def;
                        if (!ModifierCatalog.TryCreate(row.TargetId, ModifierSource.Node, out def))
                        {
                            outcome.Ok = false;
                            outcome.Reason = "modifier 不存在或未启用：" + row.TargetId;
                            return outcome;
                        }
                        bool duplicate = false;
                        for (int k = 0; k < modifiers.Count; k++)
                            if (modifiers[k].id == def.id) duplicate = true;
                        if (!duplicate) modifiers.Add(def);
                        break;
                    }

                    case EffectType.ClearDebuff:
                        if (!context.ModifiersAvailable)
                        {
                            outcome.Ok = false;
                            outcome.Reason = "净化要用局内 modifier 系统，但现在没有";
                            return outcome;
                        }
                        clearsDebuffs = true;
                        break;

                    default:
                        outcome.Ok = false;
                        outcome.Reason = row.EffectId + " 的效果类型没有实现：" + row.Type;
                        return outcome;
                }
            }

            outcome.CashDelta = cash - run.runCash;
            for (int i = 0; i < gear.Count; i++)
            {
                bool alreadyHeld = run.heldEquipmentIds != null && run.heldEquipmentIds.Contains(gear[i]);
                if (!alreadyHeld) outcome.GrantedGearIds.Add(gear[i]);
            }
            for (int i = 0; i < modifiers.Count; i++) outcome.AddedModifierIds.Add(modifiers[i].id);

            if (!commit) return outcome;

            // ── 落地：预检已经全过，这里不会失败 ──
            run.runCash = cash;
            if (outcome.GrantedGearIds.Count > 0)
            {
                if (run.heldEquipmentIds == null) run.heldEquipmentIds = new List<string>();
                run.heldEquipmentIds.AddRange(outcome.GrantedGearIds);
            }
            for (int i = 0; i < modifiers.Count; i++) context.AddModifier(modifiers[i]);
            if (clearsDebuffs) outcome.DebuffsCleared = context.ClearDebuffs();
            return outcome;
        }
    }
}
