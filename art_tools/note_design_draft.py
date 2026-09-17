#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""音符家族组件设计稿（第一版，供过稿；不进游戏）。

设计语言（四条，与已定美术方向一致）：
    ① 切角矩形——不用圆角，和界面组件共用同一套形状语言
    ② 亮芯线——沿中线的淡青白细线，穿过所有音符，形成家族统一感
    ③ 端部缺口——两端各留 2px 内收，让音符看起来"嵌在轨道里"
    ④ 靠装饰形状区分类型——不靠色相（五轨同色系，只差明度）

配色取 Core/LaneColors 的青绿明度阶梯 + 唯一暖色粉（#F0A8CC）。
尺寸取项目规格（gameplay美术需求与工作计划.xlsx）。

用法：python -X utf8 art_tools/note_design_draft.py [--scale 2]
"""
import argparse
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "ai_imagegen", "out", "design_v2")

# 五轨青绿明度阶梯（与 Core/LaneColors 同值）
LADDER = [(0xCF, 0xEF, 0xEA), (0xA8, 0xDE, 0xDA), (0x8A, 0xC9, 0xC6),
          (0x6F, 0xB0, 0xB0), (0x56, 0x94, 0x94)]
INK = (0xE8, 0xF6, 0xF5)        # 月白
CORE = (0xFF, 0xFF, 0xFF)       # 芯线
ACCENT = (0xF0, 0xA8, 0xCC)     # 唯一暖色（连击/奖励；这里只做标注用）
BG = (0x0F, 0x20, 0x2E)         # 方向最深
BG2 = (0x1B, 0x32, 0x3B)        # 卡片
MUTED = (0x7F, 0x96, 0x9B)

SPEC = {          # 名称: (宽, 高) —— 取自项目 xlsx
    "tap": (130, 14), "drag": (94, 12), "flick": (130, 14), "arrow": (26, 26),
    "hold_head": (130, 14), "hold_tail": (122, 10),
    "slide_head": (130, 14), "slide_tail": (74, 10), "wide": (670, 14),
}


def font(size, bold=False):
    path = r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc"
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


def chamfer(d, box, cut, fill=None, outline=None, width=1):
    """切角矩形：四角各切掉 cut。"""
    x0, y0, x1, y1 = box
    pts = [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
           (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    if fill:
        d.polygon(pts, fill=fill)
    if outline:
        d.line(pts + [pts[0]], fill=outline, width=width, joint="curve")


def notch(d, x, y0, y1, color, w=2):
    """端部缺口：一条内收的短竖线，让音符"嵌"进轨道。"""
    d.line([(x, y0), (x, y1)], fill=color, width=w)


def note_body(size, color, kind, core=True):
    """按类型画一个音符（返回 RGBA 小图，尺寸 = size + 边距）。"""
    w, h = size
    pad = 18
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    gl, gd = glow_layer(im.size)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    cy = (y0 + y1) / 2
    cut = max(2, int(h * 0.30))

    # 主体：切角 + 亮边
    chamfer(d, (x0, y0, x1, y1), cut, fill=color + (240,), outline=INK + (200,), width=1)
    gd.rectangle([x0, y0, x1, y1], fill=tuple(int(v * 0.5) for v in color))

    # ① 亮芯线：家族统一的长线（drag 例外：中间镂空）
    if kind == "drag":
        r = max(2, int(h * 0.34))
        for px in (x0 + 10, x1 - 10):
            d.ellipse([px - r, cy - r, px + r, cy + r], fill=INK + (230,))
    elif core:
        d.line([(x0 + 5, cy), (x1 - 5, cy)], fill=CORE + (150,), width=max(1, int(h * 0.16)))

    # ② 类型装饰
    if kind == "tap":
        d.rectangle([x0 + 4, cy - 1, x0 + 6, cy + 1], fill=INK + (255,))
        d.rectangle([x1 - 6, cy - 1, x1 - 4, cy + 1], fill=INK + (255,))
    elif kind == "flick":
        for s in (-1, 1):   # 两侧三角缺口提示"甩"
            px = x0 + 8 if s < 0 else x1 - 8
            d.polygon([(px - 4 * s, y0), (px, cy), (px - 4 * s, y1)], fill=INK + (210,))
    elif kind == "hold_head":
        notch(d, x0 + 5, y0 + 2, y1 - 2, INK + (235,), 2)   # 尾侧双刻度
        notch(d, x0 + 9, y0 + 2, y1 - 2, INK + (235,), 2)
    elif kind == "hold_tail":
        notch(d, x1 - 5, y0 + 2, y1 - 2, INK + (235,), 2)
        notch(d, x1 - 9, y0 + 2, y1 - 2, INK + (235,), 2)
    elif kind == "slide_head":
        for i in range(3):   # 斜纹 = 路径起点
            ox = x0 + 10 + i * 9
            d.line([(ox, y1 - 2), (ox + 7, y0 + 2)], fill=INK + (215,), width=2)
    elif kind == "slide_tail":
        d.polygon([(x0, cy), (x0 + 10, y0), (x1, cy), (x0 + 10, y1)], fill=INK + (225,))
    elif kind == "wide":
        cx = (x0 + x1) / 2
        d.polygon([(cx, y0 - 2), (cx + 8, cy), (cx, y1 + 2), (cx - 8, cy)], fill=INK + (240,))
        for i in range(1, 6):   # 分段刻度：宽键=全轨
            px = x0 + (x1 - x0) * i / 6
            d.line([(px, y0 + 1), (px, y1 - 1)], fill=INK + (90,), width=1)

    gl = gl.filter(ImageFilter.GaussianBlur(8))
    from PIL import ImageChops
    im = ImageChops.screen(im.convert("RGB"), gl).convert("RGBA")
    return im


def glow_layer(size):
    img = Image.new("RGB", size, (0, 0, 0))
    return img, ImageDraw.Draw(img)


def hold_body(size, color, segments=4):
    w, h = size
    pad = 10
    im = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    d.rectangle([x0, y0, x1, y1], fill=color + (105,))
    d.line([(x0 + 1, y0), (x0 + 1, y1)], fill=color + (235,), width=2)   # 两侧亮边
    d.line([(x1 - 1, y0), (x1 - 1, y1)], fill=color + (235,), width=2)
    for i in range(1, segments):    # 分段块：可平铺
        py = y0 + h * i / segments
        d.line([(x0 + 2, py), (x1 - 2, py)], fill=INK + (55,), width=1)
    return im


def sheet(scale):
    W, H = int(1500 * scale), int(1780 * scale)
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im)

    def t(xy, s, size=24, color=INK, bold=False, anchor="la"):
        d.text((xy[0] * scale, xy[1] * scale), s, font=font(int(size * scale), bold), fill=color, anchor=anchor)

    t((40, 34), "FallenAngel · 音符家族设计稿 v1", 40, INK, True)
    t((40, 92), "切角矩形 + 亮芯线 + 端部缺口；类型靠装饰形状区分，不靠色相", 22, MUTED)

    # 色板
    t((40, 140), "五轨明度阶梯（不靠色相区分）", 22, MUTED)
    for i, c in enumerate(LADDER):
        x = 40 + i * 150
        d.rectangle([x * scale, 176 * scale, (x + 130) * scale, (176 + 46) * scale], fill=c)
        t((x, 232), "L%d  #%02X%02X%02X" % (i, c[0], c[1], c[2]), 19, MUTED)

    y = 300
    t((40, y), "① 基础族：单击 / 滑过 / 轻扫（含方向箭头）", 26, INK, True)
    y += 56
    for kind, label, size, lane in [("tap", "tap 单击 130×14", SPEC["tap"], 0),
                                    ("drag", "drag 滑过 94×12", SPEC["drag"], 1),
                                    ("flick", "flick 轻扫 130×14", SPEC["flick"], 2)]:
        ic = note_body(size, LADDER[lane], kind)
        im.paste(ic, (int(40 * scale), int(y * scale)), ic)
        t((40, y + 62), label, 21, MUTED)
        y += 130

    # 上下箭头（独立图形）
    for i, (name, flip) in enumerate([("↑", 0), ("↓", 1)]):
        x = 620 + i * 200
        box = Image.new("RGBA", (90, 90), (0, 0, 0, 0))
        bd = ImageDraw.Draw(box)
        col = LADDER[3]
        chamfer(bd, (18, 18, 72, 72), 10, fill=col + (225,), outline=INK + (190,), width=1)
        pts = [(45, 26), (30, 54), (60, 54)] if flip == 0 else [(45, 64), (30, 36), (60, 36)]
        bd.polygon(pts, fill=INK + (245,))
        im.paste(box, (int(x * scale), int((y - 130 + 10) * scale)), box)
        t((x + 8, y - 40), "箭头 %s 26×26" % name, 19, MUTED)

    y += 20
    t((40, y), "② 长按：头 / 连接段 / 尾（三段可无缝拼接）", 26, INK, True)
    y += 56
    hh = note_body(SPEC["hold_head"], LADDER[1], "hold_head")
    im.paste(hh, (int(40 * scale), int(y * scale)), hh)
    t((40, y + 62), "head 头 130×14（尾侧双刻度）", 21, MUTED)
    body = hold_body((128, 260), LADDER[1])
    im.paste(body, (int(320 * scale), int((y - 96) * scale)), body)
    t((320, y + 174), "body 连接段 128×可变（两侧亮边 + 分段块，可平铺）", 21, MUTED)
    ht = note_body(SPEC["hold_tail"], LADDER[1], "hold_tail")
    im.paste(ht, (int(620 * scale), int((y + 30) * scale)), ht)
    t((620, y + 62), "tail 尾 122×10（双刻度）", 21, MUTED)
    y += 300

    t((40, y), "③ slide：头（斜纹起点）/ 尾（箭头终点） + 轨迹带", 26, INK, True)
    y += 56
    sh = note_body(SPEC["slide_head"], LADDER[3], "slide_head")
    im.paste(sh, (int(40 * scale), int(y * scale)), sh)
    t((40, y + 62), "slide head 130×14", 21, MUTED)
    st = note_body(SPEC["slide_tail"], LADDER[3], "slide_tail")
    im.paste(st, (int(320 * scale), int((y + 4) * scale)), st)
    t((320, y + 62), "slide tail 74×10", 21, MUTED)
    ribbon = hold_body((420, 90), LADDER[3], segments=6)
    im.paste(ribbon, (int(540 * scale), int((y - 14) * scale)), ribbon)
    t((540, y + 92), "轨迹带：按住时逐段收起，尾端锚在路径终点", 21, MUTED)
    y += 170

    t((40, y), "④ 宽键（kick）：横跨全轨", 26, INK, True)
    y += 56
    wide = note_body(SPEC["wide"], LADDER[2], "wide")
    im.paste(wide, (int(40 * scale), int(y * scale)), wide)
    t((40, y + 62), "wide 670×14（中央菱形 + 六段刻度）", 21, MUTED)
    y += 150

    t((40, y), "⑤ 同轨对比：同形状、不同轨色（五轨同色系，只差明度）", 26, INK, True)
    y += 56
    for i, c in enumerate(LADDER):
        ic = note_body(SPEC["tap"], c, "tap")
        im.paste(ic, (int((40 + i * 280) * scale), int(y * scale)), ic)
        t((40 + i * 280, y + 62), "lane %d" % i, 19, MUTED)
    y += 130

    t((40, y), "注：暖色粉 #F0A8CC 仅用于连击/奖励等正向提示，不出现在音符本体上", 20, ACCENT)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "note_family_draft.png")
    im.save(path)
    print("WROTE", path, im.size)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scale", type=float, default=1.0)
    args = ap.parse_args()
    sheet(args.scale)


if __name__ == "__main__":
    main()
