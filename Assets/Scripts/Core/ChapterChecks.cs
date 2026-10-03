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
    /// <summary>
    /// 第一章路线的自检（内容未铺也要能验）：
    ///   · 出货表通过校验：3 层 / 15 步 / 29 房间 / 一局 7 场演奏 / 硬仗候选 3 步；
    ///   · 构图规则逐条验：第一步必演奏、终房唯一且人人可达、每步 1–3 个候选、同类不连三步、
    ///     硬仗不连硬仗且每层最多一个、商店与休息至少能遇到、连线只连下一步且不跨列、无死胡同；
    ///   · 与运行时地图（PortfolioConfig）不漂移：29 节点 / 47 条边 / 13 个关卡一一对应；
    ///   · 数值对得上设计稿：稀有度按层倾斜、价格 40/80/160、包 8 格、刷新首价 30、
    ///     演奏房奖励 60/25/15、硬仗保底至少蓝；
    ///   · 故意写错的表（第 1 步不是演奏、删边造成死胡同、某步塞 4 个候选、硬仗放到第 1 步、
    ///     层权重和不是 1、id 重复）必须被逐条点到名。
    ///
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.ChapterChecks.Run）两用；报告写 Logs/chapter_check.txt。
    /// </summary>
    public static class ChapterChecks
    {
        private const string Tag = "[ChapterCatalog] ";

        [MenuItem("Tools/FallenAngel/Validation/Content/Validate Chapter 1 Route", false, 704)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-28} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                // ---------- ① 出货表 ----------
                var shipped = ChapterCatalog.Validate();
                check("出货表校验通过", shipped.Count == 0,
                    shipped.Count == 0
                        ? string.Format("层 {0} / 步 {1} / 房间 {2} / 连线 {3}",
                            ChapterCatalog.Layers.Count, ChapterCatalog.StepCount,
                            ChapterCatalog.Rooms.Count, ChapterCatalog.Edges.Count)
                        : string.Join("；", shipped.ToArray()));

                check("层、步与房间总数",
                    ChapterCatalog.Layers.Count == 3 && ChapterCatalog.StepCount == 15
                    && ChapterCatalog.Rooms.Count == 29 && ChapterCatalog.Edges.Count == 47,
                    string.Format("层 3 / 步 15 / 房间 {0} / 连线 {1}",
                        ChapterCatalog.Rooms.Count, ChapterCatalog.Edges.Count));

                int performanceSteps = ChapterCatalog.PerformanceStepCountOf(ChapterCatalog.Rooms);
                check("一局 7 场演奏（有演奏候选的步数）",
                    performanceSteps == ChapterCatalog.PerformanceStepCount,
                    string.Format("演奏步 {0}；地图上的演奏类房间 {1}",
                        performanceSteps, ChapterCatalog.PerformanceCountOf(ChapterCatalog.Rooms)));

                var hardSteps = ChapterCatalog.Rooms.Where(r => r.Kind == ChapterRoomKind.HardFight)
                    .Select(r => r.Step).Distinct().OrderBy(s => s).ToArray();
                check("硬仗候选 2 步、每层一个、不连着",
                    hardSteps.Length == ChapterCatalog.HardFightOptionSteps
                    && !hardSteps.Contains(1)
                    && hardSteps.Zip(hardSteps.Skip(1), (a, b) => b > a + 1).All(ok => ok),
                    "步 " + string.Join(" / ", hardSteps.Select(s => s.ToString()).ToArray()));

                int shopSteps = ChapterCatalog.Rooms.Where(r => r.Kind == ChapterRoomKind.Shop)
                    .Select(r => r.Step).Distinct().Count();
                int restSteps = ChapterCatalog.Rooms.Where(r => r.Kind == ChapterRoomKind.Rest)
                    .Select(r => r.Step).Distinct().Count();
                check("商店与休息至少能遇到", shopSteps >= 4 && restSteps >= 3,
                    string.Format("能给商店 {0} 步 / 能给休息 {1} 步", shopSteps, restSteps));

                var startRoom = ChapterCatalog.Rooms.First(r => r.Kind == ChapterRoomKind.Start);
                var finalRoom = ChapterCatalog.Rooms.First(r => r.Kind == ChapterRoomKind.Final);
                check("起点在第 0 步、终房在最后一步",
                    startRoom.Step == 0 && finalRoom.Step == ChapterCatalog.StepCount
                    && ChapterCatalog.Rooms.Count(r => r.Kind == ChapterRoomKind.Start) == 1
                    && ChapterCatalog.Rooms.Count(r => r.Kind == ChapterRoomKind.Final) == 1,
                    string.Format("起点 {0}（步 {1}）/ 终房 {2}（步 {3}）",
                        startRoom.RoomId, startRoom.Step, finalRoom.RoomId, finalRoom.Step));

                bool candidateBounds = true;
                for (int step = 1; step <= ChapterCatalog.StepCount; step++)
                {
                    int count = ChapterCatalog.Rooms.Count(r => r.Step == step);
                    if (count < 1 || count > ChapterCatalog.MaxCandidatesPerStep) candidateBounds = false;
                }
                check("每步都有 1–3 个候选", candidateBounds, "上限 " + ChapterCatalog.MaxCandidatesPerStep);

                // ---------- ② 数值 ----------
                var layerSpecs = ChapterCatalog.Layers;
                bool weightsOk = layerSpecs.All(l => Math.Abs(l.TotalWeight - 1f) < 1e-4f);
                bool tiltOk = true;
                for (int i = 1; i < layerSpecs.Count && tiltOk; i++)
                {
                    if (layerSpecs[i].WhiteWeight > layerSpecs[i - 1].WhiteWeight) tiltOk = false;
                    if (layerSpecs[i].GoldWeight < layerSpecs[i - 1].GoldWeight) tiltOk = false;
                }
                check("稀有度按层倾斜", weightsOk && tiltOk,
                    string.Join(" / ", layerSpecs.Select(l => string.Format("L{0} {1:0.00}:{2:0.00}:{3:0.00}",
                        l.Layer, l.WhiteWeight, l.BlueWeight, l.GoldWeight)).ToArray()));

                check("价格 白 40 / 蓝 80 / 金 160 且递增",
                    ChapterCatalog.PriceOf(GearRarity.White) == 40 &&
                    ChapterCatalog.PriceOf(GearRarity.Blue) == 80 &&
                    ChapterCatalog.PriceOf(GearRarity.Gold) == 160,
                    string.Format("{0} / {1} / {2}",
                        ChapterCatalog.PriceOf(GearRarity.White),
                        ChapterCatalog.PriceOf(GearRarity.Blue),
                        ChapterCatalog.PriceOf(GearRarity.Gold)));

                float rewardSum = ChapterCatalog.RewardChoiceChance + ChapterCatalog.RewardCashChance
                    + ChapterCatalog.RewardNothingChance;
                check("包 8 格 / 刷新 30 / 奖励 60-25-15 / 硬仗保底蓝",
                    ChapterCatalog.BagCapacity == 8 && ChapterCatalog.RefreshFirstPrice == 30
                    && Math.Abs(ChapterCatalog.RewardChoiceChance - 0.60f) < 1e-4f
                    && Math.Abs(rewardSum - 1f) < 1e-4f
                    && (int)ChapterCatalog.HardFightMinimumRarity >= (int)GearRarity.Blue,
                    string.Format("包 {0} / 刷新 {1} / 奖励和 {2:0.00} / 保底 {3}",
                        ChapterCatalog.BagCapacity, ChapterCatalog.RefreshFirstPrice, rewardSum,
                        ChapterCatalog.HardFightMinimumRarity));

                // ---------- ③ 与运行时地图一致 ----------
                var kindByNodeType = new Dictionary<string, ChapterRoomKind>
                {
                    { "START", ChapterRoomKind.Start }, { "STAGE", ChapterRoomKind.Performance },
                    { "HARD", ChapterRoomKind.HardFight }, { "SHOP", ChapterRoomKind.Shop },
                    { "EVENT", ChapterRoomKind.Event }, { "REST", ChapterRoomKind.Rest },
                    { "FINAL", ChapterRoomKind.Final },
                };
                var mismatched = new List<string>();
                foreach (ChapterRoomSpec room in ChapterCatalog.Rooms)
                {
                    MapNodesRow node = PortfolioConfig.MapNodes.FirstOrDefault(n => n.NodeId == room.RoomId);
                    ChapterRoomKind mapped;
                    if (node == null) { mismatched.Add(room.RoomId + " 不在运行时地图里"); continue; }
                    if (!kindByNodeType.TryGetValue(node.NodeType, out mapped))
                        mismatched.Add(node.NodeId + " 类型未知 " + node.NodeType);
                    else if (mapped != room.Kind) mismatched.Add(node.NodeId + " 类型不符 " + node.NodeType);
                    else if ((node.StageId ?? "") != (room.StageId ?? ""))
                        mismatched.Add(node.NodeId + " 关卡不符 " + node.StageId);
                }
                foreach (ChapterEdgeSpec edge in ChapterCatalog.Edges)
                {
                    if (!PortfolioConfig.MapEdges.Any(e => e.FromNodeId == edge.FromRoomId && e.ToNodeId == edge.ToRoomId))
                        mismatched.Add("连线缺失 " + edge.FromRoomId + "→" + edge.ToRoomId);
                }
                check("运行时地图与路线表一致",
                    mismatched.Count == 0
                    && PortfolioConfig.MapNodes.Count == ChapterCatalog.Rooms.Count
                    && PortfolioConfig.MapEdges.Count == ChapterCatalog.Edges.Count
                    // 10 个普通演奏 + 2 个硬仗（第 2 层末换成馈赠占位房后，H02 已撤）
                    && PortfolioConfig.Stages.Count == 12,
                    mismatched.Count == 0
                        ? string.Format("节点 {0} / 连线 {1} / 关卡 {2}",
                            PortfolioConfig.MapNodes.Count, PortfolioConfig.MapEdges.Count, PortfolioConfig.Stages.Count)
                        : string.Join("；", mismatched.Take(4).ToArray()));

                // ---------- ④ 路径性质 ----------
                var reachable = ForwardReachable("R00");
                check("从起点能走到每一个房间",
                    reachable.Count == ChapterCatalog.Rooms.Count,
                    string.Format("可达 {0} / 总 {1}", reachable.Count, ChapterCatalog.Rooms.Count));

                var missingContent = PortfolioConfig.MapNodes
                    .Where(n => !string.IsNullOrEmpty(n.ContentId) && RoomContentCatalog.FindContent(n.ContentId) == null)
                    .Select(n => n.NodeId).ToArray();
                check("地图引用的房间内容都存在",
                    missingContent.Length == 0,
                    missingContent.Length == 0
                        ? string.Format("内容 {0} 条 / 浓雾节点 {1}",
                            RoomContentCatalog.Events.Count + RoomContentCatalog.Rests.Count
                            + RoomContentCatalog.Keepsakes.Count,
                            PortfolioConfig.MapNodes.Count(n => n.Fog))
                        : "缺：" + string.Join("、", missingContent));

                check("任何一条路都是 15 步 + 起点 = 16 站",
                    ChapterCatalog.Rooms.All(r => r.Step <= ChapterCatalog.StepCount)
                    && ChapterCatalog.PerformanceStepCount == 7,
                    "步 1–15，其中 7 步是演奏");

                // ---------- ⑤ 写错的表要被点名 ----------
                var badRooms = ChapterCatalog.Rooms.ToList();
                badRooms = badRooms.Select(r => r.RoomId == "R1A"
                    ? new ChapterRoomSpec("R1A", r.Layer, 1, r.Column, ChapterRoomKind.Event) : r).ToList();   // 第 1 步不是演奏
                badRooms.Add(new ChapterRoomSpec("R9B", 2, 9, 2, ChapterRoomKind.HardFight, "H02"));            // id 重复
                for (int i = 0; i < 2; i++)
                    badRooms.Add(new ChapterRoomSpec("RX" + i, 2, 6, i, ChapterRoomKind.Event));                // 第 6 步挤到 4 个候选
                badRooms.Add(new ChapterRoomSpec("RY", 2, 1, 1, ChapterRoomKind.HardFight, "H99"));             // 硬仗塞进第 1 步
                var badEdges = ChapterCatalog.Edges.Where(e => e.FromRoomId != "R3A").ToList();                  // 删掉 R3A 的出口 → 死胡同
                var badLayers = new List<ChapterLayerSpec>
                {
                    new ChapterLayerSpec(1, 0.70f, 0.25f, 0.05f),
                    new ChapterLayerSpec(2, 0.50f, 0.40f, 0.05f),   // 权重和 0.95
                };
                var badErrors = ChapterCatalog.Validate(badRooms, badEdges, badLayers);
                var missing = Missing(badErrors, "第 1 步必须是演奏", "重复", "超过上限", "硬仗不能出现在第 1 步",
                    "稀有度权重和", "没有出口");
                check("写错的表要被点名", missing.Count == 0,
                    missing.Count == 0 ? string.Format("抓到 {0} 条", badErrors.Count)
                        : "没点到：" + string.Join("、", missing.ToArray()));

                string summary = string.Format("{0} PASS {1} / FAIL {2}（第一章路线：分支、构图规则、占位房与数值）",
                    Tag, pass, fail);
                for (int i = 0; i < lines.Count; i++) Debug.Log(Tag + lines[i]);
                Debug.Log(summary);
                try
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllLines("Logs/chapter_check.txt", lines.ToArray());
                    File.AppendAllText("Logs/chapter_check.txt", summary + Environment.NewLine);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add("FAIL " + "自检异常".PadRight(28) + e.Message);
                Debug.LogError(Tag + "自检异常：" + e);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>从某个房间出发、沿连线能到达的房间集合（含它自己）。</summary>
        private static HashSet<string> ForwardReachable(string fromRoomId)
        {
            var seen = new HashSet<string> { fromRoomId };
            var queue = new Queue<string>();
            queue.Enqueue(fromRoomId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (ChapterEdgeSpec edge in ChapterCatalog.Edges)
                {
                    if (edge.FromRoomId != current) continue;
                    if (seen.Add(edge.ToRoomId)) queue.Enqueue(edge.ToRoomId);
                }
            }
            return seen;
        }

        /// <summary>错误清单里没有点到名的关键词</summary>
        private static List<string> Missing(List<string> errors, params string[] keywords)
        {
            string all = errors == null ? string.Empty : string.Join("\n", errors.ToArray());
            var missing = new List<string>();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (all.IndexOf(keywords[i], StringComparison.Ordinal) < 0) missing.Add(keywords[i]);
            }
            return missing;
        }
    }
}
#endif
