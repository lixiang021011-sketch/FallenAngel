#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 局内 modifier 自检：判定窗口缩放、判定线/谱面可见性、按歌曲时间失效、同 id 刷新不叠加，
    /// 以及 D3 定案（2026-09-22）的两条新轴——**来源**（Source）与**作用域**（Scope）：
    /// 本曲（曲终自动清）vs 本次行程（跨曲保留、不按秒计时）。
    ///
    /// 只读 + 临时对象；不改场景与资源。菜单与 batchmode
    /// （-executeMethod FallenAngel.Core.PlayModifierChecks.Run）两用。
    /// </summary>
    public static class PlayModifierChecks
    {
        private const float Tol = 1e-4f;
        // 批处理（非播放模式）不跑 Awake/OnEnable：单例与订阅要显式补上（照 JudgeTimingChecks 的沙箱写法）
        private static readonly PropertyInfo PGameInstance = typeof(GameManager).GetProperty("Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo PModifierInstance = typeof(ModifierManager).GetProperty("Instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        [MenuItem("Tools/FallenAngel/Validation/Gameplay/Validate Play Modifiers", false, 510)]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            GameObject host = null;
            JudgeWindows savedBase = null;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-22} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                savedBase = JudgeWindows.Default;
                var baseWindows = new JudgeWindows();   // 0.08 / 0.16 / 0.18 / 0.20
                PlayRules.SetBaseWindows(baseWindows);

                host = new GameObject("__ModifierCheckHost");
                host.SetActive(false);                  // 不让 Update 跑，时序由自检显式控制
                var mgr = host.AddComponent<ModifierManager>();

                check("基线窗口", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f) <= Tol
                        && Mathf.Abs(PlayRules.Windows.badWindow - 0.20f) <= Tol,
                    string.Format("perfect={0:0.###} / bad={1:0.###}", PlayRules.Windows.perfectWindow, PlayRules.Windows.badWindow));

                mgr.Apply(new ModifierDef("M_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f), 0f);
                check("判定窗口放宽 1.5×", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.12f) <= Tol,
                    string.Format("perfect 0.08 → {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_LOOSE2", ModifierKind.JudgeWindowScale, 2f, 0f), 0f);
                check("窗口倍率乘法叠加", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol,
                    string.Format("0.08 × 1.5 × 2 = {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_LOOSE", ModifierKind.JudgeWindowScale, 1.5f, 0f), 0f);
                check("同 id 刷新不叠加强度", Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol,
                    string.Format("重复施加后仍为 {0:0.###}", PlayRules.Windows.perfectWindow));

                mgr.Apply(new ModifierDef("M_NOLINE", ModifierKind.JudgeLineHidden, 0f, 0f), 0f);
                check("判定线消失", !PlayRules.JudgeLineVisible, "JudgeLineVisible=false（判定逻辑不受影响）");

                mgr.Apply(new ModifierDef("M_NONOTES", ModifierKind.NotesHidden, 0f, 0f), 0f);
                check("谱面隐身", !PlayRules.NotesVisible, "NotesVisible=false（位置与判定照常）");

                // 到时失效：duration=1.0，从 t=40 起算。
                // 用窗口倍率测（此时常驻倍率为 1.5×2=0.24），避免被上面那条常驻的"判定线消失"干扰。
                mgr.Apply(new ModifierDef("M_TEMP", ModifierKind.JudgeWindowScale, 3f, 1.0f), 40f);
                mgr.Tick(40.5f);
                bool stillScaled = Mathf.Abs(PlayRules.Windows.perfectWindow - 0.72f) <= Tol;
                mgr.Tick(41.0f);
                bool reverted = Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol;
                check("按时失效", stillScaled && reverted,
                    string.Format("t=40.5 仍生效={0}（0.72）、t=41.0 已失效={1}（{2:0.###}）",
                        stillScaled, reverted, PlayRules.Windows.perfectWindow));

                // ---------- D3：来源（Source）与正负标记 ----------
                mgr.ClearAll();
                mgr.Apply(new ModifierDef("M_NODE_DEBUFF", ModifierKind.LifeDamageScale, 1.5f, 0f,
                    scope: ModifierScope.Song, source: ModifierSource.Node, debuff: true), 0f);
                mgr.Apply(new ModifierDef("M_TALENT", ModifierKind.JudgeWindowScale, 1.5f, 0f,
                    scope: ModifierScope.Song, source: ModifierSource.Talent), 0f);
                ModifierRuntime nodeRow = FindRuntime(mgr, "M_NODE_DEBUFF");
                bool sourceOk = nodeRow != null && nodeRow.def.source == ModifierSource.Node
                                && nodeRow.def.debuff && nodeRow.def.scope == ModifierScope.Song;
                check("来源与 debuff 标记透传", sourceOk && mgr.Active.Count == 2,
                    string.Format("来源={0}、debuff={1}、生效 {2} 条",
                        nodeRow != null ? nodeRow.def.source.ToString() : "缺", nodeRow != null && nodeRow.def.debuff,
                        mgr.Active.Count));

                int cleansed = mgr.RemoveDebuffs();
                check("净化只吃 debuff",
                    cleansed == 1 && !mgr.Has("M_NODE_DEBUFF") && mgr.Has("M_TALENT")
                    && Mathf.Abs(PlayRules.LifeDamageScale - 1f) <= Tol
                    && Mathf.Abs(PlayRules.Windows.perfectWindow - 0.12f) <= Tol,
                    string.Format("移除 {0} 条；受伤倍率回到 {1:0.00}、天赋 buff 仍在（窗口 {2:0.###}）",
                        cleansed, PlayRules.LifeDamageScale, PlayRules.Windows.perfectWindow));

                // ---------- D3：作用域（本曲 vs 本次行程） ----------
                mgr.ClearAll();
                mgr.Apply(new ModifierDef("M_SONG_KEEP", ModifierKind.JudgeWindowScale, 1.5f, 0f,
                    scope: ModifierScope.Song, source: ModifierSource.Item), 100f);
                mgr.Apply(new ModifierDef("M_SONG_TIMED", ModifierKind.JudgeWindowScale, 2f, 10f,
                    scope: ModifierScope.Song, source: ModifierSource.Item), 100f);   // 110 到期
                mgr.Apply(new ModifierDef("M_RUN_KEEP", ModifierKind.LifeDamageScale, 0.5f, 0f,
                    scope: ModifierScope.Run, source: ModifierSource.Build), 100f);
                mgr.Apply(new ModifierDef("M_RUN_TIMED", ModifierKind.ScoreMultiplier, 1.5f, 5f,
                    scope: ModifierScope.Run, source: ModifierSource.Build), 100f);    // 行程内没有秒表 → 常驻

                mgr.Tick(105f);
                bool allAlive = mgr.Active.Count == 4 && Mathf.Abs(PlayRules.Windows.perfectWindow - 0.24f) <= Tol;
                mgr.Tick(111f);   // 本曲秒计时到期（110）
                bool songTimedGone = !mgr.Has("M_SONG_TIMED") && mgr.Has("M_SONG_KEEP")
                                     && Mathf.Abs(PlayRules.Windows.perfectWindow - 0.12f) <= Tol;
                check("本曲秒计时到期", allAlive && songTimedGone,
                    string.Format("t=105 窗口 0.24（0.08×1.5×2）；t=111 只剩 1.5×（0.08×1.5={0:0.###}）",
                        PlayRules.Windows.perfectWindow));

                mgr.Tick(99999f);   // 覆盖整段行程的时间跨度
                check("行程作用域不按秒失效",
                    mgr.Has("M_RUN_KEEP") && mgr.Has("M_RUN_TIMED")
                    && Mathf.Abs(PlayRules.ScoreMultiplier - 1.5f) <= Tol
                    && Mathf.Abs(PlayRules.LifeDamageScale - 0.5f) <= Tol,
                    string.Format("跑过 99999s 后行程仍然生效（分 ×{0:0.00}、受伤 ×{1:0.00}，duration=5 被规范成常驻）",
                        PlayRules.ScoreMultiplier, PlayRules.LifeDamageScale));

                int ended = mgr.EndSong();
                check("曲终只清本曲作用域",
                    ended == 1 && !mgr.Has("M_SONG_KEEP") && mgr.Has("M_RUN_KEEP") && mgr.Has("M_RUN_TIMED")
                    && Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f) <= Tol,
                    string.Format("清 {0} 条本曲效果；行程 2 条保留，判定窗口回到 {1:0.###}",
                        ended, PlayRules.Windows.perfectWindow));

                mgr.EndRun();
                check("局终清空全部（含行程）",
                    mgr.Active.Count == 0 && Mathf.Abs(PlayRules.ScoreMultiplier - 1f) <= Tol
                    && Mathf.Abs(PlayRules.LifeDamageScale - 1f) <= Tol
                    && PlayRules.JudgeLineVisible && PlayRules.NotesVisible,
                    string.Format("生效 {0} 条；分 ×{1:0.00}、受伤 ×{2:0.00}、线/音符可见",
                        mgr.Active.Count, PlayRules.ScoreMultiplier, PlayRules.LifeDamageScale));

                // ---------- D3：曲终钩子（真装配 GameManager，验证"自动清"而不是"手动清"） ----------
                // 场景里若已有 GameManager / ModifierManager 就用它们，否则现建（自检不依赖场景内容）
                GameObject gmGo = null;
                bool injectedGame = false, injectedModifier = false;
                GameManager gm = GameManager.Instance;
                if (gm == null)
                {
                    gmGo = new GameObject("__ModifierCheckGM");
                    gm = gmGo.AddComponent<GameManager>();
                    PGameInstance.SetValue(null, gm);   // 批处理不跑 Awake：显式补上
                    injectedGame = true;
                }
                ModifierManager live = ModifierManager.Instance;
                if (live == null)
                {
                    host.SetActive(true);   // 激活才会走 OnEnable → 订阅 GameManager 事件
                    live = mgr;
                    PModifierInstance.SetValue(null, live);
                    injectedModifier = true;
                }
                live.Subscribe();   // 强制与当前 GameManager 同源（场景单例可能更早订过、或当时还没有 GameManager）
                live.ClearAll();
                live.Apply(new ModifierDef("M_AUTO_SONG", ModifierKind.JudgeWindowScale, 1.5f, 0f,
                    scope: ModifierScope.Song, source: ModifierSource.Item), 0f);
                live.Apply(new ModifierDef("M_AUTO_RUN", ModifierKind.LifeDamageScale, 0.5f, 0f,
                    scope: ModifierScope.Run, source: ModifierSource.Build), 0f);
                gm.EndGame();   // 曲终 → OnGameEnd → 自动清本曲作用域
                bool autoSong = !live.Has("M_AUTO_SONG") && live.Has("M_AUTO_RUN");
                live.Apply(new ModifierDef("M_AUTO_SONG", ModifierKind.JudgeWindowScale, 1.5f, 0f,
                    scope: ModifierScope.Song, source: ModifierSource.Item), 0f);
                gm.BackToMenu();   // 回落菜单（放弃本曲走这条路，不经过 OnGameEnd）
                bool autoMenu = !live.Has("M_AUTO_SONG") && live.Has("M_AUTO_RUN");
                check("曲终/回落菜单自动清本曲", autoSong && autoMenu,
                    string.Format("EndGame 后本曲已清={0}、BackToMenu 后本曲已清={1}、行程始终在={2}",
                        autoSong, autoMenu, live.Has("M_AUTO_RUN")));
                mgr.ClearAll();
                if (live != mgr) live.ClearAll();   // 场景单例与自检宿主是两份状态，收尾各清一次
                check("清空效果回到基线",
                    Mathf.Abs(PlayRules.Windows.perfectWindow - 0.08f) <= Tol
                    && PlayRules.JudgeLineVisible && PlayRules.NotesVisible,
                    string.Format("perfect={0:0.###}、线可见={1}、音符可见={2}",
                        PlayRules.Windows.perfectWindow, PlayRules.JudgeLineVisible, PlayRules.NotesVisible));
                // 激活过的宿主先销毁：OnDisable 会自己退订（不依赖 gmGo 还在）
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
                if (injectedModifier) PModifierInstance.SetValue(null, null);
                if (injectedGame)
                {
                    PGameInstance.SetValue(null, null);
                    UnityEngine.Object.DestroyImmediate(gmGo);
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add(string.Format("FAIL {0,-22} 自检异常：{1}", "执行", e.Message));
            }
            finally
            {
                PlayRules.SetBaseWindows(JudgeWindows.Default);
                PlayRules.ResetModifiers();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }

            string summary = string.Format(
                "局内 modifier 自检：{0} 项通过 / {1} 项异常（判定：基础值+修改器、倍率乘法叠加、同 id 刷新、"
                + "按时失效、来源与 debuff 标记、净化只吃 debuff、本曲 vs 行程作用域、曲终/回落菜单自动清、清空复位）",
                pass, fail);
            foreach (string line in lines) Debug.Log("[PlayModifier] " + line);
            Debug.Log("[PlayModifier] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/play_modifier_check.txt", lines.ToArray());
                File.AppendAllText("Logs/play_modifier_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayModifier] 报告写入失败：" + e.Message);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>按 id 取运行时条目（自检读字段用）</summary>
        private static ModifierRuntime FindRuntime(ModifierManager mgr, string id)
        {
            var list = mgr.Active;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].def != null && list[i].def.id == id) return list[i];
            }
            return null;
        }
    }
}
#endif
