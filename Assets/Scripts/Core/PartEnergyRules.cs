using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>
    /// 原型期的声部道具（三条互斥，用来验证"道具是否真的改变玩家的声部路线"）。
    /// 为何不做成 ModifierKind：ModifierManager 的倍率是无条件的，
    /// 而这三条依赖**声部身份 + 段内计数**（绑哪个声部 / 留守多久 / 换手后第几颗），
    /// 放进判定层会让 ModifierKind 反查声部状态；这里由 SongPartSession 组合。
    /// </summary>
    public enum PartItem
    {
        /// <summary>无道具（对照组）</summary>
        None = 0,
        /// <summary>鼓手契约：Drums 的 Note 资源 ×1.3（逼一个声部，难度自负）</summary>
        DrumContract = 1,
        /// <summary>长坐：同一声部连续 8 秒后资源倍率 1.0 → 1.5（反对频繁切换）</summary>
        StayRamp = 2,
        /// <summary>换手红利：切换后前 10 颗 Note ×1.5（鼓励频繁切换）</summary>
        SwitchBonus = 3,
    }

    /// <summary>
    /// 声部资源规则：单位产率（音符权重）+ 三条道具倍率。
    /// 全是纯函数，自检可以直接断言，不依赖场景与判定层。
    /// </summary>
    public static class PartEnergyRules
    {
        public const float ContractMultiplier = 1.3f;
        /// <summary>判定等级的单位产率：Perfect 全给，Great 与计分的 65% 同口径，Good 保值但不保量</summary>
        public const float PerfectYield = 1f;
        public const float GreatYield = 0.65f;
        public const float GoodYield = 0.35f;
        /// <summary>留守倍率从这个秒数之后开始爬</summary>
        public const float StayRampStartSeconds = 8f;
        /// <summary>到这里封顶 1.5×</summary>
        public const float StayRampFullSeconds = 16f;
        public const float StayRampMaxMultiplier = 1.5f;
        public const int SwitchBonusNotes = 10;
        public const float SwitchBonusMultiplier = 1.5f;

        /// <summary>
        /// 单位产率：普通 / flick / drag = 1×；长音（在尾判结算）与滑条 = 2×。
        /// 作用：让"稀疏但有长音"的声部靠稳定命中也能和"密集但难打"的声部掰手腕。
        /// </summary>
        public static float NoteWeight(NoteType type)
        {
            switch (type)
            {
                case NoteType.LongStart:
                case NoteType.Slide:
                    return 2f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 判定等级 → 单位产率。为什么与计分表不同：计分照 Phigros 语义（Good 不给分），
        /// 而资源是玩法货币，Good 仍要保值（见 2026-10-03 设计稿第三节的"Good → 较少资源"）。
        /// Great 取 0.65 是为了和已有的计分口径一致（Great ≈ Perfect 的 65%）。
        /// </summary>
        public static float GradeWeight(JudgeResultType result)
        {
            switch (result)
            {
                case JudgeResultType.Perfect: return PerfectYield;
                case JudgeResultType.Great: return GreatYield;
                case JudgeResultType.Good: return GoodYield;
                default: return 0f;
            }
        }

        /// <summary>
        /// 一次命中的实得资源 = 音符权重 × 判定等级 × 倍率（modifier）× 道具倍率。
        /// 单独抽出来是为了能直接断言"高密度段掉档后是否还比稳段赚"这类问题。
        /// </summary>
        public static float Yield(NoteType type, JudgeResultType result, float multiplier, float itemMultiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 1f;
            if (float.IsNaN(itemMultiplier) || float.IsInfinity(itemMultiplier)) itemMultiplier = 1f;
            float value = NoteWeight(type) * GradeWeight(result) * multiplier * itemMultiplier;
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : UnityEngine.Mathf.Max(0f, value);
        }

        public static float PartMultiplier(PartItem item, string partName)
        {
            if (item != PartItem.DrumContract) return 1f;
            return string.Equals(partName, "Drums", System.StringComparison.Ordinal) ? ContractMultiplier : 1f;
        }

        public static float StayMultiplier(PartItem item, float samePartSeconds)
        {
            if (item != PartItem.StayRamp) return 1f;
            if (samePartSeconds <= StayRampStartSeconds) return 1f;
            float k = (samePartSeconds - StayRampStartSeconds) / (StayRampFullSeconds - StayRampStartSeconds);
            return 1f + (StayRampMaxMultiplier - 1f) * UnityEngine.Mathf.Clamp01(k);
        }

        public static float SwitchMultiplier(PartItem item, int notesSinceSwitch)
        {
            if (item != PartItem.SwitchBonus) return 1f;
            return notesSinceSwitch < SwitchBonusNotes ? SwitchBonusMultiplier : 1f;
        }

        /// <summary>本颗音符的道具总倍率（非法值兜底成 1）</summary>
        public static float Compose(PartItem item, string partName, float samePartSeconds, int notesSinceSwitch)
        {
            float m = PartMultiplier(item, partName) * StayMultiplier(item, samePartSeconds)
                      * SwitchMultiplier(item, notesSinceSwitch);
            return float.IsNaN(m) || float.IsInfinity(m) ? 1f : m;
        }

        /// <summary>原型面板显示用；正式内容里道具名走本地化表</summary>
        public static string DisplayName(PartItem item)
        {
            switch (item)
            {
                case PartItem.DrumContract: return "鼓手契约 Drums ×1.3";
                case PartItem.StayRamp: return "长坐 8s 起爬到 ×1.5";
                case PartItem.SwitchBonus: return "换手红利 前 10 颗 ×1.5";
                default: return "无道具（对照）";
            }
        }

        /// <summary>原型面板按钮用：按顺序循环</summary>
        public static PartItem Next(PartItem item)
        {
            return (PartItem)(((int)item + 1) % 4);
        }
    }
}
