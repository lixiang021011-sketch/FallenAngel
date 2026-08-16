# -*- coding: utf-8 -*-
"""
逐 16 分格详细乐器图（人耳验证用）
用法: python render_detailed.py <instruments.json> <analysis.json>
每行 = 一个 80BPM 小节(3秒) = 32 个 160BPM 16 分格
每格显示优先级最高的乐器: K底鼓 S军鼓 H镲 B贝斯 G中频 L高频 .空
"""
import sys
import json

ins_path = sys.argv[1] if len(sys.argv) > 1 else "demo_song_instruments.json"
ana_path = sys.argv[2] if len(sys.argv) > 2 else "demo_song_analysis.json"

with open(ins_path, encoding="utf-8") as f:
    rep = json.load(f)
with open(ana_path, encoding="utf-8") as f:
    ana = json.load(f)

instruments = rep["instruments"]
grid = rep["grid"]
phase = grid["phase"]
tick = 60.0 / grid["bpm"] / 4.0

# 每个格点上出现的乐器集合（事件为 [t, score] 对）
slots = {}
for name in ["kick", "snare", "hihat", "bass", "mid_melody", "high_lead"]:
    for e in instruments[name]:
        t = e[0]
        k = round((t - phase) / tick)
        slots.setdefault(k, []).append(name)

prio = ["kick", "snare", "hihat", "bass", "mid_melody", "high_lead"]
sym = {"kick": "K", "snare": "S", "hihat": "H", "bass": "B",
       "mid_melody": "G", "high_lead": "L"}

beats = ana["beats"]
secs = ana["sections"]

def sec_at(t):
    for s in secs:
        if s["start"] <= t < s["end"]:
            return s["label"]
    return "?"

print(f"grid: {grid['bpm']} BPM / {grid['division']}th, tick {tick*1000:.1f}ms, phase {phase}s")
print("each bar = 3s = 32 slots; slot = instrument with highest priority")
print("=" * 90)

# 每 80 小节 = 3 秒 = 32 格
bar_len_s = 3.0
n_bars = int(ana["duration"] / bar_len_s) + 1
for bar in range(n_bars):
    t0 = phase + bar * bar_len_s
    k0 = round((t0 - phase) / tick)
    line = ""
    for s in range(32):
        names = slots.get(k0 + s, [])
        ch = "."
        for p in prio:
            if p in names:
                ch = sym[p]
                break
        line += ch
    # 统计
    counts = {}
    for name in ["kick", "snare", "hihat", "bass", "mid_melody", "high_lead"]:
        counts[name] = sum(1 for e in instruments[name] if t0 <= e[0] < t0 + bar_len_s)
    print(f"{bar:3d} {t0:6.1f} | {line} | K{counts['kick']:2d} S{counts['snare']:2d} "
          f"H{counts['hihat']:2d} B{counts['bass']:2d} G{counts['mid_melody']:2d} "
          f"L{counts['high_lead']:2d} | {sec_at(t0)}")

print("=" * 90)
print("K=底鼓 S=军鼓 H=镲 B=贝斯 G=中频旋律(疑似吉他) L=高频主音(疑似合成器)")
