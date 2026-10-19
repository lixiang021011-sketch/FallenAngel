// Semantics that the research data depends on. If these break, the exported
// session JSON silently stops meaning what the spec says it means.

import test from "node:test";
import assert from "node:assert/strict";

import { buildRegions, pairIntervals, regionId } from "../js/derive.js";
import { SessionRecorder } from "../js/session.js";

let seq = 0;
function ev(eventType, audioTime, partId = null, extra = {}) {
  seq += 1;
  return {
    eventId: `e${String(seq).padStart(4, "0")}`,
    sessionId: "s1",
    songId: "Song001",
    audioTime,
    eventType,
    partId,
    clientTime: "2026-10-05T14:30:42.123Z",
    ...extra,
  };
}

test("raw in/out pair becomes one interval and one region", () => {
  const events = [
    ev("attention_in", 42.813, "Bass"),
    ev("attention_out", 47.312, "Bass"),
  ];
  const { intervals } = pairIntervals(events);
  assert.equal(intervals.length, 1);
  assert.equal(intervals[0].partId, "Bass");
  assert.equal(intervals[0].start, 42.813);
  assert.equal(intervals[0].end, 47.312);
  assert.equal(intervals[0].autoClosed, false);

  const regions = buildRegions(events);
  assert.equal(regions.length, 1);
  assert.deepEqual(regions[0].partIds, ["Bass"]);
  assert.equal(regions[0].regionId, regionId(42.813, 47.312));
});

test("timestamps are never snapped or rounded beyond milliseconds", () => {
  const events = [ev("attention_in", 42.8134, "Bass"), ev("attention_out", 42.8139, "Bass")];
  const { intervals } = pairIntervals(events);
  assert.equal(intervals[0].start, 42.8134);
});

test("an overlapping second part splits the span into constant part-sets", () => {
  const events = [
    ev("attention_in", 10, "Guitar"),
    ev("attention_in", 12, "Bass"),
    ev("attention_out", 14, "Bass"),
    ev("attention_out", 16, "Guitar"),
  ];
  const regions = buildRegions(events);
  assert.deepEqual(regions.map((r) => [r.start, r.end, r.partIds]), [
    [10, 12, ["Guitar"]],
    [12, 14, ["Bass", "Guitar"]],
    [14, 16, ["Guitar"]],
  ]);
});

test("seek closes open parts at the pre-jump position and marks them automatic", () => {
  const events = [
    ev("attention_in", 42, "Bass"),
    // exactly what the recorder writes when the playhead moves
    ev("attention_out", 42.9, "Bass", { autoClosed: true, closeReason: "seek" }),
    ev("seek", 42.9, null, { seekTo: 90 }),
    ev("attention_in", 93, "Drums"),
    ev("attention_out", 95, "Drums"),
  ];
  const { intervals } = pairIntervals(events);
  const bass = intervals.find((i) => i.partId === "Bass");
  assert.equal(bass.start, 42);
  assert.equal(bass.end, 42.9);
  assert.equal(bass.autoClosed, true);
  assert.equal(bass.closeReason, "seek");

  const regions = buildRegions(events);
  assert.equal(regions.length, 2);
  assert.equal(regions[0].parts[0].autoClosed, true);
  assert.equal(regions[1].parts[0].autoClosed, false);
});

test("a seek that happens with nothing open produces no region", () => {
  const events = [ev("play", 0), ev("seek", 42.9, null, { seekTo: 90 }), ev("seek", 90, null, { seekTo: 42.9 })];
  assert.deepEqual(buildRegions(events), []);
  assert.equal(pairIntervals(events).intervals.length, 0);
});

test("zero-length taps are kept as intervals but do not create regions", () => {
  const events = [ev("attention_in", 5, "Piano"), ev("attention_out", 5, "Piano")];
  assert.equal(pairIntervals(events).intervals.length, 1);
  assert.deepEqual(buildRegions(events), []);
});

test("stray attention_out without a matching in is ignored by the derivation", () => {
  const events = [ev("attention_out", 5, "Piano"), ev("attention_in", 6, "Piano"), ev("attention_out", 7, "Piano")];
  const regions = buildRegions(events);
  assert.equal(regions.length, 1);
  assert.equal(regions[0].start, 6);
});

test("unclosed parts are reported as open, and can be closed at a known end time", () => {
  const events = [ev("attention_in", 30, "Guitar")];
  assert.equal(pairIntervals(events).open.length, 1);
  assert.equal(buildRegions(events).length, 0);

  const withEnd = buildRegions(events, { openUntil: 33.5 });
  assert.equal(withEnd.length, 1);
  assert.equal(withEnd[0].end, 33.5);
});

test("nothing is emitted for a session with no annotations", () => {
  const events = [ev("play", 0), ev("pause", 10), ev("song_end", 32)];
  assert.deepEqual(buildRegions(events), []);
});

test("simultaneous parts report the same span, not a merged blob", () => {
  const events = [
    ev("attention_in", 60, "Guitar"),
    ev("attention_in", 60, "Bass"),
    ev("attention_out", 64, "Bass"),
    ev("attention_out", 64, "Guitar"),
  ];
  const regions = buildRegions(events);
  assert.equal(regions.length, 1);
  assert.deepEqual(regions[0].partIds, ["Bass", "Guitar"]);
  assert.equal(regions[0].start, 60);
  assert.equal(regions[0].end, 64);
});

test("adjacent spans with the same part set merge back together", () => {
  const events = [
    ev("attention_in", 0, "Drums"),
    ev("attention_out", 4, "Drums"),
    ev("attention_in", 4, "Drums"),
    ev("attention_out", 9, "Drums"),
  ];
  const regions = buildRegions(events);
  assert.equal(regions.length, 1);
  assert.equal(regions[0].start, 0);
  assert.equal(regions[0].end, 9);
});

test("region ids are deterministic so notes keep their anchor", () => {
  const a = buildRegions([ev("attention_in", 12.5, "Bass"), ev("attention_out", 15, "Bass")]);
  const b = buildRegions([
    ev("play", 0),
    ev("attention_in", 12.5, "Bass"),
    ev("pause", 13),
    ev("attention_out", 15, "Bass"),
  ]);
  assert.equal(a[0].regionId, b[0].regionId);
});
