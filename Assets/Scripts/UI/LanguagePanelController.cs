using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Audio;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 语言选择面板：列出所有 Language 枚举值，点击即切换；当前语言按钮不可选（置灰）。
    /// 入口按钮接线在 GameStarter.Awake（本组件挂在初始非激活面板上，Awake 在首次激活时执行）。
    /// 语言按钮由 SceneBuilder 构建时遍历 Language 枚举生成，新增语言自动多一个按钮。
    /// </summary>
    public class LanguagePanelController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private List<Button> languageButtons = new List<Button>();
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            // 面板首次激活时执行（同 CalibrationController 模式）
            if (closeButton != null) closeButton.onClick.AddListener(Close);
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
            Loc.OnLanguageChanged -= RefreshButtons;
            Loc.OnLanguageChanged += RefreshButtons;
            RefreshButtons();
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= RefreshButtons;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && panelRoot != null && panelRoot.activeSelf)
                Close();
        }

        /// <summary>打开面板（菜单按钮，GameStarter 接线）</summary>
        public void Open()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshButtons();
        }

        public void Close()
        {
            AudioManager.Instance?.PlayButtonClick();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void SelectLanguage(Language lang)
        {
            AudioManager.Instance?.PlayButtonClick();
            Loc.SetLanguage(lang); // 触发 OnLanguageChanged → RefreshButtons + 全局 LocalizedText 刷新
        }

        /// <summary>当前语言对应的按钮置灰（不可选）</summary>
        private void RefreshButtons()
        {
            for (int i = 0; i < languageButtons.Count; i++)
            {
                if (languageButtons[i] != null)
                    languageButtons[i].interactable = (Language)i != Loc.CurrentLanguage;
            }
        }
    }
}
