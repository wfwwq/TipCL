#!/usr/bin/env bash
# tools/build.sh — TipCL Linux 版一键构建（M1-A1 骨架，A5 充实）
# 用法: ./tools/build.sh [Debug|Release] [--test] [--smoke]
set -euo pipefail

CONFIG="${1:-Release}"
shift || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# 构建防锁（GOAL §6.4）
dotnet build-server shutdown

dotnet build TipCL.Linux.sln -c "$CONFIG"

for arg in "$@"; do
    case "$arg" in
        --test) dotnet test TipCL.Linux.sln -c "$CONFIG" --no-build ;;
        --smoke) dotnet run --project src/TipCL.App -c "$CONFIG" --no-build -- --smoke ;;
        *) echo "未知参数: $arg"; exit 1 ;;
    esac
done

echo "构建完成 ($CONFIG)"
