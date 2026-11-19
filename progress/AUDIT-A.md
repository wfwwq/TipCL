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