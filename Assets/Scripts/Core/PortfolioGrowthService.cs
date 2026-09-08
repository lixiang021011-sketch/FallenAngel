using System;
using System.Collections.Generic;
using System.Linq;
using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>成长验证事务：完成关卡暂存积分，整局结束一次性入账；与真实歌曲事件连接。</summary>
    public sealed class PortfolioGrowthService
    {
        private readonly IPortfolioProfileStore store;
        private readonly PortfolioTalentService talents;
        public PortfolioGrowthService(IPortfolioProfileStore store)
        {
            this.store = store;
            talents = new PortfolioTalentService(store);
        }

        /// <summary>读出并核对局内快照；旧的单独占用标记不能伪装成可续玩的游戏。</summary>
        public PortfolioGrowthRunData ReadRun(PortfolioProfileData profile)
        {
            if (string.IsNullOrEmpty(profile.growthRunJson))
            {
                if (!string.IsNullOrEmpty(profile.activeRunId)) throw new InvalidOperationException("Missing run snapshot.");
                return null;
            }
            var run = JsonUtility.FromJson<PortfolioGrowthRunData>(profile.growthRunJson);
            if (run == null || run.version != 1 || !Guid.TryParseExact(run.runId, "N", out _)
                || run.stageIds == null || run.growthRewards == null || run.stageIds.Count == 0
                || run.stageIds.Count != run.growthRewards.Count || run.completedSongs < 0
                || run.completedSongs > run.stageIds.Count || run.failureLimit < 1 || run.completionBonus < 0
                || run.growthRewards.Any(n => n < 0)
                || run.earnedPoints != run.growthRewards.Take(run.completedSongs).Sum()
                || !new[] { "READY", "PLAYING", "RESULT", "FINISHED", "MAP", "ROOM" }.Contains(run.phase)
                || (run.phase == "FINISHED" ? !string.IsNullOrEmpty(profile.activeRunId) : profile.activeRunId != run.runId)
                || (run.phase != "FINISHED" && run.completedSongs == run.stageIds.Count))
                throw new InvalidOperationException("Invalid growth run snapshot.");
            if (run.phase == "FINISHED")
            {
                if (!new[] { "CLEARED", "FAILED", "ABANDONED", "INTERRUPTED" }.Contains(run.outcome)
                    || (run.outcome == "CLEARED" && run.completedSongs != run.stageIds.Count)
                    || run.creditedPoints != run.earnedPoints + (run.outcome == "CLEARED" ? run.completionBonus : 0))
                    throw new InvalidOperationException("Invalid run settlement.");
            }
            else if (run.creditedPoints != 0) throw new InvalidOperationException("Unfinished run already credited.");
            if (run.useMap && (run.runCash < 0 || run.visitedNodeIds == null
                || !run.visitedNodeIds.Contains(run.currentNodeId)
                || run.visitedNodeIds.Distinct().Count() != run.visitedNodeIds.Count
                || !PortfolioConfig.MapNodes.Any(n => n.NodeId == run.currentNodeId)))
                throw new InvalidOperationException("Invalid map snapshot.");
            if (run.heldEquipmentIds == null
                || run.heldEquipmentIds.Distinct().Count() != run.heldEquipmentIds.Count
                || run.heldEquipmentIds.Count > PortfolioDefaults.EquipmentCapacity
                || run.heldEquipmentIds.Any(id => !PortfolioConfig.EquipmentBase.Any(e => e.EquipmentId == id)))
                throw new InvalidOperationException("Invalid held equipment snapshot.");
            return run;
        }

        /// <summary>沿表中唯一免费出口，提取演奏关卡；本入口暂不执行商店交易。</summary>
        public PortfolioGrowthRunData Begin(string profileId, int failureLimit, int completionBonus, bool useMap = false)
        {
            if (failureLimit < 1 || completionBonus < 0) throw new ArgumentOutOfRangeException();
            var p = talents.ReadProfile(profileId);
            if (!string.IsNullOrEmpty(p.activeRunId)) throw new InvalidOperationException("Run already active.");
            var run = new PortfolioGrowthRunData { runId = Guid.NewGuid().ToString("N"), failureLimit = failureLimit, completionBonus = completionBonus };
            var node = PortfolioConfig.MapNodes.Single(n => n.NodeType == "START");
            var visited = new HashSet<string>();
            while (visited.Add(node.NodeId))
            {
                if (!string.IsNullOrEmpty(node.StageId))
                {
                    var stage = PortfolioConfig.Stages.Single(s => s.StageId == node.StageId && s.Enabled);
                    run.stageIds.Add(stage.StageId);
                    run.growthRewards.Add(stage.GrowthScore);
                }
                if (node.NodeType == "FINAL") break;
                var edge = PortfolioConfig.MapEdges.Single(e => e.FromNodeId == node.NodeId && e.RoutePrice == 0);
                node = PortfolioConfig.MapNodes.Single(n => n.NodeId == edge.ToNodeId);
            }
            if (node.NodeType != "FINAL" || run.stageIds.Count == 0) throw new InvalidOperationException("Invalid free route.");
            if (useMap)
            {
                run.useMap = true;
                run.phase = "MAP";
                run.currentNodeId = PortfolioConfig.MapNodes.Single(n => n.NodeType == "START").NodeId;
                run.visitedNodeIds.Add(run.currentNodeId);
            }
            p.activeRunId = run.runId;
            Save(p, run);
            return run;
        }

        /// <summary>GO之前先持久化演奏标记；写入失败时调用方不得播放。</summary>
        public void MarkPlaying(string profileId, string runId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (r.phase != "READY") throw new InvalidOperationException("Song cannot start now.");
            r.phase = "PLAYING";
            Save(p, r);
        }

        /// <summary>成功只结算当前歌曲一次；失败不计入当前未完成歌曲。</summary>
        public PortfolioGrowthRunData CompleteSong(string profileId, string runId, int songIndex, bool success, int score = 0, float accuracy = 0)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (songIndex < r.completedSongs || r.phase == "FINISHED") return r;
            if (r.phase != "PLAYING" || songIndex != r.completedSongs) throw new InvalidOperationException("Unexpected song completion.");
            r.lastScore = score;
            r.lastAccuracy = accuracy;
            r.lastSongPoints = 0;
            r.lastCashReward = 0;
            if (success)
            {
                r.lastSongPoints = r.growthRewards[r.completedSongs];
                r.earnedPoints = checked(r.earnedPoints + r.lastSongPoints);
                if (r.useMap)
                {
                    r.lastCashReward = PortfolioConfig.Stages.Single(s => s.StageId == r.stageIds[r.completedSongs]).BaseIncome;
                    r.runCash = checked(r.runCash + r.lastCashReward);
                }
                r.completedSongs++;
            }
            if (!success || r.completedSongs == r.stageIds.Count) Finish(p, r, success ? "CLEARED" : "FAILED");
            else r.phase = "RESULT";
            Save(p, r);
            return r;
        }

        /// <summary>从已保存的歌曲结算继续，不重播已完成关卡。</summary>
        public void Continue(string profileId, string runId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (r.phase != "RESULT") throw new InvalidOperationException("No result to continue.");
            r.phase = r.useMap ? "MAP" : "READY";
            Save(p, r);
        }

        /// <summary>进入当前房间的直接后继；支付、到达位置和访问记录一次性提交。</summary>
        public void EnterRoom(string profileId, string runId, string nodeId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (!r.useMap || r.phase != "MAP" || r.visitedNodeIds.Contains(nodeId))
                throw new InvalidOperationException("Room is not available.");
            var edge = PortfolioConfig.MapEdges.SingleOrDefault(e => e.FromNodeId == r.currentNodeId && e.ToNodeId == nodeId);
            if (edge == null) throw new InvalidOperationException("Room is not adjacent.");
            if (r.runCash < edge.RoutePrice) throw new InvalidOperationException(Loc.T("portfolio.cashShort"));
            var node = PortfolioConfig.MapNodes.Single(n => n.NodeId == nodeId);
            r.runCash -= edge.RoutePrice;
            r.currentNodeId = nodeId;
            r.visitedNodeIds.Add(nodeId);
            if (node.NodeType == "STAGE" || node.NodeType == "FINAL")
            {
                var stage = PortfolioConfig.Stages.Single(s => s.StageId == node.StageId && s.Enabled);
                r.stageIds[r.completedSongs] = stage.StageId;
                r.growthRewards[r.completedSongs] = stage.GrowthScore;
                r.phase = "READY";
            }
            else r.phase = "ROOM";
            Save(p, r);
        }

        /// <summary>离开商店或空占位房；暂无交易时不额外发放资源。</summary>
        public void LeaveRoom(string profileId, string runId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (!r.useMap || r.phase != "ROOM") throw new InvalidOperationException("No room to leave.");
            r.phase = "MAP";
            Save(p, r);
        }

        /// <summary>
        /// 获得装备：存在+启用+非重复+容量一次校验后原子提交。
        /// 任何失败都不改变资源状态——扣款/抽奖由调用方先判断再调本方法。
        /// </summary>
        public void AcquireEquipment(string profileId, string runId, string equipmentId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (r.phase == "FINISHED") throw new InvalidOperationException("Run already finished.");
            var item = PortfolioConfig.EquipmentBase.SingleOrDefault(e => e.EquipmentId == equipmentId && e.Enabled);
            if (item == null) throw new InvalidOperationException("Unknown or disabled equipment.");
            if (item.AllowDuplicate) throw new InvalidOperationException("Duplicate equipment is not supported in this version.");
            if (r.heldEquipmentIds.Contains(equipmentId)) throw new InvalidOperationException("Equipment already held.");
            if (r.heldEquipmentIds.Count >= PortfolioDefaults.EquipmentCapacity)
                throw new InvalidOperationException("Equipment capacity full.");
            r.heldEquipmentIds.Add(equipmentId);
            Save(p, r);
            Debug.Log($"[PortfolioGrowthService] Acquired equipment {equipmentId} ({r.heldEquipmentIds.Count}/{PortfolioDefaults.EquipmentCapacity})");
        }

        /// <summary>放弃本局时保留已完成关卡积分；结束后重复调用无收益。</summary>
        public PortfolioGrowthRunData Abandon(string profileId, string runId)
        {
            var p = talents.ReadProfile(profileId);
            var r = Require(p, runId);
            if (r.phase == "FINISHED") return r;
            Finish(p, r, "ABANDONED");
            Save(p, r);
            return r;
        }

        /// <summary>仅在重新选择存档时恢复；PLAYING表示上次在演奏或暂停中强退。</summary>
        public PortfolioGrowthRunData Recover(string profileId)
        {
            var p = talents.ReadProfile(profileId);
            var r = ReadRun(p);
            if (r != null && r.phase == "PLAYING")
            {
                Finish(p, r, "INTERRUPTED");
                Save(p, r);
            }
            return r;
        }

        private PortfolioGrowthRunData Require(PortfolioProfileData p, string runId)
        {
            var run = ReadRun(p);
            if (run == null || run.runId != runId) throw new InvalidOperationException("Stale run command.");
            return run;
        }

        private static void Finish(PortfolioProfileData p, PortfolioGrowthRunData r, string outcome)
        {
            r.phase = "FINISHED";
            r.outcome = outcome;
            r.creditedPoints = checked(r.earnedPoints + (outcome == "CLEARED" ? r.completionBonus : 0));
            p.growthPoints = checked(p.growthPoints + r.creditedPoints);
            p.activeRunId = null;
            r.runCash = 0;
            r.heldEquipmentIds.Clear(); // 局终清空（装备是局内资源，不跨局）
        }

        private void Save(PortfolioProfileData p, PortfolioGrowthRunData r)
        {
            int revision = p.revision;
            p.growthRunJson = JsonUtility.ToJson(r);
            p.revision = checked(revision + 1);
            ReadRun(p);
            store.Commit(p, revision);
        }
    }
}
