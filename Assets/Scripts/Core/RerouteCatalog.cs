using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>改路要花的东西（花什么、花多少）。</summary>
    public sealed class RerouteCostRow
    {
        public string CostId { get; }
        public RoomEffectKind Kind { get; }   // 现在只支持已接线的词（Money / Gear）
        public string ItemId { get; }         // Kind = Gear 时指向 GearCatalog 的件
        public int Amount { get; }
        public int LayerFilter { get; }       // 0 = 任意层
        public bool Enabled { get; }

        public RerouteCostRow(string costId, RoomEffectKind kind, int amount,
            string itemId = null, int layerFilter = 0, bool enabled = true)
        {
            CostId = costId;
            Kind = kind;
            Amount = amount;
            ItemId = itemId;
            LayerFilter = layerFilter;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// 改路消耗表（作者口径 2026-09-27："改路要花东西，花的东西走配表"）。
    ///
    /// 参照：集成战略自阴云火花起可以花 1 把钥匙跳到当前分支里的另一个节点；
    /// 我们先用"钱"起步（钱已经接线），等"人情/器材"那两套接线后可以换成更贵的东西——
    /// 换的时候只改这张表。
    /// </summary>
    public static class RerouteCatalog
    {
        private static readonly RerouteCostRow[] rows = new RerouteCostRow[]
        {
            // 草案价（待拍板）：越深越贵
            new RerouteCostRow("RR_L1", RoomEffectKind.Money, 20, layerFilter: 1),
            new RerouteCostRow("RR_L2", RoomEffectKind.Money, 40, layerFilter: 2),
            new RerouteCostRow("RR_L3", RoomEffectKind.Money, 60, layerFilter: 3),
        };

        public static IReadOnlyList<RerouteCostRow> Rows { get { return rows; } }

        /// <summary>该层的改路代价（没有对应行返回 null = 这一层不能改路）。</summary>
        public static RerouteCostRow ForLayer(int layer, IReadOnlyList<RerouteCostRow> table = null)
        {
            IReadOnlyList<RerouteCostRow> source = table ?? rows;
            for (int i = 0; i < source.Count; i++)
            {
                RerouteCostRow row = source[i];
                if (row == null || !row.Enabled) continue;
                if (row.LayerFilter == 0 || row.LayerFilter == layer) return row;
            }
            return null;
        }

        public static List<string> Validate()
        {
            return Validate(rows);
        }

        /// <summary>校验：id 空/重复、数量非正、**用了还没接线的词**、Gear 没给件或件不存在、层号越界。</summary>
        public static List<string> Validate(IReadOnlyList<RerouteCostRow> table)
        {
            var errors = new List<string>();
            if (table == null) return errors;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < table.Count; i++)
            {
                RerouteCostRow row = table[i];
                if (row == null) { errors.Add("改路消耗表：第 " + i + " 行为空"); continue; }
                string label = string.IsNullOrEmpty(row.CostId) ? "第 " + i + " 行" : row.CostId;
                if (string.IsNullOrEmpty(row.CostId)) errors.Add("改路消耗表：第 " + i + " 行的 costId 为空");
                else if (!seen.Add(row.CostId)) errors.Add("改路消耗表：" + row.CostId + " 重复");
                if (row.Amount <= 0) errors.Add("改路消耗表：" + label + " 的数量必须为正");
                if (!RoomContentCatalog.IsWired(row.Kind))
                    errors.Add("改路消耗表：" + label + " 想花 " + row.Kind + "，但这个效果还没接线");
                if (row.Kind == RoomEffectKind.Gear)
                {
                    if (string.IsNullOrEmpty(row.ItemId)) errors.Add("改路消耗表：" + label + " 花器材却没写 itemId");
                    else if (GearCatalog.Find(row.ItemId) == null)
                        errors.Add("改路消耗表：" + label + " 指向不存在的器材 " + row.ItemId);
                }
                else if (!string.IsNullOrEmpty(row.ItemId))
                    errors.Add("改路消耗表：" + label + " 不是花器材，却写了 itemId");
                if (row.LayerFilter < 0 || row.LayerFilter > ChapterCatalog.LayerCount)
                    errors.Add("改路消耗表：" + label + " 的层号越界：" + row.LayerFilter);
            }
            return errors;
        }
    }
}
