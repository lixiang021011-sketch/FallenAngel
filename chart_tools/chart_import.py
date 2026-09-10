#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""开源谱面导入 → FallenAngel 内部 v2 JSON（5 轨）。

支持：osu!mania（.osu）、StepMania（.sm / .ssc）。
映射：tap→tap、long note/hold/roll→hold；轨道按源键数线性铺到 0..4。
不写游戏侧统计（noteCounts 由本脚本填），events 默认空。

用法：
  python -X utf8 chart_tools/chart_import.py song.osu --out chart.json --audio demo_song
  python -X utf8 chart_tools/chart_import.py song.sm  --out chart.json --difficulty Expert

自测：python -X utf8 -m unittest chart_tools.test_chart_import -v
"""
import argparse
import json
import os
import re
import sys

LANES = 5


def map_lane(lane, keys):
    """把源键位（0..keys-1）线性铺到 0..4（4K→0,1,3,4；7K→0,1,1,2,3,3,4）。"""
    if keys <= 1:
        return 0
    return max(0, min(LANES - 1, int(round(lane * (LANES - 1) / (keys - 1)))))


def empty_chart():
    return {
        "metadata": {
            "songName": "", "songArtist": "", "chartAuthor": "",
            "difficulty": 2, "level": 1, "bpm": 120.0, "offset": 0.0,
            "audioFileName": "", "previewStartTime": 0.0, "previewDuration": 10.0,
            "formatVersion": 2,
        },
        "notes": [],
        "noteCounts": {},
        "events": [],
    }


def finish(chart):
    """排序 + 统计 + 基础校验。"""
    notes = chart["notes"]
    notes.sort(key=lambda n: (n["time"], n["lane"]))
    counts = {"tap": 0, "hold": 0, "drag": 0, "flick": 0, "slide": 0}
    for n in notes:
        counts[n["type"]] = counts.get(n["type"], 0) + 1
        n["lane"] = max(0, min(LANES - 1, int(n["lane"])))
        n["time"] = round(float(n["time"]), 3)
        n.setdefault("duration", 0.0)
        n["duration"] = round(float(n["duration"]), 3)
        n.setdefault("direction", "up")
        n.setdefault("wide", False)
        n.setdefault("path", [])
    chart["noteCounts"] = counts
    return chart


# ---------------- osu!mania ----------------

def parse_osu(text):
    chart = empty_chart()
    meta = chart["metadata"]
    keys = 4
    section = None
    timing = []          # (time_seconds, beat_length_seconds)

    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("//"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip()
            continue
        if section == "General" and ":" in line:
            key, value = line.split(":", 1)
            if key.strip() == "AudioFilename":
                meta["audioFileName"] = os.path.splitext(value.strip())[0]
        elif section == "Metadata" and ":" in line:
            key, value = line.split(":", 1)
            key, value = key.strip(), value.strip()
            if key == "Title":
                meta["songName"] = value
            elif key == "Artist":
                meta["songArtist"] = value
            elif key == "Creator":
                meta["chartAuthor"] = value
        elif section == "Difficulty" and line.startswith("CircleSize:"):
            keys = max(1, int(round(float(line.split(":", 1)[1]))))
        elif section == "TimingPoints":
            parts = line.split(",")
            if len(parts) >= 2:
                try:
                    t = float(parts[0]) / 1000.0
                    beat_len = float(parts[1])
                except ValueError:
                    continue
                if beat_len > 0:                     # 只看非继承（uninherited）时间点
                    timing.append((t, beat_len / 1000.0))
        elif section == "HitObjects":
            parts = line.split(",")
            if len(parts) < 4:
                continue
            try:
                x, time_ms, kind = int(parts[0]), int(parts[2]), int(parts[3])
            except ValueError:
                continue
            lane = map_lane(min(keys - 1, int(x * keys / 512)), keys)
            t = time_ms / 1000.0
            if kind & 128 and len(parts) >= 6:       # mania 长按：endTime 在 hitSample 之前
                end_ms = int(parts[5].split(":")[0])
                chart["notes"].append({"type": "hold", "lane": lane, "time": t,
                                       "duration": max(0.05, end_ms / 1000.0 - t)})
            elif kind & 1:
                chart["notes"].append({"type": "tap", "lane": lane, "time": t, "duration": 0.0})

    if timing:
        timing.sort()
        meta["bpm"] = round(60.0 / timing[0][1], 3)
        meta["offset"] = round(timing[0][0], 3)
    return finish(chart)


# ---------------- StepMania (.sm / .ssc) ----------------

def _sm_tags(text):
    """把 #TAG:value; 拆成 {TAG: [values]}（同标签可多次出现，如 #NOTES）。"""
    tags = {}
    for match in re.finditer(r"#([A-Za-z0-9_]+):(.*?);", text, re.S):
        tags.setdefault(match.group(1).upper(), []).append(match.group(2))
    return tags


def _parse_bpms(value):
    out = []
    for pair in value.split(","):
        if "=" not in pair:
            continue
        beat, bpm = pair.split("=", 1)
        try:
            out.append((float(beat), float(bpm)))
        except ValueError:
            continue
    out.sort()
    return out or [(0.0, 120.0)]


def _beat_to_time(beat, bpms, offset):
    """按 BPM 分段积分得到秒（StepMania 约定：time = 拍时间 - offset）。"""
    seconds = 0.0
    last_beat, last_bpm = bpms[0]
    for b, bpm in bpms:
        if beat <= b:
            break
        seconds += (b - last_beat) * 60.0 / last_bpm
        last_beat, last_bpm = b, bpm
    seconds += (beat - last_beat) * 60.0 / last_bpm
    return seconds - offset


def _parse_measures(body):
    """按小节切分 #NOTES/#NOTEDATA：小节之间用逗号行分隔，每小节行数决定拍长。"""
    measures, current = [], []
    for line in body.splitlines():
        line = line.strip()
        if not line or line.startswith("//") or ":" in line:
            continue
        if line == ",":
            if current:
                measures.append(current)
                current = []
            continue
        if set(line) <= set("0123456789MLF"):        # 只保留纯判定字符行
            current.append(line)
    if current:
        measures.append(current)
    return measures


def parse_sm(text, difficulty_filter=None):
    chart = empty_chart()
    meta = chart["metadata"]
    tags = _sm_tags(text)
    meta["songName"] = tags.get("TITLE", [""])[0].strip()
    meta["songArtist"] = tags.get("ARTIST", [""])[0].strip()
    meta["chartAuthor"] = (tags.get("CREDIT", tags.get("AUTHOR", [""]))[0]).strip()
    meta["audioFileName"] = os.path.splitext(tags.get("MUSIC", [""])[0].strip())[0]
    offset = float(tags.get("OFFSET", ["0"])[0] or 0.0)
    bpms = _parse_bpms(tags.get("BPMS", ["0=120"])[0])
    meta["bpm"] = bpms[0][1]
    meta["offset"] = round(-offset, 3)

    block = None
    if "NOTES" in tags:                              # .sm：难度名在第 3 行
        for candidate in tags["NOTES"]:
            lines = [l.strip() for l in candidate.splitlines() if l.strip()]
            name = lines[2].rstrip(":").strip() if len(lines) > 2 else ""   # .sm 难度行形如 "Easy:"
            if difficulty_filter and name.lower() != difficulty_filter.lower():
                continue
            block = candidate
            if difficulty_filter:
                break
    elif "NOTEDATA" in tags:                         # .ssc：#NOTEDATA + #DIFFICULTY
        diffs = tags.get("DIFFICULTY", [])
        for i, candidate in enumerate(tags["NOTEDATA"]):
            name = diffs[i].strip() if i < len(diffs) else ""
            if difficulty_filter and name.lower() != difficulty_filter.lower():
                continue
            block = candidate + "\n" + (tags.get("NOTES", [""])[i] if i < len(tags.get("NOTES", [])) else "")
            break
    if block is None:
        raise ValueError("没有找到可用的 #NOTES / #NOTEDATA 段")

    measures = _parse_measures(block)
    if not measures:
        raise ValueError("谱面段里没有解析到判定行")
    keys = len(measures[0][0])
    heads = {}                                       # lane → 未闭合的 hold 起始
    for measure_index, rows in enumerate(measures):
        for row_index, row in enumerate(rows):
            beat = measure_index * 4.0 + 4.0 * row_index / len(rows)
            time = _beat_to_time(beat, bpms, offset)
            for lane_char, ch in enumerate(row):
                if ch in "0MLF":
                    continue
                lane = map_lane(lane_char, keys)
                if ch in "24":                           # hold / roll 头
                    heads[lane] = time
                elif ch == "3":                          # 尾
                    start = heads.pop(lane, None)
                    if start is not None:
                        chart["notes"].append({"type": "hold", "lane": lane, "time": start,
                                               "duration": max(0.05, time - start)})
                else:                                    # '1' 等 → tap
                    chart["notes"].append({"type": "tap", "lane": lane, "time": time, "duration": 0.0})
    for lane, start in heads.items():                # 未闭合的 hold：按半拍补一个时长
        chart["notes"].append({"type": "hold", "lane": lane, "time": start, "duration": 0.5})
    return finish(chart)


def convert(path, difficulty=None):
    with open(path, "r", encoding="utf-8", errors="ignore") as handle:
        text = handle.read()
    ext = os.path.splitext(path)[1].lower()
    if ext == ".osu":
        return parse_osu(text)
    if ext in (".sm", ".ssc"):
        return parse_sm(text, difficulty)
    raise ValueError("暂不支持该格式：" + ext + "（支持 .osu / .sm / .ssc）")


def validate(chart):
    """返回问题列表（空 = 通过）。"""
    problems = []
    for n in chart["notes"]:
        if not (0 <= n["lane"] < LANES):
            problems.append("轨道越界: %s" % n)
        if n["time"] < -1.0:
            problems.append("负时间（检查 offset）: %s" % n)
        if n["type"] in ("hold", "slide") and n["duration"] <= 0:
            problems.append("长按时长无效: %s" % n)
    by_time = {}
    for n in chart["notes"]:
        by_time[round(n["time"], 2)] = by_time.get(round(n["time"], 2), 0) + 1
    for t, count in by_time.items():
        if count > 2:
            problems.append("同刻超过 2 音（t=%.2f，共 %d）" % (t, count))
    return problems


def main(argv=None):
    parser = argparse.ArgumentParser(description="开源谱面导入 → FallenAngel v2 JSON（5 轨）")
    parser.add_argument("input", help="源谱面（.osu / .sm / .ssc）")
    parser.add_argument("--out", required=True, help="输出 JSON 路径")
    parser.add_argument("--difficulty", help="StepMania 难度名（如 Expert / Challenge）")
    parser.add_argument("--audio", help="覆盖音频名（不含扩展名）")
    parser.add_argument("--title", help="覆盖曲名")
    parser.add_argument("--artist", help="覆盖艺术家")
    args = parser.parse_args(argv)

    chart = convert(args.input, args.difficulty)
    if args.audio:
        chart["metadata"]["audioFileName"] = args.audio
    if args.title:
        chart["metadata"]["songName"] = args.title
    if args.artist:
        chart["metadata"]["songArtist"] = args.artist

    problems = validate(chart)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(chart, handle, ensure_ascii=False, indent=2)

    counts = chart["noteCounts"]
    print("已导出 %s：音符 %d（tap %d / hold %d）BPM %.2f 偏移 %.3fs"
          % (args.out, len(chart["notes"]), counts.get("tap", 0), counts.get("hold", 0),
             chart["metadata"]["bpm"], chart["metadata"]["offset"]))
    for problem in problems[:10]:
        print("  ⚠ " + problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
