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
    /// 本地自测曲包自检：确认放进去的第三方曲包**真的能被游戏加载**。
    ///
    /// 背景：本地自测曲包（sekai.best 官方 SUS 导入）是按 .gitignore 排除的版权素材，
    /// 只在作者本机存在。所以本自检在**没放曲包时跳过**（SKIP，不算失败），
    /// 放了就逐条检查：谱面能解析成 ChartData、audioFileName 有值、对应音频在 Resources/Audio 里。
    /// 这样"曲包放错地方 / 忘了放音频 / 谱面格式坏掉"这几类问题都能立刻看出来。
    ///
    /// 只读；菜单与 batchmode（-executeMethod FallenAngel.Core.LocalSongPackChecks.Run）两用。
    /// </summary>
    public static class LocalSongPackChecks
    {
        private const string ChartFolder = "Assets/Resources/Charts";
        private const string AudioFolder = "Assets/Resources/Audio";

        /// <summary>
        /// 本地曲包的前缀：`pjsk_`（版权素材，不入库，仅作者本机）与 `cc_`（CC-BY 可再分发，
        /// 由 `chart_tools/cc_song_import.py` 从音频自动成谱，见 chart_tools/cc_song_pack.md）。
        /// </summary>
        /// <remarks>
        /// `phira_`：Phira 社区谱（`chart_tools/phira_import.py` 转成 v2），
        /// 谱面站对象模型没有 license 字段 → 默认保留所有权利，只当**本地测试素材**，不进发行包。
        /// </remarks>
        private static readonly string[] Prefixes = { "pjsk_", "cc_", "phira_" };

        [MenuItem("Tools/FallenAngel/Validate Local Song Pack")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0, skip = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-26} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                if (!Directory.Exists(ChartFolder))
                {
                    lines.Add("SKIP 本地曲包                   没有 " + ChartFolder);
                    skip++;
                }
                else
                {
                    var found = new List<string>();
                    for (int p = 0; p < Prefixes.Length; p++)
                        found.AddRange(Directory.GetFiles(ChartFolder, Prefixes[p] + "*.json"));
                    string[] files = found.ToArray();
                    if (files.Length == 0)
                    {
                        lines.Add("SKIP 本地曲包                   未找到 " + string.Join("/", Prefixes)
                                  + "*.json（曲包未放入，属正常）");
                        skip++;
                    }
                    else
                    {
                        lines.Add(string.Format("INFO 本地曲包                   发现 {0} 首", files.Length));
                        foreach (string file in files)
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            ChartData chart = null;
                            try
                            {
                                chart = JsonUtility.FromJson<ChartData>(File.ReadAllText(file));
                            }
                            catch (Exception e)
                            {
                                check(name, false, "谱面解析失败：" + e.Message);
                                continue;
                            }

                            int notes = chart?.notes?.Count ?? 0;
                            string audio = chart?.metadata?.audioFileName;
                            bool hasAudio = !string.IsNullOrEmpty(audio)
                                && (File.Exists(Path.Combine(AudioFolder, audio + ".mp3"))
                                    || File.Exists(Path.Combine(AudioFolder, audio + ".ogg"))
                                    || File.Exists(Path.Combine(AudioFolder, audio + ".wav")));

                            check(name, notes > 0 && hasAudio,
                                string.Format("{0} 音符、BPM {1:0.#}、音频 {2} {3}",
                                    notes, chart?.metadata?.bpm ?? 0f,
                                    string.IsNullOrEmpty(audio) ? "(未填)" : audio,
                                    hasAudio ? "已就位" : "缺失"));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-26} 自检异常：{1}", "执行", e));
            }

            string summary = string.Format(
                "本地曲包自检：{0} 项通过 / {1} 项异常 / {2} 项跳过（曲包不入库，未放时跳过是预期行为）",
                pass, fail, skip);
            foreach (string line in lines) Debug.Log("[SongPack] " + line);
            Debug.Log("[SongPack] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/local_song_pack_check.txt", lines.ToArray());
                File.AppendAllText("Logs/local_song_pack_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SongPack] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
#endif
