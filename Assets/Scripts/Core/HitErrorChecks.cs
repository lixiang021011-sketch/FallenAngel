#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 命中误差 → 建议偏移 的自检（照抄 osu! 的口径，见 `HitErrorTracker` 类注释的逐条对照表）。
    ///
    /// 为什么要单独一组：这条链上有三个容易搞错的点 ——
    ///   ① **中位数**（不是平均）：一局里点歪的两下不能把建议值带跑；
    ///   ② **符号**：我们采的是"触摸时刻 − 音符时刻"（正 = 打晚），而上游 `TimingAdjustData`
    ///      正 = 音符提前 → 建议 = 当前 − 误差。搞反了会越校越偏；
    ///   ③ **单位**：内部帧（±20）、显示 ms（1/60 秒 = 16.7ms/帧）。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.HitErrorChecks.Run
    /// </summary>
    public static class HitErrorChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Hit Error Offset Suggestion")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // 自检会写 PlayerPrefs 里的样本历史 → 先备份，结束还原（别把玩家的历史洗掉）
            string savedHistory = PlayerPrefs.GetString("FA_HitErrorHistory", string.Empty);
            float savedTiming = CalibrationSettings.TimingAdjustData;

            try
            {
                // ① 中位数
                var odd = new List<float> { 30f, -10f, 20f };
                check("median.odd", Mathf.Abs(HitErrorTracker.MedianMs(odd) - 20f) < 1e-4f,
                    "30/-10/20 → 20");

                var even = new List<float> { 10f, 40f };
                check("median.even", Mathf.Abs(HitErrorTracker.MedianMs(even) - 25f) < 1e-4f,
                    "10/40 → 25（中间两个取平均）");

                var outliers = new List<float> { -200f, 18f, 20f, 22f, 400f };
                check("median.outlierResistant", Mathf.Abs(HitErrorTracker.MedianMs(outliers) - 20f) < 1e-4f,
                    "-200/18/20/22/400 → 20（平均会是 52，中位数不被带跑）");

                check("median.empty", Mathf.Abs(HitErrorTracker.MedianMs(new List<float>())) < 1e-6f, "空表 → 0");

                // ② 单局门槛（osu! 是 50，我们是 15，见 HitErrorTracker 差异①）
                var tooFew = new List<float>();
                for (int i = 0; i < HitErrorTracker.MinJudgeCountPerPlay - 1; i++) tooFew.Add(10f);
                HitErrorTracker.Sample ignored;
                check("gate.tooFew",
                    !HitErrorTracker.TryCreateSample(tooFew, 0f, out ignored),
                    (HitErrorTracker.MinJudgeCountPerPlay - 1) + " 次判定 → 不出样本");

                var enough = new List<float>(tooFew) { 10f };
                check("gate.enough",
                    HitErrorTracker.TryCreateSample(enough, 0f, out ignored),
                    HitErrorTracker.MinJudgeCountPerPlay + " 次判定 → 出样本");

                // ③ 符号：打晚（正误差）→ 建议偏移变小（正 = 音符提前，所以变小 = 音符晚到）
                var lateErrors = new List<float>();
                for (int i = 0; i < HitErrorTracker.MinJudgeCountPerPlay; i++) lateErrors.Add(50f);
                HitErrorTracker.Sample lateSample;
                HitErrorTracker.TryCreateSample(lateErrors, 0f, out lateSample);
                check("sign.lateShrinksOffset",
                    lateSample.SuggestedFrames < -0.5f
                    && Mathf.Abs(HitErrorTracker.FramesToMs(lateSample.SuggestedFrames) + 50f) < 0.1f,
                    "中位 +50ms（打晚） + 当前 0 帧 → 建议 " +
                    HitErrorTracker.FramesToMs(lateSample.SuggestedFrames).ToString("0.0") + "ms");

                var earlyErrors = new List<float>();
                for (int i = 0; i < HitErrorTracker.MinJudgeCountPerPlay; i++) earlyErrors.Add(-50f);
                HitErrorTracker.Sample earlySample;
                HitErrorTracker.TryCreateSample(earlyErrors, 0f, out earlySample);
                check("sign.earlyGrowsOffset",
                    earlySample.SuggestedFrames > 0.5f,
                    "中位 −50ms（打早） → 建议 " +
                    HitErrorTracker.FramesToMs(earlySample.SuggestedFrames).ToString("0.0") + "ms");

                // 当前偏移参与计算：建议 = 当前 − 误差
                HitErrorTracker.Sample shifted;
                HitErrorTracker.TryCreateSample(lateErrors, 10f, out shifted);
                check("sign.usesCurrentOffset",
                    Mathf.Abs(shifted.SuggestedFrames - (10f - HitErrorTracker.MsToFrames(50f))) < 1e-3f,
                    "当前 10 帧 + 中位 +50ms → 建议 " + shifted.SuggestedFrames.ToString("0.00") + " 帧");

                // ④ 夹取到上游范围 ±20 帧
                var hugeErrors = new List<float>();
                for (int i = 0; i < HitErrorTracker.MinJudgeCountPerPlay; i++) hugeErrors.Add(5000f);
                HitErrorTracker.Sample huge;
                HitErrorTracker.TryCreateSample(hugeErrors, 0f, out huge);
                check("clamp.toMinFrames",
                    Mathf.Abs(huge.SuggestedFrames - CalibrationSettings.MinTiming) < 1e-3f,
                    "中位 +5000ms → 夹到 " + huge.SuggestedFrames.ToString("0.0") + " 帧（上游 MinNoteTiming）");

                // ⑤ 单位换算
                check("unit.msToFrames",
                    Mathf.Abs(HitErrorTracker.MsToFrames(1000f) - 60f) < 1e-3f
                    && Mathf.Abs(HitErrorTracker.FramesToMs(60f) - 1000f) < 1e-3f,
                    "1000ms = 60 帧（上游 1/60 秒口径）");

                // ⑥ 历史：容量 50、丢最旧、建议值取平均
                HitErrorHistory.Clear();
                for (int i = 0; i < HitErrorTracker.MaxHistory + 10; i++)
                {
                    HitErrorHistory.Add(new HitErrorTracker.Sample(i, 0f));
                }
                check("history.cap",
                    HitErrorHistory.Count == HitErrorTracker.MaxHistory,
                    "加 " + (HitErrorTracker.MaxHistory + 10) + " 个 → 留 " + HitErrorHistory.Count + " 个");

                var all = HitErrorHistory.All;
                check("history.dropsOldest",
                    Mathf.Abs(all[0].MedianErrorMs - 10f) < 1e-3f
                    && Mathf.Abs(all[all.Count - 1].MedianErrorMs - (HitErrorTracker.MaxHistory + 9)) < 1e-3f,
                    "最旧的 10 个被丢掉（首个 median = " + all[0].MedianErrorMs.ToString("0") + "）");

                HitErrorHistory.Clear();
                HitErrorHistory.Add(new HitErrorTracker.Sample(20f, 4f));
                HitErrorHistory.Add(new HitErrorTracker.Sample(40f, 4f));
                float suggest;
                bool hasSuggest = HitErrorHistory.TrySuggestFrames(out suggest);
                float expected = CalibrationSettings.Clamp(
                    (new HitErrorTracker.Sample(20f, 4f).SuggestedFrames
                     + new HitErrorTracker.Sample(40f, 4f).SuggestedFrames) * 0.5f);
                check("history.average",
                    hasSuggest && Mathf.Abs(suggest - expected) < 1e-3f,
                    "两个样本 → 建议取平均 = " + suggest.ToString("0.00") + " 帧");

                // ⑦ 持久化往返（跨局累计，重启游戏也在）
                HitErrorHistory.Clear();
                HitErrorHistory.Add(new HitErrorTracker.Sample(12.5f, 3.25f));
                HitErrorHistory.Add(new HitErrorTracker.Sample(-8f, -1.5f));
                HitErrorHistory.InvalidateCache();
                var reloaded = HitErrorHistory.All;
                check("persist.roundTrip",
                    reloaded.Count == 2
                    && Mathf.Abs(reloaded[0].MedianErrorMs - 12.5f) < 1e-2f
                    && Mathf.Abs(reloaded[1].OffsetFrames + 1.5f) < 1e-2f,
                    "写盘再读：count=" + reloaded.Count
                    + " first=" + reloaded[0].MedianErrorMs.ToString("0.##") + "ms/"
                    + reloaded[0].OffsetFrames.ToString("0.##") + "帧");

                HitErrorHistory.Clear();
                check("history.clear", HitErrorHistory.Count == 0, "清空后 count=0");

                float none;
                check("history.emptyNoSuggest", !HitErrorHistory.TrySuggestFrames(out none), "空历史 → 没有建议值");

                // ⑧ 「已经准确」门限（osu! 的 |建议 − 当前| < 1ms）
                check("correct.withinThreshold",
                    HitErrorTracker.IsAlreadyCorrect(0f, HitErrorTracker.MsToFrames(0.5f)),
                    "差 0.5ms → 判为已准确");
                check("correct.outsideThreshold",
                    !HitErrorTracker.IsAlreadyCorrect(0f, HitErrorTracker.MsToFrames(2f)),
                    "差 2ms → 仍给建议");
            }
            finally
            {
                // 还原玩家的历史与设定
                if (string.IsNullOrEmpty(savedHistory)) PlayerPrefs.DeleteKey("FA_HitErrorHistory");
                else PlayerPrefs.SetString("FA_HitErrorHistory", savedHistory);
                CalibrationSettings.TimingAdjustData = savedTiming;
                HitErrorHistory.InvalidateCache();
                PlayerPrefs.Save();
            }

            string summary = string.Format(
                "命中误差建议偏移自检：{0} 项通过 / {1} 项异常"
                + "（判定：中位数抗离群 + 单局门槛 {2} + 符号（打晚→建议变小）+ 夹取 ±{3} 帧"
                + " + 历史容量 {4} 丢最旧 + 跨局持久化 + 已准确门限）",
                pass, fail, HitErrorTracker.MinJudgeCountPerPlay, CalibrationSettings.MaxTiming,
                HitErrorTracker.MaxHistory);
            foreach (string line in lines) Debug.Log("[HitError] " + line);
            Debug.Log("[HitError] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/hit_error_check.txt", lines.ToArray());
                File.AppendAllText("Logs/hit_error_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HitError] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
