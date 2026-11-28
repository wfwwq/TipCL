# 两版内置 agent 差异清单与合并方案草案(AI5-R)

> 调研日期:2026-11-28。只读调研,**不含任何实施**;是否合并、以哪边为准,由用户在闸门处拍板。
> 基线:`b4cd7a2`(worktree `C:\tipcl-ai5\research-r`,分支 `ai5/research-r`)。
> 口径:文件名与符号名为准,行号会随改动漂移。本文所有结论都标了证据文件与符号。
> 上游依据:`windos/docs/改造工程:2611/AI-agent-合并任务书.md` §7 R 包(本节的验收对象)、§2 代码地图;a1 §7.1、a2 WP-AG1 §4(两套合并思路的来源)。

---

## 0. 一句话结论

两棵树**共 38 个同名文件、8932 行非空行**,其中真正逐行相同的是**数据/兼容层与网络层**(Client、Images、TomlLite、ModelCaps、FileMemory、VisionCaps、Conflict、AiSession 等基本一致),**差异全部集中在「谁是主循环」**:

- **Windows** 是「一个静态类 + 状态机 + 插件总线 + 后台任务」的重量级实现(`Agent.cs` 1770 行,`RunAgentCore` 一个方法 1100+ 行);
- **Linux** 是「实例类 + async + 深模块拆分」的轻量实现(`AiAgent.cs` 756 行 + `ToolRunner*.cs` 10 个分部),但没有状态机、没有非流式兜底、没有反应式压缩、没有续写、没有计划审批、没有插件运行时。

**合并方案的核心判断**:先做**数据单源**(工具 schema / ToolMeta / 系统提示词 / 权限规则匹配 / token 估算),再共享**已经事实上同源的网络与工具层**(Client / Compact / Tokens / Usage / 模型能力表),最后才谈主循环。**主循环不要先合** —— 在 AI5-T 的场景套件能在两棵树跑出同一套断言之前合主循环,等于把 T 包的安全网价值清零。

---

## 1. 方法与覆盖面

### 1.1 实际读过的文件(逐行)

| 树 | 文件 | 行数 | 读法 |
|---|---|---|---|
| Linux | `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | 756 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | 813 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/ToolRunner.EngineMcp.cs` | 71 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs` | 231 | 全文逐行(ToolMeta 78 条 / ToolCallStatus / ToolCall) |
| Linux | `src/TipCL.Core/Launcher/Ai/Scheduler.cs` | 228 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/PermissionRules.cs` | 124 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/Compact.cs` · `CompactTypes.cs` | 246 / 88 | 全文逐行 |
| Linux | `src/TipCL.Core/Launcher/Ai/AiData.cs` · `Tools.cs`(节选) · `AiSelfTest.cs`(节选) | 175 / 800 / 408 | 全文 + 定点 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 1770 | 全文逐行(分三段读完) |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Permission.cs` | 507 | 全文逐行 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Scheduler.cs` | 142 | 全文逐行 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Compact.cs` | 249 | 全文逐行 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/ToolMeta.cs` · `ToolMetaInfo.cs` · `CompactState.cs` · `AutoConfig.cs` · `MicroConfig.cs` · `MicroResult.cs` · `CompactionResult.cs` · `TurnState.cs` · `TurnPhase.cs` · `AiRule.cs` · `Decision.cs` · `PermissionResult.cs` · `ToolCall.cs` · `ToolCallStatus.cs` · `StopReason.cs` · `Prompt.cs` · `AiData.cs` · `Memory.cs` | 57 + … | 全文逐行 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Tools.cs`(节选 L80-200) · `ExtraTools.cs`(Meta 段) · `Hooks.cs`(节选) · `Mcp.cs` · `McpServer.cs`(节选) · `Client.cs`(符号面) | — | 定点 |

### 1.2 用脚本补的量化证据

- 同名文件逐行相似度(两种口径,见 §2.2);
- 工具清单集合差(集合运算,见 §4.10);
- `StopReason` 各常量的赋值点计数(见 §4.4);
- 两侧测试文件清点(见 §4.11)。

### 1.3 本次**没有**覆盖(留给后续包)

- `ToolRunner.*.cs` 其余 8 个分部(AcctLayoutMp / LogsFs / PerfSaves / NewFeatures / Modpack / Content / ModDevMcp / I18nUx)的执行体细节 —— 只读了它们的入口与 Meta 分级;执行体逐行核对属于 D3/D4 各包的活。
- WPF 页面层(`AiPage.cs` / `ConfirmCard.cs` / `AiPermissionPanel.cs`)与 Avalonia 页面层(`AiPage.cs` / `AiFixView.cs`)的 UI 状态机差异 —— 只在 B3/B4 需要时做。
- `Client.cs` 的 SSE 解析逐行对照(行相似度 99%,未逐行)。

---

## 2. 总览

### 2.1 规模与结构

| 项 | Windows(`windos/src/TipCL/Core/Launcher/Ai/`) | Linux(`src/TipCL.Core/Launcher/Ai/`) |
|---|---|---|
| 文件数 | 58 | 65 |
| 主循环 | `Agent.cs`:静态类;`RunAgent`(外层,发插件事件)→ `RunAgentCore`(内层,≈L535–1425) | `AiAgent.cs`:实例类 + async;`RunAsync` + `StreamOnceAsync` + `StreamAttemptAsync` + `RunSubagentAsync` |
| 主循环显式状态 | `TurnState` / `TurnPhase` 九相状态机(idle / processing_input / awaiting_model / streaming / scheduling_tools / executing_tools / awaiting_permission / completing / error),非法转移抛 `IllegalTransition` | 无状态机;只有 `AiAgentEvent.Kind` 的状态行(status / done / error …) |
| 工具执行 | `Tools.cs`(1264 行)直接接 `BackendApi` | `ToolRunner.cs` + 9 个分部分部,接 `IApi` 门面(可注入假门面) |
| 插件 | `Core/Plugins/` 22 个文件,含 `PluginAgentBridge` / `PluginTools` / `PluginLlm` / `NodeRuntime` / `JsonRpcPeer` | `Core/Plugins/` 6 个文件,`PluginHost` 自述"不会有插件代码在本进程里跑" |
| 权限 | `Permission.cs`(规则库存 `$ROOT/ai_permissions.json`) | `ToolRunner.cs` 内的 `DecideRaw` + `PermissionRules.cs`(规则存 `config.json` 的 `ai_permission_rules`) |
| 自检 | `--ai-ui-selftest`(WPF 页面级 + 截图,`App.xaml.cs:159`) | `--ai-selftest`(无头,110 项) |

### 2.2 逐行相似度(实测)

同名文件 38 个,Linux 侧同名文件非空行 **8932 行**。

| 口径 | 结果 | 说明 |
|---|---|---|
| 位置对齐逐行相同 | **2546 / 8932 = 29%** | 第 i 行与第 i 行逐字比较,最严格的口径 |
| 集合包含(行是否在另一侧出现) | 6178 / 8932 = 69% | 上界口径,重复行与重排都会抬高它 |
| a1 报告值 | 3372 / 8932 ≈ 38% | 与本文 29% 同量级,差异来自是否剔除空行/Ordinal 口径差异 |

**分文件看**(位置对齐口径的直观版,按行相似度排序):

| 分组 | 文件 | 相似度 | 含义 |
|---|---|---|---|
| 已事实上同源(≥97%) | `VisionCaps` `Images` `TomlLite` `ModelCaps`(100%)、`FileMemory` `AiResponse` `Defaults` `Client`(99%)、`AiSession` `HttpCancel`(98%)、`Conflict` `AIClientError`(97%) | 97–100% | 复制的成本已经付过一次,**共享几乎零风险**,收益立竿见影 |
| 同源但已分叉(50–72%) | `McpClient`(70%)、`Trace`(66%)、`McpServer`(62%)、`Store`(55%)、`Hooks`(54%)、`Checkpoint`(53%)、`Scheduler`(51%)、`Preview`(50%) | 50–72% | 结构同、行为有分叉;共享前要先抹平行为差异(Scheduler 就是典型) |
| 同名但角色不同 | `Tools`(40%)、`Compact`(45%)、`Usage`(41%)、`Tokens`(46%)、`Rewind`(45%)、`Mcp`(44%)、`AiData`(5%) | 5–46% | **看名字会误判**:`AiData` 只有 5% 相同,是因为 Windows 的 `AiData` 里塞着两份没被引用的死数据(见 §4.10) |

---

## 3. 对照表(11 个维度)

判定只取四值之一:**两边一致** / **只有 Windows 有** / **只有 Linux 有** / **同名但行为不同**。

### 3.1 主循环阶段

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 入口分层 | `RunAgent`(外层:插件事件 + `BeforeTurn` 钩子)→ `RunAgentCore`(内层) | `RunAsync`(单层;MCP 连接与子代理注入都在里面) | 同名但行为不同 |
| 显式阶段机 | `TurnState`/`TurnPhase`,九相 + `ALLOWED` 转移表 + `IllegalTransition` | 无 | **只有 Windows 有** |
| 每轮进度注入 | 改写第 2 条 system 消息:"本轮已用 n/m 步"+ 剩余不足 4 步提示(`Agent.cs:678-695`) | 无 | **只有 Windows 有** |
| steer(回合中途插话) | `drainInputsFn` 每轮 drain,插入 user 消息 + `steer` 状态 + `user/message` 插件事件 | 无 | **只有 Windows 有** |
| 后台任务回报 | `pending` 表 + `_await_pending(20s)`,把任务结果作为 user 消息续轮(`pending_task` 终态) | 无(`TaskStarted` 只通知宿主画任务卡) | **只有 Windows 有** |
| 单轮请求尝试 | 内层 `for (num5 < 6)` 六次尝试 | `StreamOnceAsync` 的 `while(true)` + `retries < 3` | 同名但行为不同 |
| 终态出口 | `_result(stop_reason)` 统一收口(10 种 `stop_reason` 全部可达) | 5 个赋值点,`stop_reason` 只有 5 种可达 | 同名但行为不同(见 §4.4) |
| 每轮工具分组 | `Scheduler.Group(list13)` 后 `Scheduler.RunGroups`(同步) | `Scheduler.Group(state.Calls, key)` 后 `RunGroupAsync`(async) | 同名但行为不同(见 §3.7 / §4.2) |
| 取消 | 抛 `AgentCancelled`;插件收到 `turn/end reason=cancelled` | `OperationCanceledException` 直接向上抛;页面收 `已停止` 事件 | 同名但行为不同 |

### 3.2 重试 / 降级

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 流式重试次数与退避 | 最多 3 次;`min(4, 2^(n-1)) * (0.8+0.4·rand)`;`Thread.Sleep`,前后各 `_check()` 一次 | 最多 3 次;同一退避公式;`Task.Delay(wait, ct)` 可被打断 | 两边一致(实现不同,数值口径一致) |
| 可重试判据 | `AIClientError.Retryable()`:429/500/502/503/504 + timeout/network/stream/rate_limited | 同一份 `AIClientError.cs`(逐行 97% 相同) | 两边一致 |
| 致命错误 | `Fatal()`:401/403/auth → 直接抛 | 同 | 两边一致 |
| "已吐出正文就不重发" | `list7.Count == 0 && list6.Count == 0` | `state.Text.Length == 0 && state.Calls.Count == 0 && attempt.PendingTools.Count == 0` | 两边一致(判据等价) |
| 半截文本处理 | 每次尝试开始重置 `list6/list7`,天然不拼接 | 记 `textBase`,`state.Text.Length = textBase` 显式回滚 | 同名但行为不同(手段不同,结果一致) |
| 重试可见性 | `_status("retry")` + `Trace` | `status` 事件("接口限流,正在重试(第 n/3 次,约 x 秒后)")+ `Store.LogEvent(ModelRetry)` + `Trace` | 同名但行为不同(Linux 多一次落盘事件) |
| **非流式兜底** | 有:`Client.ChatOnce(...)`,文本与流式结果去重拼接 | **无**:`StreamOnceAsync` 只是重试同一个流;而 `Client.AssembleStream` 仍会吐"工具参数不完整,正在换一次非流式"(`Client.cs:503/539/641/673`) | **只有 Windows 有**(且 Linux 文案在撒谎,即任务书 A5) |
| **上下文溢出 → 反应式压缩** | 有:`Client.IsContextOverflow` + `_force_compact`,每轮最多 1 次 | **无**:`Client.IsContextOverflow` 零调用者 | **只有 Windows 有**(任务书 A4) |
| **模型降级 `ai_fallback_model`** | 有:`_maybe_fallback` + `_request_settings` 换 model + `ModelFallback` 事件(粘性) | **无**:`SettingsPage.SaveLoad.cs` 读写该键,`src/TipCL.Core/` 无读取方 | **只有 Windows 有**(任务书 A8) |
| **`tool_schema_miss` 全量重发** | 有:调了未下发工具 → `list3 = SelectToolSchemas(forceAll: true)` 重发一次,记 `Trace` | **无**:未知名工具在 `ToolRunner` 直接回 `unknown_tool` | **只有 Windows 有**(任务书 C2) |

### 3.3 压缩

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 微压缩 `Microcompact` | 每轮在「不需要整段压缩」的分支里调(`Agent.cs:749`),`threshold_tokens = max(4000, AutoThreshold-16000)`,`idle_threshold_minutes=30` | 实现齐全但**没接线**:唯一调用点是 `AiSelfTest.cs` | 同名但行为不同(任务书 A3) |
| 整段压缩 `CompactConversation` | 有;摘要标记 `compact_<tailId>` + `keep` | 有;同一标记形状(注释明确"照参考版",占位串、`SUMMARY_PROMPT` 逐字相同) | 两边一致 |
| 触发判定 `ShouldAutocompact` / 熔断 `CheckRapidRefill` / `NoteCompactSuccess`/`Failure` | 有 | 有(阈值对象 `AutoConfig` 字段与默认值逐字相同) | 两边一致 |
| **回合入口的历史摘要 `_trim_history`** | 有:`HISTORY_TRIGGER_RATIO=0.5` / `KEEP_RATIO=0.25` / `MIN_KEEP=4` / 旧标记 `LEGACY_KEEP=24`;标记带 `reused`,复用不重复落盘、不重复提示 | **无** | **只有 Windows 有** |
| **反应式压缩** | 有:`_force_compact`,溢出时压一次再重发 | **无** | **只有 Windows 有** |
| **压缩状态存放** | `static Dictionary<string, CompactState> _COMPACT_STATES`,**满 16 个会话直接 `Clear()`**(`Agent.cs:418-433`) | 每个 `AiAgent` 实例持有自己的 `CompactState`(`AiAgent.cs:97`) | **同名但行为不同**(任务书 D9;Linux 无此缺陷) |
| 压缩状态释放 | 无(静态字典,靠 Clear 兜底) | 随实例 GC | 同名但行为不同 |
| 压缩可见性 | `_status("compact"/"microcompact")` + `Trace` + `Store.LogEvent(CompactBoundary)` | `compact` 事件 + `Store.LogEvent(CompactBoundary)`,**微压缩没有事件**(因为没接线) | 同名但行为不同 |

### 3.4 续写(`finish_reason=length`)

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 检查 `finish_reason` | 有:`flag7` | 记录了(`state.FinishReason`)但**从不检查** | 同名但行为不同 |
| 续写 | 最多 2 次:追加 `assistant(text)` + `user("请从断开处继续,不要重复。")` 后继续请求;用一个**空回合**再走一轮(`flag3/flag4/flag5` + "用户刚在选项里做出选择…") | 无 | **只有 Windows 有**(任务书 A6) |
| 截断终态 | 仍截断则 `truncated` + 文案"…(回复被长度限制截断了,需要的话让我继续。)" | `StopReason.TRUNCATED` 定义但零引用 | **只有 Windows 有** |
| 全文拼回 | `_full()` 把 `continuationParts` 拼回 | 无 | **只有 Windows 有** |

`StopReason` 十个常量的可达性(引用点计数实测):

| 常量 | Windows | Linux |
|---|---|---|
| `completed` | 有(17 处字面量;`_result` 收口) | **永不被赋值**(只有定义) |
| `no_tool_call` | 有 | 有(`AiAgent.cs:206`) |
| `max_rounds` | 有 | 有(`:268`) |
| `truncated` | 有(`:1005`) | 0 |
| `pending_task` | 有(`:1029`) | 0 |
| `stream_failed` | 有(`:1010`) | 有(`:257`/`:268`) |
| `empty_response` | 有(`:1034`) | 0 |
| `rate_limited` | 有(字面量 4 处) | 0 |
| `cancelled` | 有 | 有(`:280`) |
| `error` | 有 | 有(`:257`) |

即 **Linux 的 `stop_reason` 只有 4 种实际取值**(`no_tool_call` / `max_rounds` / `stream_failed` / `error` / `cancelled`)。

### 3.5 计划审批

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| `update_plan` 工具本体 | 有(`Agent.cs:1325-1335`),收集 `planItems` + `planTurnId`,发 `plan` 状态 | 有(`ToolRunner.cs:537-553`),只调 `PlanChanged` 回调画计划卡 | 两边一致 |
| **计划审批闸门** | 有:`text3 == "plan" && planItems.Count > 0 && planTurnId == turn.id && !planApproved` 时调 `confirmFn("plan_approval", …)`;批准传 `AiRule` 也算批准;驳回 → 返回"计划未获批准,本轮到此为止",`plan_rejected` 记 Trace | **完全没有**:全树 grep `plan_approval` / `planApproved` 零命中(任务书 §7 P2 的 🕓 核实项:确认**不存在**) | **只有 Windows 有** |
| 审批时序 | 审批在 `Scheduler.RunGroups(:1233)` **之后**(`Agent.cs:1247-1266`)→ 驳回时写操作可能已落盘(任务书 P2 要改成先审批) | 无 | **只有 Windows 有** |
| plan 档的执行语义 | `Permission.Decide` 在规则**之前**硬停 `plan && !readonly → deny` | `DecideRaw` 同序(注释明确"与参考版同序",并说明 Linux 原先顺序反了会 allow 规则压过 plan) | 两边一致 |

### 3.6 权限模型

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 档位集合 | 10 档:`default/plan/edit/acceptEdits/auto/dontAsk/autoEdit/yolo/bypassPermissions/build` | 同 10 档(注释逐条对位) | 两边一致 |
| 档位归一化 | `NormalizePermissionMode` 还认旧别名 `standard→default`、`full→acceptEdits|yolo`、`custom→custom`;并读 `ai_confirm_writes` | `NormalizeMode` 只做「空 → default」,**不认 standard/full/custom,不读 `ai_confirm_writes`** | **同名但行为不同**(从 Windows 迁配置会得到不同档位) |
| 判定顺序 | plan 硬停 → 规则(deny>allow>ask)→ readonly allow → 档位 switch | 完全同序 | 两边一致 |
| 规则存储 | `$ROOT/ai_permissions.json`:`{version, global:{allow,deny,ask}, per_instance:{实例:{...}}}` | `config.json` 的 `ai_permission_rules`:单表数组 | **同名但行为不同** |
| 规则字段名 | 写 `toolName` / `ruleContent` | 面板写 `tool` / `content`,读取时兼容 `toolName`/`ruleContent` | 同名但行为不同 |
| 规则作用范围 | 按**会话实例**取:有 `global` + `per_instance[当前实例]`;`AiRule.scope` 决定 `AppendRule` 落哪一层 | 按**目标实例**过滤:`PermissionRules.TargetInstance` 取参数里的 `instance`,缺省会话实例(注释说这是 Linux 有意收紧,防止换实例名绕过) | **同名但行为不同** |
| 「本对话都允许」 | `confirmFn` 返回 `AiRule` → `Permission.AppendRule` **持久化写盘**;作用域 `global`/`instance` 都在 | 进程内 `HashSet<string> _alwaysAllowed`(`ToolRunner.cs:34`),重启即失效;确认卡算出的第三个值(scope)被 UI 丢弃(`AiPage.cs:969/981`) | **同名但行为不同**(Linux 侧即任务书 B3 缺陷) |
| `dontAsk` | 档位名;命中即 `deny`;**另有** Agent 级 `ai_permission_dont_ask` 开关把 `ask` 判成 deny(`Agent.cs:571/1149-1152`) | 独立开关 `ai_permission_dont_ask` 叠加在规则**之后**(`ApplyDontAsk`),只翻 `ask`;档位名 `dontAsk` 也保留同一句理由 | 同名但行为不同(两条路径不同,文案逐字相同) |
| 未知工具 | `Decide` 没有 `meta == null` 分支(会落到档位 switch) | `DecideRaw` 显式 `meta == null → deny("未知工具")`(注释说明这是 Linux 独有) | **只有 Linux 有** |
| 外部 MCP 客户端发起的写操作 | `McpServer` 直接执行,只在 instructions 里请对方自觉确认 | `ForceConfirmWrites` + `ExternalWriteGate`:写操作一律弹确认、确认卡的"以后都允许"不生效 | **只有 Linux 有** |
| ModDev 测试环境闸门 | 无 | `ModDevForceAsk` / `ModDevGate`(绕过"本对话都允许") | **只有 Linux 有** |
| 工具前后钩子 | `Hooks.RunBefore` / `RunAfter`,在 `Agent` 里跑;只有 `args` 改写与 `errors`,不能拦截 | `Hooks.RunBeforeEx` 支持返回 `blocked` **直接拦下调用**;接在 `ToolRunner.RunAsync` 里(权限裁决之前) | 同名但行为不同 |
| 确认卡超时 | `BackendApi.WaitCard` 200ms 轮询、**无超时**(任务书 D6) | 本次未核实页面侧(超出范围) | 未核实 |
| 检查点触发 | 只看 `ToolMeta.side_effect ∈ {write_local, delete}` | `Tools.IsWriteTool` + `UpgradeCheckpoint.SnapshotFor` + `ContentTools.SnapshotNewDirs`(**新建目录差集**)+ 执行后 `CaptureAfter` | **只有 Linux 有**(后两项) |

### 3.7 子代理

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 派发入口 | `Agent.RunSubagent`(静态,`settings.Copy()` + `ai_subagent=true` → 递归 `RunAgent`) | `AiAgent.RunSubagentAsync`(构造 `ToolRunner{PermissionMode="plan"}` + 新 `AiAgent`) | 同名但行为不同 |
| 轮数 | 硬编码 `ai_max_rounds = 4`(`:1742`) | 硬编码 `MaxRounds = 4`(`:473`) | 两边一致 |
| 会话 id | `<父id>-sub` | `<父id>-sub` | 两边一致(两边都有任务书 S 的缺陷) |
| 只读保证 | 两道:1 `SubagentAllows(name) = name != "dispatch_subagent" && ToolMeta.readonly` 在权限阶段与执行阶段各拦一次;2 **工具表层面过滤**:`SelectToolSchemas` 在 `ai_subagent` 时只留 readonly 并去掉 `dispatch_subagent` | 一道:`ToolRunner` 的 `plan` 档 → 非 readonly 一律 deny。**工具表不过滤**:子代理仍会拿到写工具 schema,只能在执行时被拒 | 同名但行为不同 |
| 子代理的兜底请求 | 兜底 `ChatOnce` 传的是**全量** `TOOLS_SCHEMAS`(任务书 D8),即子代理在兜底里能看到写工具 | 无兜底 | **只有 Windows 有**(缺陷) |
| 无确认通道 | 有:子代理 + 无 `confirmFn` → `deny("子代理无交互确认通道…")` | plan 档先拒;若真到确认,`Confirm == null` → `permission_denied` | 两边一致 |
| 进度可见性 | `onDelta`/`onStatus` 传 null;只发插件事件(子代理不发,`pturn=null`) | 事件回调 `_ => { }` 丢弃 | 两边一致(都不可见) |
| 返回值 | `{ok, answer, stop_reason}` | `{ok, answer, stop_reason}` | 两边一致 |
| **调度并行性** | `Scheduler` **没有**会话状态工具排除表:`dispatch_subagent`(readonly / side_effect=none)可进并行组 → 同轮多个子代理共用 `<父id>-sub` 会话 id,压缩状态与事件日志会串 | `SESSION_STATE_TOOLS = {update_plan, remember, forget, dispatch_subagent}` 明确排除在并行之外 → 串行 | **同名但行为不同**(任务书 S 第 6 条;Linux 无需改) |
| 只读工具并行条件 | `Meta.readonly && side_effect ∈ {none, read, network}`(**不看** `long_running`) | 同上 **且** `!meta.long_running` **且** 不在 `SESSION_STATE_TOOLS` | 同名但行为不同 |
| 并发上限 | 默认 4 | 默认 4 | 两边一致 |
| 异常隔离 | `RunGroups` 遇到 faulted 任务 **直接抛出**(会打断同组其它调用) | `RunGroupAsync` 把单调用异常收成工具错误载荷、其余照常;取消等组内收尾后抛 | **同名但行为不同**(Linux 有意收紧,文件头写明) |

### 3.8 MCP

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 外部 server 客户端 | `Mcp.ConnectServers` / `McpToolSchemas` / `CloseAll`;一轮一连一关;server 名白名单校验 | 同名同名方法;`Mcp.cs` 44% 行重叠,`McpClient.cs` 70% | 两边一致 |
| 工具命名 | `mcp_<server>_<tool>` | 同 | 两边一致 |
| 未声明 MCP 工具的裁决 | `meta == null && name.StartsWith("mcp_") && allow && rule_id 空 && 档位 ∉ {yolo, bypassPermissions}` → `ask("外部 MCP 工具能力未知,需确认")` | `McpVerdict()`:一律 ask,`plan` 拒,`yolo`/`bypassPermissions` 放行 | 同名但行为不同(近似等价) |
| MCP 工具视为写操作 | 未特别处理 | `readOnly = !mcp && …` → MCP 工具一律按写操作要确认 | 同名但行为不同 |
| 本机 MCP Server | 有:`McpServer`,127.0.0.1:25585/mcp,Streamable HTTP,隐藏 5 个会话工具,395 行 | 有:`McpServer` + `McpHttpServer`,同端口同协议,62% 行重叠,**671 行** | 两边一致(协议)/ 同名但行为不同(实现) |
| MCP `tools/call` 执行路径 | **直接执行** | 经 `ToolRunner.RunAsync` 走完整链:档位 + 自定义规则(含按参数匹配)+ 钩子 + `Preview` 改前改后 + 确认回调;`ForceConfirmWrites=true` | **同名但行为不同**(Linux 更严) |
| 子代理与 MCP | `ai_subagent` 时**不连** MCP(空表) | `IsSubagent` 时不新连,但复用父 `ToolRunner.McpClients` | 同名但行为不同 |

### 3.9 插件桥

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 插件运行时 | `Core/Plugins/` 22 文件:`PluginManager`(1903 行)、`NodeRuntime`(461)、`NodeHostProcess`(263)、`JsonRpcPeer`(432)、`DotNetPluginContext`(517)、`PluginTools`(454)、`PluginLlm`(411)、`PluginMarket`(884)、`SecretStore`(163)… | `Core/Plugins/` 6 文件:清单 / 市场 / 安装 / 状态;`PluginHost` 自述"插件文件可以安装/更新/卸载,但**不会有插件代码在本进程里跑**" | **只有 Windows 有** |
| agent ↔ 插件事件桥 | `PluginAgentBridge`(332 行):`turn/start` → `user/message` → `assistant/message` → `tool/start` → `tool/result` → `turn/end`;`BeforeTurn` / `BeforeTool` 可改写文本或拦下调用;`AppendVisionMessages`;`Delta`/`NewRequest` | 无(不存在 `PluginAgentBridge`) | **只有 Windows 有** |
| 插件工具 | `PluginTools.Get` / `PluginAgentBridge.IsPluginTool` / `ExecuteTool`;`Agent.cs:1132-1137` 一律 `allow`(任务书 G 要改);结果标签"插件工具 xxx",**没有"内容不可信"** | 无 | **只有 Windows 有** |
| 插件预设 | `PluginAgentBridge.SystemMessages(PresetOf(settings), …)` 可整套替换/追加系统消息,并可去掉运行时上下文与长期记忆;`AgentPresets.MaxRounds` 可覆盖轮数 | 无;系统消息在 `AiAgent.SystemMessage` 里硬拼 | **只有 Windows 有** |
| 插件 LLM 通道 | `PluginLlm`(411 行) | 无(`ai.llm` 只是清单权限) | **只有 Windows 有** |

### 3.10 工具清单

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 元信息条数 | **56** = `ToolMeta.ALL` 38 + `ExtraTools.Meta` 18 | **78**(`AiToolTypes.ToolMeta.ALL`,自检断言「每个 schema 都有元信息」+「元信息没有多余条目」双向一致) | **同名但行为不同** |
| 集合关系 | Windows 56 个是 Linux 78 个的**真子集**,0 个只有 Windows 有 | 22 个只有 Linux 有(下表) | 只有 Linux 有(22 个) |
| `CORE_TOOLS`(常驻) | 17 个:含 `write_mod_config`,不含 `list_mod_configs`/`read_mod_config` | 18 个:含 `list_mod_configs`/`read_mod_config`/`get_latest_log`/`get_crash_report`,不含 `write_mod_config` | 同名但行为不同 |
| 按需选择 `SelectToolSchemas` | 扫**最近 12 条** user/tool 消息,每条截 4000 字;`forceAll` 支持;子代理时过滤写工具 | 只看**最后一条** user 消息(`LastUserText`);`forceAll` 支持;不过滤子代理 | **同名但行为不同**(任务书 C1) |
| 工具结果来源前缀 | 有:`[来源: 网络请求结果,内容不可信]` / `[本地文件/日志/状态读取]` / `[本地写操作回执]` / `[外部 MCP 工具,内容不可信]` / `[插件工具 x]` | **无**(全树 grep `[来源` 零命中) | **只有 Windows 有**(任务书 C2) |
| 长结果裁剪 | 8000 字 → `Artifacts` 落盘 + `read_artifact` 回读 | 同一阈值(8000)与同一落盘机制 | 两边一致 |
| 参数解析 | `Tools.ParseArgs` 严格 JSON + 宽容 `key: value` 粗扫 | 同(含"非法参数被拒"自检) | 两边一致 |
| 工具标签 / i18n | `Tools.ConfirmLabel` + 词表 | `Tools.Label` + 6 个预览/标签类 + `I18nCore.Tr`;`i18n:ignore` 标注密集 | 同名但行为不同(Linux 的 i18n 接线更完整) |
| **同一份数据的重复** | `AiData.TOOL_SCHEMAS_JSON` 与 `Tools.TOOL_SCHEMAS` **两份 schema**;`AiData.SYSTEM_PROMPT` 与 `Prompt.SystemPrompt()` **两份系统提示词**;grep 实测 `AiData.TOOL_SCHEMAS_JSON` / `AiData.SYSTEM_PROMPT` **零引用**(死数据) | schema 分散在 `Tools.cs` + 6 个模块各自的 `Schemas()`;系统提示词一份(`AiData.SYSTEM_PROMPT`) | 只有 Windows 有(重复) |
| 死代码 | `Agent.WantsAction` + `_ACTION_RE/_QUESTION_RE/_DELEGATE_RE/_OBSERVE_RE/_DIAGNOSE_RE`:**全树无调用者**(只有定义),`ClaimsAction` 有 1 处调用 | 无这套正则(`AiData` 只有 `ACTION_RE/QUESTION_RE/CLAIM_RE`) | **只有 Windows 有** |

**只有 Linux 有的 22 个工具**:

```
升级/预检:      plan_instance_upgrade, apply_instance_upgrade, preflight_check
服务器/光影:     list_servers, add_server, remove_server, ping_server, recommend_shaders
诊断报告:        export_diagnostic_report
整合包制作:      check_modpack_distribution, plan_modpack, export_modpack, install_mods_batch
数据包/资源包:   create_datapack, create_resourcepack, write_kubejs_script, validate_content
模组开发测试:    create_test_instance, install_local_mod, revert_local_mod, run_test_launch, get_task_status
```

**同名但元信息不同的**(抽样):

| 工具 | Windows | Linux |
|---|---|---|
| `multiplayer` | `ro=false, write_local, medium` | `ro=false, **network**, medium`(注释:会拉起联机内核并连公共节点) |
| `get_task_status` | 无此工具 | `ro=true, read, low, **long_running=true**`(不参与并行) |
| `fs_write` | `write_local, high` | 同,且 `AllowsAlways` 明确不给"本对话都允许" |

### 3.11 测试覆盖

| 子项 | Windows | Linux | 判定 |
|---|---|---|---|
| 测试工程文件 | `windos/tests/` 共 69 个 `.cs`,AI 相关 **13** 个:`AiChatOps` `AiCompact` `AiFileTools` `AiFixChatStore` `AiHistory` `AiMode` `ExtraTools` `FileMemory` `McpServer` `MemoryEdit` `PluginMarket` `PluginUiRowsDedup` `ToolRegistry` | `tests/` 共 144 个 `.cs`,其中 `Ai*` **37** 个(15 App + 22 Core) | **只有 Linux 有**(规模差 3 倍) |
| 自检入口 | `--ai-ui-selftest`(`App.xaml.cs:159`) | `--ai-selftest`(`AiSelfTestCli.cs`,无头 CLI) | **同名但行为不同**(任务书 §6 的 🕓 项:Windows 的参数名是 `--ai-ui-selftest`,**不是** `--ai-selftest`) |
| 自检性质 | WPF 页面级:`Pages/Ai/AiSelfTest.cs` 419 行,断言 UI 子元素数量、渲染 markdown 表格、截 PNG;**需要交互桌面** | 无头:C# 逻辑级,354 + 68 + 55 行,110 项;使用假门面 `ToolRunner.IBackend` / `RecordingBackend` | **同名但行为不同** |
| 自检内容 | UI 布局/交互(`aiPage.SelfTestMessages.Children.Count`、`Scheduler` 分组、`Scheduler` 并行) | 工具表一致性、参数解析、风险文案、权限理由、压缩、轮次、MCP 路由等 | **同名但行为不同** |
| CI | `ci-windows.yml`,**不跑**自检 | `ci.yml`,**不跑**自检 | 两边一致(都缺) |
| 录制/回放式行为测试 | 无 | 无(全仓 `replay|golden|eval` 零命中) | 两边一致(都缺) |

---

## 4. 任务书点名要求的五项核实

### 4.1 压缩状态:静态字典(满 16 清空)vs 每实例 —— **证实**

- Windows:`private static readonly Dictionary<string, CompactState> _COMPACT_STATES`(`Agent.cs:17`);`_compact_state_for`(`:418-433`)在 `Count >= 16` 时 `_COMPACT_STATES.Clear()`。同一会话正在使用时被清 = 丢掉 `CheckRapidRefill` 保护与失败计数(任务书 D9 的现象)。
- Linux:`public CompactState CompactState { get; } = new()`(`AiAgent.cs:97`),随实例走,没有缓存上限问题。
- 判定:**同名但行为不同**;修 D9 时应把 Windows 改成 LRU(容量仍 16),而不是把 Linux 改成字典。

### 4.2 子代理调度:Windows 并行 vs Linux 串行 —— **证实**

- Windows `Scheduler.IsExclusive` 只认 `ask_user` / `side_effect ∈ {delete, launch}`;`IsParallelizable` = `Meta.readonly && side_effect ∈ {none, read, network}`。`dispatch_subagent` 的元信息是 `ro: true, "none"` → **可并行**;`update_plan` / `remember` / `forget` 同样是 `ro: true, "none"` → 也可并行(且与"先记再忘"的语义冲突)。
- Linux `Scheduler` 有 `SESSION_STATE_TOOLS = {update_plan, remember, forget, dispatch_subagent}`,并在 `IsParallelizable` 里排除;另有 `long_running` 不并行的收紧。
- 判定:**同名但行为不同**。任务书 S 第 6 条对 Windows 的处置方向(排除三个会话状态工具、保留 `dispatch_subagent` 在 id 唯一后并行)与 Linux 现状**不完全一致**:Linux 连 `dispatch_subagent` 也串行。这条差异要在 R 的清单里留档,不要顺手对齐(会改变 Linux 的并发行为)。

### 4.3 Linux 有没有计划审批 —— **证实:没有**

- 全树 grep `plan_approval` / `planApproved` / `plan_items` **零命中**;`AiAgent.cs:56` 那条只是 `AiAgentEvent.Plan` 的字段注释。
- Linux 的 `update_plan` 只做:解析 items → `PlanChanged?.Invoke` → 回 `{ok, done, total}`(`ToolRunner.cs:537-553`),页面把它画成计划卡,**没有任何批准/驳回语义**。
- `plan` 档在 Linux 只承担"只读硬停"(`DecideRaw` 首段),与"计划要不要批准"无关。
- 判定:**只有 Windows 有**。任务书 P2 的处置正确:本包不在 Linux 新增,记进本清单即可。

### 4.4 插件运行时(Linux 没有)—— **证实**

- `PluginHost.cs` 文件头自述:「插件文件可以安装/更新/卸载,但**不会有插件代码在本进程里跑**」;`src/TipCL.Core/Plugins/` 只有清单/市场/安装/状态/market/state 6 个文件,**没有** PluginAgentBridge、PluginTools、PluginLlm、NodeRuntime、JsonRpcPeer。
- 判定:**只有 Windows 有**。含四项子差异:插件预设、插件工具(含 G 包要补的权限与不可信标签)、插件事件总线、插件 LLM 通道。这是**新子系统**,不在合并范围内。

### 4.5 工具数 56 vs 78 —— **证实,并补两条**

- 实测:Linux `ToolMeta.ALL` = **78**;Windows = `ToolMeta.ALL` 38 + `ExtraTools.Meta` 18 = **56**;差集为**单向**:22 个只有 Linux 有,**0 个只有 Windows 有**。
- 补充一:Windows 侧同一批 schema 存了**两份**(`AiData.TOOL_SCHEMAS_JSON` 与 `Tools.TOOL_SCHEMAS`),系统提示词也存了**两份**(`AiData.SYSTEM_PROMPT` 与 `Prompt.SystemPrompt`),且 `AiData` 那两份实测**零引用**。做数据单源时顺手删死数据,是零风险收益。
- 补充二:**`Agent.WantsAction` 全树零调用者**(配套的 5 条正则全为死代码)。任务书 I 包说"grep 出所有调用点" —— 实测结果是 0,则 I 包的实际工作量为"删死代码 + `ClaimsAction` 单点改造"。

---

## 5. 本书修完后,两边仍会剩下的结构性差异

即使 A/B/C/D/S/P/G/I 全部做完,以下差异**不会**消失(合并不在本书范围):

1. **插件运行时**:Windows 独有(含 agent↔插件事件桥、插件工具、插件预设、插件 LLM)。Linux 侧的 `ai.tools`/`ai.presets`/`ai.llm` 仍是空清单。
2. **主循环状态机**:Windows 的 `TurnState`/`TurnPhase` 九相状态机 + `IllegalTransition` 观测,Linux 无。
3. **计划审批**:Windows 有(P2 会把时序改到执行前),Linux 无。
4. **后台任务回报 `pending_task` / steer 插话 / 每轮进度注入**:Windows 独有。
5. **工具面**:Linux 仍比 Windows 多 22 个工具(升级/预检/服务器/光影推荐/诊断报告/整合包制作/数据包资源包/模组开发测试);Windows 不会因为它们而做移植。
6. **自检形态**:Windows 是 WPF 页面级 `--ai-ui-selftest`(需桌面),Linux 是无头 `--ai-selftest`(110 项)。E 包接 CI 时,Windows 那一步很可能只能做提示项。
7. **MCP Server 的执行路径**:Linux 走完整权限链,Windows 直接执行;`McpServer.cs` 规模 671 vs 395。
8. **权限规则存储与作用域**:`.json`(global + per_instance,按会话实例)vs `config.json`(单表,按目标实例)。这是**用户可见的兼容面**,合并时必须先定归一化规则与迁移策略。
9. **档位别名兼容**:Windows 认 `standard`/`full`/`custom` + `ai_confirm_writes`,Linux 不认。
10. **权限持久化范围**:Windows 的"以后都允许"写盘,Linux 是进程内 HashSet(B3 会补持久化,但写到哪里仍由 Linux 自己定)。

---

## 6. 合并方案草案(闸门:由用户拍板)

> 本节只是**草案**。本书不含任何合并实施。

### 6.1 起点:先做数据单源(五类 + 我建议补的第六类)

任务书要求的五类,逐类给出可操作口径:

| # | 数据 | 现状 | 单源做法 |
|---|---|---|---|
| 1 | **工具 schema** | Windows:两份重复(`AiData.TOOL_SCHEMAS_JSON` 死 + `Tools.cs` 内联活),另有 `ExtraTools.Schemas()` 追加 18 条 Linux 没有的;Linux:`Tools.cs` 内联 + 6 个模块各自的 `Schemas()`(AiFsTools / PerfSavesTools / AcctLayoutMpTools / ModpackTools / ContentTools / ModDevSchemas) | 一份 JSON/`.json` 资源为唯一真源(按平台裁剪条目),两侧用同一个 `PyJson.Loads` 入口解析。**先只做"读"的单源,不动运行时对象形状** |
| 2 | **`ToolMeta` 元信息** | 两侧结构完全相同 `(name, readonly, side_effect, risk, long_running)`;Windows 38+18,Linux 78 | 一份元信息表 + 平台过滤。注意 `multiplayer` 的 `side_effect` 两侧**不同**(write_local vs network),单源前必须先定哪一个是真值(建议取 Linux 的 `network`,因为它是 Linux 的真实行为) |
| 3 | **系统提示词** | Windows:`Prompt.SystemPrompt`(活,`Agent.cs:348/365` 用)+ `AiData.SYSTEM_PROMPT`(死);Linux:`AiData.SYSTEM_PROMPT`(活,含 6 个 Linux 追加段)。`__LANG_RULE__` 占位机制两侧相同 | 一个"基础段"(逐字相同的前半)+ 平台"追加段"列表;**不要**把 Linux 的追加段强行灌给 Windows(那是新增提示词成本 ≈ +2–3k token 的系统提示) |
| 4 | **权限规则匹配** | 语义已逐条对位:`RULE_CONTENT_KEYS` 10 键顺序一致、`NormPathSpelling` 归一一致、deny>allow>ask 一致、"删除类不给无限定 allow"一致 | 纯函数级单源(`Matches` / `Pick` / `RuleContentFromInput`),**不含存储层**:`ai_permissions.json`(global + per_instance)与 `config.json`(`ai_permission_rules` 单表)先各自保留,只共享匹配算法;作用范围口径(会话实例 vs 目标实例)作为**显式参数**传入 |
| 5 | **token 估算** | `Tokens.cs` 四个公开方法一一对应;`Usage` / `TokenState` / `ModelCaps`(100% 相同) | 直接单源,零风险 |
| 6 | **(建议补)网络与错误分类** | `Client.cs` 99%、`AIClientError` 97%、`HttpCancel` 98%、`AiResponse` 99%、`Images`/`VisionCaps`/`TomlLite`/`ModelCaps` 100% | 这已是事实共享代码;先把它搬进共享工程,同时**正好是 AI5-T 假模型注入点的落点**。收益立竿见影、风险最低 |

### 6.2 对照两条思路

**a1 §7.1 的思路(低风险第一步:只做数据单源,行为逻辑各留一份)** 正确,但要补一条:**"数据单源"里最容易先落地、收益最大的其实是网络层**(Client/错误分类),而不是 schema —— 因为 Client 已经 99% 相同,而 schema 的单源要同时处理"平台裁剪"和"六个模块各自追加"的加载顺序。

**a2 WP-AG1 §4 的思路(以 Linux 结构为准、下沉共享 Core、Windows 的 WPF 层只接事件与确认回调)** 方向对,但有三个前提没被说清:

1. **它会改变 Windows 的用户可见行为**。Linux 缺的:非流式兜底、反应式压缩、续写、计划审批、后台任务回报、steer、进度注入、`TurnPhase` 观测。以 Linux 结构为准 = 这些功能要么在共享 Core 里补一遍(工作量 ≥ 把 A/D 各包重做一遍),要么**明说砍掉**(产品决策,不是工程决策)。
2. **它必须先解决"插件桥怎么办"**。Windows 的 `RunAgent` 外层就是插件事件总线;Linux 没有插件运行时。共享 Core 只留"无插件"空实现,等价于**放弃 Windows 插件能力**;要保留,就得把插件桥做成 Core 里的可选扩展点(接口 + 事件表),这本身是一个新子系统。
3. **它会强制统一命名与 `Py*` 兼容层**。Windows 侧是反编译产物(`flag2`–`flag10`、`list3`–`list14`、`text3`–`text18`),Linux 侧是正常命名。共享一份源码必然带来一次**跨 1770 行的大 diff**,而这块恰好是"没有端到端测试就不敢动"的区域 —— 与 T 包的存在理由直接冲突。

**我的建议(方案 C:先共享"无状态、无 UI、已被两棵树验证过"的模块,主循环最后合)**:

- **第 1 步(零行为变化)**:把 §6.1 的 1–6 类搬进共享工程,两侧都只做"引用替换",不改行为。验收:两棵树各自 build 通过、`dotnet test` 通过、`--ai-selftest`(110 项)通过、Windows compact 体积不涨、场景套件(若 T 已就绪)零差异。
- **第 2 步(仍不动主循环)**:把 Windows 侧的重复与死数据收掉(`AiData.TOOL_SCHEMAS_JSON` / `AiData.SYSTEM_PROMPT` / `WantsAction` 及其 5 条正则),把两侧差异**显式化**为一份"能力开关表"(有哪些工具、有没有插件桥、有没有计划审批),让差异从"代码分叉"变成"配置分叉"。
- **第 3 步(等拍板 + 等 T 就绪)**:在 AI5-T 的场景套件能在两棵树跑出**同一张断言表**之后,再决定主循环是"Linux 下沉 + Windows 接事件"还是"各留一份 + 共享数据"。**没有这一步的安全网,前两步之外的任何合并都是拿用户可见行为做赌注。**

### 6.3 net48 兼容性风险(动手前必须逐条验证)

| 风险 | 具体点 | 影响面 | 处置 |
|---|---|---|---|
| 语言特性 | Linux 侧用了 file-scoped namespace、`record struct PermissionVerdict`、`is not ("a" or "b")` 模式、目标类型 `new()`、`??=`、`switch` 表达式、`StringComparer.Ordinal` 的 `HashSet<string>` | 共享源码要在 net48 下编译 | net48 支持 C# 12 语法(需 `LangVersion latest`);`record struct` 需要 `System.Runtime.CompilerServices.IsExternalInit` shim —— Windows 侧已存在 `System.Runtime.CompilerServices/` 目录,机制已有,但**要确认它同时覆盖 `IsExternalInit` 与 `RequiredMember`** |
| BCL 差异 | `System.Text.Json`(两侧 `McpServer.cs` 都在用)、`System.Range/Index`、`Span<T>` 的部分重载、`HashCode`、`DateOnly`、`MathF`、`Task.WaitAsync` | net48 下缺 BCL 成员 | 红线**禁止新增 NuGet**。若 Windows 已在用 `System.Text.Json`,先核实它是引用还是内嵌 —— **核实不清就不共享 `McpServer`** |
| `HttpClient` / SSE | `Client.cs` 99% 相同,两侧都在用 `HttpClient` + `Stream` 行读取;`AiSession`/`HttpCancel` 近似相同 | 共享前要确认 net48 下 `HttpClient` 的 dispose/连接池行为一致(Linux 侧注释里就有"以前每次调用漏一个 HttpClient"的修复记录) | 先共享再观察一次真实请求;失败就回退 |
| 异步与取消 | Linux 用 `CancellationTokenSource.CreateLinkedTokenSource` + `Task.Delay(ct)`;Windows 用 `Thread.Sleep` + 前后 `_check()` | 只在共享 `Client` 层影响 | 两边可用;但共享后要避免把 `Thread.Sleep` 带进 Linux 的 UI 线程路径 |
| 多目标工程形态 | 三个候选:(a) 多目标 `<TargetFrameworks>net48;net8.0</TargetFrameworks>`;(b) 共享工程 `.shproj`(同源两编);(c) Windows 侧 `Compile Include` 链接 Linux 的 `.cs` | 直接决定单 exe/Costura 是否受影响 | 倾向 (b) 或 (c):不动 Windows 的 TFM,不引入新包;代价是共享源码**必须两边都能编译**(即不能出现 net8-only API) |
| 命名与 `Py*` 兼容层 | Windows 是反编译命名;两侧都依赖 `Py`/`PyDict`/`PyJson` 兼容层 | 共享会强制统一命名 | 只共享"已经同名同形"的类型;`Py*` 层先原样保留(它是事实上的公共层) |
| 线程模型 | Windows `RunAgentCore` 同步阻塞 + `Task.Run`;Linux 全 async | 决定共享 Core 的 API 形状 | 共享模块**不要**含调度逻辑(所以 `Scheduler` 不该第一批共享) |

### 6.4 体积影响

- **红线**:compact ≤ **5,757,264 B**(D-19);最近一次记录是合规、余量 **224,080 B**。
- **共享本身不增加体积**:同一份源码被两个 TFM 各编译一次,Windows 侧仍只产出一份 IL 进 Costura 单 exe。
- **真正会吃掉余量的是三样**:(1) 为 net48 写的新 shim/兼容分支;(2) 若用多目标工程且配置失误,把 net8 产物也嵌进 exe(**体积可能翻倍**,这是本项目最贵的单点风险);(3) 若被迫新增 NuGet(违反红线)。
- **建议的量化纪律**:每共享一个模块,**单独量一次** Windows compact exe 的字节数,单模块增量 > 50 KB 就停下来问;整轮合并的累计增量控制在 224,080 B 余量的一半以内。
- Linux 侧无体积红线(AppImage),但要避免把 `Client`/`Compact` 抽出去之后出现"两边各编译一份等价实现"的错觉 —— 单源的意义就在于不再有两份。

### 6.5 必须先拍板的问题(合并闸门)

| # | 问题 | 为什么必须先答 |
|---|---|---|
| 1 | 合,还是不合? | 不合也完全合理:两棵树各自能跑,合并的价值主要是"少做一遍修复"与"行为一致";合并的成本是一次跨 **26,595 行**(两棵树 Ai 目录合计,Linux 14,676 + Windows 11,919)的结构改造 |
| 2 | 若合,**以哪边为准**? | 以 Linux 结构为准 = 删/改 Windows 的 7 项用户可见能力(见 §5 第 1–4、6、7 条);以 Windows 结构为准 = Linux 要补插件运行时(新子系统);第三条路是"数据单源 + 主循环各留一份"(我的建议) |
| 3 | **Windows 是否继续做主线?** | 决定合并是"收敛"还是"移植":如果 Windows 要停,那只需止损(不再双改);如果继续,评估"两份修复的成本 < 一次共享改造" |
| 4 | Linux 插件运行时做不做? | 不做,则插件桥永远是 Windows 独有,共享 Core 必须留空实现;要做,它是新子系统(进程/沙箱/按项授权/密钥隔离/热重载) |
| 5 | **允许新增 NuGet 吗?** | 直接决定多目标工程可行性;净禁则只能走 `.shproj` / `Compile Include` 路线,并放弃共享依赖 `System.Text.Json` 的模块 |
| 6 | 主循环合流的先后:AI5-T 之前还是之后? | 我的判断:**之后**。在场景套件就绪前合主循环,等于放弃唯一的行为回归防线 |

---

## 附录 A:环境基线更正(可直接引用)

任务书 §1 把"本机 `dotnet test` 起不来"记为疑难点。AI5-0 已定位:**根因是用户级环境变量 `DOTNET_ROOT` 指向 `C:\Users\Administrator\.dotnet`(该目录没有运行时),而非 testhost 或路径编码**;真实运行时在 `C:\Program Files\dotnet`。

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'; dotnet test ...
```

与代码无关,两棵树同样受影响。当前基线(Linux 树):Release 构建 **0 error / 1 warning**(`src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649);`--ai-selftest` **通过 110 / 失败 0**。

## 附录 B:文件级相似度(位置对齐口径)

同名 38 个文件、Linux 侧 8932 非空行,整体 **2546 / 8932 = 29%** 位置对齐逐行相同。分组见 §2.2。

**只有 Linux 有的文件(29 个)**:`AiAgent.cs`、`ToolRunner.cs` + 9 个分部、`AiToolTypes.cs`、`AiFsTools.cs`、`AiMemory.cs`、`AiRisk.cs`、`AiSelfTest.cs`(+EngineMcp/ModDev)、`AiToolRefs.cs`、`AiUiText.cs`、`AiFixChat.cs`、`Checkpoint.NewChildren.cs`、`CompactTypes.cs`、`I18nUxLabels.cs`、`MultiplayerTiming.cs`、`PerfSavesTools.cs`、`PerfSavesPreview.cs`、`PermissionRules.cs`、`AcctLayoutMpTools.cs`。

**只有 Windows 有的文件(28 个)**:`Agent.cs`、`AgentResult.cs`、`AgentCancelled.cs`、`TurnState.cs`、`TurnPhase.cs`、`IllegalTransition.cs`、`ToolMeta.cs`、`ToolMetaInfo.cs`、`ToolCall.cs`、`ToolCallStatus.cs`、`ToolCancelled.cs`、`ToolErrorCode.cs`、`Permission.cs`、`PermissionResult.cs`、`Decision.cs`、`AiRule.cs`、`Prompt.cs`、`Memory.cs`、`ExtraTools.cs`、`CompactState.cs`、`CompactionResult.cs`、`MicroConfig.cs`、`MicroResult.cs`、`AutoConfig.cs`、`Hook.cs`、`Diagnose.cs`、`ModConfig.cs`、`ConfigEditError.cs`。

## 附录 C:证据索引(本文结论 → 符号)

| 结论 | 证据 |
|---|---|
| 压缩状态静态字典满 16 清空 | `windos/.../Ai/Agent.cs`:`_COMPACT_STATES`(L17)、`_compact_state_for`(L418-433) |
| 压缩状态每实例 | `src/.../Ai/AiAgent.cs`:`CompactState`(L97) |
| 子代理并行(Windows) | `windos/.../Ai/Scheduler.cs`:`IsExclusive`(L14)、`IsParallelizable`(L28);`ToolMeta.cs`:`dispatch_subagent`(L51) |
| 子代理串行(Linux) | `src/.../Ai/Scheduler.cs`:`SESSION_STATE_TOOLS`(L36-39)、`IsParallelizable`(L57-73) |
| Linux 无计划审批 | 全树 grep `plan_approval|planApproved` 零命中;`ToolRunner.cs:537-553` |
| Windows 计划审批在执行之后 | `windos/.../Ai/Agent.cs`:`Scheduler.RunGroups`(L1233)、审批(L1247-1266) |
| Linux 无插件运行时 | `src/TipCL.Core/Plugins/PluginHost.cs:4-5` |
| Windows 插件桥 | `windos/src/TipCL/Core/Plugins/PluginAgentBridge.cs`(332 行);`Agent.cs:491-532/1076/1132-1137/1240-1246` |
| 工具数 56 / 78 | `windos/.../Ai/ToolMeta.cs`(38)+ `ExtraTools.cs`(Meta 18);`src/.../Ai/AiToolTypes.cs:48-138` |
| Windows 死数据 | grep `AiData.TOOL_SCHEMAS_JSON`、`AiData.SYSTEM_PROMPT`、`WantsAction` 均只有定义、零调用 |
| Linux 无兜底/反应式压缩/降级/续写 | `src/.../Ai/Client.cs:55`(`IsContextOverflow` 零调用者)、`:503/539/641/673`(误导文案);`AiAgent.cs:638`(`FinishReason` 只记不判) |
| 自检入口差异 | `windos/src/TipCL/App.xaml.cs:159`(`--ai-ui-selftest`);`src/TipCL.App/Session/AiSelfTestCli.cs:23`(`--ai-selftest`) |
| Windows 兜底用全量工具 | `windos/.../Ai/Agent.cs:885` |
| Linux 工具选择只看最后一条 user | `src/.../Ai/Tools.cs:295-358`;Windows 对位 `windos/.../Ai/Tools.cs:107-123` |
| 权限规则作用范围差异 | `windos/.../Ai/Permission.cs`:`LoadRules`(L138-147)、`_matches`(L154-165);`src/.../Ai/PermissionRules.cs`:`Matches`(L52-69)、`TargetInstance`(L119-123) |
