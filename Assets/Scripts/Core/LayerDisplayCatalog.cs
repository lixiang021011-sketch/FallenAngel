using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>状态栏里可以出现的字段（按层开关）。</summary>
    [Flags]
    public enum StatusField
    {
        None = 0,
        Cash = 1 << 0,      // 现金
        Place = 1 << 1,     // 层与步
        Negative = 1 << 2,  // 负面状态
        Favor = 1 << 3,     // 人情
        Level = 1 << 4,     // 局内等级
        Companion = 1 << 5, // 旅伴
        All = Cash | Place | Negative | Favor | Level | Companion,
    }

    /// <summary>装备栏的显示档位（越深越少信息）。</summary>
    public enum GearDisplayTier
    {
        Full = 0,        // 描边 + 名字 + 来历（点开）
        IconsOnly = 1,   // 只留图标与稀有度描边
        Faded = 2,       // 统一灰
    }

    /// <summary>一层的显示档位：状态栏显示哪些字段、装备栏给到哪一档。</summary>
    public sealed class LayerDisplayRow
    {
        public int Layer { get; }
        public StatusField Fields { get; }
        public GearDisplayTier Gear { get; }

        public LayerDisplayRow(int layer, StatusField fields, GearDisplayTier gear)
        {
            Layer = layer;
            Fields = fields;
            Gear = gear;
        }
    }

    /// <summary>
    /// 信息模型（作者口径，2026-09-27 拍板"按 b"）：**节点级浓雾 + 按层递减**。
    /// 这一张表管"按层递减"那一半——界面信息量随层减少；另一半（浓雾节点）在 map_nodes 的 fog 列上。
    /// 递减只影响"看得到多少"，不影响任何数值与判定。
    /// </summary>
    public static class LayerDisplayCatalog
    {
        private static readonly LayerDisplayRow[] rows = new LayerDisplayRow[]
        {
            // 第 1 层：全字段
            new LayerDisplayRow(1, StatusField.All, GearDisplayTier.Full),
            // 第 2 层：收起来历与等级进度（等级只给数字 → 仍显示字段，展示方式由 UI 决定）
            new LayerDisplayRow(2, StatusField.All, GearDisplayTier.IconsOnly),
            // 第 3 层：只剩 现金 / 层与步 / 负面；装备统一灰
            new LayerDisplayRow(3, StatusField.Cash | StatusField.Place | StatusField.Negative,
                GearDisplayTier.Faded),
        };

        public static IReadOnlyList<LayerDisplayRow> Rows { get { return rows; } }

        public static LayerDisplayRow Find(int layer)
        {
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null && rows[i].Layer == layer) return rows[i];
            // 超出（终房等）按最后一层处理
            return rows.Length > 0 ? rows[rows.Length - 1] : null;
        }

        public static bool Shows(int layer, StatusField field)
        {
            LayerDisplayRow row = Find(layer);
            return row != null && (row.Fields & field) == field;
        }

        public static List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<int>();
            for (int i = 0; i < rows.Length; i++)
            {
                LayerDisplayRow row = rows[i];
                if (row == null) { errors.Add("层显示表：第 " + i + " 行为空"); continue; }
                if (row.Layer < 1 || row.Layer > ChapterCatalog.LayerCount)
                    errors.Add("层显示表：层号越界 " + row.Layer);
                else if (!seen.Add(row.Layer)) errors.Add("层显示表：第 " + row.Layer + " 层重复");
            }
            for (int layer = 1; layer <= ChapterCatalog.LayerCount; layer++)
                if (!seen.Contains(layer)) errors.Add("层显示表：缺第 " + layer + " 层");
            // 越深信息不能变多（只能持平或更少）
            var ordered = new List<LayerDisplayRow>(rows);
            ordered.Sort((a, b) => a.Layer.CompareTo(b.Layer));
            for (int i = 1; i < ordered.Count; i++)
            {
                if ((ordered[i].Fields & ~ordered[i - 1].Fields) != StatusField.None)
                    errors.Add("层显示表：第 " + ordered[i].Layer + " 层显示的字段比上一层多（越深信息只能更少）");
                if ((int)ordered[i].Gear < (int)ordered[i - 1].Gear)
                    errors.Add("层显示表：第 " + ordered[i].Layer + " 层装备栏比上一层更清楚（越深只能更模糊）");
            }
            return errors;
        }
    }
}
