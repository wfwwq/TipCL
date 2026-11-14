#!/usr/bin/env bash
# tools/deb.sh — 把 dist/TipCL-linux-x64/TipCL.App 打成一个 .deb(M4-H R21h)
#
# 为什么要有它:AppImage 解决「下载就能跑」,但 Debian/Ubuntu 用户更习惯「装进系统、菜单里有、能卸载」。
# 这里**不引入任何新依赖** —— 只用系统自带的 dpkg-deb(apt 包 dpkg-dev),所以它和 tools/package.sh
# 一样是纯脚本,不需要往 csproj 里加任何 NuGet/MSBuild SDK。
#
# 用法:
#   ./tools/deb.sh                      # 用 src/TipCL.Core/CoreInfo.cs 里的版本号
#   ./tools/deb.sh --version 0.1.0-m4   # 显式指定版本(打包机没有源码树时用)
#   ./tools/deb.sh --bin dist/xxx/TipCL.App --out dist
#
# 产物:dist/tipcl_<版本>_amd64.deb
#
# 布局(与 AppImage 那套各自独立,互不依赖):
#   /opt/TipCL/TipCL.App                              self-contained 单文件本体
#   /usr/bin/tipcl                                    三行包装(exec /opt/TipCL/TipCL.App "$@")
#   /usr/share/applications/tipcl.desktop             桌面项(复用 tools/AppImage/tipcl.desktop)
#   /usr/share/icons/hicolor/256x256/apps/tipcl.png   图标
#
# 依赖声明(Depends)的口径:这个包是 self-contained 的 .NET,运行时自带,**但 UI 仍要系统的基础库**
# (X11 / 字体 / ICE / SM 这一层)。下面这份是**保守的最小集合**,在 Ubuntu 24.04 上实测可装可跑;
# 换发行版(Debian 12 / Mint 等)通常也满足,但如果目标机是极简环境,按 `ldd` 结果再补。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BIN="dist/TipCL-linux-x64/TipCL.App"
OUT="dist"
VERSION=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bin) BIN="$2"; shift 2 ;;
        --out) OUT="$2"; shift 2 ;;
        --version) VERSION="$2"; shift 2 ;;
        -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
        *) echo "未知参数: $1(支持 --bin / --out / --version)" >&2; exit 1 ;;
    esac
done

if [[ ! -f "$BIN" ]]; then
    echo "缺少 $BIN —— 先跑一次发布(工具链本机: tools/package.ps1 -SkipWin;Linux 上: tools/package.sh --no-appimage)" >&2
    exit 1
fi

if [[ -z "$VERSION" ]]; then
    COREINFO="src/TipCL.Core/CoreInfo.cs"
    if [[ -f "$COREINFO" ]]; then
        VERSION="$(sed -n 's/.*Version *= *"\([^"]*\)".*/\1/p' "$COREINFO" | head -1)"
    fi
fi
if [[ -z "$VERSION" ]]; then
    echo "拿不到版本号(源码树里没有 $COREINFO?)—— 用 --version <版本> 显式给一个" >&2
    exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
    echo "需要 dpkg-deb(apt-get install -y dpkg-dev)" >&2
    exit 1
fi

DEB="$OUT/tipcl_${VERSION}_amd64.deb"
PKG="$OUT/.deb-root"
rm -rf "$PKG"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/TipCL" "$PKG/usr/bin" \
         "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps"

install -m 0755 "$BIN" "$PKG/opt/TipCL/TipCL.App"

# 包装脚本:桌面项里 Exec=tipcl,真正启动的是 /opt 下那份单文件
cat > "$PKG/usr/bin/tipcl" <<'EOF'
#!/usr/bin/env bash
exec /opt/TipCL/TipCL.App "$@"
EOF
chmod 0755 "$PKG/usr/bin/tipcl"

install -m 0644 tools/AppImage/tipcl.desktop "$PKG/usr/share/applications/tipcl.desktop"
install -m 0644 tools/AppImage/tipcl.png "$PKG/usr/share/icons/hicolor/256x256/apps/tipcl.png"

SIZE_KB="$(du -sk "$PKG" | cut -f1)"
# 单文件本体约 43 MB,316x 倍是「解压后占用」的粗估(与 AppImage 那份同一个量级)
cat > "$PKG/DEBIAN/control" <<EOF
Package: tipcl
Version: $VERSION
Section: games
Priority: optional
Architecture: amd64
Maintainer: TipCL <noreply@example.com>
Installed-Size: $SIZE_KB
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1, libfreetype6
Description: TipCL —— AI 原生 Minecraft 启动器(Linux 版,自包含单文件)
 自包含 .NET 8 + Avalonia 的单文件产物,装完在应用菜单里出现 "TipCL"。
 与同目录的 .AppImage 是同一份二进制的两种分发形态(装 .deb 更省事,拿 AppImage 更便携)。
EOF

# postinst:更新桌面数据库/图标缓存(失败不算错 —— 很多环境没有这些工具)
cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/usr/bin/env bash
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
exit 0
EOF
chmod 0755 "$PKG/DEBIAN/postinst"

dpkg-deb --root-owner-group --build "$PKG" "$DEB" >/dev/null
rm -rf "$PKG"

echo "==================== 产物 ===================="
ls -l "$DEB"
echo "==================== 控制信息 ===================="
dpkg-deb -I "$DEB"
echo "==================== 内容(前 10 行) ===================="
# 注意:`dpkg-deb -c | head` 会因为 head 提前关管道而让 dpkg-deb 吃到 SIGPIPE(tar 子进程被杀,
# 退出码 2)—— 配合 set -o pipefail 整条脚本就"假失败"了(CI 里会红)。所以先落盘再 head。
dpkg-deb -c "$DEB" > "$OUT/.deb-contents.txt"
head -10 "$OUT/.deb-contents.txt"
rm -f "$OUT/.deb-contents.txt"
echo "提示:装/卸可以这样验(sudo 用得上时):"
echo "  sudo dpkg -i $DEB && tipcl --ai-selftest && sudo dpkg -r tipcl"
