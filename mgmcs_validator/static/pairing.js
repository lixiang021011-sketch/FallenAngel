/**
 * Which audio file goes with which document.

 * The browser cannot look inside a folder by itself, so this is the part that
 * does the guessing once somebody has dropped or picked some files. It only ever
 * decides *which two files to open together*: a bad pairing is a wasted click,
 * never a wrong number, and the page always says which pair it chose.

 * Kept separate from the page so the matching rule can be tested without a
 * browser and without audio.
 */
globalThis.MGPairing = (function () {
  'use strict';

  const AUDIO_NAME = /\.(wav|mp3|flac|m4a|aac|ogg|oga|opus|wma|aif|aiff)$/i;
  const JSON_NAME = /\.json$/i;

  /** Names compare by their words: separators and case say nothing. */
  function normalizeName(text) {
    return String(text).toLowerCase()
      .replace(/[_\-–—·・.,()（）[\]]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function stripAudioExtension(name) {
    return String(name).replace(AUDIO_NAME, '');
  }

  /**
   * 0 to 100: how much this audio file name looks like that document's song.

   * Exact name is 100, one containing the other is 70, otherwise the share of
   * the document's words that appear in the file name (so "米津玄師 アイネクライネ.flac"
   * still pairs with a title that carries the artist and a translation). Nothing
   * in common is 0, which is what makes "no pair" a possible answer.
   */
  function pairScore(audioName, doc) {
    if (!doc || !doc.song) return 0;
    const name = normalizeName(stripAudioExtension(audioName));
    let best = 0;
    for (const candidate of [doc.song.audioFileName, doc.song.title]) {
      if (!candidate) continue;
      const other = normalizeName(String(candidate).replace(/\.[a-z0-9]+$/i, ''));
      if (!other) continue;
      if (name === other) {
        best = Math.max(best, 100);
      } else if (name.includes(other) || other.includes(name)) {
        best = Math.max(best, 70);
      } else {
        const wanted = other.split(' ').filter((token) => token.length > 1);
        if (!wanted.length) continue;
        const hits = wanted.filter((token) => name.includes(token)).length;
        best = Math.max(best, Math.round(60 * hits / wanted.length));
      }
    }
    return best;
  }

  /** The best pairing among what was dropped, or null when there is nothing. */
  function bestPair(audioNames, documents) {
    let best = null;
    (audioNames || []).forEach((name, audioIndex) => {
      (documents || []).forEach((doc, documentIndex) => {
        const score = pairScore(name, doc);
        if (!best || score > best.score) best = { audioIndex, documentIndex, score };
      });
    });
    return best;
  }

  /** A validator result is not something to load as an input. */
  function isResultFile(raw) {
    return !!(raw && typeof raw === 'object'
      && (raw.gridJudgements || raw.changeMarkers || raw.eventJudgements));
  }

  return { AUDIO_NAME, JSON_NAME, normalizeName, pairScore, bestPair, isResultFile };
}());
