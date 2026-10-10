import json, shutil
from pathlib import Path
docs = Path(r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\mgmcs_validator\static\docs")
BACKUP = Path(r"F:\vibe transcription\_mgmcs_index\backup_docs_before_fold")
BACKUP.mkdir(parents=True, exist_ok=True)
TOL_BEAT = 0.15          # 相位差在这个容差内算“同一套读法”
TOL_BPM = 0.005          # 速度差 0.5% 以内
MIN_CALS = 4             # 只在 >3 次人工校准的歌上做（研究者 2026-10-10 定的门槛）

def phase_beats(cal, grid_bpm):
    return None

report = []
for p in sorted(docs.glob("*.mgmcs.json")):
    d = json.loads(p.read_text(encoding="utf-8"))
    humans = [c for c in d.get("calibrations") or [] if c.get("source") == "human"]
    if len(humans) <= 3:
        continue
    kept, groups = [], []
    for cal in humans:
        placed = False
        for group in groups:
            head = group[0]
            if abs(cal.get("beatsPerBar", 4) - head.get("beatsPerBar", 4)) > 0:
                continue
            bpm = cal.get("bpm") or head.get("bpm") or 0
            if abs(bpm - (head.get("bpm") or 0)) > TOL_BPM * max(1.0, abs(head.get("bpm") or 1)):
                continue
            beat_seconds = 60.0 / (head.get("bpm") or 120)
            if abs(cal.get("audioTime", 0) - head.get("audioTime", 0)) <= TOL_BEAT * beat_seconds:
                group.append(cal)
                placed = True
                break
        if not placed:
            groups.append([cal])
    new_humans = []
    for group in groups:
        if len(group) == 1:
            new_humans.append(group[0]); continue
        rep = max(group, key=lambda c: len(c.get("rawTaps") or []))     # 按压最多的那遍当代表
        origins = [c.get("audioTime", 0) for c in group]
        bpms = [c.get("bpm", 0) for c in group]
        rep = dict(rep)
        rep["foldedFrom"] = [c.get("calibrationId") for c in group if c is not rep]
        rep["foldSpread"] = {"originSeconds": round(max(origins) - min(origins), 4),
                             "bpm": round(max(bpms) - min(bpms), 4),
                             "note": "同一套读法的多遍打点，按研究者 2026-10-10 的 pre-clean 收束成一条；原始各遍见 foldedFrom 与手机导出的会话文档"}
        new_humans.append(rep)
    if len(new_humans) == len(humans):
        continue
    shutil.copy2(p, BACKUP / p.name)
    d["calibrations"] = new_humans + [c for c in d["calibrations"] if c.get("source") != "human"]
    p.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    report.append((p.name[: -len(".mgmcs.json")], len(humans), len(new_humans)))

print("收束了 %d 份文档：" % len(report))
for name, before, after in report:
    print("  %-14s 人工线 %d → %d" % (name, before, after))
