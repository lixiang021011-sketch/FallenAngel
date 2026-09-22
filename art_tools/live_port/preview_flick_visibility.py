#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""粉键（flick）醒目度：现状诊断 + 4 个候选（过稿用，**不进游戏**）。

作者反馈（2026-09-22）："粉键逻辑应该没问题了……就是粉键不醒目容易看错"。

现状为什么容易看错（按代码里的真实几何算，不是观感描述）：
  · 头部形状 = PointedBar：切角尖端 tip = min(0.12×w, h/2) = **14 画布单位**，
    而音符高 28、宽 130 —— 尖端只占宽度的 10.8%、高度的一半，**在满轨宽的条上几乎看不出来**；
  · 判定线处的可视形状里，tap(ChamferBar) 与 flick 的差别只有"两端 14px 尖角 vs 7.84px 切角"；
  · 方向提示 = ArrowGraphic 的**26×26 白色三角形**（3 顶点，无描边），
    面积只占音符的 26×13/2 ÷ (130×28) ≈ **4.6%**，在满轨宽的色块里是个小点；
  · 两者（tap / flick）共用同一套轨道色阶，**明度也相同** → 只能靠形状分辨。

四个候选（都只动"外形/描边/图标"，不动判定与配色来源）：
  F1 尖角加大 + 中央折角缺口：tip 14 → 34（0.26×w），两端呈明显菱形；中间加一道
     方向性缺口（上/下），形状一眼可辨
  F2 高亮描边（rim + 尖端高光）：保持形状，外圈描边改成"顶亮底暗 + 尖端一段亮白"，
     与 tap 的深色描边形成明度对比
  F3 箭头放大 + 速度线：箭头 26 → 40（1.5×），并在箭头前后各加两道"速度线"
  F4 明度抬一档 + 同色光晕：flick 固定取调色板最亮一档（L0）并加一圈低透明度同色光晕

输出（art_tools/review/flick_visibility/）：
  flick_candidates.png   现状 vs F1~F4（判定线缩放 + 中段缩放各一行，右侧放 tap 做对照）

运行：python -X utf8 art_tools/live_port/preview_flick_visibility.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

# ── 与游戏一致的几何/配色（LaneLayout、NoteVisualConfig、LaneColors）──
LANE_COUNT = 5
PITCH = (1080.0 - 40.0) / LANE_COUNT           # 208
SIZE_SCALE = (PITCH * 0.9) / 130.0             # 1.44
TAP_W, TAP_H = 130.0 * SIZE_SCALE, 28.0        # 187.2 × 28
INDIGO = [(228, 244, 247), (169, 217, 232), (127, 168, 220), (110, 127, 192), (91, 91, 166)]
LANE_COLOR = INDIGO[2]                          # 取中间一档做对照（游戏内是每音符随机取版）
ARROW_SIZE, ARROW_ALPHA = 26.0, 0.9
BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
ACCENT = (126, 224, 176)


def font(size):
    for n in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def note_rect(draw, cx, cy, w, h, fill, rim_top, rim_bottom, rim_px):
    draw.rectangle([cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], fill=rim_bottom)
    draw.rectangle([cx - w / 2 + rim_px, cy - h / 2 + rim_px, cx + w / 2 - rim_px, cy + h / 2 - rim_px],
                   fill=fill)
    draw.line([cx - w / 2 + rim_px, cy - h / 2 + rim_px, cx + w / 2 - rim_px, cy - h / 2 + rim_px],
              fill=rim_top, width=max(1, int(rim_px * 0.8)))


def chamfer_pts(cx, cy, w, h, cut):
    hw, hh = w / 2, h / 2
    return [(cx - hw + cut, cy - hh), (cx + hw - cut, cy - hh), (cx + hw, cy - hh + cut),
            (cx + hw, cy + hh - cut), (cx + hw - cut, cy + hh), (cx - hw + cut, cy + hh),
            (cx - hw, cy + hh - cut), (cx - hw, cy - hh + cut)]


def draw_tap(draw, cx, cy, s):
    w, h = TAP_W * s, TAP_H * s
    cut = max(2.0, h * 0.28)
    pts = chamfer_pts(cx, cy, w, h, cut)
    draw.polygon(pts, fill=shade(LANE_COLOR, 0.62))
    inner = chamfer_pts(cx, cy, w - 4 * s, h - 4 * s, max(1.0, cut - 2 * s))
    draw.polygon(inner, fill=LANE_COLOR)


def flick_pts(cx, cy, w, h, tip):
    hw, hh = w / 2, h / 2
    return [(cx - hw + tip, cy - hh), (cx + hw - tip, cy - hh), (cx + hw, cy),
            (cx + hw - tip, cy + hh), (cx - hw + tip, cy + hh), (cx - hw, cy)]


def draw_arrow(img, cx, cy, size, direction="up", color=(255, 255, 255), alpha=ARROW_ALPHA):
    """箭头 = 实心三角形（与 ArrowGraphic 同形），用局部 overlay 做半透明合成。"""
    a = int(255 * alpha)
    col = (color[0], color[1], color[2], a)
    pad = int(size) + 2
    ov = Image.new("RGBA", (pad * 2, pad * 2), (0, 0, 0, 0))
    ox, oy = pad, pad
    d = size / 2
    if direction == "up":
        pts = [(ox - d, oy + d * 0.6), (ox + d, oy + d * 0.6), (ox, oy - d)]
    else:
        pts = [(ox - d, oy - d * 0.6), (ox + d, oy - d * 0.6), (ox, oy + d)]
    ImageDraw.Draw(ov).polygon(pts, fill=col)
    img.alpha_composite(ov, (int(cx) - ox, int(cy) - oy))


def draw_flick(img, draw, cx, cy, s, variant):
    """variant: current / F1 / F2 / F3 / F4"""
    w, h = TAP_W * s, TAP_H * s
    base = LANE_COLOR if variant != "F4" else INDIGO[0]
    rim_px = max(1.0, 2.4 * s)
    tip_local = min(0.12 * (w / SIZE_SCALE), h / (2 * s)) if variant != "F1" else min(0.26 * (w / SIZE_SCALE), h / (2 * s))
    tip = tip_local * s

    if variant == "F4":                      # 同色光晕
        for k in range(3, 0, -1):
            grow = k * 3 * s
            draw.polygon(flick_pts(cx, cy, w + grow, h + grow, tip + grow * 0.5),
                         fill=mix(BG, base, 0.18 / k))

    pts = flick_pts(cx, cy, w, h, tip)
    draw.polygon(pts, fill=shade(base, 0.62))
    inner_tip = max(0.0, tip - 2 * s)
    inner = flick_pts(cx, cy, max(2.0, w - 4 * s), max(2.0, h - 4 * s), inner_tip)
    if variant == "F1":                      # 中央折角缺口（方向性）
        draw.polygon(inner, fill=base)
        notch_h = h * 0.34
        draw.polygon([(cx - w * 0.10, cy), (cx, cy - notch_h / 2), (cx + w * 0.10, cy),
                      (cx, cy + notch_h / 2)], fill=shade(base, 0.30))
    else:
        draw.polygon(inner, fill=base)

    if variant == "F2":                      # 顶端与尖角高光
        draw.line([cx - w * 0.36, cy - h * 0.30, cx + w * 0.36, cy - h * 0.30],
                  fill=mix(base, (255, 255, 255), 0.75), width=max(2, int(3 * s)))
        for sign in (-1, 1):
            x = cx + sign * (w / 2 - tip * 0.6)
            draw.line([x - sign * tip * 0.5, cy - h * 0.30, x, cy],
                      fill=mix(base, (255, 255, 255), 0.85), width=max(2, int(3 * s)))

    if variant == "F3":                      # 箭头放大 + 速度线
        draw_arrow(img, cx, cy, ARROW_SIZE * 1.55 * s, "up")
        for dy in (-1.05, 1.05):
            y = cy + dy * h * 0.5
            draw.line([cx - w * 0.16, y, cx + w * 0.16, y],
                      fill=mix(base, (255, 255, 255), 0.7), width=max(1, int(2.5 * s)))
    else:
        draw_arrow(img, cx, cy, ARROW_SIZE * s, "up")


def band(img, draw, y, scale, variant, label):
    draw.text((28, y - 26), label, fill=TEXT_BRIGHT if variant != "current" else TEXT, font=font(21))
    cy = y + 18
    for i in range(LANE_COUNT):
        cx = 540 + (i - 2) * PITCH
        draw.rectangle([cx - PITCH * 0.45, cy - 46, cx + PITCH * 0.45, cy + 46], fill=LANE_FILL)
    draw_flick(img, draw, 540 - PITCH * 0.5, cy, scale, variant)
    draw_tap(draw, 540 + PITCH * 0.5, cy, scale)
    return cy


def main():
    W, H = 1080, 1180
    img = Image.new("RGBA", (W, H), BG + (255,))
    draw = ImageDraw.Draw(img)
    draw.text((28, 20), "粉键（flick）醒目度：现状 vs 4 个候选 —— 右为 tap 对照",
              fill=TEXT_BRIGHT, font=font(28))
    draw.text((28, 56),
              "现状：形状与 tap 只差 14px 尖角（宽度 130 的 10.8%），箭头 26×26 面积占音符 4.6%，明度与 tap 相同",
              fill=TEXT, font=font(19))

    y = 100
    for variant, label, note in (
        ("current", "现状（PointedBar tip=14 + 26px 箭头）", ""),
        ("F1", "F1 尖角加大 14→34（0.26w）+ 中央折角缺口 —— 形状一眼可辨", ""),
        ("F2", "F2 高亮描边：顶面亮带 + 尖端高光（tap 是深色描边）", ""),
        ("F3", "F3 箭头 26→40（1.5×）+ 上下两道速度线", ""),
        ("F4", "F4 明度抬一档（L0）+ 同色光晕", ""),
    ):
        band(img, draw, y, 1.00, variant, label + "   [判定线 s=1.0]")
        y += 104
        band(img, draw, y, 0.62, variant, "     同款 @ 中段 s=0.62")
        y += 104

    out_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "review", "flick_visibility"))
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "flick_candidates.png")
    img.convert("RGB").save(out)
    print("写出：" + out + f"  ({W}×{H})")

    # 可量化指标（给作者按数字挑）
    print("\n可量化指标（判定线缩放 s=1.0，单位：画布像素）")
    print("  %-8s %-26s %-10s %-12s" % ("方案", "形状特征", "箭头面积比", "与 tap 的形状差"))
    tap_cut = max(2.0, TAP_H * 0.28)
    rows = [
        ("现状", f"尖端内缩 {min(0.12*130,14):.1f}px（{min(0.12*130,14)/130*100:.1f}% 宽）",
         f"{26*13/2/(130*28)*100:.1f}%", f"尖端 vs 切角 {min(0.12*130,14)-tap_cut:+.1f}px"),
        ("F1", "尖端内缩 34px（26.2% 宽）+ 折角缺口", f"{26*13/2/(130*28)*100:.1f}%",
         f"尖端 vs 切角 {34-tap_cut:+.1f}px"),
        ("F2", "形状同现状，描边改亮（顶面带 + 尖端高光）", f"{26*13/2/(130*28)*100:.1f}%",
         "轮廓同，明度 +35%"),
        ("F3", "形状同现状，箭头 40px + 速度线", f"{40*20/2/(130*28)*100:.1f}%",
         f"图标面积 ×{40*40/(26*26):.2f}"),
        ("F4", "形状同现状，整体提一档明度 + 光晕", f"{26*13/2/(130*28)*100:.1f}%",
         "明度 +1 档（L2→L0）"),
    ]
    for r in rows:
        print("  %-8s %-26s %-10s %-12s" % r)


if __name__ == "__main__":
    main()
