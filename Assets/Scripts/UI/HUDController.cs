using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Gameplay;
using FallenAngel.Core;

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
                JudgeManager.Instance.OnJudgeResult += HandleJudgeResult;
                JudgeManager.Instance.OnJudgeBias += HandleJudgeBias;
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
                JudgeManager.Instance.OnJudgeResult -= HandleJudgeResult;
                JudgeManager.Instance.OnJudgeBias -= HandleJudgeBias;
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
                songTitleText.text = $"{meta.songName} - {meta.songArtist}";
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

        private void Update()
        {
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
            if (progressFillImage != null)
                progressFillImage.fillAmount = progress;

            if (progressText != null)
            {
                string cur = FormatTime(GameManager.Instance.SongTime);
                string tot = FormatTime(total);
                progressText.text = $"{cur} / {tot}";
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
