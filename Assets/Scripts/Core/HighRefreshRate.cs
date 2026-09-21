using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 帧率策略与"当前实际刷新率"读数。
    ///
    /// 背景（2026-09-21 真机定位"帧率不够"）：
    /// vSync 开启时 `Application.targetFrameRate` 会被忽略，真正决定帧率的是**显示模式**——
    /// 而 Android（MIUI/HyperOS 尤其明显）默认按"普通内容"给应用 60Hz，即使面板支持 120Hz。
    /// 之前这里写的是 `targetFrameRate = Clamp(Screen.currentResolution.refreshRate, 60, 120)`：
    /// 设备当前若是 60Hz 模式，读到 60 → 自己把自己锁在 60，永远上不去 120。
    ///
    /// 现在改成：**无条件要 120**（60Hz 设备上不生效、无副作用），并把"显示模式 Hz / 实测 fps"
    /// 打进日志（`[Perf]` 行）——判断"到底有没有跑上 120"看这两个数就够了。
    ///
    /// 想进一步强制面板进 120Hz 模式，需要在 Android 侧调 `Surface.setFrameRate` /
    /// `preferredDisplayModeId`（Unity 的 JNI 模块 `com.unity.modules.androidjni` 本工程没启用，
    /// 按铁律"不新增 Package"不动包）：那一档属于机型/项目设置层面，
    /// 排查结论与建议写在 Logs/live_port_report.md。
    /// </summary>
    public static class HighRefreshRate
    {
        /// <summary>期望帧率上限（60Hz 设备上会被 vSync 压回 60）</summary>
        public const int TargetHz = 120;

        /// <summary>设备/显示模式当前报告的刷新率（Hz）</summary>
        public static int CurrentHz { get; private set; }

        public static void Request()
        {
            Application.targetFrameRate = TargetHz;
            CurrentHz = Mathf.RoundToInt(Screen.currentResolution.refreshRate);
            Debug.Log(string.Format(
                "[HighRefreshRate] 目标 {0}fps；显示模式报告 {1}Hz（vSync={2}，maxQueuedFrames={3}）",
                TargetHz, CurrentHz, QualitySettings.vSyncCount, QualitySettings.maxQueuedFrames));
        }

        /// <summary>刷新"当前 Hz"读数（日志用）</summary>
        public static int SampleCurrentHz()
        {
            CurrentHz = Mathf.RoundToInt(Screen.currentResolution.refreshRate);
            return CurrentHz;
        }
    }
}
