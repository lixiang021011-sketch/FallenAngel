// Generated from validated portfolio tables. DO NOT EDIT.
// Regenerate with 02_development/generate_unity_config.py.
using System.Collections.ObjectModel;
namespace FallenAngel.Data
{
    /// <summary>只读配置行：equipment_base。</summary>
    public sealed class EquipmentBaseRow
    {
        public string EquipmentId { get; }
        public string EffectId { get; }
        public int BasePrice { get; }
        public bool AllowDuplicate { get; }
        public bool Enabled { get; }
        public string Description { get; }
        public EquipmentBaseRow(string equipment_id, string effect_id, int base_price, bool allow_duplicate, bool enabled, string description)
        {
            EquipmentId = equipment_id;
            EffectId = effect_id;
            BasePrice = base_price;
            AllowDuplicate = allow_duplicate;
            Enabled = enabled;
            Description = description;
        }
    }
    /// <summary>只读配置行：equipment_effects。</summary>
    public sealed class EquipmentEffectsRow
    {
        public string EffectId { get; }
        public string Handler { get; }
        public string TriggerEvent { get; }
        public string ConditionMetric { get; }
        public string CompareOp { get; }
        public double? Threshold { get; }
        public string ValueBase { get; }
        public double Coefficient { get; }
        public double? ValueCapB { get; }
        public int? LimitCount { get; }
        public string LimitScope { get; }
        public string StackRule { get; }
        public string TargetScope { get; }
        public string IncomeBucket { get; }
        public bool Enabled { get; }
        public EquipmentEffectsRow(string effect_id, string handler, string trigger_event, string condition_metric, string compare_op, double? threshold, string value_base, double coefficient, double? value_cap_b, int? limit_count, string limit_scope, string stack_rule, string target_scope, string income_bucket, bool enabled)
        {
            EffectId = effect_id;
            Handler = handler;
            TriggerEvent = trigger_event;
            ConditionMetric = condition_metric;
            CompareOp = compare_op;
            Threshold = threshold;
            ValueBase = value_base;
            Coefficient = coefficient;
            ValueCapB = value_cap_b;
            LimitCount = limit_count;
            LimitScope = limit_scope;
            StackRule = stack_rule;
            TargetScope = target_scope;
            IncomeBucket = income_bucket;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：map_nodes。</summary>
    public sealed class MapNodesRow
    {
        public string NodeId { get; }
        public string NodeType { get; }
        public string StageId { get; }
        public int LayoutX { get; }
        public int LayoutY { get; }
        /// <summary>房间内容 id（RoomContentCatalog 的行；演奏/硬仗/商店为空 = 走各自的老逻辑）。</summary>
        public string ContentId { get; }
        /// <summary>浓雾节点：变成可达之前连类型都不显示（集成战略的 Dense Fog，见 docs/layer-ui-spec.md §5.2）。</summary>
        public bool Fog { get; }
        public MapNodesRow(string node_id, string node_type, string stage_id, int layout_x, int layout_y,
            string content_id = null, bool fog = false)
        {
            NodeId = node_id;
            NodeType = node_type;
            StageId = stage_id;
            LayoutX = layout_x;
            LayoutY = layout_y;
            ContentId = content_id;
            Fog = fog;
        }
    }
    /// <summary>只读配置行：map_edges。</summary>
    public sealed class MapEdgesRow
    {
        public string EdgeId { get; }
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public int RoutePrice { get; }
        public MapEdgesRow(string edge_id, string from_node_id, string to_node_id, int route_price)
        {
            EdgeId = edge_id;
            FromNodeId = from_node_id;
            ToNodeId = to_node_id;
            RoutePrice = route_price;
        }
    }
    /// <summary>只读配置行：shop_candidates。</summary>
    public sealed class ShopCandidatesRow
    {
        public string EntryId { get; }
        public string EquipmentId { get; }
        public int Weight { get; }
        public bool Enabled { get; }
        public ShopCandidatesRow(string entry_id, string equipment_id, int weight, bool enabled)
        {
            EntryId = entry_id;
            EquipmentId = equipment_id;
            Weight = weight;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：stages。</summary>
    public sealed class StagesRow
    {
        public string StageId { get; }
        public string Name { get; }
        public string StageType { get; }
        public string ChartId { get; }
        public int BaseIncome { get; }
        public int GrowthScore { get; }
        public string DropRuleId { get; }
        public bool Enabled { get; }
        public StagesRow(string stage_id, string name, string stage_type, string chart_id, int base_income, int growth_score, string drop_rule_id, bool enabled)
        {
            StageId = stage_id;
            Name = name;
            StageType = stage_type;
            ChartId = chart_id;
            BaseIncome = base_income;
            GrowthScore = growth_score;
            DropRuleId = drop_rule_id;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：chart_bindings。</summary>
    public sealed class ChartBindingsRow
    {
        public string ChartId { get; }
        public string ResourceName { get; }
        public bool IsPlaceholder { get; }
        public ChartBindingsRow(string chart_id, string resource_name, bool is_placeholder)
        {
            ChartId = chart_id;
            ResourceName = resource_name;
            IsPlaceholder = is_placeholder;
        }
    }
    /// <summary>只读配置行：drop_rules。</summary>
    public sealed class DropRulesRow
    {
        public string RuleId { get; }
        public string PoolId { get; }
        public string TriggerEvent { get; }
        public double DropChance { get; }
        public int MaxRewards { get; }
        public bool Enabled { get; }
        public DropRulesRow(string rule_id, string pool_id, string trigger_event, double drop_chance, int max_rewards, bool enabled)
        {
            RuleId = rule_id;
            PoolId = pool_id;
            TriggerEvent = trigger_event;
            DropChance = drop_chance;
            MaxRewards = max_rewards;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：drop_entries。</summary>
    public sealed class DropEntriesRow
    {
        public string EntryId { get; }
        public string PoolId { get; }
        public string RewardType { get; }
        public string RewardId { get; }
        public int Weight { get; }
        public int Quantity { get; }
        public bool ExcludeOwned { get; }
        public bool Enabled { get; }
        public DropEntriesRow(string entry_id, string pool_id, string reward_type, string reward_id, int weight, int quantity, bool exclude_owned, bool enabled)
        {
            EntryId = entry_id;
            PoolId = pool_id;
            RewardType = reward_type;
            RewardId = reward_id;
            Weight = weight;
            Quantity = quantity;
            ExcludeOwned = exclude_owned;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：talent_effects。</summary>
    public sealed class TalentEffectsRow
    {
        public string EffectId { get; }
        public string Handler { get; }
        public string TriggerEvent { get; }
        public string ConditionMetric { get; }
        public string CompareOp { get; }
        public double? Threshold { get; }
        public string ValueBase { get; }
        public double Coefficient { get; }
        public double? ValueCapB { get; }
        public int? LimitCount { get; }
        public string LimitScope { get; }
        public string StackRule { get; }
        public string TargetScope { get; }
        public string TargetEffectId { get; }
        public string IncomeBucket { get; }
        public bool Enabled { get; }
        public TalentEffectsRow(string effect_id, string handler, string trigger_event, string condition_metric, string compare_op, double? threshold, string value_base, double coefficient, double? value_cap_b, int? limit_count, string limit_scope, string stack_rule, string target_scope, string target_effect_id, string income_bucket, bool enabled)
        {
            EffectId = effect_id;
            Handler = handler;
            TriggerEvent = trigger_event;
            ConditionMetric = condition_metric;
            CompareOp = compare_op;
            Threshold = threshold;
            ValueBase = value_base;
            Coefficient = coefficient;
            ValueCapB = value_cap_b;
            LimitCount = limit_count;
            LimitScope = limit_scope;
            StackRule = stack_rule;
            TargetScope = target_scope;
            TargetEffectId = target_effect_id;
            IncomeBucket = income_bucket;
            Enabled = enabled;
        }
    }
    /// <summary>只读配置行：talent_nodes。</summary>
    public sealed class TalentNodesRow
    {
        public string NodeId { get; }
        public string Name { get; }
        public string NodeType { get; }
        public int Tier { get; }
        public int UnlockCost { get; }
        public string EffectId { get; }
        public string PrerequisiteMode { get; }
        public TalentNodesRow(string node_id, string name, string node_type, int tier, int unlock_cost, string effect_id, string prerequisite_mode)
        {
            NodeId = node_id;
            Name = name;
            NodeType = node_type;
            Tier = tier;
            UnlockCost = unlock_cost;
            EffectId = effect_id;
            PrerequisiteMode = prerequisite_mode;
        }
    }
    /// <summary>只读配置行：talent_edges。</summary>
    public sealed class TalentEdgesRow
    {
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public TalentEdgesRow(string from_node_id, string to_node_id)
        {
            FromNodeId = from_node_id;
            ToNodeId = to_node_id;
        }
    }
    /// <summary>全部12张表的只读构建快照；空值与0/false保持不同。</summary>
    public static class PortfolioConfig
    {
        public const string ManifestSha256 = "e6ae2919b3417904d024465646ad983fdf5dd8778f126927868d9638a12bd3c1";
        public const string SchemaVersion = "3.0";
        public static ReadOnlyCollection<EquipmentBaseRow> EquipmentBase { get; } =
            System.Array.AsReadOnly(new EquipmentBaseRow[]
        {
            new EquipmentBaseRow("E01", "EQ_E01", 60, false, true, "\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u7ed3\u7b97\uff1a\u6bcf\u4e2a\u6709\u6548\u4e50\u53e5Perfect\u7387\u8fbe\u523090%\uff0c\u83b7\u5f970.03B\u6f14\u594f\u989d\u5916\u6536\u76ca\u3002"),
            new EquipmentBaseRow("E02", "EQ_E02", 60, false, true, "\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u7ed3\u7b97\uff1a\u6bcf\u4e2a\u5b8c\u6574\u4e14\u4e0d\u65ad\u8fde\u7684\u6709\u6548\u4e50\u53e5\u83b7\u5f970.03B\u6f14\u594f\u989d\u5916\u6536\u76ca\u3002"),
            new EquipmentBaseRow("E03", "EQ_E03", 40, false, true, "\u672c\u66f2\u9996\u6b21\u65ad\u8fde\u540e\uff0c\u4e4b\u540e\u9996\u4e2a\u5b8c\u6574\u4e14\u4e0d\u65ad\u8fde\u7684\u6709\u6548\u4e50\u53e5\u5956\u52b10.02B\uff0c\u6bcf\u66f2\u4e00\u6b21\uff1b\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u7ed3\u7b97\u3002"),
            new EquipmentBaseRow("E04", "EQ_E04", 50, false, true, "\u6709\u6548\u4e50\u53e5\u4e2d\u6240\u6709\u5e94\u7ed3\u675f\u7684\u957f\u6309\u5c3e\u8fbe\u5230Great\u53ca\u4ee5\u4e0a\uff0c\u5956\u52b10.04B\uff1b\u65e0\u957f\u6309\u5c3e\u4e0d\u89e6\u53d1\uff0c\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u7ed3\u7b97\u3002"),
            new EquipmentBaseRow("E05", "EQ_E05", 60, false, true, "\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u4e14\u65e0Miss\uff0c\u6f14\u594f\u76f4\u63a5\u5956\u52b1\u53ca\u8865\u507f\u589e\u52a025%\uff0c\u4e0e\u9002\u7528\u589e\u5e45\u52a0\u7b97\uff1b\u4e0d\u5305\u542b\u5229\u606f\u3001\u6311\u6218\u5956\u91d1\u3001\u8def\u8d39\u8fd4\u8fd8\u53ca\u4fdd\u5e95\u3002"),
            new EquipmentBaseRow("E06", "EQ_E06", 50, false, true, "\u6bcf\u66f2\u9996\u6b21\u5408\u683c\u7684\u6f14\u594f\u5956\u52b1\u5931\u8d25\u6279\u6b21\uff0c\u8865\u507f\u5176\u4e2d\u539f\u59cb\u91d1\u989d\u6700\u9ad8\u4e00\u9879\u768450%\uff0c\u539f\u59cb\u8865\u507f\u6700\u591a0.03B\uff0c\u6bcf\u66f2\u4e00\u6b21\uff1b\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u7ed3\u7b97\u3002"),
            new EquipmentBaseRow("E07", "EQ_E07", 60, false, true, "\u88c5\u5907\u8d2d\u4e70\u4ef7\u683c\u964d\u4f4e15%\uff0c\u4e0e\u5176\u4ed6\u8d2d\u4e70\u6298\u6263\u53d6\u6700\u5927\u503c\uff1b\u4e0d\u5bf9\u4e70\u5165\u672c\u88c5\u5907\u7684\u5f53\u6b21\u4ea4\u6613\u751f\u6548\u3002"),
            new EquipmentBaseRow("E08", "EQ_E08", 60, false, true, "\u53ef\u4e3b\u52a8\u9009\u62e9\u51cf\u514d20%\u8def\u8d39\uff0c\u83b7\u5f97\u65f6\u63d0\u4f9b\u672c\u5c402\u6b21\u673a\u4f1a\uff1b\u6210\u529f\u652f\u4ed8\u5e76\u8fdb\u5165\u624d\u6d88\u8017\uff0c\u4e0e\u5929\u8d4b\u6298\u6263\u53d6\u6700\u5927\u503c\u3002"),
            new EquipmentBaseRow("E09", "EQ_E09", 60, false, true, "\u6b63\u5e38\u5b8c\u6210\u6b4c\u66f2\u540e\u83b7\u5f97\u5f00\u66f2\u73b0\u91d1\u768410%\uff0c\u6700\u591a0.05B\uff1b\u5229\u606f\u4e0d\u53c2\u4e0e\u6f14\u594f\u6536\u76ca\u500d\u7387\u3001\u4fdd\u5e95\u53ca\u6f14\u594f\u6536\u76ca\u4e0a\u9650\u3002"),
            new EquipmentBaseRow("E10", "EQ_E10", 40, false, true, "\u4e0b\u6b21\u5237\u65b0\u6216\u8fdb\u5165\u65b0\u5546\u5e97\u751f\u6210\u5546\u54c1\u65f6\uff0c\u5019\u9009\u6570\u91cf\u589e\u52a01\uff1b\u83b7\u5f97\u65f6\u4e0d\u7acb\u5373\u8865\u8d27\uff0c\u8bfb\u53d6\u5b58\u6863\u4e0d\u91cd\u590d\u751f\u6210\u5546\u54c1\u3002")
        });
        public static ReadOnlyCollection<EquipmentEffectsRow> EquipmentEffects { get; } =
            System.Array.AsReadOnly(new EquipmentEffectsRow[]
        {
            new EquipmentEffectsRow("EQ_E01", "threshold_reward", "PHRASE_END", "PERFECT_RATE", "GE", 0.9d, "SONG_BASE_INCOME", 0.03d, null, 1, "PHRASE", "ADD", "SELF", "PERFORMANCE_DIRECT", true),
            new EquipmentEffectsRow("EQ_E02", "unbroken_reward", "PHRASE_END", "COMBO_BREAK_COUNT", "EQ", 0.0d, "SONG_BASE_INCOME", 0.03d, null, 1, "PHRASE", "ADD", "SELF", "PERFORMANCE_DIRECT", true),
            new EquipmentEffectsRow("EQ_E03", "recovery_reward", "PHRASE_END", "COMBO_BREAK_COUNT", "EQ", 0.0d, "SONG_BASE_INCOME", 0.02d, null, 1, "SONG", "ADD", "SELF", "PERFORMANCE_DIRECT", true),
            new EquipmentEffectsRow("EQ_E04", "hold_reward", "PHRASE_END", "HOLD_TAIL_SUCCESS_RATE", "GE", 1.0d, "SONG_BASE_INCOME", 0.04d, null, 1, "PHRASE", "ADD", "SELF", "PERFORMANCE_DIRECT", true),
            new EquipmentEffectsRow("EQ_E05", "no_miss_multiplier", "SONG_SETTLE", "MISS_COUNT", "EQ", 0.0d, "RAW_REWARD", 0.25d, null, 1, "SONG", "ADD_BONUS", "PERFORMANCE_WITH_COMPENSATION", "NONE", true),
            new EquipmentEffectsRow("EQ_E06", "failed_reward_compensation", "DIRECT_REWARD_FAIL_BATCH", null, null, null, "FAILED_RAW_DIRECT_REWARD", 0.5d, 0.03d, 1, "SONG", "ADD", "ELIGIBLE_FAILED_DIRECT", "PERFORMANCE_COMPENSATION", true),
            new EquipmentEffectsRow("EQ_E07", "purchase_discount", "PURCHASE_QUOTE", null, null, null, "EQUIPMENT_PRICE", 0.15d, null, null, "NONE", "MAX_DISCOUNT", "EQUIPMENT_PURCHASE", "NONE", true),
            new EquipmentEffectsRow("EQ_E08", "optional_route_discount", "ROUTE_QUOTE", null, null, null, "ROUTE_PRICE", 0.2d, null, 2, "RUN", "MAX_DISCOUNT", "PAID_ROUTE", "NONE", true),
            new EquipmentEffectsRow("EQ_E09", "opening_balance_interest", "SONG_SETTLE", null, null, null, "OPENING_CASH", 0.1d, 0.05d, 1, "SONG", "ADD", "SELF", "ECONOMY", true),
            new EquipmentEffectsRow("EQ_E10", "extra_shop_candidate", "SHOP_CANDIDATES_BUILD", null, null, null, "CANDIDATE_COUNT", 1.0d, null, null, "NONE", "ADD_COUNT", "SHOP_CANDIDATES", "NONE", true)
        });
        public static ReadOnlyCollection<MapNodesRow> MapNodes { get; } =
            System.Array.AsReadOnly(new MapNodesRow[]
        {
            // 第一章（千禧梦核）路线：**一局 16 站（起点 + 15 步）**，每步 1–3 个候选、玩家自己挑。
            // 7 个演奏步 + 7 个资源步 + 终房；一局演奏数恒 7，硬仗 0–3（每层一个候选位）。
            // 与 Core/ChapterCatalog（房间表 + 连线表）一一对应，ChapterChecks 会核对防漂移。
            // LayoutY = 步号；LayoutX = 行内列（左 0 / 中 1 / 右 2 映射成 -1 / 0 / 1）。
            new MapNodesRow("R00", "START", null, 0, 0),

            // ── 第 1 层（步 1–4）──
            new MapNodesRow("R1A", "STAGE", "S01", -1, 1),
            new MapNodesRow("R2A", "EVENT", null, -1, 2, "RC_VISITED", true),
            new MapNodesRow("R2B", "SHOP", null, 1, 2),
            new MapNodesRow("R3A", "STAGE", "S02", -1, 3),
            new MapNodesRow("R3B", "HARD", "H01", 1, 3),
            new MapNodesRow("R4A", "SHOP", null, -1, 4),
            new MapNodesRow("R4B", "EVENT", null, 1, 4, "RC_EMPTY"),

            // ── 第 2 层（步 5–9）──
            new MapNodesRow("R5A", "STAGE", "S03", -1, 5),   // 器材向
            new MapNodesRow("R5B", "STAGE", "S04", 1, 5),    // 钱向
            new MapNodesRow("R6A", "EVENT", null, -1, 6, "RC_VISITED", true),
            new MapNodesRow("R6B", "SHOP", null, 1, 6),
            new MapNodesRow("R7A", "STAGE", "S05", -1, 7),   // 器材向
            new MapNodesRow("R7B", "STAGE", "S06", 1, 7),    // 钱向
            new MapNodesRow("R8A", "REST", null, -1, 8, "RC_REST"),
            new MapNodesRow("R8B", "EVENT", null, 1, 8, "RC_EMPTY"),
            new MapNodesRow("R9A", "STAGE", "S07", -1, 9),
            // 第 2 层末：先放"馈赠占位房"（作者口径）；硬仗候选因此只留第 1、3 层各一处。
            new MapNodesRow("R9B", "EVENT", null, 1, 9, "RC_GIFT"),

            // ── 第 3 层（步 10–15）──
            new MapNodesRow("R10A", "SHOP", null, -1, 10),
            new MapNodesRow("R10B", "REST", null, 1, 10, "RC_REST"),
            new MapNodesRow("R11A", "STAGE", "S08", -1, 11), // 器材向
            new MapNodesRow("R11B", "STAGE", "S09", 1, 11),  // 钱向
            new MapNodesRow("R12A", "EVENT", null, -1, 12, "RC_EMPTY"),
            new MapNodesRow("R12B", "REST", null, 1, 12, "RC_REST"),
            new MapNodesRow("R13A", "STAGE", "S10", -1, 13),
            new MapNodesRow("R13B", "HARD", "H03", 1, 13),
            new MapNodesRow("R14A", "SHOP", null, -1, 14),
            new MapNodesRow("R14B", "EVENT", null, 1, 14, "RC_EMPTY"),
            new MapNodesRow("R15", "FINAL", null, 0, 15, "RC_END")
        });
        public static ReadOnlyCollection<MapEdgesRow> MapEdges { get; } =
            System.Array.AsReadOnly(new MapEdgesRow[]
        {
            // 只连"上一步 → 下一步"，列差 ≤ 1（地图不交叉）。有些走法下一步只有一个候选
            //（例：R3B 硬仗之后只能去 R4B）——那是"选择要付的代价"，写在 docs/chapter-route-spec.md。
            // 路费：现在全部为 0；分支之后最合适的收法是"只收在付费捷径上"（同一步总有免费替代，不会卡死），
            // 等这条规则拍板再填数。
            new MapEdgesRow("E001", "R00", "R1A", 0),
            new MapEdgesRow("E002", "R1A", "R2A", 0),
            new MapEdgesRow("E003", "R1A", "R2B", 0),
            new MapEdgesRow("E004", "R2A", "R3A", 0),
            new MapEdgesRow("E005", "R2A", "R3B", 0),
            new MapEdgesRow("E006", "R2B", "R3A", 0),
            new MapEdgesRow("E007", "R2B", "R3B", 0),
            new MapEdgesRow("E008", "R3A", "R4A", 0),
            new MapEdgesRow("E009", "R3A", "R4B", 0),
            new MapEdgesRow("E010", "R3B", "R4B", 0),
            new MapEdgesRow("E011", "R4A", "R5A", 0),
            new MapEdgesRow("E012", "R4A", "R5B", 0),
            new MapEdgesRow("E013", "R4B", "R5A", 0),
            new MapEdgesRow("E014", "R4B", "R5B", 0),
            new MapEdgesRow("E015", "R5A", "R6A", 0),
            new MapEdgesRow("E016", "R5A", "R6B", 0),
            new MapEdgesRow("E017", "R5B", "R6B", 0),
            new MapEdgesRow("E018", "R6A", "R7A", 0),
            new MapEdgesRow("E019", "R6A", "R7B", 0),
            new MapEdgesRow("E020", "R6B", "R7A", 0),
            new MapEdgesRow("E021", "R6B", "R7B", 0),
            new MapEdgesRow("E022", "R7A", "R8A", 0),
            new MapEdgesRow("E023", "R7A", "R8B", 0),
            new MapEdgesRow("E024", "R7B", "R8B", 0),
            new MapEdgesRow("E025", "R8A", "R9A", 0),
            new MapEdgesRow("E026", "R8A", "R9B", 0),
            new MapEdgesRow("E027", "R8B", "R9A", 0),
            new MapEdgesRow("E028", "R8B", "R9B", 0),
            new MapEdgesRow("E029", "R9A", "R10A", 0),
            new MapEdgesRow("E030", "R9A", "R10B", 0),
            new MapEdgesRow("E031", "R9B", "R10B", 0),
            new MapEdgesRow("E032", "R10A", "R11A", 0),
            new MapEdgesRow("E033", "R10A", "R11B", 0),
            new MapEdgesRow("E034", "R10B", "R11A", 0),
            new MapEdgesRow("E035", "R10B", "R11B", 0),
            new MapEdgesRow("E036", "R11A", "R12A", 0),
            new MapEdgesRow("E037", "R11A", "R12B", 0),
            new MapEdgesRow("E038", "R11B", "R12B", 0),
            new MapEdgesRow("E039", "R12A", "R13A", 0),
            new MapEdgesRow("E040", "R12A", "R13B", 0),
            new MapEdgesRow("E041", "R12B", "R13A", 0),
            new MapEdgesRow("E042", "R12B", "R13B", 0),
            new MapEdgesRow("E043", "R13A", "R14A", 0),
            new MapEdgesRow("E044", "R13A", "R14B", 0),
            new MapEdgesRow("E045", "R13B", "R14B", 0),
            new MapEdgesRow("E046", "R14A", "R15", 0),
            new MapEdgesRow("E047", "R14B", "R15", 0)
        });
        public static ReadOnlyCollection<ShopCandidatesRow> ShopCandidates { get; } =
            System.Array.AsReadOnly(new ShopCandidatesRow[]
        {
            new ShopCandidatesRow("G01", "E01", 12, true),
            new ShopCandidatesRow("G02", "E02", 12, true),
            new ShopCandidatesRow("G03", "E03", 10, true),
            new ShopCandidatesRow("G04", "E04", 10, true),
            new ShopCandidatesRow("G05", "E05", 8, true),
            new ShopCandidatesRow("G06", "E06", 10, true),
            new ShopCandidatesRow("G07", "E07", 8, true),
            new ShopCandidatesRow("G08", "E08", 8, true),
            new ShopCandidatesRow("G09", "E09", 8, true),
            new ShopCandidatesRow("G10", "E10", 14, true)
        });
        public static ReadOnlyCollection<StagesRow> Stages { get; } =
            System.Array.AsReadOnly(new StagesRow[]
        {
            // 普通演奏 100/100；**器材向** 80 收益但掉率 50%（DP_NORMAL）；**钱向** 130 收益、掉率 20%（DP_CASH）。
            // 硬仗 150/150 + DP_CHALLENGE。一局固定 7 场（见 Core/ChapterCatalog）。
            new StagesRow("S01", "\u666e\u901a\u6f14\u594f\uff08\u5171\u7528\u9f13\u8c31\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 100, 100, "DP_NORMAL", true),
            new StagesRow("S02", "\u666e\u901a\u6f14\u594f\uff08\u5171\u7528\u9f13\u8c31\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 100, 100, "DP_NORMAL", true),
            new StagesRow("S03", "\u666e\u901a\u6f14\u594f\uff08\u504f\u5668\u6750\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 80, 100, "DP_NORMAL", true),
            new StagesRow("S04", "\u666e\u901a\u6f14\u594f\uff08\u504f\u73b0\u91d1\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 130, 100, "DP_CASH", true),
            new StagesRow("S05", "\u666e\u901a\u6f14\u594f\uff08\u504f\u5668\u6750\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 80, 100, "DP_NORMAL", true),
            new StagesRow("S06", "\u666e\u901a\u6f14\u594f\uff08\u504f\u73b0\u91d1\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 130, 100, "DP_CASH", true),
            new StagesRow("S07", "\u666e\u901a\u6f14\u594f\uff08\u5171\u7528\u9f13\u8c31\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 100, 100, "DP_NORMAL", true),
            new StagesRow("S08", "\u666e\u901a\u6f14\u594f\uff08\u504f\u5668\u6750\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 80, 100, "DP_NORMAL", true),
            new StagesRow("S09", "\u666e\u901a\u6f14\u594f\uff08\u504f\u73b0\u91d1\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 130, 100, "DP_CASH", true),
            new StagesRow("S10", "\u666e\u901a\u6f14\u594f\uff08\u5171\u7528\u9f13\u8c31\uff09", "NORMAL", "CHART_DRUMS_PLACEHOLDER", 100, 100, "DP_NORMAL", true),
            new StagesRow("H01", "\u786c\u4ed7\uff08\u5171\u7528\u9f13\u8c31\uff09", "HARD", "CHART_DRUMS_PLACEHOLDER", 150, 150, "DP_CHALLENGE", true),
            new StagesRow("H03", "\u786c\u4ed7\uff08\u5171\u7528\u9f13\u8c31\uff09", "HARD", "CHART_DRUMS_PLACEHOLDER", 150, 150, "DP_CHALLENGE", true)
        });
        public static ReadOnlyCollection<ChartBindingsRow> ChartBindings { get; } =
            System.Array.AsReadOnly(new ChartBindingsRow[]
        {
            new ChartBindingsRow("CHART_DRUMS_PLACEHOLDER", "test_chart_5k", true)
        });
        public static ReadOnlyCollection<DropRulesRow> DropRules { get; } =
            System.Array.AsReadOnly(new DropRulesRow[]
        {
            new DropRulesRow("DP_NORMAL", "POOL_STAGE_COMMON", "STAGE_COMPLETED", 0.5d, 1, true),
            new DropRulesRow("DP_CHALLENGE", "POOL_STAGE_COMMON", "STAGE_COMPLETED", 0.75d, 1, true),
            // 钱向演奏房：掉率低、现金高——"钱还是器材"这个选择就落在这一行上。
            new DropRulesRow("DP_CASH", "POOL_STAGE_COMMON", "STAGE_COMPLETED", 0.2d, 1, true)
        });
        public static ReadOnlyCollection<DropEntriesRow> DropEntries { get; } =
            System.Array.AsReadOnly(new DropEntriesRow[]
        {
            new DropEntriesRow("DE001", "POOL_STAGE_COMMON", "EQUIPMENT", "E01", 15, 1, true, true),
            new DropEntriesRow("DE002", "POOL_STAGE_COMMON", "EQUIPMENT", "E02", 15, 1, true, true),
            new DropEntriesRow("DE003", "POOL_STAGE_COMMON", "EQUIPMENT", "E03", 10, 1, true, true),
            new DropEntriesRow("DE004", "POOL_STAGE_COMMON", "EQUIPMENT", "E04", 10, 1, true, true),
            new DropEntriesRow("DE005", "POOL_STAGE_COMMON", "EQUIPMENT", "E05", 5, 1, true, true),
            new DropEntriesRow("DE006", "POOL_STAGE_COMMON", "EQUIPMENT", "E06", 10, 1, true, true),
            new DropEntriesRow("DE007", "POOL_STAGE_COMMON", "EQUIPMENT", "E07", 10, 1, true, true),
            new DropEntriesRow("DE008", "POOL_STAGE_COMMON", "EQUIPMENT", "E08", 10, 1, true, true),
            new DropEntriesRow("DE009", "POOL_STAGE_COMMON", "EQUIPMENT", "E09", 10, 1, true, true),
            new DropEntriesRow("DE010", "POOL_STAGE_COMMON", "EQUIPMENT", "E10", 5, 1, true, true),
            new DropEntriesRow("DE011", "POOL_STAGE_COMMON", "CURRENCY", "RUN_CASH", 10, 30, false, false)
        });
        public static ReadOnlyCollection<TalentEffectsRow> TalentEffects { get; } =
            System.Array.AsReadOnly(new TalentEffectsRow[]
        {
            new TalentEffectsRow("FX_A0", "threshold_reward", "PHRASE_END", "PERFECT_RATE", "GE", 0.9d, "SONG_BASE_INCOME", 0.025d, null, 1, "PHRASE", "ADD", "SELF", null, "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_A1", "streak_reward", "PHRASE_END", "QUALIFIED_PHRASE_STREAK", "GE", 2.0d, "SONG_BASE_INCOME", 0.02d, null, null, "NONE", "ADD", "SELF", "FX_A0", "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_B0", "unbroken_reward", "PHRASE_END", "COMBO_BREAK_COUNT", "EQ", 0.0d, "SONG_BASE_INCOME", 0.02d, null, 1, "PHRASE", "ADD", "SELF", null, "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_B1", "streak_reward", "PHRASE_END", "QUALIFIED_PHRASE_STREAK", "GE", 2.0d, "SONG_BASE_INCOME", 0.015d, null, null, "NONE", "ADD", "SELF", "FX_B0", "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_C0", "completion_reward", "PHRASE_END", null, null, null, "SONG_BASE_INCOME", 0.015d, null, 1, "PHRASE", "ADD", "SELF", null, "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_C1", "opening_balance_interest", "SONG_SETTLE", null, null, null, "OPENING_CASH", 0.05d, 0.03d, 1, "SONG", "ADD", "SELF", null, "ECONOMY", true),
            new TalentEffectsRow("FX_D0", "source_bonus", "SONG_SETTLE", null, null, null, "RAW_REWARD", 0.1d, null, null, "NONE", "ADD_BONUS", "PERFORMANCE_WITH_COMPENSATION", null, "NONE", true),
            new TalentEffectsRow("FX_D1", "purchase_discount", "PURCHASE_QUOTE", null, null, null, "EQUIPMENT_PRICE", 0.1d, null, null, "NONE", "MAX_DISCOUNT", "EQUIPMENT_PURCHASE", null, "NONE", true),
            new TalentEffectsRow("FX_E0", "grant_refresh_budget", "RUN_START", null, null, null, "UNIT_COUNT", 1.0d, null, 1, "RUN", "ADD", "SHOP_REFRESH", null, "NONE", true),
            new TalentEffectsRow("FX_E1", "modify_coefficient", "RUN_START", null, null, null, "TARGET_COEFFICIENT", 1.0d, null, null, "NONE", "ADD_PARAMETER", "TARGET_EFFECT", "FX_E0", "NONE", true),
            new TalentEffectsRow("FX_F0", "route_discount", "ROUTE_QUOTE", null, null, null, "ROUTE_PRICE", 0.05d, null, null, "NONE", "MAX_DISCOUNT", "PAID_ROUTE", null, "NONE", true),
            new TalentEffectsRow("FX_F1", "modify_coefficient", "RUN_START", null, null, null, "TARGET_COEFFICIENT", 0.1d, null, null, "NONE", "OVERRIDE_PARAMETER", "TARGET_EFFECT", "FX_F0", "NONE", true),
            new TalentEffectsRow("FX_G0", "income_floor", "SONG_SETTLE", null, null, null, "SONG_BASE_INCOME", 0.08d, null, 1, "SONG", "FLOOR", "PERFORMANCE_WITH_COMPENSATION", null, "PERFORMANCE_FLOOR", true),
            new TalentEffectsRow("FX_G1", "modify_coefficient", "RUN_START", null, null, null, "TARGET_COEFFICIENT", 0.12d, null, null, "NONE", "OVERRIDE_PARAMETER", "TARGET_EFFECT", "FX_G0", "NONE", true),
            new TalentEffectsRow("FX_I0", "perfect_goal_reward", "SONG_SETTLE", "PERFECT_RATE", "GE", 0.7d, "SONG_BASE_INCOME", 0.04d, null, 1, "SONG", "ADD", "SELF", null, "PERFORMANCE_DIRECT", true),
            new TalentEffectsRow("FX_I1", "modify_coefficient", "RUN_START", null, null, null, "TARGET_COEFFICIENT", 0.06d, null, null, "NONE", "OVERRIDE_PARAMETER", "TARGET_EFFECT", "FX_I0", "NONE", true),
            new TalentEffectsRow("FX_J0", "challenge_reward", "ROUTE_COMPLETE", "IS_PAID_CHALLENGE", "EQ", 1.0d, "SONG_BASE_INCOME", 0.04d, null, 1, "SONG", "ADD", "SELF", null, "CHALLENGE", true),
            new TalentEffectsRow("FX_J1", "modify_coefficient", "RUN_START", null, null, null, "TARGET_COEFFICIENT", 0.06d, null, null, "NONE", "OVERRIDE_PARAMETER", "TARGET_EFFECT", "FX_J0", "NONE", true),
            new TalentEffectsRow("FX_K0", "source_bonus", "SONG_SETTLE", null, null, null, "RAW_REWARD", 0.5d, null, null, "NONE", "ADD_BONUS", "CORE_TALENT_DIRECT", null, "NONE", true),
            new TalentEffectsRow("FX_K1", "optional_purchase_discount", "PURCHASE_QUOTE", null, null, null, "EQUIPMENT_PRICE", 0.25d, null, 1, "RUN", "MAX_DISCOUNT", "EQUIPMENT_PURCHASE", null, "NONE", true),
            new TalentEffectsRow("FX_K2", "route_cash_refund", "ROUTE_COMPLETE", null, null, null, "ACTUAL_ROUTE_CASH_PAID", 0.2d, null, 1, "SONG", "ADD", "SELF", null, "ROUTE_REFUND", true)
        });
        public static ReadOnlyCollection<TalentNodesRow> TalentNodes { get; } =
            System.Array.AsReadOnly(new TalentNodesRow[]
        {
            new TalentNodesRow("A0", "\u7cbe\u51c6\u8fbe\u6807", "NORMAL", 1, 100, "FX_A0", "NONE"),
            new TalentNodesRow("A1", "\u8fde\u7eed\u7cbe\u51c6", "NORMAL", 1, 100, "FX_A1", "ANY"),
            new TalentNodesRow("B0", "\u8fde\u8d2f\u8fbe\u6807", "NORMAL", 1, 100, "FX_B0", "NONE"),
            new TalentNodesRow("B1", "\u8fde\u8d2f\u5ef6\u7eed", "NORMAL", 1, 100, "FX_B1", "ANY"),
            new TalentNodesRow("C0", "\u7a33\u5b9a\u5b8c\u6210", "NORMAL", 1, 100, "FX_C0", "NONE"),
            new TalentNodesRow("C1", "\u50a8\u84c4\u51c6\u5907", "NORMAL", 1, 100, "FX_C1", "ANY"),
            new TalentNodesRow("D0", "\u6f14\u594f\u589e\u76ca", "NORMAL", 2, 150, "FX_D0", "ANY"),
            new TalentNodesRow("D1", "\u5e38\u9a7b\u8d2d\u4e70\u4f18\u60e0", "NORMAL", 2, 150, "FX_D1", "ANY"),
            new TalentNodesRow("E0", "\u5546\u5e97\u5237\u65b0", "NORMAL", 2, 150, "FX_E0", "ANY"),
            new TalentNodesRow("E1", "\u8ffd\u52a0\u5237\u65b0\u6b21\u6570", "NORMAL", 2, 150, "FX_E1", "ANY"),
            new TalentNodesRow("F0", "\u5e38\u9a7b\u8def\u8d39\u51cf\u514d", "NORMAL", 2, 150, "FX_F0", "ANY"),
            new TalentNodesRow("F1", "\u5f3a\u5316\u8def\u8d39\u51cf\u514d", "NORMAL", 2, 150, "FX_F1", "ANY"),
            new TalentNodesRow("G0", "\u6f14\u594f\u6536\u76ca\u4fdd\u5e95", "NORMAL", 3, 200, "FX_G0", "ANY"),
            new TalentNodesRow("G1", "\u63d0\u9ad8\u4fdd\u5e95\u7ebf", "NORMAL", 3, 200, "FX_G1", "ANY"),
            new TalentNodesRow("I0", "\u7cbe\u51c6\u76ee\u6807\u5956\u52b1", "NORMAL", 3, 200, "FX_I0", "ANY"),
            new TalentNodesRow("I1", "\u63d0\u9ad8\u76ee\u6807\u5956\u52b1", "NORMAL", 3, 200, "FX_I1", "ANY"),
            new TalentNodesRow("J0", "\u6311\u6218\u5b8c\u6210\u5956\u52b1", "NORMAL", 3, 200, "FX_J0", "ANY"),
            new TalentNodesRow("J1", "\u63d0\u9ad8\u6311\u6218\u5956\u52b1", "NORMAL", 3, 200, "FX_J1", "ANY"),
            new TalentNodesRow("K0", "\u57fa\u7840\u6f14\u594f\u5f3a\u5316", "CAPSTONE", 4, 300, "FX_K0", "ANY"),
            new TalentNodesRow("K1", "\u53ef\u9009\u8d2d\u4e70\u4f18\u60e0", "CAPSTONE", 4, 300, "FX_K1", "ANY"),
            new TalentNodesRow("K2", "\u6311\u6218\u8def\u8d39\u8fd4\u8fd8", "CAPSTONE", 4, 300, "FX_K2", "ANY"),
            new TalentNodesRow("H1", "\u4ea4\u6c471", "JUNCTION", 1, 0, null, "ANY"),
            new TalentNodesRow("H2", "\u4ea4\u6c472", "JUNCTION", 2, 0, null, "ANY"),
            new TalentNodesRow("H3", "\u4ea4\u6c473", "JUNCTION", 3, 0, null, "ANY")
        });
        public static ReadOnlyCollection<TalentEdgesRow> TalentEdges { get; } =
            System.Array.AsReadOnly(new TalentEdgesRow[]
        {
            new TalentEdgesRow("A0", "A1"),
            new TalentEdgesRow("B0", "B1"),
            new TalentEdgesRow("C0", "C1"),
            new TalentEdgesRow("D0", "D1"),
            new TalentEdgesRow("E0", "E1"),
            new TalentEdgesRow("F0", "F1"),
            new TalentEdgesRow("G0", "G1"),
            new TalentEdgesRow("I0", "I1"),
            new TalentEdgesRow("J0", "J1"),
            new TalentEdgesRow("A1", "H1"),
            new TalentEdgesRow("B1", "H1"),
            new TalentEdgesRow("C1", "H1"),
            new TalentEdgesRow("H1", "D0"),
            new TalentEdgesRow("H1", "E0"),
            new TalentEdgesRow("H1", "F0"),
            new TalentEdgesRow("D1", "H2"),
            new TalentEdgesRow("E1", "H2"),
            new TalentEdgesRow("F1", "H2"),
            new TalentEdgesRow("H2", "G0"),
            new TalentEdgesRow("H2", "I0"),
            new TalentEdgesRow("H2", "J0"),
            new TalentEdgesRow("G1", "H3"),
            new TalentEdgesRow("I1", "H3"),
            new TalentEdgesRow("J1", "H3"),
            new TalentEdgesRow("H3", "K0"),
            new TalentEdgesRow("H3", "K1"),
            new TalentEdgesRow("H3", "K2")
        });
    }
}
