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
| C-02 | P1 | `windos/src/TipCL/Core/Launcher/Shortcut.cs:79-105`(`LaunchArgs` 产出 `-i <实例> launch <版本>`)、`windos/src/TipCL/App.xaml.cs:48-70`、`windos/src/TipCL/MainWindow.xaml.cs:379`、`:572` | `Shortcut.LaunchArgs` 生成的 `.lnk` 参数在 Windows 侧**没有任何消费者**:`App.OnStartup` 只认 `--smoke/--shot/--nav-dump/--ime-check/--i18n-check/--consent-check/--ai-ui-selftest`,grep `"-i"` 全仓只有 Shortcut.cs 一处。双击快捷方式只把启动器打开,不会启动对应版本/实例(Linux 侧 `src/TipCL.App/Session/CommandLine.cs:84` 是真的消费了,PROGRESS.md:228 也把这个记为 Windows 缺陷) | `App`:新增 `ParseStartArgs`(`-i <inst> launch <ver>`,越界安全)+ `TakeStartArgs(out …)` 一次性取走;在真实 UI 分支 `new MainWindow().Show()` 之前解析。`MainWindow.ConnectAsync`:后端连通、`ReloadCurrentAsync()` 之后调 `StartFromShortcutAsync()`,如果是启动页就 `LaunchPage.LaunchNowAsync(instance, version)`(复用 AI 页那条现成入口);`Smoke.Active` 时跳过 | build 0 错误、547/547;参数形状与 `Shortcut.LaunchArgs` 逐字对齐(`-i`,`inst`,`launch`,`ver`),不与 Linux 侧 CLI 语义冲突;未改 `LaunchArgs` 输出,`.lnk` 生成逻辑零改动 | 已修 |
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
