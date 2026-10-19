// Playback for one master + N stems, all sharing a single song clock.
//
// The master element is always the clock; stems are followers that get nudged
// back into place. Full mix = master audible, stems muted. Solo = one stem
// audible, master muted. Stems keep running either way, so switching between
// mix and solo during review never costs a re-seek.

import { clamp, formatClock } from "./util.js";

const DRIFT_TOLERANCE = 0.12; // seconds
const DRIFT_POLL_MS = 250;

/** How early a loop wraps, so a late frame cannot let the next bar be heard. */
export const LOOP_EARLY = 0.05;

/**
 * Whether the playhead has reached the end of a range.
 *
 * Kept as a plain function because it is the one piece of loop behaviour that
 * can be wrong without being visible: a loop that wraps late plays the first
 * note of the next bar every time round, which sounds like the music rather
 * than like a bug.
 */
export function shouldWrap(range, currentTime, { tolerance = LOOP_EARLY } = {}) {
  if (!range) return false;
  const { from, to } = range;
  if (!Number.isFinite(from) || !Number.isFinite(to)) return false;
  if (!(to > from)) return false;
  if (!Number.isFinite(currentTime)) return false;
  return currentTime >= to - tolerance;
}

class Emitter {
  constructor() { this._map = new Map(); }
  on(name, fn) {
    if (!this._map.has(name)) this._map.set(name, new Set());
    this._map.get(name).add(fn);
    return () => this.off(name, fn);
  }
  off(name, fn) { this._map.get(name)?.delete(fn); }
  emit(name, payload) {
    for (const fn of this._map.get(name) || []) {
      try { fn(payload); } catch (err) { console.error(`[audio] listener for ${name} failed`, err); }
    }
  }
}

/**
 * Maps `performance.now()` to media time. rAF samples are ~16 ms apart, which
 * is far better than reading `currentTime` at tap time (browsers update it
 * lazily), and it is still a *raw* timestamp: nothing is snapped or shifted.
 */
export class MediaClock {
  constructor(media) {
    this.media = media;
    this.samples = [];
    this._raf = 0;
  }

  start() {
    if (this._raf) return;
    const tick = () => {
      this.sample();
      this._raf = requestAnimationFrame(tick);
    };
    this._raf = requestAnimationFrame(tick);
  }

  stop() {
    if (this._raf) cancelAnimationFrame(this._raf);
    this._raf = 0;
  }

  sample() {
    const m = this.media;
    if (!m) return;
    this.samples.push({ p: performance.now(), m: m.currentTime, playing: !m.paused && !m.ended });
    if (this.samples.length > 180) this.samples.shift();
  }

  reset() {
    this.samples.length = 0;
    this.sample();
  }

  timeAt(perf) {
    const m = this.media;
    if (!m) return 0;
    const last = this.samples[this.samples.length - 1];
    // rAF is suspended when the tab is hidden; fall back to the element.
    if (!last || perf - last.p > 1000) return m.currentTime;

    let s = null;
    for (let i = this.samples.length - 1; i >= 0; i--) {
      if (this.samples[i].p <= perf) { s = this.samples[i]; break; }
    }
    if (!s) s = this.samples[0];

    let t = s.m;
    if (s.playing) t += (perf - s.p) / 1000;
    const d = m.duration;
    if (Number.isFinite(d) && d > 0) t = Math.min(t, d);
    return clamp(t, 0, Number.isFinite(d) && d > 0 ? d : 1e9);
  }
}

export class AudioEngine extends Emitter {
  constructor() {
    super();
    this.song = null;
    this.master = null;
    this.stems = new Map(); // partId -> HTMLAudioElement
    this.clock = null;
    this.mode = { type: "full" };
    /**
     * The range being repeated, if any: `{ from, to, label, repeat }`.
     *
     * `repeat: false` is a one-shot range (replay a segment): the playhead is
     * put at `from`, playback runs to `to`, and then it simply carries on -
     * which is what "otherwise keep playing" means. Only a repeating loop emits
     * a jump, because only a repeating loop actually moved the music back.
     */
    this.loop = null;
    this._loopRaf = 0;
    this._urls = [];
    this._driftTimer = 0;
    this._loaded = false;
  }

  get ready() { return this._loaded; }
  get duration() { return this.master && Number.isFinite(this.master.duration) ? this.master.duration : 0; }
  get currentTime() { return this.master ? this.master.currentTime : 0; }
  get paused() { return this.master ? this.master.paused : true; }
  get masteredParts() { return [...this.stems.keys()]; }

  async load(song) {
    this.unload();
    this.song = song;

    const host = document.createElement("div");
    host.style.display = "none";
    host.dataset.role = "audio-host";
    document.body.append(host);
    this._host = host;

    const make = (blob) => {
      const url = URL.createObjectURL(blob);
      this._urls.push(url);
      const a = document.createElement("audio");
      a.src = url;
      a.preload = "auto";
      a.crossOrigin = "anonymous";
      a.setAttribute("playsinline", "");
      host.append(a);
      return a;
    };

    this.master = make(song.files.master);
    for (const [partId, blob] of Object.entries(song.files.stems || {})) {
      this.stems.set(partId, make(blob));
    }

    await waitForMetadata(this.master);
    this.clock = new MediaClock(this.master);
    this._wire();
    this._applyMode();
    this._loaded = true;
    this.emit("loaded", { duration: this.duration, parts: this.masteredParts });
    return this.duration;
  }

  _wire() {
    const m = this.master;
    this._onSeeked = () => {
      this._syncFollowers(true);
      this.clock.reset();
    };
    m.addEventListener("seeked", this._onSeeked);
    this._onEnded = () => {
      this.emit("ended", { audioTime: this.duration });
    };
    m.addEventListener("ended", this._onEnded);
    this._onTimeUpdate = () => {
      this.emit("time", { currentTime: m.currentTime, duration: this.duration, paused: m.paused });
    };
    m.addEventListener("timeupdate", this._onTimeUpdate);
    m.addEventListener("loadedmetadata", () => this.emit("meta"));
    m.addEventListener("error", () => this.emit("error", { audioTime: m.currentTime }));

    this._driftTimer = setInterval(() => this._syncFollowers(false), DRIFT_POLL_MS);
  }

  // ------------------------------------------------------------------- ranges

  /** Repeat `from`..`to`. The player only; nothing here writes an annotation. */
  setLoop({ from, to, label = "", repeat = true }) {
    if (!Number.isFinite(from) || !Number.isFinite(to) || !(to > from)) return false;
    this.loop = { from, to, label, repeat: repeat !== false };
    this._startLoopGuard();
    this.emit("loopchange", { ...this.loop });
    return true;
  }

  clearLoop() {
    if (!this.loop) return;
    this.loop = null;
    this.emit("loopchange", null);
  }

  /**
   * Play a range once, from its start.
   *
   * Used for "replay this segment": the playhead moves to the start (silently -
   * it is the app moving it, not the listener) and the range ends by continuing
   * unless something repeats it.
   */
  async playRange(from, to, { repeat = false } = {}) {
    if (!this.master) return;
    this.seekTo(from);
    this.setLoop({ from, to, repeat });
    if (this.master.paused) await this.play();
  }

  /** rAF while a range is active: 16 ms of resolution instead of 250. */
  _startLoopGuard() {
    if (this._loopRaf) return;
    const tick = () => {
      if (!this.loop || !this.master) {
        this._loopRaf = 0;
        return;
      }
      if (shouldWrap(this.loop, this.master.currentTime)) this._wrapLoop();
      this._loopRaf = requestAnimationFrame(tick);
    };
    this._loopRaf = requestAnimationFrame(tick);
  }

  _wrapLoop() {
    const range = this.loop;
    if (!range) return;
    const at = clamp(range.to, 0, this.duration || range.to);
    if (range.repeat) {
      this.seekTo(range.from);
      this.emit("loop", { from: at, to: range.from, at, label: range.label });
    } else {
      this.loop = null;
      this.emit("rangeend", { at, label: range.label });
      this.emit("loopchange", null);
    }
  }

  _syncFollowers(force) {
    if (!this.master) return;
    const t = this.master.currentTime;
    for (const a of this.stems.values()) {
      if (!a || a.readyState < 1) continue;
      if (force || Math.abs(a.currentTime - t) > DRIFT_TOLERANCE) {
        try { a.currentTime = t; } catch { /* not seekable yet */ }
      }
    }
  }

  _applyMode() {
    const soloing = this.mode.type === "solo" ? this.mode.partId : null;
    if (this.master) this.master.volume = soloing ? 0 : 1;
    for (const [partId, a] of this.stems) {
      a.volume = soloing === partId ? 1 : 0;
    }
  }

  async play() {
    if (!this.master) return;
    const tasks = [this.master.play()];
    for (const a of this.stems.values()) tasks.push(a.play().catch(() => {}));
    await Promise.all(tasks).catch((err) => {
      console.warn("[audio] play blocked", err);
      throw err;
    });
    this.clock.sample();
    this.emit("play", { audioTime: this.currentTime });
  }

  pause() {
    if (!this.master) return;
    this.master.pause();
    for (const a of this.stems.values()) a.pause();
    this.emit("pause", { audioTime: this.currentTime });
  }

  toggle() {
    if (this.paused) return this.play();
    this.pause();
    return Promise.resolve();
  }

  seek(to) {
    if (!this.master) return;
    const from = this.master.currentTime;
    this.seekTo(to);
    this.emit("seek", { from, to: this.master.currentTime });
  }

  /** Move the playhead without announcing it (scrub preview, region replay). */
  seekTo(to) {
    if (!this.master) return;
    const target = clamp(to, 0, this.duration || 0);
    this.master.currentTime = target;
    this._syncFollowers(true);
    this.clock.reset();
    this.emit("time", { currentTime: target, duration: this.duration, paused: this.master.paused });
    return target;
  }

  async restart() {
    if (!this.master) return;
    const from = this.master.currentTime;
    this.seekTo(0);
    this.emit("restart", { from, to: 0 });
    if (this.master.paused) await this.play();
  }

  setMode(mode) {
    this.mode = mode;
    this._applyMode();
    this.emit("modechange", mode);
  }

  playFullMix() { this.setMode({ type: "full" }); }

  soloPart(partId) {
    if (!this.stems.has(partId)) return false;
    this.setMode({ type: "solo", partId });
    return true;
  }

  unload() {
    clearInterval(this._driftTimer);
    this._driftTimer = 0;
    this.loop = null;
    if (this._loopRaf) {
      cancelAnimationFrame(this._loopRaf);
      this._loopRaf = 0;
    }
    if (this.master) {
      this.master.removeEventListener("seeked", this._onSeeked);
      this.master.removeEventListener("ended", this._onEnded);
      this.master.removeEventListener("timeupdate", this._onTimeUpdate);
      try { this.master.pause(); } catch { /* ignore */ }
    }
    for (const a of this.stems.values()) { try { a.pause(); } catch { /* ignore */ } }
    this.stems.clear();
    this.master = null;
    this.clock?.stop();
    this.clock = null;
    this._host?.remove();
    this._host = null;
    this._urls.forEach((u) => URL.revokeObjectURL(u));
    this._urls = [];
    this._loaded = false;
    this.song = null;
    this.mode = { type: "full" };
  }
}

function waitForMetadata(audio, timeoutMs = 15000) {
  if (audio.readyState >= 1) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const done = () => { cleanup(); resolve(); };
    const fail = () => { cleanup(); reject(new Error("Could not decode the master audio file.")); };
    const timer = setTimeout(fail, timeoutMs);
    const cleanup = () => {
      clearTimeout(timer);
      audio.removeEventListener("loadedmetadata", done);
      audio.removeEventListener("error", fail);
    };
    audio.addEventListener("loadedmetadata", done, { once: true });
    audio.addEventListener("error", fail, { once: true });
  });
}

export function describeMode(mode) {
  if (!mode) return "";
  if (mode.type === "full") return "Full mix";
  return `${mode.partId} solo`;
}

export function clockLabel(t, d) {
  return `${formatClock(t)} / ${formatClock(d)}`;
}
