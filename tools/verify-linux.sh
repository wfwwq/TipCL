#!/usr/bin/env bash
# TipCL Linux 端真机自检（M3-C3 交付脚本）
# 用法：在任意 x86_64 Linux 上执行
#   chmod +x tools/verify-linux.sh && tools/verify-linux.sh [产物目录]
# 默认产物目录：dist/TipCL-linux-x64（相对仓库根），可传绝对路径。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN_DIR="${1:-$ROOT/dist/TipCL-linux-x64}"
# 兼容早期手工发布留下的旧目录名 dist/linux-x64(只作后备;默认值必须与真正出包的目录一致,
# 否则「裸跑一次自检脚本」永远停在「找不到产物」并 exit 1,自检形同不存在)
if [ $# -eq 0 ] && [ ! -d "$BIN_DIR" ] && [ -d "$ROOT/dist/linux-x64" ]; then
  echo "提示: 默认目录 $BIN_DIR 不存在,退回旧目录 $ROOT/dist/linux-x64"
  BIN_DIR="$ROOT/dist/linux-x64"
fi
BIN="$BIN_DIR/TipCL.App"
OUT="${TMPDIR:-/tmp}/tipcl-verify"
PASS=0; FAIL=0

say()  { printf '\n\033[1m== %s ==\033[0m\n' "$1"; }
ok()   { printf '  \033[32m✓\033[0m %s\n' "$1"; PASS=$((PASS+1)); }
bad()  { printf '  \033[31m✗\033[0m %s\n' "$1"; FAIL=$((FAIL+1)); }

say "环境"
uname -sm | grep -q "Linux x86_64" && ok "平台 $(uname -sm)" || bad "平台 $(uname -sm)（产物为 linux-x64）"
[ -f "$BIN" ] && ok "产物存在：$BIN ($(du -h "$BIN" | cut -f1))" || { bad "找不到产物 $BIN"; exit 1; }
head -c4 "$BIN" | od -An -tx1 | grep -q "7f 45 4c 46" && ok "ELF 头正常" || bad "不是 ELF 可执行文件"
[ -z "$(find "$BIN_DIR" -maxdepth 1 -name '*.so*' -print -quit)" ] && ok "真单文件（无散落 .so）" || bad "存在散落原生库，单文件打包不完整"

say "运行权限与启动"
chmod +x "$BIN" && ok "chmod +x"
# 自包含产物不需要系统 .NET；直接跑 --smoke 无头渲染
mkdir -p "$OUT"
if (cd "$OUT" && timeout 120 "$BIN" --smoke >run.log 2>&1); then
  ok "--smoke 退出码 0"
  [ -s "$OUT/smoke.png" ] && ok "smoke.png 已生成（$(du -h "$OUT/smoke.png" | cut -f1)）" || bad "smoke.png 缺失或为空"
else
  bad "--smoke 失败，见 $OUT/run.log"
  tail -20 "$OUT/run.log" | sed 's/^/     /'
fi

say "逐页冒烟矩阵(R30 钉死的 8 帧,权威清单见 docs/PARITY.md §9.15)"
# 判定口径(D-26):退出码 0 + 日志 0 个 [WARN] / 0 个 [ERROR] + 人工看图。
# 不比 PNG 字节:同机同包以外的帧字节天然会漂(见 D-42)。
mkdir -p "$OUT/frames"
run_frame() { # $1=帧名 $2=SMOKE_PAGE(空=默认启动页) $3=SMOKE_DARK(1=深色)
  local name="$1" pg="$2" dark="$3" log="$OUT/frame-$name.log"
  local -a envs=()
  # 先清掉上一帧的 smoke.png:否则「本帧进程返回 0 但根本没写出图」会被上一帧的残影判成通过(假绿)
  rm -f "$OUT/smoke.png"
  [ -n "$pg" ] && envs+=("SMOKE_PAGE=$pg")
  [ "$dark" = "1" ] && envs+=("SMOKE_DARK=1")
  if (cd "$OUT" && env "${envs[@]+"${envs[@]}"}" timeout 120 "$BIN" --smoke >"$log" 2>&1) \
     && [ -s "$OUT/smoke.png" ] \
     && ! grep -qE '\[(WARN|ERROR)\]' "$log"; then
    ok "帧 $name (SMOKE_PAGE=${pg:-<默认>} SMOKE_DARK=${dark})"
  else
    bad "帧 $name (SMOKE_PAGE=${pg:-<默认>} SMOKE_DARK=${dark}) — 见 $log"
    grep -E '\[(WARN|ERROR)\]' "$log" 2>/dev/null | head -5 | sed 's/^/     /'
  fi
  [ -s "$OUT/smoke.png" ] && cp -f "$OUT/smoke.png" "$OUT/frames/$name.png"
}
run_frame launch         ""        0
run_frame library        library   0
run_frame download       download  0
run_frame plugins        plugins   0
run_frame settings       settings  0
run_frame ai             ai        0
run_frame library-dark   library   1
run_frame plugins-dark   plugins   1
printf '  已留存 8 帧到 %s/frames/(逐一人工核对:中文、图标、圆角、深色、空态)\n' "$OUT"

say "运行期依赖（缺字体/图形库时人工核对截图）"
ldconfig -p 2>/dev/null | grep -qE "libfontconfig" && ok "fontconfig 存在（中文字体渲染需要）" || bad "缺 fontconfig：apt install fontconfig fonts-noto-cjk"
[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && ok "图形会话可用（DISPLAY/WAYLAND_DISPLAY 已设置）" || printf '  \033[33m!\033[0m 无图形会话：图形界面需在有桌面/X11 的环境下人工核对\n'
printf '  \033[33m!\033[0m 请人工打开 %s/smoke.png 核对界面是否正常（中文、图标、圆角、深色）\n' "$OUT"

say "结论"
printf '通过 %d 项，失败 %d 项。截图目录：%s\n' "$PASS" "$FAIL" "$OUT"
[ "$FAIL" -eq 0 ] && printf '\033[32mLinux 端自检全部通过。\033[0m\n' || printf '\033[31m有失败项，请按上面提示处理。\033[0m\n'
exit $((FAIL > 0))
