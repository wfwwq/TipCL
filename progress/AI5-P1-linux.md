# AI5-P1(Linux 侧)交付进度:「不询问」档由 ask → deny 改成 ask → allow(删除类除外)

> 这是任务书 §3 裁决 6 的**有意行为变化**,单独成一组提交(便于回滚)。
> 分支 `ai5/linux-memperm`;只改 Linux 树;没改版本号、没发 release、没 push。
> Windows 侧同一条由 `win-perm` 完成(commit `e80fe3e`),两棵树的文案已对齐(见 §4)。

## 1. 做了什么

修复前(Linux):`ToolRunner.ApplyDontAsk` 把判定结果里的 **ask 改成 deny**(理由「用户开启了「不询问」模式」),
`DecideRaw` 的 `"dontAsk"` 档分支也是无条件 deny —— 名字叫「不询问」,行为却是「不询问就直接拒绝」。

修复后(按裁决):

| 情形 | 行为 |
|---|---|
| 原本 **ask** 的判定(写操作默认档、MCP 之外) | **allow**(理由:`「不询问」模式下写操作直接执行`) |
| 删除类(`side_effect == delete`) | 仍然 **deny**(理由:`「不询问」模式仍然拒绝删除类操作`) |
| 命中显式 **deny** 规则 | 仍然 **deny**(「这条操作被你的规则禁止」)—— 规则在 `DecideRaw` 里先算,只动 ask |
| 命中显式 **allow** 规则 | 仍然 allow(理由「规则允许」) |
| `plan` 档 | 不变:写操作硬停 deny(「只看不动」),只读照常 allow |
| 只读工具 | 不变:allow(「只读操作」) |
| `yolo` / `bypassPermissions` | 不变:allow(「免确认模式」) |
| **外部 MCP 工具** | **不变**:`McpVerdict` 那一支照旧(ask → 仍按拒绝处理);见 §3 的核实结论 |
| 子代理只读限制 | 不变:P2/子代理侧的只读过滤与 plan 档都没被这一档绕过 |

实现要点:

- `ApplyDontAsk(tool, verdict, allowAsking)`:只在 `decision == ask` 上生效;删除类先拦(deny);
  `allowAsking: false` 是给 MCP 那一支用的(**同一份实现**,不是第二套判据)。
- `Decide(tool)` / `Decide(tool, args)` 走非 MCP 口径(allowAsking: true);
  `RunAsync` 里 MCP 工具改走新增的私有入口 `McpDecide()`(allowAsking: false)。
- UI 文案与 i18n:
  - 权限面板新增一行说明(P1 之前界面根本没说清这个开关做什么);
  - `PermissionNote` 的 dontAsk 句改成「写操作执行前不再询问、直接放行;删除实例、删除模组这类删除操作仍然会被拒绝。」;
  - 三处新文案都走 `I18nCore.Tr(...)` 并入 zh_CN / en 两张词表。

## 2. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | `ApplyDontAsk` 加 `tool` / `allowAsking` 参数;**ask → allow**,删除类 deny;`DecideRaw` 的 `dontAsk` 档分支改成 allow(删除类 deny);新增 `DontAskAllowReason` / `DontAskDeleteReason` / `DontAskMcpReason` / `McpDecide()`;`PermissionNote` 的 dontAsk 句换文案;`RunAsync` 里 MCP 走 `McpDecide()` |
| `src/TipCL.App/Pages/Ai/AiPermissionPanel.cs` | 新增 `DontAskHint`(面板说明行):「不询问 = 写操作直接放行,删除类仍然拒绝」 |
| `src/TipCL.Core/Resources/locales/zh_CN.json` / `en.json` | 新词条 4 条(§4) |
| `tests/TipCL.Tests/Core/AiPermissionDontAskTests.cs` | 改写 3 条:开关 ask → allow、删除类仍 deny、显式 deny 规则优先;被拒步骤行那条改用删除类验,并加一条「写操作不再被拒」的正例 |
| `tests/TipCL.Tests/Core/AiPermissionAliasModeTests.cs` | `dontAsk` 档改名 + 断言改成 allow / 删除类 deny;「告知与裁决一致」那组同步 |
| `tests/TipCL.Tests/Core/AiPermissionNoteTests.cs` | `NoteDontAsk` 换成新句(逐字节,按源码字节注入);语义一致的断言跟着改;文件头写明这是对参考版的**有意偏离** |

## 3. 「MCP 外部确认口径不变」的核实结论(任务书 §7 P1 第 4 条要求先核实)

核实结果:**MCP 这一支确实读档位**,而且读法与普通工具不同:

- `McpVerdict()`(`ToolRunner.cs:341` 附近)只看档位:`yolo` / `bypassPermissions` → allow;`plan` → deny;
  其余(含 `dontAsk` 档名)→ **ask**(「外部 MCP 工具能力未知,需确认」)。
- 修复前 `RunAsync` 里是 `ApplyDontAsk(McpVerdict())`:那个开关会把 MCP 的 ask 也翻成 deny。
- 修复后:`McpDecide()` = `ApplyDontAsk("", McpVerdict(), allowAsking: false)` ⇒ **开关仍然把 ask 翻成 deny**
  (与修复前逐字相同的那句理由),档位分支一个字没动。也就是说:
  - `dontAsk` 档名下 MCP 仍然走确认(修复前后一致);
  - 「不询问」开关打开时 MCP 仍然被拒(修复前后一致);
  - 真正被放宽的只有**非 MCP** 的本地工具。
- 另外,本机 MCP Server(`McpServerHost` / `McpServer`)的执行器仍开着 `ForceConfirmWrites`,
  外部发起的写操作一律弹确认、且确认卡没有「以后都允许」——这条链与 P1 无关,没动。

## 4. 与 Windows 侧(`win-perm`,commit `e80fe3e`)的口径一致性

| 位置 | Windows | Linux(本包) |
|---|---|---|
| allow 理由 | `「不询问」模式下写操作直接执行` | **同一句**(逐字) |
| 删除类理由 | `「不询问」模式仍然拒绝删除类操作` | **同一句**(逐字) |
| 提示句 | `[权限设置] 用户选了「不询问」:写操作执行前不再询问、直接放行;删除实例、删除模组这类删除操作仍然会被拒绝。` | **同一句**(逐字;Linux 多包了一层 `I18nCore.Tr`) |
| 词条 | zh_CN / en 各 2 条(`…写操作直接执行` / `…仍然拒绝删除类操作`) | 同 2 条同名同值;**另加** Linux 独有的面板说明行与提示句(共 4 条) |

## 5. 复现 → 修复 → 转绿

1. 复现(修复前):`AiPermissionDontAskTests` 里「开关打开 ⇒ `install_mod` 的裁决是 deny」与
   `AiPermissionAliasModeTests.DontAskMode_* ⇒ deny` 是既有断言 —— 它们钉的正是「名字与行为相反」。
2. 修复:见 §1/§2。
3. 转绿:`AiPermissionDontAskTests` / `AiPermissionNoteTests` / `AiPermissionAliasModeTests` 三组
   (**52 条,含同批的权限内容/范围用例**)全过;其中的关键断言:
   - `default` 档 + 开关 ⇒ `install_mod` = **allow**、`delete_mod` = **deny**;
   - `dontAsk` 档 ⇒ `install_mod` / `create_instance` = allow,`delete_mod` / `delete_instance` = deny,`list_mods` = allow;
   - 显式 deny 规则命中 ⇒ deny(不被开关改写)、显式 allow 规则命中 ⇒ allow;
   - `plan` 档不变(理由仍是「只看不动」);
   - 提示句与裁决说的是同一件事(逐字节比对新句 + 关键字「直接放行」「删除操作」)。
4. 风险(放宽权限)已按裁决封闭:删除类不给无限定放行(执行器 `CanRememberAlways` + `PermissionRules`
   的「删除类不带限定参数的 allow 不生效」两道)、MCP 口径不变、plan/子代理只读不受影响。

## 6. 遗留问题

1. 「不询问」在 Linux 目前仍只能手改 `config.json`(`ai_permission_mode = "dontAsk"` 或
   `ai_permission_dont_ask = true`);面板只提供五档,本轮**没有**加控件(避免扩大用户可见交互变化)。
   面板已有一行说明写清它的语义,后续要加控件另立小包。
2. `launch` 类(如 `launch_game`)在这一档下也会被放行(裁决只豁免 delete);
   如果用户希望启动类也例外,需要追加裁决 —— 本包不动。
3. 与 B2 的交互已锁:`remember` / `forget` 改标写工具后,`default` 档会弹确认,`dontAsk` 档直接放行
   (用例覆盖,见 `AiToolMetaSideEffectTests` + 本包 `AiPermissionDontAskTests`)。

## 7. 验收数字

- 相关用例组(`AiPermissionNoteTests` / `AiPermissionAliasModeTests` / `AiPermissionDontAskTests` /
  `AiPermissionScopeTests` / `AiPermissionContentTests` / `I18nCoreLocalesTests`):**52 过 / 0 败**。
- 全量 Linux `dotnet test`(排除 Terracotta / ClickSweep / OpenUrl):**1640 过 / 11 跳 / 0 败 / 总 1651**,
  `exit 0`(AI5-0 基线 1594 过 / 11 跳 / 总 1605;本分支一共新增 46 条用例)。
- Linux Release 构建:0 error / 1 warning(既有 CS0649),未新增警告。
- `--ai-selftest`:通过 110 / 失败 0 / 跳过 0,exit 0。
- i18n 审计:`unwrapped` 3211 → 3177(不增反降),`missing_en` / `missing_zh` 均为 0。
- 体积:本包只动 Linux 树源码;Linux Release 产物字节数记在 `progress/AI5-B.md` §6(红线里的 compact
  是 Windows 单 exe,不是本棵树产物,本包未碰 `windos/`)。
- `--smoke` / `verify-smoke.ps1` 按用户要求**未运行**(会抢前台)。