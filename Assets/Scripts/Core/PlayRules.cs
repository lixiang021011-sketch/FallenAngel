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

        /// <summary>判定窗口倍率（1 = 原始；&gt;1 更宽松，&lt;1 更严格）</summary>
        public static float JudgeWindowScale { get { return judgeWindowScale; } }

        /// <summary>判定线是否可见（只影响渲染，判定逻辑不受影响）</summary>
        public static bool JudgeLineVisible { get { return judgeLineVisible; } }

        /// <summary>音符是否可见（位置与判定照常，只影响渲染）</summary>
        public static bool NotesVisible { get { return notesVisible; } }

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
        internal static void SetModifierState(float windowScale, bool lineVisible, bool notesVisibleNow)
        {
            judgeWindowScale = Mathf.Max(0.05f, windowScale);
            judgeLineVisible = lineVisible;
            notesVisible = notesVisibleNow;
            dirty = true;
        }

        /// <summary>回到无修正状态（切歌、退出演奏、清空效果时调用）</summary>
        public static void ResetModifiers()
        {
            SetModifierState(1f, true, true);
        }

        private static void Refresh()
        {
            effectiveWindows.perfectWindow = baseWindows.perfectWindow * judgeWindowScale;
            effectiveWindows.greatWindow = baseWindows.greatWindow * judgeWindowScale;
            effectiveWindows.goodWindow = baseWindows.goodWindow * judgeWindowScale;
            effectiveWindows.badWindow = baseWindows.badWindow * judgeWindowScale;
            dirty = false;
        }
    }
}
