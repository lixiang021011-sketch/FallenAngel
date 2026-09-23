using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FallenAngel.Core
{
    /// <summary>章节地图上的房间类型（设计稿 §3.2 的六类，另加起点与终房）。</summary>
    public enum ChapterRoomKind
    {
        Start = 0,        // 起点房
        Performance = 1,  // 演奏房
        HardFight = 2,    // 硬仗（人多、灯亮；档位高一档，输了路费不退）
        Shop = 3,         // 商店（旧货市场 / 音像店 / 小商品市场；玩家看到的字不用"商店"）
        Event = 4,        // 事件房
        Rest = 5,         // 休息房（招待所 / 网吧包夜 / 候车厅的长椅）
        Final = 6,        // 终房：这一程的落点
    }

    /// <summary>器材稀有度三档（设计稿 §6.2 的价格表就是按它给的）。</summary>
    public enum GearRarity
    {
        White = 0,
        Blue = 1,
        Gold = 2,
    }

    /// <summary>一层的规格：稀有度按层倾斜（设计稿 §6.3）。</summary>
    public sealed class ChapterLayerSpec
    {
        public int Layer { get; }
        public float WhiteWeight { get; }
        public float BlueWeight { get; }
        public float GoldWeight { get; }

        public ChapterLayerSpec(int layer, float whiteWeight, float blueWeight, float goldWeight)
        {
            Layer = layer;
            WhiteWeight = whiteWeight;
            BlueWeight = blueWeight;
            GoldWeight = goldWeight;
        }

        public float TotalWeight { get { return WhiteWeight + BlueWeight + GoldWeight; } }
    }

    /// <summary>章节地图上的一个房间：id + 层 + 类型 + 层内序号（顺序推进用）。</summary>
    public sealed class ChapterRoomSpec
    {
        public string RoomId { get; }
        public int Layer { get; }
        public ChapterRoomKind Kind { get; }
        public int Order { get; }

        public ChapterRoomSpec(string roomId, int layer, ChapterRoomKind kind, int order)
        {
            RoomId = roomId;
            Layer = layer;
            Kind = kind;
            Order = order;
        }

        /// <summary>是不是演奏类房间（演奏房与硬仗都算"一场演奏"）。</summary>
        public bool IsPerformance
        {
            get { return Kind == ChapterRoomKind.Performance || Kind == ChapterRoomKind.HardFight; }
        }
    }

    /// <summary>
    /// 第一章（千禧梦核）的结构与数值表。
    ///
    /// 本轮只出结构与校验：不铺内容、不接玩法、不动现有一局的推进（现网地图仍是 map_nodes 的 9 节点）。
    /// 表里的数字全部抄自 docs/roguelike-chapter1-design.md：
    ///   · §3.1 层 3 / 演奏 7（含硬仗 2）/ 商店 2 / 事件 3 / 休息 2 / 终房 1；
    ///   · §5.2 包 8 格；
    ///   · §5.1 演奏房奖励 三选一 60% / 给钱 25% / 无事 15%，硬仗保底至少蓝；
    ///   · §6.2 价格 白 40 / 蓝 80 / 金 160，刷新首次 30；
    ///   · §6.3 稀有度按层倾斜 70/25/5、50/40/10、35/45/20。
    ///
    /// 待拍板：每一层各放几个房间。表里按"总数照抄设计稿"的约束给了一个切法，改动只需动 Rooms。
    /// </summary>
    public static class ChapterCatalog
    {
        public const int LayerCount = 3;
        public const int PerformanceCount = 7;   // 含硬仗
        public const int HardFightCount = 2;
        public const int ShopCount = 2;
        public const int EventCount = 3;
        public const int RestCount = 2;
        public const int FinalCount = 1;
        public const int StartCount = 1;
        public const int BagCapacity = 8;
        public const int RefreshFirstPrice = 30;

        /// <summary>演奏房奖励比例（设计稿 §5.1）。</summary>
        public const float RewardChoiceChance = 0.60f;
        public const float RewardCashChance = 0.25f;
        public const float RewardNothingChance = 0.15f;

        /// <summary>硬仗保底：至少一件蓝以上（设计稿 §3.2 / §5.1）。</summary>
        public const GearRarity HardFightMinimumRarity = GearRarity.Blue;

        public const float WeightTolerance = 1e-4f;

        /// <summary>稀有度到价格（设计稿 §6.2）。</summary>
        public static int PriceOf(GearRarity rarity)
        {
            switch (rarity)
            {
                case GearRarity.White: return 40;
                case GearRarity.Blue: return 80;
                case GearRarity.Gold: return 160;
                default: return 0;
            }
        }

        /// <summary>三层：稀有度倾斜照着设计稿 §6.3（越深越容易出好东西）。</summary>
        public static ReadOnlyCollection<ChapterLayerSpec> Layers { get; } =
            new ReadOnlyCollection<ChapterLayerSpec>(new ChapterLayerSpec[]
        {
            new ChapterLayerSpec(1, 0.70f, 0.25f, 0.05f),
            new ChapterLayerSpec(2, 0.50f, 0.40f, 0.10f),
            new ChapterLayerSpec(3, 0.35f, 0.45f, 0.20f),
        });

        /// <summary>
        /// 16 个房间的顺序路线。每层切法是待拍板的提案（总数严格照设计稿：演奏 7 含硬仗 2、
        /// 商店 2、事件 3、休息 2、终房 1，加上起点）：第 1 层最短（一场认路），第 2 层开始有硬仗，
        /// 第 3 层最重（3 场普通 + 1 场硬仗）。
        /// </summary>
        public static ReadOnlyCollection<ChapterRoomSpec> Rooms { get; } =
            new ReadOnlyCollection<ChapterRoomSpec>(new ChapterRoomSpec[]
        {
            new ChapterRoomSpec("R00", 1, ChapterRoomKind.Start, 1),

            // 第 1 层：近，短
            new ChapterRoomSpec("R11", 1, ChapterRoomKind.Performance, 2),
            new ChapterRoomSpec("R12", 1, ChapterRoomKind.Event, 3),
            new ChapterRoomSpec("R13", 1, ChapterRoomKind.Shop, 4),

            // 第 2 层：开始有硬仗
            new ChapterRoomSpec("R21", 2, ChapterRoomKind.Performance, 1),
            new ChapterRoomSpec("R22", 2, ChapterRoomKind.HardFight, 2),
            new ChapterRoomSpec("R23", 2, ChapterRoomKind.Event, 3),
            new ChapterRoomSpec("R24", 2, ChapterRoomKind.Shop, 4),
            new ChapterRoomSpec("R25", 2, ChapterRoomKind.Rest, 5),

            // 第 3 层：最重，收在终房
            new ChapterRoomSpec("R31", 3, ChapterRoomKind.Performance, 1),
            new ChapterRoomSpec("R32", 3, ChapterRoomKind.Performance, 2),
            new ChapterRoomSpec("R33", 3, ChapterRoomKind.HardFight, 3),
            new ChapterRoomSpec("R34", 3, ChapterRoomKind.Event, 4),
            new ChapterRoomSpec("R35", 3, ChapterRoomKind.Rest, 5),
            new ChapterRoomSpec("R36", 3, ChapterRoomKind.Performance, 6),
            new ChapterRoomSpec("R37", 3, ChapterRoomKind.Final, 7),
        });

        /// <summary>按类型数房间。</summary>
        public static int CountOf(IList<ChapterRoomSpec> rooms, ChapterRoomKind kind)
        {
            if (rooms == null) return 0;
            int count = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].Kind == kind) count++;
            return count;
        }

        /// <summary>演奏类房间数（演奏房 + 硬仗）。</summary>
        public static int PerformanceCountOf(IList<ChapterRoomSpec> rooms)
        {
            if (rooms == null) return 0;
            int count = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].IsPerformance) count++;
            return count;
        }

        /// <summary>校验出货表（本文件里的表）。</summary>
        public static List<string> Validate()
        {
            return Validate(Rooms, Layers);
        }

        /// <summary>
        /// 校验一份章节表。返回中文错误清单（空 = 通过）；自检会喂故意写错的表确认这里抓得住。
        /// 只查结构自洽：层数、每类房间的数量、起点与终房的位置、层内序号、稀有度权重。
        /// 价格与奖励比例是常量（不是表），在自检里核对设计稿的值。
        /// </summary>
        public static List<string> Validate(IList<ChapterRoomSpec> rooms, IList<ChapterLayerSpec> layers)
        {
            var errors = new List<string>();
            if (rooms == null) rooms = new List<ChapterRoomSpec>();
            if (layers == null) layers = new List<ChapterLayerSpec>();

            // ---------- 层 ----------
            if (layers.Count != LayerCount)
                errors.Add("层数应为 " + LayerCount + "，实际 " + layers.Count);

            var seenLayers = new HashSet<int>();
            for (int i = 0; i < layers.Count; i++)
            {
                ChapterLayerSpec l = layers[i];
                if (l == null)
                {
                    errors.Add("第 " + (i + 1) + " 行层定义为空");
                    continue;
                }
                if (l.Layer < 1 || l.Layer > LayerCount)
                    errors.Add("层号越界：" + l.Layer);
                if (!seenLayers.Add(l.Layer))
                    errors.Add("层号重复：" + l.Layer);
                if (l.WhiteWeight < 0f || l.BlueWeight < 0f || l.GoldWeight < 0f)
                    errors.Add("第 " + l.Layer + " 层稀有度权重不能为负");
                if (Math.Abs(l.TotalWeight - 1f) > WeightTolerance)
                    errors.Add("第 " + l.Layer + " 层稀有度权重和应为 1，实际 " + l.TotalWeight.ToString("0.####"));
            }

            var ordered = layers.Where(l => l != null).OrderBy(l => l.Layer).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Layer != ordered[i - 1].Layer + 1)
                    errors.Add("层号不连续：" + ordered[i - 1].Layer + " 到 " + ordered[i].Layer);
                if (ordered[i].WhiteWeight > ordered[i - 1].WhiteWeight + WeightTolerance)
                    errors.Add("稀有度倾斜反了：第 " + ordered[i].Layer + " 层的白权重高于第 " + ordered[i - 1].Layer + " 层");
                if (ordered[i].GoldWeight < ordered[i - 1].GoldWeight - WeightTolerance)
                    errors.Add("稀有度倾斜反了：第 " + ordered[i].Layer + " 层的金权重低于第 " + ordered[i - 1].Layer + " 层");
            }

            // ---------- 房间 ----------
            var seenIds = new HashSet<string>();
            for (int i = 0; i < rooms.Count; i++)
            {
                ChapterRoomSpec r = rooms[i];
                if (r == null)
                {
                    errors.Add("第 " + (i + 1) + " 行房间定义为空");
                    continue;
                }
                if (string.IsNullOrEmpty(r.RoomId))
                    errors.Add("房间 id 不能为空（第 " + (i + 1) + " 行）");
                else if (!seenIds.Add(r.RoomId))
                    errors.Add("房间 id 重复：" + r.RoomId);
                if (r.Layer < 1 || r.Layer > LayerCount)
                    errors.Add("房间 " + r.RoomId + " 的层号越界：" + r.Layer);
            }

            int performances = PerformanceCountOf(rooms);
            if (performances != PerformanceCount)
                errors.Add("演奏房应为 " + PerformanceCount + "（含硬仗），实际 " + performances);

            int hardFights = CountOf(rooms, ChapterRoomKind.HardFight);
            if (hardFights != HardFightCount)
                errors.Add("硬仗应为 " + HardFightCount + "，实际 " + hardFights);

            int shops = CountOf(rooms, ChapterRoomKind.Shop);
            if (shops != ShopCount)
                errors.Add("商店房应为 " + ShopCount + "，实际 " + shops);

            int events = CountOf(rooms, ChapterRoomKind.Event);
            if (events != EventCount)
                errors.Add("事件房应为 " + EventCount + "，实际 " + events);

            int rests = CountOf(rooms, ChapterRoomKind.Rest);
            if (rests != RestCount)
                errors.Add("休息房应为 " + RestCount + "，实际 " + rests);

            int finals = CountOf(rooms, ChapterRoomKind.Final);
            if (finals != FinalCount)
                errors.Add("终房应为 " + FinalCount + "，实际 " + finals);

            int starts = CountOf(rooms, ChapterRoomKind.Start);
            if (starts != StartCount)
                errors.Add("起点房应为 " + StartCount + "，实际 " + starts);

            if (starts == 1)
            {
                ChapterRoomSpec start = rooms.First(r => r != null && r.Kind == ChapterRoomKind.Start);
                if (start.Layer != 1)
                    errors.Add("起点房应在第 1 层，实际在第 " + start.Layer + " 层");
                else if (start.Order != 1)
                    errors.Add("起点房应是第 1 层的第一个房间，实际序号 " + start.Order);
            }

            if (finals == 1)
            {
                ChapterRoomSpec final = rooms.First(r => r != null && r.Kind == ChapterRoomKind.Final);
                var inFinalLayer = rooms.Where(r => r != null && r.Layer == final.Layer).ToList();
                int lastOrder = inFinalLayer.Count == 0 ? 0 : inFinalLayer.Max(r => r.Order);
                if (final.Layer != LayerCount)
                    errors.Add("终房应在第 " + LayerCount + " 层，实际在第 " + final.Layer + " 层");
                else if (final.Order != lastOrder)
                    errors.Add("终房应是本层最后一个房间，实际序号 " + final.Order + "（本层最大 " + lastOrder + "）");
            }

            for (int layer = 1; layer <= LayerCount; layer++)
            {
                var inLayer = rooms.Where(r => r != null && r.Layer == layer).OrderBy(r => r.Order).ToList();
                if (inLayer.Count == 0)
                {
                    errors.Add("第 " + layer + " 层没有房间");
                    continue;
                }
                if (!inLayer.Any(r => r.IsPerformance))
                    errors.Add("第 " + layer + " 层没有演奏房（每层至少一场演奏）");
                for (int i = 0; i < inLayer.Count; i++)
                {
                    if (inLayer[i].Order != i + 1)
                    {
                        errors.Add("第 " + layer + " 层房间序号不连续：第 " + (i + 1) + " 个序号是 " + inLayer[i].Order);
                        break;
                    }
                }
            }

            return errors;
        }
    }
}
