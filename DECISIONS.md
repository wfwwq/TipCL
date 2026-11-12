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

