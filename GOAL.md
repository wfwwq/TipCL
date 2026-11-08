# GOAL — TipCL Linux 版立项（自主执行任务书）

> 本文档自包含，供 agent 团队执行。所有路径相对本文件所在目录 `D:\项目\tipcl\linux\`。
> Windows 版参考仓库（只读参照，不许改动）：`D:\项目\tipcl\windos\tipcl\`。

---

## 1. 使命

把 TipCL（AI 原生 Minecraft 启动器，现为 WPF / .NET Framework 4.8 单 exe）立一个 **Linux 版**工程。
完整工程预计分三个里程碑：

- **M1（本轮，工程骨架）**：可编译、可运行、有外壳的 Avalonia 空应用 + Core 业务代码复用层 + 测试/构建基建。
- **M2（后续）**：页面移植——启动页 / 实例库 / 下载 / 安装 / 插件 / 设置 + 页面布局自定义框架。
- **M3（后续）**：实机业务——启动 Minecraft、Java 管理、插件/AI 运行时在 Linux 落地、打包交付（self-contained 单文件 / AppImage）、双端测试。

**本轮只做 M1**，且要求 5 个子代理并行完成 M1 全部内容。

## 2. 技术决定（已定，agent 不重新讨论）

| 项 | 决定 |
|---|---|
| UI 框架 | Avalonia UI 11（C#，.NET 8），MVVM 不强制，允许 code-behind 风格贴近 Windows 版 |
| TFM | net8.0（App 与类库均 net8.0；Core 复用层如遇 API 缺口可 netstandard2.0） |
| 方案结构 | `TipCL.Linux.sln`；`src/TipCL.App/`（Avalonia 主工程）、`src/TipCL.Core/`（业务复用类库）、`tests/TipCL.Tests/`（xunit） |
| Core 复用 | 从 Windows 版 `src/TipCL/Core/` 与 `src/TipCL/Services/` 中挑选可跨平台文件**复制**进 `src/TipCL.Core/`（文件头注释注明来源路径），Windows 专属 API（注册表、WPF 类型、Win32）不搬；缺口用条件编译或桩实现并在 PROGRESS 记录 |
| 字体 | 不嵌字体；`FontFamily` 用系统回退（"Microsoft YaHei UI, Noto Sans CJK SC, WenQuanYi Micro Hei, sans-serif"） |
| 版本号 | 冻结 `0.1.0-m1`，不 tag、不发 release、不 push |
| 构建环境 | 本机 Windows + dotnet SDK 10.0.403，Avalonia 工程在本机编译与冒烟（Avalonia 可在 Windows 上跑）；Linux 真机验证推迟到 M3 |
| 设计基准 | 视觉沿用 Windows 版 v2 设计（`D:\项目\tipcl\windos\tipcl\docs\mockups-hark\v2\`）；M1 只落主题令牌与外壳，不做具体页面 |

## 3. 红线

1. 不改动 Windows 参考仓库的任何文件（只读）。
2. 不加 M2/M3 才需要的依赖；M1 允许的包仅限：Avalonia 全家桶（11.x）、Avalonia.Themes.Fluent、xunit、Microsoft.NET.Test.Sdk。其它包需在 DECISIONS 报备。
3. `dotnet build` 0 错误、`dotnet test` 全绿才算完成；每个子代理收尾必须自证编译/测试通过。
4. 不碰版本号、不 git push、不发 release。
5. 文件独占地盘（见 §5），子代理不许写别人地盘的文件；共享文件（sln、GOAL/PROGRESS/DECISIONS）的编辑权归指定归属。
6. 界面文案中文，与 Windows 版一致。
7. git：本目录 `git init`，每完成一块 commit 一次，提交信息注明工作包编号（如 `M1-A1`）。

## 4. M1 验收标准（主 agent 验收用）

1. `dotnet build TipCL.Linux.sln -c Release` 0 错误 0 警告级错误。
2. `dotnet test` 全绿。
3. `dotnet run --project src/TipCL.App` 能弹出一个 1180×740 的窗口：自定义标题栏（Logo + 5 页签占位 + 最小化/最大化/关闭矢量按钮）、左侧 196px 导航占位（6-7 分组项，可点击切右侧空占位页）、明暗两套主题可切换。
4. Core 复用层编译通过，附《复用清单》：哪些文件搬了、哪些没搬、为什么。
5. `--smoke` 参数：离屏渲染主窗口截图到 `smoke.png` 后退出（Avalonia Headless 或 RenderTargetBitmap 等价物）。
6. `tools/build.sh` + `tools/build.ps1` 一键构建脚本。
7. 文档齐：`PROGRESS.md`（工作包表）、`DECISIONS.md`、`交接必读.md`。

## 5. 工作包（5 个并行子代理，文件独占）

### M1-A1 · 工程脚手架（先行的地基）
- 独占：仓库根（git init、sln、根文档 GOAL/PROGRESS/DECISIONS/交接必读 的初版）、`src/TipCL.App/`（csproj、Program.cs、App.axaml(.cs)、Session/ arg 解析、`--smoke` 骨架）、`tools/`。
- 产出：解决方案能 `dotnet build`，空窗口可跑，sln 里**预登记** A2/A3/A4/A5 将创建的全部工程与目录占位说明。
- csproj 用默认 glob（新 .cs/.axaml 自动编入），后续代理不碰 csproj（A4 的 TipCL.Core csproj 与 A5 的测试 csproj 除外，各自新建）。

### M1-A2 · 设计令牌与主题库
- 独占：`src/TipCL.App/Themes/`（Palette.Light.axaml、Palette.Dark.axaml、Controls.axaml）+ `src/TipCL.App/Ui/`（UiHelpers，移植 Windows 版 `Services/Ui.cs` 的门面 API：Card/Pane/RowCard/Field/Section/Btn/Input/Combo/Check/Switch/Tag/Badge）。
- 令牌对齐 Windows 版：圆角统一 控件 8 / 卡片 12 / 窗口 8；字号阶梯 正文 13 / H3 14 / H2 17 / H1 21(页标题) / 24(罕见大标题)；间距收紧（页面 26→20 边距、组间距 14）。明暗两套成对，键名对照表写入 progress/M1-A2.md。
- 产出：一个 `ThemeGalleryPage`（临时样张页，列出全部令牌与控件样式），A5 冒烟截图会用到。

### M1-A3 · 窗口外壳与导航框架
- 独占：`src/TipCL.App/Shell/`（MainWindow.axaml(.cs)、TitleBar、WindowButtons、SideNav、NavRegistry）、`src/TipCL.App/Pages/Placeholder*.cs`。
- 规格：1180×740、标题栏 40px（Logo + 顶部 5 页签占位：启动/实例库/下载/插件/设置 + 主题切换钮 + 窗口按钮，Linux 下普通窗口带系统边框也可接受，自绘按钮照做）、侧边栏 196px 分组导航（通用组：仪表盘/账号/Java；游戏组：实例库/下载/任务；系统组：插件/设置/关于），点击切右侧 ContentControl 占位页。
- 依赖 A2 的主题键：允许 A3 先按 §4.3 规格写代码并自建临时样式，收尾时若 A2 键已就位则对齐。键名冲突由主 agent 裁决。

### M1-A4 · Core 业务复用层
- 独占：`src/TipCL.Core/`（csproj + 搬运的源文件）。
- 先做《复用清单》扫描：读 Windows 版 `src/TipCL/Core/`（全部）与 `src/TipCL/Services/`（挑非 UI 的），按依赖度排序搬运：Minecraft 版本清单/下载/安装逻辑、Java 探测、实例管理、账号存储、JSON 配置。Windows 专属（注册表、Win32、WPF 依赖）列出清单不搬。
- 搬运文件保持最小改动：只改命名空间与跨平台 API 缺口；用 `#if WINDOWS_DESKTOP` 之类条件编译隔离不可避免的双分支。附最小单测数据类逻辑（A5 测试工程里写不了的话，自建 `src/TipCL.Core.Tests` 内部小工程亦可，需报备）。

### M1-A5 · 测试与构建基建 + 冒烟
- 独占：`tests/TipCL.Tests/`（xunit：Core 单测 + 主题键存在性测试）、`tools/build.sh`、`tools/build.ps1`（A1 建骨架文件，A5 充实内容）、`docs/`（本仓库的 README：构建/运行/冒烟说明）。
- 产出：`dotnet test` 全绿；冒烟流程文档化（跑 `--smoke` 得 smoke.png 并人工核对窗口外观）。
- 依赖：A2/A3/A4 落地后才能真正全绿；A5 先按契约写测试与脚本，收尾时报告哪些测试因依赖未就位被跳过（`[Fact(Skip=...)]` 须注明）。

## 6. 协作纪律

1. 各代理只写自己独占地盘；需要别人地盘的接口时，在 progress/M1-*.md 里写清"依赖申请"，不许直接改。
2. 每个代理收尾必须：a) 自证 `dotnet build`（自己范围）0 错误；b) 写 `progress/M1-<包>.md`（做了什么、文件清单、遗留、风险）；c) git commit（信息前缀包编号）。
3. 共享文件编辑权：GOAL.md 只读；sln 归 A1；PROGRESS.md/DECISIONS.md 各包只**追加**自己小节（追加用 Edit 在文件末尾，禁止重写他人内容）。
4. 构建防锁：`dotnet build-server shutdown` 后再 build；遇 CS 文件锁报错先 `dotnet build-server shutdown` 重试。
