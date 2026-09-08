using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Data
{
    /// <summary>
    /// Roguelite 地图生成器（骨架版）：简单随机链式地图。
    /// 首格战斗、末格终点；中段战斗 60% / 增益 20% / 休息 20%；每局 8-10 格。
    /// 战斗格从歌单随机抽取（可重复）。
    /// </summary>
    public static class MapGenerator
    {
        public static MapData Generate(List<ChartData> chartPool)
        {
            MapData map = new MapData { Nodes = new List<MapNode>() };
            int nodeCount = Random.Range(8, 11);

            // 战斗格候选（谱面名非空才可用）
            List<ChartData> usable = new List<ChartData>();
            if (chartPool != null)
            {
                foreach (ChartData c in chartPool)
                {
                    if (c != null && c.metadata != null && !string.IsNullOrEmpty(c.metadata.songName))
                        usable.Add(c);
                }
            }
            if (usable.Count == 0)
            {
                Debug.LogError("[MapGenerator] 歌单为空（Resources/Charts 无可用谱面），生成仅终点地图");
                map.Nodes.Add(new MapNode { Type = MapNodeType.End });
                return map;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                MapNode node;
                if (i == nodeCount - 1)
                {
                    node = new MapNode { Type = MapNodeType.End };
                }
                else if (i == 0)
                {
                    node = new MapNode { Type = MapNodeType.Battle };
                }
                else
                {
                    float roll = Random.value;
                    node = new MapNode
                    {
                        Type = roll < 0.6f ? MapNodeType.Battle
                             : (roll < 0.8f ? MapNodeType.Buff : MapNodeType.Rest)
                    };
                }

                if (node.Type == MapNodeType.Battle)
                {
                    ChartData chart = usable[Random.Range(0, usable.Count)];
                    node.Chart = chart;
                    node.ChartName = chart.metadata.songName;
                }
                map.Nodes.Add(node);
            }

            Debug.Log($"[MapGenerator] 地图生成: {map.Nodes.Count} 格, 歌单 {usable.Count} 首");
            return map;
        }
    }
}
