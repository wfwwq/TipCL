# AI5-A · Linux 主循环可靠性(A1–A8)+ 06补 + C2(第 1 条)

- 实施者:子代理 `linux-loop`,worktree `C:\tipcl-ai5\linux-loop`,分支 `ai5/linux-loop`(从 `b4cd7a2` 拉出)
- 日期:2026-11-28
- 任务书:`C:\tipcl-ai5\_shared\AI-agent-合并任务书.md` §7 的 A 包(A1–A8),另含 lead 增补的两条
  (`06补`:被拒写工具不生成检查点;`C2 第 1 条`:schema-miss 全量重发)
- 属主边界:本包只动 `src/TipCL.Core/Launcher/Ai/AiAgent.cs`(唯一属主)、`StopReason.cs` 未改、
  `Config.cs`(加 3 个默认键)、`src/TipCL.Core/Resources/locales/{zh_CN,en}.json`(加 10 个词条);
  测试侧动 `tests/TipCL.Tests/Core/AiAgentLoopTests.cs`(新增)、`AiRetryTests.cs`(假网关扩展)、
  `AiSchedulerTests.cs`(1 条既有断言按 A6 有意调整,见 §「既有断言的有意改动」)。
  **未改** `Tools.cs` / `ToolRunner.cs` / `AiPage.cs` / `Client.cs`。

---

## 0. 一句话结论

A1–A8 全部按任务书修完并转绿;`06补`(拒绝即无副作用)一并修完,并有「关掉修复即变红」的反证;
`C2 第 1 条`按 lead 裁决的**注册表判据**实现。新增/调整用例 20 条,全量测试全绿。

---

## 1. 状态总表

| 条目 | 状态 | 一句话 | 用例 |
|---|---|---|---|
| A1 收敛终态事件 | ✅ 完成 | 整轮只在唯一出口发一条终态事件,并新带 `stop_reason` | `TailRoundFailure_StillEmitsExactlyOneTerminalEvent` |
| A2 一次失败报两遍 | ✅ 完成 | 删掉收尾那条重复报错,只留上游原文,文案走 i18n | `StreamFailure_ReportsExactlyOnceWithUpstreamText` |
| A3 微压缩没接进主循环 | ✅ 完成 | 每轮 `MaybeCompact` 之后接 `Microcompact`,保留最近 5 条 | `Microcompact_RunsEveryRound_AndKeepsTheMostRecentToolResults` |
| A4 上下文溢出没有反应式压缩 | ✅ 完成 | 识别 overflow → 强制压一次 → 重发该轮,每轮最多 1 次 | `ContextOverflow_TriggersOneReactiveCompactAndRetriesTheRound`、`ContextOverflow_TwiceInOneRound_DoesNotLoopForever` |
| A5 没有非流式兜底 | ✅ 完成 | `ChatOnce` 兜底,工具表 == 本轮流式实际下发那份 | `IncompleteStreamArgs_FallsBackToChatOnce_WithTheSameToolTable` |
| A6 length 不续写 / 空回复 | ✅ 完成 | length 最多续写 2 次 → `truncated`;空 → `empty_response`;`COMPLETED` 真正被赋值 | `FinishReasonLength_ContinuesAtMostTwiceThenTruncated`、`EmptyReply_ReportsEmptyResponseTerminal`、`StopReasonCompleted_IsAssignedAfterToolsRan`、`StopReasonNoToolCall_ForPureTextReply` |
| A7 轮数硬编码 12 | ✅ 完成 | 读 `ai_max_rounds`,默认 20(**行为变化,见 §5**) | `MaxRounds_ComesFromConfig`、`MaxRounds_DefaultsToTwenty` |
| A8 兜底模型空转 | ✅ 完成 | 粘性切换 + 切换记录 | `RateLimited_SticksToFallbackModel_AndLogsTheSwitch`、`RateLimitedWithoutFallback_KeepsRetrying` |
| 06补 被拒写工具仍生成检查点 | ✅ 完成(增补) | 权限判定提到快照之前 | `DeniedWriteTool_DoesNotCreateACheckpointSnapshot` |
| C2 第 1 条 schema-miss 全量重发 | ✅ 完成(增补) | 注册表里没有 → 记 `tool_schema_miss` + `forceAll` 重发一次 | `UnknownToolCall_RetriesOnceWithFullToolTable`、`RealToolOutsideThisRoundsSelection_StillExecutes_WithoutRetry` |

---

## 2. 做了什么(逐条)

### A1 收敛终态事件 ✅
- 证据(改动前):`AiAgent.cs` 轮数用尽后的收尾轮若 `tail.Failed`,只写 `Store.LogEvent`,**一个事件都不发**;
  而 `AiPage.cs:729-744` 只有收到 `error`/`done` 才 `FinishRun`,于是页面永远停在「生成中」。
- 修法:`RunAsync` 改成「循环只置状态、唯一出口发事件」。所有结束路径(收尾轮成功/失败、压缩熔断、空回复、
  取消、正常收尾)都汇到同一个出口,由它发**恰好一条** `done` 或 `error`,并带 `stop_reason`。
- 顺带:给 `AiAgentEvent` **新增** `StopReason` 字段(终态事件里填),`FinishReason` 的语义**一字未改**
  (仍是模型的 `finish_reason`),符合 §4.4 向后兼容。
- 用例:`TailRoundFailure_StillEmitsExactlyOneTerminalEvent` —— 收尾轮持续 429 时断言
  「终态事件恰好 1 条、是 error、`StopReason == stream_failed`、没有 done」。

### A2 一次失败报两遍 ✅
- 证据(改动前):`StreamOnceAsync` 里发一条(上游原文),回到 `RunAsync` 又发一条 `stream_failed`,
  页面渲染成两个气泡 + 两次 toast。
- 修法:失败路径只把报文带回去(`RoundState.Error`),由 A1 的唯一出口发一条;文案优先用上游原文,
  没有时用词表里的「接口没有返回内容,请稍后重试」/「AI 接口出错」(已进两份词表)。
- 用例:`StreamFailure_ReportsExactlyOnceWithUpstreamText` —— 断言恰 1 条 error、文本含 `401`、
  不含内部代号 `stream_failed`。

### A3 微压缩接进主循环 ✅
- 证据(改动前):`Compact.Microcompact` 的唯一调用点是 `AiSelfTest.cs:281`。
- 修法:每轮 `MaybeCompact` 之后调用新增的私有 `Microcompact(messages, round)`;阈值照参考版
  `Math.Max(4000, Compact.AutoThreshold(auto) - 16000)`,其余用 `MicroConfig` 默认(保留最近 5 条工具结果)。
  真的清理了才发一条 status(「已清理较早的工具结果(N 条)」)并落盘消息。
  `idleSeconds` 传 0(`Microcompact` 自己的契约是 `> 0` 才按空闲触发,0 = 不知道空闲多久),靠阈值这条路触发。
- 测试接缝:`internal MicroConfig? MicroConfigOverride`(生产恒为 null)。**理由**:微压缩阈值与整段压缩阈值
  同源于 `ai_context_window`,不压低就得造十几万 token 的历史才能触发一次,用例又慢又脆。
- 用例:8 条大工具结果 + `threshold_tokens=4000/keep_recent_tool_results=3` → 断言最近 3 条保留原文、
  5 条变成 `Compact.Placeholder`、并出现清理提示。

### A4 上下文溢出反应式压缩 ✅
- 证据(改动前):`Client.IsContextOverflow` 零调用者。
- 修法:新增 `ReactiveCompact(round)`(照参考版 `_force_compact`,`Agent.cs:1532-1559`):
  不判断「该不该压」,直接压一次、替换对话、记 `CompactBoundary{trigger:reactive}`、`NoteCompactSuccess`;
  压成功就重发该轮,`reactiveBudget`(每轮 1)用尽后仍溢出则把文案换成用户看得懂的一句
  「上下文太长,压缩一次后仍然超出模型窗口,请开新对话或减少附件」。
- 用例:两条(一次溢出→成功、连续两次溢出→不循环)。

### A5 非流式兜底 ✅
- 修法:`RoundState` 新增 `Tools` —— **本轮真正下发的那份工具表**(预设筛选 + MCP 合并 + 子代理只读过滤),
  在 `StreamOnceAsync` 开头算一次并固定;`FallbackOnceAsync` 用它发 `Client.ChatOnce`,**绝不用全量表**。
  只处理「结构性没走通」(Client 装配层给出的三条信号:工具参数不完整 / 接口超时 / 连接中断),
  不去吞掉限流/认证这类错误(否则会把额度不足掩盖成「重试成功」)。
- 用例:`IncompleteStreamArgs_FallsBackToChatOnce_WithTheSameToolTable` —— 断言第 1 次是流式、
  第 2 次是非流式、**两次请求的 tools 集合逐个相等**、整轮不算失败。
- 子代理:只读过滤加在**本轮流式请求下发的工具表**上(不是只加在兜底路径),见 §「12补」。

### A6 length 续写 / 空回复 / 枚举赋值 ✅
- 修法:`finish_reason == "length"` 时有正增量就续写(最多 2 次,续写**不占轮次**,照参考版轮号不变),
  追加的提醒文案用参考版原文「请从断开处继续,不要重复。」;续写次数用尽 → `truncated`。
  既没正文也没工具调用 → `empty_response`(以前被当成 `no_tool_call` 静默收尾)。
  赋值:`TRUNCATED` / `EMPTY_RESPONSE` / `COMPLETED` / `MAX_ROUNDS` / `STREAM_FAILED` / `CANCELLED` / `ERROR`
  现在都会真实出现;`PENDING_TASK` / `RATE_LIMITED` **仍未使用**(Linux 侧没有后台任务与限流终止态,见 §6)。
- `COMPLETED` 的口径:**这一轮执行过工具**(或续写过)再收尾 = `completed`;一句工具都没调过 = `no_tool_call`。
  这样场景 1(纯文字→`no_tool_call`)与场景 2(工具→文字→`completed`)同时成立。
- 用例:4 条(length 续写、空回复、`completed`、纯文字 `no_tool_call`)。

### A7 轮数读设置 ✅
- 修法:`AiAgent.MaxRounds` 改成「显式赋值优先,否则每次读 `Config` 的 `ai_max_rounds`」;`Config.DEFAULT_CONFIG`
  新增 `ai_max_rounds = 20`。
- 用例:`MaxRounds_ComesFromConfig`(配 3 → 3 轮工具 + 1 次不带工具的收尾,共 4 个请求)、
  `MaxRounds_DefaultsToTwenty`。

### A8 粘性 fallback ✅
- 修法:`ShouldTryFallback` 判「配上备用模型 + 还没切过 + 这次是限流/5xx」,`TryFallbackModel` 切换并记
  `ModelFallback` 会话事件 + `model_fallback` trace + 一条 status;之后 `RequestSettings()` 把 `ai_model`
  换成备用模型,**本轮之后所有请求都用它**(粘性),`AiAgent.Model` 记当前生效模型。
- **顺序修正**:fallback 原本排在「3 次重试之后」,而 429 是可重试的 —— 3 次重试会把同一个限流错误重发 3 遍,
  备用模型永远轮不到。现在改成「先试 fallback,再走重试」。
- 用例:两条(切到 backup-model 且两次请求的 model 依次是 main → backup;没配备用模型时行为不变)。

### 06补 被拒的写工具仍会先生成检查点 ✅(lead 增补)
- 证据:`AiAgent.ExecuteOneAsync` 原顺序是 `SnapshotForTool` → `_runner.RunAsync`(内含权限裁决),
  于是 plan 档下被拒的写工具照样在 `Checkpoint` 流水里留下一条快照。
- 修法:在快照之前加一道**保守前置**——写工具 + 未被「一直允许」+ 不处于强制确认态 + `Decide` 判 deny
  → 不做任何快照,直接交给 `RunAsync` 走它原本的拒绝路径(返回 `permission_denied`)。
- 反证(复现 → 修复 → 转绿三步):
  1. 复现:把前置条件临时改成 `false && ...` → `DeniedWriteTool_DoesNotCreateACheckpointSnapshot` 立刻红
     (`被拒的写工具不该生成检查点`),且 `Checkpoint` 流水里出现
     `{"turn_id":"…","files":[{"path":"…\\.minecraft\\default\\mods",…}]}` —— 与被 t-linux 抓到的证据同形。
  2. 修复:恢复前置判断 → 同一条用例转绿。
  3. 断言:`ToolRunner.TurnHasOps` 仍为 false,且工具消息里是 `permission_denied`。

### C2 第 1 条 schema-miss 全量重发 ✅(lead 增补)
- 判据(lead 裁决为「方案 A:注册表判据」):返回的 `tool_call` 名字**不在本机认识的工具集合**里
  (`Tools.Schemas` + 本轮连上的 MCP 工具)→ 记 `tool_schema_miss`(trace + 会话事件 `ToolSchemaMiss`)
  → 换 `SelectToolSchemas(forceAll: true)` 重发这一轮,每轮最多 1 次。
  **子代理在全量重发时仍然只读**(`RoundTools` 里统一过 `FilterSubagentTools`)。
- 两棵树的**有意差异**:Windows 的判据是「本轮没下发」(`list4` 每轮被覆盖成按需选出那份),
  Linux 用「注册表里没有」。理由与裁决见 §4。
- 用例:两条 —— ①不认识的名字触发重发且工具数变多;②「本轮没下发但注册表里有」的只读工具
  (`search_versions`)**照常执行、不重发**(这条专门钉住方案 A,防止以后被改回 B)。

---

## 3. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | A1–A8 + 06补 + C2第1条 + S 包 Linux 侧(唯一属主) |
| `src/TipCL.Core/Launcher/Config.cs` | 新增默认键 `ai_max_rounds=20`、`ai_subagent_max_rounds=4`、`ai_fallback_model=""` |
| `src/TipCL.Core/Resources/locales/zh_CN.json` | 新增 10 条词条(清单见 §7) |
| `src/TipCL.Core/Resources/locales/en.json` | 同上(键集合与 zh 完全一致) |
| `tests/TipCL.Tests/Core/AiAgentLoopTests.cs` | **新增** 20 条用例(本包全部验收) |
| `tests/TipCL.Tests/Core/AiRetryTests.cs` | `ScriptedChatServer` 扩展:请求快照(流式/非流式、model、下发工具名)+ 逐条脚本化应答 + 非流式转 JSON |
| `tests/TipCL.Tests/Core/AiSchedulerTests.cs` | **1 条既有断言按 A6 有意调整**(见下) |
| `progress/AI5-A.md` | 本文件 |

### 既有断言的有意改动(单独列出,便于验收核对)
`tests/TipCL.Tests/Core/AiSchedulerTests.cs:298`(`AiAgentParallelToolsTests.ReadOnlyCallsRunInParallel_EventsAndToolMessagesKeepOriginalOrder`):
`Assert.Equal(StopReason.NO_TOOL_CALL, agent.stop_reason)` → `Assert.Equal(StopReason.COMPLETED, agent.stop_reason)`。
**理由**:A6 要求「执行过工具再收尾 = `completed`」,这条老断言锁的正是被 A6 改掉的行为。行上写了理由。
除这一条外,没有改任何既有断言。

---

## 4. 两棵树的有意差异(给 `docs/ai-agent-parity.md` 用)

`schema-miss 全量重发的触发条件`:
- **Windows**:「本轮按需选出的工具表」里没有这个名字就触发(`Agent.cs` 的 `list4` 在每轮开头被覆盖成
  `Tools.SelectToolSchemas(...)` 的结果)。
- **Linux**:「本机注册表(`Tools.Schemas` + 连上的 MCP 工具)」里没有这个名字才触发。
- **为什么不同**(lead 2026-11-28 裁决「方案 A」):Linux 的执行层(`ToolRunner` 只认 `ToolMeta`)本来就不要求
  工具先被下发,所以「调了未下发但真实存在的工具」在 Linux 上是**能正常执行的**;按 Windows 判据就变成了
  「丢弃整轮 + 重发」,那是把一条能跑的路改坏。实测反证:`AiAgentParallelToolsTests` 里 6 个调用中有
  `search_versions` 未命中关键词、不在当轮下发列表里,按 B 判 → 整轮丢弃 → 并发峰值从 1 变 0、工具消息全没了。
  这条差异已用用例双向钉住(`UnknownToolCall_RetriesOnceWithFullToolTable` 正向、
  `RealToolOutsideThisRoundsSelection_StillExecutes_WithoutRetry` 反向)。

`ToolRunner.IsUnknownTool` 与本实现的关系:linux-memperm 的判据是「执行**后**看结果载荷是 `unknown_tool`」,
本实现是「发起执行**前**看名字在不在注册表」——同一类问题的两个层次,互不依赖。本实现**不依赖**
`ToolCall.code` 的前置修复,因此不会被它阻塞。

---

## 5. 行为变化(必读)

1. **轮数上限默认 12 → 20**(A7,任务书明确要求)。影响:一次对话最多多跑 8 轮工具调用。
2. **执行过工具的收尾,`stop_reason` 从 `no_tool_call` 变成 `completed`**(A6)。影响:任何按
   `no_tool_call` 判断「要不要提示用户」的下游逻辑,对「工具轮」不再命中;纯文字回复仍是 `no_tool_call`。
3. **失败时只发一条 error**,文案是上游原文(不再是内部代号 `stream_failed`)。`stop_reason` 仍是
   `stream_failed`(没有改名)。
4. **限流时先换备用模型**,而不是先把同一个限流错误重发 3 遍(A8 的顺序修正)。没配 `ai_fallback_model` 时行为不变。
5. 「本机不认识」的工具名现在会触发一次全量重发(每轮 1 次),比以前多花一次请求 —— 但那一轮本来也只能拿到
   `unknown_tool` 错误。

---

## 6. 遗留问题

1. **`StopReason.PENDING_TASK` / `RATE_LIMITED` 仍未赋值**。Linux 侧没有后台任务回报机制(没有 `pending` 队列),
   也没有「限流耗尽」这个终止态(限流会走重试或 fallback,最后归到 `stream_failed`)。A6 只要求「让这些枚举
   真正被赋值」,其中 `COMPLETED/TRUNCATED/EMPTY_RESPONSE/MAX_ROUNDS/ERROR/STREAM_FAILED/CANCELLED` 已实现,
   这两个的语义在 Linux 还不存在,不硬造。建议在 R 包的差异清单里记一条。
2. **`ToolSource.ForModel(...)` 的接线待合并后补**(lead 已同意)。`AiAgent.cs` 回填 tool 消息处
   (`messages.Add(PyDict.Of("role","tool",...))`)合并后要改成
   `"content", ToolSource.ForModel(invocation.Call.name, result)`;该类型在 linux-memperm 的 `1975689`,
   本分支编译期不存在,现在引用会编译不过,故未加。
3. **`AiAgent.MicroConfigOverride`** 是为用例加的 `internal` 接缝。生产路径恒为 null。如果评审认为不想要生产代码里
   的测试接缝,可以改成「给 `Microcompact` 增加可选参数」或把阈值也做成配置项 —— 但那会引入任务书未定义的新设置项,
   所以选了接缝。
4. **`ContextOverflow` 的压缩会丢上下文**(任务书标注的风险,已知)。本轮按「每轮最多 1 次 + 仍溢出就明确报错」
   兜住,没有做「按条回退式压缩」。
5. **`AiAgent.MaxRounds` 每次 `get` 都读一次 Config**。若用户在对话中途改了 `ai_max_rounds`,本轮循环边界已在
   `RunAsync` 开头取过,不受影响(有意)。

---

## 7. 新增 / 修改的词条清单(合并时核对,别丢)

两份词表各新增 **10** 条(键 = 中文原文,值在 `en.json` 是英文译文):

| key(中文原文) | en |
|---|---|
| 接口没有返回内容,请稍后重试 | The API returned nothing; please try again later |
| 流式接口没走通,换成非流式再要一次 | Streaming failed; retrying once without streaming |
| AI 接口出错 | The AI API returned an error |
| 模型没有返回任何内容,请再试一次 | The model returned nothing; please try again |
| 回复被长度限制截断了,需要的话让我继续。 | The reply was cut off by the length limit; ask me to continue if you need more. |
| 回复被长度限制截断了,正在接着写(第 {n}/{total} 次) | The reply was cut off by the length limit; continuing ({n}/{total}) |
| 主模型暂时不可用,正在改用备用模型 {model} | The primary model is unavailable; switching to the fallback model {model} |
| 上下文过长,压缩一次后仍然超出模型窗口,请开新对话或减少附件 | The context is too long and still exceeds the model window after one compaction; start a new chat or attach fewer files |
| 上下文超出模型窗口,正在压缩历史... | The context exceeds the model window; compacting history... |
| 已清理较早的工具结果({cleared} 条) | Cleared older tool results ({cleared}) |

复核:`zh_CN.json` 与 `en.json` 现在都是 **3686** 键,键集合双向差集为 **0**(用 Python `json` 实测)。

> 合并提醒:`.json` 按文本合并后必须重跑 `pack-core-resources.ps1` 重新生成 `.gz`,否则
> `CoreResourcesPackTests` 会当场红。

---

## 8. 验收数字

| 判据 | 基线(AI5-0) | 本包 | 结论 |
|---|---|---|---|
| Linux Release 构建 | 0 error / 1 warning(`LayoutEditor.cs:196` CS0649) | 0 error / 1 warning(同一条) | 不新增警告 |
| Linux `dotnet test`(带三条排除) | 1594 过 / 11 跳 / 0 败 / 1605 | **1614 过 / 11 跳 / 0 败 / 1625** | +20 条用例,全过 |
| Linux `--ai-selftest` | 110 过 / 0 败 | **110 过 / 0 败 / 0 跳** | 不回退 |
| 测试工程警告 | — | 0 warning(本包新增文件不引入警告) | — |
| compact 体积 | 5,759,488 B(基线已超红线 2,224 B) | **5,759,488 B(未变)** | 见下 |

> 体积:按 lead 2026-11-28 裁决(D-11/D-28 先例)本轮**不回收**,只记录改动前后字节数;
> 合格性由用户裁决,本文件不作合格/不合格结论。
>
> **本包改动前后的字节数:`5,759,488 B → 5,759,488 B`(零变化)**。依据:`git status --short` 里
> **没有任何 `windos/**` 文件**(实测计数 0),本包只改了 Linux 树(`src/TipCL.Core/**`、`tests/**`)与
> `progress/**`,而 compact 指的是 Windows 侧 net48 单文件 exe,其输入文件一个都没动,所以字节数不可能变化。
> 本机缺 net48 参照程序集、Windows 树编译不了,故不能重新出包复核;若需实测,由能编译 Windows 树的一方复跑一次。

---

## 9. 复现 → 修复 → 转绿(三步证据索引)

| 条目 | 复现(改前应当红) | 修复 | 转绿 |
|---|---|---|---|
| A1 | 收尾轮失败时页面停在「生成中」:改前该路径一个事件都不发(代码证据 `AiAgent.cs` 原 263-276) | 唯一出口 | `TailRoundFailure_StillEmitsExactlyOneTerminalEvent` 绿 |
| A2 | 同一次失败两条 error(代码证据:原 631-635 + 255-260) | 删除重复 | `StreamFailure_ReportsExactlyOnceWithUpstreamText` 绿 |
| A3 | `grep Microcompact` 只有 `AiSelfTest.cs:281` | 接进主循环 | `Microcompact_RunsEveryRound_...` 绿 |
| A4 | `grep IsContextOverflow` 零调用者 | `ReactiveCompact` | 两条 A4 用例绿 |
| A5 | 兜底请求不存在(只有同一条流重试) | `FallbackOnceAsync` | `IncompleteStreamArgs_FallsBackToChatOnce_...` 绿 |
| A6 | `grep StopReason.COMPLETED` 零赋值 | 续写 + 赋值 | 4 条 A6 用例绿 |
| A7 | `MaxRounds = 12` 硬编码 | 读 config | 2 条 A7 用例绿 |
| A8 | `grep ai_fallback_model` 在 `src/TipCL.Core` 零读取方 | 粘性切换 | 2 条 A8 用例绿 |
| 06补 | 关掉前置判断 → `DeniedWriteTool_...` 红 + 流水里出现快照(实测,见 §2 06补) | 前置判断 | 同一条用例绿 |
| C2 | 改前 `unknown_tool` 只回一个错误载荷 | 注册表判据 + forceAll 重发 | 2 条 C2 用例绿 |

---

## 11. i18n 别名修正(`T` → `Tr`)与审计数字(2026-11-28 补做)

**问题(lead 指出)**:本包一开始把词表别名写成 `private static string T(string zh) => I18nCore.Tr(zh);`,
而全树其他 12 个文件一律叫 `Tr`;`tools/i18n-audit.py` 的 `WRAPPERS` 只认 `{"Tr"}`,
于是这 11 处**实际已接线**的调用在审计里全被算成未接线 → i18n 守卫对 `AiAgent.cs` 失明,
per-file 数字假回退(32 → 49),真正的未接线文案会被淹掉。

**处理**:
1. 别名改名为 `Tr`(1 处定义 + 11 处调用),跟随全树约定。**没有**去改审计工具的 `WRAPPERS`
   (`T` 是通用类型参数名,加进去会在别的文件造成假阴性)。
2. 顺带把本包**新增**的 7 处「必须保持字面量、永远不能翻译」的串打上 `i18n:ignore(理由)`:
   - `ContinuationPrompt`(给模型看的续写指令,与参考版同文);
   - `StreamIncomplete` 的 3 条内部匹配串(工具参数不完整 / 接口超时 / 连接中断);
   - `ShouldTryFallback` 的 `限流` / `额度` / `上游负载` 三条内部匹配串。
   这些是拿去比对方报文的**检测串**,接线会让检测静默失效,所以显式标注。
3. 修掉一个**我自己写错的词表键**:代码里是「上下文**太长**,压缩一次后…」,词表里却写成了
   「上下文**过长**,…」→ 审计报 `missing en: 2`。已把键改成与代码一致,并补上漏掉的
   「上下文反复逼近窗口上限,已中断本轮以保护额度」。现在 `missing en: 0 / missing zh: 0`。

**审计数字(改前 → 改后)**

| 指标 | 改前 | 改后 | 说明 |
|---|---|---|---|
| `wrapped` | 2804 | **2815** | +11(11 处 `T(...)` 被审计认出来) |
| `unwrapped` | 3190 | **3172** | −18 |
| `AiAgent.cs` 未接线条目 | 49 | **31** | 基线是 32,现在**低于基线** |
| `ignored lines` | 119 | 126 | +7(新增的内部匹配串标注) |
| `missing en` / `missing zh` | 0 / 0 | **0 / 0** | 中途一度是 2,已修(见上第 3 点) |
| 词表键数 | zh 3686 / en 3686 | zh **3687** / en **3687** | 键集合仍相等 |
| `T("` 残留 | 11 | **0** | — |

`AiAgent.cs` 剩下的 31 条全部是**既有**文案或不该翻译的东西,逐类核对过:
`RuntimeContext` 系统提示词的 8 条(给模型看)、`messages 不能为空`(异常消息)、
`已停止` / `正在整理...` / `步骤太多,正在收尾...`(既有用户可见状态行,见下)、
`工具轮数已达上限…`(给模型看)、`这一步的改动无法回退:`、`正在压缩历史...`、
`[调用工具:` / `[子任务]`(给模型看)、子代理两条错误文案、`RetryNotice` 的 4 段拼接片段、
`AI 接口出错`(既有那条)、`AI 对话异常:`。

**关于「用户可见但没接线」的判定**(lead 问的第 2 条):被点到的三条
`正在整理...`、`步骤太多,正在收尾...`、`已停止` **全部是既有文案**——
实测 `git show b4cd7a2:src/TipCL.Core/Launcher/Ai/AiAgent.cs` 里三条各出现 1 次,
本包只是把它们随 A1 的重构**挪了位置**,没有新造。按「不动既有文案」的口径**未接线**,
在此说明一句「既有文案,本次仅移位」。

> 顺带发现(不在本包范围,交 lead 判断):`AiPage.cs:731` 用 `ev.Text == I18nCore.Tr("已停止")`
> 判断「用户点了停止」。因为事件侧的 `已停止` 是裸字面量,英文界面下 `Tr("已停止")` 会返回英文,
> 而 `ev.Text` 仍是中文 → 比较不成立,页面会走「对话失败」分支而不是「已停止」。
> 这是**既有**的 en 显示缺陷;要修的话把 `AiAgent.cs:295` 那条接线即可,但那是既有文案,
> 本包按纪律没动。

---

## 12. qa 合并后跟进的 6 件事(2026-11-28 第二批)

`qa` 合并后验出 6 项,逐条处置如下。**1、2 已在前一节做完**(别名改名 + `i18n:ignore`),
这里记 3–6 与新增的修法。

### 3 ⚠→✅ `场景 14` 收尾轮文本为空 —— **真缺陷,已修**

**根因(不是 A1 丢事件,而是 `LastText` 被两处无条件覆盖)**:
- 主循环每轮结尾无条件 `LastText = full` —— 「调了工具但没说话」的那一轮会把上一轮已经拿到的答案抹成空;
- 收尾轮同样无条件 `LastText = tail.Text`,而收尾轮是「不带工具、请直接用已有信息回答」,
  模型完全可能又回一个工具调用(那一轮的工具不执行、正文为空)→ `LastText` 变空字符串。

**修法**:两处都改成「**这一轮真吐了正文才更新** `LastText`」,保留最后一段非空助手文本。
对子代理也更稳(它把 `LastText` 当结论带回去)。
**用例**:`TailRoundWithoutText_KeepsTheAnswerFromEarlierRounds`(工具轮有话说 + 收尾轮只回工具调用 →
断言 `LastText` 仍是前面的答案、`stop_reason == max_rounds`、终态事件恰好 1 条)。

### 4 ⚠→✅ `场景 15` A8 没切到 `ai_fallback_model` —— **根因不是键名,是内置网关不让改模型;已加诚实性闸门**

**实测根因**:`Client.ResolveEndpoint`(内置分支,`Client.cs:181`)把请求体里的 model **固定**成
`Defaults.DEFAULT_MODEL`(= `deepseek-v4-flash`),`ai_model` / `ai_fallback_model` 在这一档**根本不参与请求**。
`qa` 的回放框架跑在内置(builtin)档,所以「实得模型 = deepseek-v4-flash」是必然的 —— 不是键名写错:
键名我读的就是 `ai_fallback_model`(与 `SettingsPage.SaveLoad.cs:346` 的写入方一致),在 **custom 档**下
`agent.Model == "backup-model"` 且第二次请求的 model 确实是 `backup-model`(`RateLimited_SticksToFallbackModel_AndLogsTheSwitch` 锁着)。

**修法(诚实性闸门)**:新增 `AiAgent.FallbackApplies(fallbackModel, settings)`,只有**自定义端点(custom)档**
才做降级。理由:内置档做降级只会出现「告诉用户已切到备用模型、实际发的还是内置默认模型」的**假承诺**,
比不降级更糟(这类假承诺正是 A2 要消灭的东西)。
**用例**:`Fallback_OnlyAppliesToCustomEndpoints`(custom → true;builtin → false;custom 但没填地址 → false;
没配备用模型 → false)。
**要 qa 改的地方**:场景 15 必须在 **custom 档**(指向假网关)跑,断言第 2 次请求的 model == 配置值。
**需要拍板的事**(我没做):要让内置网关也支持备用模型,等于让用户能改内置网关的模型名 ——
这与「内置网关什么都不让用户改」的既有口径冲突,属产品变更,请 lead 裁决。

### 5a ✅ `场景 08` 续写拼接方式 —— **确实无条件加了 `\n\n`,已改成首尾直接相接**

**根因**:续写时每轮是**新的** `RoundState`,所以判断「新文本是否以已拼内容开头」永远为假,
于是每次都走 `concat + "\n\n" + text` 分支 —— 等于无条件插空行。
**修法**:直接 `string.Concat(_continuationParts) + state.Text`,与参考版 `_full`
(`string.Concat(string.Concat(continuationParts), text18)`)逐字一致,不加任何分隔符。
**用例**:`LengthContinuation_JoinsWithoutInsertingBlankLines`(断言 `前半段后半段`,且不含 `\n\n`)。
**给 qa 的断言口径**:`agent.LastText == 各段直接相接`(不要断言 `\n\n`)。

### 5b ✅ `场景 09` 兜底把残段与兜底文本拼接 —— **state 早已替换,界面会重复;已补齐界面侧**

**实际状态**:
- **落盘/上下文侧本来就是替换**:`ApplyChatOnce` 在「兜底文本不以残段开头」时 `state.Text.Clear()` 再装兜底文本,
  所以 `messages` 与 `LastText` 里**没有**残段(用例 `FallbackReplacesStreamedFragment_InsteadOfAppendingIt`
  断言 assistant 消息里查不到「前半段」)。
- **但界面侧会重复**:已渲染出去的半截收不回来,旧代码又用 `"\n\n" + text` 当增量发出去 → 气泡里是「残段 + 空行 + 完整答复」。
**修法**:替换时先发一条**含「正在重试」**的状态(`AiPage.IsRetryNotice` 认这个词 → `ResetStreamingBubble()`
清掉本轮气泡),再把完整正文当增量发。文案进词表:
「兜底返回的内容与已显示的不一致,正在重试(已换成非流式重新回答)」。
**用例**:同上那条(断言回填内容不含残段,且出现过带「正在重试」的状态)。

### 6 ✅ `场景 12` 子代理工具表里出现 `install_mod` —— **过滤是生效的,断言查错了对象(附实测清单)**

自己构造子代理请求,把**实际下发的 tools** 打出来核对(一次性证据,已固化成常驻断言):
```
父请求(Records[0])里的写工具 : install_mod, install_modpack, create_instance, launch_game
子请求(Records[1])的工具表   : ask_user, get_launcher_state, list_instances, search_mods, list_mods,
                              diagnose_launch, list_mod_configs, read_mod_config, update_plan, remember,
                              forget, get_latest_log, get_crash_report
```
**`install_mod` 只在父请求里,子请求里没有**(也没有 `dispatch_subagent`)。所以 `qa` 那条断言应该是查了
**父回合**那次请求、或没有按「第几次请求」区分。
**已固化**:`Subagent_IsReadOnlyOnly_AndReturnsTheNewFields` 现在同时断言
`Assert.Contains("install_mod", Records[0].Tools)`(父表有)与 `Assert.DoesNotContain(subTools, IsWriteTool)`(子表没有),
以后两边都不会再被误判。

### 词表新增(第二批 1 条)

| key | en |
|---|---|
| 兜底返回的内容与已显示的不一致,正在重试(已换成非流式重新回答) | The fallback answer did not match what was already shown; retrying with a fresh non-streaming reply |

另:第二批同时修掉了两个**我自己写错的键**(见上一节第 3 点:`上下文过长` → `上下文太长`;
补 `上下文反复逼近窗口上限,已中断本轮以保护额度`)。词表现在 zh **3688** / en **3688**,键集合相等,
`missing en: 0 / missing zh: 0`。

---

## 10. 构建与测试命令(本包实际使用)

```powershell
# 本会话必须覆盖 DOTNET_ROOT(用户级变量在本会话里仍是旧值)
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false `
  --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest
```

未运行:`--smoke`、`tools/verify-smoke.ps1`、ClickSweep、VisualTour、任何 Avalonia 界面或浏览器/explorer 拉起
(遵守「绝对不许抢前台」)。测试过滤里始终排除 Terracotta / ClickSweep / OpenUrl。