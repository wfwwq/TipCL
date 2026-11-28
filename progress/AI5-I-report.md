# AI5-I 接线报告:「光说没做」改成对照本回合的实际执行记录

- 日期:2026-11-28
- 分支与工作区:`ai5/win-agent` @ `C:\tipcl-ai5\win-agent`(基线 `b4cd7a2`)
- 分工:I 包**评测集数据**由 `intent-i` 交付(`ai5/intent-i`,commit `560e7ee`,`Data/ai-claims-eval.jsonl` 63 条 +
  `Data/ai-wants-eval.jsonl` 60 条);**接线与生产代码改动**在本分支。
  评测集文件本分支**没有提交**(按任务分配,合并时从 `ai5/intent-i` 合进来);本机验证时临时放进
  `windos/tests/TipCL.Tests/Data/` 跑过,跑完已删除。

## 一、事实清单(grep 结果:调用点与补救行为)

### 1. `ClaimsAction` —— 唯一调用点,改前只在 `no_tool_call` 分支判

| 项 | 改前 |
|---|---|
| 定义 | `Agent.cs` 原 L457-460:`return _CLAIM_RE.IsMatch(reply ?? "")`(`_CLAIM_RE` 在 L31) |
| 唯一调用点 | `Agent.cs` 原 L1636,在 `_result(...)` 里 |
| 触发什么补救行为 | 只有一处,而且是**反向**的:`if (stop_reason == "no_tool_call" && !ClaimsAction(reply)) { stop_reason = "completed"; detail = ""; }`。<br>也就是说:**回复声称干了活 → 保留 `no_tool_call` 和 detail**,UI 侧才显示提示:<br>· `BackendApi.cs:539` 把 `no_tool_call` 映射成「它没有真的开始执行:模型只回了文字,没有调用任何工具。」<br>· `Pages/AiPage.cs:2305`(`StopNote` 拼到助手气泡末尾)+ `:2336`(`NotifyStop` 弹 warning toast「它没有真的开始执行」)<br>· `Pages/AiFixWindow.cs:684` 同理 |
| 没有补救动作 | 不重试、不重新请求、不追加系统提示 —— 纯粹是「换个 stop_reason + 界面提示」 |
| 真实缺口 | 判定被 `stop_reason == "no_tool_call"` 挡住:只要本回合调过**任何**工具(哪怕只是 `search_mods`),后面说「已经装好了」完全不判。评测集 63 条里有 47 条是带工具调用的 `completed` 回合,全部漏判 |

### 2. `WantsAction` —— 零调用点(死代码)

| 项 | 事实 |
|---|---|
| 定义 | `Agent.cs` 原 L435-455 |
| 调用点 | **全树 0 个**(`grep -r "WantsAction"` 只命中定义本身;Linux 树也没有) |
| 配套正则 | `_ACTION_RE` / `_QUESTION_RE` / `_DELEGATE_RE` / `_OBSERVE_RE` / `_DIAGNOSE_RE` 五条,**只被 `WantsAction` 使用**,因此一并成为死代码 |
| 原意推断 | 「用户这句话是不是在让我动手」——Linux 侧对应能力是把 `AiData.ACTION_RE` 内联在 `Tools.cs:313` 决定**要不要带写工具**;Windows 侧没有任何地方消费它 |
| 处理 | **本包不接线、不删**:保留公开 API(插件/后续可能用),只在 `AiWantsTests` 里把现状准确率钉在评测集上,防止以后接线时悄悄变差 |

### 3. 另外两处死数据(不在本包范围,未改)

`windos/src/TipCL/Core/Launcher/Ai/AiData.cs` 里的 `ACTION_RE` / `CLAIM_RE` / `SYSTEM_PROMPT` / `TOOL_SCHEMAS_JSON` 零引用。
本包把新的「声称类别」正则放在 `Agent.cs`(`ClaimsAction` 旁边),没有搬进 `AiData.cs`,避免和 R/G 包的调研结论抢同一份文件。

## 二、做了什么(生产代码)

**判据改成**(任务书 §7 I):回复声称做完了某类操作(**install / remove / modify / create / launch / memory**),
而**本回合没有任何同类工具成功执行**(`status == "completed"` 才算 `ok`)⇒ 命中;多个类别**逐个判**,
不因为另一个真做成了就整体放过;只读/检索类工具永远不兑现完成声明。正则只负责「提取声称的类别」。

新增 API(`Agent.cs` L532-700):

| 成员 | 作用 |
|---|---|
| `CLAIM_INSTALL/REMOVE/MODIFY/CREATE/LAUNCH/MEMORY` | 六个类别常量 |
| `ClaimCategories(reply)` | 按短句切(。!?;,,、:换行),跳过否定/条件/失败句,再扫六类声称句式;返回**声称**的类别 |
| `ToolClaimCategory(name)` | 工具名 → 能兑现的类别。口径 = `ToolMeta.side_effect`(`delete`→remove、`launch`→launch、`write_local`→modify)+ 名字前缀(`install_`/`download_`→install、`create_`/`new_`→create、`launch_`/`run_`→launch、`delete_`/`remove_`/`uninstall_`→remove)+ `remember`/`forget`→memory;只读、未知、MCP 工具一律 `null` |
| `UnfulfilledClaims(reply, toolRuns)` | **判据本体**;`toolRuns` 是 `(工具名, 状态)` 序列,只有 `ok` 算成功执行 |
| `ClaimsAction(reply)` | 旧 API 保留成 `ClaimCategories(reply).Count > 0`,语义不变 |

`_result`(L1917-1945、L1967-1970)改成对**每个回合**都算一次:
- `no_tool_call` 且没命中 → 仍翻成 `completed`(旧行为不变);
- 命中 → `Trace.Record("claim_without_action", …)` + `Store.LogEvent(… "ClaimWithoutAction" …)` +
  `_status("claim_unmatched", {claims, turn_id})` + `AgentResult.claims_unmatched`;
- 未命中 → `AgentResult.claims_unmatched` 为空表。

> 界面文案没动:非 `no_tool_call` 回合的提示留给界面侧(AiPage 的 `OnStatus` 认 `claim_unmatched` 即可),
> 这样本包不会新增 i18n 词条,也不会和 locales 的合并冲突。

## 三、复现 → 修复 → 转绿

### 复现(改造前口径,量化到评测集)

用评测集逐条比对「是否该提示」(字段 `expect_claim_flagged`),把改造前的逻辑写成对照组:
`baseline = (stop_reason == "no_tool_call") && 旧 _CLAIM_RE.IsMatch(reply)`。

| 口径 | 63 条上的误判 | 漏判 | 误报 |
|---|---|---|---|
| 改造前(基线) | **36** | 36 | 0 |
| 改造后 | **0** | 0 | 0 |

核心场景(**声称装了、但本回合没有任何 install 类工具成功执行**):评测集里 12 条,**12/12 全部命中**。
另有两条硬要求:被拒(`denied`)/报错(`error`)的工具**不算兑现**(claim-007 / 008),多类别部分兑现仍要命中
(claim-011 装成了但没删、claim-012 装成了但没改配置)。

### 修复

见上(判据 + 类别映射 + `_result` 接线)。

### 转绿(真跑)

```
dotnet test windos\tests\TipCL.Tests -c Release --no-build \
  --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
→ 已通过! 失败: 0,通过: 826,已跳过: 6,总计: 832
```
其中本包新增:AiClaimTests 9 条 + AiWantsTests 1 条。

**变异验证(证明评测集那条用例真的在跑数据,不是空过)**:
把 `bin/.../Data/ai-claims-eval.jsonl` 里 claim-001 的 `expect_claim_flagged` 由 `true` 改成 `false`
→ 只跑 `AiClaimTests.EvalSetIsNotWorseThanBaseline` → **失败 1**(触发 `newFp == 0` 断言);
恢复数据文件后 → 通过。说明这条用例确实读取并逐条比对 63 条样本。
(数据文件不在本分支时,用例会检查「源码树里有没有数据」:源码树也没有 = 等 intent-i 合并,不做断言;
源码树有、输出目录没有 = csproj 的 `CopyToOutputDirectory` 坏了,直接失败。)

### WantsAction 现状留档

`AiWantsTests` 在 60 条 wants 评测集上实测 `WantsAction` 误判 **7/60**(漏 4 条该动手没认出来、误 3 条只是提问/建议却当成动手),
用例把上界钉在 7,只防「变差」。它零调用者,本包不接线。

## 四、文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | 删除 `_CLAIM_RE`(原 L31);新增六个类别常量 + `_CLAIM_NEG_RE` / `_CLAIM_SPLIT_RE` / 六条 `_CLAIM_PAT_*` + `_CLAIM_CATS_RE` + `ClaimCategories` / `ToolClaimCategory` / `UnfulfilledClaims` / `ClaimsAction`(L532-700);`_result` 判据接线(L1917-1945、L1967-1970) |
| `windos/src/TipCL/Core/Launcher/Ai/AgentResult.cs` | 新增 `claims_unmatched` 字段 |
| `windos/tests/TipCL.Tests/AiClaimTests.cs` | 新增 9 条(8 条规则用例 + 评测集对撞) |
| `windos/tests/TipCL.Tests/AiWantsTests.cs` | 新增 1 条(wants 评测集留档) |
| `windos/tests/TipCL.Tests/TipCL.Tests.csproj` | 加 `<None Include="Data\*.jsonl" CopyToOutputDirectory="PreserveNewest" />` |

## 五、验收数字

- 构建 `-t:Rebuild`:0 error / 2 warning(与基线逐条相同)
- 测试:826 过 / 6 跳 / 0 败 / 总 832(基线 802)
- 评测集:63 条误判 36 → **0**;核心 12 条 **12/12** 命中;误报 **0**
- 体积:见 `AI5-D.md`(compact +11,776 B,按裁决不回收)
- i18n:新增/修改词条 key **0 条**(清单为空)

## 六、遗留问题

1. **非 `no_tool_call` 回合的提示还没显示**:命中信息目前落在 `AgentResult.claims_unmatched` +
   `Store` 事件 + `claim_unmatched` status 上,`AiPage.OnStatus` 没有对应分支,所以「completed 但声称做了没做」这一档
   用户暂时看不到提示。要显示需要界面侧加一行(并在需要文案时加 i18n 词条)。
2. **是否要把 `ClaimsAction` 的老正则从 `AiData` 里删掉**:`AiData.CLAIM_RE` / `ACTION_RE` 零引用,
   本包没删(不在属主范围),建议由 R/G 或收尾包统一清理。
3. **`WantsAction` 没有接线**:如果以后要让「用户要动手」影响工具表(像 Linux 那样),建议直接复用
   `ClaimCategories` 的类别体系重写,而不是把旧正则接上去(旧正则实测 7/60 误判,且只看最后一条用户消息)。
4. **类别体系与 Linux 树口径**:Linux 没有这套判据;C2/AG 若要在 Linux 落地,可以直接搬 `ClaimCategories` +
   `ToolClaimCategory`(类别口径按 `AiToolTypes.side_effect` 对齐)。
5. **评测集依赖**:本分支不含 `Data/*.jsonl`,合并 `ai5/intent-i` 后 `EvalSetIsNotWorseThanBaseline` 才会真的断言;
   merge 时请确认 `windos/tests/TipCL.Tests/Data/` 下两个文件到位,且 `TipCL.Tests.csproj` 只加了一份 `Data\*.jsonl`(本包已加,勿重复)。