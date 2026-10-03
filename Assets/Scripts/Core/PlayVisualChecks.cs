#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Gameplay;
using FallenAngel.Live;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏界面视觉基准自检：判定线的「视觉位置」必须等于「判定位置」。
    ///
    /// 背景：两者曾分别硬编码——视觉线距底 400、判定点距底 560，差 160px（约屏高 8%），
    /// 玩家看到的白线不是音符被判定到的那条线。现在两侧同读 PlayVisualSpec，本自检负责守住它。
    ///
    /// 常量检查菜单与 batchmode 两用；场景装配检查只在 batchmode 下跑
    /// （交互模式下它需要重建场景，会覆盖你正在编辑的东西，所以不自动执行）。
    /// batchmode：-executeMethod FallenAngel.Core.PlayVisualChecks.Run
    /// </summary>
    public static class PlayVisualChecks
    {
        private const float TolerancePx = 0.5f;

        [MenuItem("Tools/FallenAngel/Validation/Visuals/Validate Play Visual Spec", false, 606)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-24} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            // ① 规格常量：判定位置 / 按键区 / 换算
            check("判定位置距底 400", Mathf.Abs(PlayVisualSpec.JudgeLineFromBottom - 400f) <= 0.01f,
                string.Format("距底 {0:0.#}px", PlayVisualSpec.JudgeLineFromBottom));
            check("按键区高度 400", Mathf.Abs(PlayVisualSpec.KeyAreaHeight - 400f) <= 0.01f,
                string.Format("{0:0.#}px", PlayVisualSpec.KeyAreaHeight));
            check("中心系换算", Mathf.Abs(PlayVisualSpec.JudgeLineY - (-560f)) <= 0.01f,
                string.Format("距底 400 → 中心系 {0:0.#}", PlayVisualSpec.JudgeLineY));
            check("按键区不越过判定线", PlayVisualSpec.KeyAreaHeight <= PlayVisualSpec.JudgeLineFromBottom,
                string.Format("按键区顶边距底 {0:0.#} ≤ 判定线距底 {1:0.#}",
                    PlayVisualSpec.KeyAreaHeight, PlayVisualSpec.JudgeLineFromBottom));

            // 轨道加宽（2026-09-18）：几乎占满屏幕宽；音符宽度随轨道宽自适配
            float usable = LaneLayout.GetUsableWidth(LaneLayout.MaxLaneCount);
            float coverage = usable / LaneLayout.CanvasWidth;
            check("轨道几乎占满屏幕", coverage >= 0.9f,
                string.Format("轨组 {0:0}px / 画布 {1:0}px = {2:P0}（要求 ≥90%）",
                    usable, LaneLayout.CanvasWidth, coverage));

            float laneW = LaneLayout.GetLaneWidth(4);
            float noteFill = Note.DefaultNoteWidth / laneW;   // 4 键时的填充比
            check("音符宽度随轨道适配", noteFill >= 0.85f && noteFill <= 1.0f,
                string.Format("4 键轨宽 {0:0.#}，音符宽 {1:0.#}（填充 {2:P0}）",
                    laneW, Note.DefaultNoteWidth, noteFill));

            // ② 真实场景装配：用 SceneBuilder 造一遍，量判定线与 NoteSpawner 的实际位置
            if (!Application.isBatchMode)
            {
                lines.Add("SKIP 场景装配检查          交互模式跳过（需要重建场景）；命令行跑 -executeMethod FallenAngel.Core.PlayVisualChecks.Run");
            }
            else
            {
                try
                {
                    SceneBuilder.BuildDefaultScene(false);

                    Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
                    RectTransform[] rects = UnityEngine.Object.FindObjectsOfType<RectTransform>(true);
                    NoteSpawner[] spawners = UnityEngine.Object.FindObjectsOfType<NoteSpawner>(true);
                    if (canvases.Length == 0) throw new Exception("场景里没有 Canvas");
                    if (spawners.Length == 0) throw new Exception("场景里没有 NoteSpawner");

                    RectTransform judge = null, keyArea = null;
                    foreach (RectTransform r in rects)
                    {
                        if (judge == null && r.name == "JudgeLine") judge = r;
                        if (keyArea == null && r.name == "KeyArea") keyArea = r;
                    }
                    if (judge == null) throw new Exception("场景里没有 JudgeLine");

                    RectTransform canvasRect = canvases[0].GetComponent<RectTransform>();
                    // 无头模式下屏幕分辨率不是 9:16，画布矩形不等于参考分辨率，
                    // 所以一律换算成「距画布底边的参考单位」再比较，与分辨率无关。
                    float canvasBottom = canvasRect.rect.yMin;
                    float judgeFromBottom = canvasRect.InverseTransformPoint(judge.position).y - canvasBottom;
                    // NoteSpawner 用画布中心坐标，按参考分辨率换算回距底高度
                    float spawnJudgeFromBottom = spawners[0].JudgeLineY + PlayVisualSpec.CanvasHeight * 0.5f;

                    check("视觉线 == 判定线",
                        Mathf.Abs(judgeFromBottom - spawnJudgeFromBottom) <= TolerancePx,
                        string.Format("视觉线距底 {0:0.#} / 判定距底 {1:0.#}，差 {2:0.#}px",
                            judgeFromBottom, spawnJudgeFromBottom, judgeFromBottom - spawnJudgeFromBottom));
                    check("视觉线 == 规格值",
                        Mathf.Abs(judgeFromBottom - PlayVisualSpec.JudgeLineFromBottom) <= TolerancePx,
                        string.Format("视觉线距底 {0:0.#} / 规格 {1:0.#}",
                            judgeFromBottom, PlayVisualSpec.JudgeLineFromBottom));

                    if (keyArea != null)
                    {
                        Vector3 top = keyArea.TransformPoint(new Vector3(0f, keyArea.rect.height, 0f));
                        float padTopFromBottom = canvasRect.InverseTransformPoint(top).y - canvasBottom;
                        check("按键区顶边不越过判定线", padTopFromBottom <= judgeFromBottom + TolerancePx,
                            string.Format("按键区顶边距底 {0:0.#} ≤ 判定线距底 {1:0.#}",
                                padTopFromBottom, judgeFromBottom));
                    }
                    else
                    {
                        check("按键区顶边不越过判定线", false, "场景里没有 KeyArea");
                    }

                    check("生成点在判定线上方", spawners[0].SpawnY > spawners[0].JudgeLineY,
                        string.Format("生成 y={0:0.#} > 判定 y={1:0.#}", spawners[0].SpawnY, spawners[0].JudgeLineY));

                    // 音符带子的坐标系前提（NoteBandGraphic）：画布 y = 音符 anchoredPosition.y + 局部 y，
                    // 这要求 NotesContainer 是全画布、无偏移、无缩放的容器。改了这里，长按身体/slide 轨迹
                    // 就会整体错位到别的轨道高度上，所以钉住它。
                    RectTransform notesContainer = null;
                    foreach (RectTransform r in rects)
                    {
                        if (notesContainer == null && r.name == "NotesContainer") notesContainer = r;
                    }
                    bool containerOk = notesContainer != null &&
                        Mathf.Abs(notesContainer.anchoredPosition.x) < 0.01f &&
                        Mathf.Abs(notesContainer.anchoredPosition.y) < 0.01f &&
                        Mathf.Abs(notesContainer.anchorMin.x) < 0.01f && Mathf.Abs(notesContainer.anchorMin.y) < 0.01f &&
                        Mathf.Abs(notesContainer.anchorMax.x - 1f) < 0.01f && Mathf.Abs(notesContainer.anchorMax.y - 1f) < 0.01f &&
                        Mathf.Abs(notesContainer.sizeDelta.x) < 0.01f && Mathf.Abs(notesContainer.sizeDelta.y) < 0.01f &&
                        Mathf.Abs(notesContainer.localScale.x - 1f) < 0.0001f &&
                        Mathf.Abs(notesContainer.localScale.y - 1f) < 0.0001f;
                    check("音符容器全画布无偏移", containerOk,
                        notesContainer == null
                            ? "场景里没有 NotesContainer"
                            : string.Format("anchors {0}–{1}、sizeDelta {2}、scale {3}",
                                notesContainer.anchorMin, notesContainer.anchorMax,
                                notesContainer.sizeDelta, notesContainer.localScale));

                    RectTransform bg = null;
                    foreach (RectTransform r in rects)
                    {
                        if (bg == null && r.name == "GameplayBackground") bg = r;
                    }
                    UnityEngine.UI.Image bgImg = bg != null ? bg.GetComponent<UnityEngine.UI.Image>() : null;
                    bool bgOk = bgImg != null && bgImg.sprite != null;
                    check("演奏背景已挂载", bgOk,
                        bgOk ? string.Format("sprite={0} {1}x{2}", bgImg.sprite.name,
                                bgImg.sprite.rect.width, bgImg.sprite.rect.height)
                             : "场景里没有带 sprite 的 GameplayBackground");

                    FallenAngel.UI.HitEffectController[] hitFx =
                        UnityEngine.Object.FindObjectsOfType<FallenAngel.UI.HitEffectController>(true);
                    int laneRefs = hitFx.Length > 0 ? hitFx[0].BoundLaneColumnCount : 0;
                    check("Miss 特效已接线", hitFx.Length > 0 && laneRefs == 5,
                        string.Format("HitEffectController={0} 个，轨道柱引用 {1}/5",
                            hitFx.Length, laneRefs));

                    FallenAngel.UI.ModifierDebugPanel panel =
                        FallenAngel.UI.ModifierDebugPanel.Install(canvasRect, TMPro.TMP_Settings.defaultFontAsset);
                    int panelButtons = panel != null
                        ? panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length : 0;
                    check("modifier 调试窗口可安装", panel != null && panelButtons >= 6,
                        string.Format("面板={0}，按钮 {1} 个（1 折叠 + 5 项）",
                            panel != null ? 1 : 0, panelButtons));

                    ModifierManager[] modMgrs = UnityEngine.Object.FindObjectsOfType<ModifierManager>(true);
                    FallenAngel.UI.PlayVisibilityController[] visCtls =
                        UnityEngine.Object.FindObjectsOfType<FallenAngel.UI.PlayVisibilityController>(true);
                    check("局内管理器已装配", modMgrs.Length == 1 && visCtls.Length == 1,
                        string.Format("ModifierManager={0} 个，PlayVisibilityController={1} 个（各应为 1）",
                            modMgrs.Length, visCtls.Length));

                    // 功能自检：真的"按一下"第一个效果按钮，确认 modifier 真的生效（而不只是画出来）
                    ModifierManager.EnsureInstance();
                    UnityEngine.UI.Button probe = null;
                    UnityEngine.UI.Button[] panelBtns =
                        panel != null ? panel.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                                      : new UnityEngine.UI.Button[0];
                    foreach (UnityEngine.UI.Button b in panelBtns)
                    {
                        if (b.gameObject.name.Contains("1.5")) { probe = b; break; }
                    }
                    if (probe != null) probe.onClick.Invoke();
                    bool applied = ModifierManager.Instance != null && ModifierManager.Instance.Has("DBG_LOOSE");
                    float windowScale = PlayRules.JudgeWindowScale;
                    check("调试窗口按钮真的生效", applied && Mathf.Abs(windowScale - 1.5f) < 0.01f,
                        string.Format("点击后 已施加={0}，判定窗口倍率={1:0.0}", applied, windowScale));
                    ModifierManager.Instance.ClearAll();

                    // 布局回归：行按钮必须与折叠按钮同一条竖线。
                    // 曾因坐标重复叠加（父容器已偏移、子节点又加一次）被推到 x≈1636，整排在屏幕外。
                    float toggleX = float.NaN;
                    int rows = 0, misaligned = 0;
                    foreach (UnityEngine.UI.Button b in panelBtns)
                    {
                        RectTransform brt = (RectTransform)b.transform;
                        if (brt.parent != null && brt.parent.name == "Rows")
                        {
                            rows++;
                            if (float.IsNaN(toggleX)) toggleX = brt.position.x;   // 第一行作为基准
                            else if (Mathf.Abs(brt.position.x - toggleX) > 0.5f) misaligned++;
                        }
                    }
                    check("调试窗口各行左对齐", rows >= 5 && misaligned == 0,
                        string.Format("行按钮 {0} 个，未对齐 {1} 个", rows, misaligned));
                }
                catch (Exception e)
                {
                    fail++;
                    lines.Add(string.Format("FAIL {0,-24} 场景装配自检异常：{1}", "场景装配", e.Message));
                }
            }

            // ── 判定文字 / 描述（前端对照 OpenSekai：JudgmentView / JudgmentDescriptionView）──
            // 动画曲线（上游 t=clamp01(time*20)、scale=1-(1-t)³、time>0.3 隐藏）
            float s0 = UI.JudgmentTextView.ScaleAt(0f);
            float sMid = UI.JudgmentTextView.ScaleAt(0.025f);
            float s1 = UI.JudgmentTextView.ScaleAt(0.05f);
            float sHide = UI.JudgmentTextView.ScaleAt(0.301f);
            check("判定文字·动画曲线照抄上游",
                Mathf.Abs(s0) < 1e-4f && Mathf.Abs(sMid - 0.875f) < 1e-3f
                && Mathf.Abs(s1 - 1f) < 1e-4f && Mathf.Abs(sHide) < 1e-4f,
                string.Format("t=0→{0:0.###}、0.025s→{1:0.###}、0.05s→{2:0.###}、0.301s→{3:0.###}",
                    s0, sMid, s1, sHide));

            // 结果 → 文案（JustPerfect 复用 PERFECT；Auto 不显示）
            bool words = UI.JudgmentTextView.WordKeyFor(NoteResult.JustPerfect) == "judge.perfect"
                         && UI.JudgmentTextView.WordKeyFor(NoteResult.Perfect) == "judge.perfect"
                         && UI.JudgmentTextView.WordKeyFor(NoteResult.Miss) == "judge.miss"
                         && UI.JudgmentTextView.WordKeyFor(NoteResult.Auto) == null;
            // 描述门控（上游：只有 Bad/Good/Great 档显示；Perfect 的 Fast/Late 不显示；Just 无描述）
            bool descGate = UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Great, NoteResultDescription.Fast) == "judge.desc.fast"
                            && UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Bad, NoteResultDescription.FlickMiss) == "judge.desc.flickMiss"
                            && UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Perfect, NoteResultDescription.Late) == null
                            && UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Great, NoteResultDescription.Just) == null;
            check("判定文字·结果与描述映射（含门控）",
                words && descGate,
                string.Format("JustPerfect→PERFECT={0}、Perfect+Late 不显示={1}、Great+Fast 显示={2}、Bad+FlickMiss 显示={3}",
                    UI.JudgmentTextView.WordKeyFor(NoteResult.JustPerfect),
                    UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Perfect, NoteResultDescription.Late) == null,
                    UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Great, NoteResultDescription.Fast),
                    UI.JudgmentTextView.DescriptionKeyFor(NoteResult.Bad, NoteResultDescription.FlickMiss)));

            string summary = string.Format(
                "演奏界面视觉基准自检：{0} 项通过 / {1} 项异常（判定：判定线视觉位置 == 音符判定位置，且按键区不越过判定线）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[PlayVisual] " + line);
            Debug.Log("[PlayVisual] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/play_visual_check.txt", lines.ToArray());
                File.AppendAllText("Logs/play_visual_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayVisual] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
