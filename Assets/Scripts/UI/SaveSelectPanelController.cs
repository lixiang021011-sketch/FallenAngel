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
        private TextMeshProUGUI selectionSummary, emptyLabel;
        private RectTransform designPage;
        private ScrollRect listScroll;
        private bool refreshPending;
        private string T(string key, params object[] args) => Loc.T("save02." + key, args);

        private static void Place(RectTransform rect, Transform parent, float x, float y, float w, float h)
        {
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0,1);
            rect.pivot = new Vector2(0,1); rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(w,h);
        }
        private TextMeshProUGUI Text(Transform parent, string name, string value, float x, float y, float w, float h, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Place((RectTransform)go.transform,parent,x,y,w,h);
            var t=go.GetComponent<TextMeshProUGUI>();t.font=nameInput.textComponent.font;t.text=value;t.fontSize=size;
            t.color=DeepSeaTheme.Ink;t.raycastTarget=false;t.richText=false;t.overflowMode=TextOverflowModes.Ellipsis;return t;
        }
        private void BuildVisuals()
        {
            if(designPage!=null)return;
            Loc.AddFallback("save02.title","选择存档","Archives");
            Loc.AddFallback("save02.empty","暂无存档","No archives");
            Loc.AddFallback("save02.choose","选择一个存档","Select an archive");
            Loc.AddFallback("save02.points","成长积分  {0}    ·    天赋  {1}","Growth  {0}    ·    Talents  {1}");
            Loc.AddFallback("save02.ready","待启程","Ready");
            Loc.AddFallback("save02.active","进行中","In progress");
            Loc.AddFallback("save02.invalid","无法读取","Unavailable");
            Loc.AddFallback("save02.continue","继续游戏","Continue");
            Loc.AddFallback("save02.enter","进入游戏","Enter");
            Loc.AddFallback("save02.new","新建存档","Create archive");
            panelRoot.GetComponent<Image>().color=DeepSeaTheme.Background;
            DeepSeaTheme.Backdrop(panelRoot.transform);
            var go=new GameObject("ArchivePage",typeof(RectTransform));designPage=(RectTransform)go.transform;
            designPage.SetParent(panelRoot.transform,false);designPage.anchorMin=designPage.anchorMax=new Vector2(.5f,.5f);
            designPage.pivot=new Vector2(.5f,.5f);designPage.sizeDelta=new Vector2(1000,1760);
            var oldTitle=panelRoot.transform.Find("SaveSelectTitle");if(oldTitle!=null)oldTitle.gameObject.SetActive(false);
            Text(designPage,"ArchiveBrand","FALLEN ANGEL",20,0,960,40,24).color=DeepSeaTheme.Muted;
            Text(designPage,"ArchiveTitle",T("title"),20,65,960,90,60);
            Place(nameInput.GetComponent<RectTransform>(),designPage,20,195,630,100);
            nameInput.GetComponent<Image>().color=DeepSeaTheme.Card;
            nameInput.textComponent.fontSize=32;nameInput.textComponent.color=DeepSeaTheme.Ink;nameInput.textComponent.richText=false;
            nameInput.lineType=TMP_InputField.LineType.SingleLine;
            foreach(var t in nameInput.GetComponentsInChildren<TextMeshProUGUI>()){
                t.rectTransform.anchorMin=Vector2.zero;t.rectTransform.anchorMax=Vector2.one;t.rectTransform.offsetMin=new Vector2(22,8);t.rectTransform.offsetMax=new Vector2(-22,-8);
            }
            Place(createButton.GetComponent<RectTransform>(),designPage,680,195,300,100);
            DeepSeaTheme.ApplyRole(createButton,DeepSeaTheme.ButtonRole.Secondary);
            listScroll=content.GetComponentInParent<ScrollRect>();
            Place(listScroll.GetComponent<RectTransform>(),designPage,20,345,960,820);
            listScroll.GetComponent<Image>().color=Color.clear;listScroll.movementType=ScrollRect.MovementType.Clamped;
            content.GetComponent<RectTransform>().sizeDelta=new Vector2(940,0);
            emptyLabel=Text(designPage,"ArchiveEmpty",T("empty"),20,590,960,80,32);emptyLabel.alignment=TextAlignmentOptions.Center;
            selectionSummary=Text(designPage,"ArchiveSelection",T("choose"),20,1225,960,150,32);
            Place(enterGameButton.GetComponent<RectTransform>(),designPage,20,1400,960,110);
            DeepSeaTheme.ApplyRole(enterGameButton,DeepSeaTheme.ButtonRole.Primary);
            Place(talentButton.GetComponent<RectTransform>(),designPage,515,1550,465,100);
            DeepSeaTheme.ApplyRole(talentButton,DeepSeaTheme.ButtonRole.Secondary);
            Place(closeButton.GetComponent<RectTransform>(),designPage,20,1550,465,100);
            DeepSeaTheme.ApplyRole(closeButton,DeepSeaTheme.ButtonRole.Secondary);
        }
        private void LateUpdate(){if(refreshPending){refreshPending=false;RefreshList();RefreshButtons();}}


        private void Awake()
        {
            // 面板首次激活时执行（同 LanguagePanelController 模式）；
            // 此时 GameStarter.Awake 已把 PortfolioSession 挂到 Canvas 根
            session = FindObjectOfType<PortfolioSession>();
            BuildVisuals();
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (createButton != null) createButton.onClick.AddListener(CreateProfile);
            if (enterGameButton != null) enterGameButton.onClick.AddListener(EnterGame);
            if (talentButton != null) talentButton.onClick.AddListener(OpenTalents);
            if (nameInput != null) nameInput.onValueChanged.AddListener(value =>
            {
                pendingName = value;
                RefreshCreateButton();
            });
            RefreshCreateButton();
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
            Loc.OnLanguageChanged -= LanguageChanged;
        }

        private void LanguageChanged(){refreshPending=true; if(designPage!=null)designPage.Find("ArchiveTitle").GetComponent<TextMeshProUGUI>().text=T("title");}
        private void Subscribe()
        {
            Loc.OnLanguageChanged -= LanguageChanged; Loc.OnLanguageChanged += LanguageChanged;
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
            if (panelRoot != null) panelRoot.SetActive(true); // Awake builds UI before the first list refresh.
            if (session == null) session = FindObjectOfType<PortfolioSession>();
            selectedId = null;
            RefreshButtons();
            RefreshList();
            if (panelRoot != null) panelRoot.SetActive(true);
            refreshPending=true;
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
            refreshPending=true;
        }

        /// <summary>名字为空时新建按钮置灰（防止空名直入校验抛异常）</summary>
        private void RefreshCreateButton()
        {
            if (createButton != null)
                createButton.interactable = !string.IsNullOrWhiteSpace(pendingName);
        }

        private void CreateProfile()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (session == null) return;
            string name = (pendingName ?? "").Trim();
            // 守卫兜底：即使按钮未被置灰（如未来其他入口），空名也不进入校验
            if (name.Length == 0)
            {
                Debug.LogWarning("[SaveSelectPanelController] 存档名称为空，未创建");
                return;
            }
            session.Execute(() => session.CreateProfile(name));
            if(session.Error!=null){selectionSummary.text=T("invalid");return;}
            selectedId=session.Profile.profileId;
            pendingName = "";
            if (nameInput != null) nameInput.text = "";
            RefreshCreateButton();
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
            if (enterGameButton != null) {
                enterGameButton.gameObject.SetActive(true);enterGameButton.interactable=has;
                var t=enterGameButton.GetComponentInChildren<TextMeshProUGUI>();
                var loc=t.GetComponent<LocalizedText>();if(loc!=null)loc.enabled=false;
                t.text=has && session.Run!=null && session.Run.phase!="FINISHED"?T("continue"):T("enter");
            }
            if(selectionSummary!=null)selectionSummary.text=has && session.Profile!=null ? session.Profile.displayName+"\n"+T("points",session.Profile.growthPoints,session.Profile.unlockedNodeIds.Count) : T("choose");
            if (talentButton != null) talentButton.gameObject.SetActive(has);
        }

        /// <summary>重建档案列表（克隆模板；显示名+积分；选中条目高亮）</summary>
        private void RefreshList()
        {
            float offset=content==null?0:content.GetComponent<RectTransform>().anchoredPosition.y;
            foreach (GameObject clone in clones)
                if (clone != null) {clone.SetActive(false);Destroy(clone);}
            clones.Clear();
            if (content == null || entryTemplate == null || session == null) return;

            IReadOnlyList<string> ids = session.Talents.ListProfileIds();
            emptyLabel.gameObject.SetActive(ids.Count==0);emptyLabel.text=T("empty");
            foreach (string id in ids)
            {
                string display; bool valid=true; string status=T("ready"); string points="";
                try
                {
                    PortfolioProfileData p = session.Talents.ReadProfile(id);
                    display = p.displayName; points=T("points",p.growthPoints,p.unlockedNodeIds.Count);
                    if(!string.IsNullOrEmpty(p.activeRunId))status=T("active");
                }
                catch
                {
                    display = T("invalid");valid=false;
                }
                GameObject clone = Instantiate(entryTemplate, content, false);
                clone.name="Archive_"+id;
                clone.GetComponent<RectTransform>().sizeDelta=new Vector2(940,210);
                clone.SetActive(true);
                TextMeshProUGUI label = clone.transform.Find("Label").GetComponent<TextMeshProUGUI>();
                var localized=label.GetComponent<LocalizedText>();if(localized!=null)localized.enabled=false;
                label.text = display;label.richText=false;label.overflowMode=TextOverflowModes.Ellipsis;label.alignment=TextAlignmentOptions.MidlineLeft;
                Place(label.rectTransform,clone.transform,28,25,660,60);
                Button b = clone.GetComponent<Button>();
                string capturedId = id; // 闭包捕获安全
                bool selected = id == selectedId;
                DeepSeaTheme.ApplyRole(b,selected?DeepSeaTheme.ButtonRole.Primary:DeepSeaTheme.ButtonRole.Secondary);
                b.interactable=valid;
                Text(clone.transform,"ArchiveStatus",status,710,38,205,45,26).color=selected?DeepSeaTheme.Background:DeepSeaTheme.Muted;
                Text(clone.transform,"ArchivePoints",points,28,115,870,55,28).color=selected?DeepSeaTheme.Background:DeepSeaTheme.Muted;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() =>
                {
                    AudioManager.Instance?.PlayButtonClick();
                    session.Execute(() => session.SelectProfile(capturedId));
                    selectedId = session.Error == null ? capturedId : null; // // 只读档；PLAYING 中断结算改在进入游戏时
                    refreshPending=true;
                });
                clones.Add(clone);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
            var cr=content.GetComponent<RectTransform>();cr.anchoredPosition=new Vector2(0,Mathf.Clamp(offset,0,Mathf.Max(0,cr.rect.height-listScroll.viewport.rect.height)));
        }
    }
}
