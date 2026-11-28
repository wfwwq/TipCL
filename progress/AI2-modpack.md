# AI2-modpack · P1 整合包制作(进度文档)

分支 `ai2/modpack`(基于 main@f1c070b),worktree `D:\项目\TipCL\wt-ai2-modpack`。只改 Linux 主线(`src/` + `tests/`),`windos/` 未动。

## 做了什么

| # | 功能 | 落点 |
|---|---|---|
| 1 | **导出引擎统一**:新建 `ModpackExport`(mrpack + CurseForge),界面「导出为整合包」(`Api.cs` 的 `export_modpack` 任务 → `ExportPack.ExportMrpack`)与 AI 工具 `export_modpack` 走**同一个 Core 函数** `ModpackExport.Export`。`ExportMrpack` 保留原签名,改为 `Export(..., ModpackExportOptions.Legacy())`:行为与原实现一致(按 sha1 查 Modrinth,找到写 files,找不到把 jar 打进 overrides,四个覆盖目录全带,覆盖保存对话框选的文件)。顺带修了原实现两处与 Modrinth 规范不符的地方:`hashes.sha512` 原来查不到就写空串 → 现在本地计算;`dependencies` 的加载器键原来是 `Py.Lower(loader)`(会写出 `fabric` 而规范是 `fabric-loader`)且只认装整合包时的元数据 → 现在从已装版本 id(`fabric-loader-x-mc` / `mc-forge-x` / `neoforge-x` …)认加载器与版本 | `Core/Launcher/ModpackExport.cs`、`ExportPack.cs` |
| 2 | **export_modpack**(写本地文件,medium,long_running):参数 instance / format(mrpack\|curseforge)/ name / version / summary / author / icon / include_overrides / allow_bundled_jars / exclude / dest。导出前必跑分发许可检查,有「禁止分发」→ 拒绝并列出文件(提示替换 / 禁用 / exclude);来源不明的 jar 默认不打包。确认卡预览 = `BuildPlan` 的同一份清单:输出路径、格式与 MC/加载器、链接下载的模组(逐个 + 大小)、打进包的文件(逐个 + 大小)、不打包 / 排除 / 禁止的模组、包内合计(压缩前)与装包时另需下载的大小、联网失败条数、警告。检查点 = 输出文件(此刻不存在,回滚即删)。不覆盖已有文件(重名加 -2…)。返回导出路径 | `Ai/Modpack/ModpackTools.cs`、`ModpackPreview.cs`、`ToolRunner.Modpack.cs` |
| 3 | **check_modpack_distribution**(只读 + 联网):逐个 jar 算 sha1 / sha512 / CurseForge 指纹 → Modrinth `version_file/{sha1}` + CurseForge `/fingerprints` + `/mods`(拿 `allowModDistribution`)。三类:**可链接**(mrpack:Modrinth 找到且地址在规范允许域名 cdn.modrinth.com / github.com / raw.githubusercontent.com / gitlab.com;curseforge:指纹匹配 → projectID/fileID)/ **需打包**(CurseForge 允许分发 → 直接打包;两边都找不到、许可查不到、只在另一平台、联网失败 → `needs_consent=true`,默认不打包,`allow_bundled_jars=true` 才打)/ **禁止分发**(mrpack 下只在 CurseForge 且 `allowModDistribution=false`)。结果 10 分钟缓存(按 mods 目录文件名+大小+修改时间签名),预览与导出复用 | `Core/Launcher/ModpackDistribution.cs`、`ModpackSource.cs` |
| 4 | **plan_modpack**(只读 + 联网):mc_version / loader / theme / size(small≈8 / medium≈20 / large≈40,主题含「轻量」时默认 small)/ keywords。主题词 → Modrinth 官方分类(technology / optimization / magic …,词表只映射用词,不预设模组),每类各搜一次(search_mods 背后同一个 `/search`,MC 版本 + 加载器 + 分类三组 facets,空查询按下载量排),去掉 library(作为前置随装),按配额挑;每个候选取该 MC+加载器最新版本(`Mods.ListVersions`,与 install_mod 选版本同源)→ required = 前置,incompatible = 冲突;另按已知同类(小地图 / 配方查看 / 渲染核心 / 光影加载器 / 光照 / 逻辑优化 6 组 slug)给「功能重复」提示并标 `inferred=true`。每项带理由(分类 + 排名 + 下载量)、前置(是否已在清单)、冲突;查不到版本的放 dropped。工具自己把 6 步推到计划卡(确认清单 → 建实例并装 MC+加载器 → 批量装 → 冲突+预检 → 可选试启动读日志 → 导出),步骤标题也回给模型 | `Ai/Modpack/ModpackPlanner.cs` |
| 5 | **install_mods_batch**(写本地文件,high,long_running):把用户确认的一批模组装进同一实例,**整批只确认一次**,确认卡逐项列出每个模组;执行体与 install_mod 背后同一套 Core(`Mods.InstallModrinthMod` / `InstallCurseforgeMod`,必需前置随装),逐个装、单个失败不中断,步骤行显示 (i/n) 进度。检查点 = 实例 mods 目录。上限 60 个 | `ModpackTools.InstallBatch` |
| 6 | **系统提示**最小追加「整合包制作」一节:先 plan_modpack 出清单 → ask_user(allow_multiple)勾选,有冲突先 ask_user;**用户没确认清单前不要 create_instance、不装东西**;确认后按计划卡逐步走,每步写操作都等确认;导出前 check_modpack_distribution,禁止分发先处理,来源不明的 jar 只有用户明确同意才 allow_bundled_jars;config 等覆盖目录由用户决定、不编造预设。KEYWORD_TOOLS 末尾追加一组(整合包 / modpack / 造包 / 打包 / mrpack / 导出 / 分发 / 许可 …) | `AiData.cs` |
| 7 | **配置预设**:就是 export 时的 `include_overrides`(含 config 即带上实例当前 config;不编造通用预设) | 同 2 |

## 新工具清单

| 工具 | 风险 | 只读 | side_effect | 「本对话都允许」 |
|---|---|---|---|---|
| check_modpack_distribution | low | ✓ | network | — |
| plan_modpack | low | ✓ | network | — |
| export_modpack | medium | ✗ | write_local(long_running) | **不给**(ConfirmCard.AllowAlways + ToolRunner.AllowsAlways 两处) |
| install_mods_batch | high | ✗ | write_local(long_running) | **不给**(两处) |

Windows 版没有对应 AI 工具;`export_modpack` 与 Windows `BackendApi.export_modpack(instance, dest)` 同名,其余参数按现有 snake_case 习惯(`instance` / `format` / `dest` / `mc_version` / `loader` 与已有工具同形)。工具总数 65 → 69。

## 文件清单

新增:
- `src/TipCL.Core/Launcher/ModpackExport.cs`(导出引擎 + 选项 + 加载器识别 + mrpack 索引 / CF manifest;界面文案走 Tr)
- `src/TipCL.Core/Launcher/ModpackDistribution.cs`(分发许可三类;`i18n:ignore-file`,中文全是给模型的 reason)
- `src/TipCL.Core/Launcher/ModpackSource.cs`(`IModpackSource` + `NetModpackSource` + 注入点 `ModpackSources.Factory`)
- `src/TipCL.Core/Launcher/Ai/Modpack/{ModpackTools,ModpackPlanner,ModpackPreview,ModpackSelfTest}.cs`
- `src/TipCL.Core/Launcher/Ai/ToolRunner.Modpack.cs`(partial:执行分发,不含中文字面量)
- `tests/TipCL.Tests/Core/AiModpackTests.cs`
- `progress/AI2-modpack.md`(本文件)

共享文件的最小追加(都带 `AI2-modpack` 注释,集中一处):
- `ExportPack.cs`:`ExportMrpack` 函数体换成一行转调 `ModpackExport.Export(..., Legacy())`(签名、常量不变)
- `AiToolTypes.cs`:`ToolMeta.ALL` 末尾 4 条
- `Tools.cs`:`Schemas` 里 `AddRange(ModpackTools.Schemas())` 一行;`Label` 里 `ModpackPreview.Label` 一段;`AffectedPaths` 里 `ModpackTools.Handles` 一段(在 AcctLayoutMp 之后)
- `ToolRunner.cs`:`default:` 前 4 个 case 转 `ExecuteModpackAsync`
- `ToolRunner.NewFeatures.cs`:`AllowsAlways` 加 `"export_modpack" or "install_mods_batch" => false`
- `App/Pages/Ai/ConfirmCard.cs`:`AllowAlways` 加同一行
- `Preview.cs`:`default:` 分支开头转 `ModpackPreview.ChangePreview`
- `AiData.cs`:SYSTEM_PROMPT 末尾追加一节(把原最后一行的 `";` 改成 `" +`);KEYWORD_TOOLS 末尾一组
- `AiSelfTest.cs`:`NewFeaturesSelfTest.Verify` 后加 `ModpackSelfTest.Verify(Check)`(+5 项)
- `locales/zh_CN.json`、`en.json`:各追加 42 个键(「导出完成」「导出整合包」「原版」已存在,未重复);键集合相等
- `tests/TipCL.Tests/App/ClickSweepTests.cs`:**不是本包写的** —— 主 agent 下发到 5 个 wt-ai2-* 的同一份未提交修改(跳过会弹浏览器 / 文件管理器的按钮),单独一个 commit 原样提交,合并时与其他分支完全相同

i18n:未接线 CJK 字面量 3540 → 3536(删掉了原 ExportPack 里 4 条),已接线 2222 → 2270;基线未改。

## 测试

新增 xunit **19 个**(`AiModpackTests`,全部不联网:假 `IModpackSource` + `ModpackTools.InstallerHook`;改静态注入点的用例都在同一个类里串行,Dispose 复原):
- 注册表:schema / ToolMeta / 分发三处都在、风险分级、不给「本对话都允许」、标签;关键词选到 plan / batch / export / check;系统提示含 4 个工具与「没确认清单前不要 create_instance」;`AiToolRefs.ProblemsAsync()` 为空
- 许可三类(mrpack):Modrinth 有 → 可链接;CF 允许 → 需打包且无需同意;CF 禁止 → 禁止分发;两边都无 / 许可查不到 → 需打包且需同意;sha512 为本地 128 位
- 许可(curseforge 格式):CF 文件全可链接(禁止分发的也能写 manifest,理由里提示第三方启动器);Modrinth-only 需同意
- 不允许的下载域名 / http / CurseForge 宕机 → 不会误判禁止或允许,全部落到需同意 + lookup_errors
- 缓存:mods 不变不重复联网,加了 jar 就重查
- mrpack 结构:`formatVersion=1` / `game=minecraft` / `versionId` / `name` / `summary` / `files[{path, hashes{sha1,sha512}, downloads[], fileSize}]` / `dependencies{minecraft, fabric-loader}`(恰好 2 个键);overrides 内容正确;不留 .part
- 禁止分发 → `ModpackError`,一个文件都不写;`allow_bundled_jars` 才打包来源不明的 jar;`include_overrides` 生效;不覆盖已有文件
- CurseForge manifest:`manifestType` / `manifestVersion` / `minecraft.version` / `modLoaders[{id:"fabric-0.15.11", primary}]` / `files[{projectID, fileID, required}]`(数字)/ `overrides` / modlist.html
- 界面入口 `ExportPack.ExportMrpack` 走同一引擎且保持原行为(不做许可检查、全打包、覆盖已选文件)
- 工具链路:无确认回调被拒;预览含输出路径 / 链接的 mods/a.jar / overrides/mods/b.jar / overrides/config/a.toml / 不打包的 d.jar,不含被 include_overrides 去掉的目录;检查点路径 = 输出文件且回滚即删;点「本对话都允许」后下次仍要问;同名加 -2
- 禁止分发时工具失败、预览里已列出、目录里没有文件;check 工具只读、结果精简(不含 sha512)
- 造包:主题 → 分类 / 规模;loader 非法、缺 mc 报错;候选顺序、library 不当候选、理由含分类、前置标题与 in_list、extra_dependencies 汇总、incompatible(确定)与 overlap(inferred)、查不到版本进 dropped;plan 工具只读、计划卡 6 步首步 in_progress、结果含 plan_steps 与后续流程
- 批量安装:整批确认一次、预览逐项列出、重复去重、单个失败不中断、标签带个数;点「本对话都允许」后下一批仍要确认
- 加载器识别(forge 版本 id)、mrpack 键名、默认文件名

自检 `--ai-selftest` 新增 5 项(注册 / 写工具确认 / mrpack 域名白名单 / 索引字段 / 主题识别)。

## 遗留问题与风险

1. **既有 bug(未改,不在本包范围)**:AI 工具 `install_mod` / `install_shader` 等在 `ToolRunner.cs` 里把 `name` / `instance` / `source` 放在**顶层**传给 IApi,而 `Api.cs` 的 `InstallModSync` / `InstallContentSync` 只从 `args["extra"]` 里读(`CatalogExtra`),于是实例名与 slug 都会丢(落到默认实例、slug 为空)。`install_mods_batch` 不经过这条路(直接调 `Mods.InstallModrinthMod` / `InstallCurseforgeMod`),不受影响。建议合并时由 integration 把 ToolRunner 的 install_* 改成同时带 `extra = {name, slug, id, source, instance}`。
2. 规范里 mrpack / CurseForge 都没有图标字段:`icon` 写成包根目录的 `icon.png`(多数启动器会忽略;上传 Modrinth / CurseForge 时图标需在网页上另设)。
3. 界面菜单「导出为整合包」仍只有 mrpack、仍不做许可检查(刻意保持原行为);以后可在界面加 CurseForge 选项并展示许可三类(引擎已支持,`ModpackExportOptions` 改两个字段即可)。
4. plan_modpack 只搜 Modrinth(mrpack 只能链接 Modrinth / GitHub 地址,CF 候选导出时多半要打包或被禁);主题词表覆盖 Modrinth 的 16 个 mod 分类,认不出时用原话搜一次并提示模型补英文 `keywords`。
5. 「功能重复」只有 6 组已知同类 slug,属推断(结果标 `inferred=true`,系统提示已有「要说明是推断」的规矩);真正的不兼容以 Modrinth 版本元数据为准,装完再 `scan_mod_conflicts` 复核。
6. install_mods_batch 在工具内同步逐个下载(不起任务卡),装 40 个时 AI 页会长时间显示「执行中」;进度写在步骤行(`(i/n) 安装 …`)。
7. CurseForge 查询需要可用的 CF API(`Config.CurseforgeKey()`);连不上时相关模组按「来源不明」处理并在 `lookup_errors` / 预览里写明,不会误判成允许或禁止。
8. Modrinth 按 sha1 逐个查(与原 ExportMrpack 相同),模组很多时较慢;以后可改 `POST /version_files` 批量。`.disabled` 的模组不导出(与原实现一致)。

## 合并注意

- 高概率冲突点(全部是追加式,保留双方即可):`AiToolTypes.cs` ALL 末尾、`Tools.cs` 的 Schemas AddRange 行 / Label 钩子 / AffectedPaths 钩子、`ToolRunner.cs` `default:` 前的 case 块、`ToolRunner.NewFeatures.cs` 与 `ConfirmCard.cs` 的 AllowsAlways switch、`Preview.cs` `default:` 开头、`AiData.cs` SYSTEM_PROMPT 末尾(注意本包把原最后一行的 `";` 改成了 `" +`,别的分支若也在末尾追加,合并时要把最后一段的结尾统一成一个 `";`)与 KEYWORD_TOOLS 末尾、`AiSelfTest.cs` 末尾、两份 locales 末尾(注意逗号)。
- `ExportPack.cs` 函数体整体替换:若别的分支也改了它,以本包为准(逻辑已搬到 `ModpackExport`)。
- 自检项数 = 基线 + 5;工具数 69(加上其他分支的再累加)。

## 建议的文档改动(本包未改)

- `README.md` 功能列表 AI 部分追加:「一句话造整合包(按主题出候选清单、整批确认安装、导出前做分发许可检查)/ 导出 mrpack 与 CurseForge 整合包(禁止分发的模组会拒绝导出,来源不明的 jar 默认不打包)」。
- `docs/PARITY.md` §10 追加一行:「Linux 独有:plan_modpack / install_mods_batch / check_modpack_distribution / export_modpack(AI)与 CurseForge 格式导出 —— Windows 版只有界面 mrpack 导出」;并在导出一行注明「界面导出与 AI 导出共用 ModpackExport;Linux 修正了 sha512 与 dependencies 加载器键」。
- `docs/DECISIONS.md` 新增一条:「D-8x 整合包分发许可:mrpack 只链接规范允许域名;CF allowModDistribution=false 且 Modrinth 无 → 禁止分发、拒绝导出;来源不明 jar 默认不打包、需用户显式同意(allow_bundled_jars);export_modpack / install_mods_batch 不给『本对话都允许』」。
