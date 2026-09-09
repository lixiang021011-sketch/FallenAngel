namespace FallenAngel.Core
{
    /// <summary>成长流程的固定约定值。改动等于改存档兼容性，请谨慎。</summary>
    public static class PortfolioDefaults
    {
        /// <summary>新游戏固定覆盖槽的 profileId（合法 Guid N 格式 32 位 hex）。</summary>
        public const string DefaultProfileId = "00000000000000000000000000000001";

        /// <summary>局内装备持有上限（equipment_base_v3 约定：最多 20 件，满容量停掉落禁购买）</summary>
        public const int EquipmentCapacity = 20;

        /// <summary>商店每次生成候选的基础数量（E10 每件+1；合法池不足时少量展示，不复制商品）</summary>
        public const int ShopBaseCandidateCount = 4;

        /// <summary>演奏收益封顶比例（原型值：总演奏收益 ≤ B × (1 + 此值)，与导出器 --income-cap-ratio 一致）</summary>
        public const float IncomeCapRatio = 0.5f;

        /// <summary>整曲Perfect目标奖励的上层达标线：≥此线给满额；表内 threshold（0.7）为下层线给半额。原型规则，见 talent_effects guide。</summary>
        public const double PerfectFullThreshold = 0.9;
    }
}
