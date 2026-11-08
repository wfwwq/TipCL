#!/usr/bin/env bash
# tools/build.sh — TipCL Linux 版一键构建（M1-A5 充实）
# 用法:
#   ./tools/build.sh                     # 只构建（默认 Release）
#   ./tools/build.sh Debug --test        # 指定配置 + 测试
#   ./tools/build.sh Release --test --smoke   # 构建 + 测试 + 冒烟(--smoke 出 smoke.png)
#
# 防锁依据 GOAL §6.4: build-server shutdown + -p:UseSharedCompilation=false + -m:1。
set -euo pipefail

CONFIG="${1:-Release}"
shift || true

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BUILD_ARGS=(-p:UseSharedCompilation=false -m:1)

shutdown_build_servers() {
    dotnet build-server shutdown >/dev/null 2>&1 || true
}

echo "===== 构建 ($CONFIG) ====="
shutdown_build_servers
dotnet build TipCL.Linux.sln -c "$CONFIG" "${BUILD_ARGS[@]}"
echo "构建完成: 0 错误 ($CONFIG)"

for arg in "$@"; do
    case "$arg" in
        --test)
            echo ""
            echo "===== 测试 ====="
            shutdown_build_servers
            dotnet test TipCL.Linux.sln -c "$CONFIG" --no-build "${BUILD_ARGS[@]}"
            ;;
        --smoke)
            echo ""
            echo "===== 冒烟 (--smoke) ====="
            rm -f "$ROOT/smoke.png"
            (cd "$ROOT" && dotnet run --project src/TipCL.App -c "$CONFIG" --no-build -- --smoke)
            if [[ ! -s "$ROOT/smoke.png" ]]; then
                echo "错误: 冒烟未产出 smoke.png（或为空文件）" >&2
                exit 1
            fi
            echo "冒烟通过: $ROOT/smoke.png ($(wc -c < "$ROOT/smoke.png") B)"
            ;;
        *)
            echo "未知参数: $arg (支持: --test --smoke)" >&2
            exit 1
            ;;
    esac
done

echo ""
echo "全部完成。"
