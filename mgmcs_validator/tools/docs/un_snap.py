import json, shutil
from pathlib import Path
docs = Path(r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\mgmcs_validator\static\docs")
made = []
for p in sorted(docs.glob("*.mgmcs.json")):
    if p.name.endswith(".raw.mgmcs.json"):
        continue
    d = json.loads(p.read_text(encoding="utf-8"))
    touched = 0
    for cal in d.get("calibrations") or []:
        if cal.get("source") != "human":
            continue
        tapped_bpm, tapped_time = cal.get("tappedBpm"), cal.get("tappedAudioTime")
        if tapped_bpm is None or tapped_time is None:
            continue
        cal["bpm"] = tapped_bpm
        cal["audioTime"] = tapped_time
        cal["note"] = "未吸附：手机那一次打点自己算出来的值（吸附后的值在文档的另一份里）"
        touched += 1
    if not touched:
        continue
    out = docs / (p.name[: -len(".mgmcs.json")] + ".raw.mgmcs.json")
    out.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    made.append((p.stem.split(".")[0], touched))
print("写出未吸附版本 %d 份：%s" % (len(made), "、".join("%s(%d)" % m for m in made[:6]) + " …"))
