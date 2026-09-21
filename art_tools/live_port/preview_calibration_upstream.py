#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""校准（判定偏移）界面 —— **按上游文案还原**的对照稿（过稿用，不进游戏）。

为什么重做：作者问"这部分到底有没有参考 OpenSekai"。核查结论（可复现）：

  · 系统层 100% 照抄，有行号：
      `Sekai/LiveSettingData.cs:38`   `[Key("TimingAdjustData")] float`（默认 0）
      `Sekai/Live/LiveConfig.cs:26/28/30`  Default 0 / Min -20 / Max +20（单位 = 帧）
      `Sekai/Core/Live/BaseLiveController.cs:78`  `timingAdjust = TimingAdjustData * 0.016667f`
      `Sekai/Core/Live/BaseLiveController.cs:48`  `Max(timingAdjust + musicTime, 0)`（加法，正 = 音符提前）
  · 界面层**上游没有玩家向实现**：
      全仓库 `rg -l --text "WORD_TIMING_ADJUSTMENT|MSG_ADJUST_WITH_TAP"` 只命中
      `Assets/Resources/wording/master_wording*.txt` 自身，**0 处代码/资产引用**；
      `Assets/Resources/screen/**` 无任何含 "timing" 的界面资产，`screen/prefabs` 也没有校准界面预制体；
      唯一碰这个值的是自制谱编辑器的**工程界面**：
      `Assets/CustomMusicScoreManager/Runtime/UI/ScreenLayerCustomMusicScoreManager.cs`
      里一行数字输入框（"判定偏移 -20.0 - 20.0"）+ `MusicScoreMakerPresenter.cs:2894`。

但上游本地化表里**留着原版那套界面的完整文案**，足以还原它的形态（下面每一条都逐字取自
`Assets/Resources/wording/master_wording.txt` / `master_wording_zh.txt`）：

  WORD_TIMING_ADJUSTMENT       タイミング調整 / 时机调整                       → 标题
  WORD_TIMING_SETTING_SOUND    音に合わせてタップし、タイミングをあわせてください。 → 测量中引导语
  WORD_TIMING_SETTING_DESCRIPTION 判定ラインより上の場合は「＋」に、\n下の場合は「－」に
                               调整为「＋」，下方则「－」                        → ± 方向说明
  MSG_ADJUST_WITH_TAP          タップで調整 / 点击调整                          → 底部大按钮
  MSG_ADJUST_WITH_TAP_BODY     判定タイミングを調整できます。\n高い音が鳴るタイミングに合わせて\n
                               下のボタンをタップしてください。                    → 入口说明（"敲下方按钮"）
  MSG_ADJUST_WITH_TAP_RESULT_BODY 自動調整結果で設定しますか？ / 要使用自动调整结果进行设置吗？
                                                                              → 自动调整后的确认弹窗

还顺带验证了**符号口径与上游自己的说明一致**：
  `BaseLiveController` 正数 = 音符提前；文案说"音符在判定线**上方**（= 你打早了）→ 调「＋」"，
  也就是"打早 → 正数 → 音符提前"，与我们 `CalibrationMeasure` 的 `建议 = −Δ`（Δ>0 = 打晚）
  完全同向。

三个状态（同一套界面）：
  A 入口：标题 + MSG_ADJUST_WITH_TAP_BODY + 底部大按钮「点击调整」+ 当前偏移
  B 测量中：标题 + WORD_TIMING_SETTING_SOUND + 下落区（音符落到判定线）+ 底部大按钮（"下のボタン"）
  C 自动调整结果：MSG_ADJUST_WITH_TAP_RESULT_BODY 确认弹窗（背后是 ± 方向说明与 ± 按钮）

输出：art_tools/review/calibration_screen/calibration_upstream_{entry,measuring,confirm}.png
      + calibration_upstream_compare.png（三态并排）
      + calibration_upstream_evidence.png（文案溯源表）
运行：python -X utf8 art_tools/live_port/preview_calibration_upstream.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1080, 1920
BG = (8, 16, 22)
PANEL = (14, 26, 32)
CARD = (18, 34, 42)
LINE = (207, 239, 234)
TEXT = (150, 180, 186)
TITLE = (226, 246, 242)
ACCENT = (111, 224, 214)
DIM = (74, 104, 110)
BTN = (30, 52, 60)
BTN_KEY = (46, 92, 100)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "review", "calibration_screen"))


def font(size, bold=False):
    candidates = []
    if bold:
        candidates += ["msyhbd.ttc", "YuGothB.ttc", "meiryob.ttc", "msgothic.ttc", "simhei.ttf"]
    candidates += ["msyh.ttc", "YuGothM.ttc", "meiryo.ttc", "msgothic.ttc", "simhei.ttf", "arial.ttf"]
    for name in candidates:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def text_w(draw, text, f):
    """文本实测宽度（PIL 的 textlength 对 CJK 也准）。"""
    return draw.textlength(text, font=f)


def center(draw, text, y, size, color, bold=True):
    """y = 文本垂直中心。用 anchor 定位，避免手算 ascent 导致错位。"""
    f = font(size, bold)
    draw.text((W / 2, y), text, font=f, fill=color, anchor="mm")
    return f


def lines_center(draw, lines, y, size, color, bold=False, gap=1.45):
    f = font(size, bold)
    step = int(size * gap)
    for i, ln in enumerate(lines):
        draw.text((W / 2, y + i * step), ln, font=f, fill=color, anchor="mm")
    return y + (len(lines) - 1) * step


def button(draw, y, text, w=760, h=140, key=True, size=46, note=None):
    x0, x1 = W / 2 - w / 2, W / 2 + w / 2
    draw.rounded_rectangle([x0, y, x1, y + h], radius=h / 2, fill=BTN_KEY if key else BTN)
    f = font(size, True)
    ty = y + h / 2 - (16 if note else 0)
    draw.text((W / 2, ty), text, font=f, fill=TITLE, anchor="mm")
    if note:
        draw.text((W / 2, y + h - 30), note, font=font(22), fill=DIM, anchor="mm")


def title_bar(draw):
    center(draw, "时机调整", 96, 62, TITLE)
    f = font(26)
    draw.text((40, 150), "WORD_TIMING_ADJUSTMENT（タイミング調整 / 时机调整）", font=f, fill=DIM)


def lane(draw, note_progress=None, pulse=False):
    """下落区：竖轨 + 判定线（+ 可选的下落音符）。note_progress: 0=出生 1=到线。"""
    cx = W / 2
    y_top, y_judge = 560, 1240
    draw.rounded_rectangle([cx - 150, y_top, cx + 150, y_judge], radius=24, fill=(11, 22, 28))
    draw.rectangle([cx - 150, y_top, cx - 146, y_judge], fill=(22, 40, 46))
    draw.rectangle([cx + 146, y_top, cx + 150, y_judge], fill=(22, 40, 46))
    draw.rectangle([cx - 260, y_judge - 4, cx + 260, y_judge + 4], fill=LINE)
    center(draw, "判定线", y_judge + 22, 24, DIM, bold=False)
    if pulse:
        r = 210
        draw.ellipse([cx - r, y_judge - r, cx + r, y_judge + r], outline=(46, 92, 100), width=6)
    if note_progress is not None:
        y = y_judge - (y_judge - y_top) * (1.0 - note_progress)
        scale = 0.55 + 0.45 * note_progress
        hw, hh = int(120 * scale), int(26 * scale)
        draw.rounded_rectangle([cx - hw, y - hh, cx + hw, y + hh], radius=hh,
                               fill=ACCENT if note_progress > 0.9 else (86, 178, 172))


def state_entry(img, draw):
    title_bar(draw)
    lines_center(draw, [
        "可以调整判定时机。",
        "请配合高音响起的时机",
        "点击下方按钮。",
    ], 250, 34, TEXT)
    lane(draw)
    button(draw, 1470, "点击调整", key=True, size=52, note="MSG_ADJUST_WITH_TAP")
    center(draw, "当前偏移  +0.0 帧（+0 ms）", 1660, 30, ACCENT)
    center(draw, "MSG_ADJUST_WITH_TAP_BODY（源文案逐字）", 1700, 22, DIM, bold=False)


def state_measuring(img, draw):
    title_bar(draw)
    center(draw, "请配合声音点击，调整时机。", 250, 34, TITLE)
    center(draw, "WORD_TIMING_SETTING_SOUND（源文案逐字）", 296, 22, DIM, bold=False)
    lane(draw, note_progress=0.62, pulse=True)
    center(draw, "第 3 / 8 拍", 1330, 30, TEXT, bold=False)
    button(draw, 1470, "点击调整", key=True, size=52, note="MSG_ADJUST_WITH_TAP（“下のボタン”= 该按钮即敲击目标）")
    center(draw, "已记录 3 次点击", 1660, 30, ACCENT)


def state_confirm(img, draw):
    title_bar(draw)
    lines_center(draw, [
        "若在判定线上方请调整为“＋”，",
        "下方则调整为“－”。",
    ], 240, 34, TEXT)
    center(draw, "WORD_TIMING_SETTING_DESCRIPTION（源文案逐字）", 322, 22, DIM, bold=False)
    lane(draw)
    draw.rounded_rectangle([W / 2 - 520, 1420, W / 2 - 520 + 250, 1420 + 130], radius=65, fill=BTN)
    center(draw, "－", 1468, 46, TITLE)
    draw.rounded_rectangle([W / 2 + 270, 1420, W / 2 + 270 + 250, 1420 + 130], radius=65, fill=BTN)
    center(draw, "＋", 1468, 46, TITLE)
    center(draw, "上方 → ＋          下方 → －", 1580, 26, DIM, bold=False)

    # 确认弹窗（半透明遮罩，背后的 ± 说明仍可见 —— 原版是"结果确认"盖在时机调整页上）
    overlay = Image.new("RGBA", (W, H), (4, 9, 12, 205))
    od = ImageDraw.Draw(overlay)
    cw, ch = 900, 620
    x0, y0 = W / 2 - cw / 2, H / 2 - ch / 2
    od.rounded_rectangle([x0, y0, x0 + cw, y0 + ch], radius=32, fill=CARD + (255,))
    od.text((W / 2, y0 + 110), "要使用自动调整结果进行设置吗？", font=font(40, True), fill=TITLE, anchor="mm")
    od.text((W / 2, y0 + 176), "MSG_ADJUST_WITH_TAP_RESULT_BODY（源文案逐字）",
            font=font(22), fill=DIM, anchor="mm")
    od.text((W / 2, y0 + 300), "自动调整结果   −21 ms（−1.3 帧）", font=font(34, True), fill=ACCENT, anchor="mm")
    dy = y0 + ch - 200
    od.rounded_rectangle([x0 + 70, dy, x0 + 390, dy + 120], radius=60, fill=BTN + (255,))
    od.text((x0 + 230, dy + 60), "取消", font=font(40, True), fill=TITLE, anchor="mm")
    od.rounded_rectangle([x0 + cw - 390, dy, x0 + cw - 70, dy + 120], radius=60, fill=BTN_KEY + (255,))
    od.text((x0 + cw - 230, dy + 60), "确定", font=font(40, True), fill=TITLE, anchor="mm")
    return Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")


EVIDENCE = [
    ("WORD_TIMING_ADJUSTMENT", "タイミング調整", "时机调整", "标题"),
    ("MSG_ADJUST_WITH_TAP_BODY",
     "判定タイミングを調整できます。\\n高い音が鳴るタイミングに合わせて\\n下のボタンをタップしてください。",
     "可以调整判定时机。\\n请配合高音响起的时机\\n点击下方按钮。", "入口说明"),
    ("MSG_ADJUST_WITH_TAP", "タップで調整", "点击调整", "底部大按钮（“下のボタン”= 敲击目标）"),
    ("WORD_TIMING_SETTING_SOUND", "音に合わせてタップし、\nタイミングをあわせてください。",
     "请配合声音点击，\n调整时机。", "测量中引导语"),
    ("WORD_TIMING_SETTING_DESCRIPTION", "判定ラインより上の場合は「＋」に、\n下の場合は「－」に調整してください。",
     "若在判定线上方请调整为“＋”，\n下方则调整为“－”。", "± 方向说明"),
    ("MSG_ADJUST_WITH_TAP_RESULT_BODY", "自動調整結果で設定しますか？",
     "要使用自动调整结果进行设置吗？", "自动调整结果确认弹窗"),
]


def wrap(draw, text, f, width):
    """按实测宽度折行：先按显式换行（真换行或字面量 \\n），再按字符宽度折。"""
    text = text.replace("\\n", "\n")
    out = []
    for para in text.split("\n"):
        cur = ""
        for ch in para:
            if cur and text_w(draw, cur + ch, f) > width:
                out.append(cur)
                cur = ch
            else:
                cur += ch
        out.append(cur)
    return out


def build_evidence_sheet():
    sw = 2000
    cols = [(36, 470), (470, 1080), (1080, 1660), (1660, sw - 36)]
    heads = ("上游 key", "日文原文（master_wording.txt）", "中文原文（master_wording_zh.txt）", "落在我们界面")
    f_title, f_head, f_cell, f_key = font(38, True), font(28, True), font(26), font(26, True)
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))

    # 先量出每行高度（折行后取最高列）
    rows = []
    for row in EVIDENCE:
        cells = [wrap(probe, row[k], f_key if k == 0 else f_cell, cols[k][1] - cols[k][0] - 24)
                 for k in range(4)]
        rows.append(cells)
    line_h = 34
    row_heights = [max(len(c) for c in cells) * line_h + 24 for cells in rows]
    header_h = 210
    sheet = Image.new("RGB", (sw, header_h + 70 + sum(row_heights) + 40), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((36, 30), "校准界面 · 上游文案溯源（逐字取自 master_wording.txt / master_wording_zh.txt）",
            font=f_title, fill=TITLE)
    sd.text((36, 92),
            "上游全仓库仅本地化表出现这些 key，0 处代码/资产引用；screen 资产与 prefabs 内均无 timing 相关界面 "
            "→ 界面层上游无实现，本稿按这些文案还原",
            font=f_cell, fill=TEXT)
    sd.text((36, 132),
            "符号口径核对：上游 + = 音符提前（BaseLiveController.cs:48/78）；文案“上方（打早）→ ＋”与之同向，"
            "与我们的“建议 = −Δ（Δ>0 = 打晚）”一致。",
            font=f_cell, fill=TEXT)
    sd.text((36, 170), "唯一碰 TimingAdjustData 的上游界面是自制谱编辑器的工程输入框："
                       "ScreenLayerCustomMusicScoreManager.cs（“判定偏移 -20.0 - 20.0”）",
            font=f_cell, fill=DIM)

    y = header_h
    for (a, b), h in zip(cols, heads):
        sd.text((a, y + 12), h, font=f_head, fill=ACCENT)
    sd.line([36, y + 52, sw - 36, y + 52], fill=(40, 70, 78), width=2)
    y += 66
    for i, cells in enumerate(rows):
        rh = row_heights[i]
        if i % 2 == 1:
            sd.rectangle([0, y - 8, sw, y + rh - 16], fill=(10, 20, 26))
        for k, cell_lines in enumerate(cells):
            a = cols[k][0]
            for j, ln in enumerate(cell_lines):
                sd.text((a, y + j * line_h), ln, font=f_key if k == 0 else f_cell,
                        fill=ACCENT if k == 0 else TEXT)
        y += rh
    return sheet


def check_no_overflow():
    """出稿前自检：所有居中/按钮文案必须在自己的容器宽度内（我不看得到图，靠这个把关）。"""
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    checks = [
        ("入口说明", "可以调整判定时机。", 34, False, W - 160),
        ("入口副说明", "请配合高音响起的时机", 34, False, W - 160),
        ("测量引导语", "请配合声音点击，调整时机。", 34, True, W - 160),
        ("± 说明", "若在判定线上方请调整为“＋”，", 34, False, W - 160),
        ("大按钮标签", "点击调整", 52, True, 760 - 60),
        ("按钮注：入口", "MSG_ADJUST_WITH_TAP", 22, False, 760 - 20),
        ("按钮注：测量", "MSG_ADJUST_WITH_TAP（“下のボタン”= 该按钮即敲击目标）", 22, False, W - 80),
        ("小注：入口副行", "MSG_ADJUST_WITH_TAP_BODY（源文案逐字）", 22, False, W - 80),
        ("小注：测量副行", "WORD_TIMING_SETTING_SOUND（源文案逐字）", 22, False, W - 80),
        ("小注：± 副行", "WORD_TIMING_SETTING_DESCRIPTION（源文案逐字）", 22, False, W - 80),
        ("小注：标题副行", "WORD_TIMING_ADJUSTMENT（タイミング調整 / 时机调整）", 26, False, W - 80),
        ("弹窗标题", "要使用自动调整结果进行设置吗？", 40, True, 900 - 80),
        ("弹窗副行", "MSG_ADJUST_WITH_TAP_RESULT_BODY（源文案逐字）", 22, False, 900 - 80),
        ("弹窗结果", "自动调整结果   −21 ms（−1.3 帧）", 34, True, 900 - 120),
        ("当前偏移", "当前偏移  +0.0 帧（+0 ms）", 30, True, W - 160),
        ("状态行", "第 3 / 8 拍", 30, False, W - 160),
    ]
    bad = 0
    for label, text, size, bold, limit in checks:
        w = text_w(probe, text, font(size, bold))
        ok = w <= limit
        bad += 0 if ok else 1
        print("  [%s] %-14s %6.1f / %d  %s" % ("OK " if ok else "!! ", label, w, limit, text))
    print("文案宽度自检：%d 项，溢出 %d 项" % (len(checks), bad))
    return bad == 0


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    check_no_overflow()

    states = [("entry", state_entry), ("measuring", state_measuring), ("confirm", state_confirm)]
    images = []
    for name, fn in states:
        img = Image.new("RGB", (W, H), BG)
        result = fn(img, ImageDraw.Draw(img))
        img = result if isinstance(result, Image.Image) else img
        img.save(os.path.join(OUT_DIR, "calibration_upstream_%s.png" % name))
        images.append(img)

    scale = 0.60
    pw, ph = int(W * scale), int(H * scale)
    sheet = Image.new("RGB", (pw * 3 + 100, ph + 210), (5, 10, 14))
    sd = ImageDraw.Draw(sheet)
    sd.text((40, 28), "FallenAngel · 校准界面按上游文案还原（对照稿 · 待过稿）", font=font(42, True), fill=TITLE)
    sd.text((40, 86),
            "上游没有玩家向校准界面（只有本地化表里留下的原版文案）→ 本稿逐字使用上游文案，交互形态按文案描述还原："
            "标题 / 入口说明 / 底部大按钮 / 跟声点击 / ± 方向说明 / 自动调整结果确认",
            font=font(24), fill=TEXT)
    sd.text((40, 124),
            "“不同的地方只能是音符下落界面”——本稿的判定线、下落音符、配色沿用游戏内既有语言，未引入新视觉体系",
            font=font(24), fill=TEXT)
    labels = ["A 入口（MSG_ADJUST_WITH_TAP_BODY）", "B 测量中（WORD_TIMING_SETTING_SOUND）",
              "C 自动调整结果（MSG_ADJUST_WITH_TAP_RESULT_BODY）"]
    for i, img in enumerate(images):
        x = 30 + i * (pw + 20)
        sd.text((x + 6, 170), labels[i], font=font(24, True), fill=ACCENT)
        sheet.paste(img.resize((pw, ph), Image.LANCZOS), (x, 205))
        sd.rectangle([x, 205, x + pw, 205 + ph], outline=(40, 70, 78))
    sheet.save(os.path.join(OUT_DIR, "calibration_upstream_compare.png"))

    build_evidence_sheet().save(os.path.join(OUT_DIR, "calibration_upstream_evidence.png"))
    print("已输出：", OUT_DIR)


if __name__ == "__main__":
    main()
