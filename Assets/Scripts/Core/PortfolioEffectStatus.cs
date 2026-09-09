namespace FallenAngel.Core
{
    /// <summary>
    /// Lite 已接线效果清单。天赋/装备 UI 用它区分「会结算」和「仅设计展示」。
    /// 乐句级（A/B/C/K0）与补偿（E06）、K2 路费返还仍未接入。
    /// </summary>
    public static class PortfolioEffectStatus
    {
        public static bool IsTalentLive(string effectId)
        {
            switch (effectId)
            {
                case "FX_C1":
                case "FX_D0":
                case "FX_D1":
                case "FX_E0":
                case "FX_E1":
                case "FX_F0":
                case "FX_F1":
                case "FX_G0":
                case "FX_G1":
                case "FX_I0":
                case "FX_I1":
                case "FX_J0":
                case "FX_J1":
                case "FX_K1":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsEquipmentLive(string effectId)
        {
            switch (effectId)
            {
                case "EQ_E05":
                case "EQ_E07":
                case "EQ_E08":
                case "EQ_E09":
                case "EQ_E10":
                    return true;
                default:
                    return false;
            }
        }
    }
}
