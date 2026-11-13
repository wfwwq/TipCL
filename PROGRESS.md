# TipCL macOS 版 — 进度

> 仓库根 = 本文件所在目录 `D:\项目\tipcl\mac\`。开工前先读 [GOAL.md](GOAL.md),拿不准的先查 [DECISIONS.md](DECISIONS.md)。
> 每个工作包做完在**末尾追加**一小节:做了什么 / 关键文件 / 验证命令与实际结果 / 遗留与风险。

## 路线图状态

| 包 | 内容 | 状态 |
|---|---|---|
| A1 | 脚手架 | ✅ 完成(见下) |
| A2 | Core 基座 | ✅ 完成(见下) |
| A3 | Core/Launcher 主体 | ✅ 完成(见下) |
| A4 | Ai + Sync | ✅ 完成(见下;**Ai 53/65 + Sync 4/4 参与编译;全工程 19 个文件待 A5 恢复编译**,见 D-18;这 19 个文件已在 A5 全部恢复) |
| A5 | Plugins + BackendApi + Bridge | ✅ 完成(见下;**19 个待恢复文件全部恢复,`TipCL.Core` 里 0 个被排除编译的文件**) |
| A6 | 测试移植 | ✅ 完成(见下;**搬来 29 个文件 → A 类 15 / B 类 4 / C 类 10;可跑用例 127 条,测试总数 523 → 652,两条通道都 652/652 全绿**) |
| B1a | mac 平台服务(Keychain / Skia 编解码 / 插件凭据 AES-GCM / 启动接线) | ✅ 完成(见下;**新增 114 条用例,总数 652 → 766,两条通道都 766/766 全绿**) |
| B1b | 运行时差异修复(SecretStore 读取路径 / 代码页 / 打开文件夹 / 软链接 / CA1416) | ✅ 完成(见下;**新增 45 条用例,总数 766 → 811,两条通道都 811/811 全绿**;CA1416 ×7 → 0) |
| B2a | mac Java 运行时获取(Mojang 平台键 / Adoptium·Zulu mac 包 / 可执行位与链接 / java 定位) | ✅ 完成(见下;**新增 90 条用例,总数 811 → 901,两条通道都 901/901 全绿**) |
| B2b | Natives classifier / 规则 / `-XstartOnFirstThread` / 启动参数 | ✅ 完成(见下;**新增 137 条用例,总数 901 → 1038,两条通道都 1038/1038 全绿**;三个复制文件只加可注入 OS/ARCH 的 internal 重载 + 一处内联逻辑抽取,Windows 行为用等价钉子逐项钉住;新建 `Launcher\MacNativeSupport.cs`,未接入调用点,留 Lead 裁决 arm64 方案) |
| B2c1 | Java 与安装的收尾:Mojang 安装循环建链顺序与已存在链接 / Microsoft 厂商分支 / `forge-legacy` 的 Java 8 / Preflight 磁盘空间守卫 / `GlobalMods.IsSymlink` | ✅ 完成(见下;**新增 72 条用例,总数 1405 → 1477,两条通道都 1477/1477 全绿**;`Java.cs` / `Preflight.cs` / `GlobalMods.cs` 各按最小面改动,新逻辑全部落在 `JavaRuntimeFiles.cs` / `JavaArch.cs`;第 2 项走了「改 mac 地址为 `macos` + `.tar.gz`」这一支,**依据是 6 个 `aka.ms` HEAD 请求的线上证据**,见小节取证表与 D-54) |
| B2c2 | 其余平台差异(一):NodeRuntime darwin / Terracotta 可执行权限 / adb 路径 / PhoneLink 临时目录 | ✅ 完成(见下;**新增 102 条用例,总数 1477 → 1579,两条通道都 1579/1579 全绿**;只改了任务书点名的 4 个复制文件;第 1 项 darwin 包名有 2 个 GET 的线上证据;第 4 项 PhoneLinkTests **仍排除** —— testhost 的 HttpListener 依旧抛「句柄无效」,往 %TEMP% 写文件那个原因已修好并实测) |
| B2c3 | 其余平台差异(二):Updater 仅提示 / Shortcut / 导出启动脚本(含 `BackendApi.cs:2958` 传未合并 JSON) | ✅ 完成(见下;**新增 101 条用例,总数 1579 → 1680,两条通道都 1680/1680 全绿**;只改了 4 个复制文件:Updater.cs / Shortcut.cs / VersionOps.cs / BackendApi.cs 仅 `export_launch_script` 两行;新建 `Launcher\PosixShell.cs`;`Updater.Check` 增加唯一被授权的增量键 `can_self_update`;mac 的 `.command` 改走 POSIX 单引号并补可执行位) |
| B2d | Apple 芯片旧版本:目标架构 + natives / 规则 / classpath 按目标架构(Lead 裁决见 B2b 的 Lead 验收) | ✅ 完成(见下;**新增 132 条用例,总数 1038 → 1170,两条通道都 1170/1170 全绿**;新建 `Launcher\LaunchArch.cs`,7 处调用点改读目标架构,5 个复制文件加可注入重载 / 抽取纯函数;Java 侧留给 B2e) |
| B2e | Apple 芯片旧版本:Java 按目标架构(Mojang 平台键 / 厂商安装 / 已装 Java 挑选 / Rosetta 提示) | ✅ 完成(见下;**新增 235 条用例,总数 1170 → 1405,两条通道都 1405/1405 全绿**;新建 `Launcher\JavaArch.cs`,`Java.cs` / `McLauncher.cs` / `Installer.cs` 各改一处;写测试时抓到并修掉一个「读取委托 / 探测委托」接错的真 bug,见 D-50;两处同类缺口只记录未改,见 D-52) |
| B3 | 打包 | ✅ 完成(见下;**新增 116 条用例,总数 1680 → 1796,两条通道都 1796/1796 全绿**;`tools\package.ps1` + 打包器实跑通过:443 条目、85,812,352 字节 tar.gz、两次运行 SHA256 完全一致;审草稿时改了两处 —— tar 从 Pax 换 Ustar(确定性)、暂存目录只删 `TipCL.app`) |
| C1a | 主题色板 + 图标字体 + Lucide 图标(Avalonia 移植) | ✅ 完成(见下) |
| C1b | `Themes/Controls.axaml` 移植 | ✅ 完成(见下;**新增 257 条用例,总数 1900 → 2157,两条通道都 2157/2157 全绿**;64 个 WPF 键全部有落点,40 个具名样式映射成类名;smoke 因隐式 TextBlock 字体变化为 3956/3941,**待 Lead 裁决基准**,见小节第 7/8 条) |
| C1c | `Services/Ui.cs` / `UiExt.cs` 门面 | ✅ 完成(见下;**新增 100 条用例,总数 2157 → 2257,两条通道各连跑 2 次都 2257/2257 全绿**;`Ui.S` 的返回类型被迫由 `Style` 改成类名、`Pw` 返回 `TextBox`,逐条列在差异表里;smoke 与裁决基准逐位一致) |
| C2a | 桥层与 WPF-free 基础服务(`AppServices`/`Payload`/`TaskStore`/`TaskRow`/`I18n`/`Fmt`/`FileKinds`/`BridgeEvent`/`BridgeCallException`/`BridgeHost`/`BridgeClient`/`Dlg`/`Motion`) | ✅ 完成(见下;**新增 210 条用例,总数 2257 → 2467,两条通道各连跑 2 次都 2467/2467 全绿**;`src\TipCL.Core` 一行未动;只有进程内一条后端路径,外部 Python 桥已去) |
| C2b | 外壳:`MainWindow` / 导航 / 页面宿主 / Toast·Dialog·DropVeil 宿主 / 系统菜单与快捷键 | ✅ 完成(见下;**新增 79 个测试方法 / 122 条用例,总数 2467 → 2593,两条通道各连跑 2 次都全绿**;C2a 的接线清单除 `ToastSink`(Toast 视觉件属 C2c)与"启动后端"两处外全部落地;smoke 因新增标题栏 / 侧栏 / 页面宿主而变,**新基准 = light 21823 `8336CA13...` / dark 18200 `DBD3C171...`**) |
| C2c1 | Toast 卡片 + 真实 `ToastSink` 接线 + 对话框卡片(`Dlg` 的 confirm/message 卡) | ✅ 完成(见下;**新增 60 条用例,总数 2631 → 2691,两条通道各连跑 2 次都 2691/2691 全绿**;`--smoke` 与 C2b 基准**逐位相同**;任务书点名的 `Pages\Bubble.cs` / `Pages\ConfirmCard.cs` 实为 AI 页组件、按红线未搬,见 D-104) |
| C2c2 | 页面级视觉服务:拖放(FileDrop / PageBase.EnableDrop / DropVeil)+ Wallpaper + AccountGate + Thumbs·ThumbTile + LoaderIcons(原「剩下的视觉件」里属本包的那半) | ✅ 完成(见下;**新增 106 条用例,总数 2691 → 2797,两条通道各连跑 2 次都全绿**;`--smoke` 与 C2b 基准**逐位相同**;任务书说"预设已内嵌在 Core"实测不成立,见 D-108) |
| C2c3 | 其余视觉件:Backdrop / SideFrost / SideFrostEx / ImeGuard / WebViewHost / WebpFallback / 标题栏圆片 / 下载任务底条 | 未开始 |
| C2d | 进程内后端接线与验收(可关的登录预热 / `BackendBootstrap` 状态机 / 真启停 / 真 RPC 往返 / `BridgeReady` 开闸) | ✅ 完成(见下;**新增 38 条用例,总数 2593 → 2631,两条通道都 2631/2631 全绿**;`src\TipCL.Core` 只动 `BackendApi.cs` 一个文件(+20/-4);**后端真的起在 `127.0.0.1:0` 上并跑通了真 RPC 与真 SSE**,`BridgeReady` 改由真启动路径打开;顺带修掉"取消启动会漏监听器"的真缺口;smoke 与 C2b 基准逐位相同) |
| C3-C8 | 页面逐个移植 | 进行中(C3c 起;**第一个真实页面 = 启动页**,接入方式:各页面包 `PageRegistry.Register(...)` 注册页面 id) |
| C3c | **启动页**:`Pages\LaunchPage.cs`(58.4 KB)+ `Pages\LaunchPage.Popups.cs`(9.5 KB)移植并注册为 `launch` | ✅ 完成(见下;**新增 117 条用例,C2b 的"空宿主"那条拆成两条(+1),总数 2968 → 3086,两条通道各连跑 2 次都 3086/3086 全绿**;`--smoke` 因为页面第一次真的有内容而合法变更为 **light 37681 `2603BF6A...` / dark 33579 `7D7AFDD5...`**;实测还差 **6 个 Models DTO** 与 **hero.jpg**(C2c2 漏的那张),按"小而自包含就一并搬"字节级搬来 —— **Lead 说"这些文件自包含"第三次不成立**,见 D-121;`MainWindow.FlyToTasks` 在 mac 侧不存在 → 注入缝 + 用户可见降级,见 D-122 请 Lead 裁决;页面用例抓到一个真 bug:`ContextMenu.Open()` 无参调用会抛 `ArgumentNullException`,见 D-124) |
| C3a | 区块 / 仪表盘布局框架:`Pages\BlockHost.cs` + `Pages\DashHost.cs`(含它们真正依赖的布局模型:`Pages\PageBlock.cs`、`PageLayoutStore.cs`、`PageLayoutIO.cs`、`DashCard.cs`、`DashLayout.cs`) | ✅ 完成(见下;**新增 100 条用例,总数 2797 → 2897,两条通道各连跑 2 次都 2897/2897 全绿**;`--smoke` 与 C2b 基准**逐位相同**;任务书说"这两个文件应该自包含"**实测不成立** —— 还差 5 个没移植的模型/存储文件,按"小而自包含就一并搬"的规定搬了,见 D-117) |
| C3d | **外壳补 `FlyToTasks` + `FlyHost`**:13 处页面调用点一次修好(含 C3c 注入缝的接线) | ✅ 完成(见下;**新增 33 个测试方法 / 43 条用例,总数 3086 → 3129,两条通道各连跑 2 次都 3129/3129 全绿**;`--smoke` 与 C3c 基准**逐位相同**(light 37681 `2603BF6A...` / dark 33579 `7D7AFDD5...`),离屏时第一道守卫就把飞行挡掉,冒烟帧里没有 chip;C3c 记的那条"用户可见降级"**已修好**(提交 `a9ad510` 外壳成员 + `b61db8a` 接线);顺带实测两条框架事实:`Avalonia.Controls.Control.IsLoaded` 在 11 里存在、**以 Transform 对象为 `Animation.RunAsync` 目标会抛 `InvalidCastException`**(后者是既有问题,不在本包范围,见小节第 8 节) |
| C3b | 启动页需要的两个小对话框:`Pages\CrashUi.cs`(崩溃 / 异常退出报告卡)+ `Pages\VersionSetupDialog.cs`(版本设置) | ✅ 完成(见下;**新增 71 条用例,总数 2897 → 2968,两条通道各连跑 2 次都 2968/2968 全绿**;`--smoke` 与 C2b 基准**逐位相同**;任务书的门面计数复核吻合,但"自包含"不成立 —— 还差两个没移植的小模型 `Models\OpResult.cs` / `Models\VersionSettingsDto.cs`,按"小而自包含就一并搬"字节级搬来,见 D-118;`AiFixWindow`(22.6 KB 整窗)属 AI 全家未移植,「交给 AI 修复」按钮保留 + 注入缝 + 如实提示,请 Lead 裁决,见 D-119) |
| C3e | **修 `Motion` 的变换动画**:19 处"把 Transform 当 `RunAsync` 目标"的调用点改成"目标给宿主控件" + 失败可观测 | ✅ 完成(见下;**新增 50 条用例,总数 3129 → 3179,两条通道各连跑 2 次都 3179/3179 全绿**;`--smoke` 与 C3c 基准**逐位相同**(light 37681 `2603BF6A...` / dark 33579 `7D7AFDD5...`);受影响的是 13 个方法(`FadeIn` / `HoverLift` / `Pulse` / `Shake` / `Shine` / `Spin` / `PopIn` / `PopOut` / `SlideIn` / `SlideOut` / `PageSwap` + 间接的 `Stagger` / `StaggerItems`),公开签名一个字节没动;顺带修掉同族的第二、三个坑(`Easing` 显式写 null 会静默失效、`IterationCount.Infinite` 配 `RunAsync` 必抛),见 D-132;**用户可见差异只有一条**:`Spin` 每圈交界有一帧级重启) |
| C4 | **实例管理簇**:`Pages\ModsDialog.cs` + `Pages\SavesDialog.cs` + `Pages\LibraryPage.cs` + `Pages\InstancePage.cs` + `Pages\InstanceDetailPage.cs` 移植并注册 `library` / `instance` | ✅ 完成(见下;**新增 173 条用例 / 162 个方法,总数 3179 → 3352,两条通道各连跑 2 次都 3352/3352 全绿**;首屏两帧与基准**逐位相同**(light 37681 `2603BF6A...` / dark 33579 `7D7AFDD5...`),**新帧基准** = `library` light 42417 `905125C4...` / dark 39268 `FA926F03...`、`instance` light 27484 `92C06E15...` / dark 24511 `F1582051...`;依赖审计**第四次**推翻任务书的"自包含"(还差 7 个 Models DTO + 一个 1956 B 的小对话框,见 D-133);`InstanceDetailPage` 的「设置」分段走注入缝 + 如实占位(**用户可见降级**,见 D-134);按 D-126 还原了 C3c 的一条时代前提(断言只增不减);**两处用户可见的窄窗观感问题请 Lead 裁决**(`instance` 的 Section 标题被压成 0 宽、`library` 两栏在离屏帧里左偏约 44px),见小节第 5 节) |
| D1-D2 | 冒烟矩阵 / 文档收尾 | 未开始 |

## 仓库现状(截至 A1)

- git 仓库已在 `D:\项目\tipcl\mac\` 初始化,identity 只设在本仓库:
  `user.name=tipcl-mac agent`、`user.email=tipcl-mac@local.invalid`(无 remote,不 push、不打 tag)。
- 目录:`src/TipCL.App`(Avalonia 主工程)、`src/TipCL.Core`(类库)、`tests/TipCL.Tests`(xunit)、
  `tools/build.ps1`、`Directory.Build.props`、`.gitignore`。

---

## A1 脚手架

### 做了什么

1. **git init** + 仓库内 identity(`git config --local`)。
2. **[TipCL.Mac.sln](TipCL.Mac.sln)** 含三个工程:
   - **[src/TipCL.App](src/TipCL.App/TipCL.App.csproj)**:Avalonia **11.3.2** + `net8.0`,`OutputType=WinExe`,
     `AssemblyName=TipCL`,`RootNamespace=TipCL`;UI 全用 C# 代码构建,**没有任何 .axaml**。
   - **[src/TipCL.Core](src/TipCL.Core/TipCL.Core.csproj)**:`net8.0` 类库,`RootNamespace=TipCL.Core`;
     本包只放占位类型 [CoreMarker](src/TipCL.Core/CoreMarker.cs),真代码 A2 才搬。
   - **[tests/TipCL.Tests](tests/TipCL.Tests/TipCL.Tests.csproj)**:xunit 2.9.2 + Microsoft.NET.Test.Sdk 17.11.1,
     引用 TipCL.Core;有真实断言的测试 [CoreMarkerTests](tests/TipCL.Tests/CoreMarkerTests.cs)。
   - 三工程公共属性全在 **[Directory.Build.props](Directory.Build.props)**:`LangVersion=12`、`Nullable=annotations`、
     `AllowUnsafeBlocks=true`、`ImplicitUsings=disable`;`NoWarn` 按任务书**留空**(A2 再补)。
3. **空主窗口** [MainWindow.cs](src/TipCL.App/MainWindow.cs):标题 `TipCL`、900×550、最小 720×480。
   本包不做标题栏 / 导航 / 主题;只有一对占位配色,跟着 `RequestedThemeVariant` 走(见 DECISIONS.md D-04)。
4. **`--smoke` 离屏截图**:[Program.cs](src/TipCL.App/Program.cs) + [Smoke/SmokeOptions.cs](src/TipCL.App/Smoke/SmokeOptions.cs)
   + [Smoke/SmokeRunner.cs](src/TipCL.App/Smoke/SmokeRunner.cs)。
   `Avalonia.Headless`(`UseHeadlessDrawing = false`)+ `UseSkia()` 真实渲染主窗口 → PNG → 退出。
   退出码 **0** 成功 / **1** 渲染或写盘失败(stderr 带原因)/ **2** 命令行参数错。
   `--theme` 映射到 `RequestedThemeVariant`(Light/Dark);**`--page` 本包只解析并保存,不做页面切换**(页面 C 阶段才有)。
   不带 `--smoke` 时走正常桌面启动路径。
5. **[tools/build.ps1](tools/build.ps1)**:先 `dotnet build-server shutdown`,再按 GOAL §4 那串参数构建
   (`-p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"`);`-Test` 开关会在构建后跑 `dotnet test`。
6. **[.gitignore](.gitignore)**、本文件、[DECISIONS.md](DECISIONS.md)。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build-server shutdown` | 退出码 **1**:本机没有在跑的 VBCSCompiler(「未能关闭 VB/C# 编译器服务器」)。这是防锁预备动作,不影响构建(见 D-06) |
| 2 | `dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **0 错误 / 8 警告**,退出码 0。8 个警告**全是 NU1900**(离线取不到源的服务索引,做不了漏洞审计),没有一条编译器警告 |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` | **失败(退出码 1)**:testhost 未处理异常 `System.ComponentModel.Win32Exception (5): 拒绝访问`(栈顶 `DefaultEngineInvoker.SetParentProcessExitCallback`)→「测试运行已中止」;另一次执行直接**卡死在同一位置**(日志停在「总共 1 个测试文件与指定模式相匹配」)。根因是沙箱限制,**不是工程配置**(见 D-05),原始日志留在 `artifacts/test-diag.log` |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,**5 个用例全过/0 失败**(3 个 `[Fact]` + 1 个 `[Theory]` 展开成 2 个)。执行器见 D-05 |
| 5 | `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts/smoke-light.png"` | 退出码 **0**,`smoke-light.png` 存在,**3739 字节**,SHA256 `17F3CD87CE2C552B965D38A85DA53E05D137543B11D13D73157BF5D7F27017BF` |
| 6 | `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts/smoke-dark.png"` | 退出码 **0**,`smoke-dark.png` 存在,**3751 字节**,SHA256 `374716FF4D41365CDD182DF588CC129627035069A905007F0C6AE43F9ED38144`;**与 light 不同** |
| 7 | 像素抽查(System.Drawing 采样两张 PNG) | 两张都是 **900×550**;浅色底 `#F7F7F8`、深色底 `#1B1B1F`,中心为文字色 → 明暗确实不同,不是同图换名 |
| 8 | `--smoke --page home --theme dark --out ...` | 退出码 **0**,日志 `page=home theme=dark`,参数被正确解析保存(**未做切页**) |
| 9 | `--smoke --theme blue` / `--smoke --out`(缺值)/ `--smoke --bogus x` | 三种都退出码 **2**,stderr 打中文原因 + 用法(控制台里中文显示乱码是代码页问题,与文件无关) |
| 10 | `--smoke --theme light --out artifacts/nope/deeper/x.png` | 退出码 **0**,目录被自动创建,PNG 写出 |
| 11 | `start "" TipCL.exe` → 等 7 秒 → `Get-Process` | 进程**仍活着**(桌面启动路径正常,窗口/消息循环没崩),随后 `Stop-Process` 收掉,无残留 |
| 12 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1` | 退出码 **0**,输出 `[build] OK`、0 错误 |

> `TipCL.exe` 是 GUI 子系统,Windows PowerShell 里 `& .\TipCL.exe ...` **不会等待**(拿不到退出码),
> 所以上面用 `cmd /c` 取真实退出码;`Start-Process -NoNewWindow` 在本沙箱直接 Access is denied(见 D-09)。

### 遗留与风险

- **`dotnet test` 在本机沙箱不可用**(D-05):VSTest 的 testhost 启动时必须拿父进程句柄,而沙箱禁止跨进程句柄。
  A1 因此在 `tests/TipCL.Tests` 里加了进程内执行器(`OutputType=Exe` + [Program.cs](tests/TipCL.Tests/Program.cs))。
  **A6 搬测试、以后任何包写测试都要注意这点**;`dotnet test` 的绿灯只能在正常机器上拿到。
- **Avalonia 是 11.3.2**:本机离线,NuGet 缓存里最高的 11.x 稳定版(线上最新 11.3.23)。等能联网时升级并重跑收尾(D-01)。
- 8 条 NU1900 警告会一直在;故意没关 `NuGetAudit`,保留漏洞审计信号。
- `--page` 只解析不切页,C 阶段接页面后要改成真正切页,并补「每页明暗两帧」的冒烟矩阵。
- 空窗口的占位配色是临时的,C1 建 `Palette.Light/Dark` 时删掉(D-04)。
- 未做:标题栏(46px / 红绿灯留白)、导航、主题、图标字体、系统菜单(C1/C2);mac 打包(B3)。
- 进程内执行器不支持 xunit 的 `IClassFixture` / `ICollectionFixture` / 自定义 `TestFramework`;
  遇到不认识的用例形态会记 `UNSUPPORTED` 并返回非 0(A6 需要时再扩)。
- 本机是 Windows:`--smoke` 只验证了 Windows 上的 Avalonia 渲染,**macOS 真机渲染未验证**。

### Lead 验收(2026-11-12 05:52,在沙箱外复跑)

结论:**A1 通过**。以下命令由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行(High 完整性级别,无 DSH 沙箱限制)。

| 命令 | 结果 |
|---|---|
| `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q` | 退出码 0,0 错误 / 8 警告(全是 NU1900) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,通过 5 / 失败 0 / 跳过 0 |
| `TipCL.exe --smoke --theme light` / `--theme dark` | 两次退出码 0;3739 字节 / 3751 字节,SHA256 与上表第 5、6 行逐位一致 |
| `TipCL.exe --smoke --theme blue` | 退出码 2 |
| `git status --short`、`git remote -v`、`git tag`、全局 identity | 工作树干净;无 remote、无 tag;全局 identity 未被设置 |

对后续包有影响的两条环境事实:

- **`dotnet test` 在沙箱外是通的**。D-05 描述的 `Win32Exception (5)` 只发生在 Low 完整性级别的沙箱进程里。
  后续每个包先真跑 `dotnet test`;只有再次撞到同一个异常时才退回进程内执行器,并在回报里写明用的是哪条通道。
- **NuGet 在沙箱外能连**(`https://api.nuget.org/v3/index.json` 返回 200)。D-01 的「只能用缓存里的 11.3.2」
  是当时沙箱断网造成的,Avalonia 升级到最新 11.x 稳定版这条遗留并入 A2 处理。

---

## A2 Core 基座

### 做了什么

1. **A1 遗留(Avalonia 版本)**:`https://api.nuget.org/v3-flatcontainer/avalonia/index.json` 返回 **200**,
   索引里最高的 11.x 稳定版是 **11.3.23**;`Directory.Build.props` 的 `TipCLAvaloniaVersion` 抬到 11.3.23,
   `dotnet restore` 退出码 0(见 DECISIONS.md D-10,作废 D-01 的版本结论)。
2. **字节级复制**(全部 `Copy-Item -LiteralPath`,没有「读出来再写回去」):**53 个文件**
   - `<WIN>\Core\*.cs` 顶层 **33 个** → [src/TipCL.Core](src/TipCL.Core/)(不递归,`Launcher/`、`Plugins/`、`Platform/` 子目录不碰);
   - `<WIN>\Core\Platform\` 的 **5 个接口文件** → [src/TipCL.Core/Platform](src/TipCL.Core/Platform/)
     (`WinCredSecretStore.cs` 不搬,留给 B1;`Platform\Wpf\` 不搬);
   - `<WIN>\Resources\` 的 **locales 2 个 + ai 1 个 + plugin-host 12 个** → [src/TipCL.Core/Resources](src/TipCL.Core/Resources/)
     (`fonts/`、`wallpapers/` 不搬,留给 C1)。
   - 复制前先加了 [.gitattributes](.gitattributes)(一行 `* -text`),禁止 git 做换行符转换。
3. **工程配置**:[Directory.Build.props](Directory.Build.props) 补 `NoWarn=$(NoWarn);CS0168;CS0219;CS0162;CS0649;CS0414;CS8981`
   (与 Windows csproj 逐项一致);[TipCL.Core.csproj](src/TipCL.Core/TipCL.Core.csproj) 按 Windows csproj 的
   **LogicalName 原样**嵌入 4 条 `EmbeddedResource`(资源进 `TipCL.Core` 程序集),并用
   `<None Remove="Resources\**\*" />` 摘掉 SDK 默认 None glob,避免同一文件既是 None 又是 EmbeddedResource。
4. **排除编译 6 个文件**(裁决 A,见 DECISIONS.md D-11 的表格):文件**原样留在磁盘并入库**,
   只在 csproj 里 `<Compile Remove>`,注释写明恢复包。没有写桩类型、没有空命名空间。
   `RpcDispatcher.cs` 没有 Launcher 引用,留在编译里。
5. **裁决 B 未触发**:除上述 6 个文件外,其余 27 个 Core 顶层 .cs + 5 个 Platform 接口在 `net8.0` 上
   **一处改动都不用**,0 编译错误 0 编译警告。**对复制文件的改动数 = 0**,所以下面「对复制文件的每处改动」是空的。
6. **测试**:删掉 A1 占位 [CoreMarker.cs](src/TipCL.Core/CoreMarker.cs) / [CoreMarkerTests.cs](tests/TipCL.Tests/CoreMarkerTests.cs)
   (Lead 授权的替换),`tests/TipCL.Tests/Program.cs` 进程内执行器保留;新增 4 个测试文件、**60 个用例**:
   - [EmbeddedResourcesTests.cs](tests/TipCL.Tests/EmbeddedResourcesTests.cs):清单恰好等于 15 个点名资源(3 个用例);
   - [EmbeddedResourceContentTests.cs](tests/TipCL.Tests/EmbeddedResourceContentTests.cs):15 个资源逐个比 SHA256(期望值来自
     `Get-FileHash`)+ zh_CN.json 的 UTF-8 / BOM / 中文内容检查;
   - [PyJsonTests.cs](tests/TipCL.Tests/PyJsonTests.cs):解析出的 CLR 类型、BigInteger、NaN/Infinity、分隔符、缩进、
     sortKeys、转义(Dumps 与 DumpsU)、TypeError/JSONDecodeError 消息与位置、嵌套中文往返(22 个用例);
   - [PyDictTests.cs](tests/TipCL.Tests/PyDictTests.cs):插入序键、Ordinal 比较、缺键为 null、Req 抛 KeyError、
     Remove/Pop/SetDefault/Update/Copy/Clear、枚举快照、`ToString` 走 `Py.Repr`(17 个用例)。

### 关键文件

| 文件 | 说明 |
|---|---|
| [.gitattributes](.gitattributes) | `* -text`,保证复制内容与源逐字节一致 |
| [Directory.Build.props](Directory.Build.props) | Avalonia 11.3.23 + NoWarn 6 项 |
| [src/TipCL.Core/TipCL.Core.csproj](src/TipCL.Core/TipCL.Core.csproj) | 4 条 EmbeddedResource + 6 条 Compile Remove |
| [artifacts/verify-a2-bytes.ps1](artifacts/verify-a2-bytes.ps1) | 一次性字节一致性自检(**不入库**,`artifacts/` 已被 .gitignore) |

### 被排除编译的文件及恢复包

| 文件 | 恢复包 | 缺什么 |
|---|---|---|
| [BackendApi.cs](src/TipCL.Core/BackendApi.cs) | A5 | Launcher 的 `Config`/`LauncherConfig`/`AccountManager`/`Instance`/`DownloadManager`/`AgentResult`/`Utils` |
| [BackendWorker.cs](src/TipCL.Core/BackendWorker.cs) | A5 | 后端整体;本身只有一个未使用的 `using TipCL.Core.Launcher;` |
| [BridgeServer.cs](src/TipCL.Core/BridgeServer.cs) | A3 | `Launcher.Utils.DecodeUtf8Strict` |
| [CoreResources.cs](src/TipCL.Core/CoreResources.cs) | A3 | `Launcher.Utils.DecodeUtf8Strict` |
| [PPath.cs](src/TipCL.Core/PPath.cs) | A3 | `Launcher.Utils.ROOT` |
| [TaskContext.cs](src/TipCL.Core/TaskContext.cs) | A5 | 后端类型 `BackendWorker`(字段 / 构造 / `Current`) |

### 对复制文件的每处改动

**无。** 53 个复制文件全部与 Windows 源文件逐字节一致(SHA256 全等),裁决 B 的跨平台修补一次都没用上。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 0 | `Invoke-WebRequest .../avalonia/index.json` | HTTP **200**,最高 11.x 稳定版 **11.3.23** |
| 1 | `dotnet restore TipCL.Mac.sln` | 退出码 **0**(拉了 11.3.23) |
| 2 | `dotnet build-server shutdown` | 退出码 1(本机没有在跑的 VBCSCompiler,见 D-06),随后继续 |
| 3 | `dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **0 错误 / 4 警告**,退出码 **0**。4 条**全是 NU1900**(漏洞审计源不可达),**编译器警告 0 条**(CS\* = 0) |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,**60 个用例全过 / 0 失败 / 0 跳过**(通道 = 真 `dotnet test`,本环境没有出现 D-05 的 `Win32Exception (5)`) |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,`总计 60 个用例,通过 60,失败 0,跳过 0,不支持 0`(备用通道也绿) |
| 6 | `powershell -ExecutionPolicy Bypass -File artifacts\verify-a2-bytes.ps1` | `total_copied=53` / `byte_identical=53` / `changed_count=0` / `missing=0`;有改动的文件清单为空 |
| 7 | `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**,**3739 字节**(与 A1 / Lead 验收逐位一致) |
| 8 | `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts\smoke-dark.png"` | 退出码 **0**,**3751 字节**(与 A1 / Lead 验收逐位一致) |
| 9 | `git status --short` | 无 `bin\`、`obj\`、`artifacts\`、PNG 入库 |

### 遗留与风险

- **`CoreResources` 要到 A3 才能编译**,所以本包的资源清单断言用的是 `typeof(PyJson).Assembly`;
  A3 恢复后可补一条走 `CoreResources.ReadText` / `ListDir` 的用例(顺带验证 `ListDir` 的过滤规则)。
- **`Resources\**\*` 已经从默认 None glob 摘掉**:以后往 `src/TipCL.Core/Resources/` 加非嵌入文件
  (例如需要复制到输出目录的东西)要显式声明,否则不会被收进项目。
- **嵌入资源的 `RecursiveDir` 目前是空的**(plugin-host 无子目录)。将来加子目录会改变资源名,
  15 个名字的断言要同步改。
- **`dotnet test` 只在本次运行环境通过**:D-05 的沙箱限制仍然存在,换到沙箱内跑依旧会挂,届时退回进程内执行器。
- **本机是 Windows**:`--smoke` 只验证 Windows 上的 Avalonia 渲染(macOS 真机渲染未验证);
  Avalonia 11.3.23 只在 Windows 上编译运行过。
- 未做(不属于 A2):`Core/Launcher`(A3)、`Ai`/`Sync`(A4)、`Plugins`/`BackendApi`/Bridge 接线(A5)、
  测试移植(A6)、platform 实现(`WinCredSecretStore` → B1)。

### Lead 验收(2026-11-12 06:40)

结论:**A2 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行,哈希比对用的是 Lead 自己写的脚本,不是 `verify-a2-bytes.ps1`。

| 检查 | 结果 |
|---|---|
| 53 个复制文件对 Windows 源比 SHA256(工作树) | 53 / 53 一致 |
| 同一批文件比 git blob(`git hash-object --no-filters` 对 `HEAD:` 内容) | 53 / 53 一致,`* -text` 生效 |
| 不该搬的文件(`WinCredSecretStore`、`Wpf\`、`fonts\`、`wallpapers\`、`Launcher\`、`Plugins\`) | 均未入库 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误,警告只有 NU1900 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 60 / 失败 0 / 跳过 0 |
| `--smoke` light / dark | 退出码 0 / 0,字节数与 SHA256 和 A1 相同 |
| Avalonia 实际解析版本(`project.assets.json`) | 11.3.23 |
| 测试里的 3 个资源哈希常量(en / zh_CN / model_rules) | 与 Lead 对 Windows 源独立算出的值一致 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍是 `803dd38`,未提交改动数未变 |

给 A3 的更正与事实(Lead 实测):

- 上表把 `BackendWorker.cs`、`TaskContext.cs` 的恢复包写成 A5,**改为 A3 先尝试恢复**:`BackendWorker` 只用到 Launcher 的 `I18nCore`,
  `TaskContext` 只依赖 `BackendWorker`,而 `Launcher\SkinServer.cs` 要用 `BridgeServer` 的静态方法。只有 `BackendApi.cs` 留到 A5。
- `Launcher` 顶层共 108 个 `.cs`(883 KB),子目录只有 `Ai`(65 个)和 `Sync`(4 个)。
- 顶层只有 3 个文件引用 `TipCL.Core.Launcher.Ai`:`LauncherConfig.cs` 用 `Defaults` 的三个常量,`ModUpdate.cs`、`Preflight.cs` 用 `Conflict.InspectJar`。
  对应的 `Ai\Defaults.cs`(2 KB)和 `Ai\Conflict.cs`(15 KB)允许 A3 提前搬。

---

## A3 Core/Launcher 主体

### 做了什么

1. **字节级复制**(全部 `Copy-Item -LiteralPath`,没有「读出来再写回去」):
   - `<WIN>\Core\Launcher\*.cs` 顶层 **108 个** → [src/TipCL.Core/Launcher](src/TipCL.Core/Launcher/)(不递归,`Ai\`、`Sync\` 只按下面第 3 条搬一个文件);
   - `<WIN>\Core\Launcher\Ai\Defaults.cs` **1 个** → [src/TipCL.Core/Launcher/Ai/Defaults.cs](src/TipCL.Core/Launcher/Ai/Defaults.cs)。
   - 合计 **109 个文件**,全部与 Windows 源逐字节一致(SHA256 全等,见下)。
2. **恢复 A2 排除的文件**:`BackendWorker.cs`、`TaskContext.cs`、`CoreResources.cs`、`PPath.cs` **4 个成功恢复编译**;
   `BridgeServer.cs` **恢复失败**(缺 A5 的类型 `BackendApi`)→ 保留 `Compile Remove`,注释写明缺的具体类型。
   `BackendApi.cs` 继续排除(A5)。
3. **提前搬的 Ai 文件:A3 只搬了 `Ai\Defaults.cs`**。`Ai\Conflict.cs` 实测依赖 `Ai\TomlLite` + `Ai\Agent.DGet`,
   而 `Agent.cs` 牵出 **37 个** Ai 文件,超过「每个文件最多再搬 3 个」的上限 → 按回退方案,排除它的两个调用方
   `ModUpdate.cs`、`Preflight.cs`。**A4 整目录复制时注意 `Ai\Defaults.cs` 已经在了**(见 DECISIONS.md D-13)。
4. **修到编译通过**:0 错误、**对复制文件的改动数 = 0**(109/109 SHA256 全等)。没有写桩类型、没有空实现、
   没有删改任何被复制文件的一行逻辑;`NoWarn` 保持原有 6 项,没有扩大。

### 仍被排除编译的文件及恢复包

| 文件 | 恢复包 | 缺什么(实测) |
|---|---|---|
| [src/TipCL.Core/BackendApi.cs](src/TipCL.Core/BackendApi.cs) | A5 | 后端主体;依赖 Launcher 的 `Config`/`LauncherConfig`/`AccountManager`/`Instance`/`DownloadManager`/`Utils` |
| [src/TipCL.Core/BridgeServer.cs](src/TipCL.Core/BridgeServer.cs) | A5 | `BackendApi`(88 行属性 `Api`、102 行构造参数) |
| [src/TipCL.Core/Launcher/SkinServer.cs](src/TipCL.Core/Launcher/SkinServer.cs) | A5 | 连带:调 `BridgeServer.Unquote/SplitTarget/Phrase/ReadLine`(252/253/285/329/392/426/442) |
| [src/TipCL.Core/Launcher/McLauncher.cs](src/TipCL.Core/Launcher/McLauncher.cs) | A5 | 连带:40 行调 `SkinServer.ApiFor` |
| [src/TipCL.Core/Launcher/GameProcess.cs](src/TipCL.Core/Launcher/GameProcess.cs) | A5 | 连带:43/48 行调 `McLauncher._set_priority` / `WatchWindowTitle` |
| [src/TipCL.Core/Launcher/ModUpdate.cs](src/TipCL.Core/Launcher/ModUpdate.cs) | A4 | `Ai.Conflict.InspectJar`(Conflict → Agent → 37 个 Ai 文件) |
| [src/TipCL.Core/Launcher/Preflight.cs](src/TipCL.Core/Launcher/Preflight.cs) | A4 | 同上 |

被排除的 7 个文件都**原样留在磁盘并入库**,只在 [TipCL.Core.csproj](src/TipCL.Core/TipCL.Core.csproj) 里 `Compile Remove`。
A3 实际参与编译的 Launcher 顶层文件 = 108 − 5 = **103 个**(另加 `Ai\Defaults.cs`)。

> **A4 更正**:上表里 `ModUpdate.cs`、`Preflight.cs` 的恢复包是 **A5**,不是 A4 ——
> A4 实测它们的依赖 `Ai\Conflict.cs` 进不了编译(缺 A5 的 `Ai.Agent`),见 [DECISIONS.md](DECISIONS.md) D-18。

### 对复制文件的每处改动

**无。** 109 个复制文件全部与 Windows 源逐字节一致(SHA256 全等,`changed_count=0`),
本包**一处 edit 都没对被复制的文件做过**(唯一的 edit 是 `TipCL.Core.csproj`,它不是复制来的文件)。

### 运行时差异清点(只记录,不修)

grep 范围 = `src\TipCL.Core\`(含 Launcher、Ai、Platform;`Resources\` 是数据不算)。**本包不修,除非直接导致编译失败 —— 没有一处导致编译失败。**

| # | 位置 | 差异 | 建议包 |
|---|---|---|---|
| E1 | [Utils.cs:291](src/TipCL.Core/Launcher/Utils.cs#L291) | `Encoding.GetEncoding(437)`,net8 默认没有 437 代码页 → catch 静默 `_cp437 = Encoding.UTF8`;ZIP 条目名里的 0x80-0xFF 字节从此按 UTF-8 解,可能乱码 | B1 |
| E2 | [Java.cs:240](src/TipCL.Core/Launcher/Java.cs#L240) | `Gbk()` = `Encoding.GetEncoding(936)`,net8 无 GBK → catch 回落 UTF-8;Windows 控制台输出解码会变 | B2 |
| E3 | [Crash.cs:838](src/TipCL.Core/Launcher/Crash.cs#L838) | `Strict(cp)` 用 `Encoding.GetEncoding(cp, Exception, Exception)`,调用点 cp ∈ {54936(GB18030), 936}([Crash.cs:863](src/TipCL.Core/Launcher/Crash.cs#L863));两个都在 try 内,都失败 → 回落 UTF-8。崩溃日志若是 GBK 会解成乱码 | B1/B2 |
| E4 | [NetResponse.cs:187](src/TipCL.Core/Launcher/NetResponse.cs#L187) / [213](src/TipCL.Core/Launcher/NetResponse.cs#L213) | 用 HTTP `Content-Type; charset=` 的**名字**取编码,`gbk`/`gb2312` 在 net8 会抛 → catch 回落 UTF-8(编码名不认识就静默变 UTF-8) | B2 |
| E5 | [Terracotta.cs:639](src/TipCL.Core/Launcher/Terracotta.cs#L639) | `GetEncoding(936, ReplacementFallback...)` → catch 后走 ASCII + 非 ASCII 换 `?` 的兜底,中文提示会丢字 | B2 |
| P1 | `Process.Start`/`ProcessStartInfo` | **没有一处漏写 `UseShellExecute`**:11 处内联 `ProcessStartInfo` 全部显式赋值(Auth.cs:315=true、Crash.cs:1914=true/1921=false、Java.cs:153、Shortcut.cs:145、Sysinfo.cs:67、Terracotta.cs:563/606/1189、Updater.cs:144、Utils.cs:1421=false);其余 4 处(GameProcess.cs:31、LaunchFlow.cs:270、Optifine.cs:173、Utils.cs:1458)都走 `Utils.MakeStartInfo`(Utils.cs:1416,内部 false)。net48 默认 true / net8.0 默认 false 的差异被显式覆盖 | — |
| L1 | [GameDirDetector.cs:82](src/TipCL.Core/Launcher/GameDirDetector.cs#L82) | `AppDomain.CurrentDomain.BaseDirectory`:net8 仍可用,但 mac 打包后要确认它指向 `.app/Contents/MacOS` 还是别处 | B1 |
| L2 | [Guard.cs:77](src/TipCL.Core/Launcher/Guard.cs#L77) | `AppDomain.CurrentDomain.UnhandledException`:net8 仍可用(无差异) | — |
| L3 | [Net.cs:69-79](src/TipCL.Core/Launcher/Net.cs#L69-L79) | `ServicePointManager.SecurityProtocol / DefaultConnectionLimit / Expect100Continue`:net8 里这些**只影响已废弃的 HttpWebRequest 栈,对 `HttpClient`(SocketsHttpHandler)不生效** → TLS 版本/连接数策略需要挪到 `SocketsHttpHandler` 才有意义 | B1/B2 |
| L4 | — | `Thread.Abort` / `BinaryFormatter` / `WebRequest` / `WebClient`:**0 处**(所以没有 SYSLIB0014 之类的警告) | — |
| D1 | [Auth.cs:85/103](src/TipCL.Core/Launcher/Auth.cs#L85) | `CryptProtectData`/`CryptUnprotectData`/`LocalFree`(kernel32/crypt32)的**函数体内没有 IS_WINDOWS**,但唯一调用链 `_dpapi_protect`/`_dpapi_unprotect`(112/117)只在 `Auth.cs:221 if (Utils.IS_WINDOWS)` 之下被调用,且 `DpapiCall` 自己 catch 了 `DllNotFoundException`/`EntryPointNotFoundException` → mac 上运行时不会执行到 | — |
| D2 | [GlobalMods.cs:37](src/TipCL.Core/Launcher/GlobalMods.cs#L37) | **真正无守卫**:`CreateSymbolicLinkW`(kernel32)在 `_link_file` 里,全文件没有任何 `IS_WINDOWS` 判断。mac 上第一次调用抛 `DllNotFoundException` → 被 [43 行 catch(Exception)](src/TipCL.Core/Launcher/GlobalMods.cs#L43) 兜住 → 退化成 `File.Copy`(47 行)。**行为差异:mac 上不再建软链而是复制文件**,需要 mac 分支(`ln -s`) | B1 |
| D3 | [Preflight.cs:55](src/TipCL.Core/Launcher/Preflight.cs#L55) | **真正无守卫**:`GetDiskFreeSpaceEx`(kernel32)在 `FreeBytes` 里(同文件 185 行才有 `Utils.IS_WINDOWS`)。mac 上会抛 `DllNotFoundException`,要看它的 catch 是否兜得住 → mac 应改用 `DriveInfo` | B2 |
| D4 | 其余 29 个 extern 声明的 32 个调用点 | **都在守卫内**:`McLauncher.cs:300/377`、`VersionSettings.cs:242/267`、`Sysinfo.cs:151`、`Utils.cs:380/…`、`Terracotta.cs:…` 等。另外 [Sysinfo.cs:215](src/TipCL.Core/Launcher/Sysinfo.cs#L215) 已经是 `[DllImport("libc")] gnu_get_libc_version`(非 Windows 分支,343 行),mac 上 "libc" 能否解析到 libSystem 需要实测 | B2 |
| R1 | [Sysinfo.cs:157/162](src/TipCL.Core/Launcher/Sysinfo.cs#L157) | `Registry.LocalMachine.OpenSubKey` / `GetValue`:**第 151 行有 `if (!Utils.IS_WINDOWS) return dflt;` 守卫**,mac 上运行时安全。但编译器仍报 3 条 `CA1416`(分析器不认识自定义的 `Utils.IS_WINDOWS`)→ 建议 B1 改用 `OperatingSystem.IsWindows()` 让分析器认账,**不扩大 NoWarn** | B1 |

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build-server shutdown` | 退出码 **0** |
| 2 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **0 错误 / 7 警告**,退出码 **0**。分类:**NU1900 = 4**(漏洞审计源不可达,与 A1/A2 同因)、**CA1416 = 3**(全是 Sysinfo.cs 的 Registry,运行时有守卫,见 R1)、**编译器 CS\* = 0 条** |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,**131 个用例全过 / 0 失败 / 0 跳过**,耗时 1.1 秒。通道 = **真 `dotnet test`**,本环境没有出现 D-05 的 `Win32Exception (5)`(见 D-15) |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,`总计 131 个用例,通过 131,失败 0,跳过 0,不支持 0`(备用通道也绿) |
| 5 | `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**,**3739 字节**(与 A1/A2/Lead 验收逐位一致) |
| 6 | `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts\smoke-dark.png"` | 退出码 **0**,**3751 字节**(逐位一致) |
| 7 | `powershell -NoProfile -ExecutionPolicy Bypass -File artifacts\verify-a3-bytes.ps1` | `win_top_level_cs=108` / `mac_total_checked=109` / `byte_identical=109` / `changed_count=0` / `missing_count=0`;有改动的文件清单为空 |
| 8 | `%LOCALAPPDATA%\TipCL` 元信息前后对比(只列目录元信息,没读文件内容) | 前后**完全一致**:`LastWriteTime = 2026-11-09T22:01:49.9009329+08:00`,顶层条目数 **19**,顶层名字集合相同 → **本次工作没有改动用户的真实数据目录** |
| 9 | `git status --short` | 只有 `src/TipCL.Core/TipCL.Core.csproj`(改)+ `src/TipCL.Core/Launcher/`(109 个新文件)+ 3 个新测试文件;无 `bin\`、`obj\`、`artifacts\`、PNG、`test-home` 入库 |

### 测试(新增 71 个用例,总数 60 → 131;按执行器统计:A2 的 4 个文件 60 个 + 本包 3 个文件 71 个)

| 文件 | 内容 | 本包新用例数 |
|---|---|---|
| [TestHomeIsolation.cs](tests/TipCL.Tests/TestHomeIsolation.cs) | **数据目录隔离**(红线):`[ModuleInitializer]` 创建 `bin\...\test-home\{Guid}`、设 `TIPCL_HOME`、调 `Utils.SetRoot(同一路径)`;断言 `Utils.ROOT` 以 `AppContext.BaseDirectory` 开头、等于隔离目录、**不等于 `%LOCALAPPDATA%\TipCL`**;`Utils.ResolveRoot` 的 TIPCL_HOME 分支 / 平台默认分支(改环境变量后 `finally` 恢复原值)/ PPath 规范化;平台键一致性(见 D-16) | 7 |
| [CoreResourcesTests.cs](tests/TipCL.Tests/CoreResourcesTests.cs) | `CoreResources`(A3 起参与编译):`ListDir("plugin-host")` 的 11 项清单与**序数顺序**、`tipcl-plugin.d.ts`(两个点)被过滤规则排除、`ListDir("locales")` 恰好 2 个、`ReadText("locales/zh_CN.json")` 解出**平铺 2773 个键**且内容正确、`ReadBytes` 与 `ReadByResourceName` 一致、缺失资源返回 null;`I18nCore.Tr` 的 zh_CN/en 双语言取值与三级回落、`AvailableLanguages()` | 7 + 3 |
| [LauncherPureTests.cs](tests/TipCL.Tests/LauncherPureTests.cs) | Launcher 纯函数:占位符替换/检测、`SafeVersionId`、`SafeChildPath`、路径拼写判定、`PosixNormPath`、`DashedUuid`/`OfflineUuid`/`Hex`、`FormatSize`、`MavenArtifactPath`、`rules` 的 `os.name`/`os.arch`/`os.version`/`features` 与「最后一条命中的规则生效」、UTF-8 严格解码/BOM/`Cp437` 降级 | 51 + 3 |

- 期望值全部来自读过的源码(注释里标了行号区间);`OfflineUuid("Notch")` 的常量另用 .NET MD5 独立算过一遍。
- 用例失败过 3 次,全部是**测试自己的期望写错**(把 `Resources\locales\*.json` 键里的**全角冒号 U+FF1A** 抄成了半角 `:`),
  已按实测码位改正;**没有改弱断言、没有删用例**(见 D-16)。修正后 131/131 全绿。
- 没有写联网 / 起进程 / 写真实用户目录的测试。

### 遗留与风险

- **7 个文件仍排除编译**(上表),其中 `SkinServer`/`McLauncher`/`GameProcess` 三个是 `BridgeServer` 连带的 ——
  Lead 在 A2 验收里预期 BridgeServer 能在 A3 恢复,实测不行(缺 `BackendApi`),推导见 DECISIONS.md D-14。
  **A5 恢复 `BackendApi` 后要一次把这 4 个一起放回来。**
- **A4 必须知道** `src/TipCL.Core/Launcher/Ai/Defaults.cs` 已经存在(D-13)。
- 运行时差异清点表里的 13 项**都还没修**,是 B1/B2/A5 的活;尤其 E1-E5(代码页)在 mac 上会静默降级成乱码,D2(GlobalMods 软链)会静默变成复制文件。
- 本机是 Windows:所有验证都在 Windows 上跑,**macOS 真机行为未验证**;`--smoke` 只验证 Windows 上的 Avalonia 渲染。
- `dotnet test` 这次可用不代表 D-05 的沙箱限制消失了,换环境仍可能退回进程内执行器。

### Lead 验收(2026-11-12 07:10)

结论:**A3 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行,哈希比对用 Lead 自己的脚本。

| 检查 | 结果 |
|---|---|
| Launcher 顶层 108 个 + `Ai\Defaults.cs` 对 Windows 源比 SHA256(工作树 + git blob) | 109 / 109 一致 |
| A2 的 33 个 Core 顶层文件复查 | 0 个被改 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = NU1900 + CA1416 ×3(Sysinfo 的 Registry) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 131 / 失败 0 / 跳过 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL` 对 Lead 在 A3 开工时记的基线 | 顶层 19 项、9328 个文件、889530606 字节、最新写入 2026-11-10 20:42:16,**四项全部未变** |
| 排除链核对 | `BridgeServer` 88/102 行确实用 `BackendApi`;`McLauncher:40` → `SkinServer`;`GameProcess:43/48` → `McLauncher`。属实 |
| 桩 / 占位 | `src/` 下除 `TipCL.Core.csproj` 和复制文件外没有新增 `.cs` |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 的更正:A2 验收里我写「`BridgeServer` 在 A3 可恢复」是错的,当时只查了它对 Launcher 的引用,没查 `BackendApi`。子代理实测后保留排除是对的。

Lead 在验收时做的一处改动(提交在 A3 之后):

- 新增 [tests/TipCL.Tests/AssemblyInfo.cs](tests/TipCL.Tests/AssemblyInfo.cs):`[assembly: CollectionBehavior(DisableTestParallelization = true)]`。
  原因:`TestHomeIsolation.cs` 有用例会临时清空 `TIPCL_HOME`,而 A4 要搬的 `Ai\FileMemory`(23 行)、`Ai\McpServer`(58 行)、
  `Ai\VisionCaps`(37 行)、`Sync\PhoneLink`(58 行)每次都直接调 `Utils.ResolveRoot()`,不走缓存的 `Utils.ROOT`。
  并行时被测代码会解析到用户真实的 `%LOCALAPPDATA%\TipCL`。A3 编译范围内只有 `Utils.ROOT` 一个读取点,所以 A3 本身没有踩到。

给 A4 / A5 的事实(Lead 实测):

- **`BackendApi`、`Ai`、`Plugins` 三者循环依赖**,必须一起才能全部编译:
  - `BackendApi` 是分部类,两个文件:`Core\BackendApi.cs` 和 `Core\Plugins\BackendApi.Plugins.cs`;`BackendApi.cs` 有 `using TipCL.Core.Launcher.Ai;`。
  - `Ai` 里 5 个文件用到 `BackendApi`:`Agent.cs`、`ExtraTools.cs`、`McpServer.cs`、`Preview.cs`、`Tools.cs`;`Agent.cs` 还引用 `TipCL.Core.Plugins`。
  - `Plugins` 里 5 个文件引用 `Ai`,6 个用到 `BackendApi`。
- 因此路线图调整为:**A4 复制 `Ai` + `Sync` 并编译其中不依赖后端的最大子集**;**A5 搬 `Plugins` 并一次恢复全部被排除的文件**。
  `ModUpdate.cs`、`Preflight.cs` 的恢复包相应从 A4 改为 A5(它们要的 `Conflict` 依赖 `Agent`)。
- `Ai`:65 个 `.cs`、392 KB、无子目录。`Sync`:4 个 `.cs`、40 KB。`Plugins`:23 个文件、272 KB,含子目录 `Sdk\`(2 个)。
- `Plugins` 里 3 个文件用 `System.Windows`(`DotNetPluginContext.cs`、`Sdk\PluginSdk.cs`、`Sdk\PluginUiRegistry.cs`),`SecretStore.cs` 用 `ProtectedData`。
- `Sync\PhoneClient.cs` 376/389 行写死了 `adb.exe` 和 `%LOCALAPPDATA%\Android\Sdk`,属于 B2 的 adb 路径项。

---

## A4 Core/Launcher 的 Ai + Sync

### 做了什么

1. **字节级复制**(只用 `Copy-Item -LiteralPath`,没有「读出来再写回去」):
   - `<WIN>\Core\Launcher\Ai\*.cs` 全部 **65 个** → [src/TipCL.Core/Launcher/Ai/](src/TipCL.Core/Launcher/Ai/)
     (`Defaults.cs` 是 A3 提前搬的,这次覆盖复制后内容不变);
   - `<WIN>\Core\Launcher\Sync\*.cs` 全部 **4 个** → [src/TipCL.Core/Launcher/Sync/](src/TipCL.Core/Launcher/Sync/)。
   - 两个目录都**没有子目录、没有非 `.cs` 文件**(实测),所以不需要 `None Remove`。
2. **编译「不依赖后端的最大子集」**(Lead 裁决):先全部纳入编译,再按实测的
   「找不到类型或命名空间」错误一轮一轮排除,直到归零。**4 轮构建:26 → 6 → 11 → 0 条错误**。
   逐轮实测的类型与行号见 D-18 的表,并已写进 [TipCL.Core.csproj](src/TipCL.Core/TipCL.Core.csproj) 的注释。
3. `Ai\Conflict.cs` **进不了编译**(缺 A5 的 `Ai.Agent`)→ 按任务书的回退方案,
   把 `Launcher\ModUpdate.cs`、`Launcher\Preflight.cs` 的 `Compile Remove` 注释从「A4 恢复」改成「A5 恢复」。
4. `src/TipCL.Core/TipCL.Core.csproj` 加了一行 `<InternalsVisibleTo Include="TipCL.Tests" />`,
   好给 internal 的 `Ai\TomlLite.cs` 写用例(新增决策 **D-19**)。
5. 新增 6 个测试文件、**100 个测试方法 / 177 个用例**(见下)。

### 编译与排除的账

| 目录 | 复制总数 | 参与编译 | 排除 | 说明 |
|---|---|---|---|---|
| `Launcher\Ai\` | **65** | **53** | 12 | 5 个直接缺 `BackendApi`/`Plugins`,7 个连带 |
| `Launcher\Sync\` | **4** | **4** | 0 | `LevelDat` / `PhoneClient` / `PhoneLink` / `SaveSync` 全进 |

**整个 `TipCL.Core` 截至本包一共排除 19 个文件**(= 217 个 `.cs` − 198 参与编译):

| 文件 | 恢复包 | 实测缺什么(本包复跑确认) |
|---|---|---|
| [src/TipCL.Core/BackendApi.cs](src/TipCL.Core/BackendApi.cs) | A5 | 后端主体 |
| [src/TipCL.Core/BridgeServer.cs](src/TipCL.Core/BridgeServer.cs) | A5 | `BackendApi`(88/102 行) |
| [src/TipCL.Core/Launcher/SkinServer.cs](src/TipCL.Core/Launcher/SkinServer.cs) | A5 | 连带 `BridgeServer` 的 4 个静态方法 |
| [src/TipCL.Core/Launcher/McLauncher.cs](src/TipCL.Core/Launcher/McLauncher.cs) | A5 | 连带 `SkinServer.ApiFor`(40 行) |
| [src/TipCL.Core/Launcher/GameProcess.cs](src/TipCL.Core/Launcher/GameProcess.cs) | A5 | 连带 `McLauncher`(43/48 行) |
| [src/TipCL.Core/Launcher/ModUpdate.cs](src/TipCL.Core/Launcher/ModUpdate.cs) | **A5**(A3 写的是 A4) | `Ai.Conflict.InspectJar`;A4 实测 `Conflict` 编不了 |
| [src/TipCL.Core/Launcher/Preflight.cs](src/TipCL.Core/Launcher/Preflight.cs) | **A5**(A3 写的是 A4) | 同上 |
| [src/TipCL.Core/Launcher/Ai/Tools.cs](src/TipCL.Core/Launcher/Ai/Tools.cs) | A5 | `BackendApi` ×10(371/381/392/554/600/658/703/805/814/1067 行) |
| [src/TipCL.Core/Launcher/Ai/ExtraTools.cs](src/TipCL.Core/Launcher/Ai/ExtraTools.cs) | A5 | `BackendApi` ×7(105/269/398/426/448/607/784) |
| [src/TipCL.Core/Launcher/Ai/Agent.cs](src/TipCL.Core/Launcher/Ai/Agent.cs) | A5 | `TipCL.Core.Plugins`(7 行)+ `BackendApi` ×4(345/466/535/1731) |
| [src/TipCL.Core/Launcher/Ai/Preview.cs](src/TipCL.Core/Launcher/Ai/Preview.cs) | A5 | `BackendApi` ×2(25/86) |
| [src/TipCL.Core/Launcher/Ai/McpServer.cs](src/TipCL.Core/Launcher/Ai/McpServer.cs) | A5 | `BackendApi` ×2(32/79) |
| [src/TipCL.Core/Launcher/Ai/Conflict.cs](src/TipCL.Core/Launcher/Ai/Conflict.cs) | A5 | `Agent` ×3(385/410/418) |
| [src/TipCL.Core/Launcher/Ai/Tokens.cs](src/TipCL.Core/Launcher/Ai/Tokens.cs) | A5 | `Agent` ×2(41/59) |
| [src/TipCL.Core/Launcher/Ai/ToolMeta.cs](src/TipCL.Core/Launcher/Ai/ToolMeta.cs) | A5 | `ExtraTools` ×1(52) |
| [src/TipCL.Core/Launcher/Ai/Compact.cs](src/TipCL.Core/Launcher/Ai/Compact.cs) | A5 | `Tokens` ×6(65/83/145/148/194/239) |
| [src/TipCL.Core/Launcher/Ai/Usage.cs](src/TipCL.Core/Launcher/Ai/Usage.cs) | A5 | `Tokens` ×2(33/34) |
| [src/TipCL.Core/Launcher/Ai/Scheduler.cs](src/TipCL.Core/Launcher/Ai/Scheduler.cs) | A5 | `ToolMeta` ×2(20/30) |
| [src/TipCL.Core/Launcher/Ai/Hooks.cs](src/TipCL.Core/Launcher/Ai/Hooks.cs) | A5 | `ToolMeta` ×1(129) |

被排除的文件都**原样留在磁盘并入库**,只在 csproj 里 `Compile Remove`;**没有写任何桩类型**。

### 对复制文件的每处改动

**无。** 69 个复制文件(Ai 65 + Sync 4)对 Windows 源逐字节一致(SHA256 全等,`changed_count=0`),
本包**一处 `edit` 都没对被复制的文件做过**。

`Ai`、`Sync` 里**没有出现 net48 → net8.0 的编译缺口**(0 条 CS 错误需要修),
所以「用最小改动修跨平台缺口」这条没有触发。唯一为了能测量而改的工程配置是
`TipCL.Core.csproj` 的 `InternalsVisibleTo`(它不是复制来的文件,见 D-19)。

### 运行时差异清点(只记录,不修)

grep 范围 = [src/TipCL.Core/Launcher/Ai/](src/TipCL.Core/Launcher/Ai/) 与 [src/TipCL.Core/Launcher/Sync/](src/TipCL.Core/Launcher/Sync/)(含**未参与编译**的文件,因为 A5 会把它们编进来)。**本包不修,也没有一处导致编译失败。**

| # | 位置 | 差异 | 建议包 |
|---|---|---|---|
| A1 | [FileMemory.cs:23](src/TipCL.Core/Launcher/Ai/FileMemory.cs#L23) | `Path.Combine(Utils.ResolveRoot(), "ai_memory.md")` —— **每次调用都重解析 `TIPCL_HOME`,不吃 `Utils.ROOT` 缓存**;mac 上正确落在 `~/.tipcl` | — |
| A2 | [McpServer.cs:58](src/TipCL.Core/Launcher/Ai/McpServer.cs#L58) | 同上,落 `mcp_server.json`(只存 `enabled`/`port`) | — |
| A3 | [VisionCaps.cs:37-43](src/TipCL.Core/Launcher/Ai/VisionCaps.cs#L37-L43) | `Root => Utils.ResolveRoot()`;落 `ai_vision.json` + `cache\ai_vision_cache.json` + `cache\<name>` | — |
| A4 | [PhoneLink.cs:58](src/TipCL.Core/Launcher/Sync/PhoneLink.cs#L58) | 同上,落 `phone_link.json` | — |
| A5 | [PhoneClient.cs:376](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L376) | **任务书漏记的第 5 处** `Path.Combine(Utils.ResolveRoot(), "adb.exe")`(见 D-20) | B2 |
| A6 | [PhoneClient.cs:373-395](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L373-L395) / [439](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L439) | adb 写死 `.exe`:启动器同目录 `adb.exe` → 数据目录 `adb.exe` → PATH 逐目录 `adb.exe` → `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`;mac 上唯一能用的是 395 行的无后缀 `adb` 兜底,且报错文案也写死「adb.exe」 | B2 |
| A7 | [PhoneClient.cs:403-411](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L403-L411) | `ProcessStartInfo(adb,args)` **显式 `UseShellExecute=false`** + `StandardOutputEncoding=UTF8` | — |
| A8 | [McpClient.cs:157-178](src/TipCL.Core/Launcher/Ai/McpClient.cs#L157-L178) | stdio MCP 子进程,**显式 `UseShellExecute=false`**,`WorkingDirectory = PPath.Abs(Utils.ROOT)` | — |
| A9 | [ExtraTools.cs:801](src/TipCL.Core/Launcher/Ai/ExtraTools.cs#L801) | `Process.Start("explorer.exe", "\"" + dir + "\"")`:**没写 `UseShellExecute`**,且写死 Windows 资源管理器 → mac 应改 `open <dir>`(该文件本包不参与编译,A5 才会编进来) | A5/B1 |
| A10 | [McpServer.cs:19/28/34/107/151/224](src/TipCL.Core/Launcher/Ai/McpServer.cs#L107) | MCP Server = **`TcpListener(IPAddress.Loopback, Port)`**,默认端口 **25585**,可被 `mcp_server.json` 的 `port` 覆盖;另有 `IPAddress.IsLoopback` 复查与 `127.0.0.1/localhost/::1/[::1]` 白名单。**Ai/Sync 里 0 处 `HttpListener`** → 不存在 Windows 的 URL 前缀注册问题 | B2 |
| A11 | [PhoneClient.cs:320-346](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L320-L346) | 局域网扫描:本机各网段 `/24` 的 1..254 并发探测 27531/自定义端口(`Parallel.ForEach`,64 并发);**只连不监听** | B2 |
| A12 | [PhoneClient.cs:450/464](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L450) / [PhoneLink.cs:129](src/TipCL.Core/Launcher/Sync/PhoneLink.cs#L129) | USB 走 `adb forward tcp:0 tcp:<port>` 拿本机端口,再连 `127.0.0.1:<port>`;loopback 地址**不记进** `phone_link.json` | — |
| A13 | [AiFs.cs:39/50](src/TipCL.Core/Launcher/Ai/AiFs.cs#L39) / [McpClient.cs:44](src/TipCL.Core/Launcher/Ai/McpClient.cs#L44) | 已经是平台分支(`Utils.IS_WINDOWS` 决定路径大小写敏感比较、MCP stdio 的 `\r\n`),mac 走 else 分支 | — |
| A14 | [PhoneClient.cs:116](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L116) | `WebRequest.Create(...)` → net8 报 **SYSLIB0014**(过时但 `HttpWebRequest` 栈在 net8 仍实现;`Proxy=null/KeepAlive=false` 等仍有效)。这是 A4 **唯一新增的一条编译器警告** | B2 |
| A15 | [SaveSync.cs:55/190/308/331](src/TipCL.Core/Launcher/Sync/SaveSync.cs#L190) / [PhoneLink.cs:202](src/TipCL.Core/Launcher/Sync/PhoneLink.cs#L202) | 反斜杠只出现在**跨平台名字校验/归一**里(`\` 也当非法路径字符、`Replace('\\','/')`、`TrimEnd('\\','/')`),不是路径拼接 → 无差异 | — |
| A16 | [FileMemory.cs:51](src/TipCL.Core/Launcher/Ai/FileMemory.cs#L51) / [PhoneLink.cs:85](src/TipCL.Core/Launcher/Sync/PhoneLink.cs#L85) | 落盘都用 `new UTF8Encoding(false)`(无 BOM)→ 与 Windows 一致 | — |
| A17 | Ai/Sync 全目录 18 处 `Encoding.*` | 全是 `Encoding.UTF8` / `Encoding.ASCII`;**0 处 `Encoding.GetEncoding(...)`** → A3 的 E1-E5 代码页降级问题在这两个目录**不存在** | — |
| A18 | Ai/Sync 全目录 | **0 处** `DllImport` / `Registry` / `ProtectedData` / `System.Windows` / `System.Drawing`(`Microsoft.Win32` 也没有) | — |
| A19 | 凭据/密钥的存取位置(只写位置) | 走缓存 `Utils.ROOT` 的:**`ai_chats.json`**([Store.cs:29](src/TipCL.Core/Launcher/Ai/Store.cs#L29))、`cache\ai_sessions\*.jsonl`([Store.cs:400](src/TipCL.Core/Launcher/Ai/Store.cs#L400)、[Rewind.cs:15](src/TipCL.Core/Launcher/Ai/Rewind.cs#L15))、`cache\ai_trace\*.jsonl`([Trace.cs:20](src/TipCL.Core/Launcher/Ai/Trace.cs#L20))、`cache\ai_checkpoints`([Checkpoint.cs:26](src/TipCL.Core/Launcher/Ai/Checkpoint.cs#L26))、`cache\ai_results`([Artifacts.cs:24](src/TipCL.Core/Launcher/Ai/Artifacts.cs#L24))、`cache\ai_hooks`([Hooks.cs:122](src/TipCL.Core/Launcher/Ai/Hooks.cs#L122))、`ai_permissions.json`([Permission.cs:321](src/TipCL.Core/Launcher/Ai/Permission.cs#L321))。走 `ResolveRoot()` 的见 A1-A5。**明文凭据只有一处**:[PhoneLink.cs:58](src/TipCL.Core/Launcher/Sync/PhoneLink.cs#L58) 的 `phone_link.json` 里存手机地址 + **6 位配对码** + 设备名(无加密)。AI 密钥:`Ai\Client.cs:158` 从 settings 的 `ai_api_key` 取,取不到就用 `Ai\Defaults.DEFAULT_API_KEY` 常量当 `Authorization: Bearer`([Client.cs:177-179](src/TipCL.Core/Launcher/Ai/Client.cs#L177-L179));**Ai 自己一个字节都不落盘密钥**(settings 存哪属 A5/Launcher)。[PhoneClient.cs:22](src/TipCL.Core/Launcher/Sync/PhoneClient.cs#L22) 的 `X-PyMCL-Token` 只是 HTTP 头名 | B1 |
| A20 | [VisionCaps.cs:25/27](src/TipCL.Core/Launcher/Ai/VisionCaps.cs#L25) | 两个外网能力库 URL(`models.dev` / `openrouter.ai`)是 `const`;`Probe` 会发一张内嵌 PNG 实测。**本包没有联网调用任何 AI 接口**,这些常量也没抄进 PROGRESS/DECISIONS/测试 | B2 |

### 测试(新增 6 个文件、100 个测试方法、177 个用例;总数 131 → 308)

| 文件 | 内容 | 用例数 |
|---|---|---|
| [SyncLevelDatTests.cs](tests/TipCL.Tests/SyncLevelDatTests.cs) | `Sync\LevelDat`(NBT 只读解析,纯函数):4 个字段、gunzip、大端有符号 I32/I64、UTF-8 世界名、精确路径匹配(`Foo.*` / `Data.Version.Sub.Name` 不认)、7 种载荷(Byte/Short/Float/Double/ByteArray/IntArray/LongArray/List)跳过宽度、空/`null`/非 Compound 根/未知类型/负数长度/截断/坏 gzip 全返回 `null`;`ReadFile` 两条落盘用例写在 `TestHome.Root` 子目录。**测试数据全部在用例里用字节数组现造,不读真实存档** | 19 |
| [AiTomlLiteTests.cs](tests/TipCL.Tests/AiTomlLiteTests.cs) | `Ai\TomlLite`(internal,靠 D-19):各种标量、转义表 `\b\t\n\f\r\"\\` + `\uXXXX`/`\UXXXXXXXX`、字面/多行字符串、表/点分键/引号键、`[[array of tables]]`、数组(尾逗号/跨行/注释)、内联表、空文档、CRLF 归一,10 条错误输入的 `FormatException` 文案 | 21 |
| [AiModelCapsTests.cs](tests/TipCL.Tests/AiModelCapsTests.cs) | 读嵌入 `ai/model_rules.json` 的模型名匹配:兜底值、三层规则顺序叠加、最具体规则最后生效、全匹配(不是前缀匹配)、`[1m]` 分隔符尾巴、`inputFormat` 只留 5 个键、`modelApiRules` 被 `_slim_props` 丢弃后与 `apiType` 无关、`ResetCache`、`settings.ai_model_caps_overrides`(全匹配键 / 坏正则忽略 / 非字典忽略)、`NormalizeBaseUrl` 10 组 | 24 |
| [AiPermissionTests.cs](tests/TipCL.Tests/AiPermissionTests.cs) | `Ai\Permission` + `AiRule`/`ToolMetaInfo`/`PermissionResult`:模式归一化、10 个合法模式、`Decide` 的模式分支(plan/只读/yolo/auto/build/edit/dontAsk/默认 + 高风险)、规则 deny/allow/ask 的优先级与 `rule.<tool>.<behavior>` id、通配 allow 规则**不能**放行 delete、`RuleContentFromInput` 的键顺序、`BehaviorOf` 抛 `ValueError`、`CoerceRule`、`AiRule.Key`、`DedupeRules`/`ApplyUpdates` | 53 |
| [SyncSaveSyncTests.cs](tests/TipCL.Tests/SyncSaveSyncTests.cs) | `Sync\SaveSync`/`PhoneClient`/`PhoneLink` 纯函数:`Decide` 三方比较 8 条 + 真值表、`Sha256Hex` 公开常量、`SafeWorldName` 17 组 + 200/201 字符边界、`LockFile`、`NormalizeAddress` 7 组 + 空地址抛错、`Enc`、`PhoneLink.Decide` 的 B/L/R 映射与 2 秒容差 | 55 |
| [AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) | **隔离回归守卫**:`FileMemory.FilePath` 以 `TestHome.Root` 开头 + `Remember` 落盘;`VisionCaps.Mode` setter 落 `ai_vision.json` 且 `Detect` 在手动模式下不联网;`PhoneLink` 反向证明(先往 `TestHome.Root` 写 `phone_link.json` 再看 `LastAddress/LastToken/LastDevice` 读得出来);`Permission.SaveRuleStore` 落 `ai_permissions.json` | 5 |

- 期望值全部来自读过的源码(每个文件的头部注释里标了行号区间)与直接读 `Resources/ai/model_rules.json`
  后用独立脚本算出的匹配结果(脚本在 `artifacts/probe-model-rules*.ps1`,不入库)。
- **4 条用例先红后绿,没有一条是靠改弱断言过的**:
  - 2 条是**我抄错全角标点**(和 A3 的 D-16 同一个坑):`Permission.cs:187` 的原因是
    「当前是「只看不动」模式`\uFF0C`不能执行这类操作」、`FileMemory.cs:49` 的表头是
    「# TipCL AI 记忆`\uFF08`一行一条`\uFF0C`可直接编辑或删除`\uFF09`」——原文都是**全角**,
    我按半角写了。已改成 `\uXXXX` 转义写死。**源码行为是对的,不是移植差异。**
  - 1 条是我**测试夹具自己写错**:`Every_payload_kind_is_skipped_with_the_right_width` 里
    把收尾的 `TAG_End` 提前写了一个字节,`LevelName` 落到 `Data` 外面 → 断言抓到(实际 `""`)。
    夹具改对后通过;**这不代表 LevelDat 有移植差异**。
  - 1 条是编译期:`PermissionMode` 是嵌套类型,测试里要写 `Permission.PermissionMode.*`。
- 现有测试**一条都没删、没改弱**;`tests\TipCL.Tests\AssemblyInfo.cs` 的
  `DisableTestParallelization = true`、`TestHomeIsolation.cs` 的 `[ModuleInitializer]`
  **一个字都没动**;新用例**不改写 `TIPCL_HOME`**,要落盘就落在 `TestHome.Root`(或其子目录)下。
- `Ai\McpServer.cs` 因为 A5 的后端依赖被排除编译,**本包没法给它写守卫用例**;
  它的 `PrefPath` 与 A1-A3 是同一形状,等 A5 恢复后补。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工时量 `%LOCALAPPDATA%\TipCL`(只列目录元信息,不读文件内容) | `9328` 个文件 / `889530606` 字节 / 最新写入 `2026-11-10 20:42:16` / 顶层 19 项 —— **与 Lead 在 06:39 记的基线逐项一致**,所以继续 |
| 2 | `dotnet build-server shutdown` | 退出码 **0**(这次两个编译器服务器都在跑,正常关掉) |
| 3 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **0 错误 / 8 警告**,退出码 **0**。分类:**NU1900 = 4**(漏洞审计源不可达,同 A1-A3)、**CA1416 = 3**(全是 Sysinfo.cs 的 Registry,A3 遗留)、**SYSLIB0014 = 1**(新增,`Sync\PhoneClient.cs:116` 的 `WebRequest.Create`,见 A14);**编译器 CS\* = 0 条,`NoWarn` 仍是原来 6 项、没有扩大** |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,**308 个用例全过 / 0 失败 / 0 跳过**,耗时 1.26 秒。通道 = **真 `dotnet test`**(本次没有出现 D-05 的 `Win32Exception (5)`) |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,`总计 308 个用例,通过 308,失败 0,跳过 0,不支持 0`(备用通道也绿) |
| 6 | `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**,**3739 字节**,SHA256 `17F3CD87…017BF`(**与 A1/A2/A3 逐位一致**) |
| 7 | 同上 `--theme dark` | 退出码 **0**,**3751 字节**,SHA256 `374716FF…38144`(**逐位一致**) |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File artifacts\verify-a4-bytes.ps1` | `total=69` / `identical=69` / `changed=0` / `missing=0`;有改动的文件清单为空 |
| 9 | 收尾再量 `%LOCALAPPDATA%\TipCL` | `9328` 个文件 / `889530606` 字节 / 最新写入 `2026-11-10 20:42:16` / 顶层 19 项 —— **与开工时逐项一致,本次工作没有改动用户的真实数据目录** |
| 10 | 警告按编号计数(`-clp:"WarningsOnly;Summary"`) | `NU1900 ×4`、`CA1416 ×3`、`SYSLIB0014 ×1`,合计 8;脚本类警告(xUnit1012/xUnit2013)在收尾前**已全部修掉归零**,没有靠 NoWarn |

### 遗留与风险

- **19 个文件仍排除编译**,其中 **12 个 Ai 文件必须和 `BackendApi` + `Plugins` 在 A5 一起放回来**
  (D-18 的表就是 A5 的清单)。`ModUpdate.cs`/`Preflight.cs` 的恢复包**已从 A4 改成 A5**,
  A5 要顺手把 A3 PROGRESS 表里那句「A4」也一起改掉。
- **没有一处运行时差异被修**:A1-A20 全是 B1/B2/A5 的活。最该注意的三条:
  A5(adb 路径写死 `.exe`)、A9(`explorer.exe` 且没写 `UseShellExecute`)、
  A14(`WebRequest` 在 net8 已过时,现在只是警告)。
- `Ai\McpServer.cs` 被排除 → **MCP Server 的 372 行代码目前一行都没进编译**,
  连「端口 25585 / `TcpListener` 回环」这条也只是读源码得到的结论,不是跑出来的。
- **本机是 Windows**:所有验证都在 Windows 上跑,`Ai`/`Sync` 在 **macOS 真机行为未验证**;
  尤其 A6(adb)、A10(MCP 监听)、A11(网段扫描)在 mac 上能不能用,必须等真机。
- `Sync` 的网络路径(`PhoneClient.Ping/ListWorlds/Download/Upload`)**一条用例都没覆盖**
  —— 它们要连手机,属于联网/起进程,按红线不测。`Sync` 的覆盖只到纯函数与数据解析。
- A4 没有引任何新 NuGet 包;新增的 `InternalsVisibleTo` 只影响测试可见性,不影响运行行为(D-19)。

### Lead 验收(2026-11-12 07:40)

结论:**A4 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行,哈希比对用 Lead 自己的脚本。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下全部 215 个 `.cs` 对 Windows 源比 SHA256(工作树 + git blob) | 215 / 215 一致;没有任何在 Windows 源里找不到对应文件的 `.cs`(无桩) |
| `Ai` 65 个、`Sync` 4 个的文件数 | 与 Windows 源相同 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = NU1900 + CA1416 ×3 + SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 308 / 失败 0 / 跳过 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试前后各量一次) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| 红线文件 `AssemblyInfo.cs`、`TestHomeIsolation.cs`、`Program.cs`、`.gitattributes`、`Directory.Build.props` | 自 `6a95cda` 起未改 |
| `Ai\Defaults.cs` 里的长字符串常量是否出现在 PROGRESS / DECISIONS / 测试代码 | 0 处 |
| 新测试抽查(`AiSyncHomeIsolationTests.cs` 全文) | 断言真实;没有改写 `TIPCL_HOME`、没有反射、没有联网或起进程 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

`InternalsVisibleTo("TipCL.Tests")`(D-19)Lead 认可:不改复制文件,只多一条程序集特性。

给 A5 的事实与裁决(Lead 实测):

- `Plugins` 共 23 个文件、272 KB,含子目录 `Sdk\`(`PluginSdk.cs`、`PluginUiRegistry.cs`)。另有分部类文件 `Plugins\BackendApi.Plugins.cs`。
- **WPF 依赖只有一个类型 `FrameworkElement`**,共 3 个文件约 10 处:`DotNetPluginContext.cs`(7 行 using,481/503/519 行)、
  `Sdk\PluginSdk.cs`(4 行 using,259/261/263 行)、`Sdk\PluginUiRegistry.cs`(4 行 using,160/163/166/172 行)。
  **裁决:把这些位置的 `FrameworkElement` 换成 `object`,去掉 `using System.Windows;`**,Core 不引用任何 UI 框架;
  App 侧(C 阶段的插件页)自己把 `object` 转成 Avalonia 控件。Windows 侧唯一的使用方是 `Pages\Plugins\PluginUi.cs`。
- `Plugins\SecretStore.cs` 166/172 行用 `ProtectedData`(DPAPI)。**裁决:A5 引 NuGet 包 `System.Security.Cryptography.ProtectedData`(8.0.x)让它原样编译**;
  非 Windows 分支(AES-GCM + Keychain)按 GOAL 留给 B1。该包在 mac 上调用会抛 `PlatformNotSupportedException`,记入运行时差异。
- Windows 仓库 `tests\TipCL.Tests` 有 30 个 `.cs`、84 KB(A6 用)。





---

## A5 Plugins + BackendApi + RpcDispatcher + BridgeServer

### 做了什么

1. **第 1 步:字节级复制(中间提交 `496def3`)**。`<WIN>\Core\Plugins\` 整个目录 23 个文件(含子目录 `Sdk\`)用
   `Copy-Item -LiteralPath -Recurse` 复制到 [src/TipCL.Core/Plugins](src/TipCL.Core/Plugins/);复制后逐文件 SHA256 与 Windows 源比对 **23/23 全等**,
   然后单独提交 `A5 step 1: copy Core/Plugins verbatim`,好让后面的每处改动都能 `git diff` 看清。
2. **第 2 步:恢复全部被排除的文件**。`TipCL.Core.csproj` 里 A2–A4 留下的 **19 条 `Compile Remove` 全部删掉**。
   结果:[src/TipCL.Core](src/TipCL.Core/) 里 **0 个被排除编译的文件**(238 个 `.cs` 全部参与编译)。
3. **第 3 步:两条已定裁决照做**
   - **WPF 抽象**:3 个文件里 10 处 `FrameworkElement` → `object`,去掉 3 行 `using System.Windows;`。
     替换后**没有一处**因为调用 `FrameworkElement` 的成员而编不过,所以裁决之外的额外改动为 0;Core 不引用任何 UI 框架(Avalonia 也没引)。
   - **DPAPI**:引 NuGet 包 `System.Security.Cryptography.ProtectedData` **8.0.0**(见 D-22),`SecretStore.cs` 原样编译;非 Windows 分支按 GOAL 留给 B1。
4. **第 4 步:修到编译通过**。第一次把 `BackendApi.cs`(153 KB / 4862 行)、`Agent.cs`、`Tools.cs`、`PluginManager.cs`(55 KB)、
   `PluginMarket.cs`、`BridgeServer.cs`、`McLauncher.cs` 等 19 个文件全量编进 net8.0 —— **0 条 CS 错误**:
   这些文件里**没有 net48 → net8.0 的编译缺口**,组件的跨平台分支(A3/A4 已记录的那些)在 Windows 源里就写好了。
   本包唯一必须改的东西只有裁决里的 WPF 类型和那个 NuGet 包。

### 仍被排除编译的文件

**无。** `TipCL.Core` 里没有任何 `Compile Remove`,238 个 `.cs` 全部参与编译。

### 对复制文件的每处改动(共 3 个文件、13 行)

除下表外,`ChatEventLog.cs`、`JsonRpcPeer.cs`、`NodeHostRuntime`/`PluginManager`/`PluginMarket`/`BackendApi.Plugins.cs` 等其余 20 个 Plugins 文件
以及本包恢复编译的 19 个文件**一字未动**。

| 文件 | 位置 | 原来 | 改成 | 为什么 |
|---|---|---|---|---|
| [Plugins/DotNetPluginContext.cs](src/TipCL.Core/Plugins/DotNetPluginContext.cs#L7) | 7 | `using System.Windows;` | 删除 | WPF 抽象裁决(Lead) |
| 同上 | 481 | `Func<FrameworkElement> factory`(`RegisterPage`) | `Func<object>` | 同上 |
| 同上 | 503 | `Func<FrameworkElement> factory`(`RegisterDashCard`) | `Func<object>` | 同上 |
| 同上 | 519 | `Func<string, FrameworkElement> factory`(`RegisterAiView`) | `Func<string, object>` | 同上 |
| [Plugins/Sdk/PluginSdk.cs](src/TipCL.Core/Plugins/Sdk/PluginSdk.cs#L4) | 4 | `using System.Windows;` | 删除 | 同上 |
| 同上 | 259 | `Func<FrameworkElement>`(`IPluginUi.RegisterPage`) | `Func<object>` | 同上(公开接口签名,属裁决范围) |
| 同上 | 261 | `Func<FrameworkElement>`(`RegisterDashCard`) | `Func<object>` | 同上 |
| 同上 | 263 | `Func<string, FrameworkElement>`(`RegisterAiView`) | `Func<string, object>` | 同上 |
| [Plugins/Sdk/PluginUiRegistry.cs](src/TipCL.Core/Plugins/Sdk/PluginUiRegistry.cs#L4) | 4 | `using System.Windows;` | 删除 | 同上 |
| 同上 | 160 | `Func<FrameworkElement> Factory` | `Func<object>` | 同上 |
| 同上 | 163 | `Func<string, FrameworkElement> AiViewFactory` | `Func<string, object>` | 同上 |
| 同上 | 166 | `public FrameworkElement Create()` | `public object` | 同上 |
| 同上 | 172 | `public FrameworkElement CreateAiView(string chatId)` | `public object` | 同上 |

合计:**10 处类型名替换 + 3 行 `using` 删除**;`git diff --numstat 496def3` = 10 insertions / 13 deletions,与上表一一对应。
除类型名外没有改任何逻辑;P/Invoke 声明、Windows 分支、命名空间、注释全部保留。

`src/TipCL.Core/TipCL.Core.csproj` **不是复制来的文件**,本包对它做了两处改动:删掉 19 条 `Compile Remove`、加一条 `PackageReference`(见下)。

### 字节一致性自检(全 `TipCL.Core` 对 Windows 源)

| 项 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 总数(不含 bin/obj) | **238** |
| 与 Windows 源 SHA256 全等 | **235** |
| 有改动 | **3**(=上表那 3 个 WPF 文件) |
| Windows 源里找不到对应文件(即可能有桩) | **0** |

### 新引的 NuGet 包

`System.Security.Cryptography.ProtectedData` **8.0.0**(net8.0 类库不内置该 API,见 D-22)。
没有引 `SixLabors.ImageSharp` / `Costura.Fody` / `Fody` / `Microsoft.Web.WebView2` / 任何 WPF·WinForms 包;
TFM 仍是 `net8.0`(没有 `-windows`),没有 `UseWPF` / `UseWindowsForms`。

### 运行时差异清点(只记录,不修)

grep 范围 = [src/TipCL.Core/Plugins/](src/TipCL.Core/Plugins/)(23 个文件)+ 本包恢复编译的 19 个文件(`BackendApi.cs`、`BridgeServer.cs`、`Launcher\SkinServer|McLauncher|GameProcess|ModUpdate|Preflight.cs`、`Launcher\Ai\` 的 12 个)。

| # | 位置 | 差异 | 建议包 |
|---|---|---|---|
| A21 | [NodeRuntime.cs:47/181/185/221/238/392/466-471](src/TipCL.Core/Plugins/NodeRuntime.cs#L185) | **便携版 Node 只支持 Windows x64**:`ExeName` 是 `IsWindows ? "node.exe" : "node"`;`InstallPortable` 第 181 行 `if (!IsWindows) throw`;下载平台键写死 `win-x64` / `win-x86`(由 `Environment.Is64BitOperatingSystem` 决定)+ 后缀 `.zip`,在 `SHASUMS256.txt` 里按 `-<platform>.zip` 挑;解压走 `Utils.SafeExtractZip`;`npm` 走 `npm.cmd`(Windows)/`npm`;杀进程用 `taskkill`(**有 `IsWindows` 守卫**)。**mac 需要 `darwin-arm64` / `darwin-x64` 的 `.tar.gz` + 解 tar** | B2 |
| A22 | [ExtraTools.cs:801](src/TipCL.Core/Launcher/Ai/ExtraTools.cs#L801) | `Process.Start("explorer.exe", "\"" + dir + "\"")` —— **没写 `UseShellExecute`**(默认 true 走 shell),且写死 Windows 资源管理器;mac 应改 `open <dir>`。该文件 A5 才进编译 | B1 |
| A23 | [GameProcess.cs:30-31](src/TipCL.Core/Launcher/GameProcess.cs#L30) | 走 `Utils.MakeStartInfo(cmd, cwd, env)`,那里**显式 `UseShellExecute = false`** + 三个流重定向 + UTF8 → 无差异 | — |
| A24 | [NodeHostProcess.cs:75-77](src/TipCL.Core/Plugins/NodeHostProcess.cs#L75) | node 宿主进程启动,**显式 `UseShellExecute = false`**、`CreateNoWindow`;可执行文件来自 `NodeRuntime` → 受 A21 影响 | B2 |
| A25 | [SecretStore.cs:54-93/162-173](src/TipCL.Core/Plugins/SecretStore.cs#L162) | **任务书假设的 `PlatformNotSupportedException` 链在 mac 上不存在**:只有 `IsWindows` 才走 `DpapiProtect/Unprotect`(第 166/172 行的 `ProtectedData`),非 Windows 走 `MagicPlain("TCS0")` + `Xor(body)`。这两个方法带 `[MethodImpl(NoInlining)]`,非 Windows 上根本不会 JIT 到。**真正的差异是**:mac 上插件密钥只做**异或混淆**(不是加密);而且 Windows 写出的 `TCS1` 文件在 mac 上被当成「读不出来」直接返回空(密钥静默丢失) | B1 |
| A26 | [McLauncher.cs:268-296](src/TipCL.Core/Launcher/McLauncher.cs#L268) | 10 个 `DllImport`(kernel32 3 个、user32 7 个)。**两个调用点都有 `Utils.IS_WINDOWS` 守卫**:`_set_priority`(300 行)、`WatchWindowTitle`(377 行)→ mac 上不会调到。P/Invoke 声明按红线保留 | B2 |
| A27 | [Preflight.cs:55/67-71](src/TipCL.Core/Launcher/Preflight.cs#L55) | 2 个 `DllImport`(kernel32 `GetDiskFreeSpaceEx` / `GlobalMemoryStatusEx`)。`GlobalMemoryStatusEx` 在 185 行的 `Utils.IS_WINDOWS` 分支里(**有守卫**);但 **`GetDiskFreeSpaceEx` 第 55 行没有 `IS_WINDOWS` 守卫**,mac 上会抛 `DllNotFoundException` —— 不过它整段在 `try` 里(84 行起),会被吞掉并降级成「拿不到磁盘剩余空间」 | B2 |
| A28 | [BridgeServer.cs:61/203-215/272/687](src/TipCL.Core/BridgeServer.cs#L203) | `new TcpListener(IPAddress.Loopback, port)`;`Start(port = 0)` 时端口由系统分配,`Port` 从 `LocalEndpoint` 读回;另有 272/687 两处 `IPAddress.IsLoopback` 复查 + `127.0.0.1` 白名单(`LOOPBACK_HOST` 常量在 61 行) | — |
| A29 | [SkinServer.cs:575-597](src/TipCL.Core/Launcher/SkinServer.cs#L582) | `new TcpListener(IPAddress.Loopback, 0)`(固定让系统分配端口),对外只报 `http://127.0.0.1:<port>` | — |
| A30 | [McpServer.cs:19/38/107](src/TipCL.Core/Launcher/Ai/McpServer.cs#L107) | `new TcpListener(IPAddress.Loopback, Port)`,默认端口 **25585**,可被 `mcp_server.json` 的 `port` 覆盖;白名单 `127.0.0.1`/`localhost`/`::1`/`[::1]`(224 行)。**本包没有启动过它** | — |
| A31 | 本包文件集(42 个文件) | **0 处** `Microsoft.Win32` / 真正的 `Registry` API(`Registry` 的 24 次命中全是 `PluginUiRegistry` 这个类名和 npm `registry` 参数);**0 处** `System.Drawing`;12 个 `DllImport` 全在 `McLauncher.cs`(10)+ `Preflight.cs`(2);WPF 引用只剩本包删掉的那 3 个 `using` | — |
| A32 | [BackendApi.cs:1738-1749](src/TipCL.Core/BackendApi.cs#L1738) | 实例构造函数**联网**:后台线程 `Thread.Sleep(1500)` → `AuthHttp.Warmup("https://login.live.com/")`。这是本包不实例化 `BackendApi` 的直接原因(见 D-23);C 阶段的 `BridgeHost` 要把它做成可关 | C2 |
| A33 | [BackendApi.cs:2371/3772/3888](src/TipCL.Core/BackendApi.cs#L2371)、[McLauncher.cs:152](src/TipCL.Core/Launcher/McLauncher.cs#L152) | 已有 `Utils.IS_WINDOWS` 平台分支(`USERPROFILE`/`HOME`、路径分隔符、大小写敏感比较),mac 走 else 分支 → 无差异 | — |
| A34 | [BackendApi.Plugins.cs:66](src/TipCL.Core/Plugins/BackendApi.Plugins.cs#L66)、[PluginManager.cs:1892](src/TipCL.Core/Plugins/PluginManager.cs#L1892)、[PluginManifest.cs:430](src/TipCL.Core/Plugins/PluginManifest.cs#L430) | 反斜杠只出现在**跨平台名字归一**(`TrimEnd('/','\\')`、`Replace('\\','/')`)与非法字符校验里,不是路径拼接 → 无差异 | — |
| A35 | [Ai/Tools.cs:416](src/TipCL.Core/Launcher/Ai/Tools.cs#L416) | `Replace("\\", "/")` 归一 AI 给的路径 → 无差异 | — |
| A36 | [NodeRuntime.cs:163-168](src/TipCL.Core/Plugins/NodeRuntime.cs#L163) | `PickFromShasums(shasums, platform = "win-x64", ext = ".zip")` 已经是**可注入 platform/ext 的纯函数** → B2 可以直接拿它给 mac 的 `darwin-arm64` / `.tar.gz` 写单测;但 `.tar.gz` 的**解压**目前只有 `Utils.SafeExtractZip`(zip),tar 侧是 B2 的新工作 | B2 |

### 测试(新增 4 个文件、154 个测试方法、215 个用例;总数 308 → 523)

| 文件 | 方法 | 用例 | 覆盖 |
|---|---|---|---|
| [A5RpcDispatcherTests.cs](tests/TipCL.Tests/A5RpcDispatcherTests.cs) | 33 + 1 Theory | 38 | 方法发现(全小写才暴露 / 去掉 `_`、大写、`[RpcHidden]`、属性、`object` 成员)、同名重载抛错、位置与关键字两种绑定、`[Def]`/可选参数默认值、`[VarArgs]`/`[VarKw]`、未知关键字只告警、`RpcError(-32601)` 的两种形态(`hidden method` / `unknown method`)、Python 风格的 `takes N ... but M were given` / `missing ... argument` 文案、类型转换(bool↔long、整值 double→long、long→double/int 越界)、内部异常拆包。**用测试内自定义的小目标类,不碰 `BackendApi`** |
| [A5PluginsTests.cs](tests/TipCL.Tests/A5PluginsTests.cs) | 52 + 6 Theory | 90 | `SemVer`(解析/比较/预发布/`^`/`~`/通配/比较符/连字符范围/`\|\|`/`IsValidRange`)、`PluginManifest`(合法与非法清单、默认 runtime=node、`system` 权限自动补齐、UI 贡献、`config` 默认值与 `CoerceConfig`、`engines`、依赖、`IsValidId`、`IsSafeRelPath`、BOM、`LoadDir`)、`PluginDirs`(根为 `TestHome.Root` 下的子目录,逐个断言拼法 + 非法 id 抛 `ValueError`)、`SecretStore`(round-trip、文件头 TCS0/TCS1、空/短/坏魔数文件) |
| [A5BridgeServerTests.cs](tests/TipCL.Tests/A5BridgeServerTests.cs) | 26 + 4 Theory | 55 | 静态纯函数:`ReadLine`(CRLF/LF/EOF/裸换行/Latin-1/64 K 上限与边界)、`SplitTarget`(查询串、fragment、scheme+host+port、`//host`、冒号判定)、`Phrase`(12 个码 + 未列出码)、`Unquote`(+号、UTF-8 百分号、坏转义、`%` 结尾)、`QueryValues`、`TokenUrlSafe`、`NormalizeOrigin`、`FixedTimeEquals`。**不构造 `BridgeServer`、不开监听**(理由见 D-23) |
| [A5AiAndBackendTests.cs](tests/TipCL.Tests/A5AiAndBackendTests.cs) | 32 | 32 | `Tokens.EstimateText/EstimateMessages/UsageInt/UpdateFromUsage/CurrentInputTokens`、`Compact.AutoConfigFromSettings/AutoThreshold/ShouldAutocompact/Microcompact`(disabled / no_candidates / nothing_to_clear / not_triggered / applied / below_min_savings / 仅靠空闲触发 / 常量)、`Conflict.InspectJar`(自造 zip:fabric / 优先级 / forge / neoforge / mcmod.info / 无元数据兜底 / `.disabled` / 非 zip / 坏 JSON)、**`McpServer` 落盘隔离守卫(A4 欠的那条)** |

隔离守卫细节(`McpServer`,`A4` 的 PROGRESS 里记着「等 A5 恢复后补」):用公开方法 `McpServer.SetEnabled(false)`(只写 `mcp_server.json` 再 `Stop()`,**不开 `TcpListener`**),
断言文件出现在 `TestHome.Root` 下、内容为 `{"enabled": false, "port": 25585}`、公开读取端 `McpServer.Enabled` 为 false,并断言 `Utils.ResolveRoot()` 就是隔离目录。
反向那条把 `port` 写成 45678 读回 `Port`/`Url`,收尾把静态 `Port` 复位。整个文件没有反射、没有改 `TIPCL_HOME`、没有联网、没有起进程。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前量 `%LOCALAPPDATA%\TipCL` 元信息(只列目录,不读内容) | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 给的基线**逐项一致** |
| 2 | `dotnet build-server shutdown` | 退出码 0(本次成功关闭了 VB/C# 编译器服务器) |
| 3 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **退出码 0,0 错误 / 8 警告** |
| 3b | 同上 + `-v:n`,把警告按编号分组计数 | `CA1416 ×7`(其中 `SecretStore.cs` 的 DPAPI 4 条是本包新增)+ `SYSLIB0014 ×1`(`Sync\PhoneClient.cs:116`,A4 就有)。**没有新增警告编号**,`NoWarn` 仍是原来的 6 项 |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` | **退出码 0:通过 523 / 失败 0 / 跳过 0**(约 0.2 秒)。**走的是 `dotnet test` 这条通道**(本机本次可用,D-15 的结论延续;`dotnet exec` 那条通道本包没跑) |
| 5 | `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**(3739 字节) |
| 6 | 同上 `--theme dark` | 退出码 **0**(3751 字节) |
| 7 | 测试后再量 `%LOCALAPPDATA%\TipCL` 三项元信息 | **9328 / 889530606 / 2026-11-10 20:42:16**,与开工时**完全一致** |
| 8 | 查残留子进程 `node` / `java` / `adb` / `testhost` | `testhost`、`java`、`adb` 均为 0。有 4 个 `node` 进程,但**都不是测试留下的**:启动时间分别是 2026-11-10 22:30、2026-11-11 23:26(×2)、2026-11-10 23:11,均早于本包开工(2026-11-12 07:36),命令行属于 `WorkBuddy` 等本机其它工具 |
| 9 | 字节一致性自检(`src/TipCL.Core` 全部 `.cs` 对 Windows 源比 SHA256) | 总数 **238** / 一致 **235** / 有改动 **3**(DotNetPluginContext、Sdk\PluginSdk、Sdk\PluginUiRegistry)/ 无源文件 **0** |

### 遗留与风险

1. **未验证:mac 真机行为**。本机是 Windows,所有 mac 平台差异都只做到「清点」,没有一处修(按任务书,归 B1/B2)。
2. **未验证:`BackendApi` 的运行时行为**。本包是它第一次能编译,但按 D-23 **一次都没有实例化过** ——
   构造会联网、`Start` 会开监听,红线禁止。所以「后端能否真的跑起来」要在 C 阶段(隔离目录 + 关掉那个预热请求)才能验。
3. **`SecretStore` 在 mac 上是异或混淆而非加密**,而且读不了 Windows 写的密钥文件(见 A25)。这是**功能等价性风险**,B1 换成 AES-GCM + Keychain 才能闭合。
4. **`NodeRuntime` 在 mac 上装不了 Node**(只支持 win-x64/win-x86 的 zip,且非 Windows 直接抛),插件宿主在 mac 上暂时无 Node 可用,归 B2。
5. **`Preflight.GetDiskFreeSpaceEx` 没有 `IS_WINDOWS` 守卫**(A27),mac 上会抛异常但被 `try` 吞掉 → 表现为「查不到磁盘剩余空间」,不会崩;仍建议 B2 加守卫。
6. `ExtraTools.cs:801` 的 `explorer.exe` + 缺 `UseShellExecute`(A22)在 mac 上会直接抛,归 B1。
7. 本包**没有**扩大 `NoWarn`(仍 6 项),也因此把 2 条 `xUnit1012`(拿 null 当 `[InlineData]`)改成独立 `[Fact]` 消除,并没有压掉任何分析器警告。

### Lead 验收(2026-11-12 08:15)

结论:**A5 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行,哈希比对用 Lead 自己的脚本。

| 检查 | 结果 |
|---|---|
| 第 1 步提交 `496def3` 里的 `Plugins` 23 个文件对 Windows 源比 git blob | 23 / 23 一致,确实是原样复制 |
| `src/TipCL.Core` 下全部 238 个 `.cs` 对 Windows 源比 SHA256 | 235 一致,3 个有改动,0 个在 Windows 源里找不到对应文件(无桩) |
| 3 个被改文件的 `git diff 496def3..HEAD` | 恰好是 10 处 `FrameworkElement` → `object` 加 3 行 `using System.Windows;` 删除,没有别的改动 |
| `TipCL.Core.csproj` | 0 条 `Compile Remove`;新增包只有 `System.Security.Cryptography.ProtectedData` 8.0.0;TFM 仍 `net8.0`,没有 `UseWPF` |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = CA1416 ×7 + SYSLIB0014 ×1(NU1900 这次没出现,审计源可达) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 523 / 失败 0 / 跳过 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试前后各量一次) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| 残留进程 `testhost` / `java` / `adb` / `TipCL` | 0 个 |
| 测试代码扫描 | 没有 `new BackendApi(`、`new BridgeServer(`、`new PluginManager(`、监听、联网、起进程;只有 `TestHomeIsolation.cs` 动环境变量 |
| 红线文件、`PROGRESS.md` / `DECISIONS.md` 历史 | 红线文件自 `6a95cda` 起未改;两份文档只增不减(仅状态表 A4、A5 两行被更新) |
| `Ai\Defaults.cs` 的长字符串常量是否出现在文档或测试里 | 0 处 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 复核过的两条新事实(属实):

- `BackendApi(EventBus)` 构造函数(1738–1749 行)在 `InitAccounts()`、`EnsureDefaultInstance()` 之后,排一个线程池任务 1.5 秒后调 `AuthHttp.Warmup("https://login.live.com/")`。
- `Plugins\SecretStore.cs` 非 Windows 走 `TCS0` + 异或(175–180 行),不是加密;DPAPI 的两个方法带 `NoInlining`(163/169 行)。Lead 在 A4 验收里写的「mac 上会抛 `PlatformNotSupportedException`」是错的。

Lead 裁决:

- **预热请求**:C2 做 `BridgeHost` 时给 `BackendApi` 加一个可关的开关(默认行为与 Windows 版一致),这是对复制文件的一处已授权改动。在那之前,**任何测试都不许实例化 `BackendApi`**。

给 A6 的事实(Lead 实测 Windows `tests\TipCL.Tests`):

- 共 29 个 `.cs`(84 KB)加一个 `fixtures\webp-sample.webp`(**更正:实际 30 个 `.cs`,见 A6 的 Lead 验收**)。工程引用的是 net48 的 `TipCL.exe`,另有包 `GitHubActionsTestLogger`(mac 不需要)。
- **7 个文件依赖界面层命名空间**(`TipCL.Services` / `Pages` / `Controls` / `Themes`),A6 搬不了:`AiChatOpsTests`、`FileKindsTests`、`LayoutDefaultTests`、`NavCompatTests`、`PageLayoutTests`、`WebpFallbackTests`、`Win7CompatTests`。后两个还依赖 ImageSharp,是 Windows 专属,mac 版不适用。
- Windows 的 `TestHome.cs` 是 `public sealed class TestHome : IDisposable`(每个测试在 `%TEMP%` 建临时目录并改 `TIPCL_HOME`,`Dispose` 时恢复),**与 mac 这边的 `internal static class TestHome` 重名**;它还带一行与 `AssemblyInfo.cs` 重复的 `[assembly: CollectionBehavior(...)]`。
- `StaticInitOrderTests`、`IconFontTests` 靠向上找 `TipCL.sln` 定位仓库根再读源码文件;mac 的 sln 叫 `TipCL.Mac.sln`,源码路径也不同。`IconFontTests` 要的 `TipCLIcons.ttf` 到 C1 才有。
- `PhoneLinkTests` 在测试内起一个 `HttpListener`(`http://127.0.0.1:<随机端口>/`)当假手机;`WorldFixture.cs` 被它和 `SaveSyncTests` 共用。
- Windows 测试里没有 `IClassFixture` / `[Collection]`,也没有直接调 `Utils.SetRoot` / `ResolveRoot`。

---

## A6 测试移植

### 做了什么

1. **第 1 步(提交 `cd8e2e4`)**:把 Windows `tests\TipCL.Tests\` 下**除 `TestHome.cs` 外的 29 个 `.cs`**
   用 `Copy-Item -LiteralPath` 字节级复制到 `tests\TipCL.Tests\Ported\`(85038 字节;`TestHome.cs` 与
   `fixtures\` 不搬),在 `TipCL.Tests.csproj` 里先整体 `Compile Remove Ported\**\*.cs`,
   确认构建 0 错误、523/523 全过,然后**单独提交** —— 之后对搬来的测试的每处改动都能用 `git diff` 看清。
2. **第 2 步**:合并 `TestHome`(静态隔离一字未改 + 实例成员,见 D-25),并给 `TestHomeIsolationTests`
   补 2 条用例(525 = 523 + 2)。
3. **第 3 步**:`Program.cs` 补 `Dispose`/`IAsyncLifetime`,并把执行顺序改成声明顺序(见 D-26)。
4. **第 4 步**:逐个文件从 `Compile Remove` 里放出来,得到下面的 A/B/C 分类;C 类保留 `Compile Remove`,
   每个文件的原因与恢复包都写进了 `TipCL.Tests.csproj` 的注释。
5. **第 5 步**:重叠对照表(下面),两边用例**一条都没合并、没删**。

> 口径修正:Windows 测试目录实测是 **30 个 `.cs`**(不是 Lead 事实里的 29),除 `TestHome.cs` 外 29 个;
> 见 D-30。

### 29 个文件的分类(A 15 / B 4 / C 10)

**A 类 —— 原样通过(一处不改)** 共 15 个文件、103 条用例:

| 文件 | 用例 | 覆盖主题 |
|---|---|---|
| [WorldFixture.cs](tests/TipCL.Tests/Ported/WorldFixture.cs) | 0 | 共享夹具(造 `level.dat` 与存档树),被 `SaveSyncTests` 用;**本身没有用例** |
| [AiCompactTests.cs](tests/TipCL.Tests/Ported/AiCompactTests.cs) | 4 | `Ai\Compact` 阈值/候选/微压缩 |
| [AiHistoryTests.cs](tests/TipCL.Tests/Ported/AiHistoryTests.cs) | 1 | 失败回合不进模型上下文 |
| [AiModeTests.cs](tests/TipCL.Tests/Ported/AiModeTests.cs) | 9 | `Ai\Client` 模式归一化与端点解析(6 InlineData + 3 Fact) |
| [ExtraToolsTests.cs](tests/TipCL.Tests/Ported/ExtraToolsTests.cs) | 13 | `Ai\ExtraTools` 工具注册表(8 InlineData + 5 Fact) |
| [FileMemoryTests.cs](tests/TipCL.Tests/Ported/FileMemoryTests.cs) | 5 | `Ai\FileMemory` 读写/裁剪 |
| [ImagesTests.cs](tests/TipCL.Tests/Ported/ImagesTests.cs) | 2 | `Ai\Images` base64/data-url |
| [Mc26SupportTests.cs](tests/TipCL.Tests/Ported/Mc26SupportTests.cs) | 12 | 1.21.6+/26.x 版本支持判定(2 Theory × 12 InlineData) |
| [McpServerTests.cs](tests/TipCL.Tests/Ported/McpServerTests.cs) | 5 | `McpServer` 开关与 JSON 落盘(**不启动监听**) |
| [MemoryEditTests.cs](tests/TipCL.Tests/Ported/MemoryEditTests.cs) | 7 | 记忆条目增删改(每条一个 `TestHome`) |
| [PluginMarketTests.cs](tests/TipCL.Tests/Ported/PluginMarketTests.cs) | 11 | `PluginMarket` + `PluginMarketMirrorExtraTests`(两个类) |
| [SaveSyncTests.cs](tests/TipCL.Tests/Ported/SaveSyncTests.cs) | 19 | `Sync\SaveSync` 备份/校验/世界名(15 InlineData + 4 Fact) |
| [ToolRegistryTests.cs](tests/TipCL.Tests/Ported/ToolRegistryTests.cs) | 6 | `Ai\ToolMeta` 注册表自洽性 |
| [UpdaterApplyTests.cs](tests/TipCL.Tests/Ported/UpdaterApplyTests.cs) | 2 | `Updater.BuildApplyScript` 纯字符串拼装 |
| [VisionCapsTests.cs](tests/TipCL.Tests/Ported/VisionCapsTests.cs) | 7 | `Ai\VisionCaps` 归一化与投票(4 InlineData + 3 Fact) |

**B 类 —— 最小适配后通过** 共 4 个文件、24 条用例(改动 8 处,`git diff --stat` = 4 files / 16 insertions / 8 deletions,
其中 8 处是改动、另 8 行是解释性注释):

| 文件 | 用例 | 改动处数 |
|---|---|---|
| [AiFileToolsTests.cs](tests/TipCL.Tests/Ported/AiFileToolsTests.cs) | 18 | 3 |
| [AiFixChatStoreTests.cs](tests/TipCL.Tests/Ported/AiFixChatStoreTests.cs) | 4 | 2 |
| [DecompileArtifactTests.cs](tests/TipCL.Tests/Ported/DecompileArtifactTests.cs) | 1 | 2 |
| [WriteJsonTests.cs](tests/TipCL.Tests/Ported/WriteJsonTests.cs) | 1 | 1 |

**C 类 —— 本包不能搬** 共 10 个文件(静态计数 ≥52 条用例,`FileKindsTests` 的 `MemberData` 行数只能算下界):

| 文件 | 用例(静态) | 原因 | 恢复包 |
|---|---|---|---|
| [AiChatOpsTests.cs](tests/TipCL.Tests/Ported/AiChatOpsTests.cs) | 11 | `using TipCL.Pages.Ai`(界面层) | C3(AI 对话页) |
| [FileKindsTests.cs](tests/TipCL.Tests/Ported/FileKindsTests.cs) | ≥2(1 Fact + 1 Theory/MemberData) | `using TipCL.Services` | C2/C3 |
| [LayoutDefaultTests.cs](tests/TipCL.Tests/Ported/LayoutDefaultTests.cs) | 5 | 3 条只用 Core,1 条用 `TipCL.Pages.DashLayout` → 整文件归 C(不拆文件) | C3(启动页布局) |
| [NavCompatTests.cs](tests/TipCL.Tests/Ported/NavCompatTests.cs) | 4 | `using TipCL.Services` + `TipCL`(导航) | C2(外壳/导航) |
| [PageLayoutTests.cs](tests/TipCL.Tests/Ported/PageLayoutTests.cs) | 15 | `using TipCL.Pages` | C3(对应页面) |
| [IconFontTests.cs](tests/TipCL.Tests/Ported/IconFontTests.cs) | 1 | 要 `Resources\fonts\TipCLIcons.ttf` + 扫界面源码 | C1(主题/图标) |
| [StaticInitOrderTests.cs](tests/TipCL.Tests/Ported/StaticInitOrderTests.cs) | 1 | 读 `src/TipCL/Pages/SettingsPage.cs`(页面未移植) | C3(设置页);见 D-28 |
| [PhoneLinkTests.cs](tests/TipCL.Tests/Ported/PhoneLinkTests.cs) | 4 | **环境限制**:testhost 里 `HttpListener.Start()` 抛异常 | 能在 testhost 里起 `HttpListener` 的环境;见 D-27 |
| [WebpFallbackTests.cs](tests/TipCL.Tests/Ported/WebpFallbackTests.cs) | 4 | `TipCL.Services` + `SixLabors.ImageSharp` | **无(mac 版不适用)**,GOAL 已定图片走 SkiaSharp |
| [Win7CompatTests.cs](tests/TipCL.Tests/Ported/Win7CompatTests.cs) | 5 | 同上(`3 Fact + 2 InlineData`) | **无(mac 版不适用)** |

`PhoneLinkTests` 的异常原文(逐字):

```
System.Net.HttpListenerException : 句柄无效。
   at System.Net.HttpListener.SetupV2Config()
   at System.Net.HttpListener.Start()
   at TipCL.Tests.PhoneLinkTests.FakePhone..ctor(String dir) in ...\Ported\PhoneLinkTests.cs:line 44
   at TipCL.Tests.PhoneLinkTests.FullFlow() in ...\Ported\PhoneLinkTests.cs:line 202
```

### B 类的每一处改动

| # | 文件:行 | 原来 | 改成 | 为什么 |
|---|---|---|---|---|
| 1 | [AiFileToolsTests.cs:32](tests/TipCL.Tests/Ported/AiFileToolsTests.cs#L32) | `Path.Combine(Path.GetTempPath(), "tipcl-fs-test-" + …)` | `Path.Combine(TestHome.Root, "tipcl-fs-test-" + …)` | B.3:红线要求测试临时文件落在仓库内(这类用例会真写文件) |
| 2 | [AiFileToolsTests.cs:75](tests/TipCL.Tests/Ported/AiFileToolsTests.cs#L75) | `Path.Combine(Path.GetTempPath(), "tipcl-fs-not-allowed.txt")` | `Path.Combine(TestHome.Root, "tipcl-fs-not-allowed.txt")` | B.3:同 1;它仍是「允许根之外」的反例路径,断言不变 |
| 3 | [AiFileToolsTests.cs:190](tests/TipCL.Tests/Ported/AiFileToolsTests.cs#L190) | `Path.Combine(Path.GetTempPath(), "tipcl-fs-escape.txt")` | `Path.Combine(TestHome.Root, "tipcl-fs-escape.txt")` | B.3:同 1 |
| 4 | [WriteJsonTests.cs:18](tests/TipCL.Tests/Ported/WriteJsonTests.cs#L18) | `Path.Combine(Path.GetTempPath(), "tipcl-wjtest-" + …)` | `Path.Combine(TestHome.Root, "tipcl-wjtest-" + …)` | B.3:同 1 |
| 5 | [AiFixChatStoreTests.cs:84](tests/TipCL.Tests/Ported/AiFixChatStoreTests.cs#L84) | 向上找 `TipCL.sln` | 向上找 `TipCL.Mac.sln` | B.1:mac 的 sln 叫 `TipCL.Mac.sln` |
| 6 | [AiFixChatStoreTests.cs:76](tests/TipCL.Tests/Ported/AiFixChatStoreTests.cs#L76) | `Path.Combine(root,"src","TipCL","Core","BackendApi.cs")` | `Path.Combine(root,"src","TipCL.Core","BackendApi.cs")` | B.1:程序集拆分后的相对路径 |
| 7 | [DecompileArtifactTests.cs:18](tests/TipCL.Tests/Ported/DecompileArtifactTests.cs#L18) | 向上找 `TipCL.sln` | 向上找 `TipCL.Mac.sln` | B.1 |
| 8 | [DecompileArtifactTests.cs:21](tests/TipCL.Tests/Ported/DecompileArtifactTests.cs#L21) | 扫描根 `Path.Combine(root,"src","TipCL")` | `Path.Combine(root,"src")` | B.1 的精神:mac 没有 `src/TipCL` 这个 App 工程目录,源码根是 `src`;扫描面更大、断言未改(见 D-29) |

- **没有**改任何断言、期望值,没有删用例,没有加 `Skip`,**没有改 `src\` 下的任何文件**。
- 字节一致性自检:`Ported\` 29 个文件对 Windows 源比 SHA256 → **25 个一致、4 个有改动**(就是上面这 4 个文件)。

### 移植差异(逐条)

**无。** 两条通道各出现过 1 条红,都不是移植差异,已在包内查清并如实处置:

1. `AiFixChatStoreTests.SetActiveOnUnknownChatIsSilentAndKeepsActive`(进程内通道红、`dotnet test` 通道绿):
   真因是**执行器按方法名排序**与 xunit 的**声明顺序**不一致 —— 同类 3 条用例各自 `new TestHome()` 但 `Store`
   走缓存 `Utils.ROOT`(D-25),字母序把它排到 `EnsureChat*` 之后,于是读到前一条用例留下的
   `fix-20260101000000`,`Assert.Null(chat)` 必然失败。**不是 net48→net8.0 的行为差异**:
   同一份 dll 在 xunit 通道通过就证明了这一点。处置:改执行器顺序(D-26),没动测试。
2. `PhoneLinkTests.FullFlow`(testhost 通道红、进程内通道绿):`HttpListenerException: 句柄无效`
   (环境限制,见 D-27 / 上面的异常原文)。**不是移植差异**:同一份 dll 进程内通道 4 条全过。处置:整文件归 C。
3. 顺带发现(不是差异,是 Windows 测试的固有隐患):一个测试类里多条用例共用缓存 `Utils.ROOT`
   下的落盘文件,所以**用例顺序会决定红绿**。Windows 版同样如此(它的 `TestHome` 也只改 `TIPCL_HOME`),
   只是 xunit 的声明顺序恰好没踩到。已记 D-25/D-26。

### 重叠对照(Windows 搬来的文件 ↔ mac 侧 A2–A5 已写的文件;**两边都保留,没有合并或删除**)

| 搬来的(Windows) | 主题 | mac 侧已有 |
|---|---|---|
| `AiCompactTests.cs` | `Ai\Compact` | [A5AiAndBackendTests.cs](tests/TipCL.Tests/A5AiAndBackendTests.cs) |
| `AiHistoryTests.cs` / `AiFixChatStoreTests.cs` | `Ai\Store` 对话/历史 | [A5AiAndBackendTests.cs](tests/TipCL.Tests/A5AiAndBackendTests.cs)、[AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) |
| `FileMemoryTests.cs` / `MemoryEditTests.cs` | `Ai\FileMemory` | [AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) |
| `VisionCapsTests.cs` | `Ai\VisionCaps` | [AiModelCapsTests.cs](tests/TipCL.Tests/AiModelCapsTests.cs)、[AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) |
| `McpServerTests.cs` | `Ai\McpServer` | [A5AiAndBackendTests.cs](tests/TipCL.Tests/A5AiAndBackendTests.cs)、[AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) |
| `SaveSyncTests.cs` | `Sync\SaveSync` | [SyncSaveSyncTests.cs](tests/TipCL.Tests/SyncSaveSyncTests.cs) |
| `PhoneLinkTests.cs`(C 类) | `Sync\PhoneLink/PhoneClient` | [SyncSaveSyncTests.cs](tests/TipCL.Tests/SyncSaveSyncTests.cs)、[AiSyncHomeIsolationTests.cs](tests/TipCL.Tests/AiSyncHomeIsolationTests.cs) |
| `PluginMarketTests.cs` | `Plugins\PluginMarket` | 无(只与 [A5PluginsTests.cs](tests/TipCL.Tests/A5PluginsTests.cs) 的 Plugins 其它部分相邻) |
| `AiFileToolsTests.cs` / `ExtraToolsTests.cs` / `ToolRegistryTests.cs` / `AiModeTests.cs` / `ImagesTests.cs` / `UpdaterApplyTests.cs` / `Mc26SupportTests.cs` / `WriteJsonTests.cs` / `DecompileArtifactTests.cs` / `WorldFixture.cs` | `Ai\ExtraTools`/`ToolMeta`/`Client`/`Images`、`Launcher\Updater`/`Utils`、1.21.6+ 支持、源码卫生、夹具 | **无**(这 10 个主题以前 mac 侧没有用例,本包是新增覆盖;其中 `WriteJsonTests` 与 [LauncherPureTests.cs](tests/TipCL.Tests/LauncherPureTests.cs) 同属 `Launcher\Utils` 但用例不重叠) |

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工时量 `%LOCALAPPDATA%\TipCL`(只列目录,不读内容) | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 基线**逐项一致**,继续施工;`%TEMP%` 下 `tipcl-test-*` 目录 = **2** |
| 2 | `dotnet build-server shutdown` | 退出码 0(本次成功关闭) |
| 3 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **退出码 0,0 错误 / 8 警告**;按编号 = `CA1416 ×7` + `SYSLIB0014 ×1`,与 A5 收尾**完全相同,没有新编号**;测试工程本身 0 警告 |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` | **退出码 0:通过 652 / 失败 0 / 跳过 0**(约 0.7 秒) |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | **退出码 0:总计 652,通过 652,失败 0,跳过 0,不支持 0** |
| 6 | 交叉核对(TRX logger + 逐类对比两通道的通过数) | 两通道都是 **38 个测试类、652 条用例**,**逐类用例数 0 处不一致** |
| 7 | `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**(3739 字节,与 A5 同) |
| 8 | 同上 `--theme dark` | 退出码 **0**(3751 字节,与 A5 同) |
| 9 | 测试跑完后再量 `%LOCALAPPDATA%\TipCL` | **9328 / 889530606 / 2026-11-10 20:42:16**,与开工时**完全一致** |
| 10 | 测完 `%TEMP%` 下 `tipcl-test-*` 目录数 | **2**,没有增加 |
| 10b | 归因实验:`%TEMP%` 顶层条目数,分别单独跑一次工具 | 进程内执行器 **delta 0**(测试进程一个都不建);`dotnet test` **+2**、`dotnet build` **+2** —— 那 2 个是 VSTest / MSBuild 自己的随机空目录(形如 `8char.3ext`),A1–A5 每次构建/测试也一样,与测试代码无关 |
| 11 | 残留进程 `testhost` / `java` / `adb` / `TipCL` | 各 **0** 个 |
| 12 | 字节一致性:`Ported\` 29 个文件对 Windows 源比 SHA256 | 一致 **25** / 有改动 **4**(全是 B 类文件) |
| 13 | `git status --short`(提交前) | 只有 `tests/TipCL.Tests/` 下的文件与两份文档;没有 `bin\`、`obj\`、`artifacts\`、PNG、`test-home` |

### 遗留与风险

1. **C 类 10 个文件(≥52 条用例)没有搬**,原因与恢复包见上表;C 类文件保持与 Windows 源**逐字节一致**,
   只被 `Compile Remove` 排除,恢复时不会跟 B 类改动混淆。
2. **`PhoneLinkTests` 的处置需要 Lead 知悉**:它的 4 条用例在**进程内通道全过**,只在 `dotnet test` 的
   testhost 里因 `HttpListener` 起不来而红(D-27)。本包按任务书「环境原因 → C 类 + 环境限制」处理;
   若 Lead 认为本机应以进程内通道为准,可以直接把这个文件从 `Compile Remove` 里放出来(一行改动)。
3. **顺序敏感的用法是搬来的测试的固有属性**(同类内共用缓存 `Utils.ROOT` 的落盘文件):两条通道现在都按
   声明顺序跑,但换顺序仍可能变红。D-25/D-26 记了来龙去脉,后面谁要改执行顺序/并行度必须先看这两条。
4. **`WorldFixture.cs` 现在只被 `SaveSyncTests` 用**(`PhoneLinkTests` 归 C 之后);它是 `internal static class`、
   自己不用例,`git diff` 里是 A 类(未改)。
5. **未验证:mac 真机行为**。本机是 Windows,`HttpListener` 的差异也只在 testhost 通道出现;
   `dotnet test` 在 Mac 上会走另一套 Socket 实现,`PhoneLinkTests` 到时候可以再试一次。
6. 进程内执行的测试数据目录:`TestHome.Root` = `bin\Release\net8.0\test-home\{每次运行一个 Guid}`,
   目录主体跑完不删(与 A3 起的行为一致),它在 `bin\` 内、不入库;
   用例级目录 `TestHome.Dir` = `Root\t-{Guid}`,`Dispose` 时删除。
7. 适配前的那两次试跑(08:15/08:19)在 `%TEMP%` 留下过一个**空目录** `tipcl-phone-sync`
   (由 `PhoneLinkTests.FullFlow` 经 Core 的 `PhoneLink.TempZip()` 创建,`PhoneLink.cs:279` 用 `Path.GetTempPath()`);
   改成 C 类后不再产生。**没有删除它**:红线要求在仓库外不做写操作,留报给 Lead 处置。
8. 分层提醒:`AiFileToolsTests` 里有 `"C:\\some\\where\\else.txt"`、`"C:"`、`GetRandomFileName()` 这类
   Windows 形状的**字符串**与 `"logs\\latest.log"` 的**反斜杠相对路径**,在 Windows 上本来就该绿、本机也全绿;
   但这些字面量对 mac 没有意义,等 mac 端真的开始跑真机测试时值得再看一眼(本包按任务书**不做 mac 化改写**)。

### Lead 验收(2026-11-12 08:40)

结论:**A6 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 第 1 步提交 `cd8e2e4` 里 `Ported\` 的 29 个文件对 Windows 源比 git blob | 29 / 29 一致,确实是原样复制;`TestHome.cs` 没搬 |
| `git diff cd8e2e4..HEAD -- tests/TipCL.Tests/Ported` | 只有 4 个文件、8 处改动:3 处 `TipCL.sln` / 源码路径适配,5 处 `Path.GetTempPath()` → `TestHome.Root`。没有改断言、期望值,没有删用例或加 Skip |
| `src\` 自 `6bdad10` 起 | 0 个文件被改 |
| 红线文件 `AssemblyInfo.cs`、`.gitattributes`、`Directory.Build.props` | 未改 |
| `TestHomeIsolation.cs` | 静态 `Root` / `Init()` 未动;新增实例成员 `Dir` / 构造 / `Dispose`,临时目录建在 `Root` 下;原有用例未删,新增 2 条 |
| `Program.cs` | 按声明顺序执行(原来按方法名),补 `IDisposable` / `IAsyncLifetime` 清理;其余逻辑未动 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = CA1416 ×7 + SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 652 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 652 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试前后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录数(Lead 复跑前后) | 78 → 78;`tipcl-test-*` 仍是 2 个 |
| 残留进程 `testhost` / `java` / `adb` / `TipCL` | 0 个 |
| 参与编译的搬来测试扫描 | 没有实例化后端、监听、联网、起进程、`GetTempPath` 调用(只剩注释里的字样) |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 的更正与裁决:

- A5 验收里我写「Windows 测试共 29 个 `.cs`」是错的,实际 30 个;除 `TestHome.cs` 外搬 29 个,子代理做得对(D-30)。
- A5 验收里我把 `StaticInitOrderTests` 归因为 sln 改名也不完整,它还要读未移植的 `Pages\SettingsPage.cs`,归 C3 恢复(D-28)。
- **`PhoneLinkTests` 维持排除**。除了 testhost 里 `HttpListener` 报「句柄无效」,还有第二个原因:它经 Core 的 `PhoneLink.cs:279` 往 `%TEMP%\tipcl-phone-sync` 写文件,违反「测试临时文件落在仓库内」。Lead 没有在本机复跑它。恢复条件:B2 把该临时目录改成可注入之后。
- 红线记录:A6 适配前的两次试跑(08:15 / 08:19)在 `%TEMP%` 建了一个空目录 `tipcl-phone-sync`,属于写到了仓库之外。目录为空,Lead 没有删除,已报用户。
- 执行顺序敏感(D-25 / D-26):同类用例共用缓存的 `Utils.ROOT`,这是 Windows 测试原有的性质。以后不许给测试类加并行或乱序。

## B1a mac 平台服务(Keychain / Skia 编解码 / 插件凭据 / 启动接线)

任务书:`GOAL.md` §3 的「凭据」与「图片」两行 + Lead 给 B1a 的五条裁决。**只做 B1a**,B1b / B2 / B3 未开始。

### 做了什么

1. **`MacKeychainSecretStore`**(新):`ISecretStore` 的 mac 实现,走 `/usr/bin/security` 的 generic password。
   命令行拼装是三条纯函数(`ReadArguments` / `WriteArguments` / `DeleteArguments`),进程调用通过构造函数注入的
   运行器委托(默认运行器用 `ProcessStartInfo.ArgumentList`,`UseShellExecute=false`,stdout/stderr 并发读)。
   存储编码 = `b64:` + 密码 UTF-8 Base64;错误语义对齐 `WinCredSecretStore`(读不到 → null;删不到 →
   `SecretNotFoundException`;其它 → `OSError`),异常消息里不含密码。
2. **`SkiaImageCodec` / `SkiaDecodedImage`**(新):`IImageCodec` 的 Skia 实现,PNG / JPEG / WebP / GIF / BMP 实测都能解
   (WebP 直接由 Skia 解,不引 ImageSharp),`HasAlpha` / `Resize` / `EncodePng` / `EncodeJpeg` / `Dispose` 逐项对齐 WPF 版,
   差异见下面的对照表。
3. **`Plugins\SecretStore.cs` 的非 Windows 分支**:新增魔数 `TCS2` = AES-256-GCM(主密钥 32 字节随机数,
   Base64 存进 `ISecretStore` 的 `TipCL plugin secrets` / `master-key`)。Windows 仍写 `TCS1`;非 Windows 且没有凭据库时
   保持 `TCS0`。为了在本机测到 AES 分支,加了 `internal` 注入构造函数(公开构造函数签名与 Windows 行为不变)。
4. **启动接线**:`PlatformBootstrap.Init()`(Core)给 `PlatformServices.ImageCodec`(所有平台 → Skia)与
   `PlatformServices.SecretStore`(仅 mac → Keychain,其它平台保持 null)赋值,只赋值、不做任何 I/O;
   `TipCL.App\Program.cs` 在 `Main` 第一行调用它(桌面与 `--smoke` 共用)。
5. **测试**:新增 5 个文件、114 个用例(两条通道都全绿,总数 652 → 766),外加 webp 夹具入仓库。

### 关键文件

| 文件 | 说明 |
|---|---|
| [MacKeychainSecretStore.cs](src/TipCL.Core/Platform/Mac/MacKeychainSecretStore.cs) | 新。mac 凭据库实现 + 命令行纯函数 + `SecretStoreCommandResult` |
| [SkiaImageCodec.cs](src/TipCL.Core/Platform/Skia/SkiaImageCodec.cs) | 新。Skia 解码入口(EXIF 方向、alpha 判定、失败异常) |
| [SkiaDecodedImage.cs](src/TipCL.Core/Platform/Skia/SkiaDecodedImage.cs) | 新。`Resize` / `EncodePng` / `EncodeJpeg` / `Dispose` + `ApplyOrigin` |
| [PlatformBootstrap.cs](src/TipCL.Core/Platform/PlatformBootstrap.cs) | 新。平台接线(纯函数 + 只赋值的 `Init`) |
| [SecretStore.cs](src/TipCL.Core/Plugins/SecretStore.cs) | 改。TCS2 / 注入入口(逐处见下) |
| [Program.cs](src/TipCL.App/Program.cs) | 改。`Main` 第一行 `PlatformBootstrap.Init()`(3 行) |
| [Directory.Build.props](Directory.Build.props) | 改。新增 `TipCLSkiaSharpVersion = 2.88.9` |
| [TipCL.Core.csproj](src/TipCL.Core/TipCL.Core.csproj) | 改。加 `SkiaSharp` 包引用 |
| [TipCL.Tests.csproj](tests/TipCL.Tests/TipCL.Tests.csproj) | 改。webp 夹具 `CopyToOutputDirectory` |
| [fixtures/webp-sample.webp](tests/TipCL.Tests/fixtures/webp-sample.webp) | 新(从 Windows 测试目录字节级复制,SHA256 `1BF9449D...098A`,4880 字节,与源一致) |

测试文件:`B1aKeychainTests.cs`(39)、`B1aSkiaCodecTests.cs`(44)、`B1aPluginSecretStoreAesTests.cs`(19)、
`B1aImagesCodecTests.cs`(6)、`B1aPlatformBootstrapTests.cs`(6)。

### 对复制文件的每处改动(`Plugins\SecretStore.cs`,184 → 296 行)

Windows 源:184 行;mac 现 296 行。除下面这些,其余字节一致(见字节自检)。

1. 第 8 行后加 `using TipCL.Core.Platform;`(要用 `PlatformServices` / `ISecretStore`)。
2. 类注释里加两行 B1a 说明(TCS2 / 旧格式仍可读)。
3. 魔数区加 `MagicAes`(`TCS2`)+ 5 个常量:`AesNonceSize=12`、`AesTagSize=16`、`MasterKeySize=32`、
   `MasterKeyService="TipCL plugin secrets"`、`MasterKeyAccount="master-key"`。
4. 字段:`_keyStore`(`ISecretStore`)、`_windows`(bool)新增。
5. `public SecretStore(string path)` 改成链式委托 `: this(path, PlatformServices.SecretStore, IsWindows)`,
   方法体原样搬进新的 `internal SecretStore(string path, ISecretStore keyStore, bool treatAsWindows)`。
6. `ReadAll`:`if (!IsWindows)` → `if (!_windows)`;`else` 分支拆成 `else if (head.SequenceEqual(MagicAes)) → AesGcmDecrypt(body)`,
   原来的 `TCS0` 分支变成最后的 `else`(逻辑不变)。
7. `WriteAll`:`if (IsWindows)` → `if (_windows)`;新增 `else if (_keyStore != null)` 分支写 `TCS2`;
   并把 `Directory.CreateDirectory(...)` 从方法开头挪到**加密之后、落盘之前**(这样凭据库失败时磁盘上不留任何东西)。
8. 新增 4 个私有方法:`MasterKey()`、`AesGcmEncrypt(byte[])`、`AesGcmDecrypt(byte[])`(放在 `DpapiProtect` 之后;
   `DpapiProtect` / `DpapiUnprotect` / `Xor` 一字未动)。

没有删任何既有逻辑、没有改 `TCS0` / `TCS1` 的读写行为、没有动 `Get` / `Set` / `Names` / `DeleteAll`。

### 新引的包

- `SkiaSharp` 2.88.9(→ `src\TipCL.Core`,版本取自 `Directory.Build.props` 的 `TipCLSkiaSharpVersion`)。
  必须与 Avalonia 11.3.23 解析到的 SkiaSharp 一致(见 DECISIONS D-34)。已在 NuGet 缓存里,restore 未联网。
- 没有引 ImageSharp / WPF / WinForms / WebView2;`NoWarn` 未动。

### Skia 与 WPF 语义对照表

| 项 | WPF(`WpfImageCodec` / `WpfDecodedImage`) | Skia(本包) | 差异 |
|---|---|---|---|
| 支持的格式 | WIC 能认的(PNG/JPEG/GIF/BMP/TIFF/ICO…),WebP 先经 `WebpFallback` 转 PNG | Skia 的 PNG/JPEG/WebP/GIF/BMP…(实测这 5 种都解得出) | Skia 不需要 `WebpFallback`(那是 Win7 没有 WIC WebP 的补丁) |
| `Decode(null)` | `ArgumentNullException("data")` | 相同 | 无 |
| 认不出格式 | `InvalidDataException("cannot identify image file")` | 同一条消息(`SKCodec.Create` 返回 null 或尺寸非法) | 无 |
| 认得出但解不出像素 | WIC 抛自己的异常 | `InvalidDataException("cannot decode image data (结果码)")` | 新增的第二种消息形态(实测:截断的 PNG 会走到这里) |
| `HasAlpha` | `BitmapSource.Format` ∈ {Bgra32/Pbgra32/Rgba64/Prgba64/…} 或调色板里有 A<255 | `SKCodec.Info.AlphaType != Opaque`(实测 `ColorType` 恒 `Bgra8888`) | 「RGB + tRNS」这一种:WPF=false、Skia=Unpremul→true;其余实测一致(含「有 alpha 通道但像素全不透明」= true) |
| EXIF 方向 | 读 WIC 元数据 `/app1/ifd/{ushort=274}` 后按 1..8 变换;PNG 的 eXIf 也能读 | `SKCodec.EncodedOrigin`:JPEG EXIF 生效;**PNG eXIf 在 2.88.9 上恒 `TopLeft`(不生效)** | PNG 的 eXIf 少一次摆正(有用例钉住这个事实) |
| 解码后的像素格式 | Bgra32/Pbgra32;无 alpha 的图转 Bgr24 | Bgra8888/Premul;无 alpha 的图转 Rgb888x/Opaque | 一一对应;实测 Skia 只能解到 N32(要 `Rgb888x` 目标会 `InvalidConversion`),所以是「解完再转」 |
| `Resize` 插值 | `BitmapScalingMode.HighQuality` | `SKFilterQuality.High` | 各自的高质量档,像素级结果不保证逐位相同(尺寸与 alpha 语义一致) |
| `Resize` 后 | 无 alpha → 再转 Bgr24 | 无 alpha → 保持 Rgb888x | 一致(重编码 PNG 的 IHDR color type 都是 2) |
| `Resize(0/负数)` | WPF 的 `RenderTargetBitmap` 抛 `ArgumentException` | 显式抛 `ArgumentException` | 无(语义对齐,消息不同) |
| `EncodePng` | `PngBitmapEncoder`,不隔行 | `SKImage.Encode(Png, 100)` | 都是 PNG;带 alpha → color type 6,不带 → 2 |
| `EncodeJpeg` 的透明像素 | 先 `FormatConvertedBitmap(...Bgr24...)`,**不铺底色** | 先转 `Bgra8888` 再交给 Skia,**不铺底色** | premul→opaque 的具体取色规则不同,只对 `HasAlpha=true` 的图有像素差;`Images._shrink` 只在 `HasAlpha=false` 时走 JPEG |
| `EncodeJpeg(quality)` | `Math.Max(1, Math.Min(100, q))` | 同(抽成 internal `ClampQuality`,单测 6 个边界) | 无。另实测 `Rgb888x` 源编 JPEG 返回 null → 先转 `Bgra8888` |
| 线程 | 解码/缩放/编码都跑在 STA 线程(`RunSta`) | 直接在当前线程 | WPF 是 `BitmapSource` 的线程亲和性要求;Skia 位图没有,不需要 |
| `Dispose` | 丢掉 `BitmapSource` 引用 | `SKBitmap.Dispose()`(可重复调) | Dispose 后再读 `Width` 两者都会抛 |

### Keychain 与 WinCred 语义对照表

| 操作 | Windows `WinCredSecretStore` | `MacKeychainSecretStore` |
|---|---|---|
| 读不到 | `GetPassword` 返回 **null**(`ERROR_NOT_FOUND` 1168) | 同(退出码 44) |
| 删除不存在的 | 抛 `SecretNotFoundException(service)` | 同 |
| 其它失败 | `OSError("(码, 'CredRead', '消息')")` | `OSError("(码, 'security find-generic-password', 'stderr')")`,`stderr` 为空时 `no stderr` |
| 写 null 密码 | 落成字面量 `"None"` | 同(照抄这个怪癖以对齐语义) |
| 服务名回退 | service 找不到时再试 `username@service` 复合名 | **无**(命令行按任务书固定三条;mac 上也没有旧数据要兼容) |
| 存储编码 | UTF-16LE blob(读时先按 BOM/UTF-16 解,失败再试 UTF-8) | `b64:` + UTF-8 Base64(**读时兼容别的工具写的裸值**) |
| 运行器 | 直接 `DllImport advapi32` | 注入的可运行器委托 + argv 级 `ProcessStartInfo`(单测只用假运行器,一次进程都不起) |

### 测试(新增 114 条,总数 652 → 766)

| 文件 | 用例 | 覆盖 |
|---|---|---|
| [B1aKeychainTests.cs](tests/TipCL.Tests/B1aKeychainTests.cs) | 39 | 三条命令行逐项断言(含空格 / 引号 / 中文 / 以 `-` 开头的值 / 空串)、`b64:` 往返(中文 / 换行 / 空串 / 5600 字符)、裸值直读、末尾换行、44 码、其它码抛 `OSError`、**异常消息里不含密码**、打码、调用次数与参数、UTF-8 而非 UTF-16、构造不起进程 |
| [B1aSkiaCodecTests.cs](tests/TipCL.Tests/B1aSkiaCodecTests.cs) | 44 | 现造 PNG(带 / 不带 alpha,含「有通道但全不透明」)、JPEG、WebP(带 / 不带 alpha)、1x1 透明 GIF、手工 BMP、真实 webp 夹具(128×128)、尺寸与 `HasAlpha`、`Resize` 尺寸 / alpha / 非法尺寸、`EncodePng` 的 PNG 魔数与 IHDR color type、`EncodeJpeg` 的 SOI、编码后再解码、`ClampQuality` 边界、垃圾字节与截断 PNG 抛异常、**8 个 EXIF 方向逐像素对拍**、PNG eXIf 不生效这一事实、`Dispose` 幂等 |
| [B1aPluginSecretStoreAesTests.cs](tests/TipCL.Tests/B1aPluginSecretStoreAesTests.cs) | 19 | `TCS2` 文件头与布局、Set/Get/Names/DeleteAll 往返、两次写入密文不同(nonce 随机)、改一个字节 / 改魔数 → 读不出来、换主密钥读不出来、主密钥只生成一次且名字与长度正确、坏密钥不覆盖且报错、无凭据库仍写 `TCS0`、旧 `TCS0` 仍可读、`TCS1` 非 Windows 读不出来、**凭据库失败时 Set 抛异常且磁盘上不留任何文件**、公开构造函数按平台走 |
| [B1aImagesCodecTests.cs](tests/TipCL.Tests/B1aImagesCodecTests.cs) | 6 | 接上真编解码器后 `Images.EncodeBytes` 把 400×200 缩到 64×32(不透明走 JPEG、带 alpha 走 PNG),`ImageCodec` 为 null 时原样返回,垃圾输入抛 `ImageError`;`PlatformServices.ImageCodec` 在 `finally` 里恢复 |
| [B1aPlatformBootstrapTests.cs](tests/TipCL.Tests/B1aPlatformBootstrapTests.cs) | 6 | `CreateSecretStore(true/false)`、`CreateImageCodec()`、`Init(true/false)` 与 `Init()` 的赋值结果、不碰数据目录;`PlatformServices` 的静态在 `finally` 里恢复 |

### 验证命令与实际结果(全部在本机真实执行过)

| 检查 | 命令 | 结果 |
|---|---|---|
| 开工基线 | `Get-ChildItem %LOCALAPPDATA%\TipCL -Recurse` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16;`%TEMP%` 下 `tipcl*` 目录 **78** 个 —— 与 Lead 给的基线一致 |
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;警告 8 个 = `CA1416 ×7` + `SYSLIB0014 ×1`(与 A6 基线逐项相同,本包没有新增) |
| `dotnet test` 通道 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,**通过 766 / 失败 0 / 跳过 0**(2 秒,本机这次可用,没触发 D-05 的沙箱限制) |
| 进程内执行器 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0,**766 / 0 / 0**,**不支持 0**;与 `dotnet test` 逐数一致 |
| 新增用例数 | `dotnet exec ... \| 按 B1a* 类名计数` | 39 + 44 + 19 + 6 + 6 = **114**;652 + 114 = 766 |
| 冒烟(接线后画面未变) | `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` / `--theme dark ...` | 退出码 0 / 0;**3739 字节 / 3751 字节**(与 A1/A6 相同) |
| 收尾元信息 | 同开工基线命令 | 9328 / 889530606 / 2026-11-10 20:42:16(**三项全等**);`%TEMP%` 下 `tipcl*` 目录 **78 → 78**(没增加) |
| 残留进程 | `Get-Process testhost,java,adb,TipCL` | 各 **0** 个(另查 `security` 也是 0) |
| 字节一致性自检 | `src\TipCL.Core\**\*.cs` 对 `windos\tipcl\src\TipCL\Core\` 同相对路径比 SHA256 | 总 **242** / 一致 **234** / 有改动 **4** / 无对应 **4**。有改动 = A5 的 3 个(`Plugins\DotNetPluginContext.cs`、`Plugins\Sdk\PluginSdk.cs`、`Plugins\Sdk\PluginUiRegistry.cs`)+ 本包的 `Plugins\SecretStore.cs`;无对应 = 本包新建的 4 个平台实现(`Platform\Mac\MacKeychainSecretStore.cs`、`Platform\PlatformBootstrap.cs`、`Platform\Skia\SkiaImageCodec.cs`、`Platform\Skia\SkiaDecodedImage.cs`) |

补充实测(用 `artifacts\probe` 的一次性探针项目量的,不入库):`AesGcm.IsSupported = true`(本机 Windows)、
SkiaSharp 2.88.9 的 `SKCodec.Info.ColorType` 恒为 `Bgra8888`、JPEG 的 EXIF 方向生效、PNG 的 eXIf 不生效、
`Rgb888x` 目标解码报 `InvalidConversion`、`Rgb888x` 源编 JPEG 返回 null。

### 未验证项(本机是 Windows,没有 Mac)

- **Keychain 的一切真机行为**:`security` 的退出码 44、`find-generic-password -w` 对非 ASCII 的实际输出形态、
  `add-generic-password -U` 的更新语义、`-w` 后面带空串 / 以 `-` 开头的值是否被 getopt 当成参数、
  `/usr/bin/security` 的路径与可用性。**命令行拼装与输出解析只有单测,没有真机跑过**。
- **AES-GCM 在 mac 上是否 `IsSupported`**(本机 Windows 实测 true;不支持时按 D-32 抛 `PlatformNotSupportedException`)。
- Keychain 主密钥在真机上的存取往返(单测用的是假凭据库)。
- 桌面启动路径(会开窗口)没有跑;`--smoke` 两条路径都跑了,`PlatformBootstrap.Init()` 在分支之前共用同一行。
- mac 上 Skia 原生库的加载、Skia 在 mac 上解 WebP、mac 上 `--smoke` 渲染,全部未验证。

### 遗留与风险

- **写入时密码会短暂出现在 `security` 的 argv 里**(同机其他用户可用 `ps` 看到);备选 `security -i` 因为
  转义规则本机无法验证而没有采用。见 DECISIONS D-31。
- **PNG 的 eXIf 方向在 SkiaSharp 2.88.9 上不生效**(JPEG EXIF 正常)。升级 Skia 时要复核这条;
  上下界都不影响 `Images._shrink`(它只做缩放与重编码)。
- `security` 失败时的 stdout 没有纳入 `OSError` 消息(只带 stderr);stderr 为空写成 `no stderr`。
- 主密钥坏掉(非 Base64 / 长度不对)时**故意报错而不换密钥**,避免把既有文件变成永久读不出来。
- Windows 写出的 `TCS1`(DPAPI)文件在 mac 上仍然读不出来(与 A5 记录一致,本包没有做迁移)。
  mac 上第一次写入会生成 `TCS2` + Keychain 主密钥,不需要迁移路径。
- `MacKeychainSecretStore` 不做 Windows 的「`username@service` 复合名回退」;mac 上没有旧数据要兼容。
- 本包**没有**做 `ShellOpen` / 打开浏览器 / 去提权 / `NodeRuntime` darwin 那些运行时差异(属 A22 与 B1b / B2)。
- 工具陷阱记录:D-36(PowerShell 5.1 的 `Get-Content | Set-Content` 改坏中文,两个新建测试文件已重写)。

### Lead 验收(复跑于 2026-11-12 09:10,结论落盘于 18:30)

结论:**B1a 通过,带一条必须在 B1b 修的缺陷**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 234 一致、4 个有改动(A5 的 3 个 + 本包 `Plugins\SecretStore.cs`);无对应文件的 4 个全是本包新建的平台实现 |
| `git diff b57f0c1..HEAD -- src/TipCL.Core/Plugins/SecretStore.cs`(Lead 逐行读过) | +115 / −3:新增 `TCS2` 分支、主密钥、`internal` 注入构造;公开构造签名未变,Windows 仍写 `TCS1`,无凭据库仍写 `TCS0` |
| `MacKeychainSecretStore.cs`(Lead 全文读过) | 走 `/usr/bin/security` 绝对路径,参数用 `ArgumentList` 不经 shell;写入值恒为 `b64:` 前缀;异常消息不带密码 |
| `PlatformBootstrap.cs` + `Program.cs` | 只赋值;凭据库仅 mac 接 Keychain,其它平台为 null |
| 仓库内是否出现 `Cred*` API / `WinCredSecretStore` 实现 | 没有(只在注释里提到名字) |
| SkiaSharp 解析版本(App / Core / Tests) | 三处都是 2.88.9 |
| WebP 夹具 | 与 Windows 测试目录里的源文件 SHA256 一致 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法,没有 U+FFFD 或连串问号(D-36 的损坏确实已修) |
| 红线文件 `AssemblyInfo.cs`、`TestHomeIsolation.cs`、`Program.cs`(测试)、`.gitattributes` | 未改 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = CA1416 ×7 + SYSLIB0014 ×1,无新增 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 766 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 766 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试前后,以及 18:24 再量) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录数(Lead 复跑前后) | 78 → 78 |
| 残留进程 `testhost` / `java` / `adb` / `TipCL` / `security` | 0 个 |
| B1a 测试扫描 | 没有起进程、联网、改环境变量、实例化后端;改 `PlatformServices` 的用例都在 `finally` 里恢复;唯一一处 `new MacKeychainSecretStore()`(默认运行器)只构造不调用 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

**缺陷(Lead 读代码发现,根因在 Lead 的任务书,不是子代理没照做)**:

- `SecretStore.ReadAll()` 的 `catch (Exception)` 把**凭据库调用失败**也当成「文件读不出来」返回空字典。
  `Set(name, value)` 的流程是先 `ReadAll()` 再 `WriteAll()`:如果读的那一次 Keychain 调用失败(钥匙串被锁、用户在授权弹窗点了拒绝),
  而紧接着写的那一次成功,文件会被**只含新条目的内容覆盖**,该插件原有的其它密钥全部丢失。
  同理 `Get()` 在 Keychain 失败时返回 null,调用方分不清「没存过」和「暂时读不到」。
- 任务书只规定了「写入时 Keychain 失败不许降级」,没规定读取。B1b 修:`TCS2` 文件在取主密钥阶段的失败必须抛出,
  只有「解密认证失败 / 内容不是合法 JSON」才按读不出来处理。
- 附带:读取一个 `TCS2` 文件时如果 Keychain 里没有主密钥,当前实现会**生成并写入一把新密钥**(读操作产生写副作用)。B1b 一并改成读取路径不创建密钥。

其余 Lead 认可的取舍:D-31(写入时值出现在 `security` 的 argv)、主密钥损坏时报错而不换钥、`PlatformBootstrap` 放在 Core。

给 B1b 的事实(Lead 实测):

- `Terracotta.AllowFirewall()`(571 行)第 573 行已有 `if (!Utils.IS_WINDOWS)` 守卫,提权脚本在 mac 上走不到,**不需要改**。
- `Crash.ShellOpen`(1899 行)已有 `open` / `xdg-open` 分支和 `ShellOpenHook`,**不需要改**;`Ai\ExtraTools.cs:801` 没走它,直接起 `explorer.exe`。
- `GlobalMods._link_file`(26–53 行)无条件调 `CreateSymbolicLinkW`,失败后退化成 `File.Copy`。
- `Sysinfo._winreg_value`(149 行)用 `Utils.IS_WINDOWS` 做守卫,分析器不认,产生 3 条 CA1416。
- 代码页:全仓库没有调用 `Encoding.RegisterProvider`;`LauncherPureTests.cs:296` 的用例名是 `Cp437_is_available_or_silently_falls_back_to_utf8`。

---

## B1b 运行时差异修复(SecretStore 读取路径 / 代码页 / 打开文件夹 / 软链接 / CA1416)

> 起点 `a9ca20d`,工作树干净,766/766 全绿;收尾 811/811 全绿,警告 **CA1416 ×7 + SYSLIB0014 ×1 → SYSLIB0014 ×1**。
> 本包只做任务书点名的 5 项,没有顺手做 B2 的内容。

### 1. 插件 SecretStore 读取路径(B1a 验收时发现的缺陷,最优先)

**缺陷**:`ReadAll()` 整体包在 `catch (Exception)` 里,凭据库调用失败(钥匙串被锁 / 用户拒绝授权)被当成
「文件读不出来」返回空表;而 `Set(name, value)` 是**先 `ReadAll()` 再 `WriteAll()`** —— 读失败而紧接着写成功时,
文件会被「只含新条目」的内容覆盖,该插件原有的其它密钥全部丢失;`Get()` 也把「暂时读不到」说成「没存过」。
附带:读 `TCS2` 时若凭据库里没有主密钥,旧实现会生成并写入一把新密钥(读操作产生写副作用)。

**改法**(`src\TipCL.Core\Plugins\SecretStore.cs`):
- 主密钥取用拆成两种:`MasterKey()` = 写入用(没有才创建);新增 `ReadMasterKey()` = 读取用
  (**没有就返回 null,不创建**);Base64 / 32 字节校验抽成 `DecodeMasterKey(string)`,坏掉抛
  `InvalidDataException` 且不覆盖已有条目(消息与 B1a 相同)。
- `ReadAll()` 重写:**TCS2 分支不再有 catch-all**。
  - `AesGcm.IsSupported == false` → 抛 `PlatformNotSupportedException`(不当空表);
  - 密文长度 < 12 + 16 → 空表(判定放在取主密钥**之前**,免得为一个读不出来的文件去碰凭据库);
  - 取主密钥阶段的异常(凭据库调用失败 / 主密钥坏 Base64 / 长度不是 32)→ **原样抛给调用方**;
  - 凭据库里没有主密钥 → 空表(不创建密钥);**没有可用凭据库 → 抛 `PlatformNotSupportedException`**(见 D-37);
  - 解密认证失败(密文被改 / 换了主密钥)→ 空表;内容不是合法 UTF-8 / JSON → 空表;
  - `File.ReadAllBytes` 失败(被占用 / 权限)→ 空表(原有语义)。
  - `TCS0` / `TCS1` / 未知魔数 / 文件太短的现有行为**一字未改**。
- `AesGcmDecrypt(byte[] body, byte[] key)`:改成接收密钥,不再自己取。
- **效果**:凭据库失败时 `Get` / `Set` / `Names` 抛异常,磁盘上的文件**逐字节不变**(回归用例钉住)。

**`SecretStore` 的 `Get` / `Names` / `Set` 调用点清单**(`src\TipCL.Core\Plugins\`;**调用方本身未改,只记录**):

| 文件:行 | 调用 | 凭据库异常时的结果 |
|---|---|---|
| `BackendApi.Plugins.cs:213` | `m.HostSecrets.Get(...)`(`plugins_providers`) | 异常抛出 → 该次 `plugins_providers` 整体失败;不再把「读不到」说成 `configured:false` |
| `BackendApi.Plugins.cs:242` | `m.HostSecrets.Set(keyRef, apiKey)`(`plugins_set_provider`) | 抛出 → 调用失败。注意前面 `State.UpsertProvider` 已改内存状态,会留下「记录已改、密钥没写」的部分成功(原有顺序,本包不改) |
| `BackendApi.Plugins.cs:252` | `m.HostSecrets.Set("provider:"+pid, null)`(`plugins_remove_provider`) | 抛出 → 同上(State 已 `RemoveProvider`)。不会删错、不会覆盖别的密钥 |
| `PluginManager.cs:1678` | `p.Secrets.Set("config:"+key, v)`(`SetConfig`) | 抛出 → 中止:配置值没写,Node 的 `plugin.configChanged` 与 `config.changed` 都不发 |
| `PluginManager.cs:1712` | `p.Secrets.Set(name, value)`(`SetCredential`) | 抛出 → 中止,不 `Publish("credentials.updated")` |
| `PluginManager.cs:1721` | `new HashSet<string>(p.Secrets.Names(), ...)`(`CredentialRows`) | 抛出 → 凭据列表读取失败(UI 看到 RPC 错误),不再把「有凭据」显示成「未配置」 |
| `PluginHostApi.cs:182` | `p.Secrets.Get(name)`(`credentials.resolve`) | 抛出 → `Invoke` 里没有 catch,带 id 的请求由 RPC 层转成错误响应;插件拿到错误而不是 `value=null` |
| `PluginLlm.cs:49` | `_mgr.HostSecrets.Get(...) ?? ""`(`SettingsFor`) | 抛出 → 不再走 `key.Length == 0` 的 `not_configured`,插件看到真实错误 |

**没有任何调用点把它 catch 成「密钥为空」后继续往磁盘写**;唯一 catch-all 是
`PluginHostApi.HandleNotification`(无 id 的通知,ThreadPool)那条路径本来就不回结果。

### 2. 代码页(A3 清点表 E1–E5)

- 新文件 `src\TipCL.Core\Platform\CodePages.cs`:`EnsureRegistered()` 幂等(双检锁)调
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`。
- **不需要引包**:`System.Text.Encoding.CodePages.dll` 在 net8.0 共享框架里
  (本机 `C:\Users\Administrator\.dotnet\shared\Microsoft.NETCore.App\8.0.33\`),实测直接编译通过 →
  **没有改任何 csproj、没有动 NoWarn**(见 D-37)。
- `PlatformBootstrap.Init(bool)` 第一句调它,类注释同步更新(注册是纯内存操作,不做 I/O)。
- 测试工程新增的 `B1bCodePagesTests.cs` 里**自带 `[ModuleInitializer]`**(`B1bCodePagesModuleInit`)调注册 ——
  不动 `TestHome.Init()`、不动静态隔离;这样 `Utils.Cp437` 的静态缓存不会被 UTF-8 回落先占住,
  用例执行顺序不再影响结果。
- `Utils.cs` / `Java.cs` / `Crash.cs` / `NetResponse.cs` / `Terracotta.cs` 取编码的代码**一行未改**。
- 类库里**没有**用 `[ModuleInitializer]`(会触发 CA2255)→ 没有新增构建警告。
- E5(`Terracotta.GbkBytes`)是 private,唯一调用点 `AllowFirewall` 要管理员权限 + 写防火墙规则 →
  **没有用例**,注册后它的 try 分支自然会拿到 936(结论来自代码阅读)。

### 3. AI 工具「打开文件夹」不再写死 `explorer.exe`

`src\TipCL.Core\Launcher\Ai\ExtraTools.cs`:`OpenFolder` 里「目录已确定之后的打开动作」抽成
`internal static string OpenFolderNow(string dir, bool isWindows)`,`OpenFolder` 调它;
`isWindows` 时**保持原来那一行** `Process.Start("explorer.exe", "\"" + dir + "\"")`,非 Windows 走
`Crash.ShellOpen(dir)`(它自带 mac `open` / Linux `xdg-open` 与 `ShellOpenHook`);
成功 / 失败两种文案一字未改(`已打开 ` / `打不开:` —— 全角冒号 U+FF1A,测试里用 `\uFF1A` 写死,见 D-24)。
测试**只测 `isWindows: false`**,并且先用 `Crash.ShellOpenHook` 拦截、`finally` 恢复,**绝不真起进程 / 开窗口**。

### 4. 全局模组软链接(A3 清点表 D2)

`src\TipCL.Core\Launcher\GlobalMods.cs`:公开重载 `_link_file(dest, src)` 签名不变,内部转调新的
`internal static bool _link_file(string dest, string src, bool isWindows, Func<string,string,bool> link)`;
Windows 分支仍是 `CreateSymbolicLinkW(text, text2, 2)`,非 Windows 用委托
(默认 `CreateSymbolicLinkDefault` = `File.CreateSymbolicLink(链接路径, 目标路径)`,Unix 上走 `symlink(2)`);
建链接失败(抛异常或返回 false)后回退 `File.Copy` + 复制时间戳的兜底逻辑一字未改。
- `IsSymlink`(13–24 行)复核结论:`.NET` 的 Unix 文件状态把符号链接标成 `FileAttributes.ReparsePoint`,
  所以非 Windows 下它对 `File.CreateSymbolicLink` 建出来的链接判断**正确** → 不改。
  唯一边角:悬空链接(目标不存在)在 Unix 上会走 `File.GetAttributes` 抛异常 → catch 返回 false,
  与 Windows 略有差别,**留给 B2 真机复核**。
- 测试**不真的创建符号链接**(Windows 上要特权),用假委托验三种情况;公开重载在本机走 Windows 分支,故意不测。

### 5. CA1416 清理(没有扩 NoWarn)

- `Launcher\Sysinfo.cs` 的 `_winreg_value`:`if (!Utils.IS_WINDOWS)` → `if (!OperatingSystem.IsWindows())`。
  语义等价已核对源码:`Utils.IS_WINDOWS = OS_NAME == "windows"`,而 `OS_NAME` 来自
  `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`(Utils.cs:347-358)→ **3 条 CA1416 全消**。
- `Plugins\SecretStore.cs` 的 DPAPI 两个方法标 `[SupportedOSPlatform("windows")]`,调用点加分析器认识的判断:
  - 读路径:`if (!_windows || !OperatingSystem.IsWindows()) return new PyDict();`(`_windows` 注入标志语义不变);
  - 写路径:`if (_windows) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(...); ... }`
    —— 只有单测能造出「说自己是 Windows 但实际不是」的组合,行为与 DPAPI 在非 Windows 上原来抛的
    `PlatformNotSupportedException` 一致,Windows 上不触发 → **4 条 CA1416 全消**。

**警告前后对比**(同一条 `dotnet build ... -p:UseSharedCompilation=false -m:1 -v:q -clp:Summary`):

| | CA1416 | SYSLIB0014 | 合计 |
|---|---|---|---|
| 改动前 | 7(Sysinfo ×3、SecretStore ×4) | 1(PhoneClient.cs:116) | **8** |
| 改动后 | **0** | 1(不在本包范围) | **1** |

### 对复制文件的每处改动(共 4 个)

| 文件 | 位置 | 原来 | 改成 | 为什么 |
|---|---|---|---|---|
| `Plugins\SecretStore.cs` | `ReadAll()` | 整体 `try/catch (Exception)` → 空表 | TCS2 分支:IsSupported=false 抛、截断 → 空表、取主密钥异常上抛、没有主密钥 → 空表、认证失败 → 空表;文件读取失败与 JSON 坏 → 空表 | 修「读失败 + 写成功 → 覆盖丢密钥」的缺陷 |
| 同上 | `MasterKey()` 及其内联校验 | 方法内 try/catch(FormatException) + 长度判断,没有就创建 | 新增 `ReadMasterKey()`(读取用,不创建)与 `DecodeMasterKey(string)`;`MasterKey()` = `ReadMasterKey()` + 没有就创建 | 读 / 写两种语义分开;读操作不再产生写副作用 |
| 同上 | `AesGcmDecrypt(byte[] body)` | 内部自己调 `MasterKey()` | `AesGcmDecrypt(byte[] body, byte[] key)` | 让读取路径先判 IsSupported / 截断,再取密钥 |
| 同上 | DPAPI 两个方法 | 只有 `[MethodImpl(NoInlining)]` | 加 `[SupportedOSPlatform("windows")]` | 消 CA1416(调用点已加 `OperatingSystem.IsWindows()` 判断) |
| `Launcher\Ai\ExtraTools.cs` | `OpenFolder`(784–808 行) | 无条件 `Process.Start("explorer.exe", ...)` | 抽出 `internal static OpenFolderNow(dir, isWindows)`:Windows 保持原行,非 Windows 走 `Crash.ShellOpen` | mac 上没有 explorer.exe;复用 Crash 已有分支与钩子 |
| `Launcher\GlobalMods.cs` | `_link_file`(26–56 行) | 无条件 `CreateSymbolicLinkW` | 加 `internal` 重载(是否 Windows + 建链接委托);非 Windows 用 `File.CreateSymbolicLink` | mac 上 `DllNotFoundException` 被吞 → 静默退化成复制文件 |
| `Launcher\Sysinfo.cs` | `_winreg_value`(149–175 行) | `if (!Utils.IS_WINDOWS)` | `if (!OperatingSystem.IsWindows())` | 消 3 条 CA1416,语义等价 |

除这 4 个之外**没有改任何复制来的文件**;`Platform\PlatformBootstrap.cs`(B1a 新建、Windows 源里没有)加了 1 行调用 + 注释。

### 测试(新增 45 条用例,总数 766 → 811)

| 文件 | 覆盖 | 用例数 |
|---|---|---|
| `tests\TipCL.Tests\B1bSecretStoreReadPathTests.cs` | 第 1 项(缺陷回归:抛异常 + 文件逐字节不变 + 读不创建密钥) | 16 |
| `B1bCodePagesTests.cs` | 第 2 项(E1–E4,含自带 ModuleInitializer) | 16 |
| `B1bOpenFolderTests.cs` | 第 3 项(只测 `isWindows: false`,先拦 `ShellOpenHook`) | 4 |
| `B1bGlobalModsLinkTests.cs` | 第 4 项(假委托,不真建链接) | 6 |
| `B1bSysinfoTests.cs` | 第 5 项(只读注册表) | 3 |

- **B1a 的 19 条 `B1aPluginSecretStoreAesTests` 一字未改、全部继续通过**:它们里没有
  「TCS2 + 凭据库失败 → 空表」之类的期望,本次语义变化不需要调整任何一条(逐条复核过)。
- 新用例不起进程、不联网、不实例化 `BackendApi`;改 `Crash.ShellOpenHook` 的用例都在 `finally` 里恢复;
  临时文件全在 `TestHome.Root` 下。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build-server shutdown` | 退出码 **0**(这次两个服务器都成功关闭) |
| 2 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:Summary` | 退出码 **0**,**0 错误 / 1 警告**(SYSLIB0014,PhoneClient.cs:116;CA1416 从 7 → 0) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,`失败: 0,通过: 811,已跳过: 0,总计: 811`(791 ms,没有 Win32Exception (5)) |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,`总计 811 个用例,通过 811,失败 0,跳过 0,不支持 0`,末行 `in-proc 测试全绿` |
| 5 | `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**,**3739 字节**,SHA256 `17F3CD87…017BF`(与 A1 / Lead 验收一致) |
| 6 | `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts\smoke-dark.png"` | 退出码 **0**,**3751 字节**,SHA256 `374716FF…38144`(与 A1 / Lead 验收一致) |
| 7 | `%LOCALAPPDATA%\TipCL` 元信息(开工 / 测试跑完后再量) | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,前后完全一致,与 Lead 基线一致 |
| 8 | `%TEMP%` 下 `tipcl*` 目录**名字集合**(两条通道各跑前后各一次) | 85 → 85 与 85 → 85,**两次差集都是空**(测试窗口内没有新目录) |
| 9 | 残留进程 `testhost` / `java` / `adb` / `TipCL` / `security` | 全部 **0 个**;`explorer` 只有 1 个 PID 9920(22:30:11 启动 = 用户自己的桌面,本包没有弹过资源管理器窗口) |
| 10 | 字节一致性自检:`src\TipCL.Core\` 下 **243** 个 `.cs`(不含 bin/obj)对 Windows 源比 SHA256 | **一致 231 / 有改动 7 / Windows 源里没有对应文件 5**;改动 7 = A5 的 `Plugins\DotNetPluginContext.cs`、`Plugins\Sdk\PluginSdk.cs`、`Plugins\Sdk\PluginUiRegistry.cs` + `Plugins\SecretStore.cs` + 本包 `Launcher\Ai\ExtraTools.cs`、`Launcher\GlobalMods.cs`、`Launcher\Sysinfo.cs`;无对应 5 = B1a 的 `Platform\Mac\MacKeychainSecretStore.cs`、`Platform\PlatformBootstrap.cs`、`Platform\Skia\SkiaDecodedImage.cs`、`Platform\Skia\SkiaImageCodec.cs` + 本包 `Platform\CodePages.cs` |
| 11 | 本次改动的 11 个文本文件严格 UTF-8 解码 + U+FFFD 扫描 | 全部合法、**0 个 U+FFFD**、无 BOM(与仓库既有文件一致) |

### 未验证项(本机是 Windows,没有 Mac)

- mac 上 `File.CreateSymbolicLink` / `IsSymlink` 的真实表现(含悬空链接)未验证;
- mac 上 `Crash.ShellOpen` 真的调 `open` 打开 Finder 未验证(本包只到 `ShellOpenHook` 这一层);
- mac 上 `AesGcm.IsSupported` 未验证(本机 Windows 实测 true,由 B1a 用例钉住);
- `CodePagesEncodingProvider` 在 mac 上的注册结果未验证(本机验证的是 net8.0 共享框架里确实有这个程序集,
  且注册后 437 / 936 / 54936 / gbk / gb2312 / gb18030 都能取到、`Utils.Cp437` 拿到真 437);
- 真机 Keychain 被锁 / 用户拒绝授权时抛出的异常形态未验证(本包用假凭据库模拟 `OSError`)。

### 遗留与风险

- **TCS2 + 没有可用凭据库**时读会抛 `PlatformNotSupportedException`(不再是空表)。这是保守选择:
  返回空表会让 `Set` 在「读不出来」之后把文件覆盖成 TCS0,正是本次要修的数据丢失路径。
  代价是「没接线凭据库的进程读 TCS2」会看到异常;mac 上 `PlatformBootstrap.Init()` 必定接 Keychain,不受影响(见 D-37)。
- 写路径的 **TCS0 兜底没变**(D-32):没有凭据库时 `Set` 仍写 TCS0 明文 + 混淆,本包不碰。
- `plugins_set_provider` / `plugins_remove_provider` 在 `Set` 抛异常时会留下「内存状态已改、密钥没写」的部分成功
  (调用顺序是原有的),本包只记录不改。
- 覆盖 `Crash.ShellOpenHook` 的用例依赖全程序集串行(`AssemblyInfo.cs` 已关并行),不与其他用例互相干扰;
  `B1bOpenFolderTests.The_hook_is_restored_after_the_test` 会连带盯住「谁忘了在 finally 里恢复钩子」。
- `Crash.ReadText` 的 GB18030 回落现在真的能拿到 54936;这条路径是崩溃日志解析,没有别的副作用。

### Lead 验收(2026-11-12 18:55)

结论:**B1b 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 231 一致、7 个有改动(A5 的 3 个 + `SecretStore.cs` + 本包 `ExtraTools.cs`、`GlobalMods.cs`、`Sysinfo.cs`);无对应文件的 5 个是 B1a 的 4 个 + `Platform\CodePages.cs` |
| `SecretStore.cs` 现在的读取路径(Lead 全文读过) | `TCS2`:取主密钥阶段的异常原样抛出;凭据库里没有主密钥、密文截断、认证失败、JSON 不合法 → 空表;读取不创建主密钥。`TCS0` / `TCS1` / 未知魔数 / 文件读失败的行为未变;Windows 分支未变 |
| `ExtraTools.cs`、`GlobalMods.cs`、`Sysinfo.cs` 的 `git diff a9ca20d..HEAD`(Lead 逐行读过) | 都只加了非 Windows 分支或可注入重载;Windows 分支仍是原来那一行;公开签名未变 |
| `Directory.Build.props` 与三个 csproj | 未改:`NoWarn` 仍 6 项,没有新包 |
| 红线文件 `AssemblyInfo.cs`、`TestHomeIsolation.cs`、`Program.cs`(测试)、`B1aPluginSecretStoreAesTests.cs`、`.gitattributes` | 未改 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只剩 SYSLIB0014 ×1(CA1416 从 7 条到 0 条) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 811 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 811 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 85 → 85,没有新名字 |
| `explorer.exe` 进程(Lead 复跑前后) | 都只有 PID 9920,没有新起的 |
| 残留进程 `testhost` / `java` / `adb` / `TipCL` / `security` | 0 个 |
| B1b 测试扫描 | 没有起进程、建真实符号链接、走 Windows 分支、写注册表、改环境变量;`Crash.ShellOpenHook` 的 3 处赋值都在 `finally` 里恢复 |
| 缺陷回归用例(Lead 读过 `B1bSecretStoreReadPathTests.cs` 前 200 行) | 「凭据库失败时 Get / Names / Set 抛出且文件逐字节不变」「第一次读失败、第二次成功时 Set 仍抛出且不写」两条都在,断言真实 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 认可的取舍:D-37(`TCS2` 文件 + 没有可用凭据库时读取抛 `PlatformNotSupportedException` 而不是空表,避免随后被覆盖成 `TCS0`)。

留给后面的包(只记录):

- `plugins_set_provider` / `plugins_remove_provider` 是「内存状态先改、密钥后写」,`Set` 抛异常时会部分成功。这是 Windows 版原有的调用顺序,C 阶段接插件页时处理。
- `GlobalMods.IsSymlink` 对悬空链接的判断留给 B2 在真机清单里核对。

---

## B2a mac Java 运行时获取(Mojang 平台键 / Adoptium·Zulu mac 包 / 可执行位与链接 / java 定位)

> 本包只改 **`src\TipCL.Core\Launcher\Java.cs`**(85 插入 / 32 删除)+ **新增 1 个 Core 文件**
> `src\TipCL.Core\Launcher\JavaRuntimeFiles.cs`,另加 5 个测试文件。
> 未改任何第二个从 Windows 复制来的文件;`Java.cs` 里每一处改动见下面第 4 节。

### 1. 第 0 步线上元数据(只读查询)

查询时间:**2026-11-12 19:01–19:02**(另有 18:58 的复现)。响应体留在 `artifacts/b2a/`(**不入库**)。
联网 GET 共计 **12 个请求**(1 个被第一次脚本自身的管道 bug 浪费,实际有效请求 11 个),全部只读元数据,
**没有下载任何 Java 压缩包或二进制**。

#### 1.1 Mojang 平台键(`all.json`,HTTP 200 / 13385 字节)

地址:`https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json`

| 顶层键 | 非空组件数 | 组件 |
|---|---|---|
| `mac-os` | 7 | alpha, beta, delta, epsilon, gamma, gamma-snapshot, jre-legacy |
| `mac-os-arm64` | **4** | delta, epsilon, gamma, gamma-snapshot(**没有 jre-legacy / alpha / beta**) |
| `windows-x64` | 8 | + minecraft-java-exe |
| `windows-x86` | 6 | |
| `windows-arm64` | 4 | 与 mac-os-arm64 同 4 个 |
| `linux` | 7 | |
| `linux-i386` | 1 | |
| `gamecore` | 0 | |

**清单里没有 `osx`、`osx-arm64`,也没有 `linux-arm64`** —— Windows 版写的 mac 键是错的,已修。

#### 1.2 文件清单形态(`mac-os-arm64` / `java-runtime-gamma` 与 `mac-os` / `jre-legacy`)

清单地址(从 all.json 取):`…/packages/e0c8d5ccfe8cfb38075ff1b7f6352d7b03fd7868/manifest.json`(97689 字节)、
`…/packages/78e31aa15b3f437f37263351436f63e0a96e0067/manifest.json`(77882 字节)。

| 统计项 | mac-os-arm64 / java-runtime-gamma | mac-os / jre-legacy |
|---|---|---|
| `files` 条目总数 | 437 | 179 |
| `type=file` / `directory` / `link` | 142 / 87 / **208** | 179 / 0 / **0** |
| `executable: true` | **29**(28 个在 `bin/`,1 个在 `lib/` = `lib/jspawnhelper`) | 77(含 60 个 `lib/`、11 个 `bin/`,另有几个明显是目录的条目) |
| `downloads.raw` / `downloads.lzma` | 142 / 135(**每个 file 都有 raw**,lzma 只是额外) | 179 / 142 |
| **java 的相对路径** | **`jre.bundle/Contents/Home/bin/java`** | **`jre.bundle/Contents/Home/bin/java`** |
| link 的 target 形态 | 208 条**全是相对路径**且都是 `../…`,0 条绝对路径、0 条逃出安装根;例:<br>`jre.bundle/Contents/Home/legal/java.compiler/ADDITIONAL_LICENSE_INFO -> ../java.base/ADDITIONAL_LICENSE_INFO`<br>`…/legal/java.compiler/ASSEMBLY_EXCEPTION -> ../java.base/ASSEMBLY_EXCEPTION`<br>`…/legal/java.compiler/LICENSE -> ../java.base/LICENSE` | 无 link |

结论:mac 的 Mojang 运行时**必须**处理 `link`(208/437 = 48% 的条目)和 `executable`(否则 `bin/java` 是 0644,起不来);
`directory` 条目 87 个,现有代码已在建。

#### 1.3 Adoptium(`api.adoptium.net/v3/assets/latest/{major}/hotspot?os=mac&architecture={arch}&image_type=jre`)

| major | arch | 结果 |
|---|---|---|
| 8 | **aarch64** | **0 个包**(响应体 3 字节 = `[]`) |
| 8 | x64 | 1 个:`OpenJDK8U-jre_x64_mac_hotspot_8u512b09.tar.gz` |
| 17 | aarch64 | 1 个:`OpenJDK17U-jre_aarch64_mac_hotspot_17.0.21_9.tar.gz` |
| 17 | x64 | 1 个:`OpenJDK17U-jre_x64_mac_hotspot_17.0.21_9.tar.gz` |
| 21 | aarch64 | 1 个:`OpenJDK21U-jre_aarch64_mac_hotspot_21.0.13_9.tar.gz` |
| 21 | x64 | 1 个:`OpenJDK21U-jre_x64_mac_hotspot_21.0.13_9.tar.gz` |

→ 包名后缀是 **`.tar.gz`**,与 `ArchiveSuffix()` 在非 Windows 上返回 `.tar.gz` 一致;
→ 代码里的 `_AdoptiumOs()`=`mac`、`_AdoptiumArch(arm64)`=`aarch64` 都被线上确认**正确**;
→ **唯一缺口:`major 8 + aarch64` 没有包**(见第 3 节)。

#### 1.4 Azul(`api.azul.com/metadata/v1/zulu/packages`)

| 请求 | 结果 |
|---|---|
| `java_version=8,17&os=macos&arch=aarch64,x64&archive_type=tar.gz&latest=true` | 44 条,**全部是 `macosx_x64`**;返回了 8…27 各版本 → **逗号多值语法不被支持**(`java_version` / `arch` 都没被当作筛选条件生效),这条只证明「macos + x64 + tar.gz 有包」 |
| `java_version=17&os=macos&arch=aarch64&archive_type=zip&latest=true` | 4 条,第一条 `zulu17.70.113-ca-fx-jre17.0.21.0.3-macosx_aarch64.zip` → **mac + aarch64 有包**,且 **`archive_type=zip` 真的给 `.zip`** |

→ 与上一格合起来正是本包要修的矛盾:代码请求 `archive_type=zip`、却把响应按 `ArchiveSuffix()` 落成 `.tar.gz`,
`DownloadManager.ExtractArchive` 又按**文件名后缀**选解压器 → mac 上拿 zip 的字节走 GZipStream,必然失败(见 4-8)。
→ **mac + aarch64 + `archive_type=tar.gz` 的组合没查到**(请求预算 12 用完),记在「未验证项」。

### 2. 现有「Mojang 缺组件 → 回退」链路(改前源码 `Java.cs` 1125–1235 / 1300–1370 行)

| # | 位置 | 发生什么 |
|---|---|---|
| 1 | `EnsureJavaForVersion` | 先 `PickJavaForVersion` 找已装的;找不到才装 |
| 2 | 同上 1311 | `version_json.javaVersion.component` **有值且 ≠ `jre-legacy`** → 走 Mojang;`jre-legacy` 或没有 component → 直接 Adoptium |
| 3 | `InstallMojangRuntime` 1135–1140 | 清单里没有 `PLATFORM_KEY` → `log.Warning("Mojang 运行时清单中没有平台 %s")` + **返回 null**(不抛) |
| 4 | 同上 1141–1145 | 平台在、但该平台没有这个 component(mac-os-arm64 的 `jre-legacy` / `alpha` / `beta`)→ **返回 null,无任何日志** |
| 5 | 同上 1146–1153 | manifest 里没有 url → 返回 null |
| 6 | 同上 1165 | manifest 下载失败 / sha1 校验失败 → **抛 DownloadError** |
| 7 | 同上 1232 | 装完找不到 java 可执行文件 → `log.Warning(...)` + 返回 null |
| 8 | `EnsureJavaForVersion` 1321–1324 | 上一步抛异常 → `log.Warning("Mojang 运行时下载失败,回退 Adoptium")` |
| 9 | 同上 1326–1327 | 无论哪条路到这一步,都调 **`InstallAdoptium(major, arch)`**;`arch` 只在「Windows + x64 + 老版本」时是 `x86`,其余为 null → `Utils.ARCH` |
| 10 | 没有第三级回退 | Adoptium 失败就是最后的失败(抛异常);**代码里没有「Adoptium 失败 → 换 Zulu」的逻辑**,Zulu 只在用户显式选厂商(`InstallJavaVendor`)时才用 |

**裁决执行结果**:按任务书「不新增改用 x64 `mac-os` 经 Rosetta 运行」的要求,本包**没有**加这种路径;
`mac-os-arm64` 缺组件时沿用现有链路(第 3/4 条 → 第 9 条)。
至于「改走另一个厂商」的选择逻辑:**没有加**,理由与线上证据见 D-39 与下面第 6 节「需要 Lead 裁决的事项」。

### 3. 四项各自做了什么

| 项 | 结论 | 依据 |
|---|---|---|
| 第 1 项 Mojang 平台键 | **改了**:`osx`→`mac-os`、`osx-arm64`→`mac-os-arm64`;`windows` / `linux` / 默认分支逐字未动;`_PlatformKey()` 改成转调 `internal static PlatformKeyFor(string os, string arch)` | 第 1.1 节线上清单 |
| 第 2 项 Apple 芯片缺组件时的回退 | **只核对,未改代码**。现有回退链落到 `InstallAdoptium`;Adoptium 在 mac+aarch64 上 17/21 **有**包、**8 没有包**;Zulu 在 mac+aarch64 上至少有 17 的 zip 包,但 **8 的 tar.gz 是否有包未验证** → 按「没有证据就不硬凑」处理,**不新增厂商切换**(D-39) | 第 1.3 / 1.4 节 |
| 第 3 项 Mojang 运行时落盘 | **改了 3 处**:① `Chmod755` 从**空方法**改成真的 `File.SetUnixFileMode(0755)`(非 Windows);② 原来被 `continue` 掉的 `type: link` 条目,在非 Windows 上按安全检查建相对软链(Windows 分支仍是跳过);③ java 定位统一走新的 `FindJavaUnderRoot(root, isWindows, isMac)`(mac 上先试已知形态,再回落到原来的 `Utils.FindExecutable` 遍历) | 第 1.2 节(208 个 link / 29 个 executable / `jre.bundle/Contents/Home/bin/java`) |
| 第 4 项 Adoptium / Zulu mac 包 | **改了两处、核对了三处**:OS 名(`mac` / `macos`)、架构名(`aarch64`)、Adoptium 后缀(`.tar.gz`)**都正确,只核对未改**;Zulu 的 `archive_type` 由写死的 `zip` 改成与落盘后缀一致的 **`tar.gz`(只对 mac)**;`.tar.gz` 解压改成 mac 专用实现,**保留 tar 的权限位与软链**(旧实现两者都丢)。Linux 的同类缺陷**原样保留**(红线:只改 mac) | 第 1.3 / 1.4 节 |

### 4. 对 `Java.cs` 的每一处改动(85 插入 / 32 删除)

| # | 位置(改后行号) | 原来 | 改成 | 为什么 |
|---|---|---|---|---|
| 1 | 24 | `public static readonly string PLATFORM_KEY = _PlatformKey();` | **未动**(签名、修饰符一字不变) | 任务书要求 |
| 2 | 67–119 | `_PlatformKey()` 自己 switch;`osx` → `osx` / `osx-arm64` | `_PlatformKey()` = `PlatformKeyFor(Utils.OS_NAME, Utils.ARCH)`;新增 `internal static PlatformKeyFor(string os, string arch)`,mac → `mac-os` / `mac-os-arm64` | 线上清单里没有 `osx`/`osx-arm64`(1.1 节) |
| 3 | 125–146 | `_AdoptiumOs()` / `_AdoptiumArch()` 内联 `Pick(Utils.OS_NAME, …)` | 拆成 `internal static AdoptiumOs(string os)` / `AdoptiumArch(string arch)` + 两个私有包装;**取值一字未改** | 让 mac 的 OS/架构名能被单测;顺便核对(线上确认正确) |
| 4 | 839 | `string text2 = Utils.FindExecutable(text);`(`ListInstalledJavas`) | `JavaRuntimeFiles.FindJavaUnderRoot(text, Utils.IS_WINDOWS, Utils.IS_MAC)` | java 定位统一入口 |
| 5 | 979 | 同上(`_PreferToExe`,用户手工指定的 java 目录) | 同上 | 同上 |
| 6 | 1153–1161 | `Chmod755(p)` 是**空方法** | `JavaRuntimeFiles.SetExecutable(p)`(非 Windows 上 `File.SetUnixFileMode(p, 0755)`,带 `OperatingSystem.IsWindows()` 守卫) | 清单里 29 个 `executable:true` 的文件在 mac 上必须真的有可执行位,否则 `bin/java` 是 0644 |
| 7 | 1181 | `string text3 = Utils.FindExecutable(text2);`(Mojang 装前探测) | `FindJavaUnderRoot(...)` | 见 4-4 |
| 8 | 1222–1232 | `type: link` 落进 `else` 分支的 `if (!Py.Eq(a, "file")) continue;`(**静默跳过**) | 新增 `if (Py.Eq(a, "link"))`:非 Windows 上调 `JavaRuntimeFiles.TryCreateRuntimeLink(...)`(安全检查通过才建相对软链),Windows 分支行为不变 | mac 清单里 208 个 link,原来全被丢掉 |
| 9 | 1261 | `text3 = Utils.FindExecutable(text2);`(Mojang 装后查找) | `FindJavaUnderRoot(...)` | 见 4-4 |
| 10 | 1273–1276 | `ArchiveSuffix()` 内联 `if (!Utils.IS_WINDOWS)` | `JavaRuntimeFiles.ArchiveSuffixFor(Utils.OS_NAME)`(**三个平台的返回值一字未变**) | 让后缀可注入、可单测 |
| 11 | 1283 / 1313 | Adoptium 装前/装后 `Utils.FindExecutable(text)` | `FindJavaUnderRoot(...)` | 见 4-4 |
| 12 | 1301 | `DownloadManager.ExtractArchive(text4, text);`(Adoptium) | `JavaRuntimeFiles.ExtractJavaArchive(text4, text, Utils.IS_MAC);` | mac 上走保留权限位/软链的解压;Windows/Linux 内部原样转调 `DownloadManager.ExtractArchive` |
| 13 | 1406–1429 | `_ZuluOs()` / `_ZuluArch()` 内联 `Pick` | 拆成 `internal static ZuluOs(string os)` / `ZuluArch(string arch)` + 私有包装;**取值一字未改**(`macos` / `aarch64` 被线上确认正确) | 同 4-3 |
| 14 | 1444 / 1478 / 1490 | Zulu 装前/装后 `Utils.FindExecutable(text3)` | `FindJavaUnderRoot(...)` | 见 4-4 |
| 15 | 1448 | `…&archive_type=zip&latest=true`(**所有平台都写死 zip**) | `…&archive_type=" + JavaRuntimeFiles.ZuluArchiveTypeFor(Utils.OS_NAME) + "&latest=true`(mac → `tar.gz`,Windows/Linux/未知仍 `zip`) | zip 的字节被当成 `.tar.gz` 解压 → 必然失败(1.4 节) |
| 16 | 1487 | `DownloadManager.ExtractArchive(text6, text3);`(Zulu) | `ExtractJavaArchive(text6, text3, Utils.IS_MAC)` | 同 4-12 |
| 17 | 1494 / 1504 / 1533 | Microsoft 装前/装后 `Utils.FindExecutable(text)` | `FindJavaUnderRoot(...)` | 见 4-4 |
| 18 | 1532 | `DownloadManager.ExtractArchive(text6, text);`(Microsoft) | `ExtractJavaArchive(text6, text, Utils.IS_MAC)` | 同 4-12 |

`Utils.FindExecutable(` 在改后的 `Java.cs` 里**已无调用点**;它仍在 `Utils.cs` 里,由新函数转调(以及别的文件继续用)。
`PLATFORM_KEY` 的使用点(1135/1138/1155/1162/1164 附近的目录名与清单键)**一处未动**,只换了它算出来的值。

### 5. 新文件

| 文件 | 内容 |
|---|---|
| `src\TipCL.Core\Launcher\JavaRuntimeFiles.cs`(新,Windows 源里没有对应文件) | ① `ArchiveSuffixFor(os)` / `ZuluArchiveTypeFor(os)`;② `ModeForEntry(bool)` → 0755 / 0644;③ 路径安全检查:`IsInsideRoot` / `ResolveEntryPath` / `ResolveLinkTarget`;④ `FindJavaUnderRoot(root, isWindows, isMac)`;⑤ 落地:`SetExecutable` / `TryCreateRuntimeLink`;⑥ `ExtractJavaArchive(archive, dest, isMac, hooks)` 与 `SafeExtractTarGz`(**`System.Formats.Tar`,没有引包**),带 `TarHooks`(SetMode / CreateLink / Warn / IsWindows)注入点 |

`.tar.gz` 解压现状核对(任务书第 4 项要求先弄清):`DownloadManager.ExtractArchive` **按文件名后缀**分流,
`.tar.gz` → `Utils.SafeExtractTargz`(**托管代码,不是外部 tar**),而它:
① 用 `File.WriteAllBytes` 落盘 → **丢掉 tar 的 mode(SafeExtractTargz:`Utils.cs` 1162–1184)**;
② `type '1'/'2'`(硬链/软链)一律 `continue` → **软链丢失**;③ 入口路径用 `UnderRoot` 做穿越检查(**已满足**)。
②③ 里的①和②在 mac 上会把 `bin/java` 落成不可执行 —— 所以 mac 分支改用新实现;`Utils.SafeExtractTargz` **没有改**。

### 6. 测试(新增 90 条,总数 811 → 901)

| 文件 | 覆盖 | 用例数 |
|---|---|---|
| `tests\TipCL.Tests\B2aPlatformKeyTests.cs` | `PlatformKeyFor` 13 组 × OS/架构(Windows/Linux 逐项对照改前源码 67–96 行)+ 线上顶层键集合里有两个 mac 键、没有 `osx`/`osx-arm64` + `PLATFORM_KEY` = 纯函数 + 目录名拼装 | 17 |
| `B2aJavaArchiveNamesTests.cs` | Adoptium/Zulu 的 OS 名、架构名、`ArchiveSuffixFor`、`ZuluArchiveTypeFor`、未知键仍抛 `KeyError`、Adoptium 主版本归一化 | 27 |
| `B2aRuntimePathSafetyTests.cs` | `ModeForEntry`(2 条 + 1 组 Theory)、`IsInsideRoot`(根含自身 / 兄弟前缀陷阱 / 空串 / 大小写)、`ResolveEntryPath`、`ResolveLinkTarget`(正常相对 / 上级但仍在根内 / `../` 逃出 / 绝对路径内外 / 空串 / Windows 跳过)、`TryCreateRuntimeLink` | 23 |
| `B2aJavaLocatorTests.cs` | 4 种目录树(全是空文件)+ 找不到 / 目录不存在 / 空根 + 「Windows·Linux 分支 = 改动前的 `Utils.FindExecutable`」逐形态钉住 | 14 |
| `B2aTarExtractionTests.cs` | 现造 tar.gz:`System.Formats.Tar` 写 Ustar:内容/权限位、mode 0 兜底、setuid/setgid 被 mask、相对软链参数、逃出目标的软链被跳过并记日志、条目路径逃出抛 `ValueError`、Windows/Linux 仍走旧解压器、不支持的格式抛 `DownloadError` | 9 |

- 全部用例:**不联网、不起任何外部进程、不实例化 `BackendApi` / `BridgeServer` / `PluginManager`**;
  临时文件全在 `TestHome.Root` 下;权限位与软链**一律走假委托**,没有真的改权限位或建链接;
  `TIPCL_HOME` 没有改写(仍由 `ModuleInitializer` 与 `TestHome` 管);
  没有调 `InstallAdoptium` / `_InstallZulu` / `InstallMojangRuntime` / `FindSystemJavas` 这类会联网、会扫描磁盘的入口。
- 期望值来源都在注释里标了「源码行为」还是「线上证据」。

### 7. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工时 `%LOCALAPPDATA%\TipCL` 元信息 | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 基线三项一致 → 继续 |
| 2 | `dotnet build-server shutdown` | 退出码 **0**(两个服务器都关闭成功) |
| 3 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误 / 1 警告**;另跑一次 `-v:n` 确认警告**只有 SYSLIB0014 ×1**(`Sync\PhoneClient.cs:116`,无 CA1416、无新警告,`NoWarn` 未动) |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,`失败: 0,通过: 901,已跳过: 0,总计: 901`(853 ms;没有 Win32Exception (5),没有超时) |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,`总计 901 个用例,通过 901,失败 0,跳过 0,不支持 0`,末行 `in-proc 测试全绿` |
| 6 | `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 **0**,**3739 字节**(与 A1 / Lead 基线一致) |
| 7 | `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts\smoke-dark.png"` | 退出码 **0**,**3751 字节**(与 A1 / Lead 基线一致) |
| 8 | `%TEMP%` 下 `tipcl*` 目录**名字集合**(两条通道各跑前后各一次) | 86 → 86 与 86 → 86,**两次差集都是空**(第一条命令里出现的 `tipcl-win-smoke` 是**换行分隔快照文件读回时的解析错误**造成的假阳性:该目录 `CreationTime = 2026-11-09 22:53:38`,远早于本次测试;用数组直接比较重跑后差集为空) |
| 9 | 残留进程 `testhost` / `java` / `javaw` / `tar` / `adb` / `TipCL` | 全部 **0 个** |
| 10 | 测试跑完后 `%LOCALAPPDATA%\TipCL` 元信息 | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与开工时完全一致 |
| 11 | 字节一致性自检:`src\TipCL.Core\` 下 **244** 个 `.cs`(不含 bin/obj)对 Windows 源比 SHA256 | **一致 230 / 有改动 8 / Windows 源里没有对应文件 6**;改动 8 = 现有 7(A5 的 3 个 + `SecretStore.cs` + `ExtraTools.cs` + `GlobalMods.cs` + `Sysinfo.cs`)+ **本包 `Launcher\Java.cs`**;无对应 6 = 现有 5 + **本包 `Launcher\JavaRuntimeFiles.cs`**(另有 3 个只在 Windows 源里的平台文件:`WinCredSecretStore.cs`、`Wpf\WpfDecodedImage.cs`、`Wpf\WpfImageCodec.cs`,属既有差异) |

### 8. 未验证项(本机是 Windows,没有 Mac)

- **mac 上真机安装并运行 Java 未验证**(没有任何 `.tar.gz` 被下载,也没有起过 `java`)。
- `File.SetUnixFileMode` / `File.CreateSymbolicLink` 在 mac 上的真实效果未验证(本机只验证到「权限位取值纯函数」
  与「假委托收到的参数」这一层);
- **Azul 在 mac + aarch64 上 `archive_type=tar.gz` 是否有包未验证**(请求预算 12 用完):已证实 mac+aarch64 有 zip 包、
  mac+x64 有 tar.gz 包。若该组合无包,`_InstallZulu` 会抛 `DownloadError("Azul Zulu 没有 Java N (macos/aarch64) 的 JRE 包")`
  —— 是明确报错,不是静默错解压;
- Adoptium / Zulu / Mojang 的 mac 运行时**解压后的目录树真机形态**未验证(测试里的树按线上包名与清单键手工摇出);
- Microsoft 分支在 mac 上的 URL(写死 `.zip`)与落盘后缀(`.tar.gz`)不一致**未核对也未改**(见遗留第 3 条);
- macOS 的 Gatekeeper / quarantine 属性对解压出的 `java` 的影响未验证。

### 9. 遗留与风险

1. **Apple 芯片上 Java 8 拿不到 Adoptium 原生包**(实测 `major=8 & architecture=aarch64` 返回 0 包)。
   本包按「没有证据不硬凑」保持现有行为 → 老版本(需要 Java 8)在 Apple 芯片上会**下载失败**。
   Zulu 在 mac aarch64 上是否有 Java 8 的 tar.gz 包**没查到**(请求预算用完),因此**没有**加「major 8 改走 Zulu」的选择;
   需要 Lead 裁决(要么给 1 个查询预算,要么接受现状)。
2. **`Utils.SafeExtractTargz` 的缺陷仍在**(本包未改):丢权限位、丢软链,且 **Linux 也走这条路**
   (`ArchiveSuffix()` 在 Linux 也是 `.tar.gz`)。红线要求「只改 mac + Linux 行为不变」,所以 mac 用新实现绕过;
   要不要把它改成 `System.Formats.Tar` 的通用实现(顺带修好 Linux)由 Lead 裁决。
3. **Microsoft 厂商分支的自洽性没修**:`_InstallMicrosoft` 的 URL 写死 `.zip`,落盘却用 `ArchiveSuffixFor(os)`(mac/Linux 是 `.tar.gz`);
   与 Zulu 是同一类缺陷,但本包**没有**线上证据说明 `aka.ms/download-jdk/microsoft-jdk-<major>-mac-<arch>.<ext>` 到底给哪种格式,
   按「拿不准不改」处理,留给 Lead(修法与 Zulu 同型:扩一个可注入纯函数)。
4. `mode == 0` 的 tar 条目兜底成 **0644**:若某个厂商的 tar 真的把 `bin/java` 的 mode 写成 0,解压后仍不可执行
   (这种包本来就是坏的,不做猜测性兜底 —— 不额外 chmod 是为了不掩盖解压缺陷)。
5. `link` 条目在**安装目录整体被移动**后仍然有效(用的是相对目标);但**指向尚未下载的目标**的链接会短暂悬空,
   最终一致(所有文件下载完才建链)。
6. 本包的 `Mac` 分支只保证 `mac-os-arm64` 的 4 个组件与 `mac-os` 的 7 个组件;清单将来新增组件(例如再出现 `jre-legacy` 的 arm64 版)
   不需要改代码,`PLATFORM_KEY` 已经是对的。

### Lead 验收(2026-11-12 19:25)

结论:**B2a 通过,带 3 条更正和 2 条留给 B2c 的小修**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 230 一致、8 个有改动(原有 7 个 + `Launcher\Java.cs`);无对应文件的 6 个是原有 5 个 + `Launcher\JavaRuntimeFiles.cs` |
| `git diff f7e9523..HEAD -- Launcher/Java.cs`(Lead 逐行读过,+85 / −32) | 平台键 mac 分支改为 `mac-os` / `mac-os-arm64`;`windows` / `linux` 分支的键、Adoptium / Zulu 的 OS 名与架构名逐字未变;其余是把调用点转到 `JavaRuntimeFiles` |
| `JavaRuntimeFiles.cs`(Lead 全文读过,433 行) | 见下方「代码审读」 |
| 红线文件、`Directory.Build.props`、三个 csproj | 未改;没有新包,`NoWarn` 仍 6 项 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 901 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 901 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| 残留进程 `testhost` / `java` / `javaw` / `tar` / `adb` / `TipCL` | 0 个 |
| B2a 测试扫描 | 没有联网、起进程、调安装或扫描入口、建真实符号链接、改权限位、改环境变量 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

`%TEMP%` 说明:Lead 复跑窗口内出现过一个 `tipcl-tests-14500-…` 目录,随后自行消失。该前缀在本仓库 `src` 与 `tests` 里都不存在,
同一时刻本机有别的会话在跑 dotnet 构建(命令行里是 Avalonia 11.3.2,本仓库是 11.3.23),判定不是本仓库的测试所建。

**代码审读(`JavaRuntimeFiles.cs` 与 `Java.cs` 1196–1275 行)**:

- 厂商 `tar.gz` 解压:条目路径逃出目标目录会抛 `ValueError`;符号链接先收集、等全部文件写完再建;权限位只保留 0777;调用方在解压前先 `RemoveTree`。这条路径没有发现问题。
- 路径检查是**词法**的(`Path.GetFullPath`),不解析磁盘上已存在的符号链接。这是防手误的卫生检查,不是安全边界:这些包里的 `java` 本来就会被执行。
- 硬链接条目会被跳过并记警告。mac 的 JRE 包里有没有硬链接没有核对,列入未验证。

**更正(与上文或与回报不符的地方)**:

1. 上文「遗留」第 5 条括号里的「所有文件下载完才建链」对 Mojang 路径**不成立**。`Java.cs` 1211–1252 行的循环是边遍历清单边建链(1229 行),
   `DownloadAll` 在 1255 行,即**先建链、后下载文件**。只有厂商 `tar.gz` 路径是文件写完再建链。
2. 回报说 Windows / Linux 行为逐字不变,对 Linux 不完全准确:`Chmod755` 从空方法变成真设权限、`link` 条目从跳过变成建链,
   判断条件都是「非 Windows」,所以 Linux 上也生效。这是修正而非退化,且本仓库不出 Linux 版,Lead 接受,但记录在案。
3. `ZuluArchiveTypeFor` 注释里「mac + aarch64 + tar.gz 未能核对」已由 Lead 补查(见下表),结论是有包。

**Lead 补查的 Azul 元数据**(2026-11-12 19:20,`https://api.azul.com/metadata/v1/zulu/packages`,`os=macos`,`java_package_type=jre`,`latest=true`;5 个只读请求,响应在 `artifacts\lead-b2a\`):

| java_version | arch | archive_type | 条目数 | 第一条文件名 |
|---|---|---|---|---|
| 8 | aarch64 | tar.gz | 4 | `zulu8.98.0.111-ca-jre8.0.512-macosx_aarch64.tar.gz` |
| 8 | aarch64 | zip | 4 | `zulu8.98.0.111-ca-jre8.0.512-macosx_aarch64.zip` |
| 17 | aarch64 | tar.gz | 4 | `zulu17.70.113-ca-fx-jre17.0.21.0.3-macosx_aarch64.tar.gz` |
| 21 | aarch64 | tar.gz | 4 | `zulu21.54.109-ca-fx-jre21.0.13.0.3-macosx_aarch64.tar.gz` |
| 8 | x64 | tar.gz | 4 | `zulu8.98.0.111-ca-fx-jre8.0.512-macosx_x64.tar.gz` |

- 所以 `ZuluArchiveTypeFor("osx") = "tar.gz"` 成立;Apple 芯片的 Java 8 在 Azul 有原生包(Adoptium 没有)。
- 观察:代码取返回列表的第一条(`Java.cs:1465`),而 17 / 21 的第一条是带 JavaFX 的 `-fx-` 包。这是 Windows 版原有行为,能用但包更大,本包不改。

**对子代理 4 个待裁决事项的裁决**:

1. **Apple 芯片的 Java 8**:证据已补齐(Azul 有原生包),但**现在不加厂商切换**。需要 Java 8 的老版本游戏用的 LWJGL 原生库是否有 arm64 版,
   决定了原生 arm64 的 Java 8 能不能用;如果原生库只有 x86_64,就必须用 x64 的 Java 经 Rosetta 运行。这件事由 B2b 对版本 JSON 取证后一并裁决。
2. **`Utils.SafeExtractTargz` 在 Linux 上丢权限位和软链**:不改。本仓库只出 mac 版,mac 走新实现即可。
3. **Microsoft 厂商分支在 mac 上「地址写死 `.zip`、落盘 `.tar.gz`」**:归 B2c。
4. PowerShell 执行策略:已知(D-08),无需处理。

**留给 B2c 的两条小修(Mojang 运行时安装循环)**:

- 建链移到 `DownloadAll` 之后,与厂商路径一致。
- 非 `force` 重跑(上次下载中断后重试是常见路径,此时不会 `RemoveTree`)时,已存在的链接会让 `File.CreateSymbolicLink` 抛异常并被记成警告,
  `java-runtime-gamma` 一次就是 208 条。改成「链接已存在且目标相同则跳过」。

---

## B2b mac 上的 natives 选择、规则匹配与启动参数

本包做四件事(第 1–4 项)加一份给 Lead 的评估(第 5 项)。**没有**实现「Apple 芯片上改用 x64 Java 经 Rosetta 运行」:
只取证、给可测试的判定函数、写清改动面,由 Lead 裁决。

### 1. 第 0 步线上版本 JSON 取证(只读,共 9 个请求)

查询时间 **2026-11-12 19:27:43 +08:00**,9 个请求全部 HTTP 200
(`Invoke-WebRequest -UseBasicParsing -TimeoutSec 30`,响应体按字节存盘;**只访问 `piston-meta.mojang.com`**)。

- 清单 `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json`,279089 字节,`latest.release = **26.3**`。
- 8 份版本 JSON 地址形如 `https://piston-meta.mojang.com/v1/packages/<sha1>/<id>.json`;响应体与清单留在
  `artifacts/b2b/`(不入库),聚合结果在 `artifacts/b2b/summary.txt` / `detail.txt`,脚本 `artifacts/b2b/fetch-versions.ps1` /
  `analyze.ps1`(UTF-8 with BOM,D-12)。

| 版本 | 参数形态 | Java 组件 / 大版本 | mac 的 natives 分类器 | mac 有 arm64 原生库? | `-XstartOnFirstThread` 挂在什么规则下 | LWJGL |
|---|---|---|---|---|---|---|
| 1.7.10 | `minecraftArguments`(无 `arguments` 键) | `jre-legacy` / 8 | `natives-osx`(classifiers 里也是 `natives-osx`) | **否** | JSON 里**没有**这个参数 | 2.9 |
| 1.8.9 | `minecraftArguments` | `jre-legacy` / 8 | `natives-osx` | **否** | 没有 | 2.9 |
| 1.12.2 | `minecraftArguments` | `jre-legacy` / 8 | `natives-osx`(另有 `java-objc-bridge` 同样 `natives-osx`) | **否** | 没有 | 2.9 |
| 1.13.2 | `arguments` | `jre-legacy` / 8 | 每个库 `natives.osx = natives-macos`(LWJGL 3.1.6),另 `java-objc-bridge` 是 `natives-osx` | **否** | `arguments.jvm[0]`:`[allow os.name=osx]` → `-XstartOnFirstThread` | 3.1 |
| 1.16.5 | `arguments` | `jre-legacy` / 8 | `natives-macos`(LWJGL 3.2.1 只有 osx 条目)+ `natives-osx`(`java-objc-bridge`) | **否** | 同上(`arguments.jvm[0]`,`os.name=osx`) | 3.2 |
| 1.18.2 | `arguments` | `java-runtime-beta` / 17 | `natives-macos` + `natives-osx`,与 1.16.5 同形 | **否** | 同上 | 3.2 |
| 1.19.4 | `arguments` | `java-runtime-gamma` / 17 | 新式:库名第 4 段 `natives-macos` 与 `natives-macos-arm64`(无 `natives` 字段、无 classifiers) | **是**(7 个原生库全部两套) | 同上 | 3.3 |
| 26.3(latest) | `arguments` | `java-runtime-epsilon` / 25 | 新式:`natives-macos` + `natives-macos-arm64` | **是**(12 个原生库全部两套,含 `com.mojang:jtracy`) | 同上 | 3.4 |

1.13.2 起 `arguments.jvm` 里带 `rules` 的条目固定是这几条(逐条核对过):

| # | 规则 | 值 |
|---|---|---|
| 1 | `[allow os.name=osx]` | `-XstartOnFirstThread` |
| 2 | `[allow os.name=windows]` | `-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump` |
| 3 | `[allow os.name=windows, os.version=^10\.]` | `-Dos.name=Windows 10`、`-Dos.version=10.0`(1.13.2–1.19.4;26.3 **没有**这条) |
| 4 | `[allow os.arch=x86]` | `-Xss1M`(1.19.4 与 26.3 都在) |

其余 jvm 条目都是无 `rules` 的普通字符串(26.3 有 15 条,含 4 个指向 `${natives_directory}/java|jna|lwjgl|netty` 的 `-D` 参数);
`arguments.jvm` 里**没有任何 `os.arch` 之外的架构相关值**,libraries 里也**没有任何** `os.arch` 规则(8 份 JSON 实测 `OS_ARCH_RULE_HITS = 0`)。

**与 B2a 清单合起来的结论**:`mac-os-arm64` 清单里只有 `java-runtime-delta` / `-epsilon` / `-gamma` / `-gamma-snapshot`
(**没有** `jre-legacy` / `-alpha` / `-beta`)。所以:
1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5(要 Java 8)与 1.18.2(要 `java-runtime-beta`)在 Apple 芯片上**既没有原生 arm64 natives,也没有对应的 arm64 Java 组件**;
1.19.4(gamma)与 26.3(epsilon)两样都有。

### 2. 第 1 项:规则匹配可注入(`Utils.CheckRules` + `_expand_args`)

- `Utils.CheckRules(object, PyDict)` 保留为转调;新增 `internal CheckRules(rules, features, os, arch, osVersion)`,
  逻辑逐字搬过去(含「最后一条命中的规则生效」、`os.version` 的 `\A(?:p)` 与 `\A(?:p)\z` 两种正则、非法正则走 `catch (ArgumentException)`)。
- `McLauncher._expand_args` 原来只能走静态版本(它内部调静态 `CheckRules`),新增
  `internal _expand_args(args_raw, placeholders, features, os, arch, osVersion)`,原私有方法转调并传 `Utils.*`。
- 用第 0 步的真实 `arguments.jvm`(1.19.4、26.3 两份完整抄进测试当夹具)断言:按 `osx` 展开包含 `-XstartOnFirstThread`、
  不含 Windows 的 heapdump / `-Dos.name`;按 `windows` 反之;`os.arch=x86` 的 `-Xss1M` 只在 `arch=x86` 出现。
- **1.7.10 / 1.8.9 / 1.12.2 的 JSON 没有 `arguments` 键**,所以本包**没有**给它们补 `-XstartOnFirstThread`(红线),测试用空列表钉住。

### 3. 第 2 项:natives 选择可注入 + mac 现状钉子

- `Installer.NativeArchToken()` / `SubstNativeKey(key)` / `SelectNativeClassifier(lib)` 各加一个 internal 重载
  (入参 `arch` / `arch` / `os + arch`),原 public 方法转调并传 `Utils.ARCH` / `Utils.OS_NAME`、`Utils.ARCH`;
  **选择结果一个分支都没改**。
- 钉住的现状(测试名里写明「现状」,`DisplayName = "现状(待 Lead 裁决):osx + arm64 下老式 natives-osx / natives-macos 分类器全部选不到"`):
  键恰好是 `natives-osx` / `natives-macos` 时 `rank()` 只在 `arch ∈ {x86, x64}` 返回 1,**arm64 一律 0** →
  1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5 / 1.18.2 在 Apple 芯片上 `SelectNativeClassifier` 全部返回 `null`。
  额外发现并钉住:带 `${arch}` 的 Windows 键在 arm64 下换成 `natives-windows-64`,而 `64` 不在 arm64 的后缀表里 → 同样选不到。
- 新式库(1.19.4 起,库名第 4 段 classifier、**没有** `natives` 字段):`SelectNativeClassifier` 在任何平台都返回 `null`,
  它们靠 `rules` 进 classpath;为了能按 os/arch 断言这件事,把原来内联在 `McLauncher.BuildLaunchCommand`
  (改动前 541–573 行)的「这个库进不进 classpath、路径是什么」抽成 `internal McLauncher._classpath_path(lib, libs_dir, os, arch, osVersion)`,
  行为逐字保留(含 `clientreq=false` 跳过、没有 `name` 跳过、有 `natives` 而没有 artifact 跳过、缺 `artifact.path` 走 maven 路径兜底)。
  结论:`osx + x64` 与 `osx + arm64` 下 **`natives-macos` 与 `natives-macos-arm64` 两个 jar 都会进 classpath**
  (二者 `rules` 都是 `allow os.name=osx`),natives 目录不会被它们填充。这是否正确留给真机验证。

### 4. 第 3 项:判定函数「这个版本在 mac 上有没有 arm64 原生库」

新文件 `Launcher\MacNativeSupport.cs`(229 行,纯函数,**不接入任何调用点**):`Analyze(merged_version_json)`
返回 `MacNativeKind`(`NoMacNatives` / `X64Only` / `Arm64Native` / `Mixed`)+ 小结构(全体 mac 原生库、
有 arm64 的、只有 x86_64 的清单)。判定口径写在类注释里,依据就是第 0 步的 8 份 JSON:

- 三种 mac natives 写法都认:`natives.osx` 字段、`downloads.classifiers` 里 `natives-osx` / `natives-macos*` 键、
  以及新式的库名第 4 段 classifier;先按注入的 os / arch 过 `CheckRules`。
- 库名按 `group:artifact` 归组(1.19.4 起同一个原生库的 x64 / arm64 是**两条独立 library 条目**,不归组会把
  `Arm64Native` 误判成 `Mixed`)。
- 8 个版本的结论:1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5 / 1.18.2 = `X64Only`;1.19.4 / 26.3 = `Arm64Native`。

### 5. 第 4 项:mac 上解压 natives 与 classpath 的核对(只核对,**没有改代码**)

| 项 | 文件:行 | 现状 | mac 上是否正确 | 依据 |
|---|---|---|---|---|
| natives 解压是否按扩展名过滤 | `DownloadManager.cs` 1179-1202 | 只排除 `exclude` 前缀与 `/` 结尾的目录条目,**没有**扩展名白名单 | **正确,无需改**:`.dylib` / `.jnilib` 会照常解出 | 实测:自造 zip(含 `liblwjgl.dylib` / `liblwjgl.jnilib` / `lwjgl.dll` / `liblwjgl.so` / `META-INF/MANIFEST.MF`)全部解出 |
| 「有没有 natives」的判定 | `Installer.cs` 330-348 | 文件名前缀 `lwjgl` 或 `liblwjgl` | **正确**:mac 的 `liblwjgl.dylib` 命中 `liblwjgl` | 实测 ✓;只有 `META-INF` 的目录判 false |
| classpath 分隔符 | `McLauncher.cs` 597 + 603 的 `classpath_separator` 占位符 | `Path.PathSeparator.ToString()`(不是写死 `;`) | **正确**:mac 上 .NET 给 `:` | 源码;本机只能钉住 Windows 侧的 `;`(测试) |
| `${natives_directory}` 形态 | `Installer.NativesDir` 350-357 + `PPath` | `PPath.Join(<instance>/versions/<id>, <id>-natives)`;老版本是 `bin/natives` | **正确**:`PPath` 非 Windows 分支用正斜杠、无盘符 | `PPath.cs` 300-313 + 测试 |
| `-Djava.library.path` | `McLauncher.cs` 615(JSON 缺该参数时补)、1.19.4 的 `${natives_directory}`、26.3 的 `${natives_directory}/java|jna|lwjgl|netty` | 值来自 `PPath`,是合法 mac 路径 | **正确** | 线上 JSON + `PPath` 测试 |
| 主类启动的工作目录 | `Utils.MakeStartInfo` 1435-1442 | `cwd` 为游戏目录,没有则 `Utils.ROOT` | 与平台无关 | 只记录,不改 |
| `${launcher_name}` / `${launcher_version}` | `McLauncher.cs` 603 占位符(`TipCL` / `AppInfo.LAUNCHER_VERSION`);666-673 缺省补 `-Dminecraft.launcher.brand=TipCL` 与写死的版本 `1.0.10` | 与平台无关 | 只记录,不改(666-673 的写死版本是 Windows 版原样,不是本包的事) |

### 6. 第 5 项:给 Lead 的评估 —— Apple 芯片上没有 arm64 原生库的版本怎么跑(**不选方案,只列改动面**)

**证据汇总**(第 0 步 + B2a):

| 版本段 | mac 原生库有 arm64? | 需要的 Java 组件在 `mac-os-arm64` 清单里? |
|---|---|---|
| 1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5(Java 8,`jre-legacy`) | 否(LWJGL 2.9 / 3.1.6 / 3.2.x 只有 x86_64) | 否(`mac-os-arm64` 没有 `jre-legacy`;Azul 有 arm64 Java 8 原生包) |
| 1.18.2(Java 17,`java-runtime-beta`) | 否(LWJGL 3.2.x) | 否(`mac-os-arm64` 没有 `beta`;Azul 有 arm64 Java 17) |
| 1.19.4(Java 17,`java-runtime-gamma`)及以后 | **是**(LWJGL 3.3+ 每个原生库都有 `natives-macos-arm64`) | 是(`gamma` 在 `mac-os-arm64` 里) |
| 26.3(Java 25,`java-runtime-epsilon`) | **是** | 是 |

分界线是 **LWJGL 3.2 → 3.3**(1.18.2 → 1.19.4)。也就是:Apple 芯片上「原生 arm64」只能跑 1.19.4 及更新的版本;
1.18.2 及更老必须让游戏在 x86_64 下跑,而那意味着**Java 也必须是 x64 的**(x86_64 的 `.dylib` 只能被 x64 JVM 加载)。

**若采用「这类版本改用 x64 Java(经 Rosetta)+ x86_64 natives」,需要把下列地方从「读进程架构 `Utils.ARCH`」改成「读本次启动的目标架构」**:

| # | 文件:方法(行) | 现在读什么 | 改动面 |
|---|---|---|---|
| 1 | `Installer.SelectNativeClassifier`(`Installer.cs` 257-331,`rank` 302-330) | `Utils.ARCH`(已可注入) | **中**:选择逻辑已参数化,要动的是 6 个调用点把「目标架构」传进来:`Cleaner.cs:46`、`Installer.cs:416`(ExtractNatives)、`Installer.cs:747`(`_install_libraries` 下载 natives jar)、`McLauncher.cs:571-578`(缺失预检)、`OfficialMigrate.cs:149`、`Preflight.cs:283/385` |
| 2 | `Installer.NativeArchToken` / `SubstNativeKey`(`Installer.cs` 206-231 / 240-254) | `Utils.ARCH`(已可注入) | **小**:调用点 3 处(`Installer.cs:341`、`Installer.cs:758`、`Preflight.cs:294`) |
| 3 | `Utils.CheckRules` 的 `os.arch`(`Utils.cs` 1303-1373) | `Utils.ARCH`(已可注入) | **小但要全改**:调用点 7 处(`Installer.cs:412/711`、`McLauncher.cs:571-578/583`、`Preflight.cs:256/385`、`MacNativeSupport.cs:112`);若「目标架构」语义上等于 `os.arch`,这些地方都要换 |
| 4 | `Java.PlatformKeyFor` / `PLATFORM_KEY`(`Java.cs` 24 / 67-96 / 85) | `Utils.ARCH` → `mac-os-arm64` | **小**:x64 回退时键要变 `mac-os`(否则清单里没有 `jre-legacy` / `beta`) |
| 5 | Java 的安装:`Java.cs` 128-139(Adoptium)、1416-1449(Zulu)、1503+(Microsoft) 的 `arch ?? Utils.ARCH` | `Utils.ARCH` | **小**:入参 `arch` 已经存在,传 `"x64"` 即可;`Java.cs:1335` 的 `_IsLegacy32bit` 目前只在 Windows 生效,不受影响 |
| 6 | 「已装 Java 的挑选」`Java.JavaUsableFor`(347)/`PickJavaForVersion`(987)/`ListInstalledJavas`(801) | **不看架构**,只看 major | **中**:现状下需要 x64 Java 时**不会**主动排除 arm64 Java;要做到自洽要加「架构偏好」条件 |
| 7 | 启动参数里与架构有关的值 | 只有 `os.arch` 规则(1.19.4 / 26.3 的 `-Xss1M` 挂 `x86`,mac 用不到)与老式 natives 键里的 `${arch}` | **小**:JSON 没有别的架构相关参数,**不需要**另外加 JVM 参数 |
| 8 | `Terracotta.cs` 412-413(联机组件自读 `Utils.OS_NAME` / `Utils.ARCH`) | `Utils.*` | B2c 范围,只记在这里 |

**其它可行做法与代价**(按改动从小到大的顺序,不做选择):

1. **只在 Apple 芯片上「拒绝启动并给出提示」**:改动最小(一处判断 + 文案),代价是老版本彻底装不了/跑不了。
2. **放宽 `rank()`:arm64 下也让 `natives-osx` / `natives-macos` rank=1**(`Installer.cs` rank 一处):
   改动极小,但选出来的仍是 x86_64 dylib,**单独做没有意义** —— 只有与「x64 Java」一起才有用;
   单独做会把现在「选不到 → 启动时可能直接崩在 natives」变成「选了 x86_64 dylib → 用 arm64 JVM 加载必然失败」。
3. **原生 arm64 Java(Azul 有 Java 8 / 17 的 mac aarch64 包)跑老版本**:不可行 —— 老版本的 mac natives 只有 x86_64,
   arm64 JVM 加载不了;LWJGL 到 3.3 才有 mac arm64 原生库。
4. **x64 Java(经 Rosetta)+ x86_64 natives**(上表 8 处改动):覆盖面最大(1.7.10 – 1.18.2 全都能跑),
   代价是要把「目标架构」做成一次启动的显式状态并一路传下去,还要处理「同一台机器上两种架构的 Java 与 natives 并存」
   (`instances/<id>/libraries` 与 `versions/<id>/<id>-natives` 目前是按实例/版本共享的目录,架构不同必须分目录,否则会互相覆盖)。
5. **让用户在「Java」页手动选 Java + 手动选架构**:UI 成本,且 natives 侧仍要按同一架构走,等于把 4 的一半做成手动的。

**风险提示(现状下 Apple 芯片上会发生什么,本机未验证)**:`McLauncher.cs` 571-578 的「缺少 natives 就报错」预检
**不会**触发 —— 它用 `SelectNativeClassifier(lib) != ""` 判断「这个版本需要不需要 natives」,而在 arm64 上这批库全部返回 `null`,
预检认为「不需要 natives」,于是会带着空的 natives 目录启动,失败发生在 Java 里而不是启动前。

### 7. 对三个复制文件的每处改动(共 3 个文件、+93 / −35 行)

| 文件 | 位置 | 原来是什么 | 改成什么 | 为什么 |
|---|---|---|---|---|
| `Launcher\Utils.cs` | 1303-1312(转调)+ 新增 1313-1373 | `CheckRules(object, PyDict)` 直接读 `OS_NAME` / `ARCH` / `OS_VERSION` | public 方法转调新增的 `internal CheckRules(rules, features, os, arch, osVersion)`;内部把三处静态字段换成入参 | 让规则匹配能在 Windows 本机按 mac 参数断言;语义逐字未改 |
| `Launcher\Installer.cs` | 206-231 | `NativeArchToken()` 读 `Utils.ARCH` | public(206-212)转调 `internal NativeArchToken(arch)`(214-231) | 同上 |
| `Launcher\Installer.cs` | 240-254 | `SubstNativeKey(key)` 用 `NativeArchToken()` | public(240-243)转调 `internal SubstNativeKey(key, arch)`(248-254) | 同上 |
| `Launcher\Installer.cs` | 257-331(其中 `rank` 302-330、330 行的 `Utils.ARCH` 换参数) | `SelectNativeClassifier(lib)` 读 `Utils.OS_NAME` / `Utils.ARCH`,`rank` 里也读 `Utils.ARCH` | public(257-261)转调 `internal SelectNativeClassifier(lib, os, arch)`(268-331);内部三处替换成入参,**0/1/2 三档判定一字未改** | 同上;本包**不修** arm64 下选不到老式分类器的现状 |
| `Launcher\McLauncher.cs` | 64-71(转调)+ 72-103(新重载)+ 87 | `_expand_args`(原 63-94)调静态 `Utils.CheckRules` | 新增 `internal _expand_args(..., os, arch, osVersion)`,原私有方法转调并传 `Utils.*` | 让「arguments.jvm + os/arch → 参数列表」可按 mac 断言 |
| `Launcher\McLauncher.cs` | 519-542(新增)+ 581-592(调用点) | `BuildLaunchCommand` 里内联的「库进不进 classpath + 路径」循环 | 抽成 `internal _classpath_path(lib, libs_dir, os, arch, osVersion)`,循环改为调用它并传 `Utils.*` | 第 2 项要求「新式库进不进 classpath」也要能按 os/arch 断言;逻辑逐字搬运,Windows 入参下返回值不变(有等价钉子用例) |

三个文件的 public 签名、其它方法、`NoWarn`、csproj、TFM 全部未动。

### 8. 新文件

- `src/TipCL.Core/Launcher/MacNativeSupport.cs`(229 行):第 3 项的纯函数 + `MacNativeKind` + `MacNativeSupportReport`,**未接入调用点**。
- `tests/TipCL.Tests/B2bCheckRulesTests.cs`、`B2bExpandArgsTests.cs`、`B2bNativeClassifierTests.cs`、`B2bClasspathTests.cs`、
  `B2bMacNativeSupportTests.cs`、`B2bNativesUnpackTests.cs`:137 条新用例(全部不联网、不起进程;文件操作只在 `TestHome.Dir` 之下)。

### 9. 验证命令与实际结果(全部在本机真实执行过)

| 检查 | 结果 |
|---|---|
| 开工前 `%LOCALAPPDATA%\TipCL` 元信息 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线**一致**(不一致就停手,本次没有出现) |
| `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,0 错误;警告**只有 SYSLIB0014 ×1**(`PhoneClient.cs:116`) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0:**通过 1038 / 失败 0 / 跳过 0**(838 ms,没有出现 `Win32Exception (5)`,没有 3 分钟不结束) |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0:**1038 通过 / 0 失败 / 0 跳过 / 不支持 0**,与 `dotnet test` 一致 |
| `%TEMP%` 的 `tipcl*` 目录名字集合(两条通道各取前后各一次) | 测试窗口内**新增 0 个**(本机另有 86 个历史 `tipcl*` 目录,属于别的会话) |
| `--smoke --theme light / dark` | 退出码 0 / 0;**3739 / 3751 字节**,与 A1 起一直一致 |
| 测试后 `%LOCALAPPDATA%\TipCL` 元信息 | 9328 个文件 / 889530606 字节 / 2026-11-10 20:42:16,**与开工时逐项一致** |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | **0 个** |
| `src\TipCL.Core\` 全部 `.cs` 对 Windows 源比 SHA256 | 总数 **245**(另含 2 个 `obj\` 生成文件):一致 **227**、有改动 **11**、Windows 源里没有对应文件 **7**。有改动的 11 = 原有 8 个(`Ai\ExtraTools.cs`、`GlobalMods.cs`、`Java.cs`、`Sysinfo.cs`、`Plugins\DotNetPluginContext.cs`、`Plugins\Sdk\PluginSdk.cs`、`Plugins\Sdk\PluginUiRegistry.cs`、`Plugins\SecretStore.cs`)+ 本包 3 个(`Launcher\Utils.cs`、`Launcher\Installer.cs`、`Launcher\McLauncher.cs`);没有对应文件的 7 = 原有 6 个 + 本包 `Launcher\MacNativeSupport.cs` |
| 联网请求总数(本包) | **9 个**(1 份清单 + 8 份版本 JSON),全部 `piston-meta.mojang.com` 只读 GET,没有下载任何 jar / natives / 二进制 |

### 10. 未验证项(本机是 Windows,没有 Mac)

1. mac 上 `SelectNativeClassifier` / `_classpath_path` / `CheckRules` 的真实调用链(它们现在由 `Utils.OS_NAME` / `Utils.ARCH` 驱动,
   mac 上取值 `osx` / `arm64` 是推断,推理链:mac 上 `Utils.OS_NAME == "osx"`,`Utils.ARCH` 是进程架构)。
2. 「1.19.4 起两个 natives jar 都进 classpath」在真机上是否正确(LWJGL 从 classpath 里按架构选 `.dylib`,本机无法验证)。
3. 老版本在 Apple 芯片上的实际现象(是否崩在 natives、崩在哪一步)。
4. `MacNativeSupport` 对**整合包 / 模组**版本(继承版本合并后的 JSON)的判定:测试夹具是 vanilla 的 8 份 JSON,
   模组版本里可能出现本函数没见过的 classifier 写法。

### 11. 遗留与风险

1. **本包没修 arm64 下老式分类器选不到**(红线:只钉现状),所以 Apple 芯片上 1.18.2 及更老的版本仍然装不出 natives;
   第 5 项把改动面写清了,等 Lead 裁决。
2. **`natives` 缺失预检在 arm64 上失效**(`McLauncher.cs` 522-528):因为判断依据是先算 `SelectNativeClassifier`,
   arm64 上全部 null → 预检认为不需要 natives,于是启动失败会发生在 Java 里。这是第 5 项要一起裁决的连带问题。
3. `-XX:StackShadowPages=32`(26.3)与 `--enable-native-access=ALL-UNNAMED` 是 JSON 给的参数,本包照抄不改;
   它们在 mac 上能否被 Java 25 接受**未验证**(不是本包能判的)。
4. `MacNativeSupport` 判定「没有后缀的老式分类器按 x86_64 记」是保守假设,依据是这些版本的 JSON 里没有任何 arm64 字样;
   若将来发现某个老包其实是 universal 二进制,要改 `_key_arch`。
5. `_classpath_path` 的抽取改变了 `McLauncher` 内部结构(不改行为),如果 Lead 认为它超出了「只加可注入重载」的范围,
   回退方式很直接(把方法体搬回循环),测试里对它的断言也要跟着换成对 `Utils.CheckRules` 的断言。

### Lead 验收(2026-11-12 20:05)

结论:**B2b 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 227 一致、11 个有改动(原有 8 个 + 本包 `Utils.cs`、`Installer.cs`、`McLauncher.cs`);无对应文件的 7 个是原有 6 个 + `Launcher\MacNativeSupport.cs` |
| `git diff 46837d6..HEAD` 对三个复制文件(Lead 逐行读过) | `Utils.cs`、`Installer.cs`:只把 `OS_NAME` / `ARCH` / `OS_VERSION` 换成入参,原 public 方法转调,分支一个没动。`McLauncher.cs`:`_expand_args` 同样处理;classpath 循环体抽成 `_classpath_path` |
| `_classpath_path` 对照 Windows 原文 528–573 行 | 逐句等价:`clientreq`、规则、无名字、`artifact.path`、有 `natives` 无 artifact、maven 兜底六个分支都在;去重键 `Manifest.LibraryIdentity` 与插入顺序留在循环里未变 |
| `MacNativeSupport.cs`(Lead 全文读过) | 纯函数,没有接入任何调用点 |
| `Java.cs`、`JavaRuntimeFiles.cs`、红线文件、`Directory.Build.props`、三个 csproj | 未改 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1038 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1038 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 86 → 86,没有新名字 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 0 个 |
| B2b 测试扫描 | 没有联网、起进程、调安装或启动入口、改环境变量 |
| 调用点清单(Lead 用 grep 复核) | `SelectNativeClassifier` 6 处、`SubstNativeKey` 3 处、`CheckRules` 静态版 5 处,与上文第 6 节的表一致 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

**对子代理 4 个待裁决事项的裁决**:

1. **Apple 芯片上没有 arm64 原生库的版本:采用「x64 Java 经 Rosetta + x86_64 原生库」**(上文第 6 节做法 4)。
   依据:8 份版本 JSON 显示 1.18.2 及更老的 mac 原生库只有 x86_64,`mac-os-arm64` 运行时清单也没有它们要的 Java 组件;
   做法 1(拒绝启动)与 GOAL「功能与 Windows 版对齐、能真正启动 Minecraft」不符,做法 2、3 单独做不成立。
   设计要点(分两包实现):
   - 引入**本次启动的目标架构**:纯函数,入参是合并后的版本 JSON、OS、进程架构。只有 `osx` + 进程 `arm64` + `MacNativeSupport` 判定为 `X64Only` 或 `Mixed` 时返回 `x64`,其余一律返回进程架构。所以 Windows / Linux 与 Intel Mac 上结果恒等于现状。
   - `Mixed` 归 `x64`:缺 arm64 变体的库在 arm64 JVM 里加载不了,而观察到的数据里每个库都有 x86_64 变体。
   - 目标架构是版本的确定函数(同一台机器上同一个版本永远得到同一个值),所以 natives 目录**不需要**按架构分目录;上文第 6 节做法 4 担心的「互相覆盖」不成立。
   - **B2d**:natives 选择、`${arch}` 替换、规则里的 `os.arch`、classpath、缺 natives 预检,全部改为读目标架构。
   - **B2e**:Java 侧。Mojang 平台键、厂商安装、已装 Java 的挑选都按目标架构;Rosetta 未安装时启动前给出明确提示。
   - 本机没有 Mac,整条 Rosetta 路径只能测到纯函数,真机步骤写进 `docs/MAC-VERIFY.md`(D2)。
2. **`_classpath_path` 抽取**:接受。已对照 Windows 原文确认等价。
3. **arm64 下缺 natives 预检不触发**:由 B2d 一并解决(目标架构为 `x64` 后 `SelectNativeClassifier` 不再返回 null)。
4. **无后缀的 `natives-osx` / `natives-macos` 按 x86_64 记**:接受,列入真机验证清单。

另记:1.19.4 起 `natives-macos` 与 `natives-macos-arm64` 两个 jar 在 mac 上都进 classpath。两者在版本 JSON 里的规则都是 `allow os.name=osx`,
没有 `os.arch` 规则,按 JSON 就该如此,不改;列入真机验证清单。

---

## B2d Apple 芯片旧版本:引入「本次启动的目标架构」,natives / 规则 / classpath 按目标架构

本包实现 Lead 在 B2b 验收里裁决的做法 4 的**前一半**:引入「本次启动的目标架构」,把 Launcher 里读进程架构
(`Utils.ARCH`)的 natives 选择、`${arch}` 替换、`os.arch` 规则、classpath 全部改成读目标架构。
**Java 侧一个字没动**(Mojang 平台键、厂商安装包、已装 Java 的挑选、Rosetta 检测都是 B2e),
Terracotta(B2c)与界面/系统信息展示也不在范围内。

### 1. 三项各自做了什么

1. **第 1 项 目标架构函数**:新建 `src/TipCL.Core/Launcher/LaunchArch.cs`(58 行,纯函数)。
   - `public static string For(PyDict mergedVersionJson)` → 转调内部重载,传 `Utils.OS_NAME` / `Utils.ARCH` / `Utils.OS_VERSION`。
   - `internal static string For(PyDict mergedVersionJson, string os, string processArch, string osVersion)`:
     只有 `os == "osx"` 且 `processArch == "arm64"` 且 `MacNativeSupport.Analyze(json, os, processArch, osVersion).Kind`
     是 `X64Only` 或 `Mixed` 时才返回 `"x64"`,其余(含 `Arm64Native`、`NoMacNatives`、JSON 为 null、空 `libraries`、
     非 osx、进程不是 arm64)**一律返回 `processArch` 原值**。
   - 类注释写清四件事:目标架构是**版本的确定函数**(同一台机器上同一个版本永远得到同一个值,所以 natives 目录
     **不需要**按架构分目录,B2b 第 6 项担心的「两种架构互相覆盖」不成立);`Mixed` 归 `x64` 的理由(缺 arm64 变体的库
     在 arm64 JVM 里加载不了,而观察到的数据里每个库都有 x86_64 变体);整条 Rosetta 路径**未经真机验证**;Java 侧由 B2e 处理。
2. **第 2 项 7 处调用点**全部改读目标架构(逐处见下面第 4 节的表):`Cleaner`、`Installer`(ExtractNatives、
   `_install_libraries`、`NativesJarRelpath`)、`McLauncher`(缺 natives 预检、classpath、两处 `_expand_args`)、
   `Preflight`(`_check_libraries`、`_check_natives`)、`OfficialMigrate`。做法统一为:对合并后的版本 JSON
   **只算一次** `LaunchArch.For(...)`,循环里不再重算;调用点换成 B2b 已有的可注入 internal 重载
   (`SelectNativeClassifier(lib, os, arch)` / `SubstNativeKey(key, arch)` / `Utils.CheckRules(rules, features, os, arch, osVersion)` /
   `_classpath_path(..., os, arch, osVersion)` / `_expand_args(..., os, arch, osVersion)`)。
   顺带解决 B2b 记的两个连带问题:`McLauncher` 的缺 natives 预检在 Apple 芯片的旧版本上**重新生效**;
   `Cleaner` 的保留集合按目标架构算。
3. **第 3 项 架构读取点清点**见下(第 3 节):`Launcher\` 下(不含 `Ai\`、`Sync\`)共 23 处架构 / 位数读取点,
   本包改了 7 个调用点;其余逐条写明不改的理由。

### 2. 语义与实测口径

- 版本 JSON 规则里的 `os.arch`、natives 键里的 `${arch}`、natives 分类器的架构后缀,指的都是**将要运行游戏的 JVM 的架构**
  = 目标架构,而不是启动器进程的架构。
- `McLauncher` 的 placeholders(`McLauncher.cs` 603 行那一串)里**没有**任何架构相关的值(逐项看过:`library_directory` /
  `natives_directory` / `classpath` / `classpath_separator` / 认证 / 分辨率 / 启动器名与版本),所以没有需要换的占位符;
  版本 JSON 的 `arguments` 里唯一与架构有关的是 `os.arch` 规则(1.19.4 / 26.3 的 `-Xss1M` 挂 `x86`,mac 用不到),B2b 已核对过。
  本包**没有**给启动命令加任何 JVM 参数(红线)。
- Apple 芯片上,凡是 mac natives 键里带 `${arch}` 的老式写法都会算出 `X64Only` 或 `Mixed` → 目标架构 x64,而
  `NativeArchToken` 只把 `x86` 换成 `32`(arm64 与 x64 都得到 `64`),所以 mac 上这两处替换在现有数据里
  **看不出架构差别**;真正起作用的是「按 x64 的架构后缀表选分类器」这一条(老式 `natives-osx` / `natives-macos`
  在 arm64 下 rank=0,在 x64 下 rank=1)。

### 3. 第 3 项:`Launcher\` 里的架构 / 位数读取点(不含 `Ai\`、`Sync\`,grep `Utils.ARCH` / `NativeArchToken(` /
`RuntimeInformation` / `Is64BitProcess` / `Is64BitOperatingSystem`)

| # | 文件:行 | 用途 | 本包是否改为目标架构 | 不改的理由 |
|---|---|---|---|---|
| 1 | `Utils.cs:347-358`(`IsOSPlatform`) | `OS_NAME` 的实现 | 否 | 这是「进程在哪个系统上」的唯一来源,不是架构;它本身还作为 LaunchArch 的 os 入参 |
| 2 | `Utils.cs:247` + `360-369`(`RuntimeInformation.OSArchitecture`) | `ARCH` / `ArchName()` = **进程架构**的定义 | 否 | 架构读取的源头;本包新增的 LaunchArch 就是拿它当「进程架构」入参,保留语义不变 |
| 3 | `Utils.cs:1303-1306`(public `CheckRules`) | 转调,传 `OS_NAME` / `ARCH` / `OS_VERSION` | 保留 | public 签名不变(B2b 已有可注入重载);本包 5 个调用点全部改传目标架构 |
| 4 | `Installer.cs:206-212`(public `NativeArchToken()`) | 转调,读 `Utils.ARCH` | 保留(公开 API) | 内部重载(B2b)已存在;本包的使用方 `SubstNativeKey(key, arch)` 按目标架构 |
| 5 | `Installer.cs:240-243`(public `SubstNativeKey(key)`) | 转调,读 `Utils.ARCH` | 保留(公开 API) | 本包 3 处调用点(`NativesJarRelpath`、`_install_libraries`、`Preflight._natives_entry`)已改成传 arch |
| 6 | `Installer.cs:257-261`(public `SelectNativeClassifier(lib)`) | 转调,读 `Utils.OS_NAME` / `Utils.ARCH` | 保留(公开 API) | 6 处调用点已全部改成 `SelectNativeClassifier(lib, os, arch)` |
| 7 | `Installer.cs:333-336`(public `NativesJarRelpath(lib, nkey)`) | 转调,读 `Utils.ARCH` | 保留(公开 API) | 新增 3 参重载;2 处调用点(ExtractNatives、Cleaner)已改传目标架构 |
| 8 | `Installer.cs:454/773-775`(`ExtractNatives` / `_install_libraries`) | 目标架构 | **已改** | 见第 4 节 |
| 9 | `Cleaner.cs:30`(`_lib_paths`→`_lib_relpaths`) | 目标架构 | **已改** | 见第 4 节 |
| 10 | `McLauncher.cs:65`(私有 `_expand_args` 转调) | 读 `Utils.*` | 保留(私有,**现在没有调用方**) | 两处调用点在 626/627 行改成 internal 重载并传目标架构;转调壳留着不动以免无谓删除(见遗留 5) |
| 11 | `McLauncher.cs:589-590`(BuildLaunchCommand) | 目标架构 | **已改** | 见第 4 节 |
| 12 | `Preflight.cs:274/313/419` | 目标架构 | **已改** | 见第 4 节 |
| 13 | `OfficialMigrate.cs:136/167` | 目标架构 | **已改** | 见第 4 节 |
| 14 | `LaunchArch.cs:34` | 新文件里把 `Utils.ARCH` 当「进程架构」入参 | 新增 | 本包新增的唯一定义处 |
| 15 | `MacNativeSupport.cs:112/178/184` | 判定函数内部 | 未改 | 已经是可注入形式(`Analyze(json, os, arch, os_version)`);按调用方传入的目标架构工作 |
| 16 | `Java.cs:69`(`PlatformKeyFor(OS_NAME, ARCH)`) | Mojang 平台键 `mac-os` / `mac-os-arm64` | 未改 | **属于 Java 侧 → B2e** |
| 17 | `Java.cs:139`(`AdoptiumArch(arch ?? Utils.ARCH)`) | Adoptium 包名/架构 | 未改 | B2e |
| 18 | `Java.cs:1289` / `1439` / `1503`(`arch ?? Utils.ARCH`) | 厂商安装的默认架构 | 未改 | B2e |
| 19 | `Java.cs:1335`(`Utils.ARCH == "x64"`) | `_IsLegacy32bit`(只在 Windows 生效) | 未改 | B2e;且这个分支在非 Windows 上根本不进 |
| 20 | `Java.cs:1427`(`ZuluArch(arch ?? Utils.ARCH)`) | Zulu 包名/架构 | 未改 | B2e |
| 21 | `Terracotta.cs:413`(`Utils.ARCH`) | 联机组件自己读 OS/架构 | 未改 | **属于 Terracotta → B2c**(B2b 第 6 项也这么记) |
| 22 | `Sysinfo.cs:396`(`"arch", Utils.ARCH`)+ `249`(`PROCESSOR_ARCHITEW6432` / `PROCESSOR_ARCHITECTURE`)+ `256`(`RuntimeInformation.OSArchitecture`) | 系统信息展示(「系统信息」页给别人看的机器信息) | 未改 | 语义是「启动器进程所在机器」,不是「游戏 JVM 架构」;改了反而错 |
| 23 | `Crash.cs:1363`(`Environment.Is64BitOperatingSystem`) | 崩溃报告里的内存/位数建议 | 未改 | 系统信息展示,与 JVM 架构无关 |

`Is64BitProcess` 在 `Launcher\`(不含 `Ai\`、`Sync\`)里**没有任何出现**(grep 0 命中)。

### 4. 对复制文件的每处改动

共 5 个复制文件(+1 个只改类注释)。所有 public 签名、`NoWarn`、csproj、TFM 一律未动;
`Java.cs`、`JavaRuntimeFiles.cs`、`Utils.cs` 一行未改;`MacNativeSupport.cs` 只改类注释里的一句话(Lead 授权范围)。

| 文件 | 位置 | 原来是什么 | 改成什么 | 为什么 |
|---|---|---|---|---|
| `Launcher\Cleaner.cs` | `_lib_paths`(14-42)+ 新增 `_lib_relpaths`(46 起) | `_lib_paths` 里逐版本内联遍历 `libraries`,`Installer.SelectNativeClassifier(pyDict3)` / `Installer.NativesJarRelpath(pyDict3, text)` 读进程架构 | 循环体整段抽成 `internal static HashSet<string> _lib_relpaths(PyDict pyDict2, string libs, string os, string processArch, string osVersion)`(内部 `LaunchArch.For` 算一次),`_lib_paths` 对每个已装版本 `UnionWith(_lib_relpaths(pyDict2, libs, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION))`;两处调用换成带 arch 的重载 | 保留集合必须按目标架构算(改错会误删),而且要在 Windows 本机按 osx + arm64 断言 |
| `Launcher\Installer.cs` | `NativesJarRelpath`(333-355) | public 方法体里 `SubstNativeKey(item.Key)` 读 `Utils.ARCH` | 新增 `internal static NativesJarRelpath(lib, nkey, arch)`,public 转调并传 `Utils.ARCH`;回退查找用 `SubstNativeKey(item.Key, arch)`,其余逐字相同 | 分类器回退查找的 `${arch}` 替换要按目标架构 |
| `Launcher\Installer.cs` | `ExtractNatives`(411-460) | 方法里边遍历边查文件、边解压 | 抽出 `internal static List<KeyValuePair<string, PyDict>> _natives_to_extract(resolved, libs_dir, os, processArch, osVersion)`(含缺 jar 告警);`ExtractNatives` 保留 `NativesDir` / `EnsureDir` / 解压循环与父版本复制 | 原方法一边解压一边算,抽不出可注入重载;抽出的纯函数能断言「osx + arm64 下要解压的就是两个 x86_64 jar」 |
| `Launcher\Installer.cs` | `_install_libraries`(773/806)+ 新增 `_native_classifier_entry` | `Utils.CheckRules(item.Get("rules"))`;classifiers 回退里 `SubstNativeKey(item2.Key)`;natives 分支内联 22 行 | 方法顶部加 `string arch = LaunchArch.For(resolved)`(算一次);`CheckRules(item.Get("rules"), null, Utils.OS_NAME, arch, Utils.OS_VERSION)`;natives 分支抽成 `internal static PyDict _native_classifier_entry(lib, downloads, os, arch, out string classifier)`(返回 null = 没有分类器,返回空 PyDict = 有分类器但清单里查不到 → 原来也是走 maven 兜底) | 安装时下载哪个 natives jar 要按目标架构 |
| `Launcher\McLauncher.cs` | `BuildLaunchCommand` 586-590 | `Installer.Libs(pyDict2).Where(…clientreq…CheckRules(lib.Get("rules"))).Any(… SelectNativeClassifier(lib) …) && !Installer.NativesPresent(text6)` | `internal static bool _needs_natives(resolved, os, processArch, osVersion)`(原 Where/Any 的逐句搬运,内部按目标架构过 rules、选分类器);调用点 `_needs_natives(pyDict2, Utils.OS_NAME, Utils.ARCH, Utils.OS_VERSION) && !Installer.NativesPresent(text6)`(**短路顺序未变**) | 预检判定要可注入断言;B2b 记的「arm64 上预检失效」在目标架构为 x64 后消失 |
| `Launcher\McLauncher.cs` | 589 + 598 + 626/627 | `string text6 = …ExtractNatives(…)` 后没有架构变量;`_classpath_path(item, text5, Utils.OS_NAME, Utils.ARCH, …)`;两处 `_expand_args(…)` 走私有转调(内部读 `Utils.ARCH`) | 新增 `string arch = LaunchArch.For(pyDict2)`(**只算一次**);`_classpath_path(…, Utils.OS_NAME, arch, Utils.OS_VERSION)`;两处 `_expand_args(…, Utils.OS_NAME, arch, Utils.OS_VERSION)` | classpath 与 `os.arch` 规则按目标架构 |
| `Launcher\Preflight.cs` | `_check_libraries`(272-328)+ 新增 `_natives_entry` | `Utils.CheckRules(item.Get("rules"))`;natives 分支内联(分类器 → classifiers 查找 → 相对路径) | 顶部 `string arch = LaunchArch.For(resolved)`(算一次);`CheckRules(item.Get("rules"), null, Utils.OS_NAME, arch, Utils.OS_VERSION)`;natives 分支抽成 `internal static PyDict _natives_entry(lib, downloads, name, os, arch, out string relpath)` | 体检要按目标架构检查 natives jar(原方法查文件,抽不出可注入重载) |
| `Launcher\Preflight.cs` | `_check_natives`(396-423)+ 新增 `_natives_required` | 内联 foreach + `CheckRules(item.Get("rules"))` + `SelectNativeClassifier(item)`,`flag` + `break` | 抽成 `internal static bool _natives_required(resolved, os, processArch, osVersion)`(命中即 `return true`,语义与 break 相同);`_check_natives` 用它 | 同上;并保留「这里没有 clientreq 过滤」的原口径(与 `McLauncher._needs_natives` 差这一处) |
| `Launcher\OfficialMigrate.cs` | `_library_relpaths`(129-182) | 按 `_inherit_chain` 边读盘边遍历,`Installer.SelectNativeClassifier(pyDict)` 读进程架构 | public 方法只负责「按链读好 JSON → 转调」;新增 `internal static List<string> _library_relpaths(List<PyDict> chain_jsons, os, processArch, osVersion)` 与私有 `_merged_libraries(chain_jsons)`(只含 `libraries` 键,按链顺序汇总,给 `LaunchArch.For` 用);`SelectNativeClassifier(pyDict, os, arch)` | 任务书指定做法:先用「链上所有 JSON 的 libraries」算目标架构,再照原顺序遍历;非 mac 平台上 `LaunchArch.For` 返回进程架构原值,顺序与去重逻辑一字未动 |
| `Launcher\MacNativeSupport.cs` | 类注释 66-67 | 「本包**不接入任何调用点**,只给 Lead 裁决…」 | 「B2d 起由 `LaunchArch` 接入:只有 osx + 进程架构 arm64 时会问它…」 | 本包接入了;行为、判定口径、方法体一行未改(Lead 只授权这一句) |

`Installer.cs` 里 `ExtractNatives` 的抽取有**一处非功能性差别**(已写在方法注释里):原来「缺 jar 的告警」与「解压」是逐库交错的,
现在先算完「要解压哪些」再逐个解压 —— 告警顺序可能前移,解压顺序与文件内容不变,且 `num > 0 && NativesPresent(text)` 的收尾判断未变。
`McLauncher` 的私有 3 参 `_expand_args` 现在没有调用方(遗留 5)。

### 5. 新文件

- `src/TipCL.Core/Launcher/LaunchArch.cs`(58 行):第 1 项的目标架构纯函数 + 类注释里的判定依据。
- `tests/TipCL.Tests/B2dFixtures.cs`:B2d 的版本 JSON 夹具(8 个取证版本 + 人造 Mixed / NoMacNatives / 继承链 /
  「只有键没有 path 的 classifiers」),**不含用例**。
- `tests/TipCL.Tests/B2dLaunchArchTests.cs`:56 条。
- `tests/TipCL.Tests/B2dCallSiteArchTests.cs`:53 条。
- `tests/TipCL.Tests/B2dNonAppleNailsTests.cs`:23 条。

### 6. 测试(新增 132 条用例,总数 1038 → 1170)

- `LaunchArch.For`:8 个版本 × 5 种平台组合逐格断言(40 条:`osx+arm64` 里 6 个老版本 → `x64`、1.19.4 / 26.3 → `arm64`;
  `osx+x64`、`windows+x64`、`windows+arm64`、`linux+arm64` 一律等于进程架构),再加 null JSON、空 `libraries`、
  人造 `Mixed` → x64、人造 `NoMacNatives` → 进程架构、`Arm64Native` 显式钉 Kind、确定性(同一版本两次调用相同、
  与 osVersion 无关)、「只有 6 个老版本被改架构」、公开入口在本机恒等于 `Utils.ARCH`、注入本机三字段 == 注入字面量。
- 每个调用点:osx + arm64 下 1.16.5 形态选到 x86_64 分类器(`natives-macos`,与 osx + x64 相同)、
  1.12.2 老式参数版本选到 `natives-osx`、1.19.4 / 26.3 形态与改动前相同(新式库任何架构都返回 null);
  `_natives_to_extract` 在 `TestHome.Dir` 下自造两个空 jar 后按 osx+arm64 拿到那两条 x86_64 路径(与 osx+x64 相同、
  与 windows 注入相同),并另有一条「jar 不存在时返回空列表」证明前一条真靠文件命中;
  `Cleaner._lib_relpaths` 按 Lead 要求断言 x86_64 natives jar **在保留集合里**,并另有一条用「只有键没有 path 的
  `natives-macos-${arch}` 分类器」证明保留集合里那条 maven 兜底路径确实由目标架构算出来;
  缺 natives 预检 `_needs_natives` 与 `Preflight._natives_required` 逐版本断言(旧版本需要、1.19.4 / 26.3 不需要、
  windows 两种形态与改动前一致),并对照「按进程架构 arm64 直接判」的旧写法证明 B2b 记的风险已消失;
  `OfficialMigrate._library_relpaths` 用「子版本有 arm64 + 父版本 1.16.5」的混合链断言整链退 x64、1.19.4 链不变、
  顺序与去重照旧。
- 「非 Apple 芯片行为未变」钉子:arch 型纯函数用 `Utils.ARCH` 与 `LaunchArch` 算出的值调用结果逐项相等
  (Windows 与 Intel Mac 两种注入);(os, 进程架构, osVersion) 型纯函数用「本机三字段」与「字面量 windows + x64」逐项相等。
- 期望值全部来自「读过的源码行为」或「B2b 的线上证据」,每条用例的注释里都写明是哪一种。

### 7. 验证命令与实际结果(全部在本机真实执行过)

| 检查 | 结果 |
|---|---|
| 开工前 `%LOCALAPPDATA%\TipCL` 元信息 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线**一致**(不一致就停手,本次没有出现) |
| `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,0 错误;警告**只有 SYSLIB0014 ×1**(`Sync\PhoneClient.cs:116`,与 B2b 相同) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0:**通过 1170 / 失败 0 / 跳过 0**(862 ms,没有 `Win32Exception (5)`,没有超时) |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0:**1170 通过 / 0 失败 / 0 跳过 / 不支持 0**,与 `dotnet test` 一致;B2d 三个文件 56 + 53 + 23 = 132 条 |
| `%TEMP%` 的 `tipcl*` 目录名字集合(两条通道各前后各一次) | 86 → 86(两条通道都是),测试窗口内**新增 0 个** |
| `--smoke --theme light / dark` | 退出码 0 / 0;**3739 / 3751 字节**,与 A1 起一直一致 |
| 测试后 `%LOCALAPPDATA%\TipCL` 元信息 | 9328 个文件 / 889530606 字节 / 2026-11-10 20:42:16,**与开工时逐项一致** |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | **0 个** |
| `src\TipCL.Core\` 全部 `.cs` 对 Windows 源比 SHA256 | 总数 **246**;一致 **224**、有改动 **14**、Windows 源里没有对应文件 **8**。有改动的 14 = 原有 11(`Ai\ExtraTools.cs`、`GlobalMods.cs`、`Java.cs`、`Sysinfo.cs`、`Plugins\DotNetPluginContext.cs`、`Plugins\Sdk\PluginSdk.cs`、`Plugins\Sdk\PluginUiRegistry.cs`、`Plugins\SecretStore.cs`、`Launcher\Utils.cs`、`Launcher\Installer.cs`、`Launcher\McLauncher.cs`)+ 本包 3 个(`Launcher\Cleaner.cs`、`Launcher\OfficialMigrate.cs`、`Launcher\Preflight.cs`);没有对应文件的 8 = 原有 7 + 本包 `Launcher\LaunchArch.cs` |
| 联网请求数(本包) | **0**(所需证据 B2a / B2b 已取好,`artifacts\b2b\` 只读复用;没有启动任何外部进程) |

### 8. 未验证项(本机是 Windows,没有 Mac)

1. **整条 Rosetta 路径未在真机上验证**:Apple 芯片上目标架构为 x64 之后,`SelectNativeClassifier` /
   `${arch}` / `os.arch` / classpath / 预检在 mac 上的真实取值链(`Utils.OS_NAME == "osx"`、`Utils.ARCH == "arm64"`
   是推断),本机只测到纯函数。
2. x64 Java(经 Rosetta)能否加载这些 x86_64 `.dylib`、老版本在 Apple 芯片上的实际启动结果 —— 属 B2e 与真机。
3. 1.19.4 及更新的版本在 Apple 芯片上「目标架构 arm64 + 两个 natives jar 都进 classpath」是否正确(B2b 遗留第 2 条)。
4. 模组 / 整合包版本(继承链合并后的 JSON)上的目标架构判定:`MacNativeSupport` 可能见到 vanilla 8 份 JSON 里
   没有的 classifier 写法(B2b 遗留第 4 条,本包把它的结论接到了真实调用点上,风险面因此变大 —— 见遗留 2)。

### 9. 遗留与风险

1. `_lib_relpaths`(Cleaner)的抽取改变了 `Cleaner` 内部结构(行为等价:每个版本的保留集合逐项相同,
   只是改成先算 `arch` 再进循环)。若 Lead 认为超出「加可注入重载」的范围,回退方式是把 `_lib_relpaths` 的方法体
   搬回 `_lib_paths` 的循环、循环里传 `Utils.*`,B2d 的 Cleaner 用例随之改成对 `Installer.SelectNativeClassifier`
   的断言(与 B2b 遗留第 5 条同型)。
2. `Mixed` 在真实数据里还没出现过(8 份 vanilla JSON 只有 `X64Only` / `Arm64Native`);模组版本一旦出现
   「一半有 arm64、一半没有」,会整份退到 x64 —— 这是 Lead 裁决的口径,若真机上发现某个「没有 arm64 变体」的库
   其实是 universal 二进制,要改的是 `MacNativeSupport._key_arch`(B2b 遗留第 4 条)。
3. `_install_libraries` 的目标架构算了一次(给 rules 过滤用),`_native_classifier_entry` 又按传入的同一个 arch 工作 ——
   两处用的是同一个值(方法注释写明传入的是目标架构);`Preflight._natives_entry` 同理。若 Lead 希望
   完全只留一个计算点,做法是把 rules 过滤也一起抽成同一个纯函数。
4. `ExtractNatives` 的告警与解压顺序变化(见第 4 节末),无副作用、不改文件内容,但 diff 里能看出来,留给 Lead 复核。
5. `McLauncher` 的私有 3 参 `_expand_args`(63-66 行)现在**没有调用方**(它是 B2b 留的转调壳);本包按
   「不扩大 diff」保留,如果 Lead 认为死代码该删,直接删掉那 4 行即可(测试对它没有断言)。
6. `Preflight._natives_required` 与 `McLauncher._needs_natives` 是两处口径略有差别的判定(clientreq 过滤),
   本包按「各自保持原样」处理,没有合并成一个(B2b 的调用点清单也是分开列的)。

### Lead 验收(2026-11-12 20:45)

结论:**B2d 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 224 一致、14 个有改动(原有 11 个 + 本包 `Cleaner.cs`、`OfficialMigrate.cs`、`Preflight.cs`);无对应文件的 8 个是原有 7 个 + `Launcher\LaunchArch.cs` |
| `LaunchArch.cs`(Lead 全文读过) | 只有 `osx` + 进程 `arm64` + `X64Only` / `Mixed` 返回 `x64`,其余返回进程架构原值 |
| `git diff b745387..HEAD` 对 5 个复制文件(Lead 逐行读过) | 全部是「方法顶部算一次目标架构 + 调用换成带参重载」或逐句等价的抽取 |
| `Cleaner._lib_relpaths` 对照改动前的循环体 | 逐句等价;三个加入保留集合的分支(artifact 路径、natives jar、classifiers 全部路径)都在。有 3 条测试钉住,含「Apple 芯片上 1.16.5 形态的 x86_64 natives jar 在保留集合里」 |
| `Installer.ExtractNatives` 对照 Windows 原文 377–414 行 | 过滤、选分类器、缺 jar 告警、解压、计数、收尾判断都在;差别只有告警与解压不再逐库交错 |
| `Java.cs`、`JavaRuntimeFiles.cs`、`Utils.cs`、红线文件、`Directory.Build.props`、三个 csproj | 未改 |
| `MacNativeSupport.cs` | 只改了类注释两行 |
| 已有测试被改动的 | 只有 `B2bNativeClassifierTests.cs` 一行 `DisplayName`(Lead 授权的那处),断言未动 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1170 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1170 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 86 → 86,没有新名字 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 0 个 |
| B2d 测试扫描 | 没有联网、起进程、调安装 / 启动 / 清理入口、改环境变量 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

**对子代理 4 个待裁决事项的裁决**:

1. `ExtractNatives` 的告警与解压不再交错:接受。
2. `McLauncher` 私有 3 参 `_expand_args`(63–66 行)已无调用方:**B2e 删除**。留着的风险是以后有人顺手调它,悄悄走回进程架构。
3. `_native_classifier_entry` / `_natives_entry` 入参是已算好的目标架构:接受。
4. `Preflight._natives_required` 与 `McLauncher._needs_natives` 的 `clientreq` 口径差:保持 Windows 版原样,不合并。

**当前是半成品状态**:Apple 芯片上的旧版本现在会选对 x86_64 原生库,但 Java 的挑选与安装仍按进程架构(arm64)走,要等 B2e 才自洽。

给 B2e 的事实(Lead 实测 `Java.cs`,行号是当前 HEAD):

- `JavaUsableFor(version_json, java_exe)`(347 行)是所有挑选路径的闸门:`ResolveLaunchJava`(373 行)、`McLauncher._coerce_java_exe`(135 行)、`BackendApi.cs:3096` 都靠它。它只比较大版本,**不看架构**,且内部调 `GetJavaMajor`(会起 `java -version` 进程)。
- `PickJavaForVersion`(987 行)的局部函数 `Match` 只按大版本筛;`EnsureJavaForVersion`(1342 行)先调它,拿到非空结果就直接返回。所以挑选不看架构时,装好的 arm64 Java 会挡住 x64 Java 的安装。
- `EnsureJavaForVersion`:组件不是 `jre-legacy` 时走 `InstallMojangRuntime(dm, component, …)`(1166 行,内部 5 处读静态 `PLATFORM_KEY`),否则走 `InstallAdoptium(dm, major, …, arch)`。
- 启动器自己装的 Java 目录名带架构:`adoptium-<major>-<arch>`、`zulu-<major>-<arch>`、`<component>-<PLATFORM_KEY>`,不同架构不会互相覆盖。
- 代码里目前没有任何「探测一个 Java 是什么架构」的逻辑(只有 1016 行按路径里有没有 `x86` 字样判断 32 位)。
- `Installer._ensure_any_java()`(`Installer.cs:969`)在找不到任何 Java 时装 Adoptium 8;B2a 的线上证据是 Adoptium 对 mac aarch64 **没有** Java 8,所以这条兜底在 Apple 芯片上会失败。

---

## B2e Apple 芯片旧版本:Java 的挑选与安装按「本次启动的目标架构」

> 本包做任务书的 6 项。**改动的复制文件只有 3 个**(`Launcher\Java.cs` +54/−11、`Launcher\McLauncher.cs` +5/−7、
> `Launcher\Installer.cs` +4/−1),新逻辑全在新文件 `src\TipCL.Core\Launcher\JavaArch.cs`(381 行)
> 与 5 个测试文件里。`BackendApi.cs` 只读了两小段(第 6 项取证),**未改**。

### 1. 六项各自做了什么

| 项 | 结论 | 落点 |
|---|---|---|
| 1 探测 Java 架构 | **新增**。`JavaArch.Detect`:先读 `<JAVA_HOME>/release` 的 `OS_ARCH=`(`aarch64`/`arm64` → arm64,`x86_64`/`amd64`/`x64` → x64,`i386|i486|i586|i686|x86` → x86),读不到再按启动器目录名推断(`<厂商>-<major>-<arch>` 三段名 / `…-mac-os[-arm64]`),都不行 → null。**只读这一个文件,不起进程** | `JavaArch.cs` |
| 2 可用性与挑选按目标架构 | **改**。`Acceptable` 真值表(非 Apple 恒 true;跨架构只要 x64,探测不出来按不可用;同架构恒 true)、`Rank`/`FilterForTarget`(跨架构只留 x64;同架构 arm64/null 稳定排前)。`JavaUsableFor` 追加架构判断(非 Apple 先 return true);`PickJavaForVersion` 的 `prefer` 分支与 `Match` 都过一遍 | `JavaArch.cs` + `Java.cs` |
| 3 安装按目标架构 | **改**。`InstallMojangRuntime` 加带 `platformKey` 的 internal 重载(内部 5 处静态 `PLATFORM_KEY` → 入参);`JavaArch.PlanInstall` 纯函数给平台键与 Adoptium 的 `arch`;`EnsureJavaForVersion` 按计划执行 | `JavaArch.cs` + `Java.cs` |
| 4 Rosetta 提示 | **加**。跨架构时 `ResolveLaunchJava` 通过 `on_note` 发 `JavaArch.RosettaNote` 并 `Utils.log.Info` 记一条;`_coerce_java_exe` 抛 `LaunchError` 时用 `JavaArch.WithRosettaNote` 追加同一句(原文案一字不改)。**不检测 Rosetta、不阻断、不加 `arch -x86_64`** | `JavaArch.cs` + `Java.cs` + `McLauncher.cs` |
| 5 两处小事 | (a) 删掉 `McLauncher` 私有 3 参 `_expand_args`(grep 确认无调用方,含测试);(b) `Installer._ensure_any_java()` 的兜底大版本走 `JavaArch.FallbackMajor`(Apple 芯片 17,其余 8)—— 用途先查清是「跑 Forge/NeoForge 处理器 jar,挑 Java 时不看版本」,见 D-51 | `McLauncher.cs` + `Installer.cs` |
| 6 核对传入的版本 JSON | **只读取证,未改**(结论见下面第 5 节;两处需要 Lead 裁决的写进回报) | 无 |

### 2. `release` 的取证结果(第 1 项要求先取证)

用脚本按键取 `artifacts/b2a/` 里 B2a 留下的两份 Mojang 运行时清单(不入库,只打印聚合结果):

| 清单 | `files` 条目 | `jre.bundle/Contents/Home/release` | 该条目形态 |
|---|---|---|---|
| `mac-os-arm64` / `java-runtime-gamma` | 437 | **有** | `type=file`、`executable=false`、`downloads.raw` 有(1074 字节) |
| `mac-os` / `jre-legacy` | 179 | **有** | `type=file`、`executable=false`、`downloads.raw` 有(528 字节) |

结论:mac 上启动器自己装的 Java 也是 `<JAVA_HOME>/release`(与 Adoptium 的包布局一致),
所以第 1 项的探测路径成立。**`release` 里 `OS_ARCH` 的真实取值本机没有 mac 的 JDK 可看 → 未验证**
(测试里用的是自造的文本,见第 6 节)。

顺带核对(给第 3 项的平台键做证据,`all.json` 的组件是**按平台列全部键、没有的给空数组**):
`mac-os` 非空 7 个(含 `java-runtime-beta`、`jre-legacy`),`mac-os-arm64` 非空 4 个
(`delta`/`epsilon`/`gamma`/`gamma-snapshot`,**没有 beta、没有 jre-legacy**)—— 与 B2a 表格一致。

### 3. 第 6 项:逐调用点结论(传的是合并前还是合并后的版本 JSON)

| 调用点 | 传的 `version_json` | 依据 |
|---|---|---|
| `BackendApi.cs:2933` / `2936`(`preview_launch` 一类) | **合并后** | 2927 行 `version_json = Manifest.ResolveInherits(pyDict, pid => inst.VersionJson(pid))`,失败才退回 `pyDict` |
| `BackendApi.cs:3095` / `3096` / `3100` | **合并后** | 3055–3064 行同样先 `ResolveInherits` |
| `McLauncher.cs:578`(`_coerce_java_exe`) | **合并后** | 569–577 行 `pyDict2 = Manifest.ResolveInherits(pyDict, loadParent)`,`ResolveInherits` 抛异常时直接 `throw LaunchError`,**不会**退化成子 JSON |
| `BackendApi.cs:2958`(`export_launch_script`) | **子版本原文(未合并)** | 直接 `instance2.VersionJson(text)` |

后果与建议改法(第 4 节的风险 1、D-52 第 2 条):带 `inheritsFrom` 的版本(Forge 等)子 JSON 里没有原生库,
`LaunchArch.For` 会判成「没有 mac 原生库」→ 目标架构 = 进程架构(`arm64`),于是导出的启动脚本可能选到
arm64 的 Java(而真正启动走的是合并 JSON → x64)。红线不许改 `BackendApi.cs`,**本包未改**。

### 4. 对复制文件的每处改动

**(a) `src\TipCL.Core\Launcher\Java.cs`(+54 / −11)**

| 位置 | 原来 | 改成 | 为什么 |
|---|---|---|---|
| `JavaUsableFor`(现 364–386 行) | `return javaMajor.Value >= num;` | 大版本不够仍 return false;够的话先判 `IsAppleSilicon`,`不是`→直接 `return true`(与原来逐项相同、不读盘),`是`→`AcceptableForExecutable(os, proc, LaunchArch.For(version_json), exe)` | 挑选路径的闸门都要看架构,跨架构时 arm64 的 Java 加载不了 x86_64 原生库 |
| `ResolveLaunchJava` 方法开头(现 390–395) | 只有 `long num = RequiredJavaMajor(...)` | 追加:跨架构 → `on_note?.Invoke(JavaArch.RosettaNote)` + `Utils.log.Info("%s", JavaArch.RosettaNote)` | 第 4 项:无条件提示,不检测不阻断 |
| `PickJavaForVersion` 方法开头 | 只有 `long required = ...` | 追加 `targetArch`(只在 Apple 芯片上算 `LaunchArch.For(version_json)`,否则就是 `Utils.ARCH`) | 不在非 Apple 芯片上多做哪怕一次版本 JSON 计算 |
| `PickJavaForVersion` 的 `prefer` 分支 | `javaMajor.Value >= required` | 追加 `&& AcceptableForExecutable(os, proc, targetArch, text)` | 显式指定的 Java 也要过架构这关 |
| `PickJavaForVersion` 的局部函数 `Match` 第一行 | 直接 `from PyDict j in javas` | 先 `javas = JavaArch.FilterForTarget(javas, os, proc, targetArch);`,其余三处筛选 / 取最小大版本**逐字未改** | 非 Apple 芯片上这步原样返回同一个列表(单测钉了 `Assert.Same`) |
| `InstallMojangRuntime` | 一个 public 方法,内部 5 处读静态 `PLATFORM_KEY`(1169/1172/1189/1196/1198 行) | 拆成 public 转调 + **internal 重载**(多一个 `platformKey` 入参),5 处换成入参;方法体其余逐字不变 | 跨架构要装 `mac-os`(它有 `jre-legacy`/`java-runtime-beta`) |
| `EnsureJavaForVersion` 主体 | `if (Py.Truthy(obj) && !Py.Eq(obj,"jre-legacy"))` 里调 `InstallMojangRuntime(dm, component, on_progress, force)`;末尾 `arch = _IsLegacy32bit(version_json) ? "x86" : null` | 先算 `JavaArch.PlanInstall(...)`,再 `if (plan.TryMojang)` → `InstallMojangRuntime(dm, component, plan.MojangPlatformKey, ...)`;末尾 `InstallAdoptium(..., plan.AdoptiumArch)` | 平台键与 Adoptium 的 arch 都按目标架构;`TryMojang` 与原来的 `Py.Truthy(obj) && !Py.Eq(obj,"jre-legacy")` 等价 |

**(b) `src\TipCL.Core\Launcher\McLauncher.cs`(+5 / −7)**

| 位置 | 原来 | 改成 | 为什么 |
|---|---|---|---|
| 63–66 行 | 私有 3 参 `_expand_args` 转调 6 参重载 | **删除**(grep 全仓确认只有它自己那一行在调用 6 参版);6 参版的 XML 注释补一句说明删因 | B2d 的 Lead 裁决;留着会有人顺手调它、悄悄走回进程架构 |
| `_coerce_java_exe` 的 `throw new LaunchError` | 长中文文案直接构造 | 先放进 `text2`,再 `throw new LaunchError(JavaArch.WithRosettaNote(text2, Utils.OS_NAME, Utils.ARCH, LaunchArch.For(resolved)))`;**文案本身逐字未改**(全角标点用 `\uXXXX` 核对过) | 第 4 项:跨架构时在原文案后追加 Rosetta 提示;非跨架构原样返回 |

**(c) `src\TipCL.Core\Launcher\Installer.cs`(+4 / −1)**

| 位置 | 原来 | 改成 | 为什么 |
|---|---|---|---|
| `_ensure_any_java()` 最后一行(现 996 行) | `Java.InstallAdoptium(dm, 8L, on_progress);` | `Java.InstallAdoptium(dm, JavaArch.FallbackMajor(Utils.OS_NAME, Utils.ARCH), on_progress);` | Apple 芯片上 Adoptium 没有 Java 8 的 aarch64 包(线上证据),兜底改成 17;其它平台仍 8(见 D-51) |

### 5. 新文件

- `src\TipCL.Core\Launcher\JavaArch.cs`(381 行,全部为新代码):`RosettaNote` 常量、`IsAppleSilicon`、
  `IsCrossArch`、`Acceptable`、`AcceptableForExecutable`(生产重载 + 可注入读取委托的重载)、
  `FilterForTarget`(同上两个重载)、`Rank`、`PlanInstall`(+ 嵌套的 `InstallPlan`)、`FallbackMajor`、
  `WithRosettaNote`、`Detect(string)`(生产入口)/ `Detect(string, Func<string,string>)`(可注入)、
  `MapOsArchValue`,以及私有的 `_ParseOsArch` / `_Unquote` / `_InferFromDirectoryNames` / `_JavaHomeOf` /
  `_LastSeparatorBefore` / `_ExeOf` / `_ReadTextOrNull`。
- 测试 5 个文件(都只测纯函数 / `TestHome.Root` 下自造的小文件,全部不联网、不起进程):
  `B2eJavaArchDetectTests.cs`、`B2eJavaAcceptableTests.cs`、`B2eJavaInstallPlanTests.cs`、
  `B2eNonAppleNailsTests.cs`、`B2eRosettaNoteTests.cs`。

### 6. 测试(新增 235 条用例,总数 1170 → 1405)

| 文件 | 覆盖 | 用例数 |
|---|---|---|
| `B2eJavaArchDetectTests` | `release` 里各种 `OS_ARCH` 写法(引号 / 空白 / 多行 / CRLF / 大小写 / 未知值 / 没有该行)、`jre.bundle/Contents/Home/bin/java` 与 `…/Contents/Home/bin/java` 的 `release` 位置、读取委托返回 null / 抛异常、目录名推断的各种形态、冲突以 `release` 为准、生产入口读 `TestHome.Root` 下的真实文件、`MapOsArchValue` 全表 | 82 |
| `B2eJavaAcceptableTests` | `Acceptable` 真值表 36 格(os×进程×目标×探测)、`IsAppleSilicon` / `IsCrossArch`、可注入读取委托的入口(非 Apple **一次都不读**、Apple 每个候选正好读一次)、`FilterForTarget` 跨架构只留 x64 / 同架构稳定排序(逐项 `Assert.Same` 钉顺序)/ 非 PyDict 项当「不知道」、`Rank` | 74 |
| `B2eJavaInstallPlanTests` | 安装计划 3 形态 × 6 平台组合逐格(平台键 / Adoptium arch / TryMojang)、非跨架构平台键 == `Java.PlatformKeyFor(os, 进程架构)`、`legacy32 ? x86 : null`、空 component 与 `jre-legacy` 不走 Mojang、**夹具 → `LaunchArch.For` → 安装计划** 的端到端 3 条(1.16.5 → Adoptium `x64`;1.18.2 → Mojang `mac-os`;1.19.4 → Mojang `mac-os-arm64`)、非 Apple 同夹具钉子、`FallbackMajor` | 51 |
| `B2eNonAppleNailsTests` | 生产入口读真实文件、非 Apple 恒 true 且不看盘、Apple 跨架构 / 同架构的真实文件判定 | 16 |
| `B2eRosettaNoteTests` | 文案含 `softwareupdate --install-rosetta`、中文用 `\uXXXX` 断言、不含 `arch -x86_64`、跨架构拼接 / 非跨架构原样返回 | 12 |

**期望值来自哪** 每种都在注释里标了:非 Apple 与 `legacy32 ? x86 : null` / 平台键 == `PLATFORM_KEY`
来自**改动前的源码行为**;`mac-os` / `mac-os-arm64` 的键名与「有没有 `jre-legacy` / `java-runtime-beta`」
来自 **B2a 的线上证据**;Adoptium 没有 mac aarch64 的 Java 8 同理;目标架构来自 **B2d 的夹具与结论**。

**写测试时抓到的真 bug(已修,记 D-50)**:第一版把「读取 `release` 的委托」与「探测架构的委托」搞混,
生产代码给 `AcceptableForExecutable` / `FilterForTarget` 传了 `JavaArch.Detect` 当读取器 →
探测退化成只看目录名。现在生产重载不带委托,单测另有真实文件那一条钉住生产路径。

### 7. 验证命令与实际结果(全部在本机真实执行过)

| 命令 | 结果 |
|---|---|
| `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;警告 **1 个**(`-v:n` 复核:`PhoneClient.cs(116,38) SYSLIB0014` ×1,与基线相同) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 **1405** / 失败 0 / 跳过 0 |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0,通过 **1405** / 失败 0 / 跳过 0 / **不支持 0** |
| `TipCL.exe --smoke --theme light/dark --out artifacts\smoke-*.png` | 退出码 0 / 0;`smoke-light.png` **3739 字节**、`smoke-dark.png` **3751 字节**(与基线一致) |
| 字节一致性自检(`artifacts\b2e-bytecheck.ps1`,对 `D:\项目\tipcl\windos\tipcl\src\TipCL\Core`) | mac 侧 247 个 `.cs`:有对应文件的 238 个里 **224 一致 / 14 有改动**(含本包改的 `Launcher\Java.cs`、`Launcher\McLauncher.cs`、`Launcher\Installer.cs`);**无对应文件 9 个** = 原有 8 个 + `Launcher\JavaArch.cs` |
| `%LOCALAPPDATA%\TipCL`(开工时 / 测试后) | 两次都是 **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 给的三项基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合 | `dotnet test` 前后 **86 → 86**、进程内执行器前后 **86 → 86**,测试窗口内**没有新名字** |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | **0 个** |
| 测试扫描 | 5 个新文件都没有联网、起进程、调 `GetJavaMajor` / `ResolveLaunchJava` / 任何 `Install*`、没有实例化 `BackendApi` / `BridgeServer` / `PluginManager`、没有改 `TIPCL_HOME`、临时文件都写在 `TestHome.Root` 之下 |

### 8. 未验证项(本机是 Windows,没有 Mac)

1. **mac 上整条 Rosetta 路径**:x64 Java 经 Rosetta 起 JVM、x86_64 原生库的加载 —— 一律未验证。
2. **`release` 里 `OS_ARCH` 的真实取值与拼写**:Adoptium / Azul / Mojang 的 mac 包里这个文件长什么样,
   本机没有 mac 的 JDK 可看;测试用的是自造文本。**唯一核对过的是「这个文件在包里的相对路径」**(第 2 节)。
3. 探测不出架构时会**自动下载一个 x64 的 Java**(`Acceptable` 把 null 当不可用)—— 这个「自动下载」
   在 mac 上的真实效果未验证。
4. Apple 芯片上 `_ensure_any_java()` 改成装 Java 17 后,老 Forge 的处理器 jar 能不能被 Java 17 跑起来未验证(D-51)。
5. `PlatformKeyFor("osx","x64") == "mac-os"` 与 `mac-os` 的 `java-runtime-beta` 清单能**下载成功**未验证
   (B2a 只查了 `all.json` 的键与两份文件清单,没有下载过任何运行时)。

### 9. 遗留与风险

1. **`export_launch_script` 传的是未合并的子版本 JSON**(第 3 节):导出的启动脚本可能选到 arm64 的 Java。
   建议改法:像另外两个调用点那样先 `Manifest.ResolveInherits`(在 `BackendApi.cs` 内,本包红线不许改)。
2. **`JavaForInstaller("forge-legacy")` 仍装 Adoptium 8**(`Java.cs:1382`):Apple 芯片上必失败
   (Adoptium 没有 8 的 aarch64 包;Azul 有,见 B2a 的 Lead 补查)。本包范围没点名这个方法,**未改**。
3. `JavaUsableFor` / `PickJavaForVersion` 每次调用都要探一次架构(读一次 `release`),没有缓存 ——
   只有在 Apple 芯片上才发生,量级是「几个候选各读一个小文件」,本包没有加缓存(改动面最小优先)。
4. `FilterForTarget` 在跨架构时会把**探测不出来**的候选(包括 `/usr/bin/java` 这类系统 Java)全部滤掉:
   这是任务书裁决的保守做法,代价是 Apple 芯片跑老版本时更可能触发自动下载。
5. `JavaArch` 的目录名推断只认启动器自己会创建的两种名字形态;用户自己放到别处、又没有 `release`
   的 Java 一律探测不出来(按任务书「探测不出来按不可用」处理)。

### Lead 验收(2026-11-12 21:15)

结论:**B2e 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 224 一致、14 个有改动(与 B2d 相同的 14 个);无对应文件的 9 个是原有 8 个 + `Launcher\JavaArch.cs` |
| `git diff 496c1ce..HEAD` 对 `Java.cs`、`McLauncher.cs`、`Installer.cs`(Lead 逐行读过) | `JavaUsableFor` / `PickJavaForVersion` 在非 Apple 芯片上先返回、不探测;`InstallMojangRuntime` 只把 5 处 `PLATFORM_KEY` 换成入参;`EnsureJavaForVersion` 改走 `PlanInstall`;`McLauncher` 删掉无调用方的 3 参 `_expand_args`,报错文案只在跨架构时追加一句;`Installer` 只改兜底大版本一处 |
| `JavaArch.cs`(Lead 全文读过,452 行) | 探测只读 `<JAVA_HOME>/release`,不起进程;所有判断以「`osx` + 进程 `arm64`」为前提;没有 Rosetta 检测,没有给启动命令加前缀 |
| `BackendApi.cs`、`LaunchArch.cs`、`MacNativeSupport.cs`、`Utils.cs`、`Cleaner.cs`、`Preflight.cs`、`OfficialMigrate.cs`、`JavaRuntimeFiles.cs`、红线文件、`Directory.Build.props`、三个 csproj | 未改 |
| 已有测试被改动的 | 0 个 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1405 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1405 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 86 → 86,没有新名字 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 0 个 |
| B2e 测试扫描 | 没有联网、起进程、调 `Java` 的挑选 / 安装 / 探测版本入口、改环境变量;`JavaArch.Detect(path)` 的调用只读 `TestHome.Root` 下的文件 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

**对子代理 3 个待裁决事项的裁决**(Lead 读过 `BackendApi.cs` 2924–2963、3084–3105 与 `Java.cs` 1414–1443 核实):

1. **`BackendApi.cs:2958`(`export_launch_script`)传未合并的版本 JSON**:属实,另外三个调用点传的都是 `ResolveInherits` 的结果。
   这个功能导出的是 `.bat`(`VersionOps.ExportLaunchBat`),在 mac 上本来就要另做 `.command` / `.sh`,归 B2c 的「导出启动脚本」项一并处理,
   届时把该调用点改成先 `Manifest.ResolveInherits`(只在非 Windows 分支,Windows 上的行为不动)。在那之前 mac 上不要依赖导出脚本。
2. **`Java.JavaForInstaller("forge-legacy")` 在 Apple 芯片上装 Adoptium 8 必失败**:属实(Adoptium 没有 mac aarch64 的 Java 8)。
   裁决:Apple 芯片上这一处装 **Adoptium 8 的 x64 包**(目录 `adoptium-8-x64`)。Forge 安装器是纯 Java,x64 的 JVM 经 Rosetta 能跑;
   而 1.17 之前的版本在 Apple 芯片上本来就要用 x64 的 Java 8 启动,装同一个包可以复用,不必再多下一个 Zulu。归 B2c。
3. **`_ensure_any_java()` 兜底 8 → 17**:接受。老 Forge 处理器 jar 在 Java 17 上能否运行列入真机验证清单。

**Apple 芯片旧版本这条路径现在在代码层面自洽**(目标架构 x64 → x86_64 原生库 → x64 Java → Mojang `mac-os` 清单或 Adoptium x64),
但**全程没有在 Mac 上运行过**。真机清单里至少要验:`release` 文件里 `OS_ARCH` 的真实取值、x64 的 `java` 是否确实自动经 Rosetta 启动、
没装 Rosetta 时的实际报错、1.16.5 与 1.18.2 能否进入游戏。

B2c 拆成两包:

- **B2c1(Java 与安装的收尾)**:Mojang 安装循环建链顺序与已存在链接、Microsoft 厂商分支、`forge-legacy` 的 Java 8、`Preflight` 磁盘空间守卫、`GlobalMods.IsSymlink`。
- **B2c2(其余平台差异)**:NodeRuntime darwin、Terracotta 可执行权限、adb 路径、`PhoneLink` 临时目录、Updater 仅提示、Shortcut、导出启动脚本。

---

## B2c1 Java 与安装的收尾(建链顺序与幂等 / Microsoft 分支 / forge-legacy 的 Java 8 / 磁盘空间 / IsSymlink)

本包做任务书点名的 5 项,每一项都只用「加非 Windows 分支」或「只改 mac 分支」的形态,Windows 分支的代码路径保持原样,
并用单测钉住。**没有**碰 B2c2 的任何一项(NodeRuntime / Terracotta / adb / PhoneLink 临时目录 / Updater / Shortcut / 导出启动脚本),
也没有改 B2d / B2e 已验收的目标架构逻辑。

### 1. 五项各自做了什么

1. **Mojang 运行时安装的建链顺序与幂等(已改)**
   - `Java.cs` 的清单遍历循环里,`type == "link"` 的条目原来**当场**调 `TryCreateRuntimeLink` 建链(此时 `DownloadAll` 还在后面),
     现在循环里只把 `(条目名, 目标)` 收进 `list3`,等 `dm.DownloadAll(...)` 返回之后、设可执行权限**之前**逐个建
     (与厂商 `tar.gz` 路径「文件写完再建链」一致)。Windows 分支一字未变:仍然不收集、不建链。
   - **幂等**:`JavaRuntimeFiles.DecideLink(existingTarget, pathExists, desiredTarget)` 是纯函数,四格:
     路径上没有 → **新建**;已是符号链接且目标字符串**逐字相同**(`Ordinal`) → **跳过且不记警告**;
     已是符号链接但目标不同 → **删除后重建**;路径上是普通文件或目录 → **跳过 + 记一条警告,不删**
     (宁可少建一条链接,也不删已经下载好的文件)。
   - 生产读取委托是 `FileSystemInfo.LinkTarget`;副作用出口是新增的 `JavaRuntimeFiles.LinkHooks`
     (读取既有目标 / 判存在 / 删除 / 建链),单测全部注入假委托,不建真实符号链接。
   - 厂商 `tar.gz` 解压路径(`SafeExtractTarGz`)**未改**:它的调用方在解压前先 `RemoveTree`,不需要幂等。
   - **修正 B2a 小节那句与代码不符的描述的方式**:按任务书要求**不改历史文字**。现在的真实顺序是:
     Mojang 路径 = 先 `DownloadAll`(下载全部文件)**再**建链(本包改的);厂商 `tar.gz` 路径 = 文件写完再建链(B2a 起就是这样)。
     B2a 的 Lead 验收已经把「先建链、后下载」记为更正,B2a 小节的正文与「遗留」第 5 条里那句括号仍未改(历史文字)。
2. **Microsoft 厂商分支在 mac 上(已改,走「改下载逻辑」那一支)** —— 见下面第 2 节的取证表与裁决。
3. **`forge-legacy` 在 Apple 芯片上的 Java 8(已改)**:`JavaForInstaller("forge-legacy", ...)` 找不到已装 Java 8 时,
   Apple 芯片上改传 `arch = "x64"`(`InstallAdoptium(dm, 8L, on_progress, false, JavaArch.Legacy8AdoptiumArch(Utils.OS_NAME, Utils.ARCH))`,
   装 `adoptium-8-x64`);其余平台该实参是 `null`,逐字等于改动前的 `InstallAdoptium(dm, 8L, on_progress)`。
   纯函数 `JavaArch.Legacy8AdoptiumArch(os, processArch)`:Apple 芯片 → `"x64"`,其余 → `null`。
4. **`Preflight.FreeBytes` 的非 Windows 分支(已改)**:新增可注入重载
   `FreeBytes(path, isWindows, windowsFree, unixFree)`;`FreeBytesHook` 优先级不变(非 null 时两个委托都不碰);
   路径经 `Utils.AbsPath` 规范化后交给对应委托;任何异常仍然返回 `null`。生产入口按 `Utils.IS_WINDOWS` 选
   `FreeBytesWindowsApi`(kernel32,改动前的实现逐字保留为独立方法)或 `FreeBytesDriveInfo`
   (`new DriveInfo(绝对路径).AvailableFreeSpace`)。唯一调用点 `Preflight.cs:125` 一行未动。
   「给定路径怎么找到所在卷」的做法与「mac 上未验证」写进了方法注释与 D-56。
5. **`GlobalMods.IsSymlink` 的非 Windows 分支(已改)**:新增内部重载
   `IsSymlink(p, isWindows, readLinkTarget)`;`isWindows` 为 true 的那一支是改动前实现的**逐字**搬运,
   非 Windows 上 `(readLinkTarget ?? LinkTargetOrDefault)(p) != null`(`LinkTargetOrDefault` 用
   `Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p)` 的 `LinkTarget`,悬空链接也成立),异常一律 false。

### 2. 第 2 项的取证表与采用的分支

取证时间 **2026-11-12 21:14:52 +08:00**;命令形态
`Invoke-WebRequest -Uri https://aka.ms/download-jdk/<name> -Method Head -MaximumRedirection 0 -UseBasicParsing -TimeoutSec 20`;
**共 6 个请求,全部只访问 `aka.ms`**,不跟随重定向、不下载任何文件(脚本留在 `artifacts\b2c1-head.ps1`,不入库)。
PowerShell 5.1 对 3xx 抛 `MaximumRedirectExceeded`,状态码与 `Location` 从异常的 `Response` 里取。

| 请求的短链名(前缀都是 `https://aka.ms/download-jdk/`) | 状态码 | `Location` |
|---|---|---|
| `microsoft-jdk-17-mac-x64.zip`(改动前 mac 会拼出来的形态) | 302 | `https://www.bing.com?ref=aka&shorturl=download-jdk/microsoft-jdk-17-mac-x64.zip`(aka.ms 兜底页 → 短链不存在) |
| `microsoft-jdk-17-mac-x64.tar.gz` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(同上) |
| `microsoft-jdk-17-mac-aarch64.zip` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(同上) |
| `microsoft-jdk-17-mac-aarch64.tar.gz` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(同上) |
| `microsoft-jdk-17-macos-x64.tar.gz` | 301 | `https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-x64.tar.gz`(真实的版本化包名) |
| `microsoft-jdk-17-macos-aarch64.tar.gz` | 301 | `https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-aarch64.tar.gz`(真实的版本化包名) |

**采用的分支**:任务书的第一种情况(证据明确:某个 `<os>` + `tar.gz` 重定向到真实包名,而 `mac` + `zip` 指向兜底页)
→ **改下载逻辑**:mac 的 OS 段 `mac` → `macos`、后缀 `.zip` → `.tar.gz`。
实现是三个纯函数 `JavaRuntimeFiles.MicrosoftJdkOsFor` / `MicrosoftJdkArchiveSuffixFor` / `MicrosoftJdkUrl`,
`Java.cs` 的 `_InstallMicrosoft` 只改一行转调(删掉了原来那行 `Pick(Utils.OS_NAME, ...)` 的 `text4`,它只被那行 URL 用)。
Windows / Linux 的地址**逐字不变**(含 Linux 那个「地址 `.zip`、落盘 `.tar.gz`」的原样矛盾 —— 红线不许动),
未知 OS 仍然抛 `KeyError`(与改动前的 `Java.Pick` 同语义)。落到 mac 上,地址后缀与落盘后缀(`ArchiveSuffixFor`)现在一致了。

### 3. 对复制文件的每处改动(共 3 个文件;新逻辑都在 `JavaRuntimeFiles.cs` / `JavaArch.cs` 这两个 B2a/B2e 建的新文件里)

`Java.cs`:

| 位置 | 原来是什么 | 改成什么 | 为什么 |
|---|---|---|---|
| `InstallMojangRuntime`(内部重载)声明 `list2` 处 | 只有 `List<DownloadTask> list2 = ...` | 多一行 `List<KeyValuePair<string, string>> list3 = new ...` | 收集 link 条目,延后建链 |
| 同上,`pyDict4` 遍历里的 `type == "link"` 分支 | 当场 `JavaRuntimeFiles.TryCreateRuntimeLink(Utils.AbsPath(text2), item.Key, ..., isWindows: false)` | `list3.Add(new KeyValuePair<string, string>(item.Key, Py.Str(...)))`(仍在 `!Utils.IS_WINDOWS` 里) | 顺序:下载完成后统一建链 |
| 同上,`if (list2.Count > 0) { dm.DownloadAll(...); }` 之后 | 没有这一段 | 新增 `if (!Utils.IS_WINDOWS) { foreach (...) JavaRuntimeFiles.TryCreateRuntimeLink(Utils.AbsPath(text2), item3.Key, item3.Value, isWindows: false); }` | 同上(位置选在 `DownloadAll` 之后、设可执行权限之前) |
| `JavaForInstaller` 的 `forge-legacy` 分支 | `return InstallAdoptium(dm, 8L, on_progress);` | `return InstallAdoptium(dm, 8L, on_progress, false, JavaArch.Legacy8AdoptiumArch(Utils.OS_NAME, Utils.ARCH));` | Apple 芯片上 Adoptium 没有 8 的 aarch64 包 |
| `_InstallMicrosoft` | `string text4 = Pick(Utils.OS_NAME, "windows", "windows", "osx", "mac", "linux", "linux");` + `string url = "https://aka.ms/download-jdk/microsoft-jdk-" + major + "-" + text4 + "-" + text3 + ".zip";`(2 行) | `string url = JavaRuntimeFiles.MicrosoftJdkUrl(Utils.OS_NAME, major.ToString(), text3);`(1 行 + 2 行注释) | mac 的正确形态是 `macos` + `.tar.gz`(D-54) |

`Preflight.cs`:`FreeBytes(string)`(私有)的**方法体**搬进新的 `internal static` 重载
`FreeBytes(string, bool, Func<string,long?>, Func<string,long?>)`,私有入口改成一行转调
`FreeBytes(path, Utils.IS_WINDOWS, FreeBytesWindowsApi, FreeBytesDriveInfo)`;kernel32 那段判断搬进
`FreeBytesWindowsApi`(逐字),新增 `FreeBytesDriveInfo`。P/Invoke 声明 `GetDiskFreeSpaceEx` 保留原样。

`GlobalMods.cs`:`IsSymlink(string p)` 原来只有实现体,现在拆成
`IsSymlink(string p)`(转调)、`internal static bool IsSymlink(string p, bool isWindows, Func<string, string> readLinkTarget)`
(`isWindows` 那一支是原实现逐字搬运)、`private static string LinkTargetOrDefault(string p)`。
P/Invoke 声明 `CreateSymbolicLinkW` 保留原样,`_link_file` 的两处调用点未动。

### 4. 新增的两个非复制文件里的新成员

- `JavaRuntimeFiles.cs`:`MicrosoftJdkOsFor` / `MicrosoftJdkArchiveSuffixFor` / `MicrosoftJdkUrl`(第 2 项)、
  `DecideLink`(纯函数)+ `LinkDecision` 枚举 + `LinkHooks` 副作用出口 + `TryCreateRuntimeLink` 的幂等实现
  + `ReadLinkTargetDefault` / `ExistsDefault` / `DeleteLink`(第 1 项)。`TryCreateRuntimeLink` 的旧签名靠
  「`warn` 之后追加一个可选参数 `hooks`」保持兼容,B2a 的 3 条既有用例一个字没改。
- `JavaArch.cs`:`Legacy8AdoptiumArch(os, processArch)`(第 3 项)。

### 5. 测试(新增 72 条用例,总数 1405 → 1477)

| 文件 | 覆盖内容 | 用例数 |
|---|---|---|
| `tests\TipCL.Tests\B2c1LinkOrderTests.cs` | `DecideLink` 四格 + 逐字比较(`../T` / `./../t` / 绝对路径)/ Windows 分支不碰任何 hook / 新建(相对目标原样保留)/ 目标相同跳过且无警告 / 目标不同删后建 / 普通文件跳过且不删 / 条目名逃出 / 目标逃出 / 状态读取委托抛异常 → 警告 + 跳过 / 真实文件(生产读取委托):普通文件一个字节没动 / 真实生产建链出口在 Windows 上不落盘 | 20 |
| `B2c1MicrosoftUrlTests.cs` | Windows 三个架构段逐字 / Linux 两个架构段逐字 / major 原样拼接 / mac 的 `macos` + `tar.gz` 两个架构段 / mac 地址不再含 `-mac-` 且不以 `.zip` 结尾 / 与线上 301 的 `Location` 同族 / OS 段映射 / 未知 OS 抛 `KeyError` / 后缀映射 / mac 的地址后缀与落盘后缀一致 | 20 |
| `B2c1Legacy8Tests.cs` | `Legacy8AdoptiumArch` 5 种组合 / 与 `IsAppleSilicon` 逐格重合 / 传进去的 `x64` 落到 Adoptium 地址仍是 `x64` / 非 Apple 传 null = 原行为 / 目录名是 `adoptium-8-x64` | 12 |
| `B2c1GlobalModsSymlinkTests.cs` | Windows 分支三种形态(普通文件 / 目录 / 不存在)+ Windows 分支不读 `LinkTarget` / 非 Windows:有目标 → true、null → false、两种异常 → false / 委托收到原样路径 / 生产读取委托对真实普通文件与目录都是 false | 11 |
| `B2c1PreflightFreeBytesTests.cs` | 两个平台标志各选对委托(另一个一次都不调)/ 委托收到绝对路径 / 两个标志下抛异常都返回 null / `FreeBytesHook` 优先且 finally 恢复 / hook 为 null 时委托生效 / 真实 `DriveInfo` 委托对 `TestHome.Root` 取到 > 0 / 真实 kernel32 委托仍取到值且与 `DriveInfo` 同量级 | 9 |

期望值来源逐条写在文件头注释里(**读过的源码行为** / **线上证据** / **本包新语义**三类分开标);
断言源码中文文案时用 `\uXXXX` 转义(D-16 / D-24)。用例都不联网、不起进程、不建真实符号链接、不实例化 `BackendApi`,
`Preflight.FreeBytesHook` 的改动在 `finally` 里恢复原值。

### 6. 验证命令与实际结果(全部在本机真实执行过)

| 命令 | 结果 |
|---|---|
| `dotnet build-server shutdown` | 退出码 0 |
| `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;警告 **1 个 = SYSLIB0014 ×1**(`Launcher\Sync\PhoneClient.cs:116`,与基线一致) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 **1477** / 失败 0 / 跳过 0,用时 953 ms(真跑成功,没有出现 `Win32Exception (5)`) |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0,总计 **1477**,通过 1477,失败 0,跳过 0,**不支持 0** |
| `cmd /c "...\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` | 退出码 0,**3739** 字节(与 A1/B2e 一致) |
| `cmd /c "...\TipCL.exe --smoke --theme dark --out artifacts\smoke-dark.png"` | 退出码 0,**3751** 字节(与 A1/B2e 一致) |
| 字节一致性自检(`src\TipCL.Core\` 全部 `.cs` 排除 `bin\` / `obj\`,对 Windows 源比 SHA256) | 总数 **247**,一致 **224**,有改动 **14**(与 B2e 相同的 14 个;本包改的 `Launcher\GlobalMods.cs` / `Launcher\Java.cs` / `Launcher\Preflight.cs` 都在这 14 个里),Windows 源里没有对应文件 **9**(与 B2e 相同,**没有新建 `.cs` 文件** —— 本包只往 B2a/B2e 既有的两个新文件里加成员) |
| `%LOCALAPPDATA%\TipCL`(开工时) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16(**与 Lead 基线逐项一致**,所以继续) |
| `%LOCALAPPDATA%\TipCL`(测试与冒烟之后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16(**与开工时逐项一致**) |
| `%TEMP%` 下 `tipcl*` 目录名字集合(每条测试通道前后各取一次) | 86 → 86,**没有新名字**(两次窗口都是 0 个新增) |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 0 个 |
| 本包联网请求数 | **6**(全部是第 2 项的 `HEAD aka.ms`,只读重定向头,不跟随、不下载) |

### 7. 未验证项(本机是 Windows,没有 Mac)

1. mac 上真实的建链 / 删除 / 「目标相同就跳过」/ 悬空链接行为 —— 全部用假委托 + 生产读取委托在真实普通文件上的钉子验证,
   真实符号链接一条都没建(红线)。
2. mac 上 `DriveInfo` 对任意子目录路径(尤其 `/`、外部卷、网络挂载)的行为;本机只验证了真实 `DriveInfo` 委托
   对 `TestHome.Root` 取到大于 0 的值。
3. Microsoft 短链只查了 `major = 17`;别的 major(8 / 21 / 25 等)在 mac 上是否同样存在、`macos` + `zip` 是否存在
   (后者与本包结论无关:我们要的就是 `tar.gz`)。
4. x64 的 Adoptium Java 8 经 Rosetta 跑 Forge 安装器的真实结果;1.17 之前的版本在 Apple 芯片上复用 `adoptium-8-x64` 的实际启动结果。
5. 第 1 项的**顺序**改动(建链移到 `DownloadAll` 之后)只做了代码审读 + 纯函数/委托级单测,没有端到端装一次 Mojang 运行时
   (`InstallMojangRuntime` 要下载文件,红线不许测试联网)。

### 8. 遗留与风险

1. **第 1 项的幂等只覆盖「目标字符串逐字相同」**:如果某次清单里目标的写法变了(`./../x` ↔ `../x`),会走「删除重建」,
   这是有意的(逐字比较能暴露清单变化),代价是多一次删建。
2. **`Conflict` 分支不删文件**:清单里 `link` 与 `file` 指向同一路径时,后到的一方会让先建好的东西留着不动
   (link 分支记一条警告)。mac 的 Mojang 清单(`mac-os-arm64/java-runtime-gamma`)里没有这种冲突条目,属防御性设计。
3. **Linux 的「地址 `.zip`、落盘 `.tar.gz`」矛盾原样保留**(Microsoft 与 Zulu 两处),红线不许改 Linux 的 URL 参数;
   mac 侧已一致。
4. **界面上的「可选厂商」是否把 Microsoft 从 mac 去掉:只记录,未改**(任务书第 2 项的第二种情况才要求记录是否去掉;
   本包走的是第一种情况,但界面清单本身不在本包范围内)。
5. **`Preflight.FreeBytes` 的非 Windows 分支换成 `DriveInfo` 后,「查不到」仍然是 null**:`CheckLaunch` 因此不产生磁盘告警,
   与改动前 mac 上的表现一致(不会误报空间充足)。
6. **`JavaForInstaller("forge-legacy")` 在 Apple 芯片上会多下一个 x64 的 Java 8**:如果用户机器上已经有 Zulu 的
   arm64 Java 8(手动装的),本方法仍然会去装 Adoptium 的 x64 包(它只看「已装列表里有没有 major == 8」,
   找不到就装)。这与 Lead 的裁决一致(装同一个包可以复用),不额外判断。

### Lead 验收(2026-11-12 21:35)

结论:**B2c1 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 224 一致、14 个有改动(与 B2e 相同的 14 个);无对应文件的 9 个不变,本包没有新建 `.cs` |
| `git diff f84574f..HEAD` 对 `Java.cs`、`Preflight.cs`、`GlobalMods.cs`(Lead 逐行读过) | `Java.cs`:link 条目改为先收集、`DownloadAll` 之后再建;`forge-legacy` 只多传一个 arch 实参;Microsoft 地址交给纯函数。`Preflight.cs`:kernel32 实现原样搬进 `FreeBytesWindowsApi`,非 Windows 走 `DriveInfo`。`GlobalMods.cs`:Windows 分支原样,非 Windows 看 `LinkTarget` |
| `JavaRuntimeFiles.cs`、`JavaArch.cs` 的新增(Lead 逐行读过) | `DecideLink` 四种状态;路径上是普通文件或目录时只告警、不删除;路径与目标的安全检查仍在建链之前 |
| `BackendApi.cs`、`Installer.cs`、`McLauncher.cs`、`LaunchArch.cs`、`MacNativeSupport.cs`、`Utils.cs`、`Cleaner.cs`、`OfficialMigrate.cs`、红线文件、`Directory.Build.props`、三个 csproj | 未改 |
| 已有测试被改动的 | 0 个 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1477 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1477 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 86 → 86,没有新名字 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 0 个 |
| B2c1 测试扫描 | 没有联网、起进程、建真实符号链接、调 `Java` 的安装 / 挑选入口;`Preflight.FreeBytesHook` 的两处赋值都在 `finally` 里恢复 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 认可的取舍:D-54(Microsoft 在 mac 上用 `macos` + `.tar.gz`,依据是 6 个 HEAD 请求里只有这两个地址指向真实包名)、D-55、D-56、D-57。

只记录、不处理:

- `TryCreateRuntimeLink` 在新建或重建时返回 true,但底层 `CreateLink` 建链失败只记警告,所以「返回 true」不等于链接一定建成。目前没有调用方使用这个返回值。
- Microsoft 的地址只对 Java 17 取过证,其它大版本在 mac 上是否同样是 `macos` + `.tar.gz` 未验证。
- Mojang 运行时的建链顺序改动只有代码审读和纯函数级单测,没有端到端装过一次(测试不许联网)。

## B2c2 其余平台差异(一):NodeRuntime darwin / Terracotta 可执行权限 / adb 路径 / PhoneLink 临时目录

### 1. NodeRuntime 在 mac 上自动下载便携版 Node(已改)

**第 0 步取证(2 个 GET,查询时间 2026-11-12 21:35:33 +08:00,两份原文存在 `artifacts/b2c2/`,不入库)**

| 源 | 结果 | 里面的 darwin / win-x64 行(逐字) |
|---|---|---|
| `https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt` | 200,3981 字节 | `node-v22.23.5-darwin-arm64.tar.gz`、`node-v22.23.5-darwin-x64.tar.gz`(同架构另有 `.tar.xz`)、`node-v22.23.5-win-x64.zip` |
| `https://npmmirror.com/mirrors/node/latest-v22.x/SHASUMS256.txt` | 200,3777 字节 | `node-v22.20.0-darwin-arm64.tar.gz`、`node-v22.20.0-darwin-x64.tar.gz`(镜像版本比官方滞后)、`node-v22.20.0-win-x64.zip` |

结论:mac 包名形态就是 `node-v<版本>-darwin-<架构>.tar.gz`,与 `PickFromShasums` 的 `"-" + platform + ext` 过滤规则天然吻合,所以 platform 取 `darwin-arm64` / `darwin-x64`、ext 取 `.tar.gz`。
本次**只发了这 2 个 GET**,没有下载任何 Node 压缩包(测试夹具里那 12 行是从上面两份文件逐字抄的,已用脚本核对:`node-v*` 文件名 12 / 12 命中,真实 sha256 13 / 13 命中,唯一的「不命中」是刻意构造的大小写测试值)。

**改了什么(全部落在 `Plugins\NodeRuntime.cs`,483 → 568 行)**

| 接缝 | 内容 |
|---|---|
| `DownloadTargetFor(string os, Architecture processArch, bool is64BitOperatingSystem)` | 新增 internal 纯函数,返回 `(platform, ext)?`。Windows → `win-x64` / `win-x86` + `.zip`(与改动前 `Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86"` 逐字相同,**不看进程架构**);`osx`+`Arm64` → `darwin-arm64`+`.tar.gz`;`osx`+`X64` → `darwin-x64`+`.tar.gz`;其余(含 linux、mac 上 32 位 / 未知架构)→ null。 |
| `PortableExePath(nodeDir, isWindows)` | 新增 internal 纯函数:Windows `<dir>/node.exe`,非 Windows `<dir>/bin/node`(与 `Locate` 142–145 行已有的兜底拼法一致)。 |
| `FindExtractedNodeDir(tmp, isWindows, listDirs?, fileExists?)` | 新增 internal 纯函数:解压临时目录里「含 node 可执行文件的那一层」。Windows 看一层子目录里的 `node.exe`,非 Windows 看 `bin/node`;找不到返回 null(调用方保持原来的 `?? tmp`);`listDirs` 抛异常时**原样往上抛**,与改动前 `Directory.GetDirectories` 的异常语义一致(由镜像循环 catch)。 |
| `Download` | 平台/扩展名 = `DownloadTargetFor(Utils.OS_NAME, RuntimeInformation.ProcessArchitecture, Environment.Is64BitOperatingSystem)`;原来那句「当前系统不支持自动下载 Node…」的 `RuntimeError` **文案一字未动**,只是改由 `target == null` 触发;`PickFromShasums(shasums, platform, ext)`;解压按扩展名分流(`.zip` 仍走 `Utils.SafeExtractZip`,否则走 `JavaRuntimeFiles.SafeExtractTarGz`);一层目录用 `FindExtractedNodeDir(tmp, IsWindows)`;可执行文件用 `PortableExePath(dirs.NodeDir, IsWindows)`。镜像循环、sha256 校验、`Directory.Move`、清理、`ProbeVersion(exe) ?? file.Split('-')[1].TrimStart('v')` 全部照旧。 |
| 文案 | 「SHASUMS256.txt 里没有 X 的 zip」改成 `… + " 的 " + (ext == ".zip" ? "zip" : ext)`,`.zip` 分支拼出来与改动前**逐字相同**。 |

**npm 核对表(第 1 项要求的结论:不需要改)**

| 位置 | 现状(`FindNpmCli` / `NpmInstall`,365–409 行) | mac 布局下是否正确 | 依据 |
|---|---|---|---|
| `FindNpmCli` 候选 1 | `<node 目录>/node_modules/npm/bin/npm-cli.js` | 不适用(Windows zip 布局) | 源码 |
| `FindNpmCli` 候选 2 | `<node 目录>/../lib/node_modules/npm/bin/npm-cli.js` | **正确**:node 在 `<NodeDir>/bin/node` 时 dir=`<NodeDir>/bin`,拼出来就是 `<NodeDir>/lib/node_modules/npm/bin/npm-cli.js` | 源码 + 用例(`B2c2NodeRuntimeTests.Npm_cli_is_found_for_the_mac_tarball_layout`,用 `TestHome.Root` 下空文件摆出该布局后真的找到了) |
| `FindNpmCli` 候选 3 | `<node 目录>/../node_modules/npm/bin/npm-cli.js` | 无关(兜底) | 源码 |
| `NpmInstall` 的落点 | `cli != null` → 跑 `node <npm-cli.js> install …` | **正确**(不依赖 npm 可执行位,直接让 node 跑 js) | 源码 382–394 行 |
| `NpmInstall` 没有 cli 时 | `FindOnPath(IsWindows ? "npm.cmd" : "npm")` | **正确**:非 Windows 已经在找无后缀的 `npm` | 源码 392 行 |
| `NpmInstall` 的 `NodeDir/bin/npm` 符号链接 | 没有被使用 | 无关(mac tar 包里 `bin/npm` 是指向 `../lib/node_modules/npm/bin/npm-cli.js` 的符号链接,但代码走上面那条,不需要它) | 源码;符号链接的存在形态**未经真机验证** |

**未验证**:mac 上 tar.gz 解出来顶层是不是 `node-v22.x.y-darwin-*/`、可执行文件是不是 `bin/node`(只按 `Locate` 既有约定实现);mac 上 `Download` 端到端能否跑通(测试里一律不调它)。
`KillTree` 的非 Windows 分支只核对不改:非 Windows 不 taskkill,只 `p.Kill()`,没有缺口(B2c2 不新增 linux 功能)。

### 2. Terracotta 可执行权限(已改)

**现状结论(读代码,不是猜的)**

| 位置 | 现状 |
|---|---|
| `Launcher\Terracotta.cs:679-720` `_extract_named` | 用 `File.WriteAllBytes(...)` 把内核写到 `<ROOT>/terracotta/0.4.2/<exe>`(702 行) |
| `Launcher\Terracotta.cs:722-772` `Install` | 下载 → 校验 sha512 → 删旧目录 → `_extract_named` → `action("已安装到 …")`,**全程没有任何设可执行权限的代码**;全文件 grep 不到 `SetUnixFileMode` / `chmod`(只有 1187 行把 `Executable()` 交给 `Process.Start`) |
| 结论 | mac / linux 上装出来的内核权限位是 umask 默认值(通常 0644),**没有执行权限**,第一次 `Process.Start` 会失败 |

**改法**:新增 internal 纯函数 `FilesNeedingExecutableBit(string os, string exeName)` —— Windows 返回**空表**(循环体一次都不执行,改动前就是不设权限位,行为逐字不变);非 Windows 返回只含 `exeName` 的单元素数组(不碰同包里的 `VCRUNTIME140.DLL`)。
`Install` 在 `_extract_named` 成功之后、`action("已安装到 …")` 之前加:

```csharp
foreach (string needsExec in FilesNeedingExecutableBit(Utils.OS_NAME, pkg.exe))
{
    JavaRuntimeFiles.SetExecutable(PPath.Join(text3, needsExec));
}
```

`JavaRuntimeFiles.SetExecutable` 是 B2a 已有的实现(内部先判 `OperatingSystem.IsWindows()` 再 `File.SetUnixFileMode(…, 0755)`,失败只记警告)。
mac 上到底能不能执行**未经真机验证**。

### 3. adb 路径(已改)

- `PhoneClient.FindAdb()` 拆成三段:`AdbCandidates(...)` 纯函数(只拼字符串,不碰文件系统)、可注入重载 `FindAdb(os, configured, baseDir, dataRoot, getEnv, homeDir, localAppData, pathValue, pathSeparator, exists)`、以及 3 行的生产包装(取真实值 + 转调,`Config.CONFIG` 与 `Utils.ResolveRoot()` 的取法与顺序照旧,`ResolveRoot` 仍包在 `try/catch` 里)。
- **Windows(`os == "windows"`)候选与顺序逐项不变**:配置项 → `<程序目录>\adb.exe` → `<数据根>\adb.exe` → `ANDROID_HOME`、`ANDROID_SDK_ROOT` 下 `platform-tools\adb.exe` → `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` → PATH 逐目录先 `adb.exe` 再 `adb`(空项跳过、非空项 `Trim`)。用一条「整表逐项相等」的用例钉住。
  忠实照搬的一个细节:Windows 分支那条 `Path.Combine(localAppData, …)` 也是无条件的,所以传 null 进来同样抛 `ArgumentNullException`(生产中 `GetFolderPath` 不会给 null;非 Windows 分支根本不碰这个值)。已用一条用例明确记录。
- **非 Windows**:配置项 → `<程序目录>/adb` → `<数据根>/adb` → `ANDROID_HOME` / `ANDROID_SDK_ROOT` 下 `platform-tools/adb` → (仅 `osx`)`<用户主目录>/Library/Android/sdk/platform-tools/adb` → (仅 `osx`)`/opt/homebrew/bin/adb`、`/usr/local/bin/adb` → PATH 各目录下 `adb`。**不再探测任何 `adb.exe`,也不看 LocalApplicationData**(有用例逐条钉住)。
  mac 的 `~/Library/Android/sdk` 与两个 Homebrew 位置是任务书裁决给出的默认位置,**未经真机验证**,已列入真机清单。
- **报错文案**:`PhoneClient.cs:439` 那句原文**一个字都没动**,只是从「字面量直接传给 `InvalidOperationException`」改成「作为实参传给 `AdbMissingMessage(os, windowsText)`」;`AdbMissingMessage` 在 `os == "windows"` 时原样返回这个实参,非 Windows 返回新文案(不提 `adb.exe` 与 `AdbWin*.dll`,说明可在设置里填 adb 路径或安装 Android platform-tools)。
  有一条用例**读源码**核对生产调用点传的字面量逐字等于改动前的原文(纯函数化以后这是唯一不执行生产入口的钉法;旧源码里那句含全角括号,无法整句重抄,见 D-58)。

### 4. PhoneLink 临时目录可注入(已改)+ PhoneLinkTests 的尝试结果

- `Launcher\Sync\PhoneLink.cs`:新增 `internal static Func<string> TempDirProvider = DefaultTempDir;` 与 `internal static string DefaultTempDir()`(就是原来那一句 `Path.Combine(Path.GetTempPath(), "tipcl-phone-sync")`,只拼字符串不落盘);`TempZip()` 由 private 改 internal,落点改成 `(TempDirProvider ?? DefaultTempDir)()`,**生产默认行为与改动前完全一致**。
- 测试工程新增 `tests\TipCL.Tests\B2c2PhoneLinkTempDirTests.cs`,里面有自己的 `[ModuleInitializer]`,把 `TempDirProvider` 指到 `Path.Combine(TestHome.Root, "phone-sync")`;**委托是惰性的**(在被调用时才读 `TestHome.Root`),没有改 `TestHome.Init()`。
- **PhoneLinkTests 的尝试结果:仍然排除(`Compile Remove` 保持)**。把它从 `Compile Remove` 里放出来真跑了两条通道:
  - `dotnet test TipCL.Mac.sln -c Release --no-build`:退出码 **1**,失败 1 / 通过 1582 / 总计 1583。失败的就是 `PhoneLinkTests.FullFlow`,异常原文:
    `System.Net.HttpListenerException : 句柄无效。` ← `System.Net.HttpListener.SetupV2Config()` ← `HttpListener.Start()`(`Ported\PhoneLinkTests.cs:44`,由 `FullFlow` 在 202 行走进 `FakePhone..ctor`)—— 与 A6 / D-27 记的一模一样。
  - 进程内通道:退出码 **0**,**1583/1583 全过**(含 `FullFlow`)。
  - 两次都量了 `%TEMP%`:`tipcl*` 目录名字集合 86 → 86,**没有新名字**;`tipcl-phone-sync` 条目数 0 → 0;临时 zip 的落点确实落在 `tests\TipCL.Tests\bin\Release\net8.0\test-home\<guid>\phone-sync\` 之下(该目录被真的建了出来,里面没有残留 zip)。
  结论:两个排除原因里**第二个(往 %TEMP% 写文件)已经修好**,但 testhost 里 `HttpListener` 那个环境限制仍在(D-27),所以按任务书**放回 `Compile Remove`**,不改监听方式、不改该测试文件的任何断言;csproj 里那段旧注释下面补了一段 B2c2 复核说明。

### 对复制文件的每处改动(只动了任务书点名的 4 个文件,别的复制文件一个字没改)

| 文件(行数变化) | 原来 | 改成 | 为什么 |
|---|---|---|---|
| `Plugins\NodeRuntime.cs`(483 → 568) | `if (!IsWindows) throw new RuntimeError(…)`;`string platform = Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86"` | 先 `DownloadTargetFor(Utils.OS_NAME, RuntimeInformation.ProcessArchitecture, Environment.Is64BitOperatingSystem)`,null 时抛**同一句** `RuntimeError` | 让 mac 也能下载;不支持时文案逐字不变 |
| 同上 | `using System.Linq;` 之后没有 InteropServices | 加 `using System.Runtime.InteropServices;` | 要用 `Architecture` / `RuntimeInformation` |
| 同上 | `PickFromShasums(shasums, platform)` | `PickFromShasums(shasums, platform, ext)` | mac 要 `.tar.gz`(默认参数仍是 `.zip`,Windows 行为不变) |
| 同上 | `"SHASUMS256.txt 里没有 " + platform + " 的 zip"` | `… + " 的 " + (ext == ".zip" ? "zip" : ext)` | mac 上说 zip 不对;`.zip` 分支逐字相同 |
| 同上 | `Utils.SafeExtractZip(zip, tmp);` | `if (ext == ".zip") { SafeExtractZip } else { JavaRuntimeFiles.SafeExtractTarGz(zip, tmp); }` | mac 的包是 tar.gz;Windows 仍走原函数 |
| 同上 | `Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "node.exe"))) ?? tmp` | `FindExtractedNodeDir(tmp, IsWindows) ?? tmp` | 同样的「一层目录」判断,非 Windows 改看 `bin/node`,并做成可注入纯函数 |
| 同上 | `string exe = System.IO.Path.Combine(dirs.NodeDir, "node.exe");` | `string exe = PortableExePath(dirs.NodeDir, IsWindows);` | 非 Windows 是 `<NodeDir>/bin/node`;Windows 分支结果逐字相同 |
| 同上 | `/// 下载便携版 Node 22 LTS 到 runtime/node(走 TipCL 下载器,校验 SHASUMS256)。只支持 Windows x64(其他平台抛错)。` | 那句话保留原样,紧接着补 3 行「B2c2 更正」 | 旧句里的**全角括号无法整句重抄**(D-58);更正后描述与代码一致 |
| `Launcher\Terracotta.cs`(1833 → 1852) | `Install` 里 `_extract_named(...)` 成功之后直接 `action("已安装到 " + text3);` | 中间插入 4 行循环调用 `FilesNeedingExecutableBit(Utils.OS_NAME, pkg.exe)` + `JavaRuntimeFiles.SetExecutable(...)` | 非 Windows 上装出来的内核没有执行权限;Windows 下该表为空,行为不变 |
| 同上 | 没有这个函数 | 新增 internal 纯函数 `FilesNeedingExecutableBit(os, exeName)` + 一段 XML 注释 | 把「要不要设权限」做成可注入 os 的纯函数并单测 |
| `Launcher\Sync\PhoneClient.cs`(475 → 537) | `FindAdb()` 一个方法内含全部候选拼装 + `FirstOrDefault(File.Exists)` | 拆成 `AdbCandidates(...)`(纯函数)+ `FindAdb(…10 个参数…)`(可注入)+ 3 行的 `FindAdb()` 包装 | 平台差异要能在本机单测 |
| 同上 | 没有这个函数 | 新增 internal 纯函数 `AdbMissingMessage(os, windowsText)` | 文案按 os 选,可单测 |
| 同上 | `throw new InvalidOperationException("<旧文案>")` | `throw new InvalidOperationException(AdbMissingMessage(Utils.OS_NAME, "<同一句旧文案>"))` | 字面量一字未动,只换了传参方式 |
| 同上 | 旧注释「找 adb:设置里指定的 → …」 | 保留,后面接一段 `<remarks>` 描述两套候选 | 旧注释含全角冒号与箭头,无法整句重抄(D-58) |
| `Launcher\Sync\PhoneLink.cs`(432 → 443) | `private static string TempZip()` 内写死 `Path.Combine(Path.GetTempPath(), "tipcl-phone-sync")` | 新增 `TempDirProvider` / `DefaultTempDir()`,`TempZip` 改 internal 并读提供者 | 测试临时文件必须落在仓库内(A6 lead 验收的恢复条件) |

### 测试(新增 102 条用例,总数 1477 → 1579)

| 新文件 | 覆盖 | 用例数(含 Theory 展开) |
|---|---|---|
| `tests\TipCL.Tests\B2c2NodeRuntimeTests.cs` | `DownloadTargetFor` 的 Windows 钉子(含「不看进程架构」4 格)/ mac 两个架构 / 不支持的平台与架构 / `PortableExePath` 两个分支 / `FindExtractedNodeDir` 的真实目录树(Windows 与 unix 各一组、第一个匹配、空目录、注入委托、委托抛异常)/ `PickFromShasums` 用线上夹具挑 darwin-arm64、darwin-x64、win-x64(含**默认参数**那条钉子)、扩展名真的参与筛选、镜像夹具、找不到 / 小写化 / 坏长度 / npm 布局 4 条 / `Download` 接缝的结构钉子 | 48 |
| `tests\TipCL.Tests\B2c2TerracottaExecTests.cs` | Windows 空表(含 null 名)/ 非 Windows 单元素(4 个真实内核名)/ 只有 `"windows"` 特判 / `SetExecutable` 的 Windows 空操作与 unix 0755 / null 空 不存在路径 / 幂等 / 文件名不含分隔符 / `Install` 接缝的结构钉子 | 17 |
| `tests\TipCL.Tests\B2c2AdbPathTests.cs` | Windows 整表逐项相等 + 去掉配置项 / 数据根 / 环境变量后的整表相等 + 单个环境变量 + LocalApplicationData 无条件 + PATH 裁剪 + 注入分隔符 / mac 整表 + 主目录未知 + linux 没有 mac 专属候选 / 非 Windows 不出现 `.exe` 与 AppData / `FindAdb` 注入存在性判断(配置项优先、第一个命中、全不命中、委托调用顺序、环境变量询问顺序、真实 `File.Exists`）/ Windows 文案逐字 + 非 Windows 文案排除 exe/dll 措辞 / 生产调用点与生产入口的**源码结构钉子** | 30 |
| `tests\TipCL.Tests\B2c2PhoneLinkTempDirTests.cs` | 默认提供者 = 原来那一句(只比字符串)/ 测试进程的提供者指向 `TestHome.Root/phone-sync` / `TempZip` 落点 + 只建目录不建文件 / 两次名字不同 / 提供者在 `finally` 里恢复 / 不在默认 %TEMP% 子目录下 / 源码结构钉子 + 本文件的 `[ModuleInitializer]` | 7 |

实际条数(进程内通道按类统计,合计 **102**):`B2c2NodeRuntimeTests` 48、`B2c2AdbPathTests` 30、`B2c2TerracottaExecTests` 17、`B2c2PhoneLinkTempDirTests` 7。

用例一律不联网、不起进程、不建真实符号链接、不实例化 `BackendApi` / `BridgeServer` / `PluginManager`;临时文件都在 `TestHome.Root` 之下;改全局静态(`TempDirProvider`)的那条用例在 `finally` 里恢复。

### 验证命令与实际结果(全部在本机真实执行过)

| 命令 | 结果 |
|---|---|
| `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;**1 个警告 = SYSLIB0014 ×1**(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs(116,38)`,本包未动的那处 `WebRequest`) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,通过 **1579** / 失败 0 / 跳过 0(2.1 秒,没有出现 `Win32Exception (5)`) |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,总计 **1579**,通过 1579 / 失败 0 / 跳过 0 / **不支持 0** |
| `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` / `--theme dark …` | 退出码 **0 / 0**,**3739 字节 / 3751 字节**(与 A1 起逐次相同) |
| `%LOCALAPPDATA%\TipCL` 开工时 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16 |
| 测试后再量 | **9328 / 889530606 / 2026-11-10 20:42:16**,三项完全一致(测试全程没有碰真实数据目录) |
| `%TEMP%` 下 `tipcl*` 目录名字集合(两条通道前后各一次) | **86 → 86**,**没有新名字**;`tipcl-phone-sync`(A6 留下的空目录)条目数 **0 → 0** |
| 残留进程 `testhost` / `adb` / `java` / `javaw` / `TipCL` | 全 **0**;`node` 有 4 个,创建时间分别是 2026-11-10 22:30:23、2026-11-10 23:11:48、2026-11-12 19:20:47(×2),**都早于本包第一次跑测试(21:52)**,不是本包留下的 |
| 字节一致性自检(`src/TipCL.Core` 全部 `.cs` 对 Windows 源比 SHA256) | 总数 **247** / 一致 **220** / 有改动 **18** / Windows 源里没有对应文件 **9**。有改动的 18 = 既有 14 个 + 本包实际改的 4 个(`Plugins/NodeRuntime.cs`、`Launcher/Terracotta.cs`、`Launcher/Sync/PhoneClient.cs`、`Launcher/Sync/PhoneLink.cs`);没有对应文件的仍是既有 9 个,本包没有新建 `.cs` |
| 联网请求总数 | **2 个 GET**(上面第 0 步那两份 SHASUMS256.txt),没有别的请求、没有下载任何 Node 压缩包 |

### 未验证(本机是 Windows,没有 Mac)

1. mac 上 `NodeRuntime.Download` 端到端(下载 → 校验 → `SafeExtractTarGz` → `Directory.Move` → `bin/node` 能跑);**顶层目录名与 `bin/node` 布局只按 `Locate` 的既有约定实现**,tar 包实际形态没看过(不能下载包来看)。
2. mac 上 tar 包里 `bin/npm` 符号链接的实际形态(不影响功能,因为 `NpmInstall` 是让 node 直接跑 `npm-cli.js`)。
3. mac 上陶瓦内核能不能执行(权限位 0755 本机设不了、也验不了);`/opt/homebrew/bin/adb`、`/usr/local/bin/adb`、`~/Library/Android/sdk/platform-tools/adb` 这三条默认位置;`ANDROID_HOME` / `ANDROID_SDK_ROOT` 在 mac 上的常见形态。
4. `osx` / `linux` 分支的候选路径在本机跑纯函数时用的是 Windows 的 `Path.Combine`(反斜杠),mac 上真机才会用 `/` —— 断言本身与平台无关(同一函数构造),但真机上的绝对路径拼法没有验过。

### 遗留与风险

1. `PhoneLinkTests` 仍被排除(testhost 的 `HttpListener` 环境限制),它的 4 条用例只在进程内通道跑得到;真机上或能起 `HttpListener` 的环境里应当再试一次。
2. `NodeRuntime.Download` 的 mac 分支建议在真机上补一条:`TIPCL_NODE` 不设时让它下载一次,确认 `runtime/node/bin/node --version` 能跑(本包只能保证代码审读 + 纯函数单测)。
3. `JavaRuntimeFiles.SetExecutable` 失败只记警告不抛,所以「权限位设没设上」在 mac 上要靠真机日志确认;`FilesNeedingExecutableBit` 只处理内核本体,如果将来 mac 包里多了需要执行位的辅助文件,要同步扩这个表。
4. Windows 分支里 `Path.Combine(localAppData, …)` 对 null 抛异常这一点被忠实保留(生产中不会发生),将来若有人拿它当通用工具函数用,需要自己保证非 null。
5. 本包没有动 `Utils.cs` / `BackendApi.cs` / 其它复制文件,也没有改 TFM、没有扩 `NoWarn`、没有新增警告。

### Lead 验收(2026-11-12 22:10)

结论:**B2c2 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 220 一致、18 个有改动(原有 14 个 + 本包 `NodeRuntime.cs`、`Terracotta.cs`、`Sync\PhoneClient.cs`、`Sync\PhoneLink.cs`);无对应文件的 9 个不变,本包没有新建 `.cs` |
| `git diff adfd75b..HEAD` 对 4 个复制文件(Lead 逐行读过) | `NodeRuntime.cs`:平台与后缀交给纯函数,Windows 仍是 `win-x64` / `win-x86` + `.zip`,mac 走 `darwin-*` + `.tar.gz` 并用 `SafeExtractTarGz` 解压;sha256 校验与镜像循环未动。`Terracotta.cs`:安装后给内核可执行文件补 0755,Windows 返回空表。`PhoneClient.cs`:候选列表抽成纯函数,Windows 的 6 类候选与顺序未变(含每个 PATH 目录先 `adb.exe` 后 `adb`);Windows 报错原文一字未动。`PhoneLink.cs`:临时目录改为可注入,默认值仍是系统临时目录下的 `tipcl-phone-sync` |
| `BackendApi.cs`、`Java.cs`、`JavaArch.cs`、`JavaRuntimeFiles.cs`、`LaunchArch.cs`、`MacNativeSupport.cs`、`Utils.cs`、`Installer.cs`、`McLauncher.cs`、`PluginManager.cs`、`PluginMarket.cs`、`Ported\` 下全部文件、红线文件、`Directory.Build.props`、`TipCL.Core.csproj` | 未改 |
| `TipCL.Tests.csproj` | 只多了一段注释;`Ported\PhoneLinkTests.cs` 仍在 `Compile Remove` 里 |
| 已有测试被改动的 | 0 个 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1579 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1579 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 86 → 86,没有新名字;`tipcl-phone-sync` 条目数 0 → 0 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL`,以及新起的 `node` | 0 个 |
| B2c2 测试扫描 | 没有联网、起进程、调 `NodeRuntime` 的下载 / 探测入口、调无参 `FindAdb()`、建真实符号链接、改环境变量;`PhoneLink.TempDirProvider` 的临时替换在 `finally` 里恢复 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 的裁决与说明:

- **`PhoneLinkTests` 继续排除**:临时目录的原因已消除,但 `dotnet test` 通道里 `HttpListener.Start()` 仍报「句柄无效」。子代理把它放出来实测后又放回去,做法正确。Lead 没有单独复跑放出来的状态。
- D-58(注释里的标点用半角)、D-59(Windows 报错原文作为实参保留、旧注释用追加更正)接受。
- mac 上的三条 adb 默认位置、Node 的 tar 包布局(`bin/node`、`bin/npm` 符号链接)、Terracotta 内核能否执行,都列入真机验证清单。

---

## B2c3 其余平台差异(二):Updater 仅提示 / Shortcut / 导出启动脚本

三个项全部**已改**。唯一被授权的 Windows 可见改动是 `Updater.Check` 多一个键 `can_self_update`(Windows 上为 true);其余两处都只新增非 Windows 分支,Windows 代码路径一字未动,并用「读源码的结构钉子 + 纯函数逐字对照」钉住。

> 本节的标点按 D-58 用半角(全角 `,` `(` `)` `:` 在本机写不出)。源码里需要全角标点的新文案一律用 `\uXXXX` 转义写。

### 0. 开工前的元数据测量(红线核对)

| 项 | 结果 |
|---|---|
| `%LOCALAPPDATA%\TipCL` 开工时(只列目录,不读内容) | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 给的基线三项完全一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(开工时) | 86 个名字 |
| 真实桌面目录(metadata only)条目数(开工时) | 14 |

### 1. 第 1 项:Updater 在非 Windows 上只提示、不自替换(已改)

[Updater.cs](src/TipCL.Core/Launcher/Updater.cs) 的四处改动:

| 位置 | 改前 | 改后 |
|---|---|---|
| 新函数(第 81 行之后) | 无 | `internal static bool SelfUpdateSupported(string os)` = `os == "windows"`;`internal static void EnsureSelfUpdateSupported(string os)` 在非 Windows 上抛 `RuntimeError` |
| `ApplyStaged`(原 125 行起) | 第一句是 `string pkg = Utils.AbsPath(package);` | 第一句变成 `EnsureSelfUpdateSupported(Utils.OS_NAME);`,后面原样 |
| `Download`(原 149 行起) | 第一句是 `string text = Py.Str(Py.Or(info?["url"], ""));` | 第一句变成守卫,后面原样 |
| `Check` 的两个 `return PyDict.Of(...)`(原 95 / 102 行) | 8 / 9 个键 | 各多一个 `"can_self_update"`,取值 `SelfUpdateSupported(Utils.OS_NAME)`(Windows 上 true) |

抛出的文案在源码里是这样写的(全角逗号用转义,避免 D-58 的抄写陷阱):

```csharp
throw new RuntimeError("\u5f53\u524d\u5e73\u53f0\u4e0d\u652f\u6301\u539f\u5730\u81ea\u66f4\u65b0\uFF0C\u8bf7\u5230\u4e0b\u8f7d\u9875\u4e0b\u8f7d\u65b0\u7248\u672c\u5b89\u88c5");
```

解码后 = `当前平台不支持原地自更新` + U+FF0C + `请到下载页下载新版本安装`。

**`Check` 返回的键清单(逐行读过原 84-103 行,并逐字核对了改动后的两行)**:

| 分支 | 键(顺序即源码顺序) |
|---|---|
| 清单拉取失败(catch 分支) | `ok` `current` `latest` `has_update` `can_self_update` `message` `notes` `url` —— 原有 8 个 + 新增 1 个 |
| 拉取成功 | `ok` `current` `latest` `has_update` `can_self_update` `message` `notes` `url` `sha256` —— 原有 9 个 + 新增 1 个 |

两个分支的差别只有成功分支多一个 `sha256`(改动前就是这样,本包没动)。其它平台差异没有偷偷塞进这个字典。

**清单里能提供什么(给 UI 阶段留的取证,本包只记录、不实现)**:`Check` 只读更新清单的这四组键 ——
`version` / `latest`、`sha256`、`notes` / `changelog`、`url` / `download`。
也就是说**清单里没有独立的「下载页」字段**:唯一与下载有关的地址就是更新包自己的 `url`(`Download` 直接拿它下 `.bin`)。
另外 `Updater.DEFAULT_URL` 与 `Config.UpdateUrl()`(=`ManifestUrl()`)都是**清单地址**,不是下载页。
所以「打开下载页」这个 UI 行为要等 UI 阶段决定用哪个地址 —— **本包不发明任何 URL、也不加下载页功能**。

`BackendApi.start_self_update` **没有改**:非 Windows 上它的后台任务现在会在 `Updater.Download` 的守卫处抛出上面那句中文文案,任务以该文案失败(UI 阶段据此改走「打开下载页」)。

### 2. 第 2 项:macOS 桌面快捷方式(已改)

[Shortcut.cs](src/TipCL.Core/Launcher/Shortcut.cs) 的四处改动:

1. **`Chmod755`(原 176-178 行,空方法)**:改前方法体是空的 —— 生成的 `.command` / `.desktop` **从来没有可执行位**。改后只有一句 `JavaRuntimeFiles.SetExecutable(path)`(非 Windows 设 0755,Windows 第一句就返回,失败只记警告)。
   Linux 的 `.desktop` 因此也第一次拿到执行位 —— 任务书已接受,在此记录。
2. **新纯函数 `internal static string MacCommandScript(string description, string workdir, string target, IEnumerable<string> args)`**:返回完整正文 ——
   `#!/bin/sh` / `# <单行化的 description>` / `cd ` + `PosixShell.Quote(workdir)` / `exec ` + `PosixShell.JoinCommand(target 及其后每个参数)`,LF 行尾、结尾一个换行。
   description 里的 `\r\n` / `\r` / `\n` 一律换成空格(用户可控文本不能靠换行顶出注释行)。
3. **`_create_macos`**:改前用 `_quote`(只在「有空格」时加双引号、完全不转义)拼正文;改后调 `MacCommandScript`,并把新加的第七个参数 `IEnumerable<string> rawArgs` 传进去。`arguments` 这个参数在 mac 分支**有意不用**(它是 cmd 风格的预拼接结果)。
4. **`CreateLaunchShortcut`(原 225-226 行)**:

| | 改前 | 改后 |
|---|---|---|
| 参数表 | `IEnumerable<string> source = list.Skip(1).Concat<string>(LaunchArgs(...));` | `List<string> rawArgs = list.Skip(1).Concat(LaunchArgs(...)).ToList();` |
| 预拼接 | `string arguments = string.Join(" ", source.Select(_quote));` | `string arguments = string.Join(" ", rawArgs.Select(_quote));`(**取值逐字相同**) |
| 分发 | mac 分支只传 `arguments` | mac 分支多传 `rawArgs`;Windows / Linux 两分支的实参一字不变 |

**只复核、记录、没有改**的四件事:

- `DesktopDir()`:非 Windows 下先读 `HOME`,再依次试 `Desktop` / `桌面` 两个子目录,都不存在时返回规范化后的 `HOME` —— 最坏情况下快捷方式会落到家目录根。本包不动它。
- `LauncherCommand()`:返回 exe 同目录下的 `TipCL-CLI`(存在就用它,否则用 exe 自身)。GOAL 的 `.app` 结构里正在运行的 exe 是 `Contents/MacOS/<arch>/TipCL`,所以 mac 上写进 `.command` 的 target 就是这一层 —— 打包阶段要确认这一层真有可执行文件(B3)。
- Linux `.desktop` 的 `Exec=` 行仍是 `_quote(target) + " " + arguments` 的弱引号(与本包修掉的 mac 分支同一个毛病);本仓库不出 Linux 包,只记录。
- `RemoveLaunchShortcut()`:按 os 取扩展名(`.lnk` / `.command` / `.desktop`)删除,未改动。

### 3. 第 3 项:导出启动脚本(已改)

[VersionOps.cs](src/TipCL.Core/Launcher/VersionOps.cs) 新增四个成员(全部在 `ExportLaunchBat` **之后**,那个函数一个字节都没动):

| 新成员 | 行为 |
|---|---|
| `internal static string ExportScriptExtension(string os)` | `windows` → `.bat`,`osx` → `.command`,其余(含 null / 空 / 大小写不同)→ `.sh` |
| `internal static string PosixLaunchScript(IEnumerable<object> cmd, object cwd)` | `#!/bin/sh` / `cd ` + `PosixShell.Quote(Py.Str(cwd))` / `PosixShell.JoinCommand(cmd 逐个 Py.Str)`;LF、结尾换行、**没有** `chcp` / `pause` / `exec`,不加任何 JVM 参数或 arch 前缀 |
| `internal static string ExportLaunchScript(string dest, IEnumerable<object> cmd, object cwd, string os)` | `windows` → 原样转调 `ExportLaunchBat` 并返回它的返回值;否则建父目录 → 写 UTF-8 无 BOM → `JavaRuntimeFiles.SetExecutable` → 返回 `new PPath(dest).ToString()`(与 `ExportLaunchBat` 同形) |
| `internal static PyDict ExportVersionJson(PyDict childJson, Func<string, PyDict> loadParent, bool isWindows)` | Windows **原样返回同一个对象**(loader 一次都不调);否则 `Manifest.ResolveInherits`,抛异常(缺父版本 / 继承环 / loader 抛)时退回 `childJson` |

[BackendApi.cs](src/TipCL.Core/BackendApi.cs) 的 `export_launch_script` 内**只有两行**被改(该文件其它任何位置都没动):

```csharp
// 改前(原 2958 行)
string java_exe = Java.ResolveLaunchJava((Py.Or(instance2.VersionJson(text), new PyDict()) as PyDict) ?? new PyDict(), null, null, null, delegate(string t)
// 改后
string java_exe = Java.ResolveLaunchJava(VersionOps.ExportVersionJson((Py.Or(instance2.VersionJson(text), new PyDict()) as PyDict) ?? new PyDict(), (string pid) => instance2.VersionJson(pid) as PyDict, Utils.IS_WINDOWS), null, null, null, delegate(string t)
```

```csharp
// 改前(原 2963 行)
string text2 = VersionOps.ExportLaunchBat(PPath.Norm(Py.Truthy(dest) ? Py.Str(dest) : PPath.Join(Utils.ROOT, "exports", "launch-" + instance2.name + "-" + text + ".bat")), launchCommand.cmd.Cast<object>(), launchCommand.game_dir);
// 改后
string text2 = VersionOps.ExportLaunchScript(PPath.Norm(Py.Truthy(dest) ? Py.Str(dest) : PPath.Join(Utils.ROOT, "exports", "launch-" + instance2.name + "-" + text + VersionOps.ExportScriptExtension(Utils.OS_NAME))), launchCommand.cmd.Cast<object>(), launchCommand.game_dir, Utils.OS_NAME);
```

Windows 上的等价性:同一个 json 对象(`ExportVersionJson(..., isWindows: true)` 原样返回)、同一个默认文件名后缀(`ExportScriptExtension("windows") == ".bat"`)、同一条写盘路径(`ExportLaunchScript` 转调 `ExportLaunchBat`)—— 由纯函数逐字对照 + 字节对比用例钉住。
非 Windows 上第一个参数变成合并继承链后的 JSON:Forge 这类 `inheritsFrom` 版本从此和真实启动用同一份版本 JSON 判架构(这就是 B2e Lead 裁决里那条「`BackendApi.cs:2958` 传未合并 JSON」的落地)。

### 新文件

| 文件 | 说明 |
|---|---|
| [PosixShell.cs](src/TipCL.Core/Launcher/PosixShell.cs) | `internal static class PosixShell`:`Quote(string)`(null 按空串;整串单引号包裹,内部每个 `'` 换成 `'\''`,空串 → `''`)与 `JoinCommand(IEnumerable<string>)`(各自 Quote 后单空格连接)。纯函数,不碰盘、不碰环境 |
| [B2c3PosixShellTests.cs](tests/TipCL.Tests/B2c3PosixShellTests.cs) | 26 条 |
| [B2c3UpdaterNotifyTests.cs](tests/TipCL.Tests/B2c3UpdaterNotifyTests.cs) | 23 条 |
| [B2c3MacCommandScriptTests.cs](tests/TipCL.Tests/B2c3MacCommandScriptTests.cs) | 23 条 |
| [B2c3ExportLaunchScriptTests.cs](tests/TipCL.Tests/B2c3ExportLaunchScriptTests.cs) | 29 条 |

### 测试(新增 101 条用例,总数 1579 → 1680)

| 新文件 | 覆盖 | 用例数(含 Theory 展开) |
|---|---|---|
| `B2c3PosixShellTests.cs` | `Quote` 的 17 组逐字对照(空 / null / 空格 / 制表 / 单引号 / 那串敌意实例名 / 双引号 / `$HOME` / `$(id)` / 反引号 / 反斜杠 / 换行 / 分号 / 通配符 / 中文);「结果一定以单引号开头结尾」;「抹掉所有 `'\''` 转义后**只剩最外两个单引号**」;引号内内容逐字不变;`JoinCommand` 的空表 / null 表 / 单元素 / 多元素 / 空串元素 / null 元素 / 保序 / 敌意参数 | 26 |
| `B2c3UpdaterNotifyTests.cs` | `SelfUpdateSupported("windows")` 为真 + 9 个非 Windows 取值为假;守卫在 Windows 不抛、在 5 个非 Windows 取值上抛 `RuntimeError` 且文案逐字相等;文案含「下载页」「不支持」且不含 http / powershell;本机分支自洽;源码钉子:`Download` / `ApplyStaged` 的第一句就是守卫(各 1 处)、`BuildApplyScript` 里没有守卫且三句原文仍在、`Check` 两个 return 各含 `can_self_update`、键名清单、平台判定只有一处 | 23 |
| `B2c3MacCommandScriptTests.cs` | 普通取值逐字全文;敌意实例名逐字转义;10 个混合参数(含 `$HOME` / 反引号 / 双引号 / `$(touch ...)` / 空串 / 中文)经**独立的反解器**逐个还原成原值;description 的 CRLF / CR / LF / 双换行都压成空格(5 组逐字全文);敌意 description 不能顶出注释行;空 / null description;无参数不留尾空格;带空格 / 单引号 / 中文的路径;用户可控值只出现在正确加引号的词里;只有 LF + 恰好一个结尾换行;第一行是裸 shebang;`SetExecutable` 在 Windows 上是空操作;源码钉子:`Chmod755` 不再为空、mac 分支走 `MacCommandScript` + `Chmod755` + 无 BOM 编码且不再用 `_quote`、`CreateLaunchShortcut` 的 rawArgs、Windows / Linux 两分支与 `_quote` 未变 | 23 |
| `B2c3ExportLaunchScriptTests.cs` | `ExportScriptExtension` 8 组;Windows 扩展名等于改动前的字面量;mac 扩展名与 `Shortcut` 的选择一致;`PosixLaunchScript` 的真实命令行逐字全文(含冒号分隔 classpath 与中文路径)、敌意值逐字、`$` / 反引号 / 双引号 / 分号 / 反斜杠保持字面量、没有 cmd 三件套、空表 / null 表的空命令行、null cwd 与 Windows 分支同源(`Py.Str` → `None`);`ExportLaunchScript` 的 osx 落盘字节(UTF-8 无 BOM / 只有 LF / 首字节 `#` / 内容等于 `PosixLaunchScript` / 建父目录 / 返回值形态)、linux 同正文、**windows 与直接调 `ExportLaunchBat` 字节全等**、Windows 分支不碰权限位;`ExportVersionJson` 的 Windows 同实例且 loader 不调、非 Windows 合并父的 `libraries` 与 `assets` 且子的键覆盖父、无继承时只拷贝、loader 抛异常 / 返回 null / 成环都退回 child、null child 的两分支行为;源码钉子:`BackendApi` 两行新写法 + 旧 `.bat` 默认名片段已消失、`ExportLaunchBat` 未被本包碰 | 29 |

实际条数(进程内通道按类统计,合计 **101**):`B2c3ExportLaunchScriptTests` 29、`B2c3PosixShellTests` 26、`B2c3MacCommandScriptTests` 23、`B2c3UpdaterNotifyTests` 23。

用例一律不联网、不起进程、不实例化 `BackendApi` / `BridgeServer` / `PluginManager`;没有调 `Updater.Check` / `Download` / `ApplyStaged`、没有调 `Shortcut.CreateLaunchShortcut` / `RemoveLaunchShortcut` / `DesktopDir`、没有调 `McLauncher.BuildLaunchCommand`、没有调任何 `Java` 安装 / 挑选入口;不写真实桌面、不写 `%TEMP%`,落盘的脚本都在 `TestHome.Root` 之下;没有改任何全局静态,也没有新建 `[ModuleInitializer]`。
每条期望值的来源都写在用例注释里:来自本包读过的源码,或来自任务书给定的 POSIX 事实。
其中 8 条是「读源码」的结构钉子(用 `RepoSource` 读仓库内的 `.cs` 正文,只读不写),它们钉的是「生产调用点确实接了新接缝」和「Windows 分支一字未动」。

### 验证命令与实际结果(全部在本机真实执行过)

| 命令 | 结果 |
|---|---|
| `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;**1 个警告 = SYSLIB0014 ×1**(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs(116,38)`,本包未动的那处 `WebRequest`)。写测试时一度多出 5 个 `xUnit1012`(null 用在非空 `string` 形参上),已把形参改成 `string?` 修掉 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,通过 **1680** / 失败 0 / 跳过 0(没有出现 `Win32Exception (5)`) |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,总计 **1680**,通过 1680 / 失败 0 / 跳过 0 / **不支持 0** |
| `%TEMP%` 下 `tipcl*` 目录名字集合(两条通道前后各一次) | **87 → 87**(22:29:48 → 22:29:54 这次窗口内)**没有新名字**;相对开工时的 86 多出来的那一个是 `tipcl-probe`(22:28:19 出现),**不是本包产物**,见遗留第 5 条。真实桌面目录条目数 **14 → 14** |
| `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` / `--theme dark ...` | 退出码 **0 / 0**,**3739 字节 / 3751 字节**(与 A1 起逐次相同);SHA256 `17F3CD87…017BF` / `374716FF…38144` |
| `%LOCALAPPDATA%\TipCL` 测试后 | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,三项与开工时完全一致 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL` | 全 **0**;另有 2 个 `powershell` 进程,一个是上一个会话(Lead)的轮询脚本(PID 1660,22:24:14 起),一个是本次测量命令自己的 shell,都不是本包起的 |
| 字节一致性自检(`src/TipCL.Core` 全部 `.cs` 对 Windows 源比 SHA256) | 总数 **248** / 一致 **216** / 有改动 **22** / Windows 源里没有对应文件 **10**。有改动的 22 = 既有 18 个 + 本包实际改的 4 个(`BackendApi.cs`、`Launcher/Shortcut.cs`、`Launcher/Updater.cs`、`Launcher/VersionOps.cs`);没有对应文件的 10 = 既有 9 个 + 本包新建的 `Launcher/PosixShell.cs` |
| `git diff --stat`(提交前)| 4 个复制文件 **+162 / -14** 行;其中 `BackendApi.cs` 只有 2 行改动(`2 +/-2`),其余三个文件的改动全在本节逐条列出 |

### 未验证(本机是 Windows,没有 Mac)

1. **mac 上双击 `.command` 能不能起来**:`.command` 由 Terminal 执行、必须有可执行位这两条按任务书给定的事实实现,**真机没跑过**;`File.SetUnixFileMode` 在本机(Windows)是空操作,所以「0755 到底设上没有」只能靠真机日志确认。
2. **`_quote` 的那些旧用法在 mac 上已不再使用,但 Linux 的 `.desktop` `Exec=` 仍是弱引号**(只记录),真机上没验。
3. **`DesktopDir()` 在 mac 上到底返回哪个目录**(`~/Desktop` 还是 `~/桌面`,或最坏情况的 `HOME` 根)没有真机验证;本包的用例**不碰真实桌面**,也没有模拟它。
4. **导出的 `.command` 在 mac 上真的能启动 Minecraft** 没有验证(要真机 + 装好的实例)。
5. `ExportVersionJson` 非 Windows 分支在真机上拿到的父版本 JSON 形态没有验证(本机只用假 loader 测)。
6. mac 上「打开下载页」这一步本身属于 UI 阶段,本包没有实现,也没有验证。

### 遗留与风险

1. `Updater.Check` 新增的 `can_self_update` 需要 UI 阶段接上(否则 mac 上界面仍会给出「立即更新」的入口,点了会拿到那句中文报错)。
2. `PosixLaunchScript` 的 cwd 走 `Py.Str(cwd)`,null 会变成字符串 `None` —— 这是与 `ExportLaunchBat` 的 `Py.Str(cwd)` **同源**的既有语义(不是本包新引入),用例已把这个行为写清楚。
3. mac 分支的 `.command` 里 `exec` 会把 shell 换成目标进程(这是快捷方式该有的行为);导出脚本**故意不用 `exec`**,以便 Terminal 正常显示退出码。
4. `_create_macos` 多了一个参数而 `arguments` 在 mac 分支不参与正文;将来若有人往 mac 正文里加内容,记得别把预拼接的 `arguments` 又接回去。
5. `%TEMP%` 下出现了一个叫 `tipcl-probe` 的目录(创建于 22:28:19,窗口内),**不是本包产物**:仓库里没有任何代码提到这个名字,目录里是另一个会话的 .NET 探针工程(`probe.csproj` / `Program.cs` / `bin` / `obj` / `out` / `scratch`)。已记入 [DECISIONS.md](DECISIONS.md) D-61。
6. 本包没有改 `Utils.cs` / `Java.cs` / `McLauncher.cs` / `Installer.cs` / `Preflight.cs` / `GlobalMods.cs` / 其它复制文件;没有改 TFM、没有扩 `NoWarn`、没有新增警告;没有联网请求。

### Lead 验收(2026-11-12 22:40)

结论:**B2c3 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src/TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 有对应文件的 238 个里 216 一致、22 个有改动(原有 18 个 + 本包 `BackendApi.cs`、`Shortcut.cs`、`Updater.cs`、`VersionOps.cs`);无对应文件的 10 个是原有 9 个 + `Launcher\PosixShell.cs` |
| `git diff b54f409..HEAD -- BackendApi.cs`(Lead 逐行读过) | 只有 `export_launch_script` 里的两行:版本 JSON 经 `ExportVersionJson`,文件后缀与写盘经 `ExportScriptExtension` / `ExportLaunchScript`。Windows 上传入的仍是同一个 JSON 对象、后缀仍是 `.bat`、仍调 `ExportLaunchBat` |
| `Updater.cs`、`Shortcut.cs`、`VersionOps.cs` 的改动与新文件 `PosixShell.cs`(Lead 逐行读过) | `Updater`:`Download` / `ApplyStaged` 开头加平台守卫,`Check` 两个返回各多一个 `can_self_update` 键,`BuildApplyScript` 未动。`Shortcut`:`Chmod755` 不再是空方法;mac 的 `.command` 正文改用单引号规则,描述里的换行压成空格;Windows / Linux 分支收到的参数未变。`VersionOps`:`ExportLaunchBat` 未动,新增的都是独立方法 |
| 红线文件、`Ported\` 下全部文件、`TipCL.Tests.csproj`、`Directory.Build.props`、`TipCL.Core.csproj` | 未改 |
| 已有测试被改动的 | 0 个 |
| 本次提交改动的文本文件做严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告只有 SYSLIB0014 ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 1680 / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 1680 / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke` light / dark | 退出码 0 / 0,SHA256 与 A1 相同 |
| `%LOCALAPPDATA%\TipCL`(测试后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致;其下没有出现 `exports` 目录 |
| 真实桌面(Lead 复跑前后) | 条目数 14 → 14,最新写入时间未变 |
| `%TEMP%` 下 `tipcl*` 目录名字集合(Lead 复跑前后) | 87 → 87,没有新名字 |
| 残留进程 `testhost` / `java` / `javaw` / `adb` / `TipCL`,以及新起的 `powershell` | 0 个 |
| B2c3 测试扫描 | 没有联网、起进程、调 `Updater.Check` / `Download` / `ApplyStaged`、调 `Shortcut` 的创建 / 删除 / 桌面目录入口、读桌面路径、改环境变量 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

Lead 的说明:

- `%TEMP%\tipcl-probe`(22:28:19 创建)Lead 复核:前缀在本仓库 `src` / `tests` 里零命中,目录里是一个独立的 .NET 工程,不是本仓库的测试所建。
- 这一包顺带堵了一个从 Windows 版带来的问题:mac 的 `.command` 快捷方式原来把实例名、版本号、用户名用双引号直接拼进 shell 脚本且不转义,名字里带引号或 `$` 就能注入命令。
- D-60、D-61 接受。
- 留给界面阶段:`can_self_update` 为 false 时界面要把「立即更新」换成「打开下载页」;下载页地址清单里目前只有更新包的 `url`,没有单独的页面地址,C 阶段接设置页时再定。

B2 阶段(mac 逻辑核对)到此全部完成:B2a、B2b、B2d、B2e、B2c1、B2c2、B2c3。

---

## B3 打包

### 做了什么

1. **审读并修好了继承来的打包器草稿**。`b826a1b`("B3 WIP checkpoint")里那 10 个文件是上一个被中断的子代理留下的,**没评审过、没跑过**;
   本包逐行对照任务书的「Bundle layout」「Tar entry modes」「Verification performed by the packager」三节读完,改动**只有两处**(见下),其余原样保留。
2. **新增 [tools/package.ps1](tools/package.ps1)**:`dotnet build-server shutdown` → 两条 self-contained publish(osx-arm64 / osx-x64)→ 打包器组装 + 写归档 + 自校验。
   PowerShell 5.1 兼容,控制台输出全 ASCII(D-08),本脚本自身存成 **UTF-8 with BOM**(`tools/build.ps1` 是无 BOM 的,内容只有中文**注释**所以侥幸没坏;
   本脚本注释更多,按 D-12 补上 BOM,避免 5.1 按 ANSI 读)。全步输出进 `artifacts\package-logs\*.log`,控制台只留每步一行状态 + 打包器校验清单。
3. **真实发布 + 真实打包**(两次),产出 `dist\TipCL-macos-universal.tar.gz`,两次 SHA256 完全相同(见下表)。
4. **`.ico` 取证**(读字节 + SkiaSharp 实测),`icns` 按取证结论产出 `ic07/ic08/ic13`。
5. **新增 116 条 B3 用例**(7 个文件),两条测试通道都 1796/1796 全绿。

### 关键文件

| 文件 | 说明 |
|---|---|
| [tools/package.ps1](tools/package.ps1) | 本包新增:发布 → 组装 → 打包 → 校验的入口(参数 `-NuGetSource`) |
| [tools/TipCL.Packager/TarGz.cs](tools/TipCL.Packager/TarGz.cs) | 本包修改:Pax → **Ustar** + 名字长度上限显式报错 |
| [tools/TipCL.Packager/Program.cs](tools/TipCL.Packager/Program.cs) | 本包修改:只删 `TipCL.app` 子树;补报跳过的 `.pdb` 数 |
| [tools/TipCL.Packager/BundleLayout.cs](tools/TipCL.Packager/BundleLayout.cs) / [InfoPlist.cs](tools/TipCL.Packager/InfoPlist.cs) / [LauncherScript.cs](tools/TipCL.Packager/LauncherScript.cs) / [IcnsWriter.cs](tools/TipCL.Packager/IcnsWriter.cs) / [IconBuilder.cs](tools/TipCL.Packager/IconBuilder.cs) / [MachO.cs](tools/TipCL.Packager/MachO.cs) / [BundleVerifier.cs](tools/TipCL.Packager/BundleVerifier.cs) | 草稿原有,本包逐项核对后未改 |
| [tests/TipCL.Tests/B3PackagerFixtures.cs](tests/TipCL.Tests/B3PackagerFixtures.cs) | 本包新增:合成 Mach-O / 假 bundle / 校验助手(临时文件全部落在 `TestHome.Root` 之下) |
| [tests/TipCL.Tests/B3InfoPlistTests.cs](tests/TipCL.Tests/B3InfoPlistTests.cs) / [B3LauncherScriptTests.cs](tests/TipCL.Tests/B3LauncherScriptTests.cs) / [B3IcnsWriterTests.cs](tests/TipCL.Tests/B3IcnsWriterTests.cs) / [B3MachOTests.cs](tests/TipCL.Tests/B3MachOTests.cs) / [B3ModeClassificationTests.cs](tests/TipCL.Tests/B3ModeClassificationTests.cs) / [B3TarGzTests.cs](tests/TipCL.Tests/B3TarGzTests.cs) | 本包新增:116 条用例 |

产物目录都在 `.gitignore` 里:`artifacts\publish\osx-*`、`artifacts\package\TipCL.app`、`artifacts\package-logs\`、`dist\TipCL-macos-universal.tar.gz`。

### 继承草稿只改了两处

1. **tar 格式 `Pax` → `Ustar`**([TarGz.cs](tools/TipCL.Packager/TarGz.cs))。这是本包实测发现的**真问题**,不是口味问题:
   .NET 8 的 `PaxTarEntry` 每次写都会补 `atime` / `ctime` 两条扩展记录,而**取值是写的那一刻**(`PaxTarEntry` 没有公开的 `Atime` / `Ctime` 可以钉死),
   于是同一份输入两次写出来的字节不同(探针实测第一次差在第 533 字节:`atime=1794497685.6498163` vs `atime=1794497685.6521515`),`tar.gz` 的 SHA256 也就不可复现。
   Ustar 没有扩展头,同一份输入两次写出**逐字节相同**(探针实测;Gnu 也相同,选 Ustar 是因为它是更基础、更通用的形态)。
   探针在 `artifacts/tarprobe/`(不入库),结论记入 DECISIONS.md D-62。
   代价:Ustar 名字字段只有 100 字节 → 加 `TarGz.NameMaxBytes` 上限并在超限时显式抛错;本 bundle 实际最长的名字是
   `TipCL.app/Contents/MacOS/arm64/libSystem.Security.Cryptography.Native.OpenSsl.dylib`(83 字节),有富余,已用用例钉住。
2. **暂存目录只清 `TipCL.app`**([Program.cs](tools/TipCL.Packager/Program.cs))。草稿是 `Directory.Delete(stage, recursive: true)`;
   `--stage` 是外部传进来的参数,万一指到仓库根就删掉一整棵树。tar 条目全部来自 `TipCL.app/` 之下,清掉它就够保证两次运行一致,所以改成只删子树。
   顺带在 staged 那行补报跳过的 `.pdb` 数(实际是 2 + 2),让"没把 .pdb 打进包里"这件事在日志里看得见。

核对过、**没有改**的点(逐项对照任务书):六个必填目录 + 9 个必填文件;`Info.plist` 的 12 个键与值(名字/显示名/版本取 Core 的 `AppInfo`,不另抄);
启动脚本 6 行逐字相同(LF、无 BOM、结尾一个换行、150 字节);icns 只出 `ic07/ic08/ic13`、不出 `ic09`;目录 0755、数据 0644、三个可执行 0755;
Mach-O 头的字段偏移与 `cmd == 0x1D` 判据;校验清单 13 项 + `OK` / `FAIL` 行格式;退出码 0/1/2;控制台只输出 ASCII。

### `.ico` 取证(读字节 + 实测,不是凭记忆)

`packaging\TipCL.ico` = **94953 字节**(Windows 图标的字节级副本)。按 ICO 目录结构读出来(探针 `artifacts/iconprobe/`,不入库):

| 条目 | 尺寸 | 位深 | 数据长度 | 偏移 | 数据头 |
|---|---|---|---|---|---|
| 0 | 16×16 | 32 | 841 | 118 | `PNG` |
| 1 | 24×24 | 32 | 1537 | 959 | `PNG` |
| 2 | 32×32 | 32 | 2452 | 2496 | `PNG` |
| 3 | 48×48 | 32 | 4613 | 4948 | `PNG` |
| 4 | 64×64 | 32 | 7416 | 9561 | `PNG` |
| 5 | 128×128 | 32 | 21832 | 16977 | `PNG` |
| 6 | 256×256 | 32 | 56144 | 38809 | `PNG` |

SkiaSharp 2.88.9 在这台机器上**认得 .ico**(`encodedFormat=Ico`),`SKCodec.Create` 给的是**最大的那张**:`256×256`、`Bgra8888/Unpremul`,
`GetPixels` 返回 `Success`;`ScalePixels(High)` 缩到 128 返回 `true`。据此产出 `ic07(128, 由 256 缩下来)`、`ic08(256, 原尺寸重编码)`、`ic13(256, 同 ic08)`:
`TipCL.icns` = **133917 字节**,三个元素负载 19655 / 57115 / 57115(`ic08` 与 `ic13` 同一份 256 PNG;128 的那张走 256→128 缩小,不是去取源文件里那张原生 128 PNG,见 D-66)。

### 代码签名结论(只报告,不判失败)

打包器对bundle 里每个能解析成 **thin 64 位 Mach-O** 的文件报告 `LC_CODE_SIGNATURE`:实跑结果 **30 个,带签名的 30 个,不带的 0 个**
(2 个 apphost + 2 个 `createdump` + 26 个 thin dylib,arm64/x64 各一半)。**没有任何一个 Mach-O 文件缺签名命令。**

同时如实记录一个**解析边界**:`libSkiaSharp.dylib` / `libHarfBuzzSharp.dylib` / `libAvaloniaNative.dylib` 这 3 个 × 2 架构 = **6 个 dylib 是 universal(fat)Mach-O**
(头 4 字节 `CA FE BA BE`,偏移 4 的 `nfat_arch` = 2),任务书给的 64 位小端单架构定义覆盖不到它们,所以这 6 个**没有**出现在上面的 30 里,
它们的 `LC_CODE_SIGNATURE` **未验证**。这不影响权限位:它们以 `.dylib` 结尾,按后缀规则一律 0755(校验里 `32 dylibs 0755` 就是 16 × 2)。

### 真实打包运行(两次,都是 `tools\package.ps1` 全流程)

命令:`powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1 -NuGetSource https://api.nuget.org/v3/index.json`

| 项 | 第 1 次 | 第 2 次 |
|---|---|---|
| 退出码 | **0**(126.7 s) | **0**(111.8 s) |
| 每架构 publish 文件数 | osx-arm64 **219** / osx-x64 **219** | 同 |
| 跳过的 `.pdb` | 2 + 2 | 同 |
| 进 bundle 的文件数 | arm64 217 / x64 217 | 同 |
| 归档条目数 | **443**(6 目录 + 434 文件 + Info.plist + 启动脚本 + icns) | 同 |
| 未压缩总量 | **210,954,125 字节** | 同 |
| `dist\TipCL-macos-universal.tar.gz` | **85,812,352 字节** | 同 |
| SHA256 | `66802328B2B2279E4CADB13362A0705899C54070E3F2DB43B097E5F538C06CDC` | **完全相同** |

NuGet 因为缓存里本来没有 mac 运行时包,第一次 self-contained publish 要下载;本机实测下到 NuGet 缓存的 6 个包(都是 **8.0.33**,与共享运行时版本一致):

| 包 | 版本 | 缓存目录创建时间 |
|---|---|---|
| `microsoft.netcore.app.runtime.osx-arm64` | 8.0.33 | 23:20:30 |
| `microsoft.netcore.app.host.osx-arm64` | 8.0.33 | 23:20:30 |
| `microsoft.aspnetcore.app.runtime.osx-arm64` | 8.0.33 | 23:20:30 |
| `microsoft.netcore.app.runtime.osx-x64` | 8.0.33 | 23:22:11 |
| `microsoft.netcore.app.host.osx-x64` | 8.0.33 | 23:22:11 |
| `microsoft.aspnetcore.app.runtime.osx-x64` | 8.0.33 | 23:22:11 |

这 6 个包是开工时先用同一对 publish 命令**预热**时下下来的(日志 `artifacts\package-logs\warm-publish-*.log`),
随后脚本那两次全流程跑的是缓存命中(每步仍然真跑了 publish,只是不再下载)。机器上的 NuGet 配置一个字没改,仓库里也没有加 `NuGet.config`;
`-NuGetSource` 只是把 `--source` 透传给两条 publish 命令(见 D-67)。

打包器校验清单(第 2 次运行逐行原样,13 项):

```
    OK    entry names -- 443 entries, all under TipCL.app/ with '/' separators and no '..'
    OK    required entries -- 9 files + 6 directories present
    OK    directory modes (0755) -- 6 directories all 0755
    OK    launcher + apphost modes (0755) -- 3 executables all 0755
    OK    data modes (Info.plist + icns 0644, dylibs 0755) -- 2 data files 0644, 32 dylibs 0755
    OK    no group/other write bits -- all 443 entries clean
    OK    apphost arm64 (TipCL.app/Contents/MacOS/arm64/TipCL) -- cputype=0x0100000C cpusubtype=0x00000000 filetype=2 ncmds=18 sizeofcmds=1752
    OK    apphost x86_64 (TipCL.app/Contents/MacOS/x64/TipCL) -- cputype=0x01000007 cpusubtype=0x00000003 filetype=2 ncmds=18 sizeofcmds=1752
    OK    Info.plist keys -- 12 keys match
    OK    TipCL.icns structure -- magic ok, total=133917 bytes, elements: ic07(19663) ic08(57123) ic13(57123)
    OK    launcher script bytes -- 150 bytes, LF only, no BOM, byte-identical to the expected text
    OK    no .pdb / no .exe entries -- 0 found
    OK  * code-signature report -- mach-o files=30 with LC_CODE_SIGNATURE=30 without=0
```

(校验读的是**刚写进磁盘、再从磁盘读回来**的归档,不是内存里那份;最后一行 `OK *` 星号表示只报告、不算失败。)

### 验证命令与实际结果

| 命令 | 结果 |
|---|---|
| `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;警告合计 8 = `NU1900` ×7(镜像不可达的环境噪声)+ `SYSLIB0014` ×1(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs(116,38)`,本包未动) |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,通过 **1796** / 失败 0 / 跳过 0 |
| `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 **0**,总计 **1796**,通过 1796 / 失败 0 / 跳过 0 / **不支持 0** |
| `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\smoke-light.png"` / `--theme dark ...` | 退出码 **0 / 0**,**3739 / 3751 字节**,SHA256 `17F3CD87...017BF` / `374716FF...38144`,与 A1 起逐次相同(两次 `-r osx-*` publish **没有**动到 `bin\Release\net8.0\TipCL.exe`) |
| `%LOCALAPPDATA%\TipCL` 开工时 / 全部跑完 | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,前后一致 |
| 残留进程 `testhost` / `TipCL` / `java` / `javaw` / `adb` / `dotnet` | 全 **0** |
| `%TEMP%` 下 `tipcl*` 目录名字集合 | 现在 **87** 个,与 B2c3 的开工快照(86)相比多出来的只有 `tipcl-probe`(22:28:19 创建,早于本包开工的 23:19,属 D-61 记的另一个会话的探针,**不是本包产物**);本包窗口内**没有新名字** |
| 真实桌面条目数(只读计数) | **14**,与 Lead 记录一致,本包没有写任何东西进去 |
| `src\TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 总数 **248** / 一致 **216** / 有改动 **22** / Windows 源里没有对应文件 **10** —— 与 Lead 的上一组数字完全一致(本包没碰 Core) |
| `git status --short`(提交前) | 只有本包自己新增/修改的源码与文档,没有 `bin` / `obj` / `artifacts` / `dist` / PNG / `test-home` |

### 新增测试(116 条,1680 → 1796)

| 文件 | 条数 | 覆盖 |
|---|---|---|
| B3InfoPlistTests.cs | 19 | 12 个键与值、`AppInfo` 取值、声明与 DOCTYPE、`true/` 元素、五个 XML 字符转义、XML 往返、坏文档报错 |
| B3LauncherScriptTests.cs | 8 | 逐字文本、LF/无 BOM/结尾换行、六行结构、两条分支、纯 ASCII |
| B3IcnsWriterTests.cs | 20 | magic 与大端总长、元素类型/长度、空元素表、四种不自洽长度、`SelectSlots` 512/256/128/64、**真实 .ico 的 7 个条目与 256 解码**、重编码确定性 |
| B3MachOTests.cs | 20 | 合成 arm64 / x86_64 头、有/无 `0x1D`、非 Mach-O、null、字节不足、`ncmds` 超出、`cmdsize=0` 不空转、只有 filetype 2 算可执行、未知 cputype 回退 |
| B3ModeClassificationTests.cs | 18 | 启动脚本与两个 apphost、`.dylib`、`.dll`、`.json`、别名的 Mach-O 可执行、fat 名不符的后缀、目录、`0022` 掩码、必填条目清单 |
| B3TarGzTests.cs | 29 | 往返名字/权限、顶层条目、目录 0755、序数排序、**两次写出的 tar / tar.gz / 落盘归档逐字节相同**、gzip MTIME 为 0、名字超 100 字节显式报错、校验器全过 + 8 种「故意写坏」必失败、代码签名计数与清单 |
| B3PackagerFixtures.cs | 2(助手) | 合成 Mach-O / 假 bundle / 仓库文件定位;临时文件全在 `TestHome.Root` 之下 |

用例里的期望值都在注释里标了来源:**任务书**(字段定义、权限位、文本)还是**实测**(`.ico` 的 7 个条目、SkiaSharp 解 256、
最长文件名 83 字节、apphost 的 `sizeofcmds=1752`)。测试不起进程、不联网、不跑 `dotnet publish`、不调 `Program.Main`。

### 未验证(本机是 Windows,没有 Mac)

1. **`.app` 在 Mac 上到底能不能起来**:双击、Gatekeeper 拦不拦、`xattr -cr` 之后能不能运行 —— **全未验证**。
2. 解包后**权限位是否真的保留**(`tar -xzf` 的行为、Finder 解压的行为):本包只验到**归档里**的 mode 位,没验到磁盘上的。
3. `TipCL.icns` 在 Finder / Dock 里**画出来什么样**:只验了结构(magic / 总长 / 元素类型与长度 / PNG 签名),没验渲染。
4. 6 个 universal dylib 的 `LC_CODE_SIGNATURE`(解析器只认 thin Mach-O,见上)。
5. `LSMinimumSystemVersion = 10.15`、`NSPrincipalClass = NSApplication` 这些键在真机上够不够:值来自任务书,没有对照 Apple 文档独立核实。
6. `uname -m` 在 Intel Mac 上返回 `x86_64` 是任务书给的事实;本机没有 Intel Mac 可验,启动脚本只按「等于 `arm64` 走 arm64,其余走 x64」写。
7. 未签名产物在 macOS 上的**长期**行为(每次打开都要 `xattr -cr` 还是只需一次)、以及 macOS 版本差异,没验。
8. Avalonia/osx 的运行期行为(窗口、字体、Keychain)属 B1/C 阶段,不在 B3 范围。

### 遗留与风险

1. **归档里没有任何签名步骤**(任务书不许签名):分发时必须在 README 里写 `xattr -cr TipCL.app`,这是 D2 文档包的事。
2. `dist\TipCL-macos-universal.tar.gz` 在 `.gitignore` 里,**不入库**;要让别人拿到,得另行拷贝(本包不 push、不发 release)。
3. 两个架构目录里各自带了一份**同样的 universal dylib**(Skia/HarfBuzz/AvaloniaNative 3 个 × 2 架构 = 6 个文件被装了两遍),
   归档因此比"必要"大约多出几十 MB。这是"两个目录各自放完整 publish 输出"这个布局的直接结果,不改布局就省不掉;记在这里当优化项。
4. `artifacts\package\TipCL.app` 是暂存树(打包后留在磁盘上,已在 `.gitignore` 里),不影响归档内容。
5. `TipCL.Mac.sln` 第一行的 **UTF-8 BOM 在上一个 WIP 提交里丢了**(现在头两个字节是 `0D 0A`):按要求**保持原样不动**,只在这里记一笔;
   它不影响 `dotnet build` / `dotnet test`(实测两条通道都正常)。
6. 打包器用 Ustar:将来若 bundle 里出现**超过 100 字节**的条目名,打包会显式失败(不会静默截断),到时候要么改名要么换回带扩展头的格式。
7. 本包没有改 `src\TipCL.Core\` 下任何文件、没有改 `TipCL.App.csproj`(publish 不需要额外属性)、没有改 TFM、没有扩 `NoWarn`、没有新增警告。
8. `package.ps1` 只对 `publish` 传 `--source`;`dotnet run`(打包器自己)那一步走缓存 + 机器上的 NuGet 配置,所以它的日志里会出现 `NU1900` 噪声警告。

### 需要有人在 Mac 上核对的事(B3 交出去的清单)

1. `tar -xzf TipCL-macos-universal.tar.gz` 之后 `ls -l TipCL.app/Contents/MacOS/`:目录是 `drwxr-xr-x`,`TipCL` / `arm64/TipCL` / `x64/TipCL` 是 `-rwxr-xr-x`,
   每个 dylib 也应是 `-rwxr-xr-x`,`.dll` / `.json` / `Info.plist` / `TipCL.icns` 应是 `-rw-r--r--`。
2. `xattr -cr TipCL.app` 再双击:Apple 芯片上应跑 `Contents/MacOS/arm64/TipCL`,Intel 上应跑 `x64/TipCL`(可用 `ps -ax | grep TipCL` 看路径确认)。
3. 图标在 Finder / Dock 里是否正常显示(128 / 256 两个尺寸够不够看)。
4. Gatekeeper 具体拦成什么样(是"来自身份不明的开发者"还是"已损坏"),把原话记下来。
5. 归档里**没有任何 Mach-O 签名命令的缺失项**,但这不等于 macOS 认这份产物;真机结论以实际启动为准。
6. 路径里带中文/Finder 中文名时能不能正常启动(本机路径 `D:\项目\tipcl\mac` 只有 Windows 侧验过)。
7. B3 产出的界面还是 A1 的占位窗口:**"能起来"不等于功能可用**,功能可用要等 C 阶段。

### Lead 验收(2026-11-13 06:55)

结论:**B3 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| `src\` 全部文件 | 自 `d4ad4ce` 起一个字节未改(`git diff --name-status d4ad4ce HEAD -- src` 为空) |
| 红线文件、`Ported\`、`AssemblyInfo.cs`、`TestHomeIsolation.cs`、`Program.cs`、`.gitattributes`、`Directory.Build.props` | 未改 |
| 已有测试被改动的 | 只有 `TipCL.Tests.csproj` 多一条 `ProjectReference`(任务书授权) |
| 仓库内是否新增 `NuGet.config` | 没有;`dist\`、`artifacts\`、`bin\`、`obj\`、PNG、`test-home` 均未入库 |
| `packaging\TipCL.ico` 与 Windows 源 | SHA256 一致 |
| `tools\package.ps1` | UTF-8 with BOM;非 ASCII 只出现在注释里;输出行全是 ASCII |
| 提交的文本文件严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| 归档本身(Lead 用 `tar -tvzf` 独立读回) | 443 条目;全部以 `TipCL.app/` 开头、`/` 分隔、无 `..` 反斜杠;owner 0/0;mtime 全部 `2000-01-01` |
| 权限位 | 400 个文件 0644、37 个 0755、6 个目录 0755;32 个 `.dylib` 全部 0755;没有任何条目带组/其它写位;`Info.plist` 与 `TipCL.icns` 是 0644 |
| 可执行的三项 | `Contents/MacOS/TipCL`(150 字节)、两个架构的 apphost 都是 `-rwxr-xr-x` |
| 两个架构的对称性 | arm64 217 个文件、x64 217 个文件,文件名集合完全一致(比较结果 0 处差异) |
| 启动脚本(Lead 读原始字节) | 150 字节,CR=0、LF=6、无 BOM、无非 ASCII,正文与任务书逐字一致 |
| `Info.plist` | 878 字节,UTF-8 无 BOM;`System.Xml` 解析通过,12 个键;中文显示名 `TipCL 启动器` 正常 |
| Mach-O 头(Lead 自己写解析器复核) | arm64 apphost `cputype=0x0100000C`、x64 `cputype=0x01000007`,两者 `filetype=2` |
| 代码签名(Lead 独立复核,含 fat 切片) | 30 个 thin Mach-O 全部带 `LC_CODE_SIGNATURE`;**6 个 universal dylib 的两个切片也都带签名**,共 42 个切片;逐个用 CodeDirectory 的页哈希重算,`badPages=0`(全部完好) |
| 两次运行的确定性 | 85,812,352 字节,SHA256 `66802328B2B2279E4CADB13362A0705899C54070E3F2DB43B097E5F538C06CDC`,两次完全相同 |
| 归档压缩前后 | 未压缩 210,954,125 字节 / 条目 443 |
| `%LOCALAPPDATA%\TipCL`(本次工作全程) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| 残留进程 `testhost` / `TipCL` / `java` / `adb` | 0 个 |
| 红线 | 无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |

**对子代理 2 个待裁决事项的裁决**:

1. **universal dylib 的签名覆盖**:Lead 已用自己写的 fat 头解析器复核,6 个 fat dylib 的**两个切片都带签名且页哈希完好**,所以当前产物没有问题。
   但"校验器不解析 fat 头"确实是个盲区:将来换 SkiaSharp / Avalonia 版本时如果某个 fat 切片变成未签名,现有校验会给出假阳性。
   裁决:归 D1(打包校验加固)补上 fat 头解析,**本包不改**。
2. **6 个 universal dylib 在两个架构目录各存一份**(约占压缩后体积几十 MB):接受,不优化。
   理由:这是「两个目录各放一份完整 publish 输出」布局的直接结果,改动布局会牵动启动脚本与 `Info.plist`,收益只是体积。归 D2 记录,不在 C 阶段处理。

Lead 说明:

- `TipCL.Mac.sln` 第一行的 UTF-8 BOM 在 `b826a1b` 那次 WIP 提交里丢了。BOM 只是编辑器提示,不影响 `dotnet` 解析,保持现状。
- 上一个子代理被中断是客观事故,不是它做错了什么;接手者在 `6f83dec` 里把草稿从 Pax 换回 Ustar(定值 mtime)、把暂存目录清理收窄到 `TipCL.app` 子树,这两处都是实质修复。

**B 阶段到此全部结束**(B1a、B1b、B2a、B2b、B2c1、B2c2、B2c3、B2d、B2e、B3)。下一步进入 C 阶段(界面)。

---

## C1a 主题色板 + 图标字体 + Lucide 图标

### Lead 验收(2026-11-13 07:55)

结论:**C1a 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 两次提交(`27ccd5f` src + `e8bb9a8` 测试/文档) | 工作树干净;无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| `src\TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,**与 B3 验收时完全相同**(本包一个字节没碰 Core) |
| 红线文件(测试工程隔离三件、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| 已有测试被改动的 | 只有 `Ported\IconFontTests.cs`(A6 授权范围内的 3 处路径适配)与两个 csproj |
| 调色板键集合(Lead 独立比对) | 浅色 61 = 深色 61 = Windows 源 61;**键名与键序完全一致**(`windows 独有` 与 `mac 独有` 均为空) |
| 调色板取值(Lead 独立比对) | 61 组 `键=颜色/不透明度` 平铺对比 **0 处差异**;三处渐变的停靠点与颜色逐一相同 |
| `TipCLIcons.ttf` 与 LICENSE | 与 Windows 源 SHA256 一致 |
| `Services\LucideAssets.cs` | 与 Windows 源 SHA256 一致(字节级复制) |
| 本次提交文本文件严格 UTF-8 解码 + 乱码扫描 | 全部合法 |
| 入库垃圾(`bin`/`obj`/`dist`/`artifacts`/PNG/`test-home`) | 0 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` ×4(本机镜像不可达的环境噪声)+ `SYSLIB0014` ×1 |
| `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 **1900** / 失败 0 / 跳过 0 |
| `dotnet exec ...\TipCL.Tests.dll`(进程内执行器) | 退出码 0,通过 **1900** / 失败 0 / 跳过 0 / 不支持 0 |
| `--smoke --theme light` | 退出码 0,**3713 字节**,SHA256 `D4F53678CF9314B9F3A69B42F778F6A8FF54ADE8E8F0FEB086C4EE00355A3037` |
| `--smoke --theme dark` | 退出码 0,**3669 字节**,SHA256 `CFD798FF7B4512D99251F916D0FA72C6F15E5D427ACA1EBB25F77D9C0837FBA3`;与 light **不同** |
| 截图像素(Lead 用 `System.Drawing` 采样) | 两张都是 900×550;左上角 `#F5F5F5` / `#161616`,正是**浅色/深色色板的 `B.Canvas`**;中心文字色 `#2C2626` / `#EDEDED`(对应 `B.Ink` 的 `#262626` / `#EDEDED`,浅色那张是抗锯齿像素) |
| `--smoke --page home --theme dark` / `--smoke --theme blue` | 退出码 0 / **2**(参数校验仍有效) |
| `%LOCALAPPDATA%\TipCL`(测试前后) | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| `%TEMP%` 下 `tipcl*` 目录名字集合 | 89 → 89,没有新名字 |
| 残留进程 `testhost` / `TipCL` / `java` / `adb` | 0 个 |

**对子代理 3 个待裁决事项的裁决**:

1. **`Services\Ico.cs` 一并搬进 C1a**:接受。它是图标字体的字形码位表,没有 WPF 依赖,和 `LucideAssets.cs` 一样属于图标系统;`Ported\IconFontTests` 正是靠它才有意义。
2. **测试工程引用 `TipCL.App` + `TipCL.App` 开 `InternalsVisibleTo`**:接受(D-69)。代价是测试进程里多一套 Avalonia 程序集,但 C1a 的用例本来就必须起 headless Avalonia(`Geometry.Parse` 没有平台会直接抛)。
3. **`--smoke` 基准哈希更新为 3713 / 3669**:接受。D-04 的占位色已删,颜色改取自真色板,`--smoke` 的基准从此刻起就是这两个值;A1/B3 验收表里的 3739 / 3751 就此作废(已在上面表格留档)。

**新基准(后续包不许改)**:`--smoke --theme light` = 3713 字节 / `D4F53678…`;`--smoke --theme dark` = 3669 字节 / `CFD798FF…`。

**Lead 记下的两条风险(交给 C2)**:

- **主题热切换**:`Palette` 是取色时按当前变体解析一次,已建好的控件不会跟着换主题。C2 接主题切换时必须改成动态资源或订阅变体后重建。
- **`Motion.cs` 未移植**:`Lucide` 现在自带最小动画实现与一个 `MotionEnabled` 静态开关,C2 要换成正式门面(D-70)。

**D-69 那个坑值得单独记一笔**:Avalonia 建 Dispatcher 会替换当前线程的 `SynchronizationContext`,排在其后的 async 用例续体会永久排队 —— 表现为「用例全跑完却卡死、连汇总行都不打」。`C1aFixtures.EnsureAvalonia()` 已在 `finally` 里还原。以后任何包引入 Avalonia 初始化都必须照做。







---

## C1a 主题色板 + 图标字体 + Lucide 图标(Avalonia 移植)

### 做了什么

1. **两份主题色板移植为 axaml**:[src/TipCL.App/Themes/Palette.Light.axaml](src/TipCL.App/Themes/Palette.Light.axaml) /
   [Palette.Dark.axaml](src/TipCL.App/Themes/Palette.Dark.axaml)。键名、键序、取值与 Windows 版
   `Themes\Palette.Light|Dark.xaml` **逐项一致**(各 61 个键 = 7 个 `C.*` 颜色 + 54 个刷子,含 3 个渐变),
   语法只做等价翻译(渐变 `0,0 → 1,1` 写成 `0%,0% → 100%,100%`,语义都是相对包围盒)。
   开工时用脚本把两份 axaml 与两份 WPF 源逐条对拍过(61/61 条,唯一差异就是渐变端点写法)。
2. **色板接线**:[src/TipCL.App/Themes/Palette.cs](src/TipCL.App/Themes/Palette.cs) + `App.Initialize()` 里的
   `Palette.Install(this)`,把两份 `ResourceInclude` 挂进 `Application.Resources` 的 `ThemeDictionaries[Light/Dark]`
   (Avalonia 11 的惯用法,见 [DECISIONS.md](DECISIONS.md) D-68)。按字符串查键(`B.*` / `C.*`)走
   `Palette.Brush` / `Palette.Color`,**查不到给中性灰兜底,不抛异常**。
3. **图标字体**:`Src\TipCL.App\Resources\fonts\TipCLIcons.ttf`(14232 字节,与 Windows 源 SHA256 相同)
   与 `LICENSE-FluentSystemIcons.txt`(1078 字节)按 `Copy-Item -LiteralPath` 字节级复制;
   csproj 里注册成 `AvaloniaResource`,运行期字体族的 URI 是
   `avares://TipCL/Resources/fonts/TipCLIcons.ttf#TipCL Icons`(家族名取自 ttf 的 name 表 nameID 1,与 Windows 版
   `Services\Ui.cs` 里的 `#TipCL Icons` 一致)。`Palette.IconFont` / `Palette.IconFontUri` 是 C1c 的 `Ui.GlyphFont` 要用的入口。
4. **Lucide 图标系统**:[src/TipCL.App/Services/Lucide.cs](src/TipCL.App/Services/Lucide.cs)(WPF → Avalonia 移植,
   15 个 `public const string` path data 常量逐字节照抄)+
   [LucideAssets.cs](src/TipCL.App/Services/LucideAssets.cs)(143 KB base64 PNG 表,**字节级复制,未改一个字节**)
   + [Ico.cs](src/TipCL.App/Services/Ico.cs)(67 个字形常量,同样字节级复制)。
   公开方法 `Icon` / `Spinner` / `Asset` / `Rotate` / `Shimmer` / `ToolIcon` 的名字与参数默认值照旧;
   实现上的四处等价改写在 D-70 里逐条记了(几何烘缩放 / 动画守卫 / `Rectangle` + `OpacityMask` / `ToolIcon` 内联 `IsMcp`)。
5. **删掉 A1 的占位色(D-04)**:[MainWindow.cs](src/TipCL.App/MainWindow.cs) 改成从色板取
   `B.Canvas`(底板)与 `B.Ink`(文字),窗口仍是 900×550 / Min 720×480、仍无标题栏导航
   (C2 的事),`ApplyThemeVisuals()` 与 `PageId` 的对外行为不变,`--smoke` 照旧编译。
6. **新增 104 条用例**(1796 → 1900),并把 A6 排除的 `Ported\IconFontTests.cs` 恢复编译(按 A6 的 B 类路径规则改 3 处,断言一字未改)。

### 关键文件

| 文件 | 说明 |
|---|---|
| [src/TipCL.App/Themes/Palette.Light.axaml](src/TipCL.App/Themes/Palette.Light.axaml) / [Palette.Dark.axaml](src/TipCL.App/Themes/Palette.Dark.axaml) | 本包新增:两份色板(61 键,键序/取值照 WPF 源) |
| [src/TipCL.App/Themes/Palette.cs](src/TipCL.App/Themes/Palette.cs) | 本包新增:色板安装 + 按键查表 + 兜底色 + 图标字体入口 |
| [src/TipCL.App/Services/Lucide.cs](src/TipCL.App/Services/Lucide.cs) | 本包新增:Lucide 图标系统的 Avalonia 移植 |
| [src/TipCL.App/Services/LucideAssets.cs](src/TipCL.App/Services/LucideAssets.cs) | 字节级复制(143325 字节,SHA256 `1B93FADF…BB52`) |
| [src/TipCL.App/Services/Ico.cs](src/TipCL.App/Services/Ico.cs) | 字节级复制(2843 字节,SHA256 `7916C17E…A233`) |
| [src/TipCL.App/Resources/fonts/TipCLIcons.ttf](src/TipCL.App/Resources/fonts/TipCLIcons.ttf) / [LICENSE-FluentSystemIcons.txt](src/TipCL.App/Resources/fonts/LICENSE-FluentSystemIcons.txt) | 字节级复制(14232 / 1078 字节) |
| [src/TipCL.App/App.cs](src/TipCL.App/App.cs) / [MainWindow.cs](src/TipCL.App/MainWindow.cs) / [TipCL.App.csproj](src/TipCL.App/TipCL.App.csproj) | 本包修改:挂色板、删占位色、注册字体资源 + `InternalsVisibleTo` |
| [tests/TipCL.Tests/C1aFixtures.cs](tests/TipCL.Tests/C1aFixtures.cs) | 本包新增:测试夹具(钉死的键表/取值/字体哈希 + headless Avalonia 引导 + ttf cmap/name 解析) |
| [tests/TipCL.Tests/C1aPaletteTests.cs](tests/TipCL.Tests/C1aPaletteTests.cs) / [C1aIconFontTests.cs](tests/TipCL.Tests/C1aIconFontTests.cs) / [C1aLucideTests.cs](tests/TipCL.Tests/C1aLucideTests.cs) | 本包新增:104 条用例的主体 |
| [tests/TipCL.Tests/Ported/IconFontTests.cs](tests/TipCL.Tests/Ported/IconFontTests.cs) | 恢复编译:A6 的 B 类路径适配 3 处,断言未改 |
| [tests/TipCL.Tests/TipCL.Tests.csproj](tests/TipCL.Tests/TipCL.Tests.csproj) | 本包修改:加 `TipCL.App` 工程引用;去掉 `IconFontTests.cs` 的 `Compile Remove` |

### `--smoke` 的字节与哈希(颜色换成真色板后**应当**变,旧值作废)

| 帧 | 字节 | SHA256 | 角落像素 |
|---|---|---|---|
| `--smoke --theme light` | **3713**(原 3739) | `D4F53678CF9314B9F3A69B42F778F6A8FF54ADE8E8F0FEB086C4EE00355A3037` | `#F5F5F5` = `B.Canvas` 浅色 |
| `--smoke --theme dark` | **3669**(原 3751) | `CFD798FF7B4512D99251F916D0FA72C6F15E5D427ACA1EBB25F77D9C0837FBA3` | `#161616` = `B.Canvas` 深色 |

两帧哈希**不同** ✓;像素抽查也确认真的是色板里的颜色(不是同图换名)。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build-server shutdown` 然后 `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;警告合计 **7**,去重后只有两类:`NU1900`(本机 huaweicloud 镜像不可达的环境噪声)与 `SYSLIB0014` ×1(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs(116,38)`,本包未动)。没有新增编译器警告 |
| 2 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 **0**,通过 **1900** / 失败 0 / 跳过 0(共 1900) |
| 3 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll`(进程内通道) | 退出码 **0**,总计 **1900**,通过 1900 / 失败 0 / 跳过 0 / **不支持 0** |
| 4 | `cmd /c "src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke --theme light --out artifacts\c1a-light.png"` | 退出码 **0**,**3713** 字节 |
| 5 | 同上 `--theme dark --out artifacts\c1a-dark.png` | 退出码 **0**,**3669** 字节;两张哈希不同(见上表) |
| 6 | `--smoke --page home --theme dark` | 退出码 **0**(仍只解析并保存 page id,不切页) |
| 7 | `--smoke --theme blue` | 退出码 **2**(stderr 打中文原因 + 用法) |
| 8 | `%LOCALAPPDATA%\TipCL` 开工时 / 全部跑完 | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,前后一致 |
| 9 | 残留进程 `testhost` / `TipCL` / `java` / `javaw` / `adb` | 全 **0** |
| 10 | `%TEMP%` 下 `tipcl*` 目录个数 | **89 → 89**(最近的两个 `tipcl-r9-smoke`、`tipcl-r9-smoke2` 建在 00:07/00:15,早于本包开工的 06:53,是别的会话留下的;本包窗口内没有新名字) |
| 11 | `src\TipCL.Core` 下 `.cs` 对 Windows 源比 SHA256 | 总数 **248** / 有对应文件 **238** / 一致 **216** / 有改动 **22** / Windows 源里没有对应文件 **10** —— 与 Lead 的上一组数字逐项一致(本包没碰 Core) |
| 12 | `git status --short`(提交前) | 只有本包自己新增/修改的源码、测试与文档;没有 `bin` / `obj` / `artifacts` / `dist` / PNG / `test-home` |

### 新增测试(104 条,1796 → 1900)

| 文件 | 条数 | 覆盖 |
|---|---|---|
| C1aPaletteTests.cs | 26 | 两份 axaml 可解析为 XML、键集合与键序一致、61 键(7 颜色 + 54 刷子)、**逐项等于 WPF 源(钉死的 61 条键值表)**、颜色都是 `#RRGGBB`/`#AARRGGBB`、`Opacity` 落在 0..1、渐变 2~3 个停靠点且偏移升序、渐变端点用百分比、源码里按字符串查的 `B.*` / `C.*` 键都存在(非空性断言:至少扫到 B.Canvas/B.Ink/B.InkMuted/B.AccentDeep)、`Palette.Keys` 与 axaml 键序一致;Avalonia 层:61 键在两个变体下全部解析成功、`C.*` 是 Color 对象、刷子颜色与不透明度、八位 ARGB 透明确实透明确(浅色 `#00FFFFFF`、深色 `#0FFFFFFF`)、渐变刷 3 个停靠点、未知键(`B.Nope` / `C.Nope` / `B.` / 空串 / `not-a-key`)返回兜底灰不抛、明暗同键取值不同、`Palette.Current` 跟随 `Application.RequestedThemeVariant`(改完在 finally 里还原) |
| C1aIconFontTests.cs | 31 | ttf 在位且非空、**SHA256 与 Windows 源一致**、LICENSE 在位且哈希一致、name 表家族名 `TipCL Icons`、cmap 非空且覆盖私有区、**22 个采样码位逐个 Theory 断言在字体里**、字体以 `avares://TipCL/Resources/fonts/TipCLIcons.ttf` 嵌进去且字节与仓库文件一致、Avalonia FontManager 能按该族名拿到字形、URI 形态与家族名一致、字体目录里只有这两个文件 |
| C1aLucideTests.cs | 46 | 15 个 path data 常量逐个 Theory:能 `Geometry.Parse` 且 bounds 非零、常量个数 15、**每个常量与 Windows 源逐字节相同(从 WPF 源里抽字面量对比)**、描边/尺寸缩放(`size/24` 烘进几何、`StrokeThickness = 2×s`)、24 时不缩放、刷子键生效与未知键兜底、旋转原点居中、Spinner 是 LoaderCircle + RotateTransform + 默认 `B.AccentDeep`、Rotate 关动画时直接落位(90°/0°,改完开关在 finally 里还原)、无 RotateTransform 时自动建、null 不抛、开动画不抛、Shimmer 关时回到纯色刷、开时是 5 段渐变、关动画时不出渐变、ToolIcon 的已知工具名(Theory ×4)/ MCP 前缀 / 未知与 null 返回 null、`IsMcp` 与 WPF 判定一致、`LucideAssets` 非空且无空值、**每个内嵌资源都是合法 PNG 且 IHDR 宽高非零(只报工具名与计数,不打印 base64)**、工具名集合与 Windows 源一致、`Asset` 是 Rectangle + 调色板填充 + ImageBrush 遮罩(Bitmap 128×128)、未知工具退回 Spinner、重复调用复用同一张位图、Ico.cs 覆盖 22 个采样码位、移植后的 Lucide.cs 不引入 `TipCL.Pages` / `System.Windows`、`LucideAssets.cs` 与 Windows 源 SHA256 一致 |
| Ported/IconFontTests.cs | 1 | 恢复编译的既有用例:扫 `src\TipCL.App` + `src\TipCL.Core` 的 `.cs`/`.xaml`,每个 `\uExxx`/`\uFxxx` 码位与 PUA 字符都必须在字体里 |

用例里的期望值都在注释里标了来源:**Windows 源**(逐项钉进 `C1aFixtures`)还是**本机实测**(Skia headless 下解析出来的结果)。
`D:\项目\tipcl\windos\...` 那条路径可用时会额外跑 6 条交叉复核(不可用则打印一行原因后返回,不判失败;本机这次**是真跑的**)。
测试不起进程、不联网、不写任何临时文件(唯一的落盘位置是进程内执行器与测试自身的 `TestHome.Root` 隔离目录)。

### 未验证(本机是 Windows,没有 Mac)

1. **mac 上 `avares://…/TipCLIcons.ttf#TipCL Icons` 能不能命中字体**:本机(Windows + Avalonia headless + Skia)
   实测家族名能解析、字形能取到;macOS 的字体后端是 CoreText(Avalonia.Native),没验过,家族名里那个空格在 mac 上要不要转义也没验。
2. **mac 真机渲染出来的色板/图标长什么样**:只验到 Windows 上的 Skia 离屏像素与几何 bounds。
3. **动画的实际观感**(转圈、微光、箭头旋转):本包只在有 `ApplicationLifetime` 时启动动画,本机没有在 mac 上跑过窗口,动画的视觉效果未验证。
4. **mac 的"减弱动态效果"没有对应实现**:WPF 的 `Motion.Enabled` 读的是 `SystemParameters.ClientAreaAnimation` + 远程会话;
   mac 版现在是 `Lucide.MotionEnabled` 静态开关(默认 true),接 C2 的 Motion 门面时再补。
5. **微光的渐变相对变换**:WPF 用 `RelativeTransform = TransformGroup{ScaleTransform(3,1), TranslateTransform}`,
   Avalonia 用 `Brush.Transform` + 默认 `TransformOrigin`;坐标系是否逐像素等价没验。
6. **Retina 多倍缩放下的图标锐度**没验。

### 遗留与风险

1. **主题热切换**:`Palette.Brush` 是"创建时按当前变体解析一次",已有控件不会跟着换主题(Windows 版是 WPF 动态资源)。
   目前 mac 版只有启动期 `--theme` 决定主题,窗口自己监听 `ActualThemeVariantChanged`,所以还看不出来;
   C2 接主题热切换时要么改用 `{DynamicResource}` 绑定、要么订阅变体重建控件(见 D-68 末条)。
2. **`Motion.cs` 未移植**(属 C2 的 Services):Lucide 自带了最小动画实现 + `MotionEnabled` 静态开关;
   C2 应该把它换成正式的门面(D-70 第 2 条),届时"动画开关"要与 mac 的减弱动态效果对上。
3. **`Services\Ico.cs` 的归属待 Lead 裁决**:它不在 C1a 的编号清单里,但它是"图标字体要替代的那张字形表",
   本包把它一并字节级复制了(无依赖,随时可以搬去 C1c)—— 见 D-70 第 5 条。
4. **图标字体只有 69 个码位**(Ico.cs 用掉 67 个):以后加图标必须同时补字体,否则 `Ported\IconFontTests` 会红(这正是它的意义)。
5. **测试进程里起了 headless Avalonia**,并且 Avalonia 会把当前线程的 `SynchronizationContext` 换掉:
   `C1aFixtures.EnsureAvalonia()` 已经在 `finally` 里还原(不还原会把进程内执行器卡死,详见 D-69 末条)。
   以后再加 Avalonia 初始化一定要照做。
6. **没有做**的事:`Themes\Controls.xaml`(C1b)、`Services\Ui.cs` / `UiExt.cs`(C1c)、任何页面(C2+)、
   壁纸与 `PremiumAssets` 的内嵌(它们属于页面/C2);没有改 TFM、没有扩 `NoWarn`、没有新增警告、没有联网请求。

---

## C1b `Themes/Controls.axaml` 移植(控件样式表)

### 做了什么

1. **`src/TipCL.App/Themes/Controls.axaml`**(新建,约 640 行):Windows 版 `Themes\Controls.xaml`(1055 行、64 个 `x:Key`)
   逐键移植。根节点是 `<Styles>`,令牌与 ControlTheme 落在 `Styles.Resources`,选择器写在根下;
   每条类选择器上方都有一条固定的对应注释 `<!-- WPF键 -> .类名 -->`(测试逐条核对)。
2. **`src/TipCL.App/Themes/StyleKeys.cs`**(新建):WPF 样式键 -> Avalonia 类名的**唯一**映射表,
   四张表铺满 64 个键:`Table`(40 个具名样式键)、`Templates`(1 个 ControlTemplate 键 BtnTpl)、
   `ImplicitTypes`(13 个隐式类型样式)、`ResourceKeys`(9 个资源令牌)+ `Unsupported`(GroupBox,1 个)。
   另给 C1c 备了 `Map` / `Classes` / `Apply` / `Split`。
3. **`src/TipCL.App/App.cs`**:在 FluentTheme 与色板之后 `Styles.Add(new StyleInclude(...))` 挂上样式表
   (晚加入的 Styles 优先级更高,同名属性上我们的样式胜出)。
4. **类名规则**(任务书定的):点换连字符 + 驼峰处插连字符 + 全小写,例如
   `Btn.Primary -> btn-primary`、`T.PageTitle -> t-page-title`、`ListItemRow -> list-item-row`。
   隐式类型样式(`x:Key="{x:Type X}"`)不产生类名,直接变成类型选择器。
5. 每个变体类**自带** `Btn.Base` 的模板(类样式里设 `Theme`),等价 WPF 的 `BasedOn`;
   所以 C1c 只加变体类即可,不用再加 `btn-base`。

### WPF 键 -> Avalonia 类名(完整表)

| WPF 键 | 类名 | 备注(Avalonia 侧) |
|---|---|---|
| `T.H1` | `t-h1` | 20px SemiBold |
| `T.PageTitle` | `t-page-title` | 21px ExtraBold |
| `T.GroupTitle` | `t-group-title` | 12.5px + Margin 4,0,0,6 |
| `T.H2` | `t-h2` | 17px SemiBold |
| `T.H3` | `t-h3` | 15px SemiBold |
| `T.Muted` | `t-muted` | 12px InkMuted + Wrap |
| `T.Small` | `t-small` | 11px InkFaint |
| `T.Mono` | `t-mono` | F.Mono 12px InkSoft |
| `Card` | `card` | Border:Card 底 + CR.Card + Padding 12 |
| `Seg.Rail` | `seg-rail` | Border:SegRail 底 + 圆角 10 + Padding 3 |
| `ScrollThumb` | `scroll-thumb` | Thumb:Scroll 底 + 圆角 4 + Margin 3,2;悬停/拖动换 ScrollHover |
| `Btn.Base` | `btn-base` | 只用基模板(无变体色) |
| `Btn.Primary` | `btn-primary` | AccentStrong / OnAccentStrong / Bold |
| `Btn.Soft` | `btn-soft` | AccentSoft / AccentSoft2 / AccentText |
| `Btn.Normal` | `btn-normal` | Paper / Line / Ink |
| `Btn.Ghost` | `btn-ghost` | 全透明底 + InkSoft |
| `Btn.Danger` | `btn-danger` | DangerSoft / DangerSoft / Danger |
| `Btn.Icon` | `btn-icon` | Ghost 配色 + Padding 7 + MinWidth 30 |
| `Btn.Chip` | `btn-chip` | Paper2 / Line / InkSoft + Padding 11,5 + 12px |
| `Btn.Caption` | `btn-caption` | 46 宽窗口按钮 + 图标字体 |
| `Btn.CaptionClose` | `btn-caption-close` | 自带矢量 X + 悬停 CaptionHot |
| `Btn.TitleIcon` | `btn-title-icon` | 32x32 标题栏图标钮 |
| `Btn.TitleChip` | `btn-title-chip` | 复用 TitleIcon 模板,宽度自适应 |
| `Ico.Line` | `ico-line` | Path 16x16 / 线宽 1.7,Stroke 跟随祖先 Foreground |
| `Ico.Caption` | `ico-caption` | Path 10x10 / 线宽 1.1 |
| `TabItemBtn` | `tab-item-btn` | ToggleButton,选中下划线(Border#Un) |
| `Tab.Top` | `tab-top` | ToggleButton,13.5px Medium,选中 Bold + 底部条 |
| `Seg.Item` | `seg-item` | ToggleButton,选中 SegOn 白底 + Bold |
| `Input` | `input` | TextBox 全家共用模板(占位文字走 Tag) |
| `Input.Multi` | `input-multi` | + AcceptsReturn / Wrap / Top |
| `Input.Log` | `input-log` | + F.Mono 11.5 / 只读 / NoWrap / 双向滚动条 |
| `Pw` | `pw` | TextBox + PasswordChar(见差异 9) |
| `ComboItem` | `combo-item` | ComboBoxItem,悬停 Hover、选中 AccentSoft |
| `Combo` | `combo` | ComboBox 整份模板(含 Popup#PART_Popup) |
| `Chk` | `chk` | CheckBox:18x18 圆角 6 方框 + 勾 |
| `Switch` | `switch` | CheckBox:40x22 开关 |
| `Sld` | `sld` | Slider 整份模板(Track 部件写在模板里) |
| `Prog` | `prog` | ProgressBar 高度 6 / Accent / LineSoft |
| `ListItemRow` | `list-item-row` | ListBoxItem 行模板 |
| `PlainList` | `plain-list` | ListBox 去边框 + ItemContainerTheme=ListItemRow |

**没有类名的键**:`BtnTpl` -> ControlTheme `BtnBase`(唯一的模板键)、
`F.Icon` / `F.Ui` / `F.Mono` / `CR.Card` / `CR.Ctl` / `CR.Pill` / `D.Fast` / `D.Med` / `E.Out`(资源令牌,`x:Key` 同名)、
`GroupBox` -> `StyleKeys.Unsupported`(Avalonia 11 没有 GroupBox 控件,Windows 版也没有使用点)。

**隐式类型样式(13 个)** -> 类型选择器:`TextBlock`、`ScrollBar`、`CheckBox`、`RadioButton`、`ToggleButton`、
`ListBox`、`ListBoxItem`、`Label`、`Expander`、`TabItem`、`ToolTip`、`ContextMenu`、`MenuItem`。
另有一条 `Thumb` 的隐式样式(挂 ScrollThumb 模板),来源是 WPF 的 `ScrollThumb` 具名样式。

### 视觉差异(WPF 有、Avalonia 没有等价物的部分)

1. **隐式 TextBlock**:`TextOptions.TextFormattingMode` / `TextRenderingMode` / `SnapsToDevicePixels` 三个开关
   Avalonia 没有对应属性(它的渲染器不吃这些开关),没移植。
2. **按钮动画**:WPF 的 Storyboard 淡化改成 Avalonia `Transitions`(`DoubleTransition`,时长仍取 `D.Fast`);
   按下的 0.972 缩放是瞬时 `RenderTransform`(没有过渡);`E.Out` 目前没有使用点,等 C2 接动画。
3. **焦点环**:WPF `FocusVisualStyle=null` + 模板内 `Kf`(看 `IsKeyboardFocused`)-> Avalonia `FocusAdorner=null`
   + `:focus-visible`(语义与 WPF 一致:鼠标点击不显示)。
4. **线帽**:WPF `StrokeStartLineCap` + `StrokeEndLineCap` 在 Avalonia 合成一个 `StrokeLineCap`。
5. **ScrollBar**:WPF 的模板结构(Track + Decrease/IncreaseRepeatButton + Thumb)在 Avalonia 里没有等价物
   (Avalonia 的 ScrollBar 自己按名字找 4 个 RepeatButton 与那个 Track),所以保留 Fluent 的结构,
   只改宽度(10px)、压掉灰色轨道与两端箭头按钮(与 WPF 的 0 透明度空壳等效)、滑块改用色板灰。
   **悬停时 Fluent 可能露出箭头按钮,本机未验**。
6. **ScrollThumb 的拖动配色**:WPF 看 `Thumb.IsDragging`,Avalonia 用 `:pressed`。
7. **Combo**:Avalonia 的 ComboBox 自己处理点击,模板里不需要 WPF 那个 ToggleButton;Popup 的 `PopupAnimation=Fade`
   与 `AllowsTransparency` 没有对应属性(被丢掉);`DropShadowEffect` -> `Border.BoxShadow`;
   弹出层最小宽度改用 `$parent[ComboBox].Bounds.Width` 绑定。
8. **Prog**:保留 Fluent 的进度条模板(`PART_Indicator` 由框架找),只改高度/前景/底色/圆角;
   WPF 的 indeterminate「Glow」扫光条(70px 渐变 + 1.5s 循环)没有移植 —— Fluent 自带不确定态动画,观感不同。
9. **Pw**:Avalonia 11 **没有 PasswordBox**(反射确认),改用 `TextBox` + `PasswordChar`,与输入框共用模板。
10. **Chk / Switch**:动画改成伪类直接切值(无过渡);开关滑块的 `DropShadowEffect` -> `BoxShadow`;
    WPF 的 `ToggleButton.IsChecked` 触发 -> `:checked`。
11. **Sld**:Avalonia 的 `Track` **不是** TemplatedControl(没有 `Template` 属性),而且 Fluent 的 Slider 把 Thumb 的
    `20x20` 与系统强调蓝写成**局部值**(样式压不过),所以把 Slider 整份模板照 WPF 重画,三个部件用
    `<Track.Thumb>` / `<Track.DecreaseButton>` / `<Track.IncreaseButton>` 直接写在模板里;
    重画后必须显式把 `Orientation/Minimum/Maximum/Value` 接到 Track 上,否则 Track 一直是 `:vertical`、值不动(本机实测);
    悬停放大从 ScaleTransform 动画改成换尺寸。
12. **PlainList**:WPF 的 `VirtualizingPanel.IsVirtualizing` / `VirtualizationMode` / `ScrollUnit` 在 Avalonia 里
    没有对应设置(Avalonia 的列表本来就虚拟化 + 像素滚动);`ItemContainerStyle` -> `ItemContainerTheme`。
13. **ToolTip**:照 WPF 重画(Paper 底 + Line 边 + 圆角 8 + Padding 9,6),`DropShadowEffect` -> `BoxShadow`。
14. **ContextMenu**:没有重画外壳(Fluent 的结构复杂),只改前景色 —— 圆角/内边距/阴影按 Fluent 的,与 Windows 版不同。
15. **MenuItem**:只改字体/前景 + 布局根圆角与悬停底色(`MenuItem.IsHighlighted` -> `:pointerover` / `:selected`),
    图标/子菜单那一套按 Fluent 的。
16. **GroupBox**:Avalonia 11 没有该控件,未移植(Windows 版全仓库也没有使用点,grep 确认)。


### 新增/修改文件

- 新增 `src/TipCL.App/Themes/Controls.axaml`、`src/TipCL.App/Themes/StyleKeys.cs`
- 修改 `src/TipCL.App/App.cs`(只加挂载样式表那几行)
- 新增 `tests/TipCL.Tests/C1bControlsTests.cs`(XML 文本层 + 完整性 + 令牌 + WPF 残留检查)
- 新增 `tests/TipCL.Tests/C1bStyleKeysTests.cs`(类名映射表本身)
- 新增 `tests/TipCL.Tests/C1bRuntimeStylesTests.cs`(headless 运行期:选择器命中、模板部件、伪类)
- 没有改 `src/TipCL.Core/` 一个字节,没有改 `MainWindow.cs`,没有改任何既有测试。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `%LOCALAPPDATA%\TipCL` 开工前测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | **0 错误**;警告只有 `NU1900`(本机镜像超时,次数随重试浮动)与 `SYSLIB0014` **唯一一处**(`src/TipCL.Core/Launcher/Sync/PhoneClient.cs:116`,Windows 版原样的 `WebRequest.Create`) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 退出码 0,通过 **2157** / 失败 0 / 跳过 0(1900 + 本包 257) |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 退出码 0,总计 **2157**,通过 2157,失败 0,跳过 0,**不支持 0**;两条通道总数一致 |
| 5 | `--smoke --theme light --out artifacts/c1b-light.png` | 退出码 0,**3956 字节**,SHA256 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C` |
| 6 | `--smoke --theme dark --out artifacts/c1b-dark.png` | 退出码 0,**3941 字节**,SHA256 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E` |
| 7 | 与 C1a 基准(3713 / `D4F53678...`、3669 / `CFD798FF...`)对比 | **不相等**。原因已定位到唯一一处:隐式 TextBlock 样式的 `FontFamily`(见第 8 条) |
| 8 | 因果取证:临时注掉隐式 TextBlock 的 `FontFamily` 那一行后重跑 smoke | light **3713 字节 / `D4F53678CF9314B9F3A69B42F778F6A8FF54ADE8E8F0FEB086C4EE00355A3037`**、dark **3669 / `CFD798FF7B4512D99251F916D0FA72C6F15E5D427ACA1EBB25F77D9C0837FBA3`** —— 与 C1a 基准**逐字节一致**,随后已还原那一行 |
| 9 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与开工前完全一致 |
| 10 | 残留进程 `testhost` / `TipCL` / `java` / `adb` | 0 个 |
| 11 | `%TEMP%` 下 `tipcl*` 目录 | 共 89 个,按创建时间算**本次会话新建 0 个**(`tipcl-probe` 等属于别的会话) |
| 12 | `src\TipCL.Core` 对 Windows 源做 SHA256 比对(排除 `obj\`) | mac 侧 248 个 `.cs`:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线完全一致 |

**第 7/8 条的说明**:隐式 TextBlock 样式按 Windows 版移植了 `FontFamily=F.Ui`(`PingFang SC, Hiragino Sans GB,
Microsoft YaHei UI, sans-serif`,GOAL 的 mac 字体列表)。本机是 Windows,这个列表里第一个在本机存在的字体是
**Microsoft YaHei UI**,而 C1a 的 smoke 窗口里那句 `TipCL` 之前用的是 Avalonia 的 `$Default` 字体 —— 字形不同,
PNG 自然不同。这不是样式写错,也不是窗口外观被改(窗口仍然没有标题栏/导航/页面宿主),只是那一个 TextBlock 的
字体变了。**新基准(3956 / 3941)未经 Lead 裁决,本包没有改任何基准记录,请 Lead 定夺。**

### 未验证项

1. **macOS 真机**(Avalonia.Native / CoreText):`PingFang SC` / `Menlo` / 图标字体在 mac 上能不能命中、
   控件观感、动画观感 —— 本机是 Windows,一律未验证。
2. **真实鼠标/键盘交互**(悬停、按下、拖滑块、拖滚动条、点开下拉、Tab 焦点环):本包只验了伪类与值的计算,
   没有驱动真实指针事件(headless 不模拟)。
3. **主题热切换的视觉结果**:样式里对色板一律用 `{DynamicResource}`,天然支持热切换,但没有验运行时切换后的画面。
4. **`E.Out` 令牌**:当前没有任何使用点(等 C2 的动画门面)。
5. **`ScrollBar /template/ RepeatButton { Opacity=0 }` 在 Fluent 悬停态下是否被覆盖**:未验。

### 遗留与风险

1. **smoke 基准待 Lead 裁决**:新值 3956 / `9F93EF3E...`(light)、3941 / `F687E6CD...`(dark),
   原因是隐式 TextBlock 的字体(第 8 条已做因果取证)。要不要改基准请 Lead 定。
2. **Fluent 的模板仍有几处在用**:ContextMenu 外壳、Expander、TabItem、MenuItem 的图标/子菜单、
   ProgressBar 的不确定态动效 —— 这些的观感与 Windows 版不完全一致。
3. **C1c 的接线成本**:`Ui.cs` / `UiExt.cs` 里的 `Style="{StaticResource X}"` 都要换成类名(建议直接用
   `StyleKeys.Apply` / `StyleKeys.Classes`);`Pw` 的调用点要改成 `TextBox` + `PasswordChar`;
   标题栏那套(`Btn.Caption*` / `Btn.TitleIcon`)属 C2。
4. **探针结论已经写进实现**:Avalonia 不允许连续两个 `/template/`(运行期抛
   `ControlTemplate styles cannot contain multiple template selectors`),跨两层模板取部件必须重画外层模板;
   `Track` 不是 TemplatedControl;`TextBox` 模板必须有 `PART_ScrollViewer` + `PART_TextPresenter`(编译期强制)。
   探针文件(一次性的 `Zz*ProbeTests.cs`)已删除,不入库。
5. 测试里给 `Thumb` 挂了一条隐式样式(ScrollThumb 模板)—— 这会影响全应用的 Thumb(滚动条与滑块),
   这是有意的;C2 若引入别的 Thumb 用法需注意。

### Lead 验收(2026-11-13 09:20)

结论:**C1c 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 三次提交(`a6190b3` 基座 / `cfec58d` 输入与其余构建器 / `dd1c39c` 测试与文档) | 工作树干净;无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| **`Ui` 公开成员覆盖(Lead 独立对拍)** | Windows 39 个 vs mac 39 个;**缺失 0、多出 0** |
| **`UiExt` 公开成员覆盖(Lead 独立对拍)** | Windows 22 个 vs mac 22 个;**缺失 0、多出 0** |
| `SPanel` / `SmoothScroll` | 成员齐(我脚本报的 `SmoothScroll` 缺 `Register` 是**误报** —— 那是 WPF 的 `DependencyProperty.Register` 调用,不是成员;Windows 全树也没有 `SmoothScroll.Register` 的调用点) |
| 偏差表 | 已在 C1c 小节按「公开成员里有必要改动的全部」逐条列出(`UIElement`→`Control`、`Visibility`→`IsVisible`、`Pw` 返回 `TextBox`、`RowCard` 事件类型、`VirtualList<T>` 参数类型等),并有「降级与观感差异」单列一节 —— 没有静默丢 |
| 红线文件(`src\TipCL.Core`、测试隔离三件、`Ported\`、`AvaloniaSession.cs`、`Themes\`、`MainWindow.cs`、`Smoke\`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| 新测试是否按 C1b-fix 的纪律走会话 | `C1cUiTests.cs` 与 `C1cUiInputTests.cs` 都调用 `AvaloniaSession`/`C1aFixtures`;没有第二处 `SetupWithoutStarting` |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` ×4 + `SYSLIB0014` ×1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0**;总计 **2257**(2157 → 2257,本包 +100) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2257 / 失败 0 / 不支持 0** |
| `--smoke` light / dark | 3956 字节 `9F93EF3E…` / 3941 字节 `F687E6CD…`,**与裁决基准逐位一致** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;0 个;0 个 |

**遗留(交给 C2)**:`Skeleton` 的横扫与 `SmoothScroll` 的滚轮平滑目前是自带的最小实现,`Motion` 门面落地后要改成走 Motion(D-82 已记);`Pw` 的调用点要按 `TextBox` 处理。

**C1 完成**:C1a(色板/图标字体/Lucide)、C1b(Controls.axaml + 类名映射)、C1b-fix(测试会话)、C1c(Ui/UiExt 门面)。下一步 C2(外壳)。

---

### Lead 验收(2026-11-13 08:35)——产品代码通过,**测试基建打回**

以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

**产品代码:通过。**

| 检查 | 结果 |
|---|---|
| `src\TipCL.Core` 下 `.cs` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,**与基线完全相同** |
| 红线文件(`MainWindow.cs`、`Smoke\`、测试隔离三件、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| 已有测试被改动的 | 0 个 |
| `Controls.axaml` 的 WPF 残留扫描 | 只有三处命中,逐一确认**都不是残留**:第 2 行的 `xmlns:x` 声明(Avalonia 也用同一个 x 命名空间)、第 105 行的 `ControlTheme ... BasedOn`(Avalonia 11 的正常写法)、第 747 行注释里提到的 `x:Key="{x:Type X}"` |
| 结构计数 | 112 个 `<Style>` + 16 个 `<ControlTheme>`,每个 Style 都有 `Selector` |
| **WPF 键覆盖率(Lead 独立核对)** | Windows 源 64 个 `x:Key` 在 `StyleKeys.cs` 里 **0 个缺失**;唯一 `Unsupported` 是 `GroupBox`(理由:Avalonia 11 无该控件,Windows 侧也无使用点) |
| **色板引用校验(Lead 独立核对)** | `Controls.axaml` 里 26 个去重后的 `{DynamicResource}` 键,**在浅色与深色色板里都存在**(两边各 0 缺失)—— 没有键名写错 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` ×4 + `SYSLIB0014` ×1 |
| `--smoke --theme light` / `dark` | 退出码 0,**3956 字节 `9F93EF3E…`** / **3941 字节 `F687E6CD…`**,与子代理报告逐位一致;与 C1a 基准**不同**(原因见下) |
| 截图像素 | 900×550;左上角仍 `#F5F5F5` / `#161616`,色板未受影响 |
| `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与基线一致 |
| 残留进程 / `%TEMP%` 新目录 | 0 个 / 无 |
| 红线 | 无 remote、无 tag、全局 git identity 未设 |

**裁决 1:接受新的 `--smoke` 基准 3956 / 3941。** Windows 版的隐式 `TextBlock` 样式本来就设置 `F.Ui`,照实移植后主窗口标题文字换了字体,渲染像素必然变化。子代理做了因果取证(临时注掉那行 `FontFamily` 后逐字节回到 C1a 基准),没有为了「让基准不变」而删掉那行 —— 这是对的做法。**新基准从此刻起生效**:light 3956 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C`,dark 3941 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E`。

**裁决 2:测试基建打回 —— `dotnet test` 通道 65 个失败。** 子代理报告「两通道一致 2157/2157」,与事实不符:

- Lead 复跑 `dotnet test TipCL.Mac.sln -c Release --no-build` → **退出码 1,通过 2092 / 失败 65 / 总计 2157**;进程内执行器仍是 2157/2157 全绿。
- 65 个失败全是同一个异常:`System.InvalidOperationException : Call from invalid thread`(`AvaloniaObject..ctor` → `Dispatcher.VerifyAccess`)。分布:56 个 `C1bRuntimeStylesTests`、7 个 `C1aPaletteTests`、2 个 `C1bStyleKeysTests`。
- **根因(Lead 定位)**:`C1aFixtures.EnsureAvalonia()` 里的 `SetupWithoutStarting()` 把 Avalonia 的 `Dispatcher.UIThread` 绑在**第一个调用它的线程**上;而 xunit v2 每个测试类各跑在自己的 worker 线程上,其它类里构造 Avalonia 控件就必然抛 `VerifyAccess`。C1a 只做几何解析(不查 Dispatcher)所以看起来是绿的;C1b 一加控件构造就暴露了。C1a 的 7 条色板用例之所以也红,是因为「哪个类先抢到 Dispatcher 线程」取决于 xunit 的调度顺序。
- 处理:派 **C1b-fix** 修复(专用 Dispatcher 线程会话,所有碰 Avalonia 的用例体经它执行),要求**两个通道各连跑两次**都是 0 失败才收尾。

**给后续所有包的运营规则(务必遵守)**:测试里一切 Avalonia 工作(构造控件/刷子、加载样式、`Geometry.Parse`、`FontManager`)**必须**经统一会话在 Dispatcher 线程上执行;单通道绿不足以收尾,必须 `dotnet test` 与进程内执行器**两通道都绿**。

### Lead 验收(2026-11-13 08:50)——C1b 与 C1b-fix 一并通过

`C1b-fix`(`d1cfe68`)修好后,Lead 独立复跑:

| 检查 | 结果 |
|---|---|
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` ×4 + `SYSLIB0014` ×1 |
| `dotnet test` 第 1 次 / 第 2 次 | 都是**退出码 0,失败 0 / 通过 2157 / 总计 2157** |
| 进程内执行器 第 1 次 / 第 2 次 | 都是**退出码 0,通过 2157 / 失败 0 / 不支持 0** |
| `--smoke` light / dark | 3956 字节 `9F93EF3E…` / 3941 字节 `F687E6CD…`,**与裁决基准逐位一致**(修复没动样式) |
| `git diff --name-status 76d8888 HEAD -- src` | **空** —— 修复只动测试工程 |
| **断言未被改弱(Lead 独立逐文件比对)** | 6 个被改文件里 `Assert.` 与 `[Fact]/[Theory]` 数量改动前后**逐文件完全相等**(如 C1bRuntimeStyles 122→122、35→35),新增 `Skip` 0 处 |
| `SetupWithoutStarting` 的出现次数 | 真正调用只有 `AvaloniaSession.cs:104` 一处;`C1aFixtures.cs` 的 2 处命中都在文档注释里,不是调用 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;0 个;0 个 |
| 红线 | 无 remote、无 tag、全局 git identity 未设 |

**Lead 对自己诊断的更正(子代理的补充是对的,记下来以免以后误判)**:

- 65 **不是**固定数字。子代理连跑 3 次得到 **93 / 0 / 95** 个失败,失败分布随「哪个类先抢到 Dispatcher 线程」变化(它见到 56+30+7,我见到 56+7+2)。也就是说这个故障**时绿时红**,单次绿不能证明健康。
- 「C1a 只做几何解析所以看起来是绿的」只对一半:`Geometry.Parse` 确实不查 Dispatcher,但 `C1aLucideTests` 也在构造控件 —— 它抢到线程就绿,抢不到一次红 30 条。
- 更关键的一条:**进程内执行器结构上不可能复现这个故障**(它所有用例都在主线程上跑、平台也起在主线程上),所以「两通道一致」在这类问题上不提供任何信息。这条已写进上面的运营规则:碰 Avalonia 的用例必须走会话,两通道都要绿。

**结论**:C1b 的产品代码(C1a 键覆盖、色板引用、无 WPF 残留)与 C1b-fix 的测试基建**都通过**。C1b 收尾。

---

## C1b-fix 测试基建修复:专用 Dispatcher 线程会话(2026-11-13)

本包**只改测试工程**,`src\` 一个字节没动,没有删/弱化任何断言,没有加 `Skip`,没有扩 `NoWarn`,没有动 C1b 样式表内容。

### 症状(Lead 报的,本机复现一致)

`dotnet test TipCL.Mac.sln -c Release --no-build` 退出码 1,失败 65(Lead)/ **93、95(本机三次实跑)**,
失败用例全是同一个异常:

```
System.InvalidOperationException : Call from invalid thread
   at Avalonia.Threading.Dispatcher.<VerifyAccess>g__ThrowVerifyAccess|16_0()
   at Avalonia.Threading.Dispatcher.VerifyAccess()
   at Avalonia.AvaloniaObject.VerifyAccess()
   at Avalonia.Controls.Button..ctor()
   at TipCL.Tests.C1bRuntimeStylesTests.Button_variant_classes_use_palette_colours() ... C1bRuntimeStylesTests.cs(281,0)
```

**分布随「谁抢到 Dispatcher 线程」变**:本机测到 `C1bRuntimeStylesTests` 56 + `C1aLucideTests` 30 + `C1aPaletteTests` 7 = 93,
另一次 95;Lead 那次是 `C1bRuntimeStylesTests` 56 + `C1aPaletteTests` 7 + `C1bStyleKeysTests` 2 = 65。
**同一份代码、同一条命令,3 次连跑的结果是 93 失败 / 0 失败 / 95 失败** —— 这就是上一包「两通道一致 2157/2157」的错误来源。

### 根因(复核了 Lead 的诊断,并补齐了它没说的那一层)

1. `AppBuilder.SetupWithoutStarting()` 把 `Dispatcher.UIThread` 绑在**第一个调用它的线程**上,
   而 `AvaloniaObject.VerifyAccess()` 只认那一个线程。
2. xunit v2 走 VSTest(testhost)通道时**每个测试类跑在自己的 worker 线程上**,所以「哪个类先碰到
   Avalonia,哪个类就独占 Dispatcher 线程」,其它类里构造控件必抛 `VerifyAccess`。
3. Lead 说「C1a 只做几何解析(不查 Dispatcher)所以看起来是绿的」——**这一句只对了一半**:
   几何/资源字典/字体这些**确实**要平台,而且 `Geometry.Parse` 本身不查 Dispatcher;
   真正决定成败的是「用例体所在的线程是不是 Dispatcher 线程」。所以 C1a 的 `C1aLucideTests`
   (24 处 `Geometry.Parse` + 控件构造)在**它抢到 Dispatcher 线程时**就绿,抢不到就红 ——
   本机亲眼见到它一次性红 30 条,正是这一层。
4. 进程内执行器(`Program.Main`)全程在主线程上同步跑,平台也起在主线程上,
   所以**它永远绿** —— 不是它更严,而是它根本没撞上这个问题。

### 设计(照任务书实现,未改方案)

新增 `tests/TipCL.Tests/AvaloniaSession.cs`:

- 一个**后台线程**(`IsBackground = true`,名字 `TipCL-Avalonia-Session`)在 `ThreadMain` 里
  `AppBuilder.Configure<TipCL.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();`,
  然后 `Ready.Set()` 并进泵循环;起平台失败会把异常带回 `Ensure()` 的调用方。
- 泵循环:`Queue.TryTake(timeout 2ms)` 取一个工作项执行 -> `Dispatcher.UIThread.RunJobs()` -> 循环
  (空闲时也每 2ms 跑一次 Dispatcher 的排队工作,布局/绑定/样式/渲染定时器回调都靠它)。
- `Ensure()`:幂等、锁保护,等 `Ready`(上限 30s),起平台时的异常原样抛出。
- `Run<T>(Func<T>)` / `Run(Action)`:把用例体投到会话线程,用 `ManualResetEventSlim` 同步等结果
  (上限 30s,超时抛 `TimeoutException`,消息写明是会话超时);异常经
  `ExceptionDispatchInfo.Capture(ex).Throw()` 在调用线程**原样重抛**(类型/消息/原始栈都保留)。
- **不引新依赖**:没有 `Avalonia.Headless.XUnit`(进程内执行器不展开 collection/fixture,见 D-05/A6),
  也没有依赖 `IClassFixture`/`ICollectionFixture` —— 纯静态入口,直接反射调用照样工作。
- `C1aFixtures.EnsureAvalonia()` 退化成 `AvaloniaSession.Ensure()` 的转发(只为保留 `Hosted.Show` 等老调用点),
  `SetupWithoutStarting()` 从此在整个测试工程里**只有一处**调用(已 grep 确认)。
- 平台整个挪到会话线程后,调用线程的 `SynchronizationContext` 一个字都不用动
  (老写法那段「存下来再还原」的代码随之删除,连带删掉两行不再使用的 `using`)。

### 改了哪些文件

| 文件 | 改动 |
|---|---|
| `tests/TipCL.Tests/AvaloniaSession.cs` | **新增**(会话线程 + 泵循环 + `Run`) |
| `tests/TipCL.Tests/C1aFixtures.cs` | `EnsureAvalonia()` 改成转发;删掉本线程起平台的老代码与两行无用 `using`;注释写明历史与原因 |
| `tests/TipCL.Tests/C1aIconFontTests.cs` | 2 条(`AssetLoader` / `FontManager`)经会话 |
| `tests/TipCL.Tests/C1aLucideTests.cs` | 24 条(全部 `Geometry.Parse` / 控件构造 / Transform / Brush / Asset)经会话 |
| `tests/TipCL.Tests/C1aPaletteTests.cs` | 9 条(色板解析、刷子、渐变、兜底色、变体跟随)经会话 |
| `tests/TipCL.Tests/C1bRuntimeStylesTests.cs` | 全部 38 条经会话(它们的用例体都在构造控件/开窗口/RunJobs);`Hosted.Show`/`Dispose` 因此与用例体在同一线程上 |
| `tests/TipCL.Tests/C1bStyleKeysTests.cs` | 2 条(唯一构造 `Button` 的两条)经会话;其余 15 条是纯表检查,**不**经会话(不需要平台就不会被线程问题影响) |

`Hosted`、`ByName`、`ColourOf`、`Pseudo` 这些辅助逻辑一行未改,只是现在都在会话线程上被调用。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | 修复前 `dotnet test` **3 次连跑** | 退出码 1 / 0 / 1,失败 **93 / 0 / 95**(重现了 Lead 的 65 那类失败,只是分布不同) |
| 3 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;警告只有 `NU1900`(镜像超时,6 条)与 `SYSLIB0014` **唯一一处**(`src/TipCL.Core/Launcher/Sync/PhoneClient.cs:116`) |
| 4 | `dotnet test TipCL.Mac.sln -c Release --no-build` **第 1 次** | 退出码 **0**:`Passed!  - Failed:     0, Passed:  2157, Skipped:     0, Total:  2157, Duration: 1 s - TipCL.Tests.dll (net8.0)` |
| 5 | 同上 **第 2 次** | 退出码 **0**,同上逐字一致(总计 2157,失败 0) |
| 6 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` **第 1 次** | 退出码 **0**:`总计 2157 个用例,通过 2157,失败 0,跳过 0,不支持 0`,末尾 `in-proc 测试全绿` |
| 7 | 同上 **第 2 次** | 退出码 **0**,同上逐字一致;**不支持 0**;总数与 `dotnet test` 的 2157 相等 |
| 8 | `dotnet test` **追加连跑 12 次**(抗抖动证据) | **12 / 12 全绿**,每次都是 `Failed: 0, Passed: 2157, Total: 2157`(修复前 3 次里有 2 次红) |
| 9 | `--smoke --theme light --out artifacts/c1bfix-light.png` | 退出码 0,**3956 字节**,SHA256 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C` —— **与裁决 1 的基准逐位相等** |
| 10 | `--smoke --theme dark --out artifacts/c1bfix-dark.png` | 退出码 0,**3941 字节**,SHA256 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E` —— **与裁决 1 的基准逐位相等** |
| 11 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**与开工前完全一致** |
| 12 | 残留进程 `testhost` / `TipCL` / `java` / `adb` | **0 / 0 / 0 / 0** |
| 13 | `%TEMP%` 下 `tipcl*` 目录 | 本次会话(08:25 之后)新建 **0 个**(现存 95 个全是 2026-11-07..11-13 00:15 别的包/会话留下的;`tipcl-probe` 也不是本包的) |
| 14 | `src\TipCL.Core\*.cs` 对 Windows 源 SHA256(排除 `obj\`/`bin\`) | mac 侧 248 个:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线**完全一致** |
| 15 | `git diff --stat -- src` | **空**(`src\` 无任何改动) |

### 为什么上一包「两通道一致 2157/2157」是错的(直说)

上一包**只看到了一次结果就当成结论**。这条失败路径**取决于线程调度**:同一个命令连跑 3 次能得到
93 红 / 0 红 / 95 红。上一包看到的两次绿灯很可能是调度运气,而不是「基线本来就是绿的」;
它也没有意识到进程内执行器**结构上不可能**复现这个问题(它全程在主线程上跑,平台也起在主线程上),
所以「两条通道一致」这句话在那次报告里根本没有信息量。
本包因此把**抗抖动**当成验收条件的一部分:除了任务书要求的各连跑两次,还额外把 `dotnet test` 连跑了
12 次(全绿),并且先在前一版代码上复现出红,再证明修复后不再红。

### 未验证项

1. **修复后的 `dotnet test` 只在 Windows 上、只有本机这一个 VSTest 组合上验过**;Lead 机器上的
   `testhost` 调度与线程池参数未必与这里相同 —— 但本设计的成立条件与调度无关:平台线程 == 用例线程。
2. **会话线程的超时分支(30s)没有真跑过**:没有刻意制造死锁来验证那条报错文案与退出行为。
3. **`AvaloniaSession` 的泵循环在空闲时不回收**:进程退出靠 `IsBackground = true`,没有优雅收摊
   (队列从不 `CompleteAdding`)。测试进程本来就是一次性进程,判定为可接受。
4. **用例体必须同步**这条纪律靠约定:**如果将来有 Avalonia 用例体是 `async` 且把续体投回
   Dispatcher,会在会话线程上自锁**,30 秒后报会话超时。本项目的 Avalonia 用例全是同步的。
5. macOS 真机(见 C1b 小节的未验证项)与本次修复无关,仍未验证。

### 遗留与风险

1. **`C1aFixtures.EnsureAvalonia()` 这个老名字保留了**,现在只是转发。老包的调用点很多,
   改名会让 diff 变大;后续包如果愿意,可以统一改成 `AvaloniaSession.Run(...)`,但**不要**再让
   任何一处自己起平台。
2. **新增的 9 处会话切换**会带来每条约 1~3ms 的线程往返开销(2157 条用例的总时长仍在 1 秒级),
   没有做性能优化,也没有必要。
3. `AvaloniaSession.cs` 里的 `Run<T>` 与 `Run(Action)` 是**同一套实现**(后者转调前者),
   没有重复逻辑;`TimeoutMilliseconds` 是 `internal const`,便于将来调。
4. 探针/一次性脚本都在 `artifacts\`(git 忽略)下,`tests\TipCL.Tests` 里**没有**留任何 `Zz*ProbeTests.cs`
   或其它临时文件(已按任务书核对)。

---

## C1c `Services/Ui.cs` / `UiExt.cs` 门面移植(Avalonia 11)

本包**只做门面**:没有建任何页面(C2+),没有改 C1b 的样式表内容一个字,没有改色板,没有改 `MainWindow.cs` /
`SmokeRunner`,没有改 `src\TipCL.Core\` 一个字节,没有改任何既有测试,没有删/弱化断言,没有加 `Skip`,
没有扩 `NoWarn`,没有引新 NuGet 包。

### 做了什么

1. **`src/TipCL.App/Services/Ui.cs`(新建)** —— Windows 版 `Services\Ui.cs`(693 行 / 约 40 个构建器)的门面。
   公开成员**一个不缺**:`S` `Res` `Txt` `H1` `H2` `H3` `Muted` `Small` `Mono` `Glyph` `V` `H` `G` `Card` `Pane`
   `WindowPctOf` `Scroll` `VirtualList<T>` `Sep` `Spring` `Btn` `IconBtn` `Input` `Multi` `LogBox` `Pw` `Combo`
   `Check` `Switch` `Sld` `Prog` `Tag` `Badge` `Field` `Section` `Empty` `Skeleton` `RowCard` `OpenUrl`
   + 嵌套的 `WindowPctConverter`。
2. **`src/TipCL.App/Services/UiExt.cs`(新建)** —— `Dyn` / 两种 `M` / `W` / `MinW` / `Hh` / `At` / `Add` /
   `Left` / `Right` / `Center` / `Stretch` / `VCenter` / `VTop` / `Wrap` / `Trim` / `Tip` / `Tag2` / `Hide` / `Show` /
   `Shown` / `Str` / `Fill`,方法名与参数默认值照抄。
3. **`src/TipCL.App/Services/SPanel.cs`(新建)** —— 量/排逻辑逐条对齐 WPF 那份(间距 = Spacing ×(可见子项数 − 1),
   隐藏子项不量不排不占间距);`Orientation` / `Spacing` 加了 `InvalidateMeasure()`(公开成员不变)。
4. **`src/TipCL.App/Services/SmoothScroll.cs`(新建)** —— `Offset` / `OffsetProperty` / `BindWindowMaxHeight`
   + 滚轮平滑(230ms 三次缓出)。
5. **字节级复制**(`Copy-Item`,SHA256 逐项校验相等):`Services\BtnKind.cs`、`Services\ToastKind.cs`;
   **`UiPrefs.cs`** 与 **`Clamp.cs`** 按 Windows 的位置放到 `src\TipCL.App\` 根(namespace `TipCL`)。
   `Clamp` 不在任务书清单里,但 `Ui.Sld` / `SmoothScroll` 依赖它(见 D-85)。
6. **WPF 机制 → mac 等价物**:`Style="{StaticResource X}"` → `StyleKeys.Apply(control, "X")`(加类名);
   `SetResourceReference` → `DynamicResourceExtension`(真动态资源,主题变体切换会重取);
   `Visibility` → `IsVisible`;`ToolTip` 属性 → `ToolTip.SetTip`;`Cursors.Hand` → `new Cursor(StandardCursorType.Hand)`;
   `AutomationProperties.SetName` 同名可用。
7. 明确**没有移植**的两处 WPF 专属:`VirtualList` 的 `ScrollViewer.SetCanContentScroll`(Avalonia 本来就虚拟化)
   与 `SmoothScroll` 的 `CanContentScroll` / `PanningMode`(见 D-82)。

### 新增/修改文件

| 文件 | 说明 |
|---|---|
| `src/TipCL.App/Services/Ui.cs` | **新增**(门面,768 行) |
| `src/TipCL.App/Services/UiExt.cs` | **新增** |
| `src/TipCL.App/Services/SPanel.cs` | **新增** |
| `src/TipCL.App/Services/SmoothScroll.cs` | **新增** |
| `src/TipCL.App/Services/BtnKind.cs` | **新增**(字节级复制) |
| `src/TipCL.App/Services/ToastKind.cs` | **新增**(字节级复制) |
| `src/TipCL.App/UiPrefs.cs` | **新增**(字节级复制) |
| `src/TipCL.App/Clamp.cs` | **新增**(字节级复制;见 D-85) |
| `tests/TipCL.Tests/C1cFixtures.cs` | **新增**(挂窗口的 `UiHost` + 期望值权威副本) |
| `tests/TipCL.Tests/C1cUiTests.cs` | **新增**(文本 / 图标 / 面板 / 栅格 / 卡片 / 按钮 / S·Res / 百分比 / 滚动 / `UiExt`) |
| `tests/TipCL.Tests/C1cUiInputTests.cs` | **新增**(输入控件 + Field/Section/Empty/Skeleton/RowCard) |

没有改 `MainWindow.cs`、`Smoke\`、`Themes\`(C1b 的 `Controls.axaml` / `StyleKeys.cs` / `Palette*` 一行未动)、
`App.cs`、`src\TipCL.Core\`、测试隔离三件、`Directory.Build.props`、`TipCL.Mac.sln`。

### WPF → Avalonia 差异表(公开成员里有必要改动的**全部**)

| Windows 公开面 | mac 侧 | 原因 / 等价物 |
|---|---|---|
| `Ui.S(string) : Style` | `Ui.S(string) : string`(**类名**) | Avalonia 没有"按键取 Style 挂控件"的机制;返回空壳 Style = 桩(见 D-80) |
| 全部方法的 `UIElement` / `FrameworkElement`(参数与返回) | `Control` | Avalonia 没有这两个类型;`Control` 是 L1 的对应物(D-70 同一口径) |
| `V(double, params UIElement?[])` / `H(...)` | `V(double, params Control?[])` | 同上(参数类型;名字/顺序/默认值不变) |
| `Spring() : FrameworkElement` | `Spring() : Control` | 同上 |
| `Btn(text, kind, RoutedEventHandler?, glyph)` | `Btn(text, kind, `**`EventHandler<RoutedEventArgs>?`**`, glyph)` | Avalonia 没有 `RoutedEventHandler`;lambda 调用点写法不变 |
| `IconBtn(..., RoutedEventHandler?, size)` | `IconBtn(..., `**`EventHandler<RoutedEventArgs>?`**`, size)` | 同上 |
| `RowCard(content, MouseButtonEventHandler?, padding)` | `RowCard(content, `**`EventHandler<PointerReleasedEventArgs>?`**`, padding)` | `MouseLeftButtonUp` → `PointerReleased`(内部判 `InitialPressMouseButton == Left`,语义等价) |
| `Pw(double) : PasswordBox` | `Pw(double) : `**`TextBox`** | Avalonia 11 没有 `PasswordBox`(D-75);掩码走 `PasswordChar` = `●`(U+25CF,WPF 默认值) |
| `Scroll(UIElement content, double) : SmoothScroll` | `Scroll(Control content, double) : SmoothScroll` | 参数类型 |
| `VirtualList<T>(Func<T, UIElement>, double) : ListBox` | `VirtualList<T>(Func<T, Control>, double) : ListBox` | 参数类型;**实现**从 WPF `FrameworkElementFactory`+`IValueConverter` → `FuncDataTemplate<T>`,`ItemContainerStyle` → `ItemContainerTheme`(签名不变) |
| `Card/Pane/Tag/Badge/Skeleton/RowCard` 的 `UIElement?` 参数 | `Control?` | 参数类型 |
| `Field(string, UIElement, string?, double)` / `Section(string, string?, UIElement?)` | `Field(string, Control, ...)` / `Section(string, string?, Control?)` | 参数类型 |
| `UiExt.At<T>(this T, ...) where T : UIElement` | `where T : Control` | 同上 |
| `UiExt.Dyn<T>(this T, DependencyProperty, string)` | `Dyn<T>(this T, AvaloniaProperty, string)` | `DependencyProperty` 在 Avalonia 是 `AvaloniaProperty`;内部用 `DynamicResourceExtension` |
| `UiExt.Hide/Show/Shown`(`Visibility`) | `IsVisible` | Avalonia 11 没有 `Visibility` 属性 |
| `UiExt.Shown(this UIElement) : bool` | `Shown(this Control) : bool` | 参数类型 |
| `SmoothScroll.Offset : double` | `new double Offset`(**同名隐藏基类**) | Avalonia 的 `ScrollViewer` 自带 `Vector Offset`(D-82) |
| `SmoothScroll.BindWindowMaxHeight(double, double)` | 签名不变 | 新增**internal** 可注入重载 `(Window? host, pct, floor)` 供单测(D-85) |
| `Ui.WindowPctOf(double, double)` | 签名不变 | 新增 **internal** 纯算术重载 `(pct, floor, hostHeight)`(D-85);宿主高度改读 `Bounds.Height`(D-82) |
| `Ui.GlyphFont`(internal 字段) | internal 字段 + 新增 internal const `GlyphFontUri` | 值 = `avares://TipCL/Resources/fonts/TipCLIcons.ttf#TipCL Icons, Segoe Fluent Icons, Segoe MDL2 Assets`(与 `Controls.axaml` 的 `F.Icon` 同一串) |
| `Check` / `Switch` 的 `changed` | 签名不变 | 内部接 `IsCheckedChanged`(WPF 的 `Checked`/`Unchecked` 在 Avalonia 11.3 已过时,CS0618;见 D-81) |

**新增的公开面**(Windows 没有):一个也没有(`GlyphFontUri` 是 internal)。

### 降级与观感差异(逐条列,不静默丢)

1. **Skeleton 的横扫动画**:WPF 用 `Motion.Loop`(无限循环);mac 侧 Motion 门面属 C2,改成
   "`Lucide.MotionEnabled` + 只在真生命周期下起 1150ms 无限动画"。**离屏(`--smoke`)与测试进程里不起动画**
   (否则收尾会被挂住,见 D-69),此时得到一块静态灰条(等价 WPF 关掉动画)。
2. **SmoothScroll 的滚轮平滑**:同上;`MotionEnabled` 关掉或没有生命周期时退回系统滚动。
   另:`CanContentScroll` / `PanningMode` 无对应属性(未移植,见 D-82)。
3. **滚轮步进换算**:WPF 每格 `Delta` 是 120、Avalonia 是 1.0,所以先 `× 120` 再乘 WPF 那 0.95。
   **这个换算没有在真机上验过滚轮一格的实际位移**(本机没有驱动真实滚轮)。
4. **`OpenUrl` 失败不弹 Toast**(宿主属 C2):只 `Debug.WriteLine`,不抛异常、不改状态(D-84)。
5. **`VirtualList` 的 `SetCanContentScroll(true)`**:Avalonia 没有对应设置(列表本来就虚拟化 + 像素滚动)。
6. **`RowCard` 只认左键**(`PointerReleased` + `InitialPressMouseButton`),与 WPF 的 `MouseLeftButtonUp` 等价。
7. **`Pw` 的掩码字符**由 WPF 默认的 `●` 显式写上(mac 侧是 `TextBox.PasswordChar`)。

### 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;7 条警告 = `NU1900`(镜像超时)+ `SYSLIB0014` **唯一一处**(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs:116`);**没有 CS/CA/IDE/xUnit 警告**(按警告码分组核过) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` **第 1 次** | 退出码 **0**:`已通过! - 失败: 0,通过: 2257,已跳过: 0,总计: 2257` |
| 4 | 同上 **第 2 次** | 退出码 **0**,同上逐字一致 |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` **第 1 次** | 退出码 **0**:`总计 2257 个用例,通过 2257,失败 0,跳过 0,不支持 0`,末尾 `in-proc 测试全绿` |
| 6 | 同上 **第 2 次** | 退出码 **0**,同上逐字一致;**不支持 0**;总数与 `dotnet test` 相等 |
| 7 | `dotnet test` **追加连跑 4 次**(抗抖动证据) | **4 / 4 全绿**,每次都是 `失败 0 / 通过 2257 / 总计 2257` |
| 8 | `--smoke --theme light --out artifacts/c1c-light.png` | 退出码 0,**3956 字节**,SHA256 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C`,**与裁决基准逐位相等** |
| 9 | `--smoke --theme dark --out artifacts/c1c-dark.png` | 退出码 0,**3941 字节**,SHA256 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E`,**与裁决基准逐位相等** |
| 10 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**与开工前完全一致** |
| 11 | 残留进程 `testhost` / `TipCL` / `java` / `adb` / `dotnet` | **0 / 0 / 0 / 0 / 0** |
| 12 | `%TEMP%` 下 `tipcl*` 目录 | 现存的 89 个全是此前会话留下的;**本包自己没建过**:收尾核账时出现过 1 个瞬时的 `tipcl-tests-22360-…`(09:15:24 建、59 秒后自行消失),随后做对照实验:本包单跑一次 `dotnet test` 前后 `tipcl*` 目录数 **89 → 89(0 新增)**。该前缀在本仓库 `src` / `tests` 里不存在(B2a 已记过同一条),同一时刻另有别的会话在跑 dotnet(本机实测 PID 9000 的命令行就是另一条产品线的 `--smoke`),判定不是本包的产物 |
| 13 | `git diff --name-status d1cfe68 HEAD -- src/TipCL.Core` | **空**;`git diff --name-status -- src/TipCL.Core` 也为空 |
| 14 | `src\TipCL.Core\*.cs` 对 Windows 源 SHA256(排除 `bin\`/`obj\`) | mac 侧 248 个:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线**完全一致** |
| 15 | 新增/改动文件里搜 WPF 残留(`System.Windows` / `SetResourceReference` / `PasswordBox` / `DependencyProperty` / `Process.Start`) | 命中全在**注释**里(说明等价物),代码里 0 处 |

**第 8/9 条为什么必须与基准逐位相等**:smoke 走的是 `MainWindow`(空外壳,不建任何页面),而 C1c 全是
"页面会调用的构建器",没有任何一个被 smoke 路径调用 —— 所以门面加了多少代码都不该改动那两张图。
结果逐位相等,正好反证了"没有动 C1b 的样式表、没有动窗口"。

### 提交(小步提交,消息前缀 C1c)

| 提交 | 内容 |
|---|---|
| `a6190b3` | `C1c(a)` 门面基座:`Clamp` / `UiPrefs` / `BtnKind` / `ToastKind` / `SPanel` / `SmoothScroll` / `UiExt` + `Ui` 的文本·面板·栅格·卡片·按钮构建器(此时 `--smoke` 已与裁决基准逐位一致) |
| `cfec58d` | `C1c(b)` `Ui` 的输入控件与其余构建器(`VirtualList`/`Input`/`Multi`/`LogBox`/`Pw`/`Combo`/`Check`/`Switch`/`Sld`/`Prog`/`Tag`/`Badge`/`Field`/`Section`/`Empty`/`Skeleton`/`RowCard`/`OpenUrl`/`Sep`/`Spring`) |
| `dd1c39c` | `C1c` 测试(100 条)+ 本文档 + DECISIONS |

提交后又在 `dd1c39c` 上复跑了一遍完整验收(结果与上表相同):`dotnet build`(0 错误 / 7 警告,NU1900 + SYSLIB0014 ×1)、
`dotnet test` ×2(退出码 0,失败 0 / 通过 2257 / 总计 2257)、进程内执行器 ×2(退出码 0,不支持 0)、
`--smoke` 明暗各一次(3956 / `9F93EF3E…` 与 3941 / `F687E6CD…`,与基准逐位相等)、
`%LOCALAPPDATA%\TipCL` 仍是 9328 / 889530606 / 2026-11-10 20:42:16、`git status` 干净、
`src\TipCL.Core` 对 `d1cfe68` 与对 HEAD 的 diff 都是空。

### 测试(新增 100 条,总数 2157 → 2257)

- `tests/TipCL.Tests/C1cFixtures.cs`:`UiHost`(挂真窗口 + 推一次渲染计时器 + 还原 `RequestedThemeVariant`)、
  `C1cExpected`(颜色/字重取值助手与**权威副本**)。C1b 那份 `Hosted` 是私有嵌套类,不能复用,所以这里另立一个。
- `tests/TipCL.Tests/C1cUiTests.cs`(76 条:**按执行器打印的用例数**计,理论展开后):文本 6 个构建器的类名/字号/字重/前景色、
  `Txt` 默认值与 bold、`Glyph` 的字体族与 4 个 `Ico` 码位、`V`/`H` 的顺序与**真布局里的间距**、
  `G` 的行列定义与 5 个 Parse 分支、`Add`/`At` 的附加属性、`Card`/`Pane`/`Sep`/`Spring`/`Tag`/`Badge`、
  7 个 `BtnKind` 的类名 + Primary 配色 + 图文结构 + 自动化名 + 点击回调、`IconBtn`、`S`/`Res` 的未知键兜底、
  `WindowPctOf` 5 组、`WindowPctConverter` 的算术与 DoNothing、`Scroll` 的三项属性、`SmoothScroll.Offset`
  与 MaxHeight 绑定、`VirtualList`(模板/裸容器**真生成出来**/行工厂真被调用/横向滚动条禁用)、
  `UiExt` 的 11 组链式调用与语义。
- `tests/TipCL.Tests/C1cUiInputTests.cs`(24 条):`Input`/`Multi`/`LogBox`/`Pw`/`Combo`/`Check`/`Switch`/`Sld`/`Prog`
  的值、类名、类样式生效后的属性与回调;`Field`/`Section`/`Empty`/`Skeleton`/`RowCard` 的结构、颜色与点击。
- **纪律**:所有碰 Avalonia 的用例体一律经 `AvaloniaSession.Run(...)`(D-79);程序化触发事件用
  `RaiseEvent`(Btn 用 `Button.ClickEvent`,RowCard 自己造 `PointerReleasedEventArgs`),**不模拟输入设备**;
  全局静态(`RequestedThemeVariant`)在 `UiHost.Dispose` 里还原;没有涉及文件、网络、进程、URL 的用例。
- **6 条期望值一开始写错、已按实测改正**(不是代码 bug,记下来免得后人重踩):
  1. `Field` 的栅格**只有两列没有行定义**(Windows 源就是 `G(null, "160,*")`);
  2. 裸 `TextBlock` 的默认前景不是 null 而是 Avalonia 的 Black(Windows 的隐式 TextBlock 样式同样**不设**
     Foreground,所以不断言颜色才是对的);
  3. SPanel 作为窗口内容会被拉满,断言"面板自身尺寸"没意义,改成断言**子项位置**;
  4. `B.BannerFill` 是**渐变刷**,不能用于颜色断言;
  5. 色板里没有 `B.OkSoft`,只有 `B.Ok` / `B.OnOk`;
  6. 断言了不存在的 `B.OkOn` 键 —— 已换成真实键。

### 未验证项

1. **macOS 真机**(Avalonia.Native / CoreText):字体族 `PingFang SC` / `Menlo` / 图标字体能否命中、控件观感、动画观感 ——
   本机是 Windows,一律未验证。
2. **真实滚轮平滑滚动的观感与每格位移**(没有驱动真实滚轮/指针);只验了 `Offset` 的设值与 MaxHeight 绑定算术。
3. **真生命周期下的动画**:`Skeleton` 的横扫与 `SmoothScroll` 的缓出动画在测试与 `--smoke` 里**按设计不启动**,
   所以它们的实际播放效果本机未验证(只验了静态形态与不启动路径)。
4. **`OpenUrl` 未在测试里调用**(红线:测试不许起进程、不许开 URL),所以"真能打开浏览器"这一条未验证;
   它转调的 `Crash.ShellOpen` 的 mac 分支是 B1 的既有实现。
5. **`WindowPctOf` 从"当前主窗口"取高度这条路径未验证**:测试进程里没有 `ApplicationLifetime`,
   只验了可注入高度的纯算术与绑定接线。
6. **mac 上 `●`(U+25CF)在默认字体里是否有字形**:未验证(掩码字符取自 WPF 的默认值)。
7. `PasswordChar` 的显示效果、`TextBox` 密码框的可访问性(读屏)未验证。

### 遗留与风险

1. **`Ui.S` 的返回类型变了(见 D-80)**:C2..C8 的页面不能照抄 `Style = Ui.S(x)`,要用 `StyleKeys.Apply`。
   若 Lead 认为公开面必须保 `Style`,回退成本很小(见 D-80 的代价说明)——**这是本包唯一需要 Lead 复核的取舍**。
2. **`Pw` 的调用点**要按 `TextBox` 处理(Avalonia 没有 `PasswordBox`);`Password` 属性不存在,取文本用 `Text`。
3. **Motion 门面(C2)落地后**应把 `Skeleton` 的横扫与 `SmoothScroll` 的滚轮平滑改成走 Motion,并复核
   `Lucide.MotionEnabled` 这个替身是否还该留着。
4. **C2 接线后要复核三处**:`Ui.OpenUrl` 的失败提示(应换成 Toast)、`Ui.WindowPctOf` 的宿主高度来源
   (现在是"桌面生命周期的主窗口")、`UiExt.Tip` / `ToolTip.SetTip` 与 C2 的 ToolTip 宿主是否有交互。
5. `Clamp.cs` / `UiPrefs.cs` / `BtnKind.cs` / `ToastKind.cs` 是字节级复制来的:**后续包的清单里若再出现
   这四个文件,不要重复搬**(否则会变成"文件名冲突"或重复定义)。
6. `SPanel` 的两个属性加了 `InvalidateMeasure()`,这是相对 WPF 原版的一处**行为增强**(WPF 那份没有);
   公开成员(类型/名字)不变,已记在 D-82 之外的类注释里。



---

## C2a 桥层与 WPF-free 基础服务(Avalonia 11 移植)

本包只做**桥层 + 基础服务**:没有建外壳/窗口/导航(那是 C2b),没有搬任何页面,没有改色板 /
`Controls.axaml` / `Ui`·`UiExt` 的公开面,没有改 `MainWindow.cs` / `Smoke\`,没有改 `src\TipCL.Core`
一个字节(收尾对拍为证)。`Ui.cs` 只按任务书要求改了 **`Skeleton` 之外的 Lucide 那一层**
(见第 6 条),`App.cs` 只加了一行 `I18n.Init`(见第 7 条)。

### 1. 先纠正任务书里的一条事实:Core 侧并没有桥的客户端

任务书说「`src\TipCL.Core` 里已经有 `BackendApi`、`RpcDispatcher`、`BridgeServer`、`BridgeClient`、
`BridgeEvent`、`BridgeCallException`」。**实测不是**:

| 类型 | Windows 侧的位置 | mac 的 `src\TipCL.Core` |
|---|---|---|
| `BackendApi` | `Core\BackendApi.cs` | **有**(A5) |
| `RpcDispatcher` | `Core\RpcDispatcher.cs` | **有**(A5) |
| `BridgeServer` | `Core\BridgeServer.cs` | **有**(A5) |
| `EventBus` | `Core\EventBus.cs` | **有**(A5) |
| `BridgeClient` / `BridgeEvent` / `BridgeCallException` | `Services\`(**App 侧**,不在 Core) | **没有**(本包才搬) |

所以本包不存在「App 侧薄包装 Core 侧同名类型」的情况:这三个文件是**从 Windows 的 `Services\`
原样搬进 `src\TipCL.App\Services\`** 的,命名空间仍是 `TipCL.Services`(与 Windows 一致),
Core 侧的 `BridgeServer` 照旧被 `BridgeHost` 当服务端用。

### 2. 搬了什么(按任务书清单)

| 文件 | 处理 | 改动 |
|---|---|---|
| `Services\Fmt.cs` | **字节级复制** | 无 |
| `Services\FileKinds.cs` | **字节级复制** | 无 |
| `Services\TaskRow.cs` | **字节级复制** | 无 |
| `Services\TaskStore.cs` | **字节级复制** | 无(依赖的 `I18n` / `Fmt` / `Clamp` 都已就位) |
| `Services\BridgeEvent.cs` | **字节级复制** | 无(依赖 `TipCL.Models.CrashReport`,见下) |
| `Services\BridgeCallException.cs` | **字节级复制** | 无 |
| `Services\Payload.cs` | 最小改动 | `DefaultRoot` 改指 `Utils.ResolveRoot()`;删 `EmbeddedPython()` |
| `Services\I18n.cs` | 最小改动 | 词表改读 Core 内嵌资源;搬进 `HasCjk`(原在 `I18nCheck`) |
| `Services\AppServices.cs` | 最小改动 | `Window` 静态类型 + `ToastSink`(见差异表) |
| `Models\CrashReport.cs`、`Models\CrashAction.cs` | **字节级复制** | 无 |
| `Services\BridgeClient.cs` | **字节级复制** | 无 |
| `Services\BridgeHost.cs` | 大改(只剩进程内一条路) | 见第 3 条 |
| `Services\Dlg.cs` | 移植到 Avalonia | 见差异表 |
| `Services\Motion.cs` | 移植到 Avalonia | 见差异表 |

**`Models\` 只搬了两个文件**:`BridgeEvent.Crash` 的类型是 `TipCL.Models.CrashReport`,而 mac 侧
整个 `Models\` 目录(45 个 DTO)属页面阶段(C3+),本包只搬 `CrashReport.cs` + `CrashAction.cs`
这两个自足的(`System.Text.Json` + `System.Collections.Generic`)来保住 `BridgeEvent` 的公开成员。
**后续搬 `Models\` 整目录时不要再搬这两个**(会重复定义)。

### 3. 去掉的外部 Python 桥分支(逐条)

| Windows 成员/分支 | 为什么去掉 | 替代物 |
|---|---|---|
| `BridgeHost.StartExternalAsync`(整段 120 行:起进程、读端口、`EnableRaisingEvents`、30 秒超时、失败 `KillTree`) | GOAL 决定 mac 版**没有**外部 Python 桥 | `StartAsync` 只保留 `StartInProcessAsync` |
| `StartAsync` 里的 `TIPCL_BRIDGE` / `TIPCL_BRIDGE_EXE` 两条早退分支 | 同上 | 直接走进程内 |
| `FindPython` / `PythonCandidates` / `RegisteredPythons`(注册表)/ `ProbePython` | 只为外部桥选解释器;`RegisteredPythons` 还是 `Microsoft.Win32.Registry`(Windows 专属 API) | 全删 |
| `Payload.EmbeddedPython()`(`<root>\runtime\python\python.exe`) | 外部桥的内嵌解释器;mac 上没有,也没有调用点 | 全删(成员一起删) |
| `FindNativeBridge` / `LooksLikeRoot` / `WalkUp` / `QuoteArg` / `Tail` / `PortRegex` / `KillTree`(`taskkill /T /F`) | 只服务于「往上找 payload 根 / 起原生桥 / 解析它的 stdout」 | 全删 |
| `BridgeHost.FindRoot` 的「往上一层层找 payload 根」 | mac 版没有 payload 根 | `NormalizeRoot(Utils.ResolveRoot())` —— GOAL 的数据目录(`~/.tipcl`,`TIPCL_HOME` 可覆盖);隔离进程里就是 `TestHome.Root`(有用例钉住) |
| 外部桥那台 `BridgeHost(BridgeClient, Process, ...)` 的私有构造 + `_proc` 字段 + `Dispose` 里的杀进程 | 没有子进程了 | 只留进程内构造与 `server.Stop()` |
| 私有类 `CoreState` / `EnsureCore`(进程内那条路) | **保留,逐字未改** | 进程内路径与 Windows 完全一致(`TIPCL_HOME` → `Utils.SetRoot` → `Config.Reset` → `Guard.Install` → `I18nCore` → `EventBus` → `BackendApi` → `McpServer.StartIfEnabled` → `PluginManager.StartGlobal`) |

一条给 C2b 的提醒(D-23 里也记着):`EnsureCore` → `new BackendApi(...)` 的构造函数会**无条件**
向 `login.live.com` 发预热请求,所以真启动时那次联网是**预期行为**,不是本包引入的。

### 4. 公开面差异表(有必要的全部;其余一字未改)

| Windows 公开面 | mac 侧 | 原因 |
|---|---|---|
| `AppServices.Window : MainWindow?` | `Window? Window`(Avalonia 的窗口基类) | mac 的 `MainWindow` 是 `internal`(CS0053:公开成员不能暴露 internal 类型);同程序集里 `AppServices.Window as MainWindow` 照样能取到外壳 |
| `AppServices.Toast` 直接 `Window?.Toast(...)` | 走新增的 **internal** `ToastSink` 委托,宿主为空时 `Debug.WriteLine` | 外壳类(C2b)还没落地,本包不能凭空依赖它的成员;降级口径与 D-84 的 `Ui.OpenUrl` 一致 |
| `I18n.LocalesDir` = 磁盘目录 | = 内嵌资源位置(`TipCL.Core.Resources.locales`) | mac 没有外部 payload,**词表按 GOAL 内嵌进 Core**(与被搬进 Core 的 `I18nCore` 同一份资源);成员名与类型不变 |
| `I18n.EnsureTables` 从磁盘读 | 从 `CoreResources.ListDir("locales")` 读 | 同上 |
| `I18nCheck.HasCjk` | `I18n.HasCjk`(internal) | `I18nCheck.cs` 是开发期源码扫描器(469 行,依赖 `PyPath` 与仓库布局),本包**没有整体搬**(见遗留);只把 `L()` 用到的这一个助手按原样搬进来 |
| `Payload.DefaultRoot` = `%LOCALAPPDATA%\TipCL` | `Utils.ResolveRoot()` | 非 Windows 上 `LocalApplicationData` 是 `~/.local/share`,那不是 GOAL 的数据目录;照抄会凭空多出**第二个数据根**,而且 `EnsureRoot` 的 `CleanupLegacy` 会在那个目录下删 `mclauncher` |
| `Dlg.Host : Grid?` / `Layer.Root : Grid` / `Layer.Card : Border` | 同名同类型(Avalonia 也有 `Grid`/`Border`) | 无差异 |
| `Dlg.BlurTarget : UIElement?` | `Control?` | Avalonia 没有 `UIElement` |
| `Dlg.Ask/Alert/Prompt/Choose/Form/Panel/Busy` 的 `UIElement` 参数 | `Control` | 同上(名字/顺序/默认值/回调形状不变) |
| `Dlg.PickFile/PickFiles/SaveFile/PickFolder` | **签名一字未改** | 内部改走 `IStorageProvider`(见下) |
| `Dlg` 的 `Smoke.Active` / `Smoke.AutoAnswerDialog` / `Smoke.AutoCancelSystemDialog` 调用 | 改成可注入挂点 `Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog`(internal) | mac 的 `Smoke\` 里没有这两个入口,而 `Smoke\` 属 C2b/D1(本包不许改);未接线时这一步不生效(离屏自动答问暂时不触发) |
| `Motion.FadeIn(UIElement, ...)` 等 20 个方法的 `UIElement`/`FrameworkElement` | `Control` | 与 C1c 同一口径(D-70) |
| `Motion.Loop(FrameworkElement, ...)` | `Loop(Control, ...)` | `owner.IsLoaded` → "在可视化树上或已 Loaded" |
| `Motion.Shadow<T> where T : UIElement` | `where T : Control` | 同上;`Border` 走 `BoxShadow`、其余走 `Visual.Effect` |
| `Motion.Enabled` 的 `get` = `_enabled && SystemParameters.ClientAreaAnimation && !IsRemoteSession` | `_enabled && 有桌面生命周期` | Avalonia 没有 `SystemParameters`;「有没有人推进时钟」这条与 D-83 的纪律合流(测试/离屏一律算关) |
| WPF `BackEase(Amplitude=0.4)` / `QuadraticEase` | `CubicEaseOut` / `SineEaseInOut` | Avalonia 的 `BackEaseOut` 没有 `Amplitude`,且 `KeyFrame` 没有逐帧 `Easing`(只有 `KeySpline`),所以逐帧缓动收成整段缓动 |

**新增的公开面**:一个也没有(`AppServices.ToastSink`、`Dlg.SmokeActive`/`SmokeAutoAnswer`/
`SmokeAutoCancelSystemDialog`、`I18n.HasCjk`、`BridgeHost` 的 4 个 internal 助手、`Dlg.ParseFilter`
都是 internal)。`BridgeHost.ClientBaseUrl` / `NewToken` / `NormalizeRoot` 由 private 提为 internal —— 只为单测。

### 5. 降级与观感差异

1. **文件/文件夹对话框**:WPF 的 `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog` /
   `FolderBrowserDialog` 是**同步** API;Avalonia 只有 `IStorageProvider` 的**异步** API。mac 侧在等它的同时
   把 Dispatcher 的排队工作跑掉(等价 WPF 的同步嵌套消息循环),过滤器串仍是 WPF 的 `标签|模式;模式`
   语法,由 `Dlg.ParseFilter` 转成 `FilePickerFileType[]`(`*.*` → `*`)。
   **拿不到提供者时(离屏/测试/还没起窗口)返回 null 并只写调试输出**,不抛异常、不起进程。
2. **`Dlg.Alert` 的 `TextBox.VerticalScrollBarVisibility`**:Avalonia 的 `TextBox` 没有这个属性,
   改用 `ScrollViewer.SetVerticalScrollBarVisibility(textBox, Auto)` 附加属性(是"尽力而为":
   TextBox 模板认不认这条附加属性本机没验,见未验证项)。
3. **卡片 MaxHeight 的绑定**:WPF 绑 `Window.ActualHeight`(converter ×0.9);mac 侧绑**宿主 Grid 的
   `Bounds.Height`**(Avalonia 没有 `ActualHeight`,D-82 同一口径)。
4. **`Motion` 的真动画**:`Animation.RunAsync` + 终态填充(与 Lucide 的做法一致,D-70)。
   真生命周期下的播放效果本机没法验;测试里验的是"关掉动画 = 立刻落终态"这条路。
5. **`Dlg` 的浮层动画**:`Motion.Fade/PopIn/PopOut` 在离屏/测试里都算"关掉动画",所以关闭是**同步**完成的
   —— 这不是"桩",正是 WPF 关掉动画时的行为。

### 6. `Lucide` 的那一处改动(任务书点名)

D-83 的 C1c 遗留是「`Skeleton` 的横扫与 `SmoothScroll` 的滚轮平滑在 Motion 落地后要改成走 Motion」。
本包落到能落的部分:

- `Lucide.MotionEnabled` 由「Motion 的临时替身」改成 **Motion 用户开关的转发别名**
  (读 `Motion.UserEnabled`、写 `Motion.Enabled`),名字/类型不变 → **全应用只剩一个动画开关**;
- `Lucide.Shimmer` 的无限高光动画由自带的 `Run(animation, slide)` 改成 **`Motion.Loop`**
  (与 Windows 版的 `Motion.Loop(tb, start, stop)` 一一对应):只在真生命周期里起、卸载或关掉动画时停。
- **`Ui.Skeleton` 与 `SmoothScroll` 没有改**:任务书开头明确写"不许改 `Ui` 门面",所以这两处的接线
  **留给 C2b**(它们现在仍在用 `Lucide.MotionEnabled` 这个转发别名,行为与 C1c 一致:
  测试/离屏下都不起动画)。这条要请 C2b 收尾时一并处理,已写进遗留。

### 7. `App.cs` 的接线

只加了一句 `I18n.Init((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Args)`,
位置在 `OnFrameworkInitializationCompleted`(即所有样式安装**之后**),所以
`FluentTheme → Palette → Controls.axaml` 的安装顺序一字未动;`--smoke` 走的是
`SetupWithoutStarting()`,根本不进这个方法,所以**冒烟基准不受影响**(实测逐位一致)。
它只读数据目录里的 `config.json`,不落盘、不建目录。

### 8. 新增/修改文件

| 文件 | 说明 |
|---|---|
| `src/TipCL.App/Models/CrashReport.cs`、`CrashAction.cs` | 新增(字节级复制) |
| `src/TipCL.App/Services/Fmt.cs`、`FileKinds.cs`、`TaskRow.cs`、`TaskStore.cs`、`BridgeEvent.cs`、`BridgeCallException.cs`、`BridgeClient.cs` | 新增(字节级复制) |
| `src/TipCL.App/Services/Payload.cs`、`I18n.cs`、`AppServices.cs` | 新增(最小改动,见差异表) |
| `src/TipCL.App/Services/BridgeHost.cs` | 新增(只剩进程内一条路) |
| `src/TipCL.App/Services/Dlg.cs`、`Motion.cs` | 新增(Avalonia 移植) |
| `src/TipCL.App/Services/Lucide.cs` | 改:`MotionEnabled` 转成转发别名;`Shimmer` 走 `Motion.Loop`(公开面不变) |
| `src/TipCL.App/App.cs` | 改:加一句 `I18n.Init`(样式安装顺序不变) |
| `tests/TipCL.Tests/C2aServicesTests.cs` | 新增(Fmt 35 条 + I18n 16 条) |
| `tests/TipCL.Tests/C2aTaskStoreFileKindsTests.cs` | 新增(TaskStore/TaskRow 17 条 + FileKinds 35 条) |
| `tests/TipCL.Tests/C2aBridgeTests.cs` | 新增(BridgeHost 8 + BridgeEvent 5 + BridgeClient 15) |
| `tests/TipCL.Tests/C2aDlgMotionTests.cs` | 新增(Dlg 25 + Motion 14) |
| `tests/TipCL.Tests/TipCL.Tests.csproj` | 改:`Ported\FileKindsTests.cs` 放回编译(A6 的 C 类 → 已恢复) |
| `tests/TipCL.Tests/Ported/FileKindsTests.cs` | 改 2 处:`Path.GetTempPath()` → `TestHome.Root`(A6 的 B 类路径规则,红线:临时文件不出仓库);另加 `using TipCL.Tests;`;断言一字未改 |

**没有改**:`src\TipCL.Core\`(整个目录)、`Themes\`(色板 + `Controls.axaml` + `StyleKeys`)、
`Ui.cs` / `UiExt.cs`、`MainWindow.cs`、`Smoke\`、测试隔离三件(`TestHomeIsolation.cs` /
`AssemblyInfo.cs` / `Program.cs`)、`Directory.Build.props`、`TipCL.Mac.sln`、`.gitignore`。

### 9. 测试(新增 210 条,总数 2257 → 2467)

- 纪律:两条通道各连跑 2 次全绿;碰 Avalonia 的用例体**一律**经 `AvaloniaSession.Run(...)`(D-79),
  没有第二处 `SetupWithoutStarting`;事件用 `RaiseEvent` 程序化触发,**不模拟输入设备**;
  全局静态(`I18n.Current` / `CultureInfo.CurrentUICulture` / `Motion.Enabled` / `Dlg.Host` /
  `Dlg.SmokeActive` / `I18nCore.CurrentLang` / `TaskStore` 的事件)全部在 `finally` 里还原。
- `TaskStore` 的用例在 finally 里"退订事件 + 清空整张表 + 断言行数回到进入时的值"(不留跨用例污染)。
- 桥的用例用 **`TcpListener` 绑 127.0.0.1:0** 的假后端(**不是 `HttpListener`**,D-27),
  每个用例 `finally` 里 Dispose;**绝不实例化 `BackendApi` / `BridgeServer` / `PluginManager`,
  绝不调 `BridgeHost.StartAsync`**(D-23)。
- 写测试时抓到 **4 处我自己的期望写错**(不是产品代码的 bug,记下来免得后人重踩):
  1. `Fmt.Duration` 的小时分支只到 24 小时,`90061` 秒(25 小时)走的是**天**分支(改成 9000 秒验小时分支);
  2. `TaskStore.Newest` 在没有未完成行时退化成"最后一行",所以把 r1/r2 都完成之后是 **r2** 不是 r1;
  3. `Dlg.Panel` 的 `onClose` **只在关闭时**回调,`Push` 时不动它(我原先以为 Push 里会调一次);
  4. `BridgeEvent.Payload` **不是**普通反序列化填的 —— 真路径上由 `BridgeClient.Dispatch` 把根元素
     `Clone()` 塞进去;所以纯反序列化用例要自己给 JSON 带一个 `payload` 字段。
- **D-58 又现形两次**(全角标点抄不进字面量,这次是**键**):
  `TaskStore` 的默认状态键是 `排队中…`(**U+2026**,不是三个 ASCII 点);
  `FileKinds.ModpackProbe.LoaderLabel("")` 的键用的是**全角括号** `（`/`）`(U+FF08/U+FF09)。
  两条都改成 `\uXXXX` 转义后才绿。

### 10. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;7 条警告 = `NU1900`×6 + `SYSLIB0014` **唯一一处**,按警告码分组核过(首轮踩到 `xUnit1031`×10 / `xUnit2031`×2,已把桥的用例改成 `async`+`await`、把 `Assert.Single(x.Where(...))` 改成带谓词的重载,现在是 0) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` **第 1 / 2 次** | 都是退出码 **0**:`已通过! - 失败: 0,通过: 2467,已跳过: 0,总计: 2467,持续时间: 2 s` |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` **第 1 / 2 次** | 都是退出码 **0**:`总计 2467 个用例,通过 2467,失败 0,跳过 0,不支持 0`,末尾 `in-proc 测试全绿`;**不支持 0**;总数与 `dotnet test` 相等 |
| 5 | `--smoke --theme light --out artifacts\c2a-light.png` | 退出码 0,**3956 字节**,SHA256 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C` —— **与裁决基准逐位相等** |
| 6 | `--smoke --theme dark --out artifacts\c2a-dark.png` | 退出码 0,**3941 字节**,SHA256 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E` —— **与裁决基准逐位相等** |
| 7 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**与开工前完全一致** |
| 8 | 残留进程 `testhost` / `TipCL` / `java` / `adb` | **0 / 0 / 0 / 0** |
| 9 | `%TEMP%` 下本次会话(09:15 之后)新建的 `tipcl*` 目录 | **0 个**(现存 89 个全是此前会话留下的) |
| 10 | `git diff --name-status dd1c39c HEAD -- src/TipCL.Core` | **空** |
| 11 | `src\TipCL.Core\*.cs` 对 Windows 源 SHA256(排除 `bin\`/`obj\`) | mac 侧 248 个:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线**完全一致** |
| 12 | 新增/修改文件里搜 WPF 残留(`System.Windows` / `DependencyProperty` / `SetResourceReference` / `PasswordBox`) | 命中全在**注释**里(说明等价物),代码里 0 处 |

**第 5/6 条为什么必须与基准逐位相等**:`--smoke` 走 `SetupWithoutStarting()`,既不起桌面生命周期,
也进不了 `OnFrameworkInitializationCompleted`(所以 `I18n.Init` 不执行),窗口与样式一行未动 ——
逐位相等正是"没动样式表、没动窗口"的反证。

### 11. 提交(小步提交,消息前缀 C2a)

| 提交 | 内容 |
|---|---|
| `70b7443` | `C2a(a)` WPF-free 基础服务(`AppServices` / `Fmt` / `FileKinds` / `TaskRow` / `TaskStore` / `I18n` / `Payload` / `BridgeEvent` / `BridgeCallException` + `Models` 的 `CrashReport`/`CrashAction`) |
| `c12c9ff` | `C2a(b)` 桥对(仅进程内的 `BridgeHost` + `BridgeClient`) |
| `1c4679f` | `C2a(c)` `Dlg` 与 `Motion` 的 Avalonia 移植 + `Lucide.MotionEnabled` 转发 + `App.cs` 的 `I18n.Init` |
| 本次 | `C2a(d)` 测试(210 条)+ 本文档 + DECISIONS |

### 12. 未验证项(本机是 Windows,没有 Mac)

1. **macOS 真机**:Avalonia.Native 下的浮层观感、阴影(`BoxShadow` / `DropShadowEffect`)、
   文件对话框(`IStorageProvider` 的 NSOpenPanel/NSSavePanel)、`ScrollViewer` 附加属性在 TextBox 上的效果 —— 一律未验证。
2. **真生命周期下的动画播放**(`Motion` 的 20 个方法在 `Enabled = true` 时的实际观感、
   时长、缓动):测试与 `--smoke` 里没有时钟,按设计全走"关掉动画"分支;本机**没有验过**播放效果。
3. **`Motion.Loop` 的"已挂载元素立刻起动画"这条路**(我用 `IsAttachedToVisualTree()` 代替 WPF 的
   `IsLoaded`):离屏下 `start` 不会被调用,所以这条分支没有真跑过。
4. **`WaitForPicker` 的嵌套消息循环**:测试里 `Provider()` 恒为 null,这条路径(以及 10 分钟超时分支)未跑过。
5. **`i18n` 词表的全量对拍**:只做了"App 表 == Core 表"的交叉抽样(找到的键),没有逐键比对 100%。
6. **`EnsureCore` 的真实启动路径**(会造 `BackendApi` → 联网预热):按 D-23 红线**没有**在测试里跑过,
   只测了它的纯静态邻居。
7. `--smoke` 只验了 Windows 上的 Avalonia 渲染;离屏自动答问(`Dlg.SmokeAutoAnswer`)没有接线,未验证。

### 13. 遗留与风险

1. **`Ui.Skeleton` 与 `SmoothScroll` 还没有走 `Motion`**(任务书不许改 `Ui` 门面):
   它们现在读 `Lucide.MotionEnabled`(已是 Motion 的转发别名),行为与 C1c 一致。
   **C2b 接线时把这两处改成 `Motion.Loop` / `Motion.Enabled` 即可**,顺手复核还要不要留
   `Lucide.MotionEnabled` 这个名字(它现在只是个转发别名,页面/外壳一旦不用就可以删)。
2. **C2b 必须做的三处接线**:① `AppServices.Window = 外壳`(并设 `AppServices.ToastSink`);
   ② `Dlg.Host` / `Dlg.BlurTarget`;③ `Motion.Enabled = _prefs.Anim`(Windows 在 `MainWindow` 1437 行做这件事)。
   还有离屏那两条:`Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog`。
3. **`Payload` 现在没有调用点**:`BridgeHost.FindRoot` 改走 `Utils.ResolveRoot()` 之后,
   `Payload.EnsureRoot()` 在 mac 侧没有调用者(Windows 是 `FindRoot` 的最后兜底)。
   本包按任务书清单把它搬进来了(且 `DefaultRoot` 已对齐 mac 数据目录),但它是**事实上的死代码**;
   若 C2b 认为该删,直接删掉即可(它是 `internal` 类,不影响任何公开面)。
4. **`I18nCheck.cs` 没有搬**(469 行,开发期源码扫描器):它依赖 `PyPath.GetRelativePath` 与
   Windows 的仓库布局(`root\src\TipCL`),不满足任务书"小且自足"的条件。已把 `L()` 需要的
   `HasCjk` 搬进 `I18n`。**若 D1 想要 `--i18n-check`,需要单开一个小包**(mac 的布局是 `src\TipCL.App`)。
5. **`AppServices.Toast` 目前只写调试输出**:外壳落地前,`Dlg.Form` 的"填写有误"提示、页面里
   所有 `AppServices.Toast(...)` 都是**静默**的(C2b 设了 `ToastSink` 才出声)。
6. `Motion` 的逐帧缓动被收成整段缓动(WPF 的 `BackEase`/`QuadraticEase` 在 Avalonia 侧没有对等物),
   观感会有细微差别;`Shine` 的 7 秒节拍、`HoverLift` 的阴影强度是照 WPF 的数值写的,未在真机校验。
7. `%TEMP%` 下那 89 个 `tipcl*` 目录是历史遗留(此前会话留下),本包运行期间**没有**新增;
   `tipcl-probe` 属别的会话(D-61)。

### Lead 验收(2026-11-13 10:10)

结论:**C2a 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 四个提交(`70b7443` a / `c12c9ff` b / `1c4679f` c / `17615cd` d) | 工作树干净;无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| 红线文件(`src\TipCL.Core`、`Themes\`、`MainWindow.cs`、`Smoke\`、`Ui.cs`、`UiExt.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| `Ported\FileKindsTests.cs` 恢复编译的改动 | 只有 A6 B 类范围内的 2 处:两处 `Path.GetTempPath()` → `TestHome.Root`;断言一字未改 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` ×4 + `SYSLIB0014` ×1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2467 / 总计 2467**(2257 → 2467,+210) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2467 / 失败 0 / 不支持 0** |
| `--smoke` light / dark | 3956 字节 `9F93EF3E…` / 3941 字节 `F687E6CD…`,**与裁决基准逐位一致** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;0 个;0 个 |

**对子代理 4 个待裁决事项的裁决**:

1. **任务书说「Core 里已有 `BridgeClient` / `BridgeEvent` / `BridgeCallException`」是错的** —— 子代理实测 Core 只有 `BackendApi` / `RpcDispatcher` / `BridgeServer` / `EventBus`;那三个类在 Windows 的 `Services\` 里。**接受它的做法**(字节级搬进 App、命名空间仍 `TipCL.Services`)。这是 Lead 写任务书时的事实错误,记此更正。
2. **`Ui.Skeleton` 与 `SmoothScroll` 改成走 `Motion`**:交 **C2b**(任务书同时禁止改 `Ui` 门面与要求接线,是我给的约束冲突;C2b 无关这条禁令,由它统一改并复核 `Lucide.MotionEnabled` 转发)。
3. **`AppServices.Window` 静态类型放宽成 Avalonia `Window`**:接受。为这一个字段把 `MainWindow` 改成 public 得不偿失;`ToastSink` 委托的做法也接受。
4. **子代理修正了 Lead 插错位置的 `## C2a` 占位标题**:接受。Lead 当时用 C1b 小节里的一行作锚点插入,把 C1b 的「遗留与风险」列表切断了;现已把真实 C2a 小节追加到文件末尾。**同时记下两处文档瑕疵(纯排版,不再花提交去改)**:第 2989 与 3041 行有两个同名 `## C1a` 一级标题;C1c 的验收段落落在 C1b 小节末尾而不是 C1c 小节里。

**只记录、不处理**:

- `Payload` 在 mac 侧没有调用点(事实上的死代码,internal),留待 D 阶段清理。
- `I18nCheck.cs`(469 行)未搬,若 D1 要 `--i18n-check` 需单开小包。
- `EnsureCore` 的真实启动(真正拉起 `BackendApi` + `BridgeServer`)按 D-23 没有测过;这也是 GOAL 里「后端能否真的跑起来」的最后一个未验证点,归 C2b 的接线验收或 D1。

**给 C2b 的接线清单(必须做,否则外壳不可用)**:`AppServices.Window`、`AppServices.ToastSink`、`Dlg.Host`、`Dlg.BlurTarget`、`Motion.Enabled = <用户开关>`、离屏的 `Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog`,以及把 `Ui.Skeleton` 的横扫与 `SmoothScroll` 的滚轮平滑改走 `Motion`。

---

## C2b 应用外壳:窗口装饰 / 导航 / 页面宿主 / 系统菜单与快捷键(2026-11-13)

本包**只做外壳**:没有移植任何页面(C3 才逐页搬),没有画 Toast 卡片 / Dialog 卡片 / DropVeil /
Backdrop / Wallpaper / SideFrost / ImeGuard / AccountGate / Thumbs / LoaderIcons(属 C2c),
没有改色板值、没有改 `Controls.axaml` 的一个字符、没有改 `Ui` / `UiExt` 的公开面
(只在 `Ui.Skeleton` 与 `SmoothScroll` 的滚轮平滑这两处按任务书改走 Motion),
没有改 `src\TipCL.Core\` 一个字节,没有删改任何既有测试、没有加 `Skip`、没有扩 `NoWarn`。

### 1. 做了什么

1. **导航模型**(照 Windows 源逐行移植、语义一字不改):`NavModel`(默认顺序/置顶/隐藏、
   6 个分组、两个折叠分区 `download` / `more`、分区成员规则、可见顺序、pin/unpin/reorder/merge
   的补丁计算)、`NavConfig`(`ui_nav_*` 六个键 + `ui_section_members` 的解析/克隆/打补丁)、
   `NavDef` / `NavEntry` / `NavEntryKind` / `NavGroup`。
2. **`PageBase` + 页面宿主**:`Pages\PageBase.cs` 与 Windows 同名同义
   (`Id` / `Title` / `Api` / `Win` / `EnsureLoadedAsync` / `LoadAsync` / `RefreshAsync` /
   `OnEvent` / `OnShown` / `OnHidden` / `Toast` / `Run` / `Body` / `ScrollBody`);
   另抽出一个 `Pages\PageHost.cs`(C2b 唯一的结构性新增),把 Windows 长在 `MainWindow` 里的
   `_pages` / `_current` / `_currentId` + 换页 + 生命周期那几行搬成可单独测的控件。
3. **窗口外壳**:900×550(最小 720×480)、46px 标题栏、`ExtendClientAreaToDecorationsHint` +
   `PreferSystemChrome`(macOS 系统红绿灯)、标题栏内容左侧让出 78px、**不画任何 caption 按钮**;
   侧栏宽度取 `UiPrefs.SideWidth`(夹在 140~320);右侧是页面宿主 + 页面底纹;删掉了 A1/C1a 的占位
   底板(那块"居中大标题")。
4. **系统菜单 + 快捷键**:`NativeMenu`(挂在窗口上:`NativeDock.SetMenu`),两块子菜单
   「页面」(可见顺序前 9 项,依次挂 ⌘1...⌘9,当前页打勾)与「操作」(AI 助手 ⌘K、设置 ⌘,、
   刷新 ⌘R、切换深色 / 浅色);另挂隧道阶段的 `KeyDown` 处理(Windows 的 `PreviewKeyDown`),
   ⌘K 的"对话框开着 / 焦点在输入框里就不抢"规则照抄。
5. **接线**(C2a 验收清单):见下面第 4 节。
6. **两个 Motion 遗留点**:`Ui.Skeleton` 的横扫改成 `Motion.Loop(起横扫, 停横扫)`,
   `SmoothScroll` 的滚轮平滑改回读 `Motion.Enabled`;顺带复核了 `Lucide.MotionEnabled` 的转发。

### 2. 新增 / 改动的文件

新增(共 10 个源文件 + 4 个测试文件):

| 文件 | 来源 |
|---|---|
| `src\TipCL.App\NavModel.cs` / `NavConfig.cs` / `NavDef.cs` / `NavEntry.cs` / `NavEntryKind.cs` / `NavGroup.cs` | Windows 源同名文件移植 |
| `src\TipCL.App\Pages\PageBase.cs` | Windows 源 `Pages\PageBase.cs` 移植 |
| `src\TipCL.App\Pages\PageHost.cs` / `PageRegistry.cs` | C2b 新增(C0 结构,见 D-94) |
| `src\TipCL.App\Smoke\SmokeState.cs` | Windows `Smoke.cs` 里"离屏状态 + 对话框自动应答"那一小块(见 D-93) |
| `src\TipCL.App\Services\ShellCommand.cs` / `ShellShortcuts.cs` | C2b 新增(命令 + 纯函数键位映射) |
| `tests\TipCL.Tests\C2bFixtures.cs` / `C2bNavTests.cs` / `C2bShellTests.cs` / `C2bWiringTests.cs` | 本包测试(122 条用例) |

改动:`MainWindow.cs`(重写:窗口装饰 + 侧栏 + 页面宿主 + 菜单)、`Smoke\SmokeRunner.cs`
(离屏接线 + 注入默认偏好 + `--page` 真的尝试切页)、`Services\Ui.cs`(仅 `Skeleton` 那处)、
`Services\SmoothScroll.cs`(仅滚轮那处)、`Services\AppServices.cs`(只改注释)、
`tests\TipCL.Tests\TipCL.Tests.csproj`(恢复 `Ported\NavCompatTests.cs` 编译)。

### 3. 导航页面 id 表(与 Windows `MainWindow.NavDefs` 逐字一致)

| # | 页面 id | 标题 | 图标码位 | 导航键(`PageForNavKey`) | 默认可见顺序 | ⌘ 快捷键 |
|---|---|---|---|---|---|---|
| 1 | `launch` | 启动 | `\ue7c1` | `launch` | 1 | ⌘1 |
| 2 | `library` | 实例库 | `\ue80a` | (无,走顶栏/内部入口) | — | — |
| 3 | `instance` | 版本管理 | `\ue80a` | `instance` | 2 | ⌘2 |
| 4 | `account` | 账号 | `\ue77b` | `account` | 4 | ⌘4 |
| 5 | `ai` | AI 助手 | `\ue99a` | `ai` | 5 | ⌘5 |
| 6 | `multiplayer` | 联机 | `\ue701` | `multiplayer` | 3 | ⌘3 |
| 7 | `settings` | 设置 | `\ue713` | `settings` | 8 | ⌘8 |
| 8 | `tasks` | 下载任务 | `\ue9d5` | `tasks` | 6 | ⌘6 |
| 9 | `download` | 下载 | `\ue896` | (分区键,`PageForNavKey` 里**没有**) | — | — |
| 10 | `plugins` | 插件 | `\uea86` | `plugins` | 7 | ⌘7 |

默认配置(分组式)下的**可见页面顺序** = `launch` → `instance` → `multiplayer` → `account` →
`ai` → `tasks` → `plugins` → `settings`(共 8 项,所以默认只用到 ⌘1...⌘8;⌘9 没有第 9 页可去,
`NavigateIndex` 越界时什么都不做 —— 与 Windows 的 `num < list.Count` 守卫一致)。
侧栏行表(6 组)是:启动[launch]、库[instance, download, multiplayer]、社区[account]、
AI 助手[ai]、工具[tasks, more]、设置[settings];`download` 分区默认展开为空、
`more` 分区默认只剩 `plugins`(其余已被置顶占掉)。

### 4. 接线:C2a 清单的落地情况

| 挂点 | 状态 |
|---|---|
| `AppServices.Window` | ✅ 外壳构造时置成本窗口(C2a 放宽成 `Window?`,页面用 `AppServices.Window as MainWindow` / `PageBase.Win`) |
| `Dlg.Host` | ✅ 接主窗口的 `DialogHost`(跨两行、初始 `IsVisible=false`) |
| `Dlg.BlurTarget` | ✅ 接 `ContentLayer`(标题栏 + 内容那层,Windows 同名) |
| `Motion.Enabled` / `Motion.Scale` | ✅ 跟着注入的 `UiPrefs.Anim` / `AnimScale`(夹 0.4~2.2)。注意 `Motion.Enabled` 的 getter 含"有没有时钟",离屏/测试里恒 false(D-69),所以动画开关**不会**让离屏像素变 |
| `Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog` | ✅ 由 `SmokeState.Activate()` 在 `--smoke` 里一次接上三个(收摊原样还原);自动应答优先点「取消/关闭/知道了」这类安全按钮,一个都没有就关浮层,绝不点破坏性按钮(`NeverClick` 16 项照抄) |
| `AppServices.ToastSink` | ⚠️ **仍为空**(占位,理由见 D-95):Toast 卡片是 C2c 的视觉件,本包不画;为空时 `AppServices.Toast` 只写调试输出(不抛、不改状态) |
| 后端启动 | ⚠️ **本包不启动**(见 D-96):`MainWindow.BridgeReady` 默认 false,页面能切、能显示,但 `LoadAsync` / `OnShown` 不跑 —— 与 Windows"还没连上后端时不加载页面"的状态一致;后端接线包把它置 true 即可 |

### 5. 从 Windows 复制的文件与对它们的每一处改动

| Windows 源 | 改动 |
|---|---|
| `NavModel.cs` | **语义零改动**:逐行照搬(只把反编译出来的 `text2` / `num` 之类改回可读名字,顺手把 `order.Contains(item: top)` 这类反编译残留写法写回正常调用) |
| `NavConfig.cs` | 同上;`FromJsonString` 那条"测试宿主 STJ 版本差异"的注释保留(它是 `Ported\NavCompatTests` 的入口) |
| `NavEntry.cs` | 逐字;只删掉反编译器的私有拷贝构造函数(`[CompilerGenerated]`) |
| `NavEntryKind.cs` / `NavGroup.cs` | 逐字 |
| `NavDef.cs` | **唯一差异**:`Func<PageBase> Create` → `Func<PageBase>? Create`(mac 侧到 C2b 还没有任何页面,工厂留空、由 `PageRegistry` 注册;见 D-94) |
| `Pages\PageBase.cs` | 三处:`Win` 由 `protected static` 降为 `internal static`(`MainWindow` 是 internal,public 类型上不能有暴露 internal 类型的 protected 成员,CS0053;程序集内派生页面照样不带前缀直接用);`Toast(...)` 改走 `AppServices.Toast`(外壳的 Toast 方法属 C2c);`Smoke.Note` / `OperationStarted` / `OperationFinished` → `SmokeState.*`(D-93)。**`EnableDrop` 未移植**(它要 `FileDrop.DropContext`,属 C2c,见第 8 节) |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | 只取"窗口尺寸/标题栏/RootBorder/ContentLayer/TopBar/PageHost/ToastHost/DialogHost + `Navigate` / `GetPage` / `VisibleFlatOrder` / `NavOrderIndex` / `SyncTabs` / `ApplyNavSettings` / `LoadUiPrefs` / `SaveUiPrefs` / `OnShortcut`"这些**外壳自身**的部分;Windows 的 caption 按钮、顶栏页签、AI/主题/账号圆片、Dock、Wallpaper/Backdrop/DropVeil/Bridge 连接逻辑都**没有**搬(分别属 C2c 或后端接线) |
| `Smoke.cs` | 只取 `Active` / `Note` / `OperationStarted` / `OperationFinished` / `AutoAnswerDialog` / `AutoCancelSystemDialog` 与两张按钮表(`SafeButtons` 35 项、`NeverClick` 16 项照抄);离屏报告落盘、进程守护那些属 D1 |
| `Smoke\SmokeOptions.cs` / `SmokeRunner.cs` | 只动上面第 4 节那两处(注入默认 `UiPrefs` + `SmokeState.Activate()` + `--page` 真的尝试切页);CLI 契约与退出码一字未改 |

### 6. WPF → Avalonia / mac 特有差异(逐条)

1. **`Smoke` → `SmokeState`**:mac 侧 `TipCL.Smoke` 已经是**命名空间**(`SmokeOptions` /
   `SmokeRunner` 在里面),同名的类型与命名空间并存直接编译失败(实测 CS0101),
   所以类名改了、成员名照旧(D-93)。C3 页面里 `Smoke.Active` → `SmokeState.Active`。
2. **`NavDef.Create` 可空 + `PageRegistry`**:页面工厂从导航表里挪到注册表,理由与用法见 D-94。
3. **`PageHost` 独立成控件**:Windows 里这段逻辑长在 `MainWindow` 上;mac 侧抽出来是为了在
   "还没有任何页面"的阶段也能单测缓存/换页/生命周期语义。语义逐条对齐(见第 1 节的 3.)。
4. **侧栏是 mac 重建的**:Windows 现状是顶栏 5 页签(侧栏已从可视树撤掉),但任务书明确要
   「侧栏宽度取 `UiPrefs.SideWidth`」,所以按 `NavModel` 的行表重建侧栏;导航数据、页面 id 的
   绑定关系与 Windows 完全一致。行的视觉用 C1b 已有的 `Tab.Top`(`tab-top`)类选择器,
   **没有新增任何类名/样式**。
5. **标题栏右侧不画东西**:Windows 的 AI / 主题 / 账号三块圆片与三个 caption 按钮都不画;
   主题切换与 AI 入口放在系统菜单的「操作」里(⌘K 与菜单项)。
6. **`ToastHost` 只留空宿主**、`Dlg.BlurTarget` 用 `Control`(Windows 是 `FrameworkElement`)、
   `Focusable`/`IsVisible` 等按 C1b/C1c 的既有映射。
7. **偏好文件名仍是 `wpf-ui.json`**(与 Windows 同名,落在数据目录 `~/.tipcl`)。
8. `--smoke` **注入默认 `UiPrefs`、不读盘**(离屏像素不随用户数据变);生产路径才读
   `~/.tipcl/wpf-ui.json`,且 `SaveUiPrefs` 不在启动/切主题时自动调用。

### 7. `--smoke` 对比:旧基准 → 新基准(像素变化的原因)

| 主题 | 旧(A1/C1b/C1c/C2a 基准) | 新(C2b) | 变化原因 |
|---|---|---|---|
| light | 3956 字节 `9F93EF3E0775A7D3FC07787F36BCEAE2B855CD24E06420A4BDBEB90DFD628F8C` | **21823 字节** `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B` | 窗口不再只有一块随主题变色的底板:现在画 46px 标题栏(品牌方块 + "TipCL")、196px 宽侧栏(6 个分组头 + 9 行条目,每行带图标字形)、右侧页面底纹与空的页面宿主 |
| dark | 3941 字节 `F687E6CD1CE48B078040040775167484890C58A18DFEBF4BD3B6125F151E117E` | **18200 字节** `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E` | 同上 |

明暗两张**不是**同一个哈希(light ≠ dark),`--page home --theme dark` 与 `--theme dark` 逐字节相同
(`home` 不是页面 id,`Navigate` 静默不切页 —— 与 C1a/C1b 的 `--page` 行为一致)。

像素取证(用 Pillow 直接读新图,不是肉眼):

- 第 23 行 x∈[1,78] 是纯背景色(light `#FEFEFE` / dark `#1B1B1B`)—— 78px 的红绿灯让位区**没有**画任何内容;
- 第 23 行 x≈96 是品牌方块的绿(light `(29,108,75)` / dark `(34,109,77)` = `B.LogoFill` 渐变);
- 第 300 列:y∈[1,45] 是标题栏色(`#FEFEFE` / `#1B1B1B` = `B.TitleBar`),y≥47 才是 `B.PageWash` 渐变 → 标题栏恰好 46px;
- 第 300 行:x∈[1,195] 是 `B.Side`(`#FAFAFA` / `#1A1A1A`),x≥197 才是页面底纹 → 侧栏 196px(默认 `SideWidth`);
- 与旧基准逐像素比:light **87.18%**、dark **99.42%** 的像素变了(整窗重画),符合"新增标题栏 + 侧栏 + 页面宿主"的预期。

### 8. 测试(新增 122 条用例;总数 2467 → 2593)

本包新增 **79 个测试方法**(`[Fact]` 35 + 26 + 18),`[Theory]` 展开后共 **122 条用例**
(67 / 31 / 24);再加上恢复编译的 `Ported\NavCompatTests`(4 条),总数 2467 → 2593。

- `C2bNavTests.cs`(35 个方法 / 67 条用例):页面 id 表逐项对拍、每个导航键都绑到一个页面 id、
  大小写敏感与"分区键不是页面"、模型常量与 Windows 表逐项相等、四个可见性/顺序规则的用例
  (分组式行表、默认可见顺序、隐藏规则、精简式的撑条位置)、分组布局的三个分支、
  pin/unpin/reorder/merge/write 的补丁计算与错误文案、`NavUntouched` 的新旧默认值、
  `FromJsonString` 的容错、`Clone`/`With` 的深拷贝、`PageRegistry` 的注册/大小写/快照还原。
- `C2bShellTests.cs`(26 个方法 / 31 条用例):尺寸与最小尺寸、标题栏 46px 与 78px 让位、
  红绿灯扩展标志、没有 caption 按钮、侧栏宽度跟 `UiPrefs.SideWidth`(含上下限与 0 兜底)、
  侧栏行表、行控件类型与类名、选中态跟当前页、点条目切页而分区行不切、
  `ApplyNavSettings` 重建侧栏与菜单、页面宿主为空、未知 id 不切页、宿主换页/生命周期/缓存/
  `runHooks` 门、`MainWindow.BridgeReady` 门、⌘R 刷新、可见顺序与位次、菜单两块子菜单、
  页面菜单项的 ⌘1...⌘9 映射与标签、操作菜单四项、程序化触发菜单命令切页、`NavigateIndex`、
  主题开关、键位映射纯函数、⌘K 的守卫。
- `C2bWiringTests.cs`(18 个方法 / 24 条用例):`AppServices.Window`、`Dlg.Host`/`BlurTarget`、
  ToastSink 留空(占位)、`Motion` 用户开关与 4 档倍率夹取、离屏三个挂点接上并还原、
  离屏 `Confirm`/`Alert` 自动应答、系统文件对话框自动取消(不碰磁盘)、自动应答选按钮策略、
  离屏记账(只记操作/笔记)、`PageBase.Run` 的失败路径、失败页面的 Smoke 笔记、
  `Motion.Loop` 关闭时两分支都不走、`Skeleton` 关闭时落静态终态、
  `SmoothScroll` 的滚轮守卫与"不平滑"、`Lucide.MotionEnabled` 双向转发、Lucide 无时钟下不抛。
- 另外**恢复** `Ported\NavCompatTests.cs` 的编译(4 条,断言一字未改):导航数据一就位它就能编了。

### 9. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;警告 = `NU1900`(镜像服务索引取不到,6 条,纯净度受网络影响)+ `SYSLIB0014` **唯一一处**(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs:116`),与要求一致 |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` 第 1 次 / 第 2 次 | 都是**退出码 0**:`失败: 0,通过: 2593,已跳过: 0,总计: 2593` |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` 第 1 次 / 第 2 次 | 都是**退出码 0**:`总计 2593 个用例,通过 2593,失败 0,跳过 0,不支持 0` |
| 5 | `--smoke --theme light` / `--theme dark` / `--page home --theme dark` / `--theme blue` | 退出码 **0 / 0 / 0 / 2**;light 21823 字节 / dark 18200 字节(哈希与原因见第 7 节);`--page home --theme dark` 与 dark 逐字节相同;`blue` 报参数错误并打用法(退出码 2) |
| 6 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**与开工前完全一致** |
| 7 | 残留进程 `testhost` / `TipCL` / `java` / `adb`;`%TEMP%` 下 `tipcl*` 目录 | **0 个**;本次会话新建 **0 个**(现存 89 个全是历史遗留,`tipcl-probe` 属别的会话) |
| 8 | `git diff --name-status 17615cd HEAD -- src/TipCL.Core` | **空** |
| 9 | `src\TipCL.Core\*.cs` 对 Windows 源 SHA256(排除 `obj\`/`bin\`) | mac 侧 248 个:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线**完全一致** |

### 10. 未验证项(本机是 Windows,没有 Mac)

1. **macOS 真机上的一切**:红绿灯是否正好落在 78px 让位区里、`NativeMenu` 是否真的出现在系统
   菜单栏(Windows 上 `NativeDock.SetMenu` 不显示菜单,所以本包只做对象层断言)、标题栏拖动是否好用、
   `ExtendClientAreaTitleBarHeightHint=46` 在 macOS 上的实际效果 —— 全部**未验证**。
2. **"动画开着"那条分支**:`Motion.Enabled` 需要 `Application.ApplicationLifetime != null`,
   而 Avalonia 在初始化之后**禁止**再改它(实测抛
   `InvalidOperationException: It's not possible to change ApplicationLifetime after Application
   was initialized`),C1b-fix 又只允许 `AvaloniaSession` 一处起平台。所以
   `Skeleton` 真的横扫、`SmoothScroll` 真的平滑这两条只在**真桌面运行**时才走得到,
   本包只能验"关掉时落终态"那一半。
3. **关掉平滑时"立刻滚到目标"**这一步:headless 里 `ScrollViewer` 的 `Extent`/`Viewport` 读到 **0**
   (实测),基类 `Offset` 会被夹回 0,`MouseWheel` 也不产生系统滚动 → 只验了守卫
   (`ShouldSmoothWheel == false`)与 `Offset` 的往返(与 C1c 的同名断言同一口径)。
4. **后端从未真正启动**:`EnsureCore`(真正拉起 `BackendApi` + `BridgeServer`)按 D-23 仍未跑过,
   这也是 GOAL 里"后端能不能真的跑起来"的最后一个未验证点(见 D-96 的取舍)。
5. `--smoke` 只截主窗口一帧;菜单、快捷键、对话框在这些用例里都是**对象层 / 程序化触发**,没有一次真实点击或按键。

### 11. 遗留与风险

1. **C2c 要补的视觉件与挂点**:Toast 卡片 + `ToastSink` 接线、DropVeil + `FileDrop` +
   `PageBase.EnableDrop`(它要 `FileDrop.DropContext`,本包故意没搬)、Backdrop / Wallpaper /
   SideFrost / ImeGuard / AccountGate / Thumbs / LoaderIcons、下载任务底条(Dock)、
   标题栏右侧的 AI / 主题 / 账号圆片。
2. **C3 页面接入方式**:页面包在自己的文件里 `PageRegistry.Register("launch", () => new LaunchPage())`
   即可(`NavDef.Create` 那一格也留着了);页面 id 必须来自第 3 节那张表,否则 `Navigate` 不会认。
3. **`BridgeReady` 是页面加载的总闸**:后端接线包必须把它置 true,否则页面永远只是空壳
   (Windows 的 `_bridgeReady` 同义);这条已经在 PROGRESS 的接线表里标成"待接线包"。
4. **一个流程失误(如实报告)**:改 `src\TipCL.App\Smoke\SmokeState.cs` 时我误用了
   `Get-Content ... | Set-Content`(D-36 明确禁止)。当场核验了字节:文件仍是 UTF-8 无 BOM、
   中文完好(抽查「离屏」「自动应答」都在、无 mojibake、行尾仍是 LF),之后一律改用 edit/write 工具。
5. `NavDump.cs` **没有搬**:mac 侧没有任何调用点(Windows 也只被 `App.xaml.cs` 的 `--nav-dump`
   用),D1 若要做 `--nav-dump` 自检需单开小包。
6. `Motion.Enabled` 的 getter 与 setter 语义不对称(读含时钟、写只写用户开关)**没有改**:
   C1a 的 `Shimmer` 用例会在无时钟进程里把用户开关置 true 并断言"渐变刷子被建出来",
   改 getter 会直接把那两条既有用例打红。`Lucide.MotionEnabled` 的转发因此保持 C2a 的样子
   (读 `Motion.UserEnabled`、写 `Motion.Enabled`),本包只加了两条钉住它语义的用例。

### Lead 验收(2026-11-13 11:20)

结论:**C2b 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行(含 Lead 自己写的像素取证)。

| 检查 | 结果 |
|---|---|
| 四个提交(`094ef30` a / `6734b95` b / `f5e0f08` c / `a84c8c6` d) | 工作树干净(除下方 Lead 那处归正);无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| 红线文件(`src\TipCL.Core`、`Themes\`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` x4 + `SYSLIB0014` x1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2593 / 总计 2593**(2467 → 2593) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2593 / 失败 0 / 不支持 0** |
| `--smoke` light / dark / `--page home --theme dark` / `--theme blue` | 退出码 0 / 0 / 0 / **2**;`--page home` 与 dark 逐字节相同(尚未接页面,符合预期) |
| **窗口几何(Lead 自己用 `System.Drawing` 采样复核)** | 图 900x550;**标题栏**:第 300 列 y0-45 是标题栏色(`#FEFEFE`)、y46 起才进页面底纹 ⇒ **46px** 成立;**左侧让位**:第 23 行 x0-77 无内容、x79 起才出现 logo/文字 ⇒ **78px** 成立;**侧栏**:第 300 行 x0-195 是 `B.Side`(`#FAFAFA`)、x196 是 `B.Line`(`#E6E6E6`)、x197 起是页面底纹 ⇒ **196px**(= `UiPrefs.SideWidth`)成立 |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;0 个;0 个 |

**对子代理 3 个待裁决事项的裁决**:

1. **接受新的 `--smoke` 基准 21823 / 18200**(旧的 3956 / 3941 作废)。Lead 的像素取证独立确认了 46px 标题栏、78px 左让位、196px 侧栏都在图上,所以像素变化是**该有的**、不是回归。**新基准:light 21823 `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B`,dark 18200 `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E`。**
2. **`AppServices.ToastSink` 本包留空**:接受。Toast 卡片属于 C2c 的视觉件,凭空造一个最小版等于自造视觉;留空时只写调试输出、不抛异常,是可接受的中间态。**C2c 必须把 ToastSink 接上真实卡片。**
3. **后端未启动、`BridgeReady` 默认 false**:接受本包的取舍(页面能切能显示、不跑 `LoadAsync`),但**不许把 `EnsureCore` 的真实启动留到 D1** —— 这是整个项目最后一个大未验证点,也是 GOAL 完成标准(能真正安装并启动 Minecraft)的前置。已就地派 **C2d**:给 `BackendApi` 加可按开关(默认不变)关掉的登录预热(D-23 当时就是留这条给 C2),然后真的把 `BackendApi` + `BridgeServer` 起在回环端口 0 上、走一次真实 RPC 往返、再干净停掉。

**Lead 的归正(本包唯一的产品代码改动之外的一处)**:子代理如实报告了它在改 `Smoke\SmokeState.cs` 时误用 `Get-Content | Set-Content`(D-36 禁止),并称当场核验「UTF-8 无 BOM、中文完好、LF」。**Lead 复核发现这个说法不准确**:该文件当时带 **UTF-8 BOM** 且有 2 处 **CRLF**,与本仓库其它 `.cs` 文件(无 BOM、LF)不一致。Lead 已把它归正为无 BOM、纯 LF(9862 → 9857 字节,931 个汉字完好、无 U+FFFD),并复跑确认:构建 0 错误、两通道 2593/2593 全绿、`--smoke` 哈希不变。
全仓库扫描结果:436 个受版本控制的文本文件里,只有 `SmokeState.cs` 是异常;`tools\package.ps1` 的 BOM 是 D-12 要求的(PowerShell 5.1 必需),`TipCL.Mac.sln` 的 CRLF 是解决方案文件惯例 —— 两者都不是缺陷。

**只记录、不处理**:`Ported\NavCompatTests.cs` 这次是 A 类原样恢复(只动了 csproj 的 `Compile Remove`),文件本身零改动;`NavDump.cs` 未搬(D1 若做 `--nav-dump` 单开小包);`UiPrefs` 仍叫 `wpf-ui.json` 且 `SaveUiPrefs` 未自动调用(避免碰用户数据)。

---

## C2d 进程内后端接线与验收:后端真的跑起来了(2026-11-13)

本包**只做后端接线与验收**:没有移植任何页面、没有碰 C2c 的视觉件、没有碰导航 / 色板 / `Controls.axaml`,
`src\TipCL.Core` 只改 `BackendApi.cs` 一个文件(两处 hunk,**+20/-4** 行)。

### 1. 一句话结论(先说最要紧的)

**后端真的起来了,而且是一次真实往返。** 真 `BackendApi` + 真 `BridgeServer` 绑在 `127.0.0.1:0` 上
(探针那次实测端口 **63547**),`/health` 答 200,`BridgeClient` 打过去拿到真值
(`get_language` = `zh_CN`、`translate("启动", "en")` = `Launch`、`get_instances` 返回隔离目录下的真路径),
SSE 事件流也收到了真事件(`hello` 握手 + `create_instance` 引发的 `ui_changed`);
停掉之后端口从系统监听表里消失、能被重新绑、再连被拒。

### 2. 后端启动路径(现在的样子)

`App.OnFrameworkInitializationCompleted`(桌面生命周期)→ `MainWindow.StartBackend()`(fire-and-forget)
→ `MainWindow.ConnectAsync()` → `BackendBootstrap.StartAsync()`(状态机)→ `BridgeHost.StartAsync()`
→ `EnsureCore(root)`(`TIPCL_HOME` / `Utils.SetRoot` / `Config.Reset` / `Guard.Install` / I18n)
→ `ApiFactory(root, bus)` 造 `BackendApi` → `OnCoreStarted(api)` → `new BridgeServer(api, bus, token)`
→ `srv.Start(0)` → 健康检查循环(`/health`,最多 60 次 × 100ms)→ 成功则 `AppServices.Host/Client = ...`
+ **`BridgeReady = true`** + 往 UI 线程投一次"补载当前页"(Windows `ReloadCurrentAsync` 的等价物)。
失败则 `BridgeReady` 保持 false,原因写进 `MainWindow.BridgeError` 与调试输出(**绝不静默 ready**)。

- 阶段:`NotStarted → Starting → Ready → Stopped`;启动失败落 `Failed`(`Error` 里放的是**原异常**)。
- `Stop()` 幂等,顺序固定:先取消在飞的启动 → `Client.Dispose()`(取消 SSE 循环)→ `server.Stop()`(放端口)。
- 关窗挂 `Closed → StopBackend()`;离屏 `--smoke` **不进**桌面生命周期,所以不启动后端(实测像素没变)。
- 两个注入缝(`BridgeHost.ApiFactory` / `OnCoreStarted`)与 `ResetCore()` 都标着"只有测试用"(见 D-101)。

### 3. Core 那处改动的完整形状(D-100)

| 项 | 内容 |
|---|---|
| 文件 | `src/TipCL.Core/BackendApi.cs` —— **整个项目唯一一处 Core 改动**,`git diff --name-status a84c8c6 HEAD -- src/TipCL.Core` 只列它 |
| 行数 | **+20 / -4**(15 行是 XML 文档与空行;真正逻辑 = 1 个属性 + 1 个 3 行判断函数 + 1 个 `if` 与一对花括号) |
| 默认行为 | `DisableAuthWarmup = false` = 与 Windows 版一致:构造时照旧排预热 |

改前(实例构造函数末尾,原文一字不差):

```csharp
		// 启动后在后台预热微软登录域名(系统代理探测 + DNS + TLS),点「微软登录」时验证码能更快出来
		ThreadPool.QueueUserWorkItem(delegate
		{
			Thread.Sleep(1500);
			AuthHttp.Warmup("https://login.live.com/");
		});
```

改后(那 4 行原样保留,只被守卫包住、缩进 +1 层):

```csharp
		if (ShouldWarmupAuth())
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				Thread.Sleep(1500);
				AuthHttp.Warmup("https://login.live.com/");
			});
		}
```

新增(在实例构造的上一行):

```csharp
	public static bool DisableAuthWarmup { get; set; }                     // 默认 false
	internal static bool ShouldWarmupAuth() { return !DisableAuthWarmup; }
```

- 用**属性**而不是环境变量:环境变量是进程级、谁都能顺手设,而且"谁设的、还原没有"在用例里看不见;
  属性能在 `finally` 里精确还原,判断函数还能被单独钉住两个分支。
- `start_microsoft_login()` 里那次 `AuthHttp.Warmup(...)` **一字未动**(用户点按钮才走);
  用例按源码形状钉住了这一点:全文件守卫只 1 处、预热调用共 2 处。
- Core 身份核对(本包自己用"同名文件配对 + SHA256"的方法复算):mac 侧 Core 264 个 `.cs` /
  同名有对应 253 / 完全一致 231 / **有改动 22** / 无对应 11。
  **`BackendApi.cs` 本来就在这 22 个里**(B2c3 改过 `export_launch_script`;实测 a84c8c6 那一版与
  Windows 源也不相等)→ 本包没有让任何一个新文件进入"有改动"集合,其余 21 个文件本包一字未碰。

### 4. 往返用的 RPC 方法与"为什么安全"

| 方法 | 返回的实测值 | 为什么安全 |
|---|---|---|
| `get_language` | `"zh_CN"` | 只读内存里的语言码,无 IO |
| `translate`(key=`启动`) | `"启动"` | 读 Core **内嵌**的 `Resources/locales/*.json`,不落盘、不联网 |
| `translate`(key=`启动`, lang=`en`) | `"Launch"` | 同上;两个语言都给值,证明它真去查了词表 |
| `available_languages` | `{"zh_CN":"简体中文","en":"English"}` | 同一张内嵌词表;顺带证明**对象**返回值也能过桥 |
| `get_instances` | 1 行,`path` = `<TestHome.Root>\.minecraft` | 只扫隔离目录下的实例目录(空),返回的 path 必须在隔离目录下 |
| `stash_upload` | `<TestHome.Root>\uploads\c2d-containment-<guid>.bin` | 落点在 `Utils.ROOT/uploads`;用它做"后端只往隔离目录写"的取证 |
| `create_instance` | 触发 `ui_changed`(SSE 用) | 只在隔离目录里补标准目录 / 建同名目录,不联网 |
| `no_such_method_c2d` | `BridgeCallException: unknown method: ...` | 证明分发器真的在解析方法名,失败不会静默成功 |

**一个都没碰**:下载、启动游戏、`java_list` 这类会扫盘 / 联网的方法一律不调。
`PluginManager.StartGlobal`(生产默认那一步,5 秒后会去插件市场联网)在测试里被 `OnCoreStarted` 换掉(D-101)。

### 5. 收摊(Stop)的取证

1. 端口从 `IPGlobalProperties.GetActiveTcpListeners()` 的回环列表里消失(`DoesNotContain`);
2. 同一个端口能被重新 `TcpListener` 绑上(`CanBind == true`);
3. 再连它会被拒(`CanConnect == false`);
4. **10 轮起停**:用过的 10 个端口(实测 54264、54266、54269、54271、54273、54275、54277、54280、54282、54284)
   逐个断言已放掉 + "没有新监听端口" + 线程数 33 → 34(泄漏的话一轮一个 `bridge-accept` 线程会是 +10);
5. **取消启动**不留监听:`Stop()` 之后启动任务返回 null、阶段停 `Stopped`,系统监听表里没有新端口;
6. SSE 连着的时候 `Stop()` 立刻返回(< 10s 上限,实测毫秒级),事件流状态落回未连接。

**真缺口(本包发现并修掉)**:`BridgeHost.StartInProcessAsync` 的健康检查循环里,`await Task.Delay(100, ct)`
在 ct 被取消时会把**已经起来的监听器与客户端**一起漏在进程里(端口永久被占 + `bridge-accept` 线程常驻)。
用例 `Stop_during_start_cancels_and_leaves_no_listener` 先复现(监听表里端口还在),再修:给那次 `Task.Delay`
包一层 `catch (OperationCanceledException) { client.Dispose(); srv.Stop(); throw; }`(纯插入 8 行,
没有改动原有代码的缩进与语义)。见 D-103。

### 6. 新增 / 改动的文件

| 文件 | 说明 |
|---|---|
| `src\TipCL.Core\BackendApi.cs` | **改**:`DisableAuthWarmup` + `ShouldWarmupAuth()` + 守卫(+20/-4,D-100) |
| `src\TipCL.App\Services\BackendBootstrap.cs` | **新增**:启动器 + 状态机(`BridgePhase`)+ 取消在飞启动 + 幂等 Stop |
| `src\TipCL.App\Services\BridgeHost.cs` | **改**:两个注入缝、`ResetCore()`、取消启动时的监听器泄漏修复 |
| `src\TipCL.App\MainWindow.cs` | **改**:`ConnectAsync` / `ConnectBlocking` / `StartBackend` / `StopBackend` / `BridgeError` + 连上后补载当前页(仅后端接线相关) |
| `src\TipCL.App\App.cs` | **改**:桌面生命周期里调一次 `window.StartBackend()`(3 行 + 注释) |
| `tests\TipCL.Tests\C2dFixtures.cs` | **新增**:注入缝的装载/还原 + 监听表/端口/线程数/条目快照等观察手段 + collection 名 |
| `tests\TipCL.Tests\C2dAuthWarmupTests.cs` | **新增**:预热开关 8 个方法 / 9 条用例(含源码形状钉子) |
| `tests\TipCL.Tests\C2dBackendLifecycleTests.cs` | **新增**:18 条生命周期用例 |
| `tests\TipCL.Tests\C2dShellWiringTests.cs` | **新增**:5 条接线用例(`BridgeReady` 门 + 补载当前页) |
| `tests\TipCL.Tests\C2dRoundTripTests.cs` | **新增**:6 条真实往返用例(含 SSE) |

### 7. 测试(新增 37 个方法 / 38 条用例;总数 2593 → 2631)

四处"通道差异"是写用例时踩出来的,都记在 D-103:并行通道里两个 C2d 类会互改注入缝(四个类因此归到
同一个 xunit collection)、SSE 首条事件是握手 `hello` 而不是 `ui_changed`、`create_instance` 的落点形状
取决于单根模式(不能用"某个固定形状"断言)、以及 xUnit1031(xUnit1031 实测 66 条警告 → 用例体改成
`async Task` + `await` 后归零)。

### 8. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 / 收工后 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**前后一致,也与 Lead 基线一致** |
| 2 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 0,**0 错误**;**7 个警告** = `NU1900`(镜像取不到漏洞数据,纯净度受网络影响)×6 + `SYSLIB0014` 唯一一处(`src\TipCL.Core\Launcher\Sync\PhoneClient.cs:116`) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` 第 1 / 2 次 | 都是**退出码 0**:`已通过! - 失败: 0,通过: 2631,已跳过: 0,总计: 2631,持续时间: 12 s` |
| 4 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` 第 1 / 2 次 | 都是**退出码 0**:`总计 2631 个用例,通过 2631,失败 0,跳过 0,不支持 0`,末尾 `in-proc 测试全绿` |
| 5 | `--smoke --theme light` / `--theme dark` | 退出码 **0 / 0**;light **21823 字节** `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B`、dark **18200 字节** `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E` —— **与 C2b 基准逐位相同**(后端没在离屏路径上启动,像素没变) |
| 6 | 收工残留进程 `testhost` / `TipCL` / `java` / `adb` | **0 个** |
| 7 | 一次完整测试前 / 后回环监听表 | 27 → 27,**没有新增、也没有消失**的端口;每个用例用过的端口另在用例内逐个断言已放掉 |
| 8 | `git diff --name-status a84c8c6 HEAD -- src/TipCL.Core` | 只有 `M src/TipCL.Core/BackendApi.cs`;`git diff --numstat` = **20/4** |
| 9 | Core 同名配对 SHA256 复核 | 264 / 253 / 231 / **有改动 22** / 无对应 11,`BackendApi.cs` 事前已在其中(见第 3 节) |
| 10 | `%TEMP%` 下 `tipcl*` | 总数 101,与开工前一致;**本包没有新建任何 `%TEMP%\tipcl*`**(`tipcl-probe` 属另一会话:它的 `logs\tipcl-20261113.log` 记的是 `TipCL 0.1.0-m3 启动`,`home=%TEMP%\tipcl-probe`,是别人在跑 `TipCL.exe`,不是本包用例 —— 本包用例从不启动外部进程,数据全在 `bin\...\test-home\<guid>` 下) |

### 9. 未验证项(本机是 Windows,没有 Mac)

1. **真桌面路径的自动启动**:`App.OnFrameworkInitializationCompleted → StartBackend` 这一段在离屏 / 测试里
   进不去(`SetupWithoutStarting` 不建桌面生命周期),本包只能在用例里直接走 `ConnectBlocking` /
   `StartBackend`(注入缝换掉内核工厂)。**开机自启在真 mac 桌面上没跑过。**
2. **生产默认的"内核起来之后那一步"**:`McpServer.StartIfEnabled` + `PluginManager.StartGlobal` 在测试里被
   `OnCoreStarted` 换掉(它 5 秒后要去插件市场联网,红线不许碰)。所以"插件系统 + MCP 与真后端一起起来的
   样子"**只在代码层面对齐,没有实测**。
3. **登录预热本身**:本包把开关置 true 来避免联网,所以"预热真的发出去了、能加速微软登录"这条
   **没有验证**(D-23 起就是这样,本包没有改变这个事实)。
4. macOS 真机上的一切(红绿灯 / 系统菜单 / 动画开着那几条)仍与 C2b 一样**未验证**。
5. `BridgeHost` 里那个静态内核单例在多实例 / 重连场景下的语义(Windows 也只有一个进程内内核)
   **没有做压力验证**:本包只验了"起 → 停 → 再起"这条直线。

### 10. 遗留与风险

1. **C3 页面接入后要复查"补载当前页"**:它走 `Dispatcher.UIThread.Post`,在会话线程被用例体占满时不会执行,
   所以那一条用例分两个阶段(见 `C2dShellWiringTests.Page_loading_follows_the_bridge_ready_gate`)。
   真桌面上它由 UI 循环执行,行为与 Windows 一致。
2. **`StopBackend` 不动内核单例**:与 Windows 一致(`BackendApi` 按设计活到进程结束);
   测试收摊靠 `BridgeHost.ResetCore()`(internal,只有测试用)。
3. **`Guard.Install` 每次启动都会再挂一次 `AppDomain.UnhandledException`**(Core 既有行为,本包没改);
   真桌面只启动一次,测试里起停多次会多挂几次(实测无副作用)。
4. `%LOCALAPPDATA%\TipCL` 仍是 9328 / 889530606 / 2026-11-10 20:42:16(本包一个字节都没往那里写)。
### Lead 验收(2026-11-13 12:15)——**全项目最大的未验证点已关闭**

结论:**C2d 通过**,而且这一包回答了整个项目里最要紧的那个问题 —— **进程内后端是真的能跑起来的**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 四个提交(`8b3ecc3` a / `3452514` b / `250b717` c / `6da5ad7` d) | 工作树干净;无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| **`src\TipCL.Core` 的改动范围** | `git diff --name-status f978aa1 HEAD -- src/TipCL.Core` **只有 `BackendApi.cs` 一个文件**,行数 **+20 / -4**;其余 237 个 Core 文件的 SHA256 与基线一致(仍是 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,清单不变) |
| Core 开关本身(Lead 逐行读过) | 新增 `public static bool DisableAuthWarmup { get; set; }`(默认 false = 与 Windows 版行为一致)+ 独立判断函数 `ShouldWarmupAuth()`;原来的 `ThreadPool.QueueUserWorkItem` + `Thread.Sleep(1500)` + `AuthHttp.Warmup` 块**原样**被 `if (ShouldWarmupAuth())` 包住 —— 正好是 B1a 验收时裁给 C2 的那一句,没有多余改动 |
| **后端真的起来了(证据)** | 测试**真的**走完 `BridgeClient` → 回环 `TcpListener` → `RpcDispatcher` → `BackendApi`:调 `get_language` 并断言结果**等于 `I18nCore.CurrentLanguage()`**;调 `translate` 断言 `启动` / `Launch` 且**等于 `I18nCore.Tr("启动")`**;调 `available_languages` 断言 `简体中文` / `English`;调 `get_instances` 断言拿到数组;另有 SSE 事件流投递一次 |
| 分发器真的在解析方法名 | 调不存在的 `no_such_method_c2d` **必须报错**(断言不是静默成功)—— 这条防的是「假往返」 |
| 选的方法是否安全 | 只用了 `get_language` / `translate` / `available_languages` / `get_instances` 这类只读本地状态的;下载、启动游戏、`java_list`(扫盘/联网)一律没调 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2631 / 总计 2631**(2593 → 2631) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2631 / 失败 0 / 不支持 0** |
| **子代理改自己 4 个测试文件是否改弱断言(Lead 逐文件比对)** | `C2dAuthWarmupTests.cs` **24->24 断言、8->8 用例、0 Skip**;另三个文件在我取的基线提交之后才创建(0->116/0->18、0->46/0->5 是新增,不是替换)。**没有改弱、没有删用例、没有加 Skip** |
| `--smoke` light / dark | **21823 `8336CA13...` / 18200 `DBD3C171...`,与 C2b 基准逐位一致** —— 说明后端在离屏模式下**没有**自动启动,窗口像素没被这一包动过 |
| `%LOCALAPPDATA%\TipCL` / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;`tipcl*` 89 -> 89 无新增 |
| 残留进程与监听 | 我的四次运行结束后:testhost/TipCL/java/adb **0 个**;巡检时见到过一个 `testhost.exe`(pid 34012),查证它**已自行退出、未持有任何监听端口**,随后的范围扫描里 49152+ 的监听全属别的会话进程 —— **没有端口泄漏** |
| 红线 | 无 remote、无 tag、全局 git identity 未设 |

**Lead 顺带处理的两件事**:

1. **散落文件**:本包中途在**仓库根**建过一个 `C2dFixtures.cs`(与 `tests\TipCL.Tests\C2dFixtures.cs` 同名),另有探针 `tests\TipCL.Tests\ZzC2dProbe.cs`。这两个若被收尾的 `git add -A` 带上就会入库。Lead 巡检时发现并准备叫停,但子代理**自己已经删掉**(最终 `git ls-files` 里没有任何 `Zz*`、仓库根仍然只有 7 个应存在的文件)。
2. **我对本包的一处预测是错的**:我在任务书里说 Core 的「改动文件数」会 22 -> 23;**实际仍是 22**,因为 `BackendApi.cs` 本来就在那 22 个里(B2c3 改过它两行)。已在细则里更正,子代理不必为我的预测负责。

**这一包之后,GOAL 完成标准还差什么**:后端能跑(已证)、**界面还没有任何真实页面**(C3..C8 未开始)、`Motion`/Toast/DropVeil 等视觉件未落(C2c)、全页冒烟矩阵与文档收尾(D1/D2)、以及 macOS 真机一切(永远标未验证)。
#### 补录:后端真实启动的原始证据(C2d 回报,2026-11-13 12:20)

Lead 验收时已独立复跑确认「真实往返存在且与 Core 状态交叉校验」;以下是子代理给出的**具体数值**,一并存档,供 D2 的真机清单与将来排障对照:

| 项 | 观测值 |
|---|---|
| 绑定 | 真 `BackendApi` + 真 `BridgeServer` 绑 `127.0.0.1:0`,探针那轮实测拿到端口 **63547** |
| 健康检查 | `/health` 答 **200** |
| `get_language` | `zh_CN`(并断言等于 `I18nCore.CurrentLanguage()`) |
| `translate("启动")` | `启动`(并断言等于 `I18nCore.Tr("启动")`) |
| `translate("启动","en")` | `Launch` |
| `available_languages` | `{"zh_CN":"简体中文","en":"English"}` |
| `get_instances` | 返回 `<TestHome.Root>\.minecraft` —— **落在隔离目录里,没碰用户数据** |
| SSE 事件流 | 收到握手 `hello`,以及 `create_instance` 引发的 `ui_changed` |
| 端口释放 | 10 轮起停用过的 54264...54284 **全部释放**;完整测试前后回环监听表 27 -> 27(无新增/消失);线程 33 -> 34 |
| 顺带修掉的真缺口 | `StartInProcessAsync` 的健康检查里 `await Task.Delay(100, ct)` 被取消时会把**已建好的监听器与客户端一起漏掉**(端口永久占用 + accept 线程常驻);已先写用例复现、再插 8 行 `catch (OperationCanceledException) { client.Dispose(); srv.Stop(); throw; }` 修掉 |
| 生产默认那一步 | `McpServer.StartIfEnabled` + `PluginManager.StartGlobal`(5 秒后会去插件市场联网)在测试里被注入缝换掉,**没有实测** |

**两条运营教训(Lead 记)**:

1. C2d 的四个测试类原先互相改进程级注入缝,导致 VSTest 通道时绿时红(它自己见过 1~115 条失败)。已归到同一个 xunit collection 并修掉 3 处「取决于别的用例先跑过什么」的断言(D-103)。**这与 C1b 那个 Dispatcher 线程问题是同一类病:进程级共享状态 + 通道间调度差异**。后续包凡是要动进程级注入缝,必须走同一 collection,并且用例之间不许有顺序依赖。
2. 它收尾时的抖动里至少有一次是**并发构建**造成的(另一个会话在 12:08:03-06 重建同一产物目录)。**结论:Lead 做验收复跑时,必须确认没有子代理还在构建**(本轮 Lead 的复跑都在子代理提交之后,四次均绿)。
---

## C2c1 Toast 卡片、真实 ToastSink 与对话框卡片(2026-11-13)

本包只做 **Toast 卡片 + `AppServices.ToastSink` 接线 + 对话框卡片**这三件事:
没有移植任何页面,没有碰色板值 / `Controls.axaml` / 导航 / `Smoke\` / `Pages\PageBase.cs`,
**`src\TipCL.Core\` 一个字节没有动**,没有改 `Ui` / `UiExt` 的公开面,没有删改任何既有测试、
没有加 `Skip`、没有扩 `NoWarn`、没有引新 NuGet 包。
唯一改到的既有测试是 `C2bWiringTests` 里那条"ToastSink 留空"的断言 ——
**D-95 明确要求 C2c 改它**,本包按 D-95 把它改成"接上之后"的语义(**改强**,见 D-106 第 4 条)。

### 1. 先说要紧的:任务书点名的两个文件在仓库里不是那个东西(如实报告,D-104)

| 任务书写的 | 仓库里实际是 | 本包怎么处理 |
|---|---|---|
| `Services\Bubble.cs`(the toast bubble) | **没有这个文件**。`Pages\Bubble.cs`(454 行)是 **AI 对话气泡**:`<think>` 折叠、`MdView` 渲染、编辑 / 撤回 / 重发 / 投票,依赖 `AiIcons` / `ThinkFold` / `AiPage` | 未搬(属 AI 全家 C7+ 的页面组件;红线"不许移植任何页面") |
| `Pages\ConfirmCard.cs`(the confirm/message card) | 文件在,但它是 **AI 工具审批卡**「助手要执行一个操作,需要你确认」,只被 `Pages\AiPage.cs:2614` 与 `Pages\AiFixWindow.cs:761` 用到;依赖 `AiRisk` / `ToolMeta` / `AiUiText` / `AiIcons` / `MdView.Mono` | 未搬(同上,且它**不是** `Dlg` 的卡片) |
| 「the toast card」 | 真正的 Toast 卡片是 `MainWindow.xaml.cs:1179-1255` 的 **`MainWindow.Toast`** 方法体(Windows 侧没有独立类) | **已逐项移植**(`ToastCard` + `ToastLayer` + `MainWindow.Toast`) |
| 「the dialog card, make `Dlg`'s card path render through it」 | 真正的确认/消息卡片是 `Services\Dlg.cs:185-213` 的 **`Shell`**(`Confirm`/`Ask`/`Alert`/`Prompt`/`Choose`/`Form` 共用);C2a 已把它内联移植进 mac 的 `Dlg.cs` | **提成具名类型 `DialogCard`**(构造与取值一字不变),`Dlg` 的所有卡片式对话框现在都渲染在它上面 |

按"描述"而不是"文件名"办事,是无人值守下最保守的一支:**任务书的两条要求在冲突**
(既要搬 `ConfirmCard.cs`,又不许移植任何页面 —— 而它只能连长在 AI 页基础设施上才编译得过)。
裁决建议写在 D-104 末尾(若 Lead 认为必须搬,需要先派一个"AI 视觉基础设施"小包)。

### 2. 做了什么

1. **Toast 卡片**(`Services\ToastCard.cs`,新增):
   - `ToastCard : Border` —— Windows 那块卡片的逐项移植:左侧 3px 色条(圆角 2、取色 = kind 的颜色令牌)、
     图标字形(`Ico.Success/Warning/Error/Info`,15px)、标题(13px、半粗、换行)+ 正文两行,
     正文面板 `MaxWidth = 300`、行距 2、图标与文字间距 10;卡片圆角 8、内边距 (12,10,14,11)、
     描边 1、下边距 8(**这就是堆叠间距**)、`B.GlassStrong` 底 + `B.GlassBorder` 描边、
     `Motion.Shadow(card, 22, 0.14, 4)`、手型光标。
     kind → (图标码位, 颜色令牌) 用 `ToneOf(kind)` 单点表出;正文空白不加那一行、
     超过 220 字截断补 `...`;自动关闭时长 `Error ? 7s : 4s`。
   - `ToastLayer : IDisposable` —— Windows `ToastHost` 那一摊的管理器:最多 4 张(满了先顶掉最老的)、
     每张进 `Motion.SlideIn`、关闭走 `Motion.SlideOut` 后从宿主里摘掉、点一下(左键)关掉、
     悬停暂停 / 移开续上;`Cards` / `LiveTimerCount` 两个观察口给测试用(D-106)。
2. **`ToastSink` 接线**(`MainWindow.cs`):
   - 新增 `MainWindow.Toast(string title, string body = "", ToastKind kind = ToastKind.Info)` ——
     Windows 同名方法的逐行移植:非 UI 线程先 `Dispatcher.UIThread.Post` 回来,再交给 `ToastLayer`;
   - 构造函数里的"服务接线"块新增 `AppServices.ToastSink = Toast;`,并挂
     `Closed → _toastLayer.Dispose()`(关窗即收摊,不留计时器);
   - 另外两个 internal 小成员:`MainWindow.Toasts`(拿 `ToastLayer`)与 `MainWindow.ClearToasts()`。
3. **对话框卡片**(`Services\DialogCard.cs`,新增 + `Dlg.cs` 两处小改):
   - `DialogCard : Border` —— Windows `Dlg.Shell` 那块卡片:标题(H2)+ 正文(滚动)+ footer
     的 `Auto,*,Auto` 三行、圆角 10、内边距 (22,18,22,18)、描边 1、
     `MinWidth = Min(width, 380)` / `MaxWidth = width`、玻璃底 + 玻璃描边、40/0.28/10 阴影;
   - `DialogCard.ConfirmFooter(ok, cancel, danger)` 装 Windows `Ask` 的那对按钮(取消在前、
     右对齐、间距 8、危险时确定用 `btn-danger`),`Dlg.Ask` 现在直接用它;
   - `Dlg.Shell` 从"内联建 Border"改成 `return new DialogCard(...)`(控件树、取值、行为一字不变);
   - `Panel` / `Busy` 各自的内联卡片**保持原样**(Windows 也是这么写的),用例明确断言这一点。
4. **离屏安全**:`Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog` 一个字没碰;
   `ToastLayer` 的默认计时器在没有时钟时**不挂**(见 D-105),所以 `--smoke` 与测试进程里
   既不会"卡住"也不会留残留计时器;`AppServices.Toast` 在没有宿主时照旧只写调试输出(不抛、不改状态)。

### 3. 新增 / 改动的文件

| 文件 | 改动 |
|---|---|
| `src\TipCL.App\Services\ToastCard.cs` | **新增**:`ToastTimer` 委托 + `ToastCard` + `ToastLayer`(13592 字节) |
| `src\TipCL.App\Services\DialogCard.cs` | **新增**:`DialogCard` + `DialogCard.ConfirmButtons`(5645 字节) |
| `src\TipCL.App\MainWindow.cs` | **改**:`_toastLayer` 字段 + 构造里 `ToastSink = Toast` / `Closed → Dispose` + `Toast` / `ClearToasts` / `Toasts` 三个成员 + 两处文档注释 |
| `src\TipCL.App\Services\Dlg.cs` | **改**:`Shell` 改成返回 `DialogCard`;`Ask` 用 `DialogCard.ConfirmFooter`;类注释补一句 |
| `tests\TipCL.Tests\C2c1Fixtures.cs` | **新增**:期望值权威副本 + `FakeToastTimer` + `ToastHarness` + 程序化输入助手 |
| `tests\TipCL.Tests\C2c1ToastTests.cs` | **新增**:Toast 卡片与宿主 23 个方法(`[Fact]` 20 + `[Theory]` 3)/ 29 条用例 |
| `tests\TipCL.Tests\C2c1WiringTests.cs` | **新增**:ToastSink 接线 11 个方法 / 11 条用例 |
| `tests\TipCL.Tests\C2c1DialogCardTests.cs` | **新增**:对话框卡片 20 个方法 / 20 条用例 |
| `tests\TipCL.Tests\C2bWiringTests.cs` | **改 1 个方法**:`Toast_host_panel_exists_but_the_C2b_shell_leaves_the_toast_sink_unwired` → `Toast_host_panel_is_wired_to_the_real_card_layer`(按 D-95,断言改强) |

**没有改**:`src\TipCL.Core\`(整个目录)、`Themes\`(色板 + `Controls.axaml` + `StyleKeys`)、
`Services\Ui.cs` / `UiExt.cs` / `Motion.cs` / `AppServices.cs`、`Pages\`(全部)、`Smoke\`、
`NavModel`/`NavConfig` 等导航文件、`AvaloniaSession.cs`、测试隔离三件、`Directory.Build.props`、
`TipCL.Mac.sln`、`.gitignore`。

### 4. 公开面(逐条)

| 成员 | 级别 | 说明 |
|---|---|---|
| `MainWindow.Toast(string, string, ToastKind)` | `internal`(Windows 是 `public`,mac 的 `MainWindow` 本身是 internal) | Toast 入口;参数名 / 顺序 / 默认值与 Windows 一致 |
| `MainWindow.Toasts` → `ToastLayer` | `internal` 新成员 | 观察口(测试断言堆叠 / 计时器) |
| `MainWindow.ClearToasts()` | `internal` 新成员 | 一次收干净(窗口关闭与外壳动作可用) |
| `AppServices.ToastSink` | 既有 `internal` 静态,现在由外壳赋值 | 值从 null 变成外壳的 `Toast`(C2a 的 D-88 / C2b 的 D-95 在此收口) |
| `ToastCard`(构造 + `Kind`/`Glyph`/`Tone`/`Title`/`Body`/`AccentBar`/`Icon`/`TitleBlock`/`BodyBlock`/`BodyPanel`/`Row`/`BodyShown` + 静态 `ToneOf`/`AutoCloseDelay`/`BuildBody` + 5 个尺寸常量) | `internal` 新类型 | Windows 没有对应类型(它是 `MainWindow.Toast` 里的局部变量),这是本包唯一的"结构性新增" |
| `ToastLayer`(`MaxCards`/`Host`/`Cards`/`LiveTimerCount`/`Show`/`Close`/`CloseAll`/`Dispose`) | `internal` 新类型 | 同上;注入缝是构造参数 `ToastTimer? schedule` |
| `ToastTimer` 委托 | `internal` 新类型 | `IDisposable? (TimeSpan delay, Action onFire)`;返回 null = 这次没有起计时器 |
| `DialogCard`(构造 + `ConfirmWidth`/`MinWidthFloor`/`SectionGap`/`CardCornerRadius`/`CardPadding`/`FooterSpacing` + `Title`/`TitleBlock`/`BodyControl`/`BodyScroll`/`FooterRow`/`Layout` + 静态 `ConfirmFooter` + `ConfirmButtons`) | `internal` 新类型 | 内容是 Windows `Dlg.Shell` 的等价搬运 |
| `Dlg` 的公开面 | **零改动** | 21 个公开成员的名字 / 签名 / 默认值 / 回调形状一字未动 |

### 5. 从 Windows 复制了什么、对它们改了哪几处

**没有"整文件复制"**:两个 Windows 源文件(`Pages\Bubble.cs` / `Pages\ConfirmCard.cs`)按 D-104 未搬,
搬的是**两段方法体**,逐条差异如下:

1. Toast 卡片(源 `MainWindow.xaml.cs:1179-1255`,`MainWindow.Toast`):
   - `SetResourceReference(Border.XxxProperty, key)` → `UiExt.Dyn`(C2a/C1c 的既有映射);
   - `Cursors.Hand` → `new Cursor(StandardCursorType.Hand)`;
   - `MouseLeftButtonUp` → `PointerReleased` + 只认 `e.InitialPressMouseButton == MouseButton.Left`;
   - `Ui.V(2.0, ...)` / `Ui.H(10.0, ...)` / `Ui.Txt` / `Ui.Muted` / `Ui.Glyph` 全部原样调用;
   - `DispatcherTimer` 那段搬进 `ToastLayer.Arm/Pause`,**默认在无时钟进程里不挂**(D-105);
   - 顶掉最老一张时顺手把它自己的计时器一起停掉(D-105 的收严);
   - `Ui.H2(title)`(对话框卡片)等调用原样。
2. 对话框卡片(源 `Services\Dlg.cs:185-213`,`Dlg.Shell`):
   - 控件树、行定义、内边距、圆角、描边、宽度夹取、取色键、阴影参数**逐项相同**(只是从
     `private static Border Shell(...)` 的方法体搬进 `DialogCard` 的构造函数);
   - `ScrollViewer` 的 4 个属性(含 WPF 的 `ScrollBarVisibility`)原样;
   - `Shell` 现在 `return new DialogCard(title, body, footer, width);`。

### 6. Windows → Avalonia / mac 特有差异(逐条)

1. **Toast 卡片不是控件类**:Windows 里它是 `MainWindow.Toast` 的局部变量,mac 侧提成 `ToastCard`
   类型才能离开窗口单测(与 C2a 提 `Dlg.Push`、C2b 提 `PageHost` 同一套做法,D-106)。
2. **宿主管理提成 `ToastLayer`**:Windows 把"4 张上限 / 进退场 / 自动关闭 / 悬停暂停"内联在窗口方法里。
3. **自动关闭的默认计时器在离屏/测试里不挂**(D-105):`Motion.HasClock` 作闸;真桌面与 Windows 一致。
4. **顶掉最老一张时连计时一起停**(D-105):Windows 只 `RemoveAt(0)`,那张卡的计时器会多转 4~7 秒。
5. **对话框卡片提成 `DialogCard`**(D-107):C2a 的内联版逐行等价搬运;`Panel`/`Busy` 保持内联。
6. 其余框架差异(`Visibility`→`IsVisible`、`FrameworkElement`→`Control`、`Cursor`、`PointerPressed/Released`)
   沿用 C2a 已经落地的映射表,本包没有新增映射。

### 7. 测试(新增 60 条;总数 2631 → 2691)

四个新文件共 **54 个测试方法**,`[Theory]` 展开后 **60 条用例**:

- `C2c1ToastTests.cs`(23 个方法 / 29 条):四种 kind 的图标码位 + 颜色令牌 + 图标字体(4 条)、
  卡片几何(圆角/内边距/描边/下边距/底色/描边色/三个孩子/色条宽/正文面板宽/光标)、
  两处间距、标题与正文的显示、正文空白不加行(2 条)、超 220 字截断、正好 220 字不截断、
  进宿主(右上角 StackPanel + 边距)、三张卡的堆叠顺序与间距、4 张上限顶掉最老的、
  顶掉时自己那张表的停法、4 秒 ×3 + 7 秒的延时、到点关掉且不留计时器、点一下只关一张并重排、
  右键不关、悬停暂停 / 移开续上、`Motion.Enabled == false` 的进场与关闭都同步落终态、
  重复关同一张是 no-op、`Dispose` 收摊(A-18 类泄漏断言)、默认计时器在无时钟下不挂、默认 kind 是 Info。
- `C2c1WiringTests.cs`(11 个方法 / 11 条):`AppServices.Toast` 在外壳浮层里出卡片、
  默认参数、6 次 Toast 堆叠后剩 4 张且顺序正确、没有窗口不抛、关窗后不抛且不留计时器、
  跨线程调用被投回 UI 线程、sink 在夹具收摊后还原且不再往旧外壳塞、`window.Toast` 的默认参数、
  点一下撤掉外壳里的卡片、`ClearToasts` 一次收干净、`Dlg.Form` 的「填写有误」现在能出声。
- `C2c1DialogCardTests.cs`(20 个方法 / 20 条):卡片几何与三行布局、`Min(width,380)` 两个分支、
  标题/正文与滚动容器的属性、确认片的按钮组(取消在前、危险/普通两种取变体)、
  确定解析 true、取消解析 false、Esc = 取消、点遮罩 = 取消、Alert 一个按钮且关掉算 true、
  Prompt 去空格 / 取消 null、Choose 每项一个按钮 + 取消 -1、Form 返回 collect 的值、
  Form 抛异常时卡片不关、`Layer.Card` 指回自己那张、关掉卡片不留挂着的动画、
  默认按钮文案与 `I18n.L` 同键、`Panel` 的卡片不是 `DialogCard`、最后一层关掉时撤模糊。
- 纪律:碰 Avalonia 的用例体**一律**经 `AvaloniaSession.Run(...)`(D-79),
  事件全部 `RaiseEvent` 程序化触发(**没有模拟输入设备**),自动关闭由注入的 `FakeToastTimer`
  确定性驱动(**没有 `Thread.Sleep` 去等 4 秒**),宿主窗口 / 全局开关(`Motion.Enabled`、
  `RequestedThemeVariant`、`Dlg.Host`/`BlurTarget`、`AppServices.ToastSink`)/计时器
  全部在 `using` / `finally` 里收干净。

写用例时踩到 5 处我自己的期望写错(都不是产品代码的 bug,记下来免得后人重踩):

1. **无时钟进程里 `LiveTimerCount` 恒 0**:默认计时器在测试里根本不挂(D-105),
   我一开始按"托盘里应该有一个计时器"写,四条用例一起红 —— 改成"无时钟 = 0",
   4 秒 / 7 秒那条改由注入的假计时器验;
2. **`Cursor` 没有值相等语义**:`Assert.Equal(new Cursor(StandardCursorType.Hand), card.Cursor)` 报
   `Expected: Hand Actual: Hand`(比的是引用),改成按 `ToString()` 对拍标准光标名;
3. **`card.BodyPanel` 是纵向**(`Ui.V(2.0, 标题, 正文)`),我一开始写成 `Horizontal`;
4. **`Dlg.Alert` 返回的是 `Task` 不是 `Task<bool>`**(Windows 侧签名就是 `Task`),
   所以"关掉算 true"只能断言"任务完成 + 浮层关了",不能读 `Result`;
5. **遮罩那一路要点左键按下才算**:`Dlg.Push` 的遮罩处理读
   `e.GetCurrentPoint(mask).Properties.IsLeftButtonPressed`,而 `new PointerPointProperties()` 全是 false,
   所以要用双参构造 `new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed)`
   才能表达"左键按下";顺带发现 `PointerPressedEventArgs` 的第 8 个参数是 `int clickCount`,**不是** `MouseButton`。

另外首轮构建带出 4 条 `xUnit2017` + 1 条 `xUnit2000`(用 `Assert.True(coll.Contains(x))` 之类),
已按分析器的建议改成 `Assert.Contains` / 参数顺序对调,警告回到"只有 NU1900 + SYSLIB0014 ×1"。

### 8. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | 开工前 `%LOCALAPPDATA%\TipCL` 测量 | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,与 Lead 基线一致 |
| 2 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;**7 个警告 = `NU1900` ×6 + `SYSLIB0014` ×1**(按警告码分组核过;首轮的 5 条 xUnit 分析器警告已清零) |
| 3 | `dotnet test TipCL.Mac.sln -c Release --no-build` 第 1 次 | 退出码 **0**:`已通过! - 失败: 0,通过: 2691,已跳过: 0,总计: 2691,持续时间: 12 s - TipCL.Tests.dll (net8.0)` |
| 4 | 同上 第 2 次 | 退出码 **0**,同上逐字一致 |
| 5 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` 第 1 次 | 退出码 **0**:`总计 2691 个用例,通过 2691,失败 0,跳过 0,不支持 0`,末尾 `in-proc 测试全绿` |
| 6 | 同上 第 2 次 | 退出码 **0**,同上逐字一致;**不支持 0** |
| 7 | `--smoke --theme light` | 退出码 **0**,**21823 字节**,SHA256 `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B` |
| 8 | `--smoke --theme dark` | 退出码 **0**,**18200 字节**,SHA256 `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E` |
| 9 | `--smoke --theme blue` | 退出码 **2**(参数错误,打印用法 —— 与 C2b/C2d 一致) |
| 10 | 收工后 `%LOCALAPPDATA%\TipCL` | 9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16,**与开工前完全一致** |
| 11 | 残留进程 `testhost` / `TipCL` / `java` / `adb` | **0 / 0 / 0 / 0**(并逐个核过没有监听端口) |
| 12 | `%TEMP%` 下 `tipcl*` 目录 | **89 个**,本次会话新建 **0 个**(最近改写的 `tipcl-probe` 是 12:14 由**另一会话**维护的,它有完整的 launcher home 目录树;本包用例的数据全部落在 `tests\TipCL.Tests\bin\Release\net8.0\test-home\<guid>` 下) |
| 13 | `git diff --name-status 27da085 HEAD -- src/TipCL.Core` | **空** |
| 14 | `src\TipCL.Core\*.cs` 对 Windows 源 SHA256(排除 `obj\`/`bin\`) | mac 侧 248 个:有对应物 **238** / 完全一致 **216** / 有改动 **22** / 无对应物 **10**,与基线**完全一致** |
| 15 | 新建 / 修改文件的字节级编码核验(`ReadAllBytes`:BOM = 前 3 字节 `EF BB BF`;CR = 有没有 `0x0D`;另数 U+FFFD) | 9 个文件全部 **BOM=False / CR=0(纯 LF)/ U+FFFD=0**,与仓库其它 `.cs` 一致 |

**第 7/8 条为什么与 C2b 基准逐位相同**:本包新增的 Toast 层在**没有 Toast 的时候什么都不画**
(宿主 `ToastHost` 面板本来就是 C2b 建好的空 StackPanel,`ToastLayer` 不弹卡就不往它里面加东西,
`--smoke` 路径也不会调 `AppServices.Toast`),标题栏 / 侧栏 / 页面底纹一行未动 ⇒ 像素不变。

### 9. 未验证项(本机是 Windows,没有 Mac)

1. **macOS 真机**:Toast 在 Avalonia.Native 下的观感(`BoxShadow` 的 22/0.14/4、右上角堆叠与
   屏幕边缘的距离)、真桌面下 `Motion.SlideIn/SlideOut` 的动画播放、`DispatcherTimer` 真的到点
   (本包的 4 秒 / 7 秒只在注入的假计时器上验过,**没有一次真实的 4 秒等待**)、悬停暂停的手感 ——
   全部**未验证**。
2. **"动画开着"那条分支在测试进程里结构上不可达**(`Motion.Enabled` 需要真生命周期,而 Avalonia
   初始化后禁止再改 `ApplicationLifetime`,C1b-fix 又只允许 `AvaloniaSession` 一处起平台),
   所以两条测试只验了"关掉动画时同步落终态"那一半。
3. **`PageBase.EnableDrop` / `FileDrop` 未接**(属 C2c2),所以 C3 的拖拽路径在本包之后仍然不可用。
4. **`Dlg` 的文件/文件夹选择路径**照旧未验证(需要真桌面 `IStorageProvider`)。
5. `Point(0,0)` 的合成指针事件只走到**事件路由**这一层,没有经过平台输入栈
   (headless 下不许模拟输入设备,这是红线要求的行为)。

### 10. 遗留与风险

1. **AI 工具审批卡(`Pages\ConfirmCard.cs`)没有搬**(D-104):它要 `AiRisk` / `ToolMeta` /
   `AiUiText` / `AiIcons` / `MdView`,属 AI 全家(C7+)。**建议**:AI 页那包一起搬,
   或者先派一个"AI 视觉基础设施"小包。**这一条需要 Lead 裁决**。
2. **`Pages\Bubble.cs`(AI 对话气泡)也没有搬**:同上,属 AI 页(AiPage 那一包)。
3. **`ProfileWidgets` / 下载任务底条 / 标题栏圆片 / DropVeil / Backdrop / Wallpaper / SideFrost /
   ImeGuard / AccountGate / Thumbs / LoaderIcons** 仍属 C2c2(本包没碰)。
4. **`ToastLayer` 的 4 张上限在"退场动画进行中"时按宿主孩子数算**(与 Windows 同口径):
   真桌面下如果连着弹 5 张,第 5 张会把最老那张直接摘掉(而不是等它退完场)。行为与 Windows 一致,
   但观感上可能出现一次"闪一下";要改成等退场可以先记进 D 阶段。
5. **`AppServices.ToastSink` 在窗口关闭后仍指着那个外壳**(Windows 也是"方法长在窗口上",
   没有反注册这一步)。本包的做法是:`Closed → ToastLayer.Dispose()` 把计时器与孩子清掉,
   之后再来 Toast 只会往一个不在可视树上的面板加控件(不抛、不改状态,用例钉住了)。
   若 Lead 认为该在 `Closed` 里把 sink 置空,**不要在 `MainWindow` 里直接置空** —— 多个窗口
   (测试里就是多个)会互相把对方的 sink 清掉;正确做法是"只有 sink 还指着我时才清",本包没有做。
6. **`C2c1WiringTests` 与 `C2bWiringTests` 都动 `AppServices.ToastSink`**,但它们各自用
   `ShellFixture` 的快照在 `Dispose` 里还原,所以不会串;仍然建议 C3 起页面后复核一次
   "页面里的 Toast 会不会在关窗后还被调到"(现在不会抛,只会静默)。

### Lead 验收(2026-11-13 12:45)

结论:**C2c1 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 三个提交(`2100490` / `dcdfa16` / `2fd5be3`) | 工作树干净;无 remote、无 tag、全局 git identity 未设;Windows 仓库 HEAD 仍 `803dd38` |
| 红线文件(`src\TipCL.Core`、`Themes\`、`NavModel.cs`、`NavConfig.cs`、`Pages\`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` x6 + `SYSLIB0014` x1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2691 / 总计 2691**(2631 → 2691,+60) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2691 / 失败 0 / 不支持 0** |
| `--smoke` light / dark / bad theme | 退出码 0 / 0 / **2**;light **21823 `8336CA13...`**、dark **18200 `DBD3C171...`** —— **与 C2b 基准逐位相同**(Toast 宿主无卡片时什么都不画,离屏不调 Toast) |
| **既有测试有没有被改弱(Lead 逐项比对)** | `C2bWiringTests.cs`:**断言 75 → 79、用例 20 → 20、Skip 0 → 0**。被改的那条从 `Assert.Null(AppServices.ToastSink)` + `Assert.Empty(children)` **改成** `Assert.NotNull(...)` + 断言宿主里真的出现一张 `ToastCard` 且 `Title`/`Body`/`Kind` 正确 —— **是加强,不是削弱** |
| 全部改动文件的编码(Lead 按字节核验) | 11 个文件全部 **无 BOM、CR=0(纯 LF)、无 U+FFFD** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 / `%TEMP%` 新目录 | 9328 / 889530606 / 2026-11-10 20:42:16;0 个;`tipcl*` 89 → 89 无新增 |

**裁决:接受子代理对任务书文件名的更正(Lead 写错了)**。

Lead 在 C2c1 任务书里点名要搬 `Services\Bubble.cs` 与 `Pages\ConfirmCard.cs`,并说它们是 Toast 卡片与对话框卡片。**实际上仓库里没有 `Services\Bubble.cs`**;`Pages\Bubble.cs` 是 **AI 对话气泡**(think 折叠 / `MdView` / 编辑撤回),`Pages\ConfirmCard.cs` 是 **AI 工具审批卡**(「助手要执行一个操作,需要你确认」),只被 `AiPage.cs:2614` 与 `AiFixWindow.cs:761` 用到,依赖 `AiIcons` / `AiRisk` / `AiUiText` / `MdView`。搬这两个等于连带搬进 AI 页基础设施,与「本包不许移植任何页面」直接冲突。

子代理按**描述**而不是按错名字搬了真正对的两处:Toast 卡片 = `MainWindow.xaml.cs:1179-1255` 的 `MainWindow.Toast`(Windows 侧就是块内建 Border,没有独立类);对话框卡片 = `Services\Dlg.cs:185-213` 的 `Shell`(C2a 时已内联移植,本包抽成 `DialogCard`)。

**裁决:C2c1 的做法正确,保持。** AI 审批卡与 AI 对话气泡**不单独开包**,归到 **C7+(AI 全家)**,和 AI 页一起搬 —— 那时 `AiIcons`/`MdView` 等依赖自然就位。

**遗留**:4 张 Toast 上限按宿主孩子数算(与 Windows 同口径,退场动画期间连弹第 5 张会「闪一下」);关窗后 `ToastSink` 仍指向旧外壳(Windows 同),本包靠 `Dispose` 保证不抛不留计时器。## C2c2 页面级视觉服务:拖放 / 背景 / 缩略图 / 加载器图标 / 账号守卫(2026-11-13)

### 1. 先纠正任务书里的一条事实:内置预设背景与加载器图标**都不在 Core 里**

任务书写「内置预设背景已内嵌在 Core 的 `Resources\wallpapers\*.jpg` —— 查 `TipCL.Core.CoreResources`,不要重复嵌入」。实测**不成立**:

| 检查 | 实测结果 |
|---|---|
| mac 的 `src\TipCL.Core\Resources\` | 只有 `ai` / `locales` / `plugin-host` 三个目录(`CoreResourcesTests` 钉的就是这三处),**没有 wallpapers,也没有 PremiumAssets** |
| Windows 侧的嵌法 | `src\TipCL\TipCL.csproj:98` 的 WPF `<Resource Include="Resources\wallpapers\*.jpg" />`(App 程序集!),读取走 `pack://application:,,,/TipCL;component/Resources/wallpapers/…`(`SettingsPage.cs:643`) |
| 加载器图标 | 同一条:Windows 是 `TipCL.csproj:106-112` 的 `<Resource Include="PremiumAssets\*"/>`,**premium 才嵌**;Core 里同样没有 |

所以按**描述**而不是按错误的前提办:4 张预设 JPG 与 5 个加载器图标**字节级复制**(`Copy-Item`,与 Windows 源 SHA256 逐项相同,见第 6 节)进 `src\TipCL.App\`,用 `AvaloniaResource` 内嵌,URI 由 `pack://application:,,,/TipCL;component/…` 换成 `avares://TipCL/…`。
**`src\TipCL.Core` 一个字节没动**(`git diff --name-status 876d57d HEAD -- src/TipCL.Core` 为空;Core 身份仍是 238 有对应 / 216 一致 / 22 有改动 / 10 无对应)。

另一处口径更正:任务书把 `AccountGate` 描述成"路由到账号页 / 提示登录的守卫"。**实测 Windows 源码不是这样**:它不导航、不开账号页,而是 `IsSentinel`(名字算不算没登录)+ `ListAsync`(列真账号)+ `EnsureAsync`(挑一个,没有就现开一个离线用户)+ `CreateOfflineAsync`(弹「创建离线用户」再调后端 `add_offline_account`)。本包照源码搬(与 C2c1 处理"文件名与描述不符"同一条纪律)。

### 2. 做了什么

1. **拖放**:`Services\FileDrop.cs`(Windows 同名文件的公开面:`DropContext` / `HandleAsync` / `ImportModpackAsync`,私有那几个也逐个搬)+ 新增的"浮层那一半"(Windows 长在 `MainWindow` 里:`PreviewDragEnter/Over/Leave/Drop` → `ShowDropVeil`、`OnDragOver`/`OnDrop`);
   - `Services\DropVeil.cs`:Windows `MainWindow.xaml:78-86` 那块 `<Border Name="DropVeil">` 提成具名控件(卡片圆角 10 / 内边距 (20,16,20,18) / 描边 2 / 图标 30 / 标题 15 SemiBold / 提示 12 最宽 360 / 淡入 120ms 淡出 110ms / 文案 `松手就装`);
   - `Pages\PageBase.EnableDrop(Func<FileDrop.DropContext>)`:与 Windows 同名同参,外加 mac 侧的 `EnableDrop(bool)` 开关与 `DropEnabled` / `DropContextNow()`;
   - 外壳接线:`MainWindow` 里 `DragDrop.SetAllowDrop(this, true)` + 四个 `DragDrop.*Event` 处理器 + `FileDrop.Attach(this, _dropVeil)`,浮层插在正文之上、Toast / 对话层之下(与 Windows 的 XAML 顺序一致);关窗 `Reset()` + `Detach()`。
2. **背景管线**:`Services\Wallpaper.cs`(Windows 公开面 `Error` / `Active` / `ChromeInvalidated` / `Attach` / `SetWindowActive` / `ReloadAsync` / `SetSource` / `SetEffects` / `OnThemeChanged` / `Shutdown` 一个不少)+ `Services\ImageDecode.cs`(统一走 Core 的 `PlatformServices.ImageCodec` = B1a 的 `SkiaImageCodec`,坏数据一律 null);外壳里新增 `WallHost`(铺满内容层、在标题栏之下)与 `PageWash`(有壁纸时透明),`Activated`/`Deactivated` → `Wallpaper.SetWindowActive`,后端就绪后 `ReloadAsync`(Windows `MainWindow.xaml.cs:357` 同)。
3. **账号守卫**:`Services\AccountGate.cs`(公开面 `IsSentinel` / `ListAsync` / `EnsureAsync` / `CreateOfflineAsync` / `ValidName`)+ 两个注入缝(`RowSource` / `AccountCreator`)。
4. **缩略图**:`Services\Thumbs.cs`(`Cached` / `LoadAsync`,8 路闸门、400 → 300 裁剪、5 分钟失败冷却、in-flight 合并照搬)+ `Services\ThumbTile.cs`(圆角方块 / 首字母回落 / `SetUrl` / `SetImage` / 180ms 淡入 / token 丢过期请求)。
5. **加载器图标**:`Services\LoaderIcons.cs` = Windows 的 **premium 那一支**(GOAL 定了 mac 等同 premium):文件名表、解码宽度 `clamp(ceil(size)*2, 32, 96)`、按 `文件名|px` 缓存已解码位图;名字不认识 → 无图标 → 首字母方块(premium 的回落,与 Windows 一致)。
6. 顺手补了两个必需的 DTO(都是 WPF-free 的纯模型,从 Windows `Models\` 字节级复制):`Models\AccountRow.cs`、`Models\InstanceInfo.cs`。

### 3. 新增 / 改动的文件

| 文件 | 改动 |
|---|---|
| `src\TipCL.App\Services\FileDrop.cs` | **新增**(Windows `Services\FileDrop.cs` 的等价搬迁 + 本包新增的拖放驱动那一半) |
| `src\TipCL.App\Services\DropVeil.cs` | **新增**(Windows `MainWindow.xaml` 的 `DropVeil` 提成具名类型) |
| `src\TipCL.App\Services\Wallpaper.cs` | **新增**(Windows `Services\Wallpaper.cs` 的等价搬迁 + 预设解析/注入缝) |
| `src\TipCL.App\Services\ImageDecode.cs` | **新增**(mac 专有:统一走 Core 的 Skia 编解码器) |
| `src\TipCL.App\Services\AccountGate.cs` | **新增**(Windows `Services\AccountGate.cs` 的等价搬迁) |
| `src\TipCL.App\Services\Thumbs.cs` | **新增**(Windows `Services\Thumbs.cs` 的等价搬迁 + 路径来源注入缝) |
| `src\TipCL.App\Services\ThumbTile.cs` | **新增**(Windows `Services\ThumbTile.cs` 的等价搬迁) |
| `src\TipCL.App\Services\LoaderIcons.cs` | **新增**(Windows premium 支的等价搬迁) |
| `src\TipCL.App\Models\AccountRow.cs`、`InstanceInfo.cs` | **新增**(从 Windows `Models\` 字节级复制,SHA256 与源相同) |
| `src\TipCL.App\Resources\wallpapers\preset-*.jpg`(4 张) | **新增**(从 Windows `Resources\wallpapers\` 字节级复制) |
| `src\TipCL.App\PremiumAssets\*.png`(5 个) | **新增**(从 Windows `PremiumAssets\` 字节级复制) |
| `src\TipCL.App\TipCL.App.csproj` | 新增两个 `AvaloniaResource` 组(预设背景 4 张、加载器图标 5 个),原有内容一字未改 |
| `src\TipCL.App\Pages\PageBase.cs` | 新增 `EnableDrop(Func<...>)` / `EnableDrop(bool)` / `DropEnabled` / `DropContextNow()`;类注释里"`EnableDrop` 本包不移植"那句改成已移植 |
| `src\TipCL.App\MainWindow.cs` | 新增拖放接线与三个处理函数、`WallHost` / `PageWash` 两个图层与 `Wallpaper.Attach`、`Activated`/`Deactivated`、关窗收摊、`ApplyThemeVisuals` 尾部喊 `Wallpaper.OnThemeChanged()`、后端就绪后 `ReloadAsync`;**没有改动任何既有成员的签名或语义** |
| `tests\TipCL.Tests\C2c2Fixtures.cs` 等 5 个测试文件 | **新增**(106 条用例) |

### 4. 每个 Windows 文件的公开面(逐条对照)

| Windows 文件 | 公开面 | mac 侧 |
|---|---|---|
| `Services\FileDrop.cs` | `DropContext`(None/Instance/Version/PreferKind)、`HandleAsync(MainWindow, IReadOnlyList<string>, DropContext?)`、`ImportModpackAsync(MainWindow, string)` | 名字 / 参数 / 语义一致;**类型与成员降为 `internal`**(`MainWindow` 在 mac 是 internal,public 成员不能暴露它 —— CS0051)。页面调用形状不变:`FileDrop.HandleAsync(win, paths, ctx())` |
| `Services\Wallpaper.cs` | `Error` / `Active` / `ChromeInvalidated` / `Attach(Grid, Border, Border, Border, Rectangle)` / `SetWindowActive(bool)` / `ReloadAsync()` / `SetSource(string)` / `SetEffects(int,int)` / `OnThemeChanged()` / `Shutdown()` | **一模一样**(`Attach` 的第 4 个参数 mac 给侧栏,Windows 给 null;第 5 个参数 mac 是同一个名字的 `Rectangle`) |
| `Services\AccountGate.cs` | `IsSentinel` / `ListAsync` / `EnsureAsync` / `CreateOfflineAsync` / `ValidName`(类型 `internal`) | 一模一样 |
| `Services\Thumbs.cs` | `Cached(string,int)` / `LoadAsync(string,int)` | 名字一致,类型由 `BitmapSource` 换成 Avalonia 的 `Bitmap` |
| `Services\ThumbTile.cs` | `ThumbTile(string, double = 42, double = 10)` / `SetUrl(string?)` / `SetImage(BitmapSource?)`(premium 才有) | 名字 / 默认值一致;`SetImage` **恒在**(mac 等同 premium) |
| `Services\LoaderIcons.cs` | `Tile(string, string, double = 36)`(premium 支;**compact 支的 `Url` 不搬** —— Windows 里也只有 `Tile` 自己用) | 一致 |
| `Pages\PageBase.cs` | `protected void EnableDrop(Func<FileDrop.DropContext>)` | 同名同参,可访问性降为 `internal`(参数里有 internal 的 `DropContext`) |

### 5. 从 Windows 复制/搬迁的文件,对它们改了哪几处

除 `MainWindow.cs`(外壳,不是复制品)外,逐文件列出**全部**改动点:

- **FileDrop.cs**:① `MainWindow` 参数与 `DropContext` 降为 internal;② 皮肤那一步(Windows 直接调 `AccountPage.EditSkinExternalAsync`)改成 `SkinApplier` 注入缝,没接时抛 `InvalidOperationException(「账号」页还没移植…)`(与 Windows"没有离线账号"那条同样是 `InvalidOperationException`,由调用方的 `Run` 兜住);③ `SetWallpaperAsync` 尾部多一句:管线 `Error` 非空就把它当结果返回(见第 7 节第 2 条);④ 认种类抽成 `IdentifyPaths`(Windows 是内联的一句,语义不变);⑤ 新增拖放驱动那一半 + 注入缝。
- **Wallpaper.cs**:① 视频壁纸改成注入缝 `VideoHost`(默认 null = 本版不支持,见 D-109);② `ApplyChrome` 里对 `SideFrost.Refresh()` / `Backdrop.Refresh()` 的两处调用改成 `SideFrostRefresh` / `BackdropRefresh` 两个注入口(C2c3 落地时接上,现在为 null);③ `SetResourceReference` → `UiExt.Dyn`;④ 轮播计时器做成注入缝(默认实现与 C2c1 的 Toast 计时器同口径:没有时钟就不挂);⑤ `Attach` 会把上一次挂上去的两个静态图层从旧宿主里摘掉(一个进程里会有多个外壳);⑥ 新增预设解析(`Presets` / `PresetUri` / `PresetPath` / `ReleasePreset` / `OpenPreset`)与一批只读的 internal 取值口(测试用)。
- **Thumbs.cs**:① 路径来源改成注入缝 `Store`(`BridgeThumbStore` = Windows 的两条后端调用;测试用 `LocalThumbStore`);② 解码统一走 `ImageDecode`(Core 的 Skia,不需要 `WebpFallback`);③ `Trim` 里对 `_mem.Keys` 先 `ToList()`(照 WPF 那行写会在遍历中改集合);④ 新增 `Clear` / `MemoryCount` / `FailureCount` 给测试。
- **ThumbTile.cs**:① 显示那一步 `Dispatcher.InvokeAsync` → `CheckAccess` + `Post`;② 暴露 `ImageLayer` / `Letter` / `ImageVisible` / `LetterVisible` / `ImageSource` / `Token` 只读给测试。
- **LoaderIcons.cs**:只保留 premium 支;`pack://` → `avares://`;`BitmapImage.DecodePixelWidth` → `ImageDecode`(按宽度缩放)。文件名表 / clamp / 缓存键一字未改。
- **AccountGate.cs**:① `AppServices.Window?.Prefs` → `(AppServices.Window as MainWindow)?.Prefs`;② `AppServices.Window?.Toast` → `AppServices.Toast`(C2a 起的统一口径);③ 后端调用收进两个注入缝。
- **PageBase.cs**:新增 4 个成员(见上表),原有成员逐字未改。
- **MainWindow.cs**:只增不改(见第 3 节),`_pageWash` 由内联 `new Border{...}` 改成具名字段 `Rectangle`(像素逐位不变:smoke 与基准一致)。

### 6. 资源与复制品的字节核验

| 文件 | 结果 |
|---|---|
| `Resources\wallpapers\preset-morning.jpg` | SHA256 `D88FA221…` 与 Windows 源**相同** |
| `preset-dusk.jpg` | `99C7FA8C…` 相同 |
| `preset-night.jpg` | `656C0F88…` 相同 |
| `preset-sakura.jpg` | `7B60F059…` 相同 |
| `PremiumAssets\grass.png` | `AEE29A26…` 相同 |
| `fabric_gh.png` | `1F62ABFC…` 相同 |
| `forge_gh.png` | `9EA5E797…` 相同(内容其实是 JPEG,解码按内容识别) |
| `neoforge_gh.png` | `9590353F…` 相同 |
| `quilt_gh.png` | `B3E5A359…` 相同 |
| `Models\AccountRow.cs` / `InstanceInfo.cs` | 545 / 583 字节,与 Windows 源逐字节相同 |

### 7. Windows → mac 差异(逐条,不静默丢)

1. **视频壁纸本版不支持**(D-109):Windows 用 WPF `MediaElement` 静音循环播 mp4;Avalonia 11 **没有内建播放器**,要支持得引 LibVLC 之类的新依赖(超出本包授权)。mac 侧:`VideoHost` 为 null 时记 `Error = macOS 版还不支持视频壁纸:<path>` 且 `Active = false` —— **绝不假装放上了**;接上注入缝之后走 Windows 那条路(起播 / 跟窗口激活播停 / 换图先停)。
2. **拖进来的壁纸失败时会说实话**:`FileDrop.SetWallpaperAsync` 在 `ReloadAsync` 之后检查 `Wallpaper.Error`,非空就把原因当结果返回(于是 Toast 报的是真原因,而不是照样说"已设为背景图")。这是本包对 Windows 的一处**收严**。
3. **`EnableDrop(bool)` / `DropEnabled` / 拖放总闸 `Enabled` / `ReadyGate` 是 mac 新增**:Windows 的 `EnableDrop` 只开不关,且"后端没连上"是外壳的 `_bridgeReady`;mac 侧把这两件事做成可断言的状态(测试与 C3+ 页面都用得上),语义仍是"没连上后端就不出浮层、不接文件"。
4. **"拖出去 = 取消"显式记一笔**(`DragCancelled`):Windows 靠事件路由(拖到窗口外松手时窗口收不到 `Drop`),mac 侧在 `DragLeave` 时记标记、`Drop` 看到标记就拒绝;重新拖进来会清掉。真实拖放的语义不变。
5. **`EnableDrop(null)` 当场抛 `ArgumentNullException`**:Windows 传 null 会在落点那一刻才 NRE;mac 侧提前收严(有用例钉住)。
6. **`Wallpaper.Attach` 会摘掉旧宿主里的图层**:mac 一个进程里会有多个外壳(测试),不摘会"一个控件两个父";Windows 一辈子只有一个 MainWindow。
7. **不再直接调 `Backdrop` / `SideFrost`**:它们属 C2c3(**红线不许本包碰**),改成两个注入口;因此"有壁纸时侧栏霜化要刷新"这件事**本包没做**,留给 C2c3 接线(第 10 节)。
8. **`DropVeil` 的图标行照抄 Windows 的空文本**(`Text=""`,只占位不画字形),没有自己编一个图标。
9. **`MainWindow.ApplyThemeVisuals` 尾部会喊 `Wallpaper.OnThemeChanged()`**:Windows 是 `App.ThemeVisualsChanged` 事件,mac 的外壳直接调一次(等价,见 D-110)。
10. **`BuildLocalService` 那条口径**不变:`FileDrop` 在 mac 是 internal(见第 4 节)。

### 8. 测试(新增 106 条用例;总数 2691 → 2797)

| 文件 | 覆盖 |
|---|---|
| `C2c2FileDropTests.cs`(27 条) | 浮层进出 / 幂等 / `DragLeave` 后无残留(可见标志 + 图层在位 + 没有没落地的淡出)/ 拖出去落点算取消 / 重新进来清标记 / 载荷逐项一致(路径顺序、滤空)/ 后端没连上或总闸关掉时全压住 / `EnableDrop` 默认 false、开关、上下文每次现调、null 拒绝、切页之后仍有效 / 落点用当前页上下文、没开就用 `None` / 浮层文案与取值(圆角 10、内边距 (20,16,20,18)、描边 2、30/15/12、最宽 360、两条文案)/ `IdentifyPaths` 与 `FileKinds` 规则一致 / 关窗摘掉静态引用 |
| `C2c2WallpaperTests.cs`(26 条) | `SetSource` 状态机(真图 / 空 / 不存在 / 坏图 / 幂等)/ 没有窗口时不抛 / 模糊遮罩夹 0-40、0-80 与底色 / `ApplyChrome`(PageWash 透明度、根边框兜底、标题栏玻璃 vs 实底)/ 两个外壳之间搬图层 / 四个预设表与内嵌资源真的能打开 / 未知预设走 Windows 回落 / 释放到数据目录且不覆盖 / `ReloadAsync` 六个键与默认值 / 单图设置 / 文件夹轮播(扫描、夹 1-1440、到点换一张、取消文件夹停表)/ `Shutdown` 停表 / 没有时钟就不挂计时器 / 视频壁纸不支持与接上之后的那条路 |
| `C2c2ThumbsTests.cs`(28 条) | 缓存根可注入且落在 `TestHome.Root` 下 / 未命中 → `thumb_path` → `ensure_thumb` → 解码 → 内存命中 / 磁盘已缓存就不再 ensure / 坏图回落且记失败 / 冷却期内不重试 / 空 url 不碰后端 / 解码宽度 `max(16, px*2)` / `LocalThumbStore` 纯函数 / 瓦片首字母与 `?` / 取图期间显示首字母 / 缓存命中立即显示 / `SetImage` 顶掉未完成的取图 / 失败仍是首字母 / 瓦片宽度即取图宽度 / 加载器文件名表(含大小写)/ 五个图标的真解码 / 未知加载器回落 / clamp(32/72/96)/ `Tile` 能被 `Ui` 层直接用(挂真窗口、有尺寸)/ 图标缓存 / avares 取址 / `ImageDecode` 对空输入给 null |
| `C2c2AccountGateTests.cs`(25 条) | 哨兵名(空 / 空白 / 离线模式 中英)/ 真名字 / `ValidName` 规则与长度上下界 / `ListAsync` 过滤 + 激活优先 + 去重 / 空清单 / `EnsureAsync` 偏好命中与回落 / 没登录那一支真的弹框建号(名字、皮肤、后端返回名、Toast、取消 → null)/ `Player` 预填被清掉 + 空名字会再问一次 + 警告 Toast / 皮肤索引 0/1/2 → default·steve·alex / 已有账号时**不弹框** |

纪律:碰 Avalonia 的用例体**一律**经 `AvaloniaSession.Run(...)`(D-79);不模拟真实输入设备(拖放走公开入口 + 注入"拖的是什么",对话框用程序化 `RaiseEvent` 点按钮);临时文件全在 `TestHome.Root` 之下;本包新加的注入缝由 `C2c2Scope` 取快照并在 `Dispose` 里还原,用例类归入同一个 `C2c2Globals` collection(D-103)。每条期望都在注释里标了来源【Windows 源】/【任务书】/【本机实测】。

### 9. 验证命令与实际结果(全部在本机真实执行过)

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 错误**;警告按类别分组 = `NU1900` ×6 + `SYSLIB0014` ×1(没有别的告警) |
| 2 | `dotnet test TipCL.Mac.sln -c Release --no-build`(第 1 / 2 次) | 都是**退出码 0,失败 0 / 通过 2797 / 总计 2797** |
| 3 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll`(第 1 / 2 次) | 都是**退出码 0,通过 2797 / 失败 0 / 跳过 0 / 不支持 0**(`总计 2797 个用例,通过 2797,失败 0,跳过 0,不支持 0` + `in-proc 测试全绿`) |
| 4 | `--smoke --theme light` / `--theme dark` / `--theme blue` | 退出码 **0 / 0 / 2**;light **21823 字节 `8336CA13…049B`**、dark **18200 字节 `DBD3C171…604E`** —— **与 C2b 基准逐位相同**(新增的浮层是收起的、背景宿主是空的、`PageWash` 由 Border 换成 Rectangle 后像素一致) |
| 5 | `%LOCALAPPDATA%\TipCL` 收尾复测 | **9328 文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16** —— 与开工时**完全相同** |
| 6 | 残留进程与 `%TEMP%` | `testhost` / `TipCL` / `java` / `adb` **各 0 个**;`%TEMP%\tipcl*` 最新一项是 12:14:02 的 `tipcl-probe`(早于本包开工的 12:42),**本包开工之后新增 0 个** |
| 7 | `git diff --name-status 876d57d HEAD -- src/TipCL.Core` | **空**(Core 一字未动);Core 身份 = 238 有对应 / 216 一致 / 22 有改动 / 10 无对应 |
| 8 | 逐文件字节核验(18 个源码/csproj + 9 个二进制资源) | 全部 **无 BOM、CR=0、U+FFFD=0**;9 个资源与 Windows 源 SHA256 相同 |

### 10. 只有真机(Mac)才能验的

- **真实拖放**:Avalonia 的 `DragDrop` 走平台 DnD 会话,离屏 headless 里**起不来**。本包只把"平台事件 → 提取文件路径 → `FileDrop` 入口"这一段接线写好并用公开入口验了;真机上要验的是:拖文件到窗口上是否出现浮层、松开是否真装、拖到窗口外松手是否什么都不发生、`e.Data.GetFiles()` 拿到的路径是不是本地路径。**未验**。
- **`e.Data` 的 `[Obsolete]`(CS0618)**:Avalonia 11.3.2 的 `DragEventArgs.Data` 被标了"Use DataTransfer instead",但 11.3.2 的程序集里**没有** `DataTransfer` / `IDataTransfer`(ref 与 lib 两个目录的 `Avalonia.Base.dll`/`Avalonia.Controls.dll` 里都没有这个符号);本包**不动 `NoWarn`**,只把那一行局部 `#pragma warning disable CS0618` 包住,等 D-01 升级 Avalonia 后换新 API。
- **视频壁纸**:本版按"不支持"处理(见第 7 节第 1 条),真机上也一样不支持 —— 想支持要先引播放器依赖。
- **macOS 的窗口激活/失活与壁纸联动**:`Activated` / `Deactivated` 在 headless 里没有真实窗口管理器语义,真机上要验"切到别的应用时视频暂停、回来继续"。
- **Keychain / 后端**:与本包无关(见 B1a / C2d)。

### 11. 遗留与风险

1. **`Backdrop` / `SideFrost` 的接入口留空**(C2c3):有壁纸时侧栏霜化不会刷新、窗口背板不会重取;浮层与壁纸本身不受影响。C2c3 只需把 `Wallpaper.BackdropRefresh` / `SideFrostRefresh` 接上。
2. **皮肤拖放还没接线**:`FileDrop.SkinApplier` 为空时抛「账号页还没移植」;C3+ 的账号页包把它接到 `AccountPage.EditSkinExternalAsync` 即可(Windows 的调用点就那一处)。
3. **`FileDrop.HandleAsync` 的"认不准就问一句"这条链没有端到端用例**:它要真后端(`install_mod` 之类)与真对话框,本包只测了认种类与载荷,没测 `AskKindAsync` 的具体交互;**未验**。
4. **`AccountGate` 的对话框交互用了"先推出来再点"的写法**(见 D-113):离屏的 `SmokeAutoAnswer` 回调是在 `Dlg.Push` **里面**被调的,而 `Dlg.Ask` 的 `button.Click += …` 在 `Push` 返回**之后**才装上 —— 在那个回调里点确定,TrySetResult 还没接上,点了也白点。本包一开始就是这么写的,导致一条用例 30 秒超时并把会话线程占死,后面整批用例跟着超时(排查过程见 D-112)。C3+ 凡是要驱动 `Dlg.Ask` 的用例都按这个写法来。
5. **`ThumbTile` 的异步取图在测试里是"发射后不管"**:只断言了"此刻显示首字母";取回来之后切图那条路靠 `Thumbs` 与 `SetImage` 的用例钉住(真机上还要看淡入)。
6. **`Wallpaper` 的静态状态是进程级的**:一个进程里多个外壳会共用一套图层引用,`Attach`/`Detach` 负责搬家;真机只有一个窗口,不受影响。
7. **`Thumbs` 的内存缓存上限只在 >400 时裁到 300**,没有专门用例去灌 400 张(成本高、价值低);`Trim` 的取舍逻辑本包只做了代码级核对。
8. **`LoaderIcons` 只有 premium 支**:compact 的 `Url`(运行时下载)没有搬 —— 它在 Windows 里也没有别的调用点;GOAL 定了 mac 只出一个版本(等同 premium)。
9. **我本轮自己踩了一次 D-36 的坑(如实记录)**:用 PowerShell 的 `Get-Content -Raw | Set-Content` 批量替换 `C2c2ThumbsTests.cs` 里的一种写法时,中文被写坏(读成乱码 + BOM),`Select-String` 也匹配不到中文了;随即**用 `write` 工具把该文件整份重写**,并逐字节核验(无 BOM / CR=0 / U+FFFD=0 / 中文可 grep)。这也是为什么本小节的表格与追加内容都用"先 `write` 成临时文件、再按字节拼接"的做法。**教训与 D-36 一字不差,不要再用那个管线**。
10. **本包的提交顺序**:`C2c2(a)`(拖放 + 背景 + 账号守卫 + 两个 Models)、`C2c2(c)`(Thumbs / ThumbTile / LoaderIcons + 内嵌资源)、最后一个提交是测试与文档。任务书原本让 (a) 只放 `FileDrop`/`EnableDrop`,但 `FileDrop.SetWallpaperAsync` 要调 `Wallpaper.ReloadAsync`/`Error`,两者无法拆成两个都能独立编译的提交,所以 (a) 一并落了背景与账号守卫(如实记录)。

### 12. 本包没有做的事(红线)

没有移植任何页面(一个页面也没有);没有碰 `Backdrop` / `SideFrost` / `SideFrostEx` / `ImeGuard` / `WebViewHost` / `WebpFallback`、标题栏圆片、下载任务底条(全归 C2c3);没有碰 AI 卡片与 `Pages\Bubble.cs`;没有改 `src\TipCL.Core`(一个字节);没有改色板、`Controls.axaml`、导航模型,也没有改 `Ui` / `UiExt` / `Dlg` / `AppServices` 的任何既有签名;没有删测试、没有改弱断言、没有加 Skip、没有扩 `NoWarn`、没有改 TFM。

### Lead 验收(2026-11-13 14:25)

结论:**C2c2 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 三个提交(`0ae4fb3` / `1ebf6d5` / `56b33ae`) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`NavModel/NavConfig`、`Pages\PageHost/PageRegistry`、`ToastCard`、`DialogCard`、测试隔离三件、`AvaloniaSession`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改(`Pages\PageBase.cs` 是允许的:本包要加 `EnableDrop`) |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告 = `NU1900` x4 + `SYSLIB0014` x1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2797 / 总计 2797**(2691 → 2797,+106) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2797 / 失败 0 / 不支持 0** |
| `--smoke` light / dark / bad | 退出码 0 / 0 / **2**;light **21823 `8336CA13...`**、dark **18200 `DBD3C171...`** —— **与基准逐位相同** |
| **9 个内嵌资源对 Windows 源 SHA256(Lead 独立核对)** | **9 / 9 逐字节一致**(4 张 `preset-*.jpg` + 5 个 `PremiumAssets/*.png`) |
| **全部改动文件的编码(Lead 按字节核验)** | 无一例外:**无 BOM、CR=0、U+FFFD=0、无连续问号**;子代理自报弄坏过的 `C2c2ThumbsTests.cs` 现在是 23014 字节、794 个汉字可读、无 BOM/CR/乱码 —— 它的重写确实干净 |
| `PageBase.EnableDrop` 与 `DropVeil`(Lead 抽查源码) | `EnableDrop(Func<FileDrop.DropContext>)` 与 Windows 同名同参;`DropVeil` 的圆角 10 / 内边距 (20,16,20,18) / 描边 2 / 最宽 360 / 淡入 120ms / 淡出 110ms 与 Windows 源逐项对应 |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / **桌面** / **图片** | 9328 / 889530606 / 2026-11-10 20:42:16;桌面条目 14、最新写入 **2026-11-12 19:02:53**(与开工时相同,**零写入**);图片 3 项、最新 **2026-11-06**(零写入) |
| 残留进程 / `%TEMP%` 新目录 | 0 个;`tipcl*` 89 → 89 无新增 |

**裁决 1:接受子代理对任务书前提的更正 —— Lead 又写错了一处前提。**

Lead 在任务书里说「内置预设背景已内嵌在 Core 的 `Resources\wallpapers\*.jpg`」。**实测不成立**:mac Core 的 `src\TipCL.Core\Resources\` 只有 `ai` / `locales` / `plugin-host`(A2 就是按这个搬的)。Windows 侧壁纸与 `PremiumAssets` 都是 **App 程序集**的 WPF `<Resource>`(`TipCL.csproj:98` 与 `:106-112`,后者还要 `-p:Premium=true`),**从来不在 Core 里**。

子代理按**描述**办:把 4 张预设 + 5 个加载器图标字节级复制进 `src\TipCL.App\`,用 `AvaloniaResource` 内嵌,URI 改 `avares://`,**Core 一字节未动**。这与 GOAL §3 的「图标字体 `TipCLIcons.ttf`、壁纸、`PremiumAssets` 全部直接内嵌(mac 版无体积红线,只出一个版本,等同 premium)」**完全一致**,做法正确。

**裁决 2:接受 `AccountGate` 的更正。** Lead 把它描述成「路由到账号页 / 提示登录的守卫」;Windows 源码实际是 `IsSentinel` / `ListAsync` / `EnsureAsync` / `CreateOfflineAsync`(弹框建离线用户),**不导航**。按源码搬是对的。

**裁决 3:接受提交顺序偏离**(`FileDrop.SetWallpaperAsync` 要调 `Wallpaper.ReloadAsync`,拆开则没有单个提交能独立编译;(b) 并入 (a))。

**裁决 4:接受 D-36 的第三次误用与它的补救。** 子代理如实报告用 `Get-Content -Raw | Set-Content` 批量替换 `C2c2ThumbsTests.cs` 把中文改坏,随即整份重写并逐字节核验,该文件**未进过任何提交**;Lead 复核确认现在干净。这是这个坑第三次现形(C1b → C2b → C2c2),**不能再靠人肉扫描**:

**新增运营要求(写进 D1)**:D1 必须加一条**自动化守卫测试** —— 扫描全部受版本控制的文本文件,断言「除 `tools\*.ps1`(D-12 要求 BOM)外,任何源文件都不得带 BOM、不得含 CR 字节、不得含 U+FFFD」,发现异常即失败。把 Lead 现在手工做的那一步变成机器做的。

**C2 阶段(外壳)到此结束**:C2a(桥层+基础服务)、C2b(外壳)、C2c1(Toast/对话框卡片)、C2c2(页面级视觉服务)、C2d(后端接线与真实启动验证)。C2c3(`Backdrop`/`SideFrost`/`ImeGuard`/标题栏圆片/下载底条)零页面引用,**降级为可选**,排在页面之后。

**下一步:进入 C3 页面阶段**(顺序照 GOAL:启动 -> 实例库+实例详情 -> 下载+安装 -> 任务 -> 设置 -> 账号 -> Java -> 联机 -> 目录 Catalog -> 版本管理 -> 插件全家 -> AI 全家 -> 其余)。
---

## C3a 区块 / 仪表盘布局框架(2026-11-13)

工作包:**把 Windows `Pages\BlockHost.cs` 与 `Pages\DashHost.cs` 移植到 Avalonia 11 / net8**,
目标是让随后的 `LaunchPage`(C3b)移植变成机械劳动。**不碰** `LaunchPage.cs`、不碰外壳 / 导航 /
调色板 / 样式表、不碰 `Backdrop` / `SideFrost` / `ImeGuard` / `WebViewHost*`、`src\TipCL.Core` 一字节未改。

### 1. 这两个宿主是什么、干什么

**`BlockHost`(区块宿主,WP-U1 冻结契约)** —— 把一页的内容切成若干 `BlockSpec`(稳定英文 id +
已过 `I18n.L` 的标题 + 页面自建的内容控件 + 默认可见性),然后:

- 常态:按布局文档的顺序渲染**非隐藏**的块,视觉零装饰(间距 10、外边距默认 (18,14,18,16) 对齐
  `PageBase.ScrollBody`),默认渲染与旧版逐像素等价;
- 右键「自定义布局...」(specs ≥ 2 才有这一项)进编辑态:每块顶部出现操作条(拖拽手柄 + 标题 +
  隐藏按钮),底部出现「已隐藏的区块」托盘(点 chip 恢复)与悬浮工具条(完成编辑 / 重置本页 /
  导出布局 / 导入布局);
- 拖拽重排是**交换即落位**(与相邻块中点比 Y,可一次跨多格),不做动画插值,所以
  `Motion.Enabled = false` 时行为完全一致;
- 任何变更都即时组装 `PageLayoutDoc` → `PageLayoutStore.Save` → 触发 `Changed`;退出编辑再存一次。

**`DashHost`(自由画布仪表盘宿主)** —— 卡片坐标是 [0,1] 归一化矩形(X/Y/W/H),`Z` 决定前后。
它自己实现 `Panel` 的测量 / 排布:每张卡按归一化矩形落位(宽高各留 10px 缝,下限 40×30)。
编辑态可见八向缩放手柄(11×11、圆角 3、描边 1.5,锚在 0 / 0.5 / 1 的交点上)、整卡拖拽蒙版、
移除按钮;`Snap`(=`Grid > 0`)打开时拖拽按未缩放像素吸附到 `Grid` 网格,并铺一层瓷砖底纹。
内容由 `ContentFactory` 按 type 造一次并缓存。`AddCard` 先找左上 / 右上 / 左下 / 右下四个候选空位,
找不到才落到 (0.05,0.05)。

### 2. 这一包交付的完整成员清单(C3b 的页面就靠这些)

**`BlockHost`(`TipCL.Pages`,public 面 = 页面能用的全部):**

| 成员 | 说明 |
|---|---|
| `BlockHost(string pageId, IReadOnlyList<BlockSpec> specs, Thickness? contentMargin = null)` | 唯一构造;`contentMargin` 为 null 时用 (18,14,18,16) |
| `BlockSpec { string Id; string Title; Control? Content; bool DefaultVisible = true; }` | Windows 的 `UIElement? Content` → `Control? Content` |
| `Control Root` | 页面把它挂进自己的 Grid(LaunchPage 是 `root.Add(_blocks.Root, 1)`) |
| `event Action? Changed` | 任何布局变更(顺序 / 显隐 / 重置 / 导入 / 拖拽收尾)都会发 |

**`BlockHost.BlockCard`(internal 嵌套,页面不直接用)** `Spec` / `SetEdit(bool)` / `RefreshTint()`。

**`DashHost`(public 面):** `DashHost()`、`Load(DashLayout)`、`Rebuild()`、`AddCard(string)`、
`RemoveCard(DashCard)`、`Has(string)`、`NotifyChanged()`、`MinPx(string)`、`SnapV(double, double)`、
`BringToTop(CardView)`、`RefreshThemeVisuals()`、`Layout`、`EditMode`、`MinSizes`、
`ContentFactory`(字段)、`TitleFactory`(字段)、`event Action? Changed`、
重写 `MeasureOverride` / `ArrangeOverride`。嵌套 `DashHost.CardView`:`Card` / `SetContent(string, Control?)` /
`SetEdit(bool)` / `RefreshTheme()`。

**布局模型(页面与外壳都可能用到):** `PageLayoutDoc`(`CurrentVersion` / `Version` / `Blocks` /
`Default` / `Normalize` / `FromJson` / `ToDict` / `ToJson`)、`BlockState`、
`PageLayoutStore`(`Kind` / `DirOverride` / `DirPath` / `FilePath` / `Load` / `LoadAll` / `Save` /
`ExportJson` / `ParseImport`)、`PageLayoutIO`(`ExportToFileDialog` / `ImportFromFileDialog`)、
`DashCard`、`DashLayout`(`Version` / `GridChoices` / `Grid` / `Items` / `Snap` / `VisibleItems` /
`NextZ` / `Normalize` / `FromJson` / `Parse` / `Default`)。

**新增的 mac 侧成员(不影响上面任何签名):** 为了让"拖拽 / 缩放状态机"能被测试直接驱动,把
`BlockHost` 的一批 private 降为 `internal`(见 D-115),另加只读快照 `OrderNow` / `HiddenNow` /
`EditNow` / `CardsNow` / `CardOf(id)` / `TrayIdsNow` / `TrayShownNow` / `BarShownNow` /
`DraggingNow` / `DragSourceNow`,以及 `DashHost.CardView` 的 `GrabCore` / `DragTo` / `DropNow` /
`ModeNow` / `EditMaskNow` / `HandleLayerNow` / `CloseNow` / `BodyNow` / `TitleNow` / `EditNow` 与
`DashHost.ViewsNow` / `GridLayerNow` / `ContentsNow`、`PageLayoutStore.ResetCache()`(模拟重启重读盘)。

### 3. Windows → Avalonia 差异(逐条 + 理由)

`BlockHost.cs`:

1. 框架类型:`UIElement` / `FrameworkElement` → `Control`;`Visibility.Collapsed` → `IsVisible = false`;
   `MouseButtonEventArgs` → `PointerPressedEventArgs`、`MouseEventArgs` → `PointerEventArgs`、
   `MouseLeftButtonUp` → `PointerReleased`、`LostMouseCapture` → `PointerCaptureLost`;
   `CaptureMouse()` / `ReleaseMouseCapture()` / `IsMouseCaptured` → `IPointer.Capture(元素)` / `Capture(null)`
   (Avalonia 的捕获挂在"指针"而不是控件上)。
2. 光标:`Cursors.SizeAll` → `new Cursor(StandardCursorType.SizeAll)` —— Avalonia 11 **没有** `Cursors`
   静态类(`Avalonia.Base.dll` 里查无此串),`Ui.RowCard` 已经是这个写法。
3. 事件阶段:WPF 用 `PreviewMouseLeftButtonDown`(隧道)。Avalonia 的 `Button` 类处理器会把
   `PointerPressed` / `PointerReleased` 标记为已处理,所以本类统一用
   `AddHandler(事件, 处理器, RoutingStrategies.Bubble, handledEventsToo: true)`;
   `PointerCaptureLost` 是 **Direct** 路由事件,用两参重载订阅。三个处理器都存成字段,
   `AddHandler` / `RemoveHandler` 用同一个委托实例(不泄漏)。
4. 主题变更:Windows 订的是 **App 级** `App.ThemeVisualsChanged`;**mac 的 `App` 根本没有这个事件**
   (C2b 起外壳走 `ActualThemeVariantChanged` + `ApplyThemeVisuals` 自己刷,见
   `MainWindow.cs:1066` 的注释)。所以改成订控件自己的 `StyledElement.ActualThemeVariantChanged`
   —— 同样是"有效主题变体变了"这一刻,同样在 `Loaded` 订阅 / `Unloaded` 取消 / 订阅后立刻刷一次,
   与 Windows 逐行对应。
5. 取主题色:`Application.Current.TryFindResource("B.Accent") as SolidColorBrush` →
   `Palette.TryGet("B.Accent", Palette.Current, out ...)`;取不到时**沿用 Windows 的兜底绿**
   `Color.FromRgb(46,155,107)`(不是 `Palette.FallbackColor` 的灰)。
6. Toast:`AppServices.Window?.Toast(...)` → `AppServices.Toast(...)`(C2a 起的统一门面,
   没有宿主时降级成调试输出)。
7. `Ui.G("Auto", "*")` 后面写 `grid.Add(_bodyWrap, 1)`(只定义了 1 行却放进第 2 行)——**Windows 源本身
   的写法,逐字照抄**,并用用例钉住实际落位(WPF 与本机 Avalonia 都把越界行号夹到最后一行)。
8. "内容已挂在别的 ContentControl 上就先摘下来":Windows 判的是 `Parent is ContentControl`;
   Avalonia 里内容控件的 `Parent` 是模板里的 **ContentPresenter**(不是 ContentControl),
   所以两种父级都认,并用 `TemplatedParent` 认出"是不是自己这具 ContentControl"。

`DashHost.cs`:

1. 第 1 条同上(坐标 / 指针捕获换法)。
2. 光标映射:SizeNS → `SizeNorthSouth`、SizeWE → `SizeWestEast`、SizeNWSE → `TopLeftCorner`、
   SizeNESW → `TopRightCorner`、SizeAll → `SizeAll`(Avalonia 的 `StandardCursorType` 用角标命名,
   语义同为"沿该对角线缩放")。
3. `Rect.IntersectsWith` → `Rect.Intersects`(同名不同词);其余几何算式一字未改。
4. 网格底纹:WPF 的 `DrawingBrush` 用 `Viewport` + `ViewportUnits.Absolute` 定瓷砖尺寸;Avalonia 的
   `DrawingBrush` 没有 `Viewport`,等价物是 `SourceRect` / `DestinationRect` 都取 (0,0,grid,grid) 的
   **Absolute** 相对矩形 + `Stretch.Fill`。渲染意图一致(每 grid 像素一道主题色细线),
   像素级细节在 mac 上无法与 WPF 对比(已实测:本机 Avalonia 取到的是 `RelativeUnit.Absolute`
   的 16×16 瓷砖)。
5. 主题变更、取主题色同 BlockHost 的第 4 / 5 条。
6. `CardView.SetContent` 复用了 `BlockHost.DetachFromForeignHost` 这一份实现(Windows 在两个文件里
   各写了一遍同样的判断;这里只留一份,免得两处漂移)。
7. 拖拽 / 缩放入口拆成"事件薄壳 + internal 状态机"(见 D-115)。

`PageLayoutStore.cs`(新搬的模型层):

1. 数据目录:Windows 用 `BridgeHost.FindRoot()`(payload 根);mac 版没有 payload,
   `BridgeHost.FindRoot()` 就是 `Utils.ResolveRoot()`(`~/.tipcl`,`TIPCL_HOME` 可覆盖),
   调用形状与 try/catch 兜底一字未改。
2. **原子落盘**:Windows 是 `File.Replace(tmp, path, null)`,mac 改成
   `File.Move(tmp, path, overwrite: true)` —— 见 D-116。语义不变(先写 `.tmp` 再改名,不留半截文件),
   而且没有"只在一个平台上跑得到"的分支。

### 4. 必须更正的任务书前提(2 条)

1. **「这两个文件自包含,依赖都已移植」不成立。** 实测 `Pages\BlockHost.cs` 还引用了
   `PageLayoutDoc` / `BlockState` / `PageLayoutStore` / `PageLayoutIO`,`Pages\DashHost.cs` 还引用了
   `DashCard` / `DashLayout` —— 这 **5 个类型在 mac 侧根本还不存在**(分别在 Windows 的
   `Pages\PageBlock.cs`、`PageLayoutStore.cs`、`PageLayoutIO.cs`、`DashCard.cs`、`DashLayout.cs`)。
   它们都很小(2.5–6.9 KB)、纯 `System.Text.Json`、不碰界面类型,所以按任务书"小而自包含就一并搬"
   的规定**一起搬了**(见 D-117)。任务书列的那几个 `Ui.` / `I18n.` / `SPanel` / `Ico.` /
   `AppServices` / `Motion.` 计数器是对的,但**漏了模型层这一整块**。
   附带收益:仓库里本来就有的两个 Windows 测试文件 —— `Ported\PageLayoutTests.cs`(15 条)与
   `Ported\LayoutDefaultTests.cs`(5 条)—— 因此**恢复了编译**,本轮新增用例里有 20 条来自它们
   (`PageLayoutTests.cs` 只按 A6 的 B 类规则改了 1 处:临时目录从 `Path.GetTempPath()` 改成
   `TestHome.Root`,断言一字未改;`LayoutDefaultTests.cs` 一字未改)。
2. **`BlockHost.BlockCard` 的网格定义是真的对不上**:`Ui.G("Auto", "*")` 只定义了 1 行,紧接着
   `grid.Add(_bodyWrap, 1)` 放进第 2 行(`DashHost` 同位置写的是 `Ui.G("Auto,*")`,两行,
   说明这是 Windows 源里的笔误)。**没有借移植之机改它**:逐字照抄,并用用例钉住实际落位
   —— WPF 与 Avalonia 都把越界行号夹到最后一行(实测本机 Avalonia 的卡片高度 = 该行内容高度,
   与"两行"写法在常态下视觉等价:操作条常态收起,只占 0 高度)。

### 5. 验证命令与实际结果(本机 Windows,全部由本包亲自执行)

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 个错误**;**7 个警告**(用 `WarningsOnly` 逐个取出来看:唯一警告码只有 `NU1900` ×6 + `SYSLIB0014` ×1 —— `NU1900` 是 4 个工程 × 2 个 Logger 上下文,`SYSLIB0014` 仍只有 `TipCL.Core\Launcher\Sync\PhoneClient.cs:116` 那一处) |
| `dotnet test` 第 1 / 2 次 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 都是**退出码 0,失败 0 / 通过 2897 / 总计 2897**(2797 → 2897,+100) |
| 进程内执行器 第 1 / 2 次 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 都是**退出码 0,通过 2897 / 失败 0 / 不支持 0** |
| `--smoke` light | `TipCL.exe --smoke --theme light --out artifacts\c3a-smoke-light.png` | 退出码 **0**,**21823 字节**,SHA256 `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B` —— **与 C2b 基准逐位相同** |
| `--smoke` dark | `... --theme dark ...` | 退出码 **0**,**18200 字节**,SHA256 `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E` —— **逐位相同** |
| `--smoke` blue | `... --theme blue ...` | 退出码 **2**(参数错,符合预期),不产出文件 |
| `src\TipCL.Core` 有没有被动过 | `git diff --name-status 145dbac HEAD -- src/TipCL.Core` | **0 行输出**(空) |
| Core 对 Windows 源的同一性 | 逐文件 SHA256(mac `src\TipCL.Core\**\*.cs` ↔ Windows `src\TipCL\Core\**\*.cs`) | **238 有对应 / 216 一致 / 22 有改动 / 10 无对应**(与基线完全相同)。口径说明:这 10 是指**源文件**;若把 `obj\Release\net8.0\` 下 2 个自动生成文件也算进去,总数的分母会变成 250 / 12,本包两种口径都取到了同一批文件 —— 即"改动的 22 个"一字未变 |
| `%LOCALAPPDATA%\TipCL` | 开工时 / 收工时各量一次 | 两次都是 **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 给的基线逐项相同(零写入) |
| 真实桌面 / 图片 | 收工时 | 桌面 14 项、最新写入 **2026-11-12 19:02:53**(与基线相同);图片最新写入 **2026-11-06 23:39:03**(与基线同日,零写入)。图片的**条目数**我量到 5 而 Lead 报 3 —— 差别只在"算不算 `-Force` 才看得见的隐藏项",最新写入时间一致 |
| 残留进程 / `%TEMP%` | `Get-Process testhost,TipCL,java,adb` / `Get-ChildItem $env:TEMP -Directory -Filter tipcl*` | 进程 **0 个**;`tipcl*` 目录 **91 个**(Lead 基线 89)。**对照实验**:单跑一次 `--smoke` 前后 **91 → 91(0 新增)**;多出来的 2 个是 14:46:59 建的 `tipcl-smoke-d340b8b0` / `tipcl-smokecwd-9b09662d`,这两个名字在本仓库 `src` / `tests` 里**根本不存在**(`smokecwd` 全仓库零命中),与 B2a / C2c2 记过的"同机还有别的产品线在跑 `--smoke`"是同一现象 |
| 编码 | 对 7 个新源文件 + 4 个新测试文件 + 2 个改动文件逐个 `ReadAllBytes` | **无 BOM、CR=0、U+FFFD=0**;汉字可 grep(`C3aBlockHostTests.cs` 1669 个、`C3aDashHostTests.cs` 981 个、`C3aLayoutModelTests.cs` 576 个、`C3aFixtures.cs` 304 个);`Ported\PageLayoutTests.cs` 改后 9051 字节、303 个汉字、无 BOM / CR / 乱码 |
| 提交 | 3 个提交 | `4107699`(BlockHost + 模型层)/ `ae171ce`(DashHost + DashCard/DashLayout)/ `7a5a451`(100 条用例 + 恢复两个 Ported 文件);每个 `git add -A` 前都 `git status --short` 确认过没有 `Zz*.cs` 或仓库根下的散落文件 |

新增用例的分布:`C3aBlockHostTests`(37 条)/ `C3aDashHostTests`(29 条)/ `C3aLayoutModelTests`(14 条)
共 80 条新写的,加上恢复编译的 20 条(`Ported\PageLayoutTests` 15 条 + `Ported\LayoutDefaultTests` 5 条)
= **100 条**(2797 → 2897,与实际增量逐条对得上)。

### 6. 测试怎么驱动布局(不模拟真实输入设备)

- 每条碰 Avalonia 的用例体都走 `AvaloniaSession.Run(...)`(D-79 / C1b-fix),没有第二个
  `SetupWithoutStarting`,`AvaloniaSession.cs` 一字未改;控件要"进真可视树 + 真布局"时用 C1c 已有的
  `UiHost.Show(...)`(它内部就是 `Dispatcher.UIThread.RunJobs` + `ForceRenderTimerTick` 那一套)。
- **块拖拽**:驱动 `BlockHost.BeginDragCore(card, src, null)` / `DragTo(y)` / `EndDrag(src)`
  这一层状态机;真实事件处理器(`BeginDrag` / `OnDragMove` / `OnDragEnd`)只多做两件事 ——
  从事件对象里取坐标、判左键 —— 然后调同一批成员。**没有**构造 `PointerPressedEventArgs`,
  也**没有**用 `Avalonia.Headless` 的鼠标模拟(那属于"模拟真实输入设备")。
- **卡片拖拽 / 缩放**:驱动 `CardView.GrabCore(mode, point, pointer, captureTarget)` / `DragTo(point)` /
  `DropNow()`,真实处理器(`OnGrabMove`、八向手柄的 `PointerPressed`、`OnDragMove`、`OnDrop`)
  同样只是取坐标 + 判左键。
- 几何用例分两种宿主:离屏(没排过布局)宿主→ `H()` 全走 120 兜底,可以写精确期望值
  (MidY(0)=60 / MidY(1)=190 / MidY(2)=320);挂进窗口的宿主→ 期望值从 `host.Bounds` 与
  `host.MidY(...)` 现算,不猜像素。
- 动画:全程 `Motion.Enabled == false`(`Motion.HasClock` 需要真桌面生命周期),所以
  `StaggerItems` 直接返回、没有任何计时器被挂上;用例断言"再推一次时钟状态不变"、
  `Opacity == 1`、`RenderTransform == null`、拖拽结束后 `DraggingNow == false` /
  `DragSourceNow == null`、重复收尾不重复通知。
- 落盘全部走 `C3aHome` 注入的 `PageLayoutStore.DirOverride`(`TestHome.Root` 之下),
  Dispose 里还原并丢缓存,不与 `Ported\PageLayoutTests` 串扰(全程序集本就关了并行)。

### 7. 未验证 / 遗留

- **macOS 真机未验证**:渲染、`--smoke` 像素、`File.Move(overwrite: true)` 在 APFS 上的行为、
  `Utf8JsonWriter` 缩进输出在 mac 上的换行符(本机实测是 CRLF;**同一个 API**,但本机只能验 Windows)。
- **真实指针路径未在真输入下验证**:`AddHandler(..., handledEventsToo: true)` 那一套是按
  `Button` 类处理器会标记已处理的语义推导的;本机只验证了状态机(处理器薄壳的行为等价性未用真输入驱动)。
- `PointerCaptureLost` 是 Direct 事件、两参重载能收到 —— 本机没构造过该事件。
- 主题切换:用例直接调 `RefreshThemeVisuals()` / `RefreshTint()` 验效果,
  `ActualThemeVariantChanged` 在真桌面切主题时是否真的传到宿主**未验证**(它需要真桌面生命周期)。
- `DashHost` 的 `private const double Gap = 10.0` 在 Windows 源里其实**没被用到**(排布用的是字面量
  10.0)—— 逐字保留,`ArrangeOverride` 里的 10.0 也照抄,没有"顺手改成 Gap"。
- `DashHost.CardView` 里同一 type 的两张卡会共享同一份内容控件(Windows 也是这个行为:
  `Cached(type)` 只造一次),后建的那张会把内容从先建的卡上摘过去 —— 已用例钉住现状,
  页面移植时按 type 区分即可。
- C3a 没有接线到任何页面或外壳,所以 `--smoke`(空壳、无页面)逐位不变是**应该**的。
- 下一包(C3b)要注意两处机械但必须改的调用点:`LaunchPage` 里
  `_blocks.Root.SetBinding(FrameworkElement.MaxHeightProperty, ...)` 要改成 Avalonia 的
  `Layoutable.MaxHeightProperty`(或直接用 `Ui.WindowPctConverter` 那套 `Bind`),
  `_hero` / 内容边距 `new Thickness(56,0,24,18)` 照抄即可。

### Lead 验收(2026-11-13 15:05)

结论:**C3a 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 四个提交(`4107699` / `ae171ce` / `7a5a451` / `52b589c`) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`NavModel/NavConfig`、`Services\`、`MainWindow.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| 新增文件 | `src\TipCL.App\Pages\` 下 6 个:`BlockHost.cs` / `DashHost.cs` / `DashCard.cs` / `DashLayout.cs` / `PageBlock.cs` / `PageLayoutStore.cs` / `PageLayoutIO.cs`(7 个),与子代理的清单一致 |
| **恢复的移植测试有没有被改弱(Lead 逐项比对)** | `Ported\PageLayoutTests.cs`:**断言 47 -> 47、用例 15 -> 15**,唯一改动是实例临时目录 `Path.GetTempPath()` -> `TestHome.Root`(A6 B 类规则);`Ported\LayoutDefaultTests.cs`:**diff 完全为空**(一字未改) |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告码只有 `NU1900` x6 + `SYSLIB0014` x1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2897 / 总计 2897**(2797 -> 2897,+100 = 新写 80 + 恢复 20) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2897 / 失败 0 / 不支持 0** |
| `--smoke` light / dark / bad | 退出码 0 / 0 / **2**;light **21823 `8336CA13...`**、dark **18200 `DBD3C171...`** —— **与基准逐位相同**(空壳无页面,本包不该画任何新东西) |
| 全部改动文件编码(Lead 按字节核验) | **无 BOM、CR=0、U+FFFD=0、无连续问号** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 桌面 / 图片 / 残留进程 | 9328 / 889530606 / 2026-11-10 20:42:16;桌面 14 项、最新 2026-11-12 19:02:53(**不变**);图片最新 2026-11-06(**零写入**);残留进程 **0 个** |
| `%TEMP%` | 本包报的 2 个外来目录(`tipcl-smoke-*` / `tipcl-smokecwd-*`)Lead 复核:名字在本仓库 `src`/`tests` 里**零命中**,且收工时总数已回到 **89** —— 判定非本包产物,与 B2a / C2c2 记过的「同机另有产品线在跑 `--smoke`」同一现象 |

**裁决 1:接受依赖扫描的更正。** Lead 的扫描只数了 `Ui.`/`I18n.`/`SPanel`/`Ico.`/`AppServices`/`Motion.` 的引用次数,**漏了模型层**:`PageBlock.cs`(PageLayoutDoc/BlockState)、`PageLayoutStore.cs`、`PageLayoutIO.cs`、`DashCard.cs`、`DashLayout.cs` 这 6 个文件在 mac 侧完全不存在。子代理按「小而自包含就一并搬」处理,搬到 `src\TipCL.App\Pages\`,**Core 一字节未动** —— 正确。

**裁决 2:接受 `File.Replace` -> `File.Move(tmp, path, overwrite: true)`(D-116)。** Windows 用 `File.Replace` 做原子落盘;Avalonia/mac 侧 `File.Move(overwrite: true)` 同样是 `rename(2)` 语义(原子),而 `File.Replace` 在 Unix 上行为不可靠、本机又验不了;更关键的是该文件的 `Save` **把异常全吞掉**,一旦真出问题表现就是「布局静默不落盘」。这是一处有理由的最小偏差,记在案。

**裁决 3:Windows 源自身的笔误不修、只钉住。** `BlockHost.BlockCard` 写 `Ui.G("Auto","*")`(只有 1 行)却在下一行 `grid.Add(_bodyWrap, 1)`;同位置 `DashHost` 写的是 `Ui.G("Auto,*")`。WPF 与本机 Avalonia 都把越界行号夹到最后一行,行为一致 —— 忠实保留是对的。`DashHost` 里未被使用的 `private const double Gap` 同理逐字保留。

**给 C3b 的机械改动点(子代理点名)**:`LaunchPage` 里 `_blocks.Root.SetBinding(FrameworkElement.MaxHeightProperty, ...)` 要换成 Avalonia 的 `Layoutable.MaxHeightProperty`(或走 `Ui.WindowPctConverter` + `Bind`)。
---

## C3b 启动页需要的两个小对话框:`CrashUi` + `VersionSetupDialog`(2026-11-13)

工作包:把 Windows `Pages\CrashUi.cs`(5035 字节)与 `Pages\VersionSetupDialog.cs`(5078 字节)
移植到 Avalonia 11 / net8,好让随后的 `LaunchPage`(C3c)移植保持机械。**不碰** `LaunchPage.cs`、
不碰外壳 / 导航 / 调色板 / 样式表 / 其它已移植页面、`src\TipCL.Core` 一字节未改。

### 1. 先核实任务书的依赖扫描(结论:门面那几个数对,但**漏了两个模型类型**)

任务书给的计数逐项复核,**全部吻合**:

| 文件 | `Ui.` | `Dlg.` | `I18n.` | `Motion.` | 复核命令 |
|---|---|---|---|---|---|
| `CrashUi.cs` | 12 | 3 | 28 | 0 | `[regex]::Matches(...)` 逐项计数 |
| `VersionSetupDialog.cs` | 30 | 1 | 45 | 0 | 同上 |

但"这两个文件自包含"**不成立**(与 C3a 的 D-117 同一类问题):它们还引用了两个在 mac 侧
**根本不存在**的模型类型 —— `TipCL.Models.OpResult`(240 字节,`CrashUi` 的动作返回值)
与 `TipCL.Models.VersionSettingsDto`(1136 字节,版本设置的传输对象)。两个都
**小而自包含**(纯 `System.Text.Json`、不碰界面类型),所以按任务书"小而自包含就一并搬并说明"
的规定用 `Copy-Item` **字节级**搬进 `src\TipCL.App\Models\`(与 Windows 源 SHA256 一致、无 BOM)。
**另外发现一个真的缺口,这一块没搬**:`AiFixWindow`(Windows 侧 22662 字节的**整窗**)在 mac 侧不存在,
属 AI 全家(C7+),按红线"整页 / 大服务缺失就停下并报告"处理 —— 见第 4 条差异与第 7 条遗留。

### 2. 两个对话框各做什么

**`CrashUi`(崩溃 / 异常退出对话框)** —— 输入一份 `CrashReport`,输出**一个布尔**:

- 标题取 `Headline`,空则退 `Title`,都空用「游戏异常退出」;依次排摘要、`退出码 {0}{1}`(退出码为 null 就不排)、帮助、
  日志框(`Input.Log` 样式、高 190、换行);
- `Actions` 每项一个 `BtnKind.Soft` 按钮,点击 → `apply_crash_action`(`{action, report}`)→ Toast「已处理」/「没能处理」,
  处理期间按钮禁用;
- `gameError: true` 时多一个主按钮「交给 AI 修复」;
- 5 个固定工具按钮(`BtnKind.Chip`):打开日志文件(`open_crash_file`)/ 导出报告(`Dlg.SaveFile` → `export_crash_report`)/
  上传给开发者(`submit_crash_report`)/ 附说明反馈(`Dlg.Ask` 收一段说明 → `submit_crash_feedback`)/ 复制全文;
- 最后 `Dlg.Choose(「启动出问题了」, 面板, [关闭, 重新启动], 760)`,`== 1` 即"要重新启动"。

**`VersionSetupDialog`(版本设置)** —— 先读后写,**没有返回值**(结果只经 Toast):

- `get_version_settings`(失败 / 缺口用 `VersionSettingsDto` 的初值兜)+ `get_accounts`(列表**头部插入**「(不绑定)」);
- 14 行字段:版本隔离(4 档)/ 独占内存 / JVM 参数 / 垃圾回收器(5 档)/ 直连服务器 / 端口 /
  启动前命令 + 等待开关 / 退出后命令 / 窗口标题 / 窗口模式(3 档)/ 绑定账号 / 统一通行证 / 在版本列表中隐藏;
- `Ui.Scroll` + `MinWidth 560` + `BindWindowMaxHeight(0.55, 260)`,用 `Dlg.Ask(「版本设置 · 」+ version, ..., 「保存」)`;
- 点「保存」才把 14 个键的字典写回 `save_version_settings`(`memory_mb` 只在"能解析成正整数"时写数字,否则写 `null`),
  成功 Toast「已保存」、失败 Toast「保存失败」+ 消息。

### 3. 这一包交付的成员清单(LaunchPage / TasksPage / LibraryPage / VersionManagePage 会用到的)

| 成员 | 形状 | 说明 |
|---|---|---|
| `CrashUi.ShowAsync(CrashReport report, bool gameError = false)` | `public static Task<bool>` | **与 Windows 同名同参同默认值**;Windows 的调用点:`LaunchPage:1871`、`TasksPage:210`(`gameError: true`) |
| `VersionSetupDialog.ShowAsync(string instance, string version)` | `public static Task` | **与 Windows 同名同参**;Windows 的调用点:`LaunchPage:1635`、`LibraryPage:717`、`VersionManagePage:562` |
| `CrashUi.ClipboardWriter` | `internal static Func<Control, string, Task<bool>>?` | **mac 侧新增**:剪贴板注入缝(测试按它断言"走的是缝、不是真剪贴板") |
| `CrashUi.AiFixLauncher` | `internal static Action<CrashReport>?` | **mac 侧新增**:「交给 AI 修复」的挂点(AiFixWindow 落地后接这里) |
| `CrashUi.ClipboardPlatformAttempts` | `internal static int` | **mac 侧新增**:平台剪贴板路径被走到的次数(只增不减;测试用它钉红线) |
| `CrashUi.HeadOf(CrashReport)` / `CrashUi.CopyText(string, CrashReport)` | `internal static string` | **mac 侧新增**:从 `ShowAsync` 里抽出来的两个纯函数(逻辑一字未改,便于单测) |
| `CrashUi.CopyAsync(Control owner, string text)` | `internal static Task<bool>` | **mac 侧新增**:写剪贴板的唯一入口(缝优先 → 离屏跳过 → 平台) |

命名空间与 Windows 一致(`TipCL.Pages` / `TipCL.Models`),所以页面移植时 `using` 都不用改。

### 4. Windows → Avalonia 差异(逐条 + 理由)

`CrashUi.cs`:

1. **剪贴板**:WPF 是静态 `Clipboard.SetText(head + "\n" + Summary + "\n\n" + Detail)`,外面一个空 `catch`。
   Avalonia 11 **没有**静态剪贴板入口,剪贴板挂在 `TopLevel` 上、且只有 `SetTextAsync`。这里改成
   `CopyAsync`:`ClipboardWriter` 接了就只走缝 → 离屏(`Dlg.SmokeActive`)直接返回 false 不碰平台 →
   否则走 `TopLevel.GetTopLevel(owner)?.Clipboard`。取不到实现、或写失败、或抛异常,一律返回 `false`
   且**不弹**「已复制」—— 与 Windows"先 SetText 成功才 Toast、失败静默"逐条等价。
   离屏那一条分支是 mac 侧新增的(与 `Dlg` 在离屏下不弹真文件选择器同一口径)。
2. **「交给 AI 修复」**:Windows 直接 `new AiFixWindow(report).Show()`。那个窗口 22.6 KB、
   依赖 AI 页整套基础设施(`AiRisk` / `ToolMeta` / `AiUiText` / `AiIcons` / `MdView`),属 AI 全家(C7+),
   **不在本包范围,也没有搬**。按钮照建(文案 / 图标码位 / 提示语 / `BtnKind.Primary` 全照 Windows),
   点击走 `AiFixLauncher`;挂点没接时弹一句 Toast「暂未实现 / AI 修复窗口尚未移植」——
   **不做静默空操作**(静默会让人以为点了没反应是 bug)。这是一处**用户可见的**行为差异,已记入 D-119
   并请 Lead 裁决(要么接受这个降级,要么把 AiFixWindow 排进 C7)。
3. `TextBox.Style = Ui.S("Input.Log")` → `StyleKeys.Apply(log, "Input.Log")`:Avalonia 没有"按键取样式",
   C1b 把每个 WPF 样式键固定翻译成一个类名,C1c 的 `Ui.S` 返回的就是那个类名。
4. `button.ToolTip = ...` → `UiExt.Tip(button, ...)`(Avalonia 的 ToolTip 是挂载属性 `ToolTip.SetTip`)。
5. `UIElement` / `FrameworkElement` → `Control`;`Thickness` → `Avalonia.Thickness`;`WrapPanel` / `TextBox` /
   `Button` 都是 Avalonia 同名类型。`I18n.L` / `Dlg.Choose` / `Dlg.SaveFile` / `Dlg.Ask` / `Ui.*` / `PageBase.Run` /
   `AppServices.*` 在 C1c / C2a 已经是同名同参,mac 版一字未动。
6. 忠实保留:动作列表为 null 时照样塞一个空的 `WrapPanel`(`report.Actions ?? new List<CrashAction>()`,
   逐字照抄,没有"顺手"加判空);动作闭包在 `foreach` 里捕获循环变量(每轮一份,与 Windows 一致)。

`VersionSetupDialog.cs`:

1. 只有框架类型差异(`UIElement`/`FrameworkElement` → `Control`)。其余逐行照抄:
   `Math.Max(0, Array.IndexOf(...))` 夹越界、`acc.SelectedIndex <= 0 ? "" : acc.Str()`、
   `winMode.SelectedIndex switch { 1 => "fullscreen", 2 => "maximized", _ => "window" }`、
   `int.TryParse(mem.Text?.Trim(), out int memory) && memory > 0` 才写数字否则写 `null`。
2. 忠实保留:`IsoLabels` / `GcLabels` 是 `static readonly`,文案在**类型初始化**那一刻经 `I18n.L` 定死
   (Windows 也是这样;运行中切语言不会重算这两张表)。
3. Windows 里的 `dictionary2` / `dictionary3` 只是同一个字典的别名,这里直接写 `payload`
   (键名、赋值顺序、类型一字不差;声明成 `Dictionary<string, object?>` 好让 `memory_mb = null` 诚实)。
4. **一处值得记的行为细节(本机实测)**:`VersionSettingsDto.Isolation` 的**属性初值是 `"none"`**
   (`Models\VersionSettingsDto.cs:7`),而 `IsoValues` 的第 2 项正好是 `"none"` —— 所以后端给空对象
   (或读取失败走 `TryCallAsync` 兜底)时,版本隔离落在**「关闭(共用实例目录)」**而不是第 0 项
   「跟随全局」。第 0 项只留给"值不在候选表里"(如 `"bogus"`)这一支。
   这是 Windows 源码本来就有的行为(代码与 DTO 都是照抄的),不是移植引入的,用两条用例钉住了。

### 5. 测试(新增 71 条用例;总数 2897 → 2968)

`tests\TipCL.Tests\` 新增 3 个文件:`C3bFixtures.cs`(夹具)、`C3bCrashUiTests.cs`(40 条)、
`C3bVersionSetupTests.cs`(31 条,含 Theory 数据行)。

- 每条碰 Avalonia 的用例体都走 `AvaloniaSession.Run(...)`(D-79 / C1b-fix),没有第二个
  `SetupWithoutStarting`,`AvaloniaSession.cs` 一字未改;
- 驱动对话框一律**"先把对话框推出来、再点按钮"**(D-113);点按钮用 `RaiseEvent`,**不模拟真实输入设备**;
- 后端走 C2a 已有的回环假后端 `FakeBridgeServer`(`TcpListener` 绑 `127.0.0.1:0`,**不是** `HttpListener`,见 D-27)
  —— 不起真 `BackendApi` / `BridgeServer`、不出网、不弹真文件选择器(导出那条走 `Dlg.SmokeActive`);
- 剪贴板**只**走 `CrashUi.ClipboardWriter` 注入缝,并断言 `ClipboardPlatformAttempts` 增量恒为 0
  —— 这就是"测试绝不碰真剪贴板"那条红线的机器化证明;
- 全局静态由 `C3bScope` 快照还原(`AppServices.Client` / `ToastSink` / `Dlg.Host` / `Dlg.BlurTarget` /
  `Dlg.SmokeActive` / 两个 Smoke 挂点 / `Motion.Enabled` / `Motion.Scale` / 两个 `CrashUi` 挂点),
  D-69 的纪律;
- 收摊两件套:关掉之后宿主空 / 遮罩落到终态 / 再泵 20 轮不冒新东西 / `PendingOperations == 0`,
  以及"同一个按钮连点两次不出事"(两条通道都有)。

**踩到的坑(如实记,见 D-120)**:第一版测试全跑出来后是 **2968 用例 / 23 失败**,拆开看只有**两处真问题**,
其余 21 条全是**会话线程被一条死等用例占死**之后的连锁超时(C1b-fix 记过的同一现象):

1. 一条断言写错(把整个卡片的 `TextBlock` 当成了面板的行;卡片标题与按钮里的文字也被数进来了)
   → 改成直接钉面板的子项个数;
2. 一条用例在**对话框还开着**的时候去等对话框任务(`PumpTask(task)`) → 必然等不到 → 30 秒会话超时。
   更糟的是我第一版的泵按"轮数 × 2ms"计时,而 `UiHost.Flush()` 一轮的真实耗时远大于 2ms,
   于是我的 15 秒预算被拖成几十秒,**先超时的是 `AvaloniaSession` 的 30 秒**。修法(写进 D-120):
   泵用 `Stopwatch` 计真实时间、上限 9 秒(明显小于 30 秒),并新增 `C3bFiles.Settle()` 专门给
   "对话框还开着、只断言点了之后的效果"的用例。
   顺带修好另外两处期望值:`Ui.Txt(bold: true)` 用的是 `SemiBold`(=600,枚举别名 `DemiBold`)不是 `Bold`;
   Avalonia 的 `Layoutable.MaxHeight` 默认是 `PositiveInfinity` 而不是 `NaN`。

### 6. 验证命令与实际结果(本机 Windows,全部由本包亲自执行)

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 个错误**;**7 个警告**(用 `WarningsOnly` 逐个取出来看:**唯一警告码只有 `NU1900` ×6 + `SYSLIB0014` ×1**,与 C3a 基线逐项相同) |
| `dotnet test` 第 1 / 2 次 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 都是**退出码 0,失败 0 / 通过 2968 / 总计 2968**(20 秒 / 次) |
| 进程内执行器 第 1 / 2 次 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 都是**退出码 0,总计 2968 / 通过 2968 / 失败 0 / 跳过 0 / 不支持 0** |
| (追加一次进程内执行器,给 `%TEMP%` 做对照实验) | 同上一行 | 退出码 0,2968 / 2968 |
| `--smoke` light | `TipCL.exe --smoke --theme light --out artifacts\c3b-smoke-light.png` | 退出码 **0**,**21823 字节**,SHA256 `8336CA136B4F309D8275490578B8759E680AB5A1D8B861659D082802FE60449B` —— **与 C2b 基准逐位相同** |
| `--smoke` dark | `... --theme dark ...` | 退出码 **0**,**18200 字节**,SHA256 `DBD3C171D7E176F12F9CE05335E7E96A0CE21F1135A4CC1E2CCADD5564AD604E` —— **逐位相同** |
| `--smoke` blue | `... --theme blue ...` | 退出码 **2**(参数错,符合预期),不产出文件 |
| `--smoke`(第 4 次,对照实验) | `... --theme light --out artifacts\c3b-smoke-ab.png` | 退出码 0,21823 字节,**`%TEMP%` 下 `tipcl*` 前后 104 → 104(0 新增)** |
| `src\TipCL.Core` 有没有被动过 | `git diff --name-status bed79b0 HEAD -- src/TipCL.Core` | **0 行输出**(空) |
| Core 对 Windows 源的同一性 | 逐文件 SHA256(mac `src\TipCL.Core\**\*.cs` ↔ Windows `src\TipCL\Core\**\*.cs`) | **238 有对应 / 216 一致 / 22 有改动 / 10 无对应**(与基线完全相同) |
| 两个搬来的模型 | 与 Windows 源 `OpResult.cs` / `VersionSettingsDto.cs` SHA256 对比 | **240 / 1136 字节逐字节一致** |
| `%LOCALAPPDATA%\TipCL` | 开工时 / 收工时各量一次 | 两次都是 **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 给的基线逐项相同(**零写入**) |
| 真实桌面 / 图片 | 收工时 | 桌面 14 项、最新写入 **2026-11-12 19:02:53**(与基线相同);图片 3 项、最新 **2026-11-06 23:39:03**(零写入) |
| 残留进程 | `Get-Process testhost,TipCL,java,adb` | **0 个** |
| `%TEMP%` 下的 `tipcl*` | 开工时(Lead 基线 89)→ 收工时 `Get-ChildItem $env:TEMP -Force -Filter 'tipcl*'` | 收工时 **104 个**。**两次对照实验**:单跑一次 `--smoke` 前后 **104 → 104**;单跑一次进程内执行器前后 **104 → 104** —— **本包的两次运行都不产生 `%TEMP%\tipcl*`**。本包窗口内(15:31–15:43)新出现的名字有 3 个:`tipcl-smoke-249b9cd9…`(15:42:31)/ `tipcl-selftest-07e60d77…`(15:42:17)/ `tipcl-permnote`(15:31:37);这 3 个名字(以及同批的 `smokecwd` / `r9-smoke` / `r6-e2e` / `final-cli`)在本仓库 `src` / `tests` 里**零命中**,与 B2a / C2c2 / C3a 记过的"同机还有别的产品线在跑"是同一现象 |
| 编码 | 对本包新增 / 改动的 7 个源文件逐个 `ReadAllBytes` | 无一例外:**无 BOM(前 3 字节不是 `EF BB BF`)、CR=0、U+FFFD=0**;逐个文件核对了 31 处 `??` **全部是 C# 的空合并运算符**(唯一一处出现在注释里,是在引用 Windows 源码原文),不是乱码 |
| 提交 | 4 个提交 | `d9670a6`(CrashUi + OpResult)/ `823c3d9`(VersionSetupDialog + VersionSettingsDto)/ `246f53e`(71 条用例 + `CrashUi` 的计数成员)/ 文档提交见下一小节;每个 `git add -A` 前都 `git status --short` 确认过没有 `Zz*.cs` 或仓库根下的散落文件(诊断用的 `ZzC3bProbe.cs` 已删除,未进任何提交) |

### 7. 未验证 / 遗留

- **macOS 真机未验证**:Avalonia 的 `TopLevel.Clipboard` 在 macOS 上的真实行为(写入是否成功、
  是否需要主线程、沙箱权限)本机验不了 —— 本机没有 Mac,而且红线不许在测试里碰真剪贴板。
  平台那条分支在本机只走到"取不到实现 → 返回 false"的兜底(离屏与无头环境就是这一支)。
- **`AiFixWindow` 整块未移植**(红线要求停下并报告):因此「交给 AI 修复」在挂点没接时是
  "如实提示"而不是"真的开窗"。请 Lead 裁决(见 D-119)。
- **`BindWindowMaxHeight(0.55, 260)` 的实际绑定**在本机不生效:`Application.Current.ApplicationLifetime`
  在离屏 / 测试进程里是 null,而 C1c/C2b 定的口径是"没有宿主窗口就不绑、不抛"。用例钉的是
  "不绑 + `MaxHeight` 保持默认(`PositiveInfinity`)",真桌面上的绑定效果未验证。
- **`Dlg.SaveFile` 的真实文件选择器路径**未验证(测试里一律走 `Dlg.SmokeActive` 的取消支)。
- 两个对话框都**没有**接线到任何页面 / 外壳,所以 `--smoke`(空壳、无页面)逐位不变是**应该**的。
- 下一包(C3c)的机械改动点:照 C3a 的 Lead 验收结论,`LaunchPage` 里
  `_blocks.Root.SetBinding(FrameworkElement.MaxHeightProperty, ...)` 要换成 Avalonia 的
  `Layoutable.MaxHeightProperty`(或走 `Ui.WindowPctConverter` + `Bind`);
  `CrashUi.ShowAsync` / `VersionSetupDialog.ShowAsync` 的调用形状**不用改**。

### Lead 验收(2026-11-13 16:05)

结论:**C3b 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 四个提交(`d9670a6` / `823c3d9` / `246f53e` / `0e578f5`) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`NavModel/NavConfig`、`Services\`、`MainWindow.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| **两个新模型对 Windows 源 SHA256** | `OpResult.cs` **一致**、`VersionSettingsDto.cs` **一致**(字节级) |
| 仓库里有没有残留探针 | `git ls-files` 里 `Zz*` **0 个** |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误;警告码 = `NU1900` x6 + `SYSLIB0014` x1 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 2968 / 总计 2968**(2897 -> 2968,+71) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 2968 / 失败 0 / 不支持 0** |
| `--smoke` light / dark / bad | 退出码 0 / 0 / **2**;light **21823 `8336CA13...`**、dark **18200 `DBD3C171...`** —— **与基准逐位相同**(空壳无页面) |
| 全部改动文件编码 | **无 BOM、CR=0、U+FFFD=0** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 桌面 / 图片 / 残留进程 / `%TEMP%` | 9328 / 889530606 / 2026-11-10 20:42:16(**零写入**);桌面 14 项、最新 2026-11-12 19:02:53(**不变**);图片最新 2026-11-06(**零写入**);残留进程 **0**;`tipcl*` **92 -> 92**(我的复跑没新增) |

**裁决 1:接受依赖前提的第二次更正(Lead 的错)。** Lead 说这两个对话框「自包含」,**又不成立**:还缺 `Models\OpResult.cs`(240B)与 `Models\VersionSettingsDto.cs`(1136B)。子代理按「小而自包含就一并搬」处理,字节级搬进 `src\TipCL.App\Models\`,**Core 一字节未动** —— 正确,且我已核验两者与 Windows 源逐字节一致。这已经是同一类错误的第三次(壁纸位置、Toast/对话框文件身份、自包含判断),**教训:C3+ 的任务书里凡是 Lead 断言「自包含」的地方,子代理都必须自己读一遍依赖再动手**。

**裁决 2:`AiFixWindow` 的降级 —— 接受,挂点留给 C7。** `AiFixWindow`(22.6 KB,整窗)属 AI 全家,现在搬它会连带拖进 `AiIcons`/`MdView` 等基础设施。子代理的做法是:**按钮照 Windows 建**(文案/图标/提示/Primary 全同)、点击走新增注入缝 `CrashUi.AiFixLauncher`、**没接挂点时如实弹 Toast 说暂未实现**。这比「按钮点了没反应」或「桩一个空窗口」都诚实,也符合 GOAL §2.5。**C7 搬 AI 全家时必须把 `AiFixLauncher` 接上**,并复查这条降级消失。

**裁决 3:接受 D-120(对话框等待纪律)。** 子代理如实上报第一版 71 条里有 23 红,拆开只有 1 条断言写错 + 1 条「对话框还开着就等对话框任务」的死等,其余 21 条是会话线程被占死后的连锁超时;根因之一是它第一版按「轮数 x 2ms」计时,把 15 秒预算拖过了会话的 30 秒。修法与 C3+ 纪律已记进 D-120。**这条与 D-113/D-114 一起,是页面阶段必须遵守的三条测试纪律。**---

## C3c 启动页:`LaunchPage` + `LaunchPage.Popups` 移植并注册(2026-11-13)

工作包:把 Windows `Pages\LaunchPage.cs`(58383 字节 / 1922 行)与 `Pages\LaunchPage.Popups.cs`(9479 字节 / 338 行)
移植到 Avalonia 11 / net8,**注册成页面 id `launch`,让空壳变成真的应用**。
**不碰**外壳 / 导航 / 调色板 / 样式表 / 其它页面;`src\TipCL.Core` 一字节未改。

### 1. 这个页面做什么(与 Windows 同样的形状,WP-U3 之后的现状)

- **背景层**:当前实例(含版本隔离目录)`screenshots` 里最新的一张截图 → 没有就用内嵌 `hero.jpg` 全景图 →
  两张渐变遮罩(左→右、下→上);都没有就只是页面底色。tone 随"有没有图"整体切换
  (`ApplyHeroTone`:白字 + 半透明白玻璃块 ↔ 主题纸色 + 主题字色 + 主按钮柔光)。
- **英雄区**(左对齐、最大宽 680、边距 56/0/24/0):绿点 + 「就绪 · 游戏目录」/ 大标题(实例名,点开实例切换浮层)/
  小标签行(加载器 · Java · 内存 · 模组数)/ 主操作行(48 高「启动游戏」+ 同尺寸「停止」+ 配置钮 + 联机钮)/
  进度行(空闲时收起,状态一变就出现)。
- **内容区块**(`BlockHost("launch", ...)`,contentMargin 56/0/24/18):`news` 默认可见,
  `tasks` / `playtime` / `notes` / `log` 默认收起;整块 MaxHeight 绑页面高度的 62%。
- **三个浮层**(`LaunchPage.Popups.cs`):启动配置(实例/版本/用户/Java/内存/分辨率/直连 + 微软登录/皮肤站/更多操作/
  更多设置)、实例切换(搜索 + 当前标记 + 版本子行 + 管理 + 打开实例库)、公告(新闻列表)。
- **数据与状态机**:`FillCardData`(实例/账号/设置/版本/Java/模组数)、启动前的预检与多开确认、
  `OnEvent` 的 `game_started` / `game_exited` / `progress` / `log` / `finished` / `crash` / `login_*`,
  日志缓冲上限 2500 行,失败时弹 C3b 的崩溃卡(`gameError: true`)。

页面能处在的状态(本包按这些状态写了用例):**没有后端**(离屏现实:`AppServices.Client` 为 null → 只有静态布局)、
**已加载但没实例**、**有实例没版本**、**有实例有版本**、**正在启动/运行中**(进度条 + 停止钮)、
**跑完/失败**、**崩溃卡**、**切页离开**。

### 2. 从 Windows 复制/搬迁的文件,与那 6 个补上的依赖(依赖扫描的更正)

**Lead 说"这两个文件自包含、依赖都已移植"——第三次不成立。** 实测还差:

| 缺什么 | 大小 | 处理 |
|---|---|---|
| `Models\JavaOption.cs` | 201 B | `Copy-Item` 字节级搬进 `src\TipCL.App\Models\`(SHA256 与 Windows 源一致) |
| `Models\ModEntry.cs` | 272 B | 同上 |
| `Models\NewsRow.cs` | 183 B | 同上 |
| `Models\PreflightItem.cs` | 230 B | 同上 |
| `Models\PreflightResult.cs` | 216 B | 同上 |
| `Models\AuthlibPreset.cs` | 143 B | 同上 |
| `PremiumAssets\hero.jpg` | **275898 B** | 启动页默认背景;**Windows 侧无条件嵌入的 `<Resource>`,C2c2 只搬了 5 个加载器图标,漏了这张**。字节级复制 + `<AvaloniaResource>` 内嵌(GOAL §3 说"壁纸、PremiumAssets 全部直接内嵌") |

任务书列的 `Ui.` / `I18n.` / `Dlg.` / `Motion.` 门面引用逐项复核**全部吻合**(C1c/C2a 已经同名同参),
缺的只有**模型层与资源**,与 C3a(D-117)/C3b(D-118)是同一类问题 —— 见 D-121。

另外发现两个**服务层缺口**:
1. `PageBase.Win.FlyToTasks(...)`(Windows `MainWindow.xaml.cs:1336`,另有 13 处调用点在后续页面里)在 mac 侧**不存在**;
2. `Wallpaper` / `AccountGate` / `TaskStore` / `SmoothScroll.BindWindowMaxHeight` / `CrashUi` / `VersionSetupDialog`
   这些都在,调用形状一字未改。

### 3. Windows → Avalonia 差异(逐条,完整清单在 `LaunchPage.cs` 的类注释里)

框架类型换法(与 C3a/C3b 同一套):`UIElement`/`FrameworkElement` → `Control`;`Visibility` → `IsVisible`;
`MouseLeftButtonUp`/`MouseEnter`/`MouseLeave` → `PointerReleased`/`PointerEntered`/`PointerExited`;
`Cursors.Hand` → `new Cursor(StandardCursorType.Hand)`;`FontWeights.*` → `FontWeight.*`;
`SetResourceReference` → `UiExt.Dyn`;`Style = Ui.S(...)` → `StyleKeys.Apply`;
`BitmapSource` → `Bitmap` + 统一走 `ImageDecode`(Skia);`SolidColorBrush.Freeze()` 去掉;
`LinearGradientBrush.StartPoint` 在 Avalonia 是 `RelativePoint`。

本包特有的四处**机械但必须改**的点:

| # | Windows 写法 | mac 写法 | 理由 |
|---|---|---|---|
| 1 | `_blocks.Root.SetBinding(FrameworkElement.MaxHeightProperty, new Binding("ActualHeight"){Source=this,...})` | `_blocks.Root.Bind(Layoutable.MaxHeightProperty, new Binding("Bounds.Height"){Source=this,...})` | 两个原因:**Avalonia 没有 `SetBinding`**(等价物是 `AvaloniaObject.Bind`),而且**绑 `ActualHeight` 根本不生效**(本机实测:MaxHeight 一直留在 ∞)。`Bounds.Height` 是 C1c 的 `SmoothScroll.BindWindowMaxHeight` 已经定下的等价路径。这就是 C3a 的 Lead 验收点名的那一处 |
| 2 | `menu.PlacementTarget = anchor; menu.IsOpen = true;` | `menu.PlacementTarget = anchor; menu.Open(anchor);` | `MenuBase.IsOpen` 的 setter 在 Avalonia 不可见;而**无参 `Open()` 会抛 `ArgumentNullException("control")`** —— 这是页面用例抓到的**真 bug**(「更多操作」按钮点下去会崩),不是"移植风味"问题。见 D-124 |
| 3 | `DependencyPropertyDescriptor...AddValueChanged(_status/_prog, syncStatus)` | `PropertyChanged` 事件 + 判 `e.Property`,并且**进可视树订阅 / 出可视树退订** | WPF 那套 `DependencyPropertyDescriptor` 在 Avalonia 没有;订阅挂在长命页面对象上必须能摘(见第 6 节泄漏纪律) |
| 4 | `_log.VerticalOffset/ExtentHeight/ViewportHeight/ScrollToEnd()` | 取 `_log` 模板里那根 `ScrollViewer` 的 `Offset/Extent/Viewport/ScrollToEnd()` | Avalonia 的 `TextBox` 没有这四个成员;**取不到(还没套模板)就跳过自动滚动**,日志内容照旧刷新 |

其余忠实保留(不"顺手"修):`_user`(Player)这个字段在 Windows 的这一版里**没有放进配置面板**;
`_dash`(旧卡片画布 `DashHost`)字段保留但**不挂可视树**、页面也**不订阅它的变更**;
`_prog.BeginAnimation(RangeBase.ValueProperty, null)` 那行去掉(进度动画统一由 `Motion.Progress` 管);
`ShowMoreMenu` / `HideBlock` 等一批 private 降为 internal 只为能被**告知式地驱动**(同 D-115);
`MicrosoftLoginAsync` 的「复制代码」复用 C3b 的剪贴板唯一入口 `CrashUi.CopyAsync`(离屏/测试不碰真剪贴板)。

**`#if IMMERSIVE` 的标题栏沉浸那段没有移植**:Windows 的默认构建(`-p:Immersive=true` 才定义 `IMMERSIVE`)
里它根本不参与编译,而 mac 的外壳是系统红绿灯 + 侧栏,没有 Windows 的 `TopBar`/`WallHost` 结构。
`OnHidden` 因此与 Windows 的默认构建一样**不重写**(用例钉住了"调它什么都不做")。

### 4. 一处**用户可见的降级**(请 Lead 裁决,见 D-122)

Windows 的「修复当前版本」在启动任务后会调 `Win.FlyToTasks(_launchBtn, "修复中")` 播一个"飞向任务页"的小动画。
mac 的外壳没搬 `FlyHost`(C2b/D-97 的既有结论),而红线是**不许动外壳**,所以:

- 页面照 Windows 建:任务照跑(`repair_version`)+ 照常 Toast;
- 那个飞行动画走新增注入缝 `LaunchPage.FlyToTasksSeam`(签名 `Action<Control, string>`),**挂点没接时就不播**;
- 这不是静默桩:已在类注释、本节、D-122 里写明,并请 Lead 裁决(要么把 `FlyToTasks` 加进外壳,
  要么接受这条降级 —— 注意**后续 13 处调用点**(InstancePage / VersionManagePage / InstallPage / JavaPage /
  CatalogPage / MultiplayerPage / PluginsPage …)都会撞上同一个洞)。

### 5. 注册方式(不动外壳)

`src\TipCL.App\Pages\LaunchPageRegistration.cs`:`[ModuleInitializer]` 在**模块加载时**调
`PageRegistry.Register("launch", () => new LaunchPage())`。为什么不是别处:
外壳的构造函数第一件事就是 `Navigate("launch", instant: true)`(与 Windows 首屏一致),注册必须在那之前发生;
写进 `Program`/`App`/`MainWindow` 都要改外壳文件(红线)。`--smoke` 只 `new MainWindow`,所以它同样自动生效。
见 D-125。

### 6. 测试(新增 117 条用例;C2b 的一条拆成两条,总数 2968 → 3086)

| 文件 | 条数 | 覆盖 |
|---|---|---|
| `C3cFixtures.cs` | — | 期望值权威副本 + `C3cScope`(全局快照:**多了 `PageRegistry` / `LaunchPage.FlyToTasksSeam` / `TaskStore`**)+ `C3cToasts` + **按方法名路由**的回环假后端 `C3cFakeApi` + `C3cTaskStore`(任务队列作用域)+ 小工具 |
| `C3cPageStructureTests.cs` | 36 | 注册(模块初始化器 / 大小写敏感 / 幂等 / 外壳首屏 / 同实例复用)、无后端构造不抛、区块 specs、根 Grid、英雄区几何、类名 token、动态资源取色、内嵌 hero 能解码、背景回退到 hero、订阅生命周期 |
| `C3cPageStateTests.cs` | 57 | 加载(实例/账号/设置/版本/Java/模组数/新闻/时长/任务卡)、5 处状态文案、`LoaderChip` 7 组 Theory、Java 标签 3 组 Theory、点启动→`preflight_launch`→`launch_game` 参数逐项、`ExtraArgs` 3 组 Theory、多开确认两支、预检三支、失败还原、`game_*`/`progress`/`log`/`finished` 事件、日志 2500 上限、崩溃卡、别人的任务事件被忽略、`PreferInstance`/`LaunchNowAsync`、Java/分辨率/便签写回 |
| `C3cPageLayoutTests.cs` | 24 | 三个浮层(切换行/搜索过滤/版本与实例选中/没版本的提示/公告壳/几何/窗口百分比 MaxHeight/dev 摘取挂点)、更多菜单 7 项顺序与两项真点击、区块显隐/重排/重置**落盘到注入目录并重载复现**、DashHost 不落盘、关动画时立刻到位、计时器与订阅不留 |

**为什么自己写了个"按方法名路由"的假后端**:C2a 的 `FakeBridgeServer` 按"第几个请求"给预置响应,
而本页在 `FillCardData` 里既 await 一串调用、又 fire-and-forget 起 `ReloadJavaAsync(scan:true)` /
`LoadNewsAsync` / `LoadPlaytimeAsync`,到达顺序天生不确定 —— 按序号发响应必然时绿时红(D-103 记过的同类病)。
新夹具仍是 `TcpListener` 绑 `127.0.0.1:0`(不是 `HttpListener`,D-27),不起真后端、不出网、不启外部进程。

**驱动方式**:按钮走 `RaiseEvent`;**非按钮控件的点击**(标题行 / 新闻卡 / 玻璃钮 / 实例行 / 版本行)走页面为此留的
internal 处理器体(不构造 `PointerEventArgs`、不用 Headless 输入模拟 —— 同 D-115);
对话框走 `DlgHarness` + `C3bFiles.WaitDialog()`(先把卡片泵出来再点,D-113);
所有等待都是"一边泵 Dispatcher 一边看条件"(Stopwatch 计时、9 秒上限,D-120)。

**抓到的真 bug(1 个)**:`ContextMenu.Open()` 无参调用抛 `ArgumentNullException("control")`
—— 「更多操作」菜单在真实点击下必崩(冒烟里点不到,只有页面用例能发现)。修法是 `Open(anchor)`。

**C2b 两条用例按新契约拆分(断言只增不减)**:`Page_host_starts_empty_because_no_page_is_registered_yet`
断的是"还没有页面注册"那个前提,启动页注册之后它**不再成立**。拆成
`Page_host_opens_the_registered_launch_page`(新契约,断言更多)+
`Page_host_starts_empty_when_the_launch_page_is_unregistered`(用 `RegistryScope`+`Unregister("launch")` 把前提还原,
**原来的三条断言一字未改**);`Navigate_to_an_unknown_id...` 同样先 `Unregister("launch")` 再跑原断言。
**没有删测试、没有放宽断言、没有加 Skip**。见 D-126。

### 7. 验证命令与实际结果(本机 Windows,全部由本包亲自执行)

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 个错误**;**7 个警告**(用 `WarningsOnly` 取出:唯一警告码 = `NU1900` ×6 + `SYSLIB0014` ×1,与基线逐项相同) |
| `dotnet test` 第 1 / 2 次 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 都是**退出码 0,失败 0 / 通过 3086 / 总计 3086**(47 秒 / 次) |
| 进程内执行器 第 1 / 2 次 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 都是**退出码 0,总计 3086 / 通过 3086 / 失败 0 / 跳过 0 / 不支持 0** |
| 用例分布 | 按类过滤各跑一次 | `C3cPageStructureTests` 36 / `C3cPageStateTests` 57 / `C3cPageLayoutTests` 24 = **117** |
| `--smoke`(不带 `--page`) | `TipCL.exe --smoke --out artifacts\c3c-baseline-nopage.png` | 退出码 **0**,**37681 字节**,SHA256 `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` —— 与 light **逐位相同**(外壳首屏就是启动页) |
| `--smoke --page launch --theme light` | 同上加 `--page launch` | 退出码 **0**,**37681 字节**,`2603BF6A...` |
| `--smoke --page launch --theme dark` | 同上 `--theme dark` | 退出码 **0**,**33579 字节**,`7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` |
| `--smoke --theme blue` | 同上 `--theme blue` | 退出码 **2**(参数错,符合预期),不产出文件 |
| 冒烟确定性 | light 连跑 5 次 / dark 连跑 3 次(含重复实验) | **每次都逐位相同**(light 5/5、dark 3/3) |
| `src\TipCL.Core` 有没有被动过 | `git diff --name-status 652ed74 HEAD -- src/TipCL.Core` | **0 行输出**(空) |
| Core 对 Windows 源的同一性 | 逐文件 SHA256(mac `src\TipCL.Core\**\*.cs` ↔ Windows `src\TipCL\Core\**\*.cs`) | **248 个受版本控制的源文件:238 有对应 / 216 一致 / 22 有改动 / 10 无对应**;把 `obj\Release\net8.0\` 下 2 个自动生成文件也算进去则是 **250 / 238 / 216 / 22 / 12** —— 两种口径都与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` | 开工时 / 收工时各量一次 | 两次都是 **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16**,与 Lead 基线逐项相同(**零写入**) |
| 真实桌面 / 图片 | 收工时 | 桌面 14 项、最新写入 **2026-11-12 19:02:53**(不变);图片 3 项、最新 **2026-11-06 23:39:03**(零写入) |
| 残留进程 | `Get-Process testhost,TipCL,java,adb` | **0 个** |
| `%TEMP%` 下的 `tipcl*` | 收工时 `Get-ChildItem $env:TEMP -Force -Filter 'tipcl*'` | 总数 **104**(Lead 基线 89/92,差值是别的产品线留下的,见 B2a/C2c2/C3a 的同类记录);**本包窗口内(15:55–17:30)没有任何 `tipcl*` 条目被新建或改写**(按 LastWriteTime 过滤 70 分钟,命中 0 条) |
| 编码 | 对本包新增 / 改动的 15 个文本文件逐个 `ReadAllBytes` | 无一例外:**无 BOM(前 3 字节不是 `EF BB BF`)、CR=0、U+FFFD=0**;`LaunchPage.cs` 里 27 处、`Popups.cs` 6 处、两个测试文件 3 处 `??` **全部逐行核对过是 C# 空合并运算符**,不是乱码 |
| 提交 | 4 个提交 | `9955768`(6 个模型 + hero.jpg + csproj)/ `319ca0e`(页面 + 浮层 + 注册)/ `34c3814`(36 条结构用例)/ `068396f`(116 条状态·浮层·布局用例 + 真 bug 修复 + C2b 拆分),文档提交见下一小节;每次 `git add -A` 前都 `git status --short` 确认没有 `Zz*` 或仓库根散落文件 |

**为什么 (a)/(b)/(c) 三个提交并成了两个**:任务书要求 "页面骨架 → 动态逻辑 → 浮层" 分三次提交,
但页面骨架里的 `BuildHero` 就调了 `SyncHero` / `OpenConfigPopup` / `OpenSwitchPopup` / `OpenNewsPopup`,
把动态方法与浮层拆出去会让**没有任何一个中间提交能编译**(要能编译就得写桩,而写桩是红线)。
所以按"每个提交必须能编译"的原则并成 `319ca0e` 一个(内容仍是完整的页面 + 浮层 + 注册)。

### 8. 冒烟:新基准、以及"画面里到底有什么"(像素取证)

**新基准(本包合法变更,因为空壳第一次有了真实内容)**:

| 帧 | 字节 | SHA256 |
|---|---|---|
| light(含 `--smoke` 不带 `--page`) | **37681** | `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` |
| dark | **33579** | `7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` |

旧基准(light 21823 / dark 18200)作废:那一版是**没有任何页面的空壳**。

**像素取证**(`artifacts\c3c-pixel-probe*.py`,Pillow 采样;900×550):

| 区域 | light 主色 | dark 主色 | 说明 |
|---|---|---|---|
| 标题栏整条 | `(254,254,254)` + `(207,227,218)` + `(38,38,38)` | `(27,27,27)` + `(59,74,66)` + `(237,237,237)` | 外壳:白/深底 + 绿色 logo 片 + 反色标题字 |
| 侧栏 | `(250,250,250)` + `(38,38,38)` | `(26,26,26)` + `(237,237,237)` | 外壳:导航分组「启动/库/下载/社区/AI 助手/工具/更多」+ 选中项 |
| 英雄区大标题 | `(243,243,243)` + **2282 个 `(38,38,38)` 像素** | `(25,25,25)` + **2347 个 `(237,237,237)` 像素** | 「选择实例」的深/浅色大字 |
| 主按钮「启动游戏」 | **`(31,122,82)`** + 362 个白像素 | **`(38,122,86)`** + 362 个白像素 | 主题主色的 48 高按钮 + 白色图文 |
| 页面空白右半 | `(246,246,246)` 一片 | `(26,26,26)` 一片 | **纯页面底色,没有 hero 全景图**(见下) |
| 底边新闻卡 | `(244,244,244)` | `(26,26,26)` | 新闻卡本体(带「新闻」主色小标签) |
| 明暗两帧差异 | **494469 / 495000 像素不同(99.9%)** | | light/dark 确实不同 |

**为什么冒烟里没有 hero 全景图(诚实说明,不是 bug)**:`--smoke` 的离屏外壳 `BridgeReady` 是 false(D-96),
所以 `EnsureLoadedAsync` / `OnShown` 都不跑,而背景是在 `SyncBanner` / `OnShown` 里刷的 —— 于是离屏那一帧
是"页面刚建好、还没刷背景"的**载入前状态**。解码与应用这条链路本身**已经用测试走通**
(`Background_falls_back_to_the_embedded_hero_when_there_is_no_screenshot`:没有截图时背景落到内嵌 hero.jpg,
`OnImageNow == true` 且 `Image.Source != null`)。真桌面上的观感只能等真机验证。

### 9. 未验证项(本机是 Windows,没有 Mac)

- **macOS 真机**:渲染、`--smoke` 像素、Skia 解码 hero.jpg 的表现、系统红绿灯下的实际布局 —— 本机一律验不了。
- **浮层里那些 `DynamicResource` 底色**:离屏下浮层内容不挂进窗口的资源树,`Border.Background` 解析出来是 **null**,
  所以"浮层的纸色 / 描边色"本机**没有验**(只验了几何:圆角 12 / 描边 1 / 内边距 12 / 宽高 / MaxHeight 绑窗口百分比)。
- **指针事件薄壳那一层**:`PointerReleased` / `PointerEntered` / `PointerExited` 的订阅是照 Windows 语义写的,
  但用例驱动的是它们调用的同一批 internal 成员(不构造 Pointer 事件、不用 Headless 输入模拟,同 D-115),
  所以"点下去真的会走到那里"没有用真输入验过。
- **上下文菜单与三个浮层的真实弹出**:用例只验了"菜单项 / 浮层内容建出来了、状态正确",
  离屏下 `ContextMenu.Open(anchor)` 的**真实弹出与定位**没验(它需要真窗口管理器)。
- **`_log` 的自动滚动**:只在"模板里的 ScrollViewer 取得到"时才滚;离屏/未套模板时跳过,这条分支没有真输入验过。
- **`ufeff` 之外的字符集问题**:无。

### 10. 遗留与风险

1. **`MainWindow.FlyToTasks` 缺口**(D-122):mac 的外壳没有它,启动页用注入缝 + 不播动画兜住;
   后续 13 处调用点(InstancePage / VersionManagePage / InstallPage / JavaPage / CatalogPage /
   MultiplayerPage / PluginsPage / TasksPage 等)会撞上同一个洞,**请 Lead 早裁决**。
2. **`--smoke --page <其它 id>` 仍然什么都不切**:其它页面还没注册,`Navigate` 静默不动(与 A1 起的行为一致)。
3. **`_user`(Player)输入框没有落进配置面板**:这是 Windows 这一版的现状(字段保留),不是移植漏项;
   将来做设置页/账号页时若要用它,记得 Windows 也没用。
4. **旧卡片画布(`DashHost`)不落盘**:页面不订阅 `DashHost.Changed`,与 Windows 一致;若将来要让它持久化,
   得同时补页面侧的订阅与 `DashLayout` 的存取(本包只钉住了现状)。
5. **C2c3 的视觉件**(Backdrop / SideFrost / ImeGuard / 标题栏圆片 / 下载任务底条)仍未搬,与启动页无关。
6. **`hero.jpg` 是 275898 字节的嵌入资源**:GOAL 说 mac 无体积红线,所以直接嵌入;
   但它是"启动页默认背景"的唯一来源,真机上如果 Skia 解不出来会静默回退到纯底色(用例只在本机 Windows 验过解码成功)。
### Lead 验收(2026-11-13 17:55)——**第一个真实页面**

结论:**C3c 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

**先说一次事故**:本包的子代理在收尾前**失败退出、没有留下收尾消息**,漏了一次提交(一条新用例 + PROGRESS/DECISIONS 的书写)。但要紧的是:**代码全部已经在提交里**,留下的未提交改动经 Lead 复核是一条**完整的、通过的**新用例(`Background_falls_back_to_the_embedded_hero_when_there_is_no_screenshot`)加文档。Lead 已替它补交为 `c252335`,并在补交后**重跑两条通道:3086/3086 全绿**。所以这次事故没有丢任何工作,只是文档晚了一次提交。

| 检查 | 结果 |
|---|---|
| 提交(`9955768` a / `319ca0e` b+c / `34c3814` d1 / `068396f` d2 / **`c252335`** e,最后一条由 Lead 补) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`NavModel/NavConfig`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改 |
| **6 个模型 DTO + `hero.jpg` 对 Windows 源 SHA256** | 6 个模型 **全部一致**;`hero.jpg` **一致** |
| **既有测试有没有被改弱(Lead 逐项比对)** | `C2bShellTests.cs`:**断言 168 -> 173、用例 27 -> 28、Skip 0 -> 0**。旧的「宿主是空的」那条被改名成「宿主打开已注册的启动页」并**断言更多**(当前页 id、类型、与 `Host.Content` 同一实例);原来的三条断言**一字不改**地留在新增的 `Page_host_starts_empty_when_the_launch_page_is_unregistered` 里(靠 `RegistryScope` 把启动页临时摘掉还原 C2b 的初始状态)。**是加强,不是削弱** |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误 |
| **`dotnet test` 与进程内执行器各连跑 2 次**(补交前后各一次) | 四次全部**退出码 0**,通过 **3086 / 失败 0 / 不支持 0**(2968 -> 3086,+117 条本包用例 + 1 条拆分) |
| **`--smoke` 新基准(Lead 独立复跑)** | 不带 `--page` = light = **37681 字节 `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD`**;dark = **33579 字节 `7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893`**;`--theme blue` = **2**。旧基准(21823 / 18200)**作废** —— 那是"没有页面的空壳" |
| 全部 C3c 文件编码(含未提交时) | **无 BOM、CR=0、U+FFFD=0** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL`、桌面、图片、残留进程 | 9328 / 889530606 / 2026-11-10 20:42:16(**零写入**);桌面与图片最新写入时间**不变**;残留进程 **0** |
| 仓库有没有杂散文件 | `git ls-files` 里 `.py` / `artifacts/` / `dist/` / `Zz*` **0 个**;未跟踪文件 **0 个**(它把像素探针放在被忽略的 `artifacts\` 下,没有入库) |

**裁决 1:接受新 `--smoke` 基准(37681 / 33579)。** 空壳第一次有了真实内容,基准**该**变。子代理给了像素取证:标题栏(白/深底 + 绿色 logo 片 + 反色标题字)、侧栏(导航分组 + 选中项)、英雄区大字(light 2282 个深色像素 / dark 2347 个浅色像素)、主按钮 `(31,122,82)` 主色 + 362 个白像素、底边新闻卡;**明暗两帧 494469/495000 像素不同(99.9%)**。**新基准从此刻起生效。**

**裁决 2:接受「冒烟里没有 hero 全景图」的诚实说明。** 离屏时 `BridgeReady` 是 false(D-96),`OnShown`/`EnsureLoadedAsync` 不跑,而背景是在 `SyncBanner`/`OnShown` 里刷的 ⇒ 那一帧是**载入前状态**。解码与应用链路本身**已用测试走通**(`OnImageNow == true`、`Image.Source != null`)。这不是 bug,是如实记录的离屏限制,真机观感留给 D2 清单。

**裁决 3(重要):`FlyToTasks` —— 采纳子代理的建议,一次修好外壳,不在每个页面重复注入缝。**

事实:Windows 的启动页在「修复当前版本」后会调 `Win.FlyToTasks(_launchBtn, "修复中")` 播个飞向任务页的小动画;mac 的外壳(C2b 的 D-97)没搬 `FlyHost`/Dock,**没有这个成员**;而全仓库**还有 13 处**同类调用点(InstancePage / VersionManagePage / InstallPage / JavaPage / CatalogPage / MultiplayerPage / PluginsPage / TasksPage ...),**后面每个页面包都会撞上它**。

本包的选择(不动外壳 + 注入缝 + 挂点没接时不播 + 如实写清)在**当时**是对的 —— 因为红线是我给的。但就整个项目而言,**在 13 个页面包里各复制一次同样的注入缝是错的**:同一个洞修 13 遍,还多 13 个"没接就不播"的静默降级。

**因此就地派 C3d**:把 `FlyToTasks`(以及它需要的最小 `FlyHost` 可视件)作为**可选成员补进外壳**,并把 C3c 的注入缝接上、把这条降级从文档里划掉。C3d 之后,后续页面照 Windows 直接调外壳成员即可,不再需要各自的注入缝。

**运营备注**:本包用 **Python + Pillow**(`artifacts\c3c-pixel-probe*.py`)做像素取证 —— 能跑、产物在忽略目录里、没入库,所以接受;但 Lead 自己的取证一直用 `System.Drawing`,**后续若要写进交付物,统一用仓库里已有的工具链**,别引入新的运行时依赖。

### C3c 更正(C3d 追加,2026-11-13):那处「用户可见的降级」已经修好

> 说明:本节第 4 节与第 10 节第 1 条记的那处降级 —— **`MainWindow.FlyToTasks` 在 mac 侧不存在**、
> 启动页只能用 `LaunchPage.FlyToTasksSeam` 兜住 —— **已在 C3d 修好**。原文一字未删(不改写历史),
> 只在这里追加更正:
>
> 1. 外壳补上 `FlyHost` 画布 + `MainWindow.FlyToTasks(Control, string, string?)`(提交 `a9ad510`);
> 2. C3c 留的 `LaunchPage.FlyToTasksSeam` 由外壳构造函数接到真成员上、关窗解绑(提交 `b61db8a`);
> 3. 后续 13 处调用点(CatalogPage / InstallPage / InstancePage / JavaPage / LaunchPage /
>    MultiplayerPage / VersionManagePage)**直接写 `Win?.FlyToTasks(anchor, text)`** 即可,不必再留缝;
> 4. Lead 的裁决与全部细节见文末的 **C3d** 小节。

---

## C3d 外壳补 `FlyToTasks` / `FlyHost`:13 处页面调用点一次修好(2026-11-13)

工作包:把 Windows `MainWindow.FlyToTasks`(`MainWindow.xaml.cs:1336-1425`)与它那个 `FlyHost` 画布
(`MainWindow.xaml`:77)**补进 mac 外壳**,并把 C3c 给启动页留的注入缝接到真成员上。
依据是 C3c 的 Lead 验收**裁决 3**(「一次修好外壳,不在 13 个页面包里重复同一个注入缝」)。

**不碰**:色板 / `Controls.axaml` / 导航模型 / `Pages\PageHost.cs` / `Pages\PageRegistry.cs` /
其它页面与其它页面的移植(唯一动过的页面文件是 `LaunchPage` 里那处**注入缝本身**:接线 + 注释更正);
`src\TipCL.Core` 一字节未改。所以本包之后,后续 12 个页面包照 Windows 直接调 `<c>Win?.FlyToTasks</c>` 即可。

### 1. 外壳拿到了什么

1. **`FlyHost`**(`MainWindow._flyHost`):一个 `Canvas`(`Name="FlyHost"`,`IsHitTestVisible=false`),
   与 Windows 的 XAML 子元素顺序**同一位置** —— `ContentLayer → FlyHost → DropVeil → ToastHost → DialogHost`。
   z 序因此是:**盖在整窗内容之上(跨标题栏与正文两行),但在拖放浮层 / Toast / 对话框之下**。
2. **`public void FlyToTasks(Control source, string text = "", string? colorKey = null)`** —— 签名与 Windows
   逐参对应(`FrameworkElement` → `Control`),四道守卫、chip 的构造、几何与 620ms 运动全部照抄。
3. **`Services\FlyMotion.cs`**:620ms 时间线(四条曲线)单独成一个纯运动学类,外壳只管建 chip / 算坐标 / 摘 chip。
4. **接线**:外壳构造函数里 `LaunchPage.FlyToTasksSeam = (source, text) => FlyToTasks(source, text)`,
   `Closed` 里置空(静态接缝不许拎着死窗口)。

### 2. 与 Windows 逐条对齐的四道守卫(任一满足就什么都不做)

| # | Windows 源 | mac 写法 | 说明 |
|---|---|---|---|
| 1 | `!Motion.Enabled` | `!Motion.Enabled` | mac 的 `Motion.Enabled` 还要求"有人在推进动画时钟"(D-69)⇒ **离屏 / 测试里恒假**,冒烟帧因此一行都不画 |
| 2 | `!base.IsLoaded` | `!IsLoaded` | **Avalonia 11 的 `Control.IsLoaded` 与 WPF 同义**(本机探针实测:没 Show=假、Show 后=真、Close 后=假)。注意**不能**用 `IsAttachedToVisualTree()`:窗口是它自己那棵树的根,没 Show 过它也是真(见 D-129) |
| 3 | `target == null \|\| !source.IsVisible \|\| !target.IsVisible` | 同名成员 + 目标换成侧栏的 tasks 行 | Windows 的 `TabTasks` 是顶栏页签,mac 的外壳是侧栏(D-97),换的是"哪一行",判据不变 |
| 4 | `try { TransformToAncestor... } catch { return; }` | `TranslatePoint` 返回 null 就返回 | 源控件不在同一棵可视树里时 Avalonia 给 null,等价于 Windows 的 catch |

mac 侧只多一条**防御**:`source is null` 直接返回(Windows 会 NRE)。`target` 的取法:
`_navGrid` 里 `Tag` 的导航键映射到页面 id `tasks` 的那个 `ToggleButton`(行表被配置改没了就是 null,
与 Windows 的 `TabTasks == null` 同一分支)。

### 3. chip 与几何(逐值照抄)

| 项 | Windows 源 | 值 |
|---|---|---|
| 形状 | :1360-1361 | `CornerRadius 999`、`Padding (10,4,10,5)` |
| 文字 | :1362 | `Ui.Txt(text 为空 ? I18n.L("已加入队列") : text, 11.5, bold: true, "B.OnAccentStrong")` |
| 底色 | :1364 | `colorKey ?? "B.AccentStrong"`(走 `UiExt.Dyn` = WPF 的 `SetResourceReference`) |
| 阴影 | :1365 | `Motion.Shadow(chip, 16.0, 0.3, 3.0)`(Border 上落到 `BoxShadow`,与 C2a 同一套映射) |
| 量盒 | :1367 | `Measure(new Size(500, 60))` 之后读 `DesiredSize` |
| 起点 | :1370-1371 | `Canvas.SetLeft/Top(中心 − 尺寸/2)`,中心 = `source.TranslatePoint(Bounds/2, this)` |
| 变换 | :1372-1378 | `TransformGroup{ Scale(0.7,0.7), Translate(0,0) }`,原点 `(0.5,0.5)` 相对 |
| 终点 | :1352 | 任务行中心(同一个 `TranslatePoint`) |

### 4. 620ms 时间线(`FlyMotion`)与三处移植差异

Windows 的四条曲线:X `0→dx`(620ms,`CubicEase EaseInOut`);Y 两个关键帧(45% 处 `dy*0.28-46` 用
`EaseOut`、100% 处 `dy` 用 `EaseIn`);透明度 15%=1 / 72%=1 / 100%=0(线性);缩放 `0.7→0.42`(X/Y 各一条)。
`Completed` 挂在**缩放**那条上(Windows 的 `doubleAnimation` 就是 0.7→0.42),回调里摘 chip + 给任务行
`Motion.Pulse`。

| # | 差异 | 为什么 / 影响 |
|---|---|---|
| 1 | Avalonia 的 `Animation` 只有**一条整段缓动**,没有 WPF「每个关键帧自带缓动」 | X / 缩放走两关键帧 + 真缓动(单段 ⇒ 缓动就是曲线本身,**与 Windows 逐点相同**);Y 的两段不同缓动按 **2.5%(15.5ms)采样成线性关键帧**,0.025 能整除 0.45 与 1.0 ⇒ 45% 那帧正好落在采样点上。与 Windows 曲线的偏差**远小于 1px** |
| 2 | 驱动方式:WPF 合成时钟 → Avalonia `Animation.RunAsync`(4~5 条并发动画,目标都是 chip) | 本机探针实测:并发动画互不打架(终态都正确、变换对象没被换掉);**以 Transform 对象本身当目标是错的**(会抛 `InvalidCastException`,见第 8 节)。离屏没有时钟时走不到这里(守卫 1 已挡) |
| 3 | 时长**不乘** `Motion.Scale` | Windows 这里就是裸的 `TimeSpan.FromMilliseconds(620.0)`,mac 照抄(mac 的 `Motion` 其它成员才乘倍率) |

**还差 1px 的地方(照抄,不是本包引入)**:坐标算在**窗口**坐标系里(Windows 也是 `TransformToAncestor(this)`),
而 chip 被放进 `FlyHost` —— 但 `FlyHost` 在 `RootBorder`(1px 边框)之内,所以两个坐标系差 1px。
Windows 的 XAML 同样是 `RootBorder` 包住 `FlyHost`,这个 1px 在两边一模一样。

### 5. 后续 13 处调用点用哪一条(本包标准化的形状)

**12 处一字不改**:`PageBase.Win?.FlyToTasks(anchor, text)` —— `PageBase.Win` 早就是
`internal static MainWindow?`(C2b),`FlyToTasks` 就在那个类型上。
**1 处要一次 cast**:`InstallPage.cs:489` 的 `AppServices.Window?.FlyToTasks(_go, _ver)` 在 mac 侧写成
`(AppServices.Window as MainWindow)?.FlyToTasks(_go, _ver)` —— mac 的 `AppServices.Window` 静态类型是
Avalonia 的 `Window?`(D-88)。**没有**为此新增 `AppServices.Shell` 之类的新成员(那会偏离 Windows 原文),
null 语义与原来相同。见 D-131。

C3c 的 `FlyToTasksSeam` **签名保留**(下游页面/测试仍可注入探针),但它的默认行为已经是"接到真成员",
**新页面不要再加缝**(那正是本包要消掉的重复)。

### 6. 测试(33 个方法 / 43 条用例;总数 3086 → 3129)

| 文件 | 方法 / 用例 | 覆盖 |
|---|---|---|
| `C3dFlyTests.cs` | 24 / 34 | 四道守卫(关动画 / 用户关掉动画 / 源不可见 / 任务行不存在 / 任务行不可见 / 没 Show 过 / 已 Close / 源不在可视树 / null 源)、chip 的文字(3 组 Theory 含默认文案)、底色(默认 `B.AccentStrong` + 自定义键)、字号 11.5 半粗 + `B.OnAccentStrong`、999 药丸 + padding、阴影(含 alpha 76 = `round(0.3*255)`)、起点几何(现算一遍)、变换组顺序与原点、常量逐项(含"45% 落在采样点上")、**曲线与 WPF 三次幂闭式逐点对拍(8 组 Theory)**、Y 曲线 46px 领先量、透明度三段、**飞完宿主必须重新为空(泄漏检查)**、连飞两条都落地、终点 + 终态缩放 0.42 + 透明度 0、任务行被 Pulse 的证据 |
| `C3dReachTests.cs` | 9 / 9 | `PageBase.Win` 指向外壳、页面形状真的到得了外壳成员、没有窗口时 null 安全、窗口不是外壳时 `Win` 为 null、`AppServices.Window` 形状 cast 一次后同样到得了、**外壳把接缝接到真成员**、**关窗解绑接缝**、**修复路径调用接缝(文案「修复中」+ anchor 是启动按钮)**、**端到端:启动页长在真外壳里时修复真的在外壳 `FlyHost` 上飞出 chip 且飞完摘掉** |
| `C3dFixtures.cs` | — | 期望值权威副本(`C3dExpected`)、`C3dClock`(打开"动画开着"那一支的作用域,见 D-129)、`FlyProbePage`(只按页面统一形状调外壳成员)、`C3dFiles`(唯一 chip / 现算几何 / 拆变换组 / 色键对拍 / 启动页假后端与加载) |

驱动方式沿用既有纪律:按钮与菜单项走 `RaiseEvent`,对话框走 `DlgHarness`/`C2c2Files.Dialog()`(D-113),
等待一律"一边泵 Dispatcher 一边看条件"(Stopwatch + 9 秒上限,D-120),**不模拟真实输入设备**(D-115)。
"动画开着"那一支靠新增的 `Motion.ClockOverride` 打开(**本机探针实测:headless 的动画时钟靠
`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` 真的推得动,620ms 的 `Animation.RunAsync` 会跑完并落终态;
但 `DispatcherTimer` 在那条泵里一次都不 Tick** —— 后者否掉了"用 DispatcherTimer 逐帧驱动"的方案),见 D-129。

### 7. 验证命令与实际结果(本机 Windows,全部由本包亲自执行)

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 个错误**;警告 = `NU1900` ×6 + `SYSLIB0014` ×1(与基线逐项相同) |
| `dotnet test` 第 1 / 2 次 | `dotnet test TipCL.Mac.sln -c Release --no-build` | 都是**退出码 0,失败 0 / 通过 3129 / 总计 3129**(57 秒 / 58 秒) |
| 进程内执行器 第 1 / 2 次 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` | 都是**退出码 0,总计 3129 / 通过 3129 / 失败 0 / 跳过 0 / 不支持 0** |
| `--smoke`(不带 `--page`) | `TipCL.exe --smoke --out artifacts\c3d-nopage.png` | 退出码 **0**,**37681 字节**,SHA256 `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` = **C3c 基准逐位相同** |
| `--smoke --page launch --theme light` | 同上加 `--page launch` | 退出码 **0**,**37681 字节**,同一个哈希 |
| `--smoke --theme dark` | 同上 `--theme dark` | 退出码 **0**,**33579 字节**,`7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` = **C3c 基准逐位相同** |
| `--smoke --theme blue` | 同上 `--theme blue` | 退出码 **2**(参数错,符合预期),打用法、不产出文件 |
| 冒烟里到底有没有 chip | 见下 | **没有**:两帧与 C3c 基准**逐字节相同**,而 C3c 基准是在 `FlyToTasks` 还不存在时出的图;机制上离屏 `Motion.Enabled == false`(无动画时钟),第一道守卫就返回;外加外壳只多挂了一个**空** `Canvas`(不画、不影响布局) |
| `src\TipCL.Core` 有没有被动过 | `git diff --name-status 30196f3 HEAD -- src/TipCL.Core` | **0 行输出**(空) |
| Core 对 Windows 源的同一性 | 逐文件 SHA256(mac `src\TipCL.Core\**\*.cs` ↔ Windows `src\TipCL\Core\**\*.cs`,受版本控制的 248 个) | **有对应 238 / 完全一致 216 / 有改动 22 / 无对应 10**,与基线**完全一致** |
| `%LOCALAPPDATA%\TipCL` | 收尾时量一次(见下"诚实说明") | **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16** —— 与 Lead 基线逐项相同(**零写入**) |
| 真实桌面 / 图片 | 收尾时 | 桌面 14 项、最新写入 **2026-11-12 19:02:53**(不变);图片 3 项、最新 **2026-11-06 23:39:03**(零写入) |
| 残留进程 | `Get-Process testhost,TipCL,java,adb` | **0 个** |
| `%TEMP%` 下的 `tipcl*` | 收尾时 | 总数 **113**(C3c 记的 104 是别的产品线留下的);**本包验证窗口(18:31:08–18:34:19)内新建 0 个**;窗口之前 18:26–18:30 有 9 个(`tipcl-tests-*` / `tipcl-smoke-*` / `tipcl-final-selftest-*` 之类),这些名字在本仓库源码里**一个都不存在**,而且本仓库的测试隔离目录是 `tests\TipCL.Tests\bin\Release\net8.0\test-home\<guid>`(收尾时 424 个,最新 18:34:07)⇒ 判给别的会话/产品线,但**本包无法自证归属**,如实记录 |
| 编码 | 对本包新增 / 改动的 8 个文本文件逐个 `ReadAllBytes` | 无一例外:**无 BOM、CR=0、U+FFFD=0** |
| 提交 | 5 个提交 | `a9ad510`(FlyHost + FlyMotion + FlyToTasks)/ `b61db8a`(接缝接线 + 页面注释更正)/ `1c20fbe`(C3c 小节追加更正注)/ `90079bb`(43 条用例)/ 文档提交见下;每次 `git add -A` 前都 `git status --short` 确认没有 `Zz*` 或仓库根散落文件 |

### 8. 顺带实测到的两条框架事实(一条影响全项目)

1. **`Avalonia.Controls.Control.IsLoaded` 在 Avalonia 11 里存在**,语义与 WPF 的 `IsLoaded` 一致
   (本机探针:没 Show=假 / Show 后=真 / Close 后=假)。因此 `FlyToTasks` 的 `!IsLoaded` 是**逐字照抄**。
   ⇒ `Motion.Loop` 里那句注释("与 Windows 的 `owner.IsLoaded` 等价:Avalonia 用「在不在可视化树上」判断")
   **对窗口不成立**(窗口是它自己那棵树的根,`IsAttachedToVisualTree()` 在没 Show 过时也是真)。
   本包**没有改** `Motion.Loop`(不在红线内但属于别的包的语义,改了要重新验一批既有用例),只记录。
2. **以 Transform 对象本身当 `Animation.RunAsync` 的目标会抛 `InvalidCastException`**
   (`TransformAnimator` 要求目标是 `Visual`)。也就是说 `Motion` 里那些 `Play(translate, ...)` / `Play(scale, ...)`
   形式的调用(FadeIn 的竖直位移、HoverLift、PopIn/PopOut、Shake、Spin…)在**真桌面**(有动画时钟、
   `Motion.Enabled` 为真)会**静默失效**(异常被 `Motion.Start` 吞掉);离屏/测试里因为 `Enabled` 恒假根本走不到,
   所以一直没被发现。**这是既有问题,不是本包引入**;本包的时间线因此把**目标指向 chip**(
   setter 写 `TranslateTransform.XProperty` / `ScaleTransform.ScaleXProperty`),这也是 Avalonia 的正确写法。
   **请 Lead 决定**是否单开一个小包修 `Motion` 里那些调用点(修法是把目标换成宿主控件,不动公开签名)。

### 9. 未验证项(本机是 Windows,没有 Mac)

1. **macOS 真机**:飞行 chip 的实际观感、系统红绿灯下的坐标、真桌面合成时钟推进 620ms 的表现、
   `Motion.Enabled` 在真桌面为真那条分支 —— 全部**未验证**。
2. **动画中途逐帧的形状没有采样**:离屏没有"逐帧读某一时刻"的钩子,所以验的是
   ①曲线数学与 WPF 闭式逐点一致、②终态(位移 / 缩放 0.42 / 透明度 0)、③结束时摘 chip。
3. **`Motion.Pulse` 之后任务行的缩放是否回到 1.0** 没断言(只断言"缩放变换出现了"这一条证据)。
4. **剩余 12 处调用点还没移植**:所以"它们编得过"是形状推导(12 处逐字 + 1 处一次 cast),不是编译验证。
5. `%TEMP%` 那 9 个 `tipcl*` 目录的归属(见第 7 节末)—— 本包只能证明**不在自己的窗口内**。

### 10. 遗留与风险

1. **`Motion.ClockOverride` 是新增的 internal 测试注入点**(D-129):默认 null(行为与以前完全一致),
   设了它的包**必须**在作用域里还原,否则同进程里别的用例会看到"动画开着"。
2. **第 8 节的两条**:`Motion.Loop` 的注释不准确(只记录)、`Motion` 里以 Transform 为目标的动画在真桌面静默失效(**请 Lead 裁决是否单开小包**)。
3. **C3c 的注入缝保留着**(签名不变),但它已由外壳接上真成员;**新页面不要再加缝** —— 这层间接可以后续某个包统一拆掉。
4. 本包**只补了外壳的两个成员**;`FlyHost` 里除了飞行 chip 之外没有任何其它用途
   (Windows 也只被 `FlyToTasks` 用)。

### Lead 验收(2026-11-13 18:45)

结论:**C3d 通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| 六个提交(`a9ad510` / `b61db8a` / `1c20fbe` / `90079bb` / `f7293a8` / `6c6534b`) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`NavModel/NavConfig`、`Services\Ui.cs`、`Services\UiExt.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改(本包按裁决**允许**改 `MainWindow.cs` / `Motion.cs` / 新增 `FlyMotion.cs`) |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 3129 / 总计 3129**(3086 -> 3129,+43) |
| 进程内执行器 第 1 / 2 次 | 都是**退出码 0,通过 3129 / 失败 0 / 不支持 0** |
| **`--smoke` 与 C3c 基准逐位比对** | 不带 `--page` = **37681 `2603BF6A...`**;`--page launch --theme light` = **同哈希**;`--theme dark` = **33579 `7D7AFDD5...`**;`--theme blue` = **2** —— **逐字节相同**。离屏 `Motion.Enabled` 为假 ⇒ 第一道守卫直接返回,帧里没有 chip(新增的只是一个空 Canvas) |
| `FlyToTasks`(Lead 抽查源码) | 成员存在;`FlyHost` 是 Canvas、Row0/RowSpan2;z 序写明为 `ContentLayer -> FlyHost -> DropVeil -> ToastHost -> DialogHost`(与 Windows XAML 子元素顺序一致);`TabTasks` 的 mac 等价物与 `TabTasks == null` 同分支 |
| 全部改动文件编码 | **无 BOM、CR=0、U+FFFD=0** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 / 216 / 22 / 10,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 桌面 / 图片 / 残留进程 | 9328 / 889530606 / 2026-11-10 20:42:16(**零写入**);桌面与图片最新写入时间不变;残留进程 **0** |

**裁决 1:采纳 D-130,单开小包修 —— 但先记下我认可的严重性。**

子代理在写本包时**实测**发现:`Motion` 里凡是把 **Transform 对象本身**当作 `Animation.RunAsync` 目标的那批动画,在 Avalonia 上会抛 `InvalidCastException`(TransformAnimator 要求目标是 `Visual`),而 `Motion.Start` 把异常**吞掉**了。后果是:**真桌面(动画开着)上这些动画静默不动** —— 包括 `FadeIn` 的竖直位移、`HoverLift`、`PopIn`/`PopOut`、`Shake`、`Spin`;离屏因 `Motion.Enabled` 恒假走不到,所以一直没人发现。

这是 **C2a 移植 `Motion` 时留下的既存缺陷**,不是 C3d 引入的;但它意味着**后面每个用到这些动画的页面在真机上都会"少动一下"而不报错** —— 正是那种"测试全绿、真机坏了"的坏账。所以:**采纳,单开 C3e 修**(把目标换成宿主控件,公开签名不动),并要求它一并处理"吞异常"这一点(至少不许再把这类缺陷藏起来)。

**裁决 2:接受 `InstallPage.cs:489` 的写法差异(D-131)。** 12 处调用点可以一字不改地用 `PageBase.Win?.FlyToTasks(anchor, text)`;唯独那一处因为 mac 的 `AppServices.Window` 是 `Window?` 而写成 `(AppServices.Window as MainWindow)?.FlyToTasks(...)`。可以接受,后续页面按 D-131 的两条形状照抄即可。

**裁决 3:接受 `Motion.ClockOverride`(D-129)。** headless 里 `Motion.Enabled` 恒假,所以"动画开着"这一支结构上不可达;加一个 internal 注入点让那一支真的可验 —— 这是正解(**但使用者必须还原**,已写进遗留)。

**运营备注**:子代理如实说明它量 `%LOCALAPPDATA%\TipCL` 基线是在**收尾阶段**而不是开工第一条命令 —— 这种自我披露是对的(让我知道那条证据的强度)。以后仍要求开工先量。

---

## C3e `Motion` 的变换动画:`Transform` 当目标是错的,改成"目标给宿主控件"(2026-11-13)

工作包:把 C3d 实测发现、Lead 在 D-130/裁决 1 里认可的那一类缺陷修掉 ——
**凡是把 Transform 对象本身当 `Animation.RunAsync` 目标的动画,在真桌面上静默不动**
(异常被 `Motion.Start` 吞掉;离屏 `Motion.Enabled` 恒假,根本走不到那一步,所以测试全绿)。
同时按裁决要求处理"吞异常"这一点:失败必须**可观测**。**不碰**任何页面、外壳、色板、`Controls.axaml`、
导航、`src\TipCL.Core`(一字节未改)。

### 1. 受影响的方法(逐个 `RunAsync` 调用点查出来的,不止任务书点名的六个)

**19 处"以变换为目标的调用点",分布在 11 个方法**;另有 2 个方法是通过 FadeIn 间接受影响:

| # | 方法 | 以变换为目标的调用点 | 变换 / 属性 |
|---|---|---|---|
| 1 | `FadeIn` | 1 | `TranslateTransform.Y`(竖直滑入) |
| 2 | `HoverLift` | 6 | 进:Scale X/Y + Translate.Y;出:同样三条 |
| 3 | `Pulse` | 2 | `ScaleTransform.ScaleX/ScaleY`(三关键帧 1→peak→1) |
| 4 | `Shake` | 1 | `TranslateTransform.X`(六关键帧 −7/6/−4/3/−1.5/0) |
| 5 | `Shine` | 1 | `TranslateTransform.X`(横扫) |
| 6 | `Spin` | 1 | `RotateTransform.Angle`(无限循环) |
| 7 | `PopIn` | 2 | `ScaleTransform.ScaleX/ScaleY`(0.96→1) |
| 8 | `PopOut` | 2 | `ScaleTransform.ScaleX/ScaleY`(→0.97) |
| 9 | `SlideIn` | 1 | `TranslateTransform.X`(40→0) |
| 10 | `SlideOut` | 1 | `TranslateTransform.X`(0→40) |
| 11 | `PageSwap` | 1 | 旧页 `TranslateTransform.Y`(∓8) |
| 12 | `Stagger` | 0(间接) | 每个子项走 `FadeIn` ⇒ 同样静默不动 |
| 13 | `StaggerItems` | 0(间接) | 同上 |

**没受影响**(目标本来就是控件,`Visual`/`RangeBase.Value`/`Layoutable` 属性):`FadeOut`、`Fade`、`Progress`、
`AnimateWidth`、`AnimateHeight`、`CountUp`(走 `DispatcherTimer`)、`Shine` 的**不透明度**那条、
`Pulse`/`Shake` 之外的 `Shadow`、`PageSwap` 新页那条(FadeIn 已被上面第 1 项覆盖)。
`Loop` 本身只做"起停",不含动画调用点。

**顺带发现并修掉的第二、三个坑(同一族:真桌面上"少动一下")**:

| 坑 | 实测证据 | 修法 |
|---|---|---|
| `Animation.Easing` **显式写 null** ⇒ 动画一个字节都不写、任务永不结束(也不抛) | 探针:A/D 两条 2 秒仍 PENDING、值不变;`new Animation().Easing` 的默认值是 **`LinearEasing`** | `TwoFrame`/`KeyFrames` 只在 `easing != null` 时赋值(= WPF 不给 `EasingFunction` 的线性语义)。`Shine` 的**不透明度关键帧**一直踩在这里(旧代码 `PlayFrames(..., null)`) |
| `IterationCount.Infinite` + `RunAsync` 必抛 `InvalidOperationException: Looping animations must not use the Run method.` | 探针(有限/无限各一条,无限必失败) | `Spin` 改成"**一圈接一圈**"(有限动画跑完在收尾回调里再起下一条);`Animation.Apply` 在 11.3.2 **不是公开 API**,编译不过,所以只能这么办 |

### 2. 机制(为什么是"目标给控件")

本机读 Avalonia 11.3.2 的 IL(`TransformAnimator.Apply`)得到的事实,决定了修法:

1. 它第一句就是 `(Visual)control` ⇒ **目标是 Transform 时同步抛 `InvalidCastException`**(探针:Translate / Scale / Rotate 三种都抛);
2. `property.OwnerType` 是 `Transform` 子类时,它去 `Visual.RenderTransform` 里找 —— `RenderTransform` **恰好是那一类型**,
   或(`TransformGroup` 时)里面**运行时类型完全相等**的第一个子项;找不到就**只发一条 Warning 日志、什么都不做**;
3. 所以正确写法是:**目标给宿主控件、属性仍旧写变换自己的属性**(与 D-130 记的 chip 写法一致,也是任务书给的第一条建议)。

具体改动(全部在 `Motion.cs` 私有实现里,公开签名一个字节没动):

- 新增 `PlayTransform(owner, transform, property, from, to, ms, easing, done)` /
  `PlayTransformFrames(...)`:目标给 `owner`,并先做一次 `CheckTransformTarget` 自检;
- `Start` 的目标参数类型由 `Animatable` 收成 **`Visual`** —— "拿 Transform 当目标"从此**编译不过**
  (比一条断言更硬的回归钉子);仍保留"任何失败都不许把界面带崩";
- 动画构造抽成 `TwoFrame` / `KeyFrames`(缓动为 null 时不碰 `Animation.Easing`);
- `Spin` 改成一圈接一圈 + 取消式停止(`Loop` 的 stop 分支:取消 → 归零,等价 Windows 的 `BeginAnimation(AngleProperty, null)`);
- `Start` 起完动画顺手 `target.InvalidateVisual()`(理由见下一条)。

**headless 的时钟事实(决定了测试怎么验、也顺带更正 D-129)**:离屏的动画时钟**只在真的产出帧时前进** ——
同一条变换动画,每轮先 `InvalidateVisual()` 再泵,**251ms 跑完**;一次都不标脏就永远停在起点、任务永不结束。
所以测试的泵每轮先标脏(`C3eStage.Frame`,先 `InvalidateVisual` 再 `UiHost.Flush`),生产侧也多推一次重绘。
更正 D-129 的一句:那次"`DispatcherTimer` 一次都不 Tick"是**泵里不 sleep**、真实时间没走够导致的;
本包的泵每轮 sleep 3~5ms,实测 80ms 的定时器 60 轮 **tick 了 9 次** ⇒ FadeIn 的延时分支、Stagger 的错帧、
CountUp 的逐帧文本**在离屏都能验**(见第 3 节的强制时钟证据)。

### 3. 可观测性(任务书第 3 条)

旧 `Start` 只有 `catch (Exception)` 一吞到底 —— 那正是这个缺陷藏了这么久的原因。现在:

| 成员 | 用途 |
|---|---|
| `internal Exception? LastError` | 最近一次失败(**同步异常与"任务随后失败"两条路都记**,旧代码只看得到前者) |
| `Debug.WriteLine("[Motion] 动画失败…")` | 留在调试输出里 |
| `internal int StartedAnimations` | 真的交给 Avalonia 起过几条(0 = 一条都没到过) |
| `internal int RunningAnimations` / `RunningAnimationsSinceReset` | 现在挂着几条 / **本用例窗口里**起的还挂着几条 |
| `internal string? LastTargetType` / `LastPropertyName` | 最近一条动画的目标类型与属性(回归钉子用它) |
| `internal void ResetObservability()` | 测试用:清错误与计数,并把"按编号记账"的活动集合清空 |
| `internal void RunForTest(Animation, Visual, done)` | 测试用:把一条动画直接交给 runner(证明"失败会被记下") |
| `CheckTransformTarget` 自检 | 变换没挂到宿主上 / 拿错实例 ⇒ 记进 `LastError`(不再"悄悄不动") |

`RunningAnimationsSinceReset` 为什么按**编号**记账:`RunningAnimations` 是进程级的,而 `C3dReachTests` 关窗后
留了 4 条飞行 chip 动画(它们没消失,只是没人推帧;本用例一泵就补齐并减数 —— 差值一度变成 **−2**),
两条通道用例顺序不同,直接断言总数会 VSTest 绿、进程内执行器红(本包实测踩到)。

### 4. 公开签名(逐条确认未变)

`Motion` 的公开面一个字节没动:`Scale`、`Enabled`、`UserEnabled`、`EnabledChanged`、`Loop`、`FadeIn`、
`FadeOut`、`Stagger`、`StaggerItems`、`HoverLift`、`Shadow<T>`、`Pulse`、`Shake`、`Shine`、`Spin`、`Progress`、
`CountUp`、`AnimateWidth`、`AnimateHeight`、`Fade`、`PopIn`、`PopOut`、`SlideIn`、`SlideOut`、`PageSwap` ——
名字、参数名、顺序、默认值全同(改动只涉及 `Play`/`PlayFrames`/`Start` 这些私有成员 + 新增 internal 测试缝)。
`git diff` 里 19 处调用点全是"把第一个实参从变换换成宿主控件、其余逐字不动"。

### 5. 用户可见差异(需要 Lead 知道的两条)

1. **`Spin` 每圈交界处有一次一帧级重启**:Avalonia 没有公开的"循环动画"入口,只能一圈接一圈接。
   0° 与 360° 在同一处,观感连续,但每圈边界有一次"收尾回调往返"的间隙(实测约 0~1 帧)。
   **这是本包唯一的用户可见差异**;其余动画的时长 / 缓动 / 终值与 Windows 逐值相同。
2. **坏动画不再静默**:以前"动画没跑"与"动画跑完"在外部看起来一样,现在失败会留在 `LastError`
   (界面行为不变,失败仍然不崩)。这条是给未来的人看的,不是观感差异。

### 6. 测试(50 条用例:36 个 `[Fact]` + 1 个 14 行 `Theory`;总数 3129 → 3179)

> 计数更正:测试提交 `d0c5bdd` 的说明里写了"79 条用例",那是我用
> `--filter FullyQualifiedName~C3e` 跑出来的数 —— 这个过滤器**顺带匹配到了 `B2c3ExportLaunchScriptTests` 的 29 条**。
> 本包真正新增的是 **50 条**(`--list-tests` 复核:50 × `C3eMotionTests`),总数 3129 → 3179 与之一致。

| 组 | 条数 | 覆盖 |
|---|---|---|
| A 逐动画终态 | 20 | FadeIn(含"只补一个 Translate 不换组"、"延时分支")/ HoverLift(进 / 出 / 阴影)/ Pulse(峰值+终值)/ Shake(左右都抖到过+终值 0)/ Shine(横扫终点+透明度回 0)/ Spin(跨圈重启+停掉归零)/ PopIn / PopOut / SlideIn / SlideOut / PageSwap(前进 / 后退)/ Stagger / StaggerItems / Progress / AnimateWidth·Height / CountUp |
| B 关掉动画 | 7 | 什么都不建、什么都不起(`StartedAnimations=0`)、终态与 Windows 关掉动画时逐值一致;含"`ClockOverride=true` 但用户开关关着"这一支 |
| C 可观测性 | 6 | 故意失败的动画进 `LastError` 且不抛;`ResetObservability` 清得掉;成功动画不留错误并记 1 条;`Spin` 拿到没挂上去的变换会记原因;失败之后后续动画照跑;变换动画记到的目标是控件、属性是 `TranslateTransform.Y` |
| D 回归钉子 | 14 | 逐方法(14 行 Theory)验:目标类型里**不出现** "Transform"、`LastError` 为 null、本用例起的动画全收摊 |
| E 泄露 | 2 | 一批 FadeIn 之后无残留;关窗后 `Spin` 不再是"无限动画还挂着" |

**每个动画的"真的跑起来了"证据(强制时钟 + 逐帧推重绘,全部实测)**:

| 动画 | 断言到的终值 / 证据 |
|---|---|
| FadeIn | `Opacity 0→1`;`Translate.Y 10→0`;`LastTargetType` 含 `Border`、`LastPropertyName = TranslateTransform.Y` |
| FadeIn(延时 300ms) | 延时期间 `StartedAnimations=0`、`RenderTransform=null`;之后 `Opacity=1`、`Y=0` |
| HoverLift 进 | `ScaleX/ScaleY → 1.012`、`Y → −2`、`BoxShadow` 出现(blur 22 / offsetY 3 / alpha 41) |
| HoverLift 出 | `ScaleX/ScaleY → 1.0`、`Y → 0` |
| Pulse | 属性写入记账:**最大写值 1.06 以内且 > 1.0**,终值 `ScaleX=ScaleY=1.0` |
| Shake | 记账:**最小写值 < −1、最大写值 > 1**(左右都抖到过),终值 `X=0` |
| Shine | `Translate.X → 父宽+40 = 460`(层宽 60 ⇒ 从 −100 起),`Opacity → 0` |
| Spin | 角度真的在写(记账非空)、跨了 ≥ 2 圈(`StartedAnimations ≥ 3`)、停掉后 `Angle=0` 且无残留 |
| PopIn | `ScaleX=ScaleY=1.0`、`Opacity=1` |
| PopOut | `ScaleX=ScaleY=0.97`、`Opacity=0`、`done` 被调 |
| SlideIn | `X → 0`、`Opacity=1` |
| SlideOut | `X → 40`、`Opacity=0`、`done` 被调 |
| PageSwap 前进 | 旧页 `Y` 写值到过 ≤ −1(记账)、被 `Reset` 回 `Opacity=1 / Y=0`、内容换成新页、新页 `Opacity=1 / Y=0` |
| PageSwap 后退 | 同上但方向为 +8 |
| Stagger / StaggerItems | **每个子项**(含错帧延时的)都 `Opacity=1`、`Y=0` |

**中间态断言改用 `PropertyChanged` 记账**:一帧可能跨过整段动画(机器忙的时候),采样式断言会偶发漏掉中间态
(Pulse 峰值 / Shake 左右 / PageSwap 滑出 / Spin 转过),现在都监听属性写入(`C3eWatcher`),顺带断言"真的写过值"。

### 7. 验证命令与实际结果(本机 Windows,全部由本包亲自执行)

_(本节数字在最终代码上重跑后填写,见文末"最终验证"小节。)_

### 8. 未验证项

1. **macOS 真机**:动画的实际观感 / 每圈交界那一帧级重启能不能看出来 / 真桌面 vsync 驱动下这些动画的节奏 ——
   **全部未验证**(本机是 Windows)。可验的只有"动画真的跑起来并落终态",这一点已用 `ClockOverride` + 逐帧推重绘验到。
2. **`Motion.Scale`(时长倍率)≠ 1 时**没有专门验(所有用例都在 `Scale = 1.0` 下跑的);倍数只影响时长,不影响机制。
3. **`Shine` 的 7 秒串场间隔**:只验了"启动时那一次横扫"(`Loop` 的 start 里 post 的那一次);
   7 秒的 `DispatcherTimer` 重复触发没有等足时间验(等 7 秒会吃掉会话预算 —— D-120)。
4. **`HoverLift` 出场的阴影清除**(190ms 的 `DispatcherTimer` 到点把 `BoxShadow` 清回 `default`)只验了"进场挂上阴影",
   没有验"出场之后阴影被清掉"。
5. **两类失败只验了其中一类**:「无动画器的属性」的失败(同步抛)已验;「任务本身 fault」那条分支靠代码路径保证,
   没有真的造出来过(能造出来的形态都要依赖 Avalonia 内部行为)。

### 9. 遗留与风险

1. **`C3dReachTests` 关窗时把 4 条飞行 chip 动画留在"挂着"状态**(它们只是没人推帧,生产里窗口关掉也就一起没了)。
   它不影响本包(改用按编号记账的 seam),但**进程级 `RunningAnimations` 会一直被它们抬着** ——
   以后谁想用那个总数做断言,先看 D-132 最后一条。
2. `Motion.Loop` 里那句"IsLoaded 等价于在可视化树上"的注释**仍然不准确**(C3d 记过),本包**没有改**
   (不在本包红线内,改了要重验一批既有用例);要改请单开一个小包。
3. `CheckTransformTarget` 只覆盖"变换没挂到宿主上"这种**会静默不动**的调用;`PlayTransform` 的调用方
   (页面)如果自己换掉了 RenderTransform,依然只能靠 `LastError` 事后发现。
4. `Motion.PlayFrames` 关掉动画那一支(写最后一个关键帧)现在只剩 `Shine` 的透明度会用;
   变换那条改成了"什么都不做"(Pulse / Shake 自己在 `!Enabled` 分支里落了终态)—— 行为与旧代码逐值相同。
### Lead 验收(2026-11-13 22:20)

结论:**C3e 通过**。这一包又是「子代理被中断、代码已提交、只剩文档」,Lead 已补交文档为 `3c9f33f`;而且**它修掉的是本项目最隐蔽的一类坏账**。

**先说事故**:本包子代理与其它子代理一起被意外中断,没有留下收尾回报。工作树上只剩 `DECISIONS.md` / `PROGRESS.md` 两份文档未提交(代码分 6 次提交全部就位)。Lead 补交并逐项复核,**没有丢任何工作**。

| 检查 | 结果 |
|---|---|
| 提交(`5094e6a` runner 收口 / `d0c5bdd` 79 条用例 / `7838def` 泄露检查按编号记账 / `f4ab0bc` 中间态改 PropertyChanged / `e9eb048` 终态泵法 / **`3c9f33f`** 文档,最后一条由 Lead 补) | 工作树干净;无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\`、`MainWindow.cs`、`NavModel`、`Services\Ui.cs`、`Services\UiExt.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | 未改(本包按裁决**允许**改 `Services\Motion.cs`) |
| `dotnet build ... --no-incremental` | 退出码 0,0 错误 |
| `dotnet test` 第 1 / 2 次 | 都是**退出码 0,失败 0 / 通过 3179 / 总计 3179**(3129 -> 3179,+50) |
| 进程内执行器 | 我先跑 2 次(全绿),**再连跑 6 次做抖动探针:6/6 全绿 3179/3179,零失败**。本树累计 **10 次绿色运行** |
| **`--smoke` 与基准逐位比对** | 不带 `--page` = **37681 `2603BF6A...`**;`--page launch --theme light` = **同哈希**;`--theme dark` = **33579 `7D7AFDD5...`**;`--theme blue` = **2** —— **逐字节相同**(离屏动画恒关,这条分支进不来) |
| 全部改动文件编码 | **无 BOM、CR=0、U+FFFD=0** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 / 216 / 22 / 10,与基线完全相同 |
| `%LOCALAPPDATA%\TipCL` / 残留进程 | 9328 / 889530606 / 2026-11-10 20:42:16(**零写入**);残留进程 **0** |

**这一包到底修了什么(值得记清楚,因为它是"测试全绿、真机坏"的典型)**

Lead 在 C3d 验收里采纳的 D-130 只是一条线索。子代理**逐个 `RunAsync` 调用点**查下去,查出 **19 处以变换为目标的调用点、分布在 11 个方法**(`FadeIn` / `HoverLift` / `Pulse` / `Shake` / `Shine` / `Spin` / `PopIn` / `PopOut` / `SlideIn` / `SlideOut` / `PageSwap`),另有 `Stagger` / `StaggerItems` 通过 `FadeIn` 间接中招 —— 比我点名的六个多得多。

**并且顺带查出同族的第二、三个坑**(都以"真机上少动一下"的形式存在,离屏一律看不见):

| 坑 | 证据 | 修法 |
|---|---|---|
| `Animation.Easing` 被**显式写 null** ⇒ 动画一个字节都不写、任务永不结束,**也不抛异常** | 探针:A/D 两条 2 秒后仍 PENDING、值不变;而 `new Animation().Easing` 的默认值其实是 `LinearEasing` | 只在 `easing != null` 时赋值(等价 WPF 不给 `Easing` 时的行为) |
| `IterationCount.Infinite` + `RunAsync` 必抛 `InvalidOperationException: Looping animations must not use the Run method.` | 探针:有限/无限各一条,无限那条必失败 | `Spin` 改成"一圈接一圈"(有限动画跑完在收尾回调里再起下一条) |

**裁决 1:接受唯一的用户可见差异 —— `Spin` 每圈交界处有一次 0~1 帧的重启。** Avalonia 没有公开的"循环动画 + RunAsync"入口,只能一圈接一圈;0° 与 360° 重合,观感连续。其余动画的时长 / 缓动 / 终值与 Windows 逐值相同。**这条列入 D2 的真机清单**(让有 Mac 的人盯一眼转圈有没有可见的顿挫)。

**裁决 2:接受"坏动画不再静默"这一改动。** 失败会留在 `LastError`(界面行为不变、失败仍不崩),并有一条"故意造失败"的用例证明这个钩子真的能抓到。这正是本项目反复吃过的教训:**沉默的异常等于把缺陷藏起来**。

**裁决 3:接受抖动修复的做法。** 子代理在自查时发现两条通道顺序不同导致进程级计数不可比(`7838def` 改成按动画编号记账),以及中间态断言受逐帧采样抖动影响(`f4ab0bc` 改成 `PropertyChanged` 记账),最后把终态一律改成"条件成立 + 本用例起的动画都收摊"的泵法(`e9eb048`)。它自己留下的 `artifacts\flake-5.txt` 记着修复前那次失败;Lead 的 6 次连跑证明**修复之后是稳定的**。

**只记录、交给 D1 的一条测试卫生问题**:`C3dReachTests` 在关窗时把 4 条飞行 chip 动画留在"挂着"状态(它们只是没人推帧,生产里窗口一关就一起没了)。它会让**进程级的 `RunningAnimations` 总数一直被抬着** —— 以后谁想用那个总数做断言,先看 D-132 最后一条。不影响正确性,归 D1 清理。

---

## C4 实例管理簇:`ModsDialog` + `SavesDialog` + `LibraryPage` + `InstancePage` + `InstanceDetailPage`(2026-11-14)

工作包:把 Windows `Pages\ModsDialog.cs`(7694 B)/ `Pages\SavesDialog.cs`(8351 B)/ `Pages\LibraryPage.cs`(31560 B)/
`Pages\InstancePage.cs`(16780 B)/ `Pages\InstanceDetailPage.cs`(10450 B)移植到 Avalonia 11 / net8,
并把**两个导航页**注册好(`library` / `instance`)。**不碰**外壳 / 导航 / 色板 / `Controls.axaml` / 其它页面;
`src\TipCL.Core` 一字节未改。

### 1. 依赖审计(任务书第四次说"自包含",第四次不成立,见 D-133)

先按任务书要求自己审计:读五个文件的 `using` 与对 `TipCL.Models` / `TipCL.Services` / `TipCL.Pages` 的引用,
再逐个查 mac 侧有没有。门面计数(`Ui` / `Dlg` / `I18n` / `Motion` / `EnableDrop` / `Thumbs`·`LoaderIcons`)逐项吻合,
**缺的是模型层与一个小对话框**:

| 缺什么 | 大小 | 处理 |
|---|---|---|
| `Models\ModsTarget.cs` | 201 B | `Copy-Item` 字节级搬(SHA256 与 Windows 源一致、无 BOM、LF) |
| `Models\ModUpdateRow.cs` | 887 B | 同上 |
| `Models\SaveRow.cs` | 426 B | 同上 |
| `Models\BackupRow.cs` | 222 B | 同上 |
| `Models\MediaRow.cs` | 207 B | 同上 |
| `Models\GlobalModRow.cs` | 183 B | 同上 |
| `Models\VersionRowDto.cs` | 736 B | 同上 |
| `Pages\GlobalModsDialog.cs` | 1956 B | **WPF 代码** ⇒ 按同一套换名规则移植(`ModsDialog.cs:77` 的「全局模组」按钮直接调它;不搬就是静默桩)。这是本包**唯一**超出"五个文件"清单的产品文件 |

**按红线停下并报告的一处**:`InstanceDetailPage` 的「设置」分段内嵌 Windows 的 `VersionManagePage`(34490 B 的**整页**,
属别的工作包)⇒ 本包不搬,走注入缝 + 如实占位,见 D-134。

另有两条**不是缺文件、而是语义**的发现(都不是任务书说的那样),已分别记进 `NavModel` 相关小节与 D-133:

1. `library` 这个 id **在 Windows 与 mac 的导航表里都没有侧栏入口**(`NavModel.PageForNavKey` / `NavSpecs` /
   `GroupedNav` 里都没有它,WP4 信息架构把它换成了「版本管理」)。它只存在于 `MainWindow.NavDefs`
   (Windows <c>MainWindow.xaml.cs:109</c>),所以**只能按页面 id 直接切**(`Navigate("library")` / `--smoke --page library`),
   侧栏点不到 —— **两边一致**,不是移植漏项。`instance` 才是侧栏那一行(`PageForNavKey["instance"]`,
   行标题「版本管理」,而页面自己的 `Title` 仍是 Windows 的「实例」)。
2. `InstanceDetailPage` **不是导航页**(Windows 也没有它的 id):它由实例库「实例详情」按钮换进页内容,
   自己的「返回」再换回去。本包**没有**给它注册 id,并用一条用例把"没人给它注册"钉住。

### 2. 逐文件说明

| 文件 | 移植后 | 要点 |
|---|---|---|
| `ModsDialog.cs` | 11626 B | 目标下拉 + 计数 + 模组行(开关/名称/文件名/删除)+ 五个动作钮;`install_mod` 的 `extra.path/instance/name` 三个字段逐字保留;`sync` 标志挡"程序填下拉"那一次;`CheckUpdatesAsync` 的勾选卡、逐项 `apply_mod_update`、失败汇总成 Alert |
| `SavesDialog.cs` | 12224 B | 三段滚动区(存档 0.4/180 + 备份 150 裸 MaxHeight + 媒体 170 裸 MaxHeight);备份 `Take(20)`、媒体 `Take(60)`;媒体下拉文案 → 三个后端 kind 键;`PickSaveTargetAsync`(0/1 项短路 + 多项弹卡)与私有的 `InstallDatapackAsync` 一起搬 |
| `GlobalModsDialog.cs` | 3628 B | 见上表;两个按钮 + 开关写 `set_global_mod_enabled` |
| `LibraryPage.cs` | 43592 B | 左栏文件夹列表 + 右栏按加载器分组折叠的版本列表;WP-U3 的「自定义布局...」(显隐文件夹栏 + 导出/导入布局,直读直写 `"library"` 文档,版本列表恒可见);收藏走 `Prefs.VmFavorites`、折叠走 `Prefs.Expanded["lib:<key>"]`;「上次游玩」由 `get_playtime.sessions` 派生 |
| `InstancePage.cs` | 23664 B | Section 头 + 实例卡(图标/名称/副行/标签行/启动+模组+存档+更多);220ms 防抖过滤;上下移动选中 / 回车启动;右键 12 项菜单(含两处 `Win?.FlyToTasks`) |
| `InstanceDetailPage.cs` | 15553 B | 头部(返回/标题/副行/启动)+ 五分段 + 版本下拉 + 更多管理;共享(大锅饭)模式藏前四段并强制切「设置」;四个资源分段各拉各的后端方法 |

注册:`Pages\LibraryPageRegistration.cs` / `Pages\InstancePageRegistration.cs`,都是 `[ModuleInitializer]` + `PageRegistry.Register`
(与 `LaunchPageRegistration` 同一套,D-125),**没有动外壳**。

### 3. Windows → Avalonia 差异(逐条,完整清单写在每个文件的类注释里)

框架类型换法沿用 C3a/C3b/C3c 那一套(`UIElement`→`Control`、`Visibility`→`IsVisible`、`SetResourceReference`→`UiExt.Dyn`、
`Style = Ui.S(...)`→`StyleKeys.Apply`、`Mouse*`→`Pointer*`、`Cursors.Hand`→`new Cursor(StandardCursorType.Hand)`、
`FontWeights.*`→`FontWeight.*`)。本包新增的差异:

| # | Windows 写法 | mac 写法 | 理由 |
|---|---|---|---|
| 1 | `MenuItem.IsCheckable = true` | `ToggleType = MenuItemToggleType.CheckBox` | Avalonia 11 把"可勾选"做成三态枚举(本机核过 ref 程序集) |
| 2 | `ToolTipService.SetShowDuration(控件, 60000)` | **舍去**(留注释) | Avalonia 11.3.23 的 `ToolTip` 只有 `ShowDelay`、**没有** `ShowDuration`(逐名核过);提示照常显示,只是按平台默认时长消失 |
| 3 | `VisualTreeHelper.GetParent(d)` | `d.GetVisualParent()` | Avalonia 11 **没有 `VisualTreeHelper` 这个类**,等价物是 `Avalonia.VisualTree` 的扩展方法 |
| 4 | `CheckBox.Checked` / `Unchecked` 两个事件 | 单个 `IsCheckedChanged` + 判 `IsChecked` | Avalonia 11.3 把前两个标记成过时(CS0618),而本工程的告警红线只允许 `SYSLIB0014` ×1(与 `Ui.Check/Switch` 门面同一口径) |
| 5 | `PreviewKeyDown`(隧道) | `AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)` | Avalonia 的 `KeyDown` 直接订阅只有冒泡段 |
| 6 | `Keyboard.FocusedElement` | `TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()` | Avalonia 没有那个静态入口;判据仍"是不是 TextBox" |
| 7 | `MouseRightButtonUp` | `PointerReleased` + `e.GetCurrentPoint(...).Properties.IsRightButtonPressed` | Avalonia 没有独立的右键按下/抬起两个事件 |
| 8 | `StackPanel.FocusVisualStyle = null` | **去掉**(留注释) | Avalonia 11 的 `Control` 没有这个属性 |
| 9 | `ContextMenu.IsOpen = true` | `menu.Open(anchor)` | 见 D-124(无参 `Open()` 会抛) |
| 10 | `BclExtensions.Contains(s, q, StringComparison)` | `s.Contains(q, StringComparison)` | net48 的补丁扩展在 net8 上是 BCL 自带方法 |
| 11 | 局部变量名(反编译器风格) | 改成语义名 | 类型/顺序/判据/闭包捕获时机一字不改 |

**测试缝(D-115 / D-127 的口径,都是 internal,公开签名一个字节没动)**:`LibraryPage` 的
`FoldersVisible` / `InferType` / `SelectFolder` / `FolderPathFor` / `ToggleGroupForTest` / `GroupsNow` /
`LaunchVerAsync` / `UninstallAsync` / `OpenDetailAsync` / `BuildFolderMenu`;`InstancePage` 的
`Select` / `SelectedNow` / `HandleNavKey` / `BuildRowMenu`;`InstanceDetailPage` 的
`ShowSeg` / `ApplyVersionSegs` / `SettingsPageFactory`。**菜单被拆成"建出来"与"弹出"两步**:
Avalonia 的 `ContextMenu` 一旦 `Open` 就落进独立 popup 宿主,测试既拿不到也点不到菜单项。

### 4. 一处**用户可见的降级**(请 Lead 裁决,见 D-134)

`InstanceDetailPage` 的「设置」分段在 Windows 内嵌 `VersionManagePage`(34490 B 整页,属别的工作包)。
本包:分段按钮照 Windows 建、点击走新增注入缝 `SettingsPageFactory`;**挂点没接时**那一栏显示一句**如实的占位**
(「版本管理页还没移植」+ 为什么),不是空白、也不是假装能用。接上工厂就照 Windows 走 `EnsureLoadedAsync` + `OnShown`。
**这是本包唯一的用户可见降级。**

### 5. 冒烟:新基准 + 两组新帧

| 帧 | 退出码 | 字节 | SHA256 |
|---|---|---|---|
| 不带 `--page`(light) | 0 | **37681** | `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` = **C3c/C3d/C3e 基准逐位相同** |
| `--page launch --theme light` | 0 | 37681 | 同上(逐位相同) |
| `--page launch --theme dark` | 0 | **33579** | `7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` = **基准逐位相同** |
| **`--page library --theme light`** | 0 | **42417** | `905125C42C62804916C6766FC25BA8DDE0BA472BDA0F093AFA4ED4AB201EB5BD` |
| **`--page library --theme dark`** | 0 | **39268** | `FA926F0351BBDF1329FFC04E2FC76006224DA4DDC19961B15CDC76A79BFF988B` |
| **`--page instance --theme light`** | 0 | **27484** | `92C06E150B1D52CDEF6382F5A9E065C9190FCD82A85D38E8A5B847EE58768842` |
| **`--page instance --theme dark`** | 0 | **24511** | `F15820512ED66E301719D446C393CC2E1E55D0C782E0061BD5028BA7F187CFF2` |
| `--theme blue` | **2** | 不产出文件 | — |

**确定性**:四个新帧各连跑 2 次,每次**逐位相同**(4/4)。**新基准从此刻起生效**:
`library` light 42417 `905125C4...` / dark 39268 `FA926F03...`;`instance` light 27484 `92C06E15...` / dark 24511 `F1582051...`。
旧基准(37681 / 33579)保持不变 —— 新页面**没有**动到首屏(它们没有侧栏入口,外壳首屏仍是启动页)。

**画面里到底有什么(逐帧看 PNG + 逐像素取证,`System.Drawing`)**:

| 帧 | 看到的 |
|---|---|
| `library` light | 外壳照旧(标题栏 + 侧栏;侧栏没有「实例库」行,与 Windows 的导航表一致)+ 页面双栏骨架:左栏白卡(「文件夹列表」18pt 加粗、路径行、空的文件夹列表、「添加或导入」+ 四个动作钮:添加已有文件夹 / 导入整合包 / 新建实例 / 导入外部启动器),右栏(「版本选择」21pt 加粗 + 计数「共 0 个版本」+ 过滤框「过滤版本名」+ 「实例详情」「刷新」+ 副说明「双击版本直接启动;行尾可收藏/删除/设置」)+ 右栏中央的**空态**(堆叠图标 + 「这个文件夹还没有版本」+ 「点左侧「添加或导入」,或到「下载」页装一个」) |
| `library` dark | 同一套结构、深色底(标题栏 27,27,27;页面 26,26,26);明暗两帧 **494980 / 495000** 像素不同(99.996%) |
| `instance` light | 外壳照旧 + 一张白卡:过滤框「过滤实例名」+ 打开游戏目录 / 导入官方启动器 / 刷新 / **新建实例(Primary,主色 31,122,82)**;卡片下方是页面底色(实例列表为空) |
| `instance` dark | 同结构深色版;明暗两帧 **494807 / 495000** 像素不同(99.96%) |

**必须让 Lead 知道的两条观感问题(用户可见,都不是"没数据"造成的)**:

1. **`instance` 帧里 Section 的标题与副说明一个字都看不到**(逐像素核过:x=196~520、y=45~135 整块没有任何深色像素)。
   原因(读源码可定位):`Ui.Section` 是 `Grid("*,Auto")`,右列 Auto 放的是「计数 + 过滤框 + 四个按钮」那一簇(≈640px),
   而 mac 的页面区域被 196px 侧栏挤到 668px 宽 ⇒ 左列 `*` 被压成 **0 宽**,标题被裁没。
   Windows 在同样的 900px 窗口里没有侧栏(顶栏页签),页面区域有 900px,所以**碰不到这一条**。这是我们照抄的
   Windows 组合在 mac 外壳下的**直接后果**。
2. **`library` 帧里两栏整体比页面区域原点向左偏了约 44px**:左卡片的白色区域实测从 **x=197**(= 侧栏右缘)开始
   (正确位置应是 196+18=214),右栏标题的首字被左卡片**压住**(放大可读作「本选择」而不是「版本选择」)。
   机制上最像"两栏 body 的期望宽度超过视口 ⇒ 离屏 `ScrollViewer` 拿到了非零的水平偏移":左卡 `MinWidth=300` +
   18px 间隔 + 右栏头部那一簇(≈400)≈ 718 > 视口 632,而 `instance` 页没有水平溢出、它的白卡实测在 **220~873**
   (正是 18px 页边距的位置)且**没有偏移** —— 这条对照很干净。

  ⇒ 两条都请 Lead 裁决(是接受"窄窗下如此",还是后续小包把右栏头部改成可换行 / 让 `MinWidth` 与星号列的关系
  在 mac 的页面宽度下成立)。**本条的确切机制没有验完**:我准备的布局探针用例(量 `Bounds` / `Offset` / `Extent`)
  因为共享工作树被别的包改到编译不过而没跑起来(见第 7 节),所以上面是**像素取证 + 源码定位**的结论,
  不是几何量测的结论。

**另一条口径观察(如实记录,未深究)**:`library` 帧里右栏出现了只有 `RenderGroups` 才会写的两样东西
(计数「共 0 个版本」与空态)—— 而 `SmokeRunner` 全程 `BridgeReady` 为 false(`--smoke` 只 `SetupWithoutStarting`
+ `new MainWindow`,从不启动后端),按 D-96/C3c 的说法页面**不该**加载。本包没有去改外壳/后端接线(不在红线内),
只把这个观察记下来(它可能影响 D1 冒烟矩阵的"离屏页面处于载入前状态"这条口径)。

### 6. 测试(新增 **173 条用例** / 162 个测试方法;总数 3179 → **3352**)

| 文件 | 用例 | 覆盖 |
|---|---|---|
| `C4Fixtures.cs` | — | 期望值权威副本(`C4Expected`:分组表、两类菜单项、五个分段、媒体 kind 映射…)、`C4Scope`(全局快照:**多了 `InstanceDetailPage.SettingsPageFactory` 与 `PageLayoutStore.DirOverride`**)、`C4LayerHost`(带真窗口的浮层宿主)、`SettingsProbePage`、`C4Files`(泵/找控件/`Dlg.Panel` 卡片/菜单驱动/**假后端参数按 JSON 解码后断言**) |
| `C4DialogsTests.cs` | **67**(65 方法) | ModsDialog 12 条(标题/目标表/兜底项/行结构/计数/空态/开关两向/删除确认与取消/打开目录/导入降级/畸形行)、`CheckUpdatesAsync` 7 条、GlobalModsDialog 3 条、SavesDialog 20 条(标题两种/行结构/空态/备份 20 上限/媒体 60 上限/三个 kind 映射/备份/还原 `overwrite:true`/删除两种/打开存档/打开媒体/导出降级/目标短路与弹卡/数据包两支/畸形行/三段滚动口径/关动画终态)、面板关闭与三卡自查 2 条 |
| `C4LibraryTests.cs` | **65**(56 方法) | 注册 6 条(含 `library` 没有侧栏键这条现状)、构造与结构 8 条、加载与状态 6 条、搜索与折叠 6 条、收藏 1 条、布局自定义 5 条、交互 20 条(选中/菜单七项/Java/重命名/删除/双击启动/实例详情换页/卸载/安装流程/拖放上下文/动画残留)、`InferType` 10 条 Theory |
| `C4InstanceTests.cs` | **41** | InstancePage 15 条(标题/构造/Section 头/加载/卡片/副行/过滤与计数/空态/键盘三类/菜单九项/启动/打开目录两支/新建/导入/卡片复用/停表/载荷形状)、InstanceDetailPage 21 条(头部与 rail/版本下拉两种/共享与隔离/设置分段两条/更多管理/四个分段/空态/返回/启动/行几何)、注册现状 1 条 |

**两条通道跑出来的计数是 3352 = 3179(C3e 基线)+ 173(本包)** —— 与 `--list-tests` 口径逐类相加一致
(`67 + 65 + 41 = 173`)。

**顺带按 D-126 还原了一条时代前提(断言只增不减)**:`C3cPageStructureTests.Construction_does_not_throw_without_a_backend`
里那条"注册表里只有启动页"的断言,在本包注册两个页面之后不再成立 —— 处理是**先把前提还原**
(`PageRegistry.Unregister("instance"/"library")`,`C3cScope` 会在 Dispose 里还原注册表),
**原来那条断言一字不改地保留**,并**另加**两条"C4 确实注册了它们"的断言。**没有**删测试、没有放宽断言、没有加 Skip。

**测试期实测到的四条(写进 D-135)**:`BridgeClient` 把非 ASCII 转义成 `\uXXXX`(参数断言必须先解析 JSON);
Avalonia 的 `MenuItem` 在程序化 Click 下**不翻转** `IsChecked`(翻转在指针那条路里,`IMenuItem.RaiseClick` 是 internal);
220ms 防抖的 `DispatcherTimer` 在离屏泵里不可靠(改成"没立刻变"+"用 `RefreshAsync` 走一遍 `Render`");
`Now.AddHours(-1)` 这种时间戳凌晨 0~1 点会跨天,曾让 VSTest 通道红、进程内通道绿(已改成与跑测时刻无关的算法)。

### 7. 验证命令与实际结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build-server shutdown` + `dotnet build TipCL.Mac.sln -c Release --no-incremental -p:UseSharedCompilation=false -m:1 -v:q -clp:"ErrorsOnly;Summary"` | 退出码 **0**,**0 个错误**;警告按类别 = **`NU1900` ×6 + `SYSLIB0014` ×1**(与基线逐项相同;`WarningsOnly` 单独复跑确认过,本包一开始多出来的 CS0108 / xUnit2013 / xUnit2017 已全部改掉) |
| `dotnet test` 第 1 / 2 次 | `dotnet test TipCL.Mac.sln -c Release --no-build`(锁内跑) | 都是**退出码 0,失败 0 / 通过 3352 / 总计 3352** |
| 进程内执行器 第 1 / 2 次 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll`(锁内跑) | 都是**退出码 0,总计 3352 / 通过 3352 / 失败 0 / 跳过 0 / 不支持 0** |
| `--smoke` 八帧 | 见第 5 节的表(全部包在 `tools\with-build-lock.ps1` 里) | 退出码 0×7 + 2×1;四个新帧各复跑一次逐位相同 |
| `%LOCALAPPDATA%\TipCL` | 开工第一条命令 / 收尾各量一次 | 两次都是 **9328 个文件 / 889530606 字节 / 最新写入 2026-11-10 20:42:16** = Lead 基线逐项相同(**零写入**;最后一轮四条通道与八帧冒烟跑完之后又复量一次,仍是这三个数) |
| 真实桌面 / 图片 | 收尾 | 桌面 14 项、最新 **2026-11-12 19:02:53**(不变);图片 3 项、最新 **2026-11-06 23:39:03**(零写入) |
| 残留进程 | `Get-Process testhost,TipCL,java,adb` | 本包四条通道跑完时是 **0 个**;最后一轮复测时看到 **1 个 `testhost.exe`**(01:57:43 起、父进程 9044 **还活着**、CPU 32.5s、`Responding=True`)⇒ 判定是**并行会话正在跑测试**,本包**没有**去杀它(杀了会破坏别人的运行) |
| `%TEMP%` 下的 `tipcl*` | 收尾(最后一个数字是最终复测) | 总数 **113 → 142 → 153**;最新几条(`tipcl-tests-*` / `tipcl-det-*` / `tipcl-launch-*` / `tipcl-probe-*`)都落在本包窗口内(00:32–01:58),但这些名字在本仓库源码里**一个都不存在**,而本仓库自己的测试隔离目录是 `tests\TipCL.Tests\bin\Release\net8.0\test-home\<guid>`(收尾时 540 个)⇒ 判给并行会话,但**本包无法自证归属**,如实记录 |
| `src\TipCL.Core` 有没有被动过 | `git diff --name-status b664f4f HEAD -- src/TipCL.Core` | **0 行输出**(空) |
| Core 对 Windows 源的同一性 | 逐文件 SHA256(受版本控制的 248 个) | **238 有对应 / 216 一致 / 22 有改动 / 10 无对应**,与基线**完全相同** |
| 编码 | 对本包新增 / 改动的 21 个文本文件逐个 `ReadAllBytes` | 无一例外:**无 BOM、CR=0、U+FFFD=0** |

**共享工作树的一次干扰(如实记录,见 D-136)**:收尾阶段 `src\TipCL.App\Pages\SettingsPage.cs`、
`FirstRunWizard.cs`、`tests\TipCL.Tests\C5aFixtures.cs` 等**别的工作包**正在写入,树里一度编译不过
(`CS0103 NormalizeColor` / `CS0165 copy` / `CS0246 IBrush` / `window.Toast` 之类),导致本包的最后一次
"收尾复测"拿不到干净结论。⇒ 上表里的四条通道与八帧冒烟都是**本包亲自跑出来的**(构建/测试/冒烟全程包在
`tools\with-build-lock.ps1` 里);布局探针那一条因此**没跑成**,见第 5 节末尾。

**收尾追加(树又变了之后重跑,HEAD `fdfda0d`)**:

| 项 | 结果 |
|---|---|
| 构建 | 退出码 0,0 错误;警告 = `NU1900` ×6 + `SYSLIB0014` ×1(**与基线相同**)+ `xUnit2012` ×2(**来自并行工作包的新测试文件 `C5bTasksAccountTests.cs`,不是本包的文件**) |
| `dotnet exec`(进程内)2 次 | 都是**退出码 0,3434 / 3434,失败 0 / 不支持 0**(3434 = 3179 基线 + 本包 173 + 并行工作包 82) |
| `dotnet test`(VSTest)2 次 | 退出码 1:**3433/3434** 与 **3432/3434**,失败的**全部**是并行工作包的 `C5aSettingsTests`(`Saving_a_theme_pack_prompts_then_calls_save_theme` / `The_language_preview_calls_translate_three_times`)—— 本包的用例在两次里 **0 失败**(`[FAIL] C4*` 计数 = 0) |
| 本包用例在两条通道里的通过数 | `C4DialogsTests` **67** / `C4LibraryTests` **65** / `C4InstanceTests` **41** = **173**(两条通道逐类相同) |
| 八帧冒烟在收尾树上**复跑** | **与上表逐位相同**(含 no-`--page` 37681 `2603BF6A...` 与 `--page launch --theme dark` 33579 `7D7AFDD5...`,以及四个新帧)⇒ 别的包的提交没有动到渲染,新基准继续有效 |
| 本包对 C3c 那条断言的**第二次**更正 | 收尾时 `C3cPageStructureTests.Construction...` 又红了:并行工作包(C5)也注册了页面,而逐项 `Unregister` 挡住的是**点名的那些** ⇒ 改成"把所有非 launch 的 id 一并临时摘掉"(断言只增不减,原断言一字不改);这条同时修掉了别人的红 |

**四通道全绿的证据落点**:本包的 173 条用例在进程内通道 **3434/3434 全绿**两次、在 VSTest 通道两次里 0 失败;
本包交付时(树状态 HEAD `b253090` + 并行提交)四条通道是 **3352/3352 全绿**(见上表)。**当前树上唯一的红**
来自 `C5aSettingsTests`(并行工作包尚未完工),不是本包的文件。


### 8. 未验证项

1. **macOS 真机**:渲染观感、`--smoke` 像素、真桌面下两栏页在 900×550 的实际排布、系统红绿灯下的布局 —— 一律**未验证**。
2. **第 5 节那两条观感问题的确切几何没有量测**(布局探针用例没跑起来):左卡片左缘的精确 x、`ScrollViewer`
   的 `Offset` / `Extent` / `Viewport`、星号列与 `MinWidth` 的关系 —— 都只有像素取证 + 源码定位的结论。
3. **`library` 帧里页面被加载这件事的机制未查明**(与 D-96/C3c 的"离屏不加载"口径不符),见第 5 节末尾。
4. **指针事件薄壳那一层**照 D-115/D-127 同样没有用真输入驱动过(行选中 / 双击版本 / 右键菜单 / 组头折叠走的是
   同一批 internal 处理器体)。
5. **对话框的真实弹出与定位**:离屏下 `ContextMenu.Open(anchor)` 与 `Dlg.Panel` 的定位没验(需要真窗口管理器)。
6. **文件选择器相关的分支**(导入 jar / 导入整合包 / 添加文件夹 / 导出存档 / 导出媒体 / 导出整合包)在离屏里
   `Dlg.PickFile(s)` / `SaveFile` / `PickFolder` 一律返回 null,所以只验了"如实降级、不写后端"这一半。
7. **`InstanceDetailPage` 的「设置」分段**只验了"没接工厂时的如实占位"与"接上工厂时的嵌入 + EnsureLoaded + OnShown";
   真正的 `VersionManagePage` 内容**未验证**(它还没移植)。
8. **`preferInstance` 的偏好字段是 private**,`LaunchVerAsync` 用例只能侧证"启动页被要求加载过"+ `save_settings` + 切页,
   没有直接断言 `_preferredVersion` 被写成 `1.20.1`。

### 9. 遗留与风险

1. **第 5 节的两条用户可见观感问题**(instance 帧 Section 标题被压成 0 宽;library 帧两栏内容左偏且左卡被侧栏盖住)
   —— 请 Lead 裁决,可能要给"窄窗下右栏头部换行"或"两栏比例/最小宽按 mac 页面宽度重算"单开一个小包。
2. **D-134 的降级**(「设置」分段占位)等版本管理页那一包接上 `SettingsPageFactory` 即可消除。
3. **`LibraryPage.FolderPathFor` 兜底路径用反斜杠**(照抄 Windows,D-133):mac 上只在"实例没给 Path"那一支显示成
   Windows 风格;要改请单开小包。
4. **`Ui.Section` 的 `*,Auto` 在窄页面下会把标题压成 0 宽** —— 这是 C1c 门面的既有行为,凡是用到 `Section` 的
   页面在 mac 的 196px 侧栏下都可能撞上(本包只记录,没有改门面)。
5. 本包**没有**给 `library` 加侧栏入口(与 Windows 一致,见第 1 节):要让它出现在侧栏得改导航表(红线外,请 Lead 决定)。
6. `%TEMP%` 里 29 个新增 `tipcl-*` 目录的归属无法自证(并行会话)。

## C5b:下载任务 / 账号 / 启动向导(「下载」页按 Lead 裁决挂给下一包 C6)

**范围(独占)**:`src\TipCL.App\Pages\TasksPage.cs` / `AccountPage.cs` / `FirstRunWizard.cs` + 两个注册文件
`TasksPageRegistration.cs` / `AccountPageRegistration.cs`(`[ModuleInitializer]`,D-125),外加
`src\TipCL.App\Models\` 里的 `AccountSkin.cs` / `SkinUrls.cs`(**字节级搬**)与 `SkinPng.cs`(见下);
测试是我自己的 `tests\TipCL.Tests\C5bFixtures.cs` + `C5bTasksAccountTests.cs`。**`download` 的注册与导航没碰**(C6 的活)。

### 1. 依赖审计(Lead 的扫描历来不完整,这次先查后动)

| 文件 | 审计结论 |
|---|---|
| `TasksPage.cs`(7227 B / 263 行) | **可移植**。mac 侧齐:`TaskStore`(`Added`/`Updated`/`Cleared`/`Rows`/`RunningCount`/`ClearFinished`/`Remove`)/`TaskRow`/`Ui.LogBox`·`Prog`·`Section`·`Empty`/`Motion.Stagger`·`HoverLift`·`Progress`·`FadeIn`/`CrashUi.ShowAsync`/`EnableDrop(() => FileDrop.DropContext.None)`/`BlockHost("tasks")`。要自己解决的两处框架差异:`ScrollToEnd` 与 `VerticalOffset/ExtentHeight/ViewportHeight`(D-128 的模板 `ScrollViewer` 口径)、`SetResourceReference`(既有 `Dyn(...)`)。 |
| `AccountPage.cs`(17904 B / 582 行) | **可移植**。齐:`AccountRow`/`AuthlibPreset`/`ThumbTile`/`Thumbs.LoadAsync`/`BridgeEvent`/`Dlg` 全套/Ui 全套。缺件三类:① 小而自包含 → 字节级搬 `AccountSkin`(501 B)+`SkinUrls`(141 B);② 框架差异(`Clipboard.SetText`、`RenderSettings` 的最近邻、`PasswordBox`);③ **外壳缺一员**:Windows 的 `_ = AppServices.Window?.SyncAccountChipAsync();` 依赖顶栏账号圆片,而 mac 外壳在 C2b 就故意删了它(MainWindow.cs:40)—— Lead 裁决**整条不写 + 注释 + 登记**,不给外壳加成员。 |
| `FirstRunWizard.cs`(8667 B / 230 行) | **可移植(带一条注入缝)**。它结尾那句 `AppServices.Window?.GetPage("settings") is SettingsPage → RefreshAsync()` 依赖 **SettingsPage**(由 C5a 移植),Lead 选 (a):做 `FirstRunWizard.SettingsRefresh` 注入缝(与 D-119/D-122/D-131 同口径),**没接就什么都不做**。另:`MaybeShowAsync()` 在 mac 侧没有任何调用点(Windows 是外壳启动时调)—— 不动外壳,如实记成"外壳级缺口"交 Lead。 |
| `DownloadPage.cs`(7780 B) | **挂起(C6)**。它自己只有 7.8 KB 不是因为简单,而是因为它是 `CatalogPage`(35 KB)+`InstallPage`(15 KB)两个整页的**分段宿主**(`ShowSeg(k)` 直接 `new CatalogPage(Kinds[k])`);做"六段占位"的降级版等于把空页面包装成完成,Lead 已裁决不这么做。 |

### 2. `SkinPng`:报的是"字节级搬",实测**搬不了**(前提不成立,如实记录)

Windows 那份 3878 B 的 `Pages\SkinPng.cs` 整个建立在 WPF 成像 API 上
(`DrawingVisual` / `DrawingContext.DrawImage` / `CroppedBitmap` / `RenderTargetBitmap` / `BitmapImage.CacheOption`),
Avalonia 一个同名等价物都没有。所以保留的是**算法与全部常量**(12 个部件的 `(sx,sy,sw,sh,dx,dy,mirror)` 与顺序、
逻辑画布 16x32、`slim` 的 3 像素臂宽、64x32 旧皮肤的 `height*2 <= width` 分支、输出一律 **128x256**、
`PixelWidth < 64 → null`),画法换成 `Canvas` + `Image`(裁切 = 整图放大后塞进 `ClipToBounds` 的 Canvas,
镜像 = `ScaleTransform(-1,1)` + `RenderTransformOrigin(0.5,0)`,最近邻 = `BitmapInterpolationMode.None`),
最后 `RenderTargetBitmap` 拍图。落点在 `src\TipCL.App\Models\SkinPng.cs`(Windows 在 Pages 下,这里按 Lead 指定的 Models 目录)。

### 3. 落地时踩到的三条框架事实(写进 D-137)

1. `Button.Content` 改了,**`AutomationProperties.Name` 不会跟着变**(`Ui.Btn` 创建时写死的)—— 用自动化名判断"现在显示的是取消还是清除"会拿到过期答案(我第一版就栽在这,修的是**测试期望**,页面代码没动)。
2. Avalonia 的 `Control.IsVisible` 是**控件自己的**标志,祖先被藏它不会变假;Windows 那边藏的是 `logWrap`(`Visibility.Collapsed`),所以断言要落在**那层壳**上(`Ui.Pane` 的 Border)。
3. `Dlg.Panel` 推出来的**不是** `DialogCard`,所以 `C3bFiles.WaitDialog()` 对向导必然超时(EL 是给 `Confirm`/`Ask` 用的);面板要按标题泵。

### 4. 验收

| # | 命令(全部经 `tools\with-build-lock.ps1`) | 结果 |
|---|---|---|
| 1 | `dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1` | 退出码 **0**,**0 个错误** |
| 2 | `dotnet test TipCL.Mac.sln -c Release --no-build --nologo` 第 1 / 2 次 | 两次都 `失败: 11,通过: 3423,总计: 3434` —— **本包 33 条 0 失败**;11 条全在 c4-pages 名下(C5aSettingsTests / C1aPaletteTests / C3cPageStructureTests) |
| 3 | `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` 第 1 / 2 次 | `总计 3434,通过 3424,失败 10` / `总计 3434,通过 3427,失败 7`;**不支持 0**;同一份 DLL 两次失败数不同(10 / 7)⇒ 那 7~11 条是**非确定性**的,不是本包引入 |
| 4 | `dotnet test ... --filter FullyQualifiedName~C5b`(单独) | 退出码 **0**,`已通过! - 失败: 0,通过: 33,总计: 33` |

`--smoke`(全部走锁;既有两帧**逐字节不变**):

| 帧 | 退出码 | 字节 | SHA256 |
|---|---|---|---|
| 不带 `--page` | 0 | **37681** | `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` = 基准逐位相同 |
| `--page launch --theme dark` | 0 | **33579** | `7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` = 基准逐位相同 |
| `--page tasks`(**新**) | 0 | **27719** | `0E591B87A2B2B9E3CAC7968F1BBFD399DF9A704657506CFE38B3A5EB4E5590E9` |
| `--page account`(**新**) | 0 | **33173** | `D3F593398D737BE9512A35D4AEBB080AB87379C6EC86C52139A8426E9253BAF6` |
| `--page download` | 0 | 37681 | 与不带 `--page` **逐位相同** —— 因为 `download` 故意还没注册(C6),`Navigate` 静默不动(与 C3c 记的"其它 id 什么都不切"一致) |

新帧画面(逐帧看过):`tasks` = 外壳(侧栏「下载任务」选中)+ 页面头「下载任务 / 安装、下载、启动都在这里排队」+ 右上「清除已完成」,内容区**空**;
`account` = 外壳(「账号」选中)+ 页头「账号」+ 五个入口(刷新/统一通行证/皮肤站登录/微软登录/添加离线账号)+ 240 宽头像卡(问号片 + 未登录)。
两帧里都没出现"空态文案 / 账号卡"这类**加载钩子**产出的内容,原因是 `--smoke` 没有后端 ⇒ `MainWindow.Navigate` 的 `runHooks`(= `BridgeReady`)为假 ⇒ `EnsureLoadedAsync` / `OnShown` 不跑(PageHost.cs:108),所以帧里只有构造函数建的那部分(与 C3c 启动页帧同一道理)。

### 5. 编码(字节级实测,不是断言)

`TasksPage.cs` 11696 B / BOM=False / CR=0 / LF=324 / U+FFFD=0;`AccountPage.cs` 23369 / False / 0 / 664 / 0;
`FirstRunWizard.cs` 11815 / False / 0 / 281 / 0;`TasksPageRegistration.cs` 1410 / False / 0 / 27 / 0;
`AccountPageRegistration.cs` 1258 / False / 0 / 26 / 0;`SkinPng.cs` 6607 / False / 0 / 188 / 0;
`AccountSkin.cs` 501 / False / 0 / 21 / 0;`SkinUrls.cs` 141 / False / 0 / 8 / 0;
`C5bFixtures.cs` 12622 / False / 0 / 367 / 0;`C5bTasksAccountTests.cs` 42158 / False / 0 / 857 / 0(31 条 `[Fact]` + 1 条 `[Theory]`×2 = 33 条用例)。
`PROGRESS.md` 与 `DECISIONS.md` 的追加都是**字节拼接**(`[IO.File]::WriteAllBytes`),没有走 `Get-Content | Set-Content`(D-36)。

### 6. 留给 Lead 的

1. **本包四条运行里"本包 0 失败"是确定的**:两次 `--filter` 与四次全量的结果一致。剩下的 7~11 条红全在 c4-pages 名下,而且**同一份 DLL 两次失败数不同**(10 / 7),请它自己判定是"还没调完"还是"测试本身抖"。
2. `MaybeShowAsync()` 没有调用点、`SettingsRefresh` 注入缝还没接线 —— **都等 SettingsPage 落地后由 Lead 统一接**(我没动外壳)。
3. `download` 按裁决留给 C6(与 `CatalogPage` + `InstallPage` 一起做)。

## 运营规则(2026-11-14 凌晨,多代理并行后补写)

这一晚踩到的坑都很贵,写成规则给后面的人/代理看:

1. **长活必须派给 `spawn_teammate` 的 teammate,不要用 `subagent`。** `interrupt_agent` 只对 teammate 有效;`subagent` 起的子代理**够不到、也停不掉**(它跑在 harness 进程里,没有可 kill 的目标)。这次一个 `subagent` 一路写进了别人的范围、还提交了一棵**编译不过**的树,而 Lead 无法叫停。
2. **"测试全绿"不等于"树能编译"。** 那棵坏树的提交信息写着 160 条新用例,实际把探针属性命名成 `Loaded`/`Shown`,撞上 Avalonia 的 `Control.Loaded` 事件 ⇒ CS0079。**任何验收都必须先构建、再跑通道**,并且两条通道各跑两次(单通道绿证明不了什么)。
3. **构建锁要把"构建 + 运行"当作一个整体。** `tools\with-build-lock.ps1` 只锁住它包住的那条命令;如果别人在你 `build` 完之后重建共享 `bin/`,你的 `--no-build` / `dotnet exec` 跑的就是**另一个二进制**(Lead 因此拿到过假红)。现成做法见 `artifacts\lead-verify.cmd`:一个脚本里串起 build + VSTest + 进程内,交给锁一次执行。
4. **多写入者共树时:绝不 `git add -A`,只 add 自己的路径**;别人的未提交改动**不许** revert/checkout/提交。每个新任务书里都写这一条。
5. **每落盘一个文件就构建一次。** 攒着最后一起编的代价:一次把全队卡红两回(`TasksPage` 缺 `using Avalonia;`、`FirstRunWizard` 的 `body` 重名、`C5aFixtures` 缺 `Avalonia.Media`)。常见漏项:`Avalonia`(Thickness/GridLength/Point)、`Avalonia.Media`(IBrush/Color)、`TipCL.Services`(`Dyn`/`Ui` 扩展方法)、`TipCL.Themes`、`Avalonia.Styling`(ThemeVariant)。
6. **判断"文件还在不在被写",只能看字节/哈希,不能看 `Get-Content` 的行数。** PS 5.1 的 `Get-Content` 会把无 BOM 的 UTF-8 当 ANSI 解,**吞掉 LF**(实测 1224 vs 1254 行),造成"别人还在写我的文件"的假信号。凡读文本请带 `-Encoding UTF8`。
7. **并发负载会让"按固定轮数泵"的异步测试假红。** 遇到不属于自己范围的失败,先 `--filter` 单独复跑;复跑绿了就是并发干扰。长期修法:把固定轮数换成"条件成立 + 有截止时间"的等待。
8. **探针属性/局部变量名要避开 Avalonia 与 WPF 基类既有成员**(`Loaded`/`Shown`/`body` 这些名字已经各踩一次),否则是编译错误或成员隐藏告警。
9. **全局状态类的前提断言会被下一个包打破**(「注册表里只有启动页」这类)。既定解法是**还原前提、断言不删**(D-126):先 `Assert.Contains` 钉住新增的 id,再 `Unregister` 把它们摘掉,原来的 `DoesNotContain` 一字不改地留着。

## C5a:设置页(SettingsPage)移植 + 主题层可写色板覆盖层

### 依赖审计(先做的第一件事,四类)

Windows 侧三个 partial:`SettingsPage.cs` 2012 行 + `SettingsPage.Themes.cs` 390 行 + `SettingsPage.PhoneSync.cs` 76 行 = **2478 行**,
类 `TipCL.Pages.SettingsPage : PageBase`,7 个分段(通用 / 账号 / Java / 下载与网络 / 服务器 / 游戏时长 / 反馈与关于),
前 6 张卡挂在同一个 `BlockHost` 里。

| 类 | 内容 | 处置 |
| --- | --- | --- |
| A:本身是页面的缺件 | `AccountPage` / `JavaPage` / `ServersPage` / `PlaytimePage` / `FeedbackPage` / `FirstRunWizard`(另有 `PhoneSyncPanel`) | **注入缝 + 如实占位**(D-119 / D-134 先例);`AccountPage` / `FirstRunWizard` 由 C5b 移植,我不碰那两个文件(见 D-140) |
| B:主题运行时改写的基础设施 | Windows 写 `App.PaletteDictionary`,还有 `App.IsDark` / `App.PaletteUri` / `App.RaiseThemeVisualsChanged` | **新建 `Themes\PaletteOverride.cs`**(Lead 裁决,落在主题层;见 D-139) |
| C:小而自包含的 DTO | `CleanerEntry` 136B / `CleanerPreview` 481B / `CleanerResult` 131B / `ThemeRow` 326B / `BackgroundState` 149B / `SmartRecommendation` 655B / `UpdateInfo` 370B | **字节级搬**(共 2248 字节),`TipCL.Core` 一字节未动 |
| D:离屏/测试不能碰的三处 | 重启启动器(`Process.Start` 自身 + `Shutdown`)、复制 MCP 地址(`Clipboard.SetText`)、壁纸预设缩略图(`pack://` + `BitmapImage`) | 注入缝 + mac 既有件(见 D-140) |

**已核对过的好消息**:222 处 `Ui.*` 用到的成员(Btn/Card/Check/Combo/Field/G/Glyph/H/Input/Muted/Pane/S/Section/Sld/Small/Switch/Txt/V)
mac 侧**一个不缺**;`Wallpaper` / `FileKinds` / `Fmt` / `BridgeHost` / `Clamp` / `Core.Launcher.AppInfo` / `PhoneLink` / `PhoneClient` /
`McpServer` / `FileMemory` / `BlockHost` / `FileMemory` 都在;29 个后端方法名全是 RPC 字符串,无编译依赖;`B.*` / `C.*` 只是资源键,不是类型。

### 交付物

| 文件 | 说明 |
| --- | --- |
| `src\TipCL.App\Themes\PaletteOverride.cs`(新,8995B) | 运行时可写色板覆盖层:`Set` / `SetColor` / `SetBrush` / `Remove` / `Reset` / `TryGetNow` / `CountNow` / `KeysNow` / `AttachedNow` / `VariantNow` / `IsDarkNow` / `Builtin` / `BuiltinColor` |
| `src\TipCL.App\Pages\SettingsPage.cs`(新,101416B) | 页面本体:7 段导航 / 通用段 9 块 / 下载与网络段 1 块 / 反馈与关于段(Grid 双行)/ 自动保存 / 壁纸 / 主题包 / 清理 / 推荐 / AI / 记忆 / MCP / 手机同步接线 |
| `src\TipCL.App\Pages\SettingsPage.Themes.cs`(新,17627B) | 主题包五个动作 + `ApplyVisuals` / `ApplyAccent`(对比度补偿那一套逐字照抄)+ 颜色数学,落点改成 `PaletteOverride` |
| `src\TipCL.App\Pages\SettingsPage.PhoneSync.cs`(新,4975B) | 手机同步卡片(走 `PhoneLink` / `PhoneClient`,`PhoneSyncPanelRunner` 缝) |
| `src\TipCL.App\Pages\SettingsPageRegistration.cs`(新,1414B) | `[ModuleInitializer]` 注册页面 id `settings`(D-125) |
| `src\TipCL.App\Models\*.cs`(新 7 个) | C 类七个 DTO,字节级搬 |
| `tests\TipCL.Tests\C5aFixtures.cs`(新) | `C5aScope`(全局静态快照还原,含色板覆盖层与设置页静态视觉状态)、`C5aFiles`(泵/查找/假后端参数)、`C5aProbePage`(分段探针) |
| `tests\TipCL.Tests\C5aPaletteOverrideTests.cs`(新) | 覆盖层机制自测 9 条(含优先级探针,见 D-139) |
| `tests\TipCL.Tests\C5aSettingsTests.cs`(新) | 设置页 40 条 |
| `tools\c5a-verify.cmd`(新) | 一次锁里跑 build + VSTest + 进程内(避免"build 完被别人重建成另一个二进制") |
| `tools\c5a-smoke.ps1`(新) | 五个冒烟帧 + 基准逐字节比对 + 像素取证 |
| `tools\c5a-append-docs.ps1`(新) | 按字节把本节拼到 `PROGRESS.md` / `DECISIONS.md` 末尾(D-36) |

### 用例数

`C5aPaletteOverrideTests` 9 + `C5aSettingsTests` 40 = **49 条**(全量套件 3437 条的一部分)。覆盖:
注册(模块初始化器 / 导航表 / 外壳按 id 解析)、无后端构造与加载降级、静态结构(7 段导航 / 9 块 BlockHost / 六行外观卡 /
7 个滑杆范围与默认值 / 9 个下拉的选项 / 13 个输入框占位 / 6 个主题色色块 / 4 个预设背景缩略图 / 关于卡片)、
A 类占位与"切段才建"(探针页计数)、D 类三个注入缝、加载与保存的后端调用名与参数(整份 `save_settings` 载荷逐键抽检)、
主题包五动作、检查更新三条路、清理(勾选真的进载荷)、智能推荐、AI 测试连接与拉模型(内置模式不拉)、
壁纸应用/撤销/恢复出厂、深色切换、色块与背景预设写进覆盖层、默认色撤掉覆盖、`Extras` 落盘、记忆卡片、语言预览。

### 运行(四条 + 原子两轮)

| # | 命令 | 结果 |
| --- | --- | --- |
| 1 | **`tools\c5a-verify.cmd` 第 1 轮**(一次锁里 build + VSTest + 进程内) | `BUILD_EXIT=0`;**`已通过! 失败 0 / 通过 3437 / 总计 3437`**;进程内 **总计 3437 个用例,通过 3437,失败 0,跳过 0,不支持 0** ⇒ `VSTEST_EXIT=0` `INPROC_EXIT=0` |
| 2 | **`tools\c5a-verify.cmd` 第 2 轮** | 与第 1 轮逐字相同:三条退出码全 **0**,两条通道 3437 / 3437 全绿 |
| 3 | `--filter FullyQualifiedName~C5a`(构建后) | 退出码 **0**:`已通过! - 失败: 0,通过: 49,已跳过: 0,总计: 49`(连跑多次一致) |
| 4 | `--filter FullyQualifiedName~C5`(C5a + C5b) | 退出码 **0**:`已通过! - 失败: 0,通过: 82,已跳过: 0,总计: 82`(连跑 4 次一致) |

**修抖动之前的一轮(如实记)**:同一把脚本在那时是 `BUILD_EXIT=0` + VSTest `失败 1 / 通过 3436 / 总计 3437`,
唯一红的是 **`C4LibraryTests.Library_folder_layout_defaults_to_two_columns`**(不是本包,下一轮自己绿了 ——
同一份二进制两次结果不同 ⇒ 那条用例自己的抖动);本包的两条抖动(见 D-141)修掉之后才有上面那两轮全绿。

### 冒烟(既有帧逐字节不变 + 两个新基准)

| 帧 | 命令 | 结果 |
| --- | --- | --- |
| 不带 `--page` | `TipCL.exe --smoke --out artifacts\c5a-nopage.png` | 退出码 **0**,**37681 字节**,SHA256 `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` = **C3c 基准逐字节相同** |
| `--page launch --theme dark` | 同上 | 退出码 **0**,**33579 字节**,`7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` = **基准逐字节相同** |
| `--theme blue` | 同上 | 退出码 **2**(参数错,符合预期),打用法、**不产出文件** |
| `--page settings --theme light` | `--smoke --page settings --theme light --out artifacts\c5a-settings-light.png` | 退出码 **0**,**45594 字节**,`C19BF1C465C95116A0BBC7B4F09DF55B00CFB588FB8D258EE2C40F68C8DD4ECF`(Lead 独立复跑一致) |
| `--page settings --theme dark` | 同上 `--theme dark` | 退出码 **0**,**43745 字节**,`CE33BEFE6841EDB5B38A564EF203F7BF8D61BFCEA5FC0BE3A41208CF70465691`(Lead 独立复跑一致) |

**两帧里看得到什么(像素取证,`tools\c5a-smoke.ps1` 用 `System.Drawing` 采样,每 2 像素取一点)**:
- 尺寸都是 **900×550**;light 的主色 `#FFFFFF`(占采样点 32%,702 种颜色),dark 的主色 `#232323`(占 30%,652 种颜色);
- 左带(x<240,侧栏 + 设置页自己的 168 宽导航)x 与右区都有大量非主色像素(light:32797 / 50896;dark:33000 / 53417)⇒ 两侧都画了东西,不是空壳;
- 六个主题色色块的纯色像素都在:light 下 `#2E9B6B`=426(当前选中那个,另加主色按钮/标签也用它)、其余五个各 **78**;
  dark 下 `#2E9B6B`=42、其余五个各 **78** ⇒ 主题色那一行六块都真的渲染出来了(选中块带描边,量最大);
- 明暗两帧 **100%** 采样点不同(30907/31050)⇒ 主题切换真的换了整套配色,不是"同一张图换了个名字"。
- **看不到什么(如实说)**:离屏外壳 `BridgeReady` 为 false(D-96),所以各卡片里的**数据**全是预加载态(计数 0、列表空、AI 状态"检测中"之类);
  分段 1/2/4/5/6 的内嵌页是占位文案(那六个页面还没移植);`FlyToTasks` 一类动画在离屏 `Motion.Enabled` 恒假,不会出现在帧里。

### 未验证 / 遗留

1. **只有真机能验证**:重启启动器(`ProcessLauncher` 默认真起进程 + 桌面生命周期 `Shutdown`)、真剪贴板写入、真壁纸/视频播放;
   离屏与 headless 里这三条都走注入缝或直接不生效。
2. **A 类六个缺件仍是占位**:`AccountPage`(C5b 已移植,缝待接)/ `JavaPage` / `ServersPage` / `PlaytimePage` / `FeedbackPage` /
   `FirstRunWizard`(C5b 已移植,缝待接)/ `PhoneSyncPanel`。**接缝由提供方在自己文件的模块初始化器里接**
   (照 `MainWindow` 接 `LaunchPage.FlyToTasksSeam` 的先例),`SettingsPage` 本身不动。
3. **`FlyEnabled` 目前没有消费方**:Windows 的 `MainWindow.FlyToTasks` 会看 `SettingsPage.FlyEnabled`(关掉"下载飞入动画"),mac 的
   `MainWindow.FlyToTasks` 只看 `Motion.Enabled` ⇒ 「下载飞入动画」开关**只保存不生效**。修法在外壳(不是我的文件),请 Lead 裁决。
4. **设置页的自动保存防抖**(500ms `DispatcherTimer`)在离屏泵里未必 Tick(D-129),所以用例走 `SaveNowAsync()` 内部入口;
   "真桌面上防抖真的会触发"这条只有真机能验。
5. 全量套件里 `C4LibraryTests.Library_folder_layout_defaults_to_two_columns` 那一条的抖动不是本包的,留给它的作者。

## C5c:窄页面(668px)下的两处布局缺陷 + 一条待查异常

**来源**:C4 代理收尾时自己发现的;Lead 认可并派给我,**要求在门面层修一次**(与 C3d `FlyToTasks`、C3e `Motion` 同一条道理)。

### 1. 先用探针把几何量出来(不靠截图猜)

新增 `tests\TipCL.Tests\C5cLayoutProbeTests.cs`(3 条):把页面放进**固定 668 宽的宿主**、窗口 900×550(与 `--smoke` 同尺寸),
读 `Bounds` / `ScrollViewer.Offset` / `Extent` / `Viewport` / `TranslatePoint` / `Grid.GetRow`。

**缺陷 1(实例页标题被压成 0 宽)**:修复前实测 `页面宽=668 标题宽=0 副说明宽=0 右簇宽=653`。
根因确认:`Ui.Section` 是 `G(null,"*,Auto")`,左列 `*` 没有最小宽度、右列那一簇(计数 + 搜索 + 4 个按钮 ≈ 653px)不收缩 ⇒ 左列被算成 0。
像素佐证:`instance` 帧标题区(196-520,45-135)深色像素 **153 → 510**(修复前那点像素是别的东西,标题整块没画出来)。

**缺陷 2(库页内容左偏)**:探针实测 `Offset=(0,0) Extent=0 Viewport=0 左卡起x=-30 左卡宽=300 左卡布局x=-48 列宽=205/18/409`。
**机制与"水平偏移"的猜想不同,真机制是**:左卡 `MinWidth = 300`(`LibraryPage.cs:232`,2026-11-07 为"窄窗别占太多"留的地板),
而左列是**星列** —— 668 页面区里那条星列只有 **205** 宽 ⇒ 300 宽的卡片**溢出自己的格子**;
Avalonia 把"宽于格子的子元素"居中摆放 ⇒ 左移 `(205-300)/2 ≈ 48`(探针实测布局 x = **-48**,与算式逐位吻合)。
用户可见结果:卡片压掉 18px 左页边距(像素实测:**修复前白卡白像素从 x=196 起 → 修复后从 x=216 起**,196+18=214 加边框即 216),
同时向右多占 95px **盖住右栏标题的头几个字**(Lead 观察到的那一条)。

**缺陷 3(待查异常)结论:`PageHost` 的门闸没漏开,两边说法都成立,不是 bug。**
- `PageHost.Navigate(..., runHooks)` 只在 `runHooks` 为真时才 `EnsureLoadedAsync + OnShown`(PageHost.cs:108);
- `MainWindow.Navigate` 传的是 `BridgeReady`(MainWindow.cs:883),而 `BridgeReady` 默认 false、`--smoke` **不调** `StartBackend()`
  (MainWindow.cs:427 的注释就写着"离屏 --smoke 不走这里")⇒ 冒烟里**确实不跑加载钩子**(与本包 C5b 的报告一致);
- 但 `--page library` 那一帧里的计数与空态**不是加载钩子画出来的**:`LibraryPage` 的**构造函数**里就调了 `RenderGroups()`
  (`LibraryPage.cs:244`,那是搜索框 `TextChanged` 也复用的重绘入口)⇒ 帧里的"共 N 个版本"与空态是构造期就写好的。
  C4 代理的观察与 C5b 的结论因此**并不矛盾**,门闸行为符合 D-96/D-102 的口径。

### 2. 修法(两处,理由与影响面)

**缺陷 1 —— 修在门面 `Ui.Section`(所有用它的页面一次受益)**:新增 `internal static class SectionNarrow`(同文件 `Ui.cs`)。
- 判定阈值**自校准**:用两边的 `DesiredSize` 自身当门槛 —— 可用宽 < 标题需要 + 右簇需要 就降级(把右簇换到第二行、跨两列、靠右),
  `Slack = 6px` 是**回滞带**(没有它会在阈值上互相触发:改列宽 → 触发再判 → 又改回去);
- 摆得开就恢复原样 ⇒ **宽页面永远走原路、像素不变**;
- 没有在每个页面各打一个补丁,也没有写死"页面窄于 X 就怎样"的魔数。

**缺陷 2 —— 修在 `LibraryPage` 自己的两栏配方(这是"页面配方"而不是门面问题)**:把 300 的地板**同时写到列上**
(`body.ColumnDefinitions[0].MinWidth = 300`),让卡片**正好填满自己的列** ⇒ 既不越界(左)也不越位(右)。
- 为什么不动那三条 Windows 几何:`LibraryPage` 的列定义(`*,18,2*`)与左卡 `MinWidth=300 / MaxWidth=380 / Top`
  是 Windows `LibraryPage.cs:239-266` 的几何,`C4LibraryTests.Library_folder_layout_defaults_to_two_columns` 正钉着它们;
  我第一版是"把卡片地板降到 0"(更简单),它把这条 C4 用例打红了 —— **改用"列兑现地板"之后两边都成立**(C4 用例绿 + 缺陷消失)。
- 代价与验证:右列从 409 收到 314,探针新增一条断言**右栏内容不得宽于自己的列**(实测不超),冒烟帧也逐帧看过。

### 3. 验收

**冒烟矩阵(7 页 × 明暗 + 首屏 + `--theme blue`,全部走锁;before = 我开工前自己跑的基线,与 Lead 给的数字逐位相同)**

| 帧 | before | 最终 | 结论 |
|---|---|---|---|
| 不带 `--page` / `launch` light | 37681 `2603BF6A…` | 37681 `2603BF6A…` | **逐字节不变** |
| 不带 `--page` / `launch` dark | 33579 `7D7AFDD5…` | 33579 `7D7AFDD5…` | **逐字节不变** |
| `settings` light / dark | 45594 `C19BF1C4…` / 43745 `CE33BEFE…` | 同 | **不变**(那一处 Section 没有右簇) |
| `tasks` light / dark | 27719 `0E591B87…` / 24171 `C3EE486E…` | 同 | **不变**(右簇窄,摆得开 ⇒ 不降级) |
| `account` light / dark | 33173 `D3F59339…` / 29984 `148EB206…` | 同 | **不变**(同上) |
| `instance` light / dark | 27484 `92C06E15…` / 24511 `F1582051…` | **30057 / 27468** | 变了 = 缺陷 1 修好(标题与副说明画出来了) |
| `library` light / dark | 42417 `905125C4…` / 39268 `FA926F03…` | **变**(见下) | 变了 = 缺陷 2 修好(白卡回到 18px 页边距) |
| `--theme blue` | 退出码 2 | 退出码 2 | 参数校验未受影响 |

只有**两处缺陷页**变了,其余全部逐字节相同 —— 这正是"修法只在缺陷处生效"的证据。

**原子四跑**(`artifacts\lead-verify.cmd`,构建 + 两条通道在**同一次锁**里,连跑两轮):

| 轮 | BUILD | VSTEST | INPROC | 说明 |
|---|---|---|---|---|
| 1 | **0** | 1(1 条红) | 1(同一条) | 唯一红 = `C4LibraryTests.Library_folder_layout_defaults_to_two_columns` |
| 2 | **0** | 1(同上) | 1(同上) | 逐项相同 ⇒ 确定性 |

那条红是**我第一版修法**打出来的(见上);改成"列兑现地板"之后单独复跑该用例 **0 失败**(证据在 `artifacts\c5c-c4lib.txt`)。
**本包的用例两条通道全绿**:`C5b` 33/33、`C5c` 探针 3/3,两轮都是 `[FAIL]` 计数为 **0**。

### 4. 编码(字节级实测)

`Ui.cs` 31966 B / BOM=False / CR=0 / LF=868 / U+FFFD=0;`LibraryPage.cs` 44661 / False / 0 / 1239 / 0;
`C5cLayoutProbeTests.cs` 12716 / False / 0 / 236 / 0;`C5bTasksAccountTests.cs` 42163 / False / 0 / 857 / 0。

### 5. 遗留

1. 告警基线:`xUnit2012` 的两条来自我的 `C5bTasksAccountTests.cs`,已按 Lead 要求改成 `Assert.Contains/DoesNotContain`;本轮构建只剩 `NU1900`(取漏洞源失败,本机网络环境造成,条数随构建波动:基线 6、我这次 8)+ `SYSLIB0014` 未见(该工程本轮未重建)。
2. `SectionNarrow` 的降级是"整簇换行",**没有做**"右簇内部再折行"(按钮组在极窄时会整组下移,不会各自换行)—— 668 下够用,更窄的窗口(如 500)没验。
3. 缺陷 2 的修法在**更窄**窗口(页面区 < 300+18+右列最小)下的行为没有实测:那种宽度下两栏本来就摆不开,应当像 `Section` 一样降级堆叠;本包没做(不在 Lead 点名的范围内)。

## C5a 补记(第二个交付批:抖动根治 + `FlyEnabled` 补消费方)

### 1. 第三个同族抖动的根因是**产品缺陷**,不只是测试问题

`C5aSettingsTests.The_dark_switch_changes_the_application_variant` 偶发 `Expected: Dark / Actual: Light`。
根因:`SettingsPage.ApplyDark` 原来只写 `Application.RequestedThemeVariant`,而**窗口级**变体(外壳
`MainWindow.ApplyThemeState` / 测试的 `UiHost.Show` 都会设)会盖住应用级 ⇒ `ActualThemeVariant` 不变 ⇒
`SyncAppearanceControls` 把勾选框翻回去 ⇒ 开关在真桌面(只要外壳设过窗口级变体)是**半失效**的。
修法照 Windows 的 `MainWindow.SetDark`:**应用级 + 窗口级都设**,再 `ApplyThemeVisuals()` 重刷。

**同族审计(35 条用例统一"先钉死再还原")**:`C5aFixtures.PinTheme(Window, ThemeVariant)`(两级都设 + 泵到
`PaletteOverride.IsDarkNow` 等于目标)+ `PinAppearance()`(主题色 / 背景预设 / `ExtrasDirOverride` 指到
`TestHome.Root` 之下);还原仍由 `C5aScope`(D-69)。覆盖面:应用/窗口主题变体、`Palette` 明暗与覆盖层、
`_visualColor` / `_visualBackground` / `Extras`、`Motion.Enabled` / `ClockOverride`、`AppServices.*`、`Dlg` 离屏开关、`PageRegistry`。

### 2. `FlyEnabled` 补上了消费方(C3d 漏掉的一道守卫)

见 D-145:`src\TipCL.App\MainWindow.cs` 的 `FlyToTasks` 第一道守卫由 `!Motion.Enabled` 改成 `!SettingsPage.FlyEnabled`
(`FlyEnabled == Extras.FlyAnimation && Motion.Enabled`,语义只增不减:关动画**或**关飞入都什么都不做,与 Windows 一致)。
新增用例 1 条(开着放 chip / 关掉一行不画)。

### 3. 三轮原子运行(Lead 要求的验收线:连跑三轮)

| 轮 | BUILD | VSTest | 进程内 |
| --- | --- | --- | --- |
| 1 | `BUILD_EXIT=0` | `VSTEST_EXIT=0`:**失败 0 / 通过 3438 / 总计 3438** | `INPROC_EXIT=0`:**总计 3438,通过 3438,失败 0,跳过 0,不支持 0** |
| 2 | `BUILD_EXIT=0` | `VSTEST_EXIT=0`:**失败 0 / 通过 3438 / 总计 3438** | `INPROC_EXIT=0`:**总计 3438,通过 3438,失败 0,跳过 0,不支持 0** |
| 3 | `BUILD_EXIT=0` | `VSTEST_EXIT=0`:**失败 0 / 通过 3438 / 总计 3438** | `INPROC_EXIT=0`:**总计 3438,通过 3438,失败 0,跳过 0,不支持 0** |

三处退出码全 0,两条通道总数相等(3438 = 3437 + 新增那 1 条),不支持 0。`--filter ~C5a` 另跑一次:`失败 0 / 通过 50 / 总计 50`。

### 4. 冒烟(改完之后复跑,既有帧仍逐字节不变)

不带 `--page` = **37681 / `2603BF6A...342CBD`**;`--page launch --theme dark` = **33579 / `7D7AFDD5...276893`**;
`--theme blue` = **2**;新帧 `--page settings --theme light` = **45594 / `C19BF1C4...`**、`--theme dark` = **43745 / `CE33BEFE...`**
(与 Lead 独立复跑一致)。`FlyToTasks` 在离屏 `Motion.Enabled` 恒假,不画 chip ⇒ 帧不受这次改动影响,已复跑确认。

### Lead 验收(2026-11-14 03:50)—— C4 + C5a + C5b + C5c 四包合并

结论:**四包全部通过**。以下由 Lead 在 `D:\项目\tipcl\mac\` 亲自执行。

| 检查 | 结果 |
|---|---|
| HEAD / 工作树 | `d0dd78e`,**干净**(无未提交、无未跟踪);无 remote、无 tag;Windows 仓库 HEAD 仍 `803dd38` |
| 红线(`src\TipCL.Core`、`Themes\Palette.cs`、`Palette.Light/Dark.axaml`、`NavModel/NavConfig`、`Services\UiExt.cs`、测试隔离三件、`AvaloniaSession.cs`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`TipCL.Mac.sln`) | `git diff b664f4f HEAD` 在这批路径上**为空** |
| 授权的改动(只有三处) | `Services\Ui.cs` +53/-2(C5c 门面修窄页降级)、`MainWindow.cs` +6/-1(C5a 补 `FlyEnabled` 消费方)、`Themes\PaletteOverride.cs` 新增 +250(C5a 主题层可写覆盖层)。**`Palette.cs` 一字节未动** |
| `FlyEnabled` 那处(Lead 读 diff 核对) | 就是把第一道守卫 `if (!Motion.Enabled || !IsLoaded)` 换成 `if (!SettingsPage.FlyEnabled || !IsLoaded)`,后续四道守卫顺序未动;`FlyEnabled` 内部即 `Extras.FlyAnimation && Motion.Enabled` ⇒ **语义只增不减**,是"最小改动" |
| 构建 | **0 错误**;警告 **7 = 基线**(`NU1900` x6 + `SYSLIB0014` x1;C4 代理报过的 `xUnit2012` x2 已由 C5c 修掉) |
| **原子四跑 x2**(构建 + 两条通道**在同一次锁里**;每轮 `artifacts\lead-verify.cmd`) | 轮 1:`BUILD=0` / `VSTEST=0`(失败 0 / 通过 3438 / 总计 3438)/ `INPROC=0`(3438/3438、不支持 0);轮 2:**逐项相同**。此前那次 `INPROC=1` 是 C5a 的第三条抖动,已按根因修掉 |
| **冒烟矩阵(14 帧 + blue,Lead 逐帧比对预期值)** | **0 处不符**:首屏/launch light 37681 `2603BF6A...`、launch dark 33579 `7D7AFDD5...` 逐位不变;library 43775 `8FBA4C9F...` / 40686 `A77F3E6A...`;instance 30057 `36FAEDDE...` / 27468 `2C69550D...`;settings 45594 `C19BF1C4...` / 43745 `CE33BEFE...`;tasks 27719 `0E591B87...` / 24171 `C3EE486E...`;account 33173 `D3F59339...` / 29984 `148EB206...`;`download` = 首屏同帧(未注册,符合裁决);`--theme blue` = **2**。另抽 3 帧复跑**逐字节相同** |
| `src\TipCL.Core` 对 Windows 源 SHA256 | 238 有对应 / 216 一致 / 22 有改动 / 10 无对应,与基线完全相同 |
| 全部改动文件编码(Lead 按字节核验) | **0 处异常**(无 BOM、CR=0、U+FFFD=0);入库文件里没有 `Zz*` 探针、没有 `artifacts/`、没有多余 PNG(5 个 `.png` 是 premium 加载器图标) |
| `%LOCALAPPDATA%\TipCL` / 桌面 / 图片 / 残留进程 | 9328 / 889530606 / 2026-11-10 20:42:16(**零写入**);桌面 14 项、最新 2026-11-12 19:02:53 与图片 3 项、最新 2026-11-06 都**未变**;`testhost`/`TipCL`/`java`/`adb` **0 个** |
| `%TEMP%\tipcl*` | 142(Lead 早先基线 89)。C4 代理已做对照实验:本仓库源码里出现过的名字与这些目录**对不上**(`smokecwd`/`r9-smoke`/`selftest` 等在本仓库零命中),判定为同机其它产品线在跑 `--smoke`;本包验证期间未新增 |

**四包各自的关键结论**

1. **C4(实例管理簇)**:五个文件 + 两个导航 id(`library`/`instance`,而 `InstanceDetailPage` 按 Windows 不注册 id)。它**第四次**推翻 Lead 任务书的"自包含"前提(还差 7 个 Models DTO + `GlobalModsDialog`)。**事故记录**:它的子代理是 `subagent` 起的,Lead **无法中断**,而且它提交的 `b253090` **编译不过**(探针属性名 `Loaded`/`Shown` 撞 Avalonia 的 `Control.Loaded` 事件)—— 是另一个 teammate 发现并修好的,Lead 另修了 `InstancePage.Select` 的花括号格式(1 行),一并提交 `2e9b9fb`。**"测试全绿"不等于"树能编译"这条已写进运营规则。**
2. **C5a(设置页 + 主题覆盖层)**:共 50 条用例。它把主题能力落在**新文件** `Themes\PaletteOverride.cs`(按 Lead 裁决,没塞进设置页、没动 C1a 的 `Palette.cs`),9 条机制自测钉住"后合并者胜出 / `{DynamicResource}` 当场重解析 / 清空即摘"。**它的抖动修到第三条时才露出真相**:`ApplyDark` 只写**应用级** `RequestedThemeVariant`,而窗口级变体(外壳 `ApplyThemeState` 会设)会**盖住**应用级 ⇒ `ActualThemeVariant` 不变 ⇒ 勾选框被 `SyncAppearanceControls` 翻回去 ⇒ 再调 `ApplyDark(false)`。**这是真桌面上的用户可见缺陷(「深色模式」半失效),不只是测试问题。** 修法照 Windows `SetDark`:两级都设 + `ApplyThemeVisuals()`。它顺带把同文件里 **35 条建页面的用例**统一成"先钉死再还原"。
3. **C5b(下载宿主之外的三个页面)**:`TasksPage` / `AccountPage` / `FirstRunWizard` + `tasks`/`account` 注册,33 条用例。三件诚实处置:①`SkinPng` 实测**搬不了**(整个建立在 WPF 成像 API 上),改成"保留算法与全部常量"的 Avalonia 实现(D-137);②三条框架事实(`AutomationProperties.Name` 不跟 `Content` 变、`IsVisible` 只是本地标志、`Dlg.Panel` 不是 `DialogCard`)是它第一轮跑出来**自己期望写错**后修正的;③`SyncAccountChipAsync` 按裁决不做。**`download` 按裁决挂起给 C6**(它是 `CatalogPage` 35 KB + `InstallPage` 15 KB 的分段宿主,做个占位版等于把空壳包装成完成)。
4. **C5c(两处窄页面布局缺陷)**:先量后修,而且**推翻了 Lead 给的机制猜想**。缺陷 2 不是"离屏 `ScrollViewer` 拿到水平偏移"(`Offset=(0,0) Extent=0 Viewport=0`),而是左卡 `MinWidth=300` 溢出它所在的 205 宽星列后被 Avalonia **居中**:`-48=(205-300)/2` 逐位吻合;修法是"让列兑现 300 地板"。缺陷 1 修在门面(`Ui.Section` + 新 `SectionNarrow`,阈值用两边 `DesiredSize` **自校准**+6px 回滞),摆得开走原路 ⇒ 宽页面像素零变化。第三条"库页看起来被加载过"**结论是"不是 bug"**:门闸确实没漏,那些计数是 `LibraryPage` **构造函数**里 `RenderGroups()` 画的,两边说法都成立。它还做对了一件我很看重的事:第一版修法更省事但会打红 C4 既有的几何断言,它**没有去改别人的断言**,而是换成两边同时成立的修法。

**Lead 的裁决**:接受 D-134(`InstanceDetailPage.SettingsPageFactory` 注入缝 + 如实占位,等版本管理包接上即消除);接受 `FlyEnabled` 的最小外壳改动;把"**凡读应用级全局状态的用例,必须先钉死再在还原路径里恢复**"正式列为项目测试纪律(这个病在同一包里出现三次,事后"多跑两轮"治不了根)。

**测试总数**:3179 -> **3438**(两通道相等、不支持 0)。

**仍然只有真机能验**:重启启动器、真剪贴板/文件面板、真壁纸与视频、以及 D2 清单里的全部观感项。
## C6b:设置页分段 4/5/6 的三个子页(服务器 / 游戏时长 / 反馈与帮助)+ 注入缝接线

**范围**:`Pages\ServersPage.cs`(7256 B / 237 行)、`Pages\PlaytimePage.cs`(10802 B / 312 行)、
`Pages\FeedbackPage.cs`(8399 B / 264 行)三个子页 + `Pages\C6bSegmentRegistration.cs`(接线)+
`Models\{ServerRow,FeedbackRow,HelpArticle}.cs`(**字节级搬**,222 / 270 / 182 字节)。

### 1. 依赖审计(先查后动)

| 文件 | 结论 |
|---|---|
| `ServersPage` | **可移植**。齐:`InstanceInfo` / `Ui.Section`·`Card`·`Empty`·`Glyph`/`BlockHost("servers")`/`Dlg.Confirm·Ask·PickFile·SaveFile`/`AccountGate.EnsureAsync`/`PageBase.Win?.Navigate("launch")`。缺件只有 `Models\ServerRow`(222 B)→ 字节级搬。 |
| `PlaytimePage` | **可移植**。齐:`Ui.Prog`/`Motion.CountUp`·`Progress`·`Stagger`/`Fmt.Duration`/`BlockHost("playtime")`;**零缺件**。 |
| `FeedbackPage` | **可移植**。齐:`Ui.Multi`·`Check`·`Tag`·`Section`/`Dlg.PickFiles·Busy·Alert`/`OpResult`。缺件:`Models\FeedbackRow`(270 B)+ `Models\HelpArticle`(182 B)→ 字节级搬。 |
| `JavaPage`(12.3 KB) | **本包没做**,审计结论:可移植,但需要 ①`Models\JavaInfo`(181 B,可字节搬)②一处 `Process.Start("explorer.exe", "/select,…")` 的**平台决定**(mac 没有 explorer;要么走 `open_media` 后端、要么加一个 reveal 注入缝)—— 这两件都要 Lead 点头,所以按"大件/要先裁决"停下。 |
| `MultiplayerPage`(18.8 KB) | **本包没做**,只做了粗查(它自带联机房间/端口/host 列表等一整块);没有细读,不敢报"可移植"。 |

### 2. 接线(不改别人的文件)

C5a 已经在 `SettingsPage` 留好六个注入缝(未接时显示如实的占位说明)。本包用**自己的** `[ModuleInitializer]`
(`C6bSegmentRegistration`)把**三个**接上:

```csharp
SettingsPage.ServersPageFactory  = () => new ServersPage();
SettingsPage.PlaytimePageFactory = () => new PlaytimePage();
SettingsPage.FeedbackPageFactory = () => new FeedbackPage();
```

- **为什么用 ModuleInitializer**:真桌面启动、`--smoke`、测试里的 `new SettingsPage()` 都自动看得到,
  而且一个字节都不用改 `SettingsPage.cs` / 外壳(红线);
- **`FeedbackPage` 的懒构造**:Windows 是**字段初始化**(`new FeedbackPage()` 跟设置页构造函数一起建),
  Lead 在 C5a 裁决里点名要改成"切到第 6 段时才建" —— 交出去的是**工厂**,设置页按需调;
  有一条用例专门钉这件事(换一个计数工厂 → `new SettingsPage()` 之后计数必须还是 0);
- 另外三处缝**不归本包**:`AccountPageFactory`(C5b 的页面)、`JavaPageFactory`(本包没搬 `JavaPage`)、
  向导缝(C5b 的向导)。

### 3. 验收

**构建**:`dotnet build TipCL.Mac.sln -c Release -p:UseSharedCompilation=false -m:1` → **0 个错误**(锁下)。

**我的用例(两条通道)**:`--filter FullyQualifiedName~C6bPages` → `已通过! - 失败: 0,通过: 22,总计: 22`(退出码 0);
全量**进程内执行器** `总计 3503 个用例,通过 3499,失败 4,跳过 0,不支持 0` —— 其中 **C6b 22/22 全绿、[FAIL] 0**,
那 4 条全在 c4-pages 名下(`C6aPagesTests` ×3 + `C5aSettingsTests.Missing_segments_show_the_honest_placeholder` ×1),
它的 C6a 测试当时还在调(它自己也就此发过消息)。

**冒烟矩阵(8 个 id × 明暗,全部走锁;对照 = 我开工前自跑的 C5c 终稿基线)**

| 帧 | 结果 |
|---|---|
| 首屏/`launch` light/dark | **逐字节不变** 37681 `2603BF6A…` / 33579 `7D7AFDD5…` |
| `library` light/dark | **不变** 43775 `8FBA4C9F…` / 40686 `A77F3E6A…` |
| `instance` light/dark | **不变** 30057 `36FAEDDE…` / 27468 `2C69550D…` |
| `settings` light/dark | **不变** 45594 `C19BF1C4…` / 43745 `CE33BEFE…` |
| `tasks` light/dark | **不变** 27719 `0E591B87…` / 24171 `C3EE486E…` |
| `account` light/dark | **不变** 33173 `D3F59339…` / 29984 `148EB206…` |
| `multiplayer` light/dark | **新帧 = 与首屏逐字节相同**(37681 `2603BF6A…` / 33579 `7D7AFDD5…`) |

两条要说明的:
1. **`settings` 那一帧没有变化**,不是"接线没生效":`--smoke` 里 `BridgeReady` 为假 ⇒ 页面的加载钩子不跑
   (C5c 已查清的口径),而设置页的分段内容是**切段时才建**的,所以帧里只有构造函数那部分。
   接线本身由用例钉住(工厂造出真页面 + 懒构造),冒烟帧因此合理地**不变**。
2. **`multiplayer` 帧与首屏逐字节相同**:`MultiplayerPage` 本包没搬,`multiplayer` 这个 id 没有注册工厂
   ⇒ `Navigate` 静默不动(与 C3c 记的"其它 id 什么都不切"、以及 C5b 的 `--page download` 同一现象)。

### 4. 落地时实测到的三条(都修的是**测试期望**,页面代码没动)

1. **`UiExt.Fill` 会自动选中第一项**(与 Windows 的 `Fill` 同)⇒ `ServersPage` 首屏就会拉一次 `list_servers`,
   "没选实例就不拉列表"那个状态**在 mac 上不可达**;用例改成钉"填了实例 ⇒ 拉过列表"。
2. **`PlaytimePage` 行里的时长是同步读缓存的**(`Cached`),而缓存由 `WarmAsync` 异步预热 ⇒ 首屏画的是本地
   `Fmt.Duration`,后端 `format_playtime` 的文案在**下一次重画**时才上屏(Windows 同序)—— 用例改成先 `RefreshAsync` 再断言。
3. **`Dlg.Busy` 包着 `await Dlg.Alert`**:提示卡没关之前 Busy 那层不收,所以系统信息那一步的浮层数是 **2** 而不是 1。