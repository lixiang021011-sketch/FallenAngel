#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FallenAngel.Data;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 谱面编辑器（v2 协议 / 5 轨）。第一个可用版本：时间轴放置与删除、BPM 网格吸附、
    /// 五种音符（tap/hold/drag/flick up·down/slide 跨轨）、wide 底鼓、校验与保存 v2 JSON。
    /// 试听与"从此处试打"留作下一步（需要把编辑光标时间传给 GameManager）。
    /// </summary>
    public sealed class ChartEditorWindow : EditorWindow
    {
        private const int Lanes = 5;
        private static readonly Color[] LaneColors =
        {
            new Color(.85f, .35f, .35f), new Color(.9f, .8f, .35f), new Color(.4f, .6f, .95f),
            new Color(.45f, .85f, .5f), new Color(.75f, .55f, .9f)
        };

        private string chartPath = "Assets/Resources/Charts/chart_editor_draft.json";
        private ChartData chart;
        private int selected = -1;
        // 多选 / 框选 / 剪贴板（PhiEdit 式编辑）：
        private readonly List<NoteData> selection = new List<NoteData>();
        private readonly List<NoteData> clipboard = new List<NoteData>();
        private float clipboardBaseTime;
        private bool marqueeActive;
        private Vector2 marqueeStart, marqueeEnd;
        // 空白处左键：按下先记住"待放置"，拖动超过阈值才转为框选（点=放置，拖=框选）
        private bool pendingPlace;
        private float pendingTime;
        private int pendingLane;
        private Vector2 pendingMouse;
        private float scrollTime;
        private float pixelsPerSecond = 220f;
        private int snapDivision = 4;          // 1/4 拍；0 = 不吸附
        private NoteType placeType = NoteType.Normal;
        private FlickDirection flickDirection = FlickDirection.Up;
        private bool wide;
        private float durationBeats = 1f;
        private int slideEndLane = 4;
        private Vector2 scroll;
        // 拖拽状态：PhiEdit 式交互——中间拖动=移动（可跨轨），右边缘拖动=改时长
        private int dragIndex = -1;
        private bool dragResize;
        private float dragGrabOffset;
        private const float ResizeZonePx = 10f;
        // 音频预览：隐藏 AudioSource + 峰值缓存；空格播放/暂停，光标随音频走
        private AudioClip clip;
        private AudioSource preview;
        private float[] peaks;
        private bool playing;
        private float lastRepaint;
        private float hoverTime = -1f;   // 鼠标在时间轴上的时间位置（-1 = 不在时间轴上）
        // 预览命中效果：音符到达判定线的瞬间炸开，并标出与音频的时间差（ms）
        private sealed class Burst { public int lane; public double start; public float deltaMs; }
        private readonly List<Burst> bursts = new List<Burst>();
        private readonly Dictionary<int, float> burstFired = new Dictionary<int, float>();
        private float lastPreviewTime;   // 上一帧的预览时间，用于"跨过即触发"
        private const double BurstSeconds = .32;

        [MenuItem("Tools/FallenAngel/Chart Editor")]
        public static void Open() => GetWindow<ChartEditorWindow>("谱面编辑器");

        /// <summary>打开窗口即有可编辑内容：默认谱存在则载入，否则自动新建 5 轨草稿。</summary>
        private void OnEnable()
        {
            wantsMouseMove = true;         // 需要 MouseMove 事件才能得到 hoverTime
            if (chart != null) return;
            if (File.Exists(chartPath))
            {
                var loaded = ChartLoader.LoadFromJson(File.ReadAllText(chartPath));
                if (loaded != null) { chart = loaded; return; }
            }
            NewChart();
            Debug.Log("[ChartEditor] 已新建 5 轨草稿（" + chartPath + "），保存后写入磁盘。");
        }

        /// <summary>未保存改动：关窗时由 Unity 弹「保存 / 放弃 / 取消」。</summary>
        public override void SaveChanges()
        {
            Save();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            base.DiscardChanges();
            burstFired.Clear();
        }

        private void OnGUI()
        {
            HandleGlobalKeys();
            DrawFileBar();
            if (chart == null) { EditorGUILayout.HelpBox("先载入或新建一张谱面。", MessageType.Info); return; }
            DrawMetadata();
            DrawToolbar();
            DrawTransport();
            DrawTimeline();
            DrawPreview();
            DrawInspector();
        }

        /// <summary>
        /// 全局按键必须先于文本字段处理：否则空格会一直落进保持焦点的输入框。
        /// 空格 = 播放/暂停（同时结束文本编辑）；Shift+空格 才是往输入框里打空格。
        /// </summary>
        private void HandleGlobalKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;
            // 正在文本字段里输入时，复制/粘贴/删除都交给字段本身
            bool typing = EditorGUIUtility.editingTextField;
            if (!typing && ctrl && e.keyCode == KeyCode.C) { CopySelection(); e.Use(); return; }
            if (!typing && ctrl && e.keyCode == KeyCode.V) { PasteClipboard(); e.Use(); return; }
            if (!typing && ctrl && e.keyCode == KeyCode.D) { DuplicateSelection(); e.Use(); return; }
            if (!typing && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)) { DeleteSelection(); e.Use(); return; }
            if (e.keyCode != KeyCode.Space) return;
            if (e.shift) return;                       // Shift+空格：让字段正常输入空格
            if (EditorGUIUtility.editingTextField)
            {
                EditorGUIUtility.editingTextField = false;
                GUI.FocusControl(null);
            }
            TogglePlay();
            e.Use();
            Repaint();
        }

        private static NoteData Clone(NoteData n)
        {
            var copy = new NoteData(n.lane, n.time, n.type, n.duration, n.longNoteId, n.direction, null, n.wide);
            if (n.path != null)
            {
                copy.path = new List<SlidePathPoint>();
                foreach (var p in n.path) copy.path.Add(new SlidePathPoint { t = p.t, x = p.x });
            }
            return copy;
        }

        private void CopySelection()
        {
            if (selection.Count == 0) return;
            clipboard.Clear();
            clipboardBaseTime = float.MaxValue;
            foreach (var n in selection) clipboardBaseTime = Mathf.Min(clipboardBaseTime, n.time);
            foreach (var n in selection) clipboard.Add(Clone(n));
            Debug.Log($"[ChartEditor] 已复制 {clipboard.Count} 个音符（基准 {clipboardBaseTime:0.000}s）");
        }

        private void PasteClipboard()
        {
            if (clipboard.Count == 0) return;
            float target = hoverTime >= 0f ? Snap(hoverTime) : Snap(scrollTime);
            PasteAt(target);
        }

        private void DuplicateSelection()
        {
            if (selection.Count == 0) return;
            clipboard.Clear();
            clipboardBaseTime = float.MaxValue;
            foreach (var n in selection) clipboardBaseTime = Mathf.Min(clipboardBaseTime, n.time);
            foreach (var n in selection) clipboard.Add(Clone(n));
            PasteAt(Snap(selection[0].time + 1f));
        }

        /// <summary>把剪贴板内容粘贴到 target 时间（保持音符之间的相对时差与轨道）。</summary>
        private void PasteAt(float target)
        {
            selection.Clear();
            foreach (var src in clipboard)
            {
                var copy = Clone(src);
                copy.time = Mathf.Max(0f, target + (src.time - clipboardBaseTime));
                chart.notes.Add(copy);
                selection.Add(copy);
            }
            chart.SortNotes();
            selected = chart.notes.IndexOf(selection[0]);
            MarkDirty();
            Debug.Log($"[ChartEditor] 已粘贴 {selection.Count} 个音符到 {target:0.000}s");
            Repaint();
        }

        private void DeleteSelection()
        {
            if (selection.Count == 0 && selected >= 0 && selected < chart.notes.Count)
                selection.Add(chart.notes[selected]);
            if (selection.Count == 0) return;
            foreach (var n in selection) chart.notes.Remove(n);
            selection.Clear();
            selected = -1;
            MarkDirty();
            Repaint();
        }

        /// <summary>音频与对齐：空格播放/暂停、[ ] 微调偏移、以光标对齐第 1 拍。</summary>
        private void DrawTransport()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(playing ? "⏸ 暂停" : "▶ 播放", EditorStyles.toolbarButton, GUILayout.Width(70))) TogglePlay();
                if (GUILayout.Button("⏮ 回到 0", EditorStyles.toolbarButton, GUILayout.Width(70))) SeekTo(0f);
                if (GUILayout.Button("载入音频", EditorStyles.toolbarButton, GUILayout.Width(70))) LoadAudio();
                GUILayout.Label("偏移(ms)", GUILayout.Width(58));
                float offsetMs = chart.metadata.offset * 1000f;
                float newOffset = EditorGUILayout.FloatField(offsetMs, GUILayout.Width(70));
                if (!Mathf.Approximately(newOffset, offsetMs)) { chart.metadata.offset = newOffset / 1000f; MarkDirty(); }
                if (GUILayout.Button("−10ms", EditorStyles.toolbarButton, GUILayout.Width(52))) NudgeOffset(-10f);
                if (GUILayout.Button("+10ms", EditorStyles.toolbarButton, GUILayout.Width(52))) NudgeOffset(10f);
                GUILayout.Label($"编辑头 {scrollTime:0.000}s", EditorStyles.miniLabel, GUILayout.Width(96));
                if (GUILayout.Button("第 1 拍 = 编辑头(左边缘)", EditorStyles.toolbarButton, GUILayout.Width(160)))
                { chart.metadata.offset = scrollTime; MarkDirty(); }
                using (new EditorGUI.DisabledScope(hoverTime < 0f))
                    if (GUILayout.Button("第 1 拍 = 鼠标处", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    { chart.metadata.offset = hoverTime; MarkDirty(); }
                GUILayout.FlexibleSpace();
                GUILayout.Label(clip == null ? "音频未载入（波形不可用）" : $"{clip.name}  {clip.length:0.00}s", EditorStyles.miniLabel);
            }
            // 空格播放由 HandleGlobalKeys 统一处理（在文本字段之前抢事件）
            if (playing && preview != null) scrollTime = preview.time;
        }

        private void LoadAudio()
        {
            clip = null;
            peaks = null;
            string name = chart.metadata.audioFileName;
            if (!string.IsNullOrEmpty(name))
            {
                clip = Resources.Load<AudioClip>("Audio/" + name);
                if (clip == null)
                {
                    foreach (var ext in new[] { ".mp3", ".wav", ".ogg" })
                    {
                        clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/" + name + ext);
                        if (clip != null) break;
                    }
                }
            }
            if (clip == null) { Debug.LogWarning("[ChartEditor] 找不到音频，请在元数据行填 audioFileName（Resources/Audio 下的名字）"); return; }
            BuildPeaks();
        }

        /// <summary>按当前 clip 生成波形峰值缓存（Load Type 非 Decompress On Load 时读不到采样）。</summary>
        private void BuildPeaks()
        {
            peaks = null;
            if (clip == null) return;
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
            {
                Debug.LogWarning("[ChartEditor] 无法读取波形数据：把该音频的导入设置 Load Type 改成 Decompress On Load 后重试（音符与播放不受影响）");
                return;
            }
            int perPixel = Mathf.Max(32, clip.samples / 4000);
            int columns = Mathf.Max(1, clip.samples / perPixel);
            peaks = new float[columns * 2];
            for (int c = 0; c < columns; c++)
            {
                float min = 0f, max = 0f;
                int from = c * perPixel, to = Mathf.Min(from + perPixel, clip.samples);
                for (int i = from; i < to; i++)
                {
                    float v = samples[i * clip.channels];
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                peaks[c * 2] = min;
                peaks[c * 2 + 1] = max;
            }
            Debug.Log($"[ChartEditor] 波形就绪：{clip.name} / {clip.samples} 采样 / {columns} 列");
        }

        private void EnsureSource()
        {
            if (preview != null) return;
            var go = new GameObject("ChartEditorPreview") { hideFlags = HideFlags.HideAndDontSave };
            preview = go.AddComponent<AudioSource>();
            preview.playOnAwake = false;
            preview.spatialBlend = 0f;
        }

        private void TogglePlay()
        {
            if (clip == null) LoadAudio();
            if (clip == null) return;
            EnsureSource();
            if (playing) { preview.Pause(); playing = false; }
            else
            {
                preview.clip = clip;
                preview.time = Mathf.Clamp(scrollTime, 0f, Mathf.Max(0f, clip.length - .05f));
                preview.UnPause();
                if (!preview.isPlaying) preview.Play();
                playing = true;
                EditorApplication.update += TickWhilePlaying;
            }
            Repaint();
        }

        private void TickWhilePlaying()
        {
            if (preview == null || !playing) { EditorApplication.update -= TickWhilePlaying; return; }
            if (preview.isPlaying) Repaint();
            else { playing = false; EditorApplication.update -= TickWhilePlaying; }
        }

        private void SeekTo(float t)
        {
            scrollTime = Mathf.Max(0f, t);
            burstFired.Clear();                     // 重播同一段时爆点要重新触发
            if (playing && preview != null && clip != null)
                preview.time = Mathf.Clamp(scrollTime, 0f, Mathf.Max(0f, clip.length - .05f));
            Repaint();
        }

        private void NudgeOffset(float ms)
        {
            chart.metadata.offset += ms / 1000f;
            MarkDirty();
            Repaint();
        }

        private void DrawFileBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("谱面路径", GUILayout.Width(56));
                chartPath = EditorGUILayout.TextField(chartPath);
                if (GUILayout.Button("载入", EditorStyles.toolbarButton, GUILayout.Width(52))) Load();
                if (GUILayout.Button("新建 5 轨", EditorStyles.toolbarButton, GUILayout.Width(72))) NewChart();
                if (GUILayout.Button("保存 v2", EditorStyles.toolbarButton, GUILayout.Width(70))) Save();
            }
        }

        private void DrawMetadata()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("曲名", GUILayout.Width(30));
                chart.metadata.songName = EditorGUILayout.TextField(chart.metadata.songName, GUILayout.Width(200));
                EditorGUILayout.LabelField("BPM", GUILayout.Width(30));
                chart.metadata.bpm = EditorGUILayout.FloatField(chart.metadata.bpm);
                EditorGUILayout.LabelField("偏移(s)", GUILayout.Width(46));
                chart.metadata.offset = EditorGUILayout.FloatField(chart.metadata.offset, GUILayout.Width(60));
                EditorGUILayout.LabelField("音频", GUILayout.Width(30));
                // 可直接拖 AudioClip 进来（自动回填名字并刷新波形）；也可手填 Resources/Audio 下的名字
                var picked = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(150));
                if (picked != clip)
                {
                    clip = picked;
                    peaks = null;
                    if (picked != null)
                    {
                        chart.metadata.audioFileName = picked.name;
                        BuildPeaks();
                    }
                    Repaint();
                }
                chart.metadata.audioFileName = EditorGUILayout.TextField(chart.metadata.audioFileName, GUILayout.Width(120));
                if (GUILayout.Button("载入", EditorStyles.miniButton, GUILayout.Width(44))) LoadAudio();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("放置", GUILayout.Width(30));
                TypeButton("tap", NoteType.Normal);
                TypeButton("hold", NoteType.LongStart);
                TypeButton("drag", NoteType.Drag);
                TypeButton("flick", NoteType.Flick);
                TypeButton("slide", NoteType.Slide);
                GUILayout.Space(8);
                if (placeType == NoteType.Flick)
                {
                    flickDirection = GUILayout.Toggle(flickDirection == FlickDirection.Up, "↑ 上", EditorStyles.toolbarButton, GUILayout.Width(48))
                        ? FlickDirection.Up : FlickDirection.Down;
                    if (GUILayout.Button("↓ 下", EditorStyles.toolbarButton, GUILayout.Width(48))) flickDirection = FlickDirection.Down;
                }
                if (placeType == NoteType.LongStart || placeType == NoteType.Slide)
                {
                    GUILayout.Label("时长(拍)", GUILayout.Width(52));
                    durationBeats = EditorGUILayout.FloatField(durationBeats, GUILayout.Width(40));
                }
                if (placeType == NoteType.Slide)
                {
                    GUILayout.Label("终点轨", GUILayout.Width(46));
                    slideEndLane = EditorGUILayout.IntSlider(slideEndLane, 0, Lanes - 1, GUILayout.Width(110));
                }
                wide = GUILayout.Toggle(wide, "wide 底鼓", EditorStyles.toolbarButton, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUILayout.Label("吸附", GUILayout.Width(30));
                if (GUILayout.Button(snapDivision == 0 ? "关" : "1/" + snapDivision, EditorStyles.toolbarButton, GUILayout.Width(44)))
                    snapDivision = snapDivision == 0 ? 4 : snapDivision == 4 ? 8 : snapDivision == 8 ? 16 : 0;
                GUILayout.Label("缩放", GUILayout.Width(30));
                pixelsPerSecond = GUILayout.HorizontalSlider(pixelsPerSecond, 80f, 600f, GUILayout.Width(120));
            }
        }

        private void TypeButton(string label, NoteType type)
        {
            bool on = placeType == type;
            if (GUILayout.Toggle(on, label, EditorStyles.toolbarButton, GUILayout.Width(50)) != on) placeType = type;
        }

        private float Snap(float time)
        {
            if (snapDivision <= 0 || chart.metadata.bpm <= 0f) return time;
            float unit = 60f / chart.metadata.bpm / (snapDivision / 4f);
            // 网格从谱面偏移（第 1 拍）起算，否则设过 offset 后吸附点落不到画出来的拍线上
            float offset = chart.metadata.offset;
            return offset + Mathf.Round((time - offset) / unit) * unit;
        }

        private void DrawTimeline()
        {
            Rect view = GUILayoutUtility.GetRect(10f, 260f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(view, new Color(.08f, .12f, .15f));
            float rowH = view.height / Lanes;

            GUI.BeginClip(view);
            // 轨道背景与分隔线
            for (int lane = 0; lane < Lanes; lane++)
                EditorGUI.DrawRect(new Rect(0, lane * rowH, view.width, rowH - 1f),
                    lane % 2 == 0 ? new Color(.11f, .16f, .2f) : new Color(.13f, .18f, .22f));
            // 波形（手动对齐用：看清鼓点/人声落点，再对偏移）
            if (peaks != null && clip != null && clip.length > 0f)
            {
                int columns = peaks.Length / 2;
                for (float x = 0f; x < view.width; x += 1f)
                {
                    float t = scrollTime + x / pixelsPerSecond;
                    if (t < 0f || t > clip.length) continue;
                    int col = Mathf.Clamp(Mathf.FloorToInt(t / clip.length * columns), 0, columns - 1);
                    float min = peaks[col * 2], max = peaks[col * 2 + 1];
                    float half = view.height * .45f;
                    float yTop = view.height * .5f - max * half;
                    float yBot = view.height * .5f - min * half;
                    EditorGUI.DrawRect(new Rect(x, yTop, 1f, Mathf.Max(1f, yBot - yTop)), new Color(.55f, .75f, .8f, .2f));
                }
            }
            // 拍线（带谱面偏移，方便手动对齐）
            float beat = 60f / Mathf.Max(1f, chart.metadata.bpm);
            float offset = chart.metadata.offset;
            int firstIndex = Mathf.FloorToInt((scrollTime - offset) / beat);
            for (int index = firstIndex; ; index++)
            {
                float t = offset + index * beat;
                float x = (t - scrollTime) * pixelsPerSecond;
                if (x > view.width) break;
                if (x >= 0f)
                    EditorGUI.DrawRect(new Rect(x, 0, 1f, view.height),
                        index % 4 == 0 ? new Color(.5f, .62f, .68f, .6f) : new Color(.4f, .5f, .55f, .22f));
            }
            // 音符
            if (chart.notes != null)
                for (int i = 0; i < chart.notes.Count; i++)
                {
                    var n = chart.notes[i];
                    if (n.type == NoteType.LongEnd || n.type == NoteType.LongBody) continue;
                    float x = (n.time - scrollTime) * pixelsPerSecond;
                    if (x < -40f || x > view.width) continue;
                    float w = (n.type == NoteType.LongStart || n.type == NoteType.Slide) ? n.duration * pixelsPerSecond : 8f;
                    // 不同种类用「形状 + 字形 + 颜色」三重区分，避免长得一样
                    float h; string glyph;
                    switch (n.type)
                    {
                        case NoteType.Drag: glyph = "D"; h = rowH * .30f; break;
                        case NoteType.Flick: glyph = n.direction == FlickDirection.Up ? "↑" : "↓"; h = rowH * .72f; break;
                        case NoteType.Slide: glyph = "S"; h = rowH * .44f; break;
                        case NoteType.LongStart: glyph = "H"; h = rowH * .62f; break;
                        default: glyph = n.wide ? "W" : "T"; h = n.wide ? rowH * .82f : rowH * .46f; break;
                    }
                    var rect = new Rect(x, n.lane * rowH + (rowH - h) * .5f, Mathf.Max(12f, w), h);
                    EditorGUI.DrawRect(rect, NoteColor(n));
                    GUI.Label(new Rect(rect.x + 1f, rect.y, Mathf.Max(16f, rect.width), rect.height), glyph, GlyphStyle());
                    // Windows 式光标：长条两端显示左右缩放光标，中间显示移动光标
                    if (n.type == NoteType.LongStart || n.type == NoteType.Slide)
                        EditorGUIUtility.AddCursorRect(new Rect(rect.xMax - ResizeZonePx, rect.y, ResizeZonePx * 2f, rect.height),
                            MouseCursor.ResizeHorizontal);
                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);
                    if (n.type == NoteType.Slide && n.path != null && n.path.Count >= 2)
                    {
                        float x2 = x + n.path[n.path.Count - 1].t * pixelsPerSecond;
                        float y2 = Mathf.Clamp(n.path[n.path.Count - 1].x, 0f, Lanes - 1f) * rowH + rowH * .5f;
                        Handles.DrawLine(new Vector3(x, rect.y + rect.height * .5f), new Vector3(x2, y2));   // 跨轨路径
                        EditorGUI.DrawRect(new Rect(x2 - 3f, y2 - 3f, 6f, 6f), Color.white);
                    }
                    if (selection.Contains(n)) EditorGUI.DrawRect(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), new Color(1, 1, 1, .25f));
                }
            // 框选矩形
            if (marqueeActive)
            {
                float x0 = (Mathf.Min(marqueeStart.x, marqueeEnd.x) - scrollTime) * pixelsPerSecond;
                float x1 = (Mathf.Max(marqueeStart.x, marqueeEnd.x) - scrollTime) * pixelsPerSecond;
                float y0 = Mathf.Min(marqueeStart.y, marqueeEnd.y) * rowH;
                float y1 = (Mathf.Max(marqueeStart.y, marqueeEnd.y) + 1) * rowH;
                EditorGUI.DrawRect(new Rect(x0, y0, Mathf.Max(1f, x1 - x0), Mathf.Max(1f, y1 - y0)),
                    new Color(.55f, .8f, .85f, .18f));
            }
            GUI.EndClip();

            HandleTimelineInput(view, rowH);
            // 水平总览：拖这条等于左右滑动整段时间轴（配合中键拖动 / 滚轮）
            float total = Mathf.Max(8f, chart.GetTotalDuration() + (clip != null ? clip.length : 0f));
            float visible = view.width / pixelsPerSecond;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("时间轴", EditorStyles.miniLabel, GUILayout.Width(42));
                scrollTime = GUILayout.HorizontalSlider(scrollTime, 0f, Mathf.Max(visible, total - visible));
                GUILayout.Label($"{scrollTime:0.00}s / {total:0.0}s", EditorStyles.miniLabel, GUILayout.Width(110));
            }
        }

        private GUIStyle glyphStyle;

        private GUIStyle GlyphStyle()
        {
            if (glyphStyle != null) return glyphStyle;
            glyphStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };
            glyphStyle.normal.textColor = new Color(.06f, .09f, .12f);
            return glyphStyle;
        }

        /// <summary>
        /// 预览面板：5 轨下落示意（判定线 + 提前量 1.4s），随播放/滚动实时更新。
        /// 现在是与游戏同色的示意；后续把游戏相机的 RenderTexture 贴到这里即可"边写边看实战画面"。
        /// </summary>
        private void DrawPreview()
        {
            Rect area = GUILayoutUtility.GetRect(10f, 210f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(.06f, .09f, .12f));
            float laneW = area.width / Lanes;
            float judgeY = area.y + area.height * .82f;
            for (int lane = 0; lane < Lanes; lane++)
                EditorGUI.DrawRect(new Rect(area.x + lane * laneW + 2f, area.y, laneW - 4f, area.height), new Color(.1f, .14f, .18f));
            EditorGUI.DrawRect(new Rect(area.x, judgeY, area.width, 2f), new Color(.9f, .95f, .95f, .55f));
            const float approach = 1.4f;
            float fall = judgeY - area.y;
            foreach (var n in chart.notes)
            {
                if (n.type == NoteType.LongBody || n.type == NoteType.LongEnd) continue;
                float dt = n.time - scrollTime;
                if (dt < -.15f || dt > approach) continue;
                float y = judgeY - dt / approach * fall;
                float cx = area.x + (n.lane + .5f) * laneW;
                var rect = new Rect(cx - laneW * .38f, y - 6f, laneW * .76f, 12f);
                if (n.type == NoteType.LongStart || n.type == NoteType.Slide)
                    EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, Mathf.Min(fall, n.duration / approach * fall)),
                        new Color(NoteColor(n).r, NoteColor(n).g, NoteColor(n).b, .35f));
                if (n.type == NoteType.Slide && n.path != null && n.path.Count >= 2)
                {
                    float ex = area.x + (Mathf.Clamp(n.path[n.path.Count - 1].x, 0f, Lanes - 1f) + .5f) * laneW;
                    float ey = judgeY - (dt + n.duration) / approach * fall;
                    Handles.DrawLine(new Vector3(cx, y), new Vector3(ex, ey));
                }
                EditorGUI.DrawRect(rect, NoteColor(n));
            }
            // 命中爆点：音符刚好到达判定线时炸开，并标出与音频的时间差（音画同步检查）
            double now = EditorApplication.timeSinceStartup;
            // 判定方式：播放头「跨过」音符时间点即触发——回放、倒回去重听都会重新炸；
            // 大跳（拖动滑条/换段）不刷爆点，避免一次性喷出一片。
            float from = lastPreviewTime, to = scrollTime;
            float lo = Mathf.Min(from, to), hi = Mathf.Max(from, to);
            if (hi - lo > .0001f && hi - lo < .5f)
            {
                foreach (var n in chart.notes)
                {
                    if (n.type == NoteType.LongBody || n.type == NoteType.LongEnd) continue;
                    if (n.time <= lo || n.time > hi) continue;
                    bursts.Add(new Burst { lane = n.lane, start = now, deltaMs = (n.time - scrollTime) * 1000f });
                }
            }
            lastPreviewTime = scrollTime;
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i];
                double age = now - b.start;
                if (age > BurstSeconds) { bursts.RemoveAt(i); continue; }
                float t = (float)(age / BurstSeconds);
                float alpha = 1f - t;
                float cx = area.x + (b.lane + .5f) * laneW;
                Handles.color = new Color(1f, .95f, .8f, alpha);
                Handles.DrawWireDisc(new Vector3(cx, judgeY, 0f), Vector3.forward, Mathf.Lerp(8f, laneW * .8f, t));
                EditorGUI.DrawRect(new Rect(cx - laneW * .38f * (1f - t), judgeY - 8f * (1f - t),
                                            laneW * .76f * (1f - t) * 2f, 16f * (1f - t)),
                    new Color(1f, 1f, 1f, alpha * .75f));
                if (t < .85f)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(1f, .95f, .8f, alpha);
                    GUI.Label(new Rect(cx - 44f, judgeY + 10f, 88f, 18f), $"{b.deltaMs:+0;-0;0}ms", EditorStyles.miniLabel);
                    GUI.color = prev;
                }
            }
            EditorGUI.DrawRect(new Rect(area.x, area.y, 2f, area.height), new Color(.9f, .95f, .95f, .25f));
        }

        private void HandleTimelineInput(Rect view, float rowH)
        {
            Event e = Event.current;
            if (!view.Contains(e.mousePosition)) { hoverTime = -1f; return; }
            hoverTime = scrollTime + (e.mousePosition.x - view.x) / pixelsPerSecond;
            if (e.type == EventType.ScrollWheel) { scrollTime = Mathf.Max(0f, scrollTime + e.delta.y * .5f); e.Use(); Repaint(); return; }
            if (e.type == EventType.MouseDrag && e.button == 2)          // 中键拖动 = 左右平移视图
            {
                scrollTime = Mathf.Max(0f, scrollTime - e.delta.x / pixelsPerSecond);
                e.Use(); Repaint(); return;
            }
            float time = scrollTime + (e.mousePosition.x - view.x) / pixelsPerSecond;
            int lane = Mathf.Clamp((int)((e.mousePosition.y - view.y) / rowH), 0, Lanes - 1);

            // 框选：在空白处按下并拖动
            if (pendingPlace && e.type == EventType.MouseDrag)
            {
                if ((e.mousePosition - pendingMouse).magnitude > 6f)   // 超过阈值 → 判定为框选
                {
                    pendingPlace = false;
                    marqueeActive = true;
                    marqueeStart = new Vector2(pendingTime, pendingLane);
                    marqueeEnd = new Vector2(time, lane);
                    ApplyMarqueeSelection();
                }
                e.Use(); Repaint(); return;
            }
            if (marqueeActive)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
                {
                    marqueeEnd = new Vector2(time, lane);
                    ApplyMarqueeSelection();
                    e.Use(); Repaint(); return;
                }
                if (e.type == EventType.MouseUp)
                {
                    marqueeActive = false;
                    e.Use(); Repaint(); return;
                }
            }
            if (e.type == EventType.MouseDrag && dragIndex >= 0 && dragIndex < chart.notes.Count)
            {
                MarkDirty();
                var note = chart.notes[dragIndex];
                if (dragResize)
                {
                    note.duration = Mathf.Max(.05f, Snap(time) - note.time);
                    if (note.type == NoteType.Slide && note.path != null && note.path.Count >= 2)
                    {
                        note.path[0] = new SlidePathPoint { t = 0f, x = note.lane };
                        note.path[note.path.Count - 1] = new SlidePathPoint { t = note.duration, x = note.path[note.path.Count - 1].x };
                    }
                }
                else
                {
                    // 基准必须是「拖动开始时的原点」：用 note.time（每帧都在变）会把上一帧位移重复累加，
                    // 表现为围绕吸附点来回横跳、永远拖不到目标位置。
                    float desired = Snap(time - dragGrabOffset);
                    int primary = dragNotes.IndexOf(note);
                    float originTime = primary >= 0 ? dragOrigins[primary].x : note.time;
                    MoveDragged(desired - originTime, lane);
                }
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseUp && dragIndex >= 0)
            {
                var dragged = chart.notes[dragIndex];
                dragIndex = -1;
                chart.SortNotes();                       // 排序会重排下标，选中项按对象重新定位
                selected = chart.notes.IndexOf(dragged);
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseUp && pendingPlace)            // 没有拖动 → 放置音符
            {
                pendingPlace = false;
                AddNote(pendingTime, pendingLane);
                e.Use(); Repaint(); return;
            }
            if (e.type != EventType.MouseDown) return;
            int hit = HitTest(time, lane);
            if (e.button == 1)                                     // 右键删除
            {
                if (hit >= 0)
                {
                    var doomed = chart.notes[hit];
                    chart.notes.RemoveAt(hit);
                    selection.Remove(doomed);
                    selected = -1;
                    MarkDirty();
                }
                e.Use(); Repaint(); return;
            }
            if (hit >= 0)
            {
                var note = chart.notes[hit];
                if (e.shift)
                {
                    if (!selection.Remove(note)) selection.Add(note);   // Shift 点选：加入/移出
                    selected = chart.notes.IndexOf(note);
                    e.Use(); Repaint(); return;
                }
                if (!selection.Contains(note)) { selection.Clear(); selection.Add(note); }
                selected = hit;
                dragIndex = hit;
                bool resizable = note.type == NoteType.LongStart || note.type == NoteType.Slide;
                float noteX = (note.time - scrollTime) * pixelsPerSecond;
                float tailX = noteX + note.duration * pixelsPerSecond;
                dragResize = resizable
                             && (e.alt || Mathf.Abs(e.mousePosition.x - view.x - tailX) <= ResizeZonePx);
                dragGrabOffset = time - note.time;
                CaptureDragOrigins();
                e.Use(); Repaint(); return;
            }
            // 空白处：按下先待放置，拖动才转框选；Shift+拖动 为追加框选（不放置）
            if (!e.shift) selection.Clear();
            pendingPlace = !e.shift;
            pendingTime = Snap(time);
            pendingLane = lane;
            pendingMouse = e.mousePosition;
            marqueeStart = new Vector2(time, lane);
            marqueeEnd = marqueeStart;
            e.Use(); Repaint(); return;
        }

        // 拖拽时记录所有被选音符的原始时间/轨道，按增量整体移动（避免逐帧累积误差）
        private readonly List<NoteData> dragNotes = new List<NoteData>();
        private readonly List<Vector2> dragOrigins = new List<Vector2>();

        private void CaptureDragOrigins()
        {
            dragNotes.Clear();
            dragOrigins.Clear();
            foreach (var n in selection)
            {
                dragNotes.Add(n);
                dragOrigins.Add(new Vector2(n.time, n.lane));
            }
        }

        private void MoveDragged(float deltaT, int lane)
        {
            if (dragNotes.Count == 0) return;
            bool single = dragNotes.Count == 1;
            int laneShift = single ? lane - (int)dragOrigins[0].y : 0;   // 多选只平移时间，不改轨道
            for (int i = 0; i < dragNotes.Count; i++)
            {
                var n = dragNotes[i];
                n.time = Mathf.Max(0f, dragOrigins[i].x + deltaT);
                if (laneShift != 0)
                {
                    n.lane = Mathf.Clamp((int)dragOrigins[i].y + laneShift, 0, Lanes - 1);
                    if (n.type == NoteType.Slide && n.path != null)
                        for (int p = 0; p < n.path.Count; p++)
                            n.path[p] = new SlidePathPoint { t = n.path[p].t, x = Mathf.Clamp(n.path[p].x + laneShift, 0f, Lanes - 1f) };
                }
            }
        }

        private void ApplyMarqueeSelection()
        {
            float t0 = Mathf.Min(marqueeStart.x, marqueeEnd.x), t1 = Mathf.Max(marqueeStart.x, marqueeEnd.x);
            int l0 = Mathf.Min((int)marqueeStart.y, (int)marqueeEnd.y);
            int l1 = Mathf.Max((int)marqueeStart.y, (int)marqueeEnd.y);
            selection.Clear();
            foreach (var n in chart.notes)
            {
                if (n.type == NoteType.LongBody || n.type == NoteType.LongEnd) continue;
                if (n.time >= t0 && n.time <= t1 && n.lane >= l0 && n.lane <= l1) selection.Add(n);
            }
            selected = selection.Count > 0 ? chart.notes.IndexOf(selection[0]) : -1;
        }

        private int HitTest(float time, int lane)
        {
            for (int i = 0; i < chart.notes.Count; i++)
            {
                var n = chart.notes[i];
                if (n.lane != lane || n.type == NoteType.LongEnd || n.type == NoteType.LongBody) continue;
                float end = n.time + ((n.type == NoteType.LongStart || n.type == NoteType.Slide) ? n.duration : 8f / pixelsPerSecond);
                if (time >= n.time - 0.04f && time <= end + 0.04f) return i;
            }
            return -1;
        }

        private void AddNote(float time, int lane)
        {
            var note = new NoteData(lane, time, placeType);
            note.wide = wide && (placeType == NoteType.Normal || placeType == NoteType.LongStart);
            if (placeType == NoteType.LongStart || placeType == NoteType.Slide)
                note.duration = Mathf.Max(.05f, durationBeats * 60f / Mathf.Max(1f, chart.metadata.bpm));
            if (placeType == NoteType.Flick) note.direction = flickDirection;
            if (placeType == NoteType.Slide)
            {
                note.path = new List<SlidePathPoint>
                {
                    new SlidePathPoint { t = 0f, x = lane },
                    new SlidePathPoint { t = note.duration, x = slideEndLane }
                };
            }
            chart.notes.Add(note);
            chart.SortNotes();
            selection.Clear();
            selection.Add(note);
            selected = chart.notes.IndexOf(note);
            MarkDirty();
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("删除选中", GUILayout.Width(80)) && selected >= 0 && selected < chart.notes.Count)
                { DeleteSelection(); }
                if (GUILayout.Button("复制", GUILayout.Width(50))) CopySelection();
                if (GUILayout.Button("粘贴到光标", GUILayout.Width(90))) PasteClipboard();
                if (GUILayout.Button("清空", GUILayout.Width(60)))
                { chart.notes.Clear(); selection.Clear(); selected = -1; MarkDirty(); }
                GUILayout.FlexibleSpace();
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                    "音符 {0} · 选中 {1} · 剪贴板 {2} · 编辑头 {3:0.00}s   （左键点空白=放置 · 左键拖动=框选 · Shift+点=加选 · 中键=平移）",
                    chart.notes.Count, selection.Count, clipboard.Count, scrollTime));
            }
            var warnings = Validate();
            if (warnings.Count > 0)
                foreach (var w in warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            else
                EditorGUILayout.HelpBox("校验通过：无同轨同刻重复、同刻不超过 2 音、长按时长有效。", MessageType.Info);
        }

        private List<string> Validate()
        {
            var list = new List<string>();
            var byTime = new Dictionary<int, int>();
            var seen = new HashSet<string>();
            foreach (var n in chart.notes)
            {
                if (n.type == NoteType.LongEnd || n.type == NoteType.LongBody) continue;
                if ((n.type == NoteType.LongStart || n.type == NoteType.Slide) && n.duration <= 0f)
                    list.Add("长按/滑条时长必须大于 0（t=" + n.time.ToString("0.00") + "）");
                int key = Mathf.RoundToInt(n.time * 100f);
                byTime.TryGetValue(key, out int c);
                byTime[key] = c + 1;
                if (!seen.Add(key + "/" + n.lane)) list.Add("同轨同刻重复音（t=" + n.time.ToString("0.00") + " lane=" + n.lane + "）");
            }
            foreach (var kv in byTime)
                if (kv.Value > 2) { list.Add("同刻超过 2 音（t=" + (kv.Key / 100f).ToString("0.00") + "，共 " + kv.Value + " 音）"); break; }
            return list;
        }

        private static Color NoteColor(NoteData n)
        {
            switch (n.type)
            {
                case NoteType.Drag: return new Color(.55f, .85f, .8f);
                case NoteType.Flick: return n.direction == FlickDirection.Up ? new Color(.95f, .8f, .35f) : new Color(.95f, .55f, .3f);
                case NoteType.Slide: return new Color(.45f, .9f, .6f);
                case NoteType.LongStart: return new Color(.6f, .7f, .95f);
                default: return n.wide ? new Color(.95f, .95f, .95f) : LaneColors[Mathf.Clamp(n.lane, 0, Lanes - 1)];
            }
        }

        private void NewChart()
        {
            chart = new ChartData();
            chart.metadata.songName = "新谱面";
            chart.metadata.songArtist = "";
            chart.metadata.chartAuthor = "";
            chart.metadata.bpm = 140f;
            chart.metadata.audioFileName = "";
            chart.metadata.formatVersion = 2;   // v2 → 5 轨
            chart.metadata.noteCounts = new NoteCounts();
            selected = -1;
        }

        private void Load()
        {
            if (!File.Exists(chartPath)) { Debug.LogWarning("[ChartEditor] 文件不存在：" + chartPath); return; }
            chart = ChartLoader.LoadFromJson(File.ReadAllText(chartPath));
            hasUnsavedChanges = false;
            burstFired.Clear();
            if (chart != null && chart.metadata.formatVersion < 2)
                Debug.LogWarning("[ChartEditor] 该谱面是 v1（4 轨）；保存时会按 5 轨 v2 写回。");
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(chartPath));
            File.WriteAllText(chartPath, ToV2Json(chart));
            AssetDatabase.Refresh();
            hasUnsavedChanges = false;
            Debug.Log("[ChartEditor] 已保存：" + chartPath);
        }

        /// <summary>标记未保存改动（关窗时 Unity 会据此提示保存）。</summary>
        private void MarkDirty()
        {
            hasUnsavedChanges = true;
            saveChangesMessage = "谱面有未保存的改动，保存到 " + chartPath + " 吗？";
        }

        /// <summary>按 v2 协议序列化（JsonUtility 写不了字符串枚举，这里手写）。</summary>
        private static string ToV2Json(ChartData c)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            var m = c.metadata;
            sb.Append("{\n  \"metadata\": {\n");
            sb.Append("    \"songName\": \"").Append(m.songName).Append("\",\n");
            sb.Append("    \"songArtist\": \"").Append(m.songArtist).Append("\",\n");
            sb.Append("    \"chartAuthor\": \"").Append(m.chartAuthor).Append("\",\n");
            sb.Append("    \"difficulty\": ").Append((int)m.difficulty).Append(",\n");
            sb.Append("    \"level\": ").Append(m.level).Append(",\n");
            sb.Append("    \"bpm\": ").Append(m.bpm.ToString("0.###", inv)).Append(",\n");
            sb.Append("    \"offset\": ").Append(m.offset.ToString("0.###", inv)).Append(",\n");
            sb.Append("    \"audioFileName\": \"").Append(m.audioFileName).Append("\",\n");
            sb.Append("    \"previewStartTime\": ").Append(m.previewStartTime.ToString("0.###", inv)).Append(",\n");
            sb.Append("    \"previewDuration\": ").Append(m.previewDuration.ToString("0.###", inv)).Append(",\n");
            sb.Append("    \"formatVersion\": 2\n  },\n  \"notes\": [\n");
            int tap = 0, hold = 0, drag = 0, flick = 0, slide = 0;
            for (int i = 0; i < c.notes.Count; i++)
            {
                var n = c.notes[i];
                if (n.type == NoteType.LongBody || n.type == NoteType.LongEnd) continue;
                string type = n.type == NoteType.LongStart ? "hold" : n.type == NoteType.Slide ? "slide"
                    : n.type == NoteType.Drag ? "drag" : n.type == NoteType.Flick ? "flick" : "tap";
                if (type == "tap") tap++; else if (type == "hold") hold++; else if (type == "drag") drag++;
                else if (type == "flick") flick++; else slide++;
                sb.Append("    {\"type\": \"").Append(type).Append("\", \"lane\": ").Append(n.lane)
                  .Append(", \"time\": ").Append(n.time.ToString("0.###", inv))
                  .Append(", \"duration\": ").Append(n.duration.ToString("0.###", inv))
                  .Append(", \"direction\": \"").Append(n.direction == FlickDirection.Down ? "down" : "up").Append("\"")
                  .Append(", \"wide\": ").Append(n.wide ? "true" : "false");
                sb.Append(", \"path\": [");
                if (n.type == NoteType.Slide && n.path != null)
                    for (int p = 0; p < n.path.Count; p++)
                        sb.Append(p == 0 ? "" : ", ").Append("{\"t\": ").Append(n.path[p].t.ToString("0.###", inv))
                          .Append(", \"x\": ").Append(n.path[p].x.ToString("0.###", inv)).Append("}");
                sb.Append("]}").Append(i == c.notes.Count - 1 ? "\n" : ",\n");
            }
            sb.Append("  ],\n  \"noteCounts\": {\"tap\": ").Append(tap).Append(", \"hold\": ").Append(hold)
              .Append(", \"drag\": ").Append(drag).Append(", \"flick\": ").Append(flick).Append(", \"slide\": ").Append(slide).Append("},\n");
            sb.Append("  \"events\": []\n}\n");
            return sb.ToString();
        }
    }
}
#endif
