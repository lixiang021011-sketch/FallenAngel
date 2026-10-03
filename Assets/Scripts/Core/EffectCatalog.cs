using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>
    /// 效果类型。**这是一道闸门**：只有这里列出的词能写进表。加新词必须同时
    /// 在 <see cref="RoomEffectRunner"/> 里接上执行与预检，否则 <see cref="EffectCatalog.Validate"/> 会拦下。
    /// </summary>
    public enum EffectType
    {
        /// <summary>给局内现金（value = 金额）</summary>
        GiveCash = 0,
        /// <summary>花局内现金（value = 金额；不够则整组失败）</summary>
        TakeCash = 1,
        /// <summary>给一件器材（target_id = GearCatalog 的 id，value = 数量，当前只支持 1）</summary>
        GiveEquipment = 2,
        /// <summary>施加一条 modifier（target_id = ModifierCatalog 的 id；来源固定为 Node）</summary>
        AddModifier = 3,
        /// <summary>清掉身上全部负面状态（ModifierDef.debuff）</summary>
        ClearDebuff = 4,
    }

    /// <summary>效果组的一行：一次提交要一起发生的事情。</summary>
    public sealed class EffectGroupRow
    {
        public string GroupId { get; }
        public string Remark { get; }

        public EffectGroupRow(string groupId, string remark = null)
        {
            GroupId = groupId;
            Remark = remark;
        }
    }

    /// <summary>效果条目的一行：一行一个动作，组内按 sort 升序执行。</summary>
    public sealed class EffectEntryRow
    {
        public string EffectId { get; }
        public string GroupId { get; }
        public int Sort { get; }
        public EffectType Type { get; }
        public string TargetId { get; }
        public string Value { get; }
        /// <summary>附加条件：空 = 无条件执行；不成立时跳过这一条，不判整组失败。</summary>
        public string ConditionGroupId { get; }

        public EffectEntryRow(string effectId, string groupId, int sort, EffectType type,
            string value = null, string targetId = null, string conditionGroupId = null)
        {
            EffectId = effectId;
            GroupId = groupId;
            Sort = sort;
            Type = type;
            TargetId = targetId;
            Value = value;
            ConditionGroupId = conditionGroupId;
        }

        /// <summary>把 value 解析成整数（Validate 已保证代码持有的行是合法的）。</summary>
        public bool TryIntValue(out int value)
        {
            return ConditionCatalog.TryParseValue(Value, out value);
        }
    }

    /// <summary>
    /// 效果组表。结构参照《肉鸽配置表与写表思路_v2》的 EffectGroup / EffectEntry
    /// （见 docs/condition-effect-spec.md），本工程按自己的惯例由代码持有、自带 Validate。
    ///
    /// 目前只服务占位房的选项：看一眼给钱、三件器材各给一件。
    /// 加新内容 = 往这里加行 + 用 RoomContentCatalog 的选项引用它。
    /// </summary>
    public static class EffectCatalog
    {
        /// <summary>已经接线的效果词（内容表只能先用这些）。</summary>
        public static readonly IReadOnlyList<EffectType> WiredTypes = new[]
        {
            EffectType.GiveCash, EffectType.TakeCash, EffectType.GiveEquipment,
            EffectType.AddModifier, EffectType.ClearDebuff,
        };

        private static readonly EffectGroupRow[] groups = new EffectGroupRow[]
        {
            new EffectGroupRow("EG_LOOK", "看一眼：给 20 现金"),
            new EffectGroupRow("EG_TAKE_HOTBAG", "拿走热水袋 CG06"),
            new EffectGroupRow("EG_TAKE_BANDAGE", "拿走创可贴 CG13"),
            new EffectGroupRow("EG_TAKE_PLIERS", "拿走折叠谱架 CG01"),
        };

        private static readonly EffectEntryRow[] entries = new EffectEntryRow[]
        {
            new EffectEntryRow("FX_LOOK_CASH", "EG_LOOK", 1, EffectType.GiveCash, "20"),
            new EffectEntryRow("FX_TAKE_HOTBAG", "EG_TAKE_HOTBAG", 1, EffectType.GiveEquipment, "1", "CG06"),
            new EffectEntryRow("FX_TAKE_BANDAGE", "EG_TAKE_BANDAGE", 1, EffectType.GiveEquipment, "1", "CG13"),
            new EffectEntryRow("FX_TAKE_PLIERS", "EG_TAKE_PLIERS", 1, EffectType.GiveEquipment, "1", "CG01"),
        };

        public static IReadOnlyList<EffectGroupRow> Groups { get { return groups; } }
        public static IReadOnlyList<EffectEntryRow> Entries { get { return entries; } }

        public static bool IsWired(EffectType type)
        {
            for (int i = 0; i < WiredTypes.Count; i++)
                if (WiredTypes[i] == type) return true;
            return false;
        }

        /// <summary>目标键是否为必填（器材与 modifier 指向具体对象；钱与净化不指向对象）。</summary>
        public static bool NeedsTarget(EffectType type)
        {
            return type == EffectType.GiveEquipment || type == EffectType.AddModifier;
        }

        /// <summary>是否需要 value 参数。</summary>
        public static bool NeedsValue(EffectType type)
        {
            return type == EffectType.GiveCash || type == EffectType.TakeCash
                || type == EffectType.GiveEquipment;
        }

        public static EffectGroupRow FindGroup(string groupId, IReadOnlyList<EffectGroupRow> table = null)
        {
            if (string.IsNullOrEmpty(groupId)) return null;
            IReadOnlyList<EffectGroupRow> source = table ?? groups;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null && source[i].GroupId == groupId) return source[i];
            }
            return null;
        }

        /// <summary>组内条目，按 sort 升序（同 sort 保持写入顺序）。</summary>
        public static List<EffectEntryRow> EntriesOf(string groupId, IReadOnlyList<EffectEntryRow> table = null)
        {
            var result = new List<EffectEntryRow>();
            if (string.IsNullOrEmpty(groupId)) return result;
            IReadOnlyList<EffectEntryRow> source = table ?? entries;
            for (int i = 0; i < source.Count; i++)
            {
                EffectEntryRow row = source[i];
                if (row != null && row.GroupId == groupId) result.Add(row);
            }
            result.Sort((a, b) => a.Sort != b.Sort ? a.Sort.CompareTo(b.Sort) : 0);
            return result;
        }

        public static List<string> Validate()
        {
            return Validate(groups, entries, null);
        }

        /// <summary>
        /// 校验两张效果表，返回错误说明列表（空 = 通过）。空表必须通过。
        /// 抓的是"写错了但不会立刻报错"的几类：id 空/重复、引用了不存在的组、空组、顺序为负、
        /// 未接线的效果词、缺目标键或多写目标键、参数不是整数或非正、指向不存在的器材/modifier、
        /// 附加条件指向不存在的条件组。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<EffectGroupRow> groupTable,
            IReadOnlyList<EffectEntryRow> entryTable, IReadOnlyList<ConditionGroupRow> conditionGroupTable)
        {
            var errors = new List<string>();
            if (groupTable == null) groupTable = new List<EffectGroupRow>();
            if (entryTable == null) entryTable = new List<EffectEntryRow>();

            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < groupTable.Count; i++)
            {
                EffectGroupRow row = groupTable[i];
                if (row == null) { errors.Add("效果组表：第 " + i + " 行为空"); continue; }
                if (string.IsNullOrEmpty(row.GroupId)) errors.Add("效果组表：第 " + i + " 行的 effectGroupId 为空");
                else if (!seenGroups.Add(row.GroupId)) errors.Add("效果组表：" + row.GroupId + " 重复");
            }

            var entryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var seenEntries = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entryTable.Count; i++)
            {
                EffectEntryRow row = entryTable[i];
                if (row == null) { errors.Add("效果条目表：第 " + i + " 行为空"); continue; }
                string label = string.IsNullOrEmpty(row.EffectId) ? "第 " + i + " 行" : row.EffectId;
                if (string.IsNullOrEmpty(row.EffectId)) errors.Add("效果条目表：第 " + i + " 行的 effectId 为空");
                else if (!seenEntries.Add(row.EffectId)) errors.Add("效果条目表：" + row.EffectId + " 重复");

                if (string.IsNullOrEmpty(row.GroupId))
                    errors.Add("效果条目表：" + label + " 没有所属效果组");
                else if (FindGroup(row.GroupId, groupTable) == null)
                    errors.Add("效果条目表：" + label + " 引用了不存在的效果组 " + row.GroupId);
                else
                {
                    int n;
                    entryCounts.TryGetValue(row.GroupId, out n);
                    entryCounts[row.GroupId] = n + 1;
                }

                if (row.Sort < 0) errors.Add("效果条目表：" + label + " 的顺序不能为负");

                if (!IsWired(row.Type))
                {
                    errors.Add("效果条目表：" + label + " 想用 " + row.Type + "，但这个效果还没接线");
                    continue;
                }

                if (NeedsTarget(row.Type))
                {
                    if (string.IsNullOrEmpty(row.TargetId))
                        errors.Add("效果条目表：" + label + " 是 " + row.Type + "，必须写目标键");
                    else if (row.Type == EffectType.GiveEquipment && GearCatalog.Find(row.TargetId) == null)
                        errors.Add("效果条目表：" + label + " 指向不存在的器材 " + row.TargetId);
                    else if (row.Type == EffectType.AddModifier && ModifierCatalog.Find(row.TargetId) == null)
                        errors.Add("效果条目表：" + label + " 指向不存在的 modifier " + row.TargetId);
                }
                else if (!string.IsNullOrEmpty(row.TargetId))
                {
                    errors.Add("效果条目表：" + label + " 不是指向具体对象的效果，却写了目标键 " + row.TargetId);
                }

                if (NeedsValue(row.Type))
                {
                    int value;
                    if (!ConditionCatalog.TryParseValue(row.Value, out value))
                        errors.Add("效果条目表：" + label + " 的参数不是整数：" + row.Value);
                    else if (value <= 0)
                        errors.Add("效果条目表：" + label + " 的参数必须为正，当前 " + value);
                    else if (row.Type == EffectType.GiveEquipment && value != 1)
                        errors.Add("效果条目表：" + label + " 一次只支持给 1 件器材，当前 " + value);
                }
                else if (!string.IsNullOrEmpty(row.Value))
                {
                    errors.Add("效果条目表：" + label + " 不需要参数，却写了 " + row.Value);
                }

                if (!string.IsNullOrEmpty(row.ConditionGroupId)
                    && ConditionCatalog.FindGroup(row.ConditionGroupId, conditionGroupTable) == null)
                {
                    errors.Add("效果条目表：" + label + " 的附加条件指向不存在的条件组 " + row.ConditionGroupId);
                }
            }

            for (int i = 0; i < groupTable.Count; i++)
            {
                EffectGroupRow row = groupTable[i];
                if (row == null || string.IsNullOrEmpty(row.GroupId)) continue;
                int n;
                if (!entryCounts.TryGetValue(row.GroupId, out n) || n == 0)
                    errors.Add("效果组表：" + row.GroupId + " 是空组（没有任何效果条目）");
            }
            return errors;
        }
    }
}
