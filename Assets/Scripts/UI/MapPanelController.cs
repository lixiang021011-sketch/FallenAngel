using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Core;
using FallenAngel.Data;
using FallenAngel.Audio;

namespace FallenAngel.UI
{
    /// <summary>
    /// Roguelite 地图面板控制器。挂 MapPanel 根（始终激活），内容子节点 MapContent 显隐
    /// ——与 PauseController 挂 HUDPanel 同一个坑：挂内容节点会在失活时丢失事件链。
    /// 节点按钮由模板克隆、手动 anchoredPosition 堆叠定位（不依赖 LayoutGroup）。
    /// </summary>
    public class MapPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject mapContent;
        [SerializeField] private Transform nodesContainer;
        [SerializeField] private GameObject nodeButtonTemplate;
        [SerializeField] private GameObject mapInfoPopup;
        [SerializeField] private TextMeshProUGUI popupTitleText;
        [SerializeField] private TextMeshProUGUI popupDescText;
        [SerializeField] private Button popupCloseButton;

        private readonly List<GameObject> nodeClones = new List<GameObject>();

        private void OnEnable()
        {
            SubscribeStateChanged();
            SubscribeRunEvents();
        }

        private void Start()
        {
            SubscribeStateChanged();
            SubscribeRunEvents();
            if (popupCloseButton != null) popupCloseButton.onClick.AddListener(OnPopupClose);
            Loc.OnLanguageChanged -= RefreshNodes;
            Loc.OnLanguageChanged += RefreshNodes;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= OnStateChanged;
            if (RunManager.Instance != null)
                RunManager.Instance.OnMapUpdated -= RefreshNodes;
            Loc.OnLanguageChanged -= RefreshNodes;
        }

        private void SubscribeStateChanged()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnStateChanged -= OnStateChanged;
            GameManager.Instance.OnStateChanged += OnStateChanged;
        }

        private void SubscribeRunEvents()
        {
            if (RunManager.Instance == null) return;
            RunManager.Instance.OnMapUpdated -= RefreshNodes;
            RunManager.Instance.OnMapUpdated += RefreshNodes;
        }

        private void OnStateChanged(GameState state)
        {
            // 防御：RunManager 流退役后无人进入 Map 状态；若未来误调 GoToMap，
            // 无局（IsInRun=false）时不显示空白地图背景，避免盖住 Portfolio 地图页
            bool show = state == GameState.Map && RunManager.Instance != null && RunManager.Instance.IsInRun;
            if (mapContent != null) mapContent.SetActive(show);
            if (!show && mapInfoPopup != null) mapInfoPopup.SetActive(false); // 离开地图时收起弹窗
            if (show) RefreshNodes();
        }

        /// <summary>按当前地图重建节点按钮（克隆模板手动堆叠定位）</summary>
        public void RefreshNodes()
        {
            foreach (GameObject clone in nodeClones)
                if (clone != null) Destroy(clone);
            nodeClones.Clear();

            if (nodesContainer == null || nodeButtonTemplate == null) return;
            if (RunManager.Instance == null || RunManager.Instance.CurrentMap == null) return;

            MapData map = RunManager.Instance.CurrentMap;
            int current = RunManager.Instance.CurrentNodeIndex;
            const float stepY = 120f;
            float startY = (map.Nodes.Count - 1) * stepY * 0.5f; // 居中堆叠

            for (int i = 0; i < map.Nodes.Count; i++)
            {
                MapNode node = map.Nodes[i];

                GameObject clone = Instantiate(nodeButtonTemplate, nodesContainer);
                clone.SetActive(true);
                RectTransform rt = clone.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(0f, startY - i * stepY);
                nodeClones.Add(clone);

                TextMeshProUGUI label = clone.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = node.Type == MapNodeType.Battle && node.Chart?.metadata != null
                        ? $"{node.Chart.metadata.songName}\n{node.Chart.metadata.difficulty.ToString().ToUpper()} Lv.{node.Chart.metadata.level}"
                        : Loc.T($"map.{node.Type.ToString().ToLower()}");
                }

                Button btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.interactable = i == current; // 仅当前节点可点
                    int idx = i;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnNodeClicked(idx));
                }

                Image img = clone.GetComponent<Image>();
                if (img != null)
                {
                    // 终点独立样式；已走置灰；当前高亮；未到暗色
                    if (node.Type == MapNodeType.End)
                        img.color = new Color(0.75f, 0.6f, 0.2f, 0.9f);
                    else if (i < current)
                        img.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
                    else if (i == current)
                        img.color = new Color(0.25f, 0.65f, 0.95f, 0.95f);
                    else
                        img.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);
                }
            }

            Debug.Log($"[MapPanelController] 地图刷新: {map.Nodes.Count} 节点, 当前第 {current} 个");
        }

        private void OnNodeClicked(int index)
        {
            if (RunManager.Instance == null) return;
            RunManager run = RunManager.Instance;
            if (!run.IsInRun || index != run.CurrentNodeIndex) return; // 防御：仅当前节点

            MapNode node = run.CurrentMap.Nodes[index];

            // 战斗格真实游玩（BattleNodeAsPlaceholder=false 时启用）
            if (node.Type == MapNodeType.Battle && !RunManager.BattleNodeAsPlaceholder)
            {
                AudioManager.Instance?.PlayButtonClick();
                run.SelectCurrentNode();
                return;
            }

            // 战斗格（临时占位）/占位/终点格：说明弹窗
            if (popupTitleText != null)
                popupTitleText.text = Loc.T($"map.{node.Type.ToString().ToLower()}");
            if (popupDescText != null)
                popupDescText.text = Loc.T($"map.desc.{node.Type.ToString().ToLower()}");
            if (mapInfoPopup != null) mapInfoPopup.SetActive(true);
        }

        private void OnPopupClose()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (mapInfoPopup != null) mapInfoPopup.SetActive(false);

            if (RunManager.Instance == null || !RunManager.Instance.IsInRun) return;
            RunManager run = RunManager.Instance;
            if (run.CurrentMap == null || run.CurrentNodeIndex >= run.CurrentMap.Nodes.Count) return;

            MapNode node = run.CurrentMap.Nodes[run.CurrentNodeIndex];
            if (node.Type == MapNodeType.End)
                run.FinishRun();
            else
                run.CloseInfoAndAdvance();
        }
    }
}
