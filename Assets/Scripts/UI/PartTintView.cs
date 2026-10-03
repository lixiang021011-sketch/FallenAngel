using FallenAngel.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 声部视觉层（占位实现，未来换商用素材）：
    ///   ① G2 半场染色：一层按声部色相染色的渐变，盖在 GameplayBackground 之上、音符之下；
    ///   ② 切换演出：判定线闪白 + 左右轨道光带 + 中央声部徽记（0.6s 淡出）；
    ///   ③ 换色过程 0.35s 交叉淡化（S1）。
    ///
    /// 为什么在运行时建层、不写进场景：铁律 2 规定场景/预制体由作者在编辑器里操作，
    /// 而这个层是纯程序化占位（DeepSeaTheme.Backdrop 也是这么做的），
    /// 不建层就不需要重建场景。
    ///
    /// 时钟一律用 <see cref="GameManager.SongTime"/>：暂停时染色与徽记自然冻结，
    /// 不会出现"暂停了特效还在跑"。
    /// </summary>
    public sealed class PartTintView : MonoBehaviour
    {
        private const string LanesName = "LanesBG";
        private const string HudName = "HUDPanel";

        private Image tint;
        private Image judgeFlash;
        private Image railLeft;
        private Image railRight;
        private TextMeshProUGUI badge;

        private Color currentColor = Color.clear;
        private Color previousColor = Color.clear;
        private bool hasColor;
        private float fadeStartSongTime = -999f;
        private float fxStartSongTime = -999f;
        private float flashBaseHeight = 8f;

        private void Start()
        {
            Build();
            Subscribe();
            ApplyImmediate();
        }

        private void OnEnable() { Subscribe(); }

        private void OnDisable() { Unsubscribe(); }

        private GameManager Gm { get { return GameManager.Instance; } }

        private void Subscribe()
        {
            GameManager gm = Gm;
            if (gm == null) return;
            gm.OnPartChanged -= HandlePartChanged;
            gm.OnPartChanged += HandlePartChanged;
            gm.OnGameStart -= HandleGameStart;
            gm.OnGameStart += HandleGameStart;
            gm.OnGameEnd -= HandleGameEnd;
            gm.OnGameEnd += HandleGameEnd;
        }

        private void Unsubscribe()
        {
            GameManager gm = Gm;
            if (gm == null) return;
            gm.OnPartChanged -= HandlePartChanged;
            gm.OnGameStart -= HandleGameStart;
            gm.OnGameEnd -= HandleGameEnd;
        }

        private void HandleGameStart()
        {
            ApplyImmediate();
            fxStartSongTime = -999f;
            UpdateFx(0f, false);
        }

        private void HandleGameEnd()
        {
            fxStartSongTime = -999f;
            UpdateFx(0f, false);
        }

        private void HandlePartChanged()
        {
            GameManager gm = Gm;
            string part = gm != null && gm.Parts != null ? gm.Parts.PartName : null;
            Color color;
            if (!PartPalette.TryGetColor(part, out color)) return;

            previousColor = hasColor ? currentColor : color;
            currentColor = color;
            hasColor = true;
            fadeStartSongTime = gm != null ? gm.SongTime : 0f;
            fxStartSongTime = fadeStartSongTime;
            SetBadgeText(part.ToUpperInvariant());
            Debug.Log(string.Format("[PartTintView] 声部视觉切到 {0}（色相 {1:0}°），换色 {2:0.00}s",
                part, PartPalette.HueOf(part), PartPalette.TintFadeSeconds));
        }

        /// <summary>开局/重试：直接到位，不播换色动画</summary>
        private void ApplyImmediate()
        {
            GameManager gm = Gm;
            string part = gm != null && gm.Parts != null ? gm.Parts.PartName : null;
            Color color;
            if (!PartPalette.TryGetColor(part, out color))
            {
                hasColor = false;
                if (tint != null) tint.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            currentColor = color;
            previousColor = color;
            hasColor = true;
            fadeStartSongTime = -999f;
            SetBadgeText(string.Empty);
            ApplyTintColor(color, 1f);
        }

        private void Update()
        {
            GameManager gm = Gm;
            if (gm == null || tint == null) return;
            float songTime = gm.SongTime;

            float k = fadeStartSongTime < 0f
                ? 1f
                : Mathf.Clamp01((songTime - fadeStartSongTime) / PartPalette.TintFadeSeconds);
            ApplyTintColor(Color.Lerp(previousColor, currentColor, k), hasColor ? 1f : 0f);

            UpdateFx(songTime, true);
        }

        private void ApplyTintColor(Color color, float alpha)
        {
            if (tint == null) return;
            tint.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>徽记 / 判定线闪白 / 轨道光带：全部以切换时刻为起点按 SongTime 推进。</summary>
        private void UpdateFx(float songTime, bool running)
        {
            float elapsed = fxStartSongTime < 0f ? 999f : songTime - fxStartSongTime;
            float badgeAlpha = running ? Mathf.Clamp01(1f - elapsed / PartPalette.BadgeSeconds) : 0f;
            float railAlpha = badgeAlpha * 0.45f;
            float flash = running ? Mathf.Clamp01(1f - elapsed / PartPalette.JudgeFlashSeconds) : 0f;

            if (badge != null)
            {
                Color c = currentColor;
                badge.color = new Color(c.r, c.g, c.b, badgeAlpha * 0.9f);
                float scale = 1f + 0.08f * Mathf.Clamp01(elapsed / PartPalette.BadgeSeconds);
                badge.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            if (judgeFlash != null)
            {
                judgeFlash.color = new Color(1f, 1f, 1f, flash * 0.55f);
                judgeFlash.rectTransform.sizeDelta = new Vector2(judgeFlash.rectTransform.sizeDelta.x,
                    flashBaseHeight * (1f + 4f * flash));
            }
            SetAlpha(railLeft, railAlpha);
            SetAlpha(railRight, railAlpha);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            image.color = new Color(c.r, c.g, c.b, alpha);
        }

        // ── 建层 ────────────────────────────────────────────────────────
        private void Build()
        {
            Transform lanes = transform.Find(LanesName);
            Transform hud = transform.Find(HudName);
            if (lanes == null) return;

            // ① 染色层：LanesBG 的第一个子节点 = 盖在 50% 黑底之上、轨道与音符之下
            GameObject tintGO = new GameObject("PartTint", typeof(RectTransform), typeof(Image));
            RectTransform tintRect = (RectTransform)tintGO.transform;
            tintRect.SetParent(lanes, false);
            tintRect.anchorMin = Vector2.zero;
            tintRect.anchorMax = Vector2.one;
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            tintRect.SetSiblingIndex(0);
            tint = tintGO.GetComponent<Image>();
            tint.sprite = BuildTintSprite();
            tint.type = Image.Type.Simple;
            tint.raycastTarget = false;   // 绝不吃触摸
            tint.color = new Color(1f, 1f, 1f, 0f);

            // ② 演出层：在 HUDPanel 之前 = 盖在音符之上、HUD 之下
            GameObject fxGO = new GameObject("PartSwitchFX", typeof(RectTransform));
            RectTransform fxRect = (RectTransform)fxGO.transform;
            fxRect.SetParent(transform, false);
            fxRect.anchorMin = Vector2.zero;
            fxRect.anchorMax = Vector2.one;
            fxRect.offsetMin = Vector2.zero;
            fxRect.offsetMax = Vector2.zero;
            fxRect.SetSiblingIndex(hud != null ? hud.GetSiblingIndex() : transform.childCount - 1);

            railLeft = CreateRail(fxRect, "RailLeft");
            railRight = CreateRail(fxRect, "RailRight");
            judgeFlash = CreateJudgeFlash(fxRect, lanes);
            badge = CreateBadge(fxRect, hud);
            PositionRails();
            UpdateFx(0f, false);
        }

        private Image CreateRail(RectTransform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(26f, 1560f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);
            return image;
        }

        /// <summary>轨道光带贴着当前键数的轨组外侧（4K/5K 都要正确）</summary>
        private void PositionRails()
        {
            float halfWidth = LaneLayout.GetUsableWidth(LaneLayout.ActiveLaneCount) * 0.5f;
            if (railLeft != null) railLeft.rectTransform.anchoredPosition = new Vector2(-(halfWidth + 20f), 40f);
            if (railRight != null) railRight.rectTransform.anchoredPosition = new Vector2(halfWidth + 20f, 40f);
        }

        /// <summary>判定线闪白：几何直接抄场景里的 JudgeLine，避免又一处"视觉与判定不同源"</summary>
        private Image CreateJudgeFlash(RectTransform parent, Transform lanes)
        {
            Transform existing = lanes.Find("JudgeLine");
            GameObject go = new GameObject("JudgeFlash", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            if (existing != null && existing is RectTransform src)
            {
                rect.anchorMin = src.anchorMin;
                rect.anchorMax = src.anchorMax;
                rect.pivot = src.pivot;
                rect.anchoredPosition = src.anchoredPosition;
                rect.sizeDelta = src.sizeDelta;
                flashBaseHeight = src.sizeDelta.y;
            }
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);
            return image;
        }

        /// <summary>中央声部徽记：放在判定线以上，不压判定区</summary>
        private TextMeshProUGUI CreateBadge(RectTransform parent, Transform hud)
        {
            TMP_FontAsset font = null;
            if (hud != null)
            {
                TextMeshProUGUI sample = hud.GetComponentInChildren<TextMeshProUGUI>(true);
                if (sample != null) font = sample.font;
            }

            GameObject go = new GameObject("PartBadge", typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 300f);
            rect.sizeDelta = new Vector2(900f, 140f);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = string.Empty;
            text.fontSize = 96f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.color = new Color(1f, 1f, 1f, 0f);
            return text;
        }

        /// <summary>
        /// G2 渐变贴图：白色 + alpha 剖面（上半 55% 渐弱、底部一道反射），
        /// 色相由 Image.color 乘上去 —— 这样"强度"是贴图、'色相'是数据，换素材时只换贴图。
        /// </summary>
        private static Sprite BuildTintSprite()
        {
            const int height = 256;
            Texture2D texture = new Texture2D(1, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < height; y++)
            {
                float fromTop = 1f - y / (float)(height - 1);   // 0 = 屏幕顶，1 = 屏幕底
                float alpha = 0f;
                if (fromTop < PartPalette.TintSpan)
                    alpha += PartPalette.TintPeak *
                             Mathf.Pow(1f - fromTop / PartPalette.TintSpan, 1.2f);
                const float reflectionStart = 0.78f;
                if (fromTop > reflectionStart)
                    alpha += PartPalette.BottomReflection *
                             (fromTop - reflectionStart) / (1f - reflectionStart);
                texture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>徽记文字（原型用英文；正式内容走本地化与商用素材）</summary>
        public void SetBadgeText(string text)
        {
            if (badge != null) badge.text = text ?? string.Empty;
        }
    }
}
