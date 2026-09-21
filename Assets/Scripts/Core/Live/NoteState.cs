namespace FallenAngel.Live
{
    /// <summary>
    /// 音符判定状态。逐字取自 OpenSekai（MIT）的 Sekai/Live/NoteState.cs，
    /// 只改命名空间——判定簇（LiveLogic / NoteBase）依赖这套状态机，不能自行设计。
    /// </summary>
    public enum NoteState
    {
        Wait = 0,
        Playing = 1,
        InputBegan = 2,
        Input = 3,
        Release = 4,
        Last = 5,
        Done = 6
    }
}
