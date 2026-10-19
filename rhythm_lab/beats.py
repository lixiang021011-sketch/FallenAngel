#!/usr/bin/env python3
"""Ask a different question than detect.py does.

detect.py showed that "find every clear hit" produces 4-12 events per second -
five to ten times more than a single-lane game can ask a player to hit. So the
note source should not be raw onsets at all. It should be a beat grid locked to
the music: predictable by construction (which is what makes pre-emptive notes
possible), and with a density that is chosen rather than discovered.

This script estimates tempo and phase from the same onset curve, then reports:

  * the tempo it locked onto,
  * how densely the grid would ask the player to tap,
  * and how often a grid note lands on a real drum hit (precision), measured
    against the isolated stem.
"""

from __future__ import annotations

import argparse
import os

import numpy as np

from detect import BANDS, HOP_MS, SR, band_split, envelope, load_mono, match, onset_strength


def onset_curve(x: np.ndarray, sr: int = SR) -> np.ndarray:
    """One combined onset-strength curve at the 5 ms hop."""
    hop = max(1, int(sr * HOP_MS / 1000.0))
    total = None
    for lo, hi in BANDS.values():
        env = envelope(band_split(x, sr, lo, hi), sr)[::hop]
        strength = onset_strength(env)
        total = strength if total is None else total + strength
    return total / len(BANDS)


def onset_curve_of_stem(x: np.ndarray, sr: int = SR) -> np.ndarray:
    return onset_curve(x, sr)


def best_period(curve: np.ndarray, hop_rate: float, bpm_lo: float, bpm_hi: float):
    """Tempo by autocorrelation, then phase by comb score.

    Autocorrelation alone happily reports half or double the real tempo, so the
    candidates either side of the winner are scored with the same comb used for
    the phase and the strongest one wins.
    """
    centred = curve - curve.mean()
    spectrum = np.fft.rfft(centred, n=2 * len(centred))
    autocorrelation = np.fft.irfft(spectrum * np.conj(spectrum))[: len(centred)]

    lag_lo = int(hop_rate * 60.0 / bpm_hi)
    lag_hi = int(hop_rate * 60.0 / bpm_lo)
    lag_lo = max(1, lag_lo)
    lag_hi = min(len(autocorrelation) - 1, lag_hi)
    if lag_hi <= lag_lo:
        return 0.0, 0, 0.0

    winner = lag_lo + int(np.argmax(autocorrelation[lag_lo:lag_hi]))

    best = (0.0, 0, 0)
    for candidate in (winner // 2, winner, winner * 2):
        if candidate < lag_lo or candidate > lag_hi:
            continue
        score, phase = best_phase(curve, candidate)
        if score > best[0]:
            best = (score, candidate, phase)
    score, period, phase = best
    if period == 0:
        return 0.0, 0, 0.0
    bpm = 60.0 * hop_rate / period
    return bpm, period, phase


def best_phase(curve: np.ndarray, period: int) -> tuple:
    """Which offset of the grid lines up with the most onset energy."""
    best_score = -1.0
    best_offset = 0
    for offset in range(period):
        picks = curve[offset::period]
        if len(picks) < 4:
            continue
        score = float(picks.mean())
        if score > best_score:
            best_score = score
            best_offset = offset
    return best_score, best_offset


def grid_notes(curve: np.ndarray, period: int, phase: int, stride: int,
               threshold: float, hop_rate: float) -> np.ndarray:
    """Notes on the grid, skipping grid points where nothing actually happens."""
    notes = []
    for index in range(phase, len(curve), period * stride):
        local = curve[max(0, index - 2): index + 3]
        if len(local) and local.max() >= threshold:
            notes.append(index)
    return np.asarray(notes, dtype=np.int64) * int(SR * HOP_MS / 1000.0)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", default="../Assets/Resources/Audio")
    parser.add_argument("--limit", type=int, default=3)
    parser.add_argument("--stride", type=int, default=1,
                        help="1 = every beat, 2 = every other beat, 4 = once a bar")
    parser.add_argument("--truth-k", type=float, default=3.0)
    parser.add_argument("--refractory", type=float, default=120.0)
    parser.add_argument("--bpm-lo", type=float, default=70.0)
    parser.add_argument("--bpm-hi", type=float, default=180.0)
    args = parser.parse_args()

    hop_rate = 1000.0 / HOP_MS  # frames per second
    stems = sorted(f for f in os.listdir(args.assets) if f.endswith("_drums.ogg"))
    if args.limit:
        stems = stems[: args.limit]

    print(f"grid: every {args.stride} beat(s), +-100 ms match against the drum stem")
    print()
    print(f"{'song':<24}{'bpm':>7}{'notes':>8}{'notes/sec':>10}"
          f"{'on a hit':>10}{'hit cover':>10}")
    print("-" * 70)

    totals = {"notes": 0, "on_hit": 0, "hits": 0, "covered": 0}
    seconds_total = 0.0

    for stem_name in stems:
        base = stem_name[: -len("_drums.ogg")]
        mix_path = os.path.join(args.assets, base + ".ogg")
        if not os.path.exists(mix_path):
            continue
        mix = load_mono(mix_path)
        stem = load_mono(os.path.join(args.assets, stem_name))

        curve = onset_curve(mix)
        bpm, period, phase = best_period(curve, hop_rate, args.bpm_lo, args.bpm_hi)
        if period == 0:
            print(f"{base:<24}{'--':>7}{'no tempo':>8}")
            continue

        # A note only appears where the grid meets something audible.
        threshold = float(np.percentile(curve, 70))
        notes = grid_notes(curve, period, phase, args.stride, threshold, hop_rate)

        # Answer key: the strong hits in the isolated stem.
        truth_events = []
        for lo, hi in BANDS.values():
            env = envelope(band_split(stem, SR, lo, hi), SR)[::int(SR * HOP_MS / 1000.0)]
            strength = onset_strength(env)
            from detect import pick_peaks
            truth_events.append(pick_peaks(strength, SR, int(SR * HOP_MS / 1000.0),
                                           args.truth_k, refractory_ms=args.refractory))
        truth = np.sort(np.concatenate(truth_events))

        on_hit, _extra, _missed = match(notes, truth, SR, tolerance_ms=100.0)
        _m2, _e2, covered = match(truth, notes, SR, tolerance_ms=100.0)

        duration = len(mix) / SR
        seconds_total += duration
        totals["notes"] += len(notes)
        totals["on_hit"] += on_hit
        totals["hits"] += len(truth)
        totals["covered"] += len(truth) - covered

        print(f"{base:<24}{bpm:>7.1f}{len(notes):>8}{len(notes) / duration:>10.1f}"
              f"{(on_hit / len(notes) if len(notes) else 0):>10.0%}"
              f"{((len(truth) - covered) / len(truth) if len(truth) else 0):>10.0%}")

    print("-" * 70)
    print(f"{'TOTAL':<24}{'':>7}{totals['notes']:>8}"
          f"{totals['notes'] / seconds_total if seconds_total else 0:>10.1f}"
          f"{(totals['on_hit'] / totals['notes'] if totals['notes'] else 0):>10.0%}"
          f"{(totals['covered'] / totals['hits'] if totals['hits'] else 0):>10.0%}")
    print()
    print("notes/sec = what the player would have to tap")
    print("on a hit  = share of notes that land within 100 ms of a real drum hit")
    print("hit cover = share of the song's strong hits that get a note")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
