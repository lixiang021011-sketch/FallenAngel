using System.Collections.Generic;

namespace FallenAngel.Live
{
    /// <summary>
    /// 音符判定接口。逐字取自 OpenSekai Sekai/Live/INote.cs（仅改命名空间）。
    ///
    /// 这是判定簇的契约面：LiveLogic 只认这个接口，不认我们的 NoteData/Note。
    /// 我们的音符将来通过适配器实现它（LaneStart/LaneEnd/JudgeLaneStart/JudgeLaneEnd
    /// 是"手指落在跨度内即可判"的落点；ChildNote/PairNote 支撑摩擦链与同押）。
    /// </summary>
    public interface INote
    {
        MusicScoreInfo MusicScoreInfo { get; }

        float OffsetJudgeTime { get; }

        int DefaultLeftLane { get; }

        int DefaultRightLane { get; }

        int LaneStart { get; set; }

        int LaneEnd { get; set; }

        float LaneStartF { get; set; }

        float LaneEndF { get; set; }

        float JudgeLaneStart { get; set; }

        float JudgeLaneEnd { get; set; }

        float Progress { get; }

        NoteState State { get; }

        (NoteResult result, NoteResultDescription description) JudgeInfo { get; }

        NoteResult Result { get; }

        NoteCategory Category { get; }

        NoteType Type { get; }

        NoteDirection Direction { get; }

        NoteLineType LineType { get; }

        INote ParentNote { get; }

        INote ChildNote { get; }

        INote PairNote { get; }

        List<INote> ViewNoteList { get; set; }

        bool IsSkip { get; }

        int Id { get; }
    }
}
