using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏界面视觉基准（单一来源）。
    /// 坐标系：Canvas 本地坐标，原点在画布中心，参考分辨率 1080×1920。
    ///
    /// 为什么需要它：判定位置与判定线的视觉位置曾分别硬编码在两处——
    /// NoteSpawner.judgeLineY = -400（画布中心系，等于距底 560）
    /// SceneBuilder 判定线 anchoredPosition = 400（底部锚点，等于距底 400）
    /// 两者差 160px（约屏高 8%）：玩家看到的白线不是音符被判定到的那条线。
    /// 现在两侧都只读本类，PlayVisualChecks 会断言「视觉线 == 判定线」。
    ///
    /// 取值口径：以美术方向稿为准——判定线距底 400，按键区是判定线往下的那一段
    /// （所以按键区顶边就是判定线；xlsx v0.1 里的「判定位置距底 560 / 按键区 500 高」
    /// 是更早一版，已被美术稿取代）。
    /// </summary>
    public static class PlayVisualSpec
    {
        /// <summary>参考画布宽（竖屏）</summary>
        public const float CanvasWidth = 1080f;

        /// <summary>参考画布高</summary>
        public const float CanvasHeight = 1920f;

        /// <summary>判定位置：距屏幕底部 400px（美术方向稿口径）</summary>
        public const float JudgeLineFromBottom = 400f;

        /// <summary>按键区高度：距屏幕底部 400px —— 按键区顶边即判定线（与美术稿一致）</summary>
        public const float KeyAreaHeight = 400f;

        /// <summary>音符生成位置：相对画布中心向上的 Y（在屏幕顶部之外）</summary>
        public const float SpawnYFromCenter = 1200f;

        /// <summary>判定线视觉厚度</summary>
        public const float JudgeLineThickness = 6f;

        /// <summary>距底高度 → 画布中心坐标系 Y</summary>
        public static float FromBottomToCenter(float fromBottom)
        {
            return fromBottom - CanvasHeight * 0.5f;
        }

        /// <summary>判定线在画布中心坐标系中的 Y（NoteSpawner / 判定位置使用）</summary>
        public static float JudgeLineY
        {
            get { return FromBottomToCenter(JudgeLineFromBottom); }
        }

        /// <summary>判定线在「底部锚点」下的 anchoredPosition.y（SceneBuilder 视觉使用）</summary>
        public static float JudgeLineAnchoredY
        {
            get { return JudgeLineFromBottom; }
        }
    }
}
