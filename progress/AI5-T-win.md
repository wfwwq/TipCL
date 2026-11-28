# AI5-T(Windows 侧)· 假模型回放测试框架

- 包号:AI5-T(Windows 树)
- 分支 / worktree:`ai5/t-win` @ `C:\tipcl-ai5\t-win`(从 `b4cd7a2` 拉出)
- 日期:2026-11-28
- 覆盖场景:任务书 §7 AI5-T 那张 22 条场景表里标「两」和「W」的 **21 条**(第 20 条是 Linux 专有,不做)

---

## 1. 环境更正(与任务书 §1 的旧结论不同)

任务书 §1 记的「没有 net48 参照程序集 → Windows 树本机编译不了」**在本机不成立**:

- 参照程序集由 NuGet 包提供 —— `windos/src/TipCL/TipCL.csproj:53`
  `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />`,
  机器级 `C:\Program Files (x86)\Reference Assemblies\...\v4.8` 确实不存在,但不影响。
- 本机实测:Windows 树 **能编译、能跑测试**,所以本包**没有**「待环境」项,所有新用例都是真跑过的。
- 唯一的环境坑:`DOTNET_ROOT` 在当前 DSH 会话里仍是旧值,必须进程级覆盖:

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet build windos\TipCL.sln -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
```

红线遵守情况:没有跑 `--smoke`、没有跑 `tools/verify-smoke.ps1`、没有跑 ClickSweep / VisualTour / 任何 WPF 界面;
所有用例都在临时数据根(`TestHome`)里跑,不碰真实用户目录,不碰陶瓦联机 / GitHub / 防火墙。
异步执行全用 `-t:Rebuild`(避免「改了源码但 MSBuild 按旧时间戳跳过重编」的假红)。

---

## 2. 做了什么

### 2.1 注入点(唯一的生产代码改动)

`windos/src/TipCL/Core/Launcher/Ai/Client.cs`:**16 行新增 / 2 行替换**,`git diff -- windos/src` 只有这一个文件。

- 新增 `internal static Func<PyDict, PyDict, bool, AiResponse> TestTransport;`(参数:解析好的端点、请求体、是否流式)。
  生产恒为 `null`,行为与加之前逐字一致。
- `ChatStream`(原 :833)与 `ChatOnce`(原 :872)的网络入口改成
  `TestTransport != null ? TestTransport(...) : aiSession.Post(...)`;`AiRequestException → HttpError` 的原有分支保持不动。
- **假流不绕开解析路径**:流式响应仍旧是 `AiResponse.IterLines()` →`Client.AssembleStream` 的 SSE 解析,
  非流式仍旧是 `AiResponse.Text` → `PyJson.Loads`。这一条有专门的自检用例(T0)钉住:
  同一份 SSE 字节「直接喂 AssembleStream」与「经 ChatStream + TestTransport」解析出来的事件逐条相同,
  而且工具参数是跨两个 chunk 拼出来的(只有真解析器会这么拼)。
- `internal` + 反射挂载的原因:Windows 树**刻意没有** InternalsVisibleTo
  (`Core/Plugins/PluginManager.cs` 与 `Pages/Layout/Widgets/WidgetCommon.cs` 里各有一条注释说明「公开给单测是刻意的」),
  所以测试工程用反射挂 `Client.TestTransport`,不去改生产程序集的可见性。

### 2.2 FakeModel(`windos/tests/TipCL.Tests/AiReplay/FakeModel.cs`)

- 按脚本依次作答:正文 delta(可拆多片)、tool_calls(参数**分两片**吐,验证增量拼接)、usage、
  `finish_reason`(stop / tool_calls / length)、HTTP 层错误(429/5xx/4xx)、流内 error 事件、
  断流(`IOException` → `AiStreamBroken`)、不发 `[DONE]`、空回复。
- 每次请求都记下来:`model`、`stream`、`messages`(原样)、`tools`(原样)、端点;
  派生视图 `ToolNames` / `LastUserText` / `ToolResults` / `IsSummarizer`。
- 摘要请求(`Agent._summarize` 的 ChatOnce,不带 tools)自动作答,不占脚本步。
- 脚本用尽还来请求 → 直接抛错并把当时的请求打印出来(脚本写错当场暴露,不会静默变绿)。
- 记账加锁:并行工具组可能在别的线程上再打模型(同一回合派两个子代理),必须线程安全。

### 2.3 桩(`AiReplayHarness.cs`)

- **真 `BackendApi`** + 临时数据根:工具执行全部落在临时实例目录里(读日志、列实例、改实例 config 都是真的在跑),
  不新增任何对生产代码的桩接口(BackendApi 的方法是 non-virtual,本来也没法继承打桩)。
- `confirmFn` 桩:默认**拒绝**并记录每次询问(防止用例意外真的写盘);需要放行/记规则时用例自己覆盖。
- `askFn` 桩:默认回第一题的第一个选项 id。
- `onStatus` 旁路钩子(用于「工具跑完就置取消标志」这类场景)、插件事件快照、Trace / Store 落盘事件读取。

### 2.4 断言口径

每个用例都断言 `stop_reason` + 最终文本 + 请求序列(含 tools 集合与 model)+ 终态事件条数 + 工具调用序列,
外加插件事件序列 `turn/start → ... → turn/end`(`ChatEventLog.Snapshot`,并额外断言 `ChatEventLog.Running == false` 作为「UI 解锁」)。
断言尽量钉结构性字段(stop_reason / 事件类型与顺序 / 工具名集合 / 请求条数 / 落盘事实),
不钉 i18n 文案 —— 只有两处例外在自审后改掉了(见 §6)。

---

## 3. 文件清单

| 文件 | 说明 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Client.cs` | 唯一改动的生产文件:注入点 `Client.TestTransport` |
| `windos/tests/TipCL.Tests/AiReplay/FakeModel.cs` | 假模型(脚本 + SSE/JSON 报文 + 请求录制) |
| `windos/tests/TipCL.Tests/AiReplay/AiReplayHarness.cs` | 回放台(临时根、BackendApi、confirm/ask 桩、事件与落盘读取、断言助手) |
| `windos/tests/TipCL.Tests/AiReplay/AiReplayHarnessTests.cs` | 框架自检 T0/T1:假流走真解析路径、请求录制完整 |
| `windos/tests/TipCL.Tests/AiReplay/AiReplayLoopTests.cs` | 场景 1、2、3a、3b、4、5、7、8、9a、9b、10a、10b、11、13、14、15、19a、19b |
| `windos/tests/TipCL.Tests/AiReplay/AiReplayPlanSubagentPermissionTests.cs` | 场景 6、12a、12b、21、22a、22b |
| `windos/tests/TipCL.Tests/AiReplay/AiReplayRoutingTests.cs` | 场景 16a、16b、16c、17、18a、18b |

生产代码零改动以外的部分:除 `Client.cs` 外没有碰任何 `windos/src` 文件(见 §5 的 diff 证据)。

---

## 4. 验证结果

### 4.1 编译

```
dotnet build windos\TipCL.sln -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
→ 0 error / 2 warning
```
两条警告都是既有的 `xUnit1031`(`BridgeMutationAndLaunchGuardTests.cs:34-35`),与基线逐条相同,**零新增警告**。

### 4.2 测试

| | 通过 | 跳过 | 失败 | 总计 |
|---|---|---|---|---|
| 基线(AI5-0) | 796 | 6 | 0 | 802 |
| 本包(基线 + AI5-T) | **824** | **13** | **0** | **837** |

- 新增 **35 条**(28 条绿 + 7 条按包标 Skip),绿色新增 28 条 ≥ 覆盖到的「现状」场景数;
  净增 = 824 − 796 = 28。
- 13 条跳过 = 既有 6 条 + 本包 7 条(清单见 §6)。
- 整包里的失败数为 0;跑全量时踩到过一条**我自己的**断言不稳定(场景 3b 断言并行组内 tool/start 的固定顺序,
  实测两次跑出来的顺序不同 —— 这条不稳定本身就是「组内确实并发」的证据),已改成集合比较 + 只在组间钉顺序。

### 4.3 体积

| | compact exe |
|---|---|
| 改动前(同机同命令 `-t:Rebuild`) | 5,759,488 B |
| 改动后 | **5,760,000 B(+512 B)** |

- +512 B 全部来自注入点(一个静态字段 + 两处条件分支);按 lead 裁决本轮不回收,如实记录。
- 红线 5,757,264 B 在本包开工前就已经超了(基线 5,759,488 B,既有偏差 2,224 B,与 AI5 无关);
  本包后又超 2,736 B。是否合格由用户裁决,本包不声称结论。
- 没有跑 `--smoke`(红线),未做 premium 版体积测量。

---

## 5. 复现 → 修复 → 用例转绿(三步证据)

本包**不含缺陷修复**,所以「复现 → 修复 → 转绿」在这三条上体现:

1. **复现(注入点之前)**:注入点加之前,`Client.ChatStream` 只能真发 HTTP,端到端行为测不了(全仓 `replay|golden|eval` 零命中)。
2. **改动**:唯一的生产改动(`git diff --stat -- windos/src` → `Client.cs | 16 +++++-`,2 行替换)。
3. **转绿**:35 条新用例里 28 条当场绿(含 21 条场景里的「现状」部分),
   另外 7 条按期望行为写好、用 `Skip` 标住并注明转绿包号 —— 这 7 条**确实红**,不是「没测」:
   临时摘掉 Skip 实跑一遍拿到的失败证据见 §6 表格里的「现状红证据」列。

`git diff --stat`(提交前):

```
 windos/src/TipCL/Core/Launcher/Ai/Client.cs | 18 ++++++++++++++++--
 1 file changed, 16 insertions(+), 2 deletions(-)
```
`windos/tests/TipCL.Tests/AiReplay/` 是纯新增,未改任何既有测试文件。

---

## 6. 场景表逐条状态(21 条)

| # | 场景 | 树 | 期望 | 本包状态 | 现状红证据 / 转绿 |
|---|---|---|---|---|---|
| 1 | 纯文字回复 | 两 | `no_tool_call`,1 条终态 | ✅ 绿(1a) | Windows 实际是 `completed`:回复不声称「已经做过」时,`Agent._result` 会把 `no_tool_call` 归一到 `completed`;声称做过而没调工具才保留 `no_tool_call`(1b 锁这条)。**与表里的期望不同,属既有差异,建议 R 包收录** |
| 2 | 单个只读工具 → 文字 | 两 | `completed` | ✅ 绿 | 断言工具结果带 `[来源: 本地文件/日志/状态读取]` 前缀 |
| 3 | 一轮多个工具,部分并行 | 两 | 分组符合 `Scheduler.Group`,结果顺序正确 | ✅ 绿(3a 纯分组 + 3b 端到端) | 3b 用「两读并行组 + 一个写工具串行组」钉:写工具的 `tool/start` 必然晚于三条只读的 `tool/result`,工具结果消息顺序 == `tool_calls` 顺序 |
| 4 | 写工具,用户拒绝 | 两 | `denied`,不落盘,模型收到拒绝结果,轮次继续 | ✅ 绿 | 断盘断言:目标文件不存在、无 `ToolStarted`、`tool/result.isError == true` |
| 5 | 写工具确认后返回 `AiRule` | 两 | 规则写入,后续同类调用不再询问 | ✅ 绿(Windows 侧) | 规则落进 `ai_permissions.json` 的 `global.allow`;同回合第二次、下一轮(重新读盘)都不再询问。Linux 的「选全局重启仍生效」(B3)不在本包 |
| 6 | `update_plan` 后计划被驳回 | W | 无写操作、无检查点 | ⏸ **Skip(P2)** | 失败在 `Assert.Empty(StoreEventsOf("ToolStarted"))`:审批被驳回时本轮的 `update_plan` 已经跑过了 —— 工具执行确实排在 `plan_approval` 之前 |
| 7 | 流式报上下文溢出 | 两 | 1 次反应式压缩后继续 | ✅ 绿 | 断言 `Trace.reactive_compact` / `Store.CompactBoundary(trigger=reactive)` 各 1 条,重发后 `stop_reason == completed` |
| 8 | `finish_reason=length` | 两 | 最多续写 2 次 → `truncated` | ✅ 绿 | 3 次请求;第 2、3 次请求里能看到「请从断开处继续」 |
| 9 | 流式失败 → 非流式兜底成功 | 两 | 文本不重复拼接;兜底 tools == 流式 tools | ✅ 绿(9a)+ ⏸ **Skip(D8)**(9b) | 9b 失败证据:`Expected`(流式那 17 个 CORE 工具)vs `Actual`(全量 56 个) |
| 10 | 流式和兜底都空 | 两 | `stream_failed`/`empty_response`,1 条终态 | ✅ 绿(10a/10b) | 10a=流内报错且不可重试 → 兜底也空 → `stream_failed`;10b=流正常返回空内容 → 兜底也空 → `empty_response`;两条都断言 `turn/end` 恰好 1 条 |
| 11 | 模型调用了本轮没下发的工具 | 两 | 记录 `tool_schema_miss`,全量重试一次 | ✅ 绿 | 第一个请求不含 `fs_read` → 第二个请求是全量 56 个 → `tool_schema_miss` 恰好 1 条;重试后仍没下发的工具不再二次升级 |
| 12 | 子代理尝试写工具 | 两 | 被拦截 + 兜底请求没有写工具 | ✅ 绿(12a)+ ⏸ **Skip(D8)**(12b) | 12a:子代理的两次请求里都没有 `fs_write`,`Trace.subagent_write_blocked` 非空,模型收到「子代理只读」,文件不存在。12b 失败证据:兜底请求 56 个工具里 **24 个是写工具**(逐条列在失败信息里) |
| 13 | 中途取消 | 两 | `cancelled`,插件收到 `turn/end reason=cancelled` | ✅ 绿 | 抛 `AgentCancelled`;`turn/end{reason:"cancelled", stopReason:"cancelled"}` 恰好 1 条;`Store.TurnFailed` |
| 14 | 达到最大轮数 | 两 | `max_rounds`,恰好 1 条终态,上限读 `ai_max_rounds` | ✅ 绿 | `ai_max_rounds=2` → 2 次请求、`rounds_used=2`、`Running==false`(UI 解锁) |
| 15 | 先 429/5xx | 两 | 重试 + 粘性切到 `ai_fallback_model` + 切换记录 | ✅ 绿 | 5 次请求:前 4 次都是主模型(3 次退避重试),第 5 次 `model == fake-fallback`;`Store.ModelFallback` / `Trace.model_fallback` 各 1 条。**用例本身约 6 秒**(3 次退避是产品行为:0.8/1.6/3.2 秒×随机系数) |
| 16 | 「给 1.20.1 Fabric 装上钠和小地图」 | 两 | `search_mods` → `ask_user` → `install_mod`×2 | ✅ 绿(16a 路由 + 16b 离线派发)+ ⏸ **Skip(离线红线)**(16c) | 16a:命中意图门 + 三件套都在下发集合里;16b:`ask_user`(askFn 桩作答)→ `install_mod`×2(confirmFn 桩拒掉,不联网不下载)。16c 的完整序列要真去查 Modrinth,按红线不跑 |
| 17 | 「游戏闪退了」 | 两 | `get_crash_report`/`get_latest_log` → `diagnose_launch` | ✅ 绿 | 三条只读、全程不弹确认、工具序列与请求条数都钉住 |
| 18 | 「好卡」 | 两 | `get_system_info` → `diagnose_lag` → `tune_jvm` | ⏸ **Skip(无包号,新发现)** + ✅ 绿(18b) | **新发现**:`ExtraTools.Keywords` 里只有「很卡」「卡顿」,没有单独的「卡」,所以场景表原文「好卡」命不中,首轮不下发卡顿三件套。18b 用「帧数」命中的文本来验,这三件套都**不在** `CORE_TOOLS` 里,所以是真在验关键词路由。建议并入 C3(工具选择量化) |
| 19 | 第一轮装钠 → 第二轮再装个小地图 | 两 | 第二轮仍下发 `install_mod` | ✅ 绿(19a)+ ✅ 绿(19b) | 19a 是 CORE 常驻回归(**不敏感**:`install_mod` 本来就在 CORE);19b 是 C1 的敏感面:上一轮说「BSL 光影」、这一轮只说「再来一个吧」(本身零关键词),仍然要把非 CORE 的 `search_content` / `install_shader` 带出来。Windows 现状本来就回看最近 12 条 user/tool 消息,所以 19b 现在绿 |
| 21 | 派两个子代理 | 两 | 会话 id 不同 | ⏸ **Skip(S)** | 失败证据:`-sub` 会话文件只有 1 个(`Expected 2 / Actual 1`)—— 会话 id 固定成 `<父id>-sub` |
| 22 | `dontAsk` 档 | 两 | 写工具不弹卡直接执行;删除类仍拒绝;显式 deny 仍生效 | ⏸ **Skip(P1)**(22a)+ ✅ 绿(22b) | 22a 失败信息:「「不询问」= 自动放行,不该被拒」(现在是 ask→deny,文件没写成)。22b 是现状就该绿、P1 之后也必须绿的守卫:删除类在 `dontAsk` 下仍拒绝;**显式 deny 规则在 `yolo` 档下也优先**(用例用 `yolo` + deny 规则验证) |
| 20 | 记忆面板双向可见 | L | — | — | 场景表标 Linux,不在本包 |

**Skip 汇总(7 条)**:P2 ×1(场景 6)、D8 ×2(场景 9b、12b)、S ×1(场景 21)、P1 ×1(场景 22a)、
离线红线 ×1(场景 16c)、无包号的新发现 ×1(场景 18a)。

---

## 7. 遗留问题 / 需要别人接手的事

1. **场景 18a 的关键词缺口**(新发现,无对应包号):`ExtraTools.Keywords` 的卡顿组是
   `卡顿 / 很卡 / 掉帧 / 帧数 / fps / lag / 优化 / 流畅 / 内存 / JVM / GC / 电脑配置` —— 用户口语里的
   「好卡」「卡」命不中。建议并入 C3(工具选择先量化再定方案),或者单独补一条关键词。
2. **场景 1 的 stop_reason 与场景表不一致**:表里写 `no_tool_call`,Windows 实际在没有「声称做过」的纯文字回复上
   返回 `completed`(`Agent._result` 的归一逻辑)。这是既有行为差异,建议 R 包收录;本包不改行为。
3. **场景 12 的拒绝形态**:Windows 在**执行前**拦截时回给模型的是「[权限] 已拒绝:子代理只读:不能执行写操作」
   (走 `TurnState.SetToolStatus`),不是 `permission_denied` 的结构化 payload(那条路径是执行期拦截才有的)。
   用例按文案常量 `Agent.SUBAGENT_READONLY_REASON` 断言,跨树口径要不要统一留给 R 包。
4. **子代理/并行组的假模型**:本包里唯一会「并行打模型」的场景(21)是 Skip 的,
   所以 FakeModel 虽然加了锁,但「两个子代理并发时脚本步的分配顺序不确定」这件事没有被真正验证过;
   S 包把会话 id 拆开之后,建议给 21 补一条更细的用例(例如按 task 文本区分脚本)。
5. **场景 15 耗时**:单条用例约 6 秒(退避 3 次是产品行为)。如果以后有 CI 时长压力,可以让 `_sleep_backoff` 的
   随机数/睡眠可注入,但那属于生产代码改动,本包没做。
6. 体积:见 §4.3,红线在本包之前就已超,本包又 +512 B,如实记录,不做回收(lead 已裁决)。

## 8. 要改的文档行

- `windos/docs/改造工程:2611/AI-agent-合并任务书.md` §1 环境行(「没有 net48 参照程序集 → Windows 树本机编译不了」):
  实际由 NuGet 包提供参照程序集,本机可编译可测。建议 §6 的 Windows 构建命令改成带 `-t:Rebuild`。
- 任务书 §7 AI5-T 验收:「挂起场景清单写进 `progress/AI5-T.md`」—— Windows 侧落在本文件 `progress/AI5-T-win.md`。
- 场景 18 的建议关键词(见 §7.1),采纳的话要同步 `AiData.KEYWORD_TOOLS` 的兄弟位置:
  `windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs` 的 `Keywords`。
