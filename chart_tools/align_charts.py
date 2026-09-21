# -*- coding: utf-8 -*-
"""谱面整体对齐校正：用**音频起音**回查谱面，把 `metadata.offset` 校到起音上。

为什么需要：作者反馈"谱面本身和乐曲还是没有对齐"。实测（起音回查）—

  pjsk_0001_tell_your_world  −19.3ms
  pjsk_0003_teo               −8.3ms
  pjsk_0008_time_machine      +6.1ms
  pjsk_0010_happy_synthesizer −14.6ms      （P90 抖动 ~25ms）
  cc_bit_shift（自动成谱）      +0.0ms      （P90 0.4ms）

即第三方谱面的 offset 普遍有 6~20ms 的系统偏差 —— 单听是"略微不跟手"，
叠加输出延迟补偿后就明显了。本工具按每首的**偏差中位数**整体平移 offset：

  谱面时刻 T 的声音位置 = T + offset（本工程 `SongTime = 音频位置 − offset`）
  实测 dev = 起音时刻 − (T + offset)
  ⇒ offset_new = offset + median(dev)

**默认只读**（诊断），要真写回必须显式 `--apply`：
  python -X utf8 chart_tools/align_charts.py Assets/Resources/Charts/pjsk_*.json
  python -X utf8 chart_tools/align_charts.py --apply Assets/Resources/Charts/pjsk_*.json

⚠ 已知限制（实测踩过，别当它是万能的）：互相关看的是"整条脉冲串 vs 谱通量"的最优平移，
遇到强周期性伴奏会锁到**另一拍**：本机实测 pjsk_0008 读出 +135ms（≈ 150BPM 的下半拍），
显然是错的。所以这个工具只用于**诊断/找可疑项**，第三方谱面的最终对齐要么靠谱师工具
（如 osu! 的 timing 界面）逐拍听，要么由玩家的实战命中误差统计（我们的 osu! 式建议偏移）吸收。
只有"按构造对齐"的曲包（`cc_song_import.py` 生成）可以用它复核，且读数应接近 0（实测残留 +5ms）。
"""
import argparse
import glob
import json
import os
import shutil
import sys

import numpy as np
import miniaudio

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")
FRAME = 1024                # 23ms 窗：短窗才贴近"起音"（长窗会把能量抹开，测出来的偏移跟着窗长跑）
HOP = 128                   # 2.9ms/帧
MIN_APPLY_MS = 3.0          # 偏差小于 3ms 就不动（避免把噪声写进数据）
MAX_SHIFT_MS = 150          # 互相关搜索范围
GRID_MS = 5.0               # 互相关网格
SIGMA_MS = 12.0             # 音符脉冲的高斯宽度
SAMPLE_LIMIT = 100000


def decode(path):
    d = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.FLOAT32, nchannels=1)
    return np.asarray(d.samples, dtype=np.float32), d.sample_rate


def flux_envelope(x):
    n = 1 + (len(x) - FRAME) // HOP
    window = np.hanning(FRAME).astype(np.float32)
    mags = np.empty((n, FRAME // 2 + 1), dtype=np.float32)
    for i in range(n):
        mags[i] = np.abs(np.fft.rfft(x[i * HOP:i * HOP + FRAME] * window))
    return np.maximum(np.diff(mags, axis=0), 0).sum(axis=1)


def best_shift(chart, audio_path, limit=SAMPLE_LIMIT):
    """互相关测整体偏移：返回 (shift 秒, 相关系数, 音符数)。

    为什么不逐音符取中位数：音符不是等间隔的，逐点搜峰会锁到邻近的另一个峰 ——
    实测同一份"按构造对齐"的谱面，换个 FFT 窗长就能读出 −13ms 的假偏差，而且校正后不收敛。
    互相关看的是**整条脉冲串**和音频谱通量的最优平移，是测全局偏移的标准做法。

    约定：谱面音符的声音位置 = note.time + offset（本工程 `SongTime = 音频位置 − offset`）。
    互相关峰在 d 表示"音频起音比谱面晚 d" ⇒ `offset_new = offset + d`。
    """
    x, sr = decode(audio_path)
    flux = flux_envelope(x)
    fps = sr / HOP
    offset = chart["metadata"]["offset"]

    if not chart["notes"]:
        return 0.0, 0.0, 0

    duration = len(x) / sr
    grid = GRID_MS / 1000.0
    k = np.arange(0, duration, grid)
    # 音频谱通量 → 网格
    src_t = np.arange(len(flux)) / fps
    f = np.interp(k, src_t, flux, left=0.0, right=0.0)
    f = f - f.mean()

    # 谱面 → 网格上的高斯脉冲串
    p = np.zeros_like(k)
    sigma = SIGMA_MS / 1000.0
    for note in chart["notes"][:limit]:
        t = note["time"] + offset
        if t < -0.2 or t > duration + 0.2:
            continue
        lo = max(0, int((t - 4 * sigma) / grid))
        hi = min(len(k), int((t + 4 * sigma) / grid) + 1)
        if hi > lo:
            p[lo:hi] += np.exp(-0.5 * ((k[lo:hi] - t) / sigma) ** 2)

    norm = (np.linalg.norm(p) * np.linalg.norm(f)) + 1e-9
    max_lag = int(MAX_SHIFT_MS / 1000.0 / grid)
    best, best_lag = -1.0, 0
    for lag in range(-max_lag, max_lag + 1):
        if lag >= 0:
            a, b = p[: len(p) - lag], f[lag:]
        else:
            a, b = p[-lag:], f[: len(f) + lag]
        score = float(np.dot(a, b)) / norm
        if score > best:
            best, best_lag = score, lag

    # 抛物线插值到亚网格
    sub = 0.0
    def score_at(lag):
        if lag >= 0:
            a, b = p[: len(p) - lag], f[lag:]
        else:
            a, b = p[-lag:], f[: len(f) + lag]
        return float(np.dot(a, b)) / norm
    if -max_lag < best_lag < max_lag:
        y0, y1, y2 = score_at(best_lag - 1), best, score_at(best_lag + 1)
        denom = y0 - 2 * y1 + y2
        if abs(denom) > 1e-12:
            sub = float(np.clip(0.5 * (y0 - y2) / denom, -1.0, 1.0))
    return (best_lag + sub) * grid, best, len(chart["notes"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("charts", nargs="+", help="谱面 JSON（支持通配符，由 shell 展开或本脚本展开）")
    ap.add_argument("--apply", action="store_true", help="真的写回（默认只诊断，不写文件）")
    ap.add_argument("--dry-run", action="store_true", help="保留的老参数，等价于默认行为")
    ap.add_argument("--backup", action="store_true", default=True,
                    help="首次校正前留一份 .orig.json（默认开）")
    args = ap.parse_args()

    paths = []
    for pattern in args.charts:
        paths.extend(sorted(glob.glob(pattern)) or [pattern])

    changed = 0
    for path in paths:
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as f:
            chart = json.load(f)
        audio_name = chart.get("metadata", {}).get("audioFileName")
        if not audio_name:
            print("[align] 跳过（没写音频名）：" + os.path.basename(path))
            continue
        audio = None
        for ext in (".mp3", ".ogg", ".wav"):
            candidate = os.path.join(AUDIO_DIR, audio_name + ext)
            if os.path.exists(candidate):
                audio = candidate
                break
        if audio is None:
            print("[align] 跳过（音频缺失 %s）：%s" % (audio_name, os.path.basename(path)))
            continue

        dev, score, samples = best_shift(chart, audio)
        old = chart["metadata"]["offset"]
        if abs(dev) * 1000.0 < MIN_APPLY_MS:
            print("[align] %-34s 偏移 %+6.1fms（相关 %.3f，%d 音符）→ 不动" %
                  (os.path.basename(path), dev * 1000, score, samples))
            continue

        if args.apply and not args.dry_run:
            if args.backup and not os.path.exists(path + ".orig.json"):
                shutil.copyfile(path, path + ".orig.json")
            chart["metadata"]["offset"] = round(old + dev, 3)
            with open(path, "w", encoding="utf-8") as f:
                json.dump(chart, f, ensure_ascii=False, indent=2)

        print("[align] %-34s 偏移 %+6.1fms（相关 %.3f）→ offset %.3f → %.3f%s" %
              (os.path.basename(path), dev * 1000, score, old, old + dev,
               "" if args.apply and not args.dry_run else "（诊断，未写回）"))
        changed += 1

    print("[align] 完成：%d 首需要校正" % changed)


if __name__ == "__main__":
    main()
