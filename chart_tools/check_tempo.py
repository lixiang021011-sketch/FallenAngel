# -*- coding: utf-8 -*-
"""
节奏候选对比：对多个候选 BPM 计算拍点网格得分，判断真实速度
（半速/倍速陷阱在电子乐中很常见，如 80 vs 160）
用法: python check_tempo.py <音频路径>
输出纯 ASCII，避免控制台编码问题
"""
import sys
import numpy as np
sys.path.insert(0, '.')
import analyze

path = sys.argv[1] if len(sys.argv) > 1 else "../Assets/Resources/Audio/demo_song.mp3"

print("decoding...")
samples, sr = analyze.decode_mono(path)
mags = analyze.stft_mag(samples)
env = analyze.onset_flux(mags)
print(f"duration {len(samples)/sr:.1f}s, env frames {len(env)}")

hop = 512
results = []
for bpm in np.arange(55, 200, 1):
    fpb = (sr / hop) / (bpm / 60.0)
    if fpb < 2:
        continue
    best, bestphase = -1e9, 0.0
    for phase in np.arange(0, fpb, fpb / 64):
        idx = np.arange(phase, len(env), fpb).astype(int)
        idx = idx[idx < len(env) - 1]
        if len(idx) < 4:
            continue
        s = float(env[idx].mean())
        if s > best:
            best, bestphase = s, float(phase)
    results.append((float(bpm), best, bestphase))

results.sort(key=lambda x: -x[1])
print("\nTop 12 BPM candidates (score = avg onset strength on beat grid):")
for bpm, s, p in results[:12]:
    # 一拍帧数对应的相位换算成毫秒
    fpb = (sr / hop) / (bpm / 60.0)
    phase_ms = p / (sr / hop) * 1000.0
    print(f"  BPM {bpm:6.1f}  score {s:6.3f}  phase {phase_ms:7.1f} ms")
