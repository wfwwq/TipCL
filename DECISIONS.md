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
