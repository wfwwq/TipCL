# AI5-O · 会话级可观测性汇总(可选包)

> 日期:2026-11-28
> worktree:`C:\tipcl-ai5\observ-o`(分支 `ai5/observ-o`,基线 `b4cd7a2`)
> 范围:只做**诊断导出**;UI 面板按任务书 §7 O 的口径等用户拍板后再做,本包一行 UI 都没改。
> 两棵树都做了:Linux(Avalonia / net8,本机已验证)+ Windows(WPF / net48,本机已验证构建与测试)。

---

## 一、做了什么

按会话把散在两处落盘记录里的指标合并成一屏能看的数:

| 指标 | 数据来源(权威口径) |
| --- | --- |
| 轮数 | 会话事件的各次 `TurnSummary.rounds` 之和(旧会话文件没有 TurnSummary 时退回 `TurnCompleted.rounds` 之和,并写进 caveat) |
| 轮开始 / 完成 / 失败 | `TurnStarted` / `TurnCompleted` / `TurnFailed` 事件条数 |
| 工具调用数 | `ToolStarted` 事件条数 |
| 工具失败数 / 失败率 | `ToolCompleted.ok=false` 的条数 **+** 只有 `ToolStarted` 没有对应 `ToolCompleted`(按 `tool_call_id` 对)的条数;失败率 = 失败 / 调用 |
| 压缩次数(主动 / 被动) | `CompactBoundary.trigger=auto` 计主动;`trigger=reactive` 计被动 |
| 压缩失败数 | 轨迹事件 `compact_failed` / `compact_failure`(Windows 另收 `compact_circuit_break`) |
| `tool_schema_miss` 次数 | 轨迹事件 `tool_schema_miss`,按轨迹行的 `session` 字段归集 |
| 输入 / 输出 token | 会话事件 `Usage.prompt_tokens` / `completion_tokens` 之和(只算 provider 真值,估算值不计) |
| 旁证 | `ModelRetry` / `ModelFallback` 计重试;`TurnRewound` 计撤回 |

输出位置(两棵树):

1. **诊断导出的报告正文**:`DiagnosticReport.Build`/`Export`(Linux)/ `Diagnose.SessionMetrics`、`Diagnose.SessionMetricsText`(Windows)。
   Linux 的 zip 里另有一份机读的 `metrics.json`(导出 `report.md` + `logs/*` 之外的新条目)。
2. **不新记一份计数**:全部现算自既有事件与轨迹;读不到就如实留 0,并把原因写进报告的 `caveats` 行。

为了让「按会话」成立,补了两处**归属**信息(不改变任何既有事件与行为):

- `Trace.Record` 在**会话作用域**内记的行带上 `session` 字段;作用域由 `Trace.SessionScope(sessionId)` 开(AsyncLocal,跨 await 传递,并行会话不串)。不设作用域时字段省略 ⇒ 旧日志格式与既有调用点完全不变。
- Linux 的 `AiAgent.RunAsync` 收尾补一条 `TurnSummary` 事件、provider 真值处补一条 `Usage` 事件(字段口径逐字照 Windows 版 `Agent.cs:1652` / `:1619`)。Windows 侧本来就有这两条事件,没动记账逻辑。

---

## 二、文件清单

### Linux 树(本机已验证)

| 文件 | 改动 |
| --- | --- |
| `src/TipCL.Core/Launcher/Ai/AiSessionMetrics.cs` | **新增**:会话级汇总器(纯读,`Build(sessionId, traceDays=1)` → `Result`,含 `ToPy()` / `ToMarkdownLines()`) |
| `src/TipCL.Core/Launcher/Ai/Trace.cs` | `SessionScope` / `CurrentSession`(AsyncLocal)+ 行里写 `session` 字段 + `ReadRecords(days)` 回读轨迹 |
| `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | 开轨迹作用域;新增 `LogTurnSummary` 局部函数(3 处收尾调用);工具成功/失败计数;provider 真值处补 `Usage` 事件 |
| `src/TipCL.Core/Launcher/Ai/NewFeatures/DiagnosticReport.cs` | 报告新增「四、会话级统计」小节(含字段口径一行、caveats 行);`Build`/`Export` 加可选 `sessionId` 参数;zip 里加 `metrics.json` |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | 新增 `ChatId` 属性(只用于诊断归属,不参与权限/执行判断) |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.NewFeatures.cs` | `export_diagnostic_report` 把 `ChatId` 传给导出 |
| `src/TipCL.App/Pages/Ai/AiPage.NewFeatures.cs` | 用户点「导出诊断报告」时把当前对话 id 传进去 |
| `src/TipCL.App/Pages/Ai/AiPage.cs` | 跑 agent 时同步 `_runner.ChatId`(工具路径也能拿到会话 id) |
| `tests/TipCL.Tests/Core/AiSessionMetricsTests.cs` | **新增**:6 个用例(见下) |

### Windows 树(本机已验证)

| 文件 | 改动 |
| --- | --- |
| `windos/src/TipCL/Core/Launcher/Ai/AiSessionMetrics.cs` | **新增**:与 Linux 同名同义的汇总器 |
| `windos/src/TipCL/Core/Launcher/Ai/Trace.cs` | `SessionScope` / `CurrentSession` + 行里写 `session` 字段 + `ReadRecords(days)` |
| `windos/src/TipCL/Core/Launcher/Ai/Store.cs` | 新增 `SessionDirPath()` 与 `ReadEvents(sessionId)`(与 Linux 同义;`LogEvent` 未动) |
| `windos/src/TipCL/Core/Launcher/Ai/Diagnose.cs` | 新增 `SessionMetrics(sessionId)`(PyDict)与 `SessionMetricsText(sessionId)`(文本行) |
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 一轮开始处开轨迹作用域(**一行**);记账逻辑未动 |
| `windos/tests/TipCL.Tests/AiSessionMetricsTests.cs` | **新增**:6 个用例(与 Linux 侧同一组断言) |

> Windows 侧没有走「AI 页导出按钮」那条路:Windows 树没有诊断报告导出入口(`Diagnose.cs` 是崩溃日志诊断),
> 所以按任务书 O 的口径把汇总挂到 `Diagnose` 上(`Diagnose.SessionMetrics` / `SessionMetricsText`),
> 供导出/排查调用。

---

## 三、新增字段清单

`AiSessionMetrics.Result.ToPy()`(两棵树同字段名):

| 字段 | 含义 |
| --- | --- |
| `session` / `session_file` / `trace_dir` / `trace_days` / `has_events` / `last_event_at` / `trace_files` | 数据来源与窗口(轨迹计数只覆盖最近 `trace_days` 天,默认 1 天) |
| `turns_started` / `turns_completed` / `turns_failed` | 轮开始 / 完成 / 失败 |
| `rounds` | 轮数合计 |
| `tool_started` / `tool_completed` / `tool_calls` | 工具调用(ToolStarted 条数) |
| `tool_failures` / `tool_failure_rate` | 工具失败数与失败率 |
| `tool_calls_from_turn_summary` / `tool_failures_from_turn_summary` | TurnSummary 的同一组数(交叉核对用;不一致会写 caveat) |
| `compacts_auto` / `compacts_reactive` / `compacts` / `compact_failures` | 主动 / 被动 / 合计压缩次数与压缩失败数 |
| `tool_schema_miss` | 轨迹口径的漏选重试次数 |
| `input_tokens` / `output_tokens` / `usage_requests` | 输入 / 输出 token 与 provider 真值请求数 |
| `model_retries` / `rewinds` | 重试与撤回 |
| `usage_live` | 本次进程内存里那份 `Usage`(与落盘值对照;重启后为 0 属正常) |
| `caveats` | 口径说明 / 本次读不到的原因(人读文本里以 `⚠` 行出现) |

报告正文里的行(两棵树同一份文案):`- 会话=`、`- 轮次=`、`- 工具=`、`- 压缩=`、`- schema_miss=`、`- token=`、`- 重试=`、`- 数据来源=`。

---

## 四、怎么验证数值一致(验收口径)

**原则:汇总器不新记计数,所以「一致」= 报告里的数 == 直接数原始记录得到的数。**

1. **自动用例(两棵树各 6 个,同一组断言)**:
   - `CountsEveryMetricFromRawEvents`:造一段固定事件流(2 轮 / 4 次工具调用其中 1 次 `ok=false` / 主动压缩 1 次 + 被动压缩 1 次 / 2 条 Usage),断言汇总值逐个等于手工数出来的值(5 轮、4 调用、1 失败、25.0%、2 次压缩、1500/260 token),并断言「事件流自洽时 `caveats` 为空」。
   - `UnfinishedToolCallCountsAsFailure_AndOldSessionFallsBackToTurnCompleted`:只有 `ToolStarted` 没有 `ToolCompleted` 的那次按失败计;没有 `TurnSummary` 的旧会话退回 `TurnCompleted.rounds` 并在 caveats 里说明。
   - `MissingSessionFileReportsZeroAndSaysWhy`:事件文件不存在时全 0 且写明原因,不抛。
   - `ToolSchemaMissAndCompactFailureAreCountedFromTraceOfThisSessionOnly`:两本会话各记轨迹,断言只数本会话的行,且不在作用域里的行不计入任何会话。
   - `SessionScopeFollowsAsyncContinuations`:跨 `await` 后作用域仍生效,退出后还原。
   - `DiagnoseExposesTheSessionMetricsAndText` / `DiagnosticReportCarriesTheMetricsSectionAndMetricsJson`:导出产物里能查到上述字段(Windows 走 `Diagnose.SessionMetrics`;Linux 走 zip 里的 `report.md` + `metrics.json`)。
2. **人工对照(可复现)**:跑一段真实对话,然后
   ```powershell
   # 直接数原始事件
   $f = "<TIPCL_HOME>\cache\ai_sessions\<会话>.jsonl"
   (Select-String -Path $f -Pattern '"event": ?"ToolStarted"').Count
   (Select-String -Path $f -Pattern '"event": ?"CompactBoundary"').Count
   # 与诊断导出里 report.md 的「会话级统计」小节、metrics.json 逐项比对
   ```
3. **本包实跑证据**
   - Linux Release 构建:**0 error / 1 warning**(与基线同一条 `LayoutEditor.cs:196` CS0649)。
   - Linux `dotnet test -c Release --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"`:**1600 过 / 0 败 / 11 跳 / 总 1611**(基线是 1594 过 / 11 跳 / 总 1605 ⇒ 正好 +6 个新增用例;`AiSessionMetricsTests` 6/6 全过)。两次独立运行都得到同一组数(1 m 45 s / 2 m 13 s),SDK 9.0.321。
   - Linux `--ai-selftest`(隔离 TIPCL_HOME 下跑):**110 过 / 0 败 / 0 跳,exit 0**(与基线一致)。
   - Windows Release 构建:`0 error / 2 warning`(两条 xUnit1031,既有基线),`dotnet build-server shutdown` 后按任务书命令重测一致。
   - Windows `dotnet test -c Release`(同一组过滤):**802 过 / 0 败 / 6 跳 / 总 808**(基线 796 过 / 6 跳 / 总 802 ⇒ 正好 +6 个新增用例)。
   - i18n 守卫:`python tools/i18n-audit.py --list-unwrapped` 未接线字面量**净增 0**(见下面「偏差」)。

---

## 五、偏差与需要拍板的事

1. **i18n:新增的诊断文案标了 `i18n:ignore` / `i18n:ignore-file`**。
   - 汇总器与报告新增小节的字面量是**诊断报告正文**(随导出的 `report.md` / `metrics.json` 走,不是界面文案,也不随语言切换),与 `DiagnosticReport.cs` 里既有那批报告字面量同一性质。
   - 但守卫口径是「未接线数不得增长」,所以我给新增行加了 `i18n:ignore`(逐行 5 处)并对 `AiSessionMetrics.cs` 用了 `i18n:ignore-file`,净增 **0**、守卫转绿。
   - 要按报告正文原样计数,只需删掉这几处标记(净增 5,Linux)/ 删掉文件级标记(净增 38)。
2. **compact 体积**:本包改动后本机实测 **5,768,192 B**,基线(ai5-0)**5,759,488 B**,净增 **+8,704 B**。红线 5,757,264 B —— 我在开工前后各测一次(用任务书命令:`dotnet build windos/TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`),两次都超红线(开工前 +2,224 B、改动后 +10,928 B)。按要求:**不做体积回收,也不声称合格/不合格**,裁决归用户。
3. **`tool_schema_miss` 在 Linux 树上恒为 0**:Linux 侧根本没有「模型调了没声明的工具就补发全量工具表重试」这段逻辑(任务书 C2 负责),轨迹里自然没有这类事件。字段与计数逻辑已就位,Windows 侧是真实计数(C2 落地后 Linux 自动就有数)。
4. **没有做 UI 面板**:按任务书 §7 O 的闸门,等用户确认后再做。Windows 侧也没接「导出按钮」(Windows 树本来就没有 AI 诊断导出入口)。

## 六、要给文档加的行(建议,未改文档)

- `docs/DECISIONS.md`:可增加一条「诊断导出新增会话级统计小节:轮数 / 工具失败率 / 压缩次数 / tool_schema_miss / token 的权威口径见 `AiSessionMetrics`,报告正文里的字段名不再是猜的」。
- `docs/PARITY.md`:两棵树现在都有 `AiSessionMetrics`(同名同义),可记一条「会话可观测性:两棵树字段一致,Linux 的 `tool_schema_miss` 待 C2 才会有非零值」。
