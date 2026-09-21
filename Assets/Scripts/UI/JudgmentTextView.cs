using FallenAngel.Core;
using FallenAngel.Gameplay;
using FallenAngel.Live;
using TMPro;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// 判定文字 + 判定描述（前端对照 OpenSekai：`Sekai/JudgmentView.cs` + `JudgmentDescriptionView.cs`，行为逐字照抄）。
    ///
    /// 上游行为（本类一一对应）：
    ///   · 判定文字：按结果换图 —— Miss/Bad/Good/Great/Perfect，**JustPerfect 复用 Perfect**；
    ///   · 判定描述：**只在 Bad/Good/Great 档显示**（`result &lt; Bad || result &gt; Great` 直接 return），
    ///     三种 = Fast / Late / FlickMiss（Perfect 档的 Fast/Late 描述不显示，Just 也不显示）；
    ///   · 动画：`t = clamp01(time*20)`、`scale = 1-(1-t)³`，`time > 0.3` 立即隐藏（出现 0.05s、驻留到 0.3s）。
    ///
    /// 与上游的差别只在"图 → 文本"：上游用美术字 sprite，本工程用 TMP 文本（字号/位置见
    /// art_tools/review/judgment_text/judgment_text_compare.png 的过稿稿）；动画与门控完全一致。
    /// 用 `Time.deltaTime`（上游同款）：暂停时 timeScale=0，文字停住而不是继续淡出。
    /// </summary>
    public class JudgmentTextView : MonoBehaviour
    {
        [Header("引用（SceneBuilder 注入）")]
        [SerializeField] private TextMeshProUGUI resultText;        // 判定词
        [SerializeField] private TextMeshProUGUI descriptionText;   // 早/晚/方向错 描述

        [Header("设置")]
        [Tooltip("对应上游 LiveSettingData.IsFastLateFlick：关掉就完全不显示描述")]
        [SerializeField] private bool showDescription = true;

        /// <summary>出现速度（上游 `time * 20f`，即 0.05s 到位）</summary>
        public const float AppearSpeed = 20f;

        /// <summary>隐藏时刻（上游 `time > 0.3f` → localScale=0）</summary>
        public const float HideAfter = 0.3f;

        private float resultTime = HideAfter + 1f;      // 初始 = 已隐藏
        private float descriptionTime = HideAfter + 1f;

        // ── 纯函数（自检用；与上游动画/映射一一对应）────────────────────────────

        /// <summary>上游动画曲线：t=clamp01(time*20)、scale=1-(1-t)³；time > 0.3 → 0</summary>
        public static float ScaleAt(float time)
        {
            if (time > HideAfter) return 0f;
            float t = Mathf.Clamp01(time * AppearSpeed);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        /// <summary>结果 → 文案 key（JustPerfect 复用 PERFECT；Auto 不显示）</summary>
        public static string WordKeyFor(NoteResult result)
        {
            switch (result)
            {
                case NoteResult.Miss: return "judge.miss";
                case NoteResult.Bad: return "judge.bad";
                case NoteResult.Good: return "judge.good";
                case NoteResult.Great: return "judge.great";
                case NoteResult.Perfect:
                case NoteResult.JustPerfect: return "judge.perfect";
                default: return null;
            }
        }

        /// <summary>描述 → 文案 key；门控照抄上游：只有 Bad/Good/Great 档才显示</summary>
        public static string DescriptionKeyFor(NoteResult result, NoteResultDescription description)
        {
            if (result < NoteResult.Bad || result > NoteResult.Great) return null;
            switch (description)
            {
                case NoteResultDescription.Fast: return "judge.desc.fast";
                case NoteResultDescription.Late: return "judge.desc.late";
                case NoteResultDescription.FlickMiss: return "judge.desc.flickMiss";
                default: return null;
            }
        }

        // ── 生命周期与接线 ─────────────────────────────────────────────────

        private void Awake()
        {
            HideAll();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (JudgeManager.Instance != null)
                JudgeManager.Instance.OnJudgeJudged -= Excute;
        }

        private void Subscribe()
        {
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnJudgeJudged -= Excute;
                JudgeManager.Instance.OnJudgeJudged += Excute;
            }
        }

        /// <summary>上游 Excute((result, description))：换文案、time=0、scale=0</summary>
        public void Excute(NoteResult result, NoteResultDescription description)
        {
            if (resultText != null)
            {
                string key = WordKeyFor(result);
                if (!string.IsNullOrEmpty(key))
                {
                    resultText.text = Loc.T(key);
                    resultTime = 0f;
                    resultText.transform.localScale = Vector3.zero;
                }
            }

            if (!showDescription || descriptionText == null) return;
            string descKey = DescriptionKeyFor(result, description);
            if (string.IsNullOrEmpty(descKey)) return;      // 例如 Perfect 的 Fast/Late：不显示
            descriptionText.text = Loc.T(descKey);
            descriptionTime = 0f;
            descriptionText.transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            Animate(resultText, ref resultTime);
            Animate(descriptionText, ref descriptionTime);
        }

        private static void Animate(TextMeshProUGUI text, ref float time)
        {
            if (text == null) return;
            float scale = ScaleAt(time);
            text.transform.localScale = Vector3.one * scale;
            if (time <= HideAfter) time += Time.deltaTime;
        }

        private void HideAll()
        {
            resultTime = HideAfter + 1f;
            descriptionTime = HideAfter + 1f;
            if (resultText != null) resultText.transform.localScale = Vector3.zero;
            if (descriptionText != null) descriptionText.transform.localScale = Vector3.zero;
        }
    }
}
