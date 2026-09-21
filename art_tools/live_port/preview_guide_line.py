#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""滑条「引导线」对比稿（过稿用，**不进游戏**）。

背景（2026-09-21 判定簇移植后）：
滑条（Slide）的判定跨度现在由移植层沿"头 → 路径点 → 尾"推进（LiveUtility.CalcExcuteNoteLane），
手指必须落在当前跨度内才算跟得住。玩家侧要能**看见**"线在哪、我该跟到哪、跟丢没有"——
这就是引导线的职责。本脚本只出效果图给作者选型，不动 Assets/ 里的任何视觉。

四个方案（同一段滑条、同一时刻）：
  V0 现状：等宽轨迹带（Note.ConfigureSlidePath 的分段带，宽度=音符宽）
  V1 细引导线 + 节点：一条细线沿路径 + 每个路径点一个小方节点（未走过实线、走过淡出）
  V2 双线夹轨：两条细线夹住"当前判定跨度"的宽度（PJSK friction 的读法），头部圆点
  V3 渐隐带 + 移动高亮：半透明带子 + 手指所在处一段高亮（高亮 = 当前判定跨度的位置）

另外画出"掉段"状态（手指离开跨度 > 0.25s）：节点转告警色、连击断开（只断这一段）。

输出（art_tools/review/guide_line/）：
  guide_line_compare.png   2×2 对比板（每格 1080×1920 等比缩小）
  guide_line_v0..v3.png    四个方案的原尺寸效果图

运行：python -X utf8 art_tools/live_port/preview_guide_line.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

# ── 与游戏一致的几何（Core/LaneLayout、Core/PlayVisualSpec、NoteSpawner）──
W, H = 1080, 1920
CANVAS_H = 1920.0
JUDGE_CENTER_Y = 400.0 - CANVAS_H / 2.0          # 判定线：距底 400 → 中心系 -560
SPAWN_CENTER_Y = 600.0                            # 生成点（中心系）
LANE_WIDTH = 234.0
LANE_CENTERS = [-390.0, -130.0, 130.0, 390.0]     # 4 键
NOTE_W, NOTE_H = 217.0, 28.0

BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
LANE_EDGE = (34, 58, 66)
JUDGE_COLOR = (207, 239, 234)
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176)]

SLIDE_BAND = (96, 150, 152)
SLIDE_LINE = (168, 226, 220)
SLIDE_NODE = (207, 239, 234)
SLIDE_DONE = (86, 112, 116)
HIGHLIGHT = (111, 224, 214)
WARN = (232, 126, 108)

# 一段滑条：起点头部在轨 1（x=-130），经轨 2，落到轨 3（x=390）
PATH = [(-130.0, 0.00), (-60.0, 0.22), (60.0, 0.42), (180.0, 0.62), (320.0, 0.82), (390.0, 1.00)]
PROGRESS = 0.45                                    # 画的这一时刻：已滑到 45%


def font(size):
    for n in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def screen(x_center, y_center):
    """画布中心系 → 图像坐标（左上原点）。"""
    return W / 2.0 + x_center, H / 2.0 - y_center


def path_point(t):
    """t∈[0,1] → (x, y)（画布中心系）。y 自头部向上展开（与 Note.UpdateSlideRibbon 同向）。"""
    span = SPAWN_CENTER_Y - JUDGE_CENTER_Y
    for i in range(len(PATH) - 1):
        (x0, t0), (x1, t1) = PATH[i], PATH[i + 1]
        if t0 <= t <= t1:
            k = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
            x = x0 + (x1 - x0) * k
            y = JUDGE_CENTER_Y + span * t
            return x, y
    return PATH[-1][0], JUDGE_CENTER_Y + span


def lerp(a, b, k):
    return a + (b - a) * k


def blend(c0, c1, k):
    return tuple(int(round(lerp(c0[i], c1[i], k))) for i in range(3))


def draw_lanes(draw, ox=0, oy=0):
    for idx, cx in enumerate(LANE_CENTERS):
        x0, _ = screen(cx - LANE_WIDTH / 2.0, JUDGE_CENTER_Y)
        x1, _ = screen(cx + LANE_WIDTH / 2.0, JUDGE_CENTER_Y)
        y_top, y_bottom = oy, oy + H
        draw.rectangle([ox + x0, y_top, ox + x1, y_bottom], fill=LANE_FILL, outline=LANE_EDGE)
    _, jy = screen(0, JUDGE_CENTER_Y)
    draw.rectangle([ox, oy + jy - 3, ox + W, oy + jy + 3], fill=JUDGE_COLOR)


def draw_label(draw, ox, oy, title, subtitle, warn=False):
    f_title, f_sub = font(40), font(24)
    draw.text((ox + 28, oy + 26), title, font=f_title, fill=TEXT_BRIGHT)
    draw.text((ox + 28, oy + 78), subtitle, font=f_sub, fill=WARN if warn else TEXT)


def draw_head(draw, ox, oy, x, y, radius=NOTE_W / 2.0):
    px, py = screen(x, y)
    # 头部：贴合轨道宽的圆角条（与 tap 同宽）
    draw.rounded_rectangle(
        [ox + px - radius, oy + py - NOTE_H / 2.0, ox + px + radius, oy + py + NOTE_H / 2.0],
        radius=NOTE_H / 2.0, fill=SLIDE_NODE)


def draw_tail(draw, ox, oy):
    x, y = PATH[-1][0], JUDGE_CENTER_Y + (SPAWN_CENTER_Y - JUDGE_CENTER_Y)
    px, py = screen(x, y)
    draw.rounded_rectangle([ox + px - 60, oy + py - 10, ox + px + 60, oy + py + 10],
                           radius=10, outline=SLIDE_NODE, width=4)


def render_v0(draw, ox, oy):
    """V0 现状：等宽轨迹带（宽度 = 音符宽），整条一起画。"""
    pts = [screen(*path_point(i / 40.0)) for i in range(41)]
    band = []
    for (x, y) in pts:
        band.append((ox + x - NOTE_W / 2.0, oy + y))
    for (x, y) in reversed(pts):
        band.append((ox + x + NOTE_W / 2.0, oy + y))
    draw.polygon(band, fill=blend(BG, SLIDE_BAND, 0.55))
    draw.line([(ox + x, oy + y) for (x, y) in pts], fill=SLIDE_BAND, width=3)
    draw_head(draw, ox, oy, *path_point(0.0))
    draw_tail(draw, ox, oy)


def render_v1(draw, ox, oy):
    """V1 细引导线 + 节点：走过的那段淡出，未走过的实线；每个路径点一个小节点。"""
    pts = [screen(*path_point(i / 40.0)) for i in range(41)]
    for i in range(len(pts) - 1):
        t = (i + 1) / 40.0
        color = SLIDE_DONE if t <= PROGRESS else SLIDE_LINE
        width = 5 if t > PROGRESS else 3
        draw.line([(ox + pts[i][0], oy + pts[i][1]), (ox + pts[i + 1][0], oy + pts[i + 1][1])],
                  fill=color, width=width)
    for (x, t) in PATH:
        px, py = screen(x, JUDGE_CENTER_Y + (SPAWN_CENTER_Y - JUDGE_CENTER_Y) * t)
        color = SLIDE_DONE if t < PROGRESS else SLIDE_NODE
        draw.rectangle([ox + px - 7, oy + py - 7, ox + px + 7, oy + py + 7], fill=color)
    draw_head(draw, ox, oy, *path_point(0.0))
    draw_tail(draw, ox, oy)


def render_v2(draw, ox, oy):
    """V2 双线夹轨：两条细线夹住"当前判定跨度"的宽度（跨度为 1 轨宽），头部圆点。"""
    for side in (-1.0, 1.0):
        pts = []
        for i in range(41):
            x, y = path_point(i / 40.0)
            px, py = screen(x + side * LANE_WIDTH / 2.0, y)
            pts.append((ox + px, oy + py))
        draw.line(pts, fill=SLIDE_LINE, width=4)
    # 未走过的部分在半透明填充，跟手范围一眼可读
    pts = [path_point(i / 40.0) for i in range(41)]
    poly = []
    for (x, y) in pts:
        px, py = screen(x - LANE_WIDTH / 2.0, y)
        poly.append((ox + px, oy + py))
    for (x, y) in reversed(pts):
        px, py = screen(x + LANE_WIDTH / 2.0, y)
        poly.append((ox + px, oy + py))
    draw.polygon(poly, fill=blend(BG, SLIDE_BAND, 0.22))
    draw_head(draw, ox, oy, *path_point(0.0))
    draw_tail(draw, ox, oy)


def render_v3(draw, ox, oy):
    """V3 渐隐带 + 移动高亮：带子半透明，当前跨度位置一段高亮。"""
    pts = [path_point(i / 40.0) for i in range(41)]
    for i in range(len(pts) - 1):
        x0, y0 = pts[i]
        x1, y1 = pts[i + 1]
        t = (i + 1) / 40.0
        a = max(0.06, 0.42 * (1.0 - t))
        poly = [screen(x0 - NOTE_W / 2.0, y0), screen(x1 - NOTE_W / 2.0, y1),
                screen(x1 + NOTE_W / 2.0, y1), screen(x0 + NOTE_W / 2.0, y0)]
        draw.polygon([(ox + px, oy + py) for (px, py) in poly], fill=blend(BG, SLIDE_BAND, a))
    # 高处渐隐 + 指尖高亮段
    px, py = screen(*path_point(PROGRESS))
    hx, hy = screen(*path_point(max(0.0, PROGRESS - 0.06)))
    draw.line([(ox + hx, oy + hy), (ox + px, oy + py)], fill=HIGHLIGHT, width=16)
    draw.ellipse([ox + px - 22, oy + py - 22, ox + px + 22, oy + py + 22],
                 outline=HIGHLIGHT, width=6)
    draw_head(draw, ox, oy, *path_point(0.0))
    draw_tail(draw, ox, oy)


def render_dropped(draw, ox, oy):
    """掉段状态（V1 方案 + 告警色）：手指离开跨度 > 0.25s → 节点转告警色、只断这一段。"""
    pts = [screen(*path_point(i / 40.0)) for i in range(41)]
    for i in range(len(pts) - 1):
        t = (i + 1) / 40.0
        draw.line([(ox + pts[i][0], oy + pts[i][1]), (ox + pts[i + 1][0], oy + pts[i + 1][1])],
                  fill=WARN if 0.30 < t < 0.62 else SLIDE_LINE, width=5)
    for (x, t) in PATH:
        px, py = screen(x, JUDGE_CENTER_Y + (SPAWN_CENTER_Y - JUDGE_CENTER_Y) * t)
        broken = 0.30 < t < 0.62
        draw.rectangle([ox + px - 8, oy + py - 8, ox + px + 8, oy + py + 8],
                       fill=WARN if broken else SLIDE_NODE)
    draw_head(draw, ox, oy, *path_point(0.0))
    draw_tail(draw, ox, oy)
    fx, fy = screen(*path_point(0.45))
    fx += 150.0
    draw.line([(ox + fx - 26, oy + fy - 26), (ox + fx + 26, oy + fy + 26)], fill=WARN, width=6)
    draw.line([(ox + fx - 26, oy + fy + 26), (ox + fx + 26, oy + fy - 26)], fill=WARN, width=6)


def render_panel(kind, title, subtitle, warn=False):
    img = Image.new("RGB", (W, H), BG)
    draw = ImageDraw.Draw(img)
    draw_lanes(draw)
    if kind == "v0":
        render_v0(draw, 0, 0)
    elif kind == "v1":
        render_v1(draw, 0, 0)
    elif kind == "v2":
        render_v2(draw, 0, 0)
    elif kind == "v3":
        render_v3(draw, 0, 0)
    else:
        render_dropped(draw, 0, 0)
    draw_label(draw, 0, 0, title, subtitle, warn)
    return img


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.abspath(os.path.join(here, "..", "review", "guide_line"))
    os.makedirs(out_dir, exist_ok=True)

    panels = [
        render_panel("v0", "V0 现状：等宽轨迹带",
                     "带宽 = 音符宽 → 看不出「跟到哪」，也看不出判定跨度"),
        render_panel("v1", "V1 细引导线 + 节点",
                     "细线沿路径 + 路径点节点：走过淡出、未走实线（改动最小）"),
        render_panel("v2", "V2 双线夹轨（判定跨度可视化）",
                     "两条细线夹住当前跨度宽度 → 与判定语义一一对应"),
        render_panel("v3", "V3 渐隐带 + 移动高亮",
                     "带子随高度渐隐，手指处一段高亮（高亮位置 = 当前跨度）"),
    ]
    dropped = render_panel("drop", "掉段（任一方案共用）：节点转告警色",
                           "手指离开跨度 > 0.25s → 只断这一段 combo，不断整条", warn=True)

    for kind, img in zip(("v0", "v1", "v2", "v3"), panels):
        img.save(os.path.join(out_dir, "guide_line_%s.png" % kind))
    dropped.save(os.path.join(out_dir, "guide_line_dropped.png"))

    # 2×2 对比板 + 掉段行
    scale = 0.62
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 2 + 60, ph * 3 + 140), (6, 12, 16))
    draw = ImageDraw.Draw(sheet)
    f_head, f_note = font(40), font(24)
    draw.text((30, 24), "FallenAngel · 滑条引导线对比稿（过稿用，不进游戏）", font=f_head, fill=TEXT_BRIGHT)
    draw.text((30, 76), "同一段滑条、同一时刻（已滑 45%）；几何取自真实常量：1080×1920 / 判定线距底 400 / 4 键轨宽 234",
              font=f_note, fill=TEXT)
    for i, img in enumerate(panels + [dropped]):
        col, row = i % 2, i // 2
        x = 20 + col * (pw + 20)
        y = 120 + row * (ph + 20)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, y))
        draw.rectangle([x, y, x + pw, y + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(out_dir, "guide_line_compare.png"))
    print("已输出：", out_dir)


if __name__ == "__main__":
    main()
