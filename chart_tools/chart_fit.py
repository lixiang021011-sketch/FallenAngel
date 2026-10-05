# -*- coding: utf-8 -*-
"""AI/导入谱面的"游戏化修整"：对轨（吸附到真实起音）、减密、限同轨连打。

用途：Mapperatorinator 等工具产出的谱面，音符密度与"是否踩在乐器真实起音上"都不能直接信。
本脚本只读音频、只改谱面音符，不碰游戏代码。

三种修整（可单选/组合）：
  --fit-audio <音频>  对轨：把音符吸附到音频检测到的起音（±容差内）；偏离太远的音符直接删
  --max-nps <N>       减密：1 秒滑窗内超过 N 就丢音（保留强拍优先）
  --max-burst <N>     爆发限制：0.25 秒窗口内最多 N 个
  --max-jack <N>      同轨连打上限：同一轨连续快速敲击超过 N 就丢后面的

依赖 numpy（与 AI 工具链同一个 venv 即可）。用法：
  <venv>/python chart_tools/chart_fit.py chart.json --fit-audio song_bass.ogg --max-nps 9 --max-burst 4 --max-jack 3
"""
import argparse
import json
import os
import subprocess
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from chart_import import thin_to_nps          # noqa: E402  复用既有的减密（强拍优先）


def detect_onsets(path, sr=22050, hop=256, win=1024, threshold=1.2, min_gap=0.05):
    """谱通量起音检测：返回秒序列（约 ±12ms 精度）。"""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(sr),
                          "-f", "f32le", "-"], capture_output=True).stdout
    samples = np.frombuffer(raw, dtype="<f4").astype(np.float64)
    frames = 1 + (len(samples) - win) // hop
    flux = np.empty(frames)
    previous = np.zeros(win // 2 + 1)
    for i in range(frames):
        spectrum = np.abs(np.fft.rfft(samples[i * hop:i * hop + win] * np.hanning(win)))
        flux[i] = np.maximum(spectrum - previous, 0).sum()
        previous = spectrum
    flux = (flux - flux.mean()) / (flux.std() + 1e-9)
    times = []
    for i in range(1, frames - 1):
        if flux[i] > threshold and flux[i] >= flux[i - 1] and flux[i] > flux[i + 1]:
            t = i * hop / sr
            if not times or t - times[-1] > min_gap:
                times.append(t)
    return np.array(times)


def fit_to_onsets(notes, onsets, tolerance=0.06, drop_unmatched=True):
    """把音符吸附到最近的起音；超过容差的音符判定为"多采的音"丢弃。返回 (保留, 吸附数, 丢弃数)。"""
    if len(onsets) == 0:
        return notes, 0, 0
    kept, snapped, dropped = [], 0, 0
    for note in notes:
        t = float(note["time"])
        i = int(np.searchsorted(onsets, t))
        cands = [onsets[j] for j in (i - 1, i) if 0 <= j < len(onsets)]
        if not cands:
            dropped += 1
            continue
        nearest = min(cands, key=lambda v: abs(v - t))
        if abs(nearest - t) <= tolerance:
            if abs(nearest - t) > 0.001:
                note["time"] = round(float(nearest), 3)
                snapped += 1
            kept.append(note)
        elif not drop_unmatched:
            kept.append(note)          # 只对轨不删音（复音乐器检测不全时用）
        else:
            dropped += 1
    return kept, snapped, dropped


def cap_burst(notes, max_burst, window=0.25):
    """任意 window 秒窗口内最多 max_burst 个音（丢窗口里最靠后的多余音）。"""
    kept = 0
    notes = sorted(notes, key=lambda n: (n["time"], n["lane"]))
    out = list(notes)
    guard = 0
    while guard < 20000:
        guard += 1
        worst, worst_count = None, 0
        times = [n["time"] for n in out]
        for t in times:
            count = sum(1 for x in times if t <= x < t + window)
            if count > worst_count:
                worst, worst_count = t, count
        if worst is None or worst_count <= max_burst:
            break
        window_notes = [n for n in out if worst <= n["time"] < worst + window]
        victim = window_notes[-1]           # 同簇里最靠后的先丢（保留先到的落手）
        out.remove(victim)
        kept += 1
    return out, kept


def cap_jack(notes, max_jack, window=0.30):
    """同轨快速连打上限：连续同轨且间隔 < window 的第 max_jack+1 个及之后丢弃。"""
    out, dropped = [], 0
    run_lane, run_len, prev_time = None, 0, None
    for note in sorted(notes, key=lambda n: (n["time"], n["lane"])):
        lane, t = note["lane"], note["time"]
        if lane == run_lane and prev_time is not None and t - prev_time < window:
            run_len += 1
        else:
            run_lane, run_len = lane, 1
        if max_jack > 0 and run_len > max_jack:
            dropped += 1
            continue
        prev_time = t
        out.append(note)
    return out, dropped


def thin_chords(notes, bpm, offset=0.0, beat_tolerance=0.03, keep_on_beat=True, max_chord=2):
    """和弦瘦身：同刻多音只保留 ≤max_chord 个，且（可选）只保留落在正拍上的和弦。

    为什么需要：本作普通音符横向判定"铺满整屏"，而判定侧有 ~20ms 同刻容差——
    同刻双押过多时，一次按键会把整个和弦一起判掉（作者反馈"多判 / 一次触发两个"）。
    作者自己的谱面里和弦很少（pjira/pjsk 上百、cc 包为 0），这里对齐同一口径。
    """
    beat = 60.0 / bpm if bpm > 0 else 0.5
    groups = {}
    for note in notes:
        groups.setdefault(round(note["time"], 3), []).append(note)
    kept, dropped = [], 0
    for time in sorted(groups):
        group = sorted(groups[time], key=lambda n: n["lane"])
        on_beat = abs((time - offset) / beat - round((time - offset) / beat)) * beat <= beat_tolerance
        limit = max_chord if (on_beat or not keep_on_beat) else 1
        kept.extend(group[:limit])
        dropped += max(0, len(group) - limit)
    return kept, dropped


def main(argv=None):
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description="AI 谱面游戏化修整（对轨/减密/限连打）")
    parser.add_argument("chart")
    parser.add_argument("--audio", help="对轨参考音频（通常是该声部的分轨）")
    parser.add_argument("--fit-tolerance", type=float, default=0.06, help="吸附容差（秒）")
    parser.add_argument("--fit-no-drop", action="store_true",
                        help="只吸附不删音（复音乐器起音检测不全时用，避免误删）")
    parser.add_argument("--max-nps", type=float, default=0.0)
    parser.add_argument("--max-burst", type=int, default=0)
    parser.add_argument("--max-jack", type=int, default=0)
    parser.add_argument("--chord-thin", action="store_true",
                        help="和弦瘦身：只保留正拍双押，其余同刻多音降为单音（对齐本作判定口径）")
    parser.add_argument("--out", help="输出路径（缺省原地覆盖）")
    parser.add_argument("--dry", action="store_true", help="只报告不写回")
    args = parser.parse_args(argv)

    with open(args.chart, encoding="utf-8") as handle:
        chart = json.load(handle)
    notes = chart.get("notes") or []
    before = len(notes)

    if args.audio:
        onsets = detect_onsets(args.audio)
        notes, snapped, dropped = fit_to_onsets(notes, onsets, args.fit_tolerance,
                                                drop_unmatched=not args.fit_no_drop)
        print("  对轨：参考起音 %d 个；吸附 %d、丢弃多采 %d" % (len(onsets), snapped, dropped))
    if args.max_nps > 0:
        chart["notes"] = notes
        removed, peak = thin_to_nps(chart, args.max_nps)
        notes = chart["notes"]
        print("  减密：丢 %d（上限 %.1f 音/秒，减后峰值 %d）" % (removed, args.max_nps, peak))
    if args.max_burst > 0:
        notes, dropped = cap_burst(notes, args.max_burst)
        print("  爆发限制：丢 %d（0.25 秒上限 %d）" % (dropped, args.max_burst))
    if args.max_jack > 0:
        notes, dropped = cap_jack(notes, args.max_jack)
        print("  同轨连打：丢 %d（上限 %d 连）" % (dropped, args.max_jack))
    if args.chord_thin:
        notes, dropped = thin_chords(notes, float(chart.get("metadata", {}).get("bpm") or 0.0),
                                     float(chart.get("metadata", {}).get("offset") or 0.0))
        print("  和弦瘦身：丢 %d（同刻多音只留正拍双押）" % dropped)

    notes.sort(key=lambda n: (n["time"], n["lane"]))
    chart["notes"] = notes
    counts = {}
    for note in notes:
        counts[note["type"]] = counts.get(note["type"], 0) + 1
    chart.setdefault("metadata", {})["noteCounts"] = counts
    print("  %s：%d → %d 个音符" % (os.path.basename(args.chart), before, len(notes)))
    if args.dry:
        return 0
    out = args.out or args.chart
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(chart, handle, ensure_ascii=False, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
