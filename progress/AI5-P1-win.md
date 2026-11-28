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

### 另一处同类代码(如实登记,本包**没有**动)

`Agent.cs:1149-1153` 还有一支旧实现:设置项 `ai_permission_dont_ask`(布尔,`Config.cs:118` 默认 `false`)为真时,
把本该 `ask` 的判定改成 `deny`。它与 `dontAsk` **档位**是两回事(任务书 §3 裁决 6 只点名档位),
而且它在 `Agent.cs` 里 —— 该文件的属主是 `win-agent`,本包只被授权改另外三段(见 `progress/AI5-G.md`),
所以**保持原样**。全仓没有任何 UI 写这个键(`grep -rn ai_permission_dont_ask windos` 只有 Config/BackendApi/Agent 三处),
它只可能来自手改 `config.json`。要不要一起改口径需要裁决,见 §5。

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

1. **`ai_permission_dont_ask` 布尔开关没跟着改**:它仍把 `ask` 改成 `deny`(`Agent.cs:1149-1153`),与「不询问 = 放行」的新口径相反。
   两个理由没动它:(a) 裁决 6 只点档位名;(b) 那一段在 `Agent.cs`,不在本包被授权的三个片段里。
   **建议**:要么一起改成「`delete` 之外放行」,要么在 `docs/DECISIONS.md` 里写明它是废弃开关。
2. **显式 `ask` 规则在 `dontAsk` 档下仍然返回 `ask`**:规则循环在档位 switch 之前,规则优先。这是既有语义,
   本包按红线 6「不放宽」保持不动 —— 用户自己点名要「每次问」的工具,不该被档位静默放行。
3. **`dontAsk` 档在 Windows 的两处下拉里都没有入口**:`AiPermissionPanel.Modes` 与 `AiPage.PermissionModes` 都只有
   `default / acceptEdits / plan / yolo / custom` 五档(界面写着「与 Qt 版共用五档权限」)。所以「不询问」只能来自
   手改 `config.json` / 从别的前端迁来的配置。本包只改**语义与文案**,没有往下拉里加第六项(那会改用户可见的交互流程,
   超出 P1 的裁决范围)。如果要把「不询问(自动放行,删除类除外)」放进界面,需要另裁决。
4. **端到端没跑**:本包只做到 `Permission.Decide` 这一层的真值验证。真跑一轮 agent 看确认卡是否弹出、拒绝后是否不执行,
   属 AI5-T 的假模型回放框架(场景 22)。
