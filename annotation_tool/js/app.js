// Wiring. Owns the three screens and the small amount of state between them.

import { AudioEngine, describeMode } from "./audio.js";
import { SessionRecorder } from "./session.js";
import { AudioLibrary, classifyFiles, isAudioFile, partsForSong, suggestTitle } from "./library.js";
import { Store } from "./store.js";
import { makeBackend, UploadQueue } from "./api.js";
import { buildRegions, pairIntervals } from "./derive.js";
import { pickNextUnannotated, summarizeLibrary } from "./progress.js";
import { debounce, downloadJson, el, formatClock, roundMs } from "./util.js";
import { analysisSummary, parseAnalysis } from "./analysis.js";
import {
  makeTiming, locate, prevBarTarget, nextBarTarget, rangeOfBars,
  segmentAt, segmentRange, boundaryNear, similarOf,
} from "./timing.js";
import * as V from "./views.js";

/** How long the review screen waits before rolling into the next song. */
const AUTO_NEXT_SECONDS = 8;

const state = {
  screen: "library",
  song: null,
  session: null,
  recorder: null,
  detachRecorder: null,
  reviewSessionId: null,
  selectedRegionId: null,
  reviewRangeEnd: null,
  reviewRaf: 0,
  wakeLock: null,
  pendingFiles: null,
  partGrid: null,
  seeking: false,
  nextSong: null,
  autoNext: null,
  // The Workbench layer for the song being listened to. Read-only: nothing in
  // here is ever written, and the recorder only borrows `locate` from it.
  analysis: null,
  timing: null,
  machineCandidate: null,
  reviewedCandidates: new Set(),
  structureRaf: 0,
  structureDrawnAt: 0,
};

const store = new Store();
const library = new AudioLibrary();
const engine = new AudioEngine();
let backend = makeBackend(store.settings);
const queue = new UploadQueue({ store, backend, onchange: () => { renderStatus(); } });

// --------------------------------------------------------------------- helpers

const $ = (id) => document.getElementById(id);

function show(screen) {
  state.screen = screen;
  for (const node of document.querySelectorAll(".view")) {
    node.classList.toggle("is-active", node.dataset.view === screen);
  }
}

function audioTimeNow(perfTime) {
  if (!engine.ready || !engine.clock) return 0;
  return engine.clock.timeAt(perfTime ?? performance.now());
}

function renderStatus() {
  const net = $("net-status");
  const q = $("queue-status");
  if (!net || !q) return;
  const online = navigator.onLine;
  net.textContent = online ? "online" : "offline \u2014 recording locally";
  net.className = `chip ${online ? "ok" : "bad"}`;

  const pending = queue.pendingCount();
  const err = queue.lastError();
  q.textContent = pending ? `queue ${pending}${err ? " \u00b7 retrying" : ""}` : "queue 0 \u00b7 saved";
  q.className = `chip ${err ? "bad" : pending ? "" : "ok"}`;
}

async function requestWakeLock() {
  try {
    state.wakeLock = await navigator.wakeLock?.request("screen");
  } catch { /* not supported / denied — harmless */ }
}

function releaseWakeLock() {
  try { state.wakeLock?.release(); } catch { /* ignore */ }
  state.wakeLock = null;
}

// --------------------------------------------------------------------- library

async function refreshLibrary() {
  const songs = await library.list();
  const finished = store.listSessions().filter((s) => s.endedAt);
  const annotated = new Set(finished.map((s) => s.songId));
  V.renderProgress($("library-progress"), summarizeLibrary(songs, store.listSessions()));
  V.renderSongList($("song-list"), songs, {
    annotated,
    onStart: (song) => startSession(song),
    onDelete: async (song) => {
      await library.remove(song.id);
      refreshLibrary();
    },
  });

  V.renderSessionList($("session-list"), finished, {
    counts: (id) => ({
      events: store.listEvents(id).length,
      regions: buildRegions(store.listEvents(id)).length,
    }),
    onOpen: (session) => openPastSession(session),
    onDelete: (session) => {
      store.deleteSession(session.sessionId);
      refreshLibrary();
      renderStatus();
    },
  });
}

/** Re-open a finished session: load its song back into the player, then review. */
async function openPastSession(session) {
  stopReviewPlayback();
  const song = await library.get(session.songId);
  if (song) {
    try {
      await engine.load(song);
      state.song = song;
    } catch (err) {
      console.error(err);
      V.toast("Could not load that song's audio; the session is still readable.", true);
    }
  } else {
    engine.unload();
    V.toast("That song is no longer in the library \u2014 showing the session without playback.", true);
  }
  state.reviewSessionId = session.sessionId;
  state.selectedRegionId = null;
  renderReviewNow();
  show("review");
}

function wireAddPanel() {
  const titleInput = $("add-title");
  const preview = $("add-preview");
  const addBtn = $("btn-add-song");
  const fileInput = $("file-input");
  const folderInput = $("folder-input");

  const accept = (files) => {
    const list = [...files].filter((f) => f.type.startsWith("audio/") || /\.(wav|mp3|m4a|aac|ogg|oga|flac)$/i.test(f.name));
    if (!list.length) { V.toast("No audio files in that selection.", true); return; }
    state.pendingFiles = list;
    const sel = classifyFiles(list);
    V.renderPreview(preview, sel);
    if (!titleInput.value.trim()) titleInput.value = suggestTitle(list);
    addBtn.disabled = !sel.masterFile;
  };

  $("btn-pick-files").addEventListener("click", () => fileInput.click());
  $("btn-pick-folder").addEventListener("click", () => folderInput.click());
  fileInput.addEventListener("change", () => accept(fileInput.files));
  folderInput.addEventListener("change", () => accept(folderInput.files));

  addBtn.addEventListener("click", async () => {
    if (!state.pendingFiles) return;
    addBtn.disabled = true;
    addBtn.textContent = "Measuring\u2026";
    try {
      const duration = await measureDuration(state.pendingFiles, classifyFiles(state.pendingFiles).masterFile);
      const song = await library.addFromFiles({
        title: titleInput.value,
        files: state.pendingFiles,
        duration,
      });
      state.pendingFiles = null;
      fileInput.value = "";
      folderInput.value = "";
      titleInput.value = "";
      V.renderPreview(preview, null);
      V.toast(`Added "${song.title}"`);
      refreshLibrary();
    } catch (err) {
      console.error(err);
      V.toast(err.message || "Could not add that song.", true);
    } finally {
      addBtn.textContent = "Add to library";
      addBtn.disabled = true;
    }
  });

  $("btn-load-demo").addEventListener("click", async () => {
    try {
      const added = await library.addFromManifest("demo/manifest.json", {
        // The demo carries its analysis in the manifest; it goes straight into
        // the machine layer, where every other analysis lives.
        onAnalysis: (songId, analysis) => store.attachAnalysis(songId, analysis),
      });
      V.toast(added.length ? `Loaded ${added.length} demo song` : "Manifest had no songs.", !added.length);
      refreshLibrary();
    } catch (err) {
      console.error(err);
      V.toast("Demo song is not available. Run tools/make_demo_song.py first.", true);
    }
  });

  $("btn-settings").addEventListener("click", openSettings);
}

/**
 * Attaching a Workbench analysis.
 *
 * Matched to a song by the `songId` inside the file, and by title when that
 * matches nothing - because a Workbench run usually knows a song by its name
 * long before the library here has given it an id. When neither matches, say
 * what the file claims rather than attaching it to the wrong song.
 */
function wireAnalysisPanel() {
  const input = $("analysis-input");
  const button = $("btn-pick-analysis");
  if (!input || !button) return;
  button.addEventListener("click", () => input.click());
  input.addEventListener("change", async () => {
    const file = input.files && input.files[0];
    input.value = "";
    if (!file) return;
    const { analysis, problems } = parseAnalysis(await file.text());
    const status = $("analysis-status");
    if (!analysis) {
      if (status) status.textContent = problems.join("; ");
      V.toast(problems[0] || "Could not read that analysis.", true);
      return;
    }
    const songs = await library.list();
    const wanted = String(analysis.songId || "").toLowerCase();
    const target = songs.find((s) => s.id === analysis.songId)
      || (wanted ? songs.find((s) => String(s.title || "").toLowerCase().includes(wanted)) : null);
    if (!target) {
      const names = songs.slice(0, 5).map((s) => s.title).join(", ");
      const message = `No song matches songId "${analysis.songId ?? "(missing)"}". In the library: ${names || "nothing yet"}.`;
      if (status) status.textContent = message;
      V.toast(message, true);
      return;
    }
    store.attachAnalysis(target.id, analysis);
    const summary = analysisSummary(analysis);
    const detail = `${summary.segments} segments \u00b7 ${summary.boundaries} boundaries${summary.hasTiming ? "" : " \u00b7 no timing"}`;
    if (status) status.textContent = `Attached to ${target.title}: ${detail}`;
    V.toast(`Analysis attached to ${target.title} (${detail})${problems.length ? ` \u2014 ${problems.length} problem(s)` : ""}`,
      problems.length > 0);
    refreshLibrary();
  });
}

// ------------------------------------------------------- bulk library import

/**
 * Import a whole library in one go: every file becomes its own song. This is
 * the path for "listen to a lot of music and build a database", so it accepts
 * hundreds of files, reports progress, skips duplicates and stops cleanly if
 * the browser's storage runs out.
 */
function wireBulkPanel() {
  const fileInput = $("bulk-file-input");
  const folderInput = $("bulk-folder-input");
  const status = $("bulk-status");
  const bar = $("bulk-bar");
  const fill = bar.querySelector("i");
  let busy = false;

  const setStatus = (text) => { status.textContent = text; };

  const run = async (files) => {
    if (busy) return;
    const list = [...files].filter(isAudioFile);
    if (!list.length) { V.toast("No audio files in that selection.", true); return; }

    busy = true;
    bar.hidden = false;
    fill.style.width = "0%";
    setStatus(`Importing ${list.length} files\u2026`);
    await library.requestPersistence();

    try {
      const result = await library.addManyFromFiles(list, {
        onProgress: (p) => {
          const pct = p.total ? Math.round((p.done / p.total) * 100) : 0;
          fill.style.width = `${pct}%`;
          setStatus(`Importing ${p.done} / ${p.total} \u00b7 added ${p.added}`);
        },
      });

      const bits = [`Added ${result.added}`];
      if (result.skipped) bits.push(`${result.skipped} already in the library`);
      if (result.failures.length) bits.push(`${result.failures.length} failed`);
      setStatus(bits.join(" \u00b7 "));
      V.toast(bits.join(" \u00b7 "), result.quotaHit || result.failures.length > 0);

      if (result.quotaHit) {
        V.openModal((card) => {
          card.append(el("h3", { text: "Out of browser storage" }));
          card.append(el("p", {
            class: "muted small",
            text: `Imported ${result.added} of ${result.considered} files before the browser's storage limit was hit. The rest were not saved. Delete songs you have already annotated, or use a smaller selection.`,
          }));
          const close = el("button", { class: "btn wide", type: "button", text: "Close" });
          close.addEventListener("click", V.closeModal);
          card.append(close);
        });
      }
      const usage = await library.usage();
      if (usage?.quota) {
        setStatus(`${status.textContent} \u00b7 ${mb(usage.usage)} of ${mb(usage.quota)} used`);
      }
      await refreshLibrary();
    } catch (err) {
      console.error(err);
      setStatus(`Import failed: ${err.message || err}`);
      V.toast(err.message || "Import failed.", true);
    } finally {
      busy = false;
      fileInput.value = "";
      folderInput.value = "";
    }
  };

  $("btn-bulk-files").addEventListener("click", () => fileInput.click());
  $("btn-bulk-folder").addEventListener("click", () => folderInput.click());
  fileInput.addEventListener("change", () => run(fileInput.files));
  folderInput.addEventListener("change", () => run(folderInput.files));
}

function mb(bytes) {
  return `${Math.round(Number(bytes || 0) / 1048576)} MB`;
}

function measureDuration(files, masterFile) {
  return new Promise((resolve) => {
    if (!masterFile) { resolve(0); return; }
    const url = URL.createObjectURL(masterFile);
    const a = document.createElement("audio");
    const done = (value) => {
      URL.revokeObjectURL(url);
      a.remove();
      resolve(value);
    };
    const timer = setTimeout(() => done(0), 8000);
    a.addEventListener("loadedmetadata", () => { clearTimeout(timer); done(Number.isFinite(a.duration) ? a.duration : 0); });
    a.addEventListener("error", () => { clearTimeout(timer); done(0); });
    a.src = url;
  });
}

// -------------------------------------------------------------------- settings

function openSettings() {
  V.openModal((card) => {
    const s = store.settings;
    card.append(el("h3", { text: "Settings" }));

    const useMock = el("input", { type: "checkbox" });
    useMock.checked = !!s.useMock;
    card.append(el("label", { class: "field" }, [
      el("span", { text: "Use the built-in mock backend (nothing leaves this device)" }),
      useMock,
    ]));

    const base = el("input", { type: "url", placeholder: "https://example.com", autocomplete: "off" });
    base.value = s.apiBaseUrl || "";
    card.append(el("label", { class: "field" }, [el("span", { text: "Real API base URL" }), base]));

    const fail = el("select", {});
    for (const pct of [0, 0.25, 0.5, 0.9, 1]) {
      fail.append(el("option", { value: String(pct), text: `${pct * 100}% of uploads fail` }));
    }
    fail.value = String(s.mockFailRate || 0);
    card.append(el("label", { class: "field" }, [
      el("span", { text: "Mock failure rate (test the offline queue)" }),
      fail,
    ]));

    const apply = el("button", { class: "btn primary wide", type: "button", text: "Apply" });
    apply.addEventListener("click", () => {
      const patch = {
        useMock: useMock.checked,
        apiBaseUrl: base.value.trim(),
        mockFailRate: Number(fail.value) || 0,
      };
      store.patchSettings(patch);
      backend = makeBackend(patch);
      queue.setBackend(backend);
      V.toast(`Backend: ${backend.name}${backend.name === "mock" ? ` (fail ${patch.mockFailRate * 100}%)` : ""}`);
      renderStatus();
      V.closeModal();
    });
    card.append(apply);

    const row = el("div", { class: "row" });
    const backup = el("button", { class: "btn ghost", type: "button", text: "Download all data" });
    backup.addEventListener("click", () => {
      downloadJson(`attention-backup-${Date.now()}.json`, store.exportEverything());
    });
    const inspect = el("button", { class: "btn ghost", type: "button", text: "Inspect mock server" });
    inspect.addEventListener("click", () => {
      const db = JSON.parse(localStorage.getItem("fa_annot_mockdb") || "{}");
      const events = Object.keys(db.events || {}).length;
      V.toast(`Mock server holds ${events} events`);
    });
    row.append(backup, inspect);
    card.append(row);

    const wipe = el("button", { class: "btn ghost wide", type: "button", text: "Erase all local sessions" });
    wipe.style.color = "var(--danger)";
    wipe.addEventListener("click", () => {
      if (!confirm("Delete every local session, event, note and queued upload?")) return;
      store.clearAll();
      V.closeModal();
      refreshLibrary();
      renderStatus();
      V.toast("Local data erased");
    });
    card.append(wipe);

    const close = el("button", { class: "btn wide", type: "button", text: "Close" });
    close.addEventListener("click", V.closeModal);
    card.append(close);
  });
}

// --------------------------------------------------------------------- listen

async function startSession(song, { autoPlay = false } = {}) {
  try {
    await engine.load(song);
  } catch (err) {
    console.error(err);
    V.toast(err.message || "Could not play that song.", true);
    show("library");
    refreshLibrary();
    return;
  }

  const parts = partsForSong(Object.keys(song.files?.stems || {}));
  const session = store.createSession({
    songId: song.id,
    songTitle: song.title,
    parts: parts.map((p) => p.id),
    duration: engine.duration || song.duration || 0,
  });

  state.song = song;
  state.session = session;
  state.recorder = new SessionRecorder({
    store,
    sessionId: session.sessionId,
    songId: song.id,
    onChange: () => syncPartGrid(),
    // Borrowed from the analysis: events get bar and beat attached while the
    // recorded time stays exactly where the finger was.
    locate: (t) => locate(state.timing, t),
  });
  state.detachRecorder = state.recorder.attach(engine);
  attachAnalysisFor(song);
  startStructureTick();

  $("listen-title").textContent = song.title;
  const stemCount = parts.filter((p) => p.hasStem).length;
  $("listen-meta").textContent = [
    session.sessionId.slice(0, 8),
    stemCount ? `${stemCount} stems` : "full mix only",
    describeMode(engine.mode),
  ].join(" \u00b7 ");
  $("rec-dot").classList.add("is-on");

  state.partGrid = V.buildPartGrid($("part-grid"), parts, {
    onToggle: (partId, perfTime) => togglePart(partId, perfTime),
  });
  for (const p of parts) state.partGrid.setActive(p.id, false);

  const bar = $("seek-bar");
  bar.value = "0";
  bar.max = "1000";
  updateListenClock();
  show("listen");
  requestWakeLock();

  // Rolling into the next song must not need a tap. Browsers only allow this
  // once the page has seen a user gesture, which a listening run always has.
  if (autoPlay) {
    try {
      await engine.play();
    } catch {
      V.toast("Press play to start \u2014 the browser blocked autoplay.", true);
    }
  }
}

function togglePart(partId, perfTime) {
  if (!state.recorder) return;
  const t = audioTimeNow(perfTime);
  try {
    state.recorder.togglePart(partId, t);
  } catch (err) {
    console.error(err);
    V.toast(err.message || "Could not save that tap.", true);
  }
}

// ----------------------------------------------------------------- structure
//
// Everything below reads Workbench's analysis and shows it. The one thing it
// must never do is let machine output become an annotation: the buttons here
// either move the player, or write a *human* event that says what the person
// decided about the machine's suggestion.

/** Read this song's analysis, if one was attached. Called once per session. */
function attachAnalysisFor(song) {
  state.analysis = (song && song.analysis) || store.getAnalysis(song?.id) || null;
  state.timing = makeTiming(state.analysis);
  state.machineCandidate = null;
  state.reviewedCandidates = new Set();
  if (state.analysis && !state.timing) {
    V.toast("That analysis has no usable timing \u2014 no bar numbers for this song.", true);
  }
  const summary = analysisSummary(state.analysis);
  $("analysis-status").textContent = state.analysis
    ? `${summary.segments} segments \u00b7 ${summary.boundaries} boundary candidates \u00b7 ${summary.hasTiming ? "timing ok" : "no timing"}`
    : "";
  return state.analysis;
}

/**
 * Draw the structure strip.
 *
 * The position comes from the same clock a tap is timed with (`audioTimeNow`),
 * so "Bar 17 \u00b7 Beat 2.3" and the bar attached to an event are the same
 * number rather than two numbers that nearly agree.
 */
function renderStructure() {
  const block = $("structure");
  if (!block) return;
  if (!state.analysis || !state.timing || state.screen !== "listen") {
    block.hidden = true;
    return;
  }
  block.hidden = false;
  const now = audioTimeNow();
  const at = locate(state.timing, now);
  const segment = segmentAt(state.analysis, state.timing, now);

  $("structure-position").textContent = at
    ? `Bar ${at.bar} \u00b7 Beat ${at.beat.toFixed(2)}`
    : "Bar \u2014";
  $("structure-segment").textContent = segment
    ? `Segment ${segment.label || segment.segmentId}${Number.isFinite(segment.startBar) ? ` \u00b7 Bars ${segment.startBar}\u2013${segment.endBar}` : ""}`
    : "Segment \u2014";

  const loopChip = $("structure-loop");
  if (engine.loop) {
    loopChip.hidden = false;
    loopChip.textContent = `Loop ${engine.loop.label || ""}`.trim();
  } else {
    loopChip.hidden = true;
  }

  // A similar section is offered, never applied on its own.
  const similar = segment ? similarOf(state.analysis, segment)[0] : null;
  const similarButton = $("btn-similar");
  if (similar && similarButton) {
    similarButton.hidden = false;
    similarButton.textContent = `Similar ${similar.label || similar.segmentId} \u00b7 ${Math.round(similar.similarity * 100)}% \u2192`;
  } else if (similarButton) {
    similarButton.hidden = true;
  }

  // The machine's line: what it says about *this* bar, and its offer to be
  // judged. Ignoring is a decision too, so a reviewed candidate stops asking
  // for the rest of the session.
  const machine = $("machine");
  // Only while we are in the candidate's own bar: the machine's claim is about
  // *here*, and offering it a bar early would be a different claim.
  const candidate = boundaryNear(state.analysis, state.timing, now, 0);
  state.machineCandidate = candidate;
  if (!candidate) {
    machine.hidden = true;
    machine.querySelector("#machine-review").hidden = true;
  } else {
    const id = candidateIdOf(candidate);
    const scores = [];
    if (Number.isFinite(candidate.musicnnScore)) scores.push(`musicnn \u2191 ${candidate.musicnnScore.toFixed(2)}`);
    if (Number.isFinite(candidate.qwenScore)) scores.push(`Qwen \u2191 ${candidate.qwenScore.toFixed(2)}`);
    machine.hidden = false;
    $("machine-line").textContent = `Machine at bar ${candidate.bar ?? "\u2014"}${scores.length ? `: ${scores.join(" \u00b7 ")}` : ""}`;
    machine.querySelector("#machine-review").hidden = state.reviewedCandidates.has(id);
  }

  // Ten frames a second is plenty to read a beat fraction, and it keeps the
  // strip from being the busiest thing on the screen.
  state.structureDrawnAt = performance.now();
}

function candidateIdOf(candidate) {
  return String(candidate?.candidateId ?? candidate?.bar ?? candidate?.start ?? "candidate");
}

function structureTick() {
  if (state.screen !== "listen" || !state.analysis) {
    state.structureRaf = 0;
    return;
  }
  if (performance.now() - state.structureDrawnAt >= 100) renderStructure();
  state.structureRaf = requestAnimationFrame(structureTick);
}

function startStructureTick() {
  if (!state.structureRaf) state.structureRaf = requestAnimationFrame(structureTick);
}

function stopStructureTick() {
  if (state.structureRaf) cancelAnimationFrame(state.structureRaf);
  state.structureRaf = 0;
}

/** Loop whole bars from the one we are in; pressing the same button again stops it. */
function toggleBarLoop(bars) {
  if (!state.timing) return;
  const at = locate(state.timing, audioTimeNow());
  if (!at) return;
  const label = `${bars} bar${bars > 1 ? "s" : ""}`;
  if (engine.loop && engine.loop.label === label) {
    engine.clearLoop();
    V.toast("Loop off");
    return;
  }
  const range = rangeOfBars(state.timing, at.bar, bars);
  engine.setLoop({ ...range, label });
  V.toast(`Loop ${label}: bar ${at.bar}\u2013${at.bar + bars - 1}`);
}

function replaySegment() {
  const segment = segmentAt(state.analysis, state.timing, audioTimeNow());
  const range = segmentRange(state.analysis, state.timing, segment);
  if (!range) {
    V.toast("No segment here to replay.", true);
    return;
  }
  engine.playRange(range.from, range.to, { repeat: false })
    .catch(() => V.toast("Playback was blocked. Tap play.", true));
}

function jumpToSimilar() {
  const segment = segmentAt(state.analysis, state.timing, audioTimeNow());
  const similar = segment ? similarOf(state.analysis, segment)[0] : null;
  const target = similar
    ? (state.analysis.segments || []).find((s) => s.segmentId === similar.segmentId)
    : null;
  const range = segmentRange(state.analysis, state.timing, target);
  if (!range) {
    V.toast("That section is not in this analysis.", true);
    return;
  }
  engine.seek(range.from);
  V.toast(`Jumped to ${target.label || target.segmentId}`);
}

/** 这里换段: the one structural question only a listener can answer. */
function markSectionBoundary() {
  if (!state.recorder) return;
  try {
    state.recorder.sectionBoundary(audioTimeNow());
    V.toast("Marked: a new section starts here");
  } catch (err) {
    console.error(err);
    V.toast(err.message || "Could not save that boundary.", true);
  }
}

function reviewBoundary(decision) {
  if (!state.recorder || !state.machineCandidate) return;
  const id = candidateIdOf(state.machineCandidate);
  try {
    state.recorder.boundaryReview(id, decision, audioTimeNow());
    state.reviewedCandidates.add(id);
    $("machine-review").hidden = true;
    V.toast(decision === "accept" ? "Agreed with the machine"
      : decision === "reject" ? "Rejected the machine's boundary" : "Ignored");
  } catch (err) {
    console.error(err);
    V.toast(err.message || "Could not save that review.", true);
  }
}

/** The part grid is a view of the recorder's live state — never of a guess. */
function syncPartGrid() {
  if (!state.partGrid || !state.recorder) return;
  const now = engine.currentTime;
  const active = new Set(state.recorder.activeParts());
  for (const partId of state.partGrid.buttons.keys()) {
    const since = state.recorder.activeSince(partId);
    const on = active.has(partId);
    state.partGrid.setActive(partId, on, on ? formatClock(Math.max(0, now - (since ?? now)), false) : "");
  }
}

function updateListenClock() {
  const t = engine.currentTime;
  const d = engine.duration || 0;
  $("listen-time").textContent = `${formatClock(t)} / ${formatClock(d)}`;
  if (!state.seeking) {
    $("seek-bar").value = String(d ? Math.round((t / d) * 1000) : 0);
  }
  $("btn-play").textContent = engine.paused ? "\u25b6" : "\u23f8";
  if (state.recorder) syncPartGrid();
}

function wireListenScreen() {
  $("btn-play").addEventListener("click", () => {
    engine.toggle().catch(() => V.toast("Playback was blocked. Tap again.", true));
  });

  $("btn-restart").addEventListener("click", (ev) => {
    // Restart closes open intervals at the pre-jump position (spec section 8).
    engine.restart();
  });

  const bar = $("seek-bar");
  let scrubFrom = null;
  let scrubTo = null;
  let settleTimer = 0;

  /**
   * One gesture is one seek event, however the browser delivers it.
   *
   * `from` is captured on the first `input` — before we move the playhead —
   * rather than on `pointerdown`, because pointer events are not guaranteed to
   * arrive first (or at all) for a slider. A short idle timer commits even if
   * the browser never sends `change` or `pointerup`.
   */
  const commit = () => {
    clearTimeout(settleTimer);
    settleTimer = 0;
    state.seeking = false;
    if (scrubFrom === null || !state.recorder) { scrubFrom = null; return; }
    const from = scrubFrom;
    const target = scrubTo;
    scrubFrom = null;
    scrubTo = null;
    const to = target ?? engine.currentTime;
    if (Math.abs(to - from) < 0.05) return;
    try {
      state.recorder.closeAll("seek", from);
      state.recorder.transport("seek", from, { seekTo: roundMs(to) });
    } catch (err) {
      console.error(err);
      V.toast(err.message || "Could not save that seek.", true);
    }
  };

  bar.addEventListener("input", () => {
    const d = engine.duration || 0;
    if (scrubFrom === null) scrubFrom = engine.currentTime; // still pre-jump
    state.seeking = true;
    const target = (Number(bar.value) / 1000) * d;
    scrubTo = target;
    engine.seekTo(target);
    $("listen-time").textContent = `${formatClock(target)} / ${formatClock(d)}`;
    clearTimeout(settleTimer);
    settleTimer = setTimeout(commit, 300);
  });

  bar.addEventListener("change", commit);
  bar.addEventListener("pointerup", commit);
  bar.addEventListener("pointercancel", commit);

  $("btn-end-session").addEventListener("click", () => endSession("user_end"));
  $("btn-skip-next").addEventListener("click", () => endSession("user_skip", { toNext: true }));

  // The structure buttons. Each one either moves the player or writes a human
  // event - none of them writes anything the machine said.
  $("btn-prev-bar").addEventListener("click", () => {
    if (state.timing) engine.seek(prevBarTarget(state.timing, audioTimeNow()));
  });
  $("btn-next-bar").addEventListener("click", () => {
    if (state.timing) engine.seek(nextBarTarget(state.timing, audioTimeNow()));
  });
  $("btn-loop-1").addEventListener("click", () => toggleBarLoop(1));
  $("btn-loop-4").addEventListener("click", () => toggleBarLoop(4));
  $("btn-replay-segment").addEventListener("click", replaySegment);
  $("btn-similar").addEventListener("click", jumpToSimilar);
  $("btn-section-boundary").addEventListener("click", markSectionBoundary);
  $("btn-bnd-yes").addEventListener("click", () => reviewBoundary("accept"));
  $("btn-bnd-no").addEventListener("click", () => reviewBoundary("reject"));
  $("btn-bnd-ignore").addEventListener("click", () => reviewBoundary("ignore"));

  document.querySelectorAll('[data-action="back-to-library"]').forEach((btn) => {
    btn.addEventListener("click", () => { if (state.screen === "listen") endSession("exit"); else leaveReview(); });
  });
}

function endSession(reason, { toNext = false } = {}) {
  if (!state.session) { leaveReview(); return; }
  const t = audioTimeNow();
  try {
    // Song end, manual end and back-out all funnel through here, so the
    // closing events are written exactly once, in one order:
    // auto-closed attention_out first, then the transport event.
    state.recorder.closeAll(reason, t);
    state.recorder.transport(reason === "song_end" ? "song_end" : "exit", t);
  } catch (err) {
    console.error(err);
  }
  store.endSession(state.session.sessionId, { audioTime: t, reason });
  const sessionId = state.session.sessionId;
  cleanupPlayback();
  if (toNext) rollToNextSong(sessionId);
  else openReview(sessionId);
}

/** Skipped straight out of listening: no review stop, just the next song. */
async function rollToNextSong(fallbackSessionId) {
  const next = pickNextUnannotated(await library.list(), store.listSessions());
  if (next) {
    startSession(next, { autoPlay: true });
    return;
  }
  V.toast("That was the last song without a finished session.");
  openReview(fallbackSessionId);
}

function cleanupPlayback() {
  // Detach first: stopping playback must not append a stray `pause` after the
  // session has already been closed.
  state.detachRecorder?.();
  state.detachRecorder = null;
  try { engine.pause(); } catch { /* ignore */ }
  state.recorder = null;
  state.partGrid = null;
  state.session = null;
  state.seeking = false;
  stopStructureTick();
  try { engine.clearLoop(); } catch { /* ignore */ }
  state.analysis = null;
  state.timing = null;
  state.machineCandidate = null;
  $("structure").hidden = true;
  $("rec-dot").classList.remove("is-on");
  releaseWakeLock();
}

engine.on("ended", () => {
  if (state.screen !== "listen" || !state.session) return;
  endSession("song_end");
});

engine.on("time", () => { if (state.screen === "listen") updateListenClock(); });

engine.on("loaded", () => { if (state.screen === "listen") updateListenClock(); });

// --------------------------------------------------------------------- review

function openReview(sessionId) {
  state.reviewSessionId = sessionId;
  state.selectedRegionId = null;
  renderReview();
  show("review");
  scheduleAutoNext();
}

// ------------------------------------------------------- rolling into the next

/**
 * The bulk listening loop. When a session ends, offer the next unannotated song
 * and start it automatically after a short countdown — any sign that the
 * listener is still working in the review (selecting a span, writing a note)
 * cancels it, because a note is worth more than throughput.
 */
async function scheduleAutoNext() {
  cancelAutoNext();
  const sessionId = state.reviewSessionId;
  const songs = await library.list();
  if (state.reviewSessionId !== sessionId || state.screen !== "review") return;

  state.nextSong = pickNextUnannotated(songs, store.listSessions());
  if (!state.nextSong) { updateNextButton(); return; }

  state.autoNext = { left: AUTO_NEXT_SECONDS, timer: 0 };
  state.autoNext.timer = setInterval(autoNextTick, 1000);
  updateNextButton();
}

function autoNextTick() {
  const pending = state.autoNext;
  if (!pending) return;
  pending.left -= 1;
  if (pending.left <= 0) {
    cancelAutoNext();
    goToNextSong();
    return;
  }
  updateNextButton();
}

function cancelAutoNext() {
  if (!state.autoNext) return;
  clearInterval(state.autoNext.timer);
  state.autoNext = null;
  updateNextButton();
}

function updateNextButton() {
  const btn = $("btn-next-song");
  const cancel = $("btn-cancel-auto");
  if (!btn) return;

  if (!state.nextSong) {
    btn.textContent = "All songs annotated";
    btn.disabled = true;
    if (cancel) cancel.hidden = true;
    return;
  }
  btn.disabled = false;
  const label = state.nextSong.title.length > 16
    ? `${state.nextSong.title.slice(0, 15)}\u2026`
    : state.nextSong.title;
  btn.textContent = state.autoNext
    ? `Next: ${label} in ${state.autoNext.left}s`
    : "Next unannotated \u2192";
  if (cancel) cancel.hidden = !state.autoNext;
}

async function goToNextSong() {
  const next = state.nextSong
    || pickNextUnannotated(await library.list(), store.listSessions());
  if (!next) {
    V.toast("Every song in the library already has a finished session.");
    return;
  }
  leaveReview();
  startSession(next, { autoPlay: true });
}

const renderReview = debounce(renderReviewNow, 0);

function renderReviewNow() {
  const sessionId = state.reviewSessionId;
  if (!sessionId) return;
  const session = store.getSession(sessionId);
  const events = store.listEvents(sessionId);
  const reviews = store.listReviews(sessionId);
  const reviewsByRegion = new Map(reviews.map((r) => [r.regionId, r]));
  const regions = buildRegions(events);

  $("review-title").textContent = session?.songTitle || "Session";
  const { open } = pairIntervals(events);
  $("review-meta").textContent = [
    sessionId.slice(0, 8),
    `${events.length} raw events`,
    `${regions.length} regions`,
    open.length ? `${open.length} still open` : null,
    new Date(session?.startedAt || Date.now()).toLocaleString(),
  ].filter(Boolean).join(" \u00b7 ");

  const list = $("region-list");
  V.renderRegions(list, regions, {
    activeId: state.selectedRegionId,
    reviewsByRegion,
    onSelect: (region) => selectRegion(region),
  });

  const detail = $("region-detail");
  const selected = regions.find((r) => r.regionId === state.selectedRegionId);
  const playbackReady = engine.ready && state.song?.id === session?.songId;
  if (selected) {
    detail.hidden = false;
    V.renderRegionDetail(detail, {
      region: selected,
      review: reviewsByRegion.get(selected.regionId) || null,
      mode: playbackReady ? engine.mode : { type: "none" },
      playing: !!state.reviewRangeEnd,
      hasStems: playbackReady ? engine.masteredParts : [],
      onPlay: (region) => playReviewRegion(region),
      onStop: () => stopReviewPlayback(),
      onMode: (mode) => { engine.setMode(mode); renderReviewNow(); },
      onSaveNote: (region, note) => saveNote(region, note),
      onClearNote: (region) => saveNote(region, ""),
    });
  } else {
    detail.hidden = true;
    detail.innerHTML = "";
  }

  V.renderRawList($("raw-list"), events, $("raw-count"));
}

function selectRegion(region) {
  cancelAutoNext(); // looking at a span means the review is still in use
  state.selectedRegionId = state.selectedRegionId === region.regionId ? null : region.regionId;
  if (!state.selectedRegionId) stopReviewPlayback();
  renderReviewNow();
}

function playReviewRegion(region) {
  if (!engine.ready) { V.toast("Load this song's audio to replay the region.", true); return; }
  stopReviewPlayback();
  engine.seekTo(region.start);
  state.reviewRangeEnd = region.end;
  engine.play().catch(() => V.toast("Tap the play button first, then replay the region.", true));

  const tick = () => {
    if (state.reviewRangeEnd === null) { state.reviewRaf = 0; return; }
    if (engine.currentTime >= state.reviewRangeEnd) {
      stopReviewPlayback();
      return;
    }
    state.reviewRaf = requestAnimationFrame(tick);
  };
  state.reviewRaf = requestAnimationFrame(tick);
  renderReviewNow();
}

function stopReviewPlayback() {
  state.reviewRangeEnd = null;
  if (state.reviewRaf) cancelAnimationFrame(state.reviewRaf);
  state.reviewRaf = 0;
  try { engine.pause(); } catch { /* ignore */ }
}

function saveNote(region, note) {
  store.saveReview(state.reviewSessionId, {
    regionId: region.regionId,
    start: region.start,
    end: region.end,
    partIds: region.partIds,
    note: String(note || ""),
  });
  V.toast(note && note.trim() ? "Note saved" : "Note cleared");
  renderReviewNow();
}

function leaveReview() {
  cancelAutoNext();
  state.nextSong = null;
  stopReviewPlayback();
  engine.unload();
  state.reviewSessionId = null;
  state.selectedRegionId = null;
  show("library");
  refreshLibrary();
}

function wireReviewScreen() {
  $("btn-export").addEventListener("click", () => {
    cancelAutoNext();
    const data = store.exportSession(state.reviewSessionId);
    if (!data) return;
    downloadJson(`session-${state.reviewSessionId.slice(0, 8)}.json`, data);
    V.openModal((card) => {
      card.append(el("h3", { text: "Export written" }));
      card.append(el("p", {
        class: "muted small",
        text: "If the download did not start, copy the JSON below. It contains the session, every raw event, the derived regions and the review notes.",
      }));
      card.append(el("pre", { text: JSON.stringify(data, null, 2) }));
      const close = el("button", { class: "btn wide", type: "button", text: "Close" });
      close.addEventListener("click", V.closeModal);
      card.append(close);
    });
  });

  $("btn-listen-again").addEventListener("click", () => {
    const song = state.song;
    const current = store.getSession(state.reviewSessionId);
    leaveReview();
    if (song) startSession(song);
    else if (current) library.get(current.songId).then((s) => s && startSession(s));
  });

  $("btn-next-song").addEventListener("click", () => goToNextSong());
  $("btn-cancel-auto").addEventListener("click", () => cancelAutoNext());

  // Typing a note is not idling — stop the clock while the keyboard is up.
  $("region-detail").addEventListener("focusin", () => cancelAutoNext());
}

// ----------------------------------------------------------------------- boot

async function boot() {
  wireAddPanel();
  wireBulkPanel();
  wireAnalysisPanel();
  wireListenScreen();
  wireReviewScreen();
  V.renderPreview($("add-preview"), null);
  await refreshLibrary();
  renderStatus();
  queue.start();
  globalThis.addEventListener("online", renderStatus);
  globalThis.addEventListener("offline", renderStatus);
  setInterval(renderStatus, 2000);
  if (store.queueList().length) V.toast(`Resuming ${store.queueList().length} queued uploads`);
  console.log("[app] attention annotator ready");
}

boot().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="padding:16px;color:#ff6b6b">Startup failed: ${err.message}</pre>`;
});

// Exposed for manual poking from the console during research sessions.
globalThis.__attention = { store, library, engine, queue, state, derive: { buildRegions, pairIntervals } };
