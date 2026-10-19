#!/usr/bin/env python3
"""How many of the game's notes actually land on a drum hit?

The question this answers: the game does not play notes at detected onsets, it
puts them on a beat grid predicted from the music. Is that prediction grounded,
or is it inventing events?

Ground truth is the isolated drum stem of the same song, which the detector
never sees. Notes are matched against it with a tolerance, and the offset of
each match is reported so a systematic early/late bias shows up too.

    java -cp tools/classes BeatDump tools/raw/song.raw 1 all > notes.txt
    python check_notes.py --notes notes.txt --song hitchcock
"""

from __future__ import annotations

import argparse
import os

import numpy as np

from detect import SR, band_split, envelope, load_mono, onset_strength, pick_peaks, HOP_MS
from beats import onset_curve


def load_notes(path: str) -> np.ndarray:
    times = []
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line or line.startswith("#") or line.startswith("java ") \
                    or line.startswith("first notes") or line.startswith("lead at"):
                continue
            try:
                times.append(float(line))
            except ValueError:
                continue
    return np.asarray(sorted(times))


def stem_hits(path: str, threshold_k: float, refractory_ms: float) -> np.ndarray:
    """Strong drum hits in the isolated stem - the answer key."""
    x = load_mono(path)
    hop = max(1, int(SR * HOP_MS / 1000.0))
    events = []
    for lo, hi in ((30.0, 160.0), (1800.0, 8000.0)):
        env = envelope(band_split(x, SR, lo, hi), SR)[::hop]
        events.append(pick_peaks(onset_strength(env), SR, hop, threshold_k,
                                 refractory_ms=refractory_ms))
    return np.sort(np.concatenate(events)) / SR


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--notes", required=True)
    parser.add_argument("--song", required=True)
    parser.add_argument("--assets", default="../Assets/Resources/Audio")
    parser.add_argument("--tolerance-ms", type=float, default=100.0)
    parser.add_argument("--truth-k", type=float, default=3.0)
    parser.add_argument("--refractory", type=float, default=120.0)
    args = parser.parse_args()

    notes = load_notes(args.notes)
    hits = stem_hits(os.path.join(args.assets, args.song + "_drums.ogg"),
                     args.truth_k, args.refractory)
    tolerance = args.tolerance_ms / 1000.0

    offsets = []
    for note in notes:
        if len(hits) == 0:
            break
        nearest = hits[np.argmin(np.abs(hits - note))]
        delta = note - nearest
        if abs(delta) <= tolerance:
            offsets.append(delta)

    matched = len(offsets)
    print(f"song {args.song}")
    print(f"  notes from the game      : {len(notes)}")
    print(f"  strong hits in the stem  : {len(hits)}")
    print(f"  notes landing on a hit   : {matched}  "
          f"({matched / len(notes):.0%} of notes)" if notes.size else "  no notes")
    print(f"  hits that got a note     : {matched}  "
          f"({matched / len(hits):.0%} of hits)" if hits.size else "")
    if offsets:
        values = np.asarray(offsets)
        print(f"  offset mean / median     : {values.mean() * 1000:+.0f} ms / "
              f"{np.median(values) * 1000:+.0f} ms")
        print(f"  offset spread (std)      : {values.std() * 1000:.0f} ms")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
