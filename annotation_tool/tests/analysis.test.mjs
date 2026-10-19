// Reading what Workbench produced.
//
// The rule for this file is that bad machine data must never stop a session and
// must never be silently believed. So parsing never throws, every problem it
// finds is reported by name, and whatever it cannot understand is simply not
// there — the annotator then behaves as if that song has no analysis, which is a
// state the UI already has to handle.

import test from "node:test";
import assert from "node:assert/strict";

import { ANALYSIS_SCHEMA, parseAnalysis, analysisSummary } from "../js/analysis.js";
import { makeTiming } from "../js/timing.js";

const GOOD = {
  schema: ANALYSIS_SCHEMA,
  songId: "song001",
  timing: { beatOffset: 0.42, bpm: 128, beatsPerBar: 4 },
  segments: [
    { segmentId: "seg_001", startBar: 1, endBar: 8, label: "A" },
    { segmentId: "seg_002", startBar: 9, endBar: 16, label: "B" },
  ],
  boundaries: [
    { bar: 9, musicnnScore: 0.81, qwenScore: 0.74, candidateId: "boundary_009" },
  ],
  generator: { tool: "workbench", version: "0.1" },
};

test("a good document comes through whole, including fields we do not know", () => {
  const { analysis, problems } = parseAnalysis(JSON.stringify(GOOD));
  assert.deepEqual(problems, []);
  assert.equal(analysis.songId, "song001");
  assert.equal(analysis.segments.length, 2);
  assert.equal(analysis.boundaries[0].candidateId, "boundary_009");
  assert.equal(analysis.generator.tool, "workbench", "unknown blocks are kept");
  assert.deepEqual(makeTiming(analysis), GOOD.timing);
});

test("text that is not JSON is a problem, not an exception", () => {
  const { analysis, problems } = parseAnalysis("{ nope");
  assert.equal(analysis, null);
  assert.equal(problems.length, 1);
  assert.match(problems[0], /JSON/i);
});

test("a document that is not a document is refused", () => {
  assert.equal(parseAnalysis("[1,2,3]").analysis, null);
  assert.equal(parseAnalysis("null").analysis, null);
  assert.equal(parseAnalysis("").analysis, null);
});

test("no timing is not an error: the song simply has no coordinates", () => {
  const { analysis, problems } = parseAnalysis(JSON.stringify({ segments: GOOD.segments }));
  assert.deepEqual(problems, [], "a machine that only did structure is a valid machine");
  assert.equal(makeTiming(analysis), null);
});

test("timing that cannot be used is reported, and still read-only", () => {
  const { analysis, problems } = parseAnalysis(JSON.stringify({
    timing: { beatOffset: 0, bpm: 0, beatsPerBar: 4 },
  }));
  assert.equal(problems.length, 1);
  assert.match(problems[0], /timing/);
  assert.equal(makeTiming(analysis), null);
});

test("segments and boundaries are checked one by one", () => {
  const { analysis, problems } = parseAnalysis(JSON.stringify({
    timing: GOOD.timing,
    segments: [{ segmentId: "seg_001", label: "A" }, { label: "no id" }, "nonsense"],
    boundaries: [{ bar: 9, musicnnScore: 0.8 }, { candidateId: "no-position" }, null],
  }));
  assert.equal(analysis.segments.length, 3, "kept as given, problems reported beside");
  assert.equal(problems.filter((p) => /segment/.test(p)).length, 2);
  assert.equal(problems.filter((p) => /boundary/.test(p)).length, 2);
  assert.equal(analysis.boundaries[0].musicnnScore, 0.8);
});

test("a schema from the future is a warning, not a wall", () => {
  const { analysis, problems } = parseAnalysis(JSON.stringify({
    schema: "fallenangel.attention.analysis/9.9",
    timing: GOOD.timing,
  }));
  assert.ok(analysis, "still readable");
  assert.equal(problems.length, 1);
  assert.match(problems[0], /schema/);
});

test("the summary is what the strip needs to know", () => {
  const good = parseAnalysis(JSON.stringify(GOOD)).analysis;
  assert.deepEqual(analysisSummary(good), {
    hasTiming: true, segments: 2, boundaries: 1, similar: 0,
  });
  assert.deepEqual(analysisSummary(null), {
    hasTiming: false, segments: 0, boundaries: 0, similar: 0,
  });
});
