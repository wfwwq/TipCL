# AI5-E · 自检与场景套件接进 CI(两棵树)

- **日期**:2026-11-28(ISO 8601)
- **分支**:`ai5/ci-e`,基线 `b4cd7a2`,worktree `C:\tipcl-ai5\ci-e`
- **任务书**:§7 E(权威副本 `C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`,与主仓未跟踪副本逐行一致)
- **提交范围**:仅 `.github/workflows/ci.yml`、`.github/workflows/ci-windows.yml` + 本文件。**没有改任何生产代码**。
- **红线状态**:未改版本号、未发 release、未 push;全程未运行任何会弹窗/抢前台的入口(`--smoke`、`verify-smoke.ps1`、ClickSweep、VisualTour、WPF 界面、浏览器)。

---

## 0. 结论摘要

1. `ci.yml` 加 **Linux `--ai-selftest` 步骤**,按**硬门槛**接(它能无窗口跑,理由见 §1/§3)。
2. `ci-windows.yml` 加 **Windows `--ai-ui-selftest` 步骤**,按**只作提示、不作门槛**接(需交互桌面,见 §3);
   并补一个 `if: always()` 的证据上传步骤。
3. 本机把 workflow 里的命令序列跑了一遍:Linux 全绿(与 ai5-0-env 的基线逐项相等);Windows 除自检外全绿。
4. **变异验证做了 4 次**:2 次如期变红(其中一次把**新接进 CI 的自检步骤**打红到 exit 4),2 次**被掩盖**——
   遮掩的原因查清了,是有价值的发现(§4.2、§8)。4 次全部还原干净(§4.3)。
5. 两条 workflow 通过 YAML 语法 + 结构检查(§5)。
6. **AI5-T 的场景套件在本 worktree 里还不存在**(`git log --all` 无 AI5 提交,`tests/` 下没有 replay/scenario 文件),
   所以只接了自检;场景套件的接入待 AI5-T 完成后由合并人补(§6,不需要再加步骤)。
7. Windows 自检**不能**无窗口跑(核过代码,见 §3)。按任务书 §7 E 的退路处理:只作提示。
8. 额外产出:体积门禁可行性评估(§7,**建议,未实施**)与一条缺陷类发现(§8,**未改代码**)。

---

## 1. 改了哪两个 workflow、加了哪些步骤

| 文件 | 新增步骤 | 位置 | 性质 |
|---|---|---|---|
| `.github/workflows/ci.yml` | `AI 自检(--ai-selftest;不起 GUI、不联网,硬门槛)`,`if: runner.os == 'Linux'` | `test` job,紧接 `Release build` 之后(它跑的就是那一步的产物) | **硬门槛**:退出码非 0 → `exit 1` |
| `.github/workflows/ci-windows.yml` | `AI 自检(最好努力 —— 托管 runner 无交互桌面,不阻断)`,`shell: pwsh` | `test` job,`单元测试` 之后 | **只作提示**:失败只打 `::warning`,不 `exit 1` |
| `.github/workflows/ci-windows.yml` | `AI 自检证据(result.txt / stdout / 截图)`,`if: always()` | 紧接上一步 | 证据上传(`if-no-files-found: warn`) |

新增总行数:`ci.yml` +55 行、`ci-windows.yml` +58 行(共 113 行,`git diff --stat`),全部是注释 + 步骤。

### Linux 步骤的判据(两条,缺一不可)

```
TIPCL_HOME=<临时目录> dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest
```

1. **退出码 0**(0 = 全过,4 = 有用例没过 —— `AiSelfTestCli.SelftestFailed`);
2. **报告末行的汇总必须是「失败 0」**(`^AI 自检:` 那一行)。

第 2 条不是装饰:退出码 0 而汇总写着失败 > 0 说明自检自身坏了。两处口径都写进了步骤注释。
另外步骤把 `TIPCL_HOME` 指到 `mktemp -d`(与 `tools/linux-real/verify.sh` 同口径)——自检会往
`$TIPCL_HOME/logs/`、`$TIPCL_HOME/cache/ai_trace/` 写东西,本机实测确认(§2.2)。

### Windows 步骤的关键实现细节

- 参数名是 **`--ai-ui-selftest`**,**不是**任务书 §6 写的 `--ai-selftest`。
  任务书自己标了「🕓 核对参数写法(`App.xaml.cs:161`)」,这里按代码核对的结果写:
  `App.xaml.cs:159` 读 `--ai-ui-selftest` → `AiSelfTest.Run(e.Args)` → `Environment.ExitCode`。
  (同一份 `SelfCheckArgs` 白名单里也是这个名字,`App.xaml.cs:42`。)
- **硬超时 5 分钟 + Kill**:WPF 窗口在无桌面会话里建不起来时,进程可能挂在等窗口上;
  不设超时会把这个 job 拖到默认的 6 小时。到点 `Kill` 后仍按「未通过」记 warning。
- 判据以它自己落盘的 `result.txt` 为准(WinExe 没有附带控制台):`^ALL PASSED` + 退出码 0;
  `AiSelfTest.cs` 末行写 `ALL PASSED` / `FAILED: n`,进程退出码 = 失败项数。stdout 只作兜底。
- 证据输出到 `dist/ai-selftest-win`,由下一步一并上传。

---

## 2. 本机验证(workflow 里同样的命令序列)

### 2.0 两个必须先说明的前提

- **`DOTNET_ROOT` 必须在本机命令里覆盖**:本机用户级 `DOTNET_ROOT=C:\Users\Administrator\.dotnet`
  没有 `host\fxr`(真实运行时在 `C:\Program Files\dotnet`),不覆盖时 `dotnet test` 会以
  `hostfxr.dll could not be found` 中止 —— 这是任务书 §1 记的「testhost 起不来」的真因,
  与 testhost 本身无关。**没有写进 workflow**:托管 runner 的环境是对的,写进去反而多此一举。
- **测试命令加了强制排除过滤**(本轮约束,避免碰真实陶瓦联机/GitHub/防火墙):
  `&FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl`。
  CI 里不需要、也没有这个后缀。编译一律串行(`-m:1 -p:UseSharedCompilation=false`)。

### 2.1 Linux 树(对应 `ci.yml`)

| 命令(本机实际执行的形态) | 结果 | 与基线对比 |
|---|---|---|
| `dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false` | **0 error / 1 warning** | ✅ 与基线一致(唯一警告是既有 `src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649) |
| `dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --filter "…TipCL.Tests.App…"`(第 1 批) | 通过 **490** / 跳过 **5** / 失败 **0**(共 495) | ✅ 0 失败 |
| `dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --filter "…!~TipCL.Tests.App…"`(第 2 批) | 通过 **1104** / 跳过 **6** / 失败 **0**(共 1110) | ✅ 0 失败 |
| 两批合计 | **1594 过 / 11 跳 / 0 败 / 总 1605** | ✅ 与 ai5-0-env 给的基线**逐项相等**(任务书 §6 的 1491/7/0 已过期) |
| `TIPCL_HOME=<临时> dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest` | **exit 0,通过 110 / 失败 0 / 跳过 0** | ✅ 与基线一致 |

### 2.2 自检确实无窗口、且确实需要隔离 `TIPCL_HOME`

- 自检**没有起 GUI、没有联网**:入口 `Program.cs` 的 `--ai-selftest` 分支直接走
  `Session.AiSelfTestCli.Run`(注释即写明「不起 GUI、不联网」),实测整段在 3 秒内跑完并打印报告。
- 隔离目录里实际生成了 `config.json`、`cache/ai_trace/20261128.jsonl`、`logs/tipcl-20261128.log`
  ⇒ 印证了「必须隔离 `TIPCL_HOME`」这条判据不是洁癖。

### 2.3 Windows 树(对应 `ci-windows.yml`)

| workflow 步骤 | 本机结果 |
|---|---|
| `构建`(`dotnet build windos/src/TipCL/TipCL.csproj -c Release`) | **0 error / 0 warning**(全 sln 口径的 2 条既有 xUnit1031 不在这个工程里) |
| `构建原生区块 E2E 驱动` | **0 error / 0 warning** |
| `单元测试`(`dotnet test windos/tests/TipCL.Tests -c Release`) | 通过 **796** / 跳过 **6** / 失败 **0**(共 802)——✅ 与 ai5-0-env 的基线逐项相等 |
| 自检步骤依赖的产物路径 | `windos/src/TipCL/bin/Release/net48/TipCL.exe` **存在 = True**(该步骤的 `Resolve-Path` 不会失败) |
| `AI 自检` 步骤 | **本机不跑**(原因见 §3.2) |

---

## 3. Windows 自检能不能无窗口跑?——**不能**

### 3.1 代码级证据(不是照抄结论)

1. 入口:`App.xaml.cs:159-163` 在 `OnStartup` 里读 `--ai-ui-selftest` → `AiSelfTest.Run(e.Args)`。
2. `Pages/Ai/AiSelfTest.cs` 的 `RunAll`(:174 起)会
   **`new Window{ Width=1280, Height=800, Content=aiPage, … }` 然后 `window.Show()`**(:177-187),
   再用 `RenderTargetBitmap` + `PngBitmapEncoder` 对窗口内容截图(`Shot()`,:95-121)。
   ⇒ **必须有真实的交互桌面**;`Dispatcher.PushFrame` 轮询(:80-93)也依赖窗口消息泵。
3. 先例:同一条姊妹流水线 `ci.yml` 里 win-x64 的 `--smoke` 就是因为这个红的
   (注释在 `ci.yml:447-451`:exit 1 且没有 `smoke.png`),那里也只作提示。

### 3.2 处置

- CI 侧:**只作提示、不作门槛**(任务书 §7 E 自己写的退路),失败打 `::warning` 并带上
  退出码 / 有没有 `result.txt` / 末几行输出,不阻断 job。原因写在 workflow 注释与本节。
- 本机侧:**按本轮红线不跑**——它会 `window.Show()` 弹窗口抢前台,而用户正在用这台机器。
  所以这一步的「本机验证」**无法完成**,如实记为遗留(§9 第 1 条)。
  这是**唯一**一条本机没验证的步骤;它依赖的产物路径已单独确认存在(§2.3)。
- 不改自检实现、不加 `--headless` 之类开关:那超出 E 包范围。

---

## 4. 变异验证

方法:改坏一条路由 → 重建 → 看对应层有没有红 → 还原 → 复核哈希与复跑。
为了排除「产物没重建」的干扰,每次都显式强制全量重编(`--no-incremental`)。

### 4.1 如期变红的两次

| # | 改坏了什么路由 | 变红的那一层 | 证据 |
|---|---|---|---|
| **M1** | `AiData.KEYWORD_TOOLS` 的「卡顿 / 内存」行,把 `delete_instance` 塞进 extra(纯提问「怎么改内存?」也会带出删除工具) | CI 里的 `dotnet test` 层 | `TipCL.Tests.App.AiToolsTests.SelectToolSchemas_AddsKeywordTools` **FAIL**:`Assert.DoesNotContain() Failure: Item found in collection`(`tests/TipCL.Tests/App/AiToolsTests.cs:101`),整批 `失败 1 / 通过 1`,exit 1 |
| **M3** | `Scheduler.PARALLEL_SIDE_EFFECTS` 去掉 `"read"`(只读调用不再并行成组) | **CI 里新加的 `--ai-selftest` 层** | `--ai-selftest` **exit 4**,汇总 `AI 自检:通过 108 / 失败 2`,失败项:`调度:只读并行、写删独占(a\|b\|c\|d\|e\|f)`、`调度:结果按原顺序回填`(分组塌成 6 个单元素组,与预期一致) |

> M3 的意义:它证明**我新接进 CI 的自检步骤是真门禁、不是空跑**——路由坏了它给 exit 4,
> 而步骤里第一条判据就会把 job 打成红。附带也验证了「退出码 + 汇总两条判据」的价值。

### 4.2 被掩盖的两次(查清了原因)

| # | 变异 | 观察 | 原因 |
|---|---|---|---|
| **M2a** | `ToolRunner` 里 `case "list_instances"` 改名成 `case "list_instances_BROKEN_ROUTE"`(工具分发表漏接) | Debug 的 xunit **红**(`AiHooksTests.ToolRunner_HookErrors_DoNotBreakTheCall_AndAreReportedToTheHost`,`Assert.Contains("get_instances")` 不成立);**Release 的 `--ai-selftest` 仍绿(110/0/0)** | 自检那条判据是 `readCall.status == COMPLETED && readResult.Length > 0`(:150-152),而 `RunAsync` 把 `ExecuteAsync` 的返回值**一律**标成 COMPLETED(:321-323)——即使那段文本是 `default:` 分支吐的 `unknown_tool` 错误载荷。详见 §8 |
| **M2b** | `ToolRunner` 里「只看不动」硬停的条件 `Mode == "plan"` 改名 | 两层都绿 | 「plan 档拒写」在 `ToolRunner.cs` 里是**三处冗余实现**(:101 硬停、:135、:345),单点改坏被另外两处兜住(纵深防御,不是漏检) |

> 这两条对 E 的结论:自检层覆盖不到「**已注册工具的分发表被改坏**」这一类回归,
> 而它恰恰是 AI5-T 场景表(16-18 条的工具序列、第 11 条 `tool_schema_miss`)要兜的东西 ——
> 这也正是 E 必须等 T 落地的原因,详见 §6 与 §8。

### 4.3 还原证据(四次全部还原干净)

- 手法:`git checkout -- <file>`,随后**逐文件 SHA256 比对**(运行期改过的三个文件):

  | 文件 | 还原后 SHA256 | 与开工前 |
  |---|---|---|
  | `src/TipCL.Core/Launcher/Ai/AiData.cs` | `0C17EA67F0A8C9B4DC5C0FFCAD0BB370B9F598B3B383319B500540E1AA22C744` | 完全相同 |
  | `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | `922105C2D1D3AA8BDC7807AD37748E1365BD2C82AE4E4E5F584DAE05DFACF956` | 完全相同 |
  | `src/TipCL.Core/Launcher/Ai/Scheduler.cs` | 与 HEAD 一致(`git status` 无该文件) | 完全相同 |

- `git status --short` 只剩 `.github/workflows/ci.yml`、`.github/workflows/ci-windows.yml` 两条改动;
  `git diff --stat` = 2 files changed, 113 insertions(+), **0 deletions**。
- 还原后强制全量重编 + 复跑:`0 error / 1 warning`、`--ai-selftest` **110 / 0 / 0,exit 0**。

---

## 5. workflow 文件通过 YAML 语法检查

用本机 Python 3.13 + PyYAML 6.0.3(不用 shell 拼 heredoc,写成一次性脚本 `_e-verify/check_yaml.py`,该目录不入库):

| 检查项 | 结果 |
|---|---|
| 两条文件 `yaml.safe_load` | ✅ 都解析通过 |
| 行首制表符(YAML 禁止用于缩进) | ✅ 都是「无」 |
| `ci.yml` … `test` job 步骤序 | ✅ 共 9 步,新步骤在第 6 步(Release build 之后、冒烟矩阵之前) |
| `ci.yml` 新步骤的 `if` | ✅ `runner.os == 'Linux'` |
| `ci.yml` 新步骤的判据 | ✅ 含 `exit 1`(退出码)+ 断言含「失败 0」(汇总)、`mktemp -d`(隔离)、`$GITHUB_STEP_SUMMARY`(写 summary) |
| `ci-windows.yml` … `test` job 步骤序 | ✅ 共 7 步,自检在第 6 步、证据上传在第 7 步 |
| `ci-windows.yml` 新步骤的 `shell` | ✅ `pwsh` |
| `ci-windows.yml` 新步骤「不阻断」 | ✅ 只有 `::warning`,正文无 `exit 1` |
| `ci-windows.yml` 硬超时 | ✅ 含 `WaitForExit(300000)` |
| 结论 | **全部通过** |

**真正的 CI 执行留到用户推送之后**(红线:不许 push)。

---

## 6. AI5-T 场景套件:接入待合并人补

- 开工实测:本 worktree `tests/` 下没有 replay / scenario / golden / fake-model 相关文件,
  `git log --all` 里也**没有任何 AI5 提交**(15 个 AI5 worktree 都还在 `b4cd7a2`)。
  ⇒ **场景套件尚未落地**,本次只接了自检。
- 按任务书 §7 E 的口径:**场景套件放在测试工程里,会随现有的 `dotnet test` 一起跑**,
  所以它落地后**不需要再往 workflow 加任何步骤**——Linux 侧继续分两批跑的既有口径不变。
  这一点已经写进 `ci.yml` 新步骤的注释末尾(`AI5-T 的回放场景套件放在测试工程里,随上面两批 dotnet test 一起跑,不需要额外步骤`)。
- 合并人需要做的只有两件事:
  1. 确认 AI5-T 的用例确实落在 `tests/TipCL.Tests/`(两批过滤器的覆盖面内)、且两批都全绿;
  2. 用 T 的场景表做一次真正的「改坏一条路由 → 变红」变异验证(见 §4.2:本轮已证明
     **自检层对分发/路由类回归不敏感**,所以 E 的这条验收证据最终要靠 T 来给才完整)。

---

## 7. 体积门禁可行性评估(**建议,未实施**)

背景(ai5-0-env 核实、我已复核):`ci-windows.yml` 里**没有任何体积门禁**;全仓唯一的门禁在
`windos/tools/build-both.ps1:90-97`,那是个**发布前手动脚本**,CI 从不调用它 —— 所以 compact
体积能超线 2,224 B 而 12 天没人报警。E 包任务书只要求加自检步骤,**没有**要求加体积门禁,
因此**本次不擅自扩范围**,只给评估。

**成本:低。** `ci-windows.yml` 的「构建」步已经把 `windos/src/TipCL/bin/Release/net48/TipCL.exe`
产出来了,门禁只需要在已有产物上量一次文件大小(一条 pwsh,0 次额外构建)。

**但有三个必须一起解决的前提,所以现在直接加会得到一个不可信/常红的门禁:**

1. **±512 B 的构建口径差**:`build-both.ps1` 的标准参数是
   `-c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`
   **且先净 `bin`/`obj`**(:38-62,D-14/WP-U:陈旧或跨 flavor 的增量产物会让 exe 顶高 512 B)。
   CI 的「构建」步两者都缺。本机实测印证:我按 CI 口径量到 **5,760,000 B**,而权威基线是
   **5,759,488 B**,差**正好 +512 B**。⇒ 贴线判断会得出错结论;要可信就得同时改「构建」步(超出 E 范围)。
2. **红线现在就是超的**:compact 基线 **5,759,488 B > 5,757,264 B(超 2,224 B,开工前既有偏差)**。
   门禁一上就是红的,等于把既有偏差变成 CI 常红 —— 必须先有体积裁决。
3. **premium 在 CI 里根本没构建**:`ci-windows.yml` 不带 `-p:Premium=true`;`build-both.ps1`
   对 premium 也只记录、不设限。要连 premium 一起管,得再加一次构建(本机约 15 s)。

**建议(供用户/后续包裁决,本次不提交)**:
- 体积裁决落地后**单独一包**做:① 把 `ci-windows.yml` 的「构建」步换成 D-14 标准参数 + 净 `bin`/`obj`;
  ② 紧跟一步量 `net48/TipCL.exe`,超 `5757264` 就 `exit 1`(与 `build-both.ps1` 同一常数);
  ③ 可选:再加 `-p:Premium=true` 构一次,只记录不设限。
- **不建议**直接在 CI 调 `build-both.ps1`:它带 dist 复制、哈希、`CS2012` 重试、防锁逻辑,是给发布机用的重脚本。

**与 E 的关系**:E 只改 YAML、不碰任何代码或构建参数,所以**本轮不可能改变体积**。
记录数字(取自 ai5-0-env 的基线,与本轮无关):compact **5,759,488 B**、premium **5,779,968 B**。
不做体积回收,也不声称合格/不合格 —— 裁决由用户定。

---

## 8. 顺带发现(不在 E 范围,**未改任何代码**)

**已注册工具的分发表漏接,会被标成「成功」。**

`ToolRunner.RunAsync` 在 `:321-323` 把 `ExecuteAsync` 的返回值**无条件**标成
`ToolCallStatus.COMPLETED`。而 `ExecuteAsync` 的 `default:` 分支(`:640-651`)对一个
**在 `ToolMeta.Map` 里注册、但 switch 里漏了 case** 的工具名,返回的是
`ToolErr(..., "unknown_tool", …)` 这样一段**错误 JSON 文本**。结果是:

- 调用方只看 `status`(例如 `--ai-selftest` 的 `readCall.status == COMPLETED && readResult.Length > 0`,
  `AiSelfTest.cs:150-152`)时**看不见**这次漏接;
- 模型会收到一段 `{"ok": false, "error": "unknown_tool", …}` 的“成功”工具结果。

证据:变异 M2a(§4.2)——把 `case "list_instances"` 改名后,Debug 的 xunit 红
(`AiHooksTests` 断言后端被调用的方法名是 `get_instances`),而 `--ai-selftest` 仍报 110/0/0;
同一份源码、两种配置各自强制全量重编后复现(排除产物陈旧)。

- **为什么不在 E 修**:E 的范围只有 workflow;而 `ToolRunner.cs` 是热点文件,属主是 B3/C2/P1 那几包。
- **建议**:① 留给 AI5-T 的场景表(它断言工具调用序列与结果内容,正是能抓这类回归的地方);
  ② 或由 `ToolRunner.cs` 的属主决定是「`status` 与载荷的 `ok` 字段对齐」还是「在自检里加一条
  『已注册工具的分发表必须全覆盖』的检查」(后者成本更低:遍历 `ToolMeta.Map` × 一个假后端跑一遍分发)。

---

## 9. 遗留问题

1. **Windows 自检步骤本机未验证**(唯一一条):自检会 `window.Show()` 弹窗口抢前台,按本轮红线不能跑。
   已核实它依赖的产物路径存在,并给步骤加了 5 分钟硬超时。**最终结论要等 CI 跑过或用户确认**。
2. **场景套件接入待 AI5-T 完成后由合并人补**(§6):T 落地后不需要加步骤,但那次「改坏路由 → 变红」的
   变异证据应由 T 的场景表来给。
3. **真正跑 CI 留到用户推送之后**(红线不许 push):本轮全部证据都是本机等价命令的实测。
4. **体积**:基线已超红线 2,224 B(既有偏差),E 不改体积;门禁建议见 §7,等裁决。
5. **`DOTNET_ROOT`**:本机跑 `dotnet test` 前必须覆盖成 `C:\Program Files\dotnet`,否则报
   `hostfxr.dll could not be found`。这是环境问题(建议并入 AI5-0 的环境收尾),**没有**写进 workflow。

---

## 附:本机验证用的完整命令序列(`DOTNET_ROOT` 已覆盖)

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'

# —— ci.yml 对应(Linux 树)——
dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
dotnet test  tests/TipCL.Tests/TipCL.Tests.csproj --nologo --filter "FullyQualifiedName~TipCL.Tests.App&FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
dotnet test  tests/TipCL.Tests/TipCL.Tests.csproj --nologo --filter "FullyQualifiedName!~TipCL.Tests.App&FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
$H = Join-Path $env:TEMP ("tipcl-selftest-" + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $H | Out-Null
$env:TIPCL_HOME = $H
dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest    # → 110/0/0,exit 0

# —— ci-windows.yml 对应(Windows 树;自检那一步按红线不跑)——
dotnet build windos/src/TipCL/TipCL.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
dotnet build windos/tests/TipCL.NativeBlockE2E/TipCL.NativeBlockE2E.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
dotnet test  windos/tests/TipCL.Tests -c Release --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"

# —— YAML 检查 ——
python _e-verify/check_yaml.py     # 一次性脚本,该目录不入库
```
