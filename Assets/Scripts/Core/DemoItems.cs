using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// **示例局内道具**（作者 2026-09-22：C4/C6/C7 与技能 cut-in 都做，"因为会涉及到局内道具、buff、debuff"）。
    ///
    /// 这个类不是玩法内容，而是**把链路打通的样板**：一个道具 = 若干 `ModifierDef`（buff/debuff）
    /// + 一次 cut-in。也就是说以后加道具只要照抄这个形状，不需要碰判定层：
    ///
    ///   道具（能量饮料）：20 秒内 分数 ×1.1、受伤倍率 ×0.5，并弹一次 cut-in。
    ///
    /// 三档接口都在 `ModifierKind` 里：`ScoreMultiplier` / `LifeDamageScale` / `LifeRegen`
    /// （还有判定窗口与可见性两档老接口）。
    /// </summary>
    public static class DemoItems
    {
        public const string EnergyDrinkId = "ITEM_ENERGY_DRINK";
        public const float EnergyDrinkDuration = 20f;

        /// <summary>使用"能量饮料"：挂两个 buff + 播一次 cut-in</summary>
        public static bool UseEnergyDrink()
        {
            ModifierManager mgr = ModifierManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[DemoItems] 场景里没有 ModifierManager，道具无法生效");
                return false;
            }

            mgr.Apply(new ModifierDef(EnergyDrinkId + "_SCORE", ModifierKind.ScoreMultiplier, 1.1f,
                EnergyDrinkDuration, "item.energy.name", "item.energy.desc"));
            mgr.Apply(new ModifierDef(EnergyDrinkId + "_HURT", ModifierKind.LifeDamageScale, 0.5f,
                EnergyDrinkDuration, "item.energy.name", "item.energy.desc"));
            mgr.Apply(new ModifierDef(EnergyDrinkId + "_REGEN", ModifierKind.LifeRegen, 5f,
                EnergyDrinkDuration, "item.energy.name", "item.energy.desc"));

            CutinService.Play(CutinRequest.Make(EnergyDrinkId, "item.energy.icon",
                "item.energy.name", "item.energy.desc", 1.8f));

            Debug.Log(string.Format(
                "[DemoItems] 使用能量饮料：{0:0}s 内 分数 ×1.1 / 受伤 ×0.5 / 回血 +5s，已弹 cut-in",
                EnergyDrinkDuration));
            return true;
        }

        /// <summary>示例 debuff：8 秒内受伤 ×1.5（用来验证 debuff 同一条链路）</summary>
        public static bool ApplyFragileDebuff()
        {
            ModifierManager mgr = ModifierManager.Instance;
            if (mgr == null) return false;
            mgr.Apply(new ModifierDef("DEBUFF_FRAGILE", ModifierKind.LifeDamageScale, 1.5f, 8f,
                "item.fragile.name", "item.fragile.desc"));
            CutinService.Play(CutinRequest.Make("DEBUFF_FRAGILE", "item.fragile.icon",
                "item.fragile.name", "item.fragile.desc", 1.5f));
            return true;
        }
    }
}
