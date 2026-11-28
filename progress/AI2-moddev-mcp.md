# AI2-moddev-mcp · 模组开发测试环境(经 MCP 给外部编程工具用)+ MCP 遗留问题

分支 `ai2/moddev-mcp`(基于 `main@f1c070b`),worktree `D:\项目\TipCL\wt-ai2-moddev-mcp`。提交前缀 `AI2-moddev-mcp:`。
本文件是本包唯一的进度记录;`docs/dev/PROGRESS.md` / `docs/DECISIONS.md` / `README.md` / `docs/PARITY.md` 未改(按公共任务书),要改的行列在 §8。

定位(用户已明确):**内置助手不写、不编译 Java 模组。** 外部编程工具(Claude Code、Cursor 等)写代码、编译,
通过 TipCL 的本机 MCP Server(`http://127.0.0.1:25585/mcp`)让 TipCL 当测试环境:建测试实例 → 装本地 jar → 起一次游戏看能不能进主菜单 → 拿日志 / 崩溃摘要回去改代码。

## 1. 做了什么

### 1.1 五个新工具(schema + ToolMeta.ALL + ToolRunner 分发三处都注册;MCP `tools/list` 动态读表自动出现)

| 工具 | 只读 | side_effect / 风险 | long_running | 说明 |
|---|---|---|---|---|
| `create_test_instance` | 否 | write_local / medium | 是 | 新建实例,`.instance.json` 写 `dev=true`(另记 `dev_loader` / `dev_loader_version` / `dev_dependencies` / `dev_version`),名字自动加 `[dev] ` 前缀;`install`(默认 true)时起后台任务装 MC 本体 + 加载器 + 依赖模组(如 `fabric-api`),返回 `task_id` |
| `install_local_mod` | 否 | write_local / medium | 否 | 装本地 jar:只收 `.jar`、绝对路径、合法 zip、必须有 fabric.mod.json / quilt.mod.json / META-INF/mods.toml / META-INF/neoforge.mods.toml;读出 id / 版本 / 必需依赖 / 适用 MC 版本;**加载器不匹配直接拒**;同 id 旧 jar 先挪到 `实例/.tipcl_dev/replaced/<change_id>/` 再放新 jar,整个改动先做 Checkpoint 快照,返回 `change_id`。目标不是测试实例时必须 `allow_non_dev=true`,且执行器强制再弹一次确认(见 1.2) |
| `revert_local_mod` | 否 | **delete / high** | 否 | 按 `change_id` 回滚 Checkpoint:删掉那次装进去的 jar、旧 jar 放回 mods、备份目录清掉。删除类:**确认卡 `ConfirmCard.AllowAlways` 与执行器 `ToolRunner.AllowsAlways` 两处都不给「本对话都允许」** |
| `run_test_launch` | 否 | launch / medium | 是 | 起游戏,等「进入主菜单」特征行 / 崩溃 / 超时(默认 180 秒,5–1800)后结束并**自动关闭游戏进程**;返回 `outcome`(ready / crashed / exited / timeout / launch_failed)、`elapsed_s`、`log_excerpt`(ERROR / Exception / Caused by / Mixin 等关键行 + 最后 20 行)、`crash`(crash-reports 里本次启动后的最新一份:Description / 第一行异常 / Suspected Mods / 前 40 行)、`diagnosis`(`Crash.AnalyzeInstance`,即 diagnose_launch 背后的 Core 函数)。默认立刻返回 `task_id`,`wait=true` 时等结束再返回;同一时间只跑一个(第二个回 `busy`) |
| `get_task_status` | 是 | read / low | 是(读进程内共享任务表,不进并行组) | 查 `create_test_instance` / `run_test_launch` 的任务,以及 `install_*` / `launch_game` 起的启动器任务(TaskStore);`wait_seconds`(0–60)可阻塞等到任务结束,减少外部调用方轮询 |

- 「进入主菜单」特征行是可配置常量 `TestLaunch.MainMenuMarkers`(默认 `Sound engine started` = 1.13+ 原版加载完资源、出标题画面时那行;`OpenAL initialized` = 1.12 及以前),调用方也可用 `ready_pattern`(正则)覆盖;一出现就判崩溃的特征行在 `TestLaunch.CrashMarkers`。
- 日志来源:启动任务日志(TaskStore 里游戏 stdout)+ 本次启动后写过的 `latest.log`(`AiFsTools.FindLog`,与 get_latest_log 同口径;任务日志被裁时 latest.log 是完整的)。崩溃报告同样走 `AiFsTools.FindLog(..., "crash")`(与 get_crash_report 同口径)。
- `launch_game` 会把默认实例改成被启动的实例:测试启动结束时把用户原来的默认实例改回去。
- 关键词表(`AiData.KEYWORD_TOOLS`)加一行:「测试实例 / 本地 jar / 自己编译 / 开发模组 / 测试启动 / 任务进度」等词才把这 5 个工具带给内置助手;不进 CORE_TOOLS(内置助手不做模组开发,只在用户点名时可用)。系统提示词未改。
- 确认卡预览(Preview)、步骤行标签(Tools.Label)、检查点路径(Tools.AffectedPaths)都接上:install_local_mod 的预览/检查点/执行用**同一份** `ModDevOps.Plan`。AI 页会话里的写操作照旧由 AiAgent 按 AffectedPaths 做快照(「撤回」按钮可退),工具自己另做一份 `moddev` 会话的快照供 `revert_local_mod` 用(MCP 路径没有 AiAgent)。

### 1.2 写操作确认链(不放宽)
- 五个工具都走 `ToolRunner.RunAsync` 的同一条链:ToolMeta 分级 → `Decide` → `Confirm`(带预览)→ Checkpoint。外部 MCP 调用照旧 `ForceConfirmWrites`:**每次写都弹确认**,「以后都允许」不生效。
- **非测试实例额外确认**:`install_local_mod` 的目标实例不是 dev 时 ——
  1. 执行体本身要求 `allow_non_dev=true`,否则回 `needs_confirmation`(不改盘);
  2. 执行器 `ModDevForceAsk` 让这次调用**无视档位与「本对话都允许」一定弹确认**(全自动档也弹),确认卡理由换成「目标实例不是测试实例(dev)…风险更高」(`ModDevText.NonDevReason`,走 Tr)。

### 1.3 MCP 遗留问题(progress/AI-engine-mcp.md §5、D-81)
1. **AI 页状态条「MCP 已开启 · 最近外部调用」**:`AiPage.ModDevMcp.cs` 的 `McpStatusChip()`(Name=`McpStatusChip`),订阅 Core `McpServer.StateChanged / ToolCalled`(挂上视觉树订阅、摘下退订,不在静态事件上留页面引用);文案用 Windows 版同一组词条(`MCP 已开启` / `MCP 已开启 · {0} {1}`,悬停 `MCP Server 地址：{0}` + `最近一次外部调用：{0}（{1}）`)。MCP 没开时整条隐藏。
2. **外部写操作确认框看不到**:`McpServerHost.BringMainWindowToFront()` 弹框前把主窗口还原(最小化时)、`Activate()`、短暂 `Topmost` 一下绕过系统焦点保护;确认框挂在这个窗口上。
   **等待超时**:Core `McpConfirmGate` 给 MCP 执行器的确认回调套超时(`McpServer.ConfirmTimeout`,默认 120 秒;`McpHttpServer.ConfirmTimeout` 可单独调),超时按拒绝处理,工具结果 `{"ok":false,"error":"permission_denied","reason":"confirm_timeout","message":"等了 120 秒…"}`,同时取消 `McpConfirmGate.CurrentToken`,宿主据此把还开着的确认框关掉(关闭 = 拒绝,不会出现「超时后用户又点了允许、外部却以为被拒」)。
3. **长任务进度**:选「查询工具」`get_task_status`(而不是 MCP progress notification):本机 MCP Server 只有 POST、没有 SSE 流(GET 回 405,沿参考版),推不了通知;查询工具可测、Claude Code / Cursor 都能用。TaskStore 在 App 层,Core 经注入点 `ModDevHost.HostTaskProbe`(App 启动时 `McpServerHost.Init` 注入)读它。

### 1.4 注入点(都在独立静态类 `ModDevHost`,不碰页面类的静态初始化)
`GameInstaller`(默认 `GameInstall.InstallGame`)/ `DependencyInstaller`(默认 `Mods.InstallModFromSource`)/ `LaunchDriverFactory`(默认 `BackendLaunchDriver`:IApi 的 `launch_game` / `stop_game` + HostTaskProbe)/ `HostTaskProbe`。**测试一律注入假实现,绝不联网、绝不真启动游戏。**

## 2. 外部编程工具接入指南(合并时挪进 docs)

### 2.1 先在 TipCL 里打开 MCP Server
设置 → AI 助手 → MCP Server:打开开关(默认端口 25585,可改)→「复制地址」。AI 页标题栏会出现「MCP 已开启」状态条,外部每次调用都会更新为「MCP 已开启 · 工具名 时间」。

### 2.2 Claude Code
命令行(推荐):
```bash
claude mcp add --transport http tipcl http://127.0.0.1:25585/mcp
```
或在项目根放 `.mcp.json`:
```json
{
  "mcpServers": {
    "tipcl": { "type": "http", "url": "http://127.0.0.1:25585/mcp" }
  }
}
```
`run_test_launch wait=true` 最长要等 `timeout_seconds + 60` 秒;Claude Code 的单次工具超时不够时可设环境变量 `MCP_TOOL_TIMEOUT`(毫秒),或干脆用 `wait=false` + `get_task_status(wait_seconds=30)` 轮询(推荐)。

### 2.3 Cursor
`~/.cursor/mcp.json`(全局)或项目里的 `.cursor/mcp.json`:
```json
{
  "mcpServers": {
    "tipcl": { "url": "http://127.0.0.1:25585/mcp" }
  }
}
```
在 Cursor 设置 → MCP 里确认 `tipcl` 是绿点、能列出工具。

### 2.4 安全口径(给外部工具的使用者)
- 只听 127.0.0.1、只收本机来源,带外站 `Origin` 一律 403;没有 token 鉴权(同 Windows 参考版)。
- **所有写操作都要坐在电脑前的人在 TipCL 窗口里点「允许这一次」**;120 秒没人点按拒绝处理(`permission_denied` / `confirm_timeout`)。外部 agent 收到拒绝后应该停下来问用户,不要重试刷屏。
- 往非测试实例装 jar 需要 `allow_non_dev=true`,并且一定会再弹确认。

### 2.5 推荐的开发 → 测试循环
1. **一次性准备**:`create_test_instance {mc_version:"1.20.1", loader:"fabric", dependencies:["fabric-api"]}` → 记下 `instance` 与 `task_id`;`get_task_status {task_id, wait_seconds:30}` 直到 `state=succeeded`。
2. **改代码 → 编译**(外部工具自己做,如 `./gradlew build`,产物在 `build/libs/*.jar`;注意挑不带 `-sources` / `-dev` 后缀的那个)。
3. `install_local_mod {jar_path:"<绝对路径>/build/libs/mymod-1.0.1.jar", instance:"[dev] 1.20.1-fabric"}` → 同 id 的旧版本自动换下;返回里看 `warnings`(MC 版本不符、缺依赖提示)。被拒时看 `error`:`loader_mismatch` / `invalid_jar` / `not_a_mod` / `needs_confirmation` / `file_exists`。
4. `run_test_launch {instance:"[dev] 1.20.1-fabric"}` → `get_task_status {task_id, wait_seconds:30}` 循环,直到 `finished=true`:
   - `result.outcome == "ready"`:进了主菜单,测试通过(游戏已自动关闭);
   - `crashed`:看 `result.crash.description / exception / suspected_mods` 与 `result.log_excerpt`,回到第 2 步改;
   - `timeout`:加大 `timeout_seconds`,或用 `ready_pattern` 指定你的模组在标题画面打的日志;
   - `launch_failed`:多半是没装好版本 / 没账号 / Java,看 `message`。
5. 改坏了想退回上一个能用的 jar:`revert_local_mod {change_id}`。
6. 需要更细的信息时照常用 `get_latest_log` / `get_crash_report` / `diagnose_launch` / `scan_mod_conflicts`(都是只读,不弹确认)。

## 3. 文件清单

新增:
- `src/TipCL.Core/Launcher/Ai/ModDev/ModJarInfo.cs`(jar 元数据解析 + MC 版本约束 + 加载器兼容)
- `src/TipCL.Core/Launcher/Ai/ModDev/ModDevOps.cs`(create_test_instance / install_local_mod / revert_local_mod 执行体、执行计划、检查点路径)
- `src/TipCL.Core/Launcher/Ai/ModDev/TestLaunch.cs`(run_test_launch 监视循环、`ITestLaunchDriver`、默认 `BackendLaunchDriver`、日志关键片段与崩溃摘要)
- `src/TipCL.Core/Launcher/Ai/ModDev/ModDevHost.cs`(注入点 `ModDevHost` + 后台任务表 `ModDevTasks` / `ModDevTask` + get_task_status)
- `src/TipCL.Core/Launcher/Ai/ModDev/ModDevSchemas.cs`(5 个工具 schema)
- `src/TipCL.Core/Launcher/Ai/ModDev/ModDevText.cs`(界面文案:标签 / 预览 / 非 dev 确认理由,全走 Tr)
- `src/TipCL.Core/Launcher/Ai/ModDev/McpConfirmGate.cs`(MCP 确认等待超时)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.ModDevMcp.cs`(partial:分发 + `ModDevGate` / `ModDevForceAsk`)
- `src/TipCL.Core/Launcher/Ai/AiSelfTestModDev.cs`(自检 6 项)
- `src/TipCL.App/Pages/Ai/AiPage.ModDevMcp.cs`(AI 页 MCP 状态条)
- `tests/TipCL.Tests/Core/ModDevMcpTests.cs`、`tests/TipCL.Tests/App/AiModDevMcpAppTests.cs`
- `progress/AI2-moddev-mcp.md`(本文件)

修改(都是局部追加):
- `Tools.cs`:`Schemas` 加一行 `AddRange(ModDevSchemas.Schemas())`;`Label` 加 3 行钩子;`AffectedPaths` 加 4 行钩子。
- `AiToolTypes.cs`:`ToolMeta.ALL` 末尾 +5 条(带注释)。
- `ToolRunner.cs`:分发 switch 加一组 5 个 case;`RunAsync` 权限判断两处 —— 条件里加 `|| ModDevForceAsk(call)`,裁决外面套 `ModDevGate(call, …)`。
- `ToolRunner.NewFeatures.cs`:`AllowsAlways` 加 `"revert_local_mod" => false`。
- `Preview.cs`:`default` 分支开头加 4 行钩子(`ModDevText.ChangePreview`)。
- `AiData.cs`:`KEYWORD_TOOLS` 末尾 +1 组。
- `AiSelfTest.cs`:`AiSelfTestEngineMcp.VerifyAsync` 之后 +1 行。
- `McpServer.cs`:`McpServer.ConfirmTimeout`(静态,默认 120 秒)、`McpHttpServer.ConfirmTimeout`;`CallTool` 里套 `McpConfirmGate.Wrap`,超时改写结果。
- `src/TipCL.App/Common/Ai/McpServerHost.cs`:`Init` 注入 `ModDevHost.HostTaskProbe`;新增 `TaskProbe` / `BringMainWindowToFront`;`ConfirmAsync` 弹框前置前、超时信号到达时关框、确认框挂到主窗口。
- `src/TipCL.App/Pages/Ai/AiPage.cs`:标题栏按钮列表加 `McpStatusChip()`(1 行)。
- `src/TipCL.App/Pages/Ai/ConfirmCard.cs`:`AllowAlways` 加 `"revert_local_mod" => false`。
- `src/TipCL.Core/Resources/locales/zh_CN.json` / `en.json`:各 +17 键,插在 `"MCP 已开启 · {0} {1}"` 之后(不在文件尾);两表键集相等(3146)。
- `tests/TipCL.Tests/App/ClickSweepTests.cs`:**不是本包写的** —— 主代理在 11:42 写进本 worktree 的一行跳过词(会调系统浏览器 / 文件管理器的按钮,防测试弹窗),单独一个提交保留下来;其余 worktree 应该也有同一行,合并时取一份。

## 4. 新增测试
xunit **+53**(`ModDevMcpTests.cs` 49:ModJarInfoTests 27 含 18 条版本约束 Theory、ModDevOpsTests 9、TestLaunchTests 8、ModDevRegistryTests 2、McpModDevTests 3;`AiModDevMcpAppTests.cs` 4);`--ai-selftest` **+6** 项。
覆盖:三种加载器(+Quilt、多加载器 jar)的正反例、非 zip、无元数据、`${file.jarVersion}` 回落 MANIFEST、可选依赖不算、NeoForge 两种 toml;加载器不匹配拒绝且不改盘;同 id 替换 + 撤回(字节级还原)+ 二次撤回报 not_found;同名不同模组不覆盖;非 dev 要 flag 且全自动档 + 「本对话都允许」也强制确认;run_test_launch 成功 / 崩溃(进程退出 + 崩溃报告)/ 日志里出现崩溃特征行 / 超时(夹到 5 秒下限)/ 自定义 ready_pattern / 启动失败 / 坏正则 / 并发 busy / get_task_status 跟踪 / 读启动器任务表 / 恢复用户默认实例(**全用假驱动**);MCP 确认超时 ⇒ permission_denied + confirm_timeout 且宿主信号被取消;随机端口 HTTP 走完「建实例 → 装 jar → 测试启动 → 查状态 → 撤回」且每个写操作各确认一次。

## 5. 实测(2026-11-21,本机 Windows,SDK 8.0.428;脚本 `D:\项目\TipCL\_verify_ai2\moddev-mcp\build.ps1 full`,全程后台隐藏运行)
- Debug `--no-incremental` 构建 `TipCL.Linux.sln`:**0 错误**,3 个警告,全是基线就有的 `LayoutMotionAndHeightTests` xUnit1013×2 / xUnit2000×1,无新增。
- 全量 `dotnet test`:**通过 1174 / 跳过 7 / 失败 0(共 1181 = 基线 1128 + 本包 53)**,耗时 2 分 42 秒。
- 本包与 MCP 相关类单独跑(`ModDev|ModJarInfo|TestLaunchTests|McpServerTests`):61/61 通过。
- `--ai-selftest`(临时 TIPCL_HOME):**通过 97 / 失败 0 / 跳过 0,exit=0**(基线 91 + 本包 6)。
- i18n 守卫(`tools/i18n-audit.py`):未接线 3540 ≤ 基线 3605,已接线 2244 ≥ 基线 1623;基线文件未动。工具结果 / schema / 自检文件用 `i18n:ignore-file`(都是给模型或开发者看的),界面文案(标签 / 预览 / 确认理由 / 状态条)全部走 Tr。
- 两份词表键集相等(3146 / 3146),JSON 合法。
- `git diff f1c070b HEAD -- windos` 为空。
- 整个过程没有真启动游戏、没有联网安装;MCP 集成用例用随机端口(`Start(0)`),没碰 25585。

## 6. 遗留问题与风险
1. **真游戏没有端到端跑过**:按要求测试里绝不真启动;默认驱动 `BackendLaunchDriver` 的接线(launch_game 返回 task_id → TaskStore 日志 → stop_game)只有代码审读,没有在真实 MC 上验证「Sound engine started」在各加载器 / 各版本上的出现时机。NeoForge / Forge 早期加载画面阶段也会打别的日志,特征行可能要按实测补(常量在 `TestLaunch.MainMenuMarkers`)。
2. `stop_game` 关的是启动器记住的「当前游戏进程」(LaunchCore 只记一个);开了「允许多开」且用户同时在玩别的实例时,测试启动结束会关掉最近启动的那个 —— 正常情况下就是测试那个,但并发多开时有误伤可能。
3. `create_test_instance` 的依赖安装走 `Mods.InstallModFromSource`(Modrinth slug);CurseForge-only 的依赖装不上会如实记在结果里(任务记 failed、游戏本体已装好),建议用户下载后 `install_local_mod`。
4. 顺带发现(未改,不在本包范围):`ToolRunner` 现有的 `install_mod` 等把 `name / instance / source` 平铺传给 `InProcessApi.install_mod`,而后者从 `args["extra"]` 取参数(`CatalogExtra`),外部 / 内置助手调 `install_mod` 可能拿不到模组名;另外 `InProcessApi.install_mod` 返回的是任务 id **字符串**,`ToolRunner.Start` 用 `Py.D(result)` 取 `task_id` 会得到空串。`get_task_status` 已能查启动器任务,但前提是调用方拿得到 id。建议主线单独修。
5. 外部确认框仍是模态 `Dlg.Ask`;置前在 Windows 上受「前台锁」限制,个别情况下只会闪任务栏(已用 Topmost 抖一下缓解);Linux 各桌面环境行为不一。超时兜底保证外部调用不会无限挂着。
6. `ModDevTasks` 是进程内表(最近 50 个),启动器重启后旧 task_id 查不到(回 not_found 并说明原因)。

## 7. 合并注意(冲突点)
- `ToolRunner.cs`:`RunAsync` 里 `if (!readOnly && (ForceConfirmWrites || !_alwaysAllowed.Contains(call.name) || ModDevForceAsk(call)))` 与 `PermissionVerdict verdict = ModDevGate(call, ExternalWriteGate(...))` 这两行是在 AI-engine-mcp 的改法上再套一层;别的包若也改这两行,保留两层。分发 switch 的 5 个 case 插在 AI-new-features 那组之后。
- `Tools.cs` 三处钩子、`AiToolTypes.cs` 末尾 5 条、`AiData.KEYWORD_TOOLS` 末尾 1 组、`Preview.cs` default 分支开头、`AllowsAlways` / `ConfirmCard.AllowAlways` 各 1 行:都是追加,和别的包同位置追加时两份都留。
- locales:17 键插在 `"MCP 已开启 · {0} {1}"` 之后;合并后跑一次两表键集相等检查。
- `McpServerHost.cs`:`Init` / `ConfirmAsync` 改动较多,若别的包也改确认框,以本包的「置前 + 超时关框 + owner」为准再合。
- 工具总数 65 → 70。

## 8. 要改的 README / PARITY / DECISIONS 行(本包不改,交主线)
- README「🤖 AI 助手」功能行:补一句「模组开发测试环境:外部编程工具经 MCP 建测试实例、装本地 jar、测试启动(内置助手不写/不编译模组)」;MCP Server 小节补:外部写操作确认 120 秒超时按拒绝、确认时主窗口自动置前、AI 页有状态条;并链接 docs 里的「外部编程工具接入指南」(本文件 §2 挪过去)。
- docs/PARITY.md §10:新增 5 个 Linux 独有工具(Windows 参考版没有对应);`McpServer` 一行补「确认超时 / 置前 / 状态条(状态条对齐参考版 AiPage `_mcpBadge`)」;`--ai-selftest` 项数 +6。
- docs/DECISIONS.md 新增一条(建议 D-82):「P3 模组开发测试环境:内置助手不写不编译模组,由外部编程工具经 MCP 调 create_test_instance / install_local_mod / revert_local_mod / run_test_launch / get_task_status;测试实例 = `.instance.json` 的 `dev=true`;非 dev 实例装 jar 要 `allow_non_dev` 且执行器强制确认;长任务进度用查询工具而不是 progress notification(本机 MCP 无 SSE);外部确认 120 秒超时按拒绝」。D-81 的 MCP 遗留三条可标为已处理,指向本文件。
