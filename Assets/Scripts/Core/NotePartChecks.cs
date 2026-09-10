#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using FallenAngel.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Core
{
    /// <summary>
    /// 音符部件自检（pjsk 风 head / body·ribbon / tail 三段 + 轨道配色）。
    /// 按游戏真实尺寸装配每种音符，量化检查：
    ///   ① 部件是否存在、是否该显示（LongEnd/LongBody 是账本条目，不该有视觉）；
    ///   ② 部件网格是否真的生成、是否溢出自身矩形（运行时自建 UI 最易静默失败的一环）；
    ///   ③ 部件矩形与音符矩形是否一致（含 kick 全宽条）；
    ///   ④ 绘制顺序：身体 → 尾 → 头 → 图标（头部圆角压住身体接缝，图标在头之上）；
    ///   ⑤ 配色：头 = 轨道语义色，尾 = 头部提亮版，身体/路径 = 同色半透明；
    ///   ⑥ 尾部件位置跟随身体顶端（hold）/ 路径终点（slide）。
    /// 只读，不改场景与资源；菜单与 batchmode（-executeMethod FallenAngel.Core.NotePartChecks.Run）两用。
    /// </summary>
    public static class NotePartChecks
    {
        private static readonly Vector2 SpawnPos = new Vector2(0f, 600f);
        private static readonly Vector2 JudgePos = new Vector2(0f, -400f);
        private const float TolerancePx = 0.5f;

        private sealed class Case
        {
            public string Name;
            public NoteData Data;
            public int LaneCount;
            public bool Wide;
            public Case(string name, NoteData data, int laneCount, bool wide = false)
            { Name = name; Data = data; LaneCount = laneCount; Wide = wide; }
        }

        private static List<Case> BuildCases()
        {
            var slidePath = new List<SlidePathPoint>
            {
                new SlidePathPoint { t = 0f,   x = 1f },
                new SlidePathPoint { t = 0.8f, x = 1.6f },
                new SlidePathPoint { t = 1.5f, x = 3f },
            };

            return new List<Case>
            {
                new Case("4K tap",           new NoteData(0, 0f, NoteType.Normal), 4),
                new Case("5K tap",           new NoteData(4, 0f, NoteType.Normal), 5),
                new Case("5K drag",          new NoteData(2, 0f, NoteType.Drag), 5),
                new Case("4K flick↑",        new NoteData(2, 0f, NoteType.Flick, 0f, -1, FlickDirection.Up), 4),
                new Case("4K hold",          new NoteData(1, 0f, NoteType.LongStart, 1.5f, 7), 4),
                new Case("5K slide",         new NoteData(1, 0f, NoteType.Slide, 1.5f, -1, FlickDirection.Up, slidePath), 5),
                new Case("5K wide(kick)",    new NoteData(0, 0f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true), 5, true),
                new Case("4K longEnd(账本)", new NoteData(1, 0f, NoteType.LongEnd, 0f, 7), 4),
            };
        }

        [MenuItem("Tools/FallenAngel/Validate Note Parts")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            int savedLaneCount = LaneLayout.ActiveLaneCount;

            foreach (Case c in BuildCases())
            {
                GameObject root = null;
                try
                {
                    var notes = new List<string>();
                    Note note = BuildNote(c, out root);
                    bool ok = Inspect(c, note, root, notes);
                    if (ok) pass++; else fail++;
                    lines.Add(string.Format("{0} {1,-16} {2}", ok ? "PASS" : "FAIL", c.Name, string.Join("；", notes)));
                }
                catch (Exception e)
                {
                    fail++;
                    lines.Add(string.Format("FAIL {0,-16} 自检异常：{1}", c.Name, e.Message));
                }
                finally
                {
                    if (root != null) UnityEngine.Object.DestroyImmediate(root);
                }
            }

            // 同时押连线：配对规则（合成谱面）+ 视觉几何（真实装配）
            RunLinkRuleCases(lines, ref pass, ref fail);
            {
                var notes = new List<string>();
                bool ok = LinkVisualCheck(notes);
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-16} {2}", ok ? "PASS" : "FAIL", "同押连线", string.Join("；", notes)));
            }

            LaneLayout.SetActiveLaneCount(savedLaneCount);

            string summary = string.Format("音符部件自检：{0} 项通过 / {1} 项异常（判定：部件齐全 + 网格非空 + 不溢出矩形 + 顺序/配色/尾位置一致 + 同押配对与连线几何正确）", pass, fail);
            foreach (string line in lines) Debug.Log("[NotePart] " + line);
            Debug.Log("[NotePart] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/note_part_check.txt", lines.ToArray());
                File.AppendAllText("Logs/note_part_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NotePart] 报告写入失败：" + e.Message);
            }
        }

        /// <summary>按游戏真实装配方式造一个音符（根节点 Image + Note，与 NoteSpawner 默认预制体一致）</summary>
        private static Note BuildNote(Case c, out GameObject root)
        {
            LaneLayout.SetActiveLaneCount(c.LaneCount);

            root = new GameObject("NotePartCheck", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(Note.DefaultNoteWidth, Note.DefaultNoteHeight);

            var image = root.GetComponent<Image>();
            Note note = root.AddComponent<Note>();
            Field("noteImage").SetValue(note, image);

            note.Initialize(c.Data, SpawnPos, JudgePos);

            if (c.Wide)
            {
                float fullWidth = (LaneLayout.GetCenterXForActive(LaneLayout.ActiveLaneCount - 1) -
                                   LaneLayout.GetCenterXForActive(0)) + Note.DefaultNoteWidth;
                note.SetKickVisual(fullWidth);
            }

            // UpdatePosition 需要 GameManager；自检直接驱动尾部件定位（纯几何，不依赖时间）
            Method("UpdateTailPart").Invoke(note, null);
            return note;
        }

        private static bool Inspect(Case c, Note note, GameObject root, List<string> notes)
        {
            bool ok = true;
            var metrics = new List<string>();
            var head = (NoteHeadGraphic)Field("headGraphic").GetValue(note);
            var headRect = (RectTransform)Field("headRect").GetValue(note);
            var tail = (NoteHeadGraphic)Field("tailGraphic").GetValue(note);
            var tailRect = (RectTransform)Field("tailRect").GetValue(note);
            var body = (GradientImage)Field("bodyGraphic").GetValue(note);
            var bodyRect = (RectTransform)Field("bodyRect").GetValue(note);
            var ribbon = (SlidePathGraphic)Field("slidePathGraphic").GetValue(note);
            var link = (NoteLinkGraphic)Field("linkGraphic").GetValue(note);

            bool bookkeeping = c.Data.type == NoteType.LongEnd || c.Data.type == NoteType.LongBody;
            bool hold = c.Data.type == NoteType.LongStart && c.Data.duration > 0f;
            bool slide = c.Data.type == NoteType.Slide && c.Data.path != null && c.Data.path.Count >= 2;
            var noteRect = (RectTransform)root.transform;
            Color laneColor = LaneColors.GetLaneColor(c.Data.lane);

            // ① 存在性与显示开关
            if (head == null || headRect == null) { notes.Add("缺少头部部件"); return false; }
            if (head.gameObject.activeSelf == bookkeeping) { ok = false; notes.Add(bookkeeping ? "账本音符不该画头部" : "头部未显示"); }
            bool tailShown = tail != null && tail.gameObject.activeSelf;
            if (tailShown != (hold || slide)) { ok = false; notes.Add(tailShown ? "多余尾部件" : "缺少尾部件"); }

            // 预制体 Image 必须停绘（否则纯色矩形与部件叠画）
            var image = root.GetComponent<Image>();
            if (image != null && image.enabled) { ok = false; notes.Add("预制体 Image 仍在绘制"); }

            // 无同押伙伴（本轮单音装配）不该留下连线残留
            if (link != null && link.gameObject.activeSelf) { ok = false; notes.Add("无同押伙伴却画了连线"); }

            // ② 头部网格 + 不溢出
            if (head.gameObject.activeSelf)
            {
                int verts = head.BuildDebugMesh(out Rect headBounds);
                Rect hr = headRect.rect;
                if (verts <= 0) { ok = false; notes.Add("头部网格未生成"); }
                else if (!Inside(headBounds, hr)) { ok = false; notes.Add($"头部溢出矩形 {Fmt(headBounds)} vs {Fmt(hr)}"); }
                else metrics.Add($"头网格 {verts} 顶点 {Fmt(headBounds)}/{Fmt(hr)}");

                if (Mathf.Abs(headRect.sizeDelta.x - noteRect.sizeDelta.x) > TolerancePx ||
                    Mathf.Abs(headRect.sizeDelta.y - noteRect.sizeDelta.y) > TolerancePx)
                { ok = false; notes.Add($"头部尺寸未对齐音符 {headRect.sizeDelta} vs {noteRect.sizeDelta}"); }

                if (!Approx(head.color, laneColor)) { ok = false; notes.Add($"头部配色非轨道色 {head.color} vs {laneColor}"); }
            }

            // ③ 尾部网格 + 收窄（与头部同族但更小）
            if (tailShown)
            {
                int tverts = tail.BuildDebugMesh(out Rect tailBounds);
                if (tverts <= 0) { ok = false; notes.Add("尾部网格未生成"); }
                else if (!Inside(tailBounds, tailRect.rect)) { ok = false; notes.Add($"尾部溢出矩形 {Fmt(tailBounds)} vs {Fmt(tailRect.rect)}"); }
                if (tailRect.sizeDelta.x >= noteRect.sizeDelta.x - 0.5f) { ok = false; notes.Add($"尾部未收窄 {tailRect.sizeDelta.x} vs {noteRect.sizeDelta.x}"); }
                if (!Approx(tail.color.a, head.color.a * 0.92f, 0.02f)) { ok = false; notes.Add($"尾部透明度 {tail.color.a:0.00} 应为头部的 0.92"); }
            }

            // ④ 绘制顺序：身体 → 尾 → 头 → 图标
            if (tailShown && body != null && body.gameObject.activeSelf)
            {
                if (tailRect.GetSiblingIndex() <= bodyRect.GetSiblingIndex()) { ok = false; notes.Add("尾部未画在身体之上"); }
            }
            if (tailShown && tailRect.GetSiblingIndex() >= headRect.GetSiblingIndex()) { ok = false; notes.Add("尾部未画在头部之下"); }

            Transform icon = root.transform.Find("DrumIcon");
            if (icon != null && icon.gameObject.activeSelf && icon.GetSiblingIndex() <= headRect.GetSiblingIndex())
            { ok = false; notes.Add("鼓件图标未画在头部之上"); }
            Transform arrow = root.transform.Find("FlickArrow");
            if (c.Data.type == NoteType.Flick && (arrow == null || !arrow.gameObject.activeSelf))
            { ok = false; notes.Add("flick 方向箭头未显示"); }

            // ⑤ 身体 / 路径
            if (hold)
            {
                if (body == null || !body.gameObject.activeSelf) { ok = false; notes.Add("长按身体未显示"); }
                else
                {
                    if (!Approx(body.bottomColor, new Color(laneColor.r, laneColor.g, laneColor.b, 0.9f))) { ok = false; notes.Add("身体近端未取轨道色"); }
                    if (body.topColor.a <= 0.01f) { ok = false; notes.Add("身体远端全透明（尾标记会与连接段脱节）"); }
                    float expectY = bodyRect.sizeDelta.y * Mathf.Clamp01(bodyRect.localScale.y);
                    if (Mathf.Abs(tailRect.anchoredPosition.y - expectY) > TolerancePx || Mathf.Abs(tailRect.anchoredPosition.x) > TolerancePx)
                    { ok = false; notes.Add($"尾部件未贴身体顶端 y={tailRect.anchoredPosition.y:0.0} 期望 {expectY:0.0}"); }
                    else metrics.Add($"尾 y={tailRect.anchoredPosition.y:0.0}（身体 {bodyRect.sizeDelta.y:0.0}×{bodyRect.sizeDelta.x:0.0}，远端 α={body.topColor.a:0.00}）");
                }
            }
            if (slide)
            {
                if (ribbon == null || !ribbon.gameObject.activeSelf) { ok = false; notes.Add("slide 路径未显示"); }
                else if (!Approx(ribbon.HeadColor, new Color(laneColor.r, laneColor.g, laneColor.b, 0.55f)))
                { ok = false; notes.Add("slide 路径未取轨道色"); }

                float fallDistance = Mathf.Abs(SpawnPos.y - JudgePos.y);
                float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
                float startX = LaneLayout.GetXFromLaneCoord(c.Data.path[0].x);
                float endX = LaneLayout.GetXFromLaneCoord(c.Data.path[c.Data.path.Count - 1].x);
                float expectX = endX - startX;
                float expectY = fallDistance / Mathf.Max(0.01f, fallTime);
                if (Mathf.Abs(tailRect.anchoredPosition.x - expectX) > TolerancePx ||
                    Mathf.Abs(tailRect.anchoredPosition.y - expectY) > TolerancePx)
                { ok = false; notes.Add($"slide 尾部件未贴路径终点 {Fmt(tailRect.anchoredPosition)} 期望 ({expectX:0.0},{expectY:0.0})"); }
                else metrics.Add($"尾 {Fmt(tailRect.anchoredPosition)} = 路径终点（{c.Data.path.Count} 点）");
            }

            // ⑥ kick 全宽
            if (c.Wide)
            {
                float fullWidth = (LaneLayout.GetCenterXForActive(LaneLayout.ActiveLaneCount - 1) -
                                   LaneLayout.GetCenterXForActive(0)) + Note.DefaultNoteWidth;
                if (Mathf.Abs(headRect.sizeDelta.x - fullWidth) > TolerancePx) { ok = false; notes.Add($"kick 头部未全宽 {headRect.sizeDelta.x} 期望 {fullWidth}"); }
            }

            if (ok) notes.Add(bookkeeping ? "账本音符无视觉（预期）" : "部件齐全、顺序/配色/尾位置一致");
            foreach (string m in metrics) notes.Add(m);
            return ok;
        }

        private static bool Inside(Rect inner, Rect outer)
        {
            return inner.xMin >= outer.xMin - 1f && inner.xMax <= outer.xMax + 1f &&
                   inner.yMin >= outer.yMin - 1f && inner.yMax <= outer.yMax + 1f;
        }

        // ============================================================
        // 同时押横向连线（Sonolus SIMULTANEOUS_CONNECTION）
        // ============================================================

        /// <summary>配对规则：用合成谱面直接验证 NoteSpawner.FindSimultaneousPartner 的取舍</summary>
        private static void RunLinkRuleCases(List<string> lines, ref int pass, ref int fail)
        {
            var slidePath = new List<SlidePathPoint>
            {
                new SlidePathPoint { t = 0f, x = 3f },
                new SlidePathPoint { t = 1f, x = 4f },
            };

            var cases = new List<(string Name, List<NoteData> Notes, int Index, int Expect)>
            {
                ("同刻两音(0/3)", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0f, NoteType.Normal) }, 0, 1),
                ("右侧无伙伴", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0f, NoteType.Normal) }, 1, -1),
                ("伙伴是 slide", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0f, NoteType.Slide, 1f, -1, FlickDirection.Up, slidePath) }, 0, -1),
                ("伙伴是宽键", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0f, NoteType.Normal, 0f, -1, FlickDirection.Up, null, true) }, 0, -1),
                ("相差 5ms 不连", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0.005f, NoteType.Normal) }, 0, -1),
                ("三音取最近右侧", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(2, 0.001f, NoteType.Normal), new NoteData(4, 0.001f, NoteType.Normal) }, 0, 1),
                ("账本条目不连", new List<NoteData>
                    { new NoteData(0, 0f, NoteType.Normal), new NoteData(3, 0f, NoteType.LongEnd, 0f, 7) }, 0, -1),
            };

            foreach (var c in cases)
            {
                int got = NoteSpawner.FindSimultaneousPartner(c.Notes, c.Index, 0.0015f);
                bool ok = got == c.Expect;
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-16} 配对返回 {2}（期望 {3}）", ok ? "PASS" : "FAIL", c.Name, got, c.Expect));
            }
        }

        /// <summary>视觉几何：真实装配两个同刻音符，验证连线位置/宽度/配色/层级</summary>
        private static bool LinkVisualCheck(List<string> notes)
        {
            bool ok = true;
            LaneLayout.SetActiveLaneCount(5);
            GameObject leftGO = null, rightGO = null;
            try
            {
                Note left = BuildNote(new Case("连线左", new NoteData(0, 0f, NoteType.Normal), 5), out leftGO);
                BuildNote(new Case("连线右", new NoteData(3, 0f, NoteType.Normal), 5), out rightGO);

                float partnerX = LaneLayout.GetCenterXForActive(3);
                float myX = ((RectTransform)leftGO.transform).anchoredPosition.x;
                float expectWidth = Mathf.Abs(partnerX - myX);
                left.SetSimultaneousLink(partnerX, LaneColors.GetLaneColor(3));

                var link = (NoteLinkGraphic)Field("linkGraphic").GetValue(left);
                var linkRect = (RectTransform)Field("linkRect").GetValue(left);
                var headRect = (RectTransform)Field("headRect").GetValue(left);
                if (link == null || linkRect == null) { notes.Add("连线部件未创建"); return false; }
                if (!link.gameObject.activeSelf) { notes.Add("连线未显示"); return false; }

                int verts = link.BuildDebugMesh(out Rect bounds);
                if (verts <= 0) { ok = false; notes.Add("连线网格未生成"); }
                else if (!Inside(bounds, linkRect.rect)) { ok = false; notes.Add($"连线溢出矩形 {Fmt(bounds)} vs {Fmt(linkRect.rect)}"); }

                if (Mathf.Abs(linkRect.sizeDelta.x - expectWidth) > TolerancePx)
                { ok = false; notes.Add($"连线宽度 {linkRect.sizeDelta.x:0.0} 期望 {expectWidth:0.0}"); }
                if (Mathf.Abs(linkRect.anchoredPosition.x - expectWidth * 0.5f) > TolerancePx ||
                    Mathf.Abs(linkRect.anchoredPosition.y) > TolerancePx)
                { ok = false; notes.Add($"连线中心 {Fmt(linkRect.anchoredPosition)} 期望 ({expectWidth * 0.5f:0.0},0)"); }

                float expectHeight = Mathf.Max(6f, ((RectTransform)leftGO.transform).sizeDelta.y * 0.62f);
                if (Mathf.Abs(linkRect.sizeDelta.y - expectHeight) > TolerancePx)
                { ok = false; notes.Add($"连线高度 {linkRect.sizeDelta.y:0.0} 期望 {expectHeight:0.0}"); }

                Color expectLeft = LaneColors.GetLaneColor(0); expectLeft.a = 0.75f;
                Color expectRight = LaneColors.GetLaneColor(3); expectRight.a = 0.75f;
                if (!Approx(link.LeftColor, expectLeft)) { ok = false; notes.Add($"左端色 {link.LeftColor} 期望 {expectLeft}"); }
                if (!Approx(link.RightColor, expectRight)) { ok = false; notes.Add($"右端色 {link.RightColor} 期望 {expectRight}"); }

                if (linkRect.GetSiblingIndex() >= headRect.GetSiblingIndex())
                { ok = false; notes.Add("连线未画在头部之下"); }

                if (ok) notes.Add($"宽 {linkRect.sizeDelta.x:0.0}×{linkRect.sizeDelta.y:0.0}、网格 {verts} 顶点 {Fmt(bounds)}、两端取轨色、画在头部之下");
            }
            finally
            {
                if (leftGO != null) UnityEngine.Object.DestroyImmediate(leftGO);
                if (rightGO != null) UnityEngine.Object.DestroyImmediate(rightGO);
            }
            return ok;
        }

        private static bool Approx(Color a, Color b, float tol = 0.01f)
        {
            return Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol &&
                   Mathf.Abs(a.b - b.b) <= tol && Mathf.Abs(a.a - b.a) <= tol;
        }

        private static bool Approx(float a, float b, float tol = 0.01f)
        {
            return Mathf.Abs(a - b) <= tol;
        }

        private static string Fmt(Rect r)
        {
            return string.Format("{0:0.#}x{1:0.#}@{2:0.#},{3:0.#}", r.width, r.height, r.xMin, r.yMin);
        }

        private static string Fmt(Vector2 v) => string.Format("{0:0.#},{1:0.#}", v.x, v.y);

        private static FieldInfo Field(string name)
        {
            FieldInfo f = typeof(Note).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException("Note." + name + " 不存在（自检需随字段重命名同步更新）");
            return f;
        }

        private static MethodInfo Method(string name)
        {
            MethodInfo m = typeof(Note).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) throw new MissingMethodException("Note." + name + " 不存在（自检需随方法重命名同步更新）");
            return m;
        }
    }
}
#endif
