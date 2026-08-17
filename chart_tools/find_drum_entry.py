# -*- coding: utf-8 -*-
"""
定位进鼓瞬间：自由节拍前奏结束、160BPM 稳定鼓点开始的精确时刻
方法：谱通量起音包络 → 主段 BPM 验证 → 用 160BPM 网格从主段回溯扫描
     找到最早的"网格对齐持续成立"起点 → 取该处第一个强起音为进鼓点
用法: python find_drum_entry.py
"""
import sys, os
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analyze as ana

MP3 = r'C:\Users\LorXer\Documents\trae_projects\FallenAngel\Assets\Resources\Audio\demo_song.mp3'
BPM = 160.0
BEAT = 60.0 / BPM          # 0.375 s
TOL = 0.05                 # 网格对齐容差 ±50ms

samples, sr = ana.decode_mono(MP3)
print(f'audio: {len(samples)/sr:.2f}s, sr={sr}')
mags = ana.stft_mag(samples)
env = ana.onset_flux(mags)
hop = 512
n = len(env)
t_of = lambda i: i * hop / sr
i_of = lambda t: int(t * sr / hop)

# 起音峰值（阈值 2.0 = 中值 2 倍，不应期 80ms）
def pick_peaks(env, thr, refr):
    out = []
    i = 0
    while i < n - 1:
        if env[i] > thr:
            j = i
            while j + 1 < n and env[j + 1] > env[j]:
                j += 1
            out.append((j, env[j]))
            i = j + refr
        else:
            i += 1
    return out

onsets = pick_peaks(env, 2.0, int(0.08 * sr / hop))
print(f'onsets: {len(onsets)}')

# 1) 主段 BPM 验证（60-120s 应为 160）
env_main = env[i_of(60):i_of(120)]
bpm_est, score, phase = ana.estimate_bpm(env_main, sr, hop)
print(f'main-section BPM estimate: {bpm_est:.2f} (score {score:.0f})')

# 2) 前奏段检查（0-16s）：起音间隔应无规律
intro_ons = [t_of(i) for i, v in onsets if t_of(i) < 16.0]
if len(intro_ons) > 3:
    iv = np.diff(intro_ons)
    print(f'intro (0-16s): {len(intro_ons)} onsets, intervals mean={iv.mean():.3f}s std={iv.std():.3f}s (无规律=自由节拍)')

# 3) 网格对齐扫描：对每个候选 t0，检查其后 12s 内 160BPM 网格点附近有无起音
def grid_hits(t0, dur=12.0, tol=TOL):
    grid = t0 + np.arange(int(dur / BEAT) + 1) * BEAT
    hits = 0; strength = 0.0; n_ok = 0
    for g in grid:
        if g >= len(env) * hop / sr:
            break
        n_ok += 1
        seg = env[i_of(g - tol):i_of(g + tol)]
        if seg.size and seg.max() > 2.0:
            hits += 1
            strength += seg.max()
    return hits, n_ok, strength

print('\n扫描 t0 (12-26s, 步进20ms), 窗口12s, 命中率>=60% 视为稳定进鼓:')
candidates = []
for t0 in np.arange(12.0, 26.0, 0.02):
    hits, n_ok, strength = grid_hits(t0)
    if n_ok > 10 and hits / n_ok >= 0.60:
        candidates.append((t0, hits, n_ok))
if candidates:
    t_earliest = candidates[0][0]
    print(f'  最早的稳定网格起点: {t_earliest:.2f}s (命中 {candidates[0][1]}/{candidates[0][2]})')
    # 连续区间
    runs = []
    run_start = candidates[0]
    prev = candidates[0][0]
    for c in candidates[1:]:
        if c[0] - prev <= 0.021:
            prev = c[0]
        else:
            runs.append((run_start[0], prev)); run_start = c; prev = c[0]
    runs.append((run_start[0], prev))
    print('  稳定区间:', [(round(a,2), round(b,2)) for a, b in runs])
else:
    print('  未找到稳定网格起点！')
    t_earliest = None

# 4) 精确定位进鼓瞬间：t_earliest 前 0.8s 起、后 2s 内的第一个强起音（>=3.0）
if t_earliest:
    window = [(t_of(i), v) for i, v in onsets if t_earliest - 0.8 <= t_of(i) <= t_earliest + 2.0]
    entry = None
    for t, v in window:
        if v >= 3.0:
            entry = t
            break
    if entry is None and window:
        entry = window[0][0]
    print(f'\n进鼓瞬间 T = {entry:.3f}s ({entry*1000:.0f}ms)')
    # 对齐检验：进鼓后前 8 拍
    grid8 = entry + np.arange(9) * BEAT
    print('进鼓后前 8 拍 vs 网格:')
    for k, g in enumerate(grid8):
        seg = env[i_of(g - TOL):i_of(g + TOL)]
        ok = seg.size and seg.max() > 2.0
        print(f'  拍{k}: 网格 {g:.3f}s {"✓" if ok else "✗ 无起音"}')
    # 该时刻对应谱面 m13 的偏移量
    grid_m13 = 18.0  # m13 = 12小节 × 1.5s
    offset_ms = int(round((grid_m13 - entry) * 1000))
    print(f'\n谱面 m13 网格时刻 = {grid_m13:.3f}s')
    print(f'音频进鼓 = {entry:.3f}s')
    print(f'→ Song Properties Offset = {offset_ms} ms ({"正值=音频后移" if offset_ms >= 0 else "负值=音频前移"})')
