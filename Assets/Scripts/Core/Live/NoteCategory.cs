namespace FallenAngel.Live
{
    /// <summary>
    /// 音符类别。逐字取自 OpenSekai Sekai/Live/NoteCategory.cs。
    ///
    /// 说明：作者要求"尽可能复刻核心玩法"，所以这张表**照抄**——虽然我们自己的 NoteType
    /// 只有 7 项、也不打算实现全部类别，但判定簇（INote / NoteBase / LiveLogic）按类别分派，
    /// 少一项就编译不过、或语义不同。
    /// </summary>
    public enum NoteCategory
    {
        Normal = 0,
        Long = 1,
        Connection = 2,
        Flick = 3,
        Friction = 4,
        FrictionHide = 5,
        FrictionLong = 6,
        FrictionHideLong = 7,
        FrictionFlick = 8,
        Guide = 9,
        GuideEnd = 10,
        GuideHidden = 11,
        Combo = 12,
        Hidden = 13,
        Skip = 14,
        Error = 15
    }
}
