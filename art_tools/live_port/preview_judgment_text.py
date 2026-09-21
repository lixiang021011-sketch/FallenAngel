#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""判定文字 / 判定描述「四态 + 动画」对照稿（过稿用，不进游戏）。

上游（OpenSekai，`Sekai/JudgmentView.cs` + `JudgmentDescriptionView.cs`）的行为，逐字照抄：
  · 判定文字：按结果换 sprite（Miss/Bad/Good/Great/Perfect，**JustPerfect 复用 Perfect**）；
  · 判定描述：**只在 Bad/Good/Great 档显示**（`result < Bad || result > Great` 直接 return），
    三张图 = Fast / Late / FlickMiss；Perfect 档的 Fast/Late 描述**不显示**；
  · 动画：`t = clamp01(time*20)`、`scale = 1-(1-t)³`、`time > 0.3` 立刻隐藏（出现 0.05s、驻留到 0.3s）。

本脚本只画对照图：① 五个判定词 + 三个描述词的样式（本工程用 TMP 文本，字号/颜色在此定稿）；
② 动画四帧（t=0 / 0.025 / 0.05 / 0.3）；③ 在我们 5 键下落界面里的位置（判定线上方 100 / 40 画布单位）。

输出：art_tools/review/judgment_text/judgment_text_compare.png（+2 张原尺寸）
运行：python -X utf8 art_tools/live_port/preview_judgment_text.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
JUDGE_CENTER_Y = 400.0 - H / 2.0          # -560
JUDGE_TEXT_Y = 100.0                       # HUD 里 JudgeText 的 anchoredPosition.y（画布中心系）
DESC_TEXT_Y = 40.0                         # JudgeBiasText（= 描述行）
NOTE_W, NOTE_H = 187.0, 28.0
PITCH = 208.0
LANE_CENTERS = [-416.0, -208.0, 0.0, 208.0, 416.0]

BG = (10, 20, 26)
JUDGE_LINE = (207, 239, 234)
TEXT = (150, 180, 186)
TITLE = (222, 244, 240)
WORDS = [("PERFECT", (207, 239, 234)), ("GREAT", (176, 226, 222)),
         ("GOOD", (146, 208, 204)), ("BAD", (232, 176, 120)), ("MISS", (226, 120, 120))]
DESCR = [("FAST", (168, 226, 220)), ("LATE", (168, 226, 220)), ("FLICK MISS", (226, 180, 120))]


def font(size, bold=False):
    for n in ("msyhbd.ttc" if bold else "msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def screen(x_center, y_center):
    return W / 2.0 + x_center, H / 2.0 - y_center


def scale_at(t):
    """上游动画：t=clamp01(time*20)、scale=1-(1-t)³（此处直接给归一化时间的 scale）。"""
    t = max(0.0, min(1.0, t))
    return 1.0 - (1.0 - t) ** 3


def draw_centered(draw, text, y_center, size, color, s=1.0):
    px_size = int(size * s)
    if px_size < 1:
        return                       # t=0 时 scale=0 → 不可见（上游也是 localScale=0）
    f = font(px_size, bold=True)
    px, py = screen(0, y_center)
    box = draw.textbbox((0, 0), text, font=f)
    draw.text((px - (box[2] - box[0]) / 2.0, py - (box[3] - box[1]) / 2.0 - box[1]), text, font=f, fill=color)


def draw_playfield(draw, with_text=True, result="PERFECT", desc=None, t=1.0):
    """在我们 5 键下落界面里画判定文字（轨道无墙，带子/音符不画细节）。"""
    draw.rectangle([0, 0, W, H], fill=BG)
    _, jy = screen(0, JUDGE_CENTER_Y)
    draw.rectangle([0, jy - 3, W, jy + 3], fill=JUDGE_LINE)
    for cx in LANE_CENTERS[:3]:                      # 示意几个音符
        px, py = screen(cx, JUDGE_CENTER_Y + 260)
        draw.rounded_rectangle([px - NOTE_W / 2, py - NOTE_H / 2, px + NOTE_W / 2, py + NOTE_H / 2],
                               radius=NOTE_H / 2, fill=(96, 140, 146))
    if not with_text:
        return
    color = dict(WORDS).get(result, TITLE)
    draw_centered(draw, result, JUDGE_TEXT_Y, 72, color, s=scale_at(t))
    if desc:
        draw_centered(draw, desc, DESC_TEXT_Y, 36, dict(DESCR).get(desc, TEXT), s=scale_at(t))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.abspath(os.path.join(here, "..", "review", "judgment_text"))
    os.makedirs(out_dir, exist_ok=True)

    # ① 样式表：五个判定词 + 三个描述词
    style = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(style)
    f_head, f_note = font(38), font(24)
    d.text((30, 30), "判定文字 / 描述：样式定稿（本工程用 TMP，字号=上游比例）", font=f_head, fill=TITLE)
    y = 140
    for word, color in WORDS:
        draw_centered(d, word, H / 2.0 - y, 72, color)
        y += 110
    y += 40
    for word, color in DESCR:
        draw_centered(d, word, H / 2.0 - y, 36, color)
        y += 70
    d.text((30, H - 90), "描述只在 Bad/Good/Great 档显示（上游口径）；Perfect/JustPerfect 不显示描述",
           font=f_note, fill=TEXT)
    style.save(os.path.join(out_dir, "judgment_text_style.png"))

    # ② 动画四帧
    anim = Image.new("RGB", (W * 4, H), BG)
    for i, t in enumerate((0.0, 0.025, 0.05, 0.30)):
        panel = Image.new("RGB", (W, H), BG)
        pd = ImageDraw.Draw(panel)
        f = font(34)
        pd.text((30, 30), "t=%.3fs  scale=%.3f" % (t, scale_at(t * 20)), font=f, fill=TITLE)
        draw_playfield(pd, True, "PERFECT", "FAST", t * 20)
        anim.paste(panel, (i * W, 0))
    anim.save(os.path.join(out_dir, "judgment_text_timeline.png"))

    # ③ 演奏界面里的位置
    in_play = Image.new("RGB", (W, H), BG)
    draw_playfield(ImageDraw.Draw(in_play), True, "GREAT", "LATE", 1.0)
    f = font(26)
    ImageDraw.Draw(in_play).text((30, H - 70), "位置：判定线以上 100 / 40 画布单位（居中）", font=f, fill=TEXT)
    in_play.save(os.path.join(out_dir, "judgment_text_in_play.png"))

    scale = 0.62
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 3 + 80, ph + 150), (6, 12, 16))
    sd = ImageDraw.Draw(sheet)
    sd.text((30, 24), "FallenAngel · 判定文字对照稿（照 OpenSekai：出现 0.05s 三次方缓出 → 0.3s 隐藏）",
            font=font(38), fill=TITLE)
    sd.text((30, 76), "JustPerfect 复用 PERFECT；描述仅 Bad/Good/Great 档（FAST / LATE / FLICK MISS）",
            font=font(24), fill=TEXT)
    for i, img in enumerate((style, anim.resize((W, H)), in_play)):
        x = 20 + i * (pw + 20)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 130))
        sd.rectangle([x, 130, x + pw, 130 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(out_dir, "judgment_text_compare.png"))
    print("已输出：", out_dir)


if __name__ == "__main__":
    main()
