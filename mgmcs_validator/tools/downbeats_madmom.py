"""Ask the machine where the *bar* lines are, in the validator's own shape.

`machine_beats.py` gets the pulse (librosa): it knows how fast the beats are and
where they are, and nothing about bars. This one gets the bars: madmom's
downbeat model (Böck et al., the reference implementation for beat and downbeat
tracking) reads the whole song and says which beat is beat one.

That is the difference that mattered on 消えちゃう前に: the pulse was never in
question, and every phase fits the onsets equally well - so an *assumed* bar
line can sit a beat or a half bar away from the one the music actually has, and
nothing in the data would say so. This is the second opinion that does.

The output is one more calibration with `source: "machine"`, drawn like any
other, plus one `machine_downbeat` event per bar line. Nothing is applied to the
listener's own calibrations: the tool adds, it never edits.

    D:\\fa_tools\\db_venv\\Scripts\\python.exe tools/downbeats_madmom.py song.ogg \\
        --into song.mgmcs.json --out merged.json

That interpreter is the one madmom was built into; see README.md for how it was
built (madmom 0.16.1 predates Python 3.10 and does not install as it stands).
"""

from __future__ import annotations

# madmom 0.16.1 is from 2018 and expects the world of Python 3.7: it imports
# names that moved into collections.abc, and it uses the numpy aliases that were
# removed in numpy 1.24. Both are aliased back before it is imported, because the
# module is otherwise correct and rebuilding it would mean rewriting its models.
import collections
import collections.abc
for _name in ("MutableSequence", "Sequence", "Mapping", "Iterable", "Callable",
              "Set", "MutableMapping", "MutableSet"):
    if not hasattr(collections, _name):
        setattr(collections, _name, getattr(collections.abc, _name))

import argparse
import json
import pathlib
import statistics
import sys
import tempfile

import numpy as np
for _name, _type in (("float", float), ("int", int), ("bool", bool),
                     ("object", object), ("str", str), ("complex", complex),
                     ("long", int)):
    if not hasattr(np, _name):
        setattr(np, _name, _type)

GRID_ID = "madmom"
EVENT_PREFIX = "madmom_db_"


def as_wav(audio_path: pathlib.Path, workdir: pathlib.Path) -> pathlib.Path:
    """madmom's loader wants a wav; anything else is decoded once, here."""
    if audio_path.suffix.lower() == ".wav":
        return audio_path
    import soundfile  # a plain wheel, and it reads ogg and flac as well as wav
    samples, rate = soundfile.read(str(audio_path), always_2d=True)
    out = workdir / (audio_path.stem + ".44k.wav")
    soundfile.write(str(out), samples, rate, subtype="PCM_16")
    return out


def analyse(audio_path: pathlib.Path):
    """(downbeat times, beat times, beat numbers) - all in seconds."""
    try:
        from madmom.features.downbeats import (RNNDownBeatProcessor,
                                               DBNDownBeatTrackingProcessor)
    except ImportError:
        raise SystemExit("这个 Python 里没有 madmom。用装了它的那个：\n"
                         r"  D:\fa_tools\db_venv\Scripts\python.exe")
    with tempfile.TemporaryDirectory() as tmp:
        wav = as_wav(audio_path, pathlib.Path(tmp))
        activation = RNNDownBeatProcessor()(str(wav))
    # Allowing 3 and 4 lets the model say 3/4 by itself where the song is 3/4;
    # on this repertoire it almost always stays in 4.
    result = DBNDownBeatTrackingProcessor(beats_per_bar=[3, 4], fps=100)(activation)
    beats = np.asarray([float(t) for t in result[:, 0]])
    numbers = np.asarray([int(n) for n in result[:, 1]])
    downbeats = beats[numbers == 1]
    return downbeats, beats, numbers


def bar_lengths(numbers) -> list:
    """How many beats each bar of the model's answer has."""
    lengths = []
    count = 0
    started = False
    for n in numbers:
        if n == 1:
            if started:
                lengths.append(count)
            started = True
            count = 1
        elif started:
            count += 1
    return lengths


def machine_document(audio_path: pathlib.Path, downbeats, beats, numbers):
    if len(downbeats) < 2:
        raise SystemExit("madmom 没找到足够的小节线，这份音频可能不适合它。")
    beat_seconds = float(np.median(np.diff(beats))) if len(beats) > 1 else 0.0
    lengths = bar_lengths(numbers)
    meter = statistics.mode(lengths) if lengths else 4
    mixed = sorted(set(lengths)) if lengths else [meter]
    note = ("madmom 的 downbeat 模型（%d 个小节）自己听出来的小节线，全曲 %d/%d。"
            % (len(downbeats), meter, 4))
    if len(mixed) > 1:
        note += " 它的答案里出现过这些小节长度：%s —— 变拍子的话这张网只画得下最常见的那一种。" % mixed
    note += " 拍速与 librosa 那条略有出入时，整曲比较会慢慢滑，分段看。"
    grid = {
        "calibrationId": GRID_ID,
        "audioTime": round(float(downbeats[0]), 3),
        "bpm": round(60.0 / beat_seconds, 3) if beat_seconds else 0.0,
        "beatsPerBar": int(meter),
        "beatUnit": 4,
        "source": "machine",
        "note": note,
        "downbeats": [round(float(t), 3) for t in downbeats],
        "audioFileName": audio_path.name,
    }
    events = [
        {
            "eventId": EVENT_PREFIX + "%03d" % i,
            "audioTime": round(float(t), 3),
            "type": "machine_downbeat",
            "source": "machine",
            "payload": {"beat": "downbeat", "model": "madmom"},
        }
        for i, t in enumerate(downbeats)
    ]
    return grid, events


def merge(document, grid, events):
    """Add or refresh this model's grid and events; touch nothing else."""
    document["calibrations"] = [c for c in document.get("calibrations", [])
                                if c.get("calibrationId") != GRID_ID] + [grid]
    document["events"] = [e for e in document.get("events", [])
                          if e.get("type") != "machine_downbeat"] + events
    return document


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("audio")
    parser.add_argument("--into", help="手机导出的 mgmcs.json，把小节线并进去")
    parser.add_argument("--out", help="写到哪（默认打印）")
    args = parser.parse_args(argv)

    audio_path = pathlib.Path(args.audio)
    if not audio_path.exists():
        raise SystemExit("找不到音频：%s" % audio_path)

    downbeats, beats, numbers = analyse(audio_path)
    grid, events = machine_document(audio_path, downbeats, beats, numbers)
    print("madmom：%d 个小节，第一根在 %.3f s，%s" % (len(downbeats), downbeats[0], grid["note"]), file=sys.stderr)

    if args.into:
        document = json.loads(pathlib.Path(args.into).read_text(encoding="utf-8"))
        merged = merge(document, grid, events)
    else:
        merged = {"schemaVersion": "0.1", "generatedBy": "downbeats_madmom.py",
                  "song": {"title": audio_path.stem, "audioFileName": audio_path.name,
                           "durationSeconds": round(float(beats[-1]) + 0.5, 3)},
                  "calibrations": [grid], "events": events}

    text = json.dumps(merged, ensure_ascii=False, indent=1)
    if args.out:
        pathlib.Path(args.out).write_text(text, encoding="utf-8")
        print("写出：%s" % args.out, file=sys.stderr)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
