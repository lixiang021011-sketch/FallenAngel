using System;
using System.Collections.Generic;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>
    /// 条件的判断口径。**这是一道闸门**：只有这里列出的词能写进表，
    /// 表里出现别的词会被 <see cref="ConditionCatalog.Validate"/> 点名——
    /// 这样"表里写了但代码没实现"的状态不会混进来（与 RoomContentCatalog 的 IsWired 同一套思路）。
    /// </summary>
    public enum ConditionType
    {
        /// <summary>局内现金（compare_value = 金额）</summary>
        RunCash = 0,
        /// <summary>是否持有某件器材（target_id = 器材 id；1 = 有 / 0 = 没有）</summary>
        HeldEquipment = 1,
        /// <summary>持有器材件数</summary>
        EquipmentCount = 2,
        /// <summary>当前层（1–3）</summary>
        Layer = 3,
        /// <summary>已完成的演奏场数</summary>
        CompletedSongs = 4,
        /// <summary>是否带着某条 modifier（target_id = ModifierCatalog 的 id）</summary>
        HasModifier = 5,
    }

    /// <summary>一个条件组里多条条件怎么组合。</summary>
    public enum ConditionMatchMode
    {
        /// <summary>全部成立才算过</summary>
        All = 0,
        /// <summary>任一成立就算过</summary>
        Any = 1,
    }

    /// <summary>比较符。</summary>
    public enum ConditionCompareOp
    {
        Eq = 0,
        Ne = 1,
        Gt = 2,
        Ge = 3,
        Lt = 4,
        Le = 5,
    }

    /// <summary>
    /// 判断条件需要的运行时读口。抽成接口有两个用处：
    /// ① 自检可以不建存档、不碰 UnityEngine 直接验算；② 将来接外部配表时读口不用改。
    /// </summary>
    public interface IConditionContext
    {
        int RunCash { get; }
        int Layer { get; }
        int CompletedSongs { get; }
        int EquipmentCount { get; }
        bool HoldsEquipment(string equipmentId);
        bool HasModifier(string modifierId);
    }

    /// <summary>条件组的一行：这一组条件怎么组合。</summary>
    public sealed class ConditionGroupRow
    {
        public string GroupId { get; }
        public ConditionMatchMode MatchMode { get; }
        public string Remark { get; }

        public ConditionGroupRow(string groupId, ConditionMatchMode matchMode = ConditionMatchMode.All,
            string remark = null)
        {
            GroupId = groupId;
            MatchMode = matchMode;
            Remark = remark;
        }
    }

    /// <summary>条件条目的一行：一行只说一件可判断的事。</summary>
    public sealed class ConditionEntryRow
    {
        public string ConditionId { get; }
        public string GroupId { get; }
        public int Sort { get; }
        public ConditionType Type { get; }
        public string TargetId { get; }
        public ConditionCompareOp CompareOp { get; }
        public string CompareValue { get; }

        public ConditionEntryRow(string conditionId, string groupId, int sort, ConditionType type,
            ConditionCompareOp compareOp, string compareValue, string targetId = null)
        {
            ConditionId = conditionId;
            GroupId = groupId;
            Sort = sort;
            Type = type;
            TargetId = targetId;
            CompareOp = compareOp;
            CompareValue = compareValue;
        }
    }

    /// <summary>
    /// 条件组表。结构参照《肉鸽配置表与写表思路_v2》的 ConditionGroup / ConditionEntry
    /// （见 docs/condition-effect-spec.md），本工程按自己的惯例由代码持有、自带 Validate。
    ///
    /// 目前**没有内容用到条件**——占位房的选项都是无条件可选的。表先立着，
    /// 等 EV01 那种"有代价的选项"进内容时往里填行。
    /// </summary>
    public static class ConditionCatalog
    {
        private static readonly ConditionGroupRow[] groups = new ConditionGroupRow[0];
        private static readonly ConditionEntryRow[] entries = new ConditionEntryRow[0];

        public static IReadOnlyList<ConditionGroupRow> Groups { get { return groups; } }
        public static IReadOnlyList<ConditionEntryRow> Entries { get { return entries; } }

        /// <summary>状态类口径必须给目标键；其余口径必须留空。</summary>
        public static bool NeedsTarget(ConditionType type)
        {
            return type == ConditionType.HeldEquipment || type == ConditionType.HasModifier;
        }

        public static ConditionGroupRow FindGroup(string groupId, IReadOnlyList<ConditionGroupRow> table = null)
        {
            if (string.IsNullOrEmpty(groupId)) return null;
            IReadOnlyList<ConditionGroupRow> source = table ?? groups;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null && source[i].GroupId == groupId) return source[i];
            }
            return null;
        }

        /// <summary>组内条目，按 sort 升序（同 sort 保持写入顺序）。</summary>
        public static List<ConditionEntryRow> EntriesOf(string groupId, IReadOnlyList<ConditionEntryRow> table = null)
        {
            var result = new List<ConditionEntryRow>();
            if (string.IsNullOrEmpty(groupId)) return result;
            IReadOnlyList<ConditionEntryRow> source = table ?? entries;
            for (int i = 0; i < source.Count; i++)
            {
                ConditionEntryRow row = source[i];
                if (row != null && row.GroupId == groupId) result.Add(row);
            }
            result.Sort((a, b) => a.Sort != b.Sort ? a.Sort.CompareTo(b.Sort) : 0);
            return result;
        }

        /// <summary>整组判断；空 groupId = 无条件，直接成立。</summary>
        public static bool Evaluate(string groupId, IConditionContext context)
        {
            string reason;
            return Evaluate(groupId, context, out reason);
        }

        /// <summary>
        /// 整组判断；失败时给出第一处不成立的原因（日志与自检用）。
        /// 空 groupId = 无条件限制。引用了一个空组属于配置错误——这里按"不成立"处理，
        /// 表在 Validate 阶段会被点名（空组不允许存在）。
        /// </summary>
        public static bool Evaluate(string groupId, IConditionContext context, out string reason)
        {
            return Evaluate(groupId, context, null, null, out reason);
        }

        public static bool Evaluate(string groupId, IConditionContext context,
            IReadOnlyList<ConditionGroupRow> groupTable, IReadOnlyList<ConditionEntryRow> entryTable,
            out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(groupId)) return true;            // 空 = 不限制
            if (context == null) { reason = "没有可读取的行程状态"; return false; }

            ConditionGroupRow group = FindGroup(groupId, groupTable);
            if (group == null) { reason = "条件组 " + groupId + " 不存在"; return false; }

            List<ConditionEntryRow> rows = EntriesOf(groupId, entryTable);
            if (rows.Count == 0) { reason = "条件组 " + groupId + " 是空的（空组不允许存在）"; return false; }

            bool any = group.MatchMode == ConditionMatchMode.Any;
            bool passedAny = false;
            for (int i = 0; i < rows.Count; i++)
            {
                int left;
                if (!TryMeasure(rows[i], context, out left, out reason)) return false;

                int right;
                if (!TryParseValue(rows[i].CompareValue, out right))
                {
                    reason = rows[i].ConditionId + " 的比较值不是整数：" + rows[i].CompareValue;
                    return false;
                }

                bool ok = Compare(left, right, rows[i].CompareOp);
                if (any)
                {
                    if (ok) { passedAny = true; break; }
                }
                else if (!ok)
                {
                    reason = string.Format("{0}：{1}={2} 不满足 {3} {4}",
                        rows[i].ConditionId, Label(rows[i].Type), left, rows[i].CompareOp, right);
                    return false;
                }
            }

            if (any && !passedAny) { reason = "条件组 " + groupId + " 的 ANY 条目一条都不成立"; return false; }
            return true;
        }

        /// <summary>取这一条条件的左边值。</summary>
        private static bool TryMeasure(ConditionEntryRow row, IConditionContext context,
            out int value, out string reason)
        {
            reason = null;
            switch (row.Type)
            {
                case ConditionType.RunCash: value = context.RunCash; return true;
                case ConditionType.Layer: value = context.Layer; return true;
                case ConditionType.CompletedSongs: value = context.CompletedSongs; return true;
                case ConditionType.EquipmentCount: value = context.EquipmentCount; return true;
                case ConditionType.HeldEquipment:
                    value = context.HoldsEquipment(row.TargetId) ? 1 : 0; return true;
                case ConditionType.HasModifier:
                    value = context.HasModifier(row.TargetId) ? 1 : 0; return true;
                default:
                    value = 0;
                    reason = row.ConditionId + " 的条件类型没有实现：" + row.Type;
                    return false;
            }
        }

        private static bool Compare(int left, int right, ConditionCompareOp op)
        {
            switch (op)
            {
                case ConditionCompareOp.Eq: return left == right;
                case ConditionCompareOp.Ne: return left != right;
                case ConditionCompareOp.Gt: return left > right;
                case ConditionCompareOp.Ge: return left >= right;
                case ConditionCompareOp.Lt: return left < right;
                case ConditionCompareOp.Le: return left <= right;
                default: return false;
            }
        }

        private static string Label(ConditionType type)
        {
            switch (type)
            {
                case ConditionType.RunCash: return "现金";
                case ConditionType.HeldEquipment: return "是否持有器材";
                case ConditionType.EquipmentCount: return "器材件数";
                case ConditionType.Layer: return "层";
                case ConditionType.CompletedSongs: return "已完成演奏";
                case ConditionType.HasModifier: return "是否带着该效果";
                default: return type.ToString();
            }
        }

        public static bool TryParseValue(string raw, out int value)
        {
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public static List<string> Validate()
        {
            return Validate(groups, entries);
        }

        /// <summary>
        /// 校验两张条件表，返回错误说明列表（空 = 通过）。空表必须通过。
        /// 抓的是"写错了但不会立刻报错"的几类：id 空/重复、引用了不存在的组、空组、
        /// 状态类口径缺目标键、非状态类口径多写了目标键、比较值不是整数、比较值越界。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<ConditionGroupRow> groupTable,
            IReadOnlyList<ConditionEntryRow> entryTable)
        {
            var errors = new List<string>();
            if (groupTable == null) groupTable = new List<ConditionGroupRow>();
            if (entryTable == null) entryTable = new List<ConditionEntryRow>();

            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < groupTable.Count; i++)
            {
                ConditionGroupRow row = groupTable[i];
                if (row == null) { errors.Add("条件组表：第 " + i + " 行为空"); continue; }
                if (string.IsNullOrEmpty(row.GroupId)) errors.Add("条件组表：第 " + i + " 行的 conditionGroupId 为空");
                else if (!seenGroups.Add(row.GroupId)) errors.Add("条件组表：" + row.GroupId + " 重复");
            }

            var entryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var seenEntries = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entryTable.Count; i++)
            {
                ConditionEntryRow row = entryTable[i];
                if (row == null) { errors.Add("条件条目表：第 " + i + " 行为空"); continue; }
                string label = string.IsNullOrEmpty(row.ConditionId) ? "第 " + i + " 行" : row.ConditionId;
                if (string.IsNullOrEmpty(row.ConditionId)) errors.Add("条件条目表：第 " + i + " 行的 conditionId 为空");
                else if (!seenEntries.Add(row.ConditionId)) errors.Add("条件条目表：" + row.ConditionId + " 重复");

                if (string.IsNullOrEmpty(row.GroupId))
                    errors.Add("条件条目表：" + label + " 没有所属条件组");
                else if (FindGroup(row.GroupId, groupTable) == null)
                    errors.Add("条件条目表：" + label + " 引用了不存在的条件组 " + row.GroupId);
                else
                {
                    int n;
                    entryCounts.TryGetValue(row.GroupId, out n);
                    entryCounts[row.GroupId] = n + 1;
                }

                if (row.Sort < 0) errors.Add("条件条目表：" + label + " 的顺序不能为负");
                if (NeedsTarget(row.Type))
                {
                    if (string.IsNullOrEmpty(row.TargetId))
                        errors.Add("条件条目表：" + label + " 是 " + row.Type + "，必须写目标键");
                }
                else if (!string.IsNullOrEmpty(row.TargetId))
                {
                    errors.Add("条件条目表：" + label + " 不是状态类口径，却写了目标键 " + row.TargetId);
                }

                int value;
                if (!TryParseValue(row.CompareValue, out value))
                    errors.Add("条件条目表：" + label + " 的比较值不是整数：" + row.CompareValue);
                else if (NeedsTarget(row.Type) && value != 0 && value != 1)
                    errors.Add("条件条目表：" + label + " 是状态类口径，比较值只能是 0 或 1，当前 " + value);
            }

            for (int i = 0; i < groupTable.Count; i++)
            {
                ConditionGroupRow row = groupTable[i];
                if (row == null || string.IsNullOrEmpty(row.GroupId)) continue;
                int n;
                if (!entryCounts.TryGetValue(row.GroupId, out n) || n == 0)
                    errors.Add("条件组表：" + row.GroupId + " 是空组（没有任何条件条目）");
            }
            return errors;
        }
    }

    /// <summary>把本局快照接到条件判断上的默认读口（modifier 走 ModifierManager 单例）。</summary>
    public sealed class RunConditionContext : IConditionContext
    {
        private readonly PortfolioGrowthRunData run;

        public RunConditionContext(PortfolioGrowthRunData run)
        {
            this.run = run;
        }

        public int RunCash { get { return run != null ? run.runCash : 0; } }
        public int Layer { get { return run != null ? run.layer : 0; } }
        public int CompletedSongs { get { return run != null ? run.completedSongs : 0; } }

        public int EquipmentCount
        {
            get { return run != null && run.heldEquipmentIds != null ? run.heldEquipmentIds.Count : 0; }
        }

        public bool HoldsEquipment(string equipmentId)
        {
            return run != null && run.heldEquipmentIds != null && !string.IsNullOrEmpty(equipmentId)
                && run.heldEquipmentIds.Contains(equipmentId);
        }

        public bool HasModifier(string modifierId)
        {
            ModifierManager manager = ModifierManager.Instance;
            return manager != null && !string.IsNullOrEmpty(modifierId) && manager.Has(modifierId);
        }
    }
}
