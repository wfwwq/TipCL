# AI3-stability:稳定性遗留(分支 `ai3/stability`,基于 main@b6df4bb)

> 第三轮并行包之一。来源:docs/PARITY.md §11.4 第 5 条、progress/AI2-stability.md §5(第 1、3、4 条)。
> 只改 Linux/Avalonia 主线(`src/`、`tests/`);`windos/` 未动(`git diff b6df4bb HEAD -- windos` 为空)。
> **没有新增 AI 工具**(工具表仍 78 个),没有新增界面文案(新加的字符串都是英文协议 / 诊断行),没加 NuGet 依赖。

## 1. 做了什么

### 1.1 原地升级撤回:按「执行前后版本目录集合的差集」删(任务 1)

原来(AI2-stability):执行前记下 `versions/` 已有的子目录,撤回时删「新出现的、且**属于目标 MC 版本**」的子目录,
属于 = 目录名 / `<名>.json` 的 `id` / `inheritsFrom` 是目标版本。OptiFine 合并成独立 json、改过名的整合目录、很老的 Forge
(只有 `jar` 字段)都认不出来,撤回后留下几百 MB。

现在(口径写在 `Checkpoint.NewChildren.cs` 文件头):

1. **主口径 = 差集**。`AiAgent.ExecuteOneAsync` 在写工具执行**完**(成功 / 失败 / 取消都走 `finally`)后调
   `UpgradeCheckpoint.CaptureAfter` → `Checkpoint.CaptureNewChildren`,把 `created = 执行后 − 执行前` 补进同一条名单。
   写工具永远单独成组执行,这段时间新出现的目录就是这次升级装出来的 —— **不管目录叫什么、json 继不继承原版**。
   撤回只删 `created` 里还在的;执行完之后用户自己装的版本(哪怕也是 1.21.1 的 Fabric)不在 `created` 里,不碰。
2. `install_game` 转成后台任务(结果里有 `task_id`,目录要等任务跑完才出现)时记 `pending_install=true`:撤回额外删
   「之后才出现、且属于目标版本」的目录。Linux 主线的 InProcessApi 是同步装完才返回,正常走不到这条。
3. 没来得及补记(执行中进程被杀 / 旧流水):退回「新出现 + 属于目标版本」。`BelongsToMc` 同时扩展为:目录名就是目标版本;
   json 的 `id / inheritsFrom / jar / clientVersion / patches[id=game].version` 是目标版本;`inheritsFrom` 顺着兄弟目录一路追到目标版本
   (OptiFine 装在 Forge 上,最多 6 层防环);目录名 / id 里以完整版本号形式含目标版本(`1.21.1-OptiFine_HD_U_J1` 算,`1.21.10`、`1.21.1.2` 不算);
   目录名改了、json 名没改时,目录里唯一的 json 也认。

已存在的版本目录永远不删不改(三种口径都先排除 `existing`)。

### 1.2 改全局目录 / 静态配置的测试类:收进串行集合 + 守卫(任务 2)

**先查清现状**:`tests/TipCL.Tests/AssemblyInfo.cs` 早有 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
—— 整个测试程序集本来就是串行的。所以 AI2 §5 第 1 条说的「AiServiceDirs 在**并行**的测试类里来回改」在当前 main 上**不成立**,
默认模式下复现率是 0(见 §4.2 第一行)。但这层保护只靠程序集级的一行:有人为了提速把它去掉(或 CI 传
`xUnit.ParallelizeTestCollections=true`),这些类马上互相踩 —— 实测**10/10 次红,每次 24~60 个用例**(§4.2 第二行)。

所以做了「真正落到类上」的隔离,不再依赖那一行:

- 新增 `tests/TipCL.Tests/GlobalStateCollection.cs`:
  - 串行集合 `GlobalStateCollection`(`DisableParallelization = true`:集合内彼此串行,且整个集合在所有可并行的测试跑完后才跑,不与任何类同时执行);
  - `GlobalStateGuardTests`:**扫测试程序集的 IL**(含闭包、async 状态机、嵌套类型、测试程序集里的辅助类,按调用图求不动点),
    凡是直接或间接 ① 给产品程序集(TipCL.Core / TipCL.App)的静态字段赋值、调静态属性 setter;② 调 `Utils.SetRoot`、
    `LauncherConfig.Set / Update / Save / Load`、`Config.Reset`、`Environment.SetEnvironmentVariable`、`AiSelfTest.VerifyAsync` / `AiSelfTestCli.Run`
    (后两个在产品里临时换 Checkpoint / Artifacts / Trace / Store / Hooks 目录);③ 改 `Config.CONFIG.data` 的测试类,
    必须挂在某个 `DisableParallelization` 集合上(本集合,或已有的「TipCL 真机交互回归」「AI2 DNS 注入」)。漏挂直接红,并列出类名与第一处改动点
    (设 `TIPCL_GLOBAL_STATE_DUMP=<文件>` 可把完整清单写出来)。
- 扫出 **85** 个改全局状态的测试类:其中 14 个原本就在串行集合里(「TipCL 真机交互回归」13 个、「AI2 DNS 注入」1 个),2 个是本包新增的,
  其余 **69 个**加了一行 `[Collection(TipCL.Tests.GlobalStateCollection.Name)]`
  (用例体一行没改;类名清单见 commit 8cc4846)。
- `AiServiceDirs`(Checkpoint / Artifacts / Trace / Store 的临时目录夹具)加了运行时防线:两个实例同时存活 = 有人在并行改同一套全局目录,
  当场抛 `InvalidOperationException`(说明要挂哪个集合),不再变成「快照写进 A、回滚去 B 找」那种红在无辜用例上的偶发失败。

选「收进集合」而不是「改成 AsyncLocal 真隔离」的原因:这些目录被 Avalonia 无头会话里的 `Dispatcher.Post` 回调、
`Task.Run` 后台续体读,AsyncLocal 的值不一定跟得过去(AI2 的 DlgClose 偶发正是这种晚到回调);改成属性还要动 5 个产品类的 6 个公开字段。
集合方案零产品代码改动、行为可预期,由守卫保证不回退。

### 1.3 SRV 真网络核验:`--srv-check`(任务 3)

`dotnet TipCL.App.dll --srv-check [--dns <ip[:port]>]... [--timeout <ms>] [域名 | _service._proto.name]...`(不起 GUI,只发 DNS 查询,不连游戏服务器)。
默认问 4 个已知有 `_minecraft._tcp` 记录的域名 + 一个通用 SRV(`_xmpp-server._tcp.jabber.org`),对「系统 DNS + 8.8.8.8 + 1.1.1.1」每台单独查,
再按 ping_server 的真实路径(`DnsSrv.ResolveAsync`,系统 DNS)解析一遍。退出码 0 = 至少拿到一条记录;3 = 一条都没拿到(没网 / UDP 53 被拦);4 = 参数错。
新增 `DnsSrv.QueryServerAsync`:只问一台服务器,状态分成 ok / no_record / rcode=N / truncated / timeout / error(ping_server 仍走原来的 ResolveAsync,行为不变)。
单元测试只对本机 UDP 假 DNS(`SrvCheckCliTests`),真网络那一半不进单元测试。

**真网络结果(2026-11-21 15:25,本机,Windows 10 19045)**:20/20 次查询全部拿到记录,与 dns.google DoH(`https://dns.google/resolve?type=SRV`,同日从另一网络查)给出的值逐字一致:

| SRV 名 | 结果(优先级 权重 端口 目标) | 系统 DNS 192.168.5.1 | 系统 DNS 223.5.5.5 | 8.8.8.8 | 1.1.1.1 | ping_server 路径 |
|---|---|---|---|---|---|---|
| `_minecraft._tcp.hypixel.net` | 1 1 25565 mc.hypixel.net | 17 ms | 1130 ms | 66 ms | 204 ms | mc.hypixel.net:25565 |
| `_minecraft._tcp.mineplex.com` | 1 1 25565 java.mineplex.com | 152 ms | 602 ms | 65 ms | 210 ms | java.mineplex.com:25565 |
| `_minecraft._tcp.gommehd.net` | 0 18 25565 mc.gommehd.net | 88 ms | 1111 ms | 217 ms | 220 ms | mc.gommehd.net:25565 |
| `_minecraft._tcp.2b2t.org` | 10 10 25565 connect.2b2t.org | 10 ms | 646 ms | 64 ms | 231 ms | connect.2b2t.org:25565 |
| `_xmpp-server._tcp.jabber.org` | 30 30 5269 scarlet.jabber.org | 1337 ms | 365 ms | 498 ms | 517 ms | scarlet.jabber.org:5269 |

另用 `--dns 8.8.8.8` 单独问了一个不存在的名字 `_minecraft._tcp.no-such-srv-ai3.example.com` → `no_record`(62 ms),回落路径正常。
观察:本机网卡上的第二台系统 DNS(223.5.5.5)比第一台慢一个数量级(0.4~1.3 s)。`ResolveAsync` 依次问、第一台有记录就停,本机不受影响;
但若第一台不通,AI2 §5 第 4 条说的「等它的超时份额用完再问下一台」会叠加上这种慢服务器的延迟(本包未改这部分行为)。

### 1.4 偶发失败巡检(任务 4)

全量测试连跑 5 次(后台隐藏,机器上同时有其它代理在构建),共跑了两轮:

| 轮次 | 提交 | 5 次结果 |
|---|---|---|
| 第 1 轮 | 80961a8 | **第 1 次红 1 个**,其余 4 次 1368 通过 / 7 跳过 / 0 失败 |
| 第 2 轮(修后) | 02ba0ef | **5/5** 次 1368 通过 / 7 跳过 / 0 失败 |

红的那一个:`UpdateTargetIsolationTests.UpdateStaged_ReachingALeakedShellWindow_ReplacesOnlyInsideTheIsolationRoot`,
`System.IO.IOException: The process cannot access the file '…\update-target\TipCL.App' because it is being used by another process`,
抛在用例自己的轮询 `File.ReadAllText(TestWriteGuard.UpdateTargetSandbox)` 上(第 121 行)。
根因:更新器在另一个线程上「复制到 staged → `File.Move(staged, target, overwrite)`」,用例每 20ms 用不带 `FileShare.Delete`
的 `ReadAllText` 读同一个文件 —— 恰好撞上替换就抛;反过来读句柄还开着时覆盖也会失败。与本包的改动无关(纯时序竞争,
产品代码没有问题;不能完全排除「类被收进串行集合后执行顺序变了、邻居不同」让它更容易撞上)。
修法(只改测试):轮询改用 `ReadShared` —— `FileShare.ReadWrite | FileShare.Delete` 打开,`IOException` / `UnauthorizedAccessException`
当作「还没写好」再等一轮。修后第 2 轮 5/5 绿;另把这个类连同会写同一沙箱的 `SelfUpdateFeedbackTests` 单跑 10 次:**10/10 绿**(7 个用例)。这条竞争本来就是低概率(修前的 5 次全量里只中 1 次),10 次单跑只能说明没有引入新问题,真正的依据是上面的根因分析。

## 2. 文件清单

新增:
- `src/TipCL.App/Session/SrvCheckCli.cs` — `--srv-check` 实现
- `tests/TipCL.Tests/GlobalStateCollection.cs` — 串行集合 + IL 守卫(2 个用例)
- `tests/TipCL.Tests/Core/AI3StabilityTests.cs` — 8 个用例(见 §4)

修改:
- `src/TipCL.Core/Launcher/Ai/Checkpoint.NewChildren.cs` — `CaptureNewChildren`、三种撤回口径、`BelongsToMc` 扩展、`HasVersionToken`
- `src/TipCL.Core/Launcher/Ai/NewFeatures/UpgradeCheckpoint.cs` — `CaptureAfter` / `InstallWentBackground`
- `src/TipCL.Core/Launcher/Ai/AiAgent.cs` — `ExecuteOneAsync` 改 try/finally + `CaptureAfterTool`(新私有方法)
- `src/TipCL.Core/Launcher/DnsSrv.cs` — 新增 `QueryServerAsync`(原有方法未改)
- `src/TipCL.App/Program.cs` — `--srv-check` 分支(在 `--ai-selftest` 之后、CLI 解析之前)
- `tests/TipCL.Tests/Core/AiServicesTests.cs` — `AiServiceDirs` 重叠即抛
- `tests/TipCL.Tests/App/UpdateTargetIsolationTests.cs` — 轮询读沙箱改用 `ReadShared`(巡检发现的偶发红,见 §1.4)
- 50 个测试文件各加 1~4 行 `[Collection(TipCL.Tests.GlobalStateCollection.Name)]`(共 69 行,未改用例体)

## 3. 新工具

无。行为有变的已有工具:`apply_instance_upgrade`(write_local / high)—— 原地模式撤回时按差集删版本目录(见 §1.1);风险等级、确认链、「本对话都允许」不给的设置都没变。

## 4. 测试与验收(本机实测)

新增 xunit **10** 个:`UpgradeCheckpointDiffTests` 5(差集删 8 种形态且不碰执行后才装的;名单记录与只补记一次;
没补记时的兜底认出 9 种 OptiFine / Forge / 合并 json 形态、不误删 3 种不相干目录;后台安装;辅助函数)、
`UpgradeCheckpointAgentTests` 1(AiAgent 真跑一轮 apply_instance_upgrade:假网关 + 假门面 install_game 当场造 8 种目录 → 撤回全删、之后装的保留)、
`SrvCheckCliTests` 2(参数 / 本机假 DNS 的 ok / rcode=3 / timeout / 无记录退出码 3)、`GlobalStateGuardTests` 2。

### 4.1 验收数字(在 02ba0ef 上实测,`_verify_ai3/stability/final.ps1`,日志在 `final2/`)

- Debug `--no-incremental` 构建:exit=0,0 错误;警告只有基线那 3 条 `LayoutMotionAndHeightTests` 的 xUnit 警告(xUnit1013 ×2、xUnit2000 ×1)。
- 全量 `dotnet test` 连跑 5 次:**5 次都是 1368 通过 / 7 跳过 / 0 失败,共 1375**(= 基线 1365 + 本包 10;耗时 2m03s / 1m34s / 2m04s / 1m37s / 2m05s)。
- `--ai-selftest`(临时 TIPCL_HOME):exit=0,**通过 108 / 失败 0 / 跳过 0**(与基线项数相同)。
- i18n:守卫测试在全量里通过,基线文件未改;本包 `src/` 里新加的字符串都是英文诊断 / 协议常量,没有新增中文字面量,没有新增 `i18n:ignore`。
- `git diff b6df4bb HEAD -- windos` 为空;`git status` 干净。
- 验收中没有弹任何窗口:构建 / 测试 / 自检 / `--srv-check` 全部 `Start-Process -WindowStyle Hidden` 后台跑,没有启动 Minecraft 或 TipCL 界面。

### 4.2 改全局状态的测试类:修改前后复现率(相关 47 个类 / 388 个用例,各连跑 10 次)

| 模式 | 基线 b6df4bb | 本分支 |
|---|---|---|
| 默认(程序集级已串行) | **0/10** 次红 | **0/10** 次红 |
| 强制并行 `-- xUnit.ParallelizeTestCollections=true`(模拟去掉程序集级串行) | **10/10** 次红,每次 24 / 34 / 36 / 37 / 38 / 39 / 40 / 42 / 46 / 60 个失败 | **0/10** 次红 |

基线强制并行时红得最多的类(10 次累计):CheckpointTests 25、AiRetryTests 25、RewindTests 23、StoreTests 23、ArtifactsTests 17、
AiI18nUxEnglishUiTests 11、AiModpackTests 10、UiPrefsTests 10、AiContentTests 8、TestLaunchTests 8、AiToolsTests 7、FeedbackTests 6、
AiPermission*Tests 16、AiAgentParallelToolsTests 4……,连 AI2 的 `UpgradeCheckpointTests` 也红过 1 次 —— 正是「快照写进 A、回滚去 B」那一类。
过滤条件与脚本:`_verify_ai3/stability/filter_globals.txt`、`loop.ps1`(日志在 `base-globals/`、`base-par/`、`after-globals/`、`after-par/`)。

## 5. 遗留问题与风险

1. 原地升级:写工具执行期间用户若在界面上**同时**手动装了别的版本,它也会落进差集,撤回时一起删(窗口只有工具执行的那几十秒;写工具之间不会重叠)。
   libraries / assets 仍是多版本共享缓存,撤回不清理(与 AI2 相同)。
2. IL 守卫只看「测试代码自己(或经测试程序集的辅助方法)做的改动」;产品方法内部再去改全局状态的,只认得显式列出的 `AiSelfTest.VerifyAsync` /
   `AiSelfTestCli.Run`。以后产品里再加这类「临时换全局目录」的入口,要把它补进 `MutatorMethods`。
3. 串行集合里的类在程序集级串行被去掉之后也会排到并行阶段之后才跑,全量耗时基本不变;本包没有去掉程序集级串行(那一行还挡着 UI 资源字典的并发问题,见 AssemblyInfo.cs 注释)。
4. `--srv-check` 只走 UDP,与 ping_server 一致;TC 截断时仍按没有记录处理(真网络里 5 个名字的应答都远小于 512 字节,未遇到)。

## 6. 合并注意(冲突点)

- `AiAgent.cs`:`ExecuteOneAsync` 末尾两行改成 try/finally,后面新增私有方法 `CaptureAfterTool`;别的包若也改 `ExecuteOneAsync`,保留 finally 里的那一行。
- `Checkpoint.NewChildren.cs` / `UpgradeCheckpoint.cs`:整块重写 / 追加,只有本包动。
- `Program.cs`:`--ai-selftest` 分支后插了 3 行。
- `DnsSrv.cs`:`AskAsync` 之前插入一个新方法。
- 50 个测试文件:每个类声明上方多一行 `[Collection(TipCL.Tests.GlobalStateCollection.Name)]`。别的包若在同一类声明上方加特性 / 改类名,冲突一眼可解(两行都留)。
  **别的包新增改全局状态的测试类时,合并后 `GlobalStateGuardTests` 会红并点名**,给该类补一行同样的特性即可。
- `AiServicesTests.cs` 的 `AiServiceDirs`:构造 / Dispose 各多几行计数。

## 7. 合并后要改的文档行(本包不改 README / PARITY / DECISIONS / PROGRESS)

- `docs/PARITY.md` §11.4 第 5 条(「原地升级撤回认不出 OptiFine…;SRV 没在真网络上验证;测试夹具在并行测试类里改全局目录…」)→ 划掉,改为:
  「~~…~~ —— 2026-11-21 AI3-stability 已处理:撤回按执行前后版本目录的差集删;`--srv-check` 真网络核验 5 个 SRV 名 20/20 命中;
  改全局状态的 85 个测试类全部在串行集合里,有 IL 守卫。」
- `progress/AI2-stability.md` §5 第 1 条(夹具改全局目录)、第 3 条前半句(OptiFine 认不出)、第 4 条「没有在真网络上验证过」→ 已解决,见本文。
- `docs/DECISIONS.md` 建议追加一条:「改进程级全局状态的测试类必须挂 `DisableParallelization` 集合,由 `GlobalStateGuardTests` 按 IL 检查;
  原地升级撤回口径 = 执行前后版本目录差集(兜底:属于目标版本)」。
