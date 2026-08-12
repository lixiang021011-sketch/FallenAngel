using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Data;
using FallenAngel.Core;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 单个音符的视图组件
    /// 负责音符的下落移动、视觉效果、判定状态显示
    /// </summary>
    public class Note : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Image noteImage;          // 音符图像
        [SerializeField] private Image longNoteBodyImage;  // 长按音符身体
        [SerializeField] private RectTransform bodyRect;   // 长按身体RectTransform

        [Header("颜色（4个音轨不同颜色）")]
        [SerializeField] private Color[] laneColors = new Color[]
        {
            new Color(0.2f, 0.6f, 1f),   // 蓝 - Lane 0 (D)
            new Color(0.2f, 1f, 0.4f),   // 绿 - Lane 1 (F)
            new Color(1f, 0.85f, 0.2f),  // 黄 - Lane 2 (J)
            new Color(1f, 0.3f, 0.3f)    // 红 - Lane 3 (K)
        };

        /// <summary>音符数据</summary>
        public NoteData Data { get; private set; }

        /// <summary>是否已被判定（非Miss）</summary>
        public bool IsJudged { get; private set; }

        /// <summary>判定结果</summary>
        public JudgeResultType JudgeResult { get; private set; } = JudgeResultType.None;

        /// <summary>是否为长按音符且正在按住中</summary>
        public bool IsHolding { get; private set; }

        /// <summary>长按音符的进度 (0~1)</summary>
        public float HoldProgress { get; private set; }

        private RectTransform rectTransform;
        private bool isLongNoteConfigured;
        private Vector3 originalScale;
        private Color originalNoteColor;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null) noteImage = GetComponent<Image>();
            originalScale = transform.localScale;
            if (noteImage != null) originalNoteColor = noteImage.color;
        }

        /// <summary>
        /// 初始化音符
        /// </summary>
        public void Initialize(NoteData data, Vector2 spawnPos, Vector2 judgeLinePos)
        {
            Data = data;
            IsJudged = false;
            JudgeResult = JudgeResultType.None;
            IsHolding = false;
            HoldProgress = 0f;
            isLongNoteConfigured = false;

            // 重置缩放和颜色（从对象池复用时必须重置）
            transform.localScale = originalScale;

            // 设置位置
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
            rectTransform.anchoredPosition = spawnPos;
            gameObject.SetActive(true);

            // 设置颜色
            Color c = laneColors[Mathf.Clamp(data.lane, 0, 3)];
            if (noteImage != null)
            {
                noteImage.color = c;
                // 确保 Image 可见
                noteImage.enabled = true;
            }
            if (longNoteBodyImage != null)
            {
                Color bodyColor = c;
                bodyColor.a = 0.5f;
                longNoteBodyImage.color = bodyColor;
            }

            // 配置长按音符身体
            ConfigureLongNoteBody(spawnPos, judgeLinePos);
        }

        /// <summary>
        /// 配置长按音符的身体渲染
        /// </summary>
        private void ConfigureLongNoteBody(Vector2 spawnPos, Vector2 judgeLinePos)
        {
            if (longNoteBodyImage == null || bodyRect == null) return;

            if (Data.type == NoteType.LongStart && Data.duration > 0f)
            {
                longNoteBodyImage.gameObject.SetActive(true);
                isLongNoteConfigured = true;
                // 音符身体从生成位置延伸到判定线位置（向下）
                float height = Mathf.Abs(spawnPos.y - judgeLinePos.y);
                // 持续时间越长，身体越长（基于下落速度）
                float fallTime = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
                float durationMultiplier = Data.duration / fallTime;
                float extraHeight = height * durationMultiplier;
                bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, height + extraHeight);
                bodyRect.anchoredPosition = new Vector2(0, (height + extraHeight) * 0.5f);
            }
            else
            {
                longNoteBodyImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 每帧更新音符位置（基于时间）
        /// </summary>
        /// <param name="currentSongTime">当前歌曲时间（秒）</param>
        /// <param name="spawnPosY">生成位置Y坐标</param>
        /// <param name="judgePosY">判定线位置Y坐标</param>
        public void UpdatePosition(float currentSongTime, float spawnPosY, float judgePosY)
        {
            if (Data == null || GameManager.Instance == null) return;

            float fallTime = GameManager.Instance.ActualFallTime;
            // 进度 0 = 刚生成， 1 = 到达判定线
            float timeToJudge = Data.time - currentSongTime;
            float progress = 1f - Mathf.Clamp01(timeToJudge / fallTime);

            if (Data.type == NoteType.LongEnd)
            {
                // LongEnd 依附于长按进度
                if (IsHolding)
                {
                    float noteProgress = Mathf.Clamp01(progress);
                    float y = Mathf.Lerp(spawnPosY, judgePosY, noteProgress);
                    rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
                }
            }
            else
            {
                // 普通音符和长按头部正常下落
                float y = Mathf.Lerp(spawnPosY, judgePosY, progress);
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, y);
            }

            // 更新长按身体长度（按住时）
            if (Data.type == NoteType.LongStart && IsHolding && isLongNoteConfigured && bodyRect != null)
            {
                HoldProgress = Mathf.Clamp01((currentSongTime - Data.time) / Mathf.Max(0.01f, Data.duration));
                float currentHeight = bodyRect.sizeDelta.y * (1f - HoldProgress);
                bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, Mathf.Max(0f, currentHeight));
                bodyRect.anchoredPosition = new Vector2(0, bodyRect.sizeDelta.y * 0.5f);
            }
        }

        /// <summary>
        /// 被判定命中
        /// </summary>
        public void JudgeHit(JudgeResultType result)
        {
            if (IsJudged) return;
            IsJudged = true;
            JudgeResult = result;

            if (Data.type == NoteType.LongStart)
            {
                // 长按头部命中 -> 进入按住状态
                IsHolding = true;
                // 视觉效果：稍微亮一点
                if (noteImage != null)
                {
                    Color c = noteImage.color;
                    c.a = 0.6f;
                    noteImage.color = c;
                }
            }
            else
            {
                // 普通音符和长按尾命中 -> 播放消失动画
                PlayHitEffect(result);
            }
        }

        /// <summary>
        /// 长按释放判定
        /// </summary>
        public JudgeResultType JudgeLongRelease(float releaseTimeDiff)
        {
            if (Data.type != NoteType.LongStart) return JudgeResultType.None;
            IsHolding = false;

            JudgeWindows windows = new JudgeWindows();
            JudgeResultType result = windows.Judge(releaseTimeDiff);
            // 长按至少按到Good才算成功
            if (result == JudgeResultType.Bad) result = JudgeResultType.Good;
            JudgeResult = result;
            IsJudged = true;
            PlayHitEffect(result);
            return result;
        }

        /// <summary>
        /// 标记为Miss
        /// </summary>
        public void JudgeMiss()
        {
            if (IsJudged && Data.type != NoteType.LongStart) return;
            IsJudged = true;
            IsHolding = false;
            JudgeResult = JudgeResultType.Miss;
            PlayMissEffect();
        }

        /// <summary>
        /// 播放命中特效
        /// </summary>
        private void PlayHitEffect(JudgeResultType result)
        {
            StopAllCoroutines();
            StartCoroutine(HitEffectCoroutine(result));
        }

        private System.Collections.IEnumerator HitEffectCoroutine(JudgeResultType result)
        {
            float duration = 0.2f;
            float timer = 0f;
            Vector3 startScale = transform.localScale;
            Vector3 targetScale = startScale * 1.5f;

            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float t = timer / duration;
                transform.localScale = Vector3.Lerp(startScale, targetScale, t);
                if (noteImage != null)
                {
                    Color c = noteImage.color;
                    c.a = 1f - t;
                    noteImage.color = c;
                }
                yield return null;
            }
            gameObject.SetActive(false);
        }

        private void PlayMissEffect()
        {
            StopAllCoroutines();
            StartCoroutine(MissEffectCoroutine());
        }

        private System.Collections.IEnumerator MissEffectCoroutine()
        {
            float duration = 0.3f;
            float timer = 0f;
            if (noteImage != null)
            {
                Color orig = noteImage.color;
                while (timer < duration)
                {
                    timer += Time.unscaledDeltaTime;
                    float t = timer / duration;
                    Color c = orig;
                    c.r = Mathf.Lerp(orig.r, 0.3f, t);
                    c.g = Mathf.Lerp(orig.g, 0.3f, t);
                    c.b = Mathf.Lerp(orig.b, 0.3f, t);
                    c.a = orig.a * (1f - t);
                    noteImage.color = c;
                    yield return null;
                }
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 强制回收（超出屏幕后）
        /// </summary>
        public void Recycle()
        {
            StopAllCoroutines();
            // 重置缩放和颜色，防止从对象池复用时残留
            transform.localScale = originalScale;
            gameObject.SetActive(false);
        }
    }
}
