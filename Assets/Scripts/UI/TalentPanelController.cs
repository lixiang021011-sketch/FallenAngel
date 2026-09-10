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
    /// 天赋面板（正式）：树/详情/解锁二次确认渲染自 PortfolioPanelController.RenderTalents 平移而来，
    /// 右上角 X 关闭（替代原"返回旅程地图"按钮）。挂 TalentPanel（Canvas 根，初始非激活）；
    /// 入口由 SaveSelectPanelController 的角落"天赋"按钮打开。
    /// session 在运行时由 GameStarter.Awake 添加，本组件首次激活时查找（同 SaveSelectPanelController）。
    /// </summary>
    public sealed class TalentPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform contentRoot; // 内容容器（SceneBuilder 建的空全屏容器，Render 重建其子树）
        [SerializeField] private Button closeButton;       // 右上角 X

        private PortfolioSession session;
        private TMP_FontAsset font;
        private bool dirty = true;
        private string selected;
        private string confirmText;
        private Action confirmAction;
        private ScrollRect talentScroll;
        private float talentScrollY;
        private string talentScrollContext;
        private readonly Color card = DeepSeaTheme.Card;
        private readonly Color ink = DeepSeaTheme.Ink;
        private readonly Color accent = DeepSeaTheme.Accent;
        private readonly Color owned = DeepSeaTheme.Owned;

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

        /// <summary>打开天赋面板（SaveSelect 角落按钮；需已选中存档）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            if (session == null || session.Profile == null)
            {
                Debug.LogWarning("[TalentPanelController] 未选择存档，无法打开天赋面板");
                return;
            }
            if (panelRoot != null) panelRoot.SetActive(true);
            selected = null;
            dirty = true;
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
            confirmAction = null;
            session?.SetConfirmation(false);
        }

        // ---- 渲染辅助（平移自 PortfolioPanelController） ----
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
            DeepSeaTheme.StyleButton(button);
            DeepSeaTheme.RefineButton(button);
            return button;
        }
        private void Ask(string text, Action action)
        {
            session.SetConfirmation(true);
            confirmText = text; confirmAction = action; dirty = true;
        }

        private void Render()
        {
            if (contentRoot == null || session.Profile == null) return;
            // 重建前保存滚动位置（点选节点会触发重绘，滚动不能跳回顶部）
            if (talentScroll != null) talentScrollY = talentScroll.content.anchoredPosition.y;
            talentScroll = null;
            foreach (Transform child in contentRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }

            var p = session.Profile; var run = session.Run;
            var page = Box(contentRoot, "TalentPage", 0, 0, 1000, 1760);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;

            DeepSeaTheme.IconLabel(page, "TalentBalance", DeepSeaGraphic.Shape.Growth,
                T("balance", p.displayName, p.growthPoints), 20, 155, 960, 65, 32, font, DeepSeaTheme.Accent);

            // 树区：上下滚动（同地图路线图模式——ScrollRect + RectMask2D + 内容板）
            const float viewportHeight = 900f;
            const float contentHeight = 1000f; // 10 行 × 97 + 顶部/节点余量
            var viewport = Box(page, "TalentViewport", 20, 470, 610, viewportHeight, card);
            viewport.gameObject.AddComponent<RectMask2D>();
            var board = Box(viewport, "TalentBoard", 0, 0, 610, contentHeight);
            talentScroll = viewport.gameObject.AddComponent<ScrollRect>();
            talentScroll.viewport = viewport;
            talentScroll.content = board;
            talentScroll.horizontal = false;
            talentScroll.vertical = true;
            talentScroll.movementType = ScrollRect.MovementType.Clamped;
            talentScroll.scrollSensitivity = 65;
            talentScroll.decelerationRate = .12f;

            foreach (var edge in PortfolioConfig.TalentEdges)
            {
                Vector2 a = NodePosition(edge.FromNodeId) + new Vector2(90, 70);
                Vector2 b = NodePosition(edge.ToNodeId) + new Vector2(90, 0);
                var line = Box(board, "Edge", a.x, a.y, Vector2.Distance(a, b), 3, new Color(.24f, .33f, .4f));
                line.pivot = new Vector2(0, .5f);
                line.localEulerAngles = new Vector3(0, 0, -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                line.GetComponent<Image>().raycastTarget = false;
            }
            foreach (var node in PortfolioConfig.TalentNodes)
            {
                var pos = NodePosition(node.NodeId);
                bool unlocked = p.unlockedNodeIds.Contains(node.NodeId);
                bool available = session.Talents.CheckUnlock(p, node.NodeId) == TalentUnlockStatus.Available;
                string name = Loc.CurrentLanguage == Language.Chinese ? node.Name : node.NodeId;
                var button = Button(board, "Talent_" + node.NodeId, node.NodeId + "  " + name + "\n" +
                    (unlocked ? T("unlocked") : node.UnlockCost.ToString()), pos.x, pos.y, 180, 70,
                    () => { selected = node.NodeId; dirty = true; }, unlocked ? owned : available ? accent : new Color(.13f, .16f, .21f));
                button.GetComponentInChildren<TextMeshProUGUI>().fontSize = 20;
                // TAL001：三类节点框（普通/交汇/终点，共用 3 个框类不为 24 个节点重复画框）+ 状态标记
                var nodeFrame = button.transform.Find("SeaFrame");
                var nodeFrameGraphic = nodeFrame != null ? nodeFrame.GetComponent<DeepSeaGraphic>() : null;
                if (nodeFrameGraphic != null)
                {
                    nodeFrameGraphic.variant = node.NodeType == "CAPSTONE" ? 2 : node.NodeType == "JUNCTION" ? 1 : 0;
                    nodeFrameGraphic.SetVerticesDirty();
                }
                var nodeState = unlocked ? DeepSeaTheme.CardState.Cleared
                    : available ? DeepSeaTheme.CardState.Normal
                    : DeepSeaTheme.CardState.Locked;
                if (selected == node.NodeId) nodeState = DeepSeaTheme.CardState.Selected;
                DeepSeaTheme.ApplyCardState(button.transform, nodeState);
            }
            // 切档案时滚动归零，否则恢复保存的位置
            string context = p.profileId;
            if (talentScrollContext != context) { talentScrollContext = context; board.anchoredPosition = Vector2.zero; }
            else board.anchoredPosition = new Vector2(0, Mathf.Clamp(talentScrollY, 0, contentHeight - viewportHeight));

            var details = Box(page, "TalentDetails", 655, 470, 325, viewportHeight, card);
            DeepSeaTheme.CardSurface(details, card);
            var n = PortfolioConfig.TalentNodes.FirstOrDefault(node => node.NodeId == selected);
            if (n == null) Label(details, T("detail"), 20, 25, 285, 400, 26);
            else
            {
                Label(details, n.NodeId + "\n" + n.Name, 20, 25, 285, 110, 30);
                string pre = string.Join(" / ", PortfolioConfig.TalentEdges.Where(e => e.ToNodeId == n.NodeId).Select(e => e.FromNodeId));
                Label(details, T("cost", n.UnlockCost, pre.Length == 0 ? T("none") : pre), 20, 160, 285, 150, 26);
                var effect = PortfolioConfig.TalentEffects.FirstOrDefault(e => e.EffectId == n.EffectId);
                string description = effect == null ? T("none") : T("effect." + effect.EffectId,
                    effect.Coefficient.ToString("0.##%"), (effect.Threshold ?? 0).ToString("0.##%"),
                    (effect.ValueCapB ?? 0).ToString("0.##%"), effect.LimitCount ?? 0,
                    effect.Coefficient.ToString("0.##"), (effect.Threshold ?? 0).ToString("0.##"));
                // TAL002：效果语义符号（奖励/增幅/保底/利息/商店/路线/升级）与说明同块显示
                DeepSeaTheme.Icon(details, "EffectSymbol", EffectSymbol(effect), 20, 302, 34, 34, DeepSeaTheme.Accent);
                Label(details, T("effect", description), 62, 300, 243, 270, 24);
                if (effect != null && !PortfolioEffectStatus.IsTalentLive(effect.EffectId))
                    Label(details, T("effectNotLive"), 20, 575, 285, 90, 22);
                var status = session.Talents.CheckUnlock(p, n.NodeId);
                Label(details, T("status." + status), 20, 660, 285, 100, 27);
                Button(details, "UnlockTalent", T("unlock"), 20, 770, 285, 78, () =>
                {
                    int revision = p.revision;
                    Ask(T("confirmUnlock", n.NodeId, n.UnlockCost, p.growthPoints, p.growthPoints - n.UnlockCost),
                        () => session.Unlock(n.NodeId, revision));
                }).interactable = status == TalentUnlockStatus.Available;
            }
            if (run != null && (run.phase == "RESULT" || run.phase == "FINISHED"))
                Label(page, T("score", run.lastScore, run.lastAccuracy), 20, 1480, 960, 48, 24);
            Label(page, T("bankNote"), 20, 1550, 960, 70, 25);
            if (confirmAction != null) RenderConfirmation(page);
        }

        /// <summary>天赋树节点坐标（平移自 PortfolioPanelController.NodePosition）</summary>
        private static Vector2 NodePosition(string id)
        {
            string[][] rows = { new[] { "A0", "B0", "C0" }, new[] { "A1", "B1", "C1" }, new[] { "H1" },
                new[] { "D0", "E0", "F0" }, new[] { "D1", "E1", "F1" }, new[] { "H2" },
                new[] { "G0", "I0", "J0" }, new[] { "G1", "I1", "J1" }, new[] { "H3" }, new[] { "K0", "K1", "K2" } };
            for (int y = 0; y < rows.Length; y++)
            {
                int x = Array.IndexOf(rows[y], id);
                if (x >= 0) return new Vector2(10 + (rows[y].Length == 1 ? 1 : x) * 200, 20 + y * 97);
            }
            throw new InvalidOperationException("Talent layout missing: " + id);
        }

        /// <summary>TAL002：效果语义符号——奖励/增幅/保底/利息/商店/路线/升级；升级由数据 target_effect 判定。</summary>
        private static DeepSeaGraphic.Shape EffectSymbol(TalentEffectsRow effect)
        {
            if (effect == null) return DeepSeaGraphic.Shape.Info;
            if (effect.TargetScope == "TARGET_EFFECT") return DeepSeaGraphic.Shape.Upgrade;
            switch (effect.EffectId)
            {
                case "FX_A0": case "FX_A1": case "FX_B0": case "FX_B1": case "FX_C0":
                case "FX_I0": case "FX_I1": case "FX_J0": case "FX_J1":
                    return DeepSeaGraphic.Shape.Reward;
                case "FX_D0": case "FX_D1": case "FX_K0": case "FX_K1":
                    return DeepSeaGraphic.Shape.Amplify;
                case "FX_G0":
                    return DeepSeaGraphic.Shape.Floor;
                case "FX_C1": case "FX_K2":
                    return DeepSeaGraphic.Shape.Coin;
                case "FX_E0": case "FX_E1":
                    return DeepSeaGraphic.Shape.Shop;
                case "FX_F0":
                    return DeepSeaGraphic.Shape.Position;
                default:
                    return DeepSeaGraphic.Shape.Info;
            }
        }

        private void RenderConfirmation(RectTransform page)
        {
            var modal = Box(page, "ConfirmationShade", 0, 0, 1000, 1760, new Color(0, 0, 0, .88f));
            var box = Box(modal, "Confirmation", 75, 530, 850, 500, card);
            DeepSeaTheme.CardSurface(box, card);
            Label(box, confirmText, 35, 40, 780, 260, 32);
            Button(box, "Confirm", T("confirm"), 35, 350, 365, 90, () =>
            {
                var action = confirmAction; confirmAction = null; session.SetConfirmation(false); dirty = true; action?.Invoke();
            }, DeepSeaTheme.Danger);   // UI007：永久消耗不可退 → 危险确认（暗红，不与购买共用白底）
            Button(box, "Cancel", T("cancel"), 450, 350, 365, 90, () => { confirmAction = null; session.SetConfirmation(false); dirty = true; }, card);
        }
    }
}
