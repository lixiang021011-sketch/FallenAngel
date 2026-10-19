// Seconds to Bar / Beat, and the ranges the structure buttons ask for.
//
// The player's clock stays the real coordinate — everything an annotation
// records is still `audioTime`. Bar and beat are *derived*, computed here and
// attached as extra fields, because they are what makes a session comparable
// across listens and across tools. Two rules follow from that and are enforced
// by keeping this module pure:
//
//   - nothing here snaps a time onto a beat. A tap keeps the moment it happened.
//   - with no timing there are no coordinates. Returning a guess would put
//     invented bar numbers into a research record.
//
// `timing` is `{ beatOffset, bpm, beatsPerBar }` as Workbench publishes it. A
// tempo map (a song that changes speed) would extend `locate` alone; the shape
// of every other function here would not change.

/** Read a timing block, or null when it is not usable. */
export function makeTiming(analysis) {
  const t = analysis && analysis.timing ? analysis.timing : analysis;
  if (!t || typeof t !== "object") return null;
  const bpm = Number(t.bpm);
  const beatsPerBar = Number(t.beatsPerBar);
  const beatOffset = Number(t.beatOffset);
  if (!(bpm > 0) || !(beatsPerBar > 0) || !Number.isFinite(beatOffset)) return null;
  return { beatOffset, bpm, beatsPerBar };
}

const beatSeconds = (timing) => 60 / timing.bpm;
const barSeconds = (timing) => timing.beatsPerBar * beatSeconds(timing);

/**
 * Where a moment sits musically.
 *
 * Bars and beats count from one, the way a musician says them, and the beat
 * keeps its fraction: "Beat 2.31" is a real position, not a rounding of one.
 * Before the first beat of the song the bar is still 1 and the beat number is
 * below 1, which is the truthful reading of "we have not reached the downbeat".
 */
export function locate(timing, seconds) {
  if (!timing) return null;
  const beats = (Number(seconds) - timing.beatOffset) / beatSeconds(timing);
  const bar = Math.max(1, Math.floor(beats / timing.beatsPerBar) + 1);
  const inBar = beats - (bar - 1) * timing.beatsPerBar;
  const whole = Math.floor(inBar);
  return {
    bar,
    beat: inBar + 1,
    beatIndex: Math.max(1, whole + 1),
    beatFrac: inBar - whole,
  };
}

/** When bar `bar` begins, in seconds. */
export function barStart(timing, bar) {
  if (!timing) return 0;
  return timing.beatOffset + (Math.max(1, bar) - 1) * barSeconds(timing);
}

/** How many bars the song has room for. */
export function barCount(timing, duration) {
  if (!timing) return 0;
  const bars = Math.floor((Number(duration) - timing.beatOffset) / barSeconds(timing)) + 1;
  return Math.max(1, bars);
}

/**
 * The previous bar button: the start of the bar we are in.
 *
 * Spec section 3 says exactly this ("seek 到当前 bar 起点"), so that is what it
 * does — pressing it twice does not walk backwards through the song.
 */
export function prevBarTarget(timing, seconds) {
  const at = locate(timing, seconds);
  return at ? Math.max(0, barStart(timing, at.bar)) : 0;
}

/** The next bar button: the start of the bar after this one. */
export function nextBarTarget(timing, seconds) {
  const at = locate(timing, seconds);
  return at ? barStart(timing, at.bar + 1) : 0;
}

/** A run of whole bars, as a time range. `count` is at least one bar. */
export function rangeOfBars(timing, fromBar, count) {
  const bars = Math.max(1, Math.floor(Number(count) || 1));
  return { from: barStart(timing, fromBar), to: barStart(timing, fromBar + bars) };
}

/** The segment containing a moment, by bars when it has them, else by seconds. */
export function segmentAt(analysis, timing, seconds) {
  if (!analysis || !timing) return null;
  const at = locate(timing, seconds);
  if (!at) return null;
  for (const segment of analysis.segments || []) {
    if (!segment) continue;
    if (Number.isFinite(segment.startBar) && Number.isFinite(segment.endBar)) {
      if (at.bar >= segment.startBar && at.bar <= segment.endBar) return segment;
    } else if (Number.isFinite(segment.start) && Number.isFinite(segment.end)) {
      if (seconds >= segment.start && seconds < segment.end) return segment;
    }
  }
  return null;
}

/** A segment as a time range; `endBar` is inclusive, so the range is one bar wider. */
export function segmentRange(analysis, timing, segment) {
  if (!segment || !timing) return null;
  if (Number.isFinite(segment.startBar) && Number.isFinite(segment.endBar)) {
    return { from: barStart(timing, segment.startBar), to: barStart(timing, segment.endBar + 1) };
  }
  if (Number.isFinite(segment.start) && Number.isFinite(segment.end)) {
    return { from: segment.start, to: segment.end };
  }
  return null;
}

/**
 * The machine's boundary candidate next to this moment, if there is one.
 *
 * Only ever offered, never applied: it is a thing to agree or disagree with.
 * Inside `windowBars` the nearest candidate wins, so two candidates close
 * together do not both compete for the same button.
 */
export function boundaryNear(analysis, timing, seconds, windowBars = 1) {
  if (!analysis || !timing) return null;
  const at = locate(timing, seconds);
  if (!at) return null;
  let best = null;
  let bestDistance = Infinity;
  for (const candidate of analysis.boundaries || []) {
    if (!candidate) continue;
    let bar = Number(candidate.bar);
    if (!Number.isFinite(bar) && Number.isFinite(candidate.start)) {
      const located = locate(timing, candidate.start);
      bar = located ? located.bar : NaN;
    }
    if (!Number.isFinite(bar)) continue;
    const distance = Math.abs(bar - at.bar);
    if (distance <= windowBars && distance < bestDistance) {
      best = candidate;
      bestDistance = distance;
    }
  }
  return best;
}

/** Segments the machine thinks sound like this one, best match first. */
export function similarOf(analysis, segment) {
  if (!analysis || !segment) return [];
  const labels = new Map((analysis.segments || []).map((s) => [s?.segmentId, s?.label]));
  const out = [];
  for (const ref of segment.similar || []) {
    if (!ref || !ref.segmentId) continue;
    out.push({
      segmentId: ref.segmentId,
      similarity: Number(ref.similarity) || 0,
      label: labels.get(ref.segmentId) ?? null,
    });
  }
  // A Workbench that keeps similarity in its own list is read just as willingly.
  for (const ref of analysis.similarSections || []) {
    if (!ref || ref.segmentId !== segment.segmentId || !ref.similarSegmentId) continue;
    out.push({
      segmentId: ref.similarSegmentId,
      similarity: Number(ref.similarity) || 0,
      label: labels.get(ref.similarSegmentId) ?? null,
    });
  }
  return out.sort((a, b) => b.similarity - a.similarity);
}
