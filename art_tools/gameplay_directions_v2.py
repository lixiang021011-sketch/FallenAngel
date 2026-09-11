#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FallenAngel gameplay 美术方向探索 v2：深海荧光 · 同色系 · 透视轨道。

按用户反馈重做（v1 的 A/B/C/D 已否决）：
  ① 参考气质：《明日方舟》集成战略「水月与深蓝之森」——深海生物荧光、克制的留白、
     月光与水面、细线勾勒，不堆装饰；
  ② 同色系配色：全场落在蓝—青—月光白一个色族内（色相 165°~215°），
     轨道靠明度/彩度阶梯 + 轨标形状区分，不靠跨色相对比；
  ③ 轨道近大远小：轨道不是平行竖线，而是向屏幕上方收敛的透视梯形，
     音符随深度缩小（近大远小）；
  ④ 轨道外、屏幕内的缝隙收窄：判定线处轨道组占屏宽约 1004/1080（两侧各留 ~38px）。

布局数字与游戏一致：1080×1920、5 轨、判定位置距底 560、按键区底部 500 高。
音符尺寸 130×14 在判定线处按同比例放大（透视近端）。

三个方案（同一构图，只换做法）：
  V1 深潜 — 深蓝底 + 青绿荧光，最接近现有基调
  V2 月映 — 清透蓝灰 + 月盘涟漪，最空灵、留白最多
  V3 幽林 — 深蓝绿 + 两侧荧光藻林，最"深蓝之森"

v3 改动（用户反馈）：① 判定线整体下移（默认距底 400，v2 为 560）；
② 去掉按键区那五个轨标小多边形；③ 附一张判定线位置对照条（560/480/400/320）。

用法：python -X utf8 art_tools/gameplay_directions_v2.py [判定线距底像素]
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "art_tools", "review", "gameplay_directions_v3")
FONT = os.path.join(ROOT, "Assets", "Fonts", "SourceHanSansCN-Regular.otf")

W, H = 1080, 1920
SS = 2
CX = W // 2
LINE_FROM_BOTTOM = int(sys.argv[1]) if len(sys.argv) > 1 else 400
HIT_Y = H - LINE_FROM_BOTTOM    # 判定线位置（v3：下移到距底 400）
KEY_BOTTOM = H

# 透视：判定线处为近端（scale=1），屏幕顶端为远端
S_TOP = 0.42
LANE_BASE = [-410, -205, 0, 205, 410]     # 判定线处各轨中心（相对屏幕中心）
LANE_HALF = 92                            # 判定线处单轨半宽 → 轨组宽 1004
FIELD_HALF = LANE_BASE[-1] + LANE_HALF    # 502：轨道外沿


def sc(v):
    return int(round(v * SS))


def set_line(from_bottom):
    """改动判定线位置（相对屏幕底部像素），供对照条逐档出图"""
    global LINE_FROM_BOTTOM, HIT_Y
    LINE_FROM_BOTTOM = from_bottom
    HIT_Y = H - from_bottom


def s_at(y):
    """深度缩放：判定线=1，越往上越小"""
    if y >= HIT_Y:
        return 1.0
    return S_TOP + (1.0 - S_TOP) * (y / HIT_Y)


def lane_cx(lane, y):
    return CX + LANE_BASE[lane] * s_at(y)


def lane_hw(y):
    return LANE_HALF * s_at(y)


def font(size):
    return ImageFont.truetype(FONT, sc(size))


def rgba(c, a):
    return (c[0], c[1], c[2], max(0, min(255, int(a * 255))))


def overlay(base):
    return Image.new("RGBA", base.size, (0, 0, 0, 0))


def composite_glow(base, layer, radius):
    return Image.alpha_composite(Image.alpha_composite(
        base, layer.filter(ImageFilter.GaussianBlur(sc(radius)))), layer)


def vgrad(img, top, bottom):
    d = ImageDraw.Draw(img)
    for y in range(img.height):
        t = y / max(1, img.height - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(4))
        d.line([(0, y), (img.width, y)], fill=c)


def text(img, xy, s, size, fill, anchor="mm", stroke=0, stroke_fill=None):
    ImageDraw.Draw(img).text(xy, s, font=font(size), fill=fill, anchor=anchor,
                             stroke_width=sc(stroke) if stroke else 0, stroke_fill=stroke_fill)


def pill(img, x, y, s, size, fg, bg):
    f = font(size)
    d = ImageDraw.Draw(img)
    box = d.textbbox((x, y), s, font=f, anchor="la")
    d.rounded_rectangle([box[0] - sc(16), box[1] - sc(10), box[2] + sc(16), box[3] + sc(10)],
                        radius=sc(14), fill=bg)
    d.text((x, y), s, font=f, fill=fg, anchor="la")


# ---------- 三个方案的色族（同色系：蓝—青—月白） ----------
PALETTES = {
    "V1": dict(name="深潜", tag="深蓝底 + 青绿荧光 · 最贴近现基调",
               bg_top=(8, 26, 44, 255), bg_bottom=(3, 11, 20, 255),
               accent=(126, 232, 214), ink=(226, 246, 246, 255),
               lanes=[(79, 224, 200), (87, 190, 232), (127, 168, 242), (169, 214, 245), (69, 217, 168)],
               note_style="glass", glow=1.0),
    "V2": dict(name="月映", tag="清透蓝灰 + 月盘涟漪 · 留白最多",
               bg_top=(26, 46, 64, 255), bg_bottom=(9, 20, 32, 255),
               accent=(214, 240, 246), ink=(238, 248, 252, 255),
               lanes=[(196, 232, 240), (152, 206, 232), (176, 196, 238), (222, 236, 246), (150, 224, 214)],
               note_style="pale", glow=0.7),
    "V3": dict(name="幽林", tag="深蓝绿 + 荧光藻林 · 最《深蓝之森》",
               bg_top=(6, 30, 34, 255), bg_bottom=(2, 10, 14, 255),
               accent=(112, 240, 190), ink=(228, 250, 240, 255),
               lanes=[(96, 232, 176), (86, 208, 206), (120, 186, 226), (166, 222, 226), (74, 226, 156)],
               note_style="core", glow=1.15),
}


# ---------- 背景 ----------
def backdrop(img, key, p):
    vgrad(img, p["bg_top"], p["bg_bottom"])
    glow = overlay(img)
    g = ImageDraw.Draw(glow)

    if key == "V1":
        # 远处生物光晕 + 水母剪影 + 微粒
        for cx, cy, r, a in ((250, 520, 300, 0.20), (860, 380, 250, 0.16), (540, 1080, 380, 0.10)):
            g.ellipse([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], fill=rgba(p["accent"], a))
        for cx, cy, s0 in ((210, 640, 1.0), (905, 470, 0.7)):
            g.ellipse([sc(cx - 72 * s0), sc(cy - 52 * s0), sc(cx + 72 * s0), sc(cy + 52 * s0)],
                      outline=rgba(p["accent"], 0.30), width=sc(2))
            for i in range(7):
                off = (i - 3) * 22 * s0
                pts = [(sc(cx + off), sc(cy + 40 * s0)),
                       (sc(cx + off * 1.25 + 14 * math.sin(i)), sc(cy + 150 * s0)),
                       (sc(cx + off * 1.5 + 26 * math.sin(i * 1.7)), sc(cy + 260 * s0))]
                g.line(pts, fill=rgba(p["accent"], 0.16), width=sc(2), joint="curve")
    elif key == "V2":
        # 月盘 + 水面涟漪
        g.ellipse([sc(CX - 190), sc(300 - 190), sc(CX + 190), sc(300 + 190)],
                  fill=rgba(p["accent"], 0.13))
        g.ellipse([sc(CX - 118), sc(300 - 118), sc(CX + 118), sc(300 + 118)],
                  fill=rgba((255, 255, 255), 0.10))
        for i in range(9):
            ry = 40 + i * 26
            g.arc([sc(CX - 420 - i * 26), sc(760 - ry), sc(CX + 420 + i * 26), sc(760 + ry)],
                  start=200, end=340, fill=rgba(p["accent"], 0.10), width=sc(2))
    else:
        # 两侧荧光藻林
        for side in (0, 1):
            for i in range(11):
                bx = 40 + i * 74 if side == 0 else W - 40 - i * 74
                top = 320 + (i % 5) * 90
                pts = []
                for j in range(16):
                    t = j / 15
                    pts.append((sc(bx + math.sin(t * 3 + i) * 26), sc(top + t * 1500)))
                g.line(pts, fill=rgba(p["accent"], 0.10 + 0.05 * (i % 3)), width=sc(2), joint="curve")
                g.ellipse([sc(bx - 9), sc(top - 9), sc(bx + 9), sc(top + 9)],
                          fill=rgba(p["accent"], 0.35))

    # 微粒（确定性伪随机）
    for i in range(90):
        x = (i * 271) % W
        y = (i * 613) % H
        r = 1 + (i % 3)
        a = 0.06 + 0.05 * (i % 4)
        g.ellipse([sc(x - r), sc(y - r), sc(x + r), sc(y + r)], fill=rgba(p["accent"], a))

    return composite_glow(img, glow, 34 * p["glow"])


# ---------- 轨道 / 按键区 / 判定线 ----------
def field(img, key, p):
    layer = overlay(img)
    d = ImageDraw.Draw(layer)

    # 轨道柱：分段四边形贴合透视
    step = 60
    for lane in range(5):
        c = p["lanes"][lane]
        for y in range(120, HIT_Y, step):
            y2 = min(HIT_Y, y + step)
            x0a, x1a = lane_cx(lane, y) - lane_hw(y), lane_cx(lane, y) + lane_hw(y)
            x0b, x1b = lane_cx(lane, y2) - lane_hw(y2), lane_cx(lane, y2) + lane_hw(y2)
            t = y / HIT_Y
            d.polygon([(sc(x0a), sc(y)), (sc(x1a), sc(y)), (sc(x1b), sc(y2)), (sc(x0b), sc(y2))],
                      fill=rgba(c, 0.035 + 0.05 * t))
        # 按键区（判定线以下，近端不再继续放大）
        cx, hw = lane_cx(lane, HIT_Y), LANE_HALF
        d.polygon([(sc(cx - hw), sc(HIT_Y)), (sc(cx + hw), sc(HIT_Y)),
                   (sc(cx + hw), sc(H)), (sc(cx - hw), sc(H))], fill=rgba(c, 0.10))

    # 轨道分隔细线（透视方向）
    for i in range(6):
        edge = -FIELD_HALF + i * (FIELD_HALF * 2 / 5)
        pts = [(sc(CX + edge * s_at(y)), sc(y)) for y in range(0, HIT_Y + 1, 40)]
        pts += [(sc(CX + edge), sc(y)) for y in range(HIT_Y, H, 40)]
        d.line(pts, fill=rgba(p["accent"], 0.13), width=sc(1.6), joint="curve")

    # 轨道组外沿发丝光（屏幕内缝隙只剩 ~38px）
    for sign in (-1, 1):
        pts = [(sc(CX + sign * FIELD_HALF * s_at(y)), sc(y)) for y in range(0, HIT_Y + 1, 40)]
        pts += [(sc(CX + sign * FIELD_HALF), sc(y)) for y in range(HIT_Y, H, 40)]
        d.line(pts, fill=rgba(p["accent"], 0.34), width=sc(2.4), joint="curve")

    img = composite_glow(img, layer, 12 * p["glow"])
    return img


def judge_line(img, key, p):
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    x0, x1 = CX - FIELD_HALF - 14, CX + FIELD_HALF + 14
    g.rectangle([sc(x0), sc(HIT_Y - 30), sc(x1), sc(HIT_Y + 26)], fill=rgba(p["accent"], 0.16))
    g.rectangle([sc(x0), sc(HIT_Y - 3), sc(x1), sc(HIT_Y + 3)], fill=rgba(p["ink"][:3], 0.90))
    if key == "V2":      # 月映：水波细纹代替硬线
        for i in range(3):
            g.line([(sc(x0), sc(HIT_Y - 10 + i * 10)), (sc(x1), sc(HIT_Y - 12 + i * 10))],
                   fill=rgba(p["accent"], 0.28), width=sc(1.4))
    # v3：移除按键区的五个轨标小多边形（用户反馈）
    return composite_glow(img, glow, 16 * p["glow"])


# ---------- 音符（随深度缩放） ----------
def note(img, key, p, lane, y, kind="tap", hold_to=None, slide_to=None, arrow=None):
    s = s_at(y)
    c = p["lanes"][lane]
    cx, hw = lane_cx(lane, y), lane_hw(y)
    bar_w = (hw * 2) * 0.90
    bar_h = max(5.0, 14.0 * s)
    x0, x1 = cx - bar_w / 2, cx + bar_w / 2
    y0, y1 = y - bar_h / 2, y + bar_h / 2
    layer = overlay(img)
    d = ImageDraw.Draw(layer)

    def rounded(x0, y0, x1, y1, r, fill, outline=None, width=1):
        d.rounded_rectangle([sc(x0), sc(y0), sc(x1), sc(y1)], radius=sc(r), fill=fill,
                            outline=outline, width=sc(width) if outline else 0)

    if hold_to is not None:      # 连接段：向远端渐隐
        y2 = hold_to
        steps = 90
        for i in range(steps):
            t = i / (steps - 1)
            yy = y + (y2 - y) * t
            hwt = lane_hw(yy) * 0.78
            d.rectangle([sc(lane_cx(lane, yy) - hwt), sc(yy), sc(lane_cx(lane, yy) + hwt), sc(yy + 2)],
                        fill=rgba(c, 0.72 * (1 - t) + 0.10))

    if slide_to is not None:     # slide：路径带（从头到终点，尾端渐隐）
        steps = 80
        for i in range(steps):
            t = i / (steps - 1)
            yy = y + (HIT_Y - 40 - y) * t
            xx = lane_cx(lane, yy) + (lane_cx(slide_to, yy) - lane_cx(lane, yy)) * t
            hwt = lane_hw(yy) * 0.55 * (1 - t * 0.5)
            d.rectangle([sc(xx - hwt), sc(yy), sc(xx + hwt), sc(yy + 2)], fill=rgba(c, 0.60 * (1 - t)))

    if kind in ("tap", "hold", "flick", "wide", "slide"):
        if p["note_style"] == "glass":
            edge = tuple(min(255, int(v + (255 - v) * 0.5)) for v in c)
            rounded(x0, y0, x1, y1, bar_h / 2, rgba(edge, 0.85))
            rounded(x0 + 2.4, y0 + 2.4 * s, x1 - 2.4, y1 - 2.4 * s, max(1, bar_h / 2 - 2), rgba(c, 0.95))
            rounded(x0 + 5, y0 + 2.6, x1 - 5, y0 + bar_h * 0.42, max(1, bar_h / 2 - 4),
                    (255, 255, 255, int(0.34 * 255)))
        elif p["note_style"] == "pale":
            rounded(x0, y0, x1, y1, bar_h / 2, rgba(c, 0.92))
            rounded(x0 + 3, y0 + 2, x1 - 3, y0 + bar_h * 0.5, max(1, bar_h / 2 - 3),
                    (255, 255, 255, int(0.42 * 255)))
            d.rounded_rectangle([sc(x0), sc(y0), sc(x1), sc(y1)], radius=sc(bar_h / 2),
                                outline=rgba(p["accent"], 0.9), width=sc(1.6))
        else:                     # core：内芯发光
            rounded(x0, y0, x1, y1, bar_h / 2, rgba(c, 0.35))
            rounded(x0 + bar_w * 0.18, y0 + bar_h * 0.18, x1 - bar_w * 0.18, y1 - bar_h * 0.18,
                    max(1, bar_h * 0.3), rgba((236, 255, 250), 0.92))
            d.rounded_rectangle([sc(x0), sc(y0), sc(x1), sc(y1)], radius=sc(bar_h / 2),
                                outline=rgba(c, 0.95), width=sc(1.8))

    if kind == "flick" and arrow:
        cx2, cy2 = cx, y
        w, hgt = sc(11 * s), sc(9 * s) * (1 if arrow == "up" else -1)
        d.polygon([(cx2 - w, cy2 + hgt * 0.3), (cx2 + w, cy2 + hgt * 0.3), (cx2, cy2 - hgt * 0.7)],
                  fill=(255, 255, 255, 235))

    if hold_to is not None:      # 尾标记
        hwt = lane_hw(hold_to) * 0.86
        rounded(lane_cx(lane, hold_to) - hwt, hold_to - 5 * s_at(hold_to),
                lane_cx(lane, hold_to) + hwt, hold_to + 5 * s_at(hold_to), 4,
                rgba(tuple(min(255, int(v + (255 - v) * 0.3)) for v in c), 0.95))

    return composite_glow(img, layer, 6 * p["glow"])


def link(img, p, lane_a, lane_b, y):
    s = s_at(y)
    xa, xb = lane_cx(lane_a, y), lane_cx(lane_b, y)
    ca, cb = p["lanes"][lane_a], p["lanes"][lane_b]
    layer = overlay(img)
    d = ImageDraw.Draw(layer)
    h = max(4.0, 9.0 * s)
    steps = 120
    for i in range(steps):
        t = i / (steps - 1)
        x = xa + (xb - xa) * t
        c = tuple(int(ca[k] + (cb[k] - ca[k]) * t) for k in range(3))
        d.rectangle([sc(x), sc(y - h / 2), sc(x + (xb - xa) / steps) + 1, sc(y + h / 2)],
                    fill=rgba(c, 0.7))
    return composite_glow(img, layer, 7 * p["glow"])


SAMPLE = [
    ("tap", 0, 0.16), ("tap", 4, 0.16),
    ("drag", 1, 0.26), ("drag", 3, 0.26),
    ("link", 1, 2, 0.26),
    ("flick", 4, 0.36, "up"),
    ("hold", 3, 0.46, 0.62),
    ("slide", 1, 0.58, 3),
    ("wide", 0, 0.70),
    ("tap", 2, 0.80),
]


def chart(img, key, p):
    for item in SAMPLE:
        kind, lane, ty = item[0], item[1], item[2]
        y = 150 + ty * (HIT_Y - 150)
        if kind == "link":
            img = link(img, p, item[1], item[2], 150 + item[3] * (HIT_Y - 150))
        elif kind == "hold":
            img = note(img, key, p, lane, y, "hold", hold_to=150 + item[3] * (HIT_Y - 150))
        elif kind == "slide":
            img = note(img, key, p, lane, y, "slide", slide_to=item[3])
        elif kind == "flick":
            img = note(img, key, p, lane, y, "flick", arrow=item[3])
        elif kind == "wide":
            s = s_at(y)
            hw = FIELD_HALF * s
            layer = overlay(img)
            d = ImageDraw.Draw(layer)
            d.rounded_rectangle([sc(CX - hw), sc(y - 7 * s), sc(CX + hw), sc(y + 7 * s)],
                                radius=sc(7 * s), fill=rgba(p["accent"], 0.80))
            img = composite_glow(img, layer, 8 * p["glow"])
        else:
            img = note(img, key, p, lane, y, kind)
    return img


def hit_fx(img, key, p):
    layer = overlay(img)
    d = ImageDraw.Draw(layer)
    for r, a, w in ((56, 0.55, 3), (92, 0.30, 2), (128, 0.14, 1.6)):
        d.ellipse([sc(CX - r), sc(HIT_Y - r), sc(CX + r), sc(HIT_Y + r)],
                  outline=rgba(p["ink"][:3], a), width=sc(w))
    return composite_glow(img, layer, 14 * p["glow"])


def hud(img, key, p):
    ink = p["ink"]
    text(img, (sc(58), sc(120)), "1,234,567", 46, ink, anchor="la")
    text(img, (sc(58), sc(178)), "SCORE", 20, rgba(ink[:3], 0.50), anchor="la")
    text(img, (sc(W - 58), sc(120)), "98.7%", 40, ink, anchor="ra")
    text(img, (sc(W - 58), sc(172)), "ACC", 20, rgba(ink[:3], 0.50), anchor="ra")
    text(img, (sc(CX), sc(600)), "128", 118, ink, anchor="mm")
    text(img, (sc(CX), sc(690)), "COMBO", 24, rgba(ink[:3], 0.55), anchor="mm")
    text(img, (sc(CX), sc(HIT_Y - 92)), "PERFECT", 48, rgba(p["accent"], 0.96), anchor="mm")
    # 暂停（两道细线，克制）
    d = ImageDraw.Draw(img)
    for i in range(2):
        d.rounded_rectangle([sc(W - 112 + i * 26), sc(300), sc(W - 100 + i * 26), sc(346)],
                            radius=sc(6), fill=rgba(ink[:3], 0.65))
    return img


def build(key):
    p = PALETTES[key]
    img = Image.new("RGBA", (sc(W), sc(H)), (0, 0, 0, 255))
    img = backdrop(img, key, p)
    img = field(img, key, p)
    img = judge_line(img, key, p)
    img = chart(img, key, p)
    img = hit_fx(img, key, p)
    img = hud(img, key, p)

    pill(img, sc(36), sc(36), f"方案 {key[-1]}｜{p['name']}", 32, (255, 255, 255, 245), (6, 16, 24, 200))
    pill(img, sc(36), sc(H - 86), p["tag"], 24, (232, 244, 248, 240), (6, 16, 24, 190))
    return img.resize((W, H), Image.LANCZOS).convert("RGB")


def main():
    os.makedirs(OUT, exist_ok=True)
    primary = LINE_FROM_BOTTOM
    shots = {}
    for key in ("V1", "V2", "V3"):
        im = build(key)
        path = os.path.join(OUT, f"gameplay_{key}_{PALETTES[key]['name']}_line{primary}.png")
        im.save(path)
        shots[key] = im
        print("saved", path)

    tw, th = 560, 995
    sheet = Image.new("RGB", (tw * 3 + 48, th + 96), (6, 10, 14))
    d = ImageDraw.Draw(sheet)
    d.text((14, 22), f"FallenAngel gameplay 美术方向 v3：深海荧光 / 同色系 / 透视轨道 · 判定线距底 {primary}px",
           font=ImageFont.truetype(FONT, 24), fill=(226, 240, 246))
    for i, key in enumerate(("V1", "V2", "V3")):
        x = 12 + i * (tw + 12)
        sheet.paste(shots[key].resize((tw, th), Image.LANCZOS), (x, 62))
        d.rectangle([x, 62, x + tw, 62 + th], outline=(40, 56, 68), width=1)
        d.text((x + 6, 62 + th + 8), f"{key[-1]} · {PALETTES[key]['name']} — {PALETTES[key]['tag']}",
               font=ImageFont.truetype(FONT, 17), fill=(206, 222, 232))
    path = os.path.join(OUT, f"gameplay_v3_compare_line{primary}.png")
    sheet.save(path)
    print("saved", path)

    # 判定线位置对照条（同一方案 V1 出四档，省一轮往返）
    strip_shots = []
    for v in (560, 480, 400, 320):
        set_line(v)
        strip_shots.append((v, build("V1")))
    set_line(primary)
    sw, sh = 300, 533
    strip = Image.new("RGB", (sw * 4 + 60, sh + 96), (6, 10, 14))
    sd = ImageDraw.Draw(strip)
    sd.text((14, 22), "判定线位置对照（V1 深潜）：距底 560 / 480 / 400 / 320",
            font=ImageFont.truetype(FONT, 23), fill=(226, 240, 246))
    f17 = ImageFont.truetype(FONT, 17)
    for i, (v, im) in enumerate(strip_shots):
        x = 12 + i * (sw + 12)
        strip.paste(im.resize((sw, sh), Image.LANCZOS), (x, 62))
        sd.rectangle([x, 62, x + sw, 62 + sh], outline=(40, 56, 68), width=1)
        note = "（v2 原值）" if v == 560 else ("（本稿）" if v == primary else "")
        sd.text((x + 6, 62 + sh + 8), f"距底 {v}px {note}", font=f17, fill=(206, 222, 232))
    path = os.path.join(OUT, "gameplay_v3_line_positions.png")
    strip.save(path)
    print("saved", path)


if __name__ == "__main__":
    main()
