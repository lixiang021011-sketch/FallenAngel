using System.Collections.Generic;
using UnityEngine;
using FallenAngel.Data;
using FallenAngel.Core;
using FallenAngel.InputSystem;
using FallenAngel.Audio;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 判定管理器 - 监听输入事件，执行命中判定、计分、连击
    /// 协调 NoteSpawner、ScoreManager、AudioManager
    /// </summary>
    public class JudgeManager : MonoBehaviour
    {
        public static JudgeManager Instance { get; private set; }

        [Header("判定窗口")]
        [SerializeField] private JudgeWindows judgeWindows = new JudgeWindows();

        /// <summary>当前判定结果（供UI订阅显示）</summary>
        public event System.Action<JudgeResultType, int> OnJudgeResult; // (result, lane)

        /// <summary>
        /// 判定偏差事件（秒，正=按早了，负=按晚了），供 HUD 显示"早/晚"指示（Phigros 手感参考）。
        /// 仅非 Miss 判定触发。
        /// </summary>
        public event System.Action<float> OnJudgeBias;

        /// <summary>连击数更新事件</summary>
        public event System.Action<int, bool> OnComboUpdate; // (combo, isFullComboNow)

        /// <summary>分数更新事件</summary>
        public event System.Action<int> OnScoreUpdate; // (totalScore)

        /// <summary>各判定计数更新</summary>
        public event System.Action<int, int, int, int, int> OnJudgeCountsUpdate;
        // (perfect, great, good, bad, miss)

        // 判定计数
        public int PerfectCount { get; private set; }
        public int GreatCount { get; private set; }
        public int GoodCount { get; private set; }
        public int BadCount { get; private set; }
        public int MissCount { get; private set; }

        /// <summary>当前连击数</summary>
        public int Combo { get; private set; }
        /// <summary>最大连击数</summary>
        public int MaxCombo { get; private set; }
        /// <summary>当前总分</summary>
        public int Score { get; private set; }

        // 长按按下时的开始时间（用于长按释放判定）
        private float[] laneHoldStartTime = new float[0];
        private int[] laneHoldLongId = new int[0];
        // 按住中的宽长按头（kick 语义：任意键维持、全松即释放；单槽位）
        private Note activeWideHold;
        // 按住中的 Slide 头部（任意键维持、全松即释放；跨轨拖动不中断，见 Update）
        private readonly List<Note> activeSlides = new List<Note>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnLaneInput += HandleLaneInput;
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStart += ResetStats;
        }

        private void Start()
        {
            // 防止 Awake 执行顺序导致漏订阅
            if (InputManager.Instance != null)
            {
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
                InputManager.Instance.OnLaneInput += HandleLaneInput;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= ResetStats;
                GameManager.Instance.OnGameStart += ResetStats;
            }
        }

        private void OnDisable()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStart -= ResetStats;
        }

        private void Update()
        {
            // 宽音符（kick 语义）轮询：4K 谱 lane 0 或 v2 谱 wide 标记的音符，
            // 任意键触发——"按着任意键即可"语义。每帧轮询天然覆盖两种情况：
            // ① 按下瞬间恰好到线；② 按住期间才落到线（例如长按其他轨时 kick 到达）。
            // 宽长按头：按住任意键维持；全部键松开时按释放时刻判定。
            // 已判定（IsJudged）的音符会被 GetClosestWideJudgableNote 跳过，不重复计分。
            if (GameManager.Instance == null ||
                GameManager.Instance.CurrentState != GameState.Playing) return;
            if (NoteSpawner.Instance == null || InputManager.Instance == null) return;
            if (GameManager.Instance.CurrentChart == null) return;

            bool anyHeld = false;
            bool[] pressStates = InputManager.Instance.LanePressStates;
            for (int i = 0; i < pressStates.Length; i++)
            {
                if (pressStates[i]) { anyHeld = true; break; }
            }

            float songTime = GameManager.Instance.SongTime;

            // 宽音符按压判定
            if (anyHeld)
            {
                Note wide = NoteSpawner.Instance.GetClosestWideJudgableNote();
                if (wide != null)
                {
                    float timeDiff = songTime - wide.Data.time;
                    JudgeResultType result = judgeWindows.Judge(timeDiff);
                    if (result != JudgeResultType.Miss) // 窗口外：交给 auto-miss，不误判
                    {
                        if (wide.Data.type == NoteType.LongStart)
                        {
                            // 宽长按头：已有按住中的宽长按时不抢占（单槽位，防重叠宽长按互相覆盖）
                            if (activeWideHold == null)
                            {
                                ApplyJudge(wide, result, wide.Data.lane);
                                activeWideHold = wide;
                            }
                        }
                        else // Normal 宽音符（kick 单击）
                        {
                            ApplyJudge(wide, result, wide.Data.lane);
                        }
                    }
                }
            }

            // 宽长按 / Slide 释放：全部键松开即结束（释放时刻对结束时间判定）。
            // Slide 与宽长按同语义：由任意键维持——触屏跨轨拖动（输入层
            // 松旧轨+按新轨）时只要还有键按住就不会中断。
            // 修复：此前 Slide 挂在单轨释放事件上（HandleRelease），跨行即被释放，slide 中途停止。
            if (!anyHeld)
            {
                if (activeWideHold != null && activeWideHold.IsHolding)
                {
                    float endTime = activeWideHold.Data.time + activeWideHold.Data.duration;
                    ApplyHoldRelease(activeWideHold, songTime - endTime, activeWideHold.Data.lane);
                }
                activeWideHold = null;

                if (activeSlides.Count > 0)
                {
                    for (int i = 0; i < activeSlides.Count; i++)
                    {
                        Note slide = activeSlides[i];
                        if (slide == null || !slide.IsHolding) continue; // 防双判（兜底清扫已判过）
                        float endTime = slide.Data.time + slide.Data.duration;
                        ApplyHoldRelease(slide, songTime - endTime, slide.Data.lane);
                    }
                    activeSlides.Clear();
                }
            }
        }

        private void ResetStats()
        {
            PerfectCount = GreatCount = GoodCount = BadCount = MissCount = 0;
            Combo = 0;
            MaxCombo = 0;
            Score = 0;

            // 按当前活动键数分配轨道状态（4K/5K 切换时重建）
            int count = Mathf.Max(1, LaneLayout.ActiveLaneCount);
            laneHoldStartTime = new float[count];
            laneHoldLongId = new int[count];
            for (int i = 0; i < count; i++) laneHoldLongId[i] = -1;
            activeWideHold = null;
            activeSlides.Clear();

            OnScoreUpdate?.Invoke(0);
            OnComboUpdate?.Invoke(0, true);
            OnJudgeCountsUpdate?.Invoke(0, 0, 0, 0, 0);
        }

        /// <summary>
        /// 输入事件处理
        /// </summary>
        private void HandleLaneInput(object sender, LaneInputArgs e)
        {
            if (GameManager.Instance == null ||
                GameManager.Instance.CurrentState != GameState.Playing) return;

            if (e.isPressed)
                HandlePress(e.laneIndex);
            else
                HandleRelease(e.laneIndex);
        }

        private void HandlePress(int lane)
        {
            if (NoteSpawner.Instance == null)
            {
                Debug.LogWarning("[JudgeManager] NoteSpawner.Instance is null!");
                return;
            }
            float songTime = GameManager.Instance.SongTime;

            Note note = NoteSpawner.Instance.GetClosestJudgableNote(lane, false);

            if (note == null)
            {
                // 没找到音符 — 空按
#if UNITY_EDITOR
                Debug.Log($"[JudgeManager] Press lane={lane} songTime={songTime:F2} -> No note found (air press)");
#endif
                return;
            }

            float timeDiff = songTime - note.Data.time;
            JudgeResultType result = judgeWindows.Judge(timeDiff);

#if UNITY_EDITOR
            Debug.Log($"[JudgeManager] Press lane={lane} songTime={songTime:F2} noteTime={note.Data.time:F2} diff={timeDiff:F3} result={result}");
#endif

            if (result == JudgeResultType.Miss)
            {
                // 太远不判定（空按）；drag 音符保留等后续按（永不 MISS 语义）
                return;
            }

            // Drag：窗口内碰到即强制 Perfect（碰即 Perfect 语义）
            if (note.Data.type == NoteType.Drag)
                result = JudgeResultType.Perfect;

            // 命中
            ApplyJudge(note, result, lane);

            // 如果是长按，记录按住开始
            if (note.Data.type == NoteType.LongStart)
            {
                laneHoldStartTime[lane] = songTime;
                laneHoldLongId[lane] = note.Data.longNoteId;
            }

            // Slide 头部命中进入按住：登记到活动列表，由 Update 全松统一释放
            // （跨轨拖动不中断，见 Update 释放块）
            if (note.Data.type == NoteType.Slide && !activeSlides.Contains(note))
                activeSlides.Add(note);
        }

        private void HandleRelease(int lane)
        {
            float songTime = GameManager.Instance.SongTime;

            // 检查是否是长按的释放
            int longId = laneHoldLongId[lane];
            if (longId >= 0 && NoteSpawner.Instance != null)
            {
                Note longHead = NoteSpawner.Instance.GetLongNoteHead(longId);
                if (longHead != null && longHead.IsHolding)
                {
                    // 释放时间点 vs LongEnd的时间点
                    float longEndTime = longHead.Data.time + longHead.Data.duration;
                    ApplyHoldRelease(longHead, songTime - longEndTime, lane);
                }
                laneHoldLongId[lane] = -1;
            }
            // Slide 头部不在此释放：由任意键维持、全部松开时才结束（见 Update 释放块）。
            // 修复：此前挂在单轨释放事件上——触屏跨轨拖动（输入层先松旧轨再按新轨）
            // 会立即释放 Slide，跨行即中断。
        }

        /// <summary>
        /// 长按/Slide 释放判定：尾部才计分（头部不计分，见 ApplyJudge 门控），
        /// 计分/计数/事件与长按释放语义一致。
        /// </summary>
        private void ApplyHoldRelease(Note head, float releaseDiff, int lane)
        {
            JudgeResultType releaseResult = head.JudgeLongRelease(releaseDiff);
            GameManager.Instance?.Portfolio?.ReportJudge(head.Data, releaseResult);

            // 计数
            if (releaseResult == JudgeResultType.Miss)
            {
                ProcessMiss(lane);
            }
            else
            {
                if (JudgeWindows.BreaksCombo(releaseResult))
                    BreakCombo();
                else
                    AddScoreAndCombo(JudgeWindows.GetScore(releaseResult, true));
                AddJudgeCount(releaseResult);
                OnJudgeResult?.Invoke(releaseResult, lane);
                OnJudgeBias?.Invoke(-releaseDiff); // 正=早，负=晚
                PlayAudioJudge(releaseResult);
            }
        }

        /// <summary>
        /// 应用一次判定结果（非长按头部）
        /// </summary>
        private void ApplyJudge(Note note, JudgeResultType result, int lane)
        {
            GameManager.Instance?.Portfolio?.ReportJudge(note.Data, result);
            note.JudgeHit(result);

            if (result == JudgeResultType.Miss)
            {
                ProcessMiss(lane);
                return;
            }

            // 长按/Slide 头部仅触发视觉；Good/Bad 断连击不给分（Phigros 语义）。
            // Slide 尾部计分（见 ApplyHoldRelease），头部计分会导致双计分。
            if (note.Data.type != NoteType.LongStart && note.Data.type != NoteType.Slide)
            {
                if (JudgeWindows.BreaksCombo(result))
                    BreakCombo();
                else
                    AddScoreAndCombo(JudgeWindows.GetScore(result));
            }
            AddJudgeCount(result);
            OnJudgeResult?.Invoke(result, lane);
            OnJudgeBias?.Invoke(-(GameManager.Instance.SongTime - note.Data.time)); // 正=早，负=晚
            PlayAudioJudge(result);
        }

        /// <summary>
        /// 处理自动Miss（音符走过未击中）。
        /// 所有类型（含漏按的 LongStart/Slide 头）都计 Miss 并触发 HUD 事件；
        /// 此前 LongStart 永不自动 Miss，事件被刻意抑制——现已统一。
        /// </summary>
        public void HandleAutoMiss(Note note)
        {
            if (note == null) return;
            GameManager.Instance?.Portfolio?.ReportJudge(note.Data, JudgeResultType.Miss);
            ProcessMiss(note.Data.lane);
            OnJudgeResult?.Invoke(JudgeResultType.Miss, note.Data.lane);
            PlayAudioJudge(JudgeResultType.Miss);
        }

        private void ProcessMiss(int lane)
        {
            MissCount++;
            BreakCombo();
            OnJudgeCountsUpdate?.Invoke(PerfectCount, GreatCount, GoodCount, BadCount, MissCount);
        }

        /// <summary>
        /// 断连击（Miss/Good/Bad 共用；不增加 Miss 计数）
        /// </summary>
        private void BreakCombo()
        {
            Combo = 0;
            OnComboUpdate?.Invoke(Combo, false);
        }

        private void AddScoreAndCombo(int score)
        {
            Score += score;
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;

            // 连击加成（简化版）
            int comboBonus = Mathf.RoundToInt(score * (Mathf.Clamp01(Combo / 100f) * 0.2f));
            Score += comboBonus;

            OnScoreUpdate?.Invoke(Score);
            bool isFull = MissCount == 0;
            OnComboUpdate?.Invoke(Combo, isFull);
        }

        private void AddJudgeCount(JudgeResultType result)
        {
            switch (result)
            {
                case JudgeResultType.Perfect: PerfectCount++; break;
                case JudgeResultType.Great: GreatCount++; break;
                case JudgeResultType.Good: GoodCount++; break;
                case JudgeResultType.Bad: BadCount++; break;
            }
            OnJudgeCountsUpdate?.Invoke(PerfectCount, GreatCount, GoodCount, BadCount, MissCount);
        }

        private void PlayAudioJudge(JudgeResultType result)
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayHitSfx(result);
        }

        /// <summary>
        /// 计算当前的达成率（百分比，0~100）
        /// 权重与计分语义一致（Phigros 对齐）：Great≈65%，Good/Bad 0
        /// </summary>
        public float CalculateAccuracy()
        {
            float total = PerfectCount + GreatCount + GoodCount + BadCount + MissCount;
            if (total <= 0) return 100f;

            float weightSum =
                PerfectCount * 100f +
                GreatCount * 65f +
                GoodCount * 0f +
                BadCount * 0f +
                MissCount * 0f;
            return weightSum / total;
        }

        /// <summary>
        /// 获取评级字符串 (S~D)
        /// </summary>
        public string GetRank()
        {
            float acc = CalculateAccuracy();
            if (MissCount == 0 && PerfectCount >= GreatCount + GoodCount + BadCount) return "S";
            if (acc >= 95f) return "A";
            if (acc >= 85f) return "B";
            if (acc >= 70f) return "C";
            return "D";
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
