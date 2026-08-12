using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定结果类型
    /// </summary>
    public enum JudgeResultType
    {
        None,       // 未判定
        Perfect,    // 完美 - ±50ms
        Great,      // 优秀 - ±100ms
        Good,       // 良好 - ±150ms
        Bad,        // 较差 - ±200ms
        Miss        // 失误 - 超过时间窗
    }

    /// <summary>
    /// 判定窗口配置（毫秒）
    /// </summary>
    [System.Serializable]
    public class JudgeWindows
    {
        public float perfectWindow = 0.050f; // 50ms
        public float greatWindow = 0.100f;   // 100ms
        public float goodWindow = 0.150f;    // 150ms
        public float badWindow = 0.200f;     // 200ms
        // 超过 badWindow 的都算 Miss

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
        /// 获取分数权重
        /// </summary>
        public static int GetScore(JudgeResultType type, bool isLongNote = false)
        {
            int baseScore = type switch
            {
                JudgeResultType.Perfect => 300,
                JudgeResultType.Great => 200,
                JudgeResultType.Good => 100,
                JudgeResultType.Bad => 50,
                _ => 0
            };
            // 长按音符给额外加成
            return isLongNote ? Mathf.RoundToInt(baseScore * 1.5f) : baseScore;
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
