#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""音符「近大远小」假透视效果图（过稿用，不进游戏）。

背景（2026-09-20，参考 OpenSekai 复刻 Project Sekai 的音符渲染）：
原作音符的位置与**缩放**由同一个非线性进度驱动——
    viewProgress = 1.06 ** ((min(p, 2) - 1) * 45)
    y     = lerp(spawnY, judgeY, viewProgress)      # LerpUnclamped，过线后继续飞
    scale = viewProgress                             # 音符边下落边变大
于是音符从上方"一个点"出发、逐渐放大到满尺寸，过判定线后继续变大飞出，
视觉上就是**近大远小**。我们目前是线性下落 + 恒定尺寸（Note.UpdatePosition）。

本脚本只画对比图，不读也不写游戏资源。几何取自本工程真实常量：
    画布 1080×1920、判定线距底 400、生成点距底 1560（spawnY=600 中心系）、
    4 键轨宽 234、轨中心 ±390/±130、tap 217×28。

输出：
    out/perspective_compare.png   左=现状（线性等大） 右=提案（指数近大远小），同一时刻一屏音符
    out/perspective_timeline.png  同一个音符在 6 个时刻的位置与大小（上=现状 下=提案）

运行：python -X utf8 art_tools/note_perspective/preview_perspective.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
JUDGE_FROM_BOTTOM = 400.0
CANVAS_H = 1920.0

SPAWN_CENTER_Y = 600.0                     # NoteSpawner.spawnY（画布中心系）
JUDGE_CENTER_Y = JUDGE_FROM_BOTTOM - CANVAS_H / 2.0   # -560

LANE_WIDTH = 234.0
LANE_CENTERS = [-390.0, -130.0, 130.0, 390.0]         # 4 键
NOTE_W, NOTE_H = 217.0, 28.0

BG = (10, 20, 26)
LANE_FILL = (18, 34, 40)
LANE_EDGE = (40, 70, 78)
JUDGE_COLOR = (207, 239, 234)
TEXT = (150, 180, 186)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176)]
ACCENT = (111, 224, 214)


def center_to_screen(x_center, y_center):
    """画布中心系 → 图像坐标（左上原点）。"""
    return W / 2.0 + x_center, H / 2.0 - y_center


def view_progress(p):
    """OpenSekai 口径的原始非线性进度（1.06^((min(p,2)-1)*45)）。"""
    return 1.06 ** ((min(p, 2.0) - 1.0) * 45.0)


MIN_SCALE = 0.35
_V0 = 1.06 ** -45.0            # p=0 处的原值，用于把曲线归一到 [0,1]


def view_progress01(p):
    """归一化曲线：p=0 → 0（生成点）、p=1 → 1（判定线）。与 C# 的 PlayPerspective.ViewProgress01 一致。"""
    return max(0.0, min(1.0, (view_progress(p) - _V0) / (1.0 - _V0)))


def size_scale(p):
    """尺寸：归一化曲线 + 0.35 下限（**只作用于尺寸**，位置仍走曲线）。与 C# SizeScale 一致。"""
    return max(MIN_SCALE, view_progress01(p))


def position_linear(p):
    """旧版：线性位置。"""
    t = max(0.0, min(1.0, p))
    return SPAWN_CENTER_Y + (JUDGE_CENTER_Y - SPAWN_CENTER_Y) * t


def position_perspective(p):
    """现在实现：归一化非线性进度 + LerpUnclamped（过线后继续飞）。"""
    v = view_progress01(p)
    return SPAWN_CENTER_Y + (JUDGE_CENTER_Y - SPAWN_CENTER_Y) * v


def load_font(size):
    for name in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def draw_stage(draw, x_offset, title, subtitle, note_positions, font_title, font_small, font_note,
               perspective_lanes):
    """画一个 1080×1920 的舞台，note_positions = [(x_center, y_center, scale, color, label)]。
    perspective_lanes=True 时轨道画成与音符同曲线的梯形（严格对齐）；False 时是关键帧里的矩形轨道。"""
    # 背景
    draw.rectangle([x_offset, 0, x_offset + W, H], fill=BG)

    top_y = H / 2.0 - SPAWN_CENTER_Y
    judge_y = H / 2.0 - JUDGE_CENTER_Y
    for i, cx in enumerate(LANE_CENTERS):
        if perspective_lanes:
            # 与音符同一曲线逐段采样：任意高度上轨道半宽 == 该处音符半宽 ÷ 填充比
            pts_left, pts_right = [], []
            for k in range(33):
                p = k / 32.0
                y = H / 2.0 - position_perspective(p)
                half = LANE_WIDTH / 2.0 * size_scale(p)
                pts_left.append((x_offset + W / 2.0 + cx - half, y))
                pts_right.append((x_offset + W / 2.0 + cx + half, y))
            poly = pts_left + list(reversed(pts_right))
        else:
            poly = [(x_offset + W / 2.0 + cx - LANE_WIDTH / 2.0, top_y),
                    (x_offset + W / 2.0 + cx + LANE_WIDTH / 2.0, top_y),
                    (x_offset + W / 2.0 + cx + LANE_WIDTH / 2.0, judge_y),
                    (x_offset + W / 2.0 + cx - LANE_WIDTH / 2.0, judge_y)]
        draw.polygon(poly, fill=LANE_FILL, outline=LANE_EDGE)

    # 判定线
    draw.line([(x_offset + 0, H / 2.0 - JUDGE_CENTER_Y), (x_offset + W, H / 2.0 - JUDGE_CENTER_Y)],
              fill=JUDGE_COLOR, width=6)
    draw.text((x_offset + 24, H / 2.0 - JUDGE_CENTER_Y + 10),
              "判定线  距底 %dpx" % JUDGE_FROM_BOTTOM, fill=TEXT, font=font_small)

    # 音符
    for (cx, cy, scale, color, label) in note_positions:
        x, y = center_to_screen(cx, cy)
        w = max(6.0, NOTE_W * scale)
        h = max(3.0, NOTE_H * scale)
        box = [x_offset + x - w / 2, y - h / 2, x_offset + x + w / 2, y + h / 2]
        radius = max(2.0, h / 2)
        draw.rounded_rectangle(box, radius=radius, fill=color)
        if label:
            draw.text((x_offset + x - w / 2, y - h / 2 - 26), label, fill=TEXT, font=font_note)

    # 标题
    draw.text((x_offset + 24, 28), title, fill=JUDGE_COLOR, font=font_title)
    draw.text((x_offset + 24, 78), subtitle, fill=TEXT, font=font_small)


def render_compare(out_dir):
    font_title = load_font(40)
    font_small = load_font(24)
    font_note = load_font(20)

    img = Image.new("RGB", (W * 2, H), BG)
    draw = ImageDraw.Draw(img)

    # 同一时刻的 5 个音符：p = 0.2 / 0.4 / 0.6 / 0.8 / 1.0，分布在 4 条轨上
    samples = [(0.2, 0, 0), (0.4, 1, 1), (0.6, 2, 2), (0.8, 3, 3), (1.0, 1, 1)]

    left = []
    right = []
    for (p, lane_idx, color_idx) in samples:
        label = "p=%.1f" % p
        left.append((LANE_CENTERS[lane_idx], position_linear(p), 0.55 + 0.45 * p,
                     LANE_COLORS[color_idx], label))
        v = size_scale(p)
        right.append((LANE_CENTERS[lane_idx], position_perspective(p), v,
                      LANE_COLORS[color_idx], label))

    draw_stage(draw, 0, "旧版：线性位置 + 线性缩放 0.55→1",
               "矩形轨道配缩小音符 → 远处音符明显比轨道窄（不对齐）",
               left, font_title, font_small, font_note, perspective_lanes=False)
    draw_stage(draw, W, "现在：归一化曲线 + 下限 0.35 + 梯形轨道",
               "轨道与音符同源同曲线 → 任意高度严格对齐（作者选型 B）",
               right, font_title, font_small, font_note, perspective_lanes=True)

    path = os.path.join(out_dir, "perspective_compare.png")
    img.save(path)
    return path


def render_timeline(out_dir):
    font_title = load_font(36)
    font_small = load_font(24)
    font_note = load_font(20)

    img = Image.new("RGB", (W, H), BG)
    draw = ImageDraw.Draw(img)
    draw.rectangle([0, 0, W, H], fill=BG)

    judge_y = H / 2.0 - JUDGE_CENTER_Y
    draw.line([(0, judge_y), (W, judge_y)], fill=JUDGE_COLOR, width=6)

    # 三列：现状 / 提案 A（原作口径）/ 提案 B（带最小尺寸下限）
    columns = [
        (W * 1 / 6.0, "旧版（线性）", lambda p: (position_linear(p), 0.55 + 0.45 * p), LANE_COLORS[1]),
        (W * 3 / 6.0, "原作曲线（无下限）", lambda p: (position_perspective(p), view_progress01(p)), LANE_COLORS[2]),
        (W * 5 / 6.0, "现在（曲线+0.35 下限）", lambda p: (position_perspective(p), size_scale(p)), LANE_COLORS[3]),
    ]
    steps = [0.1, 0.25, 0.4, 0.55, 0.7, 0.85, 1.0]

    for x_frac, title, fn, color in columns:
        x = W * x_frac
        draw.text((x - 60, 150), title, fill=JUDGE_COLOR, font=font_small)
        for p in steps:
            y_center, scale = fn(p)
            img_note(draw, x, H / 2.0 - y_center, scale, color, "p=%.2f ×%.2f" % (p, scale), font_note)

    draw.text((24, 24), "同一个音符的 7 个时刻：旧版 / 原作曲线 / 现在", fill=JUDGE_COLOR, font=font_title)
    draw.text((24, 72), "现在 = 曲线归一到 [0,1] 后再设 35% 下限：出生点回到屏幕顶部，远处也看得清",
              fill=TEXT, font=font_small)

    path = os.path.join(out_dir, "perspective_timeline.png")
    img.save(path)
    return path




def img_note(draw, x, y, scale, color, label, font):
    w = max(6.0, NOTE_W * scale)
    h = max(3.0, NOTE_H * scale)
    box = [x - w / 2, y - h / 2, x + w / 2, y + h / 2]
    draw.rounded_rectangle(box, radius=max(2.0, h / 2), fill=color)
    draw.text((x + w / 2 + 10, y - 12), label, fill=TEXT, font=font)


def main():
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
    os.makedirs(out_dir, exist_ok=True)
    for path in (render_compare(out_dir), render_timeline(out_dir)):
        print("wrote", path)


if __name__ == "__main__":
    main()
