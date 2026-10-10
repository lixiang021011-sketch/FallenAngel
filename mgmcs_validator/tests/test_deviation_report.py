"""B 层（Measure Phase）的规则，逐条钉死。

这一层最容易犯的错不是算不出来，而是把三件不同的事混成一件：
"两条线长度略有出入所以慢慢滑"、"某一处真的错开了一整拍（或半小节）"、
以及"最近邻在两侧来回跳造成的假跳变"。这三个各有一组测试。

    python -m unittest discover -s tests -p "test_*.py"
"""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "docs"))

import deviation_report as dev  # noqa: E402


def reading(name, measures, beat_seconds=0.5, beats_per_bar=4, health="ok", kind="explicit"):
    return {"name": name, "kind": kind, "measures": list(measures),
            "beatSeconds": beat_seconds, "beatsPerBar": beats_per_bar, "health": health}


def grid(start, bar_seconds, count):
    return [start + index * bar_seconds for index in range(count)]


class TestVotes(unittest.TestCase):
    def test_same_phase_is_zero(self):
        votes = dev.pair_phase_votes(grid(0.0, 2.0, 6), grid(0.0, 2.0, 6), 0.5, 4)
        self.assertEqual([vote["k"] for vote in votes], [0] * 6)

    def test_one_beat_off_is_plus_one(self):
        votes = dev.pair_phase_votes(grid(0.5, 2.0, 4), grid(0.0, 2.0, 4), 0.5, 4)
        self.assertEqual([vote["k"] for vote in votes], [1] * 4)

    def test_eighth_of_a_bar_off_is_not_a_phase_relation(self):
        # 差 0.5 拍：落在两拍正中间，既不是 0 也不是 1。这一票必须作废，不能被凑成整数。
        votes = dev.pair_phase_votes(grid(0.25, 2.0, 3), grid(0.0, 2.0, 3), 0.5, 4)
        self.assertEqual([vote["k"] for vote in votes], [None] * 3)

    def test_nearest_only_looks_at_the_closest_measure(self):
        # A 的第一个小节紧挨 B 的第二个小节：最近邻是 +0 拍，不是把它算成"差两个小节"。
        votes = dev.pair_phase_votes([4.0], grid(0.0, 2.0, 5), 0.5, 4)
        self.assertEqual(votes[0]["k"], 0)


class TestUnwrap(unittest.TestCase):
    def test_half_bar_flip_is_not_a_four_beat_jump(self):
        """A 的小节正好落在 B 两个小节正中间时，最近邻会在两侧换来换去。

        换来的结果不能变成"一次 4 拍的大跳"，那 4 拍是量法自己造的。
        """
        straight = dev.unwrap_phase([2.0, 2.0, 2.0, 2.0], 4)
        self.assertTrue(all(abs(value) == 2.0 for value in straight), straight)
        flipping = dev.unwrap_phase([2.0, -2.0, 2.0, -2.0], 4)
        steps = [abs(b - a) for a, b in zip(flipping, flipping[1:])]
        self.assertTrue(all(step <= 0.001 for step in steps), flipping)

    def test_a_real_half_bar_slip_survives(self):
        phases = dev.unwrap_phase([0.0, 0.0, 2.0, 2.0, 2.0], 4)
        self.assertAlmostEqual(abs(phases[2] - phases[1]), 2.0, places=6)


class TestRuns(unittest.TestCase):
    def test_same_relation_merges_and_a_gap_breaks_it(self):
        votes = [{"k": 0, "time": 0.0, "residual": 0.0},
                 {"k": 0, "time": 2.0, "residual": 0.0},
                 {"k": 0, "time": 4.0, "residual": 0.0},
                 {"k": 0, "time": 40.0, "residual": 0.0},
                 {"k": 0, "time": 42.0, "residual": 0.0},
                 {"k": 0, "time": 44.0, "residual": 0.0}]
        runs = dev.phase_runs(votes, bar_seconds=2.0, source_measures=20)
        self.assertEqual([run["count"] for run in runs], [3, 3])

    def test_sparse_source_only_needs_two_votes(self):
        votes = [{"k": 1, "time": 0.0, "residual": 0.0},
                 {"k": 1, "time": 3.0, "residual": 0.0}]
        self.assertEqual(len(dev.phase_runs(votes, 3.0, source_measures=3)), 1)
        self.assertEqual(dev.phase_runs(votes, 3.0, source_measures=30), [])

    def test_an_unmatched_vote_is_a_hole_not_an_ending(self):
        votes = [{"k": 2, "time": 0.0, "residual": 0.0},
                 {"k": None, "time": 2.0, "residual": None},
                 {"k": 2, "time": 4.0, "residual": 0.0},
                 {"k": 2, "time": 6.0, "residual": 0.0}]
        runs = dev.phase_runs(votes, 2.0, source_measures=20)
        self.assertEqual(len(runs), 1)
        self.assertEqual(runs[0]["count"], 3)


class TestTrendAndVerdict(unittest.TestCase):
    def test_slow_slide_beats_no_jump(self):
        """两条小节长度差 0.5%：整段在滑，但没有哪一处跳了整数拍。"""
        measures_b = grid(0.0, 2.0, 120)
        measures_a = [index * 2.0 * 1.005 for index in range(120)]
        row = dev.pair_measure_phase(reading("A", measures_a, 0.5), reading("B", measures_b, 0.5))
        self.assertAlmostEqual(row["barGapPct"], 0.5, places=1)
        self.assertIsNotNone(row["trend"]["beatsPerMinute"])
        self.assertGreater(abs(row["trend"]["beatsPerMinute"]), 0.05)
        self.assertLess(row["trend"]["maxJump"], 0.4)
        self.assertIn("连续滑", dev.pair_verdict(row))

    def test_a_half_bar_slip_is_reported_as_a_jump(self):
        measures_b = grid(0.0, 2.0, 60)
        measures_a = [index * 2.0 for index in range(20)]
        measures_a += [40.0 + index * 2.0 + 1.0 for index in range(20)]
        measures_a += [80.0 + index * 2.0 for index in range(20)]
        row = dev.pair_measure_phase(reading("A", measures_a, 0.5), reading("B", measures_b, 0.5))
        self.assertGreaterEqual(row["trend"]["maxJump"], 0.4)
        self.assertIn("跳变", dev.pair_verdict(row))
        self.assertIn("reconvergence", dev.pair_verdict(row))


class TestPairRules(unittest.TestCase):
    def test_bar_length_mismatch_is_not_a_phase_question(self):
        row = dev.pair_measure_phase(reading("A", grid(0.0, 1.7, 20), 0.42),
                                     reading("B", grid(0.0, 2.5, 20), 0.63))
        self.assertIsNotNone(row["reason"])
        self.assertIn("相位不可比", row["reason"])
        self.assertEqual(row["votes"], [])

    def test_the_sparser_source_asks_the_question(self):
        row = dev.pair_measure_phase(reading("dense", grid(0.0, 2.0, 40), 0.5),
                                     reading("sparse", grid(0.0, 2.0, 3), 0.5))
        self.assertEqual(row["a"], "sparse")
        self.assertEqual(len(row["votes"]), 3)

    def test_a_suspect_source_marks_the_comparison_caution(self):
        row = dev.pair_measure_phase(reading("A", grid(0.0, 2.0, 10), 0.5, health="suspect"),
                                     reading("B", grid(0.0, 2.0, 10), 0.5))
        self.assertEqual(row["eligibility"], "caution")


class TestGrain(unittest.TestCase):
    """一小节被切成两半在数，是 pulse 层的分歧，不是两条网格打架。"""

    def test_one_to_two_is_folded_instead_of_rejected(self):
        coarse = grid(0.0, 2.0, 20)
        fine = grid(0.0, 1.0, 40)
        row = dev.pair_measure_phase(reading("fine", fine, 0.25), reading("coarse", coarse, 0.5))
        self.assertIsNone(row["reason"])
        self.assertEqual(row["grain"], "1:2")
        self.assertEqual(row["a"], "fine")
        self.assertEqual(row["aligned"], row["matched"])
        self.assertGreater(row["matched"], 10)

    def test_a_shifted_grain_still_reports_the_offset(self):
        coarse = grid(0.0, 2.0, 20)
        fine = [index * 1.0 + 0.5 for index in range(40)]
        row = dev.pair_measure_phase(reading("fine", fine, 0.25), reading("coarse", coarse, 0.5))
        self.assertEqual(row["grain"], "1:2")
        self.assertEqual({vote["k"] for vote in row["votes"] if vote["k"] is not None}, {1})

    def test_a_grain_that_lands_between_beats_is_refused(self):
        coarse = grid(0.0, 2.0, 20)
        fine = [index * 1.0 + 0.25 for index in range(40)]
        row = dev.pair_measure_phase(reading("fine", fine, 0.25), reading("coarse", coarse, 0.5))
        self.assertIsNotNone(row["reason"])
        self.assertEqual(row["votes"], [])

    def test_mapperatorinator_style_half_bar_against_a_four_four_grid(self):
        # flyway 的真实形状：一条每 1.263s 报一次小节，另一条每 2.52s。
        coarse = [1.61 + index * 2.52 for index in range(40)]
        fine = [1.585 + index * 1.263 for index in range(80)]
        row = dev.pair_measure_phase(reading("mapper", fine, 0.3158), reading("madmom", coarse, 0.63))
        self.assertEqual(row["grain"], "1:2")
        self.assertIsNone(row["reason"])
        self.assertEqual(row["aligned"], row["matched"])


class TestHumanReading(unittest.TestCase):
    def make_taps(self, downbeat_times, beats_between, tap_seconds):
        """按手机真实的写法造一条：`songTime` 一步一跳，`captureTime` 才准。"""
        taps, index, clock = [], 0, 500.0
        for downbeat in downbeat_times:
            taps.append({"tapIndex": index, "button": "DOWNBEAT", "songTime": downbeat,
                         "captureTime": clock})
            index += 1
            for _ in range(beats_between - 1):
                taps.append({"tapIndex": index, "button": "BEAT",
                             "songTime": downbeat, "captureTime": clock + tap_seconds * index})
                index += 1
            clock += tap_seconds * beats_between
        return taps

    def test_interval_comes_from_the_phone_clock_anchor_from_the_song(self):
        taps = self.make_taps([10.0, 12.0, 14.0], beats_between=4, tap_seconds=0.5)
        reading = dev.human_readings({"rawTaps": taps})
        self.assertAlmostEqual(reading["beatSeconds"], 0.5, places=6)
        self.assertEqual(reading["beatsPerBar"], 4)
        # 锚点取第一下按压的 songTime，后面按手机自己的钟推：30 秒的播放器误差不会跑进间隔里。
        self.assertAlmostEqual(reading["measures"][0], 10.0, places=6)
        self.assertAlmostEqual(reading["measures"][1], 12.0, places=6)

    def test_one_downbeat_is_not_a_reading(self):
        taps = self.make_taps([10.0], beats_between=4, tap_seconds=0.5)
        self.assertIsNone(dev.human_readings({"rawTaps": taps}))


class TestMeterMix(unittest.TestCase):
    def test_mixed_meter_is_visible(self):
        beats = [index * 0.5 for index in range(12)]
        measures = [0.0, 2.0, 3.0, 5.0]
        mix = dev.meter_mix(beats, measures, 0.5)
        self.assertIn("4 拍", mix)
        self.assertIn("2 拍", mix)

    def test_no_beats_means_no_claim(self):
        self.assertEqual(dev.meter_mix([], [0.0, 2.0], 0.5), "—")


class TestAcceptanceCases(unittest.TestCase):
    """规格里点名的几个场景，用最小数据在最底层跑一遍。"""

    def test_case_aligned(self):
        row = dev.pair_measure_phase(reading("A", grid(1.0, 2.0, 24), 0.5),
                                     reading("B", grid(1.0, 2.0, 24), 0.5))
        self.assertEqual(dev.pair_verdict(row).startswith("全段一致"), True)

    def test_case_slip_is_not_averaged_into_half_a_beat(self):
        measures_b = grid(0.0, 2.0, 40)
        measures_a = [index * 2.0 for index in range(12)] + [24.0 + index * 2.0 + 0.5 for index in range(24)]
        row = dev.pair_measure_phase(reading("A", measures_a, 0.5), reading("B", measures_b, 0.5))
        ks = {vote["k"] for vote in row["votes"] if vote["k"] is not None}
        self.assertEqual(ks, {0, 1})                    # 相位只能是整数拍，不许出现 0.5
        self.assertGreater(abs(row["trend"]["maxJump"]), 0.9)   # 一次整拍的跳变（趋势拟合会吃掉一点）
        self.assertGreater(row["aligned"], 0)           # 前 12 小节确实是 aligned

    def test_case_gap_is_not_filled_in(self):
        votes = [{"k": 0, "time": time, "residual": 0.0} for time in (0.0, 2.0, 4.0)]
        votes.append({"k": None, "time": 6.0, "residual": 0.3})
        votes += [{"k": 0, "time": time, "residual": 0.0} for time in (30.0, 32.0, 34.0)]
        runs = dev.phase_runs(votes, bar_seconds=2.0, source_measures=20)
        self.assertEqual([run["count"] for run in runs], [3, 3])  # 中间那段没票，也没被补上
        self.assertEqual(runs[1]["start"], 30.0)


if __name__ == "__main__":
    unittest.main()
