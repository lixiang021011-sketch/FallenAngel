using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>通用件（左手随身器材）的一行内容：是什么、值多少、给什么效果。</summary>
    public sealed class GearItemRow
    {
        public string GearId { get; }
        public GearRarity Rarity { get; }
        /// <summary>价格由稀有度推出（<see cref="ChapterCatalog.PriceOf"/>），所以写不进错。</summary>
        public int Price { get; }
        /// <summary>已实现且可获得。未启用的行只是"表里占位"，不进掉落与商店。</summary>
        public bool Enabled { get; }
        /// <summary>功能型：由专门接缝接管（预览 / 本层可见 / 失败重试 / 吸附…）；效果型为 null。</summary>
        public string FeatureId { get; }
        public string NameKey { get; }
        public string DescKey { get; }
        /// <summary>来历三句（别人给的 / 家里带的 / 想不起来哪来的）；获得时随机取一句记进册子。</summary>
        public string OriginKeyA { get; }
        public string OriginKeyB { get; }
        public string OriginKeyC { get; }

        public GearItemRow(string gearId, GearRarity rarity, bool enabled,
            string nameKey = null, string descKey = null, string featureId = null,
            string originKeyA = null, string originKeyB = null, string originKeyC = null)
        {
            GearId = gearId;
            Rarity = rarity;
            Price = ChapterCatalog.PriceOf(rarity);
            Enabled = enabled;
            FeatureId = featureId;
            NameKey = nameKey;
            DescKey = descKey;
            OriginKeyA = originKeyA;
            OriginKeyB = originKeyB;
            OriginKeyC = originKeyC;
        }
    }

    /// <summary>器材 → 效果内容行的关联：一件可以挂多条（CG08 耳塞同时改窗口与分数）。</summary>
    public sealed class GearEffectRow
    {
        public string GearId { get; }
        public string ModifierId { get; }

        public GearEffectRow(string gearId, string modifierId)
        {
            GearId = gearId;
            ModifierId = modifierId;
        }
    }

    /// <summary>
    /// 通用件内容表（16 件，设计稿 §4.2）。写法与分工照 <see cref="ModifierCatalog"/> / <see cref="BuildCatalog"/>：
    /// 效果型只挂 modifier 内容行（生命周期交给 ModifierManager），功能型写 FeatureId 由专门接缝接管，两型二选一。
    ///
    /// 稀有度分布按设计稿：白 9 / 蓝 5 / 金 2（改动卡池时同步改校验）。每件的实现规格见
    /// docs/common-gear-spec.md；当前 Enabled = true 的是第一批：CG01 / CG06 / CG08 / CG12 / CG13。
    /// </summary>
    public static class GearCatalog
    {
        private static readonly GearItemRow[] items = new GearItemRow[]
        {
            // 编号沿用设计稿 §4.2 的行序；只有已实现的那几行填 Loc key 与来历。
            new GearItemRow("CG01", GearRarity.White, true,
                "gear.cg01.name", "gear.cg01.desc", null,
                "gear.cg01.origin.a", "gear.cg01.origin.b", "gear.cg01.origin.c"),
            new GearItemRow("CG02", GearRarity.White, false),
            new GearItemRow("CG03", GearRarity.Blue, false),
            new GearItemRow("CG04", GearRarity.Blue, false),
            new GearItemRow("CG05", GearRarity.White, false),
            new GearItemRow("CG06", GearRarity.White, true,
                "gear.cg06.name", "gear.cg06.desc", null,
                "gear.cg06.origin.a", "gear.cg06.origin.b", "gear.cg06.origin.c"),
            new GearItemRow("CG07", GearRarity.White, false),
            new GearItemRow("CG08", GearRarity.Blue, true,
                "gear.cg08.name", "gear.cg08.desc", null,
                "gear.cg08.origin.a", "gear.cg08.origin.b", "gear.cg08.origin.c"),
            new GearItemRow("CG09", GearRarity.White, false),
            new GearItemRow("CG10", GearRarity.White, false),
            new GearItemRow("CG11", GearRarity.Blue, false),
            new GearItemRow("CG12", GearRarity.White, true,
                "gear.cg12.name", "gear.cg12.desc", null,
                "gear.cg12.origin.a", "gear.cg12.origin.b", "gear.cg12.origin.c"),
            new GearItemRow("CG13", GearRarity.White, true,
                "gear.cg13.name", "gear.cg13.desc", null,
                "gear.cg13.origin.a", "gear.cg13.origin.b", "gear.cg13.origin.c"),
            new GearItemRow("CG14", GearRarity.Gold, false),
            new GearItemRow("CG15", GearRarity.Gold, false),
            new GearItemRow("CG16", GearRarity.Blue, false),
        };

        private static readonly GearEffectRow[] effects = new GearEffectRow[]
        {
            new GearEffectRow("CG01", "MC_LINE_LOCK"),
            new GearEffectRow("CG06", "MC_HOTBAG"),
            new GearEffectRow("CG08", "MC_EARPLUG_TIGHT"),
            new GearEffectRow("CG08", "MC_EARPLUG_SCORE"),
            new GearEffectRow("CG12", "MC_THROAT_BONUS"),
            new GearEffectRow("CG13", "MC_BANDAGE"),
        };

        public static IReadOnlyList<GearItemRow> Items { get { return items; } }
        public static IReadOnlyList<GearEffectRow> Effects { get { return effects; } }

        public static GearItemRow Find(string gearId)
        {
            return Find(gearId, items);
        }

        public static GearItemRow Find(string gearId, IReadOnlyList<GearItemRow> table)
        {
            if (string.IsNullOrEmpty(gearId) || table == null) return null;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].GearId == gearId) return table[i];
            }
            return null;
        }

        /// <summary>某件挂着的效果内容行 id（按表顺序；没有就返回空表）</summary>
        public static List<string> ModifierIdsOf(string gearId, IReadOnlyList<GearEffectRow> links = null)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(gearId)) return result;
            IReadOnlyList<GearEffectRow> table = links ?? effects;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].GearId == gearId) result.Add(table[i].ModifierId);
            }
            return result;
        }

        /// <summary>已启用（已实现）的件数；内容期逐步打开，自检会核对。</summary>
        public static int EnabledCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < items.Length; i++) if (items[i].Enabled) count++;
                return count;
            }
        }

        public static List<string> Validate()
        {
            return Validate(items, effects, ModifierCatalog.Rows);
        }

        /// <summary>
        /// 校验通用件表，返回错误说明列表（空 = 通过）。只读。
        /// 抓"写错了但不会立刻报错"的几类：id 空/重复、稀有度分布不对、启用了却缺 Loc key 或来历、
        /// 启用但既没效果也没 FeatureId、两型都填、关联行指向不存在或未启用的件与内容行。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<GearItemRow> table,
            IReadOnlyList<GearEffectRow> links, IReadOnlyList<ModifierContentRow> content)
        {
            var errors = new List<string>();
            if (table == null) table = new List<GearItemRow>();
            if (links == null) links = new List<GearEffectRow>();
            if (content == null) content = new List<ModifierContentRow>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int white = 0, blue = 0, gold = 0;

            for (int i = 0; i < table.Count; i++)
            {
                GearItemRow item = table[i];
                if (item == null)
                {
                    errors.Add("通用件表：第 " + i + " 行为空");
                    continue;
                }
                if (string.IsNullOrEmpty(item.GearId))
                    errors.Add("通用件表：第 " + i + " 行的 gearId 为空");
                else if (!seen.Add(item.GearId))
                    errors.Add("通用件表：" + item.GearId + " 重复");

                switch (item.Rarity)
                {
                    case GearRarity.White: white++; break;
                    case GearRarity.Blue: blue++; break;
                    case GearRarity.Gold: gold++; break;
                }

                if (!item.Enabled) continue;

                if (string.IsNullOrEmpty(item.NameKey) || string.IsNullOrEmpty(item.DescKey))
                    errors.Add("通用件表：" + item.GearId + " 缺 nameKey/descKey（玩家可见文本要走 Loc）");
                if (string.IsNullOrEmpty(item.OriginKeyA) || string.IsNullOrEmpty(item.OriginKeyB)
                    || string.IsNullOrEmpty(item.OriginKeyC))
                    errors.Add("通用件表：" + item.GearId + " 缺来历文案（三种都要）");

                int mods = ModifierIdsOf(item.GearId, links).Count;
                bool hasFeature = !string.IsNullOrEmpty(item.FeatureId);
                if (mods == 0 && !hasFeature)
                    errors.Add("通用件表：" + item.GearId + " 已启用，但既没有效果行也没有 FeatureId");
                if (mods > 0 && hasFeature)
                    errors.Add("通用件表：" + item.GearId + " 同时挂了效果行与 FeatureId（两型二选一）");
            }

            if (white != 9 || blue != 5 || gold != 2)
                errors.Add(string.Format(
                    "通用件表：稀有度分布应为 白 9 / 蓝 5 / 金 2，实际 白 {0} / 蓝 {1} / 金 {2}",
                    white, blue, gold));

            for (int i = 0; i < links.Count; i++)
            {
                GearEffectRow link = links[i];
                if (link == null)
                {
                    errors.Add("通用件关联表：第 " + i + " 行为空");
                    continue;
                }
                GearItemRow item = Find(link.GearId, table);
                if (item == null)
                {
                    errors.Add("通用件关联表：" + link.GearId + " 在通用件表里不存在");
                    continue;
                }
                if (!item.Enabled)
                    errors.Add("通用件关联表：" + link.GearId + " 尚未启用，却挂着效果");
                if (string.IsNullOrEmpty(link.ModifierId))
                {
                    errors.Add("通用件关联表：" + link.GearId + " 的 modifierId 为空");
                    continue;
                }
                ModifierContentRow row = ModifierCatalog.Find(link.ModifierId, content);
                if (row == null)
                    errors.Add("通用件关联表：" + link.GearId + " 指向不存在的内容行 " + link.ModifierId);
                else if (!row.Enabled)
                    errors.Add("通用件关联表：" + link.GearId + " 指向未启用的内容行 " + link.ModifierId);
            }

            return errors;
        }
    }
}
