# -*- coding: utf-8 -*-
"""生成后可玩性校验 + 自动修复（需求 §十五 / §十六）。

校验（只读，输出结构化问题清单）：
    MaxNotesPerSecond / MinSameLaneInterval / MaxChordSize / MaxRepeatedLaneCount /
    MaxHoldOverlap / ImpossibleSimultaneousInput / ExcessiveJump / LaneCollision

修复（先删低价值内容 → 改轨 → 压缩和弦 → Tap 替代 Hold），每一步都记录原因：
    Event 023 Removed  Reason: DensityLimit
    Event 044 Lane 0 → Lane 1  Reason: JackReduction
    Event 071 Chord 4 → Chord 2  Reason: MaxChordSize

与 fix_charts.apply_guardrails 的关系：那个是「导入即清洗」的老护栏（去重/两指化/长按裁剪），
本模块在生成管线里做参数化校验与 jack/长音修复，两者可叠加使用（midi_chart.py 依次调用）。
"""
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

LANE_COUNT = 5
EPS = 0.001


@dataclass
class Change:
    """一次自动修改（带原因，需求 §十六）。"""
    time: float
    lane: Optional[int]
    kind: str
    reason: str
    detail: str

    def to_dict(self) -> Dict[str, Any]:
        return {"time": round(self.time, 3), "lane": self.lane, "kind": self.kind,
                "reason": self.reason, "detail": self.detail}


@dataclass
class Report:
    issues: List[Dict[str, Any]] = field(default_factory=list)
    changes: List[Change] = field(default_factory=list)
    stats: Dict[str, Any] = field(default_factory=dict)

    @property
    def ok(self) -> bool:
        return not self.issues


def _is_long(note: Dict[str, Any]) -> bool:
    return note.get("type") in ("hold", "slide") and note.get("duration", 0) > 0


def _note_key(note: Dict[str, Any]) -> Tuple[int, float]:
    return (int(note.get("lane", 0)), round(float(note.get("time", 0.0)), 3))


def validate(notes: List[Dict[str, Any]], profile: Dict[str, Any],
             lane_count: int = LANE_COUNT) -> Report:
    """只读体检：返回问题清单 + 统计（不改动输入）。"""
    report = Report()
    notes = sorted(notes, key=lambda n: (n.get("time", 0.0), n.get("lane", 0)))
    max_nps = float(profile.get("maxNPS", 10.0))
    min_same_lane = float(profile.get("minSameLaneIntervalBeats", 0.5))
    max_chord = int(profile.get("maxChordSize", 2))
    max_jack = int(profile.get("maxJackCount", 2))
    max_hold_overlap = float(profile.get("maxHoldOverlap", 0))
    max_jump = int(profile.get("maxLaneJump", 3))
    bpm = float(profile.get("bpm", 120.0) or 120.0)
    same_lane_seconds = min_same_lane * 60.0 / bpm

    # ① NPS：1 秒滑窗
    worst_t, worst_count = None, 0
    for index, note in enumerate(notes):
        t0 = note.get("time", 0.0)
        count = sum(1 for n in notes[index:] if n.get("time", 0.0) - t0 < 1.0)
        if count > worst_count:
            worst_t, worst_count = t0, count
    if worst_count > max_nps:
        report.issues.append({"kind": "MaxNotesPerSecond", "time": worst_t, "lane": None,
                              "detail": "1 秒窗口 %d 音 > 上限 %.1f" % (worst_count, max_nps)})

    # ② 同轨连打 / 同轨同刻
    run_lane, run_count, prev_time = None, 0, None
    for note in notes:
        lane, t = note.get("lane", 0), note.get("time", 0.0)
        if lane == run_lane and prev_time is not None and (t - prev_time) < same_lane_seconds:
            run_count += 1
            if run_count > max_jack:
                report.issues.append({"kind": "MaxRepeatedLaneCount", "time": t, "lane": lane,
                                      "detail": "轨 %d 在 %.3fs 内连打 %d 次 > 上限 %d"
                                                % (lane, same_lane_seconds, run_count, max_jack)})
                run_count = 0
        else:
            run_lane, run_count = lane, 1
        prev_time = t

    # ③ 同刻落手数 / 同轨碰撞
    groups: List[List[Dict[str, Any]]] = []
    for note in notes:
        if groups and abs(groups[-1][0].get("time", 0.0) - note.get("time", 0.0)) < EPS:
            groups[-1].append(note)
        else:
            groups.append([note])
    for group in groups:
        lanes = [n.get("lane") for n in group]
        if len(group) > max_chord:
            report.issues.append({"kind": "MaxChordSize", "time": group[0].get("time", 0.0),
                                  "lane": None, "detail": "同刻 %d 音 > 上限 %d"
                                                          % (len(group), max_chord)})
        if len(set(lanes)) != len(lanes):
            report.issues.append({"kind": "LaneCollision", "time": group[0].get("time", 0.0),
                                  "lane": None, "detail": "同刻同轨重复落手：%s" % lanes})
        if len(group) > 2:
            report.issues.append({"kind": "ImpossibleSimultaneousInput",
                                  "time": group[0].get("time", 0.0), "lane": None,
                                  "detail": "同刻 %d 音超出两指可打" % len(group)})

    # ④ 同轨长音重叠
    longs = [n for n in notes if _is_long(n)]
    for index, a in enumerate(longs):
        for b in longs[index + 1:]:
            if a.get("lane") != b.get("lane"):
                continue
            a_end = a.get("time", 0.0) + a.get("duration", 0.0)
            if a.get("time", 0.0) < b.get("time", 0.0) < a_end - max_hold_overlap - EPS:
                report.issues.append({"kind": "MaxHoldOverlap", "time": b.get("time", 0.0),
                                      "lane": b.get("lane"),
                                      "detail": "轨 %s 长音重叠（%.2fs + %.2fs）"
                                                % (a.get("lane"), a.get("time", 0.0),
                                                   a.get("duration", 0.0))})

    # ⑤ 过量跳轨（相邻落手跨轨过大）
    for index in range(1, len(notes)):
        prev, note = notes[index - 1], notes[index]
        if note.get("time", 0.0) - prev.get("time", 0.0) > 0.35:
            continue
        jump = abs(int(note.get("lane", 0)) - int(prev.get("lane", 0)))
        if jump > max_jump:
            report.issues.append({"kind": "ExcessiveJump", "time": note.get("time", 0.0),
                                  "lane": note.get("lane"),
                                  "detail": "相邻落手跨 %d 轨 > 上限 %d" % (jump, max_jump)})

    report.stats = {
        "notes": len(notes),
        "peakNPS": worst_count,
        "peakNPSTime": worst_t,
        "holds": sum(1 for n in notes if n.get("type") == "hold"),
        "slides": sum(1 for n in notes if n.get("type") == "slide"),
        "flicks": sum(1 for n in notes if n.get("type") == "flick"),
        "maxChord": max((len(g) for g in groups), default=0),
    }
    return report


def repair(notes: List[Dict[str, Any]], profile: Dict[str, Any],
           lane_count: int = LANE_COUNT) -> Tuple[List[Dict[str, Any]], List[Change]]:
    """按「删弱音 → 改轨 → 压和弦 → Tap 替 Hold」的顺序修复，返回 (notes, changes)。"""
    if not profile.get("repair", True):
        return notes, []
    changes: List[Change] = []
    max_chord = int(profile.get("maxChordSize", 2))
    max_jack = int(profile.get("maxJackCount", 2))
    bpm = float(profile.get("bpm", 120.0) or 120.0)
    same_lane_seconds = float(profile.get("minSameLaneIntervalBeats", 0.5)) * 60.0 / bpm

    notes = sorted(notes, key=lambda n: (n.get("time", 0.0), n.get("lane", 0)))

    # ① 去重（同刻同轨同类型）
    deduped, seen = [], set()
    for note in notes:
        key = (_note_key(note), note.get("type"))
        if key in seen:
            changes.append(Change(note.get("time", 0.0), note.get("lane"), note.get("type", "?"),
                                  "Duplicate", "同刻同轨重复音符被丢弃"))
            continue
        seen.add(key)
        deduped.append(note)
    notes = deduped

    # ② 和弦压缩：同刻超过上限 → 丢掉跳轨最小的多余音（保留音域展宽的两端）
    groups: List[List[Dict[str, Any]]] = []
    for note in notes:
        if groups and abs(groups[-1][0].get("time", 0.0) - note.get("time", 0.0)) < EPS:
            groups[-1].append(note)
        else:
            groups.append([note])
    keep_ids = set()
    for group in groups:
        if len(group) <= max_chord:
            keep_ids.update(id(n) for n in group)
            continue
        ordered = sorted(group, key=lambda n: n.get("lane", 0))
        chosen = [ordered[0], ordered[-1]] if max_chord >= 2 else [ordered[0]]
        keep_ids.update(id(n) for n in chosen)
        for note in group:
            if id(note) not in keep_ids:
                changes.append(Change(note.get("time", 0.0), note.get("lane"), note.get("type", "?"),
                                      "MaxChordSize",
                                      "同刻 %d 音 → 压缩到 %d 音（保留音域两端）"
                                      % (len(group), min(max_chord, 2))))
    notes = [n for n in notes if id(n) in keep_ids]

    # ③ Jack 削减：同轨快速连打 → 挪到最近空闲轨
    lane_busy: Dict[int, float] = {}
    current_time = None
    used_at_moment: set = set()
    repaired: List[Dict[str, Any]] = []
    run_lane, run_count, prev_time = None, 0, None
    for note in sorted(notes, key=lambda n: (n.get("time", 0.0), n.get("lane", 0))):
        t = note.get("time", 0.0)
        if current_time is None or abs(t - current_time) >= EPS:
            current_time, used_at_moment = t, set()
        lane = int(note.get("lane", 0))
        if lane == run_lane and prev_time is not None and (t - prev_time) < same_lane_seconds:
            run_count += 1
            if run_count > max_jack:
                target = _nearest_free_lane(lane, lane_count, used_at_moment)
                if target is not None:
                    changes.append(Change(t, lane, note.get("type", "?"), "JackReduction",
                                          "同轨连打：轨 %d → 轨 %d" % (lane, target)))
                    note["lane"] = lane = target
                    run_count = 1
        else:
            run_lane, run_count = lane, 1
        used_at_moment.add(lane)
        prev_time = t
        repaired.append(note)
    notes = repaired

    # ④ 长音重叠：先开始者截断到后者起点；仍冲突则后者降级 tap
    longs = sorted([n for n in notes if _is_long(n)], key=lambda n: n.get("time", 0.0))
    for index, a in enumerate(longs):
        for b in longs[index + 1:]:
            if a.get("lane") != b.get("lane"):
                continue
            a_time, a_dur = a.get("time", 0.0), a.get("duration", 0.0)
            b_time = b.get("time", 0.0)
            if not (a_time < b_time < a_time + a_dur - EPS):
                continue
            room = round(b_time - a_time, 3)
            if room > 0.35:
                changes.append(Change(a_time, a.get("lane"), a.get("type", "?"), "HoldOverlap",
                                      "长音截断 %.2fs → %.2fs（避让同轨后续长音）" % (a_dur, room)))
                a["duration"] = room
            else:
                changes.append(Change(b_time, b.get("lane"), b.get("type", "?"), "HoldOverlap",
                                      "长音被降级为 tap（同轨重叠且避让空间不足）"))
                b["type"] = "tap"
                b["duration"] = 0.0
                b.pop("path", None)

    return notes, changes


def _nearest_free_lane(lane: int, lane_count: int, used: set) -> Optional[int]:
    for distance in range(1, lane_count + 1):
        for candidate in (lane + distance, lane - distance):
            if 0 <= candidate < lane_count and candidate not in used:
                return candidate
    return None
