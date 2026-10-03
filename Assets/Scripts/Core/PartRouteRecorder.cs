using System.Collections.Generic;
using System.Text;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>一次演奏里的一段声部：供给、命中、失误、能量、失血。</summary>
    [System.Serializable]
    public struct PartSegment
    {
        public string part;
        public float start;
        public float end;
        /// <summary>该段窗口内该声部谱面供给的头音符数（长音记头，尾判的奖励算在收尾那一段）</summary>
        public int offered;
        public int hits;
        public int misses;
        /// <summary>该段的判定分布：高密度段是否真的掉档，看这三个数</summary>
        public int perfect;
        public int great;
        public int good;
        public float energy;
        public int lifeLost;

        public float Seconds { get { return end - start; } }
        public float HitRate { get { return offered > 0 ? (float)hits / offered : 0f; } }
    }

    /// <summary>
    /// 声部路线记录：把"切到哪、每段打得多好、赚了多少"变成可读对象。
    /// 存在的理由：没有它，关于"是否形成路线 / 高密度是否真的更难"的结论只能靠感觉，
    /// 而感觉在这类问题上最容易骗自己（见 2026-10-03 的原型待验证清单）。
    /// </summary>
    public sealed class PartRouteRecorder
    {
        private readonly List<PartSegment> segments = new List<PartSegment>();
        private readonly List<NoteData> currentSource = new List<NoteData>();
        private readonly List<float> switchTimes = new List<float>();
        private PartSegment open;
        private bool hasOpen;

        public IReadOnlyList<PartSegment> Segments { get { return segments; } }
        public IReadOnlyList<float> SwitchTimes { get { return switchTimes; } }
        /// <summary>除了第一段之外每开一段算一次切换</summary>
        public int SwitchCount { get { return switchTimes.Count; } }
        public float TotalEnergy { get; private set; }
        public int TotalMisses { get; private set; }

        /// <summary>开新段并结算上一段；source 是该声部的完整谱面（用它统计"供给"）</summary>
        public void OpenAt(string part, float songTime, ChartData source)
        {
            if (hasOpen)
            {
                Close(songTime);
                switchTimes.Add(songTime);
            }
            currentSource.Clear();
            if (source != null && source.notes != null)
            {
                for (int i = 0; i < source.notes.Count; i++)
                {
                    NoteData n = source.notes[i];
                    if (n == null || n.type == NoteType.LongBody || n.type == NoteType.LongEnd) continue;
                    currentSource.Add(n);
                }
            }
            open = new PartSegment { part = part, start = songTime };
            hasOpen = true;
        }

        /// <summary>关掉当前段；未开段时是空操作</summary>
        public void Close(float songTime)
        {
            if (!hasOpen) return;
            open.end = songTime;
            int offered = 0;
            for (int i = 0; i < currentSource.Count; i++)
            {
                float t = currentSource[i].time;
                if (t >= open.start && t < songTime) offered++;
            }
            open.offered = offered;
            segments.Add(open);
            hasOpen = false;
        }

        public void NoteHit(float energy, JudgeResultType result)
        {
            if (!hasOpen) return;
            open.hits++;
            open.energy += energy;
            TotalEnergy += energy;
            switch (result)
            {
                case JudgeResultType.Perfect: open.perfect++; break;
                case JudgeResultType.Great: open.great++; break;
                case JudgeResultType.Good: open.good++; break;
            }
        }

        public void NoteMiss(int damage)
        {
            if (!hasOpen) return;
            open.misses++;
            open.lifeLost += damage;
            TotalMisses++;
        }

        /// <summary>紧凑路线串：A 0–11.8 → B 11.8–20.4 → …</summary>
        public string RouteLine()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(segments[i].part).Append(' ')
                  .Append(segments[i].start.ToString("0.0")).Append('–')
                  .Append(segments[i].end.ToString("0.0")).Append('s');
            }
            return sb.ToString();
        }

        /// <summary>每段一行 + 合计一行；结算页与 Console 共用同一份文本</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                PartSegment s = segments[i];
                if (i > 0) sb.Append('\n');
                // 段窗口是 [起点, 终点)：正好压在切换时刻上的音符算给新段，所以老段可能"供 0 中 1"，
                // 这种情况不印百分比（分母为 0 的比率没有意义）。
                string rate = s.offered > 0 ? string.Format("({0:0}%)", s.HitRate * 100f) : "";
                sb.Append(string.Format("[声部] {0} {1:0.0}–{2:0.0}s 供{3} 中{4}{5} 能量{6:0.0} 失误{7} 失血{8}（P{9} G{10} Gd{11}）",
                    s.part, s.start, s.end, s.offered, s.hits, rate, s.energy, s.misses, s.lifeLost,
                    s.perfect, s.great, s.good));
            }
            if (segments.Count > 0) sb.Append('\n');
            sb.Append(string.Format("[声部] 切换 {0} 次 · 总能量 {1:0.0} · 总失误 {2}",
                SwitchCount, TotalEnergy, TotalMisses));
            return sb.ToString();
        }
    }
}
