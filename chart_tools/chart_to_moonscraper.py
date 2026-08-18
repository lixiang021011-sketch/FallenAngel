# -*- coding: utf-8 -*-
"""
FallenAngel chart v2 → Moonscraper / Clone Hero .chart 导出器（可视化检查/人工微调用）

用法: python chart_to_moonscraper.py <input.chart.json> [--out 输出.chart]

Moonscraper 不认 JSON，只认 .chart 文本；本工具把 v2 谱面转成 .chart 供其在
编辑器中查看/微调。

格式约定（5 键谱面；Moonscraper 吉他模式为 5 键，见 convert_chart.py 同源格式）：
- [ExpertGuitar]：数字类型，fret 0-4 = 绿红黄蓝橙；tap/drag/flick → "pos = N 0"；
  hold → "pos = 5+N len"（sustain 长条）；slide → "pos = 7 -1 0"（GH3 绿滑条，
  arg -1 标记，区别于黄 sustain 的 "= 7 0 len"），path 转折点连成滑条链
- [ExpertDrums]：鼓视图（4 垫 + 底鼓），"pos = N <fret> <length>"，fret 0-3 → lane 0-3；
  lane 4 音符鼓视图不表达（计数报告）；hold/slide → length > 0 长按
- 文件头 UTF-8 BOM、行尾 CRLF、[Events] 空节，与 Moonscraper 自身导出一致

tick 换算: Resolution = 480（.chart 标准），沿 BPM 事件段积分 秒 → tick。
           IR 网格 768 → 480 会引入 ≤2 tick 取整误差（0.1ms 级，可视化无感）。
"""
import json
import sys

RESOLUTION = 480


def build_time_to_tick(events):
    """events 通道 BPM 段 → (tick 积分函数, 归一化 BPM 段)。

    归一化：tick 0 处必有 B 事件（首段 BPM），否则 Moonscraper 对无 BPM 段
    用默认速度积分，时间线错乱。前端平移过的时间在此如实积分进 tick——
    即 tick 已含 offset，[Song].Offset 保持 0。
    """
    bpms = sorted((e["time"], e["value"]) for e in events if e["type"] == "bpm")
    if not bpms:
        bpms = [(0.0, 120.0)]
    if bpms[0][0] > 0:
        bpms.insert(0, (0.0, bpms[0][1]))

    def tick_of(t):
        tick = 0.0
        prev_t, prev_b = bpms[0][0], bpms[0][1]
        for t0, b in bpms[1:]:
            if t0 >= t:
                break
            tick += (t0 - prev_t) * prev_b * RESOLUTION / 60.0
            prev_t, prev_b = t0, b
        tick += max(t - prev_t, 0.0) * prev_b * RESOLUTION / 60.0
        return tick
    return tick_of, bpms


def _guitar_lines(notes, tick_of):
    """5 键吉他节内容（[ExpertGuitar]/[ExpertSingle] 共用）：
    数字类型 0-4 = 绿红黄蓝橙，5-9 = 对应 sustain 长条，
    滑条 "= 7 -1 0"（arg -1 标记，区别于黄 sustain 的 "= 7 0 len"），path 转折点连成滑条链"""
    lines = []
    for n in sorted(notes, key=lambda x: (x["time"], x.get("lane", 0))):
        pos = int(round(tick_of(n["time"])))
        lane = min(max(n.get("lane", 0), 0), 4)
        if n["type"] == "slide":
            lines.append(f"  {pos} = 7 -1 0")
            path = n.get("path") or []
            for p in path[1:]:
                pt = int(round(tick_of(n["time"] + p["t"])))
                if pt > pos:
                    lines.append(f"  {pt} = 7 -1 0")
        elif n["type"] == "hold":
            dur = n.get("duration", 0.0)
            ln = max(int(round(tick_of(n["time"] + dur) - pos)), 1)
            lines.append(f"  {pos} = {5 + lane} {ln}")
        else:  # tap / drag / flick → 普通 note（GH 无这些概念，以 tap 呈现）
            lines.append(f"  {pos} = {lane} 0")
    return lines


def to_chart_text(metadata, events, notes):
    tick_of, bpms = build_time_to_tick(events)
    out = []
    out.append("[Song]")
    out.append("{")
    out.append(f'  Name = "{metadata.get("songName", "untitled")}"')
    out.append(f'  Artist = "{metadata.get("songArtist", "unknown")}"')
    out.append('  Album = ""')
    out.append('  Genre = ""')
    out.append(f'  Charter = "{metadata.get("chartAuthor", "auto")}"')
    out.append(f"  Resolution = {RESOLUTION}")
    out.append("  Offset = 0")  # 前端已把时间轴平移进 tick（见 build_time_to_tick），Offset 保持 0
    out.append("  Difficulty = 0")
    out.append("  PreviewStart = 0")
    out.append("  PreviewEnd = 0")
    out.append('  MediaType = "cd"')
    out.append('  MusicStream = ""')
    out.append("}")
    out.append("")

    out.append("[SyncTrack]")
    out.append("{")
    out.append("  0 = TS 4")
    for t, b in bpms:  # 归一化段：tick 0 处必有 B 事件（首段 BPM）
        out.append(f"  {int(round(tick_of(t)))} = B {int(round(b * 1000))}")
    out.append("}")
    out.append("")

    out.append("[Events]")
    out.append("{")
    out.append("}")
    out.append("")

    # 吉他视图（Moonscraper 吉他模式为 5 键）：数字类型 0-4 = 绿红黄蓝橙，
    # 5-9 = 对应 sustain 长条，滑条 "= 7 -1"（arg -1 标记，区别于黄 sustain 的 "= 7 0 len"）
    guitar_lines = _guitar_lines(notes, tick_of)
    out.append("[ExpertGuitar]")
    out.append("{")
    out.extend(guitar_lines)
    out.append("}")
    out.append("")

    # GH1 兼容节名（旧版 Moonscraper 只认 [ExpertSingle]，Clone Hero 两者皆可）
    out.append("[ExpertSingle]")
    out.append("{")
    out.extend(guitar_lines)
    out.append("}")
    out.append("")

    # 鼓视图（4 垫 + 底鼓）：fret 0-3 = 四个鼓垫 → lane 0-3；lane 4 音符鼓视图不表达，计数报告
    out.append("[ExpertDrums]")
    out.append("{")
    dropped4 = 0
    for n in sorted(notes, key=lambda x: (x["time"], x.get("lane", 0))):
        pos = int(round(tick_of(n["time"])))
        lane = n.get("lane", 0)
        if lane > 3:
            dropped4 += 1
            continue
        fret = min(max(lane, 0), 3)
        if n["type"] == "hold":
            dur = n.get("duration", 0.0)
            ln = max(int(round(tick_of(n["time"] + dur) - pos)), 1)
        elif n["type"] == "slide":
            dur = n.get("duration", 0.0)  # 鼓格式无滑条：起点长按保留持续时长
            ln = max(int(round(tick_of(n["time"] + dur) - pos)), 1)
        else:  # tap / drag / flick → 普通鼓点
            ln = 0
        out.append(f"  {pos} = N {fret} {ln}")
    out.append("}")
    if dropped4:
        print(f"  ⚠ 鼓视图丢弃 lane 4 音符 {dropped4} 个（鼓 4 垫无第 5 轨，请用吉他视图）")
    return "\n".join(out) + "\n"


def main():
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # 中文名在 GBK 控制台会崩
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return
    src = args[0]
    out_path = None
    i = 1
    while i < len(args):
        if args[i] == "--out" and i + 1 < len(args):
            out_path = args[i + 1]
            i += 2
        else:
            i += 1
    if out_path is None:
        out_path = src.rsplit(".", 1)[0] + ".chart"

    chart = json.load(open(src, encoding="utf-8"))
    text = to_chart_text(chart.get("metadata", {}),
                         chart.get("events", []),
                         chart.get("notes", []))
    with open(out_path, "w", encoding="utf-8-sig", newline="\r\n") as f:  # BOM + CRLF，与 Moonscraper 一致
        f.write(text)

    n_notes = len(chart.get("notes", []))
    n_slide = sum(1 for n in chart.get("notes", []) if n["type"] == "slide")
    n_hold = sum(1 for n in chart.get("notes", []) if n["type"] == "hold")
    print(f"导出完成: {src} -> {out_path}")
    print(f"  音符 {n_notes}（slide 滑条 {n_slide} 处、hold 长条 {n_hold} 个，其余按 tap 呈现）")
    print("  Moonscraper 打开: 文件 → Open → 选 .chart；吉他视图（5 键）读 [ExpertGuitar]，鼓视图读 [ExpertDrums]")


if __name__ == "__main__":
    main()
