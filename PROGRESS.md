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

- 共 29 个 `.cs`(84 KB)加一个 `fixtures\webp-sample.webp`。工程引用的是 net48 的 `TipCL.exe`,另有包 `GitHubActionsTestLogger`(mac 不需要)。
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
