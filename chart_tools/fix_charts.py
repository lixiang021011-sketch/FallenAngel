#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""谱面数据修正（作者授权：测试谱可大胆改）。三项：
  ① 去重：同一 (time, lane, type) 的重复音符只留一条（mecha_tribe 有 6 处重复 tap）
  ② flick 补 direction：缺 direction 的 flick 显式写成 "up"（与游戏内默认值一致，但不再靠默认）
  ③ 长按裁剪：duration > --max-hold（默认 4s）的 hold 裁到上限（tell your world 的 15.6s 等）
运行：python -X utf8 chart_tools/fix_charts.py [--max-hold 4] [--dry]
输出：原地改写 Resources/Charts/*.json（原文件先备份到 Logs/chart_fix_backup/）
"""
import glob
import io
import json
import os
import shutil
import sys

CHART_DIR = os.path.join("Assets", "Resources", "Charts")
BACKUP = os.path.join("Logs", "chart_fix_backup")
END_TYPES = ("holdEnd", "longEnd", "LongEnd")


def main():
    max_hold = 4.0
    dry = "--dry" in sys.argv
    if "--max-hold" in sys.argv:
        max_hold = float(sys.argv[sys.argv.index("--max-hold") + 1])

    os.makedirs(BACKUP, exist_ok=True)
    report, changed = [], 0
    for path in sorted(glob.glob(os.path.join(CHART_DIR, "*.json"))):
        chart = json.loads(io.open(path, encoding="utf-8").read())
        notes = chart.get("notes") or []
        name = os.path.basename(path)
        out, seen, dup, dir_fix, cut = [], set(), 0, 0, 0
        for n in notes:
            key = (round(n.get("time", 0), 3), n.get("lane"), n.get("type"))
            if key in seen:
                dup += 1
                continue
            seen.add(key)
            if n.get("type") == "flick" and "direction" not in n:
                n["direction"] = "up"
                dir_fix += 1
            if n.get("type") == "hold" and n.get("duration", 0) > max_hold:
                report.append("  %s：hold t=%.2f %.2fs → %.2fs" % (name, n["time"], n["duration"], max_hold))
                n["duration"] = max_hold
                cut += 1
            out.append(n)

        # 长按尾条目跟着头部一起走（同 longNoteId 的尾时间 = 头 + duration）
        for n in out:
            if n.get("type") != "hold" or (n.get("longNoteId") or -1) < 0:
                continue
            for m in out:
                if m.get("type") in END_TYPES and m.get("longNoteId") == n["longNoteId"]:
                    m["time"] = round(n["time"] + n["duration"], 3)

        if dup or dir_fix or cut:
            changed += 1
            report.append("%s：去重 %d、补 direction %d、裁长按 %d" % (name, dup, dir_fix, cut))
            if not dry:
                shutil.copy2(path, os.path.join(BACKUP, name))
                io.open(path, "w", encoding="utf-8").write(
                    json.dumps(chart, ensure_ascii=False, indent=2) + "\n")

    lines = ["谱面修正%s（max-hold=%.1fs）" % ("（dry-run）" if dry else "", max_hold)] + report
    lines.append("改动文件数：%d（原文件备份在 %s）" % (changed, BACKUP))
    os.makedirs("Logs", exist_ok=True)
    io.open(os.path.join("Logs", "chart_fix.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
