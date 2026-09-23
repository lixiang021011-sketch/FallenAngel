#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 第一章结构与数值表的自检（内容未铺也要能验）：
    ///   · 出货表必须通过校验：3 层 / 16 房间 / 演奏 7 含硬仗 2 / 商店 2 / 事件 3 / 休息 2 / 终房 1 / 起点 1；
    ///   · 每层都有演奏房、起点在最前、终房在最后、层内序号连续；
    ///   · 稀有度按层倾斜（每层权重和为 1，越深白越少金越多）；
    ///   · 设计稿的数字对得上：价格 40/80/160、刷新首价 30、包 8 格、演奏房奖励 60/25/15、硬仗保底至少蓝；
    ///   · 故意写错的表（重复 id、少一场演奏、硬仗只剩 1、缺终房、起点跑错层、序号断档、层数不对、
    ///     稀有度权重和不是 1、某层没有演奏房）必须被逐条点到名。
    ///
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.ChapterChecks.Run）两用；
    /// 报告写在 Logs/chapter_check.txt。
    /// </summary>
    public static class ChapterChecks
    {
        private const string Tag = "[ChapterCatalog] ";

        [MenuItem("Tools/FallenAngel/Validate Chapter 1 Structure")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // ---------- 出货表本身 ----------
            var shipped = ChapterCatalog.Validate();
            check("出货表校验通过", shipped.Count == 0,
                shipped.Count == 0
                    ? string.Format("层 {0} / 房间 {1}", ChapterCatalog.Layers.Count, ChapterCatalog.Rooms.Count)
                    : string.Join("；", shipped.ToArray()));

            check("层数与房间总数",
                ChapterCatalog.Layers.Count == ChapterCatalog.LayerCount && ChapterCatalog.Rooms.Count == 16,
                string.Format("层 {0} / 房间 {1}", ChapterCatalog.Layers.Count, ChapterCatalog.Rooms.Count));

            int performances = ChapterCatalog.PerformanceCountOf(ChapterCatalog.Rooms);
            int hardFights = ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.HardFight);
            check("演奏 7 场、其中硬仗 2 场",
                performances == 7 && hardFights == 2,
                string.Format("演奏 {0}（硬仗 {1}）", performances, hardFights));

            check("六类房间的数量",
                ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Start) == 1 &&
                ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Shop) == 2 &&
                ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Event) == 3 &&
                ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Rest) == 2 &&
                ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Final) == 1,
                string.Format("起点 1 / 商店 {0} / 事件 {1} / 休息 {2} / 终房 {3}",
                    ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Shop),
                    ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Event),
                    ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Rest),
                    ChapterCatalog.CountOf(ChapterCatalog.Rooms, ChapterRoomKind.Final)));

            bool everyLayerPlays = true;
            for (int layer = 1; layer <= ChapterCatalog.LayerCount; layer++)
            {
                if (!ChapterCatalog.Rooms.Any(r => r.Layer == layer && r.IsPerformance)) everyLayerPlays = false;
            }
            check("每层都有演奏房", everyLayerPlays, "第 1 至 3 层各至少一场");

            var firstRoom = ChapterCatalog.Rooms.FirstOrDefault(r => r.Kind == ChapterRoomKind.Start);
            var lastRoom = ChapterCatalog.Rooms.LastOrDefault();
            check("起点在最前、终房在最后",
                firstRoom != null && firstRoom.Layer == 1 && firstRoom.Order == 1 &&
                lastRoom != null && lastRoom.Kind == ChapterRoomKind.Final,
                string.Format("首 = {0} / 末 = {1}",
                    firstRoom == null ? "?" : firstRoom.RoomId + "(" + firstRoom.Kind + ")",
                    lastRoom == null ? "?" : lastRoom.RoomId + "(" + lastRoom.Kind + ")"));

            // ---------- 稀有度与数值 ----------
            var layerSpecs = ChapterCatalog.Layers;
            bool weightsOk = layerSpecs.Count == 3 && layerSpecs.All(l => Math.Abs(l.TotalWeight - 1f) < 1e-4f);
            bool tiltOk = layerSpecs.Count == 3;
            for (int i = 1; i < layerSpecs.Count && tiltOk; i++)
            {
                if (layerSpecs[i].WhiteWeight > layerSpecs[i - 1].WhiteWeight) tiltOk = false;
                if (layerSpecs[i].GoldWeight < layerSpecs[i - 1].GoldWeight) tiltOk = false;
            }
            check("稀有度按层倾斜", weightsOk && tiltOk,
                string.Join(" / ", layerSpecs.Select(l => string.Format("L{0} {1:0.00}:{2:0.00}:{3:0.00}",
                    l.Layer, l.WhiteWeight, l.BlueWeight, l.GoldWeight)).ToArray()));

            check("价格 白 40 / 蓝 80 / 金 160 且递增",
                ChapterCatalog.PriceOf(GearRarity.White) == 40 &&
                ChapterCatalog.PriceOf(GearRarity.Blue) == 80 &&
                ChapterCatalog.PriceOf(GearRarity.Gold) == 160 &&
                ChapterCatalog.PriceOf(GearRarity.White) < ChapterCatalog.PriceOf(GearRarity.Blue) &&
                ChapterCatalog.PriceOf(GearRarity.Blue) < ChapterCatalog.PriceOf(GearRarity.Gold),
                string.Format("{0} / {1} / {2}",
                    ChapterCatalog.PriceOf(GearRarity.White),
                    ChapterCatalog.PriceOf(GearRarity.Blue),
                    ChapterCatalog.PriceOf(GearRarity.Gold)));

            check("包 8 格、刷新首价 30",
                ChapterCatalog.BagCapacity == 8 && ChapterCatalog.RefreshFirstPrice == 30,
                string.Format("包 {0} 格 / 刷新 {1}", ChapterCatalog.BagCapacity, ChapterCatalog.RefreshFirstPrice));

            float rewardSum = ChapterCatalog.RewardChoiceChance + ChapterCatalog.RewardCashChance + ChapterCatalog.RewardNothingChance;
            check("演奏房奖励 60 / 25 / 15",
                Math.Abs(ChapterCatalog.RewardChoiceChance - 0.60f) < 1e-4f &&
                Math.Abs(ChapterCatalog.RewardCashChance - 0.25f) < 1e-4f &&
                Math.Abs(ChapterCatalog.RewardNothingChance - 0.15f) < 1e-4f &&
                Math.Abs(rewardSum - 1f) < 1e-4f,
                string.Format("{0:0.00} / {1:0.00} / {2:0.00}（和 {3:0.00}）",
                    ChapterCatalog.RewardChoiceChance, ChapterCatalog.RewardCashChance,
                    ChapterCatalog.RewardNothingChance, rewardSum));

            check("硬仗保底至少蓝",
                (int)ChapterCatalog.HardFightMinimumRarity >= (int)GearRarity.Blue,
                "保底 " + ChapterCatalog.HardFightMinimumRarity);

            // ---------- 写错的表要被点名 ----------
            var badRooms = ChapterCatalog.Rooms.ToList();
            badRooms.Add(new ChapterRoomSpec("R11", 1, ChapterRoomKind.Event, 9));                       // id 重复
            badRooms.RemoveAll(r => r.RoomId == "R36");                                                  // 演奏少一场
            badRooms = badRooms
                .Select(r => r.RoomId == "R33"
                    ? new ChapterRoomSpec(r.RoomId, r.Layer, ChapterRoomKind.Performance, r.Order) : r)  // 硬仗只剩 1
                .Where(r => r.Kind != ChapterRoomKind.Final)                                             // 缺终房
                .Select(r => r.RoomId == "R00"
                    ? new ChapterRoomSpec("R00", 3, ChapterRoomKind.Start, 1) : r)                       // 起点跑错层
                .Select(r => r.RoomId == "R13"
                    ? new ChapterRoomSpec("R13", 1, ChapterRoomKind.Event, 9) : r)                       // 序号断档
                .ToList();
            var badLayers = new List<ChapterLayerSpec>
            {
                new ChapterLayerSpec(1, 0.70f, 0.25f, 0.05f),
                new ChapterLayerSpec(2, 0.50f, 0.40f, 0.05f),   // 权重和 0.95，而且只有两层
            };
            var badErrors = ChapterCatalog.Validate(badRooms, badLayers);
            var missing = Missing(badErrors, "重复", "演奏房", "硬仗", "终房", "起点房", "序号不连续", "层数", "稀有度权重和");
            check("写错的表要被点名", missing.Count == 0,
                missing.Count == 0
                    ? string.Format("抓到 {0} 条", badErrors.Count)
                    : "没点到：" + string.Join("、", missing.ToArray()));

            // 某层没有演奏房：单列一例（其余检查会同时报数，这里只看这一条）
            var noPlay = ChapterCatalog.Rooms
                .Select(r => r.RoomId == "R11"
                    ? new ChapterRoomSpec("R11", 1, ChapterRoomKind.Rest, r.Order) : r)
                .ToList();
            var noPlayErrors = ChapterCatalog.Validate(noPlay, ChapterCatalog.Layers);
            var missingNoPlay = Missing(noPlayErrors, "没有演奏房");
            check("某层没有演奏房要被点名", missingNoPlay.Count == 0,
                missingNoPlay.Count == 0 ? "已点名" : "没点到：" + string.Join("、", missingNoPlay.ToArray()));

            // ---------- 报告 ----------
            string summary = string.Format("{0} PASS {1} / FAIL {2}（第一章结构表；未接玩法、未铺内容）",
                Tag, pass, fail);
            for (int i = 0; i < lines.Count; i++) Debug.Log(Tag + lines[i]);
            Debug.Log(summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/chapter_check.txt", lines.ToArray());
                File.AppendAllText("Logs/chapter_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
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
    }
}
#endif
