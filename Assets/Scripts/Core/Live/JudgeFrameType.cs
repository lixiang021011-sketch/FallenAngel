namespace FallenAngel.Live
{
    /// <summary>
    /// 判定窗口表用的键：把"音符类别 × 是否 critical × 判头/判尾"的 19 种组合枚举出来。
    /// 逐字取自 OpenSekai Sekai/JudgeFrameType.cs —— LiveConfig 的窗口表与
    /// LiveUtility.GetINoteToJudgeFrameType 都按它查表，是"每类音符各自一套窗口"的载体。
    /// </summary>
    public enum JudgeFrameType
    {
        Undefined = 0,
        Normal = 1,
        Flick = 2,
        Long = 3,
        Long_End = 4,
        Long_End_Flick = 5,
        Normal_Critical = 6,
        Flick_Critical = 7,
        Long_Critical = 8,
        Long_End_Critical = 9,
        Long_End_Flick_Critical = 10,
        Friction = 11,
        Friction_Critical = 12,
        Friction_Flick = 13,
        Friction_Flick_Critical = 14,
        Friction_Long = 15,
        Friction_Long_Critical = 16,
        Friction_Long_End = 17,
        Friction_Long_End_Critical = 18
    }
}
