#!/usr/bin/env python3
"""The numbers a real model's frontend produces, for the Java one to match.

The frontend is where a mismatch is invisible. A model fed a mel that is the
wrong shape does not fail - it answers, and the answers look like answers. So the
shipped frontend is checked against the library the model was trained with: this
script runs librosa over a fixed clip and writes down exactly what it produced,
and a unit test runs the app's own frontend over the same clip and compares.

The clip travels with the answer. Generating it again on the Java side would
mean two libm sin() implementations agreeing to the last bit, and one sample
that rounds the other way is enough to move a silent mel band.

    python tools/make_frontend_golden.py
"""

import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(HERE, "..", "app", "src")
FRONTEND = os.path.join(APP, "main", "assets", "models", "musicnn_msd.frontend.json")
GOLDEN = os.path.join(APP, "test", "resources", "musicnn_frontend_golden.json")

SR = 16000
SECONDS = 0.4


def signal() -> np.ndarray:
    """Three tones and a knock: something with bands, and something with an edge."""
    n = int(SR * SECONDS)
    i = np.arange(n)
    y = (0.40 * np.sin(2 * np.pi * 220 * i / SR)
         + 0.25 * np.sin(2 * np.pi * 3000 * i / SR)
         + 0.15 * np.sin(2 * np.pi * 6300 * i / SR))
    y[n // 2:n // 2 + 20] += 0.6
    return np.floor(y * 32767 + 0.5).astype(np.int16)


def main() -> int:
    import librosa

    if not os.path.exists(FRONTEND):
        raise SystemExit(f"no {FRONTEND}; run export_musicnn.py first")
    with open(FRONTEND, encoding="utf-8") as handle:
        frontend = json.load(handle)
    hop, n_fft, mels = frontend["hop"], frontend["fft"], frontend["mels"]

    pcm = signal()
    y = pcm.astype(np.float32) / 32768.0
    rms = float(np.sqrt(np.mean(y ** 2)))
    y = y * (0.1 / rms)
    powers = librosa.feature.melspectrogram(
        y=y, sr=SR, n_fft=n_fft, hop_length=hop, n_mels=mels, center=True,
    ).T
    # In float32, which is what the app computes in. musicnn's own reader rounds
    # the mel to float16 first, a 2e-4 wobble on the log values - below the
    # tolerance this file is checked with, and not part of what the app claims to
    # reproduce. The whole chain including that rounding was checked against the
    # reference tagger when the model was exported.
    logged = np.log10(10000 * powers.astype(np.float32) + 1)

    golden = {
        "_comment": "Written by tools/make_frontend_golden.py. librosa's own "
                    "answer for this clip, for the app's frontend to reproduce.",
        "sampleRate": SR,
        "levelNormalised": True,
        "frameCount": int(logged.shape[0]),
        "expectedMel": [[float("%.6g" % value) for value in row] for row in logged],
        "samples": [int(value) for value in pcm],
    }
    with open(GOLDEN, "w", encoding="utf-8") as out:
        json.dump(golden, out, indent=1)
    print("wrote", GOLDEN, "%.0f KB" % (os.path.getsize(GOLDEN) / 1024),
          "frames", golden["frameCount"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
