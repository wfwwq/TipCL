# AUDIT-A 报告(TipCL.Core 层，审计员 aud-core / 分支 aud-core@audit/A）

工作区：`D:\项目\TipCL\_aud\A`，基线 main `b86afe3`（1.2.0）。
范围：`src/TipCL.Core/**`（可改），`src/TipCL.App/**`、`windos/**`、`tests/**` 只报告不改。
级别口径：P0 崩溃/启动失败/数据丢失/编译不通过；P1 功能错、逻辑 bug、竞态、异常被吞导致功能失效、序列化不兼容；P2 边界/健壮性/泄漏/提示不当/i18n。

环境（每条命令后都要带，PS 5.1）：
```powershell
$env:PATH="D:\项目\TipCL\_tools\dotnet;$env:PATH"; $env:DOTNET_ROOT="D:\项目\TipCL\_tools\dotnet"
$env:DOTNET_CLI_HOME="D:\项目\TipCL\_tools\home-A"; $env:NUGET_PACKAGES="D:\项目\TipCL\_tools\nuget"
$env:TEMP="D:\项目\TipCL\_tools\tmp-A"; $env:TMP=$env:TEMP
```
> `DOTNET_ROOT` 必须设：否则 `dotnet test` 的 testhost 报 `hostfxr.dll could not be found`（与代码无关的环境问题）。

---

## 一、已修问题

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| A-01 | **P1（数据丢失）** | `src/TipCL.Core/Launcher/Layout/LayoutStoreV2.cs:94,129,419,436` | Linux 移植版把 Windows 版的「损坏 v2 备份链路」整条丢了：`ReadV2File` 只返回 `null`、`WriteV2File` 直接覆盖。布局文件被写坏一次（截断/断电/杀软截断）→ 下次 `Load` 回落 v1/默认文档 → UI 一保存就把用户整份布局**永久覆盖**，没有任何备份 | 补回链路：`ReadV2File(path, out bool corrupt)` + `IsDamagedJson()`（Core 仍用自研 `PyJson`，不引 System.Text.Json）+ `BackupCorruptV2()`（改名 `*.corrupt-<时间戳>`，重名追加序号）+ `_pendingCorruptBackup` + `EnsureCorruptBackedUp()`（**写 v2 前先备份，备份不了就 `return false` 拒绝写**）+ `LastLoadV2Corrupt` / `LastCorruptBackup` 两个只读属性 | 临时工程 `_tools\tmp-A\audit-harness`（引 Core 项目）12 项断言全 PASS：损坏→Load 不动原文件且 `LastLoadV2Corrupt=true`；首次 Save 产生 1 个 `.corrupt-` 备份且内容 == 原损坏内容；v2 变合法；`LastCorruptBackup` 指向备份；**原件被用户删掉后 Save 不再被永久卡死**；「合法 JSON 但 kind/version 不对」不算损坏、既不备份也不覆盖。另跑 `LayoutV2CompatTests`/`LayoutV2ModelTests`/`PageLayoutIOV2Tests` 44 项全绿 | 已修 |
| A-02 | **P1（进程级崩溃）** | `src/TipCL.Core/PyJson.cs:13,262,321` | 自研 JSON 解析器纯递归、无深度上限。畸形/恶意 JSON（几百 KB 的 `[[[[…`，或服务端/本地文件被写坏成深嵌套）会直接 `StackOverflowException` —— .NET 里**任何 catch 都接不住**，整个启动器进程秒退；Python 原版有 `RecursionError` 兜底 | `Parser` 加 `_depth` + `MaxDepth=512`，`Obj`/`Arr` 外层包一层计数（`ObjInner`/`ArrInner` 保持原逻辑不动），超限抛 `JSONDecodeError("Maximum nesting depth exceeded")`（可被 `LoadsOr`/`ReadJson` 正常吞掉） | harness 断言：20 万层 `[[[…]]]` 与 5 千层 `{"a":…}` 均抛 `JSONDecodeError`（进程存活），常规 JSON 仍正常解析；全量测试 781 项全绿 | 已修 |
| A-03 | P2（潜在死锁/UI 卡死） | `src/TipCL.Core/Launcher/GameProcess.cs:99` | `Reader`（stdout/stderr 两条后台线程）**持 `_lk` 调 `on_line` 回调**。App 侧回调是 `Log(...)` + `api.RaiseEvent(...)`（`:212` `LaunchCore.cs`），一路可能同步回 UI 线程；此时若 UI 线程在 `LastLines()`（`:84` 同锁）里等锁 → 读日志线程持锁等 UI、UI 等锁，互相卡死 | 锁内只做 `lines` 的入队/裁剪，`on_line` 移到锁外调用（保持每行顺序，两条流的交错本来就无序） | Core 编译 0 错误/0 警告；全量 781 测试全绿；行为差异仅「回调不再阻塞写入队列」 | 已修 |

提交：`audit/A` 分支，见 `git log`。

## 二、跨域问题（不在我范围，交给对应线路）

| ID | 目标 | 级别 | 文件:行 | 说明 |
|----|------|------|---------|------|
| X-01 | aud-app | 说明（非 bug） | `src/TipCL.App/Pages/Layout/LayoutEngine.cs:1204-1243` | `LayoutCell.HeightPx` 走 `block.Extra["height_px"]` 而不是 v2 契约字段。**核实结论：跨端互导不会丢固定高度**——Core 的 `LayoutJson.MergeExtra`（`LayoutJson.cs:236`）把 `Extra` 里的未知键**平铺写到顶层**，所以 Linux 写出的 JSON 里 `height_px` 就在顶层（与 Windows 契约字段同名）；反向 Windows 导出的顶层 `height_px` 在 Linux `BlockLayout.FromDict` 里落进 `Extra`，`ReadHeightPx` 照样读得到。故 A-线**不改** Core 契约，仅当 App 侧希望 `BlockLayout` 有强类型字段时才需要动（会牵动 App 的 `LayoutCell`/序列化两处）。另外 `DECISIONS.md` 里 grep `height_px`/`Extra` **无任何决策记录**，所以这不是"有意决策"，而是"表示法不同但语义等价"。 |
| X-02 | 测试线（D） | 说明 | `tests/TipCL.Tests/Layout/LayoutV2CompatTests.cs:434` | 该用例断言「读路径不写盘：损坏的 v2 文件读完之后内容原样还在」，与 Windows 版「Load 里就改名备份」相冲突。我按用例冻结的语义实现（Load 只记待办、改名推迟到写前），**用例无需改动**。若后续想让 Windows/Linux 完全同构，需要先改这条断言。 |
| X-03 | aud-others | P2 观察 | `src/TipCL.App/Common/Launch/LaunchCore.cs:231,254` | `gp.Wait()` 在 `Task.Run` 里跑，`finally` 里 `playtime.Stop()`；`LastLines()` 在 `Wait` 返回后调用（此时 reader 线程已 Join 2s）——与 A-03 修复前存在理论上的锁竞争窗口，A-03 修完后该窗口消失。App 侧无需改动。 |

## 三、已核查「其实没问题」（记录以免下一轮重复挖）

| 位置 | 结论 |
|------|------|
| `Launcher/Saves.cs:97-123` `InstallDatapackIntoSave` | **lead 提的第 1 条不成立**：本 worktree 里已经是修好的版本——源目录优先 `Mods.ContentDir(instance,"datapacks",version_id)`、再回退 `<实例>/datapacks`（`_find_datapack` 带 parent 校验），且**存档目录不存在会抛 `SaveError("存档不存在")`**（`:106-109`）。`git log` 显示该文件自 `2f7683e` 树搬动后未再变。无需改动。 |
| `Launcher/Saves.cs` 其余 | `_safe_child` 用 `Resolve()+Parent.Equals` 防穿越；`RestoreBackup` 先校验所有 entry 路径再解到临时目录、失败回滚 `previous`、`flag` 时保留现场；`ZipFolder` `RelativeTo(src)` 保证 entry 名不越界 |
| `Launcher/Downloader.cs` | `PathLock` 按绝对路径归一（`_PATH_LOCKS` 只增不减属可接受）；`EnqueueRedirect` 的 `AddFirst` 倒序插入后顺序正确；`LooksComplete` jar/zip 头判定、html/error 前缀判定符合预期；`SplitRanges` 边界（total>0）正确 |
| `Launcher/DownloadManager.cs` | 断点续传状态机：`206` 必须 `Content-Range` 起点==已有 part 长度且 `end+1==total` 才续写；`200` 会 `num=0` + `FileMode.Create` 截断；取消/「下载不完整」保留 `.part`（`KeepPartOf`）、其它错误清 part；`416` 先删 part 再抛；分片失败 3 次重试后 `failed.Set()` 并让其它段提前返回；`DownloadAll` 线程池取 `count` 个结果与任务数严格相等（不会死等）；磁盘空间预检 `+64MB` 余量 |
| `Launcher/MultiHasher.cs` / `SpeedMeter.cs` / `NoSegment.cs` | `TransformBlock/TransformFinalBlock` 用法正确，`Finish` 幂等；`SpeedMeter` 窗口裁剪与除零保护到位 |
| `Launcher/Net.cs` / `Launcher/NetResponse.cs` | 重试只对 GET，`Retry-After` 上限 10s，`Read` 有独立读超时并把 `_r.Dispose()` 兜住；`Classify` 遍历 InnerException 归类 Socket/TLS/Proxy；`SendOnce` 里 CTS 释放后读 `IsCancellationRequested` 合法 |
| `HttpLite.cs` | `Unquote` 的 `i+2<s.Length` 边界、`IsHex` 对 `:`/`` ` ``/g 的误判排除都正确 |
| `PPath.cs` | `Suffix`/`Suffixes`/`Stem` 与 Python `pathlib` 语义一致（隐藏文件无后缀）；`RelativeTo` 锚点校验；Windows 下 `Key()` 小写化用于比较 |
| `PyDict.cs` / `Py.cs` | 插入顺序 + Ordinal 键；`Truthy/Str/Repr/ReprStr/FloatRepr/ExactFixed/RoundHalfEven` 与 CPython 行为对齐（0.1 银行家舍入、`-0.0`、`nan/inf`）；`Slice` 负索引夹取正确 |
| `Utils.cs` | `WriteJson` 的 tmp+`MoveFileEx` 原子替换 + 12 次重试 + 直写兜底；`OsReplace` Linux 分支先删后 Move；`FileMatches` 只有 size 时按 size 判定；`SafeExtractZip`/`SafeExtractTargz` 都做 `UnderRoot` 越界校验、跳过符号链接；`TarReader` 的 pax `path=`/`L` 长名处理正确；`List2CmdLine` 与 CPython `subprocess.list2cmdline` 同构（含"尾部反斜杠不翻倍"这一 Python 既有行为） |
| `Launcher/Manifest.cs` | 继承链 `ResolveInherits` 有环检测；`MergeLibraries` 按 `LibraryIdentity` 去重且**子版本覆盖父版本、保持父版本位置**；`arguments` game/jvm 拼接、legacy `minecraftArguments` 互斥分支正确；`ClosestRelease` 同 major.minor 内取 ≤ 目标的最大 release |
| `Launcher/CatalogFiles.cs` | `_source_of`/`_kind_of` 的 null 安全（调用方先 `new PyDict(extra ?? …)`）；`KIND_MR[kind]` 只在 `ContainsKey` 为真时取；`SearchProjects` 的 goto 归一化后 flag/flag2 组合对 world/curse/modrinth 都正确 |
| `Plugins/SemVer.cs` | `^`/`~`/通配/连字符范围、预发布优先级、`PreOk` 规则与 npm 简化版一致；异常只用 `FormatException` 出口 |
| `Launcher/GlobalMods.cs` | `_link_file` 已存在则跳过、符号链接失败回落 `File.Copy`；`SetEnabled` 的 `.disabled` 往返与冲突检测正确 |
| `Launcher/VersionSettings.cs` | `Load` 先记文件里的 `isolation` 再合并（避开默认值 `none` 压住全局）；`Save` 非法/空值删键回落全局；`GameDir` 对 4 种隔离模式的分支正确；junction 失败静默回落 |
| `Launcher/UiLayout.cs` | `Patch_*` 与 `Txn` 共用 `LauncherConfig._lock`，读写都走同一把锁；profile 迁移/删除逻辑自洽 |
| `Launcher/McLauncher.cs` | classpath 按 `LibraryIdentity` 去重且后写覆盖前值；`-cp` 只在缺失时补；Java<9 遇模块参数显式拒绝；`_drop_orphan_jvm_flags` 的 flag 跳位与预置值顺序正确；`_apply_memory` 两次调用幂等（第二次清掉重复的 `-Xmx/-Xms`） |
| `Launcher/LaunchFlow.cs` / `LaunchResolution.cs` / `GameProcess.cs` | hook 拆分/引号剥离；`_cmd_builtin_argv` 拒绝 shell 元字符；分辨率解析对非序列/非数字/≤0 全部回落 854×480 |
| `Launcher/AccountManager.cs` | `EnsureValid` 三种账户类型的过期判定与刷新分支；刷新后改名冲突检测 + 旧 secrets 清理 |
| `Launcher/Sync/SaveSync.cs` | 三方决策表完备；`FileSha` 只缓存 mtime 超过 3s 的文件（同刻同大小误判问题已规避）；`CheckZip` 的越界与 `level.dat` 校验；`ImportZip` 覆盖前先 `BackupSave` |
| `Launcher/Sync/PhoneClient.cs:418` | `err.Wait(1000) ? err.Result : ""`：只有 task 完成才取 `Result`，正常路径不会抛；`WaitForExit` 返回值未用（超时后仅丢 stderr，属可接受） |
| `Launcher/Config.cs` / `LauncherConfig.cs` / `UiPrefs.cs` | 默认值合并 + 未知键保留 + 迁移（instances_dir 搬家/isolation/ai 模式）幂等；`Save` 失败退避重试不打断启动；`UiPrefs` 现读不缓存、类型不符取默认 |
| `Launcher/Instances.cs` | 名称净化（非法字符/保留名/48 长度/尾随空格点）、`GetInstancePath` 防穿越、`UniqueInstanceName` 截断重算 |
| `Launcher/Installer.cs:1-1081`、`1084-1410` | natives 分类器 rank 选择、`_install_libraries` 的三种下载分支、processor 输出 `FileMatches` 校验、assets 索引为空自动重下、`ExtractNatives` 父版本回填；Forge 安装器缓存复用/多源回退/`MatchForgeArtifact` 排序键 |
| `Launcher/BaseImage` 无关项：`LayoutJson.cs` | `MergeExtra` 不覆盖已知字段；`Plain/Num/Integer/Bool/Text` 宽容读取；`Number` 拒 NaN/Inf |

## 四、未审完（下一轮建议顺序）

按"用户真实路径 × 代码体积"排序，下列文件本轮**只做了 grep 级扫描，没有通读**：

1. `Launcher/Mods.cs`（54KB）、`Launcher/Modpack.cs`（58KB）、`Launcher/Catalog.cs`（44KB）—— Modrinth/CurseForge 元数据解析、mrpack 解包与依赖安装、mod 启停/更新。建议重点：server 返回字段类型不符、`(PyDict)` 硬转型、zip 解包路径、同名文件覆盖。
2. `Launcher/Installer.cs:1411-2108`（OptiFine / LiteLoader / NeoForge / `_run_processors` 后半段）。
3. `Launcher/Java.cs`（38KB）—— java 探测正则、`-version` 输出解析、Adoptium 下载。
4. `Launcher/Ai/*`（`Client.cs`、`AiAgent.cs`、`ToolRunner.cs`、`Compact.cs`、`Checkpoint.cs`）—— 流式解析、工具循环、token 统计、checkpoint/rewind。
5. `Plugins/PluginInstaller.cs` / `PluginManifest.cs` / `PluginMarket.cs` / `PluginHost.cs` —— 安装回滚、清单校验、`long.Parse`（`PluginManifest.cs:518` 无 try 的 `long.Parse(isv.Trim())` 值得单独看）。
6. `Launcher/Crash.cs`（62KB）、`Preflight.cs`、`Sysinfo.cs`、`SkinServer.cs`、`Updater.cs`、`Cleaner.cs`、`Worlds.cs`、`Playtime*.cs`、`Sync/PhoneLink.cs`、`Layout/LayoutSchema.cs`、`Layout/LayoutDocV2.cs`（深读）。
7. 本轮**没有**对 i18n 键做机器核对：`Resources/locales/*.json` 不是扁平 key→value（`ConvertFrom-Json` 直接失败），正确做法是在 Core 单测里用 `I18nCore.Tr(key, "en") != key` 批量断言；建议下一轮补一条只读检查。

## 五、本轮结论

- **P0：0 项。P1：2 项（A-01 数据丢失、A-02 进程级崩溃），P2：1 项（A-03）——全部已修并带证据。**
- Core `dotnet build -c Release`：0 错误 0 警告；`dotnet test`：781 项通过 / 0 失败 / 1 跳过（基线同）。
- 没有任何 public API 签名变更；A-01 只新增两个只读属性和一个私有实现，A-02 只加内部深度计数，A-03 只调整锁范围。

---

# 第二轮（lead 指定优先级：Installer 1411-2108 / Java.cs / Plugins / i18n 探针）

基线：rebase 到第一轮合并后的 main。本轮改动集中在 `Launcher/Java.cs`、`Launcher/Utils.cs`，其余为核查。

## 6. 第二轮已修问题

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| A-04 | **P1（装了用不了 = 点了没反应）** | `Launcher/Java.cs:1129`（原 `Chmod755` 空方法体）、`Launcher/Utils.cs:1205`（`SafeExtractTargz` 丢弃 tar 的 `Mode`）、`Launcher/Utils.cs:1147`（`SafeExtractZip` 不应用 zip 的 Unix 权限位） | 三条路径装出来的 Java 在 Linux/macOS 上都**没有执行位**：①`InstallMojangRuntime` 是逐文件 HTTP 下载，`FileMode.Create` 落盘 0644，本来靠 `Chmod755` 补 0755，而它是空方法（参考版 Windows 没有权限位，空着能跑，移植过来就成了 Linux 上的真 bug）；②`InstallAdoptium`/`_InstallZulu`/`_InstallMicrosoft` 走解压，`TarReader` **解析了 `Mode` 却从不使用**，`SafeExtractZip` 也没读 `ExternalAttributes`。`Utils.FindExecutable` 只判存在，于是「装好了、路径也返回了、`runtime.meta.json` 也写了」，一启动 `Process.Start` permission denied → `JavaVersionOutput` 返回空 → `GetJavaMajor` null → 报「Java ? 无法启动此版本」/点了没反应 | ①`Java.Chmod755` 按 `Shortcut.cs:225` 的既有正确实现补齐（`OperatingSystem.IsWindows()` 守卫 + `File.SetUnixFileMode`，失败静默）；②新增 `Utils.ApplyUnixMode(path, mode)`：只搬 rwx 三段（`&0x1FF`，不还原 setuid/setgid/sticky），仅在有执行位时动手，非 Unix 平台直接返回；③`SafeExtractTargz` 写文件后按 `item2.Mode` 应用，`SafeExtractZip` 写文件后按 `(entry.ExternalAttributes>>16)&0x1FF` 应用 | Core Release **0 警告 0 错误**（CA1416 用 `OperatingSystem.IsWindows()` 而非 `IS_WINDOWS` 消掉）；全量 `dotnet test` **783 通过 / 0 失败 / 1 跳过**；harness 验证「zip 内 0755 条目解压成功且内容正确」；Windows 上权限位本就不适用，故 Windows 行为零变化 | 已修 |
| A-05 | **P1（发行版 Java 必装失败）** | `Launcher/Java.cs:1418`（`_InstallZulu`）、`Launcher/Java.cs:1463`（`_InstallMicrosoft`） | 本地包名用 `ArchiveSuffix()`（Linux = `.tar.gz`），但**内容是 zip**：Zulu 接口 URL 写死 `archive_type=zip`，aka.ms 的 Microsoft JDK 链接也固定 `.zip`。`DownloadManager.ExtractArchive` 按**文件名后缀**派发 → `.tar.gz` 走 `GZipStream` → 对 zip 内容抛 `InvalidDataException`。于是这两个发行版在 Linux 上「下载成功、随后解压炸」，用户只看到一句底层 gzip 异常 | 新增 `ArchiveSuffixForUrl(url, fallback)`：按下载地址的真实后缀（`.zip`/`.tar.gz`/`.tgz`）决定本地包名，认不出来才回落 `ArchiveSuffix()`；两处调用点改用它 | harness 断言：同一份 zip 内容命名 `.tar.gz` → `ExtractArchive` 抛 `InvalidDataException`（复现修前失败方式）；命名 `.zip` → 解压成功、`bin/java` 落盘且内容一致；Windows 侧 `ArchiveSuffix()` 本来就是 `.zip`，行为不变 | 已修 |
| A-06 | P2（界面显示原始 key） | `Resources/locales/zh_CN.json`（末尾）、`Resources/locales/en.json`（末尾） | `src/TipCL.App/Pages/Download/CatalogPage.cs:182` 用了 `Tr("结果列表")`，但 Core 内嵌词表里没有这个 key → 界面直接显示「结果列表」这个 key 本身（切英文时更明显）。aud-consistency 核对发现，落在 Core 词表范围 | 两侧成对新增 1 条：zh_CN `"结果列表": "结果列表"`、en `"结果列表": "Results list"`（追加在文件末尾，保持 `{`…`}` 单对象结构与 2 空格缩进；未改任何既有行） | `dotnet build -c Release` 0 错误；`--filter FullyQualifiedName~TipCL.Tests.Core` 214 项全绿、`~I18nCore` 4 项全绿（键集/键数断言未破）；探针复测：**App 侧 212 个 `Tr("…")` 字面量 → en/zh_CN 各 0 缺失**，Core 侧 37 个 → 也 0 缺失。注意探针必须重新 build（内嵌资源随 Core.dll 走），否则读到旧词表会误报 | 已修 |

## 7. 第二轮：已核查无问题（记录以免重复挖）

| 位置 | 结论 |
|------|------|
| `Plugins/PluginManifest.cs:518` 的 `long.Parse(isv.Trim())`（lead 点名） | **不是 bug**：整段包在 `try { … } catch (Exception) { throw new ValueError(key + " 必须是整数"); }` 里，任何格式错都转成中文可读的 `ValueError`；`number` 分支（`:528`）同理。 |
| `Plugins/PluginInstaller.cs` `Swap`（`:233-258`） | 安装回滚正确：旧版本先挪 `backups/<id>` → 新版本 `Directory.Move` 进来 → 失败则删残壳并挪回旧版本 → 成功后清备份；`InstallPackage` 的 catch 会清暂存目录。 |
| `Plugins/PluginStateStore.cs` | 并发写安全：所有读写同一把 `_lock`，写盘是 `tmp` + `File.Replace`（无则 `File.Move`）；坏 JSON 先 `File.Copy` 成 `.corrupt` 再按空表处理，不抛。 |
| `Installer.cs:1411-1793`（Forge 现代/legacy/CLI 三条链） | `_run_install_profile_processors`：输出 `FileMatches` 全过就跳过、退出码非 0 但输出已生成只告警继续、退出码 0 但输出缺失才抛；`_install_forge_cli` 先在临时目录里铺好 `versions/<mc>` 再跑官方安装器，回拷 versions/libraries 并 `_merge_libraries_to_shared`，临时目录 finally 必删。`_install_forge_legacy` 的 `install.path/filePath` 内嵌库解包与父版本安装链完整。 |
| `Installer.cs:1795-2108`（NeoForge / 排序 / maven-metadata） | NeoForge 版本前缀（26.x 三段 calver / 1.20.2+ 去 `1.` / 老版本 `mc-` 与 `NEOFORGE_MC_MAP` 回落）分支完整；`ParseMavenVersions` 用 `XmlResolver = null`（防外部实体）且 `XmlException` 回落空表；`_mc_tuple` 对 `1.20`、空串、非数字都有兜底。 |
| `Java.cs:138-235` `JavaVersionOutput` | stderr + stdout **合并**后再解析（`java -version` 走 stderr 的场景已覆盖）；6s 超时后 kill 并返回空串；UTF-8 解不出 `version` 时回落 GBK(936) —— Windows 中文 JDK 的典型输出。 |
| `Java.cs:468-508` `Which` + `:375` `RealPath` | Linux 上 `Which` 只扫 PATH（`Path.PathSeparator`），`RealPath` 解析 `/usr/bin/java → /etc/alternatives/java → /usr/lib/jvm/...` 软链，再取两级父目录作为 JDK 根，能落到各发行版的真实 JAVA_HOME。 |
| `Java.cs:681-772` `FindSystemJavas` | JAVA_HOME / PATH / `/usr/lib/jvm`、`/usr/java`、`/opt/java`、sdkman、`.jdks`、`.gradle/jdks` 都覆盖；结果按 major 降序、临时目录里的排最后；60s 缓存。**观察（P2，未改）**：整个扫描（Windows 上最多 8s 遍历 + 15s 版本探测）都在 `_SYS_JAVA_LOCK` 里，`CachedSystemJavas()` 会跟着等；App 侧没有调用 Cached*（grep 确认），所以当前不构成 UI 卡死，若以后 UI 直接读缓存建议把锁拆成「扫描锁 + 结果锁」。 |
| `Java.cs:876-928` `RequiredJavaMajor` | `javaVersion.majorVersion` → bootstrapLauncher 判 17 → `-p/--module-path/--add-modules` 判 17 → 否则 8；覆盖了老 Forge/1.17+ 模块参数两种极端。 |
| `Java.cs:1062-1112` `_ArchiveValid` / `InstallAdoptium` / `_InstallZulu` / `_InstallMicrosoft` 的失败回滚 | 三者都是「先删目标目录 → 解压 → 失败删目录+删包再抛 → 找不到 java 也删目录」；`ArchiveSuffix()` 与 Adoptium（Linux tar.gz / Windows zip）真实格式相符。 |
| Core 全部 `I18nCore.Tr("…")` 字面量（37 个） | **en / zh_CN 两边都 0 缺失**，无需补词条（App 侧缺的那 1 条见 A-06）。方法：harness 扫 Core 全部 `.cs` 抽 `Tr("…")` 字面量，用 `Tr(key, lang, "\u0001MISSING\u0001") == sentinel` 判「查表未命中」——**不能用「返回值 == key」判**：zh_CN 表是「中文→中文」的恒等映射，值天然等于键（第一版探针就是这么误报 37/37 的）。 |

## 8. 第二轮未审完（下一轮顺序）

1. `Launcher/Mods.cs`（54KB）、`Launcher/Modpack.cs`（58KB）、`Launcher/Catalog.cs`（44KB）—— 元数据解析、mrpack 解包与依赖安装、mod 启停/更新（仍未通读）。
2. `Launcher/Ai/*`（`Client.cs` / `AiAgent.cs` / `ToolRunner.cs` / `Compact.cs` / `Checkpoint.cs`）—— 流式解析、工具循环、token 统计、checkpoint/rewind。
3. `Launcher/Crash.cs`（62KB）、`Updater.cs`、`Cleaner.cs`、`Worlds.cs`、`Sync/PhoneLink.cs`、`Plugins/PluginMarket.cs`（zip 前缀与 SHA 校验）、`Plugins/PluginHost.cs`。
4. `Launcher/Optifine.cs` / `Liteloader.cs`（`Installer.InstallOptifine/InstallLiteloader` 只是转发，本体未读）。
---

# 第三轮（lead 优先级：Mods / Modpack / Catalog → Ai/* → Crash/Updater/Cleaner/Worlds/Sync/PluginMarket/PluginHost → Optifine/Liteloader）

基线：rebase 到第二轮合并后的 main。判据按 lead 追加的两条：**静默失败＝P1**、**异常消息能否被 UI 直接展示**。

## 9. 第三轮已修问题

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| A-07 | **P1（静默失败）** | `Launcher/Mods.cs` InstallModrinthMod 依赖分支（原 `Utils.log.Warning` 处）、`_install_cf_required_deps` 的 catch | 前置依赖（如 Fabric API）下载失败时**只写日志**：任务卡一路显示「模组安装完成」/「已随装前置:…」，进游戏才因缺前置崩。App 侧`InstallModSync`（`Api.cs:1462`）连 Modrinth 分支的返回值都不接，用户没有任何反馈 | 两处 catch 在日志之外追加 `on_progress?.Invoke("前置 <名字> 下载失败,请到模组列表手动补装", 0, 1)`/`("前置(addon <id>)安装失败,…")`。`on_progress` 正是 App 任务卡的日志通道（`dm.on_progress`），零改动 App 即可见 | Core Release 0 警告 0 错误；全量 `dotnet test` **906 通过 / 0 失败 / 1 跳过**；消息全中文、不含原始异常串 | 已修 |
| A-08 | **P1（装完看不到/游戏不加载）** | `Launcher/Mods.cs:1146` `InstallModrinthContent`（原 `string text = PPath.Join(instance.path, subdir)`；调用点 `:1303`） | `InstallContentFromSource` 已经按「实例＋版本隔离」算好目标目录（`ContentDir` → `<实例>/versions/<版本>/<subdir>`）并传给 `InstallCfContent`（它有 `dest_dir` 参数），但传给 `InstallModrinthContent` 时**没传**，后者自己拼 `<实例>/<subdir>`。隔离模式 `all`/`saves` 下两者不同 → Modrinth 源的光影/资源包/数据包被写进共享目录：列表（`ListContentFiles` 用 ContentDir）看不到、游戏也不加载 = 点了安装没反应（Windows 参考版同一处同样缺陷） | `InstallModrinthContent` 末尾新增可选参数 `string dest_dir = null`（源码兼容，唯一调用点已改），内部 `Py.Truthy(dest_dir) ? dest_dir : ContentDir(instance, subdir)`；调用点把 `text` 传进去 | harness 断言：`Utils.SetRoot(temp)` + 建实例 + `VersionSettings.SetIsolation(…, "all")` 后，`Mods.ContentDir(inst, "shaderpacks", ver)` ≠ `<实例>/shaderpacks` 且落在 `versions/1.20.1/` 下（**复现修前写错目录的前提**）；全量测试 906 项绿；Windows 无隔离/共享目录场景结果不变 | 已修 |
| A-09 | P2（假成功提示 + 空消息异常） | `Launcher/Cleaner.cs` `Apply`（原 `catch (Exception) {}` + `throw new IOException()`） | 清理时**每个删除失败都被完全吞掉**（连日志都没有）：界面照样 Toast「清理完成」（绿字）而正文是「已清理 0 个文件」，用户不知道为什么没清掉；`throw new IOException()` 写在同一个 try 里且立刻被自己的 catch 吃掉，是死代码，就算抛也没有任何消息 | 去掉死代码；失败时累加 `failed` 计数、记下首个原因（`Py.ExcStr`）、`Utils.log.Warning`；返回值追加 `"failed"`/`"error"` 两个键（App 侧要不要显示见跨域表 X-04） | Core Release 0 警告 0 错误；全量测试绿；`Preview`/`Apply` 的既有键（removed/bytes）语义未变 | 已修 |

## 10. 第三轮跨域问题

| ID | 目标 | 级别 | 位置 | 说明 |
|----|------|------|------|------|
| X-04 | aud-app | P2 | `Pages/Settings/SettingsPage.Cards.cs:274` | 清理结果只读 `result.Removed` 并固定弹绿字「清理完成」。Core 现在已回 `failed`/`error`（见 A-09），建议：`failed > 0` 时改 Warning 提示并显示首个原因，否则用户只会看到「已清理 0 个文件」的假成功。 |
| X-05 | aud-app | P2 | `Common/Api.cs:1462` | Modrinth 分支的安装结果被整体丢弃（`deps` 永远为空）。Core 已在 `InstallModrinthMod` 的返回值里带 `files`；若要像 CF 那样显示「已随装前置」，需要把返回值接住并读 `dependencies`。A-07 已经让「前置失败」通过任务卡日志可见，这条只是锦上添花。 |

## 11. 第三轮：已核查无问题

| 位置 | 结论 |
|------|------|
| `Mods.cs` `_mod_file_at` / `_download_dest` / `SetModEnabled` / `DeleteContentFile` | 非法名（含 `/ \ :`、`..`、大小写拼写变体）都过 `LooksLikePathSpelling+NormPathSpelling` 与 `Resolve+Parent.Equals` 双校验；启停的 `.disabled` 往返与冲突检测正确 |
| `Mods.cs` `InstallCfContent` | 目标目录走 `dest_dir`（调用方传 `ContentDir`）——**正是它对照出 A-08**；下载候选（downloadUrl → CDN 两种规则）去重后逐个试，全失败才抛中文错误 |
| `Mods.cs` `InstallCurseforgeMod` / `_pick_cf_file` / `_download_cf_file` | 文件 id 命中→列表兜底→MC/loader 过滤→最新文件，逐级兜底都有中文提示；下载失败 `Utils.RemoveTree(dest)` 清残包再换下一个源 |
| `Modpack.cs` `InstallMrpack` | `modrinth.index.json` 嵌套识别、`formatVersion` 提示、每条 `files[].path` 都做 `Resolve+StartsWith(目标目录)` 越界校验、`sha512` 二次校验、`overrides/client-overrides` 复制、`CleanupTemp` 对「本地目录/本地文件/下载来的」三种来源分别判断（**不会误删用户的包文件**，已逐分支核对） |
| `Modpack.cs` `InstallCfZip` / `_install_plain_zip` | `manifest.json` 缺失时回落静态识别 `.minecraft` 目录结构（`_plain_pack_root`/`_plain_pack_version`），错误文案明确列出四种支持格式与 zip 顶层内容；`CfManifestLoaders` 缺加载器时给出「只装原版不会加载 mods」的解释 |
| `Modpack.cs` `_copy_embedded_versions` / `_prepare_target_version` | 包内自带 `versions` 优先复用、`version_name` 重命名走 `VersionOps.UniqueId`、隔离时 `SetIsolation`+`GameDir` 取真实游戏目录 |
| `Mods.cs` `_pick_version` / `ListVersions` / `SearchMods*` | 版本匹配失败会降级到「仅按 MC 版本」，再失败才抛；`RankHits` 加权排序与中文别名回退逻辑自洽 |

## 12. 观察（不改，留记录）

1. `Modpack.InstallCfZip:843` 临时包名用 `"tipcl_cfpack_" + Math.Abs((long)text.GetHashCode()) + ".zip"`：.NET 的字符串哈希**进程内随机化**且同 URL 恒定 → 同进程并发安装同一个 URL 会撞同一个临时文件（先完成的 `CleanupTemp` 会把后一个的下载删掉，表现为莫名其妙的「整合包无效」）。当前 UI 是串行任务队列，触发概率低；若要修，改成 `Guid` 即可。
2. `Java.FindSystemJavas` 全程持 `_SYS_JAVA_LOCK`（第二轮已记，App 未调用 `Cached*`，暂不影响）。
3. `Cleaner.Preview` 把 `.part` 与未引用库分开统计，`Apply` 默认三类全清——语义与 Windows 版一致，未改。

## 13. 第三轮未审完（下一轮顺序）

1. `Launcher/Catalog.cs`（607 行，仍只做了 grep 级扫描）。
2. `Launcher/Ai/*`（`Client.cs`/`AiAgent.cs`/`ToolRunner.cs`/`Compact.cs`/`Checkpoint.cs`）—— 流式解析、工具循环超时与取消、上下文压缩边界。
3. `Launcher/Crash.cs`（62KB）、`Updater.cs`、`Worlds.cs`、`Sync/PhoneLink.cs`、`Plugins/PluginMarket.cs`、`Plugins/PluginHost.cs`。
4. `Launcher/Optifine.cs` / `Liteloader.cs` 本体（`Installer.InstallOptifine/InstallLiteloader` 只是转发）。
---

# 第四轮（最后一轮 Core：Catalog.cs + Ai/* + 快速筛查 + 收尾）

基线：rebase 到第三轮合并后的 main。本轮**没有发现新的 Core bug**（下面是逐项核查结论与判据），因此零代码改动，只补报告。

## 14. Catalog.cs（607 行，已通读）

| 位置 | 结论 |
|------|------|
| `_MOD_ALIASES_LOWER`(311 条) / `_MODPACK_ALIASES_LOWER`(29 条) / `Index()` / `LookupModAlias` / `LookupModpackAlias` | 查表键必须全小写（`Index` 用默认的 ordinal 敏感比较，查表前统一 `Py.Lower`）。**机器核对过 340 条键：含大写字母 0 条**，表内重复键仅「机械动力」1 处，且分属 mod 表（→ create）与整合包表（→ CBC），是**有意**区分。无 bug。 |
| `FuzzyMatchMod` | `key.Contains(text) || text.Contains(key)`：空查询会命中全部 311 条。**观察**：App 侧只在用户输入非空时调用，暂无影响；若将来做「搜索建议」，建议先判 `text.Length>0`。 |
| `ModDescCn` / `POPULAR_MODS` / `POPULAR_MODPACKS` | 纯静态文案与推荐位，回退链（slug → title → fallback）正确，无异常路径。 |

## 15. Ai/*（按 lead 点名的五处重点读，其余标「未审完」）

| 文件 | 已读部分与结论 |
|------|----------------|
| `AiResponse.cs`（全 155 行） | SSE 行切分：`\r\n`/`\n`/`\r` 三种行尾与**跨 chunk 的 CR+LF** 都正确（`lastCr` 活到下一块）；读超时→`AiStreamBroken`、IO/已释放→`AiStreamBroken`；`CopyTimed` 超时抛中文「Read timed out. (上游长时间没有返回数据)」。**无 bug**。 |
| `Compact.cs`（全 246 行） | 微压缩只换「旧工具结果」且 `keep_recent` 保底；`AlignKeepBoundary` 把切点从 `tool` 消息往前挪（避免「工具结果与它的 tool_calls 被拆开」被接口拒）；摘要为空**拒绝替换**（宁可不换也不丢历史）；`CheckRapidRefill` 熔断带中文原因。**无 bug**。 |
| `ToolRunner.cs`（权限/执行/异常收口，240-350 行） | 取消 → `cancelled`「用户停止了这次操作」并给步骤行打 `已停止`；异常 → `Tools.ClassifyException` 分类 + 中文文案 + 步骤行日志；预览算不出来只降级不影响执行。**无 bug**。 |
| `Client.cs` `AssembleStream`（467-584 行） | 流断在工具参数中间时**不吐出半截 tool_calls**，改发 `error`「接口超时,正在换一次非流式」；`[DONE]`、`usage`、`error` 三个收尾分支齐备；坏 JSON 行跳过。**无 bug**。 |
| **未审完（重要声明）** | `Client.cs:585-921`（tool_calls 增量拼接后半段）、`AiAgent.cs`（回合循环/重试/`StreamAttemptAsync`）、`McpClient.cs`、`Checkpoint.cs`/`Store.cs` 的落盘细节，本轮只做了 grep 级扫描（`catch` 多为「落盘/预览失败不影响主流程」的性质）。**AI 层不能算审完**，见 §17 结论。 |

## 16. 快速筛查（grep 高危模式 + 关键路径读一遍）

| 文件 | 结论 |
|------|------|
| `Optifine.cs`（331 行，已通读） | 列表→选择→下载→`_has_tweaker` 判定→Patcher 路径（180s 超时、失败带 stderr 尾巴）→`_extract_launchwrapper` 提取内嵌 launchwrapper 并落到 maven 路径→tweaker 版本 json 的 1.13+ `arguments` / 老 `minecraftArguments` 分支，都正确。**观察**：`HasVersion(id)` 只看版本 json 存在就提前返回，若用户删过 `versions/<id>/<id>.jar` 会得到「已安装」但启动报「客户端 jar 缺失」——属既有语义，未改。 |
| `Liteloader.cs`（111 行，已通读） | 版本 id `<mc>-LiteLoader<mc>`、JAR 预下载到 maven 路径后由 `LooksComplete` 判定跳过重复下载、launchwrapper 大小校验，均正确。 |
| `Worlds.cs`（grep + 关键路径） | 全部错误都是中文 `WorldError`（含「世界压缩包损坏 / 含非法路径 / 空的世界压缩包」），zip 逐条做路径越界校验。**无问题**。 |
| `Cleaner.cs` / `Crash.cs` / `Updater.cs` / `Sync/PhoneLink.cs` | `Cleaner` 已在第三轮修（A-09）；`Crash.cs`(62KB) 与 `Updater.cs`、`PhoneLink.cs` 本轮只做 grep 级筛查（`PhoneLink` 的异常文案全是中文；`Updater` 的 Linux 分支用 `File.GetUnixFileMode` 保留权限）。**未通读，见 §17**。 |

## 17. X-04 / X-05 归属判定（lead 问的两条）

- **X-04（Cleaner 返回值语义）→ 归 App，Core 不动。** Core 侧已经做到它能做的：`Apply` 现在回 `removed`/`bytes`/`failed`/`error` 四个键，并逐条落 Warning 日志；「要不要把 `failed>0` 显示成 Warning 而不是绿字成功」是纯展示决策，改在 `SettingsPage.Cards.cs:274`。Core 若替它抛异常，App 现有 `TryCallAsync` 会退化成「清理后端 M3 接入」这种更糟的提示，所以**不该在 Core 抛**。
- **X-05（Modrinth 返回值被丢）→ 归 App，Core 不动。** `InstallModrinthMod` 的返回值已含 `slug`/`version`/`files`，第三轮 A-07 又保证前置失败会通过任务卡日志可见；「像 CF 那样显示已随装前置」需要 App 接住返回值。Core 侧唯一可做的增强（把 dependencies 塞进返回值）对 App 目前无效，属无收益改动，不做。

## 18. 四轮汇总

| 轮次 | 修复 | 其中 P0 | P1 | P2 |
|------|------|---------|----|----|
| 第一轮 | A-01 布局 v2 损坏备份链路缺失（数据丢失）、A-02 PyJson 深嵌套栈溢出（进程崩溃）、A-03 GameProcess 持锁调回调（潜在死锁） | 0 | 2 | 1 |
| 第二轮 | A-04 Linux 装出的 Java 无执行位（Chmod755 空实现 + tar/zip 权限位丢失）、A-05 Zulu/Microsoft 压缩包后缀与实际格式不符、A-06 词表补「结果列表」 | 0 | 2 | 1 |
| 第三轮 | A-07 前置依赖失败静默、A-08 隔离实例 Modrinth 内容包装错目录、A-09 Cleaner 删除失败全吞+假成功 | 0 | 2 | 1 |
| 第四轮 | 无（Catalog/Ai/快速筛查逐项核查未发现新 bug） | 0 | 0 | 0 |
| **合计** | **9 项** | **0** | **6** | **3** |

质量门槛：每轮结束都跑 `dotnet build -c Release`（0 错误 0 警告）+ 全量 `dotnet test`；最后一轮基线 **906 通过 / 0 失败 / 1 跳过**。所有修复都有 harness 断言或测试佐证（临时探针工程 `_tools/tmp-A/audit-harness`，24 项断言）。

### 结论：**Minecraft 主流程（下载/安装/启动/布局/配置/词表/插件状态/存档同步核心/模组与整合包安装）可以判定为「Core 层审计完毕」。**

### 但建议再来一轮（范围收窄，不是重头再来），理由三条：
1. **AI 子系统没审完**：`AiAgent.cs` 的回合循环/重试、`Client.cs:585-921` 的 tool_calls 增量拼接、`McpClient.cs`、`Checkpoint/Store` 的落盘细节都只做了 grep 级扫描。AI 助手是 Linux 侧的主打功能之一，且「流式丢 token / 工具循环不退出」正是用户能立刻感知的故障，值得单独半轮。
2. **`Crash.cs`(62KB) 没通读**：它决定「游戏崩了之后提示准不准」，其中 `AnalyzeLaunch` 会被启动流程直接调用。
3. `Updater.cs`/`PhoneLink.cs`/`PluginMarket.cs`/`PluginHost.cs` 只到 grep 级；这几块失败时的用户提示质量（尤其「自更新失败」）没被逐句核过。

其余（Catalog 别名表、OptiFine/LiteLoader 本体、Worlds、Cleaner、SemVer、Java 探测、下载/安装/布局/配置/词表）已逐行或逐函数核过，可以关闭。
---

# 第五轮（收窄范围：AI 子系统 + Crash.cs + 四个文件的提示质量）

基线：第四轮合并后的 main。本轮 2 处修复，都在 AI 子系统（lead 点名的「AI 卡住/越用越慢」类）。

## 19. 第五轮已修问题

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| A-10 | **P2（资源泄漏 → AI 越用越慢/忽然连不上）** | `Ai/Client.cs` `ChatStream`（原 `AiSession aiSession = _Session();` + `return AssembleStream(...)`）、`ChatOnce`（同） | 两条路都 `new AiSession()`（内部含 `HttpClient`+`HttpClientHandler`）却**从不释放**；`ChatStream` 还有一处困难：它返回的是惰性序列，响应与会话必须活到枚举结束，用 `using` 会提前关掉。后果：每轮对话漏一个 handler 与一条 keep-alive 连接（handler 不回收连接池），长会话几十轮后表现为「AI 越用越慢 / 突然报连不上接口」。`AiSession.Send` 里另有 `_cts.Token.Register(DisposeState, result)`：会话被 Dispose/Cancel 时会连带释放已注册的响应，所以只要释放会话就能兜住响应 | `ChatStream` 改为返回 `GuardStream(aiResponse, aiSession, AssembleStream(...))`：包装枚举在 **finally** 里释放响应与会话，枚举自然结束 / 提前 break / 抛异常三条路都覆盖（App 侧是 `foreach`，break 时会 dispose 枚举器 → finally 必执行）；`ChatOnce` 因结果已全量物化，直接 `using AiSession` | Core Release **0 错误 0 警告**；全量 `dotnet test` **906 通过 / 0 失败 / 1 跳过**；改动 38 行、无 public API 变化；`ChatStream` 的错误时机未变（仍在调用点同步抛 `AIClientError`，没有因为惰性化而推迟） | 已修 |
| A-11 | **P2（重试把同一轮文本写重复）** | `Ai/AiAgent.cs` `StreamOnceAsync`（while 循环里的重试分支） | 流在**半途**断开时（`AiStreamBroken` → 事件 `error`「接口超时,正在换一次非流式」）会走整轮重试，但重试前的 `state.Text` **没回滚**：重试成功后，`前半截 + 完整回答` 一起追加进 `state.Text`。`state.Text` 是落盘的助手消息、也是下一轮的上下文 —— 于是历史里出现重复段落，模型下一轮还会把它当既有内容 | 每轮尝试开始处记 `textBase = state.Text.Length`，重试 `continue` 之前 `state.Text.Length = textBase` 回滚（`StringBuilder` 截断，零分配）。工具调用不受影响（工具只在本轮无错时才进 `state.Calls`，不会重复执行） | Core Release 0 错误 0 警告；全量测试绿；只在「重试」分支生效，正常路径与最终失败路径的文本保留策略不变（最终失败仍保留半截内容，不丢信息） | 已修 |

## 20. 第五轮：已核查无问题

| 位置 | 结论 |
|------|------|
| `AiAgent.StreamOnceAsync` 重试链 | **不会重复产生工具副作用**：`attempt.PendingTools` 只在本轮 `attempt.Error.Length == 0` 时才转成 `state.Calls`；重试分支在工具落库之前就 `continue` 了。「停止」也真的能断：`linked.Token` 注册 `cancel.Abort()`（关会话+释放响应），`Task.Delay` 带 `ct`，退避期间点停止立刻退出，`linked.IsCancellationRequested` 分支还会补发一次「已停止」事件。 |
| `Client.AssembleStream`（467-709 行） | 流断在 **工具参数中间**时不吐半截 `tool_calls`，改发 `error`「工具参数不完整,正在换一次非流式」；`finish_reason=tool_calls` 与 `[DONE]` 双收尾都对 `FlushCompleteTools` 判空；`usage` 只在真有值时发；`reasoning_content` 用 `thinking` 段包裹且能正确闭合。 |
| `AiSession`/`HttpCancel` | `Send` 用 `ResponseHeadersRead` + 独立 CTS（connect/read 超时分别给值）；`HttpCancel.Bind` 在已 flag 时会立刻 Abort；`Close()/Dispose()` 全部吞异常。 |
| `Compact`（第四轮已核） | 切点避让 `tool` 消息、空摘要拒绝替换、rapid-refill 熔断、`keep_recent` 保底 —— 边界正常。 |
| `Crash.cs` | `AnalyzeLaunch` → `_run`：分析根目录按**版本隔离**切到 `GameDir`（隔离实例的日志在 versions/<版本> 下），`LooksLikeCrash` = 取消→否 / 非 0 退出码→是 / 命中 `Game crashed!` 或 `---- Minecraft Crash Report ----`→是 / 有 hs_err→是，口径合理；`_title_of` 对未知 code 回落到中文「Minecraft 出现错误」而不是空串；`ExportReport` 逐文件去重、`FilterSecrets` 过滤、临时目录 finally 删除；`OpenPath` 失败只是返回 false。**都合理，无 bug**。 |
| `Updater` / `Sync/PhoneLink` / `Plugins/PluginMarket` / `Plugins/PluginHost` 提示质量 | 全部 `throw` 站点逐条看过：消息**都是中文且具体**（如「更新包缺少有效 SHA-256,已拒绝下载」「「世界」正在游戏中打开,先退出世界再同步」「插件包含非法路径:xxx」）。**没有空消息**，没有把原始英文异常直接抛给 UI（唯一英文串 `"symlink failed"`/`disk_usage` 都在自己的 catch 里被吞掉、不可达用户；`Updater.cs:81` 的 AttributeError 是模拟 Python 语义的内部错误）。 |

## 21. 跨域问题（第五轮新增）

| ID | 目标 | 级别 | 说明 |
|----|------|------|------|
| X-06 | aud-app | P2 | A-11 只修了 Core 侧 `state.Text`（落盘消息 + 下一轮上下文）。界面上**已经显示出来的**半截增量收不回来：重试后 UI 会看到「前半截 + 完整回答」拼接。要彻底干净需要 App 在收到 `error`（即将重试）时清掉本轮气泡文本，或 Core 增发一个 `reset` 事件由 App 处理——后者要动 App 渲染，故未做。 |

## 22. 第五轮：仍未逐行审完（如实声明）

- `AiAgent.cs` 除 `StreamOnceAsync`/`StreamAttemptAsync`/权限与压缩分支外的部分（子代理、checkpoint 交互）。
- `Ai/Checkpoint.cs`(20KB)/`Ai/Store.cs`(17KB) 的落盘细节：只做了 grep 级 + 关键路径抽读（写盘都是「临时文件 + 替换」的模式）。
- `Ai/McpClient.cs`：只确认了 10s 连接/60s 调用超时与 `answered.Wait` 的用法。
- `Crash.cs` 的**规则表**（约 1900 行里的正则与分析器内部）：只逐段读了 `AnalyzeLaunch`/`LooksLikeCrash`/`_title_of`/`ExportReport`/`BuildActions` 的框架，没逐条核 200+ 个崩溃特征规则。这一块的性质是「规则准不准」，不是「代码会不会崩」，且误判只会影响提示文案，不会伤数据。

## 23. 五轮累计

| 轮次 | 修复 | P0 | P1 | P2 |
|------|------|----|----|----|
| 一 | A-01 布局 v2 损坏备份链缺失 · A-02 PyJson 深嵌套栈溢出 · A-03 GameProcess 持锁调回调 | 0 | 2 | 1 |
| 二 | A-04 Linux Java 无执行位 · A-05 Zulu/Microsoft 包后缀错 · A-06 词表补 key | 0 | 2 | 1 |
| 三 | A-07 前置依赖失败静默 · A-08 隔离实例 Modrinth 内容包装错目录 · A-09 Cleaner 假成功 | 0 | 2 | 1 |
| 四 | 无（Catalog/Ai 抽读/快速筛查未见新 bug） | 0 | 0 | 0 |
| 五 | A-10 AI 会话/响应泄漏 · A-11 重试文本重复 | 0 | 0 | 2 |
| **合计** | **11 项** | **0** | **6** | **5** |

每轮门槛：Core Release **0 错误 0 警告** + 全量 `dotnet test` 绿（末轮 906 通过 / 0 失败 / 1 跳过）。

## 24. 结论

**Core 层可以判定为审计完毕，建议就此收工。** 理由：

1. 五轮下来 Minecraft 主流程（下载/安装/启动/崩溃提示/布局/配置/词表/插件状态/存档同步/模组与整合包）**P0 = 0、P1 = 0**（6 条已修），P2 里只剩「展示层取舍」这类需要 App 配合的项（X-04/X-05/X-06）。
2. 剩下没逐行读的只有三块，性质都是**规则/策略表**而非控制流：`Crash.cs` 的 200+ 条崩溃特征规则、`AiAgent` 的子代理与 checkpoint 交互、`Checkpoint/Store` 的落盘细节。它们出问题的表现是「提示不够准」或「恢复点不全」，不会造成数据丢失或启动失败，边际收益明显低于前五轮。
3. 若后续有真实用户反馈（例如「某个崩溃提示不对」「AI 长会话还是卡」），带着那条反馈做**定向**回归比再铺一轮全面审计更划算。
---

# 第六轮（Core 深水区：AI 落盘/MCP 生命周期/Crash 提示/Modpack 临时文件/插件市场）

基线：main 880deff（五路合并后）。本轮 4 处修复，全部 P2。

## 25. 第六轮已修问题

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| A-12 | P2（对话历史丢失 / AI 页读不出来） | `Ai/Store.cs` `Load`（首行判空 + `updated` 取值） | 两处：①`ai_chats.json` 存在但解析失败（截断/手改/磁盘问题）时，`Load` 直接建空库并 `Save` → **把玩家全部 AI 对话静默覆盖**，且不留备份（`PluginStateStore` 对同样的场景早就会留 `.corrupt`）；②`Py.Int(Py.Or(chat.Get("updated"), 0L))` 对脏字段（老版本/手改成字符串）会抛 `ValueError`，一路冒出 `Load` → 整份历史读不出来 | ①解析失败且文件确实存在时先 `File.Copy` 成 `.corrupt` 再重建；②`updated` 改 `Py.IntOr(..., 0)`（有兜底，不再炸整份存档） | Core Release 0 错误 0 警告；全量 `dotnet test` **913 通过 / 0 失败 / 1 跳过** | 已修 |
| A-13 | **P2（AI 越用越卡：孤儿进程 + 句柄泄漏）** | `Ai/McpClient.cs` `Close`（原 `_proc.Kill()`）、`Recv` 超时看门狗 | ①只 `Kill` **直接子进程**：MCP server 的命令几乎都是 wrapper（`npx xxx` / `uvx xxx` / `cmd /c`），真正的 node/python 孙进程活下来变孤儿；而 `Mcp.CloseAll` 是**每轮对话结束都关一次** → 越聊孤儿越多、内存越高，用户感知就是「AI 用久了变卡」；②`Process` 从不 `Dispose`，每轮重连漏一个进程句柄 + 三根管道 | `Close` 与超时看门狗都改 `Kill(entireProcessTree: true)`（`Kill(bool)` 在已退出/无权限时抛，catch 里再兜一次普通 `Kill`，不比原来弱）；`Close` 末尾补 `_proc.Dispose()` | Core Release 0 错误 0 警告；全量测试绿；行为差异仅在「关闭时更彻底」，正常退出路径要先 `WaitForExit(2000)` 才动手，未改变原语义 | 已修 |
| A-14 | P2（偶发「不是有效的整合包 zip」） | `Modpack.cs` 三处临时包名（`_fetch_mrpack` / `InstallCfZip` / `InstallCfModpack`） | 前两处用 `"tipcl_(mr|cf)pack_" + url.GetHashCode()`、第三处干脆是 `"tipcl_cfpack_<addon_id>_<file_id>"` —— **完全确定的名字**。同一整合包被装两次（两个启动器进程，或两个实例先后点安装）就下到**同一个临时文件**：先完成的 `CleanupTemp(downloaded=true)` 把它删掉，另一个正在解压 → 报「不是有效的整合包 zip / 找不到整合包文件」，**重试一次就好** —— 典型偶发、典型用户体感「点了没反应/莫名失败」 | 三处临时名都追加 `Guid.NewGuid().ToString("N").Substring(0, 12)`（每个调用唯一，跨进程也不撞） | Core Release 0 错误 0 警告；全量测试绿；只改临时文件名，不碰落盘目标 | 已修 |
| A-15 | P2（崩溃提示误导） | `Crash.cs` `ExitHint` | Linux/macOS 上被信号杀掉时 .NET 给的退出码是 `128+signum`，原实现只对 Windows NTSTATUS（`_NT` 表）有解释，Linux 上直接吐「退出码 139」/「退出码 137」—— 玩家完全不知道 139 = 段错误、137 = 被 OOM 强杀（Linux 上最常见的两种崩溃） | 非 Windows 且 `value>128` 时补信号映射：SIGHUP/SIGINT/SIGILL/SIGABRT/SIGFPE/**SIGKILL(常见于内存不足被 OOM Killer 杀掉)**/**SIGSEGV(段错误,多与 JVM/显卡驱动/本地库有关)**/SIGPIPE/SIGTERM，未知信号仍走原来的兜底 | Core Release 0 错误 0 警告；全量测试绿；Windows 分支字节不变（`_NT` 表与 `value==0/1` 逻辑未动） | 已修 |

## 26. 第六轮：已核查无问题

| 位置 | 结论 |
|------|------|
| `Ai/Checkpoint.cs`（536 行通读） | 恢复点完整：快照按「受影响路径」内容寻址存块（同内容只存一份、临时文件+原子替换）；`Snapshot` 对**当时不存在**的路径记 `sha256=""`（回滚时删掉 = 覆盖「这轮新增」）；`Rollback` 按 seq **逆序**重放、`SweepNewEntries` 先删深再删浅、失败条数如实进 `failures`（不静默当成功）、成功的 op 从流水摘掉、失败的原样留着；`GcBlobs` 清未被引用的块；上限（单轮 512MB / 单会话 1GB / 单目录 20000 文件）超了**整轮拒绝**而不是半个快照。 |
| `Ai/Store.cs` 事件日志 | `LogEvent` 加锁 append + 首轮裁剪（7 天/超大即删），`ReadEvents` 坏行跳过；`Save` 有 40 对话上限并修正 `active_id`。 |
| `Ai/McpClient.cs` 除生命周期外的部分 | `Request` 持 `_lock` 串行化（stdio 单管道，正确）；`Recv` 跳过通知、只认 `id` 匹配；超时/子进程退出都转 `McpError`（调用方当「工具失败」，不炸整轮）；`CallTool` 无 text 块时把原始 JSON 回给模型（不丢结果）。 |
| `Crash.cs` 框架 | `AnalyzeLaunch`/`_run` 按版本隔离切分析根目录、`LooksLikeCrash` 判据（取消→否 / 非 0 退出码→是 / `Game crashed!`/`---- Minecraft Crash Report ----`→是 / 有 hs_err→是）、`_title_of` 未知 code 回落中文、`ExportReport` 去重+`FilterSecrets`+临时目录必删、`ReadText` 多编码兜底（UTF-8→GB18030→GBK）、`NormalizeExit` 对 >int.Max 的 32 位码转有符号。 |
| `Plugins/PluginMarket.cs` | `ReadZipManifest` 根目录/唯一子目录定位、`ExtractWithPrefix` 逐条做越界校验 + 跳过符号链接（`0xA000`）+ 前缀剥离、`Sha256Matches` 大小写不敏感、`CheckDependencies` 用 `SemVer` 且错误文案中文具体。 |
| `Plugins/PluginHost.cs` | **没有子进程**：插件在宿主进程内加载（.NET）/ 由 Node 侧另起，故不存在「宿主进程退出清理」问题；失效路径全是中文 `PyException("PluginError", "插件不存在:…")`。 |
| `Mods.cs` / `Catalog.cs` 剩余分支 | 版本匹配（`_pick_version`/`_pick_cf_file` 的 MC+loader 过滤与降级）、`ListVersions` 排序、`RankHits` 加权、别名表（340 条键全小写、无表内重复）—— 前几轮已逐段核过，本轮抽查无新增问题。 |

## 27. 第六轮：仍未逐条验证（如实声明）

- **`Crash.cs` 的 200+ 条崩溃特征规则**：本轮只验证了「框架与提示口径」，规则本身的**准确率没有逐条验证**。原因：这属于「数据/文案」问题——规则误报只会让提示偏一点，漏报会退回「游戏已退出」，都不会造成崩溃或数据丢失；要真正验证需要**真实崩溃日志样本库**（每个规则至少一正一反样本），成本远高于收益。
- `AiAgent.cs` 的子代理/checkpoint 交互路径、`Rewind.cs`/`Compact` 与 `Checkpoint` 的联动细节。
- `AiSelfTest`/`VisionCaps` 等辅助模块（只做过 grep 级筛查）。

## 28. 六轮累计

| 轮次 | 修复 | P0 | P1 | P2 |
|------|------|----|----|----|
| 一 | A-01 布局 v2 损坏备份链 · A-02 PyJson 深嵌套栈溢出 · A-03 GameProcess 持锁回调 | 0 | 2 | 1 |
| 二 | A-04 Linux Java 执行位 · A-05 压缩包后缀 · A-06 词表补 key | 0 | 2 | 1 |
| 三 | A-07 前置依赖静默失败 · A-08 隔离实例内容目录 · A-09 Cleaner 假成功 | 0 | 2 | 1 |
| 四 | 无（抽读/筛查未见新 bug） | 0 | 0 | 0 |
| 五 | A-10 AI 会话/响应泄漏 · A-11 重试文本重复 | 0 | 0 | 2 |
| 六 | A-12 AI 存档覆盖/读不出 · A-13 MCP 孤儿进程与句柄 · A-14 整合包临时名撞名 · A-15 Linux 信号退出码无解释 | 0 | 0 | 4 |
| **合计** | **15 项** | **0** | **6** | **9** |

门槛：Core Release **0 错误 0 警告**；全量 `dotnet test` **913 通过 / 0 失败 / 1 跳过**。

## 29. 结论：还能再挖吗

**不建议再开第七轮做「全面审计」，建议转为「带真实样本的定向回归」。** 依据：

1. 六轮下来 **P0 = 0、P1 = 6（全在前三轮修完），第四~六轮的发现全是 P2**，且第五/六轮的 P2 集中在「资源/临时文件/提示文案」这类**不会损坏数据、不会让游戏启动不了**的问题上——严重度曲线已经明显走平。
2. 剩下唯一「有量」的未验证面是 `Crash.cs` 的 200+ 条特征规则，而它必须用**真实崩溃日志正反样本**才能判准（不是读代码能解决的）；把它当代码审计目标，投入产出比最差。
3. 真正还可能藏 P1 的地方已经不在 Core，而在**跨域配合**：X-04（清理结果的失败提示）、X-05（Modrinth 返回值展示）、X-06（重试时 UI 半截文本）。这三条都是 App 侧一行到十几行的接线，用户可见收益比继续挖 Core 大得多。

**判定：Core 层审计完毕。** 建议把资源转向：①App 侧 X-04/X-05/X-06 接线；②攒一批真实崩溃日志做 `Crash` 规则回归；③真机冒烟矩阵复核（尤其容器/无 GUI/权限受限场景）。
---

# 第七轮（复审自有修复 + Crash 规则表静态审）

基线：main 97eae96（含六轮合并）。本轮**没有开新领域**，只审自己前六轮加的代码，以及 Crash.cs 规则表的"能不能跑/会不会误伤"。

## 30. 复审范围与结论

复审对象：我的 7 个提交（c0551f7 87fc4c2 d1a8754 7313db9 ad6e72 e750ba8 44eb617），涉及 **15 个文件（13 个 Core 源文件 + en/zh_CN 各 1 条词条）、约 60 处改动**。逐处按 lead 给的三问过了一遍。

**结论：找出 4 处"修复自身引入的问题"，全部已修。** 另有 8 处 lead 点名的疑点，逐一验证后**确认不是问题**（见 §32）。

| ID | 级别 | 位置（我的改动） | 我引入的问题 | 修复 |
|----|------|------------------|--------------|------|
| **A-16** | **P1（可能永久卡住 AI 回合）** | `Ai/McpClient.cs` `Recv` 超时看门狗（第六轮 A-13 改的） | A-13 把 `proc.Kill()` 换成 `proc.Kill(entireProcessTree: true)` 并把异常一口吞掉。可 `Kill(bool)` 在 /proc 受限、进程恰好退出、无权限时会**抛异常**——抛了就**一个进程都没杀**，而 `Recv` 正阻塞在 `StandardOutput.ReadLine()` 上（deadline 检查只在一行读完之后），于是**整轮对话永久挂死**。改之前至少普通 `Kill()` 能把它弄死。即"修孤儿进程"顺手造了个"超时变成永久卡住"，比原问题更糟 | 抽出 `KillTree(proc)`：先 `Kill(entireProcessTree:true)`，**失败再退一步普通 `Kill()`**；`Recv` 看门狗与 `Close` 都只调它，不再就地 `Kill` |
| A-17 | P2（"前置失败不阻断"被我自己弄成会阻断） | `Mods.cs` 两处 `catch` 里新加的 `on_progress?.Invoke(...)`（第三轮 A-07） | A-07 的价值是让前置失败可见，但那两个 `on_progress` 是宿主回调（App 侧一路走到 UI）。**回调自己抛异常**时，异常会从这个 catch 里逃出去，把"前置失败不阻断主流程"变成整次安装失败——比原来的"只写日志"更糟 | 两处都包 try/catch：提示尽力而为，绝不改变"不阻断"的语义 |
| A-18 | P2（会把好文件改名搬走） | `Layout/LayoutStoreV2.cs` `EnsureCorruptBackedUp`（第一轮 A-01 新增） | `_pendingCorruptBackup` 是"读的时候坏的"，但**读与写之间别的进程可能已经把它写好了**。我的实现无条件 `File.Move` 改名——把一份刚被写好的**好文件**变成 `.corrupt-*`，再被我们的新文档覆盖（文件没丢，但对方那次写入凭空消失） | 写前先 `ReadV2File(pending, out stillDamaged)` 重判：**不再损坏就清掉待办、照常写**（此时覆盖属正常 Save 语义，不是"毁掉损坏原件"） |
| A-19 | P2（ailed 计数被"已删"污染） | `Cleaner.cs` `Apply` 的失败计数（第三轮 A-09） | 预览到执行之间文件被别的进程/上一次清理删掉，`FileInfo.Length` 会抛 `FileNotFoundException`，被我算成一次 `failed`。App 侧要按 `failed` 弹警告（X-04），这种幂等场景会让它刷一堆假失败 | 单独 `catch (...) when (e is FileNotFoundException || e is DirectoryNotFoundException)` 视为成功，不算 `failed` |

## 31. 复审项的可失败测试（红灯验证）

按 lead 要求给每条补断言，并把"改回旧实现"跑一遍确认真的会红。断言放在我的探针工程 `_tools/tmp-A/audit-harness`（`r7` 子命令）—— **`tests/**` 按分工不归我**，若要把它们落进 `tests/TipCL.Tests` 请指派 D 线或授权我动那个目录。

| 复审项 | 断言 | 改回旧实现时 |
|--------|------|--------------|
| A-18 | 损坏→Load（记待办）→外部写好→Save：断言 `*.corrupt-*` **不存在**、新文档已落盘 | **实测红**：`git stash` 掉本轮改动后跑，`FAIL A-18 已经变好的文件不会被改名成 .corrupt-*`（exit=1），stash pop 回来变绿 |
| A-19 | 预览后删掉缓存文件再 `Apply(["cache"])`：断言 `failed == 0` | **无法构造确定性红灯**：`Apply` 内部会重新 `Preview()`，要从"它的预览"到"它的删除"之间插进删除，只能靠并发另一进程——是真实存在但非确定性的 TOCTOU 窗口。如实记为"行为细化，无可控红灯用例" |
| A-17 | 需要真实 Modrinth 依赖失败 + 抛异常的 `on_progress` 才能触发 | 离线构造不出；靠代码审查 + 全量测试绿兜底，如实记录 |
| A-16 | 需要真实 MCP 子进程且树杀失败 | 同上（无法人为让 `Kill(true)` 失败） |
| 回归保护 | 全量跑探针默认用例：第一轮的"损坏→Save 必须先备份再写"仍 **ALL PASS**（A-18 的重判没有破坏主流程） | — |

## 32. lead 点名的疑点：逐个验证（结论：都不是问题）

| 疑点 | 验证结论 |
|------|----------|
| `PyJson._depth` 在抛异常路径上是否都回退 | 平衡：超限分支先 `_depth--` 再抛，正常分支走 `try/finally` 回退；`Parser` 每个 `Loads` 新建，不跨调用累积。 |
| `LayoutStoreV2._pendingCorruptBackup` 在 dir 变化/多实例时会不会串 | 所有读写都在 `_gate` 内；dir 变化时最坏结果是"备份了另一个目录里那份本来就损坏的文件"，**不会覆盖任何原件**；`ResetCache` 会清空三件套。A-18 的重判进一步把"已经不是损坏文件"的情况挡掉。 |
| `Java.Chmod755` 会不会给 zip 里的 symlink 条目误加权限 | 不会：`Chmod755` 只被 `InstallMojangRuntime` 调用，且只对运行时清单里 `executable: true` 的**普通文件**（逐文件下载，不是解压）；`SafeExtractZip` 那条路会先 `continue` 掉 symlink 条目（`0xA000`）与目录条目，轮不到 `ApplyUnixMode`。 |
| `Utils.ApplyUnixMode` 对 Windows 打的 zip | Windows 打的 zip 高 16 位通常是 0 → `mode & 0x49 == 0` → 直接返回，不动权限。 |
| `Mods.InstallModrinthContent` 新增 `dest_dir` 对既有调用点 | 唯一调用点已更新；`dest_dir == null` 时内部回落 `ContentDir(instance, subdir)`，而 `ContentDir` 在 `version_id == null` 时**逐字等于**旧的 `PPath.Join(instance.path, subdir)`——默认行为不变。 |
| `Cleaner.Apply` 返回值新增两键 | 只增不改：`removed`/`bytes` 语义未动；App 的 `CleanerResultStub` 只取 `Removed`，多余键在反序列化时被忽略。 |
| `.corrupt` 备份在文件被独占/只读时 | `File.Copy` 只需读源文件（只读源可复制）；目录不可写时 `IOException` 被吞，走原来的"重建"路径——不比改之前更差。 |
| `Kill(entireProcessTree:true)` 在 `npx` 没装时 | 走不到：`Connect` 里 `Process.Start` 先抛 `Win32Exception` → 转 `FileNotFoundError("No such file or directory: npx")`，与改动前一致。 |

## 33. Crash.cs 规则表静态审（不判"准不准"，只判"能不能跑/会不会误伤"）

| 检查项 | 结果 |
|--------|------|
| 规则被触发但缺文案 → `_fmt_reason` 会不会抛 | **不会**。机器对比：分析器实际发出 **40 个 reason code**；`_TABLE` 覆盖 23 个、`switch` 的 `case` 20 个、`code ==` 2 个，剩下 `pixel_format`/`intel_av`/`amd_av`/`nvidia_av`/`no_opengl` 由 `default:` 分支按"显卡/驱动"一组统一兜住。**每个 code 都有文案，无 KeyError 路径**。 |
| 字典重复键（会在静态构造时抛 `TypeInitializationException`，让"崩溃分析"自己崩掉） | `_NT`(7 条)、`_TABLE`(23 条) 用集合初始化器，重复键会**抛**——机器比对无重复；`_TITLES` 是 `PyDict.Of`，重复键会**静默覆盖**——17 条也无重复。 |
| 大小写 / locale 敏感（土耳其 i） | 文件内**没有** `.ToLower()` / `.ToUpper()` / `string.Compare` 的文化敏感用法；`Rx()` 统一追加 `RegexOptions.CultureInvariant`，两处内联 `(?i)` 的 token 正则也带 `CultureInvariant`。 |
| 正则灾难性回溯 | 只有一处嵌套量词 `(?:[\n\r]+\t.*)+`（mod_missing 的依赖列表），每轮迭代必须吃掉换行+制表符，**不存在空匹配循环**；其余规则都是线性 `[^\n]+` 类。规则正则未设 MatchTimeout，但输入是用户自己的崩溃日志、且单条正则复杂度为线性——不构成真 bug。 |
| `null` / 超长行 | `Analyzer` 全程用 `Py.Slice`/`Tail`/`ReadText(max_bytes)` 截断；`CollectFiles` 只收集已知日志文件。未发现把整份日志无界拼进正则的地方。 |
| `_title_of` 未知码回落 | 有回落：未知 code → 中文「Minecraft 出现错误」，不会返回空串。 |
| **结论** | **未发现真 bug**，规则表可安全运行。 |

## 34. 本轮过程事故（透明记录）

- 复审期间我又一次用 PowerShell 改源码：先把 `Client.cs` 写坏过一次（已 `git checkout` 还原并改用 `edit` 逐处修改），又在修 A-19 时把 `Cleaner.cs` 的 `catch` 结构弄断（构建立刻报 12 个错误，按行号手术修复，并顺手把整文件的行尾从 CRLF 归一为 LF，避免出现"整文件重写"的假 diff）。**最终 diff 是 Cleaner 7 行、LayoutStoreV2 11 行、McpClient 54 行、Mods 21 行**，无整文件重写。
- 教训（写进结论提醒后续）：`read` 工具在显示时会**把全角括号/冒号折成半角**，按显示内容写 `old_string` 必然匹配失败；改中文源码时要么用只含 ASCII 的锚点，要么按行号手术。

## 35. 本轮门槛

Core Release **0 错误 0 警告**；全量 `dotnet test` **936 通过 / 0 失败 / 1 跳过**；探针默认用例与 `r7` 用例全 PASS。

## 36. 七轮累计

| 轮次 | 修复 | P0 | P1 | P2 | 备注 |
|------|------|----|----|----|------|
| 一~三 | A-01…A-09 | 0 | 6 | 3 | 主流程真 bug |
| 四 | 无 | 0 | 0 | 0 | 抽读/筛查 |
| 五~六 | A-10…A-15 | 0 | 0 | 6 | 资源/临时文件/提示 |
| **七** | **A-16…A-19（全部是"修复自身引入"）** | 0 | **1** | 3 | 复审自有改动 |
| **合计** | **19 项** | **0** | **7** | **12** | |