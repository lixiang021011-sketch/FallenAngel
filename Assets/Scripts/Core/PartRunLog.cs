using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>一局演奏的落盘记录（真机试玩后可以直接把文件取回来汇总）。</summary>
    [Serializable]
    public sealed class PartRunRecord
    {
        public string timestamp;
        public string song;
        public string partItem;
        public float duration;
        public int switches;
        public float energy;
        public int hits;
        public int misses;
        public int lifeLost;
        public List<PartSegment> segments = new List<PartSegment>();
    }

    /// <summary>
    /// 声部试玩数据落盘：每局一行 JSON 追加到 `persistentDataPath/part_runs.jsonl`。
    ///
    /// 为什么需要：Console 日志只在编辑器/开发版能看到，手机与打包试玩拿不到；
    /// 而"玩家会不会主动切声部 / 路线是否重复 / 高密度段是否掉档"这些问题必须靠多局数据回答，
    /// 不能靠感觉。文件是 JSONL：一行一局，直接丢进表格就能对比不同道具下的路线。
    /// </summary>
    public static class PartRunLog
    {
        public const string FileName = "part_runs.jsonl";

        /// <summary>总开关：自检与调试时关掉，避免把测试局写进真实试玩数据</summary>
        public static bool Enabled = true;

        public static string DefaultPath
        {
            get { return Path.Combine(Application.persistentDataPath, FileName); }
        }

        /// <summary>把一局的结果拼成记录（纯函数，自检可以直接断言）</summary>
        public static PartRunRecord Build(GameManager gm, SongPartSession session, DateTime timestampUtc)
        {
            if (gm == null || session == null) return null;
            var record = new PartRunRecord
            {
                timestamp = timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                song = gm.CurrentChart != null && gm.CurrentChart.metadata != null
                    ? gm.CurrentChart.metadata.songName : string.Empty,
                partItem = PartEnergyRules.DisplayName(session.Item),
                duration = gm.SongTime,
                switches = session.Route.SwitchCount,
                energy = session.Energy,
                hits = session.HitCount,
                misses = session.Route.TotalMisses,
                lifeLost = TotalLifeLost(session),
            };
            IReadOnlyList<PartSegment> segments = session.Route.Segments;
            for (int i = 0; i < segments.Count; i++) record.segments.Add(segments[i]);
            return record;
        }

        /// <summary>追加一行；失败只告警不抛（试玩数据的价值不值当让结算流程崩掉）</summary>
        public static bool Append(PartRunRecord record, string path = null)
        {
            if (record == null) return false;
            string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
            try
            {
                string directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(target, JsonUtility.ToJson(record) + Environment.NewLine);
                Debug.Log("[PartRunLog] 本局已记录 → " + target);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PartRunLog] 记录失败：" + e.Message);
                return false;
            }
        }

        /// <summary>结算时调用：落盘并返回文件路径（面板显示用；失败返回 null）</summary>
        public static string Record(GameManager gm, SongPartSession session)
        {
            if (!Enabled) return null;
            PartRunRecord record = Build(gm, session, DateTime.UtcNow);
            if (record == null) return null;
            return Append(record) ? DefaultPath : null;
        }

        private static int TotalLifeLost(SongPartSession session)
        {
            int total = 0;
            IReadOnlyList<PartSegment> segments = session.Route.Segments;
            for (int i = 0; i < segments.Count; i++) total += segments[i].lifeLost;
            return total;
        }
    }
}
