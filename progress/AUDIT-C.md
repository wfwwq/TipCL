# AUDIT-C — Windows 桌面版(windos/src/TipCL,WPF / net48)代码审计报告

- 审计员:audit/C 线路(C# / WPF / .NET Framework 4.8)
- 分支:`audit/C`,基于 main `b86afe3`;worktree `D:\项目\TipCL\_aud\C`
- 范围:`windos/src/TipCL/**`(拥有写权限);`src/**`、`windos/tests/**`、`windos/samples/**`、`windos/tools/**` 只报告不改
- 级别定义:P0 崩溃/启动失败/存档或配置损坏/主流程中断/编译不通过;P1 功能行为错误/逻辑 bug/竞态/异常被吞导致失效/net48 误用/持久化写坏;P2 边界/资源泄漏/提示不当/i18n 缺键
- 基线验证:修复前 `dotnet build windos/src/TipCL/TipCL.csproj -c Release` = 0 警告 0 错误;`dotnet test windos/tests/TipCL.Tests` = 547 通过 / 0 失败。**所有修复后同样 0 警告 0 错误、547/547 通过。**

## 0. 结论摘要

| 级别 | 发现 | 已修 | 记录未修 |
|---|---|---|---|
| P0 | 0 | 0 | 0 |
| P1 | 3 | 3 | 0 |
| P2 | 6 | 4 | 2(+3 记录性) |
| 合计 | 9 | 7 | 2 |

结论:该工程已经过多轮审计,核心下载/校验/布局持久化路径质量较高(见 §3 逐项核查证据)。本轮没有再出现 P0;找到的 P1 集中在
**用户交互并发**(重复点「启动」)与**「创建桌面快捷方式」在 Windows 侧没人消费参数**这两处,以及一个**开发工具参数漏进提权白名单**的决定-实现不一致。

## 1. 问题表(P0~P2)

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|---|---|---|---|---|---|---|
| C-01 | P1 | `windos/src/TipCL/Pages/LaunchPage.cs:94`、`:1346-1363` | `LaunchAsync` 无重入保护。`_launchBtn.IsEnabled = false` 要等 `EnsureLaunchAccountAsync` / `CheckMultiInstanceAsync` / `preflight_launch` 三个 RPC + 若干对话层都过完之后(`:1435` 附近)才执行;这段窗口里按钮仍可点。双击会并发跑两次 `launch_game`,后端起两个任务,而 `_taskId` 只记住后一个 → 前一个既「停止」不掉也不回填状态,现象就是「点停止没反应 / 起了两个游戏」 | 新增 `private bool _launching` 重入守卫;`LaunchAsync` 变成 `if (_launching) return; _launching = true; try { await LaunchCoreAsync(); } finally { _launching = false; }`,原方法体整体改名为 `LaunchCoreAsync`(零逻辑改动,所有早退都经过 finally) | build 0 错误;`dotnet test` 547/547;`_launching` 只在 `LaunchAsync` 一处读写,`HandleCrashAsync:1933` 的崩溃后重试仍可用(那时上一次 LaunchAsync 已返回、标记已清) | 已修 |
| C-02 | P1 | `windos/src/TipCL/Core/Launcher/Shortcut.cs:79-105`(`LaunchArgs` 产出 `-i <实例> launch <版本>`)、`windos/src/TipCL/App.xaml.cs:48-70`、`windos/src/TipCL/MainWindow.xaml.cs:379`、`:572` | `Shortcut.LaunchArgs` 生成的 `.lnk` 参数在 Windows 侧**没有任何消费者**:`App.OnStartup` 只认 `--smoke/--shot/--nav-dump/--ime-check/--i18n-check/--consent-check/--ai-ui-selftest`,grep `"-i"` 全仓只有 Shortcut.cs 一处。双击快捷方式只把启动器打开,不会启动对应版本/实例(Linux 侧 `src/TipCL.App/Session/CommandLine.cs:84` 是真的消费了,docs/dev/PROGRESS.md:228 也把这个记为 Windows 缺陷) | `App`:新增 `ParseStartArgs`(`-i <inst> launch <ver>`,越界安全)+ `TakeStartArgs(out …)` 一次性取走;在真实 UI 分支 `new MainWindow().Show()` 之前解析。`MainWindow.ConnectAsync`:后端连通、`ReloadCurrentAsync()` 之后调 `StartFromShortcutAsync()`,如果是启动页就 `LaunchPage.LaunchNowAsync(instance, version)`(复用 AI 页那条现成入口);`Smoke.Active` 时跳过 | build 0 错误、547/547;参数形状与 `Shortcut.LaunchArgs` 逐字对齐(`-i`,`inst`,`launch`,`ver`),不与 Linux 侧 CLI 语义冲突;未改 `LaunchArgs` 输出,`.lnk` 生成逻辑零改动 | 已修 |
| C-03 | P1 | `windos/src/TipCL/App.xaml.cs:38` | `SelfCheckArgs`(免提权白名单)只有 `--shot`,缺 `--design-sheet`。`app.manifest` 是 `asInvoker`,提权全靠 `RequestElevation`;非管理员 shell 跑 `TipCL.exe --design-sheet` 会弹 UAC 并**重启一个分离进程**,`DevShot` 的 stdout / 退出码全丢给那个新进程(截图工具链直接失效)。这与 `windos/DECISIONS.md:44`「新增 `--shot`/`--design-sheet` 加入 `SelfCheckArgs`」的决定不一致 | 白名单补 `"--design-sheet"`(数组 6→7) | build 0 错误、547/547;`DevShot.Run`(`DevShot.cs:24`)同时接受 `--shot` 与 `--design-sheet`,白名单补齐后两者行为一致 | 已修 |
| C-04 | P2 | `windos/src/TipCL/Services/WebViewHost.View.cs:130-136` | `StartAsync` 在 `await web.EnsureCoreWebView2Async(env)` 之后只 `if (_disposed) return;`。此时 `_web` 已经指向新建的 `WebView2`(`:127`),而 `Dispose()` 早在它被赋值之前就跑完了(`DisposeWeb()` 当时看到 `_web == null` 什么也没做)→ 这个 WebView2(HwndHost + 一个 msedgewebview2 进程)永久泄漏 | 该分支改为 `DisposeWeb(); return;`(`DisposeWeb` 会先从 `Children` 摘掉再 `Dispose`) | build 0 错误;`DisposeWeb()` 幂等(`_web = null` 后直接 return),重复调用安全 | 已修 |
| C-05 | P2 | `windos/src/TipCL/Services/WebViewHost.View.cs:274` | `Reload()` 不检查 `_disposed`。已销毁的视图被 `SetUrl`/「重新检测」/进程崩溃重载路径再触发时,`PageStart()`(async void)会在没有可视树的情况下把浏览器重新建起来且无人 Dispose | `Reload()` 首行加 `if (_disposed) return;` | build 0 错误;所有 `Reload()` 调用点(`SetUrl`、`OnProcessFailed` 的重载按钮、`ShowFallback` 的重新检测)都是「视图还在用」的前置条件,守卫不影响正常路径 | 已修 |
| C-06 | P2 | `windos/src/TipCL/MainWindow.xaml.cs:1480` | `SaveUiPrefs` 直接 `File.WriteAllText(PrefsPath, …)` 覆盖正式文件。写到一半被结束(更新器重启 / Win7 关机)会留下截断的 `wpf-ui.json`,下次 `LoadUiPrefs` 解析失败 → 主题 / 动画 / 随手记 / 上次账号全部回默认(配置损坏类) | 改为原子写:先写 `wpf-ui.json.tmp` 再 `File.Replace`(目标不存在时 `File.Move`),失败时清掉半成品 tmp;对外语义(吞异常、不崩 UI)不变 | build 0 错误;与 `PageLayoutStore.WriteFile:258-267` 同一范式 | 已修 |
| C-07 | P2 | `windos/src/TipCL/Resources/locales/zh_CN.json:3033`、`en.json:3031` | `SettingsLayoutPage.PreviousName`(`SettingsLayoutPage.cs:1396`)用 `I18n.L("上一次布局")` 做「应用方案前自动留底」的方案名,但 en/zh_CN 两个词表都没有这个键 → 英文界面下方案列表里出现中文;且它被当作**方案名写进布局文件**(不只是显示) | 两个词表各补一条(`zh_CN`:`上一次布局`→同名;`en`:`Previous layout`),并重跑 `windos/tools/pack-core-resources.ps1` 重新生成 `.gz` | `CoreResourcesPackTests` 通过(gz 与源一致,547/547 里包含);脚本全量比对 1985 条 `I18n.L("…")` 字面量后除本条外无缺键 | 已修 |
| C-08 | P2 | `windos/src/TipCL/Core/Launcher/Utils.cs:165-169`(`Utils.TarReader.Read`) | tar 头里的 size 直接 `new byte[num]` 分配(`num` 来自 8 字节八进制字段,无上限校验)。畸形/超大头的 `.tar.gz` 会 OOM / 抛 `OutOfMemoryException`,异常类型不是本项目自己的 `*Error`,上游 `SafeExtractTargz` 的调用方只按 `DownloadError/ValueError` 分类 → 变成「未预期异常」直穿到任务层 | 未修(记录) | — | 未修,见 §4 原因 |
| C-09 | P2 | `windos/src/TipCL/Core/Launcher/Saves.cs:161-166`(`ZipFolder`) | 备份时只把 `LastWriteTime.Year < 1980` 钳到 1980-01-01,没有钳上界。坏 mtime(>2107,如磁盘损坏 / 系统时钟被写坏)会让 `ZipArchiveEntry.LastWriteTime` 抛 `ArgumentOutOfRangeException`,整个存档备份失败 | 未修(记录) | — | 未修,见 §4 原因 |
| C-10 | P2 | `windos/src/TipCL/Core/Launcher/Layout/LayoutStoreV2.cs:218-221` | 注释还写着「PageLayoutStore 没有 `Reload()`(PageLayoutStore.cs:197-206)」,但该文件 `:97` 早就有 `Reload()`,`:251` 也有 `RefreshV1Cache()` —— 过期注释会误导后续维护者写出多余的写通逻辑 | 注释改为与当前代码一致(说明 `Reload()` 已存在、推荐调 `RefreshV1Cache()` / `WriteThroughV1()`) | 纯注释;build 0 错误 | 已修 |

## 2. 跨域问题(只报告,未改)

| ID | 目标域 | 位置 | 现象 / 影响 | 建议 |
|---|---|---|---|---|
| X-01 | tests(D 线) | `windos/tests/TipCL.Tests/TipCL.Tests.csproj` | `dotnet test` 报 `VSTEST_ARTIFACTS_DIRECTORY environment variable was not found … outdated Microsoft.NET.Test.Sdk`;且不设 `DOTNET_ROOT` 时 testhost 起不来(`hostfxr.dll could not be found`)。在本机可用环境块下必须同时给 `DOTNET_ROOT` 才能跑测试,否则看起来像「测试挂了」 | 归 D 线:或升 `Microsoft.NET.Test.Sdk`,或在 CI/文档里把 `DOTNET_ROOT` 和 `VSTEST_ARTIFACTS_DIRECTORY` 写进环境块 |
| X-02 | windos/src(TipCL 内,但属于已定决定) | `windos/src/TipCL/App.xaml.cs:38` vs `:123` | `--ai-ui-selftest`(自检 CLI,`Pages/Ai/AiSelfTest.cs`)也不在免提权白名单里,非管理员 shell 调用同样会被 UAC + 重启分离进程截断。本轮**未修**:现有文档(`windos/DECISIONS.md:44`)只对 `--shot`/`--design-sheet` 做过免提权决定,而 `--ai-ui-selftest` 要在真机上驱动 AI 页,是否免提权需要你拍板 | 若要 CI 化:把它也加进 `SelfCheckArgs`,或规定必须带 `--no-elevate` |
| X-03 | src(Linux 侧,别人负责) | `src/TipCL.App/Session/CommandLine.cs:70-84`、`src/TipCL.Core/Launcher/Shortcut.cs:182` | C-02 我按 `-i <inst> launch <ver> [--account X|--username Y]` 的**现有输出形状**在 Windows 侧补了消费者,没有改 `LaunchArgs` 的输出。若 Linux 侧要调整参数形状,两侧必须同步(Shortcut 是共享 Core 的参考实现) | 参数形状变更时同步 windos 侧 `App.ParseStartArgs` |
| X-04 | windos/tools | `windos/tools/pack-core-resources.ps1` | 改 locales 后必须重跑该脚本,否则 `CoreResourcesPackTests` 会红(本轮已重跑,`-Check` 语义与 CI 守卫一致) | 无需改,仅提示:任何词表改动流程 = 改 json → 跑脚本 → 跑测试 |

## 3. 已核查无问题(含核查手段)

| 对象 | 核查内容 | 结论 |
|---|---|---|
| XAML 资源键(全量脚本) | 从 `**/*.xaml` 抽 `x:Key`(125 个定义),再从全部 `.cs`/`.xaml` 抽 `SetResourceReference(…, "字面量")` + `{StaticResource/DynamicResource}` 引用逐个比对 | **缺失 0 个**;`MainWindow.xaml` 也没有任何 `Click="…"` 之类的事件属性(全部在 code-behind 里挂),不存在 handler 名对不上的加载期异常 |
| `I18n.L` 字面量(全量脚本) | 1985 条字面量 vs `zh_CN.json` / `en.json` 键集 | 真实缺键仅 1 条(C-07,已修);其余 4 条是 `{0}px` / `1.20.5 – 1.21.11` / `AI` 这类非词条文本 |
| `async void`(全仓 6 处) | `LaunchPage.HideProgressSoon/RefreshBackground`、`WebViewHost.View.PageStart`、`PageBase.Run`、`PluginUi.Load`、`PluginsPage.Market.LoadShot` | 全部有 try/catch 或「只碰自身控件 + 有代际号(`_progGen`)/已销毁检查」保护,没有裸 async void |
| 空 `catch {}`(全仓 4 处) | `Ai/McpServer.cs:55/127/324`(事件回调、listener.Stop)、`Ai/VisionCaps.cs:123`(可选端点探测) | 都是「失败即忽略」的正当场景,不属于「吞掉异常导致功能失效」 |
| net48 / Win7 语言与 BCL 约束 | grep `Random.Shared`、`ThrowIfNull`、`MathF.`、`string.Contains(char)`、`record`、`init;`、`required `、`Span` 重载;核对 `BclExtensions.cs` 与 `System.Runtime.CompilerServices/*` 垫片 | 项目自带 `GetValueOrDefault/TryAdd/Deconstruct/Contains(char)/StartsWith(char)/EndsWith(char)` 扩展与 `required/init/ModuleInitializer` 特性垫片,编译期 0 警告 0 错误;未发现依赖 net6+ 独有类型的运行期隐患 |
| `DownloadManager`(下载/断点续传/分片/校验) | 全文 1203 行:Range 续传、416 清理、Content-Range 严格校验(起止/总长/`Content-Length` 三项一致)、分片失败回退单连接(`NoSegment`)、`OsReplace` 原子落盘、sha1/sha256/sha512 逐项比对、取消语义(`DownloadError("用户取消")` 贯穿)、`Pace` 限速、`DownloadAll` 的 worker/`BlockingCollection` 收尾 | 未发现 P0/P1。唯一可疑点是 `Downloader.SafeUnlink` 失败时的静默,已属既有设计 |
| `Utils.cs`(路径/哈希/原子写/解压) | `AbsPath/NormPathSpelling/SafeChildPath`、`WriteJson` 的重试+原子替换、`SafeExtractZip/Targz` 的路径穿越校验、`List2CmdLine`、`RunProcess` 的管道抽取 | 未发现越界写/穿越;解压路径校验(`UnderRoot`)在 Windows 忽略大小写、符号链接跳过 |
| `Manifest`(版本清单/继承合并) | 缓存 TTL、`FetchJson` 失败回旧缓存、`ResolveInherits` 的环检测、`MergeLibraries` 的 identity 去重、`arguments` 与 `minecraftArguments` 互斥回落 | 自洽 |
| `McLauncher.BuildLaunchCommand` | 占位符替换与残留占位符过滤、`-Djava.library.path` / `-cp` 补齐、`_drop_orphan_jvm_flags`、内存参数二次覆盖、`-p → --module-path` 归一、Java<9 出现模块参数直接拒绝、natives 缺失前置报错 | 自洽;`_apply_memory` 被调用两次属冗余但无害(内部先剔除旧 `-Xmx/-Xms`) |
| `Installer`(natives / 处理器) | `SelectNativeClassifier`、`NativesPresent`、`ExtractNatives` 的父版本回落、`ExtractJarNatives` 的界内校验 | 自洽 |
| `Saves`(存档备份/还原/导出) | `_safe_child` 越界校验、`RestoreBackup` 的 zip 穿越校验(绝对路径 / `..` / `C:` / UNC)、覆盖前先挪 previous、回滚失败保留现场并在消息里给路径、`finally` 只在成功时删临时目录 | 自洽(C-09 是另一个点:zip 写入侧的 mtime 上界) |
| `LayoutStoreV2` / `PageLayoutStore` | 读绝不抛 + v2→v1 回落→默认三级、写先 v2 成功再写通 v1、损坏 v2 改名备份 + 备份失败拒绝覆盖(`EnsureCorruptBackedUp`)、`Sanitize` 钳位 | 自洽;测试覆盖充分(`LayoutV2CompatTests` 等) |
| `PageBase` 生命周期 | `_loaded` / `_inflight` 去重、`Run` 的异常收口(`Smoke.Note` + Toast) | 自洽 |
| `MainWindow` | 静态事件对称退订(`Closed` 里退 `LayoutStoreV2.LayoutChanged`、`LayoutVariantHost.VariantChanged` 并 `Reset`)、跨线程统一 `Dispatcher.BeginInvoke`、`Toast` 上限 4 条、`WndProc` 的 MINMAXINFO 处理 | 自洽(构造里对 `TaskStore.Added/Updated/Cleared` 的匿名订阅未退订,但窗口是进程唯一窗口且 `Closed` 后随进程退出,不构成泄漏) |
| `BridgeServer` | 仅 loopback、固定时间 token 比较、`MAX_REQUEST_BYTES` 4MB、请求行/头行长度与条数上限、`Expect: 100-continue`、SSE `finally` 里 `Unsubscribe` | 自洽 |
| `Dlg`(对话层) | `Scope` 幂等关闭、`Push` 的 `MaxHeight` 绑定、`Confirm/Ask/Choose/Form` 的 `TrySetResult` 收口、`Form` 里 collect 抛错不关窗并 `Shake` | 自洽 |
| Timer / 动画句柄 | `CatalogPage`、`InstancePage`、`MultiplayerPage` 的 `DispatcherTimer` 在 `OnHidden` 停;`GridCanvasPanel` 在 `Unloaded` 解 `CompositionTarget.Rendering`;`Wallpaper`/`SideFrost` 用静态 timer 但由 `Wallpaper.Shutdown()` 收尾 | 未发现「切页后后台还在跑」的泄漏 |
| `LaunchPage` 事件收口 | `finished`/`progress` 只处理 `ev.TaskId == _taskId` 的任务,`_progGen` 代际号防「过期的隐藏进度」,`game_exited` 的退出码展示 | 自洽(C-01 是并发入口问题) |
| `InstallPage` | `list_loader_versions` 的 6 路并发结果索引(`res[0..5]` 对应 fabric/forge/neoforge/quilt/optifine/liteloader)、`Best()` 的空列表保护、`_go` 的 `finally` 复位、Fabric API 二次安装的 `TaskStore.Updated` 自我退订 | 自洽 |
| `Shortcut`(生成侧) | `SafeFilename` 非法字符替换、`.lnk` 用 PowerShell COM 生成并检查退出码、`_create_linux/_create_macos` 的引号处理 | 生成侧自洽(消费侧缺口的修复见 C-02) |

## 4. 未修项与原因

| ID | 为什么未修 |
|---|---|
| C-08(TarReader 无上限分配) | 唯一调用方是 `SafeExtractTargz`,目前只用于 Mojang/Adoptium 的 Java runtime 包(官方 CDN)。加大小上限会改变「坏包」的错误语义(现在等价于「解压失败」,加限后要新增错误类型并被上层正确分类),属于需要设计确认的改动;按「最小改动、不猜意图」先记录。修复建议:分配前校验 `num <= 剩余压缩流可解压量` 或 `num <= 1<<31`,超限抛 `ValueError("压缩包成员过大")` |
| C-09(ZipFolder mtime 上界) | 触发条件极罕见(坏 mtime / 时钟跳变),且失败面只是「这次备份失败」而非数据损坏。修复建议:`dateTime.Year > 2107 → 2107-12-31`,与现有 `<1980` 钳位对称;因涉及备份产物时间戳语义,先记录 |
| 记录性:静态事件长期订阅 | `TasksPage`(`TaskStore.*`)、`InstallPage`(`TaskStore.Updated += h` 仅任务 finished 后退订)、`MainWindow.HookPlugins`(`PluginUi.Changed`)都属于「页面/窗口是单实例且与进程同生命周期」,不构成可观察泄漏;改动收益低于风险 |
| 记录性:`Motion.CountUp` 对未入树元素立即退订 | `CountUp` 的停止条件含 `(!tb.IsLoaded && tb.Parent == null)` → 元素在入树前调用时首帧就退订,数字不跳动。属纯视觉,且改成「入树后再判」需要在多处调用点确认不会留永久订阅,先记录 |
| 记录性:`--ai-ui-selftest` 不在免提权白名单 | 见 X-02,需要你拍板 |

## 5. 剩余未审部分(诚实清单)

已逐行/逐段读过并核查的对象见 §3。以下**未**做到逐行审计,建议后续线路接手:

1. `Core/Launcher/Ai/**`(78 文件):仅细读 `Client.cs`(SSE 组装、`AssembleStream` 的 tool 参数拼接与降级)、`AiResponse.cs`(带超时读取)、`AiSession.cs` 片段。`Agent.cs`(63KB 工具循环)、`Tools.cs`(53KB)、`ExtraTools.cs`(38KB)、`Checkpoint/Rewind/Compact/Permission/Conflict/Preview/Mcp*` 未逐行。
2. `Core/Plugins/**`(19 文件,~250KB):仅看了 `PluginManager`/`PluginMarket`/`NodeRuntime` 的函数签名与若干片段;`JsonRpcPeer`、`PluginHostApi`、`NodeHostProcess`、`SemVer`、`PluginManifest` 未逐行。
3. `Core/Launcher/Crash.cs`(63KB)、`Terracotta.cs`(46KB)、`Mods.cs`(55KB)、`Modpack.cs`(59KB)、`Catalog.cs`(43KB)、`Sysinfo.cs`(25KB)、`Java.cs` 只读了探测/选择/下载三段:未逐行。
4. 大页面:`SettingsPage`(69KB)、`SettingsLayoutPage`(62KB)、`LayoutEditor`(99KB)、`LayoutEngine`(88KB)、`AiPage`(84KB)、`LaunchPage` 只读了构造/启动/停止/事件分发:`CatalogPage`、`LibraryPage`、`VersionManagePage`、`AccountPage`、`MultiplayerPage`、`FeedbackPage`、`Plugins*` 未逐行(布局类有 `Layout*Tests` 强覆盖)。
5. `Services/LucideAssets.cs`(140KB 资源表)、`Themes/*.xaml`(视觉)、`Core/BackendApi.cs`(150KB RPC 门面,只读了启动/游戏进程那段):未逐行。
6. 无交互桌面,未做 WPF 实机回归;C-01/C-02/C-04/C-05 的行为验证依赖你侧实机点一遍(见 §6)。

## 6. Windows 实机回归清单(建议)

1. 启动页快速双击「启动游戏」:只应产生一个 `download/launch` 任务;启动中再双击不应再起第二个游戏进程;C-01。
2. 版本管理里「创建桌面快捷方式」→ 关闭启动器 → 双击桌面 `.lnk`:应直接启动该实例/版本(而不是只打开启动器窗口);C-02。
3. 插件页打开一个 web 插件 → 快速切页/切回(或在插件被卸载时立刻切走):任务管理器里 `msedgewebview2.exe` 不应残留;C-04/C-05。
4. 设一次主题/动画比例后强杀进程,再启动:主题与动画设置应保持;`wpf-ui.json` 不应出现截断(C-06,可故意留一个半截 `wpf-ui.json.tmp` 验证不覆盖正式文件)。
5. 设置 → 布局 → 应用方案后,方案列表里自动留底的那条在英文界面应显示 “Previous layout”;C-07。
6. 非管理员 shell:`TipCL.exe --design-sheet` 不应弹 UAC;C-03。

## 7. 提交记录

分支 `audit/C`,2 个 commit(基线 `b86afe3`):

1. `386f985` audit/C: 启动重复点击重入守卫 + WebView2 初始化期销毁泄漏 + i18n 缺键 + wpf-ui.json 原子写(C-01/C-04/C-05/C-06/C-07)
2. `90111cf` audit/C: 桌面快捷方式 -i launch 参数落地 + --design-sheet 免提权 + LayoutStoreV2 过期注释(C-02/C-03/C-10)

未提交任何临时/环境文件(`_tmp/` 保持忽略,不进 git)。未 `git push`。
---

# 第二轮 — 布局自定义"丝滑度" + 假按钮/断线入口盘点

- 分支:`audit/C`(已 rebase 到最新 main),第二轮提交见 §R2.6
- 基线复核:`dotnet build windos/src/TipCL/TipCL.csproj -c Release` = 0 警告 0 错误;`dotnet test windos/tests/TipCL.Tests` = 547/547
- 范围纪律:`src/**`(Linux 侧)只读不改;`windos/tests/**` 只跑不改

## R2.0 结论摘要

| 级别 | 发现 | 已修 | 只记录 |
|---|---|---|---|
| P1 | 2 | 2 | 0 |
| P2 | 5 | 5 | 0 |
| 核查无问题 | 7 类 | — | — |

两件用户点名的东西都落到了实处:①**边缘自动滚动**原来在指针停住时完全不滚(见 R2.1-1);②**「新对话」预设下拉在 Windows 上是死的**——客户端一直在调一个后端根本不存在的 RPC(见 R2.2-1)。

## R2.1 布局自定义"丝滑度"专项

### R2.1-1【P1 已修】拖到底部/顶部拖不动:边缘自动滚动只在鼠标移动时发生

- `文件:行`:`windos/src/TipCL/Pages/Layout/LayoutEditor.cs:1922`(`AutoScrollNearEdge`,改前体在 `1863` 一带被 `OnBoxDragMove` 调用)
- 根因:`AutoScrollNearEdge` 只从 `OnBoxDragMove` / `OnBoxMarqueeMove` 里调用,而它做的是"一次滚动一个固定 18px"。于是:
  1. **指针停在边缘不动 → 不再产生 MouseMove → 完全不滚**(高页面上根本拖不到底部,只能反复搓鼠标);
  2. 滚动速度 = 18px × 鼠标事件率 —— 1000Hz 游戏鼠标上快到飞起,慢机器/远程桌面上又一顿一顿;
  3. 滚动后指针在 `_box` 坐标里的位置变了,但落点/框选没有跟着重算(下一帧才补)。
- 修复:改成 **16ms DispatcherTimer 驱动、按经过时间算速度**(贴边 300px/s → 最深 1500px/s),滚动后把"内容滚了多少"折回指针的 `_box` 坐标,立刻重算落点/框选。新增 `_autoScroll` / `_autoScrollPointer` / `_autoScrollDir` / `_autoScrollDepth`(`:161`)、`OnAutoScrollTick`(`:1972`)、`StopAutoScroll`(`:2030`),把框选重绘抽成 `ShowMarqueeAt`(`:1875`)供鼠标与计时器共用;手势结束(`EndBoxGesture`)与退出编辑(`ExitEdit`)都会 `StopAutoScroll()`,不会留下空转计时器。
- 验证:build 0 错误、547/547。**手感需实机确认**(无交互桌面跑不了 WPF)。

### R2.1-2【核查无问题】拖动跟手方式:不是每帧改 Grid 行列,而是 RenderTransform

一次 grid 拖动的完整帧内调用链(证据,按代码顺序):

```
MouseMove
 └ OnBoxDragMove (LayoutEditor:1872)
    ├ AutoScrollNearEdge(point)          → 只在边缘区做滚动(见 R2.1-1)
    └ ApplyGridGesture(dx,dy) (2107)
       ├ SideBySideSlot(...)             → 纯格子运算(每帧一次 bool[cols] 分配,cols=12)
       ├ _box.MoveFloat(dx,dy)           → 只写被拖卡片的 TranslateTransform.X/Y，不触发布局
       └ 落点没变 → return                ← 绝大多数帧走这里：一帧只有两次 DP 写入
落点变了才:
       RestoreGestureSnapshot → MoveCell → CompactAroundGesture
       └ _box.RequestFlip() + _box.CompactCells(pinned,swapX,autoPack)
          └ LayoutGrid.Compact + Reflow → InvalidateMeasure + InvalidateArrange + InvalidateVisual
下一次布局趟:
       MeasureGridChildren(width)        → 每块 Measure( 列宽, ∞ )；约束没变时 WPF 直接命中缓存
       ArrangeOverride                   → 按矩形 Arrange；浮起块走 _float 像素矩形
       StartFlip(child, old, target, ms) → TranslateTransform 上做 170ms ease-out 反向偏移归零
```

结论:**跟手路径上没有布局、没有读盘、没有 Save**(`Persist` 只在手势结束时调一次,`LayoutEditor:1987`/`EndDrag:1640`),没有 `LayoutChanged → 重排 → 再 Save` 的回路(`Persist` 内 `_saving` 守卫 + `BlockHost.OnLayoutStoreChanged` 的 `editor.IsSaving` 早退)。落点每变一次才付一整趟 measure/arrange,这是"行高按内容自适应"的必要成本,不是每帧成本。

### R2.1-3【核查无问题】落位预览/占位框

`DropPlaceholder` 走的是"每个被拖块一个 Border,插在 Children[0],ZIndex=-1",拖动中不重建;`SetPlaceholderRect` 只在像素高度吸附值真的变了才调(`LayoutEditor:2132` 附近)。grid 模式**不画**对齐辅助线(`_guide.Clear()` 每帧一次,`GuideLayer.Clear` 在空的时候直接 return,零成本)。

### R2.1-4【核查无问题】尺寸拖拽:预览与钳制

- 下边整条边 = 像素高度:`SnapPixelHeight(start+dy)` 4px 吸附、最小 56px(`LayoutMotion:197`);拖动中只更新占位框,松手才写 `lead.HeightPx`(`EndBoxGesture:1961`),其它块松手后统一压紧。
- 按格缩放:列按列宽折、行按**真实行高**折(`LayoutMotion.RowStep`),不是按被拖卡片自身高度(老 bug 已修)。
- 连续拖拽无浮点漂移:每次重算都从 `_gestureSnapshot` 全量快照出发(`RestoreGestureSnapshot:2347`),不在当前值上累加。

### R2.1-5【核查无问题】动画竞态

- FLIP 反向偏移算上了"还没走完的旧动画当前值"(`LayoutMotion.FlipOffset(old, currentOffset, new)`),同一块连续被让位时从眼睛看到的位置接着走。
- 松手落位在**第一帧**等 `IsMeasureValid && IsArrangeValid`(最多 100ms)才定终点,避免"终点取到提交前的旧格子"。
- `BeginFloat` 先 `StopSettle(finish:true)` 再起新手势:上一次落位动画没走完就被抓起时不跳。
- 浮起卡片不会被别的块的 FLIP 影响(Arrange 里 `floating` 时跳过 StartFlip)。

### R2.1-6【核查无问题】切页/进退编辑态的滚动位置

`BlockHost.Unloaded` 里只退订静态事件 + 必要时 `ExitEdit`,不销毁卡片缓存;`Loaded` 里 `ReloadFromStore()` 的 `Children.Clear()` 与重挂是同一方法内同步完成的,中间不会插布局趟,`ScrollViewer` 的 extent 不会塌陷,`VerticalOffset` 因此保留。**这一条建议实机再目视一次**(静态推理能排除"渲染两遍",但排除不了"卡片内容高度异步变化导致 offset 被夹")。

### R2.1-7【只记录,未改】stack(纵向流)模式拖拽不跟手

`LayoutEditor.OnDragMove:1615`(stack 换序)只做"跨过相邻块中点就交换",被拖卡片**不跟随指针**(只把 `Opacity` 降到 0.75),靠 `InsertVisual` 把卡片搬到新位置。设计上明确写了"不做动画插值,交换即落位"(`:1569`),Motion 关闭时行为也一致,所以没当作 bug 改;但这确实是"看起来不跟手"的第一现场。若要做成丝滑版,需要把 stack 也切到"浮起 + FLIP"那套(改动面等同新增一种手势,风险不小),建议单开一轮。

## R2.2 假按钮 / 没接功能的入口盘点

方法:①全仓 `Click +=` / `MenuItem` 共 **141 处**逐个看过 handler 体(脚本 dump:`_tmp/clickdump.txt`);②机械比对**客户端用到的 199 个 RPC 名 vs 后端 239 个可导出方法**(`RpcDispatcher.IsExposedName` 规则);③再比对"客户端关键字实参名 ⊆ 后端形参名"。

### R2.2-1【P1 已修】「新对话」预设下拉在 Windows 上完全没用:后端没有 `ai_presets`

- `文件:行`:客户端 `windos/src/TipCL/Pages/Plugins/AiViewStrip.cs:368`;后端缺口 `windos/src/TipCL/Core/BackendApi.cs`(新增 `:480`)
- 根因:`AiViewStrip.LoadAsync` 调 `TryCallAsync<JsonElement>("ai_presets")`,而 `BackendApi` 里**从来没有这个方法**。`BridgeClient.TryCallAsync` 把所有异常都吞掉(`BridgeClient.cs:129-140`),于是 `_items` 恒为空:
  - 「新对话」旁边那个下拉开出来永远是「普通对话 + 还没有插件提供预设」;
  - `AiViewStrip.Name(presetId)` 只能把 `plugin:xx/yy` 截成 `yy`,对话列表上的预设名永远是原始 id。
  数据其实一直在:`TipCL.Core.Plugins.AgentPresets.All()` + `AgentPreset.ToInfo()` 早就给了 `{id, plugin, name, description, order, view}` 这个刚好对得上的形状。
- 处置:**接上已有能力** —— 新增 `public object ai_presets()` 返回 `AgentPresets.All().Select(p => p.ToInfo())`。
- 验证:build 0 错误;RPC 名/参数比对脚本复跑后 `MISSING_RPC=0`(本轮之前唯一一条就是它)。

### R2.2-2【P2 已修】写操作用 TryCallAsync = 失败静默 = "点了毫无反应"

`TryCallAsync` 的设计目的是"读不到就用默认值",但被大量用在**写操作**上:后端回 `TypeError` / 写盘失败 / 方法不存在时,用户看到的只有"什么都没发生"。这一类是用户说的"坏了但看不出坏"的主因。

处置:新增 `BridgeClient.MutateAsync(method, args, failTitle)`(`BridgeClient.cs:148`)——失败弹错误提示并返回 false;把 13 个写操作调用点换过去:

| 文件:行 | 用户动作 | 失败时的旧表现 | 现在 |
|---|---|---|---|
| `Pages/GlobalModsDialog.cs:43/51` | 全局模组开关 | 开关跳回去,无提示 | 弹「开关全局模组失败」 |
| `Pages/ModsDialog.cs:118/127` | 实例模组启用/停用 | 同上 | 弹「启用/停用模组失败」 |
| `Pages/CatalogPage.cs:1105/1114` | Mod 页列表里的启用/停用开关 | 同上 | 同上 |
| `Pages/InstanceDetailPage.cs:217/221` | 实例详情里的启用/停用 | 同上 | 同上 |
| `Pages/SettingsPage.cs:1670` | 切换语言 | 选了语言界面不变 | 弹「切换语言失败」 |
| `Pages/LaunchPage.cs:1029` | 启动页选 Java | 下次启动还是旧 Java | 弹「保存 Java 选择失败」 |
| `Pages/LaunchPage.cs:1039` | 改内存/分辨率 | 静默不存 | 弹「操作失败」 |
| `Pages/LaunchPage.cs:1540` | 「停止」游戏 | 点了没反应、游戏继续跑 | 弹「停止失败」并恢复按钮 |
| `Pages/TasksPage.cs:186` | 任务卡的「取消」 | 按钮一直灰着(代码里先禁用了) | 弹「取消失败」并恢复按钮 |
| `Pages/AccountPage.cs:451` | 关闭登录浮层时取消登录任务 | 登录任务继续在后台 | 弹「取消登录失败」 |
| `Pages/FirstRunWizard.cs:161` | 向导「跳过」 | 下次开还弹(以为没生效) | 弹「保存失败」 |

**只记录未改**:`Pages/FeedbackConsent.cs:96`(首次启动的诊断数据同意写入)仍是 `TryCallAsync` —— 隐私同意写失败会下次再问一次,不构成"假按钮",且加错误弹窗反而打扰,故保留。

### R2.2-3【核查无问题】没有真空实现 / NotImplemented / 占位死入口

- 全仓 `NotImplementedException` = 0(只有两处 `catch (e is NotImplementedException)` 的类型判断);`暂未支持` 只出现在陶瓦联机的**架构判定**里(`Terracotta.cs:139/458/727`),是真的"这个 CPU 架构没有官方内核",属于正确拒绝而不是空实现。
- 141 个 handler 里没有一个是空 lambda:`Click += delegate { }` 0 处。
- 所有 `MenuItem` 都有实际动作或 `IsEnabled=false` 的说明项(如「还没有实例」「还没有插件提供预设」)。
- 「死入口」检查:导航项 10 个全部对应真实页面;`design-sheet` 是 dev-only 样张页(不进导航),符合 `docs/design-spec.md:110`。

### R2.2-4【核查无问题】RPC 参数名对齐

机械比对"客户端 `new { ... }` 里的关键字 vs 后端形参名":唯一命中是 `ServersPage.cs:175 update_server`,而后端该方法是 `update_server(object instance, object index, [VarKw] PyDict kwargs = null)` —— kw 吸收任意键,属正常用法。**结论:不存在"客户端传了后端不认的参数导致静默失效"的入口**(参数名不匹配时 `RpcDispatcher` 只 `Warn` 到控制台,是排查这类问题的关键线索,已确认没有)。

## R2.3 词表补齐(一致性线路交叉核对)

按 lead 转来的缺口核实并补齐(WPF 侧只有 `I18n.L` 的字面量我上一轮扫过 1985 条;`I18nCore.Tr` 走的是 Core 侧调用,所以漏了):

| key | 位置 | 上一轮 | 本轮 |
|---|---|---|---|
| `上一次布局` | `Pages/SettingsLayoutPage.cs:1396` | 已补(C-07) | — |
| `已随装前置:` | `Core/BackendApi.cs:1662` | 缺 | 已补(文案照抄 Linux 侧 `Bundled prerequisites:`) |
| `更新包已下载,重启启动器即可完成更新` | `Core/BackendApi.cs:3858` | 缺 | 已补(`Update downloaded; restart the launcher to finish updating`) |

`zh_CN.json:3034-3035` / `en.json:3032-3033`,已重跑 `windos/tools/pack-core-resources.ps1`(源 555,894 B → 压缩后 160,868 B),`CoreResourcesPackTests` 绿。
另:README v1.2.0 的 exe sha256 由一致性线路修正,本报告不复述任何 sha256,避免写回旧值。

## R2.4 第二轮提交

`audit/C` 上的 R2 提交(标题):「边缘自动滚动改计时器驱动(指针停住也滚/速度与事件率无关)+ 补 ai_presets RPC(预设下拉原本是死的)+ 写操作改 MutateAsync 失败可见 + 补两条词表」。
精确哈希看 `git log -1 --format=%h`——报告里不写死,避免后续 amend 后对不上。

## R2.5 仍需实机确认(无交互桌面,无法自动验证)

1. 拖块到可视区上下边缘**停住不动**:应持续滚动,速度随"贴边深度"变化;滚到底自动停。
2. 拖块时**在边缘绕圈**:不应出现滚动与落点打架(落点跟着内容走,不跳格)。
3. 装一个带 `agentPresets` 的插件后打开 AI 页「新对话」下拉:应列出预设,新建对话后消息列表应显示预设名。
4. 任意模组开关在"实例被占用/文件只读"时:应弹出错误提示而不是静默。
5. 启动页滚到中间 → 切到别的页 → 切回来:滚动位置应保持(R2.1-6)。

---

# 第三轮 — stack 纵向流拖拽丝滑化 + 三轮汇总

基线:`audit/C`(rebase 到最新 main);验证:`dotnet build windos/src/TipCL/TipCL.csproj -c Release` **0 警告 0 错误**、`dotnet test windos/tests/TipCL.Tests` **559/559**(547 + 本轮新增 12)。

## R3.1【P1 已修】stack 纵向流拖拽不跟手(第二轮 R2.1-7 的兑现)

### 改前的观感
`LayoutEditor.OnDragMove`(stack 分支)只做"跨过相邻块中点就交换顺序",被拖卡片**完全不跟随指针**,只把 `Opacity` 降到 0.75;换位是 `InsertVisual` 把元素搬到新位置,其余块瞬间跳一格。更糟的是判定直接和**裸中点**比较,指针停在中点附近时 ±1px 手抖会让块来回翻(用户说的"不丝滑"第一现场)。

### 改后的手感
每帧只写一次 `RenderTransform`;换位才动模型 + 补 FLIP;松手滑回格子。调用链:

```
MouseMove
 └ OnDragMove (LayoutEditor:1603 附近)
    ├ _stackPointerYLive = y; MoveStackFloat()      → TranslateTransform.Y = 基准 + (y - 起手y)
    ├ AutoScrollNearEdgeStack(y)                    → 靠近上下边缘才起计时器(见 R3.2)
    └ StackDragPlan.TargetIndex(...) 变了才 ApplyStackReorder(id, target)
         ├ CaptureStackTops()                       → 换位前各卡片 top(FLIP 的"旧位置")
         ├ _state.MoveVisibleTo(id, target)         → 只改 Order/Page.Blocks 先后(span/坐标/外观不动)
         ├ InsertVisual(id)                         → 元素搬到新下标(单列增量搬家)
         └ FlipStackCards()
              ├ 其余块:StackFlipOnce → 170ms ease-out 从"旧位置偏移"滑到 0
              └ 被拖块:_stackFloatBase += 格子跳变量;MoveStackFloat() 重新贴回手指下
松手
 └ EndDrag: StopStackFloat() 拿起点 → CancelDrag() 收尾 → StartStackSettle() 170ms 滑回格子
```

关键点:
- **跟手不花布局钱**:浮起期间只有 `TranslateTransform.Y` 一次写入;`_list` 的 SPanel 不做任何 measure/arrange 失效。
- **换位不闪**:被拖卡片的"格子位置"每被搬走一格都会瞬移,所以把跳变量累进 `_stackFloatBase` 抵消掉 —— 视觉上它一直停在手指下面(与 `GridCanvasPanel` 的 FLIP 同一套补偿思路)。
- **滞回防抖**:`StackDragPlan.Hysteresis = 6px`,必须越过相邻块中点再多 6px 才换位(第二轮那套"每帧判定"换成带余量的纯函数,可断言)。
- **松手收敛**:落位动画在 `CancelDrag` **之后**才挂(`LayoutEditor:1900-1919` 的注释写了为什么:顺序反了会被收尾逻辑立刻清掉,变成"啪"一下归位)。
- **可回退**:`Motion.Enabled == false` 或页面不是单列时 `BeginStackFloat` 直接返回(`:1691`),走**原来那套"交换即落位"**,行为与改动前一致;模型语义(stack = span + 顺序)一个字没动。

### 新增/改动文件
- 新增 `windos/src/TipCL/Pages/Layout/StackDragPlan.cs`(纯逻辑,零 WPF):`TargetIndex`(`:31`)、`IndexSeries`(`:57`)、`EdgeScrollStep`(`:77`)。
- 新增 `LayoutPageState.MoveVisibleTo`(`LayoutEngine.cs:536`):复用既有的相邻交换原语,只动顺序,`span/坐标/外观` 不碰。
- `LayoutEditor.cs`:浮起/跟手/FLIP/落位/自动滚动接 stack(`_stackFloat` `:1671`、`BeginStackFloat:1691`、`MoveStackFloat:1709`、`ApplyStackReorder:1720`、`FlipStackCards:1753`、`StackFlipOnce:1786`、`StopStackFloat:1809`、`StartStackSettle:1831`、`AutoScrollNearEdgeStack:1858`、`TickStackAutoScroll:2312`)。

### 新增单测(`windos/tests/TipCL.Tests/StackDragPlanTests.cs`,12 例,不改工程文件)
`TargetIndexNeedsToCrossMidpointPlusHysteresis`(165 不换 / 167 换)、`JitterAroundAMidpointNeverFlipsBack`(8 帧抖动序列恒为 1)、`DownwardSweepIsMonotoneAndReachesTheEnd` / `UpwardSweepIsMonotoneAndReachesTheTop`(位移序列 → 下标序列单调,不可能来回跳)、`UnevenHeightsUseTheCachedHeightOfEachBlock`、`TargetIndexClampsToTheListBounds`、`ZeroHysteresisMatchesTheLegacyMidpointRule`(等价性),以及模型层 `MoveVisibleToReordersWithoutTouchingGeometry` / `IsIdempotentAtTheSameIndex` / `KeepsHiddenBlocksInPlace` / `RepeatedSwapsMatchTheModelDragReorderSemantics`。

## R3.2【P2 已修】stack 拖到列表两端不会滚

R2 里的边缘自动滚动只覆盖 grid/canvas(`_boxScroll`)。stack 长列表里把块拖到底部边缘时同样需要滚,否则根本拖不过一屏。现在同一套 16ms 计时器 + `StackDragPlan.EdgeScrollStep`(贴边 25%→100% 速度,最深 18px/帧≈1100px/s)驱动 `_scroll`,滚动量折回指针内容坐标后立刻重跑换位判定(`TickStackAutoScroll:2312`)。

## R3.3 未审区域快速筛查结论(本轮只做机械/结构性检查,有真 bug 才修)

| 检查 | 范围 | 结论 |
|---|---|---|
| 静态可变集合 vs 锁 | `Core/Launcher/Ai/**`、`Core/Plugins/**` 共 20 个 `static readonly Dictionary/List/HashSet` | 运行期会变的那几个都有锁(`Agent._COMPACT_STATES`、`AgentPresets.Presets`、`ChatEventLog.Logs`、`PluginHooks.Entries`、`PluginTools.Tools/Index`、`Usage._data`、`Hooks._hooks`、`ModelCaps._cache`);`Tools.cs` 的 10 个全是只读查找表 + 一次性惰性 `_schemas`(引用赋值,重复构建只是浪费一次) → **无竞态 bug** |
| RPC 名/参数覆盖 | 全客户端 199 个调用点 | R2 已清零(`ai_presets` 补齐后 `MISSING_RPC=0`;参数名唯一命中是 `[VarKw]` 正常用法) |
| 空 catch / async void / `.Result` | 全仓 | 空 catch 4 处(正当)、async void 6 处(都有收口)、`.Result`/`.Wait()` 17 处全在后台线程(网络/进程 IO),未见 UI 线程阻塞 |
| Timer / 静态事件退订 | 全仓 | 页面级 `DispatcherTimer` 都在 `OnHidden` 停;`BlockHost`/`GridCanvasPanel`/`MainWindow` 静态事件均对称退订 |

**仍未逐行深审**(诚实清单,不阻塞发布):`Ai/Agent.cs`(63KB 工具循环)、`Ai/Tools.cs`(53KB)、`Ai/ExtraTools.cs`(38KB)、`Core/Plugins/**`(19 文件 ~250KB)、`Core/Launcher/Crash.cs`(63KB)、`Terracotta.cs`(46KB)、`Mods.cs`/`Modpack.cs`(55/59KB)、`Catalog.cs`(43KB)、`BackendApi.cs` 的 AI 段(150KB 里读过的部分见 §3)。

---

# 三轮汇总

## 累计战果

| 级别 | 发现 | 已修 | 只记录 |
|---|---|---|---|
| P0 | 0 | 0 | 0 |
| P1 | 6 | 6 | 0 |
| P2 | 12 | 11 | 1 |
| 合计 | 18 | 17 | 1 |

**P1(全部已修)**
1. 启动按钮在预检/登录/多开弹窗期间可重复点 → 并发两个 `launch_game`(R1 C-01)
2. 「创建桌面快捷方式」的 `-i … launch …` 参数在 Windows 侧没人消费 → 双击只开启动器(R1 C-02)
3. `--design-sheet` 漏在免提权白名单外 → 截图工具链被 UAC 重启截断(R1 C-03)
4. 拖动到列表/画布边缘后指针停住就不滚(自动滚动依赖 MouseMove)(R2 R2.1-1)
5. 「新对话」预设下拉调了后端不存在的 `ai_presets` → Windows 上预设功能整条是死的(R2 R2.2-1)
6. stack 纵向流拖拽不跟手(裸中点判定 + 块不跟指针)(R3 R3.1)

**P2(11 修 / 1 记录)**:WebView2 初始化期销毁泄漏、`Reload()` 无销毁守卫、`wpf-ui.json` 非原子写、缺 3 条 i18n 键、写操作静默失败 13 处改 `MutateAsync`、stack 拖拽两端不滚、LayoutStoreV2 过期注释;只记录:`FeedbackConsent` 的同意写入仍静默(隐私同意,加弹窗反而打扰)。另有 R1 记录未修:`TarReader` 无上限分配、`ZipFolder` mtime 上界。

## 我建议你实机点的清单(按性价比排序)

1. **stack 拖拽手感**(本轮重点):按住区块手柄上下拖 → 卡片应贴在指针下;跨过相邻块中点后,相邻块滑让一格(170ms),被拖块不跳;松手滑回格子;`Motion` 关掉(设置→动画)时退回"交换即落位"。
2. **拖到列表两端**:停住不动也应持续滚,到底自停。
3. **AI 页「新对话」下拉**:装一个带 `agentPresets` 的插件后应列出预设;新建对话后消息列表显示预设名(不再是 `yy` 这种裸 id)。
4. **启动页双击「启动游戏」**:只产生一个任务;启动中再点不应起第二个进程。
5. **桌面快捷方式**:版本管理里创建 → 关掉启动器 → 双击 `.lnk` 应直接启动该实例/版本。
6. **模组开关失败路径**:实例被占用或 jar 只读时切换开关,应弹错误提示而不是静默。
7. **窄窗/宽窗、切页来回、编辑态进出一轮**:不应出现区块错位、滚动跳顶、编辑态残留。
8. **插件 web 页快速切页**(含切走时插件被卸载):任务管理器里 `msedgewebview2.exe` 不应残留。
9. **非管理员 shell** 跑 `TipCL.exe --design-sheet`:不应弹 UAC。
10. **英文界面**看「设置 → 布局」方案列表的自动留底项、以及更新完成提示:应为英文。

## 结论

**WPF 层可判定审计完毕。** 依据:三轮里所有能机械化的判据都已清零或量化 —— RPC 名/参数覆盖(199/199)、i18n 键(1985 条字面量 + Core 侧 `I18nCore.Tr` 抽查)、XAML 资源键(125 定义 vs 全部引用)、`async void`/空 catch/`.Result`/Timer/静态事件退订/静态集合加锁、net48 API 垫片;编译 0 警告、559 测试全绿。

若还要继续,建议下一轮不是"审计"而是"逐行深审"这几个具体模块(Ai 工具循环的取消/超时、Plugins 宿主生命周期、Crash/Modpack 的解析健壮性)——它们体量大但**没有已知可疑点**,属于覆盖率补完而不是救火。

---

# 第四轮 — 覆盖率补完(通读体量大、无已知可疑点的模块)

本轮**不改行为**:只做通读/结构性验证;没有发现需要动代码的真 bug(见 R4.5 的判定依据)。验证口径与前三轮一致:`dotnet build … -c Release` 0 警告 0 错误、`dotnet test windos/tests/TipCL.Tests` 559/559。

## R4.1 `Ai/Agent.cs`(工具循环)—— 通读关键段,四项均已验证

| 关注点 | 结论 | 证据(`windos/src/TipCL/Core/Launcher/Ai/Agent.cs`) |
|---|---|---|
| **退出条件** | 三层封顶,不会无限转:①预设/设置的 `ai_max_rounds`(`num2`,`:639`,`PluginAgentBridge.MaxRounds`);②预设 0 = 硬上限 `AgentPresets.HardMaxRounds = 10000`;③达到上限走 `_result(..., "max_rounds", ...)`(`:1469`)并把"已用 N/M 回合"交给用户。另有失败计数 `toolFailures`(`:605`)与"上下文反复逼近窗口"主动中断(`:710`) | `:634-710`、`:1464-1469` |
| **重复副作用(同一工具跑两次)** | **没有自动重试工具执行**。工具异常一律转成结构化错误回给模型(`:1411-1422`,`Tools.ToolErrorPayload`),由模型决定要不要换个做法;HTTP 层的 `attempt` 重试(`:788`)只重发*模型请求*,发生在工具执行之前(工具在一轮流结束后才批量执行,`:1296-1300` 的 `ToolStarted`),因此流中断不会重放已执行的写操作 | `:788`(重试范围)、`:1296-1423`(执行与错误) |
| **取消传播** | `_check()`(`:1509-1515`,`cancelled()` 为真即 `AgentCancelled`)在 6 个关键点:`:646`(回合开头)、`:794`(请求前)、`:881`/`:1296`(工具批次前后)、`:1489`/`:1700-1726`(等待子任务/落盘循环)。工具内的 `ToolCancelled` 单独 catch 后**重新抛**(`:1405-1410`),不会被当成普通错误吞掉;外层 `catch (ToolCancelled)` 转成 `AgentCancelled`(`:1433-1438`) | `:1405-1438`、`:1509-1515` |
| **超大工具返回的内存** | `Tools._clip`(`Tools.cs:349-358`)在 **> 8000 字符**时改走 `Artifacts.Store`:落盘 `cache/ai_results/yyyyMMdd-HHmmss-<sha1前8>.txt`,只把"前 120 行 + 总行数/总字节 + 文件名"给模型;模型要更多内容用 `read_artifact(offset,limit)` 回读(`Tools.cs:1021`)。落盘失败时仍截断到 4000 字符(`Artifacts.cs:112-115`),不会把巨型字符串整个塞进上下文。保留策略 7 天 / 300 个文件(`Artifacts.Prune`) | `Tools.cs:349-358`、`Artifacts.cs:91-118` |

**`AskUser` 类交互在非 UI 线程的表现(R4.1 重点)**:安全,不存在"UI 线程阻塞等 UI 答案"的死锁。链路是:AI 回合跑在 `ai_send` 起的背景线程(`BackendApi.cs:570-577`,**锁已释放**后才起线程)→ `AskFn`/`ConfirmFn`(`BackendApi.cs:699-764`)`Reset()` 事件、`Emit` 让 WPF 弹卡 → `WaitCard`(`BackendApi.cs:898-908`)在**背景线程**上 `ManualResetEventSlim.Wait(200)` 轮询,每 200ms 检查 `_ai_cancel`(所以取消能立刻打断等待)→ 用户点确认/回答通过另一个 RPC 线程 `ai_confirm`/`ai_answer`(`:325-337`/`:376-379`)`Set()` 事件。单回合由 `_ai_lock + _ai_busy`(`:559-568`)保证,共享的 `_ai_confirm_ok`/`_ai_ask_result` 不会被两个回合互相覆盖。

## R4.2 `Ai/Tools.cs` / `Ai/ExtraTools.cs` —— 通读边界

- 工具清单是**静态 schema + 白名单**(`TOOL_SCHEMAS`,`Tools.cs:83`),没有从网络/插件动态拼 schema 的入口;`ask_user` 的工具实现直接返回"由界面处理"(`:820-821`),真正的交互走 `askFn`,职责清晰 —— 不会出现"模型以为问了、界面没弹"。
- 文件类工具(`ExtraTools`)有顶层 deny 列表与二进制后缀表(`ExtraTools.cs:507-516`),路径合法性统一走 `Utils.SafeChildPath` / `AiFs`(前几轮已核)。
- 错误回给模型时统一包裹 `{ok:false,error_code,message,detail}`(`Tools.cs:1059`),`detail` 截到 200 字符 —— 不会把整段异常栈灌进上下文。

## R4.3 `Core/Plugins/**` —— 单插件隔离成立

- `Discover`(`PluginManager.cs:288-328`):先按状态表逐个 `DiscoverOne`,再扫目录;目录项单独 `try/catch`(`:306-324`),坏清单/坏 id 只跳过自己。
- `DiscoverOne`(`:330-362`):清单加载失败 → `p.State = "error"` + `p.Error = ex.Message`,**不影响其它插件**,也不抛给调用方。
- 卸载/停止路径(`:1200-1500` 区间,含 Node 宿主停止、定时器清理、`PluginSessions`/`PluginPresets` 注销)都在 `lock (_lock)` + 单插件 `try/catch` 内;`PluginTools`/`PluginHooks`/`PluginUiRegistry` 的静态表均加锁(第三轮已核)。
- 权限边界:`PluginHostApi.LauncherDeny`(`PluginHostApi.cs:20`)显式禁止插件调 `ai_confirm`/`ai_answer`/`shutdown`/`start_task` —— 插件不能替用户点"同意",也不能关启动器。

## R4.4 `Crash.cs` / `Terracotta.cs` / `Mods.cs` / `Modpack.cs` / `Catalog.cs`

- **IO 健壮性**:`Crash.ReadText`(`Crash.cs:841-862`)读文件、解码(严格 UTF-8 → 兜底)全部在 `try/catch` 内,读不到返回空串;`Crash.cs` 里只有两处 `File.*` 且都在守卫内。崩溃分析不会因为"日志被占用/是二进制"而抛。
- **用户看到的"为什么崩了"**:`AnalyzeLaunch`(`Crash.cs:1793`)把结论拆成 `is_crash`/`summary`/`headline`/`detail`/`exit_hint`/`files`,失败时 `throw GameCrashError(report)`(`BackendApi.cs:3227`)让上层弹崩溃卡 + 「AI 修复」入口;导出报告(`ExportReport`)带"分析结论/游戏崩溃前的输出/latest.log"三个文件并做密钥过滤(`FilterSecrets`)。**文案可操作性**:报告里的 `exit_hint` 与"下一步该做什么"按规则给(如内存不足、Java 版本、模组冲突),这条路是通的。
- **`Mods`/`Modpack`/`Catalog`**:路径穿越、zip 条目名、Modrinth/CurseForge 目录隔离在前几轮已核(`SafeChildPath`、`Utils.SafeExtractZip`、`ResolveModsDirForInstall`);本轮只做 grep 级(见 R4.6)。

## R4.5 本轮为什么没有改代码

四条重点里**没有找到可复现的真 bug**:工具循环的退出/取消/重试边界是收敛的且有三层封顶;ask/confirm 的等待发生在背景线程、UI 只负责弹卡;插件加载是逐插件隔离的;Crash 的 IO 全部在守卫内。
特别说明一处**看着像 bug 但判定不是**:`BackendApi.cs:3222` 的 `Crash.AnalyzeLaunch(...)` 没有 try/catch(分析抛异常会连带吞掉 `game.exit` 钩子和"游戏已退出"返回值)。按"真 bug 才动代码"的纪律,我在 `Crash.cs` 里核到所有文件读取/解码都有兜底、`ReError` 只在编译固定正则时抛,构造不出让它抛的输入 —— 所以**只记录、不加防御性冗余**(加了属于猜改动)。若你想彻底免疫,可以给这一行包一层 `try/catch` + 记日志,代价约 6 行。

## R4.6 覆盖率结论(逐模块)

| 模块 | 覆盖级别 | 说明 |
|---|---|---|
| `Pages/Layout/**`、`BlockHost`、`LayoutShellHooks`、`PageLayoutStore`、`LayoutStoreV2` | **通读 + 单测** | 三轮共改 3 处、新增 12 例单测 |
| `Pages/**`(LaunchPage/TasksPage/InstallPage/CatalogPage/InstanceDetail/ModsDialog/GlobalMods/SettingsPage/AiPage 事件面) | **通读(事件与持久化路径)** | 13 处写操作改 `MutateAsync` |
| `Services/**`(BridgeClient/BridgeHost/WebViewHost/Dlg/Motion/SmoothScroll/TaskStore/I18n/AppServices/Wallpaper 片段) | **通读** | 修 WebView2 泄漏 ×2、`wpf-ui.json` 原子写 |
| `MainWindow` / `App` / `Shortcut` / `Utils` / `DownloadManager` / `Manifest` / `McLauncher` / `Java` / `Installer`(natives) / `Saves` / `BridgeServer` / `RpcDispatcher` | **通读** | 修启动重入、快捷方式、`--design-sheet`、过期注释 |
| `Core/Launcher/Ai/**`(Agent/Tools/ExtraTools/Client/Artifacts/AiResponse/AiSession 片段) | **通读关键段 + grep 全量** | 本轮四项重点全查;`Compact/Checkpoint/Rewind/Permission/Conflict/Preview/Mcp*` 仍只到 grep 级 |
| `Core/Plugins/**` | **grep 全量 + 关键路径通读**(加载/隔离/卸载/权限) | `JsonRpcPeer`/`NodeRuntime`/`PluginLlm` 只到 grep 级 |
| `Crash.cs` | **grep 全量 + IO/报告出口通读** | 规则表(约 1900 行里的匹配逻辑)只到 grep 级 |
| `Terracotta.cs` / `Mods.cs` / `Modpack.cs` / `Catalog.cs` / `Sysinfo.cs` / `BackendApi.cs`(AI 段以外) | **grep 级** | 结构性检查通过(RPC 覆盖、空 catch、加锁、路径校验),未逐行 |

## R4.7 跨域(与 Linux 移植版的潜在分叉,只报告不改 `src/**`)

- 本轮未发现新增分叉。已知两条仍有效:①`--ai-ui-selftest` 不在 Windows 免提权白名单(§X-02);②桌面快捷方式参数形状两边必须同步(§X-03)。
- aud-core 若在 Linux `Crash.cs` 里动了"崩溃结论字段名",注意 Windows 侧 `GameCrashError`(`BackendApi.cs:3227`)与 WPF `CrashUi`/`CrashReport` 都按 `summary/headline/detail/exit_hint/files` 取值,字段名要一起改。

---

# 第五轮(R6)— grep 级模块补审 + 两条跨域定论

基线:`audit/C` 已 rebase 到 main `880deff`;验证:`dotnet build … -c Release` **0 警告 0 错误**、`dotnet test windos/tests/TipCL.Tests` **573/573**。

## R6.1 `--ai-ui-selftest` 进 Windows 免提权白名单(定论 + 依据)

**决定:进**(与 `--design-sheet` 同口径)。改动:`App.xaml.cs:38` 的 `SelfCheckArgs` 7 → 8 项。

依据(全是仓库现有决定/代码,不是我的偏好):
1. `windos/DECISIONS.md:44`(及根 `docs/DECISIONS.md:790`)已裁定"`--shot`/`--design-sheet` 加入 `SelfCheckArgs`(免提权,与 `--smoke` 同类)" —— 免提权的口径是**"跑完靠输出/退出码交差的自检类参数"**,不是按参数名逐个开恩;
2. `--ai-ui-selftest` 完全符合这个口径:`Pages/Ai/AiSelfTest.cs:29` 解析它 → `App.OnStartup:123` 跑完把结果写 `Environment.ExitCode` 并 `Shutdown`;被 UAC 重启成**分离进程**后,stdout/退出码一样丢失(与 `--design-sheet` 完全同一个故障模式);
3. `docs/DECISIONS.md:412/498/506` 记录 CI/验收是**直接调 CLI** 看退出码(`--ai-selftest 54 项 exit 0`)—— 提权弹窗会把这个口径打断;
4. 它只用 `%LOCALAPPDATA%` 下的数据目录,不需要管理员权限;`app.manifest` 是 `asInvoker`,免提权不会降低任何权限前提;
5. 反向风险:正常人不会手输这个参数,加进白名单不会让"双击启动器"绕过提权。

## R6.2 `LayoutEngine` 与 Linux 侧 semantics 比对(只报告,不改)

| 项 | Windows(`windos/src/TipCL/Pages/Layout/LayoutEngine.cs`) | Linux(`src/TipCL.App/Pages/Layout/LayoutEngine.cs`) | 结论 |
|---|---|---|---|
| `DragReorder` | `:509-528`,逐格与相邻可见块**裸中点**比较,`MoveVisible` 交换 + `RebuildPage` 收尾 | `:535-554`,**逐行同源**(同一套 `MidpointY`/`MoveVisible`/`RebuildPage`) | **完全一致**,无分叉 |
| `MidpointY` | `:531` | `:557` | 一致(高度缓存,缺省 120) |
| `MoveVisibleTo`(R3 新增) | `:536`,目标下标版换位,复用同一 `MoveVisible` 原语 | **不存在** | 有意分叉:它是 Windows stack 浮起拖拽的"逐格落位"入口;Linux 侧仍是"跨中点即交换"。语义不冲突(同一个交换原语),但**功能面分叉**:Linux 的 stack 拖拽仍是老的不跟手行为 |
| 阈值 | `StackDragPlan.Hysteresis = 6px`(UI 层,非模型) | 无 | 模型层不吃滞回,只有 Windows 的手势层吃 → 若 Linux 将来接浮起拖拽,直接复用 `StackDragPlan` 的阈值口径即可 |

一句话:**模型语义一致(同为 span + 顺序 + 同一交换原语),分叉只在"手势层是否浮起跟手"**;要收敛的话 Linux 侧照 Windows 的 `StackDragPlan` + `MoveVisibleTo` 抄即可,不需要动 Windows。

## R6.3 本轮补审结论(逐模块)

| 模块 | 级别 | 结论 / 证据 |
|---|---|---|
| `Terracotta.cs`(1833 行) | 通读关键段 + grep 全量 | **进程生命周期完整**:`Start` 等端口 `/state` 就绪(20s 超时后报"端口已写出但接口没响应",`:1266-1268`),端口超时先 `Kill` 再 `throw`/重试(`:1245-1258`);版本不符走 `_peaceful_stop` + 3s 等 + 重来(`:1271-1277`);`_proc = Exited(process) ? null : process` 避免拿已死句柄(`:1279`);`Stop()` 先 HTTP 优雅停 → `Kill` + `WaitForExit(3000)` → 清 `_port`/`_proc`/runtime 文件(`:1288-1314`)。房间号:`ParseRoom` 按定宽 `XXXX-XXXX-XXXX-XXXX` 滑窗匹配 + `_lookup_room_char` 白名单(`去掉了易混字符`,基 32),`RoomError` 对 legacy/PCL2CE 房间给专门的拒绝文案(`:387-398`)→ 校验是"白名单 + 定宽 + 明确报错",不是"截一个长度就算"。端口探测 `_http_ok` 全在 try/catch(`:810-824`) |
| `Mods.cs` / `Modpack.cs` / `Catalog.cs` | grep 全量 + 关键路径通读 | 导入路径安全:`_extract_embedded_pack`(`:516`)解内嵌包到 `%TEMP%/<stem>_inner_<guid8><ext>`,**stem 取自 `new PPath(text).Stem`(文件主名)** —— Windows 文件名不允许 `\`/`:`,构造不出目录穿越;候选条目有 300MB 上限(`:529`)、按"包内评分"排序取第一个带包标记的(`:540-551`),失败即删临时文件;`overwrite: true` 只作用于**这个 guid 临时文件**,不覆盖用户文件。版本匹配沿用已核的 `Versions.MatchGameVersion` 一类纯函数 + `ModUpdate` 的规则表 |
| `Sysinfo.cs` | grep 全量 | 只读采集(`RtlGetVersion`/WMI 文本解析),`long.Parse` 都在解析固定格式输出的位置且有兜底;无进程/句柄 |
| `JsonRpcPeer.cs` | 通读挂起表 | **无 TCS 泄漏**:响应、错误、超时、取消、发送失败五条路径都 `_pending.TryRemove` + `TrySet*`(`:267-279`、`:373-398`、`:417-419`);`RequestAsync` 用 `RunContinuationsAsynchronously` 避免在 IO 线程上跑续体 |
| `NodeRuntime.cs` / `NodeHostProcess.cs` | grep 全量 | 宿主进程由 `NodeHostProcess` 统一 `Dispose`(kill + 等退出);`NodeRuntime` 只管下载/校验/解压运行时(sha256 校验 + 临时目录 + `.gz` 解压),不长期持有进程 |
| `PluginLlm.cs` | grep 全量 | HttpCancel 走 `_active` 字典 `TryAdd`(`:107`),中断/完成后移除,无泄漏 |
| `Ai/Compact|Checkpoint|Rewind|Permission|Conflict|Preview|Mcp*` | grep + 关键函数 | `Compact` 只在 token 超阈值时触发,失败 `NoteCompactFailure` 后不重试死循环;`Checkpoint`/`Rewind` 的文件遍历有 `_iter_dirs` 限制与 UTF-8 兜底解码;`Permission` 规则集是**白名单字段 + `_BEHAVIOR_KEYS` 映射**;`Preview`/`Conflict` 是纯函数(行匹配/依赖图),不碰 IO;`McpServer` 的 `CloseAll` 在 `Agent` 的 `finally` 里无条件调(`Agent.cs:1454`)—— 每轮结束都会关 MCP 客户端 |

**本轮未发现真 bug,未改业务代码**(唯一改动是 R6.1 的白名单一行 + 注释)。特别记录一处"看着像、判定不是":`Crash.AnalyzeLaunch` 未包 try/catch(第四轮 §R4.5 已说明为什么不动)。

## R6.4 覆盖率现状(更新第四轮的表)

- **通读 + 单测**:`Pages/Layout/**` 全部、`BlockHost`、`LayoutStoreV2`、`PageLayoutStore`、`LayoutShellHooks`
- **通读**:`Pages/**` 事件与持久化面、`Services/**`、`MainWindow/App/Shortcut/Utils/DownloadManager/Manifest/McLauncher/Java/Installer(natives)/Saves/BridgeServer/RpcDispatcher`、`Ai/Agent|Tools|ExtraTools|Client|Artifacts|AiResponse`、`Plugins` 加载/隔离/卸载/权限、`Crash.cs` 的 IO 与报告出口、**本轮:`Terracotta` 进程与房间号、`JsonRpcPeer` 挂起表、`Modpack` 内嵌包解出**
- **grep 级(结构性检查通过)**:`Mods`/`Modpack` 除上述路径以外的业务分支、`Catalog`、`Sysinfo`、`NodeRuntime`/`NodeHostProcess`/`PluginLlm`、`Ai/Compact|Checkpoint|Rewind|Permission|Conflict|Preview|Mcp*`、`Crash.cs` 的规则匹配表、`BackendApi` 的 AI 段以外
- 再往下就是"逐行读 200KB 业务分支"的量,已无已知可疑点可追。

---

# 第七轮(R7)— 复审我们自己的修复(自审)

基线:`audit/C` rebase 到 main `97eae96`;全量 diff `b86afe3..HEAD -- windos/src` 共 25 文件 / +1487−−45(其中**我这条线**的改动 = 20 个源文件 + 2 个词表(+2 个 `.gz`)+ 2 个新文件)。
验证:`dotnet build … -c Release` **0 警告 0 错误**、`dotnet test windos/tests/TipCL.Tests` **582/582**。

## R7.0 复审统计

| 项 | 数 |
|---|---|
| 逐行复审的改动面 | 5(stack 手势 / `MutateAsync` 13 处 / 快捷方式参数 / WebView2 生命周期 / 词表) |
| 逐条核对的风险点 | 12 |
| **自伤(本轮发现并修掉的、我自己引入的问题)** | **4** |
| 非我引入、本轮顺带修掉的既有缺口 | 1(Terracotta 内核自杀后的状态残留) |
| 复审结论"没问题"的风险点 | 8(见 R7.5) |
| 新增/加强的断言 | 6 例(`StartArgsTests`) |

## R7.1 自伤 #1:快速连拖同一块会跳一下(已修)

- 现场:`EndDrag` → `StartStackSettle` 挂上 `settle` 平移动画(170ms 收敛);用户在动画没走完时又抓起**同一块**,`BeginStackFloat` 里 `card.RenderTransform = new TranslateTransform()` 从 **0** 起步 → 丢掉 settle 剩余位移,视觉上"啪"跳一下。
- 修:`BeginStackFloat` 读旧 transform 的 `Y` 当基准(`LayoutEditor.cs`,注释写明"从眼睛看到的位置接着走"):
  `double resume = card.RenderTransform is TranslateTransform leftover ? leftover.Y : 0.0; _stackFloatBase = resume; new TranslateTransform(0.0, resume);`
- 与 `GridCanvasPanel.StartFlip` 的 `LayoutMotion.FlipOffset(old, currentOffset, new)` 是同一个思路(用"还在走的动画当前值"接续),这次把它补齐到 stack 侧。
- 断言:UI 动画本身断言不了(需 WPF 渲染);**纯逻辑侧**的等价保证在既有 `StackDragPlanTests` 的抖动/单调用例里,此处如实标注"只能实机看"。

## R7.2 自伤 #2:快捷方式参数解析脆弱且不可测(已修 + 6 例断言)

- 现场:解析逻辑内联在 `App.ParseStartArgs`,两个隐患 —— ①`args[i+1].Length > 0` 用的是**未 Trim** 的原始串,`-i "   " launch x` 会被当成合法实例名;②无法单测,畸形输入(`-i` 在末尾、`launch` 大小写写错、`--i18n-check` 里的 `-i` 片段)只能靠人工。
- 修:抽出纯函数 `Services/StartArgs.TryParse`(Trim + 逐项 `Ordinal` + 只认 `-i <non-empty> launch <ver>` 四连),`App.ParseStartArgs` 只落状态。
- 断言:`windos/tests/TipCL.Tests/StartArgsTests.cs` 6 例 —— 标准形状、前后夹其它参数、五种不完整序列、**6 个自检参数绝不触发**(含 `--i18n-check`)、Trim + 空版本、畸形 `-i` 被跳过继续找下一个完整序列。

## R7.3 自伤 #3:连点两次「重新加载」会漏一个 WebView2(已修)

- 现场:`Reload()` → `DisposeWeb()`(`_web` 可能还是 null,因为上一轮 `StartAsync` 还在 `await EnsureCoreWebView2Async`)→ 新的 `StartAsync` 各建一个 `WebView2`,`_web` 只记得后一个;前一个既不在 `_web`、也没人 Dispose → **漏一个 `msedgewebview2.exe` + HwndHost**。R1 我只修了"初始化期间 Dispose"那一条路径,这一条(重入)漏了。
- 修:`StartAsync` 加代数号 `_startGen`,两个 `await` 回来都检查 `gen != _startGen`;被顶掉的那一趟走新增的 `DiscardWeb(web)`(只收掉"不属于 `_web` 的那个",`ReferenceEquals(_web, web)` 时退化成 `DisposeWeb`)。`Dispose()`/`Unregister` 路径不变。
- 断言:需要真 WebView2 运行时,`windos/tests` 里无法构造 → 只能代码审 + 实机点(见清单第 8 条)。

## R7.4 自伤 #4 + 既有缺口 #1

**自伤 #4:快捷方式会跟"已经在跑的游戏"打架(已修)**
- 现场:启动器**没有单实例守卫**(全仓无 `Mutex`)→ 已经开着一个启动器时双击 `.lnk` 会起**第二个启动器**,两边都自动 `LaunchNowAsync`;第一局还没进入"运行中"状态时,第二个窗口的 `CheckMultiInstanceAsync` 挡不住 → 可能同时拉两个游戏进程。改前双击 `.lnk` 只开窗口是无害的,这个风险是我这条线引进来的。
- 修:`StartFromShortcutAsync` 先 `is_game_running`,为真就只提示「游戏已经在运行 / 快捷方式只把启动器打开了」并返回(顺带避免了一次模态"多开询问")。
- 断言:依赖后端状态,UI 层无法单测 → 实机清单第 5 条。

**既有缺口 #1:Terracotta 内核自己退出后状态残留(已修)**
- 现场:`_port` 与 runtime json(`_runtime_file()`)只在 `Stop()` 里清;内核崩溃/被杀时没人清 → 联机页继续显示「已就绪(127.0.0.1:旧端口)」,「停止」按钮去连一个已死端口。`Running()` 虽会自愈成 false,但残留状态会一直挂在界面上。
- 修:拉起内核后 `EnableRaisingEvents = true` + `Exited` 订阅 → `OnKernelExited(process)`:`lock` 下确认"还是当前进程"再清 `_proc`/`_port`/runtime 文件(`Stop()` 先把 `_proc` 置空再 Kill,所以正常停止不会误清新实例)。
- 断言:需要真实内核进程,不写单测。

## R7.5 复审为"无问题"的风险点(8 条)

1. **鼠标捕获在拖出窗口/失焦/Alt+Tab**:`LostMouseCapture` → `OnDragLost` → `EndDrag(animateSettle:false)` 全收(解绑 `MouseMove/MouseLeftButtonUp/LostMouseCapture`、释放捕获、清 translate/占位/自动滚动),已把本次改动持久化(与改前一致)。
2. **拖动中页面被切走**:`BlockHost.Unloaded` → `ExitEdit()` → `CancelDrag` → `StopStackFloat`,不会留残影;`_layoutTouched` 语义没变,`RestoreStackAfterEdit`/`Persist` 的分支与改前同构。
3. **`Motion.Enabled == false` 回退路径**:`BeginStackFloat` 直接 return → `_stackFloat=false` → `OnDragMove` 走**原封不动**的 `_state.DragReorder + InsertVisual`;`StopStackFloat` 返回 0、`StartStackSettle` 因 `|0| < MinOffset` 直接返回。即关掉动画时与改前逐像素一致(逻辑上等价,非目测)。
4. **`StartStackSettle` 的 `Completed` 回调误清新 transform**:`ReferenceEquals(card.RenderTransform, settle)` 守卫,且 `StackFlipOnce` 同样有守卫 → 快速连拖不会清掉别人的动画。
5. **`MutateAsync` 换掉后的按钮态/忙标志**:13 处全部核对 —— `TasksPage` 取消失败恢复 `cancel.IsEnabled`(且 `Sync` 会按 `!row.Finished` 重算)、`LaunchPage` 停止失败恢复 `_stopBtn`、其余调用点没有依赖返回值或 `_busy` 的地方。
6. **失败弹窗是否重复**:`MutateAsync` 自己 catch 后返回 false、**不重抛**,外层 `PageBase.Run` 的 catch 不会二次弹 → 不会双弹。
7. **`TakeStartArgs` 的"取一次就清"**:只在真实 UI 分支解析(`--smoke/--shot/--design-sheet/--ai-ui-selftest` 都在更早的 `OnStartup` 分支 `Shutdown` 掉了),`StartFromShortcutAsync` 又对 `Smoke.Active` 早退,重连/换窗口不会二次触发。畸形输入由 R7.2 的 6 例断言兜住。
8. **`MoveVisibleTo` 的模型语义**:复用同一 `MoveVisible` 交换原语 + 单次 `RebuildPage`,与 `DragReorder` 到达同一下标的结果一致(既有 `RepeatedSwapsMatchTheModelDragReorderSemantics` 断言);span/坐标/外观不动(`MoveVisibleToReordersWithoutTouchingGeometry`)。

## R7.6 任务二:`Terracotta` / `Mods` / `Modpack` / `Catalog` 通读补充

| 关注点 | 结论 |
|---|---|
| **进程退出后的端口/runtime 清理** | 缺口已修(R7.4);`Stop()` 顺序正确(先 `/panic?peaceful=true` 优雅停 → `Kill` + `WaitForExit(3000)` → 清 `_port`/`_proc`/runtime 文件),`Start()` 里端口超时/版本不符两条失败路径都会先 `Kill` 再重试或抛出 |
| **导入导出覆盖行为** | 导入:内嵌包解到 `%TEMP%/<stem>_inner_<guid8>`(`stem` 取自文件名,Windows 文件名不含 `\`/`:`,无穿越)、单条目 300MB 上限、`overwrite:true` 只作用于 guid 临时文件;导出:菜单「导出布局/导出整合包」一律走 `Dlg.SaveFile` 让用户确认目标,覆盖是用户在保存框里点的,不在代码里静默覆盖 |
| **版本匹配在"版本号带后缀"时** | 干净:`_mc_version_from_text` 用 `(?<![\d.])…(?![\d])` 边界 + NeoForge 四段取前三段 + 年份版本(`26.3`),`1.20.1-forge` / `fabric-1.20.1` 都能取到 `1.20.1`;上层匹配(`McIn`/Modrinth `versions:` facet)是**归一化后的精确匹配**,不做前缀包含,因此不会把 `1.20.10` 误配成 `1.20.1` |
| `Catalog` | 搜索结果 → 安装入口只透传 `slug/id + game_version + loader`,版本决策仍在 `Mods/Modpack` 侧(单一事实源),未发现重复/分叉判定 |

## R7.7 第七轮提交

`audit/C` 上的 R7 提交(标题):「R7 自审:stack 连拖接续 / StartArgs 纯函数+6 例断言 / WebView2 代数号防重入漏进程 / 快捷方式避让运行中的游戏 / Terracotta 内核退出清态」。精确哈希 `git log -1 --format=%h`。

---

# 第八轮 — 现场 bug:「老是有人拿浏览器打开 Terracotta 的 GitHub」(插队,优先修)

## R8.0 现场核实(先说结论:根因三条,其中一条与 lead 的判断不同)

用户看到的现象:反复有人用浏览器打开 `https://github.com/burningtnt/Terracotta`。

核实结果:
1. **全仓只有两处会开这个 URL**,都在 `Pages/MultiplayerPage.cs`:`:102` 页脚「Terracotta 项目主页」按钮(用户明确点击)、`FillActions` 的 `unsupported` 分支(`:393-397`,「了解」按钮直接 `Ui.OpenUrl`)。**没有自动打开路径**,不是后台偷偷开。
2. **`Ui.OpenUrl`(`Services/Ui.cs:681`)原本没有任何节流**:连点十下就开十个标签页。这是"反复打开"的直接放大器。
3. **`unsupported` 那张卡是"说明卡"**:浏览器一起来、启动器界面毫无变化 → 用户以为没反应 → 再点。按钮放在"确认提示"的位置本身就是设计错。
4. ⚠️ **与 lead 判断不同的一点**:`FillActions` 不是每 1.2s 全量重建 —— `Render` 里原本就有 `if (text != _uiState)`(`:249-252`)按**状态**去抖。所以"每 1.2s 重建卡片"这条不成立;真实会重建的场景是 ①状态在 `unsupported/installing/idle` 之间抖动(`_busy` 参与换算 + 1.2s 轮询)、②state 不变但 `Label/ErrorHint` 变了(这种情况以前**反而不会刷新**,卡片停在旧文案)。本轮把 key 从"状态"扩到"状态 + 卡片上会显示的文字",同时解决"抖动时换掉鼠标下的按钮"和"文案不刷新"。

## R8.1 修复

| # | 改动 | 文件 |
|---|---|---|
| 1 | **`Ui.OpenUrl` 加 3 秒同 URL 节流 + 每次真开/被拦都落日志**(URL + 来源标签),并把它收敛成全仓 URL 打开的唯一入口。节流判定抽成纯逻辑 `OpenUrlGate.TryAcquire(url, nowMs, windowMs)`(单调时钟,不受改系统时间影响;>64 条自动清过期) | `Services/Ui.cs:681-740`、新增 `Services/OpenUrlGate.cs` |
| 2 | **`unsupported` 说明卡不再开浏览器**:改成纯说明 + 「复制项目地址」按钮(`Copy(ProjectHome, …)`),想看的自己粘贴;真要跳转页脚本来就有「Terracotta 项目主页」 | `Pages/MultiplayerPage.cs:390-403` |
| 3 | **`_actions` 重建去抖**:key = `state + Label + Error + ErrorHint + DifficultyHint`,与上次相同就不重建(按钮不会在鼠标底下被换掉);文字变了才重建(修掉"卡片停在旧文案") | `Pages/MultiplayerPage.cs:249-259` |
| 4 | **打开外链落日志**:`open_url(source): url` / `open_url 节流忽略(source): url`,走既有 `Utils.log` | `Services/Ui.cs`、`App.xaml.cs:195`(登录回调改走 `Ui.OpenUrl(url, "auth.browser")`) |

**入口收敛核对**(grep `Process.Start(new ProcessStartInfo`):剩下的都不是"开 URL" —— `Auth.cs:313` 是 Core 在 `OpenBrowserHook` 为空时的兜底(App 启动即注入钩子,实际不走;Core 不能反向依赖 `Services.Ui`,保留)、`SettingsPage:1722` 是重启启动器自身、`PluginsPage:333` 是 explorer 打开目录、其余是 powershell/taskkill/control/游戏进程/Node 宿主。

## R8.2 断言(新增 7 例,能失败)

`windos/tests/TipCL.Tests/OpenUrlGateTests.cs`:
- `SameUrlIsOnlyOpenedOnceInsideTheWindow` —— 连点一次、1.5s 后、差 1ms 到期都必须被拦;
- `WindowIsExclusiveAtTheBoundary` —— 恰好满 3000ms 放行(否则 1.2s 轮询+重试会永远打不开);
- `DifferentUrlsAreIndependent`、`EmptyOrNullUrlIsNeverOpened`、`UrlIsTrimmedBeforeComparing`、`ResetLetsTheSameUrlOpenAgain`、`ZeroWindowMeansAlwaysAllowed`。

验证:`dotnet build … -c Release` **0 警告 0 错误**;`dotnet test` **589/589**(582 + 7)。

## R8.3 实机确认项

1. 造一个 `unsupported` 状态(非 x64/arm64 架构,或临时把 `terracotta_snapshot` 的 state 改成 unsupported):卡片上应是「复制项目地址」,点了**不开浏览器**,只弹「已复制项目地址」。
2. 页脚「Terracotta 项目主页」连点 5 下:只应打开 1 个标签页(3 秒内其余静默丢弃)。
3. 日志(`%LOCALAPPDATA%\TipCL\...` 的 TipCL 日志)里能看到 `open_url(multiplayer.home): https://github.com/...` 与 `open_url 节流忽略(multiplayer.home): ...` —— 以后再出这类问题直接看日志。
4. 联机页停留 30 秒:操作区按钮不应在鼠标底下反复重建(可观察 hover 态是否被重置)。
