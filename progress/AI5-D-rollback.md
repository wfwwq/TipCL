# AI5-D-rollback:回滚覆盖(D3)与预览覆盖(D4)

> 包号:AI5-D3、AI5-D4(合并一份 progress,代码分两组提交)
> 分支 / worktree:`ai5/win-rollback` @ `C:\tipcl-ai5\win-rollback`(基线 `b4cd7a2`)
> 日期:2026-11-28(ISO 8601)/ 落地人:win-rollback

---

## 0. 一句话结论

两条缺陷都**真编译、真测试**验完:Windows 树 `-t:Rebuild` 0 error / 2 warning(与基线逐条相同),
全量测试 **823 通过 / 6 跳过 / 0 失败 / 总 829**(基线 802,净增 27 = D3 13 条 + D4 14 条)。
compact exe **5,770,240 B**(基线 5,759,488 B,**+10,752 B**:代码 + 词表 gzip 增长,如实记录,不做回收)。

---

## 1. 做了什么

### D3 · 回滚不覆盖若干写工具(提交 `f03dc9c`)

**缺陷**:`Tools.AffectedPaths` 的 switch 只认基础工具,`tune_jvm` / `restore_save_backup` / `layout_apply`
落到 `default` 分支返回空清单 → `Checkpoint.Snapshot` 没有任何路径可拍 → 检查点日志里**没有这一轮的操作**
→ 用户点「撤回上一轮」时界面走「这一轮没有可还原的磁盘改动」/「已撤回」分支,**磁盘上一个字都没还原**。

**修法**(照 Linux 分包实现移植,`PerfSavesTools.AffectedPaths:290` / `AcctLayoutMpTools:192` /
`LayoutTools` / `NewFeatureEffects:29`):

| 工具 | 现在给的路径 | 取舍 |
|---|---|---|
| `tune_jvm` | `apply=true` → 全局 `<TIPCL_HOME>/config.json`;`apply=false` → 空 | Windows 版写的是**全局** `memory_mb/gc_preset`(`ExtraTools.TuneJvm → backend.save_settings`),不是 Linux 的实例 `tipcl.json`:照真实落盘位置给 |
| `restore_save_backup` | `overwrite=true` → `saves/<存档>`;`false` → 执行时真正新建的那个名字(`world-还原` / `world-还原2` …,与 `Saves.RestoreBackup` 的避让规则同一段逻辑) | 回滚前自动打的安全备份 zip **不进**清单(它就是给人留的后路);不存在的目录在检查点里记为「回退时删掉」,正是要的效果 |
| `layout_apply` | `ui_layout_v2.json` + `ui_page_layouts.json` | 与实例无关,所以排在实例解析**之前**(实例解析失败也不丢快照) |

**顺带修了同类的一条**(任务书 D3 没写,属「路径正确」):`delete_instance` 以前返回的是**默认实例**目录,
不是参数里的 `name` —— 快照拍到了别的实例上,真被删的那个实例没进检查点。现在按 `name` 解析,`name` 缺失返回空。

**不可回退的说清楚**:写盘但有意不做快照的写工具进**显式豁免表**(`Tools._NO_SNAPSHOT`,每条带理由):
`backup_save`(只新增 zip;整个 backups 目录进检查点会撞 512 MB 上限)、`multiplayer`(只驱动组件/进程)、
`add_offline_account`/`switch_account`/`set_skin`(账号文件里是封存过的凭据,回退旧文件会冲掉之后登录的账号)、
`launch_game`(只起进程)、`remember`/`forget`(逐条增删,不做整文件快照)。
配套出口:`Tools.SnapshotCoverage(name)`(`snapshot`/`exempt`/`none`)、`Tools.NoSnapshotReason(name)`、
`Tools.NoRollbackMessage(name)` →「这一步无法回退:{理由}」(词表已接线,en 有译文)。

### D4 · 预览覆盖落后于工具表(提交 `deb6b8b`)

**缺陷**:`Preview.ChangePreview` 没有 `fs_write` / `layout_apply` / `restore_save_backup` / `tune_jvm` 分支,
这四个写工具点「允许」之前看不到任何东西。

**修法**(参考 Linux `Preview.cs` / `AiFsTools.WritePreview` / `PerfSavesPreview` / `NewFeatureEffects.Preview`):

| 工具 | 现在预览什么 |
|---|---|
| `fs_write` | 逐行 unified diff + 目标绝对路径;新建/追加分行说明;超过 200 行落盘并提示。目标路径与执行走**同一套** `ExtraTools.FsResolve` 守卫,越界直接不给预览(执行时同样会被拒) |
| `tune_jvm` | 改前(全局默认)→ 改后(内存 + GC)四行、"写入:{config.json}"、版本单独设过内存/GC 的提示;`apply=false` 说清「只给建议」;内存超上限 / gc 名字不合法说清「这次不会执行」 |
| `restore_save_backup` | 备份文件名、备份在不在、目标存档名、覆盖还是新建、覆盖时「回滚前先把当前存档备份成 zip」。目标路径与 `Tools.AffectedPaths` **同源**(检查点拍的就是它) |
| `layout_apply` | 新增 `LayoutTools.PreviewPlan`(`PyDict`):在副本上干跑同一份 `ApplyOps` → 改动清单 + 改前/改后整份 v2 文档 JSON → 逐行 diff;认不出改动时说「这次改动用不了」,不给空 diff。**不落盘、不碰 profiles、不动缓存** |

另外,`ChangePreview` 外面包了一层:凡是 D3 的豁免工具(AffectedPaths 为空、撤回还原不了任何东西),
确认卡上自动多一行「这一步无法回退:…」;没有专属预览的工具也有这一行兜底(卡不会是一片空白)。

**显式豁免表**(`Preview._NO_PREVIEW`,每条带理由):`backup_save`、`multiplayer`、`add_offline_account`、
`switch_account`、`set_skin`、`remember`、`forget`。`Preview.HasBuiltinPreview` / `NoPreviewReason`
把这张表开放给用例,两个方向都锁:写工具要么有专属预览、要么有理由;豁免表里每条都必须有理由,
且不许和「有预览」重叠。

---

## 2. 文件清单

| 文件 | 改动 | 属主 |
|---|---|---|
| `windos/src/TipCL/Core/Launcher/Ai/Tools.cs` | +212/-5:三张表(`_SNAPSHOT_TOOLS` / `_NO_SNAPSHOT`)、`SnapshotCoverage` / `NoSnapshotReason` / `NoRollbackMessage`、`AffectedPaths` 拆成 (backend) / (实例名) / (Instance) 三个入口 + `_restore_targets` + `_game_dir`;`delete_instance` 按 `name` 解析 | 本包(唯一属主) |
| `windos/src/TipCL/Core/Launcher/Ai/Preview.cs` | +334:`ChangePreview` 包装(补「无法回退」行)+ `_change_preview` + `FsWritePreview` / `TunePreview` / `RestorePreview` / `LayoutPreview` + 预览豁免表 | 本包(唯一属主) |
| `windos/src/TipCL/Core/Launcher/Ai/LayoutTools.cs` | +20:新增只读 `PreviewPlan(args)`(干跑 `ApplyOps` + 导出改前改后 JSON) | 本包(小改,**非**任务书指定的属主文件,见 §6 遗留) |
| `windos/src/TipCL/Resources/locales/{zh_CN,en}.json` | 各 +34 条词条(新文案 + 理由 + 补上既有代码里一直缺的预览文案),并重跑 `windos/tools/pack-core-resources.ps1` 重新生成两个 `.gz` | **共享文件**,见 §6 |
| `windos/tests/TipCL.Tests/ToolsRollbackTests.cs` | 新增 13 条用例(D3) | 本包 |
| `windos/tests/TipCL.Tests/PreviewCoverageTests.cs` | 新增 14 条用例(D4 + D3 的 UI 文案) | 本包 |

提交:

```
f03dc9c ai(agent): AI5-D3 补齐写工具的回滚覆盖,豁免表外不放过任何一个
deb6b8b ai(agent): AI5-D4 补齐确认卡预览覆盖,fs_write / 调优 / 回滚存档 / 布局各有专属预览
docs(agent): AI5-D 进度(本文件)
```

D3 单独编译 + 全量测试验过一次(见 §4「中间提交」),D4 在最终树上验证。

---

## 3. 覆盖了哪些工具

**回滚(检查点)覆盖** —— 18 个写工具给路径 + 8 个写工具显式豁免,`ToolMeta` 里每个 `ro=false` 的工具
都在两张表之一(用例 `EveryWriteToolIsCoveredOrExplicitlyExempt` 锁住):

- 新增有快照(3):`tune_jvm`、`restore_save_backup`、`layout_apply`
- 顺便修正(1):`delete_instance`(按 `name` 而不是默认实例)
- 原本就有、这次逐个断言没坏(14):`write_mod_config`、`delete_mod`、`enable_mod`、`disable_mod`、`create_instance`、`install_mod`、`install_shader`、`install_resourcepack`、`install_datapack`、`install_world`、`install_modpack`、`install_game`、`download_java`、`fs_write`
- 显式豁免(不可能/不该快照,6+2):`backup_save`、`multiplayer`、`add_offline_account`、`switch_account`、`set_skin`、`launch_game`、`remember`、`forget`

**预览覆盖** —— 19 个写工具有专属预览分支,7 个走显式豁免表:

- 本次新增(4):`fs_write`、`tune_jvm`、`restore_save_backup`、`layout_apply`
- 原本就有(15):`write_mod_config`、`delete_mod`、`disable_mod`、`enable_mod`、`delete_instance`、`create_instance`、`install_*`(6)、`install_game`、`download_java`、`launch_game`
- 显式豁免(7):`backup_save`、`multiplayer`、`add_offline_account`、`switch_account`、`set_skin`、`remember`、`forget`(确认卡仍有「这一步无法回退」那一行)

---

## 4. 复现 → 修复 → 转绿 三步证据

### 第 1 步 · 复现(变异验证,证明用例真的抓得住这类回归)

在**已修好的树**上做两处定向变异(改完真编译、真跑测试,事后按字节哈希核对还原):

| 变异 | 模拟的旧行为 | 结果 |
|---|---|---|
| `case "restore_save_backup": return new List<string>();` | 改造前 `default` 分支返回空清单 | 见下 |
| `NoRollbackMessage` 无脑 `return "";` | 改造前界面不说「这一步无法回退」 | 见下 |

```
dotnet build … -m:1 (0 error) && dotnet test --filter "~ToolsRollbackTests|~PreviewCoverageTests"
失败!  - 失败: 7,通过: 19,总计: 26
  失败 ToolsRollbackTests.RestoreSaveBackupSnapshotsTargetWorld
  失败 ToolsRollbackTests.RestoreSaveBackupWithoutOverwriteSnapshotsNewFolder
  失败 ToolsRollbackTests.EveryRegisteredSnapshotToolReturnsPaths
  失败 ToolsRollbackTests.ExemptWritesDoNotClaimSnapshots
  失败 PreviewCoverageTests.RestoreSaveBackupPreviewShowsTargetWorld
  失败 PreviewCoverageTests.ExemptWritesTellTheUserItCannotBeRolledBack
  失败 PreviewCoverageTests.SnapshotToolsDoNotShowRollbackWarning
```

> 踩过的坑(记一笔,给后面的人):变异前我用 `Copy-Item` 备份、改完再拷回 —— **拷贝保留源文件旧时间戳**,
> MSBuild 因此静默跳过重编,一度出现「源码已还原、测试还是 7 红」的假红。用 `-t:Rebuild`(或先
> `(Get-Item f).LastWriteTime = Get-Date`)后确认终态。后续所有验证都用 `-t:Rebuild`。

### 第 2 步 · 修复

见 §1。两组提交;`AffectedPaths` 的改动只加分支,不改既有分支的返回(既有 14 个工具的路径由用例逐个钉住)。

### 第 3 步 · 转绿(最终树,`-t:Rebuild`)

```
$env:DOTNET_ROOT='C:\Program Files\dotnet'
dotnet build windos\TipCL.sln -c Release -t:Rebuild -m:1 -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false
  → 已成功生成。0 error / 2 warning(xUnit1031 ×2,与基线逐条相同)

dotnet test windos\tests\TipCL.Tests -c Release --no-build --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
  → 已通过! 失败: 0,通过: 823,已跳过: 6,总计: 829

(Get-Item windos\src\TipCL\bin\Release\net48\TipCL.exe).Length
  → 5770240
```

**中间提交也可单独编译测试**(D4 的改动 stash 掉之后重跑):
`已成功生成(0 error / 2 warning)`、`失败 0,通过 809,跳过 6,总计 815`(基线 802 + D3 的 13 条)。

---

## 5. 验收数字

| 判据 | 基线(AI5-0) | 本包后 | 结论 |
|---|---|---|---|
| Windows Release 构建 | 0 error / 2 warning | 0 error / 2 warning | 不新增警告 |
| `dotnet test`(带安全过滤) | 796 过 / 6 跳 / 0 败 / 总 802 | **823 过 / 6 跳 / 0 败 / 总 829** | 净增 27 条用例,0 失败 |
| D3 用例数 | — | 13(`ToolsRollbackTests`) | 含「逐工具断言路径」「豁免表」「覆盖率双向锁」「backend 入口与实例入口一致」 |
| D4 用例数 | — | 14(`PreviewCoverageTests`) | 含四个新分支各自的内容断言 + 豁免表两个方向 + 「预览不落盘」 |
| compact exe | 5,759,488 B(已超红线 2,224 B) | **5,770,240 B(+10,752 B)** | 见 §6:不含恢复,验收与否交用户裁决 |
| `--ai-selftest`(Windows) | — | **未跑** | 见 §6:该入口经 WPF `App` 启动(任务书 §E 记的「不能无窗口跑」),本轮红线禁止启动 WPF 界面 |
| smoke / 体积红线判定 | — | **未跑 / 未判** | 按分工留给 AI5-0 与用户;本包只报实测字节数 |

体积增量的构成(便于裁决):词表 gzip `en+1,492 B`、`zh_CN+949 B`,其余 ≈8.3 KB 是新增代码
(`Preview.cs` +334 行、`Tools.cs` +212 行)与新增用例的 IL/PDB 不计入 exe 的部分。纯增代码的包,
没有为压体积砍覆盖。

---

## 6. 遗留问题与需拍板

1. **体积**:compact 5,770,240 B > 红线 5,757,264 B(基线本身已超 2,224 B,本包再 +10,752 B)。
   按要求**不做回收、也不下结论**,数字交用户裁决。若最终要压,可考虑的最小切入点:预览文案与豁免理由
   合并词条(省不到 1 KB)、`Preview.cs` 的 `MatchingBlocks/Opcodes/UnifiedDiff` 与 Linux 树同源但各存一份
   (收敛属 R 包的合并调研,不在本包范围)。
2. **任务书没覆盖、我自行决定的两处**,请复核:
   - **`delete_instance` 的实例来源**(§1):原实现拿默认实例,与「删的是 name」不一致。我改成按 `name`。
   - **`LayoutTools.cs` 新增 `PreviewPlan`**:这是 `layout_apply` 预览能给出**真实 diff** 的前提
     (Linux 的做法就是 `LayoutTools.Plan` + `Summary`)。该文件不在任务书给我指定的两个热点文件里,
     若已有别的人认领,请以他们的版本为准并告诉我如何改接。
3. **`windos/src/TipCL/Resources/locales/{zh_CN,en}.json` 是共享文件**:本包追加了 34 条(含补上既有代码里
   一直缺失的预览文案),并按仓库规矩重跑 `windos/tools/pack-core-resources.ps1` 重生成 `.gz`
   (只改源不重跑该脚本,`CoreResourcesPackTests` 会当场红 —— 我第一轮就是这么红的)。
   合流时这两个 JSON 是**最可能冲突**的点,建议按「追加行」合并后重跑一次打包脚本。
4. **记忆文件的检查点**:`remember`/`forget` 现在(Windows)标成只读,`ToolMeta` 未改(B2 包会改)。
   我按「即使将来变成 `write_local` 也仍是显式豁免」的口径登记了理由,否则 B2 一落地
   「每个写工具必须在两张表之一」的用例会红。B2 落地后如需给记忆做快照,改 `_NO_SNAPSHOT` 一行即可。
5. **未验证项**:`--ai-selftest`(Windows 需 WPF 宿主,红线禁止本机拉起界面)。若 AI5-0/E 包能在 CI 里
   无窗口跑通,请把自检里「写工具都有检查点覆盖」这类项也接上(现在只有用例在守)。
6. **plugin 工具**:`G` 包要给声明了 `write_local` 的插件工具加 `affectedPaths`(DESIGN.md 的新字段)。
   插件工具的路径不走 `Tools.AffectedPaths`(名字不在两张表里),`SnapshotCoverage` 对它返回 `none` ——
   我**没有**把「表外的写工具」一刀切成红,而是按 `ToolMeta` 的写工具集合锁,插件工具不在 `ToolMeta` 里,
   所以不会误红;G 包落地后建议在插件工具那条链上单独加一个类似的小表。

---

## 7. 要改的文档(建议,本包未改 docs/)

- `docs/DECISIONS.md`:建议追加一条 D-8x,记录「写工具的回滚覆盖 / 预览覆盖都有显式豁免表,表由用例守」
  以及 D3 顺带修正的 `delete_instance` 口径(若用户认这个改法的话)。
- `docs/ai-agent-parity.md`(R 包):两棵树在这两张表上的差异 —— Linux 的豁免理由写在各自分包文件里
  (`PerfSavesTools` / `AcctLayoutMpTools`),Windows 收敛到 `Tools._NO_SNAPSHOT` / `Preview._NO_PREVIEW`;
  Windows 的 `tune_jvm` 写全局、Linux 写实例。
