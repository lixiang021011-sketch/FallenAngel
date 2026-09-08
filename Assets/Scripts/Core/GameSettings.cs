using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 全局设置持久化（PlayerPrefs，仿 CalibrationSettings 模式）。
    /// 纯数据读写，不依赖 AudioManager；应用方在读写后自行刷新表现。
    /// </summary>
    public static class GameSettings
    {
        private const string SfxVolumeKey = "FA_SfxVolume";
        private const string HitEffectKey = "FA_HitEffectEnabled";

        /// <summary>音效音量 0~1（含按钮音；设置页调节入口）</summary>
        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, 0.7f));
            set => PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
        }

        /// <summary>按键特效开关（占位：门控触点涟漪；美术资源到位后扩展 LaneKeyVisual/判定特效）</summary>
        public static bool HitEffectEnabled
        {
            get => PlayerPrefs.GetInt(HitEffectKey, 1) == 1;
            set => PlayerPrefs.SetInt(HitEffectKey, value ? 1 : 0);
        }
    }
}
