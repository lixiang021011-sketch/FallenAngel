using System;
using System.Collections.Generic;
using FallenAngel.Data;

namespace FallenAngel.Core
{
    /// <summary>同曲声部集合：复用原有 ChartData，所有声部共享键数、音频与 offset。</summary>
    public sealed class SongParts
    {
        public ChartData Song { get; }
        public ChartData Guitar { get; }
        public ChartData Drums { get; }
        public float Duration { get; }

        public SongParts(ChartData guitar, ChartData drums)
        {
            if (guitar?.metadata == null || drums?.metadata == null ||
                guitar.notes == null || drums.notes == null ||
                guitar.LaneCount != drums.LaneCount ||
                guitar.metadata.audioFileName != drums.metadata.audioFileName ||
                Math.Abs(guitar.metadata.offset - drums.metadata.offset) > 0.0001f)
                throw new ArgumentException("声部必须使用相同音频、偏移和键数");
            Guitar = guitar;
            Drums = drums;
            Song = guitar;
            Duration = Math.Max(guitar.GetTotalDuration(), drums.GetTotalDuration());
        }
    }

    /// <summary>一次演奏的声部和资源账本；不拥有时钟，不修改源谱。</summary>
    public sealed class SongPartSession
    {
        private readonly HashSet<NoteData> consumed = new HashSet<NoteData>();
        private readonly HashSet<NoteData> rewarded = new HashSet<NoteData>();
        public SongParts Definition { get; }
        public bool IsDrums { get; private set; }
        public string PartName => IsDrums ? "Drums" : "Guitar";
        public ChartData ActiveChart { get; private set; }
        public float Energy { get; private set; }
        public int HitCount { get; private set; }
        public event Action OnEnergyChanged;

        public SongPartSession(SongParts definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Select(false, 0f);
        }

        /// <summary>只保留当前时间之后、未结算的头音符及其尾部；跨切换的长音静默取消。</summary>
        public void Select(bool drums, float songTime)
        {
            IsDrums = drums;
            ChartData source = drums ? Definition.Drums : Definition.Guitar;
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
        }

        /// <summary>头音符一旦接受判定就不可通过切回声部重放（包括提前命中）。</summary>
        public void Consume(NoteData root) { if (root != null) consumed.Add(root); }

        /// <summary>每个谱面音符成功完成一次获 1×倍率；长音在尾判完成时奖励一次。</summary>
        public void Reward(NoteData root, JudgeResultType result, float multiplier)
        {
            if (root == null || (result != JudgeResultType.Perfect && result != JudgeResultType.Great &&
                result != JudgeResultType.Good) || !rewarded.Add(root)) return;
            Consume(root);
            HitCount++;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier)) multiplier = 1f;
            Energy += Math.Max(0f, multiplier);
            OnEnergyChanged?.Invoke();
        }
    }
}
