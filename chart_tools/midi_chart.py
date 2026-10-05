# -*- coding: utf-8 -*-
"""MIDI → FallenAngel Chart v2 JSON 一键转换（需求 §十七 / §十九 / §二十）。

复用现有管线，不重造：
    midi_to_ir.py（MIDI 前端：解析/量化/分组/重要度/难度）
      → rule_engine.py + rules/*.json（乐器映射：Lead/Bass/Guitar/Drums）
      → playability.py（可玩性校验 + 带原因的自动修复）
      → fix_charts.apply_guardrails（既有导入护栏：去重/两指化/长按裁剪）
      → audit_charts.audit（写盘后体检）

用法（单文件）:
  python -X utf8 chart_tools/midi_chart.py song.mid --instrument bass --difficulty Hard \
      --out Assets/Resources/Charts/song_Bass_Hard.json --audio song --debug Logs/song_debug.json

用法（批处理 / 一次出多难度；核心逻辑不依赖 Editor）:
  python -X utf8 chart_tools/midi_chart.py "Input/Bass/*.mid" --instrument bass \
      --difficulties Easy,Normal,Hard,Expert --out-dir Assets/Resources/Charts \
      --debug-dir Logs/midi_debug

确定性：同输入 + 同参数 = 同输出（可回归、可批处理）。
"""
import glob
import json
import os
import sys
from typing import Any, Dict, List, Optional, Tuple

import fix_charts
import audit_charts
import playability
from ir import NoteEvent
from midi_parse import MidiParseError, MidiFileData, parse_midi
from midi_to_ir import (DIFFICULTY_ORDER, Profiles, convert_midi, load_profiles)
from rule_engine import RuleEngine, build_chart, load_rules

DEFAULT_RULES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "rules", "default.json")
DEFAULT_OUT_DIR = os.path.join("Assets", "Resources", "Charts")
INSTRUMENT_LABEL = {"lead": "Lead", "bass": "Bass", "guitar": "Guitar",
                    "keys": "Keys", "drums": "Drums"}


def instrument_label(kind: str) -> str:
    return INSTRUMENT_LABEL.get(kind, kind.capitalize())


def chart_file_name(source: str, kind: str, difficulty: str,
                    name: Optional[str] = None) -> str:
    if name:
        return name if name.endswith(".json") else name + ".json"
    stem = os.path.splitext(os.path.basename(source))[0]
    return "%s_%s_%s.json" % (stem, instrument_label(kind), difficulty)


def generate_chart(source, instrument: str = "auto", difficulty: str = "Normal",
                   rules_path: Optional[str] = None, profile_path: Optional[str] = None,
                   track: Optional[int] = None, audio: Optional[str] = None,
                   title: Optional[str] = None, offset: float = 0.0,
                   max_hold: float = 4.0, use_guardrails: bool = True,
                   parsed: Optional[MidiFileData] = None) -> Dict[str, Any]:
    """核心转换（单 MIDI × 单难度），返回 {chart, debug, summary}。可被批处理直接调用。"""
    conv = convert_midi(parsed if parsed is not None else source, instrument, difficulty,
                        profile_path=profile_path, track=track, title=title,
                        audio=audio, offset=offset)
    engine = RuleEngine(load_rules(rules_path or DEFAULT_RULES))
    trace: List[Tuple[NoteEvent, List[Dict[str, Any]]]] = []
    notes, engine_warnings = engine.apply(conv.ir, trace)
    chart = build_chart(conv.ir, notes)

    profiles = Profiles(load_profiles(profile_path))
    play_profile = dict(profiles.playability)
    play_profile["bpm"] = float(chart["metadata"].get("bpm") or 120.0)
    # 密度上限以该难度自己的 MaxNotesPerSecond 为准（结构类阈值仍取全局 playability）
    play_profile["maxNPS"] = float(profiles.difficulty_profile(difficulty)
                                   .get("maxNotesPerSecond", play_profile.get("maxNPS", 10.0)))
    if conv.report["instrument"] == "drums":
        # 鼓件跨轨（kick→tom 等）是鼓谱本身的语义；同轨连打（16 分踩镲）也是鼓谱语义，
        # 因此鼓不做旋律式的跳轨上限与 jack 改轨
        play_profile["maxLaneJump"] = 99
        play_profile["maxJackCount"] = 999
        play_profile["minSameLaneIntervalBeats"] = 0.25

    repaired, changes = playability.repair(chart["notes"], play_profile)
    chart["notes"] = sorted(repaired, key=lambda n: (n.get("time", 0.0), n.get("lane", 0),
                                                     n.get("type", "")))
    guard_lines: List[str] = []
    if use_guardrails:
        guard_lines = fix_charts.apply_guardrails(chart, max_hold)
        chart["notes"] = sorted(chart["notes"], key=lambda n: (n.get("time", 0.0),
                                                              n.get("lane", 0),
                                                              n.get("type", "")))

    counts: Dict[str, int] = {}
    for note in chart["notes"]:
        counts[note["type"]] = counts.get(note["type"], 0) + 1
    chart["metadata"]["noteCounts"] = counts
    final_ids = {id(note) for note in chart["notes"]}

    validation = playability.validate(chart["notes"], play_profile)
    trace_rows = _build_trace(trace, conv, final_ids)
    debug = {
        "source": conv.report["source"],
        "instrument": conv.report["instrument"],
        "difficulty": difficulty,
        "rules": rules_path or DEFAULT_RULES,
        "metadata": chart["metadata"],
        "noteCounts": counts,
        "stages": {
            "midiNotes": sum(e["noteCount"] for e in conv.report["events"]) +
                         len(conv.report["removals"]),
            "musicalEvents": conv.report["totalMusicalEvents"],
            "keptMusicalEvents": conv.report["keptMusicalEvents"],
            "irEvents": len(conv.ir.events),
            "chartNotes": len(chart["notes"]),
        },
        "musicalEvents": conv.report["events"],
        "removals": conv.report["removals"],
        "trace": trace_rows,
        "playability": {"issues": validation.issues, "stats": validation.stats,
                        "changes": [c.to_dict() for c in changes]},
        "guardrails": guard_lines,
        "warnings": list(conv.warnings) + list(engine_warnings),
    }
    summary = {
        "source": conv.report["source"],
        "instrument": conv.report["instrument"],
        "difficulty": difficulty,
        "notes": len(chart["notes"]),
        "noteCounts": counts,
        "peakNPS": validation.stats.get("peakNPS"),
        "issues": validation.issues,
        "changes": len(changes) + len(guard_lines),
        "removals": len(conv.report["removals"]),
        "bpm": chart["metadata"].get("bpm"),
    }
    return {"chart": chart, "debug": debug, "summary": summary}


def _build_trace(trace, conv, final_ids) -> List[Dict[str, Any]]:
    """Chart Note → Musical Event → 原始 MIDI 音符 的溯源表（需求 §二十）。"""
    rows: List[Dict[str, Any]] = []
    for index, (event, emitted) in enumerate(trace):
        position = event.position or {}
        midi_ids = ["t%d:c%d:p%d:%d" % (n["track"], n["channel"], n["pitch"], n["start_tick"])
                    for n in position.get("source_notes", [])]
        for note in emitted:
            rows.append({
                "chartNoteIndex": index,
                "type": note.get("type"),
                "time": note.get("time"),
                "lane": note.get("lane"),
                "duration": note.get("duration", 0.0),
                "beat": round(event.tick / 768.0, 4),
                "pitch": event.pitch,
                "velocity": round(event.velocity * 127),
                "instrument": event.instrument,
                "musicalEventId": position.get("musical_event_id"),
                "importance": position.get("importance"),
                "isChord": position.get("is_chord"),
                "chordSize": position.get("chord_size"),
                "keptInChart": id(note) in final_ids,
                "midiIds": midi_ids,
            })
    return rows


def _audit_file(path: str, max_hold: float) -> List[str]:
    try:
        issues, _count = audit_charts.audit(path, max_hold)
        return issues
    except Exception as error:                       # noqa: BLE001 - 体检失败不应中断生成
        return ["体检异常：%s" % error]


def expand_sources(pattern: str) -> List[str]:
    if os.path.isdir(pattern):
        found = sorted(glob.glob(os.path.join(pattern, "**", "*.mid"), recursive=True)
                       + glob.glob(os.path.join(pattern, "**", "*.midi"), recursive=True))
        return found
    if any(char in pattern for char in "*?["):
        return sorted(glob.glob(pattern, recursive=True))
    return [pattern]


def run_batch(pattern: str, instrument: str, difficulties: List[str],
              out_dir: Optional[str], debug_dir: Optional[str],
              rules_path: Optional[str], profile_path: Optional[str],
              audio: Optional[str], title: Optional[str], offset: float,
              max_hold: float, use_guardrails: bool, name: Optional[str],
              single_out: Optional[str] = None) -> int:
    sources = expand_sources(pattern)
    if not sources:
        print("✗ 没有匹配到 MIDI 文件：%s" % pattern)
        return 1
    failures = 0
    issue_total = 0
    for source in sources:
        try:
            parsed = parse_midi(source)
        except (MidiParseError, OSError) as error:
            print("✗ %s 解析失败：%s" % (os.path.basename(source), error))
            failures += 1
            continue
        for difficulty in difficulties:
            result = generate_chart(source, instrument, difficulty,
                                    rules_path=rules_path, profile_path=profile_path,
                                    audio=audio, title=title, offset=offset,
                                    max_hold=max_hold, use_guardrails=use_guardrails,
                                    parsed=parsed)
            file_name = chart_file_name(source, result["summary"]["instrument"], difficulty,
                                        name if len(sources) == 1 and len(difficulties) == 1
                                        else None)
            out_path = single_out or os.path.join(out_dir or DEFAULT_OUT_DIR, file_name)
            os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
            with open(out_path, "w", encoding="utf-8") as handle:
                json.dump(result["chart"], handle, ensure_ascii=False, indent=1)

            audit_issues = _audit_file(out_path, max_hold)
            result["debug"]["audit"] = audit_issues
            if debug_dir:
                os.makedirs(debug_dir, exist_ok=True)
                debug_path = os.path.join(debug_dir,
                                          os.path.splitext(os.path.basename(out_path))[0] + ".debug.json")
                with open(debug_path, "w", encoding="utf-8") as handle:
                    json.dump(result["debug"], handle, ensure_ascii=False, indent=1)

            summary = result["summary"]
            print("✓ %s（%s %s）→ %s" % (summary["source"], summary["instrument"],
                                         difficulty, out_path))
            counts_text = " / ".join("%s %d" % (key, value)
                                     for key, value in sorted(summary["noteCounts"].items()))
            print("  音符 %d（%s）峰值 %.0f 音/秒；删减 %d，修复 %d；体检问题 %d"
                  % (summary["notes"], counts_text, summary["peakNPS"] or 0,
                     summary["removals"], summary["changes"],
                     len(audit_issues) + len(summary["issues"])))
            for issue in audit_issues:
                print("    ⚠ " + issue)
            for issue in summary["issues"]:
                print("    ⚠ 可玩性：%s" % issue["detail"])
            for warning in result["debug"]["warnings"]:
                print("    · " + warning)
            issue_total += len(audit_issues) + len(summary["issues"])
    return 1 if (failures or issue_total) else 0


def main(argv: Optional[List[str]] = None) -> int:
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = list(argv if argv is not None else sys.argv[1:])
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    pattern = args[0]
    opts: Dict[str, Any] = {"instrument": "auto", "difficulties": ["Normal"], "out": None,
                            "out_dir": DEFAULT_OUT_DIR, "debug_dir": None, "rules": None,
                            "profile": None, "audio": None, "title": None, "name": None,
                            "offset": 0.0, "max_hold": 4.0, "no_guardrails": False}
    flags = {"--instrument": "instrument", "--difficulty": "difficulties",
             "--difficulties": "difficulties", "--out": "out", "--out-dir": "out_dir",
             "--debug": "debug_dir", "--debug-dir": "debug_dir", "--rules": "rules",
             "--profile": "profile", "--audio": "audio", "--title": "title",
             "--name": "name", "--offset": "offset", "--max-hold": "max_hold"}
    index = 1
    while index < len(args):
        key = args[index]
        if key == "--no-guardrails":
            opts["no_guardrails"] = True
            index += 1
            continue
        field_name = flags.get(key)
        if field_name and index + 1 < len(args):
            value = args[index + 1]
            if field_name == "difficulties":
                opts[field_name] = [v.strip() for v in value.split(",") if v.strip()]
            elif field_name in ("offset", "max_hold"):
                opts[field_name] = float(value)
            else:
                opts[field_name] = value
            index += 2
        else:
            index += 1

    for difficulty in opts["difficulties"]:
        if difficulty not in DIFFICULTY_ORDER:
            print("✗ 未知难度 %s（可选 %s）" % (difficulty, list(DIFFICULTY_ORDER)))
            return 1
    return run_batch(pattern, opts["instrument"], opts["difficulties"], opts["out_dir"],
                     opts["debug_dir"], opts["rules"], opts["profile"], opts["audio"],
                     opts["title"], opts["offset"], opts["max_hold"],
                     not opts["no_guardrails"], opts["name"], opts["out"])


if __name__ == "__main__":
    sys.exit(main())
