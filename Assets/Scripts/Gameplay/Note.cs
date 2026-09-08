using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Data;
using FallenAngel.Core;
using FallenAngel.UI;

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

        // 长按身体：运行时自生成（GradientImage 顶点色渐变，零纹理/预制体依赖）
        private GradientImage bodyGraphic;
        private RectTransform bodyRect;
        private bool bodyCreatedLogged;

        // ===== 视觉增强：近大远小 / 鼓件图标 / 命中扩散环 =====
        private const float PerspectiveMinScale = 0.55f; // 出生（屏幕顶部）时的缩放
        private float hitScale = 1f;                     // 命中特效缩放（与透视分层，避免互抢 localScale）

        /// <summary>普通音符宽度（与 NoteSpawner 默认预制体一致；kick 全宽条复用为普通音符时还原）</summary>
        public const float DefaultNoteWidth = 130f;

        /// <summary>普通音符厚度（高度；原 40 的三分之一）</summary>
        public const float DefaultNoteHeight = 14f;
        private DrumIconGraphic iconGraphic;             // 鼓件图标（运行时自生成，零资源）
        private ArrowGraphic arrowGraphic;               // Flick 方向箭头（运行时自生成）
        private SlidePathGraphic slidePathGraphic;       // Slide 路径折线（运行时自生成）
        private RectTransform slidePathRect;
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
        public void Initialize(NoteData data, Vector2 spawnPos, Vector2 judgeLinePos)
        {
            Data = data;
            IsJudged = false;
            JudgeResult = JudgeResultType.None;
            IsHolding = false;
            HoldProgress = 0f;
            isLongNoteConfigured = false;

            // 重置缩放和颜色（从对象池复用时必须重置）
            transform.localScale = originalScale;

            // 设置位置（先还原标准尺寸：kick 全宽条复用为普通音符时）
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(DefaultNoteWidth, DefaultNoteHeight);
            rectTransform.anchoredPosition = spawnPos;
            gameObject.SetActive(true);

            // 设置颜色（轨道语义色，与谱面编辑器 Moonscraper 一致）
            Color c = LaneColors.GetLaneColor(data.lane);
            if (noteImage != null)
            {
                noteImage.color = c;
                // 确保 Image 可见
                noteImage.enabled = true;
            }

            // 配置长按音符身体 / Slide 路径（二者互斥，另一者自动隐藏）
            ConfigureLongNoteBody(spawnPos, judgeLinePos);
            ConfigureSlidePath(spawnPos, judgeLinePos);

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
            bodyRect.localScale = Vector3.one; // 池复用重置（收缩走 scaleY）

            Color c = LaneColors.GetLaneColor(Data.lane);
            bodyGraphic.bottomColor = new Color(c.r, c.g, c.b, 0.9f); // 贴近头部：接近实体
            bodyGraphic.topColor = new Color(c.r, c.g, c.b, 0f);      // 远端：完全透明
            bodyGraphic.SetVerticesDirty(); // 复用实例时强制重绘顶点色
            bodyGraphic.gameObject.SetActive(true);

            // 身体长度 = 按住期间音符下落的距离（不再叠加整条轨道高度），
            // 修复：此前 body = 轨道全长 + 按住距离，远超屏幕高度，观感像无限长
            float fallDistance = Mathf.Abs(spawnPos.y - judgeLinePos.y); // 轨道全长（下落距离）
            float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
            float holdDistance = fallDistance * (Data.duration / fallTime); // 按住期间下落距离
            float bodyHeight = Mathf.Max(holdDistance, 300f);               // 最短拖尾保证可辨识
            float bodyWidth = Mathf.Max(24f, rectTransform.sizeDelta.x - 2f); // 宽对齐音符头部（留 2px 边距；kick 全宽条随行）
            bodyRect.sizeDelta = new Vector2(bodyWidth, bodyHeight);
            // 底边贴住头部中心（pivot 在底部，anchoredPosition 必须为 0）：
            // 修复原版 bug——此前用 height/2 定位，pivot 又在底部，双重偏移导致身体整体在屏幕外
            bodyRect.anchoredPosition = Vector2.zero;

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

            GameObject bodyGO = new GameObject("LongNoteBody_Gradient", typeof(RectTransform));
            bodyGO.transform.SetParent(transform, false);
            bodyGO.transform.SetAsFirstSibling(); // 画在头部后面

            RectTransform rt = bodyGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(128f, 300f); // 宽对齐音符头部（130），留 2px 视觉边距

            GradientImage g = bodyGO.AddComponent<GradientImage>();
            // 显式补挂 CanvasRenderer（运行时 AddComponent 时 RequireComponent 不保证生效）
            if (bodyGO.GetComponent<CanvasRenderer>() == null)
                bodyGO.AddComponent<CanvasRenderer>();
            g.raycastTarget = false;

            bodyGraphic = g;
            bodyRect = rt;
        }

        /// <summary>
        /// 配置 Slide 路径渲染：路径点转为相对头部中心的局部坐标
        /// （x 随轨道坐标、y 随时间，自头部向上展开——与长按身体同向），
        /// 子节点随头部移动，无需每帧重绘。
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
            float startX = LaneLayout.GetXFromLaneCoord(Data.path[0].x);

            System.Collections.Generic.List<Vector2> local =
                new System.Collections.Generic.List<Vector2>(Data.path.Count);
            foreach (SlidePathPoint pt in Data.path)
            {
                float lx = LaneLayout.GetXFromLaneCoord(pt.x) - startX;
                float ly = fallDistance * pt.t / fallTime; // 路径自头部向上展开（与长按身体同向）
                local.Add(new Vector2(lx, ly));
            }
            slidePathGraphic.SetLocalPoints(local);
            slidePathGraphic.gameObject.SetActive(true);

            // 运行时自建的UI必须显式标记脏并强制Canvas立即重建，否则网格不会生成
            slidePathGraphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>确保 Slide 路径子节点存在：运行时自生成折线图形节点</summary>
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

            SlidePathGraphic g = go.AddComponent<SlidePathGraphic>();
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
            float progress = 1f - Mathf.Clamp01(timeToJudge / fallTime);

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
                    float holdDistance = fallDistance * (head.Data.duration / fallTime); // 与身体长度同公式
                    float y = judgePosY + holdDistance * (1f - Mathf.Clamp01(head.HoldProgress));
                    rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
                }
                // 未按住/已释放：停在生成点（离屏不可见），由 CleanupMissedNotes 静默回收
            }
            else
            {
                // 普通音符和长按头部正常下落
                float y = Mathf.Lerp(spawnPosY, judgePosY, progress);
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
            }

            // 更新长按身体长度（按住时）
            if (Data.type == NoteType.LongStart && IsHolding && isLongNoteConfigured && bodyRect != null)
            {
                HoldProgress = Mathf.Clamp01((currentSongTime - Data.time) / Mathf.Max(0.01f, Data.duration));
                // 帧数优化：收缩用 localScale.y（pivot 在底部）——不重建网格
                bodyRect.localScale = new Vector3(1f, Mathf.Max(0f, 1f - HoldProgress), 1f);
                bodyRect.anchoredPosition = Vector2.zero; // 底边始终贴住头部
            }

            // Slide 按住时：头部 x 沿路径采样（y 已被 progress 钳制在判定线），路径子节点随行
            if (Data.type == NoteType.Slide && IsHolding)
            {
                HoldProgress = Mathf.Clamp01((currentSongTime - Data.time) / Mathf.Max(0.01f, Data.duration));
                rectTransform.anchoredPosition =
                    new Vector2(SampleSlidePathX(HoldProgress), rectTransform.anchoredPosition.y);
            }

            // 近大远小：越靠近判定线越大（与命中特效缩放分层叠加）
            ApplyPerspectiveScale(progress);
        }

        /// <summary>
        /// 应用透视缩放：progress 0=出生（小）→ 1=判定线（原尺寸）。
        /// localScale = 透视缩放 * hitScale（命中特效分层，二者互不覆盖）。
        /// </summary>
        private void ApplyPerspectiveScale(float progress)
        {
            float s = Mathf.Lerp(PerspectiveMinScale, 1f, Mathf.Clamp01(progress)) * hitScale;
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

            if (Data.type == NoteType.LongStart || Data.type == NoteType.Slide)
            {
                // 长按/Slide 头部命中 -> 进入按住状态
                IsHolding = true;
                // 视觉效果：稍微亮一点
                if (noteImage != null)
                {
                    Color c = noteImage.color;
                    c.a = 0.6f;
                    noteImage.color = c;
                }
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
        /// 长按释放判定
        /// </summary>
        public JudgeResultType JudgeLongRelease(float releaseTimeDiff)
        {
            if (Data.type != NoteType.LongStart && Data.type != NoteType.Slide) return JudgeResultType.None;
            IsHolding = false;

            JudgeResultType result = JudgeWindows.Default.Judge(releaseTimeDiff);
            // 长按至少按到Good才算成功
            if (result == JudgeResultType.Bad) result = JudgeResultType.Good;
            JudgeResult = result;
            IsJudged = true;
            PlayHitEffect(result);
            SpawnHitRing(result);
            return result;
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
            PlayMissEffect();
        }

        /// <summary>
        /// 播放命中特效
        /// </summary>
        private void PlayHitEffect(JudgeResultType result)
        {
            StopAllCoroutines();
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
                if (noteImage != null)
                {
                    Color c = noteImage.color;
                    c.a = 1f - t;
                    noteImage.color = c;
                }
                yield return null;
            }
            hitScale = to; // 定格放大态，回收时由 Recycle/Initialize 重置
            gameObject.SetActive(false);
        }

        private void PlayMissEffect()
        {
            StopAllCoroutines();
            StartCoroutine(MissEffectCoroutine());
        }

        private System.Collections.IEnumerator MissEffectCoroutine()
        {
            float duration = 0.3f;
            float timer = 0f;
            if (noteImage != null)
            {
                Color orig = noteImage.color;
                while (timer < duration)
                {
                    timer += Time.unscaledDeltaTime;
                    float t = timer / duration;
                    Color c = orig;
                    c.r = Mathf.Lerp(orig.r, 0.3f, t);
                    c.g = Mathf.Lerp(orig.g, 0.3f, t);
                    c.b = Mathf.Lerp(orig.b, 0.3f, t);
                    c.a = orig.a * (1f - t);
                    noteImage.color = c;
                    yield return null;
                }
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 强制回收（超出屏幕后）
        /// </summary>
        public void Recycle()
        {
            StopAllCoroutines();
            // 重置缩放和颜色，防止从对象池复用时残留
            hitScale = 1f;
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

        /// <summary>命中时生成扩散环（环挂在 NotesContainer 下，与音符同一坐标系；协程挂环自身，不受音符回收影响）</summary>
        private void SpawnHitRing(JudgeResultType result)
        {
            if (result == JudgeResultType.Miss || result == JudgeResultType.None) return;
            if (transform.parent == null) return;

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
            entry.rt.sizeDelta = new Vector2(32f, 32f); // 网格固定，扩散用 localScale（零网格重建）
            entry.rt.localScale = Vector3.one;
            if (entry.canvasGroup != null) entry.canvasGroup.alpha = 1f;

            Color c = LaneColors.GetLaneColor(Data.lane);
            // Perfect：向白色混合 + 更亮，视觉上更"硬"
            float perfectBlend = result == JudgeResultType.Perfect ? 1f : 0f;
            c = Color.Lerp(c, Color.white, perfectBlend * 0.45f);
            c.a = result == JudgeResultType.Perfect ? 0.9f : 0.75f;
            entry.graphic.RingColor = c;

            entry.coroutine = entry.graphic.StartCoroutine(AnimateHitRing(entry, result));
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

        private System.Collections.IEnumerator AnimateHitRing(HitRingEntry entry, JudgeResultType result)
        {
            float duration = result == JudgeResultType.Perfect ? 0.35f : 0.28f;
            float endScale = result == JudgeResultType.Perfect ? 5f : 3.8f;
            float timer = 0f;
            Color baseColor = entry.graphic.RingColor;

            // 帧数优化：扩散用 localScale、淡出用 CanvasGroup——全程不重建 UI 网格
            entry.rt.localScale = new Vector3(0.9f, 0.9f, 1f);
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(timer / duration);
                float s = Mathf.Lerp(0.9f, endScale, t);
                entry.rt.localScale = new Vector3(s, s, 1f);
                if (entry.canvasGroup != null)
                    entry.canvasGroup.alpha = baseColor.a * (1f - t * t); // 先慢后快淡出
                yield return null;
            }

            entry.rt.localScale = Vector3.one;
            entry.rt.gameObject.SetActive(false);
            entry.coroutine = null;
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
