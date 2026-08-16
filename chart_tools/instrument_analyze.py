# -*- coding: utf-8 -*-
"""
FallenAngel 谱面制作工具·第二步：乐器分离 v2
用法: python instrument_analyze.py <音频路径> <analysis.json> [输出json路径]

v2 方法（160 BPM 16 分网格 + 频谱形状分类）:
  - HPSS 分离打击/谐波分量
  - 每帧对数域频带能量，减去 2 秒滚动基线（= 爆发比）
  - 每个 16 分格点分类:
      kick   = 低频爆发 且 中频安静（正拍软加权）
      snare  = 中频爆发 且 频谱平坦度高（噪声特征，反拍软加权）
      hihat  = 高频爆发 且 中频安静
      bass   = 谐波低频 起音爆发 或 起音后持续（贝斯音持续）
      mid_melody = 谐波中频爆发（疑似吉他）
      high_lead  = 谐波高频爆发（疑似合成器）
  - 持续段检测: 谐波中/高频能量持续高于基线 -> 长按候选
输出 JSON: instruments{...} + longnotes{mid/high}
"""
import sys
import json
import numpy as np
sys.path.insert(0, '.')
import analyze

FRAME, HOP = 2048, 512
GRID_BPM = 160.0
DIV = 16  # 16 分音符网格


def time_median(m, w=17):
    """沿时间轴中值滤波（谐波增强）"""
    n, b = m.shape
    out = np.empty_like(m)
    pad = w // 2
    for c0 in range(0, b, 64):
        c1 = min(b, c0 + 64)
        block = m[:, c0:c1]
        win = np.lib.stride_tricks.sliding_window_view(block, w, axis=0)
        med = np.median(win, axis=-1)
        res = np.empty_like(block)
        res[:pad] = med[:1]
        res[pad:pad + len(med)] = med
        res[pad + len(med):] = med[-1:]
        out[:, c0:c1] = res
    return out


def freq_median(m, w=17):
    """沿频率轴中值滤波（打击增强）"""
    n, b = m.shape
    out = np.empty_like(m)
    pad = w // 2
    for r0 in range(0, n, 512):
        r1 = min(n, r0 + 512)
        block = m[r0:r1]
        win = np.lib.stride_tricks.sliding_window_view(block, w, axis=1)
        med = np.median(win, axis=-1)
        res = np.empty_like(block)
        res[:, :pad] = med[:, :1]
        res[:, pad:pad + med.shape[1]] = med
        res[:, pad + med.shape[1]:] = med[:, -1:]
        out[r0:r1] = res
    return out


def band_sum(m, freqs, lo, hi):
    """频带对数能量（每帧）"""
    mask = (freqs >= lo) & (freqs <= hi)
    if not mask.any():
        return np.zeros(m.shape[0])
    return np.log1p(m[:, mask].sum(axis=1))


def band_flatness(m, freqs, lo, hi):
    """频带频谱平坦度（噪声~1，乐音~0）"""
    mask = (freqs >= lo) & (freqs <= hi)
    if not mask.any():
        return np.zeros(m.shape[0])
    p = m[:, mask]
    eps = 1e-8
    gm = np.exp(np.mean(np.log(p + eps), axis=1))
    am = np.mean(p, axis=1)
    return gm / (am + eps)


def rolling_percentile(x, w, q=10):
    """滚动 q 分位（背景水平估计），边缘复制填充。
    相比中值：密集段落中值被事件本身抬高，分位取窗口最安静的背景"""
    n = len(x)
    out = np.empty(n)
    pad = w // 2
    if n < w:
        out[:] = np.percentile(x, q)
        return out
    win = np.lib.stride_tricks.sliding_window_view(x, w)
    k = max(0, min(w - 1, q * w // 100))
    part = np.partition(win, k, axis=1)[:, k]
    out[pad:pad + len(part)] = part
    out[:pad] = part[0]
    out[pad + len(part):] = part[-1]
    return out


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "../Assets/Resources/Audio/demo_song.mp3"
    ana_path = sys.argv[2] if len(sys.argv) > 2 else "demo_song_analysis.json"
    out_path = sys.argv[3] if len(sys.argv) > 3 else "demo_song_instruments.json"

    print("[1/5] decode + STFT + HPSS")
    samples, sr = analyze.decode_mono(path)
    mags = analyze.stft_mag(samples)
    freqs = np.fft.rfftfreq(FRAME, 1.0 / sr)
    P = freq_median(mags)   # 打击分量
    H = time_median(mags)   # 谐波分量

    print("[2/5] per-frame features")
    P_low = band_sum(P, freqs, 30, 120)
    P_mid = band_sum(P, freqs, 150, 2000)
    P_high = band_sum(P, freqs, 6000, 15000)
    P_mid_flat = band_flatness(P, freqs, 150, 2000)
    H_low = band_sum(H, freqs, 40, 180)
    H_mid = band_sum(H, freqs, 250, 2500)
    H_high = band_sum(H, freqs, 2500, 8000)

    base_w = int(1.5 * sr / HOP)  # 1.5 秒滚动背景（10 分位）
    dP_low = P_low - rolling_percentile(P_low, base_w)
    dP_mid = P_mid - rolling_percentile(P_mid, base_w)
    dP_high = P_high - rolling_percentile(P_high, base_w)
    dH_low = H_low - rolling_percentile(H_low, base_w)
    dH_mid = H_mid - rolling_percentile(H_mid, base_w)
    dH_high = H_high - rolling_percentile(H_high, base_w)

    print("[3/5] grid classification")
    with open(ana_path, encoding="utf-8") as f:
        ana = json.load(f)
    phase = ana["beats"][0]["t"]
    grid = 60.0 / GRID_BPM / 4.0  # 16 分格 = 93.75ms
    n = len(dP_low)

    # 全局能量下限（20 分位）：低于此电平的帧不做打击分类，滤除静音段伪峰
    floor_low = float(np.percentile(P_low, 20))
    floor_mid = float(np.percentile(P_mid, 20))
    floor_high = float(np.percentile(P_high, 20))

    def burst(x, f):
        """起音窗口爆发比：max over [f-2, f+4]"""
        lo = max(0, f - 2)
        hi = min(n, f + 5)
        return float(np.max(x[lo:hi]))

    def sustain(x, f, a=4, b=20):
        """起音后持续：mean over [f+a, f+b]"""
        lo = min(n - 1, f + a)
        hi = min(n, f + b)
        if hi <= lo:
            return 0.0
        return float(np.mean(x[lo:hi]))

    def fast_decay(x, f):
        """起音后快速衰减度：近窗能量 - 远窗能量（正=衰减快=底鼓样）"""
        near = np.mean(x[f + 1:f + 5])
        far = np.mean(x[f + 10:f + 20])
        return float(near - far)

    instruments = {k: [] for k in
                   ["kick", "snare", "hihat", "bass", "mid_melody", "high_lead"]}

    t = phase
    k = 0
    while t < len(samples) / sr - 0.05:
        f = int(t * sr / HOP)
        k16 = k % DIV
        quarter = (k16 % 4 == 0)
        backbeat = (k16 == 4 or k16 == 12)

        b_low, b_mid, b_high = burst(dP_low, f), burst(dP_mid, f), burst(dP_high, f)
        flat = float(np.max(P_mid_flat[max(0, f - 2):min(n, f + 5)]))
        h_low_b, h_mid_b, h_high_b = burst(dH_low, f), burst(dH_mid, f), burst(dH_high, f)
        s_low = sustain(dH_low, f)
        decay_low = fast_decay(dP_low, f)

        # 能量下限门控
        raw_low_ok = P_low[f] > floor_low
        raw_mid_ok = P_mid[f] > floor_mid
        raw_high_ok = P_high[f] > floor_high

        # 软先验：底鼓正拍、军鼓反拍
        kick_score = b_low - 0.3 * b_mid + 0.6 * decay_low + (0.15 if quarter else 0.0)
        snare_score = b_mid * (1.0 + 0.8 * flat) + (0.15 if backbeat else 0.0)
        hat_score = b_high - 0.8 * b_mid

        if raw_low_ok and kick_score > 0.85:
            instruments["kick"].append([round(t, 3), round(kick_score, 2)])
        if raw_mid_ok and snare_score > 1.1:
            instruments["snare"].append([round(t, 3), round(snare_score, 2)])
        if raw_high_ok and hat_score > 0.9:
            instruments["hihat"].append([round(t, 3), round(hat_score, 2)])
        if h_low_b > 0.7 or s_low > 0.3:
            instruments["bass"].append([round(t, 3), round(max(h_low_b, s_low), 2)])
        if h_mid_b > 0.7:
            instruments["mid_melody"].append([round(t, 3), round(h_mid_b, 2)])
        if h_high_b > 0.7:
            instruments["high_lead"].append([round(t, 3), round(h_high_b, 2)])

        k += 1
        t = phase + k * grid

    print("[4/5] sustained segments (long note candidates)")
    # 平坦度平滑：高=噪声(失真节奏吉他铺底)，低=乐音(人声/主音吉他持续音)
    # 注意：失真音色是持续噪声，位于谐波分量 H，必须用 H 的平坦度
    flat_mid_sm = np.convolve(
        band_flatness(H, freqs, 250, 2500), np.ones(11) / 11, mode="same")
    flat_high_sm = np.convolve(
        band_flatness(H, freqs, 2500, 8000), np.ones(11) / 11, mode="same")

    longnotes = {}
    for name, sig, flat_sig in [("mid", dH_mid, flat_mid_sm),
                                 ("high", dH_high, flat_high_sm)]:
        # 平滑后找持续高于阈值的段
        k_sm = 5
        sm = np.convolve(sig, np.ones(k_sm) / k_sm, mode="same")
        active = sm > 0.35
        segs = []
        i = 0
        while i < len(active):
            if active[i]:
                j = i
                while j + 1 < len(active) and active[j + 1]:
                    j += 1
                t0 = i * HOP / sr
                t1 = j * HOP / sr
                if t1 - t0 >= 0.25:
                    # 噪声特征（失真吉他铺底）vs 乐音特征（人声/主音持续）
                    kind = "noise" if float(np.mean(flat_sig[i:j + 1])) > 0.5 else "tonal"
                    if segs and t0 - segs[-1][1] < 0.15 and segs[-1][2] == kind:
                        segs[-1][1] = t1
                    else:
                        segs.append([round(t0, 2), round(t1, 2), kind])
                i = j + 1
            else:
                i += 1
        longnotes[name] = segs
        n_noise = sum(1 for s in segs if s[2] == "noise")
        print(f"      {name:6s} segments {len(segs)} (noise={n_noise}, tonal={len(segs)-n_noise})")

    print("[5/5] write json")
    report = {
        "sampleRate": sr,
        "grid": {"bpm": GRID_BPM, "division": DIV, "phase": phase},
        "instruments": instruments,
        "longnotes": longnotes,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print(f"      -> {out_path}")
    for name, ts in instruments.items():
        print(f"      {name:12s} {len(ts):4d}  rate {len(ts)/max(len(samples)/sr,1e-6)*60:5.1f}/min")


if __name__ == "__main__":
    main()
