# -*- coding: utf-8 -*-
"""标准 MIDI 文件（SMF，.mid/.midi）解析器 —— 谱面转换协议 §2「MIDI 前端」的解析层。

为什么自己写：本项目不引入第三方依赖（与 chart_to_midi.py 手写 SMF 一致），
而且只需要读音符/速度/拍号等有限信息。

本模块只做「格式 → 原始数据」，不做任何音乐语义处理：
量化 / 分组 / 难度处理 / 映射 都在 midi_to_ir.py 与 rule_engine.py。

支持：
- format 0 / 1 / 2：逐轨解析后合并（保留 track 号）
- 变长量 delta time → 绝对 tick；running status
- 音符配对：同一 (track, channel, pitch) 的 note-on/note-off 配对；
  重复触发用 LIFO（后开的先关）；轨道结束时未闭合的音符截断为零长并记警告
- meta：轨名 0x03、tempo 0x51、拍号 0x58、EOT 0x2F
- program change（Cn）→ 音色号
- 明确不支持 SMPTE 分频（division 最高位为 1）→ 报错，避免静默出错

用法（自查）: python -X utf8 chart_tools/midi_parse.py <file.mid>
"""
import struct
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple


class MidiParseError(Exception):
    """MIDI 文件无法解析（结构损坏、SMPTE 分频、非 SMF 等）。"""


@dataclass
class RawNote:
    """一条 MIDI 音符（原始 tick，未归一化）。"""
    pitch: int
    start_tick: int
    end_tick: int
    velocity: int          # 0-127（note-on 力度）
    channel: int           # 0-15
    track: int             # 轨序号（文件内顺序）
    program: Optional[int] = None   # 该轨/通道最近的音色号
    track_name: str = ""

    @property
    def duration_tick(self) -> int:
        return max(0, self.end_tick - self.start_tick)


@dataclass
class TempoEvent:
    tick: int
    bpm: float


@dataclass
class TimeSignatureEvent:
    tick: int
    numerator: int
    denominator: int       # 实际分母（4 = 四分音符为一拍）


@dataclass
class MidiFileData:
    """解析结果：原始音符 + 全局信息（谱面逻辑用 beat，见 midi_to_ir.TempoMap）。"""
    ppq: int                                        # ticks per quarter note
    format: int
    name: str = "<memory>"                          # 来源文件名（错误信息/谱面标题用）
    notes: List[RawNote] = field(default_factory=list)
    tempos: List[TempoEvent] = field(default_factory=list)
    time_signatures: List[TimeSignatureEvent] = field(default_factory=list)
    track_names: Dict[int, str] = field(default_factory=dict)
    channels: Dict[int, List[int]] = field(default_factory=dict)   # track → 出现过的 channel
    warnings: List[str] = field(default_factory=list)

    def tracks(self) -> List[int]:
        return sorted({n.track for n in self.notes})


def _read_vlq(data: bytes, pos: int) -> Tuple[int, int]:
    value = 0
    for _ in range(4):                       # SMF 规定变长量最长 4 字节
        if pos >= len(data):
            raise MidiParseError("变长量读到文件末尾（文件损坏）")
        byte = data[pos]
        pos += 1
        value = (value << 7) | (byte & 0x7F)
        if not byte & 0x80:
            return value, pos
    raise MidiParseError("变长量超过 4 字节（文件损坏）")


def _close_note(out: MidiFileData, track_index: int, track_name: str,
                program: Dict[int, int], open_notes, channel: int,
                pitch: int, tick: int) -> None:
    stack = open_notes.get((channel, pitch))
    if not stack:
        out.warnings.append("轨%d 通道%d 音高%d 出现无对应的 note-off（tick %d）"
                            % (track_index, channel + 1, pitch, tick))
        return
    start_tick, velocity = stack.pop()          # LIFO：后开的先关
    out.notes.append(RawNote(pitch, start_tick, tick, velocity,
                             channel, track_index, program.get(channel), track_name))


def _parse_track(body: bytes, track_index: int, out: MidiFileData) -> None:
    pos = 0
    tick = 0
    status = 0
    running = False
    track_name = ""
    program: Dict[int, int] = {}
    open_notes: Dict[Tuple[int, int], List[Tuple[int, int]]] = defaultdict(list)

    while pos < len(body):
        delta, pos = _read_vlq(body, pos)
        tick += delta
        if pos >= len(body):
            break
        byte = body[pos]
        if byte & 0x80:
            status = byte
            pos += 1
            running = False
        elif not running and status == 0:
            raise MidiParseError("轨道出现无状态字节的数据（running status 缺失）")
        else:
            running = True

        if status == 0xFF:                    # meta
            if pos >= len(body):
                break
            meta_type = body[pos]
            pos += 1
            length, pos = _read_vlq(body, pos)
            payload = body[pos:pos + length]
            pos += length
            if meta_type == 0x03:             # 轨名
                track_name = payload.decode("utf-8", errors="replace").strip()
                out.track_names[track_index] = track_name
            elif meta_type == 0x51 and length == 3:
                micros = int.from_bytes(payload, "big")
                if micros > 0:
                    out.tempos.append(TempoEvent(tick, 60_000_000.0 / micros))
            elif meta_type == 0x58 and length >= 2:
                out.time_signatures.append(TimeSignatureEvent(tick, payload[0], 1 << payload[1]))
            elif meta_type == 0x2F:
                break
            running = False
            continue

        if status in (0xF0, 0xF7):            # sysex：跳过
            length, pos = _read_vlq(body, pos)
            pos += length
            running = False
            continue

        kind = status & 0xF0
        channel = status & 0x0F
        if kind in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
            if pos + 2 > len(body):
                break
            d1, d2 = body[pos], body[pos + 1]
            pos += 2
            if kind == 0x90:
                if d2 > 0:
                    open_notes[(channel, d1)].append((tick, d2))
                else:
                    _close_note(out, track_index, track_name, program, open_notes, channel, d1, tick)
            elif kind == 0x80:
                _close_note(out, track_index, track_name, program, open_notes, channel, d1, tick)
        elif kind in (0xC0, 0xD0):
            if pos + 1 > len(body):
                break
            d1 = body[pos]
            pos += 1
            if kind == 0xC0:
                program[channel] = d1
        else:
            raise MidiParseError("未知状态字节 0x%02X（文件损坏）" % status)

    for (channel, pitch), stack in open_notes.items():
        for start_tick, velocity in stack:
            out.warnings.append(
                "轨%d 通道%d 音高%d 未闭合（tick %d 起）→ 截断为零长"
                % (track_index, channel + 1, pitch, start_tick))
            out.notes.append(RawNote(pitch, start_tick, start_tick, velocity,
                                     channel, track_index, program.get(channel), track_name))


def parse_midi_bytes(data: bytes) -> MidiFileData:
    if len(data) < 14 or data[:4] != b"MThd":
        raise MidiParseError("不是标准 MIDI 文件（缺少 MThd 头）")
    header_len = struct.unpack(">I", data[4:8])[0]
    fmt, ntrks, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise MidiParseError("不支持 SMPTE 时间码分频的 MIDI（division=0x%04X）" % division)
    if division == 0:
        raise MidiParseError("division（PPQ）=0，文件非法")

    out = MidiFileData(ppq=division, format=fmt)
    pos = 8 + header_len
    track_index = 0
    while pos + 8 <= len(data):
        chunk_id = data[pos:pos + 4]
        chunk_len = struct.unpack(">I", data[pos + 4:pos + 8])[0]
        pos += 8
        body = data[pos:pos + chunk_len]
        pos += chunk_len
        if chunk_id != b"MTrk":
            out.warnings.append("跳过未知块 %r（%d 字节）" % (chunk_id, chunk_len))
            continue
        _parse_track(body, track_index, out)
        track_index += 1

    if ntrks and track_index < ntrks:
        out.warnings.append("头部声明 %d 轨，实际读到 %d 轨" % (ntrks, track_index))
    out.notes.sort(key=lambda n: (n.start_tick, n.track, n.pitch))
    out.tempos.sort(key=lambda t: t.tick)
    out.time_signatures.sort(key=lambda t: t.tick)
    for note in out.notes:
        channels = out.channels.setdefault(note.track, [])
        if note.channel not in channels:
            channels.append(note.channel)
    return out


def parse_midi(path: str) -> MidiFileData:
    with open(path, "rb") as handle:
        data = parse_midi_bytes(handle.read())
    data.name = path
    return data


def main() -> int:
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(sys.argv) < 2:
        print(__doc__)
        return 0
    data = parse_midi(sys.argv[1])
    print("文件：%s" % sys.argv[1])
    print("格式 %d，PPQ %d，音符 %d，tempo 事件 %d，拍号事件 %d"
          % (data.format, data.ppq, len(data.notes), len(data.tempos), len(data.time_signatures)))
    for track in data.tracks():
        notes = [n for n in data.notes if n.track == track]
        programs = sorted({n.program for n in notes if n.program is not None})
        print("  轨 %d %-20s 音符 %3d 通道 %s program %s 音高 %s"
              % (track, data.track_names.get(track, ""), len(notes),
                 data.channels.get(track, []), programs,
                 (min(n.pitch for n in notes), max(n.pitch for n in notes)) if notes else "-"))
    for warning in data.warnings:
        print("  ⚠ " + warning)
    return 0


if __name__ == "__main__":
    sys.exit(main())
