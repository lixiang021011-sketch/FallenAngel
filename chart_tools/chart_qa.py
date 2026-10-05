# -*- coding: utf-8 -*-
"""入库前体检：把"AI 谱面"和它对应的分轨摆在一起比，列出可疑时间段。

为什么需要：模型偶尔会在乐器已停下的尾奏里继续排音符、把某段采空、或在某段只写极少音符；
这些只有打到那一段才发现。本脚本把它们在**入库前**列出来（作者 2026-10-05 要求）。

三类检查（按 10 秒一段）：
  ① 尾部越界：谱面末音 > 该声部最后一个起音 + 0.3s
  ② 静音段有音符：音频活跃度 < 1%，谱面却有音符
  ③ 有声段过疏：音频活跃度正常，但音符数不到"该谱平均密度"的 1/3

用法：<venv>/python chart_tools/chart_qa.py [Charts 目录] [Audio 目录]
"""
import json
import os
import subprocess
import sys

import numpy as np

BUCKET = 10.0
SILENT_ACT = 1.0        # 活跃度低于此视为静音段
ACTIVE_ACT = 8.0        # 活跃度高于此视为有内容
SPARSE_RATIO = 0.34     # 低于全曲平均密度的这个比例 → 过疏


def flux(path, sr=22050, hop=512, win=1024):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(sr),
                          "-f", "f32le", "-"], capture_output=True).stdout
    x = np.frombuffer(raw, dtype="<f4").astype(np.float64)
    n = 1 + (len(x) - win) // hop
    e = np.empty(n)
    prev = np.zeros(win // 2 + 1)
    for i in range(n):
        spec = np.abs(np.fft.rfft(x[i * hop:i * hop + win] * np.hanning(win)))
        e[i] = np.maximum(spec - prev, 0).sum()
        prev = spec
    return e, sr / hop


def onsets_of(e, fps):
    thr = e.mean() + 0.5 * e.std()
    return [i / fps for i in range(1, len(e) - 1)
            if e[i] > thr and e[i] >= e[i - 1] and e[i] > e[i + 1]]


def check(chart_path, audio_dir):
    chart = json.load(open(chart_path, encoding="utf-8"))
    meta = chart.get("metadata") or {}
    part = meta.get("partName")
    stem = None
    for s in meta.get("stems") or []:
        if s.get("partName") == part:
            stem = s.get("audioFileName")
    name = os.path.basename(chart_path)
    if not stem:
        return "%s：没有对应分轨（跳过）" % name, []
    audio = os.path.join(audio_dir, stem + ".ogg")
    if not os.path.exists(audio):
        return "%s：分轨文件缺失 %s" % (name, stem), []

    e, fps = flux(audio)
    onsets = onsets_of(e, fps)
    thr = e.mean() + 0.5 * e.std()
    times = np.array(sorted(n["time"] for n in chart["notes"])) if chart["notes"] else np.zeros(0)
    issues = []

    # ① 尾部越界
    if times.size and onsets:
        last = max(onsets)
        tail = times[times > last + 0.30]
        if tail.size:
            issues.append("尾部越界：末音 %.2fs，最后一个起音 %.2fs（多 %d 个音符，可裁到 %.2fs）"
                          % (times[-1], last, tail.size, last + 0.30))

    # ②③ 分段
    total_notes = times.size
    active_seconds = float((e > thr).sum()) / fps
    avg_density = total_notes / max(active_seconds, 1.0)
    for start in np.arange(0, len(e) / fps, BUCKET):
        seg = e[int(start * fps):int(min(start + BUCKET, len(e) / fps) * fps)]
        act = 100.0 * float((seg > thr).mean()) if len(seg) else 0.0
        count = int(((times >= start) & (times < start + BUCKET)).sum())
        if act < SILENT_ACT and count > 0:
            issues.append("静音段有音符：%d-%ds（活跃 %.1f%%，音符 %d）" % (start, start + BUCKET, act, count))
        elif act > ACTIVE_ACT and count < SPARSE_RATIO * avg_density * BUCKET * (act / 100.0):
            issues.append("有声段过疏：%d-%ds（活跃 %.1f%%，音符 %d，该谱平均 %.1f 音/有效秒）"
                          % (start, start + BUCKET, act, count, avg_density))
    header = "%-42s 音符 %4d  末音 %6.1fs  %s" % (
        name[:42], total_notes, times[-1] if times.size else 0.0,
        "✅ 无问题" if not issues else "⚠ %d 处" % len(issues))
    return header, issues


def main(argv=None):
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = list(argv if argv is not None else sys.argv[1:])
    charts_dir = args[0] if args else os.path.join("Assets", "Resources", "Charts")
    audio_dir = args[1] if len(args) > 1 else os.path.join("Assets", "Resources", "Audio")
    files = sorted(f for f in os.listdir(charts_dir)
                   if f.endswith(".json") and "AI4K" in f and "voxel" not in f)
    flagged = 0
    for f in files:
        header, issues = check(os.path.join(charts_dir, f), audio_dir)
        print(header)
        for issue in issues:
            print("      " + issue)
            flagged += 1
    print("\n共检查 %d 张谱，可疑 %d 处" % (len(files), flagged))
    return 0


if __name__ == "__main__":
    sys.exit(main())
