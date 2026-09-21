#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校准（判定偏移）**玩家界面**对照稿（过稿用，不进游戏）。

背景：上游（OpenSekai）只有"设置里一行数字输入"（那是图表编辑器的工程界面），
没有任何玩家向的校准界面。所以这一层按音游通行做法设计——**玩家不敲数字**：

  · 主操作是「跟着节拍点屏幕」（8 拍）→ 自动算出建议值 → 一键应用；
  · 细调用大按钮 ±5ms（等价 ±0.3 帧），不出现键盘；
  · 「试听」用当前偏移循环播放节拍，让玩家直接听/看对齐结果；
  · 数值本身仍写进上游那套 `TimingAdjustData`（±20 帧 = ±333ms，正数 = 音符提前）。

三个变体（同一功能，不同主视觉）：
  A 中央脉冲环：节拍到达时屏幕中央的环脉冲 + 命中线，最省地方、竖屏友好（推荐）
  B 下落音符：沿用演奏页的下落语言（一根竖线 + 音符下落到线）
  C 节奏条：底部一条节拍进度条，节拍点依次点亮

输出：art_tools/review/calibration_screen/calibration_screen_compare.png（+3 张原尺寸）
运行：python -X utf8 art_tools/live_port/preview_calibration_screen.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
JUDGE_CENTER_Y = 400.0 - H / 2.0
BG = (8, 16, 22)
PANEL = (14, 26, 32)
LINE = (207, 239, 234)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
DIM = (74, 104, 110)
BTN = (30, 52, 60)
BTN_KEY = (46, 92, 100)


def font(size, bold=False):
    for n in ("msyhbd.ttc" if bold else "msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def center(draw, text, y, size, color, bold=True):
    f = font(size, bold)
    box = draw.textbbox((0, 0), text, font=f)
    draw.text((W / 2 - (box[2] - box[0]) / 2 - box[0], y - box[1]), text, font=f, fill=color)


def button(draw, y, text, w=520, h=110, key=True):
    x0, x1 = W / 2 - w / 2, W / 2 + w / 2
    draw.rounded_rectangle([x0, y, x1, y + h], radius=h / 2, fill=BTN_KEY if key else BTN)
    center(draw, text, y + h / 2 - 20, 40, TITLE)


def header(draw, subtitle):
    center(draw, "节奏校准", 110, 62, TITLE)
    center(draw, subtitle, 200, 30, TEXT, bold=False)


def footer(draw, value_ms, taps, suggest_ms):
    """底部：当前值 / 测量结果 / 细调与试听按钮（三变体共用）。"""
    y = H - 720
    draw.rounded_rectangle([80, y, W - 80, y + 300], radius=28, fill=PANEL)
    center(draw, "当前偏移  %+d ms（%+.1f 帧）" % (value_ms, value_ms / (1000.0 / 60.0)), y + 60, 34, ACCENT)
    center(draw, "本次测量：%d 次点击 → 建议 %+d ms" % (taps, suggest_ms), y + 130, 30, TEXT, bold=False)
    draw.rounded_rectangle([150, y + 190, 420, y + 280], radius=45, fill=BTN)
    center(draw, "−", y + 225, 44, TITLE)
    draw.rounded_rectangle([W - 420, y + 190, W - 150, y + 280], radius=45, fill=BTN)
    center(draw, "＋", y + 225, 44, TITLE)
    center(draw, "±5ms", y + 232, 26, DIM, bold=False)
    button(draw, H - 380, "应用建议值")
    button(draw, H - 250, "再测一次", key=False)


def variant_a(draw, beat_phase, value_ms, taps, suggest_ms):
    header(draw, "跟着节拍点屏幕（共 8 拍）")
    cy = 780
    r = 150 + 90 * beat_phase
    a = int(255 * (1.0 - beat_phase))
    draw.ellipse([W / 2 - r, cy - r, W / 2 + r, cy + r], outline=(DIM[0], DIM[1], DIM[2]), width=8)
    draw.ellipse([W / 2 - 120, cy - 120, W / 2 + 120, cy + 120],
                 fill=(20, 60, 66) if beat_phase < 0.4 else (30, 92, 98))
    center(draw, "点", cy - 46, 64, TITLE)
    center(draw, "第 3 / 8 拍", cy + 250, 32, TEXT, bold=False)
    footer(draw, value_ms, taps, suggest_ms)


def variant_b(draw, beat_phase, value_ms, taps, suggest_ms):
    header(draw, "音符落到线时点屏幕（共 8 拍）")
    lane_x = W / 2
    y_line, y_top = 900, 470
    draw.line([lane_x, y_top, lane_x, y_line], fill=DIM, width=6)
    y = y_line - (y_line - y_top) * beat_phase
    draw.rounded_rectangle([lane_x - 90, y - 22, lane_x + 90, y + 22], radius=22, fill=ACCENT)
    draw.rectangle([lane_x - 260, y_line - 4, lane_x + 260, y_line + 4], fill=LINE)
    footer(draw, value_ms, taps, suggest_ms)


def variant_c(draw, beat_phase, value_ms, taps, suggest_ms):
    header(draw, "节拍条走到亮块时点屏幕（共 8 拍）")
    y = 760
    draw.rounded_rectangle([120, y, W - 120, y + 46], radius=23, fill=PANEL)
    for i in range(8):
        x = 160 + i * (760 / 7.0)
        lit = i <= int(beat_phase * 7.999)
        draw.ellipse([x - 18, y + 5, x + 18, y + 41],
                     fill=ACCENT if lit else (34, 56, 62))
    x = 160 + beat_phase * 760
    draw.rounded_rectangle([x - 40, y - 22, x + 40, y + 68], radius=18, outline=ACCENT, width=6)
    center(draw, "第 3 / 8 拍", y + 130, 32, TEXT, bold=False)
    footer(draw, value_ms, taps, suggest_ms)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.abspath(os.path.join(here, "..", "review", "calibration_screen"))
    os.makedirs(out_dir, exist_ok=True)

    variants = [("A_center_pulse", variant_a), ("B_falling_note", variant_b), ("C_beat_bar", variant_c)]
    images = []
    for name, fn in variants:
        img = Image.new("RGB", (W, H), BG)
        d = ImageDraw.Draw(img)
        fn(d, 0.35, 17, 8, 21)
        d.text((40, H - 90), "数值仍写进上游的 TimingAdjustData（±20 帧 = ±333ms，正数 = 音符提前）",
               font=font(24), fill=TEXT)
        img.save(os.path.join(out_dir, "calibration_%s.png" % name))
        images.append(img)

    scale = 0.62
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 3 + 80, ph + 150), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((30, 24), "FallenAngel · 校准（判定偏移）玩家界面对照稿", font=font(40, True), fill=TITLE)
    sd.text((30, 76), "上游没有玩家向校准界面（只有图表编辑器里一行数字）→ 这一层按音游通行做法设计：跟拍点击 + 大按钮细调，玩家不敲数字",
            font=font(24), fill=TEXT)
    for i, img in enumerate(images):
        x = 20 + i * (pw + 20)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 130))
        sd.rectangle([x, 130, x + pw, 130 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(out_dir, "calibration_screen_compare.png"))
    print("已输出：", out_dir)


if __name__ == "__main__":
    main()
