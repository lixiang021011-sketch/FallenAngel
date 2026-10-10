"""Time Coordinate Map v0.1, Python side.

Same fixtures as the browser's tests (tests/coordinate_fixtures.json), because
the same arithmetic implemented twice is a way to disagree with yourself. The
expectations are hand-computed from the spec, so a change to either side that
moves a number has to move a test.

    python -m unittest discover -s tests -p "test_*.py"
"""

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))

import coordinate_map as cm  # noqa: E402

FIXTURES = json.loads(
    (Path(__file__).resolve().parents[1] / "tests" / "coordinate_fixtures.json")
    .read_text(encoding="utf-8"))


class CoordinateMapTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.song = cm.load_song(FIXTURES["document"])
        cls.map = cm.CoordinateMap(cls.song)

    def test_a_grid_with_no_positive_tempo_is_refused(self):
        self.assertEqual(self.map.grid_ids(),
                         ["grid_A", "grid_B", "grid_T", "grid_C"])
        for grid_id in FIXTURES["refused"]:
            self.assertIn("error", self.map.time_to_coordinate(1.0, grid_id))
        self.assertTrue(any("bpm" in p for p in self.song.problems))

    def test_time_to_coordinate(self):
        for case in FIXTURES["timeToCoordinate"]:
            out = self.map.time_to_coordinate(case["audioTime"], case["gridId"])
            self.assertNotIn("error", out, case["why"])
            self.assertEqual(out["bar"], case["bar"], case["why"])
            self.assertAlmostEqual(out["beat"], case["beat"], places=9, msg=case["why"])
            self.assertEqual(out.get("outOfAudioRange", False),
                             case.get("outOfAudioRange", False), case["why"])

    def test_coordinate_to_time(self):
        for case in FIXTURES["coordinateToTime"]:
            out = self.map.coordinate_to_time(case["bar"], case["beat"], case["gridId"])
            self.assertNotIn("error", out, case["why"])
            self.assertAlmostEqual(out["audioTime"], case["audioTime"], places=9, msg=case["why"])

    def test_one_moment_every_grid(self):
        for case in FIXTURES["projectTime"]:
            out = self.map.project_time(case["audioTime"])
            self.assertEqual(len(out["coordinates"]), len(case["coordinates"]))
            for got, want in zip(out["coordinates"], case["coordinates"]):
                self.assertEqual(got["gridId"], want["gridId"])
                self.assertEqual(got["bar"], want["bar"])
                self.assertAlmostEqual(got["beat"], want["beat"], places=9)

    def test_anchors_are_the_windows_beat_lines(self):
        for case in FIXTURES["generateAnchors"]:
            got = self.map.generate_anchors(case["gridId"], case["from"], case["to"])
            self.assertNotIn("error", got, case["why"])
            self.assertEqual(len(got), len(case["expect"]), case["why"])
            for got_one, want in zip(got, case["expect"]):
                self.assertAlmostEqual(got_one["audioTime"], want["audioTime"], places=9)
                self.assertEqual(got_one["bar"], want["bar"])
                self.assertEqual(got_one["isDownbeat"], want["isDownbeat"])

    def test_bad_input_is_an_error_not_a_number(self):
        for bad in FIXTURES["errors"]:
            if bad["call"] == "timeToCoordinate":
                out = self.map.time_to_coordinate(*bad["args"])
            elif bad["call"] == "coordinateToTime":
                out = self.map.coordinate_to_time(*bad["args"])
            else:
                out = self.map.generate_anchors(*bad["args"])
            self.assertIn("error", out, "%s %s —— %s" % (bad["call"], bad["args"], bad["why"]))

    def test_round_trip_within_a_millisecond(self):
        rt = FIXTURES["roundTrip"]
        for grid_id in rt["grids"]:
            for seconds in rt["times"]:
                at = self.map.time_to_coordinate(seconds, grid_id)
                back = self.map.coordinate_to_time(at["bar"], at["beat"], grid_id)
                self.assertAlmostEqual(back["audioTime"], seconds,
                                       delta=rt["toleranceSeconds"],
                                       msg="%s @ %s" % (grid_id, seconds))

    def test_the_written_map_holds_parameters_not_a_table(self):
        document = cm.coordinate_map_document(self.song)
        self.assertEqual(len(document["grids"]), len(self.song.grids))
        self.assertNotIn("anchors", document)
        self.assertIn("t = originTime", document["note"])
        # 一份只有参数的文件，四个网格，不该因为歌长而变大
        self.assertLess(len(json.dumps(document)), 4096)


class IrregularGridTest(unittest.TestCase):
    """小节不等长：小节线是一张列表，短小节不许被平均掉。"""

    @classmethod
    def setUpClass(cls):
        cls.case = FIXTURES["irregular"]
        cls.song = cm.load_song(cls.case["document"])
        cls.map = cm.CoordinateMap(cls.song)

    def test_the_bar_line_list_is_read_as_written(self):
        self.assertEqual(self.map.grid_ids(), ["g_list"])
        grid = self.song.grids[0]
        self.assertTrue(grid.irregular)
        self.assertEqual(grid.bar_times, [10.0, 12.0, 14.0, 15.0, 17.0])
        self.assertAlmostEqual(grid.bar_beats(2), 2.0)      # 短小节
        self.assertAlmostEqual(grid.bar_beats(3), 4.0)
        # 写出去的参数要能被再读一遍
        again = cm.load_song({"grids": [grid.as_dict()]})
        self.assertEqual(again.grids[0].bar_times, grid.bar_times)

    def test_time_to_coordinate(self):
        for case in self.case["timeToCoordinate"]:
            out = self.map.time_to_coordinate(case["audioTime"], case["gridId"])
            self.assertNotIn("error", out, case["why"])
            self.assertEqual(out["bar"], case["bar"], case["why"])
            self.assertAlmostEqual(out["beat"], case["beat"], places=9, msg=case["why"])

    def test_coordinate_to_time(self):
        for case in self.case["coordinateToTime"]:
            out = self.map.coordinate_to_time(case["bar"], case["beat"], case["gridId"])
            self.assertNotIn("error", out, case["why"])
            self.assertAlmostEqual(out["audioTime"], case["audioTime"], places=9, msg=case["why"])

    def test_a_short_bar_hands_out_the_beats_it_has(self):
        for case in self.case["generateAnchors"]:
            got = self.map.generate_anchors(case["gridId"], case["from"], case["to"])
            self.assertNotIn("error", got, case["why"])
            self.assertEqual(len(got), len(case["expect"]), case["why"])
            for got_one, want in zip(got, case["expect"]):
                self.assertAlmostEqual(got_one["audioTime"], want["audioTime"], places=9)
                self.assertEqual(got_one["bar"], want["bar"])
                self.assertEqual(got_one["isDownbeat"], want["isDownbeat"])

    def test_round_trip(self):
        rt = self.case["roundTrip"]
        for grid_id in rt["grids"]:
            for seconds in rt["times"]:
                at = self.map.time_to_coordinate(seconds, grid_id)
                back = self.map.coordinate_to_time(at["bar"], at["beat"], grid_id)
                self.assertAlmostEqual(back["audioTime"], seconds,
                                       delta=rt["toleranceSeconds"], msg=str(seconds))


if __name__ == "__main__":
    unittest.main()
