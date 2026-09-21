using TMPro;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// 左上角 FPS / 帧耗时读数（真机性能排查用）。有了数字才能决定砍哪里
    /// （overdraw / Canvas 重建 / 逻辑），否则只能凭感觉。每 0.5s 更新一次文本。
    /// </summary>
    public class DebugFpsOverlay : MonoBehaviour
    {
        /// <summary>总开关（关掉就不创建读数）</summary>
        public static bool Enabled = true;

        private TextMeshProUGUI text;
        private float accum;
        private int frames;
        private float worstMs;

        public static DebugFpsOverlay Install(RectTransform parent, TMP_FontAsset font)
        {
            if (!Enabled || parent == null) return null;
            Transform existing = parent.Find("DebugFps");
            if (existing != null) return existing.GetComponent<DebugFpsOverlay>();

            GameObject go = new GameObject("DebugFps", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -8f);
            rt.sizeDelta = new Vector2(360f, 52f);

            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.font = font != null ? font : TMP_Settings.defaultFontAsset;
            t.fontSize = 26f;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            t.raycastTarget = false;
            t.text = "FPS --";

            var overlay = go.AddComponent<DebugFpsOverlay>();
            overlay.text = t;
            return overlay;
        }

        private void Update()
        {
            accum += Time.unscaledDeltaTime;
            frames++;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > worstMs) worstMs = ms;
            if (accum < 0.5f) return;

            if (text != null)
                text.text = string.Format("{0:0} fps  {1:0.0} ms 峰值", frames / accum, worstMs);
            accum = 0f; frames = 0; worstMs = 0f;
        }
    }

    /// <summary>
    /// 运行时自动装配：找到 HUD 容器 → 挂安全区适配 + FPS 读数。
    /// 不依赖场景序列化、也不需要重建场景——真机适配与性能读数开箱即用。
    /// </summary>
    public static class HudRuntimeFittings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            HUDController hud = Object.FindObjectOfType<HUDController>();
            if (hud == null) return;
            RectTransform rt = hud.transform as RectTransform;
            if (rt == null) return;

            if (hud.GetComponent<FallenAngel.Core.SafeAreaFitter>() == null)
                hud.gameObject.AddComponent<FallenAngel.Core.SafeAreaFitter>();

            TextMeshProUGUI anyText = null;
            foreach (var t in hud.GetComponentsInChildren<TextMeshProUGUI>(true)) { anyText = t; break; }
            DebugFpsOverlay.Install(rt, anyText != null ? anyText.font : null);
        }
    }
}
