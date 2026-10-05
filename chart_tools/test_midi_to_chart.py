# -*- coding: utf-8 -*-
"""MIDI → Chart 工具链单元测试（unittest，无第三方依赖；测试素材现场生成）。

覆盖需求 §二十二 的四种素材（Lead / Bass / Funk Guitar / Drum Groove+Fill）与
§二十三 的验收面（Functional / Musical / Playable / Configurable / Instrument-aware /
Traceable / Batch-ready）。

用法:
  python -X utf8 -m unittest chart_tools.test_midi_to_chart -v   （仓库根目录）
  python -X utf8 test_midi_to_chart.py                           （chart_tools 目录）
"""
import json
import os
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import chart_import                       # noqa: E402  复用既有校验
import playability                        # noqa: E402
from ir import IRStream                   # noqa: E402
from midi_parse import parse_midi         # noqa: E402
from midi_to_ir import GridSnapper, convert_midi   # noqa: E402
from midi_chart import generate_chart, run_batch   # noqa: E402
from rule_engine import RuleEngine, load_rules     # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TESTDATA = os.path.join(HERE, "testdata")
PPQ = 480
BPM = 120.0


# ---------------------------------------------------------------- SMF 写出（测试素材）

def _vlq(value: int) -> bytes:
    out = [value & 0x7F]
    value >>= 7
    while value:
        out.append(0x80 | (value & 0x7F))
        value >>= 7
    return bytes(reversed(out))


def _meta(meta_type: int, payload: bytes) -> bytes:
    return b"\x00\xFF" + bytes([meta_type]) + _vlq(len(payload)) + payload


def write_smf(path: str, tracks, bpm: float = BPM, ppq: int = PPQ) -> str:
    """tracks = [{"name","channel","program","notes":[(tick,pitch,vel,dur_tick)]}]"""
    chunks = []
    conductor = _meta(0x03, b"Conductor")
    conductor += b"\x00\xFF\x51\x03" + int(60_000_000 / bpm).to_bytes(3, "big")
    conductor += b"\x00\xFF\x58\x04\x04\x02\x18\x08"
    conductor += b"\x00\xFF\x2F\x00"
    chunks.append(conductor)

    for track in tracks:
        events = []
        for tick, pitch, velocity, duration in track["notes"]:
            events.append((tick, 1, pitch, velocity))
            events.append((tick + max(1, duration), 0, pitch, 0))
        events.sort(key=lambda e: (e[0], e[1] == 0, e[2]))   # 同 tick 先 on 后 off
        body = _meta(0x03, track.get("name", "track").encode("utf-8"))
        channel = track.get("channel", 0)
        if track.get("program") is not None:
            body += bytes([0x00, 0xC0 | channel, track["program"]])
        previous = 0
        for tick, kind, pitch, velocity in events:
            status = (0x90 if kind else 0x80) | channel
            body += _vlq(tick - previous) + bytes([status, pitch, velocity])
            previous = tick
        body += b"\x00\xFF\x2F\x00"
        chunks.append(body)

    data = b"MThd" + struct.pack(">IHHH", 6, 1, len(chunks), ppq)
    for chunk in chunks:
        data += b"MTrk" + struct.pack(">I", len(chunk)) + chunk
    with open(path, "wb") as handle:
        handle.write(data)
    return path


def build_testdata(directory: str) -> dict:
    """生成 §二十二 的四段测试 MIDI（各为短 Phrase，可直接当 Phrase 素材复用）。"""
    os.makedirs(directory, exist_ok=True)
    beat = PPQ

    # 1) Lead / Sax：上行 + 下行旋律
    ascending = [60, 62, 64, 65, 67, 69, 71, 72]
    descending = [72, 69, 67, 65, 64, 62, 60, 57]
    lead_notes = []
    for index, pitch in enumerate(ascending):
        lead_notes.append((int(index * beat / 2), pitch, 96, int(beat / 2) - 10))
    for index, pitch in enumerate(descending):
        lead_notes.append((int((4 + index * 0.5) * beat), pitch, 92, int(beat / 2) - 10))
    lead = write_smf(os.path.join(directory, "phrase_lead_sax.mid"),
                     [{"name": "Sax Lead", "channel": 0, "program": 65, "notes": lead_notes}])

    # 2) Bass Groove：重复根音 + 八度跳进（考验同轨 jack）
    bass_notes = []
    for bar in range(2):
        for step in range(8):
            pitch = 33 if step < 4 else 45
            bass_notes.append((int((bar * 4 + step * 0.5) * beat), pitch, 100, int(beat / 4)))
    bass = write_smf(os.path.join(directory, "phrase_bass_groove.mid"),
                     [{"name": "Bass", "channel": 1, "program": 34, "notes": bass_notes}])

    # 3) Funk Guitar：八分和弦短促 attack（带 sustain，考验和弦压缩与 Tap/Hold 决策）
    guitar_notes = []
    for step in range(16):
        start = int(step * beat / 2)
        for pitch in (60, 64, 67):
            guitar_notes.append((start, pitch, 88, int(beat * 0.4)))
    guitar = write_smf(os.path.join(directory, "phrase_funk_guitar.mid"),
                       [{"name": "Funk Guitar", "channel": 2, "program": 27, "notes": guitar_notes}])

    # 4) Drum Groove + Fill：kick/snare/16 分踩镲 + 第 3 小节 fill + 吊镲
    drum_notes = []
    for bar in range(2):
        base = bar * 4 * beat
        drum_notes.append((int(base), 36, 110, 30))
        drum_notes.append((int(base + 2 * beat), 36, 110, 30))
        drum_notes.append((int(base + 1 * beat), 38, 105, 30))
        drum_notes.append((int(base + 3 * beat), 38, 105, 30))
        for step in range(16):
            drum_notes.append((int(base + step * beat / 4), 42, 70, 20))
    fill_base = 2 * 4 * beat
    drum_notes.append((int(fill_base), 49, 120, 60))
    for step, pitch in enumerate((45, 47, 48, 50, 45, 47, 48, 50)):
        drum_notes.append((int(fill_base + (2 + step * 0.25) * beat), pitch, 108, 20))
    drum_notes.append((int(fill_base + 4 * beat), 36, 115, 30))
    drums = write_smf(os.path.join(directory, "phrase_drum_groove.mid"),
                      [{"name": "Drums", "channel": 9, "program": None, "notes": drum_notes}])
    return {"lead": lead, "bass": bass, "guitar": guitar, "drums": drums}


# ---------------------------------------------------------------- 测试

class MidiPipelineTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.testdata = build_testdata(os.path.join(cls.tmp.name, "midi"))

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    # ---- Functional
    def test_parser_reads_notes_tempo_and_track_name(self):
        data = parse_midi(self.testdata["lead"])
        self.assertEqual(data.ppq, PPQ)
        self.assertEqual(len(data.notes), 16)
        self.assertEqual([round(t.bpm) for t in data.tempos], [120])
        self.assertEqual(data.track_names.get(1), "Sax Lead")     # 轨 0 是 conductor
        self.assertEqual((data.time_signatures[0].numerator,
                          data.time_signatures[0].denominator), (4, 4))

    def test_parser_pairs_repeated_same_pitch_notes(self):
        data = parse_midi(self.testdata["bass"])
        self.assertEqual(len(data.notes), 16)
        self.assertEqual(sum(1 for n in data.notes if n.pitch == 33), 8)
        self.assertTrue(all(n.end_tick > n.start_tick for n in data.notes))

    def test_ir_events_use_normalized_ppq_and_beat_time(self):
        result = convert_midi(self.testdata["lead"], "lead", "Hard")
        self.assertTrue(result.ir.events)
        self.assertEqual(result.ir.events[0].tick % 96, 0)          # 768 PPQ 的 1/8 网格
        self.assertAlmostEqual(result.ir.events[0].time, 0.0, places=6)
        self.assertEqual(result.ir.metadata["instrumentType"], "lead")

    # ---- Quantizer
    def test_swing_and_triplet_grids(self):
        swing = GridSnapper(8, "Swing", 0.667)
        self.assertAlmostEqual(swing.snap(0.5), 0.667, places=3)
        self.assertAlmostEqual(swing.snap(1.5), 1.667, places=3)
        triplet = GridSnapper(12, "Triplet")
        self.assertAlmostEqual(triplet.step, 1.0 / 3.0, places=6)
        straight = GridSnapper(16)
        self.assertAlmostEqual(straight.snap(0.26), 0.25, places=6)

    # ---- Musical：旋律方向看得到
    def test_lead_contour_moves_across_lanes(self):
        result = generate_chart(self.testdata["lead"], "lead", "Hard")
        notes = sorted(result["chart"]["notes"], key=lambda n: n["time"])
        ascending = [n["lane"] for n in notes[:8]]
        descending = [n["lane"] for n in notes[8:16]]
        self.assertEqual(ascending, sorted(ascending), "上行旋律的轨位应递增：%s" % ascending)
        self.assertEqual(descending, sorted(descending, reverse=True),
                         "下行旋律的轨位应递减：%s" % descending)
        self.assertGreaterEqual(max(ascending) - min(ascending), 2)
        self.assertGreaterEqual(len(set(descending)), 3)

    # ---- Playable：Bass 重复 Groove 不产生无聊 jack
    def test_bass_groove_jack_reduced(self):
        result = generate_chart(self.testdata["bass"], "bass", "Hard")
        notes = sorted(result["chart"]["notes"], key=lambda n: n["time"])
        lanes = [n["lane"] for n in notes]
        longest = run = 1
        for previous, current in zip(lanes, lanes[1:]):
            run = run + 1 if current == previous else 1
            longest = max(longest, run)
        self.assertLessEqual(longest, 3, "同轨连打过长：%s" % lanes)
        reasons = set(c["reason"] for c in result["debug"]["playability"]["changes"])
        reasons |= {r["reason"] for r in result["debug"]["removals"]}
        self.assertTrue(reasons & {"JackReduction", "RepeatedNote"},
                        "重复 Groove 既没减音也没改轨：%s" % reasons)
        self.assertFalse(any(i["kind"] == "MaxRepeatedLaneCount"
                             for i in result["debug"]["playability"]["issues"]))

    # ---- Guitar：和弦不变成四押，也不产生 Hold 风暴
    def test_guitar_chords_compressed_and_mostly_taps(self):
        result = generate_chart(self.testdata["guitar"], "guitar", "Hard")
        notes = result["chart"]["notes"]
        self.assertEqual(result["debug"]["playability"]["stats"]["maxChord"], 2)
        self.assertEqual(sum(1 for n in notes if n["type"] == "hold"), 0)
        self.assertGreaterEqual(len(notes), 16)
        removals = [r["reason"] for r in result["debug"]["removals"]]
        self.assertIn("MaxChordSize", removals)

    # ---- Drums：难度决定保留哪些鼓件，踩镲不会让 Easy 密度爆炸
    def test_drum_difficulty_filters_hihat(self):
        easy = generate_chart(self.testdata["drums"], "drums", "Easy")["summary"]
        expert = generate_chart(self.testdata["drums"], "drums", "Expert")["summary"]
        self.assertLess(easy["notes"], expert["notes"] * 0.5)
        self.assertGreaterEqual(easy["notes"], 6)
        self.assertLessEqual(easy["peakNPS"], 5.0)
        self.assertGreater(expert["peakNPS"], easy["peakNPS"])

    # ---- Configurable：调 DifficultyProfile 明显改变密度
    def test_strum_style_is_opt_in_configuration(self):
        # 默认 tap：和弦 → 落手；显式开启 drag_full：扫弦 → 跨轨 drag 序列（带 strum_lanes 归一化）
        default = generate_chart(self.testdata["guitar"], "guitar", "Expert")
        self.assertNotIn("drag", default["summary"]["noteCounts"])
        strum = convert_midi(self.testdata["guitar"], "guitar", "Expert",
                             overrides={"difficulty": {"Expert": {"strumStyle": "drag_full"}}})
        strum_events = [e for e in strum.ir.events if e.articulations == ["strum"]]
        self.assertTrue(strum_events)
        lanes = strum_events[0].position["strum_lanes"]
        self.assertEqual(len(set(lanes)), 3, "扫弦应跨轨展开：%s" % lanes)

    def test_difficulty_profile_changes_density(self):
        counts = {name: generate_chart(self.testdata["drums"], "drums", name)["summary"]["notes"]
                  for name in ("Easy", "Normal", "Hard", "Expert")}
        self.assertLess(counts["Easy"], counts["Normal"])
        self.assertLess(counts["Normal"], counts["Expert"])
        self.assertLessEqual(counts["Hard"], counts["Expert"])
        default = convert_midi(self.testdata["drums"], "drums", "Expert")
        forced = convert_midi(self.testdata["drums"], "drums", "Expert",
                              overrides={"difficulty": {"Expert": {"maxNotesPerSecond": 3.0}}})
        self.assertLess(len(forced.ir.events), len(default.ir.events))

    # ---- Instrument-aware：四种乐器结果明显不同
    def test_instrument_aware_results_differ(self):
        signatures = {}
        for kind in ("lead", "bass", "guitar", "drums"):
            result = generate_chart(self.testdata[kind], kind, "Hard")
            lanes = tuple(sorted({n["lane"] for n in result["chart"]["notes"]}))
            signatures[kind] = (result["summary"]["notes"], lanes,
                                result["summary"]["noteCounts"].get("hold", 0))
        self.assertEqual(len(set(signatures.values())), 4, signatures)

    # ---- Traceable：Chart Note → Musical Event → 原始 MIDI 音符
    def test_traceability_chain(self):
        result = generate_chart(self.testdata["lead"], "lead", "Expert")
        chart_notes = result["chart"]["notes"]
        trace = result["debug"]["trace"]
        kept = [row for row in trace if row["keptInChart"]]
        self.assertEqual(len(kept), len(chart_notes))
        for row in kept:
            self.assertIsNotNone(row["musicalEventId"])
            self.assertTrue(row["midiIds"], "缺少原始 MIDI 溯源：%s" % row)
            self.assertRegex(row["midiIds"][0], r"^t\d+:c\d+:p\d+:\d+$")
        for removal in result["debug"]["removals"]:
            self.assertTrue(removal["reason"], "删减必须带原因")

    # ---- 输出结构与游戏侧 v2 协议一致
    def test_chart_json_matches_v2_protocol(self):
        result = generate_chart(self.testdata["guitar"], "guitar", "Normal",
                                audio="demo_song", title="测试曲")
        chart = result["chart"]
        self.assertEqual(chart["metadata"]["formatVersion"], 2)
        self.assertEqual(chart["metadata"]["noteCounts"], result["summary"]["noteCounts"])
        self.assertTrue(all(0 <= n["lane"] <= 4 for n in chart["notes"]))
        self.assertTrue(all(n["time"] >= 0 for n in chart["notes"]))
        self.assertEqual(chart_import.validate(chart), [])
        json.loads(json.dumps(chart, ensure_ascii=False))

    # ---- Deterministic + Batch-ready
    def test_deterministic_repeat_runs(self):
        first = generate_chart(self.testdata["bass"], "bass", "Normal")["chart"]
        second = generate_chart(self.testdata["bass"], "bass", "Normal")["chart"]
        self.assertEqual(json.dumps(first, ensure_ascii=False, sort_keys=True),
                         json.dumps(second, ensure_ascii=False, sort_keys=True))

    def test_batch_processing_writes_multiple_difficulties(self):
        with tempfile.TemporaryDirectory() as out_dir:
            debug_dir = os.path.join(self.tmp.name, "debug_batch")
            code = run_batch(self.testdata["bass"], "bass", ["Easy", "Hard"],
                             out_dir, debug_dir, None, None, None, None, 0.0, 4.0, True, None)
            self.assertEqual(code, 0)
            produced = sorted(os.listdir(out_dir))
            self.assertEqual(produced, ["phrase_bass_groove_Bass_Easy.json",
                                        "phrase_bass_groove_Bass_Hard.json"])
            self.assertEqual(len(os.listdir(debug_dir)), 2)
            with open(os.path.join(out_dir, produced[0]), encoding="utf-8") as handle:
                easy = json.load(handle)
            with open(os.path.join(out_dir, produced[1]), encoding="utf-8") as handle:
                hard = json.load(handle)
            self.assertLessEqual(len(easy["notes"]), len(hard["notes"]))

    def test_midi_phrase_uses_local_beat_from_zero(self):
        result = convert_midi(self.testdata["bass"], "bass", "Easy")
        self.assertAlmostEqual(min(e.tick for e in result.ir.events) / 768.0, 0.0, places=6)
        self.assertTrue(all(e.tick >= 0 for e in result.ir.events))

    def test_playability_validator_reports_reasons(self):
        notes = [{"type": "tap", "time": 0.0, "lane": 1},
                 {"type": "tap", "time": 0.05, "lane": 1},
                 {"type": "tap", "time": 0.10, "lane": 1},
                 {"type": "tap", "time": 0.15, "lane": 1},
                 {"type": "tap", "time": 0.20, "lane": 1}]
        profile = {"maxNPS": 4.0, "minSameLaneIntervalBeats": 0.5, "maxChordSize": 2,
                   "maxJackCount": 2, "maxHoldOverlap": 0, "maxLaneJump": 3, "bpm": 120.0}
        report = playability.validate(notes, profile)
        self.assertIn("MaxRepeatedLaneCount", {issue["kind"] for issue in report.issues})
        repaired, changes = playability.repair(notes, profile)
        self.assertTrue(any(c.reason == "JackReduction" for c in changes))
        after = playability.validate(repaired, profile)
        self.assertNotIn("MaxRepeatedLaneCount", {issue["kind"] for issue in after.issues})

    # ---- Tap / Hold：长音成 Hold，Funk 短 sustain 不成 Hold（需求 §十三）
    def test_hold_policy_long_notes_vs_stabs(self):
        pad = write_smf(os.path.join(self.tmp.name, "pad.mid"),
                        [{"name": "Piano Pad", "channel": 0, "program": 0,
                          "notes": [(0, 60, 90, 2 * PPQ), (2 * PPQ, 64, 90, 2 * PPQ),
                                    (4 * PPQ, 67, 90, 2 * PPQ)]}])
        holds = generate_chart(pad, "keys", "Hard")["summary"]["noteCounts"]
        self.assertEqual(holds.get("hold", 0), 3, holds)
        stab = generate_chart(self.testdata["guitar"], "guitar", "Hard")["summary"]["noteCounts"]
        self.assertNotIn("hold", stab)

    def test_hold_policy_clamps_overdrawn_sustain_instead_of_dropping(self):
        # sustain 盖住下一个音：裁短而不是整体降级 tap（规则引擎认可前端的 hold 决策）
        pad = write_smf(os.path.join(self.tmp.name, "overdraw.mid"),
                        [{"name": "Synth", "channel": 0, "program": 5,
                          "notes": [(0, 60, 90, int(3 * PPQ)), (int(1.5 * PPQ), 62, 90, PPQ // 4)]}])
        result = convert_midi(pad, "keys", "Hard")
        kinds = [(round(e.start_beat, 3), e.note_kind, round(e.duration_beat, 3))
                 for e in result.events]
        self.assertEqual(kinds[0][1], "hold", kinds)
        self.assertLess(kinds[0][2], 1.5)                 # 裁到下一个音之前
        self.assertIn("裁短", result.events[0].hold_detail)

    def test_hold_target_share_is_opt_in(self):
        # 时值 0.5/1/2/3 拍：默认 2 拍阈值 → 2 个 hold；目标占比 0.6 → 阈值降到 25 分位（1 拍）→ 更多 hold
        varied = write_smf(os.path.join(self.tmp.name, "varied.mid"),
                           [{"name": "Lead", "channel": 0, "program": 65,
                             "notes": [(0, 60, 90, PPQ // 2), (2 * PPQ, 62, 90, PPQ),
                                       (4 * PPQ, 64, 90, 2 * PPQ), (8 * PPQ, 65, 90, 3 * PPQ)]}])
        default = convert_midi(varied, "lead", "Hard")
        default_holds = sum(1 for e in default.events if e.note_kind == "hold")
        adaptive = convert_midi(varied, "lead", "Hard",
                                overrides={"difficulty": {"Hard": {"holdTargetShare": 0.6}}})
        adaptive_holds = sum(1 for e in adaptive.events if e.note_kind == "hold")
        self.assertGreater(adaptive_holds, default_holds, (default_holds, adaptive_holds))
        self.assertGreaterEqual(default_holds, 1)

    # ---- 多轨 MIDI：指定乐器选轨 / auto 选音符最多的轨
    def test_multi_track_selection(self):
        multi = os.path.join(self.tmp.name, "multi.mid")
        write_smf(multi, [
            {"name": "Bass", "channel": 1, "program": 34,
             "notes": [(int(i * PPQ / 2), 36 + (i % 3), 100, PPQ // 4) for i in range(8)]},
            {"name": "Drums", "channel": 9, "program": None,
             "notes": [(int(i * PPQ / 2), 42, 80, 20) for i in range(16)]},
        ])
        auto = generate_chart(multi, "auto", "Hard")["summary"]
        self.assertEqual(auto["instrument"], "drums")
        bass = generate_chart(multi, "bass", "Hard")["summary"]
        self.assertEqual(bass["instrument"], "bass")
        self.assertLess(bass["notes"], auto["notes"])
        drums = generate_chart(multi, "drums", "Hard")["summary"]
        self.assertGreaterEqual(drums["notes"], auto["notes"])


def _self_check_rule_engine_trace():
    """规则引擎 trace 参数的最小自检（不依赖测试素材）。"""
    from ir import NoteEvent
    events = [NoteEvent(tick=0, time=0.0, pitch=60, duration=0.1, duration_ticks=0,
                        velocity=0.8, instrument="other", position={"musical_event_id": 7})]
    trace = []
    notes, _warnings = RuleEngine(load_rules(os.path.join(HERE, "rules", "default.json"))) \
        .apply(IRStream(events=events), trace)
    assert len(notes) == 1 and trace and trace[0][1] == notes


if __name__ == "__main__":
    _self_check_rule_engine_trace()
    unittest.main(verbosity=2)
