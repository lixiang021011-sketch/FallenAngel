"""分诊规则：只要"要不要人来听"这一件事判对，别的都别自作聪明。

每条规则一个反例和一个正例。最要紧的两条边界：
人按的那条不进分诊（它小节头太少，会被误判成"来源自身有问题"），
以及"滑过去"的那几对不能用来说"停错了几拍"。

    python -m unittest discover -s tests -p "test_*.py"
"""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "docs"))

import triage  # noqa: E402


def reading(name, measures, health="ok", meter_mix=""):
    return {"name": name, "health": health, "measures": measures, "meterMix": meter_mix}


def steady(start, bar, count):
    return [start + index * bar for index in range(count)]


def pair(a, b, runs, bar_beats=4.0, trend=None):
    return {"a": a, "b": b, "usable": True, "runs": runs, "beatsPerBarB": bar_beats,
            "trend": trend or {"beatsPerMinute": 0.0, "maxJump": 0.0, "jumpTime": None}}


def run(k, start, end, count, residual=0.03):
    return {"k": k, "start": start, "end": end, "count": count,
            "residuals": [residual] * count}


def facts(pairs, readings, layer_a=None, singles=None, drift_range=None):
    return {"song": "song", "pairs": pairs, "readings": readings,
            "layerA": layer_a or {"drift": "几乎没有"}, "singles": singles or [],
            "driftRange": drift_range or []}


class TestRuleOne(unittest.TestCase):
    """半小节分歧 + 某来源自己报异常小节 + 其余独立来源一致 → 降级它，不进人工。"""

    def test_a_short_bar_source_is_downgraded(self):
        # BeatThis 自己有小节长度异常（2 拍小节），另外两条互相一致
        beat_this = reading("BeatThis", steady(0, 2.0, 30))
        beat_this["meterMix"] = "4 拍×20 2 拍×8"
        beat_this["measures"] = steady(0, 2.0, 22) + steady(60, 1.0, 4)   # 4 根短小节
        result = triage.diagnose(facts(
            [pair("madmom", "BeatThis", [run(2, 135, 155, 9)])],
            [beat_this, reading("madmom", steady(0.5, 2.0, 30)),
             reading("mapperatorinator", steady(0.5, 2.0, 30))]))
        self.assertEqual(result["diagnosis"], "source_measure_count_error")
        self.assertEqual(result["suspectSource"], "BeatThis")
        self.assertFalse(result["needsHumanReview"])
        self.assertEqual(result["attribution"], "bar-length")

    def test_a_conflict_between_two_views_is_left_for_a_human(self):
        """半小节那一档指向 A，整拍那一档指向 B —— 谁都不该被自动降级。"""
        result = triage.diagnose(facts(
            [pair("madmom", "workbench", [run(2, 10, 30, 8)]),
             pair("mapperatorinator", "BeatThis", [run(1, 10, 30, 8)]),
             pair("madmom", "mapperatorinator", [run(1, 10, 30, 8)])],
            [reading("BeatThis", steady(0, 2.0, 20)), reading("madmom", steady(0, 2.0, 20)),
             reading("mapperatorinator", steady(0, 2.0, 20)),
             reading("workbench", steady(0, 2.0, 20))]))
        self.assertTrue(result["needsHumanReview"])


class TestRuleTwo(unittest.TestCase):
    """整拍分歧：来源自身有问题就降级它；都健康且一直没合回来才进人工。"""

    def test_a_source_that_admits_it_is_broken_is_downgraded(self):
        result = triage.diagnose(facts(
            [pair("madmom", "mapperatorinator", [run(1, 10, 60, 25)])],
            [reading("madmom", steady(0, 2.0, 30)),
             reading("mapperatorinator", steady(0, 2.0, 30), health="suspect")]))
        self.assertEqual(result["diagnosis"], "source_self_anomaly")
        self.assertEqual(result["suspectSource"], "mapperatorinator")
        self.assertFalse(result["needsHumanReview"])

    def test_both_healthy_and_never_reconverging_goes_to_a_human(self):
        result = triage.diagnose(facts(
            [pair("madmom", "BeatThis", [run(1, 10, 100, 40)])],
            [reading("madmom", steady(0, 2.0, 50)), reading("BeatThis", steady(0, 2.0, 50))]))
        self.assertEqual(result["diagnosis"], "unresolved_phase")
        self.assertTrue(result["needsHumanReview"])


class TestRuleThree(unittest.TestCase):
    """连续滑不是相位错位：它的 ±1/±2 是走过整数格的痕迹。"""

    def test_a_slide_is_not_read_as_a_one_beat_error(self):
        runs = [run(1, 10, 30, 15, residual=0.14), run(-1, 40, 70, 15, residual=0.13),
                run(-2, 80, 100, 9, residual=0.12), run(2, 110, 140, 8, residual=0.16)]
        result = triage.diagnose(facts(
            [pair("BeatThis", "madmom", runs, trend={"beatsPerMinute": -1.24,
                                                     "maxJump": 1.0, "jumpTime": 40})],
            [reading("BeatThis", steady(0, 2.0, 60)), reading("madmom", steady(0, 2.0, 60))]))
        self.assertEqual(result["diagnosis"], "continuous_drift")
        self.assertFalse(result["needsHumanReview"])


class TestRuleFourAndFive(unittest.TestCase):
    def test_a_single_flip_is_only_logged(self):
        result = triage.diagnose(facts(
            [], [reading("BeatThis", steady(0, 2.0, 20))], singles=[
                {"pair": "madmom↔BeatThis", "time": 100.0, "jump": 1.0}]))
        self.assertEqual(result["diagnosis"], "single_flip")
        self.assertFalse(result["needsHumanReview"])

    def test_layer_a_drift_does_not_become_a_measure_error(self):
        result = triage.diagnose(facts(
            [], [reading("BeatThis", steady(0, 2.0, 20))],
            layer_a={"drift": "局部明显", "residual": 5891},
            drift_range=[{"start": 99, "end": 104, "seconds": 5}]))
        self.assertEqual(result["diagnosis"], "local_timing_drift")
        self.assertFalse(result["needsHumanReview"])
        self.assertEqual(result["affectedRange"][0]["start"], 99)


class TestRuleSix(unittest.TestCase):
    def test_reconvergence_only_marks_the_range(self):
        result = triage.diagnose(facts(
            [pair("madmom", "BeatThis", [run(2, 135, 155, 9), run(0, 158, 200, 12)])],
            [reading("madmom", steady(0, 2.0, 40)), reading("BeatThis", steady(0, 2.0, 40))]))
        self.assertEqual(result["diagnosis"], "reconvergence")
        self.assertEqual(result["affectedRange"][0]["start"], 135)
        self.assertFalse(result["needsHumanReview"])

    def test_a_long_unreturned_stretch_keeps_the_human_flag(self):
        result = triage.diagnose(facts(
            [pair("madmom", "BeatThis", [run(2, 135, 155, 9), run(0, 158, 200, 12),
                                         run(2, 210, 260, 20)])],
            [reading("madmom", steady(0, 2.0, 60)), reading("BeatThis", steady(0, 2.0, 60))]))
        self.assertEqual(result["diagnosis"], "reconvergence")
        self.assertTrue(result["needsHumanReview"])

    def test_a_tiny_unreturned_stretch_is_not_worth_a_human(self):
        result = triage.diagnose(facts(
            [pair("madmom", "BeatThis", [run(2, 135, 155, 9), run(0, 158, 200, 12),
                                         run(-2, 210, 216, 3)])],
            [reading("madmom", steady(0, 2.0, 60)), reading("BeatThis", steady(0, 2.0, 60))]))
        self.assertFalse(result["needsHumanReview"])


class TestBoundaries(unittest.TestCase):
    def test_a_human_verdict_overrides_the_rules(self):
        """人听过并指出哪条准，规则就不再猜——但错的那几条照旧记账。"""
        result = triage.diagnose({**facts(
            [pair("madmom", "workbench", [run(2, 10, 60, 20)]),
             pair("mapperatorinator", "workbench", [run(1, 10, 200, 91)])],
            [reading("BeatThis", steady(0, 2.0, 40)), reading("madmom", steady(0, 2.0, 40)),
             reading("mapperatorinator", steady(0, 2.0, 40)),
             reading("workbench", steady(0, 2.0, 40))]),
            "verdict": {"song": "song", "correctSource": "workbench", "scope": ["00:00–00:24"]}})
        self.assertEqual(result["diagnosis"], "human_confirmed_source")
        self.assertFalse(result["needsHumanReview"])
        self.assertIn("workbench", result["resolution"])
        self.assertEqual(set((result["suspectSource"] or "").split("、")),
                         {"madmom", "mapperatorinator"})

    def test_a_human_verdict_on_a_quiet_song_says_so(self):
        result = triage.diagnose({**facts([], [reading("BeatThis", steady(0, 2.0, 30))]),
                                  "verdict": {"song": "song", "correctSource": "BeatThis",
                                              "scope": ["01:00"]}})
        self.assertEqual(result["diagnosis"], "human_confirmed_clean")
        self.assertFalse(result["needsHumanReview"])

    def test_structure_alone_is_not_enough_to_downgrade(self):
        """kaitou 的反例：与众不同的那一条才是准的。只报形状，交给人。"""
        result = triage.diagnose(facts(
            [pair("madmom", "workbench", [run(2, 10, 30, 8)]),
             pair("mapperatorinator", "workbench", [run(2, 10, 30, 8)]),
             pair("workbench", "BeatThis", [run(-2, 10, 30, 6)])],
            [reading("BeatThis", steady(0, 2.0, 20)), reading("madmom", steady(0, 2.0, 20)),
             reading("mapperatorinator", steady(0, 2.0, 20)),
             reading("workbench", steady(0, 2.0, 20))]))
        self.assertEqual(result["diagnosis"], "structure_only_attribution")
        self.assertEqual(result["suspectSource"], "workbench")
        self.assertTrue(result["needsHumanReview"])

    def test_human_readings_are_never_blamed(self):
        """人按的小节头只有几个，按"小节太少"会被误判成来源自身有问题。"""
        result = triage.diagnose(facts(
            [pair("human:cal_01", "BeatThis", [run(2, 10, 30, 4)])],
            [reading("BeatThis", steady(0, 2.0, 30)),
             reading("human:cal_01", steady(0, 2.0, 4))]))
        self.assertIsNone(result["suspectSource"])
        self.assertFalse(result["needsHumanReview"])

    def test_a_clean_song_is_clean(self):
        result = triage.diagnose(facts(
            [], [reading("BeatThis", steady(0, 2.0, 30)), reading("madmom", steady(0, 2.0, 30))]))
        self.assertEqual(result["diagnosis"], "clean")
        self.assertFalse(result["needsHumanReview"])

    def test_the_summary_counts_by_the_human_flag_not_the_name(self):
        items = [{"song": "a", "diagnosis": "clean", "needsHumanReview": False},
                 {"song": "b", "diagnosis": "reconvergence", "needsHumanReview": True},
                 {"song": "c", "diagnosis": "source_measure_count_error", "needsHumanReview": False}]
        buckets = triage.summarise(items)
        self.assertEqual(buckets["humanReview"], ["b"])
        self.assertEqual(buckets["autoPass"], ["a"])
        self.assertEqual(buckets["autoExplained"], ["c"])


if __name__ == "__main__":
    unittest.main()
