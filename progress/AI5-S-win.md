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
| 3b | **UI 先只显示一行「子任务进行中:<工具名>」**(2026-11-28 用户解除窗口限制后补做) | `Pages/AiPage.cs` 的 `OnStatus` 开头加分支:带 `subagent` 字段的事件(子代理进度)**只更新状态行**为 `I18n.L("子任务进行中:{0}", ev.Name)` 并提前返回,不再把子任务内部的步骤铺进父回合的步骤流;其余状态处理一字未动。词条 `子任务进行中:{0}` 写进 `locales/zh_CN.json`(值同 key)与 `locales/en.json`(`Subtask running: {0}`),再用 `windos/tools/pack-core-resources.ps1 -Repo windos` 重新生成两份 `.gz`(没有手改 `.gz`) |
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
| `windos/src/TipCL/Pages/AiPage.cs` | `OnStatus` 加子代理分支(10 行,只碰这一处) |
| `windos/src/TipCL/Resources/locales/zh_CN.json` / `en.json` | 新增词条 `子任务进行中:{0}` |
| `windos/src/TipCL/Resources/locales/*.json.gz` | 由 `pack-core-resources.ps1 -Repo windos` 重新生成(未手改) |
| `windos/tests/TipCL.Tests/AiSubagentTests.cs` | 新增 8 条用例 |
| `windos/tests/TipCL.Tests/AiSubagentUiWiringTests.cs` | 新增 4 条:界面接线源码守卫 + 词条覆盖 + **运行时解析**(内嵌 `.gz` → `CoreResources` → `I18nCore.Tr` 真取到 "Subtask running: {0}")+ 字段名一致性 |

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
6. **UI 那一行(3b)**:界面分支由源码守卫 `OnStatusShowsASingleSubtaskLine` 钉住(认 `subagent` 字段 →
   在 `switch` 之前提前返回 → 状态行写 `I18n.L("子任务进行中:{0}", ev.Name)`);
   词条由 `SubtaskStatusTextIsTranslated`(两份词表都有、英文不是原样中文)+
   `SubtaskStatusTextResolvesAtRuntime`(运行时真的从内嵌 `.gz` 取到 `Subtask running: {0}`)覆盖。
   写完这条守卫第一次跑红了一次:断言英文值时把 key 自带的冒号当成了 JSON 的分隔冒号(取到的值成了 `": "`),
   改成从 key 之后找分隔冒号后转绿 —— 与上面一样,是真的「红 → 绿」。
7. **转绿**:`dotnet test --filter "FullyQualifiedName~AiSubagentTests"` → 8/8;
   `--filter "FullyQualifiedName~AiSubagentUiWiringTests"` → 4/4;
   全量 Windows 用例 **830 过 / 6 跳 / 0 败 / 总 836**。

> 第一次跑的时候这 8 条里有 2 条红过:我只加了 `SESSION_STATE_TOOLS` 常量、忘了把它接进
> `IsParallelizable`(红信息是「update_plan 不该进并行组」)。补上 IsParallelizable 的排除判断后转绿 ——
> 这就是「复现 → 修复 → 转绿」的真实顺序。

## 四、验收数字

- 构建 `-t:Rebuild`:0 error / 2 warning(与基线逐条相同)
- 测试:**830 过 / 6 跳 / 0 败 / 总 836**(基线 802;本包 12 条新用例:8 + 4)
- **smoke**(用户解除窗口限制后补跑,`TipCL.exe --smoke`):**exit 0**、日志 **0 WARN / 0 ERROR**、
  report 里 `issues: []`、`i18n_missing: []`、`state: finished`、`elapsed_ms ≈ 7.9 s`
- **PNG 目视**(`%LOCALAPPDATA%\TipCL\_wpf_smoke\run-20261128-160203\ai.png`):AI 页面与改动前一致 ——
  左侧对话列表、示例/实例选择、输入框、底部「当前「每次确认」…」文案都正常,没有布局错位、没有中文残留到英文界面。
  这一行只在**真的派发子代理**时出现,smoke 不发起模型调用,所以截图里看不到它(证据链靠上面三条用例)。
- 体积:compact 仍是 **5,771,264 B**(加一条词条 + 一个状态分支后字节数没变:PE 节对齐吸收掉了这点增量;
  `-t:Rebuild` 后逐字节可复现)。premium 见 `AI5-D.md`。
- i18n:新增 **1 条** key —— `子任务进行中:{0}`(zh_CN 值同 key / en `Subtask running: {0}`),
  `.gz` 已用 `pack-core-resources.ps1 -Repo windos` 重新生成,`-Check` 通过,`CoreResourcesPackTests` 全绿。

## 五、遗留问题

### 1. `ai_subagent_max_rounds` 没有界面项 —— 功能已可用,界面项仍不做

- **功能状态**:可用。`Agent.SubagentMaxRounds` 先读本轮 `settings`,没有再读 `config.json`,
  所以在 `config.json` 里写 `"ai_subagent_max_rounds": 8` 立即生效;不写就是默认 4;`dispatch_subagent`
  的 `max_rounds` 参数可在硬上限 20 内覆盖(见 `AiSubagentTests` 的 6 条断言)。
- **缺什么**:「界面能看到 / 能改」。
  1. `windos/src/TipCL/Core/BackendApi.cs` 的 `_settings_full()` 白名单里加一行
     `pyDict["ai_subagent_max_rounds"] = Py.Int(Py.Or(G("ai_subagent_max_rounds"), 4L));`
     (该文件是 D6 属主,不在本包范围);
  2. `windos/src/TipCL/Pages/SettingsPage.cs` 的 AI 卡片加一个数字输入(或做成 `ai_max_rounds` 的同款)。
- **为什么仍然不做**:这是**新功能**(加设置项 UI),不是任务书 S 包的必需项(§7 S 的任务 3 只要那一行状态显示);
  而且只加白名单不加界面控件会出现「有个设置项但界面上改不了」的半成品状态,不如等要做设置页时一起做。
  **本包没有动 `BackendApi.cs`**(已与 lead 确认)。
- **验收时需要什么**:设置页能改、保存后 `config.json` 落盘、下一轮派发子代理时轮数按新值生效(各一条用例)+ smoke 目视。

### 2. `dispatch_subagent` 并行的安全性

依赖会话 id 唯一(已做)+ 每个子代理独立 `CompactState`(已做)。但 `Usage` / `Trace` / `Store.LogEvent`
仍是进程级静态字典:并发派发时用量按不同会话 id 各自累计(不会串),`Usage.Get(父会话)` 不含子代理用量 ——
与改造前一致;要不要合并统计到父会话,留给 O 包口径。

### 3. `truncated` 的判定口径

`stop_reason` 为 `truncated` 或 `max_rounds` 即 `truncated = true`(「输出/额度被截断」)。
如果后续想要「是否被压缩过」之类的语义,需要另加字段。

### 4. `Scheduler` 异常语义差异未改

research-r 指出 Windows 的 `RunGroups` 遇 faulted 直接抛(打断同组),Linux 收成工具错误载荷。
本包只加了「谁能进并行组」的判定,没动执行与异常语义;这条差异记在这里,不在本包顺手改。