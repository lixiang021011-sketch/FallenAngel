using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FallenAngel.Data;

namespace FallenAngel.Data
{
    /// <summary>
    /// 谱面加载器 - 支持从JSON文件加载自定义谱面（v1/v2 按 formatVersion 分派）
    /// </summary>
    public static class ChartLoader
    {
        // ==================== v2 协议 DTO 层 ====================
        // JsonUtility 不支持字符串→枚举（v2 的 type/direction 是字符串），
        // 必须经 DTO 反序列化后再转换为内部 NoteData。

        /// <summary>v2 谱面 DTO（type/direction 为字符串）</summary>
        [System.Serializable]
        public class ChartV2Dto
        {
            public ChartMetadata metadata;
            public List<NoteV2Dto> notes;
            public List<ChartEvent> events;
        }

        /// <summary>v2 音符 DTO</summary>
        [System.Serializable]
        public class NoteV2Dto
        {
            public string type;         // "tap"/"hold"/"drag"/"flick"/"slide"
            public float time;
            public int lane;
            public float duration;
            public string direction;    // flick: "up"/"down"
            public List<SlidePathPoint> path;
            public bool wide;           // 宽音符（全宽视觉 + 任意键判定，kick 语义；仅 tap/hold 有效）
        }

        /// <summary>版本探针：只取 metadata 判断 formatVersion（v1 缺失=0）</summary>
        [System.Serializable]
        public class ChartFormatProbe
        {
            public ChartMetadata metadata;
        }

        /// <summary>
        /// 从Resources/Charts目录加载JSON谱面
        /// </summary>
        /// <param name="chartName">谱面文件名（不含.json和路径）</param>
        public static ChartData LoadFromResources(string chartName)
        {
            TextAsset jsonFile = Resources.Load<TextAsset>($"Charts/{chartName}");
            if (jsonFile == null)
            {
                Debug.LogError($"[ChartLoader] 无法找到谱面文件: Charts/{chartName}");
                return null;
            }

            return LoadFromJson(jsonFile.text);
        }

        /// <summary>
        /// 从JSON字符串解析谱面数据（按 metadata.formatVersion 分派 v1/v2）
        /// </summary>
        public static ChartData LoadFromJson(string jsonText)
        {
            try
            {
                // 版本探针：v1 谱面无 formatVersion 字段 → 0
                int formatVersion = 0;
                ChartFormatProbe probe = JsonUtility.FromJson<ChartFormatProbe>(jsonText);
                if (probe?.metadata != null)
                    formatVersion = probe.metadata.formatVersion;

                ChartData chart = formatVersion >= 2
                    ? ConvertV2ToChart(JsonUtility.FromJson<ChartV2Dto>(jsonText))
                    : JsonUtility.FromJson<ChartData>(jsonText);

                if (chart == null)
                {
                    Debug.LogError("[ChartLoader] 谱面解析失败: JSON为空或格式错误");
                    return null;
                }

                chart.SortNotes();
                Debug.Log($"[ChartLoader] 成功加载谱面: {chart.metadata?.songName ?? "未知"}, 版本 v{chart.metadata?.formatVersion ?? 0}, 键数 {chart.LaneCount}, 音符数: {chart.notes.Count}");
                return chart;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ChartLoader] 谱面解析异常: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// v2 谱面 DTO → 内部 ChartData。
        /// 映射：tap→Normal；hold→LongStart+LongEnd（拆分，协议约定）；drag/flick/slide→新类型。
        /// </summary>
        public static ChartData ConvertV2ToChart(ChartV2Dto dto)
        {
            if (dto == null) return null;

            ChartData chart = new ChartData();
            chart.metadata = dto.metadata ?? new ChartMetadata();
            chart.metadata.formatVersion = 2;
            if (chart.metadata.noteCounts == null)
                chart.metadata.noteCounts = new NoteCounts();

            // metadata 完整性校验（v2 转换器可能缺字段，如 chart_tools 样例）
            if (chart.metadata.bpm <= 0f)
                Debug.LogWarning($"[ChartLoader] v2 谱面 metadata.bpm 缺失（{chart.metadata.songName}），游玩进度相关功能可能异常");
            if (string.IsNullOrEmpty(chart.metadata.audioFileName))
                Debug.LogWarning($"[ChartLoader] v2 谱面 metadata.audioFileName 缺失（{chart.metadata.songName}），BGM 将无法加载");

            // 类型计数（校验 noteCounts 用）
            int[] actual = new int[5]; // tap hold drag flick slide
            int longNoteIdCounter = 0;

            if (dto.notes != null)
            {
                foreach (NoteV2Dto n in dto.notes)
                {
                    if (n == null) continue;
                    switch (n.type)
                    {
                        case "tap":
                            chart.notes.Add(new NoteData(n.lane, n.time, NoteType.Normal,
                                0f, -1, FlickDirection.Up, null, n.wide));
                            actual[0]++;
                            break;
                        case "hold":
                        {
                            int id = longNoteIdCounter++;
                            float endTime = n.time + n.duration;
                            chart.notes.Add(new NoteData(n.lane, n.time, NoteType.LongStart,
                                n.duration, id, FlickDirection.Up, null, n.wide));
                            chart.notes.Add(new NoteData(n.lane, endTime, NoteType.LongEnd,
                                0f, id, FlickDirection.Up, null, n.wide));
                            actual[1]++;
                            break;
                        }
                        case "drag":
                            chart.notes.Add(new NoteData(n.lane, n.time, NoteType.Drag));
                            actual[2]++;
                            break;
                        case "flick":
                            chart.notes.Add(new NoteData(n.lane, n.time, NoteType.Flick,
                                0f, -1, ParseFlickDirection(n.direction)));
                            actual[3]++;
                            break;
                        case "slide":
                        {
                            // 路径或时长缺失 → 降级普通音符（防御，不崩溃）
                            if (n.path == null || n.path.Count == 0 || n.duration <= 0f)
                            {
                                Debug.LogWarning($"[ChartLoader] v2 slide 缺少 path 或 duration，降级为普通音符 (time={n.time:F2})");
                                chart.notes.Add(new NoteData(n.lane, n.time, NoteType.Normal));
                                break;
                            }
                            int lane = Mathf.RoundToInt(n.path[0].x);
                            chart.notes.Add(new NoteData(lane, n.time, NoteType.Slide, n.duration, -1,
                                FlickDirection.Up, n.path));
                            actual[4]++;
                            break;
                        }
                        default:
                            Debug.LogWarning($"[ChartLoader] 未知 v2 音符类型 \"{n.type}\"，按普通音符处理 (time={n.time:F2})");
                            chart.notes.Add(new NoteData(n.lane, n.time, NoteType.Normal));
                            actual[0]++;
                            break;
                    }
                }
            }

            // events 通道
            if (dto.events != null && dto.events.Count > 0)
                chart.events.AddRange(dto.events);

            // noteCounts 校验（协议约定；不符仅警告不阻断）
            NoteCounts declared = chart.metadata.noteCounts;
            if (declared.tap != actual[0] || declared.hold != actual[1] ||
                declared.drag != actual[2] || declared.flick != actual[3] || declared.slide != actual[4])
            {
                Debug.LogWarning($"[ChartLoader] v2 谱面 noteCounts 与实际不符: " +
                    $"声明(tap={declared.tap} hold={declared.hold} drag={declared.drag} flick={declared.flick} slide={declared.slide}) " +
                    $"实际(tap={actual[0]} hold={actual[1]} drag={actual[2]} flick={actual[3]} slide={actual[4]})");
            }

            Debug.Log($"[ChartLoader] v2 谱面: song={chart.metadata.songName} notes={chart.notes.Count} " +
                $"(drag={actual[2]} flick={actual[3]} slide={actual[4]}) events={chart.events.Count}");
            return chart;
        }

        /// <summary>flick direction 字符串 → 枚举（非法值警告后按 Up）</summary>
        private static FlickDirection ParseFlickDirection(string direction)
        {
            if (string.Equals(direction, "down", System.StringComparison.OrdinalIgnoreCase))
                return FlickDirection.Down;
            if (!string.IsNullOrEmpty(direction) &&
                !string.Equals(direction, "up", System.StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"[ChartLoader] 非法 flick direction \"{direction}\"，按 Up 处理");
            return FlickDirection.Up;
        }

        /// <summary>
        /// 加载全部可用谱面（歌单）：Resources/Charts 下所有 TextAsset。
        /// 默认过滤 demo_ 前缀（地图歌单用）；includeDemo=true 时全量（选歌界面用，测试条目更多）。
        /// 无论哪种模式，无 audioFileName 的谱面（不可游玩，如协议样例）一律跳过。
        /// </summary>
        public static List<ChartData> LoadAllChartPool(bool includeDemo = false)
        {
            List<ChartData> pool = new List<ChartData>();
            TextAsset[] assets = Resources.LoadAll<TextAsset>("Charts");
            int skipped = 0;

            foreach (TextAsset asset in assets)
            {
                if (asset == null) continue;
                if (!includeDemo && asset.name.StartsWith("demo_"))
                {
                    skipped++;
                    continue;
                }
                ChartData chart = LoadFromJson(asset.text);
                if (chart == null || chart.metadata == null || chart.notes == null ||
                    string.IsNullOrEmpty(chart.metadata.audioFileName))
                {
                    skipped++;
                    continue;
                }
                pool.Add(chart);
            }

            Debug.Log($"[ChartLoader] 歌单加载完成: {pool.Count} 首可用（跳过 {skipped} 个）");
            return pool;
        }

        /// <summary>
        /// 将谱面数据序列化为JSON字符串
        /// </summary>
        public static string SaveToJson(ChartData chart)
        {
            return JsonUtility.ToJson(chart, true);
        }

        /// <summary>
        /// 保存谱面为JSON文件（仅Editor模式可用）
        /// </summary>
        public static void SaveToFile(ChartData chart, string filePath)
        {
            string json = SaveToJson(chart);
#if UNITY_EDITOR
            File.WriteAllText(filePath, json);
            Debug.Log($"[ChartLoader] 谱面已保存至: {filePath}");
#else
            Debug.LogWarning("[ChartLoader] 运行时无法写入文件，请在Editor模式下使用");
#endif
        }

        /// <summary>
        /// 生成示例谱面（用于测试）
        /// </summary>
        public static ChartData GenerateDemoChart()
        {
            ChartData chart = new ChartData();
            chart.metadata = new ChartMetadata
            {
                songName = "堕落天使 - Demo",
                songArtist = "FallenAngel Team",
                chartAuthor = "AutoGenerated",
                difficulty = Difficulty.Normal,
                level = 7,
                bpm = 140f,
                offset = 0f,
                audioFileName = "demo_song",
                previewStartTime = 10f,
                previewDuration = 15f
            };

            // 生成一些简单的测试音符，按BPM生成
            float beatTime = 60f / chart.metadata.bpm;
            float currentTime = 2f; // 从2秒开始
            int longNoteIdCounter = 0;

            // 第一段：基础单键练习
            for (int i = 0; i < 16; i++)
            {
                chart.notes.Add(new NoteData(i % 4, currentTime, NoteType.Normal));
                currentTime += beatTime;
            }

            // 第二段：双键组合
            for (int i = 0; i < 8; i++)
            {
                chart.notes.Add(new NoteData(0, currentTime, NoteType.Normal));
                chart.notes.Add(new NoteData(3, currentTime, NoteType.Normal));
                currentTime += beatTime;

                chart.notes.Add(new NoteData(1, currentTime, NoteType.Normal));
                chart.notes.Add(new NoteData(2, currentTime, NoteType.Normal));
                currentTime += beatTime;
            }

            // 第三段：长按音符
            for (int i = 0; i < 4; i++)
            {
                float longDuration = beatTime * 2;
                int id = longNoteIdCounter++;
                chart.notes.Add(new NoteData(i, currentTime, NoteType.LongStart, longDuration, id));
                chart.notes.Add(new NoteData(i, currentTime + longDuration, NoteType.LongEnd, 0f, id));
                currentTime += beatTime * 3;
            }

            // 第四段：快速连打
            float sixteenthNote = beatTime * 0.25f;
            int[] pattern = { 0, 1, 2, 3, 3, 2, 1, 0, 0, 2, 1, 3, 3, 1, 2, 0 };
            for (int i = 0; i < pattern.Length; i++)
            {
                chart.notes.Add(new NoteData(pattern[i], currentTime, NoteType.Normal));
                currentTime += sixteenthNote;
            }

            // 结尾：同时长按
            float finalLongDuration = beatTime * 4;
            for (int l = 0; l < 4; l++)
            {
                int id = longNoteIdCounter++;
                chart.notes.Add(new NoteData(l, currentTime, NoteType.LongStart, finalLongDuration, id));
                chart.notes.Add(new NoteData(l, currentTime + finalLongDuration, NoteType.LongEnd, 0f, id));
            }

            chart.SortNotes();
            return chart;
        }
    }
}
