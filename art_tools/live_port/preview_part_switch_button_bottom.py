#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""声部切换条 · **底部、数量自适应**方案过稿稿（**不进游戏**）。

作者 2026-10-03 定：按钮放下方（C2），并且提醒"不止两个乐器"。
本稿把两件事一起画清楚：

  1. 芯片数量跟着**曲目的声部表**走（`SongParts.PartNames`）：原型期 2 件→2 个芯片，
     将来一首歌 5 件乐器→5 个芯片，界面代码不用改；
  2. 单个芯片宽度自适应：`min(240, (1040 − 12×(N−1)) / N)`
     —— 2 件时是 240 的宽条，5 件时压到 198 正好铺满一行。

工程代价（与上一稿一致，提前说清）：
  · 判定线（屏幕 y=1520）不动，音符判定与下落不受影响；
  · 底栏高 132，吃掉屏幕最底一条的触摸（判定层用 IsPointerOverUI 挡），所以那段不再是轨道输入区；
  · 键帽（KeyArea）在运行时整体上移 132，让"看得见的轨道"停在底栏之上；
  · 防误触两层：① 只有"按下→抬起都在同一枚芯片内、且按下 ≤0.4s"才算一次点击（拇指歇着抬起不算）；
    ② 点击本身还要**双击确认**（0.5s 内连点同一枚）——第一下只点亮成"待确认"，第二下才换声部。

输出（art_tools/review/part_switch/）：
  part_switch_button_bottom.png  2 件 / 5 件两条整屏对照 + 底栏放大 + 切换条状态

运行：python -X utf8 art_tools/live_port/preview_part_switch_button_bottom.py
"""
import colorsys
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
LANE_WIDTH = 234.0
LANE_CENTERS = [-390.0, -130.0, 130.0, 390.0]
NOTE_W, NOTE_H = 217.0, 28.0
JUDGE_SCREEN_Y = 1520
SPAWN_SCREEN_Y = 360

BAR_H, CHIP_H, GAP, MAX_CHIP, BOTTOM_MARGIN, ROW_W = 132, 108, 12, 240, 16, 1040
BAR_TOP = H - BAR_H

BG_DEEP = (8, 16, 21)
BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
LANE_EDGE = (34, 58, 66)
JUDGE = (207, 239, 234)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176)]
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
MUTED = (108, 130, 136)
WARN = (232, 126, 108)
ACCENT = (126, 224, 176)

# 五件乐器的色相（与 PartPalette 一致，按色相环排，最小间隔 45°）
PART_HUES = [("Drums", 0.0), ("Guitar", 45.0), ("Keys", 105.0), ("Bass", 215.0), ("Vocal", 295.0)]
DEMO_PARTS = ["Guitar", "Drums"]
FIVE_PARTS = [name for name, _ in PART_HUES]


def font(size, bold=False):
    names = ["C:/Windows/Fonts/msyhbd.ttc", "C:/Windows/Fonts/msyh.ttc"] if bold else \
            ["C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def mix(c0, c1, k):
    k = max(0.0, min(1.0, k))
    return tuple(int(round(c0[i] + (c1[i] - c0[i]) * k)) for i in range(3))


def hue_of(part):
    for name, hue in PART_HUES:
        if name == part:
            return hue
    return 45.0


def hue_rgb(hue, s=0.72, v=0.90):
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(hue / 360.0, s, v))


def chip_width(count):
    return min(MAX_CHIP, (ROW_W - GAP * (count - 1)) / float(count))


def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def draw_background(img, color):
    d = ImageDraw.Draw(img)
    for y in range(H):
        base = mix(BG_DEEP, BG, y / float(H))
        k = max(0.0, 1.0 - y / 1050.0) ** 1.2
        d.line([(0, y), (W, y)], fill=mix(base, color, 0.34 * k))


def draw_hud(img):
    d = ImageDraw.Draw(img)
    rounded(d, [20, 20, 1060, 40], 8, fill=(20, 40, 48))
    for label, cx, cy, w in (("曲名", 320, 71, 560), ("分数", 830, 71, 420),
                             ("生命", 250, 127, 420), ("FEVER", 830, 127, 420),
                             ("进度文本", 260, 164, 440), ("蓄力条", 830, 164, 420)):
        h = 12 if label == "蓄力条" else 34
        rounded(d, [cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], 6, fill=(24, 44, 52))


def draw_field(img, color):
    d = ImageDraw.Draw(img)
    edge = mix(LANE_EDGE, color, 0.75)
    lane_bottom = BAR_TOP - 10
    for lane, cx0 in enumerate(LANE_CENTERS):
        x = W / 2.0 + cx0
        d.rectangle([x - LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160, x + LANE_WIDTH / 2, lane_bottom],
                    fill=mix(LANE_FILL, color, 0.10))
        d.line([(x - LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160), (x - LANE_WIDTH / 2, lane_bottom)], fill=edge, width=2)
        d.line([(x + LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160), (x + LANE_WIDTH / 2, lane_bottom)], fill=edge, width=2)
        rounded(d, [x - LANE_WIDTH / 2 + 10, lane_bottom - 62, x + LANE_WIDTH / 2 - 10, lane_bottom], 10,
                fill=mix(color, BG, 0.55), outline=mix(LANE_COLORS[lane], color, 0.25), width=2)
        ny = SPAWN_SCREEN_Y + 260 + lane * 130
        rounded(d, [x - NOTE_W / 2, ny, x + NOTE_W / 2, ny + NOTE_H], 6,
                fill=mix(LANE_COLORS[lane], BG, 0.12), outline=LANE_COLORS[lane], width=2)
    d.line([(0, JUDGE_SCREEN_Y), (W, JUDGE_SCREEN_Y)], fill=JUDGE, width=4)


def draw_switch_bar(img, parts, active, pressed=None, armed=None):
    """与 PartSwitchBar 同一套算法：芯片居中、宽度 min(240, (1040−12(N−1))/N)。"""
    d = ImageDraw.Draw(img, "RGBA")
    d.rectangle([0, BAR_TOP, W, H], fill=(9, 18, 23, 240))
    cw = chip_width(len(parts))
    total = cw * len(parts) + GAP * (len(parts) - 1)
    start = (W - total) / 2.0
    for i, part in enumerate(parts):
        x0 = start + i * (cw + GAP)
        y0 = H - BOTTOM_MARGIN - CHIP_H
        color = hue_rgb(hue_of(part))
        is_active = part == active
        fill = (int(color[0] * 0.85), int(color[1] * 0.85), int(color[2] * 0.85), 255) if is_active \
            else (int(color[0] * 0.22), int(color[1] * 0.22), int(color[2] * 0.22), 255)
        if part == pressed:
            fill = (min(255, fill[0] + 60), min(255, fill[1] + 60), min(255, fill[2] + 60), 255)
        if part == armed:
            fill = (235, 245, 248, 255)          # 待确认：提亮成白
        rounded(d, [x0, y0, x0 + cw, y0 + CHIP_H], 14, fill=fill,
                outline=color + (255,), width=3 if is_active else 2)
        f = font(34, True)
        tw = d.textlength(part, font=f)
        text_color = (24, 28, 30, 255) if (is_active or part == armed) else color + (235,)
        d.text((x0 + cw / 2 - tw / 2, y0 + CHIP_H / 2 - 22), part, font=f, fill=text_color)
        if is_active:
            rounded(d, [x0 + 16, y0 + 8, x0 + cw - 16, y0 + 14], 3, fill=color + (255,))


def mock(parts, active, pressed=None, armed=None, badge=False):
    img = Image.new("RGB", (W, H), BG_DEEP)
    color = hue_rgb(hue_of(active))
    draw_background(img, color)
    draw_hud(img)
    draw_field(img, color)
    draw_switch_bar(img, parts, active, pressed, armed)
    if badge:
        d = ImageDraw.Draw(img)
        f = font(110, True)
        tw = d.textlength("DRUMS", font=f)
        d.text(((W - tw) / 2, 700), "DRUMS", font=f, fill=TEXT_BRIGHT, stroke_width=3,
               stroke_fill=hue_rgb(0.0))
    return img


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "review", "part_switch")
    os.makedirs(out_dir, exist_ok=True)

    scale = 0.42
    fw, fh = int(W * scale), int(H * scale)
    bar_h = int((BAR_H + 80) * scale)
    sw, sh = int(W * 0.34), int(H * 0.34)
    width = max(40 + 2 * fw + 18 + 40, 40 + 4 * sw + 3 * 16 + 40)
    height = 200 + fh + 26 + bar_h + 120 + 54 + sh + 110

    sheet = Image.new("RGB", (width, height), (7, 13, 17))
    d = ImageDraw.Draw(sheet)
    d.text((40, 34), "声部切换条 · 底部、数量自适应（作者定：放下方；提醒：不止两个乐器）",
           font=font(36, True), fill=TEXT_BRIGHT)
    d.text((42, 88), "芯片数 = 曲目声部表长度；单个宽度 = min(240, (1040 − 12×(N−1)) / N)：2 件是 240 宽条，5 件压到 198 正好一行",
           font=font(24), fill=TEXT)
    d.text((42, 124), "判定线不动；底栏吃掉最底一条的触摸（uGUI 挡住判定层），键帽整体上移 132px；防误触＝快速轻点 + 双击确认（第一下只点亮待确认）",
           font=font(24), fill=TEXT)

    y_shot, y_bar = 200, 200 + fh + 26
    for i, (parts, active, title, note) in enumerate((
            (DEMO_PARTS, "Guitar", "原型期：2 件乐器", "芯片 240 宽，居中；点另一枚即切换"),
            (FIVE_PARTS, "Keys", "将来：5 件乐器", "芯片压到 198、铺满一行（间距 12）"))):
        img = mock(parts, active)
        x = 40 + i * (fw + 18)
        sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y_shot))
        d.rectangle([x, y_shot, x + fw - 1, y_shot + fh - 1], outline=(40, 62, 70), width=1)
        band = img.crop((0, BAR_TOP - 80, W, H)).resize((fw, bar_h), Image.LANCZOS)
        sheet.paste(band, (x, y_bar))
        d.rectangle([x, y_bar, x + fw - 1, y_bar + bar_h - 1], outline=(40, 62, 70), width=1)
        d.text((x + 6, y_bar + bar_h + 8), title, font=font(28, True), fill=TEXT_BRIGHT)
        d.text((x + 6, y_bar + bar_h + 42), note, font=font(22), fill=TEXT)

    y_state = y_bar + bar_h + 120
    d.text((40, y_state), "切换条状态：当前声部实底高亮 / 第一下点亮“待确认” / 第二下确认切换（徽记仍在判定线以上）/ 5 件乐器的排布",
           font=font(32, True), fill=TEXT_BRIGHT)
    y_state += 54
    states = [("当前 Guitar", mock(DEMO_PARTS, "Guitar")),
              ("第一下：DRUMS 待确认", mock(DEMO_PARTS, "Guitar", armed="Drums")),
              ("第二下：确认切换", mock(DEMO_PARTS, "Drums", badge=True)),
              ("5 件乐器 · 当前 Keys", mock(FIVE_PARTS, "Keys"))]
    for i, (label, img) in enumerate(states):
        x = 40 + i * (sw + 16)
        sheet.paste(img.resize((sw, sh), Image.LANCZOS), (x, y_state))
        d.rectangle([x, y_state, x + sw - 1, y_state + sh - 1], outline=(40, 62, 70), width=1)
        d.text((x + 6, y_state + sh + 8), label, font=font(24), fill=TEXT)

    d.text((40, height - 46), "已按 C2 落地：运行时建层（不用重建场景），芯片数与宽度跟声部表走；音效按作者意见不做。",
           font=font(28, True), fill=ACCENT)
    path = os.path.join(out_dir, "part_switch_button_bottom.png")
    sheet.save(path)
    print("saved %s  %dx%d" % (path, sheet.size[0], sheet.size[1]))


if __name__ == "__main__":
    main()
