/**
 * The timing health page: what it shows and what it links to.

 * Two things here matter more than the rest, and both are rules the researcher
 * asked for: REVIEW songs come first (a clean song must never bury a broken one),
 * and every segment links to the exact second in the validator, because "listen
 * only to the anomaly" is the whole point of the page.
 */
import test from 'node:test';
import assert from 'node:assert/strict';

await import('../static/timing.js');
const T = globalThis.MGTiming;

const data = {
  validator: 'index.html',
  musicBase: 'http://127.0.0.1:8125',
  songs: [
    { song: 'taishou', class: 'clean', status: 'OK', segments: [], readings: [], notes: [] },
    { song: 'flyway', class: 'persistent ±2-beat disagreement', status: 'REVIEW',
      segments: [{ song: 'flyway', start: 135, end: 155, support: 9,
                   pairs: [{ pair: 'mapperatorinator↔BeatThis', label: '±2 拍（半小节）', support: 9 },
                           { pair: 'madmom↔BeatThis', label: '±2 拍（半小节）', support: 5 }],
                   others: ['mapperatorinator↔madmom：aligned'], reconverges: true,
                   reconvergeAt: 158.4 }],
      readings: [], notes: ['疑似 BeatThis 2-beat counting：它自己报的小节里有 20 个只数了 2 拍 —— 待人工确认'] },
    { song: 'lulu', class: 'local drift', status: 'REVIEW', segments: [], readings: [], notes: [] },
  ],
};

test('a review song sorts above a clean one', () => {
  assert.deepEqual(T.sortedSongs(data).map((row) => row.song), ['flyway', 'lulu', 'taishou']);
});

test('among review songs the one with more supporting measures comes first', () => {
  const rows = T.sortedSongs({ songs: [
    { song: 'a', class: 'persistent ±2-beat disagreement', segments: [{ support: 3 }] },
    { song: 'b', class: 'persistent ±2-beat disagreement', segments: [{ support: 40 }] },
  ] });
  assert.deepEqual(rows.map((row) => row.song), ['b', 'a']);
});

test('a segment links to that second, with the audio base, in the validator', () => {
  const url = T.validatorUrl(data, 'flyway', 135);
  assert.equal(url, 'index.html?doc=flyway&music=http%3A%2F%2F127.0.0.1%3A8125&at=135');
});

test('a song with no audio base still gets a document link', () => {
  const url = T.validatorUrl({ validator: 'index.html' }, 'flyway', 135);
  assert.equal(url, 'index.html?doc=flyway&at=135');
});

test('the segment line says the range, the pair and the support', () => {
  const line = T.segmentLine(data.songs[1].segments[0]);
  assert.equal(line, '02:15–02:35 · 2 对来源都指这里 · support = 9 matched measures');
});

test('a segment with a single disagreeing pair names it instead of counting', () => {
  const line = T.segmentLine({ start: 135, end: 140, support: 4,
                               pairs: [{ pair: 'madmom↔BeatThis', label: '±2 拍（半小节）' }] });
  assert.equal(line, '02:15–02:20 · madmom↔BeatThis ±2 拍（半小节） · support = 4 matched measures');
});

test('the suspicious cause is quoted as an explanation, not as the fact', () => {
  assert.match(T.suspicion(data.songs[1]), /待人工确认/);
  assert.equal(T.suspicion(data.songs[0]), '');
});

test('a status maps to the row colour the page uses', () => {
  assert.equal(T.statusClass({ status: 'OK' }), 'ok');
  assert.equal(T.statusClass({ status: 'REVIEW' }), 'review');
});
