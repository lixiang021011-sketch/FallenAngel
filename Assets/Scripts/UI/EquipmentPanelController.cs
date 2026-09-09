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
    /// 装备背包面板：网格展示本局已持有装备（4 列），点击图标在底部显示该装备的效果说明条。
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
        private string selectedId;
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;
        private readonly Color accent = DeepSeaTheme.Accent;
        private readonly Color selectedColor = DeepSeaTheme.Owned;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            session = FindObjectOfType<PortfolioSession>();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            // 全屏背景可点击：点空白处关闭特效条签（面板本体自带 Image，补 Button 即可接收射线）
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

        private void Render()
        {
            if (contentRoot == null || session.Run == null) return;
            foreach (Transform child in contentRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }

            var page = Box(contentRoot, "EquipmentPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;

            var held = session.Run.heldEquipmentIds;
            if (held == null || held.Count == 0)
            {
                Label(page, T("empty"), 20, 300, 960, 200, 30);
                return;
            }

            // 网格：4 列 × 至多 5 行（容量 20）
            for (int i = 0; i < held.Count; i++)
            {
                string id = held[i];
                var item = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == id);
                if (item == null) continue; // ReadRun 已校验存在，双保险
                float x = 80 + (i % 4) * 225;
                float y = 220 + (i / 4) * 190;
                bool isSelected = id == selectedId;
                Button(page, "Equip_" + id, id + "\n" + T("price", item.BasePrice),
                    x, y, 200, 160, () => { selectedId = id; dirty = true; },
                    isSelected ? selectedColor : accent);
            }

            // 特效条签：附着在被选中装备格的图标下方（含连接细条），横向夹在页面内
            if (!string.IsNullOrEmpty(selectedId))
            {
                var item = PortfolioConfig.EquipmentBase.First(e => e.EquipmentId == selectedId);
                int idx = held.IndexOf(selectedId);
                if (idx >= 0)
                {
                    float iconX = 80 + (idx % 4) * 225;
                    float iconY = 220 + (idx / 4) * 190;
                    const float tagW = 620f;
                    const float tagH = 230f;
                    float tagX = Mathf.Clamp(iconX + 100 - tagW * 0.5f, 40, 960 - tagW);
                    float tagY = iconY + 175;
                    // 连接细条：图标底边中点 → 条签顶边
                    var link = Box(page, "EquipTagLink", iconX + 100 - 1.5f, iconY + 160, 3, 15, accent);
                    link.GetComponent<Image>().raycastTarget = false;
                    var tag = Box(page, "EquipTag", tagX, tagY, tagW, tagH, card);
                    var label = Label(tag, selectedId + "  ·  " + item.Description, 15, 15, tagW - 30, tagH - 30, 23);
                    label.alignment = TextAlignmentOptions.TopLeft;
                }
            }
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
        private Button Button(Transform parent, string name, string text, float x, float y, float w, float h, Action action, Color? color = null)
        {
            var rect = Box(parent, name, x, y, w, h, color ?? accent);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var label = Label(rect, text, 8, 5, w - 16, h - 10, 24);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                action();
            });
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            return button;
        }
    }
}
