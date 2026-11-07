# WP-A 窗口外壳 + 全局 token —— 完成待验（2026-11-06，未编译）

基于 HEAD d325b3a（上一轮 WIP）继续；审查了 `git diff 84484d6 HEAD -- src/TipCL/Themes`，半成品 XAML 结构正确、键齐全，在其上修补/精简。未 commit（交监工）。

## 改动文件
| 文件 | 内容 |
|---|---|
| `src/TipCL/MainWindow.xaml` | 标题栏整段重写（TopBar Border 内）：Logo(22px 圆角6 B.LogoFill)+红点 `BridgeDot` / 5 页签 / AI 图标钮 / 主题钮 / 账号钮(首字母圆+名称) / 分隔线 / 最小化·最大化·关闭（矢量 Path）。标题栏高 48→**46**（CaptionHeight 与两处 RowDefinition 同步）。删除常驻“C 桥”胶囊（BridgeText）。 |
| `src/TipCL/MainWindow.xaml.cs` | ① NavDefs 加 `download → DownloadPage`（WP-C 要求）；② 构造函数：删 `BridgeText`，加 AiBtn ToolTip、账号钮初值“未登录”、`SyncThemeIndicator()`；③ 页签接线：下载→`Navigate("download")`、插件→`"plugins"`（现有 PluginsPage）、AI→`"ai"`（现有 AiPage）、账号钮→`"account"`；④ `SetBridgeState` 改为：Logo ToolTip=状态文字，红点仅在 B.Danger 或（已连上过且 B.Warn=重连中）时显示；⑤ 新 `SyncAccountChipAsync()`（AccountGate.ListAsync 第一个=active），连接完成后、以及离开 account 页时刷新；⑥ `ApplyRootCorner` 加 Win10 `SetWindowRgn`（GetWindowRect 取设备像素尺寸，半径×DpiScaleX，尺寸/半径不变不重设，最大化 r=0→清区域）；新增 `GetWindowRect` P/Invoke；⑦ `SyncMaxGlyph`/`SyncThemeIndicator` 改为切 Path.Data（常量 GeoMax/GeoRestore/GeoMoon/GeoSun）；⑧ `SyncTabs` 加插件，下载页签在 `download` 或 `tasks` 时都亮。 |
| `src/TipCL/Services/Backdrop.cs` | 新增 `SetRoundRegion(hwnd,w,h,r)`：`CreateRoundRectRgn(0,0,w+1,h+1,r*2,r*2)` + `SetWindowRgn(..., true)`，失败才 DeleteObject；r≤0 传 IntPtr.Zero。Win11 仍走既有 `TryRoundWin11`（DWM 属性 33）。`RootCornerRadius` 注释更新（Win11=0 / Win10=8 / Win7=0）。 |
| `src/TipCL/Themes/Controls.xaml` | 见下方键清单；另：Btn.Primary 字重 SemiBold→Bold；Combo 下拉/ContextMenu 圆角 10→12，ToolTip 6→8，ComboItem/MenuItem 悬停块 6→CR.Ctl(8)；Btn.TitleIcon 的 ContentPresenter 加 Margin=Padding；Btn.TitleChip 删掉重复模板改为复用 TitleIcon（省体积）。删 WIP 加的未使用键 R.Ctl / R.Window / CR.Window。 |
| `Themes/Palette.Light.xaml` / `Palette.Dark.xaml` | 成对：删 `C.CardShadow`（无人用）、`B.HeroGlow1` `B.HeroGlow2` `B.HeroGrid`（启动页 WP-B 已不用，交接必读要求删）。两套键已脚本核对一一对应。 |

## 资源键清单（WP-B/C/D/E 依赖，均未改名）
**Controls.xaml**
- 圆角：`CR.Ctl`=8、`CR.Card`=12、`CR.Small`=8、`CR.Pill`=999（仅数字角标）、`R.Card`(double)=12。窗口圆角 8 在代码 `Backdrop.RootCornerRadius`。
- 文字：`T.PageTitle`（21 ExtraBold）、`T.GroupTitle`（12.5 SemiBold 灰，卡片外上方，Margin 4,0,0,6）、原有 T.H1/H2/H3/Muted/Small/Mono。
- 卡片：`Card`（B.Card 底、B.CardBorder 边[浅色透明/深色极淡]、圆角 12、Padding 12，**无阴影**）、`Card.Shadow`（BasedOn Card + DropShadow 0.06，长列表别用，且 Effect 会让子元素文字失去 ClearType）。
- 按钮：`Btn.Primary`（AccentStrong 实心、Bold、圆角 8）、`Btn.Soft`（“启动”主题色软底）、`Btn.Gray`（“管理”灰）、`Btn.Normal/Ghost/Danger/Icon/Chip/OnBanner`。
- 分段：`Seg.Rail`（Border，灰轨道 圆角10 Padding3）、`Seg.Item`（ToggleButton，选中白底+粗体+淡阴影）。
- 标题栏：`Tab.Top`、`Btn.Caption`、`Btn.CaptionClose`、`Btn.TitleIcon`（32×32）、`Btn.TitleChip`。
- 图标：**新** `Ico.Line`（Path 16×16 线宽1.7 圆头，Stroke 绑定祖先 Control.Foreground）、**新** `Ico.Caption`（10×10 线宽1.1）。
**Palette（明暗成对）**：`B.Card`、`B.CardBorder`、`B.TitleBar`、`B.TitleBarLine`、`B.TitleSep`、`B.CaptionInk`、`B.CaptionHot`(#C42B1C)、`B.OnCaptionHot`、`B.BtnGray`、`B.BtnGrayBorder`、`B.BtnGrayInk`、`B.SegRail`、`B.SegOn`、`B.RowLine`、`B.RowHover`、`B.PageWash`（v2 斜向灰渐变）。
**已删**：`R.Ctl`、`R.Window`、`CR.Window`、`C.CardShadow`、`B.HeroGlow1`、`B.HeroGlow2`、`B.HeroGrid`（全仓库已 grep 无引用）。

## MainWindow 元素名（供他包引用）
`TopBar`(Border) `BridgePill`(Grid，Logo 容器，ToolTip=桥状态) `BridgeDot` `TabLaunch/TabLibrary/TabTasks(=下载)/TabPlugins/TabSettings` `AiBtn` `ThemeBtn`/`ThemeIco` `AccBtn`/`AccInitial`/`AccName` `MinBtn` `MaxBtn`/`MaxIco` `CloseBtn`。

## 未完成 / 留给后续
- “更多”抽屉：本来就不在可视标题栏里（BuildNav 只建离屏 NavHost），未动；multiplayer NavDef 保留。删除交 WP-F。
- 启动页背景铺到标题栏下（标题栏半透明 glass）未做：PageHost 在第 2 行，若 WP-B 需要，需在 MainWindow 加一个切换 TopBar 背景/让 PageHost 跨两行的钩子（未加，省体积）。
- 设置子页（account/java 等）时“设置”页签不亮；需要时在 SyncTabs 扩展。
- `Chk` 勾选框圆角 6、`Tpl.Version` 标签 6 未改（小控件，WP-F 统一时再定）。
- Ui.cs 里代码生成的卡片是否用 `Card` 样式未核（不在本包范围）。
- 体积：Controls.xaml 净增约 +130 行（WIP 的 Seg/TitleIcon 等），本轮已删 WIP 重复模板与 Palette 中 17×2 行 Hero 画刷抵消一部分；**需实测 exe ≤ 5,707,264 B**。超了可先删 `Card.Shadow`/`Seg.Item` 的 DropShadowEffect 段。

## 编译风险点（请在 Windows 上重点看）
1. `Ico.Line` 的 Setter 值是 `{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Control}}}`——Style 内 Binding 合法，若运行时图标不显示，查此绑定。
2. `ApplyRootCorner` 里 `if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rc)) return;` 之后使用 rc——依赖 C# 明确赋值分析（`||` 为 false 分支），应能编过。`RECT` 用的是 MainWindow 内已有 private struct。
3. `VisualTreeHelper.GetDpi(Visual)` 需 .NET 4.6.2+（net48 OK）。
4. Path 的 `x:Name`（ThemeIco/MaxIco）位于 Button.Content 内，属窗口 NameScope，生成字段类型为 System.Windows.Shapes.Path。
5. Win10 实测项：SetWindowRgn 后亚克力四角应被裁掉；窗口区域无抗锯齿（角有轻微锯齿，WPF Clip 在内侧平滑）；拖动缩放时每次尺寸变化重设区域，若闪烁可改为仅 WM_EXITSIZEMOVE 时设。
6. `TipCL.Pages.DownloadPage` 由 WP-C 提供（已确认存在、无参构造）。
