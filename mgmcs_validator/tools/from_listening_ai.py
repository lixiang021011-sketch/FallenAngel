"""Turn listening_ai's per-song records into this validator's input file.

The phone already writes everything needed, in a form meant to be re-read: one
directory per song, `events.jsonl` inside it with one JSON object per line, and
every line carrying the song's name and `songTime` - the position in the song,
which is the only clock the validator knows about.

A calibration is rebuilt from the raw presses rather than copied from the numbers
the phone worked out at the time. Those numbers are still there, and they were
probably right, but the whole reason the raw presses were kept is that a better
reading of them may exist later; a converter that trusts the old conclusion
quietly makes that impossible.

Two clocks, and the difference matters:

* the **capture clock** (`audioTimeMs`) counts real time from when the phone
  started listening. It is precise, and it is what the tempo comes from.
* the **song clock** (`songTime`) is the player's own position. It is the clock
  the validator works in, and it arrives in steps: the phone only sees a new
  position when the player feels like publishing one, so a value can be up to a
  second behind the music.

So the shape of a grid - its tempo and its meter - is measured on the precise
clock, and the grid is then placed on the song's timeline with the offset between
the two clocks, taken from the freshest sample the log contains. How far the song
clock lagged is reported with every song, because that lag is the accuracy limit
of the grid's phase, and pretending otherwise would send the researcher hunting
for a fault in the music.

    python tools/from_listening_ai.py <listening-root-or-one-song-dir> --check
    python tools/from_listening_ai.py <listening-root> --out <dir>

The root is the phone's `Android/data/com.fallenangel.listeningai/files/listening/`
pulled onto this machine. `--check` reports and writes nothing.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import statistics
import sys
from pathlib import Path

SCHEMA_VERSION = "0.1"
DOWNBEAT = "DOWNBEAT"
# A gap this small is two readings of the same press, not two beats.
MIN_BEAT_SECONDS = 0.08
# The phone says this in the readable log when it could not read the player.
NO_PLAYER_MARK = "读不到播放器信息"

# Which lines become events, and what they are called on the timeline. Anything
# not listed is counted in the report instead of being guessed at.
EVENT_TYPES = {
    "attention_query": "手机当场提的问题（列表题）",
    "pause_moment": "听众自己停下来问的那个时刻",
    "query_policy": "本地提问策略决定要问的时刻",
    "style_reading": "本地模型对这段的说法",
    "bar_answer": "听众对小节线的回答",
    "song_change": "听众从这里切走了",
    "song_enter": "这份记录从这里开始（切歌进来 / 返回重听）",
    "seek": "听众在这首歌里跳转（倒带或拖进度）",
    "ui_press": "界面按压（只在 --include-presses 时导出）",
}


# ------------------------------------------------------------------- reading

def read_jsonl(path: Path):
    lines, broken = [], 0
    try:
        text = path.read_text(encoding="utf-8")
    except OSError:
        return lines, broken
    for raw in text.splitlines():
        raw = raw.strip()
        if not raw:
            continue
        try:
            lines.append(json.loads(raw))
        except json.JSONDecodeError:
            broken += 1
    return lines, broken


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return ""


def is_song_directory(path: Path) -> bool:
    """One song's own directory: the machine log and the human one beside it.

    Both, not just the machine log. An earlier version of the phone wrote one
    `events.jsonl` per evening into the root, and that file is not a song; if the
    root is mistaken for one, every real song under it disappears.
    """
    return (path / "events.jsonl").is_file() and (path / "readable.log").is_file()


def song_directories(root: Path):
    """(the song directories under this path, the ones that only look like one)"""
    if is_song_directory(root):
        return [root], []
    everything = sorted({p.parent for p in root.rglob("events.jsonl")})
    return ([p for p in everything if is_song_directory(p)],
            [p for p in everything if not is_song_directory(p)])


# -------------------------------------------------------------- calibrations

def tap_attempts(lines):
    """The ANCHOR_TAP lines, split at every press of the first button.

    A new attempt starts when the phone's own press counter goes back to zero,
    which is what happens when somebody taps the button again after finishing;
    grouping by the grid's session id would glue several attempts of the same
    song into one calibration.
    """
    attempts, current = [], []
    for line in lines:
        if line.get("kind") != "ANCHOR_TAP":
            continue
        index = line.get("tapIndex")
        last = current[-1].get("tapIndex") if current else None
        if current and isinstance(index, int) and isinstance(last, int) and index <= last:
            attempts.append(current)
            current = []
        current.append(line)
    if current:
        attempts.append(current)
    return attempts


def round3(value):
    return round(value, 3)


def capture_seconds(line):
    """The phone's own clock for a line, in seconds, or None."""
    if line.get("kind") in ("ANCHOR_TAP", "PRESS_SNAP", "BAR_ANCHOR"):
        millis = line.get("audioTimeMs")
    elif "targetMusicTime" in line:
        # A ListeningEvent. interactionTime is when the listener stopped; the
        # target is where the analysis thinks they meant, a little earlier.
        seconds = line.get("interactionTime")
        return seconds if isinstance(seconds, (int, float)) else None
    else:
        millis = None
    return millis / 1000.0 if isinstance(millis, (int, float)) else None


def clock_offset(lines):
    """How far the song clock runs behind the capture clock, and the evidence.

    The player's position is only re-published now and then, so a line's
    `songTime` is the last value it published and is never *ahead* of the truth.
    That makes the largest difference seen in a recording the closest thing to the
    true offset: it is the one line where the phone looked just after the player
    spoke. The spread between the largest and the smallest difference is how
    coarse the song clock was, and it is the honest error bar on every grid.
    """
    offsets = []
    for line in lines:
        song = line.get("songTime")
        capture = capture_seconds(line)
        if isinstance(song, (int, float)) and capture is not None:
            offsets.append(song - capture)
    if not offsets:
        return 0.0, 0.0, 0
    values = sorted(offsets)
    if len(values) < 4:
        return values[-1], values[-1] - values[0], len(values)
    # The ninetieth percentile rather than the maximum: one line whose song clock
    # came from somewhere else - a service restart, or a spell where the player
    # could not be read - must not be able to move every grid in the song.
    def at(fraction):
        return values[min(len(values) - 1, int(fraction * len(values)))]

    return at(0.9), at(0.9) - at(0.1), len(values)


def calibration_from_attempt(taps, number, offset):
    """One attempt's presses as one grid, plus what could not be worked out.

    The rules are the phone's own, from `BeatAnchor`, so that the grid the
    researcher checks here is the grid they heard: tempo is the median gap
    between presses, the meter is a *count* of presses between two bar heads, and
    bar 0 beat 0 is the last bar head - the one nearest to where they stopped.
    """
    problems = []
    captures = [capture_seconds(t) for t in taps]
    captures = [c for c in captures if c is not None]
    if len(captures) < 2:
        return None, ["只有 %d 下，算不出拍速" % len(captures)]
    # Unsorted on purpose: consecutive presses are consecutive beats whatever the
    # reported song time did, and pressing the next button a moment later must not
    # be able to reorder them.
    intervals = [b - a for a, b in zip(captures, captures[1:]) if b - a >= MIN_BEAT_SECONDS]
    if not intervals:
        return None, ["每一下都挤在一起，这不是在打拍子"]
    beat_seconds = statistics.median(intervals)
    bpm = 60.0 / beat_seconds

    head_indices = [i for i, t in enumerate(taps) if t.get("button") == DOWNBEAT]
    meter = None
    if len(head_indices) >= 2:
        counts = [b - a for a, b in zip(head_indices, head_indices[1:])]
        counted = statistics.mode(counts)
        if 2 <= counted <= 8:
            meter = counted
    if meter is None:
        meter = 4
        problems.append("没有打满两个「小节头」，拍号按 4/4 写出，要当待确认看")
    if not head_indices:
        problems.append("一下「小节头」都没有，bar 0 只能拿第一下来当，请当作待确认")
        origin = captures[-1]
    else:
        # The listener's last bar head is bar 0 beat 0 - the same rule the phone
        # itself follows when it numbers the bars - placed on the song's own
        # timeline with the offset between the two clocks.
        origin = captures[head_indices[-1]] + offset

    calibration = {
        "calibrationId": "cal_%02d" % number,
        "audioTime": round3(origin),
        "bpm": round3(bpm),
        "beatsPerBar": meter,
        "beatUnit": 4,
        "source": "human",
        "clockOffsetSeconds": round3(offset),
        "note": "由 %d 下按压重算（拍速来自捕获钟）；小节头 %d 下"
                % (len(taps), len(head_indices)),
        "rawTaps": [{
            "tapIndex": t.get("tapIndex"),
            "button": t.get("button"),
            "songTime": t.get("songTime"),
            "captureTime": capture_seconds(t),
            "clientTime": t.get("clientTime"),
            "sessionId": t.get("sessionId"),
        } for t in taps],
    }
    return calibration, problems


# --------------------------------------------------------------------- events

# "00:31.2  [我] 停下来问：…", and with hours when the song is long.
READABLE_LINE = re.compile(r"^\d+(?::\d{2}){1,2}\.\d\s+\[([^\]]+)\]\s+(.*)$")
# Minutues and seconds, or hours and minutes and seconds when the song is long.
# Longest first: "03:37.3" must not be read as three hours thirty-seven.
CLOCK_WITH_HOURS = re.compile(r"\d+:\d{2}:\d{2}\.\d")
CLOCK_MINUTES = re.compile(r"\d{1,2}:\d{2}\.\d")


def seconds_from_clock(text):
    """Read the song clock out of a sentence, or None.

    The phone writes MM:SS.t, and H:MM:SS.t only once a song passes an hour, so
    the two are told apart by how many colons there are - not by a pattern that
    would happily read "03:37.3" as three hours.
    """
    body = text or ""
    match = CLOCK_WITH_HOURS.search(body) or CLOCK_MINUTES.search(body)
    if not match:
        return None
    parts = match.group(0).split(":")
    if len(parts) == 3:
        hours, minutes, rest = parts
        seconds, tenths = rest.split(".")
        return int(hours) * 3600 + int(minutes) * 60 + int(seconds) + int(tenths) / 10.0
    minutes, rest = parts
    seconds, tenths = rest.split(".")
    return int(minutes) * 60 + int(seconds) + int(tenths) / 10.0


def behaviour_from_readable(text):
    """What the listener did, read back out of the sentences.

    Recordings made before the machine log carried behaviour have only these
    lines - "切到下一首", "倒带或跳转…，现在在 00:31.2" - and they are written
    with the song's own clock, so they can be turned back into events without
    guessing. The new machine lines say more (which song, whether it was a
    return); these say what the old files have.
    """
    out = []
    last_known = 0.0
    for raw in (text or "").splitlines():
        match = READABLE_LINE.match(raw.strip())
        if not match:
            continue
        who = match.group(1)
        said = match.group(2)
        if who != "听":
            # Everything else (the model's readings, the tap results) is a
            # moment this song really was at, and it is what a switch gets dated
            # from when its own clock has already moved on.
            at = seconds_from_clock(raw)
            if at is not None:
                last_known = max(last_known, at)
            continue
        at = seconds_from_clock(raw)
        if at is None:
            continue
        if said.startswith("切到下一首"):
            # This sentence is written after the player has moved on, so its own
            # clock reads 00:00.0 - measured, a session with taps up to 3:37
            # dated its switch 00:00.0. The last moment the record knows about is
            # the honest answer to "when did they leave".
            when = max(at, last_known)
            out.append(("song_change", when, {
                "to": "", "note": "从 readable.log 读出来的",
                "timeNote": "时间取自这句之前最后已知的歌曲位置" if when > at else ""}))
        elif said.startswith("倒带或跳转"):
            # The position is the number after 现在在, not the tempo in brackets.
            where = said.find("现在在")
            to = seconds_from_clock(said[where + 3:]) if where >= 0 else None
            when = max(at, last_known)
            out.append(("seek", when, {
                "to": to, "note": "从 readable.log 读出来的",
                "timeNote": "时间取自这句之前最后已知的歌曲位置" if when > at else ""}))
        last_known = max(last_known, at)
    return out

def point_event(event_id, event_type, audio_time, source, payload):
    return {
        "eventId": event_id,
        "audioTime": round3(audio_time),
        "type": event_type,
        "source": source,
        "payload": payload,
    }


def events_from_lines(lines):
    """Everything in the log that happened at a moment in the music."""
    events, unmapped, presses = [], {}, 0
    open_queries = {}   # (templateId, songBar) -> the event waiting for an answer
    policy = {}         # FeedbackRecord id -> the event waiting for an answer
    styles = {}
    counter = 0

    def next_id():
        nonlocal counter
        counter += 1
        return "evt_%03d" % counter

    for line in lines:
        kind = line.get("kind")
        song_time = line.get("songTime")

        if kind == "ANCHOR_TAP":
            continue
        if kind == "BAR_ANCHOR":
            events.append(point_event(next_id(), "bar_answer", song_time, "mobile",
                                      {"choice": line.get("choice"), "meter": line.get("meter"),
                                       "bpm": line.get("bpm")}))
            continue
        if kind == "PRESS_SNAP":
            presses += 1
            continue

        # What the listener did to the music. Older recordings never wrote these
        # as lines, so the readable log is read for them as well (below).
        if kind == "SONG_CHANGE":
            events.append(point_event(next_id(), "song_change", song_time, "mobile",
                                      {"to": line.get("to", "")}))
            continue
        if kind == "SONG_ENTER":
            events.append(point_event(next_id(), "song_enter", song_time, "mobile",
                                      {"from": line.get("from", ""),
                                       "reentry": line.get("reentry", None),
                                       "playsSoFar": line.get("playsSoFar", None)}))
            continue
        if kind == "SEEK":
            events.append(point_event(next_id(), "seek", song_time, "mobile",
                                      {"from": line.get("fromSeconds"),
                                       "to": line.get("toSeconds"),
                                       "backwards": line.get("backwards")}))
            continue

        # A question from the bank: the ask and its answer are two lines.
        if line.get("type") == "QUERY" and line.get("templateId"):
            key = (line.get("templateId"), line.get("songBar"))
            if line.get("state") == "ASKED":
                event = point_event(next_id(), "attention_query", line.get("songSeconds"),
                                    "mobile",
                                    {"templateId": line.get("templateId"),
                                     "targetSignal": line.get("targetSignal"),
                                     "songBar": line.get("songBar"),
                                     "text": line.get("text"),
                                     "state": "asked", "answer": None})
                open_queries[key] = event
                events.append(event)
            else:
                event = open_queries.get(key)
                if event is None:
                    continue
                event["payload"]["state"] = ("answered" if line.get("state") == "ANSWERED"
                                             else "ignored")
                event["payload"]["answer"] = line.get("answer")
            continue

        # The local query policy's own record, if this song has one.
        if kind == "QUERY":
            query_id = line.get("id")
            if line.get("state") == "AI_PROPOSED":
                event = point_event(next_id(), "query_policy", line.get("audioTime"),
                                    "mobile",
                                    {"queryType": line.get("queryType"),
                                     "reasonCode": line.get("reasonCode"),
                                     "targetUncertainty": line.get("targetUncertainty"),
                                     "state": "asked", "answer": None})
                policy[query_id] = event
                events.append(event)
            else:
                event = policy.get(query_id)
                if event is not None:
                    event["payload"]["state"] = "answered"
                    event["payload"]["answer"] = line.get("selectedOption", "")
                    event["payload"]["dismissed"] = line.get("dismissed")
            continue

        if kind == "STYLE":
            style_id = line.get("id")
            if line.get("state") == "AI_PROPOSED":
                event = point_event(next_id(), "style_reading", line.get("audioTime"),
                                    "mobile",
                                    {"style": line.get("style"),
                                     "confidence": line.get("styleConfidence"),
                                     "phrase": line.get("phrase"),
                                     "accepted": None})
                styles[style_id] = event
                events.append(event)
            else:
                event = styles.get(style_id)
                if event is not None:
                    event["payload"]["accepted"] = line.get("accepted")
            continue

        # A ListeningEvent: the moment the listener stopped on, or the app did.
        if "targetMusicTime" in line and "aiVersion" in line:
            events.append(point_event(next_id(), "pause_moment", song_time, "mobile",
                                      {"state": line.get("state"),
                                       "chosen": line.get("chosenLabel"),
                                       "aiVersion": line.get("aiVersion"),
                                       "bar": line.get("bar"),
                                       "beat": line.get("beat")}))
            continue

        name = kind or line.get("type") or "(没有 kind)"
        unmapped[name] = unmapped.get(name, 0) + 1

    return events, unmapped, presses


# ------------------------------------------------------------------ documents

def taps_as_events(calibrations):
    """Every anchor press, as an event, at the position the phone recorded.

    The presses are what the calibration was computed *from*; drawn on the
    timeline they answer the one question the numbers cannot: is the bar line
    the tool drew actually on the beat the person pressed? The position here is
    the phone's own published one, so it carries the publish step's slop - which
    is the honest thing to draw, and the reason a press can look a beat off.
    """
    events = []
    for calibration in calibrations:
        for tap in calibration.get("rawTaps", []):
            events.append(point_event(
                "tap_%s_%02d" % (calibration["calibrationId"], tap.get("tapIndex", 0)),
                "tap_downbeat" if tap.get("button") == "DOWNBEAT" else "tap_beat",
                tap.get("songTime"), "human",
                {"grid": calibration["calibrationId"], "tapIndex": tap.get("tapIndex"),
                 "captureTime": tap.get("captureTime"),
                 "note": "手机记下的位置（播放器公布值）"}))
    return events


def document_for_song(song_dir: Path, include_presses=False, include_taps=False,
                      audio_name=""):
    lines, broken = read_jsonl(song_dir / "events.jsonl")
    readable = read_text(song_dir / "readable.log")
    label = next((l.get("song") for l in lines if l.get("song")), "") or song_dir.name

    offset, spread, samples = clock_offset(lines)
    calibrations, warnings = [], []
    for number, attempt in enumerate(tap_attempts(lines), start=1):
        calibration, problems = calibration_from_attempt(attempt, number, offset)
        if calibration is None:
            warnings.append("第 %d 次对拍没成：%s" % (number, "；".join(problems)))
            continue
        if problems:
            calibration["note"] += "；" + "；".join(problems)
        warnings.extend(problems)
        calibrations.append(calibration)
    if not calibrations:
        warnings.append("这份记录里没有一次能还原成网格的对拍")

    events, unmapped, presses = events_from_lines(lines)
    # Old recordings only told the *sentences* about a switch or a jump, so those
    # are read back. A recording that already has the machine lines says it
    # properly - and it also has the sentences, so using both would count every
    # switch twice.
    told = any(event["type"] in ("song_change", "song_enter", "seek") for event in events)
    if not told:
        for kind, at, payload in behaviour_from_readable(readable):
            events.append(point_event("evt_b%02d" % (len(events) + 1), kind, at, "mobile",
                                      payload))
    if include_presses:
        presses_included = []
        for line in lines:
            if line.get("kind") == "PRESS_SNAP":
                presses_included.append(point_event(
                    "press_%03d" % (len(presses_included) + 1), "ui_press",
                    line.get("songTime"), "mobile",
                    {"what": line.get("what"), "bar": line.get("bar"), "beat": line.get("beat"),
                     "intoBeat": line.get("intoBeat"), "anchoredBpm": line.get("anchoredBpm"),
                     "meter": line.get("meter")}))
        events.extend(presses_included)
    if include_taps:
        events.extend(taps_as_events(calibrations))
    # The validator sorts too, but a file a person may open should already read in
    # the order the music plays.
    events.sort(key=lambda e: e["audioTime"])

    player_readable = NO_PLAYER_MARK not in readable
    if not player_readable:
        warnings.append("这份记录写的时候读不到播放器位置，songTime 是手机自己的钟，"
                        "换算出来的网格整体会有未知偏移")
    elif calibrations and spread > 0.2:
        warnings.append("播放器位置大约每 %.1f 秒才发布一次（本机观测跨度 %.2f 秒），"
                        "网格相位可能整体偏最多约 ±%.2f 秒；拍速不受影响，它来自捕获钟"
                        % (spread, spread, spread / 2))
    if broken:
        warnings.append("%d 行不是合法 JSON，已跳过" % broken)

    document = {
        "schemaVersion": SCHEMA_VERSION,
        "generatedBy": "from_listening_ai.py",
        "song": {
            # The directory, not the title: the same song listened to twice is two
            # recordings with two sets of taps, and they must not share a result.
            "songId": song_dir.name,
            "title": label,
            # The validator finds a song that ships with it by this name, in its
            # own audio/ folder; without it the file has to be dragged in each time.
            "audioFileName": audio_name,
        },
        "calibrations": calibrations,
        "events": events,
    }
    report = {
        "songId": song_dir.name,
        "title": label,
        "directory": str(song_dir),
        "playerTimeReadable": player_readable,
        "clockOffsetSeconds": round3(offset),
        "clockSpreadSeconds": round3(spread),
        "clockSamples": samples,
        "calibrations": calibrations,
        "eventCounts": count_events(events),
        "unmapped": unmapped,
        "pressesNotExported": presses,
        "warnings": warnings,
        "lines": len(lines),
    }
    return document, report


def count_events(events):
    counts = {}
    for event in events:
        counts[event["type"]] = counts.get(event["type"], 0) + 1
    return counts


# ----------------------------------------------------------------------- CLI

def format_report(report, written):
    out = []
    out.append("%s" % report["title"])
    out.append("  目录：%s" % report["directory"])
    out.append("  播放器位置：%s" % ("可读" if report["playerTimeReadable"]
                                     else "读不到（时间基准不可信，见下面的警告）"))
    if report["clockSamples"]:
        out.append("  两个钟：捕获钟比歌曲钟快 %.1f 秒（播放器位置发布跨度 %.2f 秒，%d 个样本）"
                   % (-report["clockOffsetSeconds"], report["clockSpreadSeconds"],
                      report["clockSamples"]))
    out.append("  日志 %d 行，还原出 %d 条校准" % (report["lines"], len(report["calibrations"])))
    for calibration in report["calibrations"]:
        out.append("    %s  %s BPM  %d/4  起点 %ss  （%s）"
                   % (calibration["calibrationId"], calibration["bpm"],
                      calibration["beatsPerBar"], calibration["audioTime"],
                      calibration["note"]))
    if report["eventCounts"]:
        out.append("  事件：%s" % " · ".join(
            "%s %d" % (name, count) for name, count in sorted(report["eventCounts"].items())))
    else:
        out.append("  事件：没有")
    if report["unmapped"]:
        out.append("  没映射的行：%s" % " · ".join(
            "%s %d" % (name, count) for name, count in sorted(report["unmapped"].items())))
    if report["pressesNotExported"]:
        out.append("  （%d 下界面按压没有导出，要的话加 --include-presses）"
                   % report["pressesNotExported"])
    for warning in report["warnings"]:
        out.append("  ⚠ %s" % warning)
    if written:
        out.append("  写出：%s" % written)
    return "\n".join(out)


def output_path(song_dir: Path, out_dir: Path | None):
    stem = song_dir.name
    target = out_dir if out_dir else song_dir
    return target / (stem + ".mgmcs.json")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("path", help="listening 根目录，或某首歌的目录")
    parser.add_argument("--out", help="写到别的目录（默认写在每首歌旁边）")
    parser.add_argument("--check", action="store_true", help="只报告，不写文件")
    parser.add_argument("--include-presses", action="store_true",
                        help="把界面上的每次按压也导出成事件")
    parser.add_argument("--include-taps", action="store_true",
                        help="把对拍时的每一下按压也导出成事件（tap_downbeat / tap_beat）")
    parser.add_argument("--audio-name", default="",
                        help="音频文件名（放在 static/audio/ 里的那个），写进 song.audioFileName")
    args = parser.parse_args(argv)

    root = Path(args.path)
    if not root.exists():
        print("找不到：%s" % root, file=sys.stderr)
        return 2
    directories, not_songs = song_directories(root)
    if not directories:
        print("这里没有 events.jsonl：%s" % root, file=sys.stderr)
        return 2
    for stray in not_songs:
        print("跳过 %s：里面有 events.jsonl，但没有 readable.log，"
              "不是一首歌的记录（像是早先那种整晚写一个文件的格式）。" % stray,
              file=sys.stderr)

    out_dir = Path(args.out) if args.out else None
    if out_dir:
        out_dir.mkdir(parents=True, exist_ok=True)

    failures = 0
    for index, song_dir in enumerate(directories):
        document, report = document_for_song(song_dir, args.include_presses,
                                             args.include_taps, args.audio_name)
        written = ""
        if not args.check:
            target = output_path(song_dir, out_dir)
            target.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n",
                              encoding="utf-8")
            written = str(target)
        print(format_report(report, written))
        print()
        if not report["calibrations"]:
            failures += 1
    if failures:
        print("%d 首歌没有可用校准。" % failures)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
