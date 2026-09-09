using System;
using System.Collections.Generic;

namespace FallenAngel.Data
{
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
        public List<string> visitedNodeIds = new List<string>();
        public int runCash;
        public int lastCashReward;
        // 本局已持有装备（随局快照持久化；局终清空；同款不重复，容量上限见 PortfolioDefaults.EquipmentCapacity）
        public List<string> heldEquipmentIds = new List<string>();
        // 当前商店展示的候选装备（进店/刷新时生成；购买即售罄移除；随局快照持久化）
        public List<string> shopCandidates = new List<string>();
    }
}
