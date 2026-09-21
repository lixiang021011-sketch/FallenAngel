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
GROUP_EPS = 0.030      # 同刻分组容差（秒）
MAX_FINGERS = 2        # 玩法硬约束：整谱两指可打


def _is_long(n):
    return n.get("type") in ("hold", "slide") and n.get("duration", 0) > 0


def two_finger_pass(notes, lane_count, report, name):
    """两指可打 + 长音不干扰：
      ① 长音（含滑条）按住期间**占用**自己的轨道：同轨的其他音符改到最近的空闲轨（没空位就丢），
         保证"长按那根手指"不会被别的音符抢判，也不会把同轨音符挤成需要第三根手指；
      ② 同一时刻（±30ms）的落手数 ≤ 2 - 按住中的长音数（长音各占一根手指）。
    """
    notes = sorted(notes, key=lambda n: (n.get("time", 0), n.get("lane", 0)))
    out = []
    active = {}          # lane -> 长音结束时间
    moved = dropped = 0
    i = 0
    while i < len(notes):
        t0 = notes[i].get("time", 0)
        group = []
        while i < len(notes) and notes[i].get("time", 0) - t0 <= GROUP_EPS:
            group.append(notes[i])
            i += 1

        # 先让过期的长音释放轨道
        for lane in [l for l, end in active.items() if end <= t0 + GROUP_EPS]:
            del active[lane]

        kept = []
        used_lanes = set(active.keys())
        # 长音的"静音带"：±1 轨都算被它打扰（作者口径：长音不该干扰其他音符）
        holdband = set()
        for l in active.keys():
            for d in (-1, 0, 1):
                holdband.add(l + d)
        # 长音优先登记（占轨），其余音符让位
        for n in sorted(group, key=lambda x: 0 if _is_long(x) else 1):
            lane = n.get("lane", 0)
            if _is_long(n) and lane in holdband:
                # 相邻轨已有长音在按住：这条长音的时长大到会重叠 → 裁到"不重叠"，裁不掉就丢
                blocked_until = max([active[l] for l in (lane - 1, lane, lane + 1) if l in active] or [0])
                room = blocked_until - n.get("time", 0)
                if room > 0.35:
                    n["duration"] = round(room, 3)
                    report.append("%s：长音重叠 → 轨%d 的 t=%.2f 裁到 %.2fs"
                                  % (name, lane, n["time"], n["duration"]))
                else:
                    dropped += 1
                    continue
            elif lane in holdband and not _is_long(n):
                target = _nearest_free_lane(lane, lane_count, used_lanes)
                if target is not None and target in holdband:
                    target = None            # 不许挪进别的长音的静音带
                if target is None:
                    dropped += 1
                    continue
                n["lane"] = target
                lane = target
                moved += 1
            elif lane in used_lanes and not _is_long(n):
                target = _nearest_free_lane(lane, lane_count, used_lanes)
                if target is None:
                    dropped += 1
                    continue
                n["lane"] = target
                lane = target
                moved += 1
            used_lanes.add(lane)
            kept.append(n)
            if _is_long(n):
                active[lane] = n.get("time", 0) + n.get("duration", 0)
                for d in (-1, 0, 1):
                    holdband.add(lane + d)

        # 手指数上限：按住中的长音各占一根手指
        budget = max(0, MAX_FINGERS - len(active) + sum(1 for n in kept if _is_long(n)))
        if len(kept) > budget:
            dropped += len(kept) - budget
            kept = kept[:budget]
        out.extend(kept)

    if moved or dropped:
        report.append("%s：两指化改轨 %d、丢弃多余落手 %d" % (name, moved, dropped))
    return out, (moved or dropped) > 0


def _nearest_free_lane(lane, lane_count, used):
    for d in range(1, lane_count + 1):
        for cand in (lane + d, lane - d):
            if 0 <= cand < lane_count and cand not in used:
                return cand
    return None


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

        lane_count = 5 if int((chart.get("metadata") or {}).get("formatVersion", 0) or 0) >= 2 else 4
        out, two_finger_changed = two_finger_pass(out, lane_count, report, name)
        # noteCounts 跟着实际条数走（v2 校验字段）
        meta = chart.get("metadata") or {}
        if isinstance(meta.get("noteCounts"), dict):
            actual = {}
            for n in out:
                actual[n.get("type")] = actual.get(n.get("type"), 0) + 1
            for k in meta["noteCounts"]:
                meta["noteCounts"][k] = actual.get(k, 0)
        chart["notes"] = out

        if dup or dir_fix or cut or two_finger_changed:
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
