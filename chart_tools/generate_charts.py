# -*- coding: utf-8 -*-
"""
FallenAngel 谱面制作工具·第三步：谱面生成
用法: python generate_charts.py <instruments.json> <音频路径> <输出目录>

生成三张谱面:
  - demo_drums.bytes  鼓谱:  固定音色→轨道 (kick=0, snare=1, hihat=2)
  - demo_bass.bytes   贝斯谱: 音高→轨道 (40-180Hz 对数四分)
  - demo_synth.bytes  合成器谱: 音高→轨道 (800-5000Hz 对数四分) + 持续段长按

修剪规则(得分来自 instrument_analyze):
  kick : 正拍 score>=1.1 或 强证据 score>=1.7
  snare: 反拍 score>=1.15 或 强证据 score>=1.8
  hihat: score>=1.15
  bass : score>=0.9
  synth: score>=1.0
谱面 JSON 结构与 ChartData/NoteData 一致 (Unity JsonUtility 兼容)
"""
import sys
import json
import numpy as np
sys.path.insert(0, '.')
import analyze

LANES = 4


def quantize(t, phase, tick):
    k = round((t - phase) / tick)
    return round(phase + k * tick, 3)


def pitch_peak(samples, sr, t, lo, hi, take_lowest):
    """事件处主频估计：带内峰检测，返回 Hz 或 None。
    take_lowest=True 取最低强峰(贝斯基频)，False 取最强峰(合成器主音)"""
    n0 = int(t * sr)
    win_len = 2048
    if n0 + win_len > len(samples):
        return None
    win = samples[n0:n0 + win_len] * np.hanning(win_len)
    spec = np.abs(np.fft.rfft(win, 8192))
    freqs = np.fft.rfftfreq(8192, 1.0 / sr)
    mask = (freqs >= lo) & (freqs <= hi)
    s = spec[mask]
    f = freqs[mask]
    if len(s) < 3:
        return None
    peak_amp = s.max()
    if peak_amp < 1e-4:
        return None
    # 局部峰
    peaks = []
    for i in range(1, len(s) - 1):
        if s[i] >= s[i - 1] and s[i] >= s[i + 1] and s[i] >= 0.35 * peak_amp:
            peaks.append((f[i], s[i]))
    if not peaks:
        return None
    if take_lowest:
        return float(min(peaks)[0])
    return float(max(peaks, key=lambda p: p[1])[0])


def pitch_to_lane(freq, lo, hi):
    """对数四分频带 → 轨道 0-3"""
    if freq is None:
        return None
    ratio = (hi / lo) ** (1.0 / LANES)
    for i in range(LANES):
        if freq < lo * (ratio ** (i + 1)):
            return i
    return LANES - 1


def make_chart(song_name, chart_author, level, notes, bpm=160.0):
    chart = {
        "metadata": {
            "songName": song_name,
            "songArtist": "dazbee / *Luna",
            "chartAuthor": chart_author,
            "difficulty": 1,
            "level": level,
            "bpm": bpm,
            "offset": 0.0,
            "audioFileName": "demo_song",
            "previewStartTime": 63.0,
            "previewDuration": 24.0,
        },
        "notes": notes,
    }
    return chart


def main():
    ins_path = sys.argv[1] if len(sys.argv) > 1 else "demo_song_instruments.json"
    audio_path = sys.argv[2] if len(sys.argv) > 2 else "../Assets/Resources/Audio/demo_song.mp3"
    out_dir = sys.argv[3] if len(sys.argv) > 3 else "../Assets/Resources/Charts"

    with open(ins_path, encoding="utf-8") as f:
        rep = json.load(f)
    instruments = rep["instruments"]
    longnotes = rep["longnotes"]
    phase = rep["grid"]["phase"]
    tick = 60.0 / rep["grid"]["bpm"] / 4.0

    print("decode audio for pitch estimation")
    samples, sr = analyze.decode_mono(audio_path)

    # ---------- 鼓谱 ----------
    drum_notes = []
    for e in instruments["kick"]:
        t, s = e
        k16 = round((t - phase) / tick)
        quarter = (k16 % 4 == 0)
        if (quarter and s >= 1.1) or s >= 1.7:
            drum_notes.append({"lane": 0, "time": t, "type": 0, "duration": 0.0, "longNoteId": -1})
    for e in instruments["snare"]:
        t, s = e
        k16 = round((t - phase) / tick)
        backbeat = (k16 % 16 in (4, 12))
        if (backbeat and s >= 1.15) or s >= 1.8:
            drum_notes.append({"lane": 1, "time": t, "type": 0, "duration": 0.0, "longNoteId": -1})
    for e in instruments["hihat"]:
        t, s = e
        if s >= 1.15:
            drum_notes.append({"lane": 2, "time": t, "type": 0, "duration": 0.0, "longNoteId": -1})
    drum_notes.sort(key=lambda n: n["time"])
    print(f"drums: {len(drum_notes)} notes")

    # ---------- 贝斯谱 ----------
    bass_notes = []
    for e in instruments["bass"]:
        t, s = e
        if s < 0.9:
            continue
        freq = pitch_peak(samples, sr, t, 40, 180, take_lowest=True)
        lane = pitch_to_lane(freq, 40, 180)
        if lane is None:
            continue
        bass_notes.append({"lane": lane, "time": t, "type": 0, "duration": 0.0, "longNoteId": -1})
    bass_notes.sort(key=lambda n: n["time"])
    print(f"bass: {len(bass_notes)} notes")

    # ---------- 合成器谱 ----------
    synth_notes = []
    long_id = 0
    for e in instruments["high_lead"]:
        t, s = e
        if s < 1.0:
            continue
        freq = pitch_peak(samples, sr, t, 800, 5000, take_lowest=False)
        lane = pitch_to_lane(freq, 800, 5000)
        if lane is None:
            continue
        synth_notes.append({"lane": lane, "time": t, "type": 0, "duration": 0.0, "longNoteId": -1})
    # 持续段 → 长按（仅乐音特征 tonal；噪声特征=失真铺底，不做长按）
    for seg in longnotes.get("high", []):
        t0, t1, kind = seg
        if kind != "tonal" or t1 - t0 < 0.3:
            continue
        q0, q1 = quantize(t0, phase, tick), quantize(t1, phase, tick)
        if q1 - q0 < 0.3:
            continue
        lane = pitch_to_lane(pitch_peak(samples, sr, q0 + 0.05, 800, 5000, take_lowest=False), 800, 5000)
        if lane is None:
            lane = 0
        # 与该长按窗口重叠的单击去掉（同一轨道）
        synth_notes = [n for n in synth_notes
                       if not (n["lane"] == lane and q0 - 0.1 <= n["time"] <= q1 + 0.1)]
        synth_notes.append({"lane": lane, "time": q0, "type": 1, "duration": round(q1 - q0, 3), "longNoteId": long_id})
        synth_notes.append({"lane": lane, "time": q1, "type": 3, "duration": 0.0, "longNoteId": long_id})
        long_id += 1
    synth_notes.sort(key=lambda n: n["time"])
    print(f"synth: {len(synth_notes)} notes (long {long_id})")

    # ---------- 写文件 ----------
    charts = [
        ("demo_drums", make_chart("Demo - Drums", "AutoGenerated", 6, drum_notes)),
        ("demo_bass", make_chart("Demo - Bass", "AutoGenerated", 5, bass_notes)),
        ("demo_synth", make_chart("Demo - Synth", "AutoGenerated", 5, synth_notes)),
    ]
    import os
    for name, chart in charts:
        out_path = os.path.join(out_dir, name + ".bytes")
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(chart, f, ensure_ascii=False, indent=1)
        print(f"written: {out_path} ({len(chart['notes'])} notes)")
        # 校验：长按头尾配对
        ids = [n for n in chart["notes"] if n["type"] == 1]
        ends = [n for n in chart["notes"] if n["type"] == 3]
        assert len(ids) == len(ends), f"{name}: long start/end mismatch"


if __name__ == "__main__":
    main()
