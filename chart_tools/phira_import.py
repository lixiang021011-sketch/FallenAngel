# -*- coding: utf-8 -*-
"""Phira 社区谱 → 本工程 v2 谱面（**测试素材用**）。

作者口径（2026-09-22）："不考虑发行的问题，现在抓来的曲子和谱都是测试用的，
最后打包用我们自己的歌和谱。"

所以这里只解决"拿得到、转得动"：

  · 数据源：Phira 谱面站公开 API（`phira/src/client.rs:26` 的 `API_URL = https://phira.5wyxi.com`，
    列表 `GET /chart?page=N&size=M`，详情 `GET /chart/{id}`）。
    谱面对象模型见 `phira/src/client/model/chart.rs`：id/name/level/difficulty/charter/composer/
    illustrator/file/preview/illustration/uploader/tags/…（**没有 license 字段** → 默认保留所有权利，
    只当本地测试素材，不进发行包）。
  · 谱面包：`file` 是个 zip，内含谱面（RPE 的 `.json` / Phigros `.pec` / Phira `.pbc`）、
    音频（`.ogg`/`.mp3`）、封面 `.png` 与 `info.txt`。
  · 转谱：`.pec` 直接复用 `chart_import.parse_pec`；RPE 的 `.json` 由本文件的 `parse_rpe` 解析
    （`BPMList` + `judgeLineList[].notes[]`，时间 = [小节,分子,分母] → 拍 → 秒）；
    `.pbc` 暂不支持（会明确跳过）。
  · 轨数：PositionX ∈ [-675, 675]（Phigros/RPE 标准宽度）线性映射到我们 5 轨。
  · 产物：`Assets/Resources/Charts/phira_<id>.json` + `Assets/Resources/Audio/phira_<id>.<ext>`，
    曲名带来源标注：`曲名【Phira #id · 谱师 X】`。

用法：
  python -X utf8 chart_tools/phira_import.py --id 78559 --id 77695
  python -X utf8 chart_tools/phira_import.py --top 5            # 谱面站第一页前 5 首
  python -X utf8 chart_tools/phira_import.py --top 5 --name-filter "click"
"""
import argparse
import io
import json
import os
import re
import sys
import urllib.request
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chart_import  # noqa: E402  （复用 empty_chart / finish / parse_pec）

API = "https://phira.5wyxi.com"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHART_DIR = os.path.join(ROOT, "Assets", "Resources", "Charts")
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")

PHIGROS_HALF_WIDTH = 675.0        # Phigros / RPE 的 PositionX 标准半宽
TYPE_MAP = {1: "tap", 2: "drag", 3: "hold", 4: "flick"}


def http_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": "FallenAngel-chart-tools"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode("utf-8"))


def http_bytes(url):
    req = urllib.request.Request(url, headers={"User-Agent": "FallenAngel-chart-tools"})
    with urllib.request.urlopen(req, timeout=180) as resp:
        return resp.read()


def beat_of(pos):
    """RPE 的 [小节, 分子, 分母] → 拍数。"""
    bar, num, den = float(pos[0]), float(pos[1]), float(pos[2])
    return bar + (num / den if den else 0.0)


def build_time_map(bpm_list):
    """BPMList → [(起拍, 秒/拍, 起始秒)]，用于把拍换算成秒。"""
    entries = sorted(bpm_list, key=lambda b: beat_of(b["startTime"]))
    out = []
    t = 0.0
    prev_beat = 0.0
    for e in entries:
        beat = beat_of(e["startTime"])
        if out:
            t += (beat - prev_beat) * (60.0 / out[-1][1])
        out.append((beat, float(e["bpm"]), t))
        prev_beat = beat
    if not out:
        out.append((0.0, 120.0, 0.0))
    return out


def beats_to_seconds(beats, time_map):
    cur = time_map[0]
    for entry in time_map:
        if beats >= entry[0]:
            cur = entry
        else:
            break
    return cur[2] + (beats - cur[0]) * (60.0 / cur[1])


def lane_of(position_x):
    """PositionX → 0..4（超出标准宽度就夹到边缘轨）。"""
    t = (position_x + PHIGROS_HALF_WIDTH) / (2.0 * PHIGROS_HALF_WIDTH)
    return max(0, min(chart_import.LANES - 1, int(round(t * (chart_import.LANES - 1)))))


def parse_rpe(text, offset_seconds):
    """RPE（Phigros RPE 编辑器）JSON → 本工程 chart dict。"""
    data = json.loads(text)
    time_map = build_time_map(data.get("BPMList") or [])
    chart = chart_import.empty_chart()
    notes = []
    for line in (data.get("judgeLineList") or []):
        for note in (line.get("notes") or []):
            if int(note.get("isFake", 0)) != 0:
                continue                                  # 装饰音符
            kind = TYPE_MAP.get(int(note.get("type", 0)))
            if kind is None:
                continue
            start = beats_to_seconds(beat_of(note["startTime"]), time_map)
            end_pos = note.get("endTime") or note["startTime"]
            end = beats_to_seconds(beat_of(end_pos), time_map)
            if start < -0.2:
                continue
            notes.append({
                "type": kind,
                "lane": lane_of(float(note.get("positionX", 0.0))),
                "time": round(start, 3),
                "duration": round(max(0.0, end - start), 3) if kind == "hold" else 0.0,
                "direction": "up",
                "wide": False,
                "path": [],
            })
    chart["notes"] = notes
    chart["metadata"]["offset"] = round(offset_seconds, 3)
    chart["metadata"]["bpm"] = round(time_map[0][1], 3)
    return chart_import.finish(chart)


def pick_members(names):
    """从 zip 成员里挑谱面 / 音频。"""
    chart_file = None
    for n in names:
        if n.lower().endswith((".pec", ".json", ".pbc")) and not n.startswith("info"):
            chart_file = n
            break
    audio_file = None
    for n in names:
        if n.lower().endswith((".ogg", ".mp3", ".wav")):
            audio_file = n
            break
    return chart_file, audio_file


def chart_level(difficulty):
    """谱面站 difficulty（14.3 之类）→ 我们 1-20 的等级数字。"""
    try:
        return max(1, min(20, int(round(float(difficulty)))))
    except (TypeError, ValueError):
        return 10


def import_chart(meta, dry_run=False):
    cid = meta["id"]
    name = meta.get("name") or ("chart_%s" % cid)
    charter = meta.get("charter") or "?"
    composer = meta.get("composer") or ""
    print("[phira] #%s %s（谱师 %s / 作曲 %s）" % (cid, name, charter, composer))

    file_url = meta["file"]["url"] if isinstance(meta["file"], dict) else meta["file"]
    zf = zipfile.ZipFile(io.BytesIO(http_bytes(file_url)))
    members = [i.filename for i in zf.infolist()]
    chart_file, audio_file = pick_members(members)
    if chart_file is None or audio_file is None:
        print("   跳过：包里没有可识别的谱面/音频（%s）" % ", ".join(members[:6]))
        return None

    text = zf.read(chart_file).decode("utf-8", "replace")
    ext = os.path.splitext(chart_file)[1].lower()
    if ext == ".pec":
        chart = chart_import.parse_pec(text)
    elif ext == ".json":
        # 注意：**不用 RPE 的 `META.offset`**。实测三首（78559 / 77695 / 78641）用音频起音回查：
        #   78559 offset=0   → 中位 +9.9ms
        #   77695 offset=188 → 按 0 处理时中位 +9.6ms（把 188 当秒/毫秒加进去都不改善，反而更差）
        #   78641 offset=0   → 中位 −22.0ms
        # 即 Phira 谱面的音符时间**已经是相对音频文件的绝对时间**，RPE 那个字段的语义不可靠。
        # 残余的 10~20ms 是各位谱师自己的对齐习惯，交给玩家侧的统计校准吸收（见 Core/HitErrorTracker）。
        chart = parse_rpe(text, 0.0)
    else:
        print("   跳过：%s 格式暂不支持（PBC 是 Phira 自研格式）" % ext)
        return None

    m = chart["metadata"]
    m["songName"] = "%s【Phira #%s · 谱师 %s】" % (name, cid, charter)
    m["songArtist"] = composer
    m["chartAuthor"] = charter
    m["level"] = chart_level(meta.get("difficulty"))
    m["difficulty"] = 3
    audio_name = "phira_%s" % cid
    m["audioFileName"] = audio_name

    counts = {}
    for n in chart["notes"]:
        counts[n["type"]] = counts.get(n["type"], 0) + 1
    m["noteCounts"] = {"tap": counts.get("tap", 0), "hold": counts.get("hold", 0),
                       "flick": counts.get("flick", 0), "slide": counts.get("slide", 0)}

    if not chart["notes"]:
        print("   跳过：转出来 0 个音符")
        return None

    if not dry_run:
        os.makedirs(CHART_DIR, exist_ok=True)
        os.makedirs(AUDIO_DIR, exist_ok=True)
        with open(os.path.join(CHART_DIR, "phira_%s.json" % cid), "w", encoding="utf-8") as f:
            json.dump(chart, f, ensure_ascii=False, indent=2)
        dst = os.path.join(AUDIO_DIR, "%s%s" % (audio_name, os.path.splitext(audio_file)[1]))
        with open(dst, "wb") as f:
            f.write(zf.read(audio_file))

    first = chart["notes"][0]["time"]
    last = chart["notes"][-1]["time"]
    print("   → phira_%s.json  音符 %d（%s）BPM %s  时长 %.1fs  offset %.3f%s"
          % (cid, len(chart["notes"]),
             " ".join("%s%d" % (k, v) for k, v in sorted(counts.items())),
             m["bpm"], last - first, m["offset"], "（dry-run）" if dry_run else ""))
    return chart


def fetch_meta_by_id(cid):
    return http_json("%s/chart/%s" % (API, cid))


def fetch_top(count, page=1, name_filter=None):
    out = []
    size = max(30, count * 3)
    data = http_json("%s/chart?page=%d&size=%d" % (API, page, size))
    for item in data.get("results", []):
        if name_filter and not re.search(name_filter, item.get("name", ""), re.I):
            continue
        out.append(item)
        if len(out) >= count:
            break
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id", action="append", type=int, default=[], help="谱面 id（可重复）")
    ap.add_argument("--top", type=int, default=0, help="从谱面站第一页抓 N 首")
    ap.add_argument("--page", type=int, default=1)
    ap.add_argument("--name-filter", default=None, help="--top 时按曲名正则过滤")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    metas = [fetch_meta_by_id(cid) for cid in args.id]
    if args.top:
        metas.extend(fetch_top(args.top, args.page, args.name_filter))
    if not metas:
        ap.error("至少给一个 --id 或 --top")

    ok = 0
    for meta in metas:
        try:
            if import_chart(meta, args.dry_run) is not None:
                ok += 1
        except Exception as e:                                  # noqa: BLE001
            print("[phira] #%s 失败：%s" % (meta.get("id"), e))
    print("[phira] 完成：%d/%d 成功" % (ok, len(metas)))


if __name__ == "__main__":
    main()
