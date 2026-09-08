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
        private GameManager manager;
        private Action retry;
        private bool failed;
        private int playingIndex;
        private ChartData loadedChart;
        private readonly HashSet<NoteData> failedNotes = new HashSet<NoteData>();
        private readonly HashSet<int> failedHolds = new HashSet<int>();

        private void Awake()
        {
            var store = PortfolioProfileStore.CreateDefault();
            Talents = new PortfolioTalentService(store);
            growth = new PortfolioGrowthService(store);
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
        public void SelectProfile(string id)
        {
            if (IsPerforming()) return;
            growth.Recover(id);
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
                    StartSong();
                    return;
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
            Execute(() =>
            {
                growth.CompleteSong(Profile.profileId, Run.runId, playingIndex, !failed, LastScore, LastAccuracy);
                Refresh();
            });
        }
        public void Continue()
        {
            growth.Continue(Profile.profileId, Run.runId);
            OwnsSong = false;
            manager.BackToMenu();
            Refresh();
        }
        public void EnterRoom(string nodeId)
        {
            if (IsPerforming()) return;
            growth.EnterRoom(Profile.profileId, Run.runId, nodeId);
            Refresh();
        }
        public void LeaveRoom()
        {
            growth.LeaveRoom(Profile.profileId, Run.runId);
            Refresh();
        }
        public void Abandon()
        {
            growth.Abandon(Profile.profileId, Run.runId);
            manager.CancelCountdown();
            manager.BackToMenu();
            OwnsSong = false;
            Refresh();
        }
        public void Resume() { manager?.ResumeGame(); }
    }
}
