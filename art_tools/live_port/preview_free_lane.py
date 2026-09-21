#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""5 键「拆墙」对比稿：现状(有轨道墙) vs Phigros 式不固定轨道（过稿用，**不进游戏**）。

作者反馈：5 键界面太挤、容易误触到其他轨道，希望"把 5 个轨道之间的墙拆掉，
变成类似 Phigros 那样不固定轨道的界面，但是是伪 5K"。

先说清**判定侧**的事实（决定这个改动是纯视觉还是有判定含义）：
  · 移植后的判定是"跨度"模型：每条轨的判定跨度 = 轨道号 ± 0.5 轨，
    相邻跨度在中点处**恰好相接**（5 键节距 208 → 每条跨度 208 宽）。
    也就是说"点屏幕任意位置都落在某一条轨的判定跨度里"，本来就是"不固定轨道"的语义；
  · 误触来自**视觉**：能看到轨道缝时，玩家会以为缝隙是"死区"，而实际那里仍然会判到最近的那条轨。
    把墙拆掉、让音符自己成为唯一的瞄准依据（Phigros 的做法），误触的观感就消失了。

三个方案（同一段谱面、同一时刻）：
  A 现状：5 条轨道底色 + 轨道缝/描边 + 按键区
  B 全无墙：只留音符与判定线（背景与音符不变）
  C 极淡列：保留几乎看不见的列底与按键区高亮，上半屏完全干净
另外叠加"判定跨度边界"（虚线）：**无论选哪个方案，判定边界都在各轨中点**——
也就是拆墙不会让判定变松，只是不再暗示"缝隙是死区"。

输出（art_tools/review/free_lane/）：free_lane_compare.png（2×2）+ 3 张原尺寸图
运行：python -X utf8 art_tools/live_port/preview_free_lane.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
CANVAS_H = 1920.0
JUDGE_CENTER_Y = 400.0 - CANVAS_H / 2.0          # 判定线：距底 400（中心系 -560）
SPAWN_CENTER_Y = 600.0

LANE_COUNT = 5
PITCH = 208.0                                     # 5 键节距（LaneLayout）
LANE_W = 187.0                                    # 5 键轨宽
LANE_CENTERS = [-416.0, -208.0, 0.0, 208.0, 416.0]
NOTE_W, NOTE_H = 187.0, 28.0                      # tap 宽度 = 轨宽
KEY_AREA_H = 400.0                                # 按键区（判定线往下）

BG = (10, 20, 26)
LANE_FILL = (18, 33, 39)
LANE_EDGE = (46, 76, 84)
ALT_FILL = (22, 39, 45)
KEY_FILL = (28, 48, 55)
JUDGE_COLOR = (207, 239, 234)
TEXT = (150, 180, 186)
TEXT_BRIGHT = (222, 244, 240)
BOUNDARY = (96, 132, 138)
LANE_COLORS = [(207, 239, 234), (176, 226, 222), (146, 208, 204), (120, 186, 186), (98, 160, 162)]


def font(size):
    for n in ("msyh.ttc", "simhei.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def screen(x_center, y_center):
    return W / 2.0 + x_center, H / 2.0 - y_center


def draw_background(draw):
    draw.rectangle([0, 0, W, H], fill=BG)


def draw_lanes_current(draw):
    """现状：每轨一块底色 + 描边（就是"墙"的主要来源）+ 按键区。"""
    y_top = 0
    _, y_judge = screen(0, JUDGE_CENTER_Y)
    for i, cx in enumerate(LANE_CENTERS):
        x0, _ = screen(cx - LANE_W / 2.0, 0)
        x1, _ = screen(cx + LANE_W / 2.0, 0)
        draw.rectangle([x0, y_top, x1, y_judge], fill=LANE_FILL)
        draw.line([(x0, y_top), (x0, y_judge)], fill=LANE_EDGE, width=3)
        draw.line([(x1, y_top), (x1, y_judge)], fill=LANE_EDGE, width=3)
        # 按键区（判定线往下）
        draw.rectangle([x0, y_judge, x1, H], fill=KEY_FILL)
        draw.line([(x0, y_judge), (x0, H)], fill=LANE_EDGE, width=3)
        draw.line([(x1, y_judge), (x1, H)], fill=LANE_EDGE, width=3)


def draw_lanes_ghost(draw):
    """方案 C：极淡列（几乎看不见的列底）+ 按键区高亮。"""
    _, y_judge = screen(0, JUDGE_CENTER_Y)
    for i, cx in enumerate(LANE_CENTERS):
        x0, _ = screen(cx - LANE_W / 2.0, 0)
        x1, _ = screen(cx + LANE_W / 2.0, 0)
        draw.rectangle([x0, 0, x1, y_judge], fill=ALT_FILL if i % 2 == 0 else BG)
        draw.rectangle([x0, y_judge, x1, H], fill=KEY_FILL)


def draw_judge_line(draw):
    _, y = screen(0, JUDGE_CENTER_Y)
    draw.rectangle([0, y - 3, W, y + 3], fill=JUDGE_COLOR)


def draw_boundaries(draw):
    """判定跨度边界（各轨中点）：拆墙不改变它——虚线只是给作者看"判定线其实在这"。"""
    _, y_judge = screen(0, JUDGE_CENTER_Y)
    for k in range(1, LANE_COUNT):
        x, _ = screen(-PITCH * LANE_COUNT / 2.0 + PITCH * k, 0)
        y = y_judge
        while y < H - 20:
            draw.line([(x, y), (x, y + 16)], fill=BOUNDARY, width=2)
            y += 32


def draw_note(draw, lane, y_center, color, hold_tail=None):
    cx = LANE_CENTERS[lane]
    px, py = screen(cx, y_center)
    draw.rounded_rectangle([px - NOTE_W / 2.0, py - NOTE_H / 2.0, px + NOTE_W / 2.0, py + NOTE_H / 2.0],
                           radius=NOTE_H / 2.0, fill=color)
    if hold_tail is not None:
        ty = hold_tail
        draw.rounded_rectangle([px - 60, py, px + 60, ty], radius=6,
                               fill=(color[0] // 3, color[1] // 3, color[2] // 3))


def draw_sample_notes(draw):
    """一屏典型音符：判定线上一条双押、下落中的几条、一条长按。"""
    y_judge = JUDGE_CENTER_Y
    draw_note(draw, 1, y_judge - 10, LANE_COLORS[1])
    draw_note(draw, 3, y_judge - 10, LANE_COLORS[3])
    draw_note(draw, 0, y_judge + 300, LANE_COLORS[0])
    draw_note(draw, 2, y_judge + 520, LANE_COLORS[2])
    draw_note(draw, 4, y_judge + 760, LANE_COLORS[4])
    draw_note(draw, 1, y_judge + 1040, LANE_COLORS[1])
    # 长按（身体 + 尾）
    cy = LANE_CENTERS[2]
    px, py = screen(cy, y_judge - 60)
    draw.rounded_rectangle([px - 60, py - 700, px + 60, py - 6], radius=6, fill=(52, 78, 84))
    draw.rounded_rectangle([px - NOTE_W / 2.0, py - NOTE_H / 2.0, px + NOTE_W / 2.0, py + NOTE_H / 2.0],
                           radius=NOTE_H / 2.0, fill=LANE_COLORS[2])


def draw_caption(draw, title, subtitle, warn=False):
    f_title, f_sub = font(40), font(24)
    draw.text((28, 26), title, font=f_title, fill=TEXT_BRIGHT)
    draw.text((28, 78), subtitle, font=f_sub, fill=(232, 126, 108) if warn else TEXT)


def render_panel(kind):
    img = Image.new("RGB", (W, H), BG)
    draw = ImageDraw.Draw(img)
    draw_background(draw)

    if kind == "current":
        draw_lanes_current(draw)
    elif kind == "ghost":
        draw_lanes_ghost(draw)

    draw_sample_notes(draw)
    draw_judge_line(draw)
    draw_boundaries(draw)

    if kind == "current":
        draw_caption(draw, "A 现状：5 条轨道 + 轨道缝/描边",
                     "缝隙看着像「死区」，实际仍会判到最近那条轨 → 观感上的误触")
    elif kind == "free":
        draw_caption(draw, "B 提案：全无墙（Phigros 式）",
                     "只留音符与判定线；瞄准完全靠音符本身（虚线 = 判定跨度边界，各轨中点）")
    else:
        draw_caption(draw, "C 提案：极淡列 + 按键区保留",
                     "上半屏干净、下半屏保留按键落点提示；同样不画墙")
    return img


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.abspath(os.path.join(here, "..", "review", "free_lane"))
    os.makedirs(out_dir, exist_ok=True)

    panels = [render_panel("current"), render_panel("free"), render_panel("ghost")]
    for name, img in zip(("free_lane_A_current", "free_lane_B_no_wall", "free_lane_C_ghost"), panels):
        img.save(os.path.join(out_dir, name + ".png"))

    scale = 0.62
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 3 + 80, ph + 150), (6, 12, 16))
    draw = ImageDraw.Draw(sheet)
    f_head, f_note = font(40), font(24)
    draw.text((30, 24), "FallenAngel · 5 键「拆墙」对比稿（过稿用，不进游戏）", font=f_head, fill=TEXT_BRIGHT)
    draw.text((30, 76),
              "判定事实：每条轨的判定跨度 = 轨道号 ±0.5 轨（5 键节距 208），相邻跨度在中点相接 → 点任意位置都判到最近那条轨；"
              "拆墙只改视觉，不改判定边界（图中虚线）。",
              font=f_note, fill=TEXT)
    for i, img in enumerate(panels):
        x = 20 + i * (pw + 20)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 130))
        draw.rectangle([x, 130, x + pw, 130 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(out_dir, "free_lane_compare.png"))
    print("已输出：", out_dir)


if __name__ == "__main__":
    main()
