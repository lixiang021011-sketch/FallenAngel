#!/usr/bin/env python3
"""Generate a small synthesized 4-stem demo song for the attention annotator.

The point is not musicality. It is that each part *enters and leaves* at a
known time, so a listener has something real to point at:

    0- 8 s   bass + sparse piano
    8-12 s   drums enter
   12-16 s   guitar riff enters
   16-24 s   bass moves to a busier run, piano thins out
   24-32 s   everything together

Output: demo/demo01-*.wav plus demo/manifest.json. Standard library only.
"""

from __future__ import annotations

import array
import json
import math
import os
import random
import sys
import wave

SAMPLE_RATE = 22050
DURATION = 32.0
N = int(SAMPLE_RATE * DURATION)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "demo")


def hz(semitone: float) -> float:
    """MIDI note number -> frequency."""
    return 440.0 * (2.0 ** ((semitone - 69) / 12.0))


A2, C3, D3, E3, F3, G3, A3 = 45, 48, 50, 52, 53, 55, 57
C4, D4, E4, F4, G4, A4, B4, C5, D5, E5, G5 = 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 79
A5, C6, E6 = 81, 84, 88


def env(t: float, dur: float, attack: float, decay: float) -> float:
    if t < 0 or t > dur:
        return 0.0
    a = min(1.0, t / attack) if attack > 0 else 1.0
    d = math.exp(-t * decay)
    tail = 1.0
    release = dur - t
    if release < 0.02:
        tail = max(0.0, release / 0.02)
    return a * d * tail


def add_note(buf, start_s, dur_s, freq, vel, kind, rng):
    i0 = int(start_s * SAMPLE_RATE)
    i1 = min(N, int((start_s + dur_s) * SAMPLE_RATE))
    if i1 <= i0:
        return
    if kind == "bass":
        w = 2 * math.pi * freq / SAMPLE_RATE
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            e = env(t, dur_s, 0.006, 1.6)
            s = math.sin(w * (i - i0)) + 0.35 * math.sin(2 * w * (i - i0)) + 0.12 * math.sin(3 * w * (i - i0))
            s = math.tanh(s * 1.6) * 0.72
            buf[i] += s * e * vel
    elif kind == "guitar":
        w = 2 * math.pi * freq / SAMPLE_RATE
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            e = env(t, dur_s, 0.004, 5.2)
            s = (math.sin(w * (i - i0))
                 + 0.5 * math.sin(2 * w * (i - i0))
                 + 0.3 * math.sin(3 * w * (i - i0))
                 + 0.18 * math.sin(4 * w * (i - i0)))
            buf[i] += s * e * vel * 0.42
    elif kind == "piano":
        w = 2 * math.pi * freq / SAMPLE_RATE
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            e = env(t, dur_s, 0.02, 1.1)
            s = math.sin(w * (i - i0)) + 0.22 * math.sin(2 * w * (i - i0)) + 0.07 * math.sin(3 * w * (i - i0))
            buf[i] += s * e * vel * 0.3
    elif kind == "kick":
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            f = 42 + 130 * math.exp(-t * 26)
            p = 2 * math.pi * f * t
            buf[i] += math.sin(p) * math.exp(-t * 11) * vel * 0.95
    elif kind == "snare":
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            noise = rng.uniform(-1.0, 1.0)
            body = math.sin(2 * math.pi * 190 * t) * 0.4
            buf[i] += (noise * 0.7 + body) * math.exp(-t * 22) * vel * 0.5
    elif kind == "hat":
        for i in range(i0, i1):
            t = (i - i0) / SAMPLE_RATE
            noise = rng.uniform(-1.0, 1.0)
            prev = rng.uniform(-1.0, 1.0)
            buf[i] += (noise - prev) * math.exp(-t * 90) * vel * 0.18


def new_buf():
    return array.array("f", bytes(4 * N))


def build_stems():
    rng = random.Random(20261005)
    bass, piano, guitar, drums = new_buf(), new_buf(), new_buf(), new_buf()

    # ---------------------------------------------------------------- bass line
    # Walking root notes for the first half.
    walk = [(0.0, A2), (1.0, C3), (2.0, D3), (3.0, E3),
            (4.0, D3), (5.0, C3), (6.0, E3), (7.0, A2)]
    for bar in range(2):  # bars at 0-8 s
        for offset, note in walk:
            add_note(bass, bar * 8 + offset, 0.9, hz(note), 0.85, "bass", rng)
    for offset, note in [(0.0, A2), (1.0, C3), (2.0, E3), (3.0, G3),
                         (4.0, F3), (5.0, D3), (6.0, E3), (7.0, C3)]:
        add_note(bass, 8 + offset, 0.9, hz(note), 0.9, "bass", rng)
    # 16-24 s: the bass "run" the demo wants people to notice.
    run = [A2, C3, D3, E3, G3, E3, D3, C3, A2, C3, E3, A3, C3, E3, G3, A3]
    for i, note in enumerate(run):
        add_note(bass, 16 + i * 0.5, 0.45, hz(note), 0.95, "bass", rng)
    for offset, note in [(0.0, F3), (1.0, G3), (2.0, A3), (3.0, G3),
                         (4.0, F3), (5.0, E3), (6.0, D3), (7.0, A2)]:
        add_note(bass, 24 + offset, 0.9, hz(note), 0.9, "bass", rng)

    # ------------------------------------------------------------------- piano
    for start, chord, vel in [(0.0, (A3, C4, E4), 0.5), (2.0, (F3, A3, C4), 0.45),
                              (4.0, (G3, C4, E4), 0.45), (6.0, (A3, C4, E4), 0.5),
                              (8.0, (A3, D4, F4), 0.4), (10.0, (G3, C4, E4), 0.35),
                              (12.0, (F3, A3, C4), 0.32), (14.0, (G3, B4, D4), 0.32),
                              (16.0, (A3, C4, E4), 0.22), (18.0, (F3, A3, C4), 0.2),
                              (20.0, (G3, C4, E4), 0.2), (22.0, (E3, A3, C4), 0.2),
                              (24.0, (A3, C4, E4), 0.42), (26.0, (F3, A3, C4), 0.4),
                              (28.0, (G3, C4, E4), 0.4), (30.0, (A3, C4, E4), 0.45)]:
        for note in chord:
            add_note(piano, start, 1.9, hz(note), vel, "piano", rng)

    # ------------------------------------------------------------------ guitar
    riff = [E4, G4, A4, G4, E4, D4, E4, G4]
    for bar in range(3):  # 12-24 s
        base = 12 + bar * 4
        for i, note in enumerate(riff):
            add_note(guitar, base + i * 0.5, 0.55, hz(note), 0.9, "guitar", rng)
    lead = [A4, C5, D5, E5, D5, C5, A4, G4, A4, C5, E5, G5, E5, D5, C5, A4]
    for i, note in enumerate(lead):  # 24-32 s, held a little longer
        add_note(guitar, 24 + i * 0.5, 1.1, hz(note), 1.0, "guitar", rng)

    # ------------------------------------------------------------------- drums
    for bar_start in (8.0, 12.0, 16.0, 20.0, 24.0, 28.0):
        for beat in range(4):
            t = bar_start + beat
            add_note(drums, t, 0.25, 0, 1.0 if beat in (0, 2) else 0.8, "kick", rng)
            if beat in (1, 3):
                add_note(drums, t, 0.2, 0, 0.9, "snare", rng)
        for eighth in range(8):
            add_note(drums, bar_start + eighth * 0.5, 0.08, 0, 0.7 if eighth % 2 else 0.5, "hat", rng)
    add_note(drums, 23.5, 0.4, 0, 1.0, "snare", rng)  # fill into the last section
    add_note(drums, 23.75, 0.3, 0, 1.0, "snare", rng)
    add_note(drums, 23.9, 0.3, 0, 0.9, "snare", rng)

    return {"Guitar": guitar, "Bass": bass, "Drums": drums, "Piano": piano}


def normalize(buf, peak=0.92):
    hi = 0.0
    for v in buf:
        if abs(v) > hi:
            hi = abs(v)
    if hi <= 1e-9:
        return
    k = peak / hi
    for i in range(len(buf)):
        buf[i] *= k


def write_wav(path, buf):
    data = array.array("h", bytes(2 * len(buf)))
    for i, v in enumerate(buf):
        s = int(max(-1.0, min(1.0, v)) * 32767)
        data[i] = s
    if sys.byteorder == "big":  # pragma: no cover - WAV is little endian
        data.byteswap()
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(data.tobytes())


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    stems = build_stems()

    master = new_buf()
    for buf in stems.values():
        for i in range(N):
            master[i] += buf[i] * 0.55
    normalize(master, 0.95)
    for name, buf in stems.items():
        normalize(buf, 0.9)

    write_wav(os.path.join(OUT_DIR, "demo01-master.wav"), master)
    for name, buf in stems.items():
        write_wav(os.path.join(OUT_DIR, f"demo01-{name.lower()}.wav"), buf)

    manifest = {
        "songs": [{
            "id": "demo01",
            "title": "Demo 001 \u2014 four-part sketch",
            "duration": DURATION,
            "master": "demo01-master.wav",
            "stems": {
                "Guitar": "demo01-guitar.wav",
                "Bass": "demo01-bass.wav",
                "Drums": "demo01-drums.wav",
                "Piano": "demo01-piano.wav",
            },
            "notes": "bass+piano 0-8s, drums 8s, guitar 12s, bass run 16s, tutti 24s",
        }]
    }
    with open(os.path.join(OUT_DIR, "manifest.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)

    print(f"wrote {len(stems) + 1} wav files + manifest.json to {OUT_DIR}")


if __name__ == "__main__":
    main()
