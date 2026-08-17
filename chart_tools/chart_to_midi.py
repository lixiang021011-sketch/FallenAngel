# -*- coding: utf-8 -*-
"""
.chart → MIDI 转换器（供 Guitar Pro / 任意 DAW 导入打击乐轨）
用法: python chart_to_midi.py <输入.chart> [输出.mid]

轨道 → GM 鼓件: 0底鼓→36  1军鼓→38  2踩镲/嗵→42  3吊镲→49
division = 192（与 .chart tick 一致）；音符时长 = 32 分（6 tick）
"""
import sys
import struct

LANE_TO_MIDI = {0: 36, 1: 38, 2: 42, 3: 49}


def vlq(n):
    buf = [n & 0x7F]
    n >>= 7
    while n:
        buf.append(0x80 | (n & 0x7F))
        n >>= 7
    return bytes(reversed(buf))


def var_len_data(b):
    return vlq(len(b)) + b


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else src.rsplit('.', 1)[0] + '.mid'
    bpm = 160
    notes = []
    for l in open(src, encoding='utf-8'):
        l = l.strip()
        if ' = B ' in l:
            bpm = int(l.split(' = B ')[1]) / 1000
        elif ' = N ' in l:
            t, r = l.split(' = N ')
            lane = int(r.split()[0])
            notes.append((int(t), LANE_TO_MIDI.get(lane, 42)))
    notes.sort()

    tempo_us = int(60_000_000 / bpm)
    track = bytearray()
    track += b'\x00\xFF\x03' + var_len_data(b'Drums')               # 曲名
    track += b'\x00\xFF\x51\x03' + tempo_us.to_bytes(3, 'big')      # 速度
    track += b'\x00\xFF\x58\x04\x04\x02\x18\x08'                    # 4/4
    # 按 tick 分组：同 tick 全部 note-on 先行、note-off 再后（和弦同起点）
    by_tick = {}
    for tick, pitch in notes:
        by_tick.setdefault(tick, []).append(pitch)
    prev_end = 0
    for tick in sorted(by_tick):
        pitches = by_tick[tick]
        for i, pitch in enumerate(pitches):
            track += vlq(tick - prev_end if i == 0 else 0) + bytes([0x99, pitch, 100])
        for i, pitch in enumerate(pitches):
            track += vlq(6 if i == 0 else 0) + bytes([0x89, pitch, 0])
        prev_end = tick + 6
    track += b'\x00\xFF\x2F\x00'                                    # EOT

    header = b'MThd' + struct.pack('>IHHH', 6, 0, 1, 192)           # 格式0 单轨 192ppq
    tchunk = b'MTrk' + struct.pack('>I', len(track)) + bytes(track)
    with open(dst, 'wb') as f:
        f.write(header + tchunk)
    print(f'MIDI 已生成: {dst} ({len(notes)} 音符, {bpm:g} BPM)')


if __name__ == '__main__':
    main()
