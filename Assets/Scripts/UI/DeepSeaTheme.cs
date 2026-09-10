using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FallenAngel.UI
{
    /// <summary>显示样式入口；不改按钮行为、音符颜色或玩法数据。</summary>
    public static class DeepSeaTheme
    {
        // UI001：page_redesign_0909 统一规范
        public static readonly Color Background = Hex(0x101A22);
        public static readonly Color Card = new Color(0.082f, 0.141f, 0.180f, 0.94f);
        public static readonly Color Ink = Hex(0xE5E9E4);
        public static readonly Color Muted = Hex(0x9BABAF);
        public static readonly Color Accent = Hex(0x8DC6D0);
        public static readonly Color Owned = new Color(0.13f, 0.36f, 0.29f, 1);
        public static readonly Color Line = new Color(0.553f, 0.776f, 0.816f, 0.55f);
        public static readonly Color Paid = Hex(0xCFB47B);
        public static readonly Color Danger = Hex(0xC38680);

        public const int TitleSize = 60;
        public const int ActionSize = 36;
        public const int BodySize = 32;
        public const int CaptionSize = 28;
        public const float SafeMargin = 60f;
        public const float MinHit = 96f;

        public enum ButtonRole { Default, Primary, Secondary, Danger }

        static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        /// <summary>主/次/危险按钮角色。命中区仍是透明 Image，视觉走切角面。</summary>
        public static void ApplyRole(Button button, ButtonRole role)
        {
            if (button == null) return;
            StyleButton(button);

            Color fill = Card;
            Color label = Ink;
            Color frame = Line;
            if (role == ButtonRole.Primary)
            {
                fill = Ink;
                label = Background;
                frame = Ink;
            }
            else if (role == ButtonRole.Secondary)
            {
                fill = Card;
                label = Ink;
                frame = Accent;
            }
            else if (role == ButtonRole.Danger)
            {
                fill = Danger;
                label = Ink;
                frame = Danger;
            }
            else
            {
                fill = Accent;
                label = Ink;
                frame = Line;
            }

            var hit = button.GetComponent<Image>();
            if (hit != null) hit.color = fill;
            RefineButton(button);

            var surface = button.transform.Find("SeaSurface");
            if (surface != null)
            {
                var graphic = surface.GetComponent<DeepSeaGraphic>();
                if (graphic != null) graphic.color = fill;
            }
            var frameTf = button.transform.Find("SeaFrame");
            if (frameTf != null)
            {
                var graphic = frameTf.GetComponent<DeepSeaGraphic>();
                if (graphic != null)
                {
                    graphic.color = role == ButtonRole.Primary ? new Color(0, 0, 0, 0) : frame;
                    graphic.SetVerticesDirty();
                }
            }

            foreach (var text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.color = label;
                text.fontSize = ActionSize;
            }
        }

        /// <summary>Opt-in production controls; existing pages can migrate independently.</summary>
        public static void RefineButton(Button button)
        {
            if(button.transform.Find("SeaSurface")!=null)return;
            var hitArea=button.GetComponent<Image>();
            if(hitArea==null)return;
            var surface=Graphic(button.transform,"SeaSurface",DeepSeaGraphic.Shape.RoundedSurface);
            surface.color=hitArea.color;surface.transform.SetAsFirstSibling();
            hitArea.color=Color.clear; // The rectangular Image remains the generous input target.
            button.targetGraphic=surface;
            var frame=button.transform.Find("SeaFrame");
            if(frame!=null){var graphic=frame.GetComponent<DeepSeaGraphic>();graphic.shape=DeepSeaGraphic.Shape.RoundedFrame;graphic.SetVerticesDirty();}
            var state=button.colors;state.highlightedColor=new Color(1.12f,1.12f,1.12f);state.pressedColor=new Color(.68f,.78f,.8f);
            state.selectedColor=new Color(1.08f,1.12f,1.12f);state.disabledColor=new Color(.4f,.46f,.49f);state.fadeDuration=.1f;button.colors=state;
        }

        public static void Backdrop(Transform parent)
        {
            if(parent.Find("DeepSeaBackdrop")!=null)return;
            var art=Graphic(parent,"DeepSeaBackdrop",DeepSeaGraphic.Shape.Ocean);
            art.transform.SetAsFirstSibling();
        }
        /// <summary>UI005 图标：按父级左上角定位（x/y 为距左上角偏移），线宽由形状统一决定。</summary>
        public static DeepSeaGraphic Icon(Transform parent,string name,DeepSeaGraphic.Shape shape,float x,float y,float w,float h,Color tint)
        {
            var graphic=Graphic(parent,name,shape);
            var rect=(RectTransform)graphic.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);
            rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);
            rect.sizeDelta=new Vector2(w,h);
            graphic.color=tint;graphic.SetVerticesDirty();
            return graphic;
        }

        /// <summary>UI005 图标 + 文本一行（图标在左、文本在右），返回文本组件供写入动态值。</summary>
        public static TextMeshProUGUI IconLabel(Transform parent,string name,DeepSeaGraphic.Shape icon,string text,
            float x,float y,float w,float h,int size,TMP_FontAsset font,Color? tint=null)
        {
            float box=Mathf.Min(h,40f);
            Icon(parent,name+"Icon",icon,x,y+(h-box)*.5f,box,box,tint??Accent);
            var go=new GameObject(name+"Text",typeof(RectTransform),typeof(TextMeshProUGUI));
            var rect=(RectTransform)go.transform;
            rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);
            rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x+box+14f,-y);
            rect.sizeDelta=new Vector2(w-box-14f,h);
            var label=go.GetComponent<TextMeshProUGUI>();
            label.font=font;label.text=text;label.fontSize=size;label.color=Ink;
            label.alignment=TextAlignmentOptions.Left;label.raycastTarget=false;label.enableWordWrapping=true;
            return label;
        }

        /// <summary>
        /// 图标 + 文本作为「一个整体」在按钮内居中：图标与文字相邻成组，
        /// 避免图标贴左边、文字按整块居中造成的错位观感（宽按钮尤其明显）。
        /// </summary>
        public static void CenterIconGroup(RectTransform host, TextMeshProUGUI label, string iconName,
            float iconW, float iconH, float inset = 20f)
        {
            if (host == null || label == null) return;
            float w = Mathf.Max(1f, host.rect.width), h = Mathf.Max(1f, host.rect.height);
            var iconRect = host.Find(iconName) as RectTransform;
            if (iconRect == null) return;
            const float gap = 14f;
            label.ForceMeshUpdate();
            float textW = Mathf.Clamp(label.preferredWidth, 0f, Mathf.Max(0f, w - inset * 2f - iconW - gap));
            float groupW = iconW + gap + textW;
            float startX = Mathf.Max(inset, (w - groupW) * .5f);

            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 1);
            iconRect.pivot = new Vector2(0, 1);
            iconRect.sizeDelta = new Vector2(iconW, iconH);
            iconRect.anchoredPosition = new Vector2(startX, -(h - iconH) * .5f);

            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
            label.rectTransform.pivot = new Vector2(0, 1);
            label.rectTransform.sizeDelta = new Vector2(Mathf.Max(textW, 1f), h);
            label.rectTransform.anchoredPosition = new Vector2(startX + iconW + gap, 0f);
            label.alignment = TextAlignmentOptions.Left;
        }

        /// <summary>UI002/UI004 通用按钮：主/次/危险角色 + 可选前置图标（图标随角色自动配色）。命中区由调用方给足。</summary>
        public static Button Button(Transform parent,string name,string text,float x,float y,float w,float h,
            ButtonRole role,TMP_FontAsset font,Action onClick,DeepSeaGraphic.Shape? icon=null)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            var rect=(RectTransform)go.transform;
            rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);
            rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);
            rect.sizeDelta=new Vector2(w,h);
            go.GetComponent<Image>().color=Card;

            var labelGo=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));
            var labelRect=(RectTransform)labelGo.transform;
            labelRect.SetParent(rect,false);
            labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(icon.HasValue?68f:14f,6f);
            labelRect.offsetMax=new Vector2(-14f,-6f);
            var label=labelGo.GetComponent<TextMeshProUGUI>();
            label.font=font;label.text=text;label.fontSize=ActionSize;label.color=Ink;
            label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;label.enableWordWrapping=true;

            var button=go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic=go.GetComponent<Image>();
            button.onClick.AddListener(()=>onClick?.Invoke());
            ApplyRole(button,role);
            label.fontSize=ActionSize;
            if(icon.HasValue)
            {
                var graphic=Icon(rect,"ButtonIcon",icon.Value,20f,(h-36f)*.5f,36f,36f,label.color);
                graphic.transform.SetSiblingIndex(1);
                CenterIconGroup(rect,label,"ButtonIcon",36f,36f);
            }
            return button;
        }

        /// <summary>UI003 卡片状态：常态/选中/锁定/已通过/空态。除颜色外另有角标或描边变体，不靠颜色单独区分。</summary>
        public enum CardState { Normal, Selected, Locked, Cleared, Empty }

        /// <summary>UI003 圆角卡片面：把 Box 的矩形 Image 让位给圆角面（Image 转透明但仍作命中区）。</summary>
        public static void CardSurface(Transform box, Color fill, bool frame = true)
        {
            if (box == null || box.Find("CardSurface") != null) return;
            var image = box.GetComponent<Image>();
            if (image != null) image.color = Color.clear;
            var surface = Graphic(box, "CardSurface", DeepSeaGraphic.Shape.RoundedSurface);
            surface.color = fill;
            surface.transform.SetAsFirstSibling();
            if (!frame) return;
            var edge = Graphic(box, "CardFrame", DeepSeaGraphic.Shape.RoundedFrame);
            edge.color = Line;
            edge.transform.SetSiblingIndex(1);
        }

        public static void ApplyCardState(Transform card,CardState state)
        {
            if(card==null)return;
            var mark=card.Find("StateMark");
            if(mark!=null)UnityEngine.Object.Destroy(mark.gameObject);
            var frameTf=card.Find("SeaFrame");
            var frame=frameTf!=null?frameTf.GetComponent<DeepSeaGraphic>():null;
            // 常态不动框型：节点类靠 variant 区分普通/交汇/终点；选中只提亮描边
            if(state==CardState.Selected&&frame!=null){frame.color=Accent;frame.SetVerticesDirty();}
            if(state==CardState.Selected||state==CardState.Normal)return;
            var rect=(RectTransform)card;
            float w=rect.sizeDelta.x,h=rect.sizeDelta.y;
            var shape=state==CardState.Cleared?DeepSeaGraphic.Shape.Check:
                state==CardState.Locked?DeepSeaGraphic.Shape.Lock:DeepSeaGraphic.Shape.Empty;
            var tint=state==CardState.Cleared?Ink:Muted;
            // 窄卡片把标记放左侧，避免压住居中文字；宽卡片用右上角标
            if(w<260f)Icon(card,"StateMark",shape,8f,(h-24f)*.5f,24f,24f,tint);
            else Icon(card,"StateMark",shape,Mathf.Max(8f,w-40f),10f,26f,28f,tint);
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
            var frame=Graphic(button.transform,"SeaFrame",DeepSeaGraphic.Shape.RoundedFrame);
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
                    // 图标与文案成组居中：宽格子不再出现「图标贴左、文字居中」的错位
                    CenterIconGroup((RectTransform)button.transform,label,"EquipmentSymbol",shop?90f:55f,80f,12f);
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

        /// <summary>现金栏芯片：硬币图标 + 文本。返回文本组件供调用方写入金额。</summary>
        public static TextMeshProUGUI CashBar(Transform parent,float x,float y,int cash,TMP_FontAsset font)
        {
            var go=new GameObject("CashBar",typeof(RectTransform),typeof(Image));
            var rect=(RectTransform)go.transform;rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(240,56);
            go.GetComponent<Image>().color=Card;
            var coin=Graphic(rect,"Coin",DeepSeaGraphic.Shape.Coin);
            var coinRect=(RectTransform)coin.transform;coinRect.anchorMin=coinRect.anchorMax=new Vector2(0,.5f);
            coinRect.pivot=new Vector2(0,.5f);coinRect.anchoredPosition=new Vector2(16,0);coinRect.sizeDelta=new Vector2(30,30);
            coin.color=Paid;
            var label=new GameObject("CashText",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            var labelRect=(RectTransform)label.transform;labelRect.SetParent(rect,false);
            labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(58,4);labelRect.offsetMax=new Vector2(-10,-4);
            label.font=font;label.fontSize=26;label.alignment=TextAlignmentOptions.Left;
            label.raycastTarget=false;label.text=cash.ToString();
            return label;
        }

        /// <summary>标题条装饰线：与标题同锚点、置于其正下方（两端横线 + 中央竖刻）。</summary>
        public static void TitleRule(TextMeshProUGUI title)
        {
            var parent=title.rectTransform.parent as RectTransform;
            if(parent==null)return;
            var rule=Graphic(parent,"TitleRule",DeepSeaGraphic.Shape.Rule);
            var rect=(RectTransform)rule.transform;
            rect.anchorMin=rect.anchorMax=title.rectTransform.anchorMin;
            rect.pivot=title.rectTransform.pivot;
            rect.anchoredPosition=title.rectTransform.anchoredPosition-new Vector2(0,title.rectTransform.sizeDelta.y*.5f+14);
            rect.sizeDelta=new Vector2(title.rectTransform.sizeDelta.x,12);
            rule.color=Line;
        }
    }
}
