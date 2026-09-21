using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Audio;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// **下落速度预览**（照 PJSK 前端那套：设置里调速度时能直接看到音符怎么落）。
    ///
    /// 为什么要单独一屏：数字（"1.2x"）说不清手感，玩家要看的是"音符多久落到判定线"。
    /// 这里不复制任何新视觉 —— 用的就是游戏本体的下落曲线（`PlayPerspective.ViewProgress01/SizeScale`）
    /// 与判定线，和演奏页同源；改速度就是改 `GameManager.speedMultiplier`（与设置页/暂停页同一条路），
    /// 所以"预览里看到什么，演奏时就是什么"。
    ///
    /// 界面元素：标题 / 下落区（4 个循环下落的音符 + 判定线）/ 当前倍率 / ± / 完成。
    /// </summary>
    public sealed class FallSpeedPreviewController : MonoBehaviour
    {
        [Header("引用（SceneBuilder 注入）")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform lanePlayfield;      // 掉落区（局部坐标）
        [SerializeField] private RectTransform[] previewNotes;     // 循环下落的音符（SceneBuilder 建 4 个）
        [SerializeField] private TextMeshProUGUI speedText;        // 当前倍率
        [SerializeField] private TextMeshProUGUI fallTimeText;     // "音符 X.Xs 落到判定线"
        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private FallSpeedController fallSpeed;    // 唯一速度源（同源同步）

        [Header("几何（与演奏页同源：出生 Y / 判定线 Y 都是画布局部坐标）")]
        [SerializeField] private float laneSpawnY = 560f;
        [SerializeField] private float laneJudgeY = 0f;

        /// <summary>与校准面板同一个坑：面板在场景里是隐藏的，第一次 SetActive(true) 会触发 Awake，
        /// 里面的"默认隐藏"会把刚打开的面板关掉 → 用标记先置位。</summary>
        private bool openRequested;
        private float clock;

        private void Awake()
        {
            if (minusButton != null) minusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(-0.1f); });
            if (plusButton != null) plusButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButtonClick(); Nudge(+0.1f); });
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (panelRoot != null && !openRequested) panelRoot.SetActive(false);
            RefreshText();
        }

        public void Open()
        {
            openRequested = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            clock = 0f;
            RefreshText();
            Debug.Log(string.Format("[FallSpeed] 打开下落速度预览：当前 {0:0.0}x（音符 {1:0.00}s 落到判定线）",
                CurrentSpeed, FallTime));
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;

            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            if (previewNotes == null || previewNotes.Length == 0) return;

            // 用不受 timeScale 影响的时钟：暂停页打开时也能看预览
            clock += Time.unscaledDeltaTime;
            float fall = FallTime;
            float gap = 1f / previewNotes.Length;      // 屏幕上均匀铺 4 个音符

            for (int i = 0; i < previewNotes.Length; i++)
            {
                RectTransform note = previewNotes[i];
                if (note == null) continue;

                float p = Mathf.Repeat(clock / fall + i * gap, 1f);
                float v = PlayPerspective.ViewProgress01(p);
                note.anchoredPosition = new Vector2(note.anchoredPosition.x,
                    Mathf.Lerp(laneSpawnY, laneJudgeY, v));
                float s = PlayPerspective.SizeScale(p);
                note.localScale = new Vector3(s, s, 1f);

                // 过线瞬间的收尾：最后 6% 淡出（演奏页是命中特效，这里只要"到线了"的观感）
                Image img = note.GetComponent<Image>();
                if (img != null)
                {
                    Color c = img.color;
                    c.a = p > 0.94f ? Mathf.InverseLerp(1f, 0.94f, p) : 1f;
                    img.color = c;
                }
            }
        }

        private void Nudge(float delta)
        {
            if (fallSpeed != null) fallSpeed.AdjustSpeed(delta);
            RefreshText();
        }

        private float CurrentSpeed
        {
            get
            {
                if (fallSpeed != null) return fallSpeed.CurrentSpeed;
                return GameManager.Instance != null ? GameManager.Instance.speedMultiplier : 1f;
            }
        }

        /// <summary>音符从出生到判定线的秒数 = `GameManager.ActualFallTime`（= 基础在屏时长 / 速度倍率）。</summary>
        private float FallTime
        {
            get
            {
                float t = GameManager.Instance != null ? GameManager.Instance.ActualFallTime : 2f;
                return Mathf.Max(0.2f, t);
            }
        }

        private void RefreshText()
        {
            if (speedText != null) speedText.text = CurrentSpeed.ToString("0.0") + "x";
            if (fallTimeText != null)
                fallTimeText.text = Loc.T("fallPreview.time", FallTime.ToString("0.00"));
        }
    }
}
