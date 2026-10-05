# -*- coding: utf-8 -*-
"""谱面质量指标 + A 项校准（量化网格/强拍/长键）的单元测试（unittest，无第三方依赖）。

用法: python -X utf8 -m unittest chart_tools.test_chart_metrics -v
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import chart_metrics                                   # noqa: E402
from midi_to_ir import (GridSnapper, MultiGridSnapper,  # noqa: E402
                        TimeSignatureMap, load_profiles, subdivision_weight)


def chart_of(notes, bpm=120.0, difficulty="Hard", events=None):
    return {
        "metadata": {"songName": "unit", "bpm": bpm, "difficultyName": difficulty,
                     "instrumentType": "lead"},
        "notes": [{"type": t, "time": time, "lane": lane, "duration": duration}
                  for (time, lane, t, duration) in notes],
        "events": events or [],
    }


class QuantizationCalibrationTest(unittest.TestCase):
    """A 项：网格改 12/24/48 族（人谱覆盖率 ~99%），强拍按 1/12 拍网格。"""

    def test_default_profile_uses_human_grid(self):
        profiles = load_profiles()
        self.assertEqual(profiles["quantize"]["minDivision"], 48)      # 1/12 拍
        self.assertEqual(profiles["difficulty"]["Hard"]["minSubdivision"], 24)
        self.assertEqual(profiles["difficulty"]["Expert"]["minSubdivision"], 0)

    def test_multi_grid_snaps_to_straight_or_triplet(self):
        grid = MultiGridSnapper([16, 24])          # 十六分 + 十六分三连音
        self.assertAlmostEqual(grid.snap(0.26), 0.25, places=6)        # straight
        self.assertAlmostEqual(grid.snap(0.34), 1.0 / 3.0, places=6)   # triplet
        self.assertAlmostEqual(grid.distance(1.0 / 3.0), 0.0, places=6)

    def test_grid_snapper_note_values(self):
        self.assertAlmostEqual(GridSnapper(8).step, 0.5, places=9)     # 八分
        self.assertAlmostEqual(GridSnapper(16).step, 0.25, places=9)   # 十六分
        self.assertAlmostEqual(GridSnapper(12).step, 1.0 / 3.0, places=9)
        self.assertAlmostEqual(GridSnapper(48).step, 1.0 / 12.0, places=9)
        swing = GridSnapper(8, "Swing", 0.667)
        self.assertAlmostEqual(swing.snap(0.5), 0.667, places=3)

    def test_subdivision_weights_follow_metric_hierarchy(self):
        self.assertGreater(subdivision_weight(0.0), subdivision_weight(0.5))
        self.assertGreater(subdivision_weight(0.5), subdivision_weight(1.0 / 3.0))
        self.assertGreater(subdivision_weight(1.0 / 3.0), subdivision_weight(0.25))
        self.assertGreater(subdivision_weight(0.25), subdivision_weight(1.0 / 6.0))
        self.assertGreater(subdivision_weight(1.0 / 6.0), subdivision_weight(1.0 / 12.0))

    def test_beat_strength_multiplies_bar_and_subdivision(self):
        tsmap = TimeSignatureMap([], 480)                     # 默认 4/4
        table = load_profiles()["importance"]["beatStrengthTable"]
        downbeat = tsmap.beat_strength(0.0, table, 0.5)
        third = tsmap.beat_strength(2.0, table, 0.5)          # 第 3 拍（次强）
        downbeat_offbeat = tsmap.beat_strength(0.5, table, 0.5)   # 1 拍后的八分反拍
        second_beat = tsmap.beat_strength(1.0, table, 0.5)
        fine = tsmap.beat_strength(1.0 / 12.0, table, 0.5)    # 1/12 拍族
        self.assertAlmostEqual(downbeat, 1.0, places=6)
        self.assertAlmostEqual(third, 0.75, places=6)
        self.assertAlmostEqual(downbeat_offbeat, 1.0 * 0.6, places=6)
        self.assertAlmostEqual(second_beat, 0.55, places=6)
        self.assertAlmostEqual(fine, 1.0 * 0.22, places=6)
        # 层级：强拍 > 次强拍 > 强拍后的八分反拍 > 弱拍 > 1/12 拍族
        self.assertTrue(downbeat > third > downbeat_offbeat > second_beat > fine)


class NpsAndHoldCalibrationTest(unittest.TestCase):
    """A 项：难度 NPS 按人类中位折算到两指上限；长键阈值沿用已拍板的 2 拍。"""

    def test_difficulty_targets_match_calibration(self):
        difficulty = load_profiles()["difficulty"]
        self.assertEqual([difficulty[g]["maxNotesPerSecond"]
                          for g in ("Easy", "Normal", "Hard", "Expert")],
                         [3.0, 5.0, 7.5, 11.0])
        self.assertTrue(all(difficulty[g]["holdThresholdBeats"] == 2.0 for g in difficulty))
        self.assertTrue(all(difficulty[g]["holdTargetShare"] == 0.0 for g in difficulty))

    def test_human_baseline_table_loads(self):
        baseline = chart_metrics.load_baseline()
        self.assertAlmostEqual(baseline["grades"]["Hard"]["npsMedian"], 9.546, places=3)
        self.assertAlmostEqual(baseline["twoFinger"]["maxNps"], 12.0, places=3)
        self.assertAlmostEqual(baseline["grades"]["Easy"]["lnShareP50"], 0.152, places=3)


class ChartMetricsTest(unittest.TestCase):
    def test_onset_f1_against_self_is_one(self):
        chart = chart_of([(0.0, 1, "tap", 0.0), (0.25, 2, "tap", 0.0), (0.5, 3, "tap", 0.0)])
        result = chart_metrics.onset_f1(chart, [n["time"] for n in chart["notes"]], tol_ms=20)
        self.assertAlmostEqual(result["f1"], 1.0, places=6)
        self.assertAlmostEqual(result["precision"], 1.0, places=6)
        self.assertAlmostEqual(result["recall"], 1.0, places=6)

    def test_onset_f1_recall_drops_when_chart_is_sparse(self):
        chart = chart_of([(0.0, 1, "tap", 0.0)])
        result = chart_metrics.onset_f1(chart, [0.0, 0.5, 1.0, 1.5], tol_ms=50)
        self.assertAlmostEqual(result["precision"], 1.0, places=6)
        self.assertAlmostEqual(result["recall"], 0.25, places=6)

    def test_onset_f1_respects_tolerance(self):
        chart = chart_of([(0.0, 1, "tap", 0.0)])
        self.assertGreater(chart_metrics.onset_f1(chart, [0.03], tol_ms=50)["f1"], 0.9)
        self.assertEqual(chart_metrics.onset_f1(chart, [0.30], tol_ms=50)["f1"], 0.0)

    def test_pattern_clarity_detects_trill(self):
        # 周期 2 的 trill（1,2,1,2...）应被识别为 pattern；随机轨位则不应
        notes = [(index * 0.125, 1 + (index % 2), "tap", 0.0) for index in range(16)]
        chart = chart_of(notes)
        stats = chart_metrics.pattern_clarity(chart)
        self.assertGreater(stats["coverageP2"], 0.9)
        self.assertLess(stats["chance"], 0.2)

    def test_hold_stats_and_short_holds(self):
        chart = chart_of([(0.0, 1, "hold", 1.0),        # 1 拍 = 12 cell
                          (2.0, 2, "hold", 0.1),        # 0.1 拍 ≈ 1.2 cell（短长键）
                          (4.0, 3, "tap", 0.0)])
        stats = chart_metrics.hold_stats(chart)
        self.assertEqual(stats["holdCount"], 2)
        self.assertAlmostEqual(stats["holdShare"], 2 / 3, places=4)
        self.assertLess(stats["lenCellsP10"], 3.0)
        self.assertGreater(stats["shortShare"], 0.0)

    def test_lane_balance_and_outliers(self):
        chart = chart_of([(0.0, 0, "tap", 0.0), (0.0, 1, "tap", 0.0), (0.0, 2, "tap", 0.0)])
        balance = chart_metrics.lane_balance(chart)
        self.assertEqual(balance["counts"], [1, 1, 1, 0, 0])
        flags = chart_metrics.outlier_flags(chart, chart_metrics.load_baseline())
        self.assertTrue(any("同刻落手" in flag for flag in flags), flags)

    def test_nps_and_bpm_change_timeline(self):
        chart = chart_of([(0.0, 1, "tap", 0.0), (0.25, 2, "tap", 0.0), (0.5, 3, "tap", 0.0)])
        stats = chart_metrics.nps_stats(chart)
        self.assertEqual(stats["peakNps"], 3)
        self.assertAlmostEqual(stats["notesPerBeat"], 3.0, places=3)      # 3 音 / 1.0 拍
        shifted = chart_of([(0.0, 1, "tap", 0.0), (0.5, 2, "tap", 0.0)],
                           events=[{"time": 0.25, "type": "bpm", "value": 60.0}])
        beat_at = chart_metrics.beat_map(shifted)
        self.assertAlmostEqual(beat_at(0.0), 0.0, places=6)
        self.assertAlmostEqual(beat_at(0.25), 0.5, places=6)              # 前段 120 BPM
        self.assertAlmostEqual(beat_at(0.5), 0.75, places=6)              # 后段 60 BPM

    def test_compare_to_baseline_reports_verdicts(self):
        chart = chart_of([(index * 0.1, index % 5, "tap", 0.0) for index in range(20)])
        rows = chart_metrics.compare_to_baseline(chart, "Hard")
        metrics = {row["metric"] for row in rows}
        self.assertIn("中位 NPS", metrics)
        self.assertIn("pattern 覆盖", metrics)
        self.assertTrue(all(row["verdict"] for row in rows))

    def test_star_rating_optional_dependency(self):
        chart = chart_of([(0.0, 1, "tap", 0.0)])
        value = chart_metrics.star_rating(chart)
        self.assertTrue(value is None or value >= 0.0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
