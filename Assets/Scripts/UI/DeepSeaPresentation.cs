using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>同时兼容已有代码生成场景与重新构建场景；只装饰初始UI。</summary>
    public sealed class DeepSeaPresentation : MonoBehaviour
    {
        private void Start()
        {
            DeepSeaTheme.Backdrop(transform);
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (TryApplyChromeRole(button))
                    continue;

                var image = button.targetGraphic as Image;
                if (image != null) image.color = DeepSeaTheme.Card;
                DeepSeaTheme.StyleButton(button);
                if (button.name.EndsWith("CloseButton") || button.name == "PauseButton")
                {
                    foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>())
                        label.enabled = false;
                    var symbol = DeepSeaTheme.Graphic(button.transform, "ControlSymbol",
                        button.name == "PauseButton" ? DeepSeaGraphic.Shape.Pause : DeepSeaGraphic.Shape.Close);
                    symbol.color = DeepSeaTheme.Ink;
                }
            }
            foreach (string name in new[] { "MenuPanel", "TalentPanel", "EquipmentPanel", "ShopPanel", "SaveSelectPanel", "SettingsPanel" })
            {
                var panel = transform.Find(name);
                if (panel != null) DeepSeaTheme.Backdrop(panel);
            }
            foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text.name.EndsWith("Title")) DeepSeaTheme.TitleRule(text);
            }
            var game = transform.Find("GamePanel");
            if (game != null) DeepSeaTheme.Backdrop(game);
            var menu = transform.Find("MenuPanel");
            if (menu != null)
            {
                var emblem = DeepSeaTheme.Graphic(menu, "NavigationEmblem", DeepSeaGraphic.Shape.Beacon);
                var rt = (RectTransform)emblem.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .92f);
                rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(120, 120);
                emblem.color = DeepSeaTheme.Ink;
            }
            Debug.Log("[DeepSeaPresentation] Marine UI theme initialized.");
        }

        /// <summary>主菜单与覆盖确认按规范角色上色，避免统一 Accent 抹平主/次/危险。</summary>
        private static bool TryApplyChromeRole(Button button)
        {
            switch (button.name)
            {
                case "NewGameButton":
                    DeepSeaTheme.ApplyRole(button, DeepSeaTheme.ButtonRole.Primary);
                    return true;
                case "NewGameConfirmButton":
                    DeepSeaTheme.ApplyRole(button, DeepSeaTheme.ButtonRole.Danger);
                    return true;
                case "SaveSelectButton":
                case "SongSelectButton":
                case "SettingsButton":
                case "NewGameCancelButton":
                    DeepSeaTheme.ApplyRole(button, DeepSeaTheme.ButtonRole.Secondary);
                    return true;
                default:
                    return false;
            }
        }
    }
}
