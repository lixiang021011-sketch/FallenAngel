using System;
using System.Collections.Generic;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>同曲声部集合：复用原有 ChartData，所有声部共享键数、音频与 offset。</summary>
    public sealed class SongParts
    {
        private readonly ChartData[] charts;

        public ChartData Song { get { return charts.Length > 0 ? charts[0] : null; } }
        /// <summary>兼容旧用例的快捷读法（不认识就返回 null）</summary>
        public ChartData Guitar { get { return ChartOf("Guitar"); } }
        public ChartData Drums { get { return ChartOf("Drums"); } }
        public ChartData Bass { get { return ChartOf("Bass"); } }
        public float Duration { get; }
        /// <summary>
        /// 本曲可演奏的声部**顺序**（底部切换条按这个顺序出芯片；将来一首歌 5 件乐器就是 5 项）。
        /// 声部数量是内容属性，不是界面属性——界面按这个列表自适应。
        /// </summary>
        public string[] PartNames { get; }

        /// <summary>两件乐器的便捷构造（原型与自检大量在用）</summary>
        public SongParts(ChartData guitar, ChartData drums)
            : this(new[] { "Guitar", "Drums" }, new[] { guitar, drums }) { }

        /// <summary>
        /// 通用构造：名字表与谱面表一一对应，数量不限（2 件与 5 件走同一条路）。
        /// 校验口径不变：所有声部必须共享音频、偏移与键数——切换的前提是"同一首歌、同一时间轴"。
        /// </summary>
        public SongParts(string[] partNames, ChartData[] partCharts)
        {
            if (partNames == null || partCharts == null || partNames.Length == 0 ||
                partNames.Length != partCharts.Length)
                throw new ArgumentException("声部表与谱面表必须等长且非空");

            ChartData first = partCharts[0];
            if (first?.metadata == null || first.notes == null)
                throw new ArgumentException("声部必须使用相同音频、偏移和键数");

            float duration = 0f;
            for (int i = 0; i < partCharts.Length; i++)
            {
                ChartData chart = partCharts[i];
                if (chart?.metadata == null || chart.notes == null ||
                    chart.LaneCount != first.LaneCount ||
                    chart.metadata.audioFileName != first.metadata.audioFileName ||
                    Math.Abs(chart.metadata.offset - first.metadata.offset) > 0.0001f)
                    throw new ArgumentException("声部必须使用相同音频、偏移和键数");
                for (int j = 0; j < i; j++)
                    if (partNames[j] == partNames[i])
                        throw new ArgumentException("声部名不能重复：" + partNames[i]);
                duration = Math.Max(duration, chart.GetTotalDuration());
            }
            PartNames = partNames;
            charts = partCharts;
            Duration = duration;
        }

        /// <summary>按名字取该声部的谱面；不认识的名字返回 null（切换时直接拒绝）</summary>
        public ChartData ChartOf(string partName)
        {
            if (partName == null) return null;
            for (int i = 0; i < PartNames.Length; i++)
                if (PartNames[i] == partName) return charts[i];
            return null;
        }

        /// <summary>这张谱面是不是本集合里的某一员（同曲切谱：选哪张就从哪个声部开始）。</summary>
        public bool Contains(ChartData chart)
        {
            if (chart == null) return false;
            for (int i = 0; i < charts.Length; i++)
                if (ReferenceEquals(charts[i], chart)) return true;
            return false;
        }

        /// <summary>这张谱面对应的声部名；不在集合里返回 null。</summary>
        public string PartNameOf(ChartData chart)
        {
            if (chart == null) return null;
            for (int i = 0; i < charts.Length; i++)
                if (ReferenceEquals(charts[i], chart)) return PartNames[i];
            return null;
        }
    }

    /// <summary>一次演奏的声部和资源账本；不拥有时钟，不修改源谱。</summary>
    public sealed class SongPartSession
    {
        private readonly HashSet<NoteData> consumed = new HashSet<NoteData>();
        private readonly HashSet<NoteData> rewarded = new HashSet<NoteData>();
        public SongParts Definition { get; }
        public string PartName { get; private set; }
        public bool IsDrums => PartName == "Drums";
        public ChartData ActiveChart { get; private set; }
        public float Energy { get; private set; }
        public int HitCount { get; private set; }
        /// <summary>本次演奏的声部路线与分段账本（原型验证 A 项）</summary>
        public PartRouteRecorder Route { get; } = new PartRouteRecorder();
        /// <summary>当前生效的声部道具（原型用；正式内容里由局内构筑决定）</summary>
        public PartItem Item { get; set; } = PartItem.None;
        public event Action OnEnergyChanged;

        private float segmentStartTime;
        private int notesSinceSwitch;
        /// <summary>当前段已连续演奏的秒数（留守类道具的输入）</summary>
        public float SamePartSeconds(float songTime) => Math.Max(0f, songTime - segmentStartTime);
        /// <summary>切换后已经打掉几颗（换手类道具的输入）</summary>
        public int NotesSinceSwitch => notesSinceSwitch;

        public SongPartSession(SongParts definition, string initialPartName = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            string first = definition.PartNames.Length > 0 ? definition.PartNames[0] : null;
            string want = !string.IsNullOrEmpty(initialPartName) ? initialPartName : first;
            if (want != null && !Select(want, 0f) && first != null && want != first)
                Select(first, 0f);      // 声部名不认识 → 退回第一个声部，避免空谱面
        }

        /// <summary>
        /// 切到指定声部：只保留当前时间之后、未结算的头音符及其尾部；跨切换的长音静默取消。
        /// 返回 false = 名字不认识或已经是当前声部（调用方据此决定要不要发事件）。
        /// </summary>
        public bool Select(string partName, float songTime)
        {
            ChartData source = Definition.ChartOf(partName);
            if (source == null || partName == PartName) return false;
            PartName = partName;
            var selected = new ChartData { metadata = source.metadata, events = source.events };
            var longIds = new HashSet<int>();
            foreach (NoteData note in source.notes)
            {
                if (note.type == NoteType.LongEnd || note.type == NoteType.LongBody) continue;
                if (note.time < songTime || consumed.Contains(note)) continue;
                selected.notes.Add(note);
                if ((note.type == NoteType.LongStart || note.type == NoteType.Slide) && note.longNoteId >= 0)
                    longIds.Add(note.longNoteId);
            }
            foreach (NoteData note in source.notes)
                if ((note.type == NoteType.LongEnd || note.type == NoteType.LongBody) &&
                    longIds.Contains(note.longNoteId)) selected.notes.Add(note);
            selected.SortNotes();
            ActiveChart = selected;
            segmentStartTime = songTime;
            notesSinceSwitch = 0;
            Route.OpenAt(PartName, songTime, source);
            return true;
        }

        /// <summary>下一件声部（Tab / 单按钮循环用）。按谱面表的顺序循环，不是"两件对翻"。</summary>
        public string NextPartName()
        {
            string[] names = Definition.PartNames;
            if (names == null || names.Length == 0) return null;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == PartName) return names[(i + 1) % names.Length];
            return names[0];
        }

        /// <summary>头音符一旦接受判定就不可通过切回声部重放（包括提前命中）。</summary>
        public void Consume(NoteData root) { if (root != null) consumed.Add(root); }

        /// <summary>
        /// 每个谱面音符成功完成一次按"单位产率 × 倍率 × 道具倍率"结算；长音在尾判完成时奖励一次。
        /// songTime 只用来算留守时长——本工程的时钟唯一来源仍是 GameManager.SongTime。
        /// </summary>
        public void Reward(NoteData root, JudgeResultType result, float multiplier, float songTime)
        {
            if (root == null || (result != JudgeResultType.Perfect && result != JudgeResultType.Great &&
                result != JudgeResultType.Good) || !rewarded.Add(root)) return;
            Consume(root);
            HitCount++;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 1f;
            float itemMultiplier = PartEnergyRules.Compose(Item, PartName, SamePartSeconds(songTime), notesSinceSwitch);
            float gain = PartEnergyRules.Yield(root.type, result, multiplier, itemMultiplier);
            Energy += gain;
            Route.NoteHit(gain, result);
            notesSinceSwitch++;
            OnEnergyChanged?.Invoke();
        }

        /// <summary>漏掉一颗：只记账（生命扣减仍由判定层调用 PlayLife，避免两处各扣一次）</summary>
        public void Miss(NoteData root, int damage)
        {
            if (root == null) return;
            Route.NoteMiss(damage);
        }
    }
}
