"""网格诊断：把"拍有没有跟住"和"哪一拍是小节头"彻底分开量。

研究者 2026-10-10 定的两层，缺一层就会产生幻觉：

**A. Subdivision Fit** —— 16 分格贴不贴音乐事件
    ① 到最近 16 分格的距离，累积三档命中率（≤0.10 / ≤0.25 / ≤0.50 个 16 分）
    ② 有符号 Δ 的相位直方图（看系统性偏移 / swing / 三连音 / 纯噪声）
    ③ 4 小节滚窗命中率（看哪里开始漂、哪里贴回来）
    + affine 网格的拟合残差（beat 时间对拍序号做最小二乘，报斜率与残差）

    这一层**测不出整体错一拍**：两条只差 1 拍的 4/4 网格，16 分线完全重合，
    上面的数一模一样。所以必须有 B。

**B. Measure Phase** —— 这些 16 分格里，哪一格该叫"小节 1 拍"
    不碰"最近 16 分格距离"那套。只把每条来源的 measure-relative phase
    （0/1/2/3 拍）两两对出来，逐段报告相位关系（aligned / 局部 +N 拍 / 没回来）。
    依然不投票判真值。三件事必须分开：**连续滑**（两条小节线长度略有出入）、
    **跳一格**（≈整数拍，这才是相位错位）、以及最近邻在两侧来回跳造成的假跳变。

**网格参数怎么来（两条路，不能混）**：
    affine 来源   → 整首最小二乘拟合（斜率=BPM）+ 残差
    piecewise 来源 → **每个 timing section 原样用**，分段算细分拟合；
                     绝不跨 red line 做全局拟合（那会把 piecewise 压回 affine）

    python tools/docs/deviation_report.py --song taishou --song flyway
"""

from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path

CACHE = Path(r"F:\vibe transcription\music_explorer\cache")
RUNS = Path(r"F:\vibe transcription\runs")
DOCS = Path(__file__).resolve().parents[2] / "static" / "docs"
OBSERVATIONS = Path(__file__).resolve().parents[2] / "static" / "observations"
SIXTEENTH_TIERS = (0.10, 0.25, 0.50)


def events_of(song: str, instrument: str = "adtof") -> list[float]:
    """规范事件里的音频秒（workbench 的缓存，ADTOF 鼓事件）。

    缓存按"来源 + 版本"分目录（`events/adtof_v1/adtof.json`），所以用 glob 找，
    别把版本号写死——换一版适配器就找不到文件，而失败的样子是"事件 0 个"，很容易被当成歌没数据。
    """
    base = CACHE / song / "events"
    for path in sorted(base.glob("*%s*/%s.json" % (instrument, instrument))):
        raw = json.loads(path.read_text(encoding="utf-8"))
        items = raw if isinstance(raw, list) else raw.get("events", [])
        out = []
        for item in items:
            for key in ("audioTime", "time", "timeSeconds"):
                value = item.get(key)
                if isinstance(value, (int, float)):
                    out.append(float(value))
                    break
        return sorted(out)
    return []


def least_squares(beats: list[float]) -> dict:
    """拍时间对拍序号做最小二乘：斜率→BPM，残差→这张网自身的时间噪声。"""
    if len(beats) < 8:
        return {"ok": False, "reason": "拍太少"}
    n = len(beats)
    mean_x = (n - 1) / 2
    mean_y = sum(beats) / n
    sxy = sum((i - mean_x) * (y - mean_y) for i, y in enumerate(beats))
    sxx = sum((i - mean_x) ** 2 for i in range(n))
    slope = sxy / sxx
    residuals = [y - (mean_y + slope * (i - mean_x)) for i, y in enumerate(beats)]
    return {"ok": True, "beatSeconds": slope, "bpm": 60.0 / slope,
            "residualMs": 1000 * statistics.median(abs(r) for r in residuals),
            "worstMs": 1000 * max(abs(r) for r in residuals)}


def raw_deltas(events: list[float], origin: float, beat_seconds: float) -> list[tuple[float, float]]:
    """每个事件到最近 16 分格的有符号距离（单位：16 分格的分数），折叠到 (-0.5, 0.5]。"""
    sixteenth = beat_seconds / 4
    out = []
    for time in events:
        n = round((time - origin) / sixteenth)
        value = ((time - origin) - n * sixteenth) / sixteenth
        out.append((time, ((value + 0.5) % 1.0) - 0.5))
    return out


def circular_mean(values: list[float]) -> float:
    """16 分格上的平均偏移——周期是 1，所以 +0.9 ≡ -0.1，得按圆上平均。"""
    if not values:
        return 0.0
    import math
    angles = [v * 2 * math.pi for v in values]
    return math.atan2(sum(math.sin(a) for a in angles) / len(angles),
                      sum(math.cos(a) for a in angles) / len(angles)) / (2 * math.pi)


def hit_rates(pairs: list[tuple[float, float]], offset: float) -> list[float]:
    """去掉一个固定偏移之后的三档命中率。"""
    if not pairs:
        return [0.0, 0.0, 0.0]
    residual = [((d - offset + 0.5) % 1.0) - 0.5 for _, d in pairs]
    return [sum(1 for r in residual if abs(r) <= tier) / len(residual) for tier in SIXTEENTH_TIERS]


def subdivision_fit(events: list[float], origin: float, beat_seconds: float) -> dict:
    """Layer A 的三个数：整体偏移 / 去偏移后的贴合（held-out）/ 随时间的漂移。

    **防自拟合作弊**：整体偏移只用一半事件估，另一半算命中率；两个方向都算。
    用同一批事件既拟合 offset 又给自己打分，任何一批点都会被美化。
    """
    if not events or not (beat_seconds > 0):
        return {"ok": False}
    pairs = raw_deltas(events, origin, beat_seconds)
    half = len(pairs) // 2
    offset_all = circular_mean([d for _, d in pairs])
    # 前半估、后半评；后半估、前半评
    fit_a = hit_rates(pairs[half:], circular_mean([d for _, d in pairs[:half]]))
    fit_b = hit_rates(pairs[:half], circular_mean([d for _, d in pairs[half:]]))
    held_out = [(a + b) / 2 for a, b in zip(fit_a, fit_b)]
    # 逐 4 小节窗的偏移：这里看的是"它随时间变不变"，不是贴合度
    windows, bar_seconds = [], beat_seconds * 4 * 4
    start = min(time for time, _ in pairs)
    while start <= max(time for time, _ in pairs):
        inside = [d for time, d in pairs if start <= time < start + bar_seconds]
        if len(inside) >= 4:
            windows.append(circular_mean(inside))
        start += bar_seconds
    spread = max(windows) - min(windows) if len(windows) >= 2 else 0.0
    step = max((abs(b - a) for a, b in zip(windows, windows[1:])), default=0.0)
    drift = "几乎没有" if spread <= 0.10 else "轻微" if spread <= 0.25 else "局部明显"
    return {"ok": True, "count": len(pairs), "phaseOffset": offset_all,
            "heldOut": held_out, "windows": windows, "windowSpread": spread,
            "maxStep": step, "drift": drift}


def affine_grid(song: str) -> dict | None:
    """鼓起音那条读法：origin=beat_phase、拍长=60/bpm。"""
    path = RUNS / song / "report.json"
    if not path.exists():
        return None
    report = json.loads(path.read_text(encoding="utf-8"))
    bpm = report["tempo"]["bpm"]
    return {"name": "drum-onset affine", "origin": report["tempo"]["beat_phase"],
            "beatSeconds": 60.0 / bpm, "bpm": bpm,
            "lock": report["downbeats"]["beat_this_lock"]}


def phase_series(downbeats: list[float], beats_per_bar: int = 4) -> list[tuple[float, int]]:
    """每条来源的 measure-relative phase：这条线是它自己小节里的第几拍。"""
    return [(time, index % beats_per_bar) for index, time in enumerate(sorted(downbeats))]


def osu_timing_points(path: Path) -> list[dict]:
    """读 .osu 的 [TimingPoints]，取红线（uninherited=1）：每条是一个 timing section 的开头。"""
    if not path.exists():
        return []
    points, inside = [], False
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        text = line.strip()
        if text.startswith("["):
            inside = text.lower() == "[timingpoints]"
            continue
        if not inside or not text or text.startswith("//"):
            continue
        parts = text.split(",")
        if len(parts) < 8:
            continue
        try:
            time_ms, beat_length = float(parts[0]), float(parts[1])
            meter, uninherited = int(float(parts[2])), parts[6].strip()
        except ValueError:
            continue
        if uninherited != "1" or beat_length <= 0:
            continue                                   # 绿线（SV）不进 timing
        points.append({"time": time_ms / 1000.0, "bpm": 60000.0 / beat_length, "meter": meter})
    return sorted(points, key=lambda point: point["time"])


def piecewise_sections(path: Path, song_end: float) -> list[dict]:
    points = osu_timing_points(path)
    out = []
    for index, point in enumerate(points):
        end = points[index + 1]["time"] if index + 1 < len(points) else song_end
        out.append({"start": point["time"], "end": end, "bpm": point["bpm"], "meter": point["meter"],
                    "seconds": max(0.0, end - point["time"])})
    return out


def subdivision_fit_piecewise(events: list[float], sections: list[dict]) -> dict:
    """piecewise 来源：**逐 section 评分再汇总**。

    绝不先把红线摊成一条整曲 pseudo-grid——那等于把分段 timing 压回 affine，而它全部价值
    就在那段一段的局部性。太短或事件太少的 section 标成"不可评"、不入汇总：这正是那两条
    只持续 300 ms 的抖动红线该有的下场（自然降权，而不是被抹掉）。
    """
    rows = []
    for section in sections:
        inside = [time for time in events if section["start"] <= time < section["end"]]
        if len(inside) < 8 or section["seconds"] < 4 * 60.0 / section["bpm"]:
            rows.append({"section": section, "scorable": False, "count": len(inside)})
            continue
        fit = subdivision_fit(inside, section["start"], 60.0 / section["bpm"])
        fit["section"], fit["scorable"] = section, True
        rows.append(fit)
    scored = [row for row in rows if row.get("scorable")]
    total = sum(row["count"] for row in scored) or 1
    aggregate = {"sections": len(sections), "scored": len(scored),
                 "unscorable": len(rows) - len(scored), "events": total,
                 "heldOut": [sum(row["heldOut"][i] * row["count"] for row in scored) / total
                             for i in range(3)],
                 "offsets": [row["phaseOffset"] for row in scored]}
    if aggregate["offsets"]:
        values = aggregate["offsets"]
        aggregate["offsetSpread"] = max(values) - min(values)
        aggregate["maxStep"] = max((abs(b - a) for a, b in zip(values, values[1:])), default=0.0)
        aggregate["drift"] = ("几乎没有" if aggregate["offsetSpread"] <= 0.10
                              else "轻微" if aggregate["offsetSpread"] <= 0.25 else "局部明显")
    return {"rows": rows, "aggregate": aggregate}


MAPPER_OUT = Path(r"D:\fa_tools\mapperatorinator\out")


def mapper_osu(song: str) -> Path | None:
    """这首歌的 Mapperatorinator timing 输出（`<song>_timing/beatmap*.osu`）。"""
    found = sorted((MAPPER_OUT / ("%s_timing" % song)).glob("beatmap*.osu"))
    return found[-1] if found else None


def median_step(times: list[float]) -> float:
    """相邻间隔的中位数。两条序列都用它，别用（末-首）/（n-1）：后者会被一个坏点拖走。"""
    steps = [b - a for a, b in zip(times, times[1:]) if b > a]
    return statistics.median(steps) if steps else 0.0


def meter_mix(beats: list[float], measures: list[float], beat_seconds: float) -> str:
    """这条来源自己数出来的"每小节几拍"分布。

    必须有：一段"相位错了两拍"可能只是它在那一小段把拍号数成了 2 拍，
    不写出来的话，读报告的人会把两种完全不同的东西看成同一件事。
    """
    if len(measures) < 2 or beat_seconds <= 0 or not beats:
        return "—"
    counts = {}
    for a, b in zip(measures, measures[1:]):
        inside = sum(1 for beat in beats if a - beat_seconds / 2 <= beat < b - beat_seconds / 2)
        counts[inside] = counts.get(inside, 0) + 1
    ordered = sorted(counts.items(), key=lambda item: -item[1])
    return " ".join("%d 拍×%d" % (bar, n) for bar, n in ordered[:3])


def human_readings(cal: dict) -> dict | None:
    """人那条读法的小节头。

    只用「小节头」那几次按压：手机同时记两个钟，`songTime`（播放器位置）会一步一跳，
    `captureTime`（手机自己的钟）是准的。所以**时间轴用 songTime 定锚，间隔用 captureTime 量**
    —— 这正是转换器当初定下的规矩，这里不另立一套。
    """
    taps = sorted(cal.get("rawTaps") or [], key=lambda tap: tap.get("tapIndex", 0))
    marks = [tap for tap in taps if str(tap.get("button", "")).upper() == "DOWNBEAT"]
    if len(marks) < 2:
        return None
    first = marks[0]
    anchor, clock = float(first["songTime"]), float(first.get("captureTime", first["songTime"]))
    measures = [anchor + (float(tap.get("captureTime", clock)) - clock) for tap in marks]
    taps_per_bar = statistics.median(
        [b.get("tapIndex", 0) - a.get("tapIndex", 0) for a, b in zip(marks, marks[1:])]) or 4
    beat_seconds = median_step([float(tap.get("captureTime", 0)) for tap in taps])
    if beat_seconds <= 0:
        return None
    return {"measures": measures, "beatSeconds": beat_seconds,
            "beatsPerBar": float(taps_per_bar), "marks": len(marks)}


def doc_calibrations(song: str) -> list[dict]:
    path = DOCS / ("%s.mgmcs.json" % song)
    if not path.exists():
        return []
    return json.loads(path.read_text(encoding="utf-8")).get("calibrations") or []


def source_readings(song: str) -> list[dict]:
    """每条来源各自的小节头，**不做任何跨来源外推或对齐**。

    `measures` 是这条来源自己说的小节起点；`kind` 区分它是模型/人直接报的，
    还是按自己的恒速读法铺出来的。以后 Mapperatorinator 作为第五条来源从这里接进来。
    """
    out = []
    cals = doc_calibrations(song)

    beats_path = RUNS / song / "beat_this.json"
    if beats_path.exists():
        data = json.loads(beats_path.read_text(encoding="utf-8"))
        beat_seconds = median_step(sorted(float(b) for b in data["beats"]))
        measures = sorted(float(b) for b in data.get("downbeats") or [])
        if len(measures) >= 2 and beat_seconds > 0:
            out.append({"name": "BeatThis", "kind": "explicit", "measures": measures,
                        "beatSeconds": beat_seconds, "beatsPerBar": 4.0, "health": "ok",
                        "meterMix": meter_mix([float(b) for b in data["beats"]], measures, beat_seconds),
                        "note": "Beat This! 自己报的小节线；它的每小节拍数会变，所以小节长度按它自己的间隔取中位"})

    for cal in cals:
        measures = sorted(float(t) for t in (cal.get("downbeats") or []))
        if cal.get("calibrationId") == "madmom" and len(measures) >= 2:
            out.append({"name": "madmom", "kind": "explicit", "measures": measures,
                        "beatSeconds": 60.0 / float(cal["bpm"]), "beatsPerBar": 4.0,
                        "health": cal.get("health", "ok"),
                        "note": "madmom 的 downbeat 模型自己听出来的小节线"})

    for cal in cals:
        if cal.get("source") != "human":
            continue
        reading = human_readings(cal)
        if not reading:
            continue
        out.append({"name": "human:%s" % cal.get("calibrationId"), "kind": "explicit",
                    "measures": sorted(reading["measures"]), "beatSeconds": reading["beatSeconds"],
                    "beatsPerBar": reading["beatsPerBar"], "health": cal.get("health", "ok"),
                    "meterMix": "%d 拍×%d（按两个小节头之间数了几下）"
                                % (reading["beatsPerBar"], reading["marks"] - 1),
                    "note": "人按小节头那几次；间隔用手机自己的钟（%d 下）" % reading["marks"]})

    reading = mapperatorinator_reading(song)
    if reading:
        out.append(reading)
    for cal in cals:
        if cal.get("calibrationId") != "workbench":
            continue
        bar_lines = sorted(float(line["time"]) for line in (cal.get("barLines") or []))
        if len(bar_lines) >= 2:
            out.append({"name": "workbench", "kind": "explicit", "measures": bar_lines,
                        "beatSeconds": 60.0 / float(cal["bpm"]), "beatsPerBar": 4.0,
                        "health": cal.get("health", "ok"), "meterMix": "不等长（上游写下来的接缝）",
                        "note": "上游写下来的小节线列表（含不等长小节）"})
            continue
        if not cal.get("audioTime") or not cal.get("bpm"):
            continue
        bar_seconds = float(cal["beatsPerBar"]) * 60.0 / float(cal["bpm"])
        events = doc_events(song)
        if not events:
            continue
        start = float(cal["audioTime"]) - bar_seconds
        while start > 0:
            start -= bar_seconds
        measures, step = [], 0
        while start + step * bar_seconds <= max(events) + bar_seconds:
            measures.append(start + step * bar_seconds)
            step += 1
        out.append({"name": "workbench", "kind": "derived", "measures": measures,
                    "beatSeconds": 60.0 / float(cal["bpm"]), "beatsPerBar": float(cal["beatsPerBar"]),
                    "health": cal.get("health", "ok"),
                    "meterMix": "%d 拍（铺出来时假设的）" % int(cal["beatsPerBar"]),
                    "note": "起点 + 恒速铺出来（这条读法本身就是恒速假设）"})
    return out


def mapperatorinator_reading(song: str) -> dict | None:
    """第五条来源：Mapperatorinator 自己报的 measure observation（不是从红线折算的）。

    它的 health 只反映**它自己输出得像不像话**（局部缺失、输出越界这种），
    "和 Beat This 不一致"不会被算进去——分歧只是证据（§13）。
    """
    path = OBSERVATIONS / ("%s.mapperatorinator.json" % song)
    if not path.exists() and song.endswith(".raw"):
        path = OBSERVATIONS / ("%s.mapperatorinator.json" % song[: -len(".raw")])
    if not path.exists():
        return None
    doc = json.loads(path.read_text(encoding="utf-8"))
    observations = doc.get("observations") or {}
    measures = sorted(float(time) for time in observations.get("measures") or [])
    steps = [float(step) for step in (doc.get("derived") or {}).get("localBeatSeconds") or []]
    beat_seconds = statistics.median(steps) if steps else 0.0
    if len(measures) < 2 or beat_seconds <= 0:
        return None
    health = doc.get("health") or {}
    status = health.get("status", "usable")
    note = "模型自己报的 measure observation；%s" % doc.get("observationsFrom", "来源未写")
    if health.get("reasons"):
        note += "；health %s：%s" % (status, "；".join(health["reasons"]))
    if health.get("notes"):
        note += "；小事：%s" % "；".join(health["notes"])
    return {"name": "mapperatorinator", "kind": "explicit", "measures": measures,
            "beatSeconds": beat_seconds, "beatsPerBar": 4.0,
            "health": "ok" if status == "usable" else "suspect",
            "meterMix": meter_mix((doc.get("derived") or {}).get("pulsePositions") or [],
                                  measures, beat_seconds),
            "note": note}


def doc_events(song: str) -> list[float]:
    path = DOCS / ("%s.mgmcs.json" % song)
    if not path.exists():
        return []
    out = []
    for event in json.loads(path.read_text(encoding="utf-8")).get("events") or []:
        for key in ("audioTime", "startTime", "endTime"):
            if isinstance(event.get(key), (int, float)):
                out.append(float(event[key]))
    return sorted(out)


def pair_phase_votes(measures_a: list[float], measures_b: list[float],
                     beat_seconds: float, beats_per_bar: float,
                     residual_cap: float = 0.25) -> list[dict]:
    """A 的每个小节头，对到最近的 B 小节头，看差了几拍。

    只留两种情况：残差 ≤ 0.25 拍（这就是"差整数拍"的相位关系），
    或差得超过半个小节（那就不是相位关系，是"附近没有对应的小节"）。**不投票，不做多数**。
    """
    votes = []
    if not measures_a or not measures_b or beat_seconds <= 0:
        return votes
    index = 0
    for time in measures_a:
        while index + 1 < len(measures_b) and abs(measures_b[index + 1] - time) <= abs(measures_b[index] - time):
            index += 1
        nearest = measures_b[index]
        delta_beats = (time - nearest) / beat_seconds
        vote = {"time": time, "nearest": nearest, "deltaBeats": delta_beats,
                "k": None, "residual": None}
        if abs(delta_beats) <= beats_per_bar / 2.0 + residual_cap:
            k = round(delta_beats)
            residual = delta_beats - k
            if abs(residual) <= residual_cap:
                vote["k"], vote["residual"] = int(k), residual
        votes.append(vote)
    return votes


def phase_runs(votes: list[dict], bar_seconds: float, source_measures: int,
               gap_bars: float = 3.0, min_votes: int = 3) -> list[dict]:
    """把同一相位关系的连续几票合成一段。中间隔太久就断开，免得把两处并入一段。"""
    runs, current = [], None
    for vote in votes:
        if vote["k"] is None:
            continue                      # 对不上的一票只是空档，不是"关系结束"
        starts_new = (current is None or current["k"] != vote["k"]
                      or vote["time"] - current["end"] > gap_bars * bar_seconds)
        if starts_new:
            current = {"k": vote["k"], "start": vote["time"], "end": vote["time"],
                       "count": 1, "residuals": [vote["residual"]]}
            runs.append(current)
        else:
            current["end"] = vote["time"]
            current["count"] += 1
            current["residuals"].append(vote["residual"])
    floor = min_votes if source_measures >= 8 else 2
    return [run for run in runs if run["count"] >= floor]


def slip_phrase(k: int, bar_beats: float) -> str:
    """半小节的错位没有方向可言：早半小节和晚半小节是同一个相位关系。

    最近邻在这种正好落在两小节正中间的地方会随便选一边，写 "+2 拍" 会让人以为
    方向是量出来的。所以半小节一律写成 ±。
    """
    if k == 0:
        return "aligned"
    if abs(abs(k) * 2 - bar_beats) < 0.01:
        return "±%g 拍（半小节）" % abs(k)
    return "局部 %+d 拍" % k


def pair_shape(runs: list[dict], bar_beats: float = 4.0) -> str:
    """一句话的形状：一致 / 局部错位后合上 / 一路错过 / 都在跳。只描述，不解释原因。"""
    heads = [run["k"] for run in runs]
    if not runs:
        return "没有稳定的相位关系"
    if all(k == 0 for k in heads):
        return "全段一致"
    if 0 not in heads:
        if len(set(heads)) == 1:
            return "全段固定差 %s（整体相位差，不是局部错位）" % slip_phrase(heads[0], bar_beats)
        return "整段都对不上相位（几条关系轮着来）"
    first, last = heads[0], heads[-1]
    slipped = [k for k in heads if k != 0]
    if first == 0 and last == 0:
        return "局部错位后又合上（reconvergence）：错位 %s" % ", ".join(
            slip_phrase(k, bar_beats) for k in dict.fromkeys(slipped))
    if first != 0 or last != 0:
        return "有一头是错位的（%s 那端没回来）" % slip_phrase(first if first != 0 else last, bar_beats)
    return "相位在几段之间来回换"


def unwrap_phase(phases: list[float], bar_beats: float) -> list[float]:
    """把相位放到"小节"这个圆上再展开。

    不加这一步，A 的小节正好落在 B 两个小节正中间时，最近的那条会在两侧来回跳，
    于是"正 2 拍"和"负 2 拍"会被算成一次 4 拍的跳变——那是量法自己造出来的假跳变。
    同一件事在小节圆上只有半小节那么远。
    """
    out = []
    previous_turn = None
    for phase in phases:
        turn = (phase / bar_beats + 0.5) % 1.0 - 0.5
        if previous_turn is None:
            out.append(turn)
        else:
            turn += round(previous_turn - turn)
            out.append(turn)
        previous_turn = out[-1]
    return [turn * bar_beats for turn in out]


def pair_phase_trend(votes: list[dict], bar_beats: float) -> dict:
    """相位是"连续滑"还是"跳了一格"——这两件事必须分开。

    连续滑通常只是两条小节线长度略有出入，跑几分钟才攒出可观偏差；
    跳一格（≈整数拍）才是真的相位错位。只量，不解释原因。
    """
    matched = [vote for vote in votes if vote["k"] is not None]
    out = {"beatsPerMinute": None, "maxJump": None, "jumpTime": None, "count": len(matched)}
    if len(matched) < 3:
        return out
    times = [vote["time"] for vote in matched]
    phases = unwrap_phase([vote["deltaBeats"] for vote in matched], bar_beats)
    mean_t = sum(times) / len(times)
    mean_p = sum(phases) / len(phases)
    denom = sum((t - mean_t) ** 2 for t in times)
    slope = sum((t - mean_t) * (p - mean_p) for t, p in zip(times, phases)) / denom if denom else 0.0
    out["beatsPerMinute"] = slope * 60.0
    jumps = [(abs((p2 - p1) - slope * (t2 - t1)), t2)
             for t1, t2, p1, p2 in zip(times, times[1:], phases, phases[1:])]
    if jumps:
        out["maxJump"], out["jumpTime"] = max(jumps, key=lambda jump: jump[0])
    return out


def pair_verdict(row: dict) -> str:
    """一句话说清这对来源的相位到底怎么了。"""
    trend = row.get("trend") or {}
    votes = len(row["votes"])
    if votes < 4 or row["matched"] < 3:
        return "证据太少（%d 票，对上 %d）" % (votes, row["matched"])
    jump = trend.get("maxJump")
    slide = trend.get("beatsPerMinute")
    if jump is not None and jump >= 0.4:
        where = "" if trend.get("jumpTime") is None else " @%s" % clock(trend["jumpTime"])
        if row["runs"] and all(run["k"] == 0 for run in row["runs"]):
            return ("其余全段一致，只在这一处跳了 %+.2f 拍%s（一处≠一段：可能就是漏了一拍），"
                    "平时 %+.2f 拍/分钟" % (jump, where, slide or 0.0))
        return "%s；有一处 %+.2f 拍的跳变（这才是错位）%s，平时 %+.2f 拍/分钟" % (
            pair_shape(row["runs"], row["beatsPerBarB"]), jump, where, slide or 0.0)
    if slide is not None and abs(slide) >= 0.05:
        return "两条小节长度略有出入：相位连续滑 %+.2f 拍/分钟，最大跳变只有 %.2f 拍（没有突变）" % (
            slide, jump or 0.0)
    return "%s；不滑（%+.2f 拍/分钟，最大跳变 %.2f 拍）" % (
        pair_shape(row["runs"], row["beatsPerBarB"]), slide or 0.0, jump or 0.0)


def grain_matched(fine: dict, coarse: dict, residual_cap: float = 0.25) -> tuple[list[float], dict, str]:
    """小节长度不一样时，先问一句：是不是同一个小节被切成了几份？

    这是 pulse 层的分歧，不是"两条网格打架"：Mapperatorinator 在 flyway 上每 1.263s
    报一次小节，madmom 每 2.52s 报一次——正好 1:2。这种时候按粒度折算之后相位依然可比
    （它把一小节切成两个半数在数），直接判"不可比"会把真正的问题一起扔掉；
    但折算不上就是折算不上，不许凑。

    返回（用来提问的小节头、粒度标签、不可比时的理由）。
    """
    bar_fine = median_step(fine["measures"]) or fine["beatsPerBar"] * fine["beatSeconds"]
    bar_coarse = median_step(coarse["measures"]) or coarse["beatsPerBar"] * coarse["beatSeconds"]
    ratio = bar_coarse / bar_fine if bar_fine > 0 else 0.0
    if abs(ratio - 1) <= 0.15:
        return fine["measures"], {"label": "1:1", "offset": 0}, ""
    for stride in (2, 3, 4):
        if abs(ratio - stride) / stride > 0.05:
            continue
        best = None
        for offset in range(stride):
            candidate = fine["measures"][offset::stride]
            if len(candidate) < 2:
                continue
            # 用开头这十几个小节判断"从哪一个开始对"：偏移本身是个局部问题，
            # 再往后的偏差是两条线各自的拍速差攒出来的，那由趋势单独报。
            head = candidate[:20] if len(candidate) > 20 else candidate
            votes = pair_phase_votes(head, coarse["measures"],
                                     coarse["beatSeconds"], coarse["beatsPerBar"], residual_cap)
            matched = sum(1 for vote in votes if vote["k"] is not None)
            aligned = sum(1 for vote in votes if vote["k"] == 0)
            # 先看"对上了几票"，再看"是不是落在同一个小节位置"。只按匹配数挑的话，
            # 落在对方半小节上的那个偏移会跟真正的小节头一样得满分（两边都"差整数拍"）。
            if best is None or (aligned, matched) > (best[2], best[1]):
                best = (offset, matched, aligned, candidate, len(votes))
        if best and best[4] and best[1] / best[4] >= 0.8:
            return best[3], {"label": "1:%d" % stride, "offset": best[0]}, ""
        return fine["measures"], {"label": "1:1", "offset": 0}, (
            "%s 的小节约是 %s 的 1/%d（%.3fs vs %.3fs），按这个粒度折算也对不上——相位不可比"
            % (fine["name"], coarse["name"], stride, bar_fine, bar_coarse))
    return fine["measures"], {"label": "1:1", "offset": 0}, (
        "小节长度比 %.2f（%.3fs vs %.3fs）不是 1:1…1:4 的粒度，相位不可比"
        % (ratio, bar_fine, bar_coarse))


def pair_measure_phase(reading_a: dict, reading_b: dict) -> dict:
    """一对来源之间的相位关系。

    票数少的那条当 A：拿它自己说的那几个小节头去问"你在 B 里是第几拍"，
    这样稀疏来源（人只按了几下）不会被稠密来源的票数淹掉。

    小节长度差太多时先看是不是整数粒度（一条把一小节切成 2 份或 3 份在数）。
    是就按那个粒度折算再比，折算不了才说不可比——绝不硬凑成相位。
    """
    if len(reading_a["measures"]) > len(reading_b["measures"]):
        reading_a, reading_b = reading_b, reading_a
    asked, reference = reading_a, reading_b
    measures = asked["measures"]
    row = {"a": asked["name"], "b": reference["name"],
           "barA": median_step(asked["measures"]) or asked["beatsPerBar"] * asked["beatSeconds"],
           "barB": median_step(reference["measures"]) or reference["beatsPerBar"] * reference["beatSeconds"],
           "beatSecondsB": reference["beatSeconds"], "beatsPerBarB": reference["beatsPerBar"],
           "votes": [], "runs": [], "matched": 0, "aligned": 0, "unmatched": 0,
           "barGapPct": None, "residualRange": None, "grain": "1:1", "grainOffset": 0,
           "reason": None, "eligibility": "eligible"}
    if row["barA"] <= 0 or row["barB"] <= 0:
        row["reason"] = "小节长度取不出来"
        return row
    row["barGapPct"] = abs(row["barA"] - row["barB"]) / row["barB"] * 100
    if row["barGapPct"] > 15:
        fine, coarse = ((asked, reference) if row["barA"] <= row["barB"] else (reference, asked))
        measures, grain, reason = grain_matched(fine, coarse)
        if reason:
            row["reason"] = reason
            return row
        asked, reference = fine, coarse
        row["a"], row["b"] = fine["name"], coarse["name"]
        row["beatSecondsB"], row["beatsPerBarB"] = coarse["beatSeconds"], coarse["beatsPerBar"]
        row["grain"], row["grainOffset"] = grain["label"], grain["offset"]
    bar_asked = median_step(measures) or asked["beatsPerBar"] * asked["beatSeconds"]
    votes = pair_phase_votes(measures, reference["measures"],
                             row["beatSecondsB"], row["beatsPerBarB"])
    row["votes"] = votes
    row["trend"] = pair_phase_trend(votes, row["beatsPerBarB"])
    row["runs"] = phase_runs(votes, bar_asked, len(measures))
    matched = [vote for vote in votes if vote["k"] is not None]
    row["unmatched"] = len(votes) - len(matched)
    row["matched"] = len(matched)
    row["aligned"] = sum(1 for vote in matched if vote["k"] == 0)
    if matched:
        row["residualRange"] = (min(vote["residual"] for vote in matched),
                                max(vote["residual"] for vote in matched))
    if asked["health"] != "ok" or reference["health"] != "ok":
        row["eligibility"] = "caution"
    return row


def measure_phase(song: str) -> dict:
    """B 层：各来源的小节头两两对相位，只报关系，不判真值、不投票。"""
    readings = source_readings(song)
    usable = [reading for reading in readings if len(reading["measures"]) >= 2]
    pairs = []
    for i, reading_a in enumerate(usable):
        for reading_b in usable[i + 1:]:
            pairs.append(pair_measure_phase(reading_a, reading_b))
    pairs.sort(key=lambda row: ("human" in row["a"] or "human" in row["b"], row["a"], row["b"]))
    return {"readings": readings, "pairs": pairs}


def report_for(song: str) -> str:
    lines = ["", "GRID DIAGNOSTICS — %s" % song]
    grid = affine_grid(song)
    events = events_of(song)
    lines.append("")
    lines.append("SUBDIVISION FIT   （事件 %d 个：ADTOF 鼓点；网格：%s）"
                 % (len(events), (grid or {}).get("name", "无")))
    if grid and events:
        fit = subdivision_fit(events, grid["origin"], grid["beatSeconds"])
        lines.append("  整体偏移        %+.3f 个 16 分（不判好坏：里面可能混着 ADTOF 固定延迟、"
                     "演奏习惯、网格相位差）" % fit["phaseOffset"])
        lines.append("  校正后贴合      ≤0.10 %.0f%% · ≤0.25 %.0f%% · ≤0.50 %.0f%%"
                     "（held-out：前一半估偏移、后一半评，反向再评一次）"
                     % (fit["heldOut"][0] * 100, fit["heldOut"][1] * 100, fit["heldOut"][2] * 100))
        if fit["windows"]:
            seq = " ".join("%+.2f" % w for w in fit["windows"][:6])
            lines.append("  随时间漂移      %s（逐 4 小节偏移：%s%s；极差 %.2f，最大相邻跳变 %.2f）"
                         % (fit["drift"], seq, " …" if len(fit["windows"]) > 6 else "",
                            fit["windowSpread"], fit["maxStep"]))
        lines.append("  网格：%.3f BPM，lock %.3f" % (grid["bpm"], grid["lock"]))
    beats_path = RUNS / song / "beat_this.json"
    if beats_path.exists():
        beats = sorted(float(b) for b in json.loads(beats_path.read_text(encoding="utf-8"))["beats"])
        ls = least_squares(beats)
        if ls["ok"]:
            lines.append("  affine-fit residual（BeatThis）：%.0f ms（最差 %.0f ms），斜率折算 %.3f BPM"
                         % (ls["residualMs"], ls["worstMs"], ls["bpm"]))
            lines.append("      含义严格限定为：**一条固定 BPM 的直线描述不了这组 beat**。"
                         "不等于 'Beat This 坏了'——也可能是这首歌本来就有局部变速。")
    osu = mapper_osu(song)
    if osu and events:
        sections = piecewise_sections(osu, max(events) + 5)
        piece = subdivision_fit_piecewise(events, sections)
        agg = piece["aggregate"]
        lines.append("")
        lines.append("SUBDIVISION FIT — Mapperatorinator（piecewise，逐 section 评分再汇总）")
        lines.append("  红线 %d 条 → %d 段；可评 %d 段、不可评 %d 段（太短或事件 <8 的不入汇总）"
                     % (agg["sections"], agg["sections"], agg["scored"], agg["unscorable"]))
        for row in piece["rows"]:
            section = row["section"]
            if not row.get("scorable"):
                lines.append("    %7.1fs 起 %5.1fs  %7.3f BPM  事件 %2d  → 不可评"
                             % (section["start"], section["seconds"], section["bpm"], row["count"]))
                continue
            lines.append("    %7.1fs 起 %5.1fs  %7.3f BPM  事件 %3d  偏移 %+.3f  校正后 ≤0.10 %.0f%% / ≤0.25 %.0f%%  漂移 %s"
                         % (section["start"], section["seconds"], section["bpm"], row["count"],
                            row["phaseOffset"], row["heldOut"][0] * 100, row["heldOut"][1] * 100, row["drift"]))
        lines.append("  汇总（按事件数加权）：校正后 ≤0.10 %.0f%% · ≤0.25 %.0f%% · ≤0.50 %.0f%%；"
                     "段间偏移极差 %.2f，最大相邻跳变 %.2f → %s"
                     % (agg["heldOut"][0] * 100, agg["heldOut"][1] * 100, agg["heldOut"][2] * 100,
                        agg.get("offsetSpread", 0.0), agg.get("maxStep", 0.0), agg.get("drift", "-")))
    lines.append("")
    lines.append("MEASURE PHASE     （B 层：不碰 16 分距离，只比各来源的小节头对到哪一拍；不投票）")
    lines.extend(measures_lines(measure_phase(song)))
    return "\n".join(lines)


def clock(seconds: float) -> str:
    return "%02d:%02d" % (int(seconds) // 60, int(seconds) % 60)


def measures_lines(phase: dict) -> list[str]:
    readings = phase["readings"]
    if not readings:
        return ["  这首歌没有任何一条来源给出小节头，无从比相位。"]
    lines = ["  来源                  小节头  每小节   拍长    它自己数的每小节拍数        健康"]
    for reading in readings:
        if len(reading["measures"]) < 2:
            lines.append("    %-18s   %4d   —        —       —                          %s（不足两个，不参与比较）"
                         % (reading["name"], len(reading["measures"]), reading["health"]))
            continue
        lines.append("    %-18s   %4d   %.3fs  %.3fs  %-25s %s%s"
                     % (reading["name"], len(reading["measures"]),
                        median_step(reading["measures"]) or 0.0, reading["beatSeconds"],
                        reading.get("meterMix", "—"),
                        reading["health"], "" if reading["kind"] == "explicit" else "（铺出来的）"))
    if len(readings) < 2:
        lines.append("  只有一条来源，关系无从谈起——这一层至少要有两条小节线。")
        return lines
    lines.append("")
    for row in phase["pairs"]:
        head = "  %s ↔ %s" % (row["a"], row["b"])
        if row["reason"]:
            lines.append("%-38s %s" % (head, row["reason"]))
            continue
        if not row["votes"]:
            lines.append("%-38s 没有可对的票" % head)
            continue
        parts = []
        for run in row["runs"]:
            label = slip_phrase(run["k"], row["beatsPerBarB"])
            if run["k"] != 0 and "半小节" not in label:
                label += "（%+.2f 小节）" % (run["k"] * row["beatSecondsB"] / row["barB"])
            span = "%s–%s" % (clock(run["start"]), clock(run["end"]))
            parts.append("%s %s（%d 票）" % (label, span, run["count"]))
        if not parts:
            parts.append("没有连续三票以上的关系（都在跳）")
        extra = ""
        if row["matched"]:
            extra = "；相位对得上 %d/%d 票（aligned %d），残余 %+.2f…%+.2f 拍" % (
                row["matched"], len(row["votes"]), row["aligned"],
                row["residualRange"][0], row["residualRange"][1])
        if row["unmatched"]:
            extra += "；%s 有 %d 处附近找不到 %s 的小节头（可能小节长度在变、或在漂）" % (
                row["a"], row["unmatched"], row["b"])
        if row["barGapPct"] is not None and row["barGapPct"] > 2:
            extra += "；两条小节长度差 %.1f%%" % row["barGapPct"]
        if row["grain"] != "1:1":
            extra += "；粒度 %s（%s 从第 %d 个起折算）" % (row["grain"], row["a"], row["grainOffset"])
        if len(row["votes"]) < 4:
            extra += "；证据少（只有 %d 票）" % len(row["votes"])
        if row["eligibility"] != "eligible":
            extra += " → comparison_quality: %s" % row["eligibility"].upper()
        lines.append("%-34s %s" % (head, pair_verdict(row)))
        lines.append("      %s%s" % ("；".join(parts), extra))
    return lines


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    parser.add_argument("--library", action="store_true",
                        help="扫一遍 static/docs 里所有歌，只报相位关系不是'全段一致'的那些对")
    args = parser.parse_args(argv)
    if args.library:
        names = sorted({path.name[: -len(".mgmcs.json")] for path in DOCS.glob("*.mgmcs.json")})
        names = [name for name in names if not name.endswith(".raw")]
        for name in names:
            phase = measure_phase(name)
            notes = []
            for row in phase["pairs"]:
                if row["reason"]:
                    continue
                note = pair_verdict(row)
                if note.startswith("全段一致") or note.startswith("证据太少"):
                    continue
                notes.append("%s ↔ %s %s" % (row["a"], row["b"], note))
            print("%-12s %s" % (name, "；".join(notes) if notes else "（小节相位没有分歧）"))
        return 0
    for song in args.song or ["taishou", "flyway"]:
        print(report_for(song))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
