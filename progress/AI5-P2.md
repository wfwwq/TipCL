# AI5-P2(计划审批改到执行之前,Windows)

- 日期:2026-11-28
- 分支与工作区:`ai5/win-agent` @ `C:\tipcl-ai5\win-agent`(基线 `b4cd7a2`)
- 性质:**有意行为变化**(任务书 §3 裁决 2),单独成一组提交;不动版本号、不发 release

## 一、做了什么

### 现状(证据)

`Agent.cs` 原 L1230-1266:
```
turn.Transition("executing_tools");
List<List<ToolCall>> list13 = Scheduler.Group(…allow…);
Scheduler.RunGroups(list13, _execute);          // ← 先把这一轮的工具都跑完
…
if (text3 == "plan" && planItems.Count > 0 && planTurnId == turn.id && !planApproved)
{ …弹 plan_approval…; 驳回 → return "计划未获批准"; }   // ← 之后再问
```
`planItems` 是 `_execute` 里执行 `update_plan` 时才赋值的,所以这段审批**必然**发生在
「本轮所有工具(包括写工具)已经执行完、检查点已经生成」之后。驳回只能拦住后续回合,收不回已经落盘的东西。

### 修法

1. **审批前移到执行之前**(现 L1480-1531):`turn.Transition("executing_tools")` 之后、`Scheduler.Group` / `RunGroups` 之前,
   先在本轮调用里找 `update_plan` 且**被权限判定放行**的那一个(`planCall`)。
   - `text3 == "plan"` 且找到 → 用它的 `items`(过滤掉空 title)调 `plan_approval` 确认;
   - 批准 → `planApproved = true`,计划先落到 `planItems` / `planTurnId`(界面能看到计划),然后照常分组执行;
   - 驳回 → 本轮**一个工具都不执行**:把要跑的调用统一置成 `denied` + 结果「计划未获批准」(复用现成词条
     `计划未获批准,本轮到此为止`)、给插件发 `tool/result`、把结果回填成 tool 消息、`Trace.Record("plan_rejected")`、
     回合收尾返回「计划未获批准,本轮到此为止」。
2. **删掉执行之后那段旧审批**(原 L1247-1266 整段),执行完直接 `turn.Transition("awaiting_model")`。
3. **检查点**:`Checkpoint.Snapshot` 只出现在 `_execute` 里;驳回分支根本不进 `_execute` ⇒ 不可能生成检查点。
4. **插件事件序列**:`AssistantMessage` 仍在执行任何工具之前发出;`ToolStart` / `ToolResult` 只对真正执行的调用出现;
   被驳回的调用发一条 `ToolResult(isError: true)`(与权限拒绝时的形状一致),之后不再有 `tool/start`。
   `plan_approval` 仍是 `onStatus` 事件(不是插件事件),位置从「执行后」挪到「执行前」。

### 任务书 §7 P2 的 🕓:「先弄清 plan 档下写工具是从哪来的」

**结论:不是经 D8 全量兜底 / schema-miss 全量重试混进来的。** 依据(逐行核对):

- plan 档的写工具**在权限层就被拒**:`Permission.Decide`(Permission.cs L185-188)
  `if (text3 == "plan" && !flag) return deny("当前是「只看不动」模式,不能执行这类操作")`,
  与这一轮下发了哪些 schema 无关。所以 D8 改不改、schema-miss 重不重试,都不会让内置写工具在 plan 档执行。
- 真正的「写」缺口在别处,都不归本包改:
  1. **插件工具**绕过了 `Permission.Decide`(`Agent.cs` L1132-1137 `IsPluginTool` → 直接 allow)⇒ plan 档也放行。属 G 包(`win-perm` 已改)。
  2. **元信息标错的工具**:`remember` / `forget` 在 `ToolMeta` 里是 `ro: true`(实际会写记忆文件)⇒ plan 档放行。属 B2(`win-perm` 已改)。
- 因此本包**没有**给 D8 / schema-miss 重试加 plan 档过滤:那条路径不会产出可执行的写工具,
  加一层过滤只是重复 `Permission.Decide` 的判定,还会掩盖真正的缺口。这条结论已同步在 `AI5-I-report.md` 的事实清单里。

**顺带说明「批准」现在到底改变了什么**:plan 档下写操作本来就由 `Permission.Decide` 拒绝,
所以「批准」的语义是「同意这份计划并让回合继续」,批准的回合里执行的仍然只有只读工具;
本包只把「问」挪到「做」之前,不改变 plan 档的写权限判定(任务书也没要求放开)。

### 闭环:P2 + G + B2 三者合起来才构成完整的 plan 档保护(2026-11-28 与 lead 对齐)

`Permission.Decide` 在 plan 档拒绝非只读工具,这条结论只在「工具元信息老实」且「工具没有绕过 `Decide`」时才成立。
三个缺口各由一组提交补,缺任何一个 plan 档都有洞:

| 缺口 | 谁补 | 状态 |
|---|---|---|
| 计划审批在执行之后 —— 驳回时写操作已经落盘、检查点已经生成 | **P2(本包)** | 审批前移到 `Scheduler.RunGroups` 之前;驳回本轮一个工具都不执行、不生成检查点 |
| 插件工具一律 allow,绕过 `Permission.Decide` ⇒ plan 档也放行 | **G 包**(`win-perm` 提交 `a15d1ce`) | 插件工具现在也走 `Decide` |
| `remember` / `forget` 标成只读但实际写盘 ⇒ 「假只读」在 plan 档放行 | **B2**(`win-perm` 提交 `37d25b9`) | 改成 `readonly:false` / `write_local`,plan 档会被拒 |

- 没有 P2:驳回之前写操作已经执行完,审批只能拦住后续回合。
- 没有 G:插件工具在 plan 档照跑。
- 没有 B2:记忆写入在 plan 档照跑。

**因此本包不在 D8 / schema-miss 重试上加 plan 档 schema 过滤**:那只是重复 `Permission.Decide` 的判定,
既省不了风险,还会掩盖上面两个真缺口(已与 lead 确认,不加「双保险」)。
**验收时请把 P2 / G / B2 三组提交放在一起看**,单看 P2 会以为 plan 档还有洞。

## 二、文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 执行前审批块(L1480-1531,新增)、原执行后审批块(原 L1247-1266,删除);**没有**新增 i18n 词条 |
| `windos/tests/TipCL.Tests/AiAgentSourceGuardTests.cs` | `PlanApprovalHappensBeforeExecution` / `CheckpointOnlyHappensInsideExecute` 两条守卫 |

## 三、复现 → 修复 → 转绿

- **复现**:行为级复现需要假模型回放(AI5-T 场景 6:update_plan 后驳回 → 「没有执行任何写操作,也没有生成检查点」)。
  T 的框架不在本分支,本包先用源码守卫复现并钉住两件事实:
  1. `_call_confirm(confirmFn, "plan_approval"…)` 出现在 `Scheduler.RunGroups(list13, _execute)` **之前**;
     旧代码里它在其后 → 守卫必红。
  2. `Checkpoint.Snapshot(sessionId, turn.id…)` 在 `string _execute(…)` 之后,而驳回 `return` 在 `_execute` 之前 → 驳回路径不可能有检查点。
- **修复**:见上。
- **转绿**:`--filter "FullyQualifiedName~AiAgentSourceGuardTests"` → 4/4 通过(含 D8 的两条);
  全量 Windows 用例 826 过 / 6 跳 / 0 败 / 总 832。**场景 6 的行为级转绿待 AI5-T 合并后由 T 负责。**

## 四、验收数字

- 构建 `-t:Rebuild`:0 error / 2 warning(与基线逐条相同)
- 测试:826 过 / 6 跳 / 0 败 / 总 832(基线 802)
- 体积:见 `AI5-D.md`(合并口径:compact +11,776 B,按裁决不回收)
- i18n:新增词条 **0 条**。驳回回填复用了现成词条 `计划未获批准,本轮到此为止`(en.json / zh_CN.json 已有),
  所以不产生新的 `locales/*.json` / `*.gz` 改动 —— 这一包不会和 locales 合并冲突。
- smoke:本机禁止启动 WPF,未跑;「Windows 两版 smoke 全绿」留给集成阶段。

## 五、遗留问题

1. **批准语义**:plan 档下批准的回合仍只有只读工具能执行(写操作由 `Permission.Decide` 拒绝,与改造前一致)。
   如果产品上想要「批准后本回合放行写操作」,那是一次新的权限语义改动,需要用户裁决,本包没做。
2. **驳回文案**:沿用旧文案「计划未获批准,本轮到此为止」(只在「本轮没有写工具」时才追加「(本轮没有执行任何写操作)」)。
   修好之后「本轮一个工具都没执行」是恒成立的,文案可以简化,但会动 i18n 基线,留给文案/界面侧统一处理。
3. **`planApproved` 仍是回合内局部变量**:`_execute` 里的 `update_plan` 分支会把它重置为 `false`(新计划不等于旧批准)。
   本包的审批只在本轮含 `update_plan` 时触发,行为与旧版「每个新计划问一次」一致。
4. **场景 6 的端到端验证**依赖 AI5-T;本包只保证接线顺序与「不执行、不生成检查点」的结构性事实。
5. 与 G 包的重叠:插件工具分支(`Agent.cs` L1132-1137,`win-perm` 属主)在执行链路上,本包没有触碰;
   驳回分支对所有 `decisions.kind == "allow"` 的调用生效(插件 allow 也算),两者语义可以共存。