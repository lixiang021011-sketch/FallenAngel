# -*- coding: utf-8 -*-
"""
MuseScore .mscz（鼓组声部）→ .chart 转换器
用法: python mscz_to_chart.py <输入.mscz> [输出.chart] [--offset 0.929]

鼓件映射（GM MIDI → 轨道）: 35/36底鼓→0  37/38/40军鼓→1
  41/43/45/47/48/50嗵鼓、42/44/46踩镲→2  49/51/52/55/57/59吊镲类→3
自动判定偏移：首音符在第13小节→0.929s（前奏12小节）；在第1小节→-17.071s
"""
import sys
import zipfile
import xml.etree.ElementTree as ET
from fractions import Fraction

# 4 轨布局：0=底鼓 1=军鼓 2=踩镲 3=吊镲+嗵鼓（绿键）
LANE_MAP = {35: 0, 36: 0, 37: 1, 38: 1, 40: 1,
            42: 2, 44: 2, 46: 2,
            41: 3, 43: 3, 45: 3, 47: 3, 48: 3, 50: 3,
            49: 3, 51: 3, 52: 3, 55: 3, 57: 3, 59: 3}
DTYPE = {'whole': '1', 'half': '1/2', 'quarter': '1/4', 'eighth': '1/8',
         '16th': '1/16', '32nd': '1/32', '64th': '1/64', '128th': '1/128'}
DRUM_ENTRY = 17.071  # 音频进鼓时刻（s）


def dur_frac(el):
    d = el.findtext('duration')
    if d:
        try:
            return Fraction(d)
        except Exception:
            pass
    dt = el.findtext('durationType', 'quarter')
    f = Fraction(DTYPE.get(dt, '1/4'))
    dots = el.findtext('dots')
    if dots:
        for _ in range(int(dots)):
            f = f * Fraction(3, 2)
    return f


def main():
    args = sys.argv[1:]
    src = args[0]
    dst = args[1] if len(args) > 1 and not args[1].startswith('--') else src.rsplit('.', 1)[0] + '.chart'
    offset = None
    if '--offset' in args:
        offset = float(args[args.index('--offset') + 1])

    z = zipfile.ZipFile(src)
    mscx_name = [n for n in z.namelist() if n.endswith('.mscx')][0]
    root = ET.fromstring(z.read(mscx_name))

    title = root.findtext('.//metaTag[@name="workTitle"]', 'converted')
    bpm = 160.0
    tempo_el = root.find('.//Tempo')
    if tempo_el is not None and tempo_el.findtext('tempo'):
        bpm = float(tempo_el.findtext('tempo')) * 60
    if '--bpm' in args:
        bpm = float(args[args.index('--bpm') + 1])
    ts = root.find('.//TimeSig')
    sigN = int(ts.findtext('sigN', '4')) if ts is not None else 4
    sigD = int(ts.findtext('sigD', '4')) if ts is not None else 4
    measure_frac = Fraction(sigN, sigD)

    notes = []
    real_meas = 0          # 真实小节号（多小节休止展开后）
    first_note_meas = None
    max_meas = 0
    tuplets = 0
    merged = 0
    for staff in root.iter('Staff'):
        for meas in staff.findall('Measure'):
            real_meas += 1
            if meas.findtext('len') is not None:
                print(f'警告: 第{real_meas}小节有非标准长度 len={meas.findtext("len")}')
            for voice in meas.findall('voice'):
                cur = Fraction(0)
                stack = []  # 连音栈: (缩放比, 剩余音符数)
                for el in voice:
                    if el.tag == 'Tuplet':
                        nn = int(el.findtext('normalNotes', '2'))
                        an = int(el.findtext('actualNotes', '3'))
                        stack.append((Fraction(nn, an), an))
                        tuplets += 1
                        continue
                    if el.tag not in ('Chord', 'Rest'):
                        continue
                    dur = dur_frac(el)
                    for sc, rem in stack:
                        dur = dur * sc
                    if el.tag == 'Chord':
                        for note in el.findall('Note'):
                            pitch = int(note.findtext('pitch', '-1'))
                            lane = LANE_MAP.get(pitch)
                            if lane is not None:
                                tick = round(float((real_meas - 1) * measure_frac + cur) * 768)
                                notes.append((tick, lane, pitch))
                                if first_note_meas is None:
                                    first_note_meas = real_meas
                                max_meas = max(max_meas, real_meas)
                    cur += dur
                    if stack:
                        stack[-1] = (stack[-1][0], stack[-1][1] - 1)
                        if stack[-1][1] <= 0:
                            stack.pop()
            # 多小节休止符：填充超过 1 小节的部分 = 被吸收的后续小节
            fill = Fraction(0)
            for voice2 in meas.findall('voice'):
                c2 = Fraction(0)
                for el2 in voice2:
                    if el2.tag in ('Chord', 'Rest'):
                        c2 += dur_frac(el2)
                fill = max(fill, c2)
            extra = round(float(fill) - 1.0)
            if extra > 0:
                merged += extra
                real_meas += extra
    print(f'多小节休止展开: 吸收 {merged} 小节（XML 141 → 真实 {real_meas} 小节）')

    notes.sort()
    # 小节平移：使首音符落在 m13（前奏 12 空小节），避免负 Offset
    shift_meas = 0
    if first_note_meas is not None and first_note_meas < 13:
        shift_meas = 13 - first_note_meas
        notes = [(t + shift_meas * 768, lane, pitch) for t, lane, pitch in notes]
        max_meas += shift_meas
        print(f'前奏已删（首音符在第{first_note_meas}小节）→ 整体后移 {shift_meas} 小节，进鼓对齐 m13')
    if offset is None:
        offset = 0.929

    lines = ['[Song]', '{', f'  Name = "{title}"', '  Artist = "dazbee cover"',
             '  Charter = "mscz converted"', f'  Offset = {offset}', '  Resolution = 192',
             '  Player2 = bass', '  Difficulty = 0', '  PreviewStart = 0', '  PreviewEnd = 0',
             '  Genre = "rock"', '  MediaType = "cd"', '}', '[SyncTrack]', '{',
             f'  0 = TS {sigN}', f'  0 = B {int(round(bpm*1000))}', '}', '[Events]', '{', '}',
             '[ExpertDrums]', '{']
    for tick, lane, pitch in notes:
        lines.append(f'  {tick} = N {lane} 0')
    lines.append('}')
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print(f'转换完成: {len(notes)} 音符, {max_meas} 小节, BPM={bpm:g}, 拍号 {sigN}/{sigD}')
    print(f'输出: {dst}')


if __name__ == '__main__':
    main()
