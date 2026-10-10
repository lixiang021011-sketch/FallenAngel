"""把隔壁 workbench 的网格当**第三条校准**并进验证器文档。

来源（每首歌两份产物，都在 F:\\vibe transcription\\runs\\<短名>\\）：

    report.json     tempo.bpm / tempo.sixteenth_lock
                    downbeats.beat_this_lock（独立核对：Beat This! 与鼓起音那条格子对不对得上）
                    downbeats.first_downbeat（第一个强拍 = 网格起点）
                    bars.odd_bars（规则推出来的短小节：{audio_time, beats}）
    beat_this.json  beats / downbeats 原始列表（这次只用来核对数，不直接进文档）

写进文档的形状：

    { "calibrationId": "workbench", "source": "machine", "bpm": ..., "audioTime": first_downbeat,
      "beatsPerBar": 4, "barLines": [...]?, "health": "ok"|"suspect",
      "suspectReasons": [...], "needsEars": true?, "note": "..." }

四条规矩（研究者 2026-10-10 定的）：

1. **锁定度当标注，不当闸门**：`beat_this_lock < 0.9` 不阻止它进文档，只让它 `health: suspect`，
   于是依赖它的每条派生结论自动是 `caution`（feishu 0.82 / langzhizhu 0.02 / atom 0.88 都能进来看）。
   **范围必须写清**：这条只属于 `workbench` 这条校准的 source-health 规则——
   它既不是歌曲 health，也不等于"Beat This! 本身不可信"。0.82 / 0.88 这种，
   后面若与人 / librosa / madmom 在局部一致，照样可能有研究价值。
2. **短小节写成 `barLines`**：验证器本来就支持"小节线列表"（不等长小节会被打斜纹），
   这里按 odd_bars 把整条列表生成出来。
3. **规则推出来的东西标 `needsEars`，但只影响人工审校提示**：相位整体换位之前插的那种短小节
   不是测量，是规则——准确的说法是"workbench 按当前规则提出了一个局部 bar-structure 假设，
   等人耳确认"。它**不**让整条 workbench calibration 变 suspect，除非那段异常真的影响到
   你正在看的那条派生结论（那种情况由派生层的 `analysisEligibility` 表达，不由这里表达）。
4. **不把 odd_bars 偷偷变成 timing truth**：barLines 是一条读法，不是一个测量结果。

    python tools/docs/import_workbench_grid.py [--dry-run]

幂等：文档里已经有 `workbench` 这条就跳过。不改任何人的东西，只 append 一条机器网格。
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]                     # mgmcs_validator
DOCS = REPO / "static" / "docs"
RUNS = Path(r"F:\vibe transcription\runs")
MANIFEST = Path(r"F:\workbench曲库\_batch\manifest.json")
LOCK_OK = 0.9                                                  # 隔壁那条独立核对的闸门


def bar_lines(bpm, origin, beats_per_bar, odd_bars, count):
    """从起点走一遍，遇到 odd_bars 里记的短小节就按它自己的拍数走。"""
    nominal = beats_per_bar * 60.0 / bpm
    lines, time = [], origin
    for bar in range(max(0, count)):
        lines.append(round(time, 6))
        beats = beats_per_bar
        for odd in odd_bars:
            if abs(float(odd.get("audio_time", -1)) - time) < 1e-6:
                beats = float(odd.get("beats") or beats_per_bar)
                break
        time += beats * 60.0 / bpm
        if abs(beats - beats_per_bar) > 1e-9 and not lines:
            pass
    return lines, nominal


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="只报告，不写文件")
    args = parser.parse_args(argv)

    songs = {song["name"]: song for song in
             json.loads(MANIFEST.read_text(encoding="utf-8"))["songs"]}
    added, skipped, missing = [], [], []

    for path in sorted(DOCS.glob("*.mgmcs.json")):
        short = path.name[: -len(".mgmcs.json")].replace(".raw", "")
        if short not in songs:
            missing.append(path.name)
            continue
        report_path = RUNS / short / "report.json"
        if not report_path.exists():
            missing.append(path.name)
            continue
        doc = json.loads(path.read_text(encoding="utf-8"))
        existing = next((c for c in doc.get("calibrations") or []
                         if c.get("calibrationId") == "workbench"), None)
        # 幂等：已经有这条**而且把 gates 也抄进来了**才跳过。早先那版只抄了 lock，
        # 没抄 gates——"这条读法的小节线其实是退让来的"就没写进文档，所以缺 gates 的要重刷。
        if existing is not None and existing.get("gateNotes"):
            skipped.append(path.name)
            continue
        report = json.loads(report_path.read_text(encoding="utf-8"))
        tempo = report.get("tempo") or {}
        down = report.get("downbeats") or {}
        bars = report.get("bars") or {}
        bpm = float(tempo.get("bpm") or 0)
        origin = float(down.get("first_downbeat") or 0)
        lock = float(down.get("beat_this_lock") or 0)
        odd = bars.get("odd_bars") or []
        if not (bpm > 0 and origin >= 0):
            missing.append(path.name + "（report 里没有可用的 bpm/起点）")
            continue

        grid = {
            "calibrationId": "workbench",
            "source": "machine",
            "bpm": round(bpm, 6),
            "audioTime": round(origin, 6),
            "beatsPerBar": 4,
            "beatUnit": 4,
            "note": "隔壁 workbench 的读法：鼓起音定 16 分格 + Beat This! 定相位与强拍；"
                    "速度恒定才成立",
            "sixteenthLock": tempo.get("sixteenth_lock"),
            "beatThisLock": lock,
        }
        if odd:
            lines, _ = bar_lines(bpm, origin, 4, odd, int(bars.get("count") or 0))
            if lines:
                grid["barLines"] = [{"time": t} for t in lines]
                grid["needsEars"] = True
                grid["needsEarsNote"] = ("含 %d 个规则推出来的短小节：这是本读法按当前规则提出的"
                                         "局部 bar-structure 假设，需要耳朵确认；不是测量结果" % len(odd))
                grid["note"] += "；含 %d 个规则推出来的短小节（needsEars，只作人工审校提示）" % len(odd)

        if report.get("gates"):
            # 原样带上：上游哪条判据没过、有没有因此退回某个读法，读的人必须看得见
            grid["gateNotes"] = report["gates"]
        grid["health"] = "ok" if lock >= LOCK_OK else "suspect"
        grid["suspectReasons"] = ([] if lock >= LOCK_OK else
                                  ["beatThisLock %.2f（< %.1f）：这是 **workbench 这条校准**的 "
                                   "source-health 规则——Beat This! 与鼓起音那条格子没对上；"
                                   "它不代表歌曲健康，也不代表 Beat This! 本身不可信："
                                   "若这条读法在局部与人 / librosa / madmom 一致，仍有研究价值"
                                   % (lock, LOCK_OK)])
        if existing is not None:                       # 重刷：就地更新，别把命名冲掉
            reading = existing.get("reading")
            existing.clear()
            existing.update(grid)
            if reading:
                existing["reading"] = reading          # reading 由 name_readings 管
        else:
            doc.setdefault("calibrations", []).append(grid)
        if not args.dry_run:
            path.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
        added.append((path.name, round(bpm, 3), round(origin, 3), lock, len(odd), grid["health"]))

    print("%s并入 %d 份，跳过（已有）%d 份，缺产物 %d 份"
          % ("[dry-run] " if args.dry_run else "", len(added), len(skipped), len(missing)))
    for name, bpm, origin, lock, odd, health in added[:8]:
        print("   %-28s %7.3f BPM  起点 %6.3fs  锁定 %.3f  短小节 %d  → %s"
              % (name, bpm, origin, lock, odd, health))
    if missing:
        print("   缺产物：%s" % "、".join(sorted(set(missing))[:6]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
