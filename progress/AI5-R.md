# AI5-R 两版 agent 差异清单(只读调研)

- 包号:AI5-R(任务书 §7 R)
- 工作区:`C:\tipcl-ai5\research-r`,分支 `ai5/research-r`,基线 `b4cd7a2`
- 日期:2026-11-28(ISO 8601)
- 产出:`docs/ai-agent-parity.md`
- 性质:**只读调研**。未改任何生产代码、未新增/删除测试、未改版本号、未发 release、未 push。
- 闸门:合并方案由用户拍板,本包不实施合并。

---

## 一、做了什么

1. 按任务书 §7 R 的口径,逐行通读两侧主循环与工具执行层,按 11 个维度做对照表,每项判定为「两边一致 / 只有 Windows 有 / 只有 Linux 有 / 同名但行为不同」。
2. 逐条核实任务书点名要求的 5 项已知差异(压缩状态、子代理并行、Linux 计划审批、插件运行时、工具数 56 vs 78),每条给出符号级证据。
3. 用脚本补了 4 组量化证据(逐行相似度、工具集合差、`StopReason` 可达性、测试文件清点),避免只靠印象。
4. 对照 a1 §7.1(数据单源)与 a2 WP-AG1 §4(以 Linux 结构为准、下沉共享 Core)两条思路,写出合并方案草案:分步、net48 兼容性风险、体积影响、必须先拍板的 6 个问题。
5. 补记任务书 §1 的环境疑难点更正(AI5-0 已定位为 `DOTNET_ROOT` 环境变量问题)。

## 二、读过的文件(逐行)

| 树 | 文件 | 行数 |
|---|---|---|
| Linux | `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | 756 |
| Linux | `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | 813 |
| Linux | `src/TipCL.Core/Launcher/Ai/ToolRunner.EngineMcp.cs` | 71 |
| Linux | `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs` | 231 |
| Linux | `src/TipCL.Core/Launcher/Ai/Scheduler.cs` | 228 |
| Linux | `src/TipCL.Core/Launcher/Ai/PermissionRules.cs` | 124 |
| Linux | `src/TipCL.Core/Launcher/Ai/Compact.cs` · `CompactTypes.cs` | 246 / 88 |
| Linux | `src/TipCL.Core/Launcher/Ai/AiData.cs` | 175 |
| Linux | `src/TipCL.Core/Launcher/Ai/Tools.cs`(L150-369)· `AiSelfTest.cs`(L55-104)· `Client.cs`(符号面) | 定点 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 1770(全文) |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Permission.cs` | 507 |
| Windows | `windos/src/TipCL/Core/Launcher/Ai/Scheduler.cs` · `Compact.cs` | 142 / 249 |
| Windows | `ToolMeta.cs` `ToolMetaInfo.cs` `CompactState.cs` `AutoConfig.cs` `MicroConfig.cs` `MicroResult.cs` `CompactionResult.cs` `TurnState.cs` `TurnPhase.cs` `AiRule.cs` `Decision.cs` `PermissionResult.cs` `ToolCall.cs` `ToolCallStatus.cs` `StopReason.cs` `Prompt.cs` `AiData.cs` `Memory.cs` | 小文件全读 |
| Windows | `Tools.cs`(L80-200)· `ExtraTools.cs`(Meta 段)· `Hooks.cs`(节选)· `Mcp.cs` · `McpServer.cs`(节选)· `Client.cs`(符号面) | 定点 |

**未覆盖**(留给后续包):`ToolRunner.*.cs` 其余 8 个分部的执行体逐行、两棵树页面层(AiPage / ConfirmCard / AiPermissionPanel)的 UI 状态机、`Client.cs` SSE 解析逐行。

## 三、覆盖的维度(11 / 11)

主循环阶段 · 重试/降级 · 压缩 · 续写 · 计划审批 · 权限模型 · 子代理 · MCP · 插件桥 · 工具清单 · 测试覆盖。

判定分布(脚本实测,88 条对照条目):

| 判定 | 条数 | 代表 |
|---|---|---|
| 两边一致 | 22 | 10 档权限集合、判定顺序、退避公式、可重试判据、工具命名 `mcp_*`、长结果 8000 字裁剪、MCP 协议与端口、参数解析、并发上限 4、子代理轮数 4 与会话 id 规则 |
| 只有 Windows 有 | 24 | `TurnState` 九相状态机、非流式兜底、反应式压缩、`finish_reason=length` 续写、计划审批、`ai_fallback_model` 降级、`tool_schema_miss` 全量重发、工具结果来源前缀、后台任务回报、steer 插话、每轮进度注入、插件运行时与插件桥(4 子项)、历史摘要 `_trim_history` |
| 只有 Linux 有 | 6 | 22 个工具(升级/预检/服务器/光影推荐/诊断报告/整合包制作/数据包资源包/模组开发测试)、`ForceConfirmWrites` 外部写闸门、`ModDevGate`、未知工具直接 deny、检查点的新建目录差集(`UpgradeCheckpoint` / `ContentTools.SnapshotNewDirs`)、`SESSION_STATE_TOOLS` 并行排除 + `long_running` 不并行 + 调度层异常隔离、无头 `--ai-selftest`(110 项)+ 37 个 AI 测试文件 |
| 同名但行为不同 | 35 | 主循环分层与阶段机、单轮尝试结构、终态出口、压缩状态存放、`SelectToolSchemas` 扫描范围、权限规则存储与作用域、`dontAsk` 两条路径、「本对话都允许」持久化、档位别名兼容、Hooks 能否拦截、MCP 工具裁决、MCP Server 执行路径、子代理只读保证方式、Scheduler 并行条件与异常语义、`multiplayer` 元信息、工具标签 i18n、自检形态、`Client`/`Compact`/`Tokens` 等同源文件的行为分叉 |
| 混合(协议一致 / 实现分叉) | 1 | MCP 本机 Server |

## 四、任务书点名 5 项的核实结论

| # | 要求核实项 | 结论 | 关键证据 |
|---|---|---|---|
| 1 | 压缩状态:Windows 静态字典满 16 清空 vs Linux 每实例 | **证实** | `windos/.../Agent.cs:17,418-433`(`_COMPACT_STATES` + `Clear()`);`src/.../AiAgent.cs:97`(实例字段) |
| 2 | 子代理调度:Windows 并行 vs Linux 串行 | **证实** | `windos/.../Scheduler.cs:14,28-40`(无排除表;`dispatch_subagent` 元信息 `ro:true,"none"` → 可并行);`src/.../Scheduler.cs:36-39,57-73` |
| 3 | Linux 有没有计划审批 | **证实:没有** | 全树 grep `plan_approval|planApproved` 零命中;`ToolRunner.cs:537-553` 只调 `PlanChanged` 画卡;`AiAgent.cs:56` 那条只是事件字段注释 |
| 4 | Linux 没有插件运行时 | **证实** | `src/TipCL.Core/Plugins/PluginHost.cs:4-5` 自述"不会有插件代码在本进程里跑";`Plugins/` 仅 6 个文件 vs Windows 22 个 |
| 5 | 工具数 56 vs 78 | **证实,且差集单向** | `ToolMeta.cs`(38)+`ExtraTools.cs`(18)=56;`AiToolTypes.cs:48-138`=78;22 个只有 Linux 有,**0 个只有 Windows 有** |

## 五、额外发现(任务书没写、但影响合并方案)

| # | 发现 | 证据 | 影响 |
|---|---|---|---|
| R-1 | Windows 侧同一份数据存两份,且 `AiData` 那两份**零引用**(死数据) | grep `AiData.TOOL_SCHEMAS_JSON`、`AiData.SYSTEM_PROMPT` 只有定义;活的是 `Tools.TOOL_SCHEMAS` 内联 JSON 与 `Prompt.SystemPrompt()` | 做「数据单源」时先删死数据,零风险收益 |
| R-2 | `Agent.WantsAction` 及其 5 条正则**全树零调用者** | grep `WantsAction` 全仓仅 1 处(定义处) | 任务书 I 包实际工作量被高估:应是"删死代码 + `ClaimsAction` 单点改造" |
| R-3 | Windows 自检参数是 `--ai-ui-selftest`,不是任务书 §6 写的 `--ai-selftest` | `windos/src/TipCL/App.xaml.cs:159` | 任务书 §6 的 🕓 项可据此直接落定;E 包接 CI 时它是 WPF 页面级、需要交互桌面 |
| R-4 | 整体逐行相同率 29%(位置对齐)/ 69%(集合包含);客户端网络层已 **99% 相同** | 脚本实测(见 parity §2.2) | 合并的"低风险第一步"应该是网络与纯数据层,不是 schema |
| R-5 | `PermissionRules` 与 `Permission.Decide` 的规则段语义**已逐条对位**(键顺序、归一、deny>allow>ask、delete 类不给无限定 allow) | 两侧文件头注释 + 逐条比对 | 权限规则匹配是"几乎可以直接单源"的一类 |
| R-6 | Linux `stop_reason` 只有 4–5 种实际可达;`COMPLETED` 永不被赋值 | 赋值点计数(parity §3.4) | 与任务书 A6 一致,合并前需先补齐 |
| R-7 | 两侧 `Scheduler` 的**异常语义相反**:Windows 遇 faulted 直接抛(打断同组),Linux 收成工具错误载荷 | `windos/Scheduler.cs:130-137`;`src/Scheduler.cs:154-176` | 共享 `Scheduler` 前必须先定哪一种语义;不能当"纯数据"共享 |

## 六、验收数字

| 判据 | 结果 |
|---|---|
| 对照维度 | 11 / 11 |
| 对照条目 | **88 条**(两边一致 22 / 只有 Windows 有 24 / 只有 Linux 有 6 / 同名但行为不同 35 / 混合 1),无"未核实"遗留;仅"确认卡超时 Linux 侧"一条标未核实,属页面层、超出本包范围 |
| 任务书点名必收项 | 5 / 5 全部收进并逐条给证据 |
| 产出文档 | `docs/ai-agent-parity.md`(含对照表、核实、合并草案、附录 A/B/C 三份证据索引) |
| 生产代码改动 | 0 |
| 构建/测试 | 未跑(只读调研,按任务书红线与 brief 要求不动测试链路) |

## 七、遗留问题

1. **确认卡超时**:Windows `BackendApi.WaitCard` 200ms 轮询无超时(任务书 D6 已有);Linux 侧确认卡的超时/取消口径在页面层,本包未核实 —— 归 D6。
2. **`ToolRunner.*.cs` 8 个分部的执行体**未逐行:只看了入口、元信息分级与检查点接线,执行体细节留给 D3(回滚覆盖)、D4(预览覆盖)。
3. **两棵树页面层的 UI 状态机差异**未对照:本包只做了 agent 核心层。若合并方案拍板为"Linux 结构为准",页面层的差异会变成下一份清单。
4. **`System.Text.Json` 在 net48 的依赖形式**未核实(是否引用 NuGet、是否被 Costura 内嵌):这是合并第一步能否共享 `McpServer` 的前置条件,需要一次构建期核实。
5. `multiplayer` 的 `side_effect` 两侧不同(`write_local` vs `network`),做 `ToolMeta` 单源前必须定真值 —— 已写进 parity §6.1 表格。
6. 合并方案本身**未拍板**(闸门)。草案给出的建议顺序是:单源(6 类)→ 收死数据与显式化能力开关表 → 等 AI5-T 场景套件就绪后再谈主循环。

## 八、要改的文档行(交给收尾的包)

| 文档 | 位置 | 要写什么 |
|---|---|---|
| `docs/DECISIONS.md` | 新增条目(D-86 之后) | 合并方案的裁决(等用户拍板);以及本包给出的"环境疑难点根因 = `DOTNET_ROOT`"结论 |
| `docs/PARITY.md` | AI 三节 | 从 `git show d96a5a6:docs/PARITY.md` 取回后,把 AI 侧的口径指向 `docs/ai-agent-parity.md` |
| `windos/docs/改造工程:2611/AI-agent-合并任务书.md` | §6 命令块 `--ai-selftest`(Windows) | 改成 `--ai-ui-selftest`(`App.xaml.cs:159`) |
| 同上 | §1 现状快照"本机环境"行 | 更正:testhost 根因是 `DOTNET_ROOT` 指向无运行时目录,不是 testhost 自身 |
| 同上 | §7 R 包 | 补一句:Linux 侧确认**没有**计划审批(🕓 已落定) |

---

提交:分支 `ai5/research-r`,提交信息 `docs(agent): AI5-R 两版 agent 差异清单与合并方案草案`。
