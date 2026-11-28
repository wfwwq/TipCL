# AI5 合并与验收进度(第 2 版 · 侦察 + 环境更正 + 体积口径核实)

- 记录时间:2026-11-28T14:30+08:00
- 执行者:AI5 合并与验收子代理(qa)
- 工作区:`C:\tipcl-fix`,分支 `ai5/agent-fixes` @ `b4cd7a2`(与 `main` 同点)
- 任务书:`C:\tipcl-ai5\_shared\AI-agent-合并任务书.md`(567 行,未被 git 跟踪,合并时不得纳入提交)

---

## 1. 侦察结果:14 个施工分支全部为 0 提交

侦察命令:`git -C <worktree> log --oneline b4cd7a2..HEAD`、`git -C <worktree> status --porcelain`、`git -C <worktree> stash list`。

| # | 分包 | 分支 | worktree | HEAD | 领先 b4cd7a2 的提交 | 未提交改动 | progress 产出 |
|---|---|---|---|---|---|---|---|
| 1 | AI5-0 环境与基线 | `ai5/ai5-0-env` | `C:\tipcl-ai5\ai5-0-env` | b4cd7a2 | 0 | 0(仅 untracked `.ai5-logs/`) | 无 |
| 2 | AI5-T Linux 回放框架 | `ai5/t-linux` | `C:\tipcl-ai5\t-linux` | b4cd7a2 | 0 | 0 | 无 |
| 3 | AI5-T Windows 回放框架 | `ai5/t-win` | `C:\tipcl-ai5\t-win` | b4cd7a2 | 0 | 0 | 无 |
| 4 | A1-A8 + S(Linux) | `ai5/linux-loop` | `C:\tipcl-ai5\linux-loop` | b4cd7a2 | 0 | 0 | 无 |
| 5 | B1-B4 + P1(Linux)+ C2 来源前缀 | `ai5/linux-memperm` | `C:\tipcl-ai5\linux-memperm` | b4cd7a2 | 0 | 0 | 无 |
| 6 | C1 + C3 报告 | `ai5/linux-tools` | `C:\tipcl-ai5\linux-tools` | b4cd7a2 | 0 | 0 | 无 |
| 7 | D7/D8/D9 + S(Win)+ P2 + I 接线 | `ai5/win-agent` | `C:\tipcl-ai5\win-agent` | b4cd7a2 | 0 | 0 | 无 |
| 8 | D1/D2/D6 | `ai5/win-guard` | `C:\tipcl-ai5\win-guard` | b4cd7a2 | 0 | 0 | 无 |
| 9 | D3/D4 | `ai5/win-rollback` | `C:\tipcl-ai5\win-rollback` | b4cd7a2 | 0 | 0 | 无 |
| 10 | P1(Win)+ G 包 + B2(Win) | `ai5/win-perm` | `C:\tipcl-ai5\win-perm` | b4cd7a2 | 0 | 0 | 无 |
| 11 | E 包 CI | `ai5/ci-e` | `C:\tipcl-ai5\ci-e` | b4cd7a2 | 0 | 0 | 无 |
| 12 | R 差异清单 | `ai5/research-r` | `C:\tipcl-ai5\research-r` | b4cd7a2 | 0 | 0 | 无 |
| 13 | O 可观测性 | `ai5/observ-o` | `C:\tipcl-ai5\observ-o` | b4cd7a2 | 0 | 0 | 无 |
| 14 | I 评测集 | `ai5/intent-i` | `C:\tipcl-ai5\intent-i` | b4cd7a2 | 0 | 0 | 无 |

补充核对:

- `git branch -a --list 'ai5/*'` 15 个分支(含 `ai5/agent-fixes`)全部存在,`git worktree list` 中 14 个施工 worktree 路径齐全。
- 14 个 worktree 的 `git status --porcelain` 全为空、`git stash list` 全为空 → **没有任何可合并的提交,也没有藏起来的未提交工作**。
- `progress/AI5-*.md` 在 14 个 worktree 里都是 0 个文件 → 按任务书 §5.3 的口径,各包交付物尚未落地。
- 唯一有活动痕迹的是 `ai5-0-env`:14:17-14:18 之间写入了 `.ai5-logs/{build-linux,selftest-linux,test-linux}.log`,并有 `obj/`、`bin/` 产物(被 gitignore,故 status 干净)。

**结论:施工仍在进行中,已完成 0/14。本版不执行任何合并。** 按预备指令,侦察到此结束,等待后续指令再合并。

---

## 2. 关键发现:testhost 启动失败已定位并验证,是一行环境变量的错(阻塞全队)

任务书 §1「本机环境」第 2 条把 `dotnet test` 的 testhost 起不来记为「指向 testhost.exe 自身」的疑难问题;AI5-0 的 `test-linux.log`(14:18)复现了同样的报错:

```
Testhost process for source(s) '...\TipCL.Tests.dll' exited with error:
A fatal error occurred. The required library hostfxr.dll could not be found.
```

**根因**:用户级环境变量 `DOTNET_ROOT` 指向 `C:\Users\Administrator\.dotnet`,该目录只有遥测/哨兵文件,**没有 `host\fxr`、没有运行时**;真正的运行时在 `C:\Program Files\dotnet`(8.0.34 / 9.0.23)。

| 项 | 实测值 |
|---|---|
| `DOTNET_ROOT`(Machine) | 未设置 |
| `DOTNET_ROOT`(User) | `C:\Users\Administrator\.dotnet` ← 错 |
| `C:\Users\Administrator\.dotnet\host\fxr` | 不存在 |
| `C:\Program Files\dotnet\host\fxr` | 存在(8.0.34、9.0.23) |
| `dotnet --version` | 9.0.321 |

testhost.exe 以独立进程启动,靠 `DOTNET_ROOT` 定位运行时,于是报 hostfxr 丢失;这不是中文路径、不是沙箱、也不是 testhost 自身损坏。

**验证证据(本 worktree 实测,会话内临时覆盖进程级环境变量,未改系统设置)**:

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~AiClientTests|FullyQualifiedName~AiSchedulerTests"
# → 已通过! 失败: 0,通过: 31,已跳过: 0,总计: 31,持续时间: 590 ms
```

把 `DOTNET_ROOT` 指向真实运行时后,测试主机正常启动并执行、全过。**建议 AI5-0 在 `progress/AI5-0-baseline.md` 里直接采用该结论**:取基线时在每个跑测试的会话里先设 `$env:DOTNET_ROOT`(或把用户级变量改成 `C:\Program Files\dotnet`),不需要再排查 testhost。合并验收阶段我也将用同样方式取数,并在文档里写明「进程级覆盖,未改系统设置」。

注意:改用户级环境变量属于本机设置改动,按红线不擅自做;此处只做进程内覆盖。

---

## 3. 环境与基线实测(第 2 版已更正)

| 项 | 实测 | 与任务书基线对比 |
|---|---|---|
| Linux Release 构建 | **0 error / 1 warning**(`src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649) | 与 §6 基线「0 error / 1 warning」一致 |
| Linux `--ai-selftest` | **通过 110 / 失败 0 / 跳过 0** | 与 §6 基线「Linux 110 项」一致 |
| Linux `dotnet test` | 用修正后的 `DOTNET_ROOT` 可跑(小切片 31/31 过);全量数字待取 | 基线口径 1491 过 / 7 跳 / 0 败,待全量复核 |
| Windows Release 构建 | **0 error / 2 warning**(`windos/tests/TipCL.Tests/BridgeMutationAndLaunchGuardTests.cs:34-35` xUnit1031),18.71 s | 与 lead 给的基线一致 |
| Windows `dotnet test` | **失败 0 / 通过 796 / 跳过 6 / 总计 802**,4 s | 与 lead 给的基线一致(独立复现) |
| net48 参照程序集(Developer Pack) | 机器上没有 `v4.8` 目录,**但不需要**(见 §3.1) | — |
| Windows compact exe | **5,759,488 B**,超 D-19 红线 **2,224 B**(见 §3.2) | 任务书 §6 红线 5,757,264 B |

Linux 的构建/自检数字取自 `ai5-0-env` 的 `.ai5-logs/`(14:17-14:18),作为参考;正式基线以 AI5-0 的 `progress/AI5-0-baseline.md` 为准。Windows 数字为本 worktree 独立实测(命令见 §3.1、§3.2)。

### 3.1 环境更正:Windows 树**可以**本机编译,「待环境」的结论作废

第 1 版把 Windows 树标成「本机编译不可用」,是错的。参照程序集**由 NuGet 包提供**,不需要机器级的 net48 Developer Pack:

- `windos/src/TipCL/TipCL.csproj:53` → `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />`,已在本地 NuGet 缓存。
- 实测(pwsh,进程级 `DOTNET_ROOT` 覆盖):

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet build windos\TipCL.sln -c Release -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
# → 已成功生成;2 个警告(xUnit1031);0 个错误;18.71 s
# → 产出 C:\tipcl-fix\windos\src\TipCL\bin\Release\net48\TipCL.exe

dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
# → 已通过! 失败: 0,通过: 796,已跳过: 6,总计: 802,持续时间: 4 s
```

**结论:合并验收必须包含 Windows 树**(构建 + 测试),不再标「待环境」。`--smoke`、`tools/verify-smoke.ps1`、ClickSweep/VisualTour、任何 WPF 界面启动仍然禁止(会抢前台)。

### 3.2 体积口径核实:红线口径一致,基线**本身就超线 2,224 B**

核实对象:win-perm 报出的 compact `5,759,488 B`,与任务书 §1/§6 的「红线 5,757,264 B、最近一次合规余量 224,080 B」看似矛盾。**本节只列事实,不判合格/不合格。**

**(1) 两个数字口径是同一个产物,可以直接比**

- 红线出处:D-19(`docs/DECISIONS.md:886-889`,2026-11-07 用户拍板)= **5,757,264 B**,适用对象是 **compact 版 `TipCL.exe`**。
- 门禁脚本 `windos/tools/build-both.ps1` 量的就是同一个文件:`:85` `$exe = src\TipCL\bin\Release\net48\TipCL.exe`,`:92` `if ($bytes -gt 5757264) { exit 1 }`。
- 参数也一致:`:61-63` 的标准参数为 `-c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`,premium 才额外加 `-p:Premium=true`。
- 所以「5,759,488 vs 5,757,264」是同口径对比,不是口径打架。

**(2) 「余量 224,080 B」是 12 天前的旧记录,不属于当前 HEAD**

- 该记录出自 **D-38**(`docs/DECISIONS.md:1069-1083`,2026-11-14,task-11,当时基线 HEAD `9a3cc52`/`b19012c`):compact **5,533,184 B**,余量 224,080 B。
- 5,533,184 + 224,080 = 5,757,264 ✓,与红线自洽。
- 任务书 §1 把它写成「最近一次记录」,而 `main` 此后已从 `b19012c` 走到 `b4cd7a2`(v1.3.0、M5、i18n 词表、R32-R36、AI4 令牌等)。当前实测 5,759,488 比 D-38 记录涨 **226,304 B**。

**(3) 超线的 2,224 B 是基线既有的,与任何 AI5 包无关**

用仓库自己的权威脚本(它内部先 `dotnet build-server shutdown`、再删 `src\TipCL\{bin,obj}` 全量重建,脚本 `:9-10` 明确写「验收以全量构建为准」)在本 worktree 实测:

```
$env:DOTNET_ROOT='C:\Program Files\dotnet'
& windos\tools\build-both.ps1 -Compact
# → compact 构建: 0 错误
# → compact exe: 5759488 B
# → 报错: compact 超红线: 5759488 B > 限制 5757264 B,超出 2224 B    (exit 1)
```

而我的 worktree 相对基线只多了一个文档文件(`git diff --stat b4cd7a2 HEAD` → 仅 `progress/AI5-INTEGRATION.md`),源码与 `b4cd7a2` 逐字节相同 → **该数字就是基线的数字**。win-perm 的 5,759,488 与本机复现值逐字节相同,因此也**不能归因于 win-perm 的改动**。

**(4) 排除两个「构建侧」解释**

- **不是打包漂移**:`windos/tools/pack-core-resources.ps1 -Check` 全部 `ok`(15 个源文件 → .gz 完全同步,源 596,064 B / 压缩后 172,031 B)。
- **不是双重嵌入**(D-38 偏离提示 1 那个 +158 KB 的坑):该坑源自定义阶段 plugin-host 的**通配** `<EmbeddedResource Include="Resources\plugin-host\**\*" />`;现在的 `TipCL.csproj:97-105` 只列 `.gz`(`*.mjs.gz` / `*.js.gz` / 显式 `tipcl-plugin.d.ts.gz`),原文不会被一起嵌进去。

**(5) 增长来自哪里(线索,未定性)**

D-38 之后被改动的较大资源/代码文件(`git log -1` 实测):

| 文件 | 字节 | 最后改动 |
|---|---|---|
| `PremiumAssets\hero.jpg` | 234,735 | `745dba3` 2026-11-21 |
| `Resources\locales\en.json` | 222,623 | `3009724` 2026-11-28 |
| `Resources\locales\zh_CN.json` | 214,744 | `3009724` 2026-11-28 |
| `Services\LucideAssets.cs` | 143,325 | `2f7683e` 2026-11-14 |
| `TipCL.ico` | 94,953 | `2f7683e` 2026-11-14 |

注意 locales 两文件压缩后合计仅 128,820 B,**单靠 i18n 解释不了 226 KB**,增长是多项叠加,需要专门复核(见 §6 待办 1)。本次不做进一步定性,也不对任何包下体积结论。

**(6) 为什么能长期没人发现**

`ci-windows.yml` 里**没有任何体积门禁步骤**(grep `5757264|build-both|CompactLimit|size` 零命中)。体积门禁只活在 `windos/tools/build-both.ps1` 这个**发布前手动脚本**里 → CI 不会因基线超线变红。

**对任务书 DoD 的影响(提请裁决)**:§10 要求「compact 体积检查通过」、§4 红线 3 要求「每个包收尾都要检查」。按现口径,**基线就超线 2,224 B**,任何包都无法在不回收体积的前提下满足该条。需要 lead / 用户裁决:**是先按 B2/D-28 先例把当前基线如实记录并单独立一个体积回收包,还是要求本轮把 2,224 B 收回来**。在裁决前,体积一项对本轮所有包一律记「基线超线 2,224 B(既有)」,不判合格、不判不合格。

---

## 4. 合并计划(待各分支有提交后执行)

合并顺序按预备指令:

1. AI5-0(`ai5/ai5-0-env`,基线 + D-86)
2. AI5-T 两个(`ai5/t-linux`、`ai5/t-win`)
3. Linux 修复:`ai5/linux-loop`、`ai5/linux-memperm`、`ai5/linux-tools`
4. Windows 修复:`ai5/win-agent`、`ai5/win-guard`、`ai5/win-rollback`、`ai5/win-perm`
5. 其余:`ai5/ci-e`、`ai5/research-r`、`ai5/observ-o`、`ai5/intent-i`

用 `git merge --no-ff` 或 `cherry-pick`,保留每个包的提交粒度,不压缩。

### 已知冲突预判(合并时按此处置)

| 文件 | 冲突双方 | 处置 |
|---|---|---|
| `src/TipCL.Core/Launcher/Ai/Client.cs` | t-linux(注入点)vs linux-loop(A5 兜底) | 保留双方语义:注入点作为唯一网络入口,A5 兜底走同一份工具表 |
| `windos/src/TipCL/Core/Launcher/Ai/Agent.cs` | win-agent(D7/D8/D9/S/P2/I)vs win-perm(G 包 allow 与来源标签,L1132-1137 / L1237-1241) | 按任务书 §8「Agent.cs 串行、唯一属主」的口径,以 win-agent 为主线合并,win-perm 的 G 包语义逐处回填 |
| `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` | linux-memperm(B1-B4/P1/C2) | 单一属主,冲突时以该分支为准并复查其他分支的调用点 |
| `windos/src/TipCL/Core/Launcher/Ai/Tools.cs` | win-rollback | 单一属主 |

拿不准的冲突一律写进本文档「待裁决」小节,不猜、不丢边。

### 合并时必须剔除的内容

- 任务书 `windos/docs/改造工程:2611/AI-agent-合并任务书.md`(未被跟踪的文档)**不得纳入任何提交**;若某分支误提交,合并时剔除。
- `ai5-0-env` worktree 里的 `.ai5-logs/`(未跟踪的日志目录)不得纳入提交。

---

## 5. 风险与待办

| # | 风险 / 待办 | 影响 | 处置 |
|---|---|---|---|
| 1 | 14 个分支 0 提交,施工仍在早期 | 合并验收无法开始 | 等后续指令;本版只记录侦察 |
| 2 | ~~net48 参照程序集缺失~~ **已作废**(见 §3.1) | Windows 树可本机验证 | 用 `$env:DOTNET_ROOT='C:\Program Files\dotnet'` + 标准参数;`--smoke` 等仍禁止 |
| 3 | testhost 环境变量 | 已定位并验证修法(见 §2) | 建议 AI5-0 采纳;合并验收沿用进程级覆盖 |
| 4 | 红线:禁 push、禁改版本号、禁弹窗 | 冒烟/ClickSweep/VisualTour/`--smoke`/`verify-smoke.ps1` 一律不跑 | 测试统一带 `FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl` |
| 5 | 并发写冲突 | 热点文件属主重叠 | 各分支独立 worktree,合并时逐个 review diff |
| 6 | **compact 基线已超 D-19 红线 2,224 B**(既有,非 AI5 造成) | DoD §10「体积检查通过」在基线上就不成立;`build-both.ps1 -Compact` exit 1 | 只如实记录,不判合格/不合格;等裁决(见 §6 待办 1) |

### 待办

1. **体积**:需要定性「D-38 之后 +226,304 B 来自哪里」。建议做法:在 `b19012c`(D-38 记录点)与 `b4cd7a2` 各做一次全量 compact 构建,逐条 diff 内嵌资源清单与体积,再决定是「重新裁决红线 / 记录豁免」还是「立体积回收包」。**在定性前不要把任何包判成体积不合格**。可与 AI5-0 一起做。
2. **Windows 变体构建**:`-p:Premium=true`(premium 只记录不限值)本轮尚未实测,合并验收时补。
3. **Linux 全量测试数**:待取(需先在各会话设对 `DOTNET_ROOT`)。

---

## 6. 待裁决

1. **体积项口径**(见 §3.2(6)):基线(b4cd7a2)compact = 5,759,488 B,已超 D-19 红线 5,757,264 B 共 2,224 B,且 `windos/tools/build-both.ps1 -Compact` 在基线上直接 `exit 1`;而 CI 不跑体积门禁,所以长期未暴露。请 lead / 用户裁决:本轮是(a)沿用 D-11/D-28 先例,把基线超线如实记录、另立体积回收包;还是(b)要求 AI5 各包自证不新增体积增长,由某个包顺带回收这 2,224 B。裁决前体积一项按「基线超线 2,224 B(既有)」记录。

除此之外无待裁决项(本版未做任何合并,未遇冲突)。

---

## 7. 版本记录

| 版本 | 时间 | 内容 |
|---|---|---|
| 第 1 版 | 2026-11-28T14:25+08:00 | 侦察:14/14 分支 0 提交,施工中;testhost 根因定位并验证;net48 仍缺失 |
| 第 2 版 | 2026-11-28T14:30+08:00 | **更正**:Windows 树可本机编译/测试(参照程序集来自 NuGet,实测 0 错 2 警;测试 796/6/802),「待环境」作废;**新增 §3.2 体积口径核实**:红线口径一致,基线 5,759,488 B 超线 2,224 B,D-38 的「余量 224,080 B」是 12 天前旧记录;新增 §6 待裁决 1(体积) |
