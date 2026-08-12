using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Data
{
    /// <summary>
    /// 谱面难度枚举
    /// </summary>
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,
        Expert
    }

    /// <summary>
    /// 谱面元数据
    /// </summary>
    [System.Serializable]
    public class ChartMetadata
    {
        public string songName;         // 歌曲名
        public string songArtist;       // 艺术家
        public string chartAuthor;      // 谱面作者
        public Difficulty difficulty;   // 难度
        public int level;               // 等级数字 1-20
        public float bpm;               // BPM
        public float offset;            // 音频偏移（秒，用于谱面对齐）
        public string audioFileName;    // 音频文件名（不含扩展名）
        public float previewStartTime;  // 试听开始时间
        public float previewDuration;   // 试听持续时间
    }

    /// <summary>
    /// 完整谱面数据
    /// </summary>
    [System.Serializable]
    public class ChartData
    {
        public ChartMetadata metadata;
        public List<NoteData> notes;

        public ChartData()
        {
            metadata = new ChartMetadata();
            notes = new List<NoteData>();
        }

        /// <summary>
        /// 按时间排序所有音符
        /// </summary>
        public void SortNotes()
        {
            notes.Sort((a, b) =>
            {
                int timeCompare = a.time.CompareTo(b.time);
                if (timeCompare != 0)
                    return timeCompare;
                return a.lane.CompareTo(b.lane);
            });
        }

        /// <summary>
        /// 获取谱面总时长（最后一个音符的时间 + 尾判缓冲）
        /// </summary>
        public float GetTotalDuration()
        {
            if (notes == null || notes.Count == 0)
                return 0f;

            float maxTime = 0f;
            foreach (var note in notes)
            {
                float endTime = note.time;
                if (note.type == NoteType.LongStart)
                    endTime += note.duration;
                if (endTime > maxTime)
                    maxTime = endTime;
            }
            return maxTime + 2f; // 加2秒缓冲
        }

        /// <summary>
        /// 获取各类型音符数量统计
        /// </summary>
        public (int normalCount, int longCount) GetNoteCounts()
        {
            int normal = 0;
            int longNoteStarts = 0;
            foreach (var note in notes)
            {
                if (note.type == NoteType.Normal) normal++;
                else if (note.type == NoteType.LongStart) longNoteStarts++;
            }
            return (normal, longNoteStarts);
        }
    }
}
