#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 `part_runs.jsonl`（GameManager 曲终落盘的试玩记录）汇总成对比表。

一行一局；再按**道具**分组，看切换次数、路线、各段命中率与能量。
试玩前就能用：`--demo` 用合成数据把报告格式先跑一遍。

数据来源（每局一行 JSON，由 `PartRunLog` 追加）：
  {timestamp, song, partItem, duration, switches, energy, hits, misses, lifeLost,
   segments:[{part,start,end,offered,hits,misses,perfect,great,good,energy,lifeLost}]}

默认路径：%USERPROFILE%\\AppData\\LocalLow\\LorXer\\FallenAngel\\part_runs.jsonl
（手机/打包版：把 persistentDataPath 下的 part_runs.jsonl 拷回来放到同一路径，或直接传参）
安卓取回（USB 调试）：
  adb pull /storage/emulated/0/Android/data/com.lorxer.fallenangel/files/part_runs.jsonl

用法：
  python -X utf8 tools/playtest_summary.py                 # 读默认路径，打印报告
  python -X utf8 tools/playtest_summary.py path/to/part_runs.jsonl
  python -X utf8 tools/playtest_summary.py --demo          # 合成 6 局，先看格式
  python -X utf8 tools/playtest_summary.py --md out.md     # 报告同时存成 markdown
  python -X utf8 tools/playtest_summary.py --png out.png   # 再画一张对照图（有 Pillow 时）
  python -X utf8 tools/playtest_summary.py --selftest      # 自检聚合逻辑
"""
import argparse
import json
import os
import sys
from collections import OrderedDict

DEFAULT_PATH = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow",
                            "LorXer", "FallenAngel", "part_runs.jsonl")


# ── 读数据 ──────────────────────────────────────────────────────────────
def load_runs(path):
    runs, bad = [], 0
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                record = json.loads(line)
            except json.JSONDecodeError:
                bad += 1
                continue
            if isinstance(record, dict) and isinstance(record.get("segments"), list):
                runs.append(record)
            else:
                bad += 1
    return runs, bad


# ── 单段派生量 ──────────────────────────────────────────────────────────
def seg_seconds(seg):
    return max(0.0, float(seg.get("end", 0)) - float(seg.get("start", 0)))


def seg_hit_rate(seg):
    offered = int(seg.get("offered", 0))
    return None if offered <= 0 else int(seg.get("hits", 0)) / float(offered)


def seg_density(seg):
    """理论产率：该声部在这段里每秒供给多少颗（密度地形）。"""
    seconds = seg_seconds(seg)
    return None if seconds <= 0.01 else int(seg.get("offered", 0)) / seconds


def seg_rate(seg):
    """实收产率：该段每秒真的赚到多少能量（命中表现 × 密度）。"""
    seconds = seg_seconds(seg)
    return None if seconds <= 0.01 else float(seg.get("energy", 0)) / seconds


def run_hit_rate(run):
    offered = sum(int(s.get("offered", 0)) for s in run["segments"])
    hits = sum(int(s.get("hits", 0)) for s in run["segments"])
    return None if offered <= 0 else hits / float(offered)


def route_of(run):
    return " → ".join("%s %.1f–%.1fs" % (s.get("part", "?"), float(s.get("start", 0)),
                                         float(s.get("end", 0))) for s in run["segments"])


def item_of(run):
    return run.get("partItem") or "(未记录道具)"


# ── 报告 ────────────────────────────────────────────────────────────────
def fmt(value, digits=2, suffix=""):
    if value is None:
        return "—"
    if isinstance(value, float):
        text = ("%%.%df" % digits) % value
    else:
        text = str(value)
    return text + suffix


def fmt_pct(rate, digits=0):
    """比率 → 百分数文本（None 显示 —）。0.72 → '72%'"""
    if rate is None:
        return "—"
    return ("{0:." + str(digits) + "f}%").format(rate * 100.0)


def markdown_report(runs, bad_lines=0):
    lines = []
    total_seconds = sum(float(r.get("duration", 0)) for r in runs)
    lines.append("# 试玩汇总（part_runs.jsonl）")
    lines.append("")
    lines.append("- 局数：%d，总演奏时长 %.1f 秒，道具分组 %d 组%s"
                 % (len(runs), total_seconds, len(group_by_item(runs)),
                    "，另有 %d 行无法解析" % bad_lines if bad_lines else ""))
    if runs:
        lines.append("- 时间范围：%s ~ %s" % (runs[0].get("timestamp", "?"), runs[-1].get("timestamp", "?")))
    lines.append("")

    # 一行一局
    lines.append("## 一行一局")
    lines.append("")
    lines.append("| # | 道具 | 时长 s | 切换 | 总能量 | 能量/秒 | 命中率 | 失误 | 失血 | 路线 |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for index, run in enumerate(runs, 1):
        seconds = max(0.01, float(run.get("duration", 0)))
        lines.append("| %d | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            index, item_of(run), fmt(float(run.get("duration", 0)), 1),
            run.get("switches", 0), fmt(float(run.get("energy", 0)), 1),
            fmt(float(run.get("energy", 0)) / seconds, 2),
            fmt_pct(run_hit_rate(run)),
            run.get("misses", 0), run.get("lifeLost", 0), route_of(run)))
    lines.append("")

    # 按道具分组
    lines.append("## 按道具分组（同一首歌、同一玩家，换道具路线是否变）")
    lines.append("")
    lines.append("| 道具 | 局数 | 平均切换 | 平均能量/秒 | 平均命中率 | 平均失血 | 声部停留占比（时间） |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- |")
    for item, group in group_by_item(runs).items():
        seconds = sum(max(0.01, float(r.get("duration", 0))) for r in group)
        energy = sum(float(r.get("energy", 0)) for r in group)
        offered = sum(int(s.get("offered", 0)) for r in group for s in r["segments"])
        hits = sum(int(s.get("hits", 0)) for r in group for s in r["segments"])
        life = sum(int(r.get("lifeLost", 0)) for r in group)
        switches = sum(int(r.get("switches", 0)) for r in group)
        share = part_time_share(group)
        share_text = "、".join("%s %.0f%%" % (part, value * 100) for part, value in share.items())
        lines.append("| %s | %d | %s | %s | %s | %s | %s |" % (
            item, len(group), fmt(switches / float(len(group)), 1), fmt(energy / seconds, 2),
            fmt_pct(hits / float(offered)) if offered else "—", fmt(life / float(len(group)), 0),
            share_text or "—"))
    lines.append("")

    # 分段明细：这里才看得到"高密度段是不是真的掉档"
    lines.append("## 分段明细（每段一行：理论密度 vs 实际命中 vs 实收能量）")
    lines.append("")
    lines.append("| # | 道具 | 声部 | 起止 s | 时长 s | 供给 | 密度 颗/秒 | 命中率 | P/G/Gd | 能量 | 能量/秒 | 失误 |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for index, run in enumerate(runs, 1):
        for seg in run["segments"]:
            lines.append("| %d | %s | %s | %s–%s | %s | %s | %s | %s | %s/%s/%s | %s | %s | %s |" % (
                index, item_of(run), seg.get("part", "?"), fmt(float(seg.get("start", 0)), 1),
                fmt(float(seg.get("end", 0)), 1), fmt(seg_seconds(seg), 1), seg.get("offered", 0),
                fmt(seg_density(seg), 2), fmt_pct(seg_hit_rate(seg)),
                seg.get("perfect", 0), seg.get("great", 0), seg.get("good", 0),
                fmt(float(seg.get("energy", 0)), 1), fmt(seg_rate(seg), 2), seg.get("misses", 0)))
    lines.append("")

    # 按声部汇总
    lines.append("## 按声部汇总（跨局）")
    lines.append("")
    lines.append("| 声部 | 停留秒 | 平均密度 颗/秒 | 平均命中率 | 平均实收 能量/秒 | P/G/Gd |")
    lines.append("| --- | --- | --- | --- | --- | --- |")
    for part, bucket in part_summary(runs).items():
        lines.append("| %s | %s | %s | %s | %s | %s/%s/%s |" % (
            part, fmt(bucket["seconds"], 1), fmt(bucket["density"], 2),
            fmt_pct(bucket["hit_rate"]),
            fmt(bucket["rate"], 2), bucket["perfect"], bucket["great"], bucket["good"]))
    lines.append("")

    lines.append("## 怎么读")
    lines.append("")
    lines.append("- **密度 vs 命中率**：同一段里密度高、命中率明显低，才说明「高密度真的更难」；")
    lines.append("  若密度翻倍而命中率几乎不掉，说明这段只是变多、没有变难。")
    lines.append("- **实收 能量/秒**：这才是有没有赚到的答案。它低于别的声部，就该换个选择。")
    lines.append("- **同道具多局看路线是否重复**：路线每次都一样 = 玩家已经形成习惯；")
    lines.append("  不同道具下路线不同 = 道具真的改变了决策。")
    return "\n".join(lines)


def group_by_item(runs):
    groups = OrderedDict()
    for run in runs:
        groups.setdefault(item_of(run), []).append(run)
    return groups


def part_time_share(runs):
    seconds = OrderedDict()
    total = 0.0
    for run in runs:
        for seg in run["segments"]:
            span = seg_seconds(seg)
            seconds[seg.get("part", "?")] = seconds.get(seg.get("part", "?"), 0.0) + span
            total += span
    if total <= 0:
        return OrderedDict()
    return OrderedDict((part, value / total) for part, value in seconds.items())


def part_summary(runs):
    buckets = OrderedDict()
    for run in runs:
        for seg in run["segments"]:
            part = seg.get("part", "?")
            bucket = buckets.setdefault(part, {"seconds": 0.0, "offered": 0, "hits": 0, "energy": 0.0,
                                               "perfect": 0, "great": 0, "good": 0})
            bucket["seconds"] += seg_seconds(seg)
            bucket["offered"] += int(seg.get("offered", 0))
            bucket["hits"] += int(seg.get("hits", 0))
            bucket["energy"] += float(seg.get("energy", 0))
            bucket["perfect"] += int(seg.get("perfect", 0))
            bucket["great"] += int(seg.get("great", 0))
            bucket["good"] += int(seg.get("good", 0))
    for bucket in buckets.values():
        bucket["density"] = bucket["offered"] / bucket["seconds"] if bucket["seconds"] > 0 else None
        bucket["hit_rate"] = bucket["hits"] / float(bucket["offered"]) if bucket["offered"] else None
        bucket["rate"] = bucket["energy"] / bucket["seconds"] if bucket["seconds"] > 0 else None
    return buckets


# ── 合成样例（试玩前先看格式）────────────────────────────────────────────
def demo_runs():
    def run(item, switches, plan):
        segments, clock = [], 0.0
        for part, span, offered, hits, perfect, great, good, energy in plan:
            segments.append({"part": part, "start": clock, "end": clock + span, "offered": offered,
                             "hits": hits, "misses": max(0, offered - hits), "perfect": perfect,
                             "great": great, "good": good, "energy": energy,
                             "lifeLost": 80 * max(0, offered - hits)})
            clock += span
        return {"timestamp": "2026-10-05T09:0%d:00Z" % switches, "song": "Guitar / Drums Prototype",
                "partItem": item, "duration": clock, "switches": switches,
                "energy": sum(s["energy"] for s in segments), "hits": sum(s["hits"] for s in segments),
                "misses": sum(s["misses"] for s in segments),
                "lifeLost": sum(s["lifeLost"] for s in segments), "segments": segments}

    return [
        run("无道具（对照）", 1, [("Guitar", 12.0, 40, 38, 36, 2, 0, 38.0),
                                  ("Drums", 22.0, 96, 60, 30, 20, 10, 44.0)]),
        run("无道具（对照）", 0, [("Guitar", 34.0, 120, 110, 96, 10, 4, 112.0)]),
        run("鼓手契约 Drums ×1.3", 1, [("Guitar", 10.0, 32, 30, 28, 2, 0, 30.0),
                                      ("Drums", 24.0, 104, 78, 50, 20, 8, 82.3)]),
        run("鼓手契约 Drums ×1.3", 3, [("Guitar", 6.0, 20, 19, 18, 1, 0, 19.0),
                                      ("Bass", 8.0, 32, 32, 32, 0, 0, 32.0),
                                      ("Drums", 20.0, 88, 70, 46, 16, 8, 74.0)]),
        run("长坐 8s 起爬到 ×1.5", 0, [("Bass", 34.0, 128, 128, 128, 0, 0, 154.0)]),
        run("换手红利 前 10 颗 ×1.5", 5, [("Guitar", 8.0, 26, 24, 22, 2, 0, 30.0),
                                        ("Drums", 9.0, 40, 26, 14, 8, 4, 31.0),
                                        ("Guitar", 8.0, 26, 25, 24, 1, 0, 30.0),
                                        ("Bass", 9.0, 36, 36, 36, 0, 0, 36.0)]),
    ]


# ── 对照图（可选，有 Pillow 才画）───────────────────────────────────────
def write_chart(runs, path):
    from PIL import Image, ImageDraw, ImageFont

    def font(size, bold=False):
        for name in (["C:/Windows/Fonts/msyhbd.ttc", "C:/Windows/Fonts/msyh.ttc"] if bold
                     else ["C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf"]):
            try:
                return ImageFont.truetype(name, size)
            except OSError:
                continue
        return ImageFont.load_default()

    groups = group_by_item(runs)
    parts = list(part_summary(runs).keys())
    width, row_h = 1200, 90
    height = 160 + row_h * (len(groups) + len(parts)) + 120
    image = Image.new("RGB", (width, height), (10, 18, 24))
    draw = ImageDraw.Draw(image)
    draw.text((40, 34), "试玩对照：每个道具组的切换与实收", font=font(34, True), fill=(222, 244, 240))
    draw.text((40, 84), "上：按道具（平均切换次数 / 平均能量每秒）　下：按声部（停留秒 / 命中率 / 能量每秒）",
              font=font(22), fill=(150, 180, 186))

    y = 140
    max_rate = max([sum(float(r.get("energy", 0)) for r in g) /
                    max(0.01, sum(float(r.get("duration", 0)) for r in g)) for g in groups.values()] or [1.0])
    for item, group in groups.items():
        seconds = max(0.01, sum(float(r.get("duration", 0)) for r in group))
        rate = sum(float(r.get("energy", 0)) for r in group) / seconds
        switches = sum(int(r.get("switches", 0)) for r in group) / float(len(group))
        draw.text((40, y + 10), item, font=font(24, True), fill=(222, 244, 240))
        bar = int(600 * rate / max_rate) if max_rate > 0 else 0
        draw.rectangle([420, y + 8, 420 + bar, y + 44], fill=(126, 224, 176))
        draw.text((430 + bar, y + 14), "%.1f 能量/秒   平均切换 %.1f 次" % (rate, switches),
                  font=font(22), fill=(168, 222, 218))
        y += row_h

    y += 30
    draw.text((40, y - 6), "按声部（跨局）", font=font(26, True), fill=(222, 244, 240))
    y += 40
    for part, bucket in part_summary(runs).items():
        draw.text((40, y + 8), part, font=font(24, True), fill=(207, 239, 234))
        draw.text((240, y + 12), "停留 %.1fs   密度 %s 颗/秒   命中率 %s   实收 %s 能量/秒"
                  % (bucket["seconds"], fmt(bucket["density"], 2),
                     fmt_pct(bucket["hit_rate"]),
                     fmt(bucket["rate"], 2)), font=font(22), fill=(150, 180, 186))
        y += row_h
    image.save(path)
    return path


# ── 自检 ────────────────────────────────────────────────────────────────
def selftest():
    runs = demo_runs()
    groups = group_by_item(runs)
    checks = []
    checks.append(("六局样例分组正确", len(groups) == 4 and sum(len(g) for g in groups.values()) == 6))
    summary = part_summary(runs)
    checks.append(("声部汇总含 Bass", "Bass" in summary and summary["Bass"]["offered"] == 196))
    bass = summary["Bass"]
    checks.append(("Bass 全中：命中率 100%、实收 222 能量 / 51 秒",
                   abs(bass["hit_rate"] - 1.0) < 1e-9 and abs(bass["seconds"] - 51.0) < 1e-6
                   and abs(bass["rate"] - 222.0 / 51.0) < 0.005))
    share = part_time_share(groups["长坐 8s 起爬到 ×1.5"])
    checks.append(("单声部局停留占比 100%", abs(share["Bass"] - 1.0) < 1e-9))
    guitar = summary["Guitar"]
    checks.append(("Guitar 密度低于 Drums（地形没被写反）",
                   guitar["density"] < summary["Drums"]["density"]))
    report = markdown_report(runs)
    checks.append(("报告含一行一局与分段明细", "## 一行一局" in report and "## 分段明细" in report
                   and "换手红利" in report))
    failed = [name for name, ok in checks if not ok]
    for name, ok in checks:
        print(("PASS " if ok else "FAIL ") + name)
    print("ALL PASS (%d)" % len(checks) if not failed else "FAILED: %s" % "、".join(failed))
    return 0 if not failed else 1


def main():
    parser = argparse.ArgumentParser(description="试玩记录汇总（part_runs.jsonl）")
    parser.add_argument("path", nargs="?", default=DEFAULT_PATH, help="part_runs.jsonl 路径")
    parser.add_argument("--demo", action="store_true", help="用合成样例跑一遍（不需要真实数据）")
    parser.add_argument("--md", metavar="FILE", help="把报告另存成 markdown")
    parser.add_argument("--png", metavar="FILE", help="另画一张对照图")
    parser.add_argument("--selftest", action="store_true", help="自检聚合逻辑")
    args = parser.parse_args()

    if args.selftest:
        return selftest()

    bad = 0
    if args.demo:
        runs = demo_runs()
        print("# 注意：这是 --demo 合成数据，用来先看报告长什么样。\n")
    else:
        if not os.path.exists(args.path):
            print("找不到试玩记录：%s" % args.path)
            print("先玩一局（原型 F8 → 曲终会自动落盘），或用 --demo 看格式。")
            return 2
        runs, bad = load_runs(args.path)
        if not runs:
            print("文件里没有可用的记录：%s" % args.path)
            return 2

    report = markdown_report(runs, bad)
    print(report)
    if args.md:
        with open(args.md, "w", encoding="utf-8") as handle:
            handle.write(report + "\n")
        print("\n已写出：" + args.md)
    if args.png:
        try:
            print("已写出：" + write_chart(runs, args.png))
        except ImportError:
            print("没有 Pillow，跳过 --png（报告不受影响）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
