# -*- coding: utf-8 -*-
"""
按小节渲染乐器活动计数图（用于人耳验证乐器分离是否正确）
用法: python render_instruments.py <analysis.json> <instruments.json>
K=底鼓 S=军鼓 H=镲 | B=贝斯 G=中频旋律 L=高频主音
"""
import sys
import json

ana_path = sys.argv[1] if len(sys.argv) > 1 else "demo_song_analysis.json"
ins_path = sys.argv[2] if len(sys.argv) > 2 else "demo_song_instruments.json"

with open(ana_path, encoding="utf-8") as f:
    ana = json.load(f)
with open(ins_path, encoding="utf-8") as f:
    ins = json.load(f)["instruments"]

beats = ana["beats"]
secs = ana["sections"]

def sec_at(t):
    for s in secs:
        if s["start"] <= t < s["end"]:
            return s["label"]
    return "?"

# 每小节（4拍）统计各乐器起音次数
bars = []
for i in range(0, len(beats), 4):
    t0 = beats[i]["t"]
    t1 = min(beats[i + 3]["t"] + 0.74, ana["duration"]) if i + 3 < len(beats) else ana["duration"]
    counts = {}
    for name, ts in ins.items():
        counts[name] = sum(1 for e in ts if t0 <= e[0] < t1)
    bars.append((i // 4, t0, counts, sec_at(t0)))

print(f"{'bar':>4} {'t(s)':>7} |  K   S   H |  B   G   L | section")
print("=" * 60)
for bar, t0, c, sec in bars:
    print(f"{bar:4d} {t0:7.1f} | {c['kick']:3d} {c['snare']:3d} {c['hihat']:3d} |"
          f" {c['bass']:3d} {c['mid_melody']:3d} {c['high_lead']:3d} | {sec}")

print("=" * 60)
print("K=底鼓 S=军鼓 H=镲 | B=贝斯 G=中频旋律(疑似吉他) L=高频主音(疑似合成器)")
print("检验方法：跟着歌听某个段落，看对应小节的计数是否符合听感。")
