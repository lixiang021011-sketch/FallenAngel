using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 通用件（左手随身器材）的运行时口子：持有、按次数的账、曲前挂效果。
    ///
    /// 分工（见 docs/common-gear-spec.md §2.2）：
    /// 效果归 ModifierManager（种类 / 数值 / 时长 / 作用域 / 叠加），本服务不碰数值；
    /// 次数归本服务（本曲 N 次 / 本局 N 次）——ModifierDef 没有"还剩几次"的语义，
    /// 把计数塞进去，将来一改规则就会穿。
    ///
    /// 时间线：曲前 BeginSong 挂上全部已持有件的效果（Song 的随曲终由 ModifierManager.EndSong 清，
    /// Run 的留到局终）；局终（新局 / 放弃）走 EndRun。
    ///
    /// 来源固定记 ModifierSource.Item（局内道具 / 随身器材），便于 HUD"生效中"列表与结算分账。
    /// </summary>
    public static class CommonGearService
    {
        private static readonly List<string> held = new List<string>();
        private static readonly Dictionary<string, int> songUsed = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> runUsed = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>本局持有（曲前由 BeginSong 传入的快照）</summary>
        public static IReadOnlyList<string> Held { get { return held; } }

        /// <summary>最近一次曲前挂上的效果条数（自检与日志用）</summary>
        public static int AppliedCount { get; private set; }

        /// <summary>本局是否持有某件（未启用的件即使写在持有列表里也不算数）</summary>
        public static bool Owns(string gearId)
        {
            if (string.IsNullOrEmpty(gearId) || held.Count == 0) return false;
            GearItemRow item = GearCatalog.Find(gearId);
            if (item == null || !item.Enabled) return false;
            for (int i = 0; i < held.Count; i++)
            {
                if (string.Equals(held[i], gearId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>写入本局持有列表（曲前快照；只认已启用的件）</summary>
        public static void SetHeld(IReadOnlyList<string> gearIds)
        {
            held.Clear();
            if (gearIds == null) return;
            for (int i = 0; i < gearIds.Count; i++)
            {
                string id = gearIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                GearItemRow item = GearCatalog.Find(id);
                if (item == null || !item.Enabled) continue;
                if (!held.Contains(id)) held.Add(id);
            }
        }

        /// <summary>
        /// 曲前：写入持有快照 → 清零本曲次数 → 挂上已持有件的效果。
        /// 调用方是 PortfolioSession.StartSong（局内快照里的持有列表）。
        /// </summary>
        public static void BeginSong(IReadOnlyList<string> heldIds)
        {
            SetHeld(heldIds);
            songUsed.Clear();
            AppliedCount = ApplySongEffects();

            // 开局生命加成（CG12 喉糖）落在 PlayRules 上，而 JudgeManager 建谱时已经调过 PlayLife.Reset()；
            // 所以效果挂上之后补一次 Reset（此时还没 GO，语义仍是"开曲满血"）。
            if (PlayRules.LifeBonus > 0f) PlayLife.Reset();

            Debug.Log(string.Format("[CommonGear] 曲前：持有 {0} 件、挂上 {1} 条效果（生命加成 {2:0.#}）",
                held.Count, AppliedCount, PlayRules.LifeBonus));
        }

        /// <summary>曲终：Song 作用域的效果由 ModifierManager 自己清；这里给"按曲结算的件"留口子</summary>
        public static void EndSong()
        {
            songUsed.Clear();
        }

        /// <summary>局终 / 新局 / 放弃：清空持有与所有次数（效果由 ModifierManager.EndRun 清）</summary>
        public static void EndRun()
        {
            held.Clear();
            songUsed.Clear();
            runUsed.Clear();
            AppliedCount = 0;
        }

        /// <summary>本曲第 N 次：还有余额就消费一次并返回 true</summary>
        public static bool TryConsumeSongCharge(string gearId, int limit)
        {
            if (!Owns(gearId) || limit <= 0) return false;
            int used;
            songUsed.TryGetValue(gearId, out used);
            if (used >= limit) return false;
            songUsed[gearId] = used + 1;
            return true;
        }

        /// <summary>本局第 N 次：还有余额就消费一次并返回 true</summary>
        public static bool TryConsumeRunCharge(string gearId, int limit)
        {
            if (!Owns(gearId) || limit <= 0) return false;
            int used;
            runUsed.TryGetValue(gearId, out used);
            if (used >= limit) return false;
            runUsed[gearId] = used + 1;
            return true;
        }

        public static int SongChargesUsed(string gearId)
        {
            int used;
            return gearId != null && songUsed.TryGetValue(gearId, out used) ? used : 0;
        }

        public static int RunChargesUsed(string gearId)
        {
            int used;
            return gearId != null && runUsed.TryGetValue(gearId, out used) ? used : 0;
        }

        /// <summary>把已持有件挂到 ModifierManager（返回实际挂上的条数）</summary>
        private static int ApplySongEffects()
        {
            ModifierManager mgr = ModifierManager.EnsureInstance();
            if (mgr == null) return 0;

            int applied = 0;
            IReadOnlyList<GearItemRow> items = GearCatalog.Items;
            for (int i = 0; i < items.Count; i++)
            {
                GearItemRow item = items[i];
                if (item == null || !item.Enabled || !Owns(item.GearId)) continue;
                List<string> ids = GearCatalog.ModifierIdsOf(item.GearId);
                for (int k = 0; k < ids.Count; k++)
                {
                    ModifierDef def;
                    if (!ModifierCatalog.TryCreate(ids[k], ModifierSource.Item, out def)) continue;
                    mgr.Apply(def);
                    applied++;
                }
            }
            return applied;
        }
    }
}
