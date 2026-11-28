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

### 1. UI 那一行「子任务进行中:<工具名>」——本轮**不做**(字段已就位)

- **缺什么**:界面不认子代理事件,现在子代理的进度会按普通步骤渲染(工具名直接出现在父回合的步骤流里)。
- **要改哪个文件**:`windos/src/TipCL/Pages/AiPage.cs` 的 `OnStatus(BridgeEvent ev)`(switch 里加一个分支,
  读 `ev.Payload.subagent`;文案「子任务进行中:」需要新增 i18n 词条,或改用现成词条)。
- **字段已经就位(可测的部分都做了)**:
  - `Agent.cs` 的 `RunSubagent(backend, settings, args, cancelled, onStatus)` 里包的 `childStatus`:
    转发子代理的 `_status` 时给 payload 追加 `subagent`(= 本次派发的会话 id,`<父id>-sub-<8位随机>`)与
    `subagent_task`(= 任务文本前 80 字);
  - 父回合调用点:`Agent.cs` 的 `_execute` 里 `RunSubagent(backend, settings, toolCall2.args, cancelled, _status)`。
- **为什么本轮不做**:这是「动到 AI 页面的改动」,任务书 §6 对这类改动的验收判据是
  「smoke exit 0、日志 0 WARN / 0 ERROR、**目视检查 PNG**」;而本轮 smoke 是明令禁止的(会弹 WPF 窗口抢用户前台),
  `ci-e` 也已核实 Windows 自检 `AiSelfTest.RunAll` 会 `new Window{}.Show()`,必须有交互桌面。
  **改完无法验证的 UI 改动比留着不做风险更大** —— 因此本轮只交付事件与字段。
- **验收时需要什么**:能开界面的环境 + `--smoke`(两版)+ PNG 目视检查;再加一条用例断言
  「子代理 status 事件带 `subagent` 字段且值与派发 id 一致」(有了回放框架后由 T 的框架覆盖)。

### 2. `ai_subagent_max_rounds` 没有界面项 —— 本轮**不做**,但功能已可用

- **功能状态**:可用。`Agent.SubagentMaxRounds` 先读本轮 `settings`,没有再读 `config.json`,
  所以在 `config.json` 里写 `"ai_subagent_max_rounds": 8` 立即生效;不写就是默认 4;`dispatch_subagent`
  的 `max_rounds` 参数可在硬上限 20 内覆盖(见 `AiSubagentTests` 的 6 条断言)。
- **缺什么**:「界面能看到 / 能改」。
  1. `windos/src/TipCL/Core/BackendApi.cs` 的 `_settings_full()` 白名单里加一行
     `pyDict["ai_subagent_max_rounds"] = Py.Int(Py.Or(G("ai_subagent_max_rounds"), 4L));`
     (该文件是 D6 属主,不在本包范围);
  2. `windos/src/TipCL/Pages/SettingsPage.cs` 的 AI 卡片加一个数字输入(或做成 `ai_max_rounds` 的同款)。
- **为什么本轮不做**:和上面第 1 条同一个理由(UI 面,smoke 禁用,无法目视验证)。
  而且**只加白名单不加界面控件**会出现「有个设置项但界面上改不了」的半成品状态,不如等能开界面时一起做。
- **验收时需要什么**:设置页能改、保存后 `config.json` 落盘、下一轮派发子代理时轮数按新值生效(各一条用例)+ smoke 目视。

### 3. `dispatch_subagent` 并行的安全性

依赖会话 id 唯一(已做)+ 每个子代理独立 `CompactState`(已做)。但 `Usage` / `Trace` / `Store.LogEvent`
仍是进程级静态字典:并发派发时用量按不同会话 id 各自累计(不会串),`Usage.Get(父会话)` 不含子代理用量 ——
与改造前一致;要不要合并统计到父会话,留给 O 包口径。

### 4. `truncated` 的判定口径

`stop_reason` 为 `truncated` 或 `max_rounds` 即 `truncated = true`(「输出/额度被截断」)。
如果后续想要「是否被压缩过」之类的语义,需要另加字段。

### 5. `Scheduler` 异常语义差异未改

research-r 指出 Windows 的 `RunGroups` 遇 faulted 直接抛(打断同组),Linux 收成工具错误载荷。
本包只加了「谁能进并行组」的判定,没动执行与异常语义;这条差异记在这里,不在本包顺手改。