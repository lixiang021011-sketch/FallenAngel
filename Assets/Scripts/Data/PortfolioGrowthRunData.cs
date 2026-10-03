using System;
using System.Collections.Generic;

namespace FallenAngel.Data
{
    /// <summary>收益结算明细行（随局快照序列化，RESULT 界面展示）</summary>
    [Serializable]
    public sealed class IncomeLineData
    {
        public string key;    // 文案 key（income.* 后缀）
        public double amount; // 金额
        public string kind;   // DIRECT / BONUS / FLOOR / CAP / ECONOMY
    }

    /// <summary>成长验证路线的局内快照；奖励/失败阈值在开局冻结，防止中途改表影响已开始的一局。</summary>
    [Serializable]
    public sealed class PortfolioGrowthRunData
    {
        public int version = 1;
        public string runId;
        public string phase = "READY";
        public string outcome;
        public List<string> stageIds = new List<string>();
        public List<int> growthRewards = new List<int>();
        public int completedSongs;
        public int earnedPoints;
        public int lastSongPoints;
        public int creditedPoints;
        public int completionBonus;
        public int failureLimit;
        public int lastScore;
        public float lastAccuracy;
        public bool useMap;
        public string currentNodeId;
        /// <summary>当前所在层（1–3；0 = 旧档或非地图局）。房间内容的按层过滤、每层状态与旅伴作用域都读它。</summary>
        public int layer = 1;
        public List<string> visitedNodeIds = new List<string>();
        public int runCash;
        public int lastCashReward;
        // 本局已持有装备（随局快照持久化；局终清空；同款不重复，容量上限见 PortfolioDefaults.EquipmentCapacity）
        public List<string> heldEquipmentIds = new List<string>();
        // 当前商店展示的候选装备（进店/刷新时生成；购买即售罄移除；随局快照持久化）
        public List<string> shopCandidates = new List<string>();
        // 本局整批刷新预算（开局由 E0/E1 冻结发放；刷新无变化不扣；局终清空）
        public int shopRefreshBudget;
        // 开曲时现金快照（C1 利息基数；不包含本曲尚未到账奖励）
        public double openingCash;
        // 最近一次成功演奏的收益明细（RESULT 界面展示；随局快照持久化）
        public List<IncomeLineData> incomeBreakdown = new List<IncomeLineData>();
        // 最近一次成功演奏的关卡掉落（RESULT 界面展示；无掉落保持 false）
        public bool lastDropGranted;
        public string lastDropEntryId;   // drop_entries.entry_id
        public string lastDropRewardType; // EQUIPMENT / CURRENCY
        public string lastDropRewardId;   // E01…E10 / RUN_CASH
        public int lastDropQuantity;
        /// <summary>K1 可选购买减免已使用次数（成功购买才 +1）</summary>
        public int optionalPurchaseDiscountUsed;
        /// <summary>E08 可选路费减免已使用次数（成功进房才 +1）</summary>
        public int optionalRouteDiscountUsed;
        /// <summary>
        /// 本局已经结算过的房间选项，存 "节点ID|选项ID"。
        /// 幂等键：同一个选项重复提交只返回原结果，不重复发奖（关掉游戏重进同一间房不会白拿第二份）。
        /// </summary>
        public List<string> resolvedRoomChoices = new List<string>();
    }
}
