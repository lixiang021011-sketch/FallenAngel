using System;
using System.Collections.Generic;
using System.Linq;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>一次成功演奏的收益结算</summary>
    public sealed class IncomeSettlement
    {
        public double BaseIncome;
        public double PerformanceTotal; // 演奏收益（直接+增幅+保底-封顶）
        public double EconomyTotal;     // 独立经济收益（利息等，不参与增幅/保底/封顶）
        public double Total => PerformanceTotal + EconomyTotal;
        public List<IncomeLineData> Lines = new List<IncomeLineData>();
    }

    /// <summary>
    /// 收益核心闭环 Lite（纯计算，不读写存档）：整曲结算级效果——
    /// I0/I1 整曲Perfect达标奖励、J0/J1 付费挑战奖励、D0 演奏增幅、E05 无Miss增幅（装备）、
    /// G0/G1 收益保底、C1/E09 开曲现金利息、统一封顶。
    /// 乐句级效果（A0/B0/C0 等）与补偿（E06）设计已定，乐句统计接入后扩展；K0 需 A/B/C 原始奖励，暂不启用。
    /// </summary>
    public sealed class PortfolioIncomeService
    {
        /// <summary>
        /// 计算结算。talents=已解锁天赋效果；equipment=已持有装备效果；stage=本关；
        /// perfectRate=整曲 Perfect 率（成绩口径）；missCount=Miss 数；openingCash=开曲现金快照。
        /// </summary>
        public IncomeSettlement Compute(
            IReadOnlyList<TalentEffectsRow> talents,
            IReadOnlyList<EquipmentEffectsRow> equipment,
            StagesRow stage,
            double perfectRate,
            int missCount,
            double openingCash)
        {
            var s = new IncomeSettlement { BaseIncome = stage.BaseIncome };
            double b = stage.BaseIncome;
            double direct = b; // 原始直接奖励 = 基础 B + 达标/挑战奖励

            // ---- 直接奖励（DIRECT）----
            // I0/I1：整曲 Perfect 分级达标（2026-09-09 数值模拟后新规则）——
            // ≥上层线（PerfectFullThreshold）给满额；≥表内下层线（threshold=0.7）给半额。覆盖升级用 I1 系数。
            var i0 = talents.FirstOrDefault(e => e.Handler == "perfect_goal_reward");
            if (i0 != null && i0.Threshold.HasValue && perfectRate >= i0.Threshold.Value)
            {
                double coef = EffectiveCoefficient(i0, talents);
                if (i0.Threshold.Value < PortfolioDefaults.PerfectFullThreshold
                    && perfectRate < PortfolioDefaults.PerfectFullThreshold)
                    coef *= 0.5;
                double amount = b * coef;
                AddLine(s, "income.I0", amount, "DIRECT");
                direct += amount;
            }
            // J0/J1：付费挑战完成
            var j0 = talents.FirstOrDefault(e => e.Handler == "challenge_reward");
            if (j0 != null && stage.StageType == "PAID_CHALLENGE")
            {
                double amount = b * EffectiveCoefficient(j0, talents);
                AddLine(s, "income.J0", amount, "DIRECT");
                direct += amount;
            }
            double performance = direct;

            // ---- 增幅（BONUS，按来源加算）----
            // D0：演奏直接奖励 +10%（scope 区分 K0，K0 暂不启用）
            var d0 = talents.FirstOrDefault(e => e.Handler == "source_bonus"
                && e.TargetScope == "PERFORMANCE_WITH_COMPENSATION");
            if (d0 != null)
            {
                double amount = direct * d0.Coefficient;
                AddLine(s, "income.D0", amount, "BONUS");
                performance += amount;
            }
            // E05：无 Miss → +25%（装备）
            var e05 = equipment.FirstOrDefault(e => e.Handler == "no_miss_multiplier");
            if (e05 != null && missCount == 0)
            {
                double amount = direct * e05.Coefficient;
                AddLine(s, "income.E05", amount, "BONUS");
                performance += amount;
            }

            // ---- 保底（FLOOR）----
            var g0 = talents.FirstOrDefault(e => e.Handler == "income_floor");
            if (g0 != null)
            {
                double floor = b * EffectiveCoefficient(g0, talents);
                if (performance < floor)
                {
                    AddLine(s, "income.G0", floor - performance, "FLOOR");
                    performance = floor;
                }
            }

            // ---- 封顶（CAP，共同经济设置的原型上限）----
            double cap = b * (1 + PortfolioDefaults.IncomeCapRatio);
            if (performance > cap)
            {
                AddLine(s, "income.cap", performance - cap, "CAP");
                performance = cap;
            }
            s.PerformanceTotal = performance;

            // ---- 独立经济收益（ECONOMY）----
            // C1：开曲现金利息 5%，单次原始上限 3%B；E09：装备利息 10%，上限 5%B。各自封顶后加算。
            s.EconomyTotal = 0;
            var c1 = talents.FirstOrDefault(e => e.Handler == "opening_balance_interest");
            if (c1 != null && openingCash > 0)
            {
                double interest = Math.Min(openingCash * c1.Coefficient, b * (c1.ValueCapB ?? 0));
                if (interest > 0)
                {
                    AddLine(s, "income.C1", interest, "ECONOMY");
                    s.EconomyTotal += interest;
                }
            }
            var e09 = equipment.FirstOrDefault(e => e.Handler == "opening_balance_interest");
            if (e09 != null && openingCash > 0)
            {
                double interest = Math.Min(openingCash * e09.Coefficient, b * (e09.ValueCapB ?? 0));
                if (interest > 0)
                {
                    AddLine(s, "income.E09", interest, "ECONOMY");
                    s.EconomyTotal += interest;
                }
            }
            return s;
        }

        /// <summary>覆盖升级：存在指向目标效果的 modify_coefficient 时用其系数（一效果一升级，见 guide）</summary>
        private static double EffectiveCoefficient(TalentEffectsRow target, IReadOnlyList<TalentEffectsRow> talents)
        {
            var modifier = talents.FirstOrDefault(e => e.Handler == "modify_coefficient"
                && e.TargetEffectId == target.EffectId);
            return modifier != null ? modifier.Coefficient : target.Coefficient;
        }

        private static void AddLine(IncomeSettlement s, string key, double amount, string kind)
        {
            s.Lines.Add(new IncomeLineData { key = key, amount = amount, kind = kind });
        }
    }
}
