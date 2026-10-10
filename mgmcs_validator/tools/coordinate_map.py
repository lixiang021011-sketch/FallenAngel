"""Time Coordinate Map Generator v0.1 - the Python side of the same module.

One song, several grids, four calls: seconds → coordinates, coordinates →
seconds, one moment projected onto every grid, and the beat lines a window needs
to be drawn. Nothing here detects tempo, chooses a grid, merges two grids,
rewrites an origin, or judges confidence: a grid is a reading, and the reading is
the input.

The browser has the same four calls (static/mgmcs.js, `coordinateMap`). Two
implementations of one piece of arithmetic is a way to disagree with yourself, so
they are held to the same fixtures - tests/coordinate_fixtures.json - which are
written out from the spec, not from either implementation.

v0.1 assumes one constant tempo per grid. Bars that are not all the same length are
expressed as an explicit bar-line list (`barLines`), which is what a song with a
short bar is: the beats stay evenly spaced, so `barLines` and `bpm` are given
together and neither one means anything without the other. This is the same
arithmetic as the browser's (static/mgmcs.js), held to the same fixtures.

    python tools/coordinate_map.py            # the tests' own smoke run
"""

from __future__ import annotations

import bisect
import json
import math
from pathlib import Path

SCHEMA_VERSION = "0.1"


class Grid:
    """One reading of a song: where its bar 0 begins, and how it counts."""

    __slots__ = ("grid_id", "origin_time", "bpm", "beats_per_bar", "beat_unit", "source",
                 "bar_times")

    def __init__(self, grid_id, origin_time, bpm, beats_per_bar, beat_unit=4, source="",
                 bar_times=None):
        self.grid_id = grid_id
        self.origin_time = origin_time
        self.bpm = bpm
        self.beats_per_bar = beats_per_bar
        self.beat_unit = beat_unit
        self.source = source
        self.bar_times = sorted(float(t) for t in bar_times) if bar_times else None

    @property
    def beat_seconds(self):
        return 60.0 / self.bpm

    @property
    def nominal_bar_seconds(self):
        return self.beats_per_bar * self.beat_seconds

    @property
    def irregular(self):
        """True when the bars are a list rather than a period."""
        return bool(self.bar_times)

    def bar_start(self, bar):
        """When a bar begins. Outside the list the nominal bar is extrapolated.

        A song's tail is ordinary bars; only the seams are listed, and a window that
        reaches past either end still has to be drawable.
        """
        if not self.bar_times:
            return self.origin_time + bar * self.nominal_bar_seconds
        times = self.bar_times
        if bar < 0:
            return times[0] + bar * self.nominal_bar_seconds
        if bar >= len(times):
            return times[-1] + (bar - (len(times) - 1)) * self.nominal_bar_seconds
        return times[bar]

    def bar_index_at(self, t):
        """The last bar line at or before t. May be negative."""
        times = self.bar_times
        found = bisect.bisect_right(times, t + 1e-9) - 1
        if found >= 0:
            last = len(times) - 1
            if found == last and t > times[last]:
                return last + math.floor((t - times[last]) / self.nominal_bar_seconds)
            return found
        return math.floor((t - times[0]) / self.nominal_bar_seconds)

    def bar_beats(self, bar):
        """How many beats a bar holds. Not constant when the grid has a bar list."""
        return (self.bar_start(bar + 1) - self.bar_start(bar)) / self.beat_seconds

    def as_dict(self):
        out = {
            "gridId": self.grid_id,
            "originTime": self.origin_time,
            "bpm": self.bpm,
            "beatsPerBar": self.beats_per_bar,
            "beatUnit": self.beat_unit,
        }
        if self.bar_times:
            out["barLines"] = list(self.bar_times)
        return out


class Song:
    """The document the map works on: metadata plus every grid, unmerged."""

    def __init__(self, song_id="", duration=float("nan"), grids=None, problems=None):
        self.song_id = song_id
        self.duration = duration
        self.grids = list(grids or [])
        self.problems = list(problems or [])


def load_song(raw) -> Song:
    """Read either shape: this project's `calibrations`, or the spec's `grids`."""
    problems = []
    song = raw.get("song") if isinstance(raw.get("song"), dict) else {}
    song_id = str(raw.get("songId") or song.get("songId") or "")
    duration = raw.get("duration")
    if duration is None:
        duration = song.get("durationSeconds")
    duration = float(duration) if isinstance(duration, (int, float)) and not isinstance(duration, bool) else float("nan")

    entries = raw.get("grids")
    if not isinstance(entries, list):
        entries = raw.get("calibrations") if isinstance(raw.get("calibrations"), list) else []

    grids = []
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict):
            problems.append("有一条网格不是对象，跳过")
            continue
        grid_id = str(entry.get("gridId") or entry.get("calibrationId") or "")
        bar_times = _bar_times(entry.get("barLines"))
        origin = bar_times[0] if bar_times else entry.get("originTime", entry.get("audioTime"))
        bpm = entry.get("bpm")
        bars = entry.get("beatsPerBar", entry.get("meter"))
        if "barLines" in entry and bar_times is None:
            problems.append("%s：barLines 少于两根或不是数字，跳过" % grid_id)
            continue
        if not grid_id or grid_id in seen:
            problems.append("网格 id 缺失或重复：%r，跳过" % grid_id)
            continue
        if not isinstance(origin, (int, float)) or isinstance(origin, bool) or not math.isfinite(origin):
            problems.append("%s：originTime 不是数字，跳过" % grid_id)
            continue
        if not isinstance(bpm, (int, float)) or isinstance(bpm, bool) or not bpm > 0:
            problems.append("%s：bpm 不是正数，跳过" % grid_id)
            continue
        if not isinstance(bars, (int, float)) or isinstance(bars, bool) or not bars > 0:
            problems.append("%s：beatsPerBar 不是正数，跳过" % grid_id)
            continue
        seen.add(grid_id)
        grids.append(Grid(grid_id, float(origin), float(bpm), int(bars),
                          int(entry.get("beatUnit", 4) or 4), str(entry.get("source", "")),
                          bar_times))
    return Song(song_id, duration, grids, problems)


def _bar_times(raw):
    """The bar lines a document writes out by hand, when it has them.

    A grid is normally a period plus a meter, and every bar is the same length. A
    song can have a bar that is not, and that cannot be written as a period, so it
    is written as a list. The list is a reading: it is drawn and judged, not
    inferred.
    """
    if not isinstance(raw, list):
        return None
    times = []
    for item in raw:
        value = item.get("time") if isinstance(item, dict) else item
        if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
            times.append(float(value))
    times.sort()
    return times if len(times) >= 2 else None


class CoordinateMap:
    """The four calls. Every grid stays independent; none of them is chosen."""

    def __init__(self, song: Song):
        self.song = song
        self._by_id = {grid.grid_id: grid for grid in song.grids}

    def grid_ids(self):
        return [grid.grid_id for grid in self.song.grids]

    def _grid(self, grid_id):
        grid = self._by_id.get(str(grid_id))
        if grid is None:
            return {"error": "没有这条网格：%s" % grid_id}
        if not grid.bpm > 0:
            return {"error": "%s：bpm 不是正数" % grid_id}
        if not grid.beats_per_bar > 0:
            return {"error": "%s：beatsPerBar 不是正数" % grid_id}
        return grid

    def _with_range(self, out, audio_time):
        if math.isfinite(self.song.duration) and (audio_time < 0 or audio_time > self.song.duration):
            out["outOfAudioRange"] = True
        return out

    def time_to_coordinate(self, audio_time, grid_id):
        if not _is_number(audio_time):
            return {"error": "audioTime 不是数字"}
        grid = self._grid(grid_id)
        if isinstance(grid, dict):
            return grid
        bar, beat = _coordinate_of(grid, audio_time)
        return self._with_range(
            {"gridId": grid.grid_id, "audioTime": audio_time, "bar": bar, "beat": beat},
            audio_time)

    def coordinate_to_time(self, bar, beat, grid_id):
        if not _is_number(bar) or not _is_number(beat):
            return {"error": "bar / beat 不是数字"}
        grid = self._grid(grid_id)
        if isinstance(grid, dict):
            return grid
        audio_time = _time_of(grid, bar, beat)
        return self._with_range(
            {"gridId": grid.grid_id, "bar": bar, "beat": beat, "audioTime": audio_time},
            audio_time)

    def project_time(self, audio_time):
        if not _is_number(audio_time):
            return {"error": "audioTime 不是数字"}
        coordinates = []
        for grid in self.song.grids:
            bar, beat = _coordinate_of(grid, audio_time)
            coordinates.append({
                "gridId": grid.grid_id, "bar": bar, "beat": beat,
            })
        return self._with_range({"audioTime": audio_time, "coordinates": coordinates},
                                audio_time)

    def generate_anchors(self, grid_id, start_time, end_time):
        grid = self._grid(grid_id)
        if isinstance(grid, dict):
            return grid
        if not _is_number(start_time) or not _is_number(end_time) or end_time < start_time:
            return {"error": "时间窗口不合法"}
        step = grid.beat_seconds
        anchors = []
        if grid.irregular:
            # Bar by bar, so a short bar hands out the beats it really has.
            first = grid.bar_index_at(start_time)
            last = grid.bar_index_at(end_time)
            for bar in range(first, last + 1):
                beats = max(1, int(round(grid.bar_beats(bar))))
                for beat in range(beats):
                    at = grid.bar_start(bar) + beat * step
                    if at < start_time - 1e-9 or at > end_time + 1e-9:
                        continue
                    anchors.append({"audioTime": at, "bar": bar, "beat": float(beat),
                                    "isDownbeat": beat == 0})
            return anchors
        first = math.ceil((start_time - grid.origin_time) / step - 1e-9)
        last = math.floor((end_time - grid.origin_time) / step + 1e-9)
        for q in range(first, last + 1):
            bar = math.floor(q / grid.beats_per_bar)
            beat = q - bar * grid.beats_per_bar
            anchors.append({
                "audioTime": grid.origin_time + q * step,
                "bar": bar, "beat": beat, "isDownbeat": beat == 0,
            })
        return anchors


def _coordinate_of(grid, audio_time):
    """(bar, beat) for one moment. A bar line list reads bar by bar."""
    if grid.irregular:
        bar = grid.bar_index_at(audio_time)
        return bar, (audio_time - grid.bar_start(bar)) / grid.beat_seconds
    q = (audio_time - grid.origin_time) * grid.bpm / 60.0
    bar = math.floor(q / grid.beats_per_bar)
    return bar, q - bar * grid.beats_per_bar


def _time_of(grid, bar, beat):
    """The moment a coordinate names."""
    if grid.irregular:
        return grid.bar_start(bar) + beat * grid.beat_seconds
    return grid.origin_time + (bar * grid.beats_per_bar + beat) * 60.0 / grid.bpm


def _is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def map_from_file(path) -> CoordinateMap:
    raw = json.loads(Path(path).read_text(encoding="utf-8"))
    return CoordinateMap(load_song(raw))


def coordinate_map_document(song: Song, anchors=None):
    """What `generate_map.py` writes: parameters and, optionally, anchors.

    No table of seconds: the mapping is a line, `originTime + q * 60 / bpm`, and
    writing it out per millisecond would be writing the same three numbers a
    hundred thousand times.
    """
    out = {
        "schemaVersion": SCHEMA_VERSION,
        "generatedBy": "coordinate_map.py",
        "songId": song.song_id,
        "duration": song.duration if math.isfinite(song.duration) else None,
        "grids": [grid.as_dict() for grid in song.grids],
        "note": ("每个 grid 独立；坐标实时计算。t = originTime + (bar × beatsPerBar + beat) × 60 / bpm，"
                 "反向 bar = floor(q / beatsPerBar)，beat = q - bar × beatsPerBar。"),
    }
    if song.problems:
        out["problems"] = song.problems
    if anchors:
        out["anchors"] = anchors
    return out


if __name__ == "__main__":
    # A smoke run so the file can be executed on its own, with no arguments.
    document = load_song({
        "songId": "song_001", "duration": 210.35,
        "grids": [
            {"gridId": "grid_A", "originTime": 30.0, "bpm": 120.0, "beatsPerBar": 4, "beatUnit": 4},
            {"gridId": "grid_B", "originTime": 63.211, "bpm": 120.0, "beatsPerBar": 3, "beatUnit": 4},
        ],
    })
    cmap = CoordinateMap(document)
    for t in (30, 28, 35, 72.5):
        print("%8.3f s → %s" % (t, cmap.time_to_coordinate(t, "grid_A")))
    print("bar 2 beat 2 →", cmap.coordinate_to_time(2, 2, "grid_A"))
    print("72.5 投影 →", cmap.project_time(72.5)["coordinates"])
    print("29–32 s 的线 →", len(cmap.generate_anchors("grid_A", 29, 32)), "条")
