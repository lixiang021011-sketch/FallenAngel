using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>HUD 元素在**画布中心坐标系**里的占位（y 向上为正；画布 1080×1920）。</summary>
    public struct HudElement
    {
        public string name;      // 元素名（中文，便于自检报错时一眼看懂）
        public float x, y;       // 中心点
        public float w, h;       // 尺寸
        public bool optional;    // 只在特定状态出现（也参与重叠检查：它出现时不能压别人）

        public HudElement(string name, float x, float y, float w, float h, bool optional = false)
        {
            this.name = name;
            this.x = x; this.y = y; this.w = w; this.h = h;
            this.optional = optional;
        }

        public bool Overlaps(HudElement other)
        {
            return !(x + w * 0.5f <= other.x - other.w * 0.5f
                     || other.x + other.w * 0.5f <= x - w * 0.5f
                     || y + h * 0.5f <= other.y - other.h * 0.5f
                     || other.y + other.h * 0.5f <= y - h * 0.5f);
        }
    }

    /// <summary>
    /// 演奏页 HUD 的**布局单一事实来源**（前端清单 B5 总装，作者 2026-09-22 过稿）。
    ///
    /// 为什么单列一张表：以前每个元素的锚点/大小散落在 `SceneBuilder.CreateHUD` 里，
    /// 元素一多就互相压（这次实测出 6 处重叠：cut-in 压 combo、蓄力条压生命槽、800 宽的分数文本框
    /// 把右上一整片都占了）。收成表之后：
    ///   · `SceneBuilder` 按表建 UI；
    ///   · `HudLayoutChecks` 按表做"两两不重叠 + 不出安全区"断言 —— 以后谁挪元素，自检会红。
    ///
    /// 布局口径（过稿稿 `art_tools/review/hud_layout/hud_layout_compare.png`）：
    ///   顶部左右两列 × 三行 ——
    ///     左列 曲名 / 生命槽+数值 / 进度文本
    ///     右列 分数 / FEVER / 蓄力条
    ///   中部自上而下：cut-in 卡片 → combo 数字位 → COMBO 字样 → 判定文字 → 早晚描述
    ///   底部：判定线（y = PlayVisualSpec.JudgeLineY，不在此表内，由透视/判定共用）
    ///
    /// 素材说明（作者 2026-09-22）：**现有视觉全部是程序化占位**，将来会替换成可商用素材；
    /// 本表只描述"位置与尺寸"，不含任何贴图/皮肤依赖，换素材时不需要改布局。
    /// </summary>
    public static class HudLayout
    {
        public const float CanvasWidth = 1080f;
        public const float CanvasHeight = 1920f;

        // 顶部三行的中心高度：每行之间留 5~6px 空隙，整组在 20px 安全边距内（自检断言）
        /// <summary>顶部第一行（曲名 / 分数）</summary>
        public const float Row1Y = 889f;
        /// <summary>顶部第二行（生命槽+数值 / FEVER）</summary>
        public const float Row2Y = 833f;
        /// <summary>顶部第三行（进度文本 / 蓄力条）</summary>
        public const float Row3Y = 796f;

        public static readonly HudElement[] Elements =
        {
            // 顶部：进度条（横贯整屏；原来贴顶只留 10px，自检要求 ≥20px 边距 → 下移，肉眼无差别）
            new HudElement("进度条",     0f,   930f, 1040f, 20f),
            // 第一行：左曲名 / 右分数
            new HudElement("曲名",   -220f, Row1Y,  560f, 54f),
            new HudElement("分数",    290f, Row1Y,  420f, 54f),
            // 第二行：左生命槽+数值 / 右 FEVER
            new HudElement("生命槽", -290f, Row2Y,  420f, 30f),
            new HudElement("生命数值",  -5f, Row2Y,  130f, 34f),
            new HudElement("FEVER",   290f, Row2Y,  420f, 46f, true),
            // 第三行：左进度文本 / 右蓄力条
            new HudElement("进度文本", -280f, Row3Y, 440f, 34f),
            new HudElement("蓄力条",  290f, Row3Y,  420f, 12f, true),
            // 中部（判定反馈区，位置已过稿，本次不动）
            new HudElement("cut-in卡片", 0f, 470f,  820f, 150f, true),
            new HudElement("combo数字位", 0f, 250f, 300f, 96f),
            new HudElement("COMBO字样",  0f, 165f,  200f, 40f),
            new HudElement("判定文字",   0f, 100f,  520f, 72f),
            new HudElement("早晚描述",   0f,  40f,  320f, 36f),
        };

        /// <summary>顶部锚点用的 y（画布中心系 → anchoredPosition，锚点在画布顶部时）</summary>
        public static float TopAnchoredY(float centerY)
        {
            return centerY - CanvasHeight * 0.5f;
        }

        /// <summary>右侧锚点用的 x（画布中心系 → anchoredPosition，锚点在画布右侧时）</summary>
        public static float RightAnchoredX(float centerX)
        {
            return centerX - CanvasWidth * 0.5f;
        }

        public static HudElement Get(string name)
        {
            for (int i = 0; i < Elements.Length; i++)
                if (Elements[i].name == name) return Elements[i];
            Debug.LogWarning("[HudLayout] 表里没有这个元素：" + name);
            return new HudElement(name, 0f, 0f, 0f, 0f);
        }
    }
}
