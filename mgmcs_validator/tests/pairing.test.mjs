/**
 * Which audio belongs to which document.

 * The browser cannot look inside a folder, so this guess is the only automatic
 * thing about loading files. It is allowed to be wrong - the page says which pair
 * it chose - but it is not allowed to be silently wrong, which is why "nothing
 * matches" is one of the answers it can return.
 */
import test from 'node:test';
import assert from 'node:assert/strict';

await import('../static/pairing.js');
const P = globalThis.MGPairing;

function doc(title, audioFileName) {
  return { song: { title: title || '', audioFileName: audioFileName || '' } };
}

test('the same name is a perfect match', () => {
  assert.equal(P.pairScore('song_001.flac', doc('song_001')), 100);
  assert.equal(P.pairScore('Song 001.WAV', doc('song 001')), 100);
});

test('an extension and punctuation do not matter', () => {
  assert.equal(P.pairScore('Yuuri - Dry Flower.mp3', doc('Yuuri — Dry Flower')), 100);
  // A track number in front of it is not part of the name: that is containment,
  // which still pairs, one step down.
  assert.equal(P.pairScore('01 - Yuuri - Dry Flower.mp3', doc('Yuuri — Dry Flower')), 70);
});

test('two of three title words in the file name is a partial match', () => {
  // The document's title carries the artist, the song and a translation; the
  // file name has the first two. The third word is missing, so this is not a
  // containment match - it is most of a match, which is enough to pair with.
  assert.equal(P.pairScore('12 米津玄師 - アイネクライネ (320k).flac',
    doc('米津玄師 — アイネクライネ (小小的我)')), 40);
});

test('sharing most of the words is a partial match', () => {
  const score = P.pairScore('Kenshi Yonezu - アイネクライネ.flac',
    doc('米津玄師 (よねづ けんし) — アイネクライネ (小小的我)'));
  assert.ok(score > 0 && score < 70, String(score));
});

test('nothing in common scores nothing, so no pair is a possible answer', () => {
  assert.equal(P.pairScore('some other song.mp3', doc('米津玄師 — アイネクライネ')), 0);
  assert.equal(P.pairScore('anything.mp3', doc('')), 0);
  assert.equal(P.pairScore('anything.mp3', null), 0);
});

test('the audioFileName in the document counts as well as the title', () => {
  assert.equal(P.pairScore('take-3.wav', doc('', 'take-3.wav')), 100);
});

test('every audio in a folder finds its own document', () => {
  const names = ['A - 怪盗.flac', 'B - アイネクライネ.flac', 'C - 大正浪漫.flac'];
  const documents = [doc('back number — 怪盗'), doc('米津玄師 — アイネクライネ'),
    doc('YOASOBI — 大正浪漫')];
  names.forEach((name, index) => {
    assert.equal(P.bestPair([name], documents).documentIndex, index, name);
  });
});

test('out of a whole folder the pair chosen is one of the real pairs', () => {
  const names = ['A - 怪盗.flac', 'B - アイネクライネ.flac', 'C - 大正浪漫.flac'];
  const documents = [doc('back number — 怪盗'), doc('米津玄師 — アイネクライネ'),
    doc('YOASOBI — 大正浪漫')];
  const choice = P.bestPair(names, documents);
  assert.equal(choice.audioIndex, choice.documentIndex);
  // Titles of different lengths score different fractions, so the highest score
  // in a folder is not a quality claim - only the pairing it points at.
  assert.equal(P.bestPair(['B - アイネクライネ.flac'], documents).documentIndex, 1);
});

test('a saved result file is not mistaken for an input document', () => {
  assert.equal(P.isResultFile({ schemaVersion: '0.1', gridJudgements: [] }), true);
  assert.equal(P.isResultFile({ schemaVersion: '0.1', changeMarkers: [] }), true);
  assert.equal(P.isResultFile({ calibrations: [], events: [] }), false);
  assert.equal(P.isResultFile(null), false);
});
