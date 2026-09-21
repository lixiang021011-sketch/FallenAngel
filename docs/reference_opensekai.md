# 参考研究：OpenSekai（PJKS 复刻）vs FallenAngel

> 研究日期 2026-09-20；**2026-09-21 更新**（划掉已完成项、补上时基层差距，见 §6）。
> 本地克隆：`C:\Users\LorXer\Documents\trae_projects\_refs\OpenSekai`（depth=1，不在本仓库内）。
> 对象：`github.com/cubeww/OpenSekai` —— C#，MIT，71 stars，最后推送 2026-09-10，Unity **2022.3.62f2**、URP 14（与本项目同版本，代码可直接对照阅读）。

## 0. 规模与定位

| | OpenSekai | FallenAngel |
| --- | --- | --- |
| 玩法本体代码 | `Sekai/Live` + `Sekai/Core/Live` = **80 文件 / 9,720 行** | `Assets/Scripts` 共 99 文件 / 20,686 行（含 roguelike 成长、谱面编辑器、美术工作台等） |
| 全工程代码 | `Sekai` 命名空间 1,227 文件 / 119,294 行（含第三方） | 99 文件 |
| 自动化测试 | **0**（无 `Tests` 目录、无测试 asmdef，仅 3 个第三方 asmdef） | **204 项无头自检 / 11 组**（2026-09-21 全量复跑全绿） |
| 定位 | 原作的**玩法复刻**（判定/渲染/谱面导入/谱面编辑器），无原创玩法层 | 原创 roguelike 音游 |

结论先行（**2026-09-21 复核**）：几何层（假透视、长按/滑条带子）与规则结构（判定窗口表）已经对齐；
**剩下的是"时基层"**——输入事件时间戳、每指记账、音频时钟平滑与输出延迟补偿。再往外是过线表现、
谱面事件运行时、移动端适配。下面按"能拿过来用"的价值排序，✅ = 已落地。

---

## 1. 判定窗口是「按音符类型的表 + 前后不对称」（结构值得照搬）

证据：`Sekai/Live/LiveConfig.cs:174-190`（表）、`:326-395`（判定实现）、`Sekai/Live/JudgeTimeData.cs`、`Sekai/LiveUtility.cs:95-160`。

窗口用 **60fps 帧数**定义，`CalcFrameToTime(frame) = frame/60`（`LiveConfig.cs:275`）：

| 类型 | Perfect 前/后 | Great 前/后 | Good 前/后 | Bad 前/后 |
| --- | --- | --- | --- | --- |
| Normal | 2.5 / 2.5 | 5 / 5 | 6.5 / 6.5 | 7.5 / 7.5 |
| Flick | 2.5 / 2.5 | 6.5 / 7.5 | 7 / 8 | 7.5 / 8.5 |
| Long | 2.5 / 2.5 | 5 / 5 | 6.5 / 6.5 | 7.5 / 7.5 |
| Long_End | 3.5 / 4 | 6.5 / 8 | 7.5 / 8.5 | 7.5 / 8.5 |
| Long_End_Flick | 3.5 / 4 | 6.5 / 8 | 7.5 / 8.5 | 7.5 / 8.5 |
| Normal_Critical | 3.3 / 3.3 | 4.5 / 4.5 | 6.5 / 6.5 | 7.5 / 7.5 |

换成秒（Normal）：Perfect **±41.7ms**、Great ±83ms、Good ±108ms、Bad ±125ms；Long_End 前 58ms / 后 67ms。
另有 **JustPerfect = ±1/120s（8.3ms）**（`LiveConfig.cs:425-426`），落在里面返回 `NoteResult.JustPerfect`，
否则用 `NoteResultDescription` 给出 **Fast / Late / Just / FlickMiss**（`:403-411`）——就是"早/晚"指示的档位。

结构要点（与我们的差异）：

1. **每个音符类型一张窗口表**，不是一个全局窗口（`LiveUtility.GetINoteToJudgeFrameType` 把 note 映射到表项，`hasParent` 决定判头还是判尾）。
2. **前/后分开**：后判普遍更宽容（Flick 的 Great 是前 6.5 / 后 7.5 帧）。我们现在对称。
3. **Flick 方向错不判 Miss**：`isDirection == false` 且落在 Perfect 区间 → 返回 `Great` + `FlickMiss` 描述（`LiveConfig.cs:375-378`）。

我们现状（**2026-09-20 已落地** ✅）：`Core/JudgeWindowTable.cs` 按 `JudgeNoteKind`（Normal / Wide / Drag /
Flick / Slide / HoldHead / HoldEnd）查表，每档分前/后两个值，并带 `JudgeTimingHint`（Just / Fast / Late）；
**默认每一行都等于原来的对称基准 ±80/160/180/200ms，所以引入表本身零行为变化**。
`Core/JudgeWindowChecks.cs` 8 项把这个结构钉住：七类默认等同基准、Flick 前后不对称生效、按种类相互独立、
modifier 覆盖整张表、退化表回退对称、长按尾走有效窗口、时机提示档位。

有意保留的差异：**数值仍是 Phigros 派**（`JudgeWindowTable` 顶部注释写明），只是"以后要单独调某一类"或
"某类天赋更宽"时只改表，不动判定代码。

---

## 2. 输入判定：候选表 + 重叠归属 + 每指时间戳（最能解决我们的痛点）

证据：`Sekai/Core/Live/LiveLogic.cs`（1,148 行），主循环 `Judgment()` 在 `:644`。

一条输入要经过五步（`:644-651`）：

| 步骤 | 方法 | 作用 |
| --- | --- | --- |
| ① | `AllocateInputJudgableEachNotes` `:654` | 对每个「输入 × 待判音符」组合调 `note.IsJudgment(touch, lane)`，命中就进该输入的 `CandidateNotes` |
| ② | `OverlapJudgmentSorting` → `ResolveOverlappingCandidatesAcrossInputs` `:709` | **同一个音符被两根手指同时占为候选 → 比轨道距离，近者得、远者移除** |
| ③ | `LeaveNotesFastGeneration` `:758` | 同一根手指有多个候选 → 保留最早的那个（同指快速连打不误判） |
| ④ | `PickUpNotBeginInputNotes` `:809` | 处理"手指已按住、音符随后进窗"的拾取 |
| ⑤ | `EvaluateJudgment` `:827` | 逐个应用 `note.Judgment()`，未用到的输入交给 `LiveViewExt.Unpicked`（按键反馈） |

另外两条关键机制：

1. **每指记账 + 时间阈值**：`InputedNormalNoteTouch[touchId] = musicTime`；
   同一根手指要再判下一个音符，必须 `musicTime - 上次 > 1/6 s` 或新音符结果 > Great（`:922`）。
   → **这是用"时间"而不是"帧号"来做"一次输入判一个"**。我们上轮用的是 `PressJudgedThisFrame`（帧号护栏），
   `91f6169` 又补了 20ms 同刻容差；他们这套相当于把两件事统一成"每指一条时间线 + 候选排序"。
2. **输入时间戳换算成歌曲时间**：`musicTime = (touch.time - currentGameTime) * (1/60) + currentFrameInfo.time`
   （`:510`、`:606`）——用系统触摸事件的**时间戳**对齐谱面，而不是"这一帧读到输入时的 SongTime"。
   移动端判定精度的关键；我们目前读 `GameManager.SongTime`（帧粒度）。

触摸 → 轨道同样是这套思路：`GetJudgmentLane()`（`:984-1023`）在 `LiveConfig.JudgmentPositions`（`Vector2[]`
折线）里定位触点落在哪一段，返回**小数轨道**；音符用 `JudgeLaneStart..JudgeLaneEnd`（浮点）匹配，
所以"跨轨宽音符""梯形轨道"天然支持。我们目前是 `LaneLayout.GetLaneFromCanvasX` 等分。

**2026-09-21 复核：这一节仍是最大差距**，但已经从"整段缺失"缩成三条具体的事：

1. **输入事件时间戳**（`LiveLogic.cs:510/606`）：他们用触摸事件的 `time` 换算 `musicTime`；我们读
   `GameManager.SongTime`（帧粒度）。差值就是 ±1 帧：60fps ≈ 8ms、30fps ≈ 17ms。
2. **每指记账 + 候选/归属/应用三段**（`LiveLogic.cs:644-651`、`:922`）：我们仍是 `PressJudgedThisFrame`
   帧护栏 + 20ms 同刻容差（`JudgeManager.cs:44/168`）。作者视角："一次按下判几个音符"的问题我们已经连修三轮，
   根因就是缺这三段。
3. **音频时钟平滑 + 输出延迟补偿**：见 §6 P1 第三条——他们 `SoundManager.GetAudioSyncedUnityTimer()`
   把播放位置与 Unity 时间平滑对齐，另有 `fillerSec`（音频垫静音）补偿整段输出延迟；我们直读 `AudioSource.time`。

已经具备的：flick 方向在**触摸路径**上真的参与判定（`JudgeManager.cs:373-381` 用滑动向量 + 最小滑动距离，
键盘/编辑器鼠标放宽为单击），同押配对、长按尾释放判定、滑条跟手容差都在。

建议按 §6 的顺序做：把第 1、2 条合成一件事（同一个问题），先补"输入时间戳""两指同时压同一音符"两个用例，
`JudgeTimingChecks` 7 项当回归网。

---

## 3. 渲染：假透视「近大远小」+ 着色器裁剪（直接对应美术诉求）

证据：`Sekai/Live/LiveConfig.cs:292`、`Sekai/BaseNoteView.cs:41-90`、`Sekai/NotePool.cs`、
`Sekai/NoteLineView.cs`、`Assets/.../shader/live/note/note-default.shader:70`。

1. **位置映射是非线性的**：`GetNoteViewProgress(p) = 1.06^((min(p,2)-1)*45)`
   —— p=0 → 0.07，p=1 → 1.0，p>1 继续指数放大（过线后飞出）。
2. **位置 + 缩放**：`BaseNoteView.Move()`
   `position = LerpUnclamped(spawnPosition, laneCenter, viewProgress)`、`localScale = Vector3.one * viewProgress`
   → 音符从上方**一个点**出发、边下落边放大到满尺寸，过判定线后继续变大飞出。**这就是"近大远小"**。
3. **音符用 SpriteRenderer（世界空间）**，把 `(轨道中心, 轨道宽, 进度)` 塞进**顶点色 RGB** 交给自定义 shader
   （`UpdateSpritePayload`）；shader 里 `clip(_NoteShowRate - screenUv.y)` 在屏幕上部**裁掉**音符——
   所以音符不是"突然出现"，是被屏幕边界切断（`_NoteShowRate` = 音符显示范围设置项）。
4. **长按/滑条是一个 100 段的动态网格**（`NoteLineView`，`SplitMeshCount = 100`，额外 10 段缓冲），
   逐段算顶点，所以能画出贴合轨道折线的弯曲带子（我们 2026-09-21 也已换成分段带子，见下）。
5. **对象池按 (类别 × 类型) 分池**（14 个池，`NotesViewManager.CreateNotePool`），
   池容量由 `LiveUtility.CalculateMaxSimultaneousNotes` **按谱面算出的最大同屏数**决定，不是拍脑袋给常量。

我们现状（**2026-09-21 已落地** ✅，预览稿在 `art_tools/note_perspective/`）：

- 第 1、2 条：`Core/PlayPerspective.cs` 用**同一条曲线**驱动位置与缩放（`1.06^((min(p,2)−1)×45)`，归一化到
  p=0 → 生成点、p=1 → 判定线），并加了**尺寸下限 0.35**（原作口径出生只有 7%，高密度段会糊成小点；作者选型 B）。
  轨道用 `UI/PerspectiveLaneGraphic.cs` 画同一曲线的梯形网格；`Core/PerspectiveChecks.cs` 7 项断言
  "任意高度上音符半宽 == 轨道半宽"，实测偏差 0px。
- 第 4 条：长按身体与 slide 轨迹改用 `UI/NoteBandGraphic.cs`——按高度采样画带子，每个采样点半宽 =
  `LaneLayout.LaneWidth/2 × SizeScaleAtY(该点画布 y)`。我们只需 12 段（原作 100 段）：位置与尺寸共用同一条曲线，
  所以尺寸随画布 y **在折点以下精确线性、以上恒定**，只在折点补一个结点就完全贴合（`NotePartChecks` 实测
  越界 0px / 内缩 0px，含"贴判定线"姿态）。
- 第 3 条（着色器裁剪）：我们不需要——音符从画布上方 1200（屏外）出生，进出由屏幕边界完成。
  但**过线之后的行为不同**：原作 `LerpUnclamped` 继续放大飞出，我们在 p=1 定格、命中即消失、Miss 原地淡出。
  这是剩余差异里最容易被看出来的一条，已列入 §6 P1。
- 第 5 条：`NoteSpawner.PrewarmForChart` + `EstimateMaxSimultaneousNotes` 已按谱面预分配
  （`Core/NotePoolChecks.cs` 11 项）；仍是**单池**，原作按 (类别 × 类型) 分 14 个池。

---

## 4. 谱面导入：SUS 解析器（对应我们"导入开源谱面"待办）

证据：`Sekai/SUS/Converter.cs`（1,168 行）+ `NoteInfo/BpmInfo/HighSpeedInfo/VolumeInfo/TimeSignatureInfo`。

- SUS 文本用正则解析（`EventNotesRegex`/`BpmRegex`/`VolumeRegex`/`TicksPerBeatRegex`/`HighSpeedRegex`）；
  `DefaultTicksPerBeat = 480`、`LaneMaxIndex = 11`（12 条 lane 线）。
- 音符有 `Lane` 与 `Width`：`laneStart = Lane`、`laneEnd = Lane + Width - 1` → **宽度是轨道数**。
- **镜像**：`mirroredStart = 11 - laneEnd; mirroredEnd = 11 - laneStart`。
- 支持的谱面事件：BPM、音量、拍号、HIGHSPEED、guide 轨、pair（同押）、friction（滑条）、LongHoldCombo。
- 内部数据模型 `MusicScoreInfo { bar, barProgress, time, bpm, timeSignature, speedRatio, seVolume }`
  ——**每个音符/事件自带音乐位置 + 已解析时间 + 当时的 BPM/拍号/流速**；另有 `musicScoreInfoArray` 时间轴与
  `FilterBeforeTimeSec`（从中间开始练）。

我们现状（**2026-09-20 已落地** ✅）：`chart_tools/chart_import.py` 已能解析 SUS（通道 02 = 拍号、
08 = BPM、1x = 普通轨、3x = 长按、5x = flick、9x = guide）以及 SSC/SM；音符映射
tap→tap、hold→hold、friction→drag、flick→flick，轨道按源键数线性铺到 0..4。
本机放了几首 pjsk 谱做真机自测（`Core/LocalSongPackChecks.cs` 4 项，曲包按 `.gitignore` 不入库）。

仍缺：**pair（同押连线的谱面语义）**——我们现在靠运行时同刻配对推断；critical；guide 轨的可见性；
以及 SUS 的 HIGHSPEED / 音量 / 技能事件（见 §6 P2"谱面事件运行时生效"）。

---

## 5. 其它可选项（低优先）

- **设置项清单**（`Sekai/LiveSettingData.cs`，17 项）：音符速度、判定偏移、亮度、轨道透明度、cut-in 开关、
  隐藏技能与表扬、**同押连线开关**、震动、AP 特效、音符透明度、长按线透明度、**音符皮肤**、判定音 SE、
  **镜像谱面**、Fast/Late 提示、**120FPS 开关**、音符特效、音符显示范围、Fever 特效类型。
  我们已有：音效音量、按键特效开关、语言（`Core/GameSettings.cs`、`UI/SettingsPanelController.cs`）、
  判定校准（`Core/CalibrationSettings.cs` + `UI/CalibrationController.cs`）、下落速度（`UI/FallSpeedController.cs`）、
  120FPS（`GameManager` 的 `Application.targetFrameRate = 120`）。
  还缺：亮度、轨道/音符/长按线透明度、同押线开关、镜像、震动、音符皮肤、判定音选择、AP 特效、音符显示范围。
- **自动演奏**：`LiveLogic.OnAutoInput`（`:253`）+ `NoteResult.Auto` —— 可用于演示 / 录制宣传素材。
- **生命与伤害**：`Life = 1000`；`Damages = { Bad: 60, Miss: 80 }`（`LiveConfig.cs:424-442`）。
- **技能/分数**：`Core/Live/SkillLogic`、`ScoreGaugeCalculator`、`ScoreLogic` 是原作角色技能 + 分数加成，
  与我们的 roguelike modifier 不在同一层，参考其"按歌曲时间触发效果"的写法即可，不必搬。
- **谱面编辑器**：`Sekai/MusicScoreMaker` 533 个文件（Ingame 250 / OutGame 274），是独立 app 规模的编辑器，
  含试玩（`MusicScoreMakerTestPlayDialog`）。我们只有一个 `ChartEditorWindow`；规模差距大，不建议对齐，
  但它的"编辑器内直接试玩"值得我们保留/加强。

---

## 6. 差距清单（2026-09-21 更新）

> **2026-09-21 进展**：P1 的「输入事件时间戳对齐音乐时间」与「每指记账 + 候选/归属/应用」已落地
> （见开发记录 2026-09-21；提交 `e9f2d9f` / `e5e99c5` / `8e8adf8` / `fd1938a` / `f914fae`），
> 「音频时钟平滑 + 输出延迟补偿」只落了钩子（数值待真机标定）。
> 「过线飞行 + 屏幕裁剪」「安全区 / 宽高比」「谱面事件运行时生效」仍在队列。

### P1 · 判定时基层（还差一整层，最该做）

| 事项 | 他们的做法（证据） | 我们的现状 | 影响 |
| --- | --- | --- | --- |
| **输入事件时间戳对齐音乐时间** | `LiveLogic.cs:510/606`：触摸事件时间戳 → `musicTime` | `GameManager.SongTime`，帧粒度（`GameManager.cs:206`） | 判定误差 ±1 帧：60fps ≈ 8ms、30fps ≈ 17ms |
| **每指记账 + 候选 → 归属 → 应用** | `LiveLogic.cs:644-651` 五步；`:922` 同一根手指 1/6s 内不再判下一个 | `PressJudgedThisFrame` 帧护栏 + 20ms 同刻容差（`JudgeManager.cs:44/168`） | "一次输入判几个音符"我们已经连修三轮，根因就是缺这三段 |
| **音频时钟平滑 + 输出延迟补偿** | `SoundManager.GetAudioSyncedUnityTimer()` 平滑播放位置；`fillerSec` + `audiofiller.html` 垫静音补偿整段延迟 | `AudioManager.cs:74` 直读 `bgmSource.time` | DSP buffer 粒度抖动 + 整段设备延迟；现在只能靠玩家手动校准 |
| **触摸 → 轨道：折线坐标 + 小数轨道** | `LiveConfig.JudgmentPositions` 折线定位，音符带 `JudgeLaneStart/End` | 等分 `GetLaneFromCanvasX` + 宽键特判 | 轨道再变形要同时改判定；跨轨宽键难自然支持 |
| **过线后继续飞 + 屏幕裁剪** | `BaseNoteView.Move()`（`LerpUnclamped`，p>1 继续放大飞出）+ `NoteShowRateView`/`NotePool.SetupNoteMask` | p 被 `Clamp01` 定格在判定线；命中即消失、Miss 原地淡出 | **最容易看出来的差异**：漏掉的音符没有"漏过去"的反馈 |

### P2 · 表现与内容

| 事项 | 他们 | 我们 |
| --- | --- | --- |
| 长按持续特效 / flick slash / 空按反馈 | `LongTapEffectView`（每根长按一对 aura+gen 粒子）、`TapEffectView.FlickSlash()`、`Unpicked()` | 只有音符处双层环 + 放射条 + 轨道压暗/涟漪 |
| 背景表现 | `Background2DView`（jacket 渲染 / 2DMV 视频 / 与音乐时间对齐 / 亮度设置） | 一张静态 `bg_gameplay` + 少量漂移 |
| 皮肤体系 | 判定线皮肤 `MasterIngameJudgeFrame`、`NoteSkinIndex`、`NoteSeIndex` | 判定线是一根白条；外观走配表 + 3 版配色（≈半个皮肤系统） |
| **谱面事件运行时生效** | volume / timeSignature / HighSpeed / guide / pair / skill 都在运行时消费 | 只有 `bpm` 在导入期积分成时间；**`speed` 事件被丢弃**，运行时无人读 `chart.events` |
| 音符语义覆盖 | friction（4 种）/ hidden / guide / Long_End_Flick / Normal_Critical / LongHoldCombo | Sonic 派 tap/drag/flick/hold/slide（对齐与否是产品选择） |
| 练习与演示便利 | `FilterBeforeTimeSec` 从中间开始；`AutoJudgment` 自动演奏（录宣传素材） | 都没有；只有编辑器内试玩 |

### P3 · 移动端与杂项

| 事项 | 说明 |
| --- | --- |
| **安全区 / 异形屏** | 他们有 `UniSafeAreaAdjuster` + `ScreenConfig`/`ScreenCanvasScaler` 按机型算布局；我们没有任何安全区处理，刘海/圆角可能切掉 HUD 或判定线 |
| 设置项 | 见 §5：还缺亮度、轨道/音符/长按线透明度、同押线开关、镜像、震动、音符皮肤、判定音选择、AP 特效、音符显示范围 |
| 生命值 / 分数槽 | `LifeView`（Life=1000，Bad −60 / Miss −80）、`ScoreGauge`；我们无 HP，分数槽对应 roguelike 收益层（可换皮） |
| 对象池按类型分池 | 他们 14 个池；我们单池（按谱面预估容量已落地 ✅） |

### 已关闭（对照 2026-09-20 版清单）

| 事项 | 落地位置 |
| --- | --- |
| 音符假透视（位置曲线 + 尺寸下限） | `Core/PlayPerspective.cs`、`UI/PerspectiveLaneGraphic.cs`、`Core/PerspectiveChecks.cs`（7 项） |
| 长按 / slide 网格带（贴轨道） | `UI/NoteBandGraphic.cs`（12 段，越界 0）、`Core/NotePartChecks.cs` 几何断言 |
| 判定窗口表化（按类型 + 前后不对称 + Just/Fast-Late） | `Core/JudgeWindowTable.cs`、`Core/JudgeWindowChecks.cs`（8 项） |
| 对象池按谱面最大同屏数预分配 | `NoteSpawner.PrewarmForChart`、`Core/NotePoolChecks.cs`（11 项） |
| SUS / SSC 谱面导入 | `chart_tools/chart_import.py`、`Core/LocalSongPackChecks.cs`（4 项） |
| 下落速度设置 / 120FPS / 判定音 / 空按处理 / 判定文字与早晚指示 / 同押连线 / 结算 rank·acc·FC·AP | `UI/FallSpeedController.cs`、`GameManager`、`AudioManager.PlayHitSfx`、`JudgeManager` 空按分支、`UI/HUDController.cs`、`UI/NoteLinkGraphic.cs`、`UI/ResultScreen.cs` |

### 建议顺序

1. **输入时间戳 + 每指记账合成一件事做**（P1 前两条）：同一个问题，分开做只会再补一层兜底。
   先补"输入时间戳""两指同时压同一音符"两个用例，`JudgeTimingChecks` 7 项当回归网。
2. **过线飞行**（P1 最后一条）：纯表现、零判定风险；按第 8 条铁律先出 `art_tools/` 预览。
3. **音频时钟平滑 + 输出延迟补偿**（P1 第三条）：真机手感收益大，但要能在手机上量化才有意义。

## 7. 反面参考（别学的）

1. **全局可变静态状态**：`MusicScore.CurrentFrameInfo`、`LiveConfig.NoteBundleName`、`LiveConfig.MinNoteSpeedTime`
   等都是 static，随设置/歌曲变化被改写——我们已定的"单一数据源 + 只读聚合"（`PlayVisualSpec`/`PlayRules`）更好。
2. **零自动化测试**：9,720 行玩法核心全靠手点验证。他们的 `TODO(original): confirm the friction-specific judge
   frames once the matching master data is imported`（`LiveConfig.cs:187`）就是没有回归网的结果。
3. **命名与结构混杂**：同一份逻辑里混着原作 master data、AB 包名与玩法；我们保持配表与实现分离更干净。
