#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""声部配色（全色相）与声部切换特效：过稿稿（**不进游戏**）。

作者意见：
  2026-10-03 第一稿 → 「背景换色强度三档看不出区别」「声部选全色相」。
  本稿据此重做两处：
    ① 色相＝全色相：五件各占一个明确色相，并按可辨性重排间隔 ——
       上一稿的琥珀(34°)与珊瑚(9°)只差 25°，而鼓↔吉恰是原型里切得最多的一对，
       切过去背景几乎不变。现在按色相环排序，最小间隔 45°（鼓↔吉），其余 ≥60°。
    ② 换色强度三档改成**结构性差异**（不是同一个渐变改 alpha）：
       G1 点灯：背景完全不动，只有顶端光带 + 轨道边 + 键帽上色
       G2 半场：背景上半 55% 渐变到该色相，判定线以下留反射
       G3 换底：整屏底色换成该色相的深色版
       每档都印出背景采样 RGB（y=200/900/1700）、相对 G1 的 RGB 总差与音符对比度，
       用数字确认三档真的不同，而不是靠观感。

约束（工程事实）：
  · 判定区（判定线上下 ~200 画布单位）在演奏中不能长期被遮住，每个切换候选都标了遮挡范围。
  · 音符颜色不改（NoteVisualConfig 的轨道青白族），声部色只上背景层；本稿额外给出
    "音符色 vs 背景"的对比度，用来卡 G3 的上限。

输出（art_tools/review/part_switch/）：
  part_palette.png           全色相五声部色卡 + 色相间隔 + 三档换色强度（带采样数字）
  part_switch_candidates.png 三个切换候选 × 4 帧（新配色 × G2）+ S2 在 G1/G2/G3 下的对照

运行：python -X utf8 art_tools/live_port/preview_part_switch.py
"""
import colorsys
import os

from PIL import Image, ImageDraw, ImageFont

# ── 与游戏一致的几何（Core/LaneLayout、PlayVisualSpec、NoteSpawner）──
W, H = 1080, 1920
LANE_WIDTH = 234.0
LANE_CENTERS = [-390.0, -130.0, 130.0, 390.0]   # 4 键
NOTE_W, NOTE_H = 217.0, 28.0
JUDGE_Y = 1520
SPAWN_Y = 360

BG_DEEP = (8, 16, 21)
BG = (10, 20, 26)
LANE_FILL = (16, 30, 36)
LANE_EDGE = (34, 58, 66)
JUDGE = (207, 239, 234)
LANE_COLORS = [(207, 239, 234), (168, 222, 218), (138, 201, 198), (111, 176, 176)]
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
ACCENT = (126, 224, 176)

# ── 全色相方案：按色相环排，最小间隔 45° ──────────────────────────────
PART_HUES = [
    ("Drums", 0, "朱红", "心跳：最暖的一支"),
    ("Guitar", 45, "琥珀", "主音"),
    ("Keys", 105, "黄绿", "和声：唯一偏亮的冷色"),
    ("Bass", 215, "靛蓝", "低音：最暗的一支"),
    ("Vocal", 295, "紫", "人声"),
]
PART_SAT, PART_VAL = 0.72, 0.90

LEVEL_NAME = {1: "G1 点灯", 2: "G2 半场", 3: "G3 换底"}
LEVEL_DESC = {
    1: "背景不动：只有顶端光带 + 轨道边 + 键帽上色",
    2: "背景上半 55% 渐变到该色相，判定线以下留反射",
    3: "整屏底色换成该色相深色版，需盯音符对比度",
}
LANE_TINT = {1: 0.0, 2: 0.10, 3: 0.22}
SAMPLE_Y = (200, 900, 1700)


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


def hsv_rgb(hue):
    return tuple(int(round(v * 255)) for v in colorsys.hsv_to_rgb(hue / 360.0, PART_SAT, PART_VAL))


def hue_gap(a, b):
    d = abs(a - b) % 360
    return int(round(min(d, 360 - d)))


def rel_lum(c):
    def f(v):
        v /= 255.0
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2])


def contrast(c0, c1):
    a, b = rel_lum(c0), rel_lum(c1)
    hi, lo = max(a, b), min(a, b)
    return (hi + 0.05) / (lo + 0.05)


def rgb_delta(c0, c1):
    return abs(c0[0] - c1[0]) + abs(c0[1] - c1[1]) + abs(c0[2] - c1[2])


def hexs(c):
    return "#%02X%02X%02X" % c


def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


# ── 背景：三档是结构差异，不是同一个渐变换 alpha ─────────────────────────
def bg_row(y, color, level):
    base = mix(BG_DEEP, BG, y / float(H))
    if level == 1:
        return base                                   # 背景完全不动
    if level == 2:
        k = max(0.0, 1.0 - y / 1050.0) ** 1.2
        row = mix(base, color, 0.34 * k)
        if y > 1500:                                  # 判定线以下的反射
            row = mix(row, color, 0.12 * (y - 1500) / float(H - 1500))
        return row
    top = mix(BG_DEEP, color, 0.34)                   # G3：整屏换成该色相深色版
    k = max(0.0, 1.0 - y / 1300.0)
    row = mix(top, color, 0.22 * k)
    if y > 1500:
        row = mix(row, color, 0.20 * (y - 1500) / float(H - 1500))
    return row


def draw_notes(draw):
    items = [(0, 1290), (2, 1180), (3, 1010), (1, 900), (2, 700), (0, 560)]
    cx = W / 2.0 + LANE_CENTERS[1]
    body_w = 120
    draw.rectangle([cx - body_w / 2, 560, cx + body_w / 2, 1120], fill=mix(LANE_COLORS[1], BG, 0.55))
    draw.rectangle([cx - body_w / 2, 560, cx + body_w / 2, 1120],
                   outline=mix(LANE_COLORS[1], BG, 0.15), width=2)
    for lane, y in items:
        x = W / 2.0 + LANE_CENTERS[lane]
        col = LANE_COLORS[lane]
        rounded(draw, [x - NOTE_W / 2, y, x + NOTE_W / 2, y + NOTE_H], 6,
                fill=mix(col, BG, 0.12), outline=col, width=2)
    rounded(draw, [cx - NOTE_W / 2, 1120, cx + NOTE_W / 2, 1120 + NOTE_H], 4,
            fill=mix(LANE_COLORS[1], BG, 0.05), outline=LANE_COLORS[1], width=2)


def draw_play(color, level):
    """一张演奏屏：背景按声部色相染到 level 档（1/2/3）。"""
    img = Image.new("RGB", (W, H), BG_DEEP)
    d = ImageDraw.Draw(img)
    for y in range(H):
        d.line([(0, y), (W, y)], fill=bg_row(y, color, level))

    if level == 1:   # 点灯：背景不动，只在顶端点一道光带
        for y in range(0, 190):
            a = 0.30 * (1.0 - y / 190.0) ** 1.4
            d.line([(0, y), (W, y)], fill=mix(bg_row(y, color, 1), color, a))

    edge = mix(LANE_EDGE, color, 0.75)
    for lane, cx0 in enumerate(LANE_CENTERS):
        x = W / 2.0 + cx0
        d.rectangle([x - LANE_WIDTH / 2, SPAWN_Y - 160, x + LANE_WIDTH / 2, H - 40],
                    fill=mix(LANE_FILL, color, LANE_TINT[level]))
        d.line([(x - LANE_WIDTH / 2, SPAWN_Y - 160), (x - LANE_WIDTH / 2, H - 40)], fill=edge, width=2)
        d.line([(x + LANE_WIDTH / 2, SPAWN_Y - 160), (x + LANE_WIDTH / 2, H - 40)], fill=edge, width=2)
        rounded(d, [x - LANE_WIDTH / 2 + 10, H - 92, x + LANE_WIDTH / 2 - 10, H - 30], 10,
                fill=mix(color, BG, 0.55), outline=mix(LANE_COLORS[lane], color, 0.25), width=2)

    for i in range(10):
        d.line([(0, JUDGE_Y - i), (W, JUDGE_Y - i)],
               fill=mix(bg_row(JUDGE_Y, color, level), JUDGE, 0.10 * (1 - i / 10.0)))
    d.line([(0, JUDGE_Y), (W, JUDGE_Y)], fill=JUDGE, width=4)
    draw_notes(d)
    return img


def chip(img, part, color, note=""):
    d = ImageDraw.Draw(img)
    rounded(d, [40, 40, 500, 150], 16, fill=mix(BG_DEEP, color, 0.12), outline=color, width=3)
    d.text((72, 62), part, font=font(46, True), fill=TEXT_BRIGHT)
    d.text((74, 116), note, font=font(25), fill=TEXT)


def bar(img, color, energy, t):
    d = ImageDraw.Draw(img)
    rounded(d, [W - 420, 40, W - 40, 150], 16, fill=mix(BG_DEEP, color, 0.12), outline=color, width=3)
    d.text((W - 392, 62), "Energy  %d" % energy, font=font(40, True), fill=TEXT_BRIGHT)
    d.text((W - 394, 116), "SongTime %.2fs" % t, font=font(26), fill=TEXT)


# ── 三个切换候选 ────────────────────────────────────────────────────────
def effect_s1(img_old, img_new, color_new, t):
    img = Image.blend(img_old, img_new, min(1.0, t / 0.35))
    if t <= 0.55:
        d = ImageDraw.Draw(img, "RGBA")
        y = int(1700 - 1500 * min(1.0, t / 0.55))
        for i in range(-190, 190):
            a = int(78 * (1 - abs(i) / 190.0) ** 2)
            if a > 0:
                d.line([(0, y + i), (W, y + i)], fill=color_new + (a,))
    return img


def effect_s2(img_new, color_new, t):
    img = img_new.copy()
    d = ImageDraw.Draw(img, "RGBA")
    ring = int(70 + 780 * min(1.0, t / 0.45))
    fade = max(0.0, 1.0 - t / 0.55)
    for i, wd in ((0, 10), (26, 4)):
        if fade > 0.02:
            d.ellipse([W / 2 - ring - i, JUDGE_Y - ring - i, W / 2 + ring + i, JUDGE_Y + ring + i],
                      outline=color_new + (int(200 * fade),), width=wd)
    if t < 0.30:
        d.line([(0, JUDGE_Y), (W, JUDGE_Y)], fill=JUDGE + (220,), width=8)
    a = max(0.0, 1.0 - t / 0.62)
    if a > 0.02:
        f = font(120, True)
        wpx = d.textlength("DRUMS", font=f)
        d.text(((W - wpx) / 2, 700), "DRUMS", font=f,
               fill=TEXT_BRIGHT + (int(235 * a),), stroke_width=3, stroke_fill=color_new + (int(150 * a),))
        f2 = font(38)
        sub = "Tab 切换声部"
        w2 = d.textlength(sub, font=f2)
        d.text(((W - w2) / 2, 850), sub, font=f2, fill=TEXT + (int(200 * a),))
    for side in (-1, 1):
        x = W / 2.0 + side * (abs(LANE_CENTERS[0]) + LANE_WIDTH / 2 + 22)
        d.rectangle([x - 14, SPAWN_Y - 160, x + 14, H - 40], fill=color_new + (int(90 * fade),))
    return img


def effect_s3(img_old, img_new, color_new, t):
    img = img_old.copy()
    b = int(60 + 1820 * min(1.0, t / 0.45))
    if b > 0:
        img.paste(img_new.crop((0, 0, W, min(b, H))), (0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    for off, a in ((0, 230), (210, 120), (420, 70)):
        yy = b - off
        if 0 < yy < H:
            d.line([(0, yy), (W, yy)], fill=color_new + (a,), width=4)
    return img


def frame_screen(name, params, t):
    if name == "S1":
        img = effect_s1(params["img_old"], params["img_new"], params["new"], t)
        part, color = (params["from_name"], params["old"]) if t < 0.35 else (params["to_name"], params["new"])
    elif name == "S2":
        img = effect_s2(params["img_new"], params["new"], t)
        part, color = params["to_name"], params["new"]
    else:
        img = effect_s3(params["img_old"], params["img_new"], params["new"], t)
        part, color = (params["to_name"], params["new"]) if t >= 0.45 else (params["from_name"], params["old"])
    chip(img, part, color, "%s → %s" % (params["from_name"], params["to_name"]))
    bar(img, color, params["energy"], params["song_time"])
    return img


def build_switch_sheet(out_dir):
    guitar = hsv_rgb(45)
    drums = hsv_rgb(0)
    params = dict(from_name="Guitar", to_name="Drums", old=guitar, new=drums, energy=37, song_time=11.84)
    params["img_old"] = draw_play(guitar, 2)
    params["img_new"] = draw_play(drums, 2)

    frames = [0.0, 0.12, 0.35, 0.80]
    scale = 0.30
    fw, fh = int(W * scale), int(H * scale)
    header, row_label, note_h, gap = 150, 56, 78, 26
    width = 40 + 4 * fw + 3 * 18 + 40
    height = header + 3 * (row_label + fh + note_h + gap) + 60 + fh + 60
    sheet = Image.new("RGB", (width, height), (7, 13, 17))
    d = ImageDraw.Draw(sheet)
    d.text((40, 34), "声部切换特效 · 过稿候选（全色相：Guitar 琥珀 45° → Drums 朱红 0°）",
           font=font(40, True), fill=TEXT_BRIGHT)
    d.text((42, 92), "每行 4 帧：0.00 / 0.12 / 0.35 / 0.80 秒；背景强度固定 G2（半场），最后一行是同一特效在 G1 / G2 / G3 下的对照",
           font=font(26), fill=TEXT)

    rows = [
        ("S1  全场换色 + 色相扫过", "最不打断读谱；0.35s 完成换色，亮带自下而上扫过。遮挡：亮带经过处短暂压暗音符（约 0.2s）。"),
        ("S2  冲击环 + 声部徽记", "最醒目；立刻换色 + 判定线冲击环 + 中央声部名（0.6s 淡出）。遮挡：中央文字在 700~900px，不压判定区。"),
        ("S3  翻面扫过", "最像“换了件乐器”；三条翻面线自上而下扫过，扫过处换色。遮挡：翻面线经过处有一道亮边（约 0.3s）。"),
    ]
    y = header
    for name, note in rows:
        d.text((40, y + 8), name, font=font(38, True), fill=TEXT_BRIGHT)
        y += row_label
        for i, t in enumerate(frames):
            img = frame_screen(name.split()[0], params, t)
            x = 40 + i * (fw + 18)
            sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y))
            d.rectangle([x, y, x + fw - 1, y + fh - 1], outline=(40, 62, 70), width=1)
            tag = "切换前" if (t == 0.0 and name.startswith(("S1", "S3"))) else ("瞬间" if t == 0.0 else "%.2fs" % t)
            d.text((x + 6, y + fh + 8), tag, font=font(24), fill=TEXT)
        d.text((40, y + fh + 36), note, font=font(25), fill=TEXT)
        y += fh + note_h + gap

    d.text((40, y + 8), "同一特效（S2 瞬间）在 G1 点灯 / G2 半场 / G3 换底 下：三档是结构性差异，不是改 alpha",
           font=font(32, True), fill=TEXT_BRIGHT)
    y += 58
    for i, level in enumerate((1, 2, 3)):
        img = effect_s2(draw_play(drums, level), drums, 0.0)
        chip(img, "Drums " + LEVEL_NAME[level], drums, LEVEL_DESC[level])
        bar(img, drums, 37, 11.84)
        x = 40 + i * (fw + 18)
        sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + fw - 1, y + fh - 1], outline=(40, 62, 70), width=1)
        samples = [bg_row(sy, drums, level) for sy in SAMPLE_Y]
        d.text((x + 6, y + fh + 8), "%s  背景 %s / %s / %s" % (LEVEL_NAME[level], hexs(samples[0]), hexs(samples[1]), hexs(samples[2])),
               font=font(22), fill=TEXT)
    path = os.path.join(out_dir, "part_switch_candidates.png")
    sheet.save(path)
    return path, sheet.size


def swatch_row(sheet, y, title, note, palette):
    d = ImageDraw.Draw(sheet)
    d.text((40, y), title, font=font(38, True), fill=TEXT_BRIGHT)
    d.text((40, y + 50), note, font=font(26), fill=TEXT)
    x = 40
    cw, ch = 306, 214
    for index, (name, hue, hue_name, usage) in enumerate(palette):
        color = hsv_rgb(hue)
        nxt = palette[(index + 1) % len(palette)]
        card_y = y + 92
        rounded(d, [x, card_y, x + cw, card_y + ch], 14, fill=mix(BG_DEEP, color, 0.10), outline=color, width=2)
        rounded(d, [x + 16, card_y + 14, x + cw - 16, card_y + 86], 10, fill=color)
        d.text((x + 18, card_y + 98), "%s  %s" % (name, hue_name), font=font(30, True), fill=TEXT_BRIGHT)
        d.text((x + 18, card_y + 134), "%s  色相 %d°  与 %s 差 %d°"
               % (hexs(color), hue, nxt[0], hue_gap(hue, nxt[1])), font=font(22), fill=TEXT)
        d.text((x + 18, card_y + 160), "与底色对比 %.1f:1" % contrast(color, BG_DEEP), font=font(22), fill=TEXT)
        d.text((x + 18, card_y + 186), usage, font=font(22), fill=TEXT)
        x += cw + 14
    return y + 92 + ch + 46


def build_palette_sheet(out_dir):
    width = 40 + 5 * 306 + 4 * 14 + 40
    scale = 0.35
    fw, fh = int(W * scale), int(H * scale)
    height = 150 + 352 + 130 + fh + 190
    sheet = Image.new("RGB", (width, height), (7, 13, 17))
    d = ImageDraw.Draw(sheet)
    d.text((40, 34), "声部色相（全色相 · 已选）与背景换色强度三档", font=font(40, True), fill=TEXT_BRIGHT)
    d.text((42, 92), "上一稿琥珀(34°)与珊瑚(9°)只差 25°，本稿按色相环重排，最小间隔 45°（鼓↔吉）；音符色不改，声部色只上背景",
           font=font(26), fill=TEXT)
    y = swatch_row(sheet, 150, "五个声部各占一个明确色相", "色相角按色相环排序，一眼分辨“现在是哪件乐器”", PART_HUES)

    guitar, drums = hsv_rgb(45), hsv_rgb(0)
    d.text((40, y), "换色强度三档（同一把 Guitar，琥珀 45°）—— 结构与采样都不同", font=font(38, True), fill=TEXT_BRIGHT)
    y += 52
    g1 = [bg_row(sy, guitar, 1) for sy in SAMPLE_Y]
    for i, level in enumerate((1, 2, 3)):
        img = draw_play(guitar, level)
        samples = [bg_row(sy, guitar, level) for sy in SAMPLE_Y]
        delta = sum(rgb_delta(samples[j], g1[j]) for j in range(len(SAMPLE_Y)))
        chip(img, "Guitar " + LEVEL_NAME[level], guitar, LEVEL_DESC[level])
        bar(img, guitar, 41, 23.10)
        x = 40 + i * (fw + 14)
        sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + fw - 1, y + fh - 1], outline=(40, 62, 70), width=1)
        d.text((x + 6, y + fh + 10), "%s   音符/背景对比 %.1f:1"
               % (LEVEL_NAME[level], contrast(LANE_COLORS[1], samples[1])), font=font(24), fill=TEXT_BRIGHT)
        d.text((x + 6, y + fh + 42), "背景 %s / %s / %s" % tuple(hexs(c) for c in samples), font=font(20), fill=TEXT)
        d.text((x + 6, y + fh + 68), "与 G1 的 RGB 总差 %d" % delta, font=font(20), fill=TEXT)

    x = 40 + 3 * (fw + 14)
    img = draw_play(drums, 2)
    chip(img, "Drums G2", drums, "换一件乐器：同一档强度下的朱红")
    bar(img, drums, 41, 23.10)
    sheet.paste(img.resize((fw, fh), Image.LANCZOS), (x, y))
    d.rectangle([x, y, x + fw - 1, y + fh - 1], outline=(40, 62, 70), width=1)
    d.text((x + 6, y + fh + 10), "Drums × G2 对照", font=font(24), fill=TEXT_BRIGHT)

    d.text((40, height - 48), "建议：全色相 × G2 + S2 徽记（切换瞬间看得见）叠加 S1 的 0.35s 换色（过程不刺眼）。",
           font=font(30, True), fill=ACCENT)
    path = os.path.join(out_dir, "part_palette.png")
    sheet.save(path)
    return path, sheet.size


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = os.path.join(root, "review", "part_switch")
    os.makedirs(out_dir, exist_ok=True)
    for path, size in (build_palette_sheet(out_dir), build_switch_sheet(out_dir)):
        print("saved %s  %dx%d" % (path, size[0], size[1]))


if __name__ == "__main__":
    main()
