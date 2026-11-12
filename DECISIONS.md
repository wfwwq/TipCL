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
