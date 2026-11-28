# AI5 合并与验收进度(第 1 版 · 侦察)

- 记录时间:2026-11-28T14:25+08:00
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

## 3. 环境与基线实测(仅取得的部分)

| 项 | 实测 | 与任务书基线对比 |
|---|---|---|
| Linux Release 构建 | **0 error / 1 warning**(`src/TipCL.App/Pages/Layout/LayoutEditor.cs:196` CS0649) | 与 §6 基线「0 error / 1 warning」一致 |
| Linux `--ai-selftest` | **通过 110 / 失败 0 / 跳过 0** | 与 §6 基线「Linux 110 项」一致 |
| Linux `dotnet test` | 用修正后的 `DOTNET_ROOT` 可跑(小切片 31/31 过);全量数字待取 | 基线口径 1491 过 / 7 跳 / 0 败,待全量复核 |
| net48 参照程序集 | `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8` **仍不存在** | Windows 树本机编译**仍不可用**,相关验证继续标「待环境」 |
| Windows 树全部验证项 | 未取(环境缺失) | 待环境 |

以上构建/自检数字取自 `ai5-0-env` 的 `.ai5-logs/`(14:17-14:18),作为参考;正式基线以 AI5-0 的 `progress/AI5-0-baseline.md` 为准。

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
| 2 | net48 参照程序集缺失 | Windows 树 8 个包的构建/测试/C2 体积**全部无法本机验证** | 相关项一律标「待环境」,不硬试;需用户决定是否装 Developer Pack |
| 3 | testhost 环境变量 | 已定位并验证修法(见 §2) | 建议 AI5-0 采纳;合并验收沿用进程级覆盖 |
| 4 | 红线:禁 push、禁改版本号、禁弹窗 | 冒烟/ClickSweep/VisualTour/`--smoke`/`verify-smoke.ps1` 一律不跑 | 测试统一带 `FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl` |
| 5 | 并发写冲突 | 热点文件属主重叠 | 各分支独立 worktree,合并时逐个 review diff |

---

## 6. 待裁决(本版为空)

无。本版未做任何合并,未遇冲突。

---

## 7. 版本记录

| 版本 | 时间 | 内容 |
|---|---|---|
| 第 1 版 | 2026-11-28T14:25+08:00 | 侦察:14/14 分支 0 提交,施工中;testhost 根因定位并验证;net48 仍缺失 |
