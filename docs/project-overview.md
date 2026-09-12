# FallenAngel — Project Overview

> What this game is, how it plays, how far along it is, and what comes next.
> Coding standards live in [architecture.md](architecture.md); the AI collaboration entry point is `CLAUDE.md` in the repository root.

## 1. Overview

FallenAngel is a **vertical-scrolling rhythm game** built as a job-application demo. Its main loop is already **meta progression + a route map**, not a single-song demo.

- **Current shape**: a main menu with four buttons (New Game / Load Profile / Song Select / Options) → a 9-node route map (free and paid branches, shop, three performances) → end-of-song settlement of cash and growth points → permanent talents. The play layer's main path is a 4-key drum chart; the chart format supports five v2 note types.
- **Envisioned shape**: a **roguelike rhythm game** — in-run buffs and debuffs change the conditions of play: the judgement line can disappear, timing windows can scale, the chart can go invisible. Which of these the experience actually needs, and whether judgement feel itself should move, is left open until it can be played. In-run modifiers are not implemented yet (see architecture.md §9).
- Portrait design (1080×1920), PC keyboard and mobile touch.

## 2. Project facts

| Item | Value |
| --- | --- |
| Engine | Unity 2022.3.62f3c1, C# 9 |
| Packages | TextMesh Pro, UGUI, UnityWebRequest (dependencies kept deliberately minimal) |
| UI font | Source Han Sans CN Regular (SIL OFL, `Assets/Fonts`) |
| Version control | git |

## 3. Core gameplay

### 3.1 Rules

- **Lanes**: v1 charts use 4 keys (D/F/J/K); v2 charts (`formatVersion >= 2`) currently force 5 keys (guitar mode is reserved). A `wide` note, or `lane 0` in a 4K chart, is a kick: any key judges it.
- Judgement compares hit time against note time (§3.2). Time is always read from `GameManager.SongTime`.
- **Note types** (`NoteType`):
  - **Normal / tap** — a single press.
  - **Hold (head + body + tail)** — press and hold through to the tail. The head scores nothing and does not count toward the judgement total, but a Good or Bad head still breaks combo; a hold only scores at its tail. Missing the head still auto-misses.
  - **Drag** — touching is an instant Perfect and can never miss; holding the lane through the window also scores. Silently recycled once the window passes.
  - **Flick** — judged on the tap window; direction currently only draws an arrow and does not affect judgement.
  - **Slide** — head judgement plus any key held to the tail (the same as a wide hold; dragging across lanes does not break it).
- Empty presses and presses outside the window produce no judgement; an un-hit normal note auto-misses once its window passes.

### 3.2 Judgement and scoring

Judgement windows in milliseconds (`JudgeDefine.cs`, modelled on Phigros):

| Judgement | Window | Base score | Combo |
| --- | --- | --- | --- |
| PERFECT | ±80 ms | 300 | kept |
| GREAT | ±160 ms | 200 (≈65% of Perfect) | kept |
| GOOD | ±180 ms | 0 | **broken** |
| BAD | ±200 ms | 0 | **broken** |
| MISS | outside the window / not hit | 0 | broken |

- Hold tails score ×1.5; release judgement is never worse than GOOD (a BAD release counts as GOOD).
- **Early/late indicator**: shows early/late when |Δt| > 40 ms.
- **Calibration**: Options → calibration; the global offset is layered on top of the chart offset (PlayerPrefs).
- **Combo bonus**: `base × (combo/100 clamped to 1 × 20%)`.
- **Accuracy** = weighted sum ÷ judgement count (weights 100/65/0/0/0 for P/G/G/B/M). Hold heads are excluded from the denominator.
- **Grade**: S (no miss and more than half Perfect) / A (accuracy ≥ 95) / B (≥ 85) / C (≥ 70) / D.

### 3.3 Flow

**Meta progression (the main entry point)**

```
Main menu --New Game / Load Profile--> Route map (portfolio overlay)
  -> pick a room (paid routes can tick E08) -> shop / empty room / battle
  -> performance (GameState: Loading -> Playing <-> Paused -> Result)
  -> income breakdown, auto-returns to the map after 2 seconds
```

Quitting mid-song leaves the save in `PLAYING`; browsing a profile does **not** settle it; only "Enter Game" settles it as an INTERRUPTED failure and opens a new run.

The state machine is still owned by `GameManager` for the play state. `GameState.Map` has no remaining callers (the old random-grid map was removed); the route map does not switch that state.

## 4. Controls

| Input | Behaviour |
| --- | --- |
| PC keyboard | 4K: **D / F / J / K**; 5K adds G. SPACE pauses/resumes; ESC exits (inside a run, ESC returns to the map or menu via Portfolio) |
| Touch | the lower half of the screen is split evenly by lane count; lifting outside the judge zone still releases by fingerId, so keys never stick |
| Editor mouse | `#if UNITY_EDITOR` simulates touch with the mouse |

## 5. Content and data

### 5.1 Chart JSON

`Assets/Resources/Charts/*.json.bytes`, deserialised with `JsonUtility`; fields must be public and match `ChartData` / `NoteData`.

v1 example (`type` is an integer 0–3):

```json
{
  "metadata": {
    "songName": "...",
    "audioFileName": "demo_song",
    "offset": 0.929,
    "bpm": 160,
    "formatVersion": 0
  },
  "notes": [
    { "lane": 0, "time": 2.0, "type": 0, "duration": 0, "longNoteId": -1 }
  ]
}
```

- v1 `type`: 0 = Normal, 1 = LongStart, 2 = LongBody, 3 = LongEnd.
- v2 (`formatVersion: 2`) adds `drag` / `flick` / `slide`, `events`, `wide` and `path`. See [chart-format.md](chart-format.md).
- Audio lives in `Assets/Resources/Audio/` and is referenced by `metadata.audioFileName`. **This repository ships no audio** (see the README, §9).
- All three stages (S01/S02/S03) currently share the same placeholder Easy drum chart; differentiating them is on hold. Song Select can load other charts from Resources.

### 5.2 Growth configuration

Read-only tables live in `PortfolioConfig.cs` (generated by an exporter). At runtime:

- **Shop**: weighted draw without replacement; the standing discount is max(D1, owned E07); K1 requires a tick.
- **Route cost**: standing F0 (F1 overrides the multiplier); E08 requires a tick.
- **Income (Lite)**: I0/I1, J0/J1, D0, E05, G0/G1, C1, E09; capped at B×1.5. Phrase-level effects (A0–C0, K0/K2, E01–E04/E06) are not wired into settlement and are labelled "not yet settled" in the UI.
- **Save**: an independent JSON file in persistentDataPath, written with a temp file + `File.Replace`, revision CAS.

### 5.3 Scenes and prefabs

- The playable scene is rebuilt from the menu: `Tools > FallenAngel > Build Default Game Scene`; `Create Note Prefab` generates `Assets/Prefabs/Note.prefab`.
- The AI assistant does **not** touch `.unity` files, prefabs or `.meta` files (except the matching `.meta` when a script is deleted).
- Rebuild after changing the SceneBuilder, otherwise the old scene keeps Missing Script entries (for example the deleted RunManager).

## 6. Running and debugging

1. Open the project → (if the structure is stale) `Tools > FallenAngel > Build Default Game Scene` → `Create Note Prefab`.
2. Play → **New Game** or **Load Profile → Enter Game**. An editor-only floating bar can skip battles and grant equipment.
3. Compile self-check: close the editor, then run `./compile_check.sh`.
4. Growth config self-check: `Tools > FallenAngel > Validate Portfolio Config and Talents`.
5. Logs: `Logs/` and the Console, prefixed with `[ClassName]`.

## 7. Known issues and technical debt

| Issue | Impact | Plan |
| --- | --- | --- |
| All three stages share one Easy placeholder chart | Paid challenges have no chart difference | On hold |
| Third-party audio used during testing | Not distributable with the portfolio or an APK | Replace with licensed audio before release |
| v2 always uses 5 keys; `chart.events` is not consumed | A 4K chart labelled v2 loses kicks; tempo-change events are a shell | On hold |
| Flick has no direction judgement | Arrow only | Gameplay polish |
| TMP Examples & Extras | May ship into player builds | Reduce build size |
| In-run modifiers not built | Vision not landed | architecture.md §9 |
| Drop tables not running | Stage drops award nothing | Later |

## 8. Roadmap

**The full checklist of what is still missing lives in [roadmap.md](roadmap.md).** The lists below are the short version.

**Landed**

- Main menu / profiles / options / route map / shop / talents / equipment inventory
- Income Lite, F0 route cost, K1/E08 ticks, E09 interest
- 4K feel main path, drag hold polling, wide-note any-key hold

**Next**

- Rebuild the scene to drop the old MapPanel / RunManager
- Before release: audio, differentiated charts
- Working drops, a complete release build
- Stage three: in-run ModifierManager (§9)

---

## 中文原文（Chinese original）


# FallenAngel 项目说明

> 本文档回答"这个游戏是什么、怎么玩、现在做到哪、接下来做什么"。
> 代码规范与开发约定见 [架构约定.md](架构约定.md)；AI 协作入口见根目录 CLAUDE.md。

## 1. 游戏概述

FallenAngel 是一款 **竖屏下落式节奏音游**（求职 Demo），主循环已经是 **局外成长 + 行程地图**，不是单曲 START DEMO。

- **当前形态**：主菜单四按钮（新游戏 / 选择存档 / 自选曲目 / 选项设置）→ 9 节点行程地图（免费/付费分叉、商店、三次演奏）→ 曲终结算现金与成长积分 → 永久天赋。演奏层是 4K 鼓谱主路径，谱面格式支持 v2 五类音符。
- **愿景形态**：**roguelike 音游**——局内随机 buff/debuff 改变演奏条件：判定线可以消失、判定窗口可以缩放、谱面可以隐身。这些效果最终采用哪些、要不要动判定手感，由实机体验决定，现在不预设结论。局内 modifier 尚未实现（见 architecture.md §9）。
- 竖屏设计（1080×1920），PC 键盘与手机触屏。

## 2. 工程信息

| 项目 | 内容 |
| --- | --- |
| 引擎 | Unity 2022.3.62f3c1（国内版），`D:\unity\2022.3.62f3c1` |
| 语言 | C# 9 |
| 依赖包 | TextMesh Pro、UGUI、UnityWebRequest（保持最小依赖，不新增） |
| UI 字体 | 思源黑体 CN Regular（SIL OFL，`Assets/Fonts`） |
| 版本控制 | git |
| 工程路径 | `C:\Users\LorXer\Documents\trae_projects\FallenAngel` |

## 3. 核心玩法

### 3.1 基本规则
- **轨道**：v1 谱 4 键（D/F/J/K）；v2 谱按 `formatVersion>=2` **当前强制 5 键**（吉他模式预留）。4K 谱 `lane 0` 或 `wide` 标记为宽键/kick：任意键判定。
- 判定按「击打时刻 − 音符时刻」的时间差（见 3.2）。时间一律读 `GameManager.SongTime`。
- **音符类型**（`NoteType`）：
  - **Normal / tap**：单击。
  - **长按（LongStart + 身/尾）**：头按下后按住至尾；**头不计分、不计判定次数**，Good/Bad 头仍断连击；一首 hold 只在尾部计分。漏按头仍自动 Miss。
  - **Drag**：碰即 Perfect，永不记 Miss；按住该轨穿过窗口同样得分。过窗静默回收。
  - **Flick**：按 tap 窗口判定；方向目前只做视觉箭头，不参与判定。
  - **Slide**：头判定 + 任意键维持至尾（与宽长按相同，跨轨拖动不中断）。
- 空按与过早/过晚（窗外）不产生判定；普通音符超窗未击中自动 Miss。

### 3.2 判定与计分

判定窗口（毫秒，`JudgeDefine.cs`；参考 Phigros）：

| 判定 | 窗口 | 基础分 | 连击 |
| --- | --- | --- | --- |
| PERFECT | ±80ms | 300 | 保持 |
| GREAT | ±160ms | 200（≈Perfect 65%） | 保持 |
| GOOD | ±180ms | 0 | **断** |
| BAD | ±200ms | 0 | **断** |
| MISS | 超过窗口 / 未击中 | 0 | 断 |

- 长按尾部得分 ×1.5；释放判定不低于 GOOD（BAD 按 GOOD 计）。
- **早/晚指示**：|Δt| > 40ms 显示「早」「晚」。
- **节拍校准**：选项设置 → 打开校准；全局 offset 叠加在谱面 offset 之上（PlayerPrefs）。
- **连击加成**：`基础分 × (combo/100 截断到 1 × 20%)`。
- **准确率** = 加权和 ÷ 判定总数（P/G/G/B/M 权重 100/65/0/0/0）。长按头不进分母。
- **评级**：S（0 Miss 且 Perfect 过半）/ A（acc≥95）/ B（≥85）/ C（≥70）/ D。

### 3.3 游戏流程

**局外成长（正式入口）**

```
主菜单 ──新游戏/选择存档──> 行程地图（Portfolio 覆盖层）
  → 选房间（付费路可勾选 E08）→ 商店 / 空房 / 战斗
  → 演奏（GameState: Loading → Playing ↔ Paused → Result）
  → 收益明细 2 秒后自动回地图
```

演奏中途强退：存档保持 `PLAYING`；点选存档浏览**不会**结算；点「进入游戏」才按 INTERRUPTED 失败并开新局。

**状态机**仍由 `GameManager` 管演奏态。`GameState.Map` 已无调用方（旧随机格地图已删除）；行程地图不切换该状态。

## 4. 操作方式

| 输入 | 说明 |
| --- | --- |
| PC 键盘 | 4K：**D / F / J / K**；5K 加 G。SPACE 暂停/继续；ESC 退出（成长局内 ESC 回地图/菜单由 Portfolio 处理） |
| 触屏 | 屏幕下半部按轨数等分；抬出判定区也会按 fingerId 释放，避免卡键 |
| 编辑器鼠标 | `#if UNITY_EDITOR` 用鼠标模拟触摸 |

## 5. 内容与数据

### 5.1 谱面 JSON

`Assets/Resources/Charts/*.json.bytes`，`JsonUtility` 反序列化，字段须 public 且与 `ChartData`/`NoteData` 一致。

v1 示例（`type` 整数 0–3）：

```json
{
  "metadata": {
    "songName": "…",
    "audioFileName": "demo_song",
    "offset": 0.929,
    "bpm": 160,
    "formatVersion": 0
  },
  "notes": [
    { "lane": 0, "time": 2.0, "type": 0, "duration": 0, "longNoteId": -1 }
  ]
}
```

- v1 `type`：0=Normal，1=LongStart，2=LongBody，3=LongEnd。
- v2：`formatVersion: 2`，另有 `drag`/`flick`/`slide`、`events`、`wide`、`path`。协议见 [谱面转换协议.md](谱面转换协议.md)。
- 音频 `Assets/Resources/Audio/`，对应 `metadata.audioFileName`。
- **当前三关（S01/S02/S03）均绑定占位 Easy 鼓谱**（内容差异搁置）。自选曲目可加载 Resources 内其它谱。

### 5.2 成长配置

只读表在 `PortfolioConfig.cs`（由导出器生成）。运行时：

- **商店**：权重抽候选；常驻折扣 max(D1, 持有 E07)；K1 确认勾选。
- **路费**：常驻 F0（F1 覆盖系数）；E08 确认勾选。
- **收益 Lite**：I0/I1、J0/J1、D0、E05、G0/G1、C1、E09；封顶 B×1.5。乐句级 A0–C0、K0/K2、E01–E04/E06 未接线，UI 标「尚未接入结算」。
- 存档：persistentDataPath 独立 JSON，临时文件 + `File.Replace`，revision CAS。

### 5.3 场景与预制体

- 可玩场景由菜单重建：`Tools > FallenAngel > Build Default Game Scene`；`Create Note Prefab` 生成 `Assets/Prefabs/Note.prefab`。
- AI **不改** `.unity` / 预制体 / `.meta`（删死代码时的配套 `.meta` 除外）。仓库里可能已有入库的 `FallenAngel.unity`，与 SceneBuilder 双源；改 UI 层级后请重建场景。
- 改完 SceneBuilder 后必须重建，否则旧场景会留下 Missing Script（例如已删除的 RunManager）。

## 6. 运行与调试

1. 打开工程 →（结构过期时）`Tools > FallenAngel > Build Default Game Scene` → `Create Note Prefab`。
2. Play → **新游戏**或**选择存档 → 进入游戏**。编辑器左上角悬浮条（`#if UNITY_EDITOR`）可跳过战斗/发装备。
3. 编译自查：关闭编辑器后 `./compile_check.sh`。
4. 成长配置自检：`Tools > FallenAngel > Validate Portfolio Config and Talents`。
5. 日志：`Logs/` 与 Console，前缀 `[类名]`。

## 7. 已知问题与技术债

| 问题 | 影响 | 处理计划 |
| --- | --- | --- |
| 三关共用 Easy 占位谱 | 付费挑战无谱面差异 | 搁置（换差异谱） |
| `demo_song.mp3` 版权曲 | 不可随作品集/APK 外发 | 交付前换可授权音频 |
| v2 一律 5 键、chart.events 不消费 | 4K 鼓谱标 v2 会丢 kick；变速事件空壳 | 谱面工作搁置 |
| Flick 无方向判定 | 只有箭头 | 玩法打磨 |
| TMP Examples & Extras | 可能打进玩家包 | 减包 |
| 局内 modifier 未做 | 愿景未落地 | 架构约定 §9 |
| 掉落表未运行 | 关卡掉落不发奖 | 后置 |

## 8. 路线图

**完整待办清单见 [roadmap.md](roadmap.md)**，下面只是简版。

### 已落地（相对旧「单曲 demo」文档）
- 主菜单 / 存档 / 设置 / 行程地图 / 商店 / 天赋 / 装备背包
- 收益 Lite、F0 路费、K1/E08 勾选、E09 利息
- 4K 手感主路径、Drag 按住轮询、宽键任意键维持

### 下一步
- 权威文档已与代码对齐（本次）；场景重建以去掉旧 MapPanel/RunManager
- 交付前：音频、差异谱
- 掉落发奖、完整发布包
- 阶段三：局内 ModifierManager（§9）
