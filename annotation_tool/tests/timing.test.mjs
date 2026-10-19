// The bar/beat coordinate, and the ranges the structure buttons need.
//
// This is the one place that turns seconds into Bar 17 · Beat 2.3, and every
// button that moves the playhead by a musical unit goes through it. It is
// derived information and only derived: nothing here is allowed to move an
// annotation, and nothing here rounds a tap onto a beat.

import test from "node:test";
import assert from "node:assert/strict";

import {
  makeTiming,
  locate,
  barStart,
  barCount,
  prevBarTarget,
  nextBarTarget,
  rangeOfBars,
  segmentAt,
  segmentRange,
  boundaryNear,
  similarOf,
} from "../js/timing.js";

// 128 BPM, one beat = 0.46875s, one bar = 1.875s, first beat at 0.42s.
const TIMING = { beatOffset: 0.42, bpm: 128, beatsPerBar: 4 };

test("a time is located in bars and beats, counting from one", () => {
  const at = locate(TIMING, 0.42);
  assert.equal(at.bar, 1);
  assert.equal(at.beat, 1);
  assert.equal(at.beatIndex, 1);
  assert.equal(at.beatFrac, 0);
});

test("the beat number keeps its fraction, because that is what was played", () => {
  const at = locate(TIMING, 0.42 + 0.46875 * 5 + 0.1453);
  assert.equal(at.bar, 2);
  assert.equal(at.beatIndex, 2);
  assert.ok(Math.abs(at.beat - 2.31) < 0.01, `beat was ${at.beat}`);
});

test("a bar starts where it starts, and the next one where it does", () => {
  assert.ok(Math.abs(barStart(TIMING, 1) - 0.42) < 1e-9);
  assert.ok(Math.abs(barStart(TIMING, 5) - (0.42 + 4 * 1.875)) < 1e-9);
  assert.ok(Math.abs(barStart(TIMING, 17) - (0.42 + 16 * 1.875)) < 1e-9);
});

test("the two bar buttons stay inside the music", () => {
  const inside = 0.42 + 3 * 1.875 + 0.5; // middle of bar 4
  assert.ok(Math.abs(prevBarTarget(TIMING, inside) - barStart(TIMING, 4)) < 1e-9);
  assert.ok(Math.abs(nextBarTarget(TIMING, inside) - barStart(TIMING, 5)) < 1e-9);
  // Before the first beat: the first bar is still the one we are in, and it
  // starts where the music does.
  assert.equal(nextBarTarget(TIMING, 0.1), barStart(TIMING, 2));
  assert.ok(prevBarTarget(TIMING, 0.1) <= 0.42);
});

test("bar counts are whole bars that exist", () => {
  assert.equal(barCount(TIMING, 0.42 + 8 * 1.875), 9);
  assert.equal(barCount(TIMING, 0.42), 1);
  assert.equal(barCount(TIMING, 0), 1, "before the first beat there is still bar 1");
});

test("a loop of N bars is N bars long", () => {
  const one = rangeOfBars(TIMING, 5, 1);
  assert.ok(Math.abs(one.from - barStart(TIMING, 5)) < 1e-9);
  assert.ok(Math.abs(one.to - barStart(TIMING, 6)) < 1e-9);
  const four = rangeOfBars(TIMING, 5, 4);
  assert.ok(Math.abs(four.to - four.from - 4 * 1.875) < 1e-9);
});

test("no timing means no coordinates rather than invented ones", () => {
  assert.equal(makeTiming({}), null);
  assert.equal(makeTiming({ bpm: 0, beatsPerBar: 4, beatOffset: 0 }), null);
  assert.equal(makeTiming({ bpm: 128, beatsPerBar: 4 }), null, "beatOffset is required");
  assert.equal(locate(null, 51.284), null);
});

const ANALYSIS = {
  timing: TIMING,
  segments: [
    { segmentId: "seg_001", startBar: 1, endBar: 8, label: "A" },
    { segmentId: "seg_002", startBar: 9, endBar: 16, label: "B",
      similar: [{ segmentId: "seg_004", similarity: 0.91 }] },
    { segmentId: "seg_003", startBar: 17, endBar: 24, label: "C" },
  ],
  boundaries: [
    { bar: 9, musicnnScore: 0.81, qwenScore: 0.74, candidateId: "boundary_009" },
    { bar: 17, musicnnScore: 0.91, qwenScore: 0.5, candidateId: "boundary_017" },
  ],
};

test("the current segment is the one whose bars contain this one", () => {
  assert.equal(segmentAt(ANALYSIS, TIMING, barStart(TIMING, 1)).label, "A");
  assert.equal(segmentAt(ANALYSIS, TIMING, barStart(TIMING, 12)).label, "B");
  assert.equal(segmentAt(ANALYSIS, TIMING, barStart(TIMING, 20)).label, "C");
  assert.equal(segmentAt(ANALYSIS, TIMING, 1e6), null, "past the last segment");
  assert.equal(segmentAt(null, TIMING, 10), null);
});

test("a segment's range is its bars, end inclusive", () => {
  const seg = ANALYSIS.segments[1];
  const range = segmentRange(ANALYSIS, TIMING, seg);
  assert.ok(Math.abs(range.from - barStart(TIMING, 9)) < 1e-9);
  assert.ok(Math.abs(range.to - barStart(TIMING, 17)) < 1e-9);
});

test("a boundary is offered only when it is the one next to us", () => {
  assert.equal(boundaryNear(ANALYSIS, TIMING, barStart(TIMING, 12), 1), null,
    "no candidate within a bar of bar 12");
  const near = boundaryNear(ANALYSIS, TIMING, barStart(TIMING, 9) + 0.2, 1);
  assert.equal(near.candidateId, "boundary_009");
  const strong = boundaryNear(ANALYSIS, TIMING, barStart(TIMING, 16) + 0.2, 1);
  assert.equal(strong.candidateId, "boundary_017", "the nearest one wins");
});

test("a segment's similar sections are listed, best first", () => {
  const list = similarOf(ANALYSIS, ANALYSIS.segments[1]);
  assert.equal(list.length, 1);
  assert.equal(list[0].segmentId, "seg_004");
  assert.equal(similarOf(ANALYSIS, ANALYSIS.segments[0]).length, 0);
  assert.equal(similarOf(null, ANALYSIS.segments[0]).length, 0);
});
