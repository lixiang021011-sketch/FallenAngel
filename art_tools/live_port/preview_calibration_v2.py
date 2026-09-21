#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校准（判定偏移）v2 对照稿 —— **抄开源实现**后的形态（过稿用，不进游戏）。

作者口径（2026-09-22）："成熟的开发者应该先去 GitHub 找开源项目，先学习他们的写法，
分析完了，实在没得抄再自己写。"

分析全文见 art_tools/review/calibration_screen/opensource_reference.md。本稿 = 合成结果：

  · 界面语言（下落音符 / 判定线 / 命中粒子）                  ← Phira  phira/src/page/offset.rs
  · 采样流程：两轮 × 4 次共 8 次、离群 >80ms 整轮作废、进度 n/8  ← Cytoid GlobalCalibrator.cs（Unity C#）
  · 建议值口径 + 三段提示（未测量 / 已准确 / 建议 X）           ← osu!  AudioOffsetAdjustControl.cs
  · 文案与确认弹窗（标题/入口/跟声点击/± 说明/自动调整确认）      ← 上游 PJSK，master_wording*.txt 六条
  · 数值写回 TimingAdjustData（帧，±20，正 = 音符提前）         ← 上游 LiveSettingData / LiveConfig

许可：osu! 是 MIT（可抄代码）；Phira / Cytoid 是 GPL-3.0（**不搬代码，只照设计自己写**）；
Quaver 是 MPL-2.0。所以本稿只取"设计与参数"，实现全部自己写。

五态：
  A 入口        标题 + 上游入口说明 + 底部大按钮「点击调整」+ 当前偏移
  B 测量中      上游引导语 + 下落区（音符/判定线/命中粒子）+ Phira 式"敲在哪"残影 + 进度 3/8
  C 离群重来    Cytoid 的判废口径（偏差 > 80 ms → 这一轮作废）
  D 自动调整结果 上游确认弹窗「要使用自动调整结果进行设置吗？」+ osu! 式建议提示
  E 微调        ± 按钮 + 上游方向说明（上方→＋／下方→－）+ osu! 式"已准确"提示

输出：art_tools/review/calibration_screen/calibration_v2_*.png + calibration_v2_compare.png
运行：python -X utf8 art_tools/live_port/preview_calibration_v2.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
BG = (8, 16, 22)
PANEL = (14, 26, 32)
CARD = (18, 34, 42)
LINE = (207, 239, 234)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
DIM = (74, 104, 110)
BTN = (30, 52, 60)
BTN_KEY = (46, 92, 100)
WARN = (232, 150, 108)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "calibration_screen"))


def font(size, bold=False):
    names = (["msyhbd.ttc", "YuGothB.ttc", "meiryob.ttc", "msgothic.ttc", "simhei.ttf"] if bold else []) + \
            ["msyh.ttc", "YuGothM.ttc", "meiryo.ttc", "msgothic.ttc", "simhei.ttf", "arial.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def text_w(draw, text, f):
    return draw.textlength(text, font=f)


def mid(draw, text, x, y, size, color, bold=True):
    """以 (x, y) 为中心画一行（anchor=mm，避免手算 ascent）。"""
    draw.text((x, y), text, font=font(size, bold), fill=color, anchor="mm")


def center(draw, text, y, size, color, bold=True):
    mid(draw, text, W / 2, y, size, color, bold)


def lines_center(draw, lines, y, size, color, bold=False, gap=1.45):
    step = int(size * gap)
    for i, ln in enumerate(lines):
        center(draw, ln, y + i * step, size, color, bold)


def button(draw, y, text, w=760, h=140, key=True, size=48, note=None, x=None):
    cx = W / 2 if x is None else x
    draw.rounded_rectangle([cx - w / 2, y, cx + w / 2, y + h], radius=h / 2, fill=BTN_KEY if key else BTN)
    mid(draw, text, cx, y + h / 2 - (16 if note else 0), size, TITLE, True)
    if note:
        mid(draw, note, cx, y + h - 30, 20, DIM, False)


def title_bar(draw):
    center(draw, "时机调整", 96, 60, TITLE)
    draw.text((40, 138), "WORD_TIMING_ADJUSTMENT（タイミング調整 / 时机调整）", font=font(24), fill=DIM)


def lane(draw, note_progress=None, pulse=False, trail_y=None, lane_top=600, lane_bottom=1280):
    """下落区：竖轨 + 判定线 + 可选音符/脉冲/「你敲在哪」残影。"""
    cx = W / 2
    draw.rounded_rectangle([cx - 150, lane_top, cx + 150, lane_bottom], radius=24, fill=(11, 22, 28))
    for dx in (-148, 146):
        draw.rectangle([cx + dx, lane_top + 6, cx + dx + 4, lane_bottom - 6], fill=(22, 40, 46))
    draw.rectangle([cx - 260, lane_bottom - 4, cx + 260, lane_bottom + 4], fill=LINE)
    mid(draw, "判定线", cx, lane_bottom + 30, 22, DIM, False)
    if pulse:
        r = 200
        draw.ellipse([cx - r, lane_bottom - r, cx + r, lane_bottom + r], outline=(46, 92, 100), width=6)
    if note_progress is not None:
        y = lane_bottom - (lane_bottom - lane_top) * (1.0 - note_progress)
        s = 0.55 + 0.45 * note_progress
        hw, hh = int(120 * s), int(26 * s)
        draw.rounded_rectangle([cx - hw, y - hh, cx + hw, y + hh], radius=hh,
                               fill=ACCENT if note_progress > 0.9 else (86, 178, 172))
    if trail_y is not None:
        # Phira 的做法：把"你敲在哪"画成一条会淡出的白线（这里示一条打晚的残影）
        draw.rectangle([cx - 300, trail_y - 3, cx + 300, trail_y + 3], fill=(1, 1, 1))
        draw.rectangle([cx - 300, trail_y - 3, cx + 300, trail_y + 3], fill=(140, 160, 165))


def progress(draw, current, total, y=1372):
    """Cytoid 式进度：n / 8 + 细进度条。"""
    center(draw, "%d / %d" % (current, total), y, 34, ACCENT, True)
    x0, x1 = 240, W - 240
    draw.rounded_rectangle([x0, y + 34, x1, y + 46], radius=6, fill=(24, 44, 52))
    if current:
        w = (x1 - x0) * current / float(total)
        draw.rounded_rectangle([x0, y + 34, x0 + w, y + 46], radius=6, fill=ACCENT)


def state_entry(img, draw):
    title_bar(draw)
    lines_center(draw, ["可以调整判定时机。", "请配合高音响起的时机", "点击下方按钮。"], 300, 34, TEXT)
    lane(draw)
    button(draw, 1470, "点击调整", note="MSG_ADJUST_WITH_TAP")
    center(draw, "当前偏移  +0.0 帧（+0 ms）", 1672, 30, ACCENT)


def state_measuring(img, draw):
    title_bar(draw)
    center(draw, "请配合声音点击，调整时机。", 240, 34, TITLE)
    draw.text((40, 286), "WORD_TIMING_SETTING_SOUND（上游文案逐字）", font=font(22), fill=DIM)
    lane(draw, note_progress=0.68, pulse=True, trail_y=1330)
    center(draw, "白线 = 你这一下敲在哪（Phira 的残影做法）", 1452, 22, DIM, False)
    progress(draw, 3, 8)
    button(draw, 1560, "点击调整", note="MSG_ADJUST_WITH_TAP（“下のボタン”= 敲击目标）")
    center(draw, "已记录 3 次点击", 1760, 28, ACCENT)


def state_outlier(img, draw):
    title_bar(draw)
    draw.rounded_rectangle([80, 220, W - 80, 400], radius=24, fill=(34, 24, 20))
    draw.rounded_rectangle([80, 220, W - 80, 400], radius=24, outline=WARN, width=3)
    center(draw, "这一次偏差太大，本轮重来", 270, 32, WARN)
    center(draw, "和本轮平均差了 112 ms（阈值 80 ms）", 320, 26, TEXT, False)
    center(draw, "Cytoid GlobalCalibrator 的判废口径", 362, 22, DIM, False)
    lane(draw, note_progress=0.35, lane_top=470, lane_bottom=1150)
    progress(draw, 0, 8, 1242)
    button(draw, 1330, "点击调整", note="重新开始这一轮")
    center(draw, "已记录 0 次点击（上一轮 2 次作废）", 1530, 28, TEXT)
    draw.text((40, 1600), "离群剔除：单个样本与已收集样本均值相差 > 80 ms → 整轮作废（Cytoid 口径）",
              font=font(22), fill=DIM)


def state_confirm(img, draw):
    # 背后 = 微调页
    title_bar(draw)
    lines_center(draw, ["若在判定线上方请调整为“＋”，", "下方则调整为“－”。"], 250, 34, TEXT)
    lane(draw, lane_top=600, lane_bottom=1240)
    draw.rounded_rectangle([W / 2 - 500, 1400, W / 2 - 240, 1520], radius=60, fill=BTN)
    mid(draw, "－", W / 2 - 370, 1450, 44, TITLE)
    draw.rounded_rectangle([W / 2 + 240, 1400, W / 2 + 500, 1520], radius=60, fill=BTN)
    mid(draw, "＋", W / 2 + 370, 1450, 44, TITLE)
    center(draw, "上方 → ＋          下方 → －", 1560, 26, DIM, False)

    overlay = Image.new("RGBA", (W, H), (4, 9, 12, 200))
    od = ImageDraw.Draw(overlay)
    cw, ch = 920, 660
    x0, y0 = W / 2 - cw / 2, H / 2 - ch / 2
    od.rounded_rectangle([x0, y0, x0 + cw, y0 + ch], radius=32, fill=CARD + (255,))
    od.text((W / 2, y0 + 110), "要使用自动调整结果进行设置吗？", font=font(38, True), fill=TITLE, anchor="mm")
    od.text((W / 2, y0 + 172), "MSG_ADJUST_WITH_TAP_RESULT_BODY（上游文案逐字）",
            font=font(22), fill=DIM, anchor="mm")
    od.text((W / 2, y0 + 290), "自动调整结果   −21 ms（−1.3 帧）", font=font(34, True), fill=ACCENT, anchor="mm")
    od.text((W / 2, y0 + 356), "8 次点击 · 离群已剔除（osu! 式建议提示）",
            font=font(24), fill=TEXT, anchor="mm")
    dy = y0 + ch - 190
    od.rounded_rectangle([x0 + 70, dy, x0 + 400, dy + 120], radius=60, fill=BTN + (255,))
    od.text((x0 + 235, dy + 60), "取消", font=font(40, True), fill=TITLE, anchor="mm")
    od.rounded_rectangle([x0 + cw - 400, dy, x0 + cw - 70, dy + 120], radius=60, fill=BTN_KEY + (255,))
    od.text((x0 + cw - 235, dy + 60), "确定", font=font(40, True), fill=TITLE, anchor="mm")
    return Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")


def state_tuned(img, draw):
    title_bar(draw)
    lines_center(draw, ["若在判定线上方请调整为“＋”，", "下方则调整为“－”。"], 250, 34, TEXT)
    lane(draw, lane_top=600, lane_bottom=1240)
    draw.rounded_rectangle([W / 2 - 500, 1400, W / 2 - 240, 1520], radius=60, fill=BTN)
    mid(draw, "－", W / 2 - 370, 1450, 44, TITLE)
    draw.rounded_rectangle([W / 2 + 240, 1400, W / 2 + 500, 1520], radius=60, fill=BTN)
    mid(draw, "＋", W / 2 + 370, 1450, 44, TITLE)
    center(draw, "上方 → ＋          下方 → －", 1560, 26, DIM, False)
    center(draw, "当前偏移  −21 ms（−1.3 帧）", 1660, 32, ACCENT)
    center(draw, "当前偏移已经和你的手感一致（8 次点击）", 1720, 26, TEXT, False)
    center(draw, "再测一次", 1820, 30, TITLE)


def check_no_overflow():
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    checks = [
        ("入口说明", "可以调整判定时机。", 34, False, W - 160),
        ("测量引导语", "请配合声音点击，调整时机。", 34, True, W - 160),
        ("离群标题", "这一次偏差太大，本轮重来", 32, True, W - 200),
        ("离群副行", "和本轮平均差了 112 ms（阈值 80 ms）", 26, False, W - 200),
        ("弹窗标题", "要使用自动调整结果进行设置吗？", 38, True, 920 - 80),
        ("弹窗结果", "自动调整结果   −21 ms（−1.3 帧）", 34, True, 920 - 120),
        ("弹窗副行", "8 次点击 · 离群已剔除（osu! 式建议提示）", 24, False, 920 - 80),
        ("微调说明", "若在判定线上方请调整为“＋”，", 34, False, W - 160),
        ("残影说明", "白线 = 你这一下敲在哪（Phira 的残影做法）", 22, False, W - 160),
        ("已准确提示", "当前偏移已经和你的手感一致（8 次点击）", 26, False, W - 160),
        ("离群脚注", "离群剔除：单个样本与已收集样本均值相差 > 80 ms → 整轮作废（Cytoid 口径）", 22, False, W - 80),
        ("按钮注", "MSG_ADJUST_WITH_TAP（“下のボタン”= 敲击目标）", 20, False, 800),
    ]
    bad = 0
    for label, text, size, bold, limit in checks:
        w = text_w(probe, text, font(size, bold))
        ok = w <= limit
        bad += 0 if ok else 1
        print("  [%s] %-10s %6.1f / %d" % ("OK " if ok else "!! ", label, w, limit))
    print("v2 文案宽度自检：%d 项，溢出 %d 项" % (len(checks), bad))
    return bad == 0


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    check_no_overflow()

    states = [("entry", state_entry), ("measuring", state_measuring), ("outlier", state_outlier),
              ("confirm", state_confirm), ("tuned", state_tuned)]
    labels = ["A 入口", "B 测量中（3/8）", "C 离群重来", "D 自动调整结果", "E 微调"]
    images = []
    for name, fn in states:
        img = Image.new("RGB", (W, H), BG)
        out = fn(img, ImageDraw.Draw(img))
        img = out if isinstance(out, Image.Image) else img
        img.save(os.path.join(OUT_DIR, "calibration_v2_%s.png" % name))
        images.append(img)

    scale = 0.44
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 5 + 140, ph + 250), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((40, 26), "FallenAngel · 校准界面 v2（抄开源实现后的形态 · 待过稿）", font=font(40, True), fill=TITLE)
    for i, ln in enumerate([
        "界面语言抄 Phira（游戏本体的下落音符 / 判定线 / 命中粒子 / “你敲在哪”残影）",
        "采样流程抄 Cytoid（两轮 ×4 共 8 次、单点偏差 > 80 ms 整轮作废、进度 n/8）· 建议值与三段提示抄 osu!",
        "文案与确认弹窗用上游 PJSK 的六条原文；数值仍写回 TimingAdjustData（帧，±20，正 = 音符提前）",
    ]):
        sd.text((40, 80 + i * 30), ln, font=font(24), fill=TEXT)
    for i, img in enumerate(images):
        x = 30 + i * (pw + 20)
        sd.text((x + 6, 186), labels[i], font=font(24, True), fill=ACCENT)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 216))
        sd.rectangle([x, 216, x + pw, 216 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(OUT_DIR, "calibration_v2_compare.png"))
    print("已输出：", OUT_DIR)


if __name__ == "__main__":
    main()
