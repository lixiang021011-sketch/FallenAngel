using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道配色统一定义（唯一来源）。
    /// 语义对齐 Moonscraper / Rock Band 4-lane 鼓谱标准：
    ///   lane 0 = 红（底鼓）   lane 1 = 黄（军鼓）
    ///   lane 2 = 蓝（踩镲/嗵） lane 3 = 绿（吊镲/嗵）
    /// 打谱（Moonscraper）与玩游戏（下落式）共用同一套颜色语言；
    /// 若与编辑器实际显示有出入，只需改本文件。
    /// </summary>
    public static class LaneColors
    {
        /// <summary>4 轨基础色（不含透明度）</summary>
        private static readonly Color[] Colors = new Color[]
        {
            new Color(0.82f, 0.25f, 0.22f), // 红 - Lane 0 底鼓
            new Color(0.95f, 0.78f, 0.15f), // 黄 - Lane 1 军鼓
            new Color(0.25f, 0.50f, 0.95f), // 蓝 - Lane 2 踩镲/嗵鼓
            new Color(0.20f, 0.72f, 0.30f)  // 绿 - Lane 3 吊镲/嗵鼓
        };

        /// <summary>
        /// 取某轨道的颜色（越界自动钳制到合法范围）
        /// </summary>
        public static Color GetLaneColor(int lane)
        {
            return Colors[Mathf.Clamp(lane, 0, Colors.Length - 1)];
        }

        /// <summary>
        /// 轨道总数（4）
        /// </summary>
        public static int LaneCount => Colors.Length;
    }
}
