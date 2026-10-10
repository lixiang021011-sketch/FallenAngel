/**
 * The timing health page: SYSTEM FINDS THE ANOMALY, YOU LISTEN TO THE ANOMALY.

 * Three levels, in this order and no more:

 *   1. song summary      status + which kind of disagreement + where it starts
 *   2. anomaly segments  click one and it goes to that second in the validator
 *   3. the raw readings  only when you actually have to adjudicate by hand

 * The page must never be "five grids side by side": that is what the validator is
 * for, and it is one click away. What is missing from the validator is the answer
 * to *where is this song risky and who disagrees*, which is what this page holds.

 * Everything here is derived from `static/timing_health.json`, which comes out of
 * `tools/docs/timing_health_table.py`. The page does not compute any timing of its
 * own: a page that recomputes the numbers is a second source of truth.

 * Kept DOM-free at the top so the wording and the links can be tested without a
 * browser (the same split pairing.js and boundary.js use).
 */
globalThis.MGTiming = (function () {
  'use strict';

  /** REVIEW first, then by how much evidence there is behind the disagreement. */
  function severity(row) {
    const order = {
      'persistent ±1-beat disagreement': 0,
      'persistent ±2-beat disagreement': 1,
      'continuous slide': 2,
      'unresolved': 3,
      'local drift': 4,
      'clean': 5,
    };
    const key = Object.prototype.hasOwnProperty.call(order, row['class']) ? row['class'] : 'unresolved';
    const worst = (row.segments || []).reduce((top, segment) => Math.max(top, segment.support || 0), 0);
    return [order[key], -worst, row.song];
  }

  function sortedSongs(data) {
    return (data.songs || []).slice().sort((left, right) => {
      const a = severity(left), b = severity(right);
      for (let index = 0; index < a.length; index += 1) {
        if (a[index] < b[index]) return -1;
        if (a[index] > b[index]) return 1;
      }
      return 0;
    });
  }

  function clockText(seconds) {
    if (!isFinite(seconds) || seconds < 0) return '—';
    const whole = Math.floor(seconds);
    const minutes = Math.floor(whole / 60);
    const rest = whole % 60;
    return (minutes < 10 ? '0' : '') + minutes + ':' + (rest < 10 ? '0' : '') + rest;
  }

  /**
   * A link that opens the song at that second.

   * `doc=` and `music=` are the validator's own parameters; `at=` is the one this
   * page adds (seconds, or mm:ss). Without a music folder serving the audio the
   * link still opens the document - it just cannot play it.
   */
  function validatorUrl(data, song, seconds) {
    const pieces = ['doc=' + encodeURIComponent(song)];
    if (data.musicBase) pieces.push('music=' + encodeURIComponent(data.musicBase));
    if (isFinite(seconds)) pieces.push('at=' + Math.max(0, Math.round(seconds)));
    return (data.validator || 'index.html') + '?' + pieces.join('&');
  }

  /**
   * One line for a segment: the range, how many source pairs disagree there, and
   * the largest support behind it. Where it is exactly *which* pair is listed
   * inside the segment - one place on the timeline is one entry, not five.
   */
  function segmentLine(segment) {
    const pairs = segment.pairs || [];
    const distinct = pairs.map((pair) => pair.pair).filter((name, index, all) => all.indexOf(name) === index);
    const who = distinct.length === 1 ? pairs[0].pair + ' ' + pairs[0].label
      : distinct.length + ' 对来源都指这里';
    return clockText(segment.start) + '–' + clockText(segment.end) + ' · ' + who
      + ' · support = ' + segment.support + ' matched measures';
  }

  /** The explanation sentence, when the table could point at a likely cause. */
  function suspicion(row) {
    const found = (row.notes || []).filter((text) => text.indexOf('疑似') === 0);
    return found.length ? found[0] : '';
  }

  function statusClass(row) {
    return row.status === 'OK' ? 'ok' : 'review';
  }

  return {
    severity: severity,
    sortedSongs: sortedSongs,
    clockText: clockText,
    validatorUrl: validatorUrl,
    segmentLine: segmentLine,
    suspicion: suspicion,
    statusClass: statusClass,
  };
})();

/* ------------------------------------------------------------------ */
/* The page itself. Nothing below runs without a document.             */
/* ------------------------------------------------------------------ */
(function () {
  'use strict';
  if (typeof document === 'undefined') return;
  const T = globalThis.MGTiming;
  const list = document.getElementById('songs');
  const summary = document.getElementById('counts');
  const onlyReview = document.getElementById('only-review');
  let data = null;
  const open = {};

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (ch) => (
      { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
  }

  function segmentHtml(song, segment) {
    const back = segment.reconverges && isFinite(segment.reconvergeAt)
      ? '<div class="back">合回来：' + T.clockText(segment.reconvergeAt) + '</div>' : '';
    const who = (segment.pairs || []).map((pair) => '<li><span class="pair">'
      + escapeHtml(pair.pair) + '</span>：' + escapeHtml(pair.label)
      + '（support = ' + pair.support + ' matched measures）</li>').join('');
    const others = (segment.others || []).map((line) => '<li class="muted">'
      + escapeHtml(line) + '</li>').join('');
    const inside = data.validator ? T.validatorUrl(data, song, segment.start) : '#';
    return '<div class="segment">'
      + '<div class="head">' + escapeHtml(T.segmentLine(segment)) + '</div>'
      + '<ul class="others">' + who + others + '</ul>' + back
      + '<a class="jump" href="' + escapeHtml(inside) + '" target="_blank" rel="noopener">'
      + '在时间轴上打开 ' + T.clockText(segment.start) + '</a>'
      + '</div>';
  }

  function readingsHtml(row) {
    const rows = (row.readings || []).map((reading) => '<tr>'
      + '<td>' + escapeHtml(reading.name) + '</td>'
      + '<td class="' + (reading.health === 'ok' ? 'ok' : 'suspect') + '">'
      + escapeHtml(reading.health) + '</td>'
      + '<td>' + escapeHtml(String(reading.measures)) + '</td>'
      + '<td>' + escapeHtml(reading.meterMix || '—') + '</td></tr>').join('');
    return '<table class="readings"><tr><th>来源</th><th>它自己的 health</th>'
      + '<th>小节头</th><th>它自己数的每小节拍数</th></tr>' + rows + '</table>'
      + '<p class="muted">health 只说明这条来源**自己输出得像不像话**，不代表音乐真值。'
      + '五条网格并排、逐拍核对请点上面的“在时间轴上打开”。</p>';
  }

  function songHtml(row) {
    const review = row.status !== 'OK';
    const segments = (row.segments || []);
    const first = segments[0];
    const where = first ? T.clockText(first.start) + '–' + T.clockText(first.end) : '—';
    const suspicion = T.suspicion(row);
    const flags = (row.flags || []).map((flag) => '<span class="flag">' + escapeHtml(flag) + '</span>').join('');
    const layer = row.layerA || {};
    const body = open[row.song]
      ? '<div class="detail">'
        + (segments.length ? segments.map((s) => segmentHtml(row.song, s)).join('')
                           : '<p class="muted">B 层没有错位段：这首歌的风险在 A 层（拍子局部漂）。</p>')
        + '<details class="raw"><summary>展开原始读法（' + (row.readings || []).length + ' 条）</summary>'
        + readingsHtml(row) + '</details>'
        + (row.notes || []).map((text) => '<p class="note">' + escapeHtml(text) + '</p>').join('')
        + '<p class="muted">人那条：' + escapeHtml(row.human || '—') + '</p>'
        + '</div>'
      : '';
    return '<article class="song ' + T.statusClass(row) + (open[row.song] ? ' open' : '') + '">'
      + '<button class="row" data-song="' + escapeHtml(row.song) + '">'
      + '<span class="name">' + escapeHtml(row.song) + '</span>'
      + '<span class="status">' + escapeHtml(row.status) + '</span>'
      + '<span class="class">' + escapeHtml(row['class']) + '</span>'
      + '<span class="where">' + where + '</span>'
      + '<span class="a">A：' + escapeHtml((layer.hit || '—') + ' · 漂移 ' + (layer.drift || '—')) + '</span>'
      + '</button>'
      + (suspicion ? '<div class="suspicion">' + escapeHtml(suspicion) + '</div>' : '')
      + (flags ? '<div class="flags">' + flags + '</div>' : '')
      + body + '</article>';
  }

  function draw() {
    const rows = T.sortedSongs(data).filter((row) => !onlyReview.checked || row.status !== 'OK');
    list.innerHTML = rows.map(songHtml).join('');
    list.querySelectorAll('button.row').forEach((button) => {
      button.addEventListener('click', () => {
        const song = button.dataset.song;
        const wasOpen = open[song];
        Object.keys(open).forEach((key) => { open[key] = false; });
        open[song] = !wasOpen;
        draw();
      });
    });
  }

  function drawCounts() {
    const counts = data.counts || {};
    const chips = Object.keys(counts).sort().map((name) => '<span class="chip ' +
      (name === 'clean' ? 'ok' : 'review') + '">' + escapeHtml(name) + ' ' + counts[name] + '</span>');
    const total = (data.songs || []).length;
    const review = (data.songs || []).filter((row) => row.status !== 'OK').length;
    summary.innerHTML = '<span class="chip">共 ' + total + ' 首</span>'
      + '<span class="chip review">REVIEW ' + review + '</span>'
      + '<span class="chip ok">OK ' + (total - review) + '</span>' + chips.join('');
  }

  fetch('timing_health.json', { cache: 'no-cache' })
    .then((response) => {
      if (!response.ok) throw new Error('HTTP ' + response.status);
      return response.json();
    })
    .then((loaded) => {
      data = loaded;
      drawCounts();
      draw();
      onlyReview.addEventListener('change', draw);
    })
    .catch((error) => {
      list.innerHTML = '<p class="bad">读不到 timing_health.json：' + escapeHtml(error.message)
        + '。先在 mgmcs_validator 目录里跑 <code>python tools/docs/timing_health_table.py --write</code>。</p>';
    });
})();
