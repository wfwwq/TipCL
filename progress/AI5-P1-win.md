# AI5-P1(Windows 侧)·「不询问」(`dontAsk`)档由「ask → deny」改成「ask → allow」

> 依据:`windos/docs/改造工程:2611/AI-agent-合并任务书.md` §3 裁决 6、§4 红线 6、§7 P1(Windows 侧)。
> 分支:`ai5/win-perm`(worktree `C:\tipcl-ai5\win-perm`)。日期:2026-11-28。

## 1. 做了什么

「不询问」档的语义从「需要确认的写操作直接拒绝」改成「自动放行」,但**删除类仍然拒绝**。

| 项 | 改动前 | 改动后 |
|---|---|---|
| `write_local` / `launch` / `write_external` 等非删除写操作 | `deny`(理由「用户开启了「不询问」模式」)| **`allow`** |
| `delete`(`delete_mod` / `delete_instance`)| `deny` | **`deny`(不变)** |
| 显式 `deny` 规则 | 优先命中 | **优先命中(不变)** —— 规则循环在档位 switch 之前,没有动 |
| 显式 `allow` 规则(不带 `rule_content`)| 对 `delete` 不生效 | **不生效(不变)** —— 红线 6 的既有保护原样保留 |
| plan 档 | `deny` 非只读 | **不变** |
| 子代理只读限制 | `Agent.cs` 的 `SubagentAllows` | **不变**(本包没碰) |
| MCP 外部确认 | 每次写操作都要用户在启动器里点确认 | **不变** —— 已核实它根本不读档位 |

### 行号复核(任务书写的是「先复核行号」)

| 任务书给的证据 | 复核结果 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Permission.cs:245-246` | ✅ 属实:`case "dontAsk": return new PermissionResult("deny", "用户开启了「不询问」模式");` |
| 系统提示文案 `PermissionNote` 的 dontAsk 分支 | 实际在 `Permission.cs:271-272`(任务书没给行号),已同步改写 |
| 「MCP 外部确认口径不变(先核实它是否读档位)」 | **核实:不读档位**。`McpServer.CallTool`(`McpServer.cs:331`)只看 `Tools.IsWriteTool(name)`,不看 `ai_permission_mode`;`ConfirmWriteOrDeny` 与档位无关。所以 P1 不影响 MCP 外部确认,即使档位是 `yolo` 也照样要确认 |

### 另一处同类代码:已按裁决一起改掉(2026-11-28 追加,见 §7)

原 `Agent.cs:571` 的布尔开关 `ai_permission_dont_ask` 是同一个「不询问」语义的**第二条实现路径**,
已按 lead 裁决改成与档位同口径。详见 §7。

## 2. 文件清单

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Permission.cs` | `Decide` 的 `case "dontAsk"`:`delete` 仍 `deny`(新理由「「不询问」模式仍然拒绝删除类操作」),其余返回 `allow`(理由「「不询问」模式下写操作直接执行」)。`PermissionNote` 的 dontAsk 文案改成「写操作执行前不再询问,直接放行;删除实例、删除模组这类删除操作仍然会被拒绝」 |
| `windos/src/TipCL/Resources/locales/zh_CN.json` / `en.json` | 新增两条词条(上面两个理由)。用户可见的那条是 deny 理由(`[权限] 已拒绝:…` 直接显示在工具气泡里);allow 理由目前不上屏,但按本仓惯例(Decide 的每条理由都有词条)一并接线 |
| `windos/src/TipCL/Resources/locales/*.json.gz` | 重跑 `windos/tools/pack-core-resources.ps1` 生成的确定性 gzip(源改了不重打包会被 `CoreResourcesPackTests` 当场挡下) |
| `windos/tests/TipCL.Tests/AiPermissionDontAskTests.cs` | **新增** 24 条用例 |

## 3. 复现 → 修复 → 转绿(三步证据)

**第一步 · 复现(先写用例,不改产品代码)**

```
dotnet build windos\TipCL.sln -c Release -m:1 -p:UseSharedCompilation=false   # 0 error
dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName~AiPermissionDontAskTests"
```
结果:**失败 9 / 通过 15 / 总计 24**。红的是这 9 条:

```
失败 DontAskAllowsWrites(tool: "install_mod")      Expected: "allow"  Actual: "deny"
失败 DontAskAllowsWrites(tool: "install_shader")   Expected: "allow"  Actual: "deny"
失败 DontAskAllowsWrites(tool: "install_game")     Expected: "allow"  Actual: "deny"
失败 DontAskAllowsWrites(tool: "create_instance")  Expected: "allow"  Actual: "deny"
失败 DontAskAllowsWrites(tool: "write_mod_config") Expected: "allow"  Actual: "deny"
失败 DontAskAllowsWrites(tool: "launch_game")      Expected: "allow"  Actual: "deny"
失败 DenyRuleOnlyMatchesItsContent                 Expected: "allow"  Actual: "deny"(命中外的那一次也被拒)
失败 PermissionNoteTellsTheTruth                   文案里还写着「会被直接拒绝」
失败 UnqualifiedAllowRuleNeverAllowsDelete         期望用例写错了 → 见下
```

> 第 9 条的教训:我一开始写成「`delete_mod` + 不带 `rule_content` 的 allow 规则在 `yolo` 档也不许 allow」,
> 跑出来 `yolo → allow`。复核 `Decide` 后确认**这是既有语义**:不带内容的 allow 规则被跳过(红线 6 保护的是
> 「规则」这一层),而 `yolo` 档本身就是全放行 —— 两者不矛盾。用例已改成钉住真实语义:
> `default → ask`(规则被跳过,`rule_id` 为空)、`acceptEdits → ask`、`dontAsk → deny`,不碰 `yolo` 的老行为。

**第二步 · 修复**:`Permission.cs` 的 `case "dontAsk"` 加 `delete` 例外 + 改 `PermissionNote` 文案 + 补两条词条 + 重打包 `.gz`。

**第三步 · 转绿**

```
dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName~AiPermissionDontAskTests"
已通过! - 失败: 0,通过: 24,已跳过: 0,总计: 24
```

## 4. 验收数字

| 判据 | 数字 |
|---|---|
| P1 用例 | **24 / 24 通过**(红时 9 失败)|
| P1 用例覆盖验收要求 | `write_local → allow`(6 条)、`delete → deny`(2 条)、命中 deny 规则 → deny(2 条)、不带内容的 allow 规则不放行 delete(1 条)、plan 档不变(1 条)、其它档位回归(8 条)|
| 其它档位回归 | `default/acceptEdits/edit/build/yolo` 对 `install_mod`、`edit/build/acceptEdits` 对 `delete_mod` 的判定**逐条不变** |
| 全量测试(仅 P1 改动的工作树)| **820 通过 / 6 跳过 / 0 失败 / 总 826**(基线 796/6/0/802,净增 24 条 = 本包用例)|
| 构建 | `0 error / 2 warning` —— 警告数与基线**一致**(`BridgeMutationAndLaunchGuardTests.cs:34-35` 的 xUnit1031 ×2,既有)|
| compact 体积(仅 P1)| **5,760,000 B**,基线 5,759,488 B → **+512 B**(其中词表 `.gz` +77 B,产品代码 +435 B)|
| i18n | 新增 2 条词条在两份词表里都有键(已用脚本逐字比对代码字面量与词表 key);`CoreResourcesPackTests` 漂移守卫 15/15 通过 |

## 5. 遗留问题 / 需要裁决

1. ~~**`ai_permission_dont_ask` 布尔开关没跟着改**~~ —— **已按 lead 2026-11-28 裁决改掉,见 §7**。
2. **显式 `ask` 规则在 `dontAsk` 档下仍然返回 `ask`**:规则循环在档位 switch 之前,规则优先。这是既有语义,
   本包按红线 6「不放宽」保持不动 —— 用户自己点名要「每次问」的工具,不该被档位静默放行。
3. **`dontAsk` 档在 Windows 的两处下拉里都没有入口**:`AiPermissionPanel.Modes` 与 `AiPage.PermissionModes` 都只有
   `default / acceptEdits / plan / yolo / custom` 五档(界面写着「与 Qt 版共用五档权限」)。所以「不询问」只能来自
   手改 `config.json` / 从别的前端迁来的配置。本包只改**语义与文案**,没有往下拉里加第六项(那会改用户可见的交互流程,
   超出 P1 的裁决范围)。如果要把「不询问(自动放行,删除类除外)」放进界面,需要另裁决。
4. **端到端没跑**:本包只做到 `Permission.Decide` 这一层的真值验证。真跑一轮 agent 看确认卡是否弹出、拒绝后是否不执行,
   属 AI5-T 的假模型回放框架(场景 22)。

## 6. i18n 词条清单(给合并用)

**新增 2 条**,`windos/src/TipCL/Resources/locales/{zh_CN,en}.json` 各一份(key 是中文原文):

| key(zh_CN 的值同 key)| en.json 的值 | 出处 |
|---|---|---|
| `「不询问」模式下写操作直接执行` | `In "Don't ask" mode write actions run directly` | `Permission.Decide` 的 allow 理由(P1)|
| `「不询问」模式仍然拒绝删除类操作` | `In "Don't ask" mode delete actions are still refused` | `Permission.Decide` 的 deny 理由(P1;用户可见,走 `[权限] 已拒绝:` 前缀直接上屏)|

**没有删任何既有词条**。登记一条:`用户开启了「不询问」模式`(zh_CN/en 各一条)在 P1 之后已经没有代码引用了
(原来的两处引用分别在 `Permission.Decide` 和 `Agent.cs` 的开关分支,现在都不再用它)。它**保留不删** ——
避免和 `win-rollback` 的词表提交撞,也避免旧日志/旧迁移数据里的文案查不到译文。要清理的话另立一个小包。

**`.json.gz` 说明**:我没有手改 `.gz` —— 是按仓库脚本重新生成的(确定性 gzip):
```
powershell -NoProfile -ExecutionPolicy Bypass -File windos\tools\pack-core-resources.ps1 -Repo windos
```
合并时按 `qa` 的策略:**`.json` 按文本合并后重跑这个脚本、再用 `CoreResourcesPackTests` 验证**(15/15 通过)。

## 7. 追加:「不询问」开关 `ai_permission_dont_ask` 改成与档位同口径(2026-11-28 裁决)

### 7.1 调用链核实(结论:**它是并行的第二条判断,不是被档位覆盖的废路径**)

改动前的事实(逐行核对,不是推断):

| 位置 | 代码 | 说明 |
|---|---|---|
| `Agent.cs:571` | `bool flag = Py.Truthy(settings.Get("ai_permission_dont_ask", false));` | 整个 `RunAgentCore` 里只读一次 |
| `Agent.cs:1179`(内置工具)| `else if (permissionResult.decision == "ask" && flag) { deny }` | 关键:`&& flag` 只在 **ask** 这一支上 |
| `Agent.cs:1152`(G 包新增的插件工具支)| 同上 | 我按内置工具的写法镜像了一份 |

所以它们的关系是:

1. **`Decide` 先算**,开关**只在 `Decide` 给出 `ask` 时**把它改写成 `deny`;**`Decide` 给的 `allow` / `deny` 一律原样通过**。
   ⇒ 它压不过档位:yolo 的 allow、plan 的 deny、acceptEdits 的 allow 都不受影响。
2. **它确实会生效**:`default` / `edit` / `build` 这些档位对写操作给的 `ask` 会被它改成 `deny`。
   ⇒ 同一个「不询问」语义出现两种相反行为:开开关 = 拒,切到 `dontAsk` 档 = 放行。
3. **只有一种情形下它碰巧无效**:档位本身就是 `dontAsk` 时,`Decide` 对非删除工具直接返回 `allow`
   (P1 之后),开关那一支根本走不到。这只覆盖了「档位已经是 dontAsk」这一种组合,**不是**覆盖了整条路径。
4. 全仓没有任何 UI 写这个键(`grep -rn ai_permission_dont_ask windos` 只有 `Config.cs:118`(默认 `false`)、
   `BackendApi.cs` 两个读写点、`Agent.cs` 一个读点),所以它只可能来自手改 `config.json` / 迁移来的旧配置 —— 正好是
   「同一语义两种行为」最容易坑到人的场景。

结论:**要改**(不是声明废弃)。

### 7.2 改法:入口合成一个「生效档位」,删掉回合里的第二处判断

```csharp
// Permission.cs(新增)
public static string EffectiveMode(PyDict settings)
// 开关打开 ⇒ 生效档位 = dontAsk(除非档位是 plan / yolo / bypassPermissions —— 它们给了更明确的态度)
```

- `Agent.cs:569-573`:`text3 = Permission.EffectiveMode(settings)`,并删掉 `bool flag = ...`。
- 删掉两处 `else if (decision == "ask" && flag) { deny }`(内置工具一处、插件工具一处)。
- `Permission.PermissionNote` 也改用 `EffectiveMode`,于是系统提示不会再「说一套做一套」。

为什么合成档位、而不是把两条分支改成 `ask → allow`:那样会漏掉三样只在 `Decide` 内部生效的东西 ——
规则顺序(显式 deny 优先、显式 ask 仍问)、plan 硬停、以及「能力未知的外部 MCP 工具要先确认」那条分支
(它比对的就是这个档位,`text3 != "yolo" && != "bypassPermissions"`)。合成之后这些都自动对齐,P1 的
「MCP 外部确认口径不变」也顺带成立。附带效果:`Agent.cs` 少了两段重复判断,compact 体积反而**降了 512 B**。

### 7.3 用例(24 条,全部在 `AiPermissionDontAskTests.cs`)

| 覆盖点 | 用例 |
|---|---|
| 开关开启 + 写工具 → allow(与档位逐字一致)| `SwitchOnAllowsWritesLikeTheMode`(install_mod / install_game / write_mod_config / launch_game ×4,每条都同时断言「与 `dontAsk` 档给出同一个答案」)|
| 开关开启 + 删除类 → deny | `SwitchOnStillDeniesDelete`(delete_mod / delete_instance ×2)|
| 折叠规则 | `SwitchFoldsIntoTheDontAskMode`(default/edit/acceptEdits/auto/autoEdit/build/custom/dontAsk ×8)、`ClearModesOutrankTheSwitch`(plan/yolo/bypassPermissions ×3)、`SwitchOffLeavesTheModeAlone` |
| 规则仍然优先 | `SwitchOnStillHonoursRules`(显式 deny 优先;不带内容的 allow 规则不放行 delete)|
| 不削弱 plan / 不收紧 yolo | `SwitchOnDoesNotWeakenPlanMode`、`SwitchOnDoesNotTightenYolo` |
| MCP 口径不变 | `SwitchOnKeepsUnknownMcpToolsAsking`(生效档位不是 yolo/bypass ⇒ Agent 那条 ask 分支仍然成立)+ 源码守卫钉住分支还在 |
| 提示文案跟随生效档位 | `PermissionNoteFoldsTheSwitch` |
| 不再有第二处判断 | `AgentHasNoSecondSwitchPath`(源码守卫:`EffectiveMode` 在、`Get("ai_permission_dont_ask"` 与 `&& flag)` 不在、MCP 分支在)|

### 7.4 红 → 绿(变异验证)

改动前那个开关的行为等价于「折叠被拆掉」,所以用变异复现:

```
# 把 EffectiveMode 的折叠结果从 PermissionMode.DONT_ASK 改成 text(开关变空操作)
失败! - 失败: 16,通过: 32,已跳过: 0,总计: 48    ← AiPermissionDontAskTests
# 还原后
已通过! - 失败: 0,通过: 48,已跳过: 0,总计: 48
```

16 条红正好是折叠相关的:`SwitchFoldsIntoTheDontAskMode`×8、`SwitchOnAllowsWritesLikeTheMode`×4、
`SwitchOnStillDeniesDelete`×2、`SwitchOnStillHonoursRules`、`SwitchOnKeepsUnknownMcpToolsAsking`、`PermissionNoteFoldsTheSwitch`。

### 7.5 验收数字(追加后)

| 判据 | 数字 |
|---|---|
| 全量测试 | **880 通过 / 6 跳过 / 0 失败 / 总 886**(基线 796/6/0/802)|
| P1 类用例 | 48 条(原 24 + 开关 24)|
| 构建 | `0 error / 2 warning`(与基线一致)|
| compact 体积 | **5,761,536 B**(基线 5,759,488 → **+2,048 B**);比加这个改动前的 5,762,048 B **少 512 B** |

### 7.6 这次改动带来的两处行为变化(如实登记)

1. **显式 `ask` 规则 + 开关开启**:改前是「静默拒绝」,改后是「弹确认卡」(因为合成档位后 `Decide` 的规则循环
   先返回 `ask`,与 `dontAsk` 档的行为一致)。方向上是**把决定权还给用户**,不是静默放行;但它确实是行为变化。
2. **档位面板显示与实际行为不一致**:`AiPermissionPanel` 读的是 `ai_permission_mode` 原值,开关打开时它仍显示
   「每次确认」。开关本身在 UI 上没有入口,所以只影响手改配置的用户;要修得动 UI 面板(不在本包范围)。
