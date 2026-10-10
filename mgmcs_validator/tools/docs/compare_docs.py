"""两份文档集比对：字节不同 ≠ 不可重现。

研究者 2026-10-10 定的验收顺序，这个脚本就是那三步：

1. **非语义差异**：JSON key 顺序、空白、路径字符串、时间戳 —— 只要"规范化之后一样"
   就算过（canonical 一致）。
2. **机器数值差异**：逐字段量差多少。秒级时间值用容差（默认 1e-6 秒）判；
   差到能改变音乐判断的（比如 downbeat 从 31.92 挪到 32.41）必须单独报出来。
3. **只有这样才算 fail**：同一输入、同一代码/配置，产生了会改变研究结果的差异。

    python tools/docs/compare_docs.py <before_dir> <after_dir> [--tolerance 1e-6]

只读：不改任何一份文档。退出码 0 = 没有"真问题"；1 = 有。
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def walk(before, after, path, out):
    """把两份结构的差异摊平成一串 (字段路径, 前, 后)。"""
    if isinstance(before, dict) and isinstance(after, dict):
        for key in sorted(set(before) | set(after)):
            walk(before.get(key), after.get(key), path + [str(key)], out)
        return
    if isinstance(before, list) and isinstance(after, list):
        if len(before) != len(after):
            out.append((".".join(path) + ".length", len(before), len(after)))
            return
        for index, (left, right) in enumerate(zip(before, after)):
            walk(left, right, path + [str(index)], out)
        return
    if isinstance(before, bool) or isinstance(after, bool):
        if before is not after:
            out.append((".".join(path), before, after))
        return
    if isinstance(before, (int, float)) and isinstance(after, (int, float)):
        if before != after:
            out.append((".".join(path), before, after))
        return
    if before != after:
        out.append((".".join(path), before, after))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--tolerance", type=float, default=1e-6,
                        help="秒级时间值的容差（默认 1e-6 秒；机器输出的浮点尾数不算差异）")
    args = parser.parse_args(argv)

    before_dir, after_dir = Path(args.before), Path(args.after)
    before_files = {p.name: p for p in before_dir.glob("*.mgmcs.json")}
    after_files = {p.name: p for p in after_dir.glob("*.mgmcs.json")}

    identical, canonical_same, numeric_only = [], [], []
    problems, missing = [], []

    for name in sorted(before_files):
        if name not in after_files:
            missing.append(name)
            continue
        left_bytes = before_files[name].read_bytes()
        right_bytes = after_files[name].read_bytes()
        if left_bytes == right_bytes:
            identical.append(name)
            continue
        left = json.loads(left_bytes.decode("utf-8"))
        right = json.loads(right_bytes.decode("utf-8"))
        if canonical(left) == canonical(right):
            canonical_same.append(name)          # 只有 key 顺序/空白这类非语义差异
            continue
        diffs = []
        walk(left, right, [], diffs)
        numeric = [d for d in diffs if isinstance(d[1], (int, float)) and isinstance(d[2], (int, float))]
        other = [d for d in diffs if d not in numeric]
        worst = max((abs(a - b) for _, a, b in numeric), default=0.0)
        if not other and worst <= args.tolerance:
            numeric_only.append((name, worst, len(numeric)))
        else:
            problems.append((name, worst, other[:3], numeric[:3]))

    print("逐份比 {0} 份（前 {1} / 后 {2}）".format(
        len(before_files), len(before_files), len(after_files)))
    print("  字节一致            : {0}".format(len(identical)))
    print("  仅非语义差异        : {0} {1}".format(len(canonical_same), canonical_same[:3] or ""))
    print("  仅浮点尾数差异(<= {0:g}): {1}".format(args.tolerance, len(numeric_only)))
    for name, worst, count in numeric_only[:5]:
        print("      {0}：{1} 处数值差异，最大 {2:.3g}".format(name, count, worst))
    if missing:
        print("  重建后缺失          : {0} {1}".format(len(missing), missing[:5]))
    if problems:
        print("  **真问题**          : {0} 份".format(len(problems)))
        for name, worst, other, numeric in problems:
            print("      {0}：最大数值差 {1:.4g}".format(name, worst))
            for path, a, b in other:
                print("         非数值差异 {0}: {1!r} → {2!r}".format(path, a, b))
            for path, a, b in numeric:
                print("         数值差异   {0}: {1!r} → {2!r}".format(path, a, b))
    ok = not problems and not missing
    print()
    print("结论：" + ("可重现（字节一致、或只有非语义/浮点尾数差异）" if ok else "存在会改变研究结果的差异，必须追"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
