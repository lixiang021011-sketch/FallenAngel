# -*- coding: utf-8 -*-
"""谱面质量指标（只读度量，不改谱面）—— 需求 A 项：用人类谱基线给生成结果打分。

方法论与公式改编自开源项目 **rhythm-chart-diffusion**（MIT，© 2026 Jeonghyun Moon,
Hyunwoo Ji, Youngbok Pyo）的 `src/evaluation/`：
  - `metrics.onset_f1`  → `onset_f1`（贪心一对一匹配，±20/±50ms）
  - `patterns.summarize` → `pattern_clarity`（周期 motif 覆盖率 + 随机基线 chance）
  - `holds.summarize`    → `hold_stats`（长键占比/长度/短长键）
  - `structure`, `sr`    → 见 `bar_similarity`（简化版）与 `star_rating`（可选依赖）
人类谱基线数值放在 `metrics_baseline.json`（同源，未改动其数值）。

坐标约定：本作谱面 `time` 是秒、`metadata.bpm` 是常数；有 events 通道的 bpm 变化时按段积分。
度量内部统一换算成 **beat** 与 **cell（1/12 拍）**，与 origin 的 token 网格对齐。

用法（自查）:
    python -X utf8 chart_tools/chart_metrics.py Logs/midi_chart_demo/phrase_bass_groove_Bass_Hard.json
"""
import json
import os
import random
import sys
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

CELLS_PER_BEAT = 12          # 与 origin 的 token 网格一致（1/12 拍）
BAR_BEATS = 4.0

BASELINE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                             "metrics_baseline.json")
LONG_TYPES = ("hold", "slide")


def load_baseline(path: Optional[str] = None) -> Dict[str, Any]:
    with open(path or BASELINE_PATH, encoding="utf-8") as handle:
        return json.load(handle)


# ---------------------------------------------------------------- 时间 ↔ 拍

def beat_map(chart: Dict[str, Any]):
    """返回 f(秒) → 拍。events 通道里的 bpm 事件按段积分；无事件则常数 BPM。"""
    metadata = chart.get("metadata") or {}
    base_bpm = float(metadata.get("bpm") or 120.0)
    if base_bpm <= 0:
        base_bpm = 120.0
    segments: List[Tuple[float, float, float]] = [(0.0, 0.0, base_bpm)]   # (time, beat, bpm)
    changes = sorted((e for e in (chart.get("events") or [])
                      if str(e.get("type", "")).lower() == "bpm"
                      and float(e.get("value") or 0) > 0), key=lambda e: e["time"])
    for event in changes:
        time = float(event["time"])
        if time <= segments[-1][0]:
            continue
        prev_time, prev_beat, prev_bpm = segments[-1]
        segments.append((time, prev_beat + (time - prev_time) * prev_bpm / 60.0,
                         float(event["value"])))

    def beat_at(time: float) -> float:
        segment = segments[0]
        for candidate in segments:
            if candidate[0] <= time:
                segment = candidate
            else:
                break
        return segment[1] + (time - segment[0]) * segment[2] / 60.0

    return beat_at


def _notes(chart: Dict[str, Any]) -> List[Dict[str, Any]]:
    return sorted(chart.get("notes") or [], key=lambda n: (n.get("time", 0.0), n.get("lane", 0)))


def rows(chart: Dict[str, Any]) -> List[Dict[str, Any]]:
    """把音符压成"事件行"：同一 (cell) 的落手算一行，lanes = 该行的轨集合。
    长键只计 onset（与 onset F1 口径一致）。"""
    beat_at = beat_map(chart)
    grouped: Dict[int, Dict[str, Any]] = {}
    for note in _notes(chart):
        beat = beat_at(float(note.get("time", 0.0)))
        cell = int(round(beat * CELLS_PER_BEAT))
        row = grouped.setdefault(cell, {"cell": cell, "beat": cell / CELLS_PER_BEAT,
                                        "lanes": set(), "types": set(), "onsets": 0})
        row["lanes"].add(int(note.get("lane", 0)))
        row["types"].add(str(note.get("type", "tap")))
        row["onsets"] += 1
    return [grouped[cell] for cell in sorted(grouped)]


# ---------------------------------------------------------------- 基础指标

def note_stats(chart: Dict[str, Any]) -> Dict[str, Any]:
    notes = _notes(chart)
    beat_at = beat_map(chart)
    if not notes:
        return {"notes": 0}
    times = [float(n.get("time", 0.0)) for n in notes]
    last = max(times[-1], 0.001)
    counts: Dict[str, int] = {}
    for note in notes:
        key = str(note.get("type", "tap"))
        counts[key] = counts.get(key, 0) + 1
    return {
        "notes": len(notes),
        "durationSeconds": round(last, 3),
        "durationBeats": round(beat_at(last), 3),
        "counts": counts,
        "simultaneousMax": max((len(row["lanes"]) for row in rows(chart)), default=0),
    }


def nps_stats(chart: Dict[str, Any]) -> Dict[str, Any]:
    """中位/峰值 NPS 与每拍音符数（长键只计 onset）。"""
    notes = _notes(chart)
    if not notes:
        return {"medianNps": 0.0, "peakNps": 0.0, "notesPerBeat": 0.0}
    beat_at = beat_map(chart)
    times = [float(n.get("time", 0.0)) for n in notes]
    span = max(times[-1] - times[0], 0.5)
    windows = []
    index = 0
    for start in times:
        while index < len(times) and times[index] < start:
            index += 1
        count = 0
        cursor = index
        while cursor < len(times) and times[cursor] - start < 1.0:
            count += 1
            cursor += 1
        windows.append(count)
    windows.sort()
    median = windows[len(windows) // 2]
    beats = max(beat_at(times[-1]) - beat_at(times[0]), 0.25)
    return {
        "medianNps": round(float(median), 3),
        "peakNps": round(float(max(windows)), 3),
        "meanNps": round(len(notes) / span, 3),
        "notesPerBeat": round(len(notes) / beats, 3),
    }


def hold_stats(chart: Dict[str, Any]) -> Dict[str, Any]:
    """长键统计：占比、长度分位（cell）、短长键比例、松手→同轨下一次按下的间隔。"""
    notes = _notes(chart)
    beat_at = beat_map(chart)
    onsets = len(rows(chart)) or 1
    holds = [n for n in notes if str(n.get("type")) in LONG_TYPES and float(n.get("duration") or 0) > 0]
    lengths = sorted(float(n["duration"]) * float((chart.get("metadata") or {}).get("bpm") or 120.0)
                     / 60.0 * CELLS_PER_BEAT for n in holds)

    def percentile(values: Sequence[float], fraction: float) -> float:
        if not values:
            return 0.0
        index = max(0, min(len(values) - 1, int(round(fraction * (len(values) - 1)))))
        return round(values[index], 3)

    short = sum(1 for value in lengths if value < 3.0) / len(lengths) if lengths else 0.0
    gaps = []
    by_lane: Dict[int, List[float]] = {}
    for note in notes:
        by_lane.setdefault(int(note.get("lane", 0)), []).append(float(note.get("time", 0.0)))
    for lane, times in by_lane.items():
        times.sort()
        for hold in (n for n in notes if int(n.get("lane", 0)) == lane
                     and str(n.get("type")) in LONG_TYPES):
            release = float(hold.get("time", 0.0)) + float(hold.get("duration") or 0)
            for time in times:
                if time > release + 1e-6:
                    gaps.append((time - release) * float((chart.get("metadata") or {}).get("bpm") or 120.0)
                                / 60.0 * CELLS_PER_BEAT)
                    break
    return {
        "holdCount": len(holds),
        "holdShare": round(len(holds) / onsets, 4),
        "lenCellsP10": percentile(lengths, 0.10),
        "lenCellsP25": percentile(lengths, 0.25),
        "lenCellsP50": percentile(lengths, 0.50),
        "shortShare": round(short, 4),
        "releaseGapCellsP50": percentile(sorted(gaps), 0.50) if gaps else None,
        "releaseGapUnder3Cells": round(sum(1 for g in gaps if g < 3.0) / len(gaps), 4) if gaps else None,
    }


def lane_balance(chart: Dict[str, Any]) -> Dict[str, Any]:
    counts = [0] * 5
    for note in _notes(chart):
        lane = int(note.get("lane", 0))
        if 0 <= lane < 5:
            counts[lane] += 1
    used = [c for c in counts if c > 0]
    return {"counts": counts,
            "imbalance": round(max(used) / min(used), 2) if used and min(used) > 0 else None,
            "unusedLanes": sum(1 for c in counts if c == 0)}


# ---------------------------------------------------------------- Pattern clarity

def _run_indices(events: List[Dict[str, Any]], max_period: int, max_gap_beats: float,
                 min_extra: int = 4) -> List[Tuple[int, int, int]]:
    segments = []
    start = 0
    for index in range(1, len(events)):
        if events[index]["beat"] - events[index - 1]["beat"] > max_gap_beats:
            segments.append((start, index))
            start = index
    segments.append((start, len(events)))

    runs: List[Tuple[int, int, int]] = []
    for begin, end in segments:
        for period in range(1, max_period + 1):
            index = begin + period
            while index < end:
                if events[index]["lanes"] != events[index - period]["lanes"]:
                    index += 1
                    continue
                cursor = index + 1
                while cursor < end and events[cursor]["lanes"] == events[cursor - period]["lanes"]:
                    cursor += 1
                length = cursor - (index - period)
                if length >= max(2 * period, period + min_extra) and \
                        not _periodic_with_divisor(events, index - period, cursor, period):
                    runs.append((index - period, cursor, period))
                index = cursor if cursor > index else index + 1
    return runs


def _periodic_with_divisor(events: List[Dict[str, Any]], begin: int, end: int, period: int) -> bool:
    for divisor in range(1, period):
        if period % divisor:
            continue
        if all(events[i]["lanes"] == events[i - divisor]["lanes"] for i in range(begin + divisor, end)):
            return True
    return False


def pattern_clarity(chart: Dict[str, Any], seeds: Sequence[int] = (0, 1, 2),
                    max_period: int = 6, max_gap_beats: float = 1.0) -> Dict[str, Any]:
    """周期 motif 覆盖率（origin `patterns.summarize` 的简化实现）。
    chance = 同节奏、同和弦数但轨随机重画时的覆盖率均值（也就是"光是节奏碰巧给出的"）。"""
    events = rows(chart)
    if len(events) < 4:
        return {"coverage": 0.0, "coverageP1": 0.0, "coverageP2": 0.0,
                "coverageP3plus": 0.0, "runLength": 0.0, "chance": 0.0, "events": len(events)}
    runs = _run_indices(events, max_period, max_gap_beats)
    total_onsets = sum(len(e["lanes"]) for e in events) or 1

    def coverage_for(run_list) -> Tuple[float, float, float, float, float]:
        covered = set()
        p1 = set()
        p2 = set()
        p3 = set()
        for begin, end, period in run_list:
            indices = set(range(begin, end))
            covered |= indices
            (p1 if period == 1 else p2 if period == 2 else p3).update(indices)
        onsets = lambda index_set: sum(len(events[i]["lanes"]) for i in sorted(index_set))
        run_length = (sum(end - begin for begin, end, _p in run_list) / len(run_list)) if run_list else 0.0
        return (onsets(covered) / total_onsets, onsets(p1) / total_onsets,
                onsets(p2) / total_onsets, onsets(p3) / total_onsets, run_length)

    coverage, p1, p2, p3, run_length = coverage_for(runs)
    chances = []
    rng = random.Random(0)
    for _seed in seeds:
        random_events = []
        for event in events:
            size = len(event["lanes"])
            lanes = frozenset(rng.sample(range(5), size))
            random_events.append({"beat": event["beat"], "lanes": lanes})
        random_runs = _run_indices(random_events, max_period, max_gap_beats)
        chances.append(coverage_for(random_runs)[0])
    return {
        "coverage": round(coverage, 4),
        "coverageP1": round(p1, 4),
        "coverageP2": round(p2, 4),
        "coverageP3plus": round(p3, 4),
        "runLength": round(run_length, 2),
        "chance": round(sum(chances) / len(chances), 4),
        "events": len(events),
    }


# ---------------------------------------------------------------- 保真度 / 红线 / 星级

def onset_f1(pred_chart: Dict[str, Any], ref_times: Iterable[float], tol_ms: float = 50.0,
             lanes: bool = False, ref_lanes: Optional[Iterable[int]] = None) -> Dict[str, Any]:
    """贪心一对一 onset 匹配（origin `metrics.onset_f1`）。
    ref_times 为参考 onset 的秒序列（如原始 MIDI 音符 onset），用于衡量"节奏保真度"。"""
    pred = [(float(n.get("time", 0.0)), int(n.get("lane", 0))) for n in _notes(pred_chart)]
    ref_time_list = [float(t) for t in ref_times]
    ref_lane_list = [int(l) for l in ref_lanes] if ref_lanes is not None else [None] * len(ref_time_list)
    ref = list(zip(ref_time_list, ref_lane_list))
    if not pred or not ref:
        return {"precision": 0.0, "recall": 0.0, "f1": 0.0, "matched": 0,
                "pred": len(pred), "ref": len(ref)}
    tolerance = tol_ms / 1000.0
    candidates = []
    for i, (ptime, plane) in enumerate(pred):
        for j, (rtime, rlane) in enumerate(ref):
            if lanes and rlane is not None and plane != int(rlane):
                continue
            delta = abs(ptime - rtime)
            if delta <= tolerance:
                candidates.append((delta, i, j))
    candidates.sort()
    used_pred, used_ref = set(), set()
    for _delta, i, j in candidates:
        if i in used_pred or j in used_ref:
            continue
        used_pred.add(i)
        used_ref.add(j)
    matched = len(used_pred)
    precision = matched / len(pred)
    recall = matched / len(ref)
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"precision": round(precision, 4), "recall": round(recall, 4), "f1": round(f1, 4),
            "matched": matched, "pred": len(pred), "ref": len(ref), "tolMs": tol_ms}


def outlier_flags(chart: Dict[str, Any], baseline: Dict[str, Any],
                  max_hold: float = 4.0) -> List[str]:
    """origin outliers.csv 的红线 + 本作协议红线（只报告，不修改）。
    鼓例外：鼓件→轨是固定映射（hihat 恒在 2 轨），"轨失衡"对鼓没有意义，故跳过。"""
    limits = baseline.get("outliers", {})
    instrument = str((chart.get("metadata") or {}).get("instrumentType") or "")
    flags: List[str] = []
    stats = note_stats(chart)
    nps = nps_stats(chart)
    balance = lane_balance(chart)
    if not stats.get("notes"):
        return ["空谱面"]
    if stats.get("simultaneousMax", 0) > int(limits.get("maxSimultaneous", 2)):
        flags.append("同刻落手 %d > %d（两指上限）"
                     % (stats["simultaneousMax"], int(limits.get("maxSimultaneous", 2))))
    if nps["peakNps"] > float(limits.get("maxNpsTwoFinger", 12.0)):
        flags.append("峰值 %.1f 音/秒 > 两指上限 %.1f" % (nps["peakNps"], limits["maxNpsTwoFinger"]))
    if instrument != "drums" and balance.get("imbalance") \
            and balance["imbalance"] > float(limits.get("laneImbalanceRatioMax", 2.0)):
        flags.append("轨失衡 %.2f > %.2f（%s）"
                     % (balance["imbalance"], limits["laneImbalanceRatioMax"], balance["counts"]))
    short_limit = float(limits.get("holdShorterThanCellBeats", 0.0833))
    for note in _notes(chart):
        if str(note.get("type")) in LONG_TYPES:
            duration_beats = float(note.get("duration") or 0) * \
                float((chart.get("metadata") or {}).get("bpm") or 120.0) / 60.0
            if 0 < duration_beats < short_limit:
                flags.append("长键短于 1/12 拍（t=%.2f，%.3f 拍）" % (note.get("time", 0), duration_beats))
                break
    for note in _notes(chart):
        if float(note.get("time", 0.0)) < -0.001:
            flags.append("负时间音符（检查 offset）")
            break
    holds = [n for n in _notes(chart) if str(n.get("type")) == "hold"]
    if holds and max(float(n.get("duration") or 0) for n in holds) > max_hold:
        flags.append("长键超过 %.1fs 上限" % max_hold)
    return flags


def star_rating(chart: Dict[str, Any], key_count: int = 5) -> Optional[float]:
    """本作 5 轨谱 → 临时 .osu → rosu-pp 星级（可选依赖 rosu-pp-py，未安装返回 None）。
    说明：drag/flick/slide 没有 osu!mania 对应物，按 tap（带时长时长键）估算，仅供难度标定参考。"""
    try:
        import rosu_pp_py as rosu          # type: ignore
    except ImportError:
        return None
    import tempfile
    beat = 60000.0 / float((chart.get("metadata") or {}).get("bpm") or 120.0)
    lines = ["osu file format v14", "", "[General]", "Mode: 3", "", "[Difficulty]",
             "CircleSize: %d" % key_count, "OverallDifficulty: 8", "ApproachRate: 5",
             "", "[TimingPoints]", "0,%.6f,4,2,0,70,1,0" % beat, "", "[HitObjects]"]
    for note in _notes(chart):
        lane = max(0, min(key_count - 1, int(note.get("lane", 0))))
        x = int((lane + 0.5) * 512 / key_count)
        time = float(note.get("time", 0.0)) * 1000.0
        if str(note.get("type")) in LONG_TYPES and float(note.get("duration") or 0) > 0:
            end = time + float(note["duration"]) * 1000.0
            lines.append("%d,192,%d,128,0,%d:0:0:0:0:" % (x, time, end))
        else:
            lines.append("%d,192,%d,1,0,0:0:0:0:" % (x, time))
    with tempfile.NamedTemporaryFile("w", suffix=".osu", delete=False, encoding="utf-8") as handle:
        handle.write("\n".join(lines))
        path = handle.name
    try:
        beatmap = rosu.Beatmap(path=path)
        difficulty = rosu.Difficulty().calculate(beatmap)
        return round(float(difficulty.stars), 3)
    except Exception:                       # noqa: BLE001 - 星级只是参考，失败不影响体检
        return None
    finally:
        try:
            os.unlink(path)
        except OSError:
            pass


# ---------------------------------------------------------------- 与基线对比

GRADE_ALIASES = {"Easy": "Easy", "Normal": "Normal", "Hard": "Hard",
                 "Insane": "Hard", "Expert": "Expert", "Expert+": "Expert"}


def compare_to_baseline(chart: Dict[str, Any], grade: str,
                        baseline: Optional[Dict[str, Any]] = None) -> List[Dict[str, Any]]:
    """与本难度的人类谱分位对比，返回逐项 (指标, 本作, 人类, 结论)。"""
    baseline = baseline or load_baseline()
    human = baseline["grades"].get(GRADE_ALIASES.get(grade, grade))
    if human is None:
        return []
    two_finger = baseline.get("twoFinger", {})
    scale = float(two_finger.get("maxNps", 12.0)) / max(
        float(baseline["grades"]["Expert"]["npsMedian"]), 1e-6)
    nps = nps_stats(chart)
    holds = hold_stats(chart)
    patterns = pattern_clarity(chart)
    rows_out: List[Dict[str, Any]] = []

    def verdict(ours: float, low: float, high: float, unit: str = "") -> str:
        if ours < low:
            return "偏低"
        if ours > high:
            return "偏高"
        return "吻合"

    target = human["npsMedian"] * scale
    rows_out.append({"metric": "中位 NPS", "ours": nps["medianNps"], "human": human["npsMedian"],
                     "humanTwoFinger": round(target, 2), "unit": "音/秒",
                     "verdict": verdict(nps["medianNps"], target * 0.7, target * 1.4)})
    rows_out.append({"metric": "每拍音符数", "ours": nps["notesPerBeat"], "human": human["npbMedian"],
                     "humanTwoFinger": round(human["npbMedian"] * scale, 2), "unit": "个/拍",
                     "verdict": verdict(nps["notesPerBeat"], human["npbMedian"] * scale * 0.6,
                                        human["npbMedian"] * scale * 1.6)})
    rows_out.append({"metric": "长键占比", "ours": holds["holdShare"], "human": human["lnShareP50"],
                     "humanTwoFinger": human["lnShareP50"], "unit": "占 onset",
                     "verdict": verdict(holds["holdShare"], human["lnShareP50"] * 0.5,
                                        human["lnShareP75"])})
    rows_out.append({"metric": "长键长度中位", "ours": holds["lenCellsP50"],
                     "human": human["holdLenCellsP50"], "humanTwoFinger": human["holdLenCellsP50"],
                     "unit": "1/12 拍",
                     "verdict": verdict(holds["lenCellsP50"], human["holdLenCellsP25"] * 0.7,
                                        human["holdLenCellsP50"] * 2.0)})
    rows_out.append({"metric": "短长键占比", "ours": holds["shortShare"],
                     "human": human["shortHoldShare"], "humanTwoFinger": human["shortHoldShare"],
                     "unit": "占长键",
                     "verdict": verdict(holds["shortShare"], 0.0, human["shortHoldShare"] * 2.5 + 0.02)})
    rows_out.append({"metric": "pattern 覆盖", "ours": patterns["coverage"],
                     "human": human["patternCoverage"], "humanTwoFinger": human["patternCoverage"],
                     "unit": "占 onset",
                     "verdict": verdict(patterns["coverage"], human["patternCoverage"] * 0.5,
                                        max(0.06, human["patternCoverage"] * 3.0))})
    rows_out.append({"metric": "pattern 随机基线", "ours": patterns["chance"],
                     "human": human["patternChance"], "humanTwoFinger": human["patternChance"],
                     "unit": "占 onset",
                     "verdict": "越低越好（本作不应明显高于人类）"
                     if patterns["chance"] <= human["patternChance"] * 2.5 else "偏高"})
    return rows_out


def summarize(chart: Dict[str, Any], grade: Optional[str] = None,
              baseline: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    baseline = baseline or load_baseline()
    grade = grade or str((chart.get("metadata") or {}).get("difficultyName") or "Hard")
    return {
        "songName": (chart.get("metadata") or {}).get("songName"),
        "instrument": (chart.get("metadata") or {}).get("instrumentType"),
        "grade": grade,
        "noteStats": note_stats(chart),
        "nps": nps_stats(chart),
        "holds": hold_stats(chart),
        "lanes": lane_balance(chart),
        "patterns": pattern_clarity(chart),
        "starRating": star_rating(chart),
        "outliers": outlier_flags(chart, baseline),
        "vsHuman": compare_to_baseline(chart, grade, baseline),
    }


def main(argv: Optional[List[str]] = None) -> int:
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = list(argv if argv is not None else sys.argv[1:])
    if not args:
        print(__doc__)
        return 0
    baseline = load_baseline()
    exit_code = 0
    for path in args:
        with open(path, encoding="utf-8") as handle:
            chart = json.load(handle)
        summary = summarize(chart, baseline=baseline)
        print("== %s（%s / %s）" % (path, summary["instrument"], summary["grade"]))
        print("   音符 %(notes)d，峰值 %(peakNps).1f 音/秒，长键占比 %(holdShare).3f，"
              "pattern 覆盖 %(coverage).3f（随机 %(chance).3f）"
              % {**summary["nps"], **summary["holds"], **summary["patterns"],
                 "notes": summary["noteStats"]["notes"]})
        for row in summary["vsHuman"]:
            print("   - %-14s 本作 %-8s 人类 %-8s（两指折算 %-8s）%s"
                  % (row["metric"], row["ours"], row["human"], row["humanTwoFinger"], row["verdict"]))
        for flag in summary["outliers"]:
            print("   ⚠ " + flag)
            exit_code = 1
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
