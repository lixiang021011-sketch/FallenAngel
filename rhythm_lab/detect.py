#!/usr/bin/env python3
"""Offline prototype of the rhythm-event detector, and a way to measure it.

The game only needs to find a few clear events - the snare crack, the kick, the
strong downbeat - not a full transcription. This script implements the detector
that will be ported to the phone, and evaluates it against material that has a
known answer:

    <song>.ogg              the full mix, what the phone will actually hear
    <song>_drums.ogg        the isolated drum stem, used as ground truth

The question being measured is not "can we transcribe drums" but the one the
game depends on: how many of the drum hits that are unmistakable in isolation
are still findable inside a full mix, and how many extra events does the mix
produce.

Same maths as the intended on-device version: band split, envelope, adaptive
threshold, peak pick with a refractory period. No machine learning, no model
files, nothing that needs more than a few milliseconds of audio at a time.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile
import wave

import numpy as np

SR = 22050
HOP_MS = 5.0
ENV_MS = 15.0

# Kick and snare live in different places; splitting them is what turns "some
# loud thing happened" into "that was a kick" / "that was a snare".
BANDS = {
    "kick": (30.0, 160.0),
    "snare": (1800.0, 8000.0),
}


# --------------------------------------------------------------------- loading


def load_mono(path: str, sr: int = SR) -> np.ndarray:
    """Decode anything ffmpeg understands into a mono float array."""
    with tempfile.TemporaryDirectory() as tmp:
        wav_path = os.path.join(tmp, "out.wav")
        subprocess.run(
            ["ffmpeg", "-v", "error", "-y", "-i", path,
             "-ac", "1", "-ar", str(sr), "-f", "wav", wav_path],
            check=True,
        )
        with wave.open(wav_path, "rb") as handle:
            frames = handle.readframes(handle.getnframes())
    data = np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0
    return data


# ------------------------------------------------------------------------ dsp


def band_split(x: np.ndarray, sr: int, lo: float, hi: float) -> np.ndarray:
    """Zero-phase band isolation via FFT.

    Fine offline, and trivial to reproduce on device with a block-wise FFT. The
    phone version will use overlapping blocks so it can run in real time.
    """
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1.0 / sr)
    spectrum[(freqs < lo) | (freqs > hi)] = 0
    return np.fft.irfft(spectrum, n=len(x))


def envelope(x: np.ndarray, sr: int, win_ms: float = ENV_MS) -> np.ndarray:
    """Absolute value smoothed over a short window - the loudness contour."""
    win = max(1, int(sr * win_ms / 1000.0))
    cumulative = np.concatenate([[0.0], np.cumsum(np.abs(x))])
    smoothed = (cumulative[win:] - cumulative[:-win]) / win
    return np.concatenate([np.zeros(win - 1), smoothed])


def onset_strength(env: np.ndarray) -> np.ndarray:
    """Half-wave rectified difference of the log envelope.

    Log makes a quiet snare in a loud chorus count as much as a loud one in a
    quiet verse, which is what "obvious rhythm event" means to a listener.
    """
    log_env = np.log(env + 1e-6)
    diff = np.diff(log_env, prepend=log_env[0])
    return np.maximum(diff, 0.0)


def rolling_percentile(values: np.ndarray, window: int, percentile: float) -> np.ndarray:
    """Percentile over a sliding window, computed on a coarse grid and stretched.

    A true sliding percentile over millions of frames is far too slow, and the
    threshold only needs to move smoothly, so sampling every 20th frame and
    interpolating back is both accurate enough and cheap - the same trick the
    on-device version will use.
    """
    n = len(values)
    step = 20
    grid = np.arange(0, n, step)
    half = max(1, window // 2)
    sampled = np.empty(len(grid), dtype=np.float32)
    for index, centre in enumerate(grid):
        lo = max(0, centre - half)
        hi = min(n, centre + half)
        sampled[index] = np.percentile(values[lo:hi], percentile)
    return np.interp(np.arange(n), grid, sampled)


def pick_peaks(strength: np.ndarray, sr: int, hop: int, k: float,
               refractory_ms: float = 60.0, percentile: float = 75.0) -> np.ndarray:
    """Adaptive threshold plus a refractory period, returned as sample offsets."""
    window = int(sr * 2.0 / hop)  # +-1 s of context
    threshold = rolling_percentile(strength, window, percentile) * k
    above = strength > threshold
    if not above.any():
        return np.empty(0, dtype=np.int64)

    refractory = int(refractory_ms / HOP_MS)
    peaks = []
    last = -refractory - 1
    candidates = np.flatnonzero(above)
    for index in candidates:
        if index - last < refractory:
            continue
        lo = max(0, index - 1)
        hi = min(len(strength), index + 2)
        if strength[index] >= strength[lo:hi].max():
            peaks.append(index)
            last = index
    return np.asarray(peaks, dtype=np.int64) * hop


def detect(x: np.ndarray, sr: int = SR, k: float = 1.6,
           refractory_ms: float = 60.0) -> dict:
    """Detect events per band. Returns sample offsets keyed by band name."""
    hop = max(1, int(sr * HOP_MS / 1000.0))
    out = {}
    for name, (lo, hi) in BANDS.items():
        isolated = band_split(x, sr, lo, hi)
        env = envelope(isolated, sr)[::hop]
        strength = onset_strength(env)
        out[name] = pick_peaks(strength, sr, hop, k, refractory_ms=refractory_ms)
    return out


# ------------------------------------------------------------------ evaluation


def match(reference: np.ndarray, found: np.ndarray, sr: int, tolerance_ms: float = 50.0):
    """Greedy nearest matching. Returns (matched, extra, missed)."""
    tolerance = sr * tolerance_ms / 1000.0
    used = np.zeros(len(found), dtype=bool)
    matched = 0
    for ref in reference:
        if len(found) == 0:
            break
        distances = np.abs(found.astype(np.float64) - float(ref))
        distances[used] = np.inf
        best = int(np.argmin(distances))
        if distances[best] <= tolerance:
            used[best] = True
            matched += 1
    return matched, int((~used).sum()), int(len(reference) - matched)


def strong_events(x: np.ndarray, truth_k: float, refractory_ms: float) -> np.ndarray:
    """The events a listener would actually notice, taken from the clean stem."""
    events = detect(x, k=truth_k, refractory_ms=refractory_ms)
    return np.sort(np.concatenate([events["kick"], events["snare"]]))


def evaluate(assets: str, limit: int | None, k: float, truth_k: float,
             refractory_ms: float, sweep: bool) -> int:
    stems = sorted(f for f in os.listdir(assets) if f.endswith("_drums.ogg"))
    if limit:
        stems = stems[:limit]
    if not stems:
        print(f"no drum stems found in {assets}")
        return 1

    mixes, truths, durations = [], [], []
    for stem_name in stems:
        base = stem_name[: -len("_drums.ogg")]
        mix_path = os.path.join(assets, base + ".ogg")
        if not os.path.exists(mix_path):
            continue
        mix = load_mono(mix_path)
        stem = load_mono(os.path.join(assets, stem_name))
        mixes.append((base, mix))
        truths.append(strong_events(stem, truth_k, refractory_ms))
        durations.append(len(mix) / SR)

    print(f"ground truth: drum-stem events at k={truth_k}, "
          f"{refractory_ms:.0f} ms minimum spacing, matched within +-50 ms")
    print(f"{len(mixes)} songs, {sum(durations):.0f} s of music")
    print()

    def run(label: str, threshold: float) -> None:
        ref_total = matched_total = found_total = 0
        for (base, mix), reference, _duration in zip(mixes, truths, durations):
            events = detect(mix, k=threshold, refractory_ms=refractory_ms)
            found = np.sort(np.concatenate([events["kick"], events["snare"]]))
            matched, _extra, _missed = match(reference, found, SR)
            ref_total += len(reference)
            matched_total += matched
            found_total += len(found)
        recall = matched_total / ref_total if ref_total else 0.0
        total_seconds = sum(durations)
        print(f"{label:<12}{ref_total:>12}{matched_total:>12}{recall:>9}"
              f"{found_total:>14}{found_total / total_seconds:>10.1f}")

    print(f"{'threshold k':<12}{'strong hits':>12}{'found':>12}{'recall':>9}"
          f"{'mix events':>14}{'events/sec':>10}")
    print("-" * 70)
    if sweep:
        for threshold in (1.6, 2.0, 2.5, 3.0, 3.5, 4.5):
            run(f"k={threshold:<10}", threshold)
    else:
        run(f"k={k:<10}", k)
    print()
    print("strong hits = drum-stem events a listener would notice (the answer key)")
    print("found       = how many of those the same detector still finds in the mix")
    print("events/sec  = how many notes per second the game would ask the player to hit")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", default="../Assets/Resources/Audio")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--k", type=float, default=1.6)
    parser.add_argument("--truth-k", type=float, default=3.0,
                        help="threshold used on the isolated stem to define the answer key")
    parser.add_argument("--refractory", type=float, default=120.0,
                        help="minimum spacing between events, in ms")
    parser.add_argument("--sweep", action="store_true")
    args = parser.parse_args()
    return evaluate(args.assets, args.limit, args.k, args.truth_k,
                    args.refractory, args.sweep)


if __name__ == "__main__":
    raise SystemExit(main())
