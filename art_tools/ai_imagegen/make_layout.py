#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按项目规格生成演奏界面「结构图纸」，用作 ControlNet 的控制图。

规格来源：桌面 `gameplay美术需求与工作计划.xlsx`（1080×1920 / 判定线距底 400 /
轨组 1004px / 单轨 184px / 五轨中心 ±410、±205、0 / 顶端缩至 42%）。

输出：
    out/layout/layout_gameplay_1080x1920.png   黑底白线，直接喂给 ControlNet
    out/layout/layout_gameplay_annotated.png   带中文尺寸标注的图纸，给人看
"""
import argparse
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out", "layout")

W, H = 1080, 1920
LINE_FROM_BOTTOM = 400          # 判定线距底
Y_JUDGE = H - LINE_FROM_BOTTOM  # 判定线 y = 1520
LANE_CENTERS = [-410, -205, 0, 205, 410]
LANE_W = 184                    # 单轨宽（判定线处）
LANE_HALF = LANE_W // 2
TOP_SCALE = 0.30                # 顶端缩放（规格 0.42，这里默认加强透视）
LINE_PX = 7                     # 线条粗细（越粗 ControlNet 越听话）


def scale_at(y):
    """判定线处 = 1.0，屏幕顶端 = TOP_SCALE，线性插值（按键区恒为 1.0）。"""
    if y >= Y_JUDGE:
        return 1.0
    t = max(0.0, min(1.0, y / Y_JUDGE))
    return TOP_SCALE + (1.0 - TOP_SCALE) * t


def cx(offset, y):
    return W / 2 + offset * scale_at(y)


def draw_structure(d, annotate=False):
    # 五轨的 10 条边线（透视）
    edges = []
    for c in LANE_CENTERS:
        edges += [c - LANE_HALF, c + LANE_HALF]
    for e in sorted(set(edges)):
        d.line([(cx(e, 0), 0), (cx(e, Y_JUDGE), Y_JUDGE)], fill=(255, 255, 255), width=LINE_PX)

    # 判定线：横跨整个轨组
    span = LANE_CENTERS[-1] + LANE_HALF
    d.line([(cx(-span, Y_JUDGE), Y_JUDGE), (cx(span, Y_JUDGE), Y_JUDGE)],
           fill=(255, 255, 255), width=LINE_PX + 3)

    # 五轨按键区
    for c in LANE_CENTERS:
        # 注意：矩形参数顺序是 [x0, y0, x1, y1]，顶边必须贴判定线
        d.rectangle(
            [W / 2 + c - LANE_HALF, Y_JUDGE, W / 2 + c + LANE_HALF, H],
            outline=(255, 255, 255), width=LINE_PX)

    # HUD 占位
    hud = {
        "score": (60, 100, 420, 190),
        "combo": (420, 540, 660, 700),
        "judge_text": (340, Y_JUDGE - 176, 740, Y_JUDGE - 96),
        "pause": (960, 100, 1056, 196),
        "progress": (100, 40, 980, 48),
    }
    for key, (x0, y0, x1, y1) in hud.items():
        d.rectangle([x0, y0, x1, y1], outline=(255, 255, 255), width=LINE_PX)

    if not annotate:
        return hud

    # ---- 标注层 ----
    try:
        f_big = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 30)
        f = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 24)
        f_sm = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 20)
    except Exception:
        f_big = f = f_sm = ImageFont.load_default()

    yellow = (255, 214, 102)
    cyan = (126, 232, 214)

    # 画布尺寸
    d.line([(20, 20), (W - 20, 20)], fill=yellow, width=2)
    d.text((W / 2, 40), f"画布 {W} × {H}", font=f_big, fill=yellow, anchor="ma")

    # 轨组宽度（判定线处）
    d.line([(W / 2 - span, Y_JUDGE + 70), (W / 2 + span, Y_JUDGE + 70)], fill=yellow, width=2)
    for x in (W / 2 - span, W / 2 + span):
        d.line([(x, Y_JUDGE + 55), (x, Y_JUDGE + 85)], fill=yellow, width=2)
    d.text((W / 2, Y_JUDGE + 96), f"轨组 {span * 2}px（两侧各留 {int(W / 2 - span)}px）",
           font=f_sm, fill=yellow, anchor="ma")

    # 判定线高度
    d.line([(span + 90, Y_JUDGE), (span + 90, H)], fill=yellow, width=2)
    d.line([(span + 75, Y_JUDGE), (span + 105, Y_JUDGE)], fill=yellow, width=2)
    d.line([(span + 75, H - 2), (span + 105, H - 2)], fill=yellow, width=2)
    d.text((span + 100, (Y_JUDGE + H) / 2), f"距底 {LINE_FROM_BOTTOM}px", font=f_sm, fill=yellow, anchor="lm")

    # 单轨宽
    c0 = LANE_CENTERS[0]
    d.line([(W / 2 + c0 - LANE_HALF, Y_JUDGE - 120), (W / 2 + c0 + LANE_HALF, Y_JUDGE - 120)], fill=cyan, width=2)
    d.text((W / 2 + c0, Y_JUDGE - 148), f"单轨 {LANE_W}px", font=f_sm, fill=cyan, anchor="ma")

    # HUD 标注
    labels = {
        "score": "分数 / 准确率",
        "combo": "连击数字",
        "judge_text": "判定文字",
        "pause": "暂停 96×96",
        "progress": "进度条 880×8",
    }
    for key, (x0, y0, x1, y1) in hud.items():
        d.text((x0 + 6, y1 + 8), labels[key], font=f_sm, fill=cyan)

    # 透视说明
    d.text((30, H - 60), f"透视：顶端缩至 {int(TOP_SCALE * 100)}%，判定线处 100%",
           font=f, fill=cyan)
    d.text((30, H - 28), "五轨中心：±410 / ±205 / 0", font=f, fill=cyan)
    return hud


def main():
    global TOP_SCALE, LINE_PX
    ap = argparse.ArgumentParser()
    ap.add_argument("--top-scale", type=float, default=TOP_SCALE,
                    help="屏幕顶端的轨组缩放，越小透视越强（规格 0.42）")
    ap.add_argument("--line-px", type=int, default=LINE_PX, help="控制图线条粗细")
    ap.add_argument("--suffix", default="", help="输出文件名后缀，便于并排比较")
    args = ap.parse_args()
    TOP_SCALE = args.top_scale
    LINE_PX = args.line_px

    os.makedirs(OUT, exist_ok=True)

    base = Image.new("RGB", (W, H), (0, 0, 0))
    draw_structure(ImageDraw.Draw(base), annotate=False)
    p1 = os.path.join(OUT, f"layout_gameplay_1080x1920{args.suffix}.png")
    base.save(p1)

    ann = Image.new("RGB", (W, H), (0, 0, 0))
    draw_structure(ImageDraw.Draw(ann), annotate=True)
    p2 = os.path.join(OUT, f"layout_gameplay_annotated{args.suffix}.png")
    ann.save(p2)

    print(f"top_scale={TOP_SCALE} line_px={LINE_PX}")
    print("WROTE", p1)
    print("WROTE", p2)


if __name__ == "__main__":
    main()
