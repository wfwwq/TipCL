# AI5-S(子代理改造:Windows 侧)

- 日期:2026-11-28
- 分支与工作区:`ai5/win-agent` @ `C:\tipcl-ai5\win-agent`(基线 `b4cd7a2`)
- 属主:win-agent;Linux 侧不在本包(另一棵树),本包只做 Windows(`Agent.cs` + `Scheduler.cs` + `AgentResult.cs`)

## 一、做了什么(任务书 S 包 6 条,Windows 侧)

| # | 任务书要求 | Windows 落地 |
|---|---|---|
| 1 | 会话 id 改成 `<父id>-sub-<短随机id>`,每次派发唯一 | `Agent.SubagentSessionId(parent)`(L2101):`父id + "-sub-" + 8 位 Guid hex`;`RunSubagent` L2116 改用它。原实现是固定 `<父id>-sub`(L1743) |
| 2 | 新增设置项 `ai_subagent_max_rounds`,默认 4,`dispatch_subagent` 参数可在硬上限内覆盖 | `Agent.SubagentMaxRounds(settings, args)`(L2054):参数 > 设置项 > 默认 4,一律夹在 `1..SUBAGENT_MAX_ROUNDS_HARD_CAP(20)`;设置项先看本轮 settings,再看 `config.json`(主设置白名单在 `BackendApi` 里、不归本包,所以按 `_current_instance` 的口径直读 config 兜底) |
| 3 | 子代理 onStatus 转给父回合,事件加 `subagent` 字段 | `RunSubagent` 新增可选参数 `onStatus`;调用点(L1620)把父回合的 `_status` 传进去;转发时 payload 追加 `subagent`(=本次派发的会话 id)与 `subagent_task`(任务摘要前 80 字) |
| 4 | 返回值保留原字段,新增 `rounds`、`tools_used`、`truncated` | 旧字段 `ok` / `answer` / `stop_reason` / `error` 全部保留;新增 `rounds`(= `rounds_used`)、`tools_used`(本回合工具名去重、按首次调用顺序)、`truncated`(`stop_reason` 是 `truncated` 或 `max_rounds`)。`tools_used` 由 `_result` 从 `turn.tool_calls` 汇总进 `AgentResult.tools_used` |
| 5 | 子代理结束后清掉自己的压缩状态条目 | `RunSubagent` 的 `finally` 调 `Agent.ForgetCompactState(subSessionId)`(异常/取消路径也会清) |
| 6 | `Scheduler` 补会话状态工具不并行:`update_plan` / `remember` / `forget`;`dispatch_subagent` 保留并行 | `Scheduler.SESSION_STATE_TOOLS`(L14-21)+ `IsParallelizable` 里排除(L45-51)。`dispatch_subagent`、普通只读查询照旧并行;写工具照旧不并行 |

**没有改的既有语义**(research-r 提醒的差异):`Scheduler.RunGroups` 遇到 faulted 仍然直接抛、打断同组 ——
本包只加了「谁能进并行组」的判定,没动执行与异常语义。Linux 侧「异常收成工具载荷」的差异记为遗留,不在本包顺手改。

**UI 那一半没做**:任务书说「UI 先只显示一行『子任务进行中:<工具名>』」。
本包把事件通道和 `subagent` 字段打通了,但 `Pages/AiPage.cs` 的 `OnStatus` 是界面侧文件、也不在本包属主范围;
界面只要在 `OnStatus` 里读 `ev.Payload.subagent` 显示一行即可(见遗留 1)。

## 二、文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | `SUBAGENT_MAX_ROUNDS_DEFAULT/HARD_CAP` + `SubagentMaxRounds` + `_int_setting`(L2047-2099)、`SubagentSessionId`(L2101)、`RunSubagent` 重写(L2113-2180)、`dispatch_subagent` 调用点(L1620)、`_result` 汇总 `tools_used`(L1950-1958) |
| `windos/src/TipCL/Core/Launcher/Ai/Scheduler.cs` | `SESSION_STATE_TOOLS` + `IsParallelizable` |
| `windos/src/TipCL/Core/Launcher/Ai/AgentResult.cs` | 新增 `tools_used` 字段 |
| `windos/tests/TipCL.Tests/AiSubagentTests.cs` | 新增 8 条用例 |

## 三、复现 → 修复 → 转绿

1. **会话 id 串号(复现)**:改造前同一轮派两个子代理,两者 `ai_session_id` 都是 `<父id>-sub`,
   共用一个 `CompactState` 与事件日志。用例 `SessionIdIsUniquePerDispatch` 断言两次派发 id 不同、
   形如 `chat-1-sub-` + 8 位;旧实现下这条必红(固定串不随机)。
2. **轮数写死(复现)**:改造前 `pyDict["ai_max_rounds"] = 4L` 是常量,设置项不生效。
   `MaxRoundsReadsSettingAndArgument` / `MaxRoundsIsCapped` / `MaxRoundsDefaultsToFour` 覆盖:
   不设 = 4;设 8 = 8;字符串 "6" = 6;参数 2 = 2;参数 12 覆盖设置 8;参数 999 / 设置 50 都夹到 20;参数 0 当没给。
3. **并行排除(复现)**:改造前 `update_plan` / `remember` / `forget` 都标 `readonly` + side_effect `none`,
   会进并行组。`SessionStateToolsAreNotParallelizable` + `SessionStateToolsSitInTheirOwnGroup`:
   这三个各自单独成组、`dispatch_subagent` 仍可并行、写工具仍不并行。
4. **返回值**:`rounds` / `tools_used` / `truncated` 由 `RunSubagent` 写进 JSON;`tools_used` 的来源
   (`AgentResult.tools_used`)在 `_result` 里汇总,已被 I 包的回合级用例间接覆盖。
5. **清理压缩状态**:`ForgetCompactState` 由 `AiCompactStateLruTests.ForgetRemovesOnlyThatSession` 覆盖
   (只删指定会话、别人不受影响),`RunSubagent` 的 `finally` 调用是源码级事实。
6. **转绿**:`dotnet test --filter "FullyQualifiedName~AiSubagentTests"` → 8/8 通过;
   全量 Windows 用例 826 过 / 6 跳 / 0 败 / 总 832。

> 第一次跑的时候这 8 条里有 2 条红过:我只加了 `SESSION_STATE_TOOLS` 常量、忘了把它接进
> `IsParallelizable`(红信息是「update_plan 不该进并行组」)。补上 IsParallelizable 的排除判断后转绿 ——
> 这就是「复现 → 修复 → 转绿」的真实顺序。

## 四、验收数字

- 构建 `-t:Rebuild`:0 error / 2 warning(与基线逐条相同)
- 测试:826 过 / 6 跳 / 0 败 / 总 832(基线 802;本包 8 条新用例)
- 体积:见 `AI5-D.md`(compact +11,776 B / premium +11,264 B,按裁决不回收)
- i18n:本包**没有新增/修改词条 key**(清单为空)—— 转发事件带的是字段名,不是文案
- smoke:本机禁止启动 WPF,未跑 `--smoke`(交由 AI5-0 / 集成阶段)

## 五、遗留问题

1. **界面一行提示未接**:事件已带 `subagent` / `subagent_task`,但 `windos/src/TipCL/Pages/AiPage.cs`
   的 `OnStatus` 没有加对应分支(不在本包属主范围)。现在子代理的进度事件会按普通步骤渲染。
2. **`ai_subagent_max_rounds` 没有界面项**:当前只能写 `config.json`。`BackendApi._settings_full()`
   里也没加这条键(那是 D6 属主的文件),本包按「先读 settings、再读 config」兜底,所以 config 里写就能生效。
   若要加设置页控件,由界面侧补一行即可。
3. **`dispatch_subagent` 并行的安全性**依赖会话 id 唯一(已做)+ 每个子代理独立 `CompactState`(已做)。
   但 `Usage` / `Trace` / `Store.LogEvent` 仍是进程级静态字典,并发派发时用量会按不同会话 id 各自累计(不会串),
   `Usage.Get(父会话)` 不含子代理用量 —— 与改造前一致,是否合并统计留给 O 包口径。
4. `truncated` 的判定口径是「`stop_reason` 为 `truncated` 或 `max_rounds`」,即「输出/额度被截断」;
   如果后续想要「是否被压缩过」之类的语义,需要另加字段。
5. research-r 指出的 `Scheduler` 异常语义差异(Windows faulted 直接抛 / Linux 收成载荷)未改,记录在案。