# -*- coding: utf-8 -*-
"""
规则引擎单元测试（unittest，无第三方依赖）
用法: python -m unittest test_rule_engine -v   （在 chart_tools 目录下）

覆盖：default.json 全部规则类型、post 处理、校验断言、确定性、IR 往返。
"""
import json
import unittest

from ir import IRStream, NoteEvent, stream_to_dict, stream_from_dict, IR_PPQ
from rule_engine import RuleEngine, build_chart, load_rules

PPQ = IR_PPQ
rules = load_rules("rules/default.json")


def ev(**kw):
    base = dict(tick=0, time=0.0, pitch=60, duration=0.0, duration_ticks=0,
                instrument="keys", articulations=[], velocity=0.8)
    base.update(kw)
    return NoteEvent(**base)


def apply(events, r=None, post=True):
    eng = RuleEngine(r or rules)
    ir = IRStream(events=events)
    notes, warnings = eng.apply(ir)
    return notes, warnings, eng


class TestDrumRules(unittest.TestCase):
    def test_kick_snare_tom(self):
        n, _, _ = apply([ev(instrument="drums", pitch=36, duration=0.1),
                         ev(instrument="drums", pitch=38, duration=0.1),
                         ev(instrument="drums", pitch=43, duration=0.1)])
        self.assertEqual([(x["type"], x["lane"]) for x in n],
                         [("tap", 0), ("tap", 1), ("tap", 3)])

    def test_hihat_closed_open_pedal(self):
        # HH 家族音乐上互斥（同一物理镲片的状态），错开时刻逐个验证
        for p, expect in [(42, "tap"), (46, "flick"), (44, "flick")]:
            n, _, _ = apply([ev(instrument="drums", pitch=p, time=float(p))])
            self.assertEqual((n[0]["type"], n[0]["lane"]), (expect, 2))
        n, _, _ = apply([ev(instrument="drums", pitch=46, time=0.0),
                         ev(instrument="drums", pitch=44, time=0.5)])
        self.assertEqual([(x["type"], x["lane"], x.get("direction")) for x in n],
                         [("flick", 2, "up"), ("flick", 2, "down")])

    def test_crash_ride(self):
        # 吊镲+叮叮同击 → 双镲拆两轨（chord_split 标准行为）
        n, w, _ = apply([ev(instrument="drums", pitch=49), ev(instrument="drums", pitch=52)])
        self.assertTrue(all(x["type"] == "tap" for x in n))
        self.assertEqual(sorted(x["lane"] for x in n), [2, 3])
        self.assertTrue(any("翻空位" in x for x in w))

    def test_drum_roll_tremolo_to_drag(self):
        n, _, _ = apply([ev(instrument="drums", pitch=42, articulations=["tremolo"])])
        self.assertEqual(n[0]["type"], "drag")

    def test_unknown_drum_pitch_falls_through(self):
        # 120 不在鼓件表 → 继续向下匹配 melodic-hold-long（<2拍不中）→ 未命中 → tap
        n, _, _ = apply([ev(instrument="drums", pitch=120)])
        self.assertEqual(n[0]["type"], "tap")


class TestMelodicRules(unittest.TestCase):
    def test_short_note_tap(self):
        n, _, _ = apply([ev(pitch=72, duration=0.25, duration_ticks=480)])
        self.assertEqual(n[0]["type"], "tap")

    def test_hold_tied_2beats(self):
        n, _, _ = apply([ev(pitch=60, duration=2.0, duration_ticks=2 * PPQ, tied=True)])
        self.assertEqual(n[0]["type"], "hold")

    def test_hold_long_not_tied(self):
        n, _, _ = apply([ev(pitch=60, duration=2.0, duration_ticks=2 * PPQ, tied=False)])
        self.assertEqual(n[0]["type"], "hold")

    def test_gliss_to_slide(self):
        n, _, _ = apply([ev(pitch=60, pitch_end=72, duration=0.5,
                            duration_ticks=PPQ, articulations=["gliss"])])
        self.assertEqual(n[0]["type"], "slide")
        path = n[0]["path"]
        self.assertEqual(path[0]["t"], 0.0)
        self.assertEqual(path[-1]["t"], n[0]["duration"])
        self.assertTrue(path[-1]["x"] > path[0]["x"])  # 上行 → 右移
        self.assertTrue(all(0.0 <= p["x"] <= 4.0 for p in path))

    def test_gliss_short_to_flick(self):
        # 0.05s < slide_min_duration(0.15) → 短滑音降级为方向 flick（上行→up / 下行→down）
        up, _, _ = apply([ev(pitch=60, pitch_end=64, duration=0.05, articulations=["gliss"])])
        dn, _, _ = apply([ev(pitch=60, pitch_end=56, duration=0.05, articulations=["gliss"])])
        self.assertEqual((up[0]["type"], up[0]["direction"]), ("flick", "up"))
        self.assertEqual((dn[0]["type"], dn[0]["direction"]), ("flick", "down"))

    def test_gliss_missing_pitch_end_downgrade(self):
        n, w, _ = apply([ev(pitch=60, duration=0.5, articulations=["gliss"])])
        self.assertEqual(n[0]["type"], "tap")
        self.assertTrue(any("降级" in x for x in w))

    def test_grace_flick_direction(self):
        up, _, _ = apply([ev(pitch=58, pitch_end=60, articulations=["grace"])])
        dn, _, _ = apply([ev(pitch=62, pitch_end=60, articulations=["grace"])])
        self.assertEqual((up[0]["type"], up[0]["direction"]), ("flick", "up"))
        self.assertEqual((dn[0]["type"], dn[0]["direction"]), ("flick", "down"))

    def test_tremolo_to_drag(self):
        n, _, _ = apply([ev(pitch=70, articulations=["tremolo"])])
        self.assertEqual(n[0]["type"], "drag")

    def test_unmatched_tap_with_pitch_lane(self):
        # 无记号普通音：未命中任何规则 → defaults.tap_lane = from_pitch
        n, _, _ = apply([ev(pitch=36)])  # keys 乐器低音
        self.assertEqual(n[0]["type"], "tap")
        self.assertEqual(n[0]["lane"], pitch_lane(36))

    def test_pitch_null_unmatched(self):
        n, _, _ = apply([ev(pitch=None, instrument="other")])
        self.assertEqual(n[0]["type"], "tap")


def pitch_lane(p):
    return int(round(p / 127.0 * 4))


class TestGuitarRules(unittest.TestCase):
    def test_bend_slide(self):
        # 推弦 2 半音 → 位移 = 2 × 0.5 = 1 整轨（全音=1轨）
        n, _, _ = apply([ev(instrument="guitar", pitch=57, duration=1.0,
                            duration_ticks=PPQ, articulations=["bend"],
                            position={"bend_semitones": 2})])
        self.assertEqual(n[0]["type"], "slide")
        path = n[0]["path"]
        self.assertEqual(path[1]["x"], round(path[0]["x"] + 1.0, 3))
        self.assertEqual(path[1]["t"], 1.0)

    def test_bend_missing_amount_downgrade(self):
        n, w, _ = apply([ev(instrument="guitar", pitch=57, duration=1.0,
                            articulations=["bend"])])
        self.assertEqual(n[0]["type"], "tap")
        self.assertTrue(any("降级" in x for x in w))

    def test_release_slide_left(self):
        n, _, _ = apply([ev(instrument="guitar", pitch=59, duration=1.0,
                            articulations=["release"], position={"bend_semitones": 1})])
        path = n[0]["path"]
        self.assertEqual(path[1]["x"], round(path[0]["x"] - 0.5, 3))

    def test_strum_cross_lane_drags(self):
        n, _, _ = apply([ev(instrument="guitar", pitch=30, articulations=["strum"],
                            position={"strum_pitches": [30, 60, 90, 120]})])
        self.assertEqual([x["type"] for x in n], ["drag"] * 4)
        lanes = [x["lane"] for x in n]
        self.assertEqual(lanes[0], 1)  # 低音
        self.assertEqual(lanes[-1], 4)  # 高音（5 键）
        self.assertTrue(all(n[i]["time"] < n[i + 1]["time"] for i in range(3)))


class TestBassRules(unittest.TestCase):
    def test_ghost_dense_to_drag(self):
        # 间隔 0.5 拍 ≤ 1 拍 → drag（密集）
        n, _, _ = apply([ev(instrument="bass", pitch=36, articulations=["ghost"],
                            tick=0, duration_ticks=PPQ),
                         ev(instrument="bass", pitch=38, articulations=["ghost"],
                            tick=PPQ // 2, duration_ticks=PPQ)])
        self.assertEqual([x["type"] for x in n], ["drag", "drag"])

    def test_ghost_sparse_to_flick_down(self):
        # 间隔 4 拍 > 1 拍 → 稀疏 → flick down
        n, _, _ = apply([ev(instrument="bass", pitch=36, articulations=["ghost"],
                            tick=0, duration_ticks=PPQ),
                         ev(instrument="bass", pitch=38, articulations=["ghost"],
                            tick=4 * PPQ, duration_ticks=PPQ)])
        self.assertEqual([(x["type"], x.get("direction")) for x in n],
                         [("flick", "down"), ("flick", "down")])

    def test_slap_flick_down(self):
        n, _, _ = apply([ev(instrument="bass", pitch=36, articulations=["slap"])])
        self.assertEqual((n[0]["type"], n[0]["direction"]), ("flick", "down"))

    def test_hammer_on_flick_up(self):
        n, _, _ = apply([ev(instrument="bass", pitch=36, articulations=["hammer_on"])])
        self.assertEqual((n[0]["type"], n[0]["direction"]), ("flick", "up"))

    def test_pull_off_drag(self):
        n, _, _ = apply([ev(instrument="bass", pitch=38, articulations=["pull_off"])])
        self.assertEqual(n[0]["type"], "drag")


class TestPostProcess(unittest.TestCase):
    def test_chord_split_shift(self):
        # 同刻 3 音同 pitch（同轨）→ 第二个翻相邻空位，第三个保留碰撞 + 警告
        n, w, _ = apply([ev(pitch=60, tick=0), ev(pitch=60, tick=0), ev(pitch=60, tick=0)])
        lanes = sorted(x["lane"] for x in n)
        self.assertEqual(lanes, [1, 2, 3])  # 5 键下 pitch=60 → lane 2，翻 1 → 再翻 3
        self.assertTrue(any("翻空位" in x for x in w))
        self.assertTrue(any("双轨上限" in x for x in w))

    def test_overlap_truncate_downgrade(self):
        # A: t0 长 2s；B: t1 同轨长 2s → A 截断到 1s，B 降级 tap
        a = ev(pitch=60, tick=0, time=0.0, duration=2.0, duration_ticks=4 * PPQ, tied=True)
        b = ev(pitch=60, tick=PPQ, time=1.0, duration=2.0, duration_ticks=4 * PPQ, tied=True)
        n, w, _ = apply([a, b])
        holds = [x for x in n if x["type"] == "hold"]
        taps = [x for x in n if x["type"] == "tap"]
        self.assertEqual(len(holds), 1)
        self.assertEqual(holds[0]["duration"], 1.0)
        self.assertEqual(len(taps), 1)
        self.assertTrue(any("降级 tap" in x for x in w))

    def test_density_warn(self):
        notes = [ev(pitch=60 + i, time=i * 0.1, tick=i * 100) for i in range(10)]
        _, w, _ = apply(notes)
        self.assertTrue(any("超密段" in x for x in w))

    def test_determinism(self):
        events = [ev(pitch=p, tick=t, duration=1.0, duration_ticks=PPQ * 2, tied=True)
                  for t, p in [(0, 60), (500, 64), (1000, 67)]]
        n1, w1, _ = apply(events)
        n2, w2, _ = apply(events)
        self.assertEqual(n1, n2)
        self.assertEqual(w1, w2)

    def test_flick_enabled_off_downgrade(self):
        r = json.loads(json.dumps(rules))
        r["params"]["flick_enabled"]["drums"] = False
        n, w, _ = apply([ev(instrument="drums", pitch=46)], r=r)
        self.assertEqual(n[0]["type"], "tap")
        self.assertTrue(any("flick_enabled" in x for x in w))


class TestChartOutput(unittest.TestCase):
    def test_build_chart_v2(self):
        events = [ev(instrument="drums", pitch=46, time=1.0, tick=768)]
        notes, _, eng = apply(events)
        ir = IRStream(events=events, metadata={"songName": "test"},
                      events_channel=[{"time": 0.0, "type": "bpm", "value": 160.0}])
        chart = build_chart(ir, notes)
        self.assertEqual(chart["metadata"]["formatVersion"], 2)
        self.assertEqual(chart["metadata"]["noteCounts"], {"flick": 1})
        self.assertEqual(chart["events"][0]["type"], "bpm")
        self.assertEqual(chart["notes"][0]["type"], "flick")

    def test_ir_roundtrip(self):
        e = ev(instrument="bass", pitch=36, articulations=["ghost"], tick=768,
               duration=1.0, duration_ticks=PPQ, position={"bend_semitones": 2})
        s = IRStream(events=[e], metadata={"a": 1})
        d = stream_to_dict(s)
        s2 = stream_from_dict(d)
        self.assertEqual(s2.events[0].position, {"bend_semitones": 2})
        self.assertEqual(s2.events[0].beats(), 1.0)
        self.assertEqual(s2.metadata, {"a": 1})

    def test_ir_ppq_reject(self):
        with self.assertRaises(ValueError):
            stream_from_dict({"ppq": 480, "events": []})


class TestValidate(unittest.TestCase):
    def test_lane_bounds(self):
        # 极端音高（0/127）不产生越界 lane（5 键 0-4）
        n, _, _ = apply([ev(pitch=0), ev(pitch=127)])
        self.assertTrue(all(0 <= x["lane"] <= 4 for x in n))


if __name__ == "__main__":
    unittest.main()
