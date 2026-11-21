# AI-logs-fs · P0 修复 + 文件类工具(分支 `ai/logs-fs`,基于 main@4277a02)

## 做了什么

1. **P0:补注册 `get_latest_log` / `get_crash_report`**
   系统提示(`AiData.cs` 排错流程第 2 步)、崩溃修复上下文(`AiFixChat.cs`)、locales「日志自查提醒」都让模型调这两个工具,
   但 Linux 的 schema / `ToolMeta.ALL` / `ToolRunner` 分发三处一处都没有 ——「交给 AI 修复」里模型会调一个不存在的工具。
   现在三处都有,并照 Windows 版放进 `CORE_TOOLS`(常驻,否则 `SelectToolSchemas` 根本不会把它们给模型)。
   - 只读、risk low、side_effect read;读实例(版本隔离时为 `VersionSettings.GameDir`)的 `logs/latest.log`
     与 `crash-reports` 里**修改时间最新**的一份;
   - 找文件 / 读文件复用 diagnose_launch 那条链:`Crash.CollectFiles` + `Crash.Classify` + `Crash.ReadText`(UTF-8 读不了回落 GBK);
   - 参数:`instance` / `version`(与 Windows 同名同形)+ 可选分页 `offset_line` / `max_lines`(与 Windows `fs_read` 同名);
     不传分页参数时输出与 Windows `Diagnose.LogExcerpt` 同形(`文件: 路径` + 末尾 6000 字);
   - 超过 8000 字的结果照旧由 `ToolRunner.Clip` → `Artifacts.Store` 落盘,模型用 `read_artifact` 回读(有测试)。
2. **自检加固(动态,不写死清单)**:新增 `AiToolRefs`
   - 来源:`AiData.SystemPrompt()`、`AiFixChat.BuildCrashContext/FixSystemPrompt`(用样例报告现生成)、
     locales 里「提到任一已注册工具名或键里带『工具』」的条目;
   - 抠出 snake_case 标识符与 `xxx_*` 通配写法,排除 schema 参数名、文件名(后跟 `.ext`)、`mcp_` 外部工具;
   - 每个点名的工具必须同时:在 `Tools.Names()`、`ToolMeta.Get` 非空、`ToolRunner.DispatchesAsync` 认;通配至少匹配一个已注册工具;
   - `ToolRunner.DispatchesAsync`:switch 没法反射,改用**无副作用的真调用**探针 —— 后端一调就抛、每个 schema 参数都塞一个
     「一转字符串就抛」的探针值,真进了 case 会在动机器之前抛出,只有落到 default 的才返回 `unknown_tool`。完全跟着真实 switch 走,
     其他包新加的工具不用在这里登记;
   - `--ai-selftest` 新增两条检查,xunit 另有「schema 里每个工具执行器都认」的全量检查。
   - **变异验证**:临时删掉 switch 里的 `case "get_latest_log":`,`MentionedTools_…` 与 `Dispatch_…` 两条用例立刻红,
     逐条报出 5 处来源「提到 get_latest_log:ToolRunner 分发不认」;原来的 `context.Contains("get_latest_log")` 检查照样是绿的。
3. **文件工具 `fs_list` / `fs_read` / `fs_write`**(照 Windows `ExtraTools.cs:497-793`)
   - 允许根照 Windows `FsRoots`:启动器数据目录 + 实例根 + 有独立 saves 的 `versions/<版本>/`;相对路径基于实例根;
   - 路径守卫(比 Windows 严):`..`、驱动器相对(`C:foo`)、备用数据流(`a.txt:x`)、UNC / `\\?\` 设备路径、根相对(`\x`)、
     8.3 短名、末尾带点 / 空格的段、Windows 设备名(`nul`/`con`)一律拒;**逐段跟随符号链接 / 目录联接**算真实路径后再判是否在允许根内
     (Windows 版 `PPath.Resolve` 只是 GetFullPath,实例里放个指向外面的链接就能出去);系统目录(Windows 那 6 个 + Linux `/etc` `/usr` `/proc` …)、
     `.ssh` / `.gnupg` 拒;
   - 启动器账号 / 配置(`accounts.json`、`config.json`、`device_id`)读写都拒 —— 否则「fs_write 改 config.json 把权限档改成 yolo」就能绕过确认链;
     AI 状态文件(`ai_chats.json`、`ai-memory.json`、`ai_memory.md`)与 `cache/`(检查点、产物)不许写;脚本 / 二进制后缀不许写;
   - `fs_write`:`write_local` / risk **high** → 走 `ToolRunner.Confirm` 确认链;`Preview.ChangePreview` 新增 `fs_write` 分支给逐行 diff
     (新建 / 追加有标注,超 200 行落盘);`Tools.AffectedPaths` 新增 `fs_write` 分支 → `AiAgent.SnapshotForTool` 做检查点,
     `/撤回` 能还原改过的文件、删掉新建的文件(有测试);确认卡**不给「本对话都允许」**(D-24 危险类,`ConfirmCard.AllowAlways`);
     执行、预览、检查点三处用同一个守卫算出的真实路径,不会出现「检查的是 A、写的是 B」。
4. **`open_folder`**:复用 `VersionOps.OpenFolder`(自带实例内校验)/ `Crash.OpenPath` → `Crash.ShellOpen`
   (Linux `xdg-open`、macOS `open`、Windows 跑 Avalonia 时 ShellExecute → 资源管理器),测试用 `Crash.ShellOpenHook` 截住。
   kind:Windows 那 8 个(game/mods/saves/resourcepacks/shaderpacks/config/logs/backups)+ Linux FolderMap 已有的
   datapacks/screenshots/crash-reports/root/version,另认几个中文别名;不认识的 kind 报错而不是悄悄打开实例根。
5. **系统提示最小追加**:`AiData.SYSTEM_PROMPT` 末尾加一节「# 日志与文件」(4 行用法);`KEYWORD_TOOLS` 照 Windows `ExtraTools.Keywords`
   加「文件夹/目录/folder → open_folder」「latest.log/读文件/… → fs_*」两行。

## 文件清单

新建(本包独占,合并无冲突):
- `src/TipCL.Core/Launcher/Ai/AiFsTools.cs` —— 6 个工具的实现 + 路径守卫 + schema JSON
- `src/TipCL.Core/Launcher/Ai/ToolRunner.LogsFs.cs` —— `ToolRunner` 分部:`RunLogsFs` 分发入口 + `DispatchesAsync` 探针
- `src/TipCL.Core/Launcher/Ai/AiToolRefs.cs` —— 提示点名工具 ↔ 注册表 一致性检查
- `tests/TipCL.Tests/Core/AiLogsFsTests.cs` —— 19 个用例
- `progress/AI-logs-fs.md` —— 本文

共享文件(都是集中一处的追加式改动):
- `ToolRunner.cs`:类声明加 `partial`;switch 在 `default:` 前加一组 6 个 case → `RunLogsFs`
- `Tools.cs`:`Schemas` getter 里 `cached.AddRange(AiFsTools.Schemas())` 一行;`Labels` / `Formats` 末尾各加几行;`AffectedPaths` 加 `fs_write` 分支
- `AiToolTypes.cs`:`ToolMeta.ALL` 末尾 6 行
- `AiData.cs`:`SYSTEM_PROMPT` 末尾一节;`CORE_TOOLS` 末尾 2 项;`KEYWORD_TOOLS` 末尾 2 行
- `AiSelfTest.cs`:第 8 节后加 8b(两条检查)
- `Preview.cs`:`ChangePreview` switch 开头加 `fs_write` 分支(委托 `AiFsTools.WritePreview`)
- `src/TipCL.App/Pages/Ai/ConfirmCard.cs`:`AllowAlways` 加 `"fs_write" => false`
- `locales/zh_CN.json`、`en.json`:新增 1 键「（追加到文件末尾）」(紧跟「（新建文件）」)

## 新工具

| 工具 | 风险 | side_effect | 只读 | 说明 |
|---|---|---|---|---|
| `get_latest_log` | low | read | 是 | 实例 / 版本隔离目录的 `logs/latest.log`;可分页 |
| `get_crash_report` | low | read | 是 | `crash-reports` 里最新一份;可分页 |
| `fs_list` | low | read | 是 | 列目录,≤200 条,glob 过滤 |
| `fs_read` | low | read | 是 | 读文本,默认末尾 200 行,≤512 KB,拒二进制 |
| `fs_write` | **high** | write_local | 否 | 确认链 + diff 预览 + 检查点;≤256 KB;不给「本对话都允许」 |
| `open_folder` | low | none | 是 | 用系统文件管理器打开实例子目录(与 Windows 同为只读分级) |

## 测试

- 新增 xunit **19** 个(`AiLogsFsTests`),`--ai-selftest` 新增 2 条检查(总数 54 → 56)。
- 全量:`dotnet test` 通过 988 / 跳过 7 / 失败 0;Debug 构建 0 错误,警告仍是基线的 3 条(`LayoutMotionAndHeightTests` 的 xUnit 分析器警告,未新增);
  `--ai-selftest`(临时 TIPCL_HOME)exit=0,通过 56 / 失败 0。
- 覆盖:三处来源点名都注册(并验证扫描真的扫到东西)、合成未注册名三处缺失都报、通配匹配 / 不匹配、参数名 / 文件名 / mcp_ 不误报;
  schema 里每个工具执行器都认、未知名不认;最新日志末尾 / 分页 / 缺失;崩溃报告按 mtime 取最新;超长结果落盘 + `read_artifact` 回读;
  fs_list glob / fs_read 相对与绝对路径;`..`、`..\`、绝对路径逃逸、盘根、`C:x`、UNC、`\\?\`、ADS、8.3、尾点尾空格、设备名、`/etc/passwd` 全拒;
  **目录链接逃逸**(Windows 用目录联接,Linux 用符号链接)读 / 列 / 写都拒、指向允许范围内的链接照常可用;账号 / 配置 / 状态 / 缓存 / 脚本 / 二进制写入拒;
  二进制 / 超大 / 空字节拒读;fs_write 无确认回调拒、用户拒绝不落盘、同意时确认卡拿到 diff + 标签 + 理由、追加预览、plan 档直接拒且不弹卡、超限拒写;
  `AffectedPaths` → 检查点 → 回滚还原修改并删掉新建文件;open_folder 各 kind 走 `Crash.ShellOpen`、默认 / backups 在实例内、坏 kind 报错不打开。

## 遗留问题与风险

- **探针的前提**:`DispatchesAsync` 依赖「每个 case 在副作用之前先读参数或调后端」。现有工具全部满足;若以后有人写出
  「不读参数、不调后端就直接写盘」的写工具,探针会真执行它。`--ai-selftest` 只探提示里点名的工具(在用户机器上跑),全量探测只在 xunit 的临时目录里跑。
  已知轻微副作用:探 `write_mod_config` / `list_mod_configs` / `read_mod_config` 时,`ToolRunner.ConfigDir` 会给默认实例建一个(本来就该有的)`config/` 目录。
- **提示扫描的约定**:系统提示 / 修复上下文里出现的 snake_case 标识符一律视为工具名(参数名、`xxx.json` 这类文件名除外)。
  以后若要在提示里写配置键(如 `ai_permission_mode`),要么改写法,要么在 `AiToolRefs.Scan` 里加排除规则 —— 自检会直接指出是哪个来源的哪个词。
- **与 Windows 的差别**(都是更严):路径守卫跟随链接、拒账号/配置文件与脚本/二进制写入、`get_crash_report` 取最新(Windows 取排序第一 = 最旧)、
  `get_latest_log`/`get_crash_report` 多两个可选分页参数、`open_folder` 不认识的 kind 报错。
- `SYSTEM_PROMPT` 原注释写「与 Windows 版逐字相同」—— 本包在末尾追加了一节,前文一字未改。
- fs_write 在 `edit` / `acceptEdits` 档下按既有规则(write_local 直接执行)不弹确认,与 Windows 一致;靠路径守卫保证改不到账号 / 配置 / 状态文件。

## 合并注意

- `ToolRunner.cs` 的 `public sealed partial class ToolRunner`:其他包若也加了 `partial`,是同一行同样的改法,取任一即可。
- `ToolRunner.cs` switch 的 `default:` 前、`ToolMeta.ALL` 末尾、`Tools.Labels/Formats` 末尾、`CORE_TOOLS/KEYWORD_TOOLS` 末尾、
  `SYSTEM_PROMPT` 末尾都是追加点,与其他包大概率在同一位置相邻追加 —— 两边都保留即可。
- `Tools.Schemas` getter 里加了一行 `cached.AddRange(AiFsTools.Schemas());`;别的包若用同样方式接 schema,保留各自一行。
- **合并后务必跑一次 `--ai-selftest` 与 `AiLogsFsTests`**:它们会把「别的包在提示里点名、却漏注册某一处」的工具当场报出来(这正是本包的目的)。
- `ConfirmCard.AllowAlways` 的 switch:只加了一个分支。

## 要改的 README / PARITY 行(合并时由主代理改)

- `docs/PARITY.md:239`(`PluginTools.cs` 行)里「34 项内置」→ 改为实际数量(本包后为 42 项:原 36 + 本包 6;再加上其他包的)。
- `docs/PARITY.md` 的 ExtraTools 相关描述(若有「fs_* / open_folder 未移植」字样)改为「已移植:fs_list / fs_read / fs_write / open_folder(Linux 侧路径守卫更严,见 progress/AI-logs-fs.md)」;
  并记一条「get_latest_log / get_crash_report 已与 Windows 同名同形注册(Linux 多两个可选分页参数)」。
- `README.md:152` 目录树里 `Launcher/Ai/` 的说明可补「日志 / 文件 / 打开文件夹工具」。
