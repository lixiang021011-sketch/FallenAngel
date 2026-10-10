/**
 * What an event is, how it is labelled, and how in/out pairs become a bar.

 * The pairing rules matter more than they look: a span drawn between an out and
 * an in that never belonged together is the tool inventing a fact, and this tool
 * exists to check other people's facts. So the rule is that an out is only ever
 * used as the end of an in that came before it, and an event that cannot be
 * paired stays a point rather than acquiring an end it does not have.
 */
import test from 'node:test';
import assert from 'node:assert/strict';

await import('../static/events.js');
const E = globalThis.MGEvents;

function point(id, type, time, payload) {
  return { eventId: id, type, kind: 'point', time, endTime: NaN, payload, raw: {} };
}

test('a part shortens to one letter, from the payload or from the type', () => {
  assert.equal(E.labelOf(point('a', 'guitar_attention_in', 1, { part: 'guitar' })), 'G');
  assert.equal(E.labelOf(point('b', 'bass_attention_out', 1)), 'B');
  assert.equal(E.labelOf(point('c', 'drum_attention_in', 1)), 'D');
  assert.equal(E.labelOf(point('d', 'piano_attention_in', 1)), 'P');
});

test('the marker types have their own letters', () => {
  assert.equal(E.labelOf(point('a', 'meter_change', 1)), 'M');
  assert.equal(E.labelOf(point('a', 'tempo_change', 1)), 'T');
  assert.equal(E.labelOf(point('a', 'half_bar', 1)), 'H');
});

test('events are sorted into the three lanes the spec asks for', () => {
  assert.equal(E.laneOf(point('a', 'guitar_attention_in', 1)), 'attention');
  assert.equal(E.laneOf(point('a', 'meter_change', 1)), 'marker');
  assert.equal(E.laneOf(point('a', 'something_from_the_future', 1)), 'other');
});

test('an in and an out of the same part become one span', () => {
  const spans = E.pairAttention([
    point('a', 'guitar_attention_in', 72.5),
    point('b', 'guitar_attention_out', 80.2),
  ]);
  assert.equal(spans.length, 1);
  assert.equal(spans[0].part, 'guitar');
  assert.equal(spans[0].start, 72.5);
  assert.equal(spans[0].end, 80.2);
  assert.equal(spans[0].inEventId, 'a');
  assert.equal(spans[0].outEventId, 'b');
});

test('an in with no out stays a point: no end is invented for it', () => {
  assert.deepEqual(E.pairAttention([point('a', 'guitar_attention_in', 10)]), []);
});

test('an out with no in stays a point as well', () => {
  assert.deepEqual(E.pairAttention([point('a', 'guitar_attention_out', 10)]), []);
});

test('an out before an in never produces a span that runs backwards', () => {
  const spans = E.pairAttention([
    point('a', 'bass_attention_out', 5),
    point('b', 'bass_attention_in', 9),
  ]);
  assert.deepEqual(spans, []);
});

test('both are still there as points, whatever the pairing did', () => {
  const events = E.normalizeEvents([
    point('a', 'guitar_attention_in', 72.5),
    point('b', 'guitar_attention_out', 80.2),
  ]);
  assert.equal(events.length, 2);
  assert.deepEqual(events.map(e => e.eventId), ['a', 'b']);
  assert.deepEqual(events.map(e => e.shape), ['in', 'out']);
});

test('two parts pair independently and never with each other', () => {
  const spans = E.pairAttention([
    point('g1', 'guitar_attention_in', 0),
    point('d1', 'drum_attention_in', 1),
    point('g2', 'guitar_attention_out', 4),
    point('d2', 'drum_attention_out', 5),
  ]);
  assert.equal(spans.length, 2);
  const guitar = spans.find(s => s.part === 'guitar');
  const drum = spans.find(s => s.part === 'drum');
  assert.deepEqual([guitar.start, guitar.end], [0, 4]);
  assert.deepEqual([drum.start, drum.end], [1, 5]);
});

test('a second in takes over as the open one, and the first is left a point', () => {
  const spans = E.pairAttention([
    point('a', 'guitar_attention_in', 0),
    point('b', 'guitar_attention_in', 2),
    point('c', 'guitar_attention_out', 5),
  ]);
  assert.equal(spans.length, 1);
  assert.deepEqual([spans[0].start, spans[0].end], [2, 5]);
  assert.equal(spans[0].inEventId, 'b');
});

test('events are sorted by time, and the audio time is untouched', () => {
  const events = E.normalizeEvents([
    point('late', 'guitar_attention_in', 30),
    point('early', 'meter_change', 4),
  ]);
  assert.deepEqual(events.map(e => e.eventId), ['early', 'late']);
  assert.equal(events[1].time, 30);
});

test('an interval from the file keeps its own ends and its own shape', () => {
  const events = E.normalizeEvents([
    { eventId: 's', type: 'attention_span', kind: 'span', time: 3, endTime: 9, payload: { part: 'bass' } },
  ]);
  assert.equal(events[0].shape, 'span');
  assert.equal(events[0].label, 'B');
  assert.equal(events[0].start, 3);
  assert.equal(events[0].end, 9);
});

test('overlapping items stack; items far apart share a level', () => {
  const one = E.stackLevels([{ start: 0, end: 100 }, { start: 50, end: 150 }, { start: 400, end: 450 }], 12);
  assert.deepEqual(one.levels, [0, 1, 0]);
  assert.equal(one.count, 2);
});

test('a point gets a minimum width, so two taps close together still stack', () => {
  const two = E.stackLevels([{ start: 0, end: 0 }, { start: 3, end: 3 }], 12);
  assert.deepEqual(two.levels, [0, 1]);
});

test('the filter list is what is in the file, in a stable order', () => {
  const types = E.filterTypes(E.normalizeEvents([
    point('a', 'guitar_attention_in', 1),
    point('b', 'meter_change', 2),
    point('c', 'guitar_attention_in', 3),
  ]));
  assert.deepEqual(types, [
    { type: 'guitar_attention_in', lane: 'attention', label: 'G' },
    { type: 'meter_change', lane: 'marker', label: 'M' },
  ]);
});
