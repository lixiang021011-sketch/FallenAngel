using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>底部调参：三档判定按钮、长按、滑条、预设槽、实机绑定。</summary>
    public sealed class ArtWorkbenchPanel : MonoBehaviour
    {
        public ArtWorkbenchHost Host { get; private set; }
        TextMeshProUGUI status;
        TextMeshProUGUI slotLabel;
        int slotIndex;
        Slider widthSlider;
        Slider burstSlider;
        Slider pulseSlider;
        Toggle liveToggle;
        bool applying;

        public void Build(ArtWorkbenchHost host)
        {
            Host = host;
            var bg = ArtWorkbenchUi.Panel(transform, "PanelBg", new Color(0.06f, 0.11f, 0.14f, 0.96f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);

            ArtWorkbenchUi.Label(bg.transform, "Title", "参数", 24, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -8f), new Vector2(-24f, -40f));

            // 编译修复（2026-09-20，AI 协作侧）：元组数组里的 lambda 推不出类型（CS0826），显式给出元素类型
            RowButtons(bg.transform, 48f, new (string, Color, UnityEngine.Events.UnityAction)[]
            {
                ("Perfect", ArtWorkbenchTokens.Perfect, () => Host.FireGrade(ArtWorkbenchGrade.Perfect)),
                ("Great", ArtWorkbenchTokens.Great, () => Host.FireGrade(ArtWorkbenchGrade.Great)),
                ("Miss", ArtWorkbenchTokens.Miss, () => Host.FireGrade(ArtWorkbenchGrade.Miss))
            });
            RowButtons(bg.transform, 112f, new (string, Color, UnityEngine.Events.UnityAction)[]
            {
                ("长按开始", ArtWorkbenchTokens.Paid, () => Host.FireHold(true)),
                ("长按结束", ArtWorkbenchTokens.Card, () => Host.FireHold(false)),
                ("生成音符", ArtWorkbenchTokens.Card, () => Host.SpawnTap())
            });

            widthSlider = SliderRow(bg.transform, 176f, "音符宽", 80f, 260f, Host.Preset.noteWidth, v => { Host.Preset.noteWidth = v; Host.ApplyPreset(); });
            burstSlider = SliderRow(bg.transform, 224f, "爆发", 0.4f, 2.2f, Host.Preset.hitBurstScale, v => { Host.Preset.hitBurstScale = v; Host.ApplyPreset(); });
            pulseSlider = SliderRow(bg.transform, 272f, "长按脉冲", 0.4f, 5f, Host.Preset.holdPulseSpeed, v => { Host.Preset.holdPulseSpeed = v; Host.ApplyPreset(); });

            RowButtons(bg.transform, 320f, new (string, Color, UnityEngine.Events.UnityAction)[]
            {
                ("保存", ArtWorkbenchTokens.Accent, () => Host.SaveSlot(CurrentSlot())),
                ("读取", ArtWorkbenchTokens.Card, () => Host.LoadSlot(CurrentSlot())),
                ("切槽", ArtWorkbenchTokens.Card, CycleSlot)
            });

            slotLabel = ArtWorkbenchUi.Label(bg.transform, "Slot", "槽位 " + CurrentSlot(), 22, ArtWorkbenchTokens.Ink,
                TextAlignmentOptions.MidlineRight, new Vector2(0.55f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -320f), new Vector2(-24f, -368f));

            liveToggle = ArtWorkbenchUi.Toggle(bg.transform, "Live", "绑定实机事件", Host.Preset.bindLiveEvents,
                on => { Host.Preset.bindLiveEvents = on; Host.SetLiveBind(on); },
                new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(24f, -368f), new Vector2(-8f, -412f));

            status = ArtWorkbenchUi.Label(bg.transform, "Status", "", 20, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 8f), new Vector2(-24f, 40f));
            SetStatus("沙盒事件就绪");
        }

        public void SetStatus(string text)
        {
            if (status != null) status.text = text;
        }

        public void SyncFromPreset()
        {
            applying = true;
            if (liveToggle != null) liveToggle.isOn = Host.Preset.bindLiveEvents;
            if (widthSlider != null) widthSlider.value = Host.Preset.noteWidth;
            if (burstSlider != null) burstSlider.value = Host.Preset.hitBurstScale;
            if (pulseSlider != null) pulseSlider.value = Host.Preset.holdPulseSpeed;
            applying = false;
            if (slotLabel != null) slotLabel.text = "槽位 " + CurrentSlot();
        }

        string CurrentSlot()
        {
            return ArtWorkbenchStore.Slots[Mathf.Clamp(slotIndex, 0, ArtWorkbenchStore.Slots.Length - 1)];
        }

        void CycleSlot()
        {
            slotIndex = (slotIndex + 1) % ArtWorkbenchStore.Slots.Length;
            if (slotLabel != null) slotLabel.text = "槽位 " + CurrentSlot();
            Host.SetStatus("当前槽位 " + CurrentSlot());
        }

        void RowButtons(Transform parent, float y, (string label, Color color, UnityEngine.Events.UnityAction click)[] items)
        {
            float gap = 12f;
            float w = (ArtWorkbenchTokens.CanvasWidth - 48f - gap * (items.Length - 1)) / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                float x = 24f + i * (w + gap);
                ArtWorkbenchUi.Button(parent, items[i].label, items[i].label, items[i].color, items[i].click,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero)
                    .GetComponent<RectTransform>().anchoredPosition = new Vector2(x, -y);
                var rt = parent.Find(items[i].label) as RectTransform;
                if (rt != null)
                {
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = new Vector2(w, 52f);
                    rt.anchoredPosition = new Vector2(x, -y);
                }
            }
        }

        Slider SliderRow(Transform parent, float y, string label, float min, float max, float value, UnityEngine.Events.UnityAction<float> changed)
        {
            ArtWorkbenchUi.Label(parent, label + "L", label, 22, ArtWorkbenchTokens.Muted,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero)
                .rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 140f);
            var lab = parent.Find(label + "L") as RectTransform;
            if (lab != null)
            {
                lab.pivot = new Vector2(0f, 1f);
                lab.anchoredPosition = new Vector2(24f, -y);
                lab.sizeDelta = new Vector2(140f, 40f);
            }
            var slider = ArtWorkbenchUi.Slider(parent, label + "S", min, max, value, v =>
            {
                if (!applying) changed(v);
            }, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            var srt = slider.GetComponent<RectTransform>();
            srt.pivot = new Vector2(0f, 1f);
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(170f, -y);
            srt.sizeDelta = new Vector2(860f, 36f);
            return slider;
        }
    }
}
