# -*- coding: utf-8 -*-
"""
规则引擎 —— 谱面转换协议 v1 §3 的实现。

消费 IRStream（ir.py），按 JSON 声明式规则（chart_tools/rules/*.json）产出
FallenAngel Chart v2 JSON。IR→chart 是纯函数：确定性（同输入同输出）、可单测、可回归。

匹配模型（协议 §3.2）：
- 自上而下第一条命中生效（顺序 = 优先级），defaults.unmatched 兜底。
- match 条件：字段相等、集合包含（articulations/pitch_in ∈）、范围（duration_min/max、
  duration_beats_min/max、next_interval_beats_max）、存在性（"pitch": null）。
- emit 值三类：固定值、来源引用（"from_pitch" 音高→轨道 / 方向上下）、生成器
  （"pitch_to_x"/"bend_offset"/"bend_offset_neg" 生成 slide path、"cross_lane" 生成扫弦 drag 序列）。
- "param:x" 字符串先经 params 解析（规则文件 params 段，<song>.json 可覆盖 default.json）。

post 处理（协议 §4）：chord_split（同刻 ≤2 轨，翻空位）、overlap（同轨长音重叠截断降级）、
density_warn（>8 音/秒警告不删音）。
校验断言（协议 §5.3）：lane ∈ 0-4、slide path t 单调且首 0 末 duration、x ∈ [0,4]。

用法: python rule_engine.py <ir.json> [--rules 规则文件] [--out chart.json]
"""
import json
import sys
from typing import Any, Dict, List, Optional, Tuple

from ir import IRStream, IR_PPQ, NoteEvent, load_json

DEFAULT_RULES = "rules/default.json"
PITCH_MIN, PITCH_MAX = 0, 127   # 音高→连续 x 的映射音域（规则 params.pitch_range 可覆盖）

# 校验/输出 常量（5 键：lane 0-4，x 连续坐标 0.0~4.0）
LANE_MAX = 4
X_MIN, X_MAX = 0.0, 4.0


# ---------- 规则加载 ----------

def load_rules(path: str) -> Dict[str, Any]:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _merge_params(base: Dict[str, Any], override: Dict[str, Any]) -> Dict[str, Any]:
    """params 深合并：song 文件覆盖 default 文件的同名键（flick_enabled 等嵌套 dict 按乐器键覆盖）"""
    out = dict(base)
    for k, v in override.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = {**out[k], **v}
        else:
            out[k] = v
    return out


def resolve_params(rules: Dict[str, Any]) -> Dict[str, Any]:
    """解析规则文件 params（含 "param:x" 引用）"""
    p = dict(rules.get("params", {}))
    # 解析嵌套 param 引用（暂只有顶层值被引用，防御性处理一层）
    for k, v in list(p.items()):
        if isinstance(v, str) and v.startswith("param:"):
            p[k] = p[v[6:]]
    return p


# ---------- 音高 → 轨道 / x ----------

def pitch_to_x(pitch: int, params: Dict[str, Any]) -> float:
    """音高 → 连续轨道坐标 0.0~4.0（协议 §3.4：音高越高轨道越右）"""
    lo, hi = params.get("pitch_range", [PITCH_MIN, PITCH_MAX])
    x = (pitch - lo) / (hi - lo) * LANE_MAX if hi > lo else 0.0
    return max(X_MIN, min(X_MAX, x))


def pitch_to_lane(pitch: int, params: Dict[str, Any]) -> int:
    return int(round(pitch_to_x(pitch, params)))


def _resolve_lane(emit_lane: Any, ev: NoteEvent, params: Dict[str, Any]) -> Optional[int]:
    if isinstance(emit_lane, (int, float)):
        return int(emit_lane)
    if emit_lane == "from_pitch":
        return pitch_to_lane(ev.pitch if ev.pitch is not None else PITCH_MIN + 12, params)
    return None  # cross_lane 等生成器另行处理


def _resolve_direction(emit_dir: Any, ev: NoteEvent) -> str:
    if isinstance(emit_dir, str) and emit_dir in ("up", "down"):
        return emit_dir
    if emit_dir == "from_pitch":
        # 参照 pitch_end：上行技法（击弦/推弦/开镲）→ up；下行（勾弦/放弦/踏板镲）→ down
        if ev.pitch is not None and ev.pitch_end is not None:
            return "up" if ev.pitch_end > ev.pitch else "down"
        return "up"  # 无参照时默认 up（可配）
    return "up"


# ---------- slide path 生成器 ----------

def _gen_slide_path(kind: str, ev: NoteEvent, params: Dict[str, Any]) -> Optional[List[Dict[str, float]]]:
    """生成 slide path 点数组 [{t, x}]，首点 t=0，末点 t=duration"""
    dur = round(max(ev.duration, 0.05), 3)
    x0 = pitch_to_x(ev.pitch if ev.pitch is not None else PITCH_MIN + 12, params)
    if kind in ("bend_offset", "bend_offset_neg"):
        st = (ev.position or {}).get("bend_semitones")
        if st is None:
            return None  # 缺推弦幅度 → 上层降级
        disp = float(st) * float(params.get("bend_displacement_lanes_per_semitone", 0.5))
        sign = 1.0 if kind == "bend_offset" else -1.0
        x1 = max(X_MIN, min(X_MAX, x0 + sign * disp))
        return [{"t": 0.0, "x": round(x0, 3)}, {"t": dur, "x": round(x1, 3)}]
    # pitch_to_x（gliss/滑弦）：起点 pitch → 终点 pitch_end 线性
    if ev.pitch_end is None:
        return None
    x1 = pitch_to_x(ev.pitch_end, params)
    return [{"t": 0.0, "x": round(x0, 3)}, {"t": dur, "x": round(x1, 3)}]


def _gen_strum_drags(ev: NoteEvent, params: Dict[str, Any]) -> List[Dict[str, Any]]:
    """扫弦 → 快速跨轨 drag 序列（协议：扫弦 = 快速跨轨 drag；Phigros 语义宽松不 miss）"""
    pitches = (ev.position or {}).get("strum_pitches") or [ev.pitch]
    pitches = [p for p in pitches if p is not None]
    n = len(pitches)
    if n == 0:
        return [{"type": "drag", "time": round(ev.time, 3), "lane": 1}]
    span = float(params.get("strum_span_seconds", 0.08))
    if n == 1:
        return [{"type": "drag", "time": round(ev.time, 3),
                 "lane": pitch_to_lane(pitches[0], params)}]
    out = []
    for i, p in enumerate(pitches):
        t = ev.time + span * i / (n - 1)
        out.append({"type": "drag", "time": round(t, 3),
                    "lane": pitch_to_lane(p, params)})
    return out


# ---------- match 求值 ----------

def _match_articulations(have: List[str], need: List[str]) -> bool:
    return all(a in have for a in need)


def match_event(rule: Dict[str, Any], ev: NoteEvent, ctx: Dict[str, Any]) -> bool:
    """单条规则 match 求值。ctx 提供上下文感知量（如 next_interval_beats）"""
    m = rule.get("match", {})
    for key, cond in m.items():
        if key == "duration_min":
            if ev.duration < float(_p(cond, ctx)):
                return False
        elif key == "duration_max":
            if ev.duration > float(_p(cond, ctx)):
                return False
        elif key == "duration_beats_min":
            if ev.beats() < float(_p(cond, ctx)):
                return False
        elif key == "duration_beats_max":
            if ev.beats() > float(_p(cond, ctx)):
                return False
        elif key == "next_interval_beats_max":
            nb = ctx.get("next_interval_beats")
            if nb is None or nb > float(_p(cond, ctx)):
                return False
        elif key == "pitch_in":
            if ev.pitch is None or ev.pitch not in [int(x) for x in cond]:
                return False
        elif key == "articulations":
            if not _match_articulations(ev.articulations, list(cond)):
                return False
        elif key == "pitch":
            if cond is None:
                if ev.pitch is not None:
                    return False
            elif ev.pitch != int(cond):
                return False
        elif key == "instrument":
            if ev.instrument != cond:
                return False
        elif key == "tied":
            if ev.tied != bool(cond):
                return False
        else:  # 其余字段相等匹配
            if getattr(ev, key, None) != cond:
                return False
    return True


def _p(val: Any, ctx: Dict[str, Any]) -> Any:
    return ctx["params"].get(val[6:]) if isinstance(val, str) and val.startswith("param:") else val


# ---------- emit 求值 ----------

def emit_event(rule: Dict[str, Any], ev: NoteEvent, ctx: Dict[str, Any],
               warnings: List[str]) -> List[Dict[str, Any]]:
    """规则 emit 求值 → chart 音符 dict 列表（扫弦可产多条）"""
    e = rule.get("emit", {})
    params = ctx["params"]
    ntype = e.get("type", "tap")
    lane = _resolve_lane(e.get("lane", ctx["defaults"].get("tap_lane")), ev, params)
    base = {"type": ntype, "time": round(ev.time, 3)}

    if ntype == "slide":
        path = _gen_slide_path(e.get("path", "pitch_to_x"), ev, params)
        if path is None:
            warnings.append(f"⚠ {rule.get('name')}: slide 数据不足（bend 幅度/终止音高缺失），"
                            f"t={ev.time:.2f}s 降级 tap")
            return [{"type": "tap", "time": base["time"],
                     "lane": lane if lane is not None else 1}]
        return [{"type": "slide", "time": base["time"],
                 "duration": path[-1]["t"], "path": path,
                 "lane": int(round(path[0]["x"]))}]  # 起始轨（协议 §1.1 可选字段）
    if ntype == "hold":
        return [{"type": "hold", "time": base["time"],
                 "lane": lane if lane is not None else 1,
                 "duration": round(ev.duration, 3)}]
    if ntype == "flick":
        return [{"type": "flick", "time": base["time"],
                 "lane": lane if lane is not None else 1,
                 "direction": _resolve_direction(e.get("direction", "up"), ev)}]
    if ntype == "drag":
        if e.get("lane") == "cross_lane":
            return _gen_strum_drags(ev, params)
        return [{"type": "drag", "time": base["time"],
                 "lane": lane if lane is not None else 1}]
    # tap 兜底
    return [{"type": "tap", "time": base["time"],
             "lane": lane if lane is not None else 1}]


def _next_interval_beats(events: List[NoteEvent], i: int) -> Optional[float]:
    """同 instrument 相邻事件的最小间隔（拍）——密集度判定（协议 §3.3 贝斯幽灵音）。

    首事件看后向间隔、末事件看前向间隔、中间取前后最小：幽灵音"密集"与方向无关，
    末事件不能因为后面没有事件就被判稀疏。
    """
    ev = events[i]
    prev = nxt = None
    for k in range(i - 1, -1, -1):
        if events[k].instrument == ev.instrument:
            prev = (ev.tick - events[k].tick) / IR_PPQ
            break
    for k in range(i + 1, len(events)):
        if events[k].instrument == ev.instrument:
            nxt = (events[k].tick - ev.tick) / IR_PPQ
            break
    if prev is None:
        return nxt
    if nxt is None:
        return prev
    return min(prev, nxt)


# ---------- post 处理 ----------

def _post_process(notes: List[Dict[str, Any]], post: Dict[str, Any],
                  params: Dict[str, Any], warnings: List[str]) -> List[Dict[str, Any]]:
    notes = sorted(notes, key=lambda n: (n["time"], n.get("lane", 0), n["type"]))

    # 1) density_warn：滑窗 1s 内音符数 > max → 警告（不删音）。
    #    连续超限区间合并为一条，报区间起点 + 区间内峰值密度（避免刷屏且不漏段）
    dw = post.get("density_warn") or {}
    max_ps = int(_p(dw.get("max_notes_per_second", 8), {"params": params}))
    seg_start = None
    peak = 0
    for i in range(len(notes)):
        j = i
        while j < len(notes) and notes[j]["time"] - notes[i]["time"] < 1.0:
            j += 1
        cnt = j - i
        if cnt > max_ps:
            if seg_start is None:
                seg_start = notes[i]["time"]
            peak = max(peak, cnt)
        elif seg_start is not None:
            warnings.append(f"⚠ 超密段 t={seg_start:.2f}s~{notes[i]['time']:.2f}s：峰值 {peak} 音/秒（上限 {max_ps}），未删音")
            seg_start = None
            peak = 0
    if seg_start is not None:
        warnings.append(f"⚠ 超密段 t={seg_start:.2f}s 起：峰值 {peak} 音/秒（上限 {max_ps}），未删音")

    # 2) chord_split：同刻（±1ms）组内同轨重复 → 翻相邻空位；总轨数超限 → 警告保留
    #    slide 不参与：连续坐标音符由 path 定位，翻 lane 会造成 lane 与 path 矛盾
    cs = post.get("chord_split") or {}
    max_sim = int(_p(cs.get("max_simultaneous", 2), {"params": params}))
    groups = []
    for n in notes:
        if groups and abs(groups[-1][0]["time"] - n["time"]) < 0.001:
            groups[-1].append(n)
        else:
            groups.append([n])
    for g in groups:
        lanes = [n.get("lane") for n in g if n["type"] != "slide"]
        if len(g) > max_sim and len(set(lanes)) <= max_sim:
            warnings.append(f"⚠ 同刻多音 t={g[0]['time']:.2f}s：{len(g)} 音压缩到 {len(set(lanes))} 轨")
        # 同轨重复：后者翻相邻空位（slide 除外）
        used = set()
        for n in g:
            if n["type"] == "slide":
                continue
            l = n.get("lane")
            if l in used:
                for cand in (l - 1, l + 1):
                    if 0 <= cand <= LANE_MAX and cand not in used:
                        n["lane"] = cand
                        warnings.append(f"⚠ 同刻同轨 t={n['time']:.2f}s：{l}→{cand}（翻空位）")
                        l = cand
                        break
            used.add(l)
        if len(g) > max_sim:
            warnings.append(f"⚠ 同刻多音 t={g[0]['time']:.2f}s：{len(g)} 音 > 双轨上限，保留碰撞")

    # 3) overlap：同轨长音重叠 → 先开始者截断到后者开始；后者是 hold 才降级 tap
    #    （slide 是移动音符，与 hold 重叠属正常编曲如 pad 上推弦——保持 slide 不降级，只截断长音）
    ov = post.get("overlap") or {}
    if ov.get("strategy") == "truncate_earlier_end":
        longs = [n for n in notes if n["type"] in ("hold", "slide")]
        for i, a in enumerate(longs):
            for b in longs[i + 1:]:
                if a["lane"] == b["lane"] and a["time"] < b["time"] < a["time"] + a.get("duration", 0):
                    new_dur = round(b["time"] - a["time"], 3)
                    if a["type"] == "slide":
                        # slide 截断：path 末点同步插值（保持 t 首 0 末 duration 且路径连续）
                        path = a["path"]
                        t0, x0 = path[-2]["t"], path[-2]["x"]
                        t1, x1 = path[-1]["t"], path[-1]["x"]
                        if t1 > t0:
                            x_new = x0 + (x1 - x0) * (new_dur - t0) / (t1 - t0)
                            path[-1] = {"t": new_dur, "x": round(x_new, 3)}
                    a["duration"] = new_dur
                    if b["type"] == "hold":
                        warnings.append(f"⚠ 同轨长音重叠 t={b['time']:.2f}s：后者降级 tap")
                        b2 = {"type": "tap", "time": b["time"], "lane": b["lane"]}
                        notes[notes.index(b)] = b2
                    else:
                        warnings.append(f"⚠ 同轨长音重叠 t={b['time']:.2f}s：先开始者截断到 {b['time']:.2f}s，slide 保持")
    return notes


# ---------- 校验断言（协议 §5.3） ----------

def _validate(notes: List[Dict[str, Any]], warnings: List[str]) -> None:
    for n in notes:
        l = n.get("lane")
        if l is not None and not (0 <= l <= LANE_MAX):
            warnings.append(f"⚠ 校验失败：lane={l} 越界 t={n['time']:.2f}s")
        if n["type"] == "slide":
            path = n.get("path", [])
            ts = [p["t"] for p in path]
            xs = [p["x"] for p in path]
            if not path or ts[0] != 0.0 or ts[-1] != n.get("duration") or ts != sorted(ts):
                warnings.append(f"⚠ 校验失败：slide t 非单调/首末不符 t={n['time']:.2f}s")
            if any(x < X_MIN or x > X_MAX for x in xs):
                warnings.append(f"⚠ 校验失败：slide x 越界 t={n['time']:.2f}s")


# ---------- 主流程 ----------

class RuleEngine:
    def __init__(self, rules: Dict[str, Any]):
        self.rules = rules
        self.params = resolve_params(rules)
        self.defaults = rules.get("defaults", {})
        self.post = rules.get("post", {})

    def apply(self, ir: IRStream) -> Tuple[List[Dict[str, Any]], List[str]]:
        """IR → (chart notes, 警告清单)。纯函数：同输入同输出。"""
        warnings: List[str] = []
        events = ir.sorted().events
        ctx = {"params": self.params, "defaults": self.defaults}
        notes: List[Dict[str, Any]] = []
        matched = 0
        for i, ev in enumerate(events):
            ctx["next_interval_beats"] = _next_interval_beats(events, i)
            hit = None
            for rule in self.rules.get("rules", []):
                if match_event(rule, ev, ctx):
                    hit = rule
                    break
            if hit is None:
                unmatched = self.defaults.get("unmatched", "tap")
                if unmatched == "drop":
                    warnings.append(f"⚠ 未命中规则已丢弃 t={ev.time:.2f}s pitch={ev.pitch}")
                    continue
                notes.extend(emit_event({"name": "unmatched", "emit": {"type": "tap"}}, ev, ctx, warnings))
                matched += 1
                continue
            # flick_enabled=false → flick 降级 tap（风格参数，协议对照表 §6）
            if hit.get("emit", {}).get("type") == "flick":
                fe = self.params.get("flick_enabled", {})
                if isinstance(fe, dict) and fe.get(ev.instrument) is False:
                    warnings.append(f"⚠ {hit.get('name')}: flick 被 flick_enabled 关闭，降级 tap "
                                    f"t={ev.time:.2f}s")
                    hit = {"name": hit.get("name"), "emit": {"type": "tap",
                                                              "lane": hit.get("emit", {}).get("lane", "from_pitch")}}
            notes.extend(emit_event(hit, ev, ctx, warnings))
            matched += 1
        notes = _post_process(notes, self.post, self.params, warnings)
        _validate(notes, warnings)
        return notes, warnings


def build_chart(ir: IRStream, notes: List[Dict[str, Any]]) -> Dict[str, Any]:
    """组装 Chart v2 JSON（协议 §1）：metadata + notes + events"""
    counts: Dict[str, int] = {}
    for n in notes:
        counts[n["type"]] = counts.get(n["type"], 0) + 1
    metadata = {
        **ir.metadata,
        "formatVersion": 2,
        "noteCounts": counts,
    }
    return {"metadata": metadata, "notes": notes, "events": ir.events_channel}


def main():
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # ⚠ 等字符在 GBK 控制台会崩
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return
    ir_path = args[0]
    rules_path = DEFAULT_RULES
    out_path = None
    i = 1
    while i < len(args):
        if args[i] == "--rules" and i + 1 < len(args):
            rules_path = args[i + 1]
            i += 2
        elif args[i] == "--out" and i + 1 < len(args):
            out_path = args[i + 1]
            i += 2
        else:
            i += 1
    if out_path is None:
        out_path = ir_path.rsplit(".", 1)[0] + ".chart.json"

    ir = load_json(ir_path)
    engine = RuleEngine(load_rules(rules_path))
    notes, warnings = engine.apply(ir)
    chart = build_chart(ir, notes)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(chart, f, ensure_ascii=False, indent=1)

    print(f"转换完成: {ir_path} -> {out_path}")
    print(f"  音符 {len(notes)}（规则 {len(engine.rules.get('rules', []))} 条，文件 {rules_path}）")
    if warnings:
        print(f"  警告 {len(warnings)} 条:")
        for w in warnings:
            print(f"    {w}")
    else:
        print("  无警告")


if __name__ == "__main__":
    main()
