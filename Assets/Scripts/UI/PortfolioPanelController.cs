using System;
using System.Linq;
using FallenAngel.Core;
using FallenAngel.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>运行时构建的成长验证界面。仅显示状态和转发操作，不计算成长奖励。</summary>
    public sealed class PortfolioPanelController : MonoBehaviour
    {
        /// <summary>天赋面板引用（GameStarter.Awake 注入；地图页角落"天赋"按钮打开它）</summary>
        public TalentPanelController TalentPanel { get; set; }

        /// <summary>装备背包面板引用（GameStarter.Awake 注入；地图页"装备 n/20"按钮打开它）</summary>
        public EquipmentPanelController EquipmentPanel { get; set; }

        /// <summary>商店弹窗引用（GameStarter.Awake 注入；地图页"商店"按钮重开它）</summary>
        public ShopPanelController ShopPanel { get; set; }

        private PortfolioSession session;
        private GameObject canvasObject;
        private RectTransform root;
        private TMP_FontAsset font;
        private bool dirty = true;
        private string confirmText;
        private string routeNodeId;
        private bool useOptionalRoute;
        private string scrollRunId;
        private Action confirmAction;
        private ScrollRect mapScroll;
        private string mapScrollContext;
        private float mapScrollY;
        private readonly Color background = DeepSeaTheme.Background;
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;
        private readonly Color accent = DeepSeaTheme.Accent;
        private readonly Color owned = DeepSeaTheme.Owned;

        private void OnEnable() { Subscribe(); }
        private void Start()
        {
            PortfolioText.Register();
            Loc.AddFallback("portfolio.mapCashClean", "现金  {0}", "Cash  {0}");
            Loc.AddFallback("portfolio.mapBalanceAfter", "余额  {0} → {1}", "Balance  {0} → {1}");
            Loc.AddFallback("portfolio.mapPayEnter", "支付 {0} 并进入", "Pay {0} and enter");
            Loc.AddFallback("portfolio.mapEnter", "进入", "Enter");
            Loc.AddFallback("portfolio.mapNoCash", "现金不足", "Insufficient cash");
            Loc.AddFallback("portfolio.mapEmpty", "空房", "Empty room");
            Loc.AddFallback("portfolio.mapChallenge", "挑战", "Challenge");
            Loc.AddFallback("portfolio.mapPerformance", "普通演奏", "Performance");
            Subscribe();
            font = GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.font).FirstOrDefault(f => f != null);
            if (font == null) font = TMP_Settings.defaultFontAsset;
            canvasObject = new GameObject("PortfolioCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // 独立根Canvas才会按自己的参考分辨率缩放；嵌套Canvas会继承原菜单的Rect。
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 250;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root = canvasObject.GetComponent<RectTransform>();
            dirty = true;
        }
        private void Subscribe()
        {
            session = GetComponent<PortfolioSession>();
            if (session != null) { session.OnChanged -= MarkDirty; session.OnChanged += MarkDirty; }
            Loc.OnLanguageChanged -= MarkDirty;
            Loc.OnLanguageChanged += MarkDirty;
        }
        private void OnDisable()
        {
            session?.SetConfirmation(false);
            if (session != null) session.OnChanged -= MarkDirty;
            Loc.OnLanguageChanged -= MarkDirty;
        }
        private void OnDestroy() { if (canvasObject != null) Destroy(canvasObject); }
        private void MarkDirty() { dirty = true; }
        private void LateUpdate()
        {
            if (!dirty || root == null || session == null) return;
            dirty = false;
            Render();
        }
        private string T(string key, params object[] values) => Loc.T("portfolio." + key, values);

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
        private Button Button(Transform parent, string name, string text, float x, float y, float w, float h, Action action, Color? color = null, DeepSeaGraphic.Shape? icon = null)
        {
            var rect = Box(parent, name, x, y, w, h, color ?? accent);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var label = Label(rect, text, 8, 5, w - 16, h - 10, 24);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(() => session.Execute(action));
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            if (icon.HasValue)
            {
                // UI005 图标与文案成组居中（宽按钮下不再出现图标贴左、文字居中的错位）
                DeepSeaTheme.Icon(rect, "ButtonIcon", icon.Value, 20, (h - 36) * .5f, 36, 36, ink);
                DeepSeaTheme.CenterIconGroup(rect, label, "ButtonIcon", 36f, 36f, 20f);
            }
            return button;
        }
        private void Ask(string text, Action action)
        {
            routeNodeId = null;
            session.SetConfirmation(true);
            confirmText = text; confirmAction = action; dirty = true;
        }
        private void Abandon() => Ask(T("confirmAbandon"), session.Abandon);

        private void Render()
        {
            if (mapScroll != null) mapScrollY = mapScroll.content.anchoredPosition.y;
            mapScroll = null;
            foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            var gm = GameManager.Instance;
            if (!session.IsOpen) return; // 正式入口在主菜单；不再渲染 OpenGrowth 入口按钮
            bool playing = session.OwnsSong && gm != null && gm.CurrentState == GameState.Playing;
            bool loading = session.OwnsSong && gm != null && gm.CurrentState == GameState.Loading;
            if ((playing || loading) && session.Error == null)
            {
                var hud = Box(root, "GrowthHUD", 0, 0, 1000, 58, card);
                hud.anchorMin = hud.anchorMax = new Vector2(.5f, 1); hud.pivot = new Vector2(.5f, 1);
                Label(hud, playing ? T(session.FailureLimitEnabled ? "playing" : "playingUnlimited", session.Run.completedSongs + 1, session.Run.stageIds.Count,
                    session.Failures, session.Run.failureLimit) : T("loading"), 20, 10, 960, 45, 24);
                return;
            }
            var shade = Box(root, "GrowthBackground", 0, 0, 0, 0, background);
            shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.sizeDelta = Vector2.zero;
            DeepSeaTheme.Backdrop(shade);
            var page = Box(shade, "GrowthPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;
            DeepSeaTheme.TitleRule(Label(page, T("mapTitle"), 20, 20, 960, 62, 36));
            // Map navigation is conveyed by paths and node states.
            if (session.Error != null)
            {
                Label(page, T("error"), 30, 250, 940, 150, 32);
                Button(page, "RetryOperation", T("retry"), 30, 450, 400, 72, session.RetryOperation);
                if (!session.OwnsSong) Button(page, "ReturnAfterError", T("back"), 30, 550, 400, 72, session.DismissError, card);
                // 详细异常留在Console，避免向玩家泄露路径和实现细节。
                return;
            }
            if (session.Profile == null) return; // 正式流程不会出现（入口经新游戏/存档选择）
            if (session.OwnsSong && gm != null && gm.CurrentState == GameState.Paused)
            {
                Label(page, T("paused"), 40, 400, 900, 80, 44);
                Label(page, T("pauseNote"), 40, 510, 900, 120, 28);
                Button(page, "ResumePerformance", T("resume"), 40, 690, 430, 82, session.Resume);
                Button(page, "AbandonPerformance", T("abandon"), 520, 690, 430, 82, Abandon, card);
            }
            else RenderProfile(page);
            if (confirmAction != null) RenderConfirmation(page);
        }

        private void RenderProfile(RectTransform page)
        {
            var p = session.Profile; var run = session.Run;
            if (run != null && run.useMap && (run.phase == "MAP" || run.phase == "ROOM" || run.phase == "READY"))
            {
                DeepSeaTheme.CashBar(page, 700, 18, run.runCash, font).text = T("mapCashClean", run.runCash);
                RenderMap(page);
                if (EquipmentPanel != null) Button(page, "OpenEquipmentPanel", T("equipment", run.heldEquipmentIds.Count, session.EquipmentCapacity), 20, 1570, 465, 90, () => EquipmentPanel.Open(), card, DeepSeaGraphic.Shape.Equipment);
                if (TalentPanel != null) Button(page, "OpenTalentsFromMap", T("talentsPage"), 515, 1570, 465, 90, () => TalentPanel.Open(), card, DeepSeaGraphic.Shape.Talent);
                Button(page, "BackToMenuFromMap", T("back"), 20, 1690, 180, 60, session.Close, card);
                if (run.phase == "ROOM") Button(page, "LeaveMapRoom", T("leaveRoom"), 730, 1690, 250, 60, session.LeaveRoom, card);
                return;
            }
            Label(page, T("balance", p.displayName, p.growthPoints), 20, 155, 620, 65, 32);
            // 装备持有计数（按钮：点击打开背包审阅；局内资源，局终清空）
            if (EquipmentPanel != null)
                Button(page, "OpenEquipmentPanel", T("equipment", run != null ? run.heldEquipmentIds.Count : 0, session.EquipmentCapacity),
                    20, 1640, 300, 65, () => EquipmentPanel.Open(), card, DeepSeaGraphic.Shape.Equipment);
            // 右上角"天赋"入口：与存档选择面板同源（TalentPanel 自带 Canvas 排序 251，盖在地图页上）
            if (TalentPanel != null)
                Button(page, "OpenTalentsFromMap", T("talentsPage"), 350, 1640, 300, 65, () => TalentPanel.Open(), card, DeepSeaGraphic.Shape.Talent);
            // 返回主菜单：局进度保留（含 growth.Run 快照），下次经"选择存档"进入继续
            Button(page, "BackToMenuFromMap", T("back"), session.InShop ? 830 : 720, 236, session.InShop ? 150 : 260, 68, session.Close, card);
            if (run == null || run.phase == "FINISHED")
            {
                Button(page, "BeginGrowthRun", T("begin"), 20, 236, 640, 68, session.Begin);
                if (run != null)
                {
                    Label(page, T(run.outcome), 20, 330, 960, 45, 28);
                    Label(page, T("credited", run.earnedPoints, run.creditedPoints - run.earnedPoints, run.creditedPoints), 20, 380, 960, 55, 25);
                }
            }
            else
            {
                // 商店房间：主操作行额外放"商店"重开按钮（弹窗由 ShopPanelController 管理）
                bool inShop = session.InShop;
                var next = Button(page, "ContinueGrowth", T(run.phase == "MAP" ? "chooseRoom" : run.phase == "ROOM" ? "leaveRoom" : run.phase == "READY" ? "startSong" : "continue"), 20, 236, inShop ? 250 : 300, 68,
                    run.phase == "RESULT" ? (Action)session.Continue : run.phase == "ROOM" ? session.LeaveRoom : session.StartSong);
                next.interactable = run.phase != "MAP";
                if (inShop && ShopPanel != null)
                    Button(page, "OpenShopPanel", T("shopOpen"), 290, 236, 250, 68, () => ShopPanel.Open(), card);
                Button(page, "AbandonGrowth", T("abandon"), inShop ? 560 : 350, 236, inShop ? 250 : 310, 68, Abandon, card);
                Label(page, T("pending", run.completedSongs, run.stageIds.Count, run.earnedPoints), 20, 330, 960, 48, 27);
                Label(page, run.phase == "MAP" ? T("mapCash", run.runCash) : run.phase == "ROOM" ? T("room." + PortfolioConfig.MapNodes.Single(n => n.NodeId == run.currentNodeId).NodeType) : run.phase == "RESULT" ? T("songResult", run.lastSongPoints)
                    : T(session.FailureLimitEnabled ? "nextReward" : "nextRewardUnlimited", run.growthRewards[run.completedSongs], run.failureLimit), 20, 380, 960, 56, 24);
            }
            // 结算详情：RESULT 阶段隐藏地图，展示收益明细（2 秒后自动继续回地图）；
            // 无效果时也显示合计（收入永远有基础部分）
            if (run != null && run.phase == "RESULT")
            {
                var cardBox = Box(page, "IncomeBreakdown", 20, 440, 960, 430, card);
                DeepSeaTheme.CardSurface(cardBox, card);
                Label(cardBox, T("income.title"), 25, 20, 910, 50, 30);
                float ly = 90;
                foreach (var l in run.incomeBreakdown)
                {
                    Label(cardBox, Loc.T("portfolio." + l.key), 40, ly, 600, 40, 24);
                    Label(cardBox, "+" + l.amount.ToString("0.#"), 700, ly, 240, 40, 24);
                    ly += 42;
                }
                Label(cardBox, T("income.total", run.lastCashReward), 40, ly + 10, 910, 50, 28);
                // 关卡掉落结果与收益同屏展示（掉落现金/装备在本次结算后入账，不参与收益倍率/保底/封顶）
                if (run.lastDropGranted)
                {
                    var dropBox = Box(page, "DropGranted", 20, 890, 960, 84, card);
                    DeepSeaTheme.CardSurface(dropBox, card);
                    string dropText = run.lastDropRewardType == "CURRENCY"
                        ? T("drop.cash", run.lastDropQuantity)
                        : T("drop.equipment", run.lastDropRewardId);
                    Label(dropBox, dropText, 25, 22, 910, 44, 26);
                }
            }
            else RenderMap(page);
        }

        private void RenderMap(RectTransform page)
        {
            var r = session.Run;
            const float viewportHeight = 1400;
            float contentHeight = 380 + PortfolioConfig.MapNodes.Max(n => n.LayoutY) * 440;
            // 上下文含 phase：回到 MAP（战斗结算/离开房间）即视为"新抵达"，触发路线延展动画与节点点亮
            string context = session.Profile.profileId + "/" + (r == null ? "preview" : r.runId + "/" + r.currentNodeId + "/" + r.phase);
            bool freshArrival = r != null && r.useMap && r.phase == "MAP" && mapScrollContext != context;
            var viewport = Box(page, "RunMap", 20, 125, 960, viewportHeight, new Color(.025f,.07f,.095f,.65f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var board = Box(viewport, "MapContent", 0, 0, 960, contentHeight);
            mapScroll = viewport.gameObject.AddComponent<ScrollRect>();
            mapScroll.viewport = viewport;
            mapScroll.content = board;
            mapScroll.horizontal = false;
            mapScroll.vertical = true;
            mapScroll.movementType = ScrollRect.MovementType.Clamped;
            mapScroll.scrollSensitivity = 65;
            mapScroll.decelerationRate = .12f;
            Func<MapNodesRow, Vector2> position = n => new Vector2(60 + (n.LayoutX + 1) * 290, 120 + n.LayoutY * 440);
            foreach (var edge in PortfolioConfig.MapEdges)
            {
                var from = PortfolioConfig.MapNodes.Single(n => n.NodeId == edge.FromNodeId);
                var to = PortfolioConfig.MapNodes.Single(n => n.NodeId == edge.ToNodeId);
                Vector2 a = position(from) + new Vector2(120, 200), b = position(to) + new Vector2(120, 0);
                bool travelled = r != null && r.useMap && r.visitedNodeIds.Contains(from.NodeId) && r.visitedNodeIds.Contains(to.NodeId);
                bool newlyReachable = freshArrival && r != null && r.useMap && edge.FromNodeId == r.currentNodeId && !travelled;
                bool availablePath = r != null && r.useMap && edge.FromNodeId == r.currentNodeId;
                var color = availablePath ? (edge.RoutePrice > 0 ? new Color(.85f, .71f, .5f) : ink) : new Color(.28f, .36f, .4f);
                var line = Box(board, "MapEdge_" + edge.EdgeId, 0, 0, 960, contentHeight);
                var path = line.gameObject.AddComponent<PortfolioMapPathGraphic>();
                // 新抵达时，从当前房间向下一步房间的连线做延展动画（箭头在画完时出现）
                if (newlyReachable) path.AnimatePath(new Vector2(a.x, -a.y), new Vector2(b.x, -b.y), color, 0.45f);
                else path.SetPath(new Vector2(a.x, -a.y), new Vector2(b.x, -b.y), color);
                if (edge.RoutePrice > 0)
                {
                    int fee = r != null && r.useMap ? session.QuoteRouteFee(edge.RoutePrice, false) : edge.RoutePrice;
                    var price = Label(board, T("routeFee", fee), (a.x+b.x)/2 + 22, (a.y+b.y)/2-18, 180, 50, 23);
                    price.color = color;
                }
            }
            foreach (var n in PortfolioConfig.MapNodes)
            {
                var pos = position(n);
                var edge = r != null && r.useMap ? PortfolioConfig.MapEdges.FirstOrDefault(e => e.FromNodeId == r.currentNodeId && e.ToNodeId == n.NodeId) : null;
                bool visited = r != null && r.useMap && r.visitedNodeIds.Contains(n.NodeId);
                bool reachable = edge != null && r.phase == "MAP" && !visited;
                bool current = r != null && r.useMap && r.currentNodeId == n.NodeId;
                // 当前房间可直接行动：READY 点击开曲 / 商店点击开商店 / 空房点击离开
                bool actionable = current && r != null && r.useMap && r.phase != "MAP";
                // Current position remains distinct after the room event is complete.
                bool paidNode = PortfolioConfig.MapEdges.Any(e => e.ToNodeId == n.NodeId && e.RoutePrice > 0);
                string label = n.NodeType == "EMPTY" ? T("mapEmpty") : n.NodeType == "STAGE" ? T(paidNode ? "mapChallenge" : "mapPerformance") : T("node." + n.NodeType);
                var button = Button(board, "Room_" + n.NodeId, label, pos.x, pos.y, 240, 150, () =>
                {
                    if (reachable)
                    {
                        useOptionalRoute = false;
                        Ask(null, () => session.EnterRoom(n.NodeId, useOptionalRoute));
                        routeNodeId = n.NodeId;
                        return;
                    }
                    if (current && r != null && r.useMap)
                    {
                        if (r.phase == "READY") session.StartSong();
                        else if (r.phase == "ROOM")
                        {
                            if (n.NodeType == "SHOP" && ShopPanel != null) ShopPanel.Open();
                            else if (n.NodeType == "EMPTY") session.LeaveRoom();
                        }
                    }
                }, current ? new Color(.13f,.2f,.25f) : card);
                button.interactable = reachable || actionable;
                // Current position stays visually prominent even after its room event is consumed.
                var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
                var tint = current ? ink : reachable ? (edge.RoutePrice > 0 ? new Color(.85f,.71f,.5f) : ink) : new Color(.36f,.44f,.49f);
                DeepSeaTheme.RoomIcon(button, n.NodeType);
                var icon = button.transform.Find("RoomSymbol").GetComponent<DeepSeaGraphic>();
                var iconRect = (RectTransform)icon.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f,.5f);
                iconRect.pivot = new Vector2(.5f,.5f); iconRect.anchoredPosition = Vector2.zero; iconRect.sizeDelta = new Vector2(82,82); icon.color = tint;
                button.GetComponentInChildren<TextMeshProUGUI>().text = "";
                var nodeFrame = button.transform.Find("SeaFrame").GetComponent<DeepSeaGraphic>();
                nodeFrame.color = tint; nodeFrame.variant = current ? 1 : 0;
                var strip = Box(button.transform, "NodeTitle", 0, 160, 240, 48, current ? ink : background);
                strip.GetComponent<Image>().raycastTarget = false;
                var title = Label(strip, label, 5, 4, 230, 40, 25); title.alignment = TextAlignmentOptions.Center; title.color = current ? background : tint;
                if (current)
                {
                    var marker = Box(board, "CurrentPosition", pos.x + 103, pos.y - 45, 34, 25);
                    DeepSeaTheme.Graphic(marker, "CurrentBeacon", DeepSeaGraphic.Shape.Position).color = new Color(.6f,.8f,.83f);
                    marker.gameObject.AddComponent<DeepSeaBeaconPulse>();
                }
                else if (visited)
                {
                    var passed = Box(button.transform, "Visited", 208, 8, 22, 26);
                    DeepSeaTheme.Graphic(passed, "Check", DeepSeaGraphic.Shape.Check).color = tint;
                }
                else if (!reachable && !visited)
                {
                    var locked = Box(button.transform, "Locked", 208, 8, 22, 26);
                    DeepSeaTheme.Graphic(locked, "Lock", DeepSeaGraphic.Shape.Lock).color = tint;
                }
            }
            string runKey = session.Profile.profileId + "/" + (r == null ? "preview" : r.runId);
            if (scrollRunId != runKey) { scrollRunId = runKey; mapScrollY = 0; }
            mapScrollContext = context;
            board.anchoredPosition = new Vector2(0, Mathf.Clamp(mapScrollY, 0, contentHeight - viewportHeight));
        }

        /// <summary>节点点亮脉动：新抵达时下一步房间缩放呼吸一次（重绘销毁时协程随之终止）</summary>
        private System.Collections.IEnumerator PulseNode(GameObject go)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            const float duration = 0.5f;
            float t = 0f;
            while (t < duration && rt != null)
            {
                t += Time.unscaledDeltaTime;
                float s = 1f + 0.12f * Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        /// <summary>节点 → 关卡 → 谱面资源名（非关卡节点返回 null）</summary>
        private static string ChartNameForNode(MapNodesRow n)
        {
            if (string.IsNullOrEmpty(n.StageId)) return null;
            var stage = PortfolioConfig.Stages.FirstOrDefault(s => s.StageId == n.StageId);
            if (stage == null) return null;
            var binding = PortfolioConfig.ChartBindings.FirstOrDefault(b => b.ChartId == stage.ChartId);
            return binding == null ? stage.ChartId : binding.ResourceName;
        }

        private void RenderRouteDetails(RectTransform page)
        {
            var run = session.Run;
            var node = PortfolioConfig.MapNodes.FirstOrDefault(n => n.NodeId == routeNodeId);
            var edge = PortfolioConfig.MapEdges.FirstOrDefault(e => e.FromNodeId == run.currentNodeId && e.ToNodeId == routeNodeId);
            if (node == null || edge == null) { confirmAction = null; routeNodeId = null; useOptionalRoute = false; session.SetConfirmation(false); return; }
            var shade = Box(page, "RouteShade", 0, 0, 0, 0, new Color(0,0,0,.68f));
            shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.offsetMin = new Vector2(-500,-500); shade.offsetMax = new Vector2(500,500);
            var drawer = Box(page, "RouteDetails", 0, 1030, 1000, 730, new Color(.094f,.16f,.204f,1));
            var drawerGroup = drawer.gameObject.AddComponent<CanvasGroup>();
            Action close = () => { confirmAction = null; routeNodeId = null; useOptionalRoute = false; session.SetConfirmation(false); dirty = true; };
            var closeButton = Button(drawer, "CloseRoute", "", 900, 15, 80, 70, close, card);
            Action dismiss = () => { if (!drawerGroup.interactable) return; drawerGroup.interactable = false; StartCoroutine(CloseDrawer(drawer, close)); };
            closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(() => dismiss());
            DeepSeaTheme.Graphic(closeButton.transform, "CloseIcon", DeepSeaGraphic.Shape.Close).color = ink;
            Label(drawer, node.NodeType == "STAGE" ? T(edge.RoutePrice > 0 ? "mapChallenge" : "mapPerformance") : T("node." + node.NodeType), 40, 35, 790, 45, 26);
            Label(drawer, ChartNameForNode(node) ?? T("node." + node.NodeType), 40, 110, 900, 120, 42);
            int quote = session.QuoteRouteFee(edge.RoutePrice, useOptionalRoute);
            Label(drawer, T("routeFee", quote), 40, 250, 900, 50, 30);
            Label(drawer, T("mapBalanceAfter", run.runCash, run.runCash - quote), 40, 310, 900, 50, 30);

            bool showOptional = edge.RoutePrice > 0 && session.CanUseOptionalRouteDiscount();
            if (showOptional)
            {
                int remain = session.OptionalRouteRemaining();
                int withOpt = session.QuoteRouteFee(edge.RoutePrice, true);
                string mark = useOptionalRoute ? "☑" : "☐";
                Button(drawer, "OptionalRoute", mark + "  " + T("optionalRoute", remain, withOpt), 40, 370, 920, 70, () =>
                {
                    useOptionalRoute = !useOptionalRoute;
                    dirty = true;
                }, card);
            }

            float submitY = showOptional ? 460 : 430;
            var submit = Button(drawer, "ConfirmRoute", run.runCash < quote ? T("mapNoCash") : quote > 0 ? T("mapPayEnter", quote) : T("mapEnter"), 40, submitY, 920, 100, () =>
            {
                string nodeId = routeNodeId;
                bool opt = useOptionalRoute;
                confirmAction = null; routeNodeId = null; useOptionalRoute = false;
                session.SetConfirmation(false); dirty = true;
                if (!string.IsNullOrEmpty(nodeId)) session.EnterRoom(nodeId, opt);
            }, ink);
            submit.GetComponentInChildren<TextMeshProUGUI>().color = background;
            submit.interactable = run.runCash >= quote;
            var cancel = Button(drawer, "CancelRoute", T("cancel"), 340, showOptional ? 580 : 620, 320, 70, close, card);
            cancel.onClick.RemoveAllListeners(); cancel.onClick.AddListener(() => dismiss());
            StartCoroutine(SlideDrawer(drawer));
        }
        private System.Collections.IEnumerator CloseDrawer(RectTransform drawer, Action closed)
        {
            var start = drawer.anchoredPosition;
            float time = 0;
            while (drawer != null && time < .18f)
            {
                time += Time.unscaledDeltaTime;
                drawer.anchoredPosition = start + Vector2.down * Mathf.Clamp01(time / .18f) * 730;
                yield return null;
            }
            closed();
        }
        private System.Collections.IEnumerator SlideDrawer(RectTransform drawer)
        {
            var end = drawer.anchoredPosition;
            float time = 0;
            while (drawer != null && drawer.GetComponent<CanvasGroup>().interactable && time < .22f)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / .22f);
                drawer.anchoredPosition = end + Vector2.down * (1-t)*(1-t)*180;
                yield return null;
            }
            if (drawer != null && drawer.GetComponent<CanvasGroup>().interactable) drawer.anchoredPosition = end;
        }

        private void RenderConfirmation(RectTransform page)
        {
            if (routeNodeId != null) { RenderRouteDetails(page); return; }
            var modal = Box(page, "ConfirmationShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            var box = Box(modal, "Confirmation", 75, 530, 850, 500, card);
            DeepSeaTheme.CardSurface(box, card);
            Label(box, confirmText, 35, 40, 780, 260, 32);
            Button(box, "Confirm", T("confirm"), 35, 350, 365, 90, () =>
            {
                var action = confirmAction; confirmAction = null; session.SetConfirmation(false); dirty = true; action?.Invoke();
            });
            Button(box, "Cancel", T("cancel"), 450, 350, 365, 90, () => { confirmAction = null; session.SetConfirmation(false); dirty = true; }, background);
        }
    }
}
