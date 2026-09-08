using System.Collections.Generic;

namespace FallenAngel.Data
{
    /// <summary>
    /// Roguelite 地图节点类型（骨架版）：
    /// Battle 战斗格（绑定一首歌）/ Buff 增益占位 / Rest 休息占位 / End 终点
    /// </summary>
    public enum MapNodeType
    {
        Battle,
        Buff,
        Rest,
        End
    }

    /// <summary>
    /// 单个地图节点。战斗格持有谱面引用；占位格仅说明文案（map.* 语言 key）。
    /// </summary>
    [System.Serializable]
    public class MapNode
    {
        public MapNodeType Type;
        public string ChartName;    // 谱面名（冗余，便于日志/序列化）
        public ChartData Chart;     // 战斗格装载的谱面（运行时引用）
    }

    /// <summary>一局的链式地图（首格战斗，末格终点）</summary>
    [System.Serializable]
    public class MapData
    {
        public List<MapNode> Nodes;
    }
}
