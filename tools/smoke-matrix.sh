#!/usr/bin/env bash
# tools/smoke-matrix.sh —— 逐页无头冒烟矩阵(Linux / CI 侧;与 tools/smoke-matrix.ps1 同源)
#
# 目的:把「每一页都能无头打开、都不炸」变成一条可重跑的命令。用户最贵的一类反馈是
# 「某个页面点进去就白屏/报错」—— 默认帧(只拍启动页)完全看不见它。
# 每个页面 id 各起一次独立进程、各给一份隔离 TIPCL_HOME,四条判据:
#   1) 进程退出码 == 0
#   2) smoke.png > MIN_PNG_BYTES(默认 10240;近空白/纯背景的帧会被这条挡住)
#   3) 日志 0 个 [WARN]
#   4) 日志 0 个 [ERROR]
# 任意一页不达标 ⇒ exit 1,并打印该页的 console 尾部与 WARN/ERROR 原文。
#
# 用法:
#   bash tools/smoke-matrix.sh
#   bash tools/smoke-matrix.sh --bin dist/TipCL-linux-x64/TipCL.App
#   bash tools/smoke-matrix.sh --bin src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --pages launch,settings
#   bash tools/smoke-matrix.sh --min-png 10240 --out /tmp/tipcl-matrix --markdown progress/smoke-matrix.md
#
# 页面清单的来源:src/TipCL.App/Shell/MainWindow.axaml.cs 的 GetPage(id) switch。
# 与 Windows 侧的 tools/smoke-matrix.ps1 必须一致(RepoConsistencyTests 会挡漂移)。
set -uo pipefail

MIN_PNG=10240
OUT=""
BIN=""
MD=""
PAGES="launch,download,library,plugins,settings,instance,account,multiplayer,tasks,ai"
TIMEOUT=180

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bin) BIN="${2:-}"; shift 2 ;;
        --out) OUT="${2:-}"; shift 2 ;;
        --pages) PAGES="${2:-}"; shift 2 ;;
        --min-png) MIN_PNG="${2:-10240}"; shift 2 ;;
        --timeout) TIMEOUT="${2:-180}"; shift 2 ;;
        --markdown) MD="${2:-}"; shift 2 ;;
        -h|--help) sed -n '2,28p' "$0"; exit 0 ;;
        *) echo "未知参数: $1(支持 --bin / --out / --pages / --min-png / --timeout / --markdown)" >&2; exit 1 ;;
    esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[ -n "$OUT" ] || OUT="$ROOT/dist/smoke-matrix"

if [ -z "$BIN" ]; then
    for c in "$ROOT/src/TipCL.App/bin/Release/net8.0/TipCL.App.dll" \
             "$ROOT/src/TipCL.App/bin/Debug/net8.0/TipCL.App.dll"; do
        [ -f "$c" ] && BIN="$c" && break
    done
fi
if [ -z "$BIN" ] || [ ! -f "$BIN" ]; then
    echo "找不到被测程序:${BIN:-<未找 TipCL.App.dll>}" >&2
    echo "先构建: dotnet build src/TipCL.App/TipCL.App.csproj -c Release" >&2
    exit 1
fi
BIN="$(cd "$(dirname "$BIN")" && pwd)/$(basename "$BIN")"

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

# dll 用 dotnet 宿主跑;AppImage / 单文件 exe 直接跑
RUN=( "$BIN" --smoke )
case "$BIN" in
    *.dll) RUN=( dotnet "$BIN" --smoke ) ;;
esac

echo "TipCL 冒烟矩阵(逐页:能否无头打开、是否 0 WARN / 0 ERROR)"
echo "  app      : $BIN"
echo "  out      : $OUT"
echo "  pages    : ${PAGES//,/ / }"
echo "  criteria : exit=0, smoke.png > ${MIN_PNG} B, 0 [WARN], 0 [ERROR]"
echo

rows_file="$OUT/matrix.tsv"
: > "$rows_file"
FAILS=0
TOTAL=0

count_marker() { # $1=file $2=literal
    [ -f "$1" ] || { echo 0; return; }
    local n
    n=$(grep -F -c -- "$2" "$1" 2>/dev/null)
    [ -n "$n" ] || n=0
    echo "$n"
}

IFS=',' read -r -a page_list <<< "$PAGES"
for page in "${page_list[@]}"; do
    [ -n "$page" ] || continue
    TOTAL=$((TOTAL+1))
    home="$OUT/home-$page"
    work="$OUT/work-$page"
    rm -rf "$home" "$work"
    mkdir -p "$home" "$work"

    # 首启提示/反馈同意预置,免得每页都弹首启对话框
    printf '%s\n' '{"first_run": false, "feedback_consent": true}' > "$home/config.json"

    console="$work/console.log"
    case "$home" in
        "$OUT"/*) ;;
        *) echo "拒绝运行: TIPCL_HOME '$home' 不在 --out '$OUT' 之内" >&2; exit 1 ;;
    esac

    start_ms=$(date +%s%3N 2>/dev/null || date +%s000)
    (cd "$work" && env TIPCL_HOME="$home" TEMP="$work" TMP="$work" SMOKE_PAGE="$page" \
        timeout "$TIMEOUT" "${RUN[@]}" > "$console" 2>&1)
    code=$?
    end_ms=$(date +%s%3N 2>/dev/null || date +%s000)
    ms=$((end_ms - start_ms))

    png="$work/smoke.png"
    png_bytes=0
    [ -f "$png" ] && png_bytes=$(stat -c%s "$png" 2>/dev/null || echo 0)

    warn=$(count_marker "$console" '[WARN]')
    err=$(count_marker "$console" '[ERROR]')
    if [ -d "$home/logs" ]; then
        for lf in "$home"/logs/*.log; do
            [ -f "$lf" ] || continue
            warn=$((warn + $(count_marker "$lf" '[WARN]')))
            err=$((err + $(count_marker "$lf" '[ERROR]')))
        done
    fi

    verdict=OK
    if [ "$code" -ne 0 ] || [ "$png_bytes" -le "$MIN_PNG" ] || [ "$warn" -ne 0 ] || [ "$err" -ne 0 ]; then
        verdict=FAIL
        FAILS=$((FAILS+1))
    fi
    printf '  %-12s exit=%-3s png=%-8s warn=%s error=%s %sms  %s\n' "$page" "$code" "$png_bytes" "$warn" "$err" "$ms" "$verdict"
    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$page" "$verdict" "$code" "$png_bytes" "$warn" "$err" "$ms" >> "$rows_file"

    if [ "$verdict" = FAIL ]; then
        echo "    --- console(尾部 20 行)---"
        tail -20 "$console" 2>/dev/null | sed 's/^/    /'
        for src in "$console" "$home"/logs/*.log; do
            [ -f "$src" ] || continue
            grep -E '\[(WARN|ERROR)\]' "$src" 2>/dev/null | head -10 | sed 's/^/    /'
        done
    fi
done

echo
echo "== 汇总 =="
awk -F'\t' 'BEGIN{printf "  %-12s %-6s %-5s %-10s %-5s %-6s %s\n","page","verdict","exit","png(B)","WARN","ERROR","ms"} {printf "  %-12s %-6s %-5s %-10s %-5s %-6s %s\n",$1,$2,$3,$4,$5,$6,$7}' "$rows_file"
echo "  rows: $rows_file"

if [ -n "$MD" ]; then
    mkdir -p "$(dirname "$MD")"
    {
        echo "| 页面 | 结论 | exit | smoke.png (B) | [WARN] | [ERROR] | 耗时 (ms) |"
        echo "| --- | --- | --- | --- | --- | --- | --- |"
        awk -F'\t' '{ mark = ($2=="OK") ? "✅" : "❌"; printf "| %s | %s | %s | %s | %s | %s | %s |\n",$1,mark,$3,$4,$5,$6,$7 }' "$rows_file"
    } > "$MD"
    echo "  wrote $MD"
fi

echo
if [ "$FAILS" -ne 0 ]; then
    echo "至少 $FAILS/$TOTAL 页没达标:退出码 0 / png > $MIN_PNG B / 0 WARN / 0 ERROR 没有全部成立。" >&2
    exit 1
fi
echo "全部 $TOTAL 页通过:exit=0、smoke.png > $MIN_PNG B、0 WARN、0 ERROR。"
exit 0