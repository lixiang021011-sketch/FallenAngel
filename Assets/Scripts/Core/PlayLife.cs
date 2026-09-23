using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏中的生命值（前端对照清单 B4）。数值与判定伤害**逐字照抄上游**：
    ///
    /// | 上游 | 我们 |
    /// | --- | --- |
    /// | `Sekai/Live/LiveConfig.cs:424` `Life = 1000` | `MaxLife = 1000f` |
    /// | `Sekai/Live/LiveConfig.cs:438` `Damages = { Bad: 60, Miss: 80 }` | `DamageBad = 60` / `DamageMiss = 80` |
    /// | `Sekai/LifeView.cs`：`rate = life / maxLife`，双层槽（实槽 + 掉血残影） | `Rate` 供 UI 读；残影在 `UI/LifeView` |
    /// | `ScreenEffectView.Excute(note)`：只有 Miss/Bad 播屏幕伤害特效 | 伤害在 `JudgeManager` 判定结果处结算 |
    ///
    /// **归零后果（作者 2026-09-22 定案）：不结算、继续打完**（练习模式口径）。
    /// 注意出处：上游反编译里**没有**练习模式的 live 分支（练习相关只有菜单/票券；
    /// `LiveConfig.ContinueNoDamageTime = 1f` 是"复活后 1 秒无伤"），所以这条是我们按作者口径实现的，
    /// 不是从上游抄来的分支 —— 写在这里免得以后当成"照抄"。
    ///
    /// buff/debuff 口子：受伤倍率（`PlayRules.LifeDamageScale`）与每秒回血（`PlayRules.LifeRegenPerSecond`）
    /// 都由 `ModifierManager` 汇总，局内道具只要 `Apply(new ModifierDef(...))` 就能改生命玩法。
    /// </summary>
    public static class PlayLife
    {
        /// <summary>最大生命（上游 `LiveConfig.Life`）</summary>
        public const float MaxLife = 1000f;

        /// <summary>Bad 的伤害（上游 `LiveConfig.Damages[NoteResult.Bad]`）</summary>
        public const int DamageBad = 60;

        /// <summary>Miss 的伤害（上游 `LiveConfig.Damages[NoteResult.Miss]`）</summary>
        public const int DamageMiss = 80;

        private static float current = MaxLife;

        /// <summary>生命值变化（current, max）；UI 订阅它刷新槽与数字。</summary>
        public static event System.Action<float, float> OnChanged;

        public static float Current { get { return current; } }

        /// <summary>有效上限 = 基础上限（上游 LiveConfig.Life）+ PlayRules 的开局生命加成（CG12 喉糖）。</summary>
        public static float Max { get { return MaxLife + Mathf.Max(0f, PlayRules.LifeBonus); } }

        /// <summary>`life / max`（上游 `LifeView` 的 rate；分母用有效上限，开曲满血时恒为 1）</summary>
        public static float Rate { get { return Max > 0f ? current / Max : 0f; } }

        /// <summary>生命是否已经见底（见底后照常继续打，只是不再往下扣）</summary>
        public static bool IsEmpty { get { return current <= 0f; } }

        /// <summary>开局/重开时调用</summary>
        public static void Reset()
        {
            current = Max;
            Raise();
        }

        /// <summary>受一次判定伤害（乘 `PlayRules.LifeDamageScale`；归零后夹在 0，不触发结算）</summary>
        public static void ApplyDamage(int rawDamage)
        {
            if (rawDamage <= 0) return;
            float scaled = rawDamage * Mathf.Max(0f, PlayRules.LifeDamageScale);
            current = Mathf.Max(0f, current - scaled);
            Raise();
        }

        /// <summary>回血（夹在 [0, max]）</summary>
        public static void Heal(float amount)
        {
            if (amount <= 0f) return;
            current = Mathf.Min(Max, current + amount);
            Raise();
        }

        /// <summary>按 `PlayRules.LifeRegenPerSecond` 持续变化（负数 = 持续掉血）。每帧由 JudgeManager 调。</summary>
        public static void Tick(float deltaSeconds)
        {
            float regen = PlayRules.LifeRegenPerSecond;
            if (regen == 0f || deltaSeconds <= 0f) return;
            float next = Mathf.Clamp(current + regen * deltaSeconds, 0f, Max);
            if (Mathf.Abs(next - current) < 1e-4f) return;
            current = next;
            Raise();
        }

        private static void Raise()
        {
            if (OnChanged != null) OnChanged(current, Max);
        }
    }
}
