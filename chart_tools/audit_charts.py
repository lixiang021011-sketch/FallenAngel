#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""谱面层体检（只读）：把 Resources/Charts/*.json 里的"可疑项"一次列全。

为什么需要：作者反馈"谱面层 bug 不多但会有"（例如 tell your world 的 15.6s 长条）。
逐个人眼看不现实，这里把可判定的规则做成清单，导入新谱后跑一遍即可。

检查项：
  ① 长按过长（> MAX_HOLD_S，默认 4s）：多半是 SUS 长按头/尾配对错位（头丢了、尾配到了很久之后的尾）
  ② 音符超出音频长度 / 音频缺失
  ③ 轨道越界（lane 不在 [0, laneCount-1]）
  ④ 时间未排序 / 同时同轨重复（同一时刻同一轨两条 tap）
  ⑤ slide 路径：t 非单调递增、x 越界、首点 t≠0 或末点 t≠duration
  ⑥ flick 方向非法
  ⑦ noteCounts 与实际条数不符（v2 校验字段）

运行：python -X utf8 chart_tools/audit_charts.py [--max-hold 4]
输出：Logs/chart_audit.txt（同时打印到屏幕）
"""
import glob
import io
import json
import os
import sys

MAX_HOLD_DEFAULT = 4.0
CHART_DIR = os.path.join("Assets", "Resources", "Charts")
AUDIO_DIR = os.path.join("Assets", "Resources", "Audio")
OUT = os.path.join("Logs", "chart_audit.txt")


def load(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def audio_length(name):
    """音频长度：优先读同名 .ogg/.wav/.mp3 的 sidecar（若存在 .len 文件），否则返回 None。"""
    if not name:
        return None
    for ext in (".ogg", ".wav", ".mp3"):
        p = os.path.join(AUDIO_DIR, name + ext)
        if os.path.exists(p):
            sidecar = p + ".len"
            if os.path.exists(sidecar):
                try:
                    return float(open(sidecar).read().strip())
                except ValueError:
                    return None
            return None
    return "missing"


def audit(path, max_hold):
    issues = []
    name = os.path.basename(path)
    try:
        chart = load(path)
    except Exception as e:  # noqa: BLE001
        return ["%s 解析失败：%s" % (name, e)], 0

    meta = chart.get("metadata") or {}
    notes = chart.get("notes") or []
    lane_count = 5 if int(meta.get("formatVersion", 0) or 0) >= 2 else 4

    if meta.get("audioFileName"):
        length = audio_length(meta["audioFileName"])
        if length == "missing":
            issues.append("音频缺失：%s" % meta["audioFileName"])
        elif isinstance(length, float):
            late = [n for n in notes if n.get("time", 0) > length + 0.25]
            if late:
                issues.append("有 %d 条音符超出音频长度 %.1fs（最晚 %.2fs）"
                              % (len(late), length, max(n["time"] for n in late)))

    prev = None
    for n in notes:
        t = n.get("time", 0)
        if prev is not None and t < prev - 1e-6:
            issues.append("时间未排序（%.3f 之后出现 %.3f）" % (prev, t))
            break
        prev = t

    for n in notes:
        lane = n.get("lane", 0)
        if not (0 <= lane < lane_count):
            issues.append("轨道越界：lane=%s（%d 键谱）time=%.3f" % (lane, lane_count, n.get("time", 0)))

    longest = sorted((n for n in notes if n.get("type") == "hold"),
                     key=lambda n: -n.get("duration", 0))[:3]
    for n in longest:
        if n.get("duration", 0) > max_hold:
            issues.append("长按过长 %.2fs（t=%.2f lane=%d，> %.1fs 需人工确认是谱面设计还是配对错位）"
                          % (n["duration"], n["time"], n["lane"], max_hold))

    seen = {}
    missing_dir = 0
    # 玩法硬约束：整谱两指可打 + 长音不干扰其他音符
    EPS = 0.030
    sorted_notes = sorted(notes, key=lambda n: n.get("time", 0))
    active = {}
    j = 0
    while j < len(sorted_notes):
        t0 = sorted_notes[j].get("time", 0)
        group = []
        while j < len(sorted_notes) and sorted_notes[j].get("time", 0) - t0 <= EPS:
            group.append(sorted_notes[j])
            j += 1
        for lane in [l for l, end in active.items() if end <= t0 + EPS]:
            del active[lane]
        long_here = [n for n in group if n.get("type") in ("hold", "slide") and n.get("duration", 0) > 0]
        # 手指数 = 仍在按住的长音（active）+ 这一簇的落手（long_here 也在 group 里）
        fingers = len(active) - len(long_here) + len(group)
        if fingers > 2:
            issues.append("同时刻需要 %d 根手指 > 2（%s @ %.3f）——两指打不了" %
                          (fingers, "+".join(n.get("type", "?") for n in group), t0))
        for n in group:
            lane = n.get("lane", 0)
            if lane in active and n.get("type") not in ("hold", "slide"):
                issues.append("长音干扰：lane %d 正在长按（到 %.2fs），但 %.3fs 有 %s"
                              % (lane, active[lane], n.get("time", 0), n.get("type")))
        for n in long_here:
            active[n.get("lane", 0)] = n.get("time", 0) + n.get("duration", 0)

    for n in notes:
        key = (round(n.get("time", 0), 3), n.get("lane", 0), n.get("type"))
        if key in seen:
            issues.append("同时同轨重复：%s @ %.3f lane=%s" % (n.get("type"), n["time"], n["lane"]))
        seen[key] = True

    for n in notes:
        if n.get("type") == "slide":
            pts = n.get("path") or []
            if len(pts) < 2:
                issues.append("slide 路径点不足：t=%.3f" % n["time"])
                continue
            ts = [p.get("t", 0) for p in pts]
            xs = [p.get("x", 0) for p in pts]
            if any(ts[i + 1] < ts[i] - 1e-6 for i in range(len(ts) - 1)):
                issues.append("slide 路径 t 非单调：t=%.3f" % n["time"])
            if any(not (0 <= x <= lane_count - 1) for x in xs):
                issues.append("slide 路径 x 越界：t=%.3f x=%s" % (n["time"], xs))
            if abs(ts[0]) > 1e-3 or abs(ts[-1] - n.get("duration", 0)) > 0.05:
                issues.append("slide 首末点与 duration 不匹配：t=%.3f dur=%.3f 路径末=%.3f"
                              % (n["time"], n.get("duration", 0), ts[-1]))
        if n.get("type") == "flick" and "direction" not in n:
            missing_dir += 1
        elif n.get("type") == "flick" and n.get("direction") not in ("up", "down"):
            issues.append("flick 方向非法：%s @ %.3f" % (n.get("direction"), n["time"]))

    if missing_dir:
        issues.append("有 %d 条 flick 没写 direction（游戏内按默认 up 处理；若是下划甩键会静默变上划，"
                      "建议导入器/编辑器始终写出该字段）" % missing_dir)

    counts = meta.get("noteCounts")
    if isinstance(counts, dict):
        actual = {}
        for n in notes:
            actual[n.get("type")] = actual.get(n.get("type"), 0) + 1
        for k, v in counts.items():
            if v and actual.get(k, 0) != v:
                issues.append("noteCounts.%s=%s 与实际 %s 不符" % (k, v, actual.get(k, 0)))

    return issues, len(notes)


def main():
    max_hold = MAX_HOLD_DEFAULT
    if "--max-hold" in sys.argv:
        max_hold = float(sys.argv[sys.argv.index("--max-hold") + 1])

    lines = ["谱面层体检（max-hold=%.1fs）" % max_hold]
    total = 0
    bad = 0
    for path in sorted(glob.glob(os.path.join(CHART_DIR, "*.json"))):
        issues, n = audit(path, max_hold)
        total += n
        head = "%-32s 音符 %4d" % (os.path.basename(path), n)
        if issues:
            bad += 1
            lines.append(head + "   ⚠ %d 项" % len(issues))
            for i in issues:
                lines.append("    - " + i)
        else:
            lines.append(head + "   ok")
    lines.append("合计：%d 个谱面 / %d 条音符，%d 个谱面有可疑项" % (
        len(glob.glob(os.path.join(CHART_DIR, "*.json"))), total, bad))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
