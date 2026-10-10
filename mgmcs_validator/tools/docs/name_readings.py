"""给每条校准写上正式的 reading 名字，并把 librosa 那条从 `machine` 改名过来。

研究者 2026-10-10 定的四个名字（四组 observation，不是四票表决）：

    human_unquantized   人打的那一遍，未吸附（只在 *.raw 变体里）
    human_snapped       人打的那一遍，被工具吸附到机器网格上（合并文档里的那份；
                        它是个派生读法，标签要如实写出来，不能冒充人的原始读数）
    workbench           隔壁那条：鼓起音定 16 分格 + Beat This! 定相位与强拍
    librosa             machine_beats.py 的拍格（工具写出来的 id 是 'machine'，这里改名）
    madmom              downbeats_madmom.py 的小节线

只改这些标签字段，**不动任何数值**：bpm / audioTime / barLines / rawTaps 一个字节不碰。
幂等：已经是目标名字就跳过。

    python tools/docs/name_readings.py [--dry-run]
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DOCS = REPO / "static" / "docs"

# 校准 id / 来源 → reading 名字。id 优先，因为 'machine' 要改名成 'librosa'。
BY_ID = {"machine": ("librosa", "librosa"), "madmom": ("madmom", "madmom"),
         "workbench": ("workbench", "workbench")}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    counts, renamed = {}, 0
    for path in sorted(DOCS.glob("*.mgmcs.json")):
        raw_variant = ".raw.mgmcs.json" in path.name
        doc = json.loads(path.read_text(encoding="utf-8"))
        touched = False
        for cal in doc.get("calibrations") or []:
            cid = str(cal.get("calibrationId") or "")
            if cid in BY_ID:
                new_id, reading = BY_ID[cid]
                if cid == "machine":
                    cal["calibrationId"] = new_id
                    renamed += 1
                    touched = True
                if cal.get("reading") != reading:
                    cal["reading"] = reading
                    touched = True
            elif cal.get("source") == "human":
                reading = "human_unquantized" if raw_variant else "human_snapped"
                if cal.get("reading") != reading:
                    cal["reading"] = reading
                    touched = True
            else:
                reading = cal.get("reading") or "unknown"
            counts[cal.get("reading") or "unknown"] = counts.get(cal.get("reading") or "unknown", 0) + 1
        if touched and not args.dry_run:
            path.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")

    print("%s改名 machine → librosa：%d 处" % ("[dry-run] " if args.dry_run else "", renamed))
    for name in sorted(counts):
        print("   %-20s %d 条校准" % (name, counts[name]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
