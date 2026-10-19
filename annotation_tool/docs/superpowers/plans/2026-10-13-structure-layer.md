# Attention Annotator 结构层 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development for the pure modules. Steps use checkbox (`- [ ]`) syntax.

**Goal:** 让 Annotator 读 Workbench 已经算好的小节/段落/边界，用同一套小节坐标显示、导航、循环、显示机器判断，并新增两类轻量人工事件（这里换段 / 边界复核）——同时保证机器结果不污染人工原始标注。

**Architecture:** 机器数据是**只读文档**：每首歌一份 analysis（timing / segments / boundaries / similar），解析后按 songId 存在 store 的一个独立分区里，和 `events`（人工）物理分开。小节坐标换算是纯函数（`timing.js`），导航/循环只作用于播放器，事件仍由 `SessionRecorder` 这一个地方写。UI 只在听歌页加一条结构行，不做 DAW。

**Tech Stack:** 原生 ES modules、`node --test`、localStorage + IndexedDB（沿用现有）。

**Spec:** 作者 2026-10-13 给的《Attention Annotator 增量改造需求》（15 节）。本次只做 §14 的前 8 步；§14 的"暂时不要做"清单（手机端推理、自动重算 BPM、自动分轨、Basic Pitch、自动和弦、Verse/Chorus 人工分类、复杂 waveform editor）一条都不碰。

## Global Constraints

- 原始事件仍然只记 `audioTime`；`bar` / `beat` 是**派生附加字段**，允许缺。
- **绝不把点击时间吸附到拍点**（§2）。
- 机器结果只能显示，不能改写人工数据（§5、§11）；导出里 `machineObservations` 与 `rawEvents` 分开。
- 现有 session 规则不变：seek / restart 先关掉所有 active interval 再记传输事件（§3）。
- Loop 不制造 IN/OUT，也不结束 session；但要记 `LOOP_JUMP`（§4）。
- 阈值/文件名/字段名一律照 spec 原文；不新增依赖。

---

## File Structure

| 文件 | 责任 |
|---|---|
| `js/timing.js`（新） | 纯坐标换算：audioTime → bar/beat，bar → 时间，段落/边界/相似段的区间查询 |
| `js/analysis.js`（新） | analysis 文档的解析与校验（宽容但报告问题），只读 |
| `js/session.js`（改） | 事件写入：给每个事件附 bar/beat；新增 SECTION_BOUNDARY / BOUNDARY_REVIEW / LOOP_JUMP |
| `js/audio.js`（改） | 播放器加 loop（1/2/4 小节、段落）与 playRange；wrap 时发 `loop` 事件 |
| `js/store.js`（改） | 新增 `analysis` 分区（机器，按 songId）；导出里分出 machineObservations |
| `js/views.js`（改） | 结构行、机器行、边界复核按钮的渲染 |
| `js/app.js`（改） | 接线：结构行刷新、按钮动作、analysis 装载入口 |
| `index.html` / `styles.css`（改） | 结构层的 DOM 与样式 |
| `demo/demo01.analysis.json`（新） | 演示用的 analysis，跟着 `demo/manifest.json` 一起加载 |
| `tests/timing.test.mjs`、`tests/analysis.test.mjs`、`tests/store.test.mjs`（新）；`tests/recorder.test.mjs`（改） | 纯逻辑与事件 schema |

**接口（精确）**

```js
// timing.js
makeTiming(analysis) -> { beatOffset, bpm, beatsPerBar } | null
locate(timing, seconds) -> { bar, beat, beatIndex, beatFrac }        // bar/beat 从 1 起
barStart(timing, bar) -> seconds
barCount(timing, duration) -> integer
prevBarTarget(timing, seconds) -> seconds                            // 本小节开头
nextBarTarget(timing, seconds) -> seconds                            // 下一小节开头
rangeOfBars(timing, fromBar, count) -> { from, to }
segmentAt(analysis, timing, seconds) -> segment | null
segmentRange(analysis, timing, segment) -> { from, to }
boundaryNear(analysis, timing, seconds, windowBars) -> boundary | null
similarOf(analysis, segment) -> [{ segmentId, similarity }]

// analysis.js
parseAnalysis(text) -> { analysis, problems: string[] }              // 永不抛，坏数据返回 null + problems
ANALYSIS_SCHEMA = "fallenangel.attention.analysis/0.1"

// session.js
new SessionRecorder({ store, sessionId, songId, locate })
togglePart(partId, audioTime)
closeAll(reason, audioTime)
transport(eventType, audioTime, extra)
sectionBoundary(audioTime)                     -> SECTION_BOUNDARY
boundaryReview(candidateId, decision, audioTime) -> BOUNDARY_REVIEW（decision: accept|reject|ignore）
loopJump(from, to, audioTime)                  -> LOOP_JUMP（不关 interval）

// audio.js
setLoop({ from, to, label }) / clearLoop()
playRange(from, to, { repeat })
```

---

### Task 1: timing.js

**Files:** Create `js/timing.js`; Test `tests/timing.test.mjs`.

- [ ] Step 1: 写测试：51.284s / offset 0.42 / 128bpm / 4拍 → `bar 17, beat 2.31`；bar 起点与下一小节起点；小节数；段落区间；边界在 ±1 小节内才返回；无 timing 时 `locate` 返回 null（不猜）。
- [ ] Step 2: `node --test tests/timing.test.mjs` → RED。
- [ ] Step 3: 实现。
- [ ] Step 4: 全绿。

### Task 2: analysis.js

**Files:** Create `js/analysis.js`; Test `tests/analysis.test.mjs`.

- [ ] Step 1: 测试：合法文档原样通过；缺 timing 不报错但 `makeTiming` 为 null；`segments`/`boundaries` 类型不对时记进 `problems` 而不是抛；多余字段保留；JSON 坏掉返回 null + problems。
- [ ] Step 2: RED → 实现 → GREEN。

### Task 3: session.js 的三个新事件

**Files:** Modify `js/session.js`; Test `tests/recorder.test.mjs`（沿用其中的 fake store）。

- [ ] Step 1: 测试：`sectionBoundary` 写 `SECTION_BOUNDARY` + `source:"human"` + bar/beat；`boundaryReview` 写 `BOUNDARY_REVIEW` + `candidateId` + `decision`；`loopJump` 写 `LOOP_JUMP` 且**不动 active 集合**；`locate` 存在时每个事件都带 bar/beat，不存在时字段缺省而 `audioTime` 不变。
- [ ] Step 2: RED → 实现 → GREEN。

### Task 4: audio.js loop

**Files:** Modify `js/audio.js`（+ 同文件导出的纯判断 `shouldWrap`）; Test `tests/timing.test.mjs` 里加一组 `shouldWrap` 用例。

- [ ] Step 1: 测试 `shouldWrap(loop, currentTime, {repeat})`：到达 to 返回 true；repeat=false 到达后只穿越一次；无 loop 返回 false。
- [ ] Step 2: RED → 实现（播放器在 timeupdate 里用它，wrap 时 `seekTo(from)` + `emit("loop", …)`）→ GREEN。

### Task 5: store.js analysis 分区 + 导出分层

**Files:** Modify `js/store.js`; Test `tests/store.test.mjs`（用 localStorage stub）。

- [ ] Step 1: 测试：`attachAnalysis`/`getAnalysis` 按 songId 存；`exportSession` 里 `rawEvents` 只含人工事件、`machineObservations` 来自 analysis；`exportEverything` 含 analysis 分区。
- [ ] Step 2: RED → 实现 → GREEN。

### Task 6: UI（结构行 + 机器行 + 这里换段）

**Files:** Modify `index.html`、`styles.css`、`js/views.js`、`js/app.js`。

- [ ] 结构行：`Bar 17 · Beat 2.3`、`Segment C · Bars 9–16`、`‹ Bar`、`Bar ›`、`Loop 1`、`Loop 4`、`Replay seg`。
- [ ] 机器行：`musicnn ↑ 0.81 · Qwen ↑ 0.74`，有候选时给 `✓ / × / ignore`。
- [ ] 底部 footer 里加 `这里换段`。
- [ ] 没有 analysis 时整块隐藏（不显示假坐标），库页提供"Attach analysis JSON"入口。

### Task 7: 演示数据 + 手验

- [ ] `demo/demo01.analysis.json`（bpm 128、offset 0.42、4/4；段落 A/B/C；两处边界候选；一处 similar）。
- [ ] `demo/manifest.json` 里加 `"analysis": "demo01.analysis.json"`。
- [ ] `npm test` 全绿；`npm run serve` 后在浏览器里手验七个动作（见验收）。

**验收（对齐 spec §14 的八步）**
1. 读 timing；2. 显示 Bar/Beat；3. 上一/下一小节；4. Loop 1 与 4 小节；5. 读 segment；6. 显示当前 segment；7. `这里换段` 写出事件；8. 显示机器边界候选并可 ✓/×/ignore。外加：没有 analysis 的歌曲上不出现任何结构 UI。

## Self-Review

- 逐条对照 spec：§2 ✅ Task 1/3/6；§3 ✅ Task 1/6；§4 ✅ Task 4/6；§5/§6 ✅ Task 1/6；§7/§8 ✅ Task 3/6；§9 ✅ Task 4/6（Replay segment = playRange 到段尾）；§10 ✅ Task 1/6；§11 ✅ Task 5；§12 ✅ Task 2；§13 ✅ Task 6；§14 ✅ 顺序即任务序；§15 ✅ 只加坐标与结构事件，不动原有语义。
- 与技能的偏差（同上次）：不把每个测试/实现的完整代码抄进计划——同一轮紧接着实现，抄两遍是浪费；接口签名、文件路径、测试清单写全。
- 已知取舍：tempo map（变速歌）这一版只留接口，不实现（spec 说"或后续更复杂的 tempo map"）；半小节/弱起仍由人工 `这里换段` 与 `‹ Bar` 兜住，不做自动识别。
