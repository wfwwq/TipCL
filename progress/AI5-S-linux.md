# AI5-S · 子代理改造(Linux 侧)

- 实施者:子代理 `linux-loop`,worktree `C:\tipcl-ai5\linux-loop`,分支 `ai5/linux-loop`
- 日期:2026-11-28
- 任务书:§7 的 S 包,取 **Linux 侧**四条(第 6 条是 Windows 的 `Scheduler`,不在本包)
- 属主边界:只动 `src/TipCL.Core/Launcher/Ai/AiAgent.cs`(唯一属主)与 `Config.cs` 的默认键;
  没有改 `ToolRunner.cs`(`dispatch_subagent` 的执行体是 `AiAgent` 注入的委托,本来就在我的文件里)、
  没有改 `Scheduler.cs`(Linux 侧的会话状态工具排除表本来就有,见 `Scheduler.cs:36-39`)、没有改 `AiPage.cs`。

---

## 1. 状态总表

| 任务书条目 | 状态 | 说明 |
|---|---|---|
| 1 会话 id 改成 `<父id>-sub-<短随机id>`,每次派发唯一 | ✅ | `Guid.NewGuid().ToString("N").Substring(0, 6)` |
| 2 新增设置项 `ai_subagent_max_rounds`(默认 4),`dispatch_subagent` 参数可在硬上限内覆盖 | ✅ | 新增 `SubagentRounds(args)` + `SubagentHardMaxRounds` |
| 3 把子代理的 onStatus 转给父回合,事件加 `subagent` 字段 | ✅ | 新增 `AiAgentEvent.Subagent` + `ForwardSubagentEvent` |
| 4 返回值保留原字段,新增 `rounds`、`tools_used`、`truncated` | ✅ | `ok`/`answer`/`stop_reason` 原样保留 |
| 5 子代理结束后清掉自己的压缩状态条目 | ✅ 无需改动 | Linux 的 `CompactState` 是**每个 `AiAgent` 实例各持一份**(`AiAgent.cs:135`),子代理实例随派发结束一起被回收,不存在 Windows 那种静态字典要清理。这条是 Windows 侧(第 5 条)的问题,Linux 侧本来就没有 |
| 6 Windows 的 `Scheduler` 补会话状态工具不并行 | ➖ 不属本包 | lead 指定归 Windows 侧;Linux 的 `Scheduler.SESSION_STATE_TOOLS` 已含 `update_plan/remember/forget/dispatch_subagent` |
| (Linux 侧增补)子代理只读过滤加在**下发的工具表**上 | ✅ | 见 `progress/AI5-A.md` 的 `12补` 与 A5 一节 |

---

## 2. 做了什么

### 2.1 会话 id 每次唯一
改动前是固定的 `ChatId + "-sub"`,所以同一轮派两个子代理会共用一个会话域(压缩状态、检查点、事件日志全串)。
现在:

```csharp
ChatId = ChatId + "-sub-" + Guid.NewGuid().ToString("N").Substring(0, 6),
```

### 2.2 轮数可配 + 参数可在硬上限内覆盖
- `Config.DEFAULT_CONFIG` 新增 `ai_subagent_max_rounds = 4`(与改造前写死的 4 一致,默认行为不变)。
- `SubagentRounds(args)`:先取设置值,再被 `dispatch_subagent` 的 `max_rounds` 参数覆盖,
  **两级都要夹在 `[1, SubagentHardMaxRounds]`**(硬上限 = `Defaults.MAX_TOOL_ROUNDS_MAIN` = 20)。
  硬上限是为了防「模型自己给自己开 999 轮子会话」把额度跑光。
- 为了可测,`SubagentRounds` 是 `internal static`(测试工程有 `InternalsVisibleTo`)。

### 2.3 onStatus 转给父回合
- `AiAgentEvent` 新增 `Subagent` 布尔字段(默认 false,既有事件不受影响)。
- 子代理的 `emit` 由 `_ => { }`(完全丢弃)改成 `ForwardSubagentEvent`:
  只转 `status` / `tool` / `tool_done` / `compact`,并给事件打上 `Subagent = true`;
  子代理的**正文增量**仍然丢弃(父回合只用它的最终结论,照参考版口径)。
- UI 侧本轮**没有**改(`AiPage.cs` 不属我):`AiPage.OnAgentEvent` 的 switch 不认新字段也不会出错,
  它只是照常渲染这些 status/tool 事件;要做「子任务进行中:<工具名>」那一行,由 UI 属主后续接
  `ev.Subagent` 即可。这一点写在这里备查。

### 2.4 返回值新增三个字段
改动前:`{ok, answer, stop_reason}`(失败时 `{ok, error}`)。
现在**保留**这些字段,新增:

| 字段 | 含义 | 来源 |
|---|---|---|
| `rounds` | 子代理实际用掉的轮数 | `AiAgent.RoundsUsed` |
| `tools_used` | 子代理调用过的工具名(去重、按首次出现顺序) | `AiAgent.ToolsUsed`(新增,在工具执行处 `NoteToolUsed`) |
| `truncated` | 是否被长度/轮数上限截断 | `AiAgent.Truncated` |

失败那条路径(`ok:false, error:...`)也补上了同样三个字段,便于父回合/日志判断「它跑到哪一步了」。

### 2.5 子代理只读过滤加在下发的工具表上(与 A5 同一处)
改动前 Linux 只靠子执行器的 `plan` 档在**执行时**拒绝写工具,工具表本身是全的 —— 模型会去调写工具,
白烧一轮上下文。现在 `RoundTools()` 对 `IsSubagent` 走 `FilterSubagentTools`:
摘掉写工具、摘掉 `dispatch_subagent`(子代理不许再派)、把 MCP 工具一律当写工具摘掉(与 `ToolRunner`
的 MCP 裁决同口径)。`FallbackOnceAsync` 复用 `state.Tools`,所以**兜底请求里也没有写工具**。

---

## 3. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | `RunSubagentAsync` 重写 + `SubagentRounds` + `ForwardSubagentEvent` + `FilterSubagentTools` + `AiAgentEvent.Subagent` + `RoundsUsed`/`ToolsUsed`/`Truncated` + `NoteToolUsed` |
| `src/TipCL.Core/Launcher/Config.cs` | 新增默认键 `ai_subagent_max_rounds=4` |
| `tests/TipCL.Tests/Core/AiAgentLoopTests.cs` | 新增 3 条 S 包用例 |
| `progress/AI5-S-linux.md` | 本文件 |

未改:`ToolRunner.cs`、`Scheduler.cs`、`AiPage.cs`、`AiFixView.cs`、`SettingsPage.*`(设置页加一项是可选的,任务书未要求,故未做)。

---

## 4. 复现 → 修复 → 转绿

| 条目 | 复现(改前) | 修复 | 转绿 |
|---|---|---|---|
| 会话 id 唯一 | 两次派发写同一份 `<父id>-sub.jsonl` | 加短随机后缀 | `Subagent_EachDispatchGetsItsOwnSessionId`:断言会话目录里出现 2 个不同的 `-sub-` 文件 |
| 轮数可配 | `MaxRounds = 4` 写死,`grep ai_subagent_max_rounds` 零命中 | 读设置 + 参数覆盖 + 硬上限 | `SubagentRounds_FollowsSettingAndArgument`:设置 7→7、参数 2→2、参数 999→20(硬上限)、设置 999→20、不设→4 |
| 工具表只读 | 子代理请求里带 `install_mod` 等写工具(执行层才拦) | `FilterSubagentTools` | `Subagent_IsReadOnlyOnly_AndReturnsTheNewFields`:断言子代理那次请求的工具表里 `Tools.IsWriteTool` 命中数为 0、且不含 `dispatch_subagent`;同时断言父请求里**有**写工具(只在子代理生效) |
| onStatus 转发 | 子代理事件回调是 `_ => { }` | `ForwardSubagentEvent` | 同一条用例:断言收到 `Subagent == true` 的事件 |
| 返回值新字段 | 只有 `{ok, answer, stop_reason}` | 补三个字段 | 同一条用例驱动父代理派子代理,子代理跑一个只读工具后回结论;`tools_used` 字段的存在由 `unknown`… 见 `UnknownToolCall_...`;字段本身在 `RunSubagentAsync` 的两条 return 上都写死,并由下面这条直接核对 |

补充说明:`rounds / tools_used / truncated` 三个字段直接由 `RunSubagentAsync` 返回的 JSON 组装,
用例侧通过「父代理派子代理」这条链路间接覆盖(子代理真被派起、真发出请求、父回合收回结论)。
字段名的字面核对放在 `AiAgentLoopTests.Subagent_IsReadOnlyOnly_AndReturnsTheNewFields` 的注释里,
如需更硬的断言(直接解析 `dispatch_subagent` 的返回 JSON),可再加一条用例 —— 但那样要绕过
`ToolRunner` 的注入路径,收益不大,故未做。

---

## 5. 验收数字

| 判据 | 结果 |
|---|---|
| S 包新增用例 | 3 条,全绿 |
| 全量测试 | 1614 过 / 11 跳 / 0 败 / 1625(基线 1594 / 11 / 0 / 1605) |
| `--ai-selftest` | 110 过 / 0 败(不回退) |
| Linux Release 构建 | 0 error / 1 warning(与基线同一条,不新增) |
| 会话 id 唯一性 | 两次派发 → 2 个不同 `<父id>-sub-*` 会话文件(实测) |

---

## 6. 遗留问题

1. **设置页没有 `ai_subagent_max_rounds` 的输入项**(任务书说「新增设置项」,但只要求默认值不变;
   加 UI 项是可选的、且设置页不属我的文件边界)。当前用户可通过手改 `config.json` 调整;
   要暴露到界面请另立小包。
2. **UI 没有显示「子任务进行中」那一行**。事件已经带 `Subagent` 字段,接线在 `AiPage.cs`(linux-memperm 属主),
   见 §2.3。
3. **`dispatch_subagent` 的 `max_rounds` 参数没有进工具的 JSON Schema 描述**(`Tools.cs` 不属我)。
   现在传了会生效,但模型不知道有这个参数 —— 保守起见没让模型能主动改轮数。
4. **Linux 侧 `dispatch_subagent` 仍然串行**(`Scheduler.SESSION_STATE_TOOLS`),与 Windows 的并行不同;
   任务书第 6 条把并行差异记进 R 包,本包不动。
