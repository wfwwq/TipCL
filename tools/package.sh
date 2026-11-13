#!/usr/bin/env bash
# tools/package.sh — TipCL Linux 版打包（M3-C2，在 Linux 上跑）
#
# 用法:
#   ./tools/package.sh                       # 发布 + dist/TipCL-linux-x64 单文件 + AppImage 结构组装
#   ./tools/package.sh --no-appimage         # 只出单文件产物，不组装 AppImage
#   ./tools/package.sh --appimage-only       # 跳过 dotnet publish，只用已有的
#                                            # dist/TipCL-linux-x64/TipCL.App 组装 AppImage
#                                            # （发布在 Windows/CI 做、组装在 Linux 上做时用这条，
#                                            #  R21 的做法就是这个，见 progress/M4-H-R21-AppImage组装与实机验证.md）
#   ./tools/package.sh --deb                 # 额外打一个 .deb（纯 dpkg-deb，不引入新依赖，见 tools/deb.sh）
#
# 前置条件（AppImage 出包与运行，R21 真机踩出来的两条）:
#   1. `file` 包必须存在（sudo apt-get install -y file）——appimagetool 靠它做 MIME 识别，
#      缺了会在最后一步以 `ERROR: file command is missing but required` 失败
#      （linuxdeploy 自己会先跑完 desktop/icon 部署，只有产出那一步挂）。
#   2. 这台机器要能跑 AppImage：有 /dev/fuse 就直接跑；没有就给 linuxdeploy 与产物加
#      APPIMAGE_EXTRACT_AND_RUN=1（解包到临时目录再执行）。脚本里按「先直接跑、
#      失败再 extract-and-run」处理。
#
# 产物:
#   dist/TipCL-linux-x64/TipCL.App           self-contained 单文件（chmod +x 直接跑，无需 .NET）
#   dist/TipCL-linux-x64.AppImage            AppImage（需本机有 linuxdeploy，见下方约定）
#
# 发布参数（与 tools/package.ps1 同源；实测记录见 progress/M3-C2.md）:
#   --self-contained true                      不依赖目标机 .NET 运行时
#   -p:PublishSingleFile=true                  单文件宿主
#   -p:IncludeNativeLibrariesForSelfExtract=true  Skia/HarfBuzz 原生库并入单文件
#   -p:EnableCompressionInSingleFile=true      压缩（约 72MB -> 43MB）
#   不开 PublishTrimmed：Core 的 System.Text.Json 反射序列化路径（PageLayoutDoc/
#   PageLayoutStore/PhoneLink）带 IL2026 trim 警告，Avalonia 亦有 IL2104；trim 后运行时
#   行为无法在本机验证，M3 决策默认不 trim（取舍见 progress/M3-C2.md）。
#
# ---------------------------------------------------------------------------
# linuxdeploy 约定（本脚本只组装 AppDir，不强制依赖 linuxdeploy；本机没装时
# 打印手动命令，装好后照抄即可出 .AppImage）:
#   1. AppDir 布局（linuxdeploy 标准）:
#        TipCL.AppDir/
#          AppRun                          # 入口脚本（本脚本生成，exec usr/bin/TipCL）
#          tipcl.desktop                   # 桌面项（tools/AppImage/tipcl.desktop）
#          tipcl.png                       # 256x256 图标（tools/AppImage/tipcl.png）
#          usr/bin/TipCL                   # 单文件可执行
#          usr/share/applications/tipcl.desktop
#          usr/share/icons/hicolor/256x256/apps/tipcl.png
#   2. 出包命令（x86_64）:
#        linuxdeploy-x86_64.AppImage --appdir dist/TipCL.AppDir \
#            --desktop-file=dist/TipCL.AppDir/tipcl.desktop --icon-file=dist/TipCL.AppDir/tipcl.png \
#            --output appimage
#      linuxdeploy 下载: https://github.com/linuxdeploy/linuxdeploy/releases
#      （如用持续集成，可设 LINUXDEPLOY 官方 GitHub Actions：mikepenz/release-changelog-builder 无关，
#        推荐直接 curl 下载后 chmod +x 使用； linuxdeploy 会自动补齐依赖并调用
#        linuxdeploy-plugin-appimage 产出 dist/TipCL-linux-x64.AppImage。）
#   3. 也可用 appimagetool 代替: appimagetool dist/TipCL.AppDir dist/TipCL-linux-x64.AppImage
# ---------------------------------------------------------------------------
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

NO_APPIMAGE=0
APPIMAGE_ONLY=0
MAKE_DEB=0
for arg in "$@"; do
    case "$arg" in
        --no-appimage) NO_APPIMAGE=1 ;;
        --appimage-only) APPIMAGE_ONLY=1 ;;
        --deb) MAKE_DEB=1 ;;
        *) echo "未知参数: $arg (支持: --no-appimage / --appimage-only / --deb)" >&2; exit 1 ;;
    esac
done

PUBLISH_ARGS=(--self-contained true
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    -p:EnableCompressionInSingleFile=true
    -p:UseSharedCompilation=false -m:1)

if [[ $APPIMAGE_ONLY -eq 1 ]]; then
    echo "===== 跳过 dotnet publish（--appimage-only）====="
    if [[ ! -f dist/TipCL-linux-x64/TipCL.App ]]; then
        echo "--appimage-only: 缺少 dist/TipCL-linux-x64/TipCL.App（先跑一次完整打包，或把别处发布的单文件放进来）" >&2
        exit 1
    fi
else
    echo "===== dotnet publish (linux-x64) ====="
    dotnet build-server shutdown >/dev/null 2>&1 || true
    dotnet publish src/TipCL.App -c Release -r linux-x64 "${PUBLISH_ARGS[@]}" -o dist/TipCL-linux-x64
fi
# 只留单文件本体；pdb 调试符号不随包分发
rm -f dist/TipCL-linux-x64/*.pdb
chmod +x dist/TipCL-linux-x64/TipCL.App
SIZE=$(du -h dist/TipCL-linux-x64/TipCL.App | cut -f1)
echo "产物: dist/TipCL-linux-x64/TipCL.App ($SIZE)"

if [[ $NO_APPIMAGE -eq 1 ]]; then
    echo ""
    echo "完成（--no-appimage，跳过 AppDir 组装）。"
    exit 0
fi

echo ""
echo "===== AppImage 结构组装 ====="
APPDIR=dist/TipCL.AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp dist/TipCL-linux-x64/TipCL.App "$APPDIR/usr/bin/TipCL"
chmod +x "$APPDIR/usr/bin/TipCL"
cp tools/AppImage/tipcl.desktop "$APPDIR/tipcl.desktop"
cp tools/AppImage/tipcl.desktop "$APPDIR/usr/share/applications/tipcl.desktop"
cp tools/AppImage/tipcl.png "$APPDIR/tipcl.png"
cp tools/AppImage/tipcl.png "$APPDIR/usr/share/icons/hicolor/256x256/apps/tipcl.png"

# AppRun 入口：转发到单文件宿主。self-contained 无需任何运行时环境变量。
cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(cd "$(dirname "$(readlink -f "${0}")")" && pwd)"
exec "$HERE/usr/bin/TipCL" "$@"
EOF
chmod +x "$APPDIR/AppRun"

echo "AppDir 就绪: $APPDIR"
echo ""
echo "===== 冒烟（可选，需图形/无头环境） ====="
echo "  SMOKE_PAGE=launch $APPDIR/AppRun --smoke   # 生成 smoke.png 后退出（SMOKE_PAGE 可换 library/download/plugins/settings 等）"

LD_BIN=""
if [[ -n "${LINUXDEPLOY:-}" && -x "${LINUXDEPLOY:-}" ]]; then
    LD_BIN="${LINUXDEPLOY}"
elif command -v linuxdeploy-x86_64.AppImage >/dev/null 2>&1; then
    LD_BIN="$(command -v linuxdeploy-x86_64.AppImage)"
elif command -v linuxdeploy >/dev/null 2>&1; then
    LD_BIN="$(command -v linuxdeploy)"
fi
# linuxdeploy 自己是 AppImage:相对路径先转绝对，否则下面 cd dist 之后找不到它
case "$LD_BIN" in
    */*) LD_BIN="$(readlink -f "$LD_BIN")" ;;
esac

if [[ -n "$LD_BIN" ]]; then
    # 前置条件 1:file 包（appimagetool 靠它做 MIME 识别；缺了只有最后一步挂）
    if ! command -v file >/dev/null 2>&1; then
        echo "警告: 未检测到 file 命令，appimagetool 会在最后一步失败 —— 先 sudo apt-get install -y file" >&2
    fi
    # 前置条件 2:先直接跑；失败（无 /dev/fuse 等）再 extract-and-run 重试
    # 注意 --desktop-file/--icon-file 要给 AppDir 里的路径:这里已经 cd 到 dist,
    # 只写 tipcl.desktop / tipcl.png 会以 ERROR: No such file or directory: tipcl.png 失败
    # (R21b 第一次把这条分支真跑起来时才暴露:那两个文件在 AppDir 下,不在 dist 下)。
    if ! (cd dist && "$LD_BIN" --appdir TipCL.AppDir \
            --desktop-file=TipCL.AppDir/tipcl.desktop --icon-file=TipCL.AppDir/tipcl.png \
            --output appimage); then
        echo "linuxdeploy 直接执行失败 —— 改用 APPIMAGE_EXTRACT_AND_RUN=1 重试（无 /dev/fuse 环境的后备）"
        (cd dist && APPIMAGE_EXTRACT_AND_RUN=1 "$LD_BIN" --appdir TipCL.AppDir \
            --desktop-file=TipCL.AppDir/tipcl.desktop --icon-file=TipCL.AppDir/tipcl.png \
            --output appimage)
    fi
    # linuxdeploy 产物名取自 desktop 文件 Name 字段，统一改名
    mv -f dist/TipCL-*.AppImage dist/TipCL-linux-x64.AppImage 2>/dev/null || true
    echo "AppImage: dist/TipCL-linux-x64.AppImage ($(du -h dist/TipCL-linux-x64.AppImage | cut -f1))"
else
    echo "未检测到 linuxdeploy —— AppDir 已组装完成，出包命令："
    echo "  curl -LO https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage"
    echo "  chmod +x linuxdeploy-x86_64.AppImage"
    echo "  ./linuxdeploy-x86_64.AppImage --appdir dist/TipCL.AppDir --desktop-file=dist/TipCL.AppDir/tipcl.desktop --icon-file=dist/TipCL.AppDir/tipcl.png --output appimage"
    echo "（或用 appimagetool: appimagetool dist/TipCL.AppDir dist/TipCL-linux-x64.AppImage）"
fi

echo ""
echo "打包完成。运行: chmod +x dist/TipCL-linux-x64/TipCL.App && ./dist/TipCL-linux-x64/TipCL.App"
echo "AppImage 运行: chmod +x dist/TipCL-linux-x64.AppImage && ./dist/TipCL-linux-x64.AppImage"
echo "  （没有 /dev/fuse 的环境: 前缀 APPIMAGE_EXTRACT_AND_RUN=1 即可）"

if [[ $MAKE_DEB -eq 1 ]]; then
    echo ""
    echo "===== .deb ====="
    if command -v dpkg-deb >/dev/null 2>&1; then
        bash "$ROOT/tools/deb.sh" --bin "$ROOT/dist/TipCL-linux-x64/TipCL.App" --out "$ROOT/dist"
    else
        echo "跳过: 本机没有 dpkg-deb(apt-get install -y dpkg-dev),或交给 CI 里的 ubuntu 任务"
    fi
fi
