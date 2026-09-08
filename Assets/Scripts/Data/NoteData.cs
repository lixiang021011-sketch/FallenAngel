using UnityEngine;

namespace FallenAngel.Data
{
    /// <summary>
    /// 音符类型枚举。
    /// 注意：0-3（v1 四键类型）顺序不可变更——v1 谱面 JSON 的 type 是整数直接映射；
    /// 4-6 为 v2 协议新增类型（tap→Normal、hold→LongStart/Body/End 由加载侧拆分）。
    /// </summary>
    public enum NoteType
    {
        Normal,     // 普通音符（单击）/ v2 tap
        LongStart,  // 长按音符头部 / v2 hold 拆分
        LongBody,   // 长按音符身体（辅助渲染用）
        LongEnd,    // 长按音符尾部
        Drag,       // v2 drag：碰即 Perfect，永不 MISS
        Flick,      // v2 flick：tap 判定 + 方向视觉
        Slide       // v2 slide：路径长按（头部判定 + 按住至尾）
    }

    /// <summary>
    /// Flick 方向（v2 协议 "up"/"down"）
    /// </summary>
    public enum FlickDirection
    {
        Up = 0,
        Down = 1
    }

    /// <summary>
    /// Slide 路径点（v2 协议：t 相对起点秒、单调递增，x 连续轨道坐标 0.0~4.0）
    /// </summary>
    [System.Serializable]
    public class SlidePathPoint
    {
        public float t;
        public float x;
    }

    /// <summary>
    /// 单条音符数据
    /// </summary>
    [System.Serializable]
    public class NoteData
    {
        /// <summary>音轨索引（v1: 0-3；v2: 0-4；Slide 取 path[0] 舍入后的轨）</summary>
        public int lane;

        /// <summary>音符出现的时间（秒，相对歌曲开始）</summary>
        public float time;

        /// <summary>音符类型</summary>
        public NoteType type;

        /// <summary>持续时间（秒，LongStart/Slide 有效）</summary>
        public float duration;

        /// <summary>长按音符所属的组ID（用于匹配头尾）</summary>
        public int longNoteId;

        /// <summary>Flick 方向（仅 Flick 有效）</summary>
        public FlickDirection direction;

        /// <summary>Slide 路径（仅 Slide 有效；首点 t=0，末点 t=duration）</summary>
        public System.Collections.Generic.List<SlidePathPoint> path;

        /// <summary>宽音符标记（v2 协议扩展：全宽视觉 + 任意键判定，kick 语义；v1 谱缺失 = false）</summary>
        public bool wide;

        public NoteData(int lane, float time, NoteType type = NoteType.Normal, float duration = 0f, int longNoteId = -1,
                        FlickDirection direction = FlickDirection.Up,
                        System.Collections.Generic.List<SlidePathPoint> path = null,
                        bool wide = false)
        {
            this.lane = lane;
            this.time = time;
            this.type = type;
            this.duration = duration;
            this.longNoteId = longNoteId;
            this.direction = direction;
            this.path = path;
            this.wide = wide;
        }
    }
}
