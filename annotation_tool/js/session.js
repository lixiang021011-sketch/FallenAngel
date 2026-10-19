// Turns user gestures into raw events.
//
// This is the only place that decides what "an annotation" means, and it is
// deliberately dumb: it writes what happened, at the time it happened, and
// nothing else. No snapping, no smoothing, no reaction-time correction.
//
// Seek / restart / song end are the interesting case (spec section 8): the part
// intervals that are open when the playhead moves must be closed *before* the
// jump, flagged as automatic, with the reason recorded.

import { nowIso, roundMs, uuid } from "./util.js";

export class SessionRecorder {
  /**
   * @param locate optional `(seconds) => ({ bar, beat })`, from the song's
   *   analysis. Optional on purpose: a song with no analysis still records
   *   everything, it just has no musical coordinates to attach.
   */
  constructor({ store, sessionId, songId, onChange = () => {}, locate = null }) {
    this.store = store;
    this.sessionId = sessionId;
    this.songId = songId;
    this.onChange = onChange;
    this.locate = typeof locate === "function" ? locate : null;
    this.active = new Map(); // partId -> { eventId, audioTime }
  }

  activeParts() { return [...this.active.keys()]; }

  isActive(partId) { return this.active.has(partId); }

  activeSince(partId) { return this.active.get(partId)?.audioTime ?? null; }

  _write({ eventType, partId = null, audioTime, autoClosed, closeReason, ...extra }) {
    const event = {
      eventId: uuid(),
      sessionId: this.sessionId,
      songId: this.songId,
      audioTime: roundMs(audioTime),
      eventType,
      partId,
      clientTime: nowIso(),
    };
    if (autoClosed) {
      event.autoClosed = true;
      event.closeReason = closeReason ?? "unknown";
    }
    // Bar and beat are *derived* and only added when the song has timing. The
    // recorded time is never touched: a tap keeps the moment it happened, and a
    // reader who does not trust the machine's grid can ignore these two fields
    // and still have the raw data.
    if (this.locate) {
      try {
        const at = this.locate(event.audioTime);
        if (at && Number.isFinite(at.bar)) event.bar = at.bar;
        if (at && Number.isFinite(at.beat)) event.beat = roundMs(at.beat);
      } catch (err) {
        console.warn("[session] could not place the event in the bar grid", err);
      }
    }
    // Optional extras (seekTo, ...) are kept after the schema fields.
    for (const [k, v] of Object.entries(extra)) {
      if (v === undefined) continue;
      event[k] = typeof v === "number" ? roundMs(v) : v;
    }
    this.store.appendEvent(this.sessionId, event);
    return event;
  }

  /** Lets the UI mirror the recorder instead of guessing what a gesture did. */
  _fire() {
    try { this.onChange(this); } catch (err) { console.error("[session] onChange failed", err); }
  }

  /** Toggle a part. Returns the raw event that was written. */
  togglePart(partId, audioTime) {
    if (this.active.has(partId)) {
      const event = this._write({ eventType: "attention_out", partId, audioTime });
      this.active.delete(partId);
      this._fire();
      return event;
    }
    const event = this._write({ eventType: "attention_in", partId, audioTime });
    this.active.set(partId, { eventId: event.eventId, audioTime: event.audioTime });
    this._fire();
    return event;
  }

  /**
   * Close every open interval because the playhead is about to move (or the
   * session is about to end). Returns the events written, in order.
   */
  closeAll(reason, audioTime) {
    const events = [];
    for (const partId of [...this.active.keys()]) {
      events.push(this._write({
        eventType: "attention_out",
        partId,
        audioTime,
        autoClosed: true,
        closeReason: reason,
      }));
      this.active.delete(partId);
    }
    if (events.length) this._fire();
    return events;
  }

  transport(eventType, audioTime, extra = {}) {
    const event = this._write({ eventType, partId: null, audioTime, ...extra });
    this._fire();
    return event;
  }

  /**
   * "这里换段" — the listener says a new section starts here.
   *
   * The only structural question this asks of a person, and it deliberately does
   * not ask for a name: verse / chorus / bridge is a taxonomy to argue about
   * later, while "is this a new section" is answerable in one tap and is what a
   * boundary predictor can be scored against.
   */
  sectionBoundary(audioTime) {
    const event = this._write({
      eventType: "SECTION_BOUNDARY",
      partId: null,
      audioTime,
      source: "human",
    });
    this._fire();
    return event;
  }

  /**
   * The listener's verdict on a machine boundary candidate.
   *
   * A human decision about machine data, so it is a human event carrying the
   * candidate it refers to. Ignoring it is a decision too, and is recorded as
   * one - "I looked and it is not interesting" is not the same as never having
   * been asked.
   */
  boundaryReview(candidateId, decision, audioTime) {
    const allowed = ["accept", "reject", "ignore"];
    if (!allowed.includes(decision)) {
      throw new Error(`unknown decision "${decision}" (expected ${allowed.join(" / ")})`);
    }
    const event = this._write({
      eventType: "BOUNDARY_REVIEW",
      partId: null,
      audioTime,
      candidateId: candidateId ?? null,
      decision,
      source: "human",
    });
    this._fire();
    return event;
  }

  /**
   * The player wrapped a loop.
   *
   * Recorded, and deliberately *not* treated as a seek: nothing is closed, and
   * the session does not end. A loop is the same music again, so attention that
   * was held across the wrap really did continue. What the event is for is the
   * opposite: so that a reader does not mistake the end of a loop for the
   * listener having listened straight through to the beginning.
   */
  loopJump(from, to, audioTime) {
    const event = this._write({
      eventType: "LOOP_JUMP",
      partId: null,
      audioTime: audioTime ?? to,
      from: roundMs(from),
      to: roundMs(to),
    });
    this._fire();
    return event;
  }

  /**
   * Wire an AudioEngine to this recorder.
   *
   * The order matters: for `seek` and `restart` the auto-closed outs are
   * written at the pre-jump position and *before* the transport event, so a
   * reader replaying the log in order sees attention stop, then the playhead
   * move.
   */
  attach(engine) {
    const offs = [];

    offs.push(engine.on("play", (e) => {
      this.transport("play", e.audioTime);
    }));

    offs.push(engine.on("pause", (e) => {
      this.transport("pause", e.audioTime);
    }));

    offs.push(engine.on("seek", (e) => {
      this.closeAll("seek", e.from);
      // audioTime is where the user was when they acted; seekTo records where
      // they went. Both are kept — replaying a region is itself signal.
      this.transport("seek", e.from, { seekTo: roundMs(e.to) });
    }));

    offs.push(engine.on("restart", (e) => {
      this.closeAll("restart", e.from);
      this.transport("restart", e.from);
    }));

    offs.push(engine.on("loop", (e) => {
      this.loopJump(e.from, e.to, e.at ?? e.to);
    }));

    return () => offs.forEach((off) => off());
  }
}
