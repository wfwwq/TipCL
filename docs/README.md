# TipCL Linux 版（M1）

TipCL 是 AI 原生 Minecraft 启动器；本仓库是它的 Linux 版工程（Avalonia UI 11 + .NET 8）。
Windows 版（WPF / .NET Framework 4.8）仓库只读参照，位于 `D:\项目\tipcl\windos\tipcl\`。

立项与分工见 [GOAL.md](../GOAL.md)、[PROGRESS.md](../PROGRESS.md)、[DECISIONS.md](../DECISIONS.md)、[交接必读.md](../交接必读.md)。

## 仓库结构

| 路径 | 内容 |
|---|---|
| `src/TipCL.App/` | Avalonia 主工程（外壳、主题、`--smoke` 冒烟入口） |
| `src/TipCL.Core/` | 业务复用类库（从 Windows 版搬运的跨平台代码） |
| `tests/TipCL.Tests/` | xunit 测试（Core 单测 + 主题键存在性 + 冒烟占位） |
| `tools/` | 一键构建脚本 `build.ps1` / `build.sh` |
| `progress/` | 各工作包进度记录 |

## 环境

- .NET SDK 8.0（本机另装了 10.x 也可，工程 TFM 为 net8.0）
- Avalonia 11.x（NuGet 自动还原）
- Windows 上即可编译与冒烟（Avalonia Headless）；Linux 真机验证在 M3

## 构建与测试

一键脚本（推荐）：

```bash
# Windows (PowerShell)
powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test          # 构建 + 测试
powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test -Smoke   # 再加冒烟

# Linux / macOS / Git Bash
./tools/build.sh Release --test
./tools/build.sh Release --test --smoke
```

或直接用 dotnet CLI：

```bash
dotnet build TipCL.Linux.sln -c Release -p:UseSharedCompilation=false -m:1
dotnet test  TipCL.Linux.sln -c Release --no-build
```

防锁说明（GOAL §6.4）：脚本在构建前执行 `dotnet build-server shutdown`，并加 `-p:UseSharedCompilation=false -m:1`；若遇 CS2012 文件锁，脚本会自动 shutdown 等 10 秒重试（最多 3 次）。

## 运行

```bash
dotnet run --project src/TipCL.App
```

应弹出 1180×740 启动器外壳窗口：自定义标题栏（Logo + 5 页签 + 窗口按钮）、左侧 196px 分组导航、明暗主题可切换。

## 冒烟测试（--smoke）

```bash
dotnet run --project src/TipCL.App -c Release -- --smoke
```

行为：以 Avalonia Headless 无头平台启动，离屏渲染主窗口，把截图保存到仓库根的 `smoke.png`，然后退出。

- 退出码 0 = 渲染成功；1 = 捕获失败或渲染异常。
- 脚本入口（`-Smoke` / `--smoke`）会额外校验 `smoke.png` 存在且非空（< 1 KB 视为疑似空白渲染）。
- **人工核对**：打开 `smoke.png`，确认窗口外观——标题栏、页签、侧边导航、主题配色正常，无空白/乱版。
- M1 阶段 `--smoke` 渲染的是 A3 的 MainWindow 外壳；A2 的 ThemeGalleryPage 样张页后续也可用于冒烟核对主题令牌。

## 测试内容（tests/TipCL.Tests）

| 测试 | 说明 | 依赖 |
|---|---|---|
| `SmokeTests` | 测试基建自检（Core 版本号 = 0.1.0-m1） | 无 |
| `Theme.ThemeKeysTests` | 解析 `Palette.Light.axaml` / `Palette.Dark.axaml` 源文件：明暗键集合一致、键数达标、关键键在（B.\* 画笔 / C.\* 颜色） | M1-A2 的主题文件；未落地时 `Skip="等待 M1-A2"` |
| `Smoke.SmokeIntegrationTests` | 冒烟流程占位：`--smoke` 进程级冒烟由 tools 脚本承担（App 入口 internal） | tools/build.\* |
| Core 单测（`Core/` 目录） | 按 A4 搬运结果对数据类/纯逻辑补充 | M1-A4 |

注意：A2/A4 落地后，对应 `Skip` 需移除并补齐 Core 单测；测试工程的 csproj 对 TipCL.App 的引用按工程存在性自动启用。

## 版本

`0.1.0-m1`（M1 冻结，不 tag、不 release）。
