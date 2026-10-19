// The demo song's analysis has to match the demo song.
//
// The demo is the one song whose structure is known exactly (it is generated,
// not recorded), so it is the one place where the timing can be checked against
// the audio instead of taken on faith: drums start at 8.0s, guitar at 12.0s,
// everything at 24.0s, at 60 BPM in 4/4. If those land on bar starts, the
// coordinate this layer computes is right at least once, verifiably.

import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { parseAnalysis } from "../js/analysis.js";
import { makeTiming, locate, barStart, segmentAt, boundaryNear } from "../js/timing.js";

const read = (p) => readFileSync(new URL(p, import.meta.url), "utf8");

test("the demo analysis parses without problems", () => {
  const { analysis, problems } = parseAnalysis(read("../demo/demo01.analysis.json"));
  assert.deepEqual(problems, []);
  assert.ok(analysis);
});

test("the manifest points at an analysis that is there", () => {
  const manifest = JSON.parse(read("../demo/manifest.json"));
  const entry = manifest.songs[0];
  assert.equal(entry.analysis, "demo01.analysis.json");
  const { analysis } = parseAnalysis(read("../demo/demo01.analysis.json"));
  assert.equal(analysis.songId, entry.id);
});

test("the demo's known events land on the bar starts the analysis claims", () => {
  const { analysis } = parseAnalysis(read("../demo/demo01.analysis.json"));
  const timing = makeTiming(analysis);
  assert.ok(timing, "the demo has timing");

  // 60 BPM, 4/4: one bar per four seconds, first beat at zero.
  assert.equal(barStart(timing, 1), 0);
  assert.equal(barStart(timing, 3), 8, "drums enter on a bar line");
  assert.equal(barStart(timing, 4), 12, "guitar enters on a bar line");
  assert.equal(barStart(timing, 7), 24, "the tutti starts on a bar line");

  const atDrums = locate(timing, 8.0);
  assert.equal(atDrums.bar, 3);
  assert.equal(atDrums.beatIndex, 1);

  const atGuitar = locate(timing, 12.5);
  assert.equal(atGuitar.bar, 4);
  assert.ok(Math.abs(atGuitar.beat - 1.5) < 1e-9, `halfway through the bar: ${atGuitar.beat}`);
});

test("the demo's segments and candidates line up with those events", () => {
  const { analysis } = parseAnalysis(read("../demo/demo01.analysis.json"));
  const timing = makeTiming(analysis);
  assert.equal(segmentAt(analysis, timing, 1).label, "A");
  assert.equal(segmentAt(analysis, timing, 8).label, "B");
  assert.equal(segmentAt(analysis, timing, 12.5).label, "C");
  assert.equal(segmentAt(analysis, timing, 30).label, "D");
  // The strip offers a candidate while you are in its bar, not a bar early:
  // "机器认为这里换段" is a claim about *here*.
  assert.equal(boundaryNear(analysis, timing, 8.2, 0).candidateId, "boundary_003");
  assert.equal(boundaryNear(analysis, timing, 12.5, 0).candidateId, "boundary_004");
  assert.equal(boundaryNear(analysis, timing, 20, 0), null, "nothing claimed inside C");
  assert.equal(boundaryNear(analysis, timing, 20, 1).candidateId, "boundary_007",
    "a wider window is available to a caller that wants a warning earlier");
});
