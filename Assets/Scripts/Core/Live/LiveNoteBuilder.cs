using System.Collections.Generic;
using UnityEngine;
using FallenAngel.Data;

namespace FallenAngel.Live
{
    /// <summary>
    /// 适配层：本工程的谱面数据（<see cref="NoteData"/>）→ 判定簇的音符树（<see cref="LiveNoteBase"/>）。
    ///
    /// 映射规则（照上游 SUS 解析的语义，只是字段来源换成我们的 v1/v2 谱面协议）：
    ///
    /// | 本工程 NoteData | 判定簇 | 说明 |
    /// | --- | --- | --- |
    /// | Normal | LiveNormalNote（Normal） | 按下边沿；wide → 跨度铺满全部轨道（= SUS 的 Width，任意键都能判） |
    /// | LongStart(+LongEnd/LongBody) | LiveLongNote（Long）+ 尾 LiveNormalNote（Long） | 头判 → 按住 → 尾判（尾时刻 = time + duration）；LongEnd/LongBody 只做视图 |
    /// | Slide | LiveLongNote（Long）+ 尾（路径终点） | 头的判定跨度沿"头→尾"插值推进（LiveLongNote + CalcExcuteNoteLane）；路径本身是视图 |
    /// | Drag | LiveFrictionNote（Friction） | 不要求按下边沿：手指在跨度内 + 窗口内 → 触到即按住到过线 → JustPerfect |
    /// | Flick | LiveFlickNote（Flick） | 滑动幅度 + 方向（上/下）；方向不满足 → FlickMiss 分支（Great，不 Miss） |
    ///
    /// 两处本工程口径（不是判定语义）：
    /// · 判定跨度外扩 <see cref="LiveConfig.DefaultLaneOffset"/> = 半条轨道（手指落在整条轨道内都算）；
    /// · 4 键鼓谱的 lane 0 沿用"底鼓 = 全宽"的旧语义（<paramref name="laneZeroIsWide"/>）。
    /// </summary>
    public static class LiveNoteBuilder
    {
        /// <summary>判定跨度外扩（轨）：0.5 = 手指落在整条轨道宽度内都算命中</summary>
        public const float DefaultLaneOffset = LiveConfig.DefaultLaneOffset;

        /// <summary>
        /// 甩键判定幅度（像素）：与旧行为同口径（max(18px, 屏高×1.2%)）。
        /// 原作从 SUS 的音符距离参数来，我们按屏幕算，手感不变。
        /// </summary>
        public static float DefaultFlickDistancePixels
        {
            get { return Mathf.Max(18f, Screen.height * 0.012f); }
        }

        /// <summary>
        /// 把整张谱面切成判定音符树。<paramref name="laneCount"/> 用于 wide 音符的跨度与坐标钳制。
        /// </summary>
        public static List<LiveNoteBase> Build(IReadOnlyList<NoteData> notes, int laneCount,
            float flickDistancePixels, bool laneZeroIsWide)
        {
            var result = new List<LiveNoteBase>();
            if (notes == null)
            {
                return result;
            }

            var count = Mathf.Max(1, laneCount);
            for (var i = 0; i < notes.Count; i++)
            {
                var data = notes[i];
                if (data == null)
                {
                    continue;
                }

                var root = BuildNote(data, i, count, flickDistancePixels, laneZeroIsWide);
                if (root != null)
                {
                    result.Add(root);
                }
            }

            return result;
        }

        private static LiveNoteBase BuildNote(NoteData data, int id, int laneCount,
            float flickDistancePixels, bool laneZeroIsWide)
        {
            var isWide = data.wide
                || (laneZeroIsWide && data.lane == 0 && (data.type == Data.NoteType.Normal || data.type == Data.NoteType.LongStart));
            var laneStart = isWide ? 0 : Mathf.Clamp(data.lane, 0, laneCount - 1);
            var laneEnd = isWide ? laneCount - 1 : laneStart;
            var info = Info(data.time);

            switch (data.type)
            {
                case Data.NoteType.Normal:
                    return new LiveNormalNote(info, id, laneStart, laneEnd,
                        NoteCategory.Normal, 1f, NoteType.Default, DefaultLaneOffset);

                case Data.NoteType.LongStart:
                    return BuildLongNote(data, id, laneStart, laneEnd, info);

                case Data.NoteType.Slide:
                    return BuildSlide(data, id, laneStart, laneEnd, laneCount, info);

                case Data.NoteType.Drag:
                    return new LiveFrictionNote(info, id, laneStart, laneEnd,
                        NoteCategory.Friction, NoteType.Default, 1f, DefaultLaneOffset);

                case Data.NoteType.Flick:
                    return new LiveFlickNote(info, id, laneStart, laneEnd,
                        NoteCategory.Flick, 1f, NoteType.Default, DirectionOf(data.direction),
                        DefaultLaneOffset, flickDistancePixels);

                default:
                    // LongBody / LongEnd：只是长按的视图部件，不参与判定（尾由头的 duration 生成）
                    return null;
            }
        }

        private static LiveNoteBase BuildLongNote(NoteData data, int id, int laneStart, int laneEnd,
            MusicScoreInfo info)
        {
            var head = new LiveLongNote(info, id, laneStart, laneEnd,
                NoteCategory.Long, NoteType.Default, 1f, DefaultLaneOffset);
            head.CompleteTailWhileHolding = true;   // 本工程语义：按住到尾巴 = 成功（见 LiveLongNote 注释）
            var endTime = data.time + Mathf.Max(0f, data.duration);
            var tail = new LiveNormalNote(Info(endTime), id, laneStart, laneEnd,
                NoteCategory.Normal, 1f, NoteType.Default, DefaultLaneOffset);
            head.AddNoteListAndSetupChildNote(tail);
            return head;
        }

        private static LiveNoteBase BuildSlide(NoteData data, int id, int laneStart, int laneEnd,
            int laneCount, MusicScoreInfo info)
        {
            var head = new LiveLongNote(info, id, laneStart, laneEnd,
                NoteCategory.Long, NoteType.Default, 1f, DefaultLaneOffset, NoteLineType.Linear);
            head.CompleteTailWhileHolding = true;   // 滑条同样：跟到尾巴就算成功

            var path = data.path;
            var endTime = data.time + Mathf.Max(0f, data.duration);
            var endLane = laneEnd;

            if (path != null && path.Count >= 2)
            {
                // 路径终点 = 判定跨度的终点（跨度由 CalcExcuteNoteLane 沿头→尾插值推进，见 LiveLongNote）
                endLane = Mathf.Clamp(Mathf.RoundToInt(path[path.Count - 1].x), 0, laneCount - 1);

                // 判定跨度按谱面 path 采样：玩家看到的引导线与判定用的跨度是同一条曲线（弯道不脱手）
                head.PathLaneSampler = progress01 => SamplePathLane(path, progress01);
            }

            var tail = new LiveNormalNote(Info(endTime), id, endLane, endLane,
                NoteCategory.Normal, 1f, NoteType.Default, DefaultLaneOffset);
            head.AddNoteListAndSetupChildNote(tail);
            return head;
        }

        /// <summary>本工程谱面没有 BPM/拍号（导入期已经积分成秒），这里只填 time</summary>
        public static MusicScoreInfo Info(float time)
        {
            return new MusicScoreInfo(0, 0f, time, 120f, 4f, 1f, 1f);
        }

        /// <summary>按进度（0..1 映射到路径总时长）线性插值采样路径的轨道坐标</summary>
        private static float SamplePathLane(IReadOnlyList<SlidePathPoint> path, float progress01)
        {
            if (path == null || path.Count == 0) return 0f;
            if (path.Count == 1) return path[0].x;

            float total = Mathf.Max(0.0001f, path[path.Count - 1].t);
            float t = Mathf.Clamp01(progress01) * total;
            for (var i = 0; i < path.Count - 1; i++)
            {
                var t0 = path[i].t;
                var t1 = path[i + 1].t;
                if (t >= t0 && t <= t1)
                {
                    return t1 > t0 ? Mathf.Lerp(path[i].x, path[i + 1].x, (t - t0) / (t1 - t0)) : path[i].x;
                }
            }
            return path[path.Count - 1].x;
        }

        private static NoteDirection DirectionOf(FlickDirection direction)
        {
            return direction == FlickDirection.Down ? NoteDirection.Down : NoteDirection.Up;
        }
    }
}
