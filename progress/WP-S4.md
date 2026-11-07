# WP-S4：死导航入口中清理（实修）

日期：2026-11-07。基线 commit b76c66a。按 WP-S3 清单做中清理（深清理未做）。

## 删除明细

1. **MainWindow.xaml.cs**：删 12 个死 NavDef 注册行（version/mod/mods/modpack/datapack/resourcepack/shader/world/java/servers/playtime/feedback），共 -12 行。保留 launch/library/instance/download/ai/account/multiplayer/settings/tasks/plugins（各有可达路径）。
2. **MainWindow.xaml.cs WarmUpAsync**：预热数组 `{instance, version, tasks}` → `{instance, tasks}`（-1 处死预热）。
3. **MainWindow.xaml.cs SyncTabs**：TabSettings 高亮判断里删掉 java/servers/playtime/feedback 死比较（WP-S3 报告称白名单不含旧 id，实测 1535 行含 4 个旧 id，已一并清理）。
4. **NavModel.cs** 三张表同步删键（-37 行净）：
   - `SubDefaultMembers`：download 分区成员全死 → `Array.Empty`；more 分区删 mods/servers/playtime/feedback。
   - `SubTitles`：删 12 个死键（含 "resource" 别名）。
   - `PageForNavKey`：删 12 个死键（含 "resource"→"resourcepack" 别名）。
   - `GroupedNav`：删「反馈」组（仅含 feedback）与库组里的 servers，7 组 → 6 组；数组长度声明同步改 [6]。
5. **CatalogPage.cs installed-first**（-20 行）：`installedFirst:true` 唯一调用方是 mods NavDef，删参后无调用方。**注意**：WP-S3 判断"Mode.Installed 分支成死码"不完全准确——页面内有「已安装」toggle（`_modeInstalled` → `SetMode(Mode.Installed)`），该模式仍用户可达，故只删了死的构造参数 `_installedFirst`（Title getter 分支、SubTitle 分支、初始 mode 分支），保留 toggle 可达的 Mode.Installed 逻辑。深清理（拆 Mode.Installed）未做。

共约 -72 行（净，不含测试注释改动）。

## 测试同步（NavCompatTests.cs）

- `OldCompactConfigLoads`：旧存档 cfg 字面量保留原样（java pinned / version,mod / playtime 成员），断言从 `Contains "java"` 改为 `DoesNotContain "java"/"playtime"`——语义变为"旧存档里的已删导航键被安全忽略"。
- `NewDefaultGroupsCoverAllEntriesAndRender`：分组头断言 7 → 6。
- **Ctrl+1..9**：`VisibleFlatOrder` 由 NavModel 键驱动，死键删除后数字键自然到不了死页，实现无需改动（确认）。

## 体积与测试

- compact：5,705,216 B（基线 5,707,264，**净减 2,048 B**），远低于红线 5,757,264。
- premium：Release `-p:Premium=true` 构建通过。
- `dotnet test TipCL.sln -c Release`：**127/128**（唯一失败 PhoneLinkTests.FullFlow，Expected OnlyRemote / Actual Conflict，为既有基线失败，与本次改动无关）。

## 冒烟结论

- `--nav-dump`：grouped 默认 6 组 8 项，全部键都在 PageForNavKey / 分区头，无悬空；legacy compact 存档（pinned java/playtime、members version/mod/feedback）输入后死键被安全丢弃、不抛异常。
- `--shot launch --theme dark` / `--shot settings --theme dark`：两页渲染正常（存 `_wph_run/wps4/shot-*.png`），顶栏页签、启动卡、设置侧栏（Java/服务器/游戏时长/反馈与关于为设置页内嵌直达，不依赖 NavDef）均完好。
- grep 复核：`Navigate("<死id>")` / `GetPage("<死id>")` / installedFirst 全仓无残留。

## 提交

只 add 本任务 4 文件 + 本文件；LibraryPage.cs 的并行改动（WP-S6）未纳入本提交。
