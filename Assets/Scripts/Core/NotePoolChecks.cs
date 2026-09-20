#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FallenAngel.Data;
using FallenAngel.Gameplay;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 音符对象池自检：同屏数估算 + 按谱面预热。
    ///
    /// 背景（2026-09-20，参考 OpenSekai 的做法）：对象池容量应当由**谱面本身**决定
    /// （他们用 CalculateMaxSimultaneousNotes 算最大同频存活数），而不是拍一个常量；
    /// 并且要在加载页把实例造好，避免演奏中途 Instantiate 造成掉帧。
    ///
    /// 只读 + 临时对象；菜单与 batchmode（-executeMethod FallenAngel.Core.NotePoolChecks.Run）两用。
    /// </summary>
    public static class NotePoolChecks
    {
        private static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/FallenAngel/Validate Note Pool")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-24} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                RunEstimatorCases(check);
                RunPrewarmCases(check);
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-24} 自检异常：{1}", "执行", e));
            }

            string summary = string.Format(
                "音符对象池自检：{0} 项通过 / {1} 项异常（判定：同屏数估算 + 长按占位 + 首尾相接不重复计 + 按谱面预热 + 上下限）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[NotePool] " + line);
            Debug.Log("[NotePool] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/note_pool_check.txt", lines.ToArray());
                File.AppendAllText("Logs/note_pool_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NotePool] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        private static List<NoteData> Chart(params float[] times)
        {
            var list = new List<NoteData>();
            for (int i = 0; i < times.Length; i++) list.Add(new NoteData(0, times[i], NoteType.Normal));
            return list;
        }

        private static void RunEstimatorCases(Action<string, bool, string> check)
        {
            // ① 空谱不塌到 0
            check("空谱至少留 1", NoteSpawner.EstimateMaxSimultaneousNotes(new List<NoteData>(), 1f) == 1, "空谱 → 1");

            // ② 同一屏内 5 个音符
            int burst = NoteSpawner.EstimateMaxSimultaneousNotes(Chart(0f, 0.1f, 0.2f, 0.3f, 0.4f), 1f);
            check("同屏 5 个", burst == 5, string.Format("5 个音符挤在 0.4s 内（下落 1s）→ {0}", burst));

            // ③ 分散排布只占 1
            int spread = NoteSpawner.EstimateMaxSimultaneousNotes(Chart(0f, 5f, 10f, 15f), 1f);
            check("分散排布 = 1", spread == 1, string.Format("每 5s 一个（下落 1s）→ {0}", spread));

            // ④ 首尾相接不算同屏（t=0 的区间到 1s 结束，t=1s 的开始）
            int adjacent = NoteSpawner.EstimateMaxSimultaneousNotes(Chart(0f, 1f), 1f);
            check("首尾相接不重复计", adjacent == 1, string.Format("相隔一个下落时长 → {0}", adjacent));

            // ⑤ 长按按持续时长占屏：1 个 3s 长按 + 中间 2 个普通音符
            //    注意两个普通音符要**真的重叠**：间隔 1s 恰好首尾相接时同屏数只有 2（下面的用例说明这一点）
            var withHold = new List<NoteData>
            {
                new NoteData(0, 0f, NoteType.LongStart, 3f, 1),
                new NoteData(1, 1f, NoteType.Normal),
                new NoteData(2, 1.5f, NoteType.Normal),
            };
            int hold = NoteSpawner.EstimateMaxSimultaneousNotes(withHold, 1f);
            check("长按按持续时长占屏", hold == 3, string.Format("3s 长按 + 1.0s/1.5s 两个普通音符 → {0}", hold));

            // ⑤b 首尾相接的普通音符（间隔 = 下落时长）只算 1 个在场——估算器不会高估
            int touching = NoteSpawner.EstimateMaxSimultaneousNotes(Chart(0f, 1f), 1f);
            check("相接音符不高估", touching == 1, string.Format("间隔正好一个下落时长 → {0}", touching));

            // ⑥ 超过下落窗的普通音符不算同屏（第 3 个在 2.5s，超出 t=0 的 1s 窗）
            int tail = NoteSpawner.EstimateMaxSimultaneousNotes(Chart(0f, 0.5f, 2.5f), 1f);
            check("超出下落窗不计", tail == 2, string.Format("0 / 0.5 / 2.5s（下落 1s）→ {0}", tail));
        }

        private static void RunPrewarmCases(Action<string, bool, string> check)
        {
            GameObject host = null;
            GameObject prefab = null;
            bool savedInstance = false;
            NoteSpawner saved = NoteSpawner.Instance;
            try
            {
                host = new GameObject("__NotePoolCheckHost", typeof(RectTransform));
                host.SetActive(false);
                var spawner = host.AddComponent<NoteSpawner>();
                typeof(NoteSpawner).GetField("notesContainer", Priv).SetValue(spawner, (RectTransform)host.transform);

                // 批处理非播放模式不跑 Awake：手动造缺省预制体并建池
                Note defaultNote = (Note)typeof(NoteSpawner).GetMethod("CreateDefaultNotePrefab", Priv)
                    .Invoke(spawner, null);
                prefab = defaultNote != null ? defaultNote.gameObject : null;
                typeof(NoteSpawner).GetField("notePrefab", Priv).SetValue(spawner, defaultNote);
                typeof(NoteSpawner).GetMethod("InitializePool", Priv).Invoke(spawner, null);

                int defaultSize = (int)typeof(NoteSpawner).GetField("defaultPoolSize", Priv).GetValue(spawner);
                int maxSize = (int)typeof(NoteSpawner).GetField("maxPoolSize", Priv).GetValue(spawner);

                // ⑦ 预热到估算值
                int afterWarm = spawner.PrewarmPool(30);
                check("预热到指定数量", afterWarm >= 30, string.Format("请求 30 → 池中空闲 {0}", afterWarm));

                // ⑧ 下限：请求 0 也要有 defaultPoolSize
                var host2 = new GameObject("__NotePoolCheckHost2", typeof(RectTransform));
                host2.SetActive(false);
                var spawner2 = host2.AddComponent<NoteSpawner>();
                typeof(NoteSpawner).GetField("notesContainer", Priv).SetValue(spawner2, (RectTransform)host2.transform);
                Note defaultNote2 = (Note)typeof(NoteSpawner).GetMethod("CreateDefaultNotePrefab", Priv)
                    .Invoke(spawner2, null);
                GameObject prefab2 = defaultNote2 != null ? defaultNote2.gameObject : null;
                typeof(NoteSpawner).GetField("notePrefab", Priv).SetValue(spawner2, defaultNote2);
                typeof(NoteSpawner).GetMethod("InitializePool", Priv).Invoke(spawner2, null);
                int floor = spawner2.PrewarmPool(0);
                check("预热有下限", floor == defaultSize, string.Format("请求 0 → {0}（defaultPoolSize={1}）", floor, defaultSize));

                // ⑨ 上限：请求远超 maxPoolSize
                int ceiling = spawner2.PrewarmPool(9999);
                check("预热不超上限", ceiling <= maxSize, string.Format("请求 9999 → {0}（maxPoolSize={1}）", ceiling, maxSize));

                // ⑩ 按谱面预热：20 个同屏音符 → 至少 20
                var dense = new List<NoteData>();
                for (int i = 0; i < 20; i++) dense.Add(new NoteData(i % 5, i * 0.02f, NoteType.Normal));
                var chart = new ChartData();
                chart.notes = dense;
                int byChart = spawner2.PrewarmForChart(chart, 2f);
                int expect = NoteSpawner.EstimateMaxSimultaneousNotes(dense, 2f);
                check("按谱面预热", byChart >= expect && expect == 20,
                    string.Format("20 个同屏音符 → 估算 {0}、池中空闲 {1}", expect, byChart));

                UnityEngine.Object.DestroyImmediate(prefab2);
                UnityEngine.Object.DestroyImmediate(host2);
                savedInstance = true;
            }
            finally
            {
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (savedInstance) { /* 沙箱实例随 host 销毁自动置空 */ }
                else if (saved != null) typeof(NoteSpawner).GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(null, saved);
            }
        }
    }
}
#endif
