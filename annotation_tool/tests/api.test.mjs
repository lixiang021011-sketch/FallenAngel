// Transport guarantees: idempotency and "local first, retry forever".

import test from "node:test";
import assert from "node:assert/strict";

// Minimal localStorage so the mock backend can be exercised outside a browser.
if (!globalThis.localStorage) {
  const map = new Map();
  globalThis.localStorage = {
    getItem: (k) => (map.has(k) ? map.get(k) : null),
    setItem: (k, v) => map.set(k, String(v)),
    removeItem: (k) => map.delete(k),
    clear: () => map.clear(),
  };
}

const { MockBackend, UploadQueue } = await import("../js/api.js");

function memoryStore() {
  let queue = [];
  return {
    queueList: () => [...queue],
    queueReplace: (items) => { queue = items; },
  };
}

test("the same eventId is stored once, however many times it is uploaded", async () => {
  const backend = new MockBackend({ failRate: 0, key: "test-mock-1" });
  localStorage.clear();
  backend.db = { sessions: {}, events: {}, sessionEnds: {}, reviews: {} };

  const event = { eventId: "abc", sessionId: "s", eventType: "attention_in", partId: "Bass", audioTime: 1 };
  assert.deepEqual(await backend.postEvent(event), { ok: true, eventId: "abc" });
  assert.deepEqual(await backend.postEvent(event), { ok: true, deduped: true, eventId: "abc" });
  assert.deepEqual(await backend.postEvent(event), { ok: true, deduped: true, eventId: "abc" });
  assert.equal(Object.keys(backend.db.events).length, 1);
});

test("a failing upload stays queued with a backoff, and survives as-is", async () => {
  const store = memoryStore();
  const dead = new MockBackend({ failRate: 1, key: "test-mock-2" });
  const queue = new UploadQueue({ store, backend: dead });

  store.queueReplace([
    { queueId: "q1", kind: "event", payload: { eventId: "e1" }, attempts: 0, nextAt: 0, lastError: null, createdAt: "2026-10-05T00:00:00.000Z" },
  ]);

  await queue.flush();
  const after = store.queueList();
  assert.equal(after.length, 1, "the item must not be dropped");
  assert.equal(after[0].attempts, 1);
  assert.ok(after[0].nextAt > Date.now(), "a backoff must be scheduled");
  assert.ok(after[0].lastError);
});

test("a working backend drains the queue in order", async () => {
  const store = memoryStore();
  const backend = new MockBackend({ failRate: 0, key: "test-mock-3" });
  localStorage.clear();
  backend.db = { sessions: {}, events: {}, sessionEnds: {}, reviews: {} };
  const queue = new UploadQueue({ store, backend });

  store.queueReplace([
    { queueId: "q1", kind: "event", payload: { eventId: "e1" }, attempts: 3, nextAt: Date.now() + 60000, lastError: "boom", createdAt: "2026-10-05T00:00:02.000Z" },
    { queueId: "q2", kind: "event", payload: { eventId: "e2" }, attempts: 0, nextAt: 0, lastError: null, createdAt: "2026-10-05T00:00:01.000Z" },
  ]);

  // q1 is inside its backoff window, so only q2 should go out.
  await queue.flush();
  assert.deepEqual(store.queueList().map((i) => i.queueId), ["q1"]);

  // Recovering the network clears the backoff (this is what setBackend does).
  queue.resetBackoff();
  await queue.flush();
  assert.deepEqual(store.queueList(), []);
  assert.equal(Object.keys(backend.db.events).length, 2);
});

test("an http backend with no base URL refuses instead of pretending to succeed", async () => {
  const { HttpBackend } = await import("../js/api.js");
  const backend = new HttpBackend({ baseUrl: "" });
  await assert.rejects(() => backend.postEvent({ eventId: "e" }), /No API base URL/);
});
