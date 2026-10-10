"""全库 timing 体检表：一首歌一行，只汇总**现有**的 A/B 两层诊断，不引入新指标。

A 层（Subdivision Fit）回答"拍子有没有跟住"，B 层（Measure Phase）回答"哪一拍是小节头"。
这里做的只是把两层的结论并排、给一个分类、指出该听哪儿。

分类是**机械规则**（写在 classify 里，可查可改），不是判断"谁对"：

```
1-beat phase error      有一对来源差了整一拍（±1 拍），到现在没合回来或合回来过
2-beat counting error   差的是半小节（±2 拍），而且嫌疑那条自己在那一带把一小节数成了 2 拍
continuous slide        两条小节长度略有出入，相位连续滑；没有突变
unresolved              机器读法之间没有一对能对上（或不可比），谁也没能给出第二个意见
local drift             B 层干净，但 A 层显示拍子在局部漂
clean                   两层都干净
```

    python tools/docs/timing_health_table.py                    # 打印
    python tools/docs/timing_health_table.py --write            # 写 reports/timing_health.md
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import deviation_report as dr  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
REPORT_PATH = REPO / "reports" / "timing_health.md"
HALF = 1e-9


def machine_sources(song: str) -> dict:
    """这首歌上能参与小节相位比较的机器读法（人那条单独看，它的票数多数不够）。"""
    phase = dr.measure_phase(song)
    return {"readings": phase["readings"], "pairs": phase["pairs"]}


def comparable(row: dict) -> bool:
    return not row["reason"] and row["matched"] >= 3


def jump_beats(row: dict) -> float | None:
    """这一对里最大的一次跳变，折算成"对方的一拍"是几拍。"""
    trend = row.get("trend") or {}
    jump = trend.get("maxJump")
    if jump is None or jump < 0.4:
        return None
    return jump


def classify(song: str) -> dict:
    sources = machine_sources(song)
    machine = [reading for reading in sources["readings"]
               if not reading["name"].startswith("human") and len(reading["measures"]) >= 2]
    # 人那条不参与分类：它多数只有 2–10 下小节头，拿它给出"这首歌是什么错"没有依据。
    # 它照样会出现在下面的详情里（"人：n 票"），只是不当判据。
    pairs = [row for row in sources["pairs"]
             if comparable(row) and "human" not in row["a"] and "human" not in row["b"]]
    flags, note = [], []
    one_beat, two_beat, slides, fixed, weird = [], [], [], [], []
    single = []
    for row in pairs:
        verdict = dr.pair_verdict(row)
        pair_name = "%s↔%s" % (row["a"], row["b"])
        # 判据是**持续**的错位（规格 §16：跳一次不算）：拿连续几票的段去说事，
        # 单点的一次跳变只登记，不拿来给这首歌定性。
        offsets = [run for run in row["runs"] if run["k"] != 0]
        if offsets:
            kinds = {run["k"] for run in offsets}
            # 「停错了整数拍」和「相位滑过整数拍」长得像，靠三个数分开：
            # 段的个数、k 有没有走过正负两侧、以及残余离那一格有多远。
            # 停错：段少、残差小（真的停在那一格）；滑：段多、k 走遍正负、残差悬在中间。
            means = [sum(abs(value) for value in run["residuals"]) / len(run["residuals"])
                     for run in offsets]
            trend = abs(row["trend"].get("beatsPerMinute") or 0.0)
            walking = (len(offsets) >= 2 and (trend >= 0.5 or len({k > 0 for k in kinds}) > 1)
                       and sum(means) / len(means) >= 0.10)
            parked = [k for k, mean in zip([run["k"] for run in offsets], means) if mean <= 0.10]
            if walking:
                slides.append(pair_name)
                note.append("%s：%d 段错位、k 走遍 %s、趋势 %+.2f 拍/分钟、残余悬在中间 —— "
                            "这是相位滑过整数拍（两条小节长度不同），不是停错在某一格"
                            % (pair_name, len(offsets), "".join("%+d" % k for k in sorted(kinds)),
                               row["trend"]["beatsPerMinute"] or 0.0))
            elif any(abs(k) == 1 for k in parked):
                one_beat.append(pair_name)
            elif any(abs(abs(k) * 2 - row["beatsPerBarB"]) < 0.01 for k in parked):
                two_beat.append(pair_name)
            elif parked:
                note.append("%s 有停在 %s 的段（既不是整拍也不是半小节）"
                            % (pair_name, "、".join("%+d 拍" % k for k in sorted(set(parked)))))
        jump = jump_beats(row)
        if jump is not None and not offsets:
            single.append("%s @%s" % (pair_name, dr.clock(row["trend"]["jumpTime"])))
        if "连续滑" in verdict:
            slides.append(pair_name)
            continue
        if verdict.startswith("全段固定差"):
            fixed.append(pair_name)
            continue
        if "没有稳定的相位关系" in verdict or "轮着来" in verdict:
            weird.append(pair_name)
    if single:
        flags.append("single flip（只一处，未持续）")
    if two_beat:
        flags.append("phase slip（半小节）")
        if "reconvergence" in " ".join(dr.pair_verdict(row) for row in pairs):
            flags.append("reconvergence")
    if one_beat:
        flags.append("phase slip（整拍）")
    if slides:
        flags.append("continuous drift")
    if fixed:
        flags.append("fixed offset")
    if weird:
        flags.append("unstable")
    # 证据归属：每一对分歧都把两条来源列出来；同时出现在**每一对**里的那条，就是它跟别人不同
    suspects = set()
    for names in (one_beat + two_beat):
        a, b = names.split("↔")
        suspects = ({a, b} if not suspects else suspects & {a, b})
    grid = dr.affine_grid(song)
    events = dr.events_of(song)
    fit = dr.subdivision_fit(events, grid["origin"], grid["beatSeconds"]) if grid and events else None
    layer_a = {"drift": fit["drift"] if fit else "无网格", "residual": None, "lock": None,
               "hit": ("%.0f%% / %.0f%%" % (fit["heldOut"][0] * 100, fit["heldOut"][1] * 100))
                      if fit else "—"}
    if grid:
        layer_a["lock"] = grid["lock"]
    beats_path = dr.RUNS / song / "beat_this.json"
    if beats_path.exists():
        beats = sorted(float(b) for b in json.loads(beats_path.read_text(encoding="utf-8"))["beats"])
        least = dr.least_squares(beats)
        layer_a["residual"] = least["residualMs"] if least["ok"] else None
        beat_ms = 60000.0 / least["bpm"] if least["ok"] and least["bpm"] else None
        layer_a["residualInBeats"] = (layer_a["residual"] / beat_ms) if beat_ms else None
    if layer_a["residual"] is not None and layer_a["residual"] > 200:
        note.append("A 层：Beat This 的拍序列不能被一条直线描述（残差 %.0f ms）——"
                    "这是拍层的事，和小节相位是两回事" % layer_a["residual"])
    if layer_a["lock"] is not None and layer_a["lock"] < 0.9:
        note.append("workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上"
                    "（lock %.3f），这是它自己的规则，不是歌曲健康" % layer_a["lock"])
    human = [reading for reading in sources["readings"] if reading["name"].startswith("human")
             and len(reading["measures"]) >= 2]
    human_note = "、".join("%s %d 票" % (reading["name"], len(reading["measures"]))
                          for reading in human) or "没有小节头按压"
    if not machine or not pairs:
        klass = "unresolved"
        note.append("机器读法之间没有一对能比较")
    elif one_beat:
        klass = "1-beat phase error"
    elif two_beat:
        klass = "2-beat counting error"
    elif slides:
        klass = "continuous slide"
    elif weird or fixed:
        klass = "unresolved" if weird else "clean"
        if fixed:
            note.append("整体固定相位差（不是局部错位）：%s" % "、".join(fixed))
    elif layer_a["drift"] == "局部明显":
        klass = "local drift"
    else:
        klass = "clean"
    phase_state = ("全部一致" if pairs and not (one_beat or two_beat or slides or fixed or weird)
                   else "有一对分歧" if pairs else "不可比")
    if single and not (one_beat or two_beat or slides or fixed or weird):
        phase_state = "只有单点跳变"
    if klass == "clean" and single:
        note.append("单点跳变（没持续成一段，先记着）：%s" % "、".join(single))
    return {"song": song, "class": klass, "oneBeat": one_beat, "twoBeat": two_beat,
            "slides": slides, "suspects": sorted(suspects), "layerA": layer_a,
            "flags": flags, "single": single, "phase": phase_state, "human": human_note,
            "notes": note, "pairs": len(pairs),
            "readings": [(reading["name"], reading["health"], reading.get("meterMix", "—"),
                          len(reading["measures"])) for reading in sources["readings"]]}


def meter_warnings(reading: dict) -> bool:
    """这条读法自己数的每小节拍数里有没有 2 拍 —— 半小节错位最常见的解释。"""
    return "2 拍×" in (reading.get("meterMix") or "")


def detail(song: str) -> list[str]:
    """这首歌该听哪儿：把异常区段与它涉及的来源列出来。"""
    lines = []
    for row in machine_sources(song)["pairs"]:
        if "human" in row["a"] or "human" in row["b"] or not comparable(row):
            continue
        verdict = dr.pair_verdict(row)
        if verdict.startswith("全段一致") or verdict.startswith("证据太少"):
            continue
        spans = ["%s–%s" % (dr.clock(run["start"]), dr.clock(run["end"]))
                 for run in row["runs"] if run["k"] != 0]
        lines.append("%s：%s%s" % (row["a"] + "↔" + row["b"], verdict,
                                   ("｜异常区段 " + "、".join(spans)) if spans else ""))
    return lines


def report(songs: list[str]) -> tuple[str, list[dict]]:
    rows = [classify(song) for song in songs]
    counts = {}
    for row in rows:
        counts[row["class"]] = counts.get(row["class"], 0) + 1
    lines = ["# 全库 timing 体检表", "",
             "31 首歌，2026-10-11 那一轮：五条读法（人 / BeatThis / madmom / workbench / Mapperatorinator）齐了之后的第一张表。",
             "", "分类：" + "　".join("**%s** %d" % (name, counts[name]) for name in sorted(counts)), "",
             "只汇总现有两层诊断：**A 层** 拍子跟没跟住（ADTOF 鼓点对 affine 网格，held-out 校正固定偏移后），",
             "**B 层** 各来源小节头两两的相位关系。分类是机械规则（见 `tools/docs/timing_health_table.py`）。",
             "人那条（手机按的小节头）不进分类，只列在下面详情里——它多数只有几票，拿它判\"这首歌是什么错\"没有依据。",
             "", "| 歌 | 分类 | A 层：≤0.10 / ≤0.25 | A 层漂移 | A:BeatThis 直线残差 | B 层相位 | B 层标志 | 嫌疑来源 |",
             "|---|---|---|---|---|---|---|---|"]
    for row in rows:
        layer = row["layerA"]
        residual = ("%.0f ms" % layer["residual"]) if layer.get("residual") is not None else "—"
        if layer.get("residualInBeats") is not None:
            residual += "（%.2f 拍）" % layer["residualInBeats"]
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s |"
                     % (row["song"], row["class"], layer.get("hit", "—"), layer["drift"],
                        residual,
                        row["phase"], "、".join(row["flags"]) or "—",
                        "、".join(row["suspects"]) or "—"))
    lines.append("")
    lines.append("各来源自己的 health（只反映它自己输出得像不像话，不代表音乐真值）：")
    lines.append("")
    for row in rows:
        lines.append("- **%s**：" % row["song"] + "、".join(
            "%s %s" % (name, health) for name, health, _mix, _count in row["readings"]) or "—")
    lines.append("")
    lines.append("## 哪首歌要听哪一段")
    lines.append("")
    lines.append("有 B 层分歧、或 A 层有提示（局部漂移 / 拍序列不成直线）的歌列在这里；其余两层都干净的歌曲不必整曲审。")
    lines.append("")
    for row in rows:
        if row["class"] == "clean" and not row["notes"]:
            continue
        lines.append("### %s — %s" % (row["song"], row["class"]))
        for line in detail(row["song"]):
            lines.append("- " + line)
        lines.append("- 人那条：%s" % row["human"])
        for text in row["notes"]:
            lines.append("- " + text)
        lines.append("")
    return "\n".join(lines), rows


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args(argv)
    songs = args.song or sorted(path.name[: -len(".mgmcs.json")] for path in dr.DOCS.glob("*.mgmcs.json")
                                if not path.name.endswith(".raw.mgmcs.json"))
    text, rows = report(songs)
    counts = {}
    for row in rows:
        counts[row["class"]] = counts.get(row["class"], 0) + 1
    print("%-12s %-22s %-6s %-18s %s" % ("歌", "分类", "对数", "嫌疑来源", "A 层漂移"))
    for row in rows:
        print("%-12s %-22s %-6d %-18s %s"
              % (row["song"], row["class"], row["pairs"], "、".join(row["suspects"]) or "—",
                 row["layerA"]["drift"]))
    print()
    print("合计：" + "　".join("%s %d" % (name, counts[name]) for name in sorted(counts)))
    if args.write:
        REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
        REPORT_PATH.write_text(text + "\n", encoding="utf-8", newline="\n")
        print("已写 %s" % REPORT_PATH)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
