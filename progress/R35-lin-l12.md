# R35 · lin-l12 —— 布局 v3 WP-L12(§5.1 区块卡片编辑态 + §5.2 编辑条与「添加」抽屉)落地到 Linux 树

分支 `r35/lin-l12`(从 `r35/lin-base` 7039719 出发)· 工作区 `D:\项目\TipCL\_r35\lin-l12` · 只写了 R35-BRIEF §3 L12 行清单里的文件。
没有推倒 quick-v3 代理 C 的实现,只对照 GOAL 逐条补齐 / 修正。没有启动 TipCL.App。

## 1. 测试数字

| | 通过 | 失败 | 跳过 | 合计 |
|---|---|---|---|---|
| 开工基线(base 代码,`git stash` 后分 7 段跑) | 978 | 0 | 7 | 985 |
| 收尾(本包代码) | **1002** | **0** | 7 | 1009 |

- 增加的 24 条全部来自新建的 `tests/TipCL.Tests/Layout/LayoutEditorV3Tests.cs`(A 组纯规则 15 条,含 6 行 Theory;B 组无头 UI 9 条)。
- 现有的 `LayoutEditorTests` / `LayoutAutoPackTests` / `LayoutSmoothDragTests` **全部原样通过,一条断言都没删**。
- 跑法:MCP 桥单次调用约 2 分钟就超时,整套跑一次会被中途杀掉;后台进程(Start-Process / WMI)也活不下来。所以全量分成 7 段 `-Filter` 跑:Core;非 App 且非 Core;App.A|D|F;App.C;App.I|J|L;App.S;App.M|N|P|R|T|U|V|W。各段合计数与 `--list-tests` 总数(985 → 1009)对得上。
- 编译:0 错误,警告 4 → 3。

## 2. 逐条对照 GOAL

### §5.1 区块编辑态外观
| 项 | 落地点 | 说明 |
|---|---|---|
| 边框:常态 1.5px 灰虚线、外扩 4px;悬停加深;选中 2px 主题色实线;启动页白色半透明 | `LayoutEditor.cs` `ApplyEditOutline` + `OutlineBrush`(:3530) | 颜色读 `B.EditOutline` / `B.EditOutlineHover` / `B.EditOutlineOnDark`。找不到这些键时,先退回旧键 `B.InkFaint` / `B.InkMuted`,再退回效果图数值。画刷直接赋值,不再每次悬停都 `Dyn` 一个新订阅(C 的写法在反复悬停时会越挂越多订阅)。深色表面:常态白 35%、悬停白 70%、选中纯白(pages3 `.win.edit.dark`)。 |
| 块标题:浮动小标签(拖拽点 + 标题),自建组件后加灰字「· 组件」 | 卡片构造(`LayoutBlockTitles.LabelSuffix`)、`LayoutEditChrome.Chip` | C 已经做成悬停 / 选中才出现。本包把底色改读 `B.EditBarBg`,有 `Layout.EditLabel` ControlTheme 就套上;有 `Geo.Grip` 就换成六点拖拽图标。 |
| 自建块标题用 widgets[] 的 title,没有就用类型名;插件块用插件给的标题;**永远不显示 id** | `LayoutAddDrawer.cs` `LayoutBlockTitles.Resolve`(:137)、`LayoutEditor.cs` `SpecOf` / `SyntheticTitle`(:1014)、`Widgets/PluginBlocks.cs` `TitleOf`(:60) | 原来的两种兜底(`Title = id`、插件块拼「plugin · inner」)都去掉了。插件块现在取界面贡献的 `Label`,拿不到就显示「插件」;其它情况显示「内容」。任何分支都不会把 id 当标题(包括 spec 标题或插件标题本身恰好就是 id 的情况)。 |
| 块操作:内置块用「眼睛划线」= 隐藏;组件用垃圾桶 = 删除;放右上角,悬停 / 选中时出现 | 卡片构造 | C 已实现。本包让眼睛图标优先用 `Geo.EyeOff`,有 `Layout.IconAct` 就套上。 |
| 折叠按钮删除 | — | C 已删掉界面。`ToggleFoldAll` 方法保留(现有测试用反射调它),没有界面入口。 |
| 手柄(Q3):网格的右边缘 + 下边缘,悬停 / 选中时出现;拖右边缘显示「N / 列数」,按整列变化,受 MinCols 限制 | 新增下边缘把手 `_pillS`(:3128,按下改高度、双击恢复跟随内容)、`UpdateChrome`(:3262)、`ApplyGridGesture` 改用 `MinColsOf(primary)`(:2158) | C 保留了改高度的热区,但没画把手 → 本包补上下边缘把手,和右边缘同款,横放。「N / 12 列」提示 C 已有。**修正**:以前只看全局 2 列下限,现在同时受该块自己 `BlockSpec.MinCols` 的限制(有单测)。 |
| 拖动:落点框主题色浅底 + 虚线;拖动时淡色列底纹 | 新建 `GuideLayer.cs` `EditColumnShade`(:182),放在 `_stage` 第 0 层(`LayoutEditor.cs:298`),只在手势中显示;`ShowColumnShade` | 底纹是每列一条 8px 圆角的淡色竖条,读 `B.EditColShade`(找不到用 rgba(0,0,0,.035));深色表面用白 6%。落点框在 `GridCanvasPanel`(归 L3)里本来就是主题色浅底 + 4/3 虚线,本包没动它。 |

### §5.2 编辑条与「添加」抽屉
| 项 | 落地点 | 说明 |
|---|---|---|
| 编辑条位置 / 内容 | C 已实现(`LayoutEditor.V3.cs` `BuildEditBar`) | 底色改读 `B.EditBarBg`,有 `Layout.EditBar` ControlTheme 就套上(`LayoutEditor.cs:258`);撤销 / 重做图标优先用 `Geo.Undo` / `Geo.Redo`(`V3.cs:142`)。 |
| 撤销 / 重做随历史栈置灰 | `RefreshToolbar` | C 已实现,本包补了单测。 |
| ⋯ 菜单:导出本页… / 导入到本页… / 整套布局与方案… | `OpenMoreMenu` | **删掉了 C 加的「松手后自动靠拢」一项。** |
| 删掉「吸附 N」「自动靠拢」「全部折叠」;吸附档用默认值,自动靠拢固定为开 | `LayoutEditor.cs:881` `AutoPackOn = true`,替换掉原来读 `LayoutEditPrefs.AutoPack` 的 3 处;删除 `ToggleAutoPack` | `LayoutEditPrefs.AutoPack` 本身没改(归 L4),编辑器不再读它,界面上也没有入口了。 |
| 抽屉从右侧滑出、宽 316 | `V3.cs` `BuildDrawer` / `ShowDrawer`(:533) | `TranslateTransform` + `DoubleTransition` 260ms 缓出;收起时先滑出,播完再隐藏;退出编辑时立刻收起。底色读 `B.EditBarBg`,有 `Layout.Drawer` 就套上。 |
| 上半「已隐藏的内容」+ 虚线空状态框 | `EmptyBox`(:617) | 原来是实线框,改成 1px、4/3 虚线、圆角 12。 |
| 下半「组件」:不可重复的类型加过就「已添加」置灰;网页 / 按钮看全局开关 | 纯规则放进新文件 `LayoutAddDrawer.cs`(`Types` / `AllowsMultiple` / `IsAdded` / `DescriptionOf`) | 可重复的类型按效果图 `multi:true`:便签、分隔线、网页、按钮。底部「默认关闭,去设置开启」提示只在开关关着时显示。 |
| 新块加到末尾,滚过去并闪主题色光晕 | `FlashBlock`(:838)+ `LayoutCard.Flash`(:3544) | 「添加」和「显示」(找回)都会触发:先滚到块所在位置,再播 0.9s 光晕(0 0 0 4px → 0)。C 原来只是滚到页面末尾。 |
| Esc 先关抽屉再退出;Delete:内置 = 隐藏、组件 = 删除 | 键盘处理 / `DeleteSelection` | C 已实现,本包补了 Esc 的单测。 |
| 启动页(pageId=="launch")深色玻璃配色 | `IsDarkSurface`(:888)→ 虚线框 / 列底纹 | 按 pages3:深色表面上编辑条和标签仍是白底,只有虚线框和底纹换成白色半透明。 |

## 3. 文件清单
- 改:`src/TipCL.App/Pages/Layout/LayoutEditor.cs`、`LayoutEditor.V3.cs`、`LayoutEditChrome.cs`、`GuideLayer.cs`、`Widgets/PluginBlocks.cs`
- 新建:`src/TipCL.App/Pages/Layout/LayoutAddDrawer.cs`、`tests/TipCL.Tests/Layout/LayoutEditorV3Tests.cs`、本报告
- 一并提交主 agent 改的 `tests/TipCL.Tests/App/ClickSweepTests.cs`(SideEffectWords 里加了「主页 / GitHub / …」,防止测试点开浏览器),内容原样未动。
- 没碰:`LayoutEngine.cs` 公开签名、`BlockHost.cs`、`LayoutShellHooks` 的签名、`GridCanvasPanel.cs`、`LayoutEditPrefs.cs`、locales、Themes。

## 4. 新增 / 废弃词表 key(中文 → 英文)
新增:无。新文案只复用了已有的 key:「插件」「内容」「· 组件」「组件」,以及抽屉说明文案(这些 C 已经在用)。为了不让 i18n 守卫测试变红,下边缘把手**没加** tooltip。
废弃(界面上已不再用到):
- 松手后自动靠拢 → Snap together on release
- 开:松手后区块自动向上靠拢、与相邻区块互换位置、靠近时磁性吸附;关:区块停在你放下的位置 → (原英文)
- 自动靠拢已开启 / 自动靠拢已关闭 / 区块松手后会自动靠拢相邻区块 / 区块会停在你放下的位置 → (原英文)

建议合并时补一条(可选):「拖动改高度，双击恢复自动高度」→ "Drag to change height, double-click to reset",给下边缘把手做 tooltip。

## 5. 跨包接线需求(合并时处理)
1. **L0 资源键**:全部用 `TryGetResource` 加兜底读取:`B.EditOutline`、`B.EditOutlineHover`、`B.EditOutlineOnDark`、`B.EditColShade`、`B.EditBarBg`、`Geo.Grip/EyeOff/Undo/Redo`;ControlTheme `Layout.EditBar/EditLabel/IconAct/Drawer`(只有 `TargetType` 匹配时才套)。另外深色底纹查的是 `B.EditColShadeOnDark`(清单外,找不到就用白 6%)。**如果 L0 选的是 Style Classes 而不是 ControlTheme**,本包这边套不上,合并时需要在 `LayoutEditChrome.TryApplyTheme` 里补上加类名。`Layout.SegItem` 没接:分段按钮的选中态目前靠换 Btn.Normal/Ghost 两套主题表达。
2. **网页 / 按钮开关**:`LayoutAddDrawer.DangerWidgetsAllowed` 默认就读 `LayoutProfiles.DangerousWidgetsEnabled`,Linux 不需要额外接线。
3. **L3 / GridCanvasPanel**:落点框颜色仍是画布自己按主题色算的(没读 `B.EditDropFill`),深色表面上也还是主题色。要按 pages3 改成白 12% 浅底 + 白边,得在 `GridCanvasPanel.DrawPlaceholders` 里改(归 L3)。
4. **WidgetUI.MissingWidget** 的说明文字里仍带块 id(「布局文档里找不到 {0} 的定义」)。按 §5.1 应该去掉,但 `App/WidgetTests.cs:533` 断言了原文(不在本包清单里),所以没改。

## 6. 决定(合并时可编号为 D-8x)
- 自动靠拢固定为开:编辑器内部常量 `AutoPackOn = true`,不再读 `LayoutEditPrefs.AutoPack`。用户以前关掉过的,也一律按开处理(GOAL「固定为开」)。
- 能重复添加的组件类型以效果图为准:便签 / 分隔线 / 网页 / 按钮(Windows r34/l12 只放了便签和分隔线)。
- 下边缘把手和右边缘把手同款横放,只在网格模式出现;列表排法下也能改高度(右边缘把手照旧锁住)。

## 7. 待真机
- `--shot download --edit --select list`:看边框、标签、眼睛 / 垃圾桶、右边缘和下边缘把手。
- 「添加」抽屉滑入 / 滑出的动画;新块闪光晕;拖动时的淡色列底纹。
- 启动页编辑态:白色半透明虚线框。
- 明 / 暗两套主题各看一遍(颜色键回退链路)。

## 8. 风险
- 抽屉关闭时 `IsDrawerOpen` 立刻变 false,但滑出的 260ms 里抽屉还看得见、也能点。
- 光晕用 `DispatcherTimer`(30ms 一帧,共 0.9s)改卡片的 `BoxShadow`;卡片 style=card 时如果别处也设了 BoxShadow,播完会被清空(目前代码里没有别处设它)。
- 全量测试是分段跑的,没在一个 testhost 进程里整体跑过一遍;涉及静态状态的用例,我的新用例都会自己还原。
