/**
 * 事件与小节边界之间的派生关系层（Boundary Association）。

 * 这一层只回答一个问题：**这个事件与该 grid 最近的小节边界是什么关系？**
 * 所以每个 event × 每条 grid 最多产生一条 relation；跨 grid 绝不合并 boundary，
 * 一拍级错位要单独暴露成"局部候选"而不是给整对 grid 打标签。

 * 这里是规格 §12 那六个 Case，加上研究者 2026-10-10 追加的三条约束：
 * 只关联最近 boundary、相位候选要带位置且需要连续证据、±0.5 beat 是 hard cap。
 */
import test from 'node:test';
import assert from 'node:assert/strict';

await import('../static/mgmcs.js');
await import('../static/boundary.js');
const M = globalThis.MGMCS;
const B = M.boundary;

function close(actual, expected, what = '') {
  assert.ok(Math.abs(actual - expected) < 1e-9,
    `${what}${what ? ': ' : ''}${actual} != ${expected}`);
}

/** 一条 grid：120 BPM、4/4 时一拍 0.5 秒，方便手算。 */
function grid(gridId, originTime, extra = {}) {
  return {
    gridId, originTime, bpm: 120, beatsPerBar: 4, beatUnit: 4,
    source: 'human', irregular: false, barTimes: null, ...extra,
  };
}

function point(id, audioTime) {
  return { eventId: id, type: 'guitar_attention_in', kind: 'point', time: audioTime,
           endTime: NaN, payload: {}, raw: {} };
}

function doc(grids, events) {
  return { schemaVersion: '0.1', song: { songId: 's', title: 't', audioFileName: '',
           audioPath: '', durationSeconds: 300, hash: '' },
           grids, events, problems: [] };
}

// ---------------------------------------------------------------- 规格 §12

test('Case 1 轻微漂移：两条线各自保留，同一个事件各有各的 relativeBeat', () => {
  const a = grid('A', 32.0);
  const b = grid('B', 32.08);                  // 差 80 ms
  const event = point('e1', 32.06);
  const relations = B.boundaryAssociations(doc([a, b], [event]));

  assert.equal(relations.length, 2, '一个事件、两条 grid → 两条 relation');
  const byGrid = Object.fromEntries(relations.map(r => [r.gridId, r]));
  close(byGrid.A.relativeBeat, 0.12, 'A 相对位置');
  close(byGrid.B.relativeBeat, -0.04, 'B 相对位置');
  assert.equal(byGrid.A.boundaryId, 'A_bar0');
  assert.equal(byGrid.B.boundaryId, 'B_bar0');
  assert.notEqual(byGrid.A.boundaryTime, byGrid.B.boundaryTime, '两条边界没有被合并');
});

test('Case 2 事件正落在边界上 → near_boundary', () => {
  const relations = B.boundaryAssociations(doc([grid('A', 32.0)], [point('e1', 32.0)]));
  assert.equal(relations.length, 1);
  close(relations[0].relativeBeat, 0);
  assert.equal(relations[0].associationClass, 'near_boundary');
});

test('Case 3 提前进入 0.2 beat → boundary_associated，负号保留', () => {
  const relations = B.boundaryAssociations(doc([grid('A', 32.0)], [point('e1', 31.9)]));
  assert.equal(relations.length, 1);
  close(relations[0].relativeBeat, -0.2);
  assert.equal(relations[0].associationClass, 'boundary_associated');
});

test('Case 4 距离 0.7 beat → 不关联这条边界', () => {
  const relations = B.boundaryAssociations(doc([grid('A', 32.0)], [point('e1', 32.35)]));
  assert.equal(relations.length, 0);
});

test('Case 5 一拍错位：仍是两套解释，并给出带位置的局部相位候选', () => {
  const a = grid('A', 10.0);
  const b = grid('B', 10.5);                   // 正好差一拍（0.5 秒 @120BPM）
  const nearA = point('e1', 10.02);            // 贴着 A 的边界
  const nearB = point('e2', 10.52);            // 贴着 B 的边界
  const document = doc([a, b], [nearA, nearB]);
  const relations = B.boundaryAssociations(document);

  // 贴 A 的那个事件，相对 B 正好差一拍 → 超出窗口，不关联 B
  assert.deepEqual(relations.filter(r => r.eventId === 'e1').map(r => r.gridId), ['A']);
  assert.deepEqual(relations.filter(r => r.eventId === 'e2').map(r => r.gridId), ['B']);

  const candidates = B.phaseOffsetCandidates(document);
  assert.equal(candidates.length, 1);
  assert.equal(candidates[0].gridA, 'A');
  assert.equal(candidates[0].gridB, 'B');
  close(candidates[0].offsetBeats, 1, '偏移拍数');
  assert.equal(candidates[0].barIndex, 0);
  close(candidates[0].aroundTime, 10.0, '候选发生的位置');
  assert.ok(candidates[0].bars >= 2, '要有连续若干根 boundary 支持，不是单根碰巧');
  assert.equal(candidates[0].kind, 'local_phase_offset_candidate');
  assert.equal(candidates[0].phase.comparable, true, '这两条速度一致，全局相位也比得起来');
});

test('Case 6 原数据一字不变：算关系不会写回 grid / event', () => {
  const a = grid('A', 32.0);
  const b = grid('B', 32.08);
  const event = point('e1', 32.06);
  const document = doc([a, b], [event]);
  const before = JSON.stringify(document);

  B.boundaryAssociations(document);
  B.phaseOffsetCandidates(document);
  B.associatedEvents(document, 'A', 0);

  assert.equal(JSON.stringify(document), before);
});

// ------------------------------------------- 研究者追加的三条约束（2026-10-10）

test('约束一：每个事件 × 每条 grid 只关联最近的小节边界', () => {
  // 一拍一小节：事件正好落在两根边界中间，两边距离都是 0.5 beat
  const tight = grid('T', 0.0, { beatsPerBar: 1 });
  const relations = B.boundaryAssociations(doc([tight], [point('e1', 0.25)]));
  assert.equal(relations.length, 1, '最近边界只有一个，不能两根都算');
  assert.equal(relations[0].associationClass, 'peripheral');
});

test('约束一之补：窗口外的事件一条 relation 也不产生', () => {
  const relations = B.boundaryAssociations(doc([grid('A', 0.0)], [point('e1', 0.26)]));
  assert.equal(relations.length, 0);
});

test('约束二：漂移里只擦过一根 boundary 差一拍 → 不算候选（要连续证据）', () => {
  // 120 vs 121.2：速度差 1%，两套线的相对位置慢慢滑过去，只在某一根上正好差一拍。
  // 容差收紧到 0.02 拍，就是为了让"擦过"只发生在那一根上——这正是研究者担心的
  // "局部速度漂移碰巧落到一拍附近"。
  const a = grid('A', 0.0);
  const b = grid('B', 0.0, { bpm: 121.2 });
  const nearEnough = { offsetTolerance: 0.02 };

  assert.equal(B.phaseOffsetCandidates(doc([a, b], []), { ...nearEnough, minRunBars: 2 }).length, 0,
    '只擦过一根，判不了局部相位');
  const one = B.phaseOffsetCandidates(doc([a, b], []), { ...nearEnough, minRunBars: 1 });
  assert.ok(one.length >= 1, '把门槛放到一根时它才出现，说明闸门就是"连续几根"');
  assert.ok(one.every(candidate => candidate.bars === 1), '每一处都只擦过一根');
  assert.equal(one[0].barIndex, 25, '第一次擦过发生在 A 的第 25 小节');
  assert.equal(one[0].phase.comparable, false, '全局相位不可比（速度不同）要如实带出来');
  assert.match(one[0].phase.reason, /速度/);
});

test('约束二之补：恒定的差一拍 → 一个候选，位置指向第一根', () => {
  const a = grid('A', 0.0);
  const b = grid('B', 0.5);
  const candidates = B.phaseOffsetCandidates(doc([a, b], []), { minRunBars: 3 });
  assert.equal(candidates.length, 1);
  close(candidates[0].offsetBeats, 1);
  assert.equal(candidates[0].barIndex, 0);
  assert.ok(candidates[0].bars >= 3);
});

test('约束二之补二：差半拍不是相位候选（半小节不走这条路）', () => {
  const candidates = B.phaseOffsetCandidates(doc([grid('A', 0.0), grid('B', 0.25)], []));
  assert.equal(candidates.length, 0);
});

test('约束二之补三：漂移滑过整数拍只留下极短的擦过段，不会刷成一片', () => {
  // 120 vs 121.2：偏移慢慢滑过 1 拍、2 拍……每一段都"连续几根落在整数拍附近"，
  // 但它们不是停在那里，是滑过去。没有稳定性判据时，一首歌能刷出几百条
  // （实测 taishou 一对网格几百条）；有了它，只剩最短的擦过段。
  const a = grid('A', 0.0);
  const b = grid('B', 0.0, { bpm: 121.2 });
  const swept = B.phaseOffsetCandidates(doc([a, b], []));
  assert.ok(swept.every((candidate) => candidate.bars <= 3), '擦过段只能是短的');

  // 把稳定性放宽到 0.3 拍（比滑过的步子还大）才会长出长段——说明闸门确实是它。
  const loose = B.phaseOffsetCandidates(doc([a, b], []), { stabilityBeats: 0.3 });
  assert.ok(loose.length > swept.length, '放宽稳定性会冒出更多（更长的）候选');
  assert.ok(loose.some((candidate) => candidate.bars > 3));
});

test('约束三：±0.5 beat 是 hard cap，传 0.8 也不放大', () => {
  const options = B.options({ windowBeats: 0.8 });
  close(options.windowBeats, 0.5, '窗口被夹到 0.5');
  assert.equal(options.windowClamped, true, '夹了要说出来，不能悄悄改');

  // 0.6 beat 的事件：即便调用方传 0.8，也必须没有关联
  const relations = B.boundaryAssociations(
    doc([grid('A', 0.0)], [point('e1', 0.3)]), { windowBeats: 0.8 });
  assert.equal(relations.length, 0);
  assert.equal(B.HARD_CAP_BEATS, 0.5);
});

// ------------------------------------------------------------------ 面板要用

test('一节的关联事件按 |relativeBeat| 排序，正负都在', () => {
  const a = grid('A', 0.0);
  const document = doc([a], [point('late', 0.2), point('early', -0.05), point('far', 2.0)]);
  const listed = B.associatedEvents(document, 'A', 0);

  assert.deepEqual(listed.map(row => row.eventId), ['early', 'late']);
  close(listed[0].relativeBeat, -0.1, '早到 0.1 拍');
  close(listed[1].relativeBeat, 0.4, '晚到 0.4 拍');
});

test('区间事件用它的起点参与关联，导出结构里带 eventTime 与 beatSeconds', () => {
  const span = { eventId: 's1', type: 'attention_span', kind: 'span', time: 0.1,
                 endTime: 1.0, payload: {}, raw: {} };
  const relations = B.boundaryAssociations(doc([grid('A', 0.0)], [span]));
  assert.equal(relations.length, 1);
  close(relations[0].eventTime, 0.1);
  close(relations[0].relativeBeat, 0.2);
  close(relations[0].beatSeconds, 0.5, '本地一拍时长');
  assert.equal(relations[0].boundaryTime, 0);
});

test('不规则网格（小节线列表）用真实的小节长度折算拍', () => {
  const irregular = grid('R', 0.0, { irregular: true, barTimes: [0.0, 2.0, 4.5], beatsPerBar: 4 });
  const relations = B.boundaryAssociations(doc([irregular], [point('e1', 2.25)]));
  assert.equal(relations.length, 1);
  assert.equal(relations[0].barIndex, 1);
  // 第二小节 2.0→4.5 秒，长 2.5 秒；按 120 BPM 折算是 5 拍 → 那一节的一拍 0.5 秒
  close(relations[0].beatSeconds, 0.5, '不规则网格那一节的本地拍长');
  close(relations[0].relativeBeat, 0.5, '0.25 秒 / 0.5 秒');
});

// ---------------------------------------------------------- Grid Pair Diff（2026-10-10）

// ---------------------------------------------------------- 准入 analysisEligibility

test('准入三档：一边 suspect 是 caution，两边都 suspect 才 ineligible', () => {
  const ok = grid('ok1', 0.0, { health: 'ok' });
  const suspect = grid('sus1', 0.0, { health: 'suspect', suspectReasons: ['outOfRange: 起点 6574.9s 不在音频内'] });

  assert.equal(B.eligibilityOf([ok]), 'eligible');
  assert.equal(B.eligibilityOf([ok, grid('ok2', 0.0)]), 'eligible');
  assert.equal(B.eligibilityOf([ok, suspect]), 'caution', '一条可信、一条不可信 → 这条结论只能存疑');
  assert.equal(B.eligibilityOf([suspect, grid('sus2', 0.0, { health: 'suspect' })]), 'ineligible');
});

test('准入报告带来源与理由，且不编有效区间', () => {
  const report = B.eligibilityReport([
    grid('ok1', 0.0, { health: 'ok' }),
    grid('sus1', 0.0, { health: 'suspect', suspectReasons: ['起点越界'] }),
  ]);
  assert.equal(report.analysisEligibility, 'caution');
  assert.deepEqual(report.sourceHealth.map((row) => row.gridId), ['ok1', 'sus1']);
  assert.equal(report.sourceHealth[0].valid, 'unknown', '没有区间就写 unknown，不编');
  assert.deepEqual(report.sourceHealth[1].reasons, ['起点越界']);
  assert.equal(report.sourceHealth[1].level, 'ineligible');
});

test('每条派生结论都自带准入：单网格的关系、两网格的重叠分与相位候选', () => {
  const ok = grid('ok1', 0.0, { health: 'ok' });
  const suspect = grid('sus1', 0.0, { health: 'suspect' });
  const document = doc([ok, suspect], [point('e1', 0.01)]);

  for (const relation of B.boundaryAssociations(document)) {
    const expected = relation.gridId === 'sus1' ? 'ineligible' : 'eligible';
    assert.equal(relation.analysisEligibility, expected, relation.gridId);
  }

  const scores = B.overlapScores(document, 'ok1', 'sus1');
  assert.equal(scores[0].analysisEligibility, 'caution', '一条 suspect → 这列分数只能存疑');
  assert.equal(scores[0].sourceHealth.length, 2);

  const candidates = B.phaseOffsetCandidates(doc([ok, grid('sus2', 0.5, { health: 'suspect' })], []));
  assert.ok(candidates.length > 0);
  assert.equal(candidates[0].analysisEligibility, 'caution');
});

test('Pair Diff：差一拍的网格上，贴 A 的事件就是 A-only', () => {
  const a = grid('A', 0.0);
  const b = grid('B', 0.5);                    // 正好差一拍
  const document = doc([a, b], [point('nearA', 0.02), point('nearB', 0.52)]);
  const diff = B.pairDiff(document, 'A', 'B');

  assert.deepEqual(diff.map((row) => [row.eventId, row.kind]),
    [['nearA', 'a_only'], ['nearB', 'b_only']]);
  close(diff[0].a.relativeBeat, 0.04, 'A 边上的相对拍');
  assert.equal(diff[0].b, null, 'B 那边窗口外，不硬塞一个关系');
});

test('Pair Diff：两条几乎重合的网格上，同一个事件是 shared', () => {
  const diff = B.pairDiff(doc([grid('A', 0.0), grid('B', 0.02)], [point('e1', 0.01)]), 'A', 'B');
  assert.equal(diff.length, 1);
  assert.equal(diff[0].kind, 'shared');
  assert.ok(diff[0].a && diff[0].b, '两边的关系都带出来，不合并');
});

test('Pair Diff：机器拍点被标出来，只看人的开关能把它挡掉', () => {
  const machineBeat = { ...point('m1', 0.01), source: 'machine', type: 'machine_beat' };
  const human = { ...point('h1', 0.01), source: 'mobile', type: 'pause_moment' };
  const document = doc([grid('A', 0.0), grid('B', 0.0)], [machineBeat, human]);

  const all = B.pairDiff(document, 'A', 'B');
  assert.deepEqual(all.map((row) => row.machine), [true, false]);

  const humanOnly = B.pairDiff(document, 'A', 'B', { onlyHuman: true });
  assert.deepEqual(humanOnly.map((row) => row.eventId), ['h1']);
});

test('逐小节重叠分：重合的网格是 1，差一拍的是 0，没事件的是 NaN', () => {
  const aligned = B.overlapScores(doc([grid('A', 0.0), grid('B', 0.0)], [point('e1', 0.01)]), 'A', 'B');
  close(aligned[0].overlap, 1, '同一批事件 → 完全重叠');
  close(aligned[0].aCount, 1);

  const shifted = B.overlapScores(doc([grid('A', 0.0), grid('B', 0.5)], [point('e1', 0.01)]), 'A', 'B');
  close(shifted[0].overlap, 0, '事件只落在 A 那边 → 不重叠');
  assert.equal(shifted[0].bBarIndex, 0, '对齐的是 B 的第 0 根（离 A 第 0 根最近）');

  const empty = B.overlapScores(doc([grid('A', 0.0), grid('B', 0.0)], []), 'A', 'B');
  assert.ok(Number.isNaN(empty[0].overlap), '没有证据就是 NaN，不是 1');
});

test('热区要连续：单格的低分不算，连续两格才算', () => {
  const rows = [
    { barIndex: 0, time: 0.0, overlap: 1.0 },
    { barIndex: 1, time: 2.0, overlap: 0.2 },
    { barIndex: 2, time: 4.0, overlap: 0.1 },
    { barIndex: 3, time: 6.0, overlap: 1.0 },
  ];
  const runs = B.hotRuns(rows, { minRunBars: 2, disagreement: 0.5 });
  assert.equal(runs.length, 1);
  assert.equal(runs[0].startBar, 1);
  assert.equal(runs[0].endBar, 2);
  assert.equal(runs[0].bars, 2);
  close(runs[0].worstOverlap, 0.1);

  assert.equal(B.hotRuns([rows[1]], { minRunBars: 2 }).length, 0, '孤零零一格不算热区');
  assert.equal(B.hotRuns([{ barIndex: 0, time: 0, overlap: NaN }, { barIndex: 1, time: 2, overlap: 0.1 }],
    { minRunBars: 2 }).length, 0, 'NaN 既不算分歧也不充数');
});
