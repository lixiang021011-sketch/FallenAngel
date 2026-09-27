using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>
    /// 一层的过渡页：**时刻 + 层名 + 一段引言 + 该层专属的失败文案**。
    /// 文案全部走 Loc key（玩家可见文本一律走 Loc.T），改文案不用改代码；
    /// 这一行只负责"哪一层、什么时刻、用哪几个 key"。
    ///
    /// 参照：集成战略每层一屏「时刻 + 层名 + 引言」（17:03 → 20:36 → 00:00 → --:--），
    /// 见 docs/layer-ui-spec.md §2。
    /// </summary>
    public sealed class LayerTransitionRow
    {
        public int Layer { get; }          // 1..LayerCount；终房用 LayerCount + 1
        public string TimeLabel { get; }   // 直接显示的时刻（不是 Loc key：它就是符号）
        public string NameKey { get; }
        public string IntroKey { get; }
        public string FailKey { get; }
        public bool Enabled { get; }

        public LayerTransitionRow(int layer, string timeLabel, string nameKey, string introKey,
            string failKey, bool enabled = true)
        {
            Layer = layer;
            TimeLabel = timeLabel;
            NameKey = nameKey;
            IntroKey = introKey;
            FailKey = failKey;
            Enabled = enabled;
        }
    }

    /// <summary>层过渡文案表（含终房那一屏）。</summary>
    public static class LayerCatalog
    {
        private static readonly LayerTransitionRow[] rows = new LayerTransitionRow[]
        {
            new LayerTransitionRow(1, "17:03", "layer.1.name", "layer.1.intro", "layer.1.fail"),
            new LayerTransitionRow(2, "20:36", "layer.2.name", "layer.2.intro", "layer.2.fail"),
            new LayerTransitionRow(3, "00:00", "layer.3.name", "layer.3.intro", "layer.3.fail"),
            // 终房：照参照"时间不存在"的写法（第六层是 --:--）
            new LayerTransitionRow(4, "--:--", "layer.final.name", "layer.final.intro", "layer.final.fail"),
        };

        public static IReadOnlyList<LayerTransitionRow> Rows { get { return rows; } }

        /// <summary>取某一屏（1–3 = 层；4 = 终房）。</summary>
        public static LayerTransitionRow Find(int layer)
        {
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null && rows[i].Layer == layer) return rows[i];
            return null;
        }

        public static List<string> Validate()
        {
            return Validate(rows);
        }

        /// <summary>校验：1–3 层各一行 + 终房一行；时刻非空；文案 key 齐全；层号不重复。</summary>
        public static List<string> Validate(IReadOnlyList<LayerTransitionRow> table)
        {
            var errors = new List<string>();
            if (table == null) return errors;
            var seen = new HashSet<int>();
            for (int i = 0; i < table.Count; i++)
            {
                LayerTransitionRow row = table[i];
                if (row == null) { errors.Add("层过渡表：第 " + i + " 行为空"); continue; }
                if (row.Layer < 1 || row.Layer > ChapterCatalog.LayerCount + 1)
                    errors.Add("层过渡表：层号越界 " + row.Layer);
                else if (!seen.Add(row.Layer)) errors.Add("层过渡表：第 " + row.Layer + " 屏重复");
                if (string.IsNullOrEmpty(row.TimeLabel)) errors.Add("层过渡表：第 " + row.Layer + " 屏缺时刻");
                if (row.Enabled && (string.IsNullOrEmpty(row.NameKey) || string.IsNullOrEmpty(row.IntroKey)
                    || string.IsNullOrEmpty(row.FailKey)))
                    errors.Add("层过渡表：第 " + row.Layer + " 屏缺 nameKey/introKey/failKey");
            }
            for (int layer = 1; layer <= ChapterCatalog.LayerCount + 1; layer++)
                if (!seen.Contains(layer)) errors.Add("层过渡表：缺第 " + layer + " 屏");
            return errors;
        }
    }
}
