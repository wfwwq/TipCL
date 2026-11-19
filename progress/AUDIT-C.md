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