#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""「远方版」草稿（只用来出试听）

作者对上一稿的意见（2026-09-24）：「走向太民谣太乡愁了，没有对远方的期盼。」
诊断：上一稿把《No Surprises》的引擎一起搬了过来 —— 那首歌的引擎是**认命**。
  乡愁的具体来源有三处：
    1. 低音每一小节都在往下掉（A4→E4 / F4→C4 / G4→D4）—— 向下就是回头看；
    2. 旋律每个循环都以"落下来"收尾；
    3. 和声中心停在 Am，一个"回头看"的落点。

这一稿的处理：**保留 No Surprises 的骨架（4 小节循环、紧重复、音乐盒、方整），
把情绪的矢量翻过来 —— 全部改成向上。**
  * 低音不再下落，改成静止的"地面"（每小节一个全音符，F4 G4 A4 G4），
    旋律负责往上走 —— 地面不动、人往上够；
  * **同一个 4 小节句子，一遍比一遍高**（按 A 小调的级数往上移：+0、+2、+3、+5、+7 度），
    第 5 遍正好到 A5~E6 —— **E6 就是 17 键卡林巴的最高键，远方就是琴的尽头**；
  * 旋律本身也改成"上行起句、落在高处"，不再往下收；
  * 第 5 遍**低音全部撤掉**：地面消失，只剩高处的旋律 —— 底下那点"认命"没有了；
  * 音乐盒第 1 遍在**高八度**上陪（像有人在教你），第 5 遍回到原位陪（陪你到最远处）。

20 小节 = 4 的倍数，♩=76，约 63 秒。守卡林巴的规矩：C4~E6、最多两音、无升降号
（级数移调天然不会移出升降号）。
"""
from pathlib import Path

from motif_draft02 import REST, note_xml

TEMPO = 76
DIV = 4
TITLE = "远方版 · 草稿"
EXPRESSION = "往上走，别回头"

NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
PC = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi_of(name):
    return (int(name[1:]) + 1) * 12 + PC[name[0]]


def name_of(value):
    return NAMES[value % 12] + str(value // 12 - 1)


NATURALS = [name_of(m) for m in range(45, 104) if "#" not in name_of(m)]


def transpose(name, degrees):
    """按 A 小调的级数往上移（不产生升降号）"""
    return NATURALS[NATURALS.index(name) + degrees]


# 唯一的一条 4 小节循环（旋律用 A4~E5 这个窄带 —— 一个"第一次做"的人手上的六个音）
BASE_MELODY = [
    [("A4", 4), ("C5", 2), ("D5", 2), ("E5", 8)],      # 上行，落在 E5
    [("D5", 4), ("C5", 2), ("B4", 2), ("C5", 8)],      # 退一步（迷茫）
    [("C5", 2), ("D5", 2), ("E5", 4), ("E5", 8)],      # 再上去
    [("E5", 4), ("D5", 2), ("E5", 2), ("E5", 8)],      # 够住，停在高处
]

WHOLE_BASS = [[("F4", 16)], [("G4", 16)], [("A4", 16)], [("G4", 16)]]
RISING_BASS = [
    [("F4", 8), ("A4", 8)],
    [("G4", 8), ("B4", 8)],
    [("A4", 8), ("B4", 8)],
    [("G4", 8), ("B4", 8)],
]
NO_BASS = [[(REST, 16)] for _ in range(4)]      # 每小节都要有一条空的小节线

# 五遍的移调级数（+7 度 = 正好一个八度）
PASS_SHIFT = [0, 2, 3, 5, 7]


def pass_bars(shift, bass):
    return [([(transpose(p, shift), d) for p, d in base], list(b))
            for base, b in zip(BASE_MELODY, bass)]


KALIMBA = (
    pass_bars(0, WHOLE_BASS)          # 第 1 遍：从最低的 A4 起步
    + pass_bars(2, WHOLE_BASS)        # 第 2 遍：整体高一个三度
    + pass_bars(3, RISING_BASS)       # 第 3 遍：再高，低音开始往上走
    + pass_bars(5, RISING_BASS)       # 第 4 遍：到 C6
    + pass_bars(7, NO_BASS)           # 第 5 遍：到琴顶 E6，地面撤掉
)
KALIMBA[19] = ([(REST, 16)], [(REST, 16)])       # 最后一小节：空


def music_box_notes(shift, octave_up):
    extra = 7 if octave_up else 0
    return [[(transpose(p, shift + extra), d) for p, d in bar] for bar in BASE_MELODY]


MUSIC_BOX = (
    [(bar, [(REST, 16)]) for bar in music_box_notes(0, True)]      # 第 1 遍：高八度陪
    + [([(REST, 16)], [(REST, 16)]) for _ in range(12)]
    + [(bar, [(REST, 16)]) for bar in music_box_notes(7, False)[:3]]  # 第 5 遍：回到原位
    + [([(REST, 16)], [(REST, 16)])]
)

DYNAMICS = {1: ("p", 49), 5: ("p", 49), 9: ("mp", 64), 13: ("mf", 71), 17: ("mp", 64)}


def build_part(part_id, bars, dynamics, leading):
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
            p.append(leading)
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
    lead = (
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
    p = ['<?xml version="1.0" encoding="UTF-8"?>\n',
         '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 3.1 Partwise//EN" '
         '"http://www.musicxml.org/dtds/partwise.dtd">\n',
         '<score-partwise version="3.1">\n',
         f'  <work><work-title>{TITLE}</work-title></work>\n',
         '  <identification>\n    <creator type="composer">FallenAngel</creator>\n'
         '    <encoding><software>FallenAngel sketch_far01.py</software></encoding>\n  </identification>\n',
         '  <part-list>\n'
         '    <score-part id="P1">\n      <part-name>Kalimba</part-name>\n'
         '      <score-instrument id="P1-I1"><instrument-name>Kalimba</instrument-name></score-instrument>\n'
         '      <midi-instrument id="P1-I1"><midi-channel>1</midi-channel>'
         '<midi-program>109</midi-program></midi-instrument>\n    </score-part>\n'
         '    <score-part id="P2">\n      <part-name>Music Box</part-name>\n'
         '      <score-instrument id="P2-I2"><instrument-name>Music Box</instrument-name></score-instrument>\n'
         '      <midi-instrument id="P2-I2"><midi-channel>2</midi-channel>'
         '<midi-program>11</midi-program></midi-instrument>\n    </score-part>\n'
         '  </part-list>\n']
    p.append(build_part("P1", KALIMBA, DYNAMICS, lead))
    p.append(build_part("P2", MUSIC_BOX, {1: ("p", 49), 17: ("p", 49)}, ""))
    p.append('</score-partwise>\n')
    return "".join(p)


def main():
    out_dir = Path(__file__).parent / "sketch_far01"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "sketch_far01.musicxml"
    out.write_text(build(), encoding="utf-8")
    print(f"wrote {out}")
    bars = len(KALIMBA)
    beats = sum(sum(d for _, d in m) for m, _ in KALIMBA) / DIV
    print(f"bars={bars} duration={beats * 60 / TEMPO:.1f}s @ {TEMPO}bpm")
    for i, shift in enumerate(PASS_SHIFT):
        lo, hi = None, None
        for base in BASE_MELODY:
            for p_, _ in base:
                t = transpose(p_, shift)
                lo = t if lo is None or midi_of(t) < midi_of(lo) else lo
                hi = t if hi is None or midi_of(t) > midi_of(hi) else hi
        print(f"  第 {i+1} 遍 +{shift} 度：旋律 {lo} ~ {hi}")


if __name__ == "__main__":
    main()
