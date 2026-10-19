import test from "node:test";
import assert from "node:assert/strict";

import { summarizeLibrary, pickNextUnannotated } from "../js/progress.js";

const song = (id, duration = 0) => ({ id, title: id, duration });
const session = (songId, extra = {}) => ({
  sessionId: `s-${songId}-${Math.random()}`,
  songId,
  endedAt: "2026-10-05T10:00:00.000Z",
  ...extra,
});

test("an empty library reports zeroes rather than NaN", () => {
  assert.deepEqual(summarizeLibrary([], []), {
    total: 0, annotated: 0, remaining: 0, coveredSeconds: 0, sessions: 0,
  });
});

test("a library with no sessions is all remaining", () => {
  const s = summarizeLibrary([song("a", 200), song("b", 180)], []);
  assert.equal(s.total, 2);
  assert.equal(s.annotated, 0);
  assert.equal(s.remaining, 2);
  assert.equal(s.coveredSeconds, 0);
});

test("covered time is the length of the material, not the number of sessions", () => {
  const songs = [song("a", 200), song("b", 180)];
  const sessions = [session("a"), session("a"), session("b")];
  const s = summarizeLibrary(songs, sessions);
  assert.equal(s.annotated, 2);
  assert.equal(s.remaining, 0);
  assert.equal(s.coveredSeconds, 380, "200 + 180, counted once each");
  assert.equal(s.sessions, 3, "repeat listens still count as sessions");
});

test("a bulk-imported song with no stored duration uses what the session measured", () => {
  const songs = [song("bulk", 0)];
  const s = summarizeLibrary(songs, [session("bulk", { songDuration: 231.5, endedAtAudioTime: 228 })]);
  assert.equal(s.coveredSeconds, 231.5);
});

test("a session that was never ended does not count as annotated", () => {
  const songs = [song("a", 100)];
  const open = session("a", { endedAt: null });
  const s = summarizeLibrary(songs, [open]);
  assert.equal(s.annotated, 0);
  assert.equal(s.remaining, 1);
  assert.equal(s.sessions, 0);
});

test("sessions for songs no longer in the library do not inflate the totals", () => {
  const songs = [song("a", 100)];
  const s = summarizeLibrary(songs, [session("a"), session("deleted-song")]);
  assert.equal(s.total, 1);
  assert.equal(s.annotated, 1);
  assert.equal(s.coveredSeconds, 100);
});

test("the next unannotated song follows import order and skips finished ones", () => {
  const songs = [song("newest"), song("middle"), song("oldest")];
  const sessions = [session("oldest")];
  assert.equal(pickNextUnannotated(songs, sessions).id, "middle");
});

test("the next unannotated song is null once everything is done", () => {
  const songs = [song("a"), song("b")];
  assert.equal(pickNextUnannotated(songs, [session("a"), session("b")]), null);
  assert.equal(pickNextUnannotated([], []), null);
});
