# R35 lin-l0 · 布局 v3 WP-L0 落地到 Linux 树(令牌 / 矢量图标 / 样式 / 截图开关)

> 分支 `r35/lin-l0`(工作区 `D:\项目\TipCL\_r35\lin-l0`,起点 `r35/lin-base` 7039719)。2026-11-20。
> 规格:`windos/GOAL-layout-v3.md` §4.3、§6.3「WP-L0」、§6.4 第 1 条;R35-BRIEF §3「跨包接口」第 1 条。
> 参考:Windows `r34/l0`(b609ef1)。**未启动程序**,截图类全部列在「待真机」。

## 1. 做了什么

1. **色板成对**:`Themes/Palette.Light.axaml` / `Palette.Dark.axaml` 各加 22 个画刷 + 5 个 `BoxShadows` 阴影,键名、取值与 Windows r34/l0 一致
   (Windows 的 `DropShadowEffect` → Avalonia `BoxShadows`,浅色档按 layout3.html CSS 原值重算)。`B.WarnSoft` 沿用 v2 已有键。
2. **矢量图标**:`Themes/Controls.axaml` 加 `Geo.Eye / EyeOff / Lock / Undo / Redo / Grip`(契约 6 枚)+ `Geo.ListMode / GridMode`(额外 2 枚),
   `StreamGeometry`、16×16 视框、描边绘制;Undo/Redo 用 layout3 SVG 原路径(与基线 `LayoutEditChrome.UndoData` 同形,Windows 版整体上移了 1)。
3. **样式**:`Controls.axaml` 加 33 个 `Layout.*` ControlTheme + 16 个 `.OnDark` 深色玻璃版本;契约 5 个(`Layout.EditBar / EditLabel / IconAct / Drawer / SegItem`)全在。
   做法 = **ControlTheme**(与本文件既有 `Btn.* / Seg.* / T.*` 一致),状态只在 `Layout.EditOutline*` 上走 Classes `hover / selected / drag`。
4. **令牌门面** `Pages/Layout/LayoutVisualTokens.cs`:键名常量(`BrushKeys / ShadowKeys / GeoKeys / ThemeKeys / OutlineClasses`)、尺寸常量、
   与色板逐字一致的兜底表、取用工具 `Resource / BrushOr / ColorOr / DynOr / ShadowOr / Theme / ApplyTheme / GeometryOf / Glyph` —— 全部 TryGetResource 式、不抛异常。
   原有 `AccentColor()` 一字未改(GridCanvasPanel / GuideLayer 在用)。
5. **键名文档** `docs/layout-v3-tokens.md`(新建):做法、键表(浅 / 深 / CSS 出处)、尺寸、截图开关,供 lin-l12 / lin-l4 / lin-l5 按键名消费。
6. **截图开关**(只写代码不运行)`Session/SmokeRunner.cs`:`SmokeShotOptions` 纯函数解析 `--shot/--page`、`--edit [页键]`、`--select <blockId>`、
   `--pop layout-drawer`、`--expand advanced`、`--dark`、`--out`(及 `SMOKE_EDIT / SMOKE_SELECT / SMOKE_POP / SMOKE_EXPAND` 环境变量),
   支持 `settings:layout` / `settings:<子页>`;选中 / 抽屉留 `SelectBlockHook` / `OpenAddDrawerHook` 钩子 + 按名字反射兜底;不给开关时行为逐字节不变。
   `Session/CommandLine.cs` 不需要改(那是启动 CLI,`--smoke` 由 Program.cs 直接交给 SmokeRunner,开关在 SmokeRunner 里读 `Environment.GetCommandLineArgs()`)。
7. **测试** `tests/TipCL.Tests/Layout/LayoutV3TokensTests.cs`(新建,29 例,含 Theory 展开):契约键清单、两色板键齐全且类型对、兜底值 = 色板值(画刷 / 阴影)、
   玻璃键明暗同值、效果图关键数值、Controls 几何齐全且兜底串逐字一致且在 16 视框内、主题与 .OnDark 齐全、新段落圆角只 8/12 且 DynamicResource 都在两色板、
   运行期每个键类型正确、每个主题能套到 TargetType 且关键尺寸生效、EditOutline 的 Classes 切换虚实、ApplyTheme 不抛、Glyph 缩放与线宽、Glyph 跟随按钮前景、
   截图开关 7 例(含反射兜底)。

## 2. 对照 GOAL 的落地点

| GOAL 条目 | 落地(文件:行) |
|---|---|
| §4.3 图标:眼睛 / 眼睛划线 / 锁 / 撤销 / 重做用矢量 Path,统一放进 L0 样式 | `Themes/Controls.axaml:487-495`(Geo.*)、`:498-527`(Layout.Glyph / Glyph.Small / Grip / Grip.OnDark);`LayoutVisualTokens.cs:117`(GeoKeys)、`:488`(GeometryOf)、`:513`(Glyph,线宽 1.6×size/16) |
| §4.4 色板成对改 | `Palette.Light.axaml:85-116`、`Palette.Dark.axaml:84-114`;`ThemeKeysTests.LightAndDark_KeySets_AreIdentical` 仍绿 + `LayoutV3TokensTests.Palette_HasEveryV3BrushAndShadow` |
| §6.3 WP-L0 `Layout.EditOutline`(虚线框) | `Controls.axaml:530-566`(含 .OnDark);`LayoutV3TokensTests.Runtime_EditOutlineClassesSwitchDashedToSolid` |
| §6.3 `Layout.EditLabel`(浮动标签) | `Controls.axaml:596-628`(EditLabel / .OnDark / EditLabelText / EditLabelNote) |
| §6.3 `Layout.EditBar`(编辑条) | `Controls.axaml:745-810`(EditBar / Title / Hint / Sep / Count) |
| §6.3 `Layout.Drawer`(抽屉) | `Controls.axaml:873-929`(Drawer / Title / Section / Item / Icon / Empty / Note) |
| §6.3 `Layout.IconAct`(块上图标按钮) | `Controls.axaml:636-695`(IconAct / .Danger / .OnDark / .Danger.OnDark) |
| §6.3 「以及深色玻璃版本」 | 16 个 `.OnDark`(`ThemeKeys.WithOnDark`,`LayoutVisualTokens.cs:134`)+ `Layout.LockNote`(`Controls.axaml:942`)+ `B.EditGlass* / B.OnEditGlass* / B.EditDim` |
| §6.3 `--edit` / `--pop layout-drawer` / `--select <blockId>` / `--expand advanced` | `SmokeRunner.cs:83-89`(解析)、`:99-106`(子页导航)、`:122-126`(摆拍)、`:175-376`(实现与钩子)、`:1128-1262`(SmokeShotOptions) |
| §6.3 `--design-sheet` 样张 | **Linux 树没有 `--design-sheet` 入口**(Program.cs 只认 `--smoke` / `--ai-selftest` / 启动 CLI),本包未新增(见「决定」D3) |
| §6.4 第 1 条 资源键 + TryFindResource 兜底 | 契约 9 画刷 / 6 几何 / 5 主题全在;门面 `LayoutVisualTokens.cs:364-540` 全部不抛 |
| §5.1 / §5.2 / §5.5 里的数值(1.5px 虚线外扩 4、标签 24、按钮 26×24、手柄 10×36、编辑条 46 圆角 12、抽屉 316、英雄区 45%) | `LayoutVisualTokens.cs:213-253` 尺寸常量 + 对应主题 |

## 3. 文件清单

| 文件 | 改动 |
|---|---|
| `src/TipCL.App/Themes/Palette.Light.axaml` | +31 行(22 画刷 + 5 阴影) |
| `src/TipCL.App/Themes/Palette.Dark.axaml` | +30 行(同上,成对) |
| `src/TipCL.App/Themes/Controls.axaml` | +476 行(8 几何 + 49 ControlTheme) |
| `src/TipCL.App/Pages/Layout/LayoutVisualTokens.cs` | 22 → 544 行(AccentColor 原样保留) |
| `src/TipCL.App/Session/SmokeRunner.cs` | +365 行(行尾保持 CRLF) |
| `docs/layout-v3-tokens.md` | 新建 |
| `tests/TipCL.Tests/Layout/LayoutV3TokensTests.cs` | 新建(29 例) |
| `progress/R35-lin-l0.md` | 本报告 |

`Session/CommandLine.cs` 未改。清单外文件一个没动。
例外:`tests/TipCL.Tests/App/ClickSweepTests.cs` 的 SideEffectWords 加了「主页 / GitHub / issue / 网页 / 浏览器 / 官网 / 链接 / 文档」——这是**主 agent** 在本工作区里改的
(按钮遍历点了联机页「Terracotta 项目主页」,在 Windows 机上真的调起了浏览器),按其要求随本包第二个提交一起提交。本包新写的测试不点击任何按钮、不调外部程序。

## 4. 测试前后

| | 通过 | 失败 | 跳过 | 合计 |
|---|---|---|---|---|
| 开工基线(`build.ps1 -Pkg lin-l0 -Test`,22:29) | 978 | 0 | 7 | 985 |
| 收尾全量(22:57,含新增 29 例) | 1007 | 0 | 7 | 1014 |

没有删除或改动任何既有断言;7 个跳过与基线是同一批(CliSmoke ×5、SmokeIntegration ×1、LayoutMotionAndHeight ×1)。

## 5. 编辑器侧应改为读哪个键(给 lin-l12 / lin-l3 的对照表)

基线(quick-v3 C)里的硬编码 → v3 令牌。L0 **不改这些文件**,只给对照;全部建议经 `LayoutVisualTokens` 取(找不到有兜底)。

| 位置(基线行号) | 现在硬编码 | 改读 |
|---|---|---|
| `LayoutEditor.cs:250-266` `_barHost`(编辑条) | 圆角 12、内距 14/8、MinHeight 46、`B.Paper` 底、`B.Line` 边、BoxShadow 0 1 3 `#10000000` | `ApplyTheme(_barHost, "Layout.EditBar", onDark: pageId=="launch")`(= `B.EditBarBg` / `B.EditBarLine` / `Fx.EditBar`);上距 12 = `EditBarGap` |
| `LayoutEditor.V3.cs:126-133` 分段轨道 | 圆角 8、`B.SegRail` | `Layout.SegRail`(.OnDark)= `B.EditSegRail`、圆角 12 |
| `LayoutEditor.V3.cs` `SegButton(...)`(列表 / 网格) | 自绘 | `Layout.SegItem`(.OnDark);图标 `Glyph("Geo.ListMode"/"Geo.GridMode", 13, owner)` |
| `LayoutEditor.V3.cs:134-137` 提示 | `U.Txt(...,12,"B.InkMuted")` + 省略号 | `Layout.EditHint`(.OnDark) |
| `LayoutEditor.V3.cs:139-148` 撤销 / 重做 | `LayoutEditChrome.IconButton(UndoData/RedoData)`(线宽固定 1.4) | `Glyph("Geo.Undo"/"Geo.Redo", 15, owner: 按钮)`(线宽 1.5) |
| `LayoutEditor.V3.cs:149-156` 竖分隔线 | 1×18、`B.Line` | `Layout.EditBarSep`(.OnDark)= `B.EditOutline` |
| `LayoutEditor.V3.cs:162-176` 「添加」角标 | 字号 11、圆角 999 | `Layout.Count` + `Layout.CountText`(字号 12、圆角 8,= 效果图 `.cnt`) |
| `LayoutEditor.V3.cs:490-508` 抽屉 | 宽 316、圆角 12,0,0,12、边 1,1,0,1、阴影 blur 32 offX -12 `#24000000`、`B.Paper`/`B.Line` | `Layout.Drawer`(.OnDark)= `B.EditBarBg` / `B.EditBarLine` / `Fx.Drawer`、左 1px 边、无圆角(效果图 `.drawer` 贴边) |
| `LayoutEditor.V3.cs:557-565` 空状态框 | 实线 1px `B.Line`、圆角 12 | `Layout.DrawerEmpty`(1px **虚线** 4,3 `B.EditOutline`)垫底 + 文字 |
| `LayoutEditor.V3.cs:613-620` 抽屉项图标框 | 40×40、圆角 8、`B.Paper` | `Layout.DrawerIcon`(`B.EditBarBg`) |
| `LayoutEditor.V3.cs:640-645` 抽屉项 | 圆角 12、内距 8(底色另设) | `Layout.DrawerItem`(`B.EditItemBg`、下距 6) |
| 抽屉标题 / 分节字 | `U.Txt` 自定字号 | `Layout.DrawerTitle`(15 ExtraBold)/ `Layout.DrawerSection`(12 B.InkMuted) |
| `LayoutEditor.cs:2945-2946` `LayoutCard` 构造 | 圆角 10、`B.Accent` 边 | v3 编辑态不铺主题色描边(§5.1),交给 `Layout.EditOutline`;plain 块圆角 `:3426` 的 10 → `RadiusCard` 12 |
| `LayoutEditor.cs:3434-3459` `ApplyEditOutline` | 选中 `B.Accent` 2 实线;否则虚线 3,2 / 1.5,`B.InkMuted`(悬停)/ `B.InkFaint`(常态) | `_outline` 套 `Layout.EditOutline`(launch 用 `.OnDark`),切 Classes `hover` / `selected` / `drag`;颜色变成 `B.EditOutline` / `B.EditOutlineHover` / `B.Accent` |
| `LayoutEditor.cs:3066-3095` 右边缘手柄 `_pill` | 10×36、圆角 8、`B.Paper`/`B.Line`、阴影 0 1 4 `#32000000`、灰条 `B.InkMuted` | 套 `Layout.ResizeGrip`(Thumb 版)或保留 Border 改读 `B.EditBarBg` / `B.EditOutline` / `Fx.ResizeGrip`;外伸 `ResizeGripOutset` 9;下边缘用 `.Bottom` |
| `LayoutEditChrome.cs:21-31` 路径常量 | `UndoData / RedoData / EyeOffData / ListModeData / GridModeData` | `Geo.Undo / Geo.Redo / Geo.EyeOff / Geo.ListMode / Geo.GridMode`(同一串路径) |
| `LayoutEditChrome.cs:34-50` `MakePath` | 线宽固定 1.4 | `LayoutVisualTokens.Glyph(key, size, owner)`(1.6×size/16,= 效果图) |
| `LayoutEditChrome.cs:108-126` `Chip` | 圆角 8、1px、阴影 0 2 8 `#1E000000`、`B.Paper` / `B.Line` | 标签用 `Layout.EditLabel`(.OnDark),块上按钮用 `Layout.IconAct` / `.Danger`(.OnDark);`Fx.EditLabel` = 0 2 8 `#1F000000` |
| 浮动标签里的拖拽点 / 标题 / 「· 组件」 | — | `Glyph("Geo.Grip", 12)` 或 `Layout.Grip`;`Layout.EditLabelText` / `Layout.EditLabelNote` |
| 块右上角眼睛划线 / 垃圾桶 | — | `Layout.IconAct` + `Glyph("Geo.EyeOff", 14, owner)`;垃圾桶 `Layout.IconAct.Danger` + `IconLib.TrashData` |
| `GridCanvasPanel.cs:604-608`(L3)落点 / 网格 | accent α38 网格线、α28 落点底、α200 虚线 4,3 / 1.5 | 落点:`B.EditDropFill` + `B.Accent` 1.5 虚线 3,2(= `Layout.DropBox`);拖动列底纹 `B.EditColShade`(`Layout.ColShade`)—— 用 `ColorOr` 取色进 DrawingContext |
| `GuideLayer.cs:120-157`(L12)尺寸气泡 / 参考线 | accent α230 胶囊白字、α150/α180 参考线 | 「N / 列数」气泡 = `Layout.EditTip`(`B.Ink` 底、`B.Paper` 字、圆角 8);参考线可保留主题色 |
| 启动页编辑态(L12 按 `pageId=="launch"`) | — | 压暗 `B.EditDim`;英雄区 `HeroEditOpacity` 0.45;锁形提示 `Layout.LockNote` + `Glyph("Geo.Lock", 12)` + `Layout.EditLabelText.OnDark` |
| 设置 → 布局 → 高级(L4)琥珀提示 | — | `Layout.WarnNote` + `Layout.WarnText`(`B.WarnSoft` / `B.WarnInk`);「设置」页签锁 `Glyph("Geo.Lock", 13)`、眼睛 `Geo.Eye` / `Geo.EyeOff` |

## 6. 新增 / 废弃词表 key(中文 → 英文)

无。本包没有新增界面文案(令牌 / 主题 / 截图开关的控制台输出不进界面,沿用 SmokeRunner 既有写法不走 `I18nCore.Tr`)。

## 7. 跨包接线需求(合并时由主 agent 处理)

1. **截图钩子**:合并 lin-l12 后,在启动处(或 LayoutAddDrawer 静态构造)接
   `SmokeRunner.SelectBlockHook = (ed, id) => ed.<正式选中入口>(id);`、`SmokeRunner.OpenAddDrawerHook = ed => { ed.<打开抽屉>(); return true; };`。
   不接也能用:反射兜底按名字找 `SelectBlock / SelectForShot / Select(string)`、退到 `_session.Select + UpdateSelectionVisual`;抽屉找 `OpenAddDrawer / ShowAddDrawer / OpenDrawer / ShowDrawer(true)`。
   L12 若改了这些名字,把新名字加进 `SmokeRunner.SelectMethodNames / DrawerMethodNames` 即可。
2. **编辑器换读令牌**:按 §5 对照表(L12 / L3 的文件,本包不碰)。
3. `--shot` 只在 `--smoke` 之后生效(Program.cs 只认 `--smoke`);若要像 Windows 一样单独 `--shot` 启动,需在 `Program.cs`(清单外)加一行路由,本包未做。

## 8. 决定(合并时统一编号为 D-8x)

- **D1 ControlTheme 而非 Style Classes**:Linux `Controls.axaml` 全是 keyed ControlTheme,`Ui.Theme(key)` 也按这个取;Style Classes 需要把 Styles 挂到 Application.Styles,
  与现有挂载链(`Ui.EnsureMounted` 只挂 ResourceDictionary)不一致。唯一用 Classes 的是 EditOutline 的三态。
- **D2 阴影用 `BoxShadows` 资源**(`Fx.*`,Palette 成对):Avalonia 无 DropShadowEffect;浅色按 CSS 原值,深色沿用 Windows 深色档的加重不透明度。
- **D3 不做 `--design-sheet` 样张**:Linux 树没有 design-sheet 入口,新建一个整页样张超出 L0 清单(要动 Program.cs);改为单测里逐个主题实例化 + 套用 + 断言尺寸,
  并在「待真机」里给出用 `--smoke` 拍各页编辑态的命令。若主 agent 要样张,建议合并后另起一个 `SMOKE_PROBE=design-sheet` 分支。
- **D4 Undo / Redo 路径按效果图**(y 起点 3.5),不照搬 Windows 的上移 1 版本;与基线 `LayoutEditChrome` 同形,L12 换读时外观不跳。
- **D5 Border 主题不设 `TextElement.*` 附加属性**:文字另给 TextBlock 主题(`*Text` / `EditHint` / `DrawerTitle` …),避免附加属性 setter 在 Avalonia 版本间的语法差异。
- **D6 新画刷一律 `#AARRGGBB`**(不用 `Opacity`):代码兜底才能与色板逐字比对(单测锁)。
- **D7 分段圆角取整**:效果图 `.sg2` 轨道 8 / 项 6,按 GOAL「圆角只用 8 / 12」取 12 / 8(同 Windows)。
- **D8 新增测试文件** `tests/TipCL.Tests/Layout/LayoutV3TokensTests.cs`:BRIEF 的 L0 行没列测试文件,此文件为本包新建、不与任何包冲突。

## 9. 待真机(用户在场时跑;本轮一次都没跑)

在 Linux 真机 / 构建产物目录下(`TipCL.App` 可执行文件,`--smoke` 走无头 Skia,不弹窗):

```bash
# 编辑态 + 选中「列表」块(拍虚线框、浮动标签、眼睛划线按钮、右边缘手柄)
TipCL.App --smoke --shot download --edit --select list --out layout3-download-edit.png
TipCL.App --smoke --shot download --edit --select list --dark --out layout3-download-edit-dark.png
# 「添加」抽屉打开
TipCL.App --smoke --shot download --pop layout-drawer --out layout3-drawer.png
# 设置 → 布局,「高级」展开
TipCL.App --smoke --shot settings:layout --expand advanced --out layout3-settings-advanced.png
TipCL.App --smoke --expand advanced --dark --out layout3-settings-advanced-dark.png
# 启动页深色玻璃编辑态(依赖 lin-l12 / lin-l5 合并后)
TipCL.App --smoke --shot launch --edit --out pages3-launch-edit.png
# 各页常态 / 编辑态(lin-l5 的页)
for p in multiplayer settings:playtime settings:java; do TipCL.App --smoke --shot $p --out $p.png; TipCL.App --smoke --shot $p --edit --out $p-edit.png; done
```

核对点:
1. 输出里出现 `SMOKE-SHOT: 已进入编辑态 / 已模拟选中 / 已打开「添加」抽屉 / 已展开「高级」`,没有 `SMOKE-SHOT NOTE`(有 NOTE = 钩子未接线或页不可编辑)。
2. 不带任何新开关的 `--smoke`(含 `SMOKE_PAGE` 20 帧回归)与改动前逐字节相同。
3. 明暗两套色板下:编辑条白底细边轻阴影、虚线框 1.5px 外扩 4、选中 2px 主题色、抽屉 316 宽左阴影 —— 与 layout3.html 对照。
4. 在设置里切明暗,已套主题的元素即时跟随(DynamicResource)。
5. `settings:java` / `settings:playtime` 等子页键以 `LayoutBlockEntryCatalog` 实际 PageId 为准(lin-l4 可能调整)。

## 10. 风险

- **主题与本地值冲突**:L12 换读时若既套 `Theme` 又保留 `Dyn(Background, "B.Paper")` 这类本地绑定,本地值会盖掉主题 —— 对照表里每项都要删掉对应的手写属性。
- **反射兜底依赖基线内部名**(`_session` / `UpdateSelectionVisual` / `ShowDrawer`):L12 改名后不报错,只会打印 NOTE、截图里没选中 / 没抽屉;合并时接钩子即可。
- **`SMOKE_PAGE` 带冒号的语义变化**:以前 `SMOKE_PAGE=settings:xxx` 会原样交给 `Navigate`(认不出 → 停在原页),现在拆成「设置页 + 内层视图」。现有回归帧都不带冒号,不受影响。
- **深色取值是推导值**:效果图只画了浅色,深色档与 Windows 同值,真机明暗截图后可能要微调(只改两个 Palette + `LayoutVisualTokens.FallbackDark`,单测会提示两边对齐)。
