# WP-T：AI 入口改右侧玻璃药丸钮（✳ AI）+ Ctrl+K

2026-11-07。本工作包为**接续**前任被中断的半成品（工作树已有改动，方向获监工认可），在其基础上补完验收。

## 前任 diff 审读结论
半成品其实已基本完整，逐项核对无缺漏，本包未再改代码：
- `MainWindow.xaml`（+10/-2）：AiBtn 由纯图标钮改为玻璃药丸 —— `Btn.TitleChip`（Height 26）内套 CornerRadius 13 的 `B.GlassStrong`/`B.GlassBorder` Border + 星芒 Path（原 Ico.Line 数据保留）+ 11.5px SemiBold "AI" 小字，位于标题栏右侧簇、ThemeBtn（月亮）左边。
- `MainWindow.xaml.cs`（+12/-1，3 处）：
  - ToolTip 改 `"AI 助手" + " Ctrl+K"`（:168）；
  - OnShortcut（PreviewKeyDown，:259 挂接）新增 `Key.K` 分支（:1052-1061）：Ctrl+K → `Navigate("ai")`，守卫 `DialogHost.Visibility` 不可见 + 焦点非 `TextBoxBase`/`PasswordBox` 才抢键；
  - AiBtn.Click → `Navigate("ai")` 为更早已存在的接线（:203），确认无需补。
- 资源键核对：`B.GlassStrong`/`B.GlassBorder` 在 Palette.Light/Dark.xaml:44-45 均存在；`Btn.TitleChip` 在 Controls.xaml:398 存在。

## LaunchPage.cs 的 3 行（归属说明）
前任在 `Pages/LaunchPage.cs:633-634` 沉浸字典加了 `B.GlassStrong`/`B.GlassBorder` 深色 scrim 覆盖（属 WP-G 沉浸适配，保证暗色沉浸态药丸白字可读）。**仍留在工作树未提交，留给 WP-U 一并提交**（本包禁碰 Pages/）。

## 验证
- compact 构建 0 错误，exe **5,706,240 B** ≤ 5,707,264 B（余量 1,024 B，与改动前持平）。
- premium 构建（-p:Premium=true）0 错误，exe 6,001,152 B。
- `dotnet test`：**127/128** 通过，PhoneLinkTests.FullFlow 为既有基线失败。
- 截图 `_wph_run/wpt/launch-dark.png`、`launch-light.png`（`--shot launch`，compact exe）：两主题药丸均清晰可读 —— light 浅玻璃底墨字，dark 深 scrim 白字，与效果图 v2 一致。

## 遗留
- Ctrl+K 的守卫逻辑为代码级验证（构建 + 逻辑审读），运行时按键行为待用户实测。
- LaunchPage.cs 3 行未提交（WP-U 地盘）。
