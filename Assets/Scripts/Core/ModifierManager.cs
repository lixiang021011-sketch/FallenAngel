using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// modifier 的作用域（D3 定案 2026-09-22：作用域只有两种，"常驻到 ClearAll"不是第三种）。
    ///
    /// · <see cref="Song"/> = 本曲：一局演奏的边界内有效。duration &lt;= 0 = 整曲常驻；
    ///   duration &gt; 0 = 施加后 N 秒失效（时间轴 = SongTime，暂停自动冻结）。曲终由
    ///   <see cref="ModifierManager.EndSong"/> 统一清理。
    /// · <see cref="Run"/> = 本次行程：跨曲保留（地图阶段 + 本局所有演奏），局终/新局/放弃时
    ///   由 <see cref="ModifierManager.EndRun"/> 清理。**duration 对 Run 无意义**——SongTime
    ///   每曲归零，秒表跨不过曲界，所以 Run 作用域一律常驻（要消耗就按次数，见 D1 构筑的 limit）。
    /// </summary>
    public enum ModifierScope
    {
        /// <summary>本曲（默认；曲终清除）</summary>
        Song = 0,
        /// <summary>本次行程（跨曲保留；局终清除）</summary>
        Run = 1,
    }

    /// <summary>
    /// modifier 的来源（D3 定案 2026-09-22）。用途：① 日志与 HUD"生效中"列表告诉玩家效果是谁给的；
    /// ② 定向移除（净化器只吃 debuff、某来源失效时按来源回收）；③ 结算与统计按来源分账。
    /// 来源不参与数值叠加——叠加只看 <see cref="ModifierKind"/>。
    /// </summary>
    public enum ModifierSource
    {
        /// <summary>玩法本身（Fever、规则兜底）</summary>
        System = 0,
        /// <summary>局外天赋（注册后长期生效）</summary>
        Talent = 1,
        /// <summary>局内构筑（D1：花局内现金买的"降风险 / 加杠杆"）</summary>
        Build = 2,
        /// <summary>局内道具（DemoItems 样板）</summary>
        Item = 3,
        /// <summary>房间/节点事件（D6 路线随机化产生的正负效果）</summary>
        Node = 4,
        /// <summary>掉落物（E5）</summary>
        Drop = 5,
        /// <summary>调试面板与快捷键（不进正式玩法）</summary>
        Debug = 6,
    }

    /// <summary>modifier 作用类型（首批，见 docs/architecture.md §9）</summary>
    public enum ModifierKind
    {
        /// <summary>判定窗口缩放：magnitude = 倍率（&gt;1 更宽松）</summary>
        JudgeWindowScale = 0,
        /// <summary>判定线消失：magnitude 忽略</summary>
        JudgeLineHidden = 1,
        /// <summary>谱面隐身：magnitude 忽略（位置与判定照常）</summary>
        NotesHidden = 2,
        /// <summary>受伤倍率：magnitude = 倍率（&lt;1 抗揍，&gt;1 更脆）——局内道具/buff/debuff 的一档</summary>
        LifeDamageScale = 3,
        /// <summary>每秒回血：magnitude = 每秒生命值（负数 = 持续掉血）</summary>
        LifeRegen = 4,
        /// <summary>分数倍率：magnitude = 倍率（Fever 与局内道具都走这一档）</summary>
        ScoreMultiplier = 5,
        /// <summary>判定线不可被遮蔽：magnitude 忽略。有它在，同期的 <see cref="JudgeLineHidden"/> 一律不生效（通用件 CG01 折叠谱架）</summary>
        JudgeLineLock = 6,
        /// <summary>开局生命加成：magnitude = 额外生命值（与基础上限相加，见 <see cref="PlayLife.Max"/>；通用件 CG12 喉糖）</summary>
        LifeBonus = 7,
        /// <summary>命中局内资源倍率：与分数倍率独立，沿用原有叠加/时效规则。</summary>
        EnergyMultiplier = 8,
    }

    /// <summary>一条 modifier 的定义（纯数据，可由配置表生成）</summary>
    [Serializable]
    public class ModifierDef
    {
        public string id;
        public ModifierKind kind;
        public float magnitude = 1f;
        /// <summary>持续秒数；&lt;= 0 表示本曲常驻（仅对 <see cref="ModifierScope.Song"/> 生效）</summary>
        public float duration;
        public string nameKey;
        public string descKey;
        /// <summary>作用域（本曲 / 本次行程），见 <see cref="ModifierScope"/></summary>
        public ModifierScope scope;
        /// <summary>来源（谁给的），见 <see cref="ModifierSource"/></summary>
        public ModifierSource source;
        /// <summary>是否负面效果（净化器按这个标记回收；数值本身不读它）</summary>
        public bool debuff;

        public ModifierDef() { }

        public ModifierDef(string id, ModifierKind kind, float magnitude, float duration,
                           string nameKey = null, string descKey = null,
                           ModifierScope scope = ModifierScope.Song,
                           ModifierSource source = ModifierSource.System,
                           bool debuff = false)
        {
            this.id = id;
            this.kind = kind;
            this.magnitude = magnitude;
            this.duration = duration;
            this.nameKey = nameKey;
            this.descKey = descKey;
            this.scope = scope;
            this.source = source;
            this.debuff = debuff;
        }
    }

    /// <summary>运行中的 modifier 实例（剩余时长 / 常驻）</summary>
    public class ModifierRuntime
    {
        public ModifierDef def;
        public float remaining;

        /// <summary>常驻（不按秒倒计时）：Run 作用域一律常驻；Song 作用域由 duration 决定</summary>
        public bool IsPersistent
        {
            get { return def == null || def.scope == ModifierScope.Run || def.duration <= 0f; }
        }
        public float Remaining { get { return remaining; } }
    }

    /// <summary>
    /// 局内 modifier 管理器（单例，已在 docs/architecture.md §2 登记）。
    /// 职责：持有 buff/debuff 列表、叠加规则、按歌曲时间倒计时、对外广播施加/失效事件，
    /// 并把当前状态汇总给 <see cref="PlayRules"/>。
    ///
    /// 计时一律取 GameManager.SongTime（§5）：暂停时 SongTime 冻结，效果计时自动冻结。
    /// 叠加规则：同 id 的效果重复施加 = 刷新时长（不叠加强度）；判定窗口倍率按乘法叠加。
    ///
    /// 作用域生命周期（D3 定案 2026-09-22）：本曲结束（GameManager.OnGameEnd）或状态回落菜单时
    /// 自动调 <see cref="EndSong"/>（只清 <see cref="ModifierScope.Song"/>）；整段行程结束（新局 /
    /// 放弃 / 退出成长）由 PortfolioSession 调 <see cref="EndRun"/>（清全部，含 Run 作用域）。
    /// </summary>
    public class ModifierManager : MonoBehaviour
    {
        public static ModifierManager Instance { get; private set; }

        private readonly List<ModifierRuntime> active = new List<ModifierRuntime>();
        private readonly List<int> expireScratch = new List<int>();
        private GameManager subscribedManager;   // 已订阅的 GameManager（退订用，避免 OnDisable 时它已销毁）

        /// <summary>施加一条新效果（重复施加同一 id 视为刷新时长）</summary>
        public event Action<ModifierDef> OnModifierApplied;
        /// <summary>一条效果到时失效</summary>
        public event Action<ModifierDef> OnModifierExpired;

        public IReadOnlyList<ModifierRuntime> Active { get { return active; } }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // 订阅规范见 docs/architecture.md §3：OnEnable 订阅、Start 再订一次（防 Awake 顺序漏订）、OnDisable 退订。
        private void OnEnable() { Subscribe(); }
        private void Start() { Subscribe(); }
        private void OnDisable() { Unsubscribe(); }

        /// <summary>订阅曲终/状态事件（自检也用它强制重新订阅，保证与当前 GameManager 同源）</summary>
        internal void Subscribe()
        {
            Unsubscribe();
            subscribedManager = GameManager.Instance;
            if (subscribedManager == null) return;
            subscribedManager.OnGameEnd -= HandleGameEnd;
            subscribedManager.OnGameEnd += HandleGameEnd;
            subscribedManager.OnStateChanged -= HandleStateChanged;
            subscribedManager.OnStateChanged += HandleStateChanged;
        }

        private void Unsubscribe()
        {
            if (subscribedManager == null) return;
            subscribedManager.OnGameEnd -= HandleGameEnd;
            subscribedManager.OnStateChanged -= HandleStateChanged;
            subscribedManager = null;
        }

        /// <summary>曲终 = 本曲作用域的边界</summary>
        private void HandleGameEnd() { EndSong(); }

        /// <summary>回落菜单也要收本曲作用域：放弃本曲走的是 BackToMenu，不经过 OnGameEnd</summary>
        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.Menu) EndSong();
        }

        /// <summary>
        /// 取实例；场景里没有就地建一个。
        /// 调试窗口（ModifierDebugPanel）靠它工作：旧场景、或编辑器非播放态下 Awake 不会跑，
        /// 这里显式兜底，避免"点了没反应"这种静默失败。
        /// </summary>
        public static ModifierManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ModifierManager(Runtime)");
            var mgr = go.AddComponent<ModifierManager>();
            if (Instance == null) Instance = mgr;   // Awake 未必被调用（编辑器非播放态）
            return mgr;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            PlayRules.ResetModifiers();
        }

        private void Update()
        {
            // 只在演奏中推进：SongTime 由 AudioManager 供时，菜单/暂停期间不流逝
            if (GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentState != GameState.Playing) return;
            Tick(GameManager.Instance.SongTime);
        }

        /// <summary>按歌曲时间推进效果计时（自检可直接调用，避免依赖 Update 时序）</summary>
        public void Tick(float songTime)
        {
            if (active.Count == 0) return;

            expireScratch.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                ModifierRuntime rt = active[i];
                if (rt.IsPersistent) continue;
                // remaining 存的是"到期时刻"，与 SongTime 同一时间轴
                if (songTime >= rt.remaining) expireScratch.Add(i);
            }
            if (expireScratch.Count == 0) return;

            for (int i = expireScratch.Count - 1; i >= 0; i--)
            {
                ModifierRuntime rt = active[expireScratch[i]];
                active.RemoveAt(expireScratch[i]);
                Debug.Log(string.Format("[ModifierManager] 失效 {0}（{1}）", rt.def.id, rt.def.kind));
                OnModifierExpired?.Invoke(rt.def);
            }
            Recompute();
        }

        /// <summary>施加效果：duration &lt;= 0 为本曲常驻，否则按 SongTime 倒计时</summary>
        public void Apply(ModifierDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return;
            float now = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            Apply(def, now);
        }

        /// <summary>施加效果（显式给当前歌曲时间，便于自检与外部按同一时间轴调用）</summary>
        public void Apply(ModifierDef def, float songTime)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return;

            ModifierRuntime existing = Find(def.id);
            if (existing != null)
            {
                // 同 id 重复施加 = 刷新时长（不叠加强度）
                existing.def = def;
                existing.remaining = ExpiryOf(def, songTime);
                Debug.Log(string.Format("[ModifierManager] 刷新 {0}（{1}，来源={2}，作用域={3}）",
                    def.id, def.kind, def.source, def.scope));
            }
            else
            {
                var rt = new ModifierRuntime
                {
                    def = def,
                    remaining = ExpiryOf(def, songTime),
                };
                active.Add(rt);
                Debug.Log(string.Format(
                    "[ModifierManager] 施加 {0}（{1}，magnitude={2}，{3}，来源={4}，作用域={5}{6}）",
                    def.id, def.kind, def.magnitude, DurationText(def), def.source, def.scope,
                    def.debuff ? "，debuff" : ""));
            }
            Recompute();
            OnModifierApplied?.Invoke(def);
        }

        /// <summary>移除一条效果（按 id），返回是否移除成功</summary>
        public bool Remove(string id)
        {
            int idx = active.FindIndex(r => r.def != null && r.def.id == id);
            if (idx < 0) return false;
            ModifierDef def = active[idx].def;
            active.RemoveAt(idx);
            Recompute();
            OnModifierExpired?.Invoke(def);
            return true;
        }

        /// <summary>
        /// 本曲结束：只清 <see cref="ModifierScope.Song"/>，本次行程（Run）作用域跨曲保留。
        /// 由曲终事件与"回落菜单"自动调用；也可手动调用（调试面板的"模拟曲终"）。
        /// </summary>
        public int EndSong()
        {
            return RemoveWhere(rt => rt.def != null && rt.def.scope == ModifierScope.Song,
                "本曲结束：清除本曲作用域");
        }

        /// <summary>整段行程结束（新局 / 放弃 / 退出成长）：清空全部，含本次行程作用域</summary>
        public void EndRun()
        {
            RemoveWhere(rt => true, "本次行程结束：清空全部效果（含行程作用域）");
        }

        /// <summary>清空全部效果（调试与自检入口；生产里的曲终/局终走 EndSong / EndRun）</summary>
        public void ClearAll()
        {
            RemoveWhere(rt => true, "清空全部效果");
        }

        /// <summary>
        /// 净化器入口：移除全部 debuff（<see cref="ModifierDef.debuff"/>），buff 原样保留。返回移除条数。
        /// </summary>
        public int RemoveDebuffs()
        {
            return RemoveWhere(rt => rt.def != null && rt.def.debuff, "净化：移除 debuff");
        }

        /// <summary>统一的移除实现：按条件收集 → 集体退场 → 逐个广播失效事件 → 重算有效参数</summary>
        private int RemoveWhere(System.Predicate<ModifierRuntime> match, string reason)
        {
            List<ModifierDef> removedDefs = null;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (!match(active[i])) continue;
                if (removedDefs == null) removedDefs = new List<ModifierDef>();
                if (active[i].def != null) removedDefs.Add(active[i].def);
                active.RemoveAt(i);
            }
            Recompute();   // 空表重算 = 回到基线（PlayRules.ResetModifiers 的等价写法）
            if (removedDefs == null) return 0;
            Debug.Log(string.Format("[ModifierManager] {0}：{1} 条", reason, removedDefs.Count));
            for (int i = 0; i < removedDefs.Count; i++) OnModifierExpired?.Invoke(removedDefs[i]);
            return removedDefs.Count;
        }

        /// <summary>失效时刻（SongTime 轴）；常驻返回 0</summary>
        private static float ExpiryOf(ModifierDef def, float songTime)
        {
            if (def.scope == ModifierScope.Run && def.duration > 0f)
            {
                // 秒表跨不过曲界（SongTime 每曲归零），Run 作用域按设计一律常驻。
                Debug.LogWarning(string.Format(
                    "[ModifierManager] {0}：Run 作用域不接受秒计时（duration={1:0.#}），已按常驻处理",
                    def.id, def.duration));
            }
            return def.scope == ModifierScope.Song && def.duration > 0f ? songTime + def.duration : 0f;
        }

        private static string DurationText(ModifierDef def)
        {
            if (def.scope == ModifierScope.Run) return "行程内常驻";
            return def.duration > 0f ? def.duration + "s" : "本曲常驻";
        }

        public bool Has(string id)
        {
            return Find(id) != null;
        }

        private ModifierRuntime Find(string id)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].def != null && active[i].def.id == id) return active[i];
            }
            return null;
        }

#if UNITY_EDITOR
        // Play 模式下的手动验证入口：选中场景里的 Managers 对象，在 Inspector 齿轮菜单里点。
        [ContextMenu("调试/判定窗口放宽 1.5×（常驻）")]
        private void DebugLooseWindows()
        {
            Apply(new ModifierDef("DBG_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/判定窗口收紧 0.6×（常驻）")]
        private void DebugTightWindows()
        {
            Apply(new ModifierDef("DBG_TIGHT", ModifierKind.JudgeWindowScale, 0.6f, 0f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/判定线消失 3 秒")]
        private void DebugHideJudgeLine()
        {
            Apply(new ModifierDef("DBG_NOLINE", ModifierKind.JudgeLineHidden, 0f, 3f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/谱面隐身 3 秒")]
        private void DebugHideNotes()
        {
            Apply(new ModifierDef("DBG_NONOTES", ModifierKind.NotesHidden, 0f, 3f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/判定线不可遮蔽 5 秒（CG01 折叠谱架的口径）")]
        private void DebugLineLock()
        {
            Apply(new ModifierDef("DBG_LINELOCK", ModifierKind.JudgeLineLock, 0f, 5f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/开局生命 +100（CG12 喉糖的口径）")]
        private void DebugLifeBonus()
        {
            Apply(new ModifierDef("DBG_LIFEBONUS", ModifierKind.LifeBonus, 100f, 0f,
                scope: ModifierScope.Song, source: ModifierSource.Debug));
            PlayLife.Reset();   // 开局加成要重新进一次 Reset 才落到当前生命上
        }

        [ContextMenu("调试/判定窗口放宽 1.2×（本次行程）")]
        private void DebugRunWideWindows()
        {
            Apply(new ModifierDef("DBG_RUN_WIDE", ModifierKind.JudgeWindowScale, 1.2f, 0f,
                scope: ModifierScope.Run, source: ModifierSource.Debug));
        }

        [ContextMenu("调试/模拟曲终（清本曲作用域）")]
        private void DebugEndSong()
        {
            EndSong();
        }

        [ContextMenu("调试/模拟局终（清全部含行程）")]
        private void DebugEndRun()
        {
            EndRun();
        }

        [ContextMenu("调试/清空全部效果")]
        private void DebugClearAll()
        {
            ClearAll();
        }
#endif

        /// <summary>把当前效果汇总成 PlayRules 的有效参数</summary>
        private void Recompute()
        {
            float scale = 1f;
            bool lineVisible = true;
            bool notesVisible = true;
            float damageScale = 1f;
            float regen = 0f;
            float scoreMul = 1f;
            float energyMul = 1f;
            float lifeBonus = 0f;
            bool lineLocked = false;

            for (int i = 0; i < active.Count; i++)
            {
                ModifierDef def = active[i].def;
                if (def == null) continue;
                switch (def.kind)
                {
                    case ModifierKind.JudgeWindowScale:
                        scale *= def.magnitude;   // 乘法叠加
                        break;
                    case ModifierKind.JudgeLineHidden:
                        lineVisible = false;
                        break;
                    case ModifierKind.JudgeLineLock:
                        lineLocked = true;      // 锁在：最后统一把可见性拉回可见（见循环后的 if）
                        break;
                    case ModifierKind.NotesHidden:
                        notesVisible = false;
                        break;
                    case ModifierKind.LifeDamageScale:
                        damageScale *= def.magnitude;   // 乘法叠加：两个 0.5 = 只吃 25% 伤害
                        break;
                    case ModifierKind.LifeRegen:
                        regen += def.magnitude;         // 加法叠加：两条 +5/s = +10/s
                        break;
                    case ModifierKind.ScoreMultiplier:
                        scoreMul *= def.magnitude;      // 乘法叠加（fever × 道具）
                        break;
                    case ModifierKind.EnergyMultiplier:
                        energyMul *= def.magnitude;
                        break;
                    case ModifierKind.LifeBonus:
                        lifeBonus += def.magnitude;     // 加法叠加：两条 +100 = +200
                        break;
                }
            }
            // 判定线锁只抬可见性下限，不影响判定；音符隐身不受它管（那是另一条效果）
            if (lineLocked) lineVisible = true;
            PlayRules.SetModifierState(scale, lineVisible, notesVisible, damageScale, regen, scoreMul,
                lifeBonus, energyMul);
        }
    }
}
