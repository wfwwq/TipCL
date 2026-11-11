# TipCL macOS 版 — 进度

> 仓库根 = 本文件所在目录 `D:\项目\tipcl\mac\`。开工前先读 [GOAL.md](GOAL.md),拿不准的先查 [DECISIONS.md](DECISIONS.md)。
> 每个工作包做完在**末尾追加**一小节:做了什么 / 关键文件 / 验证命令与实际结果 / 遗留与风险。

## 路线图状态

| 包 | 内容 | 状态 |
|---|---|---|
| A1 | 脚手架 | ✅ 完成(见下) |
| A2 | Core 基座 | ✅ 完成(见下) |
| A3 | Core/Launcher 主体 | ✅ 完成(见下) |
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


