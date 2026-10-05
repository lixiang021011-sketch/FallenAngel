# -*- coding: utf-8 -*-
"""MIDI → 归一化 IR 前端 —— 谱面转换协议 §2「MIDI 职责」的语义层。

管线（对应需求文档 §二 / §二十六 Step 1-5）：
    MidiParser(midi_parse.py)
      → TempoMap / TimeSignatureMap（beat ↔ 秒，谱面逻辑一律用 beat）
      → Quantizer（Straight / Swing / Triplet，minDivision 可配）
      → EventGrouper（近似同响 → 一个 MusicalEvent，和弦聚合）
      → ImportanceScorer（强拍/力度/时值/旋律转折/乐句边界）
      → DifficultyProcessor（减音、和弦压缩、密度上限、Tap/Hold 决策）
      → IRStream（交给 rule_engine.py 做乐器映射 → Chart v2 JSON）

设计约束（与项目铁律一致）：
- 时间轴以 beat 为主，秒只由 TempoMap 折算，方便换 BPM / 变速 / Phrase 复用；
- 所有自动改动都带原因（removals / warnings），不静默修谱；
- 纯函数式：同输入同输出，可单测、可批处理，不依赖 Unity / Editor。

用法:
  python -X utf8 chart_tools/midi_to_ir.py in.mid --instrument bass --difficulty Hard --out ir.json
  python -X utf8 chart_tools/midi_to_ir.py in.mid --instrument lead --debug debug.json
"""
import json
import os
import sys
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

from ir import IRStream, IR_PPQ, NoteEvent
from midi_parse import (MidiFileData, MidiParseError, RawNote, TempoEvent,
                        TimeSignatureEvent, parse_midi)

DEFAULT_PROFILE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "midi_profiles.json")
DIFFICULTY_ORDER = ("Easy", "Normal", "Hard", "Expert")
INSTRUMENT_TYPES = ("auto", "lead", "bass", "guitar", "keys", "drums")


# ---------------------------------------------------------------- 配置

def load_profiles(path: Optional[str] = None) -> Dict[str, Any]:
    """读配置（JSON；与项目现有 rule JSON 体系一致，不引入新的配置框架）。"""
    with open(path or DEFAULT_PROFILE_PATH, encoding="utf-8") as handle:
        return json.load(handle)


def _deep_merge(base: Dict[str, Any], override: Dict[str, Any]) -> Dict[str, Any]:
    out = dict(base)
    for key, value in (override or {}).items():
        if isinstance(value, dict) and isinstance(out.get(key), dict):
            out[key] = _deep_merge(out[key], value)
        else:
            out[key] = value
    return out


class Profiles:
    """配置视图：quantize / importance / difficulty / instruments / ..."""

    def __init__(self, raw: Dict[str, Any]):
        self.raw = raw
        self.quantize = raw.get("quantize", {})
        self.importance = raw.get("importance", {})
        self.difficulty = raw.get("difficulty", {})
        self.instruments = raw.get("instruments", {})
        self.chord = raw.get("chord", {})
        self.drum_classes = raw.get("drumClasses", {})
        self.drum_keep = raw.get("drumKeep", {})
        self.playability = raw.get("playability", {})
        self.track_detect = raw.get("trackDetect", {})

    def instrument(self, name: str) -> Dict[str, Any]:
        return self.instruments.get(name, self.instruments.get("lead", {}))

    def difficulty_profile(self, difficulty: str) -> Dict[str, Any]:
        if difficulty not in self.difficulty:
            raise ValueError("未知难度 %r（可选 %s）" % (difficulty, list(self.difficulty)))
        return dict(self.difficulty[difficulty])

    def drum_class(self, pitch: int) -> str:
        for name, pitches in self.drum_classes.items():
            if pitch in pitches:
                return name
        return "other"

    def drum_keep_classes(self, difficulty: str) -> List[str]:
        return list(self.drum_keep.get(difficulty, ["kick", "snare", "crash"]))


# ---------------------------------------------------------------- 时间轴

class TempoMap:
    """beat → 秒 的分段积分（变速 MIDI 也正确）。beat = tick / PPQ，与 BPM 无关。"""

    def __init__(self, tempos: List[TempoEvent], ppq: int, default_bpm: float = 120.0):
        self.ppq = ppq
        points = sorted(((t.tick / ppq, t.bpm) for t in tempos if t.bpm > 0),
                        key=lambda p: p[0])
        segments: List[Tuple[float, float, float]] = []     # (beat, time, bpm)
        if not points or points[0][0] > 0:
            points.insert(0, (0.0, default_bpm))
        time = 0.0
        for index, (beat, bpm) in enumerate(points):
            if index > 0:
                prev_beat, _prev_time, prev_bpm = segments[-1]
                time += (beat - prev_beat) * 60.0 / prev_bpm
            if not segments or beat > segments[-1][0]:
                segments.append((beat, time, bpm))
            else:                                            # 同 tick 多个 tempo：后者生效
                segments[-1] = (beat, time, bpm)
        self.segments = segments

    def _segment(self, beat: float) -> Tuple[float, float, float]:
        seg = self.segments[0]
        for candidate in self.segments:
            if candidate[0] <= beat:
                seg = candidate
            else:
                break
        return seg

    def time_at_beat(self, beat: float) -> float:
        seg = self._segment(beat)
        return seg[1] + (beat - seg[0]) * 60.0 / seg[2]

    def seconds_per_beat(self, beat: float) -> float:
        return 60.0 / self._segment(beat)[2]

    def bpm_at_beat(self, beat: float) -> float:
        return self._segment(beat)[2]

    def tempo_events(self) -> List[Dict[str, Any]]:
        """非首段 tempo → 事件通道 [{time, type:"bpm", value}]（协议 §1.2）。"""
        return [{"time": round(self.time_at_beat(beat), 3), "type": "bpm", "value": round(bpm, 3)}
                for beat, _time, bpm in self.segments[1:]]


class TimeSignatureMap:
    """拍号 → 小节内位置（强拍判定用）。"""

    def __init__(self, signatures: List[TimeSignatureEvent], ppq: int):
        points = sorted(((s.tick / ppq, s.numerator, s.denominator) for s in signatures
                         if s.numerator > 0 and s.denominator > 0), key=lambda p: p[0])
        if not points or points[0][0] > 0:
            points.insert(0, (0.0, 4, 4))
        self.segments = [(beat, numerator * 4.0 / denominator)
                         for beat, numerator, denominator in points]

    def _segment(self, beat: float) -> Tuple[float, float]:
        current = self.segments[0]
        for segment in self.segments:
            if segment[0] <= beat:
                current = segment
            else:
                break
        return current

    def beats_per_bar(self, beat: float) -> float:
        return self._segment(beat)[1]

    def bar_beat_and_position(self, beat: float) -> Tuple[float, float]:
        bar_start, beats_per_bar = self._segment(beat)
        return bar_start, (beat - bar_start) % beats_per_bar

    def beat_strength(self, beat: float, table: Dict[str, float], fallback: float) -> float:
        """强拍 = 小节内拍位权重 × 拍内细分权重（1/12 拍网格，straight/triplet 兼顾）。"""
        _bar, position = self.bar_beat_and_position(beat)
        beat_index = int(position)                 # 小节内第几拍（0 起）
        fraction = position - beat_index
        bar_weight = float(table.get(str(beat_index + 1), fallback))
        return round(bar_weight * subdivision_weight(fraction), 6)


class GridSnapper:
    """量化网格：Straight / Swing / Triplet，division = 每四分音符等分数（4/8/16/24/32）。"""

    def __init__(self, division: int, mode: str = "Straight", swing_ratio: float = 0.667):
        self.division = max(1, int(division))
        self.mode = mode
        self.swing_ratio = min(0.75, max(0.5, float(swing_ratio)))
        self.step = 4.0 / self.division
        if mode == "Triplet" and self.division not in (12, 24, 48):
            self.step = (4.0 / self.division) * (2.0 / 3.0)
        self.step = round(self.step, 9)

    def position(self, index: int) -> float:
        if self.mode == "Swing" and index % 2 != 0:
            return (index - 1) * self.step + 2.0 * self.step * self.swing_ratio
        return index * self.step

    def snap(self, beat: float) -> float:
        return round(self.position(int(round(beat / self.step))), 9)

    def distance(self, beat: float) -> float:
        return abs(beat - self.snap(beat))


# 小节内细分权重（相对"整拍"）：straight 与 triplet 两族的常用落点。
# 依据：人谱 onset 中 1/6、1/3、1/12 拍位置占比很高（rhythm-chart-diffusion
# docs/_stats/coverage.csv：1/16 单网格覆盖 94.8%，加入三连音族后 ~99%），
# 因此"反拍/三连音位置"不能一律当成弱位。
SUBDIVISION_WEIGHTS = {
    0: 1.00,           # 整拍
    6: 0.60,           # 1/2 拍（八分反拍）
    4: 0.50, 8: 0.50,  # 1/3 / 2/3 拍（八分三连音）
    3: 0.45, 9: 0.45,  # 1/4 / 3/4 拍（十六分反拍）
    2: 0.32, 10: 0.32, # 1/6 / 5/6 拍（十六分三连音）
}
SUBDIVISION_FALLBACK = 0.22   # 更细的 1/12 拍族


class MultiGridSnapper:
    """多个网格的并集（straight + 三连音族）：吸附到最近候选点。

    为什么不是单一网格：人谱同时使用 straight 与 triplet 位置，LCM 需要两族并存
    （1/16 与 1/6 互补，单用任何一个都会把另一族抹掉）。difficulty 的 MinSubdivision
    就是"该难度允许哪两族"。
    """

    def __init__(self, divisions, mode: str = "Straight", swing_ratio: float = 0.667):
        self.grids = [GridSnapper(int(d), mode, swing_ratio) for d in divisions if int(d) > 0]

    def snap(self, beat: float) -> float:
        if not self.grids:
            return round(beat, 9)
        return min((grid.snap(beat) for grid in self.grids), key=lambda candidate: abs(candidate - beat))

    def distance(self, beat: float) -> float:
        return abs(beat - self.snap(beat))


def subdivision_weight(position_in_beat: float) -> float:
    """拍内位置 → 权重（1/12 拍网格；超出网格回退最小权重）。"""
    index = int(round(position_in_beat * 12))
    if index == 12:
        index = 0
    if abs(position_in_beat * 12 - index) > 1e-6:
        return SUBDIVISION_FALLBACK
    return SUBDIVISION_WEIGHTS.get(index, SUBDIVISION_FALLBACK)


def quantize_beat(raw_beat: float, snapper: GridSnapper,
                  profile: Dict[str, Any]) -> Tuple[float, bool]:
    """→ (量化后 beat, 是否超出容差的 outlier)。超出容差也保留（只记录），不丢音。"""
    if profile.get("preserveExpressiveTiming"):
        return round(raw_beat, 6), False
    snapped = snapper.snap(raw_beat)
    tolerance = float(profile.get("timingTolerance", 0.09))
    return snapped, snapper.distance(raw_beat) > tolerance


# ---------------------------------------------------------------- 音乐事件

@dataclass
class MusicalEvent:
    """MIDI 音符聚合后的音乐事件（需求 §六）。后续处理的主要对象。"""
    id: int
    start_beat: float
    duration_beat: float
    pitches: List[int]
    velocities: List[int]
    sources: List[RawNote]
    representative_pitch: int
    average_velocity: float
    is_chord: bool
    note_count: int
    source_track: int
    instrument: str
    beat_strength: float = 0.0
    melodic_direction: int = 0
    is_phrase_start: bool = False
    is_phrase_end: bool = False
    importance: float = 0.0
    keep: bool = True
    removal_reason: str = ""
    removal_detail: str = ""
    note_kind: str = "tap"                 # tap / hold（HoldPolicy 决策结果）
    hold_detail: str = ""                  # 长键裁剪/降级原因（需求 §十六 不静默修复）
    keep_pitches: List[int] = field(default_factory=list)
    strum: bool = False
    strum_pitches: List[int] = field(default_factory=list)
    strum_lanes: List[int] = field(default_factory=list)
    timing_outlier: bool = False
    quantize_offset: float = 0.0

    def to_dict(self, tempo: TempoMap) -> Dict[str, Any]:
        return {
            "id": self.id,
            "startBeat": round(self.start_beat, 4),
            "time": round(tempo.time_at_beat(self.start_beat), 4),
            "durationBeat": round(self.duration_beat, 4),
            "pitches": self.pitches,
            "representativePitch": self.representative_pitch,
            "averageVelocity": round(self.average_velocity, 1),
            "isChord": self.is_chord,
            "noteCount": self.note_count,
            "beatStrength": round(self.beat_strength, 4),
            "melodicDirection": self.melodic_direction,
            "isPhraseStart": self.is_phrase_start,
            "isPhraseEnd": self.is_phrase_end,
            "importance": round(self.importance, 4),
            "sourceTrack": self.source_track,
            "instrument": self.instrument,
            "kept": self.keep,
            "removalReason": self.removal_reason,
            "removalDetail": self.removal_detail,
            "noteKind": self.note_kind,
            "holdDetail": self.hold_detail,
            "keepPitches": self.keep_pitches,
            "strum": self.strum,
            "timingOutlier": self.timing_outlier,
            "midiIds": ["t%d:c%d:p%d:%d" % (s.track, s.channel, s.pitch, s.start_tick)
                        for s in self.sources],
        }


@dataclass
class MidiConvertResult:
    ir: IRStream
    events: List[MusicalEvent]
    report: Dict[str, Any]

    @property
    def warnings(self) -> List[str]:
        return self.report.get("warnings", [])


# ---------------------------------------------------------------- 乐器识别

def classify_track(data: MidiFileData, track: int, profiles: Profiles) -> str:
    """轨 → 乐器类型（drums/bass/guitar/keys/lead）。用户可 --instrument 覆盖。"""
    notes = [n for n in data.notes if n.track == track]
    detect = profiles.track_detect
    if any(n.channel == int(detect.get("drumChannel", 9)) for n in notes):
        return "drums"
    name = (data.track_names.get(track) or "").lower()
    for kind, keywords in (detect.get("nameKeywords") or {}).items():
        if any(keyword.lower() in name for keyword in keywords):
            return kind
    programs = [n.program for n in notes if n.program is not None]
    for kind, ranges in (detect.get("programRanges") or {}).items():
        for program in programs:
            for low, high in ranges:
                if low <= program <= high:
                    return kind
    return "lead"


def select_notes(data: MidiFileData, instrument: str, profiles: Profiles,
                 track: Optional[int]) -> Tuple[List[RawNote], str, List[str]]:
    """选轨：指定 track > 指定乐器类型匹配 > 音符最多的轨。"""
    log: List[str] = []
    if track is not None:
        selected = [n for n in data.notes if n.track == track]
        kind = classify_track(data, track, profiles)
        if instrument != "auto":
            kind = instrument
        log.append("选定轨 %d（自动识别为 %s），音符 %d" % (track, kind, len(selected)))
        return selected, kind, log

    by_track: Dict[int, List[RawNote]] = {}
    for note in data.notes:
        by_track.setdefault(note.track, []).append(note)
    if not by_track:
        raise ValueError("MIDI 里没有任何音符")

    classified = {t: classify_track(data, t, profiles) for t in by_track}
    if instrument == "auto":
        best = max(by_track, key=lambda t: (len(by_track[t]), -t))
        others = {t: len(v) for t, v in by_track.items() if t != best}
        log.append("自动选择音符最多的轨 %d（识别 %s，%d 音符）；其余轨未使用：%s"
                   % (best, classified[best], len(by_track[best]), others or "无"))
        return by_track[best], classified[best], log

    matching = [t for t in by_track if classified[t] == instrument]
    if not matching:
        best = max(by_track, key=lambda t: (len(by_track[t]), -t))
        log.append("⚠ 没有识别为 %s 的轨（实际 %s），退化为音符最多的轨 %d"
                   % (instrument, classified, best))
        return by_track[best], instrument, log
    if len(matching) > 1:
        log.append("⚠ 有 %d 条轨识别为 %s，合并使用：%s" % (len(matching), instrument, matching))
    merged: List[RawNote] = []
    for t in matching:
        merged.extend(by_track[t])
    merged.sort(key=lambda n: (n.start_tick, n.pitch))
    return merged, instrument, log


# ---------------------------------------------------------------- 主流程

def convert_midi(source, instrument: str = "auto", difficulty: str = "Normal",
                 profile_path: Optional[str] = None, profile_raw: Optional[Dict[str, Any]] = None,
                 track: Optional[int] = None, title: Optional[str] = None,
                 audio: Optional[str] = None, offset: float = 0.0,
                 overrides: Optional[Dict[str, Any]] = None) -> MidiConvertResult:
    """MIDI → (IRStream, MusicalEvent[], 报告)。source 可为路径或 MidiFileData。"""
    if instrument not in INSTRUMENT_TYPES:
        raise ValueError("未知乐器类型 %r（可选 %s）" % (instrument, list(INSTRUMENT_TYPES)))
    if difficulty not in DIFFICULTY_ORDER:
        raise ValueError("未知难度 %r（可选 %s）" % (difficulty, list(DIFFICULTY_ORDER)))

    data = source if isinstance(source, MidiFileData) else parse_midi(source)
    raw_profiles = _deep_merge(load_profiles(profile_path) if profile_raw is None else profile_raw,
                               overrides or {})
    profiles = Profiles(raw_profiles)

    warnings: List[str] = list(data.warnings)
    notes_raw, kind, selection_log = select_notes(data, instrument, profiles, track)
    inst_profile = profiles.instrument(kind)
    ir_instrument = inst_profile.get("irInstrument", "other")
    diff = profiles.difficulty_profile(difficulty)

    tempo = TempoMap(data.tempos, data.ppq)
    tsmap = TimeSignatureMap(data.time_signatures, data.ppq)
    quant = raw_profiles.get("quantize", {})
    snapper = GridSnapper(int(quant.get("minDivision", 48)),
                          str(quant.get("mode", "Straight")),
                          float(quant.get("swingRatio", 0.667)))

    # 1) 归一化 tick + 量化 beat
    quantized: List[Dict[str, Any]] = []
    outliers = 0
    for note in notes_raw:
        raw_start_beat = note.start_tick / data.ppq
        raw_duration_beat = note.duration_tick / data.ppq
        start_beat, outlier = quantize_beat(raw_start_beat, snapper, quant)
        outliers += 1 if outlier else 0
        quantized.append({
            "note": note,
            "start_beat": start_beat,
            "duration_beat": max(0.0, raw_duration_beat),
            "outlier": outlier,
            "offset": raw_start_beat - start_beat,
        })
    quantized.sort(key=lambda q: (q["start_beat"], q["note"].pitch))
    if quant.get("preserveExpressiveTiming"):
        warnings.append("量化：preserveExpressiveTiming=true，保留原始 onset（未吸附网格）")
    elif outliers:
        warnings.append("量化：%d/%d 个音符偏离网格超过容差 %.3f 拍（保留，仅标记 outlier）"
                        % (outliers, len(quantized),
                           float(quant.get("timingTolerance", 0.09))))

    # 2) 事件分组（和弦聚合）
    chord_tol = float(inst_profile.get("chordGroupingToleranceBeats", 0.03))
    mono = not bool(inst_profile.get("polyphonic", False))
    groups: List[List[Dict[str, Any]]] = []
    for item in quantized:
        if groups:
            group = groups[-1]
            anchor = group[0]["start_beat"]
            same_onset = abs(item["start_beat"] - anchor) <= chord_tol
            overlaps = any(g["start_beat"] + max(g["duration_beat"], 0.05) >
                           item["start_beat"] + chord_tol for g in group)
            if same_onset and not (mono and overlaps):
                group.append(item)
                continue
        groups.append([item])

    events: List[MusicalEvent] = []
    order = profiles.chord.get("keepPitchOrder", "highest_first")
    for index, group in enumerate(groups):
        pitches = [g["note"].pitch for g in group]
        velocities = [g["note"].velocity for g in group]
        distinct = sorted(set(pitches), reverse=(order == "highest_first"))
        representative = distinct[0]
        events.append(MusicalEvent(
            id=index,
            start_beat=group[0]["start_beat"],
            duration_beat=max(g["duration_beat"] for g in group),
            pitches=pitches,
            velocities=velocities,
            sources=[g["note"] for g in group],
            representative_pitch=representative,
            average_velocity=sum(velocities) / len(velocities),
            is_chord=len(distinct) > 1,
            note_count=len(distinct),
            source_track=group[0]["note"].track,
            instrument=ir_instrument,
            timing_outlier=any(g["outlier"] for g in group),
            quantize_offset=group[0]["offset"],
        ))

    # 3) 乐句边界 + 旋律方向 + 重要度
    _analyze_phrases(events, profiles.importance)
    _analyze_contour(events)
    _score_importance(events, profiles.importance, tsmap)

    # 4) 难度处理（只决定保留谁）
    removals: List[Dict[str, Any]] = []
    all_pitches = [n.pitch for n in notes_raw]
    pitch_range = (min(all_pitches), max(all_pitches)) if all_pitches else (60, 60)
    _apply_min_subdivision(events, diff, snapper, removals)
    if kind == "drums":
        _apply_drum_difficulty(events, profiles, difficulty, removals)
    _apply_ornament_filter(events, diff, removals)
    _apply_syncopation(events, diff, removals)
    _apply_chord_limit(events, diff, profiles, pitch_range, removals)
    if kind != "drums":
        # 鼓件重复（16 分踩镲等）是鼓谱语义，不做旋律式同音连打减音
        _apply_repeated_notes(events, diff, removals)
    _apply_density(events, diff, tempo, removals)
    events = [e for e in events if e.keep]

    # 5) Tap / Hold 决策（需求 §十三）
    _apply_hold_policy(events, diff, inst_profile, kind)

    # 6) 发射 IR 事件（和弦按 ChordPolicy 压成 1~N 个）
    ir_events = _emit_ir_events(events, tempo)

    source_name = data_source_name(source)
    metadata = {
        "songName": title or os.path.splitext(os.path.basename(source_name))[0],
        "songArtist": "",
        "chartAuthor": "MidiConverter",
        "difficulty": DIFFICULTY_ORDER.index(difficulty),
        "level": 1 + DIFFICULTY_ORDER.index(difficulty) * 3,
        "bpm": round(tempo.bpm_at_beat(0.0), 3),
        "offset": round(offset, 3),
        "audioFileName": audio or "",
        "previewStartTime": 0.0,
        "previewDuration": 10.0,
        "sourceMidi": os.path.basename(source_name),   # 谱面元数据不带本机绝对路径
        "instrumentType": kind,
        "difficultyName": difficulty,
    }
    ir = IRStream(events=ir_events, metadata=metadata,
                  events_channel=tempo.tempo_events()).sorted()

    report = {
        "source": source_name,
        "instrument": kind,
        "difficulty": difficulty,
        "ppq": data.ppq,
        "tempoEvents": len(data.tempos),
        "timeSignatures": [(round(s.tick / data.ppq, 3), s.numerator, s.denominator)
                           for s in data.time_signatures],
        "quantize": {"mode": quant.get("mode"), "minDivision": quant.get("minDivision"),
                     "outliers": outliers,
                     "preserveExpressiveTiming": bool(quant.get("preserveExpressiveTiming"))},
        "selection": selection_log,
        "totalMusicalEvents": len(groups),
        "keptMusicalEvents": len(events),
        "removals": removals,
        "holdPolicy": {
            "holds": sum(1 for e in events if e.note_kind == "hold"),
            "taps": sum(1 for e in events if e.note_kind == "tap"),
            "clamped": [{"eventId": e.id, "beat": round(e.start_beat, 4),
                         "detail": e.hold_detail} for e in events if e.hold_detail],
        },
        "events": [e.to_dict(tempo) for e in events],
        "warnings": warnings,
    }
    return MidiConvertResult(ir=ir, events=events, report=report)


def data_source_name(source) -> str:
    return source if isinstance(source, str) else getattr(source, "name", "<memory>")


def _analyze_phrases(events: List[MusicalEvent], importance_profile: Dict[str, Any]) -> None:
    gap = float(importance_profile.get("phraseGapBeats", 1.0))
    for index, event in enumerate(events):
        prev = events[index - 1] if index > 0 else None
        nxt = events[index + 1] if index + 1 < len(events) else None
        event.is_phrase_start = prev is None or (event.start_beat - prev.start_beat) >= gap
        event.is_phrase_end = nxt is None or (nxt.start_beat - event.start_beat) >= gap


def _analyze_contour(events: List[MusicalEvent]) -> None:
    previous_pitch: Optional[int] = None
    for event in events:
        pitch = event.representative_pitch
        if previous_pitch is None or event.is_phrase_start:
            event.melodic_direction = 0
        elif pitch > previous_pitch:
            event.melodic_direction = 1
        elif pitch < previous_pitch:
            event.melodic_direction = -1
        else:
            event.melodic_direction = 0
        previous_pitch = pitch


def _score_importance(events: List[MusicalEvent], profile: Dict[str, Any],
                      tsmap: TimeSignatureMap) -> None:
    weights = {
        "beatStrengthWeight": float(profile.get("beatStrengthWeight", 0.30)),
        "velocityWeight": float(profile.get("velocityWeight", 0.20)),
        "melodicWeight": float(profile.get("melodicWeight", 0.20)),
        "phraseBoundaryWeight": float(profile.get("phraseBoundaryWeight", 0.15)),
        "durationWeight": float(profile.get("durationWeight", 0.15)),
    }
    total = sum(weights.values()) or 1.0
    table = profile.get("beatStrengthTable", {}) or {}
    fallback = float(profile.get("beatStrengthFallback", 0.12))

    for index, event in enumerate(events):
        event.beat_strength = tsmap.beat_strength(event.start_beat, table, fallback)
        velocity = min(1.0, event.average_velocity / 127.0)
        duration = min(1.0, event.duration_beat / 4.0)
        prev = events[index - 1] if index > 0 else None
        leap = 0.0
        if prev is not None and not event.is_phrase_start:
            leap = min(1.0, abs(event.representative_pitch - prev.representative_pitch) / 12.0)
        melodic = min(1.0, leap + (0.35 if event.is_chord else 0.0))
        phrase = 1.0 if (event.is_phrase_start or event.is_phrase_end) else 0.0
        event.importance = round(
            (weights["beatStrengthWeight"] * event.beat_strength
             + weights["velocityWeight"] * velocity
             + weights["melodicWeight"] * melodic
             + weights["phraseBoundaryWeight"] * phrase
             + weights["durationWeight"] * duration) / total, 6)


def _remove(event: MusicalEvent, reason: str, detail: str,
            removals: List[Dict[str, Any]]) -> None:
    if not event.keep:
        return
    event.keep = False
    event.removal_reason = reason
    event.removal_detail = detail
    removals.append({"eventId": event.id, "beat": round(event.start_beat, 4),
                     "pitch": event.representative_pitch, "reason": reason, "detail": detail,
                     "importance": round(event.importance, 4)})


def _apply_min_subdivision(events: List[MusicalEvent], diff: Dict[str, Any],
                           snapper: GridSnapper, removals: List[Dict[str, Any]]) -> None:
    """难度网格粗化：吸附到该难度允许的网格（straight + 三连音族并集），
    同格碰撞留高重要度（需求 §八）。

    minSubdivision <= 0 表示不粗化（Expert：直接用量化网格）。
    网格定义：division = 每四分音符的等分数（16 = 十六分，24 = 十六分三连音）。
    """
    division = int(diff.get("minSubdivision", 0) or 0)
    if division <= 0:
        return
    triplet_sibling = division + division // 2        # 16 → 24（十六分三连音）
    grid = MultiGridSnapper([division, triplet_sibling], snapper.mode, snapper.swing_ratio)
    by_slot: Dict[int, MusicalEvent] = {}
    for event in events:
        if not event.keep:
            continue
        snapped = grid.snap(event.start_beat)
        slot = int(round(snapped * 96))               # 1/96 拍精度的稳定 slot 键
        if abs(snapped - event.start_beat) > 1e-9:
            event.quantize_offset = event.start_beat - snapped
            event.start_beat = snapped
        rival = by_slot.get(slot)
        if rival is None:
            by_slot[slot] = event
        elif event.importance > rival.importance:
            _remove(rival, "MinSubdivision",
                    "与更重要的 Event 落在同一 1/%d 网格（含三连音族）" % division, removals)
            by_slot[slot] = event
        else:
            _remove(event, "MinSubdivision",
                    "与更重要的 Event 落在同一 1/%d 网格（含三连音族）" % division, removals)


def _apply_drum_difficulty(events: List[MusicalEvent], profiles: Profiles, difficulty: str,
                           removals: List[Dict[str, Any]]) -> None:
    """鼓：按鼓件类别保留（Easy 只留 kick/snare/crash…）（需求 §十四）。"""
    allowed = set(profiles.drum_keep_classes(difficulty))
    for event in events:
        if not event.keep:
            continue
        classes = {profiles.drum_class(pitch) for pitch in set(event.pitches)}
        if not (classes & allowed):
            _remove(event, "DrumPieceFilter",
                    "鼓件 %s 不在该难度保留列表 %s" % (sorted(classes), sorted(allowed)),
                    removals)


def _apply_ornament_filter(events: List[MusicalEvent], diff: Dict[str, Any],
                           removals: List[Dict[str, Any]]) -> None:
    """装饰音 / ghost / 极弱短音过滤（需求 §七 优先删除项）。"""
    if diff.get("keepOrnaments") and diff.get("keepGhostNotes"):
        return
    for event in events:
        if not event.keep:
            continue
        weak_short = event.duration_beat <= 0.25 and event.average_velocity <= 45
        if weak_short and not event.is_phrase_start and not event.is_phrase_end \
                and not diff.get("keepGhostNotes"):
            _remove(event, "Ornament", "极弱短音（vel %.0f, %.2f 拍）"
                    % (event.average_velocity, event.duration_beat), removals)


def _apply_syncopation(events: List[MusicalEvent], diff: Dict[str, Any],
                       removals: List[Dict[str, Any]]) -> None:
    """SimplifySyncopation：反拍弱音被邻近正拍强音吸收（需求 §八）。"""
    if not diff.get("simplifySyncopation"):
        return
    for index, event in enumerate(events):
        if not event.keep or event.beat_strength >= 0.4 or event.is_phrase_start:
            continue
        neighbours = [events[index - 1] if index > 0 else None,
                      events[index + 1] if index + 1 < len(events) else None]
        for other in neighbours:
            if other is None or not other.keep:
                continue
            if abs(other.start_beat - event.start_beat) <= 0.3 \
                    and other.beat_strength > event.beat_strength:
                _remove(event, "Syncopation",
                        "反拍弱音被 %.2f 拍处的正拍音替代" % other.start_beat, removals)
                break


def _normalized_lane(pitch: int, pitch_range: Tuple[int, int]) -> int:
    """乐句音域归一化 → 轨位（需求 §十 relative pitch；5 键 → 0..4）。"""
    low, high = pitch_range
    if high <= low:
        return 2
    return max(0, min(4, int(round((pitch - low) / (high - low) * 4))))


def _apply_chord_limit(events: List[MusicalEvent], diff: Dict[str, Any], profiles: Profiles,
                       pitch_range: Tuple[int, int], removals: List[Dict[str, Any]]) -> None:
    """和弦压缩：1 Chord attack → 1~N 个音（需求 §十二）。"""
    max_size = int(diff.get("maxChordSize", 1))
    order = profiles.chord.get("keepPitchOrder", "highest_first")
    style = str(diff.get("strumStyle", "tap"))
    for event in events:
        if not event.keep:
            continue
        pitches = sorted(set(event.pitches), reverse=(order == "highest_first"))
        keep = pitches[:max_size]
        if len(pitches) > max_size:
            removals.append({
                "eventId": event.id, "beat": round(event.start_beat, 4),
                "pitch": event.representative_pitch, "reason": "MaxChordSize",
                "detail": "和弦 %d 音 → %d 音（保留 %s）" % (len(pitches), max_size, keep),
                "importance": round(event.importance, 4),
            })
        event.keep_pitches = keep
        event.representative_pitch = keep[0] if keep else event.representative_pitch
        if (event.is_chord and event.instrument == "guitar" and style != "tap"
                and event.duration_beat < 1.0):
            event.strum = True
            event.strum_pitches = (pitches if style == "drag_full" else keep)[:4]
            event.strum_lanes = [_normalized_lane(p, pitch_range) for p in event.strum_pitches]


def _apply_repeated_notes(events: List[MusicalEvent], diff: Dict[str, Any],
                          removals: List[Dict[str, Any]]) -> None:
    """MaxRepeatedNotes：同一音高的连打上限（需求 §八 / Bass jack 来源之一）。"""
    limit = int(diff.get("maxRepeatedNotes", 2))
    if limit <= 0:
        return
    run_pitch: Optional[int] = None
    run_count = 0
    for event in events:
        if not event.keep or event.is_chord:
            run_pitch, run_count = None, 0
            continue
        if event.representative_pitch == run_pitch:
            run_count += 1
            if run_count > limit:
                _remove(event, "RepeatedNote", "同音 %d 连打超过上限 %d"
                        % (event.representative_pitch, limit), removals)
        else:
            run_pitch, run_count = event.representative_pitch, 1


def _apply_density(events: List[MusicalEvent], diff: Dict[str, Any],
                   tempo: TempoMap, removals: List[Dict[str, Any]]) -> None:
    """MaxNotesPerSecond：滑窗超限则丢窗口内重要度最低者（需求 §八）。"""
    strength = min(1.0, max(0.0, float(diff.get("densityReductionStrength", 1.0))))
    base = float(diff.get("maxNotesPerSecond", 8.0))
    if strength <= 0 or base <= 0:
        return
    alive = [e for e in events if e.keep]
    guard = 0
    while alive and guard < 500:
        guard += 1
        times = [(tempo.time_at_beat(e.start_beat), e) for e in alive]
        worst_start, worst_count = None, 0
        for t0, _e in times:
            count = sum(1 for t, _ in times if 0 <= t - t0 < 1.0)
            if count > worst_count:
                worst_start, worst_count = t0, count
        if worst_start is None:
            break
        target = base + (1.0 - strength) * max(0.0, worst_count - base)
        if worst_count <= target:
            break
        window_events = [e for t, e in times if 0 <= t - worst_start < 1.0]
        victim = min(window_events, key=lambda e: (e.importance, -e.start_beat))
        _remove(victim, "DensityLimit",
                "%.2fs 起 1 秒窗口 %d 音 > 上限 %.1f 音/秒"
                % (worst_start, worst_count, base), removals)
        alive = [e for e in alive if e.keep]


def _apply_hold_policy(events: List[MusicalEvent], diff: Dict[str, Any],
                       inst_profile: Dict[str, Any], kind: str) -> None:
    """Tap / Hold 决策（需求 §十三 + 人类谱统计校准）。

    规则（默认沿用 docs/note-types.md 已拍板的"≥2 拍 → hold"）：
      1) duration >= 阈值（holdThresholdBeats；可选 holdTargetShare 按乐句时值分位自适应）；
      2) 与下一 Event 的 start 间隔 >= holdMinGapBeats（避免 Funk 式每音带 sustain 全变长键）；
      3) 长键不能吞掉下一个音：时长裁到 next.start 之前（留 holdReleaseMarginBeats），
         裁完不足 holdMinLengthBeats（人类谱 <1/4 拍的长键很少）则降级 tap。
    裁剪与降级都写进 event.hold_detail，便于溯源。
    """
    if inst_profile.get("hold") is False or kind == "drums":
        for event in events:
            event.note_kind = "tap"
        return
    threshold = float(diff.get("holdThresholdBeats", 2.0))
    target_share = float(diff.get("holdTargetShare", 0.0) or 0.0)
    durations = sorted(e.duration_beat for e in events if e.duration_beat > 0)
    if target_share > 0 and len(durations) >= 4:     # 短 Phrase 也要能自适应，4 个音起算
        index = int(round((1.0 - target_share) * (len(durations) - 1)))
        threshold = max(float(diff.get("holdMinLengthBeats", 0.25)),
                        durations[max(0, min(len(durations) - 1, index))])
    min_gap = float(diff.get("holdMinGapBeats", 1.0))
    min_length = float(diff.get("holdMinLengthBeats", 0.25))
    release_margin = float(diff.get("holdReleaseMarginBeats", 0.05))
    for index, event in enumerate(events):
        nxt = events[index + 1] if index + 1 < len(events) else None
        gap = (nxt.start_beat - event.start_beat) if nxt is not None else float("inf")
        if event.duration_beat < threshold or gap < min_gap:
            event.note_kind = "tap"
            continue
        length = event.duration_beat
        if nxt is not None and length > gap:
            # 只有 sustain 真的盖住下一个音才裁（正好接上下一个 onset 不算重叠）
            length = gap - release_margin
            if length < min_length:
                event.note_kind = "tap"
                event.hold_detail = "sustain 盖住下一个音且剩余 %.2f 拍 < 最短长键 %.2f 拍" \
                    % (length, min_length)
                continue
            event.hold_detail = "长键裁短 %.2f → %.2f 拍（给下一个音让位）" \
                % (event.duration_beat, length)
        event.note_kind = "hold"
        event.duration_beat = round(length, 6)


def _emit_ir_events(events: List[MusicalEvent], tempo: TempoMap) -> List[NoteEvent]:
    out: List[NoteEvent] = []
    for event in events:
        tick = int(round(event.start_beat * IR_PPQ))
        time = round(tempo.time_at_beat(event.start_beat), 6)
        position = {
            "musical_event_id": event.id,
            "importance": round(event.importance, 4),
            "is_chord": event.is_chord,
            "chord_size": event.note_count,
            "chord_pitches": list(event.pitches),
            "beat_strength": round(event.beat_strength, 4),
            "melodic_direction": event.melodic_direction,
            "quantize_offset_beats": round(event.quantize_offset, 6),
            "timing_outlier": event.timing_outlier,
            "source_track": event.source_track,
            "source_notes": [
                {"track": n.track, "channel": n.channel, "pitch": n.pitch,
                 "start_tick": n.start_tick, "end_tick": n.end_tick,
                 "velocity": n.velocity, "program": n.program}
                for n in event.sources
            ],
        }
        if event.strum:
            strum_position = dict(position)
            strum_position["strum_pitches"] = list(event.strum_pitches)
            strum_position["strum_lanes"] = list(event.strum_lanes)
            out.append(NoteEvent(
                tick=tick, time=time, pitch=event.strum_pitches[-1],
                duration=0.05, duration_ticks=0,
                velocity=round(event.average_velocity / 127.0, 4),
                instrument=event.instrument, staff=event.source_track,
                articulations=["strum"], position=strum_position))
            continue
        if event.note_kind == "hold":
            duration_seconds = event.duration_beat * tempo.seconds_per_beat(event.start_beat)
            duration_ticks = int(round(event.duration_beat * IR_PPQ))
        else:
            duration_seconds, duration_ticks = 0.05, 0
        for index, pitch in enumerate(event.keep_pitches or [event.representative_pitch]):
            child = dict(position)
            child["chord_keep_index"] = index
            out.append(NoteEvent(
                tick=tick, time=time, pitch=pitch,
                duration=round(duration_seconds, 6), duration_ticks=duration_ticks,
                velocity=round(event.average_velocity / 127.0, 4),
                instrument=event.instrument, staff=event.source_track,
                tied=(event.note_kind == "hold"), position=child))
    out.sort(key=lambda e: (e.tick, e.pitch))
    return out


# ---------------------------------------------------------------- CLI

def main(argv: Optional[List[str]] = None) -> int:
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = list(argv if argv is not None else sys.argv[1:])
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    source = args[0]
    opts = {"instrument": "auto", "difficulty": "Normal", "out": None, "debug": None,
            "profile": None, "track": None, "title": None, "audio": None, "offset": 0.0}
    index = 1
    flags = {"--instrument": "instrument", "--difficulty": "difficulty", "--out": "out",
             "--debug": "debug", "--profile": "profile", "--track": "track",
             "--title": "title", "--audio": "audio", "--offset": "offset"}
    while index < len(args):
        key = args[index]
        field_name = flags.get(key)
        if field_name and index + 1 < len(args):
            value = args[index + 1]
            if field_name in ("track",):
                opts[field_name] = int(value)
            elif field_name in ("offset",):
                opts[field_name] = float(value)
            else:
                opts[field_name] = value
            index += 2
        else:
            index += 1

    try:
        result = convert_midi(source, opts["instrument"], opts["difficulty"],
                              profile_path=opts["profile"], track=opts["track"],
                              title=opts["title"], audio=opts["audio"], offset=opts["offset"])
    except (MidiParseError, ValueError) as error:
        print("✗ 转换失败：%s" % error)
        return 1

    out_path = opts["out"] or os.path.splitext(source)[0] + ".ir.json"
    from ir import dump_json
    dump_json(result.ir, out_path)
    print("IR 已导出 %s：IR 事件 %d（音乐事件 %d，删减 %d）"
          % (out_path, len(result.ir.events), result.report["totalMusicalEvents"],
             len(result.report["removals"])))
    if opts["debug"]:
        with open(opts["debug"], "w", encoding="utf-8") as handle:
            json.dump(result.report, handle, ensure_ascii=False, indent=1)
        print("调试报告已导出 %s" % opts["debug"])
    for warning in result.warnings:
        print("  ⚠ " + warning)
    return 0


if __name__ == "__main__":
    sys.exit(main())
