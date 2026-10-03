#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""1456 舒缓民谣伴奏（草稿）—— MusicXML 生成器

作者要求（2026-09-24）：「做一个 1456 的舒缓的民谣伴奏」。

  1456 = I - IV - V - vi（C 调即 C - F - G - Am）。
  民谣编配：指弹吉他分解和弦 + 贝斯 + 副歌扫弦吉他 + 尾段弦乐。

设计取舍（都写在谱里，方便逐条改）：

  1. 调性取 C 大调、♩=72、4/4。72 是"舒缓"的常用档，也刚好让十六分音符
     （93ms）落在游戏 ±80ms 的 PERFECT 窗口之外 —— 音符不会挤在一起。
  2. 正句一律是 C-F-G-Am（作者要的 1456）；收束句（副歌第二句、尾奏）
     把最后两小节换成 C，让 4 小节循环有落点，而不是每次都停在 vi 上回头看。
  3. 贝斯是**上行**的：C2(36) → F2(41) → G2(43) → A2(45)，每小节第 3 拍再往上走一个五度。
     低音向上 = 向前看。这条沿用了 sketch_far01 的诊断（向下掉就是回头看）。
  4. 指弹用一条"波浪"分解型（低—中—高—最高—回落），每小节 8 个八分音符填满，
     音区压在 C3~A4 —— 把中高音区让给人声。
  5. 段落靠**乐器进出**做力度，不靠加音量：前奏只有吉他，主歌加贝斯，
     副歌加扫弦，第二遍副歌才加弦乐。符合第一章"不写燃、不写压迫感"的基调。

输出：music_tools/folk_1456/folk_1456.musicxml
"""
from pathlib import Path

DIV = 4                      # divisions：四分音符 = 4
BEAT = DIV
BAR = DIV * 4
TEMPO = 72
TITLE = "一四五六 · 民谣伴奏（草稿）"
SUBTITLE = "指弹吉他 / 贝斯 / 扫弦吉他 / 弦乐 · 4/4 · C 大调"
EXPRESSION = "舒缓，像在慢慢走"
COMPOSER = "FallenAngel"

TYPE_BY_DUR = {16: "whole", 8: "half", 4: "quarter", 2: "eighth", 1: "sixteenth"}
NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def name_of(value):
    return NAMES[value % 12] + str(value // 12 - 1)


# 每个和弦：stack = 吉他上的五个可弹音（低→高），bass = 第 1 拍 / 第 3 拍的贝斯音
CHORDS = {
    "C":  {"stack": [48, 55, 60, 64, 67], "bass": [36, 43]},
    "F":  {"stack": [53, 57, 60, 65, 69], "bass": [41, 48]},
    "G":  {"stack": [50, 55, 59, 62, 67], "bass": [43, 50]},
    "Am": {"stack": [52, 57, 60, 64, 69], "bass": [45, 52]},
}

# 指弹分解型：下上下上（低—中—高—最高—回落），8 个八分音符
WAVE = [0, 1, 2, 3, 4, 3, 2, 1]

MAIN = ["C", "F", "G", "Am"]      # 1456 正句
CLOSE = ["C", "F", "G", "C"]      # 收束句：还是 1-4-5，落在 1
OUTRO = ["F", "G", "C", "C"]

BAR_CHORDS = (
    MAIN                                  # 1-4    前奏
    + MAIN + MAIN                         # 5-12   主歌两句
    + MAIN + CLOSE                        # 13-20  副歌两句
    + MAIN                                # 21-24  间奏
    + MAIN + MAIN                         # 25-32  主歌两句（第二遍）
    + MAIN + CLOSE                        # 33-40  副歌两句（第二遍）
    + OUTRO                               # 41-44  尾奏
)
TOTAL_BARS = len(BAR_CHORDS)


def arpeggio_bar(chord, last=False):
    """指弹：8 个八分音符；末小节改成一个整小节的和弦（让它自己响完）"""
    stack = CHORDS[chord]["stack"]
    if last:
        return [(0, BAR, list(stack))]
    return [(i * 2, 2, [stack[k]]) for i, k in enumerate(WAVE)]


def bass_bar(chord):
    """贝斯：两个二分音符 —— 第 1 拍根音，第 3 拍五度（整体上行）"""
    low, fifth = CHORDS[chord]["bass"]
    return [(0, BEAT * 2, [low]), (BEAT * 2, BEAT * 2, [fifth])]


def strum_bar(chord):
    """扫弦：两个二分音符的和弦块，低一点 → 高一点，像轻轻扫两下"""
    s = CHORDS[chord]["stack"]
    return [(0, BEAT * 2, [s[1], s[2], s[3]]), (BEAT * 2, BEAT * 2, [s[2], s[3], s[4]])]


def pad_bar(chord):
    """弦乐：整小节的开放五度长音，只做底色"""
    s = CHORDS[chord]["stack"]
    return [(0, BAR, [s[2], s[4]])]


PARTS = [
    {
        "id": "P1", "name": "Guitar (fingerstyle)", "program": 25, "channel": 1,
        "clef": ("G", 2), "stem": "up", "voice": 1,
        "active": lambda b: True,
        "events": lambda b, ch: arpeggio_bar(ch, last=(b == TOTAL_BARS)),
        "dynamics": {1: ("p", 49), 5: ("mp", 64), 13: ("mf", 71), 21: ("p", 49),
                     25: ("mp", 64), 33: ("mf", 74), 41: ("p", 52)},
    },
    {
        "id": "P2", "name": "Acoustic Bass", "program": 33, "channel": 2,
        "clef": ("F", 4), "stem": "down", "voice": 1,
        "active": lambda b: (5 <= b <= 20) or (25 <= b <= 43),
        "events": lambda b, ch: bass_bar(ch),
        "dynamics": {5: ("mp", 64), 13: ("mf", 71), 25: ("mp", 64), 33: ("mf", 74),
                     41: ("p", 52)},
    },
    {
        "id": "P3", "name": "Guitar (strum)", "program": 26, "channel": 3,
        "clef": ("G", 2), "stem": "up", "voice": 1,
        "active": lambda b: (13 <= b <= 20) or (33 <= b <= 44),
        "events": lambda b, ch: strum_bar(ch),
        "dynamics": {13: ("mf", 68), 33: ("mf", 74), 41: ("mp", 60)},
    },
    {
        "id": "P4", "name": "Strings", "program": 49, "channel": 4,
        "clef": ("G", 2), "stem": "down", "voice": 1,
        "active": lambda b: 33 <= b <= 44,
        "events": lambda b, ch: pad_bar(ch),
        "dynamics": {33: ("mp", 58), 41: ("p", 49)},
    },
]


def split_pitch(value):
    name = name_of(value)
    return name[0], int(name[1:])


def note_xml(pitches, dur, voice, stem, chord_tail=False):
    out = []
    for index, value in enumerate(pitches):
        step, octave = split_pitch(value)
        out.append("      <note>\n")
        if index > 0:
            out.append("        <chord/>\n")
        out.append(
            f'        <pitch><step>{step}</step><octave>{octave}</octave></pitch>\n'
        )
        out.append(f"        <duration>{dur}</duration>\n")
        out.append(f"        <voice>{voice}</voice>\n")
        out.append(f"        <type>{TYPE_BY_DUR[dur]}</type>\n")
        if stem:
            out.append(f"        <stem>{stem}</stem>\n")
        out.append("      </note>\n")
    return "".join(out)


def rest_bar_xml(voice):
    return (
        "      <note>\n        <rest measure=\"yes\"/>\n"
        f"        <duration>{BAR}</duration>\n"
        f"        <voice>{voice}</voice>\n      </note>\n"
    )


def dynamic_xml(mark, value):
    return (
        '      <direction placement="below">\n'
        f"        <direction-type><dynamics><{mark}/></dynamics></direction-type>\n"
        f'        <sound dynamics="{value}"/>\n'
        "      </direction>\n"
    )


def build_part(part, leading):
    p = [f'  <part id="{part["id"]}">\n']
    for bar in range(1, TOTAL_BARS + 1):
        chord = BAR_CHORDS[bar - 1]
        p.append(f'    <measure number="{bar}">\n')
        if bar == 1:
            sign, line = part["clef"]
            p.append(
                "      <attributes>\n"
                f"        <divisions>{DIV}</divisions>\n"
                "        <key><fifths>0</fifths><mode>major</mode></key>\n"
                "        <time><beats>4</beats><beat-type>4</beat-type></time>\n"
                f"        <clef><sign>{sign}</sign><line>{line}</line></clef>\n"
                "      </attributes>\n"
            )
            p.append(leading)
        mark = part["dynamics"].get(bar)
        if mark:
            p.append(dynamic_xml(*mark))

        if not part["active"](bar):
            p.append(rest_bar_xml(part["voice"]))
        else:
            written = 0
            for offset, dur, pitches in part["events"](bar, chord):
                # 每个分段都写满整小节（分解型 8×2、贝斯 8+8、扫弦 8+8、弦乐 16），不留空隙
                if offset != written:
                    raise AssertionError(
                        f'{part["id"]} 第 {bar} 小节在 {written} 处出现空隙（下一个音在 {offset}）'
                    )
                p.append(note_xml(pitches, dur, part["voice"], part["stem"]))
                written = offset + dur
            if written != BAR:
                raise AssertionError(f'{part["id"]} 第 {bar} 小节只有 {written}/{BAR} 个 division')
        p.append("    </measure>\n")
    p.append("  </part>\n")
    return "".join(p)


def build():
    lead = (
        '      <direction placement="above">\n'
        "        <direction-type><metronome>"
        f"<beat-unit>quarter</beat-unit><per-minute>{TEMPO}</per-minute>"
        "</metronome></direction-type>\n"
        f'        <sound tempo="{TEMPO}"/>\n'
        "      </direction>\n"
        "      <direction placement=\"above\">\n"
        f"        <direction-type><words>{EXPRESSION}</words></direction-type>\n"
        "      </direction>\n"
    )
    p = [
        '<?xml version="1.0" encoding="UTF-8"?>\n',
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 3.1 Partwise//EN" '
        '"http://www.musicxml.org/dtds/partwise.dtd">\n',
        '<score-partwise version="3.1">\n',
        f"  <work><work-title>{TITLE}</work-title></work>\n",
        '  <movement-title>' + TITLE + "</movement-title>\n",
        "  <identification>\n"
        f'    <creator type="composer">{COMPOSER}</creator>\n'
        "    <encoding><software>FallenAngel folk_1456.py</software></encoding>\n"
        "  </identification>\n",
        "  <part-list>\n",
    ]
    for part in PARTS:
        pid = part["id"]
        p.append(
            f'    <score-part id="{pid}">\n'
            f'      <part-name>{part["name"]}</part-name>\n'
            f'      <score-instrument id="{pid}-I1">'
            f'<instrument-name>{part["name"]}</instrument-name></score-instrument>\n'
            f'      <midi-instrument id="{pid}-I1">\n'
            f'        <midi-channel>{part["channel"]}</midi-channel>\n'
            f'        <midi-program>{part["program"]}</midi-program>\n'
            "      </midi-instrument>\n"
            "    </score-part>\n"
        )
    p.append("  </part-list>\n")
    for part in PARTS:
        p.append(build_part(part, lead if part["id"] == "P1" else ""))
    p.append("</score-partwise>\n")
    return "".join(p)


def main():
    out_dir = Path(__file__).parent / "folk_1456"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "folk_1456.musicxml"
    out.write_text(build(), encoding="utf-8")

    print(f"wrote {out}")
    print(f"bars={TOTAL_BARS}  ♩={TEMPO}  {TOTAL_BARS * 4 * 60 / TEMPO:.1f}s")
    for part in PARTS:
        bars = [b for b in range(1, TOTAL_BARS + 1) if part["active"](b)]
        span = f"{bars[0]}-{bars[-1]}" if bars else "-"
        print(f'  {part["id"]} {part["name"]:<22} 出场 {len(bars):2d} 小节（{span}）')
    for bar, chord in enumerate(BAR_CHORDS, start=1):
        if bar % 4 == 1:
            phrase = BAR_CHORDS[bar - 1:bar + 3]
            print(f"    第 {bar:2d}-{bar + 3:2d} 小节：{' '.join(phrase)}")


if __name__ == "__main__":
    main()
