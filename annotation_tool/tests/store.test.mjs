// The two layers, and the promise that they stay apart.
//
// Human events and machine observations live in the same local store but in
// different places, and the export keeps them apart. That is the one structural
// guarantee this layer owes the research: a machine prediction must never be
// readable as something a person said.

import test from "node:test";
import assert from "node:assert/strict";

// store.js reads localStorage in its constructor, so the stub comes first.
globalThis.localStorage = {
  _v: new Map(),
  getItem(k) { return this._v.has(k) ? this._v.get(k) : null; },
  setItem(k, v) { this._v.set(k, String(v)); },
  removeItem(k) { this._v.delete(k); },
};

const { Store } = await import("../js/store.js");

const ANALYSIS = {
  schema: "fallenangel.attention.analysis/0.1",
  songId: "song001",
  timing: { beatOffset: 0.42, bpm: 128, beatsPerBar: 4 },
  segments: [{ segmentId: "seg_001", startBar: 1, endBar: 8, label: "A" }],
  boundaries: [{ bar: 9, musicnnScore: 0.81 }],
};

function freshStore() {
  globalThis.localStorage._v.clear();
  return new Store();
}

test("an analysis is kept per song, and replaced rather than merged", () => {
  const store = freshStore();
  assert.equal(store.getAnalysis("song001"), null, "nothing yet");
  store.attachAnalysis("song001", ANALYSIS);
  assert.equal(store.getAnalysis("song001").segments.length, 1);
  assert.equal(store.getAnalysis("song002"), null);

  store.attachAnalysis("song001", { ...ANALYSIS, segments: [] });
  assert.equal(store.getAnalysis("song001").segments.length, 0,
    "a new analysis is a new document, not a patch on the old one");
});

test("the machine layer survives a reload", () => {
  const store = freshStore();
  store.attachAnalysis("song001", ANALYSIS);
  const reopened = new Store();
  assert.equal(reopened.getAnalysis("song001").timing.bpm, 128);
});

test("a session's export keeps human events and machine observations apart", () => {
  const store = freshStore();
  store.attachAnalysis("song001", ANALYSIS);
  const session = store.createSession({
    songId: "song001", songTitle: "Song 1", parts: ["Guitar"], duration: 32,
  });
  store.appendEvent(session.sessionId, {
    eventId: "e1", audioTime: 4.2, eventType: "attention_in", partId: "Guitar", bar: 3, beat: 1.2,
  });
  store.appendEvent(session.sessionId, {
    eventId: "e2", audioTime: 51.284, eventType: "SECTION_BOUNDARY", partId: null, source: "human",
  });

  const out = store.exportSession(session.sessionId);
  assert.equal(out.rawEvents.length, 2, "only what the person did");
  assert.equal(out.rawEvents.some((e) => /NOVELTY|PREDICTION|SCORE/.test(e.eventType)), false);
  assert.equal(out.machineObservations.songId, "song001");
  assert.equal(out.machineObservations.analysis.boundaries[0].musicnnScore, 0.81,
    "machine data is handed back as the document it came in as");
  assert.equal(out.machineObservations.analysis === ANALYSIS, true, "not copied, not rewritten");
});

test("a song with no analysis exports an empty machine layer, not a missing one", () => {
  const store = freshStore();
  const session = store.createSession({ songId: "song404", songTitle: "None", parts: [], duration: 10 });
  const out = store.exportSession(session.sessionId);
  assert.deepEqual(out.machineObservations, { songId: "song404", analysis: null });
});

test("a full backup carries both layers", () => {
  const store = freshStore();
  store.attachAnalysis("song001", ANALYSIS);
  const all = store.exportEverything();
  assert.equal(all.analysis.song001.timing.bpm, 128);
  assert.ok("events" in all && "sessions" in all, "the human layer is still there");
});
