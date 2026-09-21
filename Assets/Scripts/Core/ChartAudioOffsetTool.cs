#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.Data;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 谱面「音画对齐」体检工具（只读，不改任何谱面/音频）。
    ///
    /// 为什么需要：作者反馈"测试谱的音画不齐，差了好几秒，音频和谱子的初始位置不一样"。
    /// 根源是**音频文件开头有一段静音**（导出留白/母带前导），而谱面 metadata.offset 是 0：
    /// 于是第一个音符在"音乐真正响起来之前好几秒"就到判定线了。
    /// 对齐口径：SongTime = 音频时间 − (metadata.offset + 校准 + 输出延迟补偿)，
    /// 所以 **metadata.offset 应当等于音频开头的静音秒数**（正偏移 = 音符整体推迟 = 等静音过去）。
    ///
    /// 本工具逐个谱面量出"音频静音长度 / 谱面现有偏移 / 建议偏移"，写进
    /// Logs/chart_audio_offset.txt 并打进 Console，**不写回谱面**（改谱面由作者决定）。
    ///
    /// batchmode：-executeMethod FallenAngel.Core.ChartAudioOffsetTool.Run
    /// </summary>
    public static class ChartAudioOffsetTool
    {
        private const float LoudThreshold = 0.004f;
        private const float SustainSeconds = 0.05f;

        [MenuItem("Tools/FallenAngel/Measure Chart Audio Offset")]
        public static void Run()
        {
            RunInternal(false);
        }

        /// <summary>
        /// 按实测静音把 metadata.offset 写回谱面（**只改 offset 这一个数字**，其余字节不动）。
        /// 只读版本先看体检表；确认无误再跑这个。
        /// batchmode：-executeMethod FallenAngel.Core.ChartAudioOffsetTool.ApplyOffsets
        /// </summary>
        [MenuItem("Tools/FallenAngel/Apply Chart Audio Offsets (by measured silence)")]
        public static void ApplyOffsets()
        {
            RunInternal(true);
        }

        private static void RunInternal(bool apply)
        {
            var lines = new List<string>();
            lines.Add(apply ? "== 模式：写回 offset（只改 offset 字段） ==" : "== 模式：只读体检 ==");
            TextAsset[] charts = Resources.LoadAll<TextAsset>("Charts");
            lines.Add(string.Format("谱面数 {0}（Resources/Charts）", charts.Length));
            lines.Add(string.Format("{0,-26} {1,-24} {2,9} {3,9} {4,9} {5,10}", "谱面", "音频", "静音(s)", "现有偏移", "建议偏移", "首音符"));

            int measured = 0, failed = 0;
            foreach (TextAsset asset in charts)
            {
                ChartData chart = null;
                try { chart = JsonUtility.FromJson<ChartData>(asset.text); }
                catch (Exception e) { lines.Add(asset.name + " 解析失败：" + e.Message); failed++; continue; }
                if (chart == null || chart.metadata == null) { failed++; continue; }

                string audioName = chart.metadata.audioFileName;
                float firstNote = FirstNoteTime(chart);
                string row = string.Format("{0,-26} {1,-24}", asset.name,
                    string.IsNullOrEmpty(audioName) ? "(无音频)" : audioName);

                if (string.IsNullOrEmpty(audioName))
                {
                    lines.Add(row + string.Format(" {0,9} {1,9:0.###} {2,9} {3,10:0.###}",
                        "-", chart.metadata.offset, "无音频", firstNote));
                    continue;
                }

                AudioClip clip = Resources.Load<AudioClip>("Audio/" + audioName);
                if (clip == null)
                {
                    lines.Add(row + string.Format(" {0,9} {1,9:0.###} {2,9} {3,10:0.###}",
                        "缺文件", chart.metadata.offset, "-", firstNote));
                    failed++;
                    continue;
                }

                float silence;
                if (!TryMeasureLeadingSilence(clip, out silence))
                {
                    lines.Add(row + string.Format(" {0,9} {1,9:0.###} {2,9} {3,10:0.###}",
                        "读不到", chart.metadata.offset, "-", firstNote));
                    failed++;
                    continue;
                }

                float suggested = (float)Math.Round(silence, 3);
                string hint = Mathf.Abs(suggested - chart.metadata.offset) > 0.05f ? " ← 建议改" : "  ok";
                lines.Add(row + string.Format(" {0,9:0.###} {1,9:0.###} {2,9:0.###} {3,10:0.###}",
                    silence, chart.metadata.offset, suggested, firstNote) + hint
                    + string.Format("（音频长 {0:0.##}s）", clip.length));
                measured++;

                if (apply && Mathf.Abs(suggested - chart.metadata.offset) > 0.05f)
                {
                    string applied = TryRewriteOffset(asset, suggested);
                    lines.Add("        → 写回： " + applied);
                }
            }

            string summary = string.Format(
                "谱面音画体检：{0} 个可测 / {1} 个有问题（口径：建议偏移 = 音频开头静音秒数；正偏移 = 音符整体推迟）",
                measured, failed);
            lines.Add(summary);
            foreach (string line in lines) Debug.Log("[ChartAudioOffset] " + line);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/chart_audio_offset.txt", lines.ToArray());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ChartAudioOffset] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// 把谱面资源里的 `"offset": <数字>` 改成指定值（只动这一个数字）。
        /// 为什么要做"文本级微改"：用 JsonUtility 重写会重新序列化整个文件（字段顺序/格式全变），
        /// diff 会变得没法审；只替换一个数字，diff 一眼能看懂。
        /// </summary>
        private static string TryRewriteOffset(TextAsset asset, float seconds)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return "找不到资源路径";

            string full = Path.GetFullPath(path);
            string text = File.ReadAllText(full);
            var match = System.Text.RegularExpressions.Regex.Match(text, "\"offset\"\\s*:\\s*(-?[0-9]+\\.?[0-9]*)");
            if (!match.Success) return "没找到 offset 字段";

            string backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "chart_offset_backup");
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(Path.Combine(backupDir, asset.name + ".json"), text);

            string replaced = text.Substring(0, match.Groups[1].Index)
                + seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + text.Substring(match.Groups[1].Index + match.Groups[1].Length);
            File.WriteAllText(full, replaced);
            AssetDatabase.ImportAsset(path);
            return string.Format("{0} → {1:0.###}（原文件备份在 Logs/chart_offset_backup/）", match.Groups[1].Value, seconds);
        }

        private static float FirstNoteTime(ChartData chart)
        {
            float first = float.MaxValue;
            if (chart.notes == null) return 0f;
            foreach (NoteData n in chart.notes)
            {
                if (n != null && n.time < first) first = n.time;
            }
            return first == float.MaxValue ? 0f : first;
        }

        private static bool TryMeasureLeadingSilence(AudioClip clip, out float silenceSeconds)
        {
            silenceSeconds = 0f;
            int channels = clip.channels;
            int rate = clip.frequency;
            if (channels <= 0 || rate <= 0) return false;

            const int chunkSeconds = 2;
            int chunkSamples = rate * chunkSeconds;
            var buffer = new float[chunkSamples * channels];
            int sustainNeeded = Mathf.Max(1, Mathf.RoundToInt(SustainSeconds * rate));
            int sustained = 0;
            int offsetSamples = 0;

            while (offsetSamples < clip.samples)
            {
                int want = Mathf.Min(chunkSamples, clip.samples - offsetSamples);
                if (!clip.GetData(buffer, offsetSamples)) return false;

                for (int i = 0; i < want; i++)
                {
                    float peak = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        peak = Mathf.Max(peak, Mathf.Abs(buffer[i * channels + c]));
                    }

                    if (peak >= LoudThreshold)
                    {
                        sustained++;
                        if (sustained >= sustainNeeded)
                        {
                            int startSample = offsetSamples + i - sustainNeeded + 1;
                            silenceSeconds = Mathf.Max(0f, startSample / (float)rate);
                            return true;
                        }
                    }
                    else
                    {
                        sustained = 0;
                    }
                }

                offsetSamples += want;
            }

            return true;
        }
    }
}
#endif
