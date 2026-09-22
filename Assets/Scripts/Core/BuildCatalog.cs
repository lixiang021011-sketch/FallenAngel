using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// D1 构筑的两条线（作者 2026-09-22 的设计分法）：**降低风险**与**增加杠杆**。
    /// 定位：局内现金的第二个去处（第一个是装备）。装备买"收益"，构筑买"这局能不能走完 / 敢不敢赌"。
    /// </summary>
    public enum BuildLine
    {
        /// <summary>降低风险：让这一局更容易走完（净化器、判定宽度、无敌时间、复活）</summary>
        RiskReduction = 0,
        /// <summary>增加杠杆：拿风险换收益（更脆 / 更严的判定 / 断连惩罚换更高结算）</summary>
        Leverage = 1,
    }

    /// <summary>
    /// 构筑项的**购买**限次口径。限次属于购买流程（扣现金、记账随局快照持久化），
    /// 本轮只落字段与校验——购买流程与 UI 未接（见 D1 设计：内容侧排期时一起做）。
    /// </summary>
    public enum BuildLimitScope
    {
        /// <summary>不限次</summary>
        None = 0,
        /// <summary>每曲限 n 次</summary>
        Song = 1,
        /// <summary>每局（本次行程）限 n 次</summary>
        Run = 2,
    }

    /// <summary>
    /// 一行构筑项：花局内现金买的"降风险 / 加杠杆"。价格与限次是**购买侧**的数据，
    /// 效果（modifier）在 <see cref="BuildEffectRow"/> 里挂，一条构筑项可以挂多条效果。
    ///
    /// 两类构筑项：
    /// · 效果型——挂 modifier（判定宽度、无敌时间、收益倍率…），走 <see cref="BuildCatalog.Apply"/>；
    /// · 功能型——没有 modifier 效果（换谱卡、换乐器卡、复活币），效果由专门功能接管，
    ///   本表只保留价格 / 限次 / 文案，免得把两类硬凑进同一个机制里。
    /// </summary>
    public sealed class BuildEntryRow
    {
        public string EntryId { get; }
        public BuildLine Line { get; }
        public int Price { get; }
        public BuildLimitScope LimitScope { get; }
        public int LimitCount { get; }
        public string NameKey { get; }
        public string DescKey { get; }
        public bool Enabled { get; }

        public BuildEntryRow(string entryId, BuildLine line, int price,
                             BuildLimitScope limitScope = BuildLimitScope.None, int limitCount = 0,
                             string nameKey = null, string descKey = null, bool enabled = true)
        {
            EntryId = entryId;
            Line = line;
            Price = price;
            LimitScope = limitScope;
            LimitCount = limitCount;
            NameKey = nameKey;
            DescKey = descKey;
            Enabled = enabled;
        }
    }

    /// <summary>构筑项 → modifier 内容 的关联行（同一构筑项可挂多条，Order 决定施加顺序）</summary>
    public sealed class BuildEffectRow
    {
        public string EntryId { get; }
        public string ModifierId { get; }
        public int Order { get; }

        public BuildEffectRow(string entryId, string modifierId, int order = 0)
        {
            EntryId = entryId;
            ModifierId = modifierId;
            Order = order;
        }
    }

    /// <summary>
    /// D1 构筑表（购买侧数据 + 效果挂接）。**当前内容为空**——本轮只落表结构与校验口子：
    /// 内容（净化器 / 判定宽度 / 复活币 / 无敌 / 换谱卡 / 换乐器卡的具体数值与价格）另排期。
    ///
    /// 为什么单开一张表而不是塞进 equipment_*：装备的效果是**结算时算钱**（收益引擎），
    /// 构筑的效果是**买入即生效改玩法**（modifier）。两条口径混在一张表里，
    /// 会让"这件东西什么时候起作用"变得说不清。共用的是**现金与限次**那一层。
    /// </summary>
    public static class BuildCatalog
    {
        private static readonly BuildEntryRow[] entries = new BuildEntryRow[0];
        private static readonly BuildEffectRow[] effects = new BuildEffectRow[0];

        /// <summary>全部构筑项（当前为空）</summary>
        public static IReadOnlyList<BuildEntryRow> Entries { get { return entries; } }

        /// <summary>全部"构筑项 → 效果"关联行（当前为空）</summary>
        public static IReadOnlyList<BuildEffectRow> Effects { get { return effects; } }

        /// <summary>按 id 找构筑项（出货表）</summary>
        public static BuildEntryRow FindEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].EntryId == entryId) return entries[i];
            }
            return null;
        }

        /// <summary>某构筑项挂的效果行（按 Order 升序）</summary>
        public static List<BuildEffectRow> EffectsOf(string entryId)
        {
            return EffectsOf(entryId, effects);
        }

        /// <summary>某构筑项挂的效果行（指定表；自检与外部表共用）</summary>
        public static List<BuildEffectRow> EffectsOf(string entryId, IReadOnlyList<BuildEffectRow> table)
        {
            var result = new List<BuildEffectRow>();
            if (string.IsNullOrEmpty(entryId) || table == null) return result;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].EntryId == entryId) result.Add(table[i]);
            }
            result.Sort((a, b) => a.Order.CompareTo(b.Order));
            return result;
        }

        /// <summary>
        /// 施加一条构筑项的全部效果（买入即生效），来源固定为 <see cref="ModifierSource.Build"/>。
        /// 返回实际挂上的效果条数（内容缺失 / 无效果 / 没有 ModifierManager 时为 0，并打日志）。
        /// </summary>
        public static int Apply(string entryId, float songTime)
        {
            BuildEntryRow entry = FindEntry(entryId);
            if (entry == null || !entry.Enabled)
            {
                Debug.LogWarning("[BuildCatalog] 构筑项不存在或未启用：" + entryId);
                return 0;
            }
            return Apply(entry, effects, ModifierCatalog.Rows, ModifierSource.Build, songTime);
        }

        /// <summary>按给定表施加（自检与将来"外部表导出"共用同一条实现）</summary>
        public static int Apply(BuildEntryRow entry, IReadOnlyList<BuildEffectRow> effectTable,
            IReadOnlyList<ModifierContentRow> contentTable, ModifierSource source, float songTime)
        {
            if (entry == null) return 0;

            ModifierManager mgr = ModifierManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[BuildCatalog] 场景里没有 ModifierManager，构筑效果无法生效");
                return 0;
            }

            List<BuildEffectRow> list = EffectsOf(entry.EntryId, effectTable);
            if (list.Count == 0)
            {
                // 功能型构筑项（换谱卡 / 复活币）本来就没有 modifier 效果，不是错误。
                Debug.Log(string.Format("[BuildCatalog] {0} 没有 modifier 效果（由专门功能接管），只记购买", entry.EntryId));
                return 0;
            }

            int applied = 0;
            for (int i = 0; i < list.Count; i++)
            {
                ModifierDef def;
                if (!ModifierCatalog.TryCreate(ModifierCatalog.Find(list[i].ModifierId, contentTable), source, out def))
                {
                    Debug.LogWarning(string.Format("[BuildCatalog] {0} 引用的 modifier 内容缺失或未启用：{1}",
                        entry.EntryId, list[i].ModifierId));
                    continue;
                }
                mgr.Apply(def, songTime);
                applied++;
            }
            Debug.Log(string.Format("[BuildCatalog] 构筑生效：{0}（{1}，{2} 条效果，来源={3}）",
                entry.EntryId, entry.Line, applied, source));
            return applied;
        }

        /// <summary>
        /// 校验构筑表（含对 modifier 内容表的引用），返回错误说明列表（空 = 通过）。
        /// 只读、不改表；允许"没有效果的构筑项"（功能型）。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<BuildEntryRow> entryTable,
            IReadOnlyList<BuildEffectRow> effectTable, IReadOnlyList<ModifierContentRow> contentTable)
        {
            var errors = new List<string>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);

            if (entryTable != null)
            {
                for (int i = 0; i < entryTable.Count; i++)
                {
                    BuildEntryRow entry = entryTable[i];
                    if (entry == null)
                    {
                        errors.Add(string.Format("构筑表：第 {0} 行为空", i));
                        continue;
                    }
                    string label = string.IsNullOrEmpty(entry.EntryId) ? string.Format("第 {0} 行", i) : entry.EntryId;
                    if (string.IsNullOrEmpty(entry.EntryId)) errors.Add(string.Format("构筑表：第 {0} 行的 entryId 为空", i));
                    else if (!entryIds.Add(entry.EntryId)) errors.Add("构筑表：" + entry.EntryId + " 重复");
                    if (entry.Price < 0) errors.Add(string.Format("构筑表：{0} 的价格不能为负（{1}）", label, entry.Price));
                    if (entry.LimitScope == BuildLimitScope.None && entry.LimitCount != 0)
                    {
                        errors.Add(string.Format("构筑表：{0} 不限次时 LimitCount 应为 0（当前 {1}）", label, entry.LimitCount));
                    }
                    if (entry.LimitScope != BuildLimitScope.None && entry.LimitCount < 1)
                    {
                        errors.Add(string.Format("构筑表：{0} 声明了限次口径，LimitCount 至少为 1（当前 {1}）",
                            label, entry.LimitCount));
                    }
                    if (entry.Enabled && (string.IsNullOrEmpty(entry.NameKey) || string.IsNullOrEmpty(entry.DescKey)))
                    {
                        errors.Add(string.Format("构筑表：{0} 缺 nameKey/descKey（玩家可见文本要走 Loc）", label));
                    }
                }
            }

            if (effectTable != null)
            {
                var pairs = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < effectTable.Count; i++)
                {
                    BuildEffectRow row = effectTable[i];
                    if (row == null)
                    {
                        errors.Add(string.Format("构筑效果表：第 {0} 行为空", i));
                        continue;
                    }
                    string label = string.Format("{0} → {1}",
                        string.IsNullOrEmpty(row.EntryId) ? string.Format("第 {0} 行", i) : row.EntryId,
                        string.IsNullOrEmpty(row.ModifierId) ? "（缺 modifierId）" : row.ModifierId);
                    if (string.IsNullOrEmpty(row.EntryId)) errors.Add(string.Format("构筑效果表：第 {0} 行缺 entryId", i));
                    else if (entryTable == null || !ContainsEntry(entryTable, row.EntryId))
                        errors.Add(string.Format("构筑效果表：{0} 指向了不存在的构筑项", label));
                    if (string.IsNullOrEmpty(row.ModifierId)) errors.Add(string.Format("构筑效果表：{0} 缺 modifierId", label));
                    else if (ModifierCatalog.Find(row.ModifierId, contentTable) == null)
                        errors.Add(string.Format("构筑效果表：{0} 指向了不存在的内容行", label));
                    if (!string.IsNullOrEmpty(row.EntryId) && !string.IsNullOrEmpty(row.ModifierId)
                        && !pairs.Add(row.EntryId + "|" + row.ModifierId))
                    {
                        errors.Add(string.Format("构筑效果表：{0} 重复挂同一条内容", label));
                    }
                }
            }

            return errors;
        }

        private static bool ContainsEntry(IReadOnlyList<BuildEntryRow> table, string entryId)
        {
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].EntryId == entryId) return true;
            }
            return false;
        }
    }
}
