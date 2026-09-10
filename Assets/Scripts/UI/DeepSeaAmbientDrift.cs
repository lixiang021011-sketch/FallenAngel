using FallenAngel.Core;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>
    /// FX005/M11 环境缓动：12 秒循环、位移不超过 12 逻辑单位；演奏/暂停期间完全冻结（不做全屏闪烁）。
    /// 挂在海洋底纹上，只动这一层的锚点位置。
    /// </summary>
    public sealed class DeepSeaAmbientDrift : MonoBehaviour
    {
        [SerializeField] private float cycleSeconds = 12f;
        [SerializeField] private float amplitude = 10f;
        private RectTransform rect;
        private Vector2 origin;

        private void Awake()
        {
            rect = transform as RectTransform;
            if (rect != null) origin = rect.anchoredPosition;
        }

        private void Update()
        {
            if (rect == null) return;
            var gm = GameManager.Instance;
            bool performing = gm != null && (gm.CurrentState == GameState.Playing || gm.CurrentState == GameState.Paused
                                             || gm.CurrentState == GameState.Loading);
            if (performing)
            {
                if (rect.anchoredPosition != origin) rect.anchoredPosition = origin;
                return;
            }
            float phase = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, cycleSeconds);
            // 双轴不同相位，读起来像缓慢水流；单轴位移不超过 amplitude（≤12）
            rect.anchoredPosition = origin + new Vector2(Mathf.Sin(phase) * amplitude * .6f,
                                                         Mathf.Cos(phase * .75f) * amplitude);
        }
    }
}
