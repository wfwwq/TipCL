# TipCL macOS 版 — 决策记录

> 记录「按 GOAL.md 裁决不下来 / 本机环境逼出来」的选择,方便后面的包不重复踩坑。
> 只追加,不删改历史结论;推翻旧结论时新开一条并注明作废了哪条。

## D-01 Avalonia 版本用 11.3.2(不是线上最新的 11.3.23)

- **背景**:本机进程级网络被挡(ssl 认证失败),`api.nuget.org` / `mirrors.huaweicloud.com` 都取不到服务索引,
  `dotnet add package` 报 NU1301;**NuGet 本地缓存里只有 Avalonia 11.3.2**。
  线上最新 11.x 稳定版是 11.3.23(已用 flatcontainer 索引核对过,12.x 也有,但按 GOAL 不用 12)。
- **决定**:三个工程统一定 `TipCLAvaloniaVersion = 11.3.2`(在 `Directory.Build.props`),
  含 Avalonia / Avalonia.Desktop / Avalonia.Skia / Avalonia.Themes.Fluent / Avalonia.Headless。
- **遗留**:等机器能联网时把版本号抬到最新的 11.x 稳定版,重新跑一次收尾验证。

## D-02 用 SDK 10.0.403 构建,但 sln 必须是经典 .sln

- 本机装了 8.0.427 与 10.0.403,`dotnet` 默认走 10.0.403。
- SDK 10 的 `dotnet new sln` 默认产出 **.slnx**,而收尾命令点名 `TipCL.Mac.sln`,
  所以建的时候显式加了 `--format sln`。
- **没有**加 `global.json` 去钉 SDK 版本:那会改变整个仓库的 SDK 行为,超出 A1 需要;
  net8.0 用 SDK 10 构建目前正常(见 PROGRESS.md 的验证结果)。

## D-03 NoWarn 本包留空

按任务书,A2 对照 Windows csproj 补 NoWarn 之前,这里不预置任何 `NoWarn`。

## D-04 A1 空窗口自带一对占位色,让明暗两帧确实不同

- GOAL 要求「`--theme` 映射到 `RequestedThemeVariant`,两种主题截出的图必须确实不同」,
  但真正的 `Palette.Light/Dark` 属于 C1。
- A1 的窗口底板用一对写死的占位色:`Light = #F7F7F8 / #1A1A1E`,`Dark = #1B1B1F / #F2F2F4`,
  由 `RequestedThemeVariant` 决定;同时应用里挂了 FluentTheme。
- **C1 建好真主题后删掉这对占位色**,改用 Palette 资源。

## D-05 `dotnet test` 在本机 DSH 沙箱里跑不了,补了进程内执行器

- **现象**:`dotnet test TipCL.Mac.sln -c Release --no-build` →
  testhost 未处理异常 `System.ComponentModel.Win32Exception (5): 拒绝访问`
  (栈顶 `Microsoft.VisualStudio.TestPlatform.TestHost.DefaultEngineInvoker.SetParentProcessExitCallback`),
  「Testhost 进程已退出」→ `MSB4181 VSTestTask 返回 false` → 退出码 1。
- **根因(已独立验证)**:本沙箱把进程跑在 **Low 完整性级别**(`whoami /groups` → `S-1-16-4096`),
  且**禁止跨进程拿句柄**:子进程对父进程、对兄弟进程执行 `EnableRaisingEvents` 一律 `Access is denied`
  (用临时探针脚本验证:`child-probe.ps1` 里对目标进程设 `EnableRaisingEvents`;
  先试父进程、再试兄弟进程,两次都 `Access is denied`。探针放 `artifacts/` 下,不入库)。
  VSTest 的 testhost 启动时无条件做这件事,所以它在本沙箱里**结构上不可用**,与工程配置无关。
- **决定**:`tests/TipCL.Tests` 改成 `OutputType=Exe`(加 `GenerateProgramFile=false` 避免和
  Microsoft.NET.Test.Sdk 自动生成的入口点冲突),加 `Program.cs` 进程内执行器。
  它直接在本进程反射执行 `[Fact]` / `[Theory]`(+`InlineData`/`MemberData`/`ClassData`)、支持 Skip 与 async,
  断言还是 xunit 自己的 `Assert`。**不静默跳过**:解析不出来的用例记 `UNSUPPORTED` 并让退出码非 0。
- **注意**:这只是本沙箱的替代验证通道,**不是**「`dotnet test` 已通过」。在正常机器上 `dotnet test` 照旧可用
  (`OutputType` 不影响 VSTest 加载测试程序集)。
- 命令:`dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll`。

## D-06 `dotnet build-server shutdown` 非 0 退出不算构建失败

本机没有在跑的 VBCSCompiler 时,这条命令返回 1(输出「未能关闭 VB/C# 编译器服务器」)。
它只是构建防锁的预备动作,所以 `tools/build.ps1` 改成打 warning 后继续。

## D-07 界面全部用 C# 代码构建,不用 axaml

按 GOAL「code-behind 风格」,A1 不引入任何 `.axaml`;FluentTheme 用
`Styles.Add(new FluentTheme())` 在代码里挂。C1 引入主题/控件 axaml 时再改。

## D-08 本机 PowerShell 是 5.1,脚本按 5.1 写

- 本机 `pwsh` 不存在,`$PSVersionTable.PSVersion` = **5.1.19041.6456**(Windows PowerShell)。
- 执行策略:`CurrentUser = RemoteSigned`,但 DSH 写出来的 `.ps1` 直接 `-File` 跑会被拒
  (「is not digitally signed」),要 `-ExecutionPolicy Bypass`。
- 因此 `tools/build.ps1` **只用 ASCII 输出**(避免无 BOM 的 UTF-8 被 5.1 读成乱码),语法也只用 5.1 支持的。

## D-09 跑 `TipCL.exe` 必须用会等待的方式,否则拿不到退出码

`TipCL.exe` 是 `WinExe`(GUI 子系统)。在 Windows PowerShell 里用 `& .\TipCL.exe ...` 调它**不会等待**,
`$LASTEXITCODE` 是空的。收尾验证用 `cmd /c "..."`(cmd 会等 GUI 进程)拿到真实退出码。
`Start-Process -NoNewWindow` 在本沙箱直接 `Access is denied`,没走通。

## D-10 Avalonia 抬到 11.3.23(**取代** D-01 的版本结论)

- **背景**:A2 开工时 `https://api.nuget.org/v3-flatcontainer/avalonia/index.json` 在本次运行环境里能通
  (HTTP 200),索引里最高的 11.x 稳定版(版本号不含 `-` 后缀)是 **11.3.23**。
- **决定**:`Directory.Build.props` 的 `TipCLAvaloniaVersion` 从 11.3.2 抬到 **11.3.23**,
  `dotnet restore TipCL.Mac.sln` 退出码 0(实测拉到了线上包)。
- **作废**:D-01 里「只能用本地缓存 11.3.2」与它「等能联网时再升」的遗留项到此关闭。
  仍然坚持 GOAL 的「不用 12.x」。

## D-11 A2 暂时排除编译的 6 个文件(裁决 A)

- A2 只做 Core 基座。下面 6 个文件已**按 Windows 版字节级复制并入库**(内容与源文件逐字节一致),
  但在 `src/TipCL.Core/TipCL.Core.csproj` 里 `<Compile Remove>` 掉,等对应包恢复:

  | 文件 | 缺什么 | 恢复包 |
  |---|---|---|
  | `BackendApi.cs` | Launcher 的 `Config/LauncherConfig`、`AccountManager`、`Instance`、`DownloadManager`、`AgentResult`、`Utils` | A5(后端主体) |
  | `BackendWorker.cs` | 后端整体;本身只有一个用不到的 `using TipCL.Core.Launcher;` | A5 |
  | `BridgeServer.cs` | `Launcher.Utils.DecodeUtf8Strict` | A3(主体接线在 A5) |
  | `CoreResources.cs` | `Launcher.Utils.DecodeUtf8Strict` | A3 |
  | `PPath.cs` | `Launcher.Utils.ROOT` | A3 |
  | `TaskContext.cs` | 后端类型 `BackendWorker`(字段 / 构造 / `Current`) | A5 |

- **为什么不写桩类型**:任务书红线禁止空命名空间 / 空实现硬凑。
- **为什么 `BackendWorker.cs` 也算「依赖」**:它只差一个不用的 `using`,删掉这一行就能编译,但那是
  改被复制文件(不是跨平台缺口),A5 会把它连 BackendApi 一起接上,所以选**一处不改**的最保守做法。
- **影响**:`CoreResources` 读资源的逻辑本包进不了编译,嵌入资源清单改用 `typeof(PyJson).Assembly` 断言
  (A3 恢复 `CoreResources` 后可以补一条走 `CoreResources.ReadText` 的用例)。

## D-12 两个本机踩到的工具陷阱(A2 记录,后续包直接用)

1. **别用 `Assert.DoesNotContain(string, string)` 查 `U+FEFF`**:xunit 走 `CurrentCulture`,
   而 U+FEFF 是**可忽略字符**,任何字符串都会被判成「在 0 处找到了」。
   要查 BOM 就用 char 比较(序数),例如 `Assert.NotEqual('\uFEFF', text[0])` /
   `text.IndexOf('\uFEFF') < 0`。
2. **本机 `powershell -File x.ps1` 读不到非 ASCII 路径**:Windows PowerShell 5.1 把无 BOM 的
   UTF-8 脚本按 ANSI 读,脚本里的中文路径会变成乱码(实测 `D:\项目\...` → `D:\椤圭洰\...`)。
   `artifacts/` 下的一次性脚本要写成 **UTF-8 with BOM**(D-08 只说了输出用 ASCII,这次补上输入侧)。

## D-13 A3 只提前搬 `Ai\Defaults.cs`;`Ai\Conflict.cs` 及其依赖方等 A4/A5

- **背景**:A3 要恢复 A2 排除的文件,并允许提前搬 `Ai\Defaults.cs` + `Ai\Conflict.cs`(每个文件最多再往前搬 3 个 Ai 文件)。
- **实测**:
  - `Ai\Defaults.cs`(97 行)只用 `Py.*` + `Encoding`,**零** Ai 依赖 → 提前搬,LauncherConfig.cs / VersionSettings.cs 因此可编译。
  - `Ai\Conflict.cs`(420 行)依赖 `Ai\TomlLite`(48 行)与 `Ai\Agent.DGet`(385 行)。`TomlLite.cs` 自足(1 个 Ai 依赖),
    但 **`Agent.cs`(64 KB / 1759 行)牵出 37 个 Ai 文件**(Client/Mcp/Store/Tools/Memory/Prompt/Trace... ),
    远超「再搬 3 个」的上限 → **停手**,按任务书的回退方案处理:不搬 `Conflict.cs`,改为排除它的两个**调用方**
    `Launcher\ModUpdate.cs` 与 `Launcher\Preflight.cs`(全仓库只有 BackendApi.cs(已排除)和它们自己引用这两个类)。
- **A4 注意**:`src/TipCL.Core/Launcher/Ai/` 里**已经存在 `Defaults.cs`**(与 Windows 源逐字节一致),
  整目录复制时别漏了这一层、也要知道它不是 A4 新搬的。
- **结论**:A3 排除了 7 个文件 —— `BackendApi.cs`(A5)、`BridgeServer.cs`(A5)、`Launcher\SkinServer.cs`(A5)、
  `Launcher\McLauncher.cs`(A5)、`Launcher\GameProcess.cs`(A5)、`Launcher\ModUpdate.cs`(A4)、`Launcher\Preflight.cs`(A4)。

## D-14 `BridgeServer.cs` 在 A3 恢复失败,它的硬依赖链一并等 A5

- **背景**:A2 的 PROGRESS 写「`BridgeServer.cs` 依赖 `Utils.DecodeUtf8Strict`,A3 恢复」。A3 实测这只说对了一半:
  Utils 那一侧确实满足了,但 `BridgeServer.cs` 还有一个**属于 A5 的**依赖 —— 第 88 行
  `public BackendApi Api { get; }` 与第 102 行构造函数第 1 个参数 `BackendApi api`(类型 `BackendApi`,A2 起一直排除到 A5)。
- **连带链实测**(每步都是编译错误实测出来的,不是猜的):
  `BridgeServer.cs` →(缺 `BackendApi`,2 个 CS0246)→ `Launcher\SkinServer.cs`(7 个 CS0103,调 `BridgeServer.Unquote/SplitTarget/Phrase/ReadLine`)
  → `Launcher\McLauncher.cs`(1 个 CS0103,`SkinServer.ApiFor`)→ `Launcher\GameProcess.cs`(调 `McLauncher._set_priority`/`WatchWindowTitle`)
  → 到此为止(`GameProcess` 只被已排除的 `BackendApi.cs` 引用,`I18nCore.cs`/`Terracotta.cs` 里出现的 "mclauncher" 是路径字符串不是类型引用)。
- **决定**:不为此写桩类型、不改被复制文件的逻辑(红线),这 4 个文件保持 `Compile Remove`,等 A5 与 `BackendApi` 一起恢复。
  这与 GOAL §7 把 `BackendApi + RpcDispatcher + BridgeServer` 划给 A5 也是一致的。
- **影响**:A3 实际参与编译的 Launcher 顶层文件是 108 − 5 = **103 个**(另加 `Ai\Defaults.cs`),
  `BridgeServer` 侧的「主体接线」本来就属于 A5,所以 A3 的覆盖面损失只在这 4 个文件。

## D-15 本机 `dotnet test` 在本次运行环境里可用(A1/D-05 的沙箱限制这次没复现)

- A3 收尾时真跑 `dotnet test TipCL.Mac.sln -c Release --no-build`:**退出码 0,131 个用例全过,耗时 1.1 秒**,
  没有出现 D-05 记录的 `Win32Exception (5)`。进程内执行器仍保留:
  `dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll` 也跑了一次,**131/131 全过**(退出码 0)。
- 结论:两条通道本包都验过;D-05 的现象是**运行环境相关**的,不推翻它,只是本次没撞上。

## D-16 测试数据目录隔离用 `[ModuleInitializer]` + `Utils.SetRoot`

- 红线要求「不许碰用户真实的 `%LOCALAPPDATA%\TipCL`」。A3 在 `tests/TipCL.Tests/TestHomeIsolation.cs` 里用
  `[ModuleInitializer]` 做隔离:创建 `AppContext.BaseDirectory\test-home\{Guid}`、设 `TIPCL_HOME`、调 `Utils.SetRoot(同一路径)`。
  `[ModuleInitializer]` 在本 module 的**任何**代码之前运行,所以进程内执行器 `Program.Main`(同程序集)
  与 VSTest 的 testhost 都会先触发它,任何用例都不可能先读到真实 `ROOT`。
- **收尾实测**:工作前后 `%LOCALAPPDATA%\TipCL` 的 `LastWriteTime`(= 2026-11-09T22:01:49.9009329+08:00)与顶层条目数(19)完全一致。
- 顺带一条给后续包的坑:**`Resources\locales\*.json` 的键用的是全角冒号 `：`(U+FF1A)**,
  写测试时手抄这些键很容易差一个字符(A3 就在这里栽过一次)。`tests` 里改成 `\uXXXX` 转义。

## D-17 A3 没有引任何新 NuGet 包,`NoWarn` 保持 6 项

- Launcher 主体在 `net8.0` 上**一处改动都不用**(109 个复制文件 SHA256 全等,改动数 0),所以没有跨平台缺口要修。
- `System.Text.Encoding.CodePages` 是 net8.0 自带的(`CodePagesEncodingProvider`),A3 没引包;
  但**代码页相关的降级行为差异**记在 PROGRESS.md 的 A3「运行时差异清点」里,留给 B1/B2 处理。
- 新增的 3 条 `CA1416` 警告(Sysinfo.cs 的 Registry)是分析器不认识自定义的 `Utils.IS_WINDOWS` 所致,
  运行时有守卫;**不靠扩大 NoWarn 压掉**。

## D-18 A4 只编译 53/65 个 Ai 文件,用「全纳入 → 按实测错误排除」的不动点做

- **背景**:任务书把 A4 的目标定为「编译不依赖后端的最大子集」。A4 之前的事实是
  `BackendApi` / `Ai` / `Plugins` 三者循环依赖,而 `BackendApi`+`Plugins` 要到 A5 才有。
- **做法**:第 1 步把 `Ai\*.cs` 全部 65 个与 `Sync\*.cs` 全部 4 个**字节级复制**进来;
  第 2 步先全部参与编译,再按「找不到类型/命名空间」(CS0246 / CS0103 / CS0234)
  的**实测**错误一轮一轮排除,直到这类错误归零(共 4 轮构建:26 → 6 → 11 → 0 条)。
  每轮的错误行号都记进了 `TipCL.Core.csproj` 的注释。
- **最终排除 12 个 Ai 文件**(`Sync` 一个都没排除),分两轮/两类:

  | 轮次 | 文件 | 实测缺什么 |
  |---|---|---|
  | 1(直接缺后端) | `Ai\Tools.cs` | `BackendApi` ×10(371/381/392/554/600/658/703/805/814/1067 行) |
  | 1 | `Ai\ExtraTools.cs` | `BackendApi` ×7(105/269/398/426/448/607/784) |
  | 1 | `Ai\Agent.cs` | 第 7 行 `using TipCL.Core.Plugins`;`BackendApi` ×4(345/466/535/1731) |
  | 1 | `Ai\Preview.cs` | `BackendApi` ×2(25/86) |
  | 1 | `Ai\McpServer.cs` | `BackendApi` ×2(32/79) |
  | 2(连带) | `Ai\Conflict.cs` | `Agent` ×3(385/410/418) |
  | 2 | `Ai\Tokens.cs` | `Agent` ×2(41/59) |
  | 2 | `Ai\ToolMeta.cs` | `ExtraTools` ×1(52) |
  | 3(连带) | `Ai\Compact.cs` | `Tokens` ×6(65/83/145/148/194/239) |
  | 3 | `Ai\Usage.cs` | `Tokens` ×2(33/34) |
  | 3 | `Ai\Scheduler.cs` | `ToolMeta` ×2(20/30) |
  | 3 | `Ai\Hooks.cs` | `ToolMeta` ×1(129) |

- **`ModUpdate.cs` / `Preflight.cs` 的恢复包从 A4 改成 A5**(推翻了 A3 的 PROGRESS 表):
  A4 实测 `Ai\Conflict.cs` 进不了编译(缺 `Agent`),所以这两个文件仍然恢复不了。
  任务书给的回退方案就是「改注释」,已照做;**不为此写桩类型**。
- **A4 一共排除了 19 个文件**:A2/A3 遗留的 7 个(`BackendApi.cs`、`BridgeServer.cs`、
  `Launcher\SkinServer.cs`、`Launcher\McLauncher.cs`、`Launcher\GameProcess.cs`、
  `Launcher\ModUpdate.cs`、`Launcher\Preflight.cs`)+ 本包的 12 个 Ai 文件。
  `Sync` 的 4 个(`LevelDat` / `PhoneClient` / `PhoneLink` / `SaveSync`)全部参与编译。
- **没有改被复制文件的任何一处**(69 个文件 SHA256 全等,`changed_count=0`),
  本包唯一动过的工程文件是 `TipCL.Core.csproj`(它不是复制来的)。

## D-19 给 `TipCL.Tests` 开 `InternalsVisibleTo`,好给 internal 的 `TomlLite` 写用例

- **背景**:`Ai\TomlLite.cs` 是 `internal sealed class`。A4 之前全仓库只有
  `Ai\Conflict.cs` 用它,而 `Conflict.cs` 因为缺 A5 的 `Agent` 被排除 → 没有任何公开路径能碰到它。
  任务书把「`Ai\TomlLite`(若参与编译)」列为优先测试对象,它确实参与了编译。
- **决定**:`src\TipCL.Core\TipCL.Core.csproj` 加 `<InternalsVisibleTo Include="TipCL.Tests" />`,
  测试直接 `TomlLite.Parse(...)`。
- **为什么不用反射**:反射测内部类型要么绕不开 `BindingFlags.NonPublic`、
  要么把断言写成字符串化的 MethodInfo,出错时信息更差;`InternalsVisibleTo` 是 SDK 原生支持的一行。
  这**不**影响任何被复制的源码,也不改变运行行为。
- **仍然不用反射**:`AiSyncHomeIsolationTests.cs` 对私有路径属性(`VisionCaps.PrefPath`、
  `Permission._store_path`、`Sync\PhoneLink.StatePath`)一律走「调一次会落盘的公开方法 / 公开读取端」
  来证明目录位置,不写反射。

## D-20 A4 复核:直接调 `Utils.ResolveRoot()` 的地方是 5 处,不是任务书里写的 4 处

- 任务书列的 4 处(`Ai\FileMemory.cs:23`、`Ai\McpServer.cs:58`、`Ai\VisionCaps.cs:37`、`Sync\PhoneLink.cs:58`)
  逐行核对**全部属实**;但 `Sync\PhoneClient.cs:376` 还有一处
  `Path.Combine(Utils.ResolveRoot(), "adb.exe")`,属于同一形状(每次调用都重新解析,不吃 `Utils.ROOT` 缓存)。
- **影响**:A4 的红线处置不变(`AssemblyInfo.cs` 的 `DisableTestParallelization` 与
  `TestHomeIsolation.cs` 的 `[ModuleInitializer]` 一个字都没动,新用例也不改写 `TIPCL_HOME`)。
  A5/B2 改动这几个点时按 **5 处**来数。
- 另外这两个目录里**走缓存 `Utils.ROOT`** 的落盘位置一共 12 处(Artifacts 24、Checkpoint 26、
  Hooks 122、Permission 321、Rewind 15、Store 29/400、Trace 20 等),它们本来就吃 `Utils.SetRoot`,
  隔离由 A3 的机制覆盖。

## D-21 A4 踩到的两个新工具坑(与 D-12 同类)

1. **`write` 工具写出来的 `.ps1` 没有 BOM**:Windows PowerShell 5.1 会按 ANSI 读,
   脚本里的中文路径再次变成乱码(D-12 已经记过这件事,这次是「自己生成的文件」也中招)。
   一次性脚本落盘后要再 `[IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding $true))` 补 BOM。
2. **`Set-Location` 对 `[IO.File]::*` 静态调用无效**:同一段脚本里先 `Set-Location` 再按相对路径调
   `[IO.File]::ReadAllText`,解析基目录仍是 DSH 的进程工作目录,结果把 4 个**空文件**写到了仓库根。
   已按字节数为 0 + 目录校验后删除,提交前 `git status` 复核干净。**改文件一律用 edit 工具,别用脚本批量替换。**

## D-22 A5 引 NuGet 包 `System.Security.Cryptography.ProtectedData` 8.0.0(照 Lead 裁决执行)

- **背景**:`Plugins\SecretStore.cs` 第 166/172 行用 `ProtectedData.Protect/Unprotect`(Windows DPAPI)。
  这个 API 在 net8.0 **类库**(Microsoft.NETCore.App)里不存在,它属于 Windows Desktop 共享框架,
  所以不引包无法原样编译。
- **决定**:`src\TipCL.Core\TipCL.Core.csproj` 加
  `<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />`。
  版本核对:`api.nuget.org` flatcontainer 索引里 8.0.x 的稳定版只有 **8.0.0**(没有 8.0.1+)。
- **范围**:只为了让 `SecretStore.cs` **原样编译**,一处被复制的逻辑都没改;
  非 Windows 分支(AES-GCM + Keychain)按 GOAL 留给 B1。
- **副作用**:新增 4 条 `CA1416`(Protect/Unprotect/DataProtectionScope「仅在 windows 上受支持」)。
  分析器不认识 `SecretStore.IsWindows` 这个手写守卫,运行时有守卫;
  **不靠扩大 NoWarn 压掉**(与 D-17 同例),`NoWarn` 仍是原来的 6 项。

## D-23 A5 的测试不实例化 `BackendApi` / `BridgeServer` / `PluginManager`,也不起监听

- **红线要求**:在测试里 `new BackendApi(...)` / `new PluginManager(...)` / 启 `BridgeServer`、`McpServer`
  之前,先读它的构造函数/静态构造函数/`Start`,确认不联网、不起进程、不读写 `TestHome.Root` 之外。**只要有一项不确定就不实例化。**
- **实测结论(读了源码,不是猜的)**:

  | 类型 | 构造点 | 实测 |
  |---|---|---|
  | `BackendApi` | 实例构造 `BackendApi.cs:1738-1749` | **联网**:末尾 `ThreadPool.QueueUserWorkItem` → `Thread.Sleep(1500)` → `AuthHttp.Warmup("https://login.live.com/")`(系统代理探测 + DNS + TLS)。另 `InitAccounts`/`EnsureDefaultInstance` 会在 `Utils.ROOT` 下真建实例目录 |
  | `BackendApi` | 静态构造 `BackendApi.cs:2656-2679` | 纯的:只填常量数组 / `HashSet` / 两个 `Regex` / 一个委托,无 IO、无网络、无进程 |
  | `BridgeServer` | 构造 `BridgeServer.cs:102-119` | 第 118 行 `Dispatcher = new RpcDispatcher(api)`;而 `RpcDispatcher` 构造第 48 行立刻 `target.GetType()` → **传 null 会 NullReferenceException**。要构造就必须先有 `BackendApi` → 一并被否 |
  | `BridgeServer.Start` | `BridgeServer.cs:203-215` | 只是 `TcpListener(IPAddress.Loopback, port)` + `Start` + 读回端口,本身安全;但进不到这一步 |
  | `McpServer.SetEnabled` | `McpServer.cs:86-98` | 写 `mcp_server.json`(在 `Utils.ResolveRoot()`,即测试隔离目录)+ 调 `Stop()`(只是把 `_listener` 置空,**不开监听**)→ **安全,可以用** |
  | `McpServer.Start` | `McpServer.cs:100-120` | 真开 `TcpListener`,本包不用 |

- **决定**:本包不写实例化 `BackendApi` / `PluginManager` / `BridgeServer` 的用例,也不调 `McpServer.Start`。
  - `RpcDispatcher` 用测试文件内自定义的小目标类测(它有 30+ 条用例,不需要后端);
  - `BridgeServer` 只测静态纯函数(`Unquote`/`SplitTarget`/`Phrase`/`ReadLine`/`NormalizeOrigin`/`TokenUrlSafe`/`FixedTimeEquals`);
  - `McpServer` 只用 `SetEnabled(false)` 做落盘隔离守卫(A4 欠的那条);
  - 「用例没在隔离目录之外落盘」这条由收尾的 `%LOCALAPPDATA%\TipCL` 前后元信息对比兜底(实测三项全等)。
- **顺带记一条给 C 阶段的事实**:`BackendApi` 构造函数会**无条件**往 `login.live.com` 发预热请求,
  所以 mac 版的 `BridgeHost` 要先把这行做成可关(否则每次启动都联网)。

## D-24 全角标点又踩一次:测试里的中文文案一律先用 `\uXXXX` 转义再写

- **现象**:A5 新增的用例第一次跑挂 14 条,其中 12 条 xunit 报「Expected 与 Actual 不同」但**打印出来完全一样**。
  真因是 **D-16 那个全角标点问题**,这次不在 `locales` 里,而在 C# 源码的报错文案里:
  `RpcDispatcher.cs:57`(U+FF1A)、`RpcDispatcher.cs:384`(U+FF1A)、`PluginManifest.cs:147/180/323/343/364`(U+FF1A)、
  `SemVer.cs:89`(U+FF1A)、`PluginDirs.cs:80`(U+FF1A)、`Compact.cs:9` 的 placeholder(U+FF1A + U+FF0C)、
  `PluginManifest.cs:287`(U+FF0C)等。
- **教训**:**用 read 工具看源码时全角 ':' 与半角 ':' 长得一模一样**(控制台里还会变成乱码),不能靠肉眼抄。
- **做法**:先用一次性脚本(`artifacts/`,不入库)把目标行的非 ASCII 字符转成 `\uXXXX` 打印出来,
  再把这些**转义**写进测试;或者改成断言「不含标点的子串」(`Assert.Contains("main \u6216 client", ...)`)。
- **另外 2 条是我自己的期望写错**(不是移植差异,已按实测行为改正):
  1. `SemVer.IsValidRange("1.2.3 | 2.0.0")` 实际返回 **true** —— 第一个 token 就先比较失败了,`"|"` 那一段根本走不到解析;
     (`SemVer.Satisfies("1.2.3", "1.2.3 | 2.0.0")` 仍然返回 false,因为 `Satisfies` 会走到并捕获 `FormatException`。)
  2. `Conflict.InspectJar` 返回的 `id`/`name`/`version` 来自**清单里的字段**,不是文件名;
     文件名只在「zip 里没有任何 mod 元数据」时才当兜底。

## D-25 `TestHome` 合并:静态隔离一字未改,实例成员只改临时目录的落点

- **背景**:Windows 的 `TestHome`(`public sealed class TestHome : IDisposable`,构造时在 `%TEMP%` 建目录 +
  改 `TIPCL_HOME`,`Dispose` 恢复并删目录)与 mac 这边 A3 起的 `internal static class TestHome`(静态 `Root` +
  `[ModuleInitializer]`)重名,是全量测试里唯一的类名冲突。
- **做法**(Lead 授权的唯一改法):`TestHomeIsolation.cs` 里把它换成
  `public sealed class TestHome : IDisposable`,静态 `Root` 与 `Init()` 行为一字不变,
  新增无参构造 / `Dir` / `Dispose`,与 Windows 版**只有一处不同**:目录建在 `Path.Combine(Root, "t-" + Guid)` 下。
  `AssemblyInfo.cs` 的 `[assembly: CollectionBehavior]` 未动(Windows 那份的重复声明不搬)。
- **为什么实例构造不调 `Utils.SetRoot`**:那会引入与 Windows 版的第二处差异,还会把 `Utils.ROOT`
  按用例切来切去。现在的语义是「`TIPCL_HOME` 按用例隔离、`Utils.ROOT` 仍按进程隔离」,
  与 Windows 版完全一致(Windows 的 `TestHome` 也只改环境变量)。
- **实测副作用**(记给后面的包):一个小类里多条用例 `new TestHome()` 时,走缓存 `Utils.ROOT` 的落盘位置
  (如 `Ai\Store` 的 `ai_chats.json`)是**共用**的;只有每次调用 `Utils.ResolveRoot()` 的那 5 处才吃 `TIPCL_HOME`(D-20)。
  搬来的 Windows 测试里因此存在顺序敏感的用例,见 D-26。

## D-26 进程内执行器:补 `Dispose`/`IAsyncLifetime`,执行顺序改成声明顺序

- **背景**:`Program.cs` 原来用 `Activator.CreateInstance` 建实例后不 `Dispose`
  (`AiFileToolsTests : IDisposable` 的临时目录删不掉),也不支持 `IAsyncLifetime`;
  另外它按**方法名排序**执行用例,而 xunit 走**声明顺序**。
- **实测**:`AiFixChatStoreTests` 三条用例各自 `new TestHome()`,但 `Store` 走缓存 `Utils.ROOT`(D-25),
  字母序把 `SetActiveOnUnknownChatIsSilentAndKeepsActive` 排到 `EnsureChat*` 之后,于是它读到前一条用例
  留下的 `fix-20260101000000`,`Assert.Null(chat)` 失败;同一份 dll 走 `dotnet test` 通道**通过**。
- **决定**:`Execute` 跑完(无论成败)按 xunit 规则清理实例(`IAsyncLifetime` 优先于 `IDisposable`);
  方法排序 `OrderBy(m => m.Name)` → `OrderBy(m => m.MetadataToken)`(声明顺序,与 xunit 一致)。
- **范围**:只动 `tests/TipCL.Tests/Program.cs`(本仓库自己的测试基础设施),**没有**改任何被搬来的测试、
  没有删用例、没有改弱断言。改完两条通道的用例数逐类一致(见 PROGRESS.md 的 A6 小节)。

## D-27 `PhoneLinkTests` 归 C 类:testhost 里 `HttpListener` 起不来(环境限制,不是移植差异)

- **现象**:`dotnet test` 通道里 `PhoneLinkTests.FullFlow` 必失败(连跑 2 次复现),异常原文:
  `System.Net.HttpListenerException : 句柄无效。`,栈顶 `System.Net.HttpListener.SetupV2Config()`
  ← `HttpListener.Start()`(`PhoneLinkTests.cs:44`,前缀 `http://127.0.0.1:<随机端口>/`)。
- **反证**:同一份 `TipCL.Tests.dll` 走进程内执行器通道时,`PhoneLinkTests` 的 4 条用例**全过** ——
  说明不是代码问题、也不是移植差异,而是本沙箱 testhost 子进程的环境限制
  (它在 Low 完整性 / 受限句柄下跑,D-05 记过它一启动就要拿跨进程句柄)。
- **决定**:按任务书「环境原因归 C 类并标『环境限制』,不要改成别的监听方式」处理:整文件保持
  `Compile Remove`,不改 `FakePhone`、不换监听实现。恢复条件:能在 testhost 里起 `HttpListener` 的环境
  (本机可改用进程内通道验收)。
- **顺带**:`PhoneLink.cs:279` 的 `TempZip()` 用 `Path.GetTempPath()`(Core 源码,不许改),
  进程内跑 `FullFlow` 会在 `%TEMP%` 建一个空目录 `tipcl-phone-sync`;归 C 之后不再产生(见 A6 收尾实测)。

## D-28 `StaticInitOrderTests` 归 C 类(修正 Lead 给的事实:不只是 sln 名字的问题)

- **背景**:Lead 把它和 `IconFontTests` 一起列为「靠向上找 `TipCL.sln` 定位仓库根」。向上找 sln 那部分属实
  (B 类改名 `TipCL.sln` → `TipCL.Mac.sln` 就能修),但它读的源码是 `src/TipCL/Pages/SettingsPage.cs`
  —— 界面层的设置页,mac 版到 C3 才有。
- **决定**:整文件归 C,恢复包 **C3(设置页)**,保持字节原样、不放进编译,
  也不单独做那处 sln 改名(C 类文件与 Windows 源逐字节一致,以后恢复时差异才看得清)。

## D-29 `DecompileArtifactTests` 的扫描根从 `src\TipCL` 改成 `src`(B 类的一处判断)

- **背景**:该用例扫「整个启动器源码树」找反编译残留 `*)(&`。Windows 的根是 `src/TipCL`(App 工程),
  mac 版没有这个目录(`src/TipCL.App` + `src/TipCL.Core`)。
- **决定**:按 B.1「读源码的相对路径适配」的精神,把扫描根改成 mac 的源码根 `src`。
  断言与判据一个字没改,而且扫描面**更大**(Core + App 都要干净),不是放宽。
- 同一个文件里另一处 B.1 改动是 `TipCL.sln` → `TipCL.Mac.sln`。

## D-30 事实修正:Windows 测试目录是 30 个 `.cs`,不是 29 个

- Lead 的事实写「共 29 个 `.cs`(84 KB)加一个 `fixtures\webp-sample.webp`」,并据此说「除 `TestHome.cs` 以外的
  28 个复制过去」。
- **实测**(`Get-ChildItem <WT> -File -Filter *.cs`):**30 个** `.cs`,合计 85875 字节(≈84 KB,与 Lead 的字节数吻合);
  除 `TestHome.cs` 外是 **29 个**,不是 28 个。A6 按「除 `TestHome.cs` 外全搬」执行,共搬 29 个(85038 字节)。
- 用例数口径:本包最后 **652 = 525(第 2 步补 2 条隔离用例后、搬测试前的数)+ 127(搬来的测试本机实际可跑的用例数)**。





## D-31 `MacKeychainSecretStore` 走 `/usr/bin/security`,密码会短暂出现在 argv 里

- **背景**:GOAL §3 已定「凭据走 `/usr/bin/security`(Keychain generic password)」,所以命令行形态没有选择余地。
- **已知风险(如实记录)**:写入(`add-generic-password -w <value>`)时密码是**命令行参数**,
  同机其他用户在那一瞬间可以用 `ps` / `ps -ww` 看到它,或者读到该用户的 `ps` 快照。
  这是「走 `/usr/bin/security`」的直接后果,不是实现缺陷。
- **备选方案与为什么不采用**:`security -i`(从标准输入读命令行)能把密码挪出 argv,
  但它的引号/转义规则(以及 `-i` 下 `-w` 后面带空格、带引号的值怎么切分)本机**没有任何办法验证**
  (本机是 Windows,没有 `security`,红线也不许起外部进程去试)。按「拿不准选最保守」的反面考量:
  不能验证的输入转义规则风险更高(可能写出错误的密码),所以保留 argv 形态并把风险写在这里。
- **已做的补偿**:写入的值一律是 `b64:` + UTF-8 Base64(见 D-32 同源理由),所以 argv 里出现的是
  Base64 而不是明文,`ps` 看到的内容需要再解码一次才是有意义的明文 —— 这降低了(不是消除)泄漏价值。
- **错误语义对齐 Windows**:`GetPassword` 找不到 → 返回 null(exit 44);`DeletePassword` 找不到 →
  `SecretNotFoundException(service)`;其它非 0 → `OSError("(退出码, 'security <子命令>', 'stderr')")`。
  异常消息与日志里**不许出现密码**(写入失败时先对 stderr 做打码,见单测)。

## D-32 插件 `SecretStore` 的 TCS2(AES-256-GCM)+ internal 注入入口

- **新魔数**:`TCS2`。布局 = `TCS2`(4) + nonce(12,每次写入随机) + tag(16) + 密文;魔数 4 字节作为 AAD。
- **主密钥**:32 字节随机数,Base64 后写进 `ISecretStore`(服务名 `TipCL plugin secrets` / 账号 `master-key`);
  不存在时生成并写入,已存在就复用(单测:两次写入只调一次 `SetPassword`)。
- **写入分支**:Windows 仍写 `TCS1`(DPAPI,行为不变);非 Windows 且有可用 `PlatformServices.SecretStore` 时写 `TCS2`;
  非 Windows 且没有凭据库时保持原来的 `TCS0`(不改变现有行为)。
- **读取分支**:`TCS2` 解密失败 / 密钥缺失 / 文件被改一个字节 → 与这个文件原有的「读不出来」完全一致(当空表,
  `Get` 返回 null、`Names` 空);`TCS0` 继续可读;`TCS1` 在非 Windows 上仍然读不出来(与 A5 记录一致)。
- **不静默降级**:凭据库调用失败(非「条目不存在」)时 `Set` 把异常抛出去,磁盘上**不留** `TCS0` 文件,
  也不留 `.tmp`(先取密钥再落盘)。单测覆盖。
- **主密钥坏了不覆盖**:凭据库里已有的主密钥不是合法 Base64 / 长度不是 32 字节时,抛 `InvalidDataException`
  且**不覆盖**那条记录 —— 悄悄换密钥会让既有文件永久读不出来,那比报错更糟。
- **`AesGcm.IsSupported` 为假**时抛 `PlatformNotSupportedException`,不降级。本机(Windows)实测为 true;**mac 上未验证**。
- **注入入口**:`internal SecretStore(string path, ISecretStore keyStore, bool treatAsWindows)`。
  公开构造函数 `SecretStore(string path)` 签名与 Windows 上的行为一字不变(改成把
  `PlatformServices.SecretStore` / `IsWindows` 交给内部构造函数),这样 AES 分支在本机也能被测到。

## D-33 Skia 编解码器的语义取舍(逐项对齐 WPF 后的实测结论)

- **只能解到 N32**:实测 SkiaSharp 2.88.9 里 `SKCodec.Info.ColorType` 恒为 `Bgra8888`,
  而且对 `Rgb888x/Opaque` 目标调 `GetPixels` 会返回 `SKCodecResult.InvalidConversion`。
  → 统一解到 `Bgra8888/Premul`,再用 `SKBitmap.Copy(Rgb888x)` 给「没有 alpha 通道」的图去掉 alpha
  (对齐 WPF 的 `FormatConvertedBitmap(...Bgr24...)`,也让重编码的 PNG 是 color type 2)。
- **HasAlpha 按 `AlphaType` 判**:`Info.AlphaType != Opaque` → 有 alpha(`Alpha8` 也直接算有)。
  与 WPF 按「像素格式是否带 alpha 通道」等价;唯一已知边角是「RGB + tRNS」(WPF 报 false,Skia 报 Unpremul → true)。
- **EXIF 方向**:`SKCodec.EncodedOrigin` 生效的是 JPEG 的 EXIF;**PNG 的 eXIf 块在 2.88.9 上不生效**(实测恒 `TopLeft`)。
  8 个方向的矩阵按「像素矩形约定」推出,并用「EXIF 定义 vs 逐像素对拍」的 8 条用例钉住(不写成实现的自证)。
- **JPEG 编码器只吃 N32**:实测对 `Rgb888x` 源调 `SKImage.Encode(Jpeg, q)` 返回 null
  → `EncodeJpeg` 先转 `Bgra8888`;`ClampQuality` 抽成 internal 纯函数(1..100)并单测。
- **不引 ImageSharp**:WebP 直接由 Skia 解(不需要 WPF 那套 `WebpFallback`);实测 PNG / JPEG / WebP / GIF / BMP 都能解。
- **不需要 `RunSta`**:WPF 的 `BitmapSource` 有线程亲和性才要 STA 线程,Skia 位图没有。

## D-34 `TipCLSkiaSharpVersion = 2.88.9` 放在 Directory.Build.props

- `TipCL.Core` 为了平台实现引了 `SkiaSharp`;Avalonia 11.3.23 解析到的也是 2.88.9。
  版本写成一个属性并注释「必须与 Avalonia 解析到的版本一致,升级 Avalonia 时一起核对」——
  两份不同版本会让进程同时加载两个 SkiaSharp 与两份原生库。
- 包已经在 NuGet 缓存里(Avalonia 带来的),所以本机 restore 不需要联网。

## D-35 平台接线放在 `TipCL.Core.PlatformBootstrap`,不在 `TipCL.App`

- `PlatformBootstrap.Init(bool isMacOs)` 是纯赋值(不起进程、不读写数据目录),`Init()` 用
  `OperatingSystem.IsMacOS()` 决定凭据实现;`CreateImageCodec()` / `CreateSecretStore(bool)` 是可直接单测的纯函数。
- 放在 Core 的原因:测试工程只引用 `TipCL.Core`。若放进 `TipCL.App`,测试就得引用 App,
  有把 Avalonia 的平台初始化拉进测试进程的风险(拿不准 → 选保守)。
- `TipCL.App\Program.cs` 只在 `Main` 第一行调 `PlatformBootstrap.Init()`(桌面与 `--smoke` 共用同一行,
  在分支之前),其它逻辑未动。`--smoke` 的两张图字节数与 A1/A6 完全一致(3739 / 3751),接线没有改变画面。

## D-36 本机工具陷阱:PowerShell 5.1 的 `Get-Content | Set-Content` 会把中文改坏

- **现象**:用 `(Get-Content x.cs -Raw) -replace ... | Set-Content -Encoding UTF8 x.cs` 做批量替换后,
  文件里的中文变成 `绉樺瘑` 这类乱码(UTF-8 字节被按 GBK 读进来)。
  本机 `pwsh` 实际是 **Windows PowerShell 5.1**,`Get-Content` 不带 `-Encoding` 时用 ANSI 代码页 ——
  这是 D-12「含中文的文件禁止用 `Get-Content | Set-Content` 重写」的又一次现形。
- **代价与修复**:B1a 期间两个**新建的测试文件**被这样改坏(`B1aKeychainTests.cs` / `B1aPlatformBootstrapTests.cs`),
  已用编辑工具整文件重写;随后全仓库扫描(`*.cs` / `*.md` 找 `U+FFFD` 与典型乱码字)确认**没有其它文件受影响**,
  被复制来的 Core 源码与既有文件全部完好。
- **以后的做法**:批量替换一律走 .NET API + 显式编码(`[System.IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)`
  → `WriteAllText(..., new UTF8Encoding($false))`),不要用 `Get-Content`/`Set-Content`。

## D-37 B1b 的四处保守取舍(读取路径的异常语义、代码页、软链、截断)

1. **`TCS2` + 没有可用凭据库 → 读抛 `PlatformNotSupportedException`(不是空表)**。
   - 任务书规定了「凭据库调用失败」必须原样抛出、「凭据库里没有主密钥」按读不出来返回空表,
     但没说 `PlatformServices.SecretStore == null`(凭空没有凭据库)算哪一种。
   - **选了抛**:写路径在没有凭据库时是走 `TCS0`(D-32,不变),如果读路径把「没有凭据库」当成空表,
     那么 `Set` 就会在「读不出来」之后把已有的 `TCS2` 文件覆盖成只含新条目的 `TCS0` —— 正是本包要修的
     数据丢失路径。抛出后上层看到异常、文件一个字节不变。
   - 代价:没接线凭据库的进程读 `TCS2` 会看到异常;mac 上 `PlatformBootstrap.Init()` 一定会接 Keychain,不受影响。
2. **`CodePages` 不引包**。实测 `System.Text.Encoding.CodePages.dll` 就在本机 net8.0 共享框架里
   (`C:\Users\Administrator\.dotnet\shared\Microsoft.NETCore.App\8.0.33\`),`TipCL.Core` 直接编译通过 →
   **没有改 csproj、没有动 `NoWarn`、没有新增警告**。类库里**不用** `[ModuleInitializer]`(会触发 CA2255),
   注册改由 `PlatformBootstrap.Init(bool)` 第一句调用;**测试工程**用自己的 `[ModuleInitializer]`
   (`tests\TipCL.Tests\B1bCodePagesTests.cs`)保证「用例执行顺序不影响代码页状态」——
   没有改 `TestHome.Init()`,也没改 `Utils.Cp437` 的缓存逻辑(D-16 的静态隔离一字未动)。
3. **`TCS2` 截断密文的判定放在取主密钥之前**。原来 `AesGcmDecrypt` 的判定顺序是
   IsSupported → 长度 → 取主密钥;新读取路径保持这个顺序,所以一个**截断**的 `TCS2` 文件
   不会去碰凭据库(凭据库坏了也不影响「截断 → 空表」的结果)。`AesGcmDecrypt` 内部仍保留这两道判断。
4. **`GlobalMods.IsSymlink` 不改**。复核后认为 `FileInfo.Attributes & FileAttributes.ReparsePoint`
   在 Unix 上对符号链接成立(.NET 的 Unix 文件状态把链接标成 ReparsePoint),所以非 Windows 下判断正确;
   唯一边角是「悬空链接」在 Unix 上会走 `File.GetAttributes` 抛异常 → `catch` 返回 false。
   按任务书「拿不准不要改、留给 B2」处理:**只记录,不改**。

补充记录(不是取舍,是事实):`OperatingSystem.IsWindows()` 与 `Utils.IS_WINDOWS` 语义等价
(`Utils.IS_WINDOWS = OS_NAME == "windows"`,`OS_NAME` 来自 `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`,
Utils.cs:347-358),所以 `Sysinfo._winreg_value` 与 `SecretStore` DPAPI 分支换守卫**不改变运行时行为**;
`_windows`(注入标志)的语义也保持原样,只是额外用 `OperatingSystem.IsWindows()` 满足分析器。

## D-38 mac 的 Mojang 运行时平台键是 `mac-os` / `mac-os-arm64`(不是 `osx` / `osx-arm64`)

- **线上实测**(2026-11-12 18:58 与 19:01,`…/java-runtime/2ec0cc96…/all.json`,HTTP 200 / 13385 字节):
  顶层键只有 `gamecore` / `linux` / `linux-i386` / `mac-os` / `mac-os-arm64` / `windows-arm64` / `windows-x64` / `windows-x86`。
  非空组件数:`mac-os` 7、`mac-os-arm64` 4(delta / epsilon / gamma / gamma-snapshot,**没有 jre-legacy / alpha / beta**)。
- **改动**:`_PlatformKey()` 拆成可注入纯函数 `PlatformKeyFor(string os, string arch)`;mac 分支返回 `mac-os` / `mac-os-arm64`。
  `windows` / `linux` / 默认分支逐字保持(含清单里并不存在的 `linux-arm64` —— 那不是本包的事,已按红线原样保留)。
- **为什么不顺手改 Linux 的 `linux-arm64`**:任务书明确「清单里并不存在的 `linux-arm64`,那不是本包的事」,
  且红线要求 Linux 分支算出来的每个键都不许变。

## D-39 Apple 芯片上 Java 8 的缺口:**不加**厂商切换,保持现有回退链(需要 Lead 裁决)

- **实测事实**:Adoptium `os=mac&architecture=aarch64&image_type=jre` 在 `major=8` 上返回 **0 个包**(响应体 3 字节 `[]`);
  `major=17` / `21` 都有包。Zulu 侧只查到 `os=macos&arch=aarch64` 在 17 上有 zip 包(4 条),
  **8 的 tar.gz 是否有包没查到** —— 联网请求预算(≤12)已用完,不重试。
- **裁决执行**:任务书写「如果现有回退厂商对某个 major 在 mac aarch64 上没有包**而另一个已支持的厂商有**」
  才加切换。Adoptium 没有包这一半已经证实,但「Zulu 有 Java 8 的 mac aarch64 tar.gz 包」**没有证据**,
  因此按「拿不准选最保守、不硬凑」处理:**不加**新选择逻辑,保持现有链路
  (`EnsureJavaForVersion` → Mojang 拿不到 → `InstallAdoptium`),Apple 芯片上需要 Java 8 的老版本会**下载失败**。
- **同时明确否决的两条路**:
  1. **不用** `mac-os`(x64)的 Mojang 运行时经 Rosetta 运行 —— 任务书明令禁止静默改变架构;
  2. **不用** Adoptium 的 `x64` 包在 arm64 上跑 —— 同上,都是静默改架构。
- **给 Lead 的选项**:①给 1 个查询预算(aarch64 + java_version=8 + archive_type=tar.gz),有包就加一条
  「mac + arm64 + major≤8 → 改走 zulu」的可注入纯函数 + 单测;②接受现状(失败信息是
  `Azul Zulu 没有 …` 或 Adoptium 的下载失败)。

## D-40 Zulu 的 `archive_type` 在 mac 上改成 `tar.gz`(Windows / Linux 一字不动)

- **矛盾实测**:`_InstallZulu` 的查询地址写死 `archive_type=zip`,而落盘文件名用 `ArchiveSuffix()`
  (非 Windows = `.tar.gz`),`DownloadManager.ExtractArchive` 又**按文件名后缀**选解压器 →
  mac 上会把 zip 的字节交给 `GZipStream`,必然失败。线上实测 `os=macos&arch=aarch64&java_version=17&archive_type=zip`
  确实回 `.zip` 文件名(`zulu17.70.113-ca-fx-jre17.0.21.0.3-macosx_aarch64.zip`),这个矛盾是真实存在的,不是理论上的。
- **改法**:`ZuluArchiveTypeFor(string os)`:mac → `tar.gz`,其余(**含 Linux**)→ `zip`。
  必须只改 mac:Linux 现在也是「请求 zip / 按 .tar.gz 落盘」,**改它会动到 Linux 的 URL 参数**,红线不许。
  代价是 Linux 的同类缺陷原样保留(已记在 PROGRESS.md 的 B2a 遗留第 2 条)。
- **风险**:mac + aarch64 + `archive_type=tar.gz` 是否有包**未验证**(预算用尽)。已证实 mac + x64 + tar.gz 有包、
  mac + aarch64 + zip 有包。若该组合无包,`_InstallZulu` 会抛明确的「Azul Zulu 没有 … 的 JRE 包」,不会静默错解压。

## D-41 `Utils.SafeExtractTargz` 的两处缺口:mac 用新实现绕过,**不改**第二个复制来的文件

- **现状(读了源码,不是猜的)**:`DownloadManager.ExtractArchive` 按后缀分流,`.tar.gz` → `Utils.SafeExtractTargz`
  (**托管代码,不是外部 `tar`**)。它的入口路径穿越检查(`UnderRoot`)是满足的,但
  ① `File.WriteAllBytes` 落盘 → **tar 的 mode 丢失**(`bin/java` 变成 0644、`lib/jspawnhelper` 同);
  ② `type '1'/'2'`(硬链/软链)一律 `continue` → **软链丢失**(mac 的厂商 JRE 里软链是常态形态)。
- **决定**:任务书要求「若需改,优先用 .NET 8 自带的 `System.Formats.Tar`」,但那是**第二个复制来的文件**;
  任务书同时要求「发现必须改第二个复制文件就停手、写进 PROGRESS 交 Lead 裁决」。
  两条约束的交集 → **不动 `Utils.cs`**,在**新文件** `Launcher\JavaRuntimeFiles.cs` 里写 mac 专用解压
  (`SafeExtractTarGz`,用 `System.Formats.Tar`,不引包),由 `ExtractJavaArchive(..., isMac)` 只在 mac 分支调用;
  Windows / Linux 原样转调 `DownloadManager.ExtractArchive`(有单测钉住:注入的权限位出口一次都不会被调用)。
- **代价**:Linux 仍然丢权限位与软链(它的后缀也是 `.tar.gz`)。修法建议已写进 PROGRESS.md 的 B2a 遗留第 2 条。
- **两个安全细节的取舍**:
  1. 条目路径逃出目标目录 → **抛 `ValueError`**,与旧实现一致(不是跳过),因为条目路径是压缩包的结构性问题;
  2. 软链目标逃出目标目录 → **跳过 + 记日志**(任务书明确要求「跳过并记日志」),因为软链只是条目之一,不该让整包解压失败。
     相对目标**原样保留**(而不是换成绝对路径),这样安装目录整体移动后链接依然有效。
  3. tar 里的 setuid / setgid / sticky 位一律 mask 掉(只留 0777),不给安装目录种 setuid。

## D-42 java 定位的接缝:`FindJavaUnderRoot(root, isWindows, isMac)`

- **核对结论**:mac 的三种目录形态(`jre.bundle/Contents/Home/bin/java`、`xxx.jre/Contents/Home/bin/java`、
  厂商 tar 解到根后的 `Contents/Home/bin/java`)原来的 `Utils.FindExecutable` **都能找到**
  (它先试 `bin` / `jre/bin` / 根,再按层级遍历,目录名 `Contents` / `Home` 不在 skip 表里)—— 所以**不算缺口**。
- **仍然加了接缝**:mac 分支先按已知形态的**固定顺序**试(快且确定),再回落到原来的遍历(厂商 tar 顶层目录名不定);
  `isWindows: true` 或 `isMac: false` 时**原样转调 `Utils.FindExecutable`**,Windows / Linux 行为一字不变
  (有 4 组用例直接钉住「= `Utils.FindExecutable(root)`」)。
- **为什么参数是三个布尔而不是一个**:测试要在 Windows 上验证 mac 分支,而 `Utils.IS_WINDOWS` / `Utils.IS_MAC`
  是 `readonly static`(不能注入);把两个平台标志都做成参数,生产代码传 `Utils.IS_WINDOWS` / `Utils.IS_MAC`,
  Windows / Linux 走的分支与改动前**逐字相同**,mac 分支可在本机单测。

## D-43 Microsoft 分支在 mac 上的同类不一致:只记录,不改(没有线上证据)

- `_InstallMicrosoft` 的 URL 写死 `…-<major>-<os>-<arch>.zip`,落盘却用 `ArchiveSuffixFor(os)`(mac/Linux 是 `.tar.gz`),
  与 Zulu 是同一类矛盾。
- **不改的理由**:本包没有查到「`aka.ms/download-jdk/microsoft-jdk-<major>-mac-<arch>` 到底给 `.zip` 还是 `.tar.gz`」
  的线上证据(请求预算已用完),而且 Microsoft 不在自动回退链里(只有用户显式选厂商时才走)。
  按「拿不准选最保守」保留原样,把修法(与 Zulu 同型:扩一个可注入纯函数)写进 PROGRESS.md 的 B2a 遗留第 3 条。

## D-44 `MacNativeSupport` 的判定口径:按 `group:artifact` 归组 + 无后缀分类器按 x86_64 记

- **归组**:1.19.4 起 Mojang 把 `natives-macos` 与 `natives-macos-arm64` 写成**两条独立的 library 条目**
  (库名第 4 段才是 classifier,且没有 `natives` 字段)。如果按「每一条库就是一个原生库」去判,
  7 个原生库会被数成「7 条有 arm64 + 7 条没有」→ 误判成 `Mixed`。所以按 `group:artifact` 归组,
  同一组的任一条目带 arm64 就认为这个原生库有 arm64 版本(1.19.4 / 26.3 因此都是 `Arm64Native`)。
- **无后缀的老式分类器算 x86_64**:`natives-osx` / `natives-macos` 字面上不含架构。依据是第 0 步 8 份 JSON 里
  1.7.10 / 1.8.9 / 1.12.2 / 1.13.2 / 1.16.5 / 1.18.2 连一个 arm64 字样都没有,对应的 LWJGL 2.9 / 3.1.6 / 3.2.x 在 mac 上只发 x86_64 包。
  这是保守判定:如果真机上发现某个老包其实是 universal 二进制,要改 `_key_arch`。
- **不接入调用点**:本函数只配测试,给 Lead 裁决「Apple 芯片上旧版本怎么跑」用(PROGRESS.md 的 B2b 第 5 项)。

## D-45 `McLauncher._classpath_path`:把内联的「库进不进 classpath」抽成可注入方法(逻辑逐字搬运)

- **背景**:红线只允许「加可注入 OS / ARCH 的 internal 重载 + 原有方法转调」这一种形态。但第 2 项明确要求
  「没有 `natives` 字段、靠库名 classifier + `rules` 区分平台的新式库,进不进 classpath 也要按 `osx + x64` / `osx + arm64` 断言」——
  这段逻辑当时内联在 `BuildLaunchCommand` 的循环里(改动前 `McLauncher.cs` 541-573 行),没有可注入的入口。
- **做法**:抽成 `internal static string _classpath_path(PyDict lib, string libs_dir, string os, string arch, string osVersion)`,
  方法体是那段循环的逐字搬运(含 `clientreq=false` 跳过、没有 `name` 跳过、有 `natives` 而没有 artifact 跳过、
  缺 `artifact.path` 时用 `Utils.MavenArtifactPath(name)` 兜底),循环改为调用它并传 `Utils.OS_NAME` / `Utils.ARCH` / `Utils.OS_VERSION`。
- **没有第二份实现**:不在新文件里另写一份「判定函数」,避免两处真相;测试同时用「注入本机三字段 == 注入字面量 windows + x64」
  的等价钉子把 Windows 行为钉住(见 `B2bClasspathTests`)。
- **如果 Lead 认为越界**:回退就是把方法体搬回循环,`B2bClasspathTests` 里对 `_classpath_path` 的断言随之换成对
  `Utils.CheckRules` 的断言 —— 已在 PROGRESS.md 的 B2b 遗留第 5 条写明。

## D-46 「本次启动的目标架构」= 版本的确定函数,只有 `osx + arm64` 且原生库不全有 arm64 时才退 x64

- **决定(落地 Lead 在 B2b 验收里的裁决)**:`LaunchArch.For(mergedVersionJson, os, processArch, osVersion)`:
  `os == "osx"` 且 `processArch == "arm64"` 且 `MacNativeSupport.Analyze(...).Kind` 是 `X64Only` 或 `Mixed`
  → 返回 `"x64"`;其余一切情况(含 `Arm64Native`、`NoMacNatives`、JSON 为 null、空 `libraries`、非 osx、进程不是 arm64)
  → 返回 `processArch` 原值。公开入口 `For(json)` 转调并传 `Utils.OS_NAME` / `Utils.ARCH` / `Utils.OS_VERSION`。
- **为什么 `Mixed` 也归 x64**:缺 arm64 变体的那个库在 arm64 JVM 里加载不了,而观察到的线上数据里每个 mac 原生库
  都有 x86_64 变体,整份退 x64 一定拿得齐。
- **为什么 natives 目录不需要按架构分目录**:目标架构只由「版本 JSON + 平台三要素」决定,与已装文件、用户选择、
  时间无关 —— 同一台机器上同一个版本永远得到同一个值,所以不存在「同一版本两种架构的 natives 互相覆盖」。
  B2b 第 6 项做法 4 里担心的这一点不成立。
- **为什么 Windows / Linux / Intel Mac 一定不变**:判定里只有 `osx` + 进程 `arm64` 这一条分支,其余都返回入参原值;
  单测对 8 个版本 × 5 种平台组合逐格钉住(见 `B2dLaunchArchTests` / `B2dNonAppleNailsTests`)。
- **未验证**:整条 Rosetta 路径(mac 上的真实取值链、x64 Java 加载 x86_64 dylib)本机是 Windows,一律标「未验证」。

## D-47 调用点改读目标架构的两种形态:可注入重载(takes os/processArch/osVersion)与「已算好 arch 的纯函数」

- **背景**:任务书要求「含调用点的方法如果是纯逻辑或者能拆出纯逻辑部分,就给它加 `internal` 重载,入参增加
  os / processArch / osVersion,内部用 `LaunchArch.For(resolved, os, processArch, osVersion)`;拆不出纯逻辑的
  (一边算一边下载、解压),把「决定要哪些 natives jar / 哪些库」那一段抽成 `internal static` 纯函数再测」。
- **两种形态(都在 PROGRESS.md 的 B2d 第 4 节逐处记了)**:
  1. **可注入重载**(自己从入参算目标架构,生产代码传 `Utils.*`):
     `Cleaner._lib_relpaths`、`Installer._natives_to_extract`、`McLauncher._needs_natives`、
     `Preflight._natives_required`、`OfficialMigrate._library_relpaths`、`LaunchArch.For`。
     这一种最贴任务书,单测可以直接按 `osx + arm64` 调用。
  2. **已算好 arch 的纯函数**(调用方法顶部已经为 rules 过滤算过一次目标架构,不再重算第二遍):
     `Installer._native_classifier_entry`、`Preflight._natives_entry`。单测先断言
     `LaunchArch.For(夹具, "osx", "arm64", 版本) == "x64"`,再把它传进去,两种形态的断言强度等价,
     而且避免在循环里对同一份 JSON 反复跑 `MacNativeSupport.Analyze`(任务书:不要在循环里反复算)。
- **抽取的等价性**:所有抽取都是原循环体的逐句搬运,`rank` / `CheckRules` / 分支判定一字未改,只有
  「进程架构 → 目标架构」这一处替换;`ExtractNatives` 有一处非功能性差别(缺 jar 告警与解压不再逐库交错),
  已写进方法注释与 PROGRESS.md 的 B2d 第 4 节末。

## D-48 `OfficialMigrate._library_relpaths` 按「链上所有 libraries」算目标架构;`MacNativeSupport` 只改类注释

- **`OfficialMigrate`**:它没有合并 JSON(逐个 JSON 沿继承链走),按任务书先把链上所有 JSON 的 `libraries`
  收集成一个只含 `libraries` 键的 `PyDict`(`_merged_libraries`,按链顺序汇总),用 `LaunchArch.For` 算一次目标架构,
  再照原顺序遍历。**没有改变非 mac 平台的返回值或顺序**(`LaunchArch.For` 在那三种情况下返回进程架构原值,
  遍历 / 去重 / 追加顺序一字未动,单测里有一条专门钉顺序与去重)。public 签名不变:原来「按链读盘」的部分留在 public 方法里,
  读完再转调新增的 internal 重载。
- **`MacNativeSupport.cs`**:只把类注释里「本包**不接入任何调用点**」那句改成「B2d 起由 `LaunchArch` 接入」
  (Lead 授权的唯一改法);判定口径、方法体、`_key_arch` 一行未改。
- **不动 Java 侧**:`Java.cs`(平台键、Adoptium / Zulu / Microsoft 的默认架构、已装 Java 挑选)与
  `Terracotta.cs`(B2c)一律未改,即使它们也在读 `Utils.ARCH` —— 本包只做 natives / 规则 / classpath。

## D-49 探测一个 Java 的架构:先读 `release` 的 `OS_ARCH`,再按启动器目录名推断,都不行按「不知道」处理

- **做法**:`JavaArch.Detect(javaExe)` 只读 `<JAVA_HOME>/release` 一个文件(`JAVA_HOME` = 可执行文件
  路径里 `bin` 的上一级,文本切分、不改分隔符),解析 `OS_ARCH=` 那一行:
  `aarch64`/`arm64` → `arm64`,`x86_64`/`amd64`/`x64` → `x64`,`i386|i486|i586|i686|x86` → `x86`,
  其它值 → 继续;读不到 / 抛异常 → 按目录名推断(只认 `<厂商>-<major>-<arch>` 三段名与 `…-mac-os[-arm64]`);
  仍然不行 → `null`。冲突时以 `release` 为准。
- **为什么不起进程**:`java -version` 只能给大版本,给不出架构;红线也禁止测试起进程。
- **为什么只在 Apple 芯片上调用**:所有调用点(生产)先判 `IsAppleSilicon(os, processArch)`,
  不是就直接返回原结果,连 `release` 都不碰 —— 「Windows / Linux / Intel Mac 行为不变」因此是结构性成立的。
- **未验证**:`release` 里 `OS_ARCH` 的真实取值(本机没有 mac 的 JDK)。线上能核对的是**路径形态**:
  B2e 核对了 Mojang 的 `mac-os-arm64/java-runtime-gamma` 与 `mac-os/jre-legacy` 两份文件清单里都有
  `jre.bundle/Contents/Home/release`(`type=file`,1074 / 528 字节),与 Adoptium 包的布局一致。

## D-50 「读取委托」不是「探测委托」:生产重载不带委托(测试抓到的真 bug)

- **踩到的坑**:`Detect(javaExe, readTextOrNull)` 的可注入参数是**读一个文本文件**的委托。
  第一版 `JavaUsableFor` / `PickJavaForVersion` 把它当成「探测委托」传了 `JavaArch.Detect`(公开入口),
  结果是:探测路径里 `<home>/release` 的**内容**永远拿不到(拿到的是对 release 路径再探测一次的结论),
  探测退化成「只看目录名」——在测试里表现为「`release` 明明写着 `x86_64`,却判成不可用」。
- **改法**:给 `AcceptableForExecutable` / `FilterForTarget` 各加一个**不带委托**的生产重载
  (`…(os, proc, target, exe)` / `…(javas, os, proc, target)`),生产代码只用这个;带委托的重载留给单测。
- **教训**:可注入参数的语义要写进 XML 注释(已写),单测要用**真实文件**再钉一遍生产路径
  (`B2eNonAppleNailsTests` 用 `TestHome.Root` 下的真实 `release` 文件)。

## D-51 Apple 芯片上 `Installer._ensure_any_java()` 的兜底大版本:8 → 17

- **用途先查清**(`Installer.cs:852-858` → `_run_processors`):这个 Java 只用来跑 Forge / NeoForge 安装器的
  处理器 jar(`java -cp <libs> <Main-Class> …`,`Installer.cs:913` 的 `Utils.RunProcess`),挑选时
  **完全不看版本**(第一个已装 → 第一个系统 Java → 都没有才装);所以它要的是「随便一个能跑的 Java」,
  不是「Java 8」。
- **证据**:Adoptium 对 mac `aarch64` **没有 Java 8 的包**(线上实测 0 个,见 B2a 第 1.3 节),
  原样保留的话 Apple 芯片上这条兜底必然抛异常。
- **改法**:`JavaArch.FallbackMajor(os, processArch)`:Apple 芯片 → `17`,其余平台 → `8`(原值)。
  只改 `Installer.cs` 那一行的实参,平台键 / 其它逻辑未动。
- **残留风险**(未验证,记在 PROGRESS.md):老 Forge 的处理器 jar 是 Java 8 字节码,用 Java 17 跑需要
  这些 jar 不依赖 JDK 15+ 已删除的 API(如 Nashorn / JAXB);新 Forge(1.17+)的处理器反而要 Java 17。
  两种情况下这条兜底都比「装不上 Java 8」好,但不能保证 100% 能跑。

## D-52 发现但**没有改**的两处同类缺口(交 Lead 裁决)

1. `JavaForInstaller("forge-legacy", …)`(`Java.cs:1382`)在找不到 Java 8 时仍 `InstallAdoptium(dm, 8L)`,
   即 mac + `aarch64` → **必失败**(Adoptium 没有 8 的 aarch64 包;Azul 有,B2a 的 Lead 用 5 个只读请求证实过)。
   本包的范围(任务书第 1–6 项)没有点名这个方法,**未改**。
2. `BackendApi.export_launch_script`(`BackendApi.cs:2958`)把 `instance.VersionJson(text)`
   —— **未合并的子版本 JSON**——传给 `ResolveLaunchJava`,而另外两个调用点(2933 / 3095)与
   `McLauncher.BuildLaunchCommand`(578)传的都是 `Manifest.ResolveInherits(...)` 的结果。
   带 `inheritsFrom` 的版本(Forge 等)子 JSON 里没有原生库,`LaunchArch.For` 会判成「没有 mac 原生库」
   而得到 `arm64`,于是导出的启动脚本可能选到 arm64 的 Java。红线不许改 `BackendApi.cs`,**未改**。

## D-53 Mojang 运行时安装:建链挪到下载之后,并且做成幂等(四格语义)

- **顺序**:改动前 `Java.cs` 的清单遍历循环里**边遍历边建链**(当时 `DownloadAll` 还在后面),
  也就是「链接先于被链接的文件出现」;现在循环里只收集 `(条目名, 目标)` 到 `list3`,
  等 `dm.DownloadAll(...)` 返回之后、设可执行权限**之前**逐个建 —— 与厂商 `tar.gz` 解压路径
  (文件写完再建链)一致。Windows 分支一字未改(仍然不收集、不建链)。
- **幂等(新增 `JavaRuntimeFiles.TryCreateRuntimeLink(..., LinkHooks)`)**:
  `DecideLink(existingTarget, pathExists, desiredTarget)` 是纯函数,四格:
  路径上没有 → 新建;已是符号链接且目标字符串**逐字相同**(`Ordinal`) → 跳过且**不记警告**;
  已是符号链接但目标不同 → 删掉重建(链接没有内容,删掉安全);
  路径上是普通文件或目录 → 跳过 + 记一条警告,**不删**(宁可少建一条链接,也不删已经下载好的文件)。
- **为什么「逐字相同」而不是解析后比较**:厂商与 Mojang 给的 target 都是相对路径,写进去什么就读出来什么;
  做过一次解析再比较会把 `./../x` 与 `../x` 当成同一条,反而掩盖了清单本身的变化。
- **软链接之外的其它失败仍然只记警告**(路径非法 / 目标逃出安装目录 / 父目录建不出来 / 状态读取抛异常),
  不把异常抛给「装 Java」这条链 —— 与改动前一致(一条坏链接不该让整个运行时装不上)。
- **未验证**:mac 上真实的建链 / 删除 / 悬空链接行为(本机是 Windows,不建真实符号链接;全部用假委托测)。

## D-54 Microsoft OpenJDK 在 mac 上的地址:`macos` + `.tar.gz`(6 个 HEAD 请求的线上证据)

- **取证**(2026-11-12 21:14:52 +08:00,`Invoke-WebRequest -Method Head -MaximumRedirection 0 -UseBasicParsing -TimeoutSec 20`,
  只访问 `aka.ms`,**共 6 个请求**,不跟随重定向、不下载任何文件):

  | 请求路径 | 状态码 | `Location` |
  |---|---|---|
  | `microsoft-jdk-17-mac-x64.zip` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(兜底页) |
  | `microsoft-jdk-17-mac-x64.tar.gz` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(兜底页) |
  | `microsoft-jdk-17-mac-aarch64.zip` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(兜底页) |
  | `microsoft-jdk-17-mac-aarch64.tar.gz` | 302 | `https://www.bing.com?ref=aka&shorturl=...`(兜底页) |
  | `microsoft-jdk-17-macos-x64.tar.gz` | 301 | `https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-x64.tar.gz` |
  | `microsoft-jdk-17-macos-aarch64.tar.gz` | 301 | `https://aka.ms/download-jdk/microsoft-jdk-17.0.21.0.2-macos-aarch64.tar.gz` |

- **裁决(任务书第 2 项的第一种情况)**:证据明确 —— `macos` + `tar.gz` 会重定向到**真实的版本化包名**,
  而 `mac` + `zip` / `mac` + `tar.gz` 全是 aka.ms 的 bing 兜底页(即短链不存在)。
  所以走「改下载逻辑」这一支:**只改 mac**,OS 段 `mac` → `macos`、后缀 `.zip` → `.tar.gz`。
- **实现**:`JavaRuntimeFiles.MicrosoftJdkOsFor(os)` / `MicrosoftJdkArchiveSuffixFor(os)` /
  `MicrosoftJdkUrl(os, major, archToken)` 三个纯函数,`Java.cs` 的 `_InstallMicrosoft` 只改一行转调。
  Windows / Linux 的地址**逐字不变**(`windows` / `linux` + `.zip`,含 Linux 那个「地址 zip、落盘 tar.gz」的原样矛盾——红线不许动),
  未知 OS 仍然抛 `KeyError`(与改动前的 `Java.Pick` 同语义)。
- **顺带修掉的死结**:mac 上落盘文件名走 `ArchiveSuffixFor(os)`(非 Windows = `.tar.gz`)、解压按文件名后缀选解压器,
  改动前「地址要 zip、文件名叫 tar.gz」必然解压失败(D-43 记录、B2a 未修)。现在两者一致。
- **未验证**:`major` 换别的值(8 / 21 / 25)时 mac 的短链是否同样存在(只按任务书查了 17);
  `macos` + `zip` 组合没有查(预算 6 个已用完,但任何结论都不需要它 —— 我们要的就是 `tar.gz`)。
  界面上的「可选厂商列表」是否把 Microsoft 从 mac 去掉:**只记录,未改**(那是界面的事)。

## D-55 `forge-legacy` 在 Apple 芯片上装 Adoptium 8 的 **x64** 包(目录 `adoptium-8-x64`)

- **背景**:`JavaForInstaller("forge-legacy", ...)` 找不到已装 Java 8 时 `InstallAdoptium(dm, 8L, on_progress)`,
  而 Adoptium 对 mac `aarch64` **没有** Java 8 的包(B2a 线上实测:0 个包),Apple 芯片上这条路径必失败(D-52 第 1 条)。
- **裁决落地**:Apple 芯片上改传 `arch = "x64"`(装 `adoptium-8-x64`),其余平台传 `null`(逐字等于改动前)。
  Forge 安装器是纯 Java,x64 的 JVM 经 Rosetta 能跑;而 1.17 之前的版本在 Apple 芯片上本来就要用 x64 的 Java 8 启动,
  同一个目录可以复用,不必再多下一个 Zulu。
- **实现**:`JavaArch.Legacy8AdoptiumArch(os, processArch)`(纯函数,Apple 芯片 → `"x64"`,其余 → `null`),
  `Java.cs` 只改那一行的实参。**为什么放 `JavaArch`**:它是 B2e 建的架构判定文件,`IsAppleSilicon` 已在里面,
  再往 `Java.cs` 里塞逻辑只会让复制文件的 diff 变大。
- **未验证**:Rosetta 下跑 Forge 安装器的真实结果(本机是 Windows,没有 Mac)。

## D-56 `Preflight.FreeBytes` 的非 Windows 分支:`DriveInfo`(保守写法,mac 未验证)

- **做法**:新增可注入内部重载 `FreeBytes(path, isWindows, windowsFree, unixFree)`:
  `FreeBytesHook` 优先级不变(非 null 时两个委托都不碰);否则把路径 `Utils.AbsPath` 规范化后交给对应委托;
  任何异常仍然吞成 `null`。生产入口 `FreeBytes(string)` 按 `Utils.IS_WINDOWS` 选
  `FreeBytesWindowsApi`(kernel32,逐字保留改动前的实现)或 `FreeBytesDriveInfo`。
- **「给定路径怎么找到所在卷」**:非 Windows 上用 `new DriveInfo(绝对路径).AvailableFreeSpace`,
  由运行时把绝对路径归到它所在的挂载点。这是**最保守的写法**:取不到就是抛异常 → 被吞成 `null` →
  与改动前 mac 上 `DllNotFoundException` 被吞掉的表现完全一致(「查不到磁盘剩余空间」),不会误报空间充足。
- **未验证**:mac 上 `DriveInfo` 对任意子目录路径(尤其 `/`、外部卷、网络挂载)的真实行为。
  本机(Windows)只验证了「真实 `DriveInfo` 委托对 `TestHome.Root` 取到大于 0 的值」这条钉子。
- **为什么不改 `CheckLaunch`**:红线禁止测试进 `CheckLaunch`;调用点(`Preflight.cs:125`)一行未动,
  所以「什么时候查、查谁」都没变,只有实现换成了能跨平台的。

## D-57 `GlobalMods.IsSymlink` 非 Windows 分支改用 `FileSystemInfo.LinkTarget != null`

- **背景**:改动前唯一实现是 `FileInfo.Attributes & FileAttributes.ReparsePoint`。B1b(D-37 第 4 条)复核后
  「不改」的判断在 mac 上对**悬空链接**不成立(属性读取会抛 → `catch` 返回 false),任务书第 5 项要求改掉。
- **做法**:`IsSymlink(p, isWindows, readLinkTarget)` —— `isWindows` 为 true 的那一支是改动前实现的**逐字搬运**;
  非 Windows 上 `(readLinkTarget ?? LinkTargetOrDefault)(p) != null`,`LinkTargetOrDefault` 用
  `FileSystemInfo.LinkTarget`(对普通文件返回 null,对正常链接与悬空链接都返回目标字符串)。
  异常一律返回 false(与改动前一致)。
- **`FileInfo` 还是 `DirectoryInfo`**:用 `Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p)` ——
  悬空的**目录**链接上 `Directory.Exists` 是 false,而两者取到的 `LinkTarget` 相同,所以这样能覆盖悬空链接。
- **未验证**:mac 上 `LinkTarget` 对悬空链接确实非 null(任务书的规定;本机是 Windows,测试不许建真实符号链接,
  所以全部用假委托 + 「真实普通文件必须是 false」的钉子)。

## D-58 本机工具陷阱第三次现形:全角标点**抄不进** `edit` 的 `old_string`(B2c2 实测)

- **现象**:用 `edit` 工具把源码里的中文句子整句抄进 `old_string` 时,匹配失败。实测原因是**我抄出来的全角
  标点会变成半角**:写一个 `(`,`old_string` 里就是 U+0028;写一个 `,`,就是 U+002C。而源码里的
  `,` / `(` / `)` / `:` 是 U+FF0C / U+FF08 / U+FF09 / U+FF1A,**字节不同 → 匹配不上**。
  这解释了 A5(D-24)「12 条用例期望值打印出来一模一样却判不相等」以及 A3(D-16)的同类现象:同一个陷阱,
  以前只在「读回来抄进测试」时踩到,这次是在 `edit` 的 `old_string` 里踩到。
- **能可靠写出的字符(实测)**:ASCII 全集(含 `"` `\` 制表符 U+0009)、CJK 汉字、`。`(U+3002)、`、`(U+3001)。
  **写不出的**:U+FF0C / U+FF08 / U+FF09 / U+FF1A 这类「全角 ASCII 外形」的标点。
- **做法(后续包直接用)**:
  1. 源码里的**新文案**尽量用 `\uXXXX` 转义表达需要的全角标点(例如
     `"没找到 adb\u3002可在设置里填写 adb 的完整路径\uFF0C或安装 Android platform-tools 后重试"`);
  2. 测试里的**期望值**一律 `\uXXXX` 转义(D-16 / D-24 的要求在 B2c2 依然照办,而且这次是**必须**的);
  3. 必须改到一句**含全角标点**的旧文本时,不要整句重抄,改用两种办法之一:
     (a) 只匹配/替换句子里的 ASCII 片段(例如把 `throw new X("` 换成 `throw new X(Helper(os, "`,让原字面量
     **原封不动**成为实参 —— B2c2 的 adb 报错文案就是这么做的,`git diff` 里旧文案仍以未改动的样子出现);
     (b) 在旧句后面**加一行「B2c2 更正」**,而不是重写旧句(NodeRuntime 的 `Download` 注释就是这么处理的)。
  4. 核对办法:一次性脚本(`artifacts/`,不入库)把目标行的非 ASCII 字符打成 `\uXXXX` 再比,不要靠肉眼。
- **附带一条**:`Assert.DoesNotContain(string, string)` 查 `U+FEFF` 的坑(D-12 第 1 条)同源,都是「看起来一样、
  其实不是同一个字符」。

## D-59 B2c2 的两处保守取舍

1. **adb 的 Windows 报错文案用「实参」而不是「常量」保留**。任务书要求文案按 os 选,而 `PhoneClient.cs:439`
   那句含全角括号(D-58 抄不动)。做法:`AdbMissingMessage(string os, string windowsText)` —— Windows 分支
   **原样返回调用点传进来的实参**,调用点那句字面量一个字节都没改,只是从「直接传给 `InvalidOperationException`」
   变成「传给 `AdbMissingMessage`」。代价:纯函数层单独看不知道 Windows 文案是什么,所以补了一条**读源码**的
   结构性用例,核对生产调用点传的字面量逐字等于改动前的原文(B2c1 已有「读源码」的先例)。另一个选择是把新文案
   用 `\uXXXX` 写在函数里、把旧文案留在注释里,评估后放弃(注释里的死文本更难维护)。
2. **`AdbCandidates` 的 Windows 分支保留「`localAppData` 为 null 就抛 `ArgumentNullException`」**。原实现是
   无条件的 `Path.Combine(Environment.GetFolderPath(LocalApplicationData), …)`,传 null 同样抛。加一个
   `IsNullOrEmpty` 守卫会让这条候选在「空串」时消失(与原行为不同),而生产中 `GetFolderPath` 在 Windows 上
   永远不返回 null/空。按「Windows 行为逐项不变」优先,保留原语义,并用一条用例把这个语义写清楚。
   非 Windows 分支根本不碰这个参数(mac 的默认位置是 `~/Library/Android/sdk`,不是 `%LOCALAPPDATA%`)。

## D-60 B2c3 的七处保守取舍

1. **`_create_macos` 用「加参数」而不是「改语义」**。任务书允许「加参数或加重载」,本包给 mac 分支加了第七个参数 `IEnumerable<string> rawArgs`(原始参数表),
   而 `arguments` 这个参数在 mac 分支**有意不用**(它是 cmd 风格 `_quote` 的预拼接结果,在 sh 里不安全)。
   Windows / Linux 两个分支收到的实参**逐字未变**,并用源码钉子钉住(`CreateLaunchShortcut` 里 `string.Join(" ", rawArgs.Select(_quote))` 与三条 `return _create_*(...)` 的原文)。
2. **`Chmod755` 复用 `JavaRuntimeFiles.SetExecutable`**,没有另写一套 chmod。理由:同一个程序集里已经有「非 Windows 设 0755、Windows 空操作、失败只记警告」的实现。
   副作用是 Linux 的 `.desktop` 也第一次拿到执行位 —— 任务书已明确接受。
3. **`ExportVersionJson` 非 Windows 分支用 `catch (Exception)` 全兜底**(缺父版本、继承成环、loader 自己抛异常、甚至 loader 是 null),统一退回 `childJson`。
   理由:导出启动脚本是只读动作,宁可脚本里出现未合并的版本 JSON(Java 挑选那一侧还有自己的兜底),也不要让「导出」整体失败;而 Windows 分支根本不进 try,行为逐字不变。
4. **`PosixLaunchScript` 的 cwd 走 `Py.Str(cwd)`**(null → 字符串 `None`),没有改成「null → 空串」。理由:这与 `ExportLaunchBat` 里那句 `cd /d "<Py.Str(cwd)>"` **同源**,
   两条分支在同一个输入上应当给出一致的行为;改成空串反而是新的分歧。测试把这个行为逐字写清楚。
5. **description 里的 CR / LF 压成空格**(而不是删掉或加转义)。理由:注释必须只占一行(否则用户可控文本能把后面的命令顶出注释),同时尽量保留原文信息;
   压完仍然只出现在 `#` 开头的那一行里,由逐字全文用例钉住。
6. **测试里读源码的助手返回正文而不是路径**(本包改名 `RepoSource`)。B2c2 的 `RepoFile` 返回路径、由调用方自己 `File.ReadAllText`;
   本包第一次照抄时漏了 `File.ReadAllText`,22 条用例红在同一类原因上。改名并直接返回正文,让这个坑不能再犯。
7. **导出脚本的默认文件名后缀改用 `ExportScriptExtension(Utils.OS_NAME)`**,Windows 上仍等于改动前写死的 `.bat`(纯函数逐字对照 + 「windows 分支与直接调 `ExportLaunchBat` 字节全等」用例钉住)。

## D-61 `%TEMP%\tipcl-probe` 不是 B2c3 的产物(B2c3 实测,给 Lead 的证据)

- **现象**:B2c3 跑测试的窗口内(22:28:19)`%TEMP%` 下多了一个目录名 `tipcl-probe`,按任务书的收尾要求(窗口内新出现的 `tipcl*` 名字要报出来、期望为空)必须查清。
- **证据(三条,都是实测)**:
  1. 全仓库 grep `tipcl-probe` **零命中**(源码、测试、脚本、文档都没有这个名字),本包也不可能凭空造出它;
  2. 目录内容是一个**别人的 .NET 探针工程**:`probe.csproj` / `Program.cs` / `bin` / `obj` / `out` / `scratch`,其中 `bin`/`obj`/`out` 的时间戳(22:28:32 / 22:28:35)与同一时刻本机另一个 `dotnet build` 的 MSBuild 临时目录成对出现;
  3. 本包两条通道的用例只往 `TestHome.Root` 下写(逐条审过:没有 `Path.GetTempPath()`、没有写真实桌面),随后再做一次「两条通道前后各一次」的名字集合比对是 **87 → 87,没有新名字**。
- **结论**:该目录属于本机另一个会话的临时探针工程,不是本包的越界产物;本包不改它、也不清理别人的临时文件。
  记在这里是为了避免后续验收把它算到 B2c3 头上(机器上同时有别的会话在跑,`%TEMP%` 总量不可比)。

## D-62 tar 用 Ustar 而不是 Pax(确定性实测,改掉继承草稿的写法)

- **背景**:任务书要求"同一份输入两次写出来的 tar 字节完全相同(压缩前)"。继承来的草稿(`b826a1b`)用的是
  `TarWriter(..., TarEntryFormat.Pax)` + `PaxTarEntry`,mtime / uid / gid / uname / gname 都钉死了,看上去够了。
- **实测(探针 `artifacts/tarprobe/`,不入库;`artifacts/tarprobe/run5.log`)**:
  - `Pax`:同一份输入连写两次,**第一次差异在第 533 字节**,内容是扩展头里的
    `atime=1794497685.6498163` vs `atime=1794497685.6521515`(`ctime` 同理)。也就是说 .NET 8 的
    `PaxTarEntry` 每次写都会补 `atime` / `ctime` 两条记录,取值是**写的那一刻**;而 `PaxTarEntry` 的公开属性里
    **没有** `Atime` / `Ctime`(反射确认:`ExtendedAttributes` / `DeviceMajor` / `DeviceMinor` / `GroupName` /
    `UserName` / `Checksum` / `EntryType` / `Format` / `Gid` / `ModificationTime` / `Length` / `LinkName` /
    `Mode` / `Name` / `Uid` / `DataStream`),没法钉死。后果:tar.gz 的 SHA256 不可复现。
  - `Ustar` / `Gnu`:同样两次写,**逐字节相同**(`firstdiff=-1`)。
- **决定**:`TarGz.WriteTar` 改用 `TarEntryFormat.Ustar` + `UstarTarEntry`,并把 `Mode` / `ModificationTime`(固定
  2000-01-01T00:00:00Z)/ `Uid` / `Gid` / `UserName` / `GroupName` 显式写死。实测两次全流程运行的归档 SHA256
  完全相同(`66802328...C06CDC`)。
- **代价**:Ustar 的名字字段只有 100 字节。加 `TarGz.NameMaxBytes = 100` 与显式报错(不静默截断);本 bundle 实际
  最长的名字是 `.../libSystem.Security.Cryptography.Native.OpenSsl.dylib`(83 字节),用例钉住。将来真出现超长名字,
  打包会**响亮地失败**,那时可选换 `Gnu`(实测同样确定)或给条目改名。

## D-63 暂存目录只清 `TipCL.app`,不删整个 `--stage` 目录

- 继承草稿是 `Directory.Delete(options.StageDir, recursive: true)`:`--stage` 是命令行参数,万一指到仓库根或用户目录,
  递归删整棵树的后果太大(本机也不能保证调用方永远传对)。
- 改成只删 `Path.Combine(stage, "TipCL.app")`:tar 里的条目**全部**来自 `TipCL.app/` 之下(六个固定目录 + 两个架构目录
  的实际文件 + Info.plist / 启动脚本 / icns),所以清掉这棵子树就足以保证同一份输入两次运行结果一致。
- 顺带:`staged` 那行现在报出跳过的 `.pdb` 数(实际 2 + 2),免得"没把 .pdb 打进包"只是口头承诺。

## D-64 6 个 universal(fat)dylib 不在 Mach-O 校验与代码签名报告内(如实划边界)

- 实跑发现:打包器只认出 **30** 个 thin 64 位 Mach-O 文件(2 apphost + 2 createdump + 26 thin dylib),
  而 `.dylib` 一共 **32** 个(每架构 16)。差的 6 个(每架构 3 个)是
  `libSkiaSharp.dylib` / `libHarfBuzzSharp.dylib` / `libAvaloniaNative.dylib`。
- **原因(读字节实测)**:这 3 个文件在两个架构目录里的头 4 字节都是 `CA FE BA BE`、偏移 4 的 `nfat_arch = 2`,
  即 **universal(fat)Mach-O**,不是任务书「File-format facts」里定义的 64 位小端单架构 Mach-O。
- **决定**:**不**扩展解析器去读 fat 头。理由:任务书没给 fat 头的字段定义,按"不许凭记忆写 mac 事实"的红线,
  写一个我无法用真实产物核对的解析器风险更大。于是:
  - 这 6 个文件的 `LC_CODE_SIGNATURE` **未验证**,PROGRESS.md 的 B3 小节与最终回报里都写明;
  - 它们的权限位**不受影响**:以 `.dylib` 结尾 → 一律 0755(校验里 `32 dylibs 0755` 就是 16 × 2),任务书要求满足。
- 留给后续(需要真机或 mac 产物才能做):如果要覆盖它们,得先有 fat 头的字段定义与一份真实产物做对照。

## D-65 `CFBundleIdentifier` 用占位值 `com.tipcl.launcher`,用户可改

- 任务书(Lead 裁决)给的占位值,写在 [InfoPlist.cs](tools/TipCL.Packager/InfoPlist.cs) 的 `BundleIdentifier` 常量里,
  唯一一处,`Info.plist` 由它生成,单测与打包器校验共用同一份。
- **这是占位值**:用户/以后发版时想换成自己的反向域名(例如 `com.<自己>.tipcl`),只需改这一个常量并重新打包;
  本仓库不申请、不校验任何 bundle id 的唯一性,也没有做 `codesign`,所以现在这个值只是 plist 里的一个字符串。
- 其余键的值同理集中在 `InfoPlist.ForTipCL()`:名字/显示名/版本取 Core 的 `AppInfo`(不另抄一份),
  `LSMinimumSystemVersion = 10.15` / `NSPrincipalClass = NSApplication` / `CFBundlePackageType = APPL` 等来自任务书。

## D-66 icns 的 128 那一张由 256 缩小,不去取源 .ico 里原生的 128 PNG

- 取证:`packaging\TipCL.ico`(94953 字节)里 7 个条目 16/24/32/48/64/128/**256**,全是 PNG;
  SkiaSharp 2.88.9 的 ICO 解码器给的是最大的那张(256×256,Bgra8888/Unpremul)。
- 任务书要求"只产出不需要把源图放大到超过最大边长的尺寸",于是 `SelectSlots(256)` → `ic07/ic08/ic13`(不出 `ic09`)。
  实现上 `ic08` / `ic13` 直接用解码出来的 256 重编码,`ic07`(128)是**从 256 用 `SKFilterQuality.High` 缩下来的**,
  **没有**去取 .ico 里那张原生 128 PNG。
- 理由:保持"一条数据流、一套缩放规则"更好核;任务书也没要求用原生 128。代价:128 那一张理论上不如原生 128 锐利
  (19655 字节 vs 原生 21832 字节,肉眼差别未验证——本机没有 Mac,看不到 Finder/Dock 的实际渲染)。
  若以后有人要在意这一点,只需在 `IconBuilder.Build` 里改成"源里就有目标尺寸时直接取那一条",`IcnsWriter.SelectSlots`
  的判据不用动。

## D-67 `package.ps1` 的四处取舍(都已在实跑里验证)

1. **`-NuGetSource` 只透传给两条 `dotnet publish`**(`--source <url>`),不动机器上的 NuGet 配置、仓库里也不加
   `NuGet.config`:本机 `huaweicloud` 镜像超时(每次 restore 20 s + `NU1900` 噪声),
   而 `https://api.nuget.org/v3/index.json` 正常。打包器自己那一步走 `dotnet run`,没有 `--source` 选项,
   所以它的日志里仍会出现 `NU1900`(环境噪声,不算新警告)。
2. **脚本存成 UTF-8 with BOM**,与 [tools/build.ps1](tools/build.ps1)(无 BOM)不同:build.ps1 的非 ASCII 只出现在
   **注释**里所以侥幸没坏,本脚本注释更多且更长,按 D-12 补 BOM 让 Windows PowerShell 5.1 正确读中文注释。
   脚本的**字符串字面量与输出仍全是 ASCII**(D-08):打包器打印的也是 ASCII,路径一律相对仓库根。
3. **先预热一次 publish**:本机 NuGet 缓存里原本没有 mac 运行时包,第一次 self-contained publish 要下载
   (`microsoft.netcore.app.runtime/host.osx-{arm64,x64}` 与 `microsoft.aspnetcore.app.runtime.osx-{arm64,x64}`,都是 8.0.33)。
   为避免"脚本超时/被误判卡死",开工时先用**同一对 publish 命令**预热缓存(日志 `artifacts/package-logs/warm-publish-*.log`),
   随后脚本那两次全流程运行都是缓存命中(每步仍然真跑 publish,只是不再下载)。两次全流程分别是 126.7 s / 111.8 s。
4. **每步完整输出进 `artifacts\package-logs\*.log`,控制台只留一行状态 + 打包器的校验清单**:publish 的日志几十万行,
   倒到控制台既看不清也会把上下文撑爆(GOAL 第 5 节的上下文纪律)。`stderr` 并进管道时临时把
   `$ErrorActionPreference` 切回 `Continue`,免得"有警告的构建"被 PowerShell 5.1 当成终止性错误。



## D-68 主题色板用 Avalonia 的 ThemeDictionaries,查不到键给中性灰兜底

- 色板落在 `src/TipCL.App/Themes/Palette.Light.axaml` / `Palette.Dark.axaml`(Avalonia 的 `ResourceDictionary`),
  键名、键序、取值与 Windows 版 `Themes\Palette.Light|Dark.xaml` **逐项一致**(各 61 个键:7 个 `C.*` 颜色 + 54 个刷子)。
  语法只做等价翻译:WPF 的 `<LinearGradientBrush StartPoint="0,0" EndPoint="1,1">`(默认 RelativeToBoundingBox)
  写成 Avalonia 的 `StartPoint="0%,0%" EndPoint="100%,100%"`,`Opacity` 仍是刷子的普通属性。
- 装法:在 `App.Initialize()` 里调 `Palette.Install(this)` —— 两份 `ResourceInclude` 放进
  `Application.Resources` 里一份字典的 `ThemeDictionaries[Light/Dark]`。这是 Avalonia 11 的惯用做法,
  好处是 C1b 的 `Controls.axaml` 可以直接写 `{DynamicResource B.Ink}`,不用在代码里切两份字典。
  **没有**走「两份字典 + `RequestedThemeVariant` 代码 switch」那条路(那样 C1b 的 axaml 会没法写动态资源)。
- 代码侧入口是 [src/TipCL.App/Themes/Palette.cs](src/TipCL.App/Themes/Palette.cs):`Keys`(61 个键,顺序照 WPF 源)、
  `TryGet` / `Brush` / `Color` / `Current` / `Normalize`,以及 `IconFont` / `IconFontUri`。
  **查不到的键返回中性灰 `#808080`(Palette.FallbackColor),不抛异常** —— 键名写错属于"编译期看不出来、运行期才炸"的
  那一类错,不该让窗口起不来。
- 变体归一化:只有明确 `ThemeVariant.Dark` 才算深色,其余(含 `Default`、跟随系统)按浅色 ——
  与 A1 占位色的判定是同一套规则(D-04 的判定保留,只把颜色换成真色板)。
- 「代码里列一份键表」与「axaml 里写一份」是**故意的双份**:`Keys` 负责给"按字符串查键"的老代码做存在性对拍,
  测试里要求 WPF 源、两份 axaml、`Keys`、以及源码里出现的 `"B.*"` / `"C.*"` 字面量**四处都对得上**。
- 一次性解析 vs 动态资源:图标/刷子在创建时按当前变体**解析一次**(`Palette.Brush(key)` 会读
  `Application.ActualThemeVariant`)。Windows 版用的是 WPF 的 `SetResourceReference`(动态资源,主题切换自动重取色);
  mac 版目前只有启动期决定主题(`--theme`)与窗口自己监听 `ActualThemeVariantChanged`,**已有控件不会跟着热切换**。
  C2 接主题热切换时要么改成 `{DynamicResource}` 绑定、要么订阅变体重建,到时再定(见"遗留")。

## D-69 C1a 的测试工程引用 TipCL.App,期望值的权威副本钉在测试里

- `tests/TipCL.Tests` 新增 `<ProjectReference Include="..\..\src\TipCL.App\TipCL.App.csproj" />`,
  靠 TipCL.App 的 `<InternalsVisibleTo Include="TipCL.Tests" />` 看到 `internal` 的 `Palette` / `LucideAssets`
  (与 D-19、B3 对 `TipCL.Packager` 的手法一致)。
- 为什么不能只把 axaml 当 XML 文本解析就完事:**几何解析本身就要真平台** —— 本机实测,不
  `SetupWithoutStarting()` 直接 `Geometry.Parse` 会抛
  `InvalidOperationException: Unable to locate 'Avalonia.Platform.IPlatformRenderInterface'`。
  所以 C1a 的用例分两层:
  1. **XML 文本层**(不需要平台):键集合 / 键序 / 取值 / 语法 / 源码里按字符串查的键是否存在;
  2. **headless Avalonia 层**(`UseHeadless(UseHeadlessDrawing=false)` + `UseSkia()`,与 A1 的 smoke 同一套配置):
     资源求值、`C.*` 颜色对象、刷子不透明度、渐变停靠点、avares 字体族与字形、几何 bounds。
- 期望值的权威副本钉在 [tests/TipCL.Tests/C1aFixtures.cs](tests/TipCL.Tests/C1aFixtures.cs)(键表 + 取值 + 字体 SHA256),
  **不依赖** `D:\项目\tipcl\windos\...` 那条路径可用;路径可用时另有交叉复核用例(不可用就打印一行原因后返回,不判失败)。
- **踩到的坑(必须记)**:Avalonia 建 Dispatcher 时会把当前线程的 `SynchronizationContext` 换成它自己的。
  进程内执行器是在主线程上 `task.GetAwaiter().GetResult()` 同步等 async 用例的,没人去泵 Avalonia 的 Dispatcher,
  于是**排在 C1a 后面**的 async 用例(实测是 `Ported\WriteJsonTests.cs` 的并发写用例)续体永久排队:
  整个进程跑完全部用例却卡死在收尾,连汇总行都不打。修法:`C1aFixtures.EnsureAvalonia()` 先把旧的
  `SynchronizationContext` 存下来,`SetupWithoutStarting()` 之后在 `finally` 里还原(全局静态改动必须还原)。
  还原后两条通道都正常。

## D-70 Lucide 移植的等价写法(缩放 / 动画 / Asset 缓存 / ToolIcon / Ico.cs)

1. **WPF 的 `LayoutTransform` 在 Avalonia 没有对应属性**:改成把 `size/24` 的缩放**烘进几何**
   (`Geometry.Transform = new ScaleTransform(s, s)`),并把 `StrokeThickness` 同步乘上 s(2 → 2s)。
   这样渲染与 WPF 等价(WPF 的 LayoutTransform 一样会缩放描边),而且把 `RenderTransform` 完整留给
   `Spinner` / `Rotate` 的旋转。烘完缩放后 `Width/Height = size`、旋转原点仍是控件中心 `0.5,0.5`。
2. **动画只在真正跑起来的应用生命周期下启动**:`Lucide` 的转圈/滑动用 Avalonia `Animation.RunAsync` 实现,
   但外面套一层「`Application.Current?.ApplicationLifetime is null` 就直接不启动」。
   原因:离屏(`--smoke`)与测试进程里没有任何东西推进动画时钟,无限动画会一直挂着
   (C1a 第一次全量跑测试就是这么卡住的);`--smoke` 只渲一帧,本来也用不到动画。
   真机/桌面路径(`StartWithClassicDesktopLifetime`)不受影响。
   WPF 的 `Motion.Enabled` 门面属 C2 的 Services,本包用一个 `MotionEnabled` 静态属性顶着,
   值语义与 WPF 的"关掉动画就直接落位"一致(着色板颜色而不是渐变)。
3. **`Asset` 用 `Rectangle` + `OpacityMask = ImageBrush`**,与 WPF 的 `Rectangle.OpacityMask` 完全同构
   (Avalonia 11 的 `Visual.OpacityMask` 接 `IBrush`);PNG 走 `Avalonia.Media.Imaging.Bitmap`(Skia 解码),
   **没有**绕 `TipCL.Core` 的 `IImageCodec` —— Avalonia 自带 Skia,少一层间接。
   解码结果按工具名**缓存复用**(位图不可变,可共享);WPF 版每次 new 一个 `BitmapImage`,这一点是本包刻意改的。
4. **`ToolIcon` 内联了 `AiUiText.IsMcp` 的三行判定**(`mcp__` / `mcp.` / `mcp_`),
   因为 `TipCL.Pages.Ai` 是 C3 的事,图标这一层不许依赖页面命名空间;C3 落地 AI 页后可以改回调用 `AiUiText.IsMcp`。
5. **`Services\Ico.cs` 一并按字节级复制进来**:它不在 C1a 的编号清单里,但它是"内置图标字体要替代的那张字形表",
   而 `Ported\IconFontTests.cs` 的"源码里用到的码点必须在字体里"正是靠它才有意义(67 个字形常量)。
   若 Lead 认为该归 C1c,直接搬走即可 —— 它没有任何依赖。
6. **公开签名没有一处被改口味**:`Icon` / `Spinner` / `Asset` / `Rotate` / `Shimmer` / `ToolIcon` 的名字与参数默认值照旧;
   类型上 `Asset` 由 WPF 的 `FrameworkElement` 变成 Avalonia 的 `Control`,
   `Icon` / `Spinner` / `Rotate` 的 path 由 `System.Windows.Shapes.Path` 变成
   `Avalonia.Controls.Shapes.Path` —— 这是框架类型本身,不是签名设计。

## D-71 WPF 样式键 -> Avalonia 类名,映射表只放在 `StyleKeys.cs`

- Avalonia 没有 WPF 的「按 x:Key 取 Style 挂到控件上」这套机制,样式只能靠**选择器**命中。
  所以 C1b 为每个具名 WPF 样式键固定一个**类名**,规则:点换连字符 + 驼峰处插连字符 + 全小写
  (`Btn.Primary -> btn-primary`、`T.PageTitle -> t-page-title`、`ListItemRow -> list-item-row`)。
- 表只有一份:[src/TipCL.App/Themes/StyleKeys.cs](src/TipCL.App/Themes/StyleKeys.cs) 里的
  `Table`(40 个具名样式键)/ `Templates`(BtnTpl -> ControlTheme `BtnBase`)/ `ImplicitTypes`(13 个隐式类型样式)/
  `ResourceKeys`(9 个资源令牌)/ `Unsupported`(GroupBox)。四张表 + Unsupported 正好铺满 Windows 版的 64 个 `x:Key`。
- 隐式类型样式(`x:Key="{x:Type X}"`)不产生类名,在 axaml 里就是类型选择器;`Map` 对它们返回空串。
- `Controls.axaml` 里每条类选择器上方都写着 `<!-- WPF键 -> .类名 -->`,把映射留在原地可追溯;
  两条测试(静态层)逐条核对表、注释与选择器三者一致,键名写错会立刻红。
- 每个变体类**自带**基模板(类样式里 `Theme={StaticResource BtnBase}`),等价 WPF 的 `BasedOn`,
  所以调用点只加一个变体类就够。C1c 建议直接用 `StyleKeys.Apply(control, "Btn.Primary")`。

## D-72 控制模板的移植深度按「Avalonia 内部契约」分级

- **整份重画**(WPF 模板就是视觉本体、且 Avalonia 的部件契约已知):按钮族 12 个、三个 ToggleButton 变体、
  Input 家族 / Pw、Chk / Switch、Combo / ComboItem、ListItemRow、ToolTip、Sld。
- **只改属性 + 按 `/template/` 取命名部件**(Avalonia 的内部结构比 WPF 复杂、重画风险大于收益):ScrollBar、
  ProgressBar、Expander、TabItem、MenuItem、ContextMenu(只改前景色,外壳留给 Fluent)。
- 每一条差异都写进 PROGRESS.md 的 C1b「视觉差异」表(16 条),没有静默丢功能。
- 判据是「本机实测」而不是猜:开工时先用一次性探针(headless 下把 Fluent 默认主题的视觉树打出来)
  把各控件的模板部件名摸清(PART_ScrollViewer / PART_TextPresenter / PART_Popup / PART_Indicator /
  PART_LineUpButton / Track 的 PART_DecreaseButton 等),探针文件用完即删。

## D-73 Avalonia 不允许连续两个 `/template/`,Track 不是 TemplatedControl

- 实测 1:`<Style Selector="Slider /template/ Track /template/ Thumb">` 在运行期抛
  `InvalidOperationException: ControlTemplate styles cannot contain multiple template selectors.`
  也就是说**跨两层模板取部件是做不到的**,唯一的路是把外层模板整份重画,让部件落在同一层模板里。
- 实测 2:`Avalonia.Controls.Primitives.Track` **没有 `Template` 属性**(编译期报
  「Unable to resolve property Template on type Track」),它不是 TemplatedControl;
  WPF 的 `Track.Thumb` / `Track.DecreaseRepeatButton` 写法在 Avalonia 里对应
  `<Track.Thumb>` / `<Track.DecreaseButton>` / `<Track.IncreaseButton>` 三个属性元素,
  直接写在 Slider 的模板里。
- 实测 3:重画的 Slider 若不把 `Orientation` / `Minimum` / `Maximum` / `Value` 用 TemplateBinding 接到 Track 上,
  Track 会一直是 `:vertical`、`Value` 停在 0、滑块不动(探针实测 bounds 不变)。接上之后与 Fluent 的原生行为
  逐项对上(值 0→100 时滑块 x 从 0 到 182,原生是 0 到 180,差别只是 18px 与 20px 的滑块半径)。
- 结论写进 PROGRESS 的「遗留与风险」,后续包再遇到「想改模板深处某个部件」时先按这条判断要不要重画。

## D-74 TextBox 的模板部件名以 Avalonia 为准,编译期会强制

- WPF 的 `PART_ContentHost` 在 Avalonia 里叫 **`PART_ScrollViewer`**,并且模板里还必须有
  **`PART_TextPresenter`**(缺了直接编译失败:`Required template part with x:Name 'PART_TextPresenter'
  must be defined on 'TextBox' ControlTemplate.`)。
- `Text` 用双向绑定(`{Binding Text, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}`),
  其余(CaretIndex / Selection* / CaretBrush / PasswordChar / TextWrapping / TextAlignment / LineHeight /
  LetterSpacing)用 TemplateBinding;`TextPresenter` 没有 `MaxLines`(编译期报错后删掉)。
- 占位文字仍走 WPF 的做法:`Tag` 里放提示语,用 `StringConverters.IsNullOrEmpty` 绑 `IsVisible`。
- 滚动条可见性在 Avalonia 是 `ScrollViewer.VerticalScrollBarVisibility` 这类**附加属性**
  (不加前缀会编译失败),WPF 的 `TextBoxBase.*` 前缀不存在。

## D-75 `PasswordBox` / `GroupBox` 在 Avalonia 11 不存在

- 反射确认(`Avalonia.Controls` 里名字带 Box 的公开类型只有 AutoCompleteBox / CheckBox / ComboBox /
  ComboBoxItem / ListBox / ListBoxItem / MaskedTextBox / TextBox)。
- `Pw`(WPF 的 PasswordBox 样式)因此改成一份 `TextBox.pw` 类:复用输入框模板,密码掩码由调用点设
  `PasswordChar`。C1c 的 `Ui.Pw` 要相应改成返回 `TextBox`。
- `GroupBox` 隐式样式不移植:Windows 版全仓库 grep 没有任何 GroupBox 使用点,只在
  `StyleKeys.Unsupported` 里留档(带原因),测试专门钉住这一条且要求 axaml 里不许出现 GroupBox。

## D-76 时长/缓动令牌:WPF `Duration` -> `sys:TimeSpan`,`CubicEase` -> `CubicEaseOut`

- Avalonia 的时间线只吃 `TimeSpan`,`D.Fast` / `D.Med` 写成 `<sys:TimeSpan x:Key="D.Fast">0:0:0.13</sys:TimeSpan>`
  (`xmlns:sys="clr-namespace:System;assembly=System.Runtime"`),取值仍是 130ms / 220ms,用例对拍。
- Avalonia 没有带 `EasingMode` 的 `CubicEase`(缓动类型清单实测只有 CubicEaseIn/Out/InOut 这类),
  所以 `E.Out` 用 `<CubicEaseOut/>`:曲线与 WPF 的 `CubicEase + EaseOut` 完全一致,
  用例断言 `Ease(0.5) > 0.5` 且 `Ease(1) == 1`。
- `E.Out` 目前没有使用点(WPF 里它只给按钮/开关的退场动画用,而 Avalonia 这边用无过渡的伪类直切),
  留给 C2 的动画门面。

## D-77 字体令牌按 GOAL 的 mac 列表,Avalonia 的逗号字体列表能整串解析

- `F.Ui` = `PingFang SC, Hiragino Sans GB, Microsoft YaHei UI, sans-serif`、`F.Mono` = `Menlo, Consolas, monospace`
  (GOAL §3,覆盖 Windows 的写法);`F.Icon` = C1a 的 avares 图标字体 + Windows 的两个兜底字体族。
- 本机实测 Avalonia 11.3.23 的 `FontFamily` 构造函数会把逗号列表拆成 `FamilyNames`
  (4 个 / 3 个 / 3 个),`Name` 取第一个,所以兜底列表是**真的**被接受的,不需要退化成单个字体。
- 这带来一个**必须由 Lead 裁决的副作用**:隐式 TextBlock 样式一挂上 `F.Ui`,本机(Windows)第一个可用字体
  就成了 Microsoft YaHei UI,`--smoke` 窗口里那句 `TipCL` 的字形因此变化,PNG 从 3713/3669 变成 3956/3941。
  取证方式:临时注掉那一行 `FontFamily` -> smoke 逐字节回到 C1a 基准;还原后又是新值。
  本包**没有**改基准记录、也没有为了让 smoke 不变而删掉这一行(那是把移植做残)。

## D-78 C1b 的测试分两层:XML 文本层 + headless 运行时层

- **XML 文本层**(不需要平台,`C1bControlsTests` / `C1bStyleKeysTests`):64 个 Windows 键的落点、
  40 个类名映射与注释/选择器三者一致、色板引用穷尽扫描(每个 `B.*`/`C.*` 都要在两份色板里存在)、
  令牌取值、WPF 残留语法(只看标记、不看注释,注释里本来就为了说明差异提到了那些词)。
  Windows 源路径可用时另有一条交叉复核,不可用只打印原因、不判失败。
- **headless 运行时层**(`C1bRuntimeStylesTests`,复用 `C1aFixtures.EnsureAvalonia()` 的
  `SynchronizationContext` 还原):样式表真加载、类选择器真命中、ControlTheme 真被换上、
  `PART_TextPresenter` / `PART_Popup` 等部件真被框架找到、Track 的值真的从 Slider 流过去。
- **纪律**:样式里有 `Transitions`(淡化)的地方**不断言动画中的值**,只断言没有过渡的属性
  (变换、可见性、尺寸),否则头两帧的中间值会把用例抖红;全局静态
  (`Application.RequestedThemeVariant`)在 `Hosted.Dispose` 里还原。
- 写用例时踩到 `xUnit2013`(`Assert.Equal(0, X.Count())`),已按分析器建议改成 `Assert.Empty`,
  现在全仓警告只有 NU1900 与 SYSLIB0014。

## D-79 测试里一切 Avalonia 工作都走 `AvaloniaSession`(专用 Dispatcher 线程)

- **根因(C1b-fix 实测定性)**:`AppBuilder.SetupWithoutStarting()` 把 `Dispatcher.UIThread` 绑在
  **第一个调用它的线程**上,而 `AvaloniaObject.VerifyAccess()` 只认那一个线程。xunit v2 走 VSTest
  (testhost)通道时每个测试类跑在自己的 worker 线程上,于是「哪个类先碰到 Avalonia,哪个类就独占
  Dispatcher 线程」,其它类里构造控件(`new Button()` 就进 `AvaloniaObject..ctor`)必抛
  `System.InvalidOperationException : Call from invalid thread`。
- **这条路径是调度相关的,所以同一份代码会时绿时红**(本机实跑:同一命令 3 次 -> 93 失败 / 0 失败 /
  95 失败;失败的类随「谁抢到 Dispatcher 线程」变,亲眼见过 56+30+7 与 Lead 报的 56+7+2 两种分布)。
  进程内执行器全程在主线程上跑,所以它一直是绿的 —— 这不是「另一条通道更严」,而是它根本没撞上这个问题。
- **设计**:`tests/TipCL.Tests/AvaloniaSession.cs` 用一个**后台线程**起平台 + 泵循环
  (取队列 -> `Dispatcher.UIThread.RunJobs()` -> 歇 2ms),用例体经 `Run<T>(Func<T>)` / `Run(Action)`
  在该线程上执行并同步取回结果(`ManualResetEventSlim`,上限 30s,超时抛 `TimeoutException`);
  异常用 `ExceptionDispatchInfo.Capture(ex).Throw()` **原样重抛**(类型/消息/原始栈都保留,
  xunit 报的是真实断言消息,不是包装异常)。
- **纪律**:`SetupWithoutStarting()` 在整个测试工程里**只有 AvaloniaSession 一处**调用;
  `C1aFixtures.EnsureAvalonia()` 退化成 `AvaloniaSession.Ensure()` 的转发(保留给 `Hosted.Show` 等老调用点)。
  用例体必须**同步** —— 在会话线程上阻塞等一个要把续体投回同一个 Dispatcher 的 Task 会自锁。
- **不引新依赖**:没有用 `Avalonia.Headless.XUnit`(进程内执行器不展开 collection / fixture 特性,见 A6),
  也没有依赖 `IClassFixture` / `ICollectionFixture` —— 会话是纯静态入口,直接反射调用照样工作。
  将来若要引它,前提是先让进程内执行器支持 collection/fixture,否则两条通道不再可比。
- **为什么不把「测试里的 Avalonia 工作」放在测试线程上、只让平台线程跑泵**:那样 `VerifyAccess` 仍在测试
  线程上抛;Dispatcher 线程与「构造控件的线程」必须是同一个,没有第三种写法。
- **实测证据**:修完后 `dotnet test` **连跑 12 次全绿**(修复前 3 次里有 2 次红);两条通道各连跑 2 次都是
  2157/2157、0 失败。

## D-80 `Ui.S(string)` 的返回类型由 WPF 的 `Style` 改成类名字符串

- **为什么不能照抄**:WPF 的 `Style = S("Btn.Primary")` 是"按键取一份 Style 挂到控件上";Avalonia 没有这套机制
  (样式靠选择器命中,`Control` 上既没有 `Style` 属性,`Style` 对象也没法"给控件加一个类")。
- **两条候选**:(a) 仍然返回一个 `Avalonia.Styling.Style` —— 那只能是空壳,什么也不做,等于把功能桩掉
  (GOAL §2.5 明令禁止);(b) 如实返回 C1b 的类名,调用点用 `StyleKeys.Apply(control, "Btn.Primary")`
  或 `control.Classes.Add(Ui.S("Btn.Primary"))`。
- **选了 (b)**:`Ui.S("Btn.Primary") == "btn-primary"`,认不出的键返回空串(与 `StyleKeys.Map` 一致,不抛)。
  这属于"公开面被迫变化",已逐条写进 PROGRESS.md 的 C1c 差异表。
- **代价 / 给 Lead 的提醒**:C2..C8 的页面不能照抄 Windows 的 `Style = S(x)` 写法(会编译不过),要用
  `StyleKeys.Apply`。若 Lead 认为该保留 `Style` 返回类型,回退成本很小(改签名 + 把 `Ui` 内部的
  `StyleKeys.Apply` 换成别的形态),但那样就有一个"什么也不做的门面方法"留在公开面上。

## D-81 `Pw` 返回 `TextBox`;`Check` / `Switch` 用 `IsCheckedChanged`

- `Pw` 按 D-75 的裁决改成 `TextBox` + `PasswordChar`,掩码字符取 WPF `PasswordBox` 的默认值 `●`(U+25CF),
  调用点若要别的字符直接改 `PasswordChar`;类名仍是 C1b 的 `Pw -> .pw`。
- `CheckBox.Checked` / `Unchecked` 在 Avalonia 11.3 上**已标记 `[Obsolete]`**(实测 CS0618 ×4),
  等价且不过时的写法是 `ToggleButton.IsCheckedChanged`(`EventHandler<RoutedEventArgs>`,签名相同),
  所以 `Check` / `Switch` 的 `changed` 参数接它 —— **公开签名一个字没改**(WPF 侧本来就是两个事件都接)。
- 验收口径里的"警告只许 NU1900 + SYSLIB0014 ×1"因此保持,没有扩 `NoWarn`、也没有用 `#pragma`。

## D-82 `SmoothScroll` / `WindowPctOf` 在 Avalonia 上的四处框架差异

1. **`Offset` 与基类同名**:Avalonia 的 `ScrollViewer` 自带 `Vector Offset`,而 WPF 版的
   `SmoothScroll.Offset` 是 `double`;保持 WPF 的公开类型,用 `new` 显式隐藏基类成员(类型不同,不能 override)。
   `OffsetProperty`(静态字段)同样 `new`。设值经 `OnPropertyChanged` 转成基类的 `Vector`(等价 WPF 的
   `ScrollToVerticalOffset`)。
2. **`Window.ActualHeight` 在 Avalonia 不存在**(反射确认:只有 `Visual.Bounds`),所以
   `Ui.WindowPctOf` 用 `Bounds.Height`,`BindWindowMaxHeight` 的绑定路径写 `Bounds.Height`。
3. **`CanContentScroll` / `PanningMode` 没有对应属性**:Avalonia 本来就是像素滚动,触控板平移归平台手势层,
   这两项没有移植(不影响滚动本身)。
4. **滚动上限**:WPF 读 `ScrollableHeight`,Avalonia 用 `Extent.Height - Viewport.Height`。

## D-83 骨架屏与平滑滚动的动画暂时挂在 `Lucide.MotionEnabled` 上

- WPF 用 `Motion.Loop`(骨架横扫)与 `Motion.Enabled`(滚轮平滑);**mac 侧的 Motion 门面属 C2**(D-70),
  所以 C1c 复用 `Lucide` 已经暴露的 `MotionEnabled` 开关,并沿用 Lucide 那条纪律:
  **只在真正跑起来的应用生命周期下起动画**(`Application.Current?.ApplicationLifetime is null` 就不起)——
  离屏(`--smoke`)与测试进程里没有东西推进动画时钟,无限/长动画会把收尾挂住(D-69 的实测教训)。
- 被跳过动画时:Skeleton 得到一块静态灰条(等价 WPF 关掉动画),SmoothScroll 退回 `base.OnPointerWheelChanged`
  的系统滚动。**没有静默少功能**:两者都是"关掉动画时的样子"。
- C2 落地 `Motion` 后应该把这两处改成走 Motion(已写进 PROGRESS.md 的 C1c 遗留)。

## D-84 `Ui.OpenUrl` 走 `Crash.ShellOpen`,失败只有调试输出

- mac 的"用系统程序打开"已经在 B1 落地(`TipCL.Core.Launcher.Crash.ShellOpen` 的非 Windows 分支就是
  `open <url>`),所以 `OpenUrl` 直接转调它,并**不再自己 `Process.Start`**(与红线"测试不许起进程"也天然一致)。
- WPF 版失败时弹 `AppServices.Toast`;mac 版的 Toast 宿主属 C2,门面这一层不许依赖外壳,所以退化成
  `Debug.WriteLine`(**不抛异常、不改状态**)。C2 接线 Toast 后应把这一行换成真提示。

## D-85 C1c 附带搬进来的两个小文件与两个可注入重载

1. **`src\TipCL.App\Clamp.cs`**:Windows 位置是工程根 `src\TipCL\Clamp.cs`(namespace `TipCL`),
   是 `Ui.Sld` 夹值与 `SmoothScroll` 夹滚轮目标用的泛型小工具,**字节级复制**(ASCII,无 WPF 依赖)。
   它不在任务书的编号清单里,但 `Ui.cs` 依赖它 —— 若 Lead 认为该归别处,直接搬走即可。
2. **`src\TipCL.App\UiPrefs.cs`**:任务书点名要的纯 POCO,字节级复制。
3. **两个 internal 可注入重载**(只为单测,不改公开面):
   `Ui.WindowPctOf(pct, floor, hostHeight)`(纯算术)与
   `SmoothScroll.BindWindowMaxHeight(host, pct, floor)`(离屏/测试进程里 `ApplicationLifetime` 是 null,
   公开入口拿不到宿主窗口)。公开入口转调它们,行为不变。

## D-86 C2a:外部 Python 桥的边界与 `FindRoot` 的落点(秒级可查,不再有第二种根)

- **背景**:任务书要求 mac 版只去掉外部 Python 桥,进程内 `BackendApi` + `RpcDispatcher` +
  `BridgeServer` + `BridgeClient` 原样保留。
- **删掉的分支**(逐条列在 PROGRESS.md 的 C2a 第 3 条表里):`StartExternalAsync` 整段、
  `TIPCL_BRIDGE`/`TIPCL_BRIDGE_EXE` 两条早退、`FindPython`/`PythonCandidates`/`RegisteredPythons`/
  `ProbePython`、`FindNativeBridge`/`LooksLikeRoot`/`WalkUp`/`QuoteArg`/`Tail`/`PortRegex`/`KillTree`、
  `Payload.EmbeddedPython()`、以及只有外部桥用的那个 `BridgeHost(BridgeClient, Process, ...)` 私有构造。
- **`FindRoot` 的替代**:Windows 会先看 `TIPCL_HOME`,再往上找"带 `bridge\server.py` 或
  `native\data\catalog.json`"的 payload 根,最后落 `Payload.EnsureRoot()`。mac 侧没有 payload 根,
  于是直接 `NormalizeRoot(Utils.ResolveRoot())` —— GOAL §3 的数据目录(`~/.tipcl`,`TIPCL_HOME` 可覆盖)。
  隔离测试进程里 `TIPCL_HOME` 指向 `TestHome.Root`,**有用例钉住这条等式**。
- **`Payload` 的处境**:按任务书清单搬了,但 `DefaultRoot` 由 `%LOCALAPPDATA%\TipCL` 改成
  `Utils.ResolveRoot()`。不改的话在 mac 上会得到一个 `~/.local/share/TipCL` 的**第二数据根**,
  而且 `EnsureRoot()` 里的 `CleanupLegacy` 会在那个根下删 `mclauncher` —— 都不该发生。
  改完之后它在 mac 侧**没有调用点**(Windows 是 `FindRoot` 的最后兜底),这是事实上的死代码;
  它是 `internal` 类,删了不影响公开面(留给 C2b 决定,已写进遗留)。

## D-87 C2a:`I18n` 的词表来源改成 Core 内嵌资源;`I18nCheck` 不搬

- Windows 的 `I18n.EnsureTables()` 从磁盘读:先 `root\mclauncher\locales`,开发态回退
  `root\src\TipCL\Resources\locales`。mac 版没有外部 payload,词表按 GOAL **内嵌进 `TipCL.Core`**
  (LogicalName 与 Windows csproj 一致,A2 已落地),所以改成 `CoreResources.ListDir("locales")` +
  `ReadText` —— 与被搬进 Core 的 `I18nCore.LoadStrings()` **同一份数据、同一套资源名**。
  用例里做了一次交叉对拍:`I18n.L(k)` 必须等于 `I18nCore.Tr(k)`(同一个键、同一种语言)。
- **`I18n.LocalesDir` 的语义变了**(公开成员名/类型不变):现在报 `TipCL.Core.Resources.locales`
  这个资源位置,因为 mac 上没有磁盘词表目录(*如实反映*比硬塞一个假路径强)。
- **`I18nCheck.cs`(469 行)没有搬**:它是开发期的源码扫描器,依赖 `PyPath.GetRelativePath`
  与 Windows 的仓库布局(`root\src\TipCL`),不满足任务书"小且自足"的条件。
  但 `I18n.L()` 会调它的 `HasCjk`,所以把这**一个助手按原样搬进 `I18n`**(internal)。

## D-88 C2a:`AppServices.Window` 的静态类型与 `Toast` 的宿主委托

- `public static MainWindow? Window` 在 mac 上**编译不过**:mac 的 `MainWindow` 是 `internal`
  (CS0053:公开成员不能暴露 internal 类型),而 `MainWindow.cs` 属 C2b、本包不许改。
- 取舍:保留成员名 `Window`,静态类型放宽成 Avalonia 的 `Window`。同程序集里外壳与页面写
  `AppServices.Window as MainWindow` 照样能拿到外壳(C2b 落地后即可)。
- `Toast` 不再直接 `Window?.Toast(...)`(外壳类还没有这个方法),改走新增的 **internal**
  `ToastSink` 委托;宿主为空时 `Debug.WriteLine` —— 与 D-84 的 `Ui.OpenUrl` 同一口径
  (不抛异常、不改状态)。代价:**外壳接线前所有 Toast 都是静默的**(已写进遗留)。

## D-89 C2a:`Dlg` 的文件对话框走 `IStorageProvider` 的同步等待 + 过滤器解析

- WPF 的 `OpenFileDialog`/`SaveFileDialog`/`FolderBrowserDialog` 是同步的;Avalonia 只有
  `IStorageProvider` 的异步 API。mac 侧选了**最保守的等价物**:提交任务后一边
  `Dispatcher.UIThread.RunJobs()` 一边 `Thread.Sleep(1)` 等它完成(上限 10 分钟),
  即"自己实现 WPF 的嵌套消息循环"。公开签名一字未改。
- 过滤器串照旧是页面的 WPF 语法(`标签|模式;模式`),新增 `internal static FilePickerFileType[]
  ParseFilter(string)` 转成 Avalonia 的类型;`*.*` 映射成 `*`(Avalonia 的"全部文件")。
  这是个纯函数,有用例逐条钉住。
- **拿不到提供者就返回 null**(离屏 / 测试 / 还没起窗口),只写调试输出:不抛、不起进程、不弹窗。
  离屏那两条自动取消挂点(`Dlg.SmokeActive` / `SmokeAutoAnswer` / `SmokeAutoCancelSystemDialog`)
  是可注入的,因为 mac 的 `Smoke\` 里没有对应的入口,而 `Smoke\` 不属本包。

## D-90 C2a:`Motion.Enabled` 合并"用户开关 + 有没有时钟";`Lucide.MotionEnabled` 变成转发别名

- WPF 的 `Motion.Enabled` = 用户开关 && `SystemParameters.ClientAreaAnimation` && 非远程会话。
  Avalonia 没有 `SystemParameters`,而本项目早就有一条更硬的纪律(D-69/D-83):
  **只有真桌面生命周期里才起动画**(离屏/测试里没人推进时钟,无限动画会把收尾挂住)。
  于是 mac 的 `Enabled = _enabled && (Application.Current?.ApplicationLifetime is not null)`,
  `UserEnabled` 仍是纯净的用户开关,`set` 的语义(值变了才发 `EnabledChanged`)与 Windows 一致。
- 所有成员在 `!Enabled` 时**立刻落终态**(不是什么都不做):与 WPF 关掉动画时的样子一致,
  也不会把控件留在 `Opacity = 0` 的中间态。副作用是**关闭浮层变成同步的** —— 有用例利用这一点。
- `Lucide.MotionEnabled` 由「临时替身」改成 **`Motion` 用户开关的转发别名**(读 `UserEnabled`、
  写 `Motion.Enabled`),名字/类型不变 → 全应用只剩一个动画开关;`Lucide.Shimmer` 的无限高光动画
  改成走 `Motion.Loop`(与 Windows 版一一对应)。
- **`Ui.Skeleton` 与 `SmoothScroll` 没改**:任务书开头明确"不许改 `Ui` 门面",这两处的 Motion 接线
  留给 C2b(它们现在读的是那个转发别名,行为与 C1c 完全一致)。
- 逐帧缓动(Avalonia 的 `KeyFrame` 只有 `KeySpline`、`BackEaseOut` 没有 `Amplitude`)收成整段缓动,
  用 `CubicEaseOut` / `SineEaseInOut` 近似 WPF 的 `CubicEase` / `QuadraticEase`。

## D-91 C2a:桥的用例用 `TcpListener` 假后端;全角标点在"键"上又现形两次

- 桥的请求/响应帧与 SSE 是真的走 socket 验的:自建 `FakeBridgeServer`(`TcpListener` 绑
  `IPAddress.Loopback:0`,**不是 `HttpListener`**,D-27),按预置顺序回包并把请求原文(头 + 体)
  记下来供断言;每个用例 `finally` 里 Dispose。**绝不实例化 `BackendApi`/`BridgeServer`/`PluginManager`,
  绝不调 `BridgeHost.StartAsync`**(D-23);`BridgeEvent` 的反序列化另有纯 `JsonSerializer` 用例。
- **D-58(全角标点抄不进字面量)这次踩在"键"上,两次**:
  1. `TaskStore` 的默认状态键是 `排队中…` —— 省略号是 **U+2026**,不是三个 ASCII 点;
  2. `FileKinds.ModpackProbe.LoaderLabel("")` 的键用**全角括号** U+FF08/U+FF09。
  两条都是"Expected 与 Actual 打印出来一模一样"的失败,只有转成 `\uXXXX` 才看得出差别。
  **教训:凡是与词表键比较的字面量,一律用 `\uXXXX` 写,或改成比较 `I18n.L(同一个键)`。**
- 另外记两条写用例时的常识坑:`Fmt.Duration` 的小时分支只覆盖 `< 24 小时`(25 小时走天分支);
  `BridgeEvent.Payload` **不是**普通反序列化填的(真路径上由 `BridgeClient.Dispatch` 把根元素
  `Clone()` 塞进去),纯反序列化用例要自己在 JSON 里带 `payload` 字段。

## D-92 C2a:`Models\` 只搬 `CrashReport.cs` + `CrashAction.cs`

- `BridgeEvent.Crash` 的类型是 `TipCL.Models.CrashReport`,而 mac 侧整个 `Models\`(45 个 DTO)
  属页面阶段(C3+)。本包只搬这两个自足的文件(只依赖 `System.Text.Json` /
  `System.Collections.Generic`),为的是保住 `BridgeEvent` 的公开成员一个不缺。
- **后续搬 `Models\` 整目录时不要再搬这两个**(会重复定义)。

## D-93 C2b:离屏状态那个静态类在 mac 侧叫 `SmokeState`(不叫 `Smoke`)

- Windows 里它是 `TipCL.Smoke`(根命名空间下的静态类),而 mac 侧 `TipCL.Smoke` **已经是命名空间**
  (`SmokeOptions` / `SmokeRunner` 在里面)。同名类型与同名命名空间并存会直接编译失败
  (本机实测 `error CS0101: 命名空间"TipCL"已经包含"Smoke"的定义`)。
- 处理:成员名照抄(`Active` / `Note` / `OperationStarted` / `OperationFinished` /
  `AutoAnswerDialog` / `AutoCancelSystemDialog`),类名改成 `SmokeState`,落在同一个 `TipCL.Smoke`
  命名空间里。**C3 页面里 `Smoke.X` 一律写 `SmokeState.X`**(本包已把 `PageBase` 里的两处改好)。
- 没有去动 Windows 里那份 `Smoke.cs` 的其它成员(离屏报告落盘、进程守护) —— 那些属 D1。

## D-94 C2b:页面工厂走 `PageRegistry`;`NavDef.Create` 可空

- Windows 的 `MainWindow.NavDefs` 直接把页面类型写死(`() => new LaunchPage()`)。mac 版按 GOAL 是
  一页一包(C3+),C2b 时页面类还不存在,所以把"页面 id → 建页工厂"抽成
  `TipCL.Pages.PageRegistry`(注册/注销/快照/还原),`NavDef.Create` 在 mac 侧允许为 null。
- 为什么不直接 `new` 一个占位页:那等于凭空造页面(任务书明确不许),而且 C3 还得再删。
- 语义仍然与 Windows 对齐:`GetPage(未知 id)` 返回 null(不抛),`Navigate(未知 id)` 静默不切页;
  id 一律**序号比较**(大小写敏感)。
- C3 用法:`PageRegistry.Register("launch", () => new LaunchPage());`(`NavDef.Create` 那一格也留着了,
  想写死工厂的包可以直接填)。

## D-95 C2b:`AppServices.ToastSink` 仍然留空(不画 C2c 的 Toast 卡片)

- 任务书给了两条路:"最小可用的窗口内 Toast 宿主"或"留调试 sink 并说明"。选后者:
  Windows 的 Toast 卡片(`MainWindow.Toast`,含 4 张上限、图标/颜色按 kind、进出场动画)是
  **C2c 的视觉件**,本包画一个最小版等于凭空造视觉,还会白改一次冒烟像素。
- 现状:`AppServices.Toast(...)` 在没有宿主时只写 `Debug.WriteLine`(不抛、不改状态,与 D-84 的
  `Ui.OpenUrl` 同一口径);主窗口里那个 `ToastHost` 面板是空占位,给 C2c 用。
- 影响:C2b/C3 期间页面弹 Toast 看不到东西(只在调试输出里);这是**已知且刻意**的降级。
  C2c 把外壳的 Toast 方法挂到 `ToastSink` 之后,把 `C2bWiringTests` 里那条"留空"的断言改掉即可。

## D-96 C2b:**不启动后端**;页面加载由 `MainWindow.BridgeReady` 把关

- C2a 的验收说 `EnsureCore` 的真实启动"归 C2b 的接线验收或 D1"。本包选**不做**,理由:
  1. 任务书给 C2b 的接线清单里没有它(列的是 `Window` / `ToastSink` / `Dlg.Host` / `BlurTarget` /
     `Motion.Enabled` / 三个离屏挂点);
  2. 按 D-23,这条路径在测试里不许跑;本机又无法验证"真跑起来"的界面行为,做了也只能标未验证;
  3. 无人值守下让外壳在启动时拉起 `BackendApi` + `BridgeServer`(网络预热、插件扫描、MCP)
     风险大于收益,而且会把 `--smoke` 的确定性搭进去。
- 结果:`MainWindow.BridgeReady` 默认 false,页面能切、能显示,但**不**跑 `LoadAsync` / `OnShown`
  —— 这正是 Windows"还没连上后端时不加载页面"的状态(Windows 的 `_bridgeReady` 门)。
  后端接线包只要 `window.BridgeReady = true` 就能打开总闸;这条已写进 PROGRESS 的接线表与遗留。

## D-97 C2b:侧栏是 mac 侧按 `NavModel` 重建的;标题栏右侧不画圆片;Dock 不搬

- Windows 现状是顶栏 5 页签(侧栏已从可视树撤掉),但任务书明确要"侧栏 + 宽度取
  `UiPrefs.SideWidth`",所以侧栏按 `NavModel.NavItemsFromConfig` 的行表重建(分组头 / 条目的键、
  标签、顺序、隐藏规则全部与 Windows 一致),宽度夹在 `SideMinWidth=140` ~ `SideMaxWidth=320`。
  视觉只复用 C1b 已有的 `Tab.Top`(`tab-top`)类选择器,**没有新增类名、没有改样式表**。
- 标题栏左边照 Windows 画 logo + "TipCL",并让出 78px 给系统红绿灯;**右边什么都不画**:
  Windows 的 AI / 主题 / 账号圆片与三个 caption 按钮都不要(任务书:不画 Windows 式按钮)。
  主题切换与 AI 入口因此放进系统菜单的「操作」里。
- 底部的下载任务条(Dock,`BuildDock`/`SyncDock`)与 Toast 一样属视觉件,**不搬**,留给 C2c。

## D-98 C2b:两个 Motion 遗留点的改法;"动画开着"那条分支在测试进程里不可达

- `Ui.Skeleton`:Windows 就是 `Motion.Loop(起横扫, 停横扫)`,改成一样(新增私有 `StartGlide`);
  关掉动画时**两分支都不走**(`Motion.Loop` 只在状态翻转时回调),横扫停在起点 -90 = 静态灰条。
  公开签名一字未动。
- `SmoothScroll`:守卫改回读 `Motion.Enabled`(用户开关 && 有时钟),并抽了一个 internal 的
  `ShouldSmoothWheel` 让两条分支能被告知式地验;`AnimateTo` 在没有时钟时**直接落终态**
  (把偏移设到目标),与"关掉平滑"的观感一致。公开签名一字未动。
- `Lucide.MotionEnabled` 的转发**保持 C2a 的样子**(读 `Motion.UserEnabled`、写 `Motion.Enabled`):
  改成读 `Motion.Enabled` 会把 C1a 两条既有用例打红(它们在无时钟进程里把用户开关置 true 并断言
  "渐变刷子被建出来"),而"不许改弱既有断言"是红线。本包只补了两条钉住它语义的用例。
- `Motion.Enabled` 需要 `Application.ApplicationLifetime != null`,而 Avalonia **初始化之后禁止再改它**
  (实测抛 `InvalidOperationException: It's not possible to change ApplicationLifetime after
  Application was initialized`),C1b-fix 又只允许 `AvaloniaSession` 一处起平台 ⇒
  "动画开着"那条分支在测试进程里结构上不可达,只能靠真桌面运行验证。已列入未验证项。

## D-99 C2b:偏好文件名仍是 `wpf-ui.json`;`--smoke` 注入默认偏好、不读盘

- `UiPrefs` 与 Windows 同名同文件(`~/.tipcl/wpf-ui.json`,数据目录由 `BridgeHost.FindRoot()` 给),
  没有改成 mac 风味的新名字:字段与语义与 Windows 完全一致,换名只会让 C3 的偏好读写对不上。
- `MainWindow(prefs)` 允许注入:`--smoke` 与测试注入一份默认 `UiPrefs`(离屏像素不随用户数据变),
  只有生产路径才读盘(只读,读不到/读坏一律回默认值,不抛)。
- `SaveUiPrefs()` 保留但**不在启动 / 切主题时自动调用**:避免在离屏与测试里碰用户数据;
  将来由设置页(或收尾时的显式保存)调用。

## D-100 C2d:`BackendApi.DisableAuthWarmup` —— 唯一一处 Core 改动的形状与理由

- **背景**:B1a 验收时 Lead 的裁决是「C2 做 BridgeHost 时给 `BackendApi` 加一个可关的开关(默认行为与
  Windows 版一致)」。C2d 落地:**默认 false(预热照发,与 Windows 一致)**,只有测试/离屏显式置 true。
- **形状**(`src\TipCL.Core\BackendApi.cs` 一个文件、两处 hunk、**+20/-4 行**,其中 15 行是文档与空行):

  ```csharp
  // 新增在读实例构造的上一行
  public static bool DisableAuthWarmup { get; set; }            // 默认 false
  internal static bool ShouldWarmupAuth() { return !DisableAuthWarmup; }

  // 构造函数里那 4 行原样保留,只是被守卫包住(缩进 +1 层)
  if (ShouldWarmupAuth())
  {
      ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(1500); AuthHttp.Warmup("https://login.live.com/"); });
  }
  ```

- **为什么用属性而不是环境变量**:环境变量是进程级、跨程序集可被任何人顺手设上,而且"谁设的、有没有
  还原"在用例里看不见;属性可以在用例的 `finally` 里精确还原,也能被静态判断函数(
  `ShouldWarmupAuth()`)单独钉住两个分支。测试必须成对使用(置 true → 用完还原),本包四个测试类
  都遵守,并在 `C2dFixtures.Install/Restore` 里统一保存/还原快照。
- **开关只关"构造时的自动预热",不关"用户点了微软登录"**:`start_microsoft_login()` 里那次
  `AuthHttp.Warmup(...)` 一字未动(用例按源码形状钉住了这一点:全文件守卫只有 1 处、预热调用共 2 处)。
- **没有别的 Core 改动**:`git diff --name-status a84c8c6 HEAD -- src/TipCL.Core` 只列
  `src/TipCL.Core/BackendApi.cs`。

## D-101 C2d:测试怎么"安全地"起真后端 —— 两个注入缝 + 一处必须换掉的收尾

- `BridgeHost` 上新增两个 **internal 注入缝**(生产代码永远不写它们,默认值就是生产行为):
  1. `ApiFactory`:**默认 `new BackendApi(bus)`**;测试换成「把 `DisableAuthWarmup` 置 true 再构造」的
     工厂 —— 于是整条启动路径是真的(真 BackendApi / 真 BridgeServer / 真回环 socket / 真 RpcDispatcher),
     唯独不发那次登录预热;
  2. `OnCoreStarted`:默认是生产那一步(`McpServer.StartIfEnabled` + `PluginManager.StartGlobal`);
     **测试必须注入空实现**。理由是实测读代码:`PluginManager.StartGlobal` 起的后台线程
     300 ms 后 `StartAll()` → `Market.CheckUpdates(refresh: true)`,**5 秒后会去插件市场拉更新(联网)**,
     而"测试绝不碰真网络"是红线。所以本包验的是"除了这一步之外的全部启动路径",这一步只能由真桌面运行覆盖。
- 另加 `BridgeHost.ResetCore()`(**只有测试收摊用**):清掉进程内内核单例缓存,好让每个用例都从干净状态起;
  生产路径下内核按 Windows 语义活到进程结束(与 `BridgeServer.Stop` 不动内核一致)。

## D-102 C2d:`BridgeReady` 由真启动路径打开;失败保持 false 并留原因

- `MainWindow` 新增 `ConnectAsync(BackendBootstrap?, ct)`(Windows `MainWindow.ConnectAsync` 的前半截):
  `BridgeHost.StartAsync` 成功才 `AppServices.Host/Client = ...` + **`BridgeReady = true`**;
  失败时 `BridgeReady` 保持 false、原因写进 `BridgeError` 与调试输出(绝不静默 ready)。
  生产入口是 `MainWindow.StartBackend()`(`App.OnFrameworkInitializationCompleted` 的桌面分支里调一次,
  fire-and-forget),关窗时挂 `Closed → StopBackend()` 收摊。
- **离屏 `--smoke` 不走这条路**:`SetupWithoutStarting()` 不进桌面生命周期,所以冒烟像素与后端无关
  (实测:light/dark 与 C2b 基准逐位相同)。
- 连上之后照 Windows 做一次"补载当前页"(`ReloadCurrentAsync` 的等价物)。它**碰页面**,所以必须回到
  UI 线程:`ConnectAsync` 的续体在线程池上(用 `ConfigureAwait(false)` 换来的 —— 同步等待版
  `ConnectBlocking` 因此能在 AvaloniaSession 线程上安全使用),于是走
  `Dispatcher.UIThread.Post`;`CheckAccess()` 为真时直接跑。
- `ConnectBlocking(...)` 是**测试/离屏同步入口**(用例体跑在会话线程上不能 await),标注清楚只给它用。

## D-103 C2d:用例发现并修掉的真缺口(取消启动会漏监听器)+ 测试隔离的三处纪律

1. **真缺口**:`BridgeHost.StartInProcessAsync` 的健康检查循环里,`await Task.Delay(100, ct)` 在 ct 被取消时
   会把**已经起来的监听器与客户端**一起漏在进程里(方法直接抛出去,没走到后面的 `client.Dispose();
   srv.Stop();`)—— 监听 socket 与 `bridge-accept` 线程会永久留下。
   用例 `Stop_during_start_cancels_and_leaves_no_listener` 先复现(端口还在监听表里),再修:
   给那次 `Task.Delay` 包一层 `catch (OperationCanceledException) { client.Dispose(); srv.Stop(); throw; }`
   (纯插入 8 行,**没有改动原有代码的缩进或语义**)。
2. **四个 C2d 测试类归到同一个 xunit collection**(`C2dFixtures.Collection`):它们都临时改写进程级静态
   注入缝;xunit 默认按类并行,两个 C2d 类并行时会互相把对方的内核工厂换掉(VSTest 通道实测红,
   进程内执行器串行看不到)。
3. **不写"取决于别的用例先跑过什么"的断言**:`create_instance` 的落点在单根模式/多实例模式下形状不同
   (单根模式补齐根实例标准目录,多实例模式新建同名目录),VSTest 通道里因为并行而两种都出现过;用例因此
   改成"两种形状都接受,但落点必须在隔离目录之下"。同理,SSE 首条事件是握手 `hello`
   (BridgeServer.cs:993),用例只认 `ui_changed`。
4. **用例体一律 `async Task` + `await`**(不用 `.GetAwaiter().GetResult()`):否则 xUnit1031 会给 66 条警告
   (实测),而"NU1900 之外只允许 SYSLIB0014 一处"是硬要求。Avalonia 用例体仍必须同步(那条走
   `AvaloniaSession` + `ConnectBlocking`)。

## D-104 C2c1:任务书点名的两个文件与仓库里的实际内容不符(如实记录,不擅自改口径)

- 任务书写「port `Services/Bubble.cs`(the toast bubble)」与「port `Pages/ConfirmCard.cs`(the
  confirm/message card)」。实测 `D:\项目\tipcl\windos\tipcl\src\TipCL\`:
  1. **没有** `Services\Bubble.cs` 这个文件。`Pages\Bubble.cs`(454 行)是 **AI 对话气泡**:
     `<think>` 折叠、Markdown 渲染(`MdView`)、编辑 / 撤回 / 重发 / 投票,依赖
     `AiIcons` / `ThinkFold` / `AiPage` —— 属 AI 全家(C7+);
  2. `Pages\ConfirmCard.cs`(271 行)是 **AI 工具审批卡**「助手要执行一个操作,需要你确认」,
     只被 `Pages\AiPage.cs:2614` 与 `Pages\AiFixWindow.cs:761` 用到,依赖
     `AiRisk` / `ToolMeta` / `AiUiText` / `AiIcons` / `MdView.Mono` —— 同样属 AI 全家。
     **它不是 `Dlg` 的卡片**;
  3. **真正的 Toast 卡片**是 `MainWindow.xaml.cs:1179-1255` 的 `MainWindow.Toast`(块内建的 Border),
     Windows 侧没有把它做成独立类;
  4. **真正的"确认 / 消息卡片"**是 `Services\Dlg.cs:185-213` 的 `Shell`(`Confirm` / `Ask` /
     `Alert` / `Prompt` / `Choose` / `Form` 共用),C2a 已经**逐行等价**地移植进 mac 的 `Dlg.cs`。
- **取舍(无人值守下最保守的一支)**:按"描述"而不是"文件名"办事 —— 移植 3 与 4;
  **不**移植 1、2。理由是它们只能连带移植 `AiIcons` / `AiRisk` / `AiUiText` / `MdView` 等 AI 页
  基础设施才编译得过,而任务书同时写着「do NOT port any page」、红线也写着"不许移植任何页面":
  **这两条要求在 `ConfirmCard.cs` 上是直接冲突的**,而"不搬"这一支不违反任何一条红线。
- **留给 Lead 裁决**:若确实要 C2c1 就搬 AI 审批卡,需要先派一个"AI 视觉基础设施"小包
  (`AiIcons` / `AiRisk` / `AiUiText` / `MdView` 的 Markdown 渲染),否则它在 mac 侧编译不过。
  本包按红线选择不做,并把两条都记进 PROGRESS 的遗留(需要裁决)。

## D-105 C2c1:Toast 的自动关闭计时器 —— 离屏/测试里不挂,顶掉最老一张时顺手停表

- **默认计时器只在真桌面挂**:`ToastLayer.DefaultTimer` 以 `Motion.HasClock`
  (`Application.Current?.ApplicationLifetime is not null`)作闸。理由:离屏 `--smoke` 与测试进程里
  没有人推进 Dispatcher 的时钟 —— Windows 那边同样收不到 `Tick`(**行为等价**),但它会**留着**
  一个注册在 Dispatcher 上的 `DispatcherTimer` 句柄;mac 侧干脆不挂,"没有计时器"就是"没有泄漏"。
  真桌面生命周期下与 Windows 完全一致(到点先 `Stop()` 再关卡片,错误 7 秒、其余 4 秒)。
- **4 秒 / 7 秒怎么验**:`ToastLayer` 的构造函数收一个注入缝
  `delegate IDisposable? ToastTimer(TimeSpan delay, Action onFire)`(返回 `null` = 这次没起计时器)。
  用例注入 `FakeToastTimer`(记账 + 手动点火),于是"到点关掉"确定性地验,**不用 `Thread.Sleep`**、
  也不会留下真实计时器;`LiveTimerCount` 直接断言"还挂着的计时器数"。
- **顶掉最老一张时顺手停表**(本包对 Windows 的唯一一处收严):Windows 的
  `while (ToastHost.Children.Count >= 4) ToastHost.Children.RemoveAt(0);` 只摘孩子,
  那张卡片的计时器会一直转到自己到点(最长 7 秒)才在 `Tick` 里被 `Stop()` 收掉。
  mac 侧在顶掉时连计时一起 `Dispose` —— 对外行为等价(那张卡已经不在宿主里,`Kill()` 本来也
  只检查 `Contains`),但"没有活的计时器"这条纪律能直接断言。
- **窗口关闭即收摊**:`MainWindow` 的构造里挂 `Closed → ToastLayer.Dispose()`(停掉所有计时器 + 清空宿主),
  所以关窗之后再有人 `AppServices.Toast(...)` 也不会留残留计时器(有用例钉住)。

## D-106 C2c1:Toast 提成 `ToastLayer` + `ToastCard`;`AppServices.ToastSink` 在这里接上

1. **结构**:`Services\ToastCard.cs` 里两个类型 —— `ToastCard`(那块卡片:色条 / 图标 / 标题 / 正文,
   几何与取色照 Windows 抄)与 `ToastLayer`(宿主管理:4 张上限、进 `Motion.SlideIn`、
   退场 `Motion.SlideOut` 后摘掉、点一下关掉、悬停暂停、计时器收摊)。理由与 C2a 提 `Dlg.Push`、
   C2b 提 `PageHost` 一样:离开窗口才能单独钉住"堆叠 / 自动关闭 / 不泄漏"。
2. **接线**:`MainWindow` 构造里 `AppServices.ToastSink = Toast;`,而 `MainWindow.Toast(string, string,
   ToastKind)` 是 Windows `MainWindow.Toast` 的逐行移植(非 UI 线程先 `Dispatcher.UIThread.Post` 回来)。
   这条把 C2a 的 D-88、C2b 的 D-95 两处"刻意的静默"收口:`Dlg.Form` 的「填写有误」、
   页面里所有 `AppServices.Toast(...)`、`PageBase.Toast` 从这一刻起都出声。
3. **没有窗口也不会炸**(红线要求):`AppServices.ToastSink` 为空时照旧只写 `Debug.WriteLine`
   (D-88 的口径不变);外壳关掉之后 sink 还指着那个关闭窗口时,也只是往一个不在可视树上的
   `StackPanel` 加控件(没有时钟 ⇒ `Motion` 落终态),用例两个分支都钉住了。
4. `C2bWiringTests` 里那条"ToastSink 留空"的断言按 **D-95 的明确指示**改成"接上之后"的语义:
   原来断言 `Assert.Null(AppServices.ToastSink)` + "调一次 Toast 后宿主仍然空",现在断言
   sink 非空 + "调一次就在宿主里出对应 kind 的卡片"。**改强,不是改弱**;没有删用例、没有加 `Skip`。

## D-107 C2c1:"对话框卡片" = Windows `Dlg.Shell` 那块,提成 `DialogCard`

- `Services\DialogCard.cs` 的 `DialogCard` 就是 Windows `Dlg.Shell`(`Dlg.cs:185-213`)那块 Border:
  标题(H2)+ 正文(滚动)+ footer 的 `Auto,*,Auto` 三行、圆角 10、内边距 (22,18,22,18)、描边 1、
  `MinWidth = Min(width, 380)` / `MaxWidth = width`、玻璃底 + 玻璃描边、40/0.28/10 阴影。
  C2a 已经把它内联在 `Dlg.cs` 的 `Shell` 里,本包只是**逐行等价地搬运**(构造与取值一字不变),
  让 `Confirm` / `Ask` / `Alert` / `Prompt` / `Choose` / `Form` 都渲染在这一个具名类型上,
  并让它能被单独钉住(标题 / 正文 / 按钮组 / 宽度 / 取色 / 三行布局)。
- `DialogCard.ConfirmFooter(ok, cancel, danger)` 装的是 Windows `Dlg.Ask` 那对按钮(取消在前、
  右对齐、间距 8、危险操作时确定用 `btn-danger`),`Dlg.Ask` 现在直接用它。
- **`Panel` 与 `Busy` 保持各自的内联卡片**(Windows 也是这么写的:内边距 (20,16,20,18)、
  宽度夹 420、标题旁有关闭按钮)。用例明确断言"`Panel` 的 `Layer.Card` **不是** `DialogCard`",
  免得以后有人以为所有对话框都走同一个卡片。
- **公开面零改动**:`Dlg` 的公开成员名字 / 签名 / 默认值 / 回调形状一个字没动;
  `DialogCard` / `ToastCard` / `ToastLayer` / `ToastTimer` 全是新增的 `internal` 成员。
- **顺带更正 C2b 的一条链**:D-95 说"C2c 把外壳的 Toast 方法挂到 `ToastSink`" —— 实际落地的是
  `AppServices.ToastSink = Toast`(外壳的方法),不是"外壳另开一个 Toast 方法"的第二种写法。
## D-108 C2c2:内置预设背景与加载器图标**不在 Core 里**(任务书的前提不成立,按描述搬)

- 任务书说「内置预设背景已内嵌在 Core 的 `Resources\wallpapers\*.jpg` —— 查 `TipCL.Core.CoreResources`,不要重复嵌入」。**实测不成立**:mac 的 `src\TipCL.Core\Resources\` 只有 `ai` / `locales` / `plugin-host`(`CoreResourcesTests` 钉的就是这三处);Windows 侧那 4 张 JPG 是 `src\TipCL\TipCL.csproj:98` 的 WPF `<Resource>`(App 程序集),用 `pack://application:,,,/TipCL;component/Resources/wallpapers/…` 读(`SettingsPage.cs:643`);`PremiumAssets\*` 同一条(`TipCL.csproj:106-112`,而且**只有 `-p:Premium=true` 才嵌**)。
- **取舍**:按"描述"办 —— 把 4 张预设与 5 个加载器图标**字节级复制**进 `src\TipCL.App\`(SHA256 与 Windows 源逐项相同),用 `AvaloniaResource` 内嵌,URI 换成 `avares://TipCL/…`。**不改 Core**(红线)。mac 只出一个版本,所以加载器图标恒嵌(GOAL §3 "等同 premium")。
- 回到页面层的取址口:`Wallpaper.Presets` / `PresetUri(name)` / `PresetPath(name)` / `ReleasePreset(name)` / `OpenPreset(name)` —— 正好替掉 `SettingsPage` 里那三行 `pack://` 拼串 + `GetResourceStream` + `File.Create`,C3+ 的设置页移植因此仍是机械的。未知预设名:拼出来的 URI 打不开 → `OpenPreset` 给 null、`ReleasePreset` 给空串(Windows 那边是 `GetResourceStream` 抛异常、页面在 catch 里报「预设背景应用失败」—— 同一结果,不抛给用户)。

## D-109 C2c2:视频壁纸在 mac 侧**不支持**,做成注入缝且绝不假装放上了

- Windows 用 WPF `MediaElement` 静音循环播 mp4(`Wallpaper.cs:293-347`)。**Avalonia 11 没有内建媒体播放器**,要支持得引 LibVLCSharp 之类的新依赖(超出本包授权,而且 mac 真机也验不了)。
- 形状:`internal interface IVideoHost { Start(path); SetActive(bool); Stop(); }` + `Wallpaper.VideoHost`(默认 null)。null 时 `SetSource(video)` 记 `Error = 「macOS 版还不支持视频壁纸:」+path` 并保持 `Active = false`;接上之后走 Windows 那条路(起播 / 跟窗口激活播停 / 换图或收摊先停)。
- **不许假装成功**:`FileDrop.SetWallpaperAsync` 在 `ReloadAsync` 之后检查 `Wallpaper.Error`,非空就把原因当结果返回(于是 Toast 报真原因)。这是本包对 Windows 的一处收严。

## D-110 C2c2:拖放的注入缝、`EnableDrop` 的可访问性,以及"拖出去 = 取消"

- **类型可访问性**:`FileDrop` 与 `DropContext` 降为 `internal`,`PageBase.EnableDrop` 由 `protected` 降为 `internal`。理由是硬约束:mac 的 `MainWindow` 是 internal,`public static Task HandleAsync(MainWindow, …)` 会 CS0051;`protected` 成员也不能暴露 internal 的 `DropContext`。页面全在同一个程序集里派生,调用形状(`EnableDrop(() => new FileDrop.DropContext{…})` / `FileDrop.HandleAsync(win, paths, ctx())`)一字未变。
- **注入缝**:`Sink`(落点投递口)、`DragEnter/DragOver/DragLeave/Drop`(程序化入口,"拖的是什么"由调用方给)、`Enabled`(总闸)、`ReadyGate`(后端有没有连上,默认 `AppServices.Ready` = Windows 的 `_bridgeReady`)、`SkinApplier`(皮肤那一步在 Windows 直连 `AccountPage`)。测试因此不需要真实输入设备,也不需要后端。
- **"拖出去 = 取消"**:Windows 靠事件路由(指针离开窗口后松手,窗口收不到 `Drop`)。mac 侧 `DragLeave` 记 `_cancelledAfterLeave`,`Drop` 看到标记就拒绝并清标记,`DragEnter` 清标记 —— 语义等价,而且可断言。**注意不要**把它当成"必须有 DragEnter 才收 Drop":标记初值是 false,所以即便某个平台只发 `DragOver`+`Drop`,照收。
- **总闸与就绪闸分开**:`Enabled` 是"整关"(测试/离屏用),`ReadyGate` 是"后端连上没有"。两者都真时才出浮层、才接文件 —— 与 Windows `ShowDropVeil` 头一句 `if (!_bridgeReady) on = false;` 一致。

## D-111 C2c2:缩略图走 Core 的 Skia 编解码器;两个计时器都与 D-105 同口径

- **解码**:`Thumbs` / `Wallpaper` / `LoaderIcons` / `ThumbTile` 的解码统一收进 `Services\ImageDecode.cs` —— 走 `TipCL.Core.Platform.PlatformServices.ImageCodec`(B1a 的 `SkiaImageCodec`),再交给 Avalonia 的 `Bitmap` 显示。好处:WebP / EXIF 方向 / 坏数据全由 Core 那一个实现负责(Windows 侧散在 `WebpFallback`+`BitmapImage` 两处),坏数据一律 null(与 Windows 的 `catch { return null; }` 同)。
- **路径来源**:`Thumbs` 的"这张图在本地是哪个文件"做成 `IThumbStore`(`BridgeThumbStore` = Windows 的 `thumb_path` / `ensure_thumb` 两条;`LocalThumbStore` = 根目录 + `sha1(url).img`,给测试与离线场景)。**缓存根因此可注入**,测试一律给 `TestHome.Root` 下的目录 —— 红线:绝不碰用户的图片/缓存。
- **轮播计时器**:`Wallpaper.TimerFactory` 注入缝,默认实现与 C2c1 的 Toast 计时器同一口径 —— `Motion.HasClock` 为假(离屏 `--smoke` / 测试进程)时**不挂计时器**,于是"没有残留计时器"本身可以断言;真桌面下就是 Windows 的那个 `DispatcherTimer`(到点换一张、不停表)。
- **`Trim` 的一处小改**:Windows 的 `foreach (_mem.Keys.Take(_mem.Count - 300))` 在 .NET 8 的 `ConcurrentDictionary` 上会边走边改集合,先 `ToList()` 再删(语义不变)。

## D-112 C2c2:我自己踩了 D-36(用 PowerShell 的 `Get-Content | Set-Content` 改中文文件),如实记录

- 过程:为把 `C2c2ThumbsTests.cs` 里十几处 `.GetAwaiter().GetResult()` 换成 `C2c2Files.RunOffThread(...)`,我图快用了
  `(Get-Content -Raw $f) -replace … | Set-Content -Encoding utf8 $f`:结果中文被读坏(乱码 + BOM),`Select-String` 连"缩略图"都匹配不到了 —— **与 D-36 描述的坑一模一样**。
- 处理:立刻用 `write` 工具把该文件整份重写(内容我有完整备份),然后**逐字节核验**:无 BOM、CR=0、U+FFFD=0、中文可 grep、`dotnet build` 0 错误、用例全绿。**该文件没有进过任何提交**(损坏发生在提交之前)。
- 之后的文档更新一律改成"先用 `write` 工具写成 `artifacts\*` 临时文件 → 用 `[System.IO.File]::WriteAllBytes/ReadAllBytes` 按字节拼接/替换整行",再逐字节复核。**结论重申:D-36 不是建议,是硬规则。**

## D-113 C2c2:测试里驱动 `Dlg.Ask` 必须"先把对话框推出来、再点按钮"

- 事实:`Dlg.Ask` 的顺序是 `layer = Push(card, …)` **然后** `button.Click += …`;而离屏的 `Dlg.SmokeAutoAnswer` 恰恰是在 `Push` **里面**被调(`Dlg.cs:158-164`)。所以在 `SmokeAutoAnswer` 里点"确定",TrySetResult 的处理器还没装上 —— 点了也白点(只有 `Push` 给的 `onDismiss` = 取消那条路是通的)。
- 后果(本包实测):一条 `Ensure_creates_an_offline_account_when_signed_out` 因此死等 30 秒 → `AvaloniaSession` 超时 → **会话线程被占死**,后续每条用例都跟着超时(看起来像"整批卡死",实际只有一个用例的写法错)。
- 正确写法(见 `C2c2Files.Dialog()` / `Click()`):`Task t = AccountGate.EnsureAsync();` → `DialogCard card = C2c2Files.Dialog();`(内部先 `UiHost.Flush()`,把 `await` 的续体推进到"对话框已推出")→ `C2c2Files.Click(card, I18n.L("创建并继续"));` → `C2c2Files.Pump(() => t.IsCompleted);` → 再读结果。C3+ 驱动 `Dlg.Ask` 的用例照这个来。

## D-114 C2c2:在会话线程上同步等 `Thumbs.LoadAsync` 会自锁(D-79 的一个新实例)

- `Thumbs.FetchAsync` 里有 `await Task.Run(解码)`:在会话线程上 `await` 会捕获 Avalonia 的 `SynchronizationContext`,续体被投回同一个线程;而会话线程正被用例体占着(`GetResult()` 阻塞)→ 自锁,30 秒超时。
- 修法(**只改测试**):`C2c2Files.RunOffThread(() => Thumbs.LoadAsync(url, px))` = `Task.Run(...)` 起在池上再同步取结果 —— 池上没有 Dispatcher 的 `SynchronizationContext`,续体留在池上;这也与生产环境一致(UI 线程不阻塞、取图照常回来)。
- **没有**为了测试去给 `Thumbs` 加 `ConfigureAwait(false)`:那会改动产品行为(续体线程),而 Windows 源码也没有这么写。
## D-115 C3a:拖拽 / 缩放的注入缝是"事件薄壳 + internal 状态机",测试驱动状态机

- 事实:`BlockHost` 与 `DashHost` 的拖拽 / 缩放逻辑 100% 是"从事件对象里取坐标 + 判左键",其余全是纯算术状态机。
  Windows 版把两者揉在同一个方法里(`OnDragMove` 里既 `e.GetPosition(...)` 又算几何)。
- 做法:事件的取坐标 / 判左键留在事件处理器里,状态机抽成 internal 成员,真实处理器调它们:
  - `BlockHost`:`BeginDragCore(BlockCard, Control src, IPointer?)` / `DragTo(double y)` / `EndDrag(Control src)`
    —— 真实路径 `BeginDrag` / `OnDragMove` / `OnDragEnd` / `OnDragLost` 只做
    `e.Pointer`、`e.GetPosition(_list).Y`、`e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed` 三件事;
  - `DashHost.CardView`:`GrabCore(string? mode, Point grabPoint, IPointer?, IInputElement?)` /
    `DragTo(Point)` / `DropNow()` —— 真实路径 `Grab` / `OnDragMove` / `OnDrop` 同样只取坐标 + 判左键。
- `BlockHost` 里一批 private 因此降为 internal(`EnterEdit` / `ExitEdit` / `HideBlock` / `Restore` /
  `ResetPage` / `MoveVisible` / `MidY` / `H` / `VisibleIndexOf` / `VisibleCount` / `VisibleSpecs` /
  `Persist` / `ApplyDoc` / `Render` / `InsertVisual` / `KnownIds` / `VisibleIds` / `DefaultDoc` /
  `ApplyStored` / `RefreshThemeVisuals`),并加了若干只读快照(`OrderNow` / `HiddenNow` / `EditNow` /
  `CardsNow` / `CardOf` / `TrayIdsNow` / …)。**public 面一个签名都没动** —— 页面的调用形状与 Windows 完全一致。
- **没有**为了测试去构造 `PointerPressedEventArgs` / `PointerEventArgs`,**也没有**用
  `Avalonia.Headless` 的鼠标模拟(那属于任务书禁止的"模拟真实输入设备")。代价如实记在
  PROGRESS 的"未验证"里:事件薄壳那一层没有真输入驱动过,`handledEventsToo: true` 的等价性是按
  Avalonia `Button` 类处理器会标记已处理的语义推导的。

## D-116 C3a:`PageLayoutStore` 的原子落盘改用 `File.Move(overwrite: true)`

- Windows 源:`File.WriteAllText(tmp, ...)` 之后 `if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);`
- mac 侧:`File.Move(tmp, path, overwrite: true)` 一步到底。
- 理由(保守选项):`File.Replace` 在 **Unix** 上的支持情况在本机(Windows)无法验证 —— 它要么正常工作、
  要么抛 `PlatformNotSupportedException`,而 `Save` 外层把异常**全部吞掉**(那是 Windows 的设计:
  "写不进去也不能崩 UI"),于是真出问题时的表现会是"布局静默不落盘"。`File.Move(overwrite: true)`
  是 .NET 文档里跨平台的原子改名(Windows 走 `MoveFileEx(REPLACE_EXISTING)`),而且**不引入
  "只在某个平台跑得到"的分支**,风险最低。
- 语义没变:先写 `.tmp` 再改名,不留半截文件;用例钉住了"连存两次后文件是最新那份、目录里没有 `.tmp`"。
- 另一个未知项照实记:`.NET 8` 的 `Utf8JsonWriter` 在 `WriteIndented = true` 时,本机实测输出的是
  **CRLF**。红线里的"CR=0"针对**受版本控制的源文件**,运行时数据文件的换行符不在其内;
  mac 上是否同样 CRLF 本机不可验证(同一个 API,行为由运行时决定)。

## D-117 C3a:任务书"BlockHost/DashHost 自包含"的前提不成立(如实记录,按"小而自包含就一并搬"处理)

- 任务书的依赖扫描只数了 `Ui.` / `I18n.` / `SPanel` / `Ico.` / `AppServices` / `Motion.` 这些**门面**
  引用(那些确实都已移植),**漏了布局模型层**:`Pages\BlockHost.cs` 还要 `PageLayoutDoc` / `BlockState` /
  `PageLayoutStore` / `PageLayoutIO`,`Pages\DashHost.cs` 还要 `DashCard` / `DashLayout` —— 这 5 个类型
  在 mac 侧**完全不存在**(分别在 Windows 的 `Pages\PageBlock.cs`、`PageLayoutStore.cs`、
  `PageLayoutIO.cs`、`DashCard.cs`、`DashLayout.cs`,合计 2.5–6.9 KB,纯 `System.Text.Json`)。
- 处理:按任务书"小而自包含就一并搬并说明"的规定一起搬进 `src\TipCL.App\Pages\`(命名空间 `TipCL.Pages`,
  与 Windows 一致),`src\TipCL.Core` 一字节未动。
- 附带收益:仓库里早就存在的两个 Windows 测试文件 `Ported\PageLayoutTests.cs` 与
  `Ported\LayoutDefaultTests.cs`(之前因 `using TipCL.Pages` 被 `Compile Remove`)因此恢复编译,
  贡献 20 条用例(前者的实例临时目录按 A6 的 B 类规则从 `Path.GetTempPath()` 改成 `TestHome.Root`,
  断言一字未改)。
- 顺带记一个 Windows 源自身的笔误(不修,只钉):`BlockHost.BlockCard` 写
  `Ui.G("Auto", "*")`(**只定义 1 行**)之后又 `grid.Add(_bodyWrap, 1)`;同位置的 `DashHost` 写的是
  `Ui.G("Auto,*")`(2 行)。WPF 与 Avalonia 都会把越界行号夹到最后一行,所以两边行为一致;
  移植逐字照抄,并用用例钉住实际落位(常态操作条收起、只占 0 高度,视觉等价)。

## D-118 C3b:任务书"这两个对话框自包含"的前提只对了一半(如实记录,按"小而自包含就一并搬"处理)

- 任务书给的依赖计数(`CrashUi.cs`:`Ui.` 12 / `Dlg.` 3 / `I18n.` 28 / `Motion.` 0;
  `VersionSetupDialog.cs`:`Ui.` 30 / `Dlg.` 1 / `I18n.` 45 / `Motion.` 0)**逐项复核全部吻合**。
- 但"所有服务都已移植,所以本包自包含"**不成立**:两个文件还引用 `TipCL.Models.OpResult`
  (`CrashUi` 的动作返回值)与 `TipCL.Models.VersionSettingsDto`(版本设置的 DTO),
  这两个类型在 mac 侧**完全不存在**(Windows 侧各 240 / 1136 字节,纯 `System.Text.Json`
  数据对象,不碰界面类型)。
- 处理:按任务书"小而自包含就一并搬并说明"的规定,用 `Copy-Item` **字节级**搬进
  `src\TipCL.App\Models\`(SHA256 与 Windows 源逐字节一致、无 BOM、LF)。
  `src\TipCL.Core` 一字节未动。与 C3a 的 D-117 是同一类问题(模型层被依赖扫描漏掉)。
- 另一处**真的缺口**没有搬,按红线停下并报告:`Pages\AiFixWindow.cs`(Windows 侧 22662 字节的**整窗**,
  依赖 AI 页整套基础设施)—— 见 D-119。
- 顺带记一个**照抄来的**行为细节(不是移植引入的,已用两条用例钉住):
  `VersionSettingsDto.Isolation` 的属性初值是 `"none"`,而 `IsoValues` 的第 2 项正好是 `"none"`,
  所以后端给空对象(或 `TryCallAsync` 兜底)时版本隔离落在「关闭(共用实例目录)」而**不是**第 0 项
  「跟随全局」;第 0 项只留给"值不在候选表里"这一支。

## D-119 C3b:`AiFixWindow` 未移植 → 「交给 AI 修复」走注入缝 + 如实提示(请 Lead 裁决)

- 事实:`CrashUi` 在 `gameError: true` 时会建一个主按钮「交给 AI 修复」,Windows 的点击处理是
  `new AiFixWindow(report).Show()`。`AiFixWindow` 是 **22.6 KB 的整窗**,依赖 `AiRisk` / `ToolMeta` /
  `AiUiText` / `AiIcons` / `MdView`(AI 页基础设施),属 C7+ 的范围,**本包按红线没有移植它**。
- 选择(保守 + 不静默):按钮**照 Windows 建**(文案、图标码位 `\ue99a`、提示语、`BtnKind.Primary` 全一致),
  点击走 mac 侧新增的 `CrashUi.AiFixLauncher`;挂点没接时弹一句 Toast(标题「暂未实现」,
  正文「AI 修复窗口尚未移植」)。
- 为什么不静默空操作:静默会让人以为"点了没反应 = bug";为什么不干脆不建这个按钮:那会改变
  `LaunchPage` / `TasksPage` 看到的 UI 形状,页面移植就不再机械。
- **这是一处用户可见的行为差异**,请 Lead 裁决:接受这个降级,还是把 `AiFixWindow` 排进 AI 全家那一包并
  把 `AiFixLauncher` 接上(接的时候 `CrashUi` 本身不用再改)。

## D-120 C3b:测试里"对话框还开着"时不许等对话框任务;泵的计时必须用 Stopwatch(会话 30 秒会先炸)

- 现象:第一版 71 条用例全跑出来后是 **23 失败**,拆开只有**两处真问题**,其余 21 条是
  `AvaloniaSession` 会话线程被一条死等用例占死之后的**连锁超时**(C1b-fix 记过的同一现象:
  "看起来像整批卡死,实际只有一个用例的写法错")。
- 真问题 1:**在对话框还开着的用例里去等对话框任务**(`PumpTask(Choose/Ask 返回的那个 Task)`)。
  那个任务只有关掉卡片才会结束,于是必然死等到会话超时。修法:新增
  `C3bFiles.Settle()`(推几轮 Dispatcher 让"点了之后的效果"落地,但**不等**未结束的任务),
  这类用例只断言效果,最后再点关闭 + `PumpTask`。
- 真问题 2(**更隐蔽**):`C3bFiles.Pump` 第一版按"轮数 × 2ms"记账,而 `UiHost.Flush()` 一轮要
  `RunJobs` + `ForceRenderTimerTick`,真实耗时远大于 2ms —— 于是 15 秒的预算被拖成几十秒,
  **先超时的是 `AvaloniaSession` 的 30 秒**,失败消息还指向"卡在等待 Dispatcher",误导排查方向。
  修法:改用 `Stopwatch` 计真实时间,上限 **9 秒**(明显小于 30 秒),保证失败时冒出来的是本方法
  自己的异常。
- 配套记两条本机实测的期望值(都是别人写的 UI 门面的既有行为,本包只是照抄期望):
  `Ui.Txt(bold: true)` 用的是 `FontWeight.SemiBold`(=600,枚举别名 `DemiBold`)不是 `Bold`(700);
  Avalonia 的 `Layoutable.MaxHeight` 默认值是 `PositiveInfinity` 而不是 `NaN`。
- 结论(给 C3+ 的后续包):**驱动对话框的用例,只想"点了之后的效果"就用 `Settle`;要等任务就在
  关掉卡片之后再等;泵一律按真实时间设上限,且必须明显小于会话的 30 秒。**
## D-121 C3c:任务书"这两个文件自包含"的前提(第三次)不成立——还差 6 个 Models DTO 与 hero.jpg

- 事实:`Pages\LaunchPage.cs` 除了门面(`Ui` / `I18n` / `Dlg` / `Motion` / `AppServices` / `TaskStore` /
  `AccountGate` / `Wallpaper` / `CrashUi` / `VersionSetupDialog` / `BlockHost` / `DashHost` / `SmoothScroll`)之外,
  还引用 **6 个在 mac 侧根本不存在的模型**:`JavaOption`(201 B)/ `ModEntry`(272 B)/ `NewsRow`(183 B)/
  `PreflightItem`(230 B)/ `PreflightResult`(216 B)/ `AuthlibPreset`(143 B),以及一个**嵌入资源** `PremiumAssets\hero.jpg`
  (275898 B,Windows csproj 里是**无条件**嵌入的 `<Resource>`,C2c2 只搬了 5 个加载器图标 PNG)。
- 处理:6 个 DTO 用 `Copy-Item` **字节级**搬进 `src\TipCL.App\Models\`(SHA256 与 Windows 源一致、无 BOM、LF);
  `hero.jpg` 同样字节级复制进 `src\TipCL.App\PremiumAssets\` 并在 csproj 里加 `<AvaloniaResource>`
  (URI `avares://TipCL/PremiumAssets/hero.jpg`,对应 Windows 的 `pack://application:,,,/TipCL;component/PremiumAssets/hero.jpg`)。
  `src\TipCL.Core` 一字节未动。
- 这是同一类问题的**第三次**(C3a 的模型层 D-117、C3b 的两个 DTO D-118、本包的 6 个 DTO + 1 个资源)。
  教训重申:**C3+ 的任务书里凡是断言"自包含"的地方,子代理都必须自己逐条读依赖再动手**;
  Lead 也该把"依赖扫描"从"数门面引用次数"改成"编译一次看看缺什么"。

## D-122 C3c:`MainWindow.FlyToTasks` 在 mac 侧不存在——注入缝 + 用户可见降级(请 Lead 裁决)

- 事实:Windows 的启动页在「修复当前版本」后会调 `Win.FlyToTasks(_launchBtn, "修复中")`
  (`MainWindow.xaml.cs:1336`)播一个"飞向任务页"的小动画。mac 的外壳(C2b)没搬 `FlyHost`/Dock(D-97),
  也**没有**这个成员;而全仓库还有 **13 处**同类调用点(InstancePage / VersionManagePage / InstallPage /
  JavaPage / CatalogPage / MultiplayerPage / PluginsPage / TasksPage 等),后续每个页面包都会撞上它。
- 本包的选择(保守 + 不越红线):**不动外壳**。页面照 Windows 建,任务照跑(`repair_version`)+ 照常 Toast;
  飞行动画走新增注入缝 `LaunchPage.FlyToTasksSeam`(`Action<Control, string>`),挂点没接时**不播**。
- 这是一处**用户可见的降级**(修复时少一个小动画),已在 `LaunchPage` 类注释、PROGRESS 的 C3c 第 4 节写明。
  **请 Lead 裁决**:是把 `FlyToTasks` 作为可选成员补进外壳(推荐 —— 一次修好 13 处调用点),
  还是接受这条降级并在每个页面包里重复同样的注入缝。

## D-123 C3c:WPF 的 `ActualHeight` + `SetBinding` → Avalonia 的 `Bounds.Height` + `Bind`

- C3a 的 Lead 验收点名了 `LaunchPage` 里 `_blocks.Root.SetBinding(FrameworkElement.MaxHeightProperty, ...)`
  这一处要换。实测下来是**两个**改动,不是一个:
  1. Avalonia 11 **没有 `SetBinding`**,等价物是 `AvaloniaObject.Bind(property, binding)`(返回可释放的订阅句柄);
  2. 把属性换成 `Layoutable.MaxHeightProperty` 之后,**绑 `"ActualHeight"` 依然不生效** —— 本机实测
     `MaxHeight` 一直留在默认的 `PositiveInfinity`(页面明明已经排过版,`Bounds.Height ≈ 480`)。
     改成 `"Bounds.Height"` 才生效(用例:`Blocks_max_height_binding_exists_and_follows_the_page_height`,
     断言 `MaxHeight == page.Bounds.Height × 0.62`)。
- 这个路径**不是新发明**:C1c 的 `SmoothScroll.BindWindowMaxHeight` 早就把"窗口 ActualHeight"翻译成
  `Bounds.Height` 了(那里也留了同样的注释)。浮层的 `MakePopup` 也改成 `Source = PageBase.Win` + `"Bounds.Height"`
  (用例 `Popup_max_height_follows_the_host_window` 钉住 `MaxHeight == 窗口高 × 0.86`)。
- 附带结论:那条订阅的**源是页面自己、目标是页面的孩子**,引用图完全在页面内部,不会把页面挂到外部根上;
  句柄存进 `_blocksMaxHeightBinding`(只留给测试看,生产路径不释放,Windows 的 `SetBinding` 同样是"活到进程结束")。

## D-124 C3c:`ContextMenu.IsOpen = true`(WPF)→ `ContextMenu.Open(anchor)`(Avalonia)——无参 `Open()` 会抛异常

- WPF 的「更多操作」是 `menu.PlacementTarget = anchor; menu.IsOpen = true;`。Avalonia 的
  `MenuBase.IsOpen` 的 **setter 不可见**(`CS0272`),文档给的两个入口是 `ContextMenu.Open()` 与 `Open(Control)`。
- **本机实测:`menu.Open()`(无参)抛 `ArgumentNullException("control")`** —— 它要去解析 PlacementTarget 的宿主,
  离屏/测试里解析不到。**这是页面用例抓到的真 bug**:真实点击「更多操作」会直接崩,而 `--smoke` 点不到它,
  只有"驱动页面自己的入口"的用例能发现。修法:`menu.Open(anchor)`(锚点显式传进去,与 WPF 的语义一致)。
- 记在这里也是给后续页面提个醒:**WPF 里 `IsOpen = true` 的地方,在 Avalonia 要确认是有参的 `Open(target)`**。

## D-125 C3c:启动页用 `[ModuleInitializer]` 自注册,不动外壳

- 外壳的构造函数第一件事就是 `Navigate("launch", instant: true)`(与 Windows 首屏一致),而在 mac 侧页面工厂
  只能来自 `PageRegistry`(D-94),所以注册必须发生在"任何人碰这个程序集之前"——模块初始化器正好是那一刻。
- 备选方案都被红线挡掉:写进 `Program` / `App` / `MainWindow` 都要改外壳文件;写进页面类型的静态构造函数
  则太晚(那时外壳已经 `Navigate` 过了)。
- 文件:`src\TipCL.App\Pages\LaunchPageRegistration.cs`,成员 `LaunchPageRegistration.PageId = "launch"` 与
  `Register()`(幂等,`PageRegistry.Register` 对同一 id 是覆盖)。测试里也可以直接调它。
- 副作用(已核):`C2bShellTests` 里两条"还没有页面注册所以宿主是空的"用例因此不再成立,按 D-126 处理。

## D-126 C3c:C2b 那两条"宿主是空的"用例按新契约拆分(断言只增不减)

- `Page_host_starts_empty_because_no_page_is_registered_yet` 断的是"还没有页面注册"这个**时代前提**;
  启动页注册进来之后,外壳首屏就是启动页,这个前提按设计作废。
- 处理(没有删测试、没有放宽断言、没有加 Skip):
  1. `Page_host_opens_the_registered_launch_page`:**新契约**,断言更多(当前页 id / 类型 / 宿主内容同一个实例);
  2. `Page_host_starts_empty_when_the_launch_page_is_unregistered`:用 `RegistryScope` + `PageRegistry.Unregister("launch")`
     把前提还原,**原来的三条断言一字不改**地保留;
  3. `Navigate_to_an_unknown_id_does_nothing_and_does_not_throw` 同样先 `Unregister("launch")` 再跑原断言
     (它要验的是"未知 id 不切页 + 大小写敏感",与启动页在不在无关)。
- 全量用例数因此 **+1**(一条拆成两条):2968 + 117 + 1 = **3086**。

## D-127 C3c:非按钮控件的点击,驱动它调用的那个 internal 处理器体(同 D-115 的口径)

- 启动页里有一批"不是按钮"的可点区域:大标题行(开实例切换浮层)、新闻卡(开公告浮层)、两个玻璃钮(配置 / 联机)、
  实例切换浮层里的实例行与版本行、便签/分辨率输入框的 `LostFocus`。
- 构造 `PointerReleasedEventArgs` / `LostFocusEventArgs` 属于任务书禁止的"模拟真实输入设备"(而且这些 args 的
  构造函数在 Avalonia 11 里并不都公开)。所以沿用 C3a 的做法:**事件处理器只留薄壳,处理器体抽成 internal 成员**,
  用例驱动那一层。本包新增的成员:`OpenMultiplayer` / `CommitNotes` / `CommitDisplayPrefs` /
  `PickInstanceFromSwitch` / `PickVersionFromSwitch`(`PickInstanceForTest` / `PickVersionForTest` 是它们的一行转发)、
  以及 `ApplyHeroTone` 由 private 降为 internal、`ShowMoreMenu` 由 private 降为 internal。
- 代价如实记:那一层"事件真的会调到它"没有用真输入验过(PROGRESS 的未验证项里写了)。
- 顺带:按钮(`Button.Click`)、菜单项(`MenuItem.ClickEvent`)仍然用 `RaiseEvent` 真驱动,不走上面这套。

## D-128 C3c:Avalonia 的 `TextBox` 没有 `VerticalOffset` / `ScrollToEnd()` —— 日志自动滚动改走模板里的 `ScrollViewer`

- Windows 的 `AppendLog` 里有一句 `_log.VerticalOffset >= _log.ExtentHeight - _log.ViewportHeight - 24.0`(判断"是不是
  停在底部")加一句 `_log.ScrollToEnd()`。Avalonia 的 `TextBox` **这四个成员都没有**(只有 `CaretIndex` /
  `SelectionStart` 之类),它们长在 `TextBox` 模板里那根 `ScrollViewer` 上。
- 做法:`GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()` 取到就用它的
  `Offset.Y / Extent.Height / Viewport.Height / ScrollToEnd()` 做同一件事;**取不到(还没套模板 / 离屏)就跳过自动滚动**
  —— 日志内容照旧刷新,只是不追底。这是"降级但如实",不是静默丢功能(行缓冲 2500 的上限与裁剪逻辑一字未改,用例钉住)。
## D-129 C3d:测试里"动画开着"那一支用 `Motion.ClockOverride` 打开(headless 的实测取舍)

- **事实**:headless 会话里 `Application.ApplicationLifetime` 永远是 null ⇒ `Motion.HasClock` 恒假 ⇒
  `Motion.Enabled` 恒假 ⇒ **所有"动画开着"的分支在测试里不可达**(D-69 / D-98 记过同一件事,
  当时只能验"关掉时落终态"那一半)。
- **本包探针实测(两条,决定了设计)**:
  1. 离屏的动画时钟**推得动**:620ms 的 `Animation.RunAsync` 在 `UiHost.Flush()` 的泵
     (`Dispatcher.UIThread.RunJobs()` + `AvaloniaHeadlessPlatform.ForceRenderTimerTick()`)里真的跑完并落了终态;
     同一控件上并发跑 5 条 Animation(分属 Translate / Scale / Opacity)互不打架。
  2. **`DispatcherTimer` 在那条泵里一次都不 Tick**(400ms 的 16ms 定时器,计数 0)⇒
     "用 `DispatcherTimer` 逐帧插值驱动动画"这条方案被**否掉**(那会在离屏/测试里永远走不完)。
- **决定**:给 `Motion` 加一个 **internal 注入点** `internal static bool? ClockOverride`(默认 null =
  按真桌面生命周期判断,行为与以前完全一致;`HasClock` 读它)。测试用 `C3dClock` 作用域把它与用户开关
  一起置真、用完**两个都还原**(先还用户开关、再撤时钟)。这样 `FlyToTasks` 的守卫可以**逐字**保持
  Windows 的 `if (!Motion.Enabled || !IsLoaded) return;`,而"动画开着"那一支真的可验。
- **纪律**:只在作用域内设它;**任何**设了它的包都有义务还原,否则同进程里别的用例会看到"动画开着"。
- 顺带实测:`Avalonia.Controls.Control.IsLoaded` 在 Avalonia 11 里**存在**且语义与 WPF 一致
  (没 Show=假 / Show 后=真 / Close 后=假),所以 `!IsLoaded` 这条守卫也是逐字照搬;
  **不能**拿 `IsAttachedToVisualTree()` 当"窗口活着"的判据(窗口是它自己那棵树的根,没 Show 过也是真)——
  `Motion.Loop` 里那句把它当作 `IsLoaded` 等价物的注释对窗口不成立(本包只记录,不改它)。

## D-130 C3d:620ms 时间线怎么落到 Avalonia(Avalonia 只有一条整段缓动的取舍)

- Windows 的四条曲线:X `0→dx`(620ms,`CubicEase EaseInOut`);Y 两个关键帧(45% 处 `dy*0.28-46` 配
  `EaseOut`、100% 处 `dy` 配 `EaseIn`);透明度 15%/72%/100% = 1/1/0(线性);缩放 `0.7→0.42`(X、Y 各一条)。
- Avalonia 的 `Animation` 只有**一条**整段缓动(`Animation.Easing`),没有 WPF 那种"每个关键帧自带缓动"。
  因此:X / 缩放用**两关键帧 + 真缓动**(只有一段 ⇒ 缓动就是曲线本身,与 Windows 逐点相同);
  Y 的两段不同缓动按 **2.5%(15.5ms)采样成线性关键帧**(`0.025` 能整除 `0.45` 与 `1.0`,
  45% 那一帧正好落在采样点上,不会被采样误差吃掉);透明度本来就是线性的,直接照搬。
- **以 Transform 对象本身当 `RunAsync` 目标是错的**(探针实测抛 `InvalidCastException`:
  `TransformAnimator` 要求目标是 `Visual`)。正确写法是**目标给 chip**、setter 写
  `TranslateTransform.XProperty` / `ScaleTransform.ScaleXProperty`,由动画器去解析 chip 的 `RenderTransform`。
- 时长 **620ms 不乘 `Motion.Scale`**:Windows 这里就是裸的 `TimeSpan.FromMilliseconds(620.0)`(其它
  `Motion` 成员才乘倍率)。`Completed` 挂在**缩放**那条动画上(Windows 的 `doubleAnimation` 就是 0.7→0.42)。

## D-131 C3d:剩余 13 处调用点的统一写法(12 处逐字、1 处一次 cast)

- 12 处(LaunchPage / InstancePage / VersionManagePage / CatalogPage / JavaPage / MultiplayerPage 等)
  **一字不改**:`PageBase.Win?.FlyToTasks(anchor, text)` —— `PageBase.Win` 早就是
  `internal static MainWindow?`(C2b),`FlyToTasks` 就在那个类型上。
- 只有 `InstallPage.cs:489` 的 `AppServices.Window?.FlyToTasks(_go, _ver)` 要写成
  `(AppServices.Window as MainWindow)?.FlyToTasks(_go, _ver)`:mac 的 `AppServices.Window` 静态类型是
  Avalonia 的 `Window?`(D-88),而 `MainWindow` 是 internal。**没有**新增 `AppServices.Shell` 之类的成员
  (会偏离 Windows 原文,且多一个要维护的门面),null 语义与原来相同。
- C3c 的 `LaunchPage.FlyToTasksSeam` **签名保留**(下游可注入探针),但由外壳构造函数接到真成员、
  `Closed` 时置空;**新页面不要再加缝**,直接写 `Win?.FlyToTasks(anchor, text)`。

## D-132 C3e:Motion 的变换动画改成"目标给宿主控件" + 失败必须可观测(附三条框架实测)

- **缺陷(任务书 + C3d 实测)**:`Motion` 里凡把 **Transform 对象本身**当 `Animation.RunAsync` 目标的地方,
  Avalonia 会**同步抛 `InvalidCastException`**。本机读 11.3.2 的 IL 坐实了原因:`TransformAnimator.Apply`
  第一句就是 `(Visual)control`。旧 `Motion.Start` 把它 `catch` 掉 ⇒ 离屏(`Enabled` 恒假)走不到,
  **真桌面上这些动画静默不动**(FadeIn 的位移 / HoverLift / PopIn / PopOut / Shake / Spin / Pulse /
  Shine / SlideIn / SlideOut / PageSwap,以及间接走 FadeIn 的 Stagger / StaggerItems)。
- **动画器怎么找变换(读 IL 的结论,用来定修法)**:`property.OwnerType` 是 `Transform` 子类时,
  它在 `Visual.RenderTransform` 里找 —— ① `RenderTransform` 恰好就是那一类型,或 ② `RenderTransform` 是
  `TransformGroup` 时,取里面**运行时类型完全相等**的第一个子项;两者都不是就**只发一条 Warning 日志、
  什么都不做**(任务永不结束)。所以调用方必须已经把那个变换实例挂进宿主的 RenderTransform ——
  `EnsureTranslate` / `EnsureScale` / `Shine` 的 `layer.RenderTransform = tx` 都保证了这一点。
- **修法**:目标给**宿主控件**、属性仍旧写变换自己的属性(`TranslateTransform.YProperty` 之类),
  统一走 `PlayTransform` / `PlayTransformFrames`;`Motion.Start` 的目标参数类型收成 `Visual` ——
  "拿 Transform 当目标"从此**编译不过**(比一条断言更硬的回归钉子)。
  **公开签名一个字节没动**(19 处调用点都在私有实现里改)。
- **第二条坑(同族,顺带修)**:**`Animation.Easing` 显式写 null 会让动画静默失效** ——
  属性一个字节不写、`RunAsync` 的任务永不结束,也不抛异常(本机探针:A/D 两条都 PENDING 2 秒)。
  `Animation.Easing` 的**默认值是 `LinearEasing`**,所以"不缓动"的正确写法是**不碰这个属性**:
  `TwoFrame` / `KeyFrames` 现在只在 `easing != null` 时才赋值。`Shine` 的不透明度关键帧
  (`PlayFrames(..., null)`)一直踩在这个坑上(也是真桌面上的"少动一下")。
- **第三条坑(Spin 专属)**:**`IterationCount = Infinite` 配 `RunAsync` 必抛**
  `InvalidOperationException: Looping animations must not use the Run method.`(本机探针;同样被旧代码吞掉)。
  Avalonia 11.3.2 里唯一能循环的公开入口 `Animation.Apply` **不是公开 API**(编译不过),所以 `Spin`
  改成"**一圈接一圈**":一条有限动画跑完,在收尾回调里再起下一条。停止沿用 Windows 的语义
  (`BeginAnimation(AngleProperty, null)`)= **取消 + 归零**:本机探针实测"取消之后动画立刻不再写值",
  所以归零不会被最后一帧盖回去。代价是每圈交界处有一次收尾往返的一帧级重启(0° 与 360° 同处,观感连续)——
  这是本包**唯一**的用户可见差异。
- **headless 的时钟事实(测试能不能验动画,全看它)**:离屏的动画时钟**只在真的产出帧时前进** ——
  同一条变换动画,每轮先 `InvalidateVisual()` 再泵,251ms 跑完;一次都不标脏就永远停在起点、任务永不结束。
  所以:测试的泵每轮先标脏(`C3eStage.Frame`);生产侧 `Motion.Start` 起完动画也顺手 `InvalidateVisual()`
  推一次(真桌面由 vsync 驱动,多推一次无害,但能把"没帧 ⇒ 不动"这条路也堵上)。
  顺带更正 D-129 的一句:那次的"`DispatcherTimer` 一次都不 Tick"是**泵里不 sleep**、真实时间没走够导致的;
  本包用"每轮 sleep 3~5ms"的泵实测 80ms 的定时器 60 轮 tick 了 9 次 —— 所以 FadeIn 的延时分支、
  Stagger 的错帧、CountUp 的逐帧文本**都能在离屏验**。
- **失败可观测(任务书第 3 条)**:`Motion.Start` 仍然"任何失败都不许把界面带崩",但同步异常**与任务失败**
  两条路都记进 `LastError` + `Debug.WriteLine`(旧代码只看得到同步异常);另加三个测试缝:
  `StartedAnimations`(真起过几条)、`RunningAnimationsSinceReset`(本窗口还挂着几条)、
  `LastTargetType`/`LastPropertyName`(最近一条动画的目标与属性)。`ResetObservability()` 清空前三者。
  `CheckTransformTarget` 把"动画器会静默不动"的那种调用(变换没挂上去 / 拿错了实例)也变成一条记录。
- **泄露检查为什么按编号记账**:`RunningAnimations` 是进程级的,而 `C3dReachTests` 关窗后留了 4 条飞行 chip
  动画(它们没消失,只是没人推帧;本用例一泵就会跑完并减数,差值甚至变成 **−2**)。两条通道用例顺序不同,
  直接断言总数会 VSTest 绿、进程内执行器红(本包实测踩到)。所以 `ResetObservability()` 之后按**每条动画的编号**
  记账,`RunningAnimationsSinceReset` 只反映本用例起的那些。

## D-133 C4:实例管理簇的依赖审计(任务书"自包含"第四次不成立)+ 照抄未改的一处 Windows 路径写法

- **任务书前提**:「五个文件(~75 KB)只差 `ModsDialog` / `SavesDialog` 两个对话框,其余服务都已移植」。
  **实测不成立**(与 D-117 / D-118 / D-121 同一类问题,**第四次**):
  | 缺什么 | 大小 | 处理 |
  |---|---|---|
  | `Models\ModsTarget.cs` | 201 B | `Copy-Item` 字节级搬进 `src\TipCL.App\Models\`(SHA256 与 Windows 源逐字节一致、无 BOM、LF) |
  | `Models\ModUpdateRow.cs` | 887 B | 同上 |
  | `Models\SaveRow.cs` | 426 B | 同上 |
  | `Models\BackupRow.cs` | 222 B | 同上 |
  | `Models\MediaRow.cs` | 207 B | 同上 |
  | `Models\GlobalModRow.cs` | 183 B | 同上 |
  | `Models\VersionRowDto.cs` | 736 B | 同上 |
  | `Pages\GlobalModsDialog.cs` | 1956 B | **不是 DTO,是 WPF 代码** ⇒ 按同一套换名规则移植(它是本包**唯一**超出"五个文件"清单的产品文件;`ModsDialog.cs:77` 的「全局模组」按钮直接调它,不搬就是静默桩) |
- 门面计数(任务书给的 `Ui` x58 / `Dlg` x9 / `I18n` x98 / `Motion` x1 / `EnableDrop` x3 / `Thumbs`·`LoaderIcons` x3 等)**逐项复核吻合**,
  缺的只有模型层与那一个小对话框。`src\TipCL.Core` 一字节未动。
- **按红线停下并报告的一处**:`InstanceDetailPage` 的「设置」分段内嵌 Windows 的 `VersionManagePage`
  (34490 字节的**整页**,属别的工作包)——见 D-134。
- **照抄未改的一处 Windows 写法(用户可见,请 Lead 知悉)**:`LibraryPage.FolderPathFor` 在实例没给 `Path` 时
  用 `_rootPath.TrimEnd('\\','/') + "\\" + name` 拼一条**反斜杠**路径。mac 上这会在"路径缺失"那一支显示成
  Windows 风格路径。本包按"忠实移植"保留(Windows 源码就是这么写的,同文件里 `AddFolderAsync` 又用
  `TrimEnd('\\','/')`,说明 Windows 自己也是混的);要改成平台路径请单开小包,**本包不擅自改语义**。

## D-134 C4:`InstanceDetailPage` 的「设置」分段走注入缝 + 如实占位(用户可见降级,请 Lead 裁决)

- **事实**:Windows 的「设置」分段是 `_settings ??= new VersionManagePage { Folder = _inst }`,而
  `VersionManagePage` 是 **34490 字节的整页**(mac 侧完全不存在,属 C3 的后续包)。任务书红线:大文件 / 整页
  **不许本包顺手搬**。
- **选择(保守 + 不静默)**:分段按钮**照 Windows 建**(「设置」是第 5 个 `ToggleButton`,文案、顺序、`Seg.Item` 类名一致);
  点击走 mac 侧新增的 `internal static Func<string, PageBase>? InstanceDetailPage.SettingsPageFactory`;
  **挂点没接时**那一栏显示一句如实的占位(「版本管理页还没移植」+ 为什么),
  **不是空白、也不是假装能用**(与 D-119 的 `CrashUi.AiFixLauncher`、D-122 的 `LaunchPage.FlyToTasksSeam` 同一口径)。
  接上工厂之后:页面照 Windows 走 `EnsureLoadedAsync()` + `OnShown()`,并且 `_settings ??=` 只建一次。
- **这是一处用户可见的行为差异**,请 Lead 裁决:接受这个降级,还是把「设置」分段排进版本管理页那一包
  (那时 `InstanceDetailPage` 一行都不用改,只要把工厂接上)。
- 另记:`InstanceDetailPage` **不是导航页**(Windows 也没有它的 id),它由实例库「实例详情」换进页内容;
  本包**没有**给它注册任何页面 id,并用一条用例把这个现状钉住。

## D-135 C4:写这一包的测试时实测到的四条(Avalonia / 本仓库纪律)

1. **`BridgeClient` 序列化会把非 ASCII 转义成 `\uXXXX`** ⇒ 断言"发给后端的参数值"时,直接
   `Assert.Contains("\"instance\":\"默认\"", 参数原文)` **必然失效**(原文里是 `\u9ED8\u8BA4`)。
   本包新增 `C4Files.ParamString` / `ParamStringIn` / `AssertParam`:按 JSON 解析后取**解码值**再比。
   这条与通道无关(两条通道都会红),但踩一次就要重跑一遍四条通道,所以记下来。
2. **Avalonia 的 `MenuItem` 在"程序化 Click"下不会翻转 `IsChecked`**(本机探针实测:`IsChecked` 保持原值、
   处理器读到的也是原值;键盘 Enter / Space 激活同样不抛 `Click`;而它自己的 `IMenuItem.RaiseClick` 是
   **internal**,测试里碰不到)。翻转发生在**指针点击那条路**里。⇒ 可勾选菜单项的用例按"真实点击进处理器那一刻
   的状态"**先把 `IsChecked` 翻转做掉再抛 Click**(`LibraryPage` 的「显示文件夹栏」用例就是这么写的,
   并在注释里写清了为什么)。
3. **离屏的 `DispatcherTimer`(220ms 防抖)在会被 `Sleep` 的泵里不可靠** ⇒ 页面"过滤 → 计数"这类断言
   改成:①先钉"防抖没过点之前计数不该变";②再 `RefreshAsync()` 走一遍 `Render()` 验过滤结果。
   另外 **`DateTimeOffset.Now.AddHours(-1)` 这种相对时间戳在凌晨 0~1 点会跨到昨天** —— 本包因此出现过
   "进程内通道绿、VSTest 通道红"(两条通道跑的时刻不同);改成"今天 00:05"与"它减一小时"之后与跑测时刻无关。
4. **分析器告警也算进红线**:`Assert.Equal(N, collection.Count)` 触发 xUnit2013、`Assert.True(list.Contains(x))`
   触发 xUnit2017,而红线是"除 `NU1900` 外只许 `SYSLIB0014` ×1" ⇒ 一律改用 `Assert.Single` / `Assert.Empty` /
   `Assert.Contains`;`Avalonia.Controls.Control` 上**已经有 `Loaded` 事件** ⇒ 测试探针页的字段不能叫 `Loaded`
   (CS0108 成员隐藏),本包改叫 `LoadRan`。

## D-136 C4:提交顺序、与 C3c 时代前提的冲突、以及和别人共树的纪律

- **提交顺序 (c)/(d) 对调**(如实记录):任务书要求先 `LibraryPage` 后 `InstancePage`,但
  `LibraryPage` 依赖 `InstancePage.SetDefaultAsync` 与 `InstanceDetailPage` ⇒ **先提交 InstancePage +
  InstanceDetailPage + instance 注册,再提交 LibraryPage + library 注册**,否则没有一个中间提交能编译
  (写桩是红线)。与 C3c / C2c2 的同类处理一致。
- **C3c 的既有用例按 D-126 还原前提**:`C3cPageStructureTests.Construction_does_not_throw_without_a_backend`
  里有一条"注册表里只有启动页"的断言,本包注册 `instance` / `library` 之后不再成立。处理:**先把前提还原**
  (`PageRegistry.Unregister` 两个 id,C3cScope 会在 Dispose 里还原注册表),原来那条断言**一字不改**地保留,
  并**另加**两条"C4 确实注册了它们"的断言 —— 断言只增不减,没有删测试 / 放宽断言 / 加 Skip。
- **共树纪律(本包实测)**:`tools\with-build-lock.ps1` 落地前后,本包遇到过两次外部干扰:
  ①`dotnet test` 通道在别人重编译同一棵输出树时从全绿变成 115 失败(Lead 已为此加了锁);
  ②收尾阶段 `src\TipCL.App\Pages\TasksPage.cs` / `FirstRunWizard.cs`(别的工作包)在树里**编译不过**,
  导致本包的构建命令直接失败。⇒ **凡是 dotnet 命令(含 smoke)都必须包在锁里**,并且**只认自己跑出来的
  绿色结论**:别人改坏的树不是我的失败,但也不能拿它当我的证据。


## D-137 C5b:`SkinPng` 报的是"小而自包含、字节级搬",实测搬不了(WPF 成像没有 Avalonia 等价物)+ 三条框架实测

**背景**:`AccountPage` 需要"把皮肤 PNG 拼成正面全身像"。Windows 的 `Pages\SkinPng.cs` 只有 3878 B,
我在审计时按"小而自包含"报给 Lead(和之前 D-117/D-118/D-121 那几次"自包含"的前提一样乐观),
Lead 批准"字节级搬进 `Models\`"。

**实测:搬不了。** 那份文件整个建立在 WPF 的成像 API 上,一个同名等价物都没有:

| Windows(WPF) | Avalonia 侧 | 处置 |
|---|---|---|
| `DrawingVisual` + `DrawingContext.DrawImage` + `ScaleTransform(8,8)` | 无 `DrawingVisual` | 换成 `Canvas` + `Image`(部件 8 倍放大、`RenderTransformOrigin(0.5,0)` 做镜像) |
| `CroppedBitmap` | 无 `CroppedBitmap` | 换成"整图放大后塞进一个 `ClipToBounds` 的 `Canvas`、再平移到部件位置"(数学等价,不需要像素级 API) |
| `BitmapImage` + `CacheOption.OnLoad` + `IgnoreColorProfile` | `Bitmap` 只能从流建 | 走本仓既有的 `ImageDecode`(注入缝 + Core 的 Skia 编解码器) |
| `BitmapScalingMode.NearestNeighbor` | — | `BitmapInterpolationMode.None`(Wallpaper.cs 的既有口径) |
| `RenderTargetBitmap(128, 256, 96, 96, Pbgra32)` | 有 `RenderTargetBitmap` | 保留 128x256/96dpi;另外**必须先 `Measure` + `Arrange` 再 `Render`**,否则拍出来是空白 |

**保留下来的是算法与全部常量**(这才是"移植"该保的东西):12 个部件的 `(sx,sy,sw,sh,dx,dy,mirror)` 与顺序、
逻辑画布 16x32、`slim` 的 3 像素臂宽、64x32 旧皮肤的 `height*2 <= width` 分支、输出一律 128x256、
`PixelWidth < 64 → null`、任何失败都 `return null` 不抛。落点按 Lead 指定放在 `src\TipCL.App\Models\SkinPng.cs`
(Windows 是 `Pages\`,命名空间改 `TipCL.Models` 以便 `AccountPage` 直接可见)。

**三条框架实测(都是本包真跑出来才发现的)**:
1. **`Button.Content` 改了,`AutomationProperties.Name` 不跟着变**:`Ui.Btn` 在创建时把文字写进自动化名,
   `Sync` 里把「取消」改成「清除」之后,自动化名仍是「取消」。用自动化名断言"现在显示哪个词"会拿到过期答案
   ⇒ 判断当前文案只能看 `Content`(本包第一版用例因此红了,修的是**测试期望**,产品代码没动)。
2. **`Control.IsVisible` 只是控件自己的标志**:祖先 `IsVisible = false` 时它自己仍是 `true`。
   Windows 藏的是 `logWrap`(那层 `Ui.Pane` 的 Border),所以断言必须落在那层壳上 —— 想一次问"到底看不看得见"
   要用 `IsEffectivelyVisible`(但离屏宿主窗口不一定算 visible,所以本包选了"断言那层壳"这条与 Windows 同构的路)。
3. **`Dlg.Panel` 推出来的不是 `DialogCard`**:`C3bFiles.WaitDialog()` 等的是 `DialogCard`(给 `Confirm`/`Ask` 用的),
   对 `Dlg.Panel` 必然等到超时。面板要按"标题文字"泵(`PanelCard`)。向导、三个对话框都踩得上这条。

## D-138 C5b:`SyncAccountChipAsync` 有意不搬;向导的设置刷新走注入缝;`MaybeShowAsync` 没有调用点

1. **`AccountPage.LoadAsync` 的头一行 `_ = AppServices.Window?.SyncAccountChipAsync();` 整条不写。**
   mac 外壳在 C2b 就**故意删掉了顶栏的 AI/主题/账号圆片**(`MainWindow.cs:40` 有记录,Lead 把 C2c3 那条待办降级为可选)。
   为它新增一个外壳方法就是给不存在的控件造同步活(GOAL §2.5 禁止的"假装完成")。Lead 已裁决:如实登记即可,
   这一条算 **C2b/C2c3 的已知缺口**,不算 C5b 的降级。代码里留了注释指向本条。
2. **`FirstRunWizard` 结尾那次"刷新设置页"走注入缝**:Windows 是
   `AppServices.Window?.GetPage("settings") is SettingsPage page → page.RefreshAsync()`,而 `SettingsPage`
   由 C5a 在另一个 teammate 手里。本包不凭空引用别人的页面类型,也不改外壳去接:留
   `internal static Func<Task>? SettingsRefresh`,**接了才调、没接就什么都不做**(与 D-119 / D-122 / D-131 同口径),
   接线由 Lead 在两边都落地后处理。有一条用例专门钉"缝为空时也走完、不抛、不假装刷新过"。
3. **`FirstRunWizard.MaybeShowAsync()` 在 mac 侧没有任何调用点**:Windows 是外壳启动时调它决定弹不弹。
   本包不碰外壳(红线),**如实记成"外壳级缺口"交 Lead 统一处理**;`MaybeShowAsync` 本身有用例(读 `first_run` 为假时不推浮层)。

## D-139 C5a:运行时色板覆盖层落在**主题层**(`Themes\PaletteOverride.cs`)—— 不进设置页,也不改 C1a 的 `Palette`

- **要解决什么**:Windows 的 `SettingsPage.Themes.cs` 直接往 WPF 的 `App.PaletteDictionary`(运行时可写
  `ResourceDictionary`)里写 24 个强调色键 + 3 个背景键(主题色 / 界面背景"立即生效"就是这一步)。
  mac 的内置色板(`Palette.Light/Dark.axaml`)是**编译进程序集的只读资源**,`Palette.Brush/Color` 又是
  **全局**读的(C1a 起所有页面与外壳都走它),所以"换个主题色立刻全局生效"只能靠**比内置色板更晚被查到**的一层同名键。
- **落点(Lead 裁决)**:新建 `src\TipCL.App\Themes\PaletteOverride.cs`(纯新增文件)。`Palette.cs`(C1a 的文件)一字节未动;
  覆盖层也**不**藏在设置页里 —— 否则就是一个"谁都不知道存在的全局",而且别的页面想用主题能力就得 import 一个页面类。
  这与 C3d 的裁决同一条道理:`FlyToTasks` 一次修在外壳,主题能力一次修在主题层。
- **机制**:一个自己的 `ResourceDictionary`,第一次写入时**追加**到 `Application.Resources.MergedDictionaries`
  末尾;Avalonia 的 `ResourceDictionary.TryGetResource` 先查自己的键、再**逆序**查合并字典(后合并的优先)。
- **探针实测**(`tests\TipCL.Tests\C5aPaletteOverrideTests.cs`,9 条,全绿):
  1. 后合并的那一层确实胜出:`{DynamicResource}` 绑上的 Border 背景在 `Set` 之后**当场**变成覆盖色,`Reset` 之后回到内置色;
  2. `Palette.Brush` / `Palette.Color`(全局读取口)同样看得见覆盖;
  3. `PaletteOverride.BuiltinColor` 读的是 axaml 直读缓存,**绕开**覆盖层 —— 这正是 Windows 那句
     `new ResourceDictionary { Source = PaletteUri(isDark) }` 的等价物(不绕开的话"把主题色恢复成默认绿"会读到被覆盖过的值);
  4. 深/浅两本内置色板的 `C.Paper` / `C.Canvas` 各不相同,`IsDarkNow` 跟当前变体走;
  5. 覆盖层"有条目才挂、清空即摘":`MergedDictionaries.Count` 回到进作用域前的原样(测试纪律:D-69)。
- **代价(如实记)**:覆盖层是**不按主题分**的一层普通键值,所以主题变体一变必须重算(设置页在
  `ApplyDark` 与 `Application.ActualThemeVariantChanged` 两条路里都调了 `ApplyVisuals()`)。Windows 因为每个变体
  有一份自己的字典,天生不用这一步 —— 这是 mac 侧唯一需要补的地方。

## D-140 C5a:设置页六个缺件走注入缝;`FeedbackPage` 从"字段初始化"改成"切段才建";D 类三处一并走缝

- **依赖审计(四类)**:A 类 = 本身是页面的缺件六个:`AccountPage` / `JavaPage` / `ServersPage` / `PlaytimePage` /
  `FeedbackPage` / `FirstRunWizard`(另有 `PhoneSyncPanel`);B 类 = 主题运行时改写的基础设施(= D-139);
  C 类 = 小而自包含的 DTO 七个(`CleanerEntry` / `CleanerPreview` / `CleanerResult` / `ThemeRow` /
  `BackgroundState` / `SmartRecommendation` / `UpdateInfo`,共 2118 字节,字节级搬);D 类 = 离屏/测试不能碰的三处。
- **A 类做法**(照 D-119 / D-134 的先例,请 Lead 裁决):`SettingsPage.AccountPageFactory` 等六个
  `Func<PageBase>` / `Func<Task>` 注入缝;**没接缝时显示一句如实的占位说明**(`Ui.Empty`:说清这一栏内嵌的是哪个页面、
  属后续工作包、接上缝就会显示),不空白、不假装能用。`AccountPage` 与 `FirstRunWizard` 本轮由 c4-dialogs 的 C5b 移植,
  我**不碰**那两个页面文件,只留缝。
- **`FeedbackPage` 必须懒建**:Windows 是字段初始化(`private readonly FeedbackPage _feedback = new FeedbackPage();`),
  构造设置页就会连带构造它 —— mac 侧那页还不存在,所以改成"切到第 6 段时才经工厂建"。
- **实测踩到的 bug(值得记)**:一开始我把"段视图"和"内嵌子页"共用一个 `_views[7]` 缓存,于是分段 6 的段视图
  (上标题卡 + 下内嵌页的 Grid)被 `SubPageFor(6)` 缓存进去的子页**顶掉**,"关于"那段整块不见了(两条用例红)。
  修法:`_subs[7]`(子页实例)与 `_aboutView`(段视图)分开。
- **D 类三处**:重启启动器 = `ProcessLauncher` 注入缝(默认真起进程 + 桌面生命周期 `Shutdown`;离屏没有桌面生命周期
  ⇒ 只起不退,如实记录,**只有真机能完整验证**);复制 MCP 地址 = `ClipboardWriter` 注入缝(默认走
  `TopLevel.Clipboard`,`Dlg.SmokeActive` 时**不碰**真剪贴板,与 `CrashUi.ClipboardWriter` 同一模式);
  内置预设背景缩略图 = 走 C2c2 已落地的 `Wallpaper.OpenPreset` + `ImageDecode`(不自己新写解码)。
- **可测性(同 D-115 / D-127)**:色块与预设缩略图的点击体抽成 internal `PickSwatch` / `PickPreset`。
  原因实测:`PointerReleasedEventArgs` 的构造函数不公开,硬 `RaiseEvent(new RoutedEventArgs(PointerReleasedEvent))`
  会 `InvalidCastException`(本包踩到)。

## D-141 C5a:只在 VSTest 通道红的两条用例 —— "数调用次数当同步手段" + "进程级静态状态"

- **现象**:同一份产品代码,进程内通道全绿,VSTest 里 1~2 条浮动(`Saving_a_theme_pack_prompts_then_calls_save_theme`、
  `The_language_preview_calls_translate_three_times`)。Lead 用"同一次锁里 build + 两通道"的原子脚本复现过。
- **根因 1(计数当同步)**:`Assert.Equal(0, api.Count("save_settings"))` 与 `Assert.Equal(3, api.Count("translate"))`
  都是"数调用次数"。设置页的自动保存是 500 ms 防抖(`DispatcherTimer`),机器忙的时候它会在用例中途真的 Tick 一次
  ⇒ 多一条 `save_settings`;语言预览那条则在 3 次 `translate` 的续体还没回来时就数了次数。
- **根因 2(进程级静态)**:`SettingsPage` 的 `_visualColor` / `_visualBackground` / `Extras` 是**静态**的,
  别的用例类动过就会漏进来(实测:保存主题包那条读到了上一条留下的 `ui_bg_preset="eye"` 与 `theme_color`)。
- **修法(不放宽断言)**:
  1. 等**效果**落地再断言:语言预览那条泵到"预览文案出现"才计数;
  2. 载荷断言按**最后一次**同方法调用取 —— 新增 `C5aFiles.ParamAt(api, method, index, path...)` 重载;
  3. 用例自己把界面状态钉死(显式 `PickSwatch("#2E9B6B")`、把背景选回"默认");
  4. `C5aScope` 补还原设置页的静态视觉状态与 `ExtrasDirOverride`(D-69)。
- **纪律**:等待一律"条件成立 + 有截止时间",**不要**写"数调用次数"或"泵固定轮数";断言失败信息里带上实际观察值。
- 佐证:修完后 `tools\c5a-verify.cmd`(build + VSTest + 进程内,一次锁里)连跑两轮,`C5a*` 用例两通道全绿。

## D-142 C5c:`Ui.Section` 在窄页面降级为两行 —— 阈值用 `DesiredSize` 自校准 + 回滞,宽页面零像素变化

**问题(实测,不是猜)**:mac 的页面区被 196px 侧栏压到约 **668px**,而 Windows 版没有侧栏 ⇒ 只在移植后暴露。
`Ui.Section` 原样照抄 Windows 的 `G(null, "*,Auto")`:左列是标题块(`*`、**没有最小宽**),右列是调用方给的动作簇
(实例页 = 计数 + 搜索框 + 4 个按钮 ≈ **653px**,也**不收缩**)⇒ 窄页面时左列被算成 **0 宽**。
探针实测:`页面宽=668 标题宽=0 副说明宽=0 右簇宽=653`(副说明早就设了 `CharacterEllipsis`,但 0 宽时截断也救不了它)。

**修法**:门面里加 `SectionNarrow.Watch(grid, head, right)`(与 `Section` 同文件)。
- **阈值自校准**:`可用宽 < head.DesiredSize.Width + right.DesiredSize.Width` 才降级(把右簇换到第二行、跨两列、靠右)。
  不写"窄于 560 就怎样"这种魔数 —— 魔数会在别的页面/别的字号上失效,而 `DesiredSize` 永远是当下的真值。
- **回滞 `Slack = 6px`**:**必须有**。没有它,降级/恢复会互相触发 —— 换了行 → 宽度判定变化 → 又换回来 → 死循环/抖动。
- **宽页面零变化**:摆得开就永远走原来的两列路径,所以 `launch` / 首屏 / `settings` / `tasks` / `account` 的冒烟帧**逐字节不变**
  (实测:tasks 右簇窄、account 右簇 ~420,都不触发;settings 那处 Section 根本没有右簇)。

**为什么修在门面**:`Ui.Section` 被 4 个页面用(`AccountPage` / `InstancePage` / `TasksPage` / `SettingsPage` 的关于卡),
在每页各打一个补丁既会漏(以后新页面照抄就有缺陷)又不一致 —— 与 C3d 的 `FlyToTasks`、C3e 的 `Motion` 同一条道理。

## D-143 C5c:库页左偏的真机制是"卡片宽于自己的星列后被居中溢出";`C4LibraryTests` 与修复的两全,以及第 3 条异常的结论

**1. 真机制(与"ScrollViewer 水平偏移"的猜想不同)。** 探针把整条链读出来了:
`Offset=(0,0) Extent=0 Viewport=0`、`滚动框 0..668`、`Grid 在 x=18 宽 632`、`列宽=205/18/409`,而
**左卡 `布局x=-48`、宽 300** ⇒ `-48 = (205-300)/2`:卡片比它的格子宽 95,而 Avalonia 把"宽于格子的子元素"**居中**摆放。
根因在 `LibraryPage.cs:232` 的 `left.MinWidth = 300`(2026-11-07 为"窄窗别占太多"留的地板)与 `ApplyFolderLayout` 里
**只给星列、没给最小宽**这对矛盾。用户可见效果:压掉 18px 左页边距(白卡白像素 **196 → 216**)并向右盖住右栏标题的头几个字。
⚠️ 教训(值得记):**子元素的最小宽大于它所在星列的最小宽时,得到的不是"占位"而是"越界"**。

**2. 修法:把地板同时写到列上**(`ColumnDefinitions[0].MinWidth = 300`)⇒ 卡片正好填满自己的列,既不越界也不越位;
右列 409 → 314,新增探针断言"右栏内容不得宽于自己的列"(实测不超),冒烟帧逐帧看过。
第一版是"把卡片地板降到 0"(更小、更简单),但 `C4LibraryTests.Library_folder_layout_defaults_to_two_columns` 钉着
Windows 的几何(`*,18,2*` + 左卡 `MinWidth=300 / MaxWidth=380 / Top`),那条用例因此红了 ⇒ **不许为了让自己的修复好看就去改别人的断言**
(Lead 的纪律),改成"列兑现地板"之后**两边同时成立**。这也是"宽页面像素不变"的另一种体现:星列在宽窗口本来就 ≥ 300,MinWidth 从不生效。

**3. 第 3 条异常(库页"看起来被加载过")的结论:`PageHost` 门闸没漏开,不是 bug。**
`PageHost.Navigate` 只在 `runHooks` 为真时跑 `EnsureLoadedAsync + OnShown`(PageHost.cs:108);`MainWindow.Navigate` 传的
`BridgeReady` 默认 false 且 `--smoke` 不调 `StartBackend()`(MainWindow.cs:427 注释明写"离屏 --smoke 不走这里")⇒ 冒烟里确实不跑加载钩子。
库页帧里那些计数与空态是**构造函数**里的 `RenderGroups()`(`LibraryPage.cs:244`)画出来的 —— 两边的说法都成立,矛盾出在"看见了什么"而不是"门闸开了"。
