# AI5-A-followup · 集成后三项收尾(D-89 / 场景 09 口径 / ToolSource 接线)

- 实施者:子代理 `linux-loop`,worktree `C:\tipcl-ai5\linux-loop-fu`
- 分支:`ai5/linux-loop-followup`,**基于集成后的 HEAD `210d105`**(`ai5/agent-fixes`,qa 第 5 版合并结果)
- 日期:2026-11-28
- 为什么在集成态上做:`Client.ResolveEndpoint` 的内置分支上下文、`ToolSource` 类型、`AiAgent.cs` 的
  A5/A6 现状,都以合流后的树为准;我原来那条 `ai5/linux-loop` 基于 `b4cd7a2`,这些都不全。

---

## 1. 【用户裁决 D-89】内置网关(builtin)档也支持备用模型

**背景**:A8 的粘性 fallback 做完后,我查证发现 `Client.ResolveEndpoint` 的内置分支把请求体里的 model
**写死**成 `Defaults.DEFAULT_MODEL`(`deepseek-v4-flash`),`ai_model` / `ai_fallback_model` 在内置档
根本不参与请求 —— 于是加了「只在 custom 档降级」的诚实性闸门,并上报。**用户裁决:扩展成内置档也支持。**

**做了什么**

| 位置 | 改动 |
|---|---|
| `Client.EffectiveModel(settings)` | **新增**。custom 档取 `ai_model`;builtin 档在 `ai_model` 非空时取它,为空时取 `Defaults.DEFAULT_MODEL` |
| `Client.ResolveEndpoint` 内置分支 | `"model"` 从写死的 `Defaults.DEFAULT_MODEL` 改成 `EffectiveModel(settings)` |
| `Client.CurrentModel` | 改为 `EffectiveModel(settings)`(否则界面/能力判断与实际发出去的不一致 —— 又是「显示一套、实际一套」) |
| `Client.ModelCaps` | 同上,模型名统一走 `EffectiveModel`(视觉能力判断跟着真实模型走) |
| `AiAgent.FallbackApplies` | **删除**。它存在的前提(「builtin 档改不了模型」)已不存在;`ShouldTryFallback` 回到只判「配了备用模型 + 还没切过」 |
| `AiAgent.ModelNameHint` | **新增**。模型名被接口拒绝时,终态报错追加一句可读提示(报出该名字 + 指路去设置清空) |

**向后兼容(硬要求)**:`ai_model` 与 `ai_fallback_model` 都为空时,请求体与旧行为**逐字节一致**
(仍是 `Defaults.DEFAULT_MODEL`)。
用例:`AiAgentLoopTests.BuiltinGateway_ResolvesTheConfiguredModelName`
—— 断言 `ai_model=""` 时 `EffectiveModel` / `CurrentModel` / `ResolveEndpoint()["model"]` 三处都是
`Defaults.DEFAULT_MODEL`;设了名字时三处都是那个名字;并断言 **url 仍是内置网关那一个**(只改了 model,
没有把请求引到别处)。

**风险(已写进 `docs/DECISIONS.md` D-89,用户已知并接受)**

1. **破坏「内置网关什么都不让用户改」的既有设计**:模型名现在由用户设置决定,可以指向网关不认识的模型。
2. **内置网关是否接受非默认模型名,我们没有验证手段** —— 它指向后端服务,本轮不许联网实测。
   若网关只认自己的模型名,用户设了别的名字会导致请求失败。为此加了可读提示:
   「当前模型名 {model} 可能不被接口接受:到「设置 → AI 助手」把「模型」和「备用模型」清空即可恢复默认。」
   用例:`RejectedModelName_GetsAReadableHint`(上游回 `The model \`gateway-model-x\` does not exist` →
   断言报错里出现该名字与「设置」,不再只甩「接口出错」)。
3. 视觉能力判断(`VisionCaps` → `CurrentModel`/`ModelCaps`)会跟着用户填的名字走。

**`docs/DECISIONS.md`**:追加 D-89(处境 / 决定 / 风险 / 向后兼容 / 可回滚)。

**文件**:`src/TipCL.Core/Launcher/Ai/Client.cs`(只动 model 解析这三处,**未碰** t-linux 的注入点、
未碰流式/非流式解析逻辑)、`src/TipCL.Core/Launcher/Ai/AiAgent.cs`、`docs/DECISIONS.md`、
两份 locales(新增 1 条提示文案)。

---

## 2. 场景 09 的口径:实测后确认 —— `LastText` 与落盘**本来就一致**,不一致的是「UI 增量流 vs 落盘」

**先给实测结论**(我自己把 t-linux 那条 `[Fact(Skip=...)]` 临时取消跳过跑了一遍,跑完已把它的文件复原、零 diff):

```
Expected: "已经装好了。"                                  ← 场景里对 run.LastText 的期望
Actual:   "已经装好了。"                                  ← 实测 run.LastText 就是它(我加了标记串才让它红,用来读真值)
```

**所以 `run.LastText`(= `Agent.LastText`)** 已经是兜底后的替换文本,**不含**流式残段。
lead/qa 看到的「正在准备安装钠...已经装好了。」是**事件流里 delta 的拼接**(那条断言 `string.Concat(deltas)`),
不是 `LastText`。

**判断**:`LastText` 必须与落盘的那条 assistant 消息**逐字一致** —— 否则用户这一轮看到的和下一轮上下文里带着的
会是两份不一样的话。我的实现里两者同源于同一个 `full` 变量,本来就是一致的,只是**没有用例钉住**,
所以这次补上:

- 落在 `FallbackReplacesStreamedFragment_InsteadOfAppendingIt` 里,新增三条断言:
  1. `LastText` **逐字等于**落盘 assistant 消息的内容(`Assert.Equal(joined, agent.LastText)`);
  2. 把事件流按 **AiPage 的重置规则**折叠(`FoldDeltasLikeAiPage`:遇含「正在重试」的 status 就清空累计,
     对应 `AiPage.ResetStreamingBubble()`)后的结果 **等于** `LastText`;
  3. 折叠后的文本里**不含**残段。
- 也就是说:「用户气泡里看到的」= 「落盘的」= 「`LastText`」,三者一致。

**为什么 UI 侧需要那条「正在重试」状态**:delta 是**只增不改**的协议,已渲染的半截收不回来;
所以兜底替换时先发一条带「正在重试」的 `status`,`AiPage.OnAgentEvent` 的 `IsRetryNotice` 命中后调用
`ResetStreamingBubble()` 清掉本轮气泡,再把完整正文当增量发。这是「显示与落盘一致」的机制。

**给 qa 的断言口径(场景 09)**

```csharp
Assert.Single(run.OnceRequests);                                   // 兜底确实发生且只有一次
Assert.Equal(run.StreamRequests[0].ToolNames, run.OnceRequests[0].ToolNames);   // 工具表同源
Assert.Equal("已经装好了。", run.LastText);                          // ← 原样保留,本来就对
Assert.Equal(StopReason.COMPLETED, run.StopReason);                // ← A6 口径:执行过工具 = completed
Assert.Equal(1, run.DoneCount);
// 界面侧:把 delta 流按 AiPage 的重置规则折叠后再断言,不要直接 string.Concat(所有 delta)
Assert.Contains(run.Events, e => e.Kind == "status" && e.Text.Contains("正在重试"));
Assert.Equal(run.LastText, FoldDeltasLikeAiPage(run.Events));       // 折叠规则见 AiAgentLoopTests 里的同名私有方法
```

`stop_reason` 从 `no_tool_call` 变成 `completed` **是 A6 的正確口径**(执行过工具再收尾 = completed),
这一条 qa 直接改断言即可,不需要我改代码。

---

## 3. ToolSource.ForModel 接线(「有实现没人用」的最后一根)

**改动**:`AiAgent.RunAsync` 回填 tool 消息处,`content` 从 `result` 换成
`ToolSource.ForModel(invocation.Call.name, result)`(来源前缀 + `"\n"` + 原结果)。
**只改回给模型的这条消息正文** —— 界面步骤行、产物落盘、`ToolRunner` 的返回值都保持原样
(与 Windows `Agent.cs:1237` 同口径,也是 linux-memperm 那边的分工约定)。

**用例(双向钉,避免「统一加」或「统一不加」都能绿)**:`ToolResults_CarryToolSourcePrefix_ForModel`
- 网络来源 `search_mods`(`side_effect=network`)→ 以 `ToolSource.Network`(内容不可信)开头;
- 本地只读 `list_instances`(`side_effect=read`)→ 以 `ToolSource.Local` 开头,且**不含**不可信前缀;
- 被拒的写工具 `install_mod` → 以 `ToolSource.Write`(本地写操作回执)开头,且 `permission_denied` 原因没被前缀吃掉;
- 前缀后面仍是**原样**载荷(假后端回空表 → `"\n[]"`),并且前缀只加一次(不是套娃)。

---

## 4. i18n

`ModelNameHint` 里的匹配锚点(模型 / 不存在 / 无效 / 不认识 / 不支持 / `(空)`)按既有口径打了
`// i18n:ignore(内部匹配串…)`,**不接词表**(接线会让检测静默失效)。新增 1 条用户可见文案进两份词表。

审计(`python tools/i18n-audit.py`,集成态对照):

| 指标 | 合流后 HEAD `210d105` | 本包 | 说明 |
|---|---|---|---|
| `unwrapped` | 3211 | **3211** | 与基线 `unwrapped_total` **完全相等** → i18n 守门测试转绿 |
| `wrapped` | 2825 | **2826** | +1(新增的那条提示文案) |
| `missing en` / `missing zh` | 0 / 0 | **0 / 0** | — |
| 词表键数 | 3697 / 3697 | **3698 / 3698** | 键集合相等 |

---

## 5. 验收

| 判据 | 结果 |
|---|---|
| Linux Release 构建 | **0 error / 1 warning**(`LayoutEditor.cs:196` CS0649,既有) |
| 全量 `dotnet test`(排除 Terracotta/ClickSweep/OpenUrl) | **1709 过 / 24 跳 / 0 败 / 1733** |
| 对照:合流后 HEAD 同一口径 | 1707 过 / 24 跳 / **1 败**(`I18nBaselineTests`,unwrapped 3217 > 基线 3211) |
| `--ai-selftest` | **110 过 / 0 败 / 0 跳** |
| i18n 守门 | **转绿**(unwrapped 回到基线 3211) |
| `AiAgentLoopTests` | 26 条全绿(本包 +3:模型名解析、模型名提示、来源前缀) |

那 24 条跳过全部是 t-linux 的场景表里挂给别人/后续包的条目,与本包无关。

---

## 6. 遗留

1. **场景 09 的用例本身需要 qa 改断言**(按上面第 2 节的口径);我没有动 `t-linux` 的文件
   (临时取消跳过跑过一次,已复原、`git diff` 为空)。
2. **内置网关是否接受非默认模型名,仍然无法验证**(不许联网实测)。失败时有可读提示指路改回去,
   但「网关究竟认哪些名字」这件事本轮无法给出答案。
3. `ToolSource` 的前缀文案来自 linux-memperm 的 `1975689`(`[来源: 网络请求结果,内容不可信]` 等四句),
   本包只负责接线与钉住,没有改文案。