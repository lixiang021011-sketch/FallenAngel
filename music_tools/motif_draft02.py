#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""《未完成》第二稿（独白版）—— MusicXML 生成器

作者对第一稿的意见（2026-09-24）：「旋律太呆了，而且没有记忆点，很机械。」
诊断与对策：
  1) 一稿全曲最大音程只有三度 —— 二稿母题开口就是一次**小六度上跳**
     （第 1 小节末的 D5-E5 吸气，落到第 2 小节的 C6），然后一路滑下来。
  2) 一稿节奏只有两种细胞 —— 二稿用四种（♩♪♪𝅗𝅥 / ♪♪♩𝅗𝅥 / ♩𝅗𝅥𝅗𝅥 / ♩♪♪𝅗𝅥 带休止）。
  3) 一稿低音每小节一个全音符（根音打拍子）—— 二稿低音全部走动，每小节两音，
     互相叠着响（卡林巴的雾状余韵）。
  4) 一稿母题原样重复三次 —— 二稿第二次回来时**节奏被改**（♪♪♩𝅗𝅥），
     第三次回来时**和声被改**（同样的收尾，底下从 Am 换成 F）。

保留第一稿立住的部分：不落 C 的调性设计、「爬到琴的最高键 E6 就上不去」、
末尾停在 D/G 上等一个始终没来的 C、以及 17 键卡林巴的可操作性
（音域 C4~E6、最多两音同时、无升降号）。

输出：draft02/motif_draft02.musicxml
"""
from pathlib import Path

DIV = 4                      # divisions：四分音符 = 4
TEMPO = 68
TITLE = "未完成 · 第二稿（独白版）"
SUBTITLE = "卡林巴独奏 ♩=68 · 熄灯之后"
EXPRESSION = "轻轻地，像怕吵到别人"
COMPOSER = "FallenAngel"

REST = None
TYPE_BY_DUR = {16: "whole", 8: "half", 4: "quarter", 2: "eighth", 1: "sixteenth"}
# 附点：12 = 附点二分（引子那个占三个拍的休止）
DOTTED_BY_DUR = {24: "whole", 12: "half", 6: "quarter", 3: "eighth"}

# 母题：小六度上跳（E5→C6）后级进滑落 —— 这是整首曲子的记忆点
HOOK = [("C6", 4), ("B5", 2), ("A5", 2), ("G5", 8)]
HOOK_SHIFTED = [("C6", 2), ("B5", 2), ("A5", 4), ("G5", 8)]   # 同一个形状，换节奏
SIGH = [("A5", 4), (REST, 2), ("G5", 2), ("E5", 8)]           # 带一个八分休止的叹气
LIFT = [("D5", 2), ("E5", 2), ("G5", 4)]                       # 往上够

BARS = [
    # 引子：一个低音 + 吸气上行，直接推进母题
    ([(REST, 12), ("D5", 2), ("E5", 2)], [("G4", 16)]),
    # A 句：母题（小六度上跳 → 滑落）
    (list(HOOK), [("A4", 8), ("E4", 8)]),
    (list(SIGH), [("F4", 8), ("C4", 8)]),
    (LIFT + [("A5", 8)], [("G4", 8), ("D4", 8)]),
    ([("G5", 4), ("E5", 2), ("D5", 2), ("E5", 8)], [("A4", 8), ("E4", 8)]),   # 收在 Am：柔软
    # A' 句：母题换节奏回来，低音换路
    (list(HOOK_SHIFTED), [("F4", 8), ("C4", 8)]),
    (list(SIGH), [("G4", 8), ("D4", 8)]),
    (LIFT + [("B5", 8)], [("A4", 8), ("E4", 8)]),                             # 收在 B5：问句
    ([("C6", 4), ("B5", 4), ("A5", 8)], [("G4", 8), ("D4", 8)]),              # 悬着
    # B 段：一个音一个音爬，爬到琴的最高键
    ([("A5", 4), ("B5", 4), ("C6", 4), ("D6", 4)], [("A4", 16)]),
    ([("E6", 8), ("D6", 8)], [("F4", 8), ("C4", 8)]),                         # E6 = 17 键卡林巴顶
    (list(HOOK), [("G4", 8), ("D4", 8)]),                                     # 下来用母题的节奏
    ([("E5", 8), (REST, 8)], [("A4", 16)]),                                   # 手停住
    # 尾声：母题原样回来，收尾也回来 —— 底下换成 F，然后停在 D/G
    (list(HOOK), [("A4", 8), ("E4", 8)]),
    (list(SIGH), [("F4", 8), ("C4", 8)]),
    ([("G5", 4), ("E5", 2), ("D5", 2), ("E5", 8)], [("F4", 8), ("C4", 8)]),
    ([("D5", 16)], [("G4", 16)]),
]

# 力度：平铺的强弱本身就是机械感的一种
DYNAMICS = {1: ("p", 49), 10: ("mf", 71), 13: ("p", 49)}


def split_pitch(name):
    return name[0], int(name[1:])


def type_xml(dur):
    if dur in DOTTED_BY_DUR:
        return f"<type>{DOTTED_BY_DUR[dur]}</type><dot/>"
    return f"<type>{TYPE_BY_DUR[dur]}</type>"


def note_xml(pitch, dur, voice, stem):
    if pitch is REST:
        return (
            f'      <note>\n        <rest/>\n        <duration>{dur}</duration>\n'
            f'        <voice>{voice}</voice>\n        {type_xml(dur)}\n      </note>\n'
        )
    step, octave = split_pitch(pitch)
    return (
        f'      <note>\n'
        f'        <pitch><step>{step}</step><octave>{octave}</octave></pitch>\n'
        f'        <duration>{dur}</duration>\n'
        f'        <voice>{voice}</voice>\n'
        f'        {type_xml(dur)}\n'
        f'        <stem>{stem}</stem>\n'
        f'      </note>\n'
    )


def build():
    p = []
    p.append('<?xml version="1.0" encoding="UTF-8"?>\n')
    p.append(
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 3.1 Partwise//EN" '
        '"http://www.musicxml.org/dtds/partwise.dtd">\n'
    )
    p.append('<score-partwise version="3.1">\n')
    p.append(f'  <work><work-title>{TITLE}</work-title></work>\n')
    p.append(
        '  <identification>\n'
        f'    <creator type="composer">{COMPOSER}</creator>\n'
        '    <encoding><software>FallenAngel motif_draft02.py</software></encoding>\n'
        '  </identification>\n'
    )
    p.append(
        '  <part-list>\n    <score-part id="P1">\n      <part-name>Kalimba</part-name>\n'
        '      <score-instrument id="P1-I1"><instrument-name>Kalimba</instrument-name></score-instrument>\n'
        '      <midi-instrument id="P1-I1">\n        <midi-channel>1</midi-channel>\n'
        '        <midi-program>109</midi-program>\n      </midi-instrument>\n'
        '    </score-part>\n  </part-list>\n'
    )
    p.append('  <part id="P1">\n')

    for index, (melody, bass) in enumerate(BARS, start=1):
        p.append(f'    <measure number="{index}">\n')
        if index == 1:
            p.append(
                '      <attributes>\n'
                f'        <divisions>{DIV}</divisions>\n'
                '        <key><fifths>0</fifths><mode>major</mode></key>\n'
                '        <time><beats>4</beats><beat-type>4</beat-type></time>\n'
                '        <clef><sign>G</sign><line>2</line></clef>\n'
                '      </attributes>\n'
                '      <direction placement="above">\n'
                '        <direction-type><metronome>'
                f'<beat-unit>quarter</beat-unit><per-minute>{TEMPO}</per-minute>'
                '</metronome></direction-type>\n'
                f'        <sound tempo="{TEMPO}"/>\n'
                '      </direction>\n'
                '      <direction placement="above">\n'
                f'        <direction-type><words>{EXPRESSION}</words></direction-type>\n'
                '      </direction>\n'
            )
        if index in DYNAMICS:
            mark, value = DYNAMICS[index]
            p.append(
                '      <direction placement="below">\n'
                f'        <direction-type><dynamics><{mark}/></dynamics></direction-type>\n'
                f'        <sound dynamics="{value}"/>\n'
                '      </direction>\n'
            )

        for pitch, dur in melody:
            p.append(note_xml(pitch, dur, 1, "up"))
        p.append(f'      <backup><duration>{sum(d for _, d in melody)}</duration></backup>\n')
        for pitch, dur in bass:
            p.append(note_xml(pitch, dur, 2, "down"))
        p.append('    </measure>\n')

    p.append('  </part>\n</score-partwise>\n')
    return "".join(p)


def main():
    out_dir = Path(__file__).parent / "draft02"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "motif_draft02.musicxml"
    out.write_text(build(), encoding="utf-8")

    beats = sum(sum(d for _, d in m) for m, _ in BARS) / DIV
    notes = [p for m, b in BARS for p, _ in m + b if p]
    midi = {n: (int(n[1:]) + 1) * 12 + "C D EF G A B".index(n[0]) for n in set(notes)}
    lo, hi = min(midi, key=midi.get), max(midi, key=midi.get)
    # 音程统计：一稿的问题就是最大音程只有三度
    line = [p for m, _ in BARS for p, _ in m if p]
    leaps = []
    for a, b in zip(line, line[1:]):
        if a == b:
            continue
        leaps.append(abs(midi[a] - midi[b]))
    print(f"wrote {out}")
    print(f"bars={len(BARS)} duration={beats * 60 / TEMPO:.1f}s @ {TEMPO}bpm")
    print(f"pitch {lo}->{hi} | 旋律相邻音最大音程 {max(leaps)} 半音 | 旋律音符 {len(line)}")


if __name__ == "__main__":
    main()
