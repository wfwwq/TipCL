# LAYOUT-V3-C · Linux 编辑态 v3(代理 C `lin-edit`)

> 任务书:工作区 `tmp/v3_spec.md` §1.A(10 条)· 效果图 `tmp/layout3_noimg.html` 的 `secA` · 现状截图 `tmp/layout3_shots/now_1.png` / `now_2.png`。
> 前提:`docs/README.md`「Linux 编辑态与 Windows 的四处差异」**原样保留**(stack 进编辑态折成 12 列 grid 且退出保留网格 / 格子运算换位无滞回 / 30ms·48px·28px 边缘滚动 / 滚动条冒出后按新宽度重量行高),v3 是叠在这之上的。
> 本机不能编译 / 跑测试 / 用 git:下面所有调用的 API 都先 grep 过签名(Avalonia 11.3.2,net8,`LangVersion latest`,`Nullable enable`)。

## 0. 改了哪些文件(全部在代理 C 名下)

| 文件 | 性质 |
|---|---|
| `src/TipCL.App/Pages/Layout/LayoutEditor.cs` | 改:`partial` 化 + 若干小 diff(工具条 / 托盘 / 根网格 / 卡片外观 / 删除与隐藏 / 画布读入) |
| `src/TipCL.App/Pages/Layout/LayoutEditor.V3.cs` | **新建**:v3 编辑外壳(顶部编辑条、⋯ 菜单、列表/网格、添加抽屉、加组件、删组件、单块判定) |
| `src/TipCL.App/Pages/Layout/LayoutEditChrome.cs` | **新建**:撤销/重做/眼睛/列表/网格/分隔线图标(IconLib 里没有)、页名、浮层小底板 |
| `src/TipCL.App/Pages/Layout/LayoutShellHooks.cs` | 改:右键菜单单块页不给「自定义布局...」;新增 `CanEditPage` / `BeginEditPage` |
| `src/TipCL.App/Pages/Layout/LayoutEngine.cs` | 改:新增 `LayoutEngine.CanvasToGrid` |
| `src/TipCL.App/Pages/Layout/GridCanvasPanel.cs` | 改:网格底纹只在拖动中画;悬停 / 选中卡片抬高 ZIndex |
| `src/TipCL.App/Pages/Layout/BlockHost.cs` | 改:进入编辑的 Toast 正文换成 §1.A.10 文案 |
| `src/TipCL.App/Pages/Layout/LayoutBlockEntryCatalog.cs` | 改:`feedback` 标单块;7 个裸中文标签接进 `I18nCore.Tr` |

非本人文件一个都没动(SettingsPage*、MainWindow、Common/IconLib、locales、tests、docs 均未改)。

## 1. §1.A 逐条落地

| # | 要求 | 落地点 | 说明 |
|---|---|---|---|
| 1 | 红色实线 → 灰色虚线;只有选中的块用主题色 | `LayoutEditor.cs:3031`(`_outline` 虚线矩形,外扩 4px 对应 outline-offset)、`:3434` `ApplyEditOutline`、`:3393` `ApplyLook` | 编辑态不再铺主题色描边 + 10/26 透明度蒙版,内容保持常态外观;未选中 = `B.InkFaint` 虚线(悬停 `B.InkMuted`),选中 = `B.Accent` 2px 实线。Avalonia `Border` 没有虚线 ⇒ 用 `Shapes.Rectangle.StrokeDashArray` 叠一层,不参与命中。 |
| 2 | 块名只在悬停时出现;自建组件显示自身标题 | 卡片:`LayoutEditor.cs:2962` 起(块名浮条 + 「· 组件」后缀)、`:3196` `UpdateChrome`;标题:`LayoutEditor.cs:1011` `SpecOf` → `LayoutEditor.V3.cs:834` `WidgetTitle` | 块名 / 按钮浮在卡片上沿外 14px(`_bar` 跨三行叠放,不占内容高度,出现消失内容不跳);只在悬停或选中时显示。自建块标题 = `WidgetDef.Title`(如「联机便签」),没写用类型名,定义缺失叫「组件」,**不再露 `w_note1` / `custom:` id**。 |
| 3 | 底部 7 个按钮 → 顶部一条 | 编辑条:`LayoutEditor.V3.cs:112` `BuildEditBar`;挂接:`LayoutEditor.cs:245-290`(根网格改 `Auto,*,Auto`,编辑条在第 0 行,`_scroll`/`_boxScroll` 换到第 1 行 `:813/:825`);⋯ 菜单 `LayoutEditor.V3.cs:260` | 标题「编辑「<页名>」页」· 列表\|网格分段 · 撤销 / 重做**图标** · 「添加」(角标 = 隐藏了几项)· 重置本页 · 完成 · ⋯(导出本页… / 导入到本页… / 整套布局与方案… → `LayoutShellHooks.OpenLayoutSettings` → `SettingsPage.OpenLayoutView()`)。提示文字列可截断、悬停显示 §1.A.10 全文。 |
| 4 | 去掉「吸附 N」;整数列;网格下拖右边缘改宽度 | 删 `_snapBtn` / `CycleSnapStep`(`LayoutEditor.cs` RefreshToolbar 段);右边缘把手 `_pill` `LayoutEditor.cs:3075`;列数提示 `:2178`(「N / 12 列」);小方块手柄只留给 canvas `:3326` | 宽度本来就按整列吸附(`LayoutGrid.ResizeCell`),这里补上效果图的右边缘竖把手 + 拖动时「N / 12 列」标签;e/s/se 小方块在网格里不再画。右边缘整条热区(`_strips["e"]`)保留。 |
| 5 | 隐藏托盘并入「添加」抽屉 | 抽屉 `LayoutEditor.V3.cs:452` `BuildDrawer`、`:536` `RefreshDrawer`、加组件 `:713` `AddWidgetToPage`;托盘停用 `LayoutEditor.cs:245`、`RefreshTray` | 抽屉标题「添加到「<页名>」页」,两组「已隐藏的内容」(点一下放回原位)/「组件」(点一下加到页末,单例组件已加过显示「已添加」);底部「网页、按钮组件默认关闭，可在 设置 → 布局 → 高级 里开启。」可点 → `SettingsPage.OpenLayoutAdvanced()`(D 提供)。`LayoutProfiles.DangerousWidgetsEnabled` 为 true 时多列网页 / 按钮(新块默认关)。底部不再有「已隐藏的区块」整条;托盘外壳保留但永不显示,非编辑态底部那条约 24px 空带与改动前一致。Esc:抽屉开着先关抽屉。 |
| 6 | 眼睛隐藏 / 垃圾桶删除分开 | 卡片按钮 `LayoutEditor.cs:2962-2971`;删除 `LayoutEditor.V3.cs:810` `DeleteBlock`;隐藏 Toast `LayoutEditor.cs:1060` `HideBlock`;键盘 Delete `LayoutEditor.cs:2685` `DeleteSelection` | 内置内容(含插件块)= 眼睛,Toast「已隐藏「X」· 在「添加」里可以找回」;自建组件 = 垃圾桶(悬停变红),只摘本页块引用、`widgets[]` 定义保留,压撤销栈,Toast「已删除「X」· 按 Ctrl+Z 可以撤销」。键盘 Delete 同规则(内置改为隐藏,不再弹「内置区块不能移除」)。隐藏后其余块压紧补位。 |
| 7 | 只留列表 / 网格,不再提供自由画布,老存档兼容 | 分段 / 切换 `LayoutEditor.V3.cs:352` `SetLayoutKind`;判定 `IsListShape`;读入换算 `LayoutEditor.cs:603` → `LayoutEngine.cs:1000` `CanvasToGrid` | **列表** = 每块占满一整行的 12 列网格(Linux 差异 #1:stack 页进编辑态本来就折成网格,这里不"统一"回一维换序);**网格** = 一行可并排。切到列表前记下每块列位/列宽,本次编辑内切回网格还原。进编辑时按现状自动选中(stack 单列 → 列表)。列表态右边缘改宽度禁用。**canvas 老存档**:读取时在内存里换算成 12 列整数列网格(先每块一行,再压紧让左右并排的块回到同一行),磁盘上的 canvas 字段原样不动,编辑态真改过才按 grid 落盘;Core 的 schema / 模型 / v1 兼容一个字段都没删。 |
| 8 | 单块页面不提供编辑入口 | `LayoutEditor.cs:375` `CanEdit` → `LayoutEditor.V3.cs:65` `IsSingleBlockPage`;右键 `LayoutShellHooks.cs:197`;`CanEditPage` `LayoutShellHooks.cs:291`;清单 `LayoutBlockEntryCatalog.cs:63` | 入口清单标了单块(`settings:network`,本次新增 `feedback`)或实际块数 < 2 → 不给入口:右键菜单不出现「自定义布局...」(只剩「布局设置...」),设置页卡片经 `CanEditPage` 判定不给「编辑」。`EnterEdit()` 自身仍只要求 ≥1 块(测试宿主 / dev 钩子不受影响)。 |
| 9 | 右键空白处 →「自定义布局…」 | `LayoutShellHooks.cs:180-225` | 保留原链路(键名沿用既有的「自定义布局...」,已在词表里)。 |
| 10 | 提示文案 | `LayoutEditor.V3.cs:106` `FullHint()`;编辑条提示悬停全文;`BlockHost.cs:170` 进入编辑 Toast | 「拖动块调整顺序；网格模式下拖块的右边缘改宽度；悬停能看到块名和隐藏 / 删除按钮。」;编辑条短提示按排法:列表「拖动调整上下顺序」/ 网格「拖动换位置 · 拖右边缘改宽度」。 |

附带:网格底纹(列分隔线)只在拖动 / 改宽度过程中画,行分隔线不再画(`GridCanvasPanel.cs:934`,效果图 `.busy .cols`);悬停 / 选中卡片 ZIndex 抬到 10,浮出的块名盖住上一行(`GridCanvasPanel.cs:881`、`LayoutEditor.cs:3196`);编辑态画布上边距 +14px 给第一行的块名留地方(`LayoutEditor.cs:741`)。

## 2. 给设置代理 D 的入口(已写进 `tmp/v3_handoff.md`「Linux」)

- `public static bool LayoutShellHooks.CanEditPage(string pageKey)` — 单块 → false;空串 → false;没装载过 / 未注册 → true(按 D 要求不误判成单块)。
- `public static bool LayoutShellHooks.BeginEditPage(string pageKey)` — 跳页(SelectTab / OpenLayoutView / SelectSegment / ShowTab)→ Loaded 后找可见宿主 → `EnterEdit()`;找不到给 `NeedsContext` 提示。

## 3. 没做 / 降级

1. **「自动靠拢」开关**:效果图工具条没有它,但删掉会让关掉过的用户再也开不回来 ⇒ 挪进 ⋯ 菜单最后一项「松手后自动靠拢」(开着时带勾)。
2. **「全部折叠」/ 卡片折叠按钮**:按效果图从界面上拿掉;折叠逻辑与字段保留(无入口)。
3. **下边改高度**:效果图只画了右边缘把手;下边整条热区(拖出固定像素高、双击复位)**保留但不画把手**,避免功能回退。
4. **抽屉动画**:效果图是 260ms 右滑入;这里是直接显隐(不额外加动画依赖)。
5. **新加组件的闪烁高亮**:没做;改为自动选中新块 + 滚到页末。
6. **列表↔网格的撤销**:撤销会还原几何,但「排法」标记只在"列表态撤销出并排块"时自动回网格;反过来(网格态撤销回整行)仍显示网格,点一下即可。
7. **删除确认**:按任务书走既有撤销机制(Ctrl+Z / 撤销图标),没有额外弹确认框。

## 4. 新字符串

全部包 `I18nCore.Tr`,清单(中文 + 建议英文,共 65 条,其中 7 条是入口清单里原本裸写、这次接线的标签)在 `tmp/v3_strings/C.txt`。i18n 基线:本次在本人文件里新增 wrapped 约 70+、移除约 20(旧工具条 / 托盘文案),**wrapped 净增**;`LayoutBlockEntryCatalog.cs` 7 个裸字面量接线 ⇒ **unwrapped 减 7**。

## 5. 风险

- **不能编译**:最可能出问题的点(已逐个 grep 过,但未经编译器):`Shapes.Rectangle.StrokeDashArray = new AvaloniaList<double>{…}`;`GetObservable(...).ToBinding()`(照抄 `U.cs` / `UiHelpers.cs` 的写法与 using);`Avalonia.Controls.PlacementMode.Bottom`;`switch` 表达式里用 `LayoutSchema.Widget*` 常量做模式;LayoutCard 构造里对 `readonly` 字段 `_outline/_pill/_custom` 的赋值顺序。
- `LayoutEditor.CanEdit` 语义收紧(<2 块 → false):依赖它判断"能不能进编辑"的调用方都会跟着变(右键、FindHosts、CanEditPage、dev 钩子 `TIPCL_DEV_LAYOUT_EDIT`)。
- `feedback` 标为单块 ⇒ 反馈页右键不再有「自定义布局...」。若产品上 Linux 的反馈页仍想可编辑,只需撤掉 `LayoutBlockEntryCatalog.cs:63` 的 `SingleBlock = true`。
- 编辑态卡片悬停会 `ApplyLook` + 切 `_bar` 显隐(触发一次卡片测量);大页面上悬停扫过很多卡片时可能有轻微开销。
- canvas → grid 换算是近似:画布里重叠的块会被压紧拆开;画布高度(`H`)不换算成固定像素高(改为跟随内容)。

## 6. 预计受影响的测试(请 E 更新;本人未改 tests)

| 测试 | 原因 |
|---|---|
| `LayoutEditorTests.FoldAllCollapsesCardsWithoutWritingLayout` | 编辑条上不再有「全部折叠 / 全部展开」按钮 |
| `LayoutEditorTests.ToolbarHintIsEllipsizedAndCarriesFullTooltip` | 提示文案改为「拖动换位置 · 拖右边缘改宽度」(全文在 ToolTip);按钮集合改为 撤销/重做(图标,无文字)、添加、重置本页、完成、⋯;不再有 吸附 / 全部折叠 / 完成编辑 / 导出布局 / 导入布局 |
| `LayoutEditorTests.HideCardThenRestoreFromTrayKeepsOriginalSlot` | 托盘 chip → 抽屉「已隐藏的内容」行(仍是含块名 TextBlock 的 Button,但抽屉默认不可见;若测试的 `Find` 跳过不可见子树就找不到,需先 `ShowDrawer(true)` 或点「添加」);隐藏按钮 ToolTip 仍是「隐藏」 |
| `LayoutSmoothDragTests.ResizingFollowsPixelsShowsSizeAndSnapsOnRelease` | 拖右边缘时尺寸标签改为「N / 12 列」,不再是「宽 × 高」像素 |
| `LayoutGridCanvasTests.HatchIsDrawnOnlyInEditMode` | 网格底纹改为只在拖动中(`ShowPlaceholder == true`)画 |
| `SubViewBlockHostWiringTests`(`LayoutMoreTests.cs:657-661`)里 `FeedbackView` 那条 | `feedback` 标为单块 ⇒ `host.Editor.CanEdit == false` |
| 可能:依赖编辑态卡片 `BorderThickness` / 主题色蒙版、或依赖「内置区块不能移除」Toast 的用例 | 编辑态外观与 Delete 键语义变了(grep 未发现直接断言,列出备查) |
| i18n:`I18nBaselineTests.UnwiredCjkLiteralCountMustNotGrowSinceBaseline` | 预计**仍绿**(unwrapped -7、wrapped 净增);E 刷基线时 `LayoutBlockEntryCatalog.cs` 的 per_file 计数会下降 |

## 7. Linux 真机回归步骤

1. 「下载 → 原版」页空白处右键 →「自定义布局...」:顶部出现一条编辑条(「编辑「下载」页」· 列表 选中 · 撤销/重做灰 · 添加 · 重置本页 · 完成 · ⋯),底部**没有**旧的 7 个按钮和「已隐藏的区块」条;每块外面一圈灰色虚线,没有红框 / 蒙版。
2. 鼠标移到某块上:左上浮出「⋮⋮ 块名」,右上浮出眼睛;移开就消失;点一下块 → 变主题色实线并保持显示。
3. 点眼睛隐藏「筛选」:其余块补上来;Toast 提示;「添加」上出现角标 1;点「添加」→ 右侧抽屉「添加到「下载」页」,「已隐藏的内容」里有「筛选」,点它 → 回到原位,角标消失;Esc 关抽屉,再 Esc 完成编辑。
4. 抽屉「组件」里加一个「便签」:页末出现,块名显示便签的标题(不是 `w-…` / `custom:`)且带「· 组件」;悬停右上是垃圾桶(悬停变红),点它删除 → Toast「已删除…按 Ctrl+Z 可以撤销」→ Ctrl+Z 回来。
5. 点「网格」:Toast;把鼠标放到某块右边缘竖把手上向左拖 → 出现「N / 12 列」标签,松手整列落位,拖动时才出现列分隔线;拖块换位置正常(Linux 原有的格子换位手感不变)。再点「列表」→ 每块回到整行,且右边缘把手消失;再点「网格」→ 刚才的宽度回来。
6. ⋯ → 导出本页… / 导入到本页… 弹文件框;⋯ → 整套布局与方案… → 跳到 设置 → 布局(编辑态自动保存退出);⋯ → 松手后自动靠拢 切换后再开菜单,勾随之变化。
7. 设置 →「下载与网络」「反馈与帮助」页空白右键:**没有**「自定义布局...」(只有「布局设置...」);设置 → 布局 页面卡片上这两页没有「编辑」。其它卡片点「编辑」→ 跳到该页并直接进编辑态(如「下载」「Java」「资源目录 · Mod」)。
8. 老存档兼容:把某页 `ui_layout_v2.json` 里的 `"mode": "canvas"`(带 0..1 的 x/y/w/h)放回去重启 → 该页按网格显示、不报错;只进编辑不改直接「完成」→ 文件里仍是 canvas;拖动一下再完成 → 文件里该页变成 `"mode": "grid"`、`cols: 12`。
9. 切英文界面:编辑条 / 抽屉 / Toast 若显示中文,说明 `tmp/v3_strings/C.txt` 还没入词表(E 负责)。
