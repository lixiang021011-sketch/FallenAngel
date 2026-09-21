using TMPro;
using UnityEngine;
using System.Collections.Generic;

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
        private readonly List<float> samples = new List<float>(256);
        private float rebuildRate;

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
            // 贴左上角会被状态栏/刘海盖住：按安全区上边再往下让一点，并把读数放在一个半透明底上
            Vector2 safeMin, safeMax;
            FallenAngel.Core.SafeAreaFitter.ComputeAnchors(
                Screen.safeArea, new Vector2(Screen.width, Screen.height), out safeMin, out safeMax);
            float notchInset = Mathf.Max(0f, (1f - safeMax.y) * Screen.height / Mathf.Max(1f, parent.rect.height));
            rt.anchoredPosition = new Vector2(10f, -(8f + notchInset * parent.rect.height));
            rt.sizeDelta = new Vector2(400f, 56f);

            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.font = font != null ? font : TMP_Settings.defaultFontAsset;
            t.fontSize = 26f;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            t.raycastTarget = false;
            t.text = "FPS --";

            // 半透明底：纯文字在深色背景上够看，但在亮色 UI（选歌/结算）上会被淹没
            GameObject bg = new GameObject("Bg", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            bg.transform.SetParent(go.transform, false);
            RectTransform brt = (RectTransform)bg.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            UnityEngine.UI.Image img = bg.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0.45f);
            img.raycastTarget = false;
            t.transform.SetAsLastSibling();   // 文字压在底上

            var overlay = go.AddComponent<DebugFpsOverlay>();
            overlay.text = t;
            // 每秒 Canvas 重建次数：这是"每帧重建网格"最直接的指标（HUD 文本/轨迹/带子都会触发）
            Canvas.willRenderCanvases += CountCanvasRebuild;
            return overlay;
        }

        private static int canvasRebuilds;
        private static int canvasRebuildsAtSample;
        private static void CountCanvasRebuild() { canvasRebuilds++; }

        private void Update()
        {
            accum += Time.unscaledDeltaTime;
            frames++;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > worstMs) worstMs = ms;
            samples.Add(ms);
            if (accum < 0.5f) return;

            samples.Sort();
            float p95 = samples.Count > 0 ? samples[Mathf.Clamp((int)(samples.Count * 0.95f), 0, samples.Count - 1)] : 0f;
            rebuildRate = (canvasRebuilds - canvasRebuildsAtSample) / accum;
            canvasRebuildsAtSample = canvasRebuilds;

            if (text != null)
                text.text = string.Format("{0:0} fps  P95 {1:0.0}ms  峰值 {2:0.0}ms  重建 {3:0.0}/s",
                    frames / accum, p95, worstMs, rebuildRate);
            accum = 0f; frames = 0; worstMs = 0f;
            samples.Clear();
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
            // 关键：FindObjectOfType 默认**不找未激活对象**，而 HUD 在菜单状态是隐藏的——
            // 上一版真机"什么都没装上"就是这个原因（hud == null 直接 return）。必须传 includeInactive。
            HUDController hud = Object.FindObjectOfType(typeof(HUDController), true) as HUDController;

            // FPS 读数挂在 **Canvas** 上（不是 HUD）：HUD 在菜单/加载状态是隐藏的，
            // 挂在它下面会导致"只有进局内才看得见"，真机上很容易被误判成"没生效"。
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            RectTransform host = canvas != null ? canvas.transform as RectTransform : null;
            if (host == null && hud != null) host = hud.transform as RectTransform;
            if (host == null)
            {
                Debug.LogWarning("[HudFittings] 没找到 HUD/Canvas：安全区适配与 FPS 读数未安装");
                return;
            }

            bool fitted = false;
            if (hud != null && hud.GetComponent<FallenAngel.Core.SafeAreaFitter>() == null)
            {
                hud.gameObject.AddComponent<FallenAngel.Core.SafeAreaFitter>();
                fitted = true;
            }
            else if (hud != null)
            {
                fitted = true;   // 已有
            }

            TextMeshProUGUI anyText = host.GetComponentInChildren<TextMeshProUGUI>(true);
            DebugFpsOverlay overlay = DebugFpsOverlay.Install(host, anyText != null ? anyText.font : null);
            Debug.Log(string.Format("[HudFittings] 安装完成：HUD={0} 宿主={1} 安全区适配={2} FPS读数={3}",
                hud != null, host.name, fitted, overlay != null));
        }
    }
}
