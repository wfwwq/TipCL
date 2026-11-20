# R35 · lin-int —— 布局 v3 WP-L6 集成(Linux 树)

> 分支 `r35/lin-int` · 工作区 `D:\项目\TipCL\_r35\lin-int` · 起点 4eb32fc(主 agent 已按 l0 → l3 → l5 → l4 → l12 `merge --no-ff`,git 零冲突)。
> 任务书:`_r35/R35-INT-BRIEF.md` §2 / §3;规格 `windos/GOAL-layout-v3.md` §6.3 WP-L6、§8。2026-11-21 00:20 收尾。
> 只跑了 `dotnet build` / `dotnet test`(经 `_r35\build.ps1` 与同口径的脱离桥脚本);**没有启动 TipCL.App,测试没有开浏览器 / 文件管理器**。

## 1. 数字

| | 编译 | 测试(通过 / 失败 / 跳过 / 合计) |
|---|---|---|
| 各包开工基线(lin-base 7039719) | 0 错误 · 4 警告 | 978 / 0 / 7 / 985 |
| 集成起点 4eb32fc(合并后未改一行) | **0 错误 · 5 警告**(直接编过,无合并编译错) | —(未单独跑,见下) |
| 接线后第 1、2 次全量 | 0 错误 · 4 警告 | 1063 / **1** / 11 / 1075 |
| **收尾全量**(`logs/lin-int-full.log`,00:14) | **0 错误 · 4 警告** | **1064 / 0 / 11 / 1075** |

- 合计 1075 = 985 + L0 29 + L12 24 + L3 10 + L4 14 + L5 13,与各包报告的新增数逐一对得上。
- 跳过 11 = 基线 7(CliSmoke ×5、SmokeIntegration ×1、LayoutMotionAndHeight ×1)+ **lin-l3 已列明的 4 例**(`LayoutGridCanvasTests` 里针对已退役 canvas 自由画布界面的断言:`CanvasModeMapsNormalizedRectsAndUsesHostHeight` / `CanvasModeWithInfiniteHeightFallsBackToReferenceHeight` / `CanvasModeDeliversChildrenInZOrderAndMatchesById` / `CanvasHatchFollowsSnapStepAndSkipsWhenStepIsZero`)。集成没有再删 / 跳任何断言。
- 改动的既有断言只有 1 处:`App/WidgetTests.cs:533`(任务书点名,见 §2 第 9 条)。
- 跑法:整套在一个 testhost 里跑(不是分段);用 `Invoke-CimMethod Win32_Process Create` 启动 `C:\r35\_tmp\lin-int\t.cmd` 脱离 MCP 桥(用 `.ps1` 或带中文路径的命令行起不来,换成纯 ASCII 路径的 `cmd /c` 才行),每次约 45–115 s。
- 警告 5 → 4:L5 带来的那一条是 `BlockHost.cs:222` CS8604(`specs` 可能为 null 传进 `LayoutEditor`),已消。剩下 4 条都是基线就有的:`LayoutEditor._foldAllBtn` 从未赋值(CS0649,「全部折叠」界面已删、字段留着)、`LayoutMotionAndHeightTests` 的 3 条 xUnit 分析器提示。

## 2. 接线清单(任务书 §3,逐条)

| # | 项 | 状态 | 落点(文件:行) |
|---|---|---|---|
| 1 | L12←L3:`CanvasToGrid` 传 `minColsOf` | ✅ | `LayoutEditor.cs:612-614`(读入时按 `BlockSpec.MinCols` 换算到 `SnapCols(cols)`,下限 2) |
| 1b | 面板推导也认 MinCols | ✅ | `GridCanvasPanel.cs:151-155` 新属性 `MinColsOf`;`:396` `CanvasGridView(page, MinColsOf)`;`:530` `ApplyTo` 提交时 `CanvasToGrid(page, MinColsOf)`;编辑器 `LayoutEditor.cs:740-741` 每次 `RenderBox` 给它赋值 |
| 2 | L12←L3:canvas 分支改读面板实际 Mode | ✅ | `LayoutEditor.cs:749-751`(吃满视口判定读 `_box.Mode` / `_box.RowH`)、`:763` `card.SetBoxMode(_box.Mode)`、`:768` `SetFixedHeight` 读 `_box.Mode`;`:3210-3212` 卡片 `SetBoxMode` 把 canvas 一律折成 grid ⇒ 8 向手柄(`:3388` 只给 canvas)不可达。`IsBoxMode` 保持「grid 或 canvas」(canvas 页在面板里就是网格,仍走画布宿主) |
| 3 | L4←L3:页面卡片缩略图 canvas 先转网格 | ✅ | `SettingsPage.Layout.cs:787-797`(`ThumbFromPage` 对 canvas 先 `LayoutEngine.CanvasGridView` 副本再按 grid 画,删掉了按 w×6 的 canvas 分支) |
| 4 | L12←L5:「自定义布局…」过滤 = `OffersLayoutMenu && Editor.CanEdit` | ✅ | `LayoutShellHooks.cs:449-451`(`Collect`/`FindHosts`)、`:307-308`(`CanEditPage` 的在屏宿主分支) |
| 4b | `CanEditPage` / `CanEdit` 计数口径与 `BlockHostRules` 对齐 | ✅ | `LayoutEditor.V3.cs:57-59` `RememberBlockCount` 改记 `_specs.Count`(内置块含默认隐藏,不含文档里的自建 / 插件块);`:74-75` `IsSingleBlockPage` 改为 `!BlockHostRules.OffersLayoutMenu(_specs.Count)`;入口表 SingleBlock 仍一律 false(`:68`) |
| 4c | 启动页压暗层只留一个 | ✅(无需改) | grep 确认 lin-l12 **没有**另铺 `B.EditDim` 压暗层;启动页只有 lin-l5 的英雄区 45% + 锁形标签(`LaunchPage.cs:494-505`)。见「决定」L-3 |
| 5 | L12/L3←L0:硬编码换令牌、删掉盖过主题的本地属性 | ✅(主体)/ 部分见 §5 | 见下表 |
| 6 | L3:启动页落点框白色半透明 | ✅ | `GridCanvasPanel.cs:157-160` 新属性 `DarkSurface`;`:635-660` `EnsurePens`:浅色 = `B.EditDropFill` 浅底 + 主题色 1.5px 3,2 虚线(= `Layout.DropBox`),深色 = `B.EditDropFillOnDark`(白 12%)+ `B.OnEditGlass` 白线,网格线白 15%;`:1016` 圆角 10 → `RadiusCard` 12;编辑器 `LayoutEditor.cs:746-747` 按 `IsDarkSurface`(pageId == "launch")设 |
| 7 | L0 截图开关:`SelectBlockHook` / `OpenAddDrawerHook` 接正式入口 | ✅ | 新正式入口 `LayoutEditor.V3.cs:487-512` `SelectBlock(string)`(编辑态 + 块在本页才选中)、`:514-519` `OpenAddDrawer()`;`SmokeRunner.cs:183-189` 两个钩子默认值接上(置 null 仍走反射兜底,L0 的反射测试照旧过) |
| 8 | `WidgetUI.MissingWidget` 去掉块 id | ✅ | `WidgetUI.cs:53-59` 说明文字改为「找不到这个组件的内容，可能已被删除；可以在编辑态点「添加」重新放一个。」 |
| 9 | 同步 `App/WidgetTests.cs:533` 断言 | ✅ | `WidgetTests.cs:533-535`:断言新文案,并加一条「任何文字都不含块 id」 |
| 10 | 下边缘把手 tooltip | ✅ | `LayoutEditor.cs:3170-3171`「拖动改高度 · 双击恢复自动高度」(进词表) |
| 11 | L5:`SettingsSubView.Layout.cs` 新重载保留;警告 4→5 | ✅ | 重载原样保留;第 5 条警告是 `BlockHost.cs:222` CS8604,`BlockHost.cs:222-223` 改传 `specs ?? Array.Empty<BlockSpec>()`,已消 |
| 12 | L4:补 §6 的 18 条词条;废弃「账号名称」 | ✅ | 见 §3;「账号名称」源码里已无引用,词表里保留 |

### 2.1 L0 令牌接线明细(第 5 条)

做法核对:L0 用的是 **ControlTheme**(不是 Style Classes),L12 的 `TryApplyTheme` 也是按 ControlTheme 写的,TargetType 能对上(Border / Button / TextBlock / Rectangle / ToggleButton)。集成统一改走 `LayoutVisualTokens.ApplyTheme`,**套上主题就不再写同名本地属性**(本地值优先级高于主题,原来 `_barHost` / `Chip` / `_drawer` 都是「先写本地值再套主题」,主题等于没生效);套不上时保留 C / L12 的兜底外观。

| 位置 | 改为 | 文件:行 |
|---|---|---|
| 编辑条 `_barHost` | `Layout.EditBar`(高 46、圆角 12、内距 14/8、`B.EditBarBg`/`B.EditBarLine`/`Fx.EditBar`);上距 `EditBarGap` | `LayoutEditor.cs:252-275` |
| 分段轨道 | `Layout.SegRail` | `LayoutEditor.V3.cs:128-136` |
| 列表 / 网格分段 | `Layout.SegItem`:按钮换成 `ToggleButton`(字段类型仍是 Button),选中态由 `IsChecked` 表达;图标 `Geo.ListMode/GridMode` 13 | `LayoutEditor.V3.cs:228-245`(构建)、`:266-279`(`RefreshEditBar` 选中态) |
| 提示文字 | `Layout.EditHint` | `LayoutEditor.V3.cs:137-143` |
| 撤销 / 重做 | `Glyph("Geo.Undo"/"Geo.Redo", 15, owner)`(线宽 1.5,跟随按钮前景,置灰自动变色) | `LayoutEditor.V3.cs:156-157`、`:163` |
| 竖分隔线 | `Layout.EditBarSep`(`B.EditOutline`) | `LayoutEditor.V3.cs:165-175` |
| 「添加」角标 | `Layout.Count` + `Layout.CountText`(字号 12、圆角 8) | `LayoutEditor.V3.cs:183-207` |
| 抽屉 `_drawer` | `Layout.Drawer`(316、贴右、左 1px、`Fx.Drawer`、无圆角) | `LayoutEditor.V3.cs:570-598` |
| 抽屉标题 / 分节字 | `Layout.DrawerTitle` / `Layout.DrawerSection` | `LayoutEditor.V3.cs:523-529`、`:602-610` |
| 抽屉空状态框 | `Layout.DrawerEmpty` | `LayoutEditor.V3.cs:700-718` |
| 抽屉项 / 图标框 | `Layout.DrawerItem` / `Layout.DrawerIcon` | `LayoutEditor.V3.cs:747-776` |
| 块名标签 `Chip` | `Layout.EditLabel`(套上后不再写圆角/边/内距/阴影/底色);文字 `Layout.EditLabelText` / `Layout.EditLabelNote`;拖拽点 `Glyph("Geo.Grip", 12)`(线宽 2.2×size/16) | `LayoutEditChrome.cs:188-213`;`LayoutEditor.cs:2981-2984`、`:3004-3022` |
| 块上眼睛 / 垃圾桶 | `Layout.IconAct` / `Layout.IconAct.Danger`(主题模板自带白底细边阴影,不再外包 Chip、不再写本地宽高与悬停底色;垃圾桶悬停变红由主题负责);眼睛图标 `Glyph("Geo.EyeOff", 14, owner)` | `LayoutEditor.cs:2996-3003`、`:3024-3047` |
| 右 / 下边缘把手 | 仍是 Border(见 L-6),底 `B.EditBarBg`、边 `B.EditOutline`、阴影 `Fx.ResizeGrip`、尺寸 `ResizeGrip*`、外伸 `ResizeGripOutset` | `LayoutEditor.cs:3119-3171` |
| 尺寸气泡「N / 列数」 | `Layout.EditTip` 口径:`B.Ink` 底、`B.Paper` 字、12 号、圆角 8(原来是主题色胶囊) | `GuideLayer.cs:121-134` |
| 落点框 | 见第 6 条 | `GridCanvasPanel.cs:635-660` |
| 新增辅助 | `LayoutEditChrome.ThemedText`(TextBlock 主题在就只设 Text + Theme,否则 `U.Txt`)、`GlyphFor` | `LayoutEditChrome.cs:214-236` |

## 3. 词表合入

- 合入方式:离线脚本 `_r35/logs/lin-int-i18n.py`(在设备 python 上跑,源码按任务书正则扫 `src/**` 全部 `I18nCore.Tr("…")` 含中文的字面量,和内嵌 `zh_CN.json` / `en.json` 比对),再按「一行一键」格式成对追加到两份表末尾(无 BOM、LF,与 README 约定一致)。
- 扫描结果:`I18nCore.Tr` 含中文字面量 1433 个;合入前缺 **30** 条(两表都缺),合入后缺 **0**;键数 3240 → **3270**(两表键集相同,差集 0)。
- 30 条来源:lin-l4 §6 18 条 + lin-l5 §4 9 条 + Java 版本对应句 1 条 + 集成新加 2 条(`MissingWidget` 新说明、下边缘把手 tooltip)。漏网扫描没有发现报告之外的缺口。译文清单:`_r35/logs/lin-int-i18n-add.json`。
- 废弃 key:lin-l12 的「松手后自动靠拢」系 6 条、lin-l5 的「状态卡 / 房间卡 / 操作区 / 玩家列表 / 累计卡 / 范围卡 / 明细列表 / 已检测列表 / 实例行 / 账号卡」、lin-l4 的「账号名称」、旧的「布局文档里找不到 {0} 的定义;可以重新添加同名区块。」—— grep 确认 `src/TipCL.App` 里都已无引用,**全部保留在词表里**(不删,见 L-7)。
- `.gz`:Linux 树的 `src/TipCL.Core/Resources/locales/` 没有 `.json.gz`(只有 `windos/` 子树有,本轮不碰),无需打包。
- `I18N-BASELINE.md`:设备上有 python,直接按仓库方式 `python tools/i18n-audit.py --baseline progress/I18N-BASELINE.md` 刷新。未接线 CJK 字面量 3605 → **3067**,已接线 1623 → **2300**(分母 5371)。`I18nBaselineTests`、`LayoutV3I18nTests`、`I18nCoreLocalesTests` 收尾全量里都是绿的。

## 4. 修掉的问题

1. 编译:合并后直接 0 错误(5 个包的改动没有撞同一成员),无需修编译错。
2. **主题被本地值盖掉**:`_barHost` / `Chip` / `_drawer` 原来先写本地圆角、底色、阴影再 `TryApplyTheme`,主题实际不生效 —— 见 §2.1。
3. **块上按钮双层底板**:IconAct 主题模板自己画底板,L12 又在外面包了一层 Chip,套上主题后会出现两层白底 + 两层阴影;现在套上主题就不再外包。
4. **全量测试里一条稳定红**:`DlgCloseTests.Confirm_WindowClosed_ReturnsFalse`(lin-l4 报告里记过它偶发)。原因:前一个用例发起的 AI 对话,其回调在下一个用例的 `Dispatcher.ResetForUnitTests` 里才执行,这时 `Application.Current == null`,`AiBubble` 构造里 `Res.Bind` / `StreamGeometry.Parse` 依次抛异常,把下一个用例弄红。合并后变成每次必现。修法:`Pages/Ai/AiPage.cs:653-689` 加 `AppGone` 判断,Application 不在时迟到的会话回调直接丢弃(正常运行时 Application 总在,行为不变)。**这个文件不在任何包的清单里**,是集成为了让全量 0 失败做的最小修改。
5. `ClickSweepTests` 跳过词再补「文件夹」「在编辑器中打开」:设置 → 布局 → 高级的「打开所在文件夹」不含「打开文件夹」子串,遍历时会被点到、调起系统文件管理器;「在编辑器中打开…」会开独立窗口(`ClickSweepTests.cs:58-59`)。原有的「主页 / GitHub / issue / …」跳过词确认在位。
6. `SmokeRunner.cs` 的 CRLF 行尾在编辑后被改成 LF,已按 HEAD 恢复 CRLF(否则 diff 是整文件)。

## 5. 仍未做 / 降级

- **启动页锁形标签**仍是 lin-l5 的「🔒 + 文字」Border,没换成 L0 的 `Layout.LockNote` + `Geo.Lock` 矢量锁:`PageDefaultsV3Tests.cs:467` 把 `HeroLock.Child` 强转成 `TextBlock`,换成「图标 + 文字」会改别人的断言;emoji 在没有彩色 emoji 字体的 Linux 上可能显示成方块(待真机)。
- **`LayoutCard` 自身**的圆角 10 / `B.Accent` 边色(`LayoutEditor.cs` 卡片构造)没改成 L0 建议的 `RadiusCard` 12:它也影响非编辑态的 plain / card 外观,集成里不动常态像素。
- **右 / 下边缘把手**没有换成 `Layout.ResizeGrip`(Thumb 版):要改整条按下 → `BeginBoxResize` 的事件链,只改了取值(L-6)。
- **深色表面上的 `.OnDark` 主题**(EditBar / EditLabel / IconAct / Drawer / SegItem 的玻璃版)没有用上,见 L-4。
- **canvas 退役代码**(`LayoutCanvas` 8 向手柄 / 吸附、`GridCanvasPanel` canvas 排布与方格底纹、`Nudge` canvas 分支、4 个 Skip 用例)仍按 lin-l3 的做法「留着不可达」,没有真删。
- **WP-L6 文档**(`docs/布局使用指南.md`、`docs/布局快速上手.md` 改写)与**截图矩阵**都依赖真机截图,本轮没做。
- `--i18n-check`(GOAL §8)是 Windows 入口,Linux 没有;以 §3 的离线扫描(缺 0)+ `LayoutV3I18nTests` 代替。
- Release / premium 两版构建、体积门槛是 Windows 条目,本轮只做了 Linux Debug 构建 + 测试。

## 6. 决定(候选,请统一编号 D-82 起)

- **L-1 Q5 计数口径**:「≥ 2 块才给编辑入口」数的是**页面交给宿主的内置块(含默认隐藏,不含文档里的自建 / 插件块)**;`BlockHostRules`、`LayoutEditor.CanEdit`、`LayoutShellHooks.CanEditPage` / 右键菜单、`KnownBlockCount` 四处同一口径(同 Windows 与 lin-l5 R35-L5-3)。
- **L-2 canvas 在编辑器里一律按 grid**:读入时 `CanvasToGrid`(带 MinCols);万一 canvas 页绕过读入直接进来,面板推导成网格、卡片 `SetBoxMode` 也把 canvas 折成 grid,8 向手柄不可达。
- **L-3 启动页只保留英雄区 45% 一层压暗**:lin-l12 没有另铺 `B.EditDim` 压暗层,不新增(`B.EditDim` 只用在锁形标签底色)。
- **L-4 深色表面上编辑条 / 块名标签 / 块上按钮 / 抽屉仍用白底**,只有虚线框、列底纹、落点框换白色半透明 —— 沿用 lin-l12 对 pages3 `.win.edit.dark` 的读法;L0 做好的 `.OnDark` 玻璃主题暂不启用(调用处已集中在 `ApplyTheme(…, onDark)`,要改只需传 `IsDarkSurface`)。
- **L-5 「列表 / 网格」分段改用 ToggleButton + `Layout.SegItem`**,选中态 = `IsChecked`;L0 主题不在时退回 Btn.Normal / Btn.Ghost 两套主题。
- **L-6 边缘把手保留 Border**,只读令牌(不换 Thumb)。
- **L-7 废弃词条一律保留**(只增不删,避免误删别处仍要的键;`I18nCoreLocalesTests` 对键数有下限)。
- **L-8 缺失组件占位不再显示 id**,文案改为「找不到这个组件的内容，可能已被删除；可以在编辑态点「添加」重新放一个。」
- **L-9 迟到的 AI 会话回调在 Application 不在时丢弃**(`AiPage.AppGone`),用来稳定无头测试。
- **L-10 截图钩子默认接正式入口**(`LayoutEditor.SelectBlock` / `OpenAddDrawer`),反射兜底保留。

## 7. 待用户确认

1. **深色启动页编辑态的外观**(L-4):编辑条、块名标签、眼睛 / 垃圾桶按钮、「添加」抽屉在启动页上是保持白底(现状),还是改用 L0 已经做好的深色玻璃版?
2. **Q5 计数含默认隐藏块**(L-1):启动页内容区 5 块里默认只有新闻可见,按「含隐藏」算仍可编辑;若您的本意是「≥ 2 个**可见**块」,启动页右键会失去「自定义布局…」。
3. **「列表」= 每块占满一整行的网格**(不是 mode=stack),Linux 一直如此,集成保留。
4. **设置 → 布局的页面卡片 Linux 有 21 张**(lin-l4:入口表 24 − 2 个单块页 − 布局页本身),比 Windows 的 15 张多(插件市场、6 个资源目录、版本安装 / 管理、下载任务等)。
5. **「自动靠拢」固定为开**,用户以前关掉的也一律按开(lin-l12)。
6. **启动页编辑态英雄区只做 45% 不透明,不做去饱和**(Avalonia 11 无颜色矩阵效果,lin-l5 R35-L5-5)。
7. **缺失组件占位的新文案**(L-8)。
8. **锁形标签的 🔒 emoji** 在 Linux 上可能显示成方块,是否换成矢量锁(要改 `PageDefaultsV3Tests.cs:467` 的断言)。

## 8. 待真机(合并 5 包;本轮一次都没跑)

截图(明 / 暗各一张,常态 + 编辑态),在 Linux 产物目录下 `TipCL.App --smoke …`:
1. 下载页:`--shot download --edit --select list`(虚线框 1.5px 外扩 4、块名标签、眼睛划线按钮、右 / 下边缘把手 + 下边缘 tooltip)、`--dark` 同上;`--shot download --pop layout-drawer`(抽屉 316、空状态虚线框、组件行、「已添加」置灰、底部提示)。输出里应有「已模拟选中 / 已打开「添加」抽屉」,**不应再有 `SMOKE-SHOT NOTE`**(钩子已接正式入口)。
2. 编辑条:标题 · 列表|网格分段(ToggleButton 选中态白底粗体)· 提示 · 撤销 / 重做(置灰)· 分隔线 · 添加(角标数字)· 重置本页 · 完成 · ⋯;明暗切换即时跟随。
3. 拖动 / 改宽:淡色列底纹、落点框(浅底 + 主题色 3,2 虚线、圆角 12)、「N / 12 列」气泡(深底白字、圆角 8);右边缘按 MinCols 限宽。
4. 启动页 `--shot launch --edit`:英雄区 45% + 右上锁形标签(🔒 是否方块)、内容区白色半透明虚线框、**落点框白色半透明**、列底纹白 6%;只有一层压暗。
5. 联机 / 设置→游戏时长 / 设置→Java / 服务器 / 账号:新块名与默认布局(lin-l5 §7 的 1–7 条)。
6. 设置 → 布局:`--shot settings:layout --expand advanced`(明 / 暗)、页面卡片(含**老 canvas 存档的缩略图**按网格画)、方案卡片 AI 备份显示名、组件管理面板、锁 / 眼睛图标、琥珀提示。
7. canvas 老存档(lin-l3 §8):把某页改成 `"mode": "canvas"` 重启 → 按网格显示、块不少、先上后下先左后右;只进出编辑不改 → 文件哈希不变;拖一下再完成 → 变 `"mode": "grid"`。宽块的 MinCols 在推导时就生效(不再先 2 列再撑宽)。
8. 单块页(下载与网络、反馈与帮助):右键没有「自定义布局…」;1 个内置块 + 导入了 1 个组件的页同样没有(L-1)。
9. 缺失组件:手动删掉 `widgets[]` 里某个定义 → 占位里**不显示 id**。
10. 老用户回归:已存过 multiplayer / playtime / java / launch 的 `ui_layout_v2.json` 外观与改动前一致。
11. 不带任何新开关的 `--smoke`(含 `SMOKE_PAGE` 20 帧回归)与改动前逐字节相同。

## 9. 风险

- **ToggleButton 分段**:点击时 ToggleButton 会先自己翻转 `IsChecked`,再由 `SetLayoutKind → RefreshEditBar` 写回真值;若某条路径点了却没触发 `RefreshEditBar`,分段会短暂显示反的选中态。
- **主题接管后的尺寸**:`Layout.EditBar` 是固定 `Height=46`(原来是 `MinHeight=46`);编辑条内容若在超窄宽度下换行会被裁掉(目前一行 Grid,不换行)。`Layout.EditLabel` 自带 `Margin 8,-14`,集成里显式归零(标签已放在同样偏移的 `_bar` 里),真机核对标签位置不偏。
- **AiPage 修补**只在 Application 不在时生效,正常运行无影响;但它掩盖了「前一个用例留下了未结束的 AI 会话」这件事本身,哪个用例在发起会话没有继续追。
- **ClickSweep 新跳过词「文件夹」**会连带跳过其它名字含「文件夹」的按钮(如实例文件夹类),遍历覆盖面略降;断言只要求点到 ≥ 20 个,仍满足。
- 全量测试靠脱离桥的 `cmd /c` 跑;MCP 桥中途断开过一次(重连后继续),不影响结果。

## 10. 文件清单(本次提交)

`src/TipCL.App/Pages/Layout/{LayoutEditor.cs, LayoutEditor.V3.cs, LayoutEditChrome.cs, GridCanvasPanel.cs, GuideLayer.cs, LayoutShellHooks.cs, BlockHost.cs, Widgets/WidgetUI.cs}`、`src/TipCL.App/Pages/Settings/SettingsPage.Layout.cs`、`src/TipCL.App/Session/SmokeRunner.cs`、`src/TipCL.App/Pages/Ai/AiPage.cs`、`src/TipCL.Core/Resources/locales/{zh_CN,en}.json`、`tests/TipCL.Tests/App/{WidgetTests.cs, ClickSweepTests.cs}`、`progress/I18N-BASELINE.md`(工具刷新)、本报告。
未碰:`main`、`platform-*`、`r34/*`、其他工作区、`windos/` 子树、版本号;未 push。
