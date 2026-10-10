"""Ask the machine what this file's tempo and beats are, in the validator's own shape.

The engine for now is librosa (Ellis's dynamic-programming beat tracker), because
it is already installed on this machine. The point of the file is that the *engine
is not the interface*: what comes out is one calibration with `source: "machine"`
plus its beat times, which is exactly what a human calibration looks like, so the
validator draws both without knowing or caring which is which.

madmom (the reference for beat and *downbeat* tracking) does build on this
machine - see `downbeats_madmom.py`, which is the other half: this script gets
the pulse, that one gets the bar lines. Essentia still has no Windows build.
Swapping an engine in means replacing one script and nothing else.

    python tools/machine_beats.py song.flac --out song.machine.json
    python tools/machine_beats.py song.flac --into song.mgmcs.json --out merged.json

`--into` is the useful one: it merges the machine's lattice into the document the
phone produced, snapping each of the listener's tapped bar heads onto the nearest
machine beat. The listener's own numbers are kept beside the snapped ones, so the
agreement between ear and machine can be read afterwards.
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
import statistics
import sys

BEAT_UNIT = 4
# The machine does not know the meter - only a person, or the pattern of kicks and
# snares, can say how many beats a bar has. Until that is measured the lattice is
# drawn as if it were in 4, and the note says so.
ASSUMED_BEATS_PER_BAR = 4


def analyse(audio_path: pathlib.Path):
    """(tempo, beat times in seconds)."""
    try:
        import librosa
    except ImportError:
        raise SystemExit("这个 Python 里没有 librosa。用装了它的那个：\n"
                         r"  D:\fa_tools\bp_venv\Scripts\python.exe  或  "
                         r"D:\musicnn_export\Scripts\python.exe")
    samples, rate = librosa.load(str(audio_path), sr=None, mono=True)
    tempo, beats = librosa.beat.beat_track(y=samples, sr=rate, units="time", trim=False)
    tempo = float(tempo[0]) if hasattr(tempo, "__len__") else float(tempo)
    return tempo, [float(b) for b in beats]


def machine_document(audio_path: pathlib.Path, tempo: float, beats: list, duration: float):
    """One calibration for the lattice, and one event per beat."""
    grid = {
        "calibrationId": "machine",
        "audioTime": round(beats[0], 3) if beats else 0.0,
        "bpm": round(tempo, 3),
        "beatsPerBar": ASSUMED_BEATS_PER_BAR,
        "beatUnit": BEAT_UNIT,
        "source": "machine",
        "note": "librosa beat_track（拍号是假设，机器不知道小节有几拍）",
        "beats": [round(b, 3) for b in beats],
    }
    events = []
    for index, at in enumerate(beats):
        events.append({
            "eventId": "machine_beat_%04d" % index,
            "audioTime": round(at, 3),
            "type": "machine_beat",
            "source": "machine",
            "payload": {"beatIndex": index},
        })
    return {
        "schemaVersion": "0.1",
        "generatedBy": "machine_beats.py (librosa)",
        "song": {
            "songId": audio_path.stem,
            "title": audio_path.stem,
            "audioFileName": audio_path.name,
            "durationSeconds": round(duration, 3) if duration else None,
        },
        "calibrations": [grid if beats else {}],
        "events": events,
    }


def lattice(tempo: float, beats: list, seconds: float):
    """The machine's grid, as a *grid*: its tempo laid out evenly from its first beat.

    Not the beat times themselves. Measured on ChiliChill — 逃, librosa's own beats
    sit 427 to 715 ms apart around a 565 ms median: the tracker lets the tempo
    wander, which is fine for following a performance and useless as a ruler. A
    grid is what the tempo says it is, so the ruler is built from the tempo.
    """
    if not beats or tempo <= 0:
        return []
    period = 60.0 / tempo
    first = beats[0]
    out = []
    at = first - math.floor(first / period) * period
    while at <= seconds:
        if at >= 0:
            out.append(at)
        at += period
    return out


def nearest(grid: list, at: float):
    """(the grid line nearest this moment, how far away it is)."""
    if not grid:
        return None, float("nan")
    period = grid[1] - grid[0] if len(grid) > 1 else 1.0
    best = min(grid, key=lambda b: abs(b - at))
    if abs(best - at) > period / 2:
        # The nearest grid line may be one step outside the list; snap to the
        # lattice, not to the ends of the list.
        steps = round((at - grid[0]) / period)
        best = grid[0] + steps * period
    return best, at - best


def snap_human(document: dict, tempo: float, grid: list):
    """Put the listener's bar heads on the machine's lattice, and keep the originals.

    The tempo becomes the machine's - a whole file's worth of evidence against
    twenty seconds of tapping - and each calibration's origin moves to the nearest
    machine beat. Both the number it was and the distance it moved are kept: that
    distance *is* the agreement between the ear and the machine, and an audit that
    throws it away has nothing left to check.
    """
    if not grid:
        return []
    notes = []
    for calibration in document["calibrations"]:
        tapped_bpm = calibration.get("bpm")
        tapped_origin = calibration.get("audioTime")
        # Every bar head of the pass, not just the last one. Each tap lands within
        # the player's position-reporting step of the truth, which on a streamed
        # track is a few tenths of a second; the median over the pass's bar heads
        # averages that away, and the spread around it is the honest measure of
        # how well the ear and the machine agree.
        offsets = []
        for tap in calibration.get("rawTaps", []):
            if tap.get("button") != "DOWNBEAT":
                continue
            at = tap.get("songTime")
            if at is None:
                continue
            _, offset = nearest(grid, at)
            if offset == offset:          # not NaN
                offsets.append(offset)
        offsets.sort()
        offset = statistics.median(offsets) if offsets else 0.0
        spread = (offsets[-1] - offsets[0]) if len(offsets) > 1 else 0.0
        snapped, _ = nearest(grid, tapped_origin - offset)
        calibration["tappedBpm"] = tapped_bpm
        calibration["tappedAudioTime"] = tapped_origin
        calibration["snapOffsetSeconds"] = round(offset, 3)
        calibration["snapSpreadSeconds"] = round(spread, 3)
        calibration["snapTaps"] = len(offsets)
        calibration["bpm"] = round(tempo, 3)
        calibration["audioTime"] = round(snapped, 3)
        calibration["note"] = (calibration.get("note", "")
                              + "；BPM 换成机器的，起点按 %d 个小节头的中位偏移吸到机器网格"
                                "（%+.0f ms，抖动 %.0f ms）"
                              % (len(offsets), offset * 1000, spread * 1000))
        notes.append((calibration.get("calibrationId"), tapped_bpm, tapped_origin,
                      tempo, snapped, offset, spread, len(offsets)))
    return notes


def bar_lengths(document: dict, grid: list, tempo: float):
    """How many of the machine's beats each of the listener's bars spans.

    This is where a meter change shows up, and it shows up as a number rather than
    as a feeling: a bar is four beats, then three, then four again - or, when a
    section is a hemiola, "four" and then "two" against the machine's own pulse,
    because three triplet beats and two ordinary ones are the same length of time.

    The offsets are corrected by the pass's own median first, so that one tap
    sitting either side of a grid line - which the player's position reporting
    makes ordinary - cannot turn one bar into two.
    """
    period = 60.0 / tempo
    out = []
    for calibration in document["calibrations"]:
        offset = calibration.get("snapOffsetSeconds", 0.0)
        # The *capture* clock, not the song clock.
        #
        # The player's reported position moves in steps of about a second: on this
        # phone, every tap of a pass landed on a 1.02 s grid, so a bar measured
        # with it can only come out as one step, two steps or three - 1.80, 3.59,
        # 5.38 machine beats - and a bar that is genuinely three and a half beats
        # is indistinguishable from a rounding of four. The phone's own clock
        # counts real milliseconds and has no such grid, so a bar's *length* is
        # measured there. (Its *phase* still comes from the song clock, because
        # that is the clock the validator draws in.)
        heads = [t for t in calibration.get("rawTaps", []) if t.get("button") == "DOWNBEAT"]
        gaps = []
        for earlier, later in zip(heads, heads[1:]):
            first, second = earlier.get("captureTime"), later.get("captureTime")
            if first is None or second is None:
                continue
            gaps.append((second - first) / period)
        usual = statistics.median(gaps) if gaps else 0.0
        out.append((calibration.get("calibrationId"), gaps, usual))
    return out


def fold_identical(document: dict, grid: list, tempo: float, tolerance=0.1):
    """Collapse the calibrations whose bar lines are the same lines.

    Two grids that differ by a whole number of bars are one ruler with a different
    label on it: every line of one is a line of the other, so drawing both is
    drawing the same thing twice. After the snapping that is common - measured, on
    Cö shu Nie — 消えちゃう前に three of five 4/4 passes folded onto one lattice.

    What survives the fold is the count. "Three separate passes landed on this
    exact grid" is a fact about the listener's ear, and it is worth more than the
    three identical tracks it replaces; so the passes that folded in are kept by
    name, with how far each sat from the line it was folded onto.
    """
    period = 60.0 / tempo

    def circular_gap(a, b, bars):
        """How far apart two bar phases are, the short way round the bar.

        A phase of 3.9998 and one of 0.0009 are the same bar line - the first is
        a rounding-hair below zero, the second a hair above it. Subtracting them
        gives 3.9989 and the fold then decides the two passes disagree, which is
        what it did to cal_01 and cal_05 before this: two identical grids drawn as
        two rows. The distance that matters wraps at the bar.
        """
        gap = abs(a - b) % bars
        return min(gap, bars - gap)

    groups = []
    for calibration in document["calibrations"]:
        if calibration.get("source") == "machine":
            continue
        bars = calibration.get("beatsPerBar") or 4
        phase = ((calibration.get("audioTime", 0.0) - grid[0]) / period) % bars
        placed = False
        for group in groups:
            if group["bars"] != bars:
                continue
            if circular_gap(group["phase"], phase, bars) <= tolerance:
                group["members"].append(calibration)
                placed = True
                break
        if not placed:
            groups.append({"bars": bars, "phase": phase, "members": [calibration]})

    folded = []
    for group in groups:
        members = sorted(group["members"], key=lambda c: c.get("audioTime", 0.0))
        keeper = members[0]
        keeper["mergedFrom"] = [
            {
                "calibrationId": other.get("calibrationId"),
                "tappedAudioTime": other.get("tappedAudioTime"),
                "snappedAudioTime": other.get("audioTime"),
                "snapOffsetSeconds": other.get("snapOffsetSeconds"),
                "snapSpreadSeconds": other.get("snapSpreadSeconds"),
                "tappedBpm": other.get("tappedBpm"),
                # Every press of that pass, with both clocks, exactly as the phone
                # wrote it. A bar that is three and a half beats long can only be
                # seen in the times somebody actually pressed - a summary of the
                # pass cannot show it, and nothing else in the file can either.
                "rawTaps": other.get("rawTaps", []),
            }
            for other in members[1:]
        ]
        # Count the *passes*, not the rows: a keeper that already stands for three
        # passes and absorbs one more is four passes agreeing, and saying "2" would
        # understate the only thing the fold is worth.
        absorbed = sum(m.get("passesAgreeing", 1) for m in members[1:])
        keeper["passesAgreeing"] = sum(m.get("passesAgreeing", 1) for m in members)
        if absorbed:
            keeper["note"] = (keeper.get("note", "")
                              + "；与另外 %d 遍的小节线完全重合，合成一条" % absorbed)
        folded.append(keeper)

    machine = [c for c in document["calibrations"] if c.get("source") == "machine"]
    document["calibrations"] = folded + machine
    return [(group["members"][0].get("calibrationId"),
             [c.get("calibrationId") for c in group["members"]]) for group in groups]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("audio")
    parser.add_argument("--into", help="手机导出的 mgmcs.json，把机器网格并进去")
    parser.add_argument("--out", help="写到哪（默认打印）")
    args = parser.parse_args(argv)

    audio = pathlib.Path(args.audio)
    if not audio.is_file():
        print("找不到音频：%s" % audio, file=sys.stderr)
        return 2
    tempo, beats = analyse(audio)
    duration = 0.0
    try:
        import soundfile
        duration = soundfile.info(str(audio)).duration
    except Exception:
        pass

    print("机器（librosa）：%.2f BPM，%d 拍，第一拍在 %.3f 秒"
          % (tempo, len(beats), beats[0] if beats else 0))
    if len(beats) > 2:
        intervals = [b - a for a, b in zip(beats, beats[1:])]
        median = sorted(intervals)[len(intervals) // 2]
        print("  每拍间隔：中位 %.1f ms（跨度 %.1f–%.1f ms）"
              % (median * 1000, min(intervals) * 1000, max(intervals) * 1000))

    if args.into:
        path = pathlib.Path(args.into)
        document = json.loads(path.read_text(encoding="utf-8"))
        seconds = max([c.get("audioTime", 0) for c in document["calibrations"]] + [0]) + 10.0
        grid = lattice(tempo, beats, seconds)
        notes = snap_human(document, tempo, grid)
        lengths = bar_lengths(document, grid, tempo)
        document["calibrations"].append(machine_document(audio, tempo, beats, duration)["calibrations"][0])
        document["events"].extend(machine_document(audio, tempo, beats, duration)["events"])
        document["generatedBy"] = (document.get("generatedBy", "") + " + machine_beats.py").strip(" +")
        print()
        print("人对拍 → 机器拍（吸上去挪了多少）：")
        for (name, tapped_bpm, tapped_origin, machine_bpm, snapped,
             offset, spread, taps) in notes:
            ratio = (tapped_bpm / machine_bpm) if tapped_bpm else 0
            print("  %-8s 人 %7.3f / %8.3f  →  机器 %7.3f / %8.3f"
                  "   %d 个小节头中位偏移 %+6.0f ms（抖动 %4.0f ms）  拍速比 %.3f"
                  % (name, tapped_bpm or 0, tapped_origin or 0, machine_bpm, snapped,
                     taps, offset * 1000, spread * 1000, ratio))
        print()
        print("每一条网格的「一个自己小节 = 机器的几拍」（不取整——非整数小节就在这里）：")
        for name, gaps, usual in lengths:
            if not gaps:
                continue
            # How far each bar sits from a whole number of beats. One bar off by
            # half is a wobble in the recording; several in a row at the same
            # fraction are a bar that is not a whole number of beats, and that is
            # a finding rather than noise.
            off = sum(1 for gap in gaps if abs(gap - round(gap)) > 0.25)
            print("  %-8s 中位 %.2f 拍，逐个 %s"
                  % (name, usual, " ".join("%.2f" % gap for gap in gaps)))
            print("           %s" % ("← %d 个不是整拍的小节" % off if off
                                     else "全是整拍（小数部分只是记录抖动）"))
            for calibration in document["calibrations"]:
                if calibration.get("calibrationId") != name:
                    continue
                # The bar length is the one the machine's pulse measures: three
                # triplet beats and two ordinary ones are the same length of time,
                # and a grid that is snapped onto the machine's beats has to be
                # drawn in *those* beats or it stops passing through the taps it
                # came from. What the listener called it is kept beside it - in a
                # hemiola, "3 against 2" is the finding itself, not noise to
                # overwrite.
                calibration["tappedBeatsPerBar"] = calibration.get("beatsPerBar")
                # Two bar heads give one gap, and one gap agrees with itself: a
                # pass that short says nothing about the meter, so it keeps the
                # listener's number rather than the machine's guess.
                whole = abs(usual - round(usual)) <= 0.15
                if len(gaps) >= 3 and whole and 2 <= round(usual) <= 8:
                    calibration["beatsPerBar"] = int(round(usual))
                else:
                    calibration["barLengthSource"] = (
                        "人标的（机器这边样本太少）" if len(gaps) < 3
                        else "人标的（机器量出来不是整数拍）")
                calibration["barsInMachineBeats"] = [round(gap, 3) for gap in gaps]
                calibration["barInMachineBeats"] = round(usual, 3)
                if off:
                    calibration["nonIntegerBars"] = off
        print()
        print("吸附后小节线重合的，合成一条：")
        for kept, members in fold_identical(document, grid, tempo):
            if len(members) > 1:
                print("  %s  ←  %s   （%d 遍互相印证）"
                      % (kept, " + ".join(members), len(members)))
            else:
                print("  %s  （只有这一遍）" % kept)
        target = pathlib.Path(args.out) if args.out else path.with_suffix(".merged.json")
        target.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")
        print()
        print("写出：%s" % target)
        return 0

    document = machine_document(audio, tempo, beats, duration)
    text = json.dumps(document, ensure_ascii=False, indent=2)
    if args.out:
        pathlib.Path(args.out).write_text(text + "\n", encoding="utf-8")
        print("写出：%s" % args.out)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
