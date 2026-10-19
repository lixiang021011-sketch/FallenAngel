// Transport layer. The app only ever talks to the `UploadQueue`, which talks to
// a backend object. Swapping the mock backend for the real one is a one-line
// change in settings — see spec section 11.

import { delay, uuid } from "./util.js";

export const ENDPOINTS = {
  session: "/api/v1/annotation/sessions",
  event: "/api/v1/annotation/events",
  sessionEnd: (id) => `/api/v1/annotation/sessions/${encodeURIComponent(id)}/end`,
  review: "/api/v1/annotation/reviews",
};

function httpError(message, status) {
  const err = new Error(message);
  err.status = status;
  return err;
}

/** In-browser stand-in for the backend. Persists, and de-duplicates by eventId. */
export class MockBackend {
  constructor({ failRate = 0, key = "fa_annot_mockdb" } = {}) {
    this.name = "mock";
    this.failRate = failRate;
    this.key = key;
    this.db = this._load();
    this.calls = [];
  }

  _load() {
    try {
      const raw = localStorage.getItem(this.key);
      if (raw) return JSON.parse(raw);
    } catch { /* ignore */ }
    return { sessions: {}, events: {}, sessionEnds: {}, reviews: {} };
  }

  _save() {
    try { localStorage.setItem(this.key, JSON.stringify(this.db)); } catch (err) { console.warn(err); }
  }

  _maybeFail() {
    if (this.failRate > 0 && Math.random() < this.failRate) {
      throw httpError("mock backend refused the request", 0);
    }
  }

  async _send(name, run) {
    await delay(90 + Math.random() * 160); // pretend network
    this._maybeFail();
    this.calls.push({ name, at: new Date().toISOString() });
    const result = run();
    this._save();
    return result;
  }

  postSession(session) {
    return this._send("postSession", () => {
      this.db.sessions[session.sessionId] = session;
      return { ok: true, sessionId: session.sessionId };
    });
  }

  postEvent(event) {
    return this._send("postEvent", () => {
      if (this.db.events[event.eventId]) return { ok: true, deduped: true, eventId: event.eventId };
      this.db.events[event.eventId] = event;
      return { ok: true, eventId: event.eventId };
    });
  }

  postSessionEnd(sessionId, payload) {
    return this._send("postSessionEnd", () => {
      this.db.sessionEnds[sessionId] = payload;
      return { ok: true, sessionId };
    });
  }

  postReview(review) {
    return this._send("postReview", () => {
      this.db.reviews[review.reviewId] = review;
      return { ok: true, reviewId: review.reviewId };
    });
  }
}

/** Real backend. Same surface, plain fetch with a timeout. */
export class HttpBackend {
  constructor({ baseUrl, timeoutMs = 8000 } = {}) {
    this.name = "http";
    this.baseUrl = String(baseUrl || "").replace(/\/+$/, "");
    this.timeoutMs = timeoutMs;
  }

  async _post(path, body) {
    if (!this.baseUrl) throw httpError("No API base URL configured.", 0);
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), this.timeoutMs);
    try {
      const res = await fetch(`${this.baseUrl}${path}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
        signal: ctrl.signal,
      });
      if (!res.ok) throw httpError(`HTTP ${res.status}`, res.status);
      const text = await res.text();
      return text ? JSON.parse(text) : { ok: true };
    } catch (err) {
      if (err.name === "AbortError") throw httpError("request timed out", 0);
      throw err;
    } finally {
      clearTimeout(timer);
    }
  }

  postSession(session) { return this._post(ENDPOINTS.session, session); }
  postEvent(event) { return this._post(ENDPOINTS.event, event); }
  postSessionEnd(sessionId, payload) { return this._post(ENDPOINTS.sessionEnd(sessionId), payload); }
  postReview(review) { return this._post(ENDPOINTS.review, review); }
}

/**
 * Durable upload queue. Local writes never wait on this; it drains in the
 * background, backs off on failure, and retries forever.
 */
export class UploadQueue {
  constructor({ store, backend, onchange = () => {}, maxBackoffMs = 30000 } = {}) {
    this.store = store;
    this.backend = backend;
    this.onchange = onchange;
    this.maxBackoffMs = maxBackoffMs;
    this._flushing = false;
    this._timer = 0;
  }

  setBackend(backend) {
    this.backend = backend;
    this.resetBackoff();
    this.flush();
  }

  start() {
    globalThis.addEventListener?.("online", () => {
      // Recovery is the one moment it is worth ignoring the backoff schedule.
      this.resetBackoff();
      this.flush();
    });
    this._timer = setInterval(() => this.flush(), 4000);
    this.flush();
  }

  stop() {
    clearInterval(this._timer);
    this._timer = 0;
  }

  pendingCount() {
    return this.store.queueList().length;
  }

  /** Forget accumulated backoff — used when the network or backend changes. */
  resetBackoff() {
    const items = this.store.queueList();
    if (!items.length) return;
    for (const item of items) {
      item.nextAt = 0;
      item.attempts = 0;
      item.lastError = null;
    }
    this.store.queueReplace(items);
    this.onchange();
  }

  lastError() {
    const items = this.store.queueList();
    const withError = items.filter((i) => i.lastError);
    return withError.length ? withError[withError.length - 1].lastError : null;
  }

  async flush() {
    if (this._flushing) return;
    this._flushing = true;
    try {
      let items = this.store.queueList();
      const now = Date.now();
      // Oldest first so events reach the server in the order they happened.
      items.sort((a, b) => (a.createdAt < b.createdAt ? -1 : 1));

      let changed = false;
      for (const item of items) {
        if (item.nextAt && item.nextAt > now) continue;
        try {
          await this._deliver(item);
          items = items.filter((i) => i.queueId !== item.queueId);
          changed = true;
        } catch (err) {
          item.attempts = (item.attempts || 0) + 1;
          item.lastError = err.message || String(err);
          const backoff = Math.min(this.maxBackoffMs, 800 * Math.pow(2, item.attempts - 1));
          item.nextAt = Date.now() + backoff + Math.random() * 400;
          changed = true;
          break; // don't hammer a dead server; keep ordering
        }
      }

      if (changed) {
        this.store.queueReplace(items);
        this.onchange();
      }
    } finally {
      this._flushing = false;
    }
  }

  _deliver(item) {
    switch (item.kind) {
      case "session": return this.backend.postSession(item.payload);
      case "event": return this.backend.postEvent(item.payload);
      case "session_end": return this.backend.postSessionEnd(item.payload.sessionId, item.payload);
      case "review": return this.backend.postReview(item.payload);
      default: return Promise.resolve();
    }
  }
}

export function makeBackend(settings) {
  if (settings.useMock) return new MockBackend({ failRate: Number(settings.mockFailRate) || 0 });
  return new HttpBackend({ baseUrl: settings.apiBaseUrl });
}

export function newId() { return uuid(); }
