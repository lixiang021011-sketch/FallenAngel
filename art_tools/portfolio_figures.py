#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""作品集示意图（不依赖截图，程序化绘制，风格与 UI001 一致）。

产出 portfolio/figures/：
  fig_loop.png     双层循环：局内行程 ↔ 局外永久成长
  fig_income.png   收益结算顺序：原始奖励 → 增幅加算 → 保底 → 封顶（利息独立）

用法：python -X utf8 art_tools/portfolio_figures.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

INK = (229, 233, 228, 255)
MUTED = (155, 171, 175, 255)
ACCENT = (141, 198, 208, 255)
PAID = (207, 180, 123, 255)
BG = (16, 26, 34, 255)
CARD = (23, 39, 50, 255)
LINE = (141, 198, 208, 120)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "portfolio", "figures")
FONT_PATH = os.path.join(ROOT, "Assets", "Fonts", "SourceHanSansCN-Regular.otf")


def font(size):
    try:
        return ImageFont.truetype(FONT_PATH, size)
    except Exception:
        return ImageFont.load_default()


def card(d, x, y, w, h, radius=22, fill=CARD, stroke=LINE, width=3):
    d.rounded_rectangle([x, y, x + w, y + h], radius=radius, fill=fill, outline=stroke, width=width)


def text(d, xy, s, size=30, color=INK, anchor="la"):
    d.text(xy, s, font=font(size), fill=color, anchor=anchor)


def arrow(d, x0, y0, x1, y1, color=ACCENT, width=4, head=12):
    d.line([x0, y0, x1, y1], fill=color, width=width)
    import math
    ang = math.atan2(y1 - y0, x1 - x0)
    for side in (-0.5, 0.5):
        d.line([x1, y1, x1 - head * math.cos(ang + side), y1 - head * math.sin(ang + side)], fill=color, width=width)


def fig_loop():
    W, H = 1700, 1000
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    text(d, (60, 44), "FallenAngel ｜ 双层循环", 52)
    text(d, (60, 112), "局内做选择，局外沉淀成长；两边共用同一套配置表驱动", 28, MUTED)

    # 左：局内行程
    card(d, 60, 190, 760, 700)
    text(d, (100, 226), "局内 · 一次行程（5–8 分钟）", 38, ACCENT)

    card(d, 120, 300, 220, 110, fill=(31, 52, 64, 255))
    text(d, (230, 355), "起点", 32, INK, "mm")
    card(d, 400, 300, 260, 110, fill=(31, 52, 64, 255))
    text(d, (530, 355), "战斗房 ×3", 32, INK, "mm")
    card(d, 720, 300, 100, 110, fill=(31, 52, 64, 255))
    text(d, (770, 355), "终点", 30, INK, "mm")
    arrow(d, 340, 355, 400, 355)
    arrow(d, 660, 355, 720, 355)

    # 分叉
    card(d, 300, 470, 250, 120, fill=(28, 46, 58, 255))
    text(d, (425, 530), "免费路线", 30, INK, "mm")
    card(d, 610, 470, 250, 120, fill=(46, 40, 30, 255), stroke=PAID)
    text(d, (735, 530), "付费路线", 30, PAID, "mm")
    text(d, (425, 606), "基础收益", 24, MUTED, "mm")
    text(d, (735, 606), "路费 40 · 更高收益", 24, PAID, "mm")
    d.line([530, 410, 425, 470], fill=LINE, width=3)
    d.line([530, 410, 735, 470], fill=LINE, width=3)

    card(d, 120, 660, 640, 110, fill=(28, 46, 58, 255))
    text(d, (440, 715), "商店：现金买装备（10 件，各有效果）", 30, INK, "mm")
    card(d, 120, 790, 640, 70, fill=(24, 38, 48, 255))
    text(d, (440, 825), "每次分叉都有免费出口 · 失败不退路费", 26, MUTED, "mm")

    # 右：局外成长
    card(d, 880, 190, 760, 700)
    text(d, (920, 226), "局外 · 永久成长（跨局）", 38, ACCENT)
    card(d, 940, 300, 640, 150, fill=(31, 52, 64, 255))
    text(d, (1260, 350), "三曲积分累加", 34, INK, "mm")
    text(d, (1260, 400), "局终一次性入账，未完成曲目不计分", 26, MUTED, "mm")
    card(d, 940, 490, 640, 150, fill=(31, 52, 64, 255))
    text(d, (1260, 540), "天赋树 24 节点 + 3 交汇", 34, INK, "mm")
    text(d, (1260, 590), "满树 3600 分 · 永久生效", 26, MUTED, "mm")
    card(d, 940, 680, 640, 150, fill=(28, 46, 58, 255))
    text(d, (1260, 730), "改变下一局的规则", 34, ACCENT, "mm")
    text(d, (1260, 780), "收益保底 / 利息 / 刷新 / 路费减免 / 可选折扣", 24, MUTED, "mm")

    arrow(d, 820, 420, 880, 420)
    arrow(d, 880, 700, 820, 700, PAID)
    text(d, (850, 380), "积分", 24, MUTED, "mm")
    text(d, (850, 668), "效果", 24, PAID, "mm")
    text(d, (60, 930), "设计取舍：付费路线永远不是唯一通路；未接线的效果在 UI 上明示，不伪装成结算内容。", 26, MUTED)
    img.save(os.path.join(OUT, "fig_loop.png"))


def fig_income():
    W, H = 1700, 860
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    text(d, (60, 44), "FallenAngel ｜ 收益结算顺序", 52)
    text(d, (60, 112), "顺序固定、可预测；同一来源的效果按加算，保底与封顶兜底", 28, MUTED)

    steps = [
        ("① 原始奖励", "关卡基础收益 + 达标奖励（I0/I1 双线）", INK),
        ("② 增幅加算", "演奏增幅 D0 + 无 Miss E05 + 挑战 J0/J1（按来源相加）", INK),
        ("③ 保底补足", "G0/G1：不足基础收入的一定比例时补足", INK),
        ("④ 封顶裁剪", "统一封顶 B × 1.5（原型值，与导出器一致）", INK),
    ]
    y = 210
    for i, (title, desc, _c) in enumerate(steps):
        card(d, 120, y, 900, 120)
        text(d, (160, y + 60), title, 34, ACCENT, "lm")
        text(d, (470, y + 60), desc, 26, MUTED, "lm")
        if i < len(steps) - 1:
            arrow(d, 570, y + 120, 570, y + 150, INK, 3, 10)
        y += 150
    text(d, (120, y + 10), "利息（C1 开曲 + E09 装备）独立成条，不参与增幅 / 保底 / 封顶", 28, PAID)

    card(d, 1100, 210, 520, 330, fill=(46, 40, 30, 255), stroke=PAID)
    text(d, (1360, 265), "利息支线", 34, PAID, "mm")
    text(d, (1360, 330), "按开曲现金计提", 26, INK, "mm")
    text(d, (1360, 380), "单次上限 3% 基础收入", 26, MUTED, "mm")
    arrow(d, 1020, 375, 1100, 375, PAID)

    card(d, 1100, 580, 520, 200, fill=(28, 46, 58, 255))
    text(d, (1360, 635), "校验", 30, INK, "mm")
    text(d, (1360, 690), "95 项确定性校验覆盖\n配置 / 折扣 / 掉落 / 存档事务", 24, MUTED, "mm")
    text(d, (120, 790), "结算顺序与导出器、模拟脚本（balance_sim.py）三处同语义，避免「两套账」。", 26, MUTED)
    img.save(os.path.join(OUT, "fig_income.png"))


def main():
    os.makedirs(OUT, exist_ok=True)
    fig_loop()
    fig_income()
    print("已生成：portfolio/figures/fig_loop.png, portfolio/figures/fig_income.png")


if __name__ == "__main__":
    main()
