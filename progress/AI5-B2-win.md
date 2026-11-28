# AI5-B2(Windows 侧)· `remember` / `forget` 的元信息改成 `write_local`

> 依据:任务书 §7 B2(两棵树一起改,本包只做 Windows 侧)、§4 红线 6(不放宽权限)。
> 分支:`ai5/win-perm`。日期:2026-11-28。

## 1. 做了什么

`remember` / `forget` 真的会写盘(记忆文件),但 `ToolMeta` 把它们标成了只读。元信息改成与真实副作用一致:

| 工具 | 改动前 | 改动后 |
|---|---|---|
| `remember` | `readonly: true` / `side_effect: "none"` / `risk: "low"` | `readonly: false` / `side_effect: "write_local"` / `risk: "low"` |
| `forget` | 同上 | 同上 |

**没有**加白名单、没有加「始终允许」规则 —— 任务书 B2 允许「不想每次弹卡就加显式白名单」,但那属于放宽权限
(红线 6 只给 §3 第 6 条开了口子),所以本包按最小改动只修元信息。**代价是如实记录的**:默认档下这两个工具
现在会弹确认卡(`Permission.Decide` 对 `write_local` 在 `default` 档返回 `ask`)——见 §4。

### 行号复核

| 任务书给的证据 | 复核结果 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/ToolMeta.cs:48-49` | ✅ 属实(`remember` / `forget` 两行) |
| `windos/tests/TipCL.Tests/ToolRegistryTests.cs:53` | ✅ 属实(`Assert.False(Tools.IsWriteTool("remember"));`,已改成 `True` 并补 `forget`) |

## 2. 文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/ToolMeta.cs` | `remember` / `forget` 两行:`ro: false, "write_local", "low"`,并加一段说明为什么必须与真实副作用一致 |
| `windos/tests/TipCL.Tests/ToolRegistryTests.cs` | `MemoryAndWriteFlags` 里 `remember` 的断言 `False → True`、补 `forget`;新增 `MemoryToolsDeclareWriteLocal` 钉住 `readonly=false` + `side_effect=write_local` + `risk=low` |

## 3. 复现 → 修复 → 转绿

**第一步 · 复现**:改之前先看现状证据(不需要新建用例,仓库里已有的断言就是「错的元信息」的钉子):

```
$ grep -n "IsWriteTool" windos/tests/TipCL.Tests/ToolRegistryTests.cs
53:		Assert.False(Tools.IsWriteTool("remember"));      ← 这条断言与真实副作用矛盾
$ dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName~ToolRegistryTests"
已通过! - 失败: 0,通过: 6          ← 基线:6 条全绿,因为断言本身写的是旧口径
```

**第二步 · 修复**:`ToolMeta.cs` 的两行改 `write_local`;断言同步改。

**第三步 · 转绿**:

```
已通过! - 失败: 0,通过: 821,已跳过: 6,总计: 827       ← 全量(基线 802 + P1 的 24 + 本包 1)
```

顺带做了**变异验证**:把 `ToolMeta.Get` 里 `remember` 那行改回 `ro: true` 再跑,

```
失败 TipCL.Tests.ToolRegistryTests.MemoryAndWriteFlags      Expected: True  Actual: False
失败 TipCL.Tests.ToolRegistryTests.MemoryToolsDeclareWriteLocal
```
两条用例都变红 —— 证明它们真的钉在这一点上(改回后按字节复原,见下方提交)。

## 4. 影响面(逐条核实,含一处对任务书假设的更正)

| 受影响的消费点 | 位置 | 影响 |
|---|---|---|
| 确认卡 | `Agent.cs` 的 `Permission.Decide` 调用 | 默认档下 `remember` / `forget` **开始弹确认**;「不询问」档按 P1 放行;`edit` / `acceptEdits` 档也放行(`write_local`);plan 档拒绝 |
| 子代理工具表 | `Tools.SelectToolSchemas`(`Tools.cs:162` / `:173` 按 `@readonly` 过滤) | 子代理**不再拿到** `remember` / `forget`(以前会被当只读工具下发)。与「子代理只读」一致 |
| 并行调度 | `Scheduler.IsParallelizable`(`Scheduler.cs:35`) | 不再是可并行工具 → 进串行组。这正是 S 包要的「会话状态工具不并行」,本包通过元信息提前达成 |
| 钩子审计 | `Hooks._audit_worthy`(`Hooks.cs:132`) | `write_local` 属审计范围 → 这两个调用的审计记录开始出现(以前被当只读跳过)|
| 确认卡的「始终允许」 | `BackendApi.cs:750` 的 `allow_always` | 仍为 `true`(`side_effect != "delete"`),行为不变 |
| **MCP 外部确认** | `McpServer.cs:331` + `:25` 的 `_hidden` | **更正任务书的一处推断**:任务书说「`Tools.IsWriteTool` 是 MCP 判断要不要外部确认的依据,所以经 MCP 调用时也不确认」。核实结果是:`remember` / `forget` 在 `McpServer._hidden` 名单里(`McpServer.cs:25`),`ToolList` 根本不会把它们列给外部 agent,`CallTool` 也会直接回「该工具只能在启动器界面里用」。所以外部调用这条路径**本来就不存在**;本次改 `IsWriteTool` 后的真实收益是:万一以后有人把它们移出 `_hidden`,外部调用会**自动要求确认**(看的就是这一位)|

## 5. 验收数字

| 判据 | 数字 |
|---|---|
| 元信息 | `Tools.IsWriteTool("remember") == true`、`Tools.IsWriteTool("forget") == true`、两者 `side_effect == "write_local"`、`risk == "low"`、`readonly == false` |
| 用例 | 新增/更新 2 条,全绿;变异验证:改回 `ro: true` → 2 条变红 |
| 全量测试 | **821 通过 / 6 跳过 / 0 失败 / 总 827**(基线 796/6/0/802)|
| 构建 | `0 error / 2 warning`(与基线一致,无新增警告)|
| compact 体积 | **5,760,000 B**(基线 5,759,488 B)。P1 之后就是 5,760,000 B,B2 自己的增量**在压缩后测不出**(字符串 +12 B / 一个 bool 位)|

## 6. 遗留问题

1. **默认档下会弹确认卡**(上面 §4 第一行)。这是「元信息与真实副作用一致」的必然结果,不是 bug;
   如果产品上不想每次都问,需要一条**显式裁决**(加白名单或在 UI 上说明),本包不擅自放宽。
2. **Linux 侧同款改动不在本包**(任务书 B2 是两棵树的活,我只被派了 Windows 侧)。Linux 的
   `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs:67-68` 需要另一个属主跟进,验收口径为「两边 `readonly == false`」。
3. **`update_plan` / `dispatch_subagent` 仍是 `readonly: true` / `side_effect: none`**:S 包(并行排除表)要不要
   跟着改元信息,由 S 包属主决定;本包只动了任务书点名的两个记忆工具。
