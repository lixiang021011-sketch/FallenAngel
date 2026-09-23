> **English summary** — Implementation spec for the 16 common ("left-hand") gear items of chapter 1,
> written to be handed to any programmer: for each item — what the player sees, the exact rule and
> edge cases, where it plugs into the existing code, how to verify it, what it must not touch, and
> what is still undecided. It also defines the shared table shape (`GearCatalog`), the per-song /
> per-run charge accounting, and the seams into `ModifierManager` / `PlayRules` / the judge core.
> The design baseline stays `docs/roguelike-chapter1-design.md` §4.2; this document only expands it.
> Nothing here is implemented yet.

---

# 通用件（左手随身器材）实现规格

> 状态：**实现前的规格稿**（2026-09-23 起草）。设计基线是 `docs/roguelike-chapter1-design.md` §4.2 与 §4.6，
> 代码规范是 `docs/architecture.md`。本文档只做一件事：把 16 件通用件展开到"任意一个程序员照着能写"的粒度。
> 与设计稿冲突时**以设计稿为准**，并回来改本文。
>
> **进度（2026-09-24）**：第一批 **5 件已实现**——CG01 折叠谱架、CG06 热水袋、CG08 耳塞、CG12 喉糖、
> CG13 创可贴（表：`GearCatalog`；运行时：`CommonGearService`；效果：`ModifierCatalog` 6 行；自检：
> `GearChecks` 20 项全绿）。其余 11 件仍未实现，表里已占位（`Enabled = false`）。
> **CG05 摆针节拍器**要改 HUD 的偏差显示，按铁律 8 得先出效果图过稿，暂时不进代码批次。

---

## 0. 怎么用这份文档

- 一件一节，每节六块固定顺序：**玩家看到的 → 机制 → 落点 → 验收 → 边界 → 依赖与待拍板**。
- 一件可以独立实现、独立验收、独立提交；不要"顺手"改到隔壁那件。
- 开工前先读 §1（总表）与 §2（共同底座）。§2 里定义的表结构、计数机制、接缝与验收模板是 16 件共用的。
- 凡是写「待拍板」的，都是**设计稿没定、需要作者拍一下**的地方；拍完把结论写回本文，再动代码。
- 已实现的件在 §3 各自的开头标了 **状态**（含落点与自检名），照它接着改不会跑偏。

---

## 1. 总表

编号沿用设计稿 §4.2 的行序（CG01 = 设计稿第 1 行）。稀有度分布核对过设计稿：**白 9 / 蓝 5 / 金 2**。

| id | 器材 | 稀有度 | 价格 | 机制一句话 | 底座 | 建议批次 |
| --- | --- | --- | --- | --- | --- | --- |
| CG01 | 折叠谱架 | 白 | 40 | 判定线不会被遮蔽 | 现成（可见性加"锁"） | ① |
| CG02 | 谱架上的铅笔 | 白 | 40 | 开曲预览前 8 小节 + 音符标注 | 钩子（新 UI + 谱面读取） | ⑤ |
| CG03 | 抄歌本 | 蓝 | 80 | 本层剩余房间可见 | 钩子（依赖路线随机化） | ⑥ |
| CG04 | 耳机 | 蓝 | 80 | 自己声部与命中更清楚；被遮蔽时靠听也能打 | 钩子（音频滤波近似） | ⑥ |
| CG05 | 摆针节拍器 | 白 | 40 | 早/晚偏差更醒目 | 现成（只改显示） | ① |
| CG06 | 热水袋 | 白 | 40 | 开场 30 秒判定更准 | **现成**（`JudgeWindowScale` + 30s） | ① |
| CG07 | 电子节拍器 | 白 | 40 | 密集段判定更准 | 钩子（谱面网格 + 分段窗口） | ④ |
| CG08 | 耳塞 | 蓝 | 80 | 判定收紧、Perfect 加分更高 | **现成**（窗口缩放 + 分数倍率） | ① |
| CG09 | 铁盒里的拨片 | 白 | 40 | 前 3 次误触不计 | 钩子（判定闸门 + 计数） | ⑤ |
| CG10 | 一卷透明胶 | 白 | 40 | 让一个负面失效 | 钩子（依赖负面状态系统） | ⑥ |
| CG11 | 备用弦 | 蓝 | 80 | 第一次断连不清零 | 钩子（`BreakCombo` 闸门） | ② |
| CG12 | 喉糖 | 白 | 40 | 每次演奏结束回血 | **口径冲突，待拍板** | ①（拍完再动） |
| CG13 | 创可贴 | 白 | 40 | 受伤减少 | **现成**（`LifeDamageScale`） | ① |
| CG14 | 旧收音机的旋钮 | 金 | 160 | 自己推判定窗口，推完这一曲定住 | 钩子（推子 UI + 锁定） | ⑤ |
| CG15 | 备用的那把 | 金 | 160 | 本曲失败可原地再来一次 | 钩子（失败路径分支） | ③ |
| CG16 | 复读机 | 蓝 | 80 | 单次手滑自动吸附（范围与次数有限） | 钩子（输入时间吸附） | ④ |

**已落（2026-09-24）**：CG01 / CG06 / CG08 / CG12 / CG13。**已拍板待落**：CG09（Bad 或 Miss 前三次不计）、
CG11 / CG15 / CG16（口径已定，等排期）。**等外部能力**：CG02 / CG03 / CG04 / CG07 / CG10。

「现成」= 现有系统直接支持，改的只是表与几行接线；「钩子」= 需要新写一小块能力（每节的"落点"会写清楚）。

---

## 2. 共同的底座（先读这一节）

### 2.1 数据表：新增 `Assets/Scripts/Core/GearCatalog.cs`

写法照 `ModifierCatalog`（内容表）与 `BuildCatalog`（购买表）：**只读行 + 静态表 + `Validate()`**，内容先空着、结构先立住。

```csharp
public sealed class GearItemRow
{
    public string GearId { get; }      // 通用件 CG01…CG16
    public string NameKey { get; }     // Loc key：玩家看到的那句话（例：晃是晃，摊开就能用）
    public string DescKey { get; }     // Loc key：机制说明（给玩家看的白话，不是系统术语）
    public string OriginKeyA { get; }  // 来历三选一：别人给的
    public string OriginKeyB { get; }  //            家里带的
    public string OriginKeyC { get; }  //            想不起来哪来的
    public GearRarity Rarity { get; }
    public int Price { get; }          // 必须等于 ChapterCatalog.PriceOf(Rarity)
    public string FeatureId { get; }   // 功能型：由专门接缝处理（见 2.3）；效果型为 null
    public bool Enabled { get; }
}
```

两条硬规矩：

1. **效果型 / 功能型二选一**，与 `BuildCatalog` 同款分工：效果型只挂 modifier（能自动被 `ModifierManager` 管生命周期），
   功能型（预览、本层可见、失败重试、吸附）没有 modifier 效果，由专门的接缝接管。
2. **一件可以挂多条内容行**（`CG08 耳塞` 要同时改窗口与分数）：**已拍板用关联表**，实现为
   `GearEffectRow(gearId, modifierId)`（`GearCatalog.Effects`），与 `ModifierCatalog` 的行按 id 关联；
   效果型的器材行本身不带 `ModifierId`。校验会抓"指向不存在 / 未启用的内容行"。

`Validate()` 必须抓得住：id 空/重复、价格与稀有度不符、缺任何一个 Loc key、效果型与功能型都不填或都填、
指向不存在的 `ModifierCatalog` 行、稀有度分布不是白 9 / 蓝 5 / 金 2。

### 2.2 计数与作用域：新增 `Assets/Scripts/Core/CommonGearService.cs`

**为什么不能只靠 modifier**：`ModifierDef` 表达的是"现在生效的效果"（种类 / 数值 / 时长 / 作用域 / 正负），
没有"还剩几次"的语义。带次数的件（CG09 前 3 次、CG11 一次、CG15 一次、CG16 5 次）如果把计数塞进 modifier，
以后一改就会穿。所以：**效果归 `ModifierManager`，次数归道具服务**，两边都走 `PlayRules` 读数值。

```csharp
public static class CommonGearService
{
    public static bool Owns(string gearId);                            // 本局是否持有
    public static bool TryConsumeSongCharge(string gearId, int times); // 本曲次数（曲首自动补满）
    public static bool TryConsumeRunCharge(string gearId, int times);  // 本局次数
    public static void BeginSong();                                    // 本曲开门：清本曲计数、挂 Song 常驻件
    public static void EndSong();                                      // 曲终（ModifierManager.EndSong 已管效果）
    public static void EndRun();                                       // 局终/新局/放弃：清空
}
```

- 持有列表复用局内快照 `PortfolioGrowthRunData.heldEquipmentIds`；**id 空间要分开**：通用件 `CG##`，
  职业件另起前缀（别和现有的 `E01…E10` 撞）。是否新开字段、旧 `E01–E10` 怎么退役，见设计稿 §7 与本文 §2.7。
- 计数只活在内存 + 随局快照走；跨重启要不要持久化属于「Run 作用域的跨重启持久化」这个已知缺口。

### 2.3 三条施加时间线（每件都要说清自己走哪条）

| 时间线 | 什么时候 | 谁 | 走这条的件 |
| --- | --- | --- | --- |
| 曲前 | `PortfolioSession.BeforeSongStart()` 之后、倒计时 GO 之前 | `CommonGearService.BeginSong()` 挂 Song 常驻件 | CG01 / CG06 / CG08 / CG13 / CG14 |
| 曲中 | 判定、输入、结算等事件发生时 | 各自的接缝消费次数 | CG09 / CG11 / CG15 / CG16 |
| 房间阶段 | 在地图上、进演奏之前 | 地图与曲前 UI | CG02（预习界面）/ CG03（本层可见） |

曲终清效果由 `ModifierManager.EndSong()` 负责（现成），局终由 `EndRun()` 负责（现成）。

### 2.4 接缝表（想要的能力 → 现有东西 → 允许怎么改）

| 想要的能力 | 现成的东西 | 允许的改法（以及不许动的） |
| --- | --- | --- |
| 判定窗口缩放 | `PlayRules.JudgeWindowScale`；基础窗口 Perfect 0.08 / Great 0.16 / Good 0.18 / Bad 0.20 | 经 `ModifierManager.Apply(def)` 施加。**不许**直接改 `JudgeManager.judgeWindows` 或窗口表 |
| 判定线 / 谱面可见性 | `PlayRules.JudgeLineVisible`、`PlayRules.NotesVisible`、`PlayVisibilityController` | 在 `PlayRules` 加"可见性锁"由它汇总；渲染层不动 |
| 分数倍率 | `PlayRules.ScoreMultiplier` | 同上（Fever 与道具共用这一档） |
| 受伤 / 回血 | `PlayRules.LifeDamageScale`、`PlayRules.LifeRegenPerSecond`；`PlayLife`（上限 1000，Bad 60 / Miss 80，**逐曲 `Reset()`**） | 同上 |
| 连击 | `JudgeManager.BreakCombo()`（Miss / Good / Bad 共用，只把 Combo 归零 + 抛 `OnComboUpdate`） | 在它之前加一道闸门；不改 `MaxCombo` 的既有语义 |
| 空按 | `JudgeManager.HandleUnpicked()` —— **现在什么都不做、不参与计分**（已确认） | 要加惩罚得先拍板（见 CG09） |
| 早 / 晚偏差 | `JudgeManager.OnJudgeBias` → HUD | 只改显示强度 |
| 输入时间戳 | `InputManager` → `LaneInputArgs.musicTime` → `LiveJudgeCore` 候选归属 | 吸附只在这一层；**窗口表一个字不改**；原始时间要留一份 |
| 结算与收益 | `PortfolioSession.CompleteCurrentSong` → `PortfolioIncomeService`（三个桶：演奏直接 / 补偿 / 经济） | 新的收益项必须走 `IncomeBucket`，别绕开 |
| 谱面数据 | `ChartData.notes`、`metadata.bpm`、`ChartLoader` | 只读；音符创建只经 `NoteSpawner` 对象池 |
| 音频 | `AudioManager`：**单条 `bgmSource` + `sfxSource` + `hitSource`，没有分轨** | 只能加滤波 / 音量；不许假设有 stem |

### 2.5 三条不许做的事（照 `docs/architecture.md`）

1. 不许直接改核心系统的序列化字段——一律走 `PlayRules` / `ModifierManager`（§9 的"基础值 + 修改器"）。
2. 时间一律读 `GameManager.SongTime`，不许自建计时器（时间类效果暂停时也该冻结）。
3. 音符只经 `NoteSpawner` 对象池创建与回收，不许 `Instantiate` / `Destroy` 音符。

### 2.6 验收模板（每件都按这四步交，缺一步不算完）

1. **自检**：新增 `Assets/Scripts/Core/GearChecks.cs`（照 `BuildChecks` 的写法：菜单 + `-executeMethod` 两用、报告写 `Logs/`），
   每件至少两项：生效侧与不生效侧。
2. **Play**：写一条可复现路径（按什么键、看哪里、期望什么），交给人跑一遍。
3. **反例**：不带这件时行为与改动前一致（回归）。
4. **日志**：关键状态打 `Debug.Log("[Gear] ...")`，方便玩家侧排查。

### 2.7 一件要先说清楚的事：与现有 `E01–E10` 的关系

现在局内持有的是 `E01–E10`（**全是钱 / 收益向**，`equipment_base` / `equipment_effects` 两张表，还依赖尚未接线的"乐句"）。
设计稿把"只改演奏"的通用件与"经济"分开，并建议经济那部分搬到天赋侧（本文档 §5 待拍板第 1 条）。
在作者拍板前，**不要动 `E01–E10`、商店、掉落、容量**：本文档第 1 批（CG01/05/06/08/13）只依赖"持有列表 + 曲前挂效果"，
可以做到不碰商店也能验证（调试面板施加，或 `PortfolioSession.AcquireEquipment` 走一次）。

---

## 3. 逐件规格

### CG01 折叠谱架 · 白 · 40

- **状态**：**已实现（2026-09-24）**——新增 modifier 种类 `JudgeLineLock`（聚合时把可见性拉回可见），内容行 `MC_LINE_LOCK`（Run 常驻）。自检：「折叠谱架：判定线不消失」+「不持有时照常消失（回归）」。
- **玩家看到的**：「晃是晃，摊开就能用。」
- **机制**：判定线在任何情况下都可见——屏蔽 `JudgeLineHidden` 类效果。
- **口径**：只对"判定线可见"这一条做下限；**不**影响"谱面隐身"（那是另一条）。作用域 Run（走哪带哪），演奏中生效。
- **落点**：`PlayRules` 现在直接从 modifier 汇总出 `JudgeLineVisible`。加一个"锁"（例：`PlayRules.LockJudgeLineVisible(bool)`），
  汇总时若锁在，则忽略 `JudgeLineHidden`。`PlayVisibilityController` 不用改（它照旧读 `PlayRules`）。
- **验收**：调试面板施加「判定线消失 3 秒」→ 持有本件时判定线不消失；不持有则消失。自检两条。
- **边界**：只挡渲染；判定逻辑本来就不受影响（`docs/architecture.md` §9.1 已注明）。
- **依赖与待拍板**：无。**待拍板**：要不要同时挡"谱面隐身"（提案：不挡，两件东西分开，别做全能免疫）。

### CG02 谱架上的铅笔 · 白 · 40

- **玩家看到的**：「你在谱上画了记号：哪里该重，哪里要轻。」
- **机制**：开曲前给一次"预习"：显示**前 8 小节**的音符与标注（哪一拍重、哪一段密）。
- **口径**：8 小节按谱面 `metadata.bpm` 换算成秒；预习界面只读谱面，**不进演奏状态、不计时、不扣血**；随时可跳过。
- **落点**：新增 `UI/SongPreviewController`（曲前一层 UI）+ `Core/ChartPreviewBuilder`（截取前 8 小节的音符与标注）。
  取数走 `ChartData.notes` 与 `metadata.bpm`；入口在 `PortfolioSession.BeforeSongStart()` 之后、GO 之前。
- **验收**：持有 → GO 前出现预习界面，前 8 小节音符与标注与谱面一致、可跳过；不持有 → 无此界面（回归）。
- **边界**：只给前 8 小节，不给全谱；不改判定；「密集」的定义与 CG07 共用同一套网格底子（§7.5）。
- **依赖与待拍板**：**依赖谱面网格**（第一版可先只画音符与拍点，不做"轻重"标注）。

### CG03 抄歌本 · 蓝 · 80

- **玩家看到的**：「你抄下了这一带会唱的东西。字很丑，但你认得。」
- **机制**：本层剩余的房间可见。
- **口径**：只影响显示；离开本层后效果结束（每层重新给）。**作用域是本层，不是整局。**
- **落点**：地图绘制（`UI/PortfolioPanelController` / `PortfolioMapPathGraphic`）现在整图可见，本件只在
  **路线随机化（roadmap D6）** 之后才有意义。届时接缝：本层未知节点由"问号"变"可见"。
- **验收**：随机化落地后：不持有 → 未探索房间显示为未知；持有 → 本层房间全显示。**随机化没做之前本件无效果**（这要写进玩家文案层，别骗人）。
- **边界**：不改路线本身、不预告未来层。
- **依赖与待拍板**：**待拍板**：在 D6 之前要不要先落地（提案：先不落地，等随机化）。

### CG04 耳机 · 蓝 · 80

- **玩家看到的**：「塞上它，外面就安静了。」
- **机制**：① 自己的声部更清楚；② 命中（判定音效）更清楚；③ 谱面 / 判定线被遮蔽时也能靠听觉打。
- **口径**：③ 现状**天然成立**（判定音效走 `hitSource`，与视觉无关），所以本件真正的收益是 ① + ②。
  ① 在**没有分轨**的当下只能用滤波近似（给"自己声部"的频段加权），这是设计稿 §7.4 已认的短期方案。
- **落点**：`AudioManager` 在 `bgmSource` 上加一条可开关的滤波链（例：`AudioLowPassFilter` / `AudioHighPassFilter` 组合），
  演奏内有效、曲终撤销；同时临时抬高 `hitVolume`（退出演奏复位）。
- **验收**：自检只验"开关时滤波器与音量按预期变化"；听感由人耳在 Play 里判（写清 A/B 步骤）。
- **边界**：不许假设有分轨；不许改 BGM 播放时机与 `SongTime` 口径。
- **依赖与待拍板**：**待拍板**：滤波近似值不值得做（设计稿自己标了"短期用滤波近似，长期再升级真分轨"）；
  建议排到真分轨之后，或先只做 ② 那一半。

### CG05 摆针节拍器 · 白 · 40

- **玩家看到的**：「它比你还稳。」
- **机制**：早 / 晚偏差更醒目。
- **口径**：只改显示——偏差指示更灵敏（例如：更小的阈值就开始显示、显示持续更久、更显眼）。不动判定窗口。
- **落点**：`JudgeManager.OnJudgeBias`（现成）→ `UI/HUDController` 的早/晚指示与 `JudgmentTextView`；
  由 `CommonGearService.Owns("CG05")` 决定强度档。
- **验收**：自检（持有/不持有两种强度档取值正确）；Play（偏差 20ms 左右时也能看见指示）。
- **边界**：不许改判定；不许把"显示"做成"修正"（修正是 `HitErrorTracker` 的校准，免费且人人有，见设计稿 §4.6）。
- **依赖与待拍板**：无。**待拍板**：加强到什么程度（提案：阈值从现档收窄一半、停留时间 ×1.5）。

### CG06 热水袋 · 白 · 40

- **状态**：**已实现（2026-09-24）**——内容行 `MC_HOTBAG`（`JudgeWindowScale` 0.85、duration 30、Song）。自检：「有效窗口真的缩了」「热水袋 30 秒到点恢复」。
- **玩家看到的**：「手热了才弹得动。」
- **机制**：开场 30 秒判定更准。
- **口径**：`ModifierKind.JudgeWindowScale` + `duration = 30` + `ModifierScope.Song`（**按 `SongTime` 倒计时，暂停自动冻结**）。
  这是现有底座最标准的一条用法。
- **落点**：`ModifierCatalog` 加一行（例 `MC_WARM`：`JudgeWindowScale`, magnitude 0.85, duration 30, scope Song, debuff=false，
  带 name/desc 的 Loc key），`GearCatalog` 里 CG06 指向它。
- **验收**：`PlayModifierChecks` 加一项——曲首 `PlayRules.Windows` 是基础窗口 ×0.85，30 秒后回到 1.0；不持有时全程 1.0。
- **边界**：不许直接改窗口表；magnitude 与时长写进表里，不硬编码在代码里。
- **依赖与待拍板**：**待拍板**：magnitude（提案 0.85 = 窗口收窄 15%）。

### CG07 电子节拍器 · 白 · 40

- **玩家看到的**：「三连、十六分，它都会打。」
- **机制**：密集段内判定更准。
- **口径**：需要先有"密集段"的可计算定义。提案：以 **1/16 音符网格**为单位，连续 ≥ 8 个网格有音符 = 密集段，
  段内判定窗口放宽一档（magnitude 1.15），出段即恢复。**这段判定是"按时间变化的窗口"**，与 CG06 的"整段固定倍率"不同。
- **落点**：两件事——① 谱面侧网格与密集段标记（`Core/ChartGrid`，与 CG02 共用）；② `PlayRules` 增加"按歌曲时间查询窗口倍率"的形状
  （现版本只有一个全局 `judgeWindowScale`），由 `ModifierManager` 汇总出一组带区间的修正。
- **验收**：自检——给定网格与区间，区间内窗口是基础 ×1.15、区间外是 ×1.0；Play 里打一段密集段应有体感（写清用哪张谱的哪一段）。
- **边界**：不许把网格计算结果写回谱面文件；不许改窗口表本身。
- **依赖与待拍板**：**依赖谱面网格**（roadmap §7.5）；**待拍板**：密集段阈值与放宽倍数。

### CG08 耳塞 · 蓝 · 80

- **状态**：**已实现（2026-09-24）**——两条内容行 `MC_EARPLUG_TIGHT`（窗口 0.90）与 `MC_EARPLUG_SCORE`（分数 1.10），走关联表。自检：「一件挂多条」「曲前挂上效果」。
- **玩家看到的**：「外面的声音没了，只剩你和拍子。」
- **机制**：判定收紧、Perfect 加分更高。
- **口径**：两条同时生效——`JudgeWindowScale`（收窄，提案 0.90）与 `ScoreMultiplier`（提案 1.10）。
  这里出现"一件挂两条效果"，是 §2.1 要求表结构支持 1..N 的原因。
- **落点**：`ModifierCatalog` 两行（窗口一条、分数一条），CG08 同时指向两条；施加在曲前（时间线①）。
- **验收**：自检——持有时窗口与分数倍率同时按表取值；不持有都为 1.0。
- **边界**：分数倍率走 `PlayRules.ScoreMultiplier`（Fever 同档，倍率相乘叠加），不许在判定层硬编码。
- **依赖与待拍板**：**待拍板**：两个倍数。

### CG09 铁盒里的拨片 · 白 · 40

- **状态**：口径已拍板（2026-09-24），**未实现**（等排期）。
- **玩家看到的**：「盒子里还有一整把。」
- **机制**：前 3 次误触不计。
- **口径（已拍板 2026-09-24）**：**本曲前 3 次 Bad 或 Miss 都不计**——不进 Bad/Miss 计数、不扣血、不断连，
  **也不计入失败次数**（`PortfolioSession.Failures`）。要写明的副作用：这一曲的失败预算实际被放宽 3 次
  （默认线 30 → 有效 33）。空按保持"无代价"（`JudgeManager.HandleUnpicked` 只留给 HUD 涟漪），不参与本件。
  与 CG11 备用弦不打架：拨片吃最前面三次失误，备用弦管"计数已经生效之后的那次断连"。
- **落点**：`JudgeManager.HandleLiveJudgment` 的 Bad 分支之前加一道闸门（消费 `CommonGearService` 的本曲次数）。
  若将来给空按加惩罚，本件再扩展到"空按也算"。
- **验收**：自检——前 3 次 Bad/Miss 不计（血 / 连击 / 计数 / 失败次数四项都要验），第 4 次正常；不持有则第一次就正常。
- **边界**：不许顺手改空按行为（那会改全局手感）；不许影响 `MaxCombo` 既有语义。
- **依赖与待拍板**：口径已定；实现时注意 `PortfolioSession.ReportJudge` 与 `JudgeManager` 的 Bad/Miss 两处都要过这道闸门。

### CG10 一卷透明胶 · 白 · 40

- **玩家看到的**：「什么都能粘。」
- **机制**：让一个负面失效。
- **口径**：提案——拿到时存着，遇到**负面状态**时自动消耗一次把它挡掉（带一行提示，玩家知道用掉了）。
- **落点**：依赖**每层一个负面状态**这套系统（设计稿 §5.3，**未实现**）。接缝：负面状态施加前先问
  `CommonGearService.TryConsumeRunCharge("CG10", 1)`；回收侧复用 `ModifierManager` 的 `debuff` 标记与 `RemoveDebuffs()` 口径。
- **验收**：待负面系统落地后：有本件时第一次负面被挡掉并有提示，第二次起照常；不持有则第一次就吃上。
- **边界**：只挡一次，不许做成"负面免疫"。
- **依赖与待拍板**：**依赖负面系统**（建议与 §5.3 一起做，别单飞）。

### CG11 备用弦 · 蓝 · 80

- **玩家看到的**：「断了一根，你还弹得下去。」
- **机制**：本曲第一次断连不清零。
- **口径**：本曲第一次 `BreakCombo()` 时**保留连击数**（不归零），Miss 计数与扣血照常，本曲一次。
  注意 `BreakCombo()` 由 Miss / Good / Bad 共用，三者都算"断连"。
- **落点**：`JudgeManager.BreakCombo()` 开头加闸门：`if (CommonGearService.TryConsumeSongCharge("CG11", 1)) return;`。
- **验收**：自检——第一次 Miss：Combo 保留、`MissCount += 1`、扣血照常；第二次 Miss：Combo 归零；不持有：第一次就归零。
- **边界**：`MaxCombo` 不因"保留"而增加（它只在连击上涨时刷新）；不许改 Miss 判定与伤害。
- **依赖与待拍板**：无（无待拍板项）。

### CG12 喉糖 · 白 · 40

- **状态**：**已实现（2026-09-24）**——口径拍板为"本曲开场生命更高（+100）"，新增 modifier 种类 `LifeBonus`；`PlayLife.Max = 1000 + PlayRules.LifeBonus`，`Rate` 以有效上限为分母。自检：「喉糖：开曲生命 1100 且满格」+「曲终回到 1000（回归）」。
- **玩家看到的**：「唱完一颗。」
- **机制（设计稿原文）**：每次演奏结束回血。
- **口径（已拍板 2026-09-24）**：设计稿原文"每次演奏结束回血"与现状冲突——`PlayLife` **逐曲重置**
  （`JudgeManager` 建谱时调 `PlayLife.Reset()`），下一曲开场本来就满血，"结束回血"没有承载。
  因此改成 **(a) 本曲开场生命更高：+100**（不改生命结构，也不让生命跨曲保留）。
- **落点**：`ModifierKind.LifeBonus` → `PlayRules.LifeBonus` → `PlayLife.Max`（基础 1000 + 加成）、
  `Rate` 用有效上限；`CommonGearService.BeginSong` 挂上效果后补一次 `PlayLife.Reset()`（此时还没 GO）。
- **验收**：自检（加成 100 / 上限 1100 / 开曲满格 / 曲终回 1000）+ Play 看开曲血条。
- **边界**：不许悄悄改 `PlayLife` 的伤害常量（那是对照上游抄来的，见 `PlayLife` 类注释）。
- **依赖与待拍板**：口径已定；数值 +100 为提案值（作者已同意按提案走）。

### CG13 创可贴 · 白 · 40

- **状态**：**已实现（2026-09-24）**——内容行 `MC_BANDAGE`（`LifeDamageScale` 0.85、Run 作用域）。自检：「Song 清、Run 留」。
- **玩家看到的**：「手破了，贴上继续。」
- **机制**：受伤减少。
- **口径**：`ModifierKind.LifeDamageScale`（magnitude 0.85 = 受伤八五折）。作用域提案 Run（免得每曲重复施加）。
- **落点**：`ModifierCatalog` 一行（现成 kind），曲前施加。
- **验收**：`LifeChecks` / `PlayModifierChecks` 扩展：Bad/Miss 掉血按 0.85 取整，且 1000 上限不变。
- **边界**：走 `PlayRules.LifeDamageScale`；不许直接改 `PlayLife` 的常量。
- **依赖与待拍板**：倍数已定 0.85（2026-09-24 按提案）。

### CG14 旧收音机的旋钮 · 金 · 160

- **玩家看到的**：「拧到哪儿，就停在那儿。」
- **机制**：自己推判定窗口，推完这一曲定住。
- **口径**：演奏中可调（推子），**一次生效后本曲内不可再调**（"定住"）；范围提案 ±10%（0.90–1.10）；
  作用域 Song 常驻（本曲有效，曲终清）。
- **落点**：`PlayRules` 的窗口缩放（现成）+ 新的推子 UI（最省事的位置：暂停面板，或演奏页一角）；
  "锁定"状态记在 `CommonGearService`（本曲一次）。
- **验收**：自检——第一次调整生效、第二次被拒；Play——推到底后窗口确有变化（用早/晚指示或 `Debug.Log` 验证）。
- **边界**：**不许动 `SongTime`，不许改判定窗口表**（设计稿 §4.6 的两条实现要点）。
- **依赖与待拍板**：**待拍板**：可调范围与 UI 位置。

### CG15 备用的那把 · 金 · 160

- **玩家看到的**：「你还有另一把。」
- **机制**：本曲失败可原地再来一次。
- **口径（现状先说清）**：本作现在的"失败"= 累计 Bad/Miss（按音符去重）达到 `Run.failureLimit`（默认 30）
  → `PortfolioSession` 立刻 `EndGame()` → 结算 `success=false` → `outcome = FAILED`。
  本件 = 这条路径上拦一次：**不结算、重开本曲**，本局一次。
- **落点**：`PortfolioSession` 的失败分支（`failures >= failureLimit` 处）+ 重开走曲前流程；次数走 Run 计数。
- **验收**：自检——把 `failureLimit` 调小（例 2）模拟失败：持有时不结算、重开、本曲分数清零重算；
  次数用尽后再失败则照常 `FAILED`。Play 走一遍。
- **边界**：不许改"归零不结算继续打完"的既有口径（`PlayLife` 注释里作者已定案）；重开不能重复计分。
- **依赖与待拍板**：**待拍板**：重开后本曲已得分数 / 收益是否保留（提案：不保留，本曲重算）。

### CG16 复读机 · 蓝 · 80

- **玩家看到的**：「本来是拿来学英语的。」
- **机制**：单次手滑自动吸附到正确的音符（范围与次数有限）。
- **口径**：设计稿 §4.6 的"输入时间吸附"——把输入的音乐时间戳吸附到容差内最近的待判音符：
  **必须设上限**（提案 ±120ms，本曲 5 次）；**原始时间要留一份**（早/晚指示与命中误差统计用原始值，不能污染 `HitErrorTracker`）。
  判定窗口表一个字不改。
- **落点**：输入装配处或 `LiveJudgeCore` 的候选归属；只在这一层做时间吸附，不碰判定窗口。
- **验收**：自检——容差内吸附生效、超容差不动、次数用尽后不再吸附、`HitErrorTracker` 收到的仍是原始时间；
  Play——故意偏一点仍能判上（写清偏差与对应结果）。
- **边界**：吸附必须有上限，不许做成"替我打"（设计稿 §4.6 明写）。
- **依赖与待拍板**：**待拍板**：容差与次数（提案 ±120ms / 5 次）。

---

## 4. 建议实现顺序（照设计稿 §8，按"能不能立刻验证"排）

| 批次 | 内容 | 为什么先做 |
| --- | --- | --- |
| ① 现成 5 件 ✅ | CG06 热水袋、CG08 耳塞、CG13 创可贴、CG01 折叠谱架、CG12 喉糖（**2026-09-24 已落**） | 只动表 + 几行接线；`GearChecks` 20 项全绿 |
| ①b 视觉件 | CG05 摆针节拍器 | 要改 HUD 显示 → 按铁律 8 先出效果图过稿 |
| ② 连击保护 | CG11 备用弦 | 代表"手感的保护"，一处闸门搞定 |
| ③ 失败兜底 | CG15 备用的那把 | 代表"失败的兜底"，动的是失败路径 |
| ④ 节奏本身 | CG07 电子节拍器 + CG16 复读机 | 都摸"判定/输入"的底层，且网格底子后面 CG02 还要用 |
| ⑤ 判定自主权 | CG14 旋钮 + CG09 拨片 | 需要 UI 与判定闸门 |
| ⑥ 内容依赖 | CG02 铅笔、CG03 抄歌本、CG04 耳机、CG10 透明胶 | 分别等谱面网格 / 路线随机化 / 分轨 / 负面系统 |

每批都遵守：**先写这一批的规格回填（若拍板项有结论）→ 表与校验 → 接线 → 自检 → Play → 提交**。

---

## 5. 拍板记录与仍待拍板

**已拍板（2026-09-24）**

1. **CG12 喉糖** → (a) 本曲开场生命更高，+100。（已实现）
2. **CG09 拨片** → 本曲前 3 次 **Bad 或 Miss 都不计**，含失败次数；空按保持无代价。
3. **一件挂多条效果** → 另开关联表（已实现为 `GearEffectRow`）。
4. **数值** → 按本文提案走：CG06 0.85 / CG08 0.90 与 1.10 / CG13 0.85（已实现）；
   CG05 显示强度、CG07 阈值 8 格与 1.15、CG14 ±10%、CG16 ±120ms 与 5 次沿用提案。

**仍待拍板**

1. **通用件与现有 `E01–E10` 的关系**：经济向内容搬到天赋侧、`equipment_*` 表退役、容量 20 → 8（设计稿 §5.2 / §7.1）。
   这条挡着"在游戏里拿到通用件"（商店与掉落还是按 `E01–E10` 走的）。
2. **CG04 耳机**：滤波近似要不要做，还是等真分轨。
3. **CG03 抄歌本**：路线随机化（D6）之前先不落地。

---

## 6. 已知缺口（不属于本文档范围，但会挡住某些件）

- **谱面网格 / 密集段**（roadmap §7.5）：CG02、CG07 的前置。
- **路线随机化 D6**：CG03 的前置。
- **分轨音频**（设计稿 §7.4）：CG04 的前置。
- **负面状态系统**（设计稿 §5.3）：CG10 的前置。
- **卡池目标 20 张**：现在还差 4 张（设计稿 §7.3），留到扩章节时补。
- **局内外两条账与天赋重排**（设计稿 §7.1 / §7.2）：第 1 批不看它也能做，但排第 2 批之前要先定。
- **拿到通用件的通路**：第一批的件已能挂效果，但商店 / 掉落仍只认 `E01–E10`，所以**在游戏里还买不到**；
  要等上面那条 `E01–E10` 的拍板。
