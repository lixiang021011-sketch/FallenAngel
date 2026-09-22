using System;
using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>一次 cut-in 的内容（纯数据；文案走 Loc key，图标先用 key 首字母占位，等美术资源再换）</summary>
    public struct CutinRequest
    {
        public string iconKey;       // 图标 key（占位：显示首字母）
        public string titleKey;      // 主标题（Loc key）
        public string subtitleKey;   // 副标题（Loc key，可空）
        public float duration;       // 显示时长（秒，含滑入淡出）
        public string sourceId;      // 触发来源（道具 id / "combo" 等，便于排查）

        public static CutinRequest Make(string sourceId, string iconKey, string titleKey,
            string subtitleKey = null, float duration = 1.6f)
        {
            return new CutinRequest
            {
                sourceId = sourceId,
                iconKey = iconKey,
                titleKey = titleKey,
                subtitleKey = subtitleKey,
                duration = duration,
            };
        }
    }

    /// <summary>
    /// 技能/道具 cut-in 服务（前端对照清单 E6/E7）。
    ///
    /// 上游是 `CutinManager`（`Setup(bootData)` / `UpdateSkill(skillData, time, score, isEncore)` /
    /// `UpdateCombo(score)`）+ `CutinView` + `IngameComboCutin`，数据类 `CutinData`/`CutinCardData`。
    /// 我们取它的**两个触发源**，正好对上作者要的"局内道具、buff/debuff"：
    ///
    ///   1. 局内道具/技能：`Play(CutinRequest.Make(itemId, icon, title, desc))` —— 道具应用时调一次；
    ///   2. 连击里程碑：`NotifyCombo(combo)` —— 每 `ComboMilestone` 连击触发一次（对应上游 IngameComboCutin）。
    ///
    /// 队列规则：同时只显示一张，后来的排队；同 sourceId 在显示期间重复请求直接忽略（避免刷屏）。
    /// 时间基准：`Tick(deltaSeconds)` 由 JudgeManager 每帧用未缩放时间推进
    /// （cut-in 属演出，暂停时 Time.deltaTime = 0 自然冻结，与 modifier 的歌曲时间轴分开）。
    /// </summary>
    public static class CutinService
    {
        /// <summary>连击里程碑间隔（上游 IngameComboCutin 也是按连击数触发；数值取 50，属我们的口径）</summary>
        public const int ComboMilestone = 50;

        private static readonly Queue<CutinRequest> pending = new Queue<CutinRequest>();
        private static CutinRequest current;
        private static bool hasCurrent;
        private static float remaining;
        private static float currentDuration;
        private static int lastMilestone;

        /// <summary>一张 cut-in 开始显示（UI 订阅）</summary>
        public static event Action<CutinRequest> OnCutinStarted;

        /// <summary>当前 cut-in 结束（UI 订阅）</summary>
        public static event Action OnCutinEnded;

        /// <summary>队列长度（含正在显示的）——自检与调试用</summary>
        public static int PendingCount { get { return pending.Count + (hasCurrent ? 1 : 0); } }

        public static bool IsPlaying { get { return hasCurrent; } }

        /// <summary>
        /// 当前正在播放的 cut-in（若没有则返回 false）。
        /// 为什么需要：HUD/演出层可能**晚于**请求才被激活（例如局内道具在菜单/结算期发放，
        /// 而 CutinView 在演奏开始才 OnEnable）—— UI 在 OnEnable 里用它"追赶"当前状态，
        /// 否则这条 cut-in 永远不会显示（真机实测踩过：道具在开局前用掉 → 卡片不出现）。
        /// </summary>
        public static bool TryGetCurrent(out CutinRequest request)
        {
            request = current;
            return hasCurrent;
        }

        /// <summary>当前 cut-in 剩余时长（秒；未播放时为 0）</summary>
        public static float CurrentRemaining { get { return hasCurrent ? Mathf.Max(0f, remaining) : 0f; } }

        /// <summary>当前 cut-in 的总时长（秒）——UI 用它算播放进度，避免自己再维护一份时钟</summary>
        public static float CurrentDuration { get { return currentDuration; } }

        /// <summary>当前 cut-in 的播放进度 0..1（由 `Tick` 推进；UI 只读它做动画）</summary>
        public static float CurrentProgress
        {
            get
            {
                if (!hasCurrent || currentDuration <= 0f) return 0f;
                return Mathf.Clamp01(1f - remaining / currentDuration);
            }
        }

        /// <summary>请求一次 cut-in</summary>
        public static void Play(CutinRequest request)
        {
            if (request.duration <= 0f) request.duration = 1.6f;

            if (hasCurrent && !string.IsNullOrEmpty(request.sourceId)
                && request.sourceId == current.sourceId)
            {
                return;                       // 同一来源正在显示 → 忽略
            }

            pending.Enqueue(request);
            if (!hasCurrent) StartNext();
        }

        /// <summary>连击里程碑触发（JudgeManager 每次涨连击时调用）</summary>
        public static void NotifyCombo(int combo)
        {
            if (combo <= 0 || combo < lastMilestone + ComboMilestone) return;
            lastMilestone = (combo / ComboMilestone) * ComboMilestone;
            Play(CutinRequest.Make("combo", "cutin.combo.icon", "cutin.combo.title",
                "cutin.combo.desc", 1.4f));
        }

        /// <summary>按未缩放帧时间推进（暂停时不流逝）</summary>
        public static void Tick(float deltaSeconds)
        {
            if (!hasCurrent) return;
            remaining -= deltaSeconds;
            if (remaining > 0f) return;

            hasCurrent = false;
            if (OnCutinEnded != null) OnCutinEnded();
            if (pending.Count > 0) StartNext();
        }

        /// <summary>开局/重开清空（连击里程碑计数也复位）</summary>
        public static void Reset()
        {
            pending.Clear();
            hasCurrent = false;
            remaining = 0f;
            lastMilestone = 0;
            if (OnCutinEnded != null) OnCutinEnded();
        }

        private static void StartNext()
        {
            if (pending.Count == 0) return;
            current = pending.Dequeue();
            hasCurrent = true;
            currentDuration = Math.Max(0.2f, current.duration);
            remaining = currentDuration;
            if (OnCutinStarted != null) OnCutinStarted(current);
        }
    }
}
