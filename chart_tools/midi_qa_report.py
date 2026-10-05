# -*- coding: utf-8 -*-
"""MIDI → 谱面质量报告（需求 A 项交付物）：把生成谱与人类谱基线放在一起对比。

两种用法：
  # 1) 分析已有谱面
  python -X utf8 chart_tools/midi_qa_report.py --charts Logs/midi_chart_demo \
      --out Logs/midi_qa_report.md --csv Logs/midi_qa_report.csv

  # 2) 直接从 MIDI 生成并体检（同时给出"节奏保真度 F1"：谱面 onset 对回 MIDI onset）
  python -X utf8 chart_tools/midi_qa_report.py --midi chart_tools/testdata \
      --instrument auto --difficulties Easy,Normal,Hard,Expert --out Logs/midi_qa_report.md

指标口径与人类基线来源见 chart_metrics.py / metrics_baseline.json
（改编自 rhythm-chart-diffusion，MIT © 2026 Jeonghyun Moon 等）。
"""
import csv
import glob
import json
import os
import sys
from typing import Any, Dict, List, Optional, Tuple

import chart_metrics
from midi_parse import parse_midi
from midi_to_ir import DIFFICULTY_ORDER, Profiles, TempoMap, load_profiles, select_notes


def _expand(pattern: str, extensions: Tuple[str, ...]) -> List[str]:
    if os.path.isdir(pattern):
        found: List[str] = []
        for ext in extensions:
            found += glob.glob(os.path.join(pattern, "**", "*" + ext), recursive=True)
        return sorted(found)
    if any(char in pattern for char in "*?["):
        return sorted(glob.glob(pattern, recursive=True))
    return [pattern]


def _midi_reference_onsets(path: str, instrument: str) -> List[float]:
    """参考 onset：与前端同一套选轨逻辑选出的 MIDI 音符，换算成秒（长键只取起点）。"""
    data = parse_midi(path)
    profiles = Profiles(load_profiles())
    notes, _kind, _log = select_notes(data, instrument, profiles, None)
    tempo = TempoMap(data.tempos, data.ppq)
    return sorted(tempo.time_at_beat(note.start_tick / data.ppq) for note in notes)


def analyze_chart(chart: Dict[str, Any], ref_onsets: Optional[List[float]] = None,
                  baseline: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    summary = chart_metrics.summarize(chart, baseline=baseline)
    summary["fidelity"] = None
    if ref_onsets:
        summary["fidelity"] = chart_metrics.onset_f1(chart, ref_onsets, tol_ms=50.0)
    return summary


def _md_table(headers: List[str], rows: List[List[str]]) -> List[str]:
    lines = ["| " + " | ".join(headers) + " |",
             "|" + "|".join(["---"] * len(headers)) + "|"]
    for row in rows:
        lines.append("| " + " | ".join(str(cell) for cell in row) + " |")
    return lines


def write_markdown(summaries: List[Dict[str, Any]], baseline: Dict[str, Any],
                   out_path: str, origin: str) -> None:
    lines: List[str] = []
    lines.append("# MIDI → 谱面质量报告")
    lines.append("")
    lines.append("对比对象：`%s`。" % origin)
    lines.append("人类基线来自 **rhythm-chart-diffusion**（MIT © 2026 Jeonghyun Moon 等）"
                 "`docs/_stats/*.csv`；本作是两指可打，NPS 按 `twoFinger.maxNps=%s` 折算后比较。"
                 % baseline.get("twoFinger", {}).get("maxNps"))
    lines.append("")
    lines.append("> 读法：基线是人类**整曲**统计，短 Phrase 的 NPS/每拍音符数偏差多来自素材自身密度，"
                 "短素材优先看 **节奏 F1（保留率）**、**长键占比**、**pattern 覆盖** 三列；"
                 "NPS 与每拍音符数在整曲上有意义。")
    lines.append("")
    headers = ["谱面", "难度", "音符", "中位 NPS", "峰值 NPS", "长键占比",
               "长键中位(cell)", "pattern 覆盖", "随机基线", "节奏 F1(±50ms)", "红线"]
    rows = []
    for summary in summaries:
        nps, holds, patterns = summary["nps"], summary["holds"], summary["patterns"]
        rows.append([
            "%s / %s" % (summary["songName"], summary["instrument"]),
            summary["grade"], summary["noteStats"]["notes"],
            nps["medianNps"], nps["peakNps"], holds["holdShare"],
            holds["lenCellsP50"], patterns["coverage"], patterns["chance"],
            (summary["fidelity"]["f1"] if summary.get("fidelity") else "—"),
            len(summary["outliers"]),
        ])
    lines += _md_table(headers, rows)
    lines.append("")
    for summary in summaries:
        lines.append("## %s / %s / %s" % (summary["songName"], summary["instrument"],
                                          summary["grade"]))
        lines.append("")
        if summary.get("fidelity"):
            fidelity = summary["fidelity"]
            lines.append("- 节奏保真度：F1 %.3f（precision %.3f / recall %.3f，"
                         "谱面 %d onset vs MIDI %d onset，±50ms）"
                         % (fidelity["f1"], fidelity["precision"], fidelity["recall"],
                            fidelity["pred"], fidelity["ref"]))
        if summary.get("starRating") is not None:
            lines.append("- rosu-pp 估算星级：%s" % summary["starRating"])
        else:
            lines.append("- rosu-pp 星级：未计算（可选依赖 `pip install rosu-pp-py` 后自动启用）")
        lines.append("- 轨分布：%s（失衡 %.2f，未用轨 %d）"
                     % (summary["lanes"]["counts"], summary["lanes"]["imbalance"] or 0,
                        summary["lanes"]["unusedLanes"]))
        lines.append("")
        lines += _md_table(["指标", "本作", "人类中位", "本作两指折算", "结论"],
                           [[row["metric"], row["ours"], row["human"],
                             row["humanTwoFinger"], row["verdict"]]
                            for row in summary["vsHuman"]])
        if summary["outliers"]:
            lines.append("")
            lines.append("红线告警：")
            for flag in summary["outliers"]:
                lines.append("- ⚠ %s" % flag)
        lines.append("")
    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def write_csv(summaries: List[Dict[str, Any]], out_path: str) -> None:
    fieldnames = ["songName", "instrument", "grade", "notes", "medianNps", "peakNps",
                  "holdShare", "lenCellsP50", "shortShare", "patternCoverage",
                  "patternChance", "laneImbalance", "fidelityF1", "outliers"]
    with open(out_path, "w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        for summary in summaries:
            writer.writerow({
                "songName": summary["songName"], "instrument": summary["instrument"],
                "grade": summary["grade"], "notes": summary["noteStats"]["notes"],
                "medianNps": summary["nps"]["medianNps"], "peakNps": summary["nps"]["peakNps"],
                "holdShare": summary["holds"]["holdShare"],
                "lenCellsP50": summary["holds"]["lenCellsP50"],
                "shortShare": summary["holds"]["shortShare"],
                "patternCoverage": summary["patterns"]["coverage"],
                "patternChance": summary["patterns"]["chance"],
                "laneImbalance": summary["lanes"]["imbalance"],
                "fidelityF1": (summary["fidelity"] or {}).get("f1"),
                "outliers": "; ".join(summary["outliers"]),
            })


def main(argv: Optional[List[str]] = None) -> int:
    if sys.stdout and hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = list(argv if argv is not None else sys.argv[1:])
    if not args or args[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    opts: Dict[str, Any] = {"charts": None, "midi": None, "instrument": "auto",
                            "difficulties": ["Easy", "Normal", "Hard", "Expert"],
                            "out": None, "csv": None}
    flags = {"--charts": "charts", "--midi": "midi", "--instrument": "instrument",
             "--difficulties": "difficulties", "--out": "out", "--csv": "csv"}
    index = 0
    while index < len(args):
        key = args[index]
        field = flags.get(key)
        if field and index + 1 < len(args):
            value = args[index + 1]
            if field == "difficulties":
                opts[field] = [item.strip() for item in value.split(",") if item.strip()]
            else:
                opts[field] = value
            index += 2
        else:
            index += 1

    baseline = chart_metrics.load_baseline()
    summaries: List[Dict[str, Any]] = []
    origin = ""

    if opts["midi"]:
        import midi_chart
        files = _expand(opts["midi"], (".mid", ".midi"))
        origin = "%s（现场生成 %s）" % (opts["midi"], ",".join(opts["difficulties"]))
        for path in files:
            instrument = opts["instrument"]
            if instrument == "auto":
                instrument = "auto"
            for difficulty in opts["difficulties"]:
                if difficulty not in DIFFICULTY_ORDER:
                    print("✗ 未知难度 %s" % difficulty)
                    return 1
                result = midi_chart.generate_chart(path, instrument, difficulty)
                reference = _midi_reference_onsets(path, instrument)
                summaries.append(analyze_chart(result["chart"], reference, baseline))
    elif opts["charts"]:
        files = _expand(opts["charts"], (".json",))
        origin = opts["charts"]
        for path in files:
            with open(path, encoding="utf-8") as handle:
                chart = json.load(handle)
            summaries.append(analyze_chart(chart, None, baseline))
    else:
        print(__doc__)
        return 0

    if not summaries:
        print("✗ 没有可分析的谱面")
        return 1

    out_path = opts["out"] or os.path.join("Logs", "midi_qa_report.md")
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    write_markdown(summaries, baseline, out_path, origin)
    print("报告已写出 %s（%d 张谱）" % (out_path, len(summaries)))
    if opts["csv"]:
        os.makedirs(os.path.dirname(os.path.abspath(opts["csv"])), exist_ok=True)
        write_csv(summaries, opts["csv"])
        print("CSV 已写出 %s" % opts["csv"])

    flagged = sum(len(s["outliers"]) for s in summaries)
    print("%-26s %-6s %5s %7s %7s %7s %7s %7s %7s" %
          ("谱面", "难度", "音符", "中位NPS", "峰值NPS", "长键%", "pattern", "随机", "F1"))
    for summary in summaries:
        print("%-26s %-6s %5d %7.2f %7.2f %7.3f %7.3f %7.3f %7s" % (
            "%s/%s" % (summary["songName"][:18], summary["instrument"]),
            summary["grade"], summary["noteStats"]["notes"], summary["nps"]["medianNps"],
            summary["nps"]["peakNps"], summary["holds"]["holdShare"],
            summary["patterns"]["coverage"], summary["patterns"]["chance"],
            ("%.3f" % summary["fidelity"]["f1"]) if summary.get("fidelity") else "—"))
    print("红线告警 %d 条（详见报告）" % flagged)
    return 0


if __name__ == "__main__":
    sys.exit(main())
