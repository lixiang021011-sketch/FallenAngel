#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""开源谱面导入 → FallenAngel 内部 v2 JSON（5 轨）。

支持：osu!mania（.osu）、StepMania（.sm / .ssc）、SUS（.sus，Project Sekai 社区格式）、
Phigros 官方 JSON（.json）、社区 PEC（.pec）。
映射：tap→tap、long note/hold/roll→hold、friction→drag、flick→flick；轨道按源键数线性铺到 0..4。
不写游戏侧统计（noteCounts 由本脚本填），events 默认空。

用法：
  python -X utf8 chart_tools/chart_import.py song.osu --out chart.json --audio demo_song
  python -X utf8 chart_tools/chart_import.py song.sm  --out chart.json --difficulty Expert
  python -X utf8 chart_tools/chart_import.py song.sus --out chart.json

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
    # v2 协议与游戏加载侧都读 metadata.noteCounts（ChartData.ChartMetadata.noteCounts）；
    # 顶层那份是历史写法，保留给既有脚本/测试读，但游戏不认。
    chart.setdefault("metadata", {})["noteCounts"] = counts
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
    # 官方/社区 SUS 导出常见两种扩展名：.sus 与 .txt（sekai.best 直链给的就是 .txt）
    if ext in (".sus", ".txt"):
        return parse_sus(text, flick_direction)
    if ext == ".json":
        return parse_phigros(text, flick_direction)
    if ext == ".pec":
        return parse_pec(text, flick_direction)
    raise ValueError("暂不支持该格式：" + ext + "（支持 .osu / .sm / .ssc / .sus / .txt / .json / .pec）")


# ---------------- SUS（Project Sekai 社区格式） ----------------
#
# 依据 OpenSekai（cubeww/OpenSekai，MIT）的 Sekai/SUS/Converter.cs 复刻其通道语义：
#   数据行 = #<bar:3><通道:1~2><子通道:1>:<数据>
#     · 通道 02 = 拍号（TIL）、08 = BPM；1x = 普通轨、3x = 长按、5x = flick、9x = guide
#     · 长按/guide 多一位 longNo（同 longNo 的一串音符串成一条长按）
#   数据 = 等分小节的一串 2 字符记号 `[记号][宽度]`（可跟 `,倍速`），`00` 表示空
#     记号：1=普通 2=critical 3=skip 4=技能 5=friction 6=critical friction 7/8=隐藏 friction
#     flick 通道：1=flick 2=缓入普通 3=左 flick 4=右 flick 5/6=缓出普通
#     longNo 通道：1=长按头 2=长按尾 3=连接 5=隐藏
#   小节内位置 = 记号序号 / 记号总数。

SUS_LANE_NAMES = {"2": 0, "3": 1, "4": 2, "5": 3, "6": 4, "7": 5,
                  "8": 6, "9": 7, "a": 8, "b": 9, "c": 10, "d": 11}
SUS_LANE_COUNT = 12


def _sus_digit(ch):
    """SUS 的十六进制式记号：0-9 a-z。"""
    ch = ch.lower()
    if "0" <= ch <= "9":
        return int(ch)
    if "a" <= ch <= "z":
        return ord(ch) - ord("a") + 10
    return -1


def _sus_map_lane(sus_lane):
    """SUS 的 12 条判定线 → 本作 5 轨（线性铺开）。"""
    if sus_lane < 0:
        return -1
    return max(0, min(LANES - 1, int(round(sus_lane * (LANES - 1) / (SUS_LANE_COUNT - 1)))))


def _drop_same_lane_duplicates(chart, tolerance=0.005):
    """丢弃"同一轨 + 同刻（±tolerance）"的重复音符，返回丢弃数。

    为什么需要：SUS 是 12 条判定线，本作只有 5 轨，映射必然塌缩——
    官方谱里不同源轨的同刻音符会落到同一条轨的同刻位置，一个按键位置放不下两个音符。
    这是"12→5"的固有损失，不是解析 bug；这里显式丢弃并报告，避免导入出的谱面
    出现同轨同刻重复（那种谱面在游戏里必然判不准）。
    """
    kept, seen, dropped = [], set(), 0
    for note in chart["notes"]:
        key = (note["lane"], round(note["time"] / tolerance))
        if key in seen:
            dropped += 1
            continue
        seen.add(key)
        kept.append(note)
    chart["notes"] = kept
    return dropped


def _sus_tokens(data):
    """把数据字段拆成 [(记号2字符, 倍速)]；忽略空白与非法字符。"""
    out = []
    i = 0
    data = (data or "").replace("\t", " ")
    while i < len(data):
        while i < len(data) and data[i].isspace():
            i += 1
        if i + 1 >= len(data):
            break
        token = data[i:i + 2].lower()
        if _sus_digit(token[0]) < 0 or _sus_digit(token[1]) < 0:
            i += 1
            continue
        i += 2
        speed = 1.0
        if i < len(data) and data[i] == ",":
            i += 1
            start = i
            while i < len(data) and not data[i].isspace():
                i += 1
            try:
                speed = float(data[start:i])
            except ValueError:
                speed = 1.0
        out.append((token, speed))
    return out


def _sus_header(text):
    """#TITLE / #BPMxx / #TILxx 等头部标签（SUS 常用 `#TAG "值"` 无冒号形式，也兼容 `#TAG:值`）。"""
    header = {"bpm": {}, "til": {}, "meta": {}}
    for raw in text.splitlines():
        line = raw.strip()
        if not line.startswith("#"):
            continue
        body = line[1:]
        if ":" in body:
            tag, value = body.split(":", 1)
        else:
            parts = body.split(None, 1)
            if len(parts) < 2:
                continue
            tag, value = parts[0], parts[1]
        tag = tag.strip().upper()
        value = value.strip().strip('"')
        if tag.startswith("BPM") and len(tag) == 5:          # #BPM01:120
            try:
                header["bpm"][tag[3:]] = float(value)
            except ValueError:
                pass
        elif tag.startswith("TIL") and len(tag) == 5:        # #TIL01:4/4
            header["til"][tag[3:]] = value
        elif tag in ("TITLE", "ARTIST", "DESIGNER", "CHARTER", "WAVE", "GENRE"):
            header["meta"][tag] = os.path.splitext(value)[0] if tag == "WAVE" else value
    return header


def _sus_beats_per_bar(til_text, default=4.0):
    """TIL 形如 4/4 → 每小节拍数（以四分音符为 1 拍）。"""
    if not til_text or "/" not in til_text:
        return default
    try:
        num, den = til_text.split("/", 1)
        num, den = float(num), float(den)
        if num <= 0 or den <= 0:
            return default
        return num * 4.0 / den
    except ValueError:
        return default


def _sus_time_map(bpm_events, til_events, default_bpm, default_beats):
    """把 (bar, progress) 映射成秒：按事件切段做分段积分。

    bpm_events / til_events 形如 [(bar, progress, 值)]；返回 f(bar, progress) -> 秒。
    """
    merged = sorted([(b, p, "bpm", v) for b, p, v in bpm_events] +
                    [(b, p, "til", v) for b, p, v in til_events],
                    key=lambda e: (e[0], e[1]))
    # 每段 = (起始 bar, 起始 progress, bpm, 每小节拍数, 该段起点的秒数)
    timeline = [(0, 0.0, default_bpm, default_beats, 0.0)]
    for bar, progress, kind, value in merged:
        prev = timeline[-1]
        elapsed = _sus_advance(prev[0], prev[1], int(bar), float(progress), prev[2], prev[3])
        bpm = float(value) if kind == "bpm" and float(value) > 0 else prev[2]
        beats = value if kind == "til" else prev[3]
        timeline.append((int(bar), float(progress), bpm, beats, prev[4] + elapsed))

    def time_at(bar, progress):
        current = timeline[0]
        for seg in timeline:
            if (seg[0], seg[1]) <= (bar, progress):
                current = seg
            else:
                break
        return current[4] + _sus_advance(current[0], current[1], bar, progress, current[2], current[3])

    return time_at


def _sus_advance(from_bar, from_progress, to_bar, to_progress, bpm, beats_per_bar):
    """(from) → (to) 之间的秒数；假设区间内 BPM/拍号不变。"""
    if (to_bar, to_progress) <= (from_bar, from_progress):
        return 0.0
    seconds_per_beat = 60.0 / bpm if bpm > 0 else 0.5
    # 小节内进度线性：跨 n 个小节就是 n 个小节长，加上两端进度差（可为负）
    bars = (to_bar - from_bar) + (to_progress - from_progress)
    return bars * beats_per_bar * seconds_per_beat


def parse_sus(text, flick_direction="up"):
    """SUS → 内部 v2 JSON。只取可玩内容：tap / hold / flick / drag（friction）。"""
    chart = empty_chart()
    meta = chart["metadata"]
    header = _sus_header(text)
    meta["songName"] = header["meta"].get("TITLE", "")
    meta["songArtist"] = header["meta"].get("ARTIST", "")
    meta["chartAuthor"] = header["meta"].get("DESIGNER") or header["meta"].get("CHARTER", "")
    meta["audioFileName"] = header["meta"].get("WAVE", "")

    # 收集数据行：区分短音符/事件（3 字符通道）与长按（4 字符通道）
    note_lines, bpm_events, til_events = [], [], []
    for raw in text.splitlines():
        line = raw.strip()
        if not line.startswith("#"):
            continue
        match = re.match(r"^#(\d{3})([0-9a-zA-Z]{2,3}):(.*)$", line)
        if not match:
            continue
        bar = int(match.group(1))
        channel = match.group(2).lower()
        data = match.group(3)
        if len(channel) == 2 and channel == "02":
            til_events.append((bar, data))
        elif len(channel) == 2 and channel == "08":
            bpm_events.append((bar, data))
        else:
            note_lines.append((bar, channel, data))

    default_bpm = 120.0
    if "01" in header["bpm"]:
        default_bpm = header["bpm"]["01"]
    elif header["bpm"]:
        default_bpm = sorted(header["bpm"].items())[0][1]
    meta["bpm"] = round(default_bpm, 3)
    default_beats = _sus_beats_per_bar(header["til"].get("01"))

    def expand(events, lookup, fallback):
        out = []
        for bar, data in events:
            tokens = _sus_tokens(data)
            total = max(1, len(tokens))
            for index, (token, _speed) in enumerate(tokens):
                # 数据记号本身就是要引用的 id（如 #BPM01 → 记号 "01"）
                value = lookup.get(token, fallback)
                if value is None:
                    continue
                out.append((bar, index / total, value))
        return out

    bpm_resolved = expand(bpm_events, header["bpm"], None)
    til_lookup = {}
    for key, value in header["til"].items():
        til_lookup[key] = _sus_beats_per_bar(value, default_beats)
    til_resolved = expand(til_events, til_lookup, None)

    time_at = _sus_time_map(bpm_resolved, til_resolved, default_bpm, default_beats)

    longs = {}            # (sus_lane, long_no) -> 起始时间
    flick_index = 0
    for bar, channel, data in note_lines:
        tokens = _sus_tokens(data)
        if not tokens:
            continue
        total = len(tokens)
        channel_type = _sus_digit(channel[0])
        sub = _sus_digit(channel[1]) if len(channel) > 1 else -1
        long_no = channel[2] if len(channel) > 2 else None
        sus_lane = SUS_LANE_NAMES.get(channel[1].lower(), -1)
        lane = _sus_map_lane(sus_lane)
        if lane < 0:
            continue                                  # 'e'/'f' 等特殊线：fever/事件，跳过

        for index, (token, _speed) in enumerate(tokens):
            if token == "00":
                continue
            code = _sus_digit(token[0])
            if code < 0:
                continue
            time = time_at(bar, index / total)

            if long_no is not None and channel_type == 3:      # 长按通道
                key = (sus_lane, long_no)
                if code == 1:                                  # 头
                    longs[key] = time
                elif code == 2:                                # 尾 → 合成 hold
                    start = longs.pop(key, None)
                    if start is not None and time > start:
                        chart["notes"].append({"type": "hold", "lane": lane,
                                               "time": start, "duration": time - start})
                # 连接/隐藏记号忽略
                continue
            if long_no is not None:
                continue                                       # guide 等其它长通道：跳过

            if channel_type == 1:
                if code in (1, 2):
                    chart["notes"].append({"type": "tap", "lane": lane, "time": time, "duration": 0.0})
                elif code in (5, 6, 7, 8):                     # friction：碰即过，映射为 drag
                    chart["notes"].append({"type": "drag", "lane": lane, "time": time, "duration": 0.0})
            elif channel_type == 5:
                if code in (1, 3, 4):                          # flick（原作左右滑，本作上下滑）
                    if flick_direction == "alternate":
                        direction = "up" if flick_index % 2 == 0 else "down"
                        flick_index += 1
                    else:
                        direction = flick_direction
                    chart["notes"].append({"type": "flick", "lane": lane, "time": time,
                                           "duration": 0.0, "direction": direction})
                elif code in (2, 5, 6):
                    chart["notes"].append({"type": "tap", "lane": lane, "time": time, "duration": 0.0})

    # 未闭合的长按：按 0.5s 补一个音，避免只剩一个孤零零的头
    for (_sus_lane, _long_no), start in longs.items():
        lane = _sus_map_lane(_sus_lane)
        if lane >= 0:
            chart["notes"].append({"type": "hold", "lane": lane, "time": start, "duration": 0.5})
    chart = finish(chart)
    dropped = _drop_same_lane_duplicates(chart)
    if dropped:
        print("  ⚠ 12→5 轨映射塌缩：丢弃同轨同刻重复音符 %d 个（原谱 12 线，本作 5 轨）" % dropped)
    return finish(chart)


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


def holds_to_taps(chart, min_beats):
    """把短于 min_beats 拍的长键降级为单击，返回改动数。

    为什么需要：osu!mania 的长键常是 0.25~0.5 拍的"短划"，而本作的长键阈值是
    **2 拍**（docs/note-types.md 已拍板）。从 osu!/AI 生成谱导入时，短长键直接搬进游戏
    会变成"按不动的短长条"，故按本作规则降级（只改类型，不改节奏/时间）。
    """
    meta = chart.get("metadata") or {}
    bpm = float(meta.get("bpm") or 0.0) or 120.0
    limit = min_beats * 60.0 / bpm
    changed = 0
    for note in chart.get("notes") or []:
        if note.get("type") == "hold" and float(note.get("duration") or 0.0) < limit:
            note["type"] = "tap"
            note["duration"] = 0.0
            changed += 1
    return changed


def thin_to_nps(chart, max_nps, window=1.0):
    """超密减音：1 秒窗口内超过 max_nps 就丢音，返回 (减音数, 剩余峰值)。

    为什么需要：osu!/AI 谱的瞬时密度远超本作两指上限（现有谱平均 3.5~6.2 音/秒）。
    保留优先级：整拍 > 半拍 > 其它细分；同一档次里优先丢"贴着邻居"的音（连打中最挤的那个）。
    只减音不移动既有音符，时间轴不变。
    """
    import math
    notes = chart.get("notes") or []
    if not notes or max_nps <= 0:
        return 0, 0
    meta = chart.get("metadata") or {}
    bpm = float(meta.get("bpm") or 0.0) or 120.0
    offset = float(meta.get("offset") or 0.0)
    beat = 60.0 / bpm

    def strength(note):
        pos = (float(note.get("time", 0.0)) - offset) / beat
        frac = abs(pos - round(pos))
        if frac < 0.03:
            return 1.0
        if abs(frac - 0.5) < 0.03:
            return 0.6
        if abs(frac - 0.25) < 0.03 or abs(frac - 0.75) < 0.03:
            return 0.4
        return 0.15

    def peak():
        times = sorted(n["time"] for n in notes)
        best = 0
        for i, t in enumerate(times):
            best = max(best, sum(1 for x in times[i:] if x - t < window))
        return best

    removed = 0
    guard = 0
    while guard < 20000:
        guard += 1
        times = sorted(n["time"] for n in notes)
        worst_start, worst_count = None, 0
        for i, t in enumerate(times):
            count = sum(1 for x in times[i:] if x - t < window)
            if count > worst_count:
                worst_start, worst_count = t, count
        if worst_start is None or worst_count <= max_nps:
            break
        window_notes = [n for n in notes if worst_start <= n["time"] < worst_start + window]
        ranked = sorted(window_notes, key=lambda n: (strength(n), -min(
            abs(n["time"] - o["time"]) for o in notes if o is not n) if len(notes) > 1 else 0.0))
        victim = ranked[0]
        notes.remove(victim)
        removed += 1
    chart["notes"] = notes
    return removed, peak()


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
    parser.add_argument("input", help="源谱面（.osu / .sm / .ssc / .sus / .json / .pec）")
    parser.add_argument("--out", required=True, help="输出 JSON 路径")
    parser.add_argument("--difficulty", help="StepMania 难度名（如 Expert / Challenge）")
    parser.add_argument("--audio", help="覆盖音频名（不含扩展名）")
    parser.add_argument("--title", help="覆盖曲名")
    parser.add_argument("--artist", help="覆盖艺术家")
    parser.add_argument("--flick-direction", default="up", choices=["up", "down", "alternate"],
                        help="Phigros/PEC 的 flick 没有方向信息，用它决定（alternate=上下交替，便于测试方向判定）")
    parser.add_argument("--max-hold", type=float, default=4.0,
                        help="谱面护栏：长按超过这个秒数就裁到上限（0 = 不裁；测试期默认 4s）")
    parser.add_argument("--hold-min-beats", type=float, default=0.0,
                        help="短于这个拍数的长键降级为单击（本作长键阈值是 2 拍；0 = 不处理）")
    parser.add_argument("--time-shift-ms", type=float, default=0.0,
                        help="整谱时间平移（毫秒）：谱面比音乐早到就填正数，晚到填负数")
    parser.add_argument("--max-nps", type=float, default=0.0,
                        help="超密减音：1 秒窗口内超过这个密度就丢音（0 = 不减；本作两指上限约 12）")
    parser.add_argument("--no-guardrails", action="store_true",
                        help="跳过谱面护栏（去重 / flick 补 direction / 长按裁剪 / 两指化），只在需要原始输出时用")
    args = parser.parse_args(argv)

    chart = convert(args.input, args.difficulty, args.flick_direction)
    if args.audio:
        chart["metadata"]["audioFileName"] = args.audio
    if args.title:
        chart["metadata"]["songName"] = args.title
    if args.artist:
        chart["metadata"]["songArtist"] = args.artist
    if args.hold_min_beats > 0:
        changed = holds_to_taps(chart, args.hold_min_beats)
        if changed:
            print("  · 短长键 → 单击 %d 个（短于 %.2f 拍；本作长键阈值 2 拍）"
                  % (changed, args.hold_min_beats))
    if args.time_shift_ms:
        shift = args.time_shift_ms / 1000.0
        for note in chart.get("notes") or []:
            note["time"] = round(float(note.get("time", 0.0)) + shift, 3)
        meta = chart.get("metadata") or {}
        meta["offset"] = round(float(meta.get("offset") or 0.0) + shift, 3)
        print("  · 整谱时间平移 %+.0f ms（谱面比音乐早到填正数）" % args.time_shift_ms)
    if args.max_nps > 0:
        removed, peak = thin_to_nps(chart, args.max_nps)
        if removed:
            print("  · 超密减音 %d 个（上限 %.1f 音/秒，减后峰值 %d）" % (removed, args.max_nps, peak))

    # ── 谱面护栏（2026-09-21）：导入即清洗 + 导入后体检，脏数据不进仓库 ──
    if not args.no_guardrails:
        from fix_charts import apply_guardrails   # 同目录模块
        for line in apply_guardrails(chart, args.max_hold if args.max_hold > 0 else None):
            print("  · " + line)

    problems = validate(chart)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(chart, handle, ensure_ascii=False, indent=2)

    if not args.no_guardrails:
        import audit_charts
        file_issues, _ = audit_charts.audit(args.out, args.max_hold if args.max_hold > 0 else 1e9)
        problems = problems + ["（导入后体检）" + issue for issue in file_issues]

    counts = chart["noteCounts"]
    print("已导出 %s：音符 %d（tap %d / hold %d）BPM %.2f 偏移 %.3fs"
          % (args.out, len(chart["notes"]), counts.get("tap", 0), counts.get("hold", 0),
             chart["metadata"]["bpm"], chart["metadata"]["offset"]))
    for problem in problems[:10]:
        print("  ⚠ " + problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
