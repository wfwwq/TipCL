# GOAL — TipCL macOS 版（自主执行任务书）

> 本文件是 mac 线的唯一任务书。每个子代理开工前先读完本文件，再读 `PROGRESS.md`。
> 仓库根 = 本文件所在目录 `D:\项目\tipcl\mac\`。用户已休息，全程无人值守，**不许向用户提问**，拿不准的按本文件裁决或选最保守方案并记入 `DECISIONS.md`。

## 1. 使命

把 TipCL（AI 原生 Minecraft 启动器，Windows 版为 WPF / .NET Framework 4.8）移植出一个 **macOS 版**，功能与 Windows 版完全对齐（含 AI 对话、插件系统、MCP Server、手机同步、联机），最终目标是在 Mac 上能真正安装并启动 Minecraft。

## 2. 唯一基准与红线

1. **唯一源码基准**：`D:\项目\tipcl\windos\tipcl\`（Windows 版仓库，HEAD 803dd38）。**只读**，任何文件都不许改、不许在里面构建（它有未提交改动，别碰）。
2. **禁止读取、引用、复制** `D:\项目\tipcl\linux\` 与 `D:\项目\tipcl\handoff-*\` 下的任何内容。mac 版完全独立于 Linux 版，这是用户的硬要求。
3. 只允许写 `D:\项目\tipcl\mac\` 之内。不装全局软件，不改系统设置。
4. 不 `git push`、不发 release、不打 tag。
5. **诚实**：不许把功能桩掉后报告“已完成”；本机验证不了的写“未验证”并说明原因；不许为了让测试变绿而删测试或改弱断言。
6. 界面文案中文，与 Windows 版一致（走 `I18n.L()` 的保持原样）。

## 3. 已定技术决定（不重新讨论）

| 项 | 决定 |
|---|---|
| UI 框架 | Avalonia 11（最新 11.x 稳定版）+ .NET 8，code-behind 风格，贴近 Windows 版「UI 全部用 C# 代码构建」的写法 |
| 方案 | `TipCL.Mac.sln`；`src/TipCL.App`（Avalonia 主工程，AssemblyName `TipCL`）、`src/TipCL.Core`（类库）、`tests/TipCL.Tests`（xunit） |
| TFM | 全部 `net8.0`；`LangVersion` 12；`Nullable` annotations；`AllowUnsafeBlocks` true；`NoWarn` 沿用 Windows csproj 的列表 |
| Core 复用 | 把 Windows 版 `src/TipCL/Core/` **整目录字节级复制**（`Copy-Item`）进 `src/TipCL.Core/`，命名空间不改（`TipCL.Core.*`），只修跨平台编译缺口。P/Invoke 声明保留（运行时都有 `IS_WINDOWS` 守卫），新增 mac 分支而不是删 Windows 分支 |
| 后端架构 | 与 Windows 版一致：进程内 `BackendApi` + `RpcDispatcher` + `BridgeServer`（回环 HTTP + SSE）+ `BridgeClient`。只去掉外部 Python 桥。页面照旧用 `CallAsync("方法名", args)` |
| 内嵌资源 | `Resources/locales`、`Resources/ai`、`Resources/plugin-host` 以与 Windows csproj 相同的 `LogicalName`（`TipCL.Core.*`）嵌入；图标字体 `TipCLIcons.ttf`、壁纸、`PremiumAssets` 全部直接内嵌（mac 版无体积红线，只出一个版本，等同 premium） |
| 架构 | arm64 + x64 合成一个通用包：`TipCL.app/Contents/MacOS/TipCL` 是 shell 启动脚本，按 `uname -m` exec `Contents/MacOS/arm64/TipCL` 或 `x64/TipCL`（本机没有 `lipo`，不做 fat binary） |
| 分发 | 未签名 `.app`，打成 `.tar.gz`（必须保留 0755 权限位，Windows 上用 `System.Formats.Tar` 或等价方式写入 mode）。README 写明首次打开需 `xattr -cr TipCL.app` |
| 窗口 | 900×550，Min 720×480，标题栏 46px。用系统红绿灯（`ExtendClientAreaToDecorationsHint`），标题栏内容左侧让出约 78px；不画 Windows 式最小化/最大化/关闭钮。接 `NativeMenu` 系统菜单栏，快捷键 Ctrl→⌘（⌘1…9 切页、⌘K AI、⌘, 设置、⌘R 刷新） |
| 字体 | `PingFang SC, Hiragino Sans GB, Microsoft YaHei UI, sans-serif`；等宽 `Menlo, Consolas, monospace` |
| 数据目录 | `~/.tipcl`（`Utils.ResolveRoot` 非 Windows 分支现状），`TIPCL_HOME` 可覆盖 |
| Windows 专属功能 | 管理员提权：去掉。自动更新：只提示新版本 + 打开下载页，不做自替换。插件网页界面：用系统浏览器打开（不嵌 WebView）。compact/premium 双版本与体积红线：不要 |
| 凭据 | `ISecretStore` 的 mac 实现走 `/usr/bin/security`（Keychain generic password）；插件 `SecretStore` 的 DPAPI 在非 Windows 上改为 AES-GCM，密钥存 Keychain |
| 图片 | `IImageCodec` 用 SkiaSharp 实现（Avalonia 自带），WebP 也走 Skia，不引 ImageSharp |
| 版本号 | Core 里的 `AppInfo.APP_VERSION` 保持 Windows 版原值不动；`.app` 的 `CFBundleShortVersionString` 取同值 |

## 4. 本机环境与验证手段

- 本机是 **Windows**，dotnet SDK 8.0.427 / 10.0.403，**没有 Mac**。mac 产物跑不了，但 Avalonia 能在 Windows 上编译运行，所以：
  - `dotnet build` / `dotnet test` 必须真跑；
  - `TipCL --smoke [--page <id>] [--theme light|dark] [--out x.png]`：Avalonia.Headless 离屏渲染截图后退出（退出码 0=成功），用它在 Windows 上验收界面；
  - mac 专属逻辑（路径、平台键、下载包名、Keychain 命令行拼装等）写成**可注入 OS/ARCH 的纯函数**并配单测；
  - osx 产物只能做结构校验：Mach-O 魔数、`.app` 目录结构、`Info.plist`、tar 内权限位。
- 「在 Mac 上真机启动 Minecraft」本机**无法验证**，一律标「未验证」，并把待验步骤写进 `docs/MAC-VERIFY.md`。
- 构建防锁：先 `dotnet build-server shutdown`，再 `dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"`。

## 5. 上下文纪律（模型单次请求体上限 4 MB，必须遵守）

1. 一个子代理只做一个小工作包，做完就收尾回报，不要顺手做下一个。
2. 不要整文件读大文件：先 `grep` 定位，再按行区间读（一次 ≤ 200 行）。
3. 批量搬文件用 `Copy-Item`（字节级），**不要**「读出来再写回去」；含中文的文件禁止用 `Get-Content | Set-Content` 重写（会坏编码），改内容只用 edit 工具。
4. 构建/测试输出只看错误摘要（上面的 `-v:q -clp` 参数）；错误多时先 `Select-Object -First 40`，按类别成批修。
5. 不读图片、二进制、`bin/`、`obj/`；`Services/LucideAssets.cs` 是整文件 base64，别打进上下文。
6. 控制台里中文显示成乱码是代码页问题，不代表文件坏了，别去“修”。

## 6. 每个工作包的收尾（缺一项算未完成）

1. `dotnet build` 0 错误；`dotnet test` 全绿（既有红的要说明）。
2. `git add -A && git commit -m "<包编号> <一句话>"`（仓库内 `user.name=tipcl-mac agent`、`user.email=tipcl-mac@local.invalid`，只设本仓库）。
3. 在 `PROGRESS.md` 末尾追加一小节：做了什么、关键文件、验证命令与结果、遗留与风险。
4. 最终回报 ≤ 30 行：完成项 / 实际跑过的验证命令与结果 / 未验证项 / 遗留。

## 7. 路线图（由 Lead 逐个派发，子代理只做被点名的那一个）

### A 地基
- **A1 脚手架**：`git init`、sln、三工程、空 Avalonia 窗口、`--smoke` 出图、`tools/build.ps1`、`.gitignore`、`PROGRESS.md`/`DECISIONS.md` 初版。
- **A2 Core 基座**：复制 `Core/*.cs` 顶层（Py 运行时等）+ `Core/Platform` 的 5 个接口文件 + `Resources/`（locales、ai、plugin-host）并按 LogicalName 嵌入；编译通过。
- **A3 Core/Launcher 主体**：复制 `Core/Launcher/*.cs`（不含子目录）并修到编译通过。
- **A4 Core/Launcher/Ai + Sync**。
- **A5 Core/Plugins + BackendApi + RpcDispatcher + BridgeServer**；`DotNetPluginContext`、`Sdk/*` 对 WPF 的依赖抽象成接口。
- **A6 测试移植**：把 Windows `tests/TipCL.Tests` 里不依赖 WPF 的测试搬过来跑通。

### B mac 平台层
- **B1 平台服务**：`MacKeychainSecretStore`、`SkiaImageCodec`、插件 `SecretStore` 非 Windows 分支、`ShellOpen`/打开浏览器走 `open`、去提权。
- **B2 mac 逻辑核对 + 单测**：Mojang Java 运行时平台键（对照线上清单实测，Windows 版写的是 `osx`/`osx-arm64`，疑似应为 `mac-os`/`mac-os-arm64`）、Adoptium/Zulu 的 mac 包（`.tar.gz`、`Contents/Home/bin/java`）、natives classifier 选择、规则里的 `-XstartOnFirstThread`、`NodeRuntime` 的 darwin 下载、`Updater` 仅提示、Terracotta 可执行权限、adb 路径、`Shortcut`。
- **B3 打包**：`tools/package.ps1` 发布 `osx-arm64` + `osx-x64`（self-contained），组装通用 `TipCL.app`（`Info.plist`、`.icns`、启动脚本），产出保留权限位的 `dist/TipCL-macos-universal.tar.gz`，并做结构校验。

### C 界面
- **C1 主题**：`Themes/Palette.Light|Dark` 与 `Controls` 移植为 axaml（键名不变）、图标字体、`Ui`/`UiExt` 门面。
- **C2 外壳**：`MainWindow`、导航（`NavModel`/`NavConfig`/`NavDef`）、`PageBase`、Toast/Dialog/DropVeil 宿主、系统菜单与快捷键、`BridgeHost`（仅进程内）/`BridgeClient`、基础 Services（`Dlg`/`Motion`/`TaskStore`/`I18n`/`Fmt` 等）。
- **C3… 页面逐个移植**（顺序：启动 → 实例库+实例详情 → 下载+安装 → 任务 → 设置 → 账号 → Java → 联机 → 目录 Catalog → 版本管理 → 插件全家 → AI 全家 → 其余对话框/向导/崩溃/反馈/时长/服务器）。每页一包，带 `--smoke --page <id>` 明暗两帧。

### D 收尾
- **D1** 全页冒烟矩阵；**D2** `README.md`、`docs/MAC-VERIFY.md`（真机验证清单）。
