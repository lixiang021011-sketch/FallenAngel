# -*- coding: utf-8 -*-
"""
按小节渲染拍点强度图（ASCII），辅助谱面设计讨论
用法: python render_map.py <analysis.json> [bpm]
输出纯 ASCII
"""
import sys
import json

path = sys.argv[1] if len(sys.argv) > 1 else "demo_song_analysis.json"
with open(path, encoding="utf-8") as f:
    rep = json.load(f)

beats = rep["beats"]
secs = rep["sections"]

# 强度分级: s>=1.5 '#', >=0.8 '+', >=0.35 'o', else '.'
def ch(s):
    if s >= 1.5: return '#'
    if s >= 0.8: return '+'
    if s >= 0.35: return 'o'
    return '.'

# 段落查找
def sec_at(t):
    for s in secs:
        if s["start"] <= t < s["end"]:
            return s["label"]
    return "?"

print(f"BPM {rep['bpm']}  duration {rep['duration']}s  beats {rep['beatCount']}")
print("=" * 78)
print(f"{'bar':>4} {'t(s)':>7} | 1 2 3 4 | strength | section")
line = ""
bar = 0
for i, b in enumerate(beats):
    pos_in_bar = i % 4
    if pos_in_bar == 0:
        bar = i // 4
        line = f"{bar:4d} {b['t']:7.1f} | "
    line += ch(b["s"]) + " "
    if pos_in_bar == 3:
        avg = sum(x["s"] for x in beats[i-3:i+1]) / 4
        line += f"| {avg:6.2f}   | {sec_at(b['t'])}"
        print(line)
# 不足一拍的尾
if len(beats) % 4:
    print(line)
print("=" * 78)
print("legend: # strong  + medium  o weak  . empty")
