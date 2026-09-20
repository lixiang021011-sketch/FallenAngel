#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""长按/滑条「分段带」效果图（过稿用，不进游戏）。

现状（Note.cs / NotePartChecks 的几何）：
  · 长按身体 = 一个矩形 Image，按住时用 localScale.y 收缩，宽度恒等于音符宽；
  · 滑条 = 沿路径的等宽条带。
两种都是**等宽带**，而轨道现在是上窄下宽的梯形——带子会明显撑出轨道边界。

提案：把「身体 / 轨迹」改成**多段网格带**（OpenSekai 的 NoteLineView 做法：一段 mesh、100 段采样）：
  · 沿身体/路径逐段采样，每一段的半宽 = 轨道半宽 × 填充比 × 该处透视缩放；
  · 与音符、轨道共用 PlayPerspective，所以带子任意高度都不会越出轨道；
  · 段数 32 段（1080 宽下每段约 70px）足够平滑，还能表达滑条曲线。

输出：out/hold_band_compare.png
运行：python -X utf8 art_tools/note_perspective/preview_hold_band.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
SPAWN_CENTER_Y, JUDGE_CENTER_Y = 600.0, -560.0
LANE_WIDTH, NOTE_FILL = 234.0, 1.0
BG, LANE_FILL, LANE_EDGE = (10, 20, 26), (18, 34, 40), (40, 70, 78)
JUDGE_COLOR, TEXT = (207, 239, 234), (150, 180, 186)
BAND = (111, 176, 176)
BAND_OLD = (90, 120, 126)
HEAD = (207, 239, 234)

_V0 = 1.06 ** -45.0


def vp01(p):
    v = 1.06 ** ((min(p, 2.0) - 1.0) * 45.0)
    return max(0.0, min(1.0, (v - _V0) / (1.0 - _V0)))


def size_scale(p):
    return max(0.35, vp01(p))


def y_at(p):
    return SPAWN_CENTER_Y + (JUDGE_CENTER_Y - SPAWN_CENTER_Y) * vp01(p)


def screen(x_center, y_center):
    return W / 2.0 + x_center, H / 2.0 - y_center


def font(size):
    for n in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def draw_lane(draw, cx):
    pts_l, pts_r = [], []
    for k in range(33):
        p = k / 32.0
        x, y = screen(cx, y_at(p))
        half = LANE_WIDTH / 2.0 * size_scale(p)
        pts_l.append((x - half, y))
        pts_r.append((x + half, y))
    draw.polygon(pts_l + list(reversed(pts_r)), fill=LANE_FILL, outline=LANE_EDGE)
    x, yj = screen(cx, JUDGE_CENTER_Y)
    draw.line([(0, yj), (W, yj)], fill=JUDGE_COLOR, width=6)


def band_old(draw, cx, p_head, p_tail, color):
    """现状：等宽带（宽度 = 音符宽，不随高度变化）。"""
    x, y0 = screen(cx, y_at(p_head))
    _, y1 = screen(cx, y_at(p_tail))
    half = LANE_WIDTH * NOTE_FILL / 2.0
    draw.rectangle([x - half, min(y0, y1), x + half, max(y0, y1)], fill=color)


def band_mesh(draw, cx, p_head, p_tail, color, segments=32):
    """提案：逐段采样、半宽随透视缩放的分段带（与 mesh 版同算法）。"""
    left, right = [], []
    for k in range(segments + 1):
        p = p_head + (p_tail - p_head) * k / segments
        x, y = screen(cx, y_at(p))
        half = LANE_WIDTH * NOTE_FILL / 2.0 * size_scale(p)
        left.append((x - half, y))
        right.append((x + half, y))
    draw.polygon(left + list(reversed(right)), fill=color)


def note_head(draw, cx, p, color):
    x, y = screen(cx, y_at(p))
    w = 217.0 * size_scale(p)
    h = 28.0 * size_scale(p)
    draw.rounded_rectangle([x - w / 2, y - h / 2, x + w / 2, y + h / 2], radius=max(2, h / 2), fill=color)


def render():
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    os.makedirs(out_dir, exist_ok=True)

    img = Image.new("RGB", (W * 2, H), BG)
    draw = ImageDraw.Draw(img)
    f_title, f_small = font(38), font(23)

    for panel, (title, sub) in enumerate((
        ("现状：等宽带", "带宽固定 = 音符宽 → 越靠上越撑出轨道"),
        ("提案：分段带（32 段）", "逐段半宽 = 轨道半宽 × 填充比 → 恒在轨道内"),
    )):
        ox = panel * W
        cx = -130.0                      # 画在第 2 轨
        draw.rectangle([ox, 0, ox + W, H], fill=BG)
        # 轨道（含梯形）
        pts_l, pts_r = [], []
        for k in range(33):
            p = k / 32.0
            x, y = screen(cx, y_at(p))
            half = LANE_WIDTH / 2.0 * size_scale(p)
            pts_l.append((ox + x - half, y))
            pts_r.append((ox + x + half, y))
        draw.polygon(pts_l + list(reversed(pts_r)), fill=LANE_FILL, outline=LANE_EDGE)
        _, yj = screen(cx, JUDGE_CENTER_Y)
        draw.line([(ox, yj), (ox + W, yj)], fill=JUDGE_COLOR, width=6)

        # 长按：头在 p=0.55，身体一直延伸到 p=0.95
        p_head, p_tail = 0.55, 0.95
        if panel == 0:
            band_old(draw, cx, p_head, p_tail, BAND_OLD)
        else:
            band_mesh(draw, cx, p_head, p_tail, BAND)
        note_head(draw, cx, p_head, HEAD)

        draw.text((ox + 24, 28), title, fill=JUDGE_COLOR, font=f_title)
        draw.text((ox + 24, 76), sub, fill=TEXT, font=f_small)
        draw.text((ox + 24, 110), "长按头 p=0.55 → 尾 p=0.95（同一时刻的两种画法）", fill=TEXT, font=f_small)

    path = os.path.join(out_dir, "hold_band_compare.png")
    img.save(path)
    print("wrote", path)

    # 量化：带子半宽 vs 轨道半宽（越界量应为 0）
    print()
    print("p     轨道半宽  现状带半宽  提案带半宽  现状越界  提案越界")
    for p in (0.55, 0.7, 0.85, 0.95):
        lane_half = LANE_WIDTH / 2 * size_scale(p)
        old_half = LANE_WIDTH * NOTE_FILL / 2
        new_half = LANE_WIDTH * NOTE_FILL / 2 * size_scale(p)
        print("%.2f  %8.1f  %10.1f  %10.1f  %8.1f  %8.1f"
              % (p, lane_half, old_half, new_half, old_half - lane_half, new_half - lane_half))


if __name__ == "__main__":
    render()
