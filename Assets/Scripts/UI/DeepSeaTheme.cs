using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FallenAngel.UI
{
    /// <summary>显示样式入口；不改按钮行为、音符颜色或玩法数据。</summary>
    public static class DeepSeaTheme
    {
        public static readonly Color Background=new Color(.018f,.049f,.075f,1);
        public static readonly Color Card=new Color(.037f,.09f,.12f,.94f);
        public static readonly Color Ink=new Color(.87f,.94f,.91f,1);
        public static readonly Color Accent=new Color(.09f,.32f,.36f,1);
        public static readonly Color Owned=new Color(.13f,.36f,.29f,1);
        public static readonly Color Line=new Color(.38f,.68f,.67f,.55f);

        public static void Backdrop(Transform parent)
        {
            if(parent.Find("DeepSeaBackdrop")!=null)return;
            var art=Graphic(parent,"DeepSeaBackdrop",DeepSeaGraphic.Shape.Ocean);
            art.transform.SetAsFirstSibling();
        }
        public static DeepSeaGraphic Graphic(Transform parent,string name,DeepSeaGraphic.Shape shape)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(DeepSeaGraphic));
            var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphic=go.GetComponent<DeepSeaGraphic>();graphic.shape=shape;graphic.color=Line;graphic.raycastTarget=false;
            return graphic;
        }
        public static void StyleButton(Button button)
        {
            if(button==null || button.transform.Find("SeaFrame")!=null)return;
            var frame=Graphic(button.transform,"SeaFrame",DeepSeaGraphic.Shape.Frame);
            frame.transform.SetAsFirstSibling();
            ColorBlock colors=button.colors;
            colors.normalColor=Color.white;colors.highlightedColor=new Color(1.25f,1.3f,1.3f,1);
            colors.pressedColor=new Color(.68f,.9f,.91f,1);colors.selectedColor=new Color(1.1f,1.22f,1.2f,1);
            colors.disabledColor=new Color(.48f,.53f,.56f,.8f);colors.fadeDuration=.12f;
            button.colors=colors;
            if(button.name.StartsWith("Equip_") || button.name.StartsWith("ShopBuy_"))
            {
                bool shop=button.name.StartsWith("ShopBuy_");
                var icon=Graphic(button.transform,"EquipmentSymbol",DeepSeaGraphic.Shape.Equipment);
                string id=button.name.Substring(button.name.LastIndexOf('_')+1);
                int.TryParse(id.TrimStart('E'),out int index);icon.variant=index;icon.color=Ink;
                var rt=(RectTransform)icon.transform;rt.anchorMin=rt.anchorMax=new Vector2(0,.5f);
                rt.pivot=new Vector2(0,.5f);rt.anchoredPosition=new Vector2(12,0);rt.sizeDelta=new Vector2(shop?90:55,80);
                var label=button.GetComponentInChildren<TextMeshProUGUI>();
                if(label!=null)
                {
                    float left=shop?116:70;
                    label.rectTransform.anchoredPosition=new Vector2(left,-5);
                    label.rectTransform.sizeDelta=new Vector2(((RectTransform)button.transform).sizeDelta.x-left-8,((RectTransform)button.transform).sizeDelta.y-10);
                }
            }
        }
        public static void RoomIcon(Button button,string type)
        {
            var shape=type=="SHOP"?DeepSeaGraphic.Shape.Shop:type=="STAGE"?DeepSeaGraphic.Shape.Battle:
                type=="FINAL"?DeepSeaGraphic.Shape.Final:type=="EMPTY"?DeepSeaGraphic.Shape.Empty:DeepSeaGraphic.Shape.Beacon;
            var icon=Graphic(button.transform,"RoomSymbol",shape);
            var rect=(RectTransform)icon.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);
            rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(8,0);rect.sizeDelta=new Vector2(45,55);
            icon.color=Ink;
            var label=button.GetComponentInChildren<TextMeshProUGUI>();
            if(label!=null){label.rectTransform.anchoredPosition=new Vector2(56,-5);label.rectTransform.sizeDelta=new Vector2(176,120);label.fontSize=18;}
        }
    }
}
