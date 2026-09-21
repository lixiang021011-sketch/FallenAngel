namespace FallenAngel.Live
{
    /// <summary>
    /// 谱面时刻信息（音符自带音乐位置 + 已解析时间 + 当时的 BPM/拍号/流速）。
    /// 逐字取自 OpenSekai Sekai/Live/MusicScoreInfo.cs——判定与视图都按 musicTime 查它。
    /// </summary>
    public struct MusicScoreInfo
    {
        public int bar;
        public float barProgress;
        public float time;
        public float bpm;
        public float timeSignature;
        public float speedRatio;
        public float seVolume;

        public MusicScoreInfo(int bar, float barProgress, float time, float bpm,
                              float timeSignature, float speedRatio, float seVolume)
        {
            this.bar = bar;
            this.barProgress = barProgress;
            this.time = time;
            this.bpm = bpm;
            this.timeSignature = timeSignature;
            this.speedRatio = speedRatio;
            this.seVolume = seVolume;
        }
    }
}
