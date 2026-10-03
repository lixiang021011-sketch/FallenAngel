using System.Collections.Generic;
using FallenAngel.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 底部声部切换条（作者 2026-10-03 定：按钮放下方；C2 方案）。
    ///
    /// 数量自适应：芯片按 <see cref="SongParts.PartNames"/> 生成 —— 原型期 2 件就是 2 个，
    /// 将来一首歌 5 件乐器就是 5 个（单个宽度上限 240，数量多时自动铺满可用宽）。
    ///
    /// 与演奏输入的关系（工程事实）：芯片是 uGUI 图形，`InputManager.IsPointerOverUI` 会把
    /// 落在它上面的触摸挡在判定层之外，所以底栏这段不再是轨道输入区；
    /// 对应地，键帽（KeyArea）在运行时整体上移，让"看得见的轨道"不压在底栏上。
    ///
    /// 防误触两层（作者 2026-10-03 定）：
    ///   ① 只在"按下→抬起都在同一个芯片内、且按下时长 ≤ <see cref="MaxTapSeconds"/>"时算一次点击
    ///      —— 拇指歇在屏幕上（长按）抬起不算；
    ///   ② 点击本身还要**双击确认**（<see cref="PartSwitchTap.ConfirmSeconds"/> 内连点同一枚）：
    ///      第一下只把芯片点亮成"待确认"，第二下才真的换声部。
    /// 切换条在屏幕最底部、正是拇指常驻与滑过的地方，所以要两层一起挡。
    /// </summary>
    public sealed class PartSwitchBar : MonoBehaviour
    {
        public const float BarHeight = 132f;
        public const float ChipHeight = 108f;
        public const float ChipGap = 12f;
        public const float ChipMaxWidth = 240f;
        public const float BottomMargin = 16f;
        public const float MaxTapSeconds = 0.4f;
        /// <summary>底栏可用宽（画布 1080 − 两侧 20 边距），芯片按它均分</summary>
        public const float RowWidth = 1040f;

        private RectTransform barRect;
        private RectTransform chipRow;
        private TMP_FontAsset font;
        private readonly List<PartSwitchChip> chips = new List<PartSwitchChip>();
        private readonly PartSwitchTap tap = new PartSwitchTap();

        private void Start()
        {
            Build();
            Subscribe();
            RebuildChips();
        }

        private void OnEnable() { Subscribe(); }
        private void OnDisable() { Unsubscribe(); }

        private GameManager Gm { get { return GameManager.Instance; } }

        private void Subscribe()
        {
            GameManager gm = Gm;
            if (gm == null) return;
            gm.OnGameStart -= HandleGameStart;
            gm.OnGameStart += HandleGameStart;
            gm.OnPartChanged -= Refresh;
            gm.OnPartChanged += Refresh;
        }

        private void Unsubscribe()
        {
            GameManager gm = Gm;
            if (gm == null) return;
            gm.OnGameStart -= HandleGameStart;
            gm.OnPartChanged -= Refresh;
        }

        private void HandleGameStart()
        {
            tap.Reset();
            RebuildChips();
            Refresh();
        }

        private void Update()
        {
            // 待确认超时 → 熄灭（不换声部）
            if (tap.Tick(Time.unscaledTime)) Refresh();
        }

        // ── 建层 ────────────────────────────────────────────────────────
        private void Build()
        {
            Transform lanes = transform.Find("LanesBG");
            Transform hud = transform.Find("HUDPanel");
            if (lanes == null) return;

            LiftKeyArea(lanes);

            GameObject barGO = new GameObject("PartSwitchBar", typeof(RectTransform), typeof(Image));
            barRect = (RectTransform)barGO.transform;
            barRect.SetParent(transform, false);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, BarHeight);
            barRect.SetSiblingIndex(hud != null ? hud.GetSiblingIndex() : transform.childCount - 1);
            Image barImage = barGO.GetComponent<Image>();
            barImage.color = new Color(0.035f, 0.07f, 0.09f, 0.94f);
            barImage.raycastTarget = true;   // 底栏整条吃掉触摸：不让手指穿到底下

            GameObject rowGO = new GameObject("Chips", typeof(RectTransform));
            chipRow = (RectTransform)rowGO.transform;
            chipRow.SetParent(barRect, false);
            chipRow.anchorMin = new Vector2(0.5f, 0f);
            chipRow.anchorMax = new Vector2(0.5f, 0f);
            chipRow.pivot = new Vector2(0.5f, 0f);
            chipRow.anchoredPosition = new Vector2(0f, BottomMargin);
            chipRow.sizeDelta = new Vector2(1040f, ChipHeight);

            if (hud != null)
            {
                TextMeshProUGUI sample = hud.GetComponentInChildren<TextMeshProUGUI>(true);
                if (sample != null) font = sample.font;
            }
        }

        /// <summary>底栏占掉屏幕最底一条，键帽整体上移，让"看得见的轨道"不压在按钮上。</summary>
        private static void LiftKeyArea(Transform lanes)
        {
            Transform keyArea = lanes.Find("KeyArea");
            if (keyArea == null) return;
            RectTransform rect = keyArea as RectTransform;
            if (rect == null || rect.anchoredPosition.y > 1f) return;   // 已经抬过就不重复抬
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y + BarHeight);
        }

        /// <summary>按当前曲目的声部表重建芯片（数量自适应）</summary>
        private void RebuildChips()
        {
            if (chipRow == null) return;
            for (int i = 0; i < chips.Count; i++)
                if (chips[i] != null) Destroy(chips[i].gameObject);
            chips.Clear();

            GameManager gm = Gm;
            SongParts definition = gm != null && gm.Parts != null ? gm.Parts.Definition : null;
            string[] names = definition != null ? definition.PartNames : null;
            if (names == null || names.Length == 0) return;

            float chipWidth = ChipWidthFor(names.Length, chipRow.sizeDelta.x);
            float total = chipWidth * names.Length + ChipGap * (names.Length - 1);
            float startX = -total * 0.5f;
            for (int i = 0; i < names.Length; i++)
                chips.Add(CreateChip(names[i], startX + i * (chipWidth + ChipGap) + chipWidth * 0.5f, chipWidth));
            Refresh();
        }

        private PartSwitchChip CreateChip(string partName, float centerX, float width)
        {
            GameObject go = new GameObject("Chip_" + partName, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(chipRow, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, ChipHeight);
            rect.anchoredPosition = new Vector2(centerX, 0f);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = true;

            GameObject labelGO = new GameObject("Label", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)labelGO.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 6f);
            labelRect.offsetMax = new Vector2(-8f, -6f);
            TextMeshProUGUI label = labelGO.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = partName;
            label.fontSize = 34f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;

            PartSwitchChip chip = go.AddComponent<PartSwitchChip>();
            chip.Bind(partName, image, label, HandleChipTapped);
            return chip;
        }

        private void HandleChipTapped(string partName)
        {
            GameManager gm = Gm;
            if (gm == null || gm.Parts == null) return;
            if (partName == gm.Parts.PartName) return;      // 点当前声部是空操作
            if (!tap.Register(partName, Time.unscaledTime))
            {
                Debug.Log("[PartSwitchBar] 待确认：" + partName + "（再点一次切换）");
                Refresh();
                return;
            }
            Debug.Log("[PartSwitchBar] 双击确认，切换到 " + partName);
            gm.SwitchPart(partName);
        }

        /// <summary>芯片宽度：装满一行，但不超过 <see cref="ChipMaxWidth"/>（2 件时不至于拉成两条巨条）。</summary>
        public static float ChipWidthFor(int partCount, float rowWidth)
        {
            if (partCount <= 0) return 0f;
            return Mathf.Min(ChipMaxWidth, (rowWidth - ChipGap * (partCount - 1)) / partCount);
        }

        /// <summary>当前声部高亮，其余压暗（颜色仍来自 PartPalette）</summary>
        private void Refresh()
        {
            GameManager gm = Gm;
            string active = gm != null && gm.Parts != null ? gm.Parts.PartName : null;
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i] == null) continue;
                bool isActive = chips[i].partName == active;
                chips[i].SetActivePart(isActive);
                chips[i].SetArmed(!isActive && chips[i].partName == tap.PendingPart);
            }
        }
    }

    /// <summary>一枚声部芯片：只在"快速轻点"时触发，长按（拇指歇着）不算。</summary>
    public sealed class PartSwitchChip : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public string partName { get; private set; }
        private Image image;
        private TextMeshProUGUI label;
        private System.Action<string> onTapped;
        private float downTime;
        private bool down;
        private bool armedState;

        public void Bind(string name, Image target, TextMeshProUGUI text, System.Action<string> tapped)
        {
            partName = name;
            image = target;
            label = text;
            onTapped = tapped;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            down = true;
            downTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!down) return;
            down = false;
            if (Time.unscaledTime - downTime <= PartSwitchBar.MaxTapSeconds) onTapped?.Invoke(partName);
        }

        /// <summary>当前声部：实底色 + 顶部高亮条；其余：压暗等点击</summary>
        public void SetActivePart(bool active)
        {
            Color color;
            if (!PartPalette.TryGetColor(partName, out color))
            {
                if (image != null) image.color = new Color(0.10f, 0.16f, 0.18f, 1f);
                return;
            }
            if (image != null)
                image.color = active
                    ? new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f, 1f)
                    : new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 1f);
            if (label != null)
                label.color = active ? new Color(0.09f, 0.11f, 0.12f, 1f) : new Color(color.r, color.g, color.b, 0.85f);
        }

        /// <summary>"待确认"：整体提亮成白，配合日志告诉玩家再点一次（第一下不换声部）</summary>
        public void SetArmed(bool armed)
        {
            armedState = armed;
            if (!armed || image == null) return;   // 熄灭时颜色已由 SetActivePart 复原
            image.color = new Color(0.92f, 0.96f, 0.97f, 1f);
            if (label != null) label.color = new Color(0.08f, 0.10f, 0.11f, 1f);
        }
    }
}
