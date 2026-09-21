using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 安全区适配（刘海 / 挖孔 / 圆角屏）。思路抄 OpenSekai 的 UniSafeAreaAdjuster：
    /// 读 <see cref="Screen.safeArea"/>，把屏幕像素安全区换算成父容器的 normalized anchors，
    /// 让贴边内容落进安全区；分辨率/安全区变化时自动重算，**不按机型打表**。
    ///
    /// 放置原则：文字与 HUD 进安全区；演奏区保持全屏出血（轨道居中、判定线在安全区内）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafe;
        private Vector2 lastScreen;
        private ScreenOrientation lastOrientation;

        /// <summary>
        /// 纯函数：屏幕像素安全区 + 屏幕尺寸 → 归一化锚点（可直接自检，不需要真机）。
        /// </summary>
        public static void ComputeAnchors(Rect safeArea, Vector2 screenSize,
                                         out Vector2 anchorMin, out Vector2 anchorMax)
        {
            float w = Mathf.Max(1f, screenSize.x);
            float h = Mathf.Max(1f, screenSize.y);
            anchorMin = new Vector2(Mathf.Clamp01(safeArea.xMin / w), Mathf.Clamp01(safeArea.yMin / h));
            anchorMax = new Vector2(Mathf.Clamp01(safeArea.xMax / w), Mathf.Clamp01(safeArea.yMax / h));
        }

        private void Awake() { rect = GetComponent<RectTransform>(); Apply(); }
        private void OnEnable() { Apply(); }

        private void Update()
        {
            if (Screen.safeArea != lastSafe
                || new Vector2(Screen.width, Screen.height) != lastScreen
                || Screen.orientation != lastOrientation)
                Apply();
        }

        private void Apply()
        {
            if (rect == null) rect = GetComponent<RectTransform>();
            if (rect == null) return;

            Rect safe = Screen.safeArea;
            Vector2 size = new Vector2(Screen.width, Screen.height);
            lastSafe = safe; lastScreen = size; lastOrientation = Screen.orientation;

            Vector2 min, max;
            ComputeAnchors(safe, size, out min, out max);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Debug.Log(string.Format("[SafeArea] 适配：safe={0} 屏幕={1} → anchors {2}..{3}", safe, size, min, max));
        }
    }
}
