using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Data;
using FallenAngel.Core;
using FallenAngel.Live;
using FallenAngel.UI;
// 本工程的谱面 NoteType 与判定簇 NoteType 同名：这里以谱面协议为准（判定簇的枚举只在 Core/Live 内部使用）
using NoteType = FallenAngel.Data.NoteType;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 单个音符的视图组件
    /// 负责音符的下落移动、视觉效果、判定状态显示
    /// </summary>
    public class Note : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Image noteImage;          // 音符图像
        // 轨道颜色统一取自 LaneColors（Moonscraper/Rock Band 标准：红黄蓝绿），不在此重复定义

        /// <summary>音符数据</summary>
        public NoteData Data { get; private set; }

        /// <summary>是否已被判定（非Miss）</summary>
        public bool IsJudged { get; private set; }

        /// <summary>
        /// 对应的判定音符（Core/Live 判定簇；长按尾/身体条目共用头部的判定音符）。
        /// 视图只读它拿"按住中/进度/最终结果"，不再自己算判定。
        /// </summary>
        public LiveNoteBase Live { get; private set; }

        /// <summary>命中/漏掉特效是否正在播（判定层用它判断"结果已给、FX 还没完"）</summary>
        public bool FxPlaying { get; private set; }

        /// <summary>判定层给出结果的时刻（歌曲时间；FX 兜底回收用）</summary>
        public float JudgedAtSongTime { get; private set; }

        /// <summary>判定结果</summary>
        public JudgeResultType JudgeResult { get; private set; } = JudgeResultType.None;

        /// <summary>是否为长按音符且正在按住中</summary>
        public bool IsHolding { get; private set; }

        /// <summary>长按音符的进度 (0~1)</summary>
        public float HoldProgress { get; private set; }

        private RectTransform rectTransform;
        private bool isLongNoteConfigured;
        private Vector3 originalScale;
        private Color originalNoteColor;

        // 长按身体：运行时自生成（NoteBandGraphic 梯形带，零纹理/预制体依赖）
        // 带子宽度按"当前画布高度上轨道的半宽"取，所以身体与轨道在任意高度都严丝合缝
        private NoteBandGraphic bodyGraphic;
        private RectTransform bodyRect;
        private bool bodyCreatedLogged;
        private float bodyShrink = 1f;                   // 身体可见比例（1 = 全长；按住时随 HoldProgress 收缩）

        // ===== 视觉增强：近大远小 / 鼓件图标 / 命中扩散环 =====
        // 出生（屏幕顶部）时的缩放下限已统一到 PlayPerspective.MinScale（0.35，作者 2026-09-20 选型 B）；
        // 原来的线性 0.55 是没和轨道对齐的旧版，已删。
        private float hitScale = 1f;                     // 命中特效缩放（与透视分层，避免互抢 localScale）

        /// <summary>
        /// 普通音符宽度：配表里 tap = 130，而参考轨宽也是 130，
        /// 所以 **tap 恒定等于整条轨道宽**（作者 2026-09-20：音符任何时候都和轨道一样宽），
        /// 再乘透视缩放 PlayPerspective.SizeScale 保证任意高度都贴合梯形轨道。
        /// </summary>
        public static float DefaultNoteWidth
        {
            get { return 130f * LaneLayout.SizeScale; }
        }

        /// <summary>普通音符厚度（高度，不随轨道宽缩放；2026-09-17 设计稿定为 28）</summary>
        public const float DefaultNoteHeight = 28f;
        private DrumIconGraphic iconGraphic;             // 鼓件图标（运行时自生成，零资源）
        private ArrowGraphic arrowGraphic;               // Flick 方向箭头（运行时自生成）
        private NoteBandGraphic slidePathGraphic;        // Slide 轨迹带（运行时自生成，与长按身体同一把尺子）
        private RectTransform slidePathRect;
        private float slideEndCanvasY;                   // 轨迹终点相对头部的画布高度（按住时按剩余量收缩）
        private float lastRibbonProgress = -1f;          // 轨迹重建节流：上次重建时的进度/头部 x
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private bool loggedFlight;                       // 开发版：每颗音符只报一次"过线继续飞"
#endif
        private float lastRibbonHeadX = float.NaN;
        // 逐帧重算轨迹用的复用缓冲（滑条按住期间每帧都会重写，别每次新建 List）
        private readonly List<Vector2> slidePointBuffer = new List<Vector2>(16);
        // ===== 部件化（pjsk 风）：head / tail 部件（运行时自生成，零纹理）=====
        private NoteHeadGraphic headGraphic;             // 头部部件（绘制取代预制体 Image）
        private RectTransform headRect;
        private NoteHeadGraphic tailGraphic;             // 尾部部件（hold/slide 的结束标记）
        private RectTransform tailRect;
        // 同时押横向连线（Sonolus SIMULTANEOUS_CONNECTION 语义）
        private NoteLinkGraphic linkGraphic;
        private RectTransform linkRect;
        private Color linkBaseLeft = Color.clear;        // 连线两端基色（着色时按音符 alpha 统一缩放）
        private Color linkBaseRight = Color.clear;
        // Slide 剩余轨迹重算用（生成时记录，避免每帧重取）
        private float slideStartX;                       // slide 起点 Canvas X（尾部件定位用）
        // 生成时用过的下落时长 + 生成/判定位置：变速（FallSpeedController 实时生效）后要按新时长重建
        private float builtFallTime = -1f;
        private Vector2 builtSpawnPos;
        private Vector2 builtJudgePos;
        private readonly List<HitRingEntry> hitRingPool = new List<HitRingEntry>(8); // 命中扩散环池

        /// <summary>轨道 → 鼓件图标类型（对齐 Moonscraper 鼓件语义：0底鼓/1军鼓/2踩镲/3吊镲）</summary>
        private static readonly DrumIconType[] LaneIconTypes =
            { DrumIconType.Kick, DrumIconType.Snare, DrumIconType.HiHat, DrumIconType.Crash };

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null) noteImage = GetComponent<Image>();
            originalScale = transform.localScale;
            if (noteImage != null) originalNoteColor = noteImage.color;
        }

        /// <summary>
        /// 初始化音符
        /// </summary>
        /// <summary>
        /// 该音符使用的配色版本（由 NoteSpawner 在生成时决定；默认跟随当前选定调色板）。
        /// 每局三版混排：见 LaneColors.NoteMode / PickNotePalette。对象池复用时每次生成都会重设。
        /// </summary>
        public LaneColors.PaletteId Palette { get; set; } = LaneColors.ActivePalette;

        public void Initialize(NoteData data, Vector2 spawnPos, Vector2 judgeLinePos)
        {
            Initialize(data, null, spawnPos, judgeLinePos);
        }

        public void Initialize(NoteData data, LiveNoteBase live, Vector2 spawnPos, Vector2 judgeLinePos)
        {
            Data = data;
            Live = live;
            IsJudged = false;
            JudgeResult = JudgeResultType.None;
            IsHolding = false;
            HoldProgress = 0f;
            FxPlaying = false;
            JudgedAtSongTime = 0f;
            isLongNoteConfigured = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            loggedFlight = false;          // 池复用：清掉"已报过过线飞行"
#endif

            // 重置缩放和颜色（从对象池复用时必须重置）
            transform.localScale = originalScale;

            // 设置位置（先还原标准尺寸：kick 全宽条复用为普通音符时）
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            // 尺寸也走配表（2026-09-17：设计稿 v2 厚度 ×2，tap 130×28 / drag 94×24 / 尾 20）
            NoteVisualRow visual = NoteVisualConfig.Find(VisualKey(false));
            rectTransform.sizeDelta = visual != null
                ? new Vector2(visual.Width * LaneLayout.SizeScale, visual.Height)
                : new Vector2(DefaultNoteWidth, DefaultNoteHeight);
            rectTransform.anchoredPosition = spawnPos;
            gameObject.SetActive(true);

            // 部件化（pjsk 风）：头部部件接管绘制，预制体 Image 退化为尺寸容器（不再绘制）
            EnsureHeadGraphic();
            if (headGraphic != null) headGraphic.Shape = NoteVisualConfig.ShapeOf(VisualKey(false));
            SyncPartSizes();
            Color c = LaneColors.GetLaneColor(data.lane, Palette);
            ApplyPartsColor(c);

            // LongEnd/LongBody 只是长按的账本条目（判定与回收用），不参与绘制
            bool bookkeepingOnly = data.type == NoteType.LongEnd || data.type == NoteType.LongBody;
            if (headGraphic != null) headGraphic.gameObject.SetActive(!bookkeepingOnly);

            // 配置长按音符身体 / Slide 路径（二者互斥，另一者自动隐藏）
            ConfigureLongNoteBody(spawnPos, judgeLinePos);
            ConfigureSlidePath(spawnPos, judgeLinePos);

            // 尾部部件：hold/slide 的结束标记（与头部同族的圆角条，位置每帧跟随身体/路径末端）
            bool wantsTail = (data.type == NoteType.LongStart && data.duration > 0f) ||
                             (data.type == NoteType.Slide && data.path != null && data.path.Count >= 2);
            if (wantsTail && !bookkeepingOnly) EnsureTailGraphic();
            else if (tailGraphic != null)
                tailGraphic.gameObject.SetActive(false);
            if (tailGraphic != null) tailGraphic.Shape = NoteVisualConfig.ShapeOf(VisualKey(true));

            // 身体 / 路径 / 尾部件按轨道色统一着色（部件存在性已在上方确定）
            SyncPartSizes();
            ApplyPartsColor(c);

            // 同时押横向连线：由 NoteSpawner 在生成时按同刻伙伴设置；池复用先清掉上一条残留
            ClearSimultaneousLink();

            // 重置命中特效缩放，应用出生透视缩放（近大远小：顶部小、判定线大）
            hitScale = 1f;
            ApplyPerspectiveScale(0f);

            // 头部图标：flick → 方向箭头；slide → 无；4 键鼓谱 → 鼓件图标
            EnsureIcon(Data);
        }

        /// <summary>
        /// 配置长按音符的身体渲染（渐变透明：贴近头部实体→远端渐隐）
        /// </summary>
        private void ConfigureLongNoteBody(Vector2 spawnPos, Vector2 judgeLinePos)
        {
            if (Data.type != NoteType.LongStart || Data.duration <= 0f)
            {
                // 普通音符：隐藏身体（若该实例此前是长按，从池中复用后需隐藏）
                if (bodyGraphic != null)
                    bodyGraphic.gameObject.SetActive(false);
                return;
            }

            EnsureBodyGraphic();
            isLongNoteConfigured = true;
            // 池复用重置：带子的形状由"画布高度 → 轨道半宽"决定，缩放恒为 1
            //（收缩不再走 localScale，见 UpdatePosition —— 按 scaleY 压扁会让带子跟着变窄，反而脱离轨道）
            bodyRect.localScale = Vector3.one;
            bodyShrink = 1f;

            bodyGraphic.gameObject.SetActive(true);
            // 身体配色并入部件统一着色：贴近头部 0.9 实体 → 远端 0.18 半透明
            //（远端不再完全透明：尾部标记挂在身体末端，全透明会让连接段与尾标记断开）
            ApplyPartsColor(LaneColors.GetLaneColor(Data.lane, Palette));

            // 身体长度 = 按住期间音符下落的距离（不再叠加整条轨道高度），
            // 修复：此前 body = 轨道全长 + 按住距离，远超屏幕高度，观感像无限长
            float fallDistance = Mathf.Abs(spawnPos.y - judgeLinePos.y); // 轨道全长（下落距离）
            float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
            builtFallTime = fallTime; builtSpawnPos = spawnPos; builtJudgePos = judgeLinePos;
            float holdDistance = fallDistance * (Data.duration / fallTime); // 按住期间下落距离
            // 上限 = 一屏下落距离：长条再长，身体也只画到"生成点"为止——
            // 否则 4s 的条身体就有 1.8 屏高（15.6s 时 7 屏），会把后面来的音符整个盖住
            //（作者反馈"长音符拖到后来的音符，双指打不了"，根因就是这个视觉遮挡）。
            float bodyHeight = Mathf.Clamp(holdDistance, 300f, fallDistance);
            // 宽度只是矩形参考值：实际画出来的宽度由带子按轨道曲线逐高度取值（kick 全宽条走 SetKickVisual）
            float bodyWidth = Mathf.Max(24f, rectTransform.sizeDelta.x - 2f);
            bodyRect.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            // 底边贴住头部中心（pivot 在底部，anchoredPosition 必须为 0）：
            // 修复原版 bug——此前用 height/2 定位，pivot 又在底部，双重偏移导致身体整体在屏幕外
            bodyRect.anchoredPosition = Vector2.zero;
            // 带子几何按**画布单位**给（与 fallDistance / 轨道宽度同口径）
            bodyGraphic.SetStraightBand(bodyHeight);

            // 运行时自建的UI必须显式标记脏并强制Canvas立即重建，否则网格不会生成
            bodyGraphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();

            if (!bodyCreatedLogged)
            {
                bodyCreatedLogged = true;
                Debug.Log($"[Note] 长按身体配置完成: lane={Data.lane} size={bodyRect.sizeDelta} " +
                          $"bottom={bodyGraphic.bottomColor} top={bodyGraphic.topColor} " +
                          $"hasRenderer={bodyGraphic.canvasRenderer != null} hasCanvas={bodyGraphic.canvas != null}");
                StartCoroutine(LogBodyRenderStateNextFrame());
            }
        }

        /// <summary>
        /// 诊断用：配置后一帧输出身体的实际渲染状态（网格是否生成、激活状态、矩形）
        /// </summary>
        private System.Collections.IEnumerator LogBodyRenderStateNextFrame()
        {
            yield return null;
            if (bodyGraphic == null) yield break;

            Mesh mesh = bodyGraphic.canvasRenderer != null ? bodyGraphic.canvasRenderer.GetMesh() : null;
            int vertexCount = mesh != null ? mesh.vertexCount : -1;
            Debug.Log($"[Note] 身体渲染状态(配置后1帧): activeInHierarchy={bodyGraphic.gameObject.activeInHierarchy} " +
                      $"hasRenderer={bodyGraphic.canvasRenderer != null} meshVerts={vertexCount} rect={bodyRect.rect} " +
                      $"localPos={bodyGraphic.transform.localPosition} graphicEnabled={bodyGraphic.enabled} color={bodyGraphic.color}");
        }

        /// <summary>
        /// 确保长按身体存在：运行时自生成渐变图形节点，不依赖预制体是否带身体。
        /// 旧版预制体/默认Prefab上遗留的纯色身体节点（"LongNoteBody"）会被关闭。
        /// </summary>
        private void EnsureBodyGraphic()
        {
            if (bodyGraphic != null && bodyRect != null) return;

            // 关闭旧方案遗留的纯色身体（Image 节点）
            Transform legacy = transform.Find("LongNoteBody");
            if (legacy != null) legacy.gameObject.SetActive(false);

            GameObject bodyGO = new GameObject("LongNoteBody_Band", typeof(RectTransform));
            bodyGO.transform.SetParent(transform, false);
            bodyGO.transform.SetAsFirstSibling(); // 画在头部后面

            RectTransform rt = bodyGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(128f, 300f); // 宽对齐音符头部（130），留 2px 视觉边距

            // 带子（梯形）：宽度按每个高度的轨道半宽取，不再是等宽矩形
            NoteBandGraphic g = bodyGO.AddComponent<NoteBandGraphic>();
            // 显式补挂 CanvasRenderer（运行时 AddComponent 时 RequireComponent 不保证生效）
            if (bodyGO.GetComponent<CanvasRenderer>() == null)
                bodyGO.AddComponent<CanvasRenderer>();
            g.raycastTarget = false;

            bodyGraphic = g;
            bodyRect = rt;
        }

        /// <summary>
        /// 配置 Slide 轨迹带：路径点转为「相对头部中心的画布偏移」
        /// （x 取轨道坐标、y 随时间自头部向上展开——与长按身体同向）。
        /// 点按画布单位给（与 LaneLayout / 轨道宽度同口径），带子自己换算回局部单位。
        /// </summary>
        private void ConfigureSlidePath(Vector2 spawnPos, Vector2 judgeLinePos)
        {
            if (Data.type != NoteType.Slide || Data.path == null || Data.path.Count < 2)
            {
                // 非 Slide：隐藏路径（池复用残留）
                if (slidePathGraphic != null)
                    slidePathGraphic.gameObject.SetActive(false);
                return;
            }

            EnsureSlidePathGraphic();
            slidePathRect.localScale = Vector3.one; // 池复用重置
            slidePathRect.anchoredPosition = Vector2.zero;

            float fallDistance = Mathf.Abs(spawnPos.y - judgeLinePos.y); // 轨道全长（下落距离）
            float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
            builtFallTime = fallTime; builtSpawnPos = spawnPos; builtJudgePos = judgeLinePos;   // 变速重建用
            float startX = LaneLayout.GetXFromLaneCoord(Data.path[0].x);
            slideStartX = startX;                    // 尾部件定位用（未按住时头部在起点）

            slidePointBuffer.Clear();
            foreach (SlidePathPoint pt in Data.path)
            {
                float x = LaneLayout.GetXFromLaneCoord(pt.x) - startX;
                float y = fallDistance * pt.t / fallTime; // 路径自头部向上展开（与长按身体同向）
                slidePointBuffer.Add(new Vector2(x, y));
            }
            slidePathGraphic.SetPathPoints(slidePointBuffer);
            slidePathGraphic.gameObject.SetActive(true);
            // 引导线样式：V2 双线夹轨（2026-09-21 作者选型）——两条边线标出"判定跨度"的宽度，
            // 中间填充压到很淡，避免和音符抢视觉。
            slidePathGraphic.edgeLineWidth = 10f;
            slidePathGraphic.edgeLineColor = new Color(0.66f, 0.89f, 0.86f, 0.95f);
            lastRibbonProgress = -1f;      // 重置节流（池复用时也要重置）
            lastRibbonHeadX = float.NaN;
            // 轨迹终点相对头部的画布高度（= 谱面路径的总时长对应的下落距离），
            // 按住时按剩余比例收缩，尾部标记也用它（两边同源，尾部件才会正好落在轨迹末端）
            slideEndCanvasY = slidePointBuffer[slidePointBuffer.Count - 1].y;

            // 运行时自建的UI必须显式标记脏并强制Canvas立即重建，否则网格不会生成
            slidePathGraphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// 按住时重算剩余轨迹：原点在头部（t=from01 处），只画「还没滑过」的部分，
        /// 并按路径 x 与头部的差值做水平偏移——这样走过的轨迹会收起、尾部仍锚在谱面路径上。
        /// 点仍是画布单位（x 取轨道坐标、y 取剩余高度），与初始轨迹口径一致。
        /// </summary>
        private void UpdateSlideRibbon(float from01, float headX)
        {
            if (slidePathGraphic == null || Data.path == null || Data.path.Count < 2) return;
            // 节流：滑条按住时每帧重建轨迹 = 整块 Canvas 每帧重建。进度或头部位移没到阈值就不重算
            //（1% 进度 / 4px 位移在 1080 宽下肉眼看不到，但省掉绝大部分重算）。
            if (Mathf.Abs(from01 - lastRibbonProgress) < 0.01f &&
                Mathf.Abs(headX - lastRibbonHeadX) < 4f) return;
            lastRibbonProgress = from01;
            lastRibbonHeadX = headX;
            const int samples = 12;
            slidePointBuffer.Clear();
            for (int i = 0; i <= samples; i++)
            {
                float t = Mathf.Lerp(from01, 1f, i / (float)samples);
                float x = SampleSlidePathX(t) - headX;
                float y = slideEndCanvasY * (t - from01);
                slidePointBuffer.Add(new Vector2(x, y));
            }
            slidePathGraphic.SetPathPoints(slidePointBuffer);
        }

        /// <summary>确保 Slide 轨迹子节点存在：运行时自生成带子节点（与长按身体同一套图形）</summary>
        private void EnsureSlidePathGraphic()
        {
            if (slidePathGraphic != null && slidePathRect != null) return;

            GameObject go = new GameObject("SlidePath", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling(); // 画在头部后面（与长按身体同层）

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            NoteBandGraphic g = go.AddComponent<NoteBandGraphic>();
            // 显式补挂 CanvasRenderer（运行时 AddComponent 时 RequireComponent 不保证生效）
            if (go.GetComponent<CanvasRenderer>() == null)
                go.AddComponent<CanvasRenderer>();
            g.raycastTarget = false;

            slidePathGraphic = g;
            slidePathRect = rt;
        }

        /// <summary>
        /// 按进度采样 slide 路径 x（线性插值；path 异常时回退当前 x）
        /// </summary>
        private float SampleSlidePathX(float t01)
        {
            if (Data.path == null || Data.path.Count == 0)
                return rectTransform.anchoredPosition.x;
            if (Data.path.Count == 1)
                return LaneLayout.GetXFromLaneCoord(Data.path[0].x);

            System.Collections.Generic.List<SlidePathPoint> pts = Data.path;
            float total = Mathf.Max(0.001f, pts[pts.Count - 1].t);
            float time = t01 * total;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float t0 = pts[i].t, t1 = pts[i + 1].t;
                if (time >= t0 && time <= t1)
                {
                    float seg = (t1 > t0) ? (time - t0) / (t1 - t0) : 0f;
                    return LaneLayout.GetXFromLaneCoord(Mathf.Lerp(pts[i].x, pts[i + 1].x, seg));
                }
            }
            return LaneLayout.GetXFromLaneCoord(pts[pts.Count - 1].x);
        }

        // ============================================================
        // 部件化（pjsk 风）：head（头）/ body·ribbon（身）/ tail（尾）
        // 预制体 Image 只作尺寸容器，绘制全部交给运行时自生成的图形部件。
        // 绘制顺序（子节点先后）：身体 / 路径 → 尾 → 头 → 图标，头部圆角正好压住身体接缝。
        // ============================================================

        /// <summary>确保头部部件存在（圆角条：描边 + 内面渐变 + 顶面高光）</summary>
        /// <summary>
        /// 配表用的外观键：宽键优先（kick 是标记不是独立类型）；尾标记按类型映射到 LongEnd / SlideTail。
        /// </summary>
        private string VisualKey(bool tail)
        {
            if (Data == null) return tail ? "LongEnd" : "Normal";
            if (Data.wide && !tail) return "Wide";
            switch (Data.type)
            {
                case NoteType.Drag: return "Drag";
                case NoteType.Flick: return "Flick";
                case NoteType.LongStart: return tail ? "LongEnd" : "LongStart";
                case NoteType.Slide: return tail ? "SlideTail" : "Slide";
                default: return "Normal";
            }
        }

        private void EnsureHeadGraphic()
        {
            if (headGraphic != null && headRect != null) return;

            GameObject go = new GameObject("NoteHead",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(NoteHeadGraphic));
            go.transform.SetParent(transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = rectTransform.sizeDelta;

            // 显式补挂 CanvasRenderer（运行时 AddComponent 时 RequireComponent 不保证生效）
            if (go.GetComponent<CanvasRenderer>() == null) go.AddComponent<CanvasRenderer>();
            headGraphic = go.GetComponent<NoteHeadGraphic>();
            headGraphic.raycastTarget = false;
            headRect = rt;

            // 预制体自带的 Image 停绘（尺寸与池复用仍以根 RectTransform 为准）
            if (noteImage != null) noteImage.enabled = false;

            headGraphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>确保尾部部件存在（hold/slide 的结束标记，与头部同族）</summary>
        private void EnsureTailGraphic()
        {
            if (tailGraphic == null || tailRect == null)
            {
                GameObject go = new GameObject("NoteTail",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(NoteHeadGraphic));
                go.transform.SetParent(transform, false);
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;

                if (go.GetComponent<CanvasRenderer>() == null) go.AddComponent<CanvasRenderer>();
                tailGraphic = go.GetComponent<NoteHeadGraphic>();
                tailGraphic.raycastTarget = false;
                tailRect = rt;

                // 插到头部之前：身体 → 尾 → 头（头压在身体接缝上，连接处无缝）
                if (headRect != null) rt.SetSiblingIndex(headRect.GetSiblingIndex());
            }

            tailGraphic.gameObject.SetActive(true);
            tailGraphic.SetAllDirty();
        }

        /// <summary>
        /// 同时押横向连线：画出「本音符中心 → 伙伴音符中心」的渐变带（两端被各自头部盖住，
        /// 读起来是一条横跨两轨的音符条）。partnerCanvasX 传 NaN 表示清除（无同押伙伴 / 池复用）。
        /// </summary>
        public void SetSimultaneousLink(float partnerCanvasX, Color partnerColor)
        {
            if (float.IsNaN(partnerCanvasX))
            {
                ClearSimultaneousLink();
                return;
            }
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();

            float dx = partnerCanvasX - rectTransform.anchoredPosition.x;
            // 连线画在**两个音符的边缘之间**（原来画的是"中心到中心"，两端插进音符里 → 排版看着糊成一坨）
            float myHalf = rectTransform.sizeDelta.x * 0.5f;
            float width = Mathf.Abs(dx) - myHalf * 2f;
            if (width < 4f) { ClearSimultaneousLink(); return; }

            EnsureLinkGraphic();

            Color mine = LaneColors.GetLaneColor(Data != null ? Data.lane : 0, Palette);
            mine.a = 0.75f;
            partnerColor.a = 0.75f;
            linkBaseLeft = dx >= 0f ? mine : partnerColor;
            linkBaseRight = dx >= 0f ? partnerColor : mine;

            linkRect.sizeDelta = new Vector2(width, Mathf.Max(6f, rectTransform.sizeDelta.y * 0.45f));
            linkRect.anchoredPosition = new Vector2(dx * 0.5f, 0f);
            linkGraphic.LeftColor = linkBaseLeft;
            linkGraphic.RightColor = linkBaseRight;
            linkGraphic.gameObject.SetActive(true);
            linkGraphic.SetAllDirty();
        }

        /// <summary>清除同时押横向连线（无伙伴或池复用）</summary>
        public void ClearSimultaneousLink()
        {
            linkBaseLeft = Color.clear;
            linkBaseRight = Color.clear;
            if (linkGraphic != null) linkGraphic.gameObject.SetActive(false);
        }

        /// <summary>确保同时押连线子节点存在（画在头部之下，两端被头部盖住）</summary>
        private void EnsureLinkGraphic()
        {
            if (linkGraphic != null && linkRect != null) return;

            GameObject go = new GameObject("NoteLink",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(NoteLinkGraphic));
            go.transform.SetParent(transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            if (go.GetComponent<CanvasRenderer>() == null) go.AddComponent<CanvasRenderer>();
            linkGraphic = go.GetComponent<NoteLinkGraphic>();
            linkGraphic.raycastTarget = false;
            linkRect = rt;

            // 插到头部之前：连线在头部之下、身体与尾之上
            if (headRect != null) rt.SetSiblingIndex(headRect.GetSiblingIndex());

            linkGraphic.SetAllDirty();
        }

        /// <summary>同步部件矩形与音符矩形（kick 全宽条、池复用改尺寸后都要对齐）</summary>
        private void SyncPartSizes()
        {
            if (rectTransform == null) return;
            Vector2 size = rectTransform.sizeDelta;
            if (headRect != null) headRect.sizeDelta = size;

            if (tailRect != null && tailGraphic != null && tailGraphic.gameObject.activeSelf)
            {
                // slide 尾收窄，与同宽的 hold 尾区分
                float width = Data != null && Data.type == NoteType.Slide
                    ? Mathf.Max(20f, size.x - 56f)
                    : Mathf.Max(24f, size.x - 8f);
                tailRect.sizeDelta = new Vector2(width, Mathf.Max(8f, size.y * 0.72f));
            }
        }

        /// <summary>
        /// 全部可见部件统一着色（轨道语义色 / 命中淡出 / Miss 压暗都走这里）：
        /// 头与尾是实体部件用原色，身体与 slide 路径是同色半透明连接段。
        /// </summary>
        private void ApplyPartsColor(Color c)
        {
            if (noteImage != null) noteImage.color = c;   // Image 已停绘，仅保持数据一致
            if (headGraphic != null) headGraphic.color = c;

            if (tailGraphic != null && tailGraphic.gameObject.activeSelf)
            {
                Color t = Color.Lerp(c, Color.white, 0.25f);   // 尾标记略亮，便于辨认结束点
                t.a = Mathf.Clamp01(c.a * 0.92f);
                tailGraphic.color = t;
            }

            if (bodyGraphic != null && bodyGraphic.gameObject.activeSelf)
            {
                bodyGraphic.bottomColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * 0.9f));
                bodyGraphic.topColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * 0.18f));
                bodyGraphic.SetVerticesDirty();
            }

            if (slidePathGraphic != null && slidePathGraphic.gameObject.activeSelf)
            {
                // V2 双线夹轨：中间填充很淡（靠两条边线表达跨度宽度）
                slidePathGraphic.bottomColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * 0.22f));
                slidePathGraphic.topColor = new Color(c.r, c.g, c.b, 0f);
                slidePathGraphic.edgeLineColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * 0.95f));
                slidePathGraphic.SetVerticesDirty();
            }

            if (linkGraphic != null && linkGraphic.gameObject.activeSelf)
            {
                Color l = linkBaseLeft, r = linkBaseRight;
                l.a = Mathf.Clamp01(l.a * c.a);
                r.a = Mathf.Clamp01(r.a * c.a);
                linkGraphic.LeftColor = l;
                linkGraphic.RightColor = r;
                linkGraphic.SetVerticesDirty();
            }
        }

        /// <summary>只改透明度（命中淡出 / Miss 淡出），色相不变</summary>
        private void SetPartsAlpha(float alpha)
        {
            Color c = GetPartsColor();
            c.a = Mathf.Clamp01(alpha);
            ApplyPartsColor(c);
        }

        /// <summary>当前部件主色（头部优先；无部件时回退预制体 Image）</summary>
        private Color GetPartsColor()
        {
            if (headGraphic != null) return headGraphic.color;
            if (noteImage != null) return noteImage.color;
            return Color.white;
        }

        /// <summary>
        /// 尾部部件跟随：hold = 身体顶端（按住时随身体收缩下移）；slide = 轨迹末端。
        /// 位置按**画布单位**算出后再除以音符缩放：身体/轨迹的几何是画布单位，而尾部件是普通子节点
        /// （尺寸随音符缩放），不换算的话长按一长，尾标记就会离开带子末端。
        /// 尺寸同样按"所在高度"收窄，尾标记才会和轨道一样宽（否则顶端那一小段会戳出轨道）。
        /// </summary>
        /// <summary>
        /// 变速后按新下落时长重建"长条身体"与"滑条轨迹"。
        ///
        /// 为什么需要：下落速度是**实时生效**的（UI/FallSpeedController 每帧改 speedMultiplier），
        /// 而身体的长度 = 下落距离 × (duration ÷ 下落时长) 只在生成时算过一次——
        /// 局内改速度后，长条的长度与尾巴位置就和"音符实际走的距离"对不上了（作者反馈长音没做速度适配）。
        /// 只在速度真的变了时才重建（不是每帧），代价是一次网格重建。
        /// </summary>
        public void RefreshLongGeometryIfSpeedChanged()
        {
            if (Data == null || builtFallTime <= 0f) return;
            float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : builtFallTime;
            if (Mathf.Abs(fallTime - builtFallTime) < 0.01f) return;
            builtFallTime = fallTime;

            float fallDistance = Mathf.Abs(builtSpawnPos.y - builtJudgePos.y);
            if (Data.type == NoteType.LongStart && Data.duration > 0f && bodyRect != null && bodyGraphic != null)
            {
                float bodyHeight = Mathf.Clamp(fallDistance * (Data.duration / fallTime), 300f, fallDistance);
                bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight);
                bodyGraphic.SetStraightBand(bodyHeight);
                bodyGraphic.SetAllDirty();
            }
            else if (Data.type == NoteType.Slide && Data.path != null && Data.path.Count >= 2
                     && slidePathGraphic != null && slidePathGraphic.gameObject.activeSelf)
            {
                slidePointBuffer.Clear();
                foreach (SlidePathPoint pt in Data.path)
                {
                    float x = LaneLayout.GetXFromLaneCoord(pt.x) - slideStartX;
                    float y = fallDistance * pt.t / fallTime;
                    slidePointBuffer.Add(new Vector2(x, y));
                }
                slidePathGraphic.SetPathPoints(slidePointBuffer);
                slideEndCanvasY = slidePointBuffer[slidePointBuffer.Count - 1].y;
                slidePathGraphic.SetAllDirty();
            }
        }

        private void UpdateTailPart()
        {
            if (tailRect == null || tailGraphic == null || !tailGraphic.gameObject.activeSelf) return;
            if (Data == null) return;

            float noteScale = Mathf.Max(0.01f, transform.localScale.x);
            float canvasX = 0f, canvasY = 0f;

            if (Data.type == NoteType.LongStart && bodyRect != null)
            {
                canvasY = bodyRect.sizeDelta.y * Mathf.Clamp01(bodyShrink);
            }
            else if (Data.type == NoteType.Slide && Data.path != null && Data.path.Count >= 2)
            {
                float progress = IsHolding ? HoldProgress : 0f;
                float headX = IsHolding ? SampleSlidePathX(progress) : slideStartX;
                canvasX = SampleSlidePathX(1f) - headX;
                canvasY = slideEndCanvasY * (1f - progress);
            }
            else
            {
                return;
            }

            tailRect.anchoredPosition = new Vector2(canvasX / noteScale, canvasY / noteScale);

            float laneScale = PlayPerspective.SizeScaleAtY(CanvasY(canvasY));
            tailRect.localScale = new Vector3(laneScale / noteScale, laneScale / noteScale, 1f);
        }

        /// <summary>
        /// 本音符中心在画布中心系里的 y（可加一个局部偏移）。
        /// 层级约定：音符 → NotesContainer（全画布、无偏移）→ Canvas，由 PlayVisualChecks 守住。
        /// </summary>
        private float CanvasY(float localOffsetY)
        {
            float y = rectTransform.anchoredPosition.y + localOffsetY;
            RectTransform parent = transform.parent as RectTransform;
            if (parent != null && parent.anchorMin == parent.anchorMax) y += parent.anchoredPosition.y;
            return y;
        }

        /// <summary>
        /// 每帧更新音符位置（基于时间）
        /// </summary>
        /// <param name="currentSongTime">当前歌曲时间（秒）</param>
        /// <param name="spawnPosY">生成位置Y坐标</param>
        /// <param name="judgePosY">判定线位置Y坐标</param>
        public void UpdatePosition(float currentSongTime, float spawnPosY, float judgePosY)
        {
            if (Data == null || GameManager.Instance == null) return;

            float fallTime = GameManager.Instance.ActualFallTime;
            // 进度 0 = 刚生成， 1 = 到达判定线
            float timeToJudge = Data.time - currentSongTime;
            float rawProgress = 1f - timeToJudge / fallTime;
            // D4 行为面：**漏掉的音符继续往下飞**（上游 `LerpUnclamped`）。
            // 命中/长按有各自的收尾（特效或跟随），只有 Miss 需要"飞过去"；
            // 上限 1.6 防止极端掉帧时飞太远（Miss 的淡出只有 0.3s，正常到不了）。
            bool flyingPast = IsJudged && JudgeResult == JudgeResultType.Miss && rawProgress > 1f;
            float progress = flyingPast ? Mathf.Min(rawProgress, 1.6f) : Mathf.Clamp01(rawProgress);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // 开发版自报：漏掉的音符是否真的越过判定线继续下移（D4 行为面）。
            // 真机没法锁竖屏（MIUI 拦 settings put），所以用日志替代像素取证。
            if (flyingPast && !loggedFlight)
            {
                loggedFlight = true;
                Debug.Log(string.Format("[Note] Miss 过线继续飞：p={0:0.###}（判定线 p=1）→ y={1:0.#}，比判定线低 {2:0.#}px",
                    progress, PlayPerspective.PositionY(progress, spawnPosY, judgePosY),
                    judgePosY - PlayPerspective.PositionY(progress, spawnPosY, judgePosY)));
            }
#endif

            // 先落定本帧的透视缩放：后面尾部件要按"画布单位 ÷ 当前缩放"定位，
            // 带子的网格重建也读它——放到最后会让尾标记慢一帧、跟着抖。
            ApplyPerspectiveScale(progress);

            if (Data.type == NoteType.LongEnd)
            {
                // LongEnd = 长按停止标记：依附头部，停在身体顶端（释放点），
                // 随 HoldProgress 下移，按住到点时恰好落在判定线。
                // 修复：此前判断自身 IsHolding（永远为 false），标记一直停在生成点
                // 不可见——表现为"长按没有停止处"。
                Note head = NoteSpawner.Instance != null
                    ? NoteSpawner.Instance.GetLongNoteHead(Data.longNoteId)
                    : null;
                if (head != null && head.IsHolding && head.Data.type == NoteType.LongStart)
                {
                    float fallDistance = Mathf.Abs(spawnPosY - judgePosY);
                    // 与身体长度同公式（含"一屏上限"）：尾部标记跟着被截断后的末端，才不会飘到屏外
                    float holdDistance = Mathf.Clamp(fallDistance * (head.Data.duration / fallTime), 300f, fallDistance);
                    float y = judgePosY + holdDistance * (1f - Mathf.Clamp01(head.HoldProgress));
                    rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
                }
                // 未按住/已释放：停在生成点（离屏不可见），由 CleanupMissedNotes 静默回收
            }
            else
            {
                // 普通音符和长按头部正常下落（近大远小：位置走 PlayPerspective 的归一化曲线，
                // 尺寸由本方法末尾的 ApplyPerspectiveScale 统一施加，两处都不再自己算）
                float y = PlayPerspective.PositionY(progress, spawnPosY, judgePosY);
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
            }

            // 更新长按身体长度（按住时）
            if (Data.type == NoteType.LongStart && IsHolding && isLongNoteConfigured && bodyRect != null)
            {
                HoldProgress = Mathf.Clamp01((currentSongTime - Data.time) / Mathf.Max(0.01f, Data.duration));
                // 收缩不再靠 localScale.y：带子的宽度是"每个高度上轨道的半宽"，
                // 纵向压扁会把上半段挪到轨道更窄的高度，宽窄就对不上了。
                // 改成把「剩余长度」交给带子重采样（12 段 = 48 顶点，网格很小），底边始终贴住头部。
                bodyShrink = Mathf.Clamp01(1f - HoldProgress);
                bodyGraphic.SetStraightBand(bodyRect.sizeDelta.y * bodyShrink);
                bodyRect.anchoredPosition = Vector2.zero; // 底边始终贴住头部
            }

            // Slide 按住时：头部 x 沿路径采样（y 已被 progress 钳制在判定线），路径子节点随行
            if (Data.type == NoteType.Slide && IsHolding)
            {
                HoldProgress = Mathf.Clamp01((currentSongTime - Data.time) / Mathf.Max(0.01f, Data.duration));
                float headX = SampleSlidePathX(HoldProgress);
                rectTransform.anchoredPosition =
                    new Vector2(headX, rectTransform.anchoredPosition.y);
                UpdateSlideRibbon(HoldProgress, headX);   // 走过的部分收起，尾部锚在谱面路径上
            }

            // 尾部部件跟随身体/路径末端（hold 收缩后再算，故放在最后）
            UpdateTailPart();
        }

        /// <summary>
        /// 应用透视缩放：progress 0=出生（小）→ 1=判定线（原尺寸）。
        /// localScale = 透视缩放 * hitScale（命中特效分层，二者互不覆盖）。
        /// </summary>
        private void ApplyPerspectiveScale(float progress)
        {
            float s = PlayPerspective.SizeScale(progress) * hitScale;
            transform.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>
        /// 被判定命中
        /// </summary>
        public void JudgeHit(JudgeResultType result)
        {
            if (IsJudged) return;
            IsJudged = true;
            JudgeResult = result;
            JudgedAtSongTime = SongTimeNow();

            if (Data.type == NoteType.LongStart || Data.type == NoteType.Slide)
            {
                // 长按/Slide 头部命中 -> 进入按住状态
                IsHolding = true;
                // 视觉效果：头部/身体/尾一并压暗一档，标示"按住中"
                SetPartsAlpha(0.6f);
                SpawnHitRing(result);
            }
            else
            {
                // 普通音符和长按尾命中 -> 播放消失动画
                PlayHitEffect(result);
                SpawnHitRing(result);
            }
        }

        /// <summary>
        /// 长按收尾（判定层已经把结果算好：移植层 Long_End 窗口 + modifier 倍率）。
        /// 视图只负责表现，不再查窗口。
        /// </summary>
        public void JudgeHoldEnd(JudgeResultType result)
        {
            if (Data.type != NoteType.LongStart && Data.type != NoteType.Slide) return;
            IsHolding = false;
            JudgeResult = result;
            IsJudged = true;
            JudgedAtSongTime = SongTimeNow();
            PlayHitEffect(result);
            SpawnHitRing(result);
        }

        /// <summary>
        /// 长按收尾的**窗口换算**（保留给自检/调试：走移植层 Long_End 行 × 局内倍率）。
        /// 正式的收尾路径是判定层给结果 → <see cref="JudgeHoldEnd"/>。
        /// </summary>
        public JudgeResultType JudgeLongRelease(float releaseTimeDiff)
        {
            if (Data.type != NoteType.LongStart && Data.type != NoteType.Slide) return JudgeResultType.None;
            LiveConfig.SetJudgeTimeScale(PlayRules.JudgeWindowScale);
            JudgeResultType result = MapWindowResult(LiveConfig.CalculateLongEndNoteResult(releaseTimeDiff).Item1);
            // 长按至少按到Good才算成功
            if (result == JudgeResultType.Bad) result = JudgeResultType.Good;
            JudgeHoldEnd(result);
            return result;
        }

        /// <summary>移植层结果 → 本工程判定档（与 JudgeManager 同一口径）</summary>
        private static JudgeResultType MapWindowResult(NoteResult result)
        {
            switch (result)
            {
                case NoteResult.JustPerfect:
                case NoteResult.Perfect: return JudgeResultType.Perfect;
                case NoteResult.Great: return JudgeResultType.Great;
                case NoteResult.Good: return JudgeResultType.Good;
                case NoteResult.Bad: return JudgeResultType.Bad;
                case NoteResult.Miss: return JudgeResultType.Miss;
                default: return JudgeResultType.None;
            }
        }

        private static float SongTimeNow()
        {
            return GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
        }

        /// <summary>
        /// 标记为Miss
        /// </summary>
        public void JudgeMiss()
        {
            if (IsJudged && Data.type != NoteType.LongStart && Data.type != NoteType.Slide) return;
            IsJudged = true;
            IsHolding = false;
            JudgeResult = JudgeResultType.Miss;
            JudgedAtSongTime = SongTimeNow();
            PlayMissEffect();
        }

        /// <summary>
        /// 播放命中特效
        /// </summary>
        private void PlayHitEffect(JudgeResultType result)
        {
            StopAllCoroutines();
            FxPlaying = true;
            StartCoroutine(HitEffectCoroutine(result));
        }

        private System.Collections.IEnumerator HitEffectCoroutine(JudgeResultType result)
        {
            float duration = 0.2f;
            float timer = 0f;
            // 特效只驱动 hitScale：透视缩放由 UpdatePosition 每帧叠加，
            // 避免两者互抢 localScale（音符在 activeNotes 中直到动画结束，UpdatePosition 持续刷新）
            float from = hitScale;
            float to = 1.5f;

            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = timer / duration;
                hitScale = Mathf.Lerp(from, to, t);
                SetPartsAlpha(1f - t);
                yield return null;
            }
            hitScale = to; // 定格放大态，回收时由 Recycle/Initialize 重置
            gameObject.SetActive(false);
            FxPlaying = false;
        }

        private void PlayMissEffect()
        {
            StopAllCoroutines();
            FxPlaying = true;
            StartCoroutine(MissEffectCoroutine());
        }

        private System.Collections.IEnumerator MissEffectCoroutine()
        {
            float duration = 0.3f;
            float timer = 0f;
            Color orig = GetPartsColor();
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = timer / duration;
                Color c = orig;
                c.r = Mathf.Lerp(orig.r, 0.3f, t);
                c.g = Mathf.Lerp(orig.g, 0.3f, t);
                c.b = Mathf.Lerp(orig.b, 0.3f, t);
                c.a = orig.a * (1f - t);
                ApplyPartsColor(c);
                yield return null;
            }
            gameObject.SetActive(false);
            FxPlaying = false;
        }

        /// <summary>
        /// 强制回收（超出屏幕后）
        /// </summary>
        public void Recycle()
        {
            StopAllCoroutines();
            // 重置缩放和颜色，防止从对象池复用时残留
            hitScale = 1f;
            FxPlaying = false;
            transform.localScale = originalScale;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 是否为 kick（底鼓）：任意键可判定（见 JudgeManager 宽音符轮询）。
        /// 4 键鼓谱 lane 0（历史语义）或 v2 wide 标记（任意轨道）。
        /// </summary>
        public bool IsKick => Data != null && (Data.wide ||
                             (Data.lane == 0 && GameManager.Instance != null &&
                              GameManager.Instance.CurrentChart != null &&
                              GameManager.Instance.CurrentChart.LaneCount == 4));

        /// <summary>
        /// Kick（底鼓）视觉：横跨四键的全宽横条（任意键触发）。
        /// 需在 Initialize 之后调用（覆盖宽度与 X 位置）。
        /// </summary>
        public void SetKickVisual(float fullWidth)
        {
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(fullWidth, rectTransform.sizeDelta.y);
            Vector2 pos = rectTransform.anchoredPosition;
            pos.x = 0f; // 横条居中跨四键
            rectTransform.anchoredPosition = pos;

            // 长按身体同步全宽（kick 长按未来可能出现；Initialize 时身体按标准宽生成）
            if (bodyRect != null && bodyGraphic != null && bodyGraphic.gameObject.activeSelf)
                bodyRect.sizeDelta = new Vector2(Mathf.Max(24f, fullWidth - 2f), bodyRect.sizeDelta.y);

            SyncPartSizes(); // 头/尾部件同步全宽（kick 横条）
        }

        // ============================================================
        // 鼓件图标（与谱面编辑器鼓件语义对应，白色半透明叠加在头部色块上）
        // ============================================================

        /// <summary>
        /// 确保头部图标子节点按音符类型就绪：
        ///   Flick → 方向箭头（ArrowGraphic）；Slide → 无图标（路径即视觉）；
        ///   4 键鼓谱其余类型 → 鼓件图标；5 键吉他谱其余类型 → 无图标。
        /// </summary>
        private void EnsureIcon(NoteData data)
        {
            // LongEnd/LongBody 只是长按的账本条目（判定/回收用），不绘制任何头部图标
            if (data.type == NoteType.LongEnd || data.type == NoteType.LongBody)
            {
                if (iconGraphic != null) iconGraphic.gameObject.SetActive(false);
                if (arrowGraphic != null) arrowGraphic.gameObject.SetActive(false);
                return;
            }

            if (data.type == NoteType.Flick)
            {
                if (iconGraphic != null) iconGraphic.gameObject.SetActive(false);
                EnsureArrowGraphic(data.direction);
                return;
            }

            if (arrowGraphic != null) arrowGraphic.gameObject.SetActive(false);

            if (data.type == NoteType.Slide)
            {
                if (iconGraphic != null) iconGraphic.gameObject.SetActive(false);
                return;
            }

            bool isFiveKey = GameManager.Instance != null && GameManager.Instance.CurrentChart != null &&
                             GameManager.Instance.CurrentChart.LaneCount == 5;
            if (isFiveKey)
            {
                if (iconGraphic != null) iconGraphic.gameObject.SetActive(false);
                return;
            }

            if (iconGraphic == null)
            {
                Transform existing = transform.Find("DrumIcon");
                if (existing != null) iconGraphic = existing.GetComponent<DrumIconGraphic>();

                if (iconGraphic == null)
                {
                    GameObject iconGO = new GameObject("DrumIcon",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(DrumIconGraphic));
                    iconGO.transform.SetParent(transform, false);
                    iconGO.transform.SetAsLastSibling(); // 画在头部色块之上（长按身体在最底层）
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.sizeDelta = new Vector2(26f, 26f);
                    irt.anchoredPosition = Vector2.zero;
                    iconGraphic = iconGO.GetComponent<DrumIconGraphic>();
                    iconGraphic.raycastTarget = false;
                }
            }

            iconGraphic.IconType = LaneIconTypes[Mathf.Clamp(data.lane, 0, LaneIconTypes.Length - 1)];
            iconGraphic.gameObject.SetActive(true);
        }

        /// <summary>确保 flick 箭头子节点存在并设置方向（运行时自生成）</summary>
        private void EnsureArrowGraphic(FlickDirection dir)
        {
            if (arrowGraphic == null)
            {
                GameObject go = new GameObject("FlickArrow",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(ArrowGraphic));
                go.transform.SetParent(transform, false);
                go.transform.SetAsLastSibling(); // 画在头部色块之上
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(26f, 26f);
                rt.anchoredPosition = Vector2.zero;
                arrowGraphic = go.GetComponent<ArrowGraphic>();
                arrowGraphic.raycastTarget = false;
            }

            arrowGraphic.Direction = dir;
            arrowGraphic.gameObject.SetActive(true);

            // 运行时自建的UI必须显式标记脏并强制Canvas立即重建，否则网格不会生成
            // （此前漏了这一步，flick 箭头整体不显示——上下方向自然无区分）
            arrowGraphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        // ============================================================
        // 命中扩散环（判定线处向外扩散的音符色圆环，判定等级决定大小/亮度）
        // ============================================================

        /// <summary>命中特效基准直径（网格固定，扩散用 localScale —— 零网格重建）</summary>
        private const float EffectBaseSize = 32f;

        /// <summary>
        /// 命中特效（对齐设计稿预览图）：**双层扩散环**（外 = 轨道色 / 内 = 月白）+ **八向放射条**。
        /// 三点与预览一致：① 全部长在音符当前位置（不是判定线上的装饰）；
        /// ② 双层环同心、内环约为外环的 0.62；③ Perfect 更大更亮，其余判定小一号。
        /// 特效对象挂在 NotesContainer 下（与音符同一坐标系），协程挂在自身，不受音符回收影响。
        /// </summary>
        private void SpawnHitRing(JudgeResultType result)
        {
            if (result == JudgeResultType.Miss || result == JudgeResultType.None) return;
            if (transform.parent == null) return;

            bool perfect = result == JudgeResultType.Perfect;
            // 宽键（横条）是全宽条：特效**不按它的宽度等比例放大**，否则环会撑满整屏。
            // 统一按普通音符宽度取尺寸；位置仍取音符中心（横条本身已居中，见 SetKickVisual）。
            float noteW = Mathf.Min(rectTransform != null ? rectTransform.sizeDelta.x : DefaultNoteWidth,
                                    DefaultNoteWidth);
            float outerScale = noteW * (perfect ? 2.7f : 2.1f) / EffectBaseSize;
            Color laneColor = LaneColors.GetLaneColor(Data.lane, Palette);

            // 外环：轨道色
            Color outer = Color.Lerp(laneColor, Color.white, perfect ? 0.25f : 0f);
            outer.a = perfect ? 0.95f : 0.8f;
            SpawnRing(outer, outerScale, perfect ? 0.34f : 0.28f);

            // 内环：月白（更小更亮）
            Color inner = new Color(1f, 1f, 1f, perfect ? 0.85f : 0.6f);
            SpawnRing(inner, outerScale * 0.62f, perfect ? 0.26f : 0.22f);

            // 放射条：比外环略大，最短
            SpawnBurst(laneColor, outerScale * 1.15f, perfect ? 0.24f : 0.2f);
        }

        private void SpawnRing(Color c, float endScale, float duration)
        {
            HitRingEntry entry = null;
            for (int i = 0; i < hitRingPool.Count; i++)
            {
                if (!hitRingPool[i].rt.gameObject.activeSelf) { entry = hitRingPool[i]; break; }
            }
            if (entry == null)
            {
                if (hitRingPool.Count >= 8) return; // 池满丢弃（极密连打兜底）
                entry = CreateHitRingEntry();
            }

            entry.rt.gameObject.SetActive(true);
            entry.rt.anchoredPosition = rectTransform.anchoredPosition; // 命中瞬间音符已在判定线
            entry.rt.sizeDelta = new Vector2(EffectBaseSize, EffectBaseSize);
            entry.rt.localScale = Vector3.one;
            if (entry.canvasGroup != null) entry.canvasGroup.alpha = 1f;
            entry.graphic.RingColor = c;
            entry.coroutine = entry.graphic.StartCoroutine(AnimateHitRing(entry, endScale, duration, c.a));
        }

        /// <summary>放射条池（预览里的八向闪光）</summary>
        private readonly List<BurstEntry> burstPool = new List<BurstEntry>(6);

        private void SpawnBurst(Color c, float endScale, float duration)
        {
            BurstEntry entry = null;
            for (int i = 0; i < burstPool.Count; i++)
            {
                if (!burstPool[i].rt.gameObject.activeSelf) { entry = burstPool[i]; break; }
            }
            if (entry == null)
            {
                if (burstPool.Count >= 6) return;
                GameObject go = new GameObject("HitBurst",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(BurstGraphic), typeof(CanvasGroup));
                go.transform.SetParent(transform.parent, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(EffectBaseSize, EffectBaseSize);
                BurstGraphic g = go.GetComponent<BurstGraphic>();
                g.raycastTarget = false;
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                go.SetActive(false);
                entry = new BurstEntry { rt = rt, graphic = g, canvasGroup = cg };
                burstPool.Add(entry);
            }

            entry.rt.gameObject.SetActive(true);
            entry.rt.anchoredPosition = rectTransform.anchoredPosition;
            entry.rt.localScale = Vector3.one;
            entry.graphic.color = c;
            if (entry.canvasGroup != null) entry.canvasGroup.alpha = 1f;
            entry.coroutine = entry.graphic.StartCoroutine(AnimateBurst(entry, endScale, duration));
        }

        private System.Collections.IEnumerator AnimateBurst(BurstEntry entry, float endScale, float duration)
        {
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float s = Mathf.Lerp(0.6f, endScale, t);
                entry.rt.localScale = new Vector3(s, s, 1f);
                if (entry.canvasGroup != null) entry.canvasGroup.alpha = 1f - t;
                yield return null;
            }
            entry.rt.localScale = Vector3.one;
            entry.rt.gameObject.SetActive(false);
            entry.coroutine = null;
        }

        private HitRingEntry CreateHitRingEntry()
        {
            GameObject go = new GameObject("HitRing",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(HitRingGraphic), typeof(CanvasGroup));
            go.transform.SetParent(transform.parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(32f, 32f);
            HitRingGraphic g = go.GetComponent<HitRingGraphic>();
            g.raycastTarget = false;
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            go.SetActive(false);

            HitRingEntry entry = new HitRingEntry { rt = rt, graphic = g, canvasGroup = cg };
            hitRingPool.Add(entry);
            return entry;
        }

        private System.Collections.IEnumerator AnimateHitRing(HitRingEntry entry, float endScale,
                                                              float duration, float baseAlpha)
        {
            float timer = 0f;

            // 帧数优化：扩散用 localScale、淡出用 CanvasGroup——全程不重建 UI 网格
            entry.rt.localScale = new Vector3(0.9f, 0.9f, 1f);
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float s = Mathf.Lerp(0.9f, endScale, t);
                entry.rt.localScale = new Vector3(s, s, 1f);
                if (entry.canvasGroup != null)
                    entry.canvasGroup.alpha = baseAlpha * (1f - t * t); // 先慢后快淡出
                yield return null;
            }

            entry.rt.localScale = Vector3.one;
            entry.rt.gameObject.SetActive(false);
            entry.coroutine = null;
        }

        /// <summary>命中放射条池条目</summary>
        private class BurstEntry
        {
            public RectTransform rt;
            public BurstGraphic graphic;
            public CanvasGroup canvasGroup;
            public Coroutine coroutine;
        }

        /// <summary>命中扩散环池条目</summary>
        private class HitRingEntry
        {
            public RectTransform rt;
            public HitRingGraphic graphic;
            public CanvasGroup canvasGroup;
            public Coroutine coroutine;
        }
    }
}
