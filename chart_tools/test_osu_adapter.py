# -*- coding: utf-8 -*-
"""osu_adapter 自测（unittest，无第三方依赖）。

python -X utf8 -m unittest chart_tools.test_osu_adapter -v
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import osu_adapter as A  # noqa: E402

# 4K 列中心：x = 64 / 192 / 320 / 448
HEADER = """osu file format v14

[General]
AudioFilename: song.mp3
Mode: 3

[Metadata]
Title:Test Song
TitleUnicode:测试曲
Artist:Someone
Creator:Mapperatorinator
Version:Bass

[Difficulty]
CircleSize:4

[TimingPoints]
{timing}

[HitObjects]
{objects}
"""

TIMING_120 = "500,500,4,2,0,100,1,0"           # 第一拍 0.5s，120 BPM


def osu(objects, timing=TIMING_120):
    return HEADER.format(timing=timing, objects="\n".join(objects))


def run(text, **kw):
    kw.setdefault("part", "Bass")
    return A.convert(A.parse_osu(text), **kw)


class Lanes(unittest.TestCase):
    def test_four_columns_map_to_0_to_3(self):
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:", "192,192,1000,1,0,0:0:0:0:",
                              "320,192,1000,1,0,0:0:0:0:", "448,192,1000,1,0,0:0:0:0:"]))
        self.assertTrue(rep.ok, rep.to_text())
        self.assertEqual([n["lane"] for n in chart["notes"]], [0, 1, 2, 3])
        self.assertEqual(chart["metadata"]["laneCount"], 4)

    def test_column_edges(self):
        # 列边界：0–127 → 0，128–255 → 1，256–383 → 2，384–511 → 3
        xs = [0, 127, 128, 255, 256, 383, 384, 511]
        objs = ["%d,192,%d,1,0,0:0:0:0:" % (x, 1000 + i * 100) for i, x in enumerate(xs)]
        chart, _ = run(osu(objs))
        self.assertEqual([n["lane"] for n in chart["notes"]], [0, 0, 1, 1, 2, 2, 3, 3])

    def test_out_of_range_x_clamped_and_reported(self):
        chart, rep = run(osu(["600,192,1000,1,0,0:0:0:0:"]))
        self.assertEqual(chart["notes"][0]["lane"], 3)
        self.assertTrue(any("超出" in w for w in rep.warnings))

    def test_no_instrument_remap(self):
        a, _ = run(osu(["64,192,1000,1,0,0:0:0:0:"]), part="Drums")
        b, _ = run(osu(["64,192,1000,1,0,0:0:0:0:"]), part="Guitar")
        self.assertEqual(a["notes"], b["notes"])


class Timing(unittest.TestCase):
    def test_times_are_audio_seconds_and_offset_zero(self):
        chart, _ = run(osu(["64,192,1234,1,0,0:0:0:0:"]))
        self.assertEqual(chart["notes"][0]["time"], 1.234)
        self.assertEqual(chart["metadata"]["offset"], 0.0)

    def test_bpm_and_beat_offset(self):
        chart, _ = run(osu(["64,192,1000,1,0,0:0:0:0:"]))
        m = chart["metadata"]
        self.assertEqual(m["bpm"], 120.0)
        self.assertEqual(m["sourceMetadata"]["beatOffset"], 0.5)

    def test_inherited_points_ignored(self):
        timing = TIMING_120 + "\n2000,-50,4,2,0,100,0,0"
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:"], timing=timing))
        self.assertEqual(chart["events"], [])
        self.assertEqual(len(chart["metadata"]["sourceMetadata"]["timingPoints"]), 1)

    def test_bpm_change_goes_to_events(self):
        timing = TIMING_120 + "\n10500,400,4,2,0,100,1,0"
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:"], timing=timing))
        self.assertEqual(chart["events"], [{"time": 10.5, "type": "bpm", "value": 150.0}])
        self.assertTrue(any("变速" in w for w in rep.warnings))

    def test_meter_recorded(self):
        chart, _ = run(osu(["64,192,1000,1,0,0:0:0:0:"], timing="500,500,3,2,0,100,1,0"))
        self.assertEqual(chart["metadata"]["sourceMetadata"]["timingPoints"][0]["meter"], 3)

    def test_no_timing_is_fatal(self):
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:"], timing=""))
        self.assertIsNone(chart)
        self.assertFalse(rep.ok)


class Holds(unittest.TestCase):
    def test_hold_time_and_duration(self):
        chart, rep = run(osu(["192,192,1000,128,0,1750:0:0:0:0:"]))
        n = chart["notes"][0]
        self.assertEqual((n["type"], n["lane"], n["time"], n["duration"]), ("hold", 1, 1.0, 0.75))
        self.assertEqual(rep.counts, {"tap": 0, "hold": 1})

    def test_zero_and_negative_hold_removed_and_reported(self):
        chart, rep = run(osu(["64,192,1000,128,0,1000:0:0:0:0:",
                              "64,192,2000,128,0,1500:0:0:0:0:",
                              "64,192,3000,1,0,0:0:0:0:"]))
        self.assertEqual(len(chart["notes"]), 1)
        self.assertEqual(rep.removed["invalid_hold"], 2)
        self.assertIn("2 invalid holds removed", rep.to_text())

    def test_short_holds_kept_as_holds(self):
        # 不按本作 2 拍阈值降级：Adapter 忠实转换，修谱交给人
        chart, _ = run(osu(["64,192,1000,128,0,1050:0:0:0:0:"]))
        self.assertEqual(chart["notes"][0]["type"], "hold")

    def test_note_inside_hold_warned_not_removed(self):
        chart, rep = run(osu(["64,192,1000,128,0,3000:0:0:0:0:", "64,192,2000,1,0,0:0:0:0:"]))
        self.assertEqual(len(chart["notes"]), 2)
        self.assertTrue(any("长按里面" in w for w in rep.warnings))


class Validation(unittest.TestCase):
    def test_sorted_output_from_unsorted_source(self):
        chart, rep = run(osu(["64,192,3000,1,0,0:0:0:0:", "448,192,1000,1,0,0:0:0:0:",
                              "192,192,1000,1,0,0:0:0:0:"]))
        self.assertEqual([(n["time"], n["lane"]) for n in chart["notes"]], [(1.0, 1), (1.0, 3), (3.0, 0)])
        self.assertTrue(any("未按时间排序" in i for i in rep.infos))

    def test_duplicates_removed_keep_first(self):
        chart, rep = run(osu(["64,192,1000,128,0,2000:0:0:0:0:", "64,192,1000,1,0,0:0:0:0:"]))
        self.assertEqual(len(chart["notes"]), 1)
        self.assertEqual(chart["notes"][0]["type"], "hold")
        self.assertEqual(rep.removed["duplicate"], 1)

    def test_same_time_other_lane_is_not_duplicate(self):
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:", "192,192,1000,1,0,0:0:0:0:"]))
        self.assertEqual(len(chart["notes"]), 2)
        self.assertEqual(rep.removed["duplicate"], 0)

    def test_negative_time_removed(self):
        chart, rep = run(osu(["64,192,-20,1,0,0:0:0:0:", "64,192,1000,1,0,0:0:0:0:"]))
        self.assertEqual(len(chart["notes"]), 1)
        self.assertEqual(rep.removed["negative_time"], 1)

    def test_unparsable_and_unsupported_reported(self):
        chart, rep = run(osu(["garbage", "64,192,abc,1,0,0:0:0:0:",
                              "256,192,1000,2,0,B|300:100,1,100", "256,192,1500,12,0,2000,0:0:0:0:",
                              "64,192,3000,1,0,0:0:0:0:"]))
        self.assertEqual(rep.unparsable, 2)
        self.assertEqual(rep.unsupported, 2)
        self.assertEqual(len(chart["notes"]), 1)

    def test_not_mania_rejected(self):
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:"]).replace("Mode: 3", "Mode: 0"))
        self.assertIsNone(chart)
        self.assertTrue(any("osu!mania" in e for e in rep.errors))

    def test_7k_rejected(self):
        chart, rep = run(osu(["64,192,1000,1,0,0:0:0:0:"]).replace("CircleSize:4", "CircleSize:7"))
        self.assertIsNone(chart)

    def test_empty_is_fatal(self):
        chart, rep = run(osu([]))
        self.assertIsNone(chart)

    def test_validate_catches_bad_data(self):
        bad = {"metadata": {"laneCount": 4}, "notes": [
            {"type": "hold", "lane": 4, "time": -1.0, "duration": 0.0},
            {"type": "tap", "lane": 0, "time": -2.0, "duration": 0.0}]}
        problems = A.validate(bad)
        self.assertTrue(any("轨道" in p for p in problems))
        self.assertTrue(any("< 0" in p for p in problems))
        self.assertTrue(any("≤ 0" in p for p in problems))
        self.assertTrue(any("排序" in p for p in problems))


class Contract(unittest.TestCase):
    """输出必须能被现有 ChartLoader（v2 DTO）和 ChartEditorWindow 直接读。"""

    def setUp(self):
        self.chart, self.rep = run(osu(["64,192,1000,1,0,0:0:0:0:", "192,192,1500,128,0,2500:0:0:0:0:"]),
                                   difficulty="Hard", audio="song_master")

    def test_metadata_fields(self):
        m = self.chart["metadata"]
        for key in ("songName", "songArtist", "chartAuthor", "difficulty", "level", "bpm", "offset",
                    "audioFileName", "partName", "previewStartTime", "previewDuration", "formatVersion",
                    "noteCounts"):
            self.assertIn(key, m)
        self.assertEqual(m["formatVersion"], 2)
        self.assertEqual(m["difficulty"], 2)
        self.assertEqual(m["partName"], "Bass")
        self.assertEqual(m["audioFileName"], "song_master")
        self.assertEqual(m["noteCounts"], {"tap": 1, "hold": 1, "drag": 0, "flick": 0, "slide": 0})
        self.assertEqual(m["sourceMetadata"]["titleUnicode"], "测试曲")

    def test_note_fields_match_v2_dto(self):
        for n in self.chart["notes"]:
            self.assertEqual(set(n), {"type", "lane", "time", "duration", "direction", "wide", "path"})

    def test_deterministic(self):
        text = osu(["64,192,1000,1,0,0:0:0:0:", "192,192,1500,128,0,2500:0:0:0:0:"])
        a = A.dumps(run(text)[0])
        b = A.dumps(run(text)[0])
        self.assertEqual(a, b)

    def test_millisecond_exact(self):
        objs = ["64,192,%d,1,0,0:0:0:0:" % t for t in (1, 999, 12345, 234567)]
        chart, _ = run(osu(objs))
        for n, t in zip(chart["notes"], (1, 999, 12345, 234567)):
            self.assertEqual(round(n["time"] * 1000), t)


class Cli(unittest.TestCase):
    def test_cli_writes_chart_and_report(self):
        script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "osu_adapter.py")
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "Bass.osu")
            with open(src, "w", encoding="utf-8") as f:
                f.write(osu(["64,192,1000,1,0,0:0:0:0:", "64,192,1000,1,0,0:0:0:0:"]))
            out, rep = os.path.join(d, "out.json"), os.path.join(d, "r", "report.txt")
            p = subprocess.run([sys.executable, "-X", "utf8", script, src, "--out", out, "--part", "Bass",
                                "--report", rep], capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
            self.assertIn("1 duplicated notes removed", p.stdout)
            with open(out, encoding="utf-8") as f:
                self.assertEqual(len(json.load(f)["notes"]), 1)
            self.assertTrue(os.path.exists(rep))

    def test_cli_fails_on_fatal(self):
        script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "osu_adapter.py")
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "x.osu")
            with open(src, "w", encoding="utf-8") as f:
                f.write(osu(["64,192,1000,1,0,0:0:0:0:"]).replace("CircleSize:4", "CircleSize:7"))
            out = os.path.join(d, "out.json")
            p = subprocess.run([sys.executable, "-X", "utf8", script, src, "--out", out, "--part", "Bass"],
                               capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(p.returncode, 1)
            self.assertFalse(os.path.exists(out))

    def test_cli_refuses_report_in_resources_charts(self):
        script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "osu_adapter.py")
        p = subprocess.run([sys.executable, "-X", "utf8", script, "x.osu", "--out", "o.json", "--part", "Bass",
                            "--report", "Assets/Resources/Charts/r.txt"], capture_output=True, text=True,
                           encoding="utf-8")
        self.assertEqual(p.returncode, 2)


if __name__ == "__main__":
    unittest.main()
