namespace FallenAngel.Live
{
    /// <summary>
    /// 一类音符的判定窗口（Perfect/Great/Good/Bad 各自的前后时间，单位：秒）。
    /// 逐字取自 OpenSekai Sekai/Live/JudgeTimeData.cs 的数据面（唯一差别：省掉 MasterIngameJudgeFrame
    /// 构造器——那是原作从 master data 建表的入口，本工程在 LiveConfig.LoadMasterConfig 里按帧数建）。
    /// </summary>
    public struct JudgeTimeData
    {
        public JudgeFrameType NoteType;

        public float PerfectBeforeJudgeTime;

        public float PerfectAfterJudgeTime;

        public float GreatBeforeJudgeTime;

        public float GreatAfterJudgeTime;

        public float GoodBeforeJudgeTime;

        public float GoodAfterJudgeTime;

        public float BadBeforeJudgeTime;

        public float BadAfterJudgeTime;

        public JudgeTimeData(
            JudgeFrameType noteType,
            float perfectBeforeJudgeTime, float perfectAfterJudgeTime,
            float greatBeforeJudgeTime, float greatAfterJudgeTime,
            float goodBeforeJudgeTime, float goodAfterJudgeTime,
            float badBeforeJudgeTime, float badAfterJudgeTime)
        {
            NoteType = noteType;
            PerfectBeforeJudgeTime = perfectBeforeJudgeTime;
            PerfectAfterJudgeTime = perfectAfterJudgeTime;
            GreatBeforeJudgeTime = greatBeforeJudgeTime;
            GreatAfterJudgeTime = greatAfterJudgeTime;
            GoodBeforeJudgeTime = goodBeforeJudgeTime;
            GoodAfterJudgeTime = goodAfterJudgeTime;
            BadBeforeJudgeTime = badBeforeJudgeTime;
            BadAfterJudgeTime = badAfterJudgeTime;
        }
    }
}
