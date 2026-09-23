using System;
using System.Collections.Generic;

namespace FallenAngel.Core
{
    /// <summary>
    /// modifier 内容表的一行（**内容层**的唯一入口，D3 定案 2026-09-22）。
    ///
    /// 分工：**"是什么效果"写在这里**（种类 / 数值 / 秒表 / 作用域 / 正负），
    /// **"谁给的"由施加方传入** <see cref="ModifierSource"/>——同一份内容可以被天赋、构筑、
    /// 房间事件、掉落共用，不必各写一份。玩家可见文本一律走 Loc key（nameKey / descKey）。
    ///
    /// 内容作用域的两条口径（见 <see cref="ModifierScope"/>）：
    /// · 本曲：duration &lt;= 0 整曲常驻；duration &gt; 0 按歌曲时间倒计时；
    /// · 本次行程：duration 必须为 0（SongTime 每曲归零，秒表跨不过曲界），消耗改用限次。
    /// </summary>
    public sealed class ModifierContentRow
    {
        public string ModifierId { get; }
        public ModifierKind Kind { get; }
        public float Magnitude { get; }
        public float Duration { get; }
        public ModifierScope Scope { get; }
        public bool Debuff { get; }
        public string NameKey { get; }
        public string DescKey { get; }
        public bool Enabled { get; }

        public ModifierContentRow(string modifierId, ModifierKind kind, float magnitude,
                                  float duration = 0f, ModifierScope scope = ModifierScope.Song,
                                  bool debuff = false, string nameKey = null, string descKey = null,
                                  bool enabled = true)
        {
            ModifierId = modifierId;
            Kind = kind;
            Magnitude = magnitude;
            Duration = duration;
            Scope = scope;
            Debuff = debuff;
            NameKey = nameKey;
            DescKey = descKey;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// modifier 内容表（D3 的"内容层"）。**当前只有通用件第一批的 6 行**（CG01 / CG06 / CG08 / CG12 / CG13，
    /// 见 docs/common-gear-spec.md）——构筑（D1）与天赋的内容仍为空，按各自排期往里填。
    ///
    /// 生成侧的口子：本表由代码持有是为了让"内容 = 数据"，将来若接到外部表导出
    /// （像 PortfolioConfig 那样由脚本生成），只需把 <see cref="Rows"/> 换成导出的行集合，
    /// 施加侧（ModifierManager / BuildCatalog）不用改。
    /// </summary>
    public static class ModifierCatalog
    {
        private static readonly ModifierContentRow[] rows = new ModifierContentRow[]
        {
            // ── 通用件第一批 ──
            // 玩家可见文本与器材共用 Loc key（一件一条效果时够用；将来一件挂多条、文案要分开时再各自写 key）。
            // 来源固定由施加方传入：通用件走 ModifierSource.Item（见 CommonGearService）。
            new ModifierContentRow("MC_LINE_LOCK", ModifierKind.JudgeLineLock, 0f, 0f,
                ModifierScope.Run, false, "gear.cg01.name", "gear.cg01.desc"),
            new ModifierContentRow("MC_HOTBAG", ModifierKind.JudgeWindowScale, 0.85f, 30f,
                ModifierScope.Song, false, "gear.cg06.name", "gear.cg06.desc"),
            new ModifierContentRow("MC_EARPLUG_TIGHT", ModifierKind.JudgeWindowScale, 0.90f, 0f,
                ModifierScope.Song, false, "gear.cg08.name", "gear.cg08.desc"),
            new ModifierContentRow("MC_EARPLUG_SCORE", ModifierKind.ScoreMultiplier, 1.10f, 0f,
                ModifierScope.Song, false, "gear.cg08.name", "gear.cg08.desc"),
            new ModifierContentRow("MC_THROAT_BONUS", ModifierKind.LifeBonus, 100f, 0f,
                ModifierScope.Song, false, "gear.cg12.name", "gear.cg12.desc"),
            new ModifierContentRow("MC_BANDAGE", ModifierKind.LifeDamageScale, 0.85f, 0f,
                ModifierScope.Run, false, "gear.cg13.name", "gear.cg13.desc"),
        };

        /// <summary>全部内容行</summary>
        public static IReadOnlyList<ModifierContentRow> Rows { get { return rows; } }

        /// <summary>按 id 找内容行（出货表）</summary>
        public static ModifierContentRow Find(string modifierId)
        {
            return Find(modifierId, rows);
        }

        /// <summary>按 id 找内容行（指定表；自检与外部表共用）</summary>
        public static ModifierContentRow Find(string modifierId, IReadOnlyList<ModifierContentRow> table)
        {
            if (string.IsNullOrEmpty(modifierId) || table == null) return null;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != null && table[i].ModifierId == modifierId) return table[i];
            }
            return null;
        }

        /// <summary>由内容行生成运行用的 modifier 定义（来源由调用方决定）</summary>
        public static ModifierDef Create(ModifierContentRow row, ModifierSource source)
        {
            if (row == null) return null;
            return new ModifierDef(row.ModifierId, row.Kind, row.Magnitude, row.Duration,
                row.NameKey, row.DescKey, row.Scope, source, row.Debuff);
        }

        /// <summary>按 id 生成定义；内容缺失或未启用时返回 false</summary>
        public static bool TryCreate(string modifierId, ModifierSource source, out ModifierDef def)
        {
            return TryCreate(Find(modifierId), source, out def);
        }

        /// <summary>按内容行生成定义；行为空或未启用时返回 false</summary>
        public static bool TryCreate(ModifierContentRow row, ModifierSource source, out ModifierDef def)
        {
            def = row != null && row.Enabled ? Create(row, source) : null;
            return def != null;
        }

        /// <summary>
        /// 校验内容表，返回错误说明列表（空 = 通过）。只读、不改表。
        /// 抓的是"写错了但不会立刻报错"的几类：id 空/重复、倍率非正、秒表口径错、缺 Loc key。
        /// </summary>
        public static List<string> Validate(IReadOnlyList<ModifierContentRow> table)
        {
            var errors = new List<string>();
            if (table == null) return errors;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < table.Count; i++)
            {
                ModifierContentRow row = table[i];
                if (row == null)
                {
                    errors.Add(string.Format("内容表：第 {0} 行为空", i));
                    continue;
                }
                string label = string.IsNullOrEmpty(row.ModifierId) ? string.Format("第 {0} 行", i) : row.ModifierId;
                if (string.IsNullOrEmpty(row.ModifierId)) errors.Add(string.Format("内容表：第 {0} 行的 modifierId 为空", i));
                else if (!seen.Add(row.ModifierId)) errors.Add("内容表：" + row.ModifierId + " 重复");

                if (NeedsPositiveMagnitude(row.Kind) && row.Magnitude <= 0f)
                {
                    errors.Add(string.Format("内容表：{0}（{1}）的 magnitude 必须 > 0，当前 {2:0.###}",
                        label, row.Kind, row.Magnitude));
                }
                if (row.Duration < 0f)
                {
                    errors.Add(string.Format("内容表：{0} 的 duration 不能为负（常驻就写 0）", label));
                }
                if (row.Scope == ModifierScope.Run && row.Duration > 0f)
                {
                    errors.Add(string.Format(
                        "内容表：{0} 是行程作用域，不接受秒计时（SongTime 每曲归零）——改 duration=0 + 限次",
                        label));
                }
                if (row.Enabled && (string.IsNullOrEmpty(row.NameKey) || string.IsNullOrEmpty(row.DescKey)))
                {
                    errors.Add(string.Format("内容表：{0} 缺 nameKey/descKey（玩家可见文本要走 Loc）", label));
                }
            }
            return errors;
        }

        /// <summary>哪些种类的数值必须为正（回血可负 = 持续掉血；两档可见性不看数值）</summary>
        private static bool NeedsPositiveMagnitude(ModifierKind kind)
        {
            switch (kind)
            {
                case ModifierKind.JudgeWindowScale:
                case ModifierKind.LifeDamageScale:
                case ModifierKind.ScoreMultiplier:
                case ModifierKind.LifeBonus:
                    return true;
                default:
                    return false;
            }
        }
    }
}
