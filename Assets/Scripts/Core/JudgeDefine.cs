using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定结果类型
    /// </summary>
    public enum JudgeResultType
    {
        None,       // 未判定
        Perfect,    // 完美
        Great,      // 优秀
        Good,       // 良好
        Bad,        // 较差
        Miss        // 失误 - 超过时间窗
    }

    /// <summary>
    /// 判定窗口配置（秒）。2026-08-16 参考 Phigros 手感对齐：
    ///   Phigros 原版: Perfect ±80ms / Good ±160ms（65%分，保持连击）/ Bad ±180ms（0分，断连击）/ 之外 Miss
    ///   本作四档映射: Perfect ±80 / Great ±160（65%分，保持连击）/ Good ±180 / Bad ±200（0分，断连击）
    /// 相比原 ±50ms 更宽松，配合"早/晚"指示训练玩家校准手感（Phigros 移动端友好策略）。
    /// </summary>
    [System.Serializable]
    public class JudgeWindows
    {
        public float perfectWindow = 0.080f; // 80ms
        public float greatWindow = 0.160f;   // 160ms
        public float goodWindow = 0.180f;    // 180ms
        public float badWindow = 0.200f;     // 200ms
        // 超过 badWindow 的都算 Miss

        /// <summary>默认判定窗口共享实例（热路径复用避免重复分配；static 不参与序列化）</summary>
        public static readonly JudgeWindows Default = new JudgeWindows();

        /// <summary>
        /// 根据时间差计算判定结果
        /// </summary>
        public JudgeResultType Judge(float timeDiff)
        {
            float abs = Mathf.Abs(timeDiff);
            if (abs <= perfectWindow) return JudgeResultType.Perfect;
            if (abs <= greatWindow) return JudgeResultType.Great;
            if (abs <= goodWindow) return JudgeResultType.Good;
            if (abs <= badWindow) return JudgeResultType.Bad;
            return JudgeResultType.Miss;
        }

        /// <summary>
        /// 获取分数权重（Phigros 语义：Good/Bad 不给分）
        /// </summary>
        public static int GetScore(JudgeResultType type, bool isLongNote = false)
        {
            int baseScore = type switch
            {
                JudgeResultType.Perfect => 300,
                JudgeResultType.Great => 200,   // ≈Perfect 的 65%，对应 Phigros Good
                JudgeResultType.Good => 0,
                JudgeResultType.Bad => 0,
                _ => 0
            };
            // 长按音符给额外加成
            return isLongNote ? Mathf.RoundToInt(baseScore * 1.5f) : baseScore;
        }

        /// <summary>
        /// 该判定是否断连击（Phigros 语义：仅 Bad 级断连击，本作 Good/Bad 断）
        /// </summary>
        public static bool BreaksCombo(JudgeResultType type)
        {
            return type == JudgeResultType.Good || type == JudgeResultType.Bad || type == JudgeResultType.Miss;
        }

        /// <summary>
        /// 获取判定结果中文名
        /// </summary>
        public static string GetChineseName(JudgeResultType type)
        {
            return type switch
            {
                JudgeResultType.Perfect => "PERFECT",
                JudgeResultType.Great => "GREAT",
                JudgeResultType.Good => "GOOD",
                JudgeResultType.Bad => "BAD",
                JudgeResultType.Miss => "MISS",
                _ => ""
            };
        }
    }
}
