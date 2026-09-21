using FallenAngel.Core;
using FallenAngel.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 美术工作台入口。独立场景或叠在正式 Canvas 上。
    /// 不改判定窗口、谱面、计分、校准，不替换 Assets/Art。
    /// </summary>
    public sealed class ArtWorkbenchHost : MonoBehaviour
    {
        public static ArtWorkbenchHost Instance { get; private set; }

        public ArtWorkbenchPreset Preset { get; private set; }
        public ArtWorkbenchBus Bus { get; private set; }

        ArtWorkbenchField field;
        ArtWorkbenchScreens screens;
        ArtWorkbenchPanel panel;
        TextMeshProUGUI banner;
        bool liveBound;
        int fireLane;
        ArtWorkbenchPage page;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallHotkey()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (FindObjectOfType<ArtWorkbenchHotkey>() != null) return;
            var go = new GameObject("ArtWorkbenchHotkey");
            go.AddComponent<ArtWorkbenchHotkey>();
#endif
        }

        public static ArtWorkbenchHost Toggle()
        {
            if (Instance != null)
            {
                bool show = !Instance.gameObject.activeSelf;
                Instance.gameObject.SetActive(show);
                Debug.Log("[ArtWorkbench] " + (show ? "打开" : "关闭"));
                return Instance;
            }
            return Open();
        }

        public static ArtWorkbenchHost Open()
        {
            if (Instance != null)
            {
                Instance.gameObject.SetActive(true);
                return Instance;
            }

            EnsureEventSystem();
            var canvasGo = new GameObject("ArtWorkbench", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ArtWorkbenchHost));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 400;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ArtWorkbenchTokens.CanvasWidth, ArtWorkbenchTokens.CanvasHeight);
            scaler.matchWidthOrHeight = 0.5f;
            var host = canvasGo.GetComponent<ArtWorkbenchHost>();
            host.Build();
            Debug.Log("[ArtWorkbench] 工作台已创建");
            return host;
        }

        public static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (Bus == null) Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnbindLive();
        }

        void OnEnable() { BindLiveIfNeeded(); }
        void Start() { BindLiveIfNeeded(); }
        void OnDisable() { UnbindLive(); }

        public void Build()
        {
            if (Bus != null) return;
            Bus = new ArtWorkbenchBus();
            Preset = ArtWorkbenchStore.LoadLastOrDefault();
            ResolveFont();

            var blocker = ArtWorkbenchUi.Panel(transform, "Blocker", new Color(0f, 0f, 0f, 0.35f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);
            blocker.transform.SetAsFirstSibling();

            var top = ArtWorkbenchUi.Panel(transform, "TopBar", new Color(0.05f, 0.09f, 0.12f, 0.98f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -ArtWorkbenchTokens.TopBar), Vector2.zero, true);
            ArtWorkbenchUi.Button(top.transform, "TabField", "测试场", ArtWorkbenchTokens.Accent, () => SetPage(ArtWorkbenchPage.Field),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 12f), new Vector2(170f, -12f));
            ArtWorkbenchUi.Button(top.transform, "TabSelect", "选曲稿", ArtWorkbenchTokens.Card, () => SetPage(ArtWorkbenchPage.SongSelect),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(182f, 12f), new Vector2(336f, -12f));
            ArtWorkbenchUi.Button(top.transform, "TabResult", "结算稿", ArtWorkbenchTokens.Card, () => SetPage(ArtWorkbenchPage.Result),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(348f, 12f), new Vector2(502f, -12f));
            ArtWorkbenchUi.Button(top.transform, "Close", "关闭", ArtWorkbenchTokens.Miss, () => gameObject.SetActive(false),
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-140f, 12f), new Vector2(-16f, -12f));
            banner = ArtWorkbenchUi.Label(top.transform, "Banner", "美术工作台  v" + ArtWorkbenchTokens.Version, 22, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.MidlineRight, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(20f, 0f), new Vector2(-156f, 0f));

            var fieldRt = ArtWorkbenchUi.Root(transform, "FieldHost",
                Vector2.zero, Vector2.one,
                new Vector2(0f, ArtWorkbenchTokens.BottomPanel),
                new Vector2(0f, -ArtWorkbenchTokens.TopBar));
            field = fieldRt.gameObject.AddComponent<ArtWorkbenchField>();
            field.Build(Bus, Preset);

            screens = fieldRt.gameObject.AddComponent<ArtWorkbenchScreens>();
            screens.Build(Bus, Preset);

            var panelRt = ArtWorkbenchUi.Root(transform, "PanelHost",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, ArtWorkbenchTokens.BottomPanel));
            panel = panelRt.gameObject.AddComponent<ArtWorkbenchPanel>();
            panel.Build(this);

            SetPage(ArtWorkbenchPage.Field);
            BindLiveIfNeeded();
            Bus.SongStart();
            SetStatus(DescribeBind());
        }

        public void ApplyPreset()
        {
            if (field != null) field.Apply(Preset);
        }

        public void FireGrade(ArtWorkbenchGrade grade)
        {
            fireLane = 1 - fireLane;
            field?.TriggerGrade(grade, fireLane);
            SetStatus("工作台事件  " + grade + "  lane " + fireLane);
        }

        public void FireHold(bool start)
        {
            int lane = fireLane;
            if (start) Bus.HoldStart(lane);
            else Bus.HoldEnd(lane);
            SetStatus(start ? "工作台事件  HoldStart" : "工作台事件  HoldEnd");
        }

        public void SpawnTap()
        {
            fireLane = 1 - fireLane;
            field?.SpawnTap(fireLane);
        }

        public void SaveSlot(string slot)
        {
            ArtWorkbenchStore.Save(Preset, slot);
            SetStatus("已保存 " + slot + "（重启后仍在）");
        }

        public void LoadSlot(string slot)
        {
            Preset = ArtWorkbenchStore.Load(slot);
            ApplyPreset();
            panel?.SyncFromPreset();
            SetLiveBind(Preset.bindLiveEvents);
            SetStatus("已读取 " + slot);
        }

        public void SetLiveBind(bool on)
        {
            Preset.bindLiveEvents = on;
            if (on) BindLiveIfNeeded();
            else UnbindLive();
            SetStatus(DescribeBind());
        }

        public void SetPage(ArtWorkbenchPage next)
        {
            page = next;
            Bus.Page(next);
        }

        public void SetStatus(string text)
        {
            panel?.SetStatus(text);
            if (banner != null) banner.text = text;
            Debug.Log("[ArtWorkbench] " + text);
        }

        public ArtWorkbenchPage CurrentPage { get { return page; } }

        void ResolveFont()
        {
            if (ArtWorkbenchUi.Font != null) return;
#if UNITY_EDITOR
            var cjk = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/CJK_Font SDF.asset");
            if (cjk != null)
            {
                ArtWorkbenchUi.Font = cjk;
                return;
            }
#endif
            foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>(true))
            {
                if (tmp != null && tmp.font != null)
                {
                    ArtWorkbenchUi.Font = tmp.font;
                    return;
                }
            }
            ArtWorkbenchUi.Font = TMP_Settings.defaultFontAsset;
        }

        void BindLiveIfNeeded()
        {
            if (Preset == null || !Preset.bindLiveEvents)
            {
                liveBound = false;
                return;
            }
            UnbindLive();
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnJudgeResult -= OnLiveJudge;
                JudgeManager.Instance.OnJudgeResult += OnLiveJudge;
                JudgeManager.Instance.OnComboUpdate -= OnLiveCombo;
                JudgeManager.Instance.OnComboUpdate += OnLiveCombo;
                JudgeManager.Instance.OnScoreUpdate -= OnLiveScore;
                JudgeManager.Instance.OnScoreUpdate += OnLiveScore;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= OnLiveSongStart;
                GameManager.Instance.OnGameStart += OnLiveSongStart;
                GameManager.Instance.OnGameEnd -= OnLiveSongEnd;
                GameManager.Instance.OnGameEnd += OnLiveSongEnd;
            }
            liveBound = JudgeManager.Instance != null || GameManager.Instance != null;
        }

        void UnbindLive()
        {
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnJudgeResult -= OnLiveJudge;
                JudgeManager.Instance.OnComboUpdate -= OnLiveCombo;
                JudgeManager.Instance.OnScoreUpdate -= OnLiveScore;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnGameStart -= OnLiveSongStart;
                GameManager.Instance.OnGameEnd -= OnLiveSongEnd;
            }
            liveBound = false;
        }

        void OnLiveJudge(JudgeResultType type, int lane)
        {
            Bus.Hit(Mathf.Clamp(lane, 0, 1), ArtWorkbenchBus.FromLive(type));
            SetStatus("实机事件  " + type + "  lane " + lane);
        }

        int liveCombo;
        void OnLiveCombo(int combo, bool fc)
        {
            liveCombo = combo;
            Bus.Hud(combo, liveScore);
        }

        int liveScore;
        void OnLiveScore(int score)
        {
            liveScore = score;
            Bus.Hud(liveCombo, score);
        }

        void OnLiveSongStart() { Bus.SongStart(); SetStatus("实机事件  OnGameStart"); }
        void OnLiveSongEnd() { Bus.SongEnd(); SetStatus("实机事件  OnGameEnd"); }

        string DescribeBind()
        {
            if (Preset != null && Preset.bindLiveEvents && liveBound)
                return "已绑定 JudgeManager / GameManager";
            if (Preset != null && Preset.bindLiveEvents)
                return "实机事件不可用，使用工作台事件";
            return "沙盒事件就绪（未绑定实机）";
        }
    }

    /// <summary>全局 F9 开关，不进 Build Settings。</summary>
    public sealed class ArtWorkbenchHotkey : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                ArtWorkbenchHost.Toggle();
        }
    }

    /// <summary>独立场景入口：场景里挂一个即可。</summary>
    public sealed class ArtWorkbenchBootstrap : MonoBehaviour
    {
        void Awake()
        {
            ArtWorkbenchHost.EnsureEventSystem();
            if (ArtWorkbenchHost.Instance == null)
                ArtWorkbenchHost.Open();
        }
    }
}
