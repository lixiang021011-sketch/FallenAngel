/**
 * The validator's screen.

 * One rule runs through all of it: the tool reads, the person judges. Nothing
 * here infers a tempo, snaps an event to a beat, or decides which grid is right.
 * Every click either moves the playhead or writes down something a person said.
 */
(function () {
  'use strict';

  const M = globalThis.MGMCS;
  const E = globalThis.MGEvents;
  // 边界关联层（派生关系，单独一个文件）。它只读文档，不写回任何东西。
  const B = M.boundary;
  const P = globalThis.MGPairing;

  const GRID_COLORS = ['var(--grid-1)', 'var(--grid-2)', 'var(--grid-3)',
    'var(--grid-4)', 'var(--grid-5)', 'var(--grid-6)'];
  const MIN_PX_PER_SECOND = 2;
  const MAX_PX_PER_SECOND = 900;
  const EVENT_ROW = 19;
  const POINT_WIDTH = 18;
  const TICK_SNAP_PX = 5;
  /**
   * How far a bar may be from a whole number of beats and still be whole.

   * Bar lines are written to the millisecond, so a 4-beat bar comes back as
   * 3.9998 and an exact comparison would call every bar in the song irregular.
   * A fiftieth of a beat is far below anything a person could mean by "a short
   * bar" - the half-bars this tool is for are two beats off - and far above the
   * rounding. A bar of 3.5 beats still reads as 3.50.
   */
  const WHOLE_BAR_TOLERANCE = 0.02;

  const el = (id) => document.getElementById(id);
  const audio = el('audio');
  const timeline = el('timeline');
  const content = el('content');
  const lanes = el('lanes');
  const playhead = el('playhead');
  const tooltip = el('tooltip');
  const STORAGE_PREFIX = 'mgmcs.validation.';

  const state = {
    doc: null,
    validation: null,
    duration: 0,
    durationFrom: '',
    pxPerSecond: 60,
    time: 0,
    playing: false,
    hiddenGrids: new Set(),
    soloGrid: '',
    filters: new Set(),
    allTypes: [],
    selection: null,
    follow: true,
    /** Clicking a bar or a beat starts playing there. The spec's original default
     *  was "paused stays paused"; the researcher asked for the other one, and the
     *  checkbox in the transport keeps both available. */
    playOnSeek: true,
    scrubbing: false,
    programmaticScroll: 0,
    dirty: false,
  };

  /** bar elements and event elements, kept so zooming is a restyle, not a rebuild. */
  const view = { bars: [], events: [], lanes: {} };

  let virtualClock = 0;
  let lastCoordsAt = 0;

  // ------------------------------------------------------------------ helpers

  function clamp(value, lo, hi) {
    return Math.max(lo, Math.min(hi, value));
  }

  function gridName(index) {
    return 'Grid ' + String.fromCharCode(65 + (index % 26));
  }

  function gridColor(index) {
    return GRID_COLORS[index % GRID_COLORS.length];
  }

  function grids() {
    return state.doc ? state.doc.grids : [];
  }

  function gridById(id) {
    return grids().find((g) => g.gridId === id) || null;
  }

  function gridIndex(id) {
    return grids().findIndex((g) => g.gridId === id);
  }

  function visibleEvents() {
    if (!state.doc) return [];
    return state.doc.events.filter((event) => state.filters.has(event.type));
  }

  function isGridDrawn(id) {
    if (state.soloGrid) return state.soloGrid === id;
    return !state.hiddenGrids.has(id);
  }

  function describeGrid(grid) {
    const base = grid.beatsPerBar + '/' + grid.beatUnit + ' · ' + formatBpm(grid.bpm) + ' BPM';
    if (!grid.irregular) return base;
    /** A grid read off a bar line list: say so, and say how many bars are not
     *  whole. The count is the interesting part - it is the half-bars. */
    let odd = 0;
    for (let bar = 0; bar < grid.barTimes.length; bar += 1) {
      if (Math.abs(M.barBeats(grid, bar) - grid.beatsPerBar) > WHOLE_BAR_TOLERANCE) odd += 1;
    }
    return base + ' · 小节线列表 ' + grid.barTimes.length + ' 根'
      + (odd ? '，其中 ' + odd + ' 根不是 ' + grid.beatsPerBar + ' 拍' : '');
  }

  function formatBpm(bpm) {
    return Number.isInteger(bpm) ? String(bpm) : bpm.toFixed(1);
  }

  function songKey() {
    if (!state.doc) return '';
    const song = state.doc.song || {};
    return (song.songId || song.audioFileName || song.title || 'unnamed');
  }

  function flash(message) {
    const note = el('source-note');
    note.textContent = message;
    note.classList.add('warn');
    clearTimeout(flash.timer);
    flash.timer = setTimeout(() => { note.textContent = ''; }, 3200);
  }

  function say(html) {
    el('details').innerHTML = html;
  }

  function coordText(grid, time) {
    const at = M.coordinateAt(grid, time);
    return 'bar ' + at.bar + ' · beat ' + M.formatBeat(at.beat);
  }

  // -------------------------------------------------------------- the document

  function setDocument(raw, fileName) {
    const doc = M.parseSongDocument(raw);
    state.doc = doc;
    state.selection = null;
    state.soloGrid = '';
    state.hiddenGrids = new Set();
    state.filters = new Set(doc.events.map((e) => e.type));

    const stored = loadStoredValidation();
    state.validation = stored || M.newValidation(doc.song.songId);
    // Keep the record pointing at the song even if the file only learned its id
    // after the first judgement was made.
    if (doc.song.songId) state.validation.songId = doc.song.songId;
    state.dirty = false;

    showWarnings(doc.problems, fileName, stored);
    updateDuration();
    rebuild();
    renderPanel();
    saveState((stored ? '已恢复这个文件上次的检验记录。' : '') + '已导入 ' +
      (fileName || 'JSON') + '。');
  }

  function showWarnings(problems, fileName, restored) {
    const box = el('warnings');
    const lines = [];
    if (fileName) lines.push('JSON：' + fileName);
    if (restored) lines.push('这个文件的检验记录从本机缓存里恢复了。');
    if (problems && problems.length) {
      box.classList.add('bad');
      box.innerHTML = '<strong>这份 JSON 有 ' + problems.length + ' 处需要你留意：</strong>'
        + '<ul>' + problems.map((p) => '<li>' + escapeHtml(p) + '</li>').join('') + '</ul>';
      box.hidden = false;
      return;
    }
    box.classList.remove('bad');
    if (lines.length) {
      box.innerHTML = escapeHtml(lines.join(' · '));
      box.hidden = false;
    } else {
      box.hidden = true;
    }
  }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (c) => (
      { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function updateDuration() {
    const shown = isFinite(audio.duration) && audio.duration > 0 ? audio.duration : NaN;
    if (isFinite(shown)) {
      state.duration = shown;
      state.durationFrom = '音频时长';
    } else if (state.doc && isFinite(state.doc.song.durationSeconds)) {
      state.duration = state.doc.song.durationSeconds;
      state.durationFrom = 'JSON 里的 durationSeconds';
    } else {
      let last = 0;
      for (const event of (state.doc ? state.doc.events : [])) {
        last = Math.max(last, isFinite(event.endTime) ? event.endTime : event.time);
      }
      for (const grid of grids()) last = Math.max(last, grid.originTime);
      state.duration = last > 0 ? last + 10 : 0;
      state.durationFrom = last > 0 ? '推算（音频和 JSON 都没给时长）' : '';
    }
    checkDurationMatch();
  }

  function checkDurationMatch() {
    if (!state.doc || !isFinite(state.doc.song.durationSeconds)) return;
    if (!isFinite(audio.duration) || audio.duration <= 0) return;
    const diff = Math.abs(audio.duration - state.doc.song.durationSeconds);
    if (diff > 0.5) {
      const box = el('warnings');
      const line = '音频长 ' + M.formatClock(audio.duration) + '，JSON 说这首歌长 '
        + M.formatClock(state.doc.song.durationSeconds) + '（差 ' + diff.toFixed(1)
        + ' 秒）。可能是拿错版本——也可能只是播放器报的时长不准：实测同一台手机把 32 秒的'
        + '节拍器报成 15 秒、把 3:42 的歌报成 4:15，所以这一条只当提示看。';
      box.innerHTML += '<hr><strong>' + escapeHtml(line) + '</strong>';
      box.hidden = false;
    }
  }

  // ------------------------------------------------------------- presentation

  function pxPerSecondMin() {
    const width = timeline.clientWidth || 900;
    return state.duration > 0 ? Math.max(0.2, (width - 20) / state.duration) : 1;
  }

  function zoomSliderToPps(value) {
    const lo = pxPerSecondMin();
    const hi = MAX_PX_PER_SECOND;
    const t = clamp(value / 1000, 0, 1);
    return lo * Math.pow(hi / lo, t);
  }

  function ppsToSlider(pps) {
    const lo = pxPerSecondMin();
    const hi = MAX_PX_PER_SECOND;
    const t = Math.log(clamp(pps, lo, hi) / lo) / Math.log(hi / lo);
    return Math.round(t * 1000);
  }

  function setZoom(pps, anchorTime, anchorClientX) {
    const rect = timeline.getBoundingClientRect();
    const localX = anchorClientX == null ? timeline.clientWidth / 2 : anchorClientX - rect.left;
    const anchor = anchorTime == null ? xToTime(timeline.scrollLeft + localX) : anchorTime;
    state.pxPerSecond = clamp(pps, pxPerSecondMin(), MAX_PX_PER_SECOND);
    layoutTimeline();
    scrollTo(anchor * state.pxPerSecond - localX);
    updateZoomRead();
  }

  function updateZoomRead() {
    el('zoom').value = String(ppsToSlider(state.pxPerSecond));
    el('zoom-read').textContent = state.pxPerSecond.toFixed(state.pxPerSecond < 10 ? 2 : 0) + ' px/s';
  }

  function scrollTo(left) {
    state.programmaticScroll += 1;
    timeline.scrollLeft = Math.max(0, left);
  }

  function xToTime(x) {
    return state.pxPerSecond > 0 ? x / state.pxPerSecond : 0;
  }

  // ------------------------------------------------------------- the timeline

  /** Full rebuild: the lanes, and every bar and event in them. */
  function rebuild() {
    lanes.innerHTML = '';
    view.bars = [];
    view.events = [];
    view.lanes = {};
    el('empty').hidden = !!(state.doc || audio.src);

    if (!state.doc && !audio.src) { content.style.width = '100%'; return; }

    const ruler = lane('ruler', '时间');
    view.lanes.ruler = ruler;
    // The ruler is the scrub bar: press anywhere on it and drag. The musical
    // reason to drag rather than click is A/B listening - move a little, listen
    // again - and it has to keep working while the view follows the playhead,
    // which is why following is suspended for the length of the drag.
    ruler.addEventListener('pointerdown', (ev) => {
      if (ev.button !== 0) return;
      ev.preventDefault();
      state.scrubbing = true;
      const move = (e) => {
        const box = ruler.getBoundingClientRect();
        seekOrRefuse(xToTime(e.clientX - box.left));
      };
      const end = () => {
        state.scrubbing = false;
        ruler.removeEventListener('pointermove', move);
        ruler.removeEventListener('pointerup', end);
        ruler.removeEventListener('pointercancel', end);
      };
      try { ruler.setPointerCapture(ev.pointerId); } catch (err) { /* no capture */ }
      ruler.addEventListener('pointermove', move);
      ruler.addEventListener('pointerup', end);
      ruler.addEventListener('pointercancel', end);
      move(ev);
    });

    const events = state.doc ? E.normalizeEvents(visibleEvents()) : [];
    const spans = state.doc ? E.pairAttention(E.normalizeEvents(state.doc.events)) : [];
    const labelFor = { attention: '事件 · 声部注意', marker: '事件 · 变化点', other: '事件 · 其他' };
    for (const laneName of E.LANES) {
      const laneEl = lane('events', labelFor[laneName]);
      laneEl.dataset.lane = laneName;
      view.lanes['events:' + laneName] = laneEl;
      buildEventLane(laneEl, laneName, events, spans);
    }

    grids().forEach((grid, index) => {
      const laneEl = lane('grid', gridName(index) + ' · ' + grid.gridId + ' · '
        + describeGrid(grid) + ' · '
        + (grid.irregular ? '第一条小节线 ' : '起点 ') + M.formatClock(grid.originTime));
      laneEl.style.setProperty('--grid-color', gridColor(index));
      laneEl.dataset.grid = grid.gridId;
      view.lanes['grid:' + grid.gridId] = laneEl;
      buildGridLane(laneEl, grid, index);
      laneEl.addEventListener('click', (ev) => onGridClick(ev, grid));
      laneEl.hidden = !isGridDrawn(grid.gridId);
    });

    layoutTimeline();
    updateZoomRead();
  }

  function lane(kind, label) {
    const laneEl = document.createElement('div');
    laneEl.className = 'lane ' + kind;
    const tag = document.createElement('span');
    tag.className = 'lane-label';
    tag.textContent = label;
    laneEl.appendChild(tag);
    lanes.appendChild(laneEl);
    return laneEl;
  }

  function buildGridLane(laneEl, grid, index) {
    if (state.duration <= 0) return;
    const range = M.barRange(grid, 0, state.duration);
    const color = gridColor(index);
    for (let bar = range.first; bar <= range.last + 1; bar += 1) {
      const beats = M.barBeats(grid, bar);
      const oddBar = Math.abs(beats - grid.beatsPerBar) > WHOLE_BAR_TOLERANCE;
      const barEl = document.createElement('div');
      barEl.className = 'bar' + (Math.abs(bar % 2) === 1 ? ' alt' : '') + (oddBar ? ' short' : '');
      barEl.dataset.grid = grid.gridId;
      barEl.dataset.bar = String(bar);
      barEl.style.setProperty('--grid-color', color);
      const number = document.createElement('span');
      number.className = 'bar-no';
      number.textContent = String(bar);
      barEl.appendChild(number);
      // 边界邻域（±0.5 拍）。必须放在 .bar-no **之后**：布局里用 firstChild 控制
      // 小节编号的显示/隐藏，抢了第一个位置编号就再也不会藏起来。
      // 位置与宽度交给 CSS（--beat 由 layoutTimeline 写好），所以缩放不用重算。
      const near = document.createElement('span');
      near.className = 'boundary-near';
      barEl.appendChild(near);
      laneEl.appendChild(barEl);
      view.bars.push({ el: barEl, grid, bar });
    }
  }

  function buildEventLane(laneEl, laneName, events, spans) {
    const items = [];
    for (const span of spans) {
      const source = events.find((e) => e.eventId === span.inEventId);
      if (source && source.lane === laneName) items.push({ kind: 'span', span, source });
      else if (!source) {
        const raw = state.doc.events.find((e) => e.eventId === span.inEventId);
        if (raw && E.laneOf(raw) === laneName && state.filters.has(raw.type)) {
          items.push({ kind: 'span', span, source: E.normalizeEvents([raw])[0] });
        }
      }
    }
    for (const event of events) {
      if (event.lane === laneName) items.push({ kind: 'marker', event });
    }
    items.sort((a, b) => timeOfItem(a) - timeOfItem(b));

    for (const item of items) {
      const markerEl = document.createElement('span');
      markerEl.className = 'evt';
      const event = item.kind === 'span' ? item.source : item.event;
      markerEl.dataset.event = event.eventId;
      markerEl.dataset.item = item.kind;
      markerEl.classList.add('part-' + (partClass(event.part)));
      markerEl.classList.add('shape-' + (item.kind === 'span' ? 'span' : event.shape));
      markerEl.classList.add(item.kind === 'span' ? 'span' : 'point');
      markerEl.textContent = item.kind === 'span' ? event.label + ' 注意区间' : event.label;
      markerEl.addEventListener('click', (ev) => {
        ev.stopPropagation();
        selectEvent(event.eventId);
      });
      markerEl.addEventListener('mousemove', (ev) => showEventTip(ev, event));
      markerEl.addEventListener('mouseleave', hideTip);
      laneEl.appendChild(markerEl);
      view.events.push({ el: markerEl, item, laneName });
    }
  }

  function partClass(part) {
    if (!part) return 'other';
    if (part === 'drums' || part === 'drum') return 'drums';
    if (part === 'vocal' || part === 'voice') return 'vocal';
    if (['guitar', 'bass', 'piano'].includes(part)) return part;
    return 'other';
  }

  function timeOfItem(item) {
    return item.kind === 'span' ? item.span.start : item.event.time;
  }

  /** Everything that changes with zoom: positions only, no new elements. */
  function layoutTimeline() {
    const pps = state.pxPerSecond;
    content.style.width = Math.max(timeline.clientWidth, state.duration * pps + 24) + 'px';

    for (const entry of view.bars) {
      const { el: barEl, grid, bar } = entry;
      const barSeconds = M.barSeconds(grid, bar);
      const beatWidth = (60 / grid.bpm) * pps;
      const left = M.timeOf(grid, bar) * pps;
      barEl.style.left = left + 'px';
      barEl.style.width = (barSeconds * pps) + 'px';
      barEl.style.setProperty('--beat', beatWidth.toFixed(3) + 'px');
      barEl.firstChild.style.display = (barSeconds * pps) >= 30 ? '' : 'none';
      entry.beatWidth = beatWidth;
    }

    for (const laneName of E.LANES) {
      const laneEl = view.lanes['events:' + laneName];
      if (!laneEl) continue;
      const here = view.events.filter((e) => e.laneName === laneName);
      const boxes = here.map((e) => {
        const item = e.item;
        const start = timeOfItem(item) * pps;
        const end = item.kind === 'span' ? item.span.end * pps : start;
        return { start, end, entry: e };
      });
      const stacked = E.stackLevels(boxes, POINT_WIDTH);
      boxes.forEach((box, i) => {
        const level = stacked.levels[i];
        const markerEl = box.entry.el;
        const isSpan = box.entry.item.kind === 'span';
        const width = isSpan ? Math.max(6, box.end - box.start) : POINT_WIDTH;
        markerEl.style.left = (isSpan ? box.start : box.start - POINT_WIDTH / 2) + 'px';
        markerEl.style.width = width + 'px';
        markerEl.style.top = (2 + level * EVENT_ROW) + 'px';
      });
      laneEl.style.height = Math.max(22, stacked.count * EVENT_ROW + 6) + 'px';
    }

    if (view.lanes.ruler) layoutRuler(view.lanes.ruler);
    layoutPlayhead();
    layoutRulerSelection();
    // 对比模式要跟着网格的数量/身份走：文档换了就重填下拉框并重画一次。
    renderDiffControls();
    renderDiff();
  }

  function layoutRuler(laneEl) {
    laneEl.querySelectorAll('.tick').forEach((n) => n.remove());
    if (state.duration <= 0) return;
    const step = tickStep(state.pxPerSecond);
    for (let t = 0; t <= state.duration + 0.001; t += step) {
      const tick = document.createElement('div');
      tick.className = 'tick';
      tick.style.left = (t * state.pxPerSecond) + 'px';
      const label = document.createElement('span');
      label.textContent = step >= 1 ? M.formatClock(t).slice(0, -4) : t.toFixed(1) + 's';
      tick.appendChild(label);
      laneEl.appendChild(tick);
    }
  }

  function tickStep(pps) {
    const candidates = [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120];
    for (const step of candidates) if (step * pps >= 70) return step;
    return 300;
  }

  function layoutPlayhead() {
    playhead.hidden = state.duration <= 0;
    playhead.style.transform = 'translateX(' + (state.time * state.pxPerSecond) + 'px)';
  }

  function layoutRulerSelection() {
    for (const bar of content.querySelectorAll('.bar.selected')) bar.classList.remove('selected');
    if (!state.selection || state.selection.kind !== 'bar') return;
    const entry = view.bars.find((b) => b.grid.gridId === state.selection.gridId
      && b.bar === state.selection.bar);
    if (entry) entry.el.classList.add('selected');
  }

  // ------------------------------------------------------------ clicks and play

  function onGridClick(ev, grid) {
    const barEl = ev.target.closest('.bar');
    if (!barEl) return;
    const box = barEl.getBoundingClientRect();
    const offset = ev.clientX - box.left;
    const bar = Number(barEl.dataset.bar);
    const beatsInBar = Math.max(1, Math.round(M.barBeats(grid, bar)));
    const beatWidth = (60 / grid.bpm) * state.pxPerSecond;
    // Beat precision everywhere. The old rule only took a beat when the pointer was
    // within a few pixels of a tick line and otherwise threw the click away to beat
    // 0 - which is the one granularity nobody asked for, when the ask is "let me
    // start on any beat".
    const beat = clamp(Math.round(offset / beatWidth), 0, beatsInBar - 1);
    const target = M.timeOf(grid, bar, beat);
    state.selection = { kind: 'bar', gridId: grid.gridId, bar, beat, time: target };
    layoutRulerSelection();
    renderDetails();
    if (seekOrRefuse(target)) {
      highlightNearbyEvents(grid, bar, beat);
      if (state.playOnSeek) play();
    }
  }

  function seekOrRefuse(target) {
    if (!isFinite(target)) return false;
    if (state.duration > 0 && (target < 0 || target > state.duration)) {
      flash('该网格位置超出音频范围（' + M.formatClock(target) + '），没有跳转。');
      return false;
    }
    seek(target);
    return true;
  }

  function seek(target) {
    state.time = clamp(target, 0, state.duration > 0 ? state.duration : target);
    if (audio.src) {
      try { audio.currentTime = state.time; } catch (err) { /* not seekable yet */ }
    }
    layoutPlayhead();
    updateCoords();
    followPlayhead(true);
  }

  function selectEvent(eventId) {
    const raw = state.doc ? state.doc.events.find((e) => e.eventId === eventId) : null;
    if (!raw) return;
    state.selection = { kind: 'event', eventId, time: raw.time };
    for (const entry of view.events) {
      entry.el.classList.toggle('selected', entry.el.dataset.event === eventId);
    }
    renderDetails();
    seekOrRefuse(raw.time);
  }

  function highlightNearbyEvents(grid, bar, beat) {
    if (!state.doc) return;
    const from = M.timeOf(grid, bar, 0) - 0.35;
    const to = M.timeOf(grid, bar + 1, 0) + 0.35;
    const inside = state.doc.events.filter((e) => e.time >= from && e.time <= to);
    if (!inside.length) { renderDetails(); return; }
    const list = inside.map((e) => '<li>' + escapeHtml(e.type) + '　'
      + M.formatClock(e.time) + '</li>').join('');
    const head = '<p><strong>' + escapeHtml(gridName(gridIndex(grid.gridId)))
      + ' bar ' + bar + '</strong>　'
      + M.formatClock(M.timeOf(grid, bar, 0)) + ' 起</p>';
    say(head + '<p class="muted">这一小节附近有这些事件：</p><ul class="tiny">' + list + '</ul>');
  }

  function togglePlay() {
    if (audio.src) {
      if (audio.paused) audio.play().catch(() => {}); else audio.pause();
      return;
    }
    if (state.duration <= 0) return;
    state.playing = !state.playing;
    el('play').textContent = state.playing ? '⏸' : '▶';
    virtualClock = performance.now();
  }

  /** Start, rather than toggle. What clicking a beat wants. */
  function play() {
    if (audio.src) {
      audio.play().catch((err) => flash('播不了：' + err.message));
      return;
    }
    if (state.duration <= 0) return;
    state.playing = true;
    el('play').textContent = '⏸';
    virtualClock = performance.now();
  }

  function followPlayhead(force) {
    if (!state.follow || state.scrubbing) return;
    const x = state.time * state.pxPerSecond;
    const left = timeline.scrollLeft;
    const width = timeline.clientWidth;
    if (force || x < left + width * 0.08 || x > left + width * 0.75) {
      scrollTo(x - width * 0.25);
    }
  }

  // ------------------------------------------------------------------- panels

  function updateCoords() {
    const box = el('current');
    if (!state.doc || !grids().length) {
      box.innerHTML = '<p class="muted">还没有网格。</p>';
      return;
    }
    const rows = grids().map((grid, index) => {
      const dim = isGridDrawn(grid.gridId) ? '' : ' style="opacity:.45"';
      return '<div class="coord-row"' + dim + '><span class="key">'
        + escapeHtml(gridName(index)) + '</span><span class="val">'
        + coordText(grid, state.time) + '</span></div>';
    }).join('');
    box.innerHTML = '<p class="big-time">' + M.formatClock(state.time) + '</p>' + rows;
  }

  function renderPanel() {
    // Before anything is loaded there is no result file yet, and every panel
    // below reads it: one shared empty record keeps the first paint from
    // throwing and taking the clock down with it.
    if (!state.validation) state.validation = M.newValidation('');
    updateCoords();
    renderPhase();
    renderGrids();
    renderFilters();
    renderDetails();
    renderMarkers();
    updateClock();
  }

  // ---------------------------------------------------------------- Grid Pair Diff
  //
  // 选一对网格：三色高亮（shared / A-only / B-only）+ 逐小节重叠分的整首热力带 +
  // "跳到下一处热区"。全部来自 boundary.js 的派生关系，不写回文档。
  // 默认把机器拍点也算进来（这批文档里事件几乎只有它们），只把它们画淡一点；
  // 「只看人产生的事件」开关留给"纯人"的场合。

  /**
   * 一条派生结论的"资格"块。

   * 「Comparison quality: CAUTION」这一行**就是 analysisEligibility 的显示名**，
   * 不是第二套判断——底下没有任何本地的健康算法，只有从 boundary.js 拿来的那一个字段。
   * 扫读要求（研究者 2026-10-10）：
   *   eligible   → 不展开也能一眼扫过
   *   caution    → 一眼看出是哪条 source 拖的（下面 ✕ 那一行）
   *   ineligible → 一眼看出为什么全都不行
   * 所以每条 source 先给一行摘要（标记 + gridId + health + valid），
   * 多出来的理由收进 details，要原话再展开。
   */
  function eligibilityBlock(verdict) {
    const label = { eligible: 'ELIGIBLE', caution: 'CAUTION', ineligible: 'INELIGIBLE' };
    const sources = (verdict.sourceHealth || []).map((source) => {
      const mark = source.level === 'eligible' ? '✓' : '✕';
      const reasons = source.reasons || [];
      const head = reasons.length ? reasons[0] : '';
      return '<div class="coord-row"><span class="key">' + mark + ' ' + escapeHtml(source.gridId)
        + '</span><span class="val">health: ' + escapeHtml(String(source.health))
        + '　valid: ' + escapeHtml(String(source.valid)) + '</span></div>'
        + (head ? '<div class="fineprint">　' + escapeHtml(head) + '</div>' : '')
        + (reasons.length > 1
          ? '<details><summary class="fineprint">还有 ' + (reasons.length - 1) + ' 条理由</summary>'
            + '<div class="fineprint">' + reasons.slice(1).map(escapeHtml).join('<br>') + '</div></details>'
          : '');
    }).join('');
    return '<div class="record"><strong>Comparison quality: '
      + (label[verdict.analysisEligibility] || 'ELIGIBLE') + '</strong>'
      + '<div class="fineprint">（这一行就是 analysisEligibility 的显示名；下面列来源，没有第二套判断）</div>'
      + sources + '</div>';
  }

  function diffPair() {
    return { a: el('diff-a') ? el('diff-a').value : '', b: el('diff-b') ? el('diff-b').value : '' };
  }

  function diffOptions() {
    return { onlyHuman: !!(el('diff-human') && el('diff-human').checked) };
  }

  /** 两条网格的下拉框：文档换了就重填，尽量保住用户选的那一对。 */
  function renderDiffControls() {
    const list = grids();
    if (!el('diff-a') || !el('diff-b')) return;
    for (const id of ['diff-a', 'diff-b']) {
      const select = el(id);
      const keep = select.value;
      select.innerHTML = list.map((grid, index) => '<option value="' + escapeHtml(grid.gridId) + '">'
        + escapeHtml(gridName(index)) + '</option>').join('');
      if (list.some((grid) => grid.gridId === keep)) select.value = keep;
    }
    if (list.length >= 2 && el('diff-a').value === el('diff-b').value) {
      el('diff-b').value = list[1].gridId;
    }
  }

  /** 三色高亮 + 热力带 + 汇总。 */
  function renderDiff() {
    const box = el('diff-read');
    if (!box) return;
    document.querySelectorAll('.evt.diff-shared, .evt.diff-a-only, .evt.diff-b-only, .evt.diff-machine')
      .forEach((node) => node.classList.remove('diff-shared', 'diff-a-only', 'diff-b-only', 'diff-machine'));
    const pair = diffPair();
    if (!state.doc || !pair.a || !pair.b || pair.a === pair.b) {
      box.innerHTML = '<p class="fineprint">选两条不同的网格。</p>';
      state.diffRuns = [];
      return;
    }
    const options = diffOptions();
    const rows = B.pairDiff(state.doc, pair.a, pair.b, options);
    const byEvent = new Map(rows.map((row) => [row.eventId, row]));
    let marked = 0;
    for (const entry of view.events) {
      const row = byEvent.get(entry.el.dataset.event);
      if (!row) continue;
      entry.el.classList.add('diff-' + row.kind.replace('_', '-'));
      if (row.machine) entry.el.classList.add('diff-machine');
      marked += 1;
    }
    const scores = B.overlapScores(state.doc, pair.a, pair.b, options);
    const runs = B.hotRuns(scores, { minRunBars: 2, disagreement: 0.5 });
    const counts = { shared: 0, a_only: 0, b_only: 0 };
    for (const row of rows) counts[row.kind] += 1;
    const strip = scores.map((row) => {
      const badness = isFinite(row.overlap) ? 1 - row.overlap : null;
      const alpha = badness === null ? 0.06 : Math.max(0.06, Math.min(1, badness));
      const color = badness === null ? 'rgba(255,255,255,.06)'
        : 'rgba(255,138,171,' + alpha.toFixed(2) + ')';
      return '<span style="background:' + color + '" data-time="' + row.time + '" title="A bar '
        + row.barIndex + '（' + M.formatClock(row.time) + '）· B bar ' + row.bBarIndex + ' · 重叠 '
        + (isFinite(row.overlap) ? row.overlap.toFixed(2) : '无证据') + ' · A ' + row.aCount
        + ' 件 / B ' + row.bCount + ' 件 / 共同 ' + row.sharedCount + '"></span>';
    }).join('');
    box.innerHTML = '<p class="fineprint">事件：共同 <strong>' + counts.shared + '</strong> · 只 A '
      + counts.a_only + ' · 只 B ' + counts.b_only + '（参与高亮 ' + marked + ' 个；'
      + (options.onlyHuman ? '只看人产生的事件' : '机器拍点也在内，画得淡一点') + '）</p>'
      + eligibilityBlock(B.eligibilityReport([gridById(pair.a), gridById(pair.b)].filter(Boolean)))
      + '<div class="heat-strip">' + strip + '</div>'
      + '<p class="fineprint">整首热力带：一格一小节，越红越说明两条网格不像在解释同一批事件'
      + '（<span class="muted">空白格 = 这一节没有事件，不是"一致"</span>）。热区 ' + runs.length + ' 段'
      + (runs.length ? '，第一段在 bar ' + runs[0].startBar + '（' + M.formatClock(runs[0].aroundTime) + '）' : '') + '。</p>';
    state.diffRuns = runs;
    for (const cell of box.querySelectorAll('.heat-strip span')) {
      cell.addEventListener('click', () => {
        const target = Number(cell.dataset.time);
        if (seekOrRefuse(target)) play();
      });
    }
  }

  /** 跳到播放头之后的下一处热区（没有就绕回第一处）。 */
  function diffNext() {
    const runs = state.diffRuns || [];
    if (!runs.length) { flash('这一对网格没有热区（或还没选两条网格）。'); return; }
    const next = runs.find((run) => run.aroundTime > state.time + 0.05) || runs[0];
    if (seekOrRefuse(next.aroundTime)) play();
  }

  function renderPhase() {
    const box = el('phase');
    if (!state.doc || grids().length < 2) {
      box.innerHTML = '<p class="muted">至少要有两条同速度同拍号的网格。</p>';
      return;
    }
    const lines = [];
    for (let i = 0; i < grids().length; i += 1) {
      for (let j = i + 1; j < grids().length; j += 1) {
        const a = grids()[i];
        const b = grids()[j];
        const offset = M.phaseOffset(a, b);
        const nameA = gridName(i) + ' ↔ ' + gridName(j);
        if (!offset.comparable) {
          lines.push('<div class="record"><strong>' + nameA + '</strong><br>'
            + '<span class="muted">不可比：' + offset.reason + '，不做换算</span></div>');
          continue;
        }
        const half = M.halfBar(a, b, {
          validA: validTimes(a.gridId),
          validB: validTimes(b.gridId),
        });
        let halfLine = '';
        if (half.result === 'half_bar_transition_candidate') {
          halfLine = '<br><strong>≈ 半小节，而且有区间证据：</strong>' + escapeHtml(half.reason);
        } else if (half.result === 'half_bar_phase_candidate') {
          halfLine = '<br><strong>≈ 半小节相位差：</strong>' + escapeHtml(half.reason);
        } else if (half.result === 'no_candidate') {
          halfLine = '<br><span class="muted">不是半小节：' + escapeHtml(half.reason) + '</span>';
        }
        if (halfLine && half.reliable === false) {
          halfLine += '<br><span class="warn">信不过：两条网格的拍速差会让这个相位本身'
            + '晃 ±' + half.phaseUncertaintyBeat.toFixed(2) + ' 拍，比半小节的判定窗还大'
            + '——要判它，得用不依赖拍速的证据（小节内强弱交替）。</span>';
        }
        lines.push('<div class="record"><strong>' + nameA + '</strong><br>'
          + 'Δtime ' + offset.deltaTime.toFixed(3) + ' s<br>'
          + 'Δbeat ' + offset.deltaBeat.toFixed(3) + '<br>'
          + 'Δbar ' + offset.deltaBar.toFixed(3)
          + (Math.abs(offset.deltaBpm) >= 0.01
            ? '<br><span class="muted">速度相差 ' + Math.abs(offset.deltaBpm).toFixed(2)
              + ' BPM（每分钟漂 ' + Math.abs(offset.driftSecondsPerMinute).toFixed(2)
              + ' 秒）</span>'
            : '')
          + halfLine
          + '</div>'
          + eligibilityBlock(B.eligibilityReport([a, b])));
      }
    }
    // 局部相位偏移候选：只提示、不修正。带位置，而且要求连续若干根 boundary 支持——
    // 单根碰巧落在 1/2/3 拍附近不算（局部漂移会那样），所以这里把"几根"写出来。
    const local = B.phaseOffsetCandidates(state.doc);
    for (const candidate of local) {
      const nameA = gridName(gridIndex(candidate.gridA));
      const nameB = gridName(gridIndex(candidate.gridB));
      lines.push('<div class="record warn"><strong>' + escapeHtml(nameA) + ' ↔ '
        + escapeHtml(nameB) + '：'
        + (candidate.coversWhole ? '整首保持一个离散拍偏移' : '局部相位偏移候选')
        + '</strong><br>'
        + 'bar ' + candidate.barIndex + ' 起（' + M.formatClock(candidate.aroundTime) + '）'
        + '连续 ' + candidate.bars + ' 根边界保持 '
        + (candidate.offsetBeats > 0 ? '+' : '') + candidate.offsetBeats + ' 拍偏移<br>'
        + (candidate.coversWhole
          ? '<span class="muted">整首都如此 → 这是这两条网格之间的全局相位关系，'
            + '不是某一处的局部错位；要看漂移请对比上面那几行的 Δbeat / Δbar</span><br>'
          : '')
        + (candidate.phase.comparable
          ? '<span class="muted">两项的全局相位本身可比</span>'
          : '<span class="muted">全局相位不可比（' + escapeHtml(candidate.phase.reason)
            + '），所以这只是一处局部的读法差异</span>')
        + '<br><span class="muted">只是提示：不移动任何网格，也不说哪条对。</span></div>');
    }
    box.innerHTML = lines.join('');
  }

  /** The moments the researcher said a grid was usable, for the half-bar check. */
  function validTimes(gridId) {
    if (!state.validation) return [];
    return state.validation.gridJudgements
      .filter((j) => j.gridId === gridId && j.judgement === 'valid')
      .map((j) => j.atTime);
  }

  function renderGrids() {
    const box = el('grids');
    if (!state.doc || !grids().length) {
      box.innerHTML = '<p class="muted">JSON 里没有可用的校准。</p>';
      return;
    }
    box.innerHTML = grids().map((grid, index) => {
      const shown = isGridDrawn(grid.gridId);
      const judgements = state.validation.gridJudgements.filter((j) => j.gridId === grid.gridId);
      return '<div class="grid-row' + (shown ? '' : ' hidden') + '" style="--grid-color:'
        + gridColor(index) + '">'
        + '<div class="head"><span class="swatch"></span>'
        + '<span class="name">' + escapeHtml(gridName(index)) + '</span>'
        + '<span class="spec">' + escapeHtml(describeGrid(grid))
        + ' · ' + escapeHtml(grid.gridId) + '</span></div>'
        + '<div class="actions">'
        + '<button class="tiny" data-act="toggle" data-grid="' + escapeHtml(grid.gridId) + '">'
        + (shown ? '隐藏' : '显示') + '</button>'
        + '<button class="tiny" data-act="solo" data-grid="' + escapeHtml(grid.gridId) + '">'
        + (state.soloGrid === grid.gridId ? '取消 Solo' : 'Solo') + '</button>'
        + '<button class="tiny" data-act="judge" data-judge="valid" data-grid="'
        + escapeHtml(grid.gridId) + '">有效</button>'
        + '<button class="tiny" data-act="judge" data-judge="invalid" data-grid="'
        + escapeHtml(grid.gridId) + '">无效</button>'
        + '<button class="tiny" data-act="judge" data-judge="unsure" data-grid="'
        + escapeHtml(grid.gridId) + '">说不清</button>'
        + '</div>'
        + '<div class="count">已记 ' + judgements.length + ' 条判断'
        + (judgements.length ? '，最近：' + escapeHtml(judgementText(judgements[judgements.length - 1]))
          + ' @ ' + M.formatClock(judgements[judgements.length - 1].atTime) : '')
        + '</div></div>';
    }).join('');
  }

  function judgementText(record) {
    return { valid: '有效', invalid: '无效', unsure: '说不清' }[record.judgement] || record.judgement;
  }

  function renderFilters() {
    const box = el('filters');
    if (!state.doc || !state.doc.events.length) {
      box.innerHTML = '<p class="muted">这份 JSON 里没有事件。</p>';
      return;
    }
    const types = E.filterTypes(E.normalizeEvents(state.doc.events));
    box.innerHTML = types.map((entry) => {
      const on = state.filters.has(entry.type);
      return '<label class="filter-row"><input type="checkbox" data-type="'
        + escapeHtml(entry.type) + '"' + (on ? ' checked' : '') + '>'
        + '<span class="dot shape-' + escapeHtml(labelShape(entry.type, entry.lane)) + '"></span>'
        + '<span>' + escapeHtml(entry.type) + '</span>'
        + '<span class="lane-tag">' + escapeHtml(entry.label) + '</span></label>';
    }).join('');
    el('filters').querySelectorAll('input[type=checkbox]').forEach((input) => {
      input.addEventListener('change', () => {
        if (input.checked) state.filters.add(input.dataset.type);
        else state.filters.delete(input.dataset.type);
        rebuild();
      });
    });
  }

  function labelShape(type, lane) {
    if (lane === 'attention') return type.endsWith('_out') ? 'out' : 'in';
    if (type === 'meter_change') return 'meter';
    if (type === 'tempo_change') return 'tempo';
    if (type === 'half_bar') return 'halfbar';
    return 'dot';
  }

  function renderDetails() {
    if (!state.selection) {
      say('<p class="muted">点时间轴上的事件，或者点某条网格的一小节。</p>');
      return;
    }
    if (state.selection.kind === 'bar') {
      const grid = gridById(state.selection.gridId);
      if (!grid) return;
      const at = M.coordinateAt(grid, state.selection.time);
      const bar = state.selection.bar;
      const boundaryTime = M.barStart(grid, bar);
      const beatSeconds = B.beatSecondsOf(grid, bar);
      const related = B.associatedEvents(state.doc, grid.gridId, bar);
      const relatedRows = related.length
        ? related.map((relation) => '<div class="coord-row"><span class="key">'
            + (relation.relativeBeat >= 0 ? '+' : '') + relation.relativeBeat.toFixed(3)
            + ' 拍</span><span class="val">' + escapeHtml(relation.eventId) + '　'
            + relation.associationClass + '</span></div>').join('')
        : '<p class="fineprint">这根边界 ±0.5 拍内没有事件。</p>';
      say('<p><strong>' + escapeHtml(gridName(gridIndex(grid.gridId)))
        + '　bar ' + state.selection.bar + '　beat ' + M.formatBeat(state.selection.beat)
        + '</strong></p>'
        + '<div class="coord-row"><span class="key">这一拍在</span><span class="val">'
        + M.formatClock(state.selection.time) + '</span></div>'
        + '<div class="coord-row"><span class="key">播放头读作</span><span class="val">'
        + 'bar ' + at.bar + ' · beat ' + M.formatBeat(at.beat) + '</span></div>'
        + '<div class="coord-row"><span class="key">这根边界在</span><span class="val">'
        + M.formatClock(boundaryTime) + '（' + boundaryTime.toFixed(3) + ' s）</span></div>'
        + '<div class="coord-row"><span class="key">本地速度</span><span class="val">'
        + formatBpm(grid.bpm) + '　一拍 ' + beatSeconds.toFixed(3) + ' s</span></div>'
        + '<p class="fineprint">关联事件（按 |相对拍| 从近到远）：</p>' + relatedRows);
      return;
    }
    const event = state.doc.events.find((e) => e.eventId === state.selection.eventId);
    if (!event) return;
    const rows = grids().map((grid, index) => '<div class="coord-row"><span class="key">'
      + escapeHtml(gridName(index)) + '</span><span class="val">'
      + coordText(grid, event.time) + '</span></div>').join('');
    // 边界关联：每个事件 × 每条 grid 最多一条——问的是"离这条网格最近的那根边界什么关系"。
    const relations = B.boundaryAssociations(state.doc)
      .filter((relation) => relation.eventId === event.eventId);
    const byGrid = new Map(relations.map((relation) => [relation.gridId, relation]));
    const boundaryRows = grids().map((grid, index) => {
      const relation = byGrid.get(grid.gridId);
      const key = '<span class="key">' + escapeHtml(gridName(index)) + '</span>';
      if (!relation) {
        return '<div class="coord-row">' + key
          + '<span class="val muted">±0.5 拍内没有边界（none）</span></div>';
      }
      return '<div class="coord-row">' + key + '<span class="val">bar ' + relation.barIndex
        + '　' + (relation.relativeBeat >= 0 ? '+' : '') + relation.relativeBeat.toFixed(3)
        + ' 拍　' + relation.associationClass + '</span></div>';
    }).join('');
    const same = state.validation.eventJudgements.filter((j) => j.eventId === event.eventId);
    say('<p><strong>' + escapeHtml(event.type) + '</strong></p>'
      + '<div class="coord-row"><span class="key">eventId</span><span class="val">'
      + escapeHtml(event.eventId) + '</span></div>'
      + '<div class="coord-row"><span class="key">来源</span><span class="val">'
      + escapeHtml(event.source || '—') + '</span></div>'
      + '<div class="coord-row"><span class="key">audioTime</span><span class="val">'
      + M.formatClock(event.time) + '（' + event.time.toFixed(3) + ' s）</span></div>'
      + (event.kind === 'span' ? '<div class="coord-row"><span class="key">区间</span>'
        + '<span class="val">' + M.formatClock(event.time) + ' → '
        + M.formatClock(event.endTime) + '</span></div>' : '')
      + '<p class="fineprint">它在每条网格里的位置：</p>' + rows
      + '<p class="fineprint">离每条网格最近的那根小节边界：</p>' + boundaryRows
      + '<p class="fineprint">记下它更像哪条网格（可选）：</p>'
      + '<div class="two"><select id="pref-grid">'
      + '<option value="">（不指定）</option>'
      + grids().map((grid, index) => '<option value="' + escapeHtml(grid.gridId) + '">'
        + escapeHtml(gridName(index)) + ' · ' + escapeHtml(grid.gridId) + '</option>').join('')
      + '</select></div>'
      + '<input id="pref-note" placeholder="备注（可选）">'
      + '<div class="save-row" style="margin-top:6px"><button class="tiny" id="pref-add">'
      + '记下这条判断</button></div>'
      + (same.length ? '<div class="fineprint">已记 ' + same.length + ' 条：'
        + same.map((j) => escapeHtml(j.preferredGrid || '未指定')
          + (j.note ? '（' + escapeHtml(j.note) + '）' : '')).join('，') + '</div>' : ''));
    const add = el('pref-add');
    if (add) {
      add.addEventListener('click', () => {
        state.validation.eventJudgements.push({
          eventId: event.eventId,
          preferredGrid: el('pref-grid').value,
          note: el('pref-note').value.trim(),
          atTime: event.time,
        });
        markDirty();
        renderDetails();
      });
    }
  }

  function renderMarkers() {
    const box = el('markers');
    const list = state.validation.changeMarkers;
    if (!list.length) {
      box.innerHTML = '<p class="muted">还没有标记。</p>';
      return;
    }
    box.innerHTML = list.map((marker, i) => '<div class="record">'
      + '<span class="when">' + M.formatClock(marker.audioTime) + '</span>　'
      + '<strong>' + escapeHtml(marker.type) + '</strong>'
      + (marker.from || marker.to ? '　' + escapeHtml(marker.from || '?') + ' → '
        + escapeHtml(marker.to || '?') : '')
      + (marker.note ? '<br>' + escapeHtml(marker.note) : '')
      + '<button class="tiny del ghost" data-del="' + i + '">×</button></div>').join('');
    box.querySelectorAll('[data-del]').forEach((button) => {
      button.addEventListener('click', () => {
        state.validation.changeMarkers.splice(Number(button.dataset.del), 1);
        markDirty();
        renderMarkers();
      });
    });
  }

  function updateClock() {
    const total = state.duration > 0 ? state.duration : 0;
    el('clock').textContent = M.formatClock(state.time) + ' / ' + M.formatClock(total);
  }

  // ------------------------------------------------------------------ storage

  function storageKey() {
    return STORAGE_PREFIX + songKey();
  }

  function loadStoredValidation() {
    try {
      const text = localStorage.getItem(storageKey());
      return text ? JSON.parse(text) : null;
    } catch (err) {
      return null;
    }
  }

  function markDirty() {
    state.dirty = true;
    try {
      localStorage.setItem(storageKey(), JSON.stringify(state.validation));
    } catch (err) {
      saveState('本机缓存写不进去（可能是隐私模式），记得手动保存。');
    }
    saveState('已记入本机缓存；要留档点上面的保存。');
  }

  function saveState(text) {
    el('save-state').textContent = text || '';
  }

  function saveFile() {
    if (!state.validation) return;
    const base = (state.doc && (state.doc.song.audioFileName || state.doc.song.songId))
      ? state.doc.song.audioFileName.replace(/\.[^.]+$/, '') : 'validation';
    const blob = new Blob([JSON.stringify(state.validation, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = (base || 'song') + '.validation.json';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    saveState('已导出 ' + link.download);
  }

  // -------------------------------------------------------------------- clock

  function tick() {
    if (audio.src) {
      if (Math.abs(audio.currentTime - state.time) > 0.001) state.time = audio.currentTime;
      state.playing = !audio.paused;
      el('play').textContent = state.playing ? '⏸' : '▶';
    } else if (state.playing) {
      const now = performance.now();
      state.time += (now - virtualClock) / 1000;
      virtualClock = now;
      if (state.duration > 0 && state.time >= state.duration) {
        state.time = state.duration;
        state.playing = false;
        el('play').textContent = '▶';
      }
    }
    layoutPlayhead();
    updateClock();
    if (state.playing) followPlayhead(false);
    const now = performance.now();
    if (now - lastCoordsAt > 66) {
      lastCoordsAt = now;
      updateCoords();
    }
    requestAnimationFrame(tick);
  }

  // ------------------------------------------------------------------- wiring

  const AUDIO_NAME = P.AUDIO_NAME;
  const JSON_NAME = P.JSON_NAME;

  /** Load one audio file, wherever it came from. */
  function loadAudioFile(file) {
    if (audio.dataset.url) URL.revokeObjectURL(audio.dataset.url);
    const url = URL.createObjectURL(file);
    audio.dataset.url = url;
    audio.src = url;
    audio.load();
    audio.addEventListener('loadedmetadata', () => {
      updateDuration();
      rebuild();
      renderPanel();
      saveState('音频：' + file.name + '（' + M.formatClock(audio.duration) + '）');
    }, { once: true });
    rebuild();
  }

  /**
   * One URL parameter, or ''.

   * The page stays a file-first tool: everything below is optional, and without
   * the parameters it behaves exactly as it did before there were any.
   */
  function queryParam(name) {
    try {
      return new URLSearchParams(window.location.search).get(name) || '';
    } catch (err) {
      return '';
    }
  }

  /**
   * The audio to load by URL, if the page was given one.

   * Two ways in, because a full song URL is long and carries characters that a
   * copy and paste can quietly cut in half - which is exactly what happened once,
   * leaving the page asking a 404 for a file called ".../Cö":

   * - `audio=` the whole URL, for a one-off.
   * - `music=` a base URL, plus the path the document names in `song.audioPath`
   *   (or its file name). Short and pure ASCII, so nothing can be eaten:
   *   index.html?doc=kiechau&music=http://127.0.0.1:8125

   * With no `music=` at all, the path in the document is relative to the page -
   * which is how a song sitting in `static/audio/` beside the tool is loaded with
   * no parameters beyond `?doc=`. Media elements are not subject to CORS, so the
   * `music=` form works across ports. With no path and no parameter, nothing is
   * requested and the page waits for a dropped file.
   */
  function audioUrlFromParams() {
    const direct = queryParam('audio');
    if (direct) return direct;
    const base = queryParam('music');
    const song = state.doc ? state.doc.song : null;
    let name = song ? (song.audioPath || song.audioFileName) : '';
    // A round that has just been converted has the song's file name but no path;
    // songs shipped with the tool live in audio/, so that is where it is looked
    // for. A document that says where its file is always wins over the guess.
    if (!base && song && !song.audioPath && name && name.indexOf('/') < 0) {
      name = 'audio/' + name;
    }
    if (!name) return '';
    if (/^[a-z][a-z0-9+.-]*:/i.test(name) || name.startsWith('//')) return name;
    // Encode each segment, not the slashes: the path stays a path.
    const encoded = String(name).split('/').map(encodeURIComponent).join('/');
    return base ? base.replace(/\/+$/, '') + '/' + encoded : encoded;
  }

  function loadAudioFromUrl() {
    const src = audioUrlFromParams();
    if (!src) return;
    audio.addEventListener('loadedmetadata', () => {
      updateDuration();
      rebuild();
      renderPanel();
      saveState('音频：' + decodeURIComponent(src.split('/').pop())
        + '（' + M.formatClock(audio.duration) + '）');
    }, { once: true });
    audio.addEventListener('error', () => {
      flash('这个音频地址打不开：' + src);
    }, { once: true });
    audio.src = src;
    audio.load();
    rebuild();
  }

  /**
   * Open a document the folder already ships, by URL: index.html?doc=kiechau.

   * Drag and drop is the right way in when the data has just come off a phone.
   * These are the songs already worked through, and having to find the same file
   * in a dialog again every time is how a tool stops being opened.
   */
  async function loadDocFromUrl() {
    const name = queryParam('doc');
    if (!name) return;
    const file = /\.json$/i.test(name) ? name : name + '.mgmcs.json';
    try {
      // no-cache, because these files are edited while the tool is open: the whole
      // point of ?doc= is to look at the current reading, not last hour's.
      const response = await fetch('docs/' + encodeURIComponent(file), { cache: 'no-cache' });
      if (!response.ok) throw new Error('HTTP ' + response.status);
      setDocument(await response.json(), file);
      saveState('已载入 docs/' + file
        + (queryParam('audio') || queryParam('music')
          ? '，音频按网址加载' : '（音频还要自己拖进来）'));
    } catch (err) {
      flash('打不开 docs/' + file + '：' + err.message
        + '。这个参数要有 http 服务才管用（file:// 下没有 docs/）。');
    }
  }

  /**
   * Open whatever the URL asked for. The audio is started after the document,
   * because with `music=` the file name comes out of the document itself.
   */
  async function openFromUrl() {
    await loadDocFromUrl();
    loadAudioFromUrl();
  }

  async function loadJsonFromFile(file) {
    let raw;
    try {
      raw = JSON.parse(await file.text());
    } catch (err) {
      const box = el('warnings');
      box.classList.add('bad');
      box.innerHTML = '<strong>' + escapeHtml(file.name) + ' 不是 JSON：'
        + escapeHtml(String(err.message)) + '</strong>';
      box.hidden = false;
      return false;
    }
    setDocument(raw, file.name);
    return true;
  }

  /** Every file in a drop, including anything inside a dropped folder. */
  async function filesFromDrop(dataTransfer) {
    const items = dataTransfer.items;
    if (!items || !items.length) return Array.from(dataTransfer.files || []);
    const out = [];
    const walk = async (entry) => {
      if (!entry) return;
      if (entry.isFile) {
        const file = await new Promise((done) => entry.file(done, () => done(null)));
        if (file) out.push(file);
        return;
      }
      if (!entry.isDirectory) return;
      const reader = entry.createReader();
      for (;;) {
        const batch = await new Promise((done) => reader.readEntries(done, () => done([])));
        if (!batch.length) break;
        for (const child of batch) await walk(child);
      }
    };
    for (const item of Array.from(items)) {
      if (item.kind !== 'file') continue;
      await walk(item.webkitGetAsEntry ? item.webkitGetAsEntry() : null);
    }
    return out.length ? out : Array.from(dataTransfer.files || []);
  }

  async function openDropped(files) {
    const audios = files.filter((file) => AUDIO_NAME.test(file.name));
    const documents = [];
    for (const file of files.filter((f) => JSON_NAME.test(f.name))) {
      let raw;
      try {
        raw = JSON.parse(await file.text());
      } catch (err) {
        continue;
      }
      if (P.isResultFile(raw)) continue;   // a saved result is not an input
      documents.push({ file, raw });
    }
    if (!audios.length && !documents.length) {
      flash('拖进来的东西里没有音频，也没有 mgmcs.json。');
      return;
    }
    if (!audios.length) {
      setDocument(documents[0].raw, documents[0].file.name);
      return;
    }
    const choice = P.bestPair(audios.map((file) => file.name),
      documents.map((entry) => entry.raw));
    if (!choice) {
      loadAudioFile(audios[0]);
      flash('只找到音频（' + audios[0].name + '），没有配得上的 JSON。');
      return;
    }
    const audioFile = audios[choice.audioIndex];
    const document = documents[choice.documentIndex];
    loadAudioFile(audioFile);
    setDocument(document.raw, document.file.name);
    const extra = (audios.length > 1 || documents.length > 1)
      ? '（拖进来 ' + audios.length + ' 个音频 / ' + documents.length + ' 个 JSON，按名字选了这一对）'
      : '';
    saveState((choice.score >= 70 ? '已配对' : '已配对（名字对得不太准，自己核对一下）')
      + '：' + audioFile.name + ' ↔ ' + document.file.name + extra);
  }

  function bind() {
    el('json-file').addEventListener('change', async (ev) => {
      const file = ev.target.files && ev.target.files[0];
      if (!file) return;
      await loadJsonFromFile(file);
    });

    el('audio-file').addEventListener('change', (ev) => {
      const file = ev.target.files && ev.target.files[0];
      if (!file) return;
      loadAudioFile(file);
    });

    const stop = (ev) => { ev.preventDefault(); ev.stopPropagation(); };
    window.addEventListener('dragover', (ev) => {
      stop(ev);
      document.body.classList.add('dropping');
    });
    window.addEventListener('dragleave', (ev) => {
      if (!ev.relatedTarget) document.body.classList.remove('dropping');
    });
    window.addEventListener('drop', async (ev) => {
      stop(ev);
      document.body.classList.remove('dropping');
      try {
        await openDropped(await filesFromDrop(ev.dataTransfer));
      } catch (err) {
        flash('拖进来的东西读不了：' + err.message);
      }
    });

    el('play').addEventListener('click', togglePlay);
    el('restart').addEventListener('click', () => seek(0));
    el('zoom-fit').addEventListener('click', () => setZoom(pxPerSecondMin(), 0));
    el('zoom').addEventListener('input', (ev) => {
      setZoom(zoomSliderToPps(Number(ev.target.value)), null, null);
    });
    el('follow').addEventListener('change', (ev) => { state.follow = ev.target.checked; });
    el('play-on-seek').addEventListener('change', (ev) => { state.playOnSeek = ev.target.checked; });
    // 边界邻域默认关：它只是"事件算不算发生在小节切换附近"的观察窗，不是网格本身的一部分。
    el('show-near').addEventListener('change', (ev) => {
      document.body.classList.toggle('show-near', ev.target.checked);
    });
    el('diff-a').addEventListener('change', renderDiff);
    el('diff-b').addEventListener('change', renderDiff);
    el('diff-human').addEventListener('change', renderDiff);
    el('diff-next').addEventListener('click', diffNext);

    timeline.addEventListener('wheel', (ev) => {
      ev.preventDefault();
      // Shift turns zooming into moving along the song, the way it does in the
      // editors a researcher already has open.
      if (ev.shiftKey) {
        scrollTo(timeline.scrollLeft + ev.deltaY);
        return;
      }
      const factor = Math.pow(1.0016, -ev.deltaY);
      setZoom(state.pxPerSecond * factor, null, ev.clientX);
    }, { passive: false });

    timeline.addEventListener('scroll', () => {
      if (state.programmaticScroll > 0) { state.programmaticScroll -= 1; return; }
      if (state.follow) {
        state.follow = false;
        el('follow').checked = false;
        flash('你手动滚动了，已暂停跟随播放头。要恢复就勾上「跟随播放头」。');
      }
    });

    el('grids').addEventListener('click', (ev) => {
      const button = ev.target.closest('button[data-act]');
      if (!button) return;
      const gridId = button.dataset.grid;
      const act = button.dataset.act;
      if (act === 'toggle') {
        if (state.hiddenGrids.has(gridId)) state.hiddenGrids.delete(gridId);
        else state.hiddenGrids.add(gridId);
      } else if (act === 'solo') {
        state.soloGrid = state.soloGrid === gridId ? '' : gridId;
      } else if (act === 'judge') {
        state.validation.gridJudgements.push({
          gridId,
          judgement: button.dataset.judge,
          atTime: Number(state.time.toFixed(3)),
          startTime: null,
          endTime: null,
          note: '',
        });
        markDirty();
      }
      rebuild();
      renderGrids();
      renderPhase();
    });

    el('marker-add').addEventListener('click', () => {
      state.validation.changeMarkers.push({
        audioTime: Number(state.time.toFixed(3)),
        type: el('marker-type').value,
        from: el('marker-from').value.trim(),
        to: el('marker-to').value.trim(),
        note: el('marker-note').value.trim(),
      });
      el('marker-note').value = '';
      markDirty();
      renderMarkers();
    });

    el('save').addEventListener('click', saveFile);
    el('clear').addEventListener('click', () => {
      state.validation = M.newValidation(state.doc ? state.doc.song.songId : '');
      try { localStorage.removeItem(storageKey()); } catch (err) { /* ignore */ }
      renderPanel();
      saveState('已清空。');
    });

    document.addEventListener('keydown', (ev) => {
      if (ev.code === 'Space' && !/input|select|textarea/i.test(ev.target.tagName)) {
        ev.preventDefault();
        togglePlay();
      }
    });

    timeline.addEventListener('mouseleave', hideTip);
    window.addEventListener('resize', () => { layoutTimeline(); updateZoomRead(); });
  }

  // ------------------------------------------------------------------ tooltips

  function showBarTip(ev, grid, bar) {
    const index = gridIndex(grid.gridId);
    const beats = M.barBeats(grid, bar);
    const odd = Math.abs(beats - grid.beatsPerBar) > WHOLE_BAR_TOLERANCE;
    // Where the pointer is, in beats: the tooltip answers "which beat would I get"
    // before the click, which is the whole point of beat granularity.
    const barEl = ev.target.closest('.bar');
    let aim = '';
    if (barEl) {
      const box = barEl.getBoundingClientRect();
      const beatWidth = (60 / grid.bpm) * state.pxPerSecond;
      const inBar = Math.max(1, Math.round(beats));
      const beat = clamp(Math.round((ev.clientX - box.left) / beatWidth), 0, inBar - 1);
      aim = '<br><b>点这里 → beat ' + beat + '</b>　'
        + M.formatClock(M.timeOf(grid, bar, beat));
    }
    tooltip.innerHTML = '<b>' + escapeHtml(gridName(index)) + ' · ' + escapeHtml(grid.gridId)
      + '</b><br>bar ' + bar + '　' + escapeHtml(describeGrid(grid))
      + '<br>起点 ' + M.formatClock(M.timeOf(grid, bar, 0))
      + '<br>这一小节 ' + M.formatBeat(beats) + ' 拍'
      + (odd ? '　<b>不是整小节</b>' : '')
      + aim
      + '<br><span class="muted">点哪一拍就从哪一拍开始，粒度到拍</span>';
    placeTip(ev);
  }

  function showEventTip(ev, event) {
    const rows = grids().map((grid, index) => escapeHtml(gridName(index))
      + '：' + coordText(grid, event.time) + '<br>').join('');
    tooltip.innerHTML = '<b>' + escapeHtml(event.type) + '</b><br>'
      + escapeHtml(event.eventId) + '　' + escapeHtml(event.source || '')
      + '<br>' + M.formatClock(event.time) + '（' + event.time.toFixed(3) + ' s）<br>'
      + rows;
    placeTip(ev);
  }

  function placeTip(ev) {
    tooltip.hidden = false;
    const box = tooltip.getBoundingClientRect();
    tooltip.style.left = Math.min(ev.clientX + 12, window.innerWidth - box.width - 8) + 'px';
    tooltip.style.top = Math.max(8, ev.clientY - box.height - 10) + 'px';
  }

  function hideTip() {
    tooltip.hidden = true;
  }

  // Bars get their tooltip through one delegated listener per grid lane. */
  lanes.addEventListener('mousemove', (ev) => {
    const barEl = ev.target.closest('.bar');
    if (!barEl) return;
    const grid = gridById(barEl.dataset.grid);
    if (grid) showBarTip(ev, grid, Number(barEl.dataset.bar));
  });
  lanes.addEventListener('mousemove', (ev) => {
    if (!ev.target.closest('.bar') && !ev.target.closest('.evt')) hideTip();
  });

  bind();
  openFromUrl();
  renderPanel();
  requestAnimationFrame(tick);
}());
