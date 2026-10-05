#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Chart Adapter：osu!mania 4K（.osu）→ FallenAngel ChartData（v2 JSON，4 轨原样）。

和 chart_import.py 的区别：
  chart_import.py 是"开源谱面进游戏"的通用导入器，会把键数铺到 5 轨、顺手做护栏清洗。
  本工具只做一件事——把自动制谱（Mapperatorinator）的 4K 草稿**忠实**转成本作数据：
    · lane 原样 0–3，不按乐器重排，不铺到 5 轨；
    · 不删音、不改节奏、不降级长按；有问题只进导入报告；
    · 坏数据（非正时长的长按、负时间、无法解析的行）不进谱面，并在报告里逐条列出。

时间标准（与游戏/编辑器一致，见 GameManager.SongTime 与 ChartEditorWindow）：
  · note.time / duration 单位为秒，等于**音频位置**（osu 毫秒 / 1000，不做任何平移）；
  · metadata.offset 写 0。游戏里 SongTime = 音频位置 − offset，offset 留给延迟/静音校准，
    不能拿来存"第一拍在哪"，否则游戏里音符会整体推迟；
  · 第一拍位置（第一个非继承时间点）记在 metadata.sourceMetadata.beatOffset，
    Song 层 Timing 建好后由它接管网格起点。

输出字段：
  metadata.partName      乐器（Guitar / Bass / Drums …，切谱运行时按它分组）
  metadata.laneCount     4（新字段；旧 v2 谱没有它 = 5 轨。C# 侧接入前会被忽略，不影响加载）
  metadata.sourceMetadata 来源信息（Title/Artist/Creator/Version/原始时间点…），不参与转换
  notes[]                {type: tap|hold, lane 0–3, time 秒, duration 秒}
  events[]               变速时写 {time, type: "bpm", value}

用法：
  python -X utf8 chart_tools/osu_adapter.py Bass.osu --part Bass --out Assets/Resources/Charts/song_bass.json
  python -X utf8 chart_tools/osu_adapter.py Bass.osu --part Bass --out song_bass.json --report reports/song_bass.txt

  报告默认打印到终端；--report 另存一份。**不要把报告写进 Resources/Charts**——
  ChartLoader 会把那里的每个 TextAsset 都当谱面读。

退出码：0 = 已输出（可能带警告）；1 = 致命错误，未输出；2 = 参数错误。
自测：python -X utf8 -m unittest chart_tools.test_osu_adapter -v
"""
import argparse
import json
import os
import sys

KEYS = 4
OSU_WIDTH = 512
DIFFICULTIES = ["Easy", "Normal", "Hard", "Expert"]   # 与 ChartData.Difficulty 枚举顺序一致


# ============================================================================
# 导入报告
# ============================================================================

class ImportReport:
    """一次转换的全部发现。errors = 不能输出；warnings = 已输出但需要人看一眼。"""

    def __init__(self, source=""):
        self.source = source
        self.errors = []
        self.warnings = []
        self.infos = []
        self.counts = {"tap": 0, "hold": 0}
        self.removed = {"duplicate": 0, "invalid_hold": 0, "negative_time": 0}
        self.unsupported = 0
        self.unparsable = 0

    def error(self, msg):
        self.errors.append(msg)

    def warn(self, msg):
        self.warnings.append(msg)

    def info(self, msg):
        self.infos.append(msg)

    @property
    def ok(self):
        return not self.errors

    def to_text(self):
        lines = ["Import Result" + ("  (" + self.source + ")" if self.source else ""), ""]
        if self.errors:
            lines.append("FAILED — 未输出谱面")
            lines += ["  ✗ " + e for e in self.errors]
            lines.append("")
        total = self.counts["tap"] + self.counts["hold"]
        lines += [
            "Total Notes: %d" % total,
            "Tap: %d" % self.counts["tap"],
            "Hold: %d" % self.counts["hold"],
            "",
            "Warnings:",
            "%d duplicated notes removed" % self.removed["duplicate"],
            "%d invalid holds removed" % self.removed["invalid_hold"],
            "%d negative-time notes removed" % self.removed["negative_time"],
            "%d unsupported objects" % self.unsupported,
            "%d unparsable lines" % self.unparsable,
        ]
        if self.warnings:
            lines.append("")
            lines += ["  ⚠ " + w for w in self.warnings]
        if self.infos:
            lines.append("")
            lines += ["  · " + i for i in self.infos]
        return "\n".join(lines) + "\n"


# ============================================================================
# ① Parse：.osu 文本 → 原始结构（不做任何语义判断）
# ============================================================================

def parse_osu(text):
    """返回 {"version", "sections": {name: {key: value}}, "timing": [(line_no, fields)],
    "objects": [(line_no, raw_line)]}。键值节保留原文，TimingPoints/HitObjects 保留行号供报告定位。"""
    result = {"version": None, "sections": {}, "timing": [], "objects": []}
    section = None
    for line_no, raw in enumerate(text.splitlines(), start=1):
        line = raw.strip()
        if line_no == 1 or result["version"] is None:
            stripped = line.lstrip("﻿")
            if stripped.startswith("osu file format"):
                result["version"] = stripped
                continue
        if not line or line.startswith("//"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip()
            continue
        if section == "TimingPoints":
            result["timing"].append((line_no, line))
        elif section == "HitObjects":
            result["objects"].append((line_no, line))
        elif section in ("General", "Metadata", "Difficulty", "Editor") and ":" in line:
            key, value = line.split(":", 1)
            result["sections"].setdefault(section, {})[key.strip()] = value.strip()
    return result


def _num(value, cast=float):
    return cast(value.strip())


# ============================================================================
# ② Convert：原始结构 → FallenAngel ChartData（dict）
# ============================================================================

def read_timing(parsed, report):
    """非继承时间点 → [{time 秒, beatLength 秒, bpm, meter}]，按时间排序。"""
    points = []
    for line_no, line in parsed["timing"]:
        parts = line.split(",")
        try:
            t_ms = _num(parts[0])
            beat_len = _num(parts[1])
            meter = int(_num(parts[2])) if len(parts) > 2 and parts[2].strip() else 4
            uninherited = int(_num(parts[6])) if len(parts) > 6 and parts[6].strip() else (1 if beat_len > 0 else 0)
        except (ValueError, IndexError):
            report.warn("TimingPoints 第 %d 行无法解析，已跳过：%s" % (line_no, line))
            continue
        if not uninherited:
            continue                                   # 继承点只改滑条速度/音效，对节奏无影响
        if beat_len <= 0:
            report.warn("TimingPoints 第 %d 行是非继承点但 beatLength=%s ≤ 0，已跳过" % (line_no, parts[1]))
            continue
        points.append({"time": t_ms / 1000.0, "beatLength": beat_len / 1000.0,
                       "bpm": 60000.0 / beat_len, "meter": meter if meter > 0 else 4})
    points.sort(key=lambda p: p["time"])
    return points


def convert(parsed, part="", difficulty="Normal", audio=None, title=None, level=1,
            source_name="", report=None):
    """返回 (chart_dict 或 None, report)。致命错误时 chart 为 None。"""
    report = report or ImportReport(source_name)
    sec = parsed["sections"]
    general, meta, diff = sec.get("General", {}), sec.get("Metadata", {}), sec.get("Difficulty", {})

    if parsed["version"] is None:
        report.warn("缺少首行 \"osu file format vN\"，按 v14 语法解析")

    # ---- 模式与键数：只接 osu!mania 4K ----
    mode = general.get("Mode", "0")
    if mode.strip() != "3":
        report.error("不是 osu!mania 谱（General.Mode=%s，需要 3）" % mode)
    try:
        keys = int(round(float(diff.get("CircleSize", "0"))))
    except ValueError:
        keys = 0
    if keys != KEYS:
        report.error("键数是 %s，本工具只接 4K（Difficulty.CircleSize=4）" % diff.get("CircleSize", "缺失"))

    # ---- Timing ----
    timing = read_timing(parsed, report)
    if not timing:
        report.error("没有可用的非继承时间点（TimingPoints），无法确定 BPM")
    if not report.ok:
        return None, report

    first = timing[0]
    bpm_events = []
    for prev, cur in zip(timing, timing[1:]):
        if abs(cur["bpm"] - prev["bpm"]) > 1e-6:
            bpm_events.append({"time": round(cur["time"], 3), "type": "bpm", "value": round(cur["bpm"], 3)})
    if bpm_events:
        report.warn("有 %d 处变速，已写入 events；现有 Editor 网格只按首个 BPM（%.3f）画"
                    % (len(bpm_events), first["bpm"]))
    meters = sorted({p["meter"] for p in timing})
    if meters != [4]:
        report.info("拍号：%s（记在 sourceMetadata.timingPoints，Song 层 Timing 接管）"
                    % "/".join("%d/4" % m for m in meters))
    report.info("BPM %.3f，第一拍 %.3fs（beatOffset）" % (first["bpm"], first["time"]))

    # ---- HitObjects ----
    raw_notes = []
    for line_no, line in parsed["objects"]:
        parts = line.split(",")
        try:
            x = _num(parts[0])
            t_ms = _num(parts[2])
            kind = int(_num(parts[3]))
        except (ValueError, IndexError):
            report.unparsable += 1
            report.warn("HitObjects 第 %d 行无法解析：%s" % (line_no, line))
            continue

        lane = int(x * KEYS // OSU_WIDTH)
        if lane < 0 or lane >= KEYS:
            clamped = min(KEYS - 1, max(0, lane))
            report.warn("第 %d 行 x=%s 超出 0–511，轨道 %d → 钳到 %d" % (line_no, parts[0], lane, clamped))
            lane = clamped
        t = t_ms / 1000.0

        if kind & 128:                                  # mania 长按：endTime 在 hitSample 之前
            try:
                end_ms = _num(parts[5].split(":")[0])
            except (ValueError, IndexError):
                report.unparsable += 1
                report.warn("第 %d 行是长按但读不到结束时间：%s" % (line_no, line))
                continue
            if end_ms <= t_ms:
                report.removed["invalid_hold"] += 1
                report.warn("第 %d 行长按结束 %sms ≤ 开始 %sms，已移除" % (line_no, _fmt(end_ms), _fmt(t_ms)))
                continue
            note = {"type": "hold", "lane": lane, "time": t, "duration": (end_ms - t_ms) / 1000.0}
        elif kind & 1:
            note = {"type": "tap", "lane": lane, "time": t, "duration": 0.0}
        else:
            report.unsupported += 1
            report.warn("第 %d 行对象类型 %d 不是单键/长按（滑条或转盘），已跳过" % (line_no, kind))
            continue

        if t < 0:
            report.removed["negative_time"] += 1
            report.warn("第 %d 行时间 %sms < 0，已移除" % (line_no, _fmt(t_ms)))
            continue
        note["_line"] = line_no
        raw_notes.append(note)

    # 源文件顺序（只报告，不影响结果）
    if any(b["time"] < a["time"] for a, b in zip(raw_notes, raw_notes[1:])):
        report.info("源文件 HitObjects 未按时间排序，输出已重新排序")

    # 排序：时间 → 轨道 → 源行号（稳定、确定）
    raw_notes.sort(key=lambda n: (n["time"], n["lane"], n["_line"]))

    # 去重：同轨同刻只留源文件里先出现的那颗
    notes, seen = [], {}
    for n in raw_notes:
        key = (n["lane"], round(n["time"] * 1000))
        if key in seen:
            report.removed["duplicate"] += 1
            report.warn("第 %d 行与第 %d 行同轨同刻（轨 %d，%.3fs），保留先出现的"
                        % (n["_line"], seen[key], n["lane"], n["time"]))
            continue
        seen[key] = n["_line"]
        notes.append(n)

    # 同轨长按内还有音符：可能是生成器的毛病，只提醒不处理
    hold_end = {}
    for n in notes:
        end = hold_end.get(n["lane"])
        if end is not None and n["time"] < end[0] - 1e-9:
            report.warn("第 %d 行落在第 %d 行长按里面（轨 %d，%.3fs）" % (n["_line"], end[1], n["lane"], n["time"]))
        if n["type"] == "hold":
            if end is None or n["time"] + n["duration"] > end[0]:
                hold_end[n["lane"]] = (n["time"] + n["duration"], n["_line"])

    if not notes:
        report.error("转换后没有任何音符")
        return None, report

    # ---- 组装 ----
    out_notes = []
    for n in notes:
        report.counts[n["type"]] += 1
        out_notes.append({"type": n["type"], "lane": n["lane"],
                          "time": _r(n["time"]), "duration": _r(n["duration"]),
                          "direction": "up", "wide": False, "path": []})

    audio_name = audio if audio is not None else os.path.splitext(general.get("AudioFilename", ""))[0]
    if not audio_name:
        report.warn("没有音频文件名（General.AudioFilename 为空且未传 --audio），游戏里会跳过这张谱")
    if difficulty not in DIFFICULTIES:
        raise ValueError("difficulty 必须是 %s 之一" % "/".join(DIFFICULTIES))

    counts = {"tap": report.counts["tap"], "hold": report.counts["hold"], "drag": 0, "flick": 0, "slide": 0}
    chart = {
        "metadata": {
            "songName": title if title is not None else meta.get("Title", ""),
            "songArtist": meta.get("Artist", ""),
            "chartAuthor": meta.get("Creator", ""),
            "difficulty": DIFFICULTIES.index(difficulty),
            "level": int(level),
            "bpm": round(first["bpm"], 3),
            "offset": 0.0,
            "audioFileName": audio_name,
            "partName": part,
            "previewStartTime": 0.0,
            "previewDuration": 10.0,
            "formatVersion": 2,
            "laneCount": KEYS,
            "noteCounts": counts,
            "sourceMetadata": {
                "format": "osu!mania",
                "fileFormat": parsed["version"] or "",
                "sourceFile": source_name,
                "title": meta.get("Title", ""),
                "titleUnicode": meta.get("TitleUnicode", ""),
                "artist": meta.get("Artist", ""),
                "creator": meta.get("Creator", ""),
                "version": meta.get("Version", ""),
                "beatOffset": _r(first["time"]),
                "timingPoints": [{"time": _r(p["time"]), "bpm": round(p["bpm"], 3), "meter": p["meter"]}
                                 for p in timing],
            },
        },
        "notes": out_notes,
        "noteCounts": counts,          # 现有编辑器写在顶层，两处都写，读哪边都对
        "events": bpm_events,
    }

    problems = validate(chart)
    for p in problems:
        report.error("输出校验失败：" + p)
    return (chart if not problems else None), report


def _r(value):
    """秒保留 3 位（osu 时间是整数毫秒，3 位即无损），并消掉 -0.0。"""
    v = round(float(value), 3)
    return 0.0 if v == 0 else v


def _fmt(ms):
    return ("%d" % ms) if float(ms).is_integer() else ("%s" % ms)


# ============================================================================
# ③ Validate：对输出做需求 §10 的检查（转换逻辑正确时应全部通过——这是兜底）
# ============================================================================

def validate(chart):
    problems = []
    notes = chart.get("notes") or []
    lanes = (chart.get("metadata") or {}).get("laneCount", KEYS)
    prev = None
    keys = set()
    for i, n in enumerate(notes):
        if n["type"] not in ("tap", "hold"):
            problems.append("第 %d 颗类型 %r 不是 tap/hold" % (i, n["type"]))
        if not (0 <= n["lane"] < lanes):
            problems.append("第 %d 颗轨道 %d 不在 0–%d" % (i, n["lane"], lanes - 1))
        if n["time"] < 0:
            problems.append("第 %d 颗时间 %.3f < 0" % (i, n["time"]))
        if n["type"] == "hold" and not n["duration"] > 0:
            problems.append("第 %d 颗长按时长 %.3f ≤ 0" % (i, n["duration"]))
        if prev is not None and (n["time"], n["lane"]) < (prev["time"], prev["lane"]):
            problems.append("第 %d 颗未按时间排序" % i)
        key = (n["lane"], n["time"])
        if key in keys:
            problems.append("第 %d 颗重复（轨 %d，%.3fs）" % (i, n["lane"], n["time"]))
        keys.add(key)
        prev = n
    return problems


# ============================================================================
# 入口
# ============================================================================

def adapt_file(path, **kwargs):
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    return convert(parse_osu(text), source_name=os.path.basename(path), **kwargs)


def dumps(chart):
    return json.dumps(chart, ensure_ascii=False, indent=2) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description="osu!mania 4K → FallenAngel ChartData（4 轨原样）")
    parser.add_argument("input", help=".osu 文件")
    parser.add_argument("--out", required=True, help="输出 JSON（如 Assets/Resources/Charts/song_bass.json）")
    parser.add_argument("--part", required=True, help="乐器/声部名（Guitar / Bass / Drums …）")
    parser.add_argument("--difficulty", default="Normal", choices=DIFFICULTIES)
    parser.add_argument("--level", type=int, default=1)
    parser.add_argument("--audio", default=None, help="audioFileName（Resources/Audio 下的名字，不带扩展名）；缺省取 .osu 的 AudioFilename")
    parser.add_argument("--title", default=None, help="songName；缺省取 .osu 的 Title")
    parser.add_argument("--report", default=None, help="另存一份导入报告（不要放进 Resources/Charts）")
    args = parser.parse_args(argv)

    norm_report = os.path.normpath(args.report).replace("\\", "/") if args.report else ""
    if "Resources/Charts" in norm_report:
        parser.error("--report 不能放进 Resources/Charts（ChartLoader 会把它当谱面读）")

    chart, report = adapt_file(args.input, part=args.part, difficulty=args.difficulty,
                               level=args.level, audio=args.audio, title=args.title)
    text = report.to_text()
    sys.stdout.write(text)
    if args.report:
        os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(text)
    if chart is None:
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(dumps(chart))
    sys.stdout.write("→ %s\n" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
