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

        private PortfolioSession session;
        private GameObject canvasObject;
        private RectTransform root;
        private TMP_FontAsset font;
        private bool dirty = true;
        private string confirmText;
        private Action confirmAction;
        private ScrollRect mapScroll;
        private string mapScrollContext;
        private float mapScrollY;
        private readonly Color background = new Color(.035f, .045f, .075f, 1);
        private readonly Color card = new Color(.075f, .095f, .14f, 1);
        private readonly Color ink = new Color(.89f, .93f, .98f, 1);
        private readonly Color accent = new Color(.12f, .48f, .60f, 1);
        private readonly Color owned = new Color(.10f, .37f, .28f, 1);

        private void OnEnable() { Subscribe(); }
        private void Start()
        {
            PortfolioText.Register();
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
        private Button Button(Transform parent, string name, string text, float x, float y, float w, float h, Action action, Color? color = null)
        {
            var rect = Box(parent, name, x, y, w, h, color ?? accent);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var label = Label(rect, text, 8, 5, w - 16, h - 10, 24);
            label.alignment = TextAlignmentOptions.Center;
            button.onClick.AddListener(() => session.Execute(action));
            return button;
        }
        private void Ask(string text, Action action)
        {
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
            var page = Box(shade, "GrowthPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;
            Label(page, T("mapTitle"), 20, 20, 960, 62, 36);
            Label(page, T("mapIntro"), 20, 92, 960, 50, 25);
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
            Label(page, T("balance", p.displayName, p.growthPoints), 20, 155, 620, 65, 32);
            // 右上角"天赋"入口：与存档选择面板同源（TalentPanel 自带 Canvas 排序 251，盖在地图页上）
            if (TalentPanel != null)
                Button(page, "OpenTalentsFromMap", T("talentsPage"), 700, 155, 280, 65, () => TalentPanel.Open(), card);
            // 返回主菜单：局进度保留（含 growth.Run 快照），下次经"选择存档"进入继续
            Button(page, "BackToMenuFromMap", T("back"), 720, 236, 260, 68, session.Close, card);
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
                var next = Button(page, "ContinueGrowth", T(run.phase == "MAP" ? "chooseRoom" : run.phase == "ROOM" ? "leaveRoom" : run.phase == "READY" ? "startSong" : "continue"), 20, 236, 300, 68,
                    run.phase == "RESULT" ? (Action)session.Continue : run.phase == "ROOM" ? session.LeaveRoom : session.StartSong);
                next.interactable = run.phase != "MAP";
                Button(page, "AbandonGrowth", T("abandon"), 350, 236, 310, 68, Abandon, card);
                Label(page, T("pending", run.completedSongs, run.stageIds.Count, run.earnedPoints), 20, 330, 960, 48, 27);
                Label(page, run.phase == "MAP" ? T("mapCash", run.runCash) : run.phase == "ROOM" ? T("room." + PortfolioConfig.MapNodes.Single(n => n.NodeId == run.currentNodeId).NodeType) : run.phase == "RESULT" ? T("songResult", run.lastSongPoints)
                    : T(session.FailureLimitEnabled ? "nextReward" : "nextRewardUnlimited", run.growthRewards[run.completedSongs], run.failureLimit), 20, 380, 960, 56, 24);
            }
            RenderMap(page);
        }

        private void RenderMap(RectTransform page)
        {
            var r = session.Run;
            const float viewportHeight = 1020;
            float contentHeight = 300 + PortfolioConfig.MapNodes.Max(n => n.LayoutY) * 360;
            var viewport = Box(page, "RunMap", 20, 470, 960, viewportHeight, card);
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
            Func<MapNodesRow, Vector2> position = n => new Vector2(60 + (n.LayoutX + 1) * 290, 100 + n.LayoutY * 360);
            foreach (var edge in PortfolioConfig.MapEdges)
            {
                var from = PortfolioConfig.MapNodes.Single(n => n.NodeId == edge.FromNodeId);
                var to = PortfolioConfig.MapNodes.Single(n => n.NodeId == edge.ToNodeId);
                Vector2 a = position(from) + new Vector2(120, 96), b = position(to) + new Vector2(120, 0);
                bool travelled = r != null && r.useMap && r.visitedNodeIds.Contains(from.NodeId) && r.visitedNodeIds.Contains(to.NodeId);
                var color = edge.RoutePrice > 0 ? new Color(.95f, .64f, .27f) : travelled ? new Color(.35f, .8f, .68f) : new Color(.49f, .57f, .65f);
                var line = Box(board, "MapEdge_" + edge.EdgeId, 0, 0, 960, contentHeight);
                var path = line.gameObject.AddComponent<PortfolioMapPathGraphic>();
                path.SetPath(new Vector2(a.x, -a.y), new Vector2(b.x, -b.y), color);
                if (edge.RoutePrice > 0)
                {
                    var price = Label(board, T("routeFee", edge.RoutePrice), (a.x+b.x)/2 + 22, (a.y+b.y)/2-18, 180, 50, 23);
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
                string label = T("node." + n.NodeType) + "  " + n.NodeId;
                if (current) label += "\n" + T("currentRoom");
                else if (visited) label += "\n" + T("mapVisited");
                else if (reachable) label += "\n" + T(r.runCash >= edge.RoutePrice ? "mapAvailable" : "cashShort");
                var button = Button(board, "Room_" + n.NodeId, label, pos.x, pos.y, 240, 96, () =>
                {
                    if (!reachable) return;
                    if (edge.RoutePrice > 0) Ask(T("confirmRoute", n.NodeId, edge.RoutePrice, r.runCash, r.runCash-edge.RoutePrice), () => session.EnterRoom(n.NodeId));
                    else session.EnterRoom(n.NodeId);
                }, current ? owned : reachable ? accent : new Color(.14f, .17f, .22f));
                button.interactable = reachable && r.runCash >= edge.RoutePrice;
            }
            string hint = r != null && !r.useMap && r.phase != "FINISHED" ? T("legacyRun") : T("mapLegend");
            Label(page, T("mapScrollHint"), 20, 1505, 610, 45, 24);
            Label(page, hint, 20, 1550, 960, 75, 22);
            Action focus = () =>
            {
                var node = PortfolioConfig.MapNodes.FirstOrDefault(n => r != null && r.useMap && n.NodeId == r.currentNodeId)
                    ?? PortfolioConfig.MapNodes.First(n => n.NodeType == "START");
                mapScroll.StopMovement();
                board.anchoredPosition = new Vector2(0, Mathf.Clamp(position(node).y - 180, 0, contentHeight - viewportHeight));
            };
            Button(page, "FocusCurrentRoom", T("mapFocus"), 660, 1500, 320, 55, focus, card);
            string context = session.Profile.profileId + "/" + (r == null ? "preview" : r.runId + "/" + r.currentNodeId);
            if (mapScrollContext != context) { mapScrollContext = context; focus(); }
            else board.anchoredPosition = new Vector2(0, Mathf.Clamp(mapScrollY, 0, contentHeight - viewportHeight));
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
            Button(box, "Cancel", T("cancel"), 450, 350, 365, 90, () => { confirmAction = null; session.SetConfirmation(false); dirty = true; }, background);
        }
    }
}
