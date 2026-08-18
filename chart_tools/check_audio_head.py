# -*- coding: utf-8 -*-
"""Audio-head check v2: LAME encoder delay/padding, silence vs quiet-intro
profile (per-100ms RMS of first 6s), and wav decode with fallbacks."""
import os
import struct
import numpy as np
import miniaudio

MP3 = r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\Assets\Resources\Audio\demo_song.mp3"
WAV = os.path.join(r"C:\Users\LorXer", "Desktop", "音游", "demo_song.wav")
THRESH_LO = 50   # near-digital-silence floor
THRESH_HI = 500  # audible-music floor

def load_pcm(path):
    """Return float32 mono 44100 samples; tries miniaudio then stdlib wave."""
    try:
        dec = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.SIGNED16,
                                    nchannels=1, sample_rate=44100)
        s = dec.samples
        if not isinstance(s, np.ndarray):
            s = np.frombuffer(s, dtype=np.int16)
        return s.astype(np.float32) / 32768.0, dec.sample_rate
    except Exception as e:
        print("   miniaudio failed (%s), trying stdlib wave..." % e)
        import wave
        with wave.open(path, "rb") as w:
            sr = w.getframerate()
            n = w.getnframes()
            raw = w.readframes(n)
            wav = np.frombuffer(raw, dtype=np.int16)
            if w.getnchannels() == 2:
                wav = wav.reshape(-1, 2).mean(axis=1)
            return wav.astype(np.float32) / 32768.0, sr

def first_sound(x, thresh):
    hit = np.flatnonzero(np.abs(x) > thresh)
    return None if len(hit) == 0 else float(hit[0])

with open(MP3, "rb") as f:
    head = f.read(65536)
pos = head.find(b"LAME")
if pos >= 0:
    tag_start = pos - 9  # LAME tag: encoder string sits at tag offset 9
    delay = int.from_bytes(head[tag_start + 21:tag_start + 24], "big")
    padding = int.from_bytes(head[tag_start + 24:tag_start + 27], "big")
    print("LAME tag: encoder_delay=%d samples (%.1fms @44.1k)  padding=%d (%.1fms)"
          % (delay, delay / 44.1, padding, padding / 44.1))
else:
    print("LAME tag: not found")

for path in (MP3, WAV):
    print("==", os.path.basename(path), "exists:", os.path.exists(path))
    if not os.path.exists(path):
        continue
    x, sr = load_pcm(path)
    total = len(x) / sr
    print("   total=%.3fs" % total)
    print("   first >%d : %s" % (THRESH_HI,
          "%.1fms" % (first_sound(x, THRESH_HI) / sr * 1000) if first_sound(x, THRESH_HI) is not None else "never"))
    print("   first >%d : %s" % (THRESH_LO,
          "%.1fms" % (first_sound(x, THRESH_LO) / sr * 1000) if first_sound(x, THRESH_LO) is not None else "never"))
    # per-100ms RMS of the first 6 seconds: silence vs quiet music
    win = int(sr * 0.1)
    rms = [float(np.sqrt(np.mean(x[i:i + win] ** 2))) for i in range(0, int(sr * 6), win)]
    print("   RMS/100ms (first 6s): " + " ".join("%.3f" % v for v in rms))
