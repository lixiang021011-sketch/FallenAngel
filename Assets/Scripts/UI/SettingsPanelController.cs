using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Audio;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 选项设置页：语言直切 / 打开校准 / 下落速度（FallSpeedController 双组绑定）/ 音效音量 / 按键特效开关。
    /// 挂 SettingsPanel（初始非激活，Awake 首次激活执行）；入口按钮由 GameStarter 接线。
    /// 校准面板盖在本页之上时，本页 ESC 关闭让位给校准层（calibrationPanel 由 SceneBuilder 注入）。
    /// </summary>
    public sealed class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private List<Button> languageButtons = new List<Button>();
        [SerializeField] private Button openCalibrationButton;
        [SerializeField] private CalibrationController calibrationController;
        [SerializeField] private GameObject calibrationPanel;          // ESC 层叠判断（SceneBuilder 注入）
        [SerializeField] private Button sfxMinusButton;
        [SerializeField] private Button sfxPlusButton;
        [SerializeField] private TextMeshProUGUI sfxVolumeText;        // 动态文本
        [SerializeField] private Button hitEffectButton;
        [SerializeField] private TextMeshProUGUI hitEffectLabel;       // 按钮内 Label（动态文本）
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            // 面板首次激活时执行（同 LanguagePanelController 模式）
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (openCalibrationButton != null) openCalibrationButton.onClick.AddListener(OpenCalibration);
            if (sfxMinusButton != null) sfxMinusButton.onClick.AddListener(() => AdjustSfxVolume(-0.1f));
            if (sfxPlusButton != null) sfxPlusButton.onClick.AddListener(() => AdjustSfxVolume(0.1f));
            if (hitEffectButton != null) hitEffectButton.onClick.AddListener(ToggleHitEffect);
            for (int i = 0; i < languageButtons.Count; i++)
            {
                Language lang = (Language)i; // 循环体内声明，闭包捕获安全
                Button b = languageButtons[i];
                if (b == null) continue;
                b.onClick.AddListener(() => SelectLanguage(lang));
            }
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged -= RefreshLanguageButtons;
            Loc.OnLanguageChanged += RefreshLanguageButtons;
            RefreshLanguageButtons();
            RefreshSfxText();
            RefreshHitEffect();
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= RefreshLanguageButtons;
        }

        private void Update()
        {
            // ESC 层叠：校准面板盖在本页上时，ESC 由校准层处理
            if (Input.GetKeyDown(KeyCode.Escape) && panelRoot != null && panelRoot.activeSelf
                && !(calibrationPanel != null && calibrationPanel.activeSelf))
                Close();
        }

        /// <summary>打开设置页（主菜单按钮，GameStarter 接线）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshLanguageButtons();
            RefreshSfxText();
            RefreshHitEffect();
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OpenCalibration()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (calibrationController != null) calibrationController.Open();
        }

        private void SelectLanguage(Language lang)
        {
            AudioManager.Instance?.PlayButtonClick();
            Loc.SetLanguage(lang); // 触发 OnLanguageChanged → 全局 LocalizedText 刷新
        }

        /// <summary>当前语言对应的按钮置灰（不可选）</summary>
        private void RefreshLanguageButtons()
        {
            for (int i = 0; i < languageButtons.Count; i++)
                if (languageButtons[i] != null)
                    languageButtons[i].interactable = (Language)i != Loc.CurrentLanguage;
        }

        /// <summary>音效音量 ±0.1（0~1），走 AudioManager.SfxVolume（内含 GameSettings 持久化）</summary>
        private void AdjustSfxVolume(float delta)
        {
            AudioManager am = AudioManager.Instance;
            if (am != null)
            {
                am.SfxVolume = Mathf.Clamp01(am.SfxVolume + delta);
                am.PlayButtonClick(); // 点击瞬间音量变化天然反馈
            }
            RefreshSfxText();
        }

        private void RefreshSfxText()
        {
            float v = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : GameSettings.SfxVolume;
            if (sfxVolumeText != null) sfxVolumeText.text = Mathf.RoundToInt(v * 100) + "%";
        }

        private void ToggleHitEffect()
        {
            AudioManager.Instance?.PlayButtonClick();
            GameSettings.HitEffectEnabled = !GameSettings.HitEffectEnabled;
            RefreshHitEffect();
        }

        private void RefreshHitEffect()
        {
            bool on = GameSettings.HitEffectEnabled;
            if (hitEffectLabel != null)
            {
                hitEffectLabel.text = on ? Loc.T("settings.hitEffectOn") : Loc.T("settings.hitEffectOff");
                hitEffectLabel.color = on ? new Color(0.45f, 1f, 0.65f) : new Color(1f, 0.55f, 0.5f);
            }
        }
    }
}
