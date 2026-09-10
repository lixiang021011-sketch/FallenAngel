#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""chart_import 的确定性自测：解析、轨道映射、拍→秒换算、校验。

运行：python -X utf8 -m unittest chart_tools.test_chart_import -v
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from chart_import import parse_osu, parse_sm, validate, LANES   # noqa: E402

OSU_4K = """osu file format v14

[General]
AudioFilename: demo_song.mp3
Mode: 3

[Metadata]
Title:Import Test
Artist:Tester
Creator:Mapper

[Difficulty]
CircleSize:4

[TimingPoints]
1000,500,4,2,1,60,1,0

[HitObjects]
64,192,1000,1,0,0:0:0:0:
192,192,1500,1,0,0:0:0:0:
320,192,2000,1,0,0:0:0:0:
448,192,2500,128,0,3000:0:0:0:0:
"""

SM_SINGLE = """#TITLE:Step Test;
#ARTIST:Tester;
#MUSIC:demo_song.mp3;
#OFFSET:0.000;
#BPMS:0.000=120.000;
#NOTES:
     dance-single:
     Mapper:
     Easy:
     3:
     0,0,0,0,0:
1000
0100
0010
0001
,
2000
0000
3000
0000
;
"""


class TestOsuMania(unittest.TestCase):
    def test_metadata_and_lanes(self):
        chart = parse_osu(OSU_4K)
        meta = chart["metadata"]
        self.assertEqual(meta["songName"], "Import Test")
        self.assertEqual(meta["songArtist"], "Tester")
        self.assertEqual(meta["chartAuthor"], "Mapper")
        self.assertEqual(meta["audioFileName"], "demo_song")
        self.assertEqual(meta["formatVersion"], 2)
        self.assertAlmostEqual(meta["bpm"], 120.0, places=3)

    def test_notes_map_to_five_lanes(self):
        chart = parse_osu(OSU_4K)
        taps = [n for n in chart["notes"] if n["type"] == "tap"]
        self.assertEqual([n["lane"] for n in taps], [0, 1, 3])
        self.assertEqual([n["time"] for n in taps], [1.0, 1.5, 2.0])   # fixture 首个音符在 1000ms

    def test_hold_duration(self):
        chart = parse_osu(OSU_4K)
        holds = [n for n in chart["notes"] if n["type"] == "hold"]
        self.assertEqual(len(holds), 1)
        self.assertEqual(holds[0]["lane"], 4)
        self.assertAlmostEqual(holds[0]["time"], 2.5, places=3)
        self.assertAlmostEqual(holds[0]["duration"], 0.5, places=3)

    def test_counts_and_validation(self):
        chart = parse_osu(OSU_4K)
        self.assertEqual(chart["noteCounts"]["tap"], 3)
        self.assertEqual(chart["noteCounts"]["hold"], 1)
        self.assertEqual(validate(chart), [])


class TestStepMania(unittest.TestCase):
    def test_beat_to_seconds_and_lanes(self):
        chart = parse_sm(SM_SINGLE, "Easy")
        # 一小节 4 行 = 每行 1 拍；BPM 120 → 每拍 0.5s
        times = [n["time"] for n in chart["notes"] if n["type"] == "tap"]
        self.assertEqual(times, [0.0, 0.5, 1.0, 1.5])
        self.assertEqual([n["lane"] for n in chart["notes"] if n["type"] == "tap"], [0, 1, 3, 4])

    def test_hold_from_head_and_tail(self):
        chart = parse_sm(SM_SINGLE, "Easy")
        holds = [n for n in chart["notes"] if n["type"] == "hold"]
        self.assertEqual(len(holds), 1)
        self.assertEqual(holds[0]["lane"], 0)
        self.assertAlmostEqual(holds[0]["time"], 2.0, places=3)
        self.assertAlmostEqual(holds[0]["duration"], 1.0, places=3)

    def test_difficulty_filter(self):
        with self.assertRaises(ValueError):
            parse_sm(SM_SINGLE, "Challenge")

    def test_lane_count_is_clamped(self):
        chart = parse_sm(SM_SINGLE, "Easy")
        self.assertTrue(all(0 <= n["lane"] < LANES for n in chart["notes"]))


if __name__ == "__main__":
    unittest.main()
