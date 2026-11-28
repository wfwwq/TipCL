# AI3-i18n · 剩下的 i18n 残留(进度文档)

分支 `ai3/i18n`(基于 main@b6df4bb),worktree `D:\项目\TipCL\wt-ai3-i18n`。只改 Linux 主线(`src/` + `tests/` + `tools/i18n-audit.py` + `progress/`),`windos/` 未动。
**本包不新增工具**(工具表仍 78 项,schema / ToolMeta / 分发三处都没加东西),没有新增 NuGet 依赖、没有新增 `i18n:ignore`。

来源:`docs/PARITY.md` §11.4 第 1 条、`progress/AI2-i18n-ux.md` §9-1/2/3。

## 1. 做了什么

### 1.1 启动页预检提示条里来自老代码的条目(§9-1)

| 文件 | 做法 |
|---|---|
| `src/TipCL.Core/Launcher/Preflight.cs` | `CheckLaunch` 及 `_check_libraries / _check_assets / _check_natives / _check_mod_conflicts` 的全部标题与说明走 `Tr`(带 `{占位}` 的模板 + 文件内 `Tr(key, (name, value)…)` 小工具;数值用 `Py.Str` 填 —— `Py.S` 会把 0 变成空串)。`code` / `level` 是数据,不翻。 |
| `src/TipCL.Core/Launcher/Ai/Conflict.cs` | `ScanConflicts` 的 5 种 `issues[].message`(重复 / 加载器不匹配 / 缺 Fabric API / 缺依赖 / 互斥)走 `Tr`;`type / id / need / file` 不动。 |

- 顺手修了一个会被翻译打坏的判定:`_check_mod_conflicts` 以前靠「文字里含不含 `Fabric API`」把缺 Fabric API 的条目分出来,现在缺 Fabric API 的直接收进单独的列表,不再看文字。
- **口径(D-82)**:这些文字同时是 `preflight_check` / `scan_mod_conflicts` 工具结果的一部分,与界面共用 ⇒ 跟界面语言走;界面是 en 时模型收到的也是英文。中文界面下 `Tr` 返回原文 ⇒ 与改动前**逐字相同**(测试按改动前的拼接方式手写了 17 类条目的期望串)。
- 仍保留的唯一一条中文字面量:`java_exe != "自动选择"` —— 这是数据约定值(与 `Instance.JavaPref`、`AiPreflight.NormalizeJava` 同一约定),不是文案。

### 1.2 AI 页步骤行前缀(§9-3 前半)

| 文件 | 做法 |
|---|---|
| `src/TipCL.App/Pages/Ai/AiPage.cs` | `"准备:" / "完成:"` 前缀、`"权限:"`、`"对话失败:"` 与「AI 对话失败」Toast 标题、步骤行结果栏「n 条结果」走 `Tr` |
| `src/TipCL.App/Pages/Ai/AiFixView.cs` | 同上口径(`准备: / 完成: / 修复会话失败: / AI 修复失败`) |
| `src/TipCL.App/Pages/Ai/StepGroup.cs` | 步骤组右侧计数「· n 步 / · n 步失败」走 `Tr` |
| `src/TipCL.Core/Launcher/Ai/AiUiText.cs` | `StripState` 在原来的中文前缀之外,再认**当前界面语言下**的译文前缀(`Preparing: / Running: / Done: / Skipped: / Failed:`;中文界面下不剥英文,避免误伤标签);`StepSummary`(「正在 X」「执行了 N 步 · 用时 Xs」)走 `Tr` |

`--ai-selftest` 里断言的中文原文(`StripState("执行中: 装模组 钠")`、`StepSummary(...).Contains("3 步")`、`RiskText`)都没变:自检在默认中文下跑,`Tr` 返回原文;`StatePrefixes` / `RiskText` 的中文原文数组没动。

### 1.3 启动时就定死语言的下拉框(§9-3 后半)

| 下拉 | 做法 | 效果 |
|---|---|---|
| `VersionSetupDialog` 隔离 / GC | `IsoLabels` / `GcLabels` 由静态只读数组改为每次现查词表的属性;对话框里取一次存局部变量(选中项与列表同一份) | 对话框每次打开都重建 ⇒ **切语言后下一次打开就是新语言**,不用重启;保存按 `SelectedIndex` 取值,与文案语言无关 |
| `VersionSetupDialog` 绑定账号「(不绑定)」、统一通行证占位、隔离说明 | 走 `Tr`(`NoAccountLabel` 属性);保存本来就按 `SelectedIndex <= 0` 判 | 同上 |
| `SavesDialog` 媒体下拉(截图 / 崩溃报告 / 日志) | `MediaLabels` 改为属性 + `MediaKinds` / `MediaKindAt(index)` 按**下标**映射目录 | 同上;顺带修了真缺陷:以前拿「选中的文字」去和 `Tr("崩溃报告")` 比,下拉定在中文而界面切到 en 时,选崩溃报告 / 日志也会读截图目录 |

**做不到即时刷新的(如实)**:页面级的静态下拉/分段文案 —— `DownloadPage.Segs / FilterNames`、`CatalogPage.Sources`、`InstallPage.Names`、`InstanceDetailPage.Segs`、`FeedbackView.Categories`。原因:`MainWindow` 把页面实例缓存到进程结束(`_pages.GetOrAdd`),控件只建一次;只把数组改成现查属性而页面不重建,会让「控件里的旧语言文字」与「按新语言算出的数组」对不上(这些页面有按文字 / 下标互查的地方),反而引入错位。切语言时设置页已有的 Toast「下次启动启动器后界面才会变成新语言」覆盖了这类情况(本包没改它);对话框类(上表)现在不再需要重启。

### 1.4 审计脚本:运行时过 Tr 的老工具标签计为已接线(§9-2)

- `tools/i18n-audit.py` 新增标记 `i18n:runtime-tr`:行内出现该标记,或位于 `i18n:runtime-tr-begin` … `i18n:runtime-tr-end` 之间的 CJK 字面量计为 **wrapped**(callee 记为 `(runtime-tr)`)。含义:字面量本身不是 `Tr(...)` 的实参,但所在的表**取出后一定过 `I18nCore.Tr`**。被标记的字面量同样进入 `wrapped_keys`,缺 en 译文会照常出现在 `missing_en` 里。`--json` 多两个字段 `runtime_tr_wrapped` / `runtime_tr_files`,文字报告多一行。
- 白名单 `RUNTIME_TR_FILES` = `Ai/Tools.cs`(`Labels` / `Formats` 两张字典,`Tools.Label` 返回前 `I18nCore.Tr(label)` / `FillTemplate(I18nCore.Tr(fmt)…)`)、`Ai/AiUiText.cs`(`StateKeys`,`StripState` 里逐个过 `Tr`)。
- 守卫:`tests/TipCL.Tests/Repo/I18nRuntimeTrMarkerTests.cs` —— 用到标记的文件必须正好等于白名单、begin/end 成对、`Tools.Label` / `StripState` 里那两处 `Tr` 调用还在。行为层面:Tools.cs 由 AI2 的 `AiI18nUxLabelTests.EveryToolLabel_InEnglish_HasNoCjk_*` 锁,AiUiText.cs 由本包 `StepPrefixesAndSummary_*` 锁。
- 顺带补了 1 条上一轮漏掉的 en 译文(`按钮不能打开这个页面`,LayoutTools / WidgetCommon 已经 `Tr` 了但词表缺键)⇒ `missing en` 1 → **0**。

### 1.5 未接线计数(`python tools/i18n-audit.py`,本机实测)

| | 基线文件(`progress/I18N-BASELINE.md`,11-21 12:24 生成) | b6df4bb 实测(开工前,旧脚本) | 本包完成后 |
|---|---|---|---|
| 未接线 unwrapped | 3370 | **3366** | **3211**(−155) |
| 已接线 wrapped | 2395 | 2479 | 2615(其中 `(runtime-tr)` 63) |
| CJK 字面量(分母) | 5832 | 5960 | 5941 |
| 词表 en / zh_CN | — | 3377 / 3377 | 3451 / 3451(两表键集相等,各 +74) |
| missing en / zh | — | 1 / 1 | 0 / 0 |

按文件(未接线,前 → 后):`Preflight.cs` 67 → 1、`Conflict.cs` 9 → 0、`Ai/Tools.cs` 87 → 29(字典 58 条改计已接线)、`AiPage.cs` 45 → 37、`AiFixView.cs` 11 → 7、`AiUiText.cs` 18 → 14、`VersionSetupDialog.cs` 4 → 0、`StepGroup.cs` 2 → 0、`SavesDialog.cs` 1 → 1。

基线文件已按项目做法 `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md` 重新生成:**3370 → 3211(调低,没有调高)**;`--selfcheck` PASS(389 个文件)。

## 2. 文件清单

新增:
- `tests/TipCL.Tests/Core/AiI18nResidueCoreTests.cs`(5 个用例)
- `tests/TipCL.Tests/App/AiI18nResidueAppTests.cs`(4 个用例)
- `tests/TipCL.Tests/Repo/I18nRuntimeTrMarkerTests.cs`(2 个用例)
- `progress/AI3-i18n.md`(本文件)

修改:`src/TipCL.Core/Launcher/Preflight.cs`、`src/TipCL.Core/Launcher/Ai/Conflict.cs`、`src/TipCL.Core/Launcher/Ai/AiUiText.cs`、`src/TipCL.Core/Launcher/Ai/Tools.cs`(只加 3 行注释/标记)、
`src/TipCL.App/Pages/Ai/AiPage.cs`、`AiFixView.cs`、`StepGroup.cs`、`src/TipCL.App/Pages/Version/VersionSetupDialog.cs`、`SavesDialog.cs`、
`src/TipCL.Core/Resources/locales/{zh_CN,en}.json`(末尾各追加 74 键)、`tools/i18n-audit.py`、`progress/I18N-BASELINE.md`(重新生成,调低)。

## 3. 测试(新增 11 个)

- `AiI18nResidueCoreTests`(放进不并行的「TipCL 真机交互回归」集合 —— 切语言是进程全局的;磁盘 / 物理内存钩子用 AsyncLocal 只对本用例生效):
  - `OldPreflightItems_InEnglish_HaveNoCjk_AndChineseIsVerbatim`:一个场景触发 17 类老预检条目(缺版本 / 版本未装 / 无客户端 jar / 磁盘不足 / 偏低 / 解压成文件夹 / 原版放模组 / Java 不存在 / 内存接近可用 / 缺库 / 资源索引缺失 / 为空 / 资源大量缺失 / 重复 / 互斥 / 缺 Fabric API / 缺依赖),en 下标题与说明不含 CJK、没有没填的 `{占位}`;zh 下逐条与改动前拼接结果逐字相等。
  - `OldPreflight_ReadyItem_FollowsTheLanguage`、`ConflictMessages_InEnglish_HaveNoCjk_AndChineseIsVerbatim`(含 loader_mismatch;type/need/file 两种语言下一致)、
    `AiPreflightReport_InEnglish_HasNoCjkAnywhere`(整份报告 + 交给 AI 的开场全文)、`StepPrefixesAndSummary_FollowTheLanguage_AndStripStateKnowsBoth`。
- `AiI18nResidueAppTests`(同一集合,无头会话):启动页提示条**真跑** AiPreflight(条目来自老 Preflight / Conflict)en 下不含 CJK、zh 下是原中文;AI 页步骤组(运行中与完成后)+ 步骤行 + 权限标签 en 下不含 CJK、zh 下与原来一致;工具行英文前缀被剥掉;版本设置 / 存档对话框下拉先在中文下取一次再切 en 拿到英文、切回中文一字不差、媒体下拉按下标映射。
- `I18nRuntimeTrMarkerTests`:见 §1.4。

## 4. 验收(本机实测,2026-11-21,`_verify_ai3/verify3.ps1 -Name i18n`)

- Debug `--no-incremental` 构建:exit=0,0 错误,3 条警告(均为基线的 `LayoutMotionAndHeightTests` xUnit1013×2 / xUnit2000×1)。
- 全量 `dotnet test`:**1369 通过 / 7 跳过 / 0 失败(共 1376 = 基线 1365 + 11)**,连跑两次一致(1 m 35 s / 3 m 11 s)。
- `--ai-selftest`:exit=0,**108 / 0 / 0**(本包未加自检项)。
- i18n:未接线 3366(基线文件 3370)→ **3211**;wrapped 2479 → 2615;两表 3451 键、键集相等;missing en 0。
- `git diff b6df4bb HEAD -- windos` 为空。

## 5. 遗留(如实)

1. 页面级静态下拉 / 分段文案(§1.3 末段列的 6 处)仍要重启启动器才换语言 —— 页面实例缓存到进程结束,要做即时刷新得给这些页面加「语言变更 → 重建」的通知链,超出本包范围;设置页切语言时的既有 Toast 已提示「下次启动后生效」。
2. AI 页 / AiFixView 里仍有与步骤行无关的中文(「自定义端点」、未配置提示、空态副标题、上下文压缩 Toast 等,`AiPage.cs` 37 条、`AiFixView.cs` 7 条),不在本包「步骤行前缀」范围。
3. `AiUiText.StatePrefixes` / `RiskText` 的中文原文没动(`--ai-selftest` 断言中文原文;`RiskText` 在 `ConfirmCard` 里已过词表)。
4. `Api.cs` 里安装任务日志的 `"完成: "` 前缀、`LaunchCore` 的 `[预检:…]` 日志行是日志,不是界面文案,未接线。
5. `runtime-tr` 块包住了 `Tools.Labels / Formats` 整张字典:以后若有人往字典里追加中文标签却没补 en 译文,审计会把它计为已接线、但会出现在 `missing en` 里 —— 合并后跑审计时看一眼 `missing en` 应为 0。
6. 英文译文是本包写的,措辞未经母语审校。

## 6. 合并注意(冲突点)

- `Preflight.cs`:几乎每条 `_item(...)` 的标题 / 说明都改了;`_check_mod_conflicts` 末尾 `fab` 的分拣方式改了(不再按文字筛)。若别的包也改了这些行,以本包的 `Tr(...)` 写法为准、保留对方的逻辑改动。
- `Conflict.cs`:`L()` 之后新增私有 `Tr` 小工具;`ScanConflicts` 里 5 处 `"message", …`。
- `AiUiText.cs`:`StripState` 末尾多一段 + 新字段 `StateKeys`;`StepSummary` 函数体。
- `Tools.cs`:`Labels` 字典上方 2 行(remarks + `// i18n:runtime-tr-begin`)、`Formats` 字典 `};` 之后 1 行 `// i18n:runtime-tr-end`。**别的包在字典末尾追加的新行落在块内即可**,新中文标签请同时补 en 译文。
- `AiPage.cs`:`RefreshPermissionLabel` 一行、`RunAgentAsync` catch 两行、`OnAgentEvent` 的 tool / tool_done / error 共 4 行、`Summarize` 一行。`AiFixView.cs` 3 处、`StepGroup.cs` `Refresh` 一行。
- `VersionSetupDialog.cs`:顶部字段段落(Iso/Gc 由字段改属性 + `NoAccountLabel` / `IsoChoices`)、`ShowAsync` 前几行;`SavesDialog.cs`:`MediaLabels` 段落、`kind` 一行、`ReloadMediaAsync` 一行。
- `locales/{zh_CN,en}.json`:末尾追加 74 键(注意上一条末尾补逗号);合并后跑 `I18nCoreLocalesTests`。
- `tools/i18n-audit.py`:`is_file_ignored` 之后新增 `RUNTIME_TR_*` 与 `runtime_tr_lines`;`scan_file` 多 5 行;result 多 2 个字段;HELP 多 3 行。
- `progress/I18N-BASELINE.md`:**合并后重跑一次** `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md`(只能调低或持平)。

## 7. 合并后要改的 README / PARITY / DECISIONS 行(本包按约定未改)

- `docs/PARITY.md` §11.4 第 1 条「启动页提示条里来自老 `Preflight` / `Conflict` 代码的条目,英文界面下仍是中文;AI 页「准备:」「完成:」前缀仍是中文」→ 改为已接(AI3-i18n;残留见本文 §5)。
- `progress/AI2-i18n-ux.md` §9 第 1、2、3 条可标「已由 AI3-i18n 处理」(第 3 条的页面级下拉见本文 §5-1)。
- `docs/DECISIONS.md` 建议补一句(可并进 D-82):「审计里 `i18n:runtime-tr` 标记 = 运行时过 Tr 的表;只能用在 `RUNTIME_TR_FILES` 白名单里的文件,由 `I18nRuntimeTrMarkerTests` 守住」。
- `README.md`:无需改动。
