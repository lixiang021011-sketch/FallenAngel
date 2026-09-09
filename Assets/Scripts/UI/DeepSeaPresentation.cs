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
            foreach(var button in GetComponentsInChildren<Button>(true))
            {
                var image=button.targetGraphic as Image;
                if(image!=null)image.color=DeepSeaTheme.Accent;
                DeepSeaTheme.StyleButton(button);
                if(button.name.EndsWith("CloseButton") || button.name=="PauseButton")
                {
                    foreach(var label in button.GetComponentsInChildren<TMPro.TextMeshProUGUI>()) label.enabled=false;
                    var symbol=DeepSeaTheme.Graphic(button.transform,"ControlSymbol",button.name=="PauseButton"?DeepSeaGraphic.Shape.Pause:DeepSeaGraphic.Shape.Close);
                    symbol.color=DeepSeaTheme.Ink;
                }
            }
            foreach(string name in new[]{"MenuPanel","TalentPanel","EquipmentPanel","ShopPanel","SaveSelectPanel","SettingsPanel"})
            {
                var panel=transform.Find(name);
                if(panel!=null)DeepSeaTheme.Backdrop(panel);
            }
            // 标题条装饰：所有面板标题下方加主题规则线（同锚点，不改变布局）
            foreach(var text in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if(text.name.EndsWith("Title"))DeepSeaTheme.TitleRule(text);
            }
            // 演奏区域沿用4K/5K颜色语义，环境只放在轨道后面。
            var game=transform.Find("GamePanel");
            if(game!=null)DeepSeaTheme.Backdrop(game);
            var menu=transform.Find("MenuPanel");
            if(menu!=null)
            {
                var emblem=DeepSeaTheme.Graphic(menu,"NavigationEmblem",DeepSeaGraphic.Shape.Beacon);
                var rt=(RectTransform)emblem.transform;rt.anchorMin=rt.anchorMax=new Vector2(.5f,.92f);
                rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(120,120);
            }
            Debug.Log("[DeepSeaPresentation] Marine UI theme initialized.");
        }
    }
}
