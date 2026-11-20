# R35 · lin-l3 —— 布局 v3 WP-L3(§5.3 模式收敛)落地到 Linux 树

> 分支 `r35/lin-l3` · 工作区 `D:\项目\TipCL\_r35\lin-l3` · 起点 `r35/lin-base`(7039719)
> 规格:`windos/GOAL-layout-v3.md` §3 Q1、§4.1/§4.2、§5.3、§6.3「WP-L3」、§6.4 第 5 条;Windows 参考 `r34/l3` @ d195462。
> 只 `dotnet build` / `dotnet test`(经 `_r35\build.ps1`),没有启动 TipCL.App。

## 1. 做了什么

1. **把推导收敛成一个公开纯函数**(Core,零 Avalonia):新建 `src/TipCL.Core/Launcher/Layout/LayoutCanvasMigration.cs`
   - `IsCanvas(page)`、`SpanOf(w, cols, minCols)`、`ToGridPage(page, minColsOf)`(返回副本)、`ConvertInPlace(page, minColsOf)`(就地)。
   - 规则(与 Windows d195462 逐条同口径):全部块(含隐藏)按 (y, x) 稳定排序,块表顺序与 z 随之改;`span = clamp(round(w × cols), min(cols, max(2, MinCols)), cols)`(0.5 进位、NaN/∞ 按整行);可见块按行流左对齐,放不下或上沿已在本行最矮块下沿之下就换行;每块 h = 1、w = span;页级 mode = grid、cols = `SnapCols(原 cols)`;id 为空的坏块原样接在末尾。
   - Linux 专属一处:隐藏块排在可见块之后的行里(x = 0、每块一行、宽 = 推导 span),沿用基线 `CanvasToGrid` 的落点(重新显示时出现在页尾);并且 `row_h` 置 0(canvas 下它不生效,推导后保留就会突然变成固定行高 —— 也沿用基线)。
2. **核对并改正基线 quick-v3 C 的 `LayoutEngine.CanvasToGrid`**(`LayoutEngine.cs:994-1042`)。原实现与 GOAL 不符的点:
   | 基线 | 问题 | 现在 |
   |---|---|---|
   | `cols` 固定 12 | 公式是 `w × cols`,6 列页会算错 | `SnapCols(page.Cols)` |
   | 下限硬编码 2 | 不认块自己的 `MinCols` | `max(2, minColsOf(id))`,经可选参数传入 |
   | `Math.Round` 默认银行家舍入 | 4.5 列 → 4 | `MidpointRounding.AwayFromZero` |
   | 每块一行后 `LayoutGrid.Compact` 压紧 | 压紧会把左边稍低的块浮到右上块前面,**打乱先 y 后 x**(用例 `CompactionWouldBreakYThenXSoDerivationDoesNotCompact` 钉住) | 行流放置,不压紧 |
   | 隐藏块不参与排序、块表顺序不变 | 块表顺序不是先 y 后 x | 全部块按 (y,x) 排序,z = 新序号 |
   | 「文档原样不动」 | ✅ 编辑器传的是克隆(`LayoutEditor.cs:597-604`),只看不改不落盘(`ExitEdit` 的 untouched 分支) | 不变;并新增只读入口 `CanvasGridView` |
   - `CanvasToGrid(page, minColsOf = null)` 现在 = `LayoutCanvasMigration.ConvertInPlace` + Linux 渲染口径「`min_h ≤ 0` 的块写 1」(与 `StackToGrid` 同,让推导网格行高跟随内容,不被 `FallbackMinH`=120px 撑高)。签名只是**加了一个可选参数**,编辑器现有调用 `CanvasToGrid(working)` 源码兼容。
   - 新增 `LayoutEngine.CanvasGridView(page, minColsOf)`(`LayoutEngine.cs:1028`):渲染用只读副本。
3. **`GridCanvasPanel`**(`GridCanvasPanel.cs:61-63` 文件头、`:139` `DerivedFromCanvas`、`:370` `SetLayout`、`:507` `ApplyTo`):
   - `SetLayout("canvas", …)` 不再按归一化矩形 / z 序 / 参考高排,而是 `CanvasGridView` 推导成网格:`Mode = grid`、`DerivedFromCanvas = true`、`Cols` 用推导后的列数、`RowH = 0`;入参页一个字节不改。
   - `ApplyTo(page)`:`DerivedFromCanvas` 或页仍是 canvas 时,先 `CanvasToGrid(page)` 整页写成 grid 再按槽位覆盖几何 —— 即「提交一次才写成 grid」。非 canvas 页行为与改动前完全一致。
   - `IsCanvas` 保留(恒为 false,注释说明),下面的 canvas 排布 / 方格底纹分支不可达,留到合并后删。
4. **退役成员只加注释,不删不改名**(BRIEF §3 第 5 条):`LayoutCanvas` 类注释(`LayoutEngine.cs:2033` 上方)、`Steps`(`:2054`)、`HandleNames`(`:2057`,`LayoutEditor.cs:3053` 仍在用)、`LayoutEditSession.Nudge` 的 canvas 分支(`:3182` 上方)。**没加 `[Obsolete]` 特性**:会在 `LayoutEditor.cs`(lin-l12 的文件)和测试里引出新增 CS0618 警告,与 Windows 参考同做法。`CycleSnapStep` 已被基线 quick-v3 C 从编辑器删掉,`_snapBtn` 也已不存在(grep 确认)。
5. **AI `layout_apply`:N/A**。`grep -r layout_apply src tests` 无结果,Linux 树没有这个工具。

## 2. 逐条对照 GOAL

| GOAL | 落地点 |
|---|---|
| §3 Q1 已存 canvas 页按网格渲染,x/w 推出列宽和顺序 | `LayoutCanvasMigration.cs:80` `ConvertInPlace`;渲染入口 `LayoutEditor.cs:601-604`(基线已接)→ `LayoutEngine.cs:1008`;`GridCanvasPanel.cs:370` |
| §3 Q1 JSON 原样保留,编辑器提交一次之后才写成 grid | 推导只作用在克隆/副本上(`LayoutEngine.cs:1028`、`LayoutCanvasMigration.cs:57`);提交 = 编辑器 `Persist`(基线)/ `GridCanvasPanel.ApplyTo`(`:507`) |
| §3 Q1 / §5.3 AI `layout_apply` 收到 canvas 按 grid | **N/A**(Linux 无此工具) |
| §4.1 向后兼容:字段不丢、未知字段原样、渲染不报错、块不丢、顺序不乱 | Core 只**新增**一个文件;`ModeCanvas` 常量、`IsKnownMode`、Sanitize 的 canvas 钳位全部保留;用例覆盖未知字段 / 隐藏 / custom / plugin |
| §4.2 不改数据模型 | `LayoutDocV2` / `LayoutSchema` 一行未改 |
| §5.3 先 y 后 x;`span = clamp(round(w × cols), MinCols, cols)`;纯函数 + 单测 | `LayoutCanvasMigration.SpanOf`(`:39`)/ `ConvertInPlace`;`LayoutCanvasMigrationTests` |
| §5.3 `Steps` / `CycleSnapStep` / 画布 8 向手柄退出界面,删不干净的留着不可达 | 引擎/面板侧不可达 + 注释(见 §1.4);编辑器侧 8 向手柄只在 `_boxMode == canvas` 时显示(`LayoutEditor.cs:3326`),读入已转 grid ⇒ 不可达 |
| §6.3 WP-L3 迁移单测(3 份 canvas 文档) | `tests/TipCL.Tests/Layout/LayoutCanvasMigrationTests.cs`(10 例,见 §4) |
| §6.3 canvas 界面行为用例改 Skip 并写明原因,纯模型用例继续通过 | `LayoutGridCanvasTests.cs` 4 例 Skip(常量 `SkipRetiredCanvasUi`);`LayoutEngineTests` 里 `LayoutCanvas` 纯几何用例一个未动、全部通过 |
| §6.4 第 5 条 不删/不改名编辑器用到的成员 | 遵守;`CanvasToGrid` 只追加可选参数 |

## 3. 文件清单

| 文件 | 性质 |
|---|---|
| `src/TipCL.Core/Launcher/Layout/LayoutCanvasMigration.cs` | 新建(Core 只许新增 ✔) |
| `src/TipCL.App/Pages/Layout/LayoutEngine.cs` | 改:`CanvasToGrid` 收敛 + `CanvasGridView` + 退役注释 |
| `src/TipCL.App/Pages/Layout/GridCanvasPanel.cs` | 改:canvas → 推导网格,`DerivedFromCanvas`,`ApplyTo` 提交写 grid |
| `tests/TipCL.Tests/Layout/LayoutCanvasMigrationTests.cs` | 新建 |
| `tests/TipCL.Tests/Layout/LayoutGridCanvasTests.cs` | 改:4 例 Skip + 压实用例的 canvas 段改断言 |
| `progress/R35-lin-l3.md` | 本报告 |

`FitBox.cs` / `FillPanel.cs` / `LayoutFill.cs` 与 canvas 无关,未改。清单外文件一个未动。

## 4. 测试

| | 通过 | 失败 | 跳过 | 合计 |
|---|---|---|---|---|
| 开工基线(全量,7039719) | 978 | 0 | 7 | 985 |
| 收尾(全量,63d8c2f) | **984** | 0 | 11 | 995 |

差额 = +10 新用例,−4 改 Skip(978 + 10 − 4 = 984)。`FullyQualifiedName~Layout`:280 / 0 / 5。

新用例 `LayoutCanvasMigrationTests`(3 份文档:① 并排两块 + 隐藏内置块 + 整行便签组件、块表顺序与位置错开;② 插件块 + 上下叠的内置块 + 隐藏时钟组件 + 宽 0.05 的便签;③ 6 列页、重叠块、未知字段、row_h≠0、隐藏插件块):
1. `DerivedGridKeepsEveryBlockIdHiddenFlagAndUnknownField` —— 块集合 / hidden / style / 未知字段不变,custom / plugin / 隐藏块都在。
2. `DerivedGridOrderIsYThenX` —— 块表顺序与网格阅读顺序都 = 先 y 后 x,z 连续;具体落点。
3. `CompactionWouldBreakYThenXSoDerivationDoesNotCompact` —— 钉住不压紧的原因。
4. `DerivedSpansFollowTheFormulaAndStayInRange` —— span = 公式、∈[min(cols,2), cols]、w == span、x + w ≤ cols、整数坐标、cols / row_h。
5. `SpanOfClampsRoundsAwayFromZeroAndHonoursMinCols` —— 边界值 + `minColsOf`。
6. `NonCanvasPagesAreLeftAlone`。
7. `ReadDeriveAndWriteBackIsByteIdentical` —— 导出 → 读入 → 三个推导入口(`CanvasGridView` / `ToGridPage` / `GridCanvasPanel.SetLayout`)→ 再导出,**UTF-8 字节逐字节相同**。
8. `EditorThatOnlyLooksLeavesTheFileByteIdentical` —— 真编辑器(无头)渲染 + 进出编辑不改,磁盘 `ui_layout_v2.json` **逐字节不变**,各页仍是 canvas。
9. `EditorCommitWritesGridAndKeepsIdsHiddenAndWidgets` —— 编辑器里提交一次后三页都落成 grid,id 集合、每块 hidden、`widgets[]`(id/type/title)、未知字段都在。
10. `PanelDerivesGridFromCanvasAndApplyToCommitsGrid`。

改 Skip 的 4 例(`LayoutGridCanvasTests`,原因:canvas 自由画布退出界面,面板收到 canvas 按网格推导,这些用例钉的是退役的 canvas 排布 / 底纹行为):
- `CanvasModeMapsNormalizedRectsAndUsesHostHeight`
- `CanvasModeWithInfiniteHeightFallsBackToReferenceHeight`
- `CanvasModeDeliversChildrenInZOrderAndMatchesById`
- `CanvasHatchFollowsSnapStepAndSkipsWhenStepIsZero`

改断言 1 处:`CompactCells…`(`LayoutGridCanvasTests.cs` 压实用例末段)原断言「canvas 不参与压实、NY 保持 0.5」→ 现断言「推导成网格、参与压实到第 0 行、入参页的 y 仍是 0.5」。

## 5. 新增 / 废弃词表 key

无(本包没有新增界面文案;新文件里的中文只在注释和测试里)。

## 6. 跨包接线需求(合并时处理,我没动别人的文件)

1. **lin-l12 · `LayoutEditor.cs:603`**:建议把 `LayoutEngine.CanvasToGrid(working)` 改成 `LayoutEngine.CanvasToGrid(working, id => Math.Max(2, MinColsOf(id, LayoutSchema.SnapCols(working.Cols))))`,让推导时就认块自己的 `BlockSpec.MinCols`(现在不传也正确,只是按 2 列兜底,进编辑态时编辑器自己的 MinCols 撑宽逻辑 `:507-509` 会再补一次)。
2. **lin-l12 · `LayoutEditor.cs:645` `IsBoxMode`、`:3129` `_boxMode`、`:3326` 8 向手柄显示**:读入已转 grid,这些 canvas 分支不可达;若合并后还有路径把 canvas 页直接交给编辑器状态(例如未来的「导入到本页」不经 `LoadFromStore`),面板会推导成网格而编辑器 `_boxMode` 仍是 canvas。保险做法:`_boxMode` 判定改读 `GridCanvasPanel.Mode`(恒为 grid/stack),或进状态前统一 `CanvasToGrid`。
3. **lin-l4 · `SettingsPage.Layout.cs:748/778`**(页面卡片缩略图):对 canvas 页仍按 canvas 画;建议先 `LayoutEngine.CanvasGridView(page)` 再按 grid 画,缩略图才与实际渲染一致。
4. 真正删除 `LayoutCanvas` 的 8 向手柄 / 吸附 / Move / Resize、`GridCanvasPanel` 的 canvas 排布与方格底纹、`Nudge` 的 canvas 分支、以及上面 4 个 Skip 用例:合并后与编辑器侧一起做。

## 7. 决定(请主 agent 合并时编号)

- **D-R35-L3-1**:推导函数放 Core 新文件 `LayoutCanvasMigration.cs`(与 Windows 同名同语义,方便两树对照);App 侧 `LayoutEngine.CanvasToGrid` 只是薄封装。
- **D-R35-L3-2**:Linux 推导与 Windows 的差异只保留基线已有的两条渲染口径:隐藏块落在可见块之后的行(Windows 是契约默认矩形 (0,0,1,1))、`min_h ≤ 0` 写 1;另外 `row_h` 置 0。三者都只影响「推导出的副本 / 提交后的 grid」,不影响「未提交时文档原样不动」。
- **D-R35-L3-3**:不加 `[Obsolete]` 特性,只写注释(避免在别人的文件里冒新警告)。
- **D-R35-L3-4**:推导不再压紧(基线 quick-v3 C 的做法),改行流放置 —— 压紧会违反「先 y 后 x」。

## 8. 待真机

1. 把某页 `ui_layout_v2.json` 改成 `"mode": "canvas"`(带 0..1 的 x/y/w/h,含一个 hidden 块、一个 `custom:` 便签、一个 `plugin:` 块)重启:该页按网格显示、不报错、块不少、顺序先上后下先左后右。
2. 只进编辑态不改直接「完成」:文件哈希不变。
3. 拖一下再「完成」:该页变 `"mode": "grid"`,三类块与 `widgets[]` 都在。
4. 截图:`--shot <该页>` / `--shot <该页> --edit`(列入 WP-L6 截图矩阵)。

## 9. 风险

- 推导不再压紧:基线里「上下错开但左右放得下」的两块原本会被压到同一行,现在会分两行(严格先 y 后 x)。看上去可能比基线稀一点,属于规格要求。
- `GridCanvasPanel.SetLayout("canvas")` 行为变了(公开 API):编辑器现在不会传 canvas 进来,测试之外没有别的调用方(grep 确认)。
