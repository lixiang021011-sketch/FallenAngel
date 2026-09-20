using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定窗口适用的音符种类。判定窗口从此**按种类查表**，而不是全局一套。
    ///
    /// 参考依据（2026-09-20，OpenSekai 复刻 PJKS 的做法）：原作把判定窗口按
    /// Normal / Flick / Long / Long_End(尾巴) 各一行，**且每档都分前(早)/后(晚)两个值**
    /// （后判普遍更宽容）。本表把这一结构搬过来，数值仍用我们已验收的 Phigros 口径
    /// （±80/160/180/200ms 对称），所以默认手感不变；差别是"以后要单独调某一类"只需改表。
    /// </summary>
    public enum JudgeNoteKind
    {
        Normal = 0,     // 普通单点
        Wide = 1,       // 全宽横条（kick 语义，任意键触发）
        Drag = 2,       // 拖键（碰即 Perfect）
        Flick = 3,      // 滑动
        Slide = 4,      // 滑条头
        HoldHead = 5,   // 长按头（只触发视觉，不计分）
        HoldEnd = 6,    // 长按/滑条尾（释放点，计分）
    }

    /// <summary>判定时机提示（对应原作的 Fast / Late / Just）。仅用于显示，不参与判定。</summary>
    public enum JudgeTimingHint
    {
        None = 0,
        Just = 1,
        Fast = 2,   // 按早了
        Late = 3,   // 按晚了
    }

    /// <summary>
    /// 单个音符种类的判定窗口（秒）。Before = 早于音符时刻的容差，After = 晚于的容差。
    /// 口袋约定：`timeDiff = 歌曲时间 − 音符时刻`，**正值 = 早**，负值 = 晚。
    /// </summary>
    [System.Serializable]
    public struct JudgeWindowRow
    {
        public float perfectBefore, perfectAfter;
        public float greatBefore, greatAfter;
        public float goodBefore, goodAfter;
        public float badBefore, badAfter;

        /// <summary>JUST 档半径（±，秒）。0 = 不区分 JUST（默认，保持现有手感）。</summary>
        public float justBand;

        public static JudgeWindowRow Symmetric(float perfect, float great, float good, float bad)
        {
            return new JudgeWindowRow
            {
                perfectBefore = perfect, perfectAfter = perfect,
                greatBefore = great, greatAfter = great,
                goodBefore = good, goodAfter = good,
                badBefore = bad, badAfter = bad,
                justBand = 0f,
            };
        }

        /// <summary>是否退化（全 0）：序列化丢字段或新建空表时用它回退到对称口径</summary>
        public bool IsDegenerate
        {
            get
            {
                return perfectBefore <= 0f && perfectAfter <= 0f
                    && greatBefore <= 0f && greatAfter <= 0f
                    && goodBefore <= 0f && goodAfter <= 0f
                    && badBefore <= 0f && badAfter <= 0f;
            }
        }

        /// <summary>按四档取容差：0=Perfect 1=Great 2=Good 3=Bad。timeDiff 正 = 早 → 用 Before</summary>
        public float Bound(float timeDiff, int grade)
        {
            bool early = timeDiff >= 0f;
            switch (grade)
            {
                case 0: return early ? perfectBefore : perfectAfter;
                case 1: return early ? greatBefore : greatAfter;
                case 2: return early ? goodBefore : goodAfter;
                default: return early ? badBefore : badAfter;
            }
        }
    }

    /// <summary>
    /// 判定窗口表。默认所有种类都是同一套对称值（= 历史 Phigros 口径），
    /// 因此引入本表**不改变**任何现有判定结果；改某一类只需 Set 那一行。
    /// </summary>
    [System.Serializable]
    public class JudgeWindowTable
    {
        private const int KindCount = 7;

        /// <summary>按 JudgeNoteKind 索引；长度 = KindCount</summary>
        public JudgeWindowRow[] rows = new JudgeWindowRow[KindCount];
        public bool initialized;

        public static JudgeWindowTable CreateDefault(float perfect = 0.080f, float great = 0.160f,
                                                     float good = 0.180f, float bad = 0.200f)
        {
            var table = new JudgeWindowTable();
            var row = JudgeWindowRow.Symmetric(perfect, great, good, bad);
            for (int i = 0; i < KindCount; i++) table.rows[i] = row;
            table.initialized = true;
            return table;
        }

        public JudgeWindowRow Get(JudgeNoteKind kind)
        {
            int i = (int)kind;
            if (rows == null || i < 0 || i >= rows.Length) return default;
            return rows[i];
        }

        public void Set(JudgeNoteKind kind, JudgeWindowRow row)
        {
            if (rows == null || rows.Length != KindCount) rows = new JudgeWindowRow[KindCount];
            int i = (int)kind;
            if (i < 0 || i >= rows.Length) return;
            rows[i] = row;
            initialized = true;
        }

        public void CopyFrom(JudgeWindowTable source)
        {
            if (source == null) return;
            if (rows == null || rows.Length != KindCount) rows = new JudgeWindowRow[KindCount];
            for (int i = 0; i < KindCount; i++) rows[i] = source.Get((JudgeNoteKind)i);
            initialized = source.initialized;
        }

        /// <summary>整表按倍率缩放（modifier 用；只缩放容差，不动 justBand）</summary>
        public void Scale(float factor)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
            {
                JudgeWindowRow r = rows[i];
                r.perfectBefore *= factor; r.perfectAfter *= factor;
                r.greatBefore *= factor; r.greatAfter *= factor;
                r.goodBefore *= factor; r.goodAfter *= factor;
                r.badBefore *= factor; r.badAfter *= factor;
                rows[i] = r;
            }
        }

        /// <summary>
        /// 判定。退化行（全 0）回退到调用方给的对称口径，避免序列化丢表后判定全 Miss。
        /// </summary>
        public JudgeResultType Judge(float timeDiff, JudgeNoteKind kind, JudgeWindowRow fallback)
        {
            JudgeWindowRow row = Get(kind);
            if (row.IsDegenerate) row = fallback;
            float early = timeDiff >= 0f ? 1f : -1f;
            for (int grade = 0; grade < 4; grade++)
            {
                float bound = row.Bound(timeDiff, grade);
                if (bound <= 0f) continue;
                if (timeDiff * early <= bound) return GradeToResult(grade);
            }
            return JudgeResultType.Miss;
        }

        public JudgeTimingHint Describe(float timeDiff, JudgeNoteKind kind, JudgeWindowRow fallback)
        {
            JudgeWindowRow row = Get(kind);
            if (row.IsDegenerate) row = fallback;
            if (row.justBand > 0f && Mathf.Abs(timeDiff) <= row.justBand) return JudgeTimingHint.Just;
            return timeDiff > 0f ? JudgeTimingHint.Fast : JudgeTimingHint.Late;
        }

        private static JudgeResultType GradeToResult(int grade)
        {
            switch (grade)
            {
                case 0: return JudgeResultType.Perfect;
                case 1: return JudgeResultType.Great;
                case 2: return JudgeResultType.Good;
                default: return JudgeResultType.Bad;
            }
        }

        /// <summary>由音符数据推出判定种类（宽键优先于普通/长按头）</summary>
        public static JudgeNoteKind KindOf(NoteData data, bool isWide)
        {
            if (data == null) return JudgeNoteKind.Normal;
            switch (data.type)
            {
                case NoteType.Drag: return JudgeNoteKind.Drag;
                case NoteType.Flick: return JudgeNoteKind.Flick;
                case NoteType.Slide: return JudgeNoteKind.Slide;
                case NoteType.LongEnd: return JudgeNoteKind.HoldEnd;
                case NoteType.LongStart: return isWide ? JudgeNoteKind.Wide : JudgeNoteKind.HoldHead;
                default: return isWide ? JudgeNoteKind.Wide : JudgeNoteKind.Normal;
            }
        }
    }
}
