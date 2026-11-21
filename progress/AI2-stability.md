# AI2-stability:测试稳定性 + 底层遗留问题(分支 `ai2/stability`,基于 main@f1c070b)

> 第二轮 5 个并行包之一。只改 Linux/Avalonia 主线(`src/`、`tests/`);`windos/` 未动(`git diff f1c070b HEAD -- windos` 为空)。
> 本包**没有新增 AI 工具**(工具表仍是 65 个),只改了三个已有工具的行为:`ping_server`(SRV)、`preflight_check`(生效内存)、`apply_instance_upgrade`(撤回范围)。

## 1. 做了什么

### 1.1 根治 DlgCloseTests 偶发失败(任务 1)

**先在基线上证明问题存在**(main@f1c070b,本 worktree 未改动时):
只跑 `ClickSweepTests|DlgCloseTests`(同一个「TipCL 真机交互回归」串行集合)循环 8 次 —— **4/8 次失败**,4 次都是
`DlgCloseTests.Confirm_WindowClosed_ReturnsFalse`,`System.ArgumentNullException: Value cannot be null. (Parameter 'control')`,调用栈:

```
ResourceNodeExtensions.GetResourceObservable ← Res.Bind ← AiBubble..ctor ← AiPage.Append ← AiPage.OnAgentEvent(error)
← AiPage.RunAgentAsync 里的 Dispatcher.UIThread.Post 回调 ← Dispatcher.ResetForUnitTests ← HeadlessUnitTestSession.EnsureApplication
```

根因链:ClickSweep 点到 AI 页「还没有对话」下的建议问题 → 测试环境里内置网关是「已配置」的 → **真的向外网发了对话请求** →
连接失败被 AiAgent 当「网络异常」退避重试 3 次(约 7 秒)→ 失败事件 Post 回界面线程时早已是别的用例 → 恰好在无头会话
`Dispatcher.ResetForUnitTests` 冲刷旧队列时执行,那一刻 `Application.Current == null` → `Res.Bind` 用 `Application.Current!` 取资源抛异常,
记在当时正在跑的 DlgCloseTests 头上。

**三层修法**(任一层单独都能挡住这次现场,叠起来是为了不再有同类问题):

| 层 | 改动 | 作用 |
|---|---|---|
| 测试隔离 | Core 新增 `HttpHandlerHook`(Net 与 AiSession 建 HttpClient 时都过它,生产不设 = 原处理器);测试程序集 `TestNetworkIsolation` 装一层闸门:**AI 通道只放行环回地址**,其余当场回 HTTP 400(不联网、4xx 不重试,失败事件在发起它的用例里就落地);Net 通道支持「当前异步流登记的假响应」(AsyncLocal,不串到别的用例),供并发压力测试用。顺带把 `Crash.ShellOpenHook` / `Auth.OpenBrowserHook` 默认设成空操作(测试里「打开文件夹 / 登录页」不真拉起资源管理器 / 浏览器) | 不再真发请求 |
| 界面回调 | 新增 `Common/Ai/AiUiPost`:AiPage / AiFixView 里所有 Agent 回到界面线程的回调(事件、计划卡、任务卡、日志、异常收尾)改走它 —— 执行时 `Application.Current` 为空,或页面挂过的窗口已关闭(页面已卸载)就**丢弃并计数**;只是切到别的页(窗口还在)照常执行 | 晚到的回调不碰控件 |
| 控件防御 | `Res.Bind` 改为 `BindFrom(Application.Current, key)`:宿主为空时给「未设置」绑定(属性保持默认值),与 `Ui.Dyn` 的空安全口径一致;AiBubble / IconLib.Make 都经由它 | 空控件不再抛 |

**复现率前后对比**(同一台机器、同一过滤条件、各 8 次):

| | ClickSweep + DlgClose 循环 8 次 | 失败用例 | 日志里 `AiBubble..ctor` / `ArgumentNullException` 帧 |
|---|---|---|---|
| 基线 f1c070b | **4/8 失败(50%)** | 4 × `Confirm_WindowClosed_ReturnsFalse` | 5 处 |
| 本分支 | **0/8 失败** | — | 0 处 |

全量测试连跑 3 次,3 次都是 0 失败(见 §4)。

**LayoutSmoothDragUiTests 的动画时序**:代码里没有字面的「300ms」断言,但确实依赖墙钟 —— 基线单跑这一类 10 次**失败 1 次**
(`EscapeDuringSmoothDragSlidesBackAndPersistsNothing` 第 403 行 `GetOffset(a).X > step*3` 不成立)。原因:无头输入
(MouseMove / KeyPress)内部会 `RunJobs`,补间计时器按 `Stopwatch` 算进度,机器一卡(首个用例 JIT 约 1s)动画就被推到半路甚至终点。
修法:`LayoutTween` 加内部时钟注入点 `Clock`(默认 `Stopwatch.GetTimestamp`,生产不变),测试夹具在 Host 里**冻结时钟**,
动画只在 `FinishAnimations` 里推到终点;修后单跑 10 次 0 失败。

### 1.2 Preflight 内存检查用实际生效值(任务 2)

新增 `Launcher/EffectiveJvm.cs`,**唯一**写着顺序的地方:
- 内存:版本设置 → 实例 `.instance.json` → 调用方传的(启动页滑块 / launch_game / preflight_check 参数)→ 全局 `default_memory_mb` → 4096;
- GC:版本设置 → 实例 → 全局 `gc_preset` → auto;每层只认正整数 / 非空串,非法值落到下一层。

`Resolve(inst, version, callerMemory, versionSettings?)` 返回 `{memory_mb, memory_source, gc_preset, gc_label, gc_source}`(source ∈ version / instance / caller / global / default)。
接入:`LaunchFlow.Prepare`(拼命令行用的就是它)、`JvmTuning.Effective`(改为一行转发,不传调用方 = 原口径)、`Preflight.CheckLaunch`(「内存接近可用物理内存」比的是生效值;新增 `AvailPhysMbHook` 注入点,钩子回 null = 照常探测)、`AiPreflight.Check` / `preflight_check`(比物理内存用生效值,结果多一个 `memory_source`)。

### 1.3 ping_server 支持 SRV(任务 3)

新增 `Launcher/DnsSrv.cs`,不加依赖:自己拼 DNS 查询(RD=1、QTYPE=SRV)用 UDP 发出,按 RFC 1035 解析应答(支持名字压缩指针、防指针环),
校验事务 id / QR / RCODE / 截断,多条记录按 RFC 2782 取优先级最小、同优先级权重最大的。DNS 服务器:Linux / macOS 读 `/etc/resolv.conf`,
Windows(以及读不到 resolv.conf 时)用 `NetworkInterface.GetIPProperties().DnsAddresses`;每台服务器单独计时,总超时 `min(1500ms, timeout/2)`。
`ServerPing.PingAsync(..., resolveSrv)`:地址**没写端口**且是域名时先查 `_minecraft._tcp.<host>`,查到就连记录里的目标与端口,
查不到 / 超时 / 没有 DNS 服务器就回落到「A 记录 + 原端口(25565)」—— 与原版客户端行为一致;IP 字面量、localhost、单段名不查。
握手里报的仍是玩家输入的地址(代理服按它分流)。结果多 `resolved_via`(srv / a / literal)、`connect_host`、`connect_port`、`srv`。
`ping_server` 分发:`address` 没带端口且没传 `port` 才开 SRV;按 `index` 取列表里的服务器时,列表里没写端口(=25565)才开。

### 1.4 apply_instance_upgrade 原地升级的检查点(任务 4)

原来原地升级的快照只有 `mods/` 与 `.instance.json`,撤回后会留下新装的版本目录(几百 MB)。把整个 `versions/` 塞进内容快照会撞 512MB 上限,
所以新增一种**只记名单、不存内容**的检查点操作(`Checkpoint.NewChildren.cs`):快照时记下 `versions/` 下已有的子目录名,回滚时删掉
「之后新出现的、且属于目标 MC 版本」的子目录 —— 属于 = 目录名就是目标版本,或 `<名>.json` 的 `id` / `inheritsFrom` 是目标版本
(Fabric / Forge / NeoForge / Quilt 都继承原版)。**已存在的版本目录不删不改;同一时间段里用户自己另装的不相干版本也不会误删。**
`UpgradeCheckpoint.SnapshotFor` 由 AiAgent 在写工具执行前调用(一行);复制模式不需要(整份新实例目录本来就会被删)。
工具结果 `next_steps` 的说明同步改为「还原 mods 与实例设置,并删掉这次新装的版本目录」。

### 1.5 并行只读工具的线程安全(任务 5)

逐个审查 search_mods / search_modpacks / search_content / search_worlds / search_versions 背后的路径(InProcessApi → Api.SearchCatalog →
CatalogFiles.SearchProjects → Mods.SearchMods / SearchModrinthProjects / SearchCurseforge → DownloadManager.FetchJson → Net;
search_versions → Manifest.ListRemoteVersions → GetVersionManifest):

| 位置 | 问题 | 处理 |
|---|---|---|
| `Api.CatalogCache`(App) | 普通 `Dictionary`,同组并发的搜索工具会同时写 | 换 `ConcurrentDictionary`(一行) |
| `Catalog._modIdx / _packIdx` | 懒加载索引无同步,弱内存模型下可能读到建了一半的字典 | `LazyInitializer.EnsureInitialized`(安全发布) |
| `Manifest.GetVersionManifest` | 缓存过期时并发调用各自联网拉一遍、同时写同一份缓存 | 单飞:拉取段加锁;等锁期间别的线程刚拉完就直接用它的结果(强制刷新也一样) |
| `ToolRunner._alwaysAllowed` | `HashSet` 被界面线程(「一直允许」)与 Agent 线程同时读写 | 加锁(三处访问收进 `IsAllowedAlways / AllowAlways`) |
| `Net` 共享 HttpClient / `CatalogFiles` 别名表 / `Downloader._PATH_LOCKS` / `Java._MAJOR_CACHE` / `Source` 探测缓存 / `SystemInfo` / `ModelCaps` | 已有锁或本身线程安全(HttpClient.SendAsync 线程安全;DownloadManager 每次新建) | 审查通过,未改 |
| `Tools._schemas` / `ToolMeta._map` | 懒加载后只读,最坏情况重复构建一次 | 未改(记在遗留) |

限流:没有全局限流器;同组并发上限仍由 `Scheduler`(默认 4)控制,本包没加新的限流。

### 1.6 联机等待时间(任务 6)

新增 `Ai/MultiplayerTiming.cs`:`DefaultStartWaitMs = 15000` / `DefaultRoomWaitMs = 6000` / `DefaultPollMs = 500` 具名常量,
可用 `config.json` 的 `ai_multiplayer_start_wait_ms` / `ai_multiplayer_room_wait_ms` 覆盖(夹在 0..120000,非法回落默认)。
`ToolRunner.WaitSnapshotAsync` 用 Stopwatch 计实际耗时,每次等待写一行日志
(`multiplayer wait start|room: elapsed N ms / limit M ms, satisfied=…, polls=…`)并进一个 20 条的环形记录 `MultiplayerTiming.Recent()`,
等有真机数据时可据此调默认值。原来的 `ToolRunner.MultiplayerStartWaitMs / RoomWaitMs` 仍可由测试直接赋值(默认 -1 = 走配置)。

## 2. 文件清单

新增:
- `src/TipCL.Core/Launcher/HttpHandlerHook.cs` — 出网处理器包装钩子(测试注入用)
- `src/TipCL.Core/Launcher/EffectiveJvm.cs` — 内存 / GC 实际生效值
- `src/TipCL.Core/Launcher/DnsSrv.cs` — 最小 DNS SRV 客户端
- `src/TipCL.Core/Launcher/Ai/Checkpoint.NewChildren.cs` — 检查点「本轮新建子目录」操作(`Checkpoint` 改为 partial)
- `src/TipCL.Core/Launcher/Ai/NewFeatures/UpgradeCheckpoint.cs` — 原地升级要监视的版本目录
- `src/TipCL.Core/Launcher/Ai/MultiplayerTiming.cs` — 联机等待常量 / 配置 / 实测记录
- `src/TipCL.App/Common/Ai/AiUiPost.cs` — 带「界面还在不在」检查的 Post
- `tests/TipCL.Tests/TestNetworkIsolation.cs` — 测试进程出网闸门 + 打开外部程序的空操作钩子
- `tests/TipCL.Tests/Core/AiStabilityTests.cs` — 20 个用例
- `tests/TipCL.Tests/App/AiStabilityUiTests.cs` — 4 个用例

修改(都是小范围改动,见 §6):
- `src/TipCL.Core/Launcher/Net.cs`(1 行)、`Ai/AiSession.cs`(1 行)— 建 HttpClient 时过 HttpHandlerHook
- `src/TipCL.App/Common/Res.cs` — Bind 对空宿主防御
- `src/TipCL.App/Pages/Ai/AiPage.cs`、`AiFixView.cs` — 回调改走 AiUiPost(各 5~6 行)
- `src/TipCL.App/Pages/Layout/LayoutMotion.cs` — LayoutTween 时钟注入点(4 处)
- `src/TipCL.Core/Launcher/LaunchFlow.cs`(内存 / GC 两行)、`JvmTuning.cs`(Effective 改一行转发)、`Preflight.cs`(内存检查段 + 钩子)、`Ai/NewFeatures/AiPreflight.cs`(内存取值 + memory_source)
- `src/TipCL.Core/Launcher/Ai/NewFeatures/ServerPing.cs`(SRV 解析 + 结果字段)、`Ai/ToolRunner.NewFeatures.cs`(ping_server 三行)
- `src/TipCL.Core/Launcher/Ai/Checkpoint.cs`(`partial` + ApplyOp 开头 4 行)、`Ai/AiAgent.cs`(SnapshotForTool 加 1 行)、`Ai/NewFeatures/NewFeatureEffects.cs`(注释)、`Ai/NewFeatures/InstanceUpgrade.cs`(next_steps 文案)
- `src/TipCL.App/Common/Api.cs`(CatalogCache 类型)、`src/TipCL.Core/Launcher/Catalog.cs`(两处懒加载)、`Manifest.cs`(单飞)、`Ai/ToolRunner.cs`(免确认名单加锁)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.AcctLayoutMp.cs` — 等待时长取值 + 实测记录
- `tests/TipCL.Tests/TestIsolation.cs`(1 行装闸门)、`Layout/LayoutSmoothDragTests.cs`(Host 冻结补间时钟)
- `tests/TipCL.Tests/App/ClickSweepTests.cs` — **不是本包写的**:开工后(11:42)worktree 里出现了一处 SideEffectWords 追加(「主页 / GitHub / issue / 网页 / 浏览器 / 官网 / 链接 / 文档 / 文件夹 / 在编辑器中打开」,注释写「用户 2026-11-21 报告会弹 Edge」),wt-ai2-content / wt-ai2-i18n-ux 里也有同样的改动;本包原样保留并随第一个提交入库,合并时三个分支这两行相同,不冲突。

## 3. 新工具

无。工具表仍 65 个;`ToolMeta` / schema / 分发三处都没加东西。行为有变的已有工具:

| 工具 | 风险 / 只读 | 变化 |
|---|---|---|
| `ping_server` | read(network)/ low / 只读 | 没写端口的域名先查 SRV;结果多 `resolved_via / connect_host / connect_port / srv` |
| `preflight_check` | read / low / 只读 | 内存按生效值比物理内存;结果多 `memory_source` |
| `apply_instance_upgrade` | write_local / high / 写 | 原地模式撤回时连带删掉本次新装的版本目录 |

## 4. 测试与验收(本机实测)

- 新增 xunit **24** 个:`EffectiveJvmTests` 4、`DnsSrvTests` 3、`ServerPingSrvTests` 5(本机 UDP 假 DNS + TCP 假 SLP 服务器)、`UpgradeCheckpointTests` 3、
  `MultiplayerTimingTests` 2(假门面)、`ParallelReadToolsStressTests` 3(假 HTTP:24 路并发目录搜索、8 路并发拉版本清单只拉 1 次、
  12 个只读工具经 Scheduler 并发走真实 InProcessApi)、`AiStabilityUiTests` 4(Res.Bind 空宿主、AiUiPost 窗口关后丢弃、
  AI 请求被闸门当场答复、AI 页点建议问题失败气泡在本用例内落地)。
- Debug `--no-incremental` 构建:0 错误,警告只有基线那 3 条 `LayoutMotionAndHeightTests` xUnit 警告。
- 全量 `dotnet test` 连跑 3 次:**3 次都是 1145 通过 / 7 跳过 / 0 失败,共 1152**(= 基线 1128 + 本包 24;耗时 1m27s / 1m38s / 4m07s,第 3 次机器上另有 4 个代理在构建)。
- `ClickSweepTests|DlgCloseTests` 循环 8 次:基线 4/8 失败 → 本分支 0/8;`LayoutSmoothDragUiTests` 单跑 10 次:基线 1/10 失败 → 本分支 0/10。
- `--ai-selftest`:exit=0,通过 91 / 失败 0 / 跳过 0(与基线项数相同)。
- i18n 守卫:未接线 CJK 字面量 3540 → 3540(与 main 相同,基线文件 3605 未改),接线数 2222 不变;本包新加的字符串都是英文日志 / 协议常量,没有新增 `i18n:ignore`。

## 5. 遗留问题与风险

1. `Checkpoint.CheckpointsDir` / `Artifacts.ArtifactsDir` / `Store.SessionsDir` 等是进程级静态,`AiServiceDirs` 夹具在**并行**的测试类里来回改它们 ——
   这是另一类潜在的偶发失败源(快照写进 A 目录、回滚去 B 目录找)。本轮连跑没有观测到,没有改;要根治可把这些目录改成 AsyncLocal 覆盖或把用到它们的类收进同一个串行集合。
2. 打开外部程序的入口没有统一:AccountPage / MultiplayerPage / SettingsPage.Cards / MdView / PluginsPage / PluginModels / PluginHostPage / DangerousWidgets / LibraryPage
   各自 `Process.Start(UseShellExecute)`。测试里只有经 `Crash.ShellOpen` / `Auth.OpenBrowser` 的两条被本包设成空操作,其余靠 ClickSweep 的跳过词表;
   建议后续收成一个 `Shell.Open(url)` 门面 + 测试钩子,ClickSweep 就不用维护词表。
3. 原地升级的「新建版本目录」判定按目录名 / `id` / `inheritsFrom` 认目标版本;OptiFine 这类不继承原版的独立 json 认不出来,撤回时不会删(宁可留下也不误删)。
   libraries / assets 是多版本共享缓存,撤回不清理。安装是后台任务:撤回时若安装还在跑,删掉的是半成品目录,安装任务随后会报错。
4. SRV:只走 UDP,应答被截断(TC)时按「没有记录」回落,不改用 TCP 重查(SRV 应答远小于 512 字节,实际不会遇到);没有做 DNS 缓存,每次 ping 都查一次;
   没有在真网络上验证过(测试全部是本机假 DNS)。Windows 上读到的是各网卡 DNS 的并集,第一台不通会等到它的超时份额用完再问下一台。
5. `Tools._schemas` / `ToolMeta._map` 等「懒加载后只读」的字段未加同步(最坏重复构建,结果相同)。
6. 联机默认等待时长仍是估计值,只是现在有实测日志可依据,没在真网络 + 第二台机器上跑过(PARITY §10.4 那条不变)。
7. `Res.Bind` 在宿主为空时给的是「未设置」绑定,之后即便 Application 恢复也不会自动重新订阅(那种控件本来就要被丢弃)。

## 6. 合并注意(冲突点)

- `AiPage.cs` / `AiFixView.cs`:构造里 runner 初始化块的三行回调、`RunAgentAsync` / 修复会话的两处 `Dispatcher.UIThread.Post` → `AiUiPost.Post(this, …)`;其它包若也改这几行,保留双方语义、统一用 AiUiPost。
- `ToolRunner.cs`:`IsAllowedAlways` / `AllowAlways` 两个成员与 `RunAsync` 里两行(`_alwaysAllowed` 访问)。
- `AiAgent.cs`:`SnapshotForTool` 里 `IsWriteTool` 判断之后一行。
- `Checkpoint.cs`:类声明加 `partial`、`ApplyOp` 开头 4 行。
- `ToolRunner.NewFeatures.cs`:`ping_server` 分支三行;`ServerPing.PingAsync` 签名末尾多一个可选参数 `resolveSrv`(位置参数调用不受影响)。
- `ToolRunner.AcctLayoutMp.cs`:三个静态字段的默认值(`MultiplayerStartWaitMs / RoomWaitMs` 现在默认 -1)、`WaitSnapshotAsync` 签名多一个可选参数。
- `LaunchFlow.Prepare` 内存 / GC 两行,`JvmTuning.Effective` 方法体,`Preflight.CheckLaunch` 内存段,`AiPreflight.Check` 内存取值。
- `Api.cs` 一行(`CatalogCache`),`Catalog.cs` 两处,`Manifest.cs` `GetVersionManifest`。
- `LayoutMotion.cs` 的 `LayoutTween`(时钟字段 + 3 处取时)。
- `TestIsolation.cs` 末尾一行;`ClickSweepTests.cs` 那两行词表与别的分支相同。

## 7. 合并后要改的文档行(本包不改 README / PARITY / DECISIONS / PROGRESS)

- `docs/PARITY.md` §10.2:「ping_server(自实现 Server List Ping,不解析 SRV)」→「ping_server(自实现 Server List Ping;地址没写端口时查 `_minecraft._tcp` SRV,查不到回落 A 记录 + 25565)」。
- `docs/PARITY.md` §10.4「联机(multiplayer)只用假接口测了流程…」后补:「等待时长已改为具名常量,可用 `ai_multiplayer_start_wait_ms` / `ai_multiplayer_room_wait_ms` 覆盖,每次等待的实际耗时写日志」。
- `progress/AI-perf-saves.md` §5 第 3 条(「Preflight.CheckLaunch 仍用调用方传入的内存」)已解决:三处共用 `EffectiveJvm.Resolve`。
- `progress/AI-new-features.md` 遗留第 3 条(ping 不解析 SRV)与第 4 条(原地升级撤回残留新版本目录)已解决。
- `progress/AI-engine-mcp.md` 第 90 行(并行只读工具「真源没有压测」)与第 95–97 行(DlgClose / LayoutSmoothDrag 偶发红)已解决,见本文 §1.1 / §1.5。
- `DECISIONS.md` 建议追加一条 D-82:测试进程出网闸门(`HttpHandlerHook` + `TestNetworkIsolation`:AI 只放行环回、其余回 400)、界面回调一律走 `AiUiPost`、
  「实际生效内存只算一处(EffectiveJvm)」、检查点新增「只记名单不存内容」的新建子目录操作及其判定口径(目录名 / id / inheritsFrom)。
