# AI5-D 守卫包进度(D1 / D2 / D6)

> 包号:AI5-D1、AI5-D2、AI5-D6(任务书 §7 D 包)
> 分支:`ai5/win-guard`;worktree `C:\tipcl-ai5\win-guard`(从 `b4cd7a2` 拉出)
> 日期:2026-11-28
> 任务书位置说明:任务书**不在**本 worktree 里(未被 git 跟踪),按 lead 口径读中立共享目录 `C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`。
> 红线口径冲突一处:任务书 §4.7 / §5.1 写「只在 `C:\tipcl-fix` 的 `ai5/agent-fixes` 里做」,本轮按 lead 的派工在我自己的 worktree `C:\tipcl-ai5\win-guard` / 分支 `ai5/win-guard` 上做;未改版本号、未发 release、未 push。

---

## 1. 做了什么

### D1 `fs_write` 没有守卫,能改写自己的权限库 / 对话历史 / 记忆

- 现象:`fs_write` 的允许根里有启动器数据目录 `Utils.ROOT`(`ExtraTools.cs` 的 `FsRoots`),而 `FsWrite` 只判断内容大小,不拒任何路径或后缀。`ai_permissions.json`(`Permission.cs:321`)、`ai_chats.json`(`Store.cs:29`)、`ai_memory.md`(`FileMemory.cs:23`)、`mcp_server.json`(`McpServer.cs:59`)都正好落在这个目录根下 —— 助手一次 `fs_write` 就能改掉自己的权限档位、对话历史、长期记忆,也能写出 `.bat` / `.exe` 这种双击就跑的文件。
- 修法(移植 Linux 版 `src/TipCL.Core/Launcher/Ai/AiFsTools.cs:91-98`、`:412-415`):
  - 新增数据目录守卫 `FsGuardDataDir(PPath p, bool forWrite)`,由 `FsResolve(raw, roots, baseDir, forWrite)` 末尾调用;`FsWrite` 传 `forWrite: true`。
  - **账号 / 配置文件**(`accounts.json`、`config.json`、`device_id`):读写都拒(里面有令牌、API Key、权限档位)。
  - **AI 自己的状态文件**(`ai_chats.json`、`ai_memory.md`、`ai_permissions.json`、`mcp_server.json`):可以读,不许写(与 Linux 的 `NoWriteFiles` 同口径;后两个是 Windows 侧自己的状态文件,一并收进来)。
  - **`cache` 目录**(检查点 `ai_checkpoints`、会话 `ai_sessions`、工具产物 `ai_results`、trace):不许写,读不受限。
  - **脚本 / 二进制后缀**:写时拒 `.sh .bash .bat .cmd .ps1 .vbs .desktop .command .appimage .msi .lnk`(新表)与既有的 `_FS_BINARY_SUFFIX`(`.exe .dll .jar .zip .png ...`)。
  - 数据目录根同时比对 `Utils.ROOT` 与 `Utils.ResolveRoot()`(`BridgeHost` 会 `SetRoot`,两条取根的路要都认)。
  - `fs_write` 的工具描述补上「哪些会被拒」,模型不用白试一轮。
- 兼容性:守卫只看「数据目录**根**下的这几种名字」和 `cache` 子树,实例目录里同名的 `config.json`、以及 `notes.txt` / `mods.json` / `logs/latest.log` / `config/options.txt` / `resourcepacks/pack.mcmeta` 全部照旧放行(有用例钉住)。
- 边界:`Tools.cs:479`(`AffectedPaths`,`win-rollback` 属主)仍按读语义调用 `FsResolve`,签名加了默认参数因此不受影响;没有改 `Permission.cs` / `ToolMeta.cs` / `Tools.cs` / `Preview.cs`。

### D2 权限面板硬编码 34 条,工具表已有 56 条

- 现象:`AiPermissionPanel` 里 `private static readonly string[] Tools = new string[34] {...}`,而注册表 `ToolMeta.Map` = `ToolMeta.ALL`(38)+ `ExtraTools.Meta`(18)= 56 条。少掉的 22 条(全部 extra 工具 + `dispatch_subagent` / `remember` / `forget` 等)在面板里根本选不到,想给它们加规则只能手改 `ai_permissions.json`。
- 修法:面板的工具清单改成从注册表生成 —— 新增 `AiPermissionTools.Names()`(同文件内的 public 静态类,`ToolMeta.Map.Keys` 按名称排序),面板字段改成 `AiPermissionTools.Names()`。
  - 为什么单独一个 public 类:仓库没有 `InternalsVisibleTo`,而 net8 测试宿主实例化不了 WPF 控件(仓库既有做法是源码文本断言,见 `LayoutV3SourceTests`)。这样「面板条目集合 == 注册表工具集合」这条验收能用**真实调用**钉住,面板本体的取清单方式再用一条源码断言兜住。

### D6 确认卡没有超时,能永久挂住

- 现象:`BackendApi._ai_run` 里的 `WaitCard` 每 200ms 轮询一次 `_ai_confirm_ev`,只有「用户点按钮」或 `_ai_cancel` 能让它退出;用户走开后这一轮永远等下去,`_ai_busy` 一直为真。
- 修法:
  - 新增 `BackendApi.ConfirmCardTimeout`(默认 **120 秒**,与 MCP 外部确认 `McpServer.ConfirmTimeout` 同口径)。
  - 抽出 `WaitCardAnswered(ev, cancelled, timeout, clock)`:超时 / 取消都返回 false;`timeout <= 0` 表示没有期限(询问卡走这条,老口径不变)。`clock` 只给测试注入。
  - **超时 → 拒绝**:`ConfirmFn` 超时返回 `false`,与用户点「拒绝」同一条路径(工具被判 `denied`,不落盘)。
  - **超时 → 事件 + UI 说明**:新增 `ConfirmTimeoutStatus(timeout, chatId)` 产出 `ai.status` 事件(`kind=confirm_timeout`,`label=「等你确认超过 120 秒,这一次已按拒绝处理」`)。`AiPage.OnStatus` 的 `default` 分支会把 `label` 显示到状态行(AiPage.cs:2429-2434),因此不用改页面就有一句明确说明,而不是含混的「已拒绝」。
  - 卡片的收尾:这一轮结束时 AiPage.cs:1842-1844 会把还没答的卡 `Reject()` 掉并加一条「已拒绝: …」,所以超时后不会留一张可以继续点的僵尸卡在后端等。

---

## 2. 文件清单

改动(3 个产品文件,均为本包独占):

| 文件 | 包 | 改动 |
|---|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs` | D1 | `FsGuardDataDir` + 四张守卫表 + `FsResolve(forWrite)` + `FsWrite` 传参 + `fs_write` 描述 |
| `windos/src/TipCL/Pages/AiPermissionPanel.cs` | D2 | 删手抄 34 条;新增 `AiPermissionTools`;下拉用 `AiPermissionTools.Names()` |
| `windos/src/TipCL/Core/BackendApi.cs` | D6 | `ConfirmCardTimeout` / `WaitCardAnswered` / `ConfirmTimeoutStatus`;`ConfirmFn` 超时按拒绝 + 发状态事件 |

新增用例(4 个测试文件,36 条):

| 文件 | 包 | 条数 |
|---|---|---|
| `windos/tests/TipCL.Tests/AiFsWriteGuardTests.cs` | D1 | 24 |
| `windos/tests/TipCL.Tests/AiPermissionPanelToolsTests.cs` | D2 | 4 |
| `windos/tests/TipCL.Tests/AiConfirmCardTimeoutTests.cs` | D6 | 8 |
| `windos/tests/TipCL.Tests/RepoFiles.cs` | D2/D6 共用 | 源码文本断言取文件工具 |

本进度文件:`progress/AI5-D-guard.md`。

### 验证命令

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'   # 当前会话仍要覆盖(旧值被 DSH 子进程继承)
dotnet build windos\TipCL.sln -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
```

> 坑(踩过):用 `Copy-Item` 把备份放回源码时**时间戳被保留**,MSBuild 认为文件没变、静默跳过重编,于是「恢复源码后」跑的其实还是变异二进制(表现是测试又挂死)。解决办法:`-t:Rebuild`,或放回后把 `LastWriteTime` 更新。

---

## 3. 复现 → 修复 → 用例转绿(三步证据)

### 第一步:复现(把修法变异回修之前,跑新用例)

| 包 | 变异 | 红了多少 |
|---|---|---|
| D1 | 摘掉 `FsResolve` 末尾的 `FsGuardDataDir(pPath2, forWrite);` | **16 条**红:写 4 个 AI 状态文件(×4)、账号/配置文件读写(×3)、cache 写(×1)、脚本/二进制后缀(×8) |
| D2 | `AiPermissionTools.Names()` 退回手抄 34 条 | **4 条**红:`PanelToolListEqualsRegistry`(34 ≠ 56)、`PanelToolListIncludesPreviouslyMissingExtraTools`、`PanelToolListCoversTheOldHardcodedThirtyFour`(34 < 56)、`PanelSourceDerivesToolsFromRegistry`(源码里 `new string[34]` 又回来了) |
| D6 | `ConfirmFn` 退回「`WaitCard(_ai_confirm_ev)` 无期限」 | **1 条**红:`ConfirmPathWiresTimeoutAndExplainsInUi` |

变异后这批用例的实测:`失败: 21,通过: 13,总计: 34,持续时间: 916 ms`(总数 34 = 36 − 2,见下)。

**D6 的另一半证据(修之前的真实行为 = 永久挂住)**:把 `WaitCardAnswered` 里那段期限分支也摘掉后,`ConfirmCardTimesOut` / `ConfirmCardTimeoutUsesClock` 两条用例**不返回** —— 实测跑满 600 秒仍未结束(只能手动终止进程)。这正是修之前 `WaitCard` 的行为:没人点卡片就永远等下去。

### 第二步:修复

见 §1 的三段改动;D1 的检查顺序、D6 的「超时即拒绝」路径都照 Linux / MCP 的既有口径,没有放宽任何权限(守卫只加不减)。

### 第三步:转绿

```
已通过! - 失败: 0,通过: 832,已跳过: 6,总计: 838,持续时间: 6 s
其中本包 36 条:
已通过! - 失败: 0,通过: 36,已跳过: 0,总计: 36(三个守卫用例类单独跑)
```

修复后重新 `-t:Rebuild` 编译:0 error / **2 warning**,与基线逐条相同(`BridgeMutationAndLaunchGuardTests.cs:34,35` 的既有 xUnit1031),**零新增警告**。

---

## 4. 验收数字

| 项 | 基线(ai5-0) | 本包实测 |
|---|---|---|
| Windows Release 构建 | 0 error / 2 warning | 0 error / 2 warning(同两条,零新增) |
| `dotnet test`(排除 Terracotta / ClickSweep / OpenUrl) | 802 总 / 796 过 / 6 跳 / 0 败 | **838 总 / 832 过 / 6 跳 / 0 败**(净增 36 条用例) |
| compact `TipCL.exe` | 5,759,488 B | **5,764,096 B(+4,608 B)** |

**体积**:如实记录,**不做回收、不声称合格与否**(按 lead 2026-11-28 裁决:本轮不回收,另立回收包)。推高来自新增的守卫逻辑、超时帮助方法与 130 多行中文注释;没有为了省字节砍掉任何守卫分支。

**D1 验收逐条**:

| 验收点 | 结果 |
|---|---|
| 写 `ai_permissions.json` 被拒 | ✅ `ConfigEditError`「这是 AI 助手自己的状态文件」,文件内容不变 |
| 写 `ai_chats.json` / `ai_memory.md` / `mcp_server.json` 被拒 | ✅ 同上(绝对路径与相对数据目录根两种写法都拒) |
| 写 `.bat` / `.exe` 被拒 | ✅ 另测 `.cmd .ps1 .vbs .jar .zip .png` 全部被拒,连父目录都没建出来 |
| 正常写实例文件放行 | ✅ `notes.txt`、`mods.json`、`logs/latest.log`、`config/options.txt`、`resourcepacks/pack.mcmeta`、实例本地 `config.json` |
| 账号 / 配置文件读写都拒 | ✅ `accounts.json`、`config.json`、`device_id` |
| `cache` 不许写 | ✅ 写拒、读仍可 |
| AI 状态文件仍可读 | ✅ `fs_read` 正常返回 |

**D2 验收逐条**:

| 验收点 | 结果 |
|---|---|
| 面板条目集合 == 注册表工具集合 | ✅ `new HashSet(AiPermissionTools.Names()) == new HashSet(ToolMeta.Map.Keys)`,且条数 == `ToolMeta.Map.Count` |
| 条数 | 56(手抄版 34,补上 22 条:全部 18 个 extra 工具 + `dispatch_subagent` / `update_plan` / `remember` / `forget`) |
| 顺序稳定 | ✅ 按名称序(与手抄版同样的排序口径) |
| 面板不再手抄 | ✅ 源码断言:有 `AiPermissionTools.Names()`,没有 `new string[34]`,也不再出现 `"search_versions"` / `"write_mod_config"` 这类手抄名单 |

**D6 验收逐条**:

| 验收点 | 结果 |
|---|---|
| 有超时,口径 120 秒 | ✅ `ConfirmCardTimeout == TimeSpan.FromSeconds(120) == McpServer.ConfirmTimeout` |
| 超时 → 拒绝 | ✅ `WaitCardAnswered` 返回 false;`ConfirmFn` 走「用户拒绝」同一条路径(工具 `denied`,不落盘) |
| 超时 → 事件 | ✅ `ai.status`(`kind=confirm_timeout`,带 `chat_id` 与可读 `label`),页面状态行按 `default` 分支显示 |
| 没超时就照常 | ✅ 100ms 内回答 → 继续等待返回 true |
| 中途取消优先 | ✅ `_ai_cancel` → 立即 false(不等满 120 秒) |
| 询问卡不受影响 | ✅ `timeout = TimeSpan.Zero` → 无期限等待(仍会一直等,见遗留 1) |

---

## 5. 遗留问题 / 需要别人接手的

1. **`ask`(询问卡)仍然没有超时**。任务书 D6 只点确认卡,`ask` 的语义是「等你回答问题」,给它加超时属于用户可见的行为变更,本包**没做**(代码注释与用例 `ZeroTimeoutWaitsWithoutDeadline` 都写明这是有意保留的老口径)。如果要统一,建议另立裁决:例如 ask 也给 120 秒,超时按「用户取消了选择」返回 `null`(Agent.cs:1195-1199 已有这条分支)。
2. **超时当刻卡片不会自己消失**。超时后 UI 的即时说明走状态行(`ai.status` 的 `label`);那张卡要等这一轮结束由 `AiPage.cs:1842-1844` 的 `Reject()` 摘掉。想让卡片在超时当刻就变灰 / 消失,需要动 `AiPage.cs` / `ConfirmCard.cs`(不在本包文件边界内,交给后续包)。
3. **i18n:新增 1 条用户可见文案,词表与 `.gz` 未动**(按 lead 口径由 i18n 收口方统一重跑 `pack-core-resources.ps1`)。
   - key:`等你确认超过 {0} 秒,这一次已按拒绝处理`(en 建议:`No confirmation after {0} seconds; this action was denied.`)
   - 位置:`BackendApi.ConfirmTimeoutStatus`;缺 en 词条时 `I18nCore.Tr` 回退成中文原文,不会显示成空。
4. **D1 的两处「有意多出 Linux 口径」**:① 读路径也拦「账号 / 配置文件」(与 Linux `SecretFiles` 一致,属修缺陷本身);② `ai_permissions.json`、`mcp_server.json` 是 Windows 侧新增到 `NoWriteFiles` 里的(任务书 D1 验收点名要拒 `ai_permissions.json`,Linux 侧没有这两个文件)。
5. **体积**:见 §4。若回收包要动这三处,注意守卫表的字符串与 `fs_write` 描述是主要占用,别把守卫逻辑本身砍掉。
6. **另有一次事故已单独报给 lead**:清理自己挂死的 `dotnet test` 时按进程名批量 `Stop-Process`,误杀了 `C:\tipcl-ai5\t-linux` 那个会话的 `testhost.exe`(可能中断了它一次测试跑)。后续只按「命令行里含自己 worktree 路径」筛进程。

---

## 7. 追加(2026-11-28):卡顿关键词补一个「卡」

> 独立小提交。D1/D2/D6 三个包不动。

### 做了什么

- `ExtraTools.Keywords` 里卡顿那一行的 keys 从 14 项变 15 项,补一个**单独的字**「卡」(在「卡顿」之后)。
  覆盖的说法:「好卡」「有点卡」「一卡一卡」「太卡了」「卡得不行」「卡住不动」。
- 由来:`t-win` 做回放场景时发现、`linux-tools` 独立核实:Windows 这张表原来只有「卡顿 / 很卡 / 掉帧 / 帧数 / fps / lag / 优化 / 流畅 / 内存 / JVM / GC / 电脑配置」,**没有「卡」也没有「好卡」**,用户说「好卡」时首轮不下发 `get_system_info` / `diagnose_lag` / `tune_jvm`。Linux 侧同一行本来就有「好卡」,且 `linux-tools` 在 Linux 侧补单字「卡」后漏选率从 23.5/百回合降到 0。
- **没有动** `ActionRe` / `QuestionRe`(「是不是在下命令」的判定):`linux-tools` 实测过,那个扫窗口会让 33 个写工具全带上、tools token 从 +2.3% 涨到 +9.1%。

### 反向风险与取舍(独立判断,不是照抄 Linux 结论)

命中方式是**子串**匹配(`Tools.SelectToolSchemas` → `text.Contains(k)`,扫最近 12 条 user/tool 消息的 `content`),所以「卡」必然把「显卡」「卡片」一起命中。按代价分两类:

| 误命中 | 判断 |
|---|---|
| 「显卡」(问配置)→ 多下发 3 个工具 | **语义相邻**,问显卡本来就要看配置,不算误伤 |
| 「卡住不动」(游戏挂住)→ 多下发 3 个工具 | **正好是该诊断的场景**,是收益不是代价 |
| 「卡片」类说法(界面卡片、任务卡片) | 纯噪声,但这个启动器的用户对话里很少见 |

误命中的代价上界:这一轮多下发 3 个工具,`get_system_info`、`diagnose_lag` 都是 `ro=true` 只读;`tune_jvm` 是 `ro=false` 但只有 `apply=true` 才写,写设置仍走原有权限判定。**不弹确认、不落盘、不放宽任何权限**,粗估 +3 个 schema ≈ 300 token/轮,且只在出现「卡」的那一轮。

为什么不用「好卡 / 有点卡」这种更长组合:会漏「太卡了」「卡得不行」「巨卡」这些同样常见的说法。**漏选比多选贵** —— 漏选要多花一整轮 + 一次全量 schema 重发(`tool_schema_miss`),多选只多几百 token。故按 lead 与 Linux 侧的一致口径补单字。

### 用例与三步证据

新增 `windos/tests/TipCL.Tests/AiLagKeywordTests.cs`(10 条)。断言走**真实调用** `Tools.SelectToolSchemas`(纯函数,不碰后端),即验证「工具真的被下发」,不是「表里有字」。

- 复现(把「卡」去掉,`-t:Rebuild` 重编):**8 红 / 2 过**,总计 10;通过的两条正好是不该受影响的 —— `UnrelatedMessageDoesNotSelectDiagnoseTools`(无关消息)和那个本来就能命中「掉帧」的 Theory 分支。
- 修复:补回「卡」。
- 转绿:`已通过! - 失败: 0,通过: 10,已跳过: 0,总计: 10`。

### 验收数字

| 项 | 上一包 | 本次追加 |
|---|---|---|
| 全量 `dotnet test`(排除 Terracotta/ClickSweep/OpenUrl) | 838 总 / 832 过 / 6 跳 / 0 败 | **848 总 / 842 过 / 6 跳 / 0 败**(+10 条) |
| Windows Release 构建 | 0 error / 2 warning | 0 error / 2 warning(既有那两条,零新增) |
| compact `TipCL.exe` | 5,764,096 B | **5,764,096 B(+0 B)** |

体积 **+0 B**:新增的 1 个汉字关键词与几行注释被 PE 对齐余量吸收,逐字节相同。仍按裁决不做回收。

### 遗留 / 说明

- 关键词表是子串匹配,没法排除「显卡」「卡片」这类组合;真要精细到「排除法」得给 `Keywords` 加否定词字段(会改数据结构与 `Tools.SelectToolSchemas`,超出本包边界),建议留给 C3(工具选择量化)一起评。
- 若后续 `docs/DECISIONS.md` 要给这条留痕:Windows 关键词表卡顿行新增「卡」,与 Linux 对齐。
