/**
 * The listening queue: one spot at a time, straight from the report to your ears.

 * What was wrong with the report page: it told you *where* to listen and then made
 * you find the minute by hand, once per spot. This page turns the whole library into
 * one list of spots - 61 places, 5 of them marked as the first pass - and each one
 * plays only its own window and stops there.

 * The judgement stays human: the page asks the one question that spot can answer and
 * records your answer as a verdict (real problem / not a problem / unclear) so it can
 * be handed back and used to set the admission thresholds later. Nothing here decides
 * which reading is right.

 * The pure parts are up top so they can be tested without audio (same split as
 * pairing.js and timing.js).
 */
globalThis.MGQueue = (function () {
  'use strict';

  function formatClock(seconds) {
    if (!isFinite(seconds) || seconds < 0) return '—';
    const whole = Math.floor(seconds);
    const minutes = Math.floor(whole / 60);
    const rest = whole % 60;
    return (minutes < 10 ? '0' : '') + minutes + ':' + (rest < 10 ? '0' : '') + rest;
  }

  /** Stable id for a spot: the same place must not be recorded twice under two keys. */
  function spotKey(spot) {
    return spot.song + '@' + Math.round(spot.start);
  }

  /** The window actually played: a spot is never longer than it needs to be. */
  function windowOf(spot) {
    const start = isFinite(spot.listenStart) ? spot.listenStart : spot.start;
    const end = isFinite(spot.listenEnd) ? spot.listenEnd : spot.end;
    return { start: Math.max(0, start), end: Math.max(start + 1, end) };
  }

  /** Has the playhead run past the end of the window? (checked on every frame) */
  function pastWindow(time, spot) {
    return time >= windowOf(spot).end;
  }

  function nextIndex(count, index, step) {
    if (!count) return 0;
    return ((index + step) % count + count) % count;
  }

  /**
   * Audio URL for a spot: the same base + path form the validator uses
   * (`music=` base + the document's own file name), so one folder serves both.
   * Each path segment is encoded, the slashes are not - the path stays a path.
   */
  function spotAudioUrl(base, audioPath) {
    if (!audioPath) return '';
    if (/^[a-z][a-z0-9+.-]*:/i.test(audioPath) || audioPath.startsWith('//')) return audioPath;
    const encoded = String(audioPath).split('/').map(encodeURIComponent).join('/');
    if (!base) return encoded;
    return base.replace(/\/+$/, '') + '/' + encoded;
  }

  function tally(spots, verdicts) {
    const out = { real: 0, fine: 0, unclear: 0, left: 0 };
    spots.forEach((spot) => {
      const verdict = verdicts[spotKey(spot)];
      if (verdict === 'real') out.real += 1;
      else if (verdict === 'fine') out.fine += 1;
      else if (verdict === 'unclear') out.unclear += 1;
      else out.left += 1;
    });
    return out;
  }

  /** The verdict list, as text, to hand back to whoever sets the thresholds. */
  function verdictLines(spots, verdicts) {
    const label = { real: '真问题', fine: '不是问题', unclear: '说不清' };
    return spots.filter((spot) => verdicts[spotKey(spot)]).map((spot) => [
      label[verdicts[spotKey(spot)]],
      spot.song,
      formatClock(spot.start) + '–' + formatClock(spot.end),
      spot.queueClass,
      'support=' + spot.support,
    ].join('\t'));
  }

  return {
    formatClock: formatClock,
    spotKey: spotKey,
    windowOf: windowOf,
    pastWindow: pastWindow,
    nextIndex: nextIndex,
    spotAudioUrl: spotAudioUrl,
    tally: tally,
    verdictLines: verdictLines,
  };
})();

/* ------------------------------------------------------------------ */
/* The page. Nothing below runs without a document.                    */
/* ------------------------------------------------------------------ */
(function () {
  'use strict';
  if (typeof document === 'undefined') return;
  const Q = globalThis.MGQueue;
  const STORE = 'mgmcs-queue-verdicts';
  const el = (id) => document.getElementById(id);
  const audio = el('audio');
  let data = null;
  let spots = [];
  let index = 0;
  let verdicts = {};
  let watching = false;
  let loop = false;

  function loadVerdicts() {
    try {
      verdicts = JSON.parse(localStorage.getItem(STORE) || '{}') || {};
    } catch (err) {
      verdicts = {};
    }
  }

  function saveVerdicts() {
    try {
      localStorage.setItem(STORE, JSON.stringify(verdicts));
    } catch (err) { /* private mode: the session still works, it just is not kept */ }
  }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (ch) => (
      { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
  }

  function drawList() {
    el('list').innerHTML = spots.map((spot, order) => {
      const verdict = verdicts[Q.spotKey(spot)];
      const mark = verdict === 'real' ? '真' : verdict === 'fine' ? '否'
        : verdict === 'unclear' ? '？' : (spot.firstPass ? '★' : '');
      return '<li class="' + (order === index ? 'now ' : '') + (verdict ? 'done' : '')
        + '"><button data-order="' + order + '">'
        + '<span class="mark">' + mark + '</span>'
        + '<span class="who">' + escapeHtml(spot.song) + '</span>'
        + '<span class="when">' + Q.formatClock(spot.start) + '</span>'
        + '<span class="kind">' + escapeHtml(spot.queueClass) + '</span>'
        + '</button></li>';
    }).join('');
    el('list').querySelectorAll('button[data-order]').forEach((button) => {
      button.addEventListener('click', () => { index = Number(button.dataset.order); show(); });
    });
    const counts = Q.tally(spots, verdicts);
    el('tally').textContent = '已判 ' + (counts.real + counts.fine + counts.unclear)
      + ' / ' + spots.length + '　真问题 ' + counts.real + '　不是问题 ' + counts.fine
      + '　说不清 ' + counts.unclear;
  }

  function drawSpot() {
    const spot = spots[index];
    if (!spot) return;
    const win = Q.windowOf(spot);
    const pairs = (spot.pairs || []).map((pair) => '<li><b>' + escapeHtml(pair.pair)
      + '</b>：' + escapeHtml(pair.label) + '（support = ' + pair.support
      + ' matched measures）</li>').join('');
    const others = (spot.others || []).map((line) => '<li class="muted">'
      + escapeHtml(line) + '</li>').join('');
    const back = spot.reconverges && isFinite(spot.reconvergeAt)
      ? '<p class="back">合回来：' + Q.formatClock(spot.reconvergeAt) + '</p>' : '';
    el('card').innerHTML =
      '<div class="head"><span class="idx">' + (index + 1) + ' / ' + spots.length + '</span>'
      + (spot.firstPass ? '<span class="badge">第一遍</span>' : '')
      + '<span class="badge">' + escapeHtml(spot.queueClass) + '</span>'
      + '<span class="badge muted">' + escapeHtml(spot.song) + '</span></div>'
      + '<h2>' + escapeHtml(spot.title || spot.song) + '</h2>'
      + '<p class="when">听 <b>' + Q.formatClock(win.start) + '–' + Q.formatClock(win.end)
      + '</b>（这一段原本 ' + spot.spanSeconds + ' 秒' + (spot.longSpan ? '，长段：听开头这一截就够判断' : '') + '）</p>'
      + '<p class="question">' + escapeHtml(spot.question || '') + '</p>'
      + (pairs ? '<ul class="pairs">' + pairs + others + '</ul>' : '')
      + back
      + (spot.suspicion ? '<p class="suspicion">' + escapeHtml(spot.suspicion) + '</p>' : '')
      + '<p class="links"><a href="' + escapeHtml(data.validator + '?doc=' + encodeURIComponent(spot.song)
        + '&music=' + encodeURIComponent(data.musicBase) + '&at=' + Math.round(spot.start))
      + '" target="_blank" rel="noopener">在验证器里看五条网格</a>'
      + '　<a href="timing.html" target="_blank" rel="noopener">回到体检表</a></p>';
    const verdict = verdicts[Q.spotKey(spot)];
    el('verdict-state').textContent = verdict
      ? '已判：' + ({ real: '真问题', fine: '不是问题', unclear: '说不清' }[verdict])
      : '还没判';
  }

  function loadAudio() {
    const spot = spots[index];
    if (!spot) return;
    const url = Q.spotAudioUrl(data.musicBase, spot.audioPath);
    if (!url) {
      el('status').textContent = '这首歌没有音频路径（文档里没写 audioFileName），只能去验证器里手动拖。';
      return;
    }
    if (audio.dataset.for !== spot.song) {
      audio.dataset.for = spot.song;
      audio.src = url;
    }
    el('status').textContent = '音频：' + spot.audioPath;
  }

  function playWindow() {
    const spot = spots[index];
    if (!spot || !audio.src) return;
    const win = Q.windowOf(spot);
    audio.currentTime = win.start;
    watching = true;
    const started = audio.play();
    if (started && started.catch) started.catch(() => { watching = false; });
  }

  function show(autoplay) {
    const spot = spots[index];
    if (!spot) return;
    drawSpot();
    drawList();
    loadAudio();
    if (autoplay) playWindow();
    else { audio.pause(); watching = false; }
  }

  function step(delta) {
    index = Q.nextIndex(spots.length, index, delta);
    show(true);
  }

  function judge(verdict) {
    const spot = spots[index];
    if (!spot) return;
    verdicts[Q.spotKey(spot)] = verdict;
    saveVerdicts();
    drawList();
    drawSpot();
    step(1);
  }

  audio.addEventListener('timeupdate', () => {
    if (!watching) return;
    const spot = spots[index];
    if (Q.pastWindow(audio.currentTime, spot)) {
      if (loop) { audio.currentTime = Q.windowOf(spot).start; return; }
      audio.pause();
      watching = false;
    }
  });

  el('play').addEventListener('click', () => {
    if (audio.paused) playWindow();
    else { audio.pause(); watching = false; }
  });
  el('replay').addEventListener('click', playWindow);
  el('prev').addEventListener('click', () => step(-1));
  el('next').addEventListener('click', () => step(1));
  el('loop').addEventListener('change', (event) => { loop = event.target.checked; });
  el('rate').addEventListener('change', (event) => { audio.playbackRate = Number(event.target.value); });
  el('scope').addEventListener('change', () => { build(0); });
  el('v-real').addEventListener('click', () => judge('real'));
  el('v-fine').addEventListener('click', () => judge('fine'));
  el('v-unclear').addEventListener('click', () => judge('unclear'));
  el('copy').addEventListener('click', () => {
    const text = Q.verdictLines(spots, verdicts).join('\n') || '（还没判过）';
    el('log').textContent = text;
    if (navigator.clipboard) navigator.clipboard.writeText(text).catch(() => {});
  });
  document.addEventListener('keydown', (event) => {
    if (event.target.tagName === 'INPUT' || event.target.tagName === 'SELECT') return;
    if (event.key === ' ') { event.preventDefault(); if (audio.paused) playWindow(); else { audio.pause(); watching = false; } }
    else if (event.key === 'ArrowRight' || event.key === 'n') step(1);
    else if (event.key === 'ArrowLeft' || event.key === 'p') step(-1);
    else if (event.key === 'r') playWindow();
    else if (event.key === '1') judge('real');
    else if (event.key === '2') judge('fine');
    else if (event.key === '3') judge('unclear');
  });

  function build(startIndex) {
    const scope = el('scope').value;
    const all = data.queue || [];
    // 默认只放"分诊没解释掉"的那几处：人工只听这些。其余仍然可查，
    // 但要自己切过去——"自动解释掉了"不等于"不用看"，只是不必专门听。
    spots = all.filter((spot) => {
      if (scope === 'human') return spot.review;
      if (scope === 'first') return spot.firstPass;
      return true;
    });
    if (!spots.length) {
      el('card').innerHTML = '<p class="muted">这一档里没有要听的：'
        + (scope === 'human' ? '分诊把所有异常都解释掉了。' : '队列是空的。') + '</p>';
      el('list').innerHTML = '';
      return;
    }
    index = Math.min(startIndex, Math.max(0, spots.length - 1));
    el('intro').textContent = (scope === 'human' ? '分诊没解释掉的 ' : scope === 'first' ? '第一遍 ' : '全部 ')
      + spots.length + ' 处'
      + '。空格播放/暂停，← → 换一处，1 真问题 / 2 不是问题 / 3 说不清。';
    show(false);
  }

  fetch('timing_health.json', { cache: 'no-cache' })
    .then((response) => {
      if (!response.ok) throw new Error('HTTP ' + response.status);
      return response.json();
    })
    .then((loaded) => {
      data = loaded;
      loadVerdicts();
      build(0);
    })
    .catch((error) => {
      el('card').innerHTML = '<p class="bad">读不到 timing_health.json：' + escapeHtml(error.message)
        + '。先在 mgmcs_validator 里跑 <code>python tools/docs/timing_health_table.py --write</code>。</p>';
    });
})();
