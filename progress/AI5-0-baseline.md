# AI5-0 · 环境与基线

- 日期:2026-11-28
- 执行者:ai5-0-env(施工子代理)
- 基线 commit:`b4cd7a22689071cd5f8b34e41108d342562b8aa3`(分支 `ai5/ai5-0-env`,worktree `C:\tipcl-ai5\ai5-0-env`)
- 任务书:`C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`(即 `windos/docs/改造工程:2611/AI-agent-合并任务书.md`,未被 git 跟踪)
- 机器:`Microsoft Windows NT 10.0.19045.0`,dotnet SDK `9.0.321`

> 后续每个包都跟本文件比。**本文件不含任何「合格」判定** —— 见 §2 与 §5。

---

## 1. 结论摘要

1. **本机两棵树都能编译、能跑测试**(Windows 树也能,任务书 §1 写的「Windows 树本机编译不了」**已证伪**,见 §3.3)。
2. **compact 体积红线在 HEAD `b4cd7a2` 上已经被突破**:实测 `5,759,488 B` > 红线 `5,757,264 B`,**超出 2,224 B**。这与 AI5 本轮任何改动无关,是开工前就存在的既有偏差(见 §5)。
3. testhost 起不来的根因是 **`DOTNET_ROOT` 用户级指向一个没有运行时的目录**(见 §3.2),进程级覆盖即可恢复 —— 任务书 §1 把它记为「testhost.exe 自身」的疑难项,**该结论应更正**。
4. Linux 树构建/测试/自检三项基线全部取到;Windows 树 `--ai-selftest` 因会拉起 WPF 窗口**按红线未跑**,标「需交互桌面,留待 CI」。

---

## 2. 基线数字总表

| 项 | 基线(本次实测,@ `b4cd7a2`) | 任务书 §6 记的口径 | 是否一致 |
|---|---|---|---|
| Linux Release 构建 | **0 error / 1 warning** | 0 error / 1 warning(`LayoutEditor.cs:196` CS0649) | ✅ 一致 |
| Linux `dotnet test`(已排除三组) | **通过 1594 / 跳过 11 / 失败 0 / 总计 1605**,耗时 4m12s | 1491 过 / 7 跳 / 0 败 | ❌ 任务书口径**已过期**,实测多 107 例 |
| Linux `--ai-selftest` | **通过 110 / 失败 0 / 跳过 0**,exit 0 | 110 项 | ✅ 一致 |
| Windows Release 构建(compact `TipCL.sln`) | **0 error / 2 warning** | 「由 AI5-0 测出」 | — 本次测出 |
| Windows Release 构建(premium) | **0 error / 2 warning** | 同上 | — 本次测出 |
| Windows `dotnet test`(已排除三组) | **通过 796 / 跳过 6 / 失败 0 / 总计 802**,耗时 5s | 「由 AI5-0 测出」 | — 本次测出 |
| Windows `--ai-selftest` | **未取**(会拉 WPF 窗口,红线禁止) | 「Windows 由 AI5-0 测出」 | ⚠️ 需交互桌面,留待 CI |
| 体积 · compact | **5,759,488 B  ❌ 超红线 2,224 B** | 红线 5,757,264 B(判据「≤ 红线」) | ❌ **开工即不合规** |
| 体积 · premium | **5,779,968 B**(仅记录,无上限) | premium 无上限仅记录(D-19) | ✅ 无约束 |
| i18n 基线 | 未测(本轮不动用户可见文案) | 基线不得回退 | — |

### 2.1 Linux 构建 warning 明细(1 条,与文档基线逐字相同)

```
C:\tipcl-ai5\ai5-0-env\src\TipCL.App\Pages\Layout\LayoutEditor.cs(196,18): warning CS0649:
Field 'LayoutEditor._foldAllBtn' is never assigned to, and will always have its default value null
[C:\tipcl-ai5\ai5-0-env\src\TipCL.App\TipCL.App.csproj]
```

### 2.2 Windows 构建 warning 明细(2 条,均为既有)

```
windos\tests\TipCL.Tests\BridgeMutationAndLaunchGuardTests.cs(34,30): warning xUnit1031:
  Test methods should not use blocking task operations, as they can cause deadlocks.
windos\tests\TipCL.Tests\BridgeMutationAndLaunchGuardTests.cs(35,22): warning xUnit1031: (同上)
```

两条都是 `TipCL.Tests` 项目里的分析器告警,**不是本轮引入**,属既有基线。

---

## 3. 环境

### 3.1 ACL(任务书 §7 AI5-0 第 3 步)

**复核结论:问题已不存在。** 本会话 pwsh 可以正常进入 `D:\项目\TipCL`:目录枚举、`git worktree list`、`git log`、以及用 read 工具直接读取该路径下的 UTF-8 文件均成功。a2 记录的「shell 进不了 `D:\项目\TipCL`」在本会话**无法复现**,按任务书要求记一句即可,无需修复。

> 附带提醒(踩过的坑):`D:\项目\TipCL\TipCL\windos\docs\改造工程:2611` 目录名里的冒号是**全角** `U+FF1A` 而非 ASCII `:`;用半角冒号拼路径会得到「文件不存在」。另外 `.NET` 静态方法(`[System.IO.File]::ReadAllText`)的相对路径按**进程 cwd** 解析,不受 PowerShell `Set-Location` 影响,读文件请用绝对路径或用 read 工具。

### 3.2 testhost 启动失败 → 根因是 `DOTNET_ROOT` 配错(重要更正)

**原始完整报错**(`dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release`):

```
Test run for ...\tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
Testhost process for source(s) '...\TipCL.Tests.dll' exited with error:
A fatal error occurred. The required library hostfxr.dll could not be found.
If this is a self-contained application, that library should exist in [...\bin\Release\net8.0\].
If this is a framework-dependent application, install the runtime in the global location
[C:\Program Files\dotnet\] or use the DOTNET_ROOT environment variable to specify the runtime
location or register the runtime location in
[HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\InstallLocation].
Test Run Aborted.
```

**根因**(实测,非推测):

| 事实 | 值 |
|---|---|
| 用户级 `DOTNET_ROOT` | `C:\Users\Administrator\.dotnet` |
| 该目录内容 | 只有遥测/哨兵文件(`*Sentinel`、`TelemetryStorageService`、`sdk-advertising`),**没有 `host\fxr`,没有 shared 运行时** |
| 真实运行时位置 | `C:\Program Files\dotnet`(ftxr `8.0.34` / `9.0.23`;shared 运行时 `Microsoft.NETCore.App 8.0.34` / `9.0.23`) |
| `dotnet` 本体 | `C:\Program Files\dotnet\dotnet.exe`(SDK `9.0.321`) |

即 testhost 的 apphost 读 `DOTNET_ROOT` 去找 `hostfxr.dll`,而该变量指向一个**没有运行时**的目录 → 报「hostfxr.dll could not be found」。**不是** 中文目录、不是 worktree 路径编码、也不是 testhost.exe 自身有问题 —— a1 §8.1 记的四种现象都指向这里,但当时没有查环境变量。

**解法(进程级覆盖,不改本机任何设置)**:

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
# → Passed! - Failed: 0, Passed: 1594, Skipped: 11, Total: 1605, Duration: 4 m 12 s
```

覆盖后 Linux、Windows 两棵树的 `dotnet test` **均恢复正常**。

> ⚠️ 每次 pwsh 调用都是独立进程,`$env:DOTNET_ROOT` 必须写在**同一条命令**里才生效。
> ⚠️ **不要**去永久修改用户级环境变量(那是改本机设置),只做进程级覆盖。
> 📌 **后续进展(2026-11-28)**:主 agent 已把 User 级 `DOTNET_ROOT` 修正为 `C:\Program Files\dotnet`,但**已在运行的会话不生效**(子进程继承旧值)。实测同日:注册表读到的已是新值,而当前会话 `$env:DOTNET_ROOT` 仍是 `C:\Users\Administrator\.dotnet`,不带覆盖跑 `dotnet test` 依旧 `missing_runtime`。→ **新开会话,或继续用进程级覆盖。** 该条已写入 `docs/dev/交接必读.md`「本机环境与构建前置」§1;CI 环境正常,**不要**把覆盖写进 workflow。
> 本条应回写任务书 §1 的「本机环境」行,替换掉原来指向 testhost.exe 的推断。

### 3.3 net48 参照程序集:机器级目录缺失**不是**障碍(更正)

| 事实 | 结论 |
|---|---|
| `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8` | **确实不存在**(复测确认) |
| 但 `windos/src/TipCL/TipCL.csproj:53` 有 `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 | 参照程序集**由 NuGet 包提供**,本地缓存已具备 |
| 实测 `dotnet build windos\TipCL.sln -c Release` | **Build succeeded,0 error / 2 warning** |

所以**任务书 §1 的「Windows 树本机编译不了」与 AI5-0 第 1 步「必须装 .NET Framework 4.8 Developer Pack」都是错的**:不需要 Developer Pack,不需要管理员权限,不需要 UAC。本包**未安装任何东西**(红线要求)。Windows 树基线数字**不需要标「待环境」**。

### 3.4 `--ai-selftest` 会不会抢前台(先核实再跑)

**Linux:** 核实路径 `src/TipCL.App/Program.cs:24-26` —— `--ai-selftest` 在 `BuildAvaloniaApp().StartWithClassicDesktopLifetime(...)`(`:42`)**之前**就 `return`,**不会起 Avalonia 窗口**。再核对自检实现不联网:全仓 grep `HttpListener|TcpListener|Socket|new HttpClient|Dns\.` 在自检相关文件零命中;`AiSelfTestEngineMcp.cs:1-3` 明说「不联网、不碰用户数据……MCP 只走 `Dispatch` 的协议面(**不起端口**)」;`AiSelfTest.cs:62-64` 把 `Hooks.HOOKS_DIR` 临时改到 `%TEMP%` 且末尾复原。→ **安全,已跑**。
- 唯一副作用:`Common.AppLog.Init()` 会在 `%LOCALAPPDATA%\TipCL\logs\` 追加一行日志(非交互、非网络)。

**Windows:** **未跑,也不该跑**。`windos/App.xaml.cs` 的自检经 WPF `App` 启动,会**弹窗并抢前台**;任务书 §6 自带的 🕓 待核标记也指向这点。按红线(禁止弹窗口)与主 agent 指令,Windows 自检项数**本机取不到**,标「需交互桌面,留待 CI」,交由 E 包在 CI 上取值。

---

## 4. 复现命令(全部为本次实际执行过的命令)

```powershell
# —— 共用:修 testhost(进程级,不改本机设置)——
$env:DOTNET_ROOT='C:\Program Files\dotnet'
$env:DOTNET_CLI_UI_LANGUAGE='en'   # 便于按英文关键字解析日志

# —— Linux 树 ——
dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
dotnet test  tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --ai-selftest

# —— Windows 树 ——
dotnet build windos\TipCL.sln -c Release -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
dotnet test  windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"

# —— 体积(仓库自带的标准口径,内部会先删 bin/obj 再全量构建)——
cd windos
.\tools\build-both.ps1 -Compact    # 超红线会 exit 1
.\tools\build-both.ps1 -Premium    # 只记录
# 只读校验 .gz 内嵌资源是否与源一致:
.\tools\pack-core-resources.ps1 -Check
```

**未执行且不该执行**(红线:禁止抢前台/碰网络):`--smoke`、`tools/verify-smoke.ps1`、ClickSweep、VisualTour、`TipCL.exe --ai-selftest`、任何真陶瓦/GitHub/防火墙相关用例。

---

## 5. 体积红线口径核实(任务一)

### 5.1 红线的准确含义

查 `docs/DECISIONS.md` **D-19**(`:886-889`,2026-11-07,用户拍板):

> 用户拍板「预算加 50KB」——compact 红线由 **5,707,264 → 5,757,264 B**(其余约束不变:**net48、单 exe、Costura、不嵌字体**;**premium 依旧无上限仅记录**)。

结论:

1. 红线 **5,757,264 B 只约束 compact**,premium 无上限、只如实记录。
2. 约束对象是 **net48 单文件 exe**,即 `windos/src/TipCL/bin/Release/net48/TipCL.exe`(Costura 打包)。**不是** `.deb`、**不是** AppImage、**不是**含 premium。Linux 侧的 `.deb` / AppImage 体积另计(`docs/DELIVERY.md:142`、`docs/PARITY.md:666`),与本红线无关。
3. 门禁实现:`windos/tools/build-both.ps1:90-97` —— compact 超线即 `Write-Error` + `exit 1`;**premium 只打印不设限**。
4. D-11 / D-28 有「布局功能豁免体积红线」的**先例**,但豁免只针对特定功能包,红线本身**未改**。

### 5.2 最近一次记录与构建参数

| 来源 | 时间 / HEAD | compact | premium | 余量 |
|---|---|---|---|---|
| D-38(`docs/DECISIONS.md:1082-1083`) | 2026-11-14 / `9a3cc52` | 5,533,184 B | 5,553,152 B | 224,080 B |
| **最新记录**:`docs/RELEASE-DRAFT.md:314-317` | 2026-11-15 / `25c4e64`(v1.2.0) | **5,534,208 B** | **5,554,176 B** | **223,056 B** |
| CI 自建件(`docs/RELEASE-DRAFT.md:294`) | 同上 | 5,534,720 B | — | — |

即 **任务书 §1/§6 引的「最近一次记录是合规、余量 224,080 B」其实是 D-38 的 2026-11-14 数字**(任务书引的是 `DECISIONS.md:888` 和 `:1083`),而**真正最近的记录是 v1.2.0 的 5,534,208 B / 余量 223,056 B**。两者相差 1,024 B,`docs/RELEASE-DRAFT.md:420` 也专门提醒过「两个数只差 1,024 B,但别混用」。

**记录口径的构建参数**(D-14 标准参数,`docs/DECISIONS.md:860`、`windos/tools/build-both.ps1:61-62`):

```
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1
# premium 追加 -p:Premium=true
# 构建前必须 rm 净 src/TipCL/{bin,obj}(WP-U 坑:跨 flavor 增量残留会把 exe 顶高 512 B)
```

### 5.3 本次实测(同一口径,两种独立构建路径)

| flavor | 字节数 | SHA256 | 路径 |
|---|---|---|---|
| **compact** | **5,759,488 B** | (未记,可重跑) | `windos/src/TipCL/bin/Release/net48/TipCL.exe` |
| **premium** | **5,779,968 B** | `070A42F8737EF73A7F08283F3273045D75B11C8E3A2FA2773EF750C4E931D082` | 同上(premium 构建后覆盖) |

两条**互相独立**的路径得到**逐字节相同**的 compact 结果:

| 路径 | 命令 | 结果 |
|---|---|---|
| A · 裸 `dotnet build` | `dotnet build windos\TipCL.sln -c Release -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false` | 5,759,488 B |
| B · 仓库标准脚本 | `cd windos; .\tools\build-both.ps1 -Compact` | 5,759,488 B |
| C · 另一施工代理 | win-perm 产出 | 5,759,488 B |

路径 B 还**触发了仓库自带门禁**,原样输出:

```
compact 构建: 0 错误
compact exe: 5759488 B
compact 超红线: 5759488 B > 限制 5757264 B,超出 2224 B
[exit code: 1]
```

### 5.4 已排除的解释:内嵌资源没有失真

D-38 记过一条坑:「只生成 `.gz` 而没改 csproj 会**同时嵌入原文 + `.gz` 两份,exe 反而涨约 158 KB**,且**静默污染体积基线**」。为排除它,跑了只读校验:

```
& .\windos\tools\pack-core-resources.ps1 -Check
→ 15 个文件全部 ok(locales 2 + ai 1 + plugin-host 12),STALE 0 个
   源 596,064 B / 压缩后 172,031 B / 可省 424,033 B
```

**15 条全部 `ok`,无 STALE**,且 exe 里确实只有 `.gz`(csproj `:97-105` 的 `EmbeddedResource` 全部指向 `.gz`,并都带 `WithCulture="false"`)。所以 5,759,488 B **不是**资源重复嵌入造成的虚高,是**真实体积**。

### 5.5 结论(不下合格判定,只给事实)

- 红线含义已查清:**compact 单 exe 的 5,757,264 B**,premium 无上限。
- 最近一次记录:**5,534,208 B**(v1.2.0 / `25c4e64` / 2026-11-15 / 余量 223,056 B)。任务书引的 224,080 是更早的 D-38 数字。
- 本次实测:**compact 5,759,488 B,premium 5,779,968 B**。
- **compact 比红线大 2,224 B**;增量按不同参照点分别是:比最新记录 `5,534,208`(v1.2.0)**增长 225,280 B**,比 D-38 的 `5,533,184` **增长 226,304 B**(两个数都对,只是参照点不同,别混用)。
- 该偏差**在 HEAD `b4cd7a2` 上可复现**,与本轮 AI5 任何改动无关 —— **是开工前就存在的既有基线偏差**。另一施工代理 win-perm 与验收方 qa 用**干净 worktree**(源码与 `b4cd7a2` 逐字节相同)独立复现出同一个 `5,759,488 B`,三方一致。
- 按任务书 §6 判据(compact ≤ 5,757,264 B)与 DoD「compact 体积检查通过」,**当前 HEAD 的起点本身即不合规**。本文件**不对任何包判体积合格/不合格**;这一条需要主 agent 上报用户裁决(见 §7 遗留问题 1)。
- → **已裁决(2026-11-28)**:见 `docs/DECISIONS.md` **D-87** —— 沿用 **D-11 / D-28 先例**,本轮**不回收**、各包如实记录改动前后字节数,体积回收另立包统一处理,**不做「零增长」要求**;DoD §10「compact 体积检查通过」在本轮按「**如实记录 + 既有超线豁免**」执行。本文件依然是唯一的比对基准。

### 5.6 为什么长期没暴露:CI 里没有体积门禁

已核实(`grep` 全量扫 `.github/workflows/*.yml` 中的 `5757264 | build-both | Length | SIZE`):

- `ci-windows.yml`:**零**体积相关步骤;
- `ci.yml`:唯一命中是 `:445` 的 `Get-ChildItem dist/TipCL-win-x64 | Format-Table Name, Length` —— 这只是把 **Linux 侧 win-x64 publish 目录**的内容列出来给人看,**不比对、不设阈值、不会失败**。

也就是说,**全仓唯一的 compact 体积门禁只存在于 `windos/tools/build-both.ps1:90-97`,而它只有在有人手工跑该脚本时才会执行**。CI 从不跑它 → 12 天里 compact 从 5,534,208 B 涨到 5,759,488 B、越过红线 2,224 B,**没有任何自动化环节报警**。这解释了偏差为何能存在至今,也说明 E 包(接 CI)如果只加自检、不加体积门禁,同类漂移会再次发生。

---

## 6. 文件清单

| 文件 | 动作 | 说明 |
|---|---|---|
| `docs/DECISIONS.md` | 追加 | 新增 **D-86**(六项裁决),用任务书附录 B 的草稿**原文照录**,共 +14 行;`docs/DECISIONS.md` 原本到 D-85,现到 D-86。UTF-8 无 BOM 编码保持不变(编辑前后首字节均为 `23 20 44 45`)。 |
| `progress/AI5-0-baseline.md` | 新增 | 本文件。 |

**没有改动**:`windos/DECISIONS.md`(旧副本,按要求未动);任何源码;任何版本号;任何 workflow;**未安装任何东西**;**未 push**。
**未纳入提交**:任务书本身(`windos/docs/改造工程:2611/` 未被 git 跟踪);构建日志(临时目录 `.ai5-logs/`,已删除)。

---

## 7. 遗留问题(需主 agent / 用户处置)

1. 【**已裁决** → `docs/DECISIONS.md` **D-87**,2026-11-28】compact 体积红线在 HEAD 上超 2,224 B(既有偏差,非 AI5 引入)。**裁决:沿用 D-11 / D-28 先例,本轮不回收、各包如实记录,回收另立包;不做「零增长」要求;DoD §10 按「如实记录 + 既有超线豁免」执行。** 另:D-87 明确 **CI 补体积门禁本轮不做**(CI 构建参数与 `build-both.ps1` 差 +512 B,基线又已超线,现在加会成为常红门禁),待体积回收包一并处理。
2. 【**需要回写任务书,尚未做**】任务书 §1「本机环境」行有三处现已证伪,建议更正:
   - 「Windows 树本机编译不了」→ **能编译**(§3.3,参照程序集来自 NuGet 包);
   - 「`dotnet test` 的 testhost 起不来,四种现象都指向 testhost.exe 自身」→ 根因是 **`DOTNET_ROOT` 指向无运行时目录**(§3.2);
   - 「最近一次记录是合规、余量 224,080 B」→ 最新记录是 **5,534,208 B / 余量 223,056 B**(§5.2)。
   相应地,AI5-0 第 1 步「装 .NET Framework 4.8 Developer Pack」**不需要做**(且它要管理员权限,已按红线回避)。
3. 【**口径不一致**】任务书 §6 记的 Linux 测试基线「1491 过 / 7 跳 / 0 败」与实测「1594 / 11 / 0 / 总计 1605」不符,差 107 例。建议后续以**实测 1605**为比对基线;旧的 1491 口径应作废(D-85 已把测试口径沿革记到 418 那一代,数字漂移属正常,但文档没同步)。
4. 【**待 CI**】Windows `--ai-selftest` 的项数本机取不到(WPF 弹窗抢前台,红线禁止)。E 包接 CI 时需先确认托管 runner 能否无窗口跑该自检;不能则只作提示不作门槛(任务书 E 包 🕓 已列此风险)。
5. 【**已落地**】`$env:DOTNET_ROOT='C:\Program Files\dotnet'` 这条已写进 `docs/dev/交接必读.md` 新增的「本机环境与构建前置」§1。User 级已于 **2026-11-28** 由主 agent 修正为 `C:\Program Files\dotnet`,但**已在运行的会话不生效**(子进程继承启动时的旧值)—— 实测同日:注册表已是新值,当前会话读到的仍是旧值,不带覆盖直接跑 `dotnet test` 依旧 `missing_runtime` 失败。**新开会话或继续用进程级覆盖。** CI 环境正常,不要把覆盖写进 workflow。

---

## 8. 与任务书的偏差说明

- 任务书 §7 AI5-0 第 1、2 步(装 Developer Pack、深修 testhost)按主 agent 指令**降级为核实并记录**,未安装任何东西、未改本机设置 —— 符合红线「不要安装需要管理员权限的东西」。
- 任务书 §4 红线 7 写「所有改动只在 worktree `C:\tipcl-fix` 里做」,与本次派工(`C:\tipcl-ai5\ai5-0-env` / 分支 `ai5/ai5-0-env`)不同。本包按**派工指令**在 `ai5-0-env` 施工,未触碰 `C:\tipcl-fix`、未触碰主工作区 `D:\项目\TipCL\TipCL`(仅用 read 工具只读读取了任务书与仓库文档)。
