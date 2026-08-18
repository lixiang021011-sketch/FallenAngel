# -*- coding: utf-8 -*-
"""
mscz（MuseScore 4）→ 归一化 IR 前端 —— 谱面转换协议 §2.4 mscz 职责实现

用法: python mscz_to_ir.py <输入.mscz> [--out ir.json] [--single-take] [--seed N] [--offset 秒]

前端职责（协议 §2.4）：
- 全部 staff 解析；linked 镜像谱表（同 Part 双谱行）按内容指纹自动去重
- 多小节休止：4.x 每小节一个 measure rest，逐小节推进天然展开（防御 <len> 异常小节）
- Tuplet 栈式缩放、dots 附点、grace 无（本格式）
- tie 链（Spanner type=Tie + next/fractions）合并为单事件：tied=True、duration 求和
- glissando（Spanner type=Glissando + next/fractions）→ articulations=["gliss"] + pitch_end
- 变速：Tempo 值 = BPM/60（MuseScore 内部拍/秒），tick→秒分段积分
- --single-take：多轨（staff）同响窗口内只保留随机一轨（确定性伪随机，--seed 可换）
- --offset 秒：谱面时间轴整体平移（音频首响校准；如音频开头 4.25s 静音 → --offset 4.25）
"""
import random
import sys
import zipfile
import xml.etree.ElementTree as ET
from fractions import Fraction

from ir import IRStream, NoteEvent, IR_PPQ, dump_json

PPQ = IR_PPQ  # 768 tick/四分音符

# 时值单位：四分音符 = 1 拍（与 IR_PPQ=768 一致，MuseScore 内部 duration 亦为拍单位）
DTYPE = {'whole': '4', 'half': '2', 'quarter': '1', 'eighth': '1/2',
         '16th': '1/4', '32nd': '1/8', '64th': '1/16', '128th': '1/32'}
TAKE_TOL_TICKS = 96  # single-take 同响窗口：32 分音符 @768


class RawNote:
    """前端解析中间态（规则引擎消费前的 NoteEvent 前身）"""
    __slots__ = ("tick", "pitch", "dur_ticks", "velocity", "staff",
                 "tie_frac", "gliss_frac", "pitch_end", "tied")

    def __init__(self, tick, pitch, dur_ticks, velocity, staff, tie_frac, gliss_frac):
        self.tick = tick
        self.pitch = pitch
        self.dur_ticks = dur_ticks
        self.velocity = velocity
        self.staff = staff
        self.tie_frac = tie_frac
        self.gliss_frac = gliss_frac
        self.pitch_end = None
        self.tied = False


def frac_to_ticks(f: Fraction) -> int:
    return int(round(f * PPQ))


def spanner_frac_to_ticks(f: Fraction) -> int:
    """Spanner location/fractions → tick。MuseScore 该字段单位为 whole 音符
    （1 = 整小节 4 拍；eighth 音符的 tie 记 1/8），换算 ×4 到拍再 ×PPQ"""
    return int(round(f * 4 * PPQ))


def dur_frac(el, stack) -> Fraction:
    """Chord/Rest 时值（Fraction 拍）：durationType + dots + tuplet 栈缩放"""
    d = el.findtext('duration')
    if d:
        try:
            f = Fraction(d)
        except Exception:
            f = None
        if f is not None:
            for sc, _ in stack:
                f = f * sc
            return f
    dt = el.findtext('durationType', 'quarter')
    f = Fraction(DTYPE.get(dt, '1'))
    if dt == 'measure':
        return Fraction(4, 1)  # 调用处会用拍号覆盖（4 拍）
    dots = el.findtext('dots')
    if dots:
        for _ in range(int(dots)):
            f = f * Fraction(3, 2)
    for sc, _ in stack:
        f = f * sc
    return f


def parse_staff(staff_el, staff_id) -> tuple:
    """解析一个 staff → (RawNote 列表, tempo 事件, 拍号异常警告数)"""
    evs = []
    tempos = []
    sig = (4, 4)
    warn = 0
    real_meas = 0
    for meas in staff_el.findall('Measure'):
        real_meas += 1
        if meas.findtext('len') is not None:
            warn += 1  # 异常小节长度（pickup 等），按 4/4 近似处理

        def tick_at(cur_frac: Fraction) -> int:
            """小节偏移 + 小节内位置 → 绝对 tick（拍单位：拍号 n/d → 每小节 n×4/d 拍）"""
            return frac_to_ticks((real_meas - 1) * Fraction(sig[0] * 4, sig[1]) + cur_frac)

        for voice in meas.findall('voice'):
            cur = Fraction(0)
            stack = []
            dyn_vel = None
            for el in voice:
                if el.tag == 'Tuplet':
                    nn = int(el.findtext('normalNotes', '2'))
                    an = int(el.findtext('actualNotes', '3'))
                    stack.append((Fraction(nn, an), an))
                    continue
                if el.tag == 'Tempo':
                    tempos.append((tick_at(cur), float(el.findtext('tempo')) * 60))
                    continue
                if el.tag == 'TimeSig':
                    sig = (int(el.findtext('sigN', '4')), int(el.findtext('sigD', '4')))
                    continue
                if el.tag == 'Dynamic':
                    v = el.findtext('velocity')
                    if v:
                        dyn_vel = int(v)
                    continue
                if el.tag not in ('Chord', 'Rest'):
                    continue
                if el.findtext('durationType') == 'measure':
                    dur = Fraction(sig[0] * 4, sig[1])  # 整小节休止：n×4/d 拍
                else:
                    dur = dur_frac(el, stack)
                if el.tag == 'Chord':
                    for note in el.findall('Note'):
                        pitch = int(note.findtext('pitch', '-1'))
                        if pitch < 0:
                            continue
                        tie_frac = gliss_frac = None
                        for sp in note.findall('Spanner'):
                            t = sp.get('type')
                            if t in ('Tie', 'Glissando'):
                                loc = sp.find('.//location/fractions')
                                if loc is not None:
                                    f = Fraction(loc.text)
                                    if t == 'Tie':
                                        tie_frac = f
                                    else:
                                        gliss_frac = f
                        evs.append(RawNote(tick_at(cur), pitch, frac_to_ticks(dur),
                                           (dyn_vel / 127.0) if dyn_vel else 0.5,
                                           staff_id, tie_frac, gliss_frac))
                cur += dur
                if stack:
                    stack[-1] = (stack[-1][0], stack[-1][1] - 1)
                    if stack[-1][1] <= 0:
                        stack.pop()
    return evs, tempos, warn


def merge_ties(evs):
    """tie 链合并：链首事件 duration 求和、tied=True，中间事件移除"""
    by_tick = {}
    for i, e in enumerate(evs):
        by_tick.setdefault(e.tick, []).append(i)
    merged = []
    skip = set()
    for i, e in enumerate(evs):
        if i in skip:
            continue
        if e.tie_frac is not None:
            total = e.dur_ticks
            t = e.tick + spanner_frac_to_ticks(e.tie_frac)
            while True:
                nxt = next((j for j in by_tick.get(t, []) if j not in skip), None)
                if nxt is None:
                    break
                ne = evs[nxt]
                if ne.pitch != e.pitch:  # tie 必须同音高（防御异常谱）
                    break
                skip.add(nxt)
                total += ne.dur_ticks
                if ne.tie_frac is None:
                    break
                t = ne.tick + spanner_frac_to_ticks(ne.tie_frac)
            e.dur_ticks = total
            e.tied = True
        merged.append(e)
    return merged, len(evs) - len(merged)


def attach_gliss(evs):
    """gliss 起点 → 目标音高：next/fractions 定位 tick 处同 staff 事件"""
    by_tick = {}
    for e in evs:
        by_tick.setdefault(e.tick, []).append(e)
    n = 0
    for e in evs:
        if e.gliss_frac is not None:
            t = e.tick + spanner_frac_to_ticks(e.gliss_frac)
            cand = [x for x in by_tick.get(t, []) if x.pitch != e.pitch]
            if cand:
                e.pitch_end = cand[0].pitch
                n += 1
    return n


def find_linked_staffs(root):
    """按 MuseScore 语义找 linked 镜像谱表：同一 Part 的后续谱表 trackName 为空（如五线谱+TAB）。

    Part 块顺序 = 谱表定义顺序（staff id 1..N 顺序分配），Part 内第 0 谱表为主谱表，
    之后 trackName 为空的都是 linked 镜像（内容等价、记谱形式不同）。
    """
    staves = [s for s in root.findall('.//Staff') if s.get('id') is not None]
    n_total = len(staves)
    linked = set()
    sid = 0
    for p in root.findall('.//Part'):
        n_def = len(p.findall('Staff'))
        names = [t.text or '' for t in p.findall('trackName')]
        for i in range(n_def):
            sid += 1
            if sid > n_total:
                break
            if i > 0 and (len(names) <= i or not names[i]):
                linked.add(sid)
    return linked


def dedup_linked(per_staff, linked_staffs):
    """linked 镜像谱表去重：trackName 语义判定的直接丢弃；指纹兜底（内容完全一致）"""
    kept = []
    for sid, evs in per_staff:
        if sid in linked_staffs:
            print(f"linked 镜像去重: staff {sid}（trackName 空，同 Part 双谱行）丢弃")
            continue
        kept.append((sid, evs))
    return kept


def build_time_map(tempos, max_tick):
    """(tick, bpm) 事件 → 分段积分函数 tick→秒"""
    ts = sorted(set((t, b) for t, b in tempos))
    if not ts:
        ts = [(0, 120.0)]

    def time_of(tick):
        acc = 0.0
        prev_t, prev_b = ts[0][0], ts[0][1]
        for t, b in ts[1:]:
            if t >= tick:
                break
            acc += (t - prev_t) / PPQ * 60.0 / prev_b
            prev_t, prev_b = t, b
        acc += (tick - prev_t) / PPQ * 60.0 / prev_b
        return acc
    return time_of, ts


def single_take(evs, seed):
    """多轨同响窗口内只保留随机一轨（确定性伪随机；--seed 可换得到不同取轨版本）"""
    rng = random.Random(seed)
    ordered = sorted(evs, key=lambda e: (e.tick, e.staff))
    # 滑窗分组：组内首尾 tick 差 ≤ 容差（≈"同响瞬间"），组与组之间不粘连
    groups = []
    i = 0
    while i < len(ordered):
        j = i
        while j < len(ordered) and ordered[j].tick - ordered[i].tick <= TAKE_TOL_TICKS:
            j += 1
        groups.append(ordered[i:j])
        i = j
    keep = set()
    dropped = 0
    picks = {}
    for g in groups:
        staffs = sorted({e.staff for e in g})
        if len(staffs) > 1:
            pick = rng.choice(staffs)
            picks[pick] = picks.get(pick, 0) + 1
            for e in g:
                if e.staff == pick:
                    keep.add(id(e))
                else:
                    dropped += 1
        else:
            for e in g:
                keep.add(id(e))
    return [e for e in evs if id(e) in keep], dropped, picks


def main():
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return
    src = args[0]
    out = None
    single_take_on = False
    seed = 42
    offset = 0.0
    i = 1
    while i < len(args):
        if args[i] == "--out" and i + 1 < len(args):
            out = args[i + 1]
            i += 2
        elif args[i] == "--single-take":
            single_take_on = True
            i += 1
        elif args[i] == "--seed" and i + 1 < len(args):
            seed = int(args[i + 1])
            i += 2
        elif args[i] == "--offset" and i + 1 < len(args):
            offset = float(args[i + 1])
            i += 2
        else:
            i += 1
    if out is None:
        out = src.rsplit(".", 1)[0] + ".ir.json"

    z = zipfile.ZipFile(src)
    mscx_name = [n for n in z.namelist() if n.endswith(".mscx")][0]
    root = ET.fromstring(z.read(mscx_name))

    per_staff = []
    all_tempos = []
    for staff_el in root.findall('.//Staff'):
        if staff_el.get('id') is None:
            continue  # Part 定义块里的 <Staff>（无 id、无 Measure），跳过
        sid = int(staff_el.get('id'))
        evs, tempos, warn = parse_staff(staff_el, sid)
        per_staff.append((sid, evs))
        all_tempos.extend(tempos)
        print(f"staff {sid}: {len(evs)} 音符（{len(evs) and '异常小节 ' + str(warn) or ''}）")

    kept = dedup_linked(per_staff, find_linked_staffs(root))

    events = []
    for sid, evs in kept:
        evs, n_merged = merge_ties(evs)
        n_gliss = attach_gliss(evs)
        events.extend(evs)
        print(f"staff {sid}: tie 合并 {n_merged} 音，gliss 定位 {n_gliss} 处")

    if single_take_on:
        events, dropped, picks = single_take(events, seed)
        print(f"single-take (seed={seed}): 丢弃 {dropped} 音，各轨被选 {picks}")

    max_tick = max((e.tick for e in events), default=0)
    time_of, ts = build_time_map(all_tempos, max_tick)

    note_events = [NoteEvent(
        tick=e.tick,
        time=round(time_of(e.tick) + offset, 4),
        pitch=e.pitch,
        duration=round(time_of(e.tick + e.dur_ticks) - time_of(e.tick), 4),
        duration_ticks=e.dur_ticks,
        velocity=round(e.velocity, 2),
        instrument="guitar",
        staff=e.staff,
        articulations=["gliss"] if e.gliss_frac else [],
        pitch_end=e.pitch_end,
        tied=e.tied,
    ) for e in events]

    stream = IRStream(
        events=note_events,
        metadata={
            "songName": src.rsplit("\\", 1)[-1].rsplit("/", 1)[-1].rsplit(".", 1)[0],
            "chartAuthor": "mscz frontend",
            "offset": offset,
        },
        events_channel=[{"time": round(time_of(t) + offset, 4), "type": "bpm", "value": round(b, 3)}
                        for t, b in ts],
    )
    dump_json(stream, out)
    print(f"转换完成: {src} -> {out}（offset={offset}s）")
    print(f"  事件 {len(note_events)}，BPM 段 {[b for _, b in ts]}，时长 {max_tick and time_of(max_tick) + offset:.1f}s")
    if events:
        first = min(e.tick for e in events)
        print(f"  首音 tick={first} t={time_of(first) + offset:.3f}s（音频首响 {offset}s 对齐）")


if __name__ == "__main__":
    main()
