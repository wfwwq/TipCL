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

仓库根的 `TipCL.Linux.sln` 登记了上面三个工程。版本号 `1.2.0` 必须三处一致:`src/TipCL.App/TipCL.App.csproj`
与 `src/TipCL.Core/TipCL.Core.csproj` 的 `<Version>`、`src/TipCL.Core/CoreInfo.cs` 的常量 —— `tests/TipCL.Tests/SmokeTests.cs` 会断言它,并顺带断言它与 `AppInfo.APP_VERSION` 相同。**(2026-11-14 由 `0.1.0-m3` 统一为 `1.1.0`,2026-11-15 抬到 `1.2.0`;`tools/deb.sh` 就是从 `CoreInfo.cs` 取版本号的,所以 deb 包名与内嵌版本也随之变成 `1.2.0`。)**

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

`--smoke` 用 Avalonia Headless(`UseHeadlessDrawing = false` + 真 Skia)离屏渲染主窗口,**截图写到进程的当前工作目录**下的 `smoke.png`(在仓库根用 `dotnet run --project ...` 跑才是仓库根;换工作目录就跟着换),尺寸固定 **900×550**(窗口尺寸常量,不可覆盖)。退出码 0 = 渲染成功;`build.ps1` 把小于 1 KB 的截图当作疑似空白渲染。

**Linux 树只认环境变量,不解析命令行开关**(2026-11-16 实测,`1823c3c`):`Session/SmokeRunner.cs` 不读参数数组,`--smoke` 之外的一切开关都被**静默忽略** —— 实测 `--smoke --page library`、`--smoke --theme dark`、`--smoke --out x.png`、`--smoke --shot`、`--smoke --size 900x550`、`--smoke --width 1000 --height 600` **全部 exit 0**,出的帧与不带这些开关时**同尺寸同亮度**,产物仍然只有 `smoke.png`。`--shot / --size / --width / --height` 是 **Windows 版开发通道**,Linux 与 mac 都没有(mac 上同类开关是 exit 2 + stderr「未知参数」,见 mac 树的 `docs/布局使用指南.md`)。

真实通道是这些环境变量(全部读自 `Session/SmokeRunner.cs`):

| 环境变量 | 作用(实测口径) |
|---|---|
| `SMOKE_PAGE=<页面键>` | 先切到该页。合法键:`launch` / `library` / `instance` / `download` / `plugins` / `settings` / `ai` / `account` / `tasks` / `multiplayer`;未知键**静默留在首屏**(实测 `SMOKE_PAGE=no-such-page` → exit 0,帧与首屏同尺寸同亮度) |
| `SMOKE_DARK=1` | 深色帧。实测 `SMOKE_PAGE=settings SMOKE_DARK=1` 平均亮度 **34.0**,浅色帧 242.7(`library`)/ 95.6(启动页) |
| `SMOKE_SEG=1..6` | 下载页目录分段。实测下载页帧 23 655 B → 36 275 B |
| `SMOKE_SETTINGS_TAB=0..6` | 设置页内层导航项。实测设置页帧 37 729 B → 44 294 B |
| `SMOKE_WAIT=<秒>` | 截图前 settle 等待;不给时用 400 ms 的最小 settle |
| `SMOKE_TASKS=1` | 塞两条假任务(任务页视觉回归) |
| `SMOKE_PROBE=1` | 打印整棵可视树(类型 / 样式类 / 边界) |
| `SMOKE_PROBE=dialog-close\|popup-theme\|widgets\|widgets-lifetime` | 四支专用回归探针 |
| `SMOKE_NET=<url>` / `SMOKE_INSTALL=<版本>` | 网络探针 / 无头安装 E2E 钩子 |

其余命令行入口在 `src/TipCL.App/Session/`:`--ai-selftest` 跑 AI 功能自检（不起 GUI、不联网，结果打到 stdout）；`-i <实例> launch <版本> [--force] [--print-command]` 走与界面相同的启动链路拉起游戏，实例不存在时按用法错误退出（退出码 4）。
数据目录由 `TIPCL_HOME` 决定，文件日志落在 `$TIPCL_HOME/logs/tipcl-YYYYMMDD.log`；冒烟截图与 `dist/` 都在 `.gitignore` 里，不入库。

## 产物

打包入口：Linux 上 `tools/package.sh`，Windows 上 `tools/package.ps1`（可双端），`.deb` 另由 `tools/deb.sh` 打。
产物：`dist/TipCL-linux-x64/TipCL.App`（Linux x64 单文件）、`dist/TipCL-linux-x64.AppImage`(AppImage)、
`dist/tipcl_1.2.0_amd64.deb`（Debian 包）、`dist/TipCL-win-x64/TipCL.App.exe`（Windows x64 单文件）。
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
[`docs/RELEASE-CHECKLIST.md`](RELEASE-CHECKLIST.md) **发版前的元数据检查单**（抬版本号要改哪 6 处 / 重算哪些 sha256 / 回填哪两份清单 / 发完怎么复核） ·
[交接必读.md](../交接必读.md) 接手流程（立项阶段写成，现状以 PROGRESS.md 为准）。

布局 v2 的 **Windows 版**手册(1.2.0):[docs/布局使用指南.md](布局使用指南.md) 完整手册(三个入口 / 手势 / 三种模式 / 分栏 / 整机方案导入导出 / 自建区块 / AI 排版回退 / 本版的坑)·
[docs/布局快速上手.md](布局快速上手.md) 30 秒版 + 手势速查表 + 10 条 FAQ。**这两份写的是 Windows 桌面版行为**;Linux 侧入口都在(「AI 排版」已于 2026-11-21 补齐,见下),但**编辑态手感有四处差异**(见下面「Linux 编辑态与 Windows 的四处差异」)。

### 布局 v2:Linux 移植版的真实进度(截至 `b86afe3`,2026-11-20 复核)

已落地(都能在本树 `src/` 里指到文件):

| 能力 | 落点 |
|---|---|
| v2 文档真源 + v1 单向写通 | `src/TipCL.Core/Launcher/Layout/LayoutStoreV2.cs`、`LayoutSchema.cs`、`LayoutDocV2.cs` |
| 布局引擎(stack / grid / canvas、打包、手势模型、撤销) | `src/TipCL.App/Pages/Layout/LayoutEngine.cs` |
| 编辑器 + 区块宿主 + 右键「自定义布局...」全局钩子 | `Pages/Layout/LayoutEditor.cs`、`BlockHost.cs`、`LayoutShellHooks.cs` |
| 自建区块内容工厂(5 个安全类型 + 2 个危险类型) | `Pages/Layout/WidgetRegistry.cs`、`Pages/Layout/Widgets/` |
| 启动时注册部件(R32-LINT 实测真窗口上屏 5 个部件) | `App.axaml.cs` |
| 设置页整页「布局」视图(7 小节:外观与密度 / 页签与标题栏 / 页面区块 / 分栏比例 / 自建区块 / 布局方案 / 布局 JSON) | `Pages/Settings/SettingsPage.Layout.cs`(`OpenLayoutView()`) |
| 分栏比例(`SplitCatalog` / `SplitRatioStore` / `PageLayoutHost`,13 条站点表) | `src/TipCL.App/Controls/PageLayoutHost.cs` |
| 整机方案(增删改查 / 导入导出;v2 真源 + v1 兼容导出) | `Pages/Layout/LayoutProfiles.cs` |
| 布局 JSON 卡片(校验 / 应用 / 刷新) | `Pages/Layout/LayoutJsonEditor.cs` |
| 页签与标题栏编辑 | `Pages/Layout/ShellTabs.cs` |

**AI 排版工具(2026-11-21 已落地,AI-acct-layout-mp 包)**:`layout_get` / `layout_apply` 在 `src/TipCL.Core/Launcher/Ai/LayoutTools.cs`,应用前先把当前布局存成「AI 备份 yyyy-MM-dd HH:mm:ss」方案(只留最近 5 份),改坏了可在「设置 → 布局 → 布局方案」切回。与 Windows 手册的差别:Linux 版对 `widgets` 参数和整份 `json` 都执行 D-30 门禁(web 只认 https;button 只认 `page:` / `launch:` / `folder:data` / `rpc:` + 只读接口白名单,不合法整次拒绝且不产生备份),AI 写入或改过地址 / 动作的网页 / 按钮区块一律 `enabled=false`,要用户自己开启;设置页「布局」视图若正开着,AI 改完后要切一下视图才刷新方案列表。

### Linux 编辑态与 Windows 的四处差异(2026-11-20 复核,别当 bug 修)

Windows 侧 R3 把 stack 纵向拖拽改成了"浮起跟手 + 滞回换位 + FLIP + 摆锤式边缘滚动",**Linux 走的不是同一条路**。下面四条都是**刻意的**(各有代码依据),改之前先读这一节:

| # | 差异 | Linux 的做法(证据) | Windows 的做法 | 后果 |
|---|---|---|---|---|
| 1 | stack 页怎么拖 | 进编辑态时**临时折成 12 列 grid**(按现状逐行折算坐标),退出后**保留网格、不还原成 stack**:`Pages/Layout/LayoutEditor.cs:518-546`(`ConvertStackToGrid` → `LayoutEngine.StackToGrid`)、`:1154`、`:1278` 的注释 | stack 保持一维纵向流,拖拽是专门的换序手势(`BeginDrag`/`MoveStackFloat`/`ApplyStackReorder`) | Linux **没有**"一维换序"这个概念:拖 stack 页的块 = 拖 grid 里的块(像素跟手、整格落位,`LayoutEditor.cs:1969`) |
| 2 | 换位会不会"抖" | 靠 grid 的落点占位 + FLIP 让位;换位判定是格子运算,天然没有"中点附近来回翻"的问题 | 有**显式滞回**:越过相邻块中点 **±6px** 才换位(`StackDragPlan.cs:21` `Hysteresis = 6.0`) | 手感不同:Windows 是"轻推不换、推够才换";Linux 是"跟着鼠标走、到格就换" |
| 3 | 边缘自动滚动 | **30ms** 一帧、边缘区 **48px**、每帧最多 **28px**(`(depth*0.6+4)`,越靠边越快):`LayoutEditor.cs:2171-2215` | **16ms** 一帧、边缘区 **36px**、每帧最多 **18px**(`0.25+0.75*depth`):`StackDragPlan.cs:77-99`、`LayoutEditor.cs:162-165,1858-1873` | 速度曲线不同;两边都是"指针停住也继续滚" |
| 4 | 滚动条冒出来之后 | **按新宽度重量行高**:Arrange 宽 ≠ 测量宽 → 重跑第一趟测量;只在"更窄且总高确实变了"时补一次 `InvalidateMeasure`,同一宽度只补一次(`_remeasureWidths` 守卫):`Pages/Layout/GridCanvasPanel.cs:667-679`、`:111`、`:513-515` 注释 | 同口径(`_remeasureWidths` 守卫来自参考件) | 用户体感一致:**滚动条一出现,卡片内容不会被切一刀** |

**`height_px` 的存储表示不同但语义等价(最容易误判成 bug 的一条)**:

- **Windows**:它是 v2 模型上的**类型化字段** `LayoutBlockV2.HeightPx`(`windos/src/TipCL/Core/Launcher/Layout/LayoutDocV2.cs:977`,`:1032` 写出、`:1088` 读入、`:1116` 清掉);
- **Linux**:它住在区块的**通用 `extra` 字典**里(`Pages/Layout/LayoutEngine.cs:1201-1243` 的 `ReadHeightPx` / `WriteHeightPx`,键名同为 `height_px`);
- **两者落到 JSON 里是同一个位置**:`LayoutJson.MergeExtra` 把 `Extra` **平铺**合并进区块对象(`src/TipCL.Core/Launcher/Layout/LayoutJson.cs` 的 `MergeExtra`:`target[kv.Key] = ...`,不加嵌套层),所以磁盘上都是区块对象的顶层 `"height_px": 172`。
- ⇒ 同一份 `ui_layout_v2.json` 两侧都能读、都能写、都不会丢这个值。**看到"Linux 里用 extra、Windows 里用字段"不要当存储格式不一致去"统一"**,那会白改一轮。

## 与 Windows 版的关系

- `windos/` 是**只读参照**:移植只从它复制可跨平台的文件（搬来的文件头会注明来源路径），
  注册表 / Win32 / WPF 相关代码不搬，缺口用条件编译或桩实现。
- 更新清单**必须留在仓库根**:Windows 版读 [`update.json`](../update.json)，移植读 [`update-linux.json`](../update-linux.json)，
  两者都按 `https://raw.githubusercontent.com/wfwwq/TipCL/main/` 下的固定文件名读取 —— 挪进子目录会让**已经装在用户机器上的客户端**更新失效；`LICENSE` 同属仓库级，也留根。
- 仓库根的 [README.md](../README.md) 是 Windows 桌面版的说明（GitHub 首页只渲染根 README），本文是移植工程的说明。
