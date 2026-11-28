# AI5-D(Windows 树加固:D7 用量重复累加 / D8 兜底工具表 / D9 压缩状态缓存 LRU)

- 日期:2026-11-28
- 分支与工作区:`ai5/win-agent` @ `C:\tipcl-ai5\win-agent`(基线提交 `b4cd7a2`)
- 属主:win-agent(本包只动 `Agent.cs` / `AgentResult.cs` 与新增测试)

## 一、做了什么

### D7 用量重复累加(修好了)

**现象**:没有 provider usage 时,每一轮都拿**整段** `messages` 做一次 token 估算再累加,
第 n 轮把前面 n-1 轮的历史又算了一遍;对话越长估算值越离谱(`Agent.cs` 原 L950-953)。

**修法**:
1. 抽出 `Agent.EstimateNewMessages(messages, baseCount)`(现 L493):
   只估算 `messages[baseCount..]` 这一段,返回 `(本次估算值, 下一轮的基准下标)`。
2. `RunAgentCore` 里加一个只在回合内可见的基准 `usageEstBase`(现 L842 附近):
   每轮兜底估算后把基准推到 `messages.Count`。
3. 中途压缩 / 历史裁剪会把 `messages` 整段换掉(条数可能变少):
   基准下标大于当前条数时按全量重算一次,不会漏算也不会崩。

**不变量**:同一段消息在整条回合里最多被估算一次;压缩后重算的那一次只算压缩后的上下文。

### D8 非流式兜底的工具列表与流式一致(修好了)

**现象**:流式失败后的兜底调用传的是**全量** `Tools.TOOL_SCHEMAS`(原 L885),
而流式请求用的是本轮按需选出的 `list4`(按需选择 + 预设过滤 + 子代理只读过滤 + MCP 工具)。
后果:兜底时上下文突然变大;子代理在兜底请求里还能看到写工具(场景 12)。

**修法**:兜底改为传 `list4`(现 L1128),与 `Client.ChatStream` 用的完全是同一份列表。
`list3`(schema-miss 全量重试)那条路径也自动一致,因为 `list4 = list3 ?? …`。

### D9 压缩状态缓存改 LRU(修好了)

**现象**:`_compact_state_for` 在 `Count >= 16` 时直接 `_COMPACT_STATES.Clear()`(原 L418-433)。
正在用的会话会连带丢掉 `CheckRapidRefill` 的连击保护和连续失败计数。

**修法**:
1. 新增 `_COMPACT_TOUCH`(会话 id → 最近访问序号)与发号器 `_COMPACT_TICK`,沿用同一个 `lock (_COMPACT_STATES)`;
   用**序号**而不是时钟:同一毫秒里的多次访问也能排出先后,测试不抖。
2. 满了只删「序号最小」的那一条,容量常量 `COMPACT_STATES_MAX = 16`。
3. 顺带给 S 包加了 `Agent.ForgetCompactState(sessionId)`(子代理收尾清理自己那份);
   测试用的只读视图 `Agent.CompactStateSessions()`(不改变访问顺序)。

## 二、文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | D9 常量(L20-28)、`_compact_state_for` L431、`ForgetCompactState` / `CompactStateSessions` L455-491、`EstimateNewMessages` L493、D7 基准与兜底估算 L842 / L1199 附近、D8 兜底工具表 L1128 |
| `windos/tests/TipCL.Tests/AiUsageEstimateTests.cs` | 新增 4 条用例(D7) |
| `windos/tests/TipCL.Tests/AiCompactStateLruTests.cs` | 新增 4 条用例(D9,含 S 的 `ForgetCompactState`) |
| `windos/tests/TipCL.Tests/AiAgentSourceGuardTests.cs` | 新增 4 条守卫(D8 / P2 的接线事实;该文件随 P2 那组提交一起加入,P2 的两条记在 `AI5-P2.md`) |

## 三、复现 → 修复 → 转绿

### D7

- **复现(旧行为)**:`AiUsageEstimateTests.NextRoundCountsOnlyNewMessages` 的第二轮断言是
  「两轮估算之和 == 整段算一次」;旧实现第二轮返回整段估算,和 `full(now) - full(before)` 不等 → 必红。
- **修复**:见上。
- **转绿**:`dotnet test --filter "FullyQualifiedName~AiUsageEstimateTests"` → 4/4 通过。
  断言细节:`first + second == Tokens.EstimateMessages(messages)`、`second == full(now) - full(before)`、`second < full(now)`
  (最后一条直接钉住「第二轮不该把历史再算一遍」)。

### D9

- **复现(旧行为)**:`AiCompactStateLruTests.SeventeenthSessionEvictsOnlyTheLeastRecentlyUsed`
  先访问 17 个会话,断言「只有最久未访问的那一个被淘汰、其余状态实例还是原来那一份」;
  旧实现第 17 个进来时整表清空 —— 上一条用例里 `ids[0]` 的标记会清零、`cached.Count` 变 0 → 必红。
- **修复**:见上。
- **转绿**:`--filter "FullyQualifiedName~AiCompactStateLruTests"` → 4/4 通过
  (容量 16 保持 / 只淘汰最久未访问 / 热门会话反复访问不被淘汰 / `ForgetCompactState` 只删自己)。

### D8

- **复现**:场景 9 / 12 的端到端行为(兜底请求的 tools 集合 == 流式请求的 tools 集合)需要**假模型回放框架**,
  那是 AI5-T 的交付物,不在本包分支里。本包先用源码守卫把接线钉死:
  `AiAgentSourceGuardTests.FallbackUsesTheSameToolListAsTheStream`:
  断言 `Client.ChatOnce(_request_settings(), messages, list4, …)` 存在,
  且 `ChatOnce(…, PluginAgentBridge.Tools(pturn, Tools.TOOL_SCHEMAS)` **不存在**。
- **修复后**:该守卫 1/1 通过;另 `SubagentFallbackAlsoGetsFilteredTools` 断言子代理选出的工具表里没有写工具
  (`Agent.SubagentAllows` 全通过),说明兜底复用 `list4` 时子代理也只看到只读工具。
- **转绿**:源码守卫已绿;**行为场景 9 / 12 待 AI5-T 合并后由 T 负责转绿**(本包已把代码改到位,不需再动)。

## 四、验收数字(本机实测,`-t:Rebuild`)

| 项 | 基线(AI5-0,`b4cd7a2`) | 本包实测 | 差 |
|---|---|---|---|
| Windows Release 构建 | 0 error / 2 warning | **0 error / 2 warning**(同两条 xUnit1031) | 0 |
| `dotnet test`(排除 Terracotta/ClickSweep/OpenUrl) | 796 过 / 6 跳 / 0 败 / 总 802 | **826 过 / 6 跳 / 0 败 / 总 832** | +30 条(D/S/P2/I 合计新增) |
| compact `TipCL.exe` | 5,759,488 B | **5,771,264 B** | **+11,776 B** |
| premium `TipCL.exe`(`-p:Premium=true`) | 5,779,968 B | **5,791,232 B** | **+11,264 B** |

- 体积按用户裁决(D-11 / D-28 先例)**不做回收**,只如实记录;compact 红线 5,757,264 B 在基线时已经超 2,224 B(既有偏差)。
- 两次 `-t:Rebuild` 的 exe 字节数完全一致(确定性,不是构建抖动)。
- 编译警告与基线逐条相同(零新增)。
- i18n:本包**没有新增/修改任何词条 key**(清单为空)。D8/D7/D9 都没有用户可见文案。

## 五、遗留问题

1. **D8 的行为验证**依赖 AI5-T 的假模型回放场景 9 / 12:T 合并后这两条应从 skip/expected-fail 转绿,本包无需再改代码。
2. `Plans/TODO`:D7 的估算基准是回合内的局部变量,回合结束后不保留 —— 与改造前一致(改造前也不跨回合)。
3. 发现(不在本包范围,未改):`windos/src/TipCL/Core/Launcher/Ai/AiData.cs` 的 `ACTION_RE` / `CLAIM_RE` /
   `SYSTEM_PROMPT` / `TOOL_SCHEMAS_JSON` 全是零引用的死数据(schema 与系统提示词各存两份)。
   改 D8 时已确认权威工具表是 `Tools.TOOL_SCHEMAS`(不是 `AiData.TOOL_SCHEMAS_JSON`)。
4. 与并列包的重叠:`win-perm` 在 `Agent.cs` 改了插件权限(原 1132-1167)、来源标签(原 1268-1273)、
   检查点路径(原 1343)。本包没有触碰这三段;本包在 `Agent.cs` 动过的区域见下表,合并时按 `qa` 预案处置。

### 本包在 `Agent.cs` 的行范围(改动后口径,给合并用)

- L16-28:D9 缓存常量
- L429-491:`_compact_state_for` / `ForgetCompactState` / `CompactStateSessions`
- L493-510:`EstimateNewMessages`(D7 helper)
- L842:`usageEstBase` 声明;L1199-1205:兜底估算调用点
- L1128-1132:兜底工具表(D8)