# LAYOUT-V3-D — Linux「设置 → 布局」v3 对等落地(代理 D / lin-settings)

日期:2026-11-20 · 范围:任务书 `tmp/v3_spec.md` §1.B 全部(Linux 移植版 Avalonia 11 / net8)
效果图:`tmp/layout3_noimg.html` 的 `secB` 段;现状:`tmp/layout3_shots/now_3.png` / `now_4.png`

> 本机不能编译 / 跑测试 / 用 git。所有调用的 API 都在仓库里 grep 过签名(见文末「API 核对」),但**需要在 Linux 上真编译一次**;
> 另外 `LayoutShellHooks.CanEditPage / BeginEditPage` 由代理 C 提供,C 没补之前 Linux 编译不过(见「风险 1」)。

## 1. 改了哪些文件

| 文件 | 改法 | 说明 |
|---|---|---|
| `src/TipCL.App/Pages/Settings/SettingsPage.Layout.cs` | 整文件重排(本代理独占;旧版 982 行 → 1376 行) | 六块:页头 / 外观 / 导航 / 页面 / 方案 / 高级。方案 CRUD、导入导出、分栏复位、`WriteLayout` 等保留原逻辑,只改呈现 |
| `src/TipCL.App/Pages/Layout/ShellTabs.cs` | 2 处小 diff | 新增 `ReorderTabs`(:256);`AnyTitlebarVisible` 去掉桥接(:202) |
| `src/TipCL.App/Pages/Layout/LayoutProfiles.cs` | 1 处追加 | `KeepCurrentAsPrevious`(:228)、`Describe`(:249)、`IsAiBackup`(:270)、`DangerousWidgetsEnabled`(:320) |
| `src/TipCL.App/Pages/Layout/LayoutJsonEditor.cs` | 1 处追加 | `HasErrors`(:180)、`Summary`(:189)、`Describe`(:212);校验/应用逻辑一字未改 |
| `src/TipCL.App/Pages/Layout/LayoutJsonWindow.cs` | **新建** | 独立 JSON 编辑窗口(非模态、单例) |
| `src/TipCL.App/Controls/PageLayoutHost.cs` | **未改** | 13 条 `SplitCatalog` 事实表与双击复位原样保留;设置页只是不再逐条展示 |

其它页面文件(SettingsPage.cs、MainWindow、TitleBar 等)**一律未动**。

## 2. 逐条对照 §1.B(文件:行 = SettingsPage.Layout.cs,除非另注)

| §1.B | 落地 | 位置 |
|---|---|---|
| 页头「布局」+「调整导航、各页面的排列…随时可以恢复默认。」;删掉 ui_layout_v2.json 那句 | ✅ | :95-96 |
| 1 外观:「区块间距」·「所有可以自定义的页面一起生效」· 三档**分段按钮**(紧凑/标准/宽松,替换下拉);改了缩略图跟着变 | ✅ `Seg.Rail` + `Seg.Item` ToggleButton(与下载页分段同一套主题);点击写 `shell.density` 后重建视图 ⇒ 缩略图块间距 = 档位 × 0.3 | `LayoutAppearanceRows` :209;`DensityGap` :263;`PageThumb` :678 |
| 2 导航:「顶部页签」· 说明 ·「恢复默认」;**拖动排序 chip** + 眼睛隐藏;「设置」不能隐藏;改完标题栏马上变 | ✅ chip = 抓手 + 名称 + 眼睛;按下捕获指针、移动跟手(TranslateTransform)、松手按落点算插入位置一次写回 `ShellTabs.ReorderTabs`;隐藏的 chip 半透明 + 删除线排在末尾;「设置」眼睛禁用并给 ToolTip;每次写回 → `RefreshLayoutView` → `RefreshShell()` → `MainWindow.ApplyShellLayout()` | `LayoutNavSection` :274;`TabChip` :338;`WireChipDrag` :409;`ChipDropIndex`;`MoveTabTo` :490;`ToggleTab` :510;`ShellTabs.ReorderTabs` ShellTabs.cs:256 |
| 2 标题栏:只留 AI / 主题 / 账号;说明「隐藏 AI 按钮后仍可按 Ctrl+K 打开」;**去掉「桥接」**与「标题栏:xxx」「只隐藏/显示,不改形态」 | ✅ 三个勾选 pill(AI 按钮 / 主题切换 / 账号名称),勾完立刻 `RefreshShell()` | `TitlebarOption` :297;ShellTabs.cs:202 |
| 3 页面:缩略图卡片网格;「默认 / 已自定义」;「编辑」(跳到该页并进编辑态);右上「全部恢复默认」;单块页面说明;默认只显示常用几页 +「显示全部 N 个页面」 | ✅ 3 列 UniformGrid;已自定义 = 存档里有该页(按存档块画:隐藏不画、网格按列换算 6 列、老画布存档按宽度换算整数列、自建/插件块用强调色);默认页用示意形状表;卡片副标题「网格 · N 个组件 · 隐藏了 N 项 / 调整了顺序 / 和出厂时一样」;「编辑」→ `LayoutShellHooks.BeginEditPage(pageId)`;已自定义的卡片多一个「恢复默认」;单块页(`CanEditPage == false`)不出卡片,在底部说明里点名「「下载与网络」…只有一块内容,不提供编辑。」;常用 6 页(启动/下载/账号/通用/游戏时长/联机)在前,其余点「显示全部 N 个页面」展开 / 「收起」 | `LayoutPagesSection` :553;`PageCard` :606;`CanEditLayoutPage` :652;`BeginLayoutPageEdit` :665;`ThumbFromPage` :732;`ResetPageLayout` :795;`ResetAllPagesAsync` :843 |
| 4 方案:卡片;正在使用描边;「AI 备份」单独标出;「导入…」「导出整套布局…」「存为新方案」;卡片菜单「重命名 / 复制一份 / 删除」 | ✅ 4 列卡片(标签:正在使用 / AI 备份 / 方案;名称;「N 个页面自定义 · 标准间距」;「应用」/「当前方案」+「⋯」菜单);激活卡 1.5px 强调色描边;没有激活方案(直接编辑态)时补一张描边的「当前布局 · 还没存成方案」卡。**AI 备份**:Linux 没有 AI 排版,按 `meta.ai_backup=true` / `meta.source="ai"` / 方案名前缀「AI 备份」「AI 改布局前」识别,都没有就不显示 | `LayoutProfilesSection` :867;`ProfileCard` :929;`OpenProfileMenu` :966;`LayoutProfiles.IsAiBackup` LayoutProfiles.cs:270、`Describe` :249 |
| 5 高级(默认折叠,「分栏、网页与按钮组件、布局文件」+「一般不需要动」) | ✅ 折叠态记在 `UiPrefs` expanded 键 `settings.layout.advanced`(默认收起);新增 `OpenLayoutAdvanced()` 供编辑态抽屉跳转 | `LayoutAdvancedSection` :1215;`OpenLayoutAdvanced` :142 |
| 5 分栏:13 行表 → 一行「已调整 N 处:<名字>。在页面上拖…双击复位。」+「全部复位」;N=0 写「都是默认宽度」并禁用 | ✅ 名字用用户叫法(实例库文件夹栏 / 设置左侧导航 / 版本管理左栏 / AI 对话历史栏 / 反馈页右栏 / 账号头像卡) | `LayoutSplitRow` :1250;`SplitLabel` :1281;`ResetAllSplits` :1266 |
| 5 网页和按钮组件:开关 + 说明 + 警示「导入的布局里如果带网页或按钮组件,仍然默认关闭,需要你逐个打开。」 | ✅ 全局开关 = 本机偏好 `LayoutProfiles.DangerousWidgetsEnabled`(config.json 顶层 `layout_dangerous_widgets`,不进布局文件、不随方案/导入走);开时显示警示条 | `LayoutDangerRow` :1297;LayoutProfiles.cs:320 |
| 5 布局文件:「用 JSON 编辑整套布局…」·「打开所在文件夹」·「在编辑器中打开…」(独立窗口) | ✅ 文件夹 = `LayoutStoreV2.DirPath`(交给 `LibraryPage.OpenInFileManager`);JSON 用 `LayoutJsonWindow`:重新载入 / 校验(给出「N 个页签 · N 个页面自定义 · …」预览)/ 应用并保存(先确认预览,再 `LayoutJsonEditor.Apply`,有硬错误不落盘)/ 关闭(有未保存修改时确认) | `LayoutFileRow` :1327;`OpenLayoutFolder` :1345;LayoutJsonWindow.cs:47 / :185 / :201 |
| 6 内部说法全部去掉(文件名、桥接、#编号、(L6)) | ✅ 页面上不再出现 ui_layout_v2.json、「桥接」、`#n pageId / splitId`、「(L6)」、「标题栏:」 | 全文件 |
| 7 两组导出改名:设置这里叫「导出整套布局」 | ✅ 按钮「导出整套布局…」,对话框/保存框标题「导出整套布局」 | `ExportLayoutAsync` :1073 |
| 8 两张联动:改间距 / 改页签 → 编辑态与标题栏跟着变;「编辑」直接进那一页编辑态 | ✅ 写盘走 `LayoutStoreV2.Save`(各页 BlockHost 经 `LayoutChanged` 跟上)+ `RefreshShell`;「编辑」走约定入口 | 同上 |

## 3. 覆盖了文件头里的旧「降级 / 恢复」决定(以效果图为准)

| 旧决定 | v3 处理 | 原因 |
|---|---|---|
| T-03(R32-LINSPLIT 恢复成 Windows 表驱动):13 条分栏站点逐条现状 + 每条「恢复默认」+「全部复位」 | 改成一行汇总 +「全部复位」 | 效果图 / §1.B-5 明确要求;13 条里 7 条是「没有左右分栏」,对用户是噪音。`SplitCatalog` 事实表(PageLayoutHost.cs)**不动**,LayoutSplitTests 照旧钉它;单处复位改为在页面上双击分隔线(PageLayoutHost 原有行为) |
| T-04:Linux 设置页补了「自建区块」部件选择器(枚举 SafeTypes → 加到选定页面) | 从设置页移除 | v3 把「加组件」放进编辑态「添加」抽屉、删组件用垃圾桶(§1.A-5/6,代理 C);效果图设置页没有这一节。`LayoutWidgetOps` 数据层不动 |
| T-05:「标题栏:桥接」开关保留但禁用 + 写明原因 | 直接去掉 | §1.B-2 要求去掉「桥接」;Linux 本来就没有桥接控件。`titlebar.bridge` 字段照旧读写(存档兼容);`AnyTitlebarVisible` 不再把它算进去(否则三项全关时分隔线永远收不起) |
| 常驻 JSON 编辑卡(校验 / 应用 / 刷新) | 改成独立窗口 | §1.B-5 |
| 「单块页(下载与网络)也能进编辑态」 | 单块页不给编辑入口 | §1.A-8 / §1.B-3 |

## 4. 没做 / 降级

1. **「编辑」的实现在代理 C**:本文件只按约定签名调用 `LayoutShellHooks.CanEditPage / BeginEditPage`(两处都包了 try/catch;判断抛错时按入口表 `SingleBlock` 兜底)。要求与实现提示已写进 `tmp/v3_handoff.md`「D → C:Linux 设置」。
2. **拖动排序没有「其它 chip 实时让位」动画**:被拖的 chip 跟手移动,松手后按落点一次重排(效果图里其它 chip 会滑开)。Avalonia 下做逐帧让位要自己管 WrapPanel 布局,风险大、收益小,先降级。
3. **方案卡片的保存时间**:效果图副标题有「11-18 保存」;Linux 的方案 `LayoutBody` 没有保存时间字段(`saved_at` 只在整份文档上),所以副标题只写「N 个页面自定义 · X 间距」,不编造时间。
4. **删除正在使用的方案**:效果图 toast「正在使用的方案不能删除」;为「保留既有功能语义」,Linux 仍允许删(先确认,删后回到直接编辑态),与旧版一致。
5. **单页「恢复默认」对自建组件**:设置页拿不到各页的出厂块表,所以恢复默认 = 删掉该页存档;页上若有自建 / 插件块,只保留它们的引用(几何清掉),它们会排在内置块前面。要精确按出厂顺序重置请用编辑态「重置本页」。
6. 「显示全部 N 个页面」的 N 是数据驱动的(Linux 入口表可编辑的页数,含 6 个资源目录页等),不是效果图里的固定 10。

## 5. 新字符串

全部套了 `I18nCore.Tr(...)`(含无障碍名);识别 AI 备份用的 3 个中文前缀带 `// i18n:ignore`(不是界面文案)。清单 ~150 条见 `tmp/v3_strings/D.txt`(中文 key + 建议英文;与 B 同 key 的英文已对齐)。
i18n 基线:`SettingsPage.Layout.cs` 旧版 134 处 `Tr(`,新版 178 处;新窗口 35 处 ⇒ wrapped 只增;旧版里十几条未接线的拼接文案(如 `"已复位 " + count + " 处"`、`"布局方案「" + name + "」已应用"`)都改成了 `Py.Fmt(I18nCore.Tr(...))` ⇒ unwrapped 只减。

## 6. 风险

1. **编译依赖 C**:`LayoutShellHooks.CanEditPage(string)`(返回 bool)/ `BeginEditPage(string)`(void 或 bool)在 C 补上之前不存在 ⇒ Linux 编译失败。C 已在 `LayoutEditor.V3.cs` 注释里提到 CanEditPage,但截至本报告 LayoutShellHooks.cs 里还没有。
2. 未真编译:Avalonia API 都 grep 过同仓库既有用法,但下面几处是本仓**首次**使用,真机编译时请留意:`new ColumnDefinition(1.0, GridUnitType.Star)` / `new RowDefinition(GridLength.Auto)`、`new RotateTransform(180)`、`Window.Closing` 的 `e.Cancel`、`TextBox.AcceptsReturn`、typed discard `out LayoutV2? _`。
3. `DangerousWidgetsEnabled` 写 config.json 顶层键:若设置页「全量 save_settings」会重写整份 config,这个键可能被冲掉(没看到证据,只是提醒);被冲掉的后果只是开关回到「关」。
4. 设置页的 BlockHost(`settings:layout`)区块 id 从 8 个变成 6 个(header/appearance/shell/blocks/profiles/advanced):用户若曾在该页自定义过区块顺序,旧 id(splits/widgets/json)由引擎忽略,新 id `advanced` 追加在后。
5. chip 的 `PointerPressed` 置了 `Handled`:如果用户正处在「设置 · 布局」这一页自己的编辑态,按 chip 不会触发编辑器的拖块(本来也不该)。

## 7. 预计受影响的 Linux 测试(本代理不改测试,交 E)

| 测试 | 原因 |
|---|---|
| `LayoutPagesSplitTests.SettingsLayoutViewGroupsAreBlocks`(tests/TipCL.Tests/App/LayoutPagesSplitTests.cs:246,断言 :262) | 区块 id 由 `header, appearance, shell, blocks, splits, widgets, profiles, json` 变为 `header, appearance, shell, blocks, profiles, advanced` |
| `SettingsLayoutAddBlockHeadlessTests.LayoutView_ListsAllFiveSafeWidgetTypes`(LayoutMoreTests.cs:796) | 设置页不再有「添加部件:xx(type)」按钮 |
| `SettingsLayoutAddBlockHeadlessTests.ClickingAddNote_PersistsWidgetAndBlock_AndRendersIt_AlsoAfterReopen`(LayoutMoreTests.cs:819) | 同上 |
| `AddBlockToNewlyWiredPageHeadlessTests.AddNoteToJavaPage_RendersOnThatPage`(LayoutMoreTests.cs:705) | 依赖「自建区块加到页面」下拉(无障碍名「自建区块加到页面」)与「添加部件:」按钮;建议改为走 C 的「添加」抽屉,或直接测 `LayoutWidgetOps` |
| `ShellTabsTests.SetTabs_Reorders_AndTitlebarTogglesPersist`(LayoutMoreTests.cs:197) | **预计不受影响**(只断言 `ShowBridge` 等读写);若有用例断言 `AnyTitlebarVisible` 在只开桥接时为 true 会红——仓库里没找到这种用例 |
| `I18nBaselineTests.UnwiredCjkLiteralCountMustNotGrowSinceBaseline` | 预计通过(unwrapped 只减、wrapped 只增);E 可顺手刷新基线 |

没有测试引用被删的私有方法(`LayoutSplitRows / LayoutJsonCard / OpenBlockEditor …`,已 grep)。`SettingsPage.OpenLayoutView()` 签名不变。

## 8. Linux 真机回归步骤

1. 编译(`dotnet build`),确认 C 已提供 `CanEditPage / BeginEditPage`。
2. 设置 → 布局:页头只有「布局」+ 一句说明,看不到 ui_layout_v2.json、桥接、#编号、(L6)。
3. 外观:点「紧凑 / 宽松」,分段高亮跟着走,toast「间距:紧凑」;下面页面缩略图块间距变化;切到「启动」页看区块间距确实变了。
4. 导航:把「插件」chip 拖到最前,松手 → 标题栏页签顺序立刻变;点「下载」的眼睛 → chip 变灰带删除线、标题栏立刻少一个页签;再点眼睛恢复;「设置」的眼睛是灰的、悬停有原因;「恢复默认」回到 启动/实例库/下载/插件/设置。
5. 标题栏:取消「AI 按钮」→ 标题栏 AI 按钮消失,按 Ctrl+K 仍能打开 AI;三项全取消 → 标题栏那条分隔线也收起。
6. 页面:默认 6 张卡;「显示全部 N 个页面」展开 / 「收起」;底部说明点名「下载与网络」等单块页且没有它们的卡;点「通用」卡的「编辑」→ 跳到 设置·通用 并直接进编辑态;在编辑态改一下再回来,卡片变「已自定义」、缩略图按新排列画、出现「恢复默认」;点它 → 回「默认」;「全部恢复默认」→ 确认框 → toast 提示原布局已存为「上一次布局」,方案区多出这张卡。
7. 方案:「存为新方案」→ 输入名 → 新卡描边 + 「正在使用」;另一张卡「应用」→ toast「已切换到…」,描边换过去,各页重排;「⋯」菜单 重命名 / 复制一份 / 删除(删除有确认);把方案改名为「AI 备份 测试」→ 标签显示「AI 备份」;「导入…」选一份整套布局 → 选「应用为当前布局 / 存为新方案」;「导出整套布局…」两种格式都能存出文件。
8. 高级:默认收起,点开后重进设置页仍保持展开;分栏:在实例库拖一下分隔条,回来看到「已调整 1 处:实例库文件夹栏。…」,「全部复位」后变「都是默认宽度」且按钮禁用;网页和按钮组件:打开 → 出现黄色警示条,重启程序后仍是开;布局文件:「打开所在文件夹」能打开放 ui_layout_v2.json 的目录;「在编辑器中打开…」弹出独立窗口,改坏 JSON 点「校验」看到红字字段,改好后「应用并保存」先弹预览确认,保存后设置页刷新;有未保存修改时关窗会确认;再次点「在编辑器中打开…」只会把已开的窗口提到前面。
9. 切英文界面:新文案(待 E 入词表后)应全部变英文。

## 9. API 核对(grep 过的既有用法)

`Seg.Rail / Seg.Item`(DownloadPage.cs)、`UiClass.Tag/Check/Switch/IconBtn/Card/G/Add/Dyn`(UiHelpers.cs)、`IconLib.Make("Grip"/"ChevronDown")`、`Res.Bind`、`Geometry.Parse + ScaleTransform`(IconLib.cs)、`TranslateTransform`(LayoutMotion.cs)、`Visual.ZIndex`(PageLayoutHost.cs D-01)、`GetCurrentPoint().Properties.IsLeftButtonPressed`(TitleBar.axaml.cs)、`TextDecorations.Strikethrough`(PlanCard.cs)、`UniformGrid`(PluginsPage.cs)、`WrapPanel`、`ContextMenu.Open + PlacementMode`(LayoutShellHooks.cs)、`Dlg.Confirm/Prompt/Choose/SaveFile/PickFile`(Dlg.cs)、`Window.Show(owner)`(Dlg.OpenPanel)、`UiPrefs.GetExpanded/SetExpanded/Save`、`Config.CONFIG.Get/Set/Save`(LauncherConfig.cs)、`LibraryPage.OpenInFileManager`、`SplitRatioStore.IsStored/Clear`、`LayoutEngine.CustomPrefix/PluginPrefix`、`LayoutWidgetOps.AppendBlock`、`LayoutStoreV2.DirPath`。
