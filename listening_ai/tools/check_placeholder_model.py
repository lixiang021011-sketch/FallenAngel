#!/usr/bin/env python3
"""Run the placeholder model on known inputs and print what it said.

The point is to check the *contract* before any of it reaches a phone: the
shapes it declares, and whether the four outputs respond to the four things
they are supposed to be measuring.

    python tools/check_placeholder_model.py app/src/main/assets/models/placeholder-events.onnx
"""

import sys

import numpy as np
import onnxruntime as ort

MELS, FRAMES = 64, 64
NAMES = ["onset", "kick", "snare", "novelty"]


def main() -> int:
    path = sys.argv[1] if len(sys.argv) > 1 else \
        "app/src/main/assets/models/placeholder-events.onnx"
    session = ort.InferenceSession(path)
    print("inputs :", [(i.name, i.shape) for i in session.get_inputs()])
    print("outputs:", [(o.name, o.shape) for o in session.get_outputs()])

    cases = {}
    cases["silence"] = np.zeros((1, 1, MELS, FRAMES), np.float32)
    low = np.zeros((1, 1, MELS, FRAMES), np.float32)
    low[:, :, : MELS // 4, :] = 3.0
    cases["low bands"] = low
    high = np.zeros((1, 1, MELS, FRAMES), np.float32)
    high[:, :, MELS - MELS // 4 :, :] = 3.0
    cases["high bands"] = high
    burst = np.zeros((1, 1, MELS, FRAMES), np.float32)
    burst[:, :, :, ::4] = 4.0
    cases["pulsing"] = burst

    print()
    print(f"{'input':<12}" + "".join(f"{name:>10}" for name in NAMES))
    for name, clip in cases.items():
        values = session.run(None, {"logmel": clip})[0][0]
        print(f"{name:<12}" + "".join(f"{v:10.3f}" for v in values))

    silent = session.run(None, {"logmel": cases["silence"]})[0][0]
    low_run = session.run(None, {"logmel": cases["low bands"]})[0][0]
    high_run = session.run(None, {"logmel": cases["high bands"]})[0][0]
    pulse_run = session.run(None, {"logmel": cases["pulsing"]})[0][0]
    checks = [
        ("silence gives nothing", float(np.max(silent)) == 0.0),
        ("low bands raise kick, not snare", low_run[1] > low_run[2] + 1),
        ("high bands raise snare, not kick", high_run[2] > high_run[1] + 1),
        ("pulsing raises novelty", pulse_run[3] > silent[3] + 1),
    ]
    print()
    for label, ok in checks:
        print(("  ok   " if ok else "  FAIL ") + label)
    return 0 if all(ok for _, ok in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
