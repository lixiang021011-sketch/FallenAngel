using UnityEngine;

namespace FallenAngel.Data
{
    /// <summary>
    /// 音符类型枚举
    /// </summary>
    public enum NoteType
    {
        Normal,     // 普通音符（单击）
        LongStart,  // 长按音符头部
        LongBody,   // 长按音符身体（辅助渲染用）
        LongEnd     // 长按音符尾部
    }

    /// <summary>
    /// 单条音符数据
    /// </summary>
    [System.Serializable]
    public class NoteData
    {
        /// <summary>音轨索引 (0-3 对应四键)</summary>
        public int lane;

        /// <summary>音符出现的时间（秒，相对歌曲开始）</summary>
        public float time;

        /// <summary>音符类型</summary>
        public NoteType type;

        /// <summary>长按音符的持续时间（秒，仅 LongStart 有效）</summary>
        public float duration;

        /// <summary>长按音符所属的组ID（用于匹配头尾）</summary>
        public int longNoteId;

        public NoteData(int lane, float time, NoteType type = NoteType.Normal, float duration = 0f, int longNoteId = -1)
        {
            this.lane = lane;
            this.time = time;
            this.type = type;
            this.duration = duration;
            this.longNoteId = longNoteId;
        }
    }
}
