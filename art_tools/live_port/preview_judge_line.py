#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""判定线皮肤 / 命中闪光 / 过线继续飞 对照稿（前端清单 D1/D2/D4，过稿用，不进游戏）。

上游规格（`_refs/OpenSekai`）：

  D1 判定线：`LaneView.defaultJudgeLine`（SpriteRenderer，皮肤来自
     `MasterIngameJudgeFrame` 资源包）—— 我们拿不到那张贴图，所以本稿给**程序化等价**的候选。
  D2 命中闪光：上游不是"整条线亮一下"，而是 `TapEffectView` 里**按轨**播的效果阵列：
     `tapLaneEffectList` / `defaultLaneEffectList` / `criticalLaneEffectList` /
     `flickLaneEffectList` / `criticalFlickLaneEffectList`（预制体 `fx_lane_tap` / `fx_lane_default` /
     `fx_lane_critical` / `fx_lane_flick` / `fx_lane_critical_flick`，每个 LaneCount 一份）。
     → 我们按同样语义做"命中那一轨的判定线发光"，并按音符类型换色。
  D4 过线继续飞：`BaseNoteView.UpdatePosition` 用 `Vector2.LerpUnclamped(spawn, center, progress)`
     —— progress > 1 时音符**继续越过判定线**往下飞；
     裁剪用 shader 属性 `_NoteShowRate`（`BaseNoteView.ApplyNoteShowRate`，来源设置项"上隐挡板"0-100，
     `LiveUtility.CalcNoteShowRate`）→ 上半个屏幕按比例挡掉。

输出：art_tools/review/judge_line/judge_line_compare.png
运行：python -X utf8 art_tools/live_port/preview_judge_line.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
PANEL = (14, 26, 32)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
DIM = (74, 104, 110)
LINE = (207, 239, 234)
FLICK = (255, 196, 120)
CRIT = (255, 140, 200)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "judge_line"))

JUDGE_Y = 1260          # 判定线在本稿里的屏幕 y（画布 1080x1920）
LANE_W = 168            # 单轨宽（5 轨 = 840，居中）


def font(size, bold=False):
    names = (["msyhbd.ttc", "simhei.ttf"] if bold else []) + ["msyh.ttc", "simhei.ttf", "arial.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def text(draw, s, x, y, size, color, bold=True, anchor="mm"):
    draw.text((x, y), s, font=font(size, bold), fill=color, anchor=anchor)


def playfield(draw, show_lanes=True):
    """画一个简化演奏区：5 轨底 + 判定线位置参考。"""
    x0 = W / 2 - LANE_W * 2.5
    for i in range(5):
        x = x0 + i * LANE_W
        draw.rectangle([x + 6, 300, x + LANE_W - 6, JUDGE_Y + 260], fill=(10, 20, 26))
    if show_lanes:
        for i in range(6):
            x = x0 + i * LANE_W
            draw.rectangle([x - 1, 320, x + 1, JUDGE_Y + 240], fill=(22, 40, 46))


# ── D1：判定线皮肤的三个候选 ──────────────────────────────────────────────

def skin_a(draw):
    """A 现状：一条 6px 白条。"""
    draw.rectangle([W / 2 - 520, JUDGE_Y - 3, W / 2 + 520, JUDGE_Y + 3], fill=LINE)


def skin_b(draw):
    """B 细芯 + 两端渐隐：中间亮、两侧淡出（程序化，无贴图）。"""
    half = 520
    for i in range(half):
        t = i / half
        a = 1.0 - t * t                      # 两端渐隐
        col = tuple(int(c * a + PANEL[i % 3] * (1 - a)) for c in LINE)
        draw.line([(W / 2 - i, JUDGE_Y), (W / 2 - i, JUDGE_Y)], fill=col)
        draw.line([(W / 2 + i, JUDGE_Y), (W / 2 + i, JUDGE_Y)], fill=col)
    draw.rectangle([W / 2 - 520, JUDGE_Y - 4, W / 2 + 520, JUDGE_Y - 1], fill=(255, 255, 255))
    draw.rectangle([W / 2 - 520, JUDGE_Y + 1, W / 2 + 520, JUDGE_Y + 4], fill=(120, 150, 150))


def skin_c(draw):
    """C 分轨刻度 + 细芯（能看出 5 轨边界，拆墙后更需要）。"""
    skin_b(draw)
    x0 = W / 2 - LANE_W * 2.5
    for i in range(6):
        x = x0 + i * LANE_W
        draw.rectangle([x - 2, JUDGE_Y - 26, x + 2, JUDGE_Y + 26], fill=(60, 96, 100))
    for i in range(5):
        x = x0 + (i + 0.5) * LANE_W
        draw.rectangle([x - 1, JUDGE_Y - 14, x + 1, JUDGE_Y + 14], fill=(40, 70, 78))


# ── D2：按轨命中闪光（时序） ──────────────────────────────────────────────

def hit_flash(draw, lane, phase, kind="tap"):
    """在 lane 那一轨的判定线上画一段发光（phase 0..1 = 出现→淡出）。"""
    colors = {"tap": ACCENT, "default": LINE, "critical": CRIT, "flick": FLICK}
    base = colors.get(kind, ACCENT)
    x0 = W / 2 - LANE_W * 2.5 + lane * LANE_W
    grow = 1.0 + 0.35 * phase                 # 向外扩张
    half = (LANE_W / 2) * grow
    alpha = max(0.0, 1.0 - phase)
    cx = x0 + LANE_W / 2
    for k in range(3):                        # 三层：芯 + 光晕
        hgt = 26 - k * 8
        a = alpha * (1.0 - k * 0.28)
        col = tuple(int(c * a + 8 * (1 - a)) for c in base)
        w = half * (1.0 - k * 0.12)
        draw.rounded_rectangle([cx - w, JUDGE_Y - hgt / 2, cx + w, JUDGE_Y + hgt / 2],
                               radius=hgt / 2, fill=col)


# ── D4：过线继续飞 + 上隐挡板裁剪 ────────────────────────────────────────

def fly_note(draw, progress, clip_rate=None):
    """progress ≥ 1 表示越过判定线；clip_rate 为上隐挡板比例（0=不挡）。"""
    lane = 2
    x0 = W / 2 - LANE_W * 2.5 + lane * LANE_W
    cx = x0 + LANE_W / 2
    y_top, y_judge = 420, JUDGE_Y
    y = y_judge + (y_judge - y_top) * (progress - 1.0)
    hw, hh = 74, 18
    col = ACCENT if progress <= 1.02 else (150, 210, 205)
    draw.rounded_rectangle([cx - hw, y - hh, cx + hw, y + hh], radius=hh, fill=col)
    if clip_rate:
        cy = int(H * clip_rate)
        draw.rectangle([0, 0, W, cy], fill=(0, 0, 0))
        draw.line([(0, cy), (W, cy)], fill=DIM, width=2)


def mini(rows, title, notes):
    """把若干"半身图"拼成一列：返回 (image, height)。"""
    img = Image.new("RGB", (W, rows * 300 + 120), (6, 12, 16))
    d = ImageDraw.Draw(img)
    text(d, title, W / 2, 40, 38, TITLE)
    for i, note in enumerate(notes):
        text(d, note, 40, 92 + i * 300, 26, TEXT, False, anchor="lm")
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # ① D1：三个皮肤候选并排（截取判定线附近区域）
    skins = [("A 现状（一条白条）", skin_a), ("B 细芯 + 两端渐隐（推荐）", skin_b),
             ("C B + 分轨刻度", skin_c)]
    crop_h = 320
    sheet_w = W + 80
    row_h = crop_h + 60
    d1 = Image.new("RGB", (sheet_w, 120 + row_h * len(skins)), (6, 12, 16))
    dd = ImageDraw.Draw(d1)
    text(dd, "D1 判定线皮肤候选（程序化等价上游 MasterIngameJudgeFrame）", sheet_w / 2, 40, 38, TITLE)
    for i, (label, fn) in enumerate(skins):
        img = Image.new("RGB", (W, H), (8, 16, 22))
        di = ImageDraw.Draw(img)
        playfield(di)
        fn(di)
        crop = img.crop((0, JUDGE_Y - 140, W, JUDGE_Y + 180))
        y = 100 + i * row_h
        d1.paste(crop, (40, y))
        text(dd, label, 60, y + crop_h + 22, 28, ACCENT, True, anchor="lm")

    # ② D2：按轨闪光时序（tap / flick / critical 三种配色各一列，四个相位）
    phases = [0.0, 0.35, 0.65, 1.0]
    kinds = ["tap", "flick", "critical"]
    tile_w, tile_h = 300, 220
    d2 = Image.new("RGB", (sheet_w, 150 + len(kinds) * (tile_h + 70)), (6, 12, 16))
    d2d = ImageDraw.Draw(d2)
    text(d2d, "D2 命中闪光：只闪被命中那一轨（按类型换色，对应上游 fx_lane_*）",
         sheet_w / 2, 40, 36, TITLE)
    for ki, kind in enumerate(kinds):
        y = 110 + ki * (tile_h + 70)
        text(d2d, "%s（%s）" % (kind, {"tap": "普通", "flick": "甩键", "critical": "critical"}[kind]),
             60, y + tile_h / 2, 28, ACCENT, True, anchor="lm")
        for pi, ph in enumerate(phases):
            x = 320 + pi * (tile_w + 30)
            img = Image.new("RGB", (W, H), (8, 16, 22))
            di = ImageDraw.Draw(img)
            playfield(di)
            skin_b(di)
            hit_flash(di, 1, ph, kind)
            crop = img.crop((0, JUDGE_Y - 130, W, JUDGE_Y + 90)).resize((tile_w, tile_h))
            d2.paste(crop, (x, y))
            d2d.rectangle([x, y, x + tile_w, y + tile_h], outline=(40, 70, 78))
            text(d2d, "%d%%" % int(ph * 100), x + tile_w / 2, y + tile_h + 24, 24, TEXT, False)

    # ③ D4：过线继续飞 + 上隐挡板
    progresses = [1.0, 1.15, 1.3, 1.45]
    d3 = Image.new("RGB", (sheet_w, 150 + tile_h + 90), (6, 12, 16))
    d3d = ImageDraw.Draw(d3)
    text(d3d, "D4 过线继续飞（LerpUnclamped，progress > 1）", sheet_w / 2, 40, 36, TITLE)
    for pi, p in enumerate(progresses):
        x = 40 + pi * (tile_w + 30)
        y = 110
        img = Image.new("RGB", (W, H), (8, 16, 22))
        di = ImageDraw.Draw(img)
        playfield(di)
        skin_b(di)
        fly_note(di, p)
        crop = img.crop((0, JUDGE_Y - 200, W, JUDGE_Y + 420)).resize((tile_w, tile_h))
        d3.paste(crop, (x, y))
        d3d.rectangle([x, y, x + tile_w, y + tile_h], outline=(40, 70, 78))
        text(d3d, "p=%.2f" % p, x + tile_w / 2, y + tile_h + 26, 24, TEXT, False)

    # 合成总图
    total_h = d1.height + d2.height + d3.height + 80
    sheet = Image.new("RGB", (sheet_w, total_h), (5, 10, 14))
    y = 20
    for part in (d1, d2, d3):
        sheet.paste(part, (0, y))
        y += part.height + 20
    sheet.save(os.path.join(OUT_DIR, "judge_line_compare.png"))
    d1.save(os.path.join(OUT_DIR, "judge_line_d1_skins.png"))
    d2.save(os.path.join(OUT_DIR, "judge_line_d2_flash.png"))
    d3.save(os.path.join(OUT_DIR, "judge_line_d4_fly.png"))
    print("已输出：", OUT_DIR)


if __name__ == "__main__":
    main()
