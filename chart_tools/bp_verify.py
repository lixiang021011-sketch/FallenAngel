# -*- coding: utf-8 -*-
"""
FallenAngel · Basic Pitch 转录验收（第 1 轮）

只回答一个问题：
    Basic Pitch 输出的 onset / pitch / duration / pitch-bend
    是否足以作为 FallenAngel Bass Chart Generator 的输入？

本脚本不生成谱面、不做量化、不接 Runtime、不改 Mapperatorinator / Adapter / Editor。

用法（在 FallenAngel 工程根目录）：
    D:\\fa_tools\\bp_venv\\Scripts\\python.exe chart_tools\\bp_verify.py --all
    D:\\fa_tools\\bp_venv\\Scripts\\python.exe chart_tools\\bp_verify.py --song light

输入：
    D:\\fa_tools\\basicpitch_out\\<song>\\<song>_bass_basic_pitch.{csv,npz}
    D:\\fa_tools\\mapperatorinator\\in\\<song>_bass.wav
    D:\\fa_tools\\mapperatorinator\\out\\<tag>\\*.osu          （同段对照用）

输出：
    D:\\fa_tools\\basicpitch_verify\\<song>\\events.json      统一 note event（保留原始附加信息）
                                                   \\stats.json       四项体检的量化结果
                                                   \\piano_roll_*.png 时间×音高图
                                                   \\ascii_roll.txt   纯文本音高轮廓（可 grep）
                                                   \\listen_list.txt  需要人耳复核的时间点 + 切片 wav
                                                   \\*.wav            原素材 / 叠加试听
"""

import argparse
import csv
import json
import os
import pathlib
import sys
import wave

import numpy as np
import soundfile as sf

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

import librosa  # noqa: E402
import scipy.signal as ss  # noqa: E402

# ---------------------------------------------------------------- 常量 / 素材表

FFT_HOP = 256
SR = 22050
FPS = SR / FFT_HOP  # 86.1328 帧/秒
BINS_PER_SEMITONE = 3  # contour 每个半音 3 个 bin
NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

BP_OUT = pathlib.Path(r"D:\fa_tools\basicpitch_out")
MAP_IN = pathlib.Path(r"D:\fa_tools\mapperatorinator\in")
MAP_OUT = pathlib.Path(r"D:\fa_tools\mapperatorinator\out")
VERIFY_OUT = pathlib.Path(r"D:\fa_tools\basicpitch_verify")

# 素材分类按「Bass 写法」而不是按歌名，来自前期人工听感记录
SONGS = {
    "chiharu": dict(
        title="赤春花 (ChiliChill)",
        role="A 简单根音型（候选）",
        stem=MAP_IN / "chiharu_bass.wav",
        bp=BP_OUT / "chiharu" / "chiharu_bass_basic_pitch",
        osu=MAP_OUT / "chiharu_bass" / "beatmap058ec78a6dc845ac824619cc4ff461f8.osu",
        focus=[(20.0, 30.0), (95.0, 105.0)],
    ),
    "feiniao": dict(
        title="飞鸟说 (ChiliChill)",
        role="B 有音高移动 / 旋律性",
        stem=MAP_IN / "feiniao_bass.wav",
        bp=BP_OUT / "feiniao" / "feiniao_bass_basic_pitch",
        osu=MAP_OUT / "feiniao_bass" / "beatmap1e30d3f93da640b09faa8750f85c2682.osu",
        focus=[(20.0, 30.0), (60.0, 70.0)],
    ),
    "tada": dict(
        title="ただ君に晴れ (Yorushika)",
        role="补充样本（长音偏多）",
        stem=MAP_IN / "tada_bass.wav",
        bp=BP_OUT / "tada" / "tada_bass_basic_pitch",
        osu=MAP_OUT / "tadaF2" / "beatmapaef66f913c0940aba3127c2852deb71e.osu",
        focus=[(20.0, 30.0), (60.0, 70.0)],
    ),
    "light": dict(
        title="light (Jizue)",
        role="C Mapperatorinator 最差的一条（30–60s 清楚的铺根音电贝斯）",
        stem=MAP_IN / "light_bass.wav",
        bp=BP_OUT / "light" / "light_bass_basic_pitch",
        osu=MAP_OUT / "light_bass" / "beatmapbb54662bbd6640a3b51119ab0a12c18e.osu",
        focus=[(20.0, 30.0), (30.0, 60.0)],
    ),
    "suanse": dict(
        title="酸橙色信笺 (塞壬唱片-MSR / Dazbee)",
        role="新歌（分轨由 Demucs htdemucs 现做）",
        stem=MAP_IN / "suanse_bass.wav",
        bp=BP_OUT / "suanse" / "suanse_bass_basic_pitch",
        osu=MAP_OUT / "suanse_bass" / "beatmap077237f580e4418da40b6c62c09e0f5e.osu",
        focus=[(20.0, 30.0), (60.0, 70.0)],
    ),
}


# ---------------------------------------------------------------- 基础工具


def pitch_name(midi: int) -> str:
    return f"{NOTE_NAMES[int(midi) % 12]}{int(midi) // 12 - 1}"


def frame_to_time(idx: float) -> float:
    return idx / FPS


def time_to_frame(t: float) -> int:
    return int(round(t * FPS))


def load_bp_notes(csv_path: pathlib.Path):
    """读 basic-pitch 的 note_events csv（不做任何后处理）。"""
    notes = []
    with open(csv_path, newline="", encoding="utf-8") as fh:
        reader = csv.reader(fh)
        header = next(reader)
        assert header[:4] == ["start_time_s", "end_time_s", "pitch_midi", "velocity"], header
        for row in reader:
            if not row or not row[0]:
                continue
            bends = [int(x) for x in row[4:]] if len(row) > 4 else []
            notes.append(
                dict(
                    start=float(row[0]),
                    end=float(row[1]),
                    pitch=int(row[2]),
                    velocity=int(row[3]),
                    amplitude=int(row[3]) / 127.0,
                    bend_bins=bends,
                )
            )
    notes.sort(key=lambda n: (n["start"], n["pitch"]))
    return notes


def load_bp_model_output(npz_path: pathlib.Path):
    """读 --save-model-outputs 的原始后验概率图（note / onset / contour）。"""
    data = np.load(npz_path, allow_pickle=True)
    payload = data["basic_pitch_model_output"]
    payload = payload.item() if payload.shape == () else payload
    return {k: np.asarray(v) for k, v in payload.items()}


def bass_band(y: np.ndarray, lo=40.0, hi=300.0, sr=SR) -> np.ndarray:
    sos = ss.butter(4, [lo / (sr / 2), hi / (sr / 2)], btype="band", output="sos")
    return ss.sosfiltfilt(sos, y).astype(np.float32)


def estimate_f0(seg: np.ndarray, sr=SR):
    """独立 F0 估计（pyin）——只用来给 Basic Pitch 做第二意见。"""
    if len(seg) < 1024:
        pad = np.zeros(1024 - len(seg), dtype=np.float32)
        seg = np.concatenate([seg, pad])
    try:
        f0, voiced, _ = librosa.pyin(
            seg, fmin=32.7, fmax=400.0, sr=sr, frame_length=2048, center=True
        )
    except Exception:
        return None
    good = f0[voiced & np.isfinite(f0)]
    if len(good) < 3:
        return None
    return float(np.median(good))


def hz_to_midi(f):
    if not f or f <= 0:
        return None
    return 69 + 12 * np.log2(f / 440.0)


def estimate_f0_fft(seg, sr=SR, lo=35.0, hi=500.0):
    """频谱法：取低频带里「最低的强峰」当基频（对电贝斯这种低音为主的有效）。"""
    if len(seg) < 2048:
        seg = np.concatenate([seg, np.zeros(2048 - len(seg), dtype=np.float32)])
    seg = seg * np.hanning(len(seg))
    spec = np.abs(np.fft.rfft(seg, n=16384))
    freqs = np.fft.rfftfreq(16384, 1.0 / sr)
    band = (freqs >= lo) & (freqs <= hi)
    if not band.any():
        return None
    idx = np.where(band)[0]
    local = spec[idx]
    if local.max() <= 0:
        return None
    peaks, _ = ss.find_peaks(local, height=0.35 * local.max())
    if len(peaks) == 0:
        return None
    # 最低的强峰（跳过 DC 附近的直流/低频噪声）
    p = None
    for cand in peaks:
        f = freqs[idx[cand]]
        if f >= lo:
            p = cand
            break
    if p is None:
        return None
    # 抛物线插值细化
    k = idx[p]
    if 0 < k < len(spec) - 1:
        a, b, c = np.log(spec[k - 1] + 1e-12), np.log(spec[k] + 1e-12), np.log(spec[k + 1] + 1e-12)
        denom = a - 2 * b + c
        delta = 0.5 * (a - c) / denom if abs(denom) > 1e-12 else 0.0
    else:
        delta = 0.0
    return float(freqs[k] + delta * (freqs[1] - freqs[0]))


def estimate_f0_robust(seg, sr=SR):
    """pyin + 频谱双估计：选更能解释谐波序列的那个，并给出两者是否一致。"""
    cands = []
    f_pyin = estimate_f0(seg)
    if f_pyin:
        cands.append(("pyin", f_pyin))
    f_fft = estimate_f0_fft(seg, sr=sr)
    if f_fft:
        cands.append(("fft", f_fft))
    if not cands:
        return None, "none", None
    if len(cands) == 1:
        return cands[0][1], cands[0][0], None
    semi = abs(12 * np.log2(cands[0][1] / cands[1][1]))
    agree = semi < 0.5
    if agree:
        return float(np.mean([c[1] for c in cands])), "both", 0.0
    # 不一致：用谐波能量和选更可信的那个
    seg = seg * np.hanning(len(seg))
    spec = np.abs(np.fft.rfft(seg, n=16384))
    freqs = np.fft.rfftfreq(16384, 1.0 / sr)

    def harmonic_score(f):
        s = 0.0
        for h in (1, 2, 3, 4):
            fh = f * h
            if fh >= freqs[-1]:
                break
            k = int(np.argmin(np.abs(freqs - fh)))
            s += float(spec[max(0, k - 2) : k + 3].max())
        return s

    best = max(cands, key=lambda c: harmonic_score(c[1]))
    return best[1], best[0], float(semi)


def best_shift_ms(bp_times, map_times, search=1.2, step=0.01):
    """找「MAP 时间 + shift ≈ BP 时间」的最佳整体偏移（应对整拍相位差）。"""
    bp = np.asarray(bp_times, dtype=float)
    diffs = []
    for mt in map_times:
        near = bp[(bp > mt - search) & (bp < mt + search)] - mt
        diffs.extend(near.tolist())
    if not diffs:
        return 0.0, 0
    diffs = np.asarray(diffs)
    bins = int(2 * search / step)
    hist, edges = np.histogram(diffs, bins=bins, range=(-search, search))
    k = int(np.argmax(hist))
    return float((edges[k] + edges[k + 1]) / 2), int(hist[k])


def parse_osu(path: pathlib.Path):
    """只取 mania 的 HitObjects（时间 + 列 + 是否长条）。"""
    keycount, notes = 5, []
    section = None
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            s = line.strip()
            if s.startswith("[") and s.endswith("]"):
                section = s
                continue
            if section == "[Difficulty]" and s.startswith("CircleSize"):
                keycount = int(float(s.split(":")[1]))
            elif section == "[HitObjects]" and s and not s.startswith("//"):
                parts = s.split(",")
                if len(parts) < 4:
                    continue
                x, t, typ = float(parts[0]), float(parts[2]), int(parts[3])
                end = float(parts[5].split(":")[0]) if (typ & 128) and len(parts) > 5 else None
                col = min(keycount - 1, max(0, int(x * keycount / 512)))
                notes.append(dict(time=t / 1000.0, end=(end / 1000.0) if end else None, column=col))
    notes.sort(key=lambda n: n["time"])
    return keycount, notes


def match_events(a_times, b_times, tol):
    """贪心最近邻配对，返回 (matched_pairs, unmatched_a_idx, unmatched_b_idx)。"""
    a = np.asarray(a_times, dtype=float)
    b = np.asarray(b_times, dtype=float)
    used_b = np.zeros(len(b), dtype=bool)
    pairs, un_a = [], []
    for i, ta in enumerate(a):
        if len(b) == 0:
            un_a.append(i)
            continue
        j = int(np.argmin(np.abs(b - ta)))
        if abs(b[j] - ta) <= tol and not used_b[j]:
            used_b[j] = True
            pairs.append((i, j, float(b[j] - ta)))
        else:
            un_a.append(i)
    un_b = [j for j in range(len(b)) if not used_b[j]]
    return pairs, un_a, un_b


def synth_notes(note_list, duration, sr=SR, style="bass"):
    """把 note event 渲染成用于试听的音频（仅供人耳 A/B，不是最终合成方案）。"""
    out = np.zeros(int(duration * sr) + sr, dtype=np.float32)
    for n in note_list:
        t0 = n.get("start", n.get("startTime"))
        t1 = n.get("end", n.get("endTime"))
        start = int(t0 * sr)
        end = int(min(t1, duration) * sr)
        if end <= start:
            end = start + int(0.08 * sr)
        t = np.arange(end - start) / sr
        if style == "bass":
            f = 440.0 * 2 ** ((n["pitch"] - 69) / 12.0)
            env = np.exp(-t * 3.2).astype(np.float32)
            sig = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t)
        else:  # click：给 Mapperatorinator 谱面对照用
            env = np.exp(-t * 60.0).astype(np.float32)
            sig = np.sin(2 * np.pi * 1200 * t)
        amp = 0.30 if style == "bass" else 0.25
        seg = amp * env * sig.astype(np.float32)
        out[start : start + len(seg)] += seg
    return np.clip(out, -1.0, 1.0)


def write_wav(path: pathlib.Path, data: np.ndarray, sr=SR):
    data = np.clip(data, -1.0, 1.0).astype(np.float32)
    sf.write(str(path), data, sr, subtype="PCM_16")


# ---------------------------------------------------------------- 主分析


def analyze(song_key: str, conf: dict) -> dict:
    out_dir = VERIFY_OUT / song_key
    out_dir.mkdir(parents=True, exist_ok=True)
    # 清掉上一轮生成的切片/图，避免改动 focus 窗口后留下过期文件
    for pat in ("listen_*.wav", "cmp_*.wav", "focus_compare_*.txt", "ascii_roll_*.txt", "piano_roll_*.png"):
        for stale in out_dir.glob(pat):
            stale.unlink()
    print(f"\n=== {song_key} · {conf['title']} ===")

    notes = load_bp_notes(pathlib.Path(str(conf["bp"]) + ".csv"))
    raw = load_bp_model_output(pathlib.Path(str(conf["bp"]) + ".npz"))
    onset_map, note_map, contour_map = raw["onset"], raw["note"], raw["contour"]
    n_frames = note_map.shape[0]

    y, _ = librosa.load(str(conf["stem"]), sr=SR, mono=True)
    duration = len(y) / SR
    y_bass = bass_band(y)

    # ---- 1. 统一 note event（保留 confidence / onset 强度 / pitch bend）
    events = []
    for n in notes:
        s_i = min(n_frames - 1, max(0, time_to_frame(n["start"])))
        e_i = min(n_frames, max(s_i + 1, time_to_frame(n["end"])))
        p_idx = n["pitch"] - 21  # 模型输出从 A0(MIDI21) 起
        onset_strength = float(onset_map[max(0, s_i - 1) : s_i + 2, p_idx].max())
        frame_act = float(note_map[s_i:e_i, p_idx].mean()) if 0 <= p_idx < note_map.shape[1] else 0.0
        bends = [b / BINS_PER_SEMITONE for b in n["bend_bins"]]
        events.append(
            dict(
                startTime=round(n["start"], 4),
                endTime=round(n["end"], 4),
                duration=round(n["end"] - n["start"], 4),
                pitch=n["pitch"],
                pitchName=pitch_name(n["pitch"]),
                velocity=n["velocity"],
                amplitude=round(n["amplitude"], 4),
                meanFrameActivation=round(frame_act, 4),
                onsetStrength=round(onset_strength, 4),
                pitchBendSemitones=[round(b, 3) for b in bends],
                pitchBendRange=round(max(bends) - min(bends), 3) if bends else 0.0,
            )
        )
    with open(out_dir / "events.json", "w", encoding="utf-8") as fh:
        json.dump(events, fh, ensure_ascii=False, indent=1)

    # ---- 2. onset：第二意见（低频带谱通量）+ BP onset 强度
    onset_env = librosa.onset.onset_strength(y=y_bass, sr=SR, hop_length=FFT_HOP)
    ref_onsets = librosa.onset.onset_detect(
        onset_envelope=onset_env, sr=SR, hop_length=FFT_HOP, units="time", delta=0.03, wait=2
    )
    ref_onsets = np.asarray(sorted(ref_onsets))
    bp_onsets = np.array([e["startTime"] for e in events])
    pairs, un_bp, un_ref = match_events(bp_onsets, ref_onsets, tol=0.06)
    onset_agreement = len(pairs) / max(1, len(bp_onsets))

    strong = np.array([e["onsetStrength"] for e in events])
    weak_bp = [i for i in un_bp if strong[i] < 0.30]
    strong_bp = [i for i in un_bp if strong[i] >= 0.30]

    # ---- 3. pitch：对每个 note 做独立 F0（pyin + 频谱）比对
    BASS_LO, BASS_HI = 21, 60  # A0 ~ C4：Bass stem 的合理音域
    pitch_stats = dict(exact=0, octave=0, semitone=0, wrong=0, unknown=0, ambiguous=0)
    pitch_detail = []
    ref_midi = []
    for e in events:
        seg = y_bass[int(e["startTime"] * SR) : int(e["endTime"] * SR)]
        f0, method, ambiguity = estimate_f0_robust(seg)
        if f0 is None:
            pitch_stats["unknown"] += 1
            ref_midi.append(None)
            continue
        est = hz_to_midi(f0)
        diff = e["pitch"] - est
        folded = (diff + 6) % 12 - 6  # 折叠到 ±6 半音，看音级对不对
        if abs(diff) < 0.5:
            cls = "exact"
        elif abs(abs(diff) - 12) < 0.7:
            cls = "octave"
        elif abs(folded) < 0.5:
            cls = "semitone"
        elif abs(folded) < 1.5:
            cls = "wrong"
        else:
            cls = "wrong"
        if ambiguity is not None and ambiguity < 2.0:
            pitch_stats["ambiguous"] += 1
        pitch_stats[cls] += 1
        ref_midi.append(float(est))
        pitch_detail.append(
            dict(
                time=round(e["startTime"], 3),
                pitchName=e["pitchName"],
                bpMidi=e["pitch"],
                refMidi=round(float(est), 2),
                method=method,
                diffSemitones=round(float(diff), 2),
                class_=cls,
            )
        )

    # 3b. 轮廓方向一致性（相对音高运动是否可靠 —— 本轮最关键的一项）
    dir_agree, dir_total, dir_disagree_cases = 0, 0, []
    for i in range(len(events) - 1):
        if ref_midi[i] is None or ref_midi[i + 1] is None:
            continue
        gap = events[i + 1]["startTime"] - events[i]["startTime"]
        if gap > 3.0:
            continue
        d_bp = events[i + 1]["pitch"] - events[i]["pitch"]
        d_ref = ref_midi[i + 1] - ref_midi[i]
        if abs(d_bp) < 1 or abs(d_ref) < 1:
            continue
        dir_total += 1
        if np.sign(d_bp) == np.sign(d_ref):
            dir_agree += 1
        elif len(dir_disagree_cases) < 25:
            dir_disagree_cases.append(
                dict(
                    timeFrom=round(events[i]["startTime"], 3),
                    timeTo=round(events[i + 1]["startTime"], 3),
                    bp=f"{events[i]['pitchName']}->{events[i + 1]['pitchName']}",
                    ref=f"{ref_midi[i]:.1f}->{ref_midi[i + 1]:.1f}",
                )
            )

    # 3c. 音域越界（Bass stem 里的高音区音符 = 串音/伪影的主要来源）
    out_of_range = [e for e in events if not (BASS_LO <= e["pitch"] <= BASS_HI)]
    in_range_events = [e for e in events if BASS_LO <= e["pitch"] <= BASS_HI]

    # ---- 4. duration
    durs = np.array([e["duration"] for e in events])
    floor_notes = int((durs <= 0.135).sum())
    split_runs = 0
    for a, b in zip(events, events[1:]):
        if a["pitch"] == b["pitch"] and 0 < b["startTime"] - a["endTime"] <= 0.02:
            split_runs += 1
    long_notes = [e for e in events if e["duration"] >= 1.0]
    long_with_inner_onset = 0
    for e in long_notes:
        s_i, e_i = time_to_frame(e["startTime"]), time_to_frame(e["endTime"])
        p_idx = e["pitch"] - 21
        if 0 <= p_idx < onset_map.shape[1] and e_i > s_i + 3:
            inner = onset_map[s_i + 2 : e_i - 1, p_idx]
            if len(inner) and inner.max() >= 0.5:
                long_with_inner_onset += 1

    # ---- 5. pitch bend / slide 候选
    bend_notes = [e for e in events if e["pitchBendRange"] >= 0.5 and e["duration"] >= 0.25]
    bend_notes.sort(key=lambda e: -e["pitchBendRange"])

    # 5b. 滑音 vs 抖动：真滑音应当是「单调、且首尾差得多」的连续变化
    #     注意：csv 里 pitch_bend 是逐帧的 contour bin（3 bin = 1 半音）；
    #     这里两种顺序都试过，结论一致（都是抖动），所以统一按存储顺序处理。
    slide_like, wobble_like = [], []
    for e in bend_notes:
        b = np.asarray(e["pitchBendSemitones"], dtype=float)
        if len(b) < 4:
            continue
        delta = float(b[-1] - b[0])
        d = np.diff(b)
        up = float((d > 0).mean())
        down = float((d < 0).mean())
        mono = max(up, down)
        rec = dict(time=round(e["startTime"], 3), duration=round(e["duration"], 3),
                   range=round(e["pitchBendRange"], 2), delta=round(delta, 2),
                   monotonicity=round(mono, 2))
        if abs(delta) >= 0.5 and mono >= 0.6:
            slide_like.append(rec)
        elif mono < 0.6:
            wobble_like.append(rec)
    slide_like.sort(key=lambda r: -abs(r["delta"]))
    wobble_like.sort(key=lambda r: -r["range"])

    # 5c. 串音：高音区（>C4）音符的振幅/起始强度 vs Bass 音域
    hi_amp = float(np.mean([e["amplitude"] for e in out_of_range])) if out_of_range else 0.0
    lo_amp = float(np.mean([e["amplitude"] for e in in_range_events])) if in_range_events else 0.0
    hi_onset = float(np.mean([e["onsetStrength"] for e in out_of_range])) if out_of_range else 0.0
    lo_onset = float(np.mean([e["onsetStrength"] for e in in_range_events])) if in_range_events else 0.0

    # 5d. 用独立 F0 轨迹反查「素材里到底有没有真滑音」——避免把 BP 的缺陷当成素材结论
    ref_slide_like = []
    for e in in_range_events:
        if e["duration"] < 0.25:
            continue
        seg = y_bass[int(e["startTime"] * SR) : int(e["endTime"] * SR)]
        if len(seg) < 2048:
            continue
        try:
            f0, voiced, _ = librosa.pyin(
                seg, fmin=32.7, fmax=400.0, sr=SR, frame_length=2048, hop_length=256, center=True
            )
        except Exception:
            continue
        good = f0[voiced & np.isfinite(f0)]
        if len(good) < 6:
            continue
        m = 69 + 12 * np.log2(good / 440.0)
        m = ss.medfilt(m, 3)
        k = max(1, len(m) // 4)
        drift = float(np.median(m[-k:]) - np.median(m[:k]))
        d = np.diff(m)
        mono = max(float((d > 0).mean()), float((d < 0).mean()))
        if abs(drift) >= 1.0 and mono >= 0.6:
            ref_slide_like.append(
                dict(time=round(e["startTime"], 3), duration=round(e["duration"], 3),
                     drift=round(drift, 2), monotonicity=round(mono, 2))
            )
    ref_slide_like.sort(key=lambda r: -abs(r["drift"]))

    # ---- 6. 与 Mapperatorinator 同段对照（含整拍相位自动对齐）
    map_cmp = None
    if os.path.exists(conf["osu"]):
        keycount, map_notes = parse_osu(pathlib.Path(conf["osu"]))
        map_times = [n["time"] for n in map_notes]
        shift, peak = best_shift_ms(bp_onsets, map_times)

        def make_cmp(use_shift, w0=None, w1=None):
            off = shift if use_shift else 0.0
            sel_bp = [e for e in events if w0 is None or (w0 <= e["startTime"] < w1)]
            sel_map = [n for n in map_notes if w0 is None or (w0 <= n["time"] + off < w1)]
            bt = [e["startTime"] for e in sel_bp]
            mt = [n["time"] + off for n in sel_map]
            pairs_c, un_bp_c, un_map_c = match_events(bt, mt, tol=0.06)
            return dict(
                bpNotes=len(sel_bp),
                mapNotes=len(sel_map),
                matched=len(pairs_c),
                bpOnly=[round(bt[i], 3) for i in un_bp_c],
                bpOnlyWithStrongOnset=[
                    round(bt[i], 3) for i in un_bp_c if sel_bp[i]["onsetStrength"] >= 0.4
                ],
                bpOnlyBassRange=[
                    round(bt[i], 3)
                    for i in un_bp_c
                    if BASS_LO <= sel_bp[i]["pitch"] <= BASS_HI
                ],
                mapOnly=[round(mt[j], 3) for j in un_map_c],
            )

        map_cmp = dict(
            keycount=keycount,
            shiftMs=round(shift * 1000.0, 1),
            shiftPeakPairs=peak,
            mapNoteCount=len(map_notes),
            bpNoteCount=len(events),
            raw=make_cmp(False),
            aligned=make_cmp(True),
            windows=[
                dict(window=[w0, w1], raw=make_cmp(False, w0, w1), aligned=make_cmp(True, w0, w1))
                for (w0, w1) in conf["focus"]
            ],
        )

    # ---- 7. 输出：图 + 文本轮廓
    for (w0, w1) in conf["focus"]:
        tag = f"{int(w0)}_{int(w1)}s"
        sel = [e for e in events if w0 - 0.5 <= e["startTime"] < w1 + 0.5]
        if not sel:
            continue
        fig, ax = plt.subplots(figsize=(16, 6))
        for e in sel:
            ax.plot([e["startTime"], e["endTime"]], [e["pitch"]] * 2, lw=4, solid_capstyle="butt")
        ax.set_xlim(w0 - 0.5, w1 + 0.5)
        lo = min(e["pitch"] for e in sel) - 3
        hi = max(e["pitch"] for e in sel) + 3
        ax.set_ylim(lo, hi)
        ax.set_yticks(range(int(lo), int(hi) + 1))
        ax.set_yticklabels([pitch_name(p) for p in range(int(lo), int(hi) + 1)], fontsize=7)
        ax.set_xlabel("time (s)")
        ax.set_title(f"{song_key} · Basic Pitch note events · {w0:.0f}-{w1:.0f}s")
        ax.grid(alpha=0.25)
        fig.tight_layout()
        fig.savefig(out_dir / f"piano_roll_{tag}.png", dpi=110)
        plt.close(fig)

        cols = [w0 + i * 0.1 for i in range(int((w1 - w0) / 0.1) + 1)]
        with open(out_dir / f"ascii_roll_{tag}.txt", "w", encoding="utf-8") as fh:
            fh.write(f"# {song_key} {w0:.0f}-{w1:.0f}s  (每格 0.1s, 行=音高, ●=有音)\n")
            fh.write("# 时间轴: " + "".join(str(int(c) % 10) for c in cols) + "\n")
            for p in range(int(hi), int(lo) - 1, -1):
                row = []
                for t in cols:
                    hit = any(
                        e["pitch"] == p
                        and e["startTime"] <= t < max(e["endTime"], e["startTime"] + 0.05)
                        for e in sel
                    )
                    row.append("●" if hit else " ")
                fh.write(f"{pitch_name(p):>3} ({p:3d}) |{''.join(row)}|\n")
            fh.write("\n# 明细（time, pitch, duration, onset强度, bend范围）\n")
            for e in sorted(sel, key=lambda x: x["startTime"]):
                fh.write(
                    f"{e['startTime']:8.3f}  {e['pitchName']:>3}  pid={e['pitch']:3d}  "
                    f"dur={e['duration']:.3f}  onset={e['onsetStrength']:.2f}  bend={e['pitchBendRange']:.2f}\n"
                )

    # ---- 8. 试听包
    full_bp = synth_notes(events, duration)
    n_mix = min(len(y), len(full_bp))
    write_wav(out_dir / f"{song_key}_bp_sonified.wav", y[:n_mix] * 0.6 + full_bp[:n_mix] * 0.6)
    if map_cmp is not None:
        keycount, map_notes = parse_osu(pathlib.Path(conf["osu"]))
        off = map_cmp["shiftMs"] / 1000.0
        # 每 10s 的时间线对照表
        with open(out_dir / "timeline_compare.txt", "w", encoding="utf-8") as fh:
            fh.write(f"# {song_key}  每 10s：BP 音符数 / 谱面音符数 / 对齐上的音符数\n")
            fh.write(f"# 整体偏移 map_time + {map_cmp['shiftMs']:+.0f}ms ≈ bp_time\n")
            fh.write(f"{'窗口':>14} {'BP':>6} {'谱面':>6} {'对齐':>6} {'仅BP':>6} {'仅谱':>6}\n")
            t = 0.0
            while t < duration:
                sel_bp = [e["startTime"] for e in events if t <= e["startTime"] < t + 10]
                sel_map = [n["time"] + off for n in map_notes if t <= n["time"] + off < t + 10]
                pr, un_bp, un_map = match_events(sel_bp, sel_map, tol=0.06)
                fh.write(
                    f"{t:6.0f}-{t + 10:6.0f} {len(sel_bp):6d} {len(sel_map):6d} {len(pr):6d}"
                    f" {len(un_bp):6d} {len(un_map):6d}\n"
                )
                t += 10.0
        # 重点段：并排事件表
        for (w0, w1) in conf["focus"]:
            tag = f"{int(w0)}_{int(w1)}s"
            rows = []
            for e in events:
                if w0 <= e["startTime"] < w1:
                    rows.append((e["startTime"], "BP", f"{e['pitchName']:>3} pid={e['pitch']:3d} "
                                                          f"dur={e['duration']:.3f} onset={e['onsetStrength']:.2f}"))
            for n in map_notes:
                nt = n["time"] + off
                if w0 <= nt < w1:
                    rows.append((nt, "MAP", f"col={n['column']} "
                                            + (f"hold={n['end'] - n['time']:.2f}s" if n["end"] else "tap")))
            rows.sort(key=lambda r: (r[0], r[1]))
            with open(out_dir / f"focus_compare_{tag}.txt", "w", encoding="utf-8") as fh:
                fh.write(f"# {song_key} {w0:.0f}-{w1:.0f}s  BP vs Mapperatorinator（谱面已 +{off * 1000:+.0f}ms 对齐）\n")
                fh.write(f"{'time':>9}  {'src':<4} detail\n")
                for t, src, detail in rows:
                    fh.write(f"{t:9.3f}  {src:<4} {detail}\n")
        map_events = [dict(start=n["time"], end=(n["end"] or n["time"] + 0.1), pitch=40) for n in map_notes]
        for (w0, w1) in conf["focus"]:
            tag = f"{int(w0)}_{int(w1)}s"
            a, b = int(w0 * SR), int(w1 * SR)
            seg = y[a:b]
            clip = seg * 0.8
            write_wav(out_dir / f"cmp_{tag}_stem.wav", clip)
            bp_clip = synth_notes(
                [dict(start=e["startTime"] - w0, end=e["endTime"] - w0, pitch=e["pitch"]) for e in events
                 if w0 <= e["startTime"] < w1],
                w1 - w0,
            )[: len(seg)]
            write_wav(out_dir / f"cmp_{tag}_stem+basicpitch.wav", clip + bp_clip * 1.0)
            map_clip = synth_notes(
                [dict(start=(n["time"] - w0), end=(n["time"] - w0) + 0.06, pitch=40) for n in map_notes
                 if w0 <= n["time"] < w1],
                w1 - w0,
                style="click",
            )[: len(seg)]
            write_wav(out_dir / f"cmp_{tag}_stem+mapperatorinator.wav", clip + map_clip * 1.0)

    # ---- 9. 需要人耳复核的时间点
    listen = []
    for i in weak_bp[:12]:
        listen.append(("BP 疑似多抓（弱 onset 且第二意见没对齐）", round(events[i]["startTime"], 3),
                       f"onset={events[i]['onsetStrength']:.2f} pitch={events[i]['pitchName']}"))
    for j in un_ref[:12]:
        listen.append(("BP 疑似漏抓（低频起音没被转录）", round(float(ref_onsets[j]), 3), ""))
    for d in [d for d in pitch_detail if d["class_"] in ("octave", "semitone", "wrong")][:12]:
        listen.append((f"音高存疑（{d['class_']}）", d["time"],
                       f"BP={d['pitchName']} ref={d['refMidi']:.1f} diff={d['diffSemitones']:+.2f} via {d['method']}"))
    for c in dir_disagree_cases[:8]:
        listen.append(("音高运动方向与独立估计相反", c["timeFrom"],
                       f"{c['bp']} vs ref {c['ref']}"))
    for e in bend_notes[:12]:
        listen.append(("滑音候选（pitch bend 跨度大）", round(e["startTime"], 3),
                       f"bend={e['pitchBendRange']:.2f} 半音 dur={e['duration']:.2f}s"))
    if map_cmp:
        for w in map_cmp["windows"]:
            for t in w["aligned"]["bpOnlyWithStrongOnset"][:6]:
                listen.append((f"谱面漏掉但 BP 起音强 ({w['window'][0]:.0f}-{w['window'][1]:.0f}s)", t, ""))
            for t in w["aligned"]["mapOnly"][:6]:
                listen.append((f"谱面有音但 BP 没有 ({w['window'][0]:.0f}-{w['window'][1]:.0f}s)", t, ""))
    with open(out_dir / "listen_list.txt", "w", encoding="utf-8") as fh:
        fh.write(f"# {song_key} 人耳复核清单（每条已生成 {song_key}/listen_*.wav 切片）\n")
        for k, (kind, t, extra) in enumerate(listen):
            fh.write(f"#{k:02d}  {t:8.3f}s  {kind}  {extra}\n")
            a, b = int(max(0, t - 1.0) * SR), int(min(duration, t + 2.0) * SR)
            write_wav(out_dir / f"listen_{k:02d}_{t:.2f}s.wav", y[a:b])

    stats = dict(
        song=song_key,
        title=conf["title"],
        role=conf["role"],
        stem=str(conf["stem"]),
        duration=round(duration, 2),
        noteCount=len(events),
        pitchMin=min(e["pitch"] for e in events),
        pitchMax=max(e["pitch"] for e in events),
        pitchMinName=pitch_name(min(e["pitch"] for e in events)),
        pitchMaxName=pitch_name(max(e["pitch"] for e in events)),
        bpOnsets=len(bp_onsets),
        refOnsets=len(ref_onsets),
        onsetMatched=len(pairs),
        onsetAgreement=round(onset_agreement, 3),
        bpOnsetUnmatched=len(un_bp),
        bpOnsetUnmatchedStrongOnset=len(strong_bp),
        refOnsetUnmatched=len(un_ref),
        pitchStats=pitch_stats,
        pitchDetailSample=pitch_detail[:250],
        contourDirectionAgreement=round(dir_agree / max(1, dir_total), 3),
        contourDirectionPairs=dir_total,
        contourDisagreeCases=dir_disagree_cases,
        bassRangeNotes=len(in_range_events),
        outOfRangeNotes=len(out_of_range),
        outOfRangeExamples=[round(e["startTime"], 3) for e in out_of_range[:15]],
        durationMedian=round(float(np.median(durs)), 3),
        durationAtFloor=floor_notes,
        splitRuns=split_runs,
        longNotes=len(long_notes),
        longNotesWithInnerOnset=long_with_inner_onset,
        bendCandidateNotes=len(bend_notes),
        bendCandidates=bend_notes[:25],
        slideLikeCandidates=slide_like[:20],
        slideLikeCount=len(slide_like),
        wobbleCandidates=wobble_like[:20],
        wobbleCount=len(wobble_like),
        referenceSlideLikeCount=len(ref_slide_like),
        referenceSlideLike=ref_slide_like[:20],
        bleedStats=dict(
            outOfRangeMeanAmplitude=round(hi_amp, 3),
            inRangeMeanAmplitude=round(lo_amp, 3),
            outOfRangeMeanOnset=round(hi_onset, 3),
            inRangeMeanOnset=round(lo_onset, 3),
        ),
        mapComparison=map_cmp,
    )
    with open(out_dir / "stats.json", "w", encoding="utf-8") as fh:
        json.dump(stats, fh, ensure_ascii=False, indent=1)

    print(
        f"  notes={stats['noteCount']} 音域={stats['pitchMinName']}~{stats['pitchMaxName']}"
        f"  onset 与第二意见一致率={stats['onsetAgreement']:.0%}"
        f"  音高 exact={pitch_stats['exact']} octave={pitch_stats['octave']}"
        f" semitone={pitch_stats['semitone']} wrong={pitch_stats['wrong']}"
        f"  轮廓方向一致={stats['contourDirectionAgreement']:.0%}({dir_total})"
        f"  越界音={stats['outOfRangeNotes']}"
        f"  滑音候选={stats['bendCandidateNotes']}"
    )
    if map_cmp:
        a = map_cmp["aligned"]
        print(
            f"  对照 Mapperatorinator: 谱面 {map_cmp['mapNoteCount']} 音 / BP {map_cmp['bpNoteCount']} 音"
            f" / 最佳整体偏移 {map_cmp['shiftMs']:+.0f}ms"
            f" → 对齐 {a['matched']} / 仅BP {len(a['bpOnly'])} / 仅谱 {len(a['mapOnly'])}"
        )
    return stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--song", choices=sorted(SONGS))
    ap.add_argument("--all", action="store_true")
    args = ap.parse_args()
    keys = sorted(SONGS) if args.all else [args.song or "light"]
    all_stats = []
    for k in keys:
        all_stats.append(analyze(k, SONGS[k]))
    with open(VERIFY_OUT / "summary.json", "w", encoding="utf-8") as fh:
        json.dump(all_stats, fh, ensure_ascii=False, indent=1)
    print(f"\n完成 -> {VERIFY_OUT}")


if __name__ == "__main__":
    main()
