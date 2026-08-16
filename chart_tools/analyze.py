# -*- coding: utf-8 -*-
"""
FallenAngel 谱面制作工具·第一步：音频分析
用法: python analyze.py <音频路径> [输出json路径]

流程: 解码 mp3 → 谱通量起音检测 → 自相关 BPM 估计 → 拍点网格对齐
     → 每拍强度/能量 → 段落结构粗分（低/中/高能量段）
输出: JSON 报告（BPM、拍点表、段落表），供下一步谱面生成使用
"""
import sys
import json
import numpy as np
import miniaudio


def decode_mono(path):
    """解码为单声道 float32 PCM"""
    dec = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.FLOAT32, nchannels=1)
    samples = np.asarray(dec.samples, dtype=np.float32)
    return samples, dec.sample_rate


def stft_mag(samples, frame=2048, hop=512):
    """短时傅里叶幅度谱"""
    n_frames = 1 + (len(samples) - frame) // hop
    frames = np.lib.stride_tricks.as_strided(
        samples,
        shape=(n_frames, frame),
        strides=(hop * 4, 4),
        writeable=False,
    )
    win = np.hanning(frame).astype(np.float32)
    mags = np.empty((n_frames, frame // 2 + 1), dtype=np.float32)
    for i in range(0, n_frames, 64):  # 分批计算，控制内存
        block = frames[i:i + 64] * win
        mags[i:i + 64] = np.abs(np.fft.rfft(block, axis=1))
    return mags


def onset_flux(mags):
    """谱通量起音强度包络（半波整流）"""
    logmag = np.log1p(mags)
    flux = np.maximum(0.0, logmag[1:] - logmag[:-1]).sum(axis=1)
    med = np.median(flux[flux > 0]) if np.any(flux > 0) else 1e-6
    return flux / med  # 中值归一化


def estimate_bpm(env, sr, hop, low=60.0, high=200.0):
    """自相关法估 BPM：对包络在 60-200 BPM 区间扫相位与周期，取得分最高者"""
    n = len(env)
    best = (0.0, -1e9, 0.0)  # (bpm, score, phase)
    for bpm in np.arange(low, high + 0.25, 0.25):
        fpb = (sr / hop) / (bpm / 60.0)  # 每拍帧数
        if fpb < 2:
            continue
        # 相位候选：1/32 拍步进
        for phase in np.arange(0, fpb, fpb / 32):
            idx = np.arange(phase, n, fpb).astype(int)
            idx = idx[idx < n - 1]
            if len(idx) < 4:
                continue
            score = env[idx].mean()
            if score > best[1]:
                best = (float(bpm), float(score), float(phase))
    return best


def beat_grid(env, sr, hop, bpm, phase):
    """生成拍点网格：时间 + 强度（拍点前后 ±40ms 窗口内的包络均值）"""
    fpb = (sr / hop) / (bpm / 60.0)
    half = max(1, int(0.040 * sr / hop))  # ±40ms
    n = len(env)
    beats = []
    t = phase / sr * hop
    idx = phase
    while idx < n - 1:
        lo = max(0, int(idx - half))
        hi = min(n - 1, int(idx + half))
        strength = float(env[lo:hi + 1].max())
        beats.append({"t": round(t, 3), "s": round(strength, 2)})
        idx += fpb
        t += 60.0 / bpm
    return beats


def energy_per_beat(samples, sr, beats):
    """每拍 RMS 能量（拍点 ±半拍窗口）"""
    half = (60.0 / (beats[-1]["t"] - beats[0]["t"] + 1e-6)) * 0.5 if len(beats) > 1 else 0.25
    out = []
    for b in beats:
        lo = max(0, int((b["t"] - half) * sr))
        hi = min(len(samples), int((b["t"] + half) * sr))
        rms = float(np.sqrt(np.mean(samples[lo:hi] ** 2))) if hi > lo else 0.0
        out.append(round(rms, 4))
    return out


def section_labels(energies, beats, bpm):
    """按平滑能量分低/中/高三档，聚成连续段落"""
    e = np.array(energies)
    k = max(8, len(e) // 16)
    kern = np.ones(k) / k
    sm = np.convolve(e, kern, mode="same")
    lo, hi = np.percentile(sm, 30), np.percentile(sm, 70)
    labels = np.where(sm < lo, "low", np.where(sm > hi, "high", "mid"))
    sections = []
    start = 0
    for i in range(1, len(labels)):
        if labels[i] != labels[start]:
            sections.append({
                "start": beats[start]["t"],
                "end": beats[i]["t"],
                "label": str(labels[start]),
                "beats": i - start,
            })
            start = i
    sections.append({
        "start": beats[start]["t"],
        "end": beats[-1]["t"] + 60.0 / bpm,  # 末段拍到最后一拍+1拍
        "label": str(labels[start]),
        "beats": len(labels) - start,
    })
    # 合并相邻同标签
    merged = []
    for s in sections:
        if merged and merged[-1]["label"] == s["label"]:
            merged[-1]["end"] = s["end"]
            merged[-1]["beats"] += s["beats"]
        else:
            merged.append(s)
    return merged


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "demo_song.mp3"
    out_path = sys.argv[2] if len(sys.argv) > 2 else path.rsplit(".", 1)[0] + "_analysis.json"

    print(f"[1/4] 解码: {path}")
    samples, sr = decode_mono(path)
    print(f"      时长 {len(samples)/sr:.1f}s, 采样率 {sr}Hz, 样本 {len(samples)}")

    print("[2/4] STFT + 起音包络")
    hop = 512
    mags = stft_mag(samples)
    env = onset_flux(mags)
    print(f"      包络 {len(env)} 帧")

    print("[3/4] BPM 估计 + 拍点对齐")
    bpm, score, phase = estimate_bpm(env, sr, hop)
    print(f"      BPM ≈ {bpm:.2f}（置信得分 {score:.2f}）")
    beats = beat_grid(env, sr, hop, bpm, phase)
    print(f"      拍点数 {len(beats)}")

    print("[4/4] 每拍能量 + 段落结构")
    energies = energy_per_beat(samples, sr, beats)
    sections = section_labels(energies, beats, bpm)

    # 每拍附能量与八拍小节号
    for i, b in enumerate(beats):
        b["e"] = energies[i]
        b["bar"] = i // 4

    report = {
        "bpm": round(bpm, 2),
        "duration": round(len(samples) / sr, 2),
        "sampleRate": sr,
        "beatCount": len(beats),
        "beats": beats,
        "sections": sections,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print(f"\n报告已写入: {out_path}")
    print("段落结构:")
    for s in sections:
        print(f"  [{s['start']:6.1f}s - {s['end']:6.1f}s] {s['label']:4s} {s['beats']:3d} 拍")


if __name__ == "__main__":
    main()
