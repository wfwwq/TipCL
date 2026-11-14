# TipCL Linux 版（Avalonia 移植）

TipCL 是 AI 原生 Minecraft 启动器；本仓库根就是它的 **Linux 版**工程：Avalonia UI 11 + .NET 8。
Windows 桌面版(WPF / .NET Framework 4.8)是**只读参照**，整棵树在同一仓库的 `windos/` 子目录里，移植不改动它。
立项口径见 [GOAL.md](../GOAL.md)，逐轮过程见 [PROGRESS.md](../PROGRESS.md)，技术决策见 [DECISIONS.md](../DECISIONS.md)。

## 仓库结构

| 路径 | 内容 |
|---|---|
| `src/TipCL.App/` | Avalonia 主工程：外壳、页面、主题、`--smoke` 冒烟入口 |
| `src/TipCL.Core/` | 业务复用类库（从 Windows 版 `Core/` `Services/` 复制可跨平台文件） |
| `tests/TipCL.Tests/` | xunit 测试（App 界面组 / Core / Theme / Layout / Smoke） |
| `tools/` | 构建与打包脚本：`build.sh` / `build.ps1` / `package.sh` / `package.ps1` / `deb.sh` |
| `docs/` | 本文、交付说明、与 Windows 版的对照矩阵 |
| `progress/` | 各工作包的收尾报告 |
| `windos/` | Windows 桌面版整棵树（**只读参照**） |

仓库根的 `TipCL.Linux.sln` 登记了上面三个工程。版本号 `0.1.0-m3` 必须三处一致：`src/TipCL.App/TipCL.App.csproj`
与 `src/TipCL.Core/TipCL.Core.csproj` 的 `<Version>`、`src/TipCL.Core/CoreInfo.cs` 的常量 —— `tests/TipCL.Tests/SmokeTests.cs` 会断言它。

## 环境要求

- .NET SDK 8.0（CI 用 `8.0.x`；工程 TFM 为 `net8.0`）
- Avalonia 11.3.2（NuGet 自动还原，不必手工安装）
- 运行端要 Linux 桌面基础库：libX11 / libxcb、fontconfig + freetype、libGL（Avalonia / Skia 的标准依赖）
- 不内嵌字体，中文走系统回退链（Noto Sans CJK SC / WenQuanYi Micro Hei 等）

## 构建与测试

一键脚本（推荐，在仓库根执行）:

```bash
./tools/build.sh                          # 只构建，默认 Release
./tools/build.sh Release --test           # 构建 + 测试
./tools/build.sh Release --test --smoke   # 再跑一次冒烟

powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test          # 构建 + 测试
powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test -Smoke   # 再加冒烟

# 或直接用 dotnet CLI
dotnet build TipCL.Linux.sln -c Release -p:UseSharedCompilation=false -m:1
dotnet test  TipCL.Linux.sln -c Release --no-build
```

两个脚本在构建前先 `dotnet build-server shutdown`，并固定带 `-p:UseSharedCompilation=false -m:1` 防文件锁；
`build.ps1` 遇 CS2012 文件锁会 shutdown 后等 10 秒重试（最多 3 次）。

## 运行与冒烟

```bash
dotnet run --project src/TipCL.App                        # 正常开窗
dotnet run --project src/TipCL.App -c Release -- --smoke  # 无头冒烟，写 smoke.png 后退出
```

`--smoke` 用 Avalonia Headless（`UseHeadlessDrawing = false` + 真 Skia）离屏渲染主窗口，截图落到仓库根 `smoke.png`，
退出码 0 = 渲染成功；`build.ps1` 把小于 1 KB 的截图当作疑似空白渲染。可选环境变量：`SMOKE_PAGE=<页面键>`（先切到该页）、
`SMOKE_DARK=1`（深色帧）、`SMOKE_SEG=1..6`（下载页目录分段）、`SMOKE_SETTINGS_TAB=0..6`（设置页内层导航项）。

其余命令行入口在 `src/TipCL.App/Session/`:`--ai-selftest` 跑 AI 功能自检（不起 GUI、不联网，结果打到 stdout）；`-i <实例> launch <版本> [--force] [--print-command]` 走与界面相同的启动链路拉起游戏，实例不存在时按用法错误退出（退出码 4）。
数据目录由 `TIPCL_HOME` 决定，文件日志落在 `$TIPCL_HOME/logs/tipcl-YYYYMMDD.log`；冒烟截图与 `dist/` 都在 `.gitignore` 里，不入库。

## 产物

打包入口：Linux 上 `tools/package.sh`，Windows 上 `tools/package.ps1`（可双端），`.deb` 另由 `tools/deb.sh` 打。
产物：`dist/TipCL-linux-x64/TipCL.App`（Linux x64 单文件）、`dist/TipCL-linux-x64.AppImage`(AppImage)、
`dist/tipcl_0.1.0-m3_amd64.deb`（Debian 包）、`dist/TipCL-win-x64/TipCL.App.exe`（Windows x64 单文件）。
发布参数：`--self-contained true`、`-p:PublishSingleFile=true`、`-p:IncludeNativeLibrariesForSelfExtract=true`、`-p:EnableCompressionInSingleFile=true`（约 72 MB → 43 MB）；**不开 `PublishTrimmed`**(Core 的 `System.Text.Json` 反射路径会被剪掉，取舍见 [docs/DELIVERY.md](DELIVERY.md))。
CI 见 [`.github/workflows/ci.yml`](../.github/workflows/ci.yml):`test`（ubuntu + windows 各跑一遍）、
`package-linux`（单文件 → AppImage + `.deb`，并用 xvfb 跑一次 `--smoke`）、`package-win`（单文件 → zip）；只动 `windos/**` 的提交不触发它。

## 文档入口

[GOAL.md](../GOAL.md) 立项任务书（M1 / M2 / M3 的范围、技术决定与验收口径）·
[PROGRESS.md](../PROGRESS.md) 逐轮过程记录（当前能力看末尾几节）·
[DECISIONS.md](../DECISIONS.md) 决策 D-1 起，含踩坑与事后更正 ·
[docs/PARITY.md](PARITY.md) 与 Windows 版的逐项对照矩阵 ·
[docs/DELIVERY.md](DELIVERY.md) 交付物清单、发布参数、冒烟矩阵 ·
[`progress/`](../progress/) 各工作包收尾报告 ·
[`docs/m2-recon/`](m2-recon/) M2 立项侦察报告 ·
[交接必读.md]（../交接必读.md） 接手流程（立项阶段写成，现状以 PROGRESS.md 为准）。

## 与 Windows 版的关系

- `windos/` 是**只读参照**:移植只从它复制可跨平台的文件（搬来的文件头会注明来源路径），
  注册表 / Win32 / WPF 相关代码不搬，缺口用条件编译或桩实现。
- 更新清单**必须留在仓库根**:Windows 版读 [`update.json`](../update.json)，移植读 [`update-linux.json`](../update-linux.json)，
  两者都按 `https://raw.githubusercontent.com/wfwwq/TipCL/main/` 下的固定文件名读取 —— 挪进子目录会让**已经装在用户机器上的客户端**更新失效；`LICENSE` 同属仓库级，也留根。
- 仓库根的 [README.md](../README.md) 是 Windows 桌面版的说明（GitHub 首页只渲染根 README），本文是移植工程的说明。
