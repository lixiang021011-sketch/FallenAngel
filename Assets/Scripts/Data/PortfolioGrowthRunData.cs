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
    }
}
