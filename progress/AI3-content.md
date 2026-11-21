# AI3-content · 内容制作与整合包遗留补齐(进度文档)

分支 `ai3/content`(基于 main@b6df4bb),worktree `D:\项目\TipCL\wt-ai3-content`。只改 Linux 主线(`src/` + `tests/`),`windos/` 未动。
来源:docs/PARITY.md §11.4 第 3、4 条;progress/AI2-content.md、progress/AI2-modpack.md 的「遗留」节。

## 做了什么

| # | 遗留项 | 做法 | 落点 |
|---|---|---|---|
| 1 | **撤回后不留空目录** | 复用 AI2-stability 的「新建子目录」检查点操作(`new_children`),加一种 `only_empty` 模式:`Checkpoint.SnapshotNewDirs(chat, turn, parent, names)` 只记「这一步会新建、此刻还不存在」的子目录名(已存在的自动去掉;带 `/` `\` `..` 的名字丢掉)。撤回时内容快照(seq 更大)先重放、删掉新建文件,再轮到这一条:点名的目录**整棵都是空目录**才删;里面还有文件(用户后来放的 / 没撤掉的)就保留目录本身,只清掉它里面已经空了的子目录;没点名的目录(如游戏自己后来建的)、原来就有的目录一个都不碰;链接一律当「不空」不进不删。`ContentTools.NewDirWatches` 按同一份计划(Plan)算出每个要写的文件「最近的已存在祖先目录 + 它下面第一级新目录」,覆盖 create_datapack / create_resourcepack / write_kubejs_script(datapacks/ 或 resourcepacks/ 本身这一步才建时也一并清掉;update 已有包时只清新加的命名空间目录)。`AiAgent.SnapshotForTool` 在内容快照**之前**调 `ContentTools.SnapshotNewDirs`(顺序决定重放先后) | `Checkpoint.NewChildren.cs`、`Content/ContentTools.cs`、`AiAgent.cs`(1 行) |
| 2 | **ID 命名空间警告** | 新文件 `Content/ContentIds.cs`:从配方(类型、原料、产物,原版各版本写法 + 常见模组写法 ingredients/results/items/…)、战利品表(item / tag 条目,含 alternatives 等的 children)、进度图标、物品 / 方块标签、资源包模型(parent / 贴图)与方块状态(方块 ID 本身 + 引用的模型)里收集引用的 ID。判定:物品 / 方块 / 配方类型只认 `minecraft` + 已装模组 mod id;标签另外认加载器公共命名空间(c / forge / neoforge / fabric / quilt)和本包自己的命名空间;模型 / 贴图另外认本包自己的命名空间(这次的文件 + 包里已有的 data/ 或 assets/ 子目录)。已装 mod id 复用 `ModJarInfo.Read`(install_local_mod 用的同一套 jar 元数据解析),`.disabled` 不算,按 mods 目录「文件名 + 大小 + mtime」签名缓存。**只警告不拒绝**:结果里多 `warnings`(按命名空间合并成一条,列出出处与引用,最多 20 条);确认卡上多一行「提醒:这些命名空间……:{list}」 | `Content/ContentIds.cs`(新)、`ContentTools.cs`、`ContentPreview.cs` |
| 3 | **KubeJS 静态检查加固** | `ContentValidate.KubeJsDenied` 末尾补 5 条 + 改 1 条:**字符串拼接构造 Java 类名**(`'java.ne' + x`、`x + '.lang.Runtime'`)、**`\u` / `\x` / 八进制转义写出普通 ASCII 字符**(只拦可见 ASCII:那只可能是在藏代码;`\u00a7` 颜色符、中文等照常放行)、**模板字符串**(反引号一律拒,可用 `${}` 拼任何东西)、**方括号动态属性访问**(任何 `x['…']` / `)['…']`,以及 Java / Packages / java / global / globalThis / this / Function / Object / Reflect 等后面跟 `[`;`return [..]`、`in [..]` 这类关键字后的数组字面量不算)、**constructor / `__proto__` / getPrototypeOf / setPrototypeOf 原型链**;`Function` 规则从只拦 `Function(` 扩到 `Function.` / `Function[` / 标签模板 / `new Function` / 赋给别名 / 当参数传 / `return Function`(注释里的「Function to …」不误拦)。检查对三份文本各跑一遍:原文、转义还原后(新增 `\x` 与八进制)、**再把相邻字符串常量拼接折叠后**(`'java.n' + 'et.URL'` → `'java.net.URL'`,跨行拼接也折叠,行号不错位);所有规则改成整段匹配(跨行写法、ASI 跨行的 `x\n['y']` 也算)。仍记 high、两处都不给「本对话都允许」 | `Content/ContentValidate.cs` |
| 4 | **plan_modpack 加 CurseForge** | `IModpackSource` 加两个带默认实现的成员(`SearchCurseForge` / `CurseForgeLatestFile`,默认抛 NotSupported → 老的假源照常只有 Modrinth);`NetModpackSource` 复用 `Mods.SearchCurseforge`(按 MC 版本、热门度排,分类按 CurseForge 分类名关键词过滤)与 `Mods.CfFiles`(按 MC + modLoaderType 取文件,优先 release,依赖 relationType 3 = 必需 / 5 = 不兼容)。规划器每组同时搜两边,用 `Mods.WeightedDownloads`(与搜索页同一套平台权重)把两个已排序列表归并;同一模组(slug / 标题归一后相同)只留一条:**Modrinth 有对应版本优先 Modrinth**(mrpack 能直接链接),CurseForge 那条挂在 `also_on_curseforge`;Modrinth 没有对应版本、CurseForge 有文件时改用 CurseForge(reason 里写明);两边都没有进 dropped。CurseForge 候选带 `source:"curseforge"`、`id`、`file_id`、依赖(标题用 CurseForgeMods 批量查)、不兼容冲突,note 提示 install_mods_batch 的写法和导出 mrpack 的分发限制。Modrinth 分类没有 CurseForge 对应分类的组只搜 Modrinth(`curseforge.status = not_searched`);**连不上 CurseForge**:第一次失败就不再试(断路),照常只用 Modrinth,结果 `curseforge.status = unavailable` + `notes` 写明原因,`search_errors` 里也有一条 | `ModpackSource.cs`、`Modpack/ModpackPlanner.cs`、schema 描述 |
| 5 | **install_mods_batch 后台任务** | 确认链不变(整批只问一次、确认卡逐项列出,卡上多一行说明会在后台逐个装)。执行时先校验(实例 / 空清单 / 超 60 个立刻报错),再请宿主起任务:App 的 `InProcessApi` 新方法 `install_mods_batch` 用 `StartDetached` 起后台任务,逐项把「(n/N)安装 X」「(n/N)✓ X 已装好」「(n/N)✗ X 没装上:原因」报到任务进度(任务卡进度条 + 状态行)并写任务日志;全部成功 → 任务成功、结果行「已装好 N 个模组」(给 `TaskProgress` 加了可选的 `FinishMessage`,不设时行为不变);有失败 → 任务失败,原因里列出是哪几个。工具立刻返回 `{background, task_id, mods[逐项], next}` 并发 `TaskStarted` 出任务卡,AI 页不再长时间「执行中」;模型用 get_task_status(task_id) 查进度与逐项结果。宿主没有这个方法(测试里的假后端、`NotImplementedException`)时退回原来的同步执行。`ModpackTools.InstallBatch` 多了逐项进度回调(`BatchEvent`:start / done + 成功与否 + 原因)与取消(两个模组之间检查) | `Modpack/ModpackTools.cs`、`ModpackPreview.cs`、`ToolRunner.Modpack.cs`、`App/Common/Api.cs` |

工具总数不变(78);没有新工具,没有改风险分级与「本对话都允许」两处开关(write_kubejs_script / install_mods_batch 仍不给)。

## 文件清单

新增:
- `src/TipCL.Core/Launcher/Ai/Content/ContentIds.cs`(ID 命名空间检查;`i18n:ignore-file`,中文只在给模型的 warnings 里)
- `tests/TipCL.Tests/Core/AiContent3Tests.cs`、`tests/TipCL.Tests/Core/AiModpack3Tests.cs`
- `progress/AI3-content.md`(本文件)

修改:
- `src/TipCL.Core/Launcher/Ai/Checkpoint.NewChildren.cs`:`SnapshotNewDirs`、`ApplyNewChildren` 的 only_empty 分支、`IsEmptyTree` / `PruneEmptySubdirs`(原有 match_mc 分支不变)
- `src/TipCL.Core/Launcher/Ai/AiAgent.cs`:`SnapshotForTool` 里 UpgradeCheckpoint 之后一行
- `src/TipCL.Core/Launcher/Ai/Content/ContentTools.cs`:Plan 加 Warnings / SuspiciousNamespaces、两个 Plan 函数收集引用、`CheckIds`、`NewDirWatches` / `SnapshotNewDirs`、Apply 输出 warnings、SelfCheck +2
- `src/TipCL.Core/Launcher/Ai/Content/ContentValidate.cs`:KubeJS 规则表、`UnescapeUnicode`(加 \x / 八进制)、`FoldConstantConcat`、`KubeJsViolations` 改整段匹配 + 三份文本
- `src/TipCL.Core/Launcher/Ai/Content/ContentPreview.cs`:确认卡提醒行
- `src/TipCL.Core/Launcher/ModpackSource.cs`:接口两个默认成员 + 网络实现 + `CfLoaderType`
- `src/TipCL.Core/Launcher/Ai/Modpack/ModpackPlanner.cs`:Pick 扩展、CF 分类表、归并、去重、版本回退、CF 依赖 / 冲突、结果 sources / curseforge / notes
- `src/TipCL.Core/Launcher/Ai/Modpack/ModpackTools.cs`:`BatchEvent`、`CheckBatch`、`BatchInstance`、`BatchStarted`、`InstallBatch` 加进度 / 取消参数;两个 schema 描述
- `src/TipCL.Core/Launcher/Ai/Modpack/ModpackPreview.cs`:任务标题 / 进度文字 / 汇总、确认卡一行
- `src/TipCL.Core/Launcher/Ai/ToolRunner.Modpack.cs`:install_mods_batch 走宿主后台任务(不支持时退回同步)
- `src/TipCL.App/Common/Api.cs`:`TaskProgress.FinishMessage`;`StartDetached` 成功时带上它;新 case `install_mods_batch`
- `src/TipCL.Core/Resources/locales/zh_CN.json`、`en.json`:末尾各 +6 键(键集合相等)
- `tests/TipCL.Tests/Core/AiContentTests.cs`:`DangerSamples` 补 5 条新规则的样例(每条规则必须有样例的那条守卫)

## 测试

新增 xunit **55 个**(Fact + Theory 数据行),全部不联网(假 IModpackSource、`ModpackTools.InstallerHook`):
- `AiContent3Tests`(47):撤回 5 例(新包整棵清掉且已存在的 datapacks/、别的空目录不动;datapacks/ 本身这一步才建时一起清;update 已有包只清新命名空间目录;用户后来放了文件时目录保留但空子目录清掉;资源包 + KubeJS 脚本目录)、`SnapshotNewDirs` 单测(已存在名字去掉、`../` 丢掉、有文件不删、没点名不删、无名字不写流水)、`NewDirWatches` 指向最外层新目录且校验不过不记;ID 警告 6 例(未知命名空间按命名空间合并、照样写入;已装 mod id 不报且 `.disabled` 不算、全有来头时无 warnings 字段;1.20.1 旧写法与模组配方类型;资源包模型 / 贴图 / 方块状态与本包命名空间;确认卡提醒行;判定规则与上限折叠);KubeJS 23 个绕过正例(拼接、跨行拼接、\x / \u / \u{} / 八进制、模板、标签模板、方括号四种含 ASI 跨行、constructor / __proto__ / getPrototypeOf、Function 四种拿法)+ 9 个正常脚本反例(数组字面量、注释里的 Function、多行 shaped、`return [...]`、翻译键拼接、`\u00a7` 颜色符、对象属性、普通拼接、`xs[i]`)+ 行号不错位 + 工具链路(绕过写法被拒、文件不落盘、high、卡片回 always 下次照样问)
- `AiModpack3Tests`(8):CF 归并顺序按加权下载量、同一模组只一条、Modrinth 有版本优先 / 没版本改用 CF、CF 库不当候选、CF 前置进 extra_dependencies(标题批量查)、also_on_curseforge、没文件进 dropped;CF 连不上 → 只用 Modrinth、status=unavailable、notes 注明、只试一次;源不支持 / 分类无对应(not_searched)/ 关键词组也搜 CF;CF 不兼容冲突;`InstallBatch` 逐项进度事件顺序与部分失败、取消;工具立刻返回 task_id、整批确认一次且逐项列出、任务卡拿到同一 id / 工具名 / 标题、后台事件 6 条、空清单不起任务、点过 always 下一批仍确认;宿主 Api 的后台任务:任务行标题、逐项日志、部分失败任务失败且原因列出、全成功结果行与进度 100、实例不存在 / 空清单不起任务

`--ai-selftest` +2 项(ContentTools.SelfCheck:KubeJS 绕过写法;ID 命名空间提醒)。

## 验收(本机实测,2026-11-21,提交 35d5fb9;之后只有本文档的一次提交)

- Debug `--no-incremental` 构建 `TipCL.Linux.sln`:**0 错误**;警告只有基线那 3 条 `LayoutMotionAndHeightTests` 的 xUnit 警告(中间一次多出的测试里 CS8604 已修)。
- 全量 `dotnet test`:**通过 1413 / 跳过 7 / 失败 0(共 1420)**;基线 1358 / 7 / 0(共 1365)→ 本包新增 **55** 个用例。两次全量都 0 失败(第二次因其他代理并行构建,耗时 5 分钟)。
- `--ai-selftest`(临时 `TIPCL_HOME`):exit=0,**通过 110 / 失败 0**(基线 108 + 本包 2)。
- i18n 守卫(`I18nBaselineTests`)在全量里通过:新增界面文案全部走 `Tr`,两份 locales 各 +6 键且键集合相等;未接线基线未改。
- `git diff b6df4bb HEAD -- windos` 为空;git status 干净。

## 遗留问题与风险

1. **撤回清空目录只覆盖内容三件套**(create_datapack / create_resourcepack / write_kubejs_script)。其他写工具(fs_write、install_* …)新建的空目录仍按原样留着;要扩时在别的工具的执行前同样调 `Checkpoint.SnapshotNewDirs` 即可。
2. **ID 检查只看命名空间**,不看 path(物品是否真存在要游戏说了算)。mod id 只取每个 jar 的主 id(`ModJarInfo.Read`):Fabric 的 `provides` 别名、Forge 一个 jar 里声明多个 `[[mods]]`、jar-in-jar 里的子模组 id 不认,这类情况会多报一条提醒(只是提醒,不影响写入)。函数文件(.mcfunction)里命令引用的 ID 不查。
3. **KubeJS 仍是启发式**:新规则更严(模板字符串一律拒、任何 `x['…']` 都拒),会误拦少数正常写法(报错里说明了是哪一行哪条规则,改成普通字符串 / 点号访问即可);通过 KubeJS 自身 API 间接拿到的对象再反射这类仍拦不全,所以仍记 high、每次确认。
4. **CurseForge 搜索没有加载器过滤**(`Mods.SearchCurseforge` 不带 modLoaderType;不改共享代码),靠之后 `CfFiles` 按 MC + 加载器取文件来淘汰不匹配的;分类用 CurseForge 分类名关键词做后过滤(每组最多取 50 条),结果比 Modrinth 少是正常的。同名去重按 slug / 标题归一,极少数不同模组同名会被当成一个(结果里有 also_on_curseforge 可看)。CurseForge 候选导出 mrpack 时仍要打包或被禁(note 里已写)。
5. **install_mods_batch 后台进行时撤回**:检查点在任务起来之前对 mods 目录做了快照,撤回会还原到装之前;但若撤回时任务还在跑,之后装上的文件不会被撤掉。任务走启动器任务表,取消(cancel_task)在两个模组之间生效;AI 页任务卡本身没有取消按钮(沿用现有任务卡)。宿主不支持后台任务时退回同步执行(旧行为)。

## 合并注意(冲突点)

- `AiAgent.SnapshotForTool`:在 `UpgradeCheckpoint.SnapshotFor` 之后、`Tools.AffectedPaths` 之前加了一行;**必须在内容快照之前**。
- `Checkpoint.NewChildren.cs`:`ApplyNewChildren` 里加了 only_empty 分支(原 match_mc 分支逻辑不变),文件末尾新函数。
- `ContentValidate.KubeJsDenied`:表末尾追加 5 条、Function 一条改了 pattern(规则名没变);`KubeJsViolations` 整体替换。若别的分支也加规则,记得在 `AiContentTests.DangerSamples` 里补样例。
- `Api.cs`:`TaskProgress` 加属性、`StartDetached` 成功分支两行、`start_mod_updates` case 之前插入新 case。
- `IModpackSource` 加的两个成员带默认实现,别的分支的假源不用改。
- 两份 locales 末尾各 +6 键(注意逗号)。

## 要改的文档行(合并时由集成代理改,本包未改)

- `docs/PARITY.md` §11.4:第 3 条(撤回空目录 / ID 校验 / KubeJS 加固)与第 4 条(plan_modpack CF 候选 / install_mods_batch 后台任务)标为已完成,指向本文件;§10 的 plan_modpack 一行补「候选来源 Modrinth + CurseForge」,install_mods_batch 一行补「后台任务,返回 task_id,任务卡逐项进度」。
- `README.md` AI 功能列表「一句话造整合包」一句补「候选来自 Modrinth 与 CurseForge」;「数据包 / 资源包」一句补「引用的 ID 命名空间不对时提醒」。
- `DECISIONS.md` 新增一条:「检查点撤回清空目录只删本次点名的新目录且须整棵为空;ID 命名空间检查只警告不拒绝;KubeJS 静态检查拒模板字符串与方括号动态属性访问(宁可误拦);plan_modpack 合并两平台时同一模组优先 Modrinth;install_mods_batch 走启动器任务表,部分失败 = 任务失败」。
