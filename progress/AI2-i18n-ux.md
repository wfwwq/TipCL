# AI2-i18n-ux · 上一轮遗留的 i18n 与界面问题(进度文档)

分支 `ai2/i18n-ux`(基于 main@f1c070b),worktree `D:\项目\TipCL\wt-ai2-i18n-ux`。只改 Linux 主线(`src/` + `tests/` + `progress/`),`windos/` 未动。
**本包不新增工具**(工具表仍 65 项,schema / ToolMeta / 分发三处都没加东西)。

## 0. 遗留清单(开工前从 D-81、PROGRESS 末节、progress/AI-*.md 遗留节列出)

| # | 来源 | 遗留 | 本包处理 |
|---|---|---|---|
| 1 | PARITY §10.4 第 1 条 / D-81 | 启动页预检提示条文案、新工具中文标签未接 Tr | ✅ §1.1 |
| 2 | 任务书 | 上一轮新增文件里界面可见的中文(确认卡 / 预览、设置页 MCP 与预检两行、AI 页导出诊断报告、权限面板新文案、给界面看的状态文字) | ✅ §1.1(设置页两行与导出入口上一轮已接,本包补测试锁住) |
| 3 | AI-perf-saves 遗留 1 | 界面上没有「实例内存 / GC」编辑入口;版本设置「留空则用全局设置」文案与新读取顺序不符 | ✅ §2 |
| 4 | AI-perf-saves 遗留 7 | SavesDialog 回滚直接覆盖、不先备份 | ✅ §2 |
| 5 | AI-acct-layout-mp 遗留 4 | 设置「布局」视图开着时 AI layout_apply 后方案列表 / JSON 卡不刷新;分栏比例要重新进入页面才生效 | ✅ §3(两项都做到即时生效,不需要「重新进入」提示) |
| 6 | AI-new-features 遗留 8 | apply_instance_upgrade 期间 AI 页一直「执行中」 | ✅ §4(install_game 仍同步,未改) |
| — | PARITY §10.4 其余三条(联机真机、MCP 确认框遮挡 / 状态条、真模型端到端) | 不在本包范围 | 未动 |

## 1. i18n

### 1.1 接线的入口

| 入口 | 文件 | 做法 |
|---|---|---|
| 新工具步骤行 / 确认卡标签(AI-logs-fs 6 + AI-new-features 9,含 10 条参数模板) | `Core/Launcher/Ai/I18nUxLabels.cs`(新)、`Tools.cs` | 从 `Tools.Labels / Formats` 两张静态字典搬到 switch + `I18nCore.Tr`(静态字典在类型初始化时就定死了语言,切语言改不动);`Tools.Label` 开头一行转发。老工具(原 36 个)的标签在返回前也过一遍 `I18nCore.Tr`,补了 29 条缺的 en 译文 ⇒ **65 个工具的标签在 en 下全部是英文**(有测试锁) |
| 账号 / 联机 / 排版 7 个工具标签与 4 条模板 | `AcctLayoutMpTools.cs` | `Labels / Formats` 由静态只读字典改成每次现查词表的属性 |
| 确认卡「改前改后」预览(add_server / remove_server / export_diagnostic_report / apply_instance_upgrade) | `NewFeatures/NewFeatureEffects.cs` | 全部 `Tr` + `{占位}` 替换;`↑` 行的全角冒号改成半角(全角标点在 CJK 区间里) |
| 启动页预检提示条的条目(标题 / 说明 / 建议)、摘要、「交给 AI 一键修复」开场 | `NewFeatures/AiPreflight.cs` | 全部 `Tr`;`"自动选择"` 作为数据值保持原样(与 `Instance.JavaPref` 同一约定,模型回传时 `NormalizeJava` 要认得),只在开场里显示时过词表 |
| 升级计划卡 4 个标题、安装任务卡标题 | `ToolRunner.NewFeatures.cs` | `Tr` |
| 外部 MCP 写操作确认理由 | `ToolRunner.EngineMcp.cs` | `ExternalWriteReason` 由 `const` 改成现查词表的静态属性(调用方 / 测试都按属性读,源码兼容) |
| 权限面板新文案(限定参数说明三段、两节说明、状态 / 用量行) | `App/Pages/Ai/AiPermissionPanel.cs` | `Tr`;`ModeLabels` / `BehaviorLabels` 由静态只读数组改为现查属性 |
| 确认卡标题、删除警告、「…还有 n 行」、风险标签 | `App/Pages/Ai/ConfirmCard.cs` | `Tr`;风险标签在卡片里过词表(`AiUiText.RiskText` 的中文原文被 `--ai-selftest` 断言,Core 不动) |
| 存档对话框、版本设置对话框里顺手碰到的裸中文 | `SavesDialog.cs` / `VersionSetupDialog.cs` | `Tr` |

**保持不动(给模型看的提示词 / 工具结果)**:各工具回给模型的结果与错误信息(LayoutTools、ToolRunner.AcctLayoutMp、ToolRunner.NewFeatures 的报错、InstanceUpgrade 的 reason / next_steps、DiagnosticReport 报告正文、McpServer 回外部客户端的文字)、`AcctLayoutMpTools.Keywords`(匹配用户用词)。本包没有新增任何 `i18n:ignore`(新文件 `InstanceJvmSettings.cs` 用 `i18n:ignore-file`:中文只在异常里,界面拿到异常后用自己的词表文案提示)。

注意:预检条目与确认卡预览是「界面和工具结果共用」的同一份文字 —— 现在跟界面语言走,界面是 en 时模型收到的也是英文(模型读英文没问题;只影响给模型看的措辞语言)。

### 1.2 未接线中文字面量计数(`python tools/i18n-audit.py`,本机实测)

| | 基线文件(`progress/I18N-BASELINE.md`,生成于 11-20) | f1c070b 实测(开工前) | 本包完成后 |
|---|---|---|---|
| 未接线 unwrapped | 3605 | **3540** | **3370**(−170) |
| 已接线 wrapped | 1623 | 2222 | 2395 |
| 词表条目 en / zh_CN | 3060 / 3060 | 3129 / 3129 | 3308 / 3308(两表键集相等) |

基线文件已按项目做法 `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md` 重新生成(**调低**到 3370;没有调高)。
唯一新增的 1 条未接线字面量是 `AiPreflight.BuildFixPrompt` 里与数据值 `"自动选择"` 的比较。

### 1.3 en 下不含 CJK 的守卫(测试)

`I18nUxCjk`(与 i18n-audit 同一 CJK 区间)断言 en 下:65 个工具的标签(无参 / 带参 / 空参)、确认卡预览、预检条目与开场框架句、
启动页提示条、确认卡、权限面板、设置页「启动前 AI 预检」与 MCP 两行、AI 页「导出诊断报告」、实例内存 / GC 编辑与版本设置说明、升级阶段文字、存档运行中提示 —— 全部不含 CJK;
同时断言 zh_CN 下文字与改动前逐字相同。

⚠ 写这类测试的坑:无头会话每次 `Dispatch` 都会重新初始化 App(按配置重设语言),所以切语言必须写在 `TestApp.Run(...)` 的**测试体里面**,写在外面会被重置回中文。

## 2. perf-saves 遗留

- **实例内存 / GC 编辑入口**:实例详情 → 设置 分段最前面加一行「实例内存 / GC」(副标题 = 实例层当前值 + 「n 个版本单独设置过,以版本设置为准」)+「编辑」按钮 → 对话框(内存输入框 + GC 下拉;留空 / 选「未设置(跟随全局)」= 实例不单独设置)。
  Core 新增 `InstanceJvmSettings`(`Read / ParseMemory / Save`):两项可**分别设置、分别清除**(`JvmTuning.ApplyToInstance` 只能两项一起写、不能清),校验同 JvmTuning(512~32768 MB、`Gc.LABELS` 预设),实例不存在不凭空建目录。
  对话框说明照实写读取顺序:内存 版本 → 实例 → 启动页 → 全局 → 4096,GC 版本 → 实例 → 全局 → 自动。用户亲手改的,不走 AI 确认卡。
- **版本设置对话框**:「独占内存」占位改为「留空则跟随实例 / 全局设置」,说明改为「单位 MB。留空时依次取:实例设置 → 启动页内存 → 全局默认 → 4096 MB」。
  顺带修了一个真问题:GC 下拉以前在未设置时默认选「自动」,一保存就把 `gc="auto"` 写进版本层 —— 版本层优先于实例层,tune_jvm / 实例设置的 GC 从此失效。
  现在下拉第 0 项是「跟随实例 / 全局」(写空串,LaunchFlow / JvmTuning 都按空 = 交给下一层);已经存过 `auto` 的版本不变。
- **存档还原**:`SavesDialog` 的「还原」改为调 `SavesRestore.RestoreAsync` → `SaveBackups.RestoreWithSafetyBackup`(先把当前同名存档打 zip,备份失败就不回滚;与 AI 工具 restore_save_backup 同一实现),Toast 里写出安全备份的文件名;
  游戏运行中(`LaunchCore.IsRunning`,与 AI 工具问宿主的 `is_game_running` 同源)还原按钮禁用 + 灰字提示 + 悬停提示,点击时再判一次(防竞态)。Api 的 `restore_save_backup` 方法未改(现在界面不再走它)。

## 3. acct-layout-mp 遗留

- **设置 →「布局」视图自动刷新**:`SettingsPage.LayoutLive.cs`(新 partial)在页面进可视树时订阅 `LayoutStoreV2.LayoutChanged`、离开时退订(静态事件对称 `-=`,与 BlockHost 同一纪律);
  事件可能来自线程池(AI 工具),一律投回构造时捕获的 UI 调度器并合并。用「布局 JSON 文本」做指纹:与本视图上次构建时相同(本视图自己的改动已经 `RefreshLayoutView` 过)就不动;
  不同且「布局」视图正显示 → 重建视图(方案列表、当前方案、分栏比例表、JSON 卡片都换新)。JSON 卡里用户**没应用的草稿**保留并 Toast 提示「点刷新载入最新」;本视图区块正处于编辑态时不重建(免得打断拖动)。
- **分栏比例即时生效**:`PageLayoutHost` 改 `partial`,`PageLayoutHost.Live.cs`(新)让每个分栏宿主挂在可视树期间订阅同一事件,重读本处存储比例,变了才 `SyncColumns`;
  自己拖动松手写入的那一次重读到的是同一个值 ⇒ 不重排;拖动中忽略;重新挂回可视树时补读一次。**做到了即时生效,不需要「重新进入页面后生效」提示。**

## 4. new-features 遗留:升级阶段进度

- `ToolRunner.I18nUx.cs`(新 partial):`ToolProgress(工具名, 阶段文字)` 回调;升级按 **核对模组 → 复制实例(原地模式为「准备原地升级」)→ 安装版本 → 替换模组 → 预检** 五段推进,
  每进一段报一次「第 n/5 步:xx」(走词表,同一段不重复报)。阶段跟着原有的计划卡推进点走(`Show(done)` 里加一行),计划卡仍是 4 步(既有用例锁着 4)。
- 最后一段「预检」是**真跑**:升级完对目标实例跑一遍 `AiPreflight.Check`(只读),结论进工具结果的新字段 `preflight: {ok, error_count, warn_count, summary, items(前 8 条)}`;预检自身出错只记 `preflight.ok=false, error`,不影响升级结果。
- AI 页:执行器初始化加一行 `ToolProgress = … Post(ShowToolProgress)`;`AiPage.I18nUx.cs`(新 partial)把阶段写到该步骤行右侧结果栏(状态置为 Running)和底部状态条。
- `install_game` 仍是同步的(任务书要求不改)。

## 5. 顺带修的 Core 缺陷(被本包的升级收尾预检暴露)

`Utils.OpenZip`:包不是合法 zip 时 `ZipArchive` 构造抛异常,原来那个 `FileStream` 泄漏到 GC 为止 —— Windows 上这期间文件删不掉。
全量测试里 `InstanceUpgradeTests.ApplyTool_ConfirmPlanCardAndCheckpointRollbackRemovesNewInstance` 因此变红(收尾预检的冲突扫描打开了假源写出的非 zip「jar」,随后检查点回滚删不掉新实例目录)。
修法:构造失败时先 `Dispose` 再抛。真实场景同样受益(mods 里有坏 jar 时,冲突扫描之后删模组 / 回滚会失败)。加了测试 `OpenZip_OnABrokenJar_ReleasesTheFile`。

## 6. 文件清单

新增:
- `src/TipCL.Core/Launcher/Ai/I18nUxLabels.cs` —— 上一轮新增 15 个工具的标签与模板(词表)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.I18nUx.cs` —— ToolProgress、升级阶段文字、升级收尾预检
- `src/TipCL.Core/Launcher/InstanceJvmSettings.cs` —— 实例层内存 / GC 读写(可分别清除)
- `src/TipCL.App/Pages/Ai/AiPage.I18nUx.cs` —— 阶段进度落到步骤行
- `src/TipCL.App/Pages/Library/InstanceJvmEditor.cs` —— 实例内存 / GC 一行 + 编辑对话框(表单与提交可单测)
- `src/TipCL.App/Pages/Version/SavesRestore.cs` —— 先备份再回滚 + 游戏运行中判定(独立静态类,带注入点)
- `src/TipCL.App/Pages/Settings/SettingsPage.LayoutLive.cs` —— 布局视图订阅 LayoutChanged
- `src/TipCL.App/Controls/PageLayoutHost.Live.cs` —— 分栏比例即时重读
- `tests/TipCL.Tests/Core/AiI18nUxCoreTests.cs`(14 个用例)、`tests/TipCL.Tests/App/AiI18nUxAppTests.cs`(15 个用例)
- `progress/AI2-i18n-ux.md`(本文件)

修改(都是最小改动,§7 列冲突点):`Tools.cs`、`AcctLayoutMpTools.cs`、`NewFeatures/NewFeatureEffects.cs`、`NewFeatures/AiPreflight.cs`、`ToolRunner.NewFeatures.cs`、`ToolRunner.EngineMcp.cs`、`Utils.cs`、
`AiPage.cs`、`AiPermissionPanel.cs`、`ConfirmCard.cs`、`InstanceDetailPage.cs`、`VersionSetupDialog.cs`、`SavesDialog.cs`、`SettingsPage.Layout.cs`、`Controls/PageLayoutHost.cs`、
`Resources/locales/{zh_CN,en}.json`(各追加 179 键)、`progress/I18N-BASELINE.md`(重新生成,调低)。

另:`tests/TipCL.Tests/App/ClickSweepTests.cs` 的 `SideEffectWords` 多了一行「主页 / GitHub / issue / 网页 / 浏览器 / 官网 / 链接 / 文档 / 文件夹 / 在编辑器中打开」(防止全量测试在 Windows 开发机上真的弹出浏览器)。
**这不是本包写的** —— 11:42 开工前就已经出现在本 worktree 里(主线 / 主 agent 下发的防弹窗热修),本包原样带上一起提交,合并时与主线那份是同一改动。

## 7. 合并注意(冲突点)

- `Tools.cs`:① `Labels` 字典末尾删掉了「AI-logs-fs」「AI-new-features」两块(换成一行注释),`Formats` 末尾同样;**别的包若在这两个字典末尾追加了新工具标签,冲突时保留对方的新行、删掉这两块旧行**(它们已搬到 `I18nUxLabels`)。
  ② `Label()` 在 `AcctLayoutMpTools.Label` 转发之后加了 `I18nUxLabels.Label` 转发;`FillTemplate(fmt…)` 改成 `FillTemplate(I18nCore.Tr(fmt)…)`;最后一行 `label` 改成 `I18nCore.Tr(label)`。
  新工具建议照 `I18nUxLabels` / `PerfSavesPreview.Label` 的写法(switch + Tr),不要再往静态字典里写裸中文。
- `ToolRunner.NewFeatures.cs` `ApplyUpgradeAsync`:titles 数组、`Show()` 里一行 `UpgradeStage(...)`、`TaskStarted` 标签、`return await AfterUpgradePreflightAsync(...)`。
- `AiPage.cs`:执行器对象初始化器里 `ConversationProvider` 之后一行 `ToolProgress = …`。
- `SettingsPage.Layout.cs`:`BuildLayoutView` 里 `RefreshShell();` 之后一行 `LayoutLiveOnBuilt(host);`。
- `Controls/PageLayoutHost.cs`:`sealed class` → `sealed partial class`;构造函数 `SyncColumns();` 之后一行 `WireLiveRatio();`。
- `InstanceDetailPage.cs`:设置分段(seg 4)开头两行。`VersionSetupDialog.cs`:GC 下拉数组多了第 0 项(`GcValues[0] = ""`),默认选中逻辑随之改。
- `AiPermissionPanel.ModeLabels` 由字段改为属性(读法不变;`AiPage.cs:220`、`AiToolsTests` 照常编译)。`ToolRunner.ExternalWriteReason` 由 const 改为属性(若别的包在 `switch case` / 特性里用它会编译不过 —— 现有代码没有)。
- `Utils.OpenZip`:只改函数体。
- `locales/{zh_CN,en}.json`:末尾追加 179 键(注意上一条末尾补逗号);合并后跑 `I18nCoreLocalesTests`。
- `progress/I18N-BASELINE.md`:**合并后重跑一次** `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md`(别的包也可能改了计数;只能调低或持平)。

## 8. 测试与验收(本机实测,数字见 complete 汇报)

- 新增 xunit **29 个**(Core 14 + App 15),覆盖 §0 每一项。
- `--ai-selftest`:91 项全过(本包未加自检项)。

## 9. 遗留问题与风险

1. 启动页提示条里来自**既有** `Preflight.CheckLaunch`(缺版本 / 库 / 磁盘…)的条目标题与说明、`Conflict.ScanConflicts` 的说明仍是中文(那两个 Core 文件 67 + 9 条未接线,不是上一轮新增,不在本包范围);本包加的那几类(Java 匹配、物理内存、模组冲突的标题与建议)已是双语。
2. 老工具的中文标签现在**运行时**会翻成英文,但字面量还写在 `Tools.Labels` 字典里,审计工具仍按「未接线」计(Tools.cs 从 112 降到 87,剩下的 87 条里 58 条是这两张字典);要让计数也降下来,得把整张字典改成 Tr switch(会和别的包在字典末尾的追加冲突,本包没做)。
3. AI 页里「准备:」「完成:」「权限:」「对话失败:」前缀与 `AiUiText` 的状态词仍是中文(既有文件,且 `--ai-selftest` 断言了中文原文);静态只读数组(`VersionSetupDialog` 的隔离 / GC 下拉、`SavesDialog.MediaLabels`)在类型初始化时定语言,切语言要重启 —— 与项目既有写法一致。
4. 升级收尾多跑一次预检,大实例上会多花几秒(库文件校验);预检结论只放进结果,不改变 `ok`。
5. 存档对话框本身(`Dlg.Panel` 模态)没有无头端到端用例;「先备份再回滚」「运行中拒绝」由 `SavesRestore` 的用例覆盖,按钮禁用逻辑只有一行赋值。
6. 布局视图在本视图区块编辑态时不自动刷新(下一次变更或重新进入时刷新);JSON 草稿被保留时界面以草稿为准,需要用户点「刷新」。

## 10. 合并后要改的 README / PARITY / DECISIONS 行(本包按约定未改)

- `docs/PARITY.md` §10.4 第 1 条「启动页预检提示条的文案(`NewFeatures/AiPreflight.cs`)与新工具的中文标签(`Tools.cs` Labels)未接 `I18nCore.Tr`」→ 改为已接(AI2-i18n-ux;残留见本文 §9-1/2)。
- `docs/PARITY.md` §10 补一条:Linux 存档对话框的还原 = 先自动备份再回滚(Windows 版界面直接覆盖);实例详情 → 设置 可编辑实例内存 / GC。
- `DECISIONS.md` 建议新增一条:「工具结果里与界面共用的文字(预检条目、确认卡预览)跟界面语言走」;D-81 的「遗留」改为指向本文件。
- `progress/AI-perf-saves.md` 遗留 1、7,`progress/AI-acct-layout-mp.md` 遗留 4,`progress/AI-new-features.md` 遗留 8 可标「已由 AI2-i18n-ux 处理」。
- `README.md`:无需改动(功能描述未涉及这些细节);如要写,可在「好卡」行补「实例内存 / GC 可在实例详情 → 设置里改」。
