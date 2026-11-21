# AI-perf-saves:卡顿优化 + 存档备份(分支 `ai/perf-saves`,基于 main@4277a02)

> 5 个并行 AI 包之一。只改 Linux/Avalonia 主线(`src/`、`tests/`);`windos/` 只读参照
> (`windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs` 的 get_system_info / diagnose_lag / tune_jvm / 存档四件套)。

## 1. 做了什么

README 第 55/56 行写的两个场景,Linux AI 之前没有对应工具(D-24 当时以「Linux 没落地」为由删掉了 `tune_jvm / backup_save`)。本包把 7 个工具补成**真能执行**的:

| 工具 | 风险(ToolMeta) | 只读 | 说明 |
|---|---|---|---|
| `get_system_info` | read / low | ✅ | CPU 名称与核数、内存总量/可用量(`total_mb` / `avail_mb`)、显卡、Java 摘要;附 `text`(格式照 Windows `Sysinfo.FormatText`) |
| `diagnose_lag` | read / low | ✅ | 硬件 × 实例实际分配内存 × 模组数 × 优化模组(Sodium / Lithium / FerriteCore / ModernFix / EntityCulling)× latest.log 信号(Can't keep up / OOM / Full GC / 警告刷屏)→ 结构化 `issues[]` + `suggestions[]` + `text` |
| `tune_jvm` | write_local / medium | ❌ | `apply=false` 只给建议;`apply=true` 写**实例**的 `memory_mb` + `gc_preset`(预设 auto / g1 / g1_tuned / zgc / none,照 Windows `Gc.LABELS`)。走确认卡,预览给改前/改后,检查点覆盖 `.instance.json` |
| `list_saves` | read / low | ✅ | `[{name, size_mb, modified, bytes}]` |
| `backup_save` | write_local / low | ❌ | zip 到 `<游戏目录>/backups/<存档>-<时间戳>.zip`;**游戏运行中拒绝** |
| `list_save_backups` | read / low | ✅ | `name` 参数按存档名过滤(参数名照 Windows) |
| `restore_save_backup` | **delete / high** | ❌ | `backup_name` + `overwrite`;**回滚前一律先自动备份当前同名存档**(备份失败就不回滚);**游戏运行中拒绝**;按 D-24 **不给「本对话都允许」**(确认卡不显示按钮 + 执行器即使收到 always=true 也不记) |

工具名 / 参数名 / 风险分级与 Windows 版同名同形。与 Windows 的有意差别:
- `tune_jvm` 写**实例**设置(Windows 写全局 `default_memory_mb`/`gc_preset`):任务书要求「设置实例的 memory_mb 与 gc_preset」,多实例时每个整合包各调各的;全局设置不动。参数同时认 `gc` 与 `gc_preset`。
- `restore_save_backup` 不论 `overwrite` 与否,只要有同名存档就先备份(Windows 只在 overwrite=true 时备份)。
- `diagnose_lag` 多给结构化字段(Windows 只回一段文字);`text` 字段保留 Windows 的那段文字格式。

### 1.1 Linux 实例怎么存内存 / JVM 参数,启动链是否真读(任务书第 3 条)
- 查清的现状:内存与 GC 原来只有两层 —— **版本设置** `versions/<v>/tipcl.json` 的 `memory_mb` / `gc` / `jvm_args`,与**全局** `config.json` 的 `default_memory_mb`(启动页滑块)/ `gc_preset`。实例目录的 `.instance.json`(`Instance.Meta`)只有 `java` 偏好,启动链**不读**任何实例级内存/GC。
- 补上:实例级键写在 `.instance.json`(`memory_mb` 正整数 MB、`gc_preset` 预设名),`LaunchFlow.Prepare`(`LaunchCore.RunAsync` 拼命令前调的就是它)的读取顺序改为
  - 内存:版本设置 → **实例设置** → 调用方传的 `memory_mb`(启动页 = 全局滑块值)→ **全局 `default_memory_mb`** → 4096(McLauncher 兜底);
  - GC:版本设置 → **实例设置** → 全局 `gc_preset` → auto。
- 测试 `LaunchCommand_ChangesAfterTuning`:真建版本 json + jar,用 `Java.VersionOutputHook` 冒充 Java 17,走 `LaunchFlow.Prepare` → `McLauncher.BuildLaunchCommand` 拼真命令行;调优前 `-Xmx3072M` 且无 GC 参数,`tune_jvm(apply)` 之后 `-Xmx2048M` + `-XX:+UseG1GC` + `-XX:+AlwaysPreTouch`(调优 G1 独有)。

## 2. 文件清单

新增:
- `src/TipCL.Core/Launcher/SystemInfo.cs` — 可复用硬件探测(API 见 §3)
- `src/TipCL.Core/Launcher/JvmTuning.cs` — 实例级内存/GC 读写、生效值、推荐值、卡顿诊断
- `src/TipCL.Core/Launcher/SaveBackups.cs` — 回滚计划(目标路径预先算好)+「先备份再回滚」
- `src/TipCL.Core/Launcher/Ai/PerfSavesTools.cs` — 7 个工具的 schema 与实现(静态、可单测)、AffectedPaths、自检
- `src/TipCL.Core/Launcher/Ai/PerfSavesPreview.cs` — 标签与确认卡「改前改后」预览(全部走 `I18nCore.Tr`)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.PerfSaves.cs` — `partial` 分发 +「游戏运行中拒绝写存档」(问宿主 `is_game_running`)
- `tests/TipCL.Tests/App/AiPerfSavesTests.cs` — 21 个用例

修改(都是最小追加,见 §6 冲突点):
- `src/TipCL.Core/Launcher/Ai/ToolRunner.cs` — `sealed` → `sealed partial`;分发 switch 加一组 7 个 case;`always` 那一行加 `!NeverAlwaysAllow(...)`
- `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs` — `ToolMeta.ALL` 末尾追加 7 行
- `src/TipCL.Core/Launcher/Ai/Tools.cs` — `Schemas` 合并 `PerfSavesTools.Schemas()`(1 行)、`Label` 开头 1 处转发、`AffectedPaths` 开头 1 处转发
- `src/TipCL.Core/Launcher/Ai/Preview.cs` — `ChangePreview` 的 `default:` 开头 1 处转发
- `src/TipCL.Core/Launcher/Ai/AiData.cs` — 系统提示在「# 排错流程」前插「# 卡顿与存档」3 行;`KEYWORD_TOOLS` 末尾追加 2 组(关键词照 Windows `ExtraTools.Keywords`,另加「好卡」)
- `src/TipCL.Core/Launcher/Ai/AiSelfTest.cs` — `VerifyAsync` 末尾 1 行调 `PerfSavesTools.SelfCheckAsync`
- `src/TipCL.App/Pages/Ai/ConfirmCard.cs` — `AllowAlways` 的 false 分支加 `restore_save_backup`
- `src/TipCL.Core/Launcher/LaunchFlow.cs` — `Prepare` 的内存 / GC 两行(§1.1)
- `src/TipCL.Core/Resources/locales/{zh_CN,en}.json` — 末尾各追加 26 个键(两表键集仍相等)

## 3. 给其他包复用的 API(AI-new-features 合并后可改用)

`TipCL.Core.Launcher.SystemInfo`(静态,只读;结果缓存 120 s):

| 成员 | 返回 |
|---|---|
| `PyDict Snapshot(bool force = false)` | `{collected_at, os:{system, display, description, arch}, cpu, memory, total_mb, avail_mb, gpus, java, summary}` |
| `PyDict Memory()` | `{total_mb, avail_mb, load_percent, total_bytes, avail_bytes, source}`(Linux `/proc/meminfo`;Windows `GlobalMemoryStatusEx`;都失败回退 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`,`source=dotnet_gc`) |
| `long TotalMemoryMb()` / `long AvailMemoryMb()` | 探测不到为 0 |
| `PyDict Cpu()` | `{name, cores_logical, cores_physical}`(物理核 0 = 不知道;Linux 按 (physical id, core id) 去重,不是 Sysinfo 那种按插槽数) |
| `List<object> Gpus()` | `[{name, vendor, driver, vram_mb}]`;Linux = lspci 名称 + `/sys/class/drm` 驱动/显存 + NVIDIA `/proc` 型号合并(没装 pciutils 也能出结果);Windows = CIM `Win32_VideoController`;macOS = 空表 |
| `List<object> JavaSummary(int max = 8)` | `[{name, major, path}]`(启动器管理的 Java,与 Windows get_system_info 同口径) |
| `string FormatText(PyDict snap)` | 给模型的多行文字 |
| 纯函数 `ParseMeminfo / ParseCpuinfo / ParseLspci / GpusFromDrm(root) / GpusFromNvidiaProc(root) / MergeLinuxGpus / VendorName` | 测试或别处直接喂文本 / 目录 |

`TipCL.Core.Launcher.JvmTuning`:`InstanceMemory(inst)` / `InstanceGc(inst)` / `Effective(inst, version?)`(`{memory_mb, memory_source, gc_preset, gc_label, gc_source}`,source ∈ version/instance/global/default)/ `ApplyToInstance(inst, mb, gc)` / `VersionOverrides(inst)` / `RecommendMemory(totalMb, mods)` / `RecommendGc(mb, javaMajor)` / `ModFiles(inst, version?)` / `PerfModStatus(files)` / `ReadLatestLog(...)` / `ScanLog(text)` / `DiagnoseLag(inst, version?, sys?, logText?)`。

`TipCL.Core.Launcher.SaveBackups`:`PlanRestore(inst, backupName, version, overwrite)` / `RestoreWithSafetyBackup(inst, backupName, version, overwrite, onProgress?)`。

## 4. 测试与验收(本机实测,结果见本包 complete 汇报)

- 新增 xunit **21** 个(`AiPerfSavesTests`):meminfo/cpuinfo/lspci/drm/nvidia 解析与合并、get_system_info 字段、推荐值表、生效值三层、**启动命令行随调优改变**、diagnose_lag(结构化字段 + 读实例 latest.log)、tune_jvm(只建议 / 确认卡改前改后预览 / 检查点回退 / 拒绝过大内存与非法 GC / 未知实例不建目录)、list_saves + backup_save + list_save_backups、**覆盖回滚前自动备份当前状态**(安全备份 zip 里是回滚前的 v2)、不覆盖回滚落到「-还原」「-还原2」、**游戏运行中拒绝**且不碰盘、**回滚每次都问**(always=true 也不记)、工具表/风险/关键词/系统提示。
- `--ai-selftest` 新增 11 项(不碰用户数据;「游戏运行中拒绝」用只回答 `is_game_running=true` 的假门面验)。
- i18n 守卫:未接线 CJK 字面量与 main 相同(3114 → 3114),接线 +28;新文件里给模型看的文案按 `i18n:ignore-file` 标明理由,用户可见的标签/预览全部走 `Tr`。

## 5. 遗留问题与风险

1. **实例设置覆盖调用方传的内存**:与版本设置一致,实例级 `memory_mb` 优先于 `launch_game` 参数 / 启动页滑块。启动页滑块显示的仍是全局值 —— 调过的实例从启动页启动时会用实例值(日志里「内存: X MB」那行打印的是调用方传入值,真实值看 `-Xmx`)。界面上暂时没有「实例内存」的编辑入口(版本设置对话框的「留空则用全局设置」文案没改,严格说现在是「留空则用实例/全局设置」),可在后续包补。
2. **`Prepare` 新增全局 `default_memory_mb` 兜底**:以前 AI `launch_game` 不带 `memory_mb`、联机大厅直连、崩溃页重试这些入口拿到的是写死的 4096;现在是用户的全局设置。属行为修正,但值得在合并说明里提一句。
3. `Preflight.CheckLaunch` 仍用调用方传入的内存做「内存是否超过物理内存」检查,没改成生效值。
4. `RecommendGc` 照 Windows 固定按 Java 17 推荐(因此不会主动推荐 ZGC);用户手动传 `gc=zgc` 可以。
5. `backup_save` 不进检查点(只新增 zip,不改已有文件;把整个 backups 目录塞进检查点会撞 512MB 上限并误报「无法回退」)。`restore_save_backup` 的检查点只覆盖目标存档目录;大世界(>512MB / >2 万文件)会提示「这一步的改动无法回退」—— 此时靠回滚前那份安全备份 zip。
6. 「游戏是否在运行」只认本启动器拉起的进程(`LaunchCore.IsRunning`);在启动器外开的 Minecraft 检测不到。
7. 界面上的存档对话框(`SavesDialog` → Api `restore_save_backup`)仍直接覆盖、不先备份 —— 本包没动 UI 路径;要统一可让 Api 改调 `SaveBackups.RestoreWithSafetyBackup`。
8. 硬件探测在真 Linux(Ubuntu 盒子)上没跑过;解析逻辑用真实样例文本 / 伪 sysfs 目录测过。macOS 显卡为空表。

## 6. 合并注意(冲突点)

其他 4 个包大概率也在这些位置追加,冲突都是「两边各加几行」,保留双方即可:
- `ToolRunner.cs`:类声明 `sealed partial`(多个包都会改成同一行,相同改动不冲突);分发 switch 里 `default:` 之前那组 case;`if (always && !NeverAlwaysAllow(call.name))` 这一行(若别的包也要「不给 always」,建议合并成一个判定,`PerfSavesTools.NeverAlwaysAllow` 可扩)。
- `AiToolTypes.cs` `ToolMeta.ALL` 末尾;`AiData.cs` 的 `KEYWORD_TOOLS` 末尾与「# 排错流程」之前;`AiSelfTest.cs` `VerifyAsync` 的 `return result;` 之前。
- `Tools.cs`:`Schemas` getter 里 `_schemas = cached;` 之前、`Label` 开头、`AffectedPaths` 开头 —— 三处都是一行转发。
- `Preview.cs`:`ChangePreview` 的 `default:` 开头。
- `ConfirmCard.AllowAlways` 的 false 分支。
- `locales/{zh_CN,en}.json`:末尾追加(注意上一条末尾补逗号;合并后跑 `I18nCoreLocalesTests` 确认两表键集相等)。
- `LaunchFlow.Prepare`:只有本包改。

合并后要改的文档行(本包不改 PROGRESS / DECISIONS / README):
- `README.md` 第 55/56 行:描述已与实现一致,无需改字;可在「好卡」行补「(调的是该实例的内存与 GC)」。
- `DECISIONS.md` D-24 第 217 行「`tune_jvm / backup_save … 不写进 schema`」:改为「`tune_jvm / backup_save` 已在 AI-perf-saves 落地(实例级内存/GC、先备份再回滚)」。
- `docs/PARITY.md` §2 AI 小节(第 121/129 行附近)补一条:Windows `ExtraTools` 的 get_system_info / diagnose_lag / tune_jvm / list_saves / backup_save / list_save_backups / restore_save_backup 已移植(差别见本文 §1);§6「参考实现缺陷」可补:Windows `restore_save_backup` 只在 overwrite=true 时先备份、`Sysinfo.CpuCountPhysical` 数的是插槽数。
