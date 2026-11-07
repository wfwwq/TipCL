# WP-S12: 壁纸铺满窗口（含标题栏）+ DWM backdrop/边框清理 —— 实修记录（2026-11-07）

基于 b0143d5。改动文件：`Services/Backdrop.cs`、`Services/Wallpaper.cs`、`MainWindow.xaml.cs`（Attach 调用 + SyncMaxGlyph 两处）。未碰 LaunchPage / Core / Plugins / 版本号。

## 用户方案逐条落地

### 1. WallHost 铺满范围核对 ✅（无需改动）
- `MainWindow.xaml:16`：`WallHost` 已是 `Grid.Row="0" RowSpan="2"`（覆盖标题栏行+内容行），`ClipToBounds="True"` 只受自身范围约束，上层无额外裁切（ContentLayer/RootBorder 无 Clip；ApplyRootCorner 只在 Win10 圆角时对 RootBorder.Child 即 ContentLayer 做 RectangleGeometry 裁切，非裁剪壁纸的额外遮挡）。
- **真正挡住壁纸的是 TopBar 的 `B.TitleBar` 背景（不透明度 0.92）**。原 `Wallpaper.ApplyChrome` 里把 chromeBar 设为玻璃的代码因 `_side == null`（Attach 第三参传 null）整个 if 被跳过——是死代码。本次修复（见第 3 条）后：壁纸激活 → TopBar `B.GlassStrong`（α0.86 玻璃）+ `B.GlassBorder` 描边，壁纸透上标题栏；无壁纸 → 回 `B.TitleBar`/`B.TitleBarLine` 主题实底。

### 2. Backdrop.Apply 按 Wallpaper.Active 分支 ✅
`Services/Backdrop.cs` Apply() 内：
- 有壁纸：`DwmSetWindowAttribute(handle, 38, DWMSBT_NONE=1)`（关闭 Mica，避免失焦退实色/亚克力盖壁纸）；
- 无壁纸：`DWMSBT_MAINWINDOW=2`（Mica）；
- attr 38 调用失败（Win10/Win7）：回退 `ApplyLegacyBlur(handle, App.IsDark)`（原逻辑保留）。
- `Backdrop.Refresh(Window)` 已存在（=Apply），无需新增。

### 3. Wallpaper：RootBorder 兜底 + 刷新联动 ✅
- `Attach` 签名加 `Border root`，`MainWindow.xaml.cs:170` 改传 `RootBorder`。
- `ApplyChrome(on)` 重写为逐元素判空（原来 `&& _side != null` 连坐导致全段死代码）：
  - `_root`：on → `SetResourceReference(Background, "B.Paper")` 不透明兜底（壁纸图片画在它上层，图片缺失/透明区域露纸底）；off → `Background = null`。
  - `_chromeBar`（TopBar）：on → `B.GlassStrong`/`B.GlassBorder`；off → `B.TitleBar`/`B.TitleBarLine`。
  - `_wash`/`_dim`/`SideFrost.Refresh` 原语义保留。
- 联动入口：新增 `public static Action? ChromeInvalidated`；`ApplyChrome` 与 `ReloadAsync` 末尾都调 `Backdrop.Refresh(AppServices.Window)` + `ChromeInvalidated?.Invoke()`（`SetSource→ApplyChrome` 覆盖轮换/失败清图等所有转换路径）。`MainWindow` 订阅 `ChromeInvalidated = SyncMaxGlyph`，保证壁纸开关瞬间边框状态同步。

### 4. 四周细边：选了 DWM 边框色方案（推荐路径）✅
- `Backdrop.Apply` 内加 `DwmwaBorderColor(34)`：壁纸激活 → `0x00262626`（深灰细线）；否则 → `0xFFFFFFFE`（DWMWA_COLOR_DEFAULT 交还系统）。Win10 上 attr 34 调用失败无副作用。
- 配套：`SyncMaxGlyph` 中 `RootBorder.BorderThickness = (!max && !Wallpaper.Active) ? 1 : 0`——壁纸激活时 WPF 自身 1px 描边归零防双线，细边完全交给 DWM；无壁纸维持原行为（窗口化 1px / 最大化 0）。

## 与 premium 沉浸态的叠加分析（代码审查级）
- LaunchPage `SyncImmersive` 进入条件含 `!Wallpaper.Active`（LaunchPage.cs:586/686/702），与壁纸互斥，两者不会同时抢 TopBar/WallHost：
  - 启动页 + 无壁纸 + premium：沉浸逻辑原样（hero 进 WallHost、TopBar 玻璃）——本改动不动该路径（ApplyChrome 只在壁纸状态翻转时跑，off 时把 TopBar 资源引用设回 B.TitleBar/B.TitleBarLine，与 XAML 默认一致，沉浸的元素级 ResourceDictionary 覆盖仍优先生效）。
  - 启动页/任意页 + 有壁纸：沉浸不进入；TopBar 玻璃（B.GlassStrong α0.86）+ 壁纸透上，页签/白字可读性依赖 `_dim`（用户可调压暗）。
  - 实例库 + 有壁纸：TopBar 玻璃非实底，是方案预期（"标题栏下也是壁纸"）；可读性由 GlassStrong 0.86 不透明度保证。
- 运行时叠加截图见下节；DWM 效果离屏不可见，标注待验。

## 验证
- compact 构建：`dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1` —— 结果见文末补记。
- premium 构建：加 `-p:Premium=true`。
- 测试：`dotnet test`（基线 127/128，唯一失败 PhoneLinkTests.FullFlow）。
- 截图：`--shot launch` / `--shot library` 存 `_wph_run\wps12\`。
- exe 体积 ≤ 5,757,264（compact）。

## DWM 运行时待验清单
1. 有壁纸时窗口失焦不退实色（DWMSBT_NONE 生效，壁纸仍可见）。
2. 无壁纸时 Mica 正常（DWMSBT_MAINWINDOW）。
3. Win11 壁纸激活时窗口四周 0x00262626 细边；取消壁纸后回系统默认边框色。
4. Win10：attr 38/34 失败回退亚克力路径，壁纸+亚克力叠加的实际观感（可能偏蒙，属方案内取舍）。
5. 壁纸轮换（文件夹播放）期间 backdrop/边框无闪烁。

## 补记（构建/测试/截图结果，2026-11-07）
- 构建期间发现工作区含并行任务未提交改动（WP-S11 Dlg.cs 起初缺 using 导致编译失败，后由其自行修复；LibraryPage.cs / PluginsPage.Market.cs 改动未入库、未由本任务触碰）。本任务只 add：`Services/Backdrop.cs`、`Services/Wallpaper.cs`、`MainWindow.xaml.cs`、`progress/WP-S12.md`。
- **compact 构建**：0 错误；TipCL.exe = **5,737,472 B** ≤ 5,757,264 红线（含并行任务未提交代码的体积）。
- **premium 构建**（-p:Premium=true）：0 错误。
- **测试**：`dotnet test` **128/129** 通过，唯一失败 PhoneLinkTests.FullFlow（基线既有，未碰）。
- **截图**（premium exe，`--shot`，存 `_wph_run\wps12\`）：
  - 首轮发现回归：launch 截图 hero 只剩标题栏 46px、页面区被盖黑——根因是本任务把死代码 ApplyChrome 救活后，`OnThemeChanged→ApplyChrome(false)` 在 premium 沉浸态生效后把 `PageWash.Opacity` 打回 1、TopBar 资源引用重设，恰好打在沉浸态上。修复：`ApplyChrome` 加 `_chromeOn` 门——从未有过壁纸时（on=false 且 !_chromeOn）直接 return，保持 XAML/沉浸态现状；壁纸从有到无才走还原分支。
  - 复验 `premium_launch3.png`：hero 全幅铺满含标题栏、TopBar 玻璃 scrim 白字，与基线 b0143d5（`base_launch.png`，独立 worktree 构建）一致——沉浸态叠加通过。
  - `premium_library3.png`：实例库正常，TopBar 回主题实底，无残留。
  - 壁纸激活态（TopBar 玻璃透壁纸、RootBorder B.Paper 兜底、DWM 边框色）离屏截图无法无副作用构造（需写用户后端设置），列运行时待验。
- git worktree 基线验证后已清理（tipcl_base / tipcl_base_bin 已删）。
