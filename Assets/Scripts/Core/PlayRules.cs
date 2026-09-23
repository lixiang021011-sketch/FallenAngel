using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 局内规则聚合（只读）。核心系统一律通过它读取"有效参数"，不直接读自己的序列化字段。
    ///
    /// 数据流：ModifierManager 施加/移除效果 → SetModifierState → Refresh → 核心系统读属性。
    /// 设计见 docs/architecture.md §9（基础值 + 修改器，禁止直接改核心系统的序列化字段）。
    ///
    /// 判定窗口用缓存实例重算（只在效果变化时分配一次），热路径直接读 <see cref="Windows"/>，不做分配。
    /// </summary>
    public static class PlayRules
    {
        private static JudgeWindows baseWindows = JudgeWindows.Default;
        private static readonly JudgeWindows effectiveWindows = new JudgeWindows();
        private static bool dirty = true;

        private static float judgeWindowScale = 1f;
        private static bool judgeLineVisible = true;
        private static bool notesVisible = true;
        private static float lifeDamageScale = 1f;
        private static float lifeRegenPerSecond;
        private static float scoreMultiplier = 1f;
        private static float lifeBonus;

        /// <summary>判定窗口倍率（1 = 原始；&gt;1 更宽松，&lt;1 更严格）</summary>
        public static float JudgeWindowScale { get { return judgeWindowScale; } }

        /// <summary>判定线是否可见（只影响渲染，判定逻辑不受影响）</summary>
        public static bool JudgeLineVisible { get { return judgeLineVisible; } }

        /// <summary>音符是否可见（位置与判定照常，只影响渲染）</summary>
        public static bool NotesVisible { get { return notesVisible; } }

        /// <summary>受伤倍率（1 = 原始；&lt;1 抗揍，&gt;1 更脆）。来源：ModifierManager 的 LifeDamageScale。</summary>
        public static float LifeDamageScale { get { return lifeDamageScale; } }

        /// <summary>每秒回血（可负 = 持续掉血）。来源：ModifierManager 的 LifeRegen。</summary>
        public static float LifeRegenPerSecond { get { return lifeRegenPerSecond; } }

        /// <summary>分数倍率（1 = 原始）。Fever 与局内道具都走这一档，判定层不硬编码。</summary>
        public static float ScoreMultiplier { get { return scoreMultiplier; } }

        /// <summary>开局生命加成（0 = 无）。来源：ModifierManager 的 LifeBonus；<see cref="PlayLife.Max"/> 把它加到基础上限上。</summary>
        public static float LifeBonus { get { return lifeBonus; } }

        /// <summary>有效判定窗口（缓存实例；调用方只读，不要改它的字段）</summary>
        public static JudgeWindows Windows
        {
            get
            {
                if (dirty) Refresh();
                return effectiveWindows;
            }
        }

        /// <summary>登记基础判定窗口（由 JudgeManager 在 Awake 时把自己的序列化实例交过来）</summary>
        public static void SetBaseWindows(JudgeWindows windows)
        {
            baseWindows = windows != null ? windows : JudgeWindows.Default;
            dirty = true;
        }

        /// <summary>由 ModifierManager 调用：写入当前生效的修正状态</summary>
        internal static void SetModifierState(float windowScale, bool lineVisible, bool notesVisibleNow,
            float damageScale, float regenPerSecond, float scoreMul, float lifeBonusNow)
        {
            judgeWindowScale = Mathf.Max(0.05f, windowScale);
            judgeLineVisible = lineVisible;
            notesVisible = notesVisibleNow;
            lifeDamageScale = Mathf.Max(0f, damageScale);
            lifeRegenPerSecond = regenPerSecond;
            scoreMultiplier = Mathf.Max(0f, scoreMul);
            lifeBonus = Mathf.Max(0f, lifeBonusNow);
            dirty = true;
        }

        /// <summary>回到无修正状态（切歌、退出演奏、清空效果时调用）</summary>
        public static void ResetModifiers()
        {
            SetModifierState(1f, true, true, 1f, 0f, 1f, 0f);
        }

        private static void Refresh()
        {
            effectiveWindows.perfectWindow = baseWindows.perfectWindow * judgeWindowScale;
            effectiveWindows.greatWindow = baseWindows.greatWindow * judgeWindowScale;
            effectiveWindows.goodWindow = baseWindows.goodWindow * judgeWindowScale;
            effectiveWindows.badWindow = baseWindows.badWindow * judgeWindowScale;
            // 按种类的细表同样要吃倍率——否则"判定窗口放宽"只对普通音符生效
            // （长按尾/滑动等走表，之前正是这类漏接）。
            effectiveWindows.CopyTableFrom(baseWindows);
            effectiveWindows.ScaleTable(judgeWindowScale);
            dirty = false;
        }
    }
}
