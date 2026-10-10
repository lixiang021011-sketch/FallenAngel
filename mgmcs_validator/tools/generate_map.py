"""Write a song's coordinate map: `python tools/generate_map.py song.mgmcs.json`.

What lands in the file is the *parameters* - one line per grid - plus, if asked,
the anchor lines for one window. Deliberately not a table of every millisecond:
the mapping is a line, and a file that spells it out per millisecond is the same
three numbers written a hundred thousand times. Anything that needs coordinates
asks coordinate_map.py.

    python tools/generate_map.py song.mgmcs.json
    python tools/generate_map.py song.mgmcs.json --out song.coordinate_map.json
    python tools/generate_map.py song.mgmcs.json --anchors 0 20 --grid grid_A
    python tools/generate_map.py song.mgmcs.json --grids          # 只列有哪些网格

Read-only: it never writes to the input document.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

from coordinate_map import CoordinateMap, coordinate_map_document, load_song


def default_out(path: pathlib.Path) -> pathlib.Path:
    """`song.mgmcs.json` → `song.coordinate_map.json`."""
    name = path.name
    for suffix in (".mgmcs.json", ".json"):
        if name.endswith(suffix):
            return path.with_name(name[: -len(suffix)] + ".coordinate_map.json")
    return path.with_name(name + ".coordinate_map.json")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("document", help="歌曲的 mgmcs.json（或任何带 grids 的 JSON）")
    parser.add_argument("--out", help="写到哪（默认 <名字>.coordinate_map.json）")
    parser.add_argument("--anchors", nargs=2, type=float, metavar=("FROM", "TO"),
                        help="顺便把某个窗口里的小节线/拍线写出来")
    parser.add_argument("--grid", dest="grid_id", help="--anchors 用哪条网格（默认第一条）")
    parser.add_argument("--grids", action="store_true", help="只列出这份文件里有哪些网格")
    parser.add_argument("--print", dest="to_stdout", action="store_true", help="打到屏幕，不写文件")
    args = parser.parse_args(argv)

    path = pathlib.Path(args.document)
    if not path.exists():
        raise SystemExit("找不到文件：%s" % path)

    raw = json.loads(path.read_text(encoding="utf-8"))
    song = load_song(raw)
    cmap = CoordinateMap(song)

    if args.grids:
        for grid in song.grids:
            print("%-16s %8.3f BPM  %d/%d  起点 %8.3f s  %s"
                  % (grid.grid_id, grid.bpm, grid.beats_per_bar, grid.beat_unit,
                     grid.origin_time, grid.source))
        for problem in song.problems:
            print("  注意：%s" % problem, file=sys.stderr)
        return 0

    anchors = None
    if args.anchors:
        grid_id = args.grid_id or (song.grids[0].grid_id if song.grids else "")
        if not grid_id:
            raise SystemExit("这份文件里没有网格")
        anchors = {grid_id: cmap.generate_anchors(grid_id, args.anchors[0], args.anchors[1])}
        if isinstance(anchors[grid_id], dict) and "error" in anchors[grid_id]:
            raise SystemExit(anchors[grid_id]["error"])

    out = coordinate_map_document(song, anchors)
    text = json.dumps(out, ensure_ascii=False, indent=1)
    if args.to_stdout:
        print(text)
        return 0
    target = pathlib.Path(args.out) if args.out else default_out(path)
    target.write_text(text, encoding="utf-8")
    print("写出：%s（%d 条网格%s）" % (target, len(song.grids),
                                   "，含 anchors" if anchors else ""))
    for problem in song.problems:
        print("  注意：%s" % problem, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
