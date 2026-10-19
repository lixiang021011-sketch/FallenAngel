// The recorder is what actually produces the raw log, so its ordering rules are
// tested against a fake engine rather than trusted.

import test from "node:test";
import assert from "node:assert/strict";

import { SessionRecorder } from "../js/session.js";
import { buildRegions, pairIntervals } from "../js/derive.js";

function fakeStore() {
  const events = [];
  return {
    events,
    appendEvent(_sessionId, event) { events.push(event); return event; },
    types() { return events.map((e) => `${e.eventType}${e.partId ? `:${e.partId}` : ""}@${e.audioTime}`); },
  };
}

function fakeEngine() {
  const handlers = new Map();
  return {
    on(name, fn) {
      if (!handlers.has(name)) handlers.set(name, new Set());
      handlers.get(name).add(fn);
      return () => handlers.get(name).delete(fn);
    },
    emit(name, payload) {
      for (const fn of [...(handlers.get(name) || [])]) fn(payload);
    },
  };
}

function setup() {
  const store = fakeStore();
  const engine = fakeEngine();
  const recorder = new SessionRecorder({ store, sessionId: "s1", songId: "Song001" });
  const detach = recorder.attach(engine);
  return { store, engine, recorder, detach };
}

test("a press writes attention_in and a second press writes attention_out", () => {
  const { store, recorder } = setup();
  recorder.togglePart("Bass", 42.813);
  recorder.togglePart("Bass", 47.312);
  assert.deepEqual(store.types(), ["attention_in:Bass@42.813", "attention_out:Bass@47.312"]);
  assert.equal(store.events[0].clientTime.endsWith("Z"), true);
});

test("two parts can be active at the same time", () => {
  const { store, recorder } = setup();
  recorder.togglePart("Guitar", 10);
  recorder.togglePart("Bass", 11);
  assert.deepEqual(recorder.activeParts().sort(), ["Bass", "Guitar"]);
  assert.equal(store.events.length, 2);
});

test("seek closes open parts first, at the pre-jump position, then logs the seek", () => {
  const { store, recorder, engine } = setup();
  recorder.togglePart("Bass", 42);
  engine.emit("seek", { from: 42.9, to: 90 });

  assert.deepEqual(store.types(), [
    "attention_in:Bass@42",
    "attention_out:Bass@42.9",
    "seek@42.9",
  ]);
  assert.equal(store.events[1].autoClosed, true);
  assert.equal(store.events[1].closeReason, "seek");
  assert.equal(store.events[2].partId, null);
  assert.equal(store.events[2].seekTo, 90);
  assert.deepEqual(recorder.activeParts(), []);
});

test("seek with nothing open does not invent an interval", () => {
  const { store, engine } = setup();
  engine.emit("seek", { from: 5, to: 90 });
  assert.deepEqual(store.types(), ["seek@5"]);
  assert.deepEqual(buildRegions(store.events), []);
});

test("repeated seeks are all kept — replaying a region is itself signal", () => {
  const { store, engine } = setup();
  engine.emit("seek", { from: 105, to: 100 });
  engine.emit("seek", { from: 108, to: 100 });
  assert.deepEqual(store.types(), ["seek@105", "seek@108"]);
  assert.equal(store.events[0].seekTo, 100);
  assert.equal(store.events[1].seekTo, 100);
});

test("restart closes open parts with closeReason restart", () => {
  const { store, recorder, engine } = setup();
  recorder.togglePart("Piano", 20);
  engine.emit("restart", { from: 22.5, to: 0 });
  assert.deepEqual(store.types(), [
    "attention_in:Piano@20",
    "attention_out:Piano@22.5",
    "restart@22.5",
  ]);
  assert.equal(store.events[1].closeReason, "restart");
});

test("play and pause are logged as part-less transport events", () => {
  const { store, engine } = setup();
  engine.emit("play", { audioTime: 0 });
  engine.emit("pause", { audioTime: 12.5 });
  assert.deepEqual(store.types(), ["play@0", "pause@12.5"]);
  assert.equal(store.events.every((e) => e.partId === null), true);
});

test("closing a session ends every open interval, not just the last one", () => {
  const { store, recorder } = setup();
  recorder.togglePart("Guitar", 30);
  recorder.togglePart("Bass", 31);
  const closed = recorder.closeAll("exit", 33);
  assert.equal(closed.length, 2);
  assert.deepEqual(recorder.activeParts(), []);
  const { intervals } = pairIntervals(store.events);
  assert.equal(intervals.length, 2);
  assert.equal(intervals.every((i) => i.autoClosed && i.closeReason === "exit"), true);
});

test("every event carries the fields the export schema promises", () => {
  const { store, recorder, engine } = setup();
  recorder.togglePart("Drums", 1.2345);
  engine.emit("seek", { from: 2, to: 8 });
  for (const e of store.events) {
    for (const key of ["eventId", "sessionId", "songId", "audioTime", "eventType", "partId", "clientTime"]) {
      assert.ok(key in e, `missing ${key}`);
    }
    assert.equal(e.sessionId, "s1");
    assert.equal(e.songId, "Song001");
    assert.equal(Math.round(e.audioTime * 1000), e.audioTime * 1000);
  }
});

// ---------------------------------------------------------------- structure
//
// The structure events added for the Workbench layer. Their rules are thin but
// they are the ones that keep human and machine data apart: a human boundary is
// a human event with source "human", a review of a machine candidate is a human
// decision *about* machine data, and a loop jump is a player event that must not
// be mistaken for the listener having heard the jump.

function setupWithTiming() {
  const store = fakeStore();
  const engine = fakeEngine();
  const recorder = new SessionRecorder({
    store,
    sessionId: "s1",
    songId: "Song001",
    locate: (t) => ({ bar: Math.floor(t / 1.875) + 1, beat: ((t % 1.875) / 0.46875) + 1 }),
  });
  const detach = recorder.attach(engine);
  return { store, engine, recorder, detach };
}

test("这里换段 writes a human boundary, with the bar it happened on", () => {
  const { store, recorder } = setupWithTiming();
  recorder.sectionBoundary(51.284);
  const e = store.events[0];
  assert.equal(e.eventType, "SECTION_BOUNDARY");
  assert.equal(e.partId, null);
  assert.equal(e.source, "human");
  assert.equal(e.audioTime, 51.284, "the time is the tap, not the bar line");
  assert.equal(e.bar, 28);
  assert.ok(Math.abs(e.beat - 2.41) < 0.02, `beat kept its fraction: ${e.beat}`);
});

test("a boundary review records the decision and which candidate it was about", () => {
  const { store, recorder } = setupWithTiming();
  recorder.boundaryReview("boundary_017", "accept", 32.4);
  recorder.boundaryReview("boundary_009", "reject", 16.9);
  recorder.boundaryReview("boundary_009", "ignore", 17.1);
  assert.deepEqual(store.types().map((t) => t.split("@")[0].split(":")[0]), [
    "BOUNDARY_REVIEW", "BOUNDARY_REVIEW", "BOUNDARY_REVIEW",
  ]);
  assert.equal(store.events[0].candidateId, "boundary_017");
  assert.equal(store.events[0].decision, "accept");
  assert.equal(store.events[1].decision, "reject");
  assert.equal(store.events[2].decision, "ignore");
  assert.throws(() => recorder.boundaryReview("boundary_009", "maybe", 1), /decision/);
});

test("a loop jump is written down and does not close anything", () => {
  const { store, recorder } = setupWithTiming();
  recorder.togglePart("Drums", 10);
  // Same notation as a seek event: `from` is where the playhead was, `to` is
  // where it went - i.e. a loop jump back from the end of the loop.
  recorder.loopJump(11.2, 5.0, 11.2);
  assert.deepEqual(store.types(), ["attention_in:Drums@10", "LOOP_JUMP@11.2"]);
  assert.equal(store.events[0].eventType, "attention_in", "the jump closed the interval");
  assert.equal(store.events[1].from, 11.2, "where the playhead was: the end of the loop");
  assert.equal(store.events[1].to, 5.0, "where it went: the start of the loop");
  assert.deepEqual(recorder.activeParts(), ["Drums"],
    "a loop is the same music again, not the listener changing their mind");
});

test("the engine's loop event becomes a LOOP_JUMP, not a seek", () => {
  const { store, engine } = setup();
  engine.emit("loop", { from: 11.2, to: 5, at: 11.2 });
  assert.deepEqual(store.types(), ["LOOP_JUMP@11.2"]);
  assert.equal(store.events[0].closeReason, undefined);
});

test("bar and beat are attached when the song has them, and absent when it does not", () => {
  const withTiming = setupWithTiming();
  withTiming.recorder.togglePart("Guitar", 4);
  assert.equal(withTiming.store.events[0].bar, 3);
  assert.ok(withTiming.store.events[0].beat > 1);

  const without = setup();
  without.recorder.togglePart("Guitar", 4);
  assert.equal("bar" in without.store.events[0], false);
  assert.equal(without.store.events[0].audioTime, 4, "and the time is untouched either way");
});
