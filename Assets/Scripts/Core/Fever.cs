using System;
using FallenAngel.Data;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>Fever 的阶段（对应上游三个事件类的先后关系）</summary>
    public enum FeverPhase
    {
        Idle = 0,
        /// <summary>已收到 fever.begin（预备演出：屏幕开始蓄势）</summary>
        Begin = 1,
        /// <summary>已收到 fever.start（生效中）</summary>
        Active = 2,
    }

    /// <summary>
    /// Fever（前端对照清单 C4）。**触发方式照抄上游：谱面事件驱动**（作者 2026-09-22 定案 Q2）——
    /// 上游 `Sekai/Live/FeverBeginEvent.cs` / `FeverStartEvent.cs` / `FeverEndEvent.cs`
    /// 都是 `EventBase` 谱面事件；我们的 `events` 通道用同名语义的字符串：
    ///
    ///     fever.begin → FeverPhase.Begin（蓄势）
    ///     fever.start → FeverPhase.Active（生效：加分数倍率）
    ///     fever.end   → FeverPhase.Idle  （收尾：撤掉倍率）
    ///
    /// **倍率走 modifier 口子**（作者定案 Q3）：进入 Active 时
    /// `ModifierManager.Apply(new ModifierDef("FEVER_SCORE", ModifierKind.ScoreMultiplier, 1.2f, 0))`，
    /// 退出时 `Remove("FEVER_SCORE")` —— 所以天赋/局内道具能改这一档（同 id 会刷新、乘法叠加）。
    ///
    /// 1.2 这个数是我们定的，不是抄来的：上游的 `ScoreLogic`（分数公式）不在本项目移植范围内
    /// （判定簇清单 G2 已登记），所以"fever 加多少分"取一个保守值并暴露成常量 + modifier。
    ///
    /// `Charge`（0..1）留给局内道具/技能去攒（作者口径：Q2 只要求谱面事件驱动，攒条先不接玩法）。
    /// </summary>
    public static class Fever
    {
        public const string BeginEvent = "fever.begin";
        public const string StartEvent = "fever.start";
        public const string EndEvent = "fever.end";

        /// <summary>生效期间的分数倍率（见类注释：数值是我们的，出口是 modifier）</summary>
        public const float ScoreMultiplier = 1.2f;

        /// <summary>倍率 modifier 的 id（同 id 重复施加 = 刷新，不会叠成一堆）</summary>
        public const string ScoreModifierId = "FEVER_SCORE";

        private static FeverPhase phase = FeverPhase.Idle;
        private static float charge;

        /// <summary>fever 阶段变化（UI 订阅；参数 = 新阶段）</summary>
        public static event Action<FeverPhase> OnPhaseChanged;

        /// <summary>蓄力值 0..1：留给道具/技能攒；谱面事件直接触发时不经过它</summary>
        public static event Action<float> OnChargeChanged;

        public static FeverPhase Phase { get { return phase; } }

        public static bool IsActive { get { return phase == FeverPhase.Active; } }

        public static float Charge { get { return charge; } }

        /// <summary>把 `fever.*` 事件接到事件派发器上（JudgeManager 装谱时调一次）</summary>
        public static void Register(LiveEventDriver driver)
        {
            if (driver == null) return;
            driver.Register(BeginEvent, e => EnterBegin());
            driver.Register(StartEvent, e => EnterActive());
            driver.Register(EndEvent, e => Exit());
        }

        /// <summary>开局/重开：回到 Idle 并撤掉倍率</summary>
        public static void Reset()
        {
            phase = FeverPhase.Idle;
            SetCharge(0f);
            RemoveScoreModifier();
            Raise();
        }

        public static void EnterBegin()
        {
            if (phase == FeverPhase.Active) return;      // 已经在 fever 里，begin 幂等
            phase = FeverPhase.Begin;
            Raise();
        }

        public static void EnterActive()
        {
            if (phase == FeverPhase.Active) return;      // 重复 start 幂等（不重复施加）
            phase = FeverPhase.Active;
            ApplyScoreModifier();
            Raise();
        }

        public static void Exit()
        {
            if (phase == FeverPhase.Idle) return;
            phase = FeverPhase.Idle;
            RemoveScoreModifier();
            SetCharge(0f);
            Raise();
        }

        /// <summary>给道具/技能用的蓄力入口（不影响谱面事件触发的路径）</summary>
        public static void AddCharge(float delta)
        {
            SetCharge(Mathf.Clamp01(charge + delta));
        }

        private static void ApplyScoreModifier()
        {
            ModifierManager mgr = ModifierManager.Instance;
            if (mgr == null) return;                     // 自检/无管理器时静默（倍率由 PlayRules 兜底）
            mgr.Apply(new ModifierDef(ScoreModifierId, ModifierKind.ScoreMultiplier, ScoreMultiplier, 0f,
                "modifier.fever.name", "modifier.fever.desc",
                scope: ModifierScope.Song, source: ModifierSource.System));
        }

        private static void RemoveScoreModifier()
        {
            ModifierManager mgr = ModifierManager.Instance;
            if (mgr != null) mgr.Remove(ScoreModifierId);
        }

        private static void SetCharge(float value)
        {
            if (Mathf.Abs(charge - value) < 1e-4f) return;
            charge = value;
            if (OnChargeChanged != null) OnChargeChanged(charge);
        }

        private static void Raise()
        {
            if (OnPhaseChanged != null) OnPhaseChanged(phase);
            Debug.Log(string.Format("[Fever] 阶段 → {0}（分数倍率 {1:0.0}x）", phase, PlayRules.ScoreMultiplier));
        }
    }
}
