#!/usr/bin/env bash
# FallenAngel 编译自检脚本（AI 协作验证用）
# 用法：在工程根目录执行 ./compile_check.sh
# 注意：运行前请先关闭 Unity 编辑器（batchmode 无法打开已被编辑器占用的工程）

set -u

UNITY="/d/unity/2022.3.62f3c1/Editor/Unity.exe"
PROJECT="C:/Users/LorXer/Documents/trae_projects/FallenAngel"
LOG="$(cd "$(dirname "$0")" && pwd)/compile.log"

if [ ! -f "$UNITY" ]; then
    echo "[错误] 未找到 Unity.exe：$UNITY"
    exit 2
fi

# 编辑器占用检查：Unity 编辑器打开着工程时，batchmode 会因文件锁而失败
if tasklist //FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
    echo "[中止] 检测到 Unity 编辑器正在运行，请先关闭再执行本脚本"
    exit 3
fi

echo "开始 batchmode 编译检查（首次可能较慢）..."
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -logFile "$LOG"

# batchmode 即使编译失败退出码也可能为 0，因此以日志内容为准
ERRORS=$(grep -E "error CS[0-9]+|Scripts have compiler errors|Failed to compile" "$LOG" | head -50)
if [ -n "$ERRORS" ]; then
    echo "----------------------------------------"
    echo "编译失败，错误如下（完整日志见 compile.log）："
    echo "$ERRORS"
    exit 1
else
    echo "编译通过，无错误。"
    exit 0
fi
