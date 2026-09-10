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
    /// 商店页（设计稿 07 商店 / 08 购买确认）：
    /// 上半是候选网格，下半是固定详情区——点选候选只在详情区展示效果与价格，
    /// 购买按钮固定在详情区底部，购买仍走二次确认；资金不足时按钮置灰并写明差额。
    /// 进店自动弹出（同一商店节点只自动弹一次），离开商店房间后自动收起。
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
        private bool frameBuilt;
        private string autoOpenedNode;      // 已自动弹出过的商店节点
        private string selectedId;          // 详情区选中的候选
        private string feedbackText;        // 购买成功反馈（下一次选择/刷新时清除）
        private string confirmText;
        private Action confirmAction;
        private string promptText;          // 单按钮提示（购买失败），不改变确认态
        private string pendingBuyId;        // 购买确认中的装备
        private bool useOptionalPurchase;
        private bool flashPending;          // FX003：购买成功后在下一次重绘的详情卡上播放闪光
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;

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
                pendingBuyId = null;
                useOptionalPurchase = false;
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
            selectedId = null;
            feedbackText = null;
            dirty = true;
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
            confirmAction = null;
            pendingBuyId = null;
            useOptionalPurchase = false;
            promptText = null;
            session?.SetConfirmation(false);
        }

        private void Ask(string text, Action action)
        {
            pendingBuyId = null;
            useOptionalPurchase = false;
            session.SetConfirmation(true);
            confirmText = text; confirmAction = action; dirty = true;
        }

        private void AskPurchase(string id)
        {
            pendingBuyId = id;
            session.SetConfirmation(true);
            confirmText = null;
            confirmAction = () => { };
            dirty = true;
        }

        /// <summary>
        /// 页面骨架：深海底色 + 右上角关闭按钮收进安全边距。
        /// 页标题由 SceneBuilder 建、DeepSeaPresentation 统一装饰（居中 + 刻线），这里不再移动它，
        /// 避免遗留装饰器画在旧位置的刻线。
        /// </summary>
        private void BuildFrame()
        {
            if (frameBuilt || panelRoot == null) return;
            frameBuilt = true;
            Loc.AddFallback("portfolio.priceOnly", "价格 {0}", "Price {0}");
            Loc.AddFallback("portfolio.shopSelectHint", "点选商品查看效果与价格", "Select an item to view its effect and price");
            Loc.AddFallback("portfolio.shopNoRefresh", "本局刷新次数已用尽", "No shop refreshes left this run");
            Loc.AddFallback("portfolio.shopPriceLine", "价格 {0}   ·   余额 {1} → {2}", "Price {0}   ·   Cash {1} → {2}");
            Loc.AddFallback("portfolio.shopShortBy", "资金不足 · 差 {0}", "Not enough cash · short {0}");
            Loc.AddFallback("portfolio.shopBuy", "购买", "Buy");
            Loc.AddFallback("portfolio.shopBought", "已获得 {0}", "Acquired {0}");
            Loc.AddFallback("portfolio.shopLeaveNote", "离开后本商店不可再次进入。", "This shop cannot be re-entered after you leave.");
            Loc.AddFallback("portfolio.shopRefreshNote", "整批刷新按商店权重重抽，结果不变不扣次数。", "A refresh re-rolls goods by shop weights; an unchanged roll costs nothing.");
            Loc.AddFallback("portfolio.shopSoldOutNote", "本店候选已买空，可刷新或离开。", "Every candidate is gone; refresh or leave.");

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

            var run = session.Run;
            var cands = run.shopCandidates;
            bool soldOut = cands == null || cands.Count == 0;
            if (!string.IsNullOrEmpty(selectedId) && (soldOut || !cands.Contains(selectedId))) selectedId = null;

            var page = Box(contentRoot, "ShopPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;

            // 顶部：现金 + 整批刷新（预算耗尽隐藏按钮，改说明文字）
            DeepSeaTheme.CashBar(page, 40, 140, run.runCash, font).text = T("shopTitle", run.runCash);
            int budget = session.ShopRefreshBudget;
            if (budget > 0)
            {
                Action(page, "ShopRefreshButton", T("shopRefresh", budget), 620, 136, 340, 72, () =>
                {
                    int b = session.ShopRefreshBudget;
                    Ask(T("confirmRefresh", b, Math.Max(0, b - 1)), () => session.RefreshShop());
                }, DeepSeaTheme.ButtonRole.Secondary, 28, DeepSeaGraphic.Shape.Refresh);
            }
            else
            {
                Label(page, T("shopNoRefresh"), 620, 150, 340, 56, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;
            }

            // 状态行：购买成功反馈优先，否则是操作提示
            var hint = Label(page, feedbackText ?? T("shopSelectHint"), 40, 236, 920, 44, DeepSeaTheme.CaptionSize);
            hint.color = feedbackText != null ? DeepSeaTheme.Accent : DeepSeaTheme.Muted;

            // 候选网格（2 列；E10 会追加候选，行距按行数收缩以免压到详情区）：点选只切换详情区，不直接购买
            if (soldOut)
            {
                Label(page, T("shopSoldOut"), 40, 300, 920, 60, DeepSeaTheme.BodySize);
                Label(page, T("shopSoldOutNote"), 40, 366, 920, 44, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;
            }
            else
            {
                const float gridTop = 292f, gridBottom = 930f;
                int rows = Mathf.Max(1, (cands.Count + 1) / 2);
                float pitch = Mathf.Min(158f, (gridBottom - gridTop) / rows);
                float cellH = Mathf.Min(140f, pitch - 16f);
                for (int i = 0; i < cands.Count; i++)
                {
                    string id = cands[i];
                    int price = session.QuoteEquipmentPrice(id);
                    bool affordable = run.runCash >= price;
                    float x = 40 + (i % 2) * 480;
                    float y = gridTop + (i / 2) * pitch;
                    SelectCell(page, "ShopBuy_" + id, id, price, affordable, id == selectedId, x, y, 440, cellH, () =>
                    {
                        selectedId = id;
                        feedbackText = null;
                        useOptionalPurchase = false;
                        dirty = true;
                    });
                }
            }

            RenderDetail(page, cands, soldOut);
            RenderLeave(page);

            if (confirmAction != null) RenderConfirmation(page);
            else if (promptText != null) RenderPrompt(page);
        }

        /// <summary>候选格：选中为冷白实底，买不起为深灰（仍可选中，在详情区看差额）</summary>
        private void SelectCell(Transform parent, string name, string id, int price, bool affordable, bool selected,
            float x, float y, float w, float h, Action action)
        {
            Color fill = selected ? ink : affordable ? card : new Color(.13f, .165f, .21f);
            var rect = Box(parent, name, x, y, w, h, fill);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var label = Label(rect, id + "\n" + T("priceOnly", price), 16, 12, w - 32, h - 24, 30);
            label.alignment = TextAlignmentOptions.Center;
            label.color = selected ? DeepSeaTheme.Background : affordable ? ink : DeepSeaTheme.Muted;
            button.onClick.AddListener(() =>
            {
                AudioManager.Instance?.PlayButtonClick();
                action();
            });
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
        }

        /// <summary>固定详情区：标题/效果/价格与余额/可选折扣/底部购买按钮</summary>
        private void RenderDetail(RectTransform page, System.Collections.Generic.List<string> cands, bool soldOut)
        {
            const float dx = 40, dy = 930, dw = 920, dh = 560;
            var detail = Box(page, "ShopDetail", dx, dy, dw, dh, card);
            DeepSeaTheme.CardSurface(detail, card);
            if (flashPending) { flashPending = false; DeepSeaSuccessFlash.Play(detail); }
            var run = session.Run;

            if (string.IsNullOrEmpty(selectedId) || cands == null || !cands.Contains(selectedId))
            {
                var idle = Label(detail, T("shopSelectHint"), 30, soldOut ? 240 : 226, dw - 60, 80, DeepSeaTheme.BodySize);
                idle.alignment = TextAlignmentOptions.Center;
                idle.color = DeepSeaTheme.Muted;
                return;
            }

            var item = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == selectedId);
            int cash = run.runCash;
            bool showOptional = session.CanUseOptionalPurchaseDiscount();
            int quote = session.QuoteEquipmentPrice(selectedId, useOptionalPurchase);
            bool affordable = cash >= quote;

            Label(detail, selectedId + "   ·   " + T("priceOnly", quote), 30, 24, dw - 60, 56, DeepSeaTheme.ActionSize);
            var body = Label(detail, item != null ? item.Description : T("none"), 30, 88, dw - 60, 180, DeepSeaTheme.BodySize);
            body.alignment = TextAlignmentOptions.TopLeft;
            if (item != null && !PortfolioEffectStatus.IsEquipmentLive(item.EffectId))
                Label(detail, T("effectNotLive"), 30, 268, dw - 60, 42, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;

            if (showOptional)
            {
                string mark = useOptionalPurchase ? "☑" : "☐";
                Action(detail, "OptionalPurchase",
                    mark + "   " + T("optionalPurchase", session.OptionalPurchaseRemaining(), session.QuoteEquipmentPrice(selectedId, true)),
                    30, 316, dw - 60, 76, () => { useOptionalPurchase = !useOptionalPurchase; dirty = true; },
                    useOptionalPurchase ? DeepSeaTheme.ButtonRole.Primary : DeepSeaTheme.ButtonRole.Secondary, DeepSeaTheme.CaptionSize,
                    DeepSeaGraphic.Shape.Check);
            }

            float priceY = showOptional ? 402 : 340;
            var price = Label(detail, T("shopPriceLine", quote, cash, cash - quote), 30, priceY, dw - 60, 44, DeepSeaTheme.CaptionSize);
            if (!affordable)
            {
                price.color = DeepSeaTheme.Danger;
                price.text = T("shopShortBy", quote - cash) + "   ·   " + T("shopPriceLine", quote, cash, cash - quote);
            }

            float buyY = showOptional ? 452 : 388;
            var buy = Action(detail, "BuySelected", affordable ? T("shopBuy") : T("shopShortBy", quote - cash),
                30, buyY, dw - 60, 88, () => AskPurchase(selectedId),
                affordable ? DeepSeaTheme.ButtonRole.Primary : DeepSeaTheme.ButtonRole.Secondary, DeepSeaTheme.ActionSize,
                DeepSeaGraphic.Shape.Coin);
            buy.interactable = affordable;
        }

        private void RenderLeave(RectTransform page)
        {
            Action(page, "ShopLeaveButton", T("shopLeave"), 40, 1524, 920, 96, () => session.Execute(session.LeaveRoom),
                DeepSeaTheme.ButtonRole.Secondary, DeepSeaTheme.ActionSize, DeepSeaGraphic.Shape.Back);
            Label(page, T("shopLeaveNote"), 40, 1638, 920, 44, DeepSeaTheme.CaptionSize).color = DeepSeaTheme.Muted;
        }

        /// <summary>单按钮提示（购买失败）：不改变确认态，确定后清错并留在商店页</summary>
        private void RenderPrompt(RectTransform page)
        {
            var modal = Box(page, "PromptShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            var box = Box(modal, "Prompt", 200, 700, 600, 340, card);
            DeepSeaTheme.CardSurface(box, card);
            Label(box, promptText, 30, 30, 540, 180, DeepSeaTheme.BodySize);
            Action(box, "PromptOK", T("confirm"), 170, 240, 260, 80, () =>
            {
                promptText = null;
                session.ClearError();
                dirty = true;
            }, DeepSeaTheme.ButtonRole.Primary, DeepSeaTheme.ActionSize, DeepSeaGraphic.Shape.Info);
        }

        private void RenderConfirmation(RectTransform page)
        {
            bool buying = !string.IsNullOrEmpty(pendingBuyId);
            bool showOptional = buying && session.CanUseOptionalPurchaseDiscount();
            var modal = Box(page, "ConfirmationShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            float boxH = showOptional ? 620 : 540;
            var box = Box(modal, "Confirmation", 75, (1760 - boxH) * .5f, 850, boxH, card);
            DeepSeaTheme.CardSurface(box, card);

            string text = confirmText;
            if (buying && session.Run != null)
            {
                int quote = session.QuoteEquipmentPrice(pendingBuyId, useOptionalPurchase);
                int cash = session.Run.runCash;
                text = T("confirmPurchase", pendingBuyId, quote, cash, cash - quote);
            }
            Label(box, text, 35, 40, 780, showOptional ? 220 : 240, DeepSeaTheme.BodySize);

            if (showOptional)
            {
                string mark = useOptionalPurchase ? "☑" : "☐";
                Action(box, "OptionalPurchase",
                    mark + "   " + T("optionalPurchase", session.OptionalPurchaseRemaining(), session.QuoteEquipmentPrice(pendingBuyId, true)),
                    35, 280, 780, 80, () => { useOptionalPurchase = !useOptionalPurchase; dirty = true; },
                    useOptionalPurchase ? DeepSeaTheme.ButtonRole.Primary : DeepSeaTheme.ButtonRole.Secondary, DeepSeaTheme.CaptionSize,
                    DeepSeaGraphic.Shape.Check);
            }

            float btnY = showOptional ? 400 : 360;
            var confirmBtn = Action(box, "Confirm", T("confirm"), 35, btnY, 365, 90, () =>
            {
                if (buying)
                {
                    string id = pendingBuyId;
                    bool opt = useOptionalPurchase;
                    pendingBuyId = null;
                    confirmAction = null;
                    useOptionalPurchase = false;
                    session.SetConfirmation(false);
                    dirty = true;
                    session.PurchaseEquipment(id, opt);
                    if (session.Error != null)
                    {
                        promptText = T("shopFailed");
                        session.ClearError();
                    }
                    else
                    {
                        feedbackText = T("shopBought", id);
                        selectedId = null;
                        flashPending = true;   // FX003：事务成功后才播获得闪光
                    }
                    dirty = true;
                    return;
                }
                var action = confirmAction; confirmAction = null; session.SetConfirmation(false); dirty = true; action?.Invoke();
            }, DeepSeaTheme.ButtonRole.Primary, DeepSeaTheme.ActionSize, DeepSeaGraphic.Shape.Check);
            if (buying && session.Run != null)
            {
                int quote = session.QuoteEquipmentPrice(pendingBuyId, useOptionalPurchase);
                confirmBtn.interactable = session.Run.runCash >= quote;
            }
            Action(box, "Cancel", T("cancel"), 450, btnY, 365, 90, () =>
            {
                confirmAction = null;
                pendingBuyId = null;
                useOptionalPurchase = false;
                session.SetConfirmation(false);
                dirty = true;
            }, DeepSeaTheme.ButtonRole.Secondary, DeepSeaTheme.ActionSize, DeepSeaGraphic.Shape.Back);
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
        /// <summary>按钮：UI002/UI004 共用组件（角色 + 可选图标），字号按 UI001 覆盖。</summary>
        private Button Action(Transform parent, string name, string text, float x, float y, float w, float h,
            Action action, DeepSeaTheme.ButtonRole role, int size = DeepSeaTheme.ActionSize, DeepSeaGraphic.Shape? icon = null)
        {
            var button = DeepSeaTheme.Button(parent, name, text, x, y, w, h, role, font, () =>
            {
                AudioManager.Instance?.PlayButtonClick();
                action();
            }, icon);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.fontSize = size;
            return button;
        }
    }
}
