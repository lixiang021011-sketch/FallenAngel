using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Gameplay;
using FallenAngel.Core;
using FallenAngel.Data;   // Difficulty 枚举（谱面元数据）

namespace FallenAngel.UI
{
    /// <summary>
    /// 游戏内HUD控制器
    /// 负责显示分数、连击、判定文字、进度条等
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("文本引用")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI comboText;
        [SerializeField] private TextMeshProUGUI comboLabelText;
        [Tooltip("Combo 数字位 + AP 特效（B3；常量照抄上游 ComboView）")]
        [SerializeField] private ComboView comboView;
        [SerializeField] private TextMeshProUGUI judgeResultText;  // 判定结果（PERFECT/GOOD...）
        [SerializeField] private TextMeshProUGUI judgeBiasText;    // 早/晚指示（Phigros 手感参考）
        [SerializeField] private TextMeshProUGUI songTitleText;    // 歌曲名
        [SerializeField] private TextMeshProUGUI progressText;     // 进度文本 01:23 / 03:45

        [Header("进度条")]
        [SerializeField] private Image progressFillImage;

        [Header("动画设置")]
        [SerializeField] private float comboPunchScale = 1.3f;
        [SerializeField] private float comboPunchDuration = 0.15f;
        [SerializeField] private float judgeFadeDuration = 0.5f;

        [Header("判定颜色")]
        [SerializeField] private Color perfectColor = Color.yellow;
        [SerializeField] private Color greatColor = Color.green;
        [SerializeField] private Color goodColor = Color.cyan;
        [SerializeField] private Color badColor = new Color(1f, 0.5f, 0f);
        [SerializeField] private Color missColor = Color.red;
        [SerializeField] private Color comboFullColor = Color.yellow;
        [SerializeField] private Color comboBrokenColor = Color.white;

        [Header("早/晚指示（Phigros 同款配色）")]
        [SerializeField] private Color earlyColor = new Color(0.012f, 0.667f, 0.976f); // #03aaf9
        [SerializeField] private Color lateColor = new Color(1f, 0.275f, 0.071f);       // #ff4612
        [Tooltip("偏差绝对值小于该值视为 Perfect(Max)，不显示早/晚指示（秒）")]
        [SerializeField] private float biasShowThreshold = 0.04f;

        private Vector3 comboOriginalScale;
        private Vector3 judgeOriginalScale;
        private Coroutine comboPunchCoroutine;
        private Coroutine judgeFadeCoroutine;
        private Coroutine biasFadeCoroutine;

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void Start()
        {
            if (comboText != null)
                comboOriginalScale = comboText.transform.localScale;
            if (judgeResultText != null)
                judgeOriginalScale = judgeResultText.transform.localScale;

            // 先退订再订阅：OnEnable 已订过一次，防 Awake 顺序导致漏订/重订（架构约定 §3）
            SubscribeEvents();
            ClearUI();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void SubscribeEvents()
        {
            UnsubscribeEvents();
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnScoreUpdate += HandleScoreUpdate;
                JudgeManager.Instance.OnComboUpdate += HandleComboUpdate;
                // 判定文字 / 早·晚描述已交给 JudgmentTextView（对照上游 JudgmentView / JudgmentDescriptionView，
                // 动画与显示门控都照抄）——这里不再订阅，避免两套逻辑抢同一个文本对象。
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart += HandleGameStart;
            }
        }

        private void UnsubscribeEvents()
        {
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnScoreUpdate -= HandleScoreUpdate;
                JudgeManager.Instance.OnComboUpdate -= HandleComboUpdate;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= HandleGameStart;
            }
        }

        private void HandleGameStart()
        {
            ClearUI();
            if (GameManager.Instance?.CurrentChart?.metadata != null && songTitleText != null)
            {
                var meta = GameManager.Instance.CurrentChart.metadata;
                // E4 曲信息区：曲名 - 曲师 · 难度 Lv.等级（上游 MusicInfoView 会显示曲信息/难度/进度；
                // 难度与等级本来就在谱面元数据里，之前只在结算页用，演奏页没显示）
                songTitleText.text = string.Format("{0} - {1} · {2} Lv.{3}",
                    meta.songName, meta.songArtist,
                    DifficultyLabel(meta.difficulty), meta.level);
            }
        }

        /// <summary>难度档位文案（对应谱面元数据的 Difficulty 枚举）</summary>
        private static string DifficultyLabel(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy: return Loc.T("difficulty.easy");
                case Difficulty.Normal: return Loc.T("difficulty.normal");
                case Difficulty.Hard: return Loc.T("difficulty.hard");
                case Difficulty.Expert: return Loc.T("difficulty.expert");
                default: return Loc.T("difficulty.normal");
            }
        }

        private void ClearUI()
        {
            if (scoreText != null) scoreText.text = "0";
            if (comboText != null) comboText.text = "0";
            if (comboLabelText != null) comboLabelText.color = comboBrokenColor;
            if (judgeResultText != null) judgeResultText.text = "";
            if (judgeBiasText != null) judgeBiasText.text = "";
            if (progressFillImage != null) progressFillImage.fillAmount = 0f;
            if (progressText != null) progressText.text = "00:00 / 00:00";
        }

        // 进度条/进度文本节流用
        private float lastProgressUpdateTime = -1f;
        private string lastProgressString;

        private void Update()
        {
            // **每帧刷新进度会强制整块 Canvas 每帧重建**：Image.fillAmount 与 TMP 文本都会标脏。
            // "分钟:秒"级进度 4Hz 足够，省下的是每帧一次网格重建（真机按键→画面延迟与丢帧的直接来源）。
            if (Time.unscaledTime - lastProgressUpdateTime < 0.25f) return;
            lastProgressUpdateTime = Time.unscaledTime;
            UpdateProgress();
        }

        /// <summary>
        /// 每帧更新进度条
        /// </summary>
        private void UpdateProgress()
        {
            if (GameManager.Instance == null || GameManager.Instance.CurrentChart == null)
                return;
            if (GameManager.Instance.CurrentState != GameState.Playing)
                return;

            float total = GameManager.Instance.CurrentChart.GetTotalDuration();
            if (total <= 0f) return;

            float progress = Mathf.Clamp01(GameManager.Instance.SongTime / total);
            if (progressFillImage != null && !Mathf.Approximately(progressFillImage.fillAmount, progress))
                progressFillImage.fillAmount = progress;

            if (progressText != null)
            {
                string cur = FormatTime(GameManager.Instance.SongTime);
                string tot = FormatTime(total);
                string next = $"{cur} / {tot}";
                if (next != lastProgressString)   // 文本没变就别 set（TMP 会重建网格）
                {
                    lastProgressString = next;
                    progressText.text = next;
                }
            }
        }

        private string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m:00}:{s:00}";
        }

        private void HandleScoreUpdate(int score)
        {
            if (scoreText != null)
                scoreText.text = score.ToString("N0");
        }

        private void HandleComboUpdate(int combo, bool isFullCombo)
        {
            // B3：数字位布局/出现/爆发/AP 呼吸全部交给 ComboView（旧的整块文本已停用）
            if (comboView != null)
            {
                JudgeManager judge = JudgeManager.Instance;
                bool allPerfect = judge != null && ComboView.IsAllPerfectScore(
                    judge.PerfectCount, judge.GreatCount, judge.GoodCount, judge.BadCount, judge.MissCount);
                comboView.SetCombo(combo, allPerfect);
            }

            if (comboText != null)
            {
                comboText.text = combo.ToString();

                // 连击颜色
                if (comboLabelText != null)
                    comboLabelText.color = isFullCombo ? comboFullColor : comboBrokenColor;

                // 连击动画（仅当combo>0且变化时）
                if (combo > 0)
                {
                    if (comboPunchCoroutine != null) StopCoroutine(comboPunchCoroutine);
                    comboPunchCoroutine = StartCoroutine(ComboPunch());
                }
            }
        }

        private System.Collections.IEnumerator ComboPunch()
        {
            float timer = 0f;
            Transform t = comboText.transform;
            while (timer < comboPunchDuration)
            {
                timer += Time.unscaledDeltaTime;
                float tt = timer / comboPunchDuration;
                // easeOutBack
                float s = 1f + (comboPunchScale - 1f) * EaseOutBack(tt);
                t.localScale = comboOriginalScale * s;
                yield return null;
            }
            t.localScale = comboOriginalScale;
        }

        private void HandleJudgeResult(JudgeResultType result, int lane)
        {
            if (judgeResultText == null) return;
            if (result == JudgeResultType.None) return;

            string name = JudgeWindows.GetChineseName(result);
            judgeResultText.text = name;
            judgeResultText.color = GetJudgeColor(result);

            if (judgeFadeCoroutine != null) StopCoroutine(judgeFadeCoroutine);
            judgeFadeCoroutine = StartCoroutine(JudgeFade());
        }

        /// <summary>
        /// 早/晚指示（Phigros 手感参考）：
        /// |偏差| ≤ 阈值 → Perfect(Max) 不显示；正=按早了（蓝"早"），负=按晚了（橙"晚"）
        /// </summary>
        private void HandleJudgeBias(float bias)
        {
            if (judgeBiasText == null) return;

            if (Mathf.Abs(bias) <= biasShowThreshold)
            {
                return; // Perfect(Max) 区间，保留上一次的指示直到自然淡出
            }

            judgeBiasText.text = bias > 0f ? "早" : "晚";
            judgeBiasText.color = bias > 0f ? earlyColor : lateColor;

            if (biasFadeCoroutine != null) StopCoroutine(biasFadeCoroutine);
            biasFadeCoroutine = StartCoroutine(BiasFade());
        }

        private System.Collections.IEnumerator BiasFade()
        {
            float timer = 0f;
            Color orig = judgeBiasText.color;
            while (timer < judgeFadeDuration)
            {
                timer += Time.unscaledDeltaTime;
                Color c = orig;
                c.a = 1f - timer / judgeFadeDuration;
                judgeBiasText.color = c;
                yield return null;
            }
            judgeBiasText.text = "";
        }

        private System.Collections.IEnumerator JudgeFade()
        {
            float timer = 0f;
            Color orig = judgeResultText.color;
            // 始终使用记录的原始缩放，防止中断时残留
            Transform t = judgeResultText.transform;
            t.localScale = judgeOriginalScale;
            Vector3 origScale = judgeOriginalScale;
            Vector3 targetScale = origScale * 1.2f;

            while (timer < 0.1f)
            {
                timer += Time.unscaledDeltaTime;
                t.localScale = Vector3.Lerp(origScale, targetScale, timer / 0.1f);
                yield return null;
            }

            timer = 0f;
            while (timer < judgeFadeDuration)
            {
                timer += Time.unscaledDeltaTime;
                float tt = timer / judgeFadeDuration;
                Color c = orig;
                c.a = 1f - tt;
                judgeResultText.color = c;
                t.localScale = Vector3.Lerp(targetScale, origScale, tt);
                yield return null;
            }
            judgeResultText.text = "";
            t.localScale = origScale;
        }

        private Color GetJudgeColor(JudgeResultType r)
        {
            return r switch
            {
                JudgeResultType.Perfect => perfectColor,
                JudgeResultType.Great => greatColor,
                JudgeResultType.Good => goodColor,
                JudgeResultType.Bad => badColor,
                JudgeResultType.Miss => missColor,
                _ => Color.white
            };
        }

        private static float EaseOutBack(float x)
        {
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
