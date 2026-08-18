# -*- coding: utf-8 -*-
"""
归一化 IR（中间表示）模型 —— 谱面转换协议 v1 §2.1 的实现。

管线：前端（mscz/MusicXML/MIDI/gp/.chart）→ IRStream → 规则引擎（rule_engine.py）→ Chart v2 JSON

IR 约定：
- tick 为 IR 标准刻度：每四分音符 768 tick（IR_PPQ）。前端必须把源 tick 归一化到
  此分辨率（无 tick 概念的格式填 0，仅用 time）。引擎据此做"拍数"判定
  （duration_beats / next_interval_beats），与 BPM 无关。
- time 为绝对秒（BPM 变化已由前端按段积分折算）。
- 字段语义详见 docs/谱面转换协议.md §2.1；实现补充字段见各字段注释。

序列化：stream_to_dict / stream_from_dict（JSON 往返）。
"""
import json
from dataclasses import dataclass, field, asdict
from typing import Any, Dict, List, Optional

IR_PPQ = 768  # IR 标准分辨率：每四分音符 tick 数（沿用 mscz 前端 768 惯例）


@dataclass
class NoteEvent:
    tick: int                        # IR 标准 tick（绝对位置，PPQ=768）
    time: float                      # 绝对秒
    pitch: Optional[int]             # MIDI 音高；鼓 = GM 鼓件号；无音高 null
    duration: float                  # 秒（tap 类可为 0）
    duration_ticks: int = 0          # 时值（IR 刻度，tie 链合并后求和）——拍数判定专用
    velocity: float = 0.5            # 0-1（无力度信息的前端填 0.5 中性值）
    instrument: str = "other"        # drums/bass/guitar/keys/vocals/other
    staff: int = 0                   # 声部/轨道序号（多轨谱）
    articulations: List[str] = field(default_factory=list)  # 演奏记号，见协议 §2.2
    pitch_end: Optional[int] = None  # gliss/bend/grace 的参照音高（滑音终点/被装饰主音）
    tied: bool = False               # 是否 tie 链合并事件（合并必须在前端完成）
    position: Optional[Dict[str, Any]] = None  # 源谱位置/技法数值，如 {"bend_semitones": 2}

    def beats(self) -> float:
        """拍数 = tick / PPQ，无 BPM 依赖（协议 §3.3 hold 阈值以拍数计）"""
        return self.duration_ticks / IR_PPQ


@dataclass
class IRStream:
    """一次转换的 IR 容器：事件流 + 元数据 + 非音符事件通道"""
    events: List[NoteEvent] = field(default_factory=list)
    metadata: Dict[str, Any] = field(default_factory=dict)  # songName 等透传到 chart metadata
    events_channel: List[Dict[str, Any]] = field(default_factory=list)  # {"time","type","value"}

    def sorted(self) -> "IRStream":
        self.events.sort(key=lambda e: (e.time, e.tick, e.instrument))
        return self


def build_event(d: Dict[str, Any]) -> NoteEvent:
    """从 dict 构造事件（容忍缺省字段，供 JSON 往返与测试手写）"""
    return NoteEvent(
        tick=int(d.get("tick", 0)),
        time=float(d["time"]),
        pitch=d.get("pitch"),
        duration=float(d.get("duration", 0.0)),
        duration_ticks=int(d.get("duration_ticks", 0)),
        velocity=float(d.get("velocity", 0.5)),
        instrument=d.get("instrument", "other"),
        staff=int(d.get("staff", 0)),
        articulations=list(d.get("articulations", [])),
        pitch_end=d.get("pitch_end"),
        tied=bool(d.get("tied", False)),
        position=d.get("position"),
    )


def stream_to_dict(stream: IRStream) -> Dict[str, Any]:
    return {
        "ir_version": 1,
        "ppq": IR_PPQ,
        "events": [asdict(e) for e in stream.events],
        "metadata": stream.metadata,
        "events_channel": stream.events_channel,
    }


def stream_from_dict(d: Dict[str, Any]) -> IRStream:
    ppq = int(d.get("ppq", IR_PPQ))
    if ppq != IR_PPQ:
        raise ValueError(f"IR ppq={ppq} 与标准 {IR_PPQ} 不符（前端必须归一化 tick）")
    return IRStream(
        events=[build_event(e) for e in d.get("events", [])],
        metadata=dict(d.get("metadata", {})),
        events_channel=list(d.get("events_channel", [])),
    ).sorted()


def dump_json(stream: IRStream, path: str) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(stream_to_dict(stream), f, ensure_ascii=False, indent=1)


def load_json(path: str) -> IRStream:
    with open(path, encoding="utf-8") as f:
        return stream_from_dict(json.load(f))
