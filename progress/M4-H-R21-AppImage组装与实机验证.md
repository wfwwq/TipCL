# M4-H R21:AppImage 组装与实机验证

状态:流程已在真机打通(2026-11-13,干跑用的是当时的产物);最终产物出来后再跑一遍并归档

## 为什么现在才做

`tools/package.sh`(M3-C2 写的)要求**在 Linux 上跑**,而它的第一步是 `dotnet publish` —— 盒子(Ubuntu 24.04.5,user,免密 sudo)**没有 dotnet**,所以这条路在盒子上根本走不通;本地能 publish,但本地不是 Linux,生不出 Linux AppImage。结果:`dist/` 里一直只有 `TipCL-linux-x64/` 与 zip,**没有 `.AppImage`**(脚本里那段 `linuxdeploy` 分支从来没被走到过)。

## 打通后的流程(可复现)

**前置条件(两条都是本轮踩出来的):**

1. 发布仍在本地/CI 做:`tools/package.ps1 -SkipWin` → `dist/TipCL-linux-x64/TipCL.App`(自包含单文件),再把产物拷到盒子;
2. 盒子上:**`file` 包必须存在**(`sudo apt-get install -y file`)。`appimagetool` 依赖它做 MIME 识别,缺了会以 `ERROR: file command is missing but required` 直接失败(linuxdeploy 自己会先跑完 desktop/icon 部署,只有最后一步挂);
3. 运行侧:盒子有 `/dev/fuse`,但在这个环境里直接执行 AppImage 仍不可靠 —— 已验证可用的方式是 `APPIMAGE_EXTRACT_AND_RUN=1 <appimage> <args>`(解包到临时目录再执行,不需要 FUSE)。

**组装步骤(纯 bash,在盒子上做):**

```bash
# 1. AppDir 布局(与 tools/package.sh 里那段注释一致)
mkdir -p TipCL.AppDir/usr/bin TipCL.AppDir/usr/share/applications TipCL.AppDir/usr/share/icons/hicolor/256x256/apps
cp TipCL.AppDir.bin  TipCL.AppDir/usr/bin/TipCL            # ← 本地发布出来的单文件
cp tools/AppImage/tipcl.desktop TipCL.AppDir/tipcl.desktop
cp tools/AppImage/tipcl.png     TipCL.AppDir/tipcl.png
cp tools/AppImage/tipcl.desktop TipCL.AppDir/usr/share/applications/tipcl.desktop
cp tools/AppImage/tipcl.png     TipCL.AppDir/usr/share/icons/hicolor/256x256/apps/tipcl.png
printf '%s\n' '#!/usr/bin/env bash' 'HERE="$(cd "$(dirname "$(readlink -f "${0}")")" && pwd)"' 'exec "$HERE/usr/bin/TipCL" "$@"' > TipCL.AppDir/AppRun
chmod +x TipCL.AppDir/AppRun TipCL.AppDir/usr/bin/TipCL

# 2. linuxdeploy(先用 APPIMAGE_EXTRACT_AND_RUN=1 跑它自己,免 FUSE)
curl -fSL -o linuxdeploy.AppImage https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage
chmod +x linuxdeploy.AppImage
APPIMAGE_EXTRACT_AND_RUN=1 ./linuxdeploy.AppImage --appdir TipCL.AppDir \
    --desktop-file=TipCL.AppDir/tipcl.desktop --icon-file=TipCL.AppDir/tipcl.png --output appimage
# 产物:TipCL-x86_64.AppImage(名字取自 desktop 的 Name 字段)
```

## 干跑结果(2026-11-13,产物 = 当时的 `tipcl14`,44,966,962 B)

- `linuxdeploy` 全流程成功:`-- Deploying desktop files -- / -- Copying files into AppDir -- / Running output plugin: appimage / **Success**`;
- **`TipCL-x86_64.AppImage` = 38,959,608 B**(38.9 MB;比单文件产物小,因为 AppImage 自己压缩 squashfs);
- 真机执行:`TIPCL_HOME=~/tipcl/smoke19 APPIMAGE_EXTRACT_AND_RUN=1 ./TipCL-x86_64.AppImage --smoke` → **exit 0**,生成 `smoke.png` 60,101 B,应用日志 `=== TipCL 0.1.0-m3 启动 pid=87161 os=Unix 6.8.0.142 ===` + `home=/home/user/tipcl/smoke19 cwd=/home/user/tipcl/appimg-run`,**0 WARN**;
- 证据:`_cmp/box-appimage-out.txt`(AppDir 组装)、`_cmp/box-appimage2-out.txt`(装 `file` 后出包 + 冒烟)。

## 还差什么(最终收尾时做)

1. 用**最终产物**重跑一遍(本轮之后 R15~R20 还会改代码),把 `dist/TipCL-linux-x64.AppImage` 归档出来;
2. 补一次**不带 `APPIMAGE_EXTRACT_AND_RUN`** 的裸跑(看这台盒子的 FUSE 到底能不能挂),结果如实写;
3. 确认 `tools/package.sh` 的注释与本文档一致(两点前置条件:`file` 包、FUSE/extract-and-run),必要时把 `file` 缺失与 FUSE 这两条写进脚本的提示文案;
4. 双击可用性:AppImage 需要 `chmod +x` + 桌面文件里 `Exec` 指向 AppImage 才行,这部分与已归档的 `.desktop` 快捷方式链(D-23)不是同一件事,文档里要写清各自覆盖的路径。
