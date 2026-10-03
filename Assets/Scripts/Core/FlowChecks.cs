#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 流程页自检（前端清单 E 组）。E1 倒计时先落地，后续 E2 结算 / E3 暂停退出 / E4 曲信息
    /// 的断言也加在这里（同一类"开局→演奏→暂停→结算"的流程约束）。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.FlowChecks.Run
    /// </summary>
    public static class FlowChecks
    {
        [MenuItem("Tools/FallenAngel/Validation/Visuals/Validate Flow Pages", false, 601)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                // ① E1 倒计时节奏：3/2/1 各 1 秒 + GO! 0.5 秒（上游 CountdownCore：每步 WaitForSeconds(1f)）
                check("countdown.timing",
                    Mathf.Abs(CountdownTiming.StepSeconds - 1f) < 1e-4f
                    && Mathf.Abs(CountdownTiming.GoSeconds - 0.5f) < 1e-4f
                    && Mathf.Abs(CountdownTiming.TotalSeconds - 3.5f) < 1e-4f,
                    string.Format("{0}→{1} 每步 {2:0.##}s + GO {3:0.##}s = 总 {4:0.##}s（上游口径）",
                        CountdownTiming.From, CountdownTiming.To, CountdownTiming.StepSeconds,
                        CountdownTiming.GoSeconds, CountdownTiming.TotalSeconds));

                // ② E1 步骤标签：3 / 2 / 1 / GO!
                check("countdown.labels",
                    CountdownTiming.LabelAt(0) == "3" && CountdownTiming.LabelAt(1) == "2"
                    && CountdownTiming.LabelAt(2) == "1" && CountdownTiming.LabelAt(3) == "GO!",
                    "step 0→3 = " + CountdownTiming.LabelAt(0) + "/" + CountdownTiming.LabelAt(1)
                    + "/" + CountdownTiming.LabelAt(2) + "/" + CountdownTiming.LabelAt(3));

                // ③ E1 曲线：缩放从 1.35 收到 1（单调）、透明度后段才淡出
                bool scaleMono = true;
                float prev = float.MaxValue;
                for (int i = 0; i <= 20; i++)
                {
                    float s = CountdownTiming.ScaleAt(i * 0.02f);
                    if (s > prev + 1e-4f) { scaleMono = false; break; }
                    prev = s;
                }
                check("countdown.curves",
                    Mathf.Abs(CountdownTiming.ScaleAt(0f) - 1.35f) < 1e-3f && scaleMono
                    && Mathf.Abs(CountdownTiming.ScaleAt(0.25f) - 1f) < 1e-3f
                    && Mathf.Abs(CountdownTiming.AlphaAt(0f) - 1f) < 1e-3f
                    && Mathf.Abs(CountdownTiming.AlphaAt(1f)) < 1e-3f,
                    "缩放 1.35→1.00（0.25s 内，单调）；透明度 1→0（后 25% 淡出）");

                // ④ E1 GO! 用强调色（不是普通白）
                check("countdown.goAccent",
                    !CountdownTiming.IsGo(2) && CountdownTiming.IsGo(3),
                    "step 2（数字 1）普通色；step 3（GO!）强调色");

                // ⑤ E2 结算分级：照上游 LiveResultAnimationType 的档位（优先级 全Perfect > 全连 > 生命归零 > 普通）
                check("result.classify",
                    ResultPresentation.Classify(true, true, false, false) == ResultAnimationType.AllPerfect
                    && ResultPresentation.Classify(false, true, false, false) == ResultAnimationType.FullCombo
                    && ResultPresentation.Classify(false, false, true, false) == ResultAnimationType.LifeZero
                    && ResultPresentation.Classify(false, false, false, false) == ResultAnimationType.Clear
                    && ResultPresentation.Classify(false, false, false, true) == ResultAnimationType.Failure,
                    "全Perfect→AllPerfect、全连→FullCombo、生命归零→LifeZero、其余→Clear、失败→Failure");

                // ⑥ E2 评级阈值（我们自己的分档；上游在 master data 里）：单调、边界正确
                check("result.rank",
                    ResultPresentation.RankOf(100f) == "S" && ResultPresentation.RankOf(98f) == "S"
                    && ResultPresentation.RankOf(97.9f) == "A" && ResultPresentation.RankOf(95f) == "A"
                    && ResultPresentation.RankOf(90f) == "B" && ResultPresentation.RankOf(80f) == "C"
                    && ResultPresentation.RankOf(79.9f) == "D",
                    "≥98 S / ≥95 A / ≥90 B / ≥80 C / 其余 D");

                // ⑦ E2 分数滚动：0 → 最终分、单调不回头、到点正好等于最终分
                int final = 1234567;
                bool rollOk = ResultPresentation.RollScore(final, 0f) == 0
                              && ResultPresentation.RollScore(final, ResultPresentation.ScoreRollSeconds) == final;
                int prevRoll = -1;
                for (int i = 0; i <= 20 && rollOk; i++)
                {
                    int v = ResultPresentation.RollScore(final, i * ResultPresentation.ScoreRollSeconds / 20f);
                    if (v < prevRoll) rollOk = false;
                    prevRoll = v;
                }
                check("result.scoreRoll", rollOk,
                    string.Format("0s → 0；{0:0.##}s → {1:N0}；过程中单调不回退",
                        ResultPresentation.ScoreRollSeconds, final));

                // ⑧ E2 逐项入场：第 i 项要等前面亮起，且全部项会在总时长内亮完
                bool revealOk = ResultPresentation.RevealAlpha(0, 0f) == 0f
                                && ResultPresentation.RevealAlpha(0, ResultPresentation.RevealFade) > 0.99f
                                && ResultPresentation.RevealAlpha(2, ResultPresentation.RevealStagger) < 0.01f
                                && ResultPresentation.RevealAlpha(2,
                                    2 * ResultPresentation.RevealStagger + ResultPresentation.RevealFade) > 0.99f;
                int count = 19;
                float total = ResultPresentation.TotalSeconds(count);
                revealOk = revealOk && ResultPresentation.RevealAlpha(count - 1, total) > 0.99f
                           && total > ResultPresentation.ScoreRollSeconds;
                check("result.reveal", revealOk,
                    string.Format("第 i 项在 i×{0:0.##}s 后开始淡入；{1} 项总时长 {2:0.##}s（不短于分数滚动）",
                        ResultPresentation.RevealStagger, count, total));

                // ⑨ E5 加载页：小贴士按曲名确定性选择、落在条数范围内、且同一首歌每次都一样
                int t1 = LoadingTip.TipIndex("Tell your world", "livetune");
                int t2 = LoadingTip.TipIndex("Tell your world", "livetune");
                int t3 = LoadingTip.TipIndex("Dear Stars", "DashyDesu");
                check("loading.tipDeterministic",
                    t1 == t2 && t1 >= 0 && t1 < LoadingTip.TipCount
                    && t3 >= 0 && t3 < LoadingTip.TipCount
                    && LoadingTip.TipKey(t1) == "loading.tip." + t1,
                    string.Format("同曲同贴士（{0}）、跨曲可不同（{1} vs {2}），范围 0..{3}",
                        t1, t1, t3, LoadingTip.TipCount - 1));

                // ⑩ E5 加载页展示窗口：必须 > 0（否则本地读谱太快，加载页只会闪一帧）
                check("loading.window", LoadingTip.LoadingSeconds > 0.2f,
                    string.Format("展示 {0:0.##}s 后进倒计时（上游是资源加载时长；我们固定窗口）",
                        LoadingTip.LoadingSeconds));
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }

            string summary = string.Format(
                "流程页自检：{0} 项通过 / {1} 项异常（判定：E1 倒计时节奏 3/2/1+GO + 标签 + 曲线 + GO 强调色）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Flow] " + line);
            Debug.Log("[Flow] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/flow_check.txt", lines.ToArray());
                File.AppendAllText("Logs/flow_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Flow] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
