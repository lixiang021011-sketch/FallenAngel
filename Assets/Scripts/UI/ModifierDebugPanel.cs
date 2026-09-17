#if UNITY_EDITOR
using System.Text;
using FallenAngel.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 局内 modifier 调试窗口（仅编辑器）。
    ///
    /// 把调试入口从 Inspector 齿轮菜单搬进游戏界面：演奏中直接施加/清除效果，
    /// 并实时列出"生效中"的效果与剩余时间——验收 modifier 时不必离开游戏。
    ///
    /// 与 GameStarter 的验收悬浮条同一约定：独立嵌套 Canvas（排序 301），只在 UNITY_EDITOR 下编译，
    /// 不进正式包；本身不含任何玩法逻辑，只调用 ModifierManager 的公开接口。
    /// </summary>
    public class ModifierDebugPanel : MonoBehaviour
    {
        private const float PanelX = 818f;      // 右侧，避开左上的分数与右上的暂停/设置
        private const float PanelY = 232f;
        private const float PanelW = 250f;
        private const float RowH = 48f;
        private const float RowGap = 8f;

        private TMP_FontAsset font;
        private GameObject rowsRoot;
        private TextMeshProUGUI status;
        private Button toggle;
        private bool visibilityChecked;
        private bool visibilityReady;

        /// <summary>在指定宿主下安装调试窗口（已存在则直接返回）</summary>
        public static ModifierDebugPanel Install(Transform host, TMP_FontAsset font)
        {
            if (host == null) return null;
            ModifierDebugPanel existing = host.GetComponentInChildren<ModifierDebugPanel>(true);
            if (existing != null) return existing;

            GameObject go = new GameObject("ModifierDebugPanel", typeof(RectTransform));
            go.transform.SetParent(host, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 301;   // 盖在验收悬浮条（300）之上
            go.AddComponent<GraphicRaycaster>();

            ModifierDebugPanel panel = go.AddComponent<ModifierDebugPanel>();
            panel.font = font;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            // 折叠按钮自己占一行，否则会被第一行盖住（点不到）
            toggle = MakeButton(transform, "MOD 调试 ▾", PanelX, -PanelY, ToggleRows);

            rowsRoot = new GameObject("Rows", typeof(RectTransform));
            rowsRoot.transform.SetParent(transform, false);
            RectTransform rrt = (RectTransform)rowsRoot.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 1f);
            rrt.anchoredPosition = new Vector2(PanelX, -(PanelY));
            rrt.sizeDelta = Vector2.zero;

            float y = 0f;   // 相对 rowsRoot：0 = 折叠按钮下一行
            rowsRoot.GetComponent<RectTransform>().anchoredPosition = new Vector2(PanelX, -(PanelY + RowH + RowGap));
            MakeRow("判定窗口 1.5×（常驻）", ref y, new Color(.18f, .32f, .36f),
                () => Apply("DBG_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f));
            MakeRow("判定窗口 0.6×（收紧）", ref y, new Color(.18f, .32f, .36f),
                () => Apply("DBG_TIGHT", ModifierKind.JudgeWindowScale, 0.6f, 0f));
            MakeRow("判定线消失 3 秒", ref y, new Color(.24f, .28f, .34f),
                () => Apply("DBG_NOLINE", ModifierKind.JudgeLineHidden, 0f, 3f));
            MakeRow("谱面隐身 3 秒", ref y, new Color(.24f, .28f, .34f),
                () => Apply("DBG_NONOTES", ModifierKind.NotesHidden, 0f, 3f));
            MakeRow("清空全部效果", ref y, new Color(.45f, .24f, .16f),
                () => ModifierManager.Instance?.ClearAll());

            // 生效中：状态行
            GameObject textGo = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform trt = (RectTransform)textGo.transform;
            trt.SetParent(rowsRoot.transform, false);
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(0f, -(y + 6f));
            trt.sizeDelta = new Vector2(PanelW + 160f, 120f);
            status = textGo.GetComponent<TextMeshProUGUI>();
            status.font = font;
            status.fontSize = 22;
            status.alignment = TextAlignmentOptions.TopLeft;
            status.color = DeepSeaTheme.Accent;
            status.raycastTarget = false;
            status.text = "生效中：（无）";
        }

        private void ToggleRows()
        {
            if (rowsRoot != null) rowsRoot.SetActive(!rowsRoot.activeSelf);
            if (toggle != null)
            {
                var label = toggle.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = rowsRoot.activeSelf ? "MOD 调试 ▾" : "MOD 调试 ▸";
            }
        }

        private void Apply(string id, ModifierKind kind, float magnitude, float duration)
        {
            ApplyInternal(id, kind, magnitude, duration);
        }

        /// <summary>
        /// 快捷键入口（GameStarter 调用）：0–3 施加预设效果，4 = 清空全部。
        /// 与面板按钮走同一条路径，用于"面板点不动时也能验证 modifier"。
        /// </summary>
        public static void ApplyPresetByIndex(int index)
        {
            switch (index)
            {
                case 0: ApplyInternal("DBG_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f); break;
                case 1: ApplyInternal("DBG_TIGHT", ModifierKind.JudgeWindowScale, 0.6f, 0f); break;
                case 2: ApplyInternal("DBG_NOLINE", ModifierKind.JudgeLineHidden, 0f, 3f); break;
                case 3: ApplyInternal("DBG_NONOTES", ModifierKind.NotesHidden, 0f, 3f); break;
                case 4:
                    ModifierManager.EnsureInstance().ClearAll();
                    Debug.Log("[ModifierDebug] 清空全部效果");
                    break;
            }
        }

        /// <summary>统一入口：就地兜底，不依赖"场景是否重建过"</summary>
        private static void ApplyInternal(string id, ModifierKind kind, float magnitude, float duration)
        {
            ModifierManager mgr = ModifierManager.EnsureInstance();
            if (kind == ModifierKind.JudgeLineHidden || kind == ModifierKind.NotesHidden)
                PlayVisibilityController.EnsureInstance();   // 渲染侧控制器，旧场景里没有就现建
            mgr.Apply(new ModifierDef(id, kind, magnitude, duration));
            Debug.Log(string.Format("[ModifierDebug] 施加 {0}（{1}）", id, kind));
        }

        private void MakeRow(string text, ref float y, Color color, System.Action action)
        {
            // 行容器已经定位在右侧，子节点必须用相对坐标 (0, -y)：
            // 之前又加了一次 PanelX/PanelY，按钮被推到 x≈1636（画布只有 1080 宽）——整排跑到屏幕外
            Button b = MakeButton(rowsRoot.transform, text, 0f, -y, action);
            b.GetComponent<Image>().color = color;
            y += RowH + RowGap;
        }

        private Button MakeButton(Transform parent, string text, float x, float y, System.Action action)
        {
            var go = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(PanelW, RowH);

            Image img = go.GetComponent<Image>();
            img.color = new Color(.16f, .22f, .26f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            RectTransform lrt = (RectTransform)label.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            label.font = font;
            label.text = text;
            label.fontSize = 22;
            label.alignment = TextAlignmentOptions.Center;
            label.color = DeepSeaTheme.Ink;
            label.raycastTarget = false;

            button.onClick.AddListener(() => action?.Invoke());
            return button;
        }

        private void Update()
        {
            if (status == null || !rowsRoot.activeSelf) return;

            if (!visibilityChecked)
            {
                visibilityReady = FindObjectOfType<PlayVisibilityController>() != null;
                visibilityChecked = true;
            }
            ModifierManager mgr = ModifierManager.EnsureInstance();
            string deps = string.Format("管理器✓ · 可见性{0}", visibilityReady ? "✓" : "○（点隐身类按钮时自动创建）");

            if (mgr == null || mgr.Active.Count == 0)
            {
                status.text = deps + "\n生效中：（无）";
                return;
            }

            float songTime = GameManager.Instance != null ? GameManager.Instance.SongTime : 0f;
            StringBuilder sb = new StringBuilder();
            sb.Append("生效中：");
            for (int i = 0; i < mgr.Active.Count; i++)
            {
                ModifierRuntime rt = mgr.Active[i];
                if (rt?.def == null) continue;
                if (i > 0) sb.Append("  ");
                sb.Append(rt.def.kind);
                sb.Append(rt.IsPersistent ? "(常驻)" : string.Format("({0:0.0}s)", Mathf.Max(0f, rt.Remaining - songTime)));
            }
            status.text = deps + "\n" + sb.ToString();
        }
    }
}
#endif
