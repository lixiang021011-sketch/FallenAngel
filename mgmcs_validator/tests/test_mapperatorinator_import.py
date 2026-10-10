"""Mapperatorinator 接入的验收用例（规格 §21 A–F）。

这一层的失败方式都很隐蔽：把一小节强掰成 4 拍、把 gap 用前后 BPM 补满、
把一次整拍的错位平均成半拍。所以每个用例都直接盯着"原始 observation 有没有被改"。

    python -m unittest discover -s tests -p "test_*.py"
"""

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "docs"))

import deviation_report as dev  # noqa: E402
import import_mapperatorinator_timing as mapper  # noqa: E402


def steady(start, bar, beats_per_bar, count, beat_step=None):
    """一段规规矩矩的输出：每小节 beats_per_bar 拍。"""
    beat_step = beat_step or bar / beats_per_bar
    events = []
    for measure in range(count):
        base = start + measure * bar
        for beat in range(beats_per_bar):
            events.append({"type": "measure" if beat == 0 else "beat", "time": base + beat * beat_step})
    return events


class ImportCase(unittest.TestCase):
    def setUp(self):
        self.temp = Path(tempfile.mkdtemp(prefix="mapper_test_"))
        self.out_root = self.temp / "out"
        # 这两个模块级常量是指向仓库的。改它们必须改回来，否则同一轮里的别的测试文件
        # 会看不到 static/observations（体检表那次就是这么被带偏的）。
        self.saved = (mapper.OBSERVATIONS, mapper.ARCHIVE, mapper.MAPPER_IN, dev.OBSERVATIONS)
        mapper.OBSERVATIONS = self.temp / "observations"
        mapper.ARCHIVE = self.temp / "observations" / "source"
        mapper.MAPPER_IN = self.temp / "in"
        dev.OBSERVATIONS = mapper.OBSERVATIONS

    def tearDown(self):
        mapper.OBSERVATIONS, mapper.ARCHIVE, mapper.MAPPER_IN, dev.OBSERVATIONS = self.saved

    def write_events(self, song, events):
        path = self.out_root / ("%s_timing_events" % song) / "timing_events.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(events), encoding="utf-8")
        return path

    def build(self, song, events):
        self.write_events(song, events)
        doc = mapper.build_song(song, self.out_root, archive=True)
        mapper.write_song(song, doc)
        return doc


class TestCaseA(ImportCase):
    """Case A：恒速正常歌——偏移小、不漂、usable。"""

    def test_steady_song_is_usable_and_aligned(self):
        doc = self.build("steady", steady(1.0, 2.0, 4, 40))
        self.assertEqual(doc["health"]["status"], "usable")
        self.assertEqual(doc["gaps"], [])
        self.assertAlmostEqual(doc["derived"]["barSeconds"], 2.0, places=6)
        reading = dev.mapperatorinator_reading("steady")
        other = {"name": "other", "kind": "explicit", "measures": [1.0 + index * 2.0 for index in range(40)],
                 "beatSeconds": 0.5, "beatsPerBar": 4.0, "health": "ok"}
        row = dev.pair_measure_phase(reading, other)
        self.assertIsNone(row["reason"])
        self.assertEqual(row["matched"], row["aligned"])
        self.assertEqual(row["eligibility"], "eligible")


class TestCaseB(ImportCase):
    """Case B：轻微持续漂移——报"连续滑"，且**不许**为了好看去重新拟合 observation。"""

    def test_drift_is_reported_as_slide_without_refitting(self):
        events = steady(1.0, 2.0, 4, 60)
        drifted = [{"type": event["type"], "time": round(event["time"] * (1 + 0.0004 * event["time"] / 60), 6)}
                   for event in events]
        doc = self.build("drift", drifted)
        # 原始 observation 一个数都不许被改写（重新拟合 = 把这首歌的局部性抹掉）
        written = sorted(beat for beat in doc["observations"]["beats"]) + \
            sorted(measure for measure in doc["observations"]["measures"])
        self.assertEqual(sorted(round(event["time"], 6) for event in drifted), sorted(written))
        reading = dev.mapperatorinator_reading("drift")
        other = {"name": "other", "kind": "explicit", "measures": [1.0 + index * 2.0 for index in range(60)],
                 "beatSeconds": 0.5, "beatsPerBar": 4.0, "health": "ok"}
        row = dev.pair_measure_phase(reading, other)
        self.assertIn("连续滑", dev.pair_verdict(row))
        self.assertLess(row["trend"]["maxJump"], 0.4)


class TestCaseC(ImportCase):
    """Case C：一致 → 分离 → 再一致，必须报 reconvergence。"""

    def test_reconvergence(self):
        other_measures = [1.0 + index * 2.0 for index in range(60)]
        events = []
        for index, time in enumerate(other_measures):
            if 20 <= index < 40:
                time += 1.0                      # 中段整体差半小节
            events.append({"type": "measure", "time": round(time, 4)})
            for beat in range(1, 4):
                events.append({"type": "beat", "time": round(time + beat * 0.5, 4)})
        events.sort(key=lambda event: event["time"])
        self.build("reconv", events)
        row = dev.pair_measure_phase(dev.mapperatorinator_reading("reconv"),
                                     {"name": "other", "kind": "explicit", "measures": other_measures,
                                      "beatSeconds": 0.5, "beatsPerBar": 4.0, "health": "ok"})
        verdict = dev.pair_verdict(row)
        self.assertIn("reconvergence", verdict)
        self.assertIn("半小节", verdict)


class TestCaseD(ImportCase):
    """Case D：整拍 phase slip——不许被平均成 +0.5 拍。"""

    def test_slip_stays_integer(self):
        other_measures = [1.0 + index * 2.0 for index in range(60)]
        events = []
        for index, time in enumerate(other_measures):
            if index >= 30:
                time += 0.5                      # 后半首差一整拍，直到结尾
            events.append({"type": "measure", "time": round(time, 4)})
            for beat in range(1, 4):
                events.append({"type": "beat", "time": round(time + beat * 0.5, 4)})
        events.sort(key=lambda event: event["time"])
        self.build("slip", events)
        row = dev.pair_measure_phase(dev.mapperatorinator_reading("slip"),
                                     {"name": "other", "kind": "explicit", "measures": other_measures,
                                      "beatSeconds": 0.5, "beatsPerBar": 4.0, "health": "ok"})
        kinds = {vote["k"] for vote in row["votes"] if vote["k"] is not None}
        self.assertEqual(kinds, {0, 1})           # 整拍就是整拍，不许出现 0.5
        self.assertGreater(row["trend"]["maxJump"], 0.9)
        self.assertIn("跳变", dev.pair_verdict(row))


class TestCaseE(ImportCase):
    """Case E：显式给出的短小节必须原样保留，不许被掰成 4/4。"""

    def test_short_measure_survives(self):
        events = steady(1.0, 2.0, 4, 8)
        events += [{"type": "measure", "time": 18.0},       # 只有一拍的小节
                   {"type": "beat", "time": 18.5},
                   {"type": "measure", "time": 19.0}]
        events += steady(21.0, 2.0, 4, 8)
        events.sort(key=lambda event: event["time"])
        doc = self.build("short", events)
        measures = doc["observations"]["measures"]
        self.assertIn(18.0, measures)
        self.assertIn(19.0, measures)
        self.assertAlmostEqual(measures[measures.index(19.0)] - measures[measures.index(18.0)], 1.0, places=6)
        self.assertEqual(doc["gaps"], [])            # 一小节短不等于"缺失"


class TestCaseF(ImportCase):
    """Case F：中间 20 秒没有输出——报 gap，不许用前后 BPM 补满。"""

    def test_gap_is_kept_and_not_filled(self):
        events = steady(1.0, 2.0, 4, 10) + steady(41.0, 2.0, 4, 10)
        doc = self.build("gap", events)
        self.assertEqual(len(doc["gaps"]), 1)
        self.assertAlmostEqual(doc["gaps"][0]["start"], 19.0, places=6)
        self.assertAlmostEqual(doc["gaps"][0]["end"], 41.0, places=6)
        self.assertEqual(len(doc["validRanges"]), 2)
        inside = [time for time in doc["observations"]["measures"] if 19.0 < time < 41.0]
        self.assertEqual(inside, [])                 # 中间一个数都没造
        self.assertEqual(doc["health"]["status"], "suspect")
        self.assertTrue(any("局部缺失" in reason for reason in doc["health"]["reasons"]))


class TestReadingRules(ImportCase):
    def test_measure_positions_are_pulse_positions_too(self):
        # 小节头那里模型发的是 MEASURE，不是 BEAT：两条合起来才是"拍"的位置。
        doc = self.build("pulse", steady(1.0, 2.0, 4, 4))
        pulse = doc["derived"]["pulsePositions"]
        self.assertEqual(len(pulse), 16)
        self.assertAlmostEqual(pulse[0], 1.0, places=6)

    def test_redlines_are_the_only_place_bpm_and_meter_come_from(self):
        # 模型自己在 1.0s 报了一次 timing change，红线也正好在 1.0s。
        events = steady(1.0, 2.0, 4, 8) + [{"type": "timing_point", "time": 1.0}]
        events.sort(key=lambda event: event["time"])
        self.write_events("meta", events)
        osu_dir = self.out_root / "meta_timing"
        osu_dir.mkdir(parents=True, exist_ok=True)
        (osu_dir / "beatmap1.osu").write_text(
            "[TimingPoints]\n1000,500,4,2,-1,100,1,0\n", encoding="utf-8")
        doc = mapper.build_song("meta", self.out_root, archive=False)
        self.assertEqual(doc["observations"]["timingPoints"],
                         [{"time": 1.0, "bpm": 120.0, "meter": 4}])
        self.assertIn("红线", doc["observationsFrom"])
        # 导出器为了对齐拍子另插的红线单列一份，不算模型说的变速
        self.assertEqual(len(doc["observations"]["exportedRedlines"]), 1)

    def test_the_model_may_report_no_timing_point_at_all(self):
        doc = self.build("notimingpoint", steady(1.0, 2.0, 4, 8))
        self.assertEqual(doc["observations"]["timingPoints"], [])
        self.assertEqual(doc["health"]["status"], "usable")

    def test_without_the_events_dump_measures_are_marked_as_an_assumption(self):
        osu_dir = self.out_root / "osuonly_timing"
        osu_dir.mkdir(parents=True, exist_ok=True)
        (osu_dir / "beatmap2.osu").write_text(
            "[TimingPoints]\n0,500,4,2,-1,100,1,0\n", encoding="utf-8")
        doc = mapper.build_song("osuonly", self.out_root, archive=False)
        self.assertIn("假设", doc["observationsFrom"])
        self.assertGreater(len(doc["observations"]["measures"]), 4)

    def test_too_few_observations_is_ineligible(self):
        doc = self.build("tiny", [{"type": "measure", "time": 1.0}, {"type": "beat", "time": 1.5}])
        self.assertEqual(doc["health"]["status"], "ineligible")

    def test_a_dense_timing_point_is_a_note_not_a_verdict(self):
        events = steady(1.0, 2.0, 4, 40)
        events.append({"type": "timing_point", "time": 10.0})
        events.append({"type": "timing_point", "time": 10.3})
        events.sort(key=lambda event: event["time"])
        doc = self.build("dense", events)
        self.assertEqual(doc["health"]["status"], "usable")
        self.assertTrue(any("过密" in note for note in doc["health"]["notes"]))

    def test_rebuild_from_archive_is_byte_identical(self):
        self.build("roundtrip", steady(1.0, 2.0, 4, 20))
        first = (mapper.OBSERVATIONS / "roundtrip.mapperatorinator.json").read_text(encoding="utf-8")
        other = self.temp / "rebuilt"
        mapper.rebuild_from_archive(mapper.ARCHIVE, other)
        self.assertEqual(first, (other / "roundtrip.mapperatorinator.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
