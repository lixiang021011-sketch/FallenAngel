#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""层间过渡 + 自身状态栏 + 装备栏 的过稿稿（铁律 8：先出图，过了再动代码）。

作者口径（2026-09-27）："下一层要做进入下一层的加载动画、自身状态栏、装备栏，
不同层级信息细节度不同。"

本稿给三张对照图（都在 1080×1920 竖屏画布上）：

  01_layer_transition.png  进入下一层的过渡候选 3 版（① 黑场层名 ② 层名+一句话+细进度 ③ 地图推近层名浮出），
                           每版标注时长 / 可否跳过 / 什么时候出现；推荐 ②。
  02_status_bar.png        自身状态栏 ×三层细节度：第 1 层全字段、第 2 层收起来历、第 3 层只剩
                           现金 / 层与步 / 负面（人情与等级收进册子）。字段来源见 docs/layer-ui-spec.md。
  03_gear_bar.png          装备栏 8 格 ×三层细节度 + 一张"点开的详情卡"（名字 / 一句话 / 来历）。
                           稀有度沿用设计稿：白 40 / 蓝 80 / 金 160。

输出：art_tools/review/layer_ui/*.png
运行：python -X utf8 art_tools/live_port/preview_layer_ui.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
BG = (8, 16, 22)
PANEL = (14, 26, 32)
LINE = (38, 58, 66)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
WARN = (232, 120, 108)
DIM = (74, 104, 110)
WHITE = (206, 214, 220)
BLUE = (120, 168, 232)
GOLD = (232, 196, 118)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "layer_ui"))


def font(size, bold=False):
    names = (["msyhbd.ttc", "simhei.ttf"] if bold else []) + ["msyh.ttc", "simhei.ttf", "arial.ttf"]
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def canvas(title, subtitle):
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    d.text((40, 34), title, font=font(46, True), fill=TITLE)
    d.text((42, 96), subtitle, font=font(24), fill=TEXT)
    d.line([(40, 140), (W - 40, 140)], fill=LINE, width=2)
    return img, d


def panel(d, x, y, w, h, outline=LINE, fill=PANEL):
    d.rounded_rectangle([x, y, x + w, y + h], radius=18, fill=fill, outline=outline, width=2)


def tag(d, x, y, text, color=ACCENT):
    f = font(22, True)
    tw = d.textlength(text, font=f)
    d.rounded_rectangle([x, y, x + tw + 26, y + 40], radius=20, outline=color, width=2, fill=(12, 24, 30))
    d.text((x + 13, y + 8), text, font=f, fill=color)
    return x + tw + 26


def chip(d, x, y, label, value, color=TITLE, width=None, dim=False):
    f_label, f_value = font(20), font(30, True)
    w = width or (d.textlength(label, font=f_label) + d.textlength(value, font=f_value) + 52)
    panel(d, x, y, w, 74, outline=DIM if dim else LINE, fill=(12, 22, 28))
    d.text((x + 16, y + 10), label, font=f_label, fill=DIM if dim else TEXT)
    d.text((x + 16, y + 34), value, font=f_value, fill=DIM if dim else color)
    return x + w + 14


def flow_chips(d, x, y, max_x, items, line_gap=12):
    """按可用宽度自动换行地画 chip 组；返回用掉的总高度（自检过稿时不会有字段溢出画布）。"""
    f_label, f_value = font(20), font(30, True)
    cx, cy, used = x, y, 0
    for label, value, color, dim in items:
        w = d.textlength(label, font=f_label) + d.textlength(value, font=f_value) + 52
        if cx > x and cx + w > max_x:
            cx = x
            cy += 74 + line_gap
        chip(d, cx, cy, label, value, color=color, width=w, dim=dim)
        cx += w + 14
        used = max(used, cy - y + 74)
    return used


def progress(d, x, y, w, h, ratio, color=ACCENT):
    d.rounded_rectangle([x, y, x + w, y + h], radius=h // 2, fill=(24, 40, 46))
    if ratio > 0:
        d.rounded_rectangle([x, y, x + w * ratio, y + h], radius=h // 2, fill=color)


def gear_cell(d, x, y, size, rarity=None, used=False, unknown=False, index=0):
    fill = (10, 20, 26)
    outline = DIM
    if rarity == "white":
        outline = WHITE
    elif rarity == "blue":
        outline = BLUE
    elif rarity == "gold":
        outline = GOLD
    if unknown:
        outline = DIM
    d.rounded_rectangle([x, y, x + size, y + size], radius=14, fill=fill, outline=outline, width=3)
    if rarity is None and not unknown:
        d.text((x + size / 2 - 12, y + size / 2 - 20), "+", font=font(40, True), fill=DIM)
        return
    if unknown:
        d.text((x + size / 2 - 10, y + size / 2 - 22), "?", font=font(40, True), fill=DIM)
        return
    # 器材剪影（占位几何：一个方块 + 一条线，正式美术另出）
    d.rounded_rectangle([x + size * 0.22, y + size * 0.26, x + size * 0.78, y + size * 0.74],
                        radius=8, outline=outline, width=2)
    d.line([(x + size * 0.28, y + size * 0.62), (x + size * 0.72, y + size * 0.38)],
           fill=outline, width=2)
    if used:
        d.ellipse([x + size - 30, y + 8, x + size - 8, y + 30], fill=WARN)
    d.text((x + 10, y + size - 26), str(index), font=font(18), fill=DIM)


def build_transition():
    img, d = canvas("进入下一层：过渡候选", "只在跨层时出现 · 可跳过 · 时长上限 1.5s · 后台加载不阻塞")
    y = 170
    blocks = [
        ("①", "黑场 + 层名淡入", "1.2s",
         [("中间大字", "第二层"), ("下方一行", "还在这座城市里"), ("右下", "任意键跳过")]),
        ("②", "层名 + 一句话 + 细进度（推荐）", "1.5s",
         [("上方小字", "跨过一段没人的路"), ("中间大字", "第二层"), ("下方一行", "灯亮起来了"), ("底部", "细进度条 · 任意键跳过")]),
        ("③", "地图推近 + 层名浮出", "0.8s",
         [("保留地图", "房间与连线做位移"), ("中间浮字", "第二层"), ("特点", "不打断视线，但存在感弱")]),
    ]
    for idx, (mark, name, dur, rows) in enumerate(blocks):
        h = 500
        panel(d, 40, y, W - 80, h)
        d.text((70, y + 24), mark + "  " + name, font=font(32, True), fill=TITLE)
        d.text((W - 210, y + 30), dur, font=font(28, True), fill=ACCENT)
        if idx == 1:
            tag(d, W - 210 - 150, y + 26, "推荐", color=ACCENT)
        # 屏幕示意
        sx, sy, sw, sh = 70, y + 96, 380, 370
        d.rounded_rectangle([sx, sy, sx + sw, sy + sh], radius=20, fill=(6, 12, 16), outline=LINE, width=2)
        for i, (label, value) in enumerate(rows):
            ry = sy + 46 + i * 74
            if idx == 2 and i == 0:
                # 地图缩略
                for k in range(4):
                    lx = sx + 60 + k * 70
                    d.rounded_rectangle([lx, sy + 40, lx + 46, sy + 86], radius=8, outline=DIM, width=2)
                    if k < 3:
                        d.line([(lx + 46, sy + 63), (lx + 70, sy + 63)], fill=DIM, width=2)
                d.text((sx + 60, sy + 110), "地图继续可见", font=font(22), fill=TEXT)
                d.text((sx + 60, sy + 190), value, font=font(44, True), fill=TITLE)
                continue
            if "大字" in label:
                d.text((sx + 60, ry), value, font=font(52, True), fill=TITLE)
            elif "进度" in label:
                d.text((sx + 60, ry - 16), value, font=font(20), fill=TEXT)
                progress(d, sx + 60, ry + 18, sw - 120, 12, 0.42)
            else:
                d.text((sx + 60, ry), label + "：" + value, font=font(22), fill=TEXT)
        # 右侧说明
        notes = {
            0: ["何时出现：跨层的第一步之前", "会不会挡住加载：不会（后台并行）", "跳过：任意键 / 点击"],
            1: ["何时出现：跨层的第一步之前", "一句话来自这一层的主题", "进度条只是「呼吸」，不表示真实加载"],
            2: ["何时出现：地图从上一层滑出的一刻", "适合「不打断」的快速通过", "深层信息少时，它自己就是信息"],
        }[idx]
        for i, n in enumerate(notes):
            d.text((sx + sw + 40, sy + 30 + i * 62), "· " + n, font=font(23), fill=TEXT)
        y += h + 24
    return img


def build_status_bar():
    img, d = canvas("自身状态栏：三层信息细节度", "字段来源：局内快照（现金 / 人情 / 局内等级 / 负面 / 旅伴）")
    y = 170
    layers = [
        (1, "第 1 层 · 全字段", [
            ("现金", "¥ 240", TITLE, False),
            ("人情", "2", ACCENT, False),
            ("局内等级", "Lv.3 ▮▮▮▯", TITLE, False),
            ("负面", "手冷", WARN, False),
            ("旅伴", "卖艺的姑娘 · 本层", ACCENT, False),
        ], "全部展开：来历、等级进度、负面名称、旅伴与同行范围都写出来"),
        (2, "第 2 层 · 收起来历", [
            ("现金", "¥ 410", TITLE, False),
            ("人情", "1", ACCENT, False),
            ("局内等级", "Lv.5", TITLE, False),
            ("负面", "走神", WARN, False),
            ("旅伴", "开货车的司机", ACCENT, False),
        ], "装备不再显示来历；等级只给数字；旅伴不再写「本层」——玩家已经知道规矩"),
        (3, "第 3 层 · 只剩三样", [
            ("现金", "¥ 690", TITLE, False),
            ("层与步", "第 3 层 · 第 12 站", TITLE, False),
            ("负面", "想家", WARN, False),
            ("人情", "—", DIM, True),
            ("局内等级", "—", DIM, True),
        ], "人情与等级收进册子；只看得到现金、走到哪儿、身上背着什么"),
    ]
    for idx, (layer, name, chips, note) in enumerate(layers):
        h = 420
        panel(d, 40, y, W - 80, h)
        d.text((70, y + 24), name, font=font(34, True), fill=TITLE)
        # 状态栏本体（画两条横条示意"行程中"版式）
        bx, by = 70, y + 96
        bar_h = 180
        d.rounded_rectangle([bx, by, W - 70, by + bar_h], radius=16, fill=(10, 20, 26), outline=LINE, width=2)
        flow_chips(d, bx + 20, by + 14, W - 90, chips)
        d.text((70, y + 292), "· " + note, font=font(23), fill=TEXT)
        # 层与步 / 装备数提示
        d.text((70, y + 330), "地图上始终显示：第 %d 层 · 走到第几站" % layer, font=font(23), fill=TEXT)
        if idx == 2:
            d.text((70, y + 366), "第 3 层起：人情与等级只在册子里看得到（深层更「不确定」）",
                   font=font(23), fill=ACCENT)
        y += h + 40
    return img


def build_gear_bar():
    img, d = canvas("装备栏：8 格 × 三层信息细节度", "设计稿 §5.2：包 8 格 · 白 40 / 蓝 80 / 金 160 · 满格要置换")
    y = 170
    size = 100
    gap = 12

    # 第 1 层：全信息（稀有度色 + 编号 + 使用中 + 空位）
    panel(d, 40, y, W - 80, 330)
    d.text((70, y + 22), "第 1 层 · 名字 + 稀有度 + 来历（点开看一句话）", font=font(30, True), fill=TITLE)
    gx = 70
    rarities = ["white", "blue", "gold", "white", "blue", None, None, None]
    for i, r in enumerate(rarities):
        gear_cell(d, gx, y + 80, size, rarity=r, used=(i == 3), index=i + 1)
        gx += size + gap
    d.text((70, y + 226), "器材 5/8 · 已用掉的件打红点（一次性）· 金色是本局最贵的那件",
           font=font(23), fill=TEXT)
    y += 354

    # 第 2 层：只有图标 + 稀有度点，不显示名字
    panel(d, 40, y, W - 80, 280)
    d.text((70, y + 22), "第 2 层 · 只留图标与稀有度（点开才出名字）", font=font(30, True), fill=TITLE)
    gx = 70
    for i, r in enumerate(rarities):
        gear_cell(d, gx, y + 74, size, rarity=r, used=(i == 3), index=i + 1)
        gx += size + gap
    d.text((70, y + 200), "理由：走到第二层，玩家已经认得自己的东西；把「名字」收起来，界面更安静",
           font=font(23), fill=TEXT)
    y += 304

    # 第 3 层：统一灰 + 疑问
    panel(d, 40, y, W - 80, 280)
    d.text((70, y + 22), "第 3 层 · 统一灰、连稀有度也不显示（深层：东西还是你的，但你不再数它们）",
           font=font(28, True), fill=TITLE)
    gx = 70
    for i in range(8):
        gear_cell(d, gx, y + 74, size, rarity="white" if i < 5 else None,
                  used=(i == 3), unknown=(i < 5), index=i + 1)
        gx += size + gap
    d.text((70, y + 200), "反例警告：这一档会削弱「刚买到好东西」的爽感——如果作者觉得太狠，可只保留稀有度描边",
           font=font(23), fill=WARN)
    y += 304

    # 详情卡
    panel(d, 40, y, W - 80, 260, outline=ACCENT)
    d.text((70, y + 22), "点开的详情卡（任一层都可用）", font=font(30, True), fill=TITLE)
    d.text((70, y + 74), "热水袋", font=font(40, True), fill=TITLE)
    d.text((70, y + 128), "手热了才弹得动。—— 一开场的那半分钟，你会更稳。", font=font(24), fill=TEXT)
    d.text((70, y + 170), "来历：出门时塞进包里的", font=font(24), fill=ACCENT)
    d.text((70, y + 206), "白 · 40 · 第 1 层买到", font=font(22), fill=DIM)
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    build_transition().save(os.path.join(OUT_DIR, "01_layer_transition.png"))
    build_status_bar().save(os.path.join(OUT_DIR, "02_status_bar.png"))
    build_gear_bar().save(os.path.join(OUT_DIR, "03_gear_bar.png"))
    print("已生成：")
    for name in ("01_layer_transition.png", "02_status_bar.png", "03_gear_bar.png"):
        print("  " + os.path.join(OUT_DIR, name))


if __name__ == "__main__":
    main()
