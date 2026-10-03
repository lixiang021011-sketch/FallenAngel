#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏时间基自检：**SongTime 是怎么算出来的**，以及音画偏差的四个来源怎么叠加。
    ///
    /// 为什么需要：真机反馈"音画偏差非常大"。这条链上有四个量——
    ///   ① 谱面 `metadata.offset`（作者对齐用）
    ///   ② 判定偏移 `CalibrationSettings.TimingAdjustData`（**帧**，±20；照抄上游 LiveSettingData.TimingAdjustData，
    ///      加在音乐时间上 → 正数 = 音符提前）
    ///   ③ 音频**输出延迟** `AudioLatency`（AudioSource.time 给的是"送去播的位置"，
    ///      真正听到还要晚 DSP 缓冲 + 设备延迟）
    ///   ④ 输入时间戳外推 `InputClock`（触摸事件时刻 → 歌曲时间）
    /// 它们必须**同向、同一处**叠加，且画面（音符位置）与判定读同一条时钟。
    /// 这一层错了的症状就是"音符和鼓点差一截"，而且人手调不回来。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.PlayClockChecks.Run
    /// </summary>
    public static class PlayClockChecks
    {
        [MenuItem("Tools/FallenAngel/Validation/Gameplay/Validate Play Clock and Offsets", false, 504)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-30} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            float savedCalibration = CalibrationSettings.TimingAdjustData;
            bool savedLatencyEnabled = AudioLatency.Enabled;
            try
            {
                // ① 判定偏移（照抄上游：单位=帧、范围 ±20、帧→秒 ×1/60、正数 = 音符提前）
                CalibrationSettings.TimingAdjustData = 20f;
                bool framesMax = Mathf.Abs(CalibrationSettings.TimingAdjustSeconds - 20f / 60f) < 1e-5f;
                CalibrationSettings.TimingAdjustData = -20f;
                bool framesMin = Mathf.Abs(CalibrationSettings.TimingAdjustSeconds + 20f / 60f) < 1e-5f;
                CalibrationSettings.TimingAdjustData = 999f;                 // 越界 → 夹到 +20
                bool clampedHigh = Mathf.Abs(CalibrationSettings.TimingAdjustData - 20f) < 1e-5f;
                bool parsed = Mathf.Abs(CalibrationSettings.ParseClamped("abc", 7f) - 7f) < 1e-5f
                              && Mathf.Abs(CalibrationSettings.ParseClamped("-5.5", 0f) + 5.5f) < 1e-5f;
                check("判定偏移·帧/范围/换算（上游口径）",
                    framesMax && framesMin && clampedHigh && parsed,
                    string.Format("+20 帧={0:0.0}ms、-20 帧={1:0.0}ms、越界→{2}、解析失败保持原值={3}",
                        CalibrationSettings.TimingAdjustSeconds * 1000f, -20f / 60f * 1000f,
                        CalibrationSettings.TimingAdjustData, parsed));

                // ② 输出延迟补偿：DSP 缓冲可测；补偿量 = DSP + 设备估计；关掉 / 平台不需要时归零
                AudioLatency.Measure();
                float dsp = AudioLatency.DspBufferSeconds;
                float raw = AudioLatency.RawCompensationSeconds;
                AudioLatency.Enabled = false;
                float disabled = AudioLatency.CompensationSeconds;
                AudioLatency.Enabled = true;
                float onThisPlatform = AudioLatency.CompensationSeconds;
                check("输出延迟补偿（可测·可关·平台判断）",
                    dsp > 0f
                    && raw > AudioLatency.DeviceLatencyEstimateSeconds
                    && Mathf.Abs(disabled) < 1e-6f
                    && (!AudioLatency.PlatformNeedsCompensation
                            ? Mathf.Abs(onThisPlatform) < 1e-6f          // 编辑器：不补偿（DSP 值代表不了真机）
                            : Mathf.Abs(onThisPlatform - raw) < 1e-6f),  // 真机：补偿 = DSP + 设备估计
                    string.Format("DSP={0:0.0}ms、原始补偿={1:0.0}ms、关掉={2:0.0}ms、本平台生效={3:0.0}ms（平台需要={4}）",
                        dsp * 1000f, raw * 1000f, disabled * 1000f, onThisPlatform * 1000f,
                        AudioLatency.PlatformNeedsCompensation));

                // ④ 输入时间戳外推：事件比采样晚 30ms → 歌曲时间 +30ms；超出上限则钳住
                InputClock.Reset();
                InputClock.Sample(10f, 100.0);
                float ahead = InputClock.ToMusicTime(100.030);
                float clamped = InputClock.ToMusicTime(200.0);
                check("输入时间戳外推（含上限钳制）",
                    Mathf.Abs(ahead - 10.030f) < 1e-3f
                    && Mathf.Abs(clamped - (10f + InputClock.MaxDelta)) < 1e-3f,
                    string.Format("+30ms→{0:0.000}s、超限→{1:0.000}s（钳 {2:0.00}s）",
                        ahead, clamped, InputClock.MaxDelta));

                // ⑤ 未采样时不能算出离谱时间（切曲/开局瞬间的保护）
                InputClock.Reset();
                float beforeSample = InputClock.ToMusicTime(999.0);
                check("未采样时回退为 0（不产生离谱时间）",
                    Mathf.Abs(beforeSample) < 1e-6f,
                    string.Format("未采样 → {0:0.###}", beforeSample));

                // ⑥ 音频平滑时钟：单调不减 + seek 立即重同步（判定时间不能倒流）
                AudioClock.Reset(10.000f, 100.000);
                float a = AudioClock.Update(10.010f, 100.010);
                float b = AudioClock.Update(10.005f, 100.020);
                float c = AudioClock.Update(10.030f, 100.030);
                float seek = AudioClock.Update(20.000f, 100.040);
                check("音频平滑时钟（单调 + seek）",
                    b >= a - 1e-4f && c >= b - 1e-4f && Mathf.Abs(seek - 20f) < 1e-3f,
                    string.Format("{0:0.###}/{1:0.###}/{2:0.###}（回跳不退）→ seek {3:0.###}",
                        a, b, c, seek));

                // ⑦ 跟拍测量（玩家界面用）：剔极值后取平均——点歪的那两下不该把结果带跑
                var samples = new List<double> { 0.010, 0.012, 0.008, 0.011, -0.30, -0.31, 0.009, 0.013 };
                double trimmed = CalibrationMeasure.TrimmedMeanSeconds(samples);
                double sum = 0.0;
                for (int i = 0; i < samples.Count; i++) sum += samples[i];
                double naive = sum / samples.Count;
                check("跟拍测量·修剪平均抗离群",
                    Mathf.Abs((float)trimmed - 0.0105f) < 0.003f
                    && Mathf.Abs((float)(naive - trimmed)) > 0.02f,
                    string.Format("8 次含 -300/-310ms 误点：修剪平均={0:0.0}ms、不剔极值={1:0.0}ms",
                        trimmed * 1000.0, naive * 1000.0));

                // ⑧ 半拍之外的点击不计；毫秒↔帧换算与上游口径一致（1 帧=16.67ms）
                double off;
                bool nearBeat = CalibrationMeasure.TryAssignToBeat(10.02, 10.00, 0.6, out off)
                                && Mathf.Abs((float)off - 0.02f) < 1e-4f;
                bool farBeat = !CalibrationMeasure.TryAssignToBeat(10.40, 10.00, 0.6, out off);
                bool msFrames = CalibrationMeasure.FramesToMs(20f) == 333
                                && Mathf.Abs(CalibrationMeasure.MsToFrames(5f) - 0.3f) < 1e-4f;
                check("跟拍测量·半拍门控 + 毫秒↔帧",
                    nearBeat && farBeat && msFrames,
                    string.Format("+20ms 计入={0}、+400ms(>半拍)计入={1}、20 帧={2}ms、5ms={3:0.0} 帧",
                        nearBeat, !farBeat, CalibrationMeasure.FramesToMs(20f), CalibrationMeasure.MsToFrames(5f)));

            }
            finally
            {
                CalibrationSettings.TimingAdjustData = savedCalibration;
                AudioLatency.Enabled = savedLatencyEnabled;
                InputClock.Reset();
            }

            string summary = string.Format(
                "演奏时间基自检：{0} 项通过 / {1} 项异常"
                + "（判定：校准 ms↔秒 + 输出延迟补偿可测可关 + 平台判断 + 输入时间戳外推与钳制 + 音频平滑时钟）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[PlayClock] " + line);
            Debug.Log("[PlayClock] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/play_clock_check.txt", lines.ToArray());
                File.AppendAllText("Logs/play_clock_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayClock] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
