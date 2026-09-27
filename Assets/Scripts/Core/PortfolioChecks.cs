#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FallenAngel.Data;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>配置/长期成长接入检查。只使用临时测试存档，不改变玩家进度或场景。</summary>
    public static class PortfolioChecks
    {
        private static int checks;
        private static readonly List<string> results = new List<string>();

        [MenuItem("Tools/FallenAngel/Print Stage-Chart Bindings")]
        public static void PrintStageChartBindings()
        {
            // 编辑器直查：把"关卡→谱面"对照打进 Console（含资源存在性检查，无需进 Play）
            foreach (var s in PortfolioConfig.Stages)
            {
                var b = PortfolioConfig.ChartBindings.FirstOrDefault(x => x.ChartId == s.ChartId);
                string resource = b == null ? "?" : b.ResourceName;
                bool exists = b != null && Resources.Load<TextAsset>("Charts/" + resource) != null;
                Debug.Log($"[PortfolioChecks] {s.StageId} | {s.Name} | chart={s.ChartId} | resource={resource} | {(exists ? "OK" : "MISSING")}");
            }
        }

        [MenuItem("Tools/FallenAngel/Validate Portfolio Config and Talents")]
        public static void Run()
        {
            checks = 0;
            results.Clear();

            // 成长服务里有几处从 UnityEngine.Random 抽签（掉落 roll、可购买路线 roll）。
            // UnityEngine.Random 由进程启动时的时间播种，于是同一套断言会随运行时间随机翻车
            //（2026-09-21 实测：连跑 4 次会在用例「Optional route flag without E08 keeps F0 price」上失败 1 次）。
            // 自检必须可复现：这里固定种子，只影响本次检查进程内的随机序列。
            UnityEngine.Random.InitState(20260921);

            string temporary = Path.Combine(Path.GetTempPath(), "FA_Portfolio_Check_" + Guid.NewGuid().ToString("N"));
            try
            {
                ValidateConfig();
                ValidateRules();
                ValidateDisk(temporary);
                ValidateGrowth();
                ValidateDrop();
                ValidateLegacy(temporary);
                ValidateMap();
                ValidateIncomeAndDiscounts();
                WriteReport("PASS");
                Debug.Log("[PortfolioChecks] PASS: " + checks + " checks. Temporary profiles only; gameplay not tested.");
            }
            catch (Exception e)
            {
                results.Add(e.ToString());
                WriteReport("FAIL");
                Debug.LogError("[PortfolioChecks] FAIL: " + e);
                // 自检必须给出进程退出码（batchmode 串行跑全量要靠它判断绿/红）
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
            finally
            {
                // 仅清理由本次检查创建的随机临时目录，不访问正式存档。
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks++;
            results.Add("PASS " + description);
        }

        private static void Reject(Action action, string description)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Check(rejected, description);
        }

        private static void ValidateConfig()
        {
            Check(PortfolioConfig.EquipmentBase.Count == 10 && PortfolioConfig.EquipmentEffects.Count == 10,
                "Equipment tables: 10 + 10");
            Check(PortfolioConfig.TalentNodes.Count == 24 && PortfolioConfig.TalentEdges.Count == 27
                && PortfolioConfig.TalentEffects.Count == 21, "Talent tables: 24 + 27 + 21");
            Check(PortfolioConfig.ShopCandidates.Count == 10 && PortfolioConfig.DropEntries.Count == 11
                && PortfolioConfig.DropRules.Count == 3, "Shop/drop tables: 10 + 11 + 3（含钱向演奏房的 DP_CASH）");
            Check(PortfolioConfig.Stages.Count == 13 && PortfolioConfig.ChartBindings.Count == 1
                && PortfolioConfig.MapNodes.Count == 29 && PortfolioConfig.MapEdges.Count == 47,
                "Stage/map tables: 13 + 1 + 29 + 47（分支路线：10 普通 + 3 硬仗）");
            Check(PortfolioConfig.TalentNodes.Sum(n => n.UnlockCost) == 3600, "Full tree cost = 3600");
            Check(PortfolioConfig.TalentNodes.Where(n => n.NodeType == "JUNCTION")
                .All(n => n.UnlockCost == 0 && n.EffectId == null), "Free junctions preserve null effect IDs");
            Check(!PortfolioConfig.TalentEffects.Single(e => e.EffectId == "FX_A0").ValueCapB.HasValue,
                "Optional numeric null is not zero");
            Check(!PortfolioConfig.DropEntries.Single(e => e.EntryId == "DE011").Enabled,
                "Reserved cash entry remains disabled");
            var binding = PortfolioConfig.ChartBindings.Single();
            var chart = ChartLoader.LoadFromResources(binding.ResourceName);
            Check(chart != null && chart.LaneCount == 5 && chart.notes.Count > 0, "Actual placeholder Resources chart loads as 5K");
            Check(Resources.Load<AudioClip>("Audio/" + chart.metadata.audioFileName) != null, "Placeholder audio imports and loads");
        }

        private static PortfolioProfileData Seed(IPortfolioProfileStore store, int points)
        {
            var service = new PortfolioTalentService(store);
            var p = service.CreateProfile("Temporary test profile");
            p.growthPoints = points;
            p.revision++;
            store.Commit(p, p.revision - 1);
            return p;
        }

        private static void ValidateRules()
        {
            var store = new MemoryStore();
            var service = new PortfolioTalentService(store);
            var empty = service.CreateProfile("Empty");
            Check(empty.growthPoints == 0 && empty.unlockedNodeIds.Count == 0, "New profile has zero points and no talents");
            Check(service.CheckUnlock(empty, "A0") == TalentUnlockStatus.InsufficientPoints, "No free paid root");
            var p = Seed(store, 3600);
            Check(service.CheckUnlock(p, "A1") == TalentUnlockStatus.PrerequisiteLocked, "Cannot skip prerequisite");
            Check(service.CheckUnlock(p, "missing") == TalentUnlockStatus.UnknownNode, "Unknown node rejected");
            int oldRevision = p.revision;
            Check(service.Unlock(p.profileId, "A0", oldRevision) == TalentUnlockStatus.Available, "Unlock root");
            Check(service.Unlock(p.profileId, "A0", oldRevision) == TalentUnlockStatus.StaleConfirmation, "Repeated confirmation rejected");
            p = service.ReadProfile(p.profileId);
            Check(p.growthPoints == 3500 && p.unlockedNodeIds.Count == 1, "Exactly one payment");
            Check(service.Unlock(p.profileId, "A0", p.revision) == TalentUnlockStatus.AlreadyUnlocked, "Already unlocked cannot pay again");
            Check(service.Unlock(p.profileId, "A1", p.revision) == TalentUnlockStatus.Available, "Unlock second node");
            p = service.ReadProfile(p.profileId);
            int beforeHub = p.growthPoints;
            Check(service.Unlock(p.profileId, "H1", p.revision) == TalentUnlockStatus.Available, "ANY junction accepts one branch");
            p = service.ReadProfile(p.profileId);
            Check(p.growthPoints == beforeHub, "Junction costs zero");
            Check(!p.unlockedNodeIds.Contains("B1") && !p.unlockedNodeIds.Contains("C1"), "Junction never unlocks upstream siblings");
            Check(service.CheckUnlock(p, "D0") == TalentUnlockStatus.Available
                && service.CheckUnlock(p, "E0") == TalentUnlockStatus.Available
                && service.CheckUnlock(p, "F0") == TalentUnlockStatus.Available, "Junction opens all next branches");
            Check(service.GetRegisteredEffects(p.profileId).Count == 2, "All unlocked effects registered; hub has no effect");
            var isolated = service.ReadProfile(empty.profileId);
            Check(isolated.growthPoints == 0 && isolated.unlockedNodeIds.Count == 0, "Profiles are independent");
            p.unlockedNodeIds.Clear();
            Check(service.ReadProfile(p.profileId).unlockedNodeIds.Count == 3, "Read result is isolated copy");
            p = service.ReadProfile(p.profileId);
            store.FailNextCommit = true;
            Reject(() => service.Unlock(p.profileId, "D0", p.revision), "Save failure is not a successful unlock");
            Check(service.ReadProfile(p.profileId).growthPoints == p.growthPoints
                && !service.ReadProfile(p.profileId).unlockedNodeIds.Contains("D0"), "Failed write deducts nothing");
            p.activeRunId = Guid.NewGuid().ToString("N");
            p.revision++;
            store.Commit(p, p.revision - 1);
            Check(service.Unlock(p.profileId, "D0", p.revision) == TalentUnlockStatus.RunInProgress, "Active run locks permanent growth");
            Check(service.CheckUnlock(service.ReadProfile(empty.profileId), "A0") == TalentUnlockStatus.InsufficientPoints,
                "One active profile does not lock another");
            var full = Seed(store, 3600);
            while (full.unlockedNodeIds.Count < PortfolioConfig.TalentNodes.Count)
            {
                var next = PortfolioConfig.TalentNodes.FirstOrDefault(n => service.CheckUnlock(full, n.NodeId) == TalentUnlockStatus.Available);
                if (next == null) throw new InvalidOperationException("Full tree cannot be unlocked.");
                service.Unlock(full.profileId, next.NodeId, full.revision);
                full = service.ReadProfile(full.profileId);
            }
            Check(full.growthPoints == 0 && service.GetRegisteredEffects(full.profileId).Count == 21,
                "All branches unlock without exclusivity; cost 3600; all 21 effects registered");
            full.unlockedNodeIds.Add("A0");
            Reject(() => service.ValidateProfile(full), "Duplicate saved talent rejected");
            var broken = service.ReadProfile(p.profileId);
            broken.unlockedNodeIds.Remove("A0");
            Reject(() => service.ValidateProfile(broken), "Missing saved prerequisite rejected");
        }

        private static void ValidateDisk(string directory)
        {
            var store = new PortfolioProfileStore(directory);
            var service = new PortfolioTalentService(store);
            var p = Seed(store, 500);
            Check(service.Unlock(p.profileId, "A0", p.revision) == TalentUnlockStatus.Available, "Real JSON atomic unlock");
            var reopened = new PortfolioTalentService(new PortfolioProfileStore(directory));
            var restored = reopened.ReadProfile(p.profileId);
            Check(restored.growthPoints == 400 && restored.unlockedNodeIds.SequenceEqual(new[] { "A0" }), "Reopen retains payment and unlock together");
            Check(File.Exists(Path.Combine(directory, p.profileId + ".json.bak")), "Previous revision backup retained");
            Reject(() => store.Commit(p, p.revision - 1), "Stale disk revision cannot overwrite newer state");
            Check(reopened.ListProfileIds().Count == 1, "Backups and locks are not extra profiles");
            Reject(() => store.Load("../outside"), "Profile path traversal rejected");
            string path = Path.Combine(directory, p.profileId + ".json");
            string original = File.ReadAllText(path);
            File.WriteAllText(path, original.Replace("400", "499"));
            Reject(() => reopened.ReadProfile(p.profileId), "Altered balance fails checksum; no silent backup rollback");
            File.WriteAllText(path, "{}");
            Reject(() => reopened.ReadProfile(p.profileId), "Truncated valid JSON does not become new profile");
            File.WriteAllText(path, original);
            Check(reopened.ReadProfile(p.profileId).growthPoints == 400, "Explicit intact restore readable");
        }

        private static void WriteReport(string status)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-portfolioReport");
            if (index >= 0 && index + 1 < args.Length)
                File.WriteAllLines(args[index + 1], new[] { status + " " + checks + " checks", "Scope: configuration + permanent talent/profile core. Not playable Demo verification." }.Concat(results));
        }

        private static void ValidateGrowth()
        {
            var store = new MemoryStore();
            var talents = new PortfolioTalentService(store);
            var growth = new PortfolioGrowthService(store);
            var p = talents.CreateProfile("Growth");
            var r = growth.Begin(p.profileId, 30, 100);
            Check(r.stageIds.SequenceEqual(new[] { "S01", "S02", "S03", "S05", "S07", "S08", "S10" })
                && r.growthRewards.SequenceEqual(new[] { 100, 100, 100, 100, 100, 100, 100 }),
                "Default route walks the first successor and reads seven stages");
            Reject(() => growth.Begin(p.profileId, 30, 100), "Only one active run per profile");
            Check(growth.Recover(p.profileId).phase == "READY", "Between songs exit remains resumable");
            store.FailNextCommit = true;
            Reject(() => growth.MarkPlaying(p.profileId, r.runId), "Failed start save blocks song start");
            Check(growth.ReadRun(talents.ReadProfile(p.profileId)).phase == "READY", "Failed start does not leave playing marker");
            growth.MarkPlaying(p.profileId, r.runId);
            store.FailNextCommit = true;
            Reject(() => growth.CompleteSong(p.profileId, r.runId, 0, true), "Failed completion save is retryable");
            Check(talents.ReadProfile(p.profileId).growthPoints == 0, "Failed completion gives no points");
            r = growth.CompleteSong(p.profileId, r.runId, 0, true);
            Check(r.earnedPoints == 100 && r.phase == "RESULT" && talents.ReadProfile(p.profileId).growthPoints == 0,
                "Song growth pending until run ends");
            Check(growth.CompleteSong(p.profileId, r.runId, 0, true).earnedPoints == 100, "Duplicate song result gives no extra growth");
            Check(growth.Recover(p.profileId).phase == "RESULT", "Saved result resumes without replay");
            Check(talents.CheckUnlock(talents.ReadProfile(p.profileId), "A0") == TalentUnlockStatus.RunInProgress, "Pending run prevents unlock");
            growth.Continue(p.profileId, r.runId);
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 1, false);
            Check(r.creditedPoints == 100 && talents.ReadProfile(p.profileId).growthPoints == 100, "Failure preserves completed stage only");
            growth.CompleteSong(p.profileId, r.runId, 1, false);
            growth.Abandon(p.profileId, r.runId);
            Check(talents.ReadProfile(p.profileId).growthPoints == 100, "Repeated run settlement is idempotent");
            p = talents.ReadProfile(p.profileId);
            Check(talents.Unlock(p.profileId, "A0", p.revision) == TalentUnlockStatus.Available, "Earned growth can unlock a permanent talent");
            string oldRun = r.runId;
            r = growth.Begin(p.profileId, 30, 100);
            Reject(() => growth.CompleteSong(p.profileId, oldRun, 0, true), "Old run callback cannot affect new run");
            growth.MarkPlaying(p.profileId, r.runId);
            Check(growth.Recover(p.profileId).outcome == "INTERRUPTED", "Force quit during performance becomes failed run");
            Check(talents.ReadProfile(p.profileId).growthPoints == 0, "Interrupted unfinished first song awards zero");
            r = growth.Begin(p.profileId, 30, 100);
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 0, true);
            growth.Continue(p.profileId, r.runId);
            r = WalkChapter(growth, talents, p.profileId, r);
            Check(r.outcome == "CLEARED" && r.completedSongs == 7 && r.creditedPoints == 800
                && talents.ReadProfile(p.profileId).growthPoints == 800,
                "Seven songs + completion credit 700 + 100 exactly once");
            growth.Recover(p.profileId);
            Check(talents.ReadProfile(p.profileId).growthPoints == 800, "Reopening finished profile adds no reward");
            r = growth.Begin(p.profileId, 30, 100);
            growth.MarkPlaying(p.profileId, r.runId);
            growth.CompleteSong(p.profileId, r.runId, 0, true);
            r = growth.Abandon(p.profileId, r.runId);
            Check(r.creditedPoints == 100 && talents.ReadProfile(p.profileId).growthPoints == 900, "Abandon banks only completed song points");
            var other = talents.CreateProfile("Other");
            growth.Begin(other.profileId, 30, 100);
            r = growth.Begin(p.profileId, 30, 100);
            Check(talents.ReadProfile(other.profileId).activeRunId != r.runId, "Two profiles can each retain one independent run");
        }

        /// <summary>
        /// 把一局从当前位置一路走到结算：演奏房自动通关、事件/休息/商店离开、终房离开即收尾。
        /// 分支地图：每一步按"第一个后继"走（自检里的固定策略），剩下的交给玩家在游戏里挑。
        /// </summary>
        private static PortfolioGrowthRunData WalkChapter(PortfolioGrowthService growth, PortfolioTalentService talents,
            string profileId, PortfolioGrowthRunData r)
        {
            r = growth.ReadRun(talents.ReadProfile(profileId));   // 调用方传进来的快照可能已经过时
            for (int guard = 0; guard < 64 && r != null && r.phase != "FINISHED"; guard++)
            {
                if (r.phase == "MAP")
                {
                    string next = PortfolioConfig.MapEdges.First(e => e.FromNodeId == r.currentNodeId).ToNodeId;
                    growth.EnterRoom(profileId, r.runId, next);
                }
                else if (r.phase == "READY")
                {
                    growth.MarkPlaying(profileId, r.runId);
                    growth.CompleteSong(profileId, r.runId, r.completedSongs, true);
                }
                else if (r.phase == "RESULT") growth.Continue(profileId, r.runId);
                else if (r.phase == "ROOM") growth.LeaveRoom(profileId, r.runId);
                r = growth.ReadRun(talents.ReadProfile(profileId));
            }
            return r;
        }

        /// <summary>掉落规则用注入随机源做确定性校验：0 恒过概率门并取首条合法候选。</summary>
        private static void ValidateDrop()
        {
            var store = new MemoryStore();
            var talents = new PortfolioTalentService(store);
            var growth = new PortfolioGrowthService(store, () => 0d);
            var p = talents.CreateProfile("Drop");
            var r = growth.Begin(p.profileId, 30, 100, true);

            // 普通演奏（R1A / S01 / DP_NORMAL）：首条合法候选 E01
            growth.EnterRoom(p.profileId, r.runId, "R1A");
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 0, true);
            Check(r.lastDropGranted && r.heldEquipmentIds.Contains("E01")
                && r.lastDropRewardId == "E01" && r.lastDropRewardType == "EQUIPMENT",
                "Normal stage grants first weighted equipment on deterministic roll");
            Check(growth.CompleteSong(p.profileId, r.runId, 0, true).heldEquipmentIds.Count(id => id == "E01") == 1,
                "Repeated completion does not duplicate a stage drop");

            // 第 2 步走商店，第 3 步走硬仗（H01 / DP_CHALLENGE）：持有 E01 后首条合法候选为 E02
            growth.Continue(p.profileId, r.runId);
            growth.EnterRoom(p.profileId, r.runId, "R2B"); growth.LeaveRoom(p.profileId, r.runId);
            growth.EnterRoom(p.profileId, r.runId, "R3B");
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 1, true);
            Check(r.lastDropGranted && r.heldEquipmentIds.Contains("E02")
                && r.heldEquipmentIds.Count(id => id == "E01") == 1,
                "Hard fight excludes owned equipment and drops next first candidate");

            // 集齐全部 10 件后走到终房：合法装备池为空 → 不再掉落（DE011 现金停用，不得补抽补货币）
            growth.Continue(p.profileId, r.runId);
            foreach (var id in PortfolioConfig.EquipmentBase.Select(e => e.EquipmentId))
                if (!r.heldEquipmentIds.Contains(id)) growth.AcquireEquipment(p.profileId, r.runId, id);
            r = growth.ReadRun(talents.ReadProfile(p.profileId));
            r = WalkChapter(growth, talents, p.profileId, r);
            Check(r.outcome == "CLEARED" && !r.lastDropGranted, "Full equipment pool grants nothing; final run still settles");

            // 失败不触发掉落（失败按局结算并清空局内资源）
            var failureStore = new MemoryStore();
            var failureTalents = new PortfolioTalentService(failureStore);
            var failureGrowth = new PortfolioGrowthService(failureStore, () => 0d);
            var fp = failureTalents.CreateProfile("DropFail");
            var fr = failureGrowth.Begin(fp.profileId, 30, 100, true);
            failureGrowth.EnterRoom(fp.profileId, fr.runId, "R1A");
            failureGrowth.MarkPlaying(fp.profileId, fr.runId);
            fr = failureGrowth.CompleteSong(fp.profileId, fr.runId, 0, false);
            Check(fr.outcome == "FAILED" && !fr.lastDropGranted && fr.heldEquipmentIds.Count == 0,
                "Failed performance never rolls a stage drop");
        }

        private static void ValidateLegacy(string directory)
        {
            var profile = new LegacyCheckProfile { profileId = Guid.NewGuid().ToString("N"), displayName = "Legacy", growthPoints = 100 };
            string payload = JsonUtility.ToJson(profile);
            string checksum;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                checksum = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            string file = Path.Combine(directory, profile.profileId + ".json");
            File.WriteAllText(file, "{\"format\":\"FallenAngel.Portfolio.Profile.v1\",\"profile\":" + payload + ",\"checksum\":\"" + checksum + "\"}");
            var talents = new PortfolioTalentService(new PortfolioProfileStore(directory));
            var loaded = talents.ReadProfile(profile.profileId);
            Check(loaded.growthPoints == 100, "Legacy profile checksum verified without resetting progress");
            talents.Unlock(profile.profileId, "A0", loaded.revision);
            Check(talents.ReadProfile(profile.profileId).unlockedNodeIds.Contains("A0") && File.ReadAllText(file).Contains("Profile.v2"),
                "Legacy profile upgrades on next successful transaction");
        }

        private static void ValidateMap()
        {
            var store = new MemoryStore();
            var talents = new PortfolioTalentService(store);
            var growth = new PortfolioGrowthService(store);
            var p = talents.CreateProfile("Map");
            var r = growth.Begin(p.profileId, 30, 100, true);
            Check(r.phase == "MAP" && r.currentNodeId == "R00" && r.stageIds.Count == 0 && r.layer == 1,
                "Branching chapter starts at R00, layer 1, empty stage list");
            Reject(() => growth.EnterRoom(p.profileId, r.runId, "R15"), "Cannot skip to the finale");
            Reject(() => growth.EnterRoom(p.profileId, r.runId, "R2B"), "Cannot skip a step");

            // 第 1 步是必打的演奏：进入演奏房才把这一场追加进关卡清单
            growth.EnterRoom(p.profileId, r.runId, "R1A");
            r = growth.ReadRun(talents.ReadProfile(p.profileId));
            Check(r.phase == "READY" && r.stageIds.Count == 1 && r.stageIds[0] == "S01",
                "Entering a performance room appends its stage");
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 0, true);
            Check(r.phase == "RESULT" && r.runCash == 100, "First performance grants configured base cash");
            Check(growth.CompleteSong(p.profileId, r.runId, 0, true).runCash == 100, "Repeated song callback does not duplicate cash");
            Reject(() => growth.EnterRoom(p.profileId, r.runId, "R2A"), "Must continue before choosing the next room");
            growth.Continue(p.profileId, r.runId);

            // 分支：第 2 步两个候选都能去；走事件，再在第 3 步选硬仗
            Check(NextRooms("R1A").SequenceEqual(new[] { "R2A", "R2B" }), "Step 2 offers two candidates");
            growth.EnterRoom(p.profileId, r.runId, "R2A");
            r = growth.ReadRun(talents.ReadProfile(p.profileId));
            Check(r.phase == "ROOM", "Event room is a stop on the road, not a dead end");
            Reject(() => growth.EnterRoom(p.profileId, r.runId, "R1A"), "Cannot backtrack to a visited room");
            growth.LeaveRoom(p.profileId, r.runId);

            growth.EnterRoom(p.profileId, r.runId, "R3B");
            r = growth.ReadRun(talents.ReadProfile(p.profileId));
            Check(r.phase == "READY" && r.stageIds[1] == "H01", "Hard fight branch selects the hard stage");
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 1, true);
            Check(r.runCash == 250, "Hard fight pays its own base income");
            growth.Continue(p.profileId, r.runId);

            // 剩下每一站取第一个后继，走到终房
            r = WalkChapter(growth, talents, p.profileId, r);
            Check(r.outcome == "CLEARED" && r.completedSongs == 7 && r.creditedPoints == 850 && r.runCash == 0
                && r.layer == ChapterCatalog.LayerCount,
                "Finale settles the run after seven performances (layer tracked to 3)");
        }

        /// <summary>某个房间的全部后继（按表顺序）——自检用来验"这一步给了几个候选"。</summary>
        private static string[] NextRooms(string roomId)
        {
            return PortfolioConfig.MapEdges.Where(e => e.FromNodeId == roomId).Select(e => e.ToNodeId).ToArray();
        }

        private static void ValidateIncomeAndDiscounts()
        {
            var income = new PortfolioIncomeService();
            var c1 = PortfolioConfig.TalentEffects.Single(e => e.EffectId == "FX_C1");
            var e09 = PortfolioConfig.EquipmentEffects.Single(e => e.EffectId == "EQ_E09");
            var stage = PortfolioConfig.Stages.Single(s => s.StageId == "S01");
            var settled = income.Compute(new[] { c1 }, new[] { e09 }, stage, 0, 1, 100);
            double c1Amt = Math.Min(100 * c1.Coefficient, stage.BaseIncome * (c1.ValueCapB ?? 0));
            double e09Amt = Math.Min(100 * e09.Coefficient, stage.BaseIncome * (e09.ValueCapB ?? 0));
            Check(Math.Abs(settled.EconomyTotal - (c1Amt + e09Amt)) < 0.001, "C1 and E09 interest add independently");
            Check(settled.Lines.Any(l => l.key == "income.C1") && settled.Lines.Any(l => l.key == "income.E09"),
                "Interest lines both present");

            var store = new MemoryStore();
            var talents = new PortfolioTalentService(store);
            var growth = new PortfolioGrowthService(store);
            var p = Seed(store, 4000);
            Check(talents.Unlock(p.profileId, "A0", p.revision) == TalentUnlockStatus.Available, "Discount seed A0");
            p = talents.ReadProfile(p.profileId);
            Check(talents.Unlock(p.profileId, "A1", p.revision) == TalentUnlockStatus.Available, "Discount seed A1");
            p = talents.ReadProfile(p.profileId);
            Check(talents.Unlock(p.profileId, "H1", p.revision) == TalentUnlockStatus.Available, "Discount seed H1");
            p = talents.ReadProfile(p.profileId);
            Check(talents.Unlock(p.profileId, "F0", p.revision) == TalentUnlockStatus.Available, "Discount seed F0");
            p = talents.ReadProfile(p.profileId);
            var r = growth.Begin(p.profileId, 30, 100, true);
            growth.EnterRoom(p.profileId, r.runId, "R1A");
            growth.MarkPlaying(p.profileId, r.runId);
            r = growth.CompleteSong(p.profileId, r.runId, 0, true);
            growth.Continue(p.profileId, r.runId);
            p = talents.ReadProfile(p.profileId);
            r = growth.ReadRun(p);
            // 章节表里现在没有收费边（硬仗入场费怎么收是待拍板项），所以路费链路用合成价直接验算术：
            // 基础 40 → F0 站定折扣 5% → 38；E08 可选折扣与其取最大 → 32。
            Check(growth.QuoteRouteFee(p, r, 40, false) == 38, "F0 standing route discount is 5%");
            Check(growth.QuoteRouteFee(p, r, 40, true) == 38, "Optional route flag without E08 keeps F0 price");
            growth.AcquireEquipment(p.profileId, r.runId, "E08");
            p = talents.ReadProfile(p.profileId);
            r = growth.ReadRun(p);
            Check(growth.QuoteRouteFee(p, r, 40, false) == 38, "E08 does not change standing map fee");
            Check(growth.QuoteRouteFee(p, r, 40, true) == 32, "E08 optional route discount is 20%");
            // 收费边进入时的扣款与"用掉一次可选折扣"要等章节表出现收费边再验（收费规则待拍板）。
        }

        [Serializable] private sealed class LegacyCheckProfile
        {
            public int version = 1;
            public string profileId;
            public string displayName;
            public int revision = 0;
            public int growthPoints;
            public List<string> unlockedNodeIds = new List<string>();
            public string activeRunId;
        }

        private sealed class MemoryStore : IPortfolioProfileStore
        {
            private readonly Dictionary<string, PortfolioProfileData> profiles = new Dictionary<string, PortfolioProfileData>();
            public bool FailNextCommit;
            public PortfolioProfileData Load(string id) => profiles[id].Copy();
            public IReadOnlyList<string> ListProfileIds() => profiles.Keys.ToList().AsReadOnly();
            public void Commit(PortfolioProfileData profile, int expectedRevision)
            {
                if (FailNextCommit) { FailNextCommit = false; throw new IOException("Injected disk failure"); }
                int revision = profiles.TryGetValue(profile.profileId, out var old) ? old.revision : -1;
                if (revision != expectedRevision) throw new InvalidOperationException("Stale revision");
                profiles[profile.profileId] = profile.Copy();
            }
            public void Delete(string profileId)
            {
                profiles.Remove(profileId);
            }
        }
    }
}
#endif
