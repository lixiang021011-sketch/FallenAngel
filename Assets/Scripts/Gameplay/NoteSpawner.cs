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

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 音符生成器 - 管理音符的创建、回收、下落更新
    /// 使用对象池复用音符以减少GC
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
        [SerializeField] private float[] lanePositionsX = new float[] { -225f, -75f, 75f, 225f };

        [Header("判定线和生成位置Y坐标（anchoredPosition Y）")]
        [Tooltip("判定线位置Y（音符到达此处需击中）")]
        [SerializeField] private float judgeLineY = -400f;

        [Tooltip("音符生成位置Y（屏幕上方）")]
        [SerializeField] private float spawnY = 600f;

        [Header("对象池设置")]
        [SerializeField] private int defaultPoolSize = 50;
        [SerializeField] private int maxPoolSize = 200;

        // 活动音符列表
        private List<Note> activeNotes = new List<Note>();
        // 按音轨和ID索引的音符字典（用于快速查找长按匹配）
        private Dictionary<int, Note> activeLongNotesById = new Dictionary<int, Note>();

        // 对象池（兼容型：Unity 2021+ 用官方，否则回退）
        private NoteObjectPool notePool;

        // 谱面扫描指针
        private int nextNoteIndex;
        private bool isPlaying;

        // 自动Miss阈值（音符过判定窗口多久后强制Miss，开局计算一次，避免热路径每帧分配）
        private float missThreshold;

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

            // 开局计算一次（JudgeWindows.Default 为共享实例；将来 roguelike 接入时改读 Rules）
            missThreshold = JudgeWindows.Default.badWindow + 0.1f;
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

            // 长按身体由 Note 运行时自生成（GradientImage 顶点色渐变），
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
            }
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= OnGameStart;
                GameManager.Instance.OnGameEnd -= OnGameEnd;
            }
        }

        private void OnGameStart()
        {
            ClearAllNotes();
            nextNoteIndex = 0;
            isPlaying = true;
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
                ReleaseNote(activeNotes[i]);
            }
            activeNotes.Clear();
            activeLongNotesById.Clear();
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

            // 3. 回收超出判定窗口太久的Miss音符
            CleanupMissedNotes(songTime);
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
                SpawnNote(notes[nextNoteIndex]);
                nextNoteIndex++;
            }
        }

        private void SpawnNote(NoteData data)
        {
            if (notePool == null) return;

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

            note.Initialize(data, spawnPos, judgePos);

            // 宽音符视觉：横跨全键的全宽横条（任意键触发，见 JudgeManager 宽音符轮询）。
            // 4 键鼓谱 lane 0（历史 kick 语义）或 v2 wide 标记（任意轨道）
            bool isDrumChart = GameManager.Instance != null && GameManager.Instance.CurrentChart != null &&
                               GameManager.Instance.CurrentChart.LaneCount == 4;
            if (data.wide || (isDrumChart && data.lane == 0))
            {
                float fullWidth = (LaneLayout.GetCenterXForActive(LaneLayout.ActiveLaneCount - 1) -
                                   LaneLayout.GetCenterXForActive(0)) + Note.DefaultNoteWidth;
                note.SetKickVisual(fullWidth);
            }

            activeNotes.Add(note);

            // 长按音符加入字典
            if (data.type == NoteType.LongStart && data.longNoteId >= 0)
            {
                activeLongNotesById[data.longNoteId] = note;
            }
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

        private void CleanupMissedNotes(float songTime)
        {
            for (int i = activeNotes.Count - 1; i >= 0; i--)
            {
                Note note = activeNotes[i];

                // 长按尾/身体不独立判定：跟随头部生命周期。
                // 头部结束（释放判定完成）后静默回收，不产生任何判定事件。
                // 修复：此前 LongEnd 会被自动Miss——正确完成的长按也会凭空蹦出 MISS、
                // 断连击、计 Miss 数，曲终无音符时也会跳 MISS。
                // 修复2：此前回收标记时会一并删除字典条目——标记在头部按下前就被
                // 回收（headHolding=false 是按下前的常态），导致释放时
                // GetLongNoteHead 查不到头、释放链断裂，命中头永久堆积在判定线。
                // 字典条目只由头部自身的清理路径删除（自动Miss/动画结束两处）。
                if (note.Data.type == NoteType.LongEnd || note.Data.type == NoteType.LongBody)
                {
                    bool headHolding = note.Data.longNoteId >= 0
                        && activeLongNotesById.TryGetValue(note.Data.longNoteId, out Note head)
                        && head != null && head.IsHolding;
                    if (!headHolding)
                    {
                        ReleaseNote(note);
                        activeNotes.RemoveAt(i);
                    }
                    continue;
                }

                // Drag：过窗静默回收（碰即 Perfect、永不 MISS 语义——不计 Miss、不发事件、不断连击）
                if (note.Data.type == NoteType.Drag)
                {
                    if (!note.IsJudged && songTime - note.Data.time > missThreshold)
                    {
                        ReleaseNote(note);
                        activeNotes.RemoveAt(i);
                    }
                    continue;
                }

                // 超窗自动 Miss：普通音符与"未按住的头部"（LongStart/Slide）。
                // 修复：此前 LongStart 无论是否按住都永不自动 Miss——漏按/按晚的
                // hold 头永久停在判定线堆积；现在仅"按住中"的头部才豁免
                // （提前松手由输入事件判定），未按住的超窗走自动 Miss。
                bool isHoldHead = note.Data.type == NoteType.LongStart || note.Data.type == NoteType.Slide;
                if (!note.IsJudged || (isHoldHead && note.IsHolding))
                {
                    bool heldHoldHead = isHoldHead && note.IsHolding;

                    // 兜底清扫：按住态但轨道早已松开、且已过结束时间+窗口
                    // （判定链断开的残留头），按 Miss 释放防堆积。
                    // 宽长按与 Slide 均由"任意键"维持（触屏跨轨拖动会切换轨道，
                    // 按单轨判断会误断），按任意键状态判断是否仍按住。
                    bool isWideNote = note.Data.wide ||
                        (GameManager.Instance != null && GameManager.Instance.CurrentChart != null &&
                         GameManager.Instance.CurrentChart.LaneCount == 4 && note.Data.lane == 0);
                    bool isSlideNote = note.Data.type == NoteType.Slide;
                    bool stillHeld = (isWideNote || isSlideNote) ? AnyLaneHeld() : IsLaneHeld(note.Data.lane);
                    if (heldHoldHead && !stillHeld &&
                        songTime > note.Data.time + note.Data.duration + missThreshold)
                    {
                        if (JudgeManager.Instance != null)
                            JudgeManager.Instance.HandleAutoMiss(note);
                        note.JudgeMiss();
                        if (note.Data.longNoteId >= 0)
                            activeLongNotesById.Remove(note.Data.longNoteId);
                        ReleaseNote(note);
                        activeNotes.RemoveAt(i);
                        continue;
                    }

                    if (!heldHoldHead)
                    {
                        float timeDiff = songTime - note.Data.time;
                        if (timeDiff > missThreshold)
                        {
                            // 通知JudgeManager
                            if (JudgeManager.Instance != null)
                                JudgeManager.Instance.HandleAutoMiss(note);

                            note.JudgeMiss();
                            if (note.Data.longNoteId >= 0)
                                activeLongNotesById.Remove(note.Data.longNoteId);
                            ReleaseNote(note);
                            activeNotes.RemoveAt(i);
                        }
                    }
                    // 按住中的头部（LongStart/Slide）：提前松手由输入事件判定
                }
                else if (!note.gameObject.activeSelf)
                {
                    // 已经隐藏的音符（被判定后动画结束）
                    if (note.Data.longNoteId >= 0)
                        activeLongNotesById.Remove(note.Data.longNoteId);
                    ReleaseNote(note);
                    activeNotes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 检查某音轨是否处于按住状态（由InputManager更新）
        /// </summary>
        private bool IsLaneHeld(int lane)
        {
            if (InputSystem.InputManager.Instance == null) return false;
            return InputSystem.InputManager.Instance.LanePressStates[lane];
        }

        /// <summary>任意轨道是否按住（宽音符 kick 语义用）</summary>
        private bool AnyLaneHeld()
        {
            if (InputSystem.InputManager.Instance == null) return false;
            bool[] states = InputSystem.InputManager.Instance.LanePressStates;
            for (int i = 0; i < states.Length; i++)
                if (states[i]) return true;
            return false;
        }

        /// <summary>
        /// 获取某音轨最靠近判定线、且未被判定的音符（用于命中判定）
        /// </summary>
        /// <param name="lane">音轨索引</param>
        /// <param name="includeLongEnds">是否包含长按尾部</param>
        /// <param name="onlyType">限定类型（null=不限；kick 轮询传 Normal，避免抢判长按头）</param>
        public Note GetClosestJudgableNote(int lane, bool includeLongEnds = true, NoteType? onlyType = null)
        {
            Note best = null;
            float songTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            float minDiff = float.MaxValue;

            for (int i = 0; i < activeNotes.Count; i++)
            {
                Note note = activeNotes[i];
                if (note == null || note.Data == null) continue;
                if (note.Data.lane != lane) continue;
                if (onlyType.HasValue && note.Data.type != onlyType.Value) continue;

                // 只处理可判定的类型
                if (note.Data.type == NoteType.LongEnd)
                {
                    if (!includeLongEnds) continue;
                    // LongEnd只有在对应LongStart处于Holding状态时才应判定
                    if (note.Data.longNoteId >= 0 && activeLongNotesById.TryGetValue(note.Data.longNoteId, out Note head))
                    {
                        if (!head.IsHolding) continue;
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (note.Data.type == NoteType.LongBody)
                {
                    continue; // LongBody不可直接判定
                }
                else // Normal / LongStart / Drag / Flick / Slide 等可点判定类型
                {
                    if (note.IsJudged) continue;
                }

                float diff = Mathf.Abs(songTime - note.Data.time);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    best = note;
                }
            }
            return best;
        }

        /// <summary>
        /// 获取最靠近判定线、未被判定的宽音符（kick 语义目标，JudgeManager 轮询用）：
        /// 4K 谱 lane 0 的音符（历史 kick 语义）或 data.wide 标记的音符（v2 协议扩展）。
        /// 仅 Normal/LongStart（长按尾/身体不独立判定；drag/flick/slide 无 kick 语义）。
        /// </summary>
        public Note GetClosestWideJudgableNote()
        {
            bool isDrumChart = GameManager.Instance != null && GameManager.Instance.CurrentChart != null &&
                               GameManager.Instance.CurrentChart.LaneCount == 4;
            float songTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            Note best = null;
            float minDiff = float.MaxValue;

            for (int i = 0; i < activeNotes.Count; i++)
            {
                Note note = activeNotes[i];
                if (note == null || note.Data == null || note.IsJudged) continue;
                if (note.Data.type != NoteType.Normal && note.Data.type != NoteType.LongStart) continue;
                bool isWide = note.Data.wide || (isDrumChart && note.Data.lane == 0);
                if (!isWide) continue;

                float diff = Mathf.Abs(songTime - note.Data.time);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    best = note;
                }
            }
            return best;
        }

        /// <summary>
        /// 获取指定longNoteId对应的头部音符
        /// </summary>
        public Note GetLongNoteHead(int longNoteId)
        {
            if (activeLongNotesById.TryGetValue(longNoteId, out Note head))
                return head;
            return null;
        }

        /// <summary>
        /// 释放音符回对象池
        /// </summary>
        public void ReleaseNote(Note note)
        {
            if (notePool != null && note != null)
            {
                notePool.Release(note);
            }
        }

        /// <summary>
        /// 获取音轨X坐标（anchoredPosition，动态布局）
        /// </summary>
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
