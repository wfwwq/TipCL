# AI5 合并与验收进度(第 3 版 · 13 个分支已合入并全绿)

- 记录时间:2026-11-28T15:30+08:00
- 执行者:AI5 合并与验收子代理(qa)
- 工作区:`C:\tipcl-fix`,分支 `ai5/agent-fixes`(基线 `b4cd7a2`,与 `main` 同点)
- 任务书:`C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`(567 行,**未被 git 跟踪**,全程未纳入任何提交)
- 状态:**13/14 已合入,双树全绿;只剩 `ai5/linux-loop` 未交付**

---

## 0. 结论速览

| 项 | 结果 |
|---|---|
| 已合入分支 | **13**(AI5-0、T-win、linux-tools、win-perm、win-rollback、ci-e、research-r、intent-i、observ-o、win-guard、win-agent、linux-memperm、T-linux) |
| 未合入 | **1**(`ai5/linux-loop`,0 提交、7 个文件在改) |
| 累计改动 | 113 文件 / +14,359 / −231,共 57 个提交(其中 14 个合并提交) |
| Linux 构建 | **0 error / 1 warning**(既有 `LayoutEditor.cs:196` CS0649) |
| Linux 测试 | **失败 0 / 通过 1665 / 跳过 26 / 总计 1691** |
| Windows 构建 | **0 error / 2 warning**(既有 `BridgeMutationAndLaunchGuardTests.cs:34-35` xUnit1031) |
| Windows 测试 | **失败 0 / 通过 1012 / 跳过 8 / 总计 1020** |
| compact 体积 | **5,796,352 B**(基线 5,759,488,本轮净增 **36,864 B**;超 D-19 红线 **39,088 B**,按 D-87 裁决只记录不回收) |
| 红线 | 未改版本号(仍 1.3.0)、未发 release、未 push、未跑任何弹窗/陶瓦/防火墙测试 |

---

## 1. 合并清单(按依赖顺序)

合并方式:`git merge --no-ff`,**每个包保留自己的提交粒度**,不压缩;每包一个合并提交以便回溯。

| # | 分支 | 包内容 | 并入提交 | 包内提交数 |
|---|---|---|---|---|
| 1 | `ai5/ai5-0-env` | AI5-0 环境核实与双树基线 + D-87 | `08226ab`、`1fbf3c5`(新提交补合) | 2 |
| 2 | `ai5/t-win` | T Windows 回放框架 + 21 条场景 | `3b590fb` | 1 |
| 3 | `ai5/linux-tools` | C1 按需加载改扫最近 12 条 + C3 数据报告 | `3aa6236` | 2 |
| 4 | `ai5/win-perm` | P1-win(不询问放行)+ B2-win + G 插件权限 | `9ec23c3` | 4 |
| 5 | `ai5/win-rollback` | D3 回滚覆盖 + D4 预览覆盖 | `2b0369a` | 3 |
| 6 | `ai5/ci-e` | E 自检接进两条 CI | `4dd84ba` | 2 |
| 7 | `ai5/research-r` | R 两版差异清单 `docs/ai-agent-parity.md` | `20070ee` | 1 |
| 8 | `ai5/intent-i` | I 评测集数据(63 + 60 条) | `0e0bfd0` | 1 |
| 9 | `ai5/observ-o` | O 会话级可观测性汇总 + 诊断导出 | `d0fd18f` | 2 |
| 10 | `ai5/win-guard` | D1 fs_write 守卫 + D2 权限面板 + D6 确认卡超时 | `4586b24` | 4 |
| 11 | `ai5/win-agent` | D7/D8/D9 + S-win + P2 + I 接线 | `eb9f3ba` | 5 |
| 12 | `ai5/linux-memperm` | B1-B4 + P1-linux + C2 来源前缀 + 前置修复 | `f3295e2` | 9 |
| 13 | `ai5/t-linux` | T Linux 回放框架 + Client 注入点 + 28 条场景 | `9d7b112` | 2 |

整合方自己的提交(不在任何包内):

| 提交 | 说明 |
|---|---|
| `090f08c` | `ai(agent): AI5-D6 i18n 收口` —— 补 win-guard 留下的 1 条词条并重生成 `locales/*.gz` |
| `af37a97` | `test(agent): AI5-T 6/12b/21/22a 去掉 Skip 并转绿;12a 会话 id 断言跟随 S 包唯一 id` |
| `f9f206d` | `test(agent): AI5-T 场景 9b 去掉 Skip 并转绿(D8 已合入)` |
| `5a80f2e`、`019bb19` | 本文档第 1、2 版 |
| 本次 | 本文档第 3 版 |

### 1.1 预检(合并前逐一核过,全部干净)

- 无任何分支修改版本号、`update.json` / `update-linux.json`、`.github/workflows/release.yml`、`.csproj` / `.sln` / `.props`(ci-e 只改 `ci.yml` / `ci-windows.yml`,属 E 包范围)。
- 任务书目录 `windos/docs/改造工程:2611/` 与 `ai5-0-env` 的 `.ai5-logs/` **均未入库**(`git ls-files` 零命中)。
- 版本号实测仍为 `1.3.0`。

---

## 2. 冲突与解冲突记录

### 2.1 Windows `locales/{en,zh_CN}.json`(win-perm vs win-rollback)—— 文本自动合并 + 逐词条核验

git 文本自动合并成功,**没有整文件取一边**。按「双方各自相对基线新增的 key 是否都在合并结果里」逐条核验:

| 文件 | 基线 key | win-perm 新增 | win-rollback 新增 | 合并后 | 缺失 | 同 key 值冲突 |
|---|---|---|---|---|---|---|
| `en.json` | 3310 | 2 | 34 | **3346** | **0** | **0** |
| `zh_CN.json` | 3310 | 2 | 34 | **3346** | **0** | **0** |

2 + 34 = 36,与 3346 − 3310 完全吻合 → 双方词条都在,**零丢失**。

### 2.2 Windows `locales/*.json.gz`(二进制冲突 UU)—— 不取任何一边,重新生成

`.gz` 是二进制,git 报 `CONFLICT (content)`。按既定策略**没有用任何一边的版本**,而是用 `windos/tools/pack-core-resources.ps1` 从合并后的 `.json` 重新生成,并用 `GZipStream` 解压后与源文件**逐字节比对**:

| 文件 | 源 `.json` | 生成 `.gz` | 解压后 == 源 |
|---|---|---|---|
| `en.json` | 226,406 B | 80,157 B | **是** |
| `zh_CN.json` | 218,271 B | 51,261 B | **是** |

### 2.3 win-guard 的 i18n 遗留(整合方补齐)

win-guard 新增了 1 条可见文案 key 但**故意没动词表**(留 i18n 收口方统一重跑)。按指派补进两侧:

- key:`等你确认超过 {0} 秒,这一次已按拒绝处理`(`BackendApi.cs:72` 的 `I18nCore.Tr(...)` 使用)
- `zh_CN.json`:同中文;`en.json`:`No confirmation after {0} seconds; this action was denied.`
- 补完后**再重跑一次** `pack-core-resources.ps1` 并用 `GZipStream` 逐字节验证;两侧 key 数 3347 = 3347,无孤儿。
- 提交为 `090f08c`。

### 2.4 Windows `Agent.cs`(win-perm 4 段 × win-agent 8 处 × observ-o 3 行)—— 自动合并 + 逐行核验

三包都改了 `Agent.cs`(win-perm 改 4 段:569-573 / 1134-1164 / 1260-1265 / 1335;win-agent 改 D9、`_compact_state_for`、`EstimateNewMessages`、兜底、P2 审批、`RunSubagent`;observ-o 加 3 行)。git **全部自动合并成功**。用「该分支相对基线新增的行,在合并结果中是否存在」逐行核验:

| 分支 | 新增非空行 | 合并后缺失 |
|---|---|---|
| `ai5/win-perm` | 36 | **0** |
| `ai5/observ-o` | 3 | **0** |
| `ai5/win-agent` | 415 | 1 |

唯一的「缺失」行是:

```
text16 = "[来源: 插件工具 " + (PluginTools.Get(item13.name)?.Plugin ?? "") + "]";
```

**查证结论:这是 win-perm 有意替换掉的旧版来源标签,不是丢失。** 合并结果保留的是 win-perm 的增强版(`Agent.cs:1568`):

```
text16 = "[来源: 插件工具 " + ... + (PluginAgentBridge.IsUntrustedSource(item13.name) ? ",内容不可信]" : "]");
```

win-agent 分支上是旧版,因为它从 `b4cd7a2` 出发、看不到 win-perm 的 G 包改动。**保留 win-perm 版本 = 保留双方语义。**

另核 win-perm 的 4 段锚点在合并后仍在:`ai_permission_dont_ask` 1 处、`PluginAgentBridge.AffectedPathsOf` 1 处、`内容不可信` 3 处、`Permission.Decide` 2 处。win-agent 的 `AiAgentSourceGuardTests`(「Agent 里不再有第二处判断」)在合并后**全绿**。

**P2 × G 的交互已实测闭环**:`PluginToolSideEffectTests.DeclaredWriteToolIsDeniedInPlanMode`(plan 档对声明 `write_local` 的插件工具判 deny)+ `AiAgentSourceGuardTests`(plan_approval 必须早于 `Scheduler.RunGroups`,且驳回后本轮不执行任何工具)两条都在,定向跑 **39 条全绿**(`失败 0 / 通过 39`)。场景 06(计划驳回 → 零工具执行、零检查点)也已转绿。

### 2.5 真实集成失败一条:`Scenario12a_子代理写工具被拦截`(已定位并修正)

第 2 轮(合入 win-agent 后)Windows 出现 1 条失败。**单跑同样失败,不是偶发**,是真实集成冲突。

- 失败断言:`Assert.NotEmpty() Failure: Collection was empty`(该文件有两处 `NotEmpty`,无 PDB 无法从堆栈区分)。
- 排查手法:写**临时**诊断用例(跑完即删,未提交)复算全部断言并 dump trace / 请求序列,实测:

| 断言 | 复算结果 |
|---|---|
| L65 `Requests[1]` 无 `fs_write` | 通过 |
| L66 `Requests[2]` 无 `fs_write` | 通过 |
| L67 `Requests[3]` 结果含「子代理只读」 | 通过 |
| L68 `TraceEventsOf("subagent_write_blocked")` 非空 | **通过(count=1,事件确实发出)** |
| L72 `StoreEvents(ChatId + "-sub")` 非空 | **失败(count=0)** ← 真正的原因 |

- 根因:该断言沿用 S 包**之前**的固定会话 id `<父id>-sub`;而任务书 §S.1 明确要求改成 `<父id>-sub-<短随机id>`(win-agent 已实现,`Agent.cs:2130-2135`)。实测新 id 前缀下会话文件命中 1 个 → **实现正确,是用例过期**。
- 修正(最小改动、保留语义「子代理有自己的会话记录」):

```csharp
string[] subSessions = Directory.GetFiles(Path.Combine(h.Root, "cache", "ai_sessions"), h.ChatId + "-sub-*.jsonl");
Assert.Single(subSessions);
Assert.NotEmpty(h.ReadJsonl(subSessions[0]));
```

- 同时把 4 条「转绿」场景的 Skip 去掉(对应修复包均已合入):`Scenario06`(P2/win-agent)、`Scenario12b`(D8/win-agent)、`Scenario21`(S/win-agent)、`Scenario22a`(P1/win-perm)。该文件 6 条全部转绿,Windows 跳过数 **13 → 9**。
- 随后又按 DoD「场景表不留残留 skip」核对其余 T 用例,发现 `AiReplayLoopTests.Scenario09b`(对应 D8)修复包已合入却仍挂着 Skip,去掉后**单独验证通过**(`失败 0 / 通过 1`),提交 `f9f206d`;Windows 跳过数 **9 → 8**。
- 以上改动提交为 `af37a97` / `f9f206d`,**属整合方对交付用例的改动,请复核**(尤其 `win-agent` 与 `t-win` 双方)。

---

## 3. 验证结果(三轮完整跑)

命令一律按任务书 §6,并加 `$env:DOTNET_ROOT='C:\Program Files\dotnet'`;Windows 用 **`-t:Rebuild`**(`Copy-Item` 时间戳坑会让 MSBuild 静默跳编,造成假红/假绿)。

| 轮次 | 已合入 | Linux 测试 | Windows 测试 |
|---|---|---|---|
| 第 1 轮 | 11 个 | 失败 1 / 通过 1606 / 跳 11 / 总 1618 | 失败 0 / 通过 977 / 跳 13 / 总 990 |
| 第 2 轮 | +win-agent(12) | 失败 0 / 通过 1607 / 跳 11 / 总 1618 | 失败 1 / 通过 1006 / 跳 13 / 总 1020 |
| 第 3 轮 | +linux-memperm(13)+用例修正 | 失败 0 / 通过 1652 / 跳 11 / 总 1663 | **失败 0 / 通过 1011 / 跳 9 / 总 1020** |
| 第 4 轮 | +t-linux(合计 13 个分支已合) | **失败 0 / 通过 1665 / 跳 26 / 总 1691** | 未变(Windows 侧无改动) |
| 第 5 轮 | +场景 9b 去 Skip | 未变(Windows 侧改动) | **失败 0 / 通过 1012 / 跳 8 / 总 1020** |

构建:

| 项 | 结果 |
|---|---|
| Linux Release 构建 | **0 error / 1 warning**(`src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649,既有) |
| Windows Release 构建(`-t:Rebuild`) | **0 error / 2 warning**(`windos/tests/TipCL.Tests/BridgeMutationAndLaunchGuardTests.cs:34-35` xUnit1031,既有) |

### 3.1 测试条数核对(逐包相加 == 实测,无回退)

Windows 基线 802,新增:win-perm +84、win-guard +36、t-win +35、win-agent +30、win-rollback +27、observ-o +6 → 合计 +218 → **1020 = 实测 1020** ✓
Linux 基线 1605,新增:C1(linux-tools)+7、observ-o +6、linux-memperm +45、t-linux +28 → 合计 +86 → **1691 = 实测 1691** ✓

### 3.2 剩余的 Skip 逐条交代(都不是本包遗漏)

**Windows 剩 8 条:**

| 用例 | 原因 | 判定 |
|---|---|---|
| `AiReplayRoutingTests.Scenario16c` | 要真的查 Modrinth/CurseForge,红线禁止本机跑真实网络;路由已由 16a/16b 覆盖 | 合理,范围外 |
| `AiReplayRoutingTests.Scenario18a` | 「好卡」命不中关键词路由(`ExtraTools.Keywords` 只有「很卡/卡顿」),**无对应包号**,建议并入 C3 | 合理,建议单列 |
| `LayoutCanvasTests.*` ×6 | 需真实界面/画布环境,与 AI5 无关的既有挂起项 | 既有 |

Windows 的 T 场景已经**零遗留转绿**:本轮去掉 Skip 并验证转绿 5 条 —— `Scenario06`(P2)、`Scenario12b`(D8)、`Scenario09b`(D8)、`Scenario21`(S)、`Scenario22a`(P1);`Scenario12a` 修正后也绿。

**Linux 剩 26 条:**

| 用例 | 数量 | 原因 | 判定 |
|---|---|---|---|
| `Ai5TPendingScenarioTests.*` | 15 | 等 A1-A8 + S Linux + C2 重发(即 `linux-loop`) | **待转绿**,移交 linux-loop |
| `Ai5TScenarioTests.场景02` | 1 | 「转绿:L→A6」——Linux 侧 `COMPLETED` 从未被赋值,现在给的是 `no_tool_call` | **待转绿**(A6) |
| `Ai5TScenarioTests.场景06补` | 1 | 无转绿包号,t-linux 已在 `progress/AI5-T.md` 记为超出任务书的发现 | 范围外 |
| `CliSmokeTests.*` ×5 / `LayoutGridCanvasTests.*` ×4 / `LayoutMotionAndHeightTests` ×1 / `SmokeIntegrationTests` ×1 | 12 | 需真实界面或冒烟环境(本轮禁用) | 既有 |

⚠ **注意**:Linux 侧「现状」组里有 2 条被挂起(`场景02`、`场景06补`),说明 Linux 主循环现状在这两点上**确实不满足**任务书 §7「现状场景全绿」的要求 —— 前者由 A6 修,后者超出本书范围。`linux-loop` 合入后需复核。

---

## 4. 体积(如实记录,不回收,不判合格/不合格)

| 阶段 | compact `TipCL.exe` | 相对上一阶段 |
|---|---|---|
| 基线 `b4cd7a2` | **5,759,488 B** | D-19 红线 5,757,264 → 已超 **+2,224 B**(既有,D-87 已裁决记录) |
| 13 个包合入后 | **5,796,352 B** | **+36,864 B** |

- 本轮净增 36,864 B,**不等于**各包自报增量之和(win-perm +2,048、win-rollback +10,752、t-win +512、observ-o +8,704、win-guard +4,608、win-agent +11,776 = 38,400):因为 `locales/*.gz` 是按**合并后的**内容重新压缩的,不是各版本 `.gz` 相加,Costura 打包也有再压缩效应。**以实测 5,796,352 B 为准。**
- 超红线 **39,088 B**,按 D-87「如实记录、不回收、不声称合格/不合格」执行,未做任何体积回收改动。
- 根因线索与 D-38 旧记录的关系见 §6(第 2 版 §3.2 已详述)。

---

## 5. 未合入 / 待完成

### 5.1 `ai5/linux-loop`(A1-A8 + S Linux + C2 重发)—— 唯一未交付

最后侦察(15:27):**0 提交**、7 个文件在改(`AiAgent.cs`、`Config.cs`、`locales/{en,zh_CN}.json`、`AiRetryTests.cs`、`AiSchedulerTests.cs`、新增 `AiAgentLoopTests.cs`)。交付后需:

1. 与 `t-linux` 的 28 条场景对接(其中 15 条 pending 是等 A1-A8 转绿的);
2. **Linux `locales` 三人冲突**(linux-loop × linux-memperm × 已合并的 t-linux 无关但同文件区域):按既定策略文本合并 → Linux 侧**没有 `.gz`**,不需重跑打包脚本(实测 `src/TipCL.Core/Resources/locales/` 下只有 `.json`);
3. 与 `observ-o` 都改 `AiAgent.cs` / `ToolRunner.cs` → 需逐行核验双方改动都在;
4. 与 `Client.cs`(t-linux 刚加的注入点)可能冲突(A5 兜底)。

### 5.2 **悬空接口:C2 生产侧未接线**(重点,不算通过)

`linux-memperm` 做了 C2 的判据与工具函数,但**最后一根接线不在它手里**(属 `linux-loop` 的 `AiAgent.cs`)。实测:

| 符号 | 定义 | 生产调用者 |
|---|---|---|
| `ToolSource.ForModel(...)` | `src/TipCL.Core/Launcher/Ai/ToolSource.cs` | **无**(只有 `AiToolSourceTests.cs` 与一处文档注释示例) |
| `ToolRunner.IsUnknownTool(call)` | `ToolRunner.cs:437` | **无**(仅注释提及) |

即 Linux 侧工具结果**尚未真正加来源前缀**,C2 的全量重发也尚未接入。等 `linux-loop` 交付后核对:是否在 `AiAgent.RunAsync` 回填 tool 消息处(约 `AiAgent.cs:243-247`)调了 `ToolSource.ForModel`,以及 C2 全量重发是否复用 `IsUnknownTool(call)` 这**同一个判据**(两边别各写一套)。

---

## 6. 遗留与已知问题(汇总,均不判为「未完成」)

1. **`ModDevTaskPersistenceTests.Records_AreCapped_AtomicallyWritten_AndABrokenFileIsIgnored` 偶发红** —— 第 1 轮出现 1 次,第 2/3/4 轮全绿;单跑该类 3/3 通过。断言是 `Assert.Empty(Directory.GetFiles(cache, "*.tmp"))`(临时文件残渣),该测试类**已挂在串行集合 `GlobalStateCollection` 上**(`ModDevAi3Tests.cs:236`),合并**未碰任何 ModDev 文件**(`git diff --name-only b4cd7a2 HEAD | grep ModDev` 为空)。判定:**与本次合并无关的计时敏感偶发**,与同机 15 个 agent 争用 CPU 有关。已列入「已知不稳定项」,建议另立 flake 治理任务。
2. **UI 两处延后**(win-agent 的 S「子任务进行中:工具名」、I 的非 `no_tool_call` 回合提示)—— 要改 `Pages/AiPage.cs`,而改 AI 页面必须 smoke + 目视 PNG,本轮禁用(WPF 会弹窗抢前台)。字段已带上(`subagent` / `claim_unmatched`)。
3. **`ai_subagent_max_rounds` 未进 `BackendApi._settings_full()` 白名单** —— 功能可用(settings → config.json 双读),缺的是界面项,与第 2 条同理延后。
4. **`WantsAction` 全树零调用者**,不接线(intent-i、research-r 独立发现一致)。
5. **Linux 「不询问」仍只能改 `config.json`**,面板只给五档 + 一行说明,没加控件 —— 与第 2 条同因。
6. **`launch_game` 这类在 `dontAsk` 下也放行**(裁决只豁免删除类)—— 已上报用户,等裁决。
7. **记忆上限 200 → 80** —— 已上报用户,等裁决。
8. **`ai_max_rounds` 默认值 Linux 侧由 12 变 20**(A7)—— 有意的行为变化,需在 `docs/DECISIONS.md` 留痕。
9. **`t-linux` 有未提交的在写改动**(`progress/AI5-T.md`、`tests/TipCL.Tests/Ai5T/Ai5TPendingScenarioTests.cs` 共 +76/−19)—— 我合的是它的**已提交**状态;若它继续提交,需再补合一次。

---

## 7. 待裁决

1. **体积项**(第 2 版 §3.2 详述,已在 D-87 裁决):基线 5,759,488 B 超 D-19 红线 2,224 B,合完 13 包后 5,796,352 B(超 39,088 B)。本轮按「如实记录、不回收」执行;DoD §10「compact 体积检查通过」在本轮**不成立**,需用户知晓并在最终验收口径里明确。
2. **`launch_game` 在 `dontAsk` 档下放行**(见 §6.6)。
3. **Linux 记忆上限 200 → 80**(见 §6.7)。
4. **`Scenario12a` 用例的整合方改动**(§2.5)需 `win-agent` / `t-win` 双方确认「唯一会话 id」口径无误。

---

## 8. 红线自查

| 红线 | 状态 |
|---|---|
| 禁改版本号 | ✅ 仍 `1.3.0`(两侧 `update*.json` 未动) |
| 禁发 release | ✅ 未动 `release.yml`,未打 tag |
| 禁 git push | ✅ 全程只在 `C:\tipcl-fix` 本地提交,**任何分支都未推送** |
| Windows TFM net48 / 不新增 NuGet | ✅ 未改任何 `.csproj`(win-agent 的 `TipCL.Tests.csproj` 只加 `Data\*.jsonl` 的 `None Include`,属测试工程;已确认**只出现 1 次,未因 intent-i 重复**) |
| 不写业务代码 | ✅ 只做集成;唯一代码侧改动是 §2.5 的**测试用例**修正与 §2.3 的**词条补齐**(两者均已在本文档标出待复核) |
| 不抢前台 | ✅ 未跑 `--smoke`、`tools/verify-smoke.ps1`、ClickSweep、VisualTour、任何 WPF/浏览器启动;测试统一带 `FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl` |
| 不按进程名批量杀进程 | ✅ 未杀任何进程 |
| ISO 8601 日期 / 中文 | ✅ |

---

## 9. 环境与命令备忘(第 2 版 §3.1 的延续)

```powershell
# 必做:testhost 靠 DOTNET_ROOT 找运行时,用户级变量指错了(C:\Users\Administrator\.dotnet 无 host\fxr)
$env:DOTNET_ROOT='C:\Program Files\dotnet'

# Linux 树
dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1
dotnet test  tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"

# Windows 树(必须 -t:Rebuild)
dotnet build windos\TipCL.sln -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
dotnet test  windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"

# locales 打包(Windows 树;Linux 侧无 .gz,不需要)
& windos\tools\pack-core-resources.ps1            # 生成
& windos\tools\pack-core-resources.ps1 -Check     # 校验
```

- Windows 树**不需要** net48 Developer Pack:参照程序集来自 `TipCL.csproj:53` 的 `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3。
- `locales/*.gz` 生成后**必跑** `CoreResourcesPackTests`(在 Windows 测试套件里);本轮两次重生成后均绿。

---

## 10. 版本记录

| 版本 | 时间 | 内容 |
|---|---|---|
| 第 1 版 | 2026-11-28T14:25+08:00 | 侦察:14/14 分支 0 提交;testhost 根因定位并验证;当时误判 Windows 树不可编译 |
| 第 2 版 | 2026-11-28T14:30+08:00 | 更正 Windows 树可本机编译/测试;体积口径核实(基线已超线 2,224 B) |
| **第 3 版** | 2026-11-28T15:30+08:00 | **13 个分支合入并双树全绿**;locales/.gz/Agent.cs 冲突解法与逐行核验;`Scenario12a` 定位与修正;5 条 T 场景转绿(06/09b/12b/21/22a);体积实测 5,796,352 B;记录悬空接口 C2 与 9 条遗留;Linux 侧 26 条 Skip 逐条交代 |
| 第 4 版(补记) | 2026-11-28T16:10+08:00 | 第二轮新提交合入(linux-tools C3 探针、win-guard 卡关键词、t-linux 场景11、linux-memperm D-88、linux-loop A1-A8);解除抢前台限制后补齐 smoke / ai-ui-selftest / verify-smoke;802 vs 809 查清;Linux 场景 13 绿 / 16 红逐条定性 |

---

## 11. 第二轮补充(2026-11-28T16:10+08:00)

### 11.1 本轮新合入的分支与提交

| 分支 | 新提交 | 并入提交 | 冲突 |
|---|---|---|---|
| `ai5/linux-tools` | `f488466` C3-1b 补关键词 + `471485e` 探针入库 | 合并提交 | 无 |
| `ai5/win-guard` | `0697a81` Windows 补「卡」关键词 | 合并提交 | 无 |
| `ai5/t-linux` | `ce9cdff` 场景 11 改用注册表外名字 | 合并提交 | 无 |
| `ai5/linux-memperm` | `e2248dd` B1 迁移诊断 + `d499670` D-88 上限 200 | 合并提交 | **`docs/DECISIONS.md`** |
| `ai5/linux-loop` | `1af591b` A1-A8 + S-linux + C2 重发 | `d7ee0b2` | **`AiAgent.cs` + Linux `locales/{en,zh_CN}.json`** |

### 11.2 三处冲突的解法

1. **`docs/DECISIONS.md`(D-88)** —— 按编号保留 **D-86(1320 行)/ D-87(1334 行)/ D-88(1347 行)**,三条齐全、编号连续、内容完整;只删了三个冲突标记,没有整段取一边。
2. **Linux `locales/{en,zh_CN}.json`** —— 双方各在文件末尾追加一批(linux-memperm 9 条、linux-loop 10 条),取**并集**:3685 → **3695**,en/zh_CN key 集合完全相等(3695=3695,零孤儿),双方新增 key 零缺失、零同 key 值冲突。Linux 侧**无 `.gz`**,故无需重跑打包脚本。
3. **`src/TipCL.Core/Launcher/Ai/AiAgent.cs`** —— observ-o(AI5-O 记账)与 linux-loop(A1-A8 重写)三方冲突 3 处:
   - hunk1 取**并集**:保留 observ-o 的 `LogTurnSummary` 局部函数 + linux-loop 的终态状态变量;丢掉只服务于旧错误路径的 `failReason`(linux-loop 改用 `terminalError`,留着会成为无用变量警告)。
   - hunk2/hunk3 取 **linux-loop 的控制流**(A6 续写/COMPLETED 口径、A7 收尾轮),丢弃 HEAD 的两个旧 `return` 出口。
   - **关键接线**:A1 把终态收敛到唯一出口后,observ-o 原先散在各 return 点的 `LogTurnSummary(...)` 调用全部随之消失。合并方在唯一出口(`Store.LogEvent(TurnCompleted/TurnFailed)` 之后)**补回一次** `LogTurnSummary(reason, RoundsUsed)`,保证 AI5-O「一轮恰好一条 TurnSummary」不丢失。
   - 核验:双方语义锚点齐全(AI5-O/TurnSummary/toolCalls + AI5-A1/A5/A6/A7/A8/FallbackOnceAsync/_continuations);observ-o 在 `ToolRunner.cs`/`Trace.cs`/`AiPage.cs`/`AiSessionMetrics.cs` 的新增行**零缺失**;编译 0 error / 仅既有 1 warning。

### 11.3 解除「不许抢前台」后的验收(全部实跑)

| 项 | 结果 |
|---|---|
| Windows `--smoke`(compact) | **exit 0**,10 页全 ok,**0 WARN / 0 ERROR**,17 张 PNG 已目视(AI 页/下载页正常渲染) |
| Windows `--smoke`(premium) | **exit 0**,10 页、**0 issues**;启动页 hero 图正常 |
| Windows `--ai-ui-selftest` | **exit 0,32 条 PASS / 0 FAIL,`ALL PASSED`**,6 张截图(AI5-0 一直缺的这一项,现已取到) |
| Linux `tools/verify-smoke.ps1` | **exit 0**,A/B1/B2 三批 × 8 帧 = **24 帧全 OK,每帧 0 WARN / 0 ERROR**;已目视 `ai/smoke.png`(含 observ-o 的「导出诊断报告」入口) |
| Linux 全量(无过滤) | 失败 **1** / 通过 1701 / 跳过 27 / 总 1729 —— 唯一失败是 **i18n 基线守门**(见 §11.5) |
| Windows 全量(无过滤) | **失败 0 / 通过 1029 / 跳过 8 / 总 1037** |

### 11.4 802 vs 809 已查清

- `OpenUrlGateTests` **恰好 7 条**,正是标准排除过滤器 `FullyQualifiedName!~OpenUrl` 挡掉的那批。
- 因此:**802 = 套过滤器的口径,809 = 不套过滤器的口径**,同一棵树、同一份用例,差值精确等于 7(通过数 796 + 7 = 803 也对上)。
- 结论:**AI5-0 的 802 没有记错**,`linux-memperm` 的 809 是没套过滤器。建议在 `progress/AI5-0-baseline.md` 注明「该数字含排除过滤器」,不必改数字。合并后的对应值:过滤器口径 1030(1022 过 / 8 跳)、无过滤器 1037(1029 过 / 8 跳)。

### 11.5 i18n:Linux 唯一红,已定位到机制

- 权威口径(`python tools/i18n-audit.py`):wrapped **2615 → 2813(+198)**、unwrapped **3211 → 3229(+18)**。
- 审计只认 `WRAPPERS = {"Tr"}`;`linux-loop` 新加了文件内别名 `AiAgent.cs:141 T(string zh) => I18nCore.Tr(zh)`,于是 **11 条实际已接线的用户可见文案被算成未接线**(审计假阴性)。全树 `T("` 调用共 11 处,**全部**在 `AiAgent.cs`,无其他同名校验函数 → 把 `T` 加进 `WRAPPERS` 无误报风险。
- `AiAgent.cs` 的 49 条未接线拆开:11 条 = 上述假阴性 / 34 条 = 基线既有 / **4 条真新增**,且这 4 条**都不该翻译**:`L129 请从断开处继续,不要重复。`(发给模型的续写系统提示)、`L836 工具参数不完整`、`L837 接口超时`(与 `Client.AssembleStream` 的错误文案做**匹配锚点**,翻译即失效)、`L984 上游负载`(上游错误文案分类关键词)。
- 待裁决(合并方未擅自改):(a) 把 `T` 加进 `tools/i18n-audit.py` 的 `WRAPPERS`(工具侧一行,+11 条转正,余 **+7**);(b) 给 4 条锚点/系统提示加 `i18n:ignore` 标注;(c) 二者做完再按脚本提示重记基线。**在此之前 Linux 全量测试保持 1 条红。**

### 11.6 Linux 场景转绿结果(15 条逐条试,13 绿 / 16 红)

13 条已去掉 Skip 并转绿:`场景01`(现状)、`场景02`(A6)、`场景06`(P2 等价)、`场景06补`(拒绝即无副作用)、`场景07`(A4)、`场景10`(A2+A6)、`场景11`(C2)、`场景12补`(A5)、`场景13`(取消)、`场景15补`(现状)、`场景19`、`场景19补`(C1)、`场景20`(B1)。

16 条仍红、已按 lead 口径**恢复 Skip 并写明原因**:

| 原因类别 | 场景 | 说明 |
|---|---|---|
| A6 有意改口径(`no_tool_call`→`completed`) | 02补、03、04、11补、16、17、18、21、22(9 条) | 实现正确、断言过期;其中 **03/04/16/17/18 合并前是绿的**,属 A6 落地后的口径变化 |
| A1 有意改口径(`stream_failed`→`cancelled`) | 13补 | 同上 |
| 疑似真缺陷 | **14**(收尾轮文本为空,期望有收尾文本)、**15**(A8 未切到 `ai_fallback_model`,实得 `deepseek-v4-flash`)、**05**(B3 作用域断言期望 `allow`、实得空串) | 建议退回 `linux-loop` / `linux-memperm` 复核 |
| 口径待裁决 | 08(`\n\n` 拼接)、09(兜底把流式残段与兜底文本拼接) | 用例与实现的文本拼接策略不一致 |
| 待核实 | 12(子代理工具表出现 `install_mod`) | 用例期望只有 `dispatch_subagent` |

`场景06`(plan 审批)按指示**保持不转绿**:`research-r` 已核实 Linux 无计划审批实现,任务书该条标的是「W(🕓)」。

### 11.7 体积(如实记录,不回收)

| 构建 | 字节 | 说明 |
|---|---|---|
| compact(合并后) | **5,796,352 B** | 基线 5,759,488 → 本轮净增 36,864;超 D-19 红线 39,088 |
| premium(合并后) | **5,816,320 B** | 比 compact +19,968,与 D-38 记录的差值完全一致 |

### 11.8 仍待处理

1. **`ToolSource.ForModel` 待接线**(`AiAgent.RunAsync` 回填 tool 消息处,约 `AiAgent.cs:243-247`)—— 归属 `linux-loop`,合并方**未动**,验收时不得判为已完成。
2. i18n 守门 1 条红(§11.5),等裁决。
3. 16 条场景 Skip(§11.6),其中 3 条疑似真缺陷需退回。
4. `t-linux` 仍有未提交改动(若它继续提交需再补合)。
