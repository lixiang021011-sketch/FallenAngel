#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""双押「提示线」对比稿：现状 vs 修复后（过稿/存证用，**不进游戏**）。

作者反馈（2026-09-22）："双押提示线是断的"。

根因（读代码 + 算几何得出，不是猜）：
两个同刻音符的**中心距**是固定的（5 键相邻轨 = 208 画布单位），但音符本体被整体
做透视缩放 s（出生 0.35 → 判定线 1.0），所以屏幕上的**可视间隙** =
中心距 − (两边半宽之和) × s —— 出生附近约 140px、判定线上约 21px。
旧实现只在生成时算了一次连线（宽度 = 中心距 − 两个半宽 = 20.8 画布单位），
而连线是音符的子节点、跟着 s 一起缩放 → 整段下落里它都是"约 20.8×s 的小墩子"，
既碰不到自己、更碰不到伙伴 —— 看起来就是断的。

修复：连线几何改成**每帧按当前 s 重算**（先在画布单位里求跨度，再 ÷ s 落到局部空间），
两端各压进音符墨迹 0.3×音符高度（覆盖 flick 尖角 / 普通切角的内缩）。

输出（art_tools/review/simultaneous_link/）：
  link_before_after.png   3 个高度 × 2 个版本（tap+tap / tap+flick）对比板

运行：python -X utf8 art_tools/live_port/preview_simultaneous_link.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

# ── 与游戏一致的几何（Core/LaneLayout、Core/PlayPerspective、Data/NoteVisualConfig）──
LANE_COUNT = 5
USABLE_W = 1080.0 - 20.0 * 2.0
PITCH = USABLE_W / LANE_COUNT                     # 208
SIZE_SCALE = (PITCH * 0.9) / 130.0                # 1.44：配表宽 × 本值 = 画布宽
TAP_W = 130.0 * SIZE_SCALE                        # 187.2（铺满一条轨）
TAP_H = 28.0
DRAG_W = 94.0 * SIZE_SCALE
FLICK_W = 130.0 * SIZE_SCALE

MIN_SCALE = 0.35                                  # PlayPerspective.MinScale
LANE_A, LANE_B = 1, 2                             # 相邻两轨（双押最常见）
CENTER_A = (LANE_A - (LANE_COUNT - 1) / 2.0) * PITCH
CENTER_B = (LANE_B - (LANE_COUNT - 1) / 2.0) * PITCH

PROGRESS = [0.10, 0.55, 1.00]                     # 出生附近 / 中段 / 判定线
BAND_H = 250
HEADER_H = 96
W = 1080
H = HEADER_H + BAND_H * len(PROGRESS) * 2

BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
LANE_EDGE = (34, 58, 66)
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176), (86, 150, 150)]
BAD = (232, 126, 108)
GOOD = (126, 224, 176)


def font(size):
    for n in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def chamfer(draw, cx, cy, w, h, cut, fill, outline):
    """切角矩形（与 NoteShapeGeometry.ChamferRect 同形）"""
    hw, hh = w * 0.5, h * 0.5
    pts = [(cx - hw + cut, cy - hh), (cx + hw - cut, cy - hh), (cx + hw, cy - hh + cut),
           (cx + hw, cy + hh - cut), (cx + hw - cut, cy + hh), (cx - hw + cut, cy + hh),
           (cx - hw, cy + hh - cut), (cx - hw, cy - hh + cut)]
    draw.polygon(pts, fill=fill, outline=outline)
    draw.line(pts + [pts[0]], fill=outline, width=2)


def pointed(draw, cx, cy, w, h, fill, outline):
    """两端尖角六边形（flick，NoteShapeGeometry.PointedBar 同形）"""
    hw, hh = w * 0.5, h * 0.5
    tip = min(w * 0.12, hh)
    pts = [(cx - hw + tip, cy - hh), (cx + hw - tip, cy - hh), (cx + hw, cy),
           (cx + hw - tip, cy + hh), (cx - hw + tip, cy + hh), (cx - hw, cy)]
    draw.polygon(pts, fill=fill, outline=outline)
    draw.line(pts + [pts[0]], fill=outline, width=2)


def draw_band(draw, y0, variant, right_kind):
    """画一个"横切面"：左音符（tap）+ 右音符（tap 或 flick）+ 提示线"""
    p_scale = variant["scale"]
    left_w, left_h = TAP_W * p_scale, TAP_H * p_scale
    right_w = (TAP_W if right_kind == "tap" else FLICK_W) * p_scale
    right_h = TAP_H * p_scale
    cx_a, cx_b = 540 + CENTER_A, 540 + CENTER_B
    cy = y0

    # 轨道底（可选）与标尺
    for cx in (540 + (i - 2) * PITCH for i in range(LANE_COUNT)):
        draw.rectangle([cx - PITCH * 0.45, y0 - BAND_H * 0.5, cx + PITCH * 0.45, y0 + BAND_H * 0.5],
                       fill=LANE_FILL, outline=None)

    left_edge, right_edge = cx_a + left_w * 0.5, cx_b - right_w * 0.5
    gap = right_edge - left_edge
    overlap = max(1.0, TAP_H * 0.3 * p_scale)

    if variant["mode"] == "before":
        band_w = max(0.0, (CENTER_B - CENTER_A) - TAP_W) * p_scale   # 生成时算死的一次性宽度 × s
        band = [cx_a + (CENTER_B - CENTER_A) * 0.5 - band_w * 0.5,
                cx_a + (CENTER_B - CENTER_A) * 0.5 + band_w * 0.5]
    else:
        band = [left_edge - overlap, right_edge + overlap]

    band_h = max(6.0, TAP_H * 0.45) * p_scale
    draw.rounded_rectangle([band[0], cy - band_h * 0.5, band[1], cy + band_h * 0.5],
                           radius=band_h * 0.45, fill=(150, 200, 214, 255) if False else (96, 150, 152))

    chamfer(draw, cx_a, cy, left_w, left_h, max(2.0, left_h * 0.28), LANE_COLORS[LANE_A],
            tuple(int(c * 0.55) for c in LANE_COLORS[LANE_A]))
    if right_kind == "tap":
        chamfer(draw, cx_b, cy, right_w, right_h, max(2.0, right_h * 0.28), LANE_COLORS[LANE_B],
                tuple(int(c * 0.55) for c in LANE_COLORS[LANE_B]))
    else:
        pointed(draw, cx_b, cy, right_w, right_h, LANE_COLORS[LANE_B],
                tuple(int(c * 0.55) for c in LANE_COLORS[LANE_B]))

    # 可视间隙与"接上没有"的判据
    covered_left = band[0] <= left_edge - 1.0
    covered_right = band[1] >= right_edge + 1.0
    ok = covered_left and covered_right
    color = GOOD if ok else BAD
    draw.line([left_edge, cy + left_h * 0.5 + 16, right_edge, cy + left_h * 0.5 + 16], fill=color, width=2)
    for x in (left_edge, right_edge):
        draw.line([x, cy + left_h * 0.5 + 8, x, cy + left_h * 0.5 + 24], fill=color, width=2)
    draw.text((cx_a - 130, cy + left_h * 0.5 + 26),
              f"可视间隙 {gap:6.1f}px ｜ 连线跨度 {band[1] - band[0]:6.1f}px ｜ "
              + ("已接上两端 ✓" if ok else "碰不到音符 ✗（看起来是断的）"),
              fill=color, font=font(22))


def main():
    img = Image.new("RGB", (W, H), BG)
    draw = ImageDraw.Draw(img)

    draw.text((32, 22), "双押提示线：现状 vs 修复后（5 键 · 相邻两轨 · 同刻双押）",
              fill=TEXT_BRIGHT, font=font(30))
    draw.text((32, 60),
              "可视间隙 = 中心距(208) − 两边半宽之和 × 透视缩放 s；s 出生 0.35 → 判定线 1.0",
              fill=TEXT, font=font(20))

    y = HEADER_H
    for p in PROGRESS:
        s = max(MIN_SCALE, p)      # 该进度下的 SizeScale（归一化曲线的近似；判定线 = 1）
        for mode, right_kind in (("before", "tap"), ("after", "tap")):
            variant = {"mode": mode, "scale": s}
            label = ("现状（生成时算一次，连线固定 20.8 画布单位 ×s）"
                     if mode == "before" else "修复后（每帧按 s 重算 + 两端各压进音符 0.3h）")
            draw.text((32, y - 22), f"p={p:0.2f}  s={s:0.2f} ｜ {label}",
                      fill=TEXT_BRIGHT if mode == "after" else TEXT, font=font(22))
            draw_band(draw, y + 26, variant, "tap")
            y += BAND_H

        # 第二条：伙伴是 flick（尖角端内缩，旧实现即使算对宽度也会留缝）
        for mode in ("before", "after"):
            variant = {"mode": mode, "scale": s}
            draw.text((32, y - 22),
                      f"p={p:0.2f}  s={s:0.2f} ｜ {'现状' if mode == 'before' else '修复后'}"
                      f"（伙伴 = flick，尖角端内缩 tip≈{min(FLICK_W * 0.12, TAP_H * 0.5):0.1f} 画布单位）",
                      fill=TEXT_BRIGHT if mode == "after" else TEXT, font=font(22))
            draw_band(draw, y + 26, variant, "flick")
            y += BAND_H

    out_dir = os.path.join(os.path.dirname(__file__), "..", "review", "simultaneous_link")
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "link_before_after.png")
    img.save(out)
    print("写出：" + out + f"  ({W}×{H})")

    # 文字结论（给报告直接引用）
    print("\n几何数字（tap+tap·相邻轨，中心距 208 画布单位）：")
    for p in PROGRESS:
        s = max(MIN_SCALE, p)
        note_edge = TAP_W * 0.5 * s
        gap = (CENTER_B - CENTER_A) - 2 * note_edge
        old = (CENTER_B - CENTER_A - TAP_W) * s
        new = gap + 2 * max(1.0, TAP_H * 0.3 * s)
        print(f"  s={s:0.2f}: 可视间隙 {gap:7.1f} / 旧连线 {old:7.1f}（缺口 {gap - old:6.1f}）"
              f" / 新连线 {new:7.1f}（重叠 {max(1.0, TAP_H * 0.3 * s):4.1f}）")


if __name__ == "__main__":
    main()
