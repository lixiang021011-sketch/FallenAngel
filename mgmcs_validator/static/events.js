/**
 * What the events in the file are, and how they look on the timeline.

 * The file stores two independent in/out events per attention span, because
 * that is what a person actually produced by tapping twice. Drawing them as one
 * bar is a convenience for the eye and nothing more, so the pairing here is
 * deliberately conservative: an out only ever ends an in that came before it, an
 * unpaired event stays a point, and no end time is ever invented for anything.
 */
globalThis.MGEvents = (function () {
  'use strict';

  /** One letter per part, the way the spec's mock-ups label the timeline. */
  const PART_CODES = {
    guitar: 'G', bass: 'B', drums: 'D', drum: 'D', piano: 'P',
    vocal: 'V', voice: 'V', strings: 'S', keys: 'K', keyboard: 'K',
    synth: 'Y', other: 'O',
  };

  /** Types the tool knows by name. Anything else is drawn neutrally. */
  const MARKER_TYPES = {
    meter_change: 'M',
    tempo_change: 'T',
    half_bar: 'H',
    phase_shift: 'F',
    grid_reentry: 'R',
    uncertain: '?',
    marker: '•',
  };

  const LANES = ['attention', 'marker', 'other'];

  /** "guitar_attention_in" -> "guitar", "attention", "in". */
  function splitAttention(type) {
    const match = /^(.+?)_(attention)_(in|out)$/.exec(String(type || ''));
    if (!match) return null;
    return { part: match[1], action: match[3] };
  }

  function partOf(event) {
    const fromPayload = event.payload && typeof event.payload.part === 'string'
      ? event.payload.part : '';
    if (fromPayload) return fromPayload;
    const split = splitAttention(event.type);
    return split ? split.part : '';
  }

  function isKnownMarker(type) {
    return Object.prototype.hasOwnProperty.call(MARKER_TYPES, type);
  }

  /**
   * Attention, a change marker, or something this build has never heard of.

   * The middle case is the one worth spelling out: an event that names a part is
   * about that part, whether it arrives as `guitar_attention_in` or as a span
   * type with `payload.part`, so it goes in the attention lane.
   */
  function laneOf(event) {
    if (splitAttention(event.type)) return 'attention';
    if (isKnownMarker(event.type)) return 'marker';
    if (String(event.type || '').includes('attention') || partOf(event)) return 'attention';
    return 'other';
  }

  function labelOf(event) {
    const part = partOf(event);
    if (part) return PART_CODES[part] || part.slice(0, 1).toUpperCase();
    if (Object.prototype.hasOwnProperty.call(MARKER_TYPES, event.type)) return MARKER_TYPES[event.type];
    return '•';
  }

  /** Which glyph the CSS gives it: a triangle up for in, down for out, and so on. */
  function shapeOf(event) {
    if (event.kind === 'span') return 'span';
    const split = splitAttention(event.type);
    if (split) return split.action;
    if (event.type === 'meter_change') return 'meter';
    if (event.type === 'tempo_change') return 'tempo';
    if (event.type === 'half_bar') return 'halfbar';
    return 'dot';
  }

  /**
   * The events as the timeline wants them: sorted, labelled, and with the times
   * the drawing code reads. The original objects are kept on `.raw` untouched,
   * because the point of the tool is to look at what arrived, not at a tidied
   * version of it.
   */
  function normalizeEvents(events) {
    return (events || [])
      .map((event) => {
        const start = event.time;
        const end = event.kind === 'span' ? event.endTime : NaN;
        return {
          eventId: event.eventId,
          type: event.type,
          kind: event.kind,
          raw: event.raw,
          payload: event.payload,
          source: event.source,
          part: partOf(event),
          lane: laneOf(event),
          label: labelOf(event),
          shape: shapeOf(event),
          start,
          end,
          time: start,
        };
      })
      .sort((a, b) => a.start - b.start || String(a.eventId).localeCompare(String(b.eventId)));
  }

  /**
   * Span bars for attention in/out pairs. Display only.

   * One open span per part. A second "in" for the same part replaces the open
   * one - the earlier in is left unmatched, since closing it would mean guessing
   * where the attention stopped. An "out" with nothing open does nothing.
   */
  function pairAttention(events) {
    const open = new Map();
    const spans = [];
    for (const event of events || []) {
      const split = splitAttention(event.type);
      if (!split || event.kind !== 'point') continue;
      if (split.action === 'in') {
        open.set(split.part, event);
        continue;
      }
      const started = open.get(split.part);
      if (!started || !(event.time > started.time)) continue;
      open.delete(split.part);
      spans.push({
        spanId: started.eventId + '..' + event.eventId,
        part: split.part,
        start: started.time,
        end: event.time,
        inEventId: started.eventId,
        outEventId: event.eventId,
      });
    }
    return spans;
  }

  /**
   * Which row each item is drawn on, so overlapping events stack instead of
   * hiding each other. Items must already be in time order, and `minWidth` gives
   * a bare point enough width to collide with its neighbour - two taps 3px apart
   * are two taps, and one covering the other loses one of them.
   */
  function stackLevels(items, minWidth) {
    const width = minWidth > 0 ? minWidth : 0;
    const levelEnds = [];
    const levels = [];
    for (const item of items || []) {
      const start = item.start;
      const end = Math.max(item.end, start + width);
      let level = 0;
      while (level < levelEnds.length && levelEnds[level] > start) level += 1;
      levelEnds[level] = end;
      levels.push(level);
    }
    return { levels, count: levelEnds.length };
  }

  /** The checkboxes: one per distinct type in this file, in lane order. */
  function filterTypes(events) {
    const seen = new Map();
    for (const event of events || []) {
      if (seen.has(event.type)) continue;
      seen.set(event.type, { type: event.type, lane: event.lane, label: event.label });
    }
    return [...seen.values()].sort((a, b) => {
      const lane = LANES.indexOf(a.lane) - LANES.indexOf(b.lane);
      return lane !== 0 ? lane : a.type.localeCompare(b.type);
    });
  }

  return {
    LANES,
    splitAttention,
    partOf,
    laneOf,
    labelOf,
    shapeOf,
    normalizeEvents,
    pairAttention,
    stackLevels,
    filterTypes,
  };
}());
