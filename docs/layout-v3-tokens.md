# 布局 v3 设计令牌(Linux · Avalonia 11)

> R35 lin-l0 交付(2026-11-20)。对应 Windows `r34/l0`(b609ef1,`windos/docs/design-spec.md` §12)。
> 数值以 `windos/docs/mockups-hark/v3/layout3.html` / `pages3.html` 的 CSS 为准;键名与 Windows 同名同值。
> 消费方:lin-l12(编辑态卡片 / 编辑条 / 「添加」抽屉)、lin-l4(设置 → 布局)、lin-l5(启动页深色玻璃)。

## 0. 做法:ControlTheme(不是 Style Classes)

Linux 树的 `Themes/Controls.axaml` 本来就全是 keyed `ControlTheme`(`Btn.*` / `Seg.*` / `T.*` …),
v3 的 `Layout.*` 沿用同一做法,**键名就是 R35-BRIEF §3 约定的名字**:

```csharp
// 推荐:走门面,找不到返回 false / 兜底值,永不抛异常
LayoutVisualTokens.ApplyTheme(bar, "Layout.EditBar", onDark: pageId == "launch");   // 先找 Layout.EditBar.OnDark,没有退回普通版
IBrush fill  = LayoutVisualTokens.BrushOr("B.EditDropFill");                     // 资源 → 登记兜底 → 透明
LayoutVisualTokens.DynOr(border, Border.BackgroundProperty, "B.EditBarBg");      // 动态跟随明暗;资源缺失时一次性设兜底色
Path eyeOff  = LayoutVisualTokens.Glyph("Geo.EyeOff", 14, owner: button);        // 16 视框等比缩到 14,线宽 1.6×14/16,描边跟随按钮前景

// 或者自己取(同样不抛):
if (Application.Current?.TryGetResource("Layout.Drawer", null, out var t) == true && t is ControlTheme ct) drawer.Theme = ct;
```

- **状态**:只有 `Layout.EditOutline*` 有状态,走 Classes —— `hover` / `selected` / `drag`
  (`LayoutVisualTokens.OutlineClasses`)。其余状态用控件自带伪类(`:pointerover` / `:pressed` / `:checked` / `:disabled`)。
- **深色玻璃**:键名后加 `.OnDark`(`LayoutVisualTokens.ThemeKeys.OnDark(key)`)。L12 在编辑器内部按 `pageId == "launch"` 决定(R35-BRIEF §3 第 4 条)。
- **本地值优先**:Avalonia 里本地值 > 主题 setter。套主题后不要再手写同一属性(例如 `Border.Background`),否则主题失效。
- **文字**:Border 主题里**不设** `TextElement.*` 附加属性(避免语法差异);文字一律另套 `Layout.*Text` / `Layout.EditHint` 等 TextBlock 主题。
- **阴影**:Avalonia 没有 DropShadowEffect,用 `BoxShadows` 资源(`Fx.*`)赋给 `Border.BoxShadow`。
- 资源挂载:`Ui.EnsureMounted()` / `Ui.ApplyTheme(dark)` 把 Palette + Controls.axaml 挂到 Application 级(MainWindow 构造时已做)。

## 1. 画刷(`Themes/Palette.Light.axaml` / `Palette.Dark.axaml` 成对)

| 键 | 浅色 | 深色 | 用途(效果图) |
|---|---|---|---|
| **B.EditOutline** | `#29000000` | `#2EFFFFFF` | 块的 1.5px 灰色虚线框(`--page-line2` = rgba(0,0,0,.16));编辑条分隔线 |
| **B.EditOutlineHover** | `#57000000` | `#5CFFFFFF` | 虚线框悬停加深(rgba(0,0,0,.34)) |
| **B.EditOutlineOnDark** | `#59FFFFFF` | 同左 | 启动页英雄图上的白色半透明虚线 |
| B.EditOutlineHoverOnDark | `#B3FFFFFF` | 同左 | 同上,悬停 |
| **B.EditDropFill** | `#E7F5EE` | `#1B3A2C` | 落点框主题色浅底(= B.AccentSoft) |
| B.EditDropFillOnDark | `#1FFFFFFF` | 同左 | 深色背景上的落点框 |
| **B.EditColShade** | `#09000000` | `#0AFFFFFF` | 拖动 / 改宽时的列底纹(rgba(0,0,0,.035)) |
| B.EditColShadeOnDark | `#0FFFFFFF` | 同左 | |
| **B.EditBarBg** | `#FFFFFF` | `#2B2B2B` | 编辑条、浮动标签、块上图标按钮、抽屉、右边缘手柄的底 |
| B.EditBarLine | `#14000000` | `#1AFFFFFF` | 以上元素的 1px 细描边(`--page-line`) |
| B.EditItemBg | `#F2F2F2` | `#313131` | 抽屉项 / 抽屉说明框灰底(`--page-paper2`) |
| B.EditSegRail | `#E7E7E7` | `#1E1E1E` | 「列表 / 网格」分段轨道(`--page-paper3`) |
| B.EditSegOn | `#FFFFFF` | `#3A3A3A` | 分段选中项 |
| **B.EditDim** | `#800A0E16` | 同左 | 深色启动页编辑态压暗遮罩 |
| B.EditGlass | `#B80E121B` | 同左 | 深色玻璃(编辑条 / 标签 / 图标按钮 / 抽屉) |
| B.EditGlassLine | `#24FFFFFF` | 同左 | 玻璃描边 |
| B.EditGlassChip | `#24FFFFFF` | 同左 | 玻璃上的小条(锁形提示、分段轨道) |
| B.EditGlassChipLine | `#38FFFFFF` | 同左 | |
| B.OnEditGlass | `#FFFFFF` | 同左 | 玻璃上的白字 / 白色实线 |
| B.OnEditGlassMuted | `#A8FFFFFF` | 同左 | 玻璃上的次要白字 |
| **B.WarnSoft** | `#FDF3E2` | `#3A2F1C` | 琥珀浅底(v2 已有键,沿用) |
| **B.WarnInk** | `#B45309` | `#EBB45E` | 琥珀字色(`--page-warn`) |

粗体 = R35-BRIEF §3 契约键(9 个)。`…OnDark` / `B.EditGlass*` / `B.OnEditGlass*` / `B.EditDim` 明暗同值(只用在永远深色的英雄图上)。
新键一律把透明度写进 `#AARRGGBB`(不用 `Opacity` 属性),代码内兜底(`LayoutVisualTokens.FallbackLight/Dark`)与色板逐字一致,单测锁住。

## 2. 阴影(`BoxShadows`,成对)

| 键 | 浅色 | 深色 | CSS |
|---|---|---|---|
| Fx.EditBar | `0 1 3 0 #0F000000` | `0 2 10 0 #59000000` | `.ebox` 0 1 3 rgba(0,0,0,.06) |
| Fx.EditLabel | `0 2 8 0 #1F000000` | `0 2 8 0 #66000000` | `.lab` / `.acts button` 0 2 8 rgba(0,0,0,.12) |
| Fx.Drawer | `-12 0 32 0 #24000000` | `-12 0 32 0 #80000000` | `.drawer` -12 0 32 rgba(0,0,0,.14) |
| Fx.SegOn | `0 1 2 0 #14000000` | `0 1 2 0 #4D000000` | `.sg2 button.on` 0 1 2 rgba(0,0,0,.08) |
| Fx.ResizeGrip | `0 1 4 0 #33000000` | `0 1 4 0 #80000000` | `.rz` 0 1 4 rgba(0,0,0,.2) |

## 3. 矢量图标(`Controls.axaml`,`StreamGeometry`,16×16 视框、只描边)

| 键 | 效果图显示尺寸 | 说明 |
|---|---|---|
| Geo.Eye | 14 | 抽屉 / 设置页签「显示」 |
| Geo.EyeOff | 14 | 内置块「隐藏」(块右上角) |
| Geo.Lock | 13(锁形提示 12) | 「设置」页签不能隐藏、启动页「主区域固定」 |
| Geo.Undo / Geo.Redo | 15 | 编辑条撤销 / 重做(路径同 layout3 SVG) |
| Geo.Grip | 12 | 浮动标签里的六点拖拽(0.1 长短线段 + 圆头线帽 = 圆点;线宽 2.2) |
| Geo.ListMode / Geo.GridMode | 13 | (契约外)编辑条「列表 / 网格」分段 |

线宽口径 = 效果图 `svg.i { stroke-width:1.6 }`(按 16 视框,随显示尺寸缩放)。`LayoutVisualTokens.Glyph(key, size, owner?)` 已按此缩放;
直接在 XAML 用时套 `Layout.Glyph`(16 显示 / 1.6)或 `Layout.Grip`。资源里的几何是共享实例 —— 要改 `Transform` 先 `Clone()`(`GeometryOf` 已做)。
垃圾桶沿用全站 IconLib 的现有字形(不新增)。

## 4. ControlTheme(`Controls.axaml`)

| 键(.OnDark = 有深色玻璃版) | TargetType | 要点 |
|---|---|---|
| **Layout.EditBar** (.OnDark) | Border | 高 46、圆角 12、内距 14/8、B.EditBarBg + B.EditBarLine 1px + Fx.EditBar |
| Layout.EditBarTitle (.OnDark) | TextBlock | 13 ExtraBold,省略号 |
| Layout.EditHint (.OnDark) | TextBlock | 12 B.InkMuted,不换行省略号 |
| Layout.EditBarSep (.OnDark) | Border | 1×18,B.EditOutline |
| Layout.Count / Layout.CountText | Border / TextBlock | 「添加」角标:16 高、圆角 8、B.Accent;字 12 粗 B.OnAccent |
| Layout.SegRail (.OnDark) | Border | 轨道圆角 12、内距 3、B.EditSegRail |
| **Layout.SegItem** (.OnDark) | ToggleButton(RadioButton 亦可) | 高 24、内距 10、圆角 8;`:checked` 白底(B.EditSegOn)+ 粗体 + Fx.SegOn |
| **Layout.EditLabel** (.OnDark) | Border | 高 24、圆角 8、内距 6/10、左 8 上 -14、Fx.EditLabel |
| Layout.EditLabelText (.OnDark) | TextBlock | 12 粗体(标题) |
| Layout.EditLabelNote (.OnDark) | TextBlock | 12 常规 B.InkMuted(「· 组件」) |
| Layout.Grip (.OnDark) | Path | Geo.Grip,线宽 2.2 |
| **Layout.IconAct** (.OnDark) | Button | 26×24、圆角 8、B.EditBarBg + B.EditBarLine + Fx.EditLabel;悬停字色 B.Ink |
| Layout.IconAct.Danger (.OnDark) | Button | 悬停整块 B.CaptionHot(#C42B1C)+ 白字(custom: / plugin: 的垃圾桶) |
| Layout.EditOutline (.OnDark) | Rectangle | 叠在块上方;Margin -4、圆角 12、1.5px 虚线 3,2;Classes `hover` 加深、`selected`/`drag` 2px 主题色实线(OnDark:白) |
| Layout.DropBox (.OnDark) | Rectangle | 落点框:B.EditDropFill + 1.5px B.Accent 虚线,圆角 12 |
| Layout.ColShade (.OnDark) | Border | 列底纹:B.EditColShade,圆角 8 |
| Layout.ResizeGrip / .Bottom | Thumb | 10×36 / 36×10 胶囊,中间 2px 灰条,`:pressed` 变主题色(外伸 9 由使用方 Margin) |
| Layout.EditTip / Layout.EditTipText | Border / TextBlock | 「N / 列数」气泡:B.Ink 底、B.Paper 字、圆角 8 |
| **Layout.Drawer** (.OnDark) | Border | 宽 316、右对齐、左 1px 线、Fx.Drawer;滑入动画由使用方做 |
| Layout.DrawerTitle | TextBlock | 15 ExtraBold |
| Layout.DrawerSection | TextBlock | 12 B.InkMuted,上 10 下 6 |
| Layout.DrawerItem | Border | 内距 8、圆角 12、B.EditItemBg、下距 6 |
| Layout.DrawerIcon | Border | 40×40、圆角 8、B.EditBarBg |
| Layout.DrawerEmpty | Rectangle | 1px 虚线 4,3 B.EditOutline、圆角 12(空状态框垫底) |
| Layout.DrawerNote | Border | 内距 12/10、圆角 12、B.EditItemBg |
| Layout.WarnNote / Layout.WarnText | Border / TextBlock | 琥珀提示条(设置 → 布局 → 高级):B.WarnSoft 底、B.WarnInk 字、圆角 8 |
| Layout.LockNote | Border | 启动页「🔒 主区域固定…」玻璃小条(B.EditGlassChip),右上 |
| Layout.Glyph / Layout.Glyph.Small | Path | 16 显示、线宽 1.6 / 1.8、圆头 |

粗体 = 契约主题(5 个)。圆角只用 8 / 12(唯一例外:2px 灰条的 1)。

## 5. 尺寸常量(`LayoutVisualTokens`)

`EditBarHeight 46`、`EditBarGap 12`、`EditBarPadding 14,0,8,0`、`EditLabelHeight 24`、`EditLabelMargin 8,-14,0,0`、
`IconActWidth 26`、`IconActHeight 24`、`IconActGap 4`、`IconActsMargin 0,-14,8,0`、`OutlineInset 4`、`OutlineThickness 1.5`、
`OutlineSelectedThickness 2`、`OutlineDash {3,2}`、`ResizeGripWidth 10`、`ResizeGripHeight 36`、`ResizeGripOutset 9`、`DrawerWidth 316`、
`DrawerIconSize 40`、`SegItemHeight 24`、`RadiusCtl 8`、`RadiusCard 12`、`HeroEditOpacity 0.45`、`GlyphStroke16 1.6`、`GripStroke16 2.2`、
`GlyphSizeUndoRedo 15`、`GlyphSizeIconAct 14`、`GlyphSizeLock 13`、`GlyphSizeGrip 12`、`GlyphSizeSeg 13`。

## 6. 截图开关(`Session/SmokeRunner.cs`,只写代码,真机才跑)

跟在 `--smoke` 后面(命令行优先,环境变量兜底):

| 开关 | 环境变量 | 作用 |
|---|---|---|
| `--shot <页>` / `--page <页>` | `SMOKE_PAGE` | 截哪一页;`settings:layout` = 设置 → 布局,`settings:<子页>` 按入口清单选内层视图 |
| `--edit [页键]` | `SMOKE_EDIT=1\|页键` | 进编辑态(页键缺省 = 当前页第一个可编辑宿主) |
| `--select <blockId>` | `SMOKE_SELECT` | 模拟选中(拍浮动标签 / 图标按钮 / 手柄),隐含 `--edit` |
| `--pop layout-drawer` | `SMOKE_POP` | 打开「添加」抽屉,隐含 `--edit` |
| `--expand advanced` | `SMOKE_EXPAND` | 设置 → 布局展开「高级」(没给页时自动 `settings:layout`) |
| `--dark` | `SMOKE_DARK=1` | 深色 |
| `--out <png>` | — | 输出文件(缺省 smoke.png) |

选中 / 抽屉的正式入口归 lin-l12:`SmokeRunner.SelectBlockHook` / `OpenAddDrawerHook` 合并后接线;未接线时按名字反射
(`SelectBlock`/`SelectForShot`/`Select`(string),退到 `_session.Select + UpdateSelectionVisual`;`OpenAddDrawer`/`ShowAddDrawer`/`OpenDrawer`/`ShowDrawer(true)`)。
一个开关都不给时行为与原 `--smoke` 逐字节相同。
