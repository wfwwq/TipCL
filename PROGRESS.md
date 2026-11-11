# TipCL macOS 版 — 进度

> 仓库根 = 本文件所在目录 `D:\项目\tipcl\mac\`。开工前先读 [GOAL.md](GOAL.md),拿不准的先查 [DECISIONS.md](DECISIONS.md)。
> 每个工作包做完在**末尾追加**一小节:做了什么 / 关键文件 / 验证命令与实际结果 / 遗留与风险。

## 路线图状态

| 包 | 内容 | 状态 |
|---|---|---|
| A1 | 脚手架 | ✅ 完成(见下) |
| A2 | Core 基座 | ✅ 完成(见下) |
| A3 | Core/Launcher 主体 | 未开始 |
| A4 | Ai + Sync | 未开始 |
| A5 | Plugins + BackendApi + Bridge | 未开始 |
| A6 | 测试移植 | 未开始 |
| B1-B3 | mac 平台层 / 逻辑核对 / 打包 | 未开始 |
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

