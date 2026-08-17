# -*- coding: utf-8 -*-
"""
Easy 谱面生成：从双指版抽稀，用于 Unity 内手感测试（降低难度保证测试可玩）。
规则:
  - kick（lane 0）全部保留（任意键机制，不占手指，不增加难度）
  - 普通键（lane 1-3）：每 96 tick（8 分音符）窗口内最多保留 1 个
用法: python easy_chart.py <输入.chart> <输出.chart>
"""
import sys
import collections


def main():
    src = sys.argv[1]
    dst = sys.argv[2]

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

    notes.sort()
    kept = []
    window_start = -10 ** 9  # 普通键窗口起点（滚动式）
    for t, lane in notes:
        if lane == 0:
            kept.append((t, lane))  # kick 全保留
            continue
        if t - window_start >= 96:
            window_start = t
            kept.append((t, lane))
        # 窗口内已有普通键 -> 丢弃

    kept.sort()
    out = list(head) + ['[ExpertDrums]', '{']
    out += [f'  {t} = N {lane} 0' for t, lane in kept]
    out.append('}')
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(out) + '\n')

    print(f'原 {len(notes)} 音 -> Easy {len(kept)} 音')
    dist = collections.Counter(lane for t, lane in kept)
    print(f'轨道分布: {dict(dist)}')
    print(f'输出: {dst}')


if __name__ == '__main__':
    main()
