// The machine's analysis document, read-only.
//
// Workbench computes this offline (timing, segments, boundaries, similarity) and
// the annotator only ever *reads* it. That asymmetry is the point of the file:
// nothing here writes, normalises or repairs machine data into the human record.
// It parses what it was given, reports what it could not understand by name, and
// hands the rest over as-is.
//
// Two consequences, both deliberate:
//   - parsing never throws. A broken analysis file must not stop a listening
//     session; it means this song has no structure UI today.
//   - unknown fields survive. A newer Workbench can add blocks without this file
//     needing to know about them first.

import { makeTiming } from "./timing.js";

export const ANALYSIS_SCHEMA = "fallenangel.attention.analysis/0.1";

/**
 * @returns {{analysis: object|null, problems: string[]}}
 */
export function parseAnalysis(text) {
  const problems = [];
  if (typeof text !== "string" || !text.trim()) {
    return { analysis: null, problems: ["the analysis is empty"] };
  }
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch (err) {
    return { analysis: null, problems: [`not JSON: ${err.message}`] };
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    return { analysis: null, problems: ["the analysis is not an object"] };
  }

  if (parsed.schema && parsed.schema !== ANALYSIS_SCHEMA) {
    problems.push(`schema "${parsed.schema}" is not "${ANALYSIS_SCHEMA}"; reading it anyway`);
  }

  if (parsed.timing && !makeTiming(parsed)) {
    problems.push("timing is present but unusable (needs bpm > 0, beatsPerBar > 0, beatOffset)");
  }

  if (parsed.segments !== undefined) {
    if (!Array.isArray(parsed.segments)) {
      problems.push("segments is not a list");
    } else {
      parsed.segments.forEach((segment, i) => {
        if (!segment || typeof segment !== "object") {
          problems.push(`segment ${i} is not an object`);
        } else if (!segment.segmentId) {
          problems.push(`segment ${i} has no segmentId`);
        }
      });
    }
  }

  if (parsed.boundaries !== undefined) {
    if (!Array.isArray(parsed.boundaries)) {
      problems.push("boundaries is not a list");
    } else {
      parsed.boundaries.forEach((boundary, i) => {
        if (!boundary || typeof boundary !== "object") {
          problems.push(`boundary ${i} is not an object`);
        } else if (!Number.isFinite(boundary.bar) && !Number.isFinite(boundary.start)) {
          problems.push(`boundary ${i} has no bar to sit on`);
        }
      });
    }
  }

  return { analysis: parsed, problems };
}

/** What the structure strip needs to decide whether to show itself. */
export function analysisSummary(analysis) {
  if (!analysis) return { hasTiming: false, segments: 0, boundaries: 0, similar: 0 };
  let similar = 0;
  for (const segment of analysis.segments || []) similar += (segment?.similar?.length || 0);
  for (const ref of analysis.similarSections || []) if (ref) similar += 1;
  return {
    hasTiming: makeTiming(analysis) !== null,
    segments: (analysis.segments || []).length,
    boundaries: (analysis.boundaries || []).length,
    similar,
  };
}
