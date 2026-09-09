using System;
using System.Collections.Generic;
using System.Linq;
using FallenAngel.Data;
using FallenAngel.Audio;
using FallenAngel.Gameplay;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>成长验证入口的场景组件；UI转发操作，核心负责演奏启动和持久化事务。</summary>
    public sealed class PortfolioSession : MonoBehaviour
    {
        [Header("验收开关（不写入存档）")]
        [SerializeField, Tooltip("暂时关闭以便验收；开启后恢复本局的Bad/Miss失败阈值。")]
        private bool enforceFailureLimit = false;
        public bool FailureLimitEnabled => enforceFailureLimit;

        [Header("成长流程原型参数（开局冻结）")]
        [SerializeField, Min(1), Tooltip("Bad/Miss达到此数量时失败；同一长按只计一次。原型初值，可调整。")]
        private int failureLimit = 30;
        [SerializeField, Min(0), Tooltip("正常完成整条免费路线的额外成长积分。原型初值，可调整。")]
        private int completionBonus = 100;

        public event Action OnChanged;
        public bool IsOpen { get; private set; }
        public bool OwnsSong { get; private set; }
        public bool HasConfirmation { get; private set; }
        public void SetConfirmation(bool value) { HasConfirmation = value; }
        public string Error { get; private set; }
        public PortfolioProfileData Profile { get; private set; }
        public PortfolioGrowthRunData Run { get; private set; }
        public int Failures { get; private set; }
        public int LastScore { get; private set; }
        public float LastAccuracy { get; private set; }
        public PortfolioTalentService Talents { get; private set; }
        private PortfolioGrowthService growth;
        private PortfolioIncomeService incomeService;
        private GameManager manager;
        private Action retry;
        private bool failed;
        private int playingIndex;
        private ChartData loadedChart;
        private readonly HashSet<NoteData> failedNotes = new HashSet<NoteData>();
        private readonly HashSet<int> failedHolds = new HashSet<int>();
        private Coroutine autoContinueCoroutine;   // 结算自动继续（游玩中只点路线按钮，无需手动点"继续"）
        private const float AutoContinueDelay = 2f; // 结算信息展示时长（秒，unscaled）

        private void Awake()
        {
            var store = PortfolioProfileStore.CreateDefault();
            Talents = new PortfolioTalentService(store);
            growth = new PortfolioGrowthService(store);
            incomeService = new PortfolioIncomeService();
        }

        private void OnEnable() { Subscribe(); }
        private void Start() { Subscribe(); }
        private void OnDisable() { Unsubscribe(); }
        private void Subscribe()
        {
            Unsubscribe();
            manager = GameManager.Instance;
            if (manager == null) return;
            manager.SetPortfolioSession(this);
            manager.OnGameEnd -= SongEnded;
            manager.OnGameEnd += SongEnded;
            manager.OnStateChanged -= StateChanged;
            manager.OnStateChanged += StateChanged;
        }
        private void Unsubscribe()
        {
            if (manager == null) return;
            manager.OnGameEnd -= SongEnded;
            manager.OnStateChanged -= StateChanged;
            if (manager.Portfolio == this) manager.SetPortfolioSession(null);
        }
        private void StateChanged(GameState state) { OnChanged?.Invoke(); }

        /// <summary>所有命令集中处理异常；失败时保留重试命令，不先展示成功状态。</summary>
        public void Execute(Action action)
        {
            try
            {
                Error = null;
                retry = null;
                action();
            }
            catch (Exception e)
            {
                Error = e.Message;
                retry = action;
                Debug.LogError("[PortfolioSession] " + e);
            }
            OnChanged?.Invoke();
        }

        public void RetryOperation() { if (retry != null) Execute(retry); }

        /// <summary>清除瞬时错误状态（不离开当前视图）；供面板内提示流复用（如商店资金不足提示）。</summary>
        public void ClearError()
        {
            if (IsPerforming()) return;
            Error = null;
            retry = null;
            OnChanged?.Invoke();
        }
        public void DismissError()
        {
            if (IsPerforming()) return;
            Error = null; retry = null; Profiles();
        }
#if UNITY_EDITOR
        /// <summary>编辑器流程检查专用；不创建或读取正式玩家存档。</summary>
        public void ConfigureForValidation(string directory)
        {
            if (Profile != null || OwnsSong) throw new InvalidOperationException("Validation store must be set before opening a profile.");
            var store = new PortfolioProfileStore(directory);
            Talents = new PortfolioTalentService(store);
            growth = new PortfolioGrowthService(store);
            // 自动检查显式启用失败分支，不改变玩家验收入口的默认关闭状态。
            enforceFailureLimit = true;
        }
#endif
        public void Open() { IsOpen = true; OnChanged?.Invoke(); }

        /// <summary>新游戏：重置固定默认槽（覆盖）→ 选中该档 → 开新局 → 打开地图视图。二次确认由UI层做。</summary>
        public void StartNewGame()
        {
            if (IsPerforming()) return;
            Execute(() =>
            {
                Talents.ResetDefaultProfile(Loc.T("portfolio.defaultProfileName"));
                SelectProfile(PortfolioDefaults.DefaultProfileId);
                Begin();
                IsOpen = true;
                Debug.Log("[PortfolioSession] 新游戏：默认槽已重置并开新局");
            });
        }

        /// <summary>
        /// 默认槽是否有可损失进度。空白档（无档、损坏、或仅停在起点且零积分/无天赋/未开曲）不弹确认。
        /// 仅有 Begin 写入的起点快照不算进度——否则点一次新游戏后永远弹窗。
        /// </summary>
        public bool DefaultProfileHasProgress()
        {
            try
            {
                var p = Talents.ReadProfile(PortfolioDefaults.DefaultProfileId);
                bool has = p.growthPoints > 0
                    || (p.unlockedNodeIds != null && p.unlockedNodeIds.Count > 0)
                    || RunHasValuableProgress(p);
                Debug.Log("[PortfolioSession] 默认槽有进度=" + has
                    + " growth=" + p.growthPoints
                    + " talents=" + (p.unlockedNodeIds == null ? 0 : p.unlockedNodeIds.Count));
                return has;
            }
            catch (Exception)
            {
                Debug.Log("[PortfolioSession] 默认槽不存在或损坏，视为空白档");
                return false;
            }
        }

        /// <summary>局内快照是否已离开「刚开局」：开过曲、拿过装备、离开起点、进过店、或已结算。</summary>
        static bool RunHasValuableProgress(PortfolioProfileData p)
        {
            if (string.IsNullOrEmpty(p.growthRunJson))
                return !string.IsNullOrEmpty(p.activeRunId);
            var run = JsonUtility.FromJson<PortfolioGrowthRunData>(p.growthRunJson);
            if (run == null) return true;
            if (run.completedSongs > 0 || run.earnedPoints > 0 || run.creditedPoints > 0) return true;
            if (run.heldEquipmentIds != null && run.heldEquipmentIds.Count > 0) return true;
            if (run.visitedNodeIds != null && run.visitedNodeIds.Count > 1) return true;
            if (run.runCash > 0 || run.lastCashReward > 0) return true;
            if (run.shopCandidates != null && run.shopCandidates.Count > 0) return true;
            if (run.optionalPurchaseDiscountUsed > 0 || run.optionalRouteDiscountUsed > 0) return true;
            if (run.phase == "PLAYING" || run.phase == "RESULT" || run.phase == "FINISHED") return true;
            return false;
        }
        public void SelectProfile(string id)
        {
            if (IsPerforming()) return;
            Profile = Talents.ReadProfile(id);
            Run = growth.ReadRun(Profile);
            OwnsSong = false;
            OnChanged?.Invoke();
        }
        public void CreateProfile(string name) { SelectProfile(Talents.CreateProfile(name).profileId); }

        /// <summary>是否处于正式存档选择面板上下文（供外部查询；面板显隐由 SaveSelectPanelController 管理）</summary>
        public bool SaveSelectOpen { get; private set; }

        /// <summary>打开正式存档选择面板：清空当前选中（含 BackToMenu），标记存档选择上下文。</summary>
        public void EnterSaveSelect()
        {
            if (IsPerforming()) return;
            Profiles();
            SaveSelectOpen = true;
            IsOpen = false;
            OnChanged?.Invoke();
        }

        /// <summary>关闭存档选择上下文（面板本体由 UI 控制器关闭）。</summary>
        public void CloseSaveSelect()
        {
            SaveSelectOpen = false;
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 存档选择界面的"进入游戏"：按局 phase 分发——
        /// 无局/FINISHED 开新局；RESULT 继续；READY 直接开曲；MAP/ROOM 进地图视图。
        /// </summary>
        public void EnterGame()
        {
            if (Profile == null || IsPerforming()) return;
            Execute(() =>
            {
                // PLAYING 表示上次演奏中强退：只在真正进入游戏时按失败结算，浏览存档不改档。
                if (Run != null && Run.phase == "PLAYING")
                {
                    growth.Recover(Profile.profileId);
                    Refresh();
                    Debug.Log("[PortfolioSession] 进入游戏：PLAYING 局按中断结算");
                }
                if (Run == null || Run.phase == "FINISHED")
                {
                    Begin();
                    IsOpen = true;
                }
                else if (Run.phase == "RESULT")
                {
                    Continue();
                    IsOpen = true;
                }
                else if (Run.phase == "READY")
                {
                    // 战斗房待开曲：开谱的同时必须关掉存档页并打开地图上下文。
                    // 此前此处 return，SaveSelectOpen 仍为 true、IsOpen 仍为 false——
                    // 曲终 Continue 回菜单时存档页会再弹出来，行程地图被藏住。
                    StartSong();
                    IsOpen = true;
                }
                else
                {
                    IsOpen = true; // MAP / ROOM / PLAYING(理论值)：进地图视图
                }
                SaveSelectOpen = false;
                Debug.Log("[PortfolioSession] 进入游戏：phase=" + Run.phase);
            });
        }
        public void Profiles()
        {
            if (IsPerforming()) return;
            Profile = null; Run = null; OwnsSong = false;
            manager?.BackToMenu();
            OnChanged?.Invoke();
        }
        public void Close()
        {
            if (IsPerforming()) return;
            CancelAutoContinue();
            IsOpen = false;
            Profiles();
        }
        private bool IsPerforming() => OwnsSong && manager != null &&
            (manager.CurrentState == GameState.Playing || manager.CurrentState == GameState.Paused || manager.CurrentState == GameState.Loading);
        private void Refresh()
        {
            Profile = Talents.ReadProfile(Profile.profileId);
            Run = growth.ReadRun(Profile);
            OnChanged?.Invoke();
        }
        public void Begin()
        {
            if (Profile == null || IsPerforming()) return;
            growth.Begin(Profile.profileId, failureLimit, completionBonus, true);
            Refresh();
        }
        public void Unlock(string nodeId, int confirmedRevision)
        {
            if (Profile == null || IsPerforming()) return;
            var result = Talents.Unlock(Profile.profileId, nodeId, confirmedRevision);
            Refresh();
            if (result != TalentUnlockStatus.Available) throw new InvalidOperationException(Loc.T("portfolio.status." + result));
        }

        public void StartSong()
        {
            if (Run == null || Run.phase != "READY" || manager == null) return;
            var stage = PortfolioConfig.Stages.Single(s => s.StageId == Run.stageIds[Run.completedSongs] && s.Enabled);
            var binding = PortfolioConfig.ChartBindings.Single(b => b.ChartId == stage.ChartId);
            var chart = ChartLoader.LoadFromResources(binding.ResourceName);
            if (chart == null || Resources.Load<AudioClip>("Audio/" + chart.metadata.audioFileName) == null)
                throw new InvalidOperationException(Loc.T("portfolio.missingChart"));
            OwnsSong = true;
            playingIndex = Run.completedSongs;
            loadedChart = chart;
            Failures = 0; failed = false; failedNotes.Clear(); failedHolds.Clear();
            AudioManager.Instance?.StopAll();
            AudioManager.Instance?.LoadBGM(chart);
            manager.LoadChart(chart);
            manager.StartCountdownAndPlay();
            OnChanged?.Invoke();
        }

        /// <summary>由GameManager在GO之前调用，保存失败不开始歌曲。</summary>
        public bool BeforeSongStart()
        {
            if (!OwnsSong) return true;
            Execute(() => { growth.MarkPlaying(Profile.profileId, Run.runId); Refresh(); });
            if (Error != null)
            {
                // 尚未开曲，重试从加载/倒计时重新开始，不把保存失败当作演出失败。
                retry = StartSong;
                return false;
            }
            return true;
        }

        /// <summary>原始判定从Core中转；只记录Bad/Miss，复用谱面音符实例去重。</summary>
        public void ReportJudge(NoteData note, JudgeResultType result)
        {
            if (!OwnsSong || manager == null || manager.CurrentState != GameState.Playing || Run == null || Run.phase != "PLAYING" || failed || note == null
                || (result != JudgeResultType.Bad && result != JudgeResultType.Miss)) return;
            bool isHold = note.type == NoteType.LongStart || note.type == NoteType.LongEnd || note.type == NoteType.Slide;
            bool first = isHold && note.longNoteId >= 0 ? failedHolds.Add(note.longNoteId) : failedNotes.Add(note);
            if (!first) return;
            Failures++;
            failed = FailureLimitEnabled && Failures >= Run.failureLimit;
            OnChanged?.Invoke();
        }
        private void LateUpdate()
        {
            // 等判定栈返回后结束，避免在NoteSpawner迭代中清空音符集合。
            if (OwnsSong && failed && manager != null && manager.CurrentState == GameState.Playing) manager.EndGame();
        }
        private void SongEnded()
        {
            if (!OwnsSong || loadedChart != manager.CurrentChart) return;
            LastScore = JudgeManager.Instance?.Score ?? 0;
            LastAccuracy = JudgeManager.Instance?.CalculateAccuracy() ?? 0;
            double perfectRate = 0;
            int missCount = 0;
            var judge = JudgeManager.Instance;
            if (judge != null)
            {
                int total = judge.PerfectCount + judge.GreatCount + judge.GoodCount + judge.BadCount + judge.MissCount;
                perfectRate = total > 0 ? (double)judge.PerfectCount / total : 0;
                missCount = judge.MissCount;
            }
            Execute(() =>
            {
                CompleteCurrentSong(!failed, perfectRate, missCount, LastScore, LastAccuracy);
                Refresh();
            });
            // 成功后自动回到地图（展示结算信息 2 秒 → 自动继续 → 路线延展动画）。
            // 失败/整局结束（FINISHED）不自动。
            StartAutoContinueIfResult();
        }

        /// <summary>统一结算入口：收益引擎计算明细后提交（真实演奏与跳过战斗共用同一条链路）</summary>
        private void CompleteCurrentSong(bool success, double perfectRate, int missCount, int score, float accuracy)
        {
            IncomeSettlement income = null;
            if (success && Run != null && Run.useMap)
            {
                var stage = PortfolioConfig.Stages.Single(s => s.StageId == Run.stageIds[Run.completedSongs] && s.Enabled);
                income = incomeService.Compute(
                    Talents.GetRegisteredEffects(Profile.profileId),
                    HeldEquipmentEffects(),
                    stage, perfectRate, missCount, Run.openingCash);
                Debug.Log($"[PortfolioSession] 收益结算：基础 {income.BaseIncome} + 额外 {income.PerformanceTotal - income.BaseIncome:0.#} + 经济 {income.EconomyTotal:0.#} = {income.Total:0.#}");
            }
            growth.CompleteSong(Profile.profileId, Run.runId, Run.completedSongs, success, score, accuracy, income);
        }

        /// <summary>已持有装备对应的启用效果行（收益引擎输入）</summary>
        private IReadOnlyList<EquipmentEffectsRow> HeldEquipmentEffects()
        {
            var list = new List<EquipmentEffectsRow>();
            if (Run == null) return list;
            foreach (string id in Run.heldEquipmentIds)
            {
                var eq = PortfolioConfig.EquipmentBase.SingleOrDefault(e => e.EquipmentId == id);
                if (eq == null) continue;
                var eff = PortfolioConfig.EquipmentEffects.SingleOrDefault(e => e.EffectId == eq.EffectId);
                if (eff != null && eff.Enabled) list.Add(eff);
            }
            return list;
        }

        /// <summary>结算完成后启动自动继续协程（成功进 RESULT 才触发；失败/FINISHED 不触发）</summary>
        private void StartAutoContinueIfResult()
        {
            if (Error == null && Run != null && Run.phase == "RESULT")
            {
                if (autoContinueCoroutine != null) StopCoroutine(autoContinueCoroutine);
                autoContinueCoroutine = StartCoroutine(AutoContinueAfterDelay());
                Debug.Log("[PortfolioSession] 演奏完成，2 秒后自动回到地图");
            }
        }

        private System.Collections.IEnumerator AutoContinueAfterDelay()
        {
            yield return new WaitForSecondsRealtime(AutoContinueDelay); // 演出时间用 unscaled（架构约定 §7）
            // 期间玩家可能已手动继续/放弃/回菜单：仅当不处于演奏中才自动。
            // 注意：跳过战斗的测试链路不经过游戏状态机（状态保持 Menu），不能要求必须是 Result。
            bool busy = manager != null && (manager.CurrentState == GameState.Playing
                || manager.CurrentState == GameState.Loading || manager.CurrentState == GameState.Paused);
            if (Run != null && Run.phase == "RESULT" && !busy)
            {
                Debug.Log("[PortfolioSession] 自动继续：回到地图");
                Continue();
            }
            autoContinueCoroutine = null;
        }

        private void CancelAutoContinue()
        {
            if (autoContinueCoroutine != null)
            {
                StopCoroutine(autoContinueCoroutine);
                autoContinueCoroutine = null;
            }
        }

        public void Continue()
        {
            CancelAutoContinue(); // 手动继续优先，取消自动
            growth.Continue(Profile.profileId, Run.runId);
            OwnsSong = false;
            manager.BackToMenu();
            Refresh();
        }
        public void EnterRoom(string nodeId, bool useOptionalDiscount = false)
        {
            if (IsPerforming()) return;
            growth.EnterRoom(Profile.profileId, Run.runId, nodeId, useOptionalDiscount);
            Refresh();
        }
        public void LeaveRoom()
        {
            growth.LeaveRoom(Profile.profileId, Run.runId);
            Refresh();
        }
        public void Abandon()
        {
            CancelAutoContinue();
            growth.Abandon(Profile.profileId, Run.runId);
            manager.CancelCountdown();
            manager.BackToMenu();
            OwnsSong = false;
            Refresh();
        }

        // ---- 装备持有 ----

        /// <summary>本局已持有装备（局终清空；跨重启随局快照保留）</summary>
        public IReadOnlyList<string> HeldEquipmentIds => Run?.heldEquipmentIds;

        /// <summary>持有上限（equipment_base_v3 约定 20 件）</summary>
        public int EquipmentCapacity => PortfolioDefaults.EquipmentCapacity;

        /// <summary>是否可获得该装备（存在+启用+未持有+未满容量）。供商店/掉落流程先行判断，不产生副作用。</summary>
        public bool CanAcquireEquipment(string equipmentId)
        {
            if (Profile == null || Run == null || Run.phase == "FINISHED") return false;
            var item = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == equipmentId && e.Enabled);
            if (item == null || item.AllowDuplicate) return false;
            if (Run.heldEquipmentIds.Contains(equipmentId)) return false;
            return Run.heldEquipmentIds.Count < PortfolioDefaults.EquipmentCapacity;
        }

        /// <summary>获得装备（原子事务；重复/满容量/未知装备时失败，进度不被当作成功）</summary>
        public void AcquireEquipment(string equipmentId)
        {
            if (Profile == null || Run == null || IsPerforming()) return;
            Execute(() =>
            {
                growth.AcquireEquipment(Profile.profileId, Run.runId, equipmentId);
                Refresh();
            });
        }

        // ---- 商店 ----

        /// <summary>当前商店展示的候选装备（进店/刷新时生成；购买即售罄移除）</summary>
        public IReadOnlyList<string> ShopCandidates => Run?.shopCandidates;

        /// <summary>是否正处于商店房间（供 UI 判断显示商店行）</summary>
        public bool InShop => Run != null && Run.useMap && Run.phase == "ROOM"
            && PortfolioConfig.MapNodes.Any(n => n.NodeId == Run.currentNodeId && n.NodeType == "SHOP");

        /// <summary>购买报价。useOptional 为 true 时叠 K1（有剩余次数才生效）。</summary>
        public int QuoteEquipmentPrice(string equipmentId, bool useOptional = false)
        {
            if (Profile == null || Run == null || !InShop) return -1;
            try { return growth.QuotePrice(Profile, Run, equipmentId, useOptional); }
            catch (Exception) { return -1; }
        }

        public bool CanUseOptionalPurchaseDiscount() => OptionalPurchaseRemaining() > 0;

        public int OptionalPurchaseRemaining()
        {
            if (Profile == null || Run == null) return 0;
            var k1 = Talents.GetRegisteredEffects(Profile.profileId)
                .FirstOrDefault(e => e.Handler == "optional_purchase_discount");
            if (k1 == null) return 0;
            int limit = k1.LimitCount ?? 1;
            return Math.Max(0, limit - Run.optionalPurchaseDiscountUsed);
        }

        /// <summary>路费报价。useOptional 为 true 时叠 E08（有剩余次数才生效）。</summary>
        public int QuoteRoutePrice(string nodeId, bool useOptional = false)
        {
            if (Profile == null || Run == null) return -1;
            try { return growth.QuoteRoutePrice(Profile, Run, nodeId, useOptional); }
            catch (Exception) { return -1; }
        }

        /// <summary>表内路费的折后价（含常驻 F0；useOptional 叠 E08）。无局时返回原价。</summary>
        public int QuoteRouteFee(int tablePrice, bool useOptional = false)
        {
            if (Profile == null || Run == null) return tablePrice;
            return growth.QuoteRouteFee(Profile, Run, tablePrice, useOptional);
        }

        public bool CanUseOptionalRouteDiscount() => OptionalRouteRemaining() > 0;

        public int OptionalRouteRemaining()
        {
            if (Run == null) return 0;
            return Math.Max(0, OptionalRouteLimit(Run) - Run.optionalRouteDiscountUsed);
        }

        private static int OptionalRouteLimit(PortfolioGrowthRunData r)
        {
            foreach (string id in r.heldEquipmentIds)
            {
                var eq = PortfolioConfig.EquipmentBase.FirstOrDefault(e => e.EquipmentId == id);
                if (eq == null) continue;
                var eff = PortfolioConfig.EquipmentEffects.FirstOrDefault(e => e.EffectId == eq.EffectId);
                if (eff != null && eff.Enabled && eff.Handler == "optional_route_discount")
                    return eff.LimitCount ?? 2;
            }
            return 0;
        }

        /// <summary>商店购买（原子事务：扣款+获得+售罄；失败时现金与持有不变）</summary>
        public void PurchaseEquipment(string equipmentId, bool useOptional = false)
        {
            if (Profile == null || Run == null || IsPerforming()) return;
            Execute(() =>
            {
                growth.PurchaseEquipment(Profile.profileId, Run.runId, equipmentId, useOptional);
                Refresh();
            });
        }

        /// <summary>本局整批刷新剩余次数（开局由 E0/E1 冻结发放）</summary>
        public int ShopRefreshBudget => Run?.shopRefreshBudget ?? 0;

        /// <summary>整批刷新（原子事务；无变化不扣次数）</summary>
        public void RefreshShop()
        {
            if (Profile == null || Run == null || IsPerforming()) return;
            Execute(() =>
            {
                growth.RefreshShop(Profile.profileId, Run.runId);
                Refresh();
            });
        }

#if UNITY_EDITOR
        /// <summary>调试入口：验证刷新链路（正式发放需解锁 E0/E1 后开局冻结）</summary>
        public void DebugGrantRefreshBudget()
        {
            if (Profile == null || Run == null) return;
            Execute(() =>
            {
                growth.DebugGrantRefreshBudget(Profile.profileId, Run.runId, 2);
                Refresh();
            });
        }
#endif

#if UNITY_EDITOR
        /// <summary>测试用：跳过当前战斗——按成功结算（积分/现金照常），不实际播放。加快验收链路，打包不包含。</summary>
        public void DebugSkipBattle()
        {
            if (Profile == null || Run == null || IsPerforming()) return;
            Execute(() =>
            {
                if (Run.phase == "READY")
                {
                    growth.MarkPlaying(Profile.profileId, Run.runId);
                    Refresh();
                }
                if (Run != null && Run.phase == "PLAYING")
                {
                    // 占位统计：全 Perfect、无 Miss → 达标/增幅全部触发（与真实演奏同一条结算+自动继续链路）
                    CompleteCurrentSong(true, 1f, 0, 1000000, 1f);
                    Refresh();
                    StartAutoContinueIfResult();
                }
                else
                {
                    Debug.LogWarning("[PortfolioSession] 当前不可跳过战斗：phase=" + (Run?.phase ?? "null"));
                }
            });
        }

        /// <summary>调试入口：无商店/掉落 UI 时验证持有链路——逐件获取下一件未持有的装备（E01→E02→…→E10）</summary>
        [ContextMenu("Debug: Acquire Next Equipment")]
        public void DebugAcquireNextEquipment()
        {
            var next = PortfolioConfig.EquipmentBase.FirstOrDefault(e =>
                e.Enabled && !e.AllowDuplicate && (Run == null || !Run.heldEquipmentIds.Contains(e.EquipmentId)));
            if (next == null)
            {
                Debug.LogWarning("[PortfolioSession] 没有更多可获取的装备");
                return;
            }
            AcquireEquipment(next.EquipmentId);
        }
#endif
        public void Resume() { manager?.ResumeGame(); }
    }
}
