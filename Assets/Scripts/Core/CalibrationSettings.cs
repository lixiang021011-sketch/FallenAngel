using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定偏移（校准）。**逐条照抄 OpenSekai**：
    ///
    /// | 上游 | 我们 |
    /// | --- | --- |
    /// | `LiveSettingData.TimingAdjustData`（`[Key("TimingAdjustData")]`，float，默认 0） | `TimingAdjustData`（PlayerPrefs `FA_TimingAdjustData`） |
    /// | `LiveConfig.DefaultNoteTiming = 0f` / `MinNoteTiming = -20` / `MaxNoteTiming = 20` | `DefaultTiming` / `MinTiming` / `MaxTiming` |
    /// | `BaseLiveController`：`timingAdjust = TimingAdjustData * 0.016667f`（**帧 → 秒**） | `TimingAdjustSeconds = TimingAdjustData * (1f/60f)` |
    /// | `currentAudioLatencyMusicTimeMs => Mathf.Max(timingAdjust + 音乐秒数, 0f)`（**加**在音乐时间上，正数 = 音符提前） | `GameManager.SongTime` 里同样**相加**并 clamp≥0 |
    /// | 设置页一行数字输入：`CreateInputField("判定偏移", "-20.0 - 20.0")`，`ParseClampedSetting(..., MinNoteTiming, MaxNoteTiming, ...)` | `SettingsPanelController` 的"判定偏移"输入行 |
    ///
    /// 注意**符号**：上游是"加在音乐时间上"，所以**正数 = 音符提前**；
    /// 本工程另一档 `AudioLatency`（输出延迟补偿）是"从音乐时间里减"，正数 = 音符推迟——两者方向相反，别混。
    /// 旧版（±200ms、减在 SongTime 上、跟拍点击测量）已废弃：那是我自己发明的口径，不是上游的。
    /// </summary>
    public static class CalibrationSettings
    {
        /// <summary>对应上游 `LiveConfig.DefaultNoteTiming`</summary>
        public const float DefaultTiming = 0f;

        /// <summary>对应上游 `LiveConfig.MinNoteTiming`（帧）</summary>
        public const int MinTiming = -20;

        /// <summary>对应上游 `LiveConfig.MaxNoteTiming`（帧）</summary>
        public const int MaxTiming = 20;

        private const string Key = "FA_TimingAdjustData";

        /// <summary>
        /// 判定偏移（**帧**，1/60 秒/帧）。范围 [MinTiming, MaxTiming]；正数 = 音符提前。
        /// </summary>
        public static float TimingAdjustData
        {
            get { return Clamp(PlayerPrefs.GetFloat(Key, DefaultTiming)); }
            set
            {
                PlayerPrefs.SetFloat(Key, Clamp(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>帧 → 秒（上游 `* 0.016667f`）</summary>
        public static float TimingAdjustSeconds
        {
            get { return TimingAdjustData * (1f / 60f); }
        }

        /// <summary>夹到上游范围（对应上游 `ParseClampedSetting(..., MinNoteTiming, MaxNoteTiming, ...)`）</summary>
        public static float Clamp(float frames)
        {
            return Mathf.Clamp(frames, MinTiming, MaxTiming);
        }

        /// <summary>显示格式（上游 `FormatSettingValue`：1 位小数）</summary>
        public static string Format(float frames)
        {
            return frames.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>解析输入（对应上游 `ParseClampedSetting`：解析失败保持原值，成功则夹取）</summary>
        public static float ParseClamped(string text, float fallback)
        {
            float parsed;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                return Clamp(fallback);
            }
            return Clamp(parsed);
        }
    }
}
