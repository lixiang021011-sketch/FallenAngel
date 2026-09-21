namespace FallenAngel.Live
{
    /// <summary>判定结果档位。逐字取自 OpenSekai Sekai/Live/NoteResult.cs（含 JustPerfect / Pass / Auto）。</summary>
    public enum NoteResult
    {
        None = 0,
        Miss = 1,
        Pass = 2,
        Bad = 3,
        Auto = 4,
        Good = 5,
        Great = 6,
        Perfect = 7,
        JustPerfect = 8
    }
}
