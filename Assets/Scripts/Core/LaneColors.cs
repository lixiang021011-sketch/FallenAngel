using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道配色统一定义（唯一来源）。
    /// 语义对齐 Moonscraper 标准，按当前活动键数选板：
    ///   4 键（鼓谱）：红黄蓝绿 —— lane 0 红（底鼓）/ 1 黄（军鼓）/ 2 蓝（踩镲/嗵）/ 3 绿（吊镲/嗵）
    ///   5 键（吉他谱）：绿红黄蓝橙 —— lane 0 绿 / 1 红 / 2 黄 / 3 蓝 / 4 橙
    /// 打谱（Moonscraper）与玩游戏（下落式）共用同一套颜色语言；
    /// 若与编辑器实际显示有出入，只需改本文件。
    /// </summary>
    public static class LaneColors
    {
        /// <summary>4 键鼓谱基础色（不含透明度）</summary>
        private static readonly Color[] DrumColors4 = new Color[]
        {
            new Color(0.82f, 0.25f, 0.22f), // 红 - Lane 0 底鼓
            new Color(0.95f, 0.78f, 0.15f), // 黄 - Lane 1 军鼓
            new Color(0.25f, 0.50f, 0.95f), // 蓝 - Lane 2 踩镲/嗵鼓
            new Color(0.20f, 0.72f, 0.30f)  // 绿 - Lane 3 吊镲/嗵鼓
        };

        /// <summary>5 键吉他谱基础色（绿红黄蓝橙，Moonscraper 标准）</summary>
        private static readonly Color[] GuitarColors5 = new Color[]
        {
            new Color(0.20f, 0.72f, 0.30f), // 绿 - Lane 0
            new Color(0.82f, 0.25f, 0.22f), // 红 - Lane 1
            new Color(0.95f, 0.78f, 0.15f), // 黄 - Lane 2
            new Color(0.25f, 0.50f, 0.95f), // 蓝 - Lane 3
            new Color(0.98f, 0.55f, 0.10f)  // 橙 - Lane 4
        };

        /// <summary>
        /// 取某轨道的颜色（越界自动钳制到合法范围）。
        /// 按 LaneLayout.ActiveLaneCount 选择色板：4 键取鼓谱，5 键取吉他谱。
        /// </summary>
        public static Color GetLaneColor(int lane)
        {
            Color[] palette = LaneLayout.ActiveLaneCount == 5 ? GuitarColors5 : DrumColors4;
            return palette[Mathf.Clamp(lane, 0, palette.Length - 1)];
        }

        /// <summary>
        /// 当前活动轨道数（与 LaneLayout 同步）
        /// </summary>
        public static int LaneCount => LaneLayout.ActiveLaneCount;
    }
}
