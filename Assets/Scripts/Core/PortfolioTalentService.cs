using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>持久化边界；Commit必须检查版本并原子替换，失败不能产生半次扣款。</summary>
    public interface IPortfolioProfileStore
    {
        PortfolioProfileData Load(string profileId);
        void Commit(PortfolioProfileData profile, int expectedRevision);
        IReadOnlyList<string> ListProfileIds();
        void Delete(string profileId);
    }

    /// <summary>天赋解锁检查结果；界面负责本地化及二次确认。</summary>
    public enum TalentUnlockStatus
    {
        Available, AlreadyUnlocked, RunInProgress, PrerequisiteLocked,
        InsufficientPoints, UnknownNode, StaleConfirmation
    }

    /// <summary>
    /// 永久天赋规则，不依赖UI或演奏模块。提交时重新读取存档和配置，防止旧确认重复扣费。
    /// 效果清单只负责登记全部已解锁效果；具体结算处理器在后续接入。
    /// </summary>
    public sealed class PortfolioTalentService
    {
        private readonly IPortfolioProfileStore store;
        private readonly Dictionary<string, TalentNodesRow> nodes;
        private readonly Dictionary<string, string[]> incoming;

        public PortfolioTalentService(IPortfolioProfileStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            nodes = PortfolioConfig.TalentNodes.ToDictionary(n => n.NodeId);
            incoming = nodes.Keys.ToDictionary(id => id, id => PortfolioConfig.TalentEdges
                .Where(e => e.ToNodeId == id).Select(e => e.FromNodeId).ToArray());
        }

        /// <summary>创建零积分、无天赋的新档；禁止以UI调试值污染正式成长。</summary>
        public PortfolioProfileData CreateProfile(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 32)
                throw new ArgumentException("Profile name must contain 1 to 32 characters.");
            var profile = new PortfolioProfileData
            {
                profileId = Guid.NewGuid().ToString("N"), displayName = displayName.Trim(), revision = 0
            };
            store.Commit(profile.Copy(), -1);
            return profile.Copy();
        }

        /// <summary>列出独立存档ID，不按名字覆盖同名存档。</summary>
        public IReadOnlyList<string> ListProfileIds() => store.ListProfileIds();

        /// <summary>
        /// 新游戏固定槽：把默认槽重置为零积分新档（覆盖）。
        /// 优先走 Commit 原子替换（revision 递增）；槽不存在走首次创建；损坏档在用户显式点新游戏的前提下删除重建。
        /// </summary>
        public PortfolioProfileData ResetDefaultProfile(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 32)
                throw new ArgumentException("Profile name must contain 1 to 32 characters.");
            var name = displayName.Trim();
            try
            {
                var old = store.Load(PortfolioDefaults.DefaultProfileId);
                var fresh = new PortfolioProfileData
                {
                    profileId = PortfolioDefaults.DefaultProfileId,
                    displayName = name,
                    revision = checked(old.revision + 1)
                };
                store.Commit(fresh, old.revision);
                Debug.Log("[PortfolioTalentService] Reset default profile (overwrite), revision " + fresh.revision);
                return fresh.Copy();
            }
            catch (FileNotFoundException)
            {
                var fresh = new PortfolioProfileData
                {
                    profileId = PortfolioDefaults.DefaultProfileId,
                    displayName = name,
                    revision = 0
                };
                store.Commit(fresh, -1);
                Debug.Log("[PortfolioTalentService] Created default profile, revision 0");
                return fresh.Copy();
            }
            catch (Exception)
            {
                store.Delete(PortfolioDefaults.DefaultProfileId);
                var fresh = new PortfolioProfileData
                {
                    profileId = PortfolioDefaults.DefaultProfileId,
                    displayName = name,
                    revision = 0
                };
                store.Commit(fresh, -1);
                Debug.Log("[PortfolioTalentService] Deleted corrupt default profile and recreated, revision 0");
                return fresh.Copy();
            }
        }

        /// <summary>读取并验证长期状态；损坏或不兼容数据报错，绝不当作新档。</summary>
        public PortfolioProfileData ReadProfile(string profileId)
        {
            var profile = store.Load(profileId);
            ValidateProfile(profile);
            if (profile.profileId != profileId) throw new InvalidOperationException("Profile ID mismatch.");
            return profile.Copy();
        }

        /// <summary>验证解锁图的完整性；不会逆向补齐其他上游分支。</summary>
        public void ValidateProfile(PortfolioProfileData profile)
        {
            if (profile == null || profile.version != 1 || !Guid.TryParseExact(profile.profileId, "N", out _)
                || string.IsNullOrWhiteSpace(profile.displayName) || profile.displayName.Length > 32
                || profile.revision < 0 || profile.growthPoints < 0 || profile.unlockedNodeIds == null
                || (!string.IsNullOrEmpty(profile.activeRunId) && !Guid.TryParseExact(profile.activeRunId, "N", out _)))
                throw new InvalidOperationException("Invalid or unsupported portfolio profile.");
            var owned = new HashSet<string>(profile.unlockedNodeIds);
            if (owned.Count != profile.unlockedNodeIds.Count || owned.Any(id => id == null || !nodes.ContainsKey(id)))
                throw new InvalidOperationException("Invalid unlocked talent IDs.");
            foreach (string id in owned)
                if (!HasPrerequisites(nodes[id], owned))
                    throw new InvalidOperationException("Unlocked talent has no valid predecessor: " + id);
        }

        private bool HasPrerequisites(TalentNodesRow node, HashSet<string> owned)
        {
            var predecessors = incoming[node.NodeId];
            switch (node.PrerequisiteMode)
            {
                case "NONE": return predecessors.Length == 0;
                case "ANY": return predecessors.Any(owned.Contains);
                case "ALL": return predecessors.Length > 0 && predecessors.All(owned.Contains);
                default: throw new InvalidOperationException("Unsupported prerequisite mode.");
            }
        }

        /// <summary>查看节点是否可解锁；界面仍可展示全部锁定节点的详情。</summary>
        public TalentUnlockStatus CheckUnlock(PortfolioProfileData profile, string nodeId)
        {
            ValidateProfile(profile);
            if (nodeId == null || !nodes.TryGetValue(nodeId, out var node)) return TalentUnlockStatus.UnknownNode;
            if (!string.IsNullOrEmpty(profile.activeRunId)) return TalentUnlockStatus.RunInProgress;
            var owned = new HashSet<string>(profile.unlockedNodeIds);
            if (owned.Contains(nodeId)) return TalentUnlockStatus.AlreadyUnlocked;
            if (!HasPrerequisites(node, owned)) return TalentUnlockStatus.PrerequisiteLocked;
            return profile.growthPoints < node.UnlockCost ? TalentUnlockStatus.InsufficientPoints : TalentUnlockStatus.Available;
        }

        /// <summary>二次确认后调用。版本、扣费、解锁在一次存档事务内完成；异常交由界面提示。</summary>
        public TalentUnlockStatus Unlock(string profileId, string nodeId, int confirmedRevision)
        {
            var profile = ReadProfile(profileId);
            if (profile.revision != confirmedRevision) return TalentUnlockStatus.StaleConfirmation;
            var status = CheckUnlock(profile, nodeId);
            if (status != TalentUnlockStatus.Available) return status;
            profile.growthPoints -= nodes[nodeId].UnlockCost;
            profile.unlockedNodeIds.Add(nodeId);
            profile.revision = checked(profile.revision + 1);
            ValidateProfile(profile);
            store.Commit(profile, confirmedRevision);
            return TalentUnlockStatus.Available;
        }

        /// <summary>返回全部永久已解锁且启用的效果，无开局选点。覆盖/叠加留给效果处理器。</summary>
        public IReadOnlyList<TalentEffectsRow> GetRegisteredEffects(string profileId)
        {
            var profile = ReadProfile(profileId);
            var effectIds = new HashSet<string>(profile.unlockedNodeIds.Select(id => nodes[id].EffectId)
                .Where(id => !string.IsNullOrEmpty(id)));
            return PortfolioConfig.TalentEffects.Where(e => e.Enabled && effectIds.Contains(e.EffectId)).ToList().AsReadOnly();
        }
    }
}
