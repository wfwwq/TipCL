# AI3-moddev:模组测试环境遗留

分支 `ai3/moddev`(worktree `wt-ai3-moddev`,基于 main@b6df4bb)。来源:docs/PARITY.md §11.4 第 2 条、progress/AI2-moddev-mcp.md §6(第 1、2、3、6 条)、docs/MCP接入指南.md §6。

## 1. 做了什么

### 1.1 测试启动只关自己拉起的进程(允许多开不误关)
- 新增 Core `LaunchedGames`(`src/TipCL.Core/Launcher/Ai/ModDev/LaunchedGames.cs`):按**启动任务 task_id → 游戏进程**登记,线程安全;
  `StopTask(taskId)` 只杀那一个(`Kill(entireProcessTree:true)`),登记表里没有就 `not_tracked`、**什么也不杀**;`StopPid` 只认登记过的 PID。
  进程抽象 `ITrackedGameProcess`(产品 `SystemGameProcess` 包 `System.Diagnostics.Process`,测试用假进程)。
- `LaunchCore.RunAsync`(App)拉起游戏进程后按 `progress.TaskId` 登记、退出后注销;登记时发现启动任务已被取消 ⇒ 立刻关掉刚起的进程
  (测试启动超时 / 用户取消时不留一个没人管的游戏)。原来的 `_gameProc` / `stop_game` / `is_game_running` 行为不变。
- 默认驱动 `BackendLaunchDriver` 关游戏**不再调无参 `stop_game`**(那关的是「当前游戏」,多开时可能是用户那一局),改成
  ① `cancel_task {id}` 取消这次的启动任务(游戏进程还没起来时就不会再起;起来了的被关后任务记「已取消」而不是崩溃);
  ② `LaunchedGames.StopTask(task_id)` 只关这次拉起的进程。探测结果带 PID(`TestLaunchProbe.Pid`)。
- `run_test_launch` 结果新增 `game_pid`(知道时)与 `stop_result`(`stopped` / `already_exited` / `not_tracked` / `kill_failed`,TipCL 去关了才有)。
  `ITestLaunchDriver` 新增带默认实现的 `DescribeStop`(老的假驱动不用改)。

### 1.2 模组测试任务记录落盘
- `ModDevTasks` 把任务表写到 `<数据目录>/cache/moddev_tasks.json`(`{version:1, tasks:[…]}`;数据目录随 `TIPCL_HOME`):
  最近 50 条(`MaxTasks`,内存与盘同一上限,没结束的不丢),每条留最后 40 行日志 + 完整 result;写法是 `Utils.WriteJson`(临时文件 + 原子替换)。
- 落盘时机:任务开始、结束(**先落盘再唤醒等待方**)、步骤变化(最多每 2 秒一次)。
- 第一次访问时读回(`_loadGate` 保证读完之前没有人拿空表去写盘);上一个进程里 `running` 的记录读回为 **`interrupted`**
  (finished=true、success=false、`error_detail` 说明启动器重启过)并立即写回;读回的记录带 `restored: true`。坏文件 / 读不出 ⇒ 当没有记录,照常工作。
- `get_task_status` 的 not_found 文案改为说明「最近 50 条、重启后仍可查;install_* / launch_game 的启动器任务只在本次运行期间可查」。
- 结果新增 `started_at` / `finished_at`。测试用 `ModDevTasks.ReloadForTests()` 模拟重启。

### 1.3 「进入主菜单」特征行表
- 新增 `ReadyMarkers`(`src/TipCL.Core/Launcher/Ai/ModDev/ReadyMarkers.cs`):代码常量表,每条 = 加载器(`*` = 全部)× MC 版本段 × 子串 × 来源 × 是否推断:
  | 加载器 | 版本段 | 特征 | 来源 | 推断 |
  |---|---|---|---|---|
  | * | 全部 | `Sound engine started` | 原版 SoundManager(1.7–1.12)/ SoundEngine.loadLibrary(1.13+)固定输出,公开 latest.log 样例 | 是(比标题画面略早) |
  | * | ≤1.12.2 | `OpenAL initialized` | paulscode SoundSystem LibraryLWJGLOpenAL 的 STDOUT | 否 |
  | * | ≥1.13 | `OpenAL initialized on device` | com.mojang.blaze3d.audio.Library | 是(起始小版本没逐版核对) |
  | * | 全部 | `Error starting SoundSystem. Turning off sounds & music` | 打不开声卡时的固定输出(无声卡也进标题画面) | 是 |
  | forge | 1.7–1.12.2 | `Forge Mod Loader has successfully loaded` | FML Loader.initializeMods 末尾 | 是 |
  Fabric / Quilt / 新版 Forge / NeoForge 没有「已进入标题画面」的固定输出,它们自己的早期输出(`Loading N mods:`、`ModLauncher running`、
  `Loading ImmediateWindowProvider fmlearlywindow`、`Launching target 'forgeclient'`)作为反例写进测试,全部沿用原版行。
- `TestLaunch` 按实例的加载器(`ModDevOps.InstanceLoader`)与 MC 版本(`InstanceMc`)取适用条目;认不出加载器 / 版本(快照号)就放宽到整张表。
  `ready_pattern` 给了就整张表让位(原行为)。`TestLaunch.MainMenuMarkers` 保留,改为由表生成;结果的 `ready_markers` 是这次实际用的那几条。

### 1.4 依赖回落 CurseForge
- `ModDevHost.DependencySources`:依赖来源链,默认 `[ModrinthDependencySource, CurseForgeDependencySource]`(`IDependencySource`)。
  Modrinth 用 `Mods.InstallModFromSource`;CurseForge 复用 `Mods.SearchCurseforge(slug=…)` + `Mods.InstallCurseforgeMod`(与下载页同一套函数),纯数字当项目 id。
- Modrinth 找不到 **或出错** 都试 CurseForge;两边都没有 ⇒ `DependencyInstallException`,依赖行 `ok=false`、`error` 写清每边结果
  (全是找不到时写「Modrinth、CurseForge 上都找不到依赖「x」」),`tried` 是每个来源的 `{source, not_found, error}`;成功行带 `source`。
- 写法 `curseforge:<slug|id>` / `cf:` / `modrinth:<slug>` / `mr:` 只查一边。依赖装进 `ModDevOps.ModsDirOf(inst)`(与 install_local_mod、启动链同一口径;原来固定 `<实例>/mods`,版本隔离时不对)。
- 老注入点 `DependencyInstaller` 保留(设了就不走来源链,source = custom)。`ModDevHost.Reset()` 也清 `DependencySources`。
- schema:create_test_instance 的 dependencies 描述、run_test_launch 描述(只关自己拉起的进程)、get_task_status 描述(重启后可查 / interrupted)已更新。

### 1.5 文档
- **docs/MCP接入指南.md**(公共任务书允许的例外,只改了 §5 第 1 步与第 4 步 ready 一行、§6 已知限制):§6 改为现状 —— 特征行表与 ready_pattern、只关自己拉起的进程(game_pid / stop_result)、依赖回落 CurseForge、任务记录落盘与 interrupted、仍用查询工具。

## 2. 文件清单
- 新增:`src/TipCL.Core/Launcher/Ai/ModDev/LaunchedGames.cs`、`src/TipCL.Core/Launcher/Ai/ModDev/ReadyMarkers.cs`、`tests/TipCL.Tests/Core/ModDevAi3Tests.cs`、`progress/AI3-moddev.md`
- 修改:`src/TipCL.Core/Launcher/Ai/ModDev/{ModDevHost,TestLaunch,ModDevOps,ModDevSchemas}.cs`、`src/TipCL.App/Common/Launch/LaunchCore.cs`(+10 行)、`docs/MCP接入指南.md`
- 没碰:`windos/`、版本号、ToolMeta / Tools.cs / ToolRunner(没加新工具,工具总数仍 78)、locales、PROGRESS / DECISIONS / README / PARITY。

## 3. 新增测试(28 条,`tests/TipCL.Tests/Core/ModDevAi3Tests.cs`,全部不联网、不真启动游戏)
- `LaunchedGamesTests`(3):登记表只关指定任务的进程、未登记的 PID / 任务一律不碰、注销只认同一进程;
  默认驱动 + 模拟进程的多开场景两条:进主菜单后只关测试那个(用户那一局 `Killed=false`、没调 `stop_game`、取消了启动任务、`game_pid=4242`、`stop_result=stopped`);
  游戏进程还没起来就超时(`stop_result=not_tracked`,谁都没杀,启动任务被取消)。
- `ModDevTaskPersistenceTests`(3,临时 TIPCL_HOME):结束的任务重启后仍可查(result / log_tail / restored);在跑的任务重启后为 interrupted 并写回盘、
  旧进程里的任务后来结束也不改写;50 条上限 + 不留 .tmp + 坏文件忽略后照常起新任务。
- `ReadyMarkerTableTests`(19):Vanilla / Fabric / Quilt / Forge / NeoForge 各至少一正一反(Theory 16 条,含版本段反例:旧 FML 行对 1.20.1 不算、
  老 OpenAL 行对 1.20.1 不算)、表的来源 / 版本段 / 放宽规则、ready_pattern 覆盖整张表、旧版 Forge 实例经 run_test_launch 用 FML 行判 ready。
- `ModDevDependencyFallbackTests`(3,假源):Modrinth 没有 ⇒ CurseForge、两边都没有如实报错且列出两边;Modrinth 网络错误也回落;前缀只查一边;默认来源链顺序与写法解析。

## 4. 验收数字
见文末「验收」小节(由 `_verify_ai3/moddev/verify.ps1` 实测)。

## 5. 遗留(如实)
- 特征行表仍没在真游戏上逐版核对(按要求不真启动);标了推断的几条最需要真机补核。`Sound engine started` 比标题画面略早,
  在标题画面 init 时才崩的模组会被判 ready(可考虑以后加「命中后再观察几秒」,本包没做,怕改变现有超时 / 成功语义)。
- 只能管 TipCL 自己(LaunchCore)拉起的进程;`launch_game` 之外的入口不经过登记表。`stop_game`(无参)本身没改,其它调用方(界面「结束游戏」等)仍是关当前游戏。
- 两个 TipCL 共用一个数据目录同时运行时,后启动的会把前一个正在跑的模组测试任务读成 interrupted(单实例使用不受影响)。
- `install_*` / `launch_game` 的启动器任务(TaskStore)仍只在内存里,不在本包范围。
- Modrinth「找不到」与「出错」是按报错文字粗判的(`not_found` 字段),判错也不影响回落,只影响报错措辞。
- CurseForge 回落依赖 CurseForge API(内置或设置里的 key);网络受限时可能两边都失败(会如实报错)。

## 6. 合并冲突点
- `src/TipCL.App/Common/Launch/LaunchCore.cs`:`lock (_gameLock) _gameProc = gp.proc;` 之后插入 9 行登记,`finally` 里 `_gameProc = null` 之后 1 行注销。别的包若改这两处,两边都留。
- `ModDevHost.cs` 基本重写(新增来源链 / 落盘),别的包一般不碰;`TestLaunch.cs` 改了 BackendLaunchDriver、RunAsync、FindReady、Finish 签名(私有)。
- `ModDevSchemas.cs` 三段描述文字;`docs/MCP接入指南.md` §5 第 1、4 步与 §6。

## 7. 要改的文档行(本包不改,交主线)
- docs/PARITY.md §11.4 第 2 条整条改为已处理:「『进入主菜单』特征行改为按加载器 × 版本段的可配置表(ReadyMarkers,来源 / 推断已标注,仍待真机核对);测试启动只关自己拉起的进程(LaunchedGames,按启动任务 PID);模组测试任务记录落盘 cache/moddev_tasks.json(重启后可查,未完成的记 interrupted);create_test_instance 依赖 Modrinth 找不到回落 CurseForge」,并指向本文件。
- DECISIONS.md 建议新增一条(D-83 或主线编号):「run_test_launch 关游戏只按启动任务 id 找 TipCL 自己登记的进程,登记表里没有就不关,绝不退回无参 stop_game;模组测试任务记录落盘 cache/moddev_tasks.json,上限 50,重启前未结束的读回为 interrupted;依赖来源链 Modrinth → CurseForge,前缀可钉单一来源」。
- progress/AI2-moddev-mcp.md §6 第 1、2、3、6 条可标为已由本包处理。
- README「🤖 AI 助手」模组测试一句可补:「依赖可回落 CurseForge;测试启动只关自己拉起的游戏;任务记录重启后可查」。

## 验收
实测(`_verify_ai3/moddev/verify.ps1`,代码 commit 3a484b8;之后的 commit 只改文档):
- `dotnet build TipCL.Linux.sln -c Debug --no-incremental`:exit 0,0 错误;警告只有基线那 3 条 `LayoutMotionAndHeightTests` 的 xUnit 警告(xUnit1013 ×2、xUnit2000 ×1)。
- 全量测试:**1386 通过 / 7 跳过 / 0 失败(共 1393 = 基线 1365 + 新增 28)**;模组相关过滤集(ModDev / LaunchedGames / ReadyMarker / TestLaunch / McpModDev / ModJarInfo)81 条另跑过 2 次均全绿。
- `--ai-selftest`(临时 TIPCL_HOME):exit 0,**通过 108 / 失败 0 / 跳过 0**。
- `git diff b6df4bb HEAD -- windos` 为空;git status 干净。工具总数仍 78;没加界面文案 / locales 键,i18n 基线不变。
