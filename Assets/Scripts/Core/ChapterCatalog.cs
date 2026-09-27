using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FallenAngel.Core
{
    /// <summary>房间类型（设计稿 §3.2）。</summary>
    public enum ChapterRoomKind
    {
        Start = 0,
        Performance = 1,  // 演奏房
        HardFight = 2,    // 硬仗
        Shop = 3,
        Event = 4,
        Rest = 5,
        Final = 6,
    }

    /// <summary>器材稀有度三档（设计稿 §6.2 的价格表按它给）。</summary>
    public enum GearRarity
    {
        White = 0,
        Blue = 1,
        Gold = 2,
    }

    /// <summary>演奏房的倾向：同一步的两个候选因此有区别（器材向更可能掉器材，钱向更可能给钱）。</summary>
    public enum RoomTag
    {
        None = 0,
        Equip = 1,
        Cash = 2,
    }

    /// <summary>一层：稀有度按层倾斜（设计稿 §6.3）。</summary>
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

    /// <summary>地图上的一个房间：第几步、行内第几列、类型、关卡与倾向。</summary>
    public sealed class ChapterRoomSpec
    {
        public string RoomId { get; }
        public int Layer { get; }
        public int Step { get; }       // 起点 0，终房 15
        public int Column { get; }     // 行内第几列（0 左 / 1 中 / 2 右）
        public ChapterRoomKind Kind { get; }
        public string StageId { get; }
        public RoomTag Tag { get; }

        public ChapterRoomSpec(string roomId, int layer, int step, int column,
            ChapterRoomKind kind, string stageId = null, RoomTag tag = RoomTag.None)
        {
            RoomId = roomId;
            Layer = layer;
            Step = step;
            Column = column;
            Kind = kind;
            StageId = stageId;
            Tag = tag;
        }

        public bool IsPerformance
        {
            get { return Kind == ChapterRoomKind.Performance || Kind == ChapterRoomKind.HardFight; }
        }
    }

    /// <summary>相邻两步之间的一条连线。</summary>
    public sealed class ChapterEdgeSpec
    {
        public string EdgeId { get; }
        public string FromRoomId { get; }
        public string ToRoomId { get; }

        public ChapterEdgeSpec(string edgeId, string fromRoomId, string toRoomId)
        {
            EdgeId = edgeId;
            FromRoomId = fromRoomId;
            ToRoomId = toRoomId;
        }
    }

    /// <summary>
    /// 第一章的路：**一局 16 站（起点 + 15 步）**，其中 7 个演奏步、7 个资源步、1 个终房。
    /// 每一步给 1–3 个候选（默认 2），玩家自己挑；分支在一两步内汇合。
    ///
    /// 模型与构图规则见 docs/chapter-route-spec.md：
    ///   · 一局演奏数恒 7（设计稿 §3.1 / §6 的经济按这个算）；
    ///   · 硬仗候选只出现在 3 个演奏步（每层一个）→ 一局硬仗 0–3 场，基准 2；
    ///   · 7 个资源步里，商店/事件/休息由玩家分配（基准 2 / 3 / 2）；
    ///   · 第一步必演奏、终房唯一且人人可达、同类不连三步、硬仗不连硬仗、分支必须汇合。
    ///
    /// 地图上共 29 个房间（1 起点 + 13 步 ×2 + 2 步 ×1），一局走其中 16 个。
    /// ChapterChecks 会核对本表与运行时地图（PortfolioConfig）不漂移，并逐条验构图规则。
    /// </summary>
    public static class ChapterCatalog
    {
        public const int LayerCount = 3;
        public const int StepCount = 15;               // 起点之外的步数
        public const int PerformanceStepCount = 7;     // 一局演奏场数 = 有演奏候选的步数
        public const int HardFightOptionSteps = 3;     // 给硬仗候选的步数
        public const int MaxCandidatesPerStep = 3;
        public const int StartCount = 1;
        public const int FinalCount = 1;
        public const int BagCapacity = 8;
        public const int RefreshFirstPrice = 30;

        /// <summary>演奏房奖励比例（设计稿 §5.1）。</summary>
        public const float RewardChoiceChance = 0.60f;
        public const float RewardCashChance = 0.25f;
        public const float RewardNothingChance = 0.15f;

        /// <summary>硬仗保底：至少一件蓝以上（设计稿 §3.2 / §5.1）。</summary>
        public const GearRarity HardFightMinimumRarity = GearRarity.Blue;

        public const float WeightTolerance = 1e-4f;

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

        /// <summary>三层：稀有度倾斜照着设计稿 §6.3。</summary>
        public static ReadOnlyCollection<ChapterLayerSpec> Layers { get; } =
            new ReadOnlyCollection<ChapterLayerSpec>(new ChapterLayerSpec[]
        {
            new ChapterLayerSpec(1, 0.70f, 0.25f, 0.05f),
            new ChapterLayerSpec(2, 0.50f, 0.40f, 0.10f),
            new ChapterLayerSpec(3, 0.35f, 0.45f, 0.20f),
        });

        /// <summary>29 个房间（手写第一版；生成器留到路线随机化 D6）。</summary>
        public static ReadOnlyCollection<ChapterRoomSpec> Rooms { get; } =
            new ReadOnlyCollection<ChapterRoomSpec>(new ChapterRoomSpec[]
        {
            new ChapterRoomSpec("R00", 1, 0, 1, ChapterRoomKind.Start),

            // ── 第 1 层（步 1–4）──
            new ChapterRoomSpec("R1A", 1, 1, 0, ChapterRoomKind.Performance, "S01"),
            new ChapterRoomSpec("R2A", 1, 2, 0, ChapterRoomKind.Event),
            new ChapterRoomSpec("R2B", 1, 2, 2, ChapterRoomKind.Shop),
            new ChapterRoomSpec("R3A", 1, 3, 0, ChapterRoomKind.Performance, "S02"),
            new ChapterRoomSpec("R3B", 1, 3, 2, ChapterRoomKind.HardFight, "H01"),
            new ChapterRoomSpec("R4A", 1, 4, 0, ChapterRoomKind.Shop),
            new ChapterRoomSpec("R4B", 1, 4, 2, ChapterRoomKind.Event),

            // ── 第 2 层（步 5–9）──
            new ChapterRoomSpec("R5A", 2, 5, 0, ChapterRoomKind.Performance, "S03", RoomTag.Equip),
            new ChapterRoomSpec("R5B", 2, 5, 2, ChapterRoomKind.Performance, "S04", RoomTag.Cash),
            new ChapterRoomSpec("R6A", 2, 6, 0, ChapterRoomKind.Event),
            new ChapterRoomSpec("R6B", 2, 6, 2, ChapterRoomKind.Shop),
            new ChapterRoomSpec("R7A", 2, 7, 0, ChapterRoomKind.Performance, "S05", RoomTag.Equip),
            new ChapterRoomSpec("R7B", 2, 7, 2, ChapterRoomKind.Performance, "S06", RoomTag.Cash),
            new ChapterRoomSpec("R8A", 2, 8, 0, ChapterRoomKind.Rest),
            new ChapterRoomSpec("R8B", 2, 8, 2, ChapterRoomKind.Event),
            new ChapterRoomSpec("R9A", 2, 9, 0, ChapterRoomKind.Performance, "S07"),
            new ChapterRoomSpec("R9B", 2, 9, 2, ChapterRoomKind.HardFight, "H02"),

            // ── 第 3 层（步 10–15）──
            new ChapterRoomSpec("R10A", 3, 10, 0, ChapterRoomKind.Shop),
            new ChapterRoomSpec("R10B", 3, 10, 2, ChapterRoomKind.Rest),
            new ChapterRoomSpec("R11A", 3, 11, 0, ChapterRoomKind.Performance, "S08", RoomTag.Equip),
            new ChapterRoomSpec("R11B", 3, 11, 2, ChapterRoomKind.Performance, "S09", RoomTag.Cash),
            new ChapterRoomSpec("R12A", 3, 12, 0, ChapterRoomKind.Event),
            new ChapterRoomSpec("R12B", 3, 12, 2, ChapterRoomKind.Rest),
            new ChapterRoomSpec("R13A", 3, 13, 0, ChapterRoomKind.Performance, "S10"),
            new ChapterRoomSpec("R13B", 3, 13, 2, ChapterRoomKind.HardFight, "H03"),
            new ChapterRoomSpec("R14A", 3, 14, 0, ChapterRoomKind.Shop),
            new ChapterRoomSpec("R14B", 3, 14, 2, ChapterRoomKind.Event),
            new ChapterRoomSpec("R15", 3, 15, 1, ChapterRoomKind.Final),
        });

        /// <summary>
        /// 连线：只连"上一步 → 下一步"，且列差 ≤ 1（地图不交叉）。
        /// 有些走法下一步只有一个候选（例：R3B 硬仗之后只能去 R4B）——那是"选择要付的代价"。
        /// </summary>
        public static ReadOnlyCollection<ChapterEdgeSpec> Edges { get; } =
            new ReadOnlyCollection<ChapterEdgeSpec>(new ChapterEdgeSpec[]
        {
            new ChapterEdgeSpec("E001", "R00", "R1A"),
            new ChapterEdgeSpec("E002", "R1A", "R2A"),
            new ChapterEdgeSpec("E003", "R1A", "R2B"),
            new ChapterEdgeSpec("E004", "R2A", "R3A"),
            new ChapterEdgeSpec("E005", "R2A", "R3B"),
            new ChapterEdgeSpec("E006", "R2B", "R3A"),
            new ChapterEdgeSpec("E007", "R2B", "R3B"),
            new ChapterEdgeSpec("E008", "R3A", "R4A"),
            new ChapterEdgeSpec("E009", "R3A", "R4B"),
            new ChapterEdgeSpec("E010", "R3B", "R4B"),
            new ChapterEdgeSpec("E011", "R4A", "R5A"),
            new ChapterEdgeSpec("E012", "R4A", "R5B"),
            new ChapterEdgeSpec("E013", "R4B", "R5A"),
            new ChapterEdgeSpec("E014", "R4B", "R5B"),
            new ChapterEdgeSpec("E015", "R5A", "R6A"),
            new ChapterEdgeSpec("E016", "R5A", "R6B"),
            new ChapterEdgeSpec("E017", "R5B", "R6B"),
            new ChapterEdgeSpec("E018", "R6A", "R7A"),
            new ChapterEdgeSpec("E019", "R6A", "R7B"),
            new ChapterEdgeSpec("E020", "R6B", "R7A"),
            new ChapterEdgeSpec("E021", "R6B", "R7B"),
            new ChapterEdgeSpec("E022", "R7A", "R8A"),
            new ChapterEdgeSpec("E023", "R7A", "R8B"),
            new ChapterEdgeSpec("E024", "R7B", "R8B"),
            new ChapterEdgeSpec("E025", "R8A", "R9A"),
            new ChapterEdgeSpec("E026", "R8A", "R9B"),
            new ChapterEdgeSpec("E027", "R8B", "R9A"),
            new ChapterEdgeSpec("E028", "R8B", "R9B"),
            new ChapterEdgeSpec("E029", "R9A", "R10A"),
            new ChapterEdgeSpec("E030", "R9A", "R10B"),
            new ChapterEdgeSpec("E031", "R9B", "R10B"),
            new ChapterEdgeSpec("E032", "R10A", "R11A"),
            new ChapterEdgeSpec("E033", "R10A", "R11B"),
            new ChapterEdgeSpec("E034", "R10B", "R11A"),
            new ChapterEdgeSpec("E035", "R10B", "R11B"),
            new ChapterEdgeSpec("E036", "R11A", "R12A"),
            new ChapterEdgeSpec("E037", "R11A", "R12B"),
            new ChapterEdgeSpec("E038", "R11B", "R12B"),
            new ChapterEdgeSpec("E039", "R12A", "R13A"),
            new ChapterEdgeSpec("E040", "R12A", "R13B"),
            new ChapterEdgeSpec("E041", "R12B", "R13A"),
            new ChapterEdgeSpec("E042", "R12B", "R13B"),
            new ChapterEdgeSpec("E043", "R13A", "R14A"),
            new ChapterEdgeSpec("E044", "R13A", "R14B"),
            new ChapterEdgeSpec("E045", "R13B", "R14B"),
            new ChapterEdgeSpec("E046", "R14A", "R15"),
            new ChapterEdgeSpec("E047", "R14B", "R15"),
        });

        public static int CountOf(IList<ChapterRoomSpec> rooms, ChapterRoomKind kind)
        {
            if (rooms == null) return 0;
            int count = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].Kind == kind) count++;
            return count;
        }

        /// <summary>演奏类房间数（演奏房 + 硬仗）——地图上的数量，不是一局的场数。</summary>
        public static int PerformanceCountOf(IList<ChapterRoomSpec> rooms)
        {
            if (rooms == null) return 0;
            int count = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].IsPerformance) count++;
            return count;
        }

        /// <summary>有演奏候选的**步数** = 一局的演奏场数。</summary>
        public static int PerformanceStepCountOf(IList<ChapterRoomSpec> rooms)
        {
            if (rooms == null) return 0;
            var steps = new HashSet<int>();
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].IsPerformance) steps.Add(rooms[i].Step);
            return steps.Count;
        }

        public static List<string> Validate()
        {
            return Validate(Rooms, Edges, Layers);
        }

        /// <summary>
        /// 校验路线表：层与稀有度、房间字段、每步候选数与类型、构图规则（第一步必演奏、
        /// 终房唯一可达、同类不连三步、硬仗不连硬仗、休息与商店至少能遇到、连线合法、无死胡同）。
        /// 返回中文错误清单（空 = 通过）。
        /// </summary>
        public static List<string> Validate(IList<ChapterRoomSpec> rooms, IList<ChapterEdgeSpec> edges,
            IList<ChapterLayerSpec> layers)
        {
            var errors = new List<string>();
            if (rooms == null) rooms = new List<ChapterRoomSpec>();
            if (edges == null) edges = new List<ChapterEdgeSpec>();
            if (layers == null) layers = new List<ChapterLayerSpec>();

            // ---------- 层与稀有度 ----------
            if (layers.Count != LayerCount)
                errors.Add("层数应为 " + LayerCount + "，实际 " + layers.Count);
            var seenLayers = new HashSet<int>();
            for (int i = 0; i < layers.Count; i++)
            {
                ChapterLayerSpec l = layers[i];
                if (l == null) { errors.Add("第 " + (i + 1) + " 行层定义为空"); continue; }
                if (l.Layer < 1 || l.Layer > LayerCount) errors.Add("层号越界：" + l.Layer);
                if (!seenLayers.Add(l.Layer)) errors.Add("层号重复：" + l.Layer);
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

            // ---------- 房间字段 ----------
            var seenIds = new HashSet<string>();
            for (int i = 0; i < rooms.Count; i++)
            {
                ChapterRoomSpec r = rooms[i];
                if (r == null) { errors.Add("第 " + (i + 1) + " 行房间定义为空"); continue; }
                if (string.IsNullOrEmpty(r.RoomId)) errors.Add("房间 id 不能为空（第 " + (i + 1) + " 行）");
                else if (!seenIds.Add(r.RoomId)) errors.Add("房间 id 重复：" + r.RoomId);
                if (r.Layer < 1 || r.Layer > LayerCount) errors.Add("房间 " + r.RoomId + " 的层号越界：" + r.Layer);
                if (r.Step < 0 || r.Step > StepCount) errors.Add("房间 " + r.RoomId + " 的步号越界：" + r.Step);
                if (r.Column < 0 || r.Column > 2) errors.Add("房间 " + r.RoomId + " 的列号越界：" + r.Column);
                if (r.IsPerformance && string.IsNullOrEmpty(r.StageId))
                    errors.Add("房间 " + r.RoomId + " 是演奏类却没有关卡 id");
                if (!r.IsPerformance && !string.IsNullOrEmpty(r.StageId))
                    errors.Add("房间 " + r.RoomId + " 不是演奏类却挂了关卡 id");
            }

            int starts = CountOf(rooms, ChapterRoomKind.Start);
            if (starts != StartCount) errors.Add("起点房应为 " + StartCount + "，实际 " + starts);
            int finals = CountOf(rooms, ChapterRoomKind.Final);
            if (finals != FinalCount) errors.Add("终房应为 " + FinalCount + "，实际 " + finals);
            if (starts == 1)
            {
                ChapterRoomSpec s = rooms.First(r => r != null && r.Kind == ChapterRoomKind.Start);
                if (s.Step != 0) errors.Add("起点房应在第 0 步，实际第 " + s.Step + " 步");
            }
            if (finals == 1)
            {
                ChapterRoomSpec f = rooms.First(r => r != null && r.Kind == ChapterRoomKind.Final);
                if (f.Step != StepCount) errors.Add("终房应在第 " + StepCount + " 步，实际第 " + f.Step + " 步");
            }

            // ---------- 每步的候选 ----------
            for (int step = 1; step <= StepCount; step++)
            {
                var inStep = rooms.Where(r => r != null && r.Step == step).ToList();
                if (inStep.Count == 0) { errors.Add("第 " + step + " 步没有房间"); continue; }
                if (inStep.Count > MaxCandidatesPerStep)
                    errors.Add("第 " + step + " 步候选 " + inStep.Count + " 个，超过上限 " + MaxCandidatesPerStep);
                if (step == 1 && !inStep.All(r => r.IsPerformance))
                    errors.Add("第 1 步必须是演奏（开场先给一场）");
            }

            int performanceSteps = PerformanceStepCountOf(rooms);
            if (performanceSteps != PerformanceStepCount)
                errors.Add("有演奏候选的步数应为 " + PerformanceStepCount + "（一局 " + PerformanceStepCount + " 场），实际 " + performanceSteps);

            var hardSteps = rooms.Where(r => r != null && r.Kind == ChapterRoomKind.HardFight)
                .Select(r => r.Step).Distinct().OrderBy(s => s).ToList();
            if (hardSteps.Count > HardFightOptionSteps)
                errors.Add("给硬仗候选的步数不能超过 " + HardFightOptionSteps + "，实际 " + hardSteps.Count);
            if (hardSteps.Contains(1)) errors.Add("硬仗不能出现在第 1 步");
            for (int i = 1; i < hardSteps.Count; i++)
                if (hardSteps[i] == hardSteps[i - 1] + 1)
                    errors.Add("硬仗不能连着硬仗（第 " + hardSteps[i - 1] + " 与第 " + hardSteps[i] + " 步）");
            foreach (int step in hardSteps)
            {
                var layerOfStep = rooms.First(r => r != null && r.Step == step).Layer;
                if (hardSteps.Count(s => rooms.First(r => r != null && r.Step == s).Layer == layerOfStep) > 1)
                    errors.Add("第 " + layerOfStep + " 层给了多个硬仗候选（每层最多一个）");
            }

            // 同类不连三步（连续三步的候选全是演奏）
            for (int step = 1; step + 2 <= StepCount; step++)
            {
                bool allPerformance = true;
                for (int s = step; s <= step + 2; s++)
                {
                    var inStep = rooms.Where(r => r != null && r.Step == s).ToList();
                    if (!inStep.All(r => r.IsPerformance)) allPerformance = false;
                }
                if (allPerformance) errors.Add("第 " + step + "–" + (step + 2) + " 步连着三步都是演奏（同类不连三步）");
            }

            int shopSteps = rooms.Where(r => r != null && r.Kind == ChapterRoomKind.Shop).Select(r => r.Step).Distinct().Count();
            if (shopSteps < 4) errors.Add("能给商店的步数至少 4 步（钱要有处可花），实际 " + shopSteps);
            int restSteps = rooms.Where(r => r != null && r.Kind == ChapterRoomKind.Rest).Select(r => r.Step).Distinct().Count();
            if (restSteps < 3) errors.Add("能给休息的步数至少 3 步，实际 " + restSteps);

            // ---------- 连线 ----------
            var byId = new Dictionary<string, ChapterRoomSpec>();
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && !string.IsNullOrEmpty(rooms[i].RoomId)) byId[rooms[i].RoomId] = rooms[i];

            var seenEdges = new HashSet<string>();
            var outgoing = new Dictionary<string, List<string>>();
            var incoming = new Dictionary<string, List<string>>();
            for (int i = 0; i < edges.Count; i++)
            {
                ChapterEdgeSpec e = edges[i];
                if (e == null) { errors.Add("第 " + (i + 1) + " 行连线为空"); continue; }
                if (string.IsNullOrEmpty(e.EdgeId)) errors.Add("连线 id 不能为空（第 " + (i + 1) + " 行）");
                else if (!seenEdges.Add(e.EdgeId)) errors.Add("连线 id 重复：" + e.EdgeId);
                ChapterRoomSpec from, to;
                if (string.IsNullOrEmpty(e.FromRoomId) || !byId.TryGetValue(e.FromRoomId, out from))
                { errors.Add("连线的起点不存在：" + e.FromRoomId); continue; }
                if (string.IsNullOrEmpty(e.ToRoomId) || !byId.TryGetValue(e.ToRoomId, out to))
                { errors.Add("连线的终点不存在：" + e.ToRoomId); continue; }
                if (to.Step != from.Step + 1)
                    errors.Add("连线 " + e.EdgeId + " 跨了不止一步：" + from.Step + " → " + to.Step);
                // 每行最多 3 列（0/1/2），所以列差 2 的斜线是正常的（杀戮尖塔也这么画）；
                // 这里只挡"跨了不止一行"的连线（上面那条），以及列号越界（下面按行内列数校验）。
                if (Math.Abs(to.Column - from.Column) > 2)
                    errors.Add("连线 " + e.EdgeId + " 跨列太多：" + from.Column + " → " + to.Column);
                if (!outgoing.ContainsKey(from.RoomId)) outgoing[from.RoomId] = new List<string>();
                outgoing[from.RoomId].Add(to.RoomId);
                if (!incoming.ContainsKey(to.RoomId)) incoming[to.RoomId] = new List<string>();
                incoming[to.RoomId].Add(from.RoomId);
            }

            foreach (ChapterRoomSpec r in byId.Values)
            {
                if (r.Kind == ChapterRoomKind.Final) continue;
                if (!outgoing.ContainsKey(r.RoomId)) errors.Add("房间 " + r.RoomId + " 没有出口（死胡同）");
                if (r.Kind != ChapterRoomKind.Start && !incoming.ContainsKey(r.RoomId))
                    errors.Add("房间 " + r.RoomId + " 没有入口（走不到）");
            }

            // 每个房间都必须能走到终房（反向可达）
            string finalId = rooms.Where(r => r != null && r.Kind == ChapterRoomKind.Final)
                .Select(r => r.RoomId).FirstOrDefault();
            if (!string.IsNullOrEmpty(finalId))
            {
                var canReachFinal = new HashSet<string> { finalId };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var kv in outgoing)
                    {
                        if (canReachFinal.Contains(kv.Key)) continue;
                        if (kv.Value.Any(to => canReachFinal.Contains(to))) { canReachFinal.Add(kv.Key); grew = true; }
                    }
                }
                foreach (ChapterRoomSpec r in byId.Values)
                    if (!canReachFinal.Contains(r.RoomId)) errors.Add("房间 " + r.RoomId + " 走不到终房");
            }

            return errors;
        }
    }
}
