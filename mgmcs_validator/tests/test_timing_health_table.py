"""全库体检表的分类规则，钉在几首已经人工核对过的歌上。

这些断言就是"分类规则改了会被发现"的那道闸：不是断言算法漂亮，而是断言
flyway 那种半小节错位、chichunhua 那种连续滑、taishou 那种干净，各自还落在原来的类里。

    python -m unittest discover -s tests -p "test_*.py"
"""

import sys
import re
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "docs"))

import timing_health_table as table  # noqa: E402


class TestClasses(unittest.TestCase):
    def test_a_clean_song_stays_clean(self):
        row = table.classify("taishou")
        self.assertEqual(row["class"], "clean")
        self.assertEqual(row["suspects"], [])
        self.assertEqual(row["phase"], "全部一致")

    def test_flyway_is_a_half_bar_slip_blamed_on_beat_this(self):
        row = table.classify("flyway")
        self.assertEqual(row["class"], "persistent ±2-beat disagreement")
        self.assertEqual(row["suspects"], ["BeatThis"])
        self.assertIn("phase slip（半小节）", row["flags"])
        self.assertIn("reconvergence", row["flags"])
        # 观测事实之外才轮到"像是谁错"，而且必须带"待人工确认"
        self.assertTrue(any("疑似 BeatThis 2-beat counting" in text for text in row["notes"]))

    def test_chichunhua_is_a_slide_not_a_parked_error(self):
        """chichunhua 的相位一直在走：段多、k 走遍正负、残差悬在中间。它不该被读成"停错一格"。"""
        row = table.classify("chichunhua")
        self.assertEqual(row["class"], "continuous slide")
        self.assertIn("continuous drift", row["flags"])
        self.assertNotIn("phase slip（整拍）", row["flags"])

    def test_eine_has_a_real_one_beat_offset(self):
        row = table.classify("eine")
        self.assertEqual(row["class"], "persistent ±1-beat disagreement")
        self.assertIn("phase slip（整拍）", row["flags"])

    def test_a_single_flip_does_not_decide_the_class(self):
        """高桥/单点：一处跳变不能给整首歌定性，只登记。"""
        for song in ("kaluo", "spica", "haiiro"):
            row = table.classify(song)
            self.assertEqual(row["class"], "clean", song)
            self.assertIn("single flip（只一处，未持续）", row["flags"], song)

    def test_local_drift_is_only_claimed_when_layer_b_is_quiet(self):
        for song in ("yorukake", "yue", "lulu"):
            row = table.classify(song)
            self.assertEqual(row["class"], "local drift", song)
            self.assertEqual(row["phase"], "全部一致" if song != "lulu" else "只有单点跳变", song)


class TestReport(unittest.TestCase):
    def test_the_admission_contract_covers_every_song_and_the_three_levels(self):
        """下游只读这一份：每首歌都必须有一个准入结论，而且说的清"哪几条还能用"。"""
        songs = sorted(path.name[: -len(".mgmcs.json")] for path in table.dr.DOCS.glob("*.mgmcs.json")
                       if not path.name.endswith(".raw.mgmcs.json"))
        admit = table.admission_payload(songs)
        self.assertEqual(admit["schemaVersion"], "timing-admission-0.1")
        self.assertEqual(len(admit["songs"]), len(songs))
        self.assertEqual(sum(admit["counts"].values()), len(songs))
        for entry in admit["songs"]:
            self.assertIn(entry["eligibility"], ("eligible", "caution", "ineligible"), entry["song"])
            self.assertTrue(entry["soundReadings"] or entry["eligibility"] == "ineligible", entry["song"])
            for item in entry["downgraded"]:
                self.assertTrue(item["ranges"], entry["song"])

    def test_a_human_confirmed_song_is_usable_with_that_one_reading(self):
        admit = table.admission_payload(["seiza"])
        entry = admit["songs"][0]
        self.assertEqual(entry["eligibility"], "eligible")
        self.assertEqual(entry["soundReadings"], ["workbench"])
        self.assertEqual({item["source"] for item in entry["downgraded"]},
                         {"BeatThis", "madmom", "mapperatorinator"})

    def test_drift_and_reconvergence_are_caution_with_ranges(self):
        admit = {entry["song"]: entry for entry in table.admission_payload(["chichunhua", "eine"])["songs"]}
        self.assertEqual(admit["chichunhua"]["eligibility"], "caution")
        self.assertTrue(admit["chichunhua"]["cautionRanges"])
        self.assertEqual(admit["eine"]["eligibility"], "caution")
        self.assertTrue(admit["eine"]["cautionRanges"])

    def test_every_song_gets_a_row_and_a_class(self):
        songs = sorted(path.name[: -len(".mgmcs.json")] for path in table.dr.DOCS.glob("*.mgmcs.json")
                       if not path.name.endswith(".raw.mgmcs.json"))
        text, rows = table.report(songs)
        self.assertEqual(len(rows), len(songs))
        allowed = {"clean", "local drift", "persistent ±2-beat disagreement",
                   "persistent ±1-beat disagreement", "continuous slide", "unresolved"}
        for row in rows:
            self.assertIn(row["class"], allowed, row["song"])
            self.assertIn("| %s |" % row["song"], text)

    def test_the_report_never_calls_support_a_vote(self):
        """不投票。支持量一律写成 matched measures。"""
        songs = sorted(path.name[: -len(".mgmcs.json")] for path in table.dr.DOCS.glob("*.mgmcs.json")
                       if not path.name.endswith(".raw.mgmcs.json"))
        text, _rows = table.report(songs)
        # "不是投票"这种解释可以出现，但"39 票"这种把支持量说成票的写法不许有
        self.assertIsNone(re.search(r"\d+\s*票", text))
        hot = table.hot_spans("kaitou")
        self.assertTrue(hot and all("support = " in line and "票" not in line for line in hot))

    def test_human_readings_never_drive_the_class(self):
        """人那条票少，不进分类——它出现在详情里，但不出现在嫌疑来源里。"""
        for song in ("eine", "velvet", "iloveyou"):
            row = table.classify(song)
            self.assertFalse(any("human" in name for name in row["suspects"]), song)


if __name__ == "__main__":
    unittest.main()
