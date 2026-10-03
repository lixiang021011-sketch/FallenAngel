#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""模仿 No Surprises 写法的一段草稿（只用来出试听）

从 Radiohead《No Surprises》身上偷的四条，全部落进这一段里：
  1. **音乐盒先把整段调子独奏一遍**，然后卡林巴接过去 —— 钩子在"正式开始"之前就教给听者；
  2. **全曲只有一条 4 小节循环**（Am - F - G - Am），没有再多的和声事件；
  3. **钩子每隔 4 小节就回来一次**，变化只靠密度（裸旋律 / 走动低音 / 八分低音）；
  4. **结尾不解决** —— 循环走到第 5 遍的最后一小节，那一小节全休止，只剩余韵。

全曲 = 同一条 4 小节循环走 5 遍 = 20 小节（4 的倍数），♩=74，约 65 秒。
音域与可行性仍守卡林巴的规矩：C4~E6、最多同时两音、无升降号。
"""
from pathlib import Path

from motif_draft02 import REST, note_xml

TEMPO = 74
DIV = 4
TITLE = "模仿 No Surprises · 一段草稿"
EXPRESSION = "像音乐盒那样，一颗一颗地"

# 唯一的一条循环（4 小节）
LOOP = [
    ([("E5", 4), ("G5", 2), ("A5", 2), ("A5", 8)], [("A4", 8), ("E4", 8)]),   # Am
    ([("G5", 4), ("E5", 2), ("D5", 2), ("E5", 8)], [("F4", 8), ("C4", 8)]),   # F
    ([("A5", 4), ("B5", 4), ("C6", 8)], [("G4", 8), ("D4", 8)]),               # G
    ([("B5", 2), ("A5", 2), ("G5", 4), ("E5", 8)], [("A4", 8), ("E4", 8)]),   # Am
]

SPARSE_BASS = [[(p, 8), (REST, 8)] for p in ("A4", "F4", "G4", "A4")]
WALK_BASS = [bass for _, bass in LOOP]          # 循环原本的低音：每小节两个音
EIGHTH_BASS = [
    [("A4", 2), ("E4", 2), ("A4", 2), ("E4", 2)],
    [("F4", 2), ("C4", 2), ("F4", 2), ("C4", 2)],
    [("G4", 2), ("D4", 2), ("G4", 2), ("D4", 2)],
    [("A4", 2), ("E4", 2), ("A4", 2), ("E4", 2)],
]
BARE = [(REST, 16)]


# 卡林巴：5 遍循环
KALIMBA = (
    [(m, b) for (m, _), b in zip(LOOP, SPARSE_BASS)]        # 第 1 遍：引子，音乐盒陪着
    + [(m, BARE) for (m, _) in LOOP]                        # 第 2 遍：裸旋律，一个人唱
    + [(m, b) for (m, _), b in zip(LOOP, WALK_BASS)]        # 第 3 遍：低音走动
    + [(m, b) for (m, _), b in zip(LOOP, EIGHTH_BASS)]      # 第 4 遍：最满
    + [(m, b) for (m, _), b in zip(LOOP, SPARSE_BASS)]      # 第 5 遍：回到最轻
)
KALIMBA[19] = ([(REST, 16)], [(REST, 16)])                  # 最后一小节：空

# 音乐盒：只在第 1 遍（教）和第 5 遍的前三小节（陪）出声
MUSIC_BOX = (
    [(m, [(REST, 16)]) for (m, _) in LOOP]
    + [([(REST, 16)], [(REST, 16)]) for _ in range(3 * 4)]
    + [(m, [(REST, 16)]) for (m, _) in LOOP[:3]]
    + [([(REST, 16)], [(REST, 16)])]
)

DYNAMICS = {1: ("p", 49), 5: ("pp", 33), 9: ("p", 49), 13: ("mf", 71), 17: ("p", 49)}


def build_part(part_id, name, program, bars, dynamics):
    p = [f'  <part id="{part_id}">\n']
    for index, (melody, bass) in enumerate(bars, start=1):
        p.append(f'    <measure number="{index}">\n')
        if index == 1:
            p.append(
                '      <attributes>\n'
                f'        <divisions>{DIV}</divisions>\n'
                '        <key><fifths>0</fifths><mode>minor</mode></key>\n'
                '        <time><beats>4</beats><beat-type>4</beat-type></time>\n'
                '        <clef><sign>G</sign><line>2</line></clef>\n'
                '      </attributes>\n'
            )
            if part_id == "P1":
                p.append(
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
        if index in dynamics:
            mark, value = dynamics[index]
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
    p.append('  </part>\n')
    return "".join(p)


def build():
    p = ['<?xml version="1.0" encoding="UTF-8"?>\n']
    p.append(
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 3.1 Partwise//EN" '
        '"http://www.musicxml.org/dtds/partwise.dtd">\n'
    )
    p.append('<score-partwise version="3.1">\n')
    p.append(f'  <work><work-title>{TITLE}</work-title></work>\n')
    p.append(
        '  <identification>\n    <creator type="composer">FallenAngel</creator>\n'
        '    <encoding><software>FallenAngel sketch_ns01.py</software></encoding>\n  </identification>\n'
    )
    p.append(
        '  <part-list>\n'
        '    <score-part id="P1">\n      <part-name>Kalimba</part-name>\n'
        '      <score-instrument id="P1-I1"><instrument-name>Kalimba</instrument-name></score-instrument>\n'
        '      <midi-instrument id="P1-I1"><midi-channel>1</midi-channel>'
        '<midi-program>109</midi-program></midi-instrument>\n    </score-part>\n'
        '    <score-part id="P2">\n      <part-name>Music Box</part-name>\n'
        '      <score-instrument id="P2-I2"><instrument-name>Music Box</instrument-name></score-instrument>\n'
        '      <midi-instrument id="P2-I2"><midi-channel>2</midi-channel>'
        '<midi-program>11</midi-program></midi-instrument>\n    </score-part>\n'
        '  </part-list>\n'
    )
    p.append(build_part("P1", "Kalimba", 109, KALIMBA, DYNAMICS))
    p.append(build_part("P2", "Music Box", 11, MUSIC_BOX, {1: ("p", 49), 17: ("mp", 64)}))
    p.append('</score-partwise>\n')
    return "".join(p)


def main():
    out_dir = Path(__file__).parent / "sketch_ns01"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "sketch_ns01.musicxml"
    out.write_text(build(), encoding="utf-8")
    beats = sum(sum(d for _, d in m) for m, _ in KALIMBA) / DIV
    print(f"wrote {out}")
    print(f"bars={len(KALIMBA)} duration={beats * 60 / TEMPO:.1f}s @ {TEMPO}bpm")


if __name__ == "__main__":
    main()
