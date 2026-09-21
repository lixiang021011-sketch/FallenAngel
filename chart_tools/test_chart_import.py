#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""chart_import 的确定性自测：解析、轨道映射、拍→秒换算、校验。

运行：python -X utf8 -m unittest chart_tools.test_chart_import -v
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from chart_import import (parse_osu, parse_sm, parse_phigros, parse_pec, parse_sus,   # noqa: E402
                          convert, validate, LANES)
import tempfile

PHIGROS_JSON = """{
  "formatVersion": 3, "offset": 0.0,
  "judgeLineList": [
    {"bpm": 120.0,
     "notesAbove": [
       {"type": 1, "time": 0.0, "positionX": -1.0, "holdTime": 0.0},
       {"type": 1, "time": 1.0, "positionX": 0.0,   "holdTime": 0.0},
       {"type": 1, "time": 2.0, "positionX": 1.0,   "holdTime": 0.0},
       {"type": 3, "time": 3.0, "positionX": -1.0,  "holdTime": 2.0},
       {"type": 2, "time": 4.0, "positionX": 0.0,   "holdTime": 0.0},
       {"type": 4, "time": 5.0, "positionX": 1.0,   "holdTime": 0.0}
     ],
     "notesBelow": [
       {"type": 1, "time": 0.5, "positionX": -1.0, "holdTime": 0.0},
       {"type": 4, "time": 1.5, "positionX": 1.0,  "holdTime": 0.0}
     ]}
  ]
}"""

PEC_TEXT = """#offset 0
#bpms 0=120
&line1
#notes
0,1,-1,0
1,3,1,2
2,4,0,0
2.5,2,0,0
#end
"""


class TestPhigros(unittest.TestCase):
    def test_above_lanes_and_beats_to_seconds(self):
        chart = parse_phigros(PHIGROS_JSON)
        # 只看上线那三个 tap（下线在 0.25s 也落在轨道 2，别混进来）
        taps = [n for n in chart["notes"] if n["type"] == "tap" and n["time"] in (0.0, 0.5, 1.0)]
        self.assertEqual([n["lane"] for n in taps], [0, 1, 2])          # x=-1/0/1 → 0/1/2
        self.assertEqual([n["time"] for n in taps], [0.0, 0.5, 1.0])    # 120BPM：1 拍 = 0.5s

    def test_below_lanes_shift_to_right_half(self):
        chart = parse_phigros(PHIGROS_JSON)
        below = [n for n in chart["notes"] if n["time"] in (0.25, 0.75)]
        self.assertEqual([n["lane"] for n in below], [2, 4])            # 下线映射 2..4

    def test_hold_and_drag_and_flick_types(self):
        chart = parse_phigros(PHIGROS_JSON)
        counts = chart["noteCounts"]
        self.assertEqual(counts["drag"], 1)
        self.assertEqual(counts["hold"], 1)
        self.assertEqual(counts["flick"], 2)
        hold = [n for n in chart["notes"] if n["type"] == "hold"][0]
        self.assertAlmostEqual(hold["duration"], 1.0, places=3)         # 2 拍 @120BPM

    def test_flick_direction_modes(self):
        up = [n for n in parse_phigros(PHIGROS_JSON)["notes"] if n["type"] == "flick"]
        self.assertEqual({n["direction"] for n in up}, {"up"})
        alt = [n for n in parse_phigros(PHIGROS_JSON, "alternate")["notes"] if n["type"] == "flick"]
        self.assertEqual(len(alt), 2)
        self.assertEqual({n["direction"] for n in alt}, {"up", "down"})   # 排序后不保证先后


class TestPec(unittest.TestCase):
    def test_pec_notes(self):
        chart = parse_pec(PEC_TEXT)
        self.assertEqual(chart["metadata"]["bpm"], 120.0)
        self.assertEqual(len(chart["notes"]), 4)
        self.assertEqual(chart["noteCounts"]["hold"], 1)
        self.assertEqual(chart["noteCounts"]["drag"], 1)
        self.assertEqual(chart["noteCounts"]["flick"], 1)

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


SUS_TEXT = """#TITLE "SUS Test"
#ARTIST "Tester"
#DESIGNER "Mapper"
#WAVE "demo_song.wav"
#BPM01: 120
#BPM02: 240
#TIL01: 4/4
#00002: 01
#00008: 01
#00012: 1100 0000 0000 0000
#00014: 0011 0000 0000 0000
#00017: 0000 0011 0000 0000
#00019: 0000 0000 0011 0000
#0001c: 0000 0000 0000 0011
#00056: 0000 0000 0000 0000 0011 0000 0000 0000
#00033a: 1100 0000 0000 0000
#00133a: 2100 0000 0000 0000
#00112: 1100 0000 0000 0000
"""


class TestSus(unittest.TestCase):
    """SUS（Project Sekai 社区格式）：通道语义照 OpenSekai 的 Converter 复刻。"""

    def test_metadata(self):
        meta = parse_sus(SUS_TEXT)["metadata"]
        self.assertEqual(meta["songName"], "SUS Test")
        self.assertEqual(meta["songArtist"], "Tester")
        self.assertEqual(meta["chartAuthor"], "Mapper")
        self.assertEqual(meta["audioFileName"], "demo_song")
        self.assertAlmostEqual(meta["bpm"], 120.0, places=3)
        self.assertEqual(meta["formatVersion"], 2)

    def test_bar_tokens_to_seconds(self):
        """120BPM 4/4：一小节 2s；数据 8 等分 → 每格 0.25s。"""
        chart = parse_sus(SUS_TEXT)
        lane0 = sorted(n["time"] for n in chart["notes"] if n["lane"] == 0 and n["type"] == "tap")
        self.assertEqual(lane0, [0.0, 2.0])          # bar0 第 0 格 / bar1 第 0 格

    def test_sus_lane_to_five_lanes(self):
        """SUS 12 条线线性铺到 5 轨。"""
        chart = parse_sus(SUS_TEXT)
        lanes = sorted({n["lane"] for n in chart["notes"]})
        self.assertEqual(lanes, [0, 1, 2, 3, 4])

    def test_friction_becomes_drag(self):
        text = '#BPM01: 120\n#00012: 5100 0000 0000 0000\n'
        chart = parse_sus(text)
        self.assertEqual(chart["noteCounts"].get("drag", 0), 1)

    def test_flick_channel(self):
        text = '#BPM01: 120\n#00052: 1100 0000 0000 0000\n'
        chart = parse_sus(text)
        flicks = [n for n in chart["notes"] if n["type"] == "flick"]
        self.assertEqual(len(flicks), 1)
        self.assertEqual(flicks[0]["direction"], "up")

    def test_long_note_pairs_head_and_tail(self):
        """longNo 通道里 1=头、2=尾 → 合成 hold，时长 = 尾时刻 − 头时刻。"""
        text = '#BPM01: 120\n#00033a: 1100 0000 0000 0000\n#00133a: 2100 0000 0000 0000\n'
        chart = parse_sus(text)
        holds = [n for n in chart["notes"] if n["type"] == "hold"]
        self.assertEqual(len(holds), 1)
        self.assertAlmostEqual(holds[0]["time"], 0.0, places=3)
        self.assertAlmostEqual(holds[0]["duration"], 2.0, places=3)   # bar1 起点 @120BPM = 2s
        self.assertEqual(holds[0]["lane"], 0)                          # SUS 线 1 → 轨 0（12→5 线性映射）

    def test_bpm_change_shifts_later_notes(self):
        """#00108 切到 240BPM：第 2 小节的音符要按新 BPM 折算。"""
        text = '#BPM01: 120\n#BPM02: 240\n#00008: 01\n#00108: 02\n#00112: 1100 0000 0000 0000\n'
        chart = parse_sus(text)
        # bar0 = 4 拍 @120 = 2.0s；bar1 的第一个记号就在 2.0s
        self.assertEqual([n["time"] for n in chart["notes"]], [2.0])

    def test_unclosed_long_note_gets_fallback_duration(self):
        chart = parse_sus('#BPM01: 120\n#00033a: 1100 0000 0000 0000\n')
        holds = [n for n in chart["notes"] if n["type"] == "hold"]
        self.assertEqual(len(holds), 1)
        self.assertAlmostEqual(holds[0]["duration"], 0.5, places=3)

    def test_skip_and_guide_ignored(self):
        """记号 3（skip）与 guide 通道（9x + longNo）不产出可玩音符。"""
        chart = parse_sus('#BPM01: 120\n#00012: 3100 0000 0000 0000\n#00092a: 1100 0000 0000 0000\n')
        self.assertEqual(chart["notes"], [])

    def test_validate_passes_on_fixture(self):
        self.assertEqual(validate(parse_sus(SUS_TEXT)), [])

    def test_txt_extension_dispatches_to_sus(self):
        """官方/社区 SUS 导出常用 .txt 扩展名（sekai.best 直链给的就是 .txt）。"""
        with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8") as handle:
            handle.write('#TITLE "TxtSus"\n#BPM01: 120\n#00012: 1100 0000 0000 0000\n')
            path = handle.name
        try:
            chart = convert(path)
            self.assertEqual(chart["metadata"]["songName"], "TxtSus")
            self.assertEqual(len(chart["notes"]), 1)
        finally:
            os.unlink(path)

    def test_same_lane_collapse_is_deduped(self):
        """12 线压到 5 轨会把不同源轨的同刻音符挤到同一条轨：必须去重，否则同轨同刻重复。"""
        text = ('#BPM01: 120\n'
                '#00012: 1100 0000 0000 0000\n'      # SUS 线 2 → 轨 0
                '#00013: 1100 0000 0000 0000\n')     # SUS 线 3 → 轨 0（同刻）
        chart = parse_sus(text)
        self.assertEqual(len(chart["notes"]), 1)
        self.assertEqual(chart["notes"][0]["lane"], 0)


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
