using System.Collections.Generic;
using UnityEngine;
using FallenAngel.Core;
using FallenAngel.InputSystem;

namespace FallenAngel.UI
{
    /// <summary>
    /// 触点涟漪特效控制器（Phigros 手感参考）：按下瞬间在实际触摸位置
    /// 生成扩散淡出的圆环涟漪，给输入以即时视觉反馈。
    /// 对象池复用，零资源依赖（RippleGraphic 运行时绘制）。
    /// </summary>
    public class HitFeedbackController : MonoBehaviour
    {
        private const int PoolSize = 16;

        [Header("涟漪外观")]
        [SerializeField] private Color rippleColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private float rippleStartSize = 80f;
        [SerializeField] private float rippleEndSize = 380f;
        [SerializeField] private float rippleDuration = 0.3f;

        private class RippleEntry
        {
            public RectTransform rt;
            public RippleGraphic graphic;
            public CanvasGroup canvasGroup;
            public Coroutine coroutine;
        }

        private readonly List<RippleEntry> pool = new List<RippleEntry>(PoolSize);
        private int nextIndex;
        private Canvas canvasForMapping;

        private void Start()
        {
            // 创建对象池（挂在本对象下，随场景重建销毁）
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject go = new GameObject($"Ripple_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(RippleGraphic), typeof(CanvasGroup));
                go.transform.SetParent(transform, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(rippleStartSize, rippleStartSize);
                RippleGraphic g = go.GetComponent<RippleGraphic>();
                g.raycastTarget = false;
                g.CenterColor = new Color(rippleColor.r, rippleColor.g, rippleColor.b, 0f);
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                go.SetActive(false);
                pool.Add(new RippleEntry { rt = rt, graphic = g, canvasGroup = cg });
            }

            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            if (InputManager.Instance != null)
                InputManager.Instance.OnLaneInput -= HandleLaneInput;
        }

        private void Subscribe()
        {
            if (InputManager.Instance == null) return;
            InputManager.Instance.OnLaneInput -= HandleLaneInput;
            InputManager.Instance.OnLaneInput += HandleLaneInput;
        }

        private void HandleLaneInput(object sender, LaneInputArgs e)
        {
            if (!e.isPressed) return;
            // 设置页"按键特效"开关（占位：门控涟漪；美术资源到位后扩展其他特效）
            if (!GameSettings.HitEffectEnabled) return;

            // 屏幕坐标 → Canvas 本地坐标（与输入判定同源）
            if (canvasForMapping == null)
            {
                canvasForMapping = FindObjectOfType<Canvas>();
                if (canvasForMapping == null) return;
            }
            RectTransform canvasRect = canvasForMapping.transform as RectTransform;
            if (canvasRect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.touchPos, null, out Vector2 local))
                return;

            SpawnRipple(local);
        }

        private void SpawnRipple(Vector2 canvasLocalPos)
        {
            RippleEntry entry = pool[nextIndex];
            nextIndex = (nextIndex + 1) % PoolSize;

            if (entry.coroutine != null)
            {
                StopCoroutine(entry.coroutine);
                entry.coroutine = null;
            }

            entry.rt.gameObject.SetActive(true);
            entry.rt.anchoredPosition = canvasLocalPos;
            entry.rt.sizeDelta = new Vector2(rippleStartSize, rippleStartSize); // 网格固定，扩散用 localScale
            entry.rt.localScale = Vector3.one;
            entry.graphic.CenterColor = rippleColor;
            if (entry.canvasGroup != null) entry.canvasGroup.alpha = 1f;

            entry.coroutine = StartCoroutine(AnimateRipple(entry));
        }

        private System.Collections.IEnumerator AnimateRipple(RippleEntry entry)
        {
            float timer = 0f;
            Color baseColor = rippleColor;

            // 帧数优化：扩散用 localScale、淡出用 CanvasGroup——全程不重建 UI 网格
            while (timer < rippleDuration)
            {
                timer += Time.unscaledDeltaTime;
                float tt = Mathf.Clamp01(timer / rippleDuration);
                float s = Mathf.Lerp(1f, rippleEndSize / rippleStartSize, tt);
                entry.rt.localScale = new Vector3(s, s, 1f);

                // 后半段淡出
                float alpha = tt < 0.5f ? 1f : 1f - (tt - 0.5f) * 2f;
                if (entry.canvasGroup != null) entry.canvasGroup.alpha = baseColor.a * alpha;
                yield return null;
            }

            entry.rt.localScale = Vector3.one;
            entry.rt.gameObject.SetActive(false);
            entry.coroutine = null;
        }
    }
}
