# AUDIT-E — 一致性与可维护性审计报告

- 审计员:aud-consistency(分支 `audit/E`,基线 main `b86afe3`;Android 仓分支 `audit/E-android`,基线 `main` `6dc4a3c`)
- 日期:2026-11-20
- 范围:i18n 词表一致性 / 版本与更新机制一致性 / 文档与实现一致性 / Android 仓库可复现性。**安全类不在范围**。
- 纪律:所有结论都带证据(命令输出 / 代码行 / 线上发行版 API 实测)。本报告不代替其他线路改它们的地盘。

## 0. 计数总览

| 部分 | P0 | P1 | P2 | 已修 |
|---|---|---|---|---|
| 1 i18n 词表 | 0 | 1(Linux 侧 94% 文案没接线) | 12(含 5 条口径类) | 0(词表不归我,给精确清单) |
| 2 版本与更新 | 0(险情 2 项已核销) | 1 | 1 | 1(README 的 sha256) |
| 3 文档与实现 | 0 | 4 | 5 | 6 个文件已改 |
| 4 Android 仓库 | 0 | 2 | 3 | 5 个文件已改 |

> 「P0」口径:会让用户拿到错误包 / 更新失败 / 文档里的关键命令跑不通。**本次没有命中 P0**;最接近的是 README 里过期的 `TipCL.exe` sha256(按它校验会对不上现在线上的附件)—— 已修,故从 P1 降级销账。

---

## 1. i18n 词表一致性

### 1.1 词表结构(先钉事实)

四份词表都是**「中文原文 → 译文」的扁平映射**:键 = 中文原文,`zh_CN.json` 是恒等映射,`en.json` 是译文。
加载点:`src/TipCL.Core/Launcher/I18nCore.cs:34-69`(`CoreResources.ListDir("locales")`)。

| 文件 | 条数 |
|---|---|
| `src/TipCL.Core/Resources/locales/en.json`(Linux) | 3028 |
| `src/TipCL.Core/Resources/locales/zh_CN.json`(Linux) | 3028 |
| `windos/src/TipCL/Resources/locales/en.json`(Windows) | 3030 |
| `windos/src/TipCL/Resources/locales/zh_CN.json`(Windows) | 3030 |
| `windos/.../en.json.gz`、`zh_CN.json.gz` | 解压后与同名 `.json` **完全一致**(已比:`identical=True`) |

### 1.2 (a) 同一实现内 en ↔ zh_CN 键集差

| 实现 | en − zh | zh − en |
|---|---|---|
| Linux | **0** | **0** |
| Windows | **0** | **0** |

⇒ 两份实现内部都**没有**「一边有译名、另一边没条目」的洞。

### 1.3 (b) 两侧实现(Linux 表 vs Windows 表)键集差

| 方向 | 条数 | 性质 |
|---|---|---|
| 只在 Linux 表 | **58** | 多为 Linux 侧独有话题(插件安装器报错、布局工具栏、快捷方式),或与 Windows 侧**同义不同措辞** |
| 只在 Windows 表 | **60** | 多为 Windows 侧布局 L6 自建区块面板、AI 历史会话、实例名/时间格式等新文案 |

同义不同措辞的代表(会造成「两个版本说法不同」,P2):

| 概念 | Linux 键 | Windows 键 |
|---|---|---|
| 安装页选项区标题 | `安装选项` | `版本与附加选项` |
| 自建区块缺失 | `自建区块不可用` | `自建区块不存在` |
| 导入布局提示 | `已导入 {0} 个页面的布局` | `已导入 {0} 个页面的布局(下次打开对应页面生效)` |
| 下载页结果区标题 | `结果列表`(Linux 代码用,**Linux 表里没有**) | 只在 Windows 表里 |

### 1.4 (c) 代码取词 key 是否都在词表里(精确清单)

取词调用:`Linux = I18nCore.Tr(...) / Tr(...)`(别名 `src/TipCL.Core/Launcher/Ai/Preview.cs:610`);`Windows = I18n.L(...)`。

| 侧 | 字面量 key(去重 / 命中) | 词表里缺的 key |
|---|---|---|
| Linux | 249 / 280 | **1 条** |
| Windows | 2106 / 3112 | **6 条**(其中 3 条是真缺口) |

**必须补的 key(硬缺口):**

| # | key | 用到它的位置 | 现状后果 | 建议文案(zh / en) |
|---|---|---|---|---|
| L1 | `结果列表` | `src/TipCL.App/Pages/Download/CatalogPage.cs:182`(`BlockSpec.Title`) | 英文界面里这一块的标题**显示成中文原文**(`Tr` 缺键回落到 key) | zh:`结果列表` / en:`Results list` · 两侧词表都补(`zh_CN.json` 恒等、`en.json` 译文) |
| W1 | `上一次布局` | `windos/src/TipCL/Pages/SettingsLayoutPage.cs:1396` | 同上;**Linux 表有这条**,抄过去即可 | zh:`上一次布局` / en:`Previous layout` |
| W2 | `已随装前置:` | `windos/src/TipCL/Core/BackendApi.cs:1662` | 同上;**Linux 表有这条** | zh:`已随装前置:` / en:`Bundled prerequisites:` |
| W3 | `更新包已下载,重启启动器即可完成更新` | `windos/src/TipCL/Core/BackendApi.cs:3858` | 同上;**Linux 表有这条** | zh:原文 / en:`Update package downloaded — restart the launcher to finish updating` |

**不是缺口、不必补**(已核):`1.20.5 – 1.21.11`(版本区间)、`AI`、`{0}px` —— 这三个是**格式串/专名**,给不给译文都成立。

### 1.5 (d) 死 key(P2,仅记录)

| 侧 | 词表里代码从不字面引用的 key | 占比 |
|---|---|---|
| Linux | **2780 / 3028** | 91.8% |
| Windows | **930 / 3030** | 30.7% |

Linux 侧这个数**不是「词表脏」,而是「界面没接线」**:仓库自带审计工具 `tools/i18n-audit.py` 的实测(本机复跑,输出见下)是 338 个 `.cs` 里 **CJK 字面量 5194 个,wrapped 只有 299(5.76%),unwrapped 4895(94.24%,去重 3624,分布 173 个文件)**;其中 **1140 个 unwrapped 字面量在词表里已有条目**,即「有译文却没用上」占 31.46%。

```
== i18n audit (Linux/Avalonia tree) ==
  files      : 338 .cs (ignored 0)
  tables     : en=3028 zh_CN=3028  key sets equal: True
  CJK literals: 5194
  wrapped     : 299   (5.76%)
  unwrapped   : 4895   (94.24% ... distinct 3624, across 173 files)
  wrapped keys : 266     missing en: 1     missing zh: 1   -> 结果列表
  unwrapped by file (top): Catalog.cs 330 | SettingsPage.cs 191 | SettingsPage.Layout.cs 187 | Crash.cs 153 | Modpack.cs 153 | SettingsSubs.cs 144 | VersionManagePage.cs 143 | CatalogPage.cs 141
```

⇒ **这是本审计发现的 i18n 最大一笔欠账(P1 口径,但属 B 线地盘)**:Linux 版把界面语言切成 `en` 后,约 94% 的中文文案不会变。Windows 侧同类工具是 `--i18n-check`(`windos/src/TipCL/I18nCheck.cs`),Linux 侧用 `python tools/i18n-audit.py`。

### 1.6 (e) 同一 key 两侧文案差异

| 维度 | 结论 |
|---|---|
| zh_CN 文案(2970 个共有 key) | **0 处不同** —— 中文侧两侧完全一致,不存在「两个版本说法不同」 |
| en 文案 | **10 处不同**(纯措辞,语义相同,P2) |

10 处 en 分叉(建议**统一取 Linux 侧**写法,它更短):

`AI 助手在标题栏右上角`、`全部入口都在标题栏上`、`启动、实例库、下载、插件、设置五个页签都在标题栏上;插件装好后会自己在「设置」右边加一个页签。`、`安装时飞入底部任务条`、`导航设置读取失败`、`按住区块拖动移动 · 拖右/下边角缩放`、`本页已恢复默认布局`、`版本安装`、`该版本已开启隔离:模组/日志/资源包位于 versions\<版本> 目录下,与实例根目录分开`、`顶栏页签、AI 标签、首页卡片、设置页、提示框`。
例:`版本安装` → Linux `Version install` / Windows `Install version`;`导航设置读取失败` → Linux `Couldn't read the navigation settings` / Windows `Failed to read navigation settings`。

### 1.7 (f) 占位符 `{0}`/`{1}`/`{name}` 一致性

**结论:两侧四份词表都没有占位符不匹配的条目(0 条 P1)。**

- 检查方法:对每个 key,比较 `key(中文原文)` / `zh_CN 值` / `en 值` 三处的 `{...}` 与 `%s` 序列,必须三处相等。
- 唯一的两条告警是**正则误报**:`100% 为纯色;...` 与 `100% 为纯色;...侧边栏...`,英文 `100% is solid; ...` 里的 `% i` 被当成了 printf 占位符。**不是缺陷**。
- 动态 key(拼接/变量传入,**需人工核对**,两侧共 57 个调用点)清单在本审计的原始输出里;其中值得盯的两组:
  - `Linux src/TipCL.Core/Launcher/Ai/Preview.cs` 用**具名占位符** `{path}` / `{n}` / `{size}` / `{name}` / `{ver}` + `.Replace(...)`,不经过 `string.Format`;这些 key 都在表里,但**译文里的 `{path}` 必须原样保留**(当前 en 译文保留完好)。
  - `Linux src/TipCL.Core/Layout/PageLayoutIO.cs:36,41` 与 `PluginMarket.cs:229`、`Windows PluginManager.cs:1182` 把**变量**当 key 传进 `Tr`(插件权限描述、布局块标题),它们的 key 来自 `PluginPermissions.Describe()` / 文档里的中文标题 —— 这批 key 若在表里没有,英文界面会显示中文(与 1.4 的「死 key」互为镜像,批量补词条的成本比逐条接线低)。

### 1.8 词表相关的「需其他线路执行」

| 线路 | 文件:行 | 具体改法 |
|---|---|---|
| A / 词表负责人 | `src/TipCL.Core/Resources/locales/zh_CN.json`、`en.json` | 加 1 条:`"结果列表": "结果列表"` / `"结果列表": "Results list"`(修 `CatalogPage.cs:182`) |
| C / 词表负责人 | `windos/src/TipCL/Resources/locales/{zh_CN,en}.json` + 重压同名 `.gz` | 加 3 条:`上一次布局`、`已随装前置:`、`更新包已下载,重启启动器即可完成更新`(文案直接照抄 Linux 表,见 1.4 表) |
| B(Linux UI) | 见 1.5 的 top 文件表 | i18n 接线欠账:先接「词表里已有条目」的 1140 个字面量(性价比最高),再议其余 |
| 任意 | 四份词表 | 10 处 en 措辞分叉统一取 Linux 写法;58/60 条跨实现 key 分叉按「同概念取同一句」收敛(P2,可不做) |

---

## 2. 版本与更新机制一致性

### 2.1 版本号:全仓一致 = `1.2.0`

| 落点 | 值 | 证据 |
|---|---|---|
| `src/TipCL.Core/Launcher/AppInfo.cs:10,16,18` | `APP_VERSION` / `LAUNCHER_VERSION` / `__version__` = `1.2.0` | 读文件 |
| `src/TipCL.Core/CoreInfo.cs:8` | `1.2.0` | 读文件 |
| `src/TipCL.App/TipCL.App.csproj:8` | `<Version>1.2.0</Version>` | 读文件 |
| `src/TipCL.Core/TipCL.Core.csproj:10` | `<Version>1.2.0</Version>` | 读文件 |
| `windos/src/TipCL/TipCL.csproj:17` | `<Version>1.2.0</Version>` | 读文件 |
| `update.json` / `update-linux.json` | `"version": "1.2.0"` | 读文件 |
| git tag | `v1.2.0` → `39056c8`(2026-11-15 14:57) | `git rev-list -n1 v1.2.0` |

### 2.2 清单字段 ↔ 更新器读取字段(逐字核对)

| 清单字段 | Linux `Updater.Check`(`src/TipCL.Core/Launcher/Updater.cs:97-102`) | Windows `Updater.Check` | 结论 |
|---|---|---|---|
| `version`(可回退 `latest`) | `Py.Or(G(data,"version"), G(data,"latest"))` | 同 | ✅ |
| `notes`(可回退 `changelog`) | `Py.Or(G(data,"notes"), G(data,"changelog"))` | 同 | ✅ |
| `url`(可回退 `download`) | `Py.Or(G(data,"url"), G(data,"download"))` | 同 | ✅ |
| `sha256` | `Py.Lower(Py.Strip(...))` + `ValidSha256(\A[0-9a-fA-F]{64}\z)` | 同 | ✅ |
| 清单地址 | `Config.DEFAULT_UPDATE_URL = .../main/update-linux.json`(`Config.cs:13`,可被 `TIPCL_UPDATE_URL` 覆盖,`:23-27`) | `.../main/update.json`(`Config.cs:12`) | ✅ 平台分流正确 |
| 降级保护 / 校验 | 版本不高于本地 → `has_update=false`;sha 非法 → `ok=false`「已拒绝自动更新」;下载后再比 `Sha256File` | 同 | ✅ |

### 2.3 清单 ↔ 线上资产(2026-11-20 用 GitHub API 实测)

| 清单项 | 清单值 | 线上资产实测 | 结论 |
|---|---|---|---|
| `update.json.url` | `.../v1.2.0/TipCL.exe` | 存在,5,604,352 B | ✅ |
| `update.json.sha256` | `a7dba497…5ac6` | 资产 digest = `a7dba497ed1729cbc63aad5c0a784f2707b42426910dda6741eab8e42b1c5ac6` | ✅ **逐字相同** |
| `update-linux.json.url` | `.../v1.2.0/TipCL-linux-x64.AppImage` | 存在,39,090,680 B | ✅ |
| `update-linux.json.sha256` | `a98e7d35…f178` | 资产 digest = `a98e7d35930315258fb5980b042a56957c06a24a41576c9225d976132cd2f178` | ✅ **逐字相同** |
| 另有资产 | — | `TipCL.exe.sha256`、`tipcl_1.2.0_amd64.deb`(37,641,702 B,digest `506a69bc…`) | ✅ 与 `release.yml`、README 描述一致 |

`release.yml` 的三个产物名(`TipCL.exe` / `TipCL-linux-x64.AppImage` / `dist/*.deb`)与清单 URL、README 表格**完全一致**。

### 2.4 已修:README 里过期的 `TipCL.exe` sha256(本可导致用户按文档校验失败)

- 事实:`update.json` 在 `b86afe3` 把 `sha256` 从 `fcc10d90…8857` 换成 `a7dba497…5ac6`(commit message:「v1.2.0 发行版的 Windows 附件替换成 2026-11-19 的构建…版本号保持 1.2.0 不变」),但 `README.md:222` 还写着旧值 `fcc10d90…`。
- 影响:照 README 校验**现在下载到的** `TipCL.exe` 必然不匹配。
- 处置:已改 `README.md:222` 为 `a7dba497…`,并注明「2026-11-19 替换后的附件,与 `update.json` 逐字一致」。

### 2.5 P1(未修,需发版决策):同一版本号对应两个不同二进制 ⇒ 自动更新不可达

- 事实链:
  1. `v1.2.0` 发布于 2026-11-15(tag `39056c8`),11-19 又**替换**了 Windows 附件(`b86afe3`),版本号**没变**;
  2. HEAD 比 tag 领先 **67 个提交**(`git log --oneline v1.2.0..HEAD | wc -l`),含 L6 自建区块面板、布局审计修复 —— 这些都在「1.2.0」这个号下;
  3. 客户端判据:`Updater.Newer(remote, local = AppInfo.APP_VERSION)`,`1.2.0 > 1.2.0` = **false**(`RELEASE-DRAFT.md:344` 有同一张真值表)。
- 后果:装着 11-15 版 1.2.0 的 Windows 用户**永远不会**被提示更新到 11-19 的修复件;Linux 的 AppImage / `.deb` 干脆还是 11-15 那一版(发行说明明写「本次只替换了 Windows 附件…仍是首发那一版」)。
- **修法(不归我,需 lead / 发版负责人拍板)**:下一个包**必须换号**(如 `1.2.1`),再按 `RELEASE-DRAFT.md §2` 的三步回填 `version` / `url` / `sha256`。只改清单不换号无效。

### 2.6 P2

- `update.json` 结尾**没有换行**(`b86afe3` 的 diff 末尾 `\ No newline at end of file`),后续每次编辑都会产生「同一行被改两次」的假 diff;建议补一个 `LF`(纯格式,不影响 `json.load` 与 `UpdaterManifestGuardTests`)。
- `docs/RELEASE-DRAFT.md` 里「清单三行仍是占位」等段落已过期 —— 已在文首加「AUDIT-E 补记」指明现状,原文保留为历史。

---

## 3. 文档与实现一致性

### 3.1 已核实的「文档命令真能跑通」(本机实跑,均在我的 worktree 内)

| 文档 | 命令 | 结果 |
|---|---|---|
| `README.md:103` | `dotnet build windos/src/TipCL/TipCL.csproj -c Release` | ✅ `已成功生成。0 个警告 0 个错误`,产物 `windos/src/TipCL/bin/Release/net48/TipCL.exe` |
| `README.md:109` | `dotnet test windos/tests/TipCL.Tests` | ✅ `失败: 0,通过: 547,已跳过: 0,总计: 547`(本机是便携 SDK,需同时设 `DOTNET_ROOT`;标准安装不需要) |
| `README.md:126` | `dotnet build TipCL.Linux.sln` | ✅ 加 `-m:1` 后 `0 警告 0 错误`;**裸跑失败**于 `NuGetScratch\lock\...`(并发还原锁,与工程无关;`docs/README.md:42` 本来就推荐 `-p:UseSharedCompilation=false -m:1`) |
| `docs/README.md:14` | `tools/` 里的 `build.sh / build.ps1 / package.sh / package.ps1 / deb.sh` | ✅ 五个文件都在(另有 `verify-linux.sh`、`verify-smoke.ps1`、`i18n-audit.py`) |
| `docs/README.md:20` | 「版本号必须三处一致」 | ✅ 实测三处 + `AppInfo.APP_VERSION` 同为 `1.2.0` |

### 3.2 已修:文档说「还没落地 / 按钮是灰的」,而代码里已经实现(P1)

| 文档 | 原文口径 | 代码事实 | 处置 |
|---|---|---|---|
| `README.md:87` | Linux 版「尚未落地设置页布局视图 / 分栏 / 方案 / JSON 卡片 / AI 排版」 | 前四项**都已落地**:`Pages/Settings/SettingsPage.Layout.cs`(7 小节)、`Controls/PageLayoutHost.cs`(`SplitCatalog`/`SplitRatioStore`/`PageLayoutHost`)、`Pages/Layout/LayoutProfiles.cs`、`Pages/Layout/LayoutJsonEditor.cs`、`Pages/Layout/ShellTabs.cs`;**只有 AI 排版(`LayoutTools`/`AI 备份`)仍缺** | 改写为现状 + 指明「唯一还缺 AI 排版」 |
| `README.md:92` | 「设置页里的『添加自建区块』按钮**本版未启用**」 | Windows 已启用:`SettingsLayoutPage.cs:448-499`(`LayoutWidgetRows`)`/ :502-580`(`WidgetCreateForm`),L6 提交 `da8b83f`(2026-11-19);发行说明「修订(2026-11-19)…自建区块管理面板启用」 | 改写为「已启用 + 三条 JSON 路径仍可用」 |
| `docs/README.md:100-112` | 「布局 v2:Linux 真实进度(截至 `1823c3c`)…**尚未落地**:设置页布局视图 / 分栏 / 方案 / JSON 卡片 / 页签」 | 同上,四项都已落地 | 表重建为 11 行「能力 → 落点」,「尚未落地」收紧到 AI 排版工具一条 |
| `docs/布局快速上手.md:92,94,198` | FAQ 标题「按钮为什么是灰的」+「本版(1.2.0)这个入口还没启用」+ 证据行「置灰 `SettingsLayoutPage.cs:427-432`」 | 已启用(L6) | 标题与正文重写;证据行改成 `:448-499 / :502-580` |
| `docs/布局使用指南.md:9-15` | 平台范围「Linux 树没有设置页布局视图/分栏/方案/JSON 卡片/AI 排版」 | 只缺 AI 排版 | 重写平台范围注 |
| `docs/布局使用指南.md:293-295,466,521` | 「按钮故意置灰、处理器传 `null`」 | 已启用 | 三处同步改(含 §11 限制表与证据表) |
| `docs/RELEASE-DRAFT.md` | §4 / 已知问题第 5 条「界面上的自建区块入口本版未启用」 | 对 11-15 的原始构件成立,对 11-19 替换件不成立 | 文首加「AUDIT-E 补记」,原文保留为历史 |
| `GOAL.md` | 路径 `D:\项目\tipcl\linux\`、SDK `10.0.403`、版本 `0.1.0-m1/m3` | 现路径 `D:\项目\TipCL\TipCL\`、SDK `8.0.428`、版本 `1.2.0` | 文首加「口径注记」,(历史原文不动) |

### 3.3 P2(只报告,未改)

| 位置 | 问题 | 为什么没改 |
|---|---|---|
| `README.md:92`(改后仍保留的半句) | 「窄窗 / 宽窗两套布局…编辑器对它是只读的,不能手动编辑」 | 代码里 `LayoutVariantWrite.SourceOf/Apply`(`windos/.../LayoutEditor.cs:3116-3140`)是「显示哪一份就写哪一份」,**并没有把变体编辑锁成只读**;但发行说明又说「本版没有『在编辑哪一套』的选择器」。两种说法都能自圆其说 ⇒ 不确定不动,留给 C 线核实 |
| `docs/DELIVERY.md:3,33,90,142` | 通篇是 `0.1.0-m3` 的交付记录 | 是历史交付快照,改写会毁证据;读者从标题日期能看出是旧包 |
| `docs/PARITY.md:672,675`、`docs/HANDOFF-d95a9472.md:60,85` | 「`update-linux.json` 仍是占位 `REPLACE-WITH-THE-REAL-SHA256`」等 | 同上,当时事实;已被 `1.2.0` 发版覆盖 |
| `docs/README.md:58,100` | 「Linux 树只认环境变量,不解析命令行开关(实测 `1823c3c`)」 | 与现状仍一致(`Program.cs:17-22` 只认 `--smoke` / `--ai-selftest` / `-i launch`),但仍是**日期限定**的实测口径,后续若加开关要同步 |
| `docs/布局快速上手.md:136` | 「改成网格/画布 目前**没有界面下拉框**」 | 实测 `SettingsLayoutPage.cs` 里没有 mode 选择控件(`grep` 只命中 `SplitKind.GridCols` 与校验分支)⇒ **仍然成立**,不改 |

---

## 4. Android 仓库(`D:\项目\TipCL\TipCL-Android`,分支 `audit/E-android`)

### 4.1 关键事实:仓库里有**两条**安卓构建线,包名/版本都不同

| 构建线 | 工程 | 包名 / launcher activity | 版本 | 脚本 | 产物 |
|---|---|---|---|---|---|
| A 自研 Gradle(主线) | `android-app/`(7 模块) | `com.tipcl.android` / `.LauncherActivity`(`android-app/app/src/main/AndroidManifest.xml:11-18`) | `versionName 1.2.0`,`versionCode 1`(`android-app/app/build.gradle.kts:15-16`) | `scripts/build_rewrite_release.bat` | `dist\TipCL-<versionName>-android.apk` |
| B apktool 重打包 | `app/Android/`(解包树)+ `补丁smali/` | `com.pymcl.android` / `com.pymcl.android.MainActivity` | `versionName 0.9.3`,`versionCode 14`(`app/Android/apktool.yml:14-16`) | `scripts/build_release.bat` | `dist\TipCL-<版本>.apk` |

### 4.2 已修

| # | 文件 | 问题(证据) | 改法 |
|---|---|---|---|
| A1(P1) | `README.md` | 只写了 B 线,还把 `app/Android/` 说成「完整可重建的安卓工程」;A 线 7 模块 Gradle 工程与 `build_rewrite_release.bat` **一个字没提**。`docs/REWRITE-PLAN.md:56` 自己把这条记成「遗留疑问 4」 | 新写「两条构建线」对照表 + 两节构建说明(含签名差异、NDK 要求、媒体/设备脚本用法) |
| A2(P1) | `TipCL-触控改动说明.md:95-108,166-171` | 重建命令引用的 `apktool_out\`、`tools\apktool.jar`、`tools\SurgicalAssemble.java`、`backup\...apk`、`surgical\`、`tools\uber-apk-signer.jar` 和 `"C:\Program Files\Java\jdk-25.0.5.0.2\bin\jar.exe"` **全都不在仓库里**(仓库里是 `app/Android/`、`重建工具/SurgicalAssemble.java`、`.tools/`(gitignore));照抄必失败 | 改成仓库真实路径;签名改用 `scripts\keystore\tipcl.keystore` + `%JAVA_HOME%`;并补一句「整包重编会损坏 Compose 类,正式流程用外科式重编」 |
| A3(P2) | `scripts/device/install.bat:8-9` | 硬编码 `PKG=com.tipcl.android` / `ACT=%PKG%/.LauncherActivity`,而 APK 搜索顺序第一条是 `dist\*.apk` —— `build_release.bat` 正好往 `dist\` 产出 **B 线**的包 ⇒ 装得上、`am start` 必失败 | 包名/Activity 支持 `TIPCL_PKG` / `TIPCL_ACT` 覆盖;默认 A 线失败后**自动回落**试 `com.pymcl.android.MainActivity`,再报错 |
| A4(P2) | `scripts/build_release.bat:13` | `set VERSION=0.9.3` 与 `apktool.yml` 版本各写一份,改一处就漂 | 改成从 `app/Android/apktool.yml` 的 `versionName` 读(实测 `PARSED=[0.9.3]`、文件名 `TipCL-0.9.3.apk` 与改前一致),读不到才回落并告警 |
| A5(P2) | `.gitignore` | `scripts/device/run_all.bat:13-14` 往**仓库根**写 `logs\session_*.md` 与采样日志,而 `logs/` 没被忽略 ⇒ 每跑一次设备回归就挂一堆未跟踪文件 | 加 `logs/` |

### 4.3 只报告(未改)

| 位置 | 现象 | 说明 |
|---|---|---|
| `app/Android/apktool.yml` vs `android-app/app/build.gradle.kts` | B 线 `0.9.3` / `versionCode 14`,A 线 `1.2.0` / `versionCode 1` | 与桌面「一个产品一个号」不一致;发行说明里也明写「是否与桌面统一是拍板项」。**A 线 `versionCode=1` 若曾发布过,后续必须递增** |
| `scripts/build_release.bat:14` | keystore 口令明文写在脚本里(`KS_PASS=tipcl123`) | 属安全类,不在本次范围;只提示「脚本可读即口令可读」 |
| `tools/notices/generate_notices.py --check` | **实跑通过**:`15 个组件` / `OK: THIRD-PARTY-NOTICES.md is up to date (2316 bytes, byte-identical)` | ✅ 无需处理 |
| `scripts/device/*.bat` | 无硬编码绝对路径(grep `[A-Za-z]:\\` 零命中),只用 `%~dp0` 相对定位 | ✅ |

---

## 5. 需其他线路执行的修复清单(交 lead 分派)

| 线路 | 文件:行 | 具体改法 | 级别 |
|---|---|---|---|
| A / 词表 | `src/TipCL.Core/Resources/locales/zh_CN.json`、`en.json` | 各加 1 条 `结果列表`(zh 恒等 / en `Results list`) | P2 |
| C / 词表 | `windos/src/TipCL/Resources/locales/{zh_CN,en}.json` + **重压 `.gz`** | 各加 3 条:`上一次布局`、`已随装前置:`、`更新包已下载,重启启动器即可完成更新`(文案照抄 Linux 表) | P2 |
| B / Linux UI | `Pages/Settings/SettingsPage.cs`(191)、`SettingsPage.Layout.cs`(187)、`SettingsSubs.cs`(144)、`Pages/Version/VersionManagePage.cs`(143)、`Pages/Download/CatalogPage.cs`(141)、`Pages/AccountPage.cs`(108)、`Pages/MultiplayerPage.cs`(108)… | 把 CJK 字面量接到 `I18nCore.Tr(...)`;先做「词表里已有条目」的 1140 个;跑 `python tools/i18n-audit.py` 看覆盖率曲线 | P1 |
| B / Linux UI | `Pages/Layout/LayoutJsonEditor.cs`、`LayoutProfiles.cs`、`SettingsPage.Layout.cs` | 同上(布局 v2 面的文案) | P2 |
| C / Windows 布局 | `windos/src/TipCL/Pages/Layout/LayoutEditor.cs:3116-3140` 与发行说明口径 | 核实「编辑器能否手动编辑 `variants.narrow`」,统一 README 与发行说明的说法 | P2 |
| 发版负责人 / lead | `update.json`、`update-linux.json`、tag | **下一个包必须换号(> `1.2.0`)**,否则 1.2.0 用户拿不到 11-19 的修复;Linux 的 AppImage / `.deb` 目前仍是 11-15 构建 | P1 |
| 任意 | `update.json` | 补文件末尾换行 | P2 |

---

## 6. 本次改动清单

### TipCL 仓:branch `audit/E`,1 个提交 `b451c91`

| 文件 | 改了什么 |
|---|---|
| `README.md` | `:87` Linux 布局进度口径、`:92` 自建区块按钮已启用、`:222` v1.2.0 `TipCL.exe` 的 sha256 → `a7dba497…`(与 `update.json` 一致) |
| `GOAL.md` | 文首「口径注记」:路径迁移 / 版本 `1.2.0` / SDK `8.0.428` / 构建命令加 `-m:1` |
| `docs/README.md` | 「布局 v2:Linux 真实进度」表重建(11 项已落地 + 仅 AI 排版未落地);`:81` 产物名 `tipcl_1.2.0_amd64.deb` |
| `docs/布局使用指南.md` | 平台范围注、§7 开头、§11 限制表、§12 证据表:自建区块入口「置灰」→「已启用(L6)」 |
| `docs/布局快速上手.md` | FAQ 1 标题与正文、证据行同上 |
| `docs/RELEASE-DRAFT.md` | 文首「AUDIT-E 补记」:附件已替换、L6 面板已启用、同号不同二进制导致自动更新不可达 |

### Android 仓:branch `audit/E-android`,1 个提交 `146746e3`

| 文件 | 改了什么 |
|---|---|
| `README.md` | 「两条构建线」对照表 + A/B 两节构建说明 + 设备脚本用法 |
| `TipCL-触控改动说明.md` | 文首「成品与备份」路径换算注;「重建方法」整节改成仓库真实路径(含 apktool 落 `.tools/`、`%JAVA_HOME%`、keystore 签名) |
| `scripts/build_release.bat` | 版本号取自 `app/Android/apktool.yml`(带回落与告警) |
| `scripts/device/install.bat` | `TIPCL_PKG` / `TIPCL_ACT` 覆盖 + B 线包名自动回落 |
| `.gitignore` | 忽略 `logs/` |

> 未 push、未改任何 `src/**`、`windos/src/**`、locales、`tests/**`、`tools/**`、`.github/**`、smali/二进制。
---

## 第 6 轮(2026-11-20):合入后的文档真相 + 发版检查单

基线:`git rebase main` → `main@880deff`(我的两笔旧提交已被 lead fast-forward 合并,分支从 0 开始)。

## 6.1 为什么这轮必须改文档:R2/R3 换了手感

| 提交 | 改了什么 |
|---|---|
| `5aece65` audit/C R2 | 边缘自动滚动从"MouseMove 里推固定 18px"改成 **16ms `DispatcherTimer` + 按深度调速**,并把内容滚动折回指针内容坐标(`LayoutEditor.cs:157-172,2234`) |
| `3000b61` audit/C R3 | stack 纵向拖拽丝滑化:**浮起跟手 + 6px 滞回换位 + 170ms FLIP 让位 + 170ms 落位收敛 + 两端自动滚动**;新增纯逻辑 `Pages/Layout/StackDragPlan.cs` + 12 例单测 `windos/tests/TipCL.Tests/StackDragPlanTests.cs` |
| `ab3d49e` | Linux 侧:`Arrange 宽 ≠ 测量宽 → 按新宽重量行高`(`GridCanvasPanel.cs:667-679`) |

**旧文档的问题**:两本手册写的是"按相邻块中点实时交换,松手落位"(既没有滞回、也没有浮起与自动滚动),而且 §3 手势表里的行号(`LayoutEditor.cs:834`(`StartGesture`)、`:772`(`EndDrag`)…)在 R2/R3 各插了上百行之后**整体失效** —— 照它去 `:834` 找的是别的东西。

## 6.2 Windows 布局手册改了什么(数字全部来自代码)

| 文档 | 改动 |
|---|---|
| `docs/布局使用指南.md` | §3 手势表「换顺序(stack)」一行重写;新增 **§3.1 拖动手感(2026-11-20 改版后的精确口径)** 6 行表;§3 三个踩点的出处改成常量/方法名;§12 依据表:手势行改为**方法名定位**(不再给会漂的行号)+ 新增 3 行(StackDragPlan / LayoutMotion / 边缘滚动)+ 版本号口径行更新为"11-19 替换件 + 行号以 `880deff` 为准" |
| `docs/布局快速上手.md` | 顶部平台范围注更新;30 秒版「换顺序」行;手势速查表 stack 行重写 + 新增「拖动中靠近上下边缘」行;**新增 FAQ 10**「拖到页面底部拖不动 / 块在手指边上抖,是 bug 吗?」(4 行现象表 + 指向 Linux 差异);依据表新增 2 行、v2 文件行补回 `Core/Launcher/Layout/` 路径 |

数字口径(逐条都能对到代码):

| 项 | 值 | 出处 |
|---|---|---|
| 换位滞回 | **±6px** | `StackDragPlan.cs:21`(`Hysteresis = 6.0`) |
| 让位 / 落位动画 | **170ms / 170ms**,ease-out 三次 | `LayoutMotion.cs:16,19,34-39` |
| 动画最小位移 | 0.5px | `LayoutMotion.cs:25` |
| 边缘自动滚动 | **16ms** 一帧、边缘 **36px**(≤视口 1/3)、**≤18px/帧**、`(0.25+0.75×深度)` | `LayoutEditor.cs:162-165,1873`;`StackDragPlan.cs:73-99` |
| 关掉动画 | 退回"交换即落位",与改版前逐像素一致 | `LayoutEditor.cs:1652-1658,1691-1697` |

## 6.3 Linux 侧新增「四处差异 + height_px 等价」小节

落点:`docs/README.md` 新增 **「Linux 编辑态与 Windows 的四处差异(2026-11-20 复核,别当 bug 修)」**。

| # | 差异 | Linux 证据 | Windows 证据 |
|---|---|---|---|
| 1 | stack 页拖拽 | 进编辑态**临时折成 12 列 grid**,退出**保留网格**不还原:`Pages/Layout/LayoutEditor.cs:518-546`(`ConvertStackToGrid`→`LayoutEngine.StackToGrid`)、`:1154`、`:1278`;拖拽=grid 像素跟手 `:1969` | stack 保持一维流:`BeginDrag`/`MoveStackFloat`/`ApplyStackReorder` |
| 2 | 换位防抖 | 靠格子落点,无滞回 | 显式滞回 6px(`StackDragPlan.cs:21`) |
| 3 | 边缘滚动 | **30ms / 48px / ≤28px**(`(depth*0.6+4)`):`LayoutEditor.cs:2171-2215` | **16ms / 36px / 18px** |
| 4 | 滚动条出现 | **按新宽重量行高**;仅"更窄且总高确实变化"时补一次 `InvalidateMeasure`,同宽度只补一次(`_remeasureWidths`):`GridCanvasPanel.cs:667-679`、`:111`、`:513-515` | 同口径 |

**`height_px`(最容易被误判成 bug 的一条)**:

- Windows 是 v2 模型上的**类型化字段** `LayoutBlockV2.HeightPx`(`windos/src/TipCL/Core/Launcher/Layout/LayoutDocV2.cs:977`,`:1032` 写、`:1088` 读、`:1116` 清);
- Linux 住在区块的**通用 `extra` 字典**(`src/TipCL.App/Pages/Layout/LayoutEngine.cs:1201-1243` 的 `ReadHeightPx`/`WriteHeightPx`,键名同为 `height_px`);
- 但 `LayoutJson.MergeExtra` 是**平铺**合并(`src/TipCL.Core/Launcher/Layout/LayoutJson.cs`:`target[kv.Key] = ...`,不加嵌套层)⇒ 磁盘上两侧都是区块对象的顶层 `"height_px": N`,互读互写都不丢。文档里写明"看到 extra vs 字段不要当存储格式不一致去统一"。

## 6.4 README「功能」表逐条核对:3 处平台口径缺失(已补)

| 能力 | 逐条结论 | 证据 |
|---|---|---|
| 版本安装 / 模组与整合包 / 实例与存档 / 账号 / 联机 / AI 助手 / 插件(逻辑) / 仪表盘 / 外观 | Linux 树**都已落地** | `src/TipCL.Core/Launcher/` 下 `Installer/LoaderMeta/Optifine/Mirrors/Mods/Modpack/ExportPack/Instances/Saves/Java/AccountManager/MicrosoftAuthenticator/Authlib/Nide8/SkinServer/Terracotta` + `Ai/*` + `Plugins/*`;UI 侧 `LaunchPage`、`Themes/*` |
| **MCP Server**(`127.0.0.1:25585/mcp`) | **仅 Windows**。Linux 只有 MCP **客户端** | `windos/.../Ai/McpServer.cs:19`(`DefaultPort = 25585`)vs `src/` 里 `25585`/`McpServer` **零命中**,只有 `Ai/Mcp.cs`、`McpClient.cs` |
| **插件网页界面**(页签/浮层/网页区块) | **仅 Windows 有 WebView2**;Linux 结构化没有网页宿主,恒走"浏览器打开"兜底 | `src/TipCL.App/Pages/Layout/Widgets/DangerousWidgets.cs:13-17`、`Pages/Layout/PluginBlocks.cs:6-15` 的降级说明 |
| **手机同步** | **两端都只有"桌面半边"**;手机端服务端不在本仓、也不在 Android 仓 | `src/TipCL.Core/Launcher/Sync/PhoneClient.cs` + `Pages/Settings/PhoneSyncPanel.cs`;`TipCL-Android` 全库 `X-PyMCL-Token` / `/api/ping` / `deviceTimeMs` **0 命中** |

处置:功能表三行加"仅 Windows / 待配套"标注;表后新增**平台差异三行表**;`## 🔗 MCP Server` 与 `## 📱 手机同步` 两节各加一段口径块(避免读者按 Windows 说明在 Linux 上找开关、或把手机同步当成"现在就能用")。

## 6.5 新增 `docs/RELEASE-CHECKLIST.md`(给 1.2.1 用)

| 节 | 内容要点 |
|---|---|
| §0 两条硬约束 | ① 新号必须**严格大于**客户端号(`Updater.Newer` 只认 `>`;1.2.0 同号换资产的教训写进去了)② 版本形状必须是三段 `X.Y.Z`(`SmokeTests.cs:21,26` 与 `VersionLandingPointsTests.cs:105` 两条守卫钉死) |
| §1 六处版本落点 | 6 个文件 / 9 个值:`src/TipCL.Core/Launcher/AppInfo.cs:10,16,18`、`src/TipCL.Core/CoreInfo.cs:8`、`src/TipCL.App/TipCL.App.csproj:8`、`src/TipCL.Core/TipCL.Core.csproj:10`、`windos/src/TipCL/TipCL.csproj:17`、`windos/src/TipCL/Core/Launcher/AppInfo.cs:9,15,17`;每行写"谁在读它 / 漏改的后果" |
| §1.1 自检 | `dotnet test`(两棵树)+ `git grep -n "1\.2\.0" -- src windos/src` 兜底 |
| §2 出包 | Windows `dotnet build windos/src/TipCL/TipCL.csproj -c Release`(compact 红线 5,757,264 B);Linux `dotnet publish …` + `tools/package.sh --appimage-only --deb` |
| §3 要重算的 4 个 sha256 | `TipCL.exe` / `TipCL-linux-x64.AppImage` / `TipCL.exe.sha256` 附件内容 / README 安全说明行;**必须先上传再算** |
| §4 回填两份清单 | 四个字段名逐字(`version`/`notes`/`url`/`sha256`)、`\A[0-9a-fA-F]{64}\z`、tag 段与 url 一致、清单必须留仓库根 |
| §5 发完复核 | `curl` 两份清单 + GitHub API 资产 `digest` 比对 + 客户端真值表 + tag |
| §6 不在清单里的东西 | 安卓两条线(不同号)、mac(无此树)、布局档 `app` 派生、`-Dminecraft.launcher.version=1.0.10` 刻意保留、资产名逐字 |
| §7 1.2.1 待填表 | 空模板(版本/两棵树测试数/三个资产大小+sha/两份清单勾选/README 勾选/tag) |

同时在 `docs/README.md` 的「文档入口」里登记了这份检查单。

## 6.6 Android 二轮

仓库已由 lead fast-forward 合并过一次(我的 R1 提交 `146746e3` 现是 `main`),本轮从 `main` 新建 `audit/E-android2`。

### 6.6.1 两条构建线:产物名 / 版本号 / 包名 三方一致(逐项核)

| 项 | A 线(自研 Gradle) | B 线(apktool 重打包) |
|---|---|---|
| 包名 | `com.tipcl.android`(`android-app/app/build.gradle.kts:12`) | `com.pymcl.android`(`app/Android/AndroidManifest.xml` 的 `package=`) |
| launcher activity | `com.tipcl.android.LauncherActivity`(`android-app/app/src/main/AndroidManifest.xml:11-18`) | `com.pymcl.android.MainActivity`(同 manifest 的 LAUNCHER 项) |
| 版本 | `versionName 1.2.0` / `versionCode 1`(`build.gradle.kts:15-16`) | `versionName 0.9.3` / `versionCode 14`(`app/Android/apktool.yml:15-16`) |
| 产物名 | `dist\TipCL-<versionName>-android.apk`(`scripts/build_rewrite_release.bat:155`;CI 同名:`android-ci.yml:218-232`) | `dist\TipCL-<versionName>.apk`(`scripts/build_release.bat:17,55`,版本取自 apktool.yml) |
| 文档说法 | `README.md` 两条构建线表 + `android-app/README-android.md:118,236` | 同上 + `TipCL-触控改动说明.md` |

实测(不需要 JDK 的两段解析逻辑):

```
A 线:findstr "versionName" build.gradle.kts → tokens=2 delims== → PARSED=[1.2.0] → TipCL-1.2.0-android.apk
B 线:findstr /b "  versionName:" apktool.yml  → PARSED=[0.9.3] → TipCL-0.9.3.apk
dex 目录:24 个(smali + smali_classes2..24)⇒ "补丁 3 个、其余 21 个保持原包字节"成立
```

### 6.6.2 `重建工具/SurgicalAssemble.java` 能否按文档编译:**静态核对通过,未真编译**

- **本机没有任何 JDK**:`java` / `javac` 都不在 PATH、`JAVA_HOME` 为空、Android 仓库 `.tools/` 也不存在 ⇒ **`javac` 这一步本轮没做**(不是"编译失败",是"没有工具链")。
- 能做的核对我做到了:把 `build_release.bat` 会下载的那个 jar 真下下来(阿里云镜像 `apktool-cli-2.10.0.jar`,**32,169,252 B**),按 zip 成员 + class 常量池核对编译前提:

| 源码里的调用 | jar 里是否存在 | 证据 |
|---|---|---|
| `import brut.androlib.mod.SmaliMod` | ✅ | `brut/androlib/mod/SmaliMod.class` 存在 |
| `SmaliMod.assembleSmaliFile(File, DexBuilder, int, boolean, boolean)` | ✅ | 类里有方法名 `assembleSmaliFile` + 描述符 `(Ljava/io/File;Lcom/android/tools/smali/dexlib2/writer/builder/DexBuilder;IZZ)Z` |
| `new DexBuilder(Opcodes.forApi(api))` | ✅ | `Opcodes.forApi(I)Lcom/android/tools/smali/dexlib2/Opcodes;` 存在 |
| `db.writeTo(new FileDataStore(out))` | ✅ | `DexBuilder extends DexWriter`,`DexWriter.writeTo(Lcom/.../io/DexDataStore;)V`;`FileDataStore implements DexDataStore`,`FileDataStore(File)` 构造存在 |

- **同时修掉我自己 R1 写错的一步**:`javac -cp apktool.jar 重建工具\SurgicalAssemble.java` 会把 `SurgicalAssemble.class` 落在 `重建工具\` 里,而下一步 `java -cp "apktool;."` 从当前目录找主类 ⇒ **必然 ClassNotFoundException**。已加 `-d .` 并在文档里注明原因。

### 6.6.3 只报告(未改)

| 位置 | 现象 | 说明 |
|---|---|---|
| `android-app/app/build.gradle.kts:15` | `versionCode = 1` 而 `versionName` 已抬到 `1.2.0`(`30eb6790` 只改了 name) | 后续每次出 A 线包都要递增 versionCode,否则侧载"降级安装"会被拒;属发版纪律,不动代码 |
| 桌面对外发行说明(不在本仓) | 1.2.0 的 release body 写"新线 `android-app` = `versionName 0.1.0`" | 仓库里早已是 `1.2.0`(`30eb6790`),发行说明那句已过期;改不了(不在仓库),记在这里免得下一轮又被当成"仓库与说明不一致" |

## 6.7 本轮提交

| 仓库 | 分支 | 提交 | 内容 |
|---|---|---|---|
| TipCL | `audit/E` | `0733a41` | 5 文件 +239/−15:两本布局手册(手感 + FAQ 10 + 依据表)、`docs/README.md`(Linux 四处差异 + height_px 等价 + 文档入口)、`README.md`(功能表平台核对 + MCP/手机同步口径)、新增 `docs/RELEASE-CHECKLIST.md` |
| TipCL-Android | `audit/E-android2` | `ac6a8491` | `TipCL-触控改动说明.md`:重建命令加 `javac -d .` + apktool jar 符号静态核对结论 |

> 未 push;未改 `src/**`、`windos/src/**`、词表、`tests/**`、`tools/**`、`.github/**`,也未动 Android 的 smali / 二进制 / apk。