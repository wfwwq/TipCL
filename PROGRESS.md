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
| C1b | `Themes/Controls.axaml` 移植 | 未开始 |
| C1c | `Services/Ui.cs` / `UiExt.cs` 门面 | 未开始 |
| C2-C8 | 外壳 + 页面 | 未开始 |
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
