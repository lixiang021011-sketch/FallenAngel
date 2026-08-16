# -*- coding: utf-8 -*-
"""
谱面目检：每小节各轨道音符数、总轨道分布、长按统计
用法: python chart_stats.py <chart.bytes>
"""
import sys
import json
from collections import Counter

path = sys.argv[1] if len(sys.argv) > 1 else "../Assets/Resources/Charts/demo_drums.bytes"
with open(path, encoding="utf-8") as f:
    chart = json.load(f)

notes = chart["notes"]
meta = chart["metadata"]
print(f"chart: {meta['songName']}  level {meta['level']}  bpm {meta['bpm']}  "
      f"notes {len(notes)}  duration {notes[-1]['time']:.1f}s")

# 长按配对校验
starts = [n for n in notes if n["type"] == 1]
ends = [n for n in notes if n["type"] == 3]
print(f"long notes: {len(starts)} (start/end {'OK' if len(starts) == len(ends) else 'MISMATCH'})")
ids_s = Counter(n["longNoteId"] for n in starts)
ids_e = Counter(n["longNoteId"] for n in ends)
for i in ids_s:
    if ids_s[i] != ids_e.get(i, 0):
        print(f"  !! long id {i}: start {ids_s[i]} end {ids_e.get(i, 0)}")

# 轨道分布
lane_c = Counter(n["lane"] for n in notes)
print("lane distribution: " + "  ".join(f"L{i}={lane_c[i]}" for i in range(4)))

# 每 3 秒小节的轨道计数
bar = 3.0
n_bars = int(notes[-1]["time"] / bar) + 1
print(f"{'bar':>4} {'t(s)':>6} | " + " ".join(f"L{i:>3}" for i in range(4)))
for b in range(0, min(n_bars, 30)):
    t0, t1 = b * bar, (b + 1) * bar
    c = Counter(n["lane"] for n in notes if t0 <= n["time"] < t1)
    print(f"{b:4d} {t0:6.1f} | " + " ".join(f"{c[i]:3d}" for i in range(4)))
