# -*- coding: utf-8 -*-
"""
贝斯增强音频 → 归一化 IR 前端（instrument=bass）。

用法: python audio_to_ir.py <音频.mp3/wav> [--out ir.json] [--offset 秒]

流程（纯 numpy，依赖 soundfile 解码 mp3）：
1. 解码 → mono float32（soundfile 自带 libsndfile，mp3/wav 均可）
2. 首响检测：开头静音段 → 报告 offset（与 demo_song 4.25s 静音对齐逻辑一致）
3. Onset 检测：STFT 对数谱通量峰 → 音符起点候选
4. 段化：onset 分割，<60ms 间隙合并，<40ms 段剔除（瞬态噪声）
5. 音高估计：每帧谐波总和打分（40-200Hz 贝斯音域，8 谐波）→ 段内中位数
   → MIDI 半音量化；段内音高不稳定（std 大）→ 按多数帧投票
6. 过滤：段 RMS 低于阈值剔除；>300Hz 主导段剔除（非贝斯频段）
7. 输出 IR：instrument=bass，pitch=MIDI，duration=段长，velocity=能量（归一化）

对齐说明：IR 时间 = 帧时刻 + offset。若音频自带前导静音（如 4.25s），
--offset 补 0 即可（本工具自动检测首响报告）；若首响检测失败再手动 --offset。
"""
import numpy as np
import sys
from fractions import Fraction

from ir import IRStream, NoteEvent, IR_PPQ, dump_json

F0_MIN, F0_MAX = 40.0, 200.0     # 贝斯基频范围（E1≈41Hz ~ G3≈196Hz）
HARMONICS = 8                    # 谐波总和打分谐波数
HOP_ONSET = 256                  # onset 谱通量帧步（5.8ms 精度）
HOP_PITCH = 512                  # 音高帧步（11.6ms）
WIN_N = 4096                     # FFT 窗（93ms，含 E1 的 4 个周期）
MIN_SEG_MS = 40.0                # 最短音符段（瞬态噪声剔除）
MERGE_GAP_MS = 60.0              # 段间间隙 < 此值合并
RMS_MIN = 0.01                   # 段能量下限（静音剔除）
SCORE_MIN = 0.008                # 段内谐波打分中位数下限（无稳定低频谐波序列 → 非贝斯段）
MAX_PITCH_STD = 3.0              # 段内基频半音 std 超此值 → 段音高不可靠，按投票


def load_mono(path):
    import soundfile as sf
    data, sr = sf.read(path)
    if data.ndim > 1:
        data = data.mean(axis=1)
    return data.astype(np.float32), int(sr)


def first_sound(data, sr, thresh=0.005):
    """首响时刻（s）：第一个 RMS>thresh 的窗口（10ms 窗口滑扫）"""
    w = int(sr * 0.01)
    for i in range(0, len(data) - w, w):
        if np.sqrt(np.mean(data[i:i + w] ** 2)) > thresh:
            return i / sr
    return 0.0


def detect_bpm(data, sr):
    """主导 BPM 检测：能量包络自相关（60-200BPM 滞后窗），取峰中最低频的强峰"""
    hop = int(sr * 0.02)
    rms = np.sqrt(np.mean(data[: (len(data) // hop) * hop].reshape(-1, hop) ** 2, axis=1))
    rms = rms - rms.mean()
    # 滞后窗：0.3s~1.0s（60~200BPM）
    lo, hi = int(0.30 * sr / hop), int(1.00 * sr / hop)
    acs = np.array([np.dot(rms[:-l], rms[l:]) / (len(rms) - l) for l in range(lo, hi + 1)])
    # 局部峰（3 点窗口），从低频（BPM 慢 = 长滞后）端取第一个显著峰
    peaks = []
    for i in range(1, len(acs) - 1):
        if acs[i] > acs[i - 1] and acs[i] > acs[i + 1] and acs[i] > 0.05 * acs.max():
            peaks.append(i)
    if not peaks:
        return 120.0
    lag = lo + peaks[0]
    return 60.0 * sr / hop / lag


def spectral_flux(data, sr):
    """对数谱通量（onset 强度序列）"""
    hop = HOP_ONSET
    win = np.hanning(WIN_N)
    prev = None
    flux = []
    frames = []
    n_frames = (len(data) - WIN_N) // hop
    for i in range(n_frames):
        seg = data[i * hop:i * hop + WIN_N] * win
        mag = np.abs(np.fft.rfft(seg)) + 1e-9
        log_mag = np.log(mag)
        if prev is not None:
            flux.append(float(np.sum(np.maximum(log_mag - prev, 0))))
        prev = log_mag
        frames.append(i * hop)
    return np.array(flux), np.array(frames[:len(flux)])


def pick_onsets(flux, sr, rel_thresh=0.6, min_gap_ms=50.0):
    """谱通量局部峰 → onset 时刻列表（自适应阈值 = 全局 p75 × 0.6）"""
    thr = np.percentile(flux, 75) * rel_thresh
    onsets = []
    gap = int(min_gap_ms / 1000 * sr / HOP_ONSET)
    i = 0
    while i < len(flux):
        if flux[i] > thr:
            j = i
            while j + 1 < len(flux) and flux[j + 1] > thr:
                j += 1
            peak = i + int(np.argmax(flux[i:j + 1]))
            if not onsets or peak - onsets[-1] >= gap:
                onsets.append(peak)
            i = j + 1
        else:
            i += 1
    return np.array(onsets)


def seg_from_onsets(onsets, n_frames, hop, sr):
    """onset 边界 → 音符段 (起帧, 止帧)。段间无缝衔接（ends = 下一 onset），
    "距上段起点总长 < 60ms" 的两个快速瞬态合并为一段（合并后起点不变，不连锁）；
    再剔除短段（<40ms 瞬态噪声）。单位统一为 onset 帧（hop）"""
    starts = list(onsets)
    ends = list(onsets[1:]) + [n_frames]
    segs = []
    gap_f = int(MERGE_GAP_MS / 1000 * sr / hop)
    for s, e in zip(starts, ends):
        if segs and s - segs[-1][0] < gap_f:
            segs[-1][1] = e  # 合并到前段（起点保留）
        else:
            segs.append([s, e])
    min_f = max(int(MIN_SEG_MS / 1000 * sr / hop), 1)
    return [[s, e] for s, e in segs if e - s >= min_f]


def frame_pitch(mag, f):
    """单帧谐波总和打分 → (f0, score)。贝斯音域候选 1Hz 步长"""
    cands = np.arange(F0_MIN, F0_MAX + 1, 1.0)
    score = np.zeros_like(cands)
    for h in range(1, HARMONICS + 1):
        idx = np.clip(np.round(cands * h / f[1]).astype(int), 0, len(f) - 1)
        score += mag[idx]
    k = int(np.argmax(score))
    return cands[k], score[k]


def hz_to_midi(hz):
    return round(12 * np.log2(hz / 440.0) + 69)


def main():
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return
    src = args[0]
    out = None
    offset = None
    i = 1
    while i < len(args):
        if args[i] == "--out" and i + 1 < len(args):
            out = args[i + 1]
            i += 2
        elif args[i] == "--offset" and i + 1 < len(args):
            offset = float(args[i + 1])
            i += 2
        else:
            i += 1
    if out is None:
        out = src.rsplit(".", 1)[0] + ".ir.json"

    data, sr = load_mono(src)
    dur = len(data) / sr
    print(f"解码: {src} → {sr}Hz mono，{dur:.1f}s")

    detected = first_sound(data, sr)
    if offset is None:
        offset = detected
    bpm = detect_bpm(data, sr)
    print(f"首响 {detected:.3f}s（offset={offset:.3f}s）；主导 BPM {bpm:.0f}")

    flux, frames = spectral_flux(data, sr)
    onsets = pick_onsets(flux, sr)
    n_onset = (len(data) - WIN_N) // HOP_ONSET  # onset 帧单位（与 onset 索引一致）
    segs = seg_from_onsets(onsets, n_onset, HOP_ONSET, sr)
    print(f"onset {len(onsets)} 个 → 段 {len(segs)} 个")

    # 预计算全部音高帧（段内共用）
    n_pitch = (len(data) - WIN_N) // HOP_PITCH
    win = np.hanning(WIN_N)
    f = np.fft.rfftfreq(WIN_N, 1 / sr)
    pitch_frames = np.zeros((n_pitch, 2))  # (f0, score)
    for i in range(n_pitch):
        seg = data[i * HOP_PITCH:i * HOP_PITCH + WIN_N] * win
        mag = np.abs(np.fft.rfft(seg)) / WIN_N
        pitch_frames[i] = frame_pitch(mag, f)

    events = []
    dropped = {"short": 0, "quiet": 0, "hi": 0, "unstable": 0}
    for s, e in segs:
        t0 = s * HOP_ONSET / sr
        t1 = min(e * HOP_ONSET + WIN_N, len(data)) / sr
        seg_data = data[int(t0 * sr):int(t1 * sr)]
        rms = float(np.sqrt(np.mean(seg_data ** 2))) if len(seg_data) else 0.0
        if rms < RMS_MIN:
            dropped["quiet"] += 1
            continue
        # 段内音高帧（onset 帧 → 音高帧索引换算）：取 f0 中位数，std 大则按 1Hz 投票
        k0 = s * HOP_ONSET // HOP_PITCH
        k1 = min((e * HOP_ONSET + WIN_N) // HOP_PITCH, n_pitch)
        f0s = [pitch_frames[k][0] for k in range(k0, k1)]
        if not f0s:
            dropped["short"] += 1
            continue
        f0s = np.array(f0s)
        # 谐波匹配分数下限：无稳定低频谐波序列的段（鼓瞬态/非贝斯）剔除
        scs = np.array([pitch_frames[k][1] for k in range(k0, k1)])
        if float(np.median(scs)) < SCORE_MIN:
            dropped["hi"] += 1
            continue
        midi_all = np.round(12 * np.log2(f0s / 440.0) + 69)
        std = float(np.std(midi_all))
        if std > MAX_PITCH_STD:
            vals, cnts = np.unique(midi_all.astype(int), return_counts=True)
            f0 = 440.0 * 2 ** ((vals[np.argmax(cnts)] - 69) / 12)
        else:
            f0 = float(np.median(f0s))
        pitch = hz_to_midi(f0)
        if not (24 <= pitch <= 60):  # 贝斯实际音域防御（C1~C4）
            dropped["hi"] += 1
            continue
        dur_s = max(t1 - t0, 0.05)
        events.append(NoteEvent(
            tick=round(t0 * IR_PPQ / 60 * bpm),  # 占位（rule_engine 只按 time 用 tick 排序）
            time=round(t0 + offset, 4),
            pitch=pitch,
            duration=round(dur_s, 4),
            duration_ticks=round(dur_s * IR_PPQ * bpm / 60),  # 检测 BPM 换算拍数（≥2 拍长音 → hold）
            velocity=round(min(rms / 0.3, 1.0), 2),
            instrument="bass",
            articulations=[],
            pitch_end=None,
            tied=False,
        ))
    print(f"音符 {len(events)} 个；剔除: {dropped}")

    stream = IRStream(
        events=events,
        metadata={"songName": src.rsplit("\\", 1)[-1].rsplit("/", 1)[-1].rsplit(".", 1)[0],
                  "chartAuthor": "audio frontend (bass)",
                  "offset": offset},
        # BPM 事件在 time=0（时间轴从 0 开始积分；tick 已含 offset 平移），避免 SyncTrack 重复事件
        events_channel=[{"time": 0.0, "type": "bpm", "value": round(bpm, 3)}],
    )
    dump_json(stream, out)
    dur_n = max((e.time for e in events), default=0)
    print(f"转换完成: {out}（事件 {len(events)}，末音符 {dur_n:.1f}s / 音频 {dur:.1f}s）")


if __name__ == "__main__":
    main()
