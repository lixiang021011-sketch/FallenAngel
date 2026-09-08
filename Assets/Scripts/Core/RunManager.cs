using System.Collections.Generic;
using UnityEngine;
using FallenAngel.Data;
using FallenAngel.Audio;

namespace FallenAngel.Core
{
    /// <summary>
    /// 一局 Roguelite 运行管理器（单例，挂 Managers，不 DontDestroyOnLoad）。
    /// 负责：局内地图生成、节点推进、战斗格装载、结算返回与弃局。
    /// 流程：主菜单 → StartRun（生成地图→Map）→ 战斗格（游玩→结算）→
    /// 回地图下一节点 → 终点 → FinishRun（回主菜单）。
    /// </summary>
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        /// <summary>
        /// 临时开关：战斗格按占位格处理（点击仅弹说明、不装载游戏）——
        /// UI 内容与核心玩法分开测试用。恢复战斗格游玩时改回 false 即可。
        /// 核心玩法调试入口：Play 模式下 GameStarter（Canvas 根）右键 ContextMenu。
        /// </summary>
        public const bool BattleNodeAsPlaceholder = true;

        /// <summary>当前局地图</summary>
        public MapData CurrentMap { get; private set; }

        /// <summary>当前节点索引（指向可操作的节点）</summary>
        public int CurrentNodeIndex { get; private set; }

        /// <summary>是否处于一局之中（开始游戏后、终点/弃局前为 true）</summary>
        public bool IsInRun { get; private set; }

        /// <summary>地图节点推进/变更事件（MapPanelController 订阅刷新）</summary>
        public event System.Action OnMapUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>开始一局：生成随机地图并进入地图界面</summary>
        public void StartRun()
        {
            List<ChartData> pool = ChartLoader.LoadAllChartPool();
            CurrentMap = MapGenerator.Generate(pool);
            CurrentNodeIndex = 0;
            IsInRun = true;
            Debug.Log($"[RunManager] 新一局开始: 地图节点数={CurrentMap.Nodes.Count}");
            GameManager.Instance?.GoToMap();
        }

        /// <summary>选择当前节点：战斗格 → 装载谱面开玩；占位/终点格 → 说明弹窗（由 MapPanelController 处理）</summary>
        public void SelectCurrentNode()
        {
            if (!IsInRun || CurrentMap == null || CurrentNodeIndex >= CurrentMap.Nodes.Count) return;
            MapNode node = CurrentMap.Nodes[CurrentNodeIndex];

            if (node.Type == MapNodeType.Battle)
            {
                LoadBattleNode(node);
            }
            else
            {
                Debug.Log($"[RunManager] 节点说明弹窗: {node.Type}（弹窗 UI 由 MapPanelController 显示）");
            }
        }

        /// <summary>装载战斗格：停残留音频 → 装 BGM → 加载谱面（Loading 状态自动触发倒计时）</summary>
        private void LoadBattleNode(MapNode node)
        {
            if (node.Chart == null)
            {
                Debug.LogError("[RunManager] 战斗格无谱面数据，跳过");
                return;
            }

            AudioManager.Instance?.StopAll();
            if (AudioManager.Instance != null)
                AudioManager.Instance.LoadBGM(node.Chart);
            GameManager.Instance?.LoadChart(node.Chart);
            // 倒计时由 GameStarter 的 Loading 状态分支触发；此处显式再调一次兜底（isCountingDown 守卫幂等）
            GameManager.Instance?.StartCountdownAndPlay();
            Debug.Log($"[RunManager] 战斗格装载: {node.Chart.metadata.songName} (Lv.{node.Chart.metadata.level})");
        }

        /// <summary>结算页返回：推进到下一节点回地图（已到末尾则结束本局）</summary>
        public void AdvanceFromResult()
        {
            if (!IsInRun) return;
            CurrentNodeIndex++;
            if (CurrentNodeIndex >= CurrentMap.Nodes.Count)
            {
                FinishRun();
                return;
            }
            OnMapUpdated?.Invoke();
            GameManager.Instance?.GoToMap();
        }

        /// <summary>占位格（buff/rest）说明弹窗关闭：推进到下一节点（终点由弹窗关闭走 FinishRun）</summary>
        public void CloseInfoAndAdvance()
        {
            if (!IsInRun) return;
            CurrentNodeIndex++;
            if (CurrentNodeIndex >= CurrentMap.Nodes.Count)
            {
                FinishRun();
                return;
            }
            OnMapUpdated?.Invoke();
            Debug.Log($"[RunManager] 推进至节点 {CurrentNodeIndex} ({CurrentMap.Nodes[CurrentNodeIndex].Type})");
        }

        /// <summary>结束本局（到达终点）：清局回主菜单</summary>
        public void FinishRun()
        {
            IsInRun = false;
            CurrentMap = null;
            CurrentNodeIndex = 0;
            Debug.Log("[RunManager] 本局结束");
            GameManager.Instance?.BackToMenu();
        }

        /// <summary>弃局（暂停退出）：清局但不切状态（由 PauseController 负责回菜单）</summary>
        public void AbandonRun()
        {
            if (!IsInRun) return;
            IsInRun = false;
            CurrentMap = null;
            CurrentNodeIndex = 0;
            Debug.Log("[RunManager] 弃局（暂停退出）");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
