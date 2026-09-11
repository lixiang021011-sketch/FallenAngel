#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FallenAngel 演奏界面设计系统（参考《明日方舟》集成战略#3「水月与深蓝之树」）。

产出：
    out/design/FA_ui_design_sheet.png   设计图纸：色板 / 音符icon / 按钮 / 特效 / 一致性规则
    out/design/FA_screen_applied.png    应用稿：把整套组件画进实际界面（2× = 2160×3840）
    out/design/icons/*.png              音符 icon 单独导出（2×，透明底）

形状语言：切角矩形 + 细亮边 + 角标装饰 + 波纹/触须特效；圆角一律不用。
"""
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "design")
ICONS = os.path.join(OUT, "icons")

W, H = 1080, 1920
Y_JUDGE = H - 400
LANE_CENTERS = [-410, -205, 0, 205, 410]
LANE_HALF = 92
TOP_SCALE = 0.30

# ---------- 1. 颜色风格 ----------
C = {
    "abyss": (4, 22, 28),        # 最深处底色
    "deep": (11, 46, 58),        # 面板底
    "sea": (20, 81, 95),         # 次级面
    "teal": (63, 210, 199),      # 主强调（生物荧光）
    "aqua": (127, 231, 222),     # 高光
    "moon": (232, 246, 245),     # 文本 / 亮线
    "jelly": (242, 166, 206),    # 水母粉：次级强调（连击 / 稀有）
    "coral": (255, 122, 107),    # 警示 / Miss
    "gold": (245, 208, 138),     # Perfect
}
LANE_COLORS = [C["teal"], (86, 196, 220), (129, 166, 240), (168, 214, 240), (86, 214, 190)]


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def rgba(c, a):
    return (c[0], c[1], c[2], a)


def sc(y, top=TOP_SCALE):
    if y >= Y_JUDGE:
        return 1.0
    return top + (1.0 - top) * max(0.0, min(1.0, y / Y_JUDGE))


def fnt(size, cjk=True, bold=False):
    path = r"C:\Windows\Fonts\msyh.ttc"
    if not cjk:
        path = r"C:\Windows\Fonts\consola.ttf"
    elif bold:
        path = r"C:\Windows\Fonts\msyhbd.ttc"
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


# ---------- 2. 组件形状风格：切角矩形 ----------
def cut_rect(d, box, cut=12, fill=None, outline=None, width=2):
    x0, y0, x1, y1 = box
    pts = [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
           (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    if fill:
        d.polygon(pts, fill=fill)
    if outline:
        d.line(pts + [pts[0]], fill=outline, width=width, joint="curve")


def corner_marks(d, box, cut=12, color=(255, 255, 255, 150), length=14, width=2):
    """角标装饰：亮边在四角向外延伸一小段，是这类界面的签名特征。"""
    x0, y0, x1, y1 = box
    for (px, py, dx, dy) in [(x0, y0, -1, -1), (x1, y0, 1, -1), (x0, y1, -1, 1), (x1, y1, 1, 1)]:
        d.line([(px, py), (px + dx * length, py)], fill=color, width=width)
        d.line([(px, py), (px, py + dy * length)], fill=color, width=width)


def glow_layer(size=(W, H)):
    img = Image.new("RGB", size, (0, 0, 0))
    return img, ImageDraw.Draw(img)


def screen(base, glow):
    return ImageChops.screen(base.convert("RGB"), glow).convert("RGBA")


# ---------- 3. 音符 icon ----------
NOTE_SPEC = {          # 名称: (宽, 高, 说明) —— 尺寸取自项目 xlsx
    "tap": (130, 14), "drag": (94, 12), "flick": (130, 14), "arrow": (26, 26),
    "hold_head": (130, 14), "hold_body": (128, 0), "hold_tail": (122, 10),
    "slide_head": (130, 14), "slide_tail": (74, 10), "wide": (670, 14),
}


def note_icon(size, kind, color, scale=1):
    """返回 RGBA 小图。kind 决定装饰形状，颜色由轨道决定。"""
    w, h = size
    pad = 26
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    gl, gd = glow_layer(im.size)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    cut = max(3, min(h // 3, w // 6))

    cut_rect(d, (x0, y0, x1, y1), cut=cut, fill=rgba(color, 235), outline=rgba(C["moon"], 210), width=2)
    gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.55) for v in color))

    cy = (y0 + y1) / 2
    if kind == "tap":
        d.line([(x0 + 6, cy), (x1 - 6, cy)], fill=rgba(C["moon"], 235), width=max(2, h // 5))
    elif kind == "drag":
        r = max(2, h // 3)
        for px in (x0 + 8, x1 - 8):
            d.ellipse([px - r, cy - r, px + r, cy + r], fill=rgba(C["moon"], 220))
    elif kind == "flick":
        d.polygon([(x0 + 10, cy - h / 2), (x0 + w * 0.3, cy), (x0 + 10, cy + h / 2)],
                  fill=rgba(C["moon"], 220))
        d.polygon([(x1 - 10, cy - h / 2), (x1 - w * 0.3, cy), (x1 - 10, cy + h / 2)],
                  fill=rgba(C["moon"], 220))
    elif kind == "arrow_up" or kind == "arrow_down":
        s = 26
        yy = cy
        tri = [(pad + w / 2, yy - s / 2), (pad + w / 2 - s / 2, yy + s / 2), (pad + w / 2 + s / 2, yy + s / 2)]
        if kind == "arrow_down":
            tri = [(pad + w / 2, yy + s / 2), (pad + w / 2 - s / 2, yy - s / 2), (pad + w / 2 + s / 2, yy - s / 2)]
        d.polygon(tri, fill=rgba(C["moon"], 240))
    elif kind == "hold_head":
        for px in (x0 + 14, x0 + 26):
            d.line([(px, y0), (px, y1)], fill=rgba(C["moon"], 230), width=3)
        d.line([(x0 + 40, cy), (x1 - 10, cy)], fill=rgba(C["moon"], 200), width=3)
    elif kind == "hold_body":
        d.rectangle([x0, y1 - h, x1, y1], fill=rgba(color, 120))
        d.line([(x0 + 8, cy), (x1 - 8, cy)], fill=rgba(C["moon"], 90), width=2)
    elif kind == "hold_tail":
        for px in (x1 - 14, x1 - 26):
            d.line([(px, y0), (px, y1)], fill=rgba(C["moon"], 230), width=3)
    elif kind == "slide_head":
        for i in range(3):
            ox = x0 + 18 + i * 12
            d.line([(ox, y1), (ox + 14, y0)], fill=rgba(C["moon"], 220), width=3)
    elif kind == "slide_tail":
        d.polygon([(x0, cy), (x0 + 14, y0), (x1, cy), (x0 + 14, y1)], fill=rgba(C["moon"], 220))
    elif kind == "wide":
        d.polygon([(pad + w / 2, y0 - 6), (pad + w / 2 + 11, cy), (pad + w / 2, y1 + 6), (pad + w / 2 - 11, cy)],
                  fill=rgba(C["moon"], 240))
        d.line([(x0 + 10, cy), (pad + w / 2 - 16, cy)], fill=rgba(C["moon"], 220), width=3)
        d.line([(pad + w / 2 + 16, cy), (x1 - 10, cy)], fill=rgba(C["moon"], 220), width=3)

    gl = gl.filter(ImageFilter.GaussianBlur(10))
    im = screen(im, gl)
    return im


# ---------- 4. 按钮 ----------
BUTTON_ICONS = ("pause", "play", "settings", "retry", "close")


def button_icon(d, box, kind, color, size=96):
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    s = size * 0.34
    if kind == "pause":
        d.rectangle([cx - s * 0.55, cy - s, cx - s * 0.15, cy + s], fill=color)
        d.rectangle([cx + s * 0.15, cy - s, cx + s * 0.55, cy + s], fill=color)
    elif kind == "play":
        d.polygon([(cx - s * 0.5, cy - s), (cx - s * 0.5, cy + s), (cx + s * 0.85, cy)], fill=color)
    elif kind == "settings":
        d.ellipse([cx - s, cy - s, cx + s, cy + s], outline=color, width=4)
        d.ellipse([cx - s * 0.35, cy - s * 0.35, cx + s * 0.35, cy + s * 0.35], fill=color)
        for i in range(6):
            a = math.radians(i * 60)
            d.line([(cx + math.cos(a) * s * 1.05, cy + math.sin(a) * s * 1.05),
                    (cx + math.cos(a) * s * 1.45, cy + math.sin(a) * s * 1.45)], fill=color, width=5)
    elif kind == "retry":
        d.arc([cx - s, cy - s, cx + s, cy + s], start=40, end=330, fill=color, width=5)
        d.polygon([(cx + s * 0.62, cy - s * 0.95), (cx + s * 1.15, cy - s * 0.35), (cx + s * 0.35, cy - s * 0.2)],
                  fill=color)
    elif kind == "close":
        d.line([(cx - s, cy - s), (cx + s, cy + s)], fill=color, width=5)
        d.line([(cx + s, cy - s), (cx - s, cy + s)], fill=color, width=5)


def draw_button(img, center, size, kind, state="normal", accent=None):
    accent = accent or C["teal"]
    gl, gd = glow_layer(img.size)
    d = ImageDraw.Draw(img)
    cx, cy = center
    box = (cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2)
    if state == "disabled":
        cut_rect(d, box, cut=size * 0.16, fill=rgba(C["deep"], 90), outline=rgba(C["moon"], 60), width=2)
        button_icon(d, box, kind, rgba(C["moon"], 70), size)
    elif state == "pressed":
        cut_rect(d, box, cut=size * 0.16, fill=rgba(accent, 90), outline=rgba(C["moon"], 235), width=3)
        gd.rectangle([box[0], box[1], box[2], box[3]], fill=tuple(int(v * 0.5) for v in accent))
        button_icon(d, box, kind, rgba(C["moon"], 255), size)
    else:
        cut_rect(d, box, cut=size * 0.16, fill=rgba(C["deep"], 150), outline=rgba(C["moon"], 170), width=2)
        corner_marks(d, box, color=rgba(accent, 220), length=size * 0.16, width=2)
        button_icon(d, box, kind, rgba(C["moon"], 225), size)
    gl = gl.filter(ImageFilter.GaussianBlur(14))
    return screen(img, gl)


# ---------- 5. 特效 ----------
def hit_ring(img, center, r, color, width=4, alpha=255):
    gl, gd = glow_layer(img.size)
    d = ImageDraw.Draw(img)
    cx, cy = center
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=rgba(color, alpha), width=width)
    gd.ellipse([cx - r, cy - r, cx + r, cy + r], outline=tuple(int(v * 0.8) for v in color), width=max(2, width))
    gl = gl.filter(ImageFilter.GaussianBlur(12))
    return screen(img, gl)


def hit_burst(img, center, r, color, spokes=8):
    gl, gd = glow_layer(img.size)
    d = ImageDraw.Draw(img)
    cx, cy = center
    for i in range(spokes):
        a = math.radians(360 / spokes * i + 12)
        d.line([(cx + math.cos(a) * r * 0.45, cy + math.sin(a) * r * 0.45),
                (cx + math.cos(a) * r, cy + math.sin(a) * r)], fill=rgba(color, 220), width=3)
    gl = gl.filter(ImageFilter.GaussianBlur(10))
    return screen(img, gl)


def miss_slash(img, center, size, color):
    d = ImageDraw.Draw(img)
    cx, cy = center
    d.line([(cx - size, cy - size * 0.5), (cx + size, cy + size * 0.5)], fill=rgba(color, 230), width=6)
    return img


def draw_track(img, with_judge=True):
    gl, gd = glow_layer(img.size)
    ov = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    for i, c in enumerate(LANE_CENTERS):
        col = LANE_COLORS[i]
        x0t, x1t = W / 2 + (c - LANE_HALF) * sc(0), W / 2 + (c + LANE_HALF) * sc(0)
        x0b, x1b = W / 2 + c - LANE_HALF, W / 2 + c + LANE_HALF
        d.polygon([(x0t, 0), (x1t, 0), (x1b, Y_JUDGE), (x0b, Y_JUDGE)], fill=rgba(col, 22))
        gd.line([(x0t, 0), (x0b, Y_JUDGE)], fill=tuple(int(v * 0.5) for v in col), width=5)
        gd.line([(x1t, 0), (x1b, Y_JUDGE)], fill=tuple(int(v * 0.5) for v in col), width=5)
    if with_judge:
        span = LANE_CENTERS[-1] + LANE_HALF
        gd.line([(W / 2 - span, Y_JUDGE), (W / 2 + span, Y_JUDGE)], fill=(215, 255, 250), width=9)
    gl = gl.filter(ImageFilter.GaussianBlur(16))
    img = screen(img, gl)
    return Image.alpha_composite(img, ov)


def draw_keypads(img):
    ov = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    for i, c in enumerate(LANE_CENTERS):
        col = LANE_COLORS[i]
        box = (W / 2 + c - LANE_HALF, Y_JUDGE + 10, W / 2 + c + LANE_HALF, H - 18)
        cut_rect(d, box, cut=14, fill=rgba(col, 26), outline=rgba(col, 150), width=3)
        corner_marks(d, box, color=rgba(C["moon"], 120), length=16, width=2)
    return Image.alpha_composite(img, ov)


def paste_note(img, kind, lane, y, color, hold_px=0):
    if kind == "hold_body":
        ov = Image.new("RGBA", img.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(ov)
        s = sc(y)
        w = 128 * s
        x0 = W / 2 + LANE_CENTERS[lane] * s - w / 2
        d.rectangle([x0, y, x0 + w, y + hold_px * s], fill=rgba(color, 110))
        d.line([(x0 + 8, y), (x0 + 8, y + hold_px * s)], fill=rgba(color, 220), width=4)
        d.line([(x0 + w - 8, y), (x0 + w - 8, y + hold_px * s)], fill=rgba(color, 220), width=4)
        gl, gd = glow_layer(img.size)
        gd.rectangle([x0, y, x0 + w, y + hold_px * s], fill=tuple(int(v * 0.35) for v in color))
        gl = gl.filter(ImageFilter.GaussianBlur(14))
        return Image.alpha_composite(screen(img, gl), ov)

    w, h = NOTE_SPEC[kind]
    s = sc(y)
    icon = note_icon((max(4, int(w * s)), max(4, int(h * s))), kind, color)
    iw, ih = icon.size
    px = int(W / 2 + LANE_CENTERS[lane] * s - iw / 2)
    py = int(y - ih / 2)
    img.alpha_composite(icon, (px, py))
    return img


def composite_bg(bg_path, darken=0.5):
    from PIL import Image as I
    import numpy as np
    bg = I.open(bg_path).convert("RGB").resize((W, H), I.LANCZOS)
    a = np.asarray(bg).astype("float32")
    xs = (np.arange(W) - W / 2) / (W / 2)
    a *= (1.0 - darken * np.exp(-(xs ** 2) / (2 * 0.45 ** 2)))[None, :, None]
    return I.fromarray(a.clip(0, 255).astype("uint8")).convert("RGBA")


# ---------- 6. 界面应用稿 ----------
def render_screen(bg_path, out_path):
    img = composite_bg(bg_path)
    img = draw_track(img)
    img = draw_keypads(img)

    # 音符（含同押、长按）
    img = paste_note(img, "hold_body", 1, 700, LANE_COLORS[1], hold_px=520)
    for lane, y, kind in [(0, 300, "tap"), (3, 420, "drag"), (2, 540, "flick"),
                          (4, 660, "slide_head"), (1, 700, "hold_head"), (1, 1220, "hold_tail"),
                          (3, 900, "slide_tail"), (0, 1080, "tap"), (4, 1260, "tap")]:
        img = paste_note(img, kind, lane, y, LANE_COLORS[lane])
    # 同押连线
    ov = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    y = 1080
    s = sc(y)
    xa = W / 2 + LANE_CENTERS[0] * s
    xb = W / 2 + LANE_CENTERS[4] * s
    band = 9 * s
    d.polygon([(xa, y - band), (xb, y - band), (xb, y + band), (xa, y + band)], fill=rgba(C["teal"], 120))
    img = Image.alpha_composite(img, ov)

    # 判定线上方的触发特效（这是重点：要看得见"设计"）
    hx, hy = W / 2 + LANE_CENTERS[2], Y_JUDGE - 8
    img = hit_ring(img, (hx, hy), 46, C["aqua"], width=5)
    img = hit_ring(img, (hx, hy), 26, C["moon"], width=3, alpha=200)
    img = hit_burst(img, (hx, hy), 62, C["teal"], spokes=8)
    img = hit_ring(img, (W / 2 + LANE_CENTERS[0], Y_JUDGE - 8), 30, LANE_COLORS[0], width=4)
    img = miss_slash(img, (W / 2 + LANE_CENTERS[4], Y_JUDGE - 8), 22, C["coral"])

    # HUD：切角面板 + 角标
    ov = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(ov)
    score_box = (52, 84, 470, 236)
    cut_rect(d, score_box, cut=16, fill=rgba(C["abyss"], 170), outline=rgba(C["moon"], 120), width=2)
    corner_marks(d, score_box, color=rgba(C["teal"], 220), length=18, width=3)
    d.text((76, 104), "SCORE", font=fnt(22, cjk=False), fill=rgba(C["teal"], 255))
    d.text((76, 132), "1284760", font=fnt(56, cjk=False), fill=rgba(C["moon"], 255))
    d.text((76, 196), "ACC 98.42%    P 1024  G 12", font=fnt(20, cjk=False), fill=rgba(C["aqua"], 230))

    combo_box = (452, 560, 628, 704)
    cut_rect(d, combo_box, cut=14, fill=rgba(C["abyss"], 120), outline=rgba(C["jelly"], 190), width=2)
    d.text((540, 604), "312", font=fnt(74, cjk=False, bold=False), fill=rgba(C["jelly"], 255), anchor="mm")
    d.text((540, 672), "COMBO", font=fnt(20, cjk=False), fill=rgba(C["moon"], 200), anchor="mm")

    judge_box = (392, Y_JUDGE - 200, 688, Y_JUDGE - 120)
    cut_rect(d, judge_box, cut=12, fill=rgba(C["abyss"], 120), outline=rgba(C["gold"], 160), width=2)
    d.text((540, Y_JUDGE - 160), "PERFECT", font=fnt(34, cjk=False), fill=rgba(C["gold"], 255), anchor="mm")

    d.rounded_rectangle([100, 44, 980, 54], radius=5, fill=rgba(C["moon"], 55))
    d.rounded_rectangle([100, 44, 452, 54], radius=5, fill=rgba(C["teal"], 235))
    d.text((540, 78), "ARCAEA  01:24 / 02:06", font=fnt(18, cjk=False), fill=rgba(C["aqua"], 200), anchor="mm")
    img = Image.alpha_composite(img, ov)

    img = draw_button(img, (1006, 148), 84, "pause")
    img = draw_button(img, (928, 148), 84, "settings")

    img = img.convert("RGB")
    img = img.resize((W * 2, H * 2), Image.LANCZOS)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    print("WROTE", out_path, img.size)


# ---------- 7. 设计图纸 ----------
def render_sheet(out_path, screen_path=None, part=1):
    """part=1：颜色 / 音符icon / 按钮；part=2：特效 / 一致性规则 / 应用稿。"""
    SW = 2160
    SH = 3840 if part == 1 else 4600
    sheet = Image.new("RGBA", (SW, SH), C["abyss"] + (255,))
    d = ImageDraw.Draw(sheet)

    def title(y, n, text, sub=""):
        d.line([(80, y), (SW - 80, y)], fill=rgba(C["teal"], 160), width=2)
        d.text((80, y + 18), f"{n}", font=fnt(40, cjk=False), fill=rgba(C["teal"], 255))
        d.text((150, y + 22), text, font=fnt(38, bold=True), fill=rgba(C["moon"], 255))
        if sub:
            d.text((150, y + 74), sub, font=fnt(22), fill=rgba(C["aqua"], 200))
        return y + 150

    # 标题
    d.text((80, 70), "FallenAngel · 演奏界面设计规范", font=fnt(64, bold=True), fill=rgba(C["moon"], 255))
    d.text((80, 156), "视觉参考：明日方舟 集成战略#3「水月与深蓝之树」 · 深海蓝绿主调 + 水母粉强调 · 切角矩形形状语言",
           font=fnt(26), fill=rgba(C["aqua"], 220))
    d.text((80, 196), f"PART {part} / 2", font=fnt(22, cjk=False), fill=rgba(C["jelly"], 220))

    if part == 1:
        # ---- 01 颜色 ----
        y = title(260, "01", "颜色风格", "60% 深渊底色 / 30% 蓝绿结构 / 10% 强调色（青绿主、水母粉次、珊瑚红警示）")
        keys = ["abyss", "deep", "sea", "teal", "aqua", "moon", "jelly", "coral", "gold"]
        names = ["深渊底", "面板底", "次级面", "主强调", "高光", "月白", "水母粉", "珊瑚红", "流光金"]
        for i, (k, nm) in enumerate(zip(keys, names)):
            x = 80 + i * 222
            d.rectangle([x, y, x + 190, y + 150], fill=C[k])
            d.text((x, y + 162), nm, font=fnt(24), fill=rgba(C["moon"], 235))
            d.text((x, y + 194), "#%02X%02X%02X" % C[k], font=fnt(20, cjk=False), fill=rgba(C["aqua"], 210))
        y += 270
        d.text((80, y), "五轨色（与项目 xlsx 一致，色盲可辨）", font=fnt(24), fill=rgba(C["aqua"], 220))
        for i, col in enumerate(LANE_COLORS):
            x = 80 + i * 200
            d.rectangle([x, y + 44, x + 168, y + 104], fill=col)
            d.text((x, y + 116), "L%d  #%02X%02X%02X" % (i, col[0], col[1], col[2]),
                   font=fnt(20, cjk=False), fill=rgba(C["moon"], 220))

        # ---- 02 音符 icon ----
        y = title(y + 220, "02", "音符 Icon（靠装饰形状区分，不靠颜色）",
                  "尺寸取自项目规格：宽键 670×14 / 单击 130×14 / 滑过 94×12 / 箭头 26×26")
        items = [("tap", "单击 130×14", 0), ("drag", "滑过 94×12", 1), ("flick", "轻扫 130×14", 2),
                 ("arrow_up", "上箭头 26×26", 3), ("arrow_down", "下箭头 26×26", 0),
                 ("hold_head", "长按头 130×14", 1), ("hold_tail", "长按尾 122×10", 2),
                 ("slide_head", "slide 头 130×14", 3), ("slide_tail", "slide 尾 74×10", 4)]
        for i, (kind, label, lane) in enumerate(items):
            cx = 80 + (i % 3) * 660
            cyy = y + (i // 3) * 190
            if kind.startswith("arrow"):
                size = (74, 12)
            elif kind == "drag":
                size = (94, 12)
            elif kind == "hold_tail":
                size = (122, 10)
            elif kind == "slide_tail":
                size = (74, 10)
            else:
                size = (130, 14)
            ic = note_icon(size, kind, LANE_COLORS[lane])
            sheet.paste(ic, (cx, cyy), ic)
            d.text((cx, cyy + 100), label, font=fnt(22), fill=rgba(C["moon"], 230))

        cyy = y + 3 * 190
        ic = note_icon((670, 14), "wide", LANE_COLORS[2])
        sheet.paste(ic, (80, cyy), ic)
        d.text((80, cyy + 100), "宽键（kick）670×14：横跨全轨，中央菱形 + 两端断线", font=fnt(22), fill=rgba(C["moon"], 230))

        cyy += 210
        ic = note_icon((128, 220), "hold_body", LANE_COLORS[1])
        sheet.paste(ic, (80, cyy), ic)
        d.text((80, cyy + 300), "长按连接段 128×可变：两侧细线 + 低饱和填充，可平铺且接缝无缝",
                font=fnt(22), fill=rgba(C["moon"], 230))

        # ---- 03 按钮 ----
        y = title(cyy + 400, "03", "按钮设计（切角矩形 + 角标 + 三态）",
                  "常态：深底 + 月白细边 + 角标；按下：青绿填充 + 亮边；禁用：整体降透明")
        labels = [("pause", "暂停"), ("play", "继续"), ("settings", "设置"), ("retry", "重开"), ("close", "退出")]
        for i, (kind, label) in enumerate(labels):
            cx = 210 + i * 385
            for j, (st, stname) in enumerate([("normal", "常态"), ("pressed", "按下"), ("disabled", "禁用")]):
                tile = Image.new("RGBA", (200, 200), rgba(C["abyss"], 255))
                tile = draw_button(tile, (100, 100), 110, kind, state=st)
                sheet.paste(tile.convert("RGB"), (cx - 90, y + j * 190))
                if i == 0:
                    d.text((60, y + j * 190 + 90), stname, font=fnt(22), fill=rgba(C["aqua"], 210))
            d.text((cx - 62, y + 600), label, font=fnt(24), fill=rgba(C["moon"], 235))
        d.text((80, y + 670), "按钮尺寸 96×96（拇指可点 ≥ 88px）；切角 = 边长的 1/6；角标颜色随功能变化",
               font=fnt(24), fill=rgba(C["moon"], 220))
    else:
        # ---- 04 特效 ----
        y = title(260, "04", "触发特效（命中 / 连击 / 未命中）",
                  "命中：双环扩散 + 八向触须；连击：水母粉脉冲；Miss：珊瑚红斜切 + 轨道压暗")
        for i in range(5):
            r = 18 + i * 16
            tile = Image.new("RGBA", (280, 300), rgba(C["abyss"], 255))
            tile = hit_ring(tile, (140, 170), r, C["aqua"], width=max(2, 6 - i))
            tile = hit_burst(tile, (140, 170), r + 16, C["teal"], spokes=8)
            sheet.paste(tile.convert("RGB"), (80 + i * 400, y))
            d.text((80 + i * 400 + 120, y + 314), f"F{i+1}", font=fnt(24, cjk=False), fill=rgba(C["aqua"], 220))
        y += 380
        d.text((80, y), "命中扩散环：半径 18→82，描边 6→2，亮度同步衰减（5 帧 @60fps ≈ 83ms）",
               font=fnt(24), fill=rgba(C["moon"], 225))

        y += 90
        specs = [("连击脉冲（水母粉）", lambda t: hit_ring(t, (140, 150), 62, C["jelly"], width=6)),
                 ("未命中斜切（珊瑚红）", lambda t: miss_slash(t, (140, 150), 62, C["coral"])),
                 ("轨道压暗（Miss 反馈）", None)]
        for i, (label, fn) in enumerate(specs):
            x = 80 + i * 400
            tile = Image.new("RGBA", (280, 300), rgba(C["abyss"], 255))
            if fn is not None:
                tile = fn(tile)
            else:
                dd = ImageDraw.Draw(tile)
                dd.rectangle([70, 90, 210, 210], fill=rgba(C["deep"], 255))
                dd.rectangle([70, 90, 210, 210], outline=rgba(C["coral"], 200), width=3)
            sheet.paste(tile.convert("RGB"), (x, y))
            d.text((x + 20, y + 314), label, font=fnt(24), fill=rgba(C["moon"], 230))

        # ---- 05 一致性 ----
        y = title(y + 420, "05", "界面一致性规则", "所有组件共用同一套数值，禁止单独调圆角 / 描边 / 发光")
        rules = [
            "形状：一律切角矩形，切角 12px（按钮为边长的 1/6），全局不使用圆角",
            "描边：统一 2px，强调态 3px；不用投影，靠内发光表达层级",
            "发光：高斯 10–16px，颜色取自组件主色，禁止白色泛光",
            "网格：8px 基准；面板内边距 24px；组件间距 16px",
            "文字：中文微软雅黑 / 思源，数字用等宽；主数值 56px，标签 22px",
            "色彩配比：深渊底 60% / 蓝绿结构 30% / 强调色 10%",
            "强调色同屏只用一处：不得同时让青绿与珊瑚红占据主视觉",
            "音符：形状区分类型、轨道色区分位置；任何情况下文字不叠在音符上",
        ]
        for i, r in enumerate(rules):
            d.text((100, y + i * 58), "·  " + r, font=fnt(27), fill=rgba(C["moon"], 235))

        # ---- 应用稿 ----
        if screen_path and os.path.exists(screen_path):
            yy = y + len(rules) * 58 + 60
            tw = 760
            thumb = Image.open(screen_path).convert("RGB").resize((tw, int(tw * H / W)), Image.LANCZOS)
            sheet.paste(thumb, (100, yy))
            d.text((100 + tw + 60, yy + 20), "应用稿：全部组件在同一屏内的关系", font=fnt(30, bold=True), fill=rgba(C["moon"], 255))
            notes = ["· 五轨透视轨道 + 判定线", "· 音符 icon 家族全部出现", "· 同押连线 / 长按连接段",
                     "· 判定线命中特效（双环 + 触须）", "· Miss 斜切与轨道压暗", "· HUD 切角面板 + 角标",
                     "· 右上暂停 / 设置按钮", "· 顶部进度条与曲目信息"]
            for i, t in enumerate(notes):
                d.text((100 + tw + 60, yy + 80 + i * 46), t, font=fnt(26), fill=rgba(C["aqua"], 225))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    sheet.convert("RGB").save(out_path)
    print("WROTE", out_path, sheet.size)


def export_icons():
    os.makedirs(ICONS, exist_ok=True)
    kinds = [("tap", (130, 14)), ("drag", (94, 12)), ("flick", (130, 14)),
             ("arrow_up", (26, 26)), ("arrow_down", (26, 26)),
             ("hold_head", (130, 14)), ("hold_tail", (122, 10)),
             ("slide_head", (130, 14)), ("slide_tail", (74, 10)), ("wide", (670, 14))]
    for i, (k, size) in enumerate(kinds):
        ic = note_icon(size, k, LANE_COLORS[i % 5])
        ic = ic.resize((ic.size[0] * 2, ic.size[1] * 2), Image.LANCZOS)
        p = os.path.join(ICONS, f"note_{k}@2x.png")
        ic.save(p)
        print("WROTE", p, ic.size)


def main():
    screen_path = os.path.join(OUT, "FA_screen_applied.png")
    bg = os.path.join(HERE, "out", "dreamshaper8", "FA_ds8_V3_00001_.png")
    if os.path.exists(bg):
        render_screen(bg, screen_path)
    render_sheet(os.path.join(OUT, "FA_ui_design_sheet_A.png"), None, part=1)
    render_sheet(os.path.join(OUT, "FA_ui_design_sheet_B.png"), screen_path, part=2)
    export_icons()


if __name__ == "__main__":
    main()
