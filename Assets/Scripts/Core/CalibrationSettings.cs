using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 节拍校准设置（全局，PlayerPrefs 持久化）。
    /// 语义：offset 增大 → SongTime 变小 → 音符整体推迟到达判定线。
    /// 正偏移用于补偿设备音频输出延迟（声音比画面晚的情况）。
    /// GameManager 读取时钟时叠加在谱面 metadata.offset 之上。
    /// </summary>
    public static class CalibrationSettings
    {
        private const string OffsetKey = "FA_CalibrationOffsetMs";

        /// <summary>校准偏移（毫秒，可正可负）</summary>
        public static int OffsetMs
        {
            get => PlayerPrefs.GetInt(OffsetKey, 0);
            set
            {
                PlayerPrefs.SetInt(OffsetKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>校准偏移（秒）</summary>
        public static float OffsetSeconds => OffsetMs / 1000f;
    }
}
