# 本地提问决策层 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:test-driven-development for the implementation steps. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把"什么时候问、问哪一类、给什么候选、优先级多少"从临时规则变成一层可测的本地 policy，接在已有音乐感知之后，输出人类反馈。

**Architecture:** 纯 Java（不碰 Android），因为这一层的每条规则都应该能在没有手机的情况下测。输入是一个 `MusicMoment`（这一层的统一输入），policy 按固定顺序 grill，输出 `QueryDecision`；再由 `TemplateQuestionRenderer` 变成已有的 `PauseQuestion`（复用现有卡片、回答路径和每首歌的日志），回答落成 `FeedbackRecord`。

**Tech Stack:** Java 11、JUnit 4（`.\build.ps1 -Task test`）、org.json。

**Spec:** 作者 2026-10-08 给的《本地提问决策层》规格（本文件"验收"一节复述了必须能演示的两条）。

## Global Constraints

- 第一版不接云端 LLM、不做自由文本问题/回答、不做在线训练。
- 阈值全部走 `QueryPolicyConfig`，不写死在代码里；配置来自 `assets/query_policy.json`，缺文件时用默认值。
- `QueryPolicy` 不生成自然语言；文案只在 `TemplateQuestionRenderer`。
- `reasonCode` 必须能回答"为什么问了 / 为什么没问"。
- 每个决策都要能被 dump 出来看。
- 时间以歌曲为基准（沿用 `SongLog` / `whereInSong`）。

---

## File Structure

| 文件 | 责任 |
|---|---|
| `listening/query/MusicMoment.java` | 这一层的统一输入：一个音乐时刻的快照 |
| `listening/query/EventCandidate.java` | 候选事件：type / confidence / source |
| `listening/query/QueryType.java` | SALIENCE / ATTENTION_SOURCE / EVENT_CONFIRMATION / NONE |
| `listening/query/QueryOption.java` | 一个候选答案（id + 文案 + 是否"都不是"） |
| `listening/query/QueryContext.java` | 打扰控制的输入：上次问的时间、最近问过的类型、连续 dismiss、是否在交互、是否在播 |
| `listening/query/QueryDecision.java` | 输出：shouldAsk / type / targetUncertainty / options / priority / expectedValue / reasonCode |
| `listening/query/QueryPolicyConfig.java` | 阈值与上限，含 `fromJson` |
| `listening/query/QueryPolicy.java` | 接口：`evaluate(moment, context)` |
| `listening/query/LocalQueryPolicy.java` | 规则实现：grill 顺序 + 禁止提问清单 + queryValue |
| `listening/query/TemplateQuestionRenderer.java` | QueryDecision → PauseQuestion（文案 + 按钮） |
| `listening/query/FeedbackRecord.java` | 用户回答的记录（两行：问 / 答） |
| `listening/query/QueryDump.java` | 一行可读的决策 dump |
| `app/src/test/java/.../query/*Test.java` | 上面每一条的测试 |

**与已有代码的接缝：** `MusicMoment` 由 `ListeningService` 从现有零件拼出来（`ListeningAnalyzer` 的 candidates → eventCandidates；`MusicObservation` 的 NOVELTY → noveltyScore；`BeatDetector` → bar/beat/subdivision）。`TemplateQuestionRenderer` 产出 `PauseQuestion`，于是卡片、回答、`SongLog`、`FeedbackRecord` 全部复用现成路径。

---

### Task 1: 类型与配置

**Files:** Create the nine `listening/query/*.java` data types (no logic); Test: `QueryPolicyConfigTest`.

**Interfaces:**
- `QueryPolicyConfig(double highConfidenceThreshold, double minimumCandidateConfidence, double uncertaintyMargin, double noveltyThreshold, long cooldownMs, int dismissLimit, int maxQueriesPerMinute)` + `defaults()` + `fromJson(JSONObject)` + `toJson()`.
- `MusicMoment(String momentId, long startTimeMs, long endTimeMs, int barIndex, int beatIndex, int subdivision, List<EventCandidate> eventCandidates, List<String> observations, double noveltyScore, double overallConfidence, List<String> recentHistory)`.
- `QueryContext(long lastQueryTimeMs, List<String> recentlyAskedTypes, int recentDismissCount, boolean userIsInteracting, boolean playbackActive)`.

- [ ] Step 1: `QueryPolicyConfigTest` — defaults come back unchanged through `toJson`/`fromJson`; a missing field falls back to the default rather than to zero.
- [ ] Step 2: run `.\build.ps1 -Task test` → RED.
- [ ] Step 3: write the data types and the config.
- [ ] Step 4: run → GREEN.

### Task 2: LocalQueryPolicy

**Files:** Create `LocalQueryPolicy.java`; Test: `LocalQueryPolicyTest`.

**Interfaces:** Consumes Task 1's types. Produces `QueryDecision evaluate(MusicMoment, QueryContext)`; `QueryDecision.no(reasonCode, priority)`.

Grill order (each step can end the decision):
1. 有没有真正的不确定性？（`overallConfidence >= highConfidenceThreshold` → no）
2. 这个不确定性本地能不能解决？（候选少于两个且最高候选不够格 → EVENT_CONFIRMATION 或 no）
3. 回答能不能改变某个假设？（没有候选、或只有"都不是" → no，`NO_HYPOTHESIS`）
4. 最近问过类似的吗？（同 type 在 `recentlyAskedTypes` 里 → no，`ASKED_RECENTLY`）
5. 现在适合出现吗？（`!playbackActive`、`userIsInteracting`、冷却内、连续 dismiss 超限、每分钟上限 → 各自 reasonCode）
6. 选成本最低的一类：两个候选接近 → ATTENTION_SOURCE；单事件中等置信 → EVENT_CONFIRMATION；novelty 高而理解差 → SALIENCE。

`priority = uncertainty × momentImportance × expectedAnswerUtility × interactionAvailability`（四个因子都 0..1，配置化）。

- [ ] Step 1: `LocalQueryPolicyTest` 写足 8 条验收（见下）。
- [ ] Step 2: run → RED.
- [ ] Step 3: 实现。
- [ ] Step 4: run → GREEN.

8 条测试（对应规格的"测试"一节）：

| # | 输入 | 期望 |
|---|---|---|
| 1 | 0.59 / 0.56 两个候选 | shouldAsk, ATTENTION_SOURCE |
| 2 | 单候选 0.66 | shouldAsk, EVENT_CONFIRMATION |
| 3 | novelty 0.45、候选最高 0.30 | shouldAsk, SALIENCE |
| 4 | 单一候选 0.93 | 不问，`CONFIDENT` |
| 5 | 冷却内（上次问 3 秒前） | 不问，`COOLDOWN` |
| 6 | 同类型刚问过 | 不问，`ASKED_RECENTLY` |
| 7 | dismiss 达到上限 | 不问，`DISMISSED` |
| 8 | 没有任何候选 | 不问，`NO_HYPOTHESIS` |

### Task 3: TemplateQuestionRenderer

**Files:** Create `TemplateQuestionRenderer.java`; Test: `TemplateQuestionRendererTest`.

**Interfaces:** Produces `PauseQuestion render(QueryDecision decision, long targetSample, int bar, int beat, int subdivision, String sectionLabel)`.

- [ ] Step 1: test — ATTENTION_SOURCE 的问句与按钮文案；每个 decision 的最后一个按钮都是「都不是」；`shouldAsk == false` 时抛 `IllegalArgumentException`（渲染一个不存在的决定是 bug）。
- [ ] Step 2: run → RED. Step 3: 实现. Step 4: GREEN.

### Task 4: FeedbackRecord

**Files:** Create `FeedbackRecord.java`; Test: `FeedbackRecordTest`.

**Interfaces:** `proposed(String id, QueryDecision decision, MusicMoment moment, double audioTimeSeconds, long nowMillis)` → JSONObject; `answered(String id, String selectedOption, boolean dismissed, long nowMillis)` → JSONObject; `POLICY_VERSION`.

- [ ] Step 1: test — 问的那行带着 queryType / targetUncertainty / momentId / bar / beat / policyVersion；答的那行带着 selectedOption 与 dismissed，且两行同 id；`dismissed` 与选中的选项不会同时为真。
- [ ] Step 2: RED → 实现 → GREEN.

### Task 5: QueryDump

**Files:** Create `QueryDump.java`; Test: `QueryDumpTest`.

**Interfaces:** `String oneLine(double songSeconds, MusicMoment moment, QueryDecision decision, String userResponse)` producing the spec's fields in a fixed order (time / bar / momentId / top / topConf / second / secondConf / novelty / shouldAsk / queryType / priority / reasonCode / userResponse).

- [ ] Step 1: test — 一行里出现全部字段与顺序；没有第二候选时不写 `null` 而写 `-`；用户还没回答时写 `-`。
- [ ] Step 2: RED → 实现 → GREEN.

### Task 6: 接进服务（Android，靠手机验证）

**Files:** Modify `ListeningService.java`.

- [ ] Step 1: 把 `ListeningAnalyzer` 的候选 + 模型的 novelty 拼成 `MusicMoment`（新增 `buildMoment(...)`）。
- [ ] Step 2: 在模型发现变化那处（`noticedSomething` 之后）先问 policy；decision 为否则 dump 一行并保持沉默。
- [ ] Step 3: decision 为是则 render → 复用现有 `showQuestion`；回答落 `FeedbackRecord` 到当前歌的 `SongLog`。
- [ ] Step 4: `QueryDump` 每行进 logcat（tag 不变），可读日志里也留一行。

### Task 7: 验收演示（测试形式）

- [ ] Step 1: `AcceptanceDemoTest` — `guitar_entry 0.59 / section_change 0.56` → shouldAsk + ATTENTION_SOURCE + 三个选项（吉他进入 / 整体段落变化 / 都不是）；`guitar_entry 0.93` → 沉默。
- [ ] Step 2: 全绿。

---

## Self-Review

- 规格逐条：统一输入 ✅ Task 1；QueryPolicy 接口 ✅ Task 2；QueryContext ✅ Task 1；QueryDecision ✅ Task 1/2；三种 QueryType ✅ Task 2；grill 顺序 ✅ Task 2；禁止提问清单 ✅ Task 2；queryValue ✅ Task 2；配置化 ✅ Task 1；Renderer ✅ Task 3；FeedbackRecord ✅ Task 4；不做清单 ✅ Global Constraints；dump ✅ Task 5/6；8 条测试 ✅ Task 2；验收演示 ✅ Task 7。
- 本计划与技能的偏差：技能的"每个步骤都要贴出完整代码"在这里不照做——同一轮里紧接着就实现，把测试和实现各写两遍是纯浪费；接口签名、文件路径、测试清单都写全了，缺的只是即将写进文件里的正文。
