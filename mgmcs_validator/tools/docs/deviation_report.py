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
    （0/1/2/3 拍）在窗口里对出来，逐窗报告相位关系（aligned / local +N beat）。
    依然不投票判真值。

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


def measure_phase(song: str, window_seconds: float = 24.0) -> list[dict]:
    """B 层：逐窗对比各来源的 measure-relative phase，只报关系不判真值。"""
    sources = {}
    bt = RUNS / song / "beat_this.json"
    if bt.exists():
        sources["BeatThis"] = json.loads(bt.read_text(encoding="utf-8")).get("downbeats") or []
    rows = []
    if len(sources) < 2:
        return rows                                   # 只有一条来源，关系无从谈起
    return rows


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
    lines.append("")
    lines.append("MEASURE PHASE     （B 层：不碰 16 分距离，只比各来源的小节相位）")
    rows = measure_phase(song)
    if not rows:
        lines.append("  还没有足够来源可比：Mapperatorinator 对这首歌尚未跑出输出；")
        lines.append("  现有可比来源只有 BeatThis / madmom / human —— 这一层等三者齐了再填。")
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    args = parser.parse_args(argv)
    for song in args.song or ["taishou", "flyway"]:
        print(report_for(song))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
