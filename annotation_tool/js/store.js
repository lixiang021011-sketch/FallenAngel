// Local-first session store.
//
// Everything the annotator produces is written here *first*. Network upload is
// a separate, retryable concern (see api.js). Losing the server must never cost
// the listener a single event. See spec sections 5, 10, 11.
//
// Backed by localStorage: an hour of listening is a few hundred events, well
// inside quota, and synchronous writes are what we want during playback.

import { nowIso, roundMs, uuid } from "./util.js";
import { buildRegions, pairIntervals } from "./derive.js";

const STORAGE_KEY = "fa_annot_v1";

function blank() {
  return {
    version: 1,
    sessions: {},
    events: {},
    reviews: {},
    /**
     * Machine observations, by song id: the analysis Workbench produced.
     *
     * A separate layer on purpose. Everything in `events` is something a person
     * did; everything in here is something a machine said, and nothing may move
     * between the two. It is stored (so the structure UI works offline) and it is
     * read-only (so a re-export of a session carries the document it came from,
     * not a copy that has drifted).
     */
    analysis: {},
    queue: [],
    settings: {
      useMock: true,
      apiBaseUrl: "",
      mockFailRate: 0,
    },
  };
}

function read() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return blank();
    const data = JSON.parse(raw);
    const base = blank();
    return {
      ...base,
      ...data,
      settings: { ...base.settings, ...(data.settings || {}) },
      sessions: data.sessions || {},
      events: data.events || {},
      reviews: data.reviews || {},
      analysis: data.analysis || {},
      queue: Array.isArray(data.queue) ? data.queue : [],
    };
  } catch (err) {
    console.warn("[store] unreadable local state, starting fresh", err);
    return blank();
  }
}

export class Store {
  constructor() {
    this.data = read();
    this._listeners = new Set();
  }

  // ------------------------------------------------------------------ plumbing

  _commit() {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.data));
    } catch (err) {
      console.error("[store] write failed", err);
      throw new Error("Local storage is full or unavailable. Export your session before continuing.");
    }
    for (const fn of this._listeners) {
      try { fn(this); } catch (err) { console.error(err); }
    }
  }

  subscribe(fn) {
    this._listeners.add(fn);
    return () => this._listeners.delete(fn);
  }

  get settings() { return this.data.settings; }

  patchSettings(patch) {
    Object.assign(this.data.settings, patch);
    this._commit();
    return this.data.settings;
  }

  // ------------------------------------------------------------------ sessions

  createSession({ songId, songTitle, parts, duration, sessionId }) {
    const id = sessionId || uuid();
    const startedAt = nowIso();
    const session = {
      sessionId: id,
      songId,
      songTitle,
      parts: parts || [],
      songDuration: roundMs(duration || 0),
      startedAt,
      endedAt: null,
      endReason: null,
      client: "annotation_tool/0.1",
    };
    this.data.sessions[id] = session;
    this.data.events[id] = this.data.events[id] || [];
    this.data.reviews[id] = this.data.reviews[id] || [];
    this._commit();
    this.enqueue({ kind: "session", payload: session });
    return session;
  }

  getSession(sessionId) {
    return this.data.sessions[sessionId] || null;
  }

  listSessions() {
    return Object.values(this.data.sessions).sort((a, b) => (a.startedAt < b.startedAt ? 1 : -1));
  }

  endSession(sessionId, { audioTime, reason }) {
    const session = this.data.sessions[sessionId];
    if (!session) return null;
    session.endedAt = nowIso();
    session.endReason = reason;
    session.endedAtAudioTime = roundMs(audioTime || 0);
    this._commit();
    this.enqueue({
      kind: "session_end",
      payload: {
        sessionId,
        endedAt: session.endedAt,
        endReason: reason,
        endedAtAudioTime: session.endedAtAudioTime,
      },
    });
    return session;
  }

  // -------------------------------------------------------------------- events

  appendEvent(sessionId, event) {
    const list = this.data.events[sessionId];
    if (!list) throw new Error(`unknown session ${sessionId}`);
    const record = { ...event, sessionId };
    list.push(record);
    this._commit();
    this.enqueue({ kind: "event", payload: record });
    return record;
  }

  listEvents(sessionId) {
    return [...(this.data.events[sessionId] || [])];
  }

  deleteSession(sessionId) {
    delete this.data.sessions[sessionId];
    delete this.data.events[sessionId];
    delete this.data.reviews[sessionId];
    this.data.queue = this.data.queue.filter((item) => item.payload?.sessionId !== sessionId);
    this._commit();
  }

  // ------------------------------------------------------------------- reviews

  // ------------------------------------------------------------------- machine

  /** Attach a song's analysis. It replaces any previous one: same song, new run. */
  attachAnalysis(songId, analysis) {
    if (!songId) throw new Error("an analysis needs a song to belong to");
    if (analysis) this.data.analysis[songId] = analysis;
    else delete this.data.analysis[songId];
    this._commit();
    return this.data.analysis[songId] || null;
  }

  getAnalysis(songId) {
    return this.data.analysis[songId] || null;
  }

  listAnalysedSongs() {
    return Object.keys(this.data.analysis);
  }

  saveReview(sessionId, review) {
    const list = this.data.reviews[sessionId] || (this.data.reviews[sessionId] = []);
    const note = String(review.note || "");
    const idx = list.findIndex((r) => r.regionId === review.regionId);
    const record = {
      reviewId: idx >= 0 ? list[idx].reviewId : uuid(),
      sessionId,
      regionId: review.regionId,
      start: roundMs(review.start),
      end: roundMs(review.end),
      partIds: review.partIds || [],
      note,
      updatedAt: nowIso(),
    };
    if (idx >= 0) list[idx] = record;
    else list.push(record);
    this._commit();
    if (note.trim()) this.enqueue({ kind: "review", payload: record });
    return record;
  }

  listReviews(sessionId) {
    return [...(this.data.reviews[sessionId] || [])];
  }

  findReview(sessionId, regionId) {
    return (this.data.reviews[sessionId] || []).find((r) => r.regionId === regionId) || null;
  }

  // --------------------------------------------------------------------- queue

  queueList() {
    return [...this.data.queue];
  }

  queueReplace(items) {
    this.data.queue = items;
    this._commit();
  }

  enqueue({ kind, payload }) {
    const item = {
      queueId: uuid(),
      kind,
      payload,
      attempts: 0,
      nextAt: 0,
      lastError: null,
      createdAt: nowIso(),
    };
    this.data.queue.push(item);
    this._commit();
    return item;
  }

  // ------------------------------------------------------------------- exports

  exportSession(sessionId) {
    const session = this.getSession(sessionId);
    if (!session) return null;
    const rawEvents = this.listEvents(sessionId);
    const reviews = this.listReviews(sessionId);

    // Derived through the same code path the review screen uses, so the export
    // can never disagree with what the user saw.
    return {
      schema: "fallenangel.attention.session/0.1",
      exportedAt: nowIso(),
      session,
      rawEvents,
      // The machine's view of the same song, beside the human record rather than
      // inside it. Present even when empty, so a reader can tell "no analysis was
      // attached" from "this export predates the field".
      machineObservations: {
        songId: session.songId,
        analysis: this.getAnalysis(session.songId),
      },
      derivedIntervals: pairIntervals(rawEvents).intervals,
      derivedRegions: buildRegions(rawEvents),
      reviews,
    };
  }

  exportEverything() {
    return {
      schema: "fallenangel.attention.backup/0.1",
      exportedAt: nowIso(),
      ...this.data,
    };
  }

  clearAll() {
    this.data = blank();
    this._commit();
  }
}
