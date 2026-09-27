using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>
    /// 房间能给的东西 / 要走的代价（**效果词表**）。内容表只能先用"已接线"的词——
    /// 这样"表里写了但代码没实现"这种半成品状态不可能出现。
    /// </summary>
    public enum RoomEffectKind
    {
        None = 0,
        Gear = 1,         // 给一件器材（已接线：走 AcquireEquipment）
        Money = 2,        // 给局内现金（已接线：走 GrantRunCash）
        Keepsake = 3,     // 给一件纪念品（未接线）
        Companion = 4,    // 遇到 / 请一位旅伴（未接线）
        Debuff = 5,       // 加一个负面状态（未接线）
        ClearDebuff = 6,  // 清一个负面状态（未接线）
        Heal = 7,         // 回状态（未接线）
        Journal = 8,      // 册子记一笔（未接线）
        FavorCost = 9,    // 花人情（未接线）
    }

    /// <summary>房间里的一个选项：给什么、要什么。</summary>
    public sealed class RoomOptionRow
    {
        public string OptionId { get; }
        public string LabelKey { get; }
        public RoomEffectKind GiveKind { get; }
        public string GiveId { get; }        // Gear 用 gearId；其余留空
        public int GiveAmount { get; }
        public RoomEffectKind TakeKind { get; }
        public string TakeId { get; }
        public int TakeAmount { get; }
        public bool Enabled { get; }

        public RoomOptionRow(string optionId, string labelKey,
            RoomEffectKind giveKind = RoomEffectKind.None, string giveId = null, int giveAmount = 0,
            RoomEffectKind takeKind = RoomEffectKind.None, string takeId = null, int takeAmount = 0,
            bool enabled = true)
        {
            OptionId = optionId;
            LabelKey = labelKey;
            GiveKind = giveKind;
            GiveId = giveId;
            GiveAmount = giveAmount;
            TakeKind = takeKind;
            TakeId = takeId;
            TakeAmount = takeAmount;
            Enabled = enabled;
        }
    }

    /// <summary>一个房间的内容：场景名 + 情境文案 + 1–3 个选项。</summary>
    public sealed class RoomContentRow
    {
        public string ContentId { get; }
        public ChapterRoomKind RoomKind { get; }
        public int LayerFilter { get; }      // 0 = 任意层
        public string SceneKey { get; }
        public string TextKey { get; }
        public IReadOnlyList<string> OptionIds { get; }
        public bool Enabled { get; }

        public RoomContentRow(string contentId, ChapterRoomKind roomKind, int layerFilter,
            string sceneKey, string textKey, IReadOnlyList<string> optionIds, bool enabled = true)
        {
            ContentId = contentId;
            RoomKind = roomKind;
            LayerFilter = layerFilter;
            SceneKey = sceneKey;
            TextKey = textKey;
            OptionIds = optionIds ?? new List<string>();
            Enabled = enabled;
        }
    }

    /// <summary>
    /// 房间内容表（事件 / 休息 / 终房纪念品）。**当前内容为空**——这一轮只出表结构与校验口子，
    /// 房间逐个做的时候往这三张表里填行（见 docs/chapter-rooms-spec.md §6 的字段与草案）。
    ///
    /// 分工：**"房间里发生什么"写在这里**（场景 / 文案 / 选项 / 给什么要什么），
    /// **"怎么生效"由 RoomContentService 负责**（已接线的效果才允许写进表）。
    /// </summary>
    public static class RoomContentCatalog
    {
        /// <summary>已经接线的效果词（内容表只能先用这些）。</summary>
        public static readonly IReadOnlyList<RoomEffectKind> WiredEffects = new[]
        {
            RoomEffectKind.None, RoomEffectKind.Gear, RoomEffectKind.Money,
        };

        // ── 占位房（作者口径 2026-09-27："当前房间可以都做几类占位房，房间类型走配表"）──
        // 效果只允许用已接线的词（None / Gear / Money）；文案走 Loc key，改文案不用改代码。
        private static readonly RoomOptionRow[] options = new RoomOptionRow[]
        {
            new RoomOptionRow("OP_LEAVE", "room.op.leave"),
            new RoomOptionRow("OP_LOOK", "room.op.look", giveKind: RoomEffectKind.Money, giveAmount: 20),
            new RoomOptionRow("OP_TAKE_HOTBAG", "room.op.take.hotbag", giveKind: RoomEffectKind.Gear, giveId: "CG06"),
            new RoomOptionRow("OP_TAKE_BANDAGE", "room.op.take.bandage", giveKind: RoomEffectKind.Gear, giveId: "CG13"),
            new RoomOptionRow("OP_TAKE_PLIERS", "room.op.take.pliers", giveKind: RoomEffectKind.Gear, giveId: "CG01"),
        };

        private static readonly RoomContentRow[] events = new RoomContentRow[]
        {
            new RoomContentRow("RC_EMPTY", ChapterRoomKind.Event, 0,
                "room.rc.empty.scene", "room.rc.empty.text", new[] { "OP_LEAVE" }),
            new RoomContentRow("RC_VISITED", ChapterRoomKind.Event, 0,
                "room.rc.visited.scene", "room.rc.visited.text", new[] { "OP_LOOK", "OP_LEAVE" }),
            // 第 2 层末的"馈赠占位房"：三件器材里挑一件（三选一奖励的第一版）
            new RoomContentRow("RC_GIFT", ChapterRoomKind.Event, 0,
                "room.rc.gift.scene", "room.rc.gift.text",
                new[] { "OP_TAKE_HOTBAG", "OP_TAKE_BANDAGE", "OP_TAKE_PLIERS" }),
        };

        private static readonly RoomContentRow[] rests = new RoomContentRow[]
        {
            new RoomContentRow("RC_REST", ChapterRoomKind.Rest, 0,
                "room.rc.rest.scene", "room.rc.rest.text", new[] { "OP_LEAVE" }),
        };

        private static readonly RoomContentRow[] keepsakes = new RoomContentRow[]
        {
            new RoomContentRow("RC_END", ChapterRoomKind.Final, 0,
                "room.rc.end.scene", "room.rc.end.text", new[] { "OP_LEAVE" }),
        };

        public static IReadOnlyList<RoomOptionRow> Options { get { return options; } }
        public static IReadOnlyList<RoomContentRow> Events { get { return events; } }
        public static IReadOnlyList<RoomContentRow> Rests { get { return rests; } }
        public static IReadOnlyList<RoomContentRow> Keepsakes { get { return keepsakes; } }

        public static bool IsWired(RoomEffectKind kind)
        {
            for (int i = 0; i < WiredEffects.Count; i++)
                if (WiredEffects[i] == kind) return true;
            return false;
        }

        public static RoomOptionRow FindOption(string optionId, IReadOnlyList<RoomOptionRow> table = null)
        {
            if (string.IsNullOrEmpty(optionId)) return null;
            IReadOnlyList<RoomOptionRow> source = table ?? options;
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null && source[i].OptionId == optionId) return source[i];
            return null;
        }

        /// <summary>按内容 id 找一条房间内容（三张表一起找）。</summary>
        public static RoomContentRow FindContent(string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return null;
            IReadOnlyList<RoomContentRow>[] tables = { events, rests, keepsakes };
            for (int t = 0; t < tables.Length; t++)
            {
                IReadOnlyList<RoomContentRow> table = tables[t];
                for (int i = 0; i < table.Count; i++)
                    if (table[i] != null && table[i].ContentId == contentId) return table[i];
            }
            return null;
        }

        /// <summary>某类房间的候选内容（事件房 / 休息房 / 终房），按层过滤。</summary>
        public static List<RoomContentRow> RowsFor(ChapterRoomKind kind, int layer,
            IReadOnlyList<RoomContentRow> table = null)
        {
            var result = new List<RoomContentRow>();
            IReadOnlyList<RoomContentRow> source = table ?? TableFor(kind);
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                RoomContentRow row = source[i];
                if (row == null || !row.Enabled) continue;
                if (row.RoomKind != kind) continue;
                if (row.LayerFilter != 0 && row.LayerFilter != layer) continue;
                result.Add(row);
            }
            return result;
        }

        public static IReadOnlyList<RoomContentRow> TableFor(ChapterRoomKind kind)
        {
            switch (kind)
            {
                case ChapterRoomKind.Event: return events;
                case ChapterRoomKind.Rest: return rests;
                case ChapterRoomKind.Final: return keepsakes;
                default: return null;
            }
        }

        public static List<string> Validate()
        {
            return Validate(options, events, rests, keepsakes);
        }

        /// <summary>
        /// 校验三张内容表，返回错误说明列表（空 = 通过）。空表必须通过（内容还没铺是正常状态）。
        /// 抓的是"写错了但不会立刻报错"的几类：id 空/重复、启用行缺文案、金额为负、
        /// **用了还没接线的效果**、Gear 没给 id 或指向不存在的器材、内容行选项数不在 1–3、
        /// 引用不存在的选项、房间类型不对（演奏/商店不能有内容行）、层号越界。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<RoomOptionRow> optionTable,
            IReadOnlyList<RoomContentRow> eventTable, IReadOnlyList<RoomContentRow> restTable,
            IReadOnlyList<RoomContentRow> keepsakeTable)
        {
            var errors = new List<string>();
            if (optionTable == null) optionTable = new List<RoomOptionRow>();

            var seenOptions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < optionTable.Count; i++)
            {
                RoomOptionRow row = optionTable[i];
                if (row == null) { errors.Add("选项表：第 " + i + " 行为空"); continue; }
                string label = string.IsNullOrEmpty(row.OptionId) ? "第 " + i + " 行" : row.OptionId;
                if (string.IsNullOrEmpty(row.OptionId)) errors.Add("选项表：第 " + i + " 行的 optionId 为空");
                else if (!seenOptions.Add(row.OptionId)) errors.Add("选项表：" + row.OptionId + " 重复");
                if (row.Enabled && string.IsNullOrEmpty(row.LabelKey))
                    errors.Add("选项表：" + label + " 缺 labelKey（玩家可见文本要走 Loc）");
                if (row.GiveAmount < 0 || row.TakeAmount < 0)
                    errors.Add("选项表：" + label + " 的金额不能为负");
                if (!IsWired(row.GiveKind))
                    errors.Add("选项表：" + label + " 想给 " + row.GiveKind + "，但这个效果还没接线");
                if (!IsWired(row.TakeKind))
                    errors.Add("选项表：" + label + " 想要 " + row.TakeKind + "，但这个效果还没接线");
                if (row.GiveKind == RoomEffectKind.Gear)
                {
                    if (string.IsNullOrEmpty(row.GiveId)) errors.Add("选项表：" + label + " 给器材却没写 gearId");
                    else if (GearCatalog.Find(row.GiveId) == null)
                        errors.Add("选项表：" + label + " 指向不存在的器材 " + row.GiveId);
                }
                else if (!string.IsNullOrEmpty(row.GiveId))
                {
                    errors.Add("选项表：" + label + " 不是给器材，却写了 gearId");
                }
            }

            var seenRows = new HashSet<string>(StringComparer.Ordinal);
            ValidateRows(eventTable, ChapterRoomKind.Event, "事件表", optionTable, seenRows, errors);
            ValidateRows(restTable, ChapterRoomKind.Rest, "休息表", optionTable, seenRows, errors);
            ValidateRows(keepsakeTable, ChapterRoomKind.Final, "终房表", optionTable, seenRows, errors);
            return errors;
        }

        private static void ValidateRows(IReadOnlyList<RoomContentRow> table, ChapterRoomKind expectedKind,
            string tableName, IReadOnlyList<RoomOptionRow> optionTable, HashSet<string> seenRows, List<string> errors)
        {
            if (table == null) return;
            for (int i = 0; i < table.Count; i++)
            {
                RoomContentRow row = table[i];
                if (row == null) { errors.Add(tableName + "：第 " + i + " 行为空"); continue; }
                string label = string.IsNullOrEmpty(row.ContentId) ? "第 " + i + " 行" : row.ContentId;
                if (string.IsNullOrEmpty(row.ContentId)) errors.Add(tableName + "：第 " + i + " 行的 contentId 为空");
                else if (!seenRows.Add(row.ContentId)) errors.Add(tableName + "：" + row.ContentId + " 重复");
                if (row.RoomKind != expectedKind)
                    errors.Add(tableName + "：" + label + " 的房间类型应为 " + expectedKind + "，实际 " + row.RoomKind);
                if (row.LayerFilter < 0 || row.LayerFilter > ChapterCatalog.LayerCount)
                    errors.Add(tableName + "：" + label + " 的层号越界：" + row.LayerFilter);
                if (row.Enabled && (string.IsNullOrEmpty(row.SceneKey) || string.IsNullOrEmpty(row.TextKey)))
                    errors.Add(tableName + "：" + label + " 缺 sceneKey/textKey（玩家可见文本要走 Loc）");
                if (row.OptionIds == null || row.OptionIds.Count == 0 || row.OptionIds.Count > 3)
                    errors.Add(tableName + "：" + label + " 的选项数应在 1–3，实际 "
                        + (row.OptionIds == null ? 0 : row.OptionIds.Count));
                if (row.OptionIds == null) continue;
                for (int k = 0; k < row.OptionIds.Count; k++)
                {
                    if (FindOption(row.OptionIds[k], optionTable) == null)
                        errors.Add(tableName + "：" + label + " 引用了不存在的选项 " + row.OptionIds[k]);
                }
            }
        }
    }
}
