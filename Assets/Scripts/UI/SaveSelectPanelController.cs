using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Audio;
using FallenAngel.Core;
using FallenAngel.Data;

namespace FallenAngel.UI
{
    /// <summary>
    /// 正式存档选择面板：档案列表（显示名+成长积分）、新建档输入框、选中档后出现
    /// "进入游戏"与角落"天赋"按钮。挂 SaveSelectPanel（初始非激活，Awake 首次激活执行）；
    /// 入口按钮由 GameStarter 接线（session.EnterSaveSelect）。
    /// session 在运行时由 GameStarter.Awake 添加，本组件首次激活时查找（FindObjectOfType）。
    /// </summary>
    public sealed class SaveSelectPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform content;           // 档案条目容器（ScrollRect Content）
        [SerializeField] private GameObject entryTemplate;    // 条目克隆源（非激活）
        [SerializeField] private TMP_InputField nameInput;    // 新建档输入
        [SerializeField] private Button createButton;
        [SerializeField] private Button enterGameButton;      // 选中档后出现
        [SerializeField] private Button talentButton;         // 角落"天赋"
        [SerializeField] private TalentPanelController talentPanel; // SceneBuilder 反向注入（ESC 层叠与打开）
        [SerializeField] private Button closeButton;

        private readonly List<GameObject> clones = new List<GameObject>();
        private PortfolioSession session;
        private string selectedId;
        private string pendingName = "";

        private void Awake()
        {
            // 面板首次激活时执行（同 LanguagePanelController 模式）；
            // 此时 GameStarter.Awake 已把 PortfolioSession 挂到 Canvas 根
            session = FindObjectOfType<PortfolioSession>();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (createButton != null) createButton.onClick.AddListener(CreateProfile);
            if (enterGameButton != null) enterGameButton.onClick.AddListener(EnterGame);
            if (talentButton != null) talentButton.onClick.AddListener(OpenTalents);
            if (nameInput != null) nameInput.onValueChanged.AddListener(value => pendingName = value);
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (session != null) session.OnChanged -= RefreshFromSession;
            if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= OnStateChanged;
        }

        private void Subscribe()
        {
            if (session != null) { session.OnChanged -= RefreshFromSession; session.OnChanged += RefreshFromSession; }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnStateChanged;
                GameManager.Instance.OnStateChanged += OnStateChanged;
            }
        }

        private void Update()
        {
            // ESC 层叠：天赋面板盖在本页上时，ESC 由天赋层处理
            if (Input.GetKeyDown(KeyCode.Escape) && panelRoot != null && panelRoot.activeSelf
                && !(talentPanel != null && talentPanel.IsOpen))
                Close();
        }

        /// <summary>打开面板（主菜单"选择存档"按钮经 session.EnterSaveSelect 驱动本面板；也可直接调用）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            selectedId = null;
            RefreshButtons();
            RefreshList();
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
            session?.CloseSaveSelect();
        }

        /// <summary>非主菜单状态自动收起（防开谱后残留在游戏画面）</summary>
        private void OnStateChanged(GameState state)
        {
            if (state != GameState.Menu && panelRoot != null && panelRoot.activeSelf)
                panelRoot.SetActive(false);
        }

        private void RefreshFromSession()
        {
            if (panelRoot == null) return;
            // 面板初始非激活时 Awake 未执行，session 字段为空：此处惰性查找
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            // session 标志驱动打开：主菜单"选择存档"按钮只调 session.EnterSaveSelect()，
            // 面板随 OnChanged 自动弹出（单一事实源在 session 状态）
            if (session != null && session.SaveSelectOpen && !panelRoot.activeSelf)
            {
                // SetActive 会同步执行面板的 Awake（按钮接线），随后即可刷新列表
                panelRoot.SetActive(true);
                selectedId = null;
                RefreshButtons();
            }
            if (!panelRoot.activeSelf) return;
            RefreshList();
            RefreshButtons();
        }

        private void CreateProfile()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) return;
            session.CreateProfile(pendingName);
            pendingName = "";
            if (nameInput != null) nameInput.text = "";
            selectedId = null;
            RefreshList();
            RefreshButtons();
        }

        private void EnterGame()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) return;
            session.EnterGame();
            // 进入游戏后关闭本面板（失败时 session.Error 由 Portfolio 视图展示）
            if (session.Error == null && panelRoot != null) panelRoot.SetActive(false);
        }

        private void OpenTalents()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (talentPanel != null) talentPanel.Open();
            else Debug.LogWarning("[SaveSelectPanelController] 天赋面板未注入，请重建场景（Tools > FallenAngel > Build Default Game Scene）");
        }

        /// <summary>选中档后显隐"进入游戏"/"天赋"按钮</summary>
        private void RefreshButtons()
        {
            bool has = !string.IsNullOrEmpty(selectedId);
            if (enterGameButton != null) enterGameButton.gameObject.SetActive(has);
            if (talentButton != null) talentButton.gameObject.SetActive(has);
        }

        /// <summary>重建档案列表（克隆模板；显示名+积分；选中条目高亮）</summary>
        private void RefreshList()
        {
            foreach (GameObject clone in clones)
                if (clone != null) Destroy(clone);
            clones.Clear();
            if (content == null || entryTemplate == null || session == null) return;

            IReadOnlyList<string> ids = session.Talents.ListProfileIds();
            foreach (string id in ids)
            {
                string display;
                try
                {
                    PortfolioProfileData p = session.Talents.ReadProfile(id);
                    display = Loc.T("portfolio.balance", p.displayName, p.growthPoints);
                }
                catch
                {
                    display = Loc.T("portfolio.error") + " " + id.Substring(0, 6);
                }
                GameObject clone = Instantiate(entryTemplate, content, false);
                clone.SetActive(true);
                TextMeshProUGUI label = clone.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = display;
                Button b = clone.GetComponent<Button>();
                string capturedId = id; // 闭包捕获安全
                bool selected = id == selectedId;
                ColorBlock cb = b.colors;
                cb.normalColor = selected ? new Color(0.12f, 0.48f, 0.60f) : new Color(0.075f, 0.095f, 0.14f);
                b.colors = cb;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() =>
                {
                    AudioManager.Instance?.PlayButtonClick();
                    selectedId = capturedId;
                    session.SelectProfile(capturedId); // 含 growth.Recover（中断局按失败结算）
                    RefreshList();
                    RefreshButtons();
                });
                clones.Add(clone);
            }
        }
    }
}
