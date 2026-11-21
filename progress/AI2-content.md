# AI2-content · P2 无代码内容制作(分支 `ai2/content`,基于 main@f1c070b)

## 做了什么

`fs_write` 的规矩不放宽(仍拒脚本、白名单不变),改为新增 4 个**专用**工具,每个只写一个很窄的位置,写之前按类型做结构校验:

1. **`create_datapack`**(写,medium)—— 只写 `<游戏目录>/saves/<存档>/datapacks/<包名>/`
   - `pack.mcmeta`:格式号查 `ContentFormats.DataPack` 对照表(来源:Minecraft Wiki「Pack format」页两张表的 Releases 列,注释里写明);
     **表里没有的版本一律拒绝**(含不存在的小版本 1.16.6 / 1.21.12、1.13 之前、26.4…),报错里写「不猜格式号」;
     1.21.9 起按 Wiki 改写 `min_format` / `max_format`(不再写 `pack_format`)。
   - `data/<ns>/` 下五类文件:recipe / loot_table / advancement / function / tag(tag 需 `tag_type`:block / item / entity_type / fluid / function / game_event)。
     **1.21 起目录名单数**(recipes→recipe、loot_tables→loot_table、advancements→advancement、functions→function、tags/items→tags/item …),按版本自动处理;
     输入写成复数也认(`recipes` / `items`)。
   - 结构校验(`ContentValidate`):resource location 小写、最多一个冒号、不许空段 / `..` 段;
     配方按版本区分写法 —— 1.20.5 起产物 `id`(之前 `item`;烧炼 / 切石之前是字符串)、1.21.2 起原料是字符串 / 字符串数组(之前 `{item}`/`{tag}` 对象)、
     26.3 起烧炼类必须 `cookingtime`;有序合成查 pattern 行宽一致、符号都在 key 里、key 里的符号都被用到;认不出的模组配方类型只查 `type` 并在结果里说明。
     战利品表查 pools/rolls/entries/type/name;进度查 criteria(非空、每条有 trigger)、requirements 引用、display 的 icon(按版本 `item`/`id`)/ title / description / frame;
     标签查 values 与 replace / required 类型。
   - 函数文件:每行一条、不以 `/` 开头、命令名小写、`#` 注释、`$` 宏行只有 1.20.2 起认;报错带行号。
   - `update=true` 才能改已有的包;已存在时逐文件 diff,内容完全相同的文件不写、不进检查点。
2. **`create_resourcepack`**(写,medium)—— 只写 `<游戏目录>/resourcepacks/<包名>/`
   - 资源包格式号对照表(同一来源,收 1.13 起;更老的结构本工具不生成);
   - `assets/<ns>/lang/<code>.json`(扁平字符串表,语言代码小写;`merge=true` 合并进已有文件)、`models/…`、`blockstates/…`(variants 与 multipart 二选一、旋转 90 的倍数);
   - 贴图**只接受用户附带的 png**(`source` 填本地绝对路径,带 `content` 直接拒):校验 PNG 签名 + IHDR、宽高都是 2 的幂或 16 的倍数、≤4 MB / ≤8192 边长。不生成图片。
3. **`write_kubejs_script`**(写,**high**)—— 只写 `<游戏目录>/kubejs/{server,client,startup}_scripts/<名字>.js`
   - 只有实例(版本)的 mods 里真装了 KubeJS 才可用:找文件名带 kubejs 的 jar,读 `fabric.mod.json` / `quilt.mod.json` / `META-INF/(neoforge.)mods.toml` 认 mod id = `kubejs`
     (附属模组 id 不是这个,不算);jar 打不开才退回按 `kubejs-<加载器>-` 文件名认。
   - 文件名白名单 `[A-Za-z0-9_-]{1,64}`(.js 可省),单文件 ≤64 KB;已存在要 `update=true`。
   - 静态检查(`ContentValidate.KubeJsDenied` 常量表,20 条):`require(`、`eval`、`Function(`、`importPackage/importClass/JavaImporter`、`JavaAdapter`、`Packages.`、
     `java.lang.Runtime`、`ProcessBuilder/Process`、`java.lang.reflect/invoke`、`ClassLoader`、`forName(`、`setAccessible(`、`java.lang.System`、`java.lang.Thread`、
     `java.net`、`java.nio.file/channels`、`java.io`、`javax.script`、`sun.*/jdk.internal/com.sun.*`、`Java.loadClass` 参数不是字符串常量(拼接 / 变量)。
     原文与「\uXXXX 转义还原后」各查一遍;注释、字符串里出现也算(宁可误拦,不做易被绕过的语法分析)。
   - **确认卡与执行器两处都不给「本对话都允许」**(`ConfirmCard.AllowAlways` + `ToolRunner.AllowsAlways`),有用例锁定「卡片回 always 也每次都问」。
4. **`validate_content`**(只读,low,side_effect read,无共享可变状态)—— 读 `latest.log`(版本隔离时在 `VersionSettings.GameDir`,复用 `AiFsTools.FindLog`)
   与 `logs/kubejs/{startup,server,client}.log`,按 `ContentTools.LogPatterns` 常量表把加载错误分成 datapack / resourcepack / kubejs,
   返回 `{issues:[{category,file,line_no,text,detail,repeat}], counts, truncated, message, tip}`;同一行去重计 `repeat`,`kind` / `filter` 可筛;
   `tip` 提示用户在游戏里 `/reload`(数据包、server_scripts)/ F3+T(资源包、client_scripts)/ 重启(startup_scripts)。
5. **确认链**:三个写工具都走 `RunAsync` 的同一条链 —— ToolMeta 分级 → `Decide` → `Confirm`(带 `ContentPreview.ChangePreview` 逐文件 diff:位置、MC 版本与格式号、
   新建 / 改前改后、贴图尺寸、未变化的文件;超 200 行落盘)→ `AiAgent.SnapshotForTool` 按 `ContentTools.AffectedPaths` 做检查点。
   执行、预览、检查点用**同一份计划**(`ContentTools.Plan`),且目标都是跟随链接后的真实路径;校验不过时卡片上直接写「这一步会被拒绝:原因」。
6. **系统提示最小追加**:`AiData.SYSTEM_PROMPT` 末尾一节「# 数据包 / 资源包 / KubeJS 脚本」(5 行:改配方优先数据包,装了 KubeJS 且要动态逻辑才用脚本;贴图只用用户附带的 png;
   版本查不到就问不编;写完提醒 /reload、F3+T;不要用 fs_write 写脚本或数据包)。`KEYWORD_TOOLS` 末尾 4 行。

## 文件清单

新建(本包独占):
- `src/TipCL.Core/Launcher/Ai/Content/ContentFormats.cs` —— 版本 → pack_format 对照表(数据包 / 资源包)、单复数目录、按版本的字段写法
- `src/TipCL.Core/Launcher/Ai/Content/ContentValidate.cs` —— 名字 / 路径 / resource location、配方 / 战利品表 / 进度 / 标签 / 函数 / 语言 / 模型 / 方块状态校验、PNG 校验、KubeJS 静态检查
- `src/TipCL.Core/Launcher/Ai/Content/ContentTools.cs` —— 四个工具的计划 / 执行 / 检查点路径 / 日志扫描 / schema / 自检
- `src/TipCL.Core/Launcher/Ai/Content/ContentPreview.cs` —— 标签与确认卡预览(界面文案,全部 `I18nCore.Tr`)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.Content.cs` —— `ToolRunner` 分部:`RunContent`
- `tests/TipCL.Tests/Core/AiContentTests.cs`
- `progress/AI2-content.md` —— 本文

共享文件(都是集中一处的追加式改动):
- `ToolRunner.cs`:switch 在 `default:` 前加一组 4 个 case → `RunContent`
- `AiToolTypes.cs`:`ToolMeta.ALL` 末尾 4 行
- `Tools.cs`:`Schemas` getter 加 `cached.AddRange(ContentTools.Schemas());` 一行;`Label` 里在 AcctLayoutMp 钩子后加一段 `ContentPreview.Label`;`AffectedPaths` 在 `fs_write` 分支后加 3 个 case
- `Preview.cs`:`ChangePreview` 开头(AcctLayoutMp 钩子后)加一段转 `ContentPreview.ChangePreview`
- `ToolRunner.NewFeatures.cs`:`AllowsAlways` 加 `"write_kubejs_script" => false`
- `src/TipCL.App/Pages/Ai/ConfirmCard.cs`:`AllowAlways` 加 `"write_kubejs_script" => false`
- `AiData.cs`:`SYSTEM_PROMPT` 末尾一节;`KEYWORD_TOOLS` 末尾 4 行
- `AiSelfTest.cs`:第 14 节后加第 15 节(`ContentTools.SelfCheck`,6 条)
- `Resources/locales/zh_CN.json`、`en.json`:末尾各加 10 个键(两表键集合仍相等)

另:`tests/TipCL.Tests/App/ClickSweepTests.cs` 的「会调系统浏览器 / 文件管理器的按钮」跳过词(10 个)不是本包写的 —— 开工后在本 worktree 里出现的未提交改动
(注释写「用户 2026-11-21 报告」会真的弹出 Edge),为了验收时不弹窗、git status 干净,单独提交了一次(见提交记录),合并时与主线 / 其他分支的同一改动取任一即可。

## 新工具

| 工具 | 风险 | side_effect | 只读 | 「本对话都允许」 | 写入范围 |
|---|---|---|---|---|---|
| `create_datapack` | medium | write_local | 否 | 给 | `saves/<存档>/datapacks/<包名>/` |
| `create_resourcepack` | medium | write_local | 否 | 给 | `resourcepacks/<包名>/` |
| `write_kubejs_script` | **high** | write_local | 否 | **不给(卡片 + 执行器两处)** | `kubejs/{server,client,startup}_scripts/<名字>.js` |
| `validate_content` | low | read | 是 | — | 不写 |

工具数 65 → 69。

## 测试

- 新增 xunit `AiContentTests` 103 个(Fact + Theory 数据行)。覆盖:
  四个工具三处注册 + 分发探针 + 标签;数据包 / 资源包格式对照(逐行抽查 + 未收录版本不猜);1.21.9 起 min/max_format;
  单复数目录(DataDir / TagDir + 端到端在 1.20.4 与 1.21.4 两个存档里落盘位置);从实例元信息推断 MC 版本;
  配方 9 个正例 / 15 个反例(缺 type、大写 RL、`a:b:c`、pattern 符号未定义、key 符号未用、行宽不一、版本写法错配、count 越界、26.3 缺 cookingtime …);
  战利品表 / 进度 / 标签 / 函数正反例;resource location 规则;语言 / 模型 / 方块状态正反例;
  PNG:7 个合法尺寸、3 个非法尺寸、非 PNG / 错误首块;资源包端到端(语言 + 模型 + 方块状态 + 贴图,merge 合并,假 PNG / 带 content / 相对路径 / 非 .png 全拒);
  KubeJS:**常量表每条规则都有样例命中**(加规则不加样例会红)、7 种绕过写法(字符串拼接、跨行、变量、\u 转义、注释里)都拦、4 个正常脚本不误拦;
  未装 KubeJS / 只有附属模组时拒、装了才写、已存在不带 update 拒、危险内容拒且原文件不动、文件名穿越 / 带点 / 错误目录 / 超 64 KB 拒;
  「卡片回 always 仍每次问」与对照组(普通写工具第二次不问);
  路径穿越 11 种(包名 `../`、`..`、存档名 `../`、文件路径 `..` / 绝对 / 反斜杠 / 盘符、namespace、大写、设备名、存档不存在)+ 资源包两种 + **datapacks 目录是指向外面的链接**;
  确认卡拿到 diff(新建 / 改前改后 / 格式号 / 会被拒的原因)、用户拒绝不落盘、plan 档不弹卡;内容不变时什么都不写;
  **检查点:两轮(新建包 + 新建脚本 / 改函数)分别回滚 → 改的还原、新建的删除**;validate_content 按类别计数、去重计 repeat、detail 取异常行、kind / filter 筛选、没有日志时的提示;
  日志判据 8 条;自检 6 条全过。
- `--ai-selftest` 新增 6 条(第 15 节)。

## 验收(本机实测,2026-11-21)

- Debug `--no-incremental` 构建:0 错误;警告只有基线那 3 条 `LayoutMotionAndHeightTests` 的 xUnit 警告。
- 全量 `dotnet test`:**通过 1224 / 跳过 7 / 失败 0(共 1231)**;基线 1121 / 7 / 0(共 1128)→ 本包新增 **103** 个用例(`AiContentTests`,Fact + Theory 数据行)。
  - 如实记录:同一提交前两次全量跑各红过 1~2 条 UI 用例(`DlgCloseTests.Alert_HasSingleKnowButton_AndNeverHangs`、
    `InstanceDetailCrashTests.LibraryDetailChip_OpensDetail_FirstLayout_DoesNotCrash`、`LayoutSmoothDragUiTests.EscapeDuringSmoothDragSlidesBackAndPersistsNothing`),
    堆栈都是「上一个用例(`ClickSweepTests` 点到 AI 页后真的发了一次模型请求,网关回 `HTTP 401 Invalid token`)留下的
    `AiPage.OnAgentEvent` 续体,在下一个用例的 `Dispatcher.ResetForUnitTests` 里执行 → `Res.Bind` 拿不到控件」,取决于网络延迟与用例顺序;
    单独跑这三个类 + ClickSweep 全绿,第三次全量 0 失败。与本包代码无关(本包没有界面改动),但合并后值得单独修:ClickSweep 不该在 AI 页触发真网络请求。
- `--ai-selftest`(临时 `TIPCL_HOME`):exit=0,**通过 97 / 失败 0**(基线 91 + 本包 6)。
- i18n 守卫:未接线中文字面量本包前后都是 3540(基线 3605,未调基线);两表键集合相等。
- `git diff f1c070b HEAD -- windos` 为空。

## 遗留问题与风险

- **回滚只删文件不删空目录**:检查点按「文件」记(新建的回滚时删掉),新建包回滚后会留下空的 `datapacks/<包名>/data/...` 目录树;
  没有 pack.mcmeta 的空目录游戏会忽略,不影响加载,但用户能看见空文件夹。要清空目录得改 `Checkpoint`(公共文件),本包没动。
- **静态检查是启发式**:按文本匹配,不做 JS 语法分析 —— 能拦常见写法与几种混淆(拼接、\u 转义、跨行、变量),拦不住所有可能的动态取值
  (如通过 KubeJS 自身 API 间接拿到的对象再反射);因此仍记 high、每次确认。也会误拦注释 / 字符串里出现这些词的正常脚本(报错里会说明是哪一行)。
- **校验不是游戏解析器的完整复刻**:只查「游戏一定会拒或明显写错」的结构;物品 ID 是否真存在、命令参数是否正确不查(要靠 validate_content 看游戏日志)。
  模组配方类型、自定义原料(带 type 的对象)只查 type 写法。26.x 版本的配方 / 进度字段只按 Wiki 已写明的变化(26.3 cookingtime)处理。
- **格式表需要随新版本补行**:来源与规矩写在 `ContentFormats.cs` 头注释;新版本发布前该版本会被拒(这是有意的)。
- **MC 版本推断**:优先参数 `mc_version` → 版本 id(或其 version json 的 inheritsFrom / clientVersion)→ 实例元信息 → 已装版本(只有一个不同 MC 版本时才用);多个时拒绝并列出。
- **权限档**:三个写工具都是 write_local,在 `edit` / `acceptEdits` 档下与 fs_write 一样直接执行(既有口径);`write_kubejs_script` 只是不给「本对话都允许」。
- 结果 JSON 经 `PyJson.Dumps` 默认 ensure_ascii,中文会转义成 `\uXXXX`(与其他工具一致)。

## 合并注意

- `ToolRunner.cs` switch 的 `default:` 前、`ToolMeta.ALL` 末尾、`Tools.Schemas` 的 AddRange 列、`Tools.Label` 的钩子列、`Tools.AffectedPaths` 的 `fs_write` 之后、
  `Preview.ChangePreview` 开头的钩子列、`AiData.SYSTEM_PROMPT` 末尾(本包把原来最后一行的 `";` 改成了 `" +` 再接新节)、`KEYWORD_TOOLS` 末尾、
  `AiSelfTest` 第 14 节之后、`ConfirmCard.AllowAlways` / `ToolRunner.AllowsAlways` 的 switch、两个 locales 末尾 —— 都是追加点,别的包大概率在同一位置相邻追加,两边都保留即可。
  **SYSTEM_PROMPT 末尾那一行要特别看**:若别的包也在末尾追加,合并后只能有最后一行以 `";` 结尾。
- locales 末尾追加 10 键;合并后跑一次 i18n 守卫(键集相等、未接线数不涨:本包前后都是 3540,基线 3605)。
- 合并后务必跑 `AiLogsFsTests` 的提示 ↔ 注册表一致性检查与 `--ai-selftest`:本包在系统提示里点名了 create_datapack / create_resourcepack / write_kubejs_script / validate_content / fs_write。

## 要改的 README / PARITY / DECISIONS 行(合并时由主代理改)

- `docs/PARITY.md` §10:工具表 +4 行(上表),注明「Windows 版无对应工具,Linux 独有;命名 snake_case」;工具总数 65 → 69(再加其他包的)。
- `README.md` 目录树 `Launcher/Ai/` 的说明可补「Content/:数据包 / 资源包 / KubeJS 脚本工具」。
- `DECISIONS.md` 建议新增一条:「无代码内容制作不放宽 fs_write,改为专用工具 + 窄写入范围 + 按类型校验;pack_format 只认对照表(来源 Wiki),查不到拒绝不猜;
  KubeJS 只在真装了时可用、静态检查拦文件 / 网络 / 进程 / 反射、high 风险且两处都不给『本对话都允许』」。
