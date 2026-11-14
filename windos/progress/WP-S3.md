# WP-S3：旧导航可达性调研（只读，未动码）

日期：2026-11-07。基线 commit 28b640e。方法：全仓 grep `Navigate("<id>")` / `GetPage("<id>")`（排除 NavDefs 注册行），追 NavConfig（NavModel.cs）消费路径、WarmUp、Smoke、tests。

## 关键机制（判定依据）

1. **Ctrl+数字复活路径**：`MainWindow.xaml.cs:1051` Ctrl+1..9 → `Navigate(VisibleFlatOrder()[n])`。`VisibleFlatOrder`（1483 行）由 `NavModel.SectionMembersFromConfig(_navCfg)` + `NavItemsFromConfig` 展开，**旧分区键（version/mod/…/java/mods/playtime…）会从 NavConfig 默认成员（NavModel.SubDefaultMembers、GroupedNav）复活进这个序列**，PageOf 经 `PageForNavKey` 映射回页面 id。所以"零直呼调用"≠ 完全不可达：键盘快捷键仍能到。
2. **WarmUpAsync**（`MainWindow.xaml.cs:554-565`）：启动 900ms 后预热 `GetPage("instance")/("version")/("tasks")` —— "version" 页每次启动都会实例化（对 compact 体积无影响，仅行为）。GetPage 对已删 id 返回 null，WarmUp 有 null 判断不会崩。
3. **Smoke**（`Smoke.cs:335`）：遍历 `MainWindow.NavDefs` 全部 22 个 id 逐页导航——删任何 NavDef 只会让 Smoke 报告页数变少，无硬编码断言，**不会 fail**。WP-I 基线"22 页"数字会变。
4. **tests/**：无任何 `NavDef`/`Navigate`/`GetPage` 引用（grep 为空）。但 **NavCompatTests 依赖 NavModel**：`OldCompactConfigLoads` 断言 items 含 `Key=="java"`（第 52 行）；`NewDefaultGroupsCoverAllEntriesAndRender` 断言每个 item 键都在 `PageForNavKey`（第 73 行）。⇒ **删 MainWindow NavDef 行不破坏测试；删 NavModel 里的 java/version/mod 键会破坏测试**。
5. **RPC/插件外部唤起**：无 `"navigate"` RPC 通道；插件侧只有 PluginEvents/PluginSettingsCard/PluginHostPage 调 `Navigate("ai"/"plugins")`。不存在用任意字符串唤起旧 id 的路径。

## 逐 id 清单

| id | 直呼调用点（Navigate/GetPage，非注册行） | 判定 | 理由 / 连带 |
|---|---|---|---|
| launch | 页签 185、启动 291、AiPage 2556、VersionManagePage 720、InstancePage 353、VersionPage 219、ServersPage 150、LaunchPage 902 | 保留 | 新 IA 核心 |
| library | 页签 189、LaunchPage 902 | 保留 | 新 IA |
| instance | **LaunchPage.cs:1064**（启动页实例行"管理"钮 ManageLink）、WarmUp 预热、Ctrl+数字（库组） | 保留 | 仍活；注意语义：instance=旧 VersionManagePage，与 WP-E 的 LibraryPage 内嵌实例详情并存，属已知双轨 |
| version | **零直呼**。仅 WarmUp GetPage、Ctrl+数字（download 分区默认成员）、Smoke | 可删 | 连带：WarmUpAsync 行 555 的 `"version"`；若深清 NavModel（PageForNavKey/SubTitles/SubDefaultMembers 的 version）→ **NavCompatTests 46 行字面量** 不受影响（那是 cfg 字符串），但 PageForNavKey 删 version 键不破坏现测试；VersionPage.cs 整文件（392 行）随之可删 |
| mod | 零直呼。DownloadPage 内嵌 `CatalogKind.Mod`（Kinds 数组，20 行）不走 NavDef | 可删 | Ctrl+数字复活 + Smoke；NavModel 键连带同上（NavCompatTests 46 行 cfg 字面量含 "mod"，但断言不查 PageForNavKey["mod"] 的存在性——最后一条测试只查 config 里出现的键，删键后该 cfg 不再出现即可，需实测） |
| mods | 零直呼。`installedFirst:true` 仅此一处使用 | 可删 | 连带：CatalogPage 的 installed-first 模式（`_installedFirst`/`Mode.Installed` 分支，31 处 _mode 引用中约一半）成死码，可再省约 80-120 行 |
| modpack / datapack / resourcepack / shader / world | 零直呼。DownloadPage 内嵌对应 CatalogKind | 可删 | 同 mod；PageForNavKey 的 `"resource"→"resourcepack"` 别名键也一并处理 |
| account | MainWindow:209（标题栏头像点击）、SettingsPage 直接 new（不走 NavDef）、Bridge 相关 765 | 保留 | 头像入口活 |
| java | 零直呼（SettingsPage:723 直接 new JavaPage 嵌入） | NavDef 行可删 | **但 NavModel 的 java 键不能删**：NavCompatTests:52 断言含 "java"；页类 JavaPage 被设置页引用必须保留 |
| ai | 标题栏 AI 钮 205/1069、Ctrl+K 1069、PluginEvents 205 | 保留 | |
| multiplayer | LaunchPage:421（启动页"联机"按钮） | 保留 | WP-B 交接时明确"删抽屉时保留 multiplayer NavDef" |
| servers | 零直呼（SettingsPage:724 直接 new） | NavDef 行可删 | 页类被设置页引用保留；NavModel 键动否需连带测 NavCompatTests |
| playtime | 零直呼（SettingsPage:725 直接 new） | NavDef 行可删 | 同上 |
| feedback | 零直呼（SettingsPage:689 直接 new） | NavDef 行可删 | 同上 |
| settings | 1060（OemComma 快捷键）、FirstRunWizard:196 | 保留 | |
| tasks | MainWindow 1262、CatalogPage 958/975、DownloadPage 76、InstallPage 491、FileDrop 174/215 | 保留 | |
| download | 页签 193、VersionPage:80、LaunchPage:1528 | 保留 | |
| plugins | PluginHostPage 77、PluginSettingsCard 118/125 | 保留 | |
| library/settings/download/launch | 见上 | 保留 | |

**死入口（零 UI 直呼，仅靠 Ctrl+数字/NavConfig 复活 + Smoke 遍历到达）**：
`version, mod, mods, modpack, datapack, resourcepack, shader, world, java, servers, playtime, feedback` —— 共 12 个。

## 预计体积/行数收益

- **浅清理（只删 12 行 NavDef 注册）**：~12 行，exe 收益 ≈ 0（页类仍被实例化路径引用/元数据仍在）。不推荐单独做——Ctrl+数字变静默 no-op、WarmUp 的 "version" 变 no-op，行为无损但脏。
- **中清理**：NavDef 12 行 + NavModel 三张表（PageForNavKey/SubTitles/SubDefaultMembers/GroupedNav 里对应键）约 25-30 行 + WarmUpAsync 一处 + CatalogPage installed-first 死分支约 80-120 行 ≈ **120-165 行**。⚠️ 动 NavModel 键前必须跑 NavCompatTests（"java" 断言、PageForNavKey 覆盖断言会红，需同步改测试或保留 java 键）。
- **深清理**：中清理 + 删 VersionPage.cs（392 行，旧原版游戏页，唯一剩余引用是 WarmUp+NavDef+复活路径）+ CatalogPage 已安装模式全拆 ≈ **500-560 行**。对 compact 体积红线（当前仅余 1,024 B）可能是最值钱的回收点之一，但 CatalogPage 是 DownloadPage 共用底座，拆 Mode.Installed 要仔细。

## 建议处置

1. 不建议现在只做浅清理（收益≈0）。建议作为 **WP-S4 清理包**在 WP-T/U/V 验收后、重切 dist 之前做**中清理**；深清理（删 VersionPage）可作为 compact 超红线时的备选回收项。
2. 清理包必须同步：WarmUpAsync、Smoke 页数基线（22→10）、NavCompatTests（如动 NavModel）、SyncTabs 不受影响（它查的是硬编码 id 白名单，旧 id 不在其中，已验证 1533/1535 行）。
3. 若保留不动：风险仅是"旧页面仍可被 Ctrl+数字 到达"，与 v2 信息架构不一致但无害。

## 验证覆盖说明

- 全部结论来自静态 grep + 逐点读码，未编译验证；`Navigate(` 动态传参仅 DevShot/Smoke（传 NavDef.Id 或命令行 pageId），无其它动态注入路径。
- 未修改任何源码文件；本文件为唯一产出。
