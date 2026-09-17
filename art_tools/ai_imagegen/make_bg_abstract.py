#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按已定方向生成「符号化 / 色块化」背景（远景层 / 中景层）。

为什么程序生成：方向要求"不出现特征明显的元素、尽量符号化色块化"，
而扩散模型很容易冒出珊瑚、海藻、生物这类具象物，且无法靠提示词稳定排除。
色块与符号本质是图形设计工作——用几何画，落点与色值都精确，也不会跑出具象物。

色板取自选定方向基准图 `out/concept_v4/FA_i2i_d70_s11_00001__flat.png`：
    最深 #0F202E  暗 #0F2F2F  冷影 #0F2F3F  主背景 #203F46
    次背景 #214F54  受光 #245F62  高光结构 #3B8284  亮元素 #91D0D4  中性暗面 #20303E

用法：
    python -X utf8 make_bg_abstract.py --out-dir <目录> [--seed 7]
"""
import argparse
import os

import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1080, 1920

C = {
    "deepest": (15, 32, 46),     # #0F202E
    "dark": (15, 47, 47),        # #0F2F2F
    "cold": (15, 47, 63),        # #0F2F3F
    "base": (32, 63, 70),        # #203F46
    "mid": (33, 79, 84),         # #214F54
    "lit": (36, 95, 98),         # #245F62
    "hi": (59, 130, 132),        # #3B8284
    "hl": (145, 208, 212),       # #91D0D4
    "neutral": (32, 48, 62),     # #20303E
    "mid2": (16, 63, 66),        # #103F42
}


def vgrad(size, top, bottom):
    w, h = size
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]
    a = np.array(top, dtype=np.float32)[None, None, :] * (1 - t) + np.array(bottom, dtype=np.float32)[None, None, :] * t
    return np.repeat(a, w, axis=1)


def over(base_rgba, layer_rgba):
    """普通 alpha 合成（base 必须是不透明底）。"""
    a = layer_rgba[..., 3:4] / 255.0
    return base_rgba[..., :3] * (1 - a) + layer_rgba[..., :3] * a


def posterize(arr, levels=18):
    return np.round(np.clip(arr, 0, 255) / 255.0 * (levels - 1)) / (levels - 1) * 255.0


def make_far(rnd):
    """远景：多重地层 + 同心弧组 + 细斜线 + 稀疏点阵 + 细颗粒，全部为符号。"""
    img = vgrad((W, H), C["deepest"], C["dark"])
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    # ① 地层：七条横向宽带，高度与透明度有节奏地变化（符号化的"水层"）
    strata = [(0.08, 0.07, C["cold"], 40), (0.17, 0.05, C["neutral"], 34), (0.26, 0.09, C["cold"], 30),
              (0.37, 0.06, C["base"], 28), (0.49, 0.11, C["mid2"], 30), (0.63, 0.08, C["base"], 26),
              (0.76, 0.12, C["mid"], 24), (0.90, 0.09, C["dark"], 30)]
    for cy, hh, col, alpha in strata:
        y0 = int(H * (cy - hh / 2)); y1 = int(H * (cy + hh / 2))
        d.rectangle([0, y0, W, y1], fill=col + (alpha,))

    # ② 同心弧组（声呐意象）：四道弧，越外越淡
    arcs = [(0.35, 0.30, 205, 335, 46, 5), (0.50, 0.46, 208, 332, 34, 4),
            (0.66, 0.62, 212, 328, 26, 3), (0.82, 0.78, 216, 324, 20, 2)]
    for rw, rh, s, e, alpha, wpx in arcs:
        d.arc([W / 2 - W * rw, H * 0.52 - H * rh, W / 2 + W * rw, H * 0.52 + H * rh],
              start=s, end=e, fill=C["hi"] + (alpha,), width=max(2, int(H * 0.0016 * wpx)))

    # ③ 细斜线：三组长线，压出方向感（不是具象物，是构成线）
    for i, (x0f, y0f, x1f, y1f, alpha) in enumerate([
            (-0.10, 0.30, 1.05, 0.14, 34), (-0.10, 0.58, 1.05, 0.40, 26), (-0.10, 0.86, 1.05, 0.66, 20)]):
        d.line([(W * x0f, H * y0f), (W * x1f, H * y1f)], fill=C["hi"] + (alpha,),
               width=max(2, int(W * 0.0016)))

    # ④ 稀疏点阵：两簇规则小点，交代"远处有结构"
    for (bx, by, cols, rows, step) in [(0.10, 0.14, 4, 3, 0.045), (0.72, 0.30, 3, 4, 0.040)]:
        for gx in range(cols):
            for gy in range(rows):
                px = int(W * (bx + gx * step)); py = int(H * (by + gy * step * 1.5))
                r = max(2, int(W * 0.0028))
                d.ellipse([px - r, py - r, px + r, py + r], fill=C["hl"] + (90,))

    img = over(img, np.asarray(layer, dtype=np.float32))
    # ⑤ 顶/底轻压暗，给画面收边
    img = img * edge_fade(H, 0.05, 0.90)
    # ⑥ 细颗粒：避免纯色块看起来像没做完
    img = img + rnd.normal(0, 2.2, img.shape)
    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    out = Image.fromarray(np.clip(posterize(np.asarray(out, dtype=np.float32), 18), 0, 255).astype(np.uint8))
    return out.filter(ImageFilter.GaussianBlur(0.9))


def make_mid(rnd):
    """中景：斜切色块群 + 折带 + 角标 + 半调网点 + 边缘斜纹 + 点阵，中央走廊保持干净。"""
    img = vgrad((W, H), C["dark"], C["cold"])
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    # ① 斜切色块群：左右各四块，尺寸递进
    for side in (-1, 1):
        for i, (cy, hh, wfrac, col, alpha) in enumerate([
            (0.16, 0.09, 0.22, C["base"], 130),
            (0.30, 0.07, 0.16, C["mid"], 110),
            (0.48, 0.10, 0.26, C["mid2"], 120),
            (0.66, 0.08, 0.18, C["lit"], 100),
            (0.80, 0.12, 0.28, C["base"], 115),
        ]):
            cx = 0.5 + side * (0.32 + (i % 3) * 0.025)
            hw = W * wfrac / 2
            y0, y1 = H * (cy - hh / 2), H * (cy + hh / 2)
            skew = H * 0.03 * (1 if side > 0 else -1)
            d.polygon([(cx * W - hw, y0), (cx * W + hw, y0 + skew),
                       (cx * W + hw, y1 + skew), (cx * W - hw, y1)], fill=col + (alpha,))

    # ② 折带：从两侧边缘伸进来的亮线折角（符号化的结构感）
    for side in (-1, 1):
        x_edge = 0 if side < 0 else W
        for (y, span, alpha, wfrac) in [(0.22, 0.05, 165, 0.0048), (0.57, 0.04, 120, 0.0036)]:
            x_in = side * (0.30 * W) + W / 2
            d.line([(x_edge, H * y), (x_in, H * (y + span))], fill=C["hi"] + (alpha,),
                   width=max(3, int(W * wfrac)))
            d.line([(x_in, H * (y + span)), (x_in, H * (y + span + 0.10))], fill=C["hi"] + (int(alpha * 0.7),),
                   width=max(2, int(W * wfrac * 0.8)))

    # ③ 半调网点：密度渐变的圆点区（上密下疏），纯符号装饰
    for (bx, by, cols, rows, step, col) in [(0.055, 0.34, 5, 7, 0.026, C["hl"]), (0.80, 0.44, 4, 6, 0.024, C["hi"])]:
        for gx in range(cols):
            for gy in range(rows):
                t = gy / max(1, rows - 1)
                r = max(1, int(W * (0.0042 * (1.0 - 0.7 * t))))
                px = int(W * (bx + gx * step)); py = int(H * (by + gy * step * 1.4))
                d.ellipse([px - r, py - r, px + r, py + r], fill=col + (int(140 * (1.0 - 0.45 * t)),))

    # ④ 边缘斜纹：低透明斜线场，只在左右边缘，增加密度但不抢中央
    for side in (-1, 1):
        for k in range(7):
            x0 = W / 2 + side * (0.30 + k * 0.035) * W
            d.line([(x0, H * 0.05), (x0 - side * W * 0.06, H * 0.95)],
                   fill=C["neutral"] + (34,), width=max(3, int(W * 0.004)))

    # ⑤ 四角角标：与界面组件同一套形状语言（细 L 形亮线）
    m = int(W * 0.03)
    for (cx, cy, dx, dy) in [(1, 1, 1, 1), (W - 1, 1, -1, 1), (1, H - 1, 1, -1), (W - 1, H - 1, -1, -1)]:
        d.line([(cx, cy), (cx + dx * m, cy)], fill=C["hl"] + (150,), width=max(2, int(W * 0.003)))
        d.line([(cx, cy), (cx, cy + dy * m)], fill=C["hl"] + (150,), width=max(2, int(W * 0.003)))

    img = over(img, np.asarray(layer, dtype=np.float32))
    img = img * edge_fade(H, 0.04, 0.92)
    img = img + rnd.normal(0, 2.6, img.shape)
    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    out = Image.fromarray(np.clip(posterize(np.asarray(out, dtype=np.float32), 22), 0, 255).astype(np.uint8))
    return out.filter(ImageFilter.GaussianBlur(0.7))


def edge_fade(h, top, bottom):
    """顶/底轻压暗，给画面收边；返回 (h,1,1) 的乘数。"""
    y = np.linspace(0.0, 1.0, h, dtype=np.float32)
    f = np.ones_like(y)
    t = np.clip(y / max(1e-6, top), 0, 1)
    f *= 0.72 + 0.28 * t
    b = np.clip((1.0 - y) / max(1e-6, 1.0 - bottom), 0, 1)
    f *= 0.76 + 0.24 * b
    return f[:, None, None]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", required=True, dest="out_dir")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--gain", type=float, default=1.0, help="整体提亮倍数（暗版 1.0，亮版约 1.5）")
    ap.add_argument("--suffix", default="", help="文件名后缀，便于同时保留暗版与亮版")
    args = ap.parse_args()

    rnd = np.random.default_rng(args.seed)
    os.makedirs(args.out_dir, exist_ok=True)
    far = make_far(rnd)
    mid = make_mid(rnd)
    if args.gain != 1.0:
        for im in (far, mid):
            a = np.asarray(im, dtype=np.float32) * args.gain
            im.paste(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)))
    p_far = os.path.join(args.out_dir, "FA_bg_FAR_abstract%s.png" % args.suffix)
    p_mid = os.path.join(args.out_dir, "FA_bg_MID_abstract%s.png" % args.suffix)
    far.save(p_far); mid.save(p_mid)

    for p in (p_far, p_mid):
        a = np.asarray(Image.open(p).convert("RGB")).astype(np.float32)
        lum = 0.2126 * a[:, :, 0] + 0.7152 * a[:, :, 1] + 0.0722 * a[:, :, 2]
        g = np.asarray(Image.open(p).convert("L"), dtype=np.float32)
        # 色块化程度：局部标准差很小的像素占比（越大越"块"）
        k = 5
        pad = np.pad(g, k // 2, mode="edge")
        win = np.lib.stride_tricks.sliding_window_view(pad, (k, k))
        flat = float((win.std(axis=(2, 3)) < 2.0).mean())
        print("%-28s 亮度 %.1f  色块像素占比 %.1f%%  唯一色 %d" % (
            os.path.basename(p), lum.mean(), flat * 100, len(np.unique(np.asarray(Image.open(p).convert("RGB")).reshape(-1, 3), axis=0))))


if __name__ == "__main__":
    main()
