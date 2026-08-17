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

        [Header("4个音轨的X位置（相对于notesContainer的anchoredPosition X）")]
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

            float x = lanePositionsX[Mathf.Clamp(data.lane, 0, 3)];
            Vector2 spawnPos = new Vector2(x, spawnY);
            Vector2 judgePos = new Vector2(x, judgeLineY);

            note.Initialize(data, spawnPos, judgePos);

            // Kick（lane 0）= 横跨四键的全宽横条（任意键触发，见 JudgeManager）
            if (data.lane == 0)
            {
                float fullWidth = (lanePositionsX[3] - lanePositionsX[0]) + Note.DefaultNoteWidth;
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
                if (note.Data.type == NoteType.LongEnd || note.Data.type == NoteType.LongBody)
                {
                    bool headHolding = note.Data.longNoteId >= 0
                        && activeLongNotesById.TryGetValue(note.Data.longNoteId, out Note head)
                        && head != null && head.IsHolding;
                    if (!headHolding)
                    {
                        if (note.Data.longNoteId >= 0)
                            activeLongNotesById.Remove(note.Data.longNoteId);
                        ReleaseNote(note);
                        activeNotes.RemoveAt(i);
                    }
                    continue;
                }

                // 对普通音符，超出判定窗口太久自动Miss
                if (!note.IsJudged || (note.Data.type == NoteType.LongStart && note.IsHolding))
                {
                    if (note.Data.type != NoteType.LongStart)
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
                    // LongStart 保持中：提前松手由输入事件判定（兜底逻辑见技术债清单）
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

        /// <summary>
        /// 获取某音轨最靠近判定线、且未被判定的音符（用于命中判定）
        /// </summary>
        /// <param name="lane">音轨索引</param>
        /// <param name="includeLongEnds">是否包含长按尾部</param>
        public Note GetClosestJudgableNote(int lane, bool includeLongEnds = true)
        {
            Note best = null;
            float songTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            float minDiff = float.MaxValue;

            for (int i = 0; i < activeNotes.Count; i++)
            {
                Note note = activeNotes[i];
                if (note == null || note.Data == null) continue;
                if (note.Data.lane != lane) continue;

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
                else // Normal 或 LongStart
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
        /// 获取音轨X坐标（anchoredPosition）
        /// </summary>
        public float GetLaneX(int lane)
        {
            if (lane < 0 || lane >= lanePositionsX.Length) return 0f;
            return lanePositionsX[lane];
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
