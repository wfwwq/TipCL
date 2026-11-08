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
