# -*- coding: utf-8 -*-
"""
FallenAngel 谱面制作工具·第四步：.chart 转换器（Moonscraper / Clone Hero 格式）
用法: python convert_chart.py <input.chart> [输出路径] [选项]

  --song 名          谱面 audioFileName（Resources/Audio 下的音频文件名，不含扩展名）
  --kick-lane N      底鼓（fret 5）映射到轨道 N（默认 0，与 AI 鼓谱 K0 约定一致）
  --author 名        谱面作者（默认取 .chart 的 Charter）
  --level N          等级数字（默认 7）

.chart 格式要点:
  [Song]        Resolution(每四分音符 tick 数)、Offset(秒)、Name/Artist/Charter
  [SyncTrack]   时间签名 TS n/d、BPM 事件 B <微BPM>（如 140000 = 140.0）
  [ExpertDrums] 鼓谱事件 "pos = N fret length"（pos/length 单位 tick）
                fret 0-3 = 四个鼓垫 → 轨道 0-3；fret 5 = 底鼓 → kick-lane
                length > 0 = 持续音（长按），时长 = pos+length 的换算时间差
时间换算: 沿 SyncTrack 逐 tick 累加 60/bpm/resolution（支持变速）
"""
import sys
import os
import json


def parse_chart(path):
    """解析 .chart 为 {节名: [(pos, type, arg), ...]} 与 song 元数据字典"""
    with open(path, encoding="utf-8-sig") as f:
        lines = f.read().splitlines()

    sections = {}
    song = {}
    cur = None
    cur_items = None

    for raw in lines:
        line = raw.strip()
        if not line or line.startswith("//") or line in ("{", "}"):
            continue
        if line.startswith("["):
            cur = line.strip("[]")
            if cur == "Song":
                cur_items = song
            else:
                cur_items = []
                sections[cur] = cur_items
            continue
        if cur_items is None:
            continue
        if cur == "Song":
            if "=" in line:
                k, v = line.split("=", 1)
                song[k.strip()] = v.strip().strip('"')
        else:
            parts = line.split("=", 1)
            if len(parts) != 2:
                continue
            pos = int(parts[0].strip())
            toks = parts[1].strip().split()
            typ = toks[0]
            arg = int(toks[1]) if len(toks) > 1 else 0
            cur_items.append((pos, typ, arg))
    return sections, song


def extract_notes(path, section="ExpertDrums"):
    """提取鼓谱音符事件（三列格式: pos = N fret length），返回排序列表"""
    with open(path, encoding="utf-8-sig") as f:
        lines = f.read().splitlines()

    notes = []
    in_section = False
    for raw in lines:
        line = raw.strip()
        if not line:
            continue
        if line.startswith("["):
            in_section = (line.strip("[]") == section)
            continue
        if not in_section or "=" not in line or line in ("{", "}"):
            continue
        head, tail = line.split("=", 1)
        toks = tail.split()
        if len(toks) >= 2 and toks[0] == "N":
            fret = int(toks[1])
            length = int(toks[2]) if len(toks) > 2 else 0
            notes.append((int(head.strip()), fret, length))
    notes.sort()
    return notes


def build_time_map(sync, resolution, max_tick):
    """逐 tick 累计时间：返回 tick -> 秒 的表"""
    bpm_events = sorted([(p, a) for p, t, a in sync if t == "B"])
    if not bpm_events:
        bpm_events = [(0, 120000)]
    times = [0.0] * (max_tick + 1)
    bpm_idx = 0
    cur_bpm = bpm_events[0][1] / 1000.0
    for tick in range(1, max_tick + 1):
        while bpm_idx + 1 < len(bpm_events) and bpm_events[bpm_idx + 1][0] <= tick:
            bpm_idx += 1
            cur_bpm = bpm_events[bpm_idx][1] / 1000.0
        times[tick] = times[tick - 1] + 60.0 / cur_bpm / resolution
    return times, bpm_events


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    opts = {}
    i = 1
    while i < len(sys.argv):
        a = sys.argv[i]
        if a.startswith("--") and i + 1 < len(sys.argv):
            opts[a[2:]] = sys.argv[i + 1]
            i += 2
        else:
            i += 1

    if not args:
        print(__doc__)
        return
    in_path = args[0]
    out_path = args[1] if len(args) > 1 else os.path.splitext(in_path)[0] + ".bytes"

    sections, song = parse_chart(in_path)
    if "SyncTrack" not in sections:
        print("错误: .chart 缺少 [SyncTrack] 节")
        return

    resolution = int(song.get("Resolution", 192))
    sync = sections["SyncTrack"]
    notes_raw = extract_notes(in_path, "ExpertDrums")
    if not notes_raw:
        print("错误: 未在 [ExpertDrums] 节找到音符（请在 Moonscraper 鼓模式下导出）")
        return

    max_tick = max([p + l for p, f, l in notes_raw] + [p for p, t, a in sync] + [0])
    times, bpm_events = build_time_map(sync, resolution, max_tick)

    kick_lane = int(opts.get("kick-lane", 0))
    song_name = opts.get("song", os.path.splitext(os.path.basename(in_path))[0])

    out_notes = []
    long_id = 0
    skipped = 0
    for pos, fret, length in notes_raw:
        t0 = times[min(pos, max_tick)]
        if fret in (0, 1, 2, 3):
            lane = fret
        elif fret == 5:
            lane = kick_lane
        else:
            skipped += 1
            continue
        if length > 0:
            t1 = times[min(pos + length, max_tick)]
            dur = round(t1 - t0, 3)
            if dur < 0.2:
                out_notes.append({"lane": lane, "time": round(t0, 3), "type": 0,
                                  "duration": 0.0, "longNoteId": -1})
                continue
            out_notes.append({"lane": lane, "time": round(t0, 3), "type": 1,
                              "duration": dur, "longNoteId": long_id})
            out_notes.append({"lane": lane, "time": round(t1, 3), "type": 3,
                              "duration": 0.0, "longNoteId": long_id})
            long_id += 1
        else:
            out_notes.append({"lane": lane, "time": round(t0, 3), "type": 0,
                              "duration": 0.0, "longNoteId": -1})
    out_notes.sort(key=lambda n: (n["time"], n["lane"]))

    first_bpm = bpm_events[0][1] / 1000.0
    chart = {
        "metadata": {
            "songName": song.get("Name", song_name),
            "songArtist": song.get("Artist", "Unknown"),
            "chartAuthor": opts.get("author", song.get("Charter", "Manual")),
            "difficulty": 1,
            "level": int(opts.get("level", 7)),
            "bpm": first_bpm,
            "offset": float(song.get("Offset", 0.0)),
            "audioFileName": song_name,
            "previewStartTime": 0.0,
            "previewDuration": 15.0,
        },
        "notes": out_notes,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(chart, f, ensure_ascii=False, indent=1)

    print(f"转换完成: {in_path} -> {out_path}")
    print(f"  音符 {len(out_notes)}（长按 {long_id} 个，跳过 fret {skipped} 个）")
    if out_notes:
        print(f"  BPM {first_bpm}，offset {chart['metadata']['offset']}s，时长 {out_notes[-1]['time']:.1f}s")
    else:
        print(f"  BPM {first_bpm}，offset {chart['metadata']['offset']}s，无音符")


if __name__ == "__main__":
    main()
