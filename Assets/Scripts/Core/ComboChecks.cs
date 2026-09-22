#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Core
{
    /// <summary>
    /// Combo 数字位 + AP 特效自检（前端清单 B3）。判据全部来自上游 `Sekai/ComboView.cs`：
    ///   · 数字位 `x = (digitCount - 1) * 0.475f + i * -0.95f`（i 从个位起）
    ///   · 出现 0.1s：`0.6 + clamp01(t/0.1) * 0.4`
    ///   · 爆发 0.1~0.2s：`1 + clamp01((t-0.1)/0.1) * 0.4`，透明度 `(1 - 同值) * 0.5`
    ///   · AP 呼吸：`(sin(t * 4.1887903) + 1) * 0.5`
    ///   · 同连击数不重播（`currentCombo == combo` 早退，在 `SetCombo` 里）
    ///
    /// batchmode：-executeMethod FallenAngel.Core.ComboChecks.Run
    /// </summary>
    public static class ComboChecks
    {
        [MenuItem("Tools/FallenAngel/Validate Combo and AP")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                // ① 位数
                check("digit.count",
                    ComboView.DigitCount(0) == 1 && ComboView.DigitCount(7) == 1
                    && ComboView.DigitCount(10) == 2 && ComboView.DigitCount(999) == 3
                    && ComboView.DigitCount(1234) == 4,
                    "0→1 位 / 7→1 / 10→2 / 999→3 / 1234→4（上游 Log10 公式）");

                // ② 布局：居中偏移 + 0.95 间距（i 从个位起 → 个位在最右）
                float x0 = ComboView.DigitX(0, 3);      // 个位
                float x1 = ComboView.DigitX(1, 3);
                float x2 = ComboView.DigitX(2, 3);      // 百位
                bool layout = Mathf.Abs(x0 - 0.95f) < 1e-5f && Mathf.Abs(x1) < 1e-5f
                              && Mathf.Abs(x2 + 0.95f) < 1e-5f
                              && Mathf.Abs(x0 + x1 + x2) < 1e-5f;   // 三位数整体居中
                check("digit.layout", layout,
                    string.Format("三位数：个位 {0:+0.00;-0.00} / 十位 {1:+0.00;-0.00} / 百位 {2:+0.00;-0.00}（间距 0.95、居中）",
                        x0, x1, x2));

                bool centered4 = Mathf.Abs(ComboView.DigitX(0, 4) + ComboView.DigitX(3, 4)) < 1e-5f;
                check("digit.centered", centered4,
                    "四位数两端对称（1234 不会偏右）");

                // ③ 出现曲线
                check("appear.curve",
                    Mathf.Abs(ComboView.AppearScale(0f) - 0.6f) < 1e-4f
                    && Mathf.Abs(ComboView.AppearScale(0.05f) - 0.8f) < 1e-4f
                    && Mathf.Abs(ComboView.AppearScale(0.1f) - 1f) < 1e-4f
                    && Mathf.Abs(ComboView.AppearScale(5f) - 1f) < 1e-4f,
                    "0s=0.6 → 0.05s=0.8 → 0.1s=1.0（之后保持）");

                // ④ 爆发曲线
                check("burst.curve",
                    Mathf.Abs(ComboView.BurstScale(0.05f)) < 1e-4f
                    && Mathf.Abs(ComboView.BurstScale(0.1f) - 1f) < 1e-4f
                    && Mathf.Abs(ComboView.BurstScale(0.15f) - 1.2f) < 1e-4f
                    && Mathf.Abs(ComboView.BurstScale(0.2f) - 1.4f) < 1e-4f
                    && Mathf.Abs(ComboView.BurstAlpha(0.1f) - 0.5f) < 1e-4f
                    && Mathf.Abs(ComboView.BurstAlpha(0.2f)) < 1e-4f,
                    "0.1s 起 1.0→1.4 倍、alpha 0.5→0（0.1s 前不显示）");

                // ⑤ AP 呼吸
                // 峰值在 sin = 1 处 → t = (π/2) / 角速度（写成 π 是零交叉，别搞错）
                float quarter = (Mathf.PI / 2f) / ComboView.ApEffectSpeed;
                float period = 2f * Mathf.PI / ComboView.ApEffectSpeed;
                check("ap.breathing",
                    Mathf.Abs(ComboView.ApEffectSpeed - 4.1887903f) < 1e-6f
                    && Mathf.Abs(ComboView.ApAlpha(0f) - 0.5f) < 1e-3f
                    && Mathf.Abs(ComboView.ApAlpha(quarter) - 1f) < 1e-3f,
                    "角速度 " + ComboView.ApEffectSpeed.ToString("0.000000") + " rad/s（周期 "
                    + period.ToString("0.00") + "s）；t=0 → 0.5、t=" + quarter.ToString("0.00") + "s 峰值 1.0");

                // ⑥ AP 门控：全 Perfect 才算（有 Great/Good/Bad/Miss 都不是）
                bool gate = ComboView.IsAllPerfectScore(12, 0, 0, 0, 0)
                            && !ComboView.IsAllPerfectScore(12, 1, 0, 0, 0)
                            && !ComboView.IsAllPerfectScore(12, 0, 0, 1, 0)
                            && !ComboView.IsAllPerfectScore(12, 0, 0, 0, 1)
                            && !ComboView.IsAllPerfectScore(0, 0, 0, 0, 0);
                check("ap.gate", gate,
                    "全 Perfect 才算 AP；出现任何 Great/Good/Bad/Miss 或还没判过都不算");

                // ⑦ 与生命槽/连击计数的口径一致：Miss 会同时断 AP 与扣血（这里只验 AP 断）
                check("ap.brokenByMiss",
                    !ComboView.IsAllPerfectScore(50, 0, 0, 0, 1),
                    "1 次 Miss → AP 立即失效（与 LifeView 的扣血同源判定）");

                // ⑧ 装配级：SetCombo 必须把"底板（父）"开关与定位，而不是只动文本（踩过这个 bug：
                //    只动文本 → 底板一直禁用 → 真机上连击数字整个不见了）
                GameObject host = new GameObject("__ComboViewSandbox", typeof(RectTransform));
                try
                {
                    ComboView view = host.AddComponent<ComboView>();
                    var plates = new Image[3];
                    var texts = new TextMeshProUGUI[3];
                    var numberRoot = new GameObject("Digits", typeof(RectTransform)).GetComponent<RectTransform>();
                    numberRoot.transform.SetParent(host.transform, false);

                    for (int i = 0; i < 3; i++)
                    {
                        GameObject plate = new GameObject("P" + i, typeof(RectTransform), typeof(Image));
                        plate.transform.SetParent(numberRoot.transform, false);
                        plates[i] = plate.GetComponent<Image>();
                        GameObject text = new GameObject("T" + i, typeof(RectTransform), typeof(TextMeshProUGUI));
                        text.transform.SetParent(plate.transform, false);
                        texts[i] = text.GetComponent<TextMeshProUGUI>();
                        plate.SetActive(false);
                    }

                    SetPrivate(view, "numberRoot", numberRoot);
                    SetPrivate(view, "digitPlates", plates);
                    SetPrivate(view, "digitTexts", texts);
                    SetPrivate(view, "digitWidth", 96f);

                    view.SetCombo(123, false);
                    bool[] active = { plates[0].gameObject.activeSelf, plates[1].gameObject.activeSelf,
                        plates[2].gameObject.activeSelf };
                    float px0 = ((RectTransform)plates[0].transform).anchoredPosition.x;
                    float px1 = ((RectTransform)plates[1].transform).anchoredPosition.x;
                    float px2 = ((RectTransform)plates[2].transform).anchoredPosition.x;
                    bool layoutOk = Mathf.Abs(px0 - 0.95f * 96f) < 0.5f && Mathf.Abs(px1) < 0.5f
                                    && Mathf.Abs(px2 + 0.95f * 96f) < 0.5f;
                    bool textOk = texts[0].text == "3" && texts[1].text == "2" && texts[2].text == "1";
                    check("view.platesActivated",
                        active[0] && active[1] && active[2] && layoutOk && textOk,
                        string.Format("底板全开={0}；三位数偏移 {1:0} / {2:0} / {3:0}px；文本=123→{4}{5}{6}",
                            active[0] && active[1] && active[2], px0, px1, px2,
                            texts[2].text, texts[1].text, texts[0].text));

                    view.SetCombo(0, false);
                    check("view.hiddenWhenZero",
                        !plates[0].gameObject.activeSelf && !plates[1].gameObject.activeSelf,
                        "连击归零 → 数字位全部隐藏");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-28} 自检异常：{1}", "执行", e));
            }

            string summary = string.Format(
                "Combo/AP 自检：{0} 项通过 / {1} 项异常"
                + "（判定：位数与 0.475/0.95 布局 + 0.1s 出现 + 0.1~0.2s 爆发 + AP 4.1887903 rad/s 呼吸 + AP 门控）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Combo] " + line);
            Debug.Log("[Combo] " + summary);

            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/combo_check.txt", lines.ToArray());
                File.AppendAllText("Logs/combo_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Combo] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>反射写私有 SerializeField（与 JudgeTimingChecks 的沙箱同一套做法）</summary>
        private static void SetPrivate(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (info == null) throw new InvalidOperationException("找不到字段：" + field);
            info.SetValue(target, value);
        }
    }
}
#endif
