#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FallenAngel 图标风格探索：4 个方向的整体预览设计稿。

只出审阅稿（art_tools/review/directions/），不覆盖已交付资源、不接入游戏。
每个方向用同一组 8 个图标演示：大图 256、小尺寸 48 检验、按钮内效果。
几何为程序化曲线（贝塞尔/圆弧/锥形笔画），确定性可复现。

用法：python -X utf8 art_tools/icon_directions.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

INK = (229, 233, 228, 255)
ACCENT = (141, 198, 208, 255)
PAID = (207, 180, 123, 255)
MUTED = (155, 171, 175, 255)
BG = (16, 26, 34, 255)
CARD = (21, 36, 46, 255)
CLEAR = (0, 0, 0, 0)
SS = 4

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "art_tools", "review", "directions")
FONT_PATH = os.path.join(ROOT, "Assets", "Fonts", "SourceHanSansCN-Regular.otf")


# ---------- 几何 ----------
def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1])


def mul(a, k):
    return (a[0] * k, a[1] * k)


def length(a):
    return math.hypot(a[0], a[1])


def unit(a):
    l = length(a) or 1.0
    return (a[0] / l, a[1] / l)


def bez2(p0, p1, p2, n=18):
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                    u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))
    return out


def bez3(p0, p1, p2, p3, n=26):
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def arc_pts(center, r, a0, a1, n=36):
    return [(center[0] + math.cos(math.radians(a0 + (a1 - a0) * i / n)) * r,
             center[1] + math.sin(math.radians(a0 + (a1 - a0) * i / n)) * r) for i in range(n + 1)]


def circle_pts(center, r, n=48):
    return arc_pts(center, r, 0, 360, n)


def spiral_pts(center, r0, r1, a0, a1, n=48):
    out = []
    for i in range(n + 1):
        t = i / n
        r = r0 + (r1 - r0) * t
        a = math.radians(a0 + (a1 - a0) * t)
        out.append((center[0] + math.cos(a) * r, center[1] + math.sin(a) * r))
    return out


def rounded_poly(pts, radius, s=8):
    """把多边形的尖角替换成二次贝塞尔圆角。"""
    out = []
    n = len(pts)
    for i in range(n):
        prev, cur, nxt = pts[i - 1], pts[i], pts[(i + 1) % n]
        v1, v2 = unit(sub(prev, cur)), unit(sub(nxt, cur))
        r = min(radius, length(sub(prev, cur)) * 0.5, length(sub(nxt, cur)) * 0.5)
        t1, t2 = add(cur, mul(v1, r)), add(cur, mul(v2, r))
        out.append(t1)
        for k in range(1, s):
            out.append(bez2(t1, cur, t2, s)[k])
        out.append(t2)
    return out


# ---------- 绘制指令 ----------
def F(pts, color=INK):
    return ("fillpoly", pts, color)


def S(pts, w=6.0, color=INK):
    return ("stroke", pts, w, color)


def T(pts, w0, w1, color=INK):
    return ("taper", pts, w0, w1, color)


def C(center, r, color=ACCENT):
    return ("circle", center, r, color)


def R(center, r, w=6.0, color=INK):
    return ("ring", center, r, w, color)


def A(center, r, a0, a1, w=6.0, color=INK):
    return ("arc", center, r, a0, a1, w, color)


def E(center, r, w=6.0, color=CLEAR):
    """负形（擦除）：在已填充的形体上打洞。"""
    return ("ring", center, r, w, color)


def RR(x, y, w, h, rad, mode="stroke", width=6.0, color=INK):
    return ("rrect", (x, y, w, h), rad, mode, width, color)


def render(ops, size, space=128.0):
    big = int(size * SS)
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = big / space

    def p(pt):
        return (pt[0] * k, pt[1] * k)

    for op in ops:
        kind = op[0]
        if kind == "fillpoly":
            d.polygon([p(q) for q in op[1]], fill=op[2])
        elif kind == "stroke":
            pts, w, color = op[1], op[2] * k, op[3]
            d.line([p(q) for q in pts], fill=color, width=max(1, int(round(w))), joint="curve")
            _caps(d, pts, w, color, p)
        elif kind == "taper":
            _taper(d, op[1], op[2] * k, op[3] * k, op[4], p)
        elif kind == "circle":
            c, r, color = op[1], op[2] * k, op[3]
            d.ellipse([c[0] * k - r, c[1] * k - r, c[0] * k + r, c[1] * k + r], fill=color)
        elif kind == "ring":
            c, r, w, color = op[1], op[2] * k, op[3] * k, op[4]
            d.ellipse([c[0] * k - r, c[1] * k - r, c[0] * k + r, c[1] * k + r],
                      outline=color, width=max(1, int(round(w))))
        elif kind == "arc":
            c, r, a0, a1, w, color = op[1], op[2], op[3], op[4], op[5] * k, op[6]
            pts = arc_pts(c, r, a0, a1, 48)
            d.line([p(q) for q in pts], fill=color, width=max(1, int(round(w))), joint="curve")
            _caps(d, pts, w, color, p)
        elif kind == "rrect":
            (x, y, w, h), rad, mode, lw, color = op[1], op[2], op[3], op[4] * k, op[5]
            box = [x * k, y * k, (x + w) * k, (y + h) * k]
            if mode == "fill":
                d.rounded_rectangle(box, radius=rad * k, fill=color)
            else:
                d.rounded_rectangle(box, radius=rad * k, outline=color, width=max(1, int(round(lw))))
    return img.resize((size, size), Image.LANCZOS)


def _caps(d, pts, w, color, p):
    r = w / 2.0
    for q in (pts[0], pts[-1]):
        d.ellipse([p(q)[0] - r, p(q)[1] - r, p(q)[0] + r, p(q)[1] + r], fill=color)


def _taper(d, pts, w0, w1, color, p):
    """逐段四边形叠加绘制锥形笔画。

    不能把整条笔画拼成一个闭合多边形：曲线采样密集时左右偏移线会在弯道自交，
    PIL 的奇偶填充会把重叠区判成空洞（大图上表现为缺口/乱纹，小图缩小后看不出来）。
    逐段 quad + 圆点接头可以完全避免这个问题。
    """
    n = len(pts)
    for i in range(n - 1):
        a, b = pts[i], pts[i + 1]
        t0, t1 = i / (n - 1), (i + 1) / (n - 1)
        h0 = (w0 + (w1 - w0) * t0) * 0.5
        h1 = (w0 + (w1 - w0) * t1) * 0.5
        nrm = unit((-unit(sub(b, a))[1], unit(sub(b, a))[0]))
        quad = [add(a, mul(nrm, h0)), add(b, mul(nrm, h1)),
                add(b, mul(nrm, -h1)), add(a, mul(nrm, -h0))]
        d.polygon([p(q) for q in quad], fill=color)
        for q, hh in ((a, h0), (b, h1)):
            d.ellipse([p(q)[0] - hh, p(q)[1] - hh, p(q)[0] + hh, p(q)[1] + hh], fill=color)


# ---------- 方向 A：圆角几何 ----------
def dir_a(name):
    c = (64.0, 64.0)
    if name == "coin":
        return [R(c, 42, 6.5), F(rounded_poly([(48, 48), (80, 48), (80, 80), (48, 80)], 10), ACCENT)]
    if name == "growth":
        return [F(rounded_poly([(64, 24), (104, 64), (64, 104), (24, 64)], 20), ACCENT),
                S([(64, 20), (64, 6)], 6.5), S([(64, 108), (64, 122)], 6.5),
                S([(20, 64), (6, 64)], 6.5), S([(108, 64), (122, 64)], 6.5)]
    if name == "refresh":
        return [A(c, 38, 330, 620, 6.5),
                F(rounded_poly([(98, 18), (120, 44), (92, 54)], 9))]
    if name == "back":
        return [S([(106, 64), (38, 64)], 7.0), S([(38, 64), (66, 38)], 7.0), S([(38, 64), (66, 90)], 7.0)]
    if name == "lock":
        return [RR(34, 58, 60, 46, 12), A((64, 58), 20, 180, 360, 6.5), F(rounded_poly([(58, 74), (70, 74), (70, 90), (58, 90)], 5), ACCENT)]
    if name == "pause":
        return [RR(40, 30, 16, 68, 8, "fill", color=INK), RR(72, 30, 16, 68, 8, "fill", color=INK)]
    if name == "shop":
        return [S([(22, 54), (34, 26)], 6.5), S([(34, 26), (94, 26)], 6.5), S([(94, 26), (106, 54)], 6.5),
                S([(22, 54), (106, 54)], 6.5),
                S([(34, 64), (34, 102)], 6.0), S([(94, 64), (94, 102)], 6.0), S([(34, 102), (94, 102)], 6.0),
                F(rounded_poly([(40, 72), (54, 72), (54, 92), (40, 92)], 6), ACCENT)]
    if name == "position":
        return [F(rounded_poly([(64, 112), (24, 42), (104, 42)], 26)), E((64, 50), 11, 24)]
    raise ValueError(name)


# ---------- 方向 B：液滴曲线 ----------
def dir_b(name):
    c = (64.0, 64.0)
    if name == "coin":
        return [T(arc_pts(c, 38, 200, 470, 40), 2.0, 8.0),
                T(arc_pts(c, 38, 20, 290, 40), 8.0, 2.0),
                C(c, 9, ACCENT)]
    if name == "growth":
        return [T(spiral_pts((70, 66), 4, 46, 200, 640, 56), 1.6, 8.5), C((70, 66), 7, ACCENT)]
    if name == "refresh":
        return [T(bez3((20, 84), (30, 28), (96, 26), (108, 74), 40), 1.8, 8.5),
                T(bez3((104, 96), (86, 112), (52, 112), (34, 96), 24), 6.0, 1.8),
                C((112, 62), 6, ACCENT)]
    if name == "back":
        return [T(bez3((110, 64), (86, 64), (58, 64), (36, 64), 20), 9.0, 2.0),
                T(bez2((36, 64), (48, 50), (62, 40), 20), 2.0, 7.0),
                T(bez2((36, 64), (48, 78), (62, 88), 20), 2.0, 7.0)]
    if name == "lock":
        return [T(bez3((34, 74), (34, 44), (94, 44), (94, 74), 32), 2.0, 7.0),
                T(bez3((32, 68), (30, 104), (98, 104), (96, 68), 32), 7.0, 2.0),
                C((64, 84), 8, ACCENT)]
    if name == "pause":
        return [T([(48, 28), (50, 64), (48, 100)], 16, 10),
                T([(80, 28), (78, 64), (80, 100)], 16, 10)]
    if name == "shop":
        return [T(bez3((18, 56), (38, 24), (90, 24), (110, 56), 32), 3.0, 8.0),
                T(arc_pts((34, 56), 14, 0, 180, 18), 5.0, 3.0),
                T(arc_pts((64, 56), 14, 0, 180, 18), 5.0, 3.0),
                T(arc_pts((94, 56), 14, 0, 180, 18), 5.0, 3.0),
                T([(30, 96), (98, 96)], 1.8, 6.5)]
    if name == "position":
        return [T(bez3((64, 114), (34, 82), (26, 46), (64, 26), 30), 8.0, 3.0),
                T(bez3((64, 114), (94, 82), (102, 46), (64, 26), 30), 3.0, 8.0),
                C((64, 54), 8, ACCENT)]
    raise ValueError(name)


# ---------- 方向 C：双侧描边 + 负形 ----------
def dir_c(name):
    c = (64.0, 64.0)
    if name == "coin":
        return [C(c, 44, ACCENT), R(c, 47, 5.0, INK), E(c, 16, 22), F(rounded_poly([(60, 78), (72, 78), (72, 88), (60, 88)], 4), INK)]
    if name == "growth":
        return [F(rounded_poly([(64, 20), (108, 64), (64, 108), (20, 64)], 22), ACCENT),
                R(c, 52, 4.0, INK), E(c, 14, 20)]
    if name == "refresh":
        # 粗弧 + 缺口 + 实心箭头；外圈细描边收边（避免负形把整圈吃掉）
        return [A(c, 40, 330, 620, 20, ACCENT),
                F(rounded_poly([(96, 16), (120, 44), (92, 54)], 8), ACCENT),
                R(c, 51, 3.0, INK)]
    if name == "back":
        return [F(rounded_poly([(18, 18), (110, 18), (110, 110), (18, 110)], 26), ACCENT),
                R(c, 52, 4.0, INK),
                S([(92, 64), (44, 64)], 12, CLEAR), S([(44, 64), (66, 42)], 12, CLEAR), S([(44, 64), (66, 86)], 12, CLEAR)]
    if name == "lock":
        return [F(rounded_poly([(28, 54), (100, 54), (100, 108), (28, 108)], 18), ACCENT),
                R(c, 56, 4.0, INK), A((64, 54), 22, 190, 350, 8.0, INK),
                E((64, 74), 10, 20), S([(64, 84), (64, 98)], 9, CLEAR)]
    if name == "pause":
        return [F(rounded_poly([(22, 22), (106, 22), (106, 106), (22, 106)], 26), ACCENT),
                R(c, 50, 4.0, INK), S([(50, 42), (50, 86)], 12, CLEAR), S([(78, 42), (78, 86)], 12, CLEAR)]
    if name == "shop":
        return [F(rounded_poly([(18, 46), (110, 46), (110, 80), (18, 80)], 16), ACCENT),
                F(rounded_poly([(30, 22), (98, 22), (98, 44), (30, 44)], 10), ACCENT),
                R(c, 58, 4.0, INK),
                S([(42, 54), (42, 72)], 8, CLEAR), S([(64, 54), (64, 72)], 8, CLEAR), S([(86, 54), (86, 72)], 8, CLEAR)]
    if name == "position":
        return [F(rounded_poly([(64, 112), (22, 40), (106, 40)], 30), ACCENT),
                R(c, 54, 4.0, INK), E((64, 50), 13, 26)]
    raise ValueError(name)


# ---------- 方向 D：细线 + 焦点 ----------
def dir_d(name):
    c = (64.0, 64.0)
    if name == "coin":
        return [R(c, 40, 3.6), C(c, 11, ACCENT)]
    if name == "growth":
        return [S(rounded_poly([(64, 22), (106, 64), (64, 106), (22, 64)], 20) +
                [rounded_poly([(64, 22), (106, 64), (64, 106), (22, 64)], 20)[0]], 3.6),
                C(c, 9, ACCENT)]
    if name == "refresh":
        return [A(c, 40, 330, 620, 3.6), F(rounded_poly([(100, 16), (122, 46), (92, 56)], 8), ACCENT)]
    if name == "back":
        return [S([(104, 64), (36, 64)], 3.6), S([(36, 64), (62, 40)], 3.6), S([(36, 64), (62, 88)], 3.6),
                C((110, 64), 7, ACCENT)]
    if name == "lock":
        return [RR(34, 58, 60, 46, 10, "stroke", 3.6), A((64, 58), 20, 180, 360, 3.6),
                F(rounded_poly([(56, 74), (72, 74), (72, 90), (56, 90)], 5), ACCENT)]
    if name == "pause":
        return [RR(32, 30, 64, 68, 12, "stroke", 3.6),
                RR(44, 44, 12, 40, 6, "fill", color=ACCENT), RR(72, 44, 12, 40, 6, "fill", color=ACCENT)]
    if name == "shop":
        return [S([(22, 54), (34, 26)], 3.6), S([(34, 26), (94, 26)], 3.6), S([(94, 26), (106, 54)], 3.6),
                S([(22, 54), (106, 54)], 3.6),
                S([(34, 66), (34, 100)], 3.6), S([(94, 66), (94, 100)], 3.6), S([(34, 100), (94, 100)], 3.6),
                F(rounded_poly([(42, 74), (86, 74), (86, 88), (42, 88)], 6), ACCENT)]
    if name == "position":
        return [S([(64, 110), (26, 44), (102, 44), (64, 110)], 3.6), C((64, 52), 10, ACCENT)]
    raise ValueError(name)


DIRECTIONS = [
    ("A", "rounded_geometry", "方向 A｜圆角几何",
     "直角与圆弧统一到同一套圆角网格：笔画 6.5、圆头圆角、棱角一律倒圆；冷静、结构清楚，接近系统图标。", dir_a),
    ("B", "liquid_curve", "方向 B｜液滴曲线",
     "全曲线 + 锥形笔画（起笔细、收笔粗），纹样取自水流与浪脊；最有深海气质，亲和度高。", dir_b),
    ("C", "duotone_negative", "方向 C｜双侧描边 + 负形",
     "大色块 + 挖空负形 + 细描边，体量感最强，远看是一枚枚徽记；适合强调「获得 / 状态」这类反馈。", dir_c),
    ("D", "fine_line_accent", "方向 D｜细线 + 焦点",
     "细线（3.6）勾勒 + 单点强调色，留白最大、最克制；信息密度高，适合列表与密集界面。", dir_d),
]

ICONS = [("coin", "货币"), ("growth", "成长"), ("refresh", "刷新"), ("back", "返回"),
         ("lock", "锁定"), ("pause", "暂停"), ("shop", "商店"), ("position", "定位")]


def font(size):
    try:
        return ImageFont.truetype(FONT_PATH, size)
    except Exception:
        return ImageFont.load_default()


def chip(icon_img, label):
    w, h = 300, 96
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = rounded_poly([(3, 3), (w - 3, 3), (w - 3, h - 3), (3, h - 3)], 26)
    d.polygon(pts, fill=CARD)
    d.line(pts + [pts[0]], fill=ACCENT, width=3, joint="curve")
    icon = icon_img.resize((52, 52), Image.LANCZOS)
    img.paste(icon, (26, (h - 52) // 2), icon)
    d.rounded_rectangle([96, h // 2 - 9, w - 30, h // 2 + 9], radius=9, fill=INK)
    return img


def direction_sheet(key, slug, title, subtitle, fn):
    cols, cell = 4, 310
    header, strip_h, chip_h = 120, 110, 150
    W = cols * cell + 80
    H = header + 2 * cell + strip_h + chip_h + 40
    sheet = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(sheet)
    d.text((40, 26), title, font=font(34), fill=INK)
    d.text((40, 74), subtitle, font=font(20), fill=MUTED)

    for i, (name, cn) in enumerate(ICONS):
        r, c = divmod(i, cols)
        big = render(fn(name), 256)
        x = 40 + c * cell + (cell - 256) // 2
        y = header + r * cell
        sheet.paste(big, (x, y), big)
        label = "%s  %s" % (cn, name)
        tw = d.textlength(label, font=font(22))
        d.text((40 + c * cell + (cell - tw) / 2, y + 262), label, font=font(22), fill=INK)

    y0 = header + 2 * cell + 10
    d.text((40, y0), "小尺寸检验（48px）", font=font(22), fill=MUTED)
    for i, (name, _cn) in enumerate(ICONS):
        small = render(fn(name), 48)
        sheet.paste(small, (40 + i * 80, y0 + 34), small)

    y1 = y0 + 96
    d.text((40, y1), "按钮内效果", font=font(22), fill=MUTED)
    for i, name in enumerate(["coin", "refresh", "back"]):
        c = chip(render(fn(name), 128), name)
        sheet.paste(c, (40 + i * 320, y1 + 30), c)

    path = os.path.join(OUT, "direction_%s_%s.png" % (key.lower(), slug))
    sheet.save(path)
    return path


def compare_sheet():
    cell_w, cell_h = 170, 210
    W = 200 + len(ICONS) * cell_w
    H = 130 + len(DIRECTIONS) * cell_h
    sheet = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(sheet)
    d.text((40, 30), "四个方向对照（同组 8 枚图标）", font=font(34), fill=INK)
    d.text((40, 78), "每格为该方向在 128px 下的实际观感；细节与小尺寸表现见各自单页。", font=font(20), fill=MUTED)
    for c, (name, cn) in enumerate(ICONS):
        d.text((200 + c * cell_w + 20, 108), cn, font=font(20), fill=MUTED)
    for r, (key, _slug, _title, _sub, fn) in enumerate(DIRECTIONS):
        y = 130 + r * cell_h
        d.text((40, y + 80), "方向 %s" % key, font=font(30), fill=ACCENT)
        for c, (name, _cn) in enumerate(ICONS):
            im = render(fn(name), 128)
            sheet.paste(im, (200 + c * cell_w + 20, y + 24), im)
    path = os.path.join(OUT, "directions_compare.png")
    sheet.save(path)
    return path


def main():
    os.makedirs(OUT, exist_ok=True)
    made = [direction_sheet(k, slug, t, s, f) for k, slug, t, s, f in DIRECTIONS]
    made.append(compare_sheet())
    for p in made:
        print(os.path.relpath(p, ROOT).replace("\\", "/"))
    print("完成：%d 张方向稿 -> %s" % (len(made), os.path.relpath(OUT, ROOT)))


if __name__ == "__main__":
    main()
