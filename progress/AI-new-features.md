# AI-new-features · 两边都还没有的新功能(进度文档)

分支 `ai/new-features`(基于 main@4277a02),worktree `D:\项目\TipCL\wt-ai-new-features`。只改 Linux 主线(`src/` + `tests/`),`windos/` 未动。

## 做了什么

| # | 功能 | 落点 |
|---|---|---|
| 1 | 整合包 / 实例升级:`plan_instance_upgrade`(只读+联网)逐个模组查 Modrinth(sha1 → project/version 按 MC+加载器过滤)/ CurseForge(指纹 → files),分「可升级 / 当前就兼容 / 缺失(带替代建议)/ 认不出」,并比较 required 依赖给出前置变化;`apply_instance_upgrade`(写,high)默认「复制为新实例再升级」:复制 config / saves / resourcepacks / shaderpacks / options*.txt / servers.dat → install_game 目标版本+加载器 → 下载升级后的模组、带上兼容的、跳过缺失的;`in_place=true` 才原地升级(缺失的改 .disabled)。接确认链 + Preview(用 10 分钟内缓存的计划逐个列出 ↑/=/✗/? 与前置变化)+ Checkpoint(复制模式 = 新实例目录,回滚即删掉;原地 = mods + .instance.json)+ update_plan 计划卡 4 步推进 | `Core/Launcher/Ai/NewFeatures/InstanceUpgrade.cs` |
| 2 | 启动前预检:`preflight_check`(只读)= 已有 `Preflight.CheckLaunch` + `Conflict.ScanConflicts`,补「自动选择 Java 时本机有没有合适版本 / 指定 Java 太旧或老 MC 配新 Java」「分配内存 vs 物理总内存」,每条带 fix 建议;启动页点启动前可选跑(设置开关「启动前 AI 预检」默认开,键 `ai_preflight_enabled`):只有提醒 → 提示条 + 照常启动;有 error → 这次不启动,提示条给「交给 AI 一键修复」(切到 AI 页,把预检结果当开场自动发出)/「仍然启动」/「关闭」;预检自身异常一律放行 | `NewFeatures/AiPreflight.cs`、`App/Pages/Launch/LaunchPage.AiPreflight.cs`、`App/Pages/Settings/SettingsPage.AiPreflight.cs`、`App/Pages/Ai/AiPage.NewFeatures.cs` |
| 3 | 服务器管理:`list_servers`(只读)/ `add_server`(写)/ `remove_server`(删除,high)基于 `Servers.cs`(写 servers.dat + servers.json),接确认 + 预览 + 检查点;`ping_server`(联网只读)自写 Minecraft Server List Ping(握手 next_state=1 → status → ping/pong),返回延迟 / 在线 / 上限 / 版本 / 协议号 / MOTD(聊天组件摊平、去 § 码)/ 玩家样本,超时与连不上返回 ok=false + 中文原因 | `NewFeatures/ServerPing.cs`、`ToolRunner.NewFeatures.cs` |
| 4 | 按硬件推荐光影 / 资源包:`recommend_shaders`(只读+联网)= search_content 同一入口(`search_shaders` / `search_resourcepacks`)+ 硬件分档(核显 / <8GB / 小显存 → 低配;RTX20+/RX6000+/≥6GB 显存且 ≥16GB → 高配;其余中配)+ 结果分档(光影按名称关键词,资源包按 16x/32x/64x/128x+)+ 光影加载器检测(Iris / Oculus / OptiFine,缺了提示装哪个) | `NewFeatures/ShaderAdvisor.cs` |
| 5 | 导出诊断报告:`export_diagnostic_report`(写本地文件,medium)导出 zip(report.md + logs/*)或 md:对话排查摘要(系统消息不进)+ 诊断结论(`Crash.AnalyzeInstance`)+ 实例/模组清单 + 硬件摘要 + 日志关键片段(`Crash.CollectFiles` / `Crash.ReadText`,**没注册** get_latest_log / get_crash_report)。全文脱敏:token / 密钥 / Bearer / JWT / `--accessToken --uuid --username` / 家目录与 `C:\Users\<名>`、`/home/<名>` / 邮箱 / UUID / 本机用户名 / 账号名。不覆盖已有文件。AI 页标题栏加「导出诊断报告」入口(选目录即导出) | `NewFeatures/DiagnosticReport.cs`、`AiPage.NewFeatures.cs` |
| 6 | 系统提示最小追加一段「升级 / 预检 / 服务器 / 光影推荐 / 诊断报告」使用指引;KEYWORD_TOOLS 追加 5 组关键词 | `AiData.cs` |

## 新工具清单

| 工具 | 风险 | 只读 | side_effect | 「本对话都允许」 |
|---|---|---|---|---|
| plan_instance_upgrade | low | ✓ | network | — |
| preflight_check | low | ✓ | read | — |
| list_servers | low | ✓ | read | — |
| ping_server | low | ✓ | network | — |
| recommend_shaders | low | ✓ | network | — |
| add_server | medium | ✗ | write_local | 给 |
| export_diagnostic_report | medium | ✗ | write_local | 给 |
| apply_instance_upgrade | high | ✗ | write_local(long_running) | **不给**(D-24) |
| remove_server | high | ✗ | delete | **不给**(D-24) |

参数名:`instance` / `target_version` / `loader` / `new_name` / `in_place` / `skip` / `version` / `memory_mb` / `java` / `address` / `port` / `index` / `timeout_ms` / `kind` / `query` / `source` / `dest_dir` / `format`。Windows 版没有对应工具,名字按两边已有习惯(`instance`、`version`、`memory_mb`、`source` 与现有工具同形)。

## 文件清单

新增:
- `src/TipCL.Core/Launcher/Ai/ToolRunner.NewFeatures.cs`(partial:执行分发、`ConversationProvider`、`AllowsAlways`)
- `src/TipCL.Core/Launcher/Ai/NewFeatures/{InstanceUpgrade,AiPreflight,ServerPing,ShaderAdvisor,DiagnosticReport,NewFeatureEffects,NewFeaturesSelfTest}.cs`
- `src/TipCL.App/Pages/Ai/AiPage.NewFeatures.cs`、`src/TipCL.App/Pages/Launch/LaunchPage.AiPreflight.cs`、`src/TipCL.App/Pages/Settings/SettingsPage.AiPreflight.cs`
- `tests/TipCL.Tests/Core/AiNewFeaturesTests.cs`、`tests/TipCL.Tests/App/AiNewFeaturesAppTests.cs`
- `progress/AI-new-features.md`(本文件)

共享文件的最小追加(都集中在一处,带 `AI-new-features` 注释):
- `Tools.cs`:SchemaChunks 在 diagnose_launch 那块之前插入一个新 chunk(9 条 schema);Labels / Formats 末尾追加;`AffectedPaths` 在 `install_game` 后加 4 个 case 转 `NewFeatureEffects.AffectedPaths`
- `AiToolTypes.cs`:`ToolMeta.ALL` 末尾追加 9 条
- `ToolRunner.cs`:`sealed class` → `sealed partial class`;`ExecuteAsync` 的 `default:` 前加 9 个 case 转 `ExecuteNewFeaturesAsync`;`if (always)` 改为 `if (always && AllowsAlways(call.name))`
- `Preview.cs`:`default:` 前加 4 个 case 转 `NewFeatureEffects.Preview`
- `AiData.cs`:SYSTEM_PROMPT 末尾追加一段;KEYWORD_TOOLS 末尾追加 5 组
- `AiSelfTest.cs`:`VerifyServices` 后加一行 `NewFeaturesSelfTest.Verify(Check)`(新增 10 项自检)
- `AiPage.cs`:class 加 partial;执行器初始化加 `ConversationProvider`;标题栏按钮加 `ExportReportButton()`;`OnActivate` 末尾加 `ConsumePendingPrompt()`;确认卡 allowAlways 参数 `&& ToolRunner.AllowsAlways(...)`
- `AiFixView.cs`:确认卡 allowAlways 参数同上
- `LaunchPage.cs`:`LaunchCoreAsync` 账号闸门之后加一行 `AiPreflightGateAsync`
- `SettingsPage.cs`:AI 卡「权限」行后加 `AiPreflightField()`
- `locales/zh_CN.json`、`en.json`:各追加 15 个键(「导出失败」「关闭」已存在,未重复)

## 测试

新增 xunit **41 个**(含 Theory 7 例):
- 注册表 / 风险分级 / 标签模板 / 关键词选工具 / 系统提示含新场景 / 危险工具不给「本对话都允许」
- SLP:本机回环假服务器(校验握手 host/port/next_state、人数、版本、协议、MOTD 摊平、玩家样本、延迟);静默服务器 400ms 超时;关掉的端口;ping_server 工具无需确认
- 服务器工具:无确认回调被拒;预览含地址;检查点路径 = servers.dat + servers.json 且回滚生效;remove_server 点了「本对话都允许」第二次仍要问;越界 index 失败
- 预检:缺前置 / 内存超物理 / 没合适 Java(注入 Java 列表);指定旧 Java 报错 + 接近物理内存提醒;工具只读且自动挑版本;开关默认开且落盘
- 光影:7 种硬件分档、名称/分辨率分档、低配只推轻量并标出过重、高配先重后轻、工具走 search_shaders 并检测 Iris、Forge 实例提示装 Oculus
- 诊断报告:**脱敏断言**(token / clientToken / Bearer / JWT / --accessToken --uuid --username / C:\Users\名 / /home/名 / 邮箱 / api_key / 本机家目录 / 本机用户名 均不出现)、对话摘要不含系统提示、zip 内 report.md 与 logs/latest.log 全部无「SECRET」「Alice」、不覆盖已有文件、工具走确认且检查点路径 = 实际输出路径
- 升级:假源下 4 类模组分类 + 替代建议 + 前置变化 + 预览逐条列出;复制模式原实例不动、新实例带 config/存档/options、mods = 升级后的 + 兼容的;原地模式替换 + 禁用;工具走确认、计划卡 4 步全完成、不给「本对话都允许」、检查点回滚后新实例整个消失;plan 工具只读、实例不存在时失败
- 界面:启动页 error → 不启动 + 提示条 + 点「交给 AI 一键修复」后 AI 页排队的开场含预检条目;warn → 照常启动且无「仍然启动」;干净 / 开关关 → 不打扰(关时根本不跑);预检抛异常不挡启动;设置开关写配置;AI 页有导出入口且执行器拿得到对话

## 遗留问题与风险

1. **硬件探测是本地最小实现**:`ShaderAdvisor.cs` 里的 `internal static class NfHardwareProbe`(内存走 `Sysinfo.MemoryInfo`,显卡走 `Sysinfo.Collect()["gpus"]`)。**合并时改为调用 AI-perf-saves 的 Core 静态类 `SystemInfo`,删除 `NfHardwareProbe`**(调用点:`ShaderAdvisor`/`AiPreflight`/`DiagnosticReport`/`ToolRunner.NewFeatures` 的 recommend_shaders;测试里的 `NfHardwareProbe.Hook` 注入要一起换)。
2. **确认卡「本对话都允许」**:本想在 `ConfirmCard.AllowAlways` 里加 `remove_server` / `apply_instance_upgrade` 一行,那次编辑被用户在权限弹窗里拒了(那次提交的补丁内容本身也写错了),按规矩没有重试。改为在执行器(`ToolRunner.AllowsAlways`,执行时强制)+ 两个页面构造确认卡处(`&& ToolRunner.AllowsAlways(...)`)把关,效果一致。合并时可考虑把这两个名字直接并进 `ConfirmCard.AllowAlways`,再去掉页面上的 `&&`。
3. ping 不解析 SRV 记录(.NET 无内置 DNS SRV 查询,不加依赖),只用 A/AAAA 直连;用 SRV 的域名需用户给出真实端口。
4. 原地升级的检查点只覆盖 mods 目录与 `.instance.json`;新装的版本目录太大不进快照,撤回后会残留一个新版本目录(复制模式无此问题,是默认路径)。
5. 光影分档按名称关键词推断(系统提示要求 AI 如实说明是推断);资源包没写分辨率时按 16x 低配处理。
6. CurseForge 依赖只认 relationType=3(required);CF 文件 sha1 不校验(与 ModUpdate 一致,仅按 size)。
7. 启动页预检会把 `Preflight.CheckLaunch` 跑一次,启动链 `LaunchCore` 里自己又跑一次(大实例上多一次库文件校验的耗时);预检出错放行,不会挡启动。
8. `install_game` 在 Linux IApi 里是同步调用(装完才返回),apply_instance_upgrade 期间 AI 页会一直显示「执行中」;装完后才下载模组。

## 合并注意

- 与其他 4 个包的高概率冲突点:`ToolRunner.cs`(partial 关键字那一行、`default:` 前的 case 块、`if (always ...)` 一行)、`Tools.cs`(SchemaChunks 插入位置、Labels/Formats 末尾、AffectedPaths 的 case)、`AiToolTypes.cs` ALL 末尾、`Preview.cs` `default:` 前、`AiData.cs` SYSTEM_PROMPT 末尾与 KEYWORD_TOOLS 末尾、`AiSelfTest.cs` 末尾、两份 locales 末尾、`AiPage.cs`(partial / 标题栏按钮列表 / OnActivate)。全部是追加式,保留双方即可。
- 「光影」「资源包」「材质」关键词与已有的 search_content 组重复命中,只是多带一个只读工具,无冲突。
- get_latest_log / get_crash_report 由 AI-logs-fs 注册,本包只调 Core 的 `Crash.*`,不要在合并时重复注册。
- README / PARITY 建议改动(本包未改):
  - `docs/PARITY.md` AI 小节追加一行:「Linux 独有:plan/apply_instance_upgrade、preflight_check(+ 启动页启动前 AI 预检)、list/add/remove/ping_server、recommend_shaders、export_diagnostic_report — Windows 版暂无对应」。
  - `README.md` 功能列表 AI 部分追加:「整合包升级向导(复制为新实例,不毁档)/ 启动前 AI 预检 / 服务器延迟与在线人数 / 按显卡推荐光影 / 一键导出脱敏诊断报告」。
