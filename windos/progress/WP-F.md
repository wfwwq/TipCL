# WP-F 收尾：审查半成品删除 + 小修 + 全局一致性 —— 完成（2026-11-07，Windows 实测）

基于 HEAD 5254d8f（WP-F 半成品 WIP）审查后补完。已编译、已测试、已量体积，本文件为验收记录。

## 1. 审查结论：5254d8f 删除安全，无悬空引用

`git show 5254d8f` 逐文件审查（MainWindow.xaml.cs −753、MainWindow.Plugins.cs −96、Controls.xaml −139、Palette.Dark/Light 各 −8）。删的是：

| 文件 | 删除内容 |
|---|---|
| MainWindow.xaml.cs | 整套旧侧栏导航系统：BuildNav/MakeRow/BuildGroup/MakeHeader/AttachDrag/CommitDrop/UpdateDrop/RemoveDropLine/MoveMember/SyncNavSelection/ResetNavLayout/SetNavStyle/ShowNavItem/IsNavHidden/ApplyNavPatchAsync、NavGlyphs/HeaderGlyphs 字典、NavHost/PinnedHost/SideFooter 字段、NavRow/_taskBadge、ContextMenu 相关（Item/MenuGlyph） |
| MainWindow.Plugins.cs | 旧侧栏插件入口：PluginNavPages/AppendPluginMoreRows/AppendPluginTopRows/MakePluginRow/SyncPluginNavSelection、_pluginNavRows/_pluginMoreRendered |
| Controls.xaml | R.Card(double)、CR.Small、E.InOut、CardFlat、Btn.Gray、Btn.OnBanner、NavItem（旧侧栏行样式）、Radio、Tpl.Version、Tpl.Installed |
| Palette 明暗成对 | C.Shadow、B.Pressed、B.DangerBorder、B.WarnBorder、B.InfoBorder、B.BtnGray、B.BtnGrayBorder、B.BtnGrayInk |

**核查证据（全仓库 grep，src/TipCL + tests）**：
- 上述每个被删资源键：`grep -rn` 于 .cs+.xaml（含 MainWindow.xaml、tests/），0 处引用残留。Controls.xaml 自身也无 BasedOn/StaticResource 回引（命中的 `CR.Card` 是现存键，非被删的 R.Card）。
- 上述每个被删 C# 成员名：0 处残留（VersionManagePage.BuildGroup 是它自己的同名私有方法，无关）。
- `Ui.S("NavItem")` / `"Radio"` 等字符串样式查找（编译器查不到的）：0 处残留；AskBlock/FileDrop 用的是 RadioButton 类型本身，不是 Radio 样式。
- NavDef 复核：MainWindow.xaml.cs NavDefs（106–133 行）含 multiplayer(120)/instance(110)/version(111)/tasks(130)/download(131)，齐全。
- C# 方法引用由编译器证明：Release 构建 0 错误。

结论：**该删除无需回滚任何内容**。

## 2. 本次改动

| 文件 | 改动 | 行数 |
|---|---|---|
| src/TipCL/Pages/Plugins/PluginsPage.cs | L57「开发者」菜单字形 `\uec7a`→`\uebe8`（Bug）。ec7a 不在 TipCLIcons.ttf；ebe8 在字体（Ico.cs 已有 `Ico.Bug` 常量），语义贴切（开发者=调试）。候选 `\ue943 Code` 经查不在 map.py/字体里，弃用。未重建字体 | 1 |
| src/TipCL/MainWindow.xaml.cs | SyncTabs：TabSettings 在 settings/account/java/servers/playtime/feedback 时都点亮（设置子页高亮，WP-A 遗留项） | 1 |
| src/TipCL/MainWindow.xaml.cs | `SyncAccountChipAsync` private→internal（供 AccountPage 调用） | 改 1 词 |
| src/TipCL/Pages/AccountPage.cs | LoadAsync 取到账号列表后 `_ = AppServices.Window?.SyncAccountChipAsync();`。LoadAsync 是所有账号变更（切换/删除/离线添加/微软·皮肤站·通行证登录成功）的唯一汇点，含空列表早退路径，标题栏 AccName/AccInitial 即时刷新（WP-D 遗留项） | 1 |

共 3 行新代码 + 1 字改。未触碰禁区文件（LaunchPage.cs、LoaderIcons.cs、TipCL.csproj、PROGRESS.md、DECISIONS.md、交接必读.md、版本号、.github/、Core/）。

## 3. 实测数据（Windows，Release）

- `dotnet build TipCL.sln -c Release`：**0 错误**（首次 CS2012 为 VBCSCompiler 锁 obj，`build-server shutdown` 后重试即过）。
- `dotnet test TipCL.sln -c Release`：**127/128 通过**，唯一失败 = PhoneLinkTests.FullFlow（基线既有失败，未碰）。IconFontTests 单跑通过。
- `TipCL.exe` = **5,705,216 B ≤ 5,707,264 B 红线**，余量 2,048 B（本次改动未增字节）。

## 4. 遗留与风险

1. **DECISIONS D-11+ 仍未写**：PROGRESS 的 WP-F 行还要求补 DECISIONS（启动大按钮圆角 12、LaunchBackground 并入、插件设置移到插件页 ⋯、设置页嵌入子页独立实例、实例详情/安装页页内切换、C 桥改 Logo 红点）；本任务禁令明确不许碰 DECISIONS.md，留给监工补。
2. compact 余量只剩 2,048 B：WP-G（premium）若用 `#if PREMIUM` 分支不占 compact 体积，但要实测；若再超，候选回收项见 progress/WP-A.md（Card.Shadow / Seg.Item 的 DropShadowEffect 段）。
3. SyncTabs 用 `||` 链显式列举 5 个设置子页 id，没有用字符串 Contains（防插件自定义页 id/空串误匹配）；新增设置子页时记得补。
4. 账号芯片刷新挂在 AccountPage.LoadAsync：账号页每次打开也会多一次 `get_account_rows`（轻量 RPC，可接受）；非账号路径的 ui_changed 不刷芯片（本来就无必要）。
5. Win10 SetWindowRgn 圆角锯齿/缩放闪烁仍需用户肉眼实测（WP-A 记录，未动）。
