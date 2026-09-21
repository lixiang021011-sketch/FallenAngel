# 判定簇移植 · OpenSekai 逐条对照清单

> 用途：**一行一条**核对"我们是否严格照 OpenSekai 做"。核对时在 `[ ]` 里打勾；
> "一致性"三档：**逐字**（只改命名空间/类型名）、**适配**（语义相同，接线不同，附理由）、**缺口**（未移植）。
> 上游引用 = `_refs/port_dump/`（本地克隆 `trae_projects\_refs\OpenSekai`，不进仓库）里的文件名 + 方法名/行号。
> 我们的落地 = `Assets/Scripts/Core/Live/`（判定簇）与 `Gameplay/`（接线与视图）。

## A. 数据与窗口表

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] A1 | `LiveConfig.LoadMasterConfig`：Normal 2.5/2.5、5/5、6.5/6.5、7.5/7.5（帧） | `LiveConfig.LoadMasterConfig` | 逐字 | `LiveJudgeChecks①`、`JudgeWindowChecks` |
| [ ] A2 | 同上：Flick 2.5/2.5、6.5/7.5、7/8、7.5/8.5（**前后不对称**） | 同 | 逐字 | `LiveJudgeChecks①b`（早115ms=Good / 晚115ms=Great） |
| [ ] A3 | 同上：Long_End **3.5/4**、6.5/8、7.5/8.5 | 同 | 逐字 | `JudgeWindowChecks⑥`（150ms：基础 Miss、1.5×→Great） |
| [ ] A4 | 同上：Normal_Critical 3.3/3.3、4.5/4.5；Long_Critical 同；Long_End_Critical 同 Long_End | 同 | 逐字 | 代码核对（谱面暂无 critical 来源） |
| [ ] A5 | 同上：Friction* 行 `CopyJudgeFrame` 自 Flick/Long（原作的 TODO） | 同 | 逐字 | 代码核对 |
| [ ] A6 | `CalcFrameToTime(frame)=frame/60` | 同 | 逐字 | `LiveJudgeChecks①`（2.5 帧=41.67ms） |
| [ ] A7 | `UpdateJudgeTimeBeforeAndAfter` 维护全表最大前后值（7.5/8.5 帧） | 同 | 逐字 | `LiveJudgeChecks①b`（前 0.1250 / 后 0.1417s） |
| [ ] A8 | `JudgeTimeData` 字段与构造 | `JudgeTimeData`（去掉 MasterIngameJudgeFrame 构造器） | 适配（原作的 master data 入口本工程没有） | 代码核对 |
| [ ] A9 | `JudgeTime` = 0.125f；`JustPerfectJudgeTimeHalf` = 1/120 | 同 | 逐字 | `LiveJudgeChecks①`（40ms=Perfect、8.3ms 内=JustPerfect） |
| [ ] A10 | `GetJudgeResultInternal`：JustPerfect → Perfect → Great → Good → Bad → Miss；`isDirection=false` 且落在 Perfect 内 → **Great + FlickMiss** | 同 | 逐字 | `LiveJudgeChecks⑧h`（方向错=Great/FlickMiss） |
| [ ] A11 | `GetNoteResultDescription`：±1/120 内 Just，否则 Fast/Late | 同 | 逐字 | 代码核对 + HUD 早/晚指示 |
| — | — | `LiveConfig.SetJudgeTimeScale`（局内 modifier 倍率，查表时套用） | **本工程扩展** | `JudgeWindowChecks⑤`、`JudgeTimingChecks⑧` |

## B. 音符状态机（NoteBase 一族）

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] B1 | `NoteBase.Excute`：Done 早退 → `State>=Last` → Miss+Done → `OffsetJudgeTime` → `CalcProgress`<0 早退 → Wait→Playing → 过窗→Last | `LiveNoteBase.Excute` | 逐字 | `LiveJudgeChecks⑧f`（全程不按=头尾双 Miss） |
| [ ] B2 | `NoteBase.CalcProgress` = (now + offsetTime − noteTime)/offsetTime；offsetTime=0 → 1 | 同 | 逐字 | `JudgeTimingChecks`（长按端到端） |
| [ ] B3 | `NoteBase.State` setter：Playing→OnSpawnNote、Done→OnUnSpawnNote | 同（回调面 = 视图/计分层入口） | 逐字 | `JudgeTimingChecks⑩`（视图接线） |
| [ ] B4 | 回调面：OnUpdateScore/OnUpdateCombo/OnDamage/OnJudgment/OnSpawn/OnUnspawn/OnUpdateResult | 同 | 逐字 | 代码核对（`JudgeManager.BindLiveNote`） |
| [ ] B5 | `NormalNote`：只认 **Began**；`SetParentNote` 挂到长按后 Category=Long 且只认 Ended/Canceled；Done 时 `parentNote?.ForceTerminate()` | `LiveNormalNote` | 逐字 | `LiveJudgeChecks③⑧d` |
| [ ] B6 | `ConnectionNote`：仅 `State==Last` 且跨度内可判，碰即 JustPerfect | `LiveConnectionNote` | 逐字 | 代码核对（本工程谱面暂不生成连接点） |
| [ ] B7 | `FrictionNote`：**不要求 Began**；标记 isTouched；Progress>=1 才 JustPerfect | `LiveFrictionNote` | **适配**：单点摩擦（drag）**触到即结算**（本工程 v2 协议"碰即 Perfect，永不 MISS"；照抄会让反馈晚一个 Bad 窗口） | `JudgeTimingChecks⑫`、真机 `Perfect 类别=Friction` |
| [ ] B8 | `FlickNote`：单帧位移 > flickDistance 且方向点积达标；方向错仍给 FlickMiss | `LiveFlickNote` | **适配**：① 幅度取"单帧**或累计**"（慢甩也算，照抄会"时灵时不灵"）② `NoteDirection` 增 Up/Down（我们的谱面协议）③ 键鼠无方向向量时不校验方向 | `JudgeTimingChecks⑬⑭` |
| [ ] B9 | `LongHoldCombo`：子音符活着 → JustPerfect，否则 Last→Miss | `LiveLongHoldCombo` | **适配**：判据改成"**长条是否还被按住**"（上游看子音符状态；照抄按住时连击点全 Miss） | `JudgeTimingChecks⑱`（按住 1.7s 连击涨 3+） |
| [ ] B10 | `FrictionLongNote` / `FrictionHideNote` / `FrictionHideLongNote` / `FrictionFlickNote` / `HiddenConnectionNote` / `GuideNote` / `GuideEndNote` / `GuideHiddenConnectionNote` | — | **缺口**（我们谱面没有 friction 链/隐藏/引导轨） | 见 G 节 |

## C. 判定流水线（LiveLogic）

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] C1 | `Judgment()` 顺序：Allocate → Overlap → LeaveNotes → PickUp → Evaluate（`:643-651`） | `LiveJudgeCore.Judgment` | 逐字 | `LiveJudgeChecks⑤⑥` |
| [ ] C2 | `AllocateInputJudgableEachNotes`（`:653`）：每个输入倒序遍历待判音符；`NoteList.Count==1` 走单点分支、否则走长按分支；首音符再走 TryAddConnectionCandidate | 同 | 逐字（含 `AddCheckSingle/Long...` 两条分支与摩擦特判） | `LiveJudgeChecks①②③④` |
| [ ] C3 | `ResolveOverlappingCandidatesAcrossInputs`（`:708`）：同一音符被多指占为候选 → 比 `LaneDistance`，远者移除 | 同 | 逐字 | `LiveJudgeChecks⑤`（两指夹一音只判一次） |
| [ ] C4 | `LeaveNotesFastGeneration`（`:757`）：同指多候选 → 同刻比距离、否则留更早/更优 | 同 | 逐字 | `JudgeTimingChecks①`（相邻音符分开按） |
| [ ] C5 | `PickUpNotBeginInputNotes`（`:808`）：已按住的手指拾取随后进窗的音符 | 同 | 逐字 | `LiveJudgeChecks④` |
| [ ] C6 | `EvaluateJudgment`（`:826`）：逐个 `Judgment`；结果 None/Bad 时写 `InputedNormalNoteTouch`；从待判表移除；Ended/Canceled 清记账 | 同（另抛 `OnJudged` 供宿主/自检观察） | 适配备注：上游靠视图回调，我们多一个只读事件 | `JudgeTimingChecks` 全组 |
| [ ] C7 | 每指 **1/6 秒**（`:922`）：同一 touchId 内 1/6s 内不再判，除非 `CalcNoteResult > Great` | 同 | 逐字 | `LiveJudgeChecks⑥`、`JudgeTimingChecks` |
| [ ] C8 | 输入对象池 `InputTmp[128]` 复用 | `LiveJudgeCore` 输入池（128，容量一致） | 逐字（第一版每帧 new，已修） | 代码核对 + 真机 120fps |
| [ ] C9 | `CalcTimeOffset(note)`（`:1144`）：按 bar 进度算速度比 → 在屏时长 | `LiveScoreRuntime.CalcTimeOffset`（按 speedRatio 折算；本工程谱面已积分成秒、无 bar） | 适配（谱面模型不同，语义=在屏时长） | `JudgeTimingChecks` 全组 |

## D. 长按容器（LongNote）

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] D1 | `State` setter：Playing/Last→InputBegan 触发 4 个回调；Done 记 `WasHoldingWhenTerminated` 并 `OnUnSpawnNote` | `LiveLongNote.State` | 逐字 | `LiveJudgeChecks⑧a` |
| [ ] D2 | `Excute`：`State==Last` → Miss + Release + 伤害/连击/判定回调；Progress；**子音符链**推进；ViewNoteList 取 current/next；`CalcExcuteNoteLane`；`elapsed > JudgeTime` → Last；`lastInputFrame + MissMesh` → Release | 同（另加"按住过尾"分支，见 D9） | 逐字 | `LiveJudgeChecks⑧b⑧c` |
| [ ] D3 | `Judgment`：头判写 `CalculateLongStartNoteResult` 并进 InputBegan；否则 Release→InputBegan / Input；刷 `lastInputFrame`；`ConnectionNoteJudgment`；尾部 `IsLongEndJudgeTime && IsJudgment && Judgment` → Done | 同 | 逐字 | `LiveJudgeChecks⑧d`、`JudgeTimingChecks⑥` |
| [ ] D4 | `IsJudgment`：左/右边界按 `JudgeInfo.result==None` 取 `JudgeLane*` 或 `JudgedLane*`；时机用 `OffsetJudgeTime`；Playing/Last 时只认 Began | 同 | 逐字 | `LiveJudgeChecks⑧a` |
| [ ] D5 | `ForceTerminate`：子音符活着/已判过 → JustPerfect，否则 Miss | 同 | 逐字 | `LiveJudgeChecks⑧e` |
| [ ] D6 | `AddConnectionNote` / `AddHoldCombo` / `AddNoteListAndSetupChildNote` / `SortNoteList` | 同（`AddNoteListAndSetupChildNote` 额外汇 `SetParentNote`，上游在转换器里做） | 适配（避免调用方漏挂） | `LiveJudgeChecks⑩`、`JudgeTimingChecks⑱` |
| [ ] D7 | `AutoJudgment` + `ConnectionNoteAutoJudgment` | 同 | 逐字 | 代码核对（演示/回归用） |
| [ ] D8 | `Time.frameCount`（MissMesh、flick lastInputFrame） | `LiveJudgeClock`（宿主每帧推进；批处理可无头验证） | 适配（引擎帧号 → 可注入钟，语义一致） | `LiveJudgeChecks⑧b`（按住保持=不掉指） |
| [ ] D9 | —— | `CompleteTailWhileHolding`：到尾部时刻仍按住 → 以尾部时刻结算为 JustPerfect | **本工程产品口径**（我们的谱是"按住打到尾"；照抄上游=按住不放反而 Miss） | `JudgeTimingChecks⑮` |
| [ ] D10 | —— | `PathLaneSampler`：滑条判定跨度按谱面 path 采样 | **本工程扩展**（上游用连接点串出弯曲跨度；我们的 path 在谱面里） | `JudgeTimingChecks⑰` |

## E. 触控 → 小数轨道（LiveLogic.GetJudgmentLane）

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] E1 | `GetJudgmentLane`（`:983`）：判定线折线定位；`positions[i]` 之间线性插值出**小数轨道**；越界返回 -1 | `LiveLaneResolver.Resolve` | 逐字（算法），坐标来源不同 ↓ | `LiveJudgeChecks⑨` |
| [ ] E2 | 坐标来自 `LiveConfig.JudgmentPositions`（AB 包/世界坐标折线） | 坐标按 `Core/LaneLayout` 轨道中心现算（4↔5 键、轨道加宽自动跟随） | 适配（本工程没有 AB 包） | `LiveJudgeChecks⑨`、真机 `canvasY=0.843×deviceY−1168.5` |
| [ ] E3 | `JudgmentOffsetX/Y` 来自 `LiveBundleBuildData`；`checkJudgmentY` 在判定线上方时拒绝（余量由该资产给） | X = 半节距；Y 闸门**关掉**（区域闸门交给输入层"整屏"，见 E4） | **适配/产品口径**：照抄 0 余量会让滑条/drag 一越过判定线就失效（实机=不跟手） | `LiveJudgeChecks⑨b`、真机 0 次误拦 |
| [ ] E4 | 触摸只在"下半屏判定区"（原作由 GetJudgmentLane 的 Y 判定隐含） | `InputManager.touchBottomRatio = 1`（整屏，参照 Phigros）；UI 上的触摸由 `IsPointerOverUI` 拦掉 | 适配（产品：竖屏横向窄，位置不罚） | 真机 `[InputManager] 触摸按下（真机）` 日志 |
| [ ] E5 | 横向判定宽度 = 音符自身 `JudgeLaneStart/End`（类别偏移来自 Bundle） | `LaneOffsetFor`：普通/drag/甩键 **铺满整屏**；长按/滑条 1.5 轨 | **产品口径**（"对时间有要求、对位置要求不高"） | 真机 + `LiveJudgeChecks⑩` |
| [ ] E6 | `ConvertLaneBy`：左向甩键用"距判定线的实际位置"重新取轨道 | 同（走注入的 `LaneResolver`） | 逐字 | `LiveJudgeChecks⑧h`（方向相关） |
| [ ] E7 | `LiveTouch`：fingerId/touchId/delta/worldPosition/phase/**musicTime** | `LiveTouch`（+ lane / screenPosition / isDirectionTrusted） | 适配（小数轨道与方向可信度由输入层先算好） | `JudgeTimingChecks⑤`（时间戳参与判定） |
| [ ] E8 | `TouchExecution`（`:490`）：touch.time → `musicTime`；同刻重复相位降级（Began/Stationary→Moved） | `InputManager` 时间戳（`InputClock.ToMusicTime`）+ `JudgeManager.MakeTouch` | 适配（我们走 legacy Input + EnhancedTouch 时间戳，相位降级由"按住帧=Stationary"实现） | `JudgeTimingChecks⑤`、真机输入滞后 8.3ms |
| [ ] E9 | `ShouldProcessTouch` / `UpdateLastTouch`：同一 touchId 时间戳回退则丢弃 | —— | **缺口**（当前输入层按帧状态生成，未做逐 touchId 时间戳单调检查） | 待补（移动端极端情况） |

## F. 运行时时钟（LiveLogic 时间推进 / 音画）

| # | 上游（引用） | 我们 | 一致性 | 验证方式 |
| --- | --- | --- | --- | --- |
| [ ] F1 | `UpdateNote`（`:339`）：noteArray 排序 + `noteStartIndex` 前进 + 高速表分开扫 + `IsJudgeTimingTimeOffsetNote` 提前 break | `LiveScoreRuntime.Update` | 逐字 | `JudgeTimingChecks` 全组 |
| [ ] F2 | `UpdateJudgmentNoteArray`（`:460`）：`HasJudgment || is FrictionHideLongNote` 才进待判表 | 同（`HasJudgment`） | 契合（我们无 hide 类型） | 代码核对 |
| [ ] F3 | `OnAutoInput`（`:253`）→ `AutoJudgment` | `LiveScoreRuntime.AutoPlay` | 逐字 | 代码核对 |
| [ ] F4 | 时间基：`scoreInfoTime` = 音频时间 −（谱面 offset + 玩家设定） | `GameManager.SongTime` = 音频时间 −（`metadata.offset` + `CalibrationSettings` + `AudioLatency` 输出延迟补偿） | 适配 + **额外一档**：输出延迟补偿（上游靠 `fillerSec` 垫音频实现同一目的） | `PlayClockChecks` 8 项、真机 `[AudioLatency]` |
| [ ] F5 | 判定用"输入事件时间戳"而非"读到它的那一帧" | 同（`InputClock.ToMusicTime` 线性外推，差值钳 ±0.25s） | 逐字（思路一致） | `PlayClockChecks④⑤`、真机输入滞后 8.3ms |
| [ ] F6 | `AudioSource.time` 直接当音乐时间 | `AudioClock` 平滑（单调不减 + seek 重同步） | **本工程增强**（原作在 `SoundManager.GetAudioSyncedUnityTimer` 里做同类平滑） | `PlayClockChecks⑥` |

## G. 未移植清单（缺口，需要时再补）

| # | 上游 | 说明 |
| --- | --- | --- |
| [ ] G1 | `MusicScore`（事件轨 / BPM 表 / `musicScoreInfoArray` / `FilterBeforeTimeSec`） | 本工程谱面已把时间积分成秒；谱面**事件**（speed/volume/拍号）尚无运行时 |
| [ ] G2 | `EventBase` / skill / ScoreLogic / `LiveScore`（分数槽、生命） | 属原作的分层，与本工程 roguelike 计分层不重合 |
| [ ] G3 | `LiveViewBase` / `LiveViewExt`（视图指令：Spawn/Unspawn/Judgment/Unpicked/Life…） | 我们用 `Note`/`NoteSpawner` + 回调面替代 |
| [ ] G4 | `LiveBundleBuildData`（各类别 laneOffset / JudgmentOffset / 判定线位置） | 用 `LaneOffsetFor` 常量表 + `LaneLayout` 取代 |
| [ ] G5 | friction 链（`ConnectionNote`+`FrictionLongNote`+`LongHoldCombo` 组合）/ 隐藏轨 / guide 轨 / `FrictionFlickNote` | 需要谱面侧支持才有意义 |
| [ ] G6 | `NativeInput`（EnhancedTouch 封装）、`CameraUtility`、mirror（镜像谱面） | 输入已用 legacy+EnhancedTouch 时间戳；镜像未做 |
| [ ] G7 | `LiveSettingData` 全量设置（皮肤/判定音/透明度/120FPS…） | 我们已有速度、校准、特效开关等子集 |

## H. 本工程自加层（不属于上游，需单独维护）

| # | 内容 | 位置 | 验证 |
| --- | --- | --- | --- |
| [ ] H1 | 适配层：`NoteData → LiveNoteBase`（含 wide=跨度音符、长按头+尾、滑条 path） | `LiveNoteBuilder` | `LiveJudgeChecks⑩` |
| [ ] H2 | 薄壳：输入装配（LiveTouch）、五步调用、结果→计分/HUD、滑条跟手规则 | `Gameplay/JudgeManager` | `JudgeTimingChecks` 全组 |
| [ ] H3 | 纯视图：对象池、位置、命中/漏掉特效、长条身体（含**一屏上限**与变速重建） | `Gameplay/Note`、`NoteSpawner` | `NotePartChecks` 16、`PlayVisualChecks` 17 |
| [ ] H4 | 视觉：无墙（方案 B）、去掉按压反馈、引导线 V2 | `SceneBuilder`、`LanePanelController`、`NoteBandGraphic` | 真机截图 + `NotePartChecks` |
| [ ] H5 | 谱面层工具：体检 / 修正 / 导入器护栏 / 音画静音体检 | `chart_tools/*.py` | 25 项单测 + `audit_charts.py` |
| [ ] H6 | 自检体系：14 组（判定簇 21 / 判定时序 19 / 窗口 8 / 时钟 8 / 部件 16 / 视觉 17 / 透视 7 / 池 11 / modifier 8 / 安全区 4 / 图标 27 / 曲包 4 / 成长 96） | `Assets/Scripts/Core/*Checks.cs` | 每组退出码 + 汇总行双查 |

## 使用建议

1. **逐行打勾**：先 A→F（上游对照），再 G（缺口决策）、H（自加层维护）。
2. 只要有一行"适配"，就顺手看它右边那列"验证方式"——**每条适配都必须有自检或真机读数支撑**，没有的话就是漏洞。
3. 想加新音符类型（critical / friction 链 / guide 轨）：先在 A~D 里找到它的上游行，照抄；再在 G 里划掉对应缺口。
