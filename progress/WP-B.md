# WP-B 启动页 v2 —— 完成待验（2026-11-06，第二轮，未编译）

## 改动文件
- `src/TipCL/Pages/LaunchPage.cs`（重写启动屏布局段；审补上一轮半成品）
- `src/TipCL/Pages/LaunchBackground.cs` **已删除**（并入 LaunchPage 的一个静态方法 `LoadShot`，省一个类型；工作区删除，未 stage）
- 未动 Themes/*、MainWindow.*，未新增任何资源键。

## 做了什么
- 删：`B.HeroGrid` 方格纹理、`B.HeroGlow1/2` 光晕、SizeChanged 定位、三个 999 圆角胶囊（账号/启动配置/更多）、「更多」浮层（OpenMorePopup/MoreRow）、旧底部公告条、`_meta` 元信息行、`_accountPillText`、`_loaderName` + `TagLoaderAsync`（只用于已删的元信息行）。
- 布局（数值取自 launch2.html）：内容左对齐左距 56、纵向居中：绿点 + 「就绪 · 游戏目录」12.5 → 实例名 34 ExtraBold + 下拉箭头（点开原实例切换浮层）→ 小标签行（Java N / 内存 GB / N 个模组，间距 6，上 10 下 24）→ 按钮行：主色「启动游戏」48 高（主色柔光，取 B.AccentStrong 当前色）+ 48×48 启动配置图标钮（原配置浮层）+「联机」按钮（`Win.Navigate("multiplayer")`，即原「更多」里的联机页）。进度/状态行仅在非空闲时显示。
- 底部新闻卡靠左（margin 0 0 18 56，圆角 12，主色「新闻」小标签 + 标题 + ›），点击打开原新闻浮层。
- 背景：`RefreshBackground()` 在线程池扫描 `<实例目录>/screenshots` 与 `<实例目录>/versions/<版本>/screenshots`，取最新 png/jpg，`BitmapImage` OnLoad + DecodePixelWidth 1600 + Freeze；同一张（路径+mtime）不重复解码；代际号防竞态；异常全吞 → 回退现有渐变底（页面透明）。用户设置了自定义壁纸（`Wallpaper.Active`）时不叠截图。有截图时叠 rgba(10,14,22) 左→右(.78/.45/0 @0/42%/75%) 与底部(.55→0 @35%) 两层渐变，文字转白、按钮/标签/新闻卡转半透明白；无截图时用 B.Paper/B.Line/B.Ink/B.InkMuted/B.Hover 主题资源。
- 圆角：按钮/标签 8，新闻卡 12，「新闻」小标签 6。主题色全走 B.Accent / B.AccentStrong / B.OnAccent / B.OnAccentStrong / B.Ok / B.Danger(Soft)（均已 grep 确认在基线与当前 Palette.*.xaml 中存在）。
- 布局相关的事件改动只有：内存滑块 ValueChanged 里加一行 `SyncHero()`（刷新内存标签）；SyncBanner 里删 TagLoader 调用、加 CountModsAsync + RefreshBackground。

## 判断与偏差（请监工确认）
- 效果图大按钮/毛玻璃钮是圆角 12，任务要求「控件 8」，按 8 落地；要改回 12 只需改 `_launchShell`、`_stopShell`、`GlassButton` 三处 CornerRadius。
- 未做真正 backdrop blur（WPF 无廉价实现），用半透明白近似。
- 标题栏半透明叠在截图上需要页面延伸到标题栏下方，属 WP-A（MainWindow）范围，本包未做。

## 留给其他包
- WP-A/WP-F：`B.HeroGrid`、`B.HeroGlow1`、`B.HeroGlow2` 在 Palette.Dark/Light.xaml 里已无代码引用，可删（省 BAML 体积）。
- WP-F 删「更多」抽屉时保留 MainWindow 里 `multiplayer` 的 NavDef，否则启动页「联机」按钮导航失效。

## 编译风险点（无 dotnet，仅靠阅读）
- LangVersion 12 / Nullable disable：新代码里的 `string?`、`Tuple<…>?` 只产生 CS8632 警告（与既有代码一致），csproj 未开 TreatWarningsAsErrors。
- `System.ComponentModel.DependencyPropertyDescriptor.FromProperty(...).AddValueChanged`（WindowsBase）用于状态行显隐。
- `Image` 初始化器里 `Stretch = Stretch.UniformToFill`：类内无名为 Stretch 的成员（UiExt.Stretch 是扩展方法），应解析为枚举。
- 字形 `\ue76c`（›）此前文件中未用过，若内置 TipCL Icons 子集字体没有，会落到 Segoe Fluent/MDL2（Win10+ 有）。
- 体积：LaunchPage 相对基线净增约 150 行（含注释/花括号）；删掉了 LaunchBackground 类、TagLoader、元信息、更多浮层等以抵消，但仍可能占用 1–2 KB 余量，需实测 exe ≤ 5,707,264 B。
- 未能运行时验证：截图解码在线程池创建 BitmapImage（冻结后跨线程），常见做法，理论安全。

## 未完成
- 无功能性未完成项；待 Windows 编译 + 截图验收。未 commit。
