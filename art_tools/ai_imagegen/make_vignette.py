#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成演奏背景的「近景暗角层」（Multiply 遮罩）。

为什么用程序而不是 AI：这层的职责是"只压暗四周、不动中央走廊"，
本质是一张遮罩，尺寸和落点必须精确。AI 生成会整张压暗（实测过：AI 版中央 142 / 两侧 146，
几乎无差别），拿去做 Multiply 等于全局降亮。

输出白底 + 四周按径向衰减到色板深处的青黑；中央 60%×60% 保持纯白。
"""
import argparse
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))

W, H = 1080, 1920
DEEP = np.array([15, 32, 46], dtype=np.float32)   # #0F202E 最深处


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--strength", type=float, default=0.55, help="四角最暗处的压暗强度")
    ap.add_argument("--white-radius", type=float, default=0.30,
                    dest="white_radius", help="保持纯白的归一化半径；0.30 约等于中央六成")
    args = ap.parse_args()

    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    nx = (xs - W / 2.0) / (W / 2.0)          # -1..1
    ny = (ys - H / 2.0) / (H / 2.0)
    r = np.sqrt((nx * 0.85) ** 2 + (ny * 1.0) ** 2)   # 横向稍宽容：轨道要横跨整屏

    t = smoothstep((r - args.white_radius) / (1.0 - args.white_radius)) * args.strength
    t = t[..., None]
    img = 255.0 * (1.0 - t) + DEEP[None, None, :] * t

    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    out.save(args.out)

    a = np.asarray(out).astype(np.float32)
    lum = 0.2126 * a[:, :, 0] + 0.7152 * a[:, :, 1] + 0.0722 * a[:, :, 2]
    c = lum[int(0.10 * H):int(0.75 * H), int(0.36 * W):int(0.64 * W)].mean()
    e = lum[int(0.10 * H):int(0.75 * H), np.r_[0:int(0.06 * W), int(0.94 * W):W]].mean()
    print("WROTE", args.out, out.size, "中央 %.0f / 两侧 %.0f" % (c, e))


if __name__ == "__main__":
    main()
