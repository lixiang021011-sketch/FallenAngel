#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Combo 数字位 + AP 特效对照稿（前端清单 B3，过稿用，不进游戏）。

上游规格逐条抄自 `_refs/OpenSekai/Assets/Scripts/Assembly-CSharp/Sekai/ComboView.cs`：

  · 数字位布局：`x = (digitCount - 1) * 0.475f + i * -0.95f`
    （i 从个位开始，所以数字是**从右往左**排；0.95 = 相邻数字间距，0.475 = 整体居中偏移）
  · 出现动画：`appear = clamp01(time / 0.1f)`，`numberRoot.localScale = 0.6 + appear * 0.4`
    → 0.1 秒内从 0.6 倍放到 1.0 倍
  · 爆发层：`burst = clamp01((time - 0.1f) / 0.1f)`，`effectScale = 1 + burst * 0.4`，
    同时 `alpha = (1 - burst) * 0.5` → 0.1~0.2 秒之间放大到 1.4 倍并淡出
  · AP（全 Perfect）专属特效：`alpha = (sin(Time.time * 4.1887903f) + 1) * 0.5f`
    —— 4.18879 rad/s 的正弦呼吸（周期约 1.5 秒），作用于 AP 数字 + AP 外圈 + AP 文本
  · 同连击数不重复播动画（`if (currentCombo == combo) return;`）

我们这边（竖屏）落点：HUD 的 combo 在屏幕中央偏上方（画布中心 + y=250），
所以本稿按 1080x1920 竖屏、数字中心 y≈710 出图。

输出：art_tools/review/combo_ap/combo_ap_compare.png（+ 三张单屏）
运行：python -X utf8 art_tools/live_port/preview_combo_ap.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
COMBO_CY = 710
BG = (8, 16, 22)
PANEL = (14, 26, 32)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
DIM = (74, 104, 110)
AP_PINK = (255, 140, 200)

DIGIT_W = 96          # 单个数字的宽（画布像素）
GAP = 0.95            # 上游的相邻间距（= 0.95 个数字宽）

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "combo_ap"))


def font(size, bold=False):
    names = (["msyhbd.ttc", "simhei.ttf"] if bold else []) + ["msyh.ttc", "simhei.ttf", "arial.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def center(draw, text, y, size, color, bold=True):
    draw.text((W / 2, y), text, font=font(size, bold), fill=color, anchor="mm")


def digits_of(combo):
    """返回 (digitCount, [高位→低位])；combo<=0 时上游只显示 1 位不显示（visible=false）。"""
    if combo <= 0:
        return 1, [0]
    s = str(combo)
    return len(s), [int(c) for c in s]


def digit_x_positions(digit_count):
    """上游 `x = (digitCount - 1) * 0.475 + i * -0.95`（i 从个位起），
    换算成"屏幕像素坐标、从右往左"，再翻成画布中心系。"""
    xs = []
    for i in range(digit_count):
        # 个位（i=0）在最右
        xs.append(((digit_count - 1) * 0.475 - i * GAP) * DIGIT_W)
    return xs   # 相对 combo 中心的 x（单位：像素；>0 在右）


def draw_combo(draw, combo, scale=1.0, ap=False, ap_alpha=0.0, effect_scale=0.0, effect_alpha=0.0):
    """按上游布局画 combo 数字位 + 形变层。"""
    count, digits = digits_of(combo)
    xs = digit_x_positions(count)
    cy = COMBO_CY

    # 爆发层（0.1~0.2s）：放大 1.4 倍、alpha 0.5→0
    if effect_scale > 0.0:
        for idx, d in enumerate(reversed(digits)):     # 低位在前
            x = W / 2 + xs[idx] * effect_scale
            w = DIGIT_W * effect_scale
            draw.rounded_rectangle([x - w / 2, cy - w / 2, x + w / 2, cy + w / 2],
                                   radius=w * 0.18,
                                   outline=ACCENT + (int(255 * effect_alpha),),
                                   width=max(2, int(4 * effect_scale)))

    # AP 外圈 + 呼吸
    if ap:
        r = 150 * scale * 1.15
        draw.ellipse([W / 2 - r, cy - r, W / 2 + r, cy + r],
                     outline=AP_PINK + (int(255 * ap_alpha),), width=10)
        draw.ellipse([W / 2 - r * 0.86, cy - r * 0.86, W / 2 + r * 0.86, cy + r * 0.86],
                     outline=AP_PINK + (int(140 * ap_alpha),), width=4)

    color = AP_PINK if ap else TITLE
    for idx, d in enumerate(reversed(digits)):
        x = W / 2 + xs[idx] * scale
        w = DIGIT_W * scale
        draw.rounded_rectangle([x - w / 2, cy - w / 2, x + w / 2, cy + w / 2],
                               radius=w * 0.18,
                               fill=(30, 60, 66) if not ap else (70, 30, 55))
        txt_color = color
        if ap and ap_alpha > 0:
            txt_color = tuple(int(c * (0.55 + 0.45 * ap_alpha)) for c in AP_PINK)
        draw.text((x, cy), str(d), font=font(int(w * 0.72), True), fill=txt_color, anchor="mm")

    # combo 文本标签（上游 comboSpriteRenderer = "COMBO" 字样）
    label_y = cy + DIGIT_W * 0.62 * scale
    center(draw, "COMBO", label_y, int(34 * scale), AP_PINK if ap else TEXT, True)


def poster(draw, combo, ap, phase_note, alpha=1.0):
    draw.rectangle([0, 0, W, H], fill=BG)
    center(draw, "B3 · combo 数字位" + ("（AP 全 Perfect）" if ap else ""), 120, 44, TITLE)
    center(draw, phase_note, 178, 26, DIM, False)

    if ap:
        draw_combo(draw, combo, scale=1.0, ap=True, ap_alpha=alpha)
    else:
        draw_combo(draw, combo, scale=1.0)

    # 说明
    center(draw, "数字位 x = (位数-1)*0.475 − i*0.95（i 从个位起，0.95 = 一个数字宽）",
           1500, 26, TEXT, False)
    center(draw, "出现：0.1s 内 0.6→1.0 倍；爆发：0.1~0.2s 放大到 1.4 倍并淡出",
           1546, 26, TEXT, False)
    center(draw, "AP：alpha = (sin(t*4.18879)+1)/2（周期约 1.5s 的呼吸）",
           1592, 26, (AP_PINK if ap else TEXT), False)


def check_no_overflow():
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    checks = [
        ("标题", "B3 · combo 数字位（AP 全 Perfect）", 44, True, W - 80),
        ("说明1", "数字位 x = (位数-1)*0.475 − i*0.95（i 从个位起，0.95 = 一个数字宽）", 26, False, W - 80),
        ("说明2", "出现：0.1s 内 0.6→1.0 倍；爆发：0.1~0.2s 放大到 1.4 倍并淡出", 26, False, W - 80),
        ("说明3", "AP：alpha = (sin(t*4.18879)+1)/2（周期约 1.5s 的呼吸）", 26, False, W - 80),
    ]
    bad = 0
    for label, text, size, bold, limit in checks:
        w = probe.textlength(text, font=font(size, bold))
        ok = w <= limit
        bad += 0 if ok else 1
        print("  [%s] %-6s %6.1f / %d" % ("OK " if ok else "!! ", label, w, limit))
    print("combo 稿文案宽度自检：%d 项，溢出 %d 项" % (len(checks), bad))
    return bad == 0


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    check_no_overflow()

    states = [
        ("combo_3digit", 137, False, "三位数（最常见）· 静态", 1.0),
        ("combo_4digit", 1234, False, "四位数 · 检查居中偏移", 1.0),
        ("combo_ap", 735, True, "AP（全 Perfect）· 呼吸相位示例", 0.85),
    ]
    images = []
    for name, combo, ap, note, alpha in states:
        img = Image.new("RGB", (W, H), BG)
        poster(ImageDraw.Draw(img), combo, ap, note, alpha)
        img.save(os.path.join(OUT_DIR, "combo_%s.png" % name))
        images.append(img)

    scale = 0.5
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 3 + 120, ph + 200), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((40, 26), "FallenAngel · B3 combo 数字位 + AP 特效 对照稿（待过稿）", font=font(40, True), fill=TITLE)
    sd.text((40, 80), "布局/动画/AP 呼吸全部取上游 ComboView 的常量：0.475 居中偏移、0.95 数字间距、"
                      "0.1s 出现、0.1~0.2s 爆发、AP 4.1887903 rad/s", font=font(24), fill=TEXT)
    sd.text((40, 118), "竖屏落点：数字中心在画布中心上方 250px（与现有 HUD 的 combo 位置一致）",
            font=font(24), fill=TEXT)
    labels = ["三位数 137", "四位数 1234", "AP 全 Perfect"]
    for i, img in enumerate(images):
        x = 30 + i * (pw + 30)
        sd.text((x + 6, 160), labels[i], font=font(24, True), fill=ACCENT)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 192))
        sd.rectangle([x, 192, x + pw, 192 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(OUT_DIR, "combo_ap_compare.png"))
    print("已输出：", OUT_DIR)


if __name__ == "__main__":
    main()
