using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// 结算页的"分数滚动 + 逐项入场"（前端清单 E2 的演出部分）。
    ///
    /// 上游按 `LiveResultAnimationType` 有六档演出（None/Failure/LifeZero/Clear/FullCombo/AllPerfect）；
    /// 演出本身按作者定案**不抄特效**，这里用程序化的分数滚动 + 逐项淡入等价：
    ///   · 分数：0 → 最终分（`ResultPresentation.RollScore`，三次缓出）
    ///   · 各项：按 `RevealStagger` 依次淡入（`RevealAlpha`）
    /// 分级本身上游有明确枚举，我们照用（`Core/ResultPresentation.ResultAnimationType`）。
    ///
    /// 用法：`ResultScreen` 在填完最终值后调 `Play(finalScore, 逐项元素)`，本组件负责把它演出来。
    /// </summary>
    public sealed class ResultReveal : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private List<CanvasGroup> revealGroups = new List<CanvasGroup>();
        [Tooltip("分数滚动的音效间隔（秒）；<=0 表示不播")]
        [SerializeField] private float rollTickInterval = 0.06f;

        private float timer;
        private bool playing;
        private int finalScore;
        private float lastTick;

        public bool IsPlaying { get { return playing; } }

        /// <summary>开始演出：把分数从 0 滚到 finalScore，并让 revealGroups 依次亮起。</summary>
        public void Play(int targetScore)
        {
            finalScore = targetScore;
            timer = 0f;
            lastTick = 0f;
            playing = true;

            if (scoreText != null) scoreText.text = "0";
            SetAllAlpha(0f);
        }

        /// <summary>跳过演出（例如玩家点了重试）：直接落到最终状态。</summary>
        public void FinishImmediately()
        {
            playing = false;
            if (scoreText != null) scoreText.text = finalScore.ToString("N0");
            SetAllAlpha(1f);
        }

        private void Update()
        {
            if (!playing) return;
            timer += Time.unscaledDeltaTime;

            if (scoreText != null)
                scoreText.text = Core.ResultPresentation.RollScore(finalScore, timer).ToString("N0");

            for (int i = 0; i < revealGroups.Count; i++)
            {
                if (revealGroups[i] == null) continue;
                revealGroups[i].alpha = Core.ResultPresentation.RevealAlpha(i, timer);
            }

            if (rollTickInterval > 0f && timer - lastTick >= rollTickInterval && timer < Core.ResultPresentation.ScoreRollSeconds)
            {
                lastTick = timer;
                Audio.AudioManager.Instance?.PlayButtonClick();
            }

            if (timer >= Core.ResultPresentation.TotalSeconds(revealGroups.Count))
                FinishImmediately();
        }

        private void SetAllAlpha(float a)
        {
            for (int i = 0; i < revealGroups.Count; i++)
                if (revealGroups[i] != null) revealGroups[i].alpha = a;
        }
    }
}
