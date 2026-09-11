#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用本机 ComfyUI（D:\\AI\\ComfyUI_windows_portable）跑 FallenAngel 背景图。

用法（先启动 ComfyUI，见 README.md）：
    python -X utf8 art_tools/ai_imagegen/comfy_run.py --prefix FA_probe_V1 --prompt "..."

默认工作流：workflow_fa_bg_sd15.json（SD1.5 两段式：512 级草图 -> 潜空间放大 -> 二次采样）
"""
import argparse
import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.request

SERVER = "127.0.0.1:8188"
COMFY_OUTPUT = r"D:\AI\ComfyUI_windows_portable\ComfyUI\output"
HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_WORKFLOW = os.path.join(HERE, "workflow_fa_bg_sd15.json")


def set_in(wf, node, key, value):
    """只在节点和输入都存在时赋值，方便同一脚本驱动不同结构的 workflow。"""
    if node in wf and key in wf[node].get("inputs", {}):
        wf[node]["inputs"][key] = value
        return True
    return False


def http_json(url, data=None, method="GET", timeout=60):
    req = urllib.request.Request(url, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
        data = json.dumps(data).encode()
    with urllib.request.urlopen(req, data=data, timeout=timeout) as r:
        return json.loads(r.read().decode())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--workflow", default=DEFAULT_WORKFLOW)
    ap.add_argument("--prompt", required=True)
    ap.add_argument("--negative", default=None)
    ap.add_argument("--ckpt", default=None, help="覆盖节点 4 的 checkpoint 名，例如 DreamShaper_8_pruned.safetensors")
    ap.add_argument("--prefix", default="FA_bg")
    ap.add_argument("--width", type=int, default=540, help="草稿宽度（8 的倍数）")
    ap.add_argument("--height", type=int, default=960, help="草稿高度（8 的倍数）")
    ap.add_argument("--out-width", type=int, default=1080, dest="out_width")
    ap.add_argument("--out-height", type=int, default=1920, dest="out_height")
    ap.add_argument("--seed", type=int, default=-1)
    ap.add_argument("--steps", type=int, default=28)
    ap.add_argument("--cfg", type=float, default=7.0)
    ap.add_argument("--hires-denoise", type=float, default=0.45, dest="hires_denoise")
    ap.add_argument("--cn-type", default=None, dest="cn_type",
                    help="SDXL union 的控制类型，如 canny/lineart/anime_lineart/mlsd、hed/pidi/scribble/ted、depth")
    ap.add_argument("--cn-strength", type=float, default=None, dest="cn_strength")
    ap.add_argument("--cn-end", type=float, default=None, dest="cn_end", help="ControlNet 生效到第几个采样步")
    ap.add_argument("--load-image", default=None, dest="load_image", help="覆盖 LoadImage 节点用的控制图文件名")
    ap.add_argument("--denoise", type=float, default=None, help="img2img 重绘幅度（0.3–0.7 保留原结构）")
    ap.add_argument("--copy-to", default=None, help="出图后复制到这个目录")
    ap.add_argument("--timeout", type=int, default=900)
    args = ap.parse_args()

    wf = json.load(open(args.workflow, encoding="utf-8"))
    seed = args.seed if args.seed >= 0 else 12345

    if args.ckpt:
        set_in(wf, "4", "ckpt_name", args.ckpt)
    if args.cn_type:
        set_in(wf, "33", "type", args.cn_type)
    if args.load_image:
        set_in(wf, "30", "image", args.load_image)
    if args.cn_strength is not None:
        set_in(wf, "34", "strength", args.cn_strength)
    if args.cn_end is not None:
        set_in(wf, "34", "end_percent", args.cn_end)
    if args.denoise is not None:
        set_in(wf, "3", "denoise", args.denoise)
    set_in(wf, "5", "width", args.width)
    set_in(wf, "5", "height", args.height)
    set_in(wf, "6", "text", args.prompt)
    if args.negative:
        set_in(wf, "7", "text", args.negative)
    set_in(wf, "3", "seed", seed)
    set_in(wf, "3", "steps", args.steps)
    set_in(wf, "3", "cfg", args.cfg)
    set_in(wf, "10", "width", args.out_width)    # SD1.5 两段式：潜空间放大
    set_in(wf, "10", "height", args.out_height)
    set_in(wf, "11", "seed", seed)
    set_in(wf, "11", "denoise", args.hires_denoise)
    set_in(wf, "15", "width", args.out_width)    # SDXL：超分后缩到目标尺寸
    set_in(wf, "15", "height", args.out_height)
    set_in(wf, "9", "filename_prefix", args.prefix)

    try:
        res = http_json(f"http://{SERVER}/prompt", data={"prompt": wf}, method="POST")
    except urllib.error.URLError as e:
        print(f"[err] 连不上 ComfyUI（{SERVER}）：{e}. 先启动 D:\\AI\\启动 ComfyUI (GPU).bat")
        return 1
    if res.get("node_errors"):
        print("[err] 工作流校验失败：", json.dumps(res["node_errors"], ensure_ascii=False))
        return 1
    pid = res["prompt_id"]
    print(f"[gen] seed={seed} queued={pid} {args.width}x{args.height} -> {args.out_width}x{args.out_height}")

    t0 = time.time()
    while time.time() - t0 < args.timeout:
        h = http_json(f"http://{SERVER}/history/{pid}")
        if pid in h:
            st = h[pid].get("status", {})
            ok = st.get("status_str") == "success" and st.get("completed")
            print(f"[gen] status={st.get('status_str')} elapsed={time.time()-t0:.1f}s")
            for out in h[pid].get("outputs", {}).values():
                for img in out.get("images", []):
                    src = os.path.join(COMFY_OUTPUT, img.get("subfolder") or "", img["filename"])
                    print(f"[gen] OUTPUT: {src}")
                    if args.copy_to:
                        os.makedirs(args.copy_to, exist_ok=True)
                        dst = os.path.join(args.copy_to, img["filename"])
                        shutil.copy2(src, dst)
                        print(f"[gen] COPIED: {dst}")
            return 0 if ok else 2
        time.sleep(2)
    print("[err] 超时")
    return 3


if __name__ == "__main__":
    sys.exit(main())
