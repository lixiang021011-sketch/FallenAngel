#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""《未完成》第一稿（独白版）—— MusicXML 生成器

与作者的约定（2026-09-24 讨论定稿）：
  * **独白版**：只有卡林巴一个声音。若「那段时间心里有过一个人」，尤克里里作为
    第二层后加 —— 本版的编排是两种选项的共同底子。
  * **音高材料 = C 调**（卡林巴的天然调，17 键 C4~E6），但**全曲不落在 C 上**：
      - 唯一摸到 C 的地方是第 5 小节的高音 C6，底下压的是 G 和弦，所以不落地；
      - 末音是 D，落在 G 上 —— 它一直在等 C，那一步在后半段。
  * **结构**（♩=66，4/4，共 17 小节 ≈ 62 秒 + 余韵）：
      引子 2 ｜ A 4（A 句落 Am，像一首完整的短歌）｜ A' 4（低音走动，收在开放的 G）
      ｜ B 4（一个一个音往上爬，到达琴的最高键 E6 就上不去了）｜ 尾声 3（回到开头，停住）
  * **可操作性**：音域 D4~E6 正好在一把 17 键卡林巴上；最多同时两音（两个拇指）；
    没有升降号；没有十六分——「第一次做」的曲子不该有炫技。

输出：draft01/motif_draft01.musicxml
"""
from pathlib import Path

DIV = 4                      # divisions：四分音符 = 4
TEMPO = 66
TITLE = "未完成 · 第一稿（独白版）"
SUBTITLE = "卡林巴独奏 ♩=66 · 熄灯之后"
EXPRESSION = "轻轻地，像怕吵到别人"
COMPOSER = "FallenAngel"

REST = None

TYPE_BY_DUR = {16: "whole", 8: "half", 4: "quarter", 2: "eighth", 1: "sixteenth"}


def split_pitch(name):
    """'E5' -> ('E', 5)"""
    return name[0], int(name[1:])


# ── 母题 ─────────────────────────────────────────────────────────────
# 前半句：E5 G5 A5 A5 —— 上行三个音再停住，是整首曲子的种子
OPENING = [("E5", 4), ("G5", 2), ("A5", 2), ("A5", 8)]
# 前半句的收尾：G5 E5 D5 E5 —— 尾声会原样再来一次，让听者以为要循环
CLOSING = [("G5", 4), ("E5", 2), ("D5", 2), ("E5", 8)]

BARS = [
    # 引子：低音稀稀落落，像手先摸到琴上
    ([(REST, 16)], [("C4", 8), ("E4", 8)]),
    ([(REST, 16)], [("G4", 8), ("E4", 8)]),
    # A：母题第一次出现，落在 Am 上 —— 像一首完整的短歌
    (list(OPENING), [("A4", 16)]),
    (list(CLOSING), [("F4", 16)]),
    ([("A5", 4), ("B5", 4), ("C6", 8)], [("G4", 16)]),      # 唯一一次摸到 C，底下是 G
    ([("B5", 4), ("A5", 4), ("A5", 8)], [("A4", 16)]),
    # A'：母题回来，低音走动，收在开放的 G 上（悬着）
    (list(OPENING), [("F4", 16)]),
    ([("G5", 4), ("A5", 4), ("B5", 8)], [("G4", 16)]),
    ([("C6", 4), ("B5", 2), ("A5", 2), ("G5", 8)], [("A4", 16)]),
    ([("E5", 8), ("G5", 8)], [("G4", 16)]),
    # B：一个一个音往上爬 —— 走到琴的最高键，就没路了
    ([("A5", 4), ("B5", 4), ("C6", 4), ("D6", 4)], [("A4", 16)]),
    ([("E6", 8), ("D6", 8)], [("F4", 16)]),                  # E6 = 17 键卡林巴的最高音
    ([("C6", 4), ("B5", 4), ("A5", 4), ("G5", 4)], [("G4", 16)]),
    ([("E5", 8), (REST, 8)], [("A4", 16)]),                  # 手停住了
    # 尾声：开头回来了，收尾也回来了 —— 然后停在 D / G 上，等一个始终没来的 C
    (list(OPENING), [("A4", 16)]),
    (list(CLOSING), [("F4", 16)]),
    ([("D5", 16)], [("G4", 16)]),
]


def note_xml(pitch, dur, voice, stem):
    if pitch is REST:
        return (
            f'      <note>\n'
            f'        <rest/>\n'
            f'        <duration>{dur}</duration>\n'
            f'        <voice>{voice}</voice>\n'
            f'        <type>{TYPE_BY_DUR[dur]}</type>\n'
            f'      </note>\n'
        )
    step, octave = split_pitch(pitch)
    return (
        f'      <note>\n'
        f'        <pitch><step>{step}</step><octave>{octave}</octave></pitch>\n'
        f'        <duration>{dur}</duration>\n'
        f'        <voice>{voice}</voice>\n'
        f'        <type>{TYPE_BY_DUR[dur]}</type>\n'
        f'        <stem>{stem}</stem>\n'
        f'      </note>\n'
    )


def build():
    parts = []
    parts.append('<?xml version="1.0" encoding="UTF-8"?>\n')
    parts.append(
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 3.1 Partwise//EN" '
        '"http://www.musicxml.org/dtds/partwise.dtd">\n'
    )
    parts.append('<score-partwise version="3.1">\n')
    parts.append(f'  <work><work-title>{TITLE}</work-title></work>\n')
    parts.append(
        '  <identification>\n'
        f'    <creator type="composer">{COMPOSER}</creator>\n'
        '    <encoding><software>FallenAngel motif_draft01.py</software></encoding>\n'
        '  </identification>\n'
    )
    parts.append(
        '  <part-list>\n'
        '    <score-part id="P1">\n'
        '      <part-name>Kalimba</part-name>\n'
        '      <score-instrument id="P1-I1"><instrument-name>Kalimba</instrument-name></score-instrument>\n'
        '      <midi-instrument id="P1-I1">\n'
        '        <midi-channel>1</midi-channel>\n'
        '        <midi-program>109</midi-program>\n'   # GM 108 = Kalimba（MusicXML 是 1-based）
        '      </midi-instrument>\n'
        '    </score-part>\n'
        '  </part-list>\n'
    )
    parts.append('  <part id="P1">\n')

    for index, (melody, bass) in enumerate(BARS, start=1):
        parts.append(f'    <measure number="{index}">\n')
        if index == 1:
            parts.append(
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

        for pitch, dur in melody:
            parts.append(note_xml(pitch, dur, 1, "up"))
        parts.append(f'      <backup><duration>{sum(d for _, d in melody)}</duration></backup>\n')
        for pitch, dur in bass:
            parts.append(note_xml(pitch, dur, 2, "down"))

        parts.append('    </measure>\n')

    parts.append('  </part>\n</score-partwise>\n')
    return "".join(parts)


def main():
    out_dir = Path(__file__).parent / "draft01"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "motif_draft01.musicxml"
    out.write_text(build(), encoding="utf-8")

    beats = sum(sum(d for _, d in m) for m, _ in BARS) / DIV
    print(f"wrote {out}")
    print(f"bars={len(BARS)} beats={beats:g} duration={beats * 60 / TEMPO:.1f}s @ {TEMPO}bpm")
    names = {p for m, b in BARS for p, _ in m + b if p}
    midi = {n: (int(n[1:]) + 1) * 12 + "C D EF G A B".index(n[0]) for n in names}
    lo, hi = min(midi, key=midi.get), max(midi, key=midi.get)
    print(f"pitch range: {lo} -> {hi} ({len(names)} distinct, C 调内无升降号)")
    print("17 键 C 调卡林巴音域 C4~E6 →", "在音域内" if lo >= "C4" and midi[hi] <= midi["E6"] else "超范围")


if __name__ == "__main__":
    main()
