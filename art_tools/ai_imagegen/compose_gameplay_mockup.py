#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""AI 背景 + 程序化透视轨道 = 演奏界面效果图。

分工：
    AI   —— 只负责海底背景的质感与氛围（它擅长的部分）
    程序 —— 负责轨道、判定线、音符、HUD 的**精确几何**（近大远小由公式保证）

透视：屏幕顶端缩放 top_scale，判定线处 100%，线性插值（与 make_layout.py 同一套参数）。

用法：
    python -X utf8 compose_gameplay_mockup.py --bg <背景图> --out <输出图> --seed 12345
"""
import argparse
import os
import random

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))

W, H = 1080, 1920
LINE_FROM_BOTTOM = 400
Y_JUDGE = H - LINE_FROM_BOTTOM
LANE_CENTERS = [-410, -205, 0, 205, 410]
LANE_HALF = 92
TOP_SCALE = 0.30
LANE_COLORS = [(79, 224, 200), (87, 190, 232), (127, 168, 242), (169, 214, 245), (69, 217, 168)]


def sc(y, top=TOP_SCALE):
    if y >= Y_JUDGE:
        return 1.0
    t = max(0.0, min(1.0, y / Y_JUDGE))
    return top + (1.0 - top) * t


def cx(offset, y, top=TOP_SCALE):
    return W / 2 + offset * sc(y, top)


def font(size, bold=False):
    path = r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc"
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


def center_darken(img, strength=0.55):
    """中央走廊压暗：让亮色音符在上面读得出来。"""
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    xs = (np.arange(W) - W / 2) / (W / 2)
    mask = 1.0 - strength * np.exp(-(xs ** 2) / (2 * 0.42 ** 2))
    a *= mask[None, :, None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def draw_lanes(base):
    """轨道柱体 + 边缘线 + 判定线 + 按键区，全部按透视公式绘制。"""
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    solid = base.convert("RGBA")
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)

    for idx, c in enumerate(LANE_CENTERS):
        col = LANE_COLORS[idx]
        x0t, x1t = cx(c - LANE_HALF, 0), cx(c + LANE_HALF, 0)
        x0b, x1b = cx(c - LANE_HALF, Y_JUDGE), cx(c + LANE_HALF, Y_JUDGE)
        # 柱体
        d.polygon([(x0t, 0), (x1t, 0), (x1b, Y_JUDGE), (x0b, Y_JUDGE)], fill=col + (26,))
        # 边缘辉光
        gd.line([(x0t, 0), (x0b, Y_JUDGE)], fill=tuple(int(v * 0.55) for v in col), width=5)
        gd.line([(x1t, 0), (x1b, Y_JUDGE)], fill=tuple(int(v * 0.55) for v in col), width=5)

    # 判定线
    span = LANE_CENTERS[-1] + LANE_HALF
    gd.line([(cx(-span, Y_JUDGE), Y_JUDGE), (cx(span, Y_JUDGE), Y_JUDGE)],
            fill=(210, 255, 250), width=9)

    # 按键区
    for idx, c in enumerate(LANE_CENTERS):
        col = LANE_COLORS[idx]
        d.rounded_rectangle([W / 2 + c - LANE_HALF, Y_JUDGE + 8, W / 2 + c + LANE_HALF, H - 14],
                            radius=18, fill=col + (34,), outline=col + (150,), width=3)
    gd.line([(cx(-span, Y_JUDGE), Y_JUDGE), (cx(span, Y_JUDGE), Y_JUDGE)],
            fill=(255, 255, 255), width=4)

    glow = glow.filter(ImageFilter.GaussianBlur(18))
    # 辉光用 screen 混合（加亮），不能用 alpha 覆盖，否则背景会被抹掉
    solid = ImageChops.screen(solid.convert("RGB"), glow).convert("RGBA")
    solid = Image.alpha_composite(solid, ov)
    return solid


def draw_note(d, gd, lane, y, kind="tap", hold_px=0):
    col = LANE_COLORS[lane]
    s = sc(y)
    w = int(166 * s)
    h = max(4, int(16 * s))
    x0 = W / 2 + LANE_CENTERS[lane] * s - w / 2
    y0 = y - h / 2
    box = [x0, y0, x0 + w, y0 + h]
    if hold_px > 0:
        d.rounded_rectangle([x0 + 6, y0, x0 + w - 6, y0 + hold_px * s],
                            radius=int(h / 2), fill=col + (110,))
        gd.rounded_rectangle([x0 + 6, y0, x0 + w - 6, y0 + hold_px * s],
                             radius=int(h / 2), fill=tuple(int(v * 0.6) for v in col))
    d.rounded_rectangle(box, radius=int(h / 2), fill=col + (235,))
    d.rounded_rectangle([box[0], box[1], box[2], box[1] + h / 2], radius=int(h / 4),
                        fill=(255, 255, 255, 120))
    gd.rounded_rectangle(box, radius=int(h / 2), fill=tuple(int(v * 0.75) for v in col))


def draw_notes(base, seed=1234):
    rnd = random.Random(seed)
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    gd = ImageDraw.Draw(glow)

    # 同押连线（先画，压在音符下方）
    y_link = 980
    a, b = 0, 4
    s = sc(y_link)
    xa = W / 2 + LANE_CENTERS[a] * s
    xb = W / 2 + LANE_CENTERS[b] * s
    band = int(9 * s)
    d.polygon([(xa, y_link - band), (xb, y_link - band), (xb, y_link + band), (xa, y_link + band)],
              fill=(150, 220, 240, 120))

    ys = [240, 430, 620, 800, 980, 1160, 1320]
    for i, y in enumerate(ys):
        lane = rnd.randrange(5)
        hold = 0
        if i == 2:
            lane, hold = 1, 400
        elif i == 5:
            lane = 2
        draw_note(d, gd, lane, y, hold_px=hold)

    # 命中环
    y_hit = 1380
    s = sc(y_hit)
    c = LANE_COLORS[2]
    r = int(34 * s)
    x = W / 2 + LANE_CENTERS[2] * s
    gd.ellipse([x - r, y_hit - r, x + r, y_hit + r], outline=tuple(int(v * 0.9) for v in c), width=5)

    glow = glow.filter(ImageFilter.GaussianBlur(14))
    base = ImageChops.screen(base.convert("RGB"), glow).convert("RGBA")
    return Image.alpha_composite(base, ov)


def draw_hud(base):
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    white = (235, 250, 252, 255)
    cyan = (126, 232, 214, 255)

    d.text((70, 92), "SCORE", font=font(28), fill=cyan)
    d.text((70, 126), "1 284 760", font=font(58, bold=True), fill=white)
    d.text((70, 196), "ACC 98.42%", font=font(30), fill=cyan)

    d.text((W / 2, 600), "312", font=font(150, bold=True), fill=white, anchor="mm")
    d.text((W / 2, 700), "COMBO", font=font(30), fill=cyan, anchor="mm")

    d.text((W / 2, Y_JUDGE - 132), "PERFECT", font=font(52, bold=True), fill=cyan, anchor="mm")
    d.text((W / 2, Y_JUDGE - 86), "EARLY  -12ms", font=font(26), fill=white, anchor="mm")

    d.rounded_rectangle([960, 100, 1056, 196], radius=22, outline=white, width=4)
    d.rectangle([992, 124, 1004, 172], fill=white)
    d.rectangle([1012, 124, 1024, 172], fill=white)

    d.rounded_rectangle([100, 40, 980, 48], radius=4, fill=(255, 255, 255, 60))
    d.rounded_rectangle([100, 40, 470, 48], radius=4, fill=cyan)
    return Image.alpha_composite(base, ov)


def main():
    global TOP_SCALE
    ap = argparse.ArgumentParser()
    ap.add_argument("--bg", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--top-scale", type=float, default=None, dest="top_scale")
    ap.add_argument("--no-notes", action="store_true", dest="no_notes")
    ap.add_argument("--darken", type=float, default=0.55)
    args = ap.parse_args()

    if args.top_scale is not None:
        TOP_SCALE = args.top_scale

    bg = Image.open(args.bg).convert("RGB").resize((W, H), Image.LANCZOS)
    img = center_darken(bg, args.darken)
    img = img.convert("RGBA")
    img = draw_lanes(img)
    if not args.no_notes:
        img = draw_notes(img, args.seed)
    img = draw_hud(img)

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    img.convert("RGB").save(args.out)
    print("WROTE", args.out, img.size)


if __name__ == "__main__":
    main()
