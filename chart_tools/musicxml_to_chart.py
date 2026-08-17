# -*- coding: utf-8 -*-
"""
MusicXML(鼓组声部) → .chart 转换器
用法: python musicxml_to_chart.py <输入.musicxml> <输出.chart> [--offset 0.929] [--bpm 160]

MIDI 鼓件 → 轨道映射:
  35/36 底鼓 → 0; 37/38/40 军鼓 → 1
  41/43/45/47/48/50 嗵鼓、42/44/46 踩镲 → 2
  49/51/52/55/57/59 吊镲类 → 3
无 midi-unpitched 时按标准鼓组显示位置回退:
  F4=底鼓 A4=地桶 C5=军鼓 D5=2桶 E5=1桶 G5=踩镲 A5=吊镲 F5=Ride
"""
import sys
import xml.etree.ElementTree as ET

LANE_MAP = {35: 0, 36: 0, 37: 1, 38: 1, 40: 1,
            41: 2, 43: 2, 45: 2, 47: 2, 48: 2, 50: 2, 42: 2, 44: 2, 46: 2,
            49: 3, 51: 3, 52: 3, 55: 3, 57: 3, 59: 3}
DISPLAY_MAP = {('F', 4): 36, ('A', 4): 43, ('C', 5): 38, ('D', 5): 48,
               ('E', 5): 50, ('G', 5): 42, ('A', 5): 49, ('F', 5): 51,
               ('B', 4): 47}


def resolve_midi(note, instr_map):
    inst = note.find('instrument')
    if inst is not None:
        mid = instr_map.get(inst.get('id'))
        if mid is not None:
            return mid
    up = note.find('unpitched')
    if up is not None:
        step = up.findtext('display-step', '').strip()
        octv = int(up.findtext('display-octave', '0'))
        return DISPLAY_MAP.get((step, octv), 42)
    pitch = note.find('pitch')
    if pitch is not None:
        step = pitch.findtext('step', '').strip()
        octv = int(pitch.findtext('octave', '0'))
        return DISPLAY_MAP.get((step, octv), 42)
    return None


def main():
    args = [a for a in sys.argv[1:]]
    src, dst = args[0], args[1]
    offset = 0.929
    bpm = 160
    ts_beats, ts_type = 4, 4
    if '--offset' in args:
        offset = float(args[args.index('--offset') + 1])
    if '--bpm' in args:
        bpm = float(args[args.index('--bpm') + 1])

    tree = ET.parse(src)
    root = tree.getroot()

    # 乐器表: id -> midi-unpitched
    instr_map = {}
    for mi in root.iter('midi-instrument'):
        up = mi.find('midi-unpitched')
        if up is not None:
            instr_map[mi.get('id')] = int(up.text)

    divisions = None
    notes = []
    measures = 0
    for part in root.findall('part'):
        for meas in part.findall('measure'):
            measures += 1
            meas_num = int(meas.get('number', str(measures)))
            cur = 0
            last_dur = 0
            for el in list(meas):
                if el.tag == 'backup':
                    cur -= int(el.findtext('duration', '0'))
                    continue
                if el.tag == 'forward':
                    cur += int(el.findtext('duration', '0'))
                    continue
                if el.tag != 'note':
                    continue
                note = el
                if note.find('rest') is not None:
                    if note.find('chord') is None:
                        cur += int(note.findtext('duration', '0'))
                    continue
                midi = resolve_midi(note, instr_map)
                dur = int(note.findtext('duration', '1'))
                pos = cur
                if note.find('chord') is not None:
                    pos = cur - last_dur   # 和弦音回到上一音符起点
                if midi is not None:
                    lane = LANE_MAP.get(midi, 2)
                    notes.append((meas_num, pos, dur, lane))
                if note.find('chord') is None:
                    cur += dur
                    last_dur = dur
            if divisions is None:
                att = meas.find('attributes')
                if att is not None and att.findtext('divisions'):
                    divisions = int(att.findtext('divisions'))

    if divisions is None:
        divisions = 4
    ticks = [(round((meas_num - 1) * 768 + cur / divisions * 192), lane)
             for meas_num, cur, dur, lane in notes]
    ticks.sort()

    lines = ['[Song]', '{', '  Name = "converted"', '  Artist = ""', '  Charter = ""',
             f'  Offset = {offset}', '  Resolution = 192', '  Player2 = bass',
             '  Difficulty = 0', '  PreviewStart = 0', '  PreviewEnd = 0',
             '  Genre = "rock"', '  MediaType = "cd"', '}', '[SyncTrack]', '{',
             f'  0 = TS {ts_beats}', f'  0 = B {int(bpm*1000)}', '}', '[Events]', '{', '}',
             '[ExpertDrums]', '{']
    for tick, lane in ticks:
        lines.append(f'  {tick} = N {lane} 0')
    lines.append('}')
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    print(f'转换完成: {len(ticks)} 音符, {measures} 小节, divisions={divisions}')
    print(f'offset={offset}s, bpm={bpm}')


if __name__ == '__main__':
    main()
