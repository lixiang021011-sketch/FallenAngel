#!/usr/bin/env python3
"""Does the chart move with the music, or does it repeat itself?

The question this answers: a chart can land on every drum hit and still have no
groove, because the drummer changes what they play and the chart does not. Two
numbers say whether that is happening.

  off-beat share   how much of the chart sits between the beats. A chart that
                   only ever writes on the beat is a metronome, whatever it is
                   aligned to.
  bar variation    how much two bars next to each other differ, counted in
                   positions out of 16. Measured for the chart and for the drum
                   stem, which is the answer key: a chart that follows the music
                   varies about as much as the music does, while one built from
                   the song-so-far average varies hardly at all.

    python -X utf8 rhythm_lab/check_groove.py --notes notes.txt --song hitchcock \
        --bpm 83.5 --assets ../Assets/Resources/Audio

ffmpeg has to be on PATH to read the stem.
"""

from __future__ import annotations

import argparse
import os

import numpy as np

from check_notes import load_notes, stem_hits

SUBDIVISIONS = 4
BEATS_PER_BAR = 4


def pattern_matrix(times: np.ndarray, bpm: float, phase: float) -> np.ndarray:
    """A bar-by-position grid of onsets: rows are bars, columns are positions."""
    step = 60.0 / bpm / SUBDIVISIONS
    bars = int((times[-1] - phase) / (step * SUBDIVISIONS * BEATS_PER_BAR)) + 1
    grid = np.zeros((max(bars, 1), SUBDIVISIONS * BEATS_PER_BAR), dtype=bool)
    for time in times:
        position = int(round((time - phase) / step))
        if position < 0:
            continue
        bar, slot = divmod(position, SUBDIVISIONS * BEATS_PER_BAR)
        if bar < grid.shape[0]:
            grid[bar, slot] = True
    return grid


def best_phase(times: np.ndarray, bpm: float) -> float:
    """The phase that puts the most onsets on a beat rather than between two."""
    if times.size == 0:
        return 0.0
    step = 60.0 / bpm / SUBDIVISIONS
    beat = step * SUBDIVISIONS
    best_score, best_phase = -1.0, 0.0
    for offset in np.arange(0.0, beat, beat / 120):
        shift = (times - offset) / beat
        score = float(np.mean(np.abs(shift - np.round(shift)) < 0.12))
        if score > best_score:
            best_score, best_phase = score, float(offset)
    return float(best_phase % (beat * BEATS_PER_BAR))


def off_beat_share(times: np.ndarray, bpm: float, phase: float) -> float:
    """How much of the chart is not on the beat."""
    step = 60.0 / bpm / SUBDIVISIONS
    positions = np.round((times - phase) / step).astype(int)
    return float(np.mean(positions % SUBDIVISIONS != 0)) if times.size else 0.0


def bar_variation(grid: np.ndarray) -> float:
    """How many positions, out of sixteen, change between one bar and the next."""
    if grid.shape[0] < 2:
        return 0.0
    return float(np.mean([np.count_nonzero(a != b)
                          for a, b in zip(grid[:-1], grid[1:])]))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--notes", required=True)
    parser.add_argument("--song", required=True)
    parser.add_argument("--bpm", type=float, required=True)
    parser.add_argument("--assets", default="../Assets/Resources/Audio")
    parser.add_argument("--truth-k", type=float, default=3.0)
    parser.add_argument("--refractory", type=float, default=120.0)
    args = parser.parse_args()

    notes = load_notes(args.notes)
    hits = stem_hits(os.path.join(args.assets, args.song + "_drums.ogg"),
                     args.truth_k, args.refractory)
    if notes.size == 0 or hits.size == 0:
        print("no notes or no hits to compare")
        return 1

    # One phase for both, taken from the chart: the question is whether the
    # chart moves with the music, not whether either is on a grid of its own.
    phase = best_phase(notes, args.bpm)
    chart_grid = pattern_matrix(notes, args.bpm, phase)
    truth_grid = pattern_matrix(hits, args.bpm, phase)

    print(f"song {args.song}   bpm {args.bpm:.1f}   bar = "
          f"{60.0 / args.bpm * BEATS_PER_BAR:.2f}s")
    print(f"  off-beat share    chart {off_beat_share(notes, args.bpm, phase):5.0%}"
          f"   (a chart that only writes on the beat is a metronome)")
    print(f"  bar variation     chart {bar_variation(chart_grid):4.1f}/16"
          f"   drums {bar_variation(truth_grid):4.1f}/16")
    print(f"  positions per bar hit: chart "
          f"{chart_grid.sum() / chart_grid.shape[0]:.1f}/16   drums "
          f"{truth_grid.sum() / truth_grid.shape[0]:.1f}/16")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
