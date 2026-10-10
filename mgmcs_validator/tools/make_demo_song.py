"""Write the demo click track and the JSON that goes with it.

Python's standard library only, on purpose: this file exists so the tool can be
handed to somebody with a matching pair of files to try, and a demo that needs
packages installed is a demo that does not get run.

The track is a plain 120 BPM click in 4/4, 32 seconds long, with a higher click
on each bar line. That makes the spec's own acceptance tests reproducible by ear:

  Test A  one calibration on a bar line   -> a click every 0.5 s, a bar every 2 s
  Test B  a second calibration 1 s later  -> 2 beats, half a bar, off the first
  Test C  a 3/4 grid at the same tempo    -> same clicks, different bar lines
  Test D  a second identical 4/4 grid     -> four lanes, none of them replaced

Run:  python tools/make_demo_song.py
"""

import json
import math
import os
import struct
import wave

SAMPLE_RATE = 44100
BPM = 120
BEATS_PER_BAR = 4
BARS = 16
SECONDS = BARS * BEATS_PER_BAR * 60 / BPM  # 32.0
FIRST_BARLINE = 2.0  # the click track starts on a bar line, so a calibration can too


def click(length_seconds, frequency, decay):
    count = int(SAMPLE_RATE * length_seconds)
    out = []
    for i in range(count):
        t = i / SAMPLE_RATE
        envelope = math.exp(-t / decay)
        out.append(0.6 * envelope * math.sin(2 * math.pi * frequency * t))
    return out


def render():
    total = int(SAMPLE_RATE * SECONDS)
    samples = [0.0] * total
    beat_seconds = 60.0 / BPM
    click_beat = click(0.05, 1000.0, 0.012)
    click_bar = click(0.09, 1600.0, 0.020)

    beats = int(BARS * BEATS_PER_BAR)
    for beat in range(beats):
        at = FIRST_BARLINE + beat * beat_seconds
        on_barline = beat % BEATS_PER_BAR == 0
        burst = click_bar if on_barline else click_beat
        # The click that starts bar 0 sits 2 s in, which is also where the first
        # calibration goes, so "the calibration point" can be heard, not assumed.
        for i, value in enumerate(burst):
            index = int(at * SAMPLE_RATE) + i
            if 0 <= index < total:
                samples[index] += value

    return samples


def write_wav(path, samples):
    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(SAMPLE_RATE)
        frames = bytearray()
        for value in samples:
            clipped = max(-1.0, min(1.0, value))
            frames += struct.pack("<h", int(clipped * 32000))
        out.writeframes(bytes(frames))


def demo_document():
    """Four calibrations, so the spec's four tests are all one file away."""
    return {
        "schemaVersion": "0.1",
        "demo": True,
        "song": {
            "songId": "demo_120",
            "title": "120 BPM 节拍器（验证器演示）",
            "audioFileName": "demo_120.wav",
            "durationSeconds": SECONDS,
        },
        "calibrations": [
            {
                "calibrationId": "cal_A",
                "audioTime": FIRST_BARLINE,
                "bpm": BPM,
                "beatsPerBar": 4,
                "beatUnit": 4,
                "source": "human",
                "note": "Test A/C/D：对着第一声重拍",
            },
            {
                "calibrationId": "cal_B",
                "audioTime": FIRST_BARLINE + 1.0,
                "bpm": BPM,
                "beatsPerBar": 4,
                "beatUnit": 4,
                "source": "human",
                "note": "Test B：比 cal_A 晚 1 秒 = 2 拍 = 半小节",
            },
            {
                "calibrationId": "cal_C",
                "audioTime": FIRST_BARLINE,
                "bpm": BPM,
                "beatsPerBar": 3,
                "beatUnit": 4,
                "source": "human",
                "note": "Test C：同速度不同拍号，拍点应当重合",
            },
            {
                "calibrationId": "cal_D",
                "audioTime": FIRST_BARLINE,
                "bpm": BPM,
                "beatsPerBar": 4,
                "beatUnit": 4,
                "source": "human",
                "note": "Test D：和 cal_A 一模一样，但必须作为独立网格存在",
            },
        ],
        "events": [
            {"eventId": "evt_001", "audioTime": 6.25, "type": "guitar_attention_in",
             "source": "demo", "payload": {"part": "guitar"}},
            {"eventId": "evt_002", "audioTime": 12.75, "type": "guitar_attention_out",
             "source": "demo", "payload": {"part": "guitar"}},
            {"eventId": "evt_003", "audioTime": 14.0, "type": "bass_attention_in",
             "source": "demo", "payload": {"part": "bass"}},
            {"eventId": "evt_004", "audioTime": 20.0, "type": "drum_attention_in",
             "source": "demo", "payload": {"part": "drums"}},
            {"eventId": "evt_005", "audioTime": 24.5, "type": "drum_attention_out",
             "source": "demo", "payload": {"part": "drums"}},
            {"eventId": "evt_006", "audioTime": FIRST_BARLINE + 1.0, "type": "half_bar",
             "source": "demo",
             "payload": {"note": "演示：cal_B 相对 cal_A 的半小节假设"}},
        ],
    }


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    demo = os.path.join(os.path.dirname(here), "demo")
    os.makedirs(demo, exist_ok=True)

    wav_path = os.path.join(demo, "demo_120.wav")
    json_path = os.path.join(demo, "demo_120.mgmcs.json")
    write_wav(wav_path, render())
    with open(json_path, "w", encoding="utf-8") as out:
        json.dump(demo_document(), out, ensure_ascii=False, indent=2)
        out.write("\n")
    print("wrote", wav_path)
    print("wrote", json_path)
    print("%.1f s, %d BPM, %d bars of 4/4" % (SECONDS, BPM, BARS))


if __name__ == "__main__":
    main()
