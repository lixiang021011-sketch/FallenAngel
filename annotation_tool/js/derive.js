// Pure derivation: raw events in, attention regions out.
//
// Rules that matter (spec sections 4, 5, 8, 14):
//  - raw events are the truth; regions are only a derived view
//  - a part's interval is closed by its own attention_out, never by guesswork
//  - auto-closed outs (seek / restart / song end) carry autoClosed + closeReason
//    and end the interval at the time the user acted
//  - no snapping of any kind
//
// The module is DOM free so it can be unit tested under node.

export const PART_EVENTS = ["attention_in", "attention_out"];
export const TRANSPORT_EVENTS = ["play", "pause", "seek", "restart", "song_end", "exit"];

function byTime(a, b) {
  if (a.audioTime !== b.audioTime) return a.audioTime - b.audioTime;
  return String(a.eventId).localeCompare(String(b.eventId));
}

export function sortedEvents(events) {
  return [...(events || [])].sort(byTime);
}

function makeInterval(partId, inEvent, outEvent) {
  return {
    partId,
    start: inEvent.audioTime,
    end: outEvent.audioTime,
    duration: Math.max(0, outEvent.audioTime - inEvent.audioTime),
    inEventId: inEvent.eventId,
    outEventId: outEvent.eventId,
    autoClosed: outEvent.autoClosed === true,
    closeReason: outEvent.closeReason ?? null,
  };
}

/**
 * Pair attention_in / attention_out per part.
 * Unmatched ins are returned as `open` so the UI can show live state.
 */
export function pairIntervals(events, opts = {}) {
  const list = sortedEvents(events);
  const fallbackEnd = Number.isFinite(opts.openUntil) ? opts.openUntil : null;
  const open = new Map();
  const intervals = [];

  for (const e of list) {
    if (!e || !e.partId) continue;
    if (e.eventType === "attention_in") {
      if (!open.has(e.partId)) open.set(e.partId, e);
      continue;
    }
    if (e.eventType === "attention_out") {
      const inEvent = open.get(e.partId);
      if (!inEvent) continue; // stray out — keep it raw, derive nothing
      open.delete(e.partId);
      intervals.push(makeInterval(e.partId, inEvent, e));
    }
  }

  const stillOpen = [];
  for (const [partId, inEvent] of open) {
    const synthetic = {
      eventId: `${inEvent.eventId}~open`,
      audioTime: fallbackEnd ?? inEvent.audioTime,
      eventType: "attention_out",
      partId,
      autoClosed: true,
      closeReason: fallbackEnd === null ? "open" : "end",
    };
    const interval = makeInterval(partId, inEvent, synthetic);
    stillOpen.push(interval);
    if (fallbackEnd !== null) intervals.push(interval);
  }

  intervals.sort((a, b) => (a.start - b.start) || a.partId.localeCompare(b.partId));
  stillOpen.sort((a, b) => a.partId.localeCompare(b.partId));
  return { intervals, open: stillOpen };
}

function sameParts(a, b) {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}

export function regionId(start, end) {
  const ms = (t) => String(Math.round(t * 1000));
  return `rg-${ms(start)}-${ms(end)}`;
}

/**
 * Sweep the interval set and emit maximal spans where the *set of attended
 * parts* is constant. `[Guitar]` then `[Guitar, Bass]` then `[Guitar]` gives
 * three regions, which keeps "Guitar + Bass together" visible without
 * pretending the guitar attention started when the bass did.
 */
export function buildRegions(events, opts = {}) {
  const { intervals, open } = pairIntervals(events, opts);
  const usable = intervals.filter((i) => i.end > i.start);

  const bounds = new Set();
  for (const i of usable) {
    bounds.add(i.start);
    bounds.add(i.end);
  }
  const points = [...bounds].sort((a, b) => a - b);

  const segments = [];
  for (let k = 0; k + 1 < points.length; k++) {
    const start = points[k];
    const end = points[k + 1];
    if (end <= start) continue;
    const mid = (start + end) / 2;
    // Distinct: one part can overlap itself (a tap adds its own short span), and
    // duplicates would stop neighbouring segments from merging back together.
    const parts = [...new Set(
      usable.filter((i) => i.start <= mid && mid < i.end).map((i) => i.partId),
    )].sort();
    if (!parts.length) continue;

    const last = segments[segments.length - 1];
    if (last && last.end === start && sameParts(last.partIds, parts)) {
      last.end = end;
    } else {
      segments.push({ start, end, partIds: parts });
    }
  }

  return segments.map((s) => {
    const parts = s.partIds.map((partId) => {
      const source = usable.filter((i) => i.partId === partId && i.start <= s.start && i.end >= s.end);
      const anyAuto = source.some((i) => i.autoClosed);
      return {
        partId,
        autoClosed: anyAuto,
        closeReason: anyAuto ? source.find((i) => i.autoClosed)?.closeReason ?? null : null,
      };
    });
    return {
      regionId: regionId(s.start, s.end),
      start: s.start,
      end: s.end,
      duration: s.end - s.start,
      partIds: s.partIds,
      parts,
    };
  });
}

/** Convenience for the review screen header. */
export function summarize(events) {
  const list = events || [];
  const counts = {};
  for (const e of list) counts[e.eventType] = (counts[e.eventType] || 0) + 1;
  return counts;
}
