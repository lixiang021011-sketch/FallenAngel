using System.Collections.Generic;
using UnityEngine;
#if UNITY_2021_1_OR_NEWER
using UnityEngine.Pool;
using NoteObjectPool = UnityEngine.Pool.ObjectPool<FallenAngel.Gameplay.Note>;
#else
using FallenAngel.Compat;
using NoteObjectPool = FallenAngel.Compat.SimpleObjectPool<FallenAngel.Gameplay.Note>;
#endif
using FallenAngel.Data;
using FallenAngel.Core;
using FallenAngel.Live;
// 同上：谱面 NoteType 优先（判定簇的同名枚举只在 Core/Live 内部使用）
using NoteType = FallenAngel.Data.NoteType;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 音符生成器 —— **纯视图**。判定规则不在这里（见 Core/Live 判定簇 + JudgeManager 薄壳）；
    /// 本类只负责：按谱面时刻生成音符对象（对象池）、每帧更新位置、在判定层给出结果后播放反馈并回收。
    ///
    /// 与判定层的接口：<see cref="FindViewNote"/>（判定音符 → 视图音符，判定结果落 FX 用）、
    /// <see cref="RegisterViewNote"/>（生成视图时登记）。判定音符由 JudgeManager 在建谱时按同一谱面顺序生成，
    /// 这里按 NoteData 取回对应关系。
    /// </summary>
    public class NoteSpawner : MonoBehaviour
    {
        public static NoteSpawner Instance { get; private set; }

        [Header("场景引用")]
        [Tooltip("音符父容器（RectTransform）")]
        [SerializeField] private RectTransform notesContainer;

        [Tooltip("音符Prefab（需包含Note组件）")]
        [SerializeField] private Note notePrefab;

        [Header("轨道X位置（兜底/编辑器Gizmos用；音符坐标统一取 LaneLayout 动态布局）")]
        // 兜底值（编辑器 Gizmos 用）。运行时音符坐标统一取 LaneLayout —— 2026-09-18 轨道加宽后
        // 4 键中心为 ±390 / ±130（节距 260、轨宽 234）；这里只作缺省，不再参与定位。
        [SerializeField] private float[] lanePositionsX = new float[] { -390f, -130f, 130f, 390f };

        [Header("判定线和生成位置Y坐标（anchoredPosition Y）")]
        [Tooltip("判定线位置Y（音符到达此处需击中）")]
        [SerializeField] private float judgeLineY = Core.PlayVisualSpec.JudgeLineY;   // 唯一来源见 Core/PlayVisualSpec

        [Tooltip("音符生成位置Y（屏幕上方）")]
        [SerializeField] private float spawnY = 600f;

        [Header("对象池设置")]
        [SerializeField] private int defaultPoolSize = 50;
        [SerializeField] private int maxPoolSize = 200;

        // 活动音符列表
        private List<Note> activeNotes = new List<Note>();
        // 按音轨和ID索引的音符字典（用于快速查找长按匹配）
        private Dictionary<int, Note> activeLongNotesById = new Dictionary<int, Note>();
        // 判定音符 → 视图音符（判定结果落 FX 用）
        private readonly Dictionary<LiveNoteBase, Note> viewByLive = new Dictionary<LiveNoteBase, Note>();

        // 对象池（兼容型：Unity 2021+ 用官方，否则回退）
        private NoteObjectPool notePool;

        // 谱面扫描指针
        private int nextNoteIndex;
        private bool isPlaying;

        /// <summary>同刻判定容差（秒）：与规则引擎 chord_split 的 ±1ms 同刻分组对齐</summary>
        private const float SimultaneousLinkTolerance = 0.0015f;

        /// <summary>判定层给出结果后，视图最多再留多久（FX 兜底回收；正常路径是 FX 自己隐藏）</summary>
        private const float JudgeKeepSeconds = 1.2f;

        public float JudgeLineY => judgeLineY;
        public float SpawnY => spawnY;
        public IReadOnlyList<Note> ActiveNotes => activeNotes;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 如果 notesContainer 没指定，尝试自动查找
            if (notesContainer == null)
            {
                notesContainer = GetComponent<RectTransform>();
                if (notesContainer == null)
                {
                    // 从父级查找
                    Transform t = transform;
                    while (t != null && notesContainer == null)
                    {
                        notesContainer = t.GetComponent<RectTransform>();
                        t = t.parent;
                    }
                }
                if (notesContainer == null)
                    Debug.LogWarning("[NoteSpawner] notesContainer 未指定且无法自动查找");
            }

            // 如果没手动指定 Prefab，自动创建一个
            if (notePrefab == null)
            {
                notePrefab = CreateDefaultNotePrefab();
            }

            InitializePool();

            // 登记演奏几何：轨道透视（PerspectiveLaneGraphic）与音符共用同一组 spawn/judge Y
            PlayPerspective.SetGeometry(spawnY, judgeLineY);
        }

        /// <summary>
        /// 自动创建一个基础的音符 Prefab
        /// </summary>
        private Note CreateDefaultNotePrefab()
        {
            GameObject go = new GameObject("DefaultNote", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.SetActive(false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(Note.DefaultNoteWidth, Note.DefaultNoteHeight);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            UnityEngine.UI.Image img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            img.raycastTarget = false; // 音符不拦截触摸

            // 长按身体由 Note 运行时自生成（NoteBandGraphic 梯形带：宽度逐高度贴合轨道），
            // 预制体不再包含身体节点，避免依赖与双重渲染
            Note note = go.AddComponent<Note>();
            // 自动绑定 noteImage
            var noteImageField = typeof(Note).GetField("noteImage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (noteImageField != null) noteImageField.SetValue(note, img);
            return note;
        }

        private void InitializePool()
        {
            notePool = new NoteObjectPool(
                createFunc: () =>
                {
                    if (notePrefab == null)
                    {
                        Debug.LogError("[NoteSpawner] 未配置Note Prefab!");
                        return null;
                    }
                    Note note = Instantiate(notePrefab, notesContainer);
                    note.gameObject.SetActive(false);
                    return note;
                },
                actionOnGet: (note) => { if (note != null) note.gameObject.SetActive(true); },
                actionOnRelease: (note) => { if (note != null) note.Recycle(); },
                actionOnDestroy: (note) => { if (note != null) Destroy(note.gameObject); },
                defaultCapacity: defaultPoolSize,
                maxSize: maxPoolSize
            );
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart += OnGameStart;
                GameManager.Instance.OnGameEnd += OnGameEnd;
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
            }
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= OnGameStart;
                GameManager.Instance.OnGameStart += OnGameStart;
                GameManager.Instance.OnGameEnd -= OnGameEnd;
                GameManager.Instance.OnGameEnd += OnGameEnd;
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
                GameManager.Instance.OnStateChanged += OnGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= OnGameStart;
                GameManager.Instance.OnGameEnd -= OnGameEnd;
                GameManager.Instance.OnStateChanged -= OnGameStateChanged;
            }
        }

        /// <summary>
        /// 进 Loading（加载页）时按谱面预热对象池。
        /// 目的：把实例化开销从"演奏中第一次用到"挪到加载页，避免演奏中的分配尖峰。
        /// </summary>
        private void OnGameStateChanged(GameState state)
        {
            if (state != GameState.Loading) return;
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.CurrentChart == null) return;
            PrewarmForChart(gm.CurrentChart, gm.ActualFallTime);
        }

        /// <summary>
        /// 按谱面预热对象池（返回预热后池中空闲音符数）。
        /// 需要多少见 <see cref="EstimateMaxSimultaneousNotes"/>：同屏最多同时存在几个音符。
        /// </summary>
        public int PrewarmForChart(ChartData chart, float fallTime)
        {
            if (chart == null || chart.notes == null) return 0;
            return PrewarmPool(EstimateMaxSimultaneousNotes(chart.notes, fallTime));
        }

        /// <summary>
        /// 预热到 target 个空闲音符（受 defaultPoolSize 下限与 maxPoolSize 上限约束）。
        /// Unity 官方 ObjectPool 的 defaultCapacity 只决定内部数组容量、不实例化对象，
        /// 所以这里显式 Get/Release 一圈把实例造出来。
        /// </summary>
        public int PrewarmPool(int target)
        {
            if (notePool == null) return 0;
            int want = Mathf.Clamp(target, defaultPoolSize, Mathf.Max(defaultPoolSize, maxPoolSize));
            var created = new List<Note>(want);
            while (notePool.CountInactive + created.Count < want)
            {
                Note note = notePool.Get();
                if (note == null) break;      // 预制体缺失等异常：不要在加载页死循环
                created.Add(note);
            }
            for (int i = 0; i < created.Count; i++) notePool.Release(created[i]);
            return notePool.CountInactive;
        }

        /// <summary>
        /// 估算谱面"同屏最大音符数"：把每个音符占屏时间看成一个区间
        /// [时刻 − 下落时长, 时刻 + 持续时长]（长按/滑条按持续时长占位），取最大重叠数。
        /// 纯函数，便于自检；空谱返回 1（池至少留一个）。
        /// </summary>
        public static int EstimateMaxSimultaneousNotes(IReadOnlyList<NoteData> notes, float fallTime)
        {
            if (notes == null || notes.Count == 0) return 1;
            float fall = Mathf.Max(0.05f, fallTime);

            var events = new List<Vector2>(notes.Count * 2);   // x = 时刻, y = ±1
            for (int i = 0; i < notes.Count; i++)
            {
                NoteData n = notes[i];
                if (n == null) continue;
                float duration = n.type == NoteType.LongStart || n.type == NoteType.Slide ? Mathf.Max(0f, n.duration) : 0f;
                events.Add(new Vector2(n.time - fall, 1f));
                events.Add(new Vector2(n.time + duration, -1f));
            }
            if (events.Count == 0) return 1;

            // 同一时刻先处理"离开"(-1)再处理"进入"(+1)：首尾相接的两个音符不算同屏
            events.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            int current = 0, max = 0;
            for (int i = 0; i < events.Count; i++)
            {
                current += events[i].y > 0f ? 1 : -1;
                if (current > max) max = current;
            }
            return Mathf.Max(1, max);
        }

        private void OnGameStart()
        {
            ClearAllNotes();
            nextNoteIndex = 0;
            isPlaying = true;
            // 每局换一次音符配色种子：同一局内混排稳定（同一音符不会中途变色），不同局之间会变
            LaneColors.SetNotePaletteSeed(Random.Range(1, 999999));
        }

        private void OnGameEnd()
        {
            isPlaying = false;
            // 给最后留一点时间让剩余音符走完
            Invoke(nameof(ClearAllNotes), 2f);
        }

        private void ClearAllNotes()
        {
            for (int i = activeNotes.Count - 1; i >= 0; i--)
            {
                UnlinkView(activeNotes[i]);
                ReleaseNote(activeNotes[i]);
            }
            activeNotes.Clear();
            activeLongNotesById.Clear();
            viewByLive.Clear();
            nextNoteIndex = 0;
        }

        private void Update()
        {
            if (!isPlaying || GameManager.Instance == null) return;
            if (GameManager.Instance.CurrentState != GameState.Playing) return;

            float songTime = GameManager.Instance.SongTime;
            float fallTime = GameManager.Instance.ActualFallTime;

            // 1. 生成应该出现的新音符
            SpawnDueNotes(songTime + fallTime);

            // 2. 更新所有活动音符位置
            UpdateAllNotePositions(songTime);

            // 3. 回收：判定层已给出结果 / 特效播完隐藏 / 长按跟随结束
            CleanupFinishedNotes(songTime);
        }

        /// <summary>
        /// 生成到期的音符
        /// </summary>
        private void SpawnDueNotes(float spawnTimeThreshold)
        {
            if (GameManager.Instance.CurrentChart == null) return;
            var notes = GameManager.Instance.CurrentChart.notes;

            while (nextNoteIndex < notes.Count && notes[nextNoteIndex].time <= spawnTimeThreshold)
            {
                SpawnNote(notes, nextNoteIndex);
                nextNoteIndex++;
            }
        }

        private void SpawnNote(IReadOnlyList<NoteData> notes, int index)
        {
            if (notePool == null) return;

            NoteData data = notes[index];
            Note note = notePool.Get();
            if (note == null) return;

            // 轨道 X 统一取自 LaneLayout（4K/5K 动态布局）；lanePositionsX 仅作 SceneBuilder 注入兜底。
            // Slide 起点用连续坐标插值（path[0].x 可为小数，如 1.5）
            float x;
            if (data.type == NoteType.Slide && data.path != null && data.path.Count > 0)
                x = LaneLayout.GetXFromLaneCoord(data.path[0].x);
            else
                x = LaneLayout.GetCenterXForActive(Mathf.Clamp(data.lane, 0, LaneLayout.ActiveLaneCount - 1));
            Vector2 spawnPos = new Vector2(x, spawnY);
            Vector2 judgePos = new Vector2(x, judgeLineY);

            // 判定音符（长按尾/身体条目返回 null：它们共用头的判定）
            LiveNoteBase live = JudgeManager.Instance != null
                ? JudgeManager.Instance.LiveNoteFor(data)
                : null;

            // 每个音符随机取一版配色（LaneColors.NoteMode 决定策略）
            note.Palette = LaneColors.PickNotePalette(data.lane, data.time);
            note.Initialize(data, live, spawnPos, judgePos);

            if (live != null) viewByLive[live] = note;

            // 宽音符视觉：横跨全键的全宽横条（判定上就是"跨度铺满所有轨道"的音符）
            // 4 键鼓谱 lane 0（历史 kick 语义）或 v2 wide 标记（任意轨道）
            bool isDrumChart = GameManager.Instance != null && GameManager.Instance.CurrentChart != null &&
                               GameManager.Instance.CurrentChart.LaneCount == 4;
            if (data.wide || (isDrumChart && data.lane == 0))
            {
                float fullWidth = (LaneLayout.GetCenterXForActive(LaneLayout.ActiveLaneCount - 1) -
                                   LaneLayout.GetCenterXForActive(0)) + Note.DefaultNoteWidth;
                note.SetKickVisual(fullWidth);
            }

            // 同时押：同刻右侧最近的伙伴 → 横向连线（协议上限 2 音；slide / 宽键不参与）。
            // 只由左侧音符画这条线，右侧音符不重复画（Initialize 已清除残留）
            int partner = FindSimultaneousPartner(notes, index, SimultaneousLinkTolerance);
            if (partner >= 0)
            {
                NoteData p = notes[partner];
                note.SetSimultaneousLink(
                    LaneLayout.GetCenterXForActive(Mathf.Clamp(p.lane, 0, LaneLayout.ActiveLaneCount - 1)),
                    // 用伙伴音符将采用的那一版配色，保证连线两端与音符本体同色
                    LaneColors.GetLaneColor(p.lane, LaneColors.PickNotePalette(p.lane, p.time)));
            }

            activeNotes.Add(note);

            // 长按音符加入字典（长按尾/身体的视图跟随头部）
            if (data.type == NoteType.LongStart && data.longNoteId >= 0)
            {
                activeLongNotesById[data.longNoteId] = note;
            }
        }

        /// <summary>
        /// 找同刻（±tolerance 秒）右侧最近的同押伙伴，返回其在 notes 中的索引，没有则 -1。
        /// 谱面按 (time, lane) 排序，同刻音符必在相邻段内，所以只扫前后相邻段、走出同刻段即停（不整表扫描）。
        /// 只认右侧伙伴：每条连线由左边那个音符负责画，避免两个音符各画一条重复线。
        /// 自检用 internal：NotePartChecks 用合成谱面直接验证配对规则。
        /// </summary>
        internal static int FindSimultaneousPartner(IReadOnlyList<NoteData> notes, int index, float tolerance)
        {
            if (notes == null || index < 0 || index >= notes.Count) return -1;
            NoteData me = notes[index];
            if (!CanLink(me)) return -1;

            int best = -1;
            int bestLane = int.MaxValue;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                for (int i = index + dir; i >= 0 && i < notes.Count; i += dir)
                {
                    NoteData other = notes[i];
                    if (Mathf.Abs(other.time - me.time) > tolerance) break; // 已排序：走出同刻段即可停
                    if (!CanLink(other)) continue;
                    if (other.lane <= me.lane || other.lane >= bestLane) continue;
                    bestLane = other.lane;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// 能否参与同时押连线：账本条目（LongBody/LongEnd）无视觉；
        /// slide 头部会沿路径离开轨道、宽键本就横跨全轨，二者连出来都对不上位置。
        /// </summary>
        private static bool CanLink(NoteData n)
        {
            if (n == null) return false;
            if (n.wide) return false;
            if (n.type == NoteType.Slide) return false;
            if (n.type == NoteType.LongEnd || n.type == NoteType.LongBody) return false;
            return true;
        }

        private void UpdateAllNotePositions(float songTime)
        {
            for (int i = 0; i < activeNotes.Count; i++)
            {
                Note note = activeNotes[i];
                if (!note.gameObject.activeSelf) continue;
                note.UpdatePosition(songTime, spawnY, judgeLineY);
            }
        }

        /// <summary>
        /// 回收视图对象。判定层已经在移植层里判完（含自动 Miss），这里只按"视图是否还需要"回收：
        /// ① 特效播完隐藏的 → 回池；② 判定层已收尾且没有特效在播（如 drag 静默过窗）→ 回池；
        /// ③ 长按尾/身体：头部不再按住 → 回池（跟随头部生命周期）。
        /// </summary>
        private void CleanupFinishedNotes(float songTime)
        {
            for (int i = activeNotes.Count - 1; i >= 0; i--)
            {
                Note note = activeNotes[i];
                if (note == null)
                {
                    activeNotes.RemoveAt(i);
                    continue;
                }

                NoteData data = note.Data;
                bool bookkeeping = data != null && (data.type == NoteType.LongEnd || data.type == NoteType.LongBody);
                if (bookkeeping)
                {
                    bool headHolding = data.longNoteId >= 0
                        && activeLongNotesById.TryGetValue(data.longNoteId, out Note head)
                        && head != null && head.IsHolding;
                    if (!headHolding)
                    {
                        ReleaseView(note, i);
                    }
                    continue;
                }

                if (!note.gameObject.activeSelf && !note.FxPlaying)
                {
                    ReleaseView(note, i);
                    continue;
                }

                // 判定层已收尾且没有特效在播（例：drag 协议内静默过窗）→ 直接回池
                if (note.Live != null && note.Live.State == NoteState.Done && !note.FxPlaying)
                {
                    ReleaseView(note, i);
                    continue;
                }

                // 兜底：判定层已收尾很久仍未消失（异常路径）→ 强制回收，避免堆积
                if (note.Live != null && note.Live.State == NoteState.Done &&
                    songTime - note.JudgedAtSongTime > JudgeKeepSeconds)
                {
                    ReleaseView(note, i);
                }
            }
        }

        private void ReleaseView(Note note, int index)
        {
            UnlinkView(note);
            if (note.Data != null && note.Data.longNoteId >= 0 &&
                (note.Data.type == NoteType.LongStart || note.Data.type == NoteType.Slide))
            {
                activeLongNotesById.Remove(note.Data.longNoteId);
            }
            ReleaseNote(note);
            activeNotes.RemoveAt(index);
        }

        private void UnlinkView(Note note)
        {
            if (note == null || note.Live == null) return;
            if (viewByLive.TryGetValue(note.Live, out Note mapped) && mapped == note)
            {
                viewByLive.Remove(note.Live);
            }
        }

        /// <summary>判定音符 → 视图音符（判定层落判定结果用）</summary>
        public Note FindViewNote(LiveNoteBase live)
        {
            if (live == null) return null;
            return viewByLive.TryGetValue(live, out Note note) ? note : null;
        }

        /// <summary>登记"判定音符 ↔ 视图音符"（视图自行生成时也可调用）</summary>
        public void RegisterViewNote(LiveNoteBase live, Note note)
        {
            if (live == null || note == null) return;
            viewByLive[live] = note;
        }

        /// <summary>获取指定longNoteId对应的头部音符</summary>
        public Note GetLongNoteHead(int longNoteId)
        {
            if (activeLongNotesById.TryGetValue(longNoteId, out Note head))
                return head;
            return null;
        }

        /// <summary>释放音符回对象池</summary>
        public void ReleaseNote(Note note)
        {
            if (notePool != null && note != null)
            {
                notePool.Release(note);
            }
        }

        /// <summary>获取音轨X坐标（anchoredPosition，动态布局）</summary>
        public float GetLaneX(int lane)
        {
            return LaneLayout.GetCenterXForActive(lane);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            notePool?.Dispose();
        }

        // ============================================================
        // 编辑器辅助：Gizmos 绘制音轨线和判定线
        // ============================================================
        private void OnDrawGizmosSelected()
        {
            if (notesContainer == null) return;
            Vector3 containerPos = notesContainer.position;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(
                new Vector3(containerPos.x - 500, containerPos.y + judgeLineY, 0),
                new Vector3(containerPos.x + 500, containerPos.y + judgeLineY, 0)
            );

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                new Vector3(containerPos.x - 500, containerPos.y + spawnY, 0),
                new Vector3(containerPos.x + 500, containerPos.y + spawnY, 0)
            );

            for (int i = 0; i < lanePositionsX.Length; i++)
            {
                Gizmos.color = new Color(1, 1, 0, 0.4f);
                Gizmos.DrawLine(
                    new Vector3(containerPos.x + lanePositionsX[i], containerPos.y + judgeLineY - 50, 0),
                    new Vector3(containerPos.x + lanePositionsX[i], containerPos.y + spawnY + 50, 0)
                );
            }
        }
    }
}
