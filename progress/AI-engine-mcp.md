# AI-engine-mcp · AI 引擎底层能力 + Linux 版 MCP Server

分支 `ai/engine-mcp`(基于 `main@4277a02`),worktree `D:\项目\TipCL\wt-ai-engine-mcp`。提交前缀 `AI-engine-mcp:`。
本文件是本包唯一的进度记录;`docs/dev/PROGRESS.md` / `docs/DECISIONS.md` / `README.md` 未改(按公共任务书),需要改的行列在文末。

## 1. 做了什么

### 1.1 只读工具并行执行(照 `windos/.../Ai/Scheduler.cs`)
- 新增 `Scheduler`:`IsExclusive` / `IsParallelizable` / `Group(maxConcurrency=4)` / `RunGroupAsync` / `RunGroupsAsync`(参考版 `RunGroups` 的 async 形状)。
- 分组规则照搬:`ask_user` 与 side_effect = `delete` / `launch` 独占;`readonly` 且 side_effect ∈ {none, read, network} 可并行;其余(写本机、未知名、`mcp_*`)单独成组串行。**每次现读 `ToolMeta`**,别的包新增工具不用改调度器。
- Linux 收紧两处(写在 `Scheduler.cs` 文件头):`long_running` 不并行;会话内状态工具 `update_plan / remember / forget / dispatch_subagent` 不并行(先后顺序有语义)。
- 接进 `AiAgent` 工具循环:每组先按原顺序发全部 `tool` 事件 → 组内并行执行 → 再按原顺序发 `tool_done` 并追加 `tool` 消息。事件与对话回填都在会话线程上做,**不乱序、不丢**;`MaxToolConcurrency`(默认 4,设 1 即退回完全串行)。
- 异常隔离:组内某个调用抛出的非取消异常只把它自己变成一条工具错误(`ok:false`),兄弟调用照常;「停止」(取消)等组内收尾后原样抛出。参考版是「等完再抛第一个异常」,Linux 改成隔离(理由见文件头)。
- 顺手修掉一个旧缺陷:参数 JSON 解析失败的调用以前会**拿空参数照跑**一遍(如 `launch_game` 会用默认实例去启动);现在照参考版 `Agent._execute` 的 parseErrors 分支直接回 `bad_arguments`,不执行。

### 1.2 工具前后钩子(照 `windos/.../Ai/Hooks.cs` + `Hook.cs`)
- 新增 `Hook` / `Hooks`:`Register / Unregister / Clear / SetEnabled / RunBefore(toolName,args) / RunAfter(toolName,result)`、`HOOKS_DIR`、内置 `write_audit` 审计钩子(写/删/启动类调用记到 `数据目录/cache/ai_hooks/audit.jsonl`,与参考版同处同形)。
- 接到 `ToolRunner.RunAsync`:**权限裁决之前**跑 RunBefore(用户在确认卡上看到的是改写后的参数),执行完、结果裁剪落盘之前跑 RunAfter。会话循环与本机 MCP Server 共用这一个入口。
- 钩子报错不让调用崩:错误收进 `errors`(「钩子名: 异常」),记 `hook_error` / `hook_errors` 轨迹,并经新回调 `ToolRunner.HookErrors(call, phase, errors)` 回传宿主。
- Linux 补一个拦截口:before 返回 `HookBlock(reason)` ⇒ 调用标 denied、理由回给模型(参考版的拦截在 `PluginAgentBridge.BeforeTool`,Linux 没有那座桥)。
- **插件**:Linux `src/TipCL.Core/Plugins` 只有文件级管理(D-25),**没有** `PluginHooks / LaunchHooks` 这类钩子机制,插件代码也不会在本进程里跑 —— 所以没有「把 AI 钩子挂到插件钩子上」的现成机制可挂,这里不伪造;将来有宿主时把插件回调包成 `Hook` 注册即可(写在 `Hooks.cs` 文件头)。

### 1.3 权限规则按参数匹配(补 D-24 的 content 判定,照 `windos/.../Ai/Permission.cs`)
- 新增 `PermissionRules`:`RULE_CONTENT_KEYS`(command, url, file_path, path, pattern, filename, slug, name, version, major,顺序即优先级)/ `RuleContentFromInput` / `NormContent`(=`Utils.NormPathSpelling`)/ `Matches` / `Pick`。
- 语义与参考版逐条对位:限定参数留空 = 整个工具;非空时取调用参数里按键序第一个非空字符串,两边归一后整串相等才命中;命中多条时 **禁止 > 允许 > 每次问**;删除类(side_effect = delete)上**无限定参数的 allow 不生效**。
- Linux 的「作用实例」(`instance` 键)现在真的参与判定,按**这次调用要动的实例**(参数里的 instance,缺省为会话实例)算 —— 「只允许对某实例写」不会被模型换个实例名绕过。参考版条目形状(`toolName` / `ruleContent`)一并认。
- `ToolRunner.Decide(tool, args)` 新重载;`RunAsync` 走带参数的入口;旧 `Decide(tool)` 保留(不带参数时只有「整个工具」规则命中)。
- `AiPermissionPanel`:新增一行说明(`ContentHint`,照实际规则写);同一(工具, 作用范围, 限定参数)只留一条,删除按三元组删;删除类加「无限定 allow 不会生效」的即时提示;规则小节副标题改成「命中多条时 禁止 > 允许 > 每次问」。

### 1.4 Linux 版 MCP Server(照 `windos/.../Ai/McpServer.cs`)
- 新增 `McpHttpServer`(可独立起停的实例,测试用随机端口)+ 静态门面 `McpServer`(名字与参考版一致)。
- 照搬:地址 `http://127.0.0.1:25585/mcp`、`DefaultPort = 25585`、`ProtocolVersion = "2025-06-18"`(initialize 回显客户端版本)、`serverInfo.name = tipcl`、instructions 原句;JSON-RPC 方法 `initialize / ping / tools/list / tools/call / notifications/*`、批量、`-32700/-32600/-32601/-32603`;HTTP 口径(只 `/mcp`;GET 405、DELETE 200、POST 读 Content-Length ≤ 4MB、通知回 202);安全口径(**只听 127.0.0.1**、只接受环回来源、带非本机 `Origin` 一律 403);隐藏表 `ask_user / dispatch_subagent / update_plan / remember / forget`;tools/call 返回 `{content:[{type:text,text}], isError}`、`LooksLikeError` 判据;`LastTool / LastToolAt / ToolCalled / StateChanged`。
- 参考版**没有 token 鉴权**(环回 + Origin 校验就是它的口径),Linux 同样不加,两边客户端配置通用。
- 偏好:`数据目录/mcp_server.json` 的 `{"enabled","port"}`,默认关 / 25585 —— 与参考版同一个文件、同名键、同默认值(参考版这两个键不在 config.json 里,Linux 也不放进 config.json)。
- **与参考版不同(守红线 4)**:tools/call 不直通后端,而是让宿主 `RunnerFactory` 造一个 `ToolRunner` 走 `RunAsync` —— 档位、自定义规则(含按参数匹配)、钩子、Preview、确认回调一个不少;服务端强制 `ForceConfirmWrites = true`:**外部发起的写操作一律弹确认**(哪怕档位是「全自动」),确认卡的「以后都允许」不生效;拒绝类裁决(只看不动 / 不询问 / 规则禁止)照旧直接拒。宿主没接确认回调时得到如实的 `permission_denied`。参考版是直接执行、只在 instructions 里请对方「先和用户确认」。这正是 D-28 当初判「不移植」的卡点,现在有了确认链就能真做。
- 实现零依赖:`TcpListener` + 手写最小 HTTP/1.1(同参考版做法;没用 `HttpListener`,它在 Windows 上需要 urlacl/管理员,本仓测试在 Windows 低完整性沙箱里跑)。修了参考版的一处:**先读完请求体再做拒绝**,否则 Windows 会回 RST,外站 Origin 的 403 实际传不到客户端(集成测试里实测踩到)。
- 界面:设置 → AI 助手 → 「MCP Server」一行(开关 + 端口 + 「应用端口」+ 状态「未开启 / 启动失败：… / 地址」+「复制地址」)。`McpServerHost`(App)注入执行器工厂(`InProcessApi` 门面 + 默认实例 + 主窗口 `Dlg.Ask` 确认框),App 启动时 `StartIfEnabled`(`--smoke` 不开端口),退出时关闭。

### 1.5 自检
`--ai-selftest` 新增 8 项(`AiSelfTestEngineMcp`):调度分组、结果按序回填、钩子改写参数、钩子报错只进 errors、规则限定参数命中、删除类无限定 allow 不生效、MCP initialize 协议版本、MCP tools/list 动态读表。自检里写调用会过审计钩子,`HOOKS_DIR` 临时指到 temp、检完复原(不弄脏用户数据目录)。

## 2. 文件清单

新增:
- `src/TipCL.Core/Launcher/Ai/Scheduler.cs`
- `src/TipCL.Core/Launcher/Ai/Hooks.cs`(`Hook` / `HookBlock` / `Hooks`)
- `src/TipCL.Core/Launcher/Ai/PermissionRules.cs`
- `src/TipCL.Core/Launcher/Ai/ToolRunner.EngineMcp.cs`(partial:钩子接线、`ForceConfirmWrites`、`HookErrors`)
- `src/TipCL.Core/Launcher/Ai/McpServer.cs`(`McpHttpServer` + `McpServer`)
- `src/TipCL.Core/Launcher/Ai/AiSelfTestEngineMcp.cs`
- `src/TipCL.App/Common/Ai/McpServerHost.cs`
- `src/TipCL.App/Pages/Settings/SettingsPage.Mcp.cs`
- `tests/TipCL.Tests/Core/AiSchedulerTests.cs`(含 `AiAgentParallelToolsTests` + 本机假网关 `SequencedSseServer`)
- `tests/TipCL.Tests/Core/AiHooksTests.cs`
- `tests/TipCL.Tests/Core/AiPermissionContentTests.cs`
- `tests/TipCL.Tests/Core/McpServerTests.cs`
- `tests/TipCL.Tests/App/AiEngineMcpAppTests.cs`
- `progress/AI-engine-mcp.md`(本文件)

修改(都是局部追加/替换):
- `src/TipCL.Core/Launcher/Ai/ToolRunner.cs`:类声明加 `partial`;`Decide(tool, args)` 重载;`DecideRaw` / `RuleFor` 带参数并改走 `PermissionRules.Pick`;`RunAsync` 里 3 处(执行前钩子、`ExternalWriteGate` + 带参裁决、执行后钩子)。**分发 switch 未动**。
- `src/TipCL.Core/Launcher/Ai/AiAgent.cs`:工具执行 foreach 换成按 `Scheduler` 分组执行;新增 `MaxToolConcurrency`、`ExecuteOneAsync`。
- `src/TipCL.Core/Launcher/Ai/AiSelfTest.cs`:开头 2 行(HOOKS_DIR 指临时)、`VerifyServices` 后 3 行(调本包自检 + 复原)。
- `src/TipCL.App/Pages/Ai/AiPermissionPanel.cs`:说明文案与规则去重/删除按(工具, 范围, 限定参数)。
- `src/TipCL.App/Pages/Settings/SettingsPage.cs`:AI 卡加 1 行 `MCP Server` 字段。
- `src/TipCL.App/App.axaml.cs`:启动 `McpServerHost.Init()`、退出 `Shutdown()`(+5 行)。
- `src/TipCL.Core/Resources/locales/zh_CN.json` / `en.json`:各 +8 键,插在 `"MCP Server 地址：{0}"` 那一行之后(不在文件尾,减少与别的包的尾部冲突);两表键集仍相等(3069)。

## 3. 新工具清单
本包**不新增 AI 工具**(Tools.cs schema / ToolMeta.ALL / 分发 switch 均未改)。MCP Server 对外暴露的就是现有 schema 表里除隐藏表外的全部工具,**动态读取** —— 别的包新增的工具会自动出现在 `tools/list` 里(前提是它们同时进了 `Tools.Schemas` 与 `ToolMeta.ALL`)。

## 4. 新增测试
新增 **39** 个 xunit 用例 + `--ai-selftest` 新增 8 项:
- `AiSchedulerTests` 10 + `AiAgentParallelToolsTests` 2(端到端:假网关第一轮回 4 只读 + 1 写 + 1 坏参数,断言并发峰值 ≥ 2、事件流逐个对位、tool 消息按原顺序、写被拒、坏参数不执行;`MaxToolConcurrency=1` 退回串行)
- `AiHooksTests` 8
- `AiPermissionContentTests` 9
- `McpServerTests` 8(含集成:`Start(0)` 随机空闲端口 → HTTP initialize / 通知 202 / tools/list / 调只读 `list_instances` / 外部写被拒 & 确认后才执行 & 「以后都允许」不生效 / 外站 Origin 403 / 本机 Origin 200 / GET 405 / 路径 404 → `Stop` 后连不上;偏好文件形状与门面起停)
- `AiEngineMcpAppTests` 2
(数字以 `dotnet test` 实测为准:本包 6 个类单独跑 39/39 通过;全量 1015 = 基线 976 + 39。)

### 实测(2026-11-20,本机 Windows,SDK 8.0.428)
- Debug 构建(`--no-incremental`):0 错误,3 个警告,全部是基线就有的 `tests/.../Layout/LayoutMotionAndHeightTests.cs` xUnit1013×2 / xUnit2000×1(基线同样 3 个),无新增。
- `dotnet test` 全量:通过 1008 / 跳过 7 / 失败 0(总 1015);另有三次全量各见一条既有时序 flaky,见 §5 第 8 条。
- `--ai-selftest`(临时 TIPCL_HOME):通过 62 / 失败 0 / 跳过 0,exit=0;临时根下没有生成 `cache/ai_hooks`(审计日志已指到 temp 并清掉)。

## 5. 遗留问题与风险
1. **外部确认框是模态 `Dlg.Ask`**:主窗口不在前台时用户可能看不到,外部客户端会一直等到用户点(或客户端自己超时)。参考版没有这个问题是因为它根本不确认。后续可以考虑加 Toast/托盘提醒。
2. **外部调用不进 AI 页的步骤行**:参考版 AiPage 顶部有「MCP 已开启 · 工具名 时间」的状态条(`McpServer.ToolCalled`),Linux 只把事件与 `LastTool` 做好了,AI 页还没订阅显示 —— 归 AI 界面那一包或后续。
3. **长任务**:外部调用 `install_*` / `launch_game` 只拿到 `task_id` 与「已开始」,没有进度推送(MCP 的 SSE 流参考版也没做,GET 回 405)。
4. **并行范围**:只读并行的收益主要在 `search_*`(联网)与读日志/扫冲突;`search_*` 走同一个 `InProcessApi`,若某个搜索源内部有非线程安全的缓存,并行可能暴露出来 —— 本轮单测用假门面覆盖,真源没有压测。
5. **规则作用实例按「目标实例」**算,与参考版「按会话实例取 per_instance 规则」不同(更严);从 Windows 迁来的 per_instance 规则 Linux 本来就不读(存储结构不同,Linux 是平铺列表 + `instance` 键),这一点未变。
6. 钩子目前只有进程内注册口与内置审计;没有用户可见的「钩子管理」界面(参考版也没有)。
7. **`ClickSweepTests` 会点到设置页新加的 MCP 开关**(它遍历每页前 80 个 Button/ToggleButton,开关没有文字不在副作用词表里):无头会话里 `McpServerHost.Init` 不会被调用(没有执行器工厂,tools/call 只会回「未就绪」),但若真点到,测试进程会在 25585 上监听到进程结束、并把隔离根里的 mcp_server.json 写成 enabled。本包测试对此已稳健(自己的用例都用随机端口,门面用例先删偏好、结束 Stop);是否把「MCP」加进 ClickSweep 的跳过词表留给主线定(不是本包文件)。
8. **全量测试里观察到两条既有的时序型 flaky**(本机同时有其它 4 个代理在编译,CPU 吃紧):
   - `DlgCloseTests.Confirm_WindowClosed_ReturnsFalse`:栈是 `AiPage.RunAgentAsync` 发出的 Post(`OnAgentEvent` → `AiBubble` 构造 → `Res.Bind` 拿不到资源)在**下一个用例**的 `ResetForUnitTests` 里才落地 —— 前面有用例(`ClickSweepTests` 遍历 AI 页,会点到「点一下直接发」的建议按钮)在默认内置网关下真的发了一次对话,网络回包时机决定它炸在哪个后续用例上。AiPage 本包未改;这是既有的测试间泄漏。
   - `LayoutSmoothDragUiTests.EscapeDuringSmoothDragSlidesBackAndPersistsNothing`:300ms 动画的时序断言,与本包无关。
   同一提交连跑 4 次全量:两次各红 DlgClose 一条、一次红 LayoutSmoothDrag 一条、一次全绿;基线 `4277a02` 在同机跑 1 次全绿。单独跑 `DlgCloseTests` / `ClickSweepTests+DlgCloseTests` 均绿。建议主线给 ClickSweep 的 AI 页跳过建议按钮,或在 AiPage 的事件 Post 里判断页面已卸载。

## 6. 合并注意(冲突点)
- `ToolRunner.cs`:类声明改成 `public sealed partial class ToolRunner`。其他包按任务书也会加 partial —— **同一处同一改法**,合并时取一份即可。`RunAsync` 开头(未知工具检查之后)插了钩子段、权限段把 `Decide(call.name)` 改成 `ExternalWriteGate(... Decide(call.name, call.args))`、执行段 `Clip(AsText(result))` 改成 `Clip(RunAfterHooks(call, AsText(result)))`。别的包若也改 `RunAsync`,请保留这三处。
- `AiAgent.cs`:工具执行循环整体换了(foreach → 按组执行)。别的包若在旧 foreach 里插了逐个调用的逻辑(例如每个调用前后发新事件),合并时要挪进新的「组前发 tool / 组后发 tool_done」两段里,**别放回逐个 await 的写法**,否则并行失效。
- 新工具:只要进了 `ToolMeta.ALL`(readonly / side_effect / long_running 填对),调度与 MCP 暴露都自动生效;**只读工具若有共享可变状态,请把 side_effect 设成非 none/read/network 或标 long_running**,否则会被并行跑。
- `AiSelfTest.cs`:只在 `Check` 定义后与 `VerifyServices(...)` 后各加了几行。
- locales:8 个键插在 `"MCP Server 地址：{0}"` 之后。
- `SettingsPage.cs` 的 AI 卡加了一行字段;`App.axaml.cs` desktop 分支末尾加 5 行。

## 7. 需要改的 README / docs/PARITY.md 行(本包不改,交主线)

README.md:
- 第 76 行功能表「🤖 AI 助手」:`MCP Server（**MCP Server 仅 Windows**，见下）` → 去掉「仅 Windows」。
- 第 81-85 行「两条实现的差异」表:删掉 **MCP Server** 那一行(第 85 行),并把第 81 行「除下面三处外」改成「除下面两处外」。建议补一句 Linux 口径:「Linux 版外部发起的写操作每次都弹确认」。
- 第 165 行「平台口径」引用块:整段删除或改为「两版都有;Linux 版写操作每次在启动器里弹确认后才执行(Windows 版直接执行)」。
- 第 169 行:设置路径 Linux 同样是「设置 → AI 助手 → MCP Server」,可补「Linux 版端口也能在这一行改」。
- 第 183 行「端口可以在数据目录的 `mcp_server.json` 里改」:两版通用,保持。

docs/PARITY.md:
- 第 124 行「仍未做(如实)」第 2 条 `McpServer ... 不移植`:改为已移植(`Core/Launcher/Ai/McpServer.cs`,写操作强制确认,见 progress/AI-engine-mcp.md);第 3 条 `Hooks/AgentPresets`:`Hooks` 已移植(进程内 + 审计;插件侧仍无宿主),`AgentPresets` 仍未做。`--ai-selftest` 项数 +8。
- 第 129 行「仍不移植」清单:删掉 `Scheduler`(已移植,只读并行)、`McpServer`;`Hooks` 改为「已移植(进程内注册 + write_audit;无插件宿主所以没有插件钩子)」;`Permission` 那句补「规则的 content(限定参数)与 instance 已参与判定(R?/AI-engine-mcp)」。
- 第 147 行第 4 条「剩真模型端到端与 `McpServer`」→ 只剩真模型端到端。
- 第 350-359 行权限对位表:新增一行「规则限定参数 `content`(参考版 `Permission._matches` / `RuleContentFromInput`)| D-24 记录但不判定 | `PermissionRules`:键序同参考版、NormPathSpelling 后整串相等、禁止>允许>每次问、删除类无限定 allow 不生效;作用实例按目标实例」。
- 第 242 行 `PluginHooks / LaunchHooks` 仍「不移植」,可补一句「AI 工具钩子已有进程内 `Hooks`,将来有插件宿主时挂这里」。
- docs/DECISIONS.md(主线补):D-24「规则匹配只按工具名」与 D-28「McpServer 不移植」两条需要追加更正条目,指向本文件。
