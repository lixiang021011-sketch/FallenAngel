using System;
using System.Collections.Generic;

namespace FallenAngel.Data
{
    /// <summary>长期存档数据；只通过服务提交副本，公开字段用于JsonUtility序列化。</summary>
    [Serializable]
    public sealed class PortfolioProfileData
    {
        public int version = 1;
        public string profileId;
        public string displayName;
        public int revision;
        public int growthPoints;
        public List<string> unlockedNodeIds = new List<string>();
        // 当前步骤预留局内事务的占用标记，不冒充完整的续玩快照。
        public string activeRunId;
        public string growthRunJson;

        /// <summary>获取隔离副本，避免调用方修改已读取的长期状态。</summary>
        public PortfolioProfileData Copy()
        {
            return new PortfolioProfileData
            {
                version = version, profileId = profileId, displayName = displayName,
                revision = revision, growthPoints = growthPoints, activeRunId = activeRunId,
                growthRunJson = growthRunJson,
                unlockedNodeIds = unlockedNodeIds == null ? null : new List<string>(unlockedNodeIds)
            };
        }
    }
}
