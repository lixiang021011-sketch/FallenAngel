"""全库 timing 体检表：一首歌一行，只汇总**现有**的 A/B 两层诊断，不引入新指标。

A 层（Subdivision Fit）回答"拍子有没有跟住"，B 层（Measure Phase）回答"哪一拍是小节头"。
这里做的只是把两层的结论并排、给一个分类、指出该听哪儿。

分类是**机械规则**（写在 classify 里，可查可改），只描述"观测到的事实"，不是判断"谁对"：

```
clean                                 机器读法之间无显著分歧（**不等于真值正确**）
local drift                           B 层干净，但 A 层显示拍子在局部漂
persistent ±2-beat disagreement       有来源在半小节（±2 拍）上持续分歧；是哪条错要人工确认
persistent ±1-beat disagreement       有来源在整拍（±1 拍）上持续分歧；同理
continuous slide                      两条小节长度略有出入，相位连续滑；没有突变
unresolved                            机器读法之间没有一对能对上（或不可比）
```

像 flyway 那种"已经有足够证据说是哪条错"的，写在**详情**里
（`suspected BeatThis 2-beat counting`），总表只留观测事实。

    python tools/docs/timing_health_table.py                    # 打印
    python tools/docs/timing_health_table.py --write            # 写 reports/timing_health.md
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import deviation_report as dr  # noqa: E402
import triage as tri  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
REPORT_PATH = REPO / "reports" / "timing_health.md"
TRIAGE_PATH = REPO / "reports" / "timing_triage.md"
JSON_PATH = REPO / "static" / "timing_health.json"
VERDICTS_PATH = REPO / "human_verdicts.json"
MUSIC_BASE = "http://127.0.0.1:8125"
VALIDATOR_PAGE = "index.html"
HALF = 1e-9


def human_verdicts() -> dict:
    """人工听审的定案（输入，不是产物）。分诊优先读它，规则只在没有定案时才起作用。"""
    if not VERDICTS_PATH.exists():
        return {}
    try:
        data = json.loads(VERDICTS_PATH.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return {item["song"]: item for item in data.get("verdicts") or []}

# 每一类该怎么听、要回答的那一个问题。写在这里，页面直接显示——
# 免得每次都要回来翻聊天记录找"我到底在判什么"。
QUESTIONS = {
    "persistent ±2-beat disagreement":
        "这一带是真的两拍一小节，还是某条读法把一小节数成了两拍？（听鼓型是否每 4 拍循环、军鼓落在第几拍）",
    "persistent ±1-beat disagreement":
        "小节头该落在哪一根拍上？（听贝斯/和弦变换落在哪一拍；两派各执一辞，只需要判这一处）",
    "continuous slide":
        "数一下这两处各 30 秒里有几小节：长度一样 → 是读法的速度估计差（不是音乐问题）；真变了 → tempo change",
    "local drift":
        "A 层说这里的拍子偏得最多：是真的局部变速 / 自由节拍，还是模型在抖？",
    "single flip":
        "那里是多出来一根小节线、少了一根，还是真有一个短小节？",
    "clean":
        "读法之间没有分歧，正常情况下不必听。",
}


def machine_sources(song: str) -> dict:
    """这首歌上能参与小节相位比较的机器读法（人那条单独看，它的支持量多数不够）。"""
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
    # 它照样会出现在下面的详情里（"人：n 个 matched measure"），只是不当判据。
    pairs = [row for row in sources["pairs"]
             if comparable(row) and "human" not in row["a"] and "human" not in row["b"]]
    flags, note = [], []
    one_beat, two_beat, slides, fixed, weird = [], [], [], [], []
    single, singles = [], []
    for row in pairs:
        verdict = dr.pair_verdict(row)
        pair_name = "%s↔%s" % (row["a"], row["b"])
        # 判据是**持续**的错位（规格 §16：跳一次不算）：拿连续支持的一段去说事，
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
            singles.append({"pair": pair_name, "time": row["trend"]["jumpTime"], "jump": jump})
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
    human_note = "、".join("%s %d 个 matched measures" % (reading["name"], len(reading["measures"]))
                           for reading in human) or "没有小节头按压"
    if not machine or not pairs:
        klass = "unresolved"
        note.append("机器读法之间没有一对能比较")
    elif one_beat:
        klass = "persistent ±1-beat disagreement"
    elif two_beat:
        klass = "persistent ±2-beat disagreement"
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
    # 观测事实之外，才轮到"像是谁错了"。只在这条读法自己报过 2 拍的小节时才说，
    # 并且永远带"待人工确认"。
    if klass == "persistent ±2-beat disagreement" and suspects:
        for name in sorted(suspects):
            reading = next((reading for reading in machine if reading["name"] == name), None)
            mix = (reading or {}).get("meterMix") or ""
            found = re.search(r"2 拍×(\d+)", mix)
            if found:
                note.append("疑似 %s 2-beat counting：它自己报的小节里有 %s 个只数了 2 拍"
                            " —— 待人工确认" % (name, found.group(1)))
    phase_state = ("全部一致" if pairs and not (one_beat or two_beat or slides or fixed or weird)
                   else "有一对分歧" if pairs else "不可比")
    if single and not (one_beat or two_beat or slides or fixed or weird):
        phase_state = "只有单点跳变"
    if klass == "clean" and single:
        note.append("单点跳变（没持续成一段，先记着）：%s" % "、".join(single))
    return {"song": song, "class": klass, "oneBeat": one_beat, "twoBeat": two_beat,
            "slides": slides, "suspects": sorted(suspects), "layerA": layer_a,
            "flags": flags, "single": single, "singles": singles,
            "phase": phase_state, "human": human_note,
            "notes": note, "pairs": len(pairs),
            "readings": [(reading["name"], reading["health"], reading.get("meterMix", "—"),
                          len(reading["measures"])) for reading in sources["readings"]]}


def diagnose_song(song: str, row: dict) -> dict:
    """把现有量出来的东西喂给分诊规则（triage.py），拿回"要不要人来听"。"""
    sources = dr.measure_phase(song)
    facts = {
        "song": song,
        # 只把能比的那些对喂进去：不可比的对连 runs / trend 都没有，
        # 分诊看它们会以为"没有分歧"，那是最危险的默认值。
        "pairs": [{"a": pair["a"], "b": pair["b"], "usable": True,
                   "runs": pair["runs"], "trend": pair["trend"],
                   "beatsPerBarB": pair["beatsPerBarB"]}
                  for pair in sources["pairs"] if comparable(pair)],
        "readings": [{"name": reading["name"], "health": reading["health"],
                      "measures": reading["measures"], "meterMix": reading.get("meterMix", "")}
                     for reading in sources["readings"]],
        "layerA": row["layerA"],
        "singles": row["singles"],
        "driftRange": [],
        "verdict": human_verdicts().get(song),
    }
    spot = drift_spot(song)
    if spot and row["class"] == "local drift":
        facts["driftRange"] = [{"start": spot["start"], "end": spot["end"],
                                "seconds": round(spot["end"] - spot["start"], 1)}]
    try:
        item = tri.diagnose(facts)
    except Exception as error:                  # 分诊坏了要看得见，不能默默当 clean
        return {"song": song, "diagnosis": "triage_error", "affectedRange": [],
                "suspectSource": None, "evidence": {"error": str(error)},
                "resolution": "分诊规则自己出错了：%s" % error, "needsHumanReview": True}
    # 单点跳变不在 offset 段里，分诊要单独看到它
    return item


def meter_warnings(reading: dict) -> bool:
    """这条读法自己数的每小节拍数里有没有 2 拍 —— 半小节错位最常见的解释。"""
    return "2 拍×" in (reading.get("meterMix") or "")


def audio_twins(songs: list[str]) -> dict:
    """同一份音频被两个文档引用（round2 与 kiechau 就是这种）。

    必须说出来：同一首歌上两次采样可能一次干净、一次有洞——那是**模型采样的差别**，
    不是歌的问题。单次采样的 Mapperatorinator 输出不能当稳定参照，能当证据的是跨来源的一致/分歧。
    """
    seen = {}
    for song in songs:
        path = dr.DOCS / ("%s.mgmcs.json" % song)
        if not path.exists():
            continue
        name = (json.loads(path.read_text(encoding="utf-8")).get("song") or {}).get("audioFileName")
        if name:
            seen.setdefault(name, []).append(song)
    return {name: group for name, group in seen.items() if len(group) > 1}


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


def hot_spans(song: str, keep: int = 3) -> list[str]:
    """该听的那几段：按"这段有多少 matched measures 支持"排，重叠的只留最有分量的一条。

    同一处差异会在好几对来源上各留一段（BeatThis↔madmom、BeatThis↔mapperatorinator……），
    逐段列出来读不动。这里每段都带上是哪一对、差了几拍、支持量多少，人工复核一次只该听这几处。
    """
    raw = []
    for row in machine_sources(song)["pairs"]:
        if "human" in row["a"] or "human" in row["b"] or not comparable(row):
            continue
        for run in row["runs"]:
            if run["k"] != 0:
                raw.append({"start": run["start"], "end": run["end"], "voted": run["count"],
                            "pair": row["a"] + "↔" + row["b"], "k": run["k"],
                            "bar": row["beatsPerBarB"]})
    raw.sort(key=lambda span: (-span["voted"], span["start"]))
    chosen = []
    for span in raw:
        if any(not (span["end"] < other["start"] or span["start"] > other["end"]) for other in chosen):
            continue                                   # 和已经选中的那段重叠，跳过
        chosen.append(span)
        if len(chosen) >= keep:
            break
    return ["%s–%s：%s 停差 %s（support = %d matched measures）"
            % (dr.clock(span["start"]), dr.clock(span["end"]), span["pair"],
               dr.slip_phrase(span["k"], span["bar"]), span["voted"]) for span in chosen]


def segments(song: str) -> list[dict]:
    """异常区段：**一个地方一条**，里面列出是哪几对来源在这里分歧、差几拍、旁边几条怎么说。

    同一处差异会在每一对来源上各留一段（flyway 的 02:15–02:35 就有三段），所以先合并。
    旁边几条的读法必须一起给出来——只看"Mapper↔BeatThis 差 2 拍"会以为两边打架；
    补上"Mapper↔madmom aligned"才知道是单独一条在那一带数错了。
    """
    phase = machine_sources(song)
    pairs = [row for row in phase["pairs"]
             if comparable(row) and "human" not in row["a"] and "human" not in row["b"]]
    raw = []
    for row in pairs:
        for run in row["runs"]:
            if run["k"] == 0:
                continue
            raw.append({"row": row, "run": run})
    raw.sort(key=lambda item: (item["run"]["start"], item["run"]["end"]))
    groups = []
    for item in raw:
        run = item["run"]
        if groups and run["start"] <= groups[-1]["end"] + 2.0:      # 相隔两秒以内算同一处
            groups[-1]["end"] = max(groups[-1]["end"], run["end"])
            groups[-1]["items"].append(item)
            continue
        groups.append({"start": run["start"], "end": run["end"], "items": [item]})
    out = []
    for group in groups:
        inside = group["items"]
        disagreeing = [item for item in inside
                       if not (item["run"]["end"] < group["start"] - HALF
                               or item["run"]["start"] > group["end"] + HALF)]
        owners = [item["row"] for item in disagreeing]
        pairs_here = []
        for item in disagreeing:
            key = (item["row"]["a"], item["row"]["b"], item["run"]["k"])
            found = next((entry for entry in pairs_here if entry["key"] == key), None)
            support = item["run"]["count"]
            if found:                                  # 同一对在这处断成了两段：合并成一件事
                found["support"] += support
                found["residualRange"] = [
                    min(found["residualRange"][0], min(item["run"]["residuals"])),
                    max(found["residualRange"][1], max(item["run"]["residuals"]))]
                continue
            pairs_here.append({"key": key, "pair": item["row"]["a"] + "↔" + item["row"]["b"],
                               "a": item["row"]["a"], "b": item["row"]["b"],
                               "k": item["run"]["k"],
                               "label": dr.slip_phrase(item["run"]["k"], item["row"]["beatsPerBarB"]),
                               "support": support,
                               "residualRange": [min(item["run"]["residuals"]),
                                                 max(item["run"]["residuals"])]})
        pairs_here = [{key: value for key, value in entry.items() if key != "key"}
                      for entry in pairs_here]
        others = []
        for row in pairs:
            if row in owners:
                continue
            overlap = [item for item in row["runs"]
                       if not (item["end"] < group["start"] - HALF or item["start"] > group["end"] + HALF)]
            label = row["a"] + "↔" + row["b"]
            if not overlap:
                others.append("%s：这一段没有它的小节头" % label)
            elif any(item["k"] != 0 for item in overlap):
                kinds = sorted({item["k"] for item in overlap if item["k"] != 0})
                others.append("%s：停差 %s" % (label, "、".join(
                    dr.slip_phrase(kind, row["beatsPerBarB"]) for kind in kinds)))
            else:
                others.append("%s：aligned" % label)
        backs = [item for row in owners for item in row["runs"]
                 if item["k"] == 0 and item["start"] > group["end"]]
        back = min(backs, key=lambda item: item["start"]) if backs else None
        out.append({"song": song, "start": group["start"], "end": group["end"],
                    "pairs": pairs_here, "pairCount": len({entry["pair"] for entry in pairs_here}),
                    "support": max(item["support"] for item in pairs_here),
                    "others": others,
                    "reconverges": back is not None,
                    "reconvergeAt": back["start"] if back else None,
                    "headline": "%s %s" % (pairs_here[0]["pair"], pairs_here[0]["label"])})
    out.sort(key=lambda segment: (-segment["support"], segment["start"]))
    return out


def payload(songs: list[str]) -> dict:
    """页面读的那份 JSON。观测事实、来源 health、异常区段都在，原因解释留在 notes 里。"""
    rows = [classify(song) for song in songs]
    counts = {}
    for row in rows:
        counts[row["class"]] = counts.get(row["class"], 0) + 1
    out = []
    diagnoses = []
    for row in rows:
        path = dr.DOCS / ("%s.mgmcs.json" % row["song"])
        title = ""
        audio = ""
        if path.exists():
            song = json.loads(path.read_text(encoding="utf-8")).get("song") or {}
            title = song.get("title") or ""
            audio = song.get("audioPath") or song.get("audioFileName") or ""
        item = diagnose_song(row["song"], row)
        diagnoses.append(item)
        out.append({"song": row["song"], "title": title, "class": row["class"],
                    "status": "OK" if row["class"] == "clean" else "REVIEW",
                    "flags": row["flags"], "suspects": row["suspects"], "notes": row["notes"],
                    "phase": row["phase"], "human": row["human"],
                    "audioPath": audio, "driftSpot": drift_spot(row["song"]),
                    "singleFlips": row["singles"],
                    "triage": {key: item[key] for key in
                               ("diagnosis", "affectedRange", "suspectSource", "resolution",
                                "needsHumanReview", "attribution", "evidence")},
                    "layerA": row["layerA"],
                    "readings": [{"name": name, "health": health, "meterMix": mix, "measures": count}
                                 for name, health, mix, count in row["readings"]],
                    "segments": segments(row["song"])})
    out.sort(key=lambda item: (item["status"] == "OK", -len(item["segments"]), item["song"]))
    return {"generatedBy": "timing_health_table.py",
            "note": "观测事实 + 来源 health + 异常区段；原因解释在 notes 里，永远带『待人工确认』。"
                    "support 是连续多少个 matched measure 支持这个 offset，不是投票。",
            "musicBase": MUSIC_BASE, "validator": VALIDATOR_PAGE,
            "counts": counts, "songs": out, "queue": queue_of(out),
            "triageCounts": tri.summarise(diagnoses)}


def drift_spot(song: str) -> dict | None:
    """A 层偏得最多的那个 4 小节窗（页面用它做"只听 A 层问题"的那几条）。

    窗口是从第一颗鼓点起按 4 小节滚的，所以起点要按 min(事件时间) 算，
    不能拿网格起点当 0——这两件事差 4 倍，早先算错过一次。
    """
    grid = dr.affine_grid(song)
    events = dr.events_of(song)
    if not grid or not events:
        return None
    fit = dr.subdivision_fit(events, grid["origin"], grid["beatSeconds"])
    windows = fit.get("windows") or []
    if len(windows) < 2:
        return None
    span = 16 * grid["beatSeconds"]
    start0 = min(time for time, _ in dr.raw_deltas(events, grid["origin"], grid["beatSeconds"]))
    worst = max(range(len(windows)), key=lambda index: abs(windows[index]))
    start = start0 + worst * span
    return {"start": start, "end": start + span, "offsetSixteenths": round(windows[worst], 3),
            "windows": len(windows)}


def queue_of(song_rows: list[dict]) -> list[dict]:
    """听审队列：一处一条，直接带音频与要回答的问题，按重要性排。

    页面不该让人一首一首点开再自己去比时间——那是人工搬砖。队列把 31 首里的
    16 处异常拧成一条清单：点一下放到那一段、放完停住、按 N 下一处。

    每处只留一段**听得了的窗口**（默认 24 秒，往前带 2 秒）。像 kaitou 那种整首差一拍的，
    真正要判的其实就一处，没必要把 3 分钟都听完，所以窗口掐短、同时在卡片上写明它原本有多长。
    """
    severity = {"persistent ±1-beat disagreement": 0, "persistent ±2-beat disagreement": 1,
                "continuous slide": 2, "unresolved": 3, "local drift": 4, "single flip": 5,
                "clean": 6}
    window = 24.0

    def spot_of(row, kind, start, end, display_class=None, **extra):
        listen_start = max(0.0, start - 2.0)
        spot = {"song": row["song"], "title": row["title"], "class": row["class"],
                # 这一条在队列里按什么排、卡片上写什么：整体干净但有一处单点跳变的歌，
                # 这一处的类别就是单点跳变，不能写成"clean"。
                "queueClass": display_class or row["class"],
                "audioPath": row["audioPath"], "kind": kind,
                "start": start, "end": end,
                "listenStart": listen_start,
                "listenEnd": min(end + 2.0, listen_start + window),
                "spanSeconds": round(end - start, 1),
                "longSpan": (end - start) > 60.0}
        spot.update(extra)
        return spot

    spots = []
    for row in song_rows:
        triage = row.get("triage") or {}
        # 队列默认只放"分诊没解释掉"的：人工只听那些。其余照样在页面上，
        # 但要显式勾一下才出现（"已自动解释"不是"不用看"，是"不必专门听"）。
        review = bool(triage.get("needsHumanReview"))
        if row["class"] == "local drift" and row.get("driftSpot"):
            spot = row["driftSpot"]
            spots.append(spot_of(row, "drift", spot["start"], spot["end"], support=0,
                                 review=review, diagnosis=triage.get("diagnosis"),
                                 pairCount=0, pairs=[], others=[], reconverges=False,
                                 reconvergeAt=None,
                                 suspicion="A 层最偏的一窗：偏移 %+.2f 个 16 分"
                                           % spot["offsetSixteenths"],
                                 question=QUESTIONS["local drift"]))
        for segment in row["segments"]:
            spots.append(spot_of(row, "segment", segment["start"], segment["end"],
                                 review=review, diagnosis=triage.get("diagnosis"),
                                 support=segment["support"], pairCount=segment["pairCount"],
                                 pairs=segment["pairs"], others=segment["others"],
                                 reconverges=segment["reconverges"],
                                 reconvergeAt=segment["reconvergeAt"],
                                 suspicion=next((text for text in row["notes"]
                                                 if text.startswith("疑似")), ""),
                                 question=QUESTIONS.get(row["class"], "")))
        # 同一处跳变会在好几对来源各报一次（它们指的是同一个时刻），合成一条，
        # 否则 haiiro 04:22 会在队列里连着出现两遍、happyend 05:01 出现三遍。
        flips = []
        for flip in sorted(row.get("singleFlips") or [], key=lambda item: item["time"]):
            if flips and flip["time"] - flips[-1]["time"] <= 3.0:
                flips[-1]["pairs"].append(flip["pair"])
                flips[-1]["jump"] = max(flips[-1]["jump"], flip["jump"])
                continue
            flips.append({"time": flip["time"], "jump": flip["jump"], "pairs": [flip["pair"]]})
        for flip in flips:
            spots.append(spot_of(row, "single", max(0.0, flip["time"] - 6.0),
                                 flip["time"] + 6.0, support=1, pairCount=len(flip["pairs"]),
                                 review=review, diagnosis=triage.get("diagnosis"),
                                 pairs=[], others=["%s：这里跳了 %.2f 拍" % (name, flip["jump"])
                                                   for name in flip["pairs"]],
                                 reconverges=False, reconvergeAt=None,
                                 suspicion="只一处、没持续成一段（%s）"
                                           % "、".join(flip["pairs"]),
                                 question=QUESTIONS["single flip"],
                                 display_class="single flip"))
    spots.sort(key=lambda spot: (severity.get(spot["queueClass"], 9), -spot["support"],
                                 spot["song"], spot["start"]))
    # 第一遍：每一类挑"最值得先学的那一处"。不是挑支持量最大的——那是给统计看的，
    # 给人看的第一处应该短、有多条来源互相印证、而且已经能指出嫌疑来源。
    def worth(spot):
        span = spot["spanSeconds"]
        return (spot["pairCount"] >= 2,                            # 多于一对来源指这里
                bool(spot.get("suspicion")),                       # 已经能指出嫌疑
                3.0 <= span <= 60.0,                               # 听得了的一段，不是整首
                spot.get("offsetSixteenths") or 0,                  # 漂移类：偏得多的
                spot["support"])
    best = {}
    for spot in spots:
        key = spot["queueClass"]
        if key not in best or worth(spot) > worth(best[key]):
            best[key] = spot
    for spot in spots:
        spot["firstPass"] = any(spot is chosen for chosen in best.values())
    return spots


def report(songs: list[str]) -> tuple[str, list[dict]]:
    rows = [classify(song) for song in songs]
    twins = audio_twins(songs)
    twin_of = {song: group for group in twins.values() for song in group}
    counts = {}
    for row in rows:
        counts[row["class"]] = counts.get(row["class"], 0) + 1
    lines = ["# 全库 timing 体检表", "",
             "31 首歌，2026-10-11 那一轮：五条读法（人 / BeatThis / madmom / workbench / Mapperatorinator）齐了之后的第一张表。",
             "", "分类：" + "　".join("**%s** %d" % (name, counts[name]) for name in sorted(counts)), "",
             "只汇总现有两层诊断：**A 层** 拍子跟没跟住（ADTOF 鼓点对 affine 网格，held-out 校正固定偏移后），",
             "**B 层** 各来源小节头两两的相位关系。分类只写**观测事实**，不判\"谁对\"；",
             "`support` 是连续多少个 matched measure 支持这个 offset（**不是投票**，没有多数表决）。",
             "像 `clean` 只表示\"机器读法之间无显著分歧\"，**不等于真值正确**。",
             "人那条（手机按的小节头）不进分类，只列在下面详情里——它多数只有几个 matched measure，"
             "拿它判\"这首歌是什么错\"没有依据。",
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
        if row["song"] in twin_of:
            others = [name for name in twin_of[row["song"]] if name != row["song"]]
            lines.append("  - 与 %s 是**同一份音频**，各跑了一次 Mapperatorinator，两次采样不一样"
                         "（一次干净、一次有洞或拍间距异常）：这是采样的差别，不是歌的问题——"
                         "单次采样不能当稳定参照。" % "、".join(others))
    lines.append("")
    lines.append("## 哪首歌要听哪一段")
    lines.append("")
    lines.append("有 B 层分歧、或 A 层有提示（局部漂移 / 拍序列不成直线）的歌列在这里；其余两层都干净的歌曲不必整曲审。")
    lines.append("")
    for row in rows:
        if row["class"] == "clean" and not row["notes"]:
            continue
        lines.append("### %s — %s" % (row["song"], row["class"]))
        hot = hot_spans(row["song"])
        if hot:
            lines.append("- **先听**：" + "；".join(hot))
        for line in detail(row["song"]):
            lines.append("- " + line)
        lines.append("- 人那条：%s" % row["human"])
        for text in row["notes"]:
            lines.append("- " + text)
        lines.append("")
    return "\n".join(lines), rows


def triage_markdown(songs: list[str]) -> str:
    """分诊表：成功标准是"要人听的有多少首"，不是"分类有多细"。"""
    rows = [(song, classify(song)) for song in songs]
    items = [(song, diagnose_song(song, row)) for song, row in rows]
    buckets = tri.summarise([item for _song, item in items])
    total = len(items)
    lines = ["# 全库 timing 分诊表", "",
             "只基于已经量出来的东西（来源 health、小节数、offset 段、持续多少个 matched measure、"
             "是否 reconverge），不新增测量、也不判哪条网格是真值。", "",
             "## 结论：%d 首里只有 %d 首需要人听" % (total, len(buckets["humanReview"])), "",
             "| 处理结果 | 首数 | 歌 |", "|---|---|---|",
             "| 自动通过（读法之间无分歧） | %d | %s |"
             % (len(buckets["autoPass"]), "、".join(buckets["autoPass"]) or "—"),
             "| 自动解释并局部降级某条来源 | %d | %s |"
             % (len(buckets["autoExplained"]), "、".join(buckets["autoExplained"]) or "—"),
             "| 只标 timing drift，仍可继续分析 | %d | %s |"
             % (len(buckets["driftOnly"]), "、".join(buckets["driftOnly"]) or "—"),
             "| 登记为局部（单点 / 合回来），不升级 | %d | %s |"
             % (len(buckets["logged"]), "、".join(buckets["logged"]) or "—"),
             "| 人工已听过并定案（按人的判定收口） | %d | %s |"
             % (len(buckets["humanConfirmed"]), "、".join(buckets["humanConfirmed"]) or "—"),
             "| **需要人工听** | %d | %s |"
             % (len(buckets["humanReview"]), "、".join(buckets["humanReview"]) or "—"),
             "",
             "## 一首歌一行", "",
             "| 歌 | diagnosis | 受影响区段 | 嫌疑来源 | 归因 | 人工？ | evidence（来源 / health / offset / 小节异常 / reconverge） |",
             "|---|---|---|---|---|---|---|"]
    for song, item in items:
        ranges = "、".join("%s–%s" % (dr.clock(span["start"]), dr.clock(span["end"]))
                          for span in item["affectedRange"]) or "—"
        evidence = item["evidence"]
        sources = "、".join("%s %s" % (reading["name"], reading["health"]) for reading in evidence["sources"])
        offsets = "、".join("%s k=%+d(support %d)" % (offset["pair"], offset["k"], offset["support"])
                           for offset in evidence["offsets"][:4]) or "—"
        anomalies = "、".join("%s 异常小节 %d" % (item["source"], item["oddBars"])
                             for item in evidence["barLengthAnomaly"]) or "无"
        reconverge = ("%d 处" % len(evidence["reconvergence"])) if evidence["reconvergence"] else "无"
        lines.append("| %s | %s | %s | %s | %s | %s | %s ｜ %s ｜ 小节异常：%s ｜ reconverge：%s |"
                     % (song, item["diagnosis"], ranges, item["suspectSource"] or "—",
                        item["attribution"] or "—", "**是**" if item["needsHumanReview"] else "否",
                        sources, offsets, anomalies, reconverge))
    lines += ["", "## 需要人听的那几首（六字段全量）", ""]
    for song, item in items:
        if not item["needsHumanReview"]:
            continue
        lines.append("### %s — %s" % (song, item["diagnosis"]))
        lines.append("- affectedRange：" + ("、".join(
            "%s–%s（%.0fs）" % (dr.clock(span["start"]), dr.clock(span["end"]), span["seconds"])
            for span in item["affectedRange"]) or "—"))
        lines.append("- suspectSource：" + (item["suspectSource"] or "没有能归因的来源"))
        lines.append("- resolution：" + item["resolution"])
        lines.append("")
    lines += ["## 自动解释了谁（局部降级，不进人工）", ""]
    for song, item in items:
        if item["diagnosis"] in ("source_measure_count_error", "source_self_anomaly"):
            lines.append("- **%s** → 降级 %s：%s"
                         % (song, item["suspectSource"], item["resolution"]))
    lines += ["", "## 只标范围、不动结论的", ""]
    for song, item in items:
        if item["diagnosis"] in ("continuous_drift", "local_timing_drift",
                                 "single_flip", "reconvergence"):
            lines.append("- **%s** → %s：%s" % (song, item["diagnosis"], item["resolution"]))
    lines += ["", "第一版不做：全库 ensemble、不信 Mapperatorinator 单次采样、"
                  "不自动改任何网格、不改人工校准。分诊的降级只影响下游读这条结论时的准入，"
                  "原始 observation 一个数都不动。", ""]
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--json", action="store_true",
                        help="给页面用的 static/timing_health.json")
    parser.add_argument("--triage", action="store_true",
                        help="分诊表 reports/timing_triage.md")
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
    if args.json or args.write:
        JSON_PATH.parent.mkdir(parents=True, exist_ok=True)
        JSON_PATH.write_text(json.dumps(payload(songs), ensure_ascii=False, indent=1),
                             encoding="utf-8", newline="\n")
        print("已写 %s" % JSON_PATH)
    if args.triage or args.write:
        TRIAGE_PATH.parent.mkdir(parents=True, exist_ok=True)
        TRIAGE_PATH.write_text(triage_markdown(songs) + "\n", encoding="utf-8", newline="\n")
        print("已写 %s" % TRIAGE_PATH)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
