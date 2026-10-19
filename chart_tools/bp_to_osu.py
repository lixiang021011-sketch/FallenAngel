# -*- coding: utf-8 -*-
"""
Basic Pitch note events → 5K mania `.osu`（FallenAngel 生成器插槽的规则线）

位置：它是「生成器」这一格，产物是 `out/*.osu`，交给 Adapter 落盘。
      不写 ChartData、不碰 chart_import、不自己算节拍（网格由 Song Timing 提供，按参数传入）。

复用而不是重造：
    rule_engine（相对音高 → 轨道，相邻 ±1 列；rules/default.json 里的 bass 规则）
    playability（可玩性校验 + 带原因的自动修复）
    fix_charts.apply_guardrails（去重 / 两指化 / 长按裁剪）

用法：
  python bp_to_osu.py --events <events.json|basic_pitch.csv> --name light \
      --bpm 132.0 --first-beat-ms -1208 --out out/light_bass_bp.osu
  # 网格也可以直接吃 Song Timing 的产物：
  python bp_to_osu.py --events ... --timing timing.json --out ...
"""

import argparse
import csv
import hashlib
import json
import math
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import fix_charts  # noqa: E402
import playability  # noqa: E402
from ir import IRStream, NoteEvent  # noqa: E402
from midi_to_ir import Profiles, load_profiles  # noqa: E402
from rule_engine import RuleEngine, build_chart, load_rules  # noqa: E402

IR_PPQ = 768
DEFAULT_RULES = pathlib.Path(__file__).resolve().parent / "rules" / "default.json"
TOOL_VERSION = 1          # bp_to_osu 自身的口径版本；改了量化/映射规则就 +1
BP_VERSION = "0.4.0"      # 生成 note events 的 Basic Pitch 版本

# .osu mania 只认 tap(1) 和 hold(128)；其余类型在这里降级并记账。
TYPE_TO_OSU = {"tap": "tap", "drag": "tap", "flick": "tap", "slide": "tap", "hold": "hold"}


# ---------------------------------------------------------------- 输入


def sha256_of(path: pathlib.Path, chunk=1 << 20) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        while True:
            b = fh.read(chunk)
            if not b:
                break
            h.update(b)
    return h.hexdigest()


def load_events(path: pathlib.Path):
    """吃两种输入：bp_verify 导出的 events.json，或 basic-pitch 原始 csv。"""
    if path.suffix.lower() == ".json":
        data = json.loads(path.read_text(encoding="utf-8"))
        out = [
            dict(_idx=i, start=float(e["startTime"]), end=float(e["endTime"]), pitch=int(e["pitch"]),
                 onset=float(e.get("onsetStrength", 1.0)), amp=float(e.get("amplitude", 0.5)))
            for i, e in enumerate(data)
        ]
        return sorted(out, key=lambda e: e["start"])
    out = []
    with open(path, newline="", encoding="utf-8") as fh:
        reader = csv.reader(fh)
        next(reader)
        for i, row in enumerate(reader):
            if not row or not row[0]:
                continue
            out.append(dict(_idx=i, start=float(row[0]), end=float(row[1]), pitch=int(row[2]),
                            onset=1.0, amp=int(row[3]) / 127.0))
    return sorted(out, key=lambda e: e["start"])


def load_grid(args):
    """网格 = (bpm, first_beat_s)。Song Timing 落地前先用 ref.osu 那两个数。"""
    bpm, first = args.bpm, args.first_beat_ms
    if args.timing:
        t = json.loads(pathlib.Path(args.timing).read_text(encoding="utf-8"))
        beat_ms = t.get("beatLengthMs")
        bpm = t.get("bpm") or (60000.0 / beat_ms if beat_ms else None)
        first = t.get("latticeOffsetMs", t.get("firstBeatMs", first))
    if not bpm:
        raise SystemExit("缺 BPM：用 --bpm 或 --timing")
    return float(bpm), float(first or 0.0) / 1000.0, (args.timing or "cli")


# ---------------------------------------------------------------- 量化


def to_ir(events, bpm, first_beat, args, report):
    beat_s = 60.0 / bpm
    step_ticks = IR_PPQ // args.subdivision
    tol_ticks = step_ticks * args.snap_tolerance
    ir = IRStream()
    kept = {}
    src_of = {}      # (tick, pitch) → 源 BP 事件，逐音追溯用
    drops = []

    def drop(e, reason, detail):
        drops.append(dict(sourceIndex=e["_idx"], sourceTime=round(e["start"], 3),
                          pitch=e["pitch"], reason=reason, detail=detail))

    for e in events:
        if not (args.pitch_min <= e["pitch"] <= args.pitch_max):
            drop(e, "out_of_range", f"音高 {e['pitch']} 超出 Bass 音域 [{args.pitch_min},{args.pitch_max}]")
            continue
        if e["onset"] < args.min_onset:
            drop(e, "weak_onset", f"起音强度 {e['onset']:.2f} < {args.min_onset}")
            continue
        raw_tick = (e["start"] - first_beat) / beat_s * IR_PPQ
        if raw_tick < -tol_ticks:
            drop(e, "before_grid", f"在网格起点之前（{raw_tick:.0f} tick）")
            continue
        snapped = int(round(raw_tick / step_ticks)) * step_ticks
        if abs(raw_tick - snapped) > tol_ticks:
            off_ms = (raw_tick - snapped) / IR_PPQ * beat_s * 1000.0
            drop(e, "unsnapped",
                 f"离最近的 {args.subdivision} 分格 {off_ms:+.0f}ms（容差 ±{tol_ticks / IR_PPQ * beat_s * 1000:.0f}ms）")
            continue
        snapped = max(0, snapped)
        key = (snapped, e["pitch"])
        if key in kept:
            if e["amp"] <= kept[key]["amp"]:
                drop(e, "duplicate", "同格同音高，音量更低被丢")
                continue
            drop(kept[key], "duplicate", "同格同音高，音量更低被丢")
        kept[key] = e

    for (tick, pitch), e in sorted(kept.items(), key=lambda kv: (kv[0][0], kv[0][1])):
        time_s = first_beat + tick / IR_PPQ * beat_s
        dur_beats = max(0.0, (e["end"] - e["start"]) / beat_s)
        src_of[(tick, pitch)] = e
        ir.events.append(NoteEvent(
            tick=tick,
            time=time_s,
            pitch=pitch,
            duration=e["end"] - e["start"],
            duration_ticks=int(round(dur_beats * IR_PPQ)),
            velocity=float(e["amp"]),
            instrument=args.instrument,
        ))
    ir.sorted()
    counts = {}
    for d in drops:
        counts[d["reason"]] = counts.get(d["reason"], 0) + 1
    report["stages"]["quantize"] = dict(input=len(events), kept=len(ir.events), dropped=counts)
    report["dropped"] = sorted(drops, key=lambda d: d["sourceTime"])
    return ir, src_of


# ---------------------------------------------------------------- 出谱


def to_notes(ir, src_of, args, report):
    engine = RuleEngine(load_rules(str(args.rules)))
    trace = []
    notes, warnings = engine.apply(ir, trace)
    report["engineWarnings"] = list(warnings)

    profiles = Profiles(load_profiles(args.profile))
    play = dict(profiles.playability)
    play["bpm"] = float(args.bpm)
    play["maxNPS"] = float(args.max_nps)
    play["maxLaneJump"] = 99           # 相对音高映射已经保证相邻 ±1 列，不再二次改轨
    play["maxJackCount"] = 999
    before = len(notes)
    repaired, changes = playability.repair(notes, play)
    report["stages"]["playability"] = dict(before=before, after=len(repaired),
                                           changes=[c.to_dict() for c in changes][:200])

    chart = build_chart(ir, sorted(repaired, key=lambda n: (n.get("time", 0.0), n.get("lane", 0))))
    guard = fix_charts.apply_guardrails(chart, args.max_hold)
    report["guardrails"] = list(guard)
    notes = sorted(chart["notes"], key=lambda n: (n["time"], n["lane"], n["type"]))

    final = []
    downgraded = {}
    for n in notes:
        kind = TYPE_TO_OSU.get(n["type"])
        if kind is None:
            continue
        if n["type"] != kind:
            downgraded[n["type"]] = downgraded.get(n["type"], 0) + 1
        final.append(dict(time=float(n["time"]), lane=int(n["lane"]), kind=kind,
                          end=float(n.get("endTime") or n["time"])))
    report["stages"]["emit"] = dict(notes=len(final), downgradedToTap=downgraded)

    # 逐音追溯：BP 事件 → IR 事件 → 规则引擎出的音符 → 最终是否活着
    alive = {(round(n["time"], 3), n["lane"]) for n in final}
    ledger = []
    for ev, emitted in trace:
        src = src_of.get((ev.tick, ev.pitch))
        if not src:
            continue
        residual_ms = (src["start"] - ev.time) * 1000.0
        for n in emitted:
            key = (round(float(n["time"]), 3), int(n["lane"]))
            ledger.append(dict(
                sourceIndex=src["_idx"], sourceTime=round(src["start"], 3), pitch=ev.pitch,
                onset=round(src["onset"], 3), amplitude=round(src["amp"], 3),
                irTick=ev.tick, irTime=round(ev.time, 4),
                residualMs=round(residual_ms, 1),
                lane=int(n["lane"]), type=n["type"],
                downgraded=TYPE_TO_OSU.get(n["type"], "dropped") if n["type"] != TYPE_TO_OSU.get(n["type"]) else None,
                survived=key in alive,
            ))
    gone = [r for r in ledger if not r["survived"]]
    report["ledger"] = sorted(ledger, key=lambda r: r["sourceTime"])
    report["stages"]["ledger"] = dict(rows=len(ledger), survived=len(ledger) - len(gone),
                                      removedAfterEmit=len(gone))
    return final


def write_osu(notes, args, ids, path: pathlib.Path):
    beat_ms = 60000.0 / args.bpm
    # 写成「第一拍在音频开头之前」的负相位，与现有 ref.osu 的写法一致
    offset_ms = args.first_beat_ms - beat_ms * math.ceil(args.first_beat_ms / beat_ms)
    lines = [
        "osu file format v14",
        "",
        "[General]",
        f"AudioFilename: {args.audio_name}",
        "AudioLeadIn: 0",
        "PreviewTime: -1",
        "Countdown: 0",
        "SampleSet: Soft",
        "StackLeniency: 0.7",
        "Mode: 3",
        "LetterboxInBreaks: 0",
        "SpecialStyle: 0",
        "WidescreenStoryboard: 0",
        "",
        "[Editor]",
        f"Bookmarks: {offset_ms:.0f}",
        "DistanceSpacing: 1",
        "BeatDivisor: 4",
        "GridSize: 8",
        "TimelineZoom: 1",
        "",
        "[Metadata]",
        f"Title:{args.title}",
        f"TitleUnicode:{args.title}",
        f"Artist:{args.artist}",
        f"ArtistUnicode:{args.artist}",
        f"Creator:basic-pitch {BP_VERSION} / bp_to_osu v{TOOL_VERSION} / grid {ids['grid']}",
        "Version:Bass",
        "Source:",
        f"Tags:{ids['tags']}",
        "BeatmapID:0",
        "BeatmapSetID:-1",
        "",
        "[Difficulty]",
        "HPDrainRate: 5",
        f"CircleSize: {args.keycount}",
        "OverallDifficulty: 8",
        "ApproachRate: 5",
        "SliderMultiplier: 1.4",
        "SliderTickRate: 1",
        "",
        "[Events]",
        "//Background and Video events",
        "",
        "[TimingPoints]",
        f"{offset_ms:.3f},{beat_ms:.8f},4,2,0,70,1,0",
        "",
        "[HitObjects]",
    ]
    for n in notes:
        x = int((n["lane"] + 0.5) * 512 / args.keycount)
        t = int(round(n["time"] * 1000))
        if n["kind"] == "hold":
            end = int(round(n["end"] * 1000))
            if end - t < args.hold_min_ms:
                lines.append(f"{x},192,{t},1,0,0:0:0:0:")
            else:
                lines.append(f"{x},192,{t},128,0,{end}:0:0:0:0:")
        else:
            lines.append(f"{x},192,{t},1,0,0:0:0:0:")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description="Basic Pitch → 5K mania .osu")
    ap.add_argument("--events", required=True, help="events.json 或 basic-pitch note events csv")
    ap.add_argument("--name", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default=None, help="生成记录（默认 <out>.report.json）")
    ap.add_argument("--bpm", type=float, default=None)
    ap.add_argument("--first-beat-ms", type=float, default=0.0)
    ap.add_argument("--timing", default=None, help="Song Timing 产物；给了就盖过 --bpm/--first-beat-ms")
    ap.add_argument("--audio-name", default=None)
    ap.add_argument("--title", default="Unknown Title")
    ap.add_argument("--artist", default="Unknown Artist")
    ap.add_argument("--instrument", default="bass")
    ap.add_argument("--keycount", type=int, default=5)
    ap.add_argument("--subdivision", type=int, default=4, help="每拍切几份（4=十六分）")
    ap.add_argument("--snap-tolerance", type=float, default=0.3, help="超过这个比例的误差就丢掉该音")
    ap.add_argument("--pitch-min", type=int, default=24, help="Bass 合理音域下限（C1）")
    ap.add_argument("--pitch-max", type=int, default=60, help="上限（C4）——再高就是分轨串音")
    ap.add_argument("--min-onset", type=float, default=0.0)
    ap.add_argument("--max-nps", type=float, default=8.0)
    ap.add_argument("--max-hold", type=float, default=4.0)
    ap.add_argument("--hold-min-ms", type=float, default=1e9, help="默认不发长条（只出 tap）")
    ap.add_argument("--bp-version", default="0.4.0")
    ap.add_argument("--rules", default=str(DEFAULT_RULES))
    ap.add_argument("--profile", default=None)
    args = ap.parse_args()

    global BP_VERSION
    BP_VERSION = args.bp_version
    bpm, first_beat, grid_source = load_grid(args)
    args.bpm, args.first_beat_ms = bpm, first_beat * 1000.0
    if args.audio_name is None:
        args.audio_name = f"local_{args.name}.ogg" if args.name != "light" else "local_light.ogg"

    events_path = pathlib.Path(args.events)
    events_sha = sha256_of(events_path)
    grid_id = f"{bpm:.3f}bpm@{args.first_beat_ms:+.1f}ms/{grid_source}"
    report = dict(name=args.name,
                  inputs=dict(
                      events=dict(path=str(events_path), sha256=events_sha),
                      audio=args.audio_name,
                      grid=dict(bpm=bpm, firstBeatMs=args.first_beat_ms, source=grid_source),
                      tool=dict(name="bp_to_osu", version=TOOL_VERSION,
                                basicPitch=BP_VERSION, rules=str(args.rules)),
                  ),
                  params=dict(subdivision=args.subdivision, snapTolerance=args.snap_tolerance,
                              pitchRange=[args.pitch_min, args.pitch_max],
                              minOnset=args.min_onset, maxNps=args.max_nps,
                              holdMinMs=args.hold_min_ms),
                  stages={})

    events = load_events(events_path)
    ir, src_of = to_ir(events, bpm, first_beat, args, report)
    notes = to_notes(ir, src_of, args, report)
    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    ids = dict(grid=grid_id,
               tags=(f"generator=basic-pitch generatorVersion={BP_VERSION} "
                     f"tool=bp_to_osu/{TOOL_VERSION} events={events_sha[:12]} "
                     f"grid={grid_id} notes={len(notes)}"))
    write_osu(notes, args, ids, out)

    report["stages"]["final"] = dict(notes=len(notes),
                                     taps=sum(1 for n in notes if n["kind"] == "tap"),
                                     holds=sum(1 for n in notes if n["kind"] == "hold"))
    osu_sha = sha256_of(out)
    report["output"] = dict(path=str(out), sha256=osu_sha, notes=len(notes))
    report["trace"] = ("拿 ChartData.sourceSha256 对同名报告即可回溯："
                       f"<gen_dir>/{osu_sha}.report.json")

    # 报告用 .osu 的 sha256 命名 —— Adapter 的 sourceSha256 直接就是这把钥匙
    rep_path = out.with_name(f"{osu_sha}.report.json")
    rep_path.write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    if args.report and pathlib.Path(args.report) != rep_path:
        pathlib.Path(args.report).write_text(
            json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")

    q = report["stages"]["quantize"]
    print(f"{args.name}: BPM={bpm:.3f} 第一拍={args.first_beat_ms:.1f}ms ({grid_source})")
    print(f"  量化: 输入 {q['input']} → 留 {q['kept']}，丢弃 {q['dropped']}")
    print(f"  出谱: {len(notes)} 音（tap {report['stages']['final']['taps']}"
          f" / hold {report['stages']['final']['holds']}）→ {out}")
    print(f"  osu sha256: {osu_sha}")
    print(f"  生成记录: {rep_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
