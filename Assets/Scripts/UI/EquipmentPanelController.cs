using System;
using System.Linq;
using FallenAngel.Audio;
using FallenAngel.Core;
using FallenAngel.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 装备背包（设计稿 09 装备背包）：网格在上、固定详情区在下。
    /// 详情不再用会盖住其他格子的附着式长条签；空背包显示空图形与 0/20。
    /// 挂 EquipmentPanel（Canvas 根，初始非激活，自带嵌套 Canvas 排序 252 盖过地图页）；
    /// 入口是地图页的"装备 n/20"按钮（PortfolioPanelController 注入打开）。
    /// session 运行时查找（同 TalentPanelController 模式）。
    /// </summary>
    public sealed class EquipmentPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform contentRoot; // 内容容器（Render 重建其子树）
        [SerializeField] private Button closeButton;       // 右上角 X

        private PortfolioSession session;
        private TMP_FontAsset font;
        private bool dirty = true;
        private bool frameBuilt;
        private string selectedId;
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            session = FindObjectOfType<PortfolioSession>();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            // 全屏背景可点击：点空白处取消选中（面板本体自带 Image，补 Button 即可接收射线）
            Button bgButton = panelRoot != null ? panelRoot.GetComponent<Button>() : null;
            if (bgButton == null && panelRoot != null) bgButton = panelRoot.AddComponent<Button>();
            if (bgButton != null) bgButton.onClick.AddListener(() =>
            {
                selectedId = null;
                dirty = true;
            });
        }

        private void OnEnable() { Subscribe(); }
        private void Start()
        {
            Subscribe();
            font = GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.font).FirstOrDefault(f => f != null);
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }
        private void Subscribe()
        {
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            if (session != null) { session.OnChanged -= MarkDirty; session.OnChanged += MarkDirty; }
            Loc.OnLanguageChanged -= MarkDirty;
            Loc.OnLanguageChanged += MarkDirty;
        }
        private void OnDisable()
        {
            if (session != null) session.OnChanged -= MarkDirty;
            Loc.OnLanguageChanged -= MarkDirty;
        }
        private void MarkDirty() { dirty = true; }

        private void LateUpdate()
        {
            if (!dirty || !IsOpen || session == null) return;
            dirty = false;
            Render();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && IsOpen) Close();
        }

        private string T(string key, params object[] values) => Loc.T("equipmentPanel." + key, values);

        /// <summary>打开背包（地图页"装备 n/20"按钮；无局时拒绝打开）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            if (session == null || session.Run == null)
            {
                Debug.LogWarning("[EquipmentPanelController] 当前无局，无法打开背包");
                return;
            }
            if (panelRoot != null) panelRoot.SetActive(true);
            selectedId = null;
            dirty = true;
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>
        /// 页面骨架：深海底色 + 右上角关闭按钮收进安全边距。
        /// 页标题由 SceneBuilder 建、DeepSeaPresentation 统一装饰，这里不移动它以免遗留旧位置的刻线。
        /// </summary>
        private void BuildFrame()
        {
            if (frameBuilt || panelRoot == null) return;
            frameBuilt = true;
            Loc.AddFallback("equipmentPanel.held", "持有 {0} / {1}", "Owned {0} / {1}");
            Loc.AddFallback("equipmentPanel.emptyShort", "暂无装备", "No equipment yet");

            var bg = panelRoot.GetComponent<Image>();
            if (bg != null) bg.color = DeepSeaTheme.Background;
            DeepSeaTheme.Backdrop(panelRoot.transform);

            RectTransform closeRect = closeButton != null ? closeButton.GetComponent<RectTransform>() : null;
            if (closeRect != null)
            {
                closeRect.anchorMin = closeRect.anchorMax = new Vector2(1, 1);
                closeRect.pivot = new Vector2(1, 1);
                closeRect.anchoredPosition = new Vector2(-DeepSeaTheme.SafeMargin, -30);
                closeRect.sizeDelta = new Vector2(96, 96);
                DeepSeaTheme.ApplyRole(closeButton, DeepSeaTheme.ButtonRole.Secondary);
            }
        }

        private void Render()
        {
            if (contentRoot == null || session == null || session.Run == null) return;
            BuildFrame();
            foreach (Transform child in contentRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }

            var page = Box(contentRoot, "EquipmentPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;

            var held = session.Run.heldEquipmentIds;
            int capacity = session.EquipmentCapacity;
            int count = held == null ? 0 : held.Count;
            if (!string.IsNullOrEmpty(selectedId) && (held == null || !held.Contains(selectedId))) selectedId = null;

            // 头部：持有量 / 容量 + 操作提示（容量满时补一句，不新增按钮）
            Label(page, T("held", count, capacity), 40, 138, 920, 56, DeepSeaTheme.ActionSize);
            string hint = count >= capacity ? T("hint") + "   ·   " + Loc.T("portfolio.equipFull") : T("hint");
            Label(page, hint, 40, 204, 920, 44, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;

            if (count == 0)
            {
                // 空态：空图形 + "暂无装备"，保留 0/20
                var icon = DeepSeaTheme.Graphic(page, "EmptyEquipmentIcon", DeepSeaGraphic.Shape.Empty);
                var iconRect = (RectTransform)icon.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, 1);
                iconRect.pivot = new Vector2(.5f, 1);
                iconRect.anchoredPosition = new Vector2(0, -320);
                iconRect.sizeDelta = new Vector2(150, 150);
                icon.color = DeepSeaTheme.Muted;
                var emptyLabel = Label(page, T("emptyShort"), 40, 510, 920, 64, DeepSeaTheme.BodySize);
                emptyLabel.alignment = TextAlignmentOptions.Center;
                var emptyHint = Label(page, T("empty"), 40, 600, 920, 160, DeepSeaTheme.CaptionSize);
                emptyHint.alignment = TextAlignmentOptions.Center;
                emptyHint.color = DeepSeaTheme.Muted;
                return;
            }

            // 网格：4 列 × 至多 5 行（容量 20）
            for (int i = 0; i < held.Count; i++)
            {
                string id = held[i];
                var item = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == id);
                if (item == null) continue; // ReadRun 已校验存在，双保险
                float x = 40 + (i % 4) * 240;
                float y = 272 + (i / 4) * 170;
                Cell(page, id, item.BasePrice, id == selectedId, x, y, 200, 150, () =>
                {
                    selectedId = id == selectedId ? null : id;
                    dirty = true;
                });
            }

            RenderDetail(page);
        }

        /// <summary>装备格：选中为冷白实底；未被选中为深底描边</summary>
        private void Cell(Transform parent, string id, int price, bool selected, float x, float y, float w, float h, Action action)
        {
            var rect = Box(parent, "Equip_" + id, x, y, w, h, selected ? ink : card);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var label = Label(rect, id + "\n" + T("price", price), 16, 12, w - 32, h - 24, 30);
            label.alignment = TextAlignmentOptions.Center;
            label.color = selected ? DeepSeaTheme.Background : ink;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                action();
            });
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            if (selected)
            {
                var symbol = button.transform.Find("EquipmentSymbol");
                if (symbol != null)
                {
                    var graphic = symbol.GetComponent<DeepSeaGraphic>();
                    if (graphic != null) graphic.color = DeepSeaTheme.Background;
                }
            }
        }

        /// <summary>固定详情区：代号/价格 + 效果说明（替代会遮挡格子的悬浮长条签）</summary>
        private void RenderDetail(RectTransform page)
        {
            const float dx = 40, dy = 1140, dw = 920, dh = 540;
            var detail = Box(page, "EquipmentDetail", dx, dy, dw, dh, card);
            DeepSeaTheme.CardSurface(detail, card);
            if (string.IsNullOrEmpty(selectedId))
            {
                var idle = Label(detail, T("hint"), 30, 230, dw - 60, 80, DeepSeaTheme.BodySize);
                idle.alignment = TextAlignmentOptions.Center;
                idle.color = DeepSeaTheme.Muted;
                return;
            }

            var item = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == selectedId);
            if (item == null)
            {
                var missing = Label(detail, T("emptyShort"), 30, 230, dw - 60, 80, DeepSeaTheme.BodySize);
                missing.alignment = TextAlignmentOptions.Center;
                return;
            }

            Label(detail, selectedId + "   ·   " + T("price", item.BasePrice), 30, 24, dw - 60, 56, DeepSeaTheme.ActionSize);
            var body = Label(detail, item.Description, 30, 92, dw - 60, 260, DeepSeaTheme.BodySize);
            body.alignment = TextAlignmentOptions.TopLeft;
            if (!PortfolioEffectStatus.IsEquipmentLive(item.EffectId))
                Label(detail, Loc.T("portfolio.effectNotLive"), 30, 372, dw - 60, 44, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;
        }

        // ---- 渲染辅助（平移自 TalentPanelController） ----
        private RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            if (color.HasValue) go.AddComponent<Image>().color = color.Value;
            return rect;
        }
        private TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h, int size = 26)
        {
            var label = Box(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size; label.color = ink;
            label.raycastTarget = false; label.enableWordWrapping = true;
            return label;
        }
    }
}
