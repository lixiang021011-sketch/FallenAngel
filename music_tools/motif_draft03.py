#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""《未完成》第三稿 —— 结构修整稿（只用来出试听）

作者意见（2026-09-24）：「线条还可以，但是结构不规整，一般来讲要 4 的倍数。」
问题定位：第二稿 = 引子 1 + 母题 4 + 变奏 4 + 爬升 4 + 尾声 4 = 17 小节，
只有引子破格，总数因此不是 4 的倍数。

本稿的唯一改动：
  * 引子由 1 小节补成 4 小节（新内容见 INTRO）；
  * 第二稿的旋律线条**一个音都没有动**，整体后移 4 小节（直接复用 draft02 的数据）。
结果：引子 4 ｜ 母题 8 ｜ 后半 8 = 20 小节 = 80 拍 @68bpm = 70.6 秒，每块都是 4 的倍数。

概念阶段只出音频试听；谱面 / MIDI / 图片等定稿后再打。
"""
from pathlib import Path

from motif_draft02 import BARS as V2_BARS, DIV, REST, TEMPO, EXPRESSION, COMPOSER, note_xml

TITLE = "未完成 · 第三稿（结构修整）"

# 新引子：手先摸到琴 → 母题「落下来」的那一半先单独响一次 → 吸气上行推进母题
INTRO = [
    ([(REST, 16)], [("G4", 16)]),                                  # b1 [G]  只有一个低音
    ([(REST, 16)], [("A4", 8), ("E4", 8)]),                        # b2 [Am]
    ([(REST, 8), ("G5", 4), ("E5", 4)], [("F4", 8), ("C4", 8)]),   # b3 [F]  母题的下行尾巴先响
    ([(REST, 12), ("D5", 2), ("E5", 2)], [("G4", 8), ("D4", 8)]),  # b4 [G]  吸气；G4+D4 与末小节同形
]

# V2_BARS[0] 是第二稿那 1 小节引子；新引子把它替换掉了（新的 b4 就是它，但低音改成 G4+D4，
# 与末小节的开放五度同形），所以从第二小节起接上。
BARS = INTRO + list(V2_BARS[1:])

# 力度随之整体后移 4 小节：开头 p、爬升段 mf、手停住回到 p
DYNAMICS = {1: ("p", 49), 13: ("mf", 71), 16: ("p", 49)}


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
        '    <encoding><software>FallenAngel motif_draft03.py</software></encoding>\n'
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
    out_dir = Path(__file__).parent / "draft03"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "motif_draft03.musicxml"
    out.write_text(build(), encoding="utf-8")
    beats = sum(sum(d for _, d in m) for m, _ in BARS) / DIV
    print(f"wrote {out}")
    print(f"bars={len(BARS)} duration={beats * 60 / TEMPO:.1f}s @ {TEMPO}bpm")


if __name__ == "__main__":
    main()
