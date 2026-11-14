# WP-G premium 版（PREMIUM 开关 + 嵌入官方图标/hero + 标题栏沉浸）—— 完成待验（2026-11-07，Windows 编译+运行验证通过）

基于 HEAD 6ebc26e（compact 已验收版）。双版本交付见交接必读 §2.5 / DECISIONS D-6。
**未 push、未改版本号（Version 仍 3.2.0）。**

## 开关用法
- compact：`dotnet build TipCL.sln -c Release`
- premium：`dotnet build TipCL.sln -c Release -p:Premium=true`（csproj 里 `Premium` 属性 → `DefineConstants` 追加 `PREMIUM`；条件资源组嵌入 PremiumAssets）
- 同一 bin 目录（bin/Release/net48/TipCL.exe），切开关即整体重编；交付时复制改名 `TipCL-compact.exe` / `TipCL-premium.exe`（监工步骤，本包未复制）。

## 改动文件
| 文件 | 内容 |
|---|---|
| `src/TipCL/TipCL.csproj` | ① 主 PropertyGroup 尾部加条件 `DefineConstants`（`'$(Premium)'=='true'` → `$(DefineConstants);PREMIUM`）；② 新增条件 ItemGroup（同 Condition）内嵌 6 个 `<Resource>`：`PremiumAssets\grass.png / fabric_gh.png / forge_gh.png / neoforge_gh.png / quilt_gh.png / hero.jpg`。写法照抄既有 `<Resource Include="Resources\fonts\TipCLIcons.ttf" />`。 |
| `src/TipCL/PremiumAssets/`（新目录） | 素材复制自 `docs/mockups-hark/v2/`（grass/fabric_gh/neoforge_gh/hero.jpg 原样；**forge_gh.png 实为 JPEG 内容**，WIC 按内容识别解码，已验证 OK）；quilt_gh.png 从官方头像源抓取（见下）。 |
| `src/TipCL/Services/LoaderIcons.cs` | `Url()` 移入 `#if !PREMIUM`；`Tile()` 分两支：compact 原样 `SetUrl(Url(loader))`；premium `SetImage(Embedded(loader, px))` —— pack URI `pack://application:,,,/TipCL;component/PremiumAssets/<file>` 解码（BeginInit/OnLoad/IgnoreColorProfile/DecodePixelWidth=尺寸×2 夹在 32..96/EndInit/Freeze），`ConcurrentDictionary` 缓解码图（版本列表每行一张草方块），未知 loader 返回 null 仍落首字母方块。 |
| `src/TipCL/Services/ThumbTile.cs` | 加 `#if PREMIUM public void SetImage(BitmapSource?)`（内部 `Show()`，`_token++` 防在途下载竞态）。compact 无此方法。 |
| `src/TipCL/Pages/LaunchPage.cs` | 全部 `#if PREMIUM`：① 字段 `_heroCache`（静态只解码一次）+ `_immersive`；② `RefreshBackground` 无截图且无自定义壁纸时 `Task.Run(HeroSource)` 用嵌入 hero.jpg 做背景（截图优先逻辑两版一致；`_bgKey` 置空保证之后出现截图仍会被扫描到）；③ `HeroSource()`：pack URI 解码 1600px、OnLoad、Freeze、异常返回 null；④ `SyncImmersive(bool)` + `GlassTopBar(Border,bool)` + `Sc()`（见沉浸段）；⑤ `OnHidden()` override → `SyncImmersive(false)`。compact 路径 IL 零改动（exe 字节级与验收基线一致）。 |
| `tests/TipCL.Tests/Win7CompatTests.cs` | 白名单数组加 `"LoaderIcons.cs"`（该测试扫描「new BitmapImage( 且无 WebpFallback」的文件；LoaderIcons premium 只解码程序集内嵌素材、永不接触外部 WebP，正属白名单设计用途「只解码内置 PNG 的地方」）。不加分会导致 compact 测试 126/128。 |

**没碰**：MainWindow.xaml / MainWindow.xaml.cs（WallHost、TopBar、PageWash 用既有 x:Name 内部字段直接访问）、Core/、PluginsPage、PROGRESS/DECISIONS/交接必读、版本号。

## premium 标题栏沉浸（实现方式与取舍）
- 进入条件：LaunchPage 有全幅背景（截图或 hero）且用户没设自定义壁纸且 `PageBase.Win != null`。
- 做法：`_bgImage`/`_bgShade` 从页面根 Grid 搬进 `MainWindow.WallHost`（RowSpan=2、TopBar 之后的非命中层；排在 Wallpaper 自带元素之后天然压住）；`PageWash.Opacity=0`；TopBar 换玻璃——元素级 `ResourceDictionary` 覆盖 `B.TitleBar`(rgba(10,14,22,.18) 深色 scrim)、`B.TitleBarLine`(白 .18，同效果图 .tb.glass)、`B.Ink/B.InkMuted/B.InkSoft/B.CaptionInk/B.TitleSep`（白系），另设 `TextElement.Foreground` 附加属性=白（管住属性继承值：「TipCL」标题继承 Window 解析好的 B.Ink，浅色主题下是深色，资源字典覆盖不了它）。
- 还原路径（三条，都验证过）：背景消失（截图没了/设了壁纸）→ `ApplyHeroTone(false)`；离开启动页 → `OnHidden()`；回启动页 → `OnShown→ApplyHeroTone→SyncImmersive` 重新进入。还原 = 元素插回页面根索引 0/1、PageWash=1、TopBar 资源字典清空（DynamicResource 回落主题值）+ `ClearValue(TextElement.Foreground)`。搬移前对两个可能的父容器都做无害 Remove，防「已有逻辑父级」异常；整个 SyncImmersive 套 try/catch，异常只丢沉浸不伤功能。
- **视觉取舍**：① hero/截图保持清晰不加 BlurEffect——launch2.png 基准图是清晰全景，毛玻璃只给 TopBar scrim 与半透明块（与 WP-B 的近似毛玻璃一致）；② 页签选中下划线保持主题色（效果图是白色，刻意跟随用户主题）；③ 窗口钮悬停红块、Logo、账号圆沿用现有样式，沉浸下白字由 B.Ink/B.CaptionInk 覆盖自动成立。

## 验证数据（2026-11-07，Windows）
| 项 | compact | premium |
|---|---|---|
| 构建 | 0 错误 | 0 错误 |
| 测试 | **127/128**（唯一失败 PhoneLinkTests.FullFlow，基线既有，未碰） | 未单跑（代码是 compact 超集，测试工程不受 Premium 影响；白名单改动已随 compact 跑过） |
| TipCL.exe | **5,705,216 B** ≤ 5,707,264 红线，余量 **2,048 B**，与 WP-F 验收值**字节一致** | **6,000,128 B**（无上限，仅记录；+294,912 B ≈ hero.jpg 275,898 + 图标 16,300 + 代码） |

- 嵌入确认：`g.resources` 里 6 个 `premiumassets/*` 键齐全（含小写化，与既有 `resources/fonts/...` 同规则；WPF pack URI 查找大小写不敏感，同字体路径机制）。
- WIC 解码确认：6 个素材用同参数 BitmapImage 逐一解码 OK（含 forge_gh.png 的 JPEG 内容、grass.png 的调色板 PNG）。

## --shot 冒烟（premium exe，产物已清理未入库）
- `--shot launch --theme dark`：**走了 hero.jpg**（渲染出与 launch2.png 同款全景，而非纯渐变底；Bridge 已连、实例数据正常）；背景铺到标题栏下、TopBar 低透明度深色 scrim + 白字、页签红下划线、左侧深色渐变可读性正常。
- `--shot launch --theme light`：同上（第一轮发现「TipCL」标题继承了浅色主题深色 Ink → 加 TextElement.Foreground 修复后复验白色）。
- `--shot library`（dark+light，验证还原）：实例库页面完全正常——无 hero 残留、TopBar 回主题实底、Ink 回主题色 → OnHidden 还原路径可靠。
- compact 未跑 --shot（compact 路径代码零改动且 exe 字节一致，无需）。

## 遗留风险 / 备注
1. **壁纸与沉浸的边界**：用户在设置页换壁纸时不在启动页（OnHidden 已还原），理论时序漏洞仅存在于「停留启动页期间壁纸被程序化置为 Active」——下一轮 RefreshBackground/OnShown 即收敛，UI 上无入口。
2. 沉浸态截图/hero 的 `_bgShade` 渐变整窗铺满（含标题栏区），窗口很矮时内容仍居中于页面区，未发现布局问题；极端小窗未逐一截图。
3. `LoaderIcons.Url`（下载 URL 表）premium 下不编译——若有第三方代码引用会编不过（当前全仓库仅 InstallPage 用 `Tile`，已 grep 确认）。
4. premium 构建与 compact 共用 obj/，来回切开关靠 DefineConstants 触发重编，无残留问题（两版反复构建实测正常）。
5. 测试白名单加了 LoaderIcons.cs：若日后 premium 改为解码外部文件需重新评估（测试注释已写明）。
6. WP-G 未做：GOAL §6 全量手工测试两版各跑一遍（监工验收步骤）；交付改名复制 TipCL-compact.exe / TipCL-premium.exe。
