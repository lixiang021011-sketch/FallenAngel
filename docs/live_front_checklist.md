# 判定簇**前端** · OpenSekai 逐条对照清单

> 作者口径（2026-09-21）："后端可能是可以的，但前端完全不行。要严格对照，**不同的地方只能是音符下落界面**，
> 其余的都要复现。"
>
> 因此本清单的判定只有三档：
> **必须复现**（除音符下落界面之外的一切：判定文字、combo、HUD、特效、倒计时、结算、暂停、加载……）、
> **允许差异**（仅限"音符下落界面"= 轨道/判定线/音符本身的画法，本工程 = 无墙方案 B + V2 双线夹轨）、
> **待决策**（上游假设了本工程没有的系统：HP 生命、技能/fever cut-in）。
>
> 上游引用 = `trae_projects\_refs\OpenSekai\Assets\Scripts\Assembly-CSharp\Sekai\...`（本地克隆，不进仓库）。
> 我们 = `Assets/Scripts/UI/*`、`Assets/Scripts/Gameplay/Note*.cs`、`Core/SceneBuilder.cs`。
> **铁律 8**：每一行"必须复现"的视觉项，落地前先出 `art_tools/` 对照稿（照着上游的尺寸/位置/配色/动画曲线），
> 过稿后才动 `Assets/Scripts` 与 `Assets/Art`。

## A. 音符下落界面（**唯一允许差异**）

| # | 上游 | 我们（现状） | 判定 | 说明 |
| --- | --- | --- | --- | --- |
| [ ] A1 | `LaneView`：轨道底 alpha 取 `LiveSettingData.LaneTransparent` | 方案 B：轨道条 alpha=0（对象保留作特效载体） | 允许差异 | 作者定案"拆墙" |
| [ ] A2 | `BaseNoteView` + 各 `*NoteView`：SpriteRenderer + 顶点色 payload + shader 裁剪 | `Note` + 部件图形（NoteHead/Band/Burst…），透视曲线同源 | 允许差异 | 位置/缩放曲线已与上游一致（`1.06^((min(p,2)-1)*45)` + 尺寸下限） |
| [ ] A3 | `NoteLineView`：长条/滑条 100 段动态网格 | `NoteBandGraphic`（12 段，采样点半宽 = 轨道半宽×透视缩放） | 允许差异（实现不同、效果等价，`NotePartChecks` 断言贴轨道 0 越界） | — |
| [ ] A4 | `NotesViewManager` / `NotePool`：按 (类别×类型) 14 个池 | 单池 + 按谱面预估容量预热（`NotePoolChecks` 11 项） | 允许差异 | — |
| [ ] A5 | 过线后 `LerpUnclamped` 继续飞 + shader 屏幕裁剪 | p=1 定格 + 命中消失 / Miss 原地淡出 | **必须复现**（属"过线行为"，不是下落界面本身） | 见 D4 |

## B. 判定反馈（必须复现）

| # | 上游 | 上游关键数值/行为 | 我们的现状 | 判定 |
| --- | --- | --- | --- | --- |
| [ ] B1 | `JudgmentView`：按结果换 sprite 的判定文字，`t = clamp01(time*20)`，`scale = 1-(1-t)³`，`Hide()` | 出现 0.05s 内三次方缓出、随后淡出 | HUD 单行 TMP 文本 + `JudgeText` 缩放/淡出（`HUDController`） | 必须复现 |
| [ ] B2 | `JudgmentDescriptionView`：Fast / Late（含 Just / FlickMiss）描述文字，同一缓动 | 独立一行、与判定文字同曲线 | `JudgeBiasText` 只显示"早/晚"两字 | 必须复现（要显示 Fast/Late/Just/FlickMiss 四态） |
| [ ] B3 | `ComboView`：7 段数字 renderer；`x=(digitCount-1)*0.475 + i*(-0.95)`；AP 特效 `4.18879 rad/s`；出现 0.1s + burst 0.1s | 数字位对齐、AP(全 Perfect) 专属特效 | `ComboText` TMP 文本 + `ComboLabel` | 必须复现 |
| [ ] B4 | `LifeView`：`rate=life/maxLife` 的槽 + 若干 sprite 状态 + Setup 回调按钮 | 生命 1000，Bad−60 / Miss−80 | **无 HP（本工程不做生命值）** | 待决策（复现=加 HP 玩法；或改为"分数槽/达成率槽"同形制） |
| [ ] B5 | `LiveFrontView`（21.5KB）：演奏页 HUD 总装（分数/曲名/进度/暂停按钮/判定线皮肤等） | 布局与皮肤都在此 | `HUDController` 简化版（分数/连击/判定/进度/曲名） | 必须复现（逐项对照布局） |

## C. 演奏特效（必须复现）

| # | 上游 | 行为 | 我们 | 判定 |
| --- | --- | --- | --- | --- |
| [ ] C1 | `LongTapEffectView`（4.4KB）：每根长按一对 aura + 生成粒子，按住期间持续 | 长按有"持续存在"的视觉主体 | 长条身体 + 连击点（无 aura/粒子） | 必须复现 |
| [ ] C2 | `TapEffectView.FlickSlash()`：甩键的斩击特效 | 方向相关 | 只有音符处的命中环 + 放射条 | 必须复现 |
| [ ] C3 | `LiveViewExt.Unpicked`：空按（没判到音符）的反馈 | 按键有即时反馈 | 我们**去掉了按压反馈**（作者定案） | 待决策（若"空按反馈"也算演奏页元素，需要恢复这一项） |
| [ ] C4 | `ScreenEffectView` / `FeverBegin/Start/End`：屏幕级效果、fever 进出 | — | 无（本工程无 fever 系统） | 待决策 |
| [ ] C5 | `EffectPool`：特效对象池 | — | 各自池（HitRing/Burst/Ripple） | 允许差异（实现层） |

## D. 音符过线 / 判定线（必须复现）

| # | 上游 | 行为 | 我们 | 判定 |
| --- | --- | --- | --- | --- |
| [ ] D1 | `LaneView`：判定线（含判定线皮肤 `MasterIngameJudgeFrame`） | 判定线有皮肤/材质 | 一条白条（`JudgeLine` Image） | 必须复现（含皮肤体系的最小版） |
| [ ] D2 | 判定线闪光：命中时按轨道闪 | — | 已随"取消按压反馈"去掉 | **必须复现**（命中闪光属命中反馈） |
| [ ] D3 | 判定线消失（设置/效果） | `LaneView` 可整体隐藏 | `PlayVisibilityController` 已支持（modifier） | 已具备 |
| [ ] D4 | 过线后继续飞 + 屏幕裁剪（`LerpUnclamped`、`_NoteShowRate`） | 漏掉的音符"飞过去" | p=1 定格、Miss 原地淡出 | 必须复现 |

## E. 流程页（必须复现）

| # | 上游 | 行为 | 我们 | 判定 |
| --- | --- | --- | --- | --- |
| [ ] E1 | `CountdownView`（3.9KB）：3-2-1 + GO 的动画/音效 | 倒计时演出 | `CountdownText` 纯文本 | 必须复现 |
| [ ] E2 | `LiveResultView` + `LiveResultAnimationType`：结算演出（分数滚动、等级、评价） | 逐项入场动画 | `ResultScreen` 静态填充 | 必须复现 |
| [ ] E3 | `LiveOutUIController`（6.6KB）：暂停/退出/继续 的 UI 与流程 | 暂停面板、退出确认 | `PauseController` 简化版 | 必须复现 |
| [ ] E4 | `MusicInfoView`（13.8KB）：曲信息/难度/进度显示 | 演奏页信息区 | `SongTitle` + 进度条 | 必须复现 |
| [ ] E5 | `LoadingView` / `LoadingLiveBackgroundView`：加载页（转圈/背景） | 加载演出 | `DeepSeaLoadingPulse` 等程序化 | 必须复现 |
| [ ] E6 | `CutinView` / `CutinManager`：技能/连击 cut-in | — | 无 | 待决策（无技能系统） |
| [ ] E7 | `SkillView` / `SkillTextView*`：技能图标与文本 | — | 无 | 待决策 |

## F. 我们这边的实现对不上时的处理原则

1. **能照抄就照抄**：尺寸、位置、配色、缓动曲线、时序（出现 0.1s / burst 0.1s / `*20` 这类常量）都取上游数值；
   上游用 SpriteRenderer + 顶点色的地方，我们用 UI 图形等价实现（保持像素级可比）。
2. **只在"音符下落界面"留差异**（A 节），其余一律复现；A5/D1–D4 属于"过线/判定线"，**不在允许差异内**。
3. 每项落地前：`art_tools/` 出对照稿（本工程几何 + 上游布局/数值）→ 作者过稿 → 改 `Assets/Scripts`/`Assets/Art` → 真机截图复核。
4. 每项落地后：加自检（几何/接线/时序，仿 `PlayVisualChecks`）+ 真机截图对照上游截图。

## G. 建议落地顺序（按"玩家一眼能看到"排）

1. **判定文字 + 早/晚描述**（B1/B2）——最显眼、改动最小
2. **combo 数字 + AP 特效**（B3）
3. **判定线皮肤 + 命中闪光 + 过线飞行**（D1/D2/D4）
4. **HUD 总装对照**（B5/E4：分数、曲名、进度、暂停按钮的位置与样式）
5. **演奏特效**（C1 长按 aura / C2 甩键斩击 / C3 空按反馈）
6. **流程页**（E1 倒计时 / E2 结算 / E3 暂停退出 / E5 加载）
7. **待决策两项**（B4 生命槽 / C4+C6+C7 技能与 fever）——需要作者先定"复现成什么"
