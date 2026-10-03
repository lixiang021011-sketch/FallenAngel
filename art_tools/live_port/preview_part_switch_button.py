#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""手机上的"切换声部"入口：过稿稿（**不进游戏**）。

为什么需要这个控件（工程事实，不是偏好）：
  · 现在只有键盘 Tab 能切声部，**手机上没有任何入口**；
  · InputManager 的 `touchBottomRatio = 1`（判定区＝整屏，参照 Phigros），
    所以"点上半屏切换""双指轻点""上滑"这类手势都会与演奏输入抢事件
    —— 双指轻点还会和合法双押撞车；
  · 唯一不与演奏冲突的做法是 uGUI 控件：判定层用 `IsPointerOverUI` 挡掉 UI 上的触摸。

所以本稿给的是**按钮的形制与位置**，不是手势。位置依据 HUD 布局表（HudLayout）：
  顶部三行的最低边在画布 y≈779，音符生成线在 y=600 —— 中间那条 179px 的带子
  既不压 HUD，也没有音符经过，正好放声部控件。

三个候选：
  B1 单按钮（顶部中央横条）：显示当前声部名，点击切到"另一个" —— 最省地方，
     但两件以上乐器时看不出"能切到谁"
  B2 双按钮（顶部中央并排，当前高亮）：两个声部都摆出来，点另一个即切换 —— 推荐，
     切换目标一目了然；乐器多了改成横向列表即可
  B3 无按钮（只保留切换瞬间的徽记）：位置最干净，但手机上无法触发 —— 标为不可行

输出（art_tools/review/part_switch/）：
  part_switch_button.png   三个候选的整屏对照 + 推荐档的放大与状态变体

运行：python -X utf8 art_tools/live_port/preview_part_switch_button.py
"""
import colorsys
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
LANE_WIDTH = 234.0
LANE_CENTERS = [-390.0, -130.0, 130.0, 390.0]
NOTE_W, NOTE_H = 217.0, 28.0
JUDGE_SCREEN_Y = 1520
SPAWN_SCREEN_Y = 360
ROW3_BOTTOM_SCREEN_Y = 1920 - (960 + 796 - 17)     # HudLayout 顶部三行最低边 → 屏幕 y≈181
BAND_TOP = ROW3_BOTTOM_SCREEN_Y                     # 179
BAND_BOTTOM = SPAWN_SCREEN_Y                        # 360
BUTTON_Y = (BAND_TOP + BAND_BOTTOM) / 2.0           # ≈ 270

BG_DEEP = (8, 16, 21)
BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
LANE_EDGE = (34, 58, 66)
JUDGE = (207, 239, 234)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176)]
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
MUTED = (108, 130, 136)
WARN = (232, 126, 108)
ACCENT = (126, 224, 176)

GUITAR_HUE, DRUMS_HUE = 45.0, 0.0


def font(size, bold=False):
    names = ["C:/Windows/Fonts/msyhbd.ttc", "C:/Windows/Fonts/msyh.ttc"] if bold else \
            ["C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def mix(c0, c1, k):
    k = max(0.0, min(1.0, k))
    return tuple(int(round(c0[i] + (c1[i] - c0[i]) * k)) for i in range(3))


def hue_rgb(hue, s=0.72, v=0.90):
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(hue / 360.0, s, v))


def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def screen(x_center, y_center_up):
    return W / 2.0 + x_center, H / 2.0 - y_center_up


def draw_background(img, color, level=2):
    d = ImageDraw.Draw(img)
    for y in range(H):
        base = mix(BG_DEEP, BG, y / float(H))
        k = max(0.0, 1.0 - y / 1050.0) ** 1.2 if level == 2 else 0.0
        d.line([(0, y), (W, y)], fill=mix(base, color, 0.34 * k))


def draw_field(img, color):
    d = ImageDraw.Draw(img)
    edge = mix(LANE_EDGE, color, 0.75)
    for lane, cx0 in enumerate(LANE_CENTERS):
        x = W / 2.0 + cx0
        d.rectangle([x - LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160, x + LANE_WIDTH / 2, H - 40],
                    fill=mix(LANE_FILL, color, 0.10))
        d.line([(x - LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160), (x - LANE_WIDTH / 2, H - 40)], fill=edge, width=2)
        d.line([(x + LANE_WIDTH / 2, SPAWN_SCREEN_Y - 160), (x + LANE_WIDTH / 2, H - 40)], fill=edge, width=2)
        rounded(d, [x - LANE_WIDTH / 2 + 10, H - 92, x + LANE_WIDTH / 2 - 10, H - 30], 10,
                fill=mix(color, BG, 0.55), outline=mix(LANE_COLORS[lane], color, 0.25), width=2)
        # 一条下落中的音符（示意，颜色同上）
        ny = SPAWN_SCREEN_Y + 240 + lane * 120
        rounded(d, [x - NOTE_W / 2, ny, x + NOTE_W / 2, ny + NOTE_H], 6,
                fill=mix(LANE_COLORS[lane], BG, 0.12), outline=LANE_COLORS[lane], width=2)
    d.line([(0, JUDGE_SCREEN_Y), (W, JUDGE_SCREEN_Y)], fill=JUDGE, width=4)


def draw_hud(img):
    d = ImageDraw.Draw(img)
    # 顶部进度条 + 左右两列三行（只画占位块，够判断"会不会压"）
    rounded(d, [20, 20, 1060, 40], 8, fill=(20, 40, 48))
    for label, x, y, w in (("曲名", -220, 889, 560), ("分数", 290, 889, 420),
                           ("生命", -290, 833, 420), ("FEVER", 290, 833, 420),
                           ("进度文本", -280, 796, 440), ("蓄力条", 290, 796, 420)):
        cx, cy = screen(x, y)
        h = 34 if "条" not in label else 12
        rounded(d, [cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], 6, fill=(24, 44, 52))
        d.text((cx - w / 2 + 10, cy - 12), label, font=font(20), fill=MUTED)
    d.text((24, ROW3_BOTTOM_SCREEN_Y + 6), "↑ HUD 最低边 y≈181", font=font(20), fill=MUTED)
    d.text((24, SPAWN_SCREEN_Y - 26), "↓ 音符生成线 y=360", font=font(20), fill=MUTED)


def chip_text(d, cx, cy, w, h, name, color, active=True, pressed=False):
    fill = mix(BG, color, 0.30 if active else 0.10)
    if pressed:
        fill = mix(fill, (255, 255, 255), 0.18)
    outline = color if active else mix(LANE_EDGE, color, 0.25)
    rounded(d, [cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2], 14, fill=fill, outline=outline,
            width=3 if active else 2)
    f = font(40, active)
    tw = d.textlength(name, font=f)
    d.text((cx - tw / 2, cy - 26), name, font=f, fill=TEXT_BRIGHT if active else MUTED)
    if active:
        rounded(d, [cx - w / 2 + 12, cy + h / 2 - 12, cx + w / 2 - 12, cy + h / 2 - 6], 3, fill=color)


def candidate_b1(img, part="Guitar"):
    d = ImageDraw.Draw(img)
    cx, cy = W / 2.0, BUTTON_Y
    color = hue_rgb(GUITAR_HUE if part == "Guitar" else DRUMS_HUE)
    chip_text(d, cx, cy, 420, 96, "切换声部 · %s" % part, color)
    return "B1 单按钮（顶部中央）", "显示当前声部，点了切到另一件", color


def candidate_b2(img, part="Guitar", pressed=None):
    d = ImageDraw.Draw(img)
    cx, cy = W / 2.0, BUTTON_Y
    chip_text(d, cx - 165, cy, 300, 96, "GUITAR", hue_rgb(GUITAR_HUE), active=(part == "Guitar"),
              pressed=(pressed == "Guitar"))
    chip_text(d, cx + 165, cy, 300, 96, "DRUMS", hue_rgb(DRUMS_HUE), active=(part == "Drums"),
              pressed=(pressed == "Drums"))
    return "B2 双按钮（推荐）", "两件都摆出来，点另一件即切换", hue_rgb(DRUMS_HUE if part == "Drums" else GUITAR_HUE)


def candidate_b3(img, part="Guitar"):
    d = ImageDraw.Draw(img)
    color = hue_rgb(GUITAR_HUE if part == "Guitar" else DRUMS_HUE)
    rounded(d, [W / 2 - 330, BUTTON_Y - 48, W / 2 + 330, BUTTON_Y + 48], 14,
            outline=WARN, width=3, fill=mix(BG, WARN, 0.10))
    label = "B3 无按钮：手机上无法触发（判定区＝整屏）"
    f = font(32, True)
    tw = d.textlength(label, font=f)
    d.text(((W - tw) / 2, BUTTON_Y - 20), label, font=f, fill=WARN)
    return "B3 无按钮", "位置最干净，但手机切不了", color


def mock(part="Guitar", kind="B2", pressed=None, tint=None):
    img = Image.new("RGB", (W, H), BG_DEEP)
    color = tint or hue_rgb(GUITAR_HUE if part == "Guitar" else DRUMS_HUE)
    draw_background(img, color)
    draw_hud(img)
    draw_field(img, color)
    if kind == "B1":
        candidate_b1(img, part)
    elif kind == "B2":
        candidate_b2(img, part, pressed)
    else:
        candidate_b3(img, part)
    return img


def crop_band(img):
    top = int(BAND_TOP) - 40
    bottom = int(BAND_BOTTOM) + 40
    return img.crop((0, top, W, bottom))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "review", "part_switch")
    os.makedirs(out_dir, exist_ok=True)

    scale = 0.42
    fw, fh = int(W * scale), int(H * scale)
    band_src_h = BAND_BOTTOM - BAND_TOP + 80
    band_h = int(band_src_h * scale)
    sw, sh = int(W * 0.34), int(H * 0.34)

    width = max(40 + 3 * fw + 2 * 18 + 40, 40 + 4 * sw + 3 * 16 + 40)
    height = 150 + fh + 26 + band_h + 100 + 54 + sh + 110
    sheet = Image.new("RGB", (width, height), (7, 13, 17))
    d = ImageDraw.Draw(sheet)
    d.text((40, 34), "手机上的“切换声部”入口 · 过稿候选（判定区＝整屏，手势会与演奏抢事件，所以只能做控件）",
           font=font(36, True), fill=TEXT_BRIGHT)
    d.text((42, 88), "位置依据 HUD 布局表：顶部 HUD 最低边 y≈181、音符生成线 y=360 —— 中间 179px 的带子不压 HUD、也无音符经过",
           font=font(24), fill=TEXT)

    y_shot = 150
    y_band = y_shot + fh + 26
    for i, kind in enumerate(("B1", "B2", "B3")):
        img = mock(kind=kind)
        x = 40 + i * (fw + 18)
        sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y_shot))
        d.rectangle([x, y_shot, x + fw - 1, y_shot + fh - 1], outline=(40, 62, 70), width=1)
        band = crop_band(img).resize((fw, band_h), Image.LANCZOS)
        sheet.paste(band, (x, y_band))
        d.rectangle([x, y_band, x + fw - 1, y_band + band_h - 1], outline=(40, 62, 70), width=1)
        label, note, _ = (candidate_b1(Image.new("RGB", (1, 1)), "Guitar") if kind == "B1" else
                          candidate_b2(Image.new("RGB", (1, 1))) if kind == "B2" else
                          candidate_b3(Image.new("RGB", (1, 1))))
        d.text((x + 6, y_band + band_h + 8), label, font=font(28, True), fill=TEXT_BRIGHT)
        d.text((x + 6, y_band + band_h + 42), note, font=font(22), fill=TEXT)

    y_state = y_band + band_h + 100
    d.text((40, y_state), "B2 的状态（推荐档）：当前声部高亮 / 另一件可点 / 点击反馈 / 切换瞬间的徽记",
           font=font(34, True), fill=TEXT_BRIGHT)
    y_state += 54
    states = [("Guitar 生效", mock("Guitar")),
              ("Drums 生效", mock("Drums")),
              ("按下 DRUMS", mock("Guitar", pressed="Drums")),
              ("切换瞬间（徽记）", mock("Drums"))]
    for i, (label, img) in enumerate(states):
        x = 40 + i * (sw + 16)
        if i == 3:
            dd = ImageDraw.Draw(img)
            f = font(110, True)
            tw = dd.textlength("DRUMS", font=f)
            dd.text(((W - tw) / 2, 700), "DRUMS", font=f, fill=TEXT_BRIGHT,
                    stroke_width=3, stroke_fill=hue_rgb(DRUMS_HUE))
        sheet.paste(img.resize((sw, sh), Image.LANCZOS), (x, y_state))
        d.rectangle([x, y_state, x + sw - 1, y_state + sh - 1], outline=(40, 62, 70), width=1)
        d.text((x + 6, y_state + sh + 8), label, font=font(24), fill=TEXT)

    d.text((40, height - 46), "建议：B2 双按钮（顶部中央，落在 181~360 的带子里）+ 现有徽记；正式内容再换成商用素材。",
           font=font(28, True), fill=ACCENT)
    path = os.path.join(out_dir, "part_switch_button.png")
    sheet.save(path)
    print("saved %s  %dx%d" % (path, sheet.size[0], sheet.size[1]))


if __name__ == "__main__":
    main()
