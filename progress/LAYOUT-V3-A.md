# LAYOUT-V3-A · Windows 编辑态（win-edit）

范围：任务书 §1.A 全部 10 条（Windows WPF / net48，LangVersion 12，Nullable=annotations）。
本机不能编译 / 跑测试；所有调用的 API 都先 grep 过签名。改动只在 A 名下文件：

| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Pages/Layout/LayoutEditorV3.cs` | **新建**（≈1330 行）：`LayoutEditor` 与 `LayoutCard` 的 partial（顶部工具条 / 列表·网格 / ⋯菜单 / 添加抽屉 / 块名·眼睛·垃圾桶·拖宽手柄 / 导出本页·导入到本页）+ `LayoutIcons` + `LayoutCanvasCompat` |
| `windos/src/TipCL/Pages/Layout/LayoutEditor.cs` | 小 diff：类改 partial、构造里换掉底部托盘/底部工具条、根网格改「工具条在上」、CanEdit≥2、进编辑不再折网格、canvas 读取按网格、卡片外观 |
| `windos/src/TipCL/Pages/Layout/LayoutShellHooks.cs` | 新增 `CanEditPage` / `BeginEditPage`，`OpenLayoutSettings(bool advanced)` 改 internal；右键对单块页不出菜单 |
| `windos/src/TipCL/Pages/BlockHost.cs` | 新增 `CanEdit`；右键菜单只给可编辑页，打开前按状态重建 |
| `windos/src/TipCL/Pages/Layout/GridCanvasPanel.cs` | 网格模式编辑态不再画主题色栅格底纹 |

未改：Core（schema/模型/v1 兼容）、locales、tests、docs、B 的文件（含 B 建在本目录下的 `LayoutSettingsPrefs.cs`，只读引用）、`LayoutPageState` 等公开成员名。

## §1.A 逐条落地

1. **边框灰色虚线，仅选中用主题色** —— `LayoutEditorV3.cs:1083` `LayoutCard.BuildV3Chrome`（`Rectangle` 外框，外扩 4px，`StrokeDashArray 4,3`，`B.Line`/悬停 `B.InkFaint`）+ `:1199` `ApplyV3Chrome`（选中 = `B.Accent` 2px 实线）；`LayoutEditor.cs:3390` `ApplyLook` 不再整卡主题色描边 + 蒙版，编辑态外观 = 常态外观 + 透明底（可按住拖）。`GridCanvasPanel.cs:594` 去掉网格底纹。
2. **块名悬停才出现、自建组件显示自身标题** —— `LayoutEditorV3.cs:1083` 左上浮标签（⋮⋮ + 标题 +「· 组件」），悬停/选中才显示；标题来自 `:408` `DisplayTitle`（custom: 查 `WidgetRegistry.FindWidgetById` → `WidgetUI.TitleOf`，查不到显示「组件」，绝不显示 `w_note1`），每次进编辑态重取（`LayoutEditor.cs:3254` `SetEdit` → `RefreshV3Label`）。旧操作条 `_bar` 永远折叠。
3. **顶部单条工具条** —— `LayoutEditorV3.cs:123` `BuildV3Chrome`：「编辑「<页名>」页」·「列表 | 网格」分段（`:214`）· 撤销/重做 Lucide 图标（tooltip 带快捷键）· 竖线 ·「添加」+ 隐藏数徽标 ·「重置本页」·「完成」(Primary) ·「⋯」；`:457` ⋯ 菜单：导出本页… / 导入到本页… / ─ / 整套布局与方案…（`:499` 先完成编辑再 `LayoutShellHooks.OpenLayoutSettings(false)` → `SettingsPage.OpenLayoutView()`）。`LayoutEditor.cs:235` 构造：根网格 `Auto,*`，工具条第 0 行、内容第 1 行（`Show*Host` 的 `Grid.SetRow` 同步改 1），常态整行折叠，高度与改动前一致。`:1006` `RefreshToolbar` 尾部调 `RefreshV3Bar`（`:252`）刷新分段、提示、撤销可用性、隐藏数。
   - 导出本页（`:525`）只写本页（顺序/显隐，v1 同格式，文件名里的 `:` 等非法字符替换掉）；导入到本页（`:549`）文件里有本页用本页、只有一页就套到本页，其它页不动，可撤销。
4. **去掉吸附 N；整数列网格；右边缘改宽** —— 吸附/自动靠拢/全部折叠按钮不再创建（字段保留为 null，旧方法 null 安全）；角上小方块手柄网格模式不再显示（`LayoutEditor.cs:3312`）；新增右边缘拖宽手柄 `LayoutEditorV3.cs:1083`（10×36 圆角条，悬停/选中出现，`BeginBoxResize(…, "e")` 走既有整数列吸附手势）。整条右/下边的透明拖拽条保留（下边 = 固定像素高度，双击复位，属既有能力）。
5. **隐藏托盘并入「添加」抽屉** —— `LayoutEditorV3.cs:598` `BuildV3Drawer`（右侧 316px 覆盖层，标题「添加到「<页名>」页」，关闭 = × / Esc / 再点添加）；`:744` `RefreshV3Drawer`：「已隐藏的内容」（显示 → 恢复 + toast；空时「没有隐藏的内容」）、「组件」（便签/时钟/快捷启动/游戏时长/分隔线，时钟等单例已放则「已添加」；便签/快捷启动行内输入框；以前建过本页没放的组件可直接加）；底部 `:649` 提示「网页、按钮组件默认关闭，可在 设置 → 布局 → 高级 里开启。」中间那段是链接 → `OpenLayoutSettings(true)` → `SettingsPage.OpenLayoutAdvanced()`。`LayoutEditor.cs:1184` `RefreshTray` 改为只刷抽屉，底部整条托盘不再出现。按 B 的约定：`LayoutSettingsPrefs.DangerWidgetsEnabled` 为真才列网页/按钮，抽屉打开期间订阅 `Changed`。
6. **眼睛隐藏 / 垃圾桶删除分开** —— 右上角动作按钮：内置内容 = EyeOff（`HideBlock`，`LayoutEditor.cs:1267` 加 toast「已隐藏「X」· 在「添加」里可以找回」）；custom:/plugin: = 垃圾桶（`LayoutEditorV3.cs:433` `DeleteCustomBlock`：压撤销栈 → 只删本页引用、组件定义保留 → toast「已删除「X」· Ctrl+Z 撤销」）。Delete 键删选中自建块在列表模式也可用（`LayoutEditor.cs:2876`）。
7. **只留列表/网格、老画布按网格呈现** —— `LayoutEditorV3.cs:313` `SetV3Mode`（可撤销、即时落盘；列表→网格 = 既有 `PrepareEditableGrid` 每块占满一行；网格→列表 `:350` 按 (行,列) 排序、span 归 1、cols 回 12）。`LayoutEditor.cs:497` 进编辑不再把 stack 偷偷折成网格（列表页按列表编辑，走既有 stack 拖拽换序；`:1490` 换序也进撤销栈，按下即选中）。`LayoutEditor.cs:609` 装载时 `LayoutCanvasCompat.AsGrid`（`LayoutEditorV3.cs:1287`，纯函数：上沿差 <5% 同行，宽按 12 列取整、≥2 列，隐藏块放末行整行）；存档本身不动，任何落盘才写 grid。Core 的 canvas 字段/模型/v1 兼容未删。重置本页（`:1330`）不再折网格。
8. **单块页不提供编辑入口** —— `LayoutEditor.cs:326` `CanEdit` = specs ≥ 2，`EnterEdit` 对单块页直接 return；`BlockHost.cs:103` 右键菜单只给可编辑页；`LayoutShellHooks.cs:94` 全局右键对单块页不出菜单；`LayoutShellHooks.cs:332` `CanEditPage`（设置页卡片用）。
9. **右键空白处「自定义布局…」** —— `BlockHost.cs:162` `RebuildContextMenu`：常态「自定义布局...」，编辑态「完成编辑 / 重置本页」；全局兜底菜单同口径（编辑中不再重复出「自定义布局...」）。沿用既有 key「自定义布局...」（已有译文）。
10. **提示文案** —— 工具条提示短句随模式变（网格「拖动换位置 · 拖右边缘改宽度」/ 列表「拖动调整上下顺序」），完整句「拖动块调整顺序；网格模式下拖块的右边缘改宽度；悬停能看到块名和隐藏 / 删除按钮。」挂在提示的 ToolTip 上（`LayoutEditorV3.cs:123`）。

## 跨代理入口（已写进 `tmp/v3_handoff.md`「A → B」）
- `public static bool LayoutShellHooks.CanEditPage(string pageKey)`：已构造 → BlockSpec≥2；未构造 → 入口表 SingleBlock 才 false，其余 true。
- `public static bool LayoutShellHooks.BeginEditPage(string pageKey)`：屏幕上就有 → 直接 EnterEdit；入口表登记 → `SettingsPage.OpenPageForLayoutEdit`；否则自导航 + 重试找宿主。
- `internal static void LayoutShellHooks.OpenLayoutSettings(bool advanced = false)`。
- `public bool BlockHost.CanEdit`。

## 降级 / 没做
- 拖宽时「N / 12 列」读数气泡没做（既有落点占位框已显示吸附后的宽度）。
- 网格是既有 12 列制（效果图示意 6 列）；宽度仍只能是整数列。
- 「导出本页」沿用 v1 页面布局格式（顺序/显隐），网格几何不进文件——与既有导出同口径；整套几何请用「导出整套布局」。
- 块卡片「折叠」按钮 / 「全部折叠」随旧操作条一起退场。
- 下边整条拖改固定高度（双击复位）保留但没有可见手柄。
- 用户在抽屉里亲手添加的网页/按钮组件直接 `settings.enabled=true`（前提：已在高级里打开总开关）；导入布局里带的仍默认关闭。
- 新组件的标题 = 类型名（便签/时钟…），改标题需在布局 JSON 里（设置页的自建区块小节已被 B 移除）。

## 新字符串
60 条，见工作区 `tmp/v3_strings/A.txt`（中文 + 建议英文）。未改 locales，由 E 入表；若有「源码 I18n key 必须在词表」守卫测试，入表前会红。

## 风险
- 未编译：最可能出问题的是 `LayoutEditorV3.cs` 里的 WPF 细节（`Hyperlink`/`TextElement`、`Lucide.Icon(..).Center()`、`Ui.Badge` 子元素为 TextBlock 的假设）。两个 partial（`LayoutEditor`/`LayoutCard`）须同为 sealed/同访问级——已核对。
- 测试：`LayoutProfileTests.cs:567` 注释写着「CanEdit 放宽到 specs ≥ 1」，若有断言单块页可进编辑会失败（归 E）。
- 进编辑不再自动折网格：以前「列表页一进编辑就能左右并排拖」的行为改为先点「网格」。
- `SettingsPage.Select(int)` 的反射兜底只在入口表外的页面才用；B 改名时只影响兜底。
- 卡片悬停标签伸出卡片上沿 14px，第一行依赖页面上边距（默认 14px）不被裁。

## 需要在 Windows 真机上点的回归
1. 下载页右键空白 →「自定义布局...」：顶部出现一条工具条（标题「编辑「下载」页」），底部再没有 7 个按钮和「已隐藏的区块」整条。
2. 块默认灰色虚线框，悬停才出现左上块名、右上眼睛；点一下块 → 主题色实线；自建便签显示「联机便签 · 组件」+ 垃圾桶，不再出现 w_note1。
3. 列表模式上下拖换序 → 撤销/重做图标可用、Ctrl+Z/Y 生效；点「网格」→ 拖右边缘手柄改宽（整数列）、拖动并排；再点「列表」→ 回到每块一行。
4. 眼睛隐藏「版本筛选」→「添加」按钮出现数字 1 → 打开抽屉在「已隐藏的内容」点「显示」恢复；Esc 先关抽屉、再按 Esc 完成编辑。
5. 抽屉「组件」：加时钟（再看按钮变「已添加」）、加便签（输入内容后确定）；垃圾桶删掉后 Ctrl+Z 恢复。
6. 抽屉底部链接 → 跳设置 → 布局并展开「高级」；在高级打开「网页和按钮组件」后回到编辑态抽屉出现网页/按钮。
7. ⋯ → 导出本页… 存文件；改顺序后 ⋯ → 导入到本页… 恢复；⋯ → 整套布局与方案… 跳设置 → 布局。
8. 设置「下载与网络」「反馈与关于」右键不出「自定义布局...」，四周没有手柄；设置 → 布局 页面卡片「编辑」能跳到对应页并直接进编辑态。
9. 手工把某页 JSON 改成 `"mode":"canvas"` → 打开该页按网格整齐呈现、启动不报错；编辑后 JSON 变为 grid。
10. 常态（不在编辑态）各页外观与改动前逐像素一致（尤其 AI 页 / 实例库这类原生默认排版页）；暗色/亮色主题切换后虚线框、标签颜色正确。
