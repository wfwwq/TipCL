# AI5-B(Linux 侧)交付进度:记忆 / 权限接线四项 + C2(来源前缀 + unknown_tool 前置修复)

> 分支 `ai5/linux-memperm`(worktree `C:\tipcl-ai5\linux-memperm`,从 `b4cd7a2` 拉出)。
> 日期一律 ISO 8601。任务书:`windos/docs/改造工程:2611/AI-agent-合并任务书.md` §4/§5/§7 的 B 包、C2、P1。
> 只改 Linux 树(`src/`、`tests/`),没碰 `windos/`、没改版本号、没发 release、没 push。

## 0. 一句话结论

B1–B4 四条接线缺陷全部修完并有回归用例;**C2 的来源前缀这一半**已交付(算前缀的纯函数 + 用例);
**C2 的全量重发那一半**(`AiAgent` 侧)按 lead 的分工归 `linux-loop`,本包只提供它要读的**唯一判据**;
另外按 lead 指派修掉了「`unknown_tool` 被无条件标成成功」这条 C2 前置缺陷。P1 单独提交(见 `progress/AI5-P1-linux.md`)。

## 1. 做了什么(逐条)

### B1 长期记忆分成两份 → 统一成一份(以 FileMemory 为准)+ 幂等迁移

- 现象/复现:助手侧(`AiAgent.SystemMessage` → `AiMemory.Context()`、`ToolRunner` 的 `remember` / `forget`)
  读写 `$TIPCL_HOME/ai-memory.json`;界面侧(记忆面板 `AiMemoryPanel`、设置页 `SettingsPage.Cards`)
  读写 `$TIPCL_HOME/ai_memory.md`(Core 的 `FileMemory`)。两侧零交叉调用 ⇒ 助手记的事面板看不见,
  面板删掉的条目助手照样「记得」。
- 修法:`AiMemory` 退化成 `FileMemory` 的**门面**(调用点一个没改,文件不再分两份);
  旧的 `ai-memory.json` 在第一次访问时**幂等迁移**:逐条走 `FileMemory.Remember`(自带「同样内容已经记过
  就不重复记」),迁完把原文件改名成 `ai-memory.json.migrated` 留档。
  改名这一步是必须的:原文件留着的话,面板里删掉的记忆下次启动会被再迁一遍(复活)。
- 上限口径:统一后是 `FileMemory` 的 80 条 / 单条 300 字(原先助手侧是 200 条)—— 迁移按新上限裁剪,最旧的先丢。
- README 口径:`README.md:72` 本来就写 `ai_memory.md`,与修完的实现一致(无需改;本包不再新增第二处口径)。

### B2 `remember` / `forget` 标成只读却会写盘

- 现象/复现:`AiToolTypes.cs` 里两项都是 `readonly = true`,而执行体确实写 `ai_memory.md`。
  后果不止「少弹一次确认」:`Tools.IsWriteTool` 就是 MCP Server 判断「这次调用要不要外部确认」的依据,
  经 MCP 调用记忆工具也不确认;并行调度还把记忆这种有先后语义的调用判成可并行。
- 修法:两项改标 `write_local` / `risk low`(与同批 Windows 侧的元信息口径一致);
  连带效果按预期:`Tools.IsWriteTool` 为真、`Scheduler.IsParallelizable` 为假、MCP 的 `readOnlyHint` 变 false。
  权限层**没有**为它们加白名单:`default` 档下现在会弹确认,这与「元信息必须和真实副作用一致」一致;
  `dontAsk` 档下按 P1 放行(见 P1 的用例)。

### B3 确认卡的作用域被丢掉 → 按作用域写进 `ai_permission_rules`

- 现象/复现:`ConfirmCard` 已经算出 `"global" | "instance"`(`ConfirmCard.cs:169`),
  但两个调用点都写成 `(ok, always, _)` 把作用域丢掉;执行器只在进程内的 `_alwaysAllowed` 里记工具名
  ⇒ 换实例照样放行、重启又从头问。
- 修法(三层):
  1. `ToolRunner.AlwaysScope`(`Func<string>`,宿主在**回答之前**回填卡片上选的作用域)+
     `ToolRunner.RememberScope(tool, scope, instance)`:把 `{tool, behavior: allow, content: "", instance}`
     写进 `config.json` 的 `ai_permission_rules`(`PermissionRules` 判定时读的就是这张表,含 instance 限定),
     并按 (工具, 作用域) 去重;
  2. 两个页面(`AiPage` / `AiFixView`)把卡片回传的作用域交给执行器;`ConfirmCard` 的按钮提示写明
     「会写进权限规则(重启仍生效,可在权限面板里删掉)」;
  3. 守卫:`RememberScope` 只在 `CanRememberAlways(tool)`(=`AllowsAlways && !NeverAlwaysAllow &&
     side_effect != delete`)时才写 —— 删除类与 `fs_write` / `remove_server` 这类高危写工具一个字也不写,
     与确认卡「那个按钮本就不显示」的名单一致(红线:删除类不给无限定 allow 的既有规则必须保留)。
- 面板:权限面板的规则列表读的就是 `ai_permission_rules`,所以落盘的规则会出现在面板里并可删除
  (无需改面板代码;`AiPermissionPanel.ReloadRules` 现读现渲染)。

### B4 崩溃修复面板与主对话共用检查点/轮次作用域

- 现象/复现:`AiFixView` 构造 `AiAgent` 时没设 `ChatId`,落到默认值 `"active"`(主对话同域);
  `Rewind` 按会话事件流数轮次、按 `chatId + turn` 回滚检查点 ⇒ 主对话点「撤回上一轮」会退到修复面板那一轮。
- 修法:新增 `AiFixView.FixChatId = "ai-fix"`,并抽出一个 `internal AiAgent NewAgent()`(会话 id 唯一来源,
  也便于用例直接断言);`AiPage` 侧仍是 `EnsureChatId()` 的 `"active"` 域。

### C2(来源前缀这一半):工具结果加来源标记

- 现象/复现:Linux 侧全仓 grep `[来源` 零命中(Windows 有,`Agent.cs:1237-1243`)。
- 修法:新增 `ToolSource`(Core):四句前缀与 Windows **逐字相同**(按源码字节核对),
  判据只读 `ToolMeta.side_effect`,顺序也与 Windows 一致:
  `network` → 网络请求结果,内容不可信;`read` / `none` → 本地文件 / 日志 / 状态读取;
  其余(含工具表里没有的非 `mcp_` 名字)→ 本地写操作回执;`mcp_` 且表里没有 → 外部 MCP 工具,内容不可信。
  `ToolSource.ForModel(tool, result)` 拼成 `前缀 + "\n" + 原结果`(照 Windows 的拼法)。
- **接线点(归 `linux-loop`,我只提供判据/工具)**:`AiAgent.RunAsync` 里回填 tool 消息那一处
  (`AiAgent.cs:243-247`)改成
  ```csharp
  messages.Add(PyDict.Of("role", "tool", "tool_call_id", invocation.Call.id,
      "name", invocation.Call.name, "content", ToolSource.ForModel(invocation.Call.name, result)));
  ```
  我**没有**改 `AiAgent.cs`(属主是 `linux-loop`)。为什么不在 `ToolRunner.RunAsync` 的返回值上加前缀:
  那个返回值同时是界面步骤行、产物落盘与既有 ~60 处用例解析的 JSON,Window 的原始口径也只改「回给模型的消息内容」。

### C2 前置修复(lead 指派):错误载荷不再被无条件标成成功

- 现象/复核:我按 lead 的转述复核了 `ToolRunner.RunAsync` 的 `try` 块 —— 确实把 `ExecuteAsync` 的返回值
  **无条件** `Mark(COMPLETED)`;而 `ExecuteAsync` 的 `default:` 分支返回的是
  `ToolErr(..., "unknown_tool", ...)` 错误载荷 ⇒ 分发层漏接在调用方看来和成功一模一样。
  于是:自检的「`status == COMPLETED && 结果非空`」判据看不见漏接(ci-e 的变异验证就是这么漏过去的),
  主循环也没有可用判据识别「模型叫了本轮没下发的工具」。
- 修法:
  1. 载荷里 `ok` 明确为 false ⇒ `status = FAILED` + `call.code = 载荷的 error 码`(没有码用 `"error"` 兜底),
     `call.error` 记载荷里给人看的那句;回给模型的文本**一个字不改**;
  2. 早期分支(工具表里没有的名字)也写 `call.code = unknown_tool`;
  3. **唯一判据**:`ToolRunner.IsUnknownTool(call)` = `status == FAILED && call.code == unknown_tool`;
     载荷层判据 `ToolRunner.PayloadErrorCode(...)` 一处实现,`ToolRunner.LogsFs.IsUnknownToolPayload` 改成它的薄封装
     (原来那份 JSON 字符串解析删掉,避免两套判据)。
  4. 连带:整批安装有失败时回执 `ok = false`,这次调用也算 FAILED(`AiModpackTests` 那条断言随语义更新)。

## 2. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/AiMemory.cs` | B1:改成 FileMemory 门面 + 旧 JSON 幂等迁移(不留缓存,迁完改名留档) |
| `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs` | B2:`remember` / `forget` → `readonly=false, write_local, low`;C2 前置:`ToolCall.code` |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | B3:`RunAsync` 里按作用域落盘;C2 前置:错误载荷 → FAILED + `code`、`UnknownToolCode`/`IsUnknownTool`/`PayloadErrorCode`/`PayloadMessage`;P1(见另一份 progress) |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.PermissionScope.cs`(新) | B3:`AlwaysScope` / `CanRememberAlways` / `RuleInstanceOf` / `RememberScope` |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.LogsFs.cs` | C2 前置:分发探针的 unknown_tool 判据收攏到 `PayloadErrorCode` |
| `src/TipCL.Core/Launcher/Ai/ToolSource.cs`(新) | C2:四句来源前缀 + `Prefix` / `ForModel` / `IsPrefixed` |
| `src/TipCL.App/Pages/Ai/AiPage.cs` | B3:回填 `_confirmScope` + 接 `AlwaysScope` |
| `src/TipCL.App/Pages/Ai/AiFixView.cs` | B3:同上;B4:`FixChatId` + `NewAgent()` |
| `src/TipCL.App/Pages/Ai/ConfirmCard.cs` | B3:按钮提示写明落盘 |
| `src/TipCL.Core/Resources/locales/{zh_CN,en}.json` | 新词条 9 条(清单见 §4) |
| `tests/TipCL.Tests/Core/AiMemoryUnifyTests.cs`(新) | B1:6 条(迁移/幂等/防复活/坏文件/上限/工具写入同源) |
| `tests/TipCL.Tests/Core/AiToolMetaSideEffectTests.cs`(新) | B2:4 条(元信息 / 写工具判据 / 不并行 / 表名唯一) |
| `tests/TipCL.Tests/Core/AiPermissionScopeTests.cs`(新) | B3:10 条(全局 / 仅当前实例 / 去重 / 删除类与高危工具拒写 / 端到端落盘) |
| `tests/TipCL.Tests/App/AiFixChatIdTests.cs`(新) | B4:2 条(agent 用独立 id / 撤回候选按会话隔离) |
| `tests/TipCL.Tests/Core/AiToolSourceTests.cs`(新) | C2:6 条(四类前缀 / 不可信标记 / 拼法) |
| `tests/TipCL.Tests/Core/AiToolUnknownToolTests.cs`(新) | C2 前置:7 条(未知工具 / 其它错误载荷 / 成功不变 / 异常不算 schema-miss / 分发探针) |
| `tests/TipCL.Tests/Core/AiModpackTests.cs` | C2 前置:整批失败那条断言随语义改 FAILED(附注释) |

## 3. 复现 → 修复 → 转绿(三步证据)

| 条目 | 复现(修复前) | 修复 | 转绿 |
|---|---|---|---|
| B1 | 新用例 `AiMemoryUnifyTests` / `RememberAndForgetTools_WriteTheSameFileThePanelReads` 在旧实现下红(助手写的 `ai-memory.json` 与面板读的 `ai_memory.md` 不是一份) | `AiMemory` → FileMemory 门面 + 迁移 | 6/6 过 |
| B2 | `AiToolMetaSideEffectTests.RememberAndForget_DeclareTheirRealSideEffect` 在旧实现下红(`@readonly` 为 true、`IsWriteTool` 为 false) | 元信息改 `write_local` | 4/4 过 |
| B3 | `AiPermissionScopeTests.InstanceScope_OnlyAppliesToThatInstance` / `ConfirmWithAlways_PersistsTheChosenScope` 在旧实现下红(作用域丢失 ⇒ 没有规则、重启又问) | `AlwaysScope` + `RememberScope` 落盘 | 10/10 过 |
| B4 | `AiFixChatIdTests.FixPanel_AgentUsesItsOwnChatId` 在旧实现下红(agent.ChatId == "active") | `FixChatId` + `NewAgent()` | 2/2 过 |
| C2 前置 | `AiToolUnknownToolTests.UnknownTool_IsMarkedFailedWithTheCode` 在旧实现下红(status 是 COMPLETED);`AiModpackTests.BatchInstall_...` 原本断言 COMPLETED(旧语义) | 错误载荷 → FAILED + code | 7/7 过;全量只剩那 1 条随语义更新的断言 |
| C2 前缀 | `AiToolSourceTests` 在旧实现下编译不过(Linux 没有 `ToolSource`)—— 这就是「Linux 零命中」的形式化 | 新增 `ToolSource`(四句与 Windows 逐字相同) | 6/6 过 |

全量(Linux,`--filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"`):

- 基线(AI5-0,`progress/AI5-0-baseline.md`):**1594 过 / 11 跳 / 0 败 / 总 1605**。
- 本包改动后(最终):**1639 过 / 11 跳 / 0 败 / 总 1650**,`exit 0`。新增 45 条用例
  (B1 6 / B2 4 / B3 17 / B4 2 / C2 前置 8 / C2 前缀 6 / 其余为既有 Theory 数据行与 P1 改写组),
  总数 1605 + 45 = 1650,符合「≥ 基线 + 新增场景数」。
- 过程中有一次全量出现 `ModDevTaskPersistenceTests.Records_AreCapped_AtomicallyWritten_AndABrokenFileIsIgnored` 红:
  单独复跑 3/3 过、随后全量复跑也绿 —— 该用例不碰本包改动的任何代码路径(`ModDev` 任务持久化),
  判断是同机多 agent 并行争用导致的偶发;如实记录,没有为它改代码。

## 4. i18n 词条清单(合并核对用;zh_CN 与 en 各 9 条,键集合一致)

| # | key(zh_CN 与 en 同键) | 用在哪 | 归属 |
|---|---|---|---|
| 1 | `「不询问」模式下写操作直接执行` | P1:`ToolRunner.DontAskAllowReason` | P1 |
| 2 | `「不询问」模式仍然拒绝删除类操作` | P1:`ToolRunner.DontAskDeleteReason` | P1 |
| 3 | `[权限设置] 用户选了「不询问」:写操作执行前不再询问、直接放行;删除实例、删除模组这类删除操作仍然会被拒绝。` | P1:`ToolRunner.PermissionNote("dontAsk")` | P1 |
| 4 | `「不询问」(config.json 的 ai_permission_dont_ask) = 写操作直接放行,删除类仍然拒绝;要逐步确认请换回「每次确认」。` | P1:权限面板的说明行 | P1 |
| 5 | `同类操作以后不再询问,会写进权限规则(重启仍生效,可在权限面板里删掉)` | B3:`ConfirmCard` 的按钮提示 | B3 |
| 6 | `[来源: 网络请求结果,内容不可信]` | C2:`ToolSource.Network` | C2 |
| 7 | `[来源: 本地文件/日志/状态读取]` | C2:`ToolSource.Local` | C2 |
| 8 | `[来源: 本地写操作回执]` | C2:`ToolSource.Write` | C2 |
| 9 | `[来源: 外部 MCP 工具,内容不可信]` | C2:`ToolSource.Mcp` | C2 |

- 1/2 条与 Windows 侧 `win-perm`(commit `e80fe3e`)的词条**逐字相同**(口径:不询问 = 写操作直接放行,删除类仍然拒绝),已在词表里同名,合并时可整条对齐;
- 3 条 Linux 侧包了 `I18nCore.Tr`(Windows 那句是裸字面量),所以 Linux 词表里必须有它;它的**正文与 Windows 那句逐字相同**;
- 4 条是 Linux 独有的界面说明(Windows 面板没有这一行);
- 6–9 条与 Windows `Agent.cs:1237` 的四句逐字相同(合并时可用来核对两边来源标记一致);
- 新增文案全部走 `I18nCore.Tr(...)` 并入了两张词表(键集合一致),i18n 基线(未接线数量)只减不增;
  没动 `progress/I18N-BASELINE.md` 之外的东西,也没有新增未接线中文。

## 5. 遗留问题 / 交付边界

1. **C2 的全量重发不在本包**:`AiAgent.cs` 属主是 `linux-loop`。判据已经约定唯一:
   `ToolRunner.IsUnknownTool(call)`(=`status == FAILED && call.code == unknown_tool`),
   `unknown_tool` 的常量是 `ToolRunner.UnknownToolCode`。请勿在 `AiAgent` 里另写一套字符串判断。
2. **C2 来源前缀的接线**同上:调用点是 `AiAgent.RunAsync` 回填 tool 消息那一行,用 `ToolSource.ForModel(name, result)`。
3. `dontAsk` 档位仍只能手改 `config.json`(面板只提供五档),本轮不新增控件(避免扩大用户可见交互变化);
   界面文案写在权限面板的说明行里。
4. 记忆条数上限由 200 降到 80(FileMemory 口径),迁移会按新上限裁剪最旧的条目 —— 这是「以 FileMemory 为准」的既定后果。
5. 体积:本 worktree 不产出 Windows compact exe(那是 `windos/` 的产物),本包只动 Linux 树源码;
   Linux Release 产物字节数见 §6。按 lead 裁决本轮不做体积回收。
6. `--smoke` / `verify-smoke.ps1` / ClickSweep / VisualTour **按用户要求没有运行**(会抢前台)。动到 AI 页面的包
   本应跑 smoke,这里如实记为未跑。

## 6. 验收数字(最终)

- Linux Release 构建(`dotnet build src/TipCL.App -c Release -m:1 -p:UseSharedCompilation=false`):
  **0 error / 1 warning**(既有 `src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649),未新增警告。
- `dotnet test`(排除 Terracotta / ClickSweep / OpenUrl):**1639 过 / 11 跳 / 0 败 / 总 1650**,`exit 0`
  (AI5-0 基线 1594 过 / 11 跳 / 总 1605;新增 45 条)。
- `--ai-selftest`(`dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest`):
  **通过 110 / 失败 0 / 跳过 0,exit 0**(与基线一致;其中「未知工具如实报错」与「只读工具执行成功」
  两项正是 C2 前置修复要保住的判据)。
- i18n 审计(`python tools/i18n-audit.py --json`):`unwrapped` **3211 → 3177(-34)**、
  `wrapped` 2615 → 2809、`missing_en` / `missing_zh` 均为 0 —— 基线判据「未接线数量不得回退」满足
  (我新增的用户可见文案全部走了 `I18nCore.Tr`,并且都入了两张词表)。
- 体积(如实记录,按 lead 裁决本轮**不做**回收):本 worktree 产出的是 Linux Release 产物,
  `src/TipCL.App/bin/Release/net8.0` 目录 33 个文件、合计 **12,794,560 B**,其中
  `TipCL.Core.dll` 2,157,568 B、`TipCL.App.dll` 1,976,832 B。
  红线里的 compact(**Windows 单 exe**)不是本棵树的产物,本包**没有改动 `windos/`**,所以那个产物的字节数不受本包影响。
- 源码规模:`git diff --stat b4cd7a2`:24 个文件、+1499 / -127 行(含用例与词条)。
- `--smoke` / `tools/verify-smoke.ps1` / ClickSweep / VisualTour **按用户要求未运行**(会抢前台)。
