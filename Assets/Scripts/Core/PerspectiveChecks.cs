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
    /// 近大远小透视自检（作者 2026-09-20 选型 B）。
    ///
    /// 守住两件事：
    ///  ① 曲线本身的性质——p=1 时尺寸恰好 1、尺寸有 0.35 下限、位置单调；
    ///  ② **音符与轨道严格对齐**——任意高度 y 上，轨道半宽 × 音符填充比 == 该处音符半宽。
    ///     两侧都读 PlayPerspective，所以这条不变量必须恒成立；一旦有人只改一边就会红。
    ///
    /// 只读 + 临时对象；菜单与 batchmode（-executeMethod FallenAngel.Core.PerspectiveChecks.Run）两用。
    /// </summary>
    public static class PerspectiveChecks
    {
        private const float Tol = 0.01f;
        // 生成点必须取游戏真实值（PlayVisualSpec.SpawnYFromCenter = 1200），
        // 否则自检量的是 NoteSpawner 上的字段缺省 600，和玩家看到的几何差着 600px。
        private static readonly Vector2 SpawnPos = new Vector2(0f, PlayVisualSpec.SpawnYFromCenter);
        private static readonly Vector2 JudgePos = new Vector2(0f, PlayVisualSpec.JudgeLineY);

        [MenuItem("Tools/FallenAngel/Validate Play Perspective")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            bool savedEnabled = PlayPerspective.Enabled;
            try
            {
                PlayPerspective.Enabled = true;
                PlayPerspective.SetGeometry(SpawnPos.y, JudgePos.y);
                RunCases(check);
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }
            finally
            {
                PlayPerspective.Enabled = savedEnabled;
            }

            string summary = string.Format(
                "透视自检：{0} 项通过 / {1} 项异常（判定：曲线性质 + 尺寸下限 + 位置单调 + 轨道与音符严格对齐 + 总开关可回退）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[Perspective] " + line);
            Debug.Log("[Perspective] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/perspective_check.txt", lines.ToArray());
                File.AppendAllText("Logs/perspective_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Perspective] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static void RunCases(Action<string, bool, string> check)
        {
            // ① 端点：p=1 恰好是判定线、p=0 恰好是生成点、尺寸 p=1 恰好 1
            bool endsOk = Mathf.Abs(PlayPerspective.PositionY(1f, SpawnPos.y, JudgePos.y) - JudgePos.y) <= Tol
                       && Mathf.Abs(PlayPerspective.PositionY(0f, SpawnPos.y, JudgePos.y) - SpawnPos.y) <= Tol
                       && Mathf.Abs(PlayPerspective.SizeScale(1f) - 1f) <= Tol;
            check("端点对齐", endsOk,
                string.Format("p=0 → y={0:0.#}（生成点）、p=1 → y={1:0.#}（判定线）、scale(1)={2:0.###}",
                    PlayPerspective.PositionY(0f, SpawnPos.y, JudgePos.y),
                    PlayPerspective.PositionY(1f, SpawnPos.y, JudgePos.y), PlayPerspective.SizeScale(1f)));

            // ② 尺寸下限：远处不会小成一个点
            float farScale = PlayPerspective.SizeScale(0f);
            check("尺寸下限 0.35", Mathf.Abs(farScale - PlayPerspective.MinScale) <= Tol,
                string.Format("p=0 缩放={0:0.###}（下限 {1:0.##}，原作口径 {2:0.###}）",
                    farScale, PlayPerspective.MinScale, PlayPerspective.ViewProgress(0f)));

            // ③ 位置单调递增（从生成点向判定线推进，不能回头）
            bool monotonic = true;
            float prev = float.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                float y = PlayPerspective.PositionY(i / 100f, SpawnPos.y, JudgePos.y);
                if (y > prev + Tol) { monotonic = false; break; }
                prev = y;
            }
            check("位置单调", monotonic, "p 从 0→1 时 y 单调下降（不会回弹）");

            // ③b 过线继续飞（D4 行为面）：p > 1 时继续单调下移，且不改变 p ≤ 1 的形状
            bool flyMonotonic = true;
            float prevFly = PlayPerspective.PositionY(1f, SpawnPos.y, JudgePos.y);
            for (int i = 1; i <= 12; i++)
            {
                float y = PlayPerspective.PositionY(1f + i * 0.05f, SpawnPos.y, JudgePos.y);
                if (y > prevFly + Tol) { flyMonotonic = false; break; }
                prevFly = y;
            }
            float past = PlayPerspective.PositionY(1.3f, SpawnPos.y, JudgePos.y);
            float judgeY = PlayPerspective.PositionY(1f, SpawnPos.y, JudgePos.y);
            check("过线继续下移", flyMonotonic && past < judgeY - 10f,
                string.Format("p=1.0 → y={0:0.#}（判定线）、p=1.3 → y={1:0.#}（继续往下，下游 {2:0.#}px）",
                    judgeY, past, judgeY - past));

            // ④ 严格对齐：轨道半宽（SizeScaleAtY）× 音符填充比 == 该处音符半宽
            float laneWidth = LaneLayout.GetLaneWidth(4);
            float fill = Note.DefaultNoteWidth / laneWidth;
            float worst = 0f;
            for (int i = 0; i <= 20; i++)
            {
                float p = i / 20f;
                float y = PlayPerspective.PositionY(p, SpawnPos.y, JudgePos.y);
                float laneHalf = laneWidth * 0.5f * PlayPerspective.SizeScaleAtY(y, SpawnPos.y, JudgePos.y);
                float noteHalf = Note.DefaultNoteWidth * 0.5f * PlayPerspective.SizeScale(p);
                worst = Mathf.Max(worst, Mathf.Abs(laneHalf * fill - noteHalf));
            }
            check("轨道与音符严格对齐", worst <= 0.5f,
                string.Format("21 个采样点上「轨道半宽×填充比 − 音符半宽」最大偏差 {0:0.###}px（填充比 {1:P0}）", worst, fill));

            // ⑤ 带子取样前提：尺寸随画布 y 必须是"折点以上恒定、折点以下**精确线性**"。
            //    NoteBandGraphic 用 12 段折线画长按身体/slide 轨迹，靠的就是这条性质
            //    （线性段上取点没有弦误差，只在折点处需要插一个结点）。
            //    一旦曲线对 y 变非线性，跨折点的弦就会外鼓、带子越出轨道——所以这里把它钉死。
            bool cornerOk = Mathf.Abs(PlayPerspective.FloorCornerY -
                                      Mathf.Lerp(SpawnPos.y, JudgePos.y, PlayPerspective.MinScale)) <= Tol;
            float worstLinearity = 0f;
            string linearityDetail = "";
            // 顺带覆盖"生成点更近"的另一组几何（旧的 600），确认这条性质与几何无关
            float[] spawnValues = { SpawnPos.y, SpawnPos.y * 0.5f };
            for (int g = 0; g < spawnValues.Length; g++)
            {
                float spawn = spawnValues[g];
                float corner = Mathf.Lerp(spawn, JudgePos.y, PlayPerspective.MinScale);
                float cornerScale = PlayPerspective.SizeScaleAtY(corner, spawn, JudgePos.y);
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    float below = Mathf.Lerp(corner, JudgePos.y, t);          // 折点 → 判定线
                    worstLinearity = Mathf.Max(worstLinearity,
                        Mathf.Abs(PlayPerspective.SizeScaleAtY(below, spawn, JudgePos.y) - Mathf.Lerp(cornerScale, 1f, t)));
                    float above = Mathf.Lerp(corner, spawn + 400f, t);        // 折点 → 生成点以上
                    worstLinearity = Mathf.Max(worstLinearity,
                        Mathf.Abs(PlayPerspective.SizeScaleAtY(above, spawn, JudgePos.y) - cornerScale));
                }
                linearityDetail += string.Format("生成 y={0:0} 折点 {1:0.#}；", spawn, corner);
            }
            check("带子取样前提（折点外线性）", cornerOk && worstLinearity <= 0.001f,
                string.Format("{0}折点缩放 {1:0.###}，最大偏差 {2:0.####}（线性 → 12 段折线无弦误差）",
                    linearityDetail, PlayPerspective.SizeScaleAtY(PlayPerspective.FloorCornerY), worstLinearity));

            // ⑥ 真实音符：按歌曲时间推进后，位置与缩放都要等于曲线值
            GameObject go = null;
            GameObject gmHost = null;
            try
            {
                // Note.UpdatePosition 需要 GameManager.Instance（读 ActualFallTime），
                // 批处理非播放模式不会自己唤醒，这里显式装一个沙箱实例
                gmHost = new GameObject("PerspectiveGM");
                gmHost.SetActive(false);
                var gm = gmHost.AddComponent<GameManager>();
                typeof(GameManager).GetProperty("Instance",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .SetValue(null, gm);

                go = new GameObject("PerspectiveNote", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(Note.DefaultNoteWidth, Note.DefaultNoteHeight);
                Note note = go.AddComponent<Note>();
                typeof(Note).GetField("noteImage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(note, go.GetComponent<Image>());
                note.Initialize(new NoteData(0, 10f, NoteType.Normal), SpawnPos, JudgePos);

                float fallTime = 2f;
                float p = 0.5f;
                float songTime = 10f - fallTime * (1f - p);      // UpdatePosition 的 p 定义
                note.UpdatePosition(songTime, SpawnPos.y, JudgePos.y);
                float expectY = PlayPerspective.PositionY(p, SpawnPos.y, JudgePos.y);
                float expectScale = PlayPerspective.SizeScale(p);
                float dy = Mathf.Abs(rt.anchoredPosition.y - expectY);
                float ds = Mathf.Abs(rt.localScale.x - expectScale);
                check("音符真的走曲线", dy <= 0.5f && ds <= 0.001f,
                    string.Format("p={0:0.##}：y 偏差 {1:0.###}px、缩放 {2:0.###}（期望 {3:0.###}）",
                        p, dy, rt.localScale.x, expectScale));
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (gmHost != null) UnityEngine.Object.DestroyImmediate(gmHost);
                typeof(GameManager).GetProperty("Instance",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .SetValue(null, null);
            }

            // ⑦ 总开关：关掉即回到线性 + 恒定尺寸（可回退）
            PlayPerspective.Enabled = false;
            bool linearY = Mathf.Abs(PlayPerspective.PositionY(0.5f, SpawnPos.y, JudgePos.y)
                                     - Mathf.Lerp(SpawnPos.y, JudgePos.y, 0.5f)) <= Tol;
            bool unitScale = Mathf.Abs(PlayPerspective.SizeScale(0.2f) - 1f) <= Tol;
            PlayPerspective.Enabled = true;
            check("可回退到线性", linearY && unitScale, "关闭透视：位置回到 Lerp、尺寸恒为 1");
        }
    }
}
#endif
