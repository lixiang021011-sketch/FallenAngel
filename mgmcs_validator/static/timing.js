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

  /**
   * Sources whose *own* output looks wrong (their health, not their disagreement).
   * A workbench reading built on a Beat This grid that never locked is worth
   * seeing on the summary line: it says the disagreement may be the grid's fault
   * rather than the music's, which is a different thing to go and check.
   */
  function suspectSources(row) {
    return (row.readings || []).filter((reading) => reading.health !== 'ok')
      .map((reading) => reading.name);
  }

  function statusClass(row) {
    return row.status === 'OK' ? 'ok' : 'review';
  }

  /** What the triage layer decided to do with this song (see tools/docs/triage.py). */
  function triageOf(row) {
    return row.triage || {};
  }

  /** One line: the diagnosis, and who got downgraded if anyone did. */
  function reviewText(row) {
    const triage = triageOf(row);
    if (!triage.diagnosis) return '';
    if (!triage.suspectSource) return triage.diagnosis;
    // "形状指向谁"和"降级了谁"是两件事：只报形状的那种没有降级任何人，
    // 写成"降级 X"会让人以为已经处理过了。
    const downgrades = ['source_measure_count_error', 'source_self_anomaly',
                        'human_confirmed_source'].indexOf(triage.diagnosis) >= 0;
    return triage.diagnosis + (downgrades ? '（降级 ' + triage.suspectSource + '）'
                                          : '（形状指向 ' + triage.suspectSource + '，未降级）');
  }

  /** The only question that decides where a human should spend ears. */
  function needsHuman(row) {
    return Boolean(triageOf(row).needsHumanReview);
  }

  /**
   * The downstream verdict: can bar-level work use this song, and where must it be
   * careful. Downstream reads this field and nothing else - that is the whole point
   * of the admission layer.
   */
  function admissionOf(row) {
    return row.admission || {};
  }

  return {
    severity: severity,
    sortedSongs: sortedSongs,
    clockText: clockText,
    validatorUrl: validatorUrl,
    segmentLine: segmentLine,
    suspicion: suspicion,
    suspectSources: suspectSources,
    triageOf: triageOf,
    reviewText: reviewText,
    needsHuman: needsHuman,
    admissionOf: admissionOf,
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
  const filter = document.getElementById('filter');
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
      + '这里只列能给出小节头的读法（librosa 只给拍、不给小节头，所以不在这一层）。'
      + '五条网格并排、逐拍核对请点上面的“在时间轴上打开”。</p>';
  }

  function songHtml(row) {
    const review = row.status !== 'OK';
    const segments = (row.segments || []);
    const first = segments[0];
    const where = first ? T.clockText(first.start) + '–' + T.clockText(first.end) : '—';
    const suspicion = T.suspicion(row);
    const suspect = T.suspectSources(row);
    const human = T.needsHuman(row);
    const byHuman = T.triageOf(row).attribution === 'human';
    const reviewLine = human ? '这个要人听一次'
      : byHuman ? '人工已听过并定案，不必再听' : '这条已经自动解释掉了，不必专门听';
    const flags = (row.flags || []).map((flag) => '<span class="flag">' + escapeHtml(flag) + '</span>').join('')
      + suspect.map((name) => '<span class="flag warn">' + escapeHtml(name)
          + ' health 有问题（它自己那条不算数）</span>').join('');
    const triageLine = '<div class="triage ' + (human ? 'human' : 'auto') + '">'
      + escapeHtml(T.reviewText(row) || '—') + ' · ' + reviewLine + '</div>';
    const admission = T.admissionOf(row);
    const admissionLine = admission.eligibility
      ? '<div class="admission ' + escapeHtml(admission.eligibility) + '">下游准入：'
        + escapeHtml({ eligible: 'eligible（可整曲用）', caution: 'caution（可用，但标出的区段要带保留）',
                       ineligible: 'ineligible（先别用）' }[admission.eligibility]
                     || admission.eligibility)
        + (admission.soundReadings && admission.soundReadings.length
            ? '｜可用读法：' + escapeHtml(admission.soundReadings.join('、')) : '')
        + (admission.downgraded && admission.downgraded.length
            ? '｜已降级：' + escapeHtml(admission.downgraded.map((d) => d.source).join('、')) : '')
        + '</div>' : '';
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
      + triageLine
      + admissionLine
      + body + '</article>';
  }

  function draw() {
    const mode = filter.value;
    const rows = T.sortedSongs(data).filter((row) => {
      if (mode === 'human') return T.needsHuman(row);
      if (mode === 'review') return row.status !== 'OK';
      return true;
    });
    list.innerHTML = rows.map(songHtml).join('');
    if (!rows.length) {
      list.innerHTML = '<p class="muted">这一档里没有歌。'
        + (mode === 'human' ? '（这是好消息：说明没有需要人听的）' : '') + '</p>';
    }
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
    const human = (data.songs || []).filter((row) => T.needsHuman(row)).map((row) => row.song);
    summary.innerHTML = '<span class="chip">共 ' + total + ' 首</span>'
      + '<span class="chip review">要人听 ' + human.length + '</span>'
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
      filter.addEventListener('change', draw);
    })
    .catch((error) => {
      list.innerHTML = '<p class="bad">读不到 timing_health.json：' + escapeHtml(error.message)
        + '。先在 mgmcs_validator 目录里跑 <code>python tools/docs/timing_health_table.py --write</code>。</p>';
    });
})();
