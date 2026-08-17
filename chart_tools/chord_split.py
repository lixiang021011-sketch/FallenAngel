# -*- coding: utf-8 -*-
"""
和弦拆分：同一 tick ≥3 键（三/四键同击）拆到相邻空位，保证任意时刻 ≤2 键（双指可打）。
在 split_16ths 连打拆分之后运行。

用法: python chord_split.py <输入.chart> [输出.chart]

策略:
  每组保留前 2 键原位，其余键按"由近及远、先左后右"贪心找空位（±48*n tick, n=1,2,...）。
  空位条件: 目标 tick 原本无音（不制造新的同刻多键），且 ≥ 首音 tick（不落入前奏空区）。
  翻出后与相邻 tick 构成 ≤48 tick 间隔属正常交替（94ms，双指可打，与 split_16ths 同标准）。
"""
import sys
import collections


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else src

    lines = open(src, encoding='utf-8').read().splitlines()
    head, notes = [], []
    in_drums = False
    for l in lines:
        ls = l.strip()
        if ls == '[ExpertDrums]':
            in_drums = True
            continue
        if in_drums:
            if ' = N ' in ls:
                t, r = ls.split(' = N ')
                notes.append((int(t), int(r.split()[0])))
            continue
        head.append(l)

    by_tick = collections.defaultdict(list)
    for t, lane in notes:
        by_tick[t].append(lane)

    # 占用表：tick -> 键集合（后续拆分只往里加，表示"该位置已有着落"）
    occupied = collections.defaultdict(set)
    for t, lane in notes:
        occupied[t].add(lane)

    first_tick = min(t for t, _ in notes)
    last_tick = max(t for t, _ in notes)

    chords = [t for t, lanes in sorted(by_tick.items()) if len(lanes) > 2]
    if not chords:
        print(f'无 ≥3 键和弦（{len(notes)} 音），无需拆分')
        return

    reloc = {}  # (原tick, lane) -> 新tick
    for t in chords:
        lanes = by_tick[t]
        for lane in lanes[2:]:
            placed = False
            for n in range(1, 400):  # 最多 ±400*48 tick，覆盖整张谱
                for dt in (n, -n):
                    nt = t + dt * 48
                    if nt < first_tick or nt > last_tick + 48:
                        continue
                    if nt not in occupied:
                        occupied[nt].add(lane)
                        reloc[(t, lane)] = nt
                        placed = True
                        break
                if placed:
                    break
            if not placed:
                print(f'警告: tick {t} 键 {lane} 无空位，保持原位（该时刻仍 >2 键，双指不可打!）')
                reloc[(t, lane)] = t

    final = [(reloc.get((t, lane), t), lane) for t, lane in notes]
    final.sort()

    out = list(head) + ['[ExpertDrums]', '{']
    out += [f'  {t} = N {lane} 0' for t, lane in final]
    out.append('}')
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(out) + '\n')

    dist = collections.Counter(lane for t, lane in final)
    tick_counts = collections.Counter(t for t, _ in final)
    new_chords = sum(1 for c in tick_counts.values() if c > 2)
    print(f'和弦组: {len(chords)} 个 | 拆出音符: {len(reloc)} | 剩余 >2 键时刻: {new_chords}')
    print(f'新轨道分布: {dict(dist)}')
    print(f'输出: {dst}')


if __name__ == '__main__':
    main()
