"""给 MGMCS 验证器生成一个可点的歌单：每首歌一行，点了直接带音频打开。

    python build_index.py

为什么要有它：验证器的入口是

    http://localhost:8123/index.html?doc=<短名>&audio=<音频网址>

而曲库里的文件名是日文加空格（`ヨルシカ - ヒッチコック (希区柯克).ogg`），手拼 URL 不现实。
这个脚本读 `static/docs/*.mgmcs.json`，用文档里记的 `song.audioFileName` 拼出音频网址，
写一个能直接双击打开的 HTML（链接是绝对的，所以文件放在哪都行）。

只读、可重复跑：文档多一首，重跑一次就多一行。
"""

from __future__ import annotations

import json
import urllib.parse
from pathlib import Path

VALIDATOR = Path(r"C:\Users\LorXer\Documents\trae_projects\FallenAngel\mgmcs_validator")
DOCS = VALIDATOR / "static" / "docs"
MUSIC = "http://127.0.0.1:8125"
PAGE = "http://localhost:8123/index.html"
# 直接写进验证器的静态目录：这样它是一个**能点开的网址**（http://localhost:8123/songs.html），
# 而不是一个要自己去 F 盘翻的本地文件。
OUT = VALIDATOR / "static" / "songs.html"


def main() -> int:
    rows, missing = [], []
    for path in sorted(DOCS.glob("*.mgmcs.json")):
        short = path.name[: -len(".mgmcs.json")]
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as failure:
            missing.append("%s（读不了：%s）" % (short, failure))
            continue
        song = doc.get("song") or {}
        audio = song.get("audioFileName") or ""
        grids = doc.get("calibrations") or []
        kinds = "、".join(sorted({str(g.get("calibrationId")) for g in grids})) or "（没有网格）"
        human = [g for g in grids if g.get("source") == "human"]
        # 用 `music=` 基址形式，不用 `&audio=<整条网址>`：
        # 验证器作者的注释里写着，长 URL 复制粘贴时会被悄悄截断（他真踩过，
        # 页面最后去 404 一个叫 ".../Cö" 的文件）。短形式是纯 ASCII，页面自己
        # 用文档里的 audioFileName 拼音频地址，短得不可能被吃掉。
        url = PAGE + "?doc=" + urllib.parse.quote(short)
        if audio:
            url += "&music=" + urllib.parse.quote(MUSIC, safe=":/")
        rows.append({
            "short": short, "url": url,
            "title": song.get("title") or short,
            "duration": song.get("durationSeconds"),
            "grids": len(grids), "kinds": kinds,
            "human": len(human),
        })

    body = "\n".join(
        '<tr><td><a href="{url}">{short}</a></td><td>{title}</td>'
        '<td>{minutes}</td><td class="{hclass}">{human}</td>'
        '<td>{grids}（{kinds}）</td></tr>'.format(
            url=row["url"], short=row["short"], title=row["title"],
            minutes="—" if not row["duration"] else "%.1f 分" % (row["duration"] / 60.0),
            human=row["human"] if row["human"] else "—",
            hclass="yes" if row["human"] else "no",
            grids=row["grids"], kinds=row["kinds"])
        for row in rows)

    page = """<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>MGMCS 验证器 · 曲库歌单</title>
<style>
 body { background:#101319; color:#e9edf5; font:14px/1.6 system-ui, sans-serif; margin:24px; }
 h1 { font-size:18px; } .note { color:#98a2b3; font-size:12px; }
 table { border-collapse:collapse; width:100%; }
 th,td { text-align:left; padding:6px 10px; border-bottom:1px solid #2c3340; }
 th { color:#98a2b3; font-weight:600; }
 a { color:#6ea8fe; text-decoration:none; } a:hover { text-decoration:underline; }
 .yes { color:#79d18a; } .no { color:#5b6472; }
</style>
</head>
<body>
<h1>MGMCS 验证器 · 曲库歌单（__COUNT__ 首）</h1>
<p class="note">点歌名直接在验证器里打开（带音频网址，能拖能听）。<br>
先看 <a href="timing.html">timing 体检</a>——那里写了哪首歌哪里有 timing 风险、该听哪一段。<br>
前提：验证器在 8123、音频服务在 8125 上跑着——两支队分别起在 mgmcs_validator 目录里：<br>
<code>python tools\\serve_range.py --directory static --port 8123</code><br>
<code>python tools\\serve_range.py --directory "F:\\vibe transcription\\_listen_root" --port 8125</code><br>
缺的：__MISSING__</p>
<table><tr><th>短名</th><th>曲目</th><th>时长</th><th>人工遍数</th><th>网格</th></tr>
__BODY__</table>
</body>
</html>
"""
    # 不用 % 格式化：这段 CSS 里就有 %，会和占位符打架
    page = (page.replace("__COUNT__", str(len(rows)))
                .replace("__MISSING__", "、".join(missing) if missing else "无")
                .replace("__BODY__", body))
    OUT.write_text(page, encoding="utf-8")
    print("→ %s（%d 首）" % (OUT, len(rows)))
    if missing:
        print("读不了：%s" % "、".join(missing))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
