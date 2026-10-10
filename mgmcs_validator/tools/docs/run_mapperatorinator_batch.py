"""把 Mapperatorinator 的 timing observation 跑满 static/docs 里的每一首歌。

跑的是什么、为什么这么跑（2026-10-10 冻结的配方）：

```
inference.py  audio_path=<in>\\<歌>_full.<ext>  output_path=<out>\\<歌>_timing_events
              gamemode=3 output_type=[TIMING] dump_timing_events=true
```

`dump_timing_events=true` 是本机那份 Mapperatorinator 的小补丁（见
`tools/docs/patches/mapperatorinator-dump-timing-events.patch`）：把模型自己那串
BEAT / MEASURE / TIMING_POINT 原样落成 `timing_events.json`。不加它，measure 会在导出
成 .osu 时被折叠掉，读回来只能靠"从红线起每 meter 拍"这个假设补——那正好是观察者不该做的。

**piecewise timing 原样保留**：这里只是把音频喂进去、把输出接出来，不做任何重采样、
不拟合、不合并。每首歌独立一次推理，音频隔开，不会互相串。

    python tools/docs/run_mapperatorinator_batch.py --all            # 缺哪首跑哪首
    python tools/docs/run_mapperatorinator_batch.py --song flyway    # 只跑一首
    python tools/docs/run_mapperatorinator_batch.py --all --force    # 全部重跑
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DOCS = REPO / "static" / "docs"
MANIFEST = Path(r"F:\workbench曲库\_batch\manifest.json")
LIBRARY = Path(r"F:\workbench曲库")
MAPPER_REPO = Path(r"D:\fa_tools\mapperatorinator\repo")
MAPPER_PY = Path(r"D:\fa_tools\mapperatorinator\venv\Scripts\python.exe")
MAPPER_IN = Path(r"D:\fa_tools\mapperatorinator\in")
MAPPER_OUT = Path(r"D:\fa_tools\mapperatorinator\out")


def library_songs() -> list[str]:
    return sorted(path.name[: -len(".mgmcs.json")] for path in DOCS.glob("*.mgmcs.json")
                  if not path.name.endswith(".raw.mgmcs.json"))


def audio_for(song: str) -> Path:
    """这首歌的音频：优先清单里的那条，没有就按文档里的文件名去曲库里找。"""
    if MANIFEST.exists():
        for item in json.loads(MANIFEST.read_text(encoding="utf-8"))["songs"]:
            if item["name"] == song and Path(item["audio"]).exists():
                return Path(item["audio"])
    doc = json.loads((DOCS / ("%s.mgmcs.json" % song)).read_text(encoding="utf-8"))
    name = (doc.get("song") or {}).get("audioFileName")
    if name and (LIBRARY / name).exists():
        return LIBRARY / name
    raise FileNotFoundError("找不到 %s 的音频（清单和文档都没给出可用的路径）" % song)


def staged_audio(song: str) -> Path:
    """把音频按 `<歌>_full.<ext>` 摆到 Mapperatorinator 的 in\\ 里。

    摆这一步不是洁癖：接出来的时候要靠这个固定命名去取音频时长（判"输出是不是跑到歌外"），
    两个地方指向同一个文件，才不会有"喂进去的是这个、判时长的是那个"。
    """
    source = audio_for(song)
    target = MAPPER_IN / ("%s_full%s" % (song, source.suffix))
    if not target.exists() or target.stat().st_size != source.stat().st_size:
        MAPPER_IN.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
    return target


def run_song(song: str) -> tuple[bool, str]:
    out_dir = MAPPER_OUT / ("%s_timing_events" % song)
    audio = staged_audio(song)
    started = time.time()
    result = subprocess.run(
        [str(MAPPER_PY), "inference.py",
         "audio_path=%s" % audio, "output_path=%s" % out_dir,
         "gamemode=3", "output_type=[TIMING]", "dump_timing_events=true"],
        cwd=str(MAPPER_REPO), capture_output=True, text=True, encoding="utf-8", errors="replace")
    took = time.time() - started
    if result.returncode != 0:
        tail = (result.stderr or result.stdout or "").strip().splitlines()[-4:]
        return False, "失败（%.0fs）：%s" % (took, " / ".join(tail))
    events = out_dir / "timing_events.json"
    if not events.exists():
        return False, "跑完了但没有 timing_events.json（补丁没生效？）"
    rows = json.loads(events.read_text(encoding="utf-8"))
    counts = {}
    for row in rows:
        counts[row["type"]] = counts.get(row["type"], 0) + 1
    return True, "%.0fs  beat %d  measure %d  timing_point %d" % (
        took, counts.get("beat", 0), counts.get("measure", 0), counts.get("timing_point", 0))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--song", action="append", default=[])
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--force", action="store_true", help="已经跑过的也重跑")
    args = parser.parse_args(argv)
    songs = args.song or (library_songs() if args.all else [])
    if not songs:
        parser.error("给 --song <歌> 或 --all")
    done, failed = 0, []
    for index, song in enumerate(songs, 1):
        out_dir = MAPPER_OUT / ("%s_timing_events" % song)
        if (out_dir / "timing_events.json").exists() and not args.force:
            print("[%2d/%2d] %-12s 已有，跳过" % (index, len(songs), song), flush=True)
            done += 1
            continue
        print("[%2d/%2d] %-12s 开始 %s" % (index, len(songs), song, time.strftime("%H:%M:%S")),
              flush=True)
        try:
            ok, message = run_song(song)
        except FileNotFoundError as error:
            ok, message = False, str(error)
        print("[%2d/%2d] %-12s %s" % (index, len(songs), song, message), flush=True)
        if ok:
            done += 1
        else:
            failed.append((song, message))
    print("完成 %d/%d%s" % (done, len(songs), "；失败 %s" % failed if failed else ""), flush=True)
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
