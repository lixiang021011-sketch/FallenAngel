using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道配色唯一来源（游戏与谱面编辑器共用）。
    ///
    /// 两种模式，改 <see cref="Mode"/> 一处即可切换：
    ///   Teal（当前，2026-09-13 美术方向定稿）—— 同一青绿色域的**明度阶梯**，
    ///        五轨不靠色相区分；色盲环境下靠亮度差同样可分（最小亮度差 25，要求 ≥8）。
    ///        最暗一档亮度 130，保证音符压在深背景上的对比度 ≥3:1（需求表硬指标）。
    ///   Moonscraper —— 传统鼓谱语义（4 键红黄蓝绿 / 5 键绿红黄蓝橙），
    ///        打谱习惯的沿用；换成 Teal 后此映射退居备选，随时可切回。
    /// </summary>
    public static class LaneColors
    {
        /// <summary>配色模式</summary>
        public enum PaletteMode
        {
            /// <summary>青绿明度阶梯：不靠色相区分（当前美术方向）</summary>
            Teal = 0,
            /// <summary>Moonscraper 传统语义：红黄蓝绿 / 绿红黄蓝橙</summary>
            Moonscraper = 1,
        }

        /// <summary>当前配色模式（改这一处即可全局切换）</summary>
        public static PaletteMode Mode = PaletteMode.Teal;

        /// <summary>青绿明度阶梯：L0 最亮 → L4 最暗（#CFEFEA / #A8DEDA / #8AC9C6 / #6FB0B0 / #569494）</summary>
        private static readonly Color[] TealLadder =
        {
            new Color(0.812f, 0.937f, 0.918f), // #CFEFEA 亮度 232
            new Color(0.659f, 0.871f, 0.855f), // #A8DEDA 亮度 205
            new Color(0.541f, 0.788f, 0.776f), // #8AC9C6 亮度 180
            new Color(0.435f, 0.690f, 0.690f), // #6FB0B0 亮度 155
            new Color(0.337f, 0.580f, 0.580f), // #569494 亮度 130
        };

        /// <summary>4 键鼓谱基础色（Moonscraper：红 / 黄 / 蓝 / 绿）</summary>
        private static readonly Color[] DrumColors4 =
        {
            new Color(0.82f, 0.25f, 0.22f), // 红 - Lane 0 底鼓
            new Color(0.95f, 0.78f, 0.15f), // 黄 - Lane 1 军鼓
            new Color(0.25f, 0.50f, 0.95f), // 蓝 - Lane 2 踩镲/嗵鼓
            new Color(0.20f, 0.72f, 0.30f)  // 绿 - Lane 3 吊镲/嗵鼓
        };

        /// <summary>5 键吉他谱基础色（Moonscraper：绿 / 红 / 黄 / 蓝 / 橙）</summary>
        private static readonly Color[] GuitarColors5 =
        {
            new Color(0.20f, 0.72f, 0.30f), // 绿 - Lane 0
            new Color(0.82f, 0.25f, 0.22f), // 红 - Lane 1
            new Color(0.95f, 0.78f, 0.15f), // 黄 - Lane 2
            new Color(0.25f, 0.50f, 0.95f), // 蓝 - Lane 3
            new Color(0.98f, 0.55f, 0.10f)  // 橙 - Lane 4
        };

        /// <summary>按当前活动键数取某轨道颜色（越界自动钳制）</summary>
        public static Color GetLaneColor(int lane)
        {
            return GetLaneColor(lane, LaneLayout.ActiveLaneCount);
        }

        /// <summary>
        /// 按指定键数取某轨道颜色（越界自动钳制）。
        /// 谱面编辑器用它：编辑中的谱面键数不一定等于运行时的 ActiveLaneCount。
        /// </summary>
        public static Color GetLaneColor(int lane, int laneCount)
        {
            if (Mode == PaletteMode.Teal)
                return TealLadder[Mathf.Clamp(lane, 0, TealLadder.Length - 1)];

            Color[] palette = laneCount == 5 ? GuitarColors5 : DrumColors4;
            return palette[Mathf.Clamp(lane, 0, palette.Length - 1)];
        }

        /// <summary>当前活动轨道数（与 LaneLayout 同步）</summary>
        public static int LaneCount { get { return LaneLayout.ActiveLaneCount; } }
    }
}
