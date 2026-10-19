// DOM rendering. Pure presentation — every callback is supplied by app.js.

import { formatClock, formatDurationShort, formatRange, el, eventPerfTime } from "./util.js";

export const PART_COLORS = {
  Guitar: "#ff922b",
  Bass: "#4dabf7",
  Drums: "#ffd43b",
  Piano: "#b197fc",
  Vocal: "#ff8fab",
  Strings: "#63e6be",
  Synth: "#a5b4fc",
  Other: "#adb5bd",
};

export function partColor(partId) {
  if (PART_COLORS[partId]) return PART_COLORS[partId];
  let h = 0;
  for (const ch of String(partId)) h = (h * 31 + ch.charCodeAt(0)) % 360;
  return `hsl(${h} 70% 62%)`;
}

// ------------------------------------------------------------------ song list

export function renderSongList(container, songs, { onStart, onDelete, annotated = new Set() }) {
  container.innerHTML = "";
  if (!songs.length) {
    container.append(el("div", {
      class: "empty",
      text: "No songs yet. Add a master + stems, import a whole library, or load the demo song.",
    }));
    return;
  }
  for (const song of songs) {
    const done = annotated.has(song.id);
    const card = el("button", { class: "song-card", type: "button" });
    card.append(
      el("span", {}, [
        el("span", { class: "song-name", text: song.title }),
        el("span", { class: "song-sub", text: describe(song) }),
      ]),
      el("span", {
        class: `song-progress ${done ? "done" : "todo"}`,
        text: done ? "\u2713 done" : "to listen",
      }),
    );
    card.addEventListener("click", () => onStart(song));

    const remove = el("button", {
      class: "chip btn-chip",
      type: "button",
      text: "\u2715",
      title: "Remove from library",
      "aria-label": `Remove ${song.title}`,
    });
    remove.addEventListener("click", (ev) => {
      ev.stopPropagation();
      if (confirm(`Remove "${song.title}" from this device?`)) onDelete(song);
    });
    const wrap = el("div", { class: "song-card-wrap" }, [card, remove]);
    wrap.style.display = "grid";
    wrap.style.gridTemplateColumns = "1fr auto";
    wrap.style.gap = "8px";
    wrap.style.alignItems = "center";
    container.append(wrap);
  }
}

function describe(song) {
  const stemIds = Object.keys(song.files?.stems || {});
  const bits = [stemIds.length ? `${stemIds.join(" / ")}` : "mix only"];
  if (song.source === "demo") bits.push("demo");
  if (song.notes) bits.push(song.notes);
  return bits.join(" \u00b7 ");
}

/** "12 / 40 songs annotated · 48 min covered" plus a bar. */
export function renderProgress(container, summary) {
  container.innerHTML = "";
  if (!summary?.total) { container.hidden = true; return; }
  container.hidden = false;

  const pct = Math.round((summary.annotated / summary.total) * 100);
  container.append(
    el("div", { class: "summary-row" }, [
      el("span", { class: "summary-big", text: `${summary.annotated} / ${summary.total} songs` }),
      el("span", { class: "muted", text: summary.coveredSeconds ? `${formatDurationShort(summary.coveredSeconds)} of music` : "" }),
    ]),
    el("div", { class: "progress" }, [el("i", { style: { width: `${pct}%` } })]),
    el("div", {
      class: "muted small",
      text: [
        `${summary.remaining} still to listen`,
        summary.sessions ? `${summary.sessions} session${summary.sessions === 1 ? "" : "s"} recorded` : null,
      ].filter(Boolean).join(" \u00b7 "),
    }),
  );
}

export function renderSessionList(container, sessions, { onOpen, onDelete, counts }) {
  container.innerHTML = "";
  if (!sessions.length) {
    container.append(el("div", {
      class: "empty",
      text: "Finished sessions land here, so you can re-read a session or export it again later.",
    }));
    return;
  }
  for (const session of sessions.slice(0, 12)) {
    const info = counts(session.sessionId);
    const card = el("button", { class: "region-card", type: "button" });
    card.append(
      el("span", { class: "range", text: session.songTitle || session.songId }),
      el("span", {
        class: "note-preview",
        text: [
          new Date(session.startedAt).toLocaleString(),
          `${info.events} events`,
          `${info.regions} regions`,
          session.endReason && session.endReason !== "song_end" ? session.endReason : null,
        ].filter(Boolean).join(" \u00b7 "),
      }),
    );
    card.addEventListener("click", () => onOpen(session));

    const row = el("div", {}, [card]);
    row.style.display = "grid";
    row.style.gridTemplateColumns = "1fr auto";
    row.style.gap = "8px";
    row.style.alignItems = "center";

    const remove = el("button", {
      class: "chip btn-chip",
      type: "button",
      text: "\u2715",
      title: "Delete this session",
      "aria-label": `Delete session ${session.sessionId.slice(0, 8)}`,
    });
    remove.addEventListener("click", (ev) => {
      ev.stopPropagation();
      if (confirm("Delete this session, its events and its notes from this device?")) onDelete(session);
    });
    row.append(remove);
    container.append(row);
  }
}

export function renderPreview(ul, selection) {
  ul.innerHTML = "";
  if (!selection || (!selection.masterFile && !Object.keys(selection.stems || {}).length)) {
    ul.append(el("li", { class: "muted", text: "No files selected." }));
    return;
  }
  const row = (tag, label, name, cls = "") =>
    el("li", {}, [
      el("span", { class: `tag ${cls}`, text: tag }),
      el("span", { text: label }),
      el("span", { class: "muted small", text: name }),
    ]);

  if (selection.masterFile) ul.append(row("master", "Full mix", selection.masterFile.name, "master"));
  else ul.append(el("li", { class: "muted", text: "No master found — the first file will be used." }));

  for (const [partId, file] of Object.entries(selection.stems || {})) {
    ul.append(row(partId.toUpperCase(), `${partId} stem`, file.name));
  }
  for (const f of selection.unmatched || []) {
    ul.append(el("li", { class: "muted", text: `ignored: ${f.name} (name it master/guitar/bass/drums/piano to map it)` }));
  }
}

// ----------------------------------------------------------------- part grid

/**
 * Four (or more) independent toggles. The press is timed at pointer-DOWN but
 * only committed on a clean release, so scrolling across the grid never
 * produces a phantom annotation.
 */
export function buildPartGrid(container, parts, { onToggle }) {
  container.innerHTML = "";
  const buttons = new Map();

  for (const part of parts) {
    const btn = el("button", {
      class: "part-btn",
      type: "button",
      dataset: part.hasStem === false ? { part: part.id, noStem: "1" } : { part: part.id },
      style: { "--part-color": partColor(part.id) },
      "aria-pressed": "false",
    });
    btn.append(
      el("span", { class: "label", text: part.label || part.id }),
      el("span", { class: "state", text: "OFF" }),
    );

    let pending = null;
    btn.addEventListener("pointerdown", (ev) => {
      pending = { x: ev.clientX, y: ev.clientY, at: eventPerfTime(ev), moved: false };
    });
    btn.addEventListener("pointermove", (ev) => {
      if (!pending) return;
      if (Math.abs(ev.clientX - pending.x) > 12 || Math.abs(ev.clientY - pending.y) > 12) pending.moved = true;
    });
    btn.addEventListener("pointerup", (ev) => {
      const p = pending;
      pending = null;
      if (!p || p.moved) return;
      if (eventPerfTime(ev) - p.at > 900) return; // held down, not a tap
      ev.preventDefault();
      onToggle(part.id, p.at);
    });
    btn.addEventListener("pointercancel", () => { pending = null; });
    btn.addEventListener("contextmenu", (ev) => ev.preventDefault());

    buttons.set(part.id, btn);
    container.append(btn);
  }

  return {
    setActive(partId, active, elapsedLabel = "") {
      const btn = buttons.get(partId);
      if (!btn) return;
      btn.classList.toggle("is-on", !!active);
      btn.setAttribute("aria-pressed", active ? "true" : "false");
      const state = btn.querySelector(".state");
      if (state) {
        state.textContent = active ? (elapsedLabel || "ON") : "OFF";
      }
    },
    buttons,
  };
}

// ------------------------------------------------------------------- regions

export function renderRegions(container, regions, { onSelect, activeId, reviewsByRegion }) {
  container.innerHTML = "";
  if (!regions.length) {
    container.append(el("div", {
      class: "empty",
      text: "No attention regions in this session. That is a valid result — not a data error.",
    }));
    return;
  }
  for (const region of regions) {
    const card = el("button", {
      class: `region-card${region.regionId === activeId ? " is-active" : ""}`,
      type: "button",
    });
    card.append(
      el("span", { class: "range", text: formatRange(region.start, region.end) }),
      chips(region),
    );
    const review = reviewsByRegion?.get(region.regionId);
    if (review?.note) {
      card.append(el("span", { class: "note-preview", text: truncate(review.note, 90) }));
    }
    card.addEventListener("click", () => onSelect(region));
    container.append(card);
  }
}

function chips(region) {
  const row = el("span", { class: "part-chips" });
  for (const part of region.parts) {
    row.append(el("span", {
      class: `part-chip on${part.autoClosed ? " auto" : ""}`,
      style: { "--part-color": partColor(part.partId) },
      text: part.partId,
      title: part.autoClosed ? `closed automatically (${part.closeReason})` : "",
    }));
  }
  return row;
}

function truncate(s, n) {
  const t = String(s).replace(/\s+/g, " ").trim();
  return t.length > n ? `${t.slice(0, n - 1)}\u2026` : t;
}

export function renderRegionDetail(container, ctx) {
  const { region, review, mode, playing, hasStems, onPlay, onStop, onMode, onSaveNote, onClearNote } = ctx;
  container.innerHTML = "";

  container.append(el("div", { class: "detail-range", text: formatRange(region.start, region.end) }));
  container.append(chips(region));

  const transport = el("div", { class: "mode-grid" });
  const playBtn = el("button", { class: "btn primary", type: "button", text: playing ? "\u25a0 Stop" : "\u25b6 Replay region" });
  playBtn.addEventListener("click", () => (playing ? onStop() : onPlay(region)));
  transport.append(playBtn);
  container.append(transport);

  const modes = el("div", { class: "mode-grid" });
  const full = el("button", {
    class: `btn${mode.type === "full" ? " primary" : " ghost"}`,
    type: "button",
    text: "Full mix",
  });
  full.addEventListener("click", () => onMode({ type: "full" }));
  modes.append(full);

  for (const part of region.parts) {
    const available = hasStems.includes(part.partId);
    const solo = el("button", {
      class: `btn${mode.type === "solo" && mode.partId === part.partId ? " primary" : " ghost"}`,
      type: "button",
      text: available ? `${part.partId} solo` : `${part.partId} (no stem)`,
    });
    solo.disabled = !available;
    solo.style.borderColor = available ? partColor(part.partId) : "";
    solo.addEventListener("click", () => onMode({ type: "solo", partId: part.partId }));
    modes.append(solo);
  }
  container.append(modes);

  const note = el("textarea", {
    placeholder: "Optional note \u2014 describe it in your own words. No categories, no rating.",
  });
  note.value = review?.note || "";
  const noteField = el("label", { class: "field" }, [el("span", { text: "Note" }), note]);
  container.append(noteField);

  const actions = el("div", { class: "row" });
  const save = el("button", { class: "btn", type: "button", text: "Save note" });
  save.addEventListener("click", () => onSaveNote(region, note.value));
  actions.append(save);
  if (review?.note) {
    const clear = el("button", { class: "btn ghost", type: "button", text: "Clear note" });
    clear.addEventListener("click", () => onClearNote(region));
    actions.append(clear);
  }
  container.append(actions);

  note.addEventListener("blur", () => {
    if (note.value !== (review?.note || "")) onSaveNote(region, note.value);
  });
}

export function renderRawList(container, events, countLabel) {
  container.innerHTML = "";
  for (const e of events) {
    const cls = e.autoClosed ? "raw-row auto" : "raw-row";
    const bits = [
      el("span", { class: "t", text: formatClock(e.audioTime) }),
      el("span", { text: e.eventType }),
      el("span", { text: e.partId || "" }),
    ];
    if (e.autoClosed) bits.push(el("span", { class: "muted", text: `auto (${e.closeReason})` }));
    if (e.seekTo !== undefined) bits.push(el("span", { class: "muted", text: `\u2192 ${formatClock(e.seekTo)}` }));
    container.append(el("div", { class: cls }, bits));
  }
  if (countLabel) countLabel.textContent = `(${events.length})`;
}

// -------------------------------------------------------------------- chrome

let toastTimer = 0;
export function toast(message, bad = false) {
  const node = document.getElementById("toast");
  if (!node) return;
  node.textContent = message;
  node.classList.toggle("toast-bad", !!bad);
  node.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { node.hidden = true; }, 2600);
}

export function closeModal() {
  const modal = document.getElementById("modal");
  if (modal) modal.hidden = true;
}

export function openModal(build) {
  const modal = document.getElementById("modal");
  const card = document.getElementById("modal-card");
  card.innerHTML = "";
  build(card, closeModal);
  modal.hidden = false;
  modal.onclick = (ev) => { if (ev.target === modal) closeModal(); };
}
