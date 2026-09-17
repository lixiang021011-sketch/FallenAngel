#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""音符家族设计稿 v2（供过稿，不进游戏）。

相对 v1 的三处改动（按作者反馈）：
  ① 厚度 ×2：tap/flick/hold头/slide头/宽键 14→28，drag 12→24，hold尾/slide尾 10→20
  ② 类型区分提到**轮廓级**——不再只靠内部装饰：
       drag  中间断开成两截（镂空槽）        flick 上下侧翼（三角凸起）
       hold头/尾 单侧齿状                    slide头 前缘非对称斜切
       slide尾 箭头尖                        wide  中央凸起块 + 分段刻度
  ③ 附命中 / Miss 特效预览，并明确**一致性规则**：特效形态只随判定等级变化，
     不随音符类型变化；Miss 表现所有类型完全共用一套。

用法：python -X utf8 art_tools/note_design_v2.py [--scale 1.5]
"""
import argparse
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "ai_imagegen", "out", "design_v2")

# 两版配色：形状与布局完全一致，只有颜色不同（稿子底色也固定，便于逐像素比对）
PALETTES = {
    "teal": {
        "name": "A · 青绿（现行方向）",
        "desc": "月白青 → 青 → 青蓝 → 深青 → 最深青",
        "ladder": [(0xCF, 0xEF, 0xEA), (0xA8, 0xDE, 0xDA), (0x8A, 0xC9, 0xC6),
                   (0x6F, 0xB0, 0xB0), (0x56, 0x94, 0x94)],
        "accent": (0xF0, 0xA8, 0xCC),
    },
    "indigo": {
        "name": "B · 靛青（★ 已选定）",
        "desc": "冰白 → 天青 → 湖蓝 → 靛蓝 → 深靛",
        "ladder": [(0xE4, 0xF4, 0xF7), (0xA9, 0xD9, 0xE8), (0x7F, 0xA8, 0xDC),
                   (0x6E, 0x7F, 0xC0), (0x5B, 0x5B, 0xA6)],
        "accent": (0xF0, 0xA8, 0xCC),
    },
    "yellow": {
        "name": "C · 金黄（暖色系）",
        "desc": "乳白 → 浅金 → 金 → 琥珀 → 深琥珀（暖底冷强调）",
        "ladder": [(0xFF, 0xFD, 0xF2), (0xFF, 0xE9, 0xA0), (0xFF, 0xD1, 0x66),
                   (0xE0, 0xA9, 0x3F), (0xA8, 0x7C, 0x22)],
        "accent": (0x6F, 0xE0, 0xD6),     # 冷色强调：暖底上做正向提示（连击/奖励）
    },
}

palette_key = "teal"
LADDER = PALETTES["teal"]["ladder"]     # 由 main() 按 --palette 覆盖
ACCENT = PALETTES["teal"]["accent"]
PALETTE_NAME = PALETTES["teal"]["name"]
PALETTE_DESC = PALETTES["teal"]["desc"]
INK = (0xE8, 0xF6, 0xF5)
CORE = (0xFF, 0xFF, 0xFF)
BG = (0x0F, 0x20, 0x2E)
BG2 = (0x1B, 0x32, 0x3B)
MUTED = (0x7F, 0x96, 0x9B)
LANE_BG = (0x12, 0x2A, 0x33)      # 轨道柱底（Miss 预览用）

# 厚度翻倍后的规格
SPEC = {
    "tap": (130, 28), "drag": (94, 24), "flick": (130, 28), "arrow": (26, 26),
    "hold_head": (130, 28), "hold_tail": (122, 20),
    "slide_head": (130, 28), "slide_tail": (74, 20), "wide": (670, 28),
}


def font(size, bold=False):
    path = r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc"
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


def chamfer(d, box, cut, fill=None, outline=None, width=1):
    x0, y0, x1, y1 = box
    pts = [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
           (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    if fill:
        d.polygon(pts, fill=fill)
    if outline:
        d.line(pts + [pts[0]], fill=outline, width=width, joint="curve")


def glare(im):
    """统一的辉光：把一层亮色做高斯后 screen 上去（与游戏内 HitRing 同一做法）。"""
    gl = Image.new("RGB", im.size, (0, 0, 0))
    gd = ImageDraw.Draw(gl)
    return gl, gd


def finish(im, gl, radius=9):
    gl = gl.filter(ImageFilter.GaussianBlur(radius))
    return ImageChops.screen(im.convert("RGB"), gl).convert("RGBA")


def note_shape(size, color, kind, alpha=245):
    """按类型画音符；轮廓级差异在加厚后应能一眼分辨。"""
    w, h = size
    pad = 34
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    gl, gd = glare(im)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    cy = (y0 + y1) / 2
    cut = max(4, int(h * 0.28))
    edge = INK + (205,)

    if kind == "drag":
        # ① 轮廓级：中间断开成两截 —— "穿过去"，与 tap 的实心条完全不同
        gap = max(10, int(w * 0.16))
        seg = (w - gap) / 2
        for sx in (x0, x0 + seg + gap):
            chamfer(d, (sx, y0, sx + seg, y1), cut, fill=color + (alpha,), outline=edge, width=2)
            gd.rectangle([sx, y0, sx + seg, y1], fill=tuple(int(v * 0.5) for v in color))
        for sx in (x0 + 7, x1 - 7):   # 两截中心各一个亮点
            r = max(2, int(h * 0.16))
            d.ellipse([sx - r, cy - r, sx + r, cy + r], fill=CORE + (200,))
        return finish(im, gl)

    if kind == "flick":
        # ② 轮廓级：上下侧翼 —— "甩出去"的动势
        wing = int(h * 0.55)
        d.polygon([(x0 + 12, y0), (x0 + 12 + wing, y0 - wing), (x0 + 12 + wing * 2, y0)],
                  fill=color + (alpha,), outline=edge)
        d.polygon([(x1 - 12 - wing * 2, y1), (x1 - 12 - wing, y1 + wing), (x1 - 12, y1)],
                  fill=color + (alpha,), outline=edge)
        chamfer(d, (x0, y0, x1, y1), cut, fill=color + (alpha,), outline=edge, width=2)
        gd.polygon([(x0 + 12, y0), (x0 + 12 + wing, y0 - wing), (x0 + 12 + wing * 2, y0)],
                   fill=tuple(int(v * 0.5) for v in color))
        gd.polygon([(x1 - 12 - wing * 2, y1), (x1 - 12 - wing, y1 + wing), (x1 - 12, y1)],
                   fill=tuple(int(v * 0.5) for v in color))
        d.line([(x0 + 10, cy), (x1 - 10, cy)], fill=CORE + (150,), width=2)
        # 轻扫方向：中央雪佛龙
        cx = (x0 + x1) / 2
        d.line([(cx - 8, cy + 5), (cx, cy - 5), (cx + 8, cy + 5)], fill=CORE + (235,), width=3)
        return finish(im, gl)

    if kind == "hold_head":
        # ③ 轮廓级：尾侧齿状 —— "按住从这里开始"
        teeth = 3
        d.rectangle([x0, y0, x0 + 10, y1], fill=color + (alpha,), outline=edge)
        for i in range(teeth):
            ty = y0 + (i + 0.5) * h / teeth
            d.line([(x0 + 2, ty), (x0 + 9, ty)], fill=INK + (240,), width=3)
        chamfer(d, (x0 + 10, y0, x1, y1), cut, fill=color + (alpha,), outline=edge, width=2)
        gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))
        d.line([(x0 + 16, cy), (x1 - 8, cy)], fill=CORE + (150,), width=2)
        return finish(im, gl)

    if kind == "hold_tail":
        teeth = 3
        d.rectangle([x1 - 10, y0, x1, y1], fill=color + (alpha,), outline=edge)
        for i in range(teeth):
            ty = y0 + (i + 0.5) * h / teeth
            d.line([(x1 - 9, ty), (x1 - 2, ty)], fill=INK + (240,), width=3)
        chamfer(d, (x0, y0, x1 - 10, y1), cut, fill=color + (alpha,), outline=edge, width=2)
        gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))
        return finish(im, gl)

    if kind == "slide_head":
        # ④ 轮廓级：前缘（右侧）非对称深斜切 + 斜纹
        pts = [(x0 + cut, y0), (x1, y0 + int(h * 0.5)), (x0 + cut, y1),
               (x0, y1 - cut), (x0, y0 + cut)]
        d.polygon(pts, fill=color + (alpha,), outline=edge)
        gd.polygon(pts, fill=tuple(int(v * 0.5) for v in color))
        for i in range(3):
            ox = x0 + 12 + i * 10
            d.line([(ox, y1 - 3), (ox + 8, y0 + 3)], fill=INK + (225,), width=3)
        return finish(im, gl)

    if kind == "slide_tail":
        # ⑤ 轮廓级：箭头尖
        d.polygon([(x0, y0), (x1 - 14, y0), (x1, cy), (x1 - 14, y1), (x0, y1)],
                  fill=color + (alpha,), outline=edge)
        gd.polygon([(x0, y0), (x1 - 14, y0), (x1, cy), (x1 - 14, y1), (x0, y1)],
                   fill=tuple(int(v * 0.5) for v in color))
        return finish(im, gl)

    if kind == "wide":
        # ⑥ 轮廓级：中央凸起块 + 分段刻度
        d.rectangle([x0, y0, x1, y1], fill=color + (alpha,), outline=edge, width=2)
        cx = (x0 + x1) / 2
        bw, bh = 34, int(h * 0.42)
        d.rectangle([cx - bw, cy - bh, cx + bw, cy + bh], fill=color + (alpha,), outline=INK + (235,), width=2)
        for i in range(1, 8):
            px = x0 + (x1 - x0) * i / 8
            d.line([(px, y0 + 2), (px, y1 - 2)], fill=INK + (95,), width=2)
        gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))
        return finish(im, gl)

    # tap（基准）
    chamfer(d, (x0, y0, x1, y1), cut, fill=color + (alpha,), outline=edge, width=2)
    gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))
    d.rectangle([x0 + 6, cy - 3, x0 + 10, cy + 3], fill=INK + (255,))
    d.rectangle([x1 - 10, cy - 3, x1 - 6, cy + 3], fill=INK + (255,))
    d.line([(x0 + 16, cy), (x1 - 16, cy)], fill=CORE + (170,), width=2)
    return finish(im, gl)


def hold_body(size, color, segments=5):
    w, h = size
    pad = 12
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    d.rectangle([x0, y0, x1, y1], fill=color + (110,))
    d.line([(x0 + 1, y0), (x0 + 1, y1)], fill=color + (240,), width=3)
    d.line([(x1 - 1, y0), (x1 - 1, y1)], fill=color + (240,), width=3)
    for i in range(1, segments):
        py = y0 + h * i / segments
        d.line([(x0 + 3, py), (x1 - 3, py)], fill=INK + (60,), width=2)
    return im


def arrow_glyph(size, color, up=True):
    w, h = size
    pad = 22
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    gl, gd = glare(im)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    chamfer(d, (x0, y0, x1, y1), 7, fill=color + (225,), outline=INK + (200,), width=2)
    gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    s = 8
    pts = [(cx, cy - s), (cx - s, cy + s), (cx + s, cy + s)] if up else \
          [(cx, cy + s), (cx - s, cy - s), (cx + s, cy - s)]
    d.polygon(pts, fill=INK + (245,))
    return finish(im, gl)


# ---------- 特效 ----------

def draw_ring(im, cx, cy, r, color, width, alpha):
    d = ImageDraw.Draw(im)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color + (alpha,), width=width)


def draw_burst(im, cx, cy, r, color, spokes, alpha, inner=0.35):
    d = ImageDraw.Draw(im)
    for i in range(spokes):
        a = math.tau * i / spokes
        x0 = cx + math.cos(a) * r * inner
        y0 = cy + math.sin(a) * r * inner
        x1 = cx + math.cos(a) * r
        y1 = cy + math.sin(a) * r
        d.line([(x0, y0), (x1, y1)], fill=color + (alpha,), width=max(2, int(r * 0.06)))


def hit_frame(size, color, kind, t01):
    """命中特效的一帧：音符收束淡出 + 扩散环 + 放射条。t01 = 0..1"""
    w, h = size
    box = max(w, h) + 220
    im = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    note = note_shape(size, color, kind, alpha=int(245 * (1 - t01)))
    nw, nh = note.size
    im.paste(note, (int((box - nw) / 2), int((box - nh) / 2)), note)
    cx = cy = box / 2
    r = (max(w, h) * 0.45) + t01 * 120
    draw_ring(im, cx, cy, r, color, max(2, int(7 * (1 - t01))), int(210 * (1 - t01)))
    draw_ring(im, cx, cy, r * 0.62, INK, 2, int(150 * (1 - t01)))
    draw_burst(im, cx, cy, r * 1.15, color, 8, int(150 * (1 - t01)))
    return im


def miss_frame(size, color, kind):
    """Miss：所有音符类型完全同一套处理——去饱和 + 压暗 + 轨道暗化。"""
    w, h = size
    box = max(w, h) + 220
    im = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    # 轨道柱压暗
    d = ImageDraw.Draw(im)
    d.rectangle([box * 0.30, 0, box * 0.70, box], fill=LANE_BG + (255,))
    # 音符去饱和 + 压暗（灰化程度固定，与类型无关）
    lum = 0.2126 * color[0] + 0.7152 * color[1] + 0.0722 * color[2]
    grey = (int(lum * 0.55), int(lum * 0.58), int(lum * 0.60))
    note = note_shape(size, grey, kind, alpha=130)
    nw, nh = note.size
    im.paste(note, (int((box - nw) / 2), int((box - nh) / 2)), note)
    # 统一的"未命中"标记：一条去饱和的斜切线（所有类型同一条）
    d.line([(box * 0.32, box * 0.42), (box * 0.68, box * 0.58)], fill=(0x8A, 0x92, 0x98, 170), width=4)
    return im


def sheet(scale):
    W, H = int(1500 * scale), int(2350 * scale)
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)

    def t(xy, s, size=24, color=INK, bold=False):
        d.text((xy[0] * scale, xy[1] * scale), s, font=font(int(size * scale), bold), fill=color)

    t((40, 30), "FallenAngel · 音符家族设计稿 v2 — " + PALETTE_NAME, 40, INK, True)
    t((40, 88), "厚度 ×2 · 轮廓级类型区分 · 命中/Miss 特效 · " + PALETTE_DESC, 22, MUTED)

    for i, c in enumerate(LADDER):
        x = 40 + i * 150
        d.rectangle([x * scale, 132 * scale, (x + 130) * scale, (132 + 44) * scale], fill=c)
        t((x, 184), "L%d" % i, 19, MUTED)

    y = 250
    t((40, y), "① 基础族（厚度 28 / 24）——轮廓级差异", 26, INK, True)
    y += 60
    for kind, label, lane in [("tap", "tap 130×28：实心切角 + 两端方点", 0),
                              ("drag", "drag 94×24：中间断开两截（镂空槽）= 穿过去", 1),
                              ("flick", "flick 130×28：上下侧翼 + 中央雪佛龙 = 甩出去", 2)]:
        ic = note_shape(SPEC[kind], LADDER[lane], kind)
        im.paste(ic, (int(40 * scale), int((y - 30) * scale)), ic)
        t((40, y + 60), label, 21, MUTED)
        y += 145

    for i, up in enumerate([True, False]):
        x = 700 + i * 240
        gl = arrow_glyph(SPEC["arrow"], LADDER[3], up)
        im.paste(gl, (int(x * scale), int((y - 250) * scale)), gl)
        t((x, y - 140), "flick 方向箭头 26×26（%s）" % ("↑ 上扫" if up else "↓ 下扫"), 19, MUTED)

    y += 10
    t((40, y), "② 长按 / slide / 宽键", 26, INK, True)
    y += 60
    hh = note_shape(SPEC["hold_head"], LADDER[1], "hold_head")
    im.paste(hh, (int(40 * scale), int((y - 30) * scale)), hh)
    t((40, y + 60), "hold head 130×28：尾侧齿状（3 齿）", 21, MUTED)
    body = hold_body((128, 300), LADDER[1])
    im.paste(body, (int(430 * scale), int((y - 40) * scale)), body)
    t((430, y + 286), "hold body 128×可变：两侧亮边 + 分段块（可平铺）", 20, MUTED)
    ht = note_shape(SPEC["hold_tail"], LADDER[1], "hold_tail")
    im.paste(ht, (int(790 * scale), int((y + 20) * scale)), ht)
    t((790, y + 60), "hold tail 122×20：齿状", 21, MUTED)
    y += 380

    sh = note_shape(SPEC["slide_head"], LADDER[3], "slide_head")
    im.paste(sh, (int(40 * scale), int((y - 30) * scale)), sh)
    t((40, y + 60), "slide head 130×28：前缘非对称斜切 + 斜纹", 21, MUTED)
    st = note_shape(SPEC["slide_tail"], LADDER[3], "slide_tail")
    im.paste(st, (int(430 * scale), int((y + 5) * scale)), st)
    t((430, y + 60), "slide tail 74×20：箭头尖", 21, MUTED)
    ribbon = hold_body((360, 84), LADDER[3], segments=6)
    im.paste(ribbon, (int(700 * scale), int((y - 14) * scale)), ribbon)
    t((700, y + 86), "slide 轨迹带：按住时逐段收起", 20, MUTED)
    y += 140

    wide = note_shape(SPEC["wide"], LADDER[2], "wide")
    im.paste(wide, (int(40 * scale), int((y - 30) * scale)), wide)
    t((40, y + 60), "wide 670×28：中央凸起块 + 八段刻度", 21, MUTED)
    y += 150

    # ③ 命中特效预览
    t((40, y), "③ 命中特效（形态只随判定等级变化，不随音符类型变化）", 26, INK, True)
    y += 30
    t((40, y + 30), "同一瞬间起算的三帧：0ms → 80ms → 160ms；环径与放射条长度是唯一的变量", 20, MUTED)
    y += 70
    for i, name in enumerate(["tap", "flick", "hold_head"]):
        base = note_shape(SPEC[name], LADDER[2], name)
        bw = base.size[0]
        im.paste(base, (int(40 * scale), int((y + i * 150 + 40) * scale)), base)
        t((60 + int(bw / 1.0) + 20, y + i * 150 + 70), name, 20, MUTED)
    for j, t01 in enumerate([0.0, 0.45, 0.9]):
        fx = hit_frame(SPEC["tap"], LADDER[2], "tap", t01)
        px = 560 + j * 320
        im.paste(fx, (int(px * scale), int((y - 40) * scale)), fx)
        t((px + 60, y + 290), "命中 +%dms" % (0 if j == 0 else int(80 * j / 0.45 * 0.45)), 20, MUTED)
    y += 380

    # ④ Miss 特效预览（一致性）
    t((40, y), "④ Miss 特效（一致性：所有音符类型共用同一套表现）", 26, INK, True)
    y += 34
    t((40, y + 30), "规则：去饱和 + 压暗 + 轨道柱暗化 + 一条斜切未命中标记。不区分类型、不使用暖色、持续 ≤0.3s",
      20, ACCENT)
    y += 80
    kinds = [("tap", "tap"), ("drag", "drag"), ("flick", "flick"), ("hold_head", "hold"), ("slide_head", "slide")]
    tile = 300
    for i, (kind, label) in enumerate(kinds):
        fx = miss_frame(SPEC[kind], LADDER[2], kind)
        px = 40 + i * (tile + 20)
        im.paste(fx, (int(px * scale), int(y * scale)), fx)
        t((px + 90, y + 300), label, 20, MUTED)
    t((40, y + 340), "↑ 五种音符的 Miss 表现逐像素同源（同一个 miss_frame 函数），仅因本体形状不同而自然不同",
      20, MUTED)
    y += 420

    t((40, y), "⑤ 五轨同形对比：同形状、不同轨色（同色系，只差明度）", 26, INK, True)
    y += 60
    for i, c in enumerate(LADDER):
        ic = note_shape(SPEC["tap"], c, "tap")
        im.paste(ic, (int((40 + i * 280) * scale), int((y - 30) * scale)), ic)
        t((40 + i * 280, y + 60), "lane %d" % i, 19, MUTED)

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "note_family_v2_%s.png" % palette_key)
    im.save(path)
    print("WROTE", path, im.size)


def main():
    global LADDER, ACCENT, PALETTE_NAME, PALETTE_DESC, palette_key
    ap = argparse.ArgumentParser()
    ap.add_argument("--scale", type=float, default=1.4)
    ap.add_argument("--palette", choices=sorted(PALETTES), default="teal")
    args = ap.parse_args()
    palette_key = args.palette
    p = PALETTES[args.palette]
    LADDER, ACCENT = p["ladder"], p["accent"]
    PALETTE_NAME, PALETTE_DESC = p["name"], p["desc"]
    sheet(args.scale)


if __name__ == "__main__":
    main()
