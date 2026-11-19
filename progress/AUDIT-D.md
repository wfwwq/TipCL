# AUDIT-D —— 测试与构建基础设施审计(C# / MSBuild / GitHub Actions / PowerShell / Bash)

- 线路:D(测试 + 构建 + CI + 脚本)
- 分支:`audit/D`(基于 main;本轮 rebase 到 main 最新提交后复跑)
- 环境:Windows 10 19045 / .NET SDK 8.0.428(`D:\项目\TipCL\_tools\dotnet`,不在 PATH)
- 跑测所需环境块(踩过坑,写死在这里):
  ```powershell
  $env:PATH="D:\项目\TipCL\_tools\dotnet;$env:PATH"
  $env:DOTNET_ROOT="D:\项目\TipCL\_tools\dotnet"          # 缺这条 → 测试宿主 hostfxr.dll not found
  $env:DOTNET_CLI_HOME="D:\项目\TipCL\_tools\home-D"
  $env:NUGET_PACKAGES="D:\项目\TipCL\_tools\nuget"
  $env:TEMP="D:\项目\TipCL\_tools\tmp-D"; $env:TMP=$env:TEMP
  ```
  另:五路并行时 NuGet 会抢 `NuGetScratch\lock` 报「无法获取锁定文件访问权限」,不是配置错;
  等 1~2 分钟重试,或 `dotnet build-server shutdown` 后加 `--no-restore`。

---

## 1. 基线测试结果(全量,可信)

### 1.1 结论表

**(A) 基线(@ b86afe3,未加任何新测试 —— 与 aud-core / aud-app 对齐的那一轮)**

| 批次 | 命令 | 总数 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- | --- | --- |
| Linux 侧 批次 1(App 无头 UI) | `--filter "FullyQualifiedName~TipCL.Tests.App"` | 355 | 355 | 0 | 0 |
| Linux 侧 批次 2(其余全部) | `--filter "FullyQualifiedName!~TipCL.Tests.App"` | 426 | 425 | 0 | 1 |
| Windows 侧(net8 宿主 + net48 被测程序集) | `dotnet test windos/tests/TipCL.Tests` | 547 | 547 | 0 | 0 |

- 全量基线 = Linux 355 + 426 = **781 通过 / 0 失败 / 1 跳过**;Windows 侧 **547 通过 / 0 失败**。
- 与 aud-core(781 / 0 / 1)、aud-app(App 355 / 0)的口径**完全对齐**,没有额外新红。

**(B) 本线收工时(rebase 到 main @ `c3ff092` 之后,含 A/B/C 的合并 + 本线 123 条新测试)**

| 批次 | 总数 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- | --- |
| Linux 侧 批次 1(App) | 358(+3:`FpsOverlayTests`,来自 main 的 `4df692b`) | 358 | 0 | 0 |
| Linux 侧 批次 2(其余,含本线新增 123 条) | 549 | 548 | 0 | 1 |
| Windows 侧(Debug 与 Release 各一遍) | 547 | 547 | 0 | 0 |

- 收工全量 = Linux **907**(906 通过 + 1 跳过)/ 0 失败;Windows **547** / 0 失败。
- rebase 前后差 6 条的假象已排除:那 6 条是本线中途建的临时诊断用例(`TempDiag`),删除后二进制没重建导致
  上一轮 TRX 里还留着它们;按 TRX 逐条比对确认当前数字是**净**结果。
- 唯一跳过项:`TipCL.Tests.Smoke.SmokeIntegrationTests.SmokeArg_Renders_MainWindow_To_SmokePng`
  —— 设计上跳过(真跑要做进程内渲染冒烟,由 CI 的 verify-linux / tools/verify-smoke.ps1 覆盖)。

### 1.2 逐条失败归类

**本轮没有失败用例**,因此 (a)/(b)/(c) 三类空表:

| 全名 | 批次 | 归类 | 证据 | 处置 |
| --- | --- | --- | --- | --- |
| —(无) | — | — | 见 1.1 三轮复跑与 §2 稳定性复跑 | — |

> 需要如实说明的两条边界:
> 1. **本机是 Windows**,批次 1/2 的二进制虽然 target net8、代码里也有 `OperatingSystem.IsWindows()`
>    分支,但"Linux-only 路径语义(大小写/`/`/时区/locale)"在本机**无法证伪**。凡是靠
>    ubuntu-latest 才暴露的差异,只能由 ci.yml 的 `test (ubuntu-latest)` job 兜底,本报告不假装跑过。
> 2. **`AI 文件工具`(AiFileToolsTests)当前不红**:Windows 侧 547 条含 `AiFileToolsTests`,Debug/Release
>    各跑 3 次全绿。历史上的"Windows 上必红"结论**已不成立**(或早就被修掉了),不要照抄旧结论。

---

## 2. 测试隔离与 flaky 审计

### 2.1 现状(机制是对的,已核到实现)

| 机制 | 位置 | 评价 |
| --- | --- | --- |
| 进程级数据根隔离 | `tests/TipCL.Tests/TestIsolation.cs:19`(ModuleInitializer)+ `windos/tests/TipCL.Tests/TestIsolation.cs:23` | 正确:同时设 `TIPCL_HOME` 与 `Utils.SetRoot()`,两条取根路径(`Utils.ROOT` / `Utils.ResolveRoot()`)都钉住;目录名带 pid+guid,TEMP 不受并发线路影响 |
| 串行执行 | `tests/TipCL.Tests/AssemblyInfo.cs:6`、`windos/tests/TipCL.Tests/TestHome.cs:7` | `DisableTestParallelization = true`,两个测试工程都设了 —— 与"共享 Application.Resources / TaskStore / Config"一致 |
| 写盘越界守卫 | `tests/TipCL.Tests/TestWriteGuard.cs` | 把 `Updater.TargetOverride` 进程级钉在隔离根内,挡住 ubuntu CI 那条「自更新把 runner 的 dotnet 写坏 → 下一条命令 exit 127」的间歇红 |
| 无头 UI 会话复用 | `tests/TipCL.Tests/App/TestApp.cs` | 单一 `HeadlessUnitTestSession` + `PinSessionDispatcher()` 兜住 Avalonia 每次派发边界的 UIThread==null 窗口;注释里已写明两类随机失败的成因 |
| 隔离回归守卫 | `windos/tests/TipCL.Tests/TestIsolationTests.cs` | 断言根不在 `%LOCALAPPDATA%\TipCL`、写盘落在隔离根、探针不落到真实用户目录 —— 这套守卫值得 Linux 侧也来一份(见 P2-4) |

### 2.2 稳定性复跑(找 flaky)

| 目标 | 次数 | 结果 |
| --- | --- | --- |
| Linux 批次 2 | 4 次(`--no-build`) | 425/425/425/425 通过,每次 10~12 s,零抖动 |
| Linux 批次 1(App) | 3 次(`--no-build`) | 355/355/355,每次 18~48 s,零抖动 |
| Windows 侧 | 4 次(Debug×3 + Release×1) | 547/547/547/547,零抖动 |

结论:**这批用例当前不 flaky**;同时段其他线路在并行构建,CPU 抖动没有传导成红,说明没有靠时序的临界断言。

### 2.3 残余风险(未修,只登记)

1. **固定时间/随机/端口**:全仓测试里没有 `Thread.Sleep` 轮询式断言之外的裸 `DateTime.Now` 比较;
   搜到的都是 `DateTime.UtcNow + timeout` 式**上限**等待(带循环退出),这类写法在负载高时只会变慢、不会假绿。
   `AiServiceFilesTests.cs:476` 用 `DateTime.Now.ToString("yyyyMMdd")` 拼日志文件名 —— 跨零点跑会换日,
   单条用例内不会跨天,可接受。
2. **`Config.CONFIG` 的跨用例污染**:多数用例是 `before=Get(); try{...} finally{Set(before)}` 的正规写法
   (AiToolsTests / ApiContractsTests / CatalogApiTests / FeedbackApiTests / LaunchResolutionTests / ThemeToggleTests),
   但**是进程级共享单例 + 隔离根里同一份 config.json**:漏还原一处就会以"执行顺序"为条件红。
   现在没红,但这是本工程最脆弱的一处;建议后续给 `Config` 加一个 `IDisposable` 快照,不要靠人工对称。
3. **`Updater` 更新目标注入点是进程级全局**:`TestWriteGuard.SuppressSandbox()` 用 try/dispose 包住,
   一旦有人在 `SuppressSandbox()` 里断言失败(异常先抛出、Dispose 仍会跑),是安全的;但若有人**忘了**用
   `using`,沙箱会一直处于关闭状态 —— 目前只有一个用例(`UpdaterTests.UpdateTarget_PrefersTheAppImagePath`)这么用,已核。

---

## 3. 问题表(按级别)

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| D-01 | **P1** | `tools/verify-linux.sh:9` | 默认产物目录写的是 `dist/linux-x64`,而出包链路(`tools/package.sh`、`tools/package.ps1`、`ci.yml`、`release.yml`)**全部**发到 `dist/TipCL-linux-x64` ⇒ 裸跑一次自检脚本必然停在「找不到产物」并 `exit 1`,自检形同不存在 | 默认值改为 `$ROOT/dist/TipCL-linux-x64`;保留旧目录名只作后备(且打印提示),不静默改道 | `tools/verify-linux.sh` 第 5/9-16 行;新增守卫用例 `RepoConsistencyTests.VerifyLinuxScriptDefaultsToTheDirectoryThePackagingScriptsActuallyProduce` | ✅ 已修 |
| D-02 | **P1** | `.github/workflows/release.yml:83`(原) | 发布前的 AppImage 冒烟只跑一次 `--smoke` 再 `ls -l smoke.png`:**进程返回 0 但根本没画出来**(缺图/空图)、日志里一堆 `[WARN]` 都算过 ⇒ 发布 job 给假绿 | 改成与 ci.yml `verify-linux` 同一套判据:退出码 0 + `smoke.png > 10 KB` + 日志 0 个 `[WARN]`/`[ERROR]`,失败打 `::error` 注解并 `tail` 日志 | 同文件 82-121 行(4 条 `chk` + 失败退出 1) | ✅ 已修 |
| D-03 | **P2** | `tools/verify-linux.sh:46` | 逐帧循环里**不清上一帧的 `smoke.png`**:某帧进程返回 0 但没写出图时,`[ -s "$OUT/smoke.png" ]` 命中的是上一帧残影 ⇒ 单帧假绿 | `run_frame()` 开头 `rm -f "$OUT/smoke.png"` | 同文件 46-49 行 | ✅ 已修 |
| D-04 | **P2** | `.gitattributes` | 没有任何 `*.sh` 规则。仓库主体在 Windows 上开发(`core.autocrlf` 常见为 true),`tools/*.sh` 一旦按 CRLF 结帐,Linux/CI 上就是 `bash: $'\r': command not found`(脚本一个字没改却红) | 新增 `*.sh text eol=lf` 段 | `.gitattributes:22-29`;新增守卫 `RepoConsistencyTests.AllShellScriptsAreLfOnlyWithoutBom`(逐字节查 CR / BOM / shebang) | ✅ 已修 |
| D-05 | **P2** | `.gitignore` | 漏 `TestResults/` 与 `*.trx`:本机 `dotnet test -l trx` 的默认落点不在忽略范围 ⇒ 工作区常年脏,容易把产物提交进仓库(本轮就发生过:`.dotnet-home/`、`_logs/` 一度成为未跟踪目录) | 补两条 | `.gitignore:3-5`;新增守卫 `RepoConsistencyTests.GitIgnoreCoversTestAndBuildArtifacts` | ✅ 已修 |
| D-06 | **P2** | `tools/build.ps1:73`、`tools/package.ps1:66`、`tools/verify-smoke.ps1:178` | 冒烟阈值三处不一致:`1 KB` / `1 KB` / `B>0`,而 CI 判据是 `>10 KB`。1 KB 的门槛连"近空白帧"都能过(实测正常帧 569 KB) | 三处统一到 `> 10240 B`,与 CI 口径一致 | 本机实跑 `TipCL.App --smoke`:`smoke.png` 569,739 B、exit 0、0 WARN | ✅ 已修 |
| D-07 | **P2** | `.github/workflows/ci.yml:65` | 注释里的测试条数是历史口径(346+404+1=751),与实测 781 不符;这类"注释骗人"会让后来者拿错的数字判断有没有漏跑 | 更新为 2026-11-20 复算(355+425+1=781),并保留旧值作对照 | 同文件注释 | ✅ 已修 |
| D-08 | **P2** | `.github/workflows/ci-windows.yml` | (1) 没有 `workflow_dispatch`,想手动验一次都做不到;(2) 刻意不进 sln 的 `windos/tests/TipCL.NativeBlockE2E` 在 CI 里**从不编译** ⇒ 引用/HintPath 改坏了也全绿 | 加 `workflow_dispatch`;加一步 `dotnet build .../TipCL.NativeBlockE2E.csproj -c Release`(只编不跑) | 本机实编通过(0 warning / 0 error);同文件 | ✅ 已修 |
| D-09 | **P2** | 全仓 | 版本号散在 5 处(`CoreInfo.cs` / 两个 csproj / `update.json` / `update-linux.json`)且**没有任何守卫**;漂移的后果是 CI 的「.deb 文件名 == `tipcl_<CoreInfo 版本>_amd64.deb`」判据红,或客户端永远认为自己是最新 | 加守卫用例 `RepoConsistencyTests.VersionIsIdenticalEverywhereItIsSpelled` + `UpdateManifestsPointAtTheirOwnArtifactAndCarryASha256` | 当前 5 处都是 `1.2.0`,用例绿 | ✅ 已修(补守卫) |
| D-10 | **P2** | 测试覆盖 | 下列高风险纯逻辑此前**零覆盖**:`Downloader.SplitRanges`(下载分段/续传元数据)、`SemVer` 解析与范围、PyJson 畸形输入、`LayoutSchema.SanitizePage/SanitizeBlock`、词表 LogicalName ↔ 读取侧一致性 | 补 5 个测试文件(123 条)+ 修 1 条写死版本号的旧断言 | 见 §4 | ✅ 已修(补测试) |
| D-11 | **P2** | `windos/src/TipCL/TipCL.csproj:53` + `FodyWeavers.xml:8` + `release.yml`(windows job) | 构建同时产出 `TipCL.exe`、`TipCL.exe.config`(含 `System.Memory 4.0.1.2`、`System.Runtime.CompilerServices.Unsafe 6.0.0.0` 两条 bindingRedirect)与 `System.Text.Encoding.CodePages.dll`(Costura **刻意**排除不嵌),而 release 只上传 `TipCL.exe` —— 看起来像"产物缺文件" | **不改**(交付物集合是版本决策)。已用**实测**证伪"缺文件会坏":把 Release 的 `TipCL.exe` **单独**拷进空目录跑 `--smoke` | exit 0、12.7 s、10 页全 `ok`、15 次点击全 `ok`、8 个对话框全答完、`issues: []`、`i18n_missing: []`,并在 `TIPCL_HOME/_wpf_smoke/run-20261120-004826/` 落下 10 张 40~96 KB 截图 + `report.json` ⇒ Costura 单文件自足,`.config` 与 `CodePages.dll` 都不需要随包 | ✅ 已验证无需修 |
| D-12 | **P2** | `windos/tools/pack-core-resources.ps1:10` | 注释说"固定 gzip level 9",代码用 `CompressionLevel.Optimal`(≈level 6)。确定性结论(.NET GZipStream 写 MTIME=0)不受影响,但注释与实现不符 | 未改(只改注释价值低,且该脚本属 Windows 工具链,归 C 线复核更合适) | 已实测:重复跑产物字节一致(守卫 `CoreResourcesPackTests` 绿) | 📝 报告 |
| D-13 | **P2** | `tests/TipCL.Tests/SmokeTests.cs:17` | 断言把产品版本**写死**成 `"1.2.0"`:每次抬版本这条必红,而"改测试让它绿"是最容易滑向假绿的一步(属于 (a) 类:测试自身写错) | 只钉 `CoreInfo.Version == AppInfo.APP_VERSION` + 版本号形状(`major.minor.patch`,`deb.sh` 直接拿它拼文件名);数值一致性交给 `RepoConsistencyTests`(同时覆盖两个 csproj 与两个 update 清单) | `SmokeTests` + `RepoConsistencyTests` 共 9 条全绿 | ✅ 已修 |

---

## 4. 新增测试清单(123 条,全部 <3 s、确定、无网络、无全局状态)

| 文件 | 条数(TRX 实测) | 钉住什么 |
| --- | --- | --- |
| `tests/TipCL.Tests/Core/SemVerTests.cs` | 45 | 解析容错(`1` / `1.2` / `v1.2.3` / `+build` / 空预发布段)、数值化比较(`1.10.0 > 1.9.0`)、预发布排序(`beta.2 < beta.10 < 正式版`)、范围(`^ ~ x.* \|\|` / 连字符区间 / 部分段边界 `>=22`)、坏输入返回 false 而不是抛 |
| `tests/TipCL.Tests/Core/PyJsonMalformedTests.cs` | 36 | 14 种坏输入必须抛 `PyException` 家族、且 `LoadsOr` 一律回落默认值(它只 catch `PyException`,漏一种就是"配置文件被截断 = 启动器崩")、Python 方言(`NaN`/`Infinity`/超大整数精确往返/重复键后值优先)、ASCII 转义与 `DumpsU` 往返 |
| `tests/TipCL.Tests/Core/DownloadSegmentsTests.cs` | 25 | `Downloader.SplitRanges` 的**不变量**:段从 0 起首尾相接无空洞无重叠、末段末端 == `total-1`、段数不超请求值、除末段外等长、<2 MB 不切碎、0 字节返回空表、同输入同输出 |
| `tests/TipCL.Tests/Layout/LayoutSchemaSanitizeTests.cs` | 9 | 手改/损坏 JSON 的兜底:页 version 门禁、未知枚举回默认、列数吸附 `{2,3,4,6,12}`、`X/W/Span` 夹到列数、`Y/H` 只受 `RowMax` 约束、canvas 归一化区间、`NaN` 回默认、重复/空 id 丢弃、`Z` 补序 |
| `tests/TipCL.Tests/Repo/RepoConsistencyTests.cs` | 8 | 版本六处一致(`CoreInfo` / `AppInfo.APP_VERSION` / 两个 csproj / 两个 update 清单)、两个更新清单指向各自产物且 sha256 形状正确、`*.sh` 全 LF 无 BOM 有 shebang、`.gitattributes` 有 `*.sh eol=lf`、`.gitignore` 覆盖产物、两个 sln 登记齐全、内嵌词表 LogicalName 与读取侧一致、`verify-linux.sh` 默认目录 == 出包目录 |

合计 **123** 条(批次 2 从 426 → 549,差额即新增;TRX 逐条核对过)。
`RepoConsistencyTests` 找不到仓库根(例如测试被复制到仓库外跑)时**安静返回**,不制造环境相关假红。

另修 1 条**写错的旧断言**:`tests/TipCL.Tests/SmokeTests.cs:17` 把产品版本写死成 `"1.2.0"` —— 每次抬版本这条必红,
而"改测试让它绿"正是最容易滑向假绿的地方。改为只钉「`CoreInfo.Version` == `AppInfo.APP_VERSION`」+
版本号形状,数值一致性交给 `RepoConsistencyTests`(见 D-13)。

---

## 5. 构建 / MSBuild / CI 审计明细(无缺陷项也记,便于交接)

### 5.1 `*.csproj`

| 检查项 | 结论 |
| --- | --- |
| TargetFramework | `TipCL.App`/`TipCL.Core`/两处测试 = net8.0;`windos/src/TipCL` = net48;`TipCL.NativeBlockE2E` = net48(刻意按路径构建) |
| LangVersion | App `latest`、Core 12.0、windos 12.0、win 测试 12.0 —— 一致到 C# 12,没有混用 preview 语法 |
| Nullable | 测试工程 `enable`,产品工程 `annotations`(**刻意**:反编译还原代码开全量分析会引入海量 CS86xx)—— 与注释口径一致 |
| EnableWindowsTargeting | 只在 `windos/src/TipCL` 需要,已设;配合 `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 ⇒ 非 Windows 也能编 net48 |
| InternalsVisibleTo | Core → `TipCL.Tests`、App → `TipCL.Tests` 都有(无 `PublicKey`,仅测试用;可接受) |
| EmbeddedResource LogicalName | Linux:`TipCL.Core.locales.{en,zh_CN}.json` 与 `CoreResources.ListDir("locales")` 逐字一致(有守卫);Windows:`.gz` 源保留**未压缩名**作 LogicalName(`.gz` 不能带进名字,否则 `ListDir` 会过滤掉),`%(Filename)` 去 `.gz`,多点名 `tipcl-plugin.d.ts.gz` 单独写全名 —— 全部与读取侧对得上,且 `CoreResourcesPackTests` 逐字节守着 |
| Costura/Fody | `FodyWeavers.xml`:`ExcludeAssemblies="WebView2Loader|System.Text.Encoding.CodePages"`(分隔符是 `|`,注释专门提醒 6.0.0 的 schema 写法)、`IncludeDebugSymbols=false`;`PrivateAssets=all` 防住"测试宿主加载 net48 WPF 程序集"(csproj:57-63 的注释写明了 VSTest UniqueID 枚举 `AssemblyAssociatedContentFile` 会 FATAL) |
| PublishTrimmed | 两处 `publish` 都**没开**(Core 的 STJ 反射 + Avalonia XAML),口径统一 |

### 5.2 两个 sln

- `TipCL.Linux.sln`:src/TipCL.App、src/TipCL.Core、tests/TipCL.Tests —— 与仓库里存在的 3 个工程一致(已加守卫)。
- `windos/TipCL.sln`:src/TipCL、tests/TipCL.Tests;**`TipCL.NativeBlockE2E` 刻意不登记**(csproj 头部说明:net48 控制台 + 真 WPF,按路径构建)。已核:该工程独立编译通过,并在 ci-windows.yml 里补了编译步骤,防止它无声腐烂。

### 5.3 `.gitattributes` / `.gitignore`

- `.gitattributes`:原有规则只覆盖 `windos/src/TipCL/Resources/**`(离线 gzip 逐字节比对的源)与 `.gz binary`,**刻意不写 `* text=auto`**(会改写全仓结帐行为);本轮补 `*.sh text eol=lf`(见 D-04)。
- `.gitignore`:原有 `bin/ obj/ dist/ smoke.png smoke-*.png *.user *.suo *.pdb publish/ artifacts/ _cmp/ scratch/ _wph_run/ _wpf_run/ .testtmp/ *_wpftmp.*` 已够用;本轮补 `TestResults/`、`*.trx`(见 D-05)。

### 5.4 CI 工作流

| 检查项 | 结论 |
| --- | --- |
| `paths-ignore` 与仓库结构 | `ci.yml` 忽略 `windos/**` + 两个 Windows/release 工作流(根目录是 Linux 移植,正确);`ci-windows.yml` 用 `paths: windos/**` 正向匹配 —— 唯一不对称:`ci-windows.yml` 的 `push` 只挂 `main`,而 `ci.yml` 挂 `**`。分支上的 Windows 改动不会被 Windows CI 验到(可接受,但要知情) |
| `needs` 依赖 | `verify-linux: needs: package-linux`(下载 artifact 再验"用户真正下载到的那份",顺序正确);`package-win` 与其余无依赖,可并行 |
| artifact 路径 | Linux:`dist/TipCL-linux-x64.AppImage`、`dist/*.deb`、`dist/TipCL-linux-x64/TipCL.App`、`*.zip`;verify 证据 `dist/verify-evidence`(`if: always()` 上传,失败也能看);Windows:`dist/TipCL-win-x64.zip`、`TipCL.App.exe`;两处 `if-no-files-found: error` 兜住"静默出空包" |
| `--smoke` 判据 | `<退出码 0>` + `<smoke.png > 10 KB>` + `<0 WARN>` + `<0 ERROR>`,四条各自 `chk` 累加 `FAILS` 后统一退出;`set +e`(为了收齐结论)**不会**吞结论,因为末尾 `exit $([ "$FAILS" -eq 0 ] && echo 0 || echo 1)` |
| 版本三方一致 | `.deb` 文件名 ← `deb.sh` 从 `CoreInfo.cs` 取;CI 用 `sed` 再抽一次源码版本比对 `dpkg -s` 的 `Version`;`update*.json` 的 version 由发版人手工填(release.yml 打印现成的 3 行片段,降低手误)。本轮补了守卫用例,把"手工填"变成"填错就红" |
| 失败可诊断性 | verify-linux 每步 `tee` 到 `verify-evidence/*.md` + `job summary` 表格;`::warning`/`::error` 带数值注解;未发现 `\|\| true` 吞结论的写法(两处"后备重试"都是显式分支) |

### 5.5 tools 脚本

| 脚本 | 幂等 / 错误处理 | 备注 |
| --- | --- | --- |
| `build.ps1` | `ErrorActionPreference=Stop`;CS2012 锁重试 3 次(shutdown + 10 s);`-Smoke` 阈值本轮统一为 10 KB | `-Test` 走 `dotnet test TipCL.Linux.sln`(单宿主,CI 注释里的 OOM 边界只在大机器上安全;21 GB/24 核本机无问题) |
| `build.sh` | `set -euo pipefail` | 与 build.ps1 行为对称 |
| `package.ps1` | 幂等:先删 pdb/多余文件再产出;`-SkipLinux/-SkipWin` 可拆;失败即 `exit $LASTEXITCODE` | 冒烟阈值本轮统一 |
| `package.sh` | `set -euo pipefail`;`--appimage-only` 缺产物直接报错;canonical 改名**只认非 canonical 的那一个**(注释里记着 R21i 踩过的"新包留在原地、canonical 还是上一版") | 无 `\|\| true` |
| `deb.sh` | `set -euo pipefail`;缺 `dpkg-deb` 报可执行的下一步;`dpkg-deb -c \| head` 先落盘再 head(避开 SIGPIPE + pipefail 假失败) | 版本从 `CoreInfo.cs` 抽,支持 `--version` 覆盖 |
| `verify-linux.sh` | `set -uo pipefail`(刻意不 `-e`,要收齐 8 帧结论);逐帧清残影本轮已修 | 默认产物目录本轮已修(D-01) |
| `verify-smoke.ps1` | `ErrorActionPreference=Stop`;每帧独立 `TIPCL_HOME`+`TEMP`,并断言 `TIPCL_HOME` 必须落在 `-OutDir` 内(防 D-43 那类"写进真实用户目录");`$home` 命名刻意避开只读 `$HOME` | `Ok` 判据本轮加严到 `> 10 KB` |
| `i18n-audit.py` | 只读扫描;`--selfcheck` 结构校验(338 文件 / 23091 字面量)本轮实跑 **PASS**;所有写盘 `newline="\n"` + `encoding="utf-8"`(Windows 上不会写出 CRLF);stdout 显式 `reconfigure(encoding="utf-8")` | 退出码:`--selfcheck` 有 failure 才 1,正常审计只打印(不阻断)——符合"报告工具"定位 |
| `windos/tools/pack-core-resources.ps1` | `-Check` 模式只校验不写盘、过期 `exit 1`;排除自身产物(`.gz`)避免"第二次运行把 .gz 再压一遍";`.NET GZipStream` 写 MTIME=0 ⇒ 幂等 | 注释的 "level 9" 与 `CompressionLevel.Optimal` 不符(D-12) |

### 5.6 CRLF / LF 与路径

- `tools/**`、`windos/tools/**`、`**/*.py` 全部实测 **CRLF=0**、无 BOM;`*.sh` 都是 `#!/usr/bin/env bash` 或 `#!/bin/bash`。
- 中文/空格路径:本轮所有命令都在 `D:\项目\TipCL\...`(含中文)下跑通,含 `dotnet test`、`dotnet publish`、
  `Start-Process` 调用 exe;`.sh` 里所有路径变量都加了双引号(`"$ROOT"`/`"$BIN"`),未发现裸 `$var` 拼路径。

---

## 6. 跨域问题(源码 bug,交给 A/B/C)

**本轮没有确认的源码 bug。** 一条曾经高度可疑、已实测排除:

| 疑点 | 结论 |
| --- | --- |
| `src/TipCL.Core/PyJson.cs:107-114` `Match()`:`string.CompareOrdinal(s, i, w, 0, w.Length)` 看起来在被截断的关键字(`"nul"`/`"tru"`/`"fals"`)上会抛 `ArgumentOutOfRangeException`,而 `LoadsOr` 只 `catch (PyException)` ⇒ 配置文件被截断会直接崩 | **实测不成立**:`"nul"`/`"nu"`/`"tru"`/`"fals"`/`"[nul]"`/`"{\"a\":tru}"` 六种输入全部抛 `JSONDecodeError`(`PyException` 子类),`LoadsOr` 全部回落默认值。该守卫逻辑已固化为 `PyJsonMalformedTests`(25 条) |

### 待决策(非 bug,交付物口径)

| 项 | 事实 | 建议 |
| --- | --- | --- |
| `TipCL.exe` 单文件交付 | net48 Release 构建同时产出 `TipCL.exe`、`TipCL.exe.config`(两条 bindingRedirect)、`System.Text.Encoding.CodePages.dll`(被 Costura 刻意排除不嵌);`release.yml` 只上传 exe | **已实测:exe 单独可用**(见 D-11 证据),不需要随包。建议把这条结论写进 `DECISIONS.md`,否则每次发版都要重新论证一遍 |

### exe-only 冒烟证据(2026-11-20 本机)

```text
目录里只有 TipCL.exe(5,587,456 B)—— 没有 .config、没有 CodePages.dll、没有 pdb
exit=0  elapsed_ms=12731  state=finished  bridge=已连上  i18n_missing=[]
pages : launch/library/instance/account/ai/multiplayer/settings/tasks/download/plugins 全 ok
clicks: 15/15 ok        dialogs: 8/8(6 个 in-app + 1 system-file-dialog + 欢迎页)
issues: []
截图  : TIPCL_HOME/_wpf_smoke/run-20261120-004826/*.png(10 张,40~96 KB)
```

> 顺带一条口径提醒:net48 版 `--smoke` **不写 `smoke.png` 到工作目录**,而是写进
> `<TIPCL_HOME>/_wpf_smoke/run-<时间戳>/`(10 页 + 对话框截图 + `report.json`)。
> `ci.yml` 的 `package-win` 冒烟步骤检查的是**工作目录下的 `smoke.png`**,那条只对 Avalonia 的
> `win-x64` 产物成立(它确实写 `smoke.png`),两者不要混用判据。

---

## 7. 实测证据(可复现)

```powershell
# 1) Linux 侧两批 + Windows 侧(基线 @ b86afe3)
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --no-build --filter "FullyQualifiedName~TipCL.Tests.App"      # 355 / 0
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --no-build --filter "FullyQualifiedName!~TipCL.Tests.App"     # 426(425 + 1 跳过)
dotnet test windos/tests/TipCL.Tests/TipCL.Tests.csproj --no-build                                              # 547 / 0
# 2) 收工( rebase 到 main @ c3ff092 + 本线 123 条新测试 )
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --no-build --filter "FullyQualifiedName~TipCL.Tests.App"      # 358 / 0
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --no-build --filter "FullyQualifiedName!~TipCL.Tests.App"     # 549(548 + 1 跳过)
dotnet test windos/tests/TipCL.Tests/TipCL.Tests.csproj -c Release --no-restore                                 # 547 / 0
# 3) 冒烟阈值依据(App 无头渲染,正常帧大小)
TIPCL_HOME=<隔离目录> dotnet src/TipCL.App/bin/Debug/net8.0/TipCL.App.dll --smoke   # exit 0,smoke.png 569,739 B,日志 0 WARN
# 4) i18n 工具自检
python tools/i18n-audit.py --selfcheck                                              # PASS: 338 files, 23091 literals
# 5) exe-only 交付验证(net48)
Copy-Item windos/src/TipCL/bin/Release/net48/TipCL.exe <空目录>\; & <空目录>\TipCL.exe --smoke   # exit 0,10 页 ok,issues []
```

---

# 第二轮(用户诉求:UI 自定义"丝滑一点" + "坏的按钮,背后根本没接功能")

本轮目标只有一件事:**把"每页能不能开、按钮能不能点"从人工印象变成可重跑的命令与判据**。
不碰 `src/**`、`windos/src/**`(发现的源码问题写在 R6 跨域表,交 A/B/C)。

## R1 逐页无头冒烟矩阵(直接命中"某页点进去就炸")

新增两个同源实现(Windows 本机 / Linux CI):

| 文件 | 用途 |
| --- | --- |
| `tools/smoke-matrix.ps1` | Windows 本机:对 10 个页面 id 各起一次独立进程 + 隔离 `TIPCL_HOME` |
| `tools/smoke-matrix.sh` | Linux/CI 等价实现(`ci.yml` 的 `test (ubuntu)` 已接入) |

判据(四条,全过才 exit 0;任意一页不过就红并打印该页现场):
`进程 exit 0` + `smoke.png > 10 KB` + `日志 0 个 [WARN]` + `日志 0 个 [ERROR]`。
页面清单来源:`src/TipCL.App/Shell/MainWindow.axaml.cs` 的 `GetPage(id)` switch
(launch / download / library / plugins / settings / instance=版本管理 / account / multiplayer / tasks / ai)。

### 实测结果(Linux 侧,Release 构建,10 页全绿)

| 页面 | 结论 | exit | smoke.png (B) | [WARN] | [ERROR] | 耗时 (ms) |
| --- | --- | --- | --- | --- | --- | --- |
| launch | OK | 0 | 569,905 | 0 | 0 | 1285 |
| download | OK | 0 | 23,643 | 0 | 0 | 1123 |
| library | OK | 0 | 44,675 | 0 | 0 | 1170 |
| plugins | OK | 0 | 34,969 | 0 | 0 | 1142 |
| settings | OK | 0 | 38,071 | 0 | 0 | 1407 |
| instance(版本管理) | OK | 0 | 41,685 | 0 | 0 | 1142 |
| account | OK | 0 | 34,345 | 0 | 0 | 1098 |
| multiplayer | OK | 0 | 46,005 | 0 | 0 | 1116 |
| tasks | OK | 0 | 23,954 | 0 | 0 | 1103 |
| ai | OK | 0 | 37,542 | 0 | 0 | 1139 |

- **没有任何一页炸**:10/10 通过,总耗时约 12 s(每页 ~1.1–1.4 s)。
- `bash tools/smoke-matrix.sh --pages launch,settings,ai` 也单跑过一遍(570,013 / 38,071 / 37,542 B)
  ⇒ 两份实现同口径(页面清单与 10 KB 阈值由 `RepoConsistencyTests.SmokeMatrixScriptsAgreeOnThePageList` 守着)。
- **CI 接线**:`ci.yml` 的 `test` job 在 Linux 上跑矩阵,结果写 `$GITHUB_STEP_SUMMARY`,
  每页截图 + `matrix.csv` 作为 `tipcl-smoke-matrix` artifact 上传(失败也能拿到现场)。
- 边界(如实说明):`--smoke` 走 Avalonia headless + Skia,不需要 X server;但它**只证明"这一页能构造、能渲染一帧、无异常日志"**,
  不证明交互正确(交互靠 R3 的点击遍历与 R2 的 WPF 侧矩阵)。

## R2 WPF 侧冒烟矩阵固化(真窗口 / 真点击 / 真对话框)

net48 版 `--smoke` 本来就比 Linux 侧强:真开窗口、遍历 10 页、点 15 个按钮、开 8 个对话框,
把结果写进 `<TIPCL_HOME>/_wpf_smoke/run-<ts>/report.json` + 每页截图。问题一直是**只有人工记得跑**。
本轮固化成两件东西:

| 新增 | 作用 |
| --- | --- |
| `windos/tools/smoke-matrix.ps1` | 隔离 `TIPCL_HOME` 跑一次 `TipCL.exe --smoke`,读 `report.json`,按 **9 条判据**判红绿,打印每页/每次点击表格,`-Markdown` 落盘 |
| `windos/tests/TipCL.Tests/SmokeReportCriteriaTests.cs` | 把 9 条判据做成**可测的纯函数**,用合成 report 覆盖每一类失败;设 `TIPCL_WPF_SMOKE_REPORT=<report.json>` 时对**真报告**再跑一遍 |

9 条判据:`C1 退出码 0` / `C2 state=finished` / `C3 issues 为空` / `C4 ≥10 页且每页 ok` /
`C5 点击数 ≥ required_clicks 且每次 ok` / `C6 对话框数 ≥ required_dialogs` / `C7 每页截图存在且 >10 KB` /
`C8 i18n_missing 为空` / `C9 不是 watchdog 超时`。

### 实测(本机 WPF,net48 Release)

```
state=finished exit=0 pages=10 clicks=15/>=9 dialogs=8/>=5 elapsed=10962ms bridge=已连上
launch/library/instance/account/ai/multiplayer/settings/tasks/download/plugins  全 ok
截图 34,045 – 57,616 B(10 张);每次点击 ok;issues: []
=> WPF 冒烟矩阵全部通过(9 条判据),总耗时 12.9 s
```

真报告门槛也验过:`TIPCL_WPF_SMOKE_REPORT=<run-.../report.json> dotnet test --filter RealReportFromSmokeMatrixScriptPasses` → 绿。
判据测试 14 条(合成失败用例 13 条 + 脚本 BOM/判据标签一致性 1 条)**14/14 通过**。

## R3 按钮点击遍历(Linux 侧,"点了没反应"的可自动化部分)

`tests/TipCL.Tests/App/ClickSweepTests.cs`:10 页逐一枚举**可见且可用**的 `Button`/`MenuItem`
(每页最多 80 个),用 headless 指针输入点击(回落:直接 raise `Click`),比较点击前后的
**指纹** = 页 id | 顶层窗口数 | 任务条数 | 可视节点数 | 全部文本 + 控件状态
(`ToggleButton.IsChecked` / `TextBox.Text` / `ComboBox.SelectedIndex` / `Slider` / `ProgressBar`)。

### 实测(一次遍历 208 个可交互控件)

| 结果 | 数量 | 说明 |
| --- | --- | --- |
| 有可见变化 | 50 | 换页 / 弹层 / 文本变化 / 开关状态变化 |
| 点了没反应 | 94 | 见下方分类,**只统计不判定** |
| 因副作用跳过 | 63 | 启动游戏 / 安装 / 下载 / 删除 / 退出 / 打开文件夹 / 选择文件 / 导入导出 + 窗口标题栏按钮 |
| 抛异常 | 1(已知跨域) | 插件页「⋯」按钮,见 R6 |

94 个"没反应"的构成(**别把它当成 94 个坏按钮**):

| 类别 | 数量 | 为什么不能算"坏" |
| --- | --- | --- |
| 勾选/开关/滚动条类(`ToggleButton`/`CheckBox`/`RepeatButton`) | 41 | headless 注入的指针事件**没有触发** `ToggleButton.OnClick` 的状态切换 ⇒ 机制未覆盖,不是产品无反应 |
| 页标题图标 `Button[titleIcon]` | 10 | 设计上就不是可点入口 |
| 普通 `Button`(有文案) | 43 | **大部分在真机上是有反应的**:它们的效果是弹一个 `Dlg` 对话框,而 headless 会话里 `Dlg.Resolve()` 拿不到 owner(`TestWriteGuard.cs:14-16` 已实测记录:`Dlg.Owner ?? desktop.MainWindow` 都是 null ⇒ 直接返回 false)⇒ 弹窗根本没出现,于是被判成"无变化" |

> 结论(交 lead / aud-app 判断):**Linux 侧 headless 点击遍历无法区分"死按钮"和"只会弹对话框的按钮"** ——
> 它拿不到对话框宿主。真正能判死按钮的是 R2 的 WPF 侧矩阵(真窗口、真 `Dlg`、15 次点击 + 8 个对话框,
> 每次点击都断言有反应)。所以 headless 遍历本轮只作**"点击不抛异常"的回归**,清单仅供参考。
> 43 个普通 Button 里值得人工确认的候选(去掉明显"只弹窗"的):
> `download | 正式版 / 快照 / 远古`、`settings | 检查更新 / 测试 AI 连接 / 连接 / 断开 / USB 自动连接 / 同步面板 / 扫描并清理 / 查看推荐`、
> `multiplayer | 允许访问 / 打开设置 / 关闭内核 / Terracotta 项目主页`、`tasks | 清除已完成`、`ai | 记忆 / 权限 / 清空 / 发送`。
> 完整清单:`TIPCL_CLICKSWEEP_OUT=<path.tsv> dotnet test --filter ClickSweepTests`。

## R4 i18n 接线覆盖率守卫(英文界面显示中文)

### 工具增强

`tools/i18n-audit.py` 新增 `--baseline <path>`:输出**未接线的用户可见 CJK 字面量**清单 ——
总览 + 按文件聚合(未接线条数、其中已有 en 译文的数量)+ 明细(`文件:行 文案`,带 `[已有译文]` 标记)+
`I18N-GUARD` 机读块(unwrapped_total / per_file)。
生成:`python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md`(本轮已生成入库)。

### 基线数字(Linux/Avalonia 侧,2026-11-20)

| 指标 | 数值 |
| --- | --- |
| CJK 字面量(分母) | **5,217**(rebase 到最新 main 后;`2094a3e` 上是 5,198) |
| 已接线(`I18nCore.Tr(...)`) | **299(5.73%)** |
| 未接线(切语言改不动) | **4,918** |
| 其中词表里**已有 en 译文**(只差接线) | **2,090 条**(去重后 1,140+ 个不同文案) |
| 涉及 .cs 文件 | 174 |

> 与一致性线路实测的 5,194 / 299 / 1,140 对齐:分母差 4 条是词表增补后的漂移;
> 1,140 是**去重后**的文案数,本基线另外给出**出现次数** 2,086(接线工作量按出现次数算更实在)。

### 未接线最多的文件(接线优先级)

| 未接线 | 其中已有 en 译文 | 文件 |
| --- | --- | --- |
| 330 | 8 | `src/TipCL.Core/Launcher/Catalog.cs` |
| 193 | 183 | `src/TipCL.App/Pages/Settings/SettingsPage.cs` |
| 187 | 126 | `src/TipCL.App/Pages/Settings/SettingsPage.Layout.cs` |
| 153 | 15 | `src/TipCL.Core/Launcher/Crash.cs` |
| 153 | 9 | `src/TipCL.Core/Launcher/Modpack.cs` |
| 144 | 22 | `src/TipCL.App/Pages/Settings/SettingsSubs.cs` |
| 143 | 31 | `src/TipCL.App/Pages/Version/VersionManagePage.cs` |
| 141 | 18 | `src/TipCL.App/Pages/Download/CatalogPage.cs` |
| 122 | 6 | `src/TipCL.Core/Launcher/Installer.cs` |
| 110 | 12 | `src/TipCL.Core/Launcher/Ai/AiSelfTest.cs` |

> **给 aud-app 的分派建议**:先做「已有 en 译文」比例高的文件 —— `SettingsPage.cs`(183/193)、
> `SettingsPage.Layout.cs`(126/187)、`SettingsSubs.cs`(22/144):接线即生效,不用补词表,单位工作量收益最高。
> `Catalog.cs`(330 条只有 8 条有译文)属于"要先补词表"的另一类工作。

### 守卫测试

`tests/TipCL.Tests/Repo/I18nBaselineTests.cs`(4 条):
1. 基线文件存在且 `I18N-GUARD` 块结构完整(unwrapped_total / per_file / cjk_literals);
2. **重新跑一遍审计**,断言 `unwrapped` **不增加**、`wrapped` **不减少**;
3. 基线必须带 `unwrapped_with_en_key`(分派接线工作的分母);
4. `per_file` 表非空且条目都是 `.cs`。

守卫**真的会红**:把基线人为改小到 4000 后跑测试 →
`未接线的 CJK 字面量从基线的 4000 涨到了 4899`,失败;还原后复绿(实测)。
依赖 python:`python3`/`python` 都找不到时安静返回;想强制要求可设 `I18N_GUARD_REQUIRE_PYTHON=1`。

#### 守卫上线当天就抓到一次真实回退(+19,已归因)

本轮的基线先在 `2094a3e` 上取(4,899),随后 main 又进了 A/B/C 的 15 个提交;
rebase 到新 main 再跑,守卫立刻红:

```
未接线的 CJK 字面量从基线的 4899 涨到了 4918
```

逐文件归因(全部来自二/三轮新增功能,**新增文案没有接线进 `Tr(...)`**):

| 文件 | 未接线 | 增量 | 谁引入的(按提交归属) |
| --- | --- | --- | --- |
| `src/TipCL.Core/Launcher/Mods.cs` | 76 → 80 | **+4** | audit/A 第二轮(Mods) |
| `src/TipCL.App/Pages/Settings/SettingsSubs.cs` | 144 → 154 | **+10** | audit/B 第二轮(服务器导入导出 / 登录入口等接线) |
| `src/TipCL.App/Pages/Settings/SettingsPage.SaveLoad.cs` | 41 → 45 | **+4** | audit/B 第二轮(主题包保存/加载) |
| `src/TipCL.Core/Launcher/Cleaner.cs` | 0 → 1 | **+1** | audit/A 第二轮(Cleaner) |
| 合计 | 4,899 → **4,918** | **+19** | — |

`wrapped` 没变(299 → 299):**新增的中文一条都没走词表**,所以"英文界面显示中文"的比例又退了一点
(覆盖率 5.75% → 5.73%)。基线已刷新到 4,918(记录当前实情),这 19 条列入交 aud-app 的接线清单;
**从现在起再有新增就是守卫挡**。刷新命令:
`python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md`。

> 这条正是"用户说英文界面还是中文"的机制性根因:**光做功能不做接线,数字就往上走,而且没人会注意到** —— 现在有人了。

## R5 本轮问题表

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
| --- | --- | --- | --- | --- | --- | --- |
| D2-01 | **P1** | `tools/build.ps1` / `tools/package.ps1` | 含中文 + **UTF-8 无 BOM** ⇒ 本机默认的 Windows PowerShell 5.1 按 ANSI 解码,乱码字节吃掉引号 ⇒ **整个文件解析失败**(实测 build.ps1 8 处 parse error、package.ps1 19 处)。「一键构建/一键打包」在这台机器的默认 shell 里根本跑不起来 | 给 `build.ps1` / `package.ps1` / 新 `smoke-matrix.ps1` 加 UTF-8 BOM(仅 +3 字节,内容字节不变);新增守卫 `RepoConsistencyTests.PowerShellScriptsWithNonAsciiTextCarryAUtf8Bom` | 修复后全仓 16 个 `.ps1` 在 PS 5.1 下 **0 parse error** | ✅ 已修 |
| D2-02 | **P1** | `tools/smoke-matrix.ps1`(新增) | 第一版用 `$home` 当变量名 —— PowerShell 里 `$HOME` 是**只读自动变量**(不区分大小写),脚本一跑就 `Cannot overwrite variable HOME`(与 `verify-smoke.ps1` 注释里记的 D-43 同一个坑) | 改名 `$homeDir` | 脚本实跑 10 页全绿 | ✅ 已修 |
| D2-03 | **P2** | `tools/smoke-matrix.ps1`(新增) | 第一版用 `Start-Process -PassThru` 读 `ExitCode`,PS 5.1 下拿到 `$null` ⇒ 10 页全部误判 FAIL(exit= 空) | 改用 `System.Diagnostics.Process` + `WaitForExit(ms)` + `ExitCode` | 修后 10/10 OK | ✅ 已修 |
| D2-04 | **P2** | `.github/workflows/ci.yml` | 只有"默认帧"一条冒烟,「某一页点进去就白屏/报错」在 CI 里完全不可见 | `test` job 接入 `tools/smoke-matrix.sh`(10 页 × 4 判据)+ 结果进 job summary + 截图 artifact | 本机同口径脚本 10/10 | ✅ 已修 |
| D2-05 | **P2** | `tools/i18n-audit.py` | 只能报聚合数字,没有"按文件、带是否已有译文"的清单,也没有基线可比 | 新增 `--baseline`,落 `progress/I18N-BASELINE.md`(含机读守卫块) | `--selfcheck` PASS;基线生成成功 | ✅ 已修 |
| D2-06 | **P2** | 测试覆盖 | "未接线中文继续增加"没有任何守卫 | `I18nBaselineTests`(4 条)+ 基线快照 | 人为改小基线 → 红;还原 → 绿 | ✅ 已修 |
| D2-07 | **P2** | 测试覆盖 | WPF 冒烟的 9 条判据只活在人的记忆里 | `windos/tools/smoke-matrix.ps1` + `SmokeReportCriteriaTests`(14 条) | WPF 实跑 10 页/15 点击/8 对话框通过;真报告门槛通过 | ✅ 已修 |

## R6 跨域问题(源码,**只报告不改**)

| 文件:行 | 现象 | 触发条件 | 建议 |
| --- | --- | --- | --- |
| `src/TipCL.App/Shell/TitleBar.axaml.cs:51` 与 `:53` | 点「最小化 / 关闭」抛 `NullReferenceException` | `(TopLevel.GetTopLevel(this) as Window)!` 强制解包一个**可能为 null** 的值;headless 会话里 `GetTopLevel` 返回 null ⇒ 100% NRE(9 页 × 2 个按钮) | 改空安全:`if (TopLevel.GetTopLevel(this) is Window w) w.WindowState = ...;` / `w.Close();` |
| `src/TipCL.App/Pages/Plugins/PluginsPage.cs:88-92` | 点插件页「⋯」抛 `ArgumentException: Cannot show ContentMenu on a different control to the one it is attached to.` | 先 `more.ContextMenu = menu`(框架已挂载),再在 Click 里 `menu.PlacementTarget = more; menu.Open(menu.PlacementTarget)`;headless 稳定复现 | 二选一:去掉 `more.ContextMenu = menu` 只走显式 `Open(more)`,或只依赖框架自动弹 |
| `src/TipCL.App/Common/Dlg.cs`(可测性) | headless 会话里 `Dlg.Resolve()` 恒为 null ⇒ 所有"只会弹对话框"的按钮在无头环境**永远没反应** | 无头环境固有限制,不是产品缺陷 | 若要自动化判"死按钮",给 `Dlg` 加可注入 owner(headless 下用虚拟 owner),否则这类按钮只能靠 WPF 真窗口矩阵覆盖 |

## R7 本轮新增/修改清单

| 类型 | 路径 |
| --- | --- |
| 新增脚本 | `tools/smoke-matrix.ps1`、`tools/smoke-matrix.sh`、`windos/tools/smoke-matrix.ps1` |
| 新增测试 | `tests/TipCL.Tests/App/ClickSweepTests.cs`、`tests/TipCL.Tests/Repo/I18nBaselineTests.cs`、`windos/tests/TipCL.Tests/SmokeReportCriteriaTests.cs` |
| 守卫增强 | `RepoConsistencyTests`:+`SmokeMatrixScriptsAgreeOnThePageList`、+`PowerShellScriptsWithNonAsciiTextCarryAUtf8Bom` |
| 新增基线 | `progress/I18N-BASELINE.md`(由 `--baseline` 生成,勿手改) |
| 修改 | `tools/i18n-audit.py`(`--baseline`)、`tools/build.ps1` + `tools/package.ps1`(BOM)、`.github/workflows/ci.yml`(矩阵) |

### 第二轮测试计数(全绿)

| 批次 | 第一轮收工 | 第二轮收工 | 增量 |
| --- | --- | --- | --- |
| Linux 批次 1(App) | 358 | **359** | +1(`ClickSweepTests`) |
| Linux 批次 2(其余) | 549(548 + 1 跳过) | **555**(554 + 1 跳过) | +6(`I18nBaselineTests` 4 + `RepoConsistencyTests` 2) |
| Windows 侧 | 547 | **561** | +14(`SmokeReportCriteriaTests`) |
| 合计 | 907 / 0 失败 | **914**(913 通过 + 1 跳过)/ **0 失败** | +21 |

### 第二轮复跑命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-matrix.ps1            # 10 页 × 4 判据
powershell -NoProfile -ExecutionPolicy Bypass -File windos\tools\smoke-matrix.ps1     # WPF 9 条判据
python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md                        # 刷新 i18n 基线
$env:TIPCL_CLICKSWEEP_OUT="<path>\sweep.tsv"; dotnet test tests\TipCL.Tests\TipCL.Tests.csproj --filter "FullyQualifiedName~ClickSweepTests"
$env:TIPCL_WPF_SMOKE_REPORT="<run>\report.json"; dotnet test windos\tests\TipCL.Tests\TipCL.Tests.csproj --filter "FullyQualifiedName~SmokeReportCriteria"
```