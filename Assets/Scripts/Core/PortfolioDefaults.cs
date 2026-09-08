namespace FallenAngel.Core
{
    /// <summary>成长流程的固定约定值。改动等于改存档兼容性，请谨慎。</summary>
    public static class PortfolioDefaults
    {
        /// <summary>新游戏固定覆盖槽的 profileId（合法 Guid N 格式 32 位 hex）。</summary>
        public const string DefaultProfileId = "00000000000000000000000000000001";

        /// <summary>局内装备持有上限（equipment_base_v3 约定：最多 20 件，满容量停掉落禁购买）</summary>
        public const int EquipmentCapacity = 20;
    }
}
