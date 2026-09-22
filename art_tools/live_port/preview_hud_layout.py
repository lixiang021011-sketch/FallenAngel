#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""演奏页 HUD 总装对照稿（前端清单 B5，过稿用，不进游戏）。

作者口径：**只做布局与显示条件**（"命中反馈和视觉特效不抄"），不搬上游皮肤/特效；
参考上游 `LiveFrontView` 的元素关系（本地克隆里的真布局在
`Assets/Resources/live/view/FrontUIView.prefab`，749KB / 26 个 RectTransform）。

本稿把**当前代码里各元素的锚点**（SceneBuilder.CreateHUD 的常量）原样画出来，再给一版收拢后的提案：

  画布 1080×1920，中心为原点（y 向上为正）。当前锚点（画布中心系 y）：
    进度条      +950 … +930（贴顶，左右各留 20）
    曲名        +920（左）
    分数        +900（右，文本框 800 宽、右对齐）
    生命槽      +848（左，420×30）+ 数值文本 x≈-66
    FEVER 字样  +886（中，360×54）… 与分数文本框同高
    蓄力条      +840（中，300×12）
    combo 数字位 +250（96px，两位时左右各偏 46）
    "COMBO"     +165
    判定文字    +100（字号 72）
    早晚描述    +40（字号 36）
    判定线      -560
    cut-in 卡片 +230（820×170 → 占 +145…+315）← **与 combo 数字位（+202…+298）重叠**

输出：art_tools/review/hud_layout/hud_layout_compare.png
运行：python -X utf8 art_tools/live_port/preview_hud_layout.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
CCY = H / 2                      # 画布中心（像素）
JUDGE_Y = -560                   # 判定线（画布中心系）
BG = (8, 16, 22)
PANEL = (14, 26, 32)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
WARN = (232, 120, 108)
DIM = (74, 104, 110)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "hud_layout"))


def font(size, bold=False):
    names = (["msyhbd.ttc", "simhei.ttf"] if bold else []) + ["msyh.ttc", "simhei.ttf", "arial.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def y2px(y):
    """画布中心系 y → 屏幕像素 y（自上而下）。"""
    return CCY - y


def box(draw, x, y, w, h, label, color=ACCENT, fill=None, warn=False, size=22):
    x0, y0 = x - w / 2, y2px(y) - h / 2
    if fill:
        draw.rectangle([x0, y0, x0 + w, y0 + h], fill=fill)
    draw.rectangle([x0, y0, x0 + w, y0 + h], outline=WARN if warn else color, width=3 if warn else 2)
    draw.text((x0 + 8, y0 + 6), label, font=font(size, True), fill=WARN if warn else color)


def field(draw):
    """演奏区参考：轨道 + 判定线。"""
    lane = 168
    x0 = W / 2 - lane * 2.5
    for i in range(5):
        draw.rectangle([x0 + i * lane + 6, y2px(1200), x0 + (i + 1) * lane - 6, y2px(JUDGE_Y)],
                       fill=(10, 20, 26))
    draw.rectangle([0, y2px(JUDGE_Y) - 3, W, y2px(JUDGE_Y) + 3], fill=(207, 239, 234))
    draw.text((16, y2px(JUDGE_Y) + 10), "判定线 y=-560", font=font(22), fill=DIM)
    draw.text((16, y2px(1200) + 4), "生成点 y=+1200（屏外）", font=font(22), fill=DIM)


def current_layout(draw):
    field(draw)
    # 进度条（贴顶）
    draw.rectangle([20, y2px(950), W - 20, y2px(930)], fill=(30, 60, 66))
    draw.text((26, y2px(950) + 2), "进度条 +950…+930", font=font(20), fill=DIM)
    # 左：曲名 / 生命槽 / 数值
    box(draw, 40 + 400, 920, 800, 60, "曲名（左，+920）", color=TEXT)
    box(draw, 40 + 210, 848, 420, 30, "生命槽（左，+848）", color=ACCENT)
    box(draw, -66 + 70, 856, 140, 40, "数值", color=ACCENT)
    # 右：分数（文本框 800 宽右对齐）
    box(draw, 0, 900, 800, 120, "分数文本框（中锚点 + 右对齐，800 宽）", color=TEXT)
    # 中：FEVER + 蓄力条
    box(draw, 0, 886, 360, 54, "FEVER 字样（中，+886）", color=(255, 140, 200))
    box(draw, 0, 840, 300, 12, "蓄力条（中，+840）", color=(255, 140, 200))
    # combo
    box(draw, 0, 250, 300, 96, "combo 数字位 +250", color=ACCENT, warn=True)
    draw.text((W / 2 - 60, y2px(165) - 10), "COMBO +165", font=font(20, True), fill=ACCENT)
    # 判定文字
    box(draw, 0, 100, 520, 72, "判定文字 +100", color=TEXT)
    box(draw, 0, 40, 320, 36, "早晚描述 +40", color=TEXT)
    # cut-in 卡片（重叠）
    box(draw, 0, 230, 820, 170, "cut-in 卡片 +230（与 combo 重叠！）", color=WARN, warn=True)


def proposed_layout(draw):
    field(draw)
    # 顶部：进度条
    draw.rectangle([20, y2px(950), W - 20, y2px(930)], fill=(30, 60, 66))
    draw.text((26, y2px(950) + 2), "① 进度条 +950…+930", font=font(20), fill=DIM)
    # 第二行：左曲名 / 右分数（收窄 —— 中间让空，长分数也顶不到中心）
    # 左列三行：曲名 / 生命槽+数值 / 进度文本    右列三行：分数 / FEVER / 蓄力条
    box(draw, -220, 898, 560, 56, "① 曲名（左列 +905）", color=TEXT)
    box(draw, -290, 848, 420, 30, "② 生命槽 +848", color=ACCENT)
    box(draw, -5, 848, 130, 34, "数值", color=ACCENT)
    box(draw, -280, 800, 440, 34, "③ 进度文本 +800（左列）", color=TEXT)
    box(draw, 290, 898, 420, 56, "① 分数（右列 +905，宽 420）", color=TEXT)
    box(draw, 290, 840, 420, 46, "② FEVER +840（右列）", color=(255, 140, 200))
    box(draw, 290, 800, 420, 12, "③ 蓄力条 +800（右列）", color=(255, 140, 200))
    # 中部：cut-in 上移到 combo 上方；combo 保持；判定文字保持
    box(draw, 0, 470, 820, 150, "③ cut-in 卡片 +470（让开 combo）", color=ACCENT)
    box(draw, 0, 250, 300, 96, "④ combo 数字位 +250（不变）", color=ACCENT)
    draw.text((W / 2 - 60, y2px(165) - 10), "COMBO +165", font=font(20, True), fill=ACCENT)
    box(draw, 0, 100, 520, 72, "⑤ 判定文字 +100（不变）", color=TEXT)
    box(draw, 0, 40, 320, 36, "早晚描述 +40（不变）", color=TEXT)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    panels = [("现状（标红 = 重叠）", current_layout), ("提案（收拢后）", proposed_layout)]
    images = []
    for name, fn in panels:
        img = Image.new("RGB", (W, H), BG)
        d = ImageDraw.Draw(img)
        d.text((30, 24), name, font=font(40, True), fill=TITLE)
        fn(d)
        images.append(img)

    scale = 0.52
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 2 + 120, ph + 220), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((40, 26), "FallenAngel · B5 演奏页 HUD 总装对照稿（只改布局与显示条件，不碰皮肤/特效）",
            font=font(38, True), fill=TITLE)
    for i, ln in enumerate([
        "现状问题：① cut-in 卡片（+145…+315）压住 combo 数字位（+202…+298）；② 分数文本框 800 宽，",
        "中心区域被它盖住，FEVER 字样（+886）与它同高，4~5 位分数会顶到中间；③ 顶部三行（进度/曲名+分数/生命槽）间距不均。",
        "提案：顶部两行分明（① 进度 / 曲名+分数；② 生命槽+数值 / FEVER+蓄力条），cut-in 上移到 +470 让开 combo，",
        "combo（+250）与判定文字（+100/+40）保持不动 —— 判定反馈的位置已过稿，这次不动它。",
    ]):
        sd.text((40, 78 + i * 30), ln, font=font(23), fill=TEXT)
    for i, img in enumerate(images):
        x = 30 + i * (pw + 30)
        sd.text((x + 6, 208), panels[i][0], font=font(24, True), fill=ACCENT)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 238))
        sd.rectangle([x, 238, x + pw, 238 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(OUT_DIR, "hud_layout_compare.png"))
    print("已输出：", OUT_DIR)


if __name__ == "__main__":
    main()
