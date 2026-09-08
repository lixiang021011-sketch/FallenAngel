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
    /// 选歌界面控制器（卷帘形态：ScrollRect 弹性滚动列表）。
    /// 面板初始非激活（其 Awake/Start 不执行），入口按钮由 GameStarter 接，
    /// 首次激活时订阅事件。条目由模板克隆进 Content，VerticalLayoutGroup 自动堆叠。
    /// 点击条目 → 直接开谱（战斗格临时占位期间的核心玩法入口之一）。
    /// </summary>
    public class SongSelectPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform content;
        [SerializeField] private GameObject entryTemplate;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameStarter gameStarter;

        private readonly List<GameObject> entries = new List<GameObject>();

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged += OnStateChanged;
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnStateChanged;
                GameManager.Instance.OnStateChanged += OnStateChanged;
            }
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= OnStateChanged;
        }

        /// <summary>打开选歌界面并刷新列表（GameStarter 入口按钮调用）</summary>
        public void Open()
        {
            Populate();
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        private void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>非菜单状态（开谱/进地图等）时自动收起</summary>
        private void OnStateChanged(GameState state)
        {
            if (state != GameState.Menu && panelRoot != null && panelRoot.activeSelf)
                panelRoot.SetActive(false);
        }

        /// <summary>重建条目列表：全量歌单（含 demo_），模板克隆进 Content</summary>
        private void Populate()
        {
            foreach (GameObject entry in entries)
                if (entry != null) Destroy(entry);
            entries.Clear();

            if (content == null || entryTemplate == null) return;

            List<ChartData> pool = ChartLoader.LoadAllChartPool(includeDemo: true);
            foreach (ChartData chart in pool)
            {
                GameObject clone = Instantiate(entryTemplate, content);
                clone.SetActive(true);
                entries.Add(clone);

                TextMeshProUGUI label = clone.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = $"{chart.metadata.songName}\n{chart.metadata.difficulty.ToString().ToUpper()} Lv.{chart.metadata.level}";
                }

                Button btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    ChartData captured = chart;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnSongClicked(captured));
                }
            }

            Debug.Log($"[SongSelectPanelController] 选歌列表刷新: {pool.Count} 首");
        }

        private void OnSongClicked(ChartData chart)
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);

            if (gameStarter != null)
            {
                gameStarter.StartChart(chart);
            }
            else
            {
                Debug.LogError("[SongSelectPanelController] 未注入 GameStarter，无法开谱（请重建场景）");
            }
        }
    }
}
