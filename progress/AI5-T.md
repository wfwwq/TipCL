# AI5-T · 假模型回放测试框架(Linux 侧)交付说明

- 日期:2026-11-28
- 分支 / worktree:`ai5/t-linux` @ `C:\tipcl-ai5\t-linux`(从 `b4cd7a2` 拉出)
- 任务书:`windos/docs/改造工程:2611/AI-agent-合并任务书.md`(注:该文件**未被 git 跟踪**,目录名里是全角冒号;
  本 worktree 里没有它,读的是中立共享目录 `C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`,567 行 / 47986 字节)
- 范围:只做任务书 §7 AI5-T 的 **Linux 侧**;场景编号以 §7 那张 22 条表为准,只做标「两」与「L」的场景

---

## 1. 做了什么

一句话:**在 Linux 树建起假模型回放框架**,把「带工具的一轮对话」整条链路(AiAgent 主循环 →
Client 流式/非流式请求 → 工具执行 → 权限 → 事件与终态)放进可脚本化的回放里。

1. **注入点**(生产代码的唯一改动):`src/TipCL.Core/Launcher/Ai/Client.cs` 加了
   `internal static Func<TestRequest, AiResponse>? TestTransport`(连带一个 `internal sealed class TestRequest`
   请求快照)。非 null 时 `ChatStream` / `ChatOnce` 不再建 `AiSession`、不走网络,改由它按请求体造一个
   `AiResponse`;响应体的消费路径**一个字都没改** —— 流式仍走 `Client.AssembleStream` 的 SSE 装配
   (含 tool_calls 增量拼接、usage、`finish_reason`、`[DONE]`),非流式仍走 `Client.OnceReply` 的 JSON 解析
   (这一步是把原来内联在 `ChatOnce` 里的那段抽成一个私有方法,两边共用一份)。
   **生产恒为 null,行为不变。**
2. **FakeModel**:按脚本依次吐 delta(正文切成 3 字一片,专门压增量拼接)、tool_calls(参数分两片)、usage、
   `finish_reason`(stop / length / tool_calls)、三类错误 —— 上下文溢出(流内 error 事件)、可重试的 429/5xx
   (HTTP 状态码)、致命 401;并记录每次请求**实际收到**的 model / messages(深拷贝)/ tools(有序工具名)。
   脚本用尽还来请求就直接抛,免得「少写了一段脚本」被当成通过。
3. **桩**:假 `ToolRunner.IBackend`(记录后端方法调用、并发峰值、可选人为耗时)、`confirmFn`、`askFn`、
   `PlanChanged`、`TaskStarted`;检查点 / 会话事件 / 追踪 / 产物 / 钩子目录全部指到测试台自己的临时目录。
4. **断言**:每条场景都按表断言五样里适用的那几样 —— `stop_reason`、最终文本、请求序列(含 tools 集合与
   model)、终态事件条数(`done`/`error`)、工具调用序列;不是只看「有没有报错」。
5. **场景表**:22 条在 Linux 侧全部落地(场景 6 按 Linux 的等价口径写),另外补了 6 条「补」用例
   (把表里那条字面场景在 Linux 上侥幸绿、或任务书没给包号的那半单独钉住):`02补 / 06补 / 12补 / 13补 / 15补 / 19补`。
   合计 29 条用例 = 22 条表内 + 7 条「补」。

## 2. 文件清单

| 文件 | 说明 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/Client.cs` | **唯一的生产改动**:+62 行、0 删除(纯新增),只有注入点与 `OnceReply` 抽取 |
| `tests/TipCL.Tests/Ai5T/FakeModel.cs` | 假模型:脚本(`FakeReply` / `FakeToolCall`)、请求记录(`FakeRequest`)、SSE 造帧 |
| `tests/TipCL.Tests/Ai5T/Ai5THarness.cs` | 测试台:假后端 `FakeBackend` + 桩 + 运行/事件收集(`Ai5TRun`)+ 目录隔离 |
| `tests/TipCL.Tests/Ai5T/Ai5TScenarioTests.cs` | 场景 1/2/2补/3/4/6/6补/13/13补/16/17/18 |
| `tests/TipCL.Tests/Ai5T/Ai5TPendingScenarioTests.cs` | 场景 5/7/8/9/10/11/12/12补/14/15/15补/19/19补/20/21/22 |
| `progress/AI5-T.md` | 本文件 |

生产代码除 `Client.cs` 的注入点外**零改动**(`git diff --stat` 只有 `Client.cs` 一个文件)。

## 3. 怎么跑这套测试

```powershell
# ⚠ 本机必须覆盖 DOTNET_ROOT:用户级 DOTNET_ROOT 指向 C:\Users\Administrator\.dotnet(只有遥测文件),
#    不覆盖会报 "hostfxr.dll could not be found"(任务书 §1 记的「testhost 起不来」就是这个)
$env:DOTNET_ROOT='C:\Program Files\dotnet'

cd <worktree 根>
dotnet build tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false

# 只跑回放套件
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo --no-build --filter "FullyQualifiedName~TipCL.Tests.Ai5T"

# 全量(排除会碰真实陶瓦联机 / 拉起浏览器或资源管理器的三类)
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
```

新增用例挂在串行集合 `GlobalStateCollection` 上(注入点是进程级静态量、用例还会改 `Config`,见
`tests/TipCL.Tests/GlobalStateCollection.cs` 的扫描规约)。

## 4. 场景表逐条结果(22 条)

「字面绿」= 表里那条原样写下来现在就绿;「挂起」= 按表写的期望行为,现在红,已加 `Skip` 并注明转绿包号。

| # | 场景 | 树 | 期望转绿 | 本条结果 | 备注 |
|---|---|---|---|---|---|
| 1 | 纯文字回复 | 两 | 现状 | **绿** | `no_tool_call`、恰 1 条 `done`、1 次请求、工具表 == `SelectToolSchemas` |
| 2 | 单个只读工具 → 文字 | 两 | 现状 | **挂起(A6)** | 表里期望 `completed`,Linux 现在给 `no_tool_call` —— 见 §5 口径差异 1 |
| 3 | 一轮多个工具,部分并行 | 两 | 现状 | **绿** | 分组 == `Scheduler.Group` 的 `[3,1,1]`;观测到并发峰值 ≥ 2;tool 消息顺序与 tool_call 一一对应 |
| 4 | 写工具,用户拒绝 | 两 | 现状 | **绿** | 状态 `denied`、后端 0 次 `install_mod`、模型收到 `permission_denied`、轮次继续 |
| 5 | 写工具确认后返回 AiRule / 选作用域 | 两 | L→B3 | **挂起(B3)** | 进程内「本对话都允许」已生效;选「全局」后换实例仍不询问 = 红(`ai_permission_rules` 为空) |
| 6 | `update_plan` 后计划被驳回 | W(L🕓) | P2 | **绿(等价口径)** | Linux 无计划审批流程,等价保证 = plan 档下写工具一律 deny 且不落盘。另见 §5 发现 1 |
| 7 | 流式报上下文溢出 | 两 | L→A4 | **挂起(A4)** | 现在 `stop_reason = stream_failed`,没有反应式压缩 |
| 8 | `finish_reason=length` | 两 | L→A6 | **挂起(A6)** | 现在给 `no_tool_call`,不续写、不给 `truncated` |
| 9 | 流式失败 → 非流式兜底成功 | 两 | L→A5 | **挂起(A5)** | 现在 0 次非流式请求 |
| 10 | 流式和兜底都空 | 两 | L→A2/A6 | **挂起(A2/A6)** | 现在 **2 条 error**,第二条文案就是内部代号 `stream_failed` |
| 11 | 模型调用了本轮没下发的工具 | 两 | L→C2 | **挂起(C2)** | Linux 判据是「注册表里没有」(`totally_unregistered_tool`),现在 `unknown_tool` 直接回错、没有全量重发;「未下发但注册表里有 → 照常执行」由 `场景11补` 钉住并且是绿的 |
| 12 | 子代理尝试写工具 | 两 | W→D8 | **绿** | 子代理写调用被拦成 `permission_denied`、后端 0 次落盘;`场景12补`(子代理请求里不含写工具)挂起,见 §5 发现 3 |
| 13 | 中途取消 | 两 | 现状 | **绿** | 工具执行中取消 → `cancelled` + `OperationCanceledException`;`场景13补`(流式过程中取消)钉住现状,见 §5 发现 2 |
| 14 | 达到最大轮数 | 两 | L→A1/A7 | **挂起(A1/A7)** | 现在第 4 轮还会发起(上限硬编码 12,不读 `ai_max_rounds`) |
| 15 | 先 429/5xx | 两 | L→A8 | **挂起(A8)** | 重试那一半已绿(`场景15补`);粘性切 `ai_fallback_model` 那一半红 |
| 16 | 「给 1.20.1 Fabric 装上钠和小地图」 | 两 | 现状 | **绿** | `search_mods → ask_user → install_mod ×2`,三样工具第一轮即下发,答案回填进工具结果 |
| 17 | 「游戏闪退了」 | 两 | 现状 | **绿** | `get_crash_report / get_latest_log → diagnose_launch`,只读流程不弹卡 |
| 18 | 「好卡」 | 两 | 现状 | **绿** | `get_system_info → diagnose_lag → tune_jvm`,「好卡」命中关键词、三样都下发 |
| 19 | 两轮之间工具表不该丢 | 两 | L→C1(已在 `880b37d` 落地) | **绿(字面)+ 挂起(19补)** | 字面那条靠 `install_mod` 是常驻工具而侥幸绿(**测不出 C1**,已改成 CORE 常驻工具的回归断言);真实缺口见 §5 发现 4,`场景19补`(敏感版)在含 C1 的树上实测为绿、本分支上为红,合流后改记「现状」 |
| 20 | 记忆面板与助手共用一份记忆 | L | B1 | **挂起(B1)** | 面板写 `ai_memory.md`、助手读 `ai-memory.json`,两个方向都看不到 |
| 21 | 派两个子代理 | 两 | S | **挂起(S)** | 两次派发写同一份会话事件(`replay-sub.jsonl`) |
| 22 | `dontAsk` 档 | 两 | P1 | **挂起(P1)** | 现在写工具被直接拒绝,不弹卡也不执行 |

**统计**:实做 29 条用例(22 条表内 + 7 条「补」)= **14 绿 / 15 挂起 / 0 失败**。
「现状」那一批里除场景 2 外全绿;场景 2 的差异见 §5。

### 挂起场景清单(转绿时把 `Skip` 去掉、把断言跑绿即可)

| 挂起用例 | 转绿包号 | 现在红在哪里(复现证据,均为未加 `Skip` 时的实测输出) |
|---|---|---|
| `场景02_..._completed` | A6 | `Expected: "completed" / Actual: "no_tool_call"` |
| `场景05_确认卡选了全局_重启后仍然不询问` | B3 | `PermissionRules.Pick(ai_permission_rules, install_mod) → Expected: "allow" / Actual: ""` |
| `场景06补_被拒的写工具不该留下检查点` | **无(见 §5 发现 1)** | `Checkpoint.TurnHasOps` 为 true,journal 里已有该轮的一条 `files:[{path: "...\.minecraft\default\mods", sha256: "", size: 0}]` |
| `场景07_上下文溢出_...` | A4 | `Expected: Not "stream_failed" / Actual: "stream_failed"` |
| `场景08_输出被截断_...` | A6 | `Expected: "truncated" / Actual: "no_tool_call"` |
| `场景09_流式参数不全_兜底...` | A5 | `Assert.Single(run.OnceRequests)` 失败:非流式请求 0 次 |
| `场景10_流式与兜底都空_...` | A2/A6 | `Assert.Equal(1, run.ErrorCount)` 失败:实际 2 条,其中一条文案是 `stream_failed` |
| `场景11_调用了不存在的工具_记schema_miss并用全量工具重试一次` | C2 | `Assert.Equal(2, run.StreamRequests.Count)` 失败:只有 1 次请求,没有全量重发 |
| `场景12补_子代理的请求里不含写工具` | **无独立包号**(A5 / P2 核实项相关) | 子代理请求的工具表里出现 `install_game / install_mod / install_modpack …` |
| `场景14_轮数上限读设置...` | A1/A7 | `Expected: "max_rounds" / Actual: "no_tool_call"`(请求发起 13 次,不是 4 次) |
| `场景15_先429_重试并粘性切到兜底模型` | A8 | `Expected: "fallback-model" / Actual: "deepseek-v4-flash"` |
| `场景19补_续话仍应下发上一轮命中的非CORE工具` | C1(已在 `880b37d` 落地) | 第二轮请求里没有 `install_shader` / `search_content` —— 见下面的「C1 已修,本分支未含」 |
| `场景20_面板写入的记忆助手能看到_反之亦然` | B1 | 系统提示里找不到 `GTX 1060` |
| `场景21_两个子代理的会话id不同` | S | 只有一个 `replay-sub.jsonl`(期望 2 个) |
| `场景22_不询问档_...` | P1 | `Assert.Equal(1, h.Backend.Count("install_mod"))` 失败(实际 0,写工具被拒了) |

> 说明:这些用例**连着完整的期望断言一起留在测试工程里**,不是占位符;修复包去掉 `Skip` 就能直接验收。

### C1 已修、但本分支未含它 —— 场景 19 的验证补充

C1 由 linux-tools 在 `880b37d` 落地(`Tools.SelectToolSchemas` 的关键词命中改成聚合最近 12 条
user/tool 消息,ACTION_RE/QUESTION_RE 仍只看本轮用户话)。我的分支从 `b4cd7a2` 拉出,**不含该提交**,
所以 `场景19补` 在本分支上仍是红的 —— 这是预期的,不是回归。

为了让「转绿」这句话有实测依据,本轮做了一次**只读**验证:用 `git archive 880b37d` 把那个提交的整棵树
解到系统临时目录(不新建 worktree、不动别人的分支),把本包的 `Client.cs` 注入点与 4 个测试文件复制进去,
去掉 `场景19补` 的 `Skip` 后单跑,结果:

| 代码树 | `场景19`(字面) | `场景19补`(敏感版) |
|---|---|---|
| 本分支 `b4cd7a2` + 注入点 | 绿 | **红**:`第二轮只凭最后一条用户消息选工具,把上一轮命中的 install_shader 丢了(见 C1)` |
| `880b37d`(含 C1)+ 注入点 | 绿 | **绿** |

=> **合流(或 rebase 到 C1 之上)之后,把 `场景19补` 的 `Skip` 去掉即应直接变绿**;届时它从
「转绿:L→C1」改记「现状」。验证用的临时目录已删除,分支上没有任何多余提交。

### 场景 19 的敏感版怎么写的(给 t-win 对齐口径)

- 第一轮:「帮我装个光影」→ 命中 `光影` 这条关键词,下发**非 CORE** 的 `search_content` / `install_shader`
  (用例里断言这两个都不在 `CORE_TOOLS`,否则测不出 C1);
- 第二轮:「**再来一个**」→ 本轮一个关键词都没有,`ACTION_RE` 也不命中(所以拿不到「下命令就给所有写工具」的兜底),
  只可能靠「上一轮的话还在窗口里」把这两个工具带出来;
- 同时钉反向:不相关的非常驻工具(`tune_jvm` / `create_datapack`)不许冒出来 —— 防「直接把 forceAll 打开」这种假绿。

### 场景 11 的触发口径(2026-11-28 裁决,两棵树有意不同)

- **Windows** = 模型调了「**本轮没下发**」的工具 → 丢弃整轮调用 + 用全量工具重发;
- **Linux** = 模型调了「**注册表里根本没有**」的工具名 → 记 `tool_schema_miss` + 用全量重发。

Linux 不跟 Windows 一致是有意的:Linux 的执行层(`ToolRunner` 只认 `ToolMeta`)本来就不要求工具先被下发,
所以「调了未下发但真实存在」在 Linux 上是**能正常执行**的路 —— 按「没下发」判会把一条能跑的路变成
丢弃重发,那是回归。因此:

- `场景11` 改用**不存在**的名字复现(`totally_unregistered_tool`),断言:记 `tool_schema_miss`、
  用全量工具重发一次(每轮最多 1 次)、最终 `stop_reason` 不是失败(仍挂起等 C2);
- 新增 `场景11补`(现状即绿)钉住分界的另一半:调了「**未下发但注册表里有**」的真实工具
  (`get_java_list`)→ 执行层照常执行、**不**触发 schema-miss 全量重发。这条防以后有人把 Linux
  改成 Windows 口径时静默回归。

## 5. 超出任务书的发现(红线 5:记在这里)

1. **plan 档下被拒的写工具仍会留下检查点**(无转绿包号)。
   `AiAgent.ExecuteOneAsync` 先 `SnapshotForTool(call)` 再 `_runner.RunAsync(call)` 判权限,于是权限判成
   `deny` 也已经在 journal 里记了一条操作。实测 journal 原文:
   `{"turn_id": "t20261128144121-8a96", "files": [{"path": "...\.minecraft\default\mods", "sha256": "", "size": 0, "mtime": 0}]}`。
   后果:「撤回上一轮」会多出一个什么都没做的空回合。任务书场景 6 的期望里写着「也没有生成检查点」,
   但把这一条挂在 Windows 的 P2 上;Linux 侧没有对应包号,**建议并进 A1(收敛出口)或单独立包**。
   本包不改生产代码,已用 `场景06补` 挂起锁住。
2. **流式过程中取消会记成 `stream_failed` 并报两条 error**(A1/A2 的证据)。
   `场景13补` 实测:取消落在流式响应消费期间时 `stop_reason = stream_failed`、`error` 事件 2 条
   (第一条文案「已停止」,第二条文案就是内部代号 `stream_failed`)。只有取消落在工具执行里才给
   `cancelled`。A1/A2 落地后这条应当改成「恰好 1 条终态事件 + `cancelled`」,改它是那两个包的验收动作。
3. **子代理的请求里含写工具**(任务书只把这条挂在 Windows 的 D8 上)。
   Linux 的 `StreamOnceAsync` 不分 `IsSubagent`,子代理下发的工具表和主对话一样(含 `install_mod` 等)。
   执行层拦得住(plan 档),但表里期望是「子代理的兜底请求里也没有写工具」;Linux 没有兜底,
   等价的「子代理流式请求里也没有写工具」也不成立。任务书没给 Linux 侧包号,已用 `场景12补` 挂起。
4. **场景 19 的字面场景测不出 C1**(`install_mod` 属于 `CORE_TOOLS`,常驻,第二轮照样下发)。
   C1 真正缺的是**非常驻**工具:第一轮「帮我装个光影」命中的 `search_content` / `install_shader`,
   第二轮只说「再来一个」就丢了(旧口径 `SelectToolSchemas` 只看 `LastUserText`)。
   已用 `场景19补`(敏感版)锁住这个真实缺口 —— 写法与实测见上一节,转绿 C1(该提交已由 linux-tools 完成)。
5. **口径差异 1:场景 2 的 `completed`**。表里期望 `completed` 且标「现状」,但 Linux 侧
   `StopReason.COMPLETED` 从来没有被赋过值(正是 A6 说的「这些枚举在 Linux 侧从未被赋值」),
   现在给的是 `no_tool_call`。本包按**表里的期望**写断言并挂起(转绿 A6),同时用 `场景02补` 钉住
   Linux 现状 `no_tool_call`,免得修 A6 的人以为它是新长出来的。
6. **场景 6 的 Linux 侧核实结论**:Linux **没有**计划审批流程 —— 全仓 grep `plan_approval` /
   `计划审批` 只命中词表里的一句文案「计划未获批准,本轮到此为止」。等价保证是 `ToolRunner.DecideRaw`
   第一条硬停(plan 档 + 非只读 → deny),所以 `场景06` 按等价口径断言并且是绿的;按任务书 P2 的说法
   「如果确实没有,记进 R 包的差异清单,本包不新增」。
7. **环境**:`dotnet test` 起不来不是 testhost 的问题,是用户级 `DOTNET_ROOT=C:\Users\Administrator\.dotnet`
   (只有遥测文件、没有 hostfxr)。每条 pwsh 命令里覆盖 `$env:DOTNET_ROOT='C:\Program Files\dotnet'` 即可;
   基线包已记,这里重复一句是因为「跑这套回放」也依赖它。

## 6. 验收数字

| 项 | 数字 |
|---|---|
| Linux Release 构建 | `0 error / 1 warning`(`src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649,既有),**不新增警告** |
| 回放套件 | `14 过 / 15 跳 / 0 败 / 总 29`(约 3 s) |
| 全量 `dotnet test`(按红线排除 Terracotta / ClickSweep / OpenUrl) | `1608 过 / 26 跳 / 0 败 / 总 1634` |
| 与 AI5-0 基线(`1594 过 / 11 跳 / 总 1605`)对比 | 总数 +28(= 本包新增用例数);通过 +13、跳过 +15;失败 0 |
| 生产代码改动面 | `git diff --stat` 只有 `src/TipCL.Core/Launcher/Ai/Client.cs`:**+62 行 / -0 行** |

**体积记录(如实记录,不做回收,也不下结论)**:本包**不碰 `windos/**`**,Windows 的 compact exe 未被改动,
仍是基线实测的 `5,759,488 B`(红线 5,757,264 B,开工前就超 2,224 B,与 AI5 无关)。
Linux 侧改动后的 Release 产物:`src/TipCL.App/bin/Release/net8.0/TipCL.App.dll` = 1,976,320 B、
`TipCL.Core.dll` = 2,151,936 B(改动前未逐字留底;本包对生产代码是纯新增 62 行,没有删除)。

## 7. 遗留问题

1. `场景06补` 与 `场景12补` 没有对应的转绿包号(按 §5 发现 1、3),需要任务书补一条裁决或并进已有的包。
2. `场景19补` 的 `Skip` 要等本分支合到含 C1(`880b37d`)的代码之后再去掉;已在含 C1 的树上实测为绿(见 §4 的验证补充)。
3. 场景 2 的 `completed` 期望与 Linux 现状冲突(§5 发现 5),请在与 A6 对接时确认口径:是让 A6 在
   「有工具跑过、然后模型收尾」时给 `COMPLETED`,还是把表里这一格改成 `no_tool_call`。
4. 尺寸:本包新增 4 个测试文件(合计 1451 行 / 77,543 B:harness 394 行、假模型 430 行、场景 398 行、挂起场景 453 行)。
   AI5-T 是开发期基础设施,不进产品二进制;若后续要求压测试代码体积,可把「补」用例合并回对应场景。
5. `FakeReply` 目前覆盖的应答形态:正文 / 推理 / 工具调用 / usage / `stop|length|tool_calls` / 流内 error /
   HTTP 错误 / 流中途断 / 工具参数不全。表外还想加的(如 `reasoning_content` 与正文交替、多次 usage)
   按需再补,框架已经留好入口。
6. **本机跑全量时遇到过的两次异常,都不是本包的代码问题**(记录下来供 E 包接 CI 时留意):
   - 一次「**测试主机进程崩溃**」(输出尾部只有这一句、没有具体用例名,总计只跑到 36 条就中断):
     已由 lead 通报定位 —— 是 `win-guard` 清理自己那条挂死的 `dotnet test` 时**按进程名批量杀**
     (`testhost|vstest`),误杀了我这一次的 testhost/vstest(PID 42896,父进程 53832,路径就是本 worktree)。
     重跑即恢复;最近一次全量是 `1608 过 / 26 跳 / 0 败`,本包用例单跑 `14 过 / 15 跳 / 0 败` 稳定复现。
   - `TipCL.Tests.Core.ModDevTaskPersistenceTests.Records_AreCapped_AtomicallyWritten_AndABrokenFileIsIgnored`
     (`tests/TipCL.Tests/Core/ModDevAi3Tests.cs:322`,断言 `cache` 目录里没有 `*.tmp` 残渣)在全量跑法下
     偶发红(公布最终数字前 5 次全量里命中 2 次),单跑该类连跑 3 次全绿 —— 看着是 `ModDevTasks` 的后台任务
     与 `Persist()` 的原子替换抢时序,机器上有别的子代理在编译/测试时更容易露头。与本包无关,不归本包修。
7. 断言口径已按「别断言易变措辞」收紧:被拒/权限拒绝只断言 `ToolCallStatus`、`call.error` 非空与工具结果里的
   结构化错误码(`permission_denied`),不断言中文理由;`stop_reason` 一律走 `StopReason` 常量。
