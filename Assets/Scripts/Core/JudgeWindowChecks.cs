#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Core
{
    /// <summary>
    /// 判定窗口表自检：按音符种类的窗口表结构、前后不对称、modifier 倍率覆盖、退化回退。
    ///
    /// 背景（2026-09-20，参考 OpenSekai 复刻 PJKS 的做法）：原作的判定窗口是
    /// **按音符类型一行 + 每档分前(早)/后(晚)** 的表，我们此前只有一套对称窗口。
    /// 本表引入时默认每行都等于历史值，所以**判定结果不变**；本自检既守住"默认没变"，
    /// 也守住"表真的能独立生效"，并覆盖一处真实漏接：长按尾释放曾经绕过 modifier。
    ///
    /// 只读 + 临时对象；菜单与 batchmode（-executeMethod FallenAngel.Core.JudgeWindowChecks.Run）两用。
    /// </summary>
    public static class JudgeWindowChecks
    {
        private const float Tol = 1e-4f;
        private static readonly Vector2 SpawnPos = new Vector2(0f, 1200f);

        [MenuItem("Tools/FallenAngel/Validate Judge Windows")]
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
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                RunCases(check);
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }
            finally
            {
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
            }

            string summary = string.Format(
                "判定窗口表自检：{0} 项通过 / {1} 项异常" +
                "（判定：默认口径不变 + 按种类独立 + 前后不对称 + modifier 覆盖全表 + 退化回退 + 时机提示 + 长按尾走有效窗口）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[JudgeWindow] " + line);
            Debug.Log("[JudgeWindow] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/judge_window_check.txt", lines.ToArray());
                File.AppendAllText("Logs/judge_window_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[JudgeWindow] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static void RunCases(Action<string, bool, string> check)
        {
            var windows = PlayRules.Windows;

            // ① 默认口径不变：Normal 仍走历史对称实现
            check("默认对称基准不变",
                Mathf.Abs(windows.perfectWindow - 0.080f) <= Tol
                && Mathf.Abs(windows.greatWindow - 0.160f) <= Tol
                && Mathf.Abs(windows.goodWindow - 0.180f) <= Tol
                && Mathf.Abs(windows.badWindow - 0.200f) <= Tol
                && windows.Judge(0.079f) == JudgeResultType.Perfect
                && windows.Judge(0.081f) == JudgeResultType.Great
                && windows.Judge(0.201f) == JudgeResultType.Miss,
                string.Format("0.08/0.16/0.18/0.20；±79ms=Perfect、81ms=Great、201ms=Miss"));

            // ② 七类都有行，且默认都等于对称基准（"引入表不改手感"的证据）
            int sameAsBase = 0;
            foreach (JudgeNoteKind kind in Enum.GetValues(typeof(JudgeNoteKind)))
            {
                JudgeWindowRow row = windows.table.Get(kind);
                bool same =
                    Mathf.Abs(row.perfectBefore - windows.perfectWindow) <= Tol
                    && Mathf.Abs(row.perfectAfter - windows.perfectWindow) <= Tol
                    && Mathf.Abs(row.greatBefore - windows.greatWindow) <= Tol
                    && Mathf.Abs(row.greatAfter - windows.greatWindow) <= Tol
                    && Mathf.Abs(row.goodBefore - windows.goodWindow) <= Tol
                    && Mathf.Abs(row.goodAfter - windows.goodWindow) <= Tol
                    && Mathf.Abs(row.badBefore - windows.badWindow) <= Tol
                    && Mathf.Abs(row.badAfter - windows.badWindow) <= Tol;
                if (same) sameAsBase++;
            }
            check("七类默认等同基准", sameAsBase == 7, string.Format("{0}/7 类与对称基准一致", sameAsBase));

            // ③ 前后不对称真的生效：Flick 前 50ms / 后 90ms
            var baseTable = new JudgeWindowTable();
            baseTable.CopyFrom(JudgeWindows.Default.table);
            baseTable.Set(JudgeNoteKind.Flick, new JudgeWindowRow
            {
                perfectBefore = 0.050f, perfectAfter = 0.090f,
                greatBefore = 0.120f, greatAfter = 0.180f,
                goodBefore = 0.180f, goodAfter = 0.200f,
                badBefore = 0.200f, badAfter = 0.220f,
            });
            var asymmetric = new JudgeWindows();
            asymmetric.CopyTableFrom(new JudgeWindows { table = baseTable });
            bool early60Great = asymmetric.Judge(0.060f, JudgeNoteKind.Flick) == JudgeResultType.Great;   // 超过 before
            bool late60Perfect = asymmetric.Judge(-0.060f, JudgeNoteKind.Flick) == JudgeResultType.Perfect; // 在 after 内
            check("前后不对称生效", early60Great && late60Perfect,
                string.Format("Flick 前 50ms/后 90ms：早 60ms={0}、晚 60ms={1}",
                    asymmetric.Judge(0.060f, JudgeNoteKind.Flick), asymmetric.Judge(-0.060f, JudgeNoteKind.Flick)));

            // ④ 每类独立：改 Flick 不影响 Normal / Drag
            check("按种类相互独立",
                asymmetric.Judge(0.060f, JudgeNoteKind.Normal) == JudgeResultType.Perfect
                && asymmetric.Judge(-0.060f, JudgeNoteKind.Normal) == JudgeResultType.Perfect
                && asymmetric.Judge(0.060f, JudgeNoteKind.Drag) == JudgeResultType.Perfect,
                "同刻 60ms 下 Normal/Drag 仍为 Perfect（未被 Flick 行污染）");

            // ⑤ modifier 倍率作用于**整张表**（原来只缩放 4 个对称字段）
            PlayRules.SetBaseWindows(JudgeWindows.Default);
            PlayRules.ResetModifiers();
            PlayRules.SetModifierState(1.5f, true, true);
            var scaled = PlayRules.Windows;
            JudgeWindowRow scaledFlick = scaled.table.Get(JudgeNoteKind.Flick);
            JudgeWindowRow scaledHoldEnd = scaled.table.Get(JudgeNoteKind.HoldEnd);
            check("modifier 覆盖整张表",
                Mathf.Abs(scaledFlick.badAfter - 0.300f) <= Tol
                && Mathf.Abs(scaledHoldEnd.badBefore - 0.300f) <= Tol
                && Mathf.Abs(scaled.perfectWindow - 0.120f) <= Tol,
                string.Format("1.5×：Flick.badAfter={0:0.###}、HoldEnd.badBefore={1:0.###}、Normal.perfect={2:0.###}",
                    scaledFlick.badAfter, scaledHoldEnd.badBefore, scaled.perfectWindow));

            // ⑥ 长按尾走**有效**窗口（回归：此前 JudgeLongRelease 读 JudgeWindows.Default，modifier 不生效）
            float releaseDiff = 0.250f;   // 基础口径 0.20 之外、1.5× 之后的 0.30 之内
            PlayRules.ResetModifiers();
            JudgeResultType atBase = JudgeLongReleaseResult(releaseDiff);
            PlayRules.SetModifierState(1.5f, true, true);
            JudgeResultType atScaled = JudgeLongReleaseResult(releaseDiff);
            PlayRules.ResetModifiers();
            check("长按尾吃 modifier",
                atBase == JudgeResultType.Miss && atScaled == JudgeResultType.Good,
                string.Format("释放偏差 250ms：基础口径={0}、1.5× 后={1}（长按至少按到 Good）", atBase, atScaled));

            // ⑦ 退化行回退对称口径（防序列化丢表导致判定全 Miss）
            var broken = new JudgeWindows();
            broken.table = new JudgeWindowTable();          // rows 全 0 且未初始化
            check("退化表回退对称",
                broken.Judge(0.050f, JudgeNoteKind.Flick) == JudgeResultType.Perfect
                && broken.Judge(0.250f, JudgeNoteKind.Flick) == JudgeResultType.Miss,
                string.Format("空表：50ms={0}、250ms={1}（回退 4 个对称字段）",
                    broken.Judge(0.050f, JudgeNoteKind.Flick), broken.Judge(0.250f, JudgeNoteKind.Flick)));

            // ⑧ 时机提示：JUST 档只吃 justBand，其余分早/晚
            var hintTable = new JudgeWindowTable();
            hintTable.CopyFrom(JudgeWindows.Default.table);
            var flickRow = hintTable.Get(JudgeNoteKind.Flick);
            flickRow.justBand = 0.016f;
            hintTable.Set(JudgeNoteKind.Flick, flickRow);
            var hinted = new JudgeWindows { table = hintTable };
            check("时机提示 Just/Fast/Late",
                hinted.Describe(0.010f, JudgeNoteKind.Flick) == JudgeTimingHint.Just
                && hinted.Describe(0.050f, JudgeNoteKind.Flick) == JudgeTimingHint.Fast
                && hinted.Describe(-0.050f, JudgeNoteKind.Flick) == JudgeTimingHint.Late
                && hinted.Describe(0.010f, JudgeNoteKind.Normal) != JudgeTimingHint.Just,   // 默认 justBand=0 → 不误报 JUST
                string.Format("Flick(±16ms)：+10ms={0}、+50ms={1}、-50ms={2}；Normal +10ms={3}",
                    hinted.Describe(0.010f, JudgeNoteKind.Flick), hinted.Describe(0.050f, JudgeNoteKind.Flick),
                    hinted.Describe(-0.050f, JudgeNoteKind.Flick), hinted.Describe(0.010f, JudgeNoteKind.Normal)));
        }

        /// <summary>造一个真实长按头，走 Note.JudgeLongRelease（判定窗口取自 PlayRules）</summary>
        private static JudgeResultType JudgeLongReleaseResult(float releaseDiff)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("JW_Note", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(Note.DefaultNoteWidth, Note.DefaultNoteHeight);

                Note note = go.AddComponent<Note>();
                typeof(Note).GetField("noteImage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(note, go.GetComponent<Image>());
                note.Initialize(new NoteData(1, 0f, NoteType.LongStart, 1.0f, 7),
                    SpawnPos, new Vector2(0f, PlayVisualSpec.JudgeLineY));
                return note.JudgeLongRelease(releaseDiff);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
