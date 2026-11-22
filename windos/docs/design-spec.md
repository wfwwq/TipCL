# TipCL 设计规范（design-spec）v2.0

> 本文件是视觉决策的**唯一真相**，与当前代码（main 分支）逐条对应。
> v1.0 是 ui-rebuild 分支的初版规范；v2 视觉层（WP-A…WP-Z，参照 `docs/mockups-hark/`）落地后
> 多项数值已演进，2026-11-08 本次修订把文档追平代码，并纳入可访问性与对比度修复。
> 历史裁决见文末 §10；v1→v2 差异见 §11。

## 1. 间距栅格

4px 基准。**合法值集合：`{4, 6, 8, 10, 12, 16, 20, 24}`**（6 为半档，供图标-文字等紧凑场景）。

| 用途 | 值 | 落点 |
|---|---|---|
| 页面外边距 | `18,14,18,14`（ScrollBody 下边距 16） | `Pages/PageBase.cs` Body/ScrollBody |
| 页面纵向间距（卡片间） | `10` | PageBase 的 SPanel spacing |
| 卡片内边距 | `12` | `Card` 样式 Padding、`Ui.Card` 默认 |
| Field 上下外距 | `0,4,0,4` | `Ui.Field` |
| Field 标签列宽 / 列距 | `140` / 右距 `10` | `Ui.Field` |
| 标题与内容间距 | `8` | `Ui.Section`、样张 |
| 按钮内边距（常规） | `12,6` | `Btn.Base` |
| 按钮内边距（Chip） | `11,5` | `Btn.Chip` |
| 输入框内边距 | `10,7`，MinHeight 32 | `Input`/`Pw` |

## 2. 字号阶梯

**合法集合：`{11, 12, 12.5, 13, 13.5, 15, 17, 20, 21, 34}`**。
12.5/13.5 是 chrome 半档（分组标题/页签）；21 仅页面标题；34 仅启动页英雄区大标题（游戏启动器范式，v2 有意保留）。

| 令牌 | 字号 | 字重 | 用途 |
|---|---|---|---|
| `T.Small` | 11 | Regular | 小注、脚注、时间戳、Tag 内文 |
| `T.Muted` | 12 | Regular | 辅助说明、hint |
| `T.GroupTitle` | 12.5 | SemiBold | 卡片外上方灰色分组标题（InkMuted） |
| 正文（默认） | 13 | Regular | 正文、按钮、输入框、列表行 |
| `Tab.Top` / `Seg.Item` | 13.5 | Medium | 顶部页签 / 分段项 |
| `T.H3` | 15 | SemiBold | 行内小标题 |
| `T.H2` | 17 | SemiBold | 卡片/Section 标题 |
| `T.H1` | 20 | SemiBold | 紧凑大标题 |
| `T.PageTitle` | 21 | ExtraBold | 页面标题（实例库/版本选择等） |
| 英雄区 `_heroTitle` | 34 | ExtraBold | 仅启动页（截图背景上） |

## 3. 圆角

**三档：`CR.Ctl=8`（控件）/ `CR.Card=12`（卡片）/ `CR.Pill=999`（胶囊/圆点）**。

- 一切按钮/输入框/下拉/菜单项/Tag/分段项：8；
- 卡片/Pane/弹层容器：12；
- 徽章、状态圆点（MCP dot、运行 dot、联机 dot）：999（**不再写半宽字面量 3.5/5**）；
- **微元素豁免**：最小边 ≤12px 的纯装饰元素（11px 拖拽手柄、3px Toast 色条、10px 停止图标方块）
  可用 ≈边长/3 的小圆角（2–3），不视为违规；
- 开发样张页（DesignSheetPage）的演示值不受约束。

## 4. 色板与对比度（WCAG AA）

明暗两套**令牌一一配对**（`Palette.Light.xaml` / `Palette.Dark.xaml`，各 ~75 键）。
**所有文字档对最坏背景 ≥ 4.5:1**（2026-11-08 修复；v1 的"辅助 ≥3:1"作废）。

| 令牌 | Light | Dark | 最坏背景对比度（浅/暗） |
|---|---|---|---|
| `B.Ink` | `#262626` | `#EDEDED` | 12.6 / 13.9 |
| `B.InkSoft` | `#565656` | `#C0C0C0` | 7.0 / 9.0 |
| `B.InkMuted` | `#666666` | `#9A9A9A` | 4.9（PageWash `#EDEDED`）/ 5.5（Card `#232323`） |
| `B.InkFaint` | `#6B6B6B` | `#8A8A8A` | 4.55（PageWash）/ 4.5（Paper2 `#242424`） |

- 主色：light `#2E9B6B` / dark `#3FB07C`；强主色（Primary 按钮底）`#1F7A52` / `#267A56`，白字 ≥5:1。
- 背景：light Canvas `#F5F5F5`、Paper/Card `#FFFFFF`、Paper2 `#FAFAFA`、PageWash 渐变 `#EDEDED→#F7F7F7→#F3F3F3`；
  dark Canvas `#161616`、Paper `#1E1E1E`、Card `#232323`、Paper2 `#242424`、PageWash `#161616→#1B1B1B→#181818`。
- 渐变：`B.LogoFill`（Logo）、`B.PageWash`（页面底，极弱）、`B.BannerFill`（**仅主题包导出兼容保留**，默认 UI 不使用）。
- 启动页英雄区在截图背景上临时覆写 `B.Ink*` 为白色系（`LaunchPage.ApplyHeroTone`），靠遮罩渐变保证对比度，属受控例外。
- 插件 WebView 的 CSS 令牌由 `WebViewHost.ThemePayload()` 从当前色板动态导出。

## 5. 组件解剖

| 组件 | padding | 字号 | 圆角 | 备注 |
|---|---|---|---|---|
| `Card`（样式） | 12 | — | 12 | B.Card 底 + 1px B.CardBorder（light 透明） |
| `Ui.Field` | 行距 `0,4` | 13 / hint 11 | — | 标签列 140，标签-hint 纵距 2 |
| `Ui.Section` | — | 17 / 副题 12 | — | 标题-副题纵距 2 |
| `Btn.*` | `12,6`（Chip `11,5`，Icon 7） | 13（Chip 12） | 8 | Primary/Soft/Normal/Ghost/Danger/Chip/Icon |
| `Input`/`Pw` | `10,7` | 13 | 8 | MinHeight 32，焦点 = 主题色描边 |
| `Tag` | `6,1.5,6,2.5` | 11 | 8 | 与 Chip 同圆角 |
| `Badge` | `6,0,6,1` | 11 SemiBold | 999 | 数字角标 |
| `Tab.Top` | `13,0` | 13.5 | — | 选中 = 粗体 + 2.5px 底部主题色条 |
| `Seg.Item` | `13,6` | 13.5 | 8 | 选中白底（B.SegOn）+ 粗体 |
| 标题栏 | 46px 高 | 14 / 12.5 | — | 窗口按钮 46 宽贴边 |
| `Empty` 空态 | 上下外距 24 | 图标 24 / 标题 13 / hint 12 | — | hint MaxWidth 420 居中 |

## 6. 窗口与导航

- 默认 **900×550**，最小 **720×480**（`MainWindow.xaml` 为唯一真相；`FitToWorkArea` 按工作区 92% 钳制；
  `WndProc` 的 `WM_GETMINMAXINFO` 读 XAML Min 值，**不再硬编码**——2026-11-08 修复三处矛盾）。
- 顶部 5 页签：启动 / 实例库 / 下载 / 插件 / 设置；设置内左导航 7 组（通用/账号/Java/下载与网络/服务器/游戏时长/反馈与关于）。
- 快捷键：Ctrl+1…9 切页、Ctrl+K AI、Ctrl+, 设置、F5 刷新。
- 启动页：全屏背景（实例截图，无则 hero.jpg）+ 左对齐英雄区（kicker 12.5 / 标题 34 / chips / 48px 主按钮内嵌版本）+ 底部新闻玻璃卡。

## 7. 可访问性（2026-11-08 新增）

- **键盘焦点环**：`FocusVisualStyle` 置空，改用模板内 2px `B.Accent` 描边（`Kf` Border，
  仅 `IsKeyboardFocused=True` 显示，鼠标点击不显示）。覆盖：BtnTpl、Btn.TitleIcon、Tab.Top、TabItemBtn、Seg.Item。
  输入框/下拉沿用焦点描边触发器。
- **Tab 可达**：顶部页签（Tab.Top）与标题栏 AI/主题/账号按钮 `IsTabStop=True`；
  窗口最小化/最大化/关闭保持系统惯例不参与 Tab。
- **屏幕阅读器**：纯图标按钮经 `AutomationProperties.Name` 命名——
  `Ui.IconBtn` 取 tooltip、`Ui.Btn` 取文字；标题栏按钮在 code-behind 随 I18n/主题/账号状态同步名字。
- **对比度**：见 §4，全部文字档 ≥4.5:1。
- 动效可关（设置 → 界面动画 / `Motion.Enabled`），关闭后全部瞬时。

## 8. 设计样张页

`src/TipCL/Pages/DesignSheetPage.cs`：仅 `--shot design-sheet` / `--design-sheet` 可达，不进导航。
渲染间距标尺、字号阶梯、圆角、色板、按钮全家、输入控件、Tag/Badge、Card/Field/Section/Empty。

## 9. 令牌→代码映射

| spec 条目 | 代码位置 |
|---|---|
| 间距栅格 | `Pages/PageBase.cs`、`Services/Ui.cs` |
| 字号阶梯 | `Themes/Controls.xaml`（T.* / Tab.Top / Seg.Item）、`Pages/LaunchPage.cs`（_heroTitle） |
| 圆角 | `Controls.xaml` CR.*、`Ui.cs` Tag、各页圆点 999 |
| 色板/对比度 | `Themes/Palette.Dark.xaml` + `Palette.Light.xaml`（成对） |
| 焦点环/Tab/命名 | `Controls.xaml`（Kf）、`MainWindow.xaml(.cs)`、`Services/Ui.cs` |
| 窗口尺寸 | `MainWindow.xaml` + `MainWindow.xaml.cs`（FitToWorkArea/WndProc） |

## 10. 裁决条目

1. GOAL 说导航 14 项，实际 NavDefs 曾注册 20 项；v2 收敛为顶部 5 页签 + 设置子导航。
2. 英雄区 34px 大标题：v1 曾禁 >20，v2 按游戏启动器范式有意保留（仅启动页截图背景上）。
3. `B.BannerFill` 不删：主题包导出/导入（SettingsPage）仍引用，默认 UI 已不使用。
4. ImageSharp 2.1.13 审计豁免：5 条 CVE 全在 TIFF/ICC/直方图路径，本程序 WebP-only 配置不可达；
   升级 3.2.0 需 net6.0+ 与 net48 冲突。详见 `docs/security/imagesharp-audit.md`。
5. 微元素圆角豁免（§3）：11px 手柄 / 3px 色条 / 10px 图标方块保留 2–3 小圆角。

## 11. v1 → v2 差异速查

| 项 | v1.0 | v2.0（当前） |
|---|---|---|
| 圆角 | 6 / 8 / 10 | 8 / 12 / 999（+微元素豁免） |
| 页标题 | H1 20 | T.PageTitle 21 ExtraBold |
| 最大字号 | ≤20 | 英雄区 34（仅启动页） |
| 导航 | 侧栏分组 6-7 项 | 顶部 5 页签 |
| 窗口 | 1180×740 | 900×550，Min 720×480 |
| 辅助文字对比度 | ≥3:1 | ≥4.5:1（AA） |
| 键盘焦点 | 无（FocusVisualStyle 置空） | 模板内 2px 主题色焦点环 |
| 渐变 | 仅 Logo | Logo + PageWash（极弱）+ BannerFill（兼容保留） |

## 12. 布局 v3 编辑态（R34 WP-L0，2026-11-20）

> 来源：`docs/mockups-hark/v3/layout3.html`（下载页编辑态 / 设置 → 布局）与 `pages3.html`（启动页深色编辑态）的 CSS。
> 落点：画刷与阴影在 `Themes/Palette.Light.xaml` / `Palette.Dark.xaml`（**成对**），矢量图标与样式在 `Themes/Controls.xaml`。
> 用法约定（GOAL-layout-v3 §6.4 第 1 条）：其他代码一律 `SetResourceReference` 或 `TryFindResource` + 兜底值取这些键，
> 不用 `FindResource` / `(Style)Resources[...]`。守卫单测：`tests/TipCL.Tests/LayoutV3ResourceTests.cs`。

### 12.1 画刷（Light / Dark）

| 键 | Light | Dark | 用途 |
|---|---|---|---|
| `B.EditOutline` | `#29000000`（黑 16%） | `#2EFFFFFF`（白 18%） | 块的常态虚线框、编辑条分隔线、空状态虚线框、手柄描边 |
| `B.EditOutlineHover` | `#57000000`（黑 34%） | `#5CFFFFFF`（白 36%） | 块悬停时的虚线框 |
| `B.EditOutlineOnDark` | `#59FFFFFF`（白 35%） | 同左 | 深色背景（启动页英雄图）上的虚线框 |
| `B.EditOutlineHoverOnDark` | `#B3FFFFFF`（白 70%） | 同左 | 同上，悬停 |
| `B.EditDropFill` | `#E7F5EE` | `#1B3A2C` | 拖动落点框浅底（= 默认主题色的 `B.AccentSoft`） |
| `B.EditDropFillOnDark` | `#1FFFFFFF`（白 12%） | 同左 | 深色背景上的落点框 |
| `B.EditColShade` | `#09000000`（黑 3.5%） | `#0AFFFFFF` | 拖动 / 改宽时的列底纹 |
| `B.EditColShadeOnDark` | `#0FFFFFFF`（白 6%） | 同左 | 深色背景上的列底纹 |
| `B.EditBarBg` | `#FFFFFF` | `#2B2B2B` | 编辑条、浮动标签、块图标按钮、手柄、抽屉的底色 |
| `B.EditBarLine` | `#14000000`（黑 8%） | `#1AFFFFFF` | 上述浮层的 1px 细描边 |
| `B.EditItemBg` | `#F2F2F2` | `#313131` | 抽屉里每一项、底部说明框的灰底 |
| `B.EditSegRail` | `#E7E7E7` | `#1E1E1E` | 「列表 / 网格」分段轨道 |
| `B.EditSegOn` | `#FFFFFF` | `#3A3A3A` | 分段选中项底色 |
| `B.EditDim` | `#800A0E16`（50%） | 同左 | 启动页编辑态压在英雄区上的遮罩（效果图是 45% 透明度 + 去饱和，WPF 没有去饱和滤镜，用遮罩近似） |
| `B.EditGlass` | `#B80E121B`（72%） | 同左 | 深色玻璃：标签 / 图标按钮 / 编辑条的 OnDark 底 |
| `B.EditGlassLine` | `#24FFFFFF`（白 14%） | 同左 | 深色玻璃描边 |
| `B.EditGlassChip` / `B.EditGlassChipLine` | `#24FFFFFF` / `#38FFFFFF` | 同左 | 「主区域固定」小条的底 / 描边；OnDark 分隔线 |
| `B.OnEditGlass` / `B.OnEditGlassMuted` | `#FFFFFF` / `#A8FFFFFF` | 同左 | 深色玻璃上的字色 / 次要字色；OnDark 选中框 |
| `B.WarnSoft`（已有） | `#FDF3E2` | `#3A2F1C` | 琥珀浅底（高级区的导入提示） |
| `B.WarnInk` | `#B45309` | `#EBB45E` | 琥珀字色（浅底上 ≥4.5:1） |

- `…OnDark`、`B.EditGlass*`、`B.OnEditGlass*`、`B.EditDim` 只用在**永远是深色**的背景上，所以明暗两套取值相同（单测守住）。
- 删除钮悬停红沿用窗口关闭钮的 `B.CaptionHot` / `B.OnCaptionHot`（`#C42B1C` / 白），不另开键。
- **主题色跟随**：自定义主题色时 `SettingsPage.ApplyAccent` 只重算 `B.Accent*` 这一组；`B.EditDropFill` 目前是默认绿的固定值，
  合并时需要在 `ApplyAccent` 里补一行（见 `windos/progress/WP-L0.md` 的合并事项）。选中框、落点框描边直接用 `B.Accent`，天然跟随。

### 12.2 阴影（`DropShadowEffect`，成对放在两个 Palette 里）

| 键 | 参数（Blur / Depth / 方向 / 不透明度 浅·深） | 对应 CSS |
|---|---|---|
| `Fx.EditBar` | 10 / 2 / 下 / 0.10 · 0.35 | `.ebox` `0 2px 10px rgba(0,0,0,.1)` |
| `Fx.EditLabel` | 8 / 2 / 下 / 0.12 · 0.40 | `.lab` / `.acts button` `0 2px 8px rgba(0,0,0,.12)` |
| `Fx.Drawer` | 32 / 12 / 左 / 0.14 · 0.50 | `.drawer` `-12px 0 32px rgba(0,0,0,.14)` |
| `Fx.SegOn` | 2 / 1 / 下 / 0.08 · 0.30 | `.sg2 button.on` `0 1px 2px rgba(0,0,0,.08)` |

`Layout.IconAct` / `Layout.SegItem` / `Layout.ResizeGrip` 把阴影放在模板里单独的底层 Border 上，内容不受 Effect 影响；
`Layout.EditBar` / `EditLabel` / `Drawer` 的阴影直接挂在 Border 上（与现有 ContextMenu / ToolTip 一样，里面的文字是灰度抗锯齿）。

### 12.3 矢量图标（`Geometry`，16×16 视框，只描边）

| 键 | 形状（参照 layout3.html 的 SVG） | 用途 |
|---|---|---|
| `Geo.Eye` | 眼睛 + 瞳孔 | 设置 → 布局的页签「显示」 |
| `Geo.EyeOff` | 眼睛划线 | 内置块「隐藏」、页签「已隐藏」 |
| `Geo.Lock` | 锁 | 「设置」页签不可隐藏、启动页英雄区固定 |
| `Geo.Undo` / `Geo.Redo` | 折返箭头 | 编辑条撤销 / 重做（比效果图整体上移 1，视觉居中） |
| `Geo.Grip` | 六点（0.1 长线段 + 圆头线帽） | 浮动标签、页签芯片的拖拽点 |

显示用 Path 样式：`Layout.Glyph`（14px = 16×0.875，有效线宽 1.4，Stroke 跟随所在 Control 的 Foreground）、
`Layout.Glyph.Small`（12px，有效线宽 1.35）、`Layout.Grip` / `Layout.Grip.OnDark`（12px，点径约 1.65，Stroke 直接取色板，可放在非 Control 里）。
都在 GOAL §4.3 的「10–14px、线宽 1.1–1.6、圆头」范围内。垃圾桶 / 更多 / 添加 / 列表 / 网格 / 关闭继续用 `Services/Ico.cs` 的字形。

### 12.4 样式

| 键 | 目标类型 | 关键数值 | 用途 |
|---|---|---|---|
| `Layout.EditOutline` / `.OnDark` | Rectangle | 1.5 虚线 `3 2`、圆角 12、Margin −4（外扩 4）；`Tag="hover"` 加深，`"selected"`/`"drag"` 2px 实线（主题色 / 白） | 块的编辑态框线（叠在块上方，不吃鼠标） |
| `Layout.EditLabel` / `.OnDark` | Border | 高 24、圆角 8、内距 6,0,10,0、12 粗体、默认偏移 8,−14 | 悬停 / 选中时块左上角的标签 |
| `Layout.EditLabelNote` / `.OnDark` | TextBlock | 12 常规、InkMuted | 标签里的「· 组件」 |
| `Layout.IconAct` / `.OnDark` | ButtonBase | 26×24、圆角 8、悬停字色加深 | 块右上角「隐藏」（内容放 `Geo.EyeOff`） |
| `Layout.IconAct.Danger` / `.Danger.OnDark` | ButtonBase | 同上，悬停整块 `#C42B1C` + 白 | `custom:` / `plugin:` 块的「删除」（`Ico.Trash`） |
| `Layout.ResizeGrip` / `.Bottom` | Thumb | 10×36 / 36×10 白胶囊 + 2px 灰条，拖动中变主题色 | 网格块右边缘 / 下边缘手柄 |
| `Layout.EditTip` | Border | 圆角 8、内距 8,2、深底浅字 12 | 拖右边缘时的「N / 列数」 |
| `Layout.DropBox` / `.OnDark` | Rectangle | 浅底 + 1.5 主题色虚线、圆角 12 | 拖动落点框 |
| `Layout.ColShade` / `.OnDark` | Border | 圆角 8 | 拖动时的列底纹 |
| `Layout.EditBar` / `.OnDark` | Border | 高 46、圆角 12、内距 14,0,8,0、13 号 | 内容区顶部的编辑条 |
| `Layout.EditBarTitle` | TextBlock | 13 ExtraBold | 「编辑「页名」」 |
| `Layout.EditHint` / `.OnDark` | TextBlock | 12、InkMuted、省略号 | 编辑条里的灰色提示 |
| `Layout.EditBarSep` / `.OnDark` | Border | 1×18、左右 2 | 编辑条竖分隔线 |
| `Layout.Count` | Border | 高 16、圆角 8、主题色底白字 12 粗 | 「添加」按钮的数字角标 |
| `Layout.SegRail` | Border | 圆角 12、内距 3 | 「列表 / 网格」轨道 |
| `Layout.SegItem` | ToggleButton（RadioButton 也可用） | 高 24、内距 10,0、圆角 8、13 号；选中白底 + 粗体 + 淡阴影 | 分段里的一项 |
| `Layout.Drawer` | Border | 宽 316、靠右、左侧阴影 + 1px 线 | 「添加」抽屉 |
| `Layout.DrawerTitle` / `DrawerSection` | TextBlock | 15 ExtraBold / 12 InkMuted、上 10 下 6 | 抽屉标题 / 分节小字 |
| `Layout.DrawerItem` / `DrawerIcon` | Border | 内距 8、圆角 12、灰底 / 40×40、圆角 8 | 抽屉里的一项 / 项目图标底 |
| `Layout.DrawerEmpty` | Rectangle | 1px 虚线 `4 3`、圆角 12 | 没有隐藏项时的空状态框（文字叠在上面） |
| `Layout.DrawerNote` | Border | 内距 12,10、圆角 12、12 号 | 抽屉底部说明 |
| `Layout.WarnNote` | Border | 内距 12,8、圆角 8、琥珀底字 | 高级区「导入的布局…仍然默认关闭」 |
| `Layout.LockNote` | Border | 内距 10,4、圆角 8、玻璃底白字 12 | 启动页「主区域固定，不能拖动或隐藏」 |

**和规范其他条目的关系**

- 圆角只用 8 / 12（单测守住）。效果图里 `.sg2 button` 是 6、外扩 4 的 outline 在浏览器里是 16，这里分别取 8 / 12 收敛进现有三档。
- 字号都在 §2 的合法集合里（12 / 13 / 15）。编辑条左内距 14、标签上偏 −14 直接取效果图，属于浮层定位，不进 §1 的间距栅格。
- 对比度：灰字一律用 `B.InkMuted`（效果图的 `#8c8c8c` 在白底只有 3.4:1，不满足 §4 的 4.5:1）；琥珀字 `B.WarnInk` 在 `B.WarnSoft` 上浅色 4.6:1、深色 7.0:1；深色抽屉项底 `#313131` 上 `B.InkMuted` 为 4.6:1。

### 12.5 截图开关（`DevShot.cs`，只写了代码，**本轮没有运行**）

| 开关 | 作用 |
|---|---|
| `--edit` | 截图前让目标页的区块宿主进编辑态（按页 id 精确匹配，其次「页 id:」前缀，例如 `library` → `library:main`） |
| `--select <blockId>` | 模拟选中某块，拍浮动标签和手柄（隐含 `--edit`） |
| `--pop layout-drawer` | 打开「添加」抽屉（隐含 `--edit`） |
| `--expand advanced` | 展开设置 → 布局的「高级」（按标题「分栏、网页与按钮组件、布局文件」找） |
| `--shot settings:<子页>` | 设置子视图：general / account / java / network / servers / playtime / about / layout；`java` / `servers` / `playtime` / `feedback` 不是顶栏页，直接 `--shot java` 也会落到设置子视图 |
| `--design-sheet` | 样张页末尾追加本节全部样式的演示（`--no-v3-sheet` 关掉） |

参数解析在纯函数 `DevShotArgs.Parse`（有单测）；选中 / 抽屉 / 展开各留了一个钩子（`DevShot.SelectHook` / `LayoutDrawerHook` / `ExpandHook`），合并后接到正式入口。
