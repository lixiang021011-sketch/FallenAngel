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

        private Vector3 comboOriginalScale;
        private Vector3 judgeOriginalScale;
        private Coroutine comboPunchCoroutine;
        private Coroutine judgeFadeCoroutine;

        private void Start()
        {
            if (comboText != null)
                comboOriginalScale = comboText.transform.localScale;
            if (judgeResultText != null)
                judgeOriginalScale = judgeResultText.transform.localScale;

            SubscribeEvents();
            ClearUI();
        }

        private void OnEnable()
        {
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void SubscribeEvents()
        {
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnScoreUpdate += HandleScoreUpdate;
                JudgeManager.Instance.OnComboUpdate += HandleComboUpdate;
                JudgeManager.Instance.OnJudgeResult += HandleJudgeResult;
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
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= HandleGameStart;
            }
        }

        private void HandleGameStart()
        {
            // 设置歌曲名
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
