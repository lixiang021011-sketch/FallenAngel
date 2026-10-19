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
    /// 谱面各类型音符数量统计（v2 协议 noteCounts，加载侧校验用）
    /// </summary>
    [System.Serializable]
    public class NoteCounts
    {
        public int tap;
        public int hold;
        public int drag;
        public int flick;
        public int slide;
    }

    /// <summary>
    /// 谱面事件（v2 协议 events 通道；type 存字符串保前向兼容，如 "bpm"/"speed"）
    /// </summary>
    [System.Serializable]
    public class ChartEvent
    {
        public float time;
        public string type;
        public float value;
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
        /// <summary>
        /// 分轨（可选）：填了就走"多轨同时播放 + 当前演奏轨增强"，不填就是单条混音（旧路径）。
        /// 每条轨对应一个声部名，顺序与谱面声部表对齐。
        /// </summary>
        public List<SongStem> stems;
        /// <summary>
        /// 本谱面演奏的声部名（可选；留空 = 单声部谱面，不做"同曲切谱"）。
        /// 同一首歌的多张切谱共用 audioFileName，各自填自己的声部名（与 SongStem.partName /
        /// PartPalette.KnownParts 对齐，如 Bass / Drums / Guitar）。
        /// </summary>
        public string partName;
        /// <summary>切换生效时机（可选；缺省 = 立即生效、不限间隔）</summary>
        public PartSwitchConfig switchTiming;
        public float previewStartTime;  // 试听开始时间
        public float previewDuration;   // 试听持续时间
        public int formatVersion;       // 谱面格式版本（v1 缺失=0；v2=2）
        /// <summary>
        /// 键数（可选）。缺省 0 = 按版本推断：v2 → 5 键、v1 → 4 键（旧谱不受影响）。
        /// 4K 外部谱经独立工程 chart_adapter（osu_adapter.py）导入时按源谱键数写 4 或 5，lane 原样。
        /// </summary>
        public int laneCount;
        public NoteCounts noteCounts;   // 各类型数量统计（v2，校验用）
        /// <summary>导入来源信息（可选；只做记录，不参与游戏逻辑）。手写/旧谱为空。</summary>
        public ChartSourceMetadata sourceMetadata;
    }

    /// <summary>外部谱面导入时保留的来源信息（osu_adapter 写入；编辑器保存时原样写回）。</summary>
    [System.Serializable]
    public class ChartSourceMetadata
    {
        public string format;           // 如 "osu!mania"；空 = 没有来源信息
        public string fileFormat;       // 源文件格式版本行
        public string sourceFileName;
        /// <summary>源文件 sha256：确认当前谱来自哪份原稿；原稿本体存在 chart_adapter 工程的 chart_sources/&lt;sha256&gt;.osu</summary>
        public string sourceSha256;
        /// <summary>导入工具与版本（如 "osu_adapter/3"）</summary>
        public string importer;
        /// <summary>生成器（osu 的 Creator，如 "Mapperatorinator"）与版本（Tags 里的 model=…）</summary>
        public string generator;
        public string generatorVersion;
        /// <summary>导入时间（ISO 8601，UTC）。唯一允许每次导入都不同的字段，比较/复现时忽略</summary>
        public string importedAt;
        public string title;
        public string titleUnicode;
        public string artist;
        public string creator;
        public string version;
        /// <summary>源文件里的生成参数原文（Mapperatorinator 写在 osu 的 Tags：模型、seed、温度等）</summary>
        public string tags;
        /// <summary>源谱第一拍所在的音频时间（秒）——生成器自己估的，只作来源记录，不能当评价网格</summary>
        public float beatOffset;
        public List<ChartSourceTimingPoint> timingPoints;
        /// <summary>导入时被剔除/修正的数量（逐条明细在导入报告）</summary>
        public ChartImportStats importStats;
    }

    /// <summary>导入统计：转换过程中被剔除或修正的对象数量</summary>
    [System.Serializable]
    public class ChartImportStats
    {
        public int removedDuplicate;
        public int removedInvalidHold;
        public int removedNegativeTime;
        public int unsupportedObjects;
        public int unparsableLines;
        public int laneClamped;
        public int notesInsideHold;
    }

    /// <summary>源谱的非继承时间点（秒 / 四分音符 BPM / 每小节四分音符数）</summary>
    [System.Serializable]
    public class ChartSourceTimingPoint
    {
        public float time;
        public float bpm;
        public int meter;
    }

    /// <summary>
    /// 完整谱面数据
    /// </summary>
    [System.Serializable]
    public class ChartData
    {
        public ChartMetadata metadata;
        public List<NoteData> notes;
        public List<ChartEvent> events; // v2 events 通道（v1 谱为 null，读取处判空）

        public ChartData()
        {
            metadata = new ChartMetadata();
            notes = new List<NoteData>();
            events = new List<ChartEvent>();
        }

        /// <summary>
        /// 键数：metadata.laneCount 填了就用它（1–5）；否则 v2 协议为 5 键 lane 0-4，
        /// v1 谱 formatVersion 缺失=0 → 4 键
        /// </summary>
        public int LaneCount
        {
            get
            {
                if (metadata == null) return 4;
                if (metadata.laneCount > 0) return Mathf.Clamp(metadata.laneCount, 1, 5);
                return metadata.formatVersion >= 2 ? 5 : 4;
            }
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
                if (note.type == NoteType.LongStart || note.type == NoteType.Slide)
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
