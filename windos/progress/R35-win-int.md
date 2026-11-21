# R35 · win-int —— 布局 v3 在 Windows 桌面版(WPF / net48)的集成

> 分支 `r35/win-int`(工作区 `D:\项目\TipCL\_r35\win-int`),起点 `r34/base` 8cad7d0。日期 2026-11-21。
> 按 R35-BRIEF §2 执行:**一次都没有启动 TipCL.exe**(没跑 `--smoke` / `--shot` / `--i18n-check` / `--design-sheet`),只跑了 `dotnet build` / `dotnet test`、git 和文件读写;没跑 `dotnet build-server shutdown`;没改版本号、没 push、没合进 main。

## 0. 结论(先看这里)

| 项 | 结果 |
|---|---|
| compact `TipCL.exe` | `D:\项目\TipCL\_r35\dist\windows\TipCL.exe` · **5,698,560 B**(上限 5,757,264 B,余 58,704 B)· SHA256 `4E70D1DD63E0154C8BA2CA5376FFED8D53A525569016343D550D354E51E296E8` |
| premium `TipCL.exe`(`-p:Premium=true`) | `D:\项目\TipCL\_r35\dist\windows-premium\TipCL.exe` · **5,718,528 B** · SHA256 `41FCD906062B25BBC206EA03FAA97AF00124AF0CF5B3C71D0A7246B94AE46AEC` |
| 源码提交 | 两份 exe 都由 **`cd8a572`**(`cd8a572cc380d29f23b9877c1958c37793d3dc34`)编出,编译时工作区干净;本报告提交在其后,不改代码 |
| 构建 | Release compact / premium 均 **0 错误**;警告 2 条(`BridgeMutationAndLaunchGuardTests.cs` 的 xUnit1031),与 r34/base 完全相同,**0 新增** |
| 测试 | r34/base 基线 **593 过 / 0 败 / 0 跳过**(本轮实测)→ 收尾 **746 过 / 0 败 / 6 跳过**(752 总数) |
| 词表 | zh_CN / en 成对 +29 条(3208 → 3237),`.json.gz` 用 `tools/pack-core-resources.ps1` 重新打包 |

> ⚠️ **关于你可能正在试的那个 exe**:9:00 前后有一个从资源管理器启动的 `C:\r35\win-int\windos\src\TipCL\bin\Release\net48\TipCL.exe`(PID 18672、41884)。那是我 8:37 编的**中间版本**(只合了快照、还没做本报告第 2 节的补齐)。我没有动它,之后的构建全部改到 `C:\r35\win-intb` 里做。**验收请用 `_r35\dist\windows\TipCL.exe`。**

提交序列(都在 `r35/win-int`):

| 提交 | 内容 |
|---|---|
| `b38f8fe` / `11e7c17` / `ff15ed7` | `merge --no-ff` r34/l0(cb8ad4c,含 b609ef1)、r34/l3(8561dc2,含 d195462)、r34/l5(f8561da,含 6277714),git 层面零冲突 |
| `9f4aac1` | 带入快照 Windows 部分(编辑态 A + 设置页 B + 词表 + 测试 + 两份报告);`BlockHost.cs` 手工合并、`GridCanvasPanel.cs` 三方合并 |
| `1f0de67` | 对照 GOAL §5.1 / §5.2 / §5.4 补齐 + 跨包接线 + 词表 + 新测试 |
| `cd8a572` | 「添加」抽屉的虚线空状态框 |

## 1. 合并做法

1. 建分支:`git branch r35/win-int 8cad7d0…` + `git worktree add D:\项目\TipCL\_r35\win-int r35/win-int`(没碰 main / platform-* / r34/* / 其他工作区)。
2. 依次 `merge --no-ff` r34/l0 → r34/l3 → r34/l5(分支头,含各自报告 `windos/progress/WP-L0/L3/L5.md`)。
3. 快照(`r35/quick-v3-snapshot` eff029d)的 Windows 改动清单 = `git diff --name-only 4277a02 r35/quick-v3-snapshot -- windos`:
   - 直接 `git checkout r35/quick-v3-snapshot --`:`LayoutEditor.cs`、`LayoutEditorV3.cs`(新)、`LayoutJsonWindow.cs`(新)、`LayoutSettingsPrefs.cs`(新)、`LayoutShellHooks.cs`、`PageLayoutHost.cs`、`ShellTabs.cs`、`SettingsLayoutPage.cs`、`locales/{zh_CN,en}.json(.gz)`、`LayoutProfileTests.cs`、`LayoutV3SourceTests.cs`(新),外加 `progress/LAYOUT-V3-A.md`、`LAYOUT-V3-B.md`。(R34 三个包都没改过这些文件。)
   - `GridCanvasPanel.cs`:`git merge-file`(基 4277a02)三方合并,无冲突 —— r34/l3 的 canvas 读时推导 + 快照「网格编辑态不铺主题色底纹」都在。
   - `BlockHost.cs`:2 处冲突,手工合并 —— 入口门槛用 r34/l5 的 `BlockHostRules.OffersLayoutMenu(_specCount)`(≥2 块),菜单体用快照的 `RebuildContextMenu`(常态「自定义布局...」/ 编辑态「完成编辑」「重置本页」);`internal OffersLayoutMenu`(l5)与 `public CanEdit`(快照)两个成员都保留;l5 的单块页禁用占位菜单保留。
   - 快照里 `windos/docs/mockups-hark/v3/` 的改动**没有带入**(以 r34/base 的效果图为准)。
4. 快照这套代码第一次编译就 0 错误,全量测试 737 过 / 0 败 / 6 跳过(6 个跳过是 r34/l3 退役的画布手势用例)。

## 2. 对照 GOAL 逐条(✅ 已落地 · ◐ 部分 / 有偏离 · ✗ 没做)

文件简称:`V3` = `Pages/Layout/LayoutEditorV3.cs`,`LE` = `Pages/Layout/LayoutEditor.cs`,`SLP` = `Pages/SettingsLayoutPage.cs`,`GCP` = `Pages/Layout/GridCanvasPanel.cs`,`LSH` = `Pages/Layout/LayoutShellHooks.cs`。

### §5.1 区块编辑态外观

| 项 | 状态 | 落点 |
|---|---|---|
| 常态 1.5px 灰虚线、外扩 4px;悬停加深;选中 2px 主题色实线 | ✅ | `V3` `LayoutCard.BuildV3Chrome` / `ApplyV3Chrome`;R35 改用 r34/l0 令牌 `B.EditOutline` / `B.EditOutlineHover`(原来是 `B.Line` / `B.InkFaint`) |
| 深色背景(启动页)白色半透明 | ✅ | 同上,`pageId == "launch"` 时用 `B.EditOutlineOnDark` / `B.EditOutlineHoverOnDark`(`LayoutEditor.IsDarkSurface`,GOAL §6.4-2) |
| 悬停 / 选中才出现的左上浮动标签(拖拽点 + 标题),自建组件后加灰字「· 组件」 | ✅ | `V3` 标签行;拖拽点优先 `Geo.Grip`;底色 `B.EditBarBg` / `B.EditBarLine`(启动页 `B.EditGlassChip*`) |
| 自建块标题用 widget 的 title,没有就用类型名,插件块用插件标题,永不显示 id | ✅ | `V3` `DisplayTitle`(快照);定义找不到时显示「组件」 |
| 内置块 = 眼睛划线(隐藏),custom/plugin = 垃圾桶(删除),右上角,悬停/选中出现 | ✅ | `V3`;眼睛划线优先 `Geo.EyeOff`,垃圾桶保留 Lucide 路径(`Ico.Trash` 是字形,这里统一成矢量) |
| 删除折叠按钮 / 全部折叠 | ✅ | 快照:操作条永远折叠,不再创建「全部折叠」 |
| 网格:右边缘 + 下边缘把手,悬停/选中出现 | ✅ | `V3` `_v3Grip`(右,e 向)+ **R35 新增 `_v3GripS`**(下,s 向;双击 = 恢复自动高度,tooltip「拖动改高度 · 双击恢复自动高度」);右下角小方块网格下不显示(快照) |
| 拖右边缘显示「N / 列数」,整列变化,受 MinCols 限制 | ✅ | **R35 新增** `V3` `ShowV3SpanTip`(`Layout.EditTip` 样式,找不到用代码兜底),在 `LE` `ApplyGridGesture` 吸附宽度变化时更新,`EndBoxGesture` 收起;整列吸附与 `ClampMinWidth` 是既有逻辑 |
| 落点框 = 主题色浅底 + 虚线框 | ✅ | **R35** `GCP` `DropPlaceholder` 改成浅底(`B.EditDropFill`)+ 虚线 Rectangle;深色表面白色半透明 |
| 拖动时淡色列底纹 | ✅ | **R35** `GCP.OnRender`:有浮起卡片时按列铺 `B.EditColShade`;浮起开始 / 结束时 `InvalidateVisual` |

### §5.2 编辑条与「添加」抽屉

| 项 | 状态 | 落点 |
|---|---|---|
| 内容区顶部单行、圆角 12 浮条;设置子页从左侧导航右边开始 | ✅ | `LE` 根网格 `Auto,*`(快照);设置子页的编辑器本来就在导航右侧的内容区里。R35 把底色换成 `B.EditBarBg` / `B.EditBarLine`,启动页用 `B.EditGlass` 深色玻璃(`ApplyV3BarSurface`) |
| 编辑「页名」/ 列表·网格分段 / 灰色提示两种措辞 / 撤销重做图标(不可用置灰)/ 分隔线 / 添加(软底 + 隐藏数角标)/ 重置本页 / 完成 / ⋯ | ✅ | `V3` `BuildV3Chrome` / `RefreshV3Bar`(快照);撤销 / 重做 R35 换成 `Geo.Undo` / `Geo.Redo` |
| ⋯:导出本页… / 导入到本页… / 整套布局与方案…(跳设置 → 布局) | ✅ | `V3` `OpenV3MoreMenu`(快照) |
| 删掉「吸附 N」「自动靠拢」「全部折叠 / 展开」 | ✅ | 按钮不再创建(快照);**R35:编辑器固定按「自动靠拢 = 开」处理**(`LE` `AutoPackOn => true`,3 处调用点),`LayoutEditPrefs.AutoPack` 继续能读写但编辑器不再看它;设置页也没有这个开关 |
| 抽屉:右侧滑出、宽 316 | ✅ | `V3` `BuildV3Drawer`(快照) |
| 上半「已隐藏的内容」(图标 + 名称 + 说明 + 显示),空时虚线空状态框 | ✅ | 快照 + **R35 把空状态改成虚线框**(`cd8a572`) |
| 下半「组件」:WidgetLibrary 类型,不可重复的已加过显示「已添加」并置灰;网页 / 按钮只在 Q6 开关打开后出现 | ✅ | `V3` `RefreshV3Drawer`,按 `LayoutSettingsPrefs.DangerWidgetsEnabled` 过滤 |
| 新块加到页尾,滚过去并闪一下主题色光晕 | ✅ | **R35 新增** `LE` `Restore` / `AddCustomBlock` 末尾调 `RevealBlock` → `BringIntoView` + `LayoutCard.FlashGlow`(约 0.9 s;关动效时只滚不闪) |
| 快捷键:Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z;Esc 先关抽屉再退出;Delete 对内置块隐藏、对组件删除 | ✅ | Esc 先关抽屉(快照);**R35 修 Delete**:原来对内置块只弹「内置区块不能移除」,现在隐藏(`LE` `DeleteSelection`,先 Clone 再改,撤销快照不受影响) |
| 常态无编辑痕迹,右键空白处「自定义布局…」 | ✅ | `BlockHost` 合并结果 + `LSH` |

### §5.3 模式收敛(r34/l3)

| 项 | 状态 | 落点 |
|---|---|---|
| 推导只保留一份实现 | ✅ | 快照自带的 `LayoutCanvasCompat.AsGrid`(上沿差 5% 同行、隐藏块挪末行)与 r34/l3 的 `LayoutCanvasMigration` 口径不同。**R35 统一用 r34/l3 的**:`LE.LoadFromStore` 直接 `LayoutCanvasMigration.ToGridPage(page, id => MinColsOf(...))`;`LayoutCanvasCompat` 留成转发到 `ToGridPage` 的薄壳(导入到本页还在用) |
| canvas 页卡片不出 8 向手柄(l3 合并事项 1) | ✅ | `LE.RenderBox` `card.SetBoxMode(_box.Mode)` |
| 设置页缩略图 canvas 页先推导(l3 合并事项 5) | ✅ | `SLP.ThumbPlanOf` |
| 进编辑转 grid / 完成不改就还原(l3 合并事项 2/3) | ◐ | 装载时已经按网格呈现,`state` 里就是 grid;「进编辑 → 什么都不动 → 完成」仍会 `Persist`,即**点一次「完成」就写成 grid**(GOAL 允许:编辑器里提交一次之后才改写)。不进编辑态时文档一字节不动 |
| 退役代码真正删除(l3 合并事项 6) | ✗ | `Steps` / `CycleSnapStep` / 8 向手柄等仍在编译、界面不可达(r34/l3 已标注)。删除牵涉 6 个 Skip 用例和其它包的测试,本轮不动,见「待用户确认」4 |

### §5.4 设置 → 布局

| 项 | 状态 | 落点 |
|---|---|---|
| 页头说明文案 | ✅ | `SLP` 顶部(快照) |
| 1 外观:三段切换 6/10/14,「所有可以自定义的页面一起生效」 | ✅ | 快照 |
| 2 导航 · 页签:可拖排序 chip + 眼睛,「设置」锁,恢复默认,标题栏马上变 | ✅ | 快照(`ShellTabs` 新增纯逻辑) |
| 2 导航 · 标题栏:AI 按钮 / 主题切换 / 账号三个勾选,去掉「桥接」,一句 Ctrl+K 说明 | ✅ | 快照;`SetTitlebar(bridge:)` 与存档值保留 |
| 3 页面:每行 3 张;**15 张** = 入口表 − SingleBlock − settings:layout + library:main | ✅ | **R35**:入口表补 `library:main`(17 → 18,`NavId = "library"`),新增纯函数 `LayoutBlockEntryCatalog.PageCardEntries()`。快照原来还额外去掉了两个子视图(Hint)并按 `CanEditPage` 过滤,只剩约 12 张;现在按 GOAL 口径固定 15 张,子视图卡片点「编辑」找不到页面时沿用 Hint 提示怎么打开 |
| 默认 6 张:启动、下载、实例库、联机、账号、AI 助手 | ✅ | **R35** `PrimaryCardPages`(快照原来是 启动 / 下载 / 账号 / 通用 / 游戏时长 / 联机) |
| 卡片:缩略图(组件主题色浅底)+ 页名 + 默认 / 已自定义 + 摘要 + 编辑(+ 恢复默认);组标题右边「全部恢复默认」;单块页一行说明 | ✅ | 快照;**R35**:「编辑」按钮按 `LayoutShellHooks.CanEditPage` 置灰 |
| 4 方案:每行 **4** 张;正在使用 1.5px 主题色描边、不能删;⋯ 重命名 / 复制 / 删除;组标题右边 导入… / 导出整套布局… / 存为新方案;导出先问 v2 / v1 | ✅ | 快照;**R35 改成每行 4 张**(快照是 3) |
| AI 备份显示「AI 改布局前」+ 时间 | ✅ | **R35** `LayoutProfiles.DisplayName`(快照只加了「AI 备份」标签,名字照原样显示) |
| 方案信息行里的「保存时间」 | ✗ | 方案存档没有这个字段,不新造字段(同快照 B 报告第 8 条) |
| 5 高级(默认折叠):标题「分栏、网页与按钮组件、布局文件」+「一般不需要动」 | ◐ | 快照把高级拆成三张相连的卡片共用一个折叠开关(`LayoutPageSplitTests` 钉着 7 个区块 id) |
| 分栏宽度一句话 + 全部复位,删 13 行表 | ✅ | 快照 |
| 网页和按钮组件开关 + 琥珀色提示 | ✅ | 快照 |
| 组件管理「已创建 N 个组件 · 管理…」,去掉 L6 | ✅ | **R35** 行标题「组件管理」、计数「已创建 N 个组件 / 还没有创建组件」(快照写的是「自建组件 · 已保存 N 个」) |
| 布局文件:打开所在文件夹 + 在编辑器中打开…(独立窗口),页面上不再常驻 JSON 框 | ✅ | 快照 `LayoutJsonWindow` |

### §5.5 各页(r34/l5)+ 启动页编辑态

| 项 | 状态 | 落点 |
|---|---|---|
| 改名、4 页新默认、≥2 块才有右键菜单、实例库菜单文案 | ✅ | r34/l5 原样合入 |
| 启动页编辑态英雄区 45% + 去饱和,右上「🔒 主区域固定，不能拖动或隐藏」 | ✅(去饱和为近似) | **R35**:`LayoutEditor` 新增 `EditingChanged` 事件,`LaunchPage` 订阅 —— `_hero.Opacity = 0.45`,第 0 行叠一层 `B.EditDim`(WPF 没有去饱和滤镜,按 r34/l0 决定 5 近似),锁形提示用 `Layout.LockNote` 样式 + `Geo.Lock`。压暗层放在英雄区文字之下(`ZIndex`),文字只被 45% 不透明度处理一次,不会叠两次 |

### §6.4 接口约定 / 各包「合并时处理」

| 约定 | 状态 | 落点 |
|---|---|---|
| 资源键用 r34/l0 的(`SetResourceReference` / `TryFindResource`,找不到不抛) | ✅ | `V3`(B.EditOutline*、B.EditBar*、B.EditGlass*、Geo.*、Layout.EditTip)、`GCP`(B.EditColShade、B.EditDropFill、B.EditOutlineOnDark)、`LaunchPage`(B.EditDim、Layout.LockNote) |
| Q6:`LayoutAddDrawer.DangerWidgetsAllowed` 与快照 `LayoutSettingsPrefs` 二者留一 | ✅ | 留 **`LayoutSettingsPrefs.DangerWidgetsEnabled`**(存在 `UiPrefs.Expanded["layout.v3.danger_widgets"]`,默认关);快照的抽屉在 `LayoutEditorV3.cs` 里,没有 `LayoutAddDrawer` 类,所以不新建;另在 `LayoutEditPrefs` 加 GOAL 约定名的别名 `AllowDangerWidgets`(直接转发) |
| 设置页「编辑」↔ `LayoutShellHooks.BeginEditPage / CanEditPage` | ✅ | 快照已接;R35 让 `CanEditPage`、`LayoutEditor.CanEdit` 都走 `BlockHostRules.OffersLayoutMenu` |
| l5 合并事项 2:壳级兜底右键跳过单块宿主 | ✅ | `LSH.FindTargetHost`:鼠标不在任何宿主里时只在 `OffersLayoutMenu || IsEditing` 的宿主里挑 |
| l5 合并事项 3:`LayoutEditor.CanEdit` 收紧到 ≥2 | ✅ | 快照已是 ≥2,R35 改成 `BlockHostRules.OffersLayoutMenu(_specs.Count)` |
| l0 合并事项 1:`B.EditDropFill` 跟随自定义主题色 | ✅ | `SettingsPage.Themes.cs` `ApplyAccent` + `SettingsPage.cs` `AccentKeys`(24 → 25) |
| l0 合并事项 2:DevShot 钩子接正式入口 | ✅ | `DevShot.Run` 开头:`SelectHook → Editor.DevSelect(id)`,`LayoutDrawerHook → Editor.OpenAddDrawer()`,`ExpandHook("advanced") → LayoutShellHooks.OpenLayoutSettings(true)`;反射兜底保留但不再需要 |
| l0 合并事项 3:样张挂进 DesignSheetPage | ✗ | 没做(dev-only,`--design-sheet` 走 DevShot 的注入照样能出样张) |

## 3. 测试

| | 通过 | 失败 | 跳过 | 总数 |
|---|---|---|---|---|
| r34/base(本轮在 `C:\r35\win-base` 实测,compact 5,599,232 B) | 593 | 0 | 0 | 593 |
| 合并 l0 + l3 + l5 + 快照之后(`9f4aac1`) | 737 | 0 | 6 | 743 |
| 收尾(`cd8a572`) | **746** | **0** | **6** | **752** |

- 6 个跳过 = r34/l3 把退役的画布手势用例改成 Skip(带理由),不是本轮新增。
- **没有删除任何断言**。改了 2 处断言(都是规格变化带来的):
  1. `LayoutProfileTests.BlockEntryCatalogCoversSingleBlockPages`:入口表数量 17 → 18(GOAL §6.3 WP-L4 原文要求「17 → 18 跟着改」)。
  2. `LayoutV3SourceTests.ShellHooksExposeV3EntryPointsAndSingleBlockPagesAreNotEditable`:`CanEdit` 的源码断言从 `return _specs.Count >= 2;` 改成 `return BlockHostRules.OffersLayoutMenu(_specs.Count);`,并加断言 `BlockHostRules.MinBlocksForLayoutMenu == 2`(规则不变,只是改成和右键菜单共用一处)。
- 新增 `windos/tests/TipCL.Tests/R35WinIntTests.cs`(9 例):页面卡片恰好 15 张且含 library:main / 不含单块页与布局页;默认 6 张顺序;library:main 入口;AI 备份显示名;`LayoutCanvasCompat` 与 `LayoutCanvasMigration` 结果一致且不改原文档;Q6 单一真源;自动靠拢固定为开;跨包接线(BlockHost / ShellHooks / 卡片手柄模式 / DropFill / DevShot / 启动页事件 / 设置页入口);卡片外观(两条边把手、列数读数、L0 令牌、316 抽屉、「· 组件」、深色表面判断、列底纹、虚线落点框)。
- 测试里不开浏览器 / 文件管理器(没有新增任何会调 `Process.Start` 的用例)。

## 4. 体积

| | r34/base | 收尾 | 变化 |
|---|---|---|---|
| compact | 5,599,232 B | **5,698,560 B** | +99,328 B(l0 样式 BAML + 快照编辑态 / 设置页 + 词表);余量 58,704 B |
| premium | —(r34/l0 报告 5,654,016 B) | **5,718,528 B** | — |

`bin\Release\net48` 旁边还有 Costura 排除的 `System.Text.Encoding.CodePages.dll` 等依赖文件(与以往相同);按任务只把 `TipCL.exe` 放进了 dist。

## 5. 词表

zh_CN / en 各 +29 条(值:zh = 原文,en 如下),`tools/pack-core-resources.ps1` 重新打包两份 `.json.gz`(en 76,504 B / zh_CN 48,898 B)。来源:快照守卫测试 `V3StringsExistInBothWordTables` 报缺的、R35 新文案,以及离线扫描全树 `I18n.L("…")` 时发现的缺键(含 r34/l5 报告列的 8 + 7 条和「主区域固定…」):

| 中文 key | 英文 |
|---|---|
| 联机状态 / 房间 / 快捷操作 | Connection status / Room / Quick actions |
| 累计时长 / 时间范围 / 明细 | Total playtime / Time range / Details |
| 已检测的 Java / 账号列表 | Detected Java / Accounts |
| 主区域固定，不能拖动或隐藏 | Main area is fixed and can't be moved or hidden |
| {0} / {1} 列 | {0} / {1} columns |
| 拖动改高度 · 双击恢复自动高度 | Drag to change height · double-click for auto height |
| AI 改布局前 / AI 改布局前 · {0} | Before AI layout change / Before AI layout change · {0} |
| 组件管理 / 已创建 {0} 个组件 / 还没有创建组件 | Widget management / {0} widgets created / No widgets created yet |
| 1.20.5 – 1.21.11 | 1.20.5 – 1.21.11 |
| 保存 Java 选择失败 / 停止失败 / 取消登录失败 / 取消失败 | Failed to save the Java choice / Failed to stop / Failed to cancel sign-in / Failed to cancel |
| 启用模组失败 / 停用模组失败 / 开关全局模组失败 / 切换语言失败 | Failed to enable the mod / Failed to disable the mod / Failed to toggle the global mod / Failed to switch language |
| 复制项目地址 / 已复制项目地址 | Copy project link / Project link copied |
| 陶瓦联机暂未提供此架构的官方内核;项目地址可以直接复制。 | Terracotta has no official core for this architecture yet; you can copy the project link instead. |
| 快捷方式只把启动器打开了,没有再启动一个实例 | The shortcut only opened the launcher; no instance was started |

- 离线复扫:全树 `I18n.L("…")` 字面量对 zh_CN / en 的缺键 = **0**。
- 废弃 key(r34/l5 列的 11 条「状态卡 / 房间卡 / … / 隐藏文件夹栏」,以及快照不再使用的「已保存 {0} 个」「还没有」「自建组件」「内置区块不能移除」等)**全部保留**:不影响体积预算(余量够),也避免误删其它页面仍在用的同名 key。

## 6. 决定(合并到 DECISIONS 时从 D-82 起编号,前缀 W-)

- **W-1 canvas 推导只留 r34/l3 一份**:快照的 `LayoutCanvasCompat` 规则(5% 同行容差、隐藏块挪末行整行)和 l3 的 `LayoutCanvasMigration`(严格先 y 后 x、隐藏块写契约默认矩形 + span)不一致;l3 有完整单测和 AI 工具共用,所以用 l3 的,`LayoutCanvasCompat` 只留薄壳。
- **W-2 Q6 开关的真源是 `LayoutSettingsPrefs`**(UiPrefs.Expanded 里的专用 key),不另加 `UiPrefs.LayoutDangerWidgets` 字段;`LayoutEditPrefs.AllowDangerWidgets` 只是别名。不建 `LayoutAddDrawer` 类(抽屉代码在 `LayoutEditorV3.cs`)。
- **W-3 页面卡片固定 15 张**(GOAL 口径),不再按 `CanEditPage` 动态过滤;`CanEditPage` 只用来让「编辑」按钮置灰。
- **W-4 自动靠拢在编辑器里写死为开**,`LayoutEditPrefs.AutoPack` 原样保留可读写(老存档里的 false 不会被改写,只是不再生效)。
- **W-5 Delete 对内置块 = 隐藏**(GOAL §5.2 原文),多选时组件删除、内置块隐藏一起做,一次撤销。
- **W-6 启动页去饱和**用 `B.EditDim` 压暗层近似(同 r34/l0 决定 5),压暗层在英雄区文字下面。
- **W-7 「N / 列数」读数**放在指针右上方(跟手),而不是固定在卡片右边缘上。
- **W-8 编辑条用画刷令牌**(`B.EditBarBg` / `B.EditBarLine` / `B.EditGlass*`)而不是整套 `Layout.EditBar` 样式:快照的编辑条是 `_barHost` + 自定义网格,套整套样式会改掉它的内边距 / 高度,风险大于收益。

## 7. 待用户确认

1. **页面卡片 15 张里包括「版本安装」「实例详情 · 设置」两个子视图**:点「编辑」时如果那个子视图没打开,只会提示怎么打开(Hint)。如果你更想要快照原来的做法(子视图不出卡片,约 13 张),改 `LayoutBlockEntryCatalog.IsPageCard` 一行即可。
2. **「完成」就算提交**:canvas 老页面进编辑态、什么都不动、点「完成」,也会写成 grid(编辑器把装载结果当作当前状态)。如果要「没动过就一个字节都不写」,需要在 `ExitEdit` 里对 `!_layoutTouched` 跳过 `Persist`(会连带改变 stack 页的既有行为)。
3. **方案卡片没有「保存时间」**(存档里没有这个字段)。要显示需要在方案里新增一个可选字段。
4. **退役的画布代码要不要真删**(r34/l3 合并事项 6 的清单 + 6 个 Skip 用例 + `LayoutEditorTests` / `LayoutWidgetLifecycleTests` 的 canvas 半段)。本轮没删,界面上不可达。
5. **「高级」是三张相连卡片**而不是效果图里的一张(快照为了不改 `LayoutPageSplitTests` 的 7 个区块 id)。
6. 废弃词条全部保留(见 §5),要不要清理。

## 8. 待真机(本轮一次都没跑;建议用临时数据目录 `$env:TIPCL_HOME="$env:TEMP\tipcl-v3-win"`)

截图命令(exe = `D:\项目\TipCL\_r35\dist\windows\TipCL.exe`):

```powershell
$exe = 'D:\项目\TipCL\_r35\dist\windows\TipCL.exe'
& $exe --design-sheet --theme light --size 1000x3600 --out layout-v3-light.png
& $exe --design-sheet --theme dark  --size 1000x3600 --out layout-v3-dark.png
& $exe --shot download --edit --select list --theme light --out download-edit-select.png   # list 换成下载页实际块 id
& $exe --shot download --pop layout-drawer --theme light --out download-drawer.png
& $exe --shot settings:layout --expand advanced --size 1000x3100 --theme light --out settings-layout-adv.png
foreach ($p in 'launch','multiplayer','playtime','java','library') {
  & $exe --shot $p --theme light --out "$p-light.png"; & $exe --shot $p --edit --theme dark --out "$p-edit-dark.png"
}
& $exe --i18n-check     # 期望缺失 = 0
& $exe --smoke
```

手工核对:

1. 下载页右键空白 →「自定义布局...」:顶部一条编辑条(圆角 12),底部没有旧托盘;块是灰虚线,悬停出左上标签和右上眼睛,点一下变主题色实线。
2. 网格模式悬停一块:右边缘和下边缘各一个小把手;拖右边缘时指针旁出现「N / 12 列」,整列跳;拖动时有淡色列底纹,落点框是浅底虚线;拖下边缘改高度,双击下边缘恢复自动高度。
3. 选中内置块按 Delete → 隐藏并提示「在「添加」里可以找回」;选中便签按 Delete → 删除;Ctrl+Z 都能撤销。
4. 「添加」抽屉:没有隐藏项时是虚线空状态框;点「显示」/「添加」后页面滚到新块并闪一下主题色光晕。
5. 启动页右键进编辑:英雄区变淡压暗,右上「🔒 主区域固定，不能拖动或隐藏」,下方编辑条 / 标签是深色玻璃、虚线框白色半透明;点「完成」后英雄区恢复。
6. 设置「下载与网络」:内容区和左侧导航空白处右键都不出「自定义布局...」。
7. 设置 → 布局:页面卡片默认 6 张(启动 / 下载 / 实例库 / 联机 / 账号 / AI 助手),「显示全部 15 个页面」;实例库卡片「编辑」能跳到实例库并进编辑态;方案卡片每行 4 张,AI 备份显示「AI 改布局前 · 11-20 21:57」这样的名字;高级里「组件管理 · 已创建 N 个组件 · 管理…」。
8. 设置里换一个自定义主题色后进编辑态拖一下,落点框浅底跟着变色。
9. 手工把某页改成 `"mode":"canvas"`:打开该页按网格整齐显示,卡片上没有 8 向小方块;进编辑点「完成」后 JSON 变成 grid。
10. 拖动手感、抽屉、亮 / 暗主题下虚线框和标签颜色(沙箱里看不到窗口,全部未实测)。

## 9. 风险

- **全部界面效果未实测**:快照代码在本轮之前从没编译过,现在能编过、单测全绿,但 WPF 运行时的细节(抽屉覆盖层、浮动标签伸出卡片上沿被裁、`Hyperlink` 点击、光晕 Effect)只能真机看。
- 浮动标签 / 右上动作 / 下边缘把手都用负外边距伸出卡片外;网格第一行和页面最后一行依赖页面内边距(上 14 / 下 16)不被裁。
- `FlashGlow` 临时替换卡片的 `Effect`,动画结束再还原;若卡片原本挂了别的 Effect 且期间被别人换掉,不会还原成别人的。
- 「N / 列数」读数挂在 `_stage` 上,坐标取指针位置;窗口很窄时可能贴边。
- 体积余量 58,704 B,后续再加样式 / 词表要留意。
- 我只在 `C:\r35\win-intb` 里编译测试;`C:\r35\win-int` 里那份是你可能正在试的旧中间版本(见 §0),它的 bin 我没动过。

## 10. 真机问题修复(win-fix2,2026-11-21 12:10)

> 依据:用户录屏 `_r35\rec\rec0853.mp4`(截帧 f014 / f027)+ 用户真实布局存档(只读复制到 `C:\r35\_tmp\win-fix\home`,真实目录 `%LOCALAPPDATA%\TipCL` 一个字节都没写,修改时间仍是 8:55:16)。
> 提交 **`3cfcd38`**(代码 + 测试),本节随后单独提交。没改版本号、没 push、没碰 main。

| 项 | 结果 |
|---|---|
| compact `dist\windows\TipCL.exe` | **5,701,632 B**(上限 5,757,264,余 55,632)· SHA256 `C18BCCC130E49D9715C1D7FAD7F58F35DBCD61944064B1C74E6653A101601646` |
| premium `dist\windows-premium\TipCL.exe` | **5,721,600 B** · SHA256 `F48A2C2CC9A77F44F4CF8013238D2456D597000C7558D6E76D3A5C77A14F5609` |
| 构建 | Release compact / premium 0 错误,警告仍是那 2 条 xUnit1031 |
| 测试 | **759 过 / 0 败 / 6 跳**(765;新增 `R35WinFix2Tests` 13 例) |

### 10.1 逐条

1. **列表模式往上拖,块反而往下走** —— 原因是**位移符号反了**,不是被 ScrollViewer 吃掉:`LayoutEditor.OnDragMove` 浮起分支把当前指针写进了起手锚点 `_stackPointerY`(应写 `_stackPointerYLive`),`MoveStackFloat` 的位移 = 实时 − 锚点 就变成「起手 y − 当前 y」,方向正好相反;换位判定用的是真实指针,所以块往下走、落点却往上换,观感很乱。
   改法:只更新 `_stackPointerYLive`;位移公式抽成纯函数 `StackDragPlan.FloatOffset(基准, 锚点, 实时)`。
   测试:`PointerUpMovesFloatingCardUp`(指针上移 50 → 位移 −50)、`PointerUpGivesSmallerDropIndex`(一路上移落点下标只减不增、到 0)、`StackDragMoveUpdatesLivePointerNotAnchor`(源码守卫)。
2. **只能抓按钮 / 小标签才能拖** —— 原因:编辑态只把 `_body` 设成不可命中,外层 `ScrollViewer` 仍可命中,它在 MouseLeftButtonDown 里抢焦点并置 Handled,冒泡到卡片的 `OnCardMouseDown` 收不到;滚轮也被块内滚动吃掉。
   改法:编辑态整个内容体 `_bodyWrap.IsHitTestVisible = false`,卡片上盖一层透明命中层 `_editShield`(效果图 `.blk .bc{pointer-events:none}`),位置在内容体之上、手柄层与 v3 浮动件(眼睛 / 垃圾桶 / 右边缘 / 下边缘把手)之下;按下冒泡到卡片开始拖,滚轮冒泡给页面滚动。常态折叠,内容照常可点。顺带:退出编辑时下边缘把手也收起(原来漏了)。
   测试:`EditModeCardHasTransparentHitShieldBelowHandles`(层级顺序 + 透明底 + SetEdit 接线)。
3. **和效果图不一致**
   - **启动页三块通栏深色空面板**:默认值本身是对的(`PageDefaultLayouts.LaunchContent` 新闻可见、其余四块隐藏,测试 `LaunchDefaultsStillHideSmallCards` 钉住);用户存档是旧版纵向流(`mode=stack`、每块 span=1、全部可见 + 一个自建「游戏时长」),所以一张占一整行。
     改法:`LayoutV3Compat.LaunchLegacyStackAsGrid` —— 只对 launch、只对「旧版纵向流」(所有块 span ≤ 1;v3 里切到「列表」保存过的页带着 span,按用户选择保持列表)读时按默认网格呈现:顺序、显隐照存档,列宽取默认(新闻 12、任务 / 时长 / 便签 6、日志 12、未知块 6),首个放得下的行左对齐 → 新闻整行、任务+时长并排、便签+自建组件并排、日志整行。存档不动,编辑并「完成」一次后才按网格落盘。
     外观:四张小卡片改成效果图 `.wcard`:圆角 12、内边距 12/16、顶部 12px 60% 不透明小标签(下载任务 / 游戏时长 / 便签 / 日志),底色走与新闻卡同一套 `Glass/Tone`(有背景图 = 半透明玻璃,无背景图 = 主题纸色);便签 / 日志的输入框去掉自身底框,字色跟随卡片。
     测试:`LaunchLegacyStackRendersAsPairedGrid` / `LaunchLegacyStackKeepsHiddenBlocksHidden` / `LegacyStackConversionIsLaunchOnlyAndSkipsV3Docs` / `LaunchDefaultsStillHideSmallCards`。
   - **实例库编辑态红色竖线常驻**:离屏截图 + 像素扫描(`logs\win-fix2-redscan.ps1`)在静态编辑态里**没有**长红色竖线;对照交互确认是「按下即选中」留下的主题色 2px 选中框 —— 文件夹栏这类高块在视口里只露出左右两条边,拖完以后那条竖边一直停在页面中间。改法:拖完松手(列表 / 网格两条路径)取消选中;只是点一下仍保持选中(Delete / 方向键要用)。测试 `SelectionIsClearedAfterADrop`。
   - **实例库左栏「实例状态」排布错乱**:用户存档里 `status` 是旧版本从「添加」显示出来的契约默认矩形 + span 1(默认布局当时没登记这块)。改法:`DefaultLibraryPage` 登记 `status` 隐藏 + 4 列(与文件夹栏同宽);`LayoutV3Compat.FillMissingSpans` 对网格页里「契约矩形且 span ≤ 1」的块按默认补列宽(读时)→ 自动流到文件夹栏下面。测试 `RestoredBlockWithoutSpanTakesDefaultSpan`。灰色虚线框 / 悬停块名 / 选中主题色本来就按 layout3.html 实现,截图核对一致。
   - **下载页编辑条夹在分类标签行下面**:编辑的只是「原版」栏的区块宿主,标签行在宿主外面。改法:`DownloadPage` 订阅编辑器 `EditingChanged`,编辑期间收起标签行,编辑条落在内容区顶部;宿主边距为 0 的页编辑条左右留 22px、上 14 下 12(效果图 `.bodyA` padding;`LayoutV3Compat.EditBarSideMargin`)。测试 `DownloadEditBarSitsAtContentTop`。
   - **「添加」抽屉「游戏时长」两次**:类型行「游戏时长 · 本周每天玩了多久」+ 末尾「你建过的游戏时长」。改法:去掉「你建过的 X」整段;单例类型(时钟 / 快捷启动 / 游戏时长)的「添加」如果以前建过且本页没放,直接把那个加回来。上半部分「已隐藏的内容」一直在(空时虚线框)。测试 `AddDrawerListsEachWidgetTypeOnce`。
   - **插件页 / 设置 → Java 里的「自定义布局...」按钮**:源码里两页(以及所有页面内容)**没有**这样的按钮,用用户数据离屏截图也没出现;录屏里能看到的「自定义布局...」只可能是右键菜单。按「入口只留右键」把壳级右键菜单收成 v3 口径:常态只有「自定义布局...」,编辑态「完成编辑」「重置本页」;去掉 v2 留下的「布局设置...」。测试 `RightClickIsTheOnlyEntryAndMenusAreV3`(含「页面里没有自定义布局按钮」守卫)。
4. 全量测试 759 / 0 / 6(≥ 746 / 0 / 6)。

### 10.2 截图(离屏,`D:\项目\TipCL\_r35\shots\win\`)

- 先读了 `DevShot.cs`:主窗口 `ShowActivated=false`、`ShowInTaskbar=false`、摆在虚拟屏幕左上角之外(`VirtualScreenLeft − 宽 − 200`),用 `RenderTargetBitmap` 出图,跑完 `Environment.Exit`;`--shot` 在单实例 / 提权逻辑之前分流。进程用 `Win32_Process.Create` + `ShowWindow=0` 隐藏启动,`TIPCL_HOME=C:\r35\_tmp\win-fix\home`。
- `s0-*` = 修复前(cd8a572),`s1-*` = 交付前一版(只差便签输入框去底框),`s2-*` = 交付版本;`before-sheet.jpg`(s0)/ `after1-sheet.jpg`(s1)为六张缩略拼图(实例库编辑 / 启动 / 下载编辑 / 启动+添加抽屉 / 插件 / Java)。只看了这两张拼图,s2 没再看。
- 截图里看到:启动页小卡片两两并排、带小标签;下载页编辑条在内容区顶部;抽屉里没有重复项。

### 10.3 没解决 / 待确认

1. 「插件页 / Java 页里的按钮」没在代码和截图里找到实体,只做了右键菜单收口;如果真机上还看得到,需要一张那个位置的截图。
2. 用户存档里「插件」页是 `content / tabs / body` 的纵向顺序(内容在页签上面,另有一个不认识的 `body` 块),截图里插件页「已安装 / 市场」页签在空状态下面 —— 这是存档里的排列,本轮没改;在插件页右键 → 自定义布局 → 重置本页即可恢复。
3. 拖动手感、悬停态、命中层是否挡住了某个应可点的东西(例如自建网页块)只能真机确认;离屏截图拍不到悬停 / 拖动过程。
4. 启动页旧存档的网格呈现在用户「完成」一次编辑后才写盘;不进编辑就一直是读时推导。

## 11. 启动页 v3(win-launch,2026-11-21 14:10)

> 依据:用户定稿效果图 `_r35\ref\launch-v3.html`(取代 pages3.html 的启动页部分;CSS 数值照抄)+ 用户两张截图。用户追加「复刻」:一比一,不是参考。
> 提交:`745dba3`(主体)、`2e97561`(词表 + 迁移顺序)、本节随后一次提交(设置 → 布局启动页卡片 + 报告)。没改版本号、没 push、没碰 main。
> 全程没有弹窗口:构建 / 测试 / 截图都用 `Win32_Process.Create` + `ShowWindow=0` 隐藏启动;截图只用离屏 `--shot`,`TIPCL_HOME=C:\r35\_tmp\win-fix\home`(复制来的用户数据),真实 `%LOCALAPPDATA%\TipCL` 没碰。

### 11.0 结论

| 项 | 结果 |
|---|---|
| compact `C:\TipCL-R35\windows\TipCL.exe`(= `_r35\dist\windows\TipCL.exe`) | **5,735,424 B**(上限 5,757,264,余 21,840)· dist 里那份 SHA256 前缀 `6B373C163A9CA180`(含 11.4 的设置页改动) |
| premium `C:\TipCL-R35\windows-premium\TipCL.exe`(= `_r35\dist\windows-premium\`) | **5,754,880 B** · SHA256 `A0C4F0AA4F8CC9D597E29739CA430F2B0A01BB1CE3CF67567AF71F48421248A2`(由 `2e97561` 编出;只差最后一处设置 → 布局「启动」卡片改动,见 11.8) |
| 构建 | Release compact / premium 0 错误,警告仍是那 2 条 xUnit1031 |
| 测试 | **792 过 / 0 败 / 6 跳**(798;基线 759 / 0 / 6) |
| 体积 | 新代码约 +33 KB;为留余量把内嵌默认背景 `PremiumAssets\hero.jpg` 用质量 80 重新压缩(275,898 → 234,735 B,1600×962 不变,上面压着 45–80% 的深色渐变,肉眼看不出) |

### 11.1 逐条规则 → 落地点

| 效果图规则 | 落地 |
|---|---|
| 主区域不进布局系统:实例名、启动、联机是固定层,不能隐藏 / 拖动 / 缩放 | `LaunchPage.cs` BuildHero:**删掉** `BlockHost("launch")`(那一排排下载任务 / 游戏时长 / 便签 / 日志大卡片);根 Grid 只放背景、主区域(`.hero` 左 56、下 70、右 = 24 + 组件区宽 + 40,垂直居中)、新闻条、组件区、AI。测试 `MainAreaIsNotInTheLayoutSystem` |
| 组件区在右侧,4 列网格:列宽 74、行高 96、间距 12;只有 2×1 / 2×2 / 4×2 | `Pages/LaunchZone.cs`(纯逻辑):`Col/Row/Gap`、`W/H/Normalize`、`Pack`(按顺序首个能放下的位置,先行后列);`LaunchPage.Zone.cs` `_zHost` 右 24、上 18(标题栏下),332×420。测试 `GridMetricsMatchMockup` `OnlyThreeSizes` `PackNeverOverlaps`(200 轮随机,不叠放) |
| 窗口变窄减列数不缩字:4 → 2 列,4×2 换 2×2 版式,放不下的收起并提示;取代 FitBox 55% | `ColsFor`(页面宽 < 900 → 2 列)、`Eff`(窄窗口 l 按 m 摆)、`RowsFor`(页面矮时减行);溢出 → 右下「另有 N 个组件放不下，窗口变宽后自动回来」(宽窗口溢出提示「移除一个或换成小尺寸」)。启动页不再用 FitBox。测试 `ColumnsByWidth` `NarrowWindowTurnsLargeIntoMediumAndCollapsesTheRest` `RowsFollowPageHeight`;截图 `v2-narrow*.png` |
| 松手就按顺序重排,不叠放、不留洞;吸附 / 靠拢不是开关,没有自由画布 | 拖动 = 效果图 onMove:被拖组件跟手(放大 1.03 + 阴影 + 主题色实线),其它组件 220 ms 让位,主题色 24% 底 + 1.5 虚线落点框(150 ms);`BestOrder` 选落点中心离指针最近的插入位置;松手提交顺序。没有任何吸附 / 画布开关。测试 `DragToTopLeftPutsItemFirst` `DragAtOwnPlaceKeepsOrder` |
| 编辑态:右下「编辑组件」进入;编辑条只剩「添加组件 / 重置 / 完成」+「⋯」;提示「拖动组件换位置 · 点尺寸标签切换大小 · Ctrl Z 撤销 Ctrl Y 重做」;导入 / 导出 / 复制布局码 / 放弃这次改动收进「⋯」 | `BuildZoneBar`:白底 97%、圆角 12、内边距 8/8/8/14、阴影 0 10 30 .35;「编辑启动页」13 号 800;`.b` 30 高 / 圆角 8 / 左右 12 / 13 号;soft(主题色 12% 底 32% 描边)「＋ 添加组件」、普通「重置」、pri「完成」、30×30「⋯」;下面 12 号 72% 白提示 + 两个 kbd。「⋯」菜单 200 宽、圆角 12:导入布局… / 导出布局… / 复制布局码 / 分隔线 / 放弃这次改动。Ctrl+Z / Ctrl+Y(含 Ctrl+Shift+Z)/ Esc 挂在窗口 PreviewKeyDown(只在编辑态)。测试 `EditBarHasOnlyAddResetDoneAndMore` |
| 编辑态主区域显示「主区域固定，不能拖动、隐藏或缩放」 | `BuildHeroLockChip`(在主区域最上面,12 号、4/10、圆角 8、14% 底 22% 描边、下 12);编辑态主区域 45% 不透明 + 整页 rgba(8,10,16,.5) 压暗;新闻条 / AI 淡出;网格空格显示 1px 18% 白虚线格(74×96、圆角 8) |
| 点组件上的尺寸标签在 2×1 / 2×2 / 4×2 之间切换 | 组件右上 `.tools`(上 6 右 6、间距 4;22 高、圆角 8、92% 白、墨色 12 号粗体):尺寸标签 + ×。`NextSize` 跳过「看起来一样」的尺寸;放不下提示「这里放不下 4×2，先挪开或移除别的组件」;单尺寸提示「窄窗口下这个组件只有一种尺寸」。测试 `SizeTagCycles…` `SizeTagSkipsSameLooksOnNarrowWindow` `SizeTagRefuses…` `SingleSizeWidgets…` |
| 组件库:「点『添加』放进右侧组件区，放满了会提示」 | `BuildZoneDrawer`:左侧 344 宽白底抽屉(标题栏下到底,0.28 s 滑入,阴影 12 0 40 .35);「组件库」15 号 800 + 12 号 ink3 说明 + 关闭钮;每行 `.lc`(112 / * / auto,间距 12,内边距 8,圆角 12,#f3f3f3):112×70 缩略图(背景图压 45% 暗 + 真组件等比缩到 104×62 内)、名称 13 粗 / 说明 12 ink2 / 尺寸 12 等宽 ink3、「添加」(已有 → 「已添加」灰字)。放满 → 「组件区放满了：先移除一个，或把大组件换成小尺寸」 |
| 常态不显示区块标题;「本周游戏时长」是内容,开发命名不露 | 组件里只有效果图 `.lb` 那样的内容标签(本周游戏时长 / 生存服名字 / 便签 / 下载任务 / 游戏日志 / 最近截图 …),没有区块标题栏;自建组件显示它自己的标题 |
| 两档圆角、五档字号:8 / 12;12 / 13 / 15 / 21 / 34 | 组件区代码里圆角只有 8、12(柱状图柱子 4 = 效果图 `.bt i`),字号只有 12 / 13 / 15 / 21 / 34;主区域:标签 12、按钮 13 / 15、标题 34、新闻 12。测试 `OnlyTwoRadiiAndFiveFontSizesInZone`(源码扫描) |
| 常态版式:左大标题 + 标签 + 启动游戏 / 设置 / 联机,左下新闻条;右侧「本周游戏时长」4×2 带每日柱状图、「生存服」2×1、快捷入口(模组 / 截图 / 日志)2×1;右下「编辑组件」「AI Ctrl K」;半透明玻璃卡片 | 启动按钮 48 高 / 圆角 12 / 左右 28 / 15 号 800 + 实心三角 18;配置 48×48 滑杆图标;联机 48 高 / 左右 16 / 13 号 700 + wifi 图标(图标都是效果图内联 SVG 的路径);标签 12 号 3/10 圆角 8;新闻条 左 56 下 20、8/14/8/8、圆角 12、12% 底 18% 描边;「编辑组件」上 = 18 + 组件区高 + 10、28 高、圆角 8、12% 底 20% 描边、70% 不透明(悬停 100%);「AI Ctrl K」右 24 下 20、36 高、圆角 12、点了进 AI 页;组件卡片 rgba(14,18,27,.48) + 14% 白描边 + 圆角 12 + 内边距 12/14。默认组件 = 效果图 DEF(游戏时长 4×2 + 服务器 2×1 + 快捷入口 2×1),数据都是真的(见 11.3) |
| 现有内容都映射成组件,只能三档尺寸;存档里的启动页块迁进组件区(读时推导,存档原样,编辑器提交一次才写新格式) | 组件类型 = 效果图 6 类(游戏时长 / 服务器状态 / 快捷入口 / 便签(可多张)/ 时钟 / 最近截图)+ 现有内容 2 类(下载任务 / 游戏日志)+ 用户自建组件 / 插件区块(`ref`,按原类型给尺寸)。`LoadZoneItems`:`UiPrefs.LaunchZone` 有就用;没有 → `LaunchZone.FromLegacy` 从 `ui_layout_v2.json` 的 `pages.launch.blocks` 读时推导(新闻 = 固定新闻条;隐藏的不迁;只有新闻 → 效果图默认;游戏时长 4×2 排第一,其余 2×1 → 用户那份正好放满 4×4 不溢出);旧文档一个字节不动,编辑器「完成」且有改动才写 `UiPrefs.LaunchZone`。第一张便签继续读写 `UiPrefs.Notes`。测试 `LegacyDefaultOnlyNewsGivesMockupDefaults` `LegacyUserBlocksMapToWidgetsAndFitTheGrid` `LegacyHiddenBlocksStayOut` `JsonRoundTrip` `BadInputIsRejected` `UnknownTypesAreDroppedAndIdsMadeUnique` |

其他接线:设置 → 布局「启动」卡片「编辑」→ 跳到启动页直接进组件区编辑态(`LayoutShellHooks.BeginEditPage("launch")`);卡片的「已自定义 / 默认」看组件区存档,「恢复默认」= 组件区回到效果图默认(旧区块文档不动)。右键启动页空白:常态「编辑组件」/ 编辑态「完成」。切走页面时在编辑态 = 「完成」。DevShot `--shot launch --edit` / `--pop layout-drawer` 走组件区。

### 11.2 截图对照(离屏 1000×600 / 780×600,`D:\项目\TipCL\_r35\shots\win-launch\`)

文件:`v2-wide.png` `v2-wide-edit.png` `v2-narrow.png` `v2-narrow-edit.png` `v2-wide-lib.png`(v1-* 是迁移顺序调整前一版);效果图截图缩到同尺寸 `mock-wide-1000x600.png` 放在 workspace `artifacts/r35launch/`。看了 4 张拼图(效果图 + v1 宽常态;v1 宽编辑 / 组件库 / 窄常态 / 窄编辑;v2 宽常态 + 下载编辑 + 设置 → 布局)。

| 区域 | 效果图(1000×600) | 新版 | 差异 / 处理 |
|---|---|---|---|
| 组件区 | 右 24、标题栏下 18、332×420;顶部 4×2 本周游戏时长 + 柱状图,下排 2×1 + 2×1 | 同位置同尺寸;顶部 4×2「本周游戏时长」+ 7 根柱(今天主题色、未来虚线) | 一致。用户存档迁移后下面是下载任务 / 便签 / 游戏日志 / 自建「游戏时长」(用户自己的块),不是生存服 / 快捷入口 —— 这是规则要求的迁移;点「重置」或设置 → 布局「恢复默认」就是效果图那三块 |
| 组件卡片 | 48% 深色玻璃、14% 描边、圆角 12、12/14 内边距 | 同 | WPF 没有 backdrop-filter 模糊,只有半透明(和现有新闻条同样处理) |
| 主区域 | 左 56;就绪行 12 号;标题 34 号 800;标签;按钮行 | 同 | 标题位置随内容高度垂直居中:没装版本时多一行「这个实例还没有版本…」状态行,整块上移约 10 px(效果图没有这行) |
| 启动按钮 | 主色实底白字 | 同(截图里这个实例没装版本,按钮是禁用的半透明态) | 数据差异 |
| 新闻条 / 编辑组件 / AI | 左下 / 组件区下 10 / 右下 | 同位置同尺寸 | 一致 |
| 标题栏 | 无 AI 药丸,头像 + 名字 | 有 AI 药丸 + 主题切换(全局壳,不属于启动页) | 没改:标题栏是所有页面共用的壳,AI 药丸可在 设置 → 布局 → 标题栏 隐藏 |
| 编辑态 | 压暗、主区域 45%、锁提示、编辑条左上、虚线组件 + 尺寸标签 | 同 | 一致;WPF 没有 `filter: saturate(.5)`,用压暗近似 |
| 组件库 | 344 白抽屉 + 缩略图行 | 同 | 列表超出时竖滚动条占约 10 px,「游戏时长」那行尺寸「2×2 · 4×2 · 2×1」末尾被截 |
| 窄窗口 780 | 2 列,4×2 → 2×2,放不下提示 | 2 列,游戏时长 2×2,「另有 2 个组件放不下，窗口变宽后自动回来」 | 一致 |

### 11.3 真实数据替换示例数据

- 本周游戏时长:`Playtime.GetAllPlaytime()` 的会话按本地日期归到周一…周日(`LaunchZone.Week`),大数字 < 1 小时按分钟;「比上周多 / 少 …」「玩得最多：<实例>」。测试 `WeekBucketsByLocalDayStartingMonday`。
- 服务器:当前实例 `list_servers` 第一个;新增 `ServerPing`(Java 版 Server List Ping,2.5 s 超时)取在线 / 上限 / 延迟 / 玩家名首字;「加入服务器」= 填直连地址后走正常启动;没有服务器 → 「还没有服务器」/「添加服务器」(去服务器页)。测试 `ServerPingParsesStatusJson`。
- 快捷入口:当前实例(版本隔离时优先 `versions/<版本>/`)的 mods / screenshots / logs / saves,资源管理器打开。
- 最近截图:两个 screenshots 目录按时间倒序,缩略图走 `WebpFallback`(Win7 守卫)。
- 下载任务 / 游戏日志:`TaskStore` / 启动输出(日志 0.5 s 合并刷新)。

### 11.4 layout3.html 对照(下载页编辑态、设置 → 布局)

截图 `k1-download-edit.png`(1000×720)、`k1-settings-layout.png`(1000×1100)。
- 下载页编辑态:编辑条「编辑「下载」页 / 列表·网格 / 提示 / 撤销·重做图标 / ＋ 添加 / 重置本页 / 完成 / ⋯」与 layout3 `.ebar` 结构一致,位于内容区顶部(win-fix2 已修)。本轮没有发现需要改的差异。
- 设置 → 布局:外观(区块间距 紧凑 / 标准 / 宽松)、导航(顶部页签拖动 + 眼睛、恢复默认;标题栏 AI 按钮 / 主题切换 / 账号)、页面缩略图卡片 3 列 + 已自定义 / 默认 + 编辑 / 恢复默认、「只有一块内容不提供编辑」+「显示全部 N 个页面」、方案(导入… / 导出整套布局… / 存为新方案)、高级,结构与 layout3 secB 一致。
- 改了一处:「启动」卡片原来看的是旧区块文档(显示「已自定义 · 调整了顺序」,「恢复默认」会去改已经不用的旧文档),现在看组件区存档,「恢复默认」恢复组件区。
- 「显示全部 15 个页面」(layout3 写 10)是实际页面数,不改。

### 11.5 删掉的断言(都针对已删的启动页 BlockHost)

- `PageDefaultsV3Tests.DefaultPages` 去掉 `launch` 一行(影响 5 个 Theory:`WithPageEntryTheHostFollowsTheDocumentExactly` / `DefaultCoversExactlyThePageBlocksAndIsWiredIntoTheHost` / `DefaultSurvivesAStoreRoundTrip` / `WithoutPageEntryTheHostRendersTheNewDefault` / `PagesWithV3DefaultsStillOfferTheMenu` 的 launch 用例)。
- `PageDefaultsV3Tests.HostTableRowMatchesSource` 去掉 `[InlineData("launch", …)]`。
- 删 `PageDefaultsV3Tests.LaunchHiddenBlocksPairUpWhenShownAndLogTakesTheWholeRow`(解析 LaunchPage.cs 里的 BlockSpec 列表)。
- `R35WinFix2Tests.LaunchDefaultsStillHideSmallCards`:删源码部分(`new BlockSpec { Id = … DefaultVisible = false` 与 `LaunchMiniCard(...)` 断言),`PageDefaultLayouts.LaunchContent()` 的默认值断言保留。
- `R35WinIntTests.CrossPackageWiringIsInPlace`:删 `_blocks.Editor.EditingChanged += OnLayoutEditingChanged;` 一行。
- 新增 `R35LaunchV3Tests`(40 例);净变化 759 → 792。

### 11.6 词表

zh_CN / en 成对 +62 条(编辑组件、编辑启动页、添加组件、导入布局… / 导出布局… / 复制布局码 / 放弃这次改动、组件库说明、主区域固定提示、本周游戏时长、比上周多 / 少、服务器在线、各组件名称与说明、提示语等),`.json.gz` 用 `tools/pack-core-resources.ps1` 重新打包。废弃:「主区域固定，不能拖动或隐藏」(旧右上角提示)不再使用,key 保留。

### 11.7 没做到 / 待真机

1. 背景模糊(`backdrop-filter: blur`)和主区域去饱和(`saturate(.5)`)WPF 做不到,用半透明 / 压暗近似。
2. 拖动手感、悬停、Ctrl+Z / Ctrl+Y、导入导出对话框、服务器延迟只能真机看;离屏截图拍不到拖动过程。
3. 标题栏的 AI 药丸是全局壳,效果图启动页上没有;没删(其他页要用),可在 设置 → 布局 → 标题栏 关掉。
4. 设置 → 布局「启动」卡片的缩略图仍按旧区块文档画(示意图),没改成组件区缩略图。
5. 旧的 `LayoutV3Compat.LaunchLegacyStackAsGrid` 与 `PageDefaultLayouts.LaunchContent()` 已不被启动页使用,为了不动别的测试先留着。

### 11.8 停在哪一步(14:12 本机命令桥断开,之后 15 分钟没恢复)

- 已完成并提交:`745dba3`、`2e97561`。已编过、测过(792 / 0 / 6)但**还没提交**:`SettingsLayoutPage.cs`(启动卡片看组件区)、`LaunchPage.Zone.cs`(`ZoneReload`)、`locales/{zh_CN,en}.json(.gz)`(+「调整了组件」)、本报告 §11。提交信息已写好:`_r35\logs\win-launch-msg3.txt`。补提交:
  `git -C 'D:\项目\TipCL\_r35\win-int' add -A windos; git -C 'D:\项目\TipCL\_r35\win-int' -c user.name=TipCL-agent -c user.email=agent@local commit -F 'D:\项目\TipCL\_r35\logs\win-launch-msg3.txt'`
- `_r35\dist\windows\TipCL.exe` 已是最新 compact(5,735,424)。`C:\TipCL-R35\windows\TipCL.exe` 与它同一条命令先复制,桥断前应已完成(没能复核哈希)。
- premium:最新代码的 premium 构建在桥断时刚启动,结果没能确认;`C:\TipCL-R35\windows-premium\` 与 `dist\windows-premium\` 里是 `2e97561` 的 premium(功能齐全,只差设置页启动卡片)。补:`win-fix-go.ps1 -Tag P7 -Mode premium -NoSync` 后把 `C:\r35\win-intb\windos\src\TipCL\bin\Release\net48\TipCL.exe` 复制到这两处。
