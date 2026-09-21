namespace FallenAngel.Live
{
    /// <summary>Flick 方向。逐字取自 OpenSekai Sekai/Live/NoteDirection.cs。</summary>
    public enum NoteDirection
    {
        Default = 0,
        Left = 1,
        Right = 2,

        // ↓ 本工程扩展（原作没有）：谱面协议（v2）只用上/下两个甩键方向。
        //   取值接在原有三项之后，不改变原作语义。
        Up = 3,
        Down = 4
    }
}
