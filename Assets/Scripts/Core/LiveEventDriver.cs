using System;
using System.Collections.Generic;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏中的**谱面事件派发**（我们 v2 协议的 `events` 通道，`ChartEvent{time,type,value}`）。
    ///
    /// 为什么要它：上游的 Fever 就是**谱面事件**（`Sekai/Live/FeverBeginEvent.cs`、
    /// `FeverStartEvent.cs`、`FeverEndEvent.cs`，都是 `EventBase` + `MusicScoreInfo`），
    /// 而我们这条 events 通道此前**没有任何消费者**（`JudgeManager` 只读 notes）。
    /// 现在把它接上：Fever 走 `fever.*`，以后的局内道具/技能 cut-in 走 `cutin.*` / `item.*`，
    /// 都只需要在这里 Register 一个处理器，判定层不用改。
    ///
    /// 时间基准：一律用 `GameManager.SongTime`（AGENTS §5），暂停时自然冻结。
    /// </summary>
    public sealed class LiveEventDriver
    {
        private readonly Dictionary<string, Action<ChartEvent>> handlers =
            new Dictionary<string, Action<ChartEvent>>(StringComparer.Ordinal);

        private readonly List<ChartEvent> events = new List<ChartEvent>();
        private int cursor;

        /// <summary>换谱/重开：重新装载事件表并把游标归零（调用方随后自行 Reset 各子系统状态）。</summary>
        public void Load(IReadOnlyList<ChartEvent> chartEvents)
        {
            events.Clear();
            cursor = 0;
            if (chartEvents == null) return;

            for (int i = 0; i < chartEvents.Count; i++)
            {
                ChartEvent e = chartEvents[i];
                if (e == null || string.IsNullOrEmpty(e.type)) continue;
                events.Add(e);
            }
            events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        public void Register(string type, Action<ChartEvent> handler)
        {
            if (string.IsNullOrEmpty(type) || handler == null) return;
            handlers[type] = handler;
        }

        public int PendingCount { get { return events.Count - cursor; } }

        /// <summary>把 `time &lt;= songTime` 的事件按序派发掉（每帧调用；事件只派发一次）。</summary>
        public void Advance(float songTime)
        {
            while (cursor < events.Count && events[cursor].time <= songTime)
            {
                ChartEvent e = events[cursor];
                cursor++;

                Action<ChartEvent> handler;
                if (handlers.TryGetValue(e.type, out handler) && handler != null)
                {
                    handler(e);
                }
            }
        }
    }
}
