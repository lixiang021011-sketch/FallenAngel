#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 AI 出图压成平面图形质感（去厚涂）。

做法：中值去噪 → 色彩阶梯化（posterize）→ 轻微提锐 → 可选描边强调。
保留结构与大色块，抹掉笔触噪声，接近"平面设计稿"而不是"厚涂原画"。

用法：
    python -X utf8 flatten_graphic.py --in <目录或文件> --out-dir <目录> --levels 18 --median 5
"""
import argparse
import glob
import os

import numpy as np
from PIL import Image, ImageFilter


def texture_energy(im):
    g = np.asarray(im.convert("L"), dtype=np.float32)
    b = np.asarray(im.convert("L").filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32)
    b24 = np.asarray(im.convert("L").filter(ImageFilter.GaussianBlur(9)), dtype=np.float32)
    tex = float(np.abs(g - b).mean())
    struct = float(np.abs(g - b24).mean()) + 1e-6
    return tex, tex / struct


def flatten(im, levels=18, median=5, sharpen=1.15, sat=1.05):
    if median:
        im = im.filter(ImageFilter.MedianFilter(median))
    im = im.filter(ImageFilter.GaussianBlur(0.6))
    a = np.asarray(im.convert("RGB")).astype(np.float32) / 255.0
    if sat != 1.0:
        gray = a.mean(axis=2, keepdims=True)
        a = np.clip(gray + (a - gray) * sat, 0, 1)
    a = np.floor(a * levels + 0.5) / max(1, levels - 0)   # 色彩阶梯化
    out = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    if sharpen != 1.0:
        out = out.filter(ImageFilter.UnsharpMask(radius=2, percent=int((sharpen - 1) * 100), threshold=2))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True, help="输入文件或目录")
    ap.add_argument("--out-dir", dest="out_dir", required=True)
    ap.add_argument("--levels", type=int, default=18)
    ap.add_argument("--median", type=int, default=5)
    ap.add_argument("--sharpen", type=float, default=1.15)
    ap.add_argument("--suffix", default="_flat")
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.src, "*.png"))) if os.path.isdir(args.src) else [args.src]
    os.makedirs(args.out_dir, exist_ok=True)
    print("%-34s %8s %9s   %8s %9s" % ("文件", "原纹理", "原纹理/结构", "新纹理", "新纹理/结构"))
    for p in files:
        im = Image.open(p).convert("RGB")
        t0, r0 = texture_energy(im)
        out = flatten(im, args.levels, args.median, args.sharpen)
        t1, r1 = texture_energy(out)
        name = os.path.splitext(os.path.basename(p))[0] + args.suffix + ".png"
        out.save(os.path.join(args.out_dir, name))
        print("%-34s %8.2f %9.2f   %8.2f %9.2f" % (name, t0, r0, t1, r1))
    print("OUT:", args.out_dir)


if __name__ == "__main__":
    main()
