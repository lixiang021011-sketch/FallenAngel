#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""FallenAngel 美术资源导出（UI001 规范 · 方向 A「圆角几何」第 2 版）。

同一套几何定义同时产出：
  1) 矢量源文件 SVG -> art_tools/source/<name>.svg
  2) 运行时 RGBA PNG -> Assets/Art/ui/<name>.png
  3) 审阅拼版图 -> art_tools/review/sheet_*.png
  4) 路径对照表 -> art_tools/review/art_delivery_review.md

方向 A 约定：笔画 6.5（128 栅格）、圆头圆角、棱角一律倒圆（二次贝塞尔圆角），
图标为单色（#E5E9E4）以便运行时按状态着色。全部程序化绘制，不使用图像生成模型。

用法：python -X utf8 art_tools/export_ui_art.py
"""
import math
import os

from PIL import Image, ImageDraw

INK = (229, 233, 228, 255)
MUTED = (155, 171, 175, 255)
ACCENT = (141, 198, 208, 255)
PAID = (207, 180, 123, 255)
DANGER = (195, 134, 128, 255)
CARD = (21, 36, 46, 242)
CARD_HOVER = (30, 52, 66, 250)
CARD_PRESS = (14, 24, 31, 255)
CARD_OFF = (21, 36, 46, 170)
LINE = (141, 198, 208, 140)
LINE_OFF = (155, 171, 175, 110)

SS = 4
SPACE_ICON = (128.0, 128.0)
SPACE_COMP = (192.0, 96.0)


# ---------- 几何工具 ----------
def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1])


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def _mul(a, k):
    return (a[0] * k, a[1] * k)


def _unit(a):
    l = math.hypot(a[0], a[1]) or 1.0
    return (a[0] / l, a[1] / l)


def bez2(p0, p1, p2, n=12):
    out = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        out.append((u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
                    u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1]))
    return out


def arc_pts(center, r, a0, a1, n=36):
    return [(center[0] + math.cos(math.radians(a0 + (a1 - a0) * i / n)) * r,
             center[1] + math.sin(math.radians(a0 + (a1 - a0) * i / n)) * r) for i in range(n + 1)]


def rounded_poly(pts, radius, s=8):
    """尖角替换为二次贝塞尔圆角，得到方向 A 的圆润轮廓。"""
    out = []
    n = len(pts)
    for i in range(n):
        prev, cur, nxt = pts[i - 1], pts[i], pts[(i + 1) % n]
        v1, v2 = _unit(_sub(prev, cur)), _unit(_sub(nxt, cur))
        r = min(radius, math.dist(prev, cur) * 0.5, math.dist(nxt, cur) * 0.5)
        t1, t2 = _add(cur, _mul(v1, r)), _add(cur, _mul(v2, r))
        out.append(t1)
        out.extend(bez2(t1, cur, t2, s)[1:-1])
        out.append(t2)
    return out


def closed(pts):
    return list(pts) + [pts[0]]


# ---------- 图元 ----------
def F(pts, color=INK):
    return ("poly", pts, color)


def P(pts, w=6.5, color=INK):
    return ("path", pts, w, color)


def R(center, r, w=6.5, color=INK):
    return ("ring", center, r, w, color)


def A(center, r, a0, a1, w=6.5, color=INK):
    return ("arc", center, r, a0, a1, w, color)


def RR(x, y, w, h, rad, mode="stroke", width=6.5, color=INK):
    return ("rrect", (x, y, w, h), rad, mode, width, color)


def cut_rect(w, h, cut, fill, stroke, stroke_w, inset=2.0):
    return ("cutrect", w, h, cut, fill, stroke, stroke_w, inset)


def dashed_rect(w, h, cut, stroke, stroke_w, inset=2.0):
    return ("dashed", w, h, cut, stroke, stroke_w, inset)


def _cut_outline(w, h, cut, inset):
    return [(inset, inset), (inset, h - cut), (cut, h - inset),
            (w - inset, h - inset), (w - inset, cut), (w - cut, inset)]


def _lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def _stroke_w(prim):
    kind = prim[0]
    if kind == "path":
        return prim[2]
    if kind == "ring":
        return prim[3]
    if kind == "arc":
        return prim[5]
    if kind == "rrect":
        return prim[4]
    if kind == "cutrect":
        return prim[5]
    if kind == "dashed":
        return prim[4]
    return 0.0


def _outline(prim):
    if prim[0] == "path":
        return list(prim[1])
    if prim[0] == "ring":
        return closed(arc_pts(prim[1], prim[2], 0, 360, 64))
    if prim[0] == "arc":
        return arc_pts(prim[1], prim[2], prim[3], prim[4], 48)
    if prim[0] == "cutrect":
        return closed(_cut_outline(prim[1], prim[2], prim[3], prim[7]))
    return []


# ---------- 渲染 ----------
def render(prims, size, space):
    if isinstance(size, int):
        size = (size, size)
    big = (size[0] * SS, size[1] * SS)
    img = Image.new("RGBA", big, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    sx, sy = big[0] / space[0], big[1] / space[1]
    scale = (sx + sy) * 0.5

    def px(pt):
        return (pt[0] * sx, pt[1] * sy)

    for prim in prims:
        kind = prim[0]
        if kind == "poly":
            d.polygon([px(q) for q in prim[1]], fill=prim[2])
        elif kind == "ring":
            c, r, w, color = prim[1], prim[2] * scale, prim[3] * scale, prim[4]
            d.ellipse([c[0] * sx - r, c[1] * sy - r, c[0] * sx + r, c[1] * sy + r],
                      outline=color, width=max(1, int(round(w))))
        elif kind == "rrect":
            (x, y, w, h), rad, mode, lw, color = prim[1], prim[2], prim[3], prim[4] * scale, prim[5]
            box = [x * sx, y * sy, (x + w) * sx, (y + h) * sy]
            if mode == "fill":
                d.rounded_rectangle(box, radius=rad * scale, fill=color)
            else:
                d.rounded_rectangle(box, radius=rad * scale, outline=color, width=max(1, int(round(lw))))
        elif kind == "cutrect":
            _, w, h, cut, fill, stroke, sw, inset = prim
            box = _cut_outline(w, h, cut, inset)
            if fill[3] > 0:
                d.polygon([px(q) for q in box], fill=fill)
            d.line([px(q) for q in closed(box)], fill=stroke, width=max(1, int(round(sw * scale))), joint="curve")
        elif kind == "dashed":
            _, w, h, cut, stroke, sw, inset = prim
            box = _cut_outline(w, h, cut, inset)
            for i in range(len(box)):
                a, b = box[i], box[(i + 1) % len(box)]
                for t in (0.0, 0.5):
                    d.line([px(_lerp(a, b, t)), px(_lerp(a, b, t + 0.32))], fill=stroke,
                           width=max(1, int(round(sw * scale))))
        else:  # path / arc：圆头笔画
            pts = _outline(prim)
            w = _stroke_w(prim) * scale
            color = prim[3] if kind == "path" else prim[6]
            d.line([px(q) for q in pts], fill=color, width=max(1, int(round(w))), joint="curve")
            r = w * 0.5
            for q in (pts[0], pts[-1]):
                d.ellipse([px(q)[0] - r, px(q)[1] - r, px(q)[0] + r, px(q)[1] + r], fill=color)
    return img.resize(size, Image.LANCZOS)


# ---------- SVG ----------
def svg_doc(prims, space, color="#E5E9E4"):
    head = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %g %g" width="%g" height="%g">\n'
            % (space[0], space[1], space[0], space[1]))
    body = []
    for prim in prims:
        kind = prim[0]
        if kind == "poly":
            pts = " ".join("%.2f,%.2f" % q for q in prim[1])
            body.append('<polygon points="%s" fill="%s"/>' % (pts, color))
        elif kind == "ring":
            c, r, w = prim[1], prim[2], prim[3]
            body.append('<circle cx="%.2f" cy="%.2f" r="%.2f" fill="none" stroke="%s" stroke-width="%.2f"/>'
                        % (c[0], c[1], r, color, w))
        elif kind == "rrect":
            (x, y, w, h), rad, mode, lw = prim[1], prim[2], prim[3], prim[4]
            body.append('<rect x="%.2f" y="%.2f" width="%.2f" height="%.2f" rx="%.2f" fill="%s" stroke="%s" '
                        'stroke-width="%.2f"/>' % (x, y, w, h, rad, "none" if mode != "fill" else color,
                                                   "none" if mode == "fill" else color, lw))
        elif kind in ("path", "arc"):
            pts = _outline(prim)
            path = " ".join(("M" if i == 0 else "L") + "%.2f %.2f" % q for i, q in enumerate(pts))
            body.append('<path d="%s" fill="none" stroke="%s" stroke-width="%.2f" stroke-linecap="round"/>'
                        % (path, color, _stroke_w(prim)))
        elif kind == "cutrect":
            _, w, h, cut, fill, stroke, sw, inset = prim
            pts = " ".join("%.2f,%.2f" % q for q in _cut_outline(w, h, cut, inset))
            body.append('<polygon points="%s" fill="#%02X%02X%02X" fill-opacity="%.2f" stroke="#%02X%02X%02X" '
                        'stroke-width="%.2f"/>' % (pts, fill[0], fill[1], fill[2], fill[3] / 255.0,
                                                   stroke[0], stroke[1], stroke[2], sw))
        elif kind == "dashed":
            _, w, h, cut, stroke, sw, inset = prim
            box = _cut_outline(w, h, cut, inset)
            for i in range(len(box)):
                a, b = box[i], box[(i + 1) % len(box)]
                for t in (0.0, 0.5):
                    p0, p1 = _lerp(a, b, t), _lerp(a, b, t + 0.32)
                    body.append('<line x1="%.2f" y1="%.2f" x2="%.2f" y2="%.2f" stroke="#%02X%02X%02X" '
                                'stroke-width="%.2f" stroke-linecap="round"/>'
                                % (p0[0], p0[1], p1[0], p1[1], stroke[0], stroke[1], stroke[2], sw))
    return head + "\n".join(body) + "\n</svg>\n"


# ---------- 方向 A 形状（128 栅格） ----------
def icon_coin():
    return [R((64, 64), 40, 6.5), RR(55, 55, 18, 18, 6, "fill", color=INK)]


def icon_growth():
    return [P(closed(rounded_poly([(64, 24), (104, 64), (64, 104), (24, 64)], 20)), 6.5),
            RR(56, 56, 16, 16, 5, "fill", color=INK),
            P([(64, 22), (64, 12)], 6.0), P([(64, 106), (64, 116)], 6.0),
            P([(22, 64), (12, 64)], 6.0), P([(106, 64), (116, 64)], 6.0)]


def icon_refresh():
    return [A((64, 64), 38, 330, 620, 6.5),
            F(rounded_poly([(98, 18), (120, 44), (92, 54)], 9))]


def icon_position():
    # 圆环 + 实心点（与游戏内保持同形；单网格无法干净挖孔，实心水滴挖孔的写法在 Unity 侧不可用）
    return [R((64, 64), 43.5, 7.0), RR(47.4, 47.4, 33.2, 33.2, 16.6, "fill")]


def icon_pause():
    return [RR(40, 30, 16, 68, 8, "fill"), RR(72, 30, 16, 68, 8, "fill")]


def icon_play():
    return [F(rounded_poly([(42, 26), (102, 64), (42, 102)], 16))]


def icon_back():
    return [P([(106, 64), (40, 64)], 7.0), P([(40, 64), (66, 40)], 7.0), P([(40, 64), (66, 88)], 7.0)]


def icon_close():
    return [P([(36, 36), (92, 92)], 7.0), P([(92, 36), (36, 92)], 7.0)]


def icon_lock():
    return [RR(34, 58, 60, 46, 14), A((64, 58), 20, 180, 360, 6.5),
            RR(58, 72, 12, 18, 5, "fill")]


def icon_check():
    return [P(bez2((30, 66), (38, 78), (48, 88), 10) + bez2((48, 88), (70, 64), (98, 32), 14), 7.0)]


def icon_chevron():
    return [P([(48, 30), (76, 64), (48, 98)], 7.0)]


def icon_info():
    return [R((64, 64), 40, 6.5), RR(58, 33, 12, 12, 5, "fill"), P([(64, 56), (64, 90)], 7.0)]


def room_start():
    return [P(closed(rounded_poly([(64, 16), (112, 64), (64, 112), (16, 64)], 22)), 6.5),
            RR(52, 52, 24, 24, 8, "fill")]


def room_battle():
    return [P([(28, 26), (100, 98)], 7.0), P([(100, 26), (28, 98)], 7.0),
            P([(20, 42), (42, 20)], 6.0), P([(86, 108), (108, 86)], 6.0)]


def room_shop():
    return [P([(20, 54), (34, 26), (94, 26), (108, 54)], 6.5), P([(20, 54), (108, 54)], 6.5),
            P([(34, 66), (34, 102)], 6.0), P([(94, 66), (94, 102)], 6.0), P([(34, 102), (94, 102)], 6.0),
            RR(42, 70, 16, 24, 5, "fill")]


def room_empty():
    return [P([(64, 100), (46, 100), (36, 90), (36, 42), (46, 32), (82, 32), (92, 42), (92, 90), (82, 100), (64, 100)], 6.5)]


def room_final():
    return [P([(30, 30), (60, 94)], 7.0), P([(98, 30), (68, 94)], 7.0),
            P([(22, 22), (106, 22)], 6.5), P([(64, 8), (64, 18)], 6.5)]


def symbol_reward():
    return [P([(30, 52), (98, 52)], 6.5), P([(34, 52), (40, 34), (88, 34), (94, 52)], 6.5),
            P([(32, 52), (32, 98)], 6.5), P([(96, 52), (96, 98)], 6.5), P([(32, 98), (96, 98)], 6.5),
            P([(64, 34), (64, 98)], 6.0)]


def symbol_amplify():
    return [P([(38, 56), (64, 30), (90, 56)], 7.0), P([(38, 90), (64, 64), (90, 90)], 7.0)]


def symbol_floor():
    return [P([(18, 98), (110, 98)], 7.0),
            P([(40, 46), (64, 20), (88, 46)], 7.0), P([(64, 20), (64, 72)], 7.0)]


def symbol_upgrade():
    return [P([(40, 70), (64, 44), (88, 70)], 7.0), P([(64, 44), (64, 100)], 7.0),
            P([(20, 20), (108, 20)], 6.5)]


def brand_emblem():
    return [P([(64, 26), (64, 80)], 4.0), P([(64, 80), (44, 106)], 4.0), P([(64, 80), (84, 106)], 4.0),
            F(rounded_poly([(64, 8), (74, 22), (64, 36), (54, 22)], 8)),
            A((64, 56), 36, 205, 335, 3.5), A((64, 56), 36, 25, 155, 3.5)]


def brand_mark():
    return [P(closed(rounded_poly([(64, 16), (112, 64), (64, 112), (16, 64)], 24)), 7.0),
            RR(50, 50, 28, 28, 9, "fill")]


def brand_result():
    return [P([(32, 34), (60, 90)], 7.0), P([(96, 34), (68, 90)], 7.0),
            P([(22, 24), (106, 24)], 6.5), P([(64, 8), (64, 18)], 6.5),
            P([(44, 108), (84, 108)], 6.0)]


# ---------- 组件（9-slice：设计 192×96，切角 30，导出 2 倍图） ----------
BW, BH, CUT = 192.0, 96.0, 30.0


def comp(fill, stroke, stroke_w=3.0):
    """UI002/UI003 组件：圆角矩形（半径 26），填充与描边分两笔，便于 9-slice。"""
    return [RR(2.5, 2.5, BW - 5, BH - 5, 26, "fill", color=fill),
            RR(2.5, 2.5, BW - 5, BH - 5, 26, "stroke", stroke_w, stroke)]


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG_DIR = os.path.join(ROOT, "Assets", "Art", "ui")
SVG_DIR = os.path.join(ROOT, "art_tools", "source")
REVIEW_DIR = os.path.join(ROOT, "art_tools", "review")
FONT_PATH = os.path.join(ROOT, "Assets", "Fonts", "SourceHanSansCN-Regular.otf")

META = {
    "ui_icon_coin": ("UI005", "货币：现金/价格/余额", "方向A 圆角几何"),
    "ui_icon_growth": ("UI005", "成长积分", "方向A 圆角几何"),
    "ui_icon_refresh": ("UI005", "刷新（商店整批刷新）", "方向A 圆角几何"),
    "ui_icon_position": ("UI005", "定位：当前位置/路线", "方向A 圆角几何"),
    "ui_icon_pause": ("UI005", "暂停", "方向A 圆角几何"),
    "ui_icon_play": ("UI005", "播放/开始演奏", "方向A 圆角几何"),
    "ui_icon_back": ("UI005", "返回", "方向A 圆角几何"),
    "ui_icon_close": ("UI005", "关闭", "方向A 圆角几何"),
    "ui_icon_lock": ("UI005", "锁定/前置不足", "方向A 圆角几何"),
    "ui_icon_check": ("UI005", "勾选/已通过", "方向A 圆角几何"),
    "ui_icon_chevron": ("UI005", "指示：可进入/展开", "方向A 圆角几何"),
    "ui_icon_info": ("UI005", "信息/提示（含错误提示）", "方向A 圆角几何"),
    "ui_map_room_start": ("MAP001", "房间：起点", "方向A 圆角几何"),
    "ui_map_room_battle": ("MAP001", "房间：战斗", "方向A 圆角几何"),
    "ui_map_room_shop": ("MAP001", "房间：商店", "方向A 圆角几何"),
    "ui_map_room_empty": ("MAP001", "房间：空占位房", "方向A 圆角几何"),
    "ui_map_room_final": ("MAP001", "房间：终点战斗", "方向A 圆角几何"),
    "ui_talent_symbol_reward": ("TAL002", "效果语义：奖励", "方向A 圆角几何"),
    "ui_talent_symbol_amplify": ("TAL002", "效果语义：增幅/折扣", "方向A 圆角几何"),
    "ui_talent_symbol_floor": ("TAL002", "效果语义：保底", "方向A 圆角几何"),
    "ui_talent_symbol_upgrade": ("TAL002", "效果语义：升级（target_effect）", "方向A 圆角几何"),
    "ui_brand_emblem_512": ("BR001", "主标识：航标（标题/加载页）", "方向A · 512"),
    "ui_brand_mark_128": ("BR001", "小标：导航标识", "方向A · 128"),
    "ui_brand_result_256": ("BR001", "结算标：旅程完成", "方向A · 256"),
    "ui_button_primary_normal": ("UI002", "主按钮·常态", "9-slice 圆角26（导出2x）"),
    "ui_button_primary_hover": ("UI002", "主按钮·悬停", "9-slice 圆角26（导出2x）"),
    "ui_button_primary_pressed": ("UI002", "主按钮·按下", "9-slice 圆角26（导出2x）"),
    "ui_button_primary_disabled": ("UI002", "主按钮·禁用", "9-slice 圆角26（导出2x）"),
    "ui_button_secondary_normal": ("UI002", "次按钮·常态", "9-slice 圆角26（导出2x）"),
    "ui_button_secondary_hover": ("UI002", "次按钮·悬停", "9-slice 圆角26（导出2x）"),
    "ui_button_secondary_pressed": ("UI002", "次按钮·按下", "9-slice 圆角26（导出2x）"),
    "ui_button_secondary_disabled": ("UI002", "次按钮·禁用", "9-slice 圆角26（导出2x）"),
    "ui_button_danger_normal": ("UI002", "危险按钮·常态", "9-slice 圆角26（导出2x）"),
    "ui_button_danger_hover": ("UI002", "危险按钮·悬停", "9-slice 圆角26（导出2x）"),
    "ui_button_danger_pressed": ("UI002", "危险按钮·按下", "9-slice 圆角26（导出2x）"),
    "ui_button_danger_disabled": ("UI002", "危险按钮·禁用", "9-slice 圆角26（导出2x）"),
    "ui_card_normal": ("UI003", "卡片·常态", "9-slice 圆角26（导出2x）"),
    "ui_card_selected": ("UI003", "卡片·选中", "9-slice 圆角26（导出2x）"),
    "ui_card_locked": ("UI003", "卡片·锁定", "9-slice 圆角26（导出2x）"),
    "ui_card_empty": ("UI003", "卡片·空态（细描边）", "9-slice 圆角26（导出2x）"),
}


def export(name, prims, size, space):
    if isinstance(size, int):
        size = (size, size)
    render(prims, size, space).save(os.path.join(PNG_DIR, name + ".png"))
    with open(os.path.join(SVG_DIR, name + ".svg"), "w", encoding="utf-8") as f:
        f.write(svg_doc(prims, space))
    return name


ICONS = [
    ("ui_icon_coin", icon_coin(), 128), ("ui_icon_growth", icon_growth(), 128),
    ("ui_icon_refresh", icon_refresh(), 128), ("ui_icon_position", icon_position(), 128),
    ("ui_icon_pause", icon_pause(), 128), ("ui_icon_play", icon_play(), 128),
    ("ui_icon_back", icon_back(), 128), ("ui_icon_close", icon_close(), 128),
    ("ui_icon_lock", icon_lock(), 128), ("ui_icon_check", icon_check(), 128),
    ("ui_icon_chevron", icon_chevron(), 128), ("ui_icon_info", icon_info(), 128),
    ("ui_map_room_start", room_start(), 256), ("ui_map_room_battle", room_battle(), 256),
    ("ui_map_room_shop", room_shop(), 256), ("ui_map_room_empty", room_empty(), 256),
    ("ui_map_room_final", room_final(), 256),
    ("ui_talent_symbol_reward", symbol_reward(), 128), ("ui_talent_symbol_amplify", symbol_amplify(), 128),
    ("ui_talent_symbol_floor", symbol_floor(), 128), ("ui_talent_symbol_upgrade", symbol_upgrade(), 128),
    ("ui_brand_emblem_512", brand_emblem(), 512), ("ui_brand_mark_128", brand_mark(), 128),
    ("ui_brand_result_256", brand_result(), 256),
]

COMPONENTS = [
    ("ui_button_primary_normal", comp(INK, INK)),
    ("ui_button_primary_hover", comp((246, 248, 245, 255), (246, 248, 245, 255))),
    ("ui_button_primary_pressed", comp((173, 199, 204, 255), (173, 199, 204, 255))),
    ("ui_button_primary_disabled", comp((102, 117, 125, 230), (102, 117, 125, 230))),
    ("ui_button_secondary_normal", comp(CARD, ACCENT)),
    ("ui_button_secondary_hover", comp(CARD_HOVER, ACCENT)),
    ("ui_button_secondary_pressed", comp(CARD_PRESS, ACCENT)),
    ("ui_button_secondary_disabled", comp(CARD_OFF, LINE_OFF)),
    ("ui_button_danger_normal", comp(DANGER, DANGER)),
    ("ui_button_danger_hover", comp((215, 150, 144, 255), (215, 150, 144, 255))),
    ("ui_button_danger_pressed", comp((160, 105, 100, 255), (160, 105, 100, 255))),
    ("ui_button_danger_disabled", comp((90, 70, 68, 220), (90, 70, 68, 220))),
    ("ui_card_normal", comp(CARD, LINE)),
    ("ui_card_selected", comp(CARD, ACCENT, 6.0)),
    ("ui_card_locked", comp((19, 25, 32, 235), (60, 74, 82, 255))),
    ("ui_card_empty", [RR(3, 3, BW - 6, BH - 6, 26, "stroke", 2.0, LINE_OFF)]),
]


def main():
    os.makedirs(PNG_DIR, exist_ok=True)
    os.makedirs(SVG_DIR, exist_ok=True)
    entries = []
    for name, prims, size in ICONS:
        entries.append((export(name, prims, size, SPACE_ICON), (size, size)))
    for name, prims in COMPONENTS:
        entries.append((export(name, prims, (int(BW * 2), int(BH * 2)), SPACE_COMP), (int(BW * 2), int(BH * 2))))

    verify(entries)
    contact_sheets([n for n, _s in entries if n.startswith(("ui_icon", "ui_map", "ui_talent", "ui_brand"))],
                   [n for n, _s in entries if n.startswith(("ui_button", "ui_card"))])
    write_review_table(entries)
    print("完成：%d 件资源 -> Assets/Art/ui（矢量源 art_tools/source）" % len(entries))


def verify(entries):
    import xml.etree.ElementTree as ET

    print("自检：")
    bad = []
    for name, size in entries:
        im = Image.open(os.path.join(PNG_DIR, name + ".png")).convert("RGBA")
        alpha = im.getchannel("A")
        bbox = alpha.getbbox()
        peak = alpha.getextrema()[1]
        ok = im.size == size and bbox is not None and peak >= 40
        if not ok:
            bad.append(name)
        print("  %s %-32s %4dx%-4d 峰值alpha=%3d 内容框=%s"
              % ("OK " if ok else "!! ", name, im.size[0], im.size[1], peak, bbox))
        ET.parse(os.path.join(SVG_DIR, name + ".svg"))
    print("  SVG：%d 个全部可解析" % len(entries))
    print("  结论：%s" % ("全部通过" if not bad else "异常项 " + ", ".join(bad)))


def _font(size):
    from PIL import ImageFont
    try:
        return ImageFont.truetype(FONT_PATH, size)
    except Exception:
        return ImageFont.load_default()


def contact_sheets(icon_names, card_names):
    os.makedirs(REVIEW_DIR, exist_ok=True)
    font = _font(18)
    cols, cell = 6, 200
    rows = (len(icon_names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 30)), (16, 26, 34))
    d = ImageDraw.Draw(sheet)
    for i, name in enumerate(icon_names):
        im = Image.open(os.path.join(PNG_DIR, name + ".png")).convert("RGBA")
        box = 128 if im.size[0] <= 160 else 180
        im = im.resize((box, box), Image.LANCZOS)
        sheet.paste(im, ((i % cols) * cell + (cell - box) // 2,
                         (i // cols) * (cell + 30) + (cell - box) // 2), im)
        d.text(((i % cols) * cell + 10, (i // cols) * (cell + 30) + cell + 4), name, font=font, fill=INK)
    sheet.save(os.path.join(REVIEW_DIR, "sheet_icons_and_marks.png"))

    cols, cell = 2, 460
    rows = (len(card_names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 20)), (16, 26, 34))
    d = ImageDraw.Draw(sheet)
    for i, name in enumerate(card_names):
        im = Image.open(os.path.join(PNG_DIR, name + ".png")).convert("RGBA")
        w, h = 384, 192
        sheet.paste(im, ((i % cols) * cell + (cell - w) // 2,
                         (i // cols) * (cell + 20) + (cell - h) // 2), im)
        d.text(((i % cols) * cell + 20, (i // cols) * (cell + 20) + cell), name, font=font, fill=INK)
    sheet.save(os.path.join(REVIEW_DIR, "sheet_components.png"))
    print("拼版：art_tools/review/sheet_icons_and_marks.png, sheet_components.png")


def write_review_table(entries):
    lines = ["# FallenAngel 美术交付对照表（自动生成）", "",
             "生成：`python -X utf8 art_tools/export_ui_art.py`。图标与标识为**方向 A｜圆角几何（第 2 版）**，",
             "几何与游戏内 DeepSeaGraphic 同源；按钮/卡片组件本轮未改（仍为切角）。", "",
             "| 需求项 | 资源 | 运行时 PNG | 矢量源 SVG | 尺寸 | 说明 |",
             "| --- | --- | --- | --- | --- | --- |"]
    order = {"UI005": 0, "MAP001": 1, "TAL002": 2, "BR001": 3, "UI002": 4, "UI003": 5}
    for name, size in sorted(entries, key=lambda e: (order.get(META[e[0]][0], 9), e[0])):
        need, use, note = META[name]
        lines.append("| %s | %s | `Assets/Art/ui/%s.png` | `art_tools/source/%s.svg` | %d×%d | %s |"
                     % (need, use, name, name, size[0], size[1], note))
    lines += ["", "## 说明",
              "- 图标单色（#E5E9E4），运行时按状态着色；方向 A 线宽 6.5 / 128 栅格，圆头圆角。",
              "- TAL002 的「利息」复用 `ui_icon_coin`、「商店」复用 `ui_map_room_shop`、「路线」复用 `ui_icon_position`。",
              "- 组件（UI002/UI003）导出 2 倍图，Unity 9-slice 边框各边 60px（切角 30 逻辑单位）。",
              "- 未交付：BG001/BG002/BG003、EQ001（位图插画，待定出图路径）。"]
    with open(os.path.join(REVIEW_DIR, "art_delivery_review.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("对照表：art_tools/review/art_delivery_review.md")


if __name__ == "__main__":
    main()
