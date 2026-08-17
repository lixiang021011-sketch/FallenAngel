# -*- coding: utf-8 -*-
"""
16 分连打拆分：同轨连续 16 分（48 tick）连打段拆到对偶轨交替
用法: python split_16ths.py <输入.chart> [输出.chart] [--min-run N]
对偶: 0↔3, 1↔2（跨中交替）；--min-run 默认 2（双击也拆，160bpm 双击单指打不了）；碰撞时保持原位
[ExpertDrums] 之前的所有段落原样保留（含 [Song]/[SyncTrack]/[Events]）
"""
import sys
import collections

PAIR = {0: 3, 3: 0, 1: 2, 2: 1}


def main():
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else src.rsplit('.', 1)[0] + '_双指版.chart'
    min_run = 2
    if '--min-run' in sys.argv:
        min_run = int(sys.argv[sys.argv.index('--min-run') + 1])

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

    # 每轨 16 分连打段检测
    per_lane = collections.defaultdict(list)
    for t, lane in notes:
        per_lane[lane].append(t)
    runs = []
    for lane, tl in per_lane.items():
        tl.sort()
        i = 0
        while i < len(tl):
            j = i
            while j + 1 < len(tl) and tl[j + 1] - tl[j] == 48:
                j += 1
            if j - i + 1 >= min_run:
                runs.append((lane, tl[i:j + 1]))
            i = j + 1

    # 贪心翻转：对每个连打段，从左到右尝试把音翻到对偶轨。
    # 翻入条件：对偶轨同刻无音（碰撞不翻），且对偶轨 ±48 tick 无音（不制造新的 94ms 间隔）
    per = collections.defaultdict(set)
    for t, lane in notes:
        per[lane].add(t)
    flipped = collisions = 0
    for lane, r in runs:
        p = PAIR[lane]
        for t in r:
            if t not in per[lane]:  # 已被前段翻走
                continue
            if t in per[p] or (t - 48) in per[p] or (t + 48) in per[p]:
                if t in per[p]:
                    collisions += 1
                continue
            per[lane].remove(t)
            per[p].add(t)
            flipped += 1

    final = []
    for t, lane in notes:
        if t in per[lane]:
            final.append((t, lane))
        else:
            final.append((t, PAIR[lane]))
    final.sort()

    out = list(head) + ['[ExpertDrums]', '{']
    out += [f'  {t} = N {lane} 0' for t, lane in final]
    out.append('}')
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(out) + '\n')

    dist = collections.Counter(lane for t, lane in final)
    min_gap = min((per_lane[l][i + 1] - per_lane[l][i]
                   for l in per_lane for i in range(len(per_lane[l]) - 1)
                   if per_lane[l][i + 1] > per_lane[l][i]), default=48)
    print(f'连打段: {len(runs)} 个 | 翻转音符: {flipped} | 碰撞保留: {collisions}')
    print(f'新轨道分布: {dict(dist)}')
    if min_gap < 48:
        print(f'警告: 存在 {min_gap} tick 间隔（快于16分），未处理')
    print(f'输出: {dst}')


if __name__ == '__main__':
    main()
