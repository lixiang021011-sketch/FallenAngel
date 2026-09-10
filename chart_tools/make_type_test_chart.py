#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成"五类型测试谱"：用 Phigros 格式写源谱 → 走 chart_import 转换 → 注入 slide → 落进 Resources。

目的：不改游戏、不依赖第三方谱，就能在游戏里验 tap / hold / drag / flick↑↓ / slide 五种音符。
音频复用现有 demo_song；flick 用 alternate 保证上下方向各半。

用法：python -X utf8 chart_tools/make_type_test_chart.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from chart_import import parse_phigros, validate   # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Charts", "types_test.json")
BPM = 120.0


def phigros_fixture():
    """16 小节、每拍一个音符：2 拍 tap / 1 拍 hold 交替，穿插 drag，尾部 flick 交替上下。"""
    above, below = [], []
    lane_x = [-1.0, -0.5, 0.0, 0.5, 1.0]
    for beat in range(0, 64):
        target = above if beat % 2 == 0 else below
        x = lane_x[(beat // 2) % 5] if beat % 2 == 0 else lane_x[4 - (beat // 2) % 5]
        if beat % 16 == 15:                       # 每 4 小节来一个长按（2 拍）
            target.append({"type": 3, "time": beat, "positionX": x, "holdTime": 2.0})
        elif beat % 8 == 6:                       # 每 2 小节一个 drag
            target.append({"type": 2, "time": beat, "positionX": x, "holdTime": 0.0})
        elif beat % 8 == 7:                       # 以及一个 flick（方向由 alternate 决定）
            target.append({"type": 4, "time": beat, "positionX": x, "holdTime": 0.0})
        else:
            target.append({"type": 1, "time": beat, "positionX": x, "holdTime": 0.0})
    return {
        "formatVersion": 3,
        "offset": 0.0,
        "judgeLineList": [{"bpm": BPM, "notesAbove": above, "notesBelow": below}],
    }


def inject_slides(chart):
    """把 6 个 tap 改成跨轨滑条（x 从起点轨线性推到终点轨），用于验证 slide 数据与渲染。"""
    taps = [n for n in chart["notes"] if n["type"] == "tap"]
    picked = taps[::max(1, len(taps) // 6)][:6]
    for i, note in enumerate(picked):
        start = note["lane"]
        end = (start + (4 if i % 2 == 0 else -3)) % 5      # 左右交替跨轨
        note["type"] = "slide"
        note["duration"] = 1.0
        note["path"] = [{"t": 0.0, "x": float(start)}, {"t": 1.0, "x": float(end)}]
    return len(picked)


def main():
    chart = parse_phigros(json.dumps(phigros_fixture()), flick_direction="alternate")
    chart["metadata"]["songName"] = "五类型测试"
    chart["metadata"]["songArtist"] = "FallenAngel"
    chart["metadata"]["chartAuthor"] = "chart_tools"
    chart["metadata"]["audioFileName"] = "demo_song"
    chart["metadata"]["level"] = 1
    slides = inject_slides(chart)
    from chart_import import finish
    finish(chart)
    problems = validate(chart)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(chart, handle, ensure_ascii=False, indent=2)
    counts = chart["noteCounts"]
    print("已生成 %s" % os.path.relpath(OUT, ROOT))
    print("  音符 %d  counts=%s（注入 slide %d 个）" % (len(chart["notes"]), counts, slides))
    for problem in problems[:5]:
        print("  ⚠ " + problem)


if __name__ == "__main__":
    main()
