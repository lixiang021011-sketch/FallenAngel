"""Mapperatorinator 的 timing 输出 → MGMCS 的 timing observation。

它在这里的角色只有一个：**局部 timing / measure 观察者**（§24）。
不替换任何现有网格，不当真值，不投票。

```
归档（不可重算的那一层）  static/observations/source/<歌>.mapperatorinator.source.json
                          = 模型自己那串 observation + 红线的 bpm/meter + 音频时长
派生（可重算的那一层）    static/observations/<歌>.mapperatorinator.json
```

为什么要走 `timing_events.json` 而不是直接读 .osu：`.osu` 只能装红线，模型报的每一个
MEASURE 在导出时就被折叠掉了，读回来的人只能**假设**"从红线起每 meter 拍是一个小节"。
这个假设正好是观察者不该做的（不等长小节、局部缺失、重新对齐都会丢）。所以给本机的
Mapperatorinator 加了一个 `dump_timing_events=true`，把模型自己那串
BEAT / MEASURE / TIMING_POINT 落成文件；没有这份文件时才退回读红线，并写明是折算的。

    python tools/docs/import_mapperatorinator_timing.py --song flyway --song hitchcock
    python tools/docs/import_mapperatorinator_timing.py --rebuild      # 只从归档重算派生层
"""

from __future__ import annotations

import argparse
import json
import shutil
import statistics
import subprocess
from pathlib import Path

MAPPER_OUT = Path(r"D:\fa_tools\mapperatorinator\out")
MAPPER_IN = Path(r"D:\fa_tools\mapperatorinator\in")
REPO = Path(__file__).resolve().parents[2]
TOOL = "import_mapperatorinator_timing.py"
OBSERVATIONS = REPO / "static" / "observations"
ARCHIVE = OBSERVATIONS / "source"


def audio_duration(song: str) -> float | None:
    """音频的真实时长，用来判"输出是不是跑到歌外面去了"。取不到就不判，别猜。"""
    for suffix in (".wav", ".ogg", ".mp3", ".flac"):
        candidate = MAPPER_IN / ("%s_full%s" % (song, suffix))
        if candidate.exists():
            try:
                out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                                      "-of", "default=nw=1:nk=1", str(candidate)],
                                     capture_output=True, text=True, check=True)
                return float(out.stdout.strip())
            except (subprocess.CalledProcessError, ValueError):
                return None
    return None


def read_events_json(path: Path) -> list[dict]:
    rows = json.loads(path.read_text(encoding="utf-8"))
    rows = rows["timingEvents"] if isinstance(rows, dict) else rows
    return [{"type": str(row["type"]), "time": float(row["time"])} for row in rows]


def read_osu_redlines(path: Path) -> tuple[list[dict], list[dict]]:
    """退回方案：只读红线。小节是按"从红线起每 meter 拍"**折算**出来的，会明说。"""
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
            continue
        points.append({"time": time_ms / 1000.0, "beatSeconds": beat_length / 1000.0, "meter": meter})
    points.sort(key=lambda point: point["time"])
    events = []
    for index, point in enumerate(points):
        end = points[index + 1]["time"] if index + 1 < len(points) else None
        events.append({"type": "timing_point", "time": point["time"]})
        time, beat = point["time"], 0
        while end is None or time < end:
            events.append({"type": "measure" if beat % point["meter"] == 0 else "beat", "time": time})
            beat += 1
            time = point["time"] + beat * point["beatSeconds"]
            if end is None and beat > 100000:
                break
    return events, points


def local_beat_seconds(beats: list[float]) -> list[float]:
    """相邻 beat observation 的间隔（§8 的派生量，只作派生，不覆盖模型的 timing point）。"""
    return [round(b - a, 6) for a, b in zip(beats, beats[1:]) if b > a]


def pulse_positions(observations: dict) -> list[float]:
    """模型每个位置只发一个标记：小节头那里发的是 MEASURE，不是 BEAT。

    所以"拍"的位置是 BEAT ∪ MEASURE。这是一个派生读法（写在 derived 里），
    原始 observation 不被改写——不要把它当成模型直接说的东西。
    """
    return sorted(set(observations["beats"]) | set(observations["measures"]))


def find_gaps(measures: list[float], bar_seconds: float, factor: float = 4.0) -> list[dict]:
    """超过 factor 个小节没有 observation 的地方，就是 gap。**绝不插值补满。**"""
    gaps = []
    for a, b in zip(measures, measures[1:]):
        if b - a > factor * bar_seconds:
            gaps.append({"start": a, "end": b, "seconds": round(b - a, 3)})
    return gaps


def valid_ranges(measures: list[float], gaps: list[dict]) -> list[dict]:
    spans, start = [], measures[0] if measures else None
    for gap in gaps:
        spans.append({"start": start, "end": gap["start"]})
        start = gap["end"]
    if start is not None:
        spans.append({"start": start, "end": measures[-1]})
    return spans


def health_of(observations: dict, duration: float | None) -> dict:
    """只看它自己输出得像不像话（§13）。**不因为"和 Beat This 不一致"就 suspect。**

    小事走 `notes`（局部瑕疵，几处不影响别处），结构性的事才走 `reasons`。
    两者都得报出来——用户要的是"每条结论带资格"，不是"一个总体分数"。
    """
    measures = observations["measures"]
    beats = pulse_positions(observations)
    reasons, notes = [], []
    if len(beats) < 8 or len(measures) < 2:
        return {"status": "ineligible", "reasons": ["beat 或 measure observation 太少（%d / %d）"
                                                    % (len(beats), len(measures))], "notes": []}
    steps = local_beat_seconds(beats)
    median_step = statistics.median(steps) if steps else 0.0
    bar = statistics.median([b - a for a, b in zip(measures, measures[1:])]) if len(measures) > 1 else 0.0
    if median_step > 0:
        odd = [step for step in steps if abs(step - median_step) / median_step > 0.5]
        share = len(odd) / len(steps)
        message = ("拍间距异常 %d/%d 处（中位 %.3fs，最小 %.3fs 最大 %.3fs）"
                   % (len(odd), len(steps), median_step, min(steps), max(steps)))
        if odd:
            (reasons if share > 0.10 else notes).append(message)
    if bar > 0:
        gaps = find_gaps(measures, bar)
        if gaps:
            reasons.append("局部缺失 %d 处（共 %.1fs 没有 measure observation）"
                           % (len(gaps), sum(gap["seconds"] for gap in gaps)))
    shifts = [point["time"] for point in observations["timingPoints"]]
    if bar > 0 and len(shifts) > 1:
        dense = [a for a, b in zip(shifts, shifts[1:]) if b - a < bar]
        if dense:
            notes.append("timing point 变化过密 %d 处（相邻两个小于一个小节，最早在 %.1fs）"
                         % (len(dense), dense[0]))
    end = max(measures[-1], beats[-1])
    if duration and end > duration + 0.5:
        reasons.append("输出超出音频范围（最后 %.1fs > 时长 %.1fs）" % (end, duration))
    return {"status": "suspect" if reasons else "usable", "reasons": reasons, "notes": notes}


def observations_for(events: list[dict]) -> dict:
    beats = sorted(event["time"] for event in events if event["type"] == "beat")
    measures = sorted(event["time"] for event in events if event["type"] == "measure")
    points = sorted(({"time": event["time"]} for event in events if event["type"] == "timing_point"),
                    key=lambda point: point["time"])
    return {"beats": beats, "measures": measures, "timingPoints": points}


def redline_points(osu: Path | None) -> list[dict]:
    if osu is None or not osu.exists():
        return []
    _, redlines = read_osu_redlines(osu)
    return [{"time": point["time"], "bpm": round(60.0 / point["beatSeconds"], 4),
             "meter": point["meter"]} for point in redlines if point["time"] > 0]


def with_redline_metadata(observations: dict, redlines: list[dict]) -> dict:
    """给 timing point observation 补上 bpm / meter。

    模型的 TIMING_POINT token 只带时刻；bpm 与 meter 是导出成 .osu 时按拍数推出来的。
    所以这两个字段的来源要写清楚：**红线（导出时推的）**，不是"模型说的"。
    取不到就留空，不补猜测（§7）。
    """
    if not redlines:
        return observations
    observations = dict(observations)
    observations["timingPoints"] = [{"time": point["time"], "bpm": point["bpm"],
                                     "meter": point["meter"]} for point in redlines]
    return observations


def assemble(song: str, events: list[dict], origin: str, produced_by: str,
             redlines: list[dict] | None = None,
             duration: float | None = None) -> dict:
    """一条 observation 集合 → 一份可给 MGMCS 读的文档。派生量与健康度都在这里定。"""
    observations = with_redline_metadata(observations_for(events), redlines or [])
    if redlines:
        origin = origin + "；timing point 的 bpm/meter 来自 .osu 红线（导出时按拍数推的）"
    duration = audio_duration(song) if duration is None else duration
    bar = statistics.median([b - a for a, b in zip(observations["measures"], observations["measures"][1:])]
                            ) if len(observations["measures"]) > 1 else 0.0
    gaps = find_gaps(observations["measures"], bar) if bar else []
    pulse = pulse_positions(observations)
    return {"schemaVersion": "0.1", "song": song, "source": "mapperatorinator",
            "producedBy": produced_by, "observationsFrom": origin,
            "observations": observations,
            "derived": {"pulsePositions": pulse, "localBeatSeconds": local_beat_seconds(pulse),
                        "barSeconds": bar},
            "gaps": gaps, "validRanges": valid_ranges(observations["measures"], gaps),
            "health": health_of(observations, duration),
            "audioDurationSeconds": duration}


def source_record(song: str, events: list[dict], redlines: list[dict], origin: str) -> dict:
    """归档层：重建派生文档所需的**全部输入**。派生层不许依赖归档层以外的东西。"""
    return {"schemaVersion": "0.1", "song": song, "source": "mapperatorinator",
            "observationsFrom": origin, "timingEvents": events, "redlines": redlines,
            "audioDurationSeconds": audio_duration(song)}


def build_song(song: str, out_root: Path, archive: bool) -> dict:
    events_path = out_root / ("%s_timing_events" % song) / "timing_events.json"
    osu_dir = out_root / ("%s_timing" % song)
    osu = (sorted(osu_dir.glob("beatmap*.osu")) or [None])[-1]
    if events_path.exists():
        events, origin = read_events_json(events_path), "timing_events.json（模型自己那串 observation）"
        if archive:
            ARCHIVE.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(events_path, ARCHIVE / ("%s.timing_events.json" % song))
            record = source_record(song, events, redline_points(osu), origin)
            (ARCHIVE / ("%s.mapperatorinator.source.json" % song)).write_text(
                json.dumps(record, ensure_ascii=False, indent=1), encoding="utf-8")
    elif list(osu_dir.glob("beatmap*.osu")):
        events, _ = read_osu_redlines(osu)
        origin = "osu 红线折算（从红线起每 meter 拍算一个小节——这是假设，不是模型报的；只有红线时 measure 是推出来的）"
    else:
        raise FileNotFoundError("没有 %s 的 timing 输出（%s 与 %s 都不存在）"
                                % (song, events_path, osu_dir))
    return assemble(song, events, origin, TOOL, redline_points(osu))


def write_song(song: str, doc: dict, root: Path | None = None) -> Path:
    root = OBSERVATIONS if root is None else root
    root.mkdir(parents=True, exist_ok=True)
    path = root / ("%s.mapperatorinator.json" % song)
    path.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    return path


def rebuild_from_archive(archive_root: Path, derived_root: Path | None = None) -> int:
    count = 0
    for path in sorted(archive_root.glob("*.mapperatorinator.source.json")):
        song = path.name[: -len(".mapperatorinator.source.json")]
        record = json.loads(path.read_text(encoding="utf-8"))
        write_song(song, assemble(song, read_events_json(path), record["observationsFrom"], TOOL,
                                  record.get("redlines") or [],
                                  record.get("audioDurationSeconds")),
                   derived_root)
        count += 1
    return count


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    parser.add_argument("--out-root", default=str(MAPPER_OUT))
    parser.add_argument("--rebuild", action="store_true",
                        help="不碰 Mapperatorinator，只从归档层重算派生层")
    parser.add_argument("--derived-root", default=None,
                        help="派生文档写到哪里（默认 static/observations；验收时写临时目录比字节）")
    parser.add_argument("--refresh", action="store_true",
                        help="同时把 Mapperatorinator 的输出复制进归档层")
    args = parser.parse_args(argv)
    derived_root = Path(args.derived_root) if args.derived_root else None
    if args.rebuild:
        print("从归档重算 %d 首 → %s" % (rebuild_from_archive(ARCHIVE, derived_root),
                                      derived_root or OBSERVATIONS))
        return 0
    songs = args.song or [path.name[: -len(".mapperatorinator.source.json")]
                          for path in ARCHIVE.glob("*.mapperatorinator.source.json")]
    if not songs:
        print("没有指定歌，归档层也是空的。先跑 Mapperatorinator（dump_timing_events=true）。")
        return 1
    for song in songs:
        try:
            doc = build_song(song, Path(args.out_root), args.refresh)
        except FileNotFoundError as error:
            print("%-12s 跳过：%s" % (song, error))
            continue
        path = write_song(song, doc, derived_root)
        observations = doc["observations"]
        print("%-12s beat %4d  measure %4d  timing point %2d  每小节 %.3fs  gap %d  %s%s"
              % (song, len(observations["beats"]), len(observations["measures"]),
                 len(observations["timingPoints"]), doc["derived"]["barSeconds"], len(doc["gaps"]),
                 doc["health"]["status"],
                 ("（" + "；".join(doc["health"]["reasons"]) + "）") if doc["health"]["reasons"] else ""))
        for note in doc["health"].get("notes") or []:
            print("             小事：%s" % note)
        print("             → %s" % path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
