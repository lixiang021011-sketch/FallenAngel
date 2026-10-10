"""自动分诊：把已经量出来的故障形状翻译成"要不要人来听"。

输入只有已经存在的 A/B 层结果（来源 health、小节数、每小节拍数、offset 段、持续多少个
matched measure、有没有 reconvergence）。**不新增任何测量**，也不判断哪条网格是真值。

输出固定六个字段（研究者 2026-10-11 定的）：

```
diagnosis        这一处是什么形状
affectedRange    只标受影响的区段，不把整首歌定性
suspectSource    要局部降级的那条来源（没有就是 None）
evidence         哪几条 source / 各自 health / offset / 持续多少 matched measures /
                 有没有小节长度异常 / 有没有 reconverge
resolution       怎么处理这条结论
needsHumanReview 只有真正解释不掉的才 true
```

六条规则（第一版，覆盖研究者列的那六类）：

1. 半小节（±bar/2）分歧 + 嫌疑来源自己的小节长度异常（连续 2 拍小节 / 接缝 / 不等长）
   + 另外的独立健康来源互相一致 → `source_measure_count_error`，局部降级那条来源
2. 整拍（±1）分歧 → 嫌疑来源自身异常就降级它；两边都健康且**一直没合回来**才
   `unresolved_phase` 进人工
3. 相位连续滑（段多、k 走遍正负、残差悬中间）→ `continuous_drift`：这是 tempo / affine
   假设的风险，不是相位错位
4. 单点跳变 → `single_flip`，只登记
5. B 层干净但 A 层局部漂 → `local_timing_drift`，不判小节错
6. 分歧之后合回来 → `reconvergence`：只标那段，不把整首歌定性

顺序上 1 / 2 是"能指出谁错"的解释，优先；解释不掉时才轮到 3 / 6 这种"只标范围"；
4 / 5 是最轻的登记。解释不掉又没合回来的，进人工。
"""

from __future__ import annotations

import re
import statistics

HALF_BAR_TOLERANCE = 0.01
ODD_BAR_TOLERANCE = 0.20
ENOUGH_ODD_BARS = 2            # 一处短小节不算"数错模式"，两处起才算
ENOUGH_SUPPORT = 4             # 少于 4 个 matched measure 的分歧：登记，不升级成人工


def bar_steps(measures: list[float]) -> list[float]:
    return [b - a for a, b in zip(measures, measures[1:]) if b > a]


def odd_bars(reading: dict) -> dict:
    """这条读法自己的小节是不是不等长（2 拍小节、接缝、局部重估都会露在这里）。

    来源无关：不看它叫什么名字，只看它报出来的小节长度里有多少个偏离中位数 > 20%。
    """
    steps = bar_steps(reading.get("measures") or [])
    if len(steps) < 4:
        return {"count": 0, "median": None, "short": 0, "long": 0}
    median = statistics.median(steps)
    if median <= 0:
        return {"count": 0, "median": None, "short": 0, "long": 0}
    odd = [step for step in steps if abs(step - median) / median > ODD_BAR_TOLERANCE]
    return {"count": len(odd), "median": median,
            "short": sum(1 for step in odd if step < median),
            "long": sum(1 for step in odd if step > median)}


def two_beat_bars(reading: dict) -> int:
    found = re.search(r"2 拍×(\d+)", reading.get("meterMix") or "")
    return int(found.group(1)) if found else 0


def self_anomaly(reading: dict, median_measures: float) -> list[str]:
    """这条来源**自己**看起来有没有问题（与它跟别人吵不吵架无关）。"""
    reasons = []
    if reading.get("health") not in (None, "ok"):
        reasons.append("health=%s" % reading["health"])
    odd = odd_bars(reading)
    if odd["count"] >= ENOUGH_ODD_BARS:
        reasons.append("小节长度异常 %d 处（短 %d / 长 %d）"
                       % (odd["count"], odd["short"], odd["long"]))
    if median_measures and len(reading.get("measures") or []) < 0.7 * median_measures:
        reasons.append("小节头只有 %d 个（同类中位数 %.0f）"
                       % (len(reading["measures"]), median_measures))
    return reasons


def blame_strength(reading: dict, median_measures: float) -> tuple:
    """几条来源都"看起来有毛病"时，谁更该被降级。

    health 直接被标了问题的排第一（那是它自己承认输出有问题），然后是异常小节多、
    小节头偏少的。像 round2 那种：mapperatorinator 的 health 是 suspect（同一份音频
    两次采样里坏的那次），madmom 只是小节长度有点不齐——该降级的是前者。
    """
    odd = odd_bars(reading)
    return (reading.get("health") not in (None, "ok"), odd["count"],
            -len(reading.get("measures") or []))


def pick_blame(names: list[str], readings: dict, median_measures: float) -> str | None:
    candidates = [name for name in names
                  if self_anomaly(readings.get(name, {}), median_measures)]
    if not candidates:
        return None
    return max(candidates, key=lambda name: blame_strength(readings.get(name, {}), median_measures))


def is_slide(pair: dict) -> bool:
    """连续滑：段多、k 走遍正负、残差悬在中间（和"停错一格"分开的那套判据）。"""
    offsets = [run for run in pair["runs"] if run["k"] != 0]
    if len(offsets) < 2:
        return False
    means = [sum(abs(v) for v in run["residuals"]) / len(run["residuals"]) for run in offsets]
    kinds = {run["k"] for run in offsets}
    trend = abs((pair.get("trend") or {}).get("beatsPerMinute") or 0.0)
    return (trend >= 0.5 or len({k > 0 for k in kinds}) > 1) \
        and sum(means) / len(means) >= 0.10


def offset_runs(pair: dict) -> list[dict]:
    return [run for run in pair["runs"] if run["k"] != 0]


def half_bar_k(pair: dict) -> bool:
    bar = pair.get("beatsPerBarB") or 4.0
    return any(abs(abs(run["k"]) * 2 - bar) < HALF_BAR_TOLERANCE for run in offset_runs(pair))


def reconverges(pair: dict, run: dict) -> float | None:
    later = [item for item in pair["runs"] if item["k"] == 0 and item["start"] > run["end"]]
    return min(later, key=lambda item: item["start"])["start"] if later else None


def diagnose(facts: dict) -> dict:
    """一首歌的分诊结论。facts 由 timing_health_table 从现有计算里挑出来喂进来。"""
    # 人工定案优先：人已经听过并且指出哪条读法是对的，就不再让规则去猜。
    verdict = facts.get("verdict")
    readings = {reading["name"]: reading for reading in facts["readings"]}
    machines = [reading for reading in facts["readings"]
                if not reading["name"].startswith("human")]
    counts = [len(reading["measures"]) for reading in machines]
    median_measures = statistics.median(counts) if counts else 0
    # 人那条不参与分诊：它多数只有几个小节头，"小节太少"会被误判成来源自身异常，
    # 而它本来就不是机器读法（这一条和体检表的分类口径保持一致）。
    pairs = [pair for pair in facts["pairs"]
             if pair.get("usable") and not pair["a"].startswith("human")
             and not pair["b"].startswith("human")]
    offsets = [(pair, run) for pair in pairs for run in offset_runs(pair)]
    slides = [pair for pair in pairs if is_slide(pair)]
    # 滑过去的那几对不能用来说"停错了几拍"：它们的 ±1/±2 是相位走过整数格留下的痕迹。
    parked = [(pair, run) for pair, run in offsets if pair not in slides]
    # 按**段**分类，不按对：同一对里可能既有半小节段又有整拍段（kaitou 就是）。
    half = [(pair, run) for pair, run in parked
            if abs(abs(run["k"]) * 2 - (pair.get("beatsPerBarB") or 4.0)) < HALF_BAR_TOLERANCE]
    whole = [(pair, run) for pair, run in parked if abs(abs(run["k"]) - 1) < HALF_BAR_TOLERANCE]

    evidence_sources = [{"name": reading["name"], "health": reading.get("health"),
                         "measures": len(reading["measures"])}
                        for reading in machines]
    evidence = {"sources": evidence_sources,
                "offsets": [{"pair": pair["a"] + "↔" + pair["b"], "k": run["k"],
                             "support": run["count"],
                             "residualRange": [min(run["residuals"]), max(run["residuals"])]}
                            for pair, run in offsets],
                "barLengthAnomaly": [
                    {"source": reading["name"], "oddBars": odd_bars(reading)["count"],
                     "twoBeatBars": two_beat_bars(reading),
                     "selfAnomaly": self_anomaly(reading, median_measures)}
                    for reading in machines
                    if self_anomaly(reading, median_measures)],
                "reconvergence": [{"pair": pair["a"] + "↔" + pair["b"],
                                   "at": reconverges(pair, run)}
                                  for pair, run in offsets if reconverges(pair, run) is not None]}

    def ranges(items, merge_seconds: float = 12.0, keep: int = 6):
        """区段要合并、要限量：给人工看的清单里出现 22 段，等于没给。"""
        spans = sorted(({"start": run["start"], "end": run["end"]} for _pair, run in items),
                       key=lambda span: span["start"])
        merged = []
        for span in spans:
            if merged and span["start"] - merged[-1]["end"] <= merge_seconds:
                merged[-1]["end"] = max(merged[-1]["end"], span["end"])
                continue
            merged.append(dict(span))
        for span in merged:
            span["seconds"] = round(span["end"] - span["start"], 1)
        merged.sort(key=lambda span: -span["seconds"])
        return merged[:keep]

    # 谁在每一对分歧里都出现——只出现一次的那条（或那两条）就是和别人不同的
    suspects = set()
    for pair, _run in half + whole:
        names = {pair["a"], pair["b"]}
        suspects = names if not suspects else suspects & names
    suspects = sorted(suspects)

    def all_offenders_include(name):
        return all(name in (pair["a"], pair["b"]) for pair, _run in half + whole)

    def independent_agreement(odd_name):
        """除嫌疑源以外的两条以上来源是不是互相一致（没有一对在不含它的分歧里）。"""
        others = [pair for pair in pairs
                  if odd_name not in (pair["a"], pair["b"]) and offset_runs(pair)]
        return len(others) == 0

    out = {"song": facts["song"], "diagnosis": "clean", "affectedRange": [],
           "suspectSource": None, "evidence": evidence,
           "resolution": "机器读法之间无显著分歧；不等于真值正确。",
           "needsHumanReview": False, "attribution": None}

    if verdict and verdict.get("correctSource"):
        correct = verdict["correctSource"]
        others = sorted({name for pair, _run in offsets for name in (pair["a"], pair["b"])
                         if name != correct})
        wrong = [pair["a"] + "↔" + pair["b"] for pair, _run in offsets
                 if correct in (pair["a"], pair["b"])]
        if not offsets:
            return {**out, "diagnosis": "human_confirmed_clean",
                    "resolution": "人工听过并确认 %s 的小节线是对的（范围 %s）；读法之间本来也没有分歧。"
                                  % (correct, "、".join(verdict.get("scope") or []))}
        return {**out, "diagnosis": "human_confirmed_source",
                "affectedRange": ranges(offsets), "suspectSource": "、".join(others) or None,
                "attribution": "human", "needsHumanReview": False,
                "resolution": "人工听过（%s）并确认 %s 的小节线是对的：与它分歧的读法按错处理"
                              "（%s）。这一首不再需要人工。"
                              % ("、".join(verdict.get("scope") or ["—"]), correct,
                                 "、".join(wrong) or "—")}

    def structural_attribution(runs_here: list[tuple]) -> str | None:
        """结构归因：唯一一条来源站在所有分歧的另一边，而其余两条以上互相一致。

        这不是"投票定真值"，是"分歧的形状只有一种解释"：每一对分歧里都有它，
        而且不存在一对不含它的分歧。2 拍小节那类症状是更强的证据（走 bar-length 那条），
        这个只作为没有症状时的兜底；resolution 里会写明是哪一种，人工不必重判。
        """
        names = set()
        for pair, _run in runs_here:
            names = {pair["a"], pair["b"]} if not names else names & {pair["a"], pair["b"]}
        if len(names) != 1:
            return None
        odd = next(iter(names))
        if not all(odd in (pair["a"], pair["b"]) for pair, _run in runs_here):
            return None
        if any(pair for pair in pairs if odd not in (pair["a"], pair["b"]) and offset_runs(pair)):
            return None                       # 有一条不含它的分歧 → 不是"只有它不同"
        return odd

    if not offsets:
        if (facts.get("layerA") or {}).get("drift") == "局部明显":
            return {**out, "diagnosis": "local_timing_drift",
                    "affectedRange": facts.get("driftRange") or [],
                    "resolution": "B 层干净，只有 A 层显示拍子在局部漂：不判小节错，"
                                  "下游用到拍层结论时带保留。",
                    "evidence": evidence}
        if facts.get("singles"):
            return {**out, "diagnosis": "single_flip",
                    "affectedRange": [{"start": flip["time"] - 6, "end": flip["time"] + 6,
                                       "seconds": 12} for flip in facts["singles"]],
                    "resolution": "单点跳变，前后立即恢复：只登记，不当异常。",
                    "evidence": evidence}
        return out

    # 规则 1 / 2：能指出是谁错
    if half:
        blamed = pick_blame(suspects, readings, median_measures)
        if blamed and all_offenders_include(blamed) and independent_agreement(blamed):
            reading = readings.get(blamed, {})
            reason = "；".join(self_anomaly(reading, median_measures))
            return {**out, "diagnosis": "source_measure_count_error",
                    "affectedRange": ranges(half), "suspectSource": blamed,
                    "attribution": "bar-length",
                    "resolution": "只在这一段降级 %s 的小节线（%s），其余读法互相一致；"
                                  "不进人工。" % (blamed, reason),
                    "evidence": evidence}
        odd = structural_attribution(half)
        if odd:
            # 结构归因不再是"自动降级"：2026-10-11 的人工定案给出反例——
            # kaitou 上"只有 workbench 一条不同"，而 workbench 才是准的。
            # 所以只报形状，结论交给人。
            return {**out, "diagnosis": "structure_only_attribution",
                    "affectedRange": ranges(half), "suspectSource": odd,
                    "attribution": "structure",
                    "needsHumanReview": True,
                    "resolution": "分歧的形状是「只有 %s 一条不同、其余互相一致」，但它自己没报出"
                                  "异常小节长度。这类形状有过反例（kaitou：与众不同的那条才是准的），"
                                  "所以只报形状、不自动降级。" % odd,
                    "evidence": evidence}
    if whole:
        blamed = pick_blame(suspects, readings, median_measures)
        unreturned = [(pair, run) for pair, run in whole if reconverges(pair, run) is None]
        if blamed:
            return {**out, "diagnosis": "source_self_anomaly",
                    "affectedRange": ranges(whole), "suspectSource": blamed,
                    "attribution": "bar-length",
                    "resolution": "整拍分歧，且 %s 自身有异常（%s）：局部降级这条来源，不进人工。"
                                  % (blamed, "；".join(self_anomaly(readings.get(blamed, {}), median_measures))),
                    "evidence": evidence}
        odd = structural_attribution(whole)
        if odd:
            return {**out, "diagnosis": "structure_only_attribution",
                    "affectedRange": ranges(whole), "suspectSource": odd,
                    "attribution": "structure",
                    "needsHumanReview": True,
                    "resolution": "整拍分歧，形状是「只有 %s 不同、其余互相一致」，但它没有自身异常"
                                  "症状：只报形状，不自动降级（这类形状有过反例）。" % odd,
                    "evidence": evidence}
        # 支持量很小的（< 4 个 matched measure）到结尾没合回来，也不值得占人工：
        # 登记成单点，写清 support。
        unreturned = [(pair, run) for pair, run in unreturned
                      if run["count"] >= ENOUGH_SUPPORT]
        if unreturned:
            held = "、".join(sorted({pair["a"] + "↔" + pair["b"] for pair, _run in unreturned}))
            return {**out, "diagnosis": "unresolved_phase",
                    "affectedRange": ranges(unreturned),
                    "resolution": "整拍分歧（%s），相关来源自身都没有异常、到结尾也没合回来："
                                  "这才是需要人听的一类。" % held,
                    "needsHumanReview": True, "evidence": evidence}

    # 规则 3 / 6 / 4：解释不掉就只标范围
    if slides and not parked:
        return {**out, "diagnosis": "continuous_drift",
                "affectedRange": ranges([(pair, run) for pair in slides for run in offset_runs(pair)]),
                "resolution": "相位连续滑：这是 tempo / affine 假设的风险（两条线的小节长度略有出入），"
                              "不是相位错位；不判小节错。",
                "evidence": evidence}
    returned = [(pair, run) for pair, run in offsets if reconverges(pair, run) is not None]
    unreturned = [(pair, run) for pair, run in parked if reconverges(pair, run) is None]
    big = [(pair, run) for pair, run in unreturned if run["count"] >= ENOUGH_SUPPORT]
    if parked and not unreturned:
        return {**out, "diagnosis": "reconvergence",
                "affectedRange": ranges(parked),
                "resolution": "分歧之后都合回来了：只标这些区段，不把整首歌定性为异常。",
                "evidence": evidence}
    if big:
        return {**out, "diagnosis": "reconvergence",
                "affectedRange": ranges(big),
                "resolution": "部分区段分歧后合回来了（那些只标范围）；剩下 %d 段到结尾没合回来"
                              "（support 最大 %d），需要人听一次。"
                              % (len(big), max(run["count"] for _pair, run in big)),
                "needsHumanReview": True, "evidence": evidence}
    if unreturned:
        return {**out, "diagnosis": "reconvergence",
                "affectedRange": ranges(unreturned),
                "resolution": "分歧都没合回来，但每段只有 %d 个 matched measure 支持（低于 %d）："
                              "登记为局部单点，不占人工。"
                              % (max(run["count"] for _pair, run in unreturned), ENOUGH_SUPPORT),
                "evidence": evidence}
    if returned:
        return {**out, "diagnosis": "reconvergence",
                "affectedRange": ranges(returned),
                "resolution": "分歧之后合回来了：只标这些区段。",
                "evidence": evidence}
    return {**out, "diagnosis": "unresolved_measure_disagreement",
            "affectedRange": ranges(offsets),
            "resolution": "有持续分歧，但既没有来源自身异常、也没合回来：需要人听一次。",
            "needsHumanReview": True, "evidence": evidence}


def summarise(diagnoses: list[dict]) -> dict:
    buckets = {"autoPass": [], "autoExplained": [], "driftOnly": [], "humanConfirmed": [],
               "logged": [], "humanReview": []}
    for item in diagnoses:
        name = item["diagnosis"]
        # needsHumanReview 说了算：一个"局部合回来"的诊断如果还有大段没合回来，
        # 它照样要人来听，不能因为名字落在 logged 里就被统计漏掉。
        if item.get("needsHumanReview"):
            buckets["humanReview"].append(item["song"])
        elif name in ("human_confirmed_source", "human_confirmed_clean"):
            buckets["humanConfirmed"].append(item["song"])
        elif name == "clean":
            buckets["autoPass"].append(item["song"])
        elif name in ("source_measure_count_error", "source_self_anomaly"):
            buckets["autoExplained"].append(item["song"])
        elif name in ("continuous_drift", "local_timing_drift"):
            buckets["driftOnly"].append(item["song"])
        elif name in ("single_flip", "reconvergence"):
            buckets["logged"].append(item["song"])
        else:
            buckets["humanReview"].append(item["song"])
    return buckets
