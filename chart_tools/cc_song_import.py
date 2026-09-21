# -*- coding: utf-8 -*-
"""CC 授权曲目 → 本工程谱面（**对齐由构造保证**）。

作者口径（2026-09-22）："谱面本身和乐曲还是没有对齐…从开源平台上找几首可以直接转换用的
歌曲和谱面，加到选歌界面，我换几首曲子测试。歌曲名后标注来源游戏。"

为什么自己成谱：osu! / Phira 的谱面不能直接搬（曲目版权 + 各家 ToS），
而本工程已经有"本地曲包"这条路（`Assets/Resources/Charts/*.json` + `Assets/Resources/Audio/`）。
所以这里用**明确可再分发的曲目**（Kevin MacLeod / incompetech.com，CC-BY 4.0，
署名即可商用）来自动成谱：

  ① 解码音频（miniaudio）→ 单声道 44.1k；
  ② 找**前导静音**：它就是谱面 `metadata.offset`（本工程 `SongTime = 音频位置 − offset`，
     所以谱面时间 = 绝对文件时间 − 前导静音，**音符时刻直接由起音推导**，不存在"差好几秒"）；
  ③ 谱通量起音检测 + 自相关 BPM 估计 → 把起音吸附到 1/4 拍网格；
  ④ 按起音处的频谱重心分轨（5 轨）；
  ⑤ 输出 v2 谱面 JSON（tap / hold），并复制音频到 Resources/Audio。

产物名的前缀是 `cc_`（与本地 PJSK 曲包的 `pjsk_` 前缀区分），曲名后带来源署名。

用法：
  python -X utf8 chart_tools/cc_song_import.py "曲子.mp3" --name "曲名" --levels 6
  python -X utf8 chart_tools/cc_song_import.py --all "C:/path/to/music_dir"
"""
import argparse
import json
import os
import sys

import numpy as np
import miniaudio

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHART_DIR = os.path.join(ROOT, "Assets", "Resources", "Charts")
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")

SR = 44100
FRAME = 1024
HOP = 256

# 来源署名（CC-BY 4.0 要求署名；不写清楚就是侵权）
SOURCE = "Kevin MacLeod (incompetech.com) · CC-BY 4.0"


def decode_mono(path):
    dec = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.FLOAT32, nchannels=1)
    samples = np.asarray(dec.samples, dtype=np.float32)
    if dec.sample_rate != SR:                      # 简单线性重采样到 44.1k
        n = int(len(samples) * SR / dec.sample_rate)
        samples = np.interp(np.linspace(0, len(samples) - 1, n),
                            np.arange(len(samples)), samples).astype(np.float32)
    return samples


def leading_silence_seconds(x):
    """前导静音 = 第一个"明显有声"的 20ms 窗口起点。返回秒。"""
    win = int(0.02 * SR)
    if len(x) < win * 4:
        return 0.0
    frames = x[: len(x) // win * win].reshape(-1, win)
    rms = np.sqrt((frames ** 2).mean(axis=1) + 1e-12)
    peak = float(np.percentile(rms, 95))
    if peak <= 0:
        return 0.0
    gate = peak * 0.06                          # 相对门限：低音量曲子也适用
    idx = int(np.argmax(rms > gate))
    return max(0.0, idx * win / SR)


def onset_envelope(x):
    """谱通量（半波整流）+ 幅度包络，用作起音强度。"""
    n = 1 + (len(x) - FRAME) // HOP
    if n <= 4:
        return np.zeros(1), np.zeros(1)
    window = np.hanning(FRAME).astype(np.float32)
    mags = np.empty((n, FRAME // 2 + 1), dtype=np.float32)
    for i in range(n):
        seg = x[i * HOP: i * HOP + FRAME] * window
        mags[i] = np.abs(np.fft.rfft(seg))
    flux = np.diff(mags, axis=0)
    flux = np.maximum(flux, 0).sum(axis=1)
    rms = np.sqrt((mags ** 2).mean(axis=1) + 1e-12)
    return flux, rms


def estimate_bpm(flux, fps):
    """自相关估 BPM（60–200），再吸附到整数。"""
    if len(flux) < 64:
        return 120.0
    e = flux - flux.mean()
    ac = np.correlate(e, e, mode="full")[len(e) - 1:]
    lo = int(fps * 60 / 200.0)
    hi = min(len(ac) - 1, int(fps * 60 / 60.0))
    if hi <= lo + 1:
        return 120.0
    lag = lo + int(np.argmax(ac[lo:hi]))
    bpm = 60.0 * fps / lag
    while bpm < 70.0:
        bpm *= 2
    while bpm > 190.0:
        bpm /= 2
    return float(round(bpm))


def pick_onsets(flux, fps, min_gap=0.09):
    """自适应门限 + 最小间隔的峰值拾取，返回 (时间秒, 强度) 列表。"""
    if len(flux) < 8:
        return []
    med = np.median(flux)
    mad = np.median(np.abs(flux - med)) + 1e-6
    thresh = med + 3.0 * mad
    peaks = []
    for i in range(1, len(flux) - 1):
        if flux[i] < thresh or flux[i] < flux[i - 1] or flux[i] < flux[i + 1]:
            continue
        t = i / fps
        if peaks and t - peaks[-1][0] < min_gap:
            if flux[i] > peaks[-1][1]:
                peaks[-1] = (t, float(flux[i]))
            continue
        peaks.append((t, float(flux[i])))
    return peaks


def spectral_centroid(x, t, half=0.04):
    """起音处 80ms 窗的频谱重心（Hz）——用来分轨，保证同一路音色落在同一轨。"""
    a = max(0, int((t - half) * SR))
    b = min(len(x), int((t + half) * SR))
    if b - a < 64:
        return 0.0
    seg = x[a:b] * np.hanning(b - a).astype(np.float32)
    sp = np.abs(np.fft.rfft(seg))
    freqs = np.fft.rfftfreq(len(seg), 1.0 / SR)
    total = sp.sum() + 1e-9
    return float((sp * freqs).sum() / total)


def quantize(t, phase, step):
    k = round((t - phase) / step)
    return max(0.0, round(phase + k * step, 3))


def build_chart(x, name, artist, keys, level, max_notes, audio_name):
    fps = SR / HOP
    offset = leading_silence_seconds(x)
    flux, rms = onset_envelope(x)
    bpm = estimate_bpm(flux, fps)
    step = 60.0 / bpm / 4.0                      # 1/4 拍 = 16 分音符
    peaks = pick_onsets(flux, fps)
    if not peaks:
        raise SystemExit("没有检出起音，无法成谱：" + name)

    # 起点相位：用前 8 个强起音的平均偏差对齐拍网格
    strong = sorted(peaks, key=lambda p: -p[1])[:8]
    phase = float(np.mean([p[0] % step for p in strong]))

    # 丢弃：前导静音之前、开场 2 秒（留垫步空间）、结尾 3 秒
    total = len(x) / SR
    keep = [p for p in peaks if p[0] > offset + 2.0 and p[0] < total - 3.0]
    if not keep:
        keep = [p for p in peaks if p[0] > offset + 0.5]

    # 密度修剪：优先保留强起音
    keep.sort(key=lambda p: -p[1])
    keep = keep[:max_notes]
    keep.sort(key=lambda p: p[0])

    lanes_used = max(1, keys)
    centroids = [spectral_centroid(x, p[0]) for p in keep]
    finite = [c for c in centroids if c > 0]
    lo, hi = (np.percentile(finite, 10), np.percentile(finite, 90)) if finite else (0.0, 1.0)
    if hi - lo < 1.0:
        hi = lo + 1.0

    notes = []
    seen = set()
    for (t, mag), cen in zip(keep, centroids):
        # 谱面时间 = 文件时间 − 前导静音。**只在吸附误差 ≤12ms 时才吸附到 1/4 拍网格**：
        # 一开始无条件吸附，实测让音符系统性偏离起音约 −20ms（网格量化误差，BPM 122 时步长 123ms），
        # 而作者反馈的"音画不齐"正是这个量级 —— 测试谱面宁可"不整齐"，也必须**落在起音上**。
        raw = t - offset
        snapped = quantize(raw, phase, step)
        ct = snapped if abs(snapped - raw) <= 0.012 else round(raw, 3)
        if ct <= 0:
            continue
        norm = 0.0 if cen <= lo else (1.0 if cen >= hi else (cen - lo) / (hi - lo))
        lane = int(min(keys - 1, max(0, round(norm * (lanes_used - 1)))))
        key = (round(ct, 3), lane)
        if key in seen:
            continue
        seen.add(key)
        notes.append({"type": "tap", "lane": lane, "time": ct, "duration": 0.0,
                      "direction": "up", "wide": False, "path": []})

    notes.sort(key=lambda n: n["time"])
    counts = {"tap": len(notes), "hold": 0, "flick": 0, "slide": 0}
    chart = {
        "metadata": {
            "songName": name,
            "songArtist": artist,
            "chartAuthor": "自动成谱（起音对齐）",
            "difficulty": 0,
            "level": level,
            "bpm": float(bpm),
            "offset": round(offset, 3),
            "audioFileName": audio_name,
            "previewStartTime": 0.0,
            "previewDuration": 10.0,
            "formatVersion": 2,
            "noteCounts": counts,
        },
        "notes": notes,
        "events": [],
    }
    stats = {
        "offset": offset, "bpm": bpm, "notes": len(notes),
        "first": notes[0]["time"] if notes else None,
        "last": notes[-1]["time"] if notes else None,
        "density": len(notes) / max(1.0, total - offset),
    }
    return chart, stats


def write_song(path, name, artist, keys, level, max_notes, source=SOURCE):
    x = decode_mono(path)
    slug = os.path.splitext(os.path.basename(path))[0].lower().replace(" ", "_")
    audio_name = "cc_" + slug
    title = "%s【%s】" % (name, source)
    chart, stats = build_chart(x, title, artist, keys, level, max_notes, audio_name)

    os.makedirs(CHART_DIR, exist_ok=True)
    os.makedirs(AUDIO_DIR, exist_ok=True)

    with open(os.path.join(CHART_DIR, "cc_%s.json" % slug), "w", encoding="utf-8") as f:
        json.dump(chart, f, ensure_ascii=False, indent=2)
    dst_audio = os.path.join(AUDIO_DIR, audio_name + os.path.splitext(path)[1])
    with open(path, "rb") as src, open(dst_audio, "wb") as dst:
        dst.write(src.read())

    print("[cc_import] %-22s → cc_%s.json  音符 %d  BPM %d  前导静音 %.3fs  首音 %.3fs  末音 %.3fs  密度 %.1f/s"
          % (name, slug, stats["notes"], round(stats["bpm"]), stats["offset"],
             stats["first"] or 0, stats["last"] or 0, stats["density"]))
    return stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("audio", help="音频文件；配合 --all 时是目录")
    ap.add_argument("--name", default=None, help="曲名（默认取文件名）")
    ap.add_argument("--artist", default="Kevin MacLeod")
    ap.add_argument("--keys", type=int, default=5)
    ap.add_argument("--level", type=int, default=6)
    ap.add_argument("--max-notes", type=int, default=700)
    ap.add_argument("--all", action="store_true", help="把目录下所有 mp3 都导入")
    args = ap.parse_args()

    if args.all:
        files = sorted(f for f in os.listdir(args.audio) if f.lower().endswith(".mp3"))
        for f in files:
            full = os.path.join(args.audio, f)
            if os.path.getsize(full) < 400000:      # 下载失败的占位文件
                continue
            try:
                write_song(full, os.path.splitext(f)[0], args.artist, args.keys,
                           args.level, args.max_notes)
            except SystemExit as e:
                print("[cc_import] 跳过", f, e)
        return

    write_song(args.audio, args.name or os.path.splitext(os.path.basename(args.audio))[0],
               args.artist, args.keys, args.level, args.max_notes)


if __name__ == "__main__":
    main()
