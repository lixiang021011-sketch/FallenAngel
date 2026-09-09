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
    /// 商店弹窗：进店自动弹出（每个商店节点仅自动弹一次），右上角 X 关闭后由地图页"商店"按钮重开。
    /// 内容：现金标题 + 候选网格（2 列）+ 购买确认弹窗（Ask 模式）。
    /// 挂 ShopPanel（Canvas 根，初始非激活，嵌套 Canvas 排序 253）；session 运行时查找。
    /// </summary>
    public sealed class ShopPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private Button closeButton;

        private PortfolioSession session;
        private TMP_FontAsset font;
        private bool dirty = true;
        private string autoOpenedNode;       // 已自动弹出过的商店节点
        private string confirmText;
        private Action confirmAction;
        private string promptText;           // 单按钮提示（资金不足等），不改变确认态
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;
        private readonly Color accent = DeepSeaTheme.Accent;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            session = FindObjectOfType<PortfolioSession>();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
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
            if (session != null) { session.OnChanged -= OnSessionChanged; session.OnChanged += OnSessionChanged; }
            Loc.OnLanguageChanged -= MarkDirty;
            Loc.OnLanguageChanged += MarkDirty;
        }
        private void OnDisable()
        {
            if (session != null) session.OnChanged -= OnSessionChanged;
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

        private string T(string key, params object[] values) => Loc.T("portfolio." + key, values);

        /// <summary>进店自动弹出（同一商店节点只自动弹一次）；离开商店后自动收起（事件结束）</summary>
        private void OnSessionChanged()
        {
            if (panelRoot == null) return;
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            // 已离开商店（事件结束）：静默收起（不播放点击音）
            if (session != null && !session.InShop && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
                confirmAction = null;
                session.SetConfirmation(false);
                return;
            }
            if (session != null && session.InShop && session.Run != null && !panelRoot.activeSelf
                && session.Run.currentNodeId != autoOpenedNode)
            {
                autoOpenedNode = session.Run.currentNodeId;
                Open();
                return;
            }
            dirty = true;
        }

        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            if (session == null || !session.InShop)
            {
                Debug.LogWarning("[ShopPanelController] 当前不在商店房间");
                return;
            }
            if (panelRoot != null) panelRoot.SetActive(true);
            dirty = true;
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
            confirmAction = null;
            promptText = null;
            session?.SetConfirmation(false);
        }

        private void Ask(string text, Action action)
        {
            session.SetConfirmation(true);
            confirmText = text; confirmAction = action; dirty = true;
        }

        private void Render()
        {
            if (contentRoot == null || session.Run == null) return;
            foreach (Transform child in contentRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }

            var page = Box(contentRoot, "ShopPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;

            DeepSeaTheme.CashBar(page, 20, 135, session.Run.runCash, font).text = T("shopTitle", session.Run.runCash);

            // 整批刷新：剩余预算 >0 才显示（E0/E1 开局发放；无变化不扣次数）
            int budget = session.ShopRefreshBudget;
            if (budget > 0)
                Button(page, "ShopRefreshButton", T("shopRefresh", budget), 650, 140, 310, 60, () =>
                {
                    int b = session.ShopRefreshBudget;
                    Ask(T("confirmRefresh", b, Math.Max(0, b - 1)), () => session.RefreshShop());
                }, accent);

            var cands = session.Run.shopCandidates;
            if (cands == null || cands.Count == 0)
            {
                Label(page, T("shopSoldOut"), 20, 350, 960, 100, 30);
            }
            else
            {
                // 2 列网格（至多 4 件）：ID + 报价；现金不足的格子置灰
                for (int i = 0; i < cands.Count; i++)
                {
                    string id = cands[i];
                    int price = session.QuoteEquipmentPrice(id);
                    float x = 90 + (i % 2) * 430;
                    float y = 300 + (i / 2) * 200;
                    bool affordable = session.Run != null && session.Run.runCash >= price;
                    Button(page, "ShopBuy_" + id, id + "\n" + price, x, y, 400, 170, () =>
                    {
                        int quote = session.QuoteEquipmentPrice(id);
                        if (session.Run == null) return;
                        // 先查现金：不足弹提示（单按钮），不进购买确认，留在商店页
                        if (session.Run.runCash < quote)
                        {
                            promptText = T("shopInsufficient");
                            dirty = true;
                            return;
                        }
                        Ask(T("confirmPurchase", id, quote, session.Run.runCash, session.Run.runCash - quote), () =>
                        {
                            session.PurchaseEquipment(id);
                            // 确认后仍可能失败（状态变化）：在面板内提示并清错，不跳错误页
                            if (session.Error != null)
                            {
                                promptText = T("shopFailed");
                                session.ClearError();
                                dirty = true;
                            }
                        });
                    }, affordable ? card : new Color(.14f, .17f, .22f));
                }
            }

            // 商品栏位最后：结束商店事件（每个商店房间只允许一次进入，离开后房间置灰）
            int leaveIndex = cands == null ? 0 : cands.Count;
            Button(page, "ShopLeaveButton", T("shopLeave"), 90 + (leaveIndex % 2) * 430, 300 + (leaveIndex / 2) * 200,
                400, 170, () => session.LeaveRoom(), new Color(.35f, .16f, .16f));

            if (confirmAction != null) RenderConfirmation(page);
            else if (promptText != null) RenderPrompt(page);
        }

        /// <summary>单按钮提示（资金不足/购买失败）：不改变确认态，确定后清错并留在商店页</summary>
        private void RenderPrompt(RectTransform page)
        {
            var modal = Box(page, "PromptShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            var box = Box(modal, "Prompt", 200, 700, 600, 340, card);
            Label(box, promptText, 30, 30, 540, 180, 30);
            Button(box, "PromptOK", T("confirm"), 170, 240, 260, 70, () =>
            {
                promptText = null;
                session.ClearError();
                dirty = true;
            });
        }

        private void RenderConfirmation(RectTransform page)
        {
            var modal = Box(page, "ConfirmationShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            var box = Box(modal, "Confirmation", 75, 530, 850, 500, card);
            Label(box, confirmText, 35, 40, 780, 260, 32);
            Button(box, "Confirm", T("confirm"), 35, 350, 365, 90, () =>
            {
                var action = confirmAction; confirmAction = null; session.SetConfirmation(false); dirty = true; action?.Invoke();
            });
            Button(box, "Cancel", T("cancel"), 450, 350, 365, 90, () => { confirmAction = null; session.SetConfirmation(false); dirty = true; });
        }

        // ---- 渲染辅助 ----
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
            var label = Label(rect, text, 8, 5, w - 16, h - 10, 28);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                session.Execute(action);
            });
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            return button;
        }
    }
}
