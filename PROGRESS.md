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
| B2b | Natives classifier / 规则 / `-XstartOnFirstThread` / 启动参数 | 未开始 |
| B2c | NodeRuntime darwin / Terracotta 权限 / adb 路径 / PhoneLink 临时目录 / Preflight 磁盘 / Updater 仅提示 / Shortcut / IsSymlink | 未开始 |
| B3 | 打包 | 未开始 |
| C1-C8 | 界面 | 未开始 |
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
