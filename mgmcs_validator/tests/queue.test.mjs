/**
 * The listening queue: the window it plays, where it goes next, and what it records.

 * The failure that matters here is playing the wrong seconds: a queue that plays the
 * whole song, or starts 2 seconds too late and misses the bar line, wastes the one
 * thing this page is supposed to save. So the window rule is tested directly, along
 * with the two things that make the pass resumable: a stable spot id and a tally.
 */
import test from 'node:test';
import assert from 'node:assert/strict';

await import('../static/queue.js');
const Q = globalThis.MGQueue;

const spot = {
  song: 'flyway', start: 135.5, end: 155.7, listenStart: 133.5, listenEnd: 157.7,
  queueClass: 'persistent ±2-beat disagreement', support: 9, audioPath: 'halyosy - Flyway.ogg',
};

test('the played window is the spot, with a little run-up and run-out', () => {
  assert.deepEqual(Q.windowOf(spot), { start: 133.5, end: 157.7 });
});

test('a spot without an explicit window falls back to its own bounds', () => {
  const bare = { song: 'x', start: 10, end: 30 };
  assert.deepEqual(Q.windowOf(bare), { start: 10, end: 30 });
});

test('a window can never run before zero', () => {
  const head = { song: 'x', start: 0.5, end: 12, listenStart: -1.5, listenEnd: 14 };
  assert.deepEqual(Q.windowOf(head), { start: 0, end: 14 });
});

test('playback stops at the end of the window, not at the end of the song', () => {
  assert.equal(Q.pastWindow(157.6, spot), false);
  assert.equal(Q.pastWindow(157.7, spot), true);
  assert.equal(Q.pastWindow(200, spot), true);
});

test('the queue wraps around in both directions', () => {
  assert.equal(Q.nextIndex(5, 4, 1), 0);
  assert.equal(Q.nextIndex(5, 0, -1), 4);
  assert.equal(Q.nextIndex(0, 0, 1), 0);
});

test('a spot keeps one id however many times it is shown', () => {
  assert.equal(Q.spotKey(spot), Q.spotKey({ ...spot }));
  assert.equal(Q.spotKey({ song: 'flyway', start: 135.6 }), 'flyway@136');
});

test('the audio URL is the music base plus the document path, segment-encoded', () => {
  assert.equal(Q.spotAudioUrl('http://127.0.0.1:8125', 'halyosy - Flyway.ogg'),
    'http://127.0.0.1:8125/halyosy%20-%20Flyway.ogg');
  assert.equal(Q.spotAudioUrl('http://127.0.0.1:8125/', 'Cö shu Nie - 消えちゃう前に.ogg'),
    'http://127.0.0.1:8125/C%C3%B6%20shu%20Nie%20-%20%E6%B6%88%E3%81%88%E3%81%A1%E3%82%83%E3%81%86%E5%89%8D%E3%81%AB.ogg');
  assert.equal(Q.spotAudioUrl('', 'audio/x.wav'), 'audio/x.wav');
  assert.equal(Q.spotAudioUrl('http://x', ''), '');
});

test('the tally counts what is left, not just what is done', () => {
  const spots = [spot, { song: 'lulu', start: 100 }, { song: 'yue', start: 200 }];
  const verdicts = { [Q.spotKey(spot)]: 'real', 'lulu@100': 'fine' };
  assert.deepEqual(Q.tally(spots, verdicts), { real: 1, fine: 1, unclear: 0, left: 1 });
});

test('the verdict list can be handed back as lines', () => {
  const lines = Q.verdictLines([spot], { [Q.spotKey(spot)]: 'unclear' });
  assert.deepEqual(lines, ['说不清\tflyway\t02:15–02:35\tpersistent ±2-beat disagreement\tsupport=9']);
});

test('spots nobody judged are not in the hand-back list', () => {
  assert.deepEqual(Q.verdictLines([spot], {}), []);
});
