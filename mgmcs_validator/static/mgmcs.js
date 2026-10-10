/**
 * Multi-grid coordinates, and the reading of the file that carries them.

 * This is the only place the arithmetic lives. The page draws with it and the
 * tests check it against the numbers written down in the spec, because a
 * validator whose own coordinate maths is a second implementation is a
 * validator that can quietly disagree with the thing it is validating.

 * Loaded as a plain script, not a module: a module needs a web server, and this
 * tool is meant to be handed to somebody who should only have to open a file.
 */
globalThis.MGMCS = (function () {
  'use strict';

  const SCHEMA_VERSION = '0.1';
  /**
   * How far apart two tempos may be and still be "the same tempo".

   * Tapping the same song twice does not give the same number twice - measured on
   * one song, four separate attempts came out 106.2, 106.8, 107.0 and 105.8. A
   * rule that demanded equality would refuse to compare the only two grids the
   * researcher most wants compared, which is the same song tapped twice.

   * So a percent, and the difference is reported rather than hidden: two grids a
   * percent apart are the same tempo at the origin and drift a third of a second
   * per minute apart, which is exactly the kind of thing worth knowing.
   */
  const TEMPO_TOLERANCE = 0.01;

  // ---------------------------------------------------------------- coordinates

  /** How many beats have passed since the grid's origin. May be negative. */
  function beatsSince(originTime, bpm, t) {
    return (t - originTime) * bpm / 60;
  }

  /** The bar length a grid would have if every bar were the same. */
  function nominalBarSeconds(grid) {
    return grid.beatsPerBar * 60 / grid.bpm;
  }

  /**
   * When a bar starts.

   * A grid with no list is a period: bar N is N bars from the origin, and that is
   * all there is to it. A grid with explicit bar lines is a *reading* whose bars
   * are not all the same length - this song's intro is cut short by half a bar
   * where the arrangement collapses - and then the only honest answer is the list.
   * Outside the recorded lines the nominal bar is extrapolated, so the timeline
   * can draw a partial bar at either end without the reading itself changing.
   */
  function barStart(grid, bar) {
    if (!grid.irregular) return timeOfBeat(grid, bar * grid.beatsPerBar);
    const times = grid.barTimes;
    if (bar < 0) return times[0] + bar * nominalBarSeconds(grid);
    if (bar >= times.length) {
      return times[times.length - 1] + (bar - (times.length - 1)) * nominalBarSeconds(grid);
    }
    return times[bar];
  }

  /** How long a bar actually is. Not constant when the grid has a bar line list. */
  function barSeconds(grid, bar) {
    return barStart(grid, bar + 1) - barStart(grid, bar);
  }

  /**
   * How many beats a bar holds.

   * Left as a fraction on purpose: a bar of three and a half beats is exactly the
   * thing this tool exists to see, and rounding it to four here would hide it in
   * the panel that is supposed to show it.
   */
  function barBeats(grid, bar) {
    return grid.irregular ? barSeconds(grid, bar) * grid.bpm / 60 : grid.beatsPerBar;
  }

  /** The index of the last bar line at or before t. May be negative. */
  function barIndexAt(grid, t) {
    const times = grid.barTimes;
    let lo = 0;
    let hi = times.length - 1;
    let found = -1;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (times[mid] <= t + 1e-9) { found = mid; lo = mid + 1; } else { hi = mid - 1; }
    }
    if (found >= 0) {
      // Past the last recorded line the nominal bar takes over again, so the bar
      // number keeps counting instead of one bar's beat running away. A song's
      // tail is ordinary bars; only the seams are listed.
      const last = times.length - 1;
      if (found === last && t > times[last]) {
        return last + Math.floor((t - times[last]) / nominalBarSeconds(grid));
      }
      return found;
    }
    return Math.floor((t - times[0]) / nominalBarSeconds(grid));
  }

  /**
   * Where a moment sits in a grid.

   * The bar is a whole number and the beat is a fraction inside it, so a moment
   * before the origin has a negative bar and a beat that still counts forward.
   * Nothing here decides which grid is right, and nothing snaps to a beat: the
   * spec is explicit that the time is the truth and the reading of it is what is
   * under study.
   */
  function coordinateAt(grid, t) {
    if (grid.irregular) {
      const bar = barIndexAt(grid, t);
      const beats = (t - barStart(grid, bar)) * grid.bpm / 60;
      return { gridId: grid.gridId, beats, bar, beat: beats };
    }
    const beats = beatsSince(grid.originTime, grid.bpm, t);
    const perBar = grid.beatsPerBar;
    const bar = Math.floor(beats / perBar);
    return { gridId: grid.gridId, beats, bar, beat: beats - bar * perBar };
  }

  /** The time of a beat count measured from the origin. */
  function timeOfBeat(grid, beats) {
    return grid.originTime + beats * 60 / grid.bpm;
  }

  /** The time of bar N beat B - what clicking a bar or a beat looks up. */
  function timeOf(grid, bar, beat) {
    if (grid.irregular) return barStart(grid, bar) + (beat || 0) * 60 / grid.bpm;
    return timeOfBeat(grid, bar * grid.beatsPerBar + (beat || 0));
  }

  /**
   * The bars of this grid that fall inside a time window.

   * Used to draw only what is on screen: a four-minute song is a hundred-odd
   * bars per grid, and at a close zoom almost all of them are off the left edge.
   */
  function barRange(grid, fromTime, toTime) {
    if (grid.irregular) {
      // One bar of margin at each end: a bar that starts before the window still
      // has to be drawn if any of it is on screen.
      return {
        first: Math.min(barIndexAt(grid, fromTime), 0) - 1,
        last: Math.max(barIndexAt(grid, toTime), grid.barTimes.length - 1) + 1,
      };
    }
    const first = coordinateAt(grid, fromTime).bar;
    const last = coordinateAt(grid, toTime).bar;
    return { first, last };
  }

  /**
   * How two grids stand to each other, in bars and beats.

   * Only defined when the two count the same way: the same beats to the bar, and
   * a tempo within a tolerance (see TEMPO_TOLERANCE). Two grids that count
   * differently share nothing but the audio, and the spec forbids converting one
   * into the other, so this reports that it cannot compare rather than inventing
   * a number.

   * Positive means the second origin is later than the first. This is a
   * statement about two grids, never about the music - "half a bar apart" is a
   * candidate to go and listen to, not a finding.
   */
  function phaseOffset(a, b) {
    if (a.irregular || b.irregular) {
      return {
        comparable: false,
        reason: '其中一条是小节线列表（小节不等长），不能用起点比相位',
      };
    }
    const deltaBpm = b.bpm - a.bpm;
    const sameTempo = Math.abs(deltaBpm) <= TEMPO_TOLERANCE * Math.max(1, Math.abs(a.bpm));
    if (!sameTempo || a.beatsPerBar !== b.beatsPerBar) {
      return {
        comparable: false,
        reason: sameTempo ? '拍号不同' : '速度不同',
      };
    }
    const deltaTime = b.originTime - a.originTime;
    const deltaBeat = deltaTime * a.bpm / 60;
    return {
      comparable: true,
      deltaBpm,
      // Seconds of drift per minute of music, from the tempo difference alone.
      driftSecondsPerMinute: (deltaBpm / a.bpm) * 60,
      deltaTime,
      deltaBeat,
      deltaBar: deltaBeat / a.beatsPerBar,
    };
  }

  // ------------------------------------------------------------------ printing

  /** 83.427 -> "01:23.427". Milliseconds, because taps are milliseconds. */
  function formatClock(seconds) {
    if (!isFinite(seconds)) return '--:--.---';
    const sign = seconds < 0 ? '-' : '';
    let s = Math.abs(seconds);
    let minutes = Math.floor(s / 60);
    let whole = Math.floor(s - minutes * 60);
    let ms = Math.round((s - minutes * 60 - whole) * 1000);
    if (ms >= 1000) { ms -= 1000; whole += 1; }
    if (whole >= 60) { whole -= 60; minutes += 1; }
    return sign
      + String(minutes).padStart(2, '0') + ':'
      + String(whole).padStart(2, '0') + '.'
      + String(ms).padStart(3, '0');
  }

  /** A beat is a fraction; two places is what the spec prints. */
  function formatBeat(beat) {
    return (isFinite(beat) ? beat : 0).toFixed(2);
  }

  // ------------------------------------------------------------------- reading

  function num(value, fallback) {
    return typeof value === 'number' && isFinite(value) ? value : fallback;
  }

  function str(value) {
    return typeof value === 'string' ? value : '';
  }

  /**
   * Write what the file says, and say what about it does not add up.

   * Permissive on purpose. The tool exists so a researcher can look at what the
   * phone produced; refusing a file because one event is malformed would hide
   * the other ninety-nine. So the problems are collected and shown, and whatever
   * can be drawn is drawn. The one thing never done is repairing a number: if a
   * calibration has no tempo it is reported and skipped, not defaulted to 120.
   */
  function parseSongDocument(raw) {
    const problems = [];
    const doc = {
      schemaVersion: SCHEMA_VERSION,
      song: {
        songId: '', title: '', audioFileName: '', audioPath: '',
        durationSeconds: NaN, hash: '',
      },
      grids: [],
      events: [],
      problems,
    };
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
      problems.push('这个文件不是一个 JSON 对象。');
      return doc;
    }
    doc.schemaVersion = str(raw.schemaVersion) || SCHEMA_VERSION;

    const song = (raw.song && typeof raw.song === 'object') ? raw.song : {};
    doc.song = {
      // The spec's input shape puts songId and duration at the top level and the
      // phone's puts them under `song`; both are read, so a document written to
      // the spec can be opened here without being rewritten first.
      songId: str(song.songId) || str(raw.songId),
      title: str(song.title) || str(raw.title),
      audioFileName: str(song.audioFileName),
      // audioPath is the same file as a path under a served folder, which is what
      // lets a page load the song itself instead of asking for it every time.
      audioPath: str(song.audioPath),
      durationSeconds: num(song.durationSeconds, num(raw.duration, NaN)),
      hash: str(song.hash),
    };
    if (!doc.song.title && !doc.song.audioFileName && !doc.song.songId) {
      problems.push('song 里没有标题、文件名或 id，这份数据以后不好认领。');
    }

    const calibrations = Array.isArray(raw.calibrations) ? raw.calibrations
      : Array.isArray(raw.grids) ? raw.grids : null;
    if (!calibrations) {
      problems.push('没有 calibrations 数组，这份文件里没有网格。');
    } else if (calibrations.length === 0) {
      problems.push('calibrations 是空的，没有网格可画。');
    }

    const used = new Set();
    (calibrations || []).forEach((entry, i) => {
      const cal = (entry && typeof entry === 'object') ? entry : {};
      let id = str(cal.calibrationId) || str(cal.gridId) || ('cal_' + (i + 1));
      if (used.has(id)) {
        problems.push('两条校准用了同一个 id（' + id + '），第二条改名为 ' + id + '#' + (i + 1) + '。');
        id = id + '#' + (i + 1);
      }
      used.add(id);

      const barTimes = readBarLines(cal);
      const originTime = barTimes ? barTimes[0] : num(cal.audioTime, num(cal.originTime, NaN));
      const bpm = num(cal.bpm, NaN);
      const beatsPerBar = Math.round(num(cal.beatsPerBar, num(cal.meter, NaN)));
      const faults = [];
      if (!barTimes && !isFinite(originTime)) faults.push('缺少 audioTime（校准点在哪一秒）');
      if (!(bpm > 0)) faults.push('bpm 缺失或不是正数');
      if (!(beatsPerBar >= 1)) faults.push('beatsPerBar 缺失或小于 1');
      if (faults.length) {
        problems.push(id + '：' + faults.join('；') + '。这条校准被跳过。');
        return;
      }
      doc.grids.push({
        gridId: id,
        originTime,
        bpm,
        beatsPerBar,
        beatUnit: Math.round(num(cal.beatUnit, 4)),
        source: str(cal.source) || 'human',
        barTimes,
        irregular: !!barTimes,
        raw: cal,
      });
    });

    const events = Array.isArray(raw.events) ? raw.events : [];
    if (raw.events && !Array.isArray(raw.events)) problems.push('events 不是数组，已忽略。');
    events.forEach((entry, i) => {
      const ev = (entry && typeof entry === 'object') ? entry : {};
      const id = str(ev.eventId) || ('evt_' + (i + 1));
      const type = str(ev.type) || 'marker';
      const audioTime = num(ev.audioTime, NaN);
      const startTime = num(ev.startTime, NaN);
      const endTime = num(ev.endTime, NaN);
      const isSpan = isFinite(startTime) && isFinite(endTime);
      if (isSpan) {
        if (!(endTime > startTime)) {
          problems.push(id + '：区间事件的 endTime 不在 startTime 之后，已跳过。');
          return;
        }
        doc.events.push({
          eventId: id, type, kind: 'span', time: startTime, endTime,
          source: str(ev.source), payload: ev.payload, raw: ev,
        });
        return;
      }
      if (!isFinite(audioTime)) {
        problems.push(id + '：既没有 audioTime 也没有成对的 startTime/endTime，已跳过。');
        return;
      }
      doc.events.push({
        eventId: id, type, kind: 'point', time: audioTime, endTime: NaN,
        source: str(ev.source), payload: ev.payload, raw: ev,
      });
    });

    return doc;
  }

  /**
   * The bar lines a document writes out by hand, when it has them.

   * A grid is normally a period plus a meter, and every bar is the same length.
   * A song can have a bar that is not - where the phrase is cut short - and that
   * cannot be written as a period, so it is written as a list. The list is a
   * reading, not a measurement: what the tool does with it is draw it and ask.
   */
  function readBarLines(cal) {
    if (!Array.isArray(cal.barLines)) return null;
    const times = [];
    for (const entry of cal.barLines) {
      const t = (entry && typeof entry === 'object') ? num(entry.time, NaN) : num(entry, NaN);
      if (isFinite(t)) times.push(t);
    }
    times.sort((a, b) => a - b);
    return times.length >= 2 ? times : null;
  }

  /**
   * Are these two grids about half a bar apart?

   * Half-Bar Detection Methodology v0.1, implemented as written: same meter, BPM
   * within a tolerance, then the distance between the two origins folded into one
   * bar, and how far that fold sits from half a bar.

   * Two things are added, both labelled rather than folded into the verdict:

   * - `phaseUncertaintyBeat`: how far the folded phase moves if the tempo is
   *   anywhere between the two grids' own numbers. Over a long gap this can be
   *   larger than the whole half-bar window, and then the answer says more about
   *   the tempo estimate than about the music.
   * - `reliable`: false when that is the case, so a candidate is never presented
   *   as stronger than its inputs.
   */
  function halfBar(a, b, options) {
    const settings = options || {};
    const bpmTolerance = settings.bpmTolerance === undefined ? 0.02 : settings.bpmTolerance;
    const phaseTolerance = settings.phaseTolerance === undefined ? 0.15 : settings.phaseTolerance;
    const out = {
      gridA: a.gridId,
      gridB: b.gridId,
      beatsPerBar: a.beatsPerBar,
      result: 'not_applicable',
      reason: '',
    };
    if (a.beatsPerBar !== b.beatsPerBar) {
      out.reason = '拍号不同';
      return out;
    }
    if (a.irregular || b.irregular) {
      out.reason = '其中一条是小节线列表：半小节已经写在它的接缝里，不用两条网格去猜';
      return out;
    }
    const meanBpm = (a.bpm + b.bpm) / 2;
    const ratio = Math.abs(a.bpm - b.bpm) / meanBpm;
    out.bpmDifferenceRatio = ratio;
    if (!(ratio < bpmTolerance)) {
      out.reason = `速度差 ${(ratio * 100).toFixed(2)}%，超过允许的 `
        + `${(bpmTolerance * 100).toFixed(1)}%`;
      return out;
    }
    out.result = 'no_candidate';

    const bars = a.beatsPerBar;
    const deltaTime = b.originTime - a.originTime;
    const deltaBeat = deltaTime * meanBpm / 60;
    const phaseBeat = ((deltaBeat % bars) + bars) % bars;
    const halfBarBeat = bars / 2;
    const raw = Math.abs(phaseBeat - halfBarBeat) % bars;
    const halfBarError = Math.min(raw, bars - raw);

    // How far the fold moves across the two tempos the caller actually gave:
    // that is the honest error bar on the phase, and it is free of charge.
    const phaseUncertaintyBeat = Math.abs(deltaTime) * Math.abs(a.bpm - b.bpm) / 60 / 2;

    out.deltaTime = deltaTime;
    out.deltaBeat = deltaBeat;
    out.phaseBeat = phaseBeat;
    out.barPhaseOffset = phaseBeat / bars;
    out.halfBarErrorBeat = halfBarError;
    out.phaseUncertaintyBeat = phaseUncertaintyBeat;
    out.reliable = phaseUncertaintyBeat <= phaseTolerance;

    if (halfBarError <= phaseTolerance) {
      out.result = 'half_bar_phase_candidate';
      out.reason = `相差 ${phaseBeat.toFixed(2)} 拍，离半小节 ${halfBarError.toFixed(2)} 拍`;
      // Stronger only with evidence about when each grid was usable: A valid
      // before the change point, B valid after it.
      const changePoint = Math.max(a.originTime, b.originTime);
      const before = (settings.validA || []).some((t) => t < changePoint);
      const after = (settings.validB || []).some((t) => t > changePoint);
      if (before && after) {
        out.result = 'half_bar_transition_candidate';
        out.changePoint = changePoint;
        out.reason += `；且 A 在 ${changePoint.toFixed(1)}s 之前有效、B 在其后有效`;
      }
      return out;
    }
    out.reason = `相差 ${phaseBeat.toFixed(2)} 拍，离半小节还差 ${halfBarError.toFixed(2)} 拍`;
    return out;
  }

  /**
   * The four calls everything else is allowed to make, as one object.

   * `timeToCoordinate(t, gridId)`, `coordinateToTime(bar, beat, gridId)`,
   * `projectTime(t)`, `generateAnchors(gridId, from, to)`.

   * This is a second *name* for the arithmetic above, not a second
   * implementation of it: a tool whose coordinate maths exists twice is a tool
   * that can quietly disagree with itself, and that is the one failure this whole
   * thing exists to catch. The Python side (tools/coordinate_map.py) is checked
   * against the same fixtures for the same reason.

   * Nothing here picks a grid, merges two grids, or edits an origin. Projecting
   * one moment onto every grid is exactly what it does - several readings of one
   * instant is the point, not a problem to be resolved.
   */
  function coordinateMap(doc) {
    const byId = new Map();
    for (const grid of (doc && doc.grids) || []) byId.set(grid.gridId, grid);
    const duration = doc && doc.song ? doc.song.durationSeconds : NaN;

    const fault = (message) => ({ error: message });

    function gridOrFault(gridId) {
      const grid = byId.get(String(gridId));
      if (!grid) return fault('没有这条网格：' + gridId);
      if (!(grid.bpm > 0)) return fault(gridId + '：bpm 不是正数');
      if (!(grid.beatsPerBar > 0)) return fault(gridId + '：beatsPerBar 不是正数');
      return grid;
    }

    /** Outside the audio is not an error: a grid has to extend past both ends. */
    function withRange(out, audioTime) {
      if (isFinite(duration) && (audioTime < 0 || audioTime > duration)) {
        out.outOfAudioRange = true;
      }
      return out;
    }

    function timeToCoordinate(audioTime, gridId) {
      if (typeof audioTime !== 'number' || !isFinite(audioTime)) {
        return fault('audioTime 不是数字');
      }
      const grid = gridOrFault(gridId);
      if (grid.error) return grid;
      const at = coordinateAt(grid, audioTime);
      return withRange({
        gridId: grid.gridId, audioTime, bar: at.bar, beat: at.beat,
      }, audioTime);
    }

    function coordinateToTime(bar, beat, gridId) {
      if (typeof bar !== 'number' || !isFinite(bar)
          || typeof beat !== 'number' || !isFinite(beat)) {
        return fault('bar / beat 不是数字');
      }
      const grid = gridOrFault(gridId);
      if (grid.error) return grid;
      const audioTime = timeOf(grid, bar, beat);
      return withRange({ gridId: grid.gridId, bar, beat, audioTime }, audioTime);
    }

    function projectTime(audioTime) {
      if (typeof audioTime !== 'number' || !isFinite(audioTime)) {
        return fault('audioTime 不是数字');
      }
      const coordinates = [];
      for (const grid of byId.values()) {
        const at = coordinateAt(grid, audioTime);
        coordinates.push({ gridId: grid.gridId, bar: at.bar, beat: at.beat });
      }
      return withRange({ audioTime, coordinates }, audioTime);
    }

    /**
     * The beat lines a player draws for one window - beats, not milliseconds.

     * Built bar by bar rather than from one period, so a grid whose bars are not
     * all the same length hands out the beats it really has: a two-beat bar gives
     * two anchors, not four.
     */
    function generateAnchors(gridId, startTime, endTime) {
      const grid = gridOrFault(gridId);
      if (grid.error) return grid;
      if (![startTime, endTime].every((x) => typeof x === 'number' && isFinite(x))
          || endTime < startTime) {
        return fault('时间窗口不合法');
      }
      const range = barRange(grid, startTime, endTime);
      const anchors = [];
      for (let bar = range.first; bar <= range.last; bar += 1) {
        const beats = Math.max(1, Math.round(barBeats(grid, bar)));
        for (let beat = 0; beat < beats; beat += 1) {
          const at = timeOf(grid, bar, beat);
          if (at < startTime - 1e-9 || at > endTime + 1e-9) continue;
          anchors.push({ audioTime: at, bar, beat, isDownbeat: beat === 0 });
        }
      }
      return anchors;
    }

    return { timeToCoordinate, coordinateToTime, projectTime, generateAnchors };
  }

  /** An empty result file. Section 13: the input is never written back to. */
  function newValidation(songId) {
    return {
      schemaVersion: SCHEMA_VERSION,
      songId: songId || '',
      gridJudgements: [],
      changeMarkers: [],
      eventJudgements: [],
      notes: [],
    };
  }

  return {
    SCHEMA_VERSION,
    beatsSince,
    coordinateAt,
    timeOfBeat,
    timeOf,
    nominalBarSeconds,
    barStart,
    barSeconds,
    barBeats,
    barIndexAt,
    barRange,
    phaseOffset,
    halfBar,
    formatClock,
    formatBeat,
    parseSongDocument,
    newValidation,
    coordinateMap,
  };
}());
