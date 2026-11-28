# AI5-G(Windows 侧)· 插件工具接入权限、检查点和不可信标记

> 依据:任务书 §7 G 包 + §3 裁决 3(方案 A)+ §4 红线 5(插件协议只许加字段)、红线 6(不放宽权限)。
> 分支:`ai5/win-perm`。日期:2026-11-28。协议文档:`windos/docs/plugins/DESIGN.md`。

## 1. 现状复核(任务书写的是「先核实」)

| 任务书给的现状 | 复核结果 |
|---|---|
| `Agent.cs` L1132-1137 让插件工具一律 `allow` | ✅ 属实(改动后移到 L1132-1167)|
| 检查点只看 `ToolMeta.Get(name)?.side_effect` | ✅ 属实:`Agent.cs:1340` 判 `toolMetaInfo3 != null && (side_effect == "write_local" \|\| "delete")` |
| 「插件工具在 `ToolMeta` 里大概率没有条目」 | ✅ **确认没有**:`ToolMeta.Map = ALL(38) + ExtraTools.Meta`,`PluginToolEntry` 是另一张表(`PluginTools`)。所以插件工具的 `ToolMeta.Get(...)` 恒为 `null` ⇒ 检查点条件永远不成立、不做快照、不进并行组 |
| L1237-1241 插件工具来源标签没有「内容不可信」 | ✅ 属实:插件工具的结果标签被无条件覆盖成 `[来源: 插件工具 <插件>]`,而 `network` 的插件工具本可以命中上面那句「内容不可信」 |

## 2. 做了什么

### 2.1 协议:两个**可选**新字段(只加字段,不改已有字段语义)

| 字段 | 取值 | 语义 |
|---|---|---|
| `sideEffect` | `none` \| `read` \| `network` \| `write_local` \| `delete` \| `launch` | 工具真实会产生的副作用。**不写 = 没声明**(老插件)|
| `affectedPaths` | `string[]` | 声明 `write_local` / `delete` 时给出会被改动的路径,供检查点快照用 |

- 作用点:manifest / `tools.register`(Node 与 .NET 插件共用一份归一化:`PluginTools.NormalizeSideEffect` / `NormalizeAffectedPaths`)。
- **非法取值在注册那一刻抛 `invalid_params`**,不会降级成「没声明」—— 拼错 `sideEffect` 就等于悄悄绕过权限判定,必须让插件作者当场看到错误。
- `DESIGN.md` 已同步:`tools.register` 的 params 行 + 新的一节「工具副作用声明」+ Node ctx 的 `tools.register` 说明 + 写法示例。

### 2.2 一份声明管住三处

| 能力 | 落点 | 行为 |
|---|---|---|
| 权限判定 | `ToolMeta.Get` 兜底 → `Agent.cs:1136-1166` | 声明了 `write_local` / `delete` 的插件工具走 `Permission.Decide`,受**现有档位和自定义规则**约束(含 P1 之后的「不询问 = 放行,删除类除外」);`none`/`read`/`network` 视为只读 |
| 检查点 | `PluginAgentBridge.AffectedPathsOf` → `Agent.cs:1343` | 声明写入的插件工具在执行前按 `affectedPaths` 做快照;内置工具照旧走 `Tools.AffectedPaths` |
| 不可信标记 | `PluginAgentBridge.IsUntrustedSource` → `Agent.cs:1272` | 声明 `network` 的标「内容不可信」;**没声明 `sideEffect` 的老插件也标**(方案 A) |

关键设计:`ToolMeta.Get` 对插件工具做兜底(`PluginTools.DeclaredMeta`),于是「权限判定、检查点、来源标签」三处**共用同一份真源**,
不必在 `Agent.cs` 里塞三套判断。没声明时 `DeclaredMeta` 返回 `null` ⇒ 与改动前**逐字一致**(仍然放行、不做快照、不进并行组)。

### 2.3 方案 A(§3 裁决 3)

没声明 `sideEffect` 的老插件:**行为不变,仍然直接放行**;只有来源标签统一变成 `[来源: 插件工具 <插件>,内容不可信]`。
`samples/hello-plugin` 的三个工具(`hello_wait` / `hello_badge` / `hello_ask`)都没声明,所以照常可用 —— 示例插件我**没有改**
(改它就会改示例的运行时行为;它现在正好是「方案 A 兼容性」的活体样本,用例里按它的声明形状钉了一条)。

## 3. 文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Plugins/PluginTools.cs` | `PluginToolEntry` 加 `SideEffect` / `AffectedPaths`;新增 `SIDE_EFFECTS`、`NormalizeSideEffect`、`NormalizeAffectedPaths`、`DeclaredMeta` |
| `windos/src/TipCL/Core/Launcher/Ai/ToolMeta.cs` | `Get` 对插件工具兜底到 `DeclaredMeta`(+`using TipCL.Core.Plugins`)|
| `windos/src/TipCL/Core/Plugins/PluginHostApi.cs` | `ToolEntry` 解析 `sideEffect` / `affectedPaths` |
| `windos/src/TipCL/Core/Plugins/Sdk/PluginSdk.cs` | `PluginToolSpec` 加同名属性(`.NET` 插件声明面)|
| `windos/src/TipCL/Core/Plugins/DotNetPluginContext.cs` | `RegisterTool` 走同一套归一化 |
| `windos/src/TipCL/Core/Plugins/PluginAgentBridge.cs` | 新增 `IsUntrustedSource` 与 `AffectedPathsOf` |
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 三处接线(见 §5)|
| `windos/docs/plugins/DESIGN.md` | 协议同步(表格行 + 新一节 + ctx 说明 + 示例)|
| `windos/tests/TipCL.Tests/PluginToolSideEffectTests.cs` | **新增** 35 条用例 |

## 4. 复现 → 修复 → 转绿

**第一步 · 复现(先把接线**定点回退**,再看用例红)**

G 包的性质是「缺一条接线」,不是「算错一个值」,所以复现方式是**只回退接线**(`git checkout -- Agent.cs ToolMeta.cs`,
保留新字段、新方法、新用例):

```
dotnet build windos\TipCL.sln -c Release -m:1 -p:UseSharedCompilation=false   # 0 error
dotnet test  windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName~PluginToolSideEffectTests"
失败! - 失败: 11,通过: 24,已跳过: 0,总计: 35
```

红的 11 条正好是「接线」相关的:

```
失败 AgentWiringHasTheThreeIntegrationPoints                       ← 三处接线被删
失败 DeclaredWriteToolAsksInDefaultMode                            ← ToolMeta.Get 拿不到声明 ⇒ 不弹确认
失败 DeclaredWriteToolFollowsTheModeLadder(default/plan/edit/acceptEdits/dontAsk) ← 5 条,声明没进权限判定
失败 DeclaredDeleteToolStaysDeniedInDontAskAndPlan                 ← delete 也没进判定
失败 DeclaredReadOnlyToolIsNeverAWrite(read/network/none)           ← 3 条,readonly 没来自声明
```

> 记录一个自己踩的坑:第一版用例里 `DeclaredWriteToolFollowsTheModeLadder` **没有**先断言元数据非空,
> 而 `Permission.Decide` 在元数据为 `null` 时会按 `write_local` 兜底 ⇒ 那 5 条在「接线被删」时**假装通过**。
> 已补 `Assert.NotNull(ToolMeta.Get("demo_write"))`,红组从 6 条涨到 11 条。

**第二步 · 修复**:按 §2 落地(新增字段 + `ToolMeta.Get` 兜底 + `Agent.cs` 三处接线 + DESIGN.md)。

**第三步 · 转绿**

```
dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName~PluginToolSideEffectTests"
已通过! - 失败: 0,通过: 35,已跳过: 0,总计: 35

全量:已通过! - 失败: 0,通过: 856,已跳过: 6,总计: 862
```

> **环境坑(给其他子代理的提醒)**:回退/恢复文件时我用 `Copy-Item`,它**保留旧时间戳**,MSBuild 的
> 「输入比输出旧」判定会**静默跳过重新编译** —— 于是「恢复后 856 全绿」那一跑实际用的是回退版的 DLL
> (出现 10 条假红)。用 `dotnet build -t:Rebuild` 强制重建后才是真结果(5,762,048 B / 856 绿)。

## 5. Agent.cs 的改动行范围(供合并时 cherry-pick)

`Agent.cs` 属主是 `win-agent`,本包**只改三处**,都在同一个提交里:

| 段 | 行范围(本分支提交后) | 内容 |
|---|---|---|
| 1 | `Agent.cs:1134-1164` | 插件工具分支:原来一行 `decisions[...] = ("allow", "")`,现在按声明的 `sideEffect` 走 `Permission.Decide`(deny / ask / allow 三种去向与内置工具一致)。未声明(`ToolMeta.Get` 返回 null)时**仍然直接 allow** |
| 2 | `Agent.cs:1260-1265` | 插件工具结果来源标签:按 `IsUntrustedSource` 决定是否加「内容不可信」 |
| 3 | `Agent.cs:1335` | 检查点取路径:`Tools.AffectedPaths(...)` → `PluginAgentBridge.AffectedPathsOf(...)`(内置工具行为不变)|

> 行号以最终提交为准(2026-11-28 修完「不询问」开关后重新取过;开关那个改动删掉了每轮两处 `ask → deny` 分支,
> 所以比 G 包刚落地时整体上移了 8 行)。

段 1 的判据只是「`ToolMeta.Get(插件工具名)` 非空」,所以合并时若 `win-agent` 已经改过这一带,冲突是局部可解的。

## 6. 验收数字

| 判据 | 数字 |
|---|---|
| 用例 | **35 / 35 通过**;定向回退接线时 11 条变红 |
| 声明写入的插件工具会触发确认 | `DeclaredWriteToolAsksInDefaultMode`:`Permission.Decide(声明 write_local, default 档)` → `ask`(Agent 里 `ask` 就是弹确认卡)|
| 拒绝后不执行 | `ask` 走 `_call_confirm`,用户拒绝 → `decisions = deny` → 不进 `Scheduler.Group` 的执行列表(与内置写工具同一条路径)|
| 执行前生成检查点 | `CheckpointPathsComeFromPluginDeclaration`:声明的路径进 `Checkpoint.Snapshot`,断言 `ok == true` 且 `files == 1`(真读了盘)|
| 老插件仍然直接放行 + 带不可信标签 | `UndeclaredPluginToolKeepsLegacyAllowBehavior`(hello-plugin 三个工具形状)、`UndeclaredPluginToolIsUntrustedButDeclaredReadIsNot` |
| 删除类不放宽 | `DeclaredDeleteToolStaysDeniedInDontAskAndPlan`:dontAsk → deny、plan → deny、不带内容的 allow 规则无效、显式 deny 规则优先 |
| 协议文档同步 | `DesignDocDeclaresTheNewFields`(DESIGN.md 含 `sideEffect` / `affectedPaths`)|
| 全量测试 | **856 通过 / 6 跳过 / 0 失败 / 总 862**(基线 796/6/0/802;净增 60 = P1 的 24 + 本包 35 + B2 的 1)|
| 构建 | `0 error / 2 warning`(与基线一致,零新增警告;过程中出现过一条 `xUnit1012`,已修掉)|
| compact 体积 | **5,762,048 B**,基线 5,759,488 B → **+2,560 B**(归因:P1 词表 `.gz` +77 B;其余是新增产品代码在 Costura 压缩后的体积。逐包实测:P1 = 5,760,000(+512)、P1+B2 = 5,760,000(+512)、P1+B2+G = 5,762,048(+2,560))|

## 7. 遗留问题

1. **端到端没跑**:「声明写入的插件工具弹确认卡、拒绝后不执行」我是拿 `Permission.Decide` 的真值 +
   源码接线守卫证明的,**没有**真跑一轮 `RunAgent`(那需要 AI5-T 的假模型回放框架)。接 `McpServer` 的外部确认路径也同理。
   建议 AI5-T 的场景表补一条「插件声明 write_local → 确认 → 拒绝 → 不执行」。
2. **未声明的插件工具仍然直接放行**(方案 A,裁决 3 明确要求)—— 这是一条**已知的宽松面**:
   插件不写 `sideEffect` 就绕过确认。好处是兼容,代价是「不声明就永远不弹卡」。建议在插件市场/安装页提示作者补声明
   (属产品决策,本包不做)。
3. **`samples/hello-plugin` 没改**:它现在正好是方案 A 的活体样本。要不要给它的 `hello_ask` 标 `network`、
   `hello_wait` / `hello_badge` 标 `none`,取决于是否愿意改示例的运行时行为(标了就进并行组、结果是另一条来源标签),
   留给插件作者/后续包。
4. **`affectedPaths` 只做路径快照,不做校验**:插件给什么路径就快照什么(与内置工具的口径一致,没有额外白名单)。
   如果以后要限制插件只能声明自己数据目录下的路径,那是另一条裁决。
5. **第三方宿主 SDK 的类型声明**:`windos/src/TipCL/Resources/plugin-host/tipcl-plugin.d.ts` 是给 Node 插件看的类型文件,
   本包**没改**它(它是 gz 内嵌资源,改它要重跑 `pack-core-resources.ps1` + 体积再涨)。要不要把 `sideEffect` 补进 d.ts
   由插件宿主属主决定,DESIGN.md 已经写清了协议。
