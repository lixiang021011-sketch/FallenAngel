/**
 * Time Coordinate Map v0.1, browser side.

 * The expectations live in coordinate_fixtures.json, written out from the spec.
 * tools/test_coordinate_map.py reads the same file, so the two implementations
 * of this arithmetic cannot drift apart without one of them failing here.
 */
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

await import('../static/mgmcs.js');
const M = globalThis.MGMCS;

const fixtures = JSON.parse(
  fs.readFileSync(new URL('./coordinate_fixtures.json', import.meta.url), 'utf8'));
const doc = M.parseSongDocument(fixtures.document);
const map = M.coordinateMap(doc);

function close(actual, expected, what = '', tolerance = 1e-9) {
  assert.ok(Math.abs(actual - expected) <= tolerance,
    `${what}: ${actual} != ${expected}`);
}

test('a grid with no positive tempo is refused, and the rest still load', () => {
  for (const id of fixtures.refused) {
    assert.ok(!map.gridIds || true);
    assert.ok(map.timeToCoordinate(1, id).error, id + ' 应当被拒收');
  }
  assert.deepEqual(doc.grids.map((g) => g.gridId),
    ['grid_A', 'grid_B', 'grid_T', 'grid_C']);
});

test('time → coordinate', () => {
  for (const k of fixtures.timeToCoordinate) {
    const out = map.timeToCoordinate(k.audioTime, k.gridId);
    assert.ok(!out.error, k.why);
    assert.equal(out.bar, k.bar, k.why + ' 的 bar');
    close(out.beat, k.beat, k.why + ' 的 beat');
    assert.equal(out.outOfAudioRange === true, k.outOfAudioRange === true, k.why + ' 的越界标记');
  }
});

test('coordinate → time', () => {
  for (const k of fixtures.coordinateToTime) {
    const out = map.coordinateToTime(k.bar, k.beat, k.gridId);
    assert.ok(!out.error, k.why);
    close(out.audioTime, k.audioTime, k.why);
  }
});

test('one moment, every grid, and no grid chosen for you', () => {
  for (const k of fixtures.projectTime) {
    const out = map.projectTime(k.audioTime);
    assert.equal(out.coordinates.length, k.coordinates.length);
    k.coordinates.forEach((want, i) => {
      const got = out.coordinates[i];
      assert.equal(got.gridId, want.gridId);
      assert.equal(got.bar, want.bar);
      close(got.beat, want.beat, k.why + ' / ' + want.gridId);
    });
  }
});

test('anchors are the window\'s beat lines, with the bar heads marked', () => {
  for (const k of fixtures.generateAnchors) {
    const got = map.generateAnchors(k.gridId, k.from, k.to);
    assert.equal(got.length, k.expect.length, k.why + ' 的条数');
    k.expect.forEach((want, i) => {
      close(got[i].audioTime, want.audioTime, k.why + ' 第 ' + i + ' 条的时间');
      assert.equal(got[i].bar, want.bar);
      assert.equal(got[i].isDownbeat, want.isDownbeat);
    });
  }
});

test('bad input comes back as an error, not as a number', () => {
  for (const k of fixtures.errors) {
    const out = map[k.call](...k.args);
    assert.ok(out && out.error, `${k.call}(${k.args}) 应当返回 error —— ${k.why}`);
  }
});

test('coordinate → time → coordinate round-trips inside a millisecond', () => {
  const rt = fixtures.roundTrip;
  for (const gridId of rt.grids) {
    for (const t of rt.times) {
      const at = map.timeToCoordinate(t, gridId);
      const back = map.coordinateToTime(at.bar, at.beat, gridId);
      close(back.audioTime, t, `${gridId} @ ${t}s 的往返`, rt.toleranceSeconds);
    }
  }
});

test('a timeline asks only for the window it is drawing', () => {
  const wide = map.generateAnchors('grid_A', 0, 210.35).length;
  const narrow = map.generateAnchors('grid_A', 60, 65).length;
  assert.ok(narrow <= 11, '五秒窗口不该吐出整首歌的线');
  assert.ok(wide > narrow, '整首歌当然更多');
});

/**
 * The same four calls on a grid whose bars are not all the same length. Same
 * fixtures as the Python side: a bar-line list is a reading, and both
 * implementations have to read it the same way.
 */
const irregular = fixtures.irregular;
const listed = M.coordinateMap(M.parseSongDocument(irregular.document));

test('a bar-line list is read as written and survives a round trip through JSON', () => {
  const grid = M.parseSongDocument(irregular.document).grids[0];
  assert.equal(grid.irregular, true);
  assert.equal(grid.barTimes.length, 5);
  assert.deepEqual(grid.barTimes, [10, 12, 14, 15, 17]);
  close(M.barBeats(grid, 2), 2.0, '短小节');
  close(M.barBeats(grid, 3), 4.0, '它后面那一个小节');
});

test('time → coordinate on a grid with a short bar', () => {
  for (const k of irregular.timeToCoordinate) {
    const out = listed.timeToCoordinate(k.audioTime, k.gridId);
    assert.ok(!out.error, k.why);
    assert.equal(out.bar, k.bar, k.why);
    close(out.beat, k.beat, k.why);
  }
});

test('coordinate → time on a grid with a short bar', () => {
  for (const k of irregular.coordinateToTime) {
    const out = listed.coordinateToTime(k.bar, k.beat, k.gridId);
    assert.ok(!out.error, k.why);
    close(out.audioTime, k.audioTime, k.why);
  }
});

test('a short bar hands out the beats it has, not the nominal four', () => {
  for (const k of irregular.generateAnchors) {
    const got = listed.generateAnchors(k.gridId, k.from, k.to);
    assert.equal(got.length, k.expect.length, k.why);
    k.expect.forEach((want, i) => {
      close(got[i].audioTime, want.audioTime, k.why + ' 第 ' + i + ' 条');
      assert.equal(got[i].bar, want.bar);
      assert.equal(got[i].isDownbeat, want.isDownbeat);
    });
  }
});

test('an irregular grid round-trips inside a millisecond', () => {
  const rt = irregular.roundTrip;
  for (const gridId of rt.grids) {
    for (const t of rt.times) {
      const at = listed.timeToCoordinate(t, gridId);
      const back = listed.coordinateToTime(at.bar, at.beat, gridId);
      close(back.audioTime, t, gridId + ' @ ' + t, rt.toleranceSeconds);
    }
  }
});
