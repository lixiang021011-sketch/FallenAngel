using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>modifier 作用类型（首批，见 docs/architecture.md §9）</summary>
    public enum ModifierKind
    {
        /// <summary>判定窗口缩放：magnitude = 倍率（&gt;1 更宽松）</summary>
        JudgeWindowScale = 0,
        /// <summary>判定线消失：magnitude 忽略</summary>
        JudgeLineHidden = 1,
        /// <summary>谱面隐身：magnitude 忽略（位置与判定照常）</summary>
        NotesHidden = 2,
    }

    /// <summary>一条 modifier 的定义（纯数据，可由配置表生成）</summary>
    [Serializable]
    public class ModifierDef
    {
        public string id;
        public ModifierKind kind;
        public float magnitude = 1f;
        /// <summary>持续秒数；&lt;= 0 表示本曲常驻</summary>
        public float duration;
        public string nameKey;
        public string descKey;

        public ModifierDef() { }

        public ModifierDef(string id, ModifierKind kind, float magnitude, float duration,
                           string nameKey = null, string descKey = null)
        {
            this.id = id;
            this.kind = kind;
            this.magnitude = magnitude;
            this.duration = duration;
            this.nameKey = nameKey;
            this.descKey = descKey;
        }
    }

    /// <summary>运行中的 modifier 实例（剩余时长 / 常驻）</summary>
    public class ModifierRuntime
    {
        public ModifierDef def;
        public float remaining;

        public bool IsPersistent { get { return def == null || def.duration <= 0f; } }
        public float Remaining { get { return remaining; } }
    }

    /// <summary>
    /// 局内 modifier 管理器（单例，已在 docs/architecture.md §2 登记）。
    /// 职责：持有 buff/debuff 列表、叠加规则、按歌曲时间倒计时、对外广播施加/失效事件，
    /// 并把当前状态汇总给 <see cref="PlayRules"/>。
    ///
    /// 计时一律取 GameManager.SongTime（§5）：暂停时 SongTime 冻结，效果计时自动冻结。
    /// 叠加规则：同 id 的效果重复施加 = 刷新时长（不叠加强度）；判定窗口倍率按乘法叠加。
    /// </summary>
    public class ModifierManager : MonoBehaviour
    {
        public static ModifierManager Instance { get; private set; }

        private readonly List<ModifierRuntime> active = new List<ModifierRuntime>();
        private readonly List<int> expireScratch = new List<int>();

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
                existing.remaining = def.duration > 0f ? songTime + def.duration : 0f;
                Debug.Log(string.Format("[ModifierManager] 刷新 {0}（{1}）", def.id, def.kind));
            }
            else
            {
                var rt = new ModifierRuntime
                {
                    def = def,
                    remaining = def.duration > 0f ? songTime + def.duration : 0f,
                };
                active.Add(rt);
                Debug.Log(string.Format("[ModifierManager] 施加 {0}（{1}，magnitude={2}，时长={3}）",
                    def.id, def.kind, def.magnitude, def.duration > 0f ? def.duration + "s" : "常驻"));
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

        /// <summary>清空全部效果（切歌 / 退出演奏 / 重开）</summary>
        public void ClearAll()
        {
            if (active.Count == 0) { PlayRules.ResetModifiers(); return; }
            active.Clear();
            Recompute();
            Debug.Log("[ModifierManager] 清空全部效果");
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
            Apply(new ModifierDef("DBG_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f));
        }

        [ContextMenu("调试/判定窗口收紧 0.6×（常驻）")]
        private void DebugTightWindows()
        {
            Apply(new ModifierDef("DBG_TIGHT", ModifierKind.JudgeWindowScale, 0.6f, 0f));
        }

        [ContextMenu("调试/判定线消失 3 秒")]
        private void DebugHideJudgeLine()
        {
            Apply(new ModifierDef("DBG_NOLINE", ModifierKind.JudgeLineHidden, 0f, 3f));
        }

        [ContextMenu("调试/谱面隐身 3 秒")]
        private void DebugHideNotes()
        {
            Apply(new ModifierDef("DBG_NONOTES", ModifierKind.NotesHidden, 0f, 3f));
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
                    case ModifierKind.NotesHidden:
                        notesVisible = false;
                        break;
                }
            }
            PlayRules.SetModifierState(scale, lineVisible, notesVisible);
        }
    }
}
