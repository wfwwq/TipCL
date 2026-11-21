#!/bin/bash
# AI3-linux-real:在真 Linux(含 WSL2)上复跑 构建 / 全量测试 / --ai-selftest / 硬件与 xdg-open 探针。
# 用法: tools/linux-real/verify.sh [输出目录]      (在仓库根目录下运行;建议仓库放在 Linux 自己的文件系统里)
# 约束: 不起任何界面(清掉 DISPLAY / WAYLAND_DISPLAY),不启动游戏;PATH 最前面放一个只记录参数的假 xdg-open,
#       WSL 的 /mnt/* Windows 互操作目录从 PATH 去掉(避免任何 Windows 程序被拉起)。需要 PATH 上有 dotnet 8 SDK。
set +e
REPO=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:-$(mktemp -d /tmp/tipcl-linux-real.XXXXXX)}
mkdir -p "$OUT"
echo "logs -> $OUT"
unset DISPLAY WAYLAND_DISPLAY
PATH=$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '^/mnt/' | paste -sd: -)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
FAKE=$(mktemp -d); CALLS=$(mktemp -d)
cat > "$FAKE/xdg-open" <<'EOF'
#!/bin/bash
# fake xdg-open: never opens anything, only records argv (argc \0 argv... \0)
d=${XDG_OPEN_LOG_DIR:?}
mkdir -p "$d"
printf '%s\0' "$#" "$@" > "$d/$(date +%s%N)-$$"
exit 0
EOF
chmod +x "$FAKE/xdg-open"; cp "$FAKE/xdg-open" "$FAKE/open"
export PATH="$FAKE:$PATH" XDG_OPEN_LOG_DIR="$CALLS/tests"
cd "$REPO"

echo "== build"
dotnet build TipCL.Linux.sln -c Debug -p:UseSharedCompilation=false -m:1 --nologo --no-incremental > "$OUT/build.log" 2>&1
echo "build exit=$?"; grep -oE '[^ ]+\([0-9]+,[0-9]+\): warning [A-Za-z]+[0-9]+' "$OUT/build.log" | sort -u

echo "== test"
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --no-build --nologo > "$OUT/test.log" 2>&1
echo "test exit=$?"; grep -E 'TipCL.Tests.dll \(net8|\[FAIL\]' "$OUT/test.log"
echo "xdg-open calls during tests (must be 0): $(ls "$CALLS/tests" 2>/dev/null | wc -l)"

echo "== --ai-selftest"
H=$(mktemp -d)
TIPCL_HOME=$H dotnet src/TipCL.App/bin/Debug/net8.0/TipCL.App.dll --ai-selftest > "$OUT/selftest.log" 2>&1
echo "selftest exit=$?"; grep -a -- '--ai-selftest' "$OUT/selftest.log" | tail -1

echo "== probe (SystemInfo vs /proc, xdg-open argv)"
dotnet build tools/linux-real/probe/LinuxProbe.csproj -c Debug --nologo -p:UseSharedCompilation=false -m:1 > "$OUT/probe-build.log" 2>&1
PROBE="dotnet tools/linux-real/probe/bin/Debug/net8.0/LinuxProbe.dll"
SMI=$(command -v nvidia-smi || { [ -x /usr/lib/wsl/lib/nvidia-smi ] && echo /usr/lib/wsl/lib/nvidia-smi; })
if [ -n "$SMI" ]; then
  export PROBE_EXPECT_GPU0=$("$SMI" --query-gpu=name --format=csv,noheader 2>/dev/null | head -1)
fi
TIPCL_HOME=$H XDG_OPEN_LOG_DIR="$CALLS/probe" PROBE_EXPECT_AUTH_OPEN=1 $PROBE all > "$OUT/probe.log" 2>&1
echo "probe exit=$?"; grep -E '^\[FAIL\]|^PROBE' "$OUT/probe.log"
rm -rf "$FAKE" "$CALLS" "$H"
