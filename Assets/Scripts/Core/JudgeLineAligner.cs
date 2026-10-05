using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 把**画出来的判定线**对齐到**判定位置**（2026-10-04 修 bug）。
    ///
    /// 背景：判定位置用画布中心系（<see cref="PlayVisualSpec.JudgeLineY"/> = 400 − 1920/2 = −560），
    /// 而画线用的是"距底 400"的底部锚点系（<see cref="PlayVisualSpec.JudgeLineAnchoredY"/>）——
    /// 两者**只在画布实际高度恰为 1920（参考分辨率）时才重合**。
    /// 真机 1280×2772 → 缩放 1.308 → 画布高 2119.5 UI：画出来的线比判定位置低约 100 UI
    /// （≈130px ≈ 0.7 拍）。玩家盯着"看到的线"打，就会一直早一截（作者 2026-10-04 反馈"对不上"）。
    ///
    /// 修法：画线位置 = 父容器高度 ÷ 2 + JudgeLineY —— 与判定用同一个数，随真实画布自适应。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class JudgeLineAligner : MonoBehaviour
    {
        private RectTransform lineRect;

        private void Awake()
        {
            lineRect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            Align();
        }

        /// <summary>画布尺寸会在启动/旋转/安全区变化后确定，晚一帧再对一次即可（不是每帧重排）。</summary>
        private void LateUpdate()
        {
            Align();
        }

        private void Align()
        {
            if (lineRect == null) return;
            RectTransform parent = lineRect.parent as RectTransform;
            if (parent == null) return;

            float target = parent.rect.height * 0.5f + PlayVisualSpec.JudgeLineY;
            if (Mathf.Abs(lineRect.anchoredPosition.y - target) < 0.5f) return;   // 已经对齐，不碰布局
            lineRect.anchoredPosition = new Vector2(lineRect.anchoredPosition.x, target);
        }
    }
}
