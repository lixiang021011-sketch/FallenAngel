/**
 * The arithmetic the whole validator rests on.

 * Every number asserted here is one that appears in the spec, so a change that
 * breaks the reading of the spec breaks a test rather than a researcher's
 * afternoon: the two projections in section 5, the bar-jump in the click
 * supplement, and the half-bar phase offset in section 11.
 */
import test from 'node:test';
import assert from 'node:assert/strict';

// The page loads this as a plain script (so the tool runs from file://, with no
// server and nothing installed); a test runs the same file for the global it sets.
await import('../static/mgmcs.js');
const M = globalThis.MGMCS;

/** Audio times are seconds; comparing them exactly is how you get 2.1439999. */
function close(actual, expected, what = '') {
  assert.ok(Math.abs(actual - expected) < 1e-9,
    `${what}${what ? ': ' : ''}${actual} != ${expected}`);
}

function grid(extra = {}) {
  return {
    gridId: 'cal_001',
    originTime: 31.428,
    bpm: 120,
    beatsPerBar: 4,
    beatUnit: 4,
    source: 'human',
    ...extra,
  };
}

test('120 BPM means half a second a beat, and two seconds a 4/4 bar', () => {
  close(M.timeOf(grid(), 0, 1) - M.timeOf(grid(), 0, 0), 0.5, 'one beat');
  close(M.timeOf(grid(), 0, 4) - M.timeOf(grid(), 0, 0), 2.0, 'one bar');
});

test('the calibration point is bar 0 beat 0, exactly', () => {
  const at = M.coordinateAt(grid(), 31.428);
  assert.equal(at.bar, 0);
  close(at.beat, 0, 'beat');
});

test('section 5: 72.5 s on the 4/4 grid is bar 20 beat 2.144', () => {
  const at = M.coordinateAt(grid(), 72.5);
  assert.equal(at.bar, 20);
  close(at.beat, 2.144, 'beat');
});

test('section 5: the same moment on the 3/4 grid is bar 6 beat 0.578', () => {
  const at = M.coordinateAt(grid({ gridId: 'cal_002', originTime: 63.211, beatsPerBar: 3 }), 72.5);
  assert.equal(at.bar, 6);
  close(at.beat, 0.578, 'beat');
});

test('before the calibration the bars are negative and the beat still counts up', () => {
  // (0 - 31.428) x 2 = -62.856 beats; floor(-62.856 / 4) = -16, and -62.856 + 64 = 1.144.
  const at = M.coordinateAt(grid(), 0);
  assert.equal(at.bar, -16);
  close(at.beat, 1.144, 'beat');
});

test('a fraction of a beat is a fraction, not a snap', () => {
  const at = M.coordinateAt(grid(), 31.428 + 0.125);
  assert.equal(at.bar, 0);
  close(at.beat, 0.25, 'beat');
});

test('the click supplement: bar -2 of a 120 BPM 4/4 grid from 30 s is 26 s', () => {
  close(M.timeOf(grid({ originTime: 30 }), -2), 26.0, 'target time');
});

test('a beat inside a bar is reachable, and bar 3 beat 2 is bar 3 plus two beats', () => {
  const g = grid({ originTime: 30 });
  close(M.timeOf(g, 3, 2), M.timeOf(g, 3, 0) + 1.0, 'bar 3 beat 2');
});

test('reading back a computed coordinate returns the time it came from', () => {
  const g = grid({ originTime: 12.5 });
  for (const t of [0, 5.25, 12.5, 61.428, 200.001]) {
    const at = M.coordinateAt(g, t);
    close(M.timeOf(g, at.bar, at.beat), t, `round trip at ${t}`);
  }
});

test('section 11: origins a second apart at 120 BPM are 2 beats, half a bar', () => {
  const a = grid({ gridId: 'A', originTime: 10.0 });
  const b = grid({ gridId: 'B', originTime: 11.0 });
  const offset = M.phaseOffset(a, b);
  assert.equal(offset.comparable, true);
  close(offset.deltaTime, 1.0, 'Δtime');
  close(offset.deltaBeat, 2.0, 'Δbeat');
  close(offset.deltaBar, 0.5, 'Δbar');
});

test('section 12: 4/4 and 3/4 are not converted into each other', () => {
  const four = grid({ gridId: 'A', beatsPerBar: 4 });
  const three = grid({ gridId: 'B', beatsPerBar: 3 });
  assert.equal(M.phaseOffset(four, three).comparable, false);
});

test('a clearly different tempo is not a phase offset', () => {
  const a = grid({ gridId: 'A', bpm: 120 });
  const b = grid({ gridId: 'B', bpm: 160 });
  assert.equal(M.phaseOffset(a, b).comparable, false);
});

test('tapping the same song twice counts as the same tempo', () => {
  // Measured: four attempts on one song gave 106.2, 106.8, 107.0 and 105.8 BPM.
  // A rule demanding equality would refuse to compare the pair the researcher
  // most wants compared.
  const a = grid({ gridId: 'A', bpm: 106.195, originTime: 16.981 });
  const b = grid({ gridId: 'B', bpm: 106.762, originTime: 102.688 });
  const offset = M.phaseOffset(a, b);
  assert.equal(offset.comparable, true);
  assert.ok(Math.abs(offset.deltaBpm - 0.567) < 1e-9);
  // And the difference is reported, not hidden: the two drift apart.
  assert.ok(Math.abs(offset.driftSecondsPerMinute - 0.32) < 0.01);
  close(offset.deltaTime, 85.707, 'Δtime');
});

test('the same spec at the same origin is no offset', () => {
  const offset = M.phaseOffset(grid({ gridId: 'A' }), grid({ gridId: 'B' }));
  assert.equal(offset.comparable, true);
  close(offset.deltaBeat, 0, 'Δbeat');
});

test('times read the way the spec writes them, to the millisecond', () => {
  assert.equal(M.formatClock(0), '00:00.000');
  assert.equal(M.formatClock(83.427), '01:23.427');
  assert.equal(M.formatClock(226.12), '03:46.120');
});

test('beats read to two places, the way the spec writes them', () => {
  assert.equal(M.formatBeat(2.144), '2.14');
  assert.equal(M.formatBeat(0.578), '0.58');
  assert.equal(M.formatBeat(0), '0.00');
});

test('section 3: the shipped example document parses into grids and events', () => {
  const doc = M.parseSongDocument(JSON.parse(EXAMPLE));
  assert.deepEqual(doc.problems, []);
  assert.equal(doc.grids.length, 2);
  assert.equal(doc.grids[0].gridId, 'cal_001');
  assert.equal(doc.grids[0].beatsPerBar, 4);
  assert.equal(doc.grids[1].beatsPerBar, 3);
  assert.equal(doc.events.length, 1);
  assert.equal(doc.events[0].type, 'guitar_attention_in');
  assert.equal(doc.events[0].kind, 'point');
  close(doc.events[0].time, 48.225, 'event time');
});

test('a document with no grids is refused with a reason rather than drawn empty', () => {
  const doc = M.parseSongDocument({ song: { songId: 'x' } });
  assert.equal(doc.grids.length, 0);
  assert.ok(doc.problems.length > 0);
});

test('a calibration without a tempo is a problem, not a grid at zero BPM', () => {
  const doc = M.parseSongDocument({ song: {}, calibrations: [{ calibrationId: 'c1', audioTime: 1 }] });
  assert.equal(doc.grids.length, 0);
  assert.match(doc.problems.join(' '), /bpm/);
});

test('the original audio time is never rewritten', () => {
  const doc = M.parseSongDocument(JSON.parse(EXAMPLE));
  assert.equal(doc.events[0].time, 48.225);
  assert.equal(doc.events[0].raw.audioTime, 48.225);
});

// ------------------------------------------------------------- half a bar
//
// Half-Bar Detection Methodology v0.1: two grids, same meter, nearly the same
// tempo, and their origins about half a bar apart. Checked against the worked
// example in the spec, and against the cases where it has to keep quiet.

test('half bar: the example in the spec comes out as specified', () => {
  const a = grid({ gridId: 'A', originTime: 10.0 });
  const b = grid({ gridId: 'B', originTime: 17.0 });   // 7 s at 120 BPM = 14 beats
  const got = M.halfBar(a, b);
  assert.equal(got.result, 'half_bar_phase_candidate');
  assert.equal(got.beatsPerBar, 4);
  close(got.deltaTime, 7.0, 'deltaTime');
  close(got.deltaBeat, 14.0, 'deltaBeat');
  close(got.phaseBeat, 2.0, 'phaseBeat');
  close(got.barPhaseOffset, 0.5, 'barPhaseOffset');
  close(got.halfBarErrorBeat, 0.0, 'halfBarErrorBeat');
  assert.equal(got.reliable, true);
});

test('half bar: a whole number of bars apart is not a half bar', () => {
  const a = grid({ gridId: 'A', originTime: 10.0 });
  const b = grid({ gridId: 'B', originTime: 16.0 });   // 12 beats = three bars
  const got = M.halfBar(a, b);
  assert.equal(got.result, 'no_candidate');
  close(got.phaseBeat, 0.0, 'phaseBeat');
  close(got.halfBarErrorBeat, 2.0, 'halfBarErrorBeat');
});

test('half bar: the order of the two grids does not change the answer', () => {
  const a = grid({ gridId: 'A', originTime: 10.0 });
  const b = grid({ gridId: 'B', originTime: 17.0 });
  const backwards = M.halfBar(b, a);
  close(backwards.phaseBeat, 2.0, 'phaseBeat');
  assert.equal(backwards.result, 'half_bar_phase_candidate');
});

test('half bar: different meters are refused rather than converted', () => {
  const a = grid({ gridId: 'A', originTime: 10.0, beatsPerBar: 4 });
  const b = grid({ gridId: 'B', originTime: 17.0, beatsPerBar: 3 });
  const got = M.halfBar(a, b);
  assert.equal(got.result, 'not_applicable');
  assert.match(got.reason, /拍号/);
});

test('half bar: tempos further apart than the tolerance are refused', () => {
  const a = grid({ gridId: 'A', originTime: 10.0, bpm: 120 });
  const b = grid({ gridId: 'B', originTime: 17.0, bpm: 130 });   // 8%
  const got = M.halfBar(a, b);
  assert.equal(got.result, 'not_applicable');
  assert.match(got.reason, /速度差/);
  // And two taps of the same song are not refused: measured, four attempts on one
  // song came out 105.8 to 107.0 BPM.
  assert.equal(M.halfBar(grid({ gridId: 'A', originTime: 17.291, bpm: 106.195 }),
    grid({ gridId: 'B', originTime: 103.387, bpm: 106.762 })).result, 'no_candidate');
});

test('half bar: a candidate over a long gap is flagged as unreliable', () => {
  // The folded phase moves by more than the half-bar window if the tempo is
  // anywhere between the two numbers, so the candidate says more about the tempo
  // than about the music.
  const a = grid({ gridId: 'A', originTime: 10.0, bpm: 120.0 });
  const b = grid({ gridId: 'B', originTime: 200.0, bpm: 121.0 });
  const got = M.halfBar(a, b);
  assert.ok(got.phaseUncertaintyBeat > 0.15, String(got.phaseUncertaintyBeat));
  assert.equal(got.reliable, false);
});

test('half bar: only evidence about when each grid worked makes it a transition', () => {
  const a = grid({ gridId: 'A', originTime: 10.0 });
  const b = grid({ gridId: 'B', originTime: 17.0 });
  assert.equal(M.halfBar(a, b, { validA: [12], validB: [20] }).result,
    'half_bar_transition_candidate');
  // B only useful before the change point is not evidence of a transition.
  assert.equal(M.halfBar(a, b, { validA: [12], validB: [12] }).result,
    'half_bar_phase_candidate');
});

/**
 * What the phone writes has to be readable here.

 * This is the field-for-field shape of `MgmcsExport.java` - the document the
 * phone produces at the end of a song, with the presses behind each grid in
 * `rawTaps` and the event names the two sides agreed on. `extra` keys the
 * desktop does not know are allowed; a missing one is a bug on the phone.
 */
test('a document written by the phone itself parses with nothing to report', () => {
  const doc = M.parseSongDocument(JSON.parse(PHONE_DOCUMENT));
  assert.deepEqual(doc.problems, []);
  assert.equal(doc.song.songId, '20261009 060116 song');
  assert.equal(doc.song.durationSeconds, 226.12);
  assert.equal(doc.grids.length, 1);
  assert.equal(doc.grids[0].bpm, 89.021);
  assert.equal(doc.grids[0].beatsPerBar, 4);
  // The presses are kept on the original object, which is what a later reading
  // of them would need; the parser's own job is the coordinates.
  assert.equal(doc.grids[0].raw.rawTaps.length, 4);
  assert.equal(doc.grids[0].raw.rawTaps[0].button, 'DOWNBEAT');
  assert.equal(doc.grids[0].raw.rawTaps[0].captureTime, 557.6);
  assert.equal(doc.events.length, 3);
  assert.deepEqual(doc.events.map((e) => e.type),
    ['attention_query', 'pause_moment', 'bar_answer']);
});

/**
 * A grid whose bars are not all the same length.

 * A period plus a meter can only describe equal bars, and a song that cuts a bar
 * short cannot be written that way - so it is written as a list of bar lines. The
 * list is a reading, and the tests here are about the reading staying put: the
 * short bar is where it says it is, the beats either side of it are still beats,
 * and nothing silently pretends the grid is periodic.
 */
function irregularGrid(extra = {}) {
  const doc = M.parseSongDocument({
    song: { title: 'short bar' },
    calibrations: [{
      calibrationId: 'cal_list',
      bpm: 120,               // half a second a beat, two seconds a 4/4 bar
      beatsPerBar: 4,
      barLines: [10, 12, 14, 15, 17],   // bar 2 is 14 -> 15: two beats
      source: 'derived',
      ...extra,
    }],
  });
  assert.deepEqual(doc.problems, []);
  return doc.grids[0];
}

test('a bar line list makes a grid, and the first line is where it starts', () => {
  const grid = irregularGrid();
  assert.equal(grid.irregular, true);
  assert.equal(grid.originTime, 10);
  assert.equal(grid.beatsPerBar, 4);
});

test('a short bar is short, and the beats either side of it still count from one', () => {
  const grid = irregularGrid();
  close(M.barBeats(grid, 0), 4, 'bar 0 beats');
  close(M.barBeats(grid, 2), 2, 'the short bar');
  close(M.barSeconds(grid, 2), 1.0, 'the short bar in seconds');
  close(M.barStart(grid, 3), 15, 'the bar after the short one');
});

test('a moment inside the short bar reads as that bar, not as the next one', () => {
  const grid = irregularGrid();
  const at = M.coordinateAt(grid, 14.5);
  assert.equal(at.bar, 2);
  close(at.beat, 1.0, 'beat inside the short bar');
  close(M.timeOf(grid, 2, 1), 14.5, 'bar 2 beat 1');
  const next = M.coordinateAt(grid, 15.0);
  assert.equal(next.bar, 3);
  close(next.beat, 0, 'the bar after it starts on its own beat 0');
});

test('before the first bar line the nominal bar is extrapolated backwards', () => {
  const grid = irregularGrid();
  const at = M.coordinateAt(grid, 9);
  assert.equal(at.bar, -1);
  close(at.beat, 2.0, 'beat two of the bar before the list starts');
});

test('past the last bar line the bars keep counting instead of the beat running on', () => {
  const grid = irregularGrid();
  // The list stops at 17, which is bar 4; 20 s is one nominal bar later, two beats in.
  const at = M.coordinateAt(grid, 20);
  assert.equal(at.bar, 5);
  close(at.beat, 2.0, 'beat inside the extrapolated bar');
});

test('a bar line list is not compared as a phase offset, and is not a half-bar candidate', () => {
  const listed = irregularGrid();
  const plain = grid({ bpm: 120 });   // the same tempo, an ordinary period grid
  assert.equal(M.phaseOffset(plain, listed).comparable, false);
  assert.equal(M.halfBar(plain, listed).result, 'not_applicable');
});

const PHONE_DOCUMENT = `{
  "schemaVersion": "0.1",
  "generatedBy": "listening_ai",
  "song": {
    "songId": "20261009 060116 song",
    "title": "米津玄師 — アイネクライネ",
    "audioFileName": "",
    "durationSeconds": 226.12
  },
  "calibrations": [
    {
      "calibrationId": "cal_01",
      "audioTime": 45.82,
      "bpm": 89.021,
      "beatsPerBar": 4,
      "beatUnit": 4,
      "source": "human",
      "note": "由 16 下按压重算（tapped）",
      "rawTaps": [
        { "tapIndex": 0, "button": "DOWNBEAT", "songTime": 36.897, "captureTime": 557.6,
          "clientTime": 1791518524738, "sessionId": 12 },
        { "tapIndex": 1, "button": "BEAT", "songTime": 37.917, "captureTime": 558.3,
          "clientTime": 1791518525400, "sessionId": 12 },
        { "tapIndex": 2, "button": "BEAT", "songTime": 38.938, "captureTime": 559.0,
          "clientTime": 1791518526123, "sessionId": 12 },
        { "tapIndex": 3, "button": "BEAT", "songTime": 38.938, "captureTime": 559.7,
          "clientTime": 1791518526796, "sessionId": 12 }
      ]
    }
  ],
  "events": [
    { "eventId": "evt_001", "audioTime": 20.125, "type": "attention_query",
      "source": "mobile",
      "payload": { "templateId": "ATTN_01", "targetSignal": "perceived_salience",
                   "songBar": 12, "text": "刚才最吸引你注意的是谁？",
                   "state": "answered", "answer": "鼓" } },
    { "eventId": "evt_002", "audioTime": 41.5, "type": "pause_moment",
      "source": "mobile",
      "payload": { "state": "AI_PROPOSED", "chosen": "", "aiVersion": "candidates-v1",
                   "bar": 3, "beat": 2 } },
    { "eventId": "evt_003", "audioTime": 33.25, "type": "bar_answer",
      "source": "mobile",
      "payload": { "choice": "bar_starts", "meter": 4, "bpm": 120 } }
  ]
}`;

const EXAMPLE = `{
  "schemaVersion": "0.1",
  "song": {
    "songId": "song_001",
    "title": "Example",
    "audioFileName": "song.flac"
  },
  "calibrations": [
    {
      "calibrationId": "cal_001",
      "audioTime": 31.428,
      "bpm": 120.0,
      "beatsPerBar": 4,
      "beatUnit": 4,
      "source": "human"
    },
    {
      "calibrationId": "cal_002",
      "audioTime": 63.211,
      "bpm": 120.0,
      "beatsPerBar": 3,
      "beatUnit": 4,
      "source": "human"
    }
  ],
  "events": [
    {
      "eventId": "evt_001",
      "audioTime": 48.225,
      "type": "guitar_attention_in"
    }
  ]
}`;
