# WP-L3 · 模式收敛（GOAL-layout-v3 §5.3，D-81 Q1）

- 分支 / 工作区：`r34/l3` · `D:\项目\TipCL\_r34\l3`
- 起点：`8cad7d0`（r34/base）。代码提交：`d195462`；本报告单独一次提交。
- 执行人：第二次派出的接替子代理（上一任在施工前掉线，工作区干净、无半成品；方案见会话里的 `WP-L3-partial.md`，下面「决定」写明了与方案不同的地方）。
- 全程**没有启动 TipCL.exe**，没有做界面自动化；只跑了 `dotnet build` / `dotnet test` / git。

## 1. 做了什么

| 需求（清单 / §5.3 / §6.3） | 落在哪里 | 说明 |
|---|---|---|
| 纯函数：canvas 页 → 网格渲染方案（先 y 后 x；`span = clamp(round(w×cols), MinCols, cols)`；保留 hidden、自建组件、插件块） | `Core/Launcher/Layout/LayoutSchema.cs` 新增 `LayoutCanvasMigration` | `IsCanvas` / `SpanOf` / `ToGridPage`（返回副本，入参不改）/ `ConvertInPlace`（提交用，就地改）。零 WPF，Core 与 Pages 共用一份。 |
| 渲染路径在 mode=canvas 时走推导 | `Pages/Layout/GridCanvasPanel.cs` `SetLayout` | 收到 canvas → `DerivedFromCanvas = true`、`Mode = grid`、槽位 = `LayoutGrid.Pack(LayoutCanvasMigration.ToGridPage(page))`。Panel 的 `Mode` 从此只会是 grid，`IsCanvas` 恒为 false（属性保留，LayoutEditor 还在读）。 |
| 文档原样不改；只有编辑器提交时才写成 grid | `GridCanvasPanel.ApplyTo` → 新增 `LayoutGrid.CommitCanvasAsGrid`（`LayoutEngine.cs`） | 推导只作用在副本上；`LayoutEditor.CommitBox → _box.ApplyTo(page)` 时整页（含白名单外的块）先 `ConvertInPlace` 成 grid，再用画布槽位覆盖可见块的几何。**LayoutEditor.cs 不用改就已经接上了**（CommitBox 本来就调 ApplyTo）。 |
| `LayoutSchema` 不改模型 | `LayoutSchema.cs` | `ModeCanvas`、`IsKnownMode`、Sanitize 的 canvas 钳位**一律不动**（否则旧文档读进来就被改写）；只补注释。 |
| AI `layout_apply`：mode 说明只列 stack / grid；收到 canvas 按 grid 处理并提示 | `Core/Launcher/Ai/LayoutTools.cs` | schema 里 `mode` 的说明改成「stack(列表…)/ grid(网格…)」；`mode=canvas`（不区分大小写）→ 落盘 grid，结果里追加 `LayoutTools.CanvasNotice`。目标页本来就是 canvas 时，先用同一个 `ConvertInPlace` 把几何换成网格单位（放在 blocks 合并之前，不盖掉 AI 给的新顺序）。 |
| 不删、不改名 LayoutEditor 用到的成员 | `LayoutEngine.cs` | `LayoutCanvas.Steps / HandleNames / StepOf / Move / Resize …`、`ModeCanvas`、`IsCanvas` 全部保留，只加「R34 退役」注释。**没有加 `[Obsolete]` 特性**（理由见决定 D-6）。 |
| 新块追加到 canvas 页时排在页尾 | `LayoutEngine.PlaceAppendedBlock` | 仍是 0.4×0.25 小卡片（`LayoutWidgetLifecycleTests` 钉着），但位置从左上角改成「最靠下那个块的同一高度、靠右 x=0.6」，推导出来就是最后一个。 |
| 单测 | 新建 `LayoutCanvasMigrationTests.cs`（21 例）；`LayoutV2RpcTests.cs` +3 例；`LayoutCanvasTests.cs` 6 例改 Skip | 见 §3。 |

推导规则（`ConvertInPlace`，`ToGridPage` 是它的副本版）：
1. 全部块（含隐藏）按 (y, x) 稳定排序，块表顺序改成这个顺序，z = 新序号；
2. 可见块按行流放置、左对齐：累计列宽超过 cols，或该块的上沿已经在本行最矮块的下沿之下，就换行；每块占 1 行，x / w / span = 推导出的列位与列宽；
3. 隐藏块写成契约默认矩形 (0,0,1,1) + 推导出的 span —— 重新显示时网格按 span 自动流；
4. 列宽下限 = `min(cols, max(2, MinCols))`（2 = 编辑器渲染兜底的「全局 2 列」）；
5. title / style / pad / settings / 未知字段 / height_px / min_h 原样；页级 gap / pad / row_h / 未知字段原样；cols 吸附到合法档；mode 改成 grid（只在副本或提交时）。

## 2. 文件清单（相对仓库根）

- `windos/src/TipCL/Core/Launcher/Layout/LayoutSchema.cs`（新增 `LayoutCanvasMigration`；`ModeCanvas` 注释）
- `windos/src/TipCL/Pages/Layout/LayoutEngine.cs`（`LayoutGrid.CommitCanvasAsGrid`；`PlaceAppendedBlock` canvas 分支；`LayoutCanvas` / `Nudge` 退役注释）
- `windos/src/TipCL/Pages/Layout/GridCanvasPanel.cs`（`DerivedFromCanvas`；`SetLayout` 推导；`ApplyTo` 提交成 grid）
- `windos/src/TipCL/Core/Launcher/Ai/LayoutTools.cs`（`mode` 说明；canvas → grid + `CanvasNotice`）
- `windos/tests/TipCL.Tests/LayoutCanvasMigrationTests.cs`（新建）
- `windos/tests/TipCL.Tests/LayoutCanvasTests.cs`（6 例 Skip）
- `windos/tests/TipCL.Tests/LayoutV2RpcTests.cs`（+3 例）
- `windos/progress/WP-L3.md`（本报告）

清单里的 `LayoutEngineTests.cs`、`LayoutGridCompactTests.cs` 没有需要改的地方（全部继续通过），所以没动。编码：全部保持原样（UTF-8 无 BOM、LF）。

## 3. 构建与测试（§7，只在本工作区）

| 项 | 基线（开工前，沿用上一任实测，见续跑须知） | 收尾 |
|---|---|---|
| compact 构建 | 0 错误；8 警告（NU1900×6 = NuGet 漏洞库缓存目录无权限；xUnit1031×2 = `BridgeMutationAndLaunchGuardTests.cs(34,30)/(35,22)`，均为既有） | 0 错误；8 警告，同样两类、同样位置，**0 新增** |
| premium 构建（`-p:Premium=true`） | — | 0 错误；8 警告（同上），**0 新增** |
| 全量测试（TipCL.Tests.dll，net8.0） | 通过 593 / 失败 0 / 跳过 0 | **通过 611 / 失败 0 / 跳过 6** |
| compact 体积 | 5,599,232 B | **5,602,304 B**（+3,072 B；上限 5,757,264 B，SIZE OK） |
| premium 体积 | — | 5,622,272 B（只记录） |

- 611 = 593 − 6（改 Skip）+ 24（新增）。失败用例：无。
- 跳过的 6 例（都在 `LayoutCanvasTests`，Skip 原因写在常量 `SkipRetiredCanvasUi` 里）：`SnapUsesOldDashHostFormula`、`SnapStepSnapsToContractChoices`、`HandleAtFindsCornersEdgesAndNothing`、`CanvasMoveSnapsAndClamps`、`CanvasResizeRespectsMinimumSize`、`CanvasResizeNorthWestKeepsOppositeCorner` —— 钉的是已退出界面的 8 向手柄 / 吸附档 / 自由拖动缩放。纯模型用例（`CanvasLoadClampsAndDefaults`、`CanvasPlaceMapsToPixels`、`CanvasStackedOrderSortsByZ`、`CanvasApplyWritesNormalizedRectBack` 及全部 grid / 外观 / 变体用例）继续通过。
- 新增用例对应 §6.3 WP-L3 的断言（3 份种子文档 = 含隐藏块 `download` / 含自建组件 `launch` / 含插件块与白名单外块 `plugins`，每份都跑一遍）：
  - 块集合不变 → `DerivedGridKeepsEveryBlockOrdersByYThenXAndClampsSpans`
  - 先 y 后 x → 同上 + `DerivedPositionsArePinned`（逐块钉死 x / y / span）
  - span 在合法范围 → 同上 + `SpanOfRoundsAndClamps`、`MinColsOfWidensNarrowBlocksAndWrapsTheRow`
  - 读进来再原样写出去逐字节相同 → `ReadThenWriteIsByteIdenticalAndStaysCanvas`、`RenderingDerivationNeverTouchesTheDocumentOrTheFile`（真文件：落盘 → Load → 跑完整渲染推导 → 文件字节与 `ToJson()` 都不变）
  - 编辑器提交一次后变 grid，id / hidden / widgets 都在 → `CommittingOnceInTheEditorWritesGridAndKeepsIdsHiddenAndWidgets`（同时断言未知字段、方案原样、提交后位置 = 提交前看到的位置）、`CommitAfterADragKeepsTheUsersNewGeometry`
  - `layout_apply mode=canvas`：返回成功 + 提示 + 落盘 grid → `ApplyModeCanvasIsTreatedAsGridAndSaysSo`、`ApplyOnAStoredCanvasPageDerivesGridGeometryAndKeepsBlocks`、`ApplySchemaOnlyOffersStackAndGrid`
  - 其它：`NonCanvasPagesAreLeftAlone`、`EmptyCanvasPageBecomesAnEmptyGrid`、`AppendedWidgetOnACanvasPageLandsLastInTheDerivedGrid`

命令（PowerShell，环境变量照续跑须知设好；脚本在被忽略的 `windos\scratch\wp-l3-run.ps1`）：
```powershell
cd D:\项目\TipCL\_r34\l3\windos
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1 -nodeReuse:false
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1 -nodeReuse:false -p:Premium=true
dotnet test TipCL.sln -c Release --no-build -nodeReuse:false
```
（最后一次构建是 compact，体积与测试都在 compact 上测。）

## 4. 词表

离线检查（`windos\scratch\wp-l3-i18n.ps1`）：4 个改过的 .cs 里 `I18n.L("…")` 字面量 = 0 个，**没有新增、也没有废弃的 key**。
`LayoutTools.CanvasNotice` 与原有 `layout_apply` 的所有返回文案一样是给 AI 模型看的工具结果，不走 `I18n.L`（与现状一致）。

## 5. 合并时需要主 agent 处理的事

1. **（WP-L12）卡片手柄**：`LayoutEditor.RenderBox` 里 `card.SetBoxMode(page.Mode)`（`LayoutEditor.cs:774`）对 canvas 页仍传 `"canvas"`，卡片会画 8 向手柄（`:3345`、`:3414`）。改成传 `_box.Mode`（推导后恒为 grid），或 `LayoutCanvasMigration.IsCanvas(page) ? LayoutSchema.ModeGrid : page.Mode`。WP-L12 重写手柄时一起处理即可。功能上不坏：网格分支的 `LayoutGrid.ResizeCell` 认 n/w 手柄。
2. **（WP-L12，推荐）进编辑态就转成 grid、没动过就还原**：`PrepareEditableGrid`（`LayoutEditor.cs:435`）对 canvas 页照 stack 的做法：`_stackBackup = page.Clone(); _stackConverted = true; LayoutCanvasMigration.ConvertInPlace(page, id => MinColsOf(id, LayoutGrid.Columns(page)));`，`RestoreStackAfterEdit` 已经会在「整场没动过」时把 mode / cols / 块原样还原。这样方向键微调（`LayoutEditSession.Nudge`）和「添加」（`PlaceAppendedBlock`）也都在 grid 上做。**不接这一条也能用**：渲染走推导，拖动 / 改宽提交时经 `ApplyTo` 写成 grid；只是方向键微调与添加组件仍在 canvas 坐标上改（页面保持 canvas，推导渲染照常、顺序一致）。
3. **（WP-L12）完成编辑无条件落盘**：`ExitEdit`（`LayoutEditor.cs:626`）对非原生页总是 `Persist()`。canvas 页「进编辑 → 什么都不动 → 完成」不会改 mode 和几何（仍是 canvas），但文件会重写一次（`saved_at`、z 序号重排）。若要严格做到「提交前文件一个字节都不动」，`ExitEdit` 在 `!_layoutTouched` 时跳过 `Persist()`（stack 页现有行为同样如此，属于既有行为，不是本包引入）。
4. **（WP-L12）列表 / 网格分段切换**：当前页是 canvas 时分段应显示为「网格」选中；用户点「列表」时先 `LayoutCanvasMigration.ConvertInPlace(page)` 再设 `Mode = stack`（与 LayoutTools 同一口径），点「网格」直接 `ConvertInPlace`。
5. **（WP-L4）设置 → 布局的页面卡片**：缩略图与摘要遇到 canvas 页时用 `LayoutCanvasMigration.ToGridPage(page)` 画，摘要写「网格」，不要出现「自由画布」。
6. **退役代码的真正删除**（L12 去掉「吸附 N」按钮与 8 向手柄之后）：`LayoutCanvas.Steps / HandleNames / HandleNone / HandleSize / StepOf / IsKnownStep / Snap / HandleAt / Move / Resize / Load / Place / Rects / Apply`、`LayoutCell.NX/NY/NW/NH/ApplyCanvas`、`GridCanvasPanel` 的全部 `IsCanvas` 分支、`LayoutEditSession.Nudge` 的 canvas 分支、`LayoutEditor.CycleSnapStep`，连同 6 个 Skip 用例与 `LayoutEditorTests.NudgeCanvasUsesSnapStepAndShiftUsesTenPixels`、`LayoutWidgetLifecycleTests.PlaceAppendedBlockStartsNewFullWidthRowInGridAndSmallCardOnCanvas` 的 canvas 半段一起删。`LayoutCanvas.FallbackMinW / FallbackMinH / RefHeight / StackedOrder` 网格也在用，**不能删**。写进 DECISIONS（见 D-6）。
7. **（WP-L6）文档**：`docs/布局使用指南.md` 的模式说明只剩列表 / 网格，加一句「旧版保存的自由画布页会自动按网格显示，编辑一次后保存为网格」。
8. **（WP-L7，下一轮）Linux 树**：`LayoutCanvasMigration` 是纯 C#，可原样移植到 `src/TipCL.App`。
9. 冲突面：本包只改了 4 个独占文件；`LayoutEngine.cs` 的新增全是**加成员**（没改任何既有公开签名），与 WP-L12「不改 LayoutEngine 公开签名」的约定不冲突。

## 6. 决定（合并时请从 D-82 起编号）

- **D-1 推导函数放在 Core**：方案原写在 `LayoutEngine.cs`（Pages），但 AI 工具 `LayoutTools` 在 Core、不能引用 Pages；为了只有一份实现，纯函数 `LayoutCanvasMigration` 放进 `Core/Launcher/Layout/LayoutSchema.cs`（本包独占文件，命名空间 `TipCL.Core.Launcher.Layout`，Pages 早已引用）。Pages 侧只加一个提交用的薄封装 `LayoutGrid.CommitCanvasAsGrid`（它要用 Pages 的 `LayoutCell`）。
- **D-2 列宽下限 = min(cols, max(2, MinCols))**：2 与编辑器 `SanitizeCellWidths` 的「全局 2 列」兜底同口径。画布 Panel 不认识 `BlockSpec.MinCols`，所以渲染推导只按 2 兜底，各块自己的 MinCols 仍由编辑器渲染兜底撑宽、提交时不写回（与现有 grid 行为一致）；需要按 MinCols 推导的调用方传 `minColsOf`。
- **D-3 行流、左对齐、每块 1 行**：只用 x / w 推出「顺序 + 列宽」（§5.3 原文），不保留 x 偏移，canvas 的 h 不映射成行跨度（行高按内容自适应，`height_px` 原样）。换行条件 = 放不下 或 上沿已在本行最矮块下沿之下。排序严格按 (y, x)（不做 y 分带），与验收里的「先 y 后 x」一字不差。
- **D-4 隐藏块**写成契约默认矩形 (0,0,1,1) + 推导 span，重新显示时由网格按 span 自动流到空位。
- **D-5 提交时块表按 (y, x) 重排**、z 重新编号（网格里块表顺序就是视觉顺序，以后切回列表也是这个顺序）。
- **D-6 退役成员只加注释，不加 `[Obsolete]` 特性**：`LayoutEditor.cs` 与现有测试仍在用 `Steps / HandleNames / StepOf / Move / Resize`，加特性会引出 CS0618 新增警告，违反「0 新增警告」。真正删除留到合并后（清单见 §5 第 6 条）。
- **D-7 `PlaceAppendedBlock` 的 canvas 分支**保留 0.4×0.25（`LayoutWidgetLifecycleTests` 钉着，不是本包的测试），位置改成 (0.6, min(最靠下块的 y, 0.75))，推导后落在页尾；`Nudge` 的 canvas 分支语义不动（`LayoutEditorTests` 钉着）。
- **D-8 LayoutTools 只按 `mode` 参数收敛**：`mode=canvas`（不区分大小写）→ grid + 提示；目标页是 canvas 且给了任何 `mode` 时先 `ConvertInPlace`（否则 0.5 这类归一化坐标会被当成第 0 列 / 第 0 行）。整份 `json` 提交里的 canvas 页**不转换**：那条路也用来把「AI 备份」原样恢复回去，转换会让恢复不再是原样（Q1：文档原样保留，用户在编辑器里提交才改写）。
- **D-9 Schema 契约不动**：`ModeCanvas` 仍是合法 mode，Sanitize 仍按 canvas 钳位 —— 这是「读进来再原样写出去逐字节相同」的前提。

## 7. 待真机（需要用户在场启动 TipCL.exe；本包一次都没跑）

准备（用临时数据目录，不碰真实数据）：
```powershell
cd D:\项目\TipCL\_r34\l3\windos   # 或合并后的 r34/base 工作区
$env:TIPCL_HOME = "$env:TEMP\tipcl-l3-check"
# 先空跑一次让它建好目录:
.\src\TipCL\bin\Release\net48\TipCL.exe --smoke
# 把下面这份种子存成布局数据目录里的 ui_layout_v2.json
# (目录 = 设置 → 布局 →「打开所在文件夹」,即 LayoutStoreV2.DirPath),存好后先记一次 Get-FileHash
```
种子用联机页的**真实块 id**(header / state / room / actions / players / nodes)。单测里的三份种子用的是虚构 id,
放到真机上会被当成「白名单外的块」原样保留、不渲染,不适合拿来看画面:
```json
{
  "kind": "tipcl-layout", "version": 2,
  "layout": {
    "pages": { "multiplayer": { "version": 2, "mode": "canvas", "cols": 12, "blocks": [
      { "id": "header",  "x": 0,    "y": 0,    "w": 1,    "h": 0.15 },
      { "id": "room",    "x": 0.5,  "y": 0.2,  "w": 0.5,  "h": 0.3 },
      { "id": "state",   "x": 0,    "y": 0.2,  "w": 0.5,  "h": 0.3 },
      { "id": "actions", "hidden": true, "x": 0, "y": 0.55, "w": 1, "h": 0.1 },
      { "id": "players", "x": 0,    "y": 0.7,  "w": 0.66, "h": 0.25 },
      { "id": "nodes",   "x": 0.66, "y": 0.7,  "w": 0.34, "h": 0.25 },
      { "id": "custom:w-note1", "x": 0, "y": 0.96, "w": 0.4, "h": 0.04 }
    ] } },
    "widgets": [ { "id": "w-note1", "type": "note", "title": "便签", "settings": {} } ]
  }
}
```
期望的推导结果(12 列):说明整行 / 联机状态 6 + 房间 6 / 玩家 8 + 节点说明 4 / 便签 5;「快捷操作」不显示(隐藏)。

1. canvas 页按网格显示:`TipCL.exe --shot multiplayer`。画面如上;`ui_layout_v2.json` 运行前后 `Get-FileHash` 相同(只看不改)。
2. 自建组件 / 插件块:同一张图里便签块在最后一行、宽 5/12;在种子里再加一块 `{ "id": "plugin:hello:farm", "x": 0.5, "y": 0.96, "w": 0.5, "h": 0.04 }` 重拍,插件没装时应显示「不可用」占位、不报错,排在便签右侧(5 + 6 列)。
3. 编辑态(依赖 WP-L0 的 `--edit` / `--select`):`TipCL.exe --shot multiplayer --edit --select room`,确认是网格手柄、没有 8 向手柄(依赖 §5 第 1 条接线)。
4. 提交一次才改写(人工):联机页右键「自定义布局…」→ 把「房间」的右边缘往左拖 → 完成;打开 `ui_layout_v2.json`,`multiplayer` 页变成 `"mode": "grid"`,7 个块 id 都在、`actions` 仍是 `"hidden": true`、`widgets` 里的便签还在;再进编辑按一次 Ctrl+Z,页面回到拖动前的样子。
5. 进编辑不动就退出(人工,先把种子放回去):文件里仍是 `"mode": "canvas"`、几何不变(`saved_at` 可能变,见 §5 第 3 条)。
6. AI:在 AI 页说「把联机页改成 canvas 布局」,回复里应有「提示:自由画布(canvas)模式已停用,已按网格(grid)处理」,文件里 `multiplayer` 页是 `grid`,方案里多一份「AI 备份 …」且里面是原来的 canvas。
7. 回归：`TipCL.exe --i18n-check`（本包无新 key，期望缺失 = 0）、`TipCL.exe --smoke`。

## 8. 清单外需要改、但没改的文件

- `windos/src/TipCL/Pages/Layout/LayoutEditor.cs`（WP-L12 独占）：§5 第 1–4 条的接线。都不是「不改就坏」，现状下 canvas 页已能按网格显示、拖动提交即写成 grid。
- `windos/src/TipCL/Pages/SettingsLayoutPage.cs`（WP-L4 独占）：§5 第 5 条，canvas 页的缩略图 / 摘要。
- `windos/tests/TipCL.Tests/LayoutEditorTests.cs`、`LayoutWidgetLifecycleTests.cs`：退役的 canvas 断言等合并后与代码一起删（§5 第 6 条）；本包没有让它们变红。

## 9. 风险

- 推导是确定性的近似：自由画布里刻意错开 / 重叠 / 留空的排法会变成左对齐的行流（顺序与列宽保住，x 偏移和高度比例不保留）。真机第 1、2 条要人眼看一下老用户最常见的 canvas 页是否可接受。
- 严格按 (y, x) 排序：两块 y 只差一点点、x 顺序相反时（例如 A 在 (0.5, 0.00)、B 在 (0.0, 0.01)），推导结果是 A 在左、B 在右，与画布上的左右相反。这是验收「先 y 后 x」的字面要求，如需改成「按 y 分带再按 x」要另行拍板。
- `PlaceAppendedBlock` 的 canvas 新位置在已有块的 y > 0.75 时可能不是页尾（新块被钳到 0.75）。只影响「还没接 §5 第 2 条时在 canvas 页上添加组件」这一种情况。
- 方向键微调在未接 §5 第 2 条时仍按 canvas 坐标走，用户在推导网格上按方向键，看到的效果是顺序偶尔变化而不是逐格移动。
- 6 个 Skip 用例覆盖的代码仍在编译、但界面上不可达；合并后删代码时要连同测试一起删，避免只删一半。
