#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FallenAngel gameplay 界面美术方向探索：4 个方向的整屏预览设计稿。

只出审阅稿（art_tools/review/gameplay_directions/），不接入游戏、不改已交付资源。、
四个方向画在同一套真实布局数字上（与游戏一致）：
  屏幕 1080×1920；5 轨中心 ±270/±135/0、轨宽 140；音符 130×14；
  判定位置距底 560；按键区底部 500 高；HUD 顶部。
轨道色相保持 Moonscraper 语义（绿/红/黄/蓝/橙）不变——四个方向只改"质感与做法"，
不玩家看得懂的颜色语言。

用法：python -X utf8 art_tools/gameplay_directions.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "art_tools", "review", "gameplay_directions")
FONT = os.path.join(ROOT, "Assets", "Fonts", "SourceHanSansCN-Regular.otf")

W, H = 1080, 1920
SS = 2                      # 超采样倍数（画完缩回一半做抗锯齿）
CX = W // 2
LANE_X = [-270, -135, 0, 135, 270]      # 5 轨中心（相对屏幕中心）
LANE_W = 140
HIT_Y = H - 560                         # 判定位置（用户拍板：距底 560）
KEY_TOP = H - 500                       # 按键区顶沿
NOTE_W, NOTE_H = 130, 14

# 轨道语义色（对齐 Assets/Scripts/Core/LaneColors.cs 的 5 键吉他色板）
LANE_RGB = [(51, 184, 77), (209, 64, 56), (242, 199, 38), (64, 128, 242), (250, 140, 26)]


# ---------- 基础工具 ----------
def sc(v):
    """超采样像素（取整：PIL 的坐标/线宽必须是 int）"""
    return int(round(v * SS))


def px(x):
    """轨道坐标（相对中心）→ 超采样像素 X"""
    return sc(CX + x)


def font(size):
    return ImageFont.truetype(FONT, sc(size))


def vgrad(img, top, bottom):
    d = ImageDraw.Draw(img)
    for y in range(img.height):
        t = y / max(1, img.height - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(4))
        d.line([(0, y), (img.width, y)], fill=c)


def overlay(base):
    return Image.new("RGBA", base.size, (0, 0, 0, 0))


def composite_glow(base, layer, radius, alpha=1.0):
    glow = layer.filter(ImageFilter.GaussianBlur(sc(radius)))
    if alpha < 1.0:
        a = glow.getchannel("A").point(lambda v: int(v * alpha))
        glow.putalpha(a)
    return Image.alpha_composite(Image.alpha_composite(base, glow), layer)


def bar(img, x0, y0, x1, y1, fill, radius=0, outline=None, width=1):
    """圆角/直角条（坐标是超采样像素）"""
    d = ImageDraw.Draw(img)
    if radius > 0:
        d.rounded_rectangle([x0, y0, x1, y1], radius=sc(radius), fill=fill, outline=outline,
                            width=max(1, sc(width)) if outline else 0)
    else:
        d.rectangle([x0, y0, x1, y1], fill=fill, outline=outline,
                    width=max(1, sc(width)) if outline else 0)


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
    return box[2] + sc(16)


def rgba(c, a):
    return (c[0], c[1], c[2], int(a * 255))


# ---------- 谱面样例（四个方向共用同一段"谱面"，只换画法） ----------
# (类型, 轨道, 屏幕高度位置 0=顶 1=底, 附加)
SAMPLE = [
    ("tap", 0, 0.20), ("tap", 3, 0.20),
    ("drag", 1, 0.30), ("drag", 2, 0.30),
    ("flick", 4, 0.40, "up"),
    ("hold", 3, 0.50, 0.62),          # 长按：头在 0.50，尾在 0.62
    ("slide", 1, 0.62, 3),            # slide：1 轨起、3 轨终
    ("wide", 0, 0.72),                # 宽键（横跨全轨）
    ("tap", 2, 0.80),
]


def note_parts(style):
    """四个方向各自的音符画法：返回 draw_tap / draw_hold / draw_slide / draw_flick 回调。"""
    glass = style["note_glass"]

    def head(img, lane, y, w=NOTE_W, h=NOTE_H, radius=None, color=None, alpha=1.0, outline=True):
        c = color or LANE_RGB[lane]
        r = style["note_radius"] if radius is None else radius
        x0, x1 = px(LANE_X[lane]) - sc(w / 2), px(LANE_X[lane]) + sc(w / 2)
        y0, y1 = sc(y) - sc(h / 2), sc(y) + sc(h / 2)
        edge = tuple(min(255, int(v + (255 - v) * style["edge_lighten"])) for v in c)
        dark = tuple(int(v * (1 - style["edge_darken"])) for v in c)
        if glass:   # 外描边 + 内面渐变 + 顶面高光
            bar(img, x0, y0, x1, y1, rgba(edge, alpha), r)
            bar(img, x0 + sc(2.4), y0 + sc(2.4), x1 - sc(2.4), y1 - sc(2.4), rgba(c, alpha),
                max(1, r - 2))
            bar(img, x0 + sc(5), y0 + sc(4.6), x1 - sc(5), y0 + sc(8.4),
                (255, 255, 255, int(0.30 * alpha * 255)), max(1, r - 5))
        else:
            bar(img, x0, y0, x1, y1, rgba(c, alpha), r,
                outline=rgba(edge, min(1.0, alpha + 0.15)) if outline else None, width=style["outline_w"])
            if style.get("inner_line"):
                bar(img, x0 + sc(3), y1 - sc(4), x1 - sc(3), y1 - sc(2),
                    rgba(dark, alpha * 0.9), 0)
        return (x0, y0, x1, y1)

    def body(img, lane, y_head, y_tail, alpha=1.0):
        x0 = px(LANE_X[lane]) - sc(NOTE_W / 2 - 1)
        x1 = px(LANE_X[lane]) + sc(NOTE_W / 2 - 1)
        c = LANE_RGB[lane]
        d = ImageDraw.Draw(img)
        for i in range(0, int(sc(y_tail) - sc(y_head))):
            t = i / max(1, sc(y_tail) - sc(y_head))
            a = (0.85 - 0.65 * t) * alpha
            d.line([(x0, sc(y_head) + i), (x1, sc(y_head) + i)], fill=rgba(c, a))

    def tail(img, lane, y, alpha=1.0):
        head(img, lane, y, w=NOTE_W - 8, h=10, color=tuple(
            min(255, int(v + (255 - v) * 0.25)) for v in LANE_RGB[lane]), alpha=alpha * 0.92)

    def arrow(img, lane, y, up=True):
        cx, cy = px(LANE_X[lane]), sc(y)
        w, hgt = sc(13), sc(11) * (1 if up else -1)
        poly = [(cx - w, cy + hgt * 0.3), (cx + w, cy + hgt * 0.3), (cx, cy - hgt * 0.7)]
        d = ImageDraw.Draw(img)
        d.polygon(poly, fill=(255, 255, 255, 235))
        d.line(poly + [poly[0]], fill=(20, 30, 40, 220), width=max(1, sc(1.4)))

    def link(img, lane_a, lane_b, y):
        xa, xb = px(LANE_X[lane_a]), px(LANE_X[lane_b])
        ca, cb = LANE_RGB[lane_a], LANE_RGB[lane_b]
        d = ImageDraw.Draw(img)
        h = sc(NOTE_H * 0.62)
        steps = 160
        for i in range(steps):
            t = i / (steps - 1)
            x = xa + (xb - xa) * t
            c = tuple(int(ca[k] + (cb[k] - ca[k]) * t) for k in range(3))
            d.rectangle([x, sc(y) - h / 2, x + (xb - xa) / steps + 1, sc(y) + h / 2],
                        fill=rgba(c, 0.75))

    def slide_path(img, lane_a, lane_b, y_a, y_b, head_x=0.0):
        """slide 头部沿路径移动，身后留下渐隐带（起点已滑过的部分收起）"""
        d = ImageDraw.Draw(img)
        pts = []
        for i in range(61):
            t = i / 60
            x = px(LANE_X[lane_a] + (LANE_X[lane_b] - LANE_X[lane_a]) * t)
            y = sc(y_a + (y_b - y_a) * t)
            pts.append((x, y))
        c = LANE_RGB[lane_a]
        for i in range(len(pts) - 1):
            t = i / (len(pts) - 2)
            d.line([pts[i], pts[i + 1]], fill=rgba(c, 0.55 * (1 - t)),
                   width=max(1, int(sc(9 * (1 - t * 0.6)))))
        return pts[-1]

    return dict(head=head, body=body, tail=tail, arrow=arrow, link=link, path=slide_path)


def draw_chart(img, style):
    """按 SAMPLE 画一遍谱面（同押连线、长按、slide、flick、宽键、命中特效）"""
    p = note_parts(style)
    fx = overlay(img)
    d = ImageDraw.Draw(fx)

    # 命中扩散环（判定位置在 HIT_Y）
    ring_c = LANE_RGB[2]
    for i, (rr, aa) in enumerate(((58, 0.85), (92, 0.42), (130, 0.18))):
        r = sc(rr)
        d.ellipse([px(LANE_X[2]) - r, sc(HIT_Y) - r, px(LANE_X[2]) + r, sc(HIT_Y) + r],
                  outline=rgba(ring_c, aa), width=max(2, sc(3.5 - i)))
    img = composite_glow(img, fx, 10)

    for item in SAMPLE:
        kind, lane, y = item[0], item[1], sc(item[2] * (H - 240) + 120)
        if kind == "tap":
            p["head"](img, lane, y)
        elif kind == "drag":
            p["head"](img, lane, y, w=NOTE_W * 0.72, h=NOTE_H * 0.86, alpha=0.85)
        elif kind == "flick":
            p["head"](img, lane, y)
            p["arrow"](img, lane, y, up=(item[3] == "up"))
        elif kind == "hold":
            y_tail = sc(item[3] * (H - 240) + 120)
            p["body"](img, lane, y, y_tail)
            p["tail"](img, lane, y_tail)
            p["head"](img, lane, y)
        elif kind == "slide":
            end = p["path"](img, lane, item[3], y, HIT_Y - sc(40))
            p["tail"](img, lane, end[1] / SS, alpha=0.9)
            p["head"](img, lane, y, color=LANE_RGB[lane])
        elif kind == "wide":
            x0, x1 = px(LANE_X[0]) - sc(NOTE_W / 2), px(LANE_X[4]) + sc(NOTE_W / 2)
            d2 = ImageDraw.Draw(img)
            d2.rounded_rectangle([x0, sc(y) - sc(NOTE_H / 2), x1, sc(y) + sc(NOTE_H / 2)],
                                 radius=sc(style["note_radius"]), fill=rgba((210, 214, 220), 0.85))
            text(img, ((x0 + x1) / 2, sc(y)), "WIDE", 26, (25, 32, 40, 230))

    p["link"](img, 1, 2, sc(0.30 * (H - 240) + 120))
    return img


# ---------- 四个方向的场景 ----------
def dir_a(img, style):
    """深海霓虹：玻璃质 + 青色辉光（现基调延伸）"""
    vgrad(img, (10, 24, 38, 255), (4, 10, 20, 255))
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    for cx, cy, r, a in ((180, 420, 300, 0.20), (900, 250, 220, 0.16), (540, 900, 420, 0.10),
                         (760, 1500, 300, 0.12)):
        g.ellipse([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], fill=(64, 200, 220, int(a * 255)))
    img = composite_glow(img, glow, 60)
    # 水面波光
    d = ImageDraw.Draw(img)
    for i in range(9):
        y = sc(150 + i * 190)
        d.line([(0, y), (sc(W), y - sc(60))], fill=(120, 200, 220, 12), width=sc(2))
    return img


def dir_b(img, style):
    """极简硬边：近黑底、直角色块、1px 判定线"""
    vgrad(img, (16, 17, 20, 255), (9, 10, 12, 255))
    d = ImageDraw.Draw(img)
    d.polygon([(0, sc(300)), (sc(W), sc(120)), (sc(W), sc(160)), (0, sc(340))], fill=(255, 255, 255, 10))
    d.polygon([(0, sc(1300)), (sc(W), sc(1150)), (sc(W), sc(1170)), (0, sc(1320))], fill=(255, 255, 255, 8))
    return img


def dir_c(img, style):
    """生物荧光：有机曲线 + 发光点 + 磨砂柱"""
    vgrad(img, (6, 26, 34, 255), (2, 10, 16, 255))
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    for i in range(7):
        x0, y0 = 90 + i * 150, 1700 - i * 40
        pts = [(sc(x0 + 60 * math.sin(j / 6 * math.pi)), sc(y0 + j * 30)) for j in range(20)]
        g.line(pts, fill=(70, 220, 190, 40), width=sc(3), joint="curve")
    for cx, cy, r in ((300, 600, 120), (800, 480, 90), (620, 1150, 70)):
        g.ellipse([sc(cx - r), sc(cy - r), sc(cx + r), sc(cy + r)], fill=(60, 230, 200, 38))
    return composite_glow(img, glow, 38)


def dir_d(img, style):
    """赛博扫描：暗紫 + 扫描线 + 品红/青双色"""
    vgrad(img, (18, 12, 30, 255), (8, 6, 16, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, H, 6):
        d.line([(0, sc(y)), (sc(W), sc(y))], fill=(255, 255, 255, 8), width=1)
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    g.line([(0, sc(1200)), (sc(W), sc(1120))], fill=(230, 60, 200, 60), width=sc(3))
    g.line([(0, sc(700)), (sc(W), sc(660))], fill=(60, 220, 240, 50), width=sc(2))
    return composite_glow(img, glow, 26)


def draw_lanes(img, style):
    d = ImageDraw.Draw(img)
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    for i, x in enumerate(LANE_X):
        c = LANE_RGB[i]
        x0, x1 = px(x) - sc(LANE_W / 2), px(x) + sc(LANE_W / 2)
        if style["lane_style"] == "glass":
            d.rounded_rectangle([x0, sc(120), x1, sc(H)], radius=sc(40), fill=rgba(c, 0.07))
            d.rounded_rectangle([x0, sc(120), x1, sc(H)], radius=sc(40), outline=rgba(c, 0.16), width=sc(2))
        elif style["lane_style"] == "flat":
            d.rectangle([x0, sc(140), x1, sc(H)], fill=rgba(c, 0.055))
        elif style["lane_style"] == "soft":
            d.rounded_rectangle([x0, sc(140), x1, sc(H)], radius=sc(52), fill=rgba(c, 0.075))
            g.rounded_rectangle([x0, sc(140), x1, sc(H)], radius=sc(52), outline=rgba(c, 0.22), width=sc(4))
        else:
            d.rectangle([x0, sc(140), x1, sc(H)], outline=rgba(c, 0.20), width=sc(2))
            d.rectangle([x0, sc(140), x0 + sc(3), sc(H)], fill=rgba(c, 0.5))
    img = composite_glow(img, glow, 16) if style["lane_style"] == "soft" else img

    # 按键区（底部 500）与判定线
    d = ImageDraw.Draw(img)
    key = overlay(img)
    kd = ImageDraw.Draw(key)
    kd.rectangle([0, sc(KEY_TOP), sc(W), sc(H)], fill=(8, 14, 22, 150 if style["lane_style"] != "flat" else 90))
    img = Image.alpha_composite(img, key)

    d = ImageDraw.Draw(img)
    for i, x in enumerate(LANE_X):
        x0, x1 = px(x) - sc(LANE_W / 2), px(x) + sc(LANE_W / 2)
        d.rectangle([x0, sc(KEY_TOP), x1, sc(H)], fill=rgba(LANE_RGB[i], 0.16))
        if style["key_icon"]:
            text(img, (px(x), sc(H - 190)), style["key_icon"][i], 40, rgba(LANE_RGB[i], 0.95))
    return img


def draw_judge(img, style):
    glow = overlay(img)
    g = ImageDraw.Draw(glow)
    line_w = style["judge_w"]
    if style["judge_style"] == "bar":
        g.rectangle([sc(60), sc(HIT_Y - line_w / 2), sc(W - 60), sc(HIT_Y + line_w / 2)],
                    fill=(255, 255, 255, 235))
        g.rectangle([sc(60), sc(HIT_Y - 16), sc(W - 60), sc(HIT_Y + 16)],
                    fill=(120, 220, 240, 60))
    elif style["judge_style"] == "hairline":
        g.rectangle([0, sc(HIT_Y - line_w / 2), sc(W), sc(HIT_Y + line_w / 2)],
                    fill=(255, 255, 255, 250))
    elif style["judge_style"] == "arc":
        for i in range(LANE_X[0] - 90, LANE_X[4] + 90, 4):
            yy = HIT_Y - 18 * math.sin((i - LANE_X[0]) / 620 * math.pi)
            g.ellipse([px(i) - sc(3), sc(yy - 3), px(i) + sc(3), sc(yy + 3)], fill=(180, 255, 235, 200))
    else:
        for i in range(60, W - 60, 46):
            g.rectangle([sc(i), sc(HIT_Y - line_w), sc(i + 26), sc(HIT_Y + line_w)],
                        fill=(255, 90, 220, 210))
    return composite_glow(img, glow, style["judge_glow"])


def draw_hud(img, style):
    fg = style["hud_fg"]
    text(img, (sc(60), sc(150)), "1,234,567", 54, fg, anchor="la")
    text(img, (sc(60), sc(214)), "SCORE", 22, rgba(fg[:3], 0.55), anchor="la")
    text(img, (sc(W - 60), sc(150)), "98.7%", 46, fg, anchor="ra")
    text(img, (sc(W - 60), sc(210)), "ACCURACY", 22, rgba(fg[:3], 0.55), anchor="ra")
    text(img, (sc(W / 2), sc(420)), "128", 132, fg, anchor="mm",
         stroke=style["hud_stroke"], stroke_fill=style["hud_stroke_fill"])
    text(img, (sc(W / 2), sc(510)), "COMBO", 26, rgba(fg[:3], 0.6), anchor="mm")
    text(img, (sc(W / 2), sc(HIT_Y - 96)), style["judge_text"], 54, style["judge_text_fill"],
         anchor="mm", stroke=style["hud_stroke"], stroke_fill=style["hud_stroke_fill"])
    # 暂停按钮
    x0, y0 = sc(W - 120), sc(300)
    bar(img, x0, y0, x0 + sc(64), y0 + sc(18), rgba(fg[:3], 0.75), 6)
    bar(img, x0, y0 + sc(30), x0 + sc(64), y0 + sc(48), rgba(fg[:3], 0.75), 6)
    return img


STYLES = {
    "A": dict(name="深海霓虹", tag="玻璃质感 + 青色辉光 · 现基调延伸",
              note_glass=True, note_radius=5, edge_lighten=0.55, edge_darken=0.45, outline_w=0,
              lane_style="glass", judge_style="bar", judge_w=3, judge_glow=14,
              hud_fg=(233, 240, 244, 255), hud_stroke=2, hud_stroke_fill=(10, 30, 42, 200),
              judge_text="PERFECT", judge_text_fill=(255, 255, 255, 240),
              key_icon=["D", "F", "G", "J", "K"]),
    "B": dict(name="极简硬边", tag="近黑底 + 直角纯色块 · 1px 判定线",
              note_glass=False, note_radius=0, edge_lighten=0.0, edge_darken=0.25, outline_w=0,
              inner_line=True, lane_style="flat", judge_style="hairline", judge_w=2, judge_glow=6,
              hud_fg=(240, 240, 240, 255), hud_stroke=0, hud_stroke_fill=None,
              judge_text="PERFECT", judge_text_fill=(255, 255, 255, 235),
              key_icon=["", "", "", "", ""]),
    "C": dict(name="生物荧光", tag="磨砂玻璃柱 + 有机发光 · 圆润胶囊",
              note_glass=False, note_radius=7, edge_lighten=0.35, edge_darken=0.15, outline_w=2,
              inner_line=False, lane_style="soft", judge_style="arc", judge_w=3, judge_glow=22,
              hud_fg=(226, 255, 246, 255), hud_stroke=3, hud_stroke_fill=(6, 40, 38, 200),
              judge_text="PERFECT", judge_text_fill=(210, 255, 240, 245),
              key_icon=["◐", "◑", "◒", "◓", "◔"]),
    "D": dict(name="赛博扫描", tag="暗紫扫描线 + 品红/青双色 · 切角框",
              note_glass=False, note_radius=3, edge_lighten=0.85, edge_darken=0.5, outline_w=2,
              inner_line=True, lane_style="frame", judge_style="dash", judge_w=3, judge_glow=18,
              hud_fg=(240, 226, 255, 255), hud_stroke=2, hud_stroke_fill=(60, 10, 70, 210),
              judge_text="PERFECT", judge_text_fill=(255, 120, 230, 245),
              key_icon=["D", "F", "G", "J", "K"]),
}
BACKDROPS = {"A": dir_a, "B": dir_b, "C": dir_c, "D": dir_d}


def build(key):
    style = STYLES[key]
    img = Image.new("RGBA", (sc(W), sc(H)), (0, 0, 0, 255))
    img = BACKDROPS[key](img, style)
    img = draw_lanes(img, style)
    img = draw_judge(img, style)
    img = draw_chart(img, style)
    img = draw_hud(img, style)

    # 审阅标签
    pill(img, sc(36), sc(36), f"方向 {key}｜{style['name']}", 34,
         (255, 255, 255, 245), (12, 20, 28, 205))
    pill(img, sc(36), sc(H - 92), style["tag"], 26, (226, 236, 240, 240), (12, 20, 28, 195))

    return img.resize((W, H), Image.LANCZOS).convert("RGB")


def main():
    os.makedirs(OUT, exist_ok=True)
    shots = {}
    for key in ("A", "B", "C", "D"):
        im = build(key)
        path = os.path.join(OUT, f"gameplay_{key}_{STYLES[key]['name']}.png")
        im.save(path)
        shots[key] = im
        print("saved", path)

    tile_w, tile_h = 520, 924
    sheet = Image.new("RGB", (tile_w * 2 + 36, tile_h * 2 + 132), (10, 12, 16))
    d = ImageDraw.Draw(sheet)
    for i, key in enumerate(("A", "B", "C", "D")):
        col, row = i % 2, i // 2
        x = 12 + col * (tile_w + 12)
        y = 60 + row * (tile_h + 36)
        sheet.paste(shots[key].resize((tile_w, tile_h), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + tile_w, y + tile_h], outline=(46, 56, 66), width=1)
        d.text((x + 8, y + tile_h + 8), f"{key} · {STYLES[key]['name']} — {STYLES[key]['tag']}",
               font=ImageFont.truetype(FONT, 17), fill=(214, 224, 232))
    d.text((12, 20), "FallenAngel gameplay 美术方向预览（同一段谱面样例，只换做法）",
           font=ImageFont.truetype(FONT, 22), fill=(236, 244, 250))
    path = os.path.join(OUT, "gameplay_directions_compare.png")
    sheet.save(path)
    print("saved", path)


if __name__ == "__main__":
    main()
