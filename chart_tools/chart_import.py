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


def convert(path, difficulty=None, flick_direction="up"):
    with open(path, "r", encoding="utf-8", errors="ignore") as handle:
        text = handle.read()
    ext = os.path.splitext(path)[1].lower()
    if ext == ".osu":
        return parse_osu(text)
    if ext in (".sm", ".ssc"):
        return parse_sm(text, difficulty)
    if ext == ".json":
        return parse_phigros(text, flick_direction)
    if ext == ".pec":
        return parse_pec(text, flick_direction)
    raise ValueError("暂不支持该格式：" + ext + "（支持 .osu / .sm / .ssc / .json / .pec）")


# ---------------- Phigros 系（官方 .json / 社区 .pec） ----------------

def _phigros_lane(position_x, above):
    """判定线内 x∈[-1,1] → 轨道：上线映射 0..2，下线映射 2..4（共用中间轨）。"""
    t = max(0.0, min(1.0, (position_x + 1.0) / 2.0))
    return int(round(t * 2)) if above else 2 + int(round(t * 2))


def parse_phigros(text, flick_direction="up"):
    """Phigros 官方谱面 JSON：judgeLineList[].notesAbove/notesBelow，time/holdTime 为拍。

    约定：秒 = 拍 × 60 / 该判定线 bpm − offset；flick 无方向信息，
    由 flick_direction 决定（up / down / alternate，alternate 便于测试上下两种判定）。
    """
    import json as _json
    data = _json.loads(text)
    chart = empty_chart()
    meta = chart["metadata"]
    meta["songName"] = data.get("songName") or data.get("name") or ""
    meta["songArtist"] = data.get("songArtist") or data.get("artist") or ""
    meta["chartAuthor"] = data.get("chartAuthor") or data.get("charter") or ""
    offset = float(data.get("offset", 0.0) or 0.0)
    lines = data.get("judgeLineList") or data.get("judgeLines") or []
    if not lines:
        raise ValueError("Phigros 谱面缺少 judgeLineList")
    meta["bpm"] = round(float(lines[0].get("bpm", 120.0) or 120.0), 3)
    meta["offset"] = round(-offset, 3)

    flick_index = 0
    for line in lines:
        bpm = float(line.get("bpm", meta["bpm"]) or meta["bpm"])
        beat_seconds = 60.0 / bpm if bpm > 0 else 0.5
        for key, above in (("notesAbove", True), ("notesBelow", False), ("notes", True)):
            for raw in line.get(key, []) or []:
                kind = int(raw.get("type", 1))
                if kind not in (1, 2, 3, 4):
                    continue
                beat = float(raw.get("time", 0.0) or 0.0)
                hold_beats = float(raw.get("holdTime", 0.0) or 0.0)
                time = beat * beat_seconds - offset
                lane = _phigros_lane(float(raw.get("positionX", 0.0) or 0.0), above)
                if kind == 1:
                    chart["notes"].append({"type": "tap", "lane": lane, "time": time, "duration": 0.0})
                elif kind == 2:
                    chart["notes"].append({"type": "drag", "lane": lane, "time": time, "duration": 0.0})
                elif kind == 3:
                    chart["notes"].append({"type": "hold", "lane": lane, "time": time,
                                           "duration": max(0.05, hold_beats * beat_seconds)})
                else:
                    if flick_direction == "alternate":
                        direction = "up" if flick_index % 2 == 0 else "down"
                        flick_index += 1
                    else:
                        direction = flick_direction
                    chart["notes"].append({"type": "flick", "lane": lane, "time": time,
                                           "duration": 0.0, "direction": direction})
    return finish(chart)


def parse_pec(text, flick_direction="up"):
    """社区 PEC 文本格式：#offset / #bpms / 判定线段落里的音符行。

    音符行形如 `时间,类型,位置,持续时间[,速度]`；类型 1=tap 2=drag 3=hold 4=flick。
    只取主判定线（首个 `&` 之前的音符段），够用于测试。
    """
    chart = empty_chart()
    offset = 0.0
    bpm = 120.0
    notes = []
    in_notes = False
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("//"):
            continue
        if line.startswith("#"):
            head = line[1:].split(None, 1)
            tag = head[0].lower() if head else ""
            value = head[1].strip() if len(head) > 1 else ""
            if tag == "offset":
                offset = float(value or 0.0)
            elif tag == "bpms":
                first = value.split(",")[0]
                bpm = float(first.split("=")[-1]) if "=" in first else float(first)
            elif tag in ("notes", "note"):
                in_notes = True
            elif tag in ("end", "line"):
                in_notes = False
            continue
        if line.startswith("&") or line.startswith("cv") or line.startswith("cp"):
            continue
        if "&" in line:
            line = line.split("&")[0]
        if in_notes and "," in line:
            parts = [p for p in line.split(",")]
            if len(parts) < 2:
                continue
            try:
                beat = float(parts[0])
                kind = int(parts[1])
                position = float(parts[2]) if len(parts) > 2 and parts[2] else 0.0
                hold_beats = float(parts[3]) if len(parts) > 3 and parts[3] else 0.0
            except ValueError:
                continue
            notes.append((beat, kind, position, hold_beats))
    if not notes:
        raise ValueError("PEC 里没有解析到音符行")
    chart["metadata"]["bpm"] = round(bpm, 3)
    chart["metadata"]["offset"] = round(-offset, 3)
    beat_seconds = 60.0 / bpm if bpm > 0 else 0.5
    flick_index = 0
    for beat, kind, position, hold_beats in notes:
        lane = _phigros_lane(position, True)
        time = beat * beat_seconds - offset
        if kind == 1:
            chart["notes"].append({"type": "tap", "lane": lane, "time": time, "duration": 0.0})
        elif kind == 2:
            chart["notes"].append({"type": "drag", "lane": lane, "time": time, "duration": 0.0})
        elif kind == 3:
            chart["notes"].append({"type": "hold", "lane": lane, "time": time,
                                   "duration": max(0.05, hold_beats * beat_seconds)})
        elif kind == 4:
            direction = ("up" if flick_index % 2 == 0 else "down") if flick_direction == "alternate" else flick_direction
            flick_index += 1
            chart["notes"].append({"type": "flick", "lane": lane, "time": time,
                                   "duration": 0.0, "direction": direction})
    return finish(chart)


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
    parser.add_argument("--flick-direction", default="up", choices=["up", "down", "alternate"],
                        help="Phigros/PEC 的 flick 没有方向信息，用它决定（alternate=上下交替，便于测试方向判定）")
    args = parser.parse_args(argv)

    chart = convert(args.input, args.difficulty, args.flick_direction)
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
