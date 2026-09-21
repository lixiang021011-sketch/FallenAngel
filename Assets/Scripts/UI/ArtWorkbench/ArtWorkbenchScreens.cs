using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>HUD 稿、选曲稿、结算稿。只读工作台事件，不改 ResultScreen / SongSelectPanelController。</summary>
    public sealed class ArtWorkbenchScreens : MonoBehaviour
    {
        public ArtWorkbenchBus Bus { get; private set; }
        public ArtWorkbenchPreset Preset { get; private set; }

        GameObject fieldHud;
        GameObject selectPage;
        GameObject resultPage;
        TextMeshProUGUI comboText;
        TextMeshProUGUI scoreText;
        TextMeshProUGUI progressText;
        Image progressFill;
        int combo;
        int score;
        float songT;
        bool songRunning;

        public void Build(ArtWorkbenchBus bus, ArtWorkbenchPreset preset)
        {
            Bus = bus;
            Preset = preset;
            BuildHud();
            BuildSongSelect();
            BuildResult();
            Show(ArtWorkbenchPage.Field);
            Subscribe();
        }

        void OnEnable() { Subscribe(); }
        void Start() { Subscribe(); }
        void OnDisable() { Unsubscribe(); }

        void Subscribe()
        {
            if (Bus == null) return;
            Unsubscribe();
            Bus.OnHud += HandleHud;
            Bus.OnHit += HandleHit;
            Bus.OnSongStart += HandleSongStart;
            Bus.OnSongEnd += HandleSongEnd;
            Bus.OnPage += Show;
        }

        void Unsubscribe()
        {
            if (Bus == null) return;
            Bus.OnHud -= HandleHud;
            Bus.OnHit -= HandleHit;
            Bus.OnSongStart -= HandleSongStart;
            Bus.OnSongEnd -= HandleSongEnd;
            Bus.OnPage -= Show;
        }

        void HandleSongStart()
        {
            songRunning = true;
            songT = 0f;
            combo = 0;
            score = 0;
            RefreshHud();
        }

        void HandleSongEnd()
        {
            songRunning = false;
            RefreshResult();
            Show(ArtWorkbenchPage.Result);
        }

        void Update()
        {
            if (!songRunning) return;
            songT += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(songT / 42f);
            if (progressFill != null) progressFill.fillAmount = p;
            if (progressText != null) progressText.text = FormatTime(songT) + " / 00:42";
        }

        public void Show(ArtWorkbenchPage page)
        {
            if (fieldHud != null) fieldHud.SetActive(page == ArtWorkbenchPage.Field);
            if (selectPage != null) selectPage.SetActive(page == ArtWorkbenchPage.SongSelect);
            if (resultPage != null) resultPage.SetActive(page == ArtWorkbenchPage.Result);
        }

        void HandleHud(int c, int s)
        {
            combo = c;
            score = s;
            RefreshHud();
        }

        void HandleHit(int lane, ArtWorkbenchGrade grade)
        {
            RefreshHud();
        }

        void RefreshHud()
        {
            if (comboText != null) comboText.text = combo > 0 ? combo.ToString() : "";
            if (scoreText != null) scoreText.text = score.ToString("0000000");
        }

        void RefreshResult()
        {
            var title = resultPage != null ? resultPage.transform.Find("Rank") : null;
            if (title != null)
            {
                var tmp = title.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = combo >= 20 ? "S" : combo >= 8 ? "A" : "B";
            }
            var scoreGo = resultPage != null ? resultPage.transform.Find("ScoreLine") : null;
            if (scoreGo != null)
            {
                var tmp = scoreGo.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "SCORE  " + score.ToString("0000000");
            }
        }

        void BuildHud()
        {
            fieldHud = ArtWorkbenchUi.Root(transform, "Hud", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            scoreText = ArtWorkbenchUi.Label(fieldHud.transform, "Score", "0000000", 36, ArtWorkbenchTokens.Ink,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            var srt = scoreText.rectTransform;
            srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(36f, -16f);
            srt.sizeDelta = new Vector2(420f, 56f);

            comboText = ArtWorkbenchUi.Label(fieldHud.transform, "Combo", "", 88, ArtWorkbenchTokens.Paid,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), Vector2.zero, Vector2.zero);
            comboText.rectTransform.sizeDelta = new Vector2(480f, 120f);

            ArtWorkbenchUi.Label(fieldHud.transform, "ComboHint", "COMBO", 22, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.Center, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), Vector2.zero, Vector2.zero)
                .rectTransform.sizeDelta = new Vector2(240f, 36f);

            var bar = ArtWorkbenchUi.Panel(fieldHud.transform, "ProgressBg", new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(36f, -12f), new Vector2(-36f, -6f));
            bar.rectTransform.sizeDelta = new Vector2(0f, 8f);
            progressFill = ArtWorkbenchUi.Panel(bar.transform, "Fill", ArtWorkbenchTokens.Accent,
                Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0.2f;
            var fillRt = progressFill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

            progressText = ArtWorkbenchUi.Label(fieldHud.transform, "Time", "00:00 / 00:42", 22, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var prt = progressText.rectTransform;
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-36f, -28f);
            prt.sizeDelta = new Vector2(280f, 40f);
        }

        void BuildSongSelect()
        {
            selectPage = ArtWorkbenchUi.Panel(transform, "SongSelect", new Color(0.04f, 0.08f, 0.11f, 0.96f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true).gameObject;
            ArtWorkbenchUi.Label(selectPage.transform, "Title", "选曲", 48, ArtWorkbenchTokens.Ink,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -36f), new Vector2(0f, -100f));

            string[] songs = { "酸橙色信笺  ·  Easy", "酸橙色信笺  ·  双指", "无声测试谱  ·  全类型" };
            string[] meta = { "鼓  ·  4K  ·  Lv.3", "鼓  ·  4K  ·  Lv.8", "调试  ·  5K  ·  虚拟钟" };
            for (int i = 0; i < songs.Length; i++)
            {
                float y = -160f - i * 150f;
                var card = ArtWorkbenchUi.Panel(selectPage.transform, "Card" + i, ArtWorkbenchTokens.Card,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero, true);
                var rt = card.rectTransform;
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(920f, 132f);
                var accent = i == 1 ? ArtWorkbenchTokens.Paid : ArtWorkbenchTokens.Accent;
                ArtWorkbenchUi.Panel(card.transform, "Bar", accent,
                    new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero)
                    .rectTransform.sizeDelta = new Vector2(10f, 0f);
                ArtWorkbenchUi.Label(card.transform, "Name", songs[i], 34, ArtWorkbenchTokens.Ink,
                    TextAlignmentOptions.Left, new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(36f, 0f), new Vector2(-24f, -12f));
                ArtWorkbenchUi.Label(card.transform, "Meta", meta[i], 24, ArtWorkbenchTokens.Muted,
                    TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(36f, 12f), new Vector2(-24f, 0f));
            }
        }

        void BuildResult()
        {
            resultPage = ArtWorkbenchUi.Panel(transform, "Result", new Color(0.04f, 0.08f, 0.11f, 0.96f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true).gameObject;
            ArtWorkbenchUi.Label(resultPage.transform, "Caption", "结算", 28, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f), new Vector2(0f, -90f));
            ArtWorkbenchUi.Label(resultPage.transform, "Rank", "S", 180, ArtWorkbenchTokens.Paid,
                TextAlignmentOptions.Center, new Vector2(0f, 0.62f), new Vector2(1f, 0.82f), Vector2.zero, Vector2.zero);
            ArtWorkbenchUi.Label(resultPage.transform, "ScoreLine", "SCORE  0000000", 40, ArtWorkbenchTokens.Ink,
                TextAlignmentOptions.Center, new Vector2(0f, 0.52f), new Vector2(1f, 0.62f), Vector2.zero, Vector2.zero);
            ArtWorkbenchUi.Label(resultPage.transform, "Judges", "P  12    G  3    M  1", 30, ArtWorkbenchTokens.Accent,
                TextAlignmentOptions.Center, new Vector2(0f, 0.42f), new Vector2(1f, 0.52f), Vector2.zero, Vector2.zero);
            ArtWorkbenchUi.Label(resultPage.transform, "MaxCombo", "MAX COMBO  12", 28, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.Center, new Vector2(0f, 0.34f), new Vector2(1f, 0.42f), Vector2.zero, Vector2.zero);
        }

        static string FormatTime(float t)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(t));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }
    }
}
