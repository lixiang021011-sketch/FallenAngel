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

        /// <summary>有效判定窗口（基础值 × 局内 modifier 倍率）；热路径读缓存实例，不分配</summary>
        private JudgeWindows EffectiveWindows { get { return PlayRules.Windows; } }

        /// <summary>
        /// 本帧是否已因"按下"判掉过一个音符。
        /// 用途：一次按下只能判掉**一个**音符——此前"按下事件（轨道音符）"与"Update 轮询（横条/拖键）"
        /// 会在同一帧各判一个，两个音符挨得近时表现为"点一次同时判两个"。
        /// </summary>
        private int pressJudgeFrame = -1;

        /// <summary>
        /// 同刻判定容差（秒）：一次按下可以同时判掉**真同刻**的多个音符（伪双押：横条 + 普通音符）。
        /// 只是"挨得近"（间隔超过此值）的音符仍需分别输入——两者不要混为一谈。
        /// </summary>
        private const float ChordPressTolerance = 0.02f;

        /// <summary>标记本帧的按下已经判掉过音符</summary>
        private void MarkPressJudged()
        {
            pressJudgeFrame = Time.frameCount;
        }

        /// <summary>本帧的按下是否已经判掉过音符</summary>
        private bool PressJudgedThisFrame
        {
            get { return pressJudgeFrame == Time.frameCount; }
        }

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
        // Slide 跟手：记录每个按住中的滑条"脱离路径"的累计秒数（回到容差内清零）
        private readonly Dictionary<Note, float> slideOutOfRange = new Dictionary<Note, float>();
        private const float SlideFollowToleranceLanes = 1.0f;   // 与路径当前 x 的允许偏差（轨）
        private const float SlideBreakSeconds = 0.25f;         // 连续脱离多久算断开

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // 判定窗口走「基础值 + 修改器」：基础值登记给 PlayRules，局内 modifier 只改倍率，
            // 不直接改这里的序列化字段（docs/architecture.md §9）
            PlayRules.SetBaseWindows(judgeWindows);
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

            // 宽音符（横条）的判定已移到 HandlePress（TryJudgeWideOnPress）。
            // 放在按下事件里，"一次按下一个音符"才成立；留在 Update 轮询会与按下事件在同一帧各判一个。

            // Drag：按住该轨穿过判定窗即 Perfect（碰即 Perfect）。
            // 只认按下边沿时，「按住等 Drag 经过」会被 NoteSpawner 静默回收，0 分。
            for (int lane = 0; lane < pressStates.Length; lane++)
            {
                if (!pressStates[lane]) continue;
                // 本帧按下已经判掉过一个音符 → 轮询不再补刀（防"点一次判两个"）
                if (PressJudgedThisFrame) break;
                Note drag = NoteSpawner.Instance.GetClosestJudgableNote(lane, false, NoteType.Drag);
                if (drag == null) continue;
                float dragDiff = songTime - drag.Data.time;
                if (EffectiveWindows.Judge(dragDiff, JudgeNoteKind.Drag) == JudgeResultType.Miss) continue;
                ApplyJudge(drag, JudgeResultType.Perfect, lane);
            }

            // 宽长按 / Slide 释放：全部键松开即结束（释放时刻对结束时间判定）。
            // Slide 跟手（pjsk 语义）：按住期间手指要停在路径当前 x 附近；
            // 连续脱离超过 SlideBreakSeconds 才判断开（按 Miss），短暂偏离不惩罚。
            for (int i = activeSlides.Count - 1; i >= 0; i--)
            {
                Note slide = activeSlides[i];
                if (slide == null || !slide.IsHolding)
                {
                    if (slide != null) slideOutOfRange.Remove(slide);
                    activeSlides.RemoveAt(i);
                    continue;
                }
                float progress = Mathf.Clamp01((songTime - slide.Data.time) / Mathf.Max(.01f, slide.Data.duration));
                float expectedX = SampleSlidePathX(slide.Data, progress);
                bool following = false;
                for (int lane = 0; lane < pressStates.Length; lane++)
                {
                    if (pressStates[lane] && Mathf.Abs(lane - expectedX) <= SlideFollowToleranceLanes)
                    { following = true; break; }
                }
                if (following) { slideOutOfRange[slide] = 0f; continue; }
                float off = (slideOutOfRange.TryGetValue(slide, out float acc) ? acc : 0f) + Time.deltaTime;
                slideOutOfRange[slide] = off;
                if (off >= SlideBreakSeconds)
                {
                    Debug.Log($"[JudgeManager] Slide 跟手断开（偏离 {off:F2}s）lane={slide.Data.lane}");
                    ApplyJudge(slide, JudgeResultType.Miss, slide.Data.lane);
                    slideOutOfRange.Remove(slide);
                    activeSlides.RemoveAt(i);
                }
            }

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
                HandlePress(e.laneIndex, e.swipeY, e.swipeKnown, e.musicTime);
            else
                HandleRelease(e.laneIndex, e.musicTime);
        }

        /// <summary>
        /// 宽键（横条）：任意键**按下**可触发，但是"一次按下一个音符"中的那一个。
        /// 只在本次按下**没有**判到轨道音符时调用（空按 / 窗口外）。
        /// 返回是否判掉了宽键。
        /// </summary>
        private bool TryJudgeWideOnPress()
        {
            if (NoteSpawner.Instance == null || GameManager.Instance == null) return false;
            Note wide = NoteSpawner.Instance.GetClosestWideJudgableNote();
            if (wide == null) return false;

            float timeDiff = GameManager.Instance.SongTime - wide.Data.time;
            JudgeResultType result = EffectiveWindows.Judge(timeDiff, JudgeNoteKind.Wide);
            if (result == JudgeResultType.Miss) return false;   // 窗口外：交给 auto-miss，不误判

            if (wide.Data.type == NoteType.LongStart)
            {
                // 宽长按头：已有按住中的宽长按时不抢占（单槽位，防重叠宽长按互相覆盖）
                if (activeWideHold != null) return false;
                ApplyJudge(wide, result, wide.Data.lane);
                activeWideHold = wide;
            }
            else
            {
                ApplyJudge(wide, result, wide.Data.lane);   // Normal 宽音符（kick 单击）
            }
            MarkPressJudged();
            return true;
        }

        /// <summary>
        /// 伪双押：把与 judgedTime **同刻**（±ChordPressTolerance）的横条一并判掉。
        /// 只在本次按下已经判掉一个轨道音符之后调用——"一次按下一个"针对的是**不同时刻**的音符，
        /// 同刻的横条本来就该被这一次按下覆盖（kick 语义：任意键触发）。
        /// </summary>
        private int JudgeCoincidentWide(float judgedTime)
        {
            if (NoteSpawner.Instance == null || GameManager.Instance == null) return 0;
            int judged = 0;
            for (int guard = 0; guard < 4; guard++)
            {
                Note wide = NoteSpawner.Instance.GetClosestWideTo(judgedTime, ChordPressTolerance);
                if (wide == null) break;

                float timeDiff = GameManager.Instance.SongTime - wide.Data.time;
                JudgeResultType result = EffectiveWindows.Judge(timeDiff, JudgeNoteKind.Wide);
                if (result == JudgeResultType.Miss) break;   // 同刻但已过窗（极端情况）：不误判

                if (wide.Data.type == NoteType.LongStart)
                {
                    if (activeWideHold != null) break;
                    ApplyJudge(wide, result, wide.Data.lane);
                    activeWideHold = wide;
                }
                else
                {
                    ApplyJudge(wide, result, wide.Data.lane);
                }
                judged++;
            }
            return judged;
        }

        private void HandlePress(int lane, float swipeY = 0f, bool swipeKnown = false, float musicTime = -1f)
        {
            if (NoteSpawner.Instance == null)
            {
                Debug.LogWarning("[JudgeManager] NoteSpawner.Instance is null!");
                return;
            }
            // 判定时刻 = 输入事件自己的时刻（时间戳管线见 InputManager.LaneInputArgs.musicTime）：
            // 引擎晚读到的触摸要按它真实发生的时刻判；没有时间戳的调用者回退到本帧 SongTime。
            float songTime = musicTime >= 0f ? musicTime : GameManager.Instance.SongTime;

            Note note = NoteSpawner.Instance.GetClosestJudgableNote(lane, false);

            if (note == null)
            {
                // 没找到音符 — 空按
#if UNITY_EDITOR
                Debug.Log($"[JudgeManager] Press lane={lane} songTime={songTime:F2} -> No note found (air press)");
#endif
                TryJudgeWideOnPress();   // 该轨没有音符：这次按下可以给横条
                return;
            }

            float timeDiff = songTime - note.Data.time;
            JudgeResultType result = EffectiveWindows.Judge(timeDiff, JudgeWindowTable.KindOf(note.Data, IsWideNote(note)));

#if UNITY_EDITOR
            Debug.Log($"[JudgeManager] Press lane={lane} songTime={songTime:F2} noteTime={note.Data.time:F2} diff={timeDiff:F3} result={result}");
#endif

            if (result == JudgeResultType.Miss)
            {
                // 太远不判定（空按）；drag 音符保留等后续按（永不 MISS 语义）
                TryJudgeWideOnPress();   // 轨道音符在窗口外：这次按下可以给横条
                return;
            }

            // Drag：窗口内碰到即强制 Perfect（碰即 Perfect 语义）
            if (note.Data.type == NoteType.Drag)
                result = JudgeResultType.Perfect;

            // Flick：触屏要有方向正确的滑动才判（上下真方向判定）；
            // 幅度不足或方向错误 → 本次不判定，留给后续正确的滑动，超时由清扫按 Miss 处理。
            // 键盘/编辑器鼠标没有滑动向量（swipeKnown=false）→ 放宽为单击，保证 PC 可玩。
            if (note.Data.type == NoteType.Flick && swipeKnown)
            {
                float minSwipe = Mathf.Max(18f, Screen.height * .012f);
                bool up = swipeY > 0f;
                bool wantUp = note.Data.direction == FlickDirection.Up;
                if (Mathf.Abs(swipeY) < minSwipe || up != wantUp)
                {
#if UNITY_EDITOR
                    Debug.Log($"[JudgeManager] Flick 方向不符：swipe={swipeY:F1}px 需要{(wantUp ? "上" : "下")}滑");
#endif
                    return;
                }
            }

            // 命中
            ApplyJudge(note, result, lane);
            MarkPressJudged();
            JudgeCoincidentWide(note.Data.time);   // 伪双押：同刻的横条一起判掉

            // 长按：普通轨跟单轨松开；宽键/kick 跟 Slide 一样由任意键维持
            if (note.Data.type == NoteType.LongStart)
            {
                if (IsWideNote(note))
                {
                    if (activeWideHold == null)
                        activeWideHold = note;
                }
                else
                {
                    laneHoldStartTime[lane] = songTime;
                    laneHoldLongId[lane] = note.Data.longNoteId;
                }
            }

            // Slide 头部命中进入按住：登记到活动列表，由 Update 全松统一释放
            // （跨轨拖动不中断，见 Update 释放块）
            if (note.Data.type == NoteType.Slide && !activeSlides.Contains(note))
                activeSlides.Add(note);
        }

        private void HandleRelease(int lane, float musicTime = -1f)
        {
            // 释放时刻同样按事件时间戳算（长按尾判定 20ms 量级，差一帧就是档位差）
            float songTime = musicTime >= 0f ? musicTime : GameManager.Instance.SongTime;

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

            // 长按/Slide 头部仅触发视觉：不计分、不进判定计数（尾部才算一首）。
            // Good/Bad 头部仍断连击（Phigros 语义）。
            bool isHoldHead = note.Data.type == NoteType.LongStart || note.Data.type == NoteType.Slide;
            if (isHoldHead)
            {
                if (JudgeWindows.BreaksCombo(result))
                    BreakCombo();
            }
            else
            {
                if (JudgeWindows.BreaksCombo(result))
                    BreakCombo();
                else
                    AddScoreAndCombo(JudgeWindows.GetScore(result));
                AddJudgeCount(result);
            }
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

        private static bool IsWideNote(Note note)
        {
            if (note?.Data == null) return false;
            if (note.Data.wide) return true;
            var chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            return chart != null && chart.LaneCount == 4 && note.Data.lane == 0;
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
        /// <summary>按进度 t01 采样 slide 路径的轨道坐标 x（分段线性；异常时回退到轨道号）。</summary>
        private static float SampleSlidePathX(NoteData data, float t01)
        {
            var pts = data.path;
            if (pts == null || pts.Count == 0) return data.lane;
            if (pts.Count == 1) return pts[0].x;
            float total = Mathf.Max(.001f, pts[pts.Count - 1].t);
            float time = t01 * total;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float t0 = pts[i].t, t1 = pts[i + 1].t;
                if (time >= t0 && time <= t1)
                    return Mathf.Lerp(pts[i].x, pts[i + 1].x, t1 > t0 ? (time - t0) / (t1 - t0) : 0f);
            }
            return pts[pts.Count - 1].x;
        }

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
