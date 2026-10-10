"""The converter, on logs shaped the way the phone actually writes them.

The fixtures here are built by hand rather than copied from a real recording, so
each test can say which part of the phone's format it is relying on - the
`tapIndex` restart that separates two attempts, `songTime` being the song's clock
while `audioTimeMs` is the phone's own, and the ask/answer pair that one question
produces.

    python -m unittest discover -s tests -p "test_*.py"
"""

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))

import from_listening_ai as conv  # noqa: E402

LABEL = "YOASOBI — 大正浪漫"


def tap(index, song_time, button="BEAT", capture=None):
    """One press. `capture` is the phone's own clock, which defaults to agreeing
    with the song's; the tests that care about the two clocks set it differently."""
    return {
        "kind": "ANCHOR_TAP",
        "tapIndex": index,
        "button": button,
        "audioTimeMs": int(round((song_time if capture is None else capture) * 1000)),
        "clientTime": 1700000000000 + index,
        "sessionId": 7,
        "song": LABEL,
        "songTime": song_time,
    }


def write_song(root, name, lines, readable="# 开始\n"):
    song_dir = Path(root) / name
    song_dir.mkdir(parents=True)
    text = "\n".join(json.dumps(line, ensure_ascii=False) for line in lines) + "\n"
    (song_dir / "events.jsonl").write_text(text, encoding="utf-8")
    (song_dir / "readable.log").write_text(readable, encoding="utf-8")
    return song_dir


def one_attempt(offset=10.0):
    """Two bar heads four beats apart at 120 BPM: a clean 4/4 calibration."""
    return [
        tap(0, offset, "DOWNBEAT"),
        tap(1, offset + 0.5),
        tap(2, offset + 1.0),
        tap(3, offset + 1.5),
        tap(4, offset + 2.0, "DOWNBEAT"),
        tap(5, offset + 2.5),
    ]


class CalibrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def convert(self, name="20261009 120000 song", lines=None, readable="# 开始\n"):
        song_dir = write_song(self.root, name, lines if lines is not None else one_attempt(),
                              readable)
        return conv.document_for_song(song_dir)

    def test_the_presses_are_read_back_into_a_grid(self):
        document, report = self.convert()
        self.assertEqual(len(document["calibrations"]), 1)
        calibration = document["calibrations"][0]
        self.assertAlmostEqual(calibration["bpm"], 120.0, places=3)
        self.assertEqual(calibration["beatsPerBar"], 4)
        # bar 0 beat 0 is the last bar head the listener pressed, not the first.
        self.assertAlmostEqual(calibration["audioTime"], 12.0, places=3)
        self.assertEqual(calibration["source"], "human")
        self.assertEqual(report["calibrations"][0]["calibrationId"], "cal_01")

    def test_the_raw_presses_are_kept_beside_the_numbers(self):
        document, _ = self.convert()
        taps = document["calibrations"][0]["rawTaps"]
        self.assertEqual(len(taps), 6)
        self.assertEqual([t["button"] for t in taps].count("DOWNBEAT"), 2)
        self.assertEqual(taps[0]["tapIndex"], 0)
        self.assertEqual(taps[0]["sessionId"], 7)
        # Both clocks travel with the press, so a later reading can redo the
        # conversion without the phone.
        self.assertAlmostEqual(taps[0]["songTime"], 10.0, places=3)
        self.assertAlmostEqual(taps[0]["captureTime"], 10.0, places=3)

    def test_the_tempo_comes_from_the_capture_clock_not_the_stale_song_clock(self):
        # What a real recording looks like: the player publishes its position
        # about once a second, so songTime steps in ~1 s jumps and repeats, while
        # the phone's own clock runs smoothly at 0.7 s a beat (about 86 BPM).
        # Reading the tempo off songTime would call this 60 BPM.
        lines = [
            tap(0, 10.0, "DOWNBEAT", capture=10.9),
            tap(1, 10.0, "BEAT", capture=11.6),
            tap(2, 11.0, "BEAT", capture=12.3),
            tap(3, 12.0, "BEAT", capture=13.0),
            tap(4, 12.0, "DOWNBEAT", capture=13.7),
            tap(5, 13.0, "BEAT", capture=14.4),
            tap(6, 14.0, "BEAT", capture=15.1),
            tap(7, 14.0, "BEAT", capture=15.8),
            tap(8, 14.0, "DOWNBEAT", capture=16.5),
        ]
        document, _ = self.convert(lines=lines)
        calibration = document["calibrations"][0]
        self.assertAlmostEqual(calibration["bpm"], 60 / 0.7, places=2)
        self.assertEqual(calibration["beatsPerBar"], 4)
        # The freshest line (10.0 reported at capture 10.9) puts the song clock
        # 0.9 s behind the capture clock, so the last bar head lands at 16.5-0.9.
        self.assertAlmostEqual(calibration["audioTime"], 15.6, places=2)

    def test_a_steppy_song_clock_is_reported_as_the_error_it_is(self):
        _, report = self.convert(lines=[
            tap(0, 10.0, "DOWNBEAT", capture=10.9),
            tap(1, 10.0, "BEAT", capture=11.6),
            tap(2, 11.0, "BEAT", capture=12.3),
            tap(3, 12.0, "BEAT", capture=13.0),
            tap(4, 12.0, "DOWNBEAT", capture=13.7),
        ])
        self.assertGreater(report["clockSpreadSeconds"], 0.2)
        self.assertTrue(any("发布" in w for w in report["warnings"]))

    def test_pressing_the_button_again_is_a_second_grid(self):
        lines = one_attempt(10.0) + one_attempt(30.0)
        document, _ = self.convert(lines=lines)
        self.assertEqual([c["calibrationId"] for c in document["calibrations"]],
                         ["cal_01", "cal_02"])
        self.assertAlmostEqual(document["calibrations"][0]["audioTime"], 12.0, places=3)
        self.assertAlmostEqual(document["calibrations"][1]["audioTime"], 32.0, places=3)

    def test_one_lone_press_is_not_a_calibration(self):
        document, report = self.convert(lines=[tap(0, 5.0, "DOWNBEAT")])
        self.assertEqual(document["calibrations"], [])
        self.assertTrue(any("算不出拍速" in w for w in report["warnings"]))
        self.assertFalse(report["calibrations"])

    def test_half_a_bar_of_taps_is_flagged_rather_than_assumed(self):
        lines = [tap(0, 10.0, "DOWNBEAT"), tap(1, 10.5), tap(2, 11.0)]
        document, report = self.convert(lines=lines)
        calibration = document["calibrations"][0]
        self.assertEqual(calibration["beatsPerBar"], 4)
        self.assertIn("待确认", calibration["note"])
        self.assertTrue(any("小节头" in w for w in report["warnings"]))

    def test_a_three_four_bar_is_counted_from_the_taps(self):
        lines = [tap(0, 0.0, "DOWNBEAT"), tap(1, 0.5), tap(2, 1.0),
                 tap(3, 1.5, "DOWNBEAT"), tap(4, 2.0), tap(5, 2.5),
                 tap(6, 3.0, "DOWNBEAT")]
        document, _ = self.convert(lines=lines)
        self.assertEqual(document["calibrations"][0]["beatsPerBar"], 3)
        self.assertAlmostEqual(document["calibrations"][0]["audioTime"], 3.0, places=3)

    def test_the_song_id_is_the_recording_not_the_song(self):
        document, _ = self.convert(name="20261009 120000 song")
        self.assertEqual(document["song"]["songId"], "20261009 120000 song")
        self.assertEqual(document["song"]["title"], LABEL)

    def test_a_recording_that_could_not_see_the_player_is_flagged(self):
        _, report = self.convert(readable="# 开始\n[听] 读不到播放器信息：曲目名和切歌都靠它\n")
        self.assertFalse(report["playerTimeReadable"])
        self.assertTrue(any("捕获时钟" in w or "偏移" in w for w in report["warnings"]))


class EventTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def convert(self, extra):
        song_dir = write_song(self.root, "song", one_attempt() + extra)
        return conv.document_for_song(song_dir)[0]

    def test_a_question_and_its_answer_become_one_point(self):
        document = self.convert([
            {"type": "QUERY", "state": "ASKED", "templateId": "ATTN_01",
             "targetSignal": "perceived_salience", "songBar": 12, "songSeconds": 20.125,
             "text": "刚才最吸引你注意的是谁？", "song": LABEL, "songTime": 20.125},
            {"type": "QUERY", "state": "ANSWERED", "templateId": "ATTN_01",
             "targetSignal": "perceived_salience", "songBar": 12, "songSeconds": 20.125,
             "answer": "鼓", "song": LABEL, "songTime": 24.0},
        ])
        self.assertEqual(len(document["events"]), 1)
        event = document["events"][0]
        self.assertEqual(event["type"], "attention_query")
        self.assertAlmostEqual(event["audioTime"], 20.125, places=3)
        self.assertEqual(event["payload"]["answer"], "鼓")
        self.assertEqual(event["payload"]["state"], "answered")
        self.assertEqual(event["payload"]["songBar"], 12)

    def test_a_question_nobody_answered_says_so(self):
        document = self.convert([
            {"type": "QUERY", "state": "ASKED", "templateId": "ROLE_01",
             "songBar": 4, "songSeconds": 9.5, "song": LABEL, "songTime": 9.5},
            {"type": "QUERY", "state": "IGNORED", "templateId": "ROLE_01",
             "songBar": 4, "song": LABEL, "songTime": 30.0},
        ])
        self.assertEqual(document["events"][0]["payload"]["state"], "ignored")
        self.assertIsNone(document["events"][0]["payload"]["answer"])

    def test_a_pause_moment_uses_the_song_clock_not_the_capture_clock(self):
        document = self.convert([
            {"id": "q3-ab", "aiVersion": "local:novelty", "state": "AI_PROPOSED",
             "interactionTime": 4001.2, "targetMusicTime": 4000.9,
             "bar": 3, "beat": 2, "chosenLabel": "", "chosenId": "",
             "song": LABEL, "songTime": 41.5},
        ])
        moment = [e for e in document["events"] if e["type"] == "pause_moment"][0]
        self.assertAlmostEqual(moment["audioTime"], 41.5, places=3)

    def test_the_human_answer_about_the_bar_line_is_kept(self):
        document = self.convert([
            {"kind": "BAR_ANCHOR", "choice": "bar_starts", "bpm": 120.0,
             "meter": 4, "sessionId": 7, "song": LABEL, "songTime": 33.25},
        ])
        event = [e for e in document["events"] if e["type"] == "bar_answer"][0]
        self.assertEqual(event["payload"]["choice"], "bar_starts")
        self.assertAlmostEqual(event["audioTime"], 33.25, places=3)

    def test_presses_are_counted_and_can_be_asked_for(self):
        song_dir = write_song(self.root, "withpress", one_attempt() + [
            {"kind": "PRESS_SNAP", "what": "question_answer", "bar": 5, "beat": 3,
             "intoBeat": 0.25, "anchoredBpm": 120.0, "meter": 4,
             "song": LABEL, "songTime": 15.75},
        ])
        document, report = conv.document_for_song(song_dir)
        self.assertEqual(report["pressesNotExported"], 1)
        self.assertNotIn("ui_press", [e["type"] for e in document["events"]])

        document, report = conv.document_for_song(song_dir, include_presses=True)
        self.assertEqual(report["pressesNotExported"], 1)
        pressed = [e for e in document["events"] if e["type"] == "ui_press"][0]
        self.assertAlmostEqual(pressed["audioTime"], 15.75, places=3)

    def test_lines_nobody_mapped_are_counted_rather_than_dropped_quietly(self):
        _, report = conv.document_for_song(write_song(
            self.root, "unmapped", one_attempt() + [{"kind": "SOMETHING_NEW", "songTime": 1.0}]))
        self.assertEqual(report["unmapped"].get("SOMETHING_NEW"), 1)

    def test_events_come_out_in_time_order(self):
        document = self.convert([
            {"kind": "BAR_ANCHOR", "choice": "lost_the_beat", "song": LABEL, "songTime": 30.0},
            {"kind": "BAR_ANCHOR", "choice": "shift_one", "song": LABEL, "songTime": 12.0},
        ])
        times = [e["audioTime"] for e in document["events"]]
        self.assertEqual(times, sorted(times))


class BehaviourTests(unittest.TestCase):
    """What the listener did to the music, which no grid can show."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def convert(self, extra_lines=None, readable="# 开始\n"):
        song_dir = write_song(self.root, "song", one_attempt() + (extra_lines or []), readable)
        return conv.document_for_song(song_dir)[0]

    def test_the_machine_lines_become_events(self):
        document = self.convert(extra_lines=[
            {"kind": "SONG_ENTER", "from": "previous song", "reentry": True,
             "playsSoFar": 2, "songTime": 0.0},
            {"kind": "SEEK", "fromSeconds": 120.0, "toSeconds": 12.0, "backwards": True,
             "songTime": 120.0},
            {"kind": "SONG_CHANGE", "to": "next song", "songTime": 205.0},
        ])
        kinds = [e["type"] for e in document["events"]]
        self.assertEqual(kinds, ["song_enter", "seek", "song_change"])
        enter = document["events"][0]
        self.assertTrue(enter["payload"]["reentry"])
        self.assertEqual(enter["payload"]["playsSoFar"], 2)
        seek = document["events"][1]
        self.assertEqual(seek["payload"]["to"], 12.0)
        self.assertTrue(seek["payload"]["backwards"])
        self.assertEqual(document["events"][2]["payload"]["to"], "next song")

    def test_an_old_recording_gives_up_its_behaviour_from_the_sentences(self):
        # What the phone wrote before the machine log carried behaviour: only the
        # readable log knows they switched, and its clock for that line has
        # already jumped to the next song.
        readable = (
            "# 歌 · 开始 22:41:01\n"
            "00:01.7  [我] 开始对拍：小节头按第一个按钮\n"
            "03:25.0  [我] 对拍完成：16 下，每下 567ms（105.8 BPM）\n"
            "03:37.3  [模型] 有点像摇滚，和刚才比很稳定\n"
            "00:00.0  [听] 倒带或跳转（保留速度假设 106，重建相位），现在在 00:00.0\n"
            "00:00.0  [听] 切到下一首\n")
        document = self.convert(readable=readable)
        kinds = sorted(e["type"] for e in document["events"])
        self.assertEqual(kinds, ["seek", "song_change"])
        # Not at 00:00.0: the last moment the record knows about is 3:37.3.
        for event in document["events"]:
            self.assertAlmostEqual(event["audioTime"], 217.3, places=1)
            self.assertIn("最后已知", event["payload"]["timeNote"])

    def test_behaviour_written_by_the_machine_is_not_added_again_from_the_sentences(self):
        readable = "00:20.0  [听] 切到下一首\n"
        document = self.convert(
            extra_lines=[{"kind": "SONG_CHANGE", "to": "x", "songTime": 20.0}],
            readable=readable)
        self.assertEqual([e["type"] for e in document["events"]], ["song_change"])

    def test_a_recording_that_says_it_properly_ignores_the_sentences_entirely(self):
        # Measured on a real session: the phone wrote SONG_ENTER/SEEK/SONG_CHANGE
        # *and* the sentences, and every switch came out twice.
        readable = (
            "00:00.0  [听] 倒带或跳转（保留速度假设 120，重建相位），现在在 00:00.0\n"
            "00:19.0  [听] 切到下一首\n")
        document = self.convert(
            extra_lines=[
                {"kind": "SONG_ENTER", "from": "", "reentry": False, "songTime": 0.0},
                {"kind": "SEEK", "fromSeconds": 0.0, "toSeconds": 0.0, "songTime": 0.0},
                {"kind": "SONG_CHANGE", "to": "next", "songTime": 19.0},
            ],
            readable=readable)
        self.assertEqual([e["type"] for e in document["events"]],
                         ["song_enter", "seek", "song_change"])

    def test_a_jump_reads_the_position_it_went_to(self):
        document = self.convert(readable="01:30.0  [听] 倒带或跳转（保留速度假设 106.5，重建相位），现在在 00:12.0\n")
        seek = document["events"][0]
        self.assertEqual(seek["type"], "seek")
        self.assertAlmostEqual(seek["payload"]["to"], 12.0, places=3)
        # The tempo in brackets is not a clock.
        self.assertAlmostEqual(seek["audioTime"], 90.0, places=3)

    def test_the_clock_is_read_as_minutes_unless_it_has_hours(self):
        self.assertAlmostEqual(conv.seconds_from_clock("03:37.3"), 217.3, places=3)
        self.assertAlmostEqual(conv.seconds_from_clock("00:19.1"), 19.1, places=3)
        self.assertAlmostEqual(conv.seconds_from_clock("1:02:03.4"), 3723.4, places=3)
        self.assertIsNone(conv.seconds_from_clock("没有时间"))


class CommandLineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_check_reports_and_writes_nothing(self):
        song_dir = write_song(self.root, "song", one_attempt())
        code = conv.main([str(self.root), "--check"])
        self.assertEqual(code, 0)
        self.assertEqual(list(song_dir.glob("*.mgmcs.json")), [])

    def test_writing_puts_the_file_next_to_the_recording(self):
        song_dir = write_song(self.root, "song", one_attempt())
        code = conv.main([str(self.root)])
        self.assertEqual(code, 0)
        written = song_dir / (song_dir.name + ".mgmcs.json")
        self.assertTrue(written.is_file())
        document = json.loads(written.read_text(encoding="utf-8"))
        self.assertEqual(document["schemaVersion"], "0.1")
        self.assertEqual(len(document["calibrations"]), 1)

    def test_the_written_document_has_what_the_validator_needs(self):
        song_dir = write_song(self.root, "song", one_attempt() + [
            {"kind": "BAR_ANCHOR", "choice": "bar_starts", "song": LABEL, "songTime": 20.0},
        ])
        conv.main([str(song_dir)])
        document = json.loads(
            (song_dir / (song_dir.name + ".mgmcs.json")).read_text(encoding="utf-8"))
        for grid in document["calibrations"]:
            for field in ("calibrationId", "audioTime", "bpm", "beatsPerBar"):
                self.assertIn(field, grid)
            self.assertGreater(grid["bpm"], 0)
            self.assertGreaterEqual(grid["beatsPerBar"], 1)
        for event in document["events"]:
            self.assertTrue("audioTime" in event or
                            ("startTime" in event and "endTime" in event))


if __name__ == "__main__":
    unittest.main()
