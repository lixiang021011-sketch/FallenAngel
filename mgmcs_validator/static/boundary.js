/**
 * Boundary Association：事件与小节边界之间的**派生关系层**。

 * 它回答一个问题：**这个事件与该 grid 最近的那根小节边界是什么关系？**
 * 它不回答"真正的小节线究竟在哪"——那仍然是 grid 自己的事，两者必须分开
 * （规格 §13）。所以这里算出来的一切都不写回文档：不改 event 的 time / bar / beat，
 * 不改 grid 的起点和速度（规格 §8、§12 Case 6）。

 * 三条来自研究者（2026-10-10）的硬约束：

 * 1. **每个事件 × 每条 grid 只关联最近的一根边界**。不做"事件 × 所有边界"的枚举：
 *    那样在变拍或异常 grid 上会长出奇怪的重复，而真正要问的是"最近那根是什么关系"。
 * 2. **一拍级的相位错位是"局部候选"，不是给整对 grid 打标签**。要有位置
 *    （哪根 bar / 哪一刻）、要连续若干根 boundary 都支持同一个离散偏移，才算候选；
 *    局部速度漂移或一根不规则小节碰巧落在整数拍附近，不该被当成相位错位。
 * 3. **±0.5 beat 是 hard cap**，不是默认值。传 0.8 也夹回 0.5。这一拍总宽度是这套
 *    机制的语义边界（"是否发生在小节切换附近"），不能让配置把它悄悄放大。

 * 载入方式是普通 <script>（这个工具要能从 file:// 直接开），所以不 import 任何东西，
 * 只在 globalThis 上挂 MGMCS.boundary，和 mgmcs.js 一样。
 */
(function () {
  'use strict';

  const core = () => globalThis.MGMCS;

  /** 窗口的语义上限：±0.5 beat（一拍总宽）。不是默认值，是天花板。 */
  const HARD_CAP_BEATS = 0.5;

  const DEFAULTS = {
    near: 0.10,             // |Δ| <= 0.10 → near_boundary
    associated: 0.30,       // |Δ| <= 0.30 → boundary_associated
    peripheral: 0.50,       // |Δ| <= 0.50 → peripheral，再往外 none
    windowBeats: HARD_CAP_BEATS,
    offsetTolerance: 0.15,  // 判"接近某个整数拍"的容差（拍）
    // 候选段内偏移的允许波动：超过它就不是"停在那里"，是"滑过那里"。
    // 0.05 是量出来的：两条差 1% 的网格每根滑 ~0.033 拍，收紧到 0.05 以下，
    // 那三首真实曲子的"漂移擦过"假候选就全没了（实测 taishou/lulu/kiechau：0 条局部）。
    stabilityBeats: 0.05,
    minRunBars: 2,          // 局部相位候选至少要几根连续 boundary 支持
    discreteOffsets: [1, 2, 3],   // 只认这些离散拍偏移（半小节的 0.5 不走这条路）
    maxBars: 4000,          // 扫候选时的上限，防御异常文档
  };

  /**
   * 把调用方给的阈值收成一份可用的配置。

   * 窗口夹在 [0, HARD_CAP_BEATS]；档位也必须落在窗口内（否则会出现"算出了窗口外的档位"）。
   * 夹过的痕迹留在 `windowClamped` 上，让界面能说"你传的 0.8 被夹成 0.5 了"，
   * 而不是悄悄改掉（规格 §2.2 与研究者对 hard cap 的要求）。
   */
  function options(raw) {
    const given = (raw && typeof raw === 'object') ? raw : {};
    const merged = Object.assign({}, DEFAULTS, given);
    const asked = Number(merged.windowBeats);
    const windowBeats = Math.min(HARD_CAP_BEATS, Math.max(0, isFinite(asked) ? asked : HARD_CAP_BEATS));
    const clamped = !(isFinite(asked) && asked === windowBeats);

    // 每个档位都夹在 [上一档, 窗口] 之间：不能超过窗口（超了就有"窗口外的关系"），
    // 也不能比前一档小（否则三档的边界会交叉，分类会互相盖住）。
    const order = ['near', 'associated', 'peripheral'];
    const values = {};
    let floor = 0;
    for (const key of order) {
      const asked = Number(merged[key]);
      const value = Math.min(windowBeats, Math.max(floor, isFinite(asked) ? asked : DEFAULTS[key]));
      values[key] = value;
      floor = value;
    }
    return {
      near: values.near,
      associated: values.associated,
      peripheral: values.peripheral,
      windowBeats,
      windowClamped: clamped,
      offsetTolerance: Math.max(0, Number(merged.offsetTolerance)),
      stabilityBeats: Math.max(0, Number(merged.stabilityBeats)),
      minRunBars: Math.max(1, Math.round(Number(merged.minRunBars) || DEFAULTS.minRunBars)),
      discreteOffsets: Array.isArray(merged.discreteOffsets)
        ? merged.discreteOffsets.map(Number).filter(isFinite) : DEFAULTS.discreteOffsets.slice(),
      maxBars: Math.max(1, Math.round(Number(merged.maxBars) || DEFAULTS.maxBars)),
    };
  }

  /** |relativeBeat| → 档位。四档的名字是规格 §4 定下来的。 */
  function classify(relativeBeat, raw) {
    const o = options(raw);
    const distance = Math.abs(Number(relativeBeat));
    if (!isFinite(distance)) return 'none';
    if (distance <= o.near) return 'near_boundary';
    if (distance <= o.associated) return 'boundary_associated';
    if (distance <= o.peripheral) return 'peripheral';
    return 'none';
  }

  /** 某一小节里"一拍"有多长。规则网格是 60/bpm；小节线列表的网格按它自己那一节折。 */
  function beatSecondsOf(grid, bar) {
    if (!grid.irregular) return 60 / grid.bpm;
    const seconds = core().barSeconds(grid, bar);
    const beats = core().barBeats(grid, bar);
    if (isFinite(seconds) && isFinite(beats) && beats > 0) return seconds / beats;
    return 60 / grid.bpm;
  }

  /**
   * 离 `time` 最近的那根小节边界。

   * 事件落在 bar 里，那么最近的边界只可能是"这一节的开头"或"下一节的开头"——
   * 所以只比这两个，不做全局枚举（研究者约束一）。
   */
  function nearestBoundary(grid, time) {
    // 用 coordinateAt 而不是 barIndexAt：后者要求 grid.barTimes 是数组（那是"小节线列表"
    // 专用的一条路），规则网格这里是 null。coordinateAt 两种网格都接。
    const bar = core().coordinateAt(grid, time).bar;
    const here = core().barStart(grid, bar);
    const next = core().barStart(grid, bar + 1);
    if (Math.abs(time - next) < Math.abs(time - here)) {
      return { bar: bar + 1, boundaryTime: next, beatSeconds: beatSecondsOf(grid, bar + 1) };
    }
    return { bar, boundaryTime: here, beatSeconds: beatSecondsOf(grid, bar) };
  }

  /**
   * 一个事件相对一条 grid 的关系，或者 null（不在窗口内）。

   * `eventTime` 用点事件的 audioTime；区间事件用它的起点——规格说的是"事件发生的时刻"，
   * 一条长条不是"发生在"某一刻，起点才是它进入音乐的那一刻。
   */
  function relationFor(event, grid, raw) {
    const o = options(raw);
    const eventTime = Number(event.time);
    if (!isFinite(eventTime) || !grid || !(grid.bpm > 0)) return null;
    const near = nearestBoundary(grid, eventTime);
    const relativeBeat = (eventTime - near.boundaryTime) / near.beatSeconds;
    if (!isFinite(relativeBeat) || Math.abs(relativeBeat) > o.windowBeats) return null;
    const associationClass = classify(relativeBeat, o);
    if (associationClass === 'none') return null;
    return {
      boundaryId: grid.gridId + '_bar' + near.bar,
      eventId: event.eventId,
      gridId: grid.gridId,
      barIndex: near.bar,
      relativeBeat,
      associationClass,
      eventTime,
      boundaryTime: near.boundaryTime,
      beatSeconds: near.beatSeconds,
      // 一条关系只依赖一条网格：资格就取它的来源档（"这条结论能不能用"）。
      analysisEligibility: gridHealth(grid),
    };
  }

  /**
   * 全部关系。每个事件对每条 grid 最多一条——这就是这套 API 的语义。

   * 不排序、不合并：调用方要的是"谁跟谁有关"，展示顺序是界面的事。
   */
  function boundaryAssociations(document, raw) {
    const o = options(raw);
    const out = [];
    if (!document || !Array.isArray(document.grids) || !Array.isArray(document.events)) return out;
    for (const event of document.events) {
      for (const grid of document.grids) {
        const relation = relationFor(event, grid, o);
        if (relation) out.push(relation);
      }
    }
    return out;
  }

  /** 某一节（gridId + barIndex）上关联到的事件，按 |relativeBeat| 从近到远（规格 §7.4）。 */
  function associatedEvents(document, gridId, barIndex, raw) {
    const wanted = String(gridId);
    const bar = Number(barIndex);
    return boundaryAssociations(document, raw)
      .filter(relation => relation.gridId === wanted && relation.barIndex === bar)
      .sort((left, right) => Math.abs(left.relativeBeat) - Math.abs(right.relativeBeat));
  }

  /**
   * 局部相位偏移候选（规格 §6、研究者约束二）。

   * 做法：拿 A 的每一根小节线，量它离 B 最近的那根线有多远（拍）。如果连续
   * `minRunBars` 根以上都落在同一个离散偏移（1/2/3 拍）附近，才算一个候选，
   * 并把位置记下来（哪根 bar、哪一刻）。单根碰巧差一拍不算——局部速度漂移、
   * 一根不规则小节，都能碰巧落到整数拍附近。

   * 只提示，不修正：这里不移动任何网格，也不说哪一条对（规格 §11）。
   */
  function phaseOffsetCandidates(document, raw) {
    const o = options(raw);
    const out = [];
    if (!document || !Array.isArray(document.grids)) return out;
    const grids = document.grids.filter(grid => grid && grid.bpm > 0);
    for (let i = 0; i < grids.length; i += 1) {
      for (let j = i + 1; j < grids.length; j += 1) {
        const a = grids[i];
        const b = grids[j];
        const verdict = eligibilityReport([a, b]);
        // 复用现成的相位比较，但**不当闸门**：它判的是"两条起点相位能不能比"（要求速度和拍号
        // 几乎一致，1% 的速度差就会说"速度不同"）。局部边界错位跟这个不是一回事——正因为它
        // 们速度略有差，才会在某一处擦过一拍。所以这里把它的结论当**上下文**带在候选上，
        // 让面板能同时说"全局相位不可比：速度不同"和"但这一处局部连续差一拍"。
        const phase = core().phaseOffset(a, b);
        collectRuns(out, a, b, o, phase, scanBars(document, a, o), verdict);
      }
    }
    return out;
  }

  /**
   * 扫多少根 bar 才停。

   * 有时长就扫到曲子结束（再留一根），免得报出"连续 4000 根"这种扫到曲子外面的数字；
   * 没有时长才退回 maxBars 那个防御上限。
   */
  function scanBars(document, grid, o) {
    let duration = document.song ? Number(document.song.durationSeconds) : NaN;
    if (!isFinite(duration) || duration <= 0) {
      // 文档没写时长就退到"最后一个事件之后一会儿"——总比扫到曲子外面强。
      const last = (document.events || []).reduce(
        (latest, event) => Math.max(latest, Number(event.endTime) || 0, Number(event.time) || 0), 0);
      duration = last > 0 ? last + 10 : NaN;
    }
    const nominal = core().nominalBarSeconds(grid);
    if (isFinite(duration) && duration > 0 && isFinite(nominal) && nominal > 0) {
      return Math.min(o.maxBars, Math.ceil(duration / nominal) + 1);
    }
    return o.maxBars;
  }

  /** 逐根量偏移，把连续的同值跑成候选。 */
  function collectRuns(out, a, b, o, phase, scanBars, verdict) {
    let runStart = null;
    let runOffset = null;
    let runLength = 0;
    let runMin = 0;
    let runMax = 0;
    const flush = () => {
      // 只有"停在那里"才算：段内偏移的波动要在 stabilityBeats 以内。
      // 少了这一条，两条速度略有差的网格会把偏移从 0 一路滑过 1、2、3 拍，
      // 每滑过一次就冒出一段假候选——一首歌能刷出几百条，把面板淹掉。
      const steady = runStart !== null && (runMax - runMin) <= o.stabilityBeats;
      if (steady && runLength >= o.minRunBars) {
        out.push({
          gridA: a.gridId,
          gridB: b.gridId,
          kind: 'local_phase_offset_candidate',
          offsetBeats: runOffset,
          barIndex: runStart,
          aroundTime: core().barStart(a, runStart),
          bars: runLength,
          // 跑满整个扫描范围 = 整首都差这么多拍。那不是"局部错位"，是这两条网格之间
          // 的全局相位关系（相位差面板已经在说这件事）。分开写，读的人才知道该去哪看。
          coversWhole: runStart === 0 && runLength >= scanBars,
          phase: { comparable: !!phase.comparable, reason: phase.reason || '' },
          sourceHealth: verdict.sourceHealth,
          analysisEligibility: verdict.analysisEligibility,
        });
      }
      runStart = null;
      runOffset = null;
      runLength = 0;
      runMin = 0;
      runMax = 0;
    };

    for (let bar = 0; bar < scanBars; bar += 1) {
      const time = core().barStart(a, bar);
      if (!isFinite(time)) break;
      const near = nearestBoundary(b, time);
      // 正负号：正 = B 的那根线在 A 之后（跟 phaseOffset 的"第二个起点更晚为正"一致）；
      // 匹配只看绝对值——A 落后一拍和 B 落后一拍都是"差一拍"。
      const signed = (near.boundaryTime - time) / near.beatSeconds;
      const discrete = o.discreteOffsets.find(offset => Math.abs(Math.abs(signed) - offset) <= o.offsetTolerance);
      if (discrete === undefined) { flush(); continue; }
      const value = Math.sign(signed) * discrete;
      if (runOffset !== null && Math.abs(value - runOffset) < 1e-9) {
        runLength += 1;
        runMin = Math.min(runMin, signed);
        runMax = Math.max(runMax, signed);
        continue;
      }
      flush();
      runStart = bar;
      runOffset = value;
      runLength = 1;
      runMin = signed;
      runMax = signed;
    }
    flush();
  }

  // ---------------------------------------------------------------- Grid Pair Diff
  //
  // 给一对网格做对比：每个事件归哪边（shared / A-only / B-only）、逐小节的重叠分、
  // 以及"整首哪里是热区"。这些是给研究者的观察窗，仍然只读文档、不写回。
  //
  // 关于事件来源（实测，2026-10-10）：这批文档里的事件几乎全是机器拍点
  // （kiechau 474 个事件里 474 个是 machine_beat / machine_downbeat，人产生的只有 7 个
  // 播放器标记）。所以**不能**默认把机器拍点排除——那样盘上什么都不剩。默认全都算，
  // 但把机器产生的标出来（`machine: true`），界面据此换一种画法；`onlyHuman` 开关留给
  // "只看人那 7 个"的场合。机器拍点对自己那条网格恒等于 0 拍，这是已知的重影，不是发现。

  // ---------------------------------------------------------------- 准入（analysisEligibility）
  //
  // 研究者 2026-10-10 定的规矩：**不判断哪条网格对，先把每条结论的资格暴露出来**。
  // 一条派生结论算出来了，不代表能拿去下判断——如果它的来源里有一条本来就不可信，
  // 这条结论只能进"存疑"档，不能用来给另一条网格定罪。
  //
  // 只有三档：eligible / caution / ineligible。它不是第三套健康算法，
  // 只回答"这条派生结论能不能进入后续分析"；往后 workbench 不必理解 suspectReasons，
  // 只读这一个字段。
  //
  // 来源网格的健康取自文档里的 grid.health（人工线由 pre-clean 写 ok/suspect；
  // 机器网格现在没有这个字段，按 ok 处理）。认不出来的一律算 eligible——
  // 没有证据说它不可信，就不给它扣帽子。

  const ELIGIBILITY = ['eligible', 'caution', 'ineligible'];

  /** 一条网格自己的健康，折算成准入里的来源档。 */
  function gridHealth(grid) {
    const declared = String((grid && (grid.health || (grid.raw && grid.raw.health))) || '').toLowerCase();
    if (declared === 'suspect' || declared === 'ineligible' || declared === 'unusable') return 'ineligible';
    if (declared === 'caution' || declared === 'uncertain' || declared === 'unknown') return 'caution';
    return 'eligible';
  }

  /**
   * 一条派生结论的准入档。

   * 只三条规则：**所有**来源都不可信 → 这条结论 ineligible；只要有一条不可信或存疑
   * → caution；全都可信 → eligible。不判断谁对，只看来源够不够格。
   * （"所有"而不是"任意"是研究者给的例子定的：beat_this usable + madmom suspect
   *  → caution，这条结论还能看，只是不能拿它给 beat_this 定罪。）
   */
  function eligibilityOf(grids) {
    const levels = (grids || []).map(gridHealth);
    if (!levels.length) return 'eligible';
    const ineligible = levels.filter((level) => level === 'ineligible').length;
    if (ineligible === levels.length) return 'ineligible';
    if (levels.some((level) => level !== 'eligible')) return 'caution';
    return 'eligible';
  }

  /**
   * 给界面/下游用的一小段：来源各自什么健康、能不能用。

   * `validRange` 没有就写 `unknown`——**不编**。网格的有效区间现在文档里还没有，
   * 等 workbench 那边把每曲的可用区间导进来再填。
   */
  function eligibilityReport(grids) {
    return {
      sourceHealth: (grids || []).map((grid) => ({
        gridId: grid.gridId,
        health: grid.health || 'ok',
        level: gridHealth(grid),
        reasons: (grid.suspectReasons || []).slice(),
        valid: grid.validRange || 'unknown',
      })),
      analysisEligibility: eligibilityOf(grids),
    };
  }

  /** 这个事件算不算进这次对比。 */
  function wanted(event, raw) {
    if (!(raw && raw.onlyHuman)) return true;
    return String(event.source || '') !== 'machine' && !isMachine(event);
  }

  /** 事件是不是机器生成的（类型带 machine / madmom，或 source 写着 machine）。 */
  function isMachine(event) {
    const type = String(event.type || '');
    const source = String(event.source || '');
    return source === 'machine' || type.indexOf('machine') === 0 || type.indexOf('madmom') === 0;
  }

  /**
   * 每个事件归哪边。

   * 两边都有关联 = shared；只有一边 = 那边的 a_only / b_only；两边都不沾 = 不进结果
   * （"跟谁都不沾"不是对比要的信息，是背景）。
   */
  function pairDiff(document, gridIdA, gridIdB, raw) {
    const o = options(raw);
    const a = (document && document.grids ? document.grids : []).find((g) => g.gridId === gridIdA);
    const b = (document && document.grids ? document.grids : []).find((g) => g.gridId === gridIdB);
    if (!a || !b || a.gridId === b.gridId) return [];
    const out = [];
    for (const event of (document.events || [])) {
      if (!wanted(event, raw)) continue;
      const relationA = relationFor(event, a, o);
      const relationB = relationFor(event, b, o);
      const kind = relationA && relationB ? 'shared' : relationA ? 'a_only' : relationB ? 'b_only' : 'none';
      if (kind === 'none') continue;
      out.push({
        eventId: event.eventId,
        kind,
        machine: isMachine(event),
        time: Number(event.time),
        a: relationA ? pick(relationA) : null,
        b: relationB ? pick(relationB) : null,
      });
    }
    return out;
  }

  /** 关系里给界面的几个数，原样带出，不做二次解释。 */
  function pick(relation) {
    return {
      barIndex: relation.barIndex,
      relativeBeat: relation.relativeBeat,
      associationClass: relation.associationClass,
    };
  }

  /** (gridId, bar) → 关联到这一根边界的事件 id 集合。算一次，两次查询共用。 */
  function associationIndex(document, raw) {
    const o = options(raw);
    const index = new Map();
    for (const grid of (document && document.grids ? document.grids : [])) {
      for (const event of (document.events || [])) {
        if (!wanted(event, raw)) continue;
        const relation = relationFor(event, grid, o);
        if (!relation) continue;
        const key = grid.gridId + '#' + relation.barIndex;
        if (!index.has(key)) index.set(key, new Set());
        index.get(key).add(event.eventId);
      }
    }
    return index;
  }

  /**
   * 逐小节的关联重叠分（Jaccard）。

   * 对齐方式：A 的第 k 根边界，配离它最近的 B 的那根边界（`bBarIndex` 会写出来，
   * 差得远时读的人能看到）。两边各自关联到的事件集合取 Jaccard——它回答的是
   * "这两条网格是不是把同一批事件解释成发生在小节切换附近"。
   * 两边都没事件时给 NaN：不是 1（完美一致），是"这里没有证据"。
   */
  function overlapScores(document, gridIdA, gridIdB, raw) {
    const o = options(raw);
    const grids = (document && document.grids) ? document.grids : [];
    const a = grids.find((g) => g.gridId === gridIdA);
    const b = grids.find((g) => g.gridId === gridIdB);
    if (!a || !b || a.gridId === b.gridId) return [];
    const index = associationIndex(document, raw);
    const rows = [];
    const scan = scanBars(document, a, o);
    // 这一整列分数都依赖同两条网格：资格一次算好，逐行带上，UI 不必自己推。
    const verdict = eligibilityReport([a, b]);
    for (let bar = 0; bar < scan; bar += 1) {
      const time = core().barStart(a, bar);
      if (!isFinite(time)) break;
      const near = nearestBoundary(b, time);
      const setA = index.get(a.gridId + '#' + bar) || new Set();
      const setB = index.get(b.gridId + '#' + near.bar) || new Set();
      let shared = 0;
      for (const id of setA) if (setB.has(id)) shared += 1;
      const union = setA.size + setB.size - shared;
      rows.push({
        barIndex: bar,
        bBarIndex: near.bar,
        time,
        aCount: setA.size,
        bCount: setB.size,
        sharedCount: shared,
        overlap: union > 0 ? shared / union : NaN,
        sourceHealth: verdict.sourceHealth,
        analysisEligibility: verdict.analysisEligibility,
      });
    }
    return rows;
  }

  /**
   * 热区：连续若干小节重叠分偏低的地方。

   * 单格的尖峰不算（跟相位候选同一个教训）：要连续 `minRunBars` 格以上、
   * 每格的分歧（1 − overlap）都不低于 `disagreement` 才算一段。没证据（overlap 为 NaN）
   * 的格子不参与，也不打断——它既不是一致也不是分歧。
   */
  function hotRuns(rows, raw) {
    const o = options(raw);
    const asked = raw && isFinite(Number(raw.disagreement)) ? Number(raw.disagreement) : 0.5;
    const threshold = asked;
    const out = [];
    let run = null;
    const flush = () => {
      if (run && run.bars >= o.minRunBars) {
        out.push({
          startBar: run.startBar,
          endBar: run.endBar,
          bars: run.bars,
          aroundTime: run.aroundTime,
          worstOverlap: run.worst,
        });
      }
      run = null;
    };
    for (const row of (rows || [])) {
      const overlap = Number(row.overlap);
      if (!isFinite(overlap)) continue;
      if (1 - overlap >= threshold) {
        if (!run) run = { startBar: row.barIndex, endBar: row.barIndex, bars: 0, aroundTime: row.time, worst: overlap };
        run.endBar = row.barIndex;
        run.bars += 1;
        run.worst = Math.min(run.worst, overlap);
        continue;
      }
      flush();
    }
    flush();
    return out;
  }

  const api = {
    HARD_CAP_BEATS,
    DEFAULTS,
    options,
    classify,
    beatSecondsOf,
    nearestBoundary,
    relationFor,
    boundaryAssociations,
    associatedEvents,
    phaseOffsetCandidates,
    pairDiff,
    overlapScores,
    hotRuns,
    gridHealth,
    eligibilityOf,
    eligibilityReport,
    ELIGIBILITY,
  };

  const target = globalThis.MGMCS || (globalThis.MGMCS = {});
  target.boundary = api;
}());
