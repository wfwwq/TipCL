# M2-R2 · 下载页 / 安装页 / 加载器图标缓存 移植规格书

> 侦察对象（Windows 版，只读）：
> - `D:\项目\tipcl\windos\tipcl\src\TipCL\Pages\DownloadPage.cs`（265 行）
> - `D:\项目\tipcl\windos\tipcl\src\TipCL\Pages\InstallPage.cs`（498 行，含 `#if PREMIUM` 分支，Linux 版按 **compact** 移植）
> - `D:\项目\tipcl\windos\tipcl\src\TipCL\Services\LoaderIcons.cs` + `Thumbs.cs` + `ThumbTile.cs`
> - 设计基准：`D:\项目\tipcl\windos\tipcl\docs\mockups-hark\v2\download2.png/.html`、`install2.png/.html`、`style2.css`
>
> 主题键现状：A2 已就位（`D:\项目\tipcl\linux\src\TipCL.App\Themes\Palette.Dark.axaml` 键名与 Windows 版一一对应，见 progress/M1-A2.md 交接与 Controls.axaml：Seg.Rail / Seg.Item / Btn.Primary/Soft/Ghost / Switch / Input / Card / Tag / T.Small / T.Muted / CR.Card=12 / CR.Ctl=8）。**下文所有 B.* 键名可直接使用，无需映射。**
> Linux 侧 Ui 门面已有：`src/TipCL.App/Ui/UiHelpers.cs`（Txt/H1/Small/Muted/Glyph/V/H/G/Card/Pane/Btn/Input/Combo/Check/Switch/Tag/Badge/Field/Section/Empty）。

---

## 1. 结构树（区块 → 尺寸/间距/字号/颜色键）

### 1.1 下载页 DownloadPage（download2）

页面本体是 `_shell` Grid（行 `Auto,*`），**不是滚动页**；原版分段内部才滚动。

```
DownloadPage (PageBase, Title="下载")
├─ top 行（Grid cols "*,Auto"，Margin 22,16,22,12）
│  ├─ Seg.Rail（灰色分段轨道，HorizontalAlignment=Left，Padding 3 / 圆角 10 / B.SegRail）
│  │  └─ HStack(gap 4) × 7 个 Seg.Item（ToggleButton）：
│  │     原版 | Mod | 整合包 | 数据包 | 资源包 | 光影 | 世界
│  │     字号 13，选中=B.SegOn 白底+粗体（样式已在 A2 Controls.axaml）
│  └─ 任务按钮（Btn.Normal 无边框，HStack gap 6）：
│     ├─ Glyph \ue896 (13px 下载箭头)
│     ├─ "任务" 13px 粗体
│     └─ 数字角标：Pane(bg=B.AccentStrong, r=8) 内 11px bold B.OnAccentStrong，
│        Padding 6,0,6,1；数字 = TaskStore.RunningCount；点击 → Navigate("tasks")
├─ _host（ContentControl，占满剩余）
│  ├─ 分段 0（原版）= BlockHost("download", 宿主边距 0) 持久化两个区块：
│  │  ├─ Block "filters"（筛选行，Grid cols "*,Auto"，Margin 22,0,22,2）
│  │  │  ├─ 搜索框 Ui.Input("搜索版本")，flex=1
│  │  │  └─ HStack(gap 4, 左边距 8) × 3 筛选钮 BtnKind.Ghost：
│  │  │     正式版 / 快照 / 远古；选中项换 Btn.Soft + Bold
│  │  └─ Block "list"（版本列表卡，Margin 22,0,22,16）
│  │     Card(圆角 12, B.Card/B.CardBorder)，Padding 0,4,0,4
│  │     └─ 虚拟化列表，行高 46，行内 Padding 18,0：
│  │        Grid cols "130,*,110,Auto"
│  │        ├─ 版本号 13.5 bold B.Ink
│  │        ├─ 类型 Tag：最新 release / 最新前一条 snapshot 用 Tag(fg=B.AccentText,bg=B.AccentSoft)，
│  │        │  其余 Tag 默认(B.InkMuted/B.LineSoft)
│  │        ├─ 日期 12.5 B.InkFaint
│  │        └─ "安装"钮（默认 Btn.Soft，行 hover 换 Btn.Primary；\ue896 图标）
│  │        行顶 1px 分隔线 B.RowLine（首行无线）；行 hover 背景 B.RowHover
│  └─ 分段 1..6 = 内嵌 CatalogPage(CatalogKind.Mod/Modpack/Datapack/ResourcePack/Shader/World)
│        （CatalogPage 属另一侦察兵范围，本页只做容器与缓存）
└─ 任务计数联动：TaskStore.Added / TaskStore.Updated(Finished) → SyncTasks
```

要点：
- 旧筛选行间距 12 = BlockHost 区块间距 10 + 行自留下边距 2（注释原话），移植时保持逐像素一致。
- `_all` 数据两段式：先 `get_version_list`（本地缓存清单）立即渲染，再 `fetch_version_list`（远程刷新，超时 `FetchTimeoutMs=9000`）成功后覆盖；远程失败且本地为空时 Toast 警告「版本清单刷新失败」。
- 筛选逻辑：正式版=`Type=="release"`，快照=`"snapshot"`，远古=`StartsWith("old_")`；叠加搜索子串（OrdinalIgnoreCase）。

### 1.2 安装页 InstallPage（install2）

下载页的**子视图**（替换 PageBase.Content，不进导航注册），构造参数 `(version, typeLabel, back)`。

```
InstallPage (UserControl, root Grid rows "Auto,*,Auto")
├─ head（HStack gap 10，Margin 22,14,22,10）
│  ├─ 返回钮：32×32 Btn.Normal，Glyph \ue72b（左箭头），点击回调 back()
│  ├─ 标题 "安装 Minecraft {version}"：20px ExtraBold（Windows 版对 Ui.H1 覆写）
│  └─ Tag(typeLabel, fg=B.Ok, bg=B.AccentSoft)
├─ body（Ui.Scroll，Margin 22,0,22,16；VStack gap 0）
│  ├─ Label("实例名称") 12.5 bold B.InkMuted，Margin 2,8,0,7
│  ├─ 名称输入行（Grid 覆盖）：Input 高 38，Padding 10,3，SemiBold，placeholder=版本号；
│  │  右内叠 _nameHint（Small，右对齐 Margin 0,0,14,0，IsHitTestVisible=false）：
│  │  名字已存在→「装入已有实例」，否则→「将新建实例」
│  ├─ Label("模组加载器")（top 14）
│  ├─ 加载器 5 卡（Grid cols "*,*,*,*,*"，卡间距 5，首卡左/末卡右 0）
│  │  每卡 = Card(padding 11)，BorderThickness 1.5，Cursor=Hand：
│  │  ├─ 图标 Tile 36×36 圆角 9（LoaderIcons.Tile，见 §3）
│  │  ├─ VStack(1)：名称 14 bold + 副文本 Small（i=0「纯原版」，其余=推荐版本号或「…」/「不支持此版本」）
│  │  └─ 选中角标：16×16 圆形 B.Accent，右上 Margin 0,9,10,0，Glyph \ue73e 9px B.OnAccent，Tag="chk"
│  │  选中态：BorderBrush=B.Accent / Background=B.AccentTint + 显示角标；
│  │  未选：B.CardBorder / B.Card；不支持：IsEnabled=false + Opacity 0.45
│  ├─ 开关卡 vbox = Card(VStack(0)：_verRow/_apiRow/OptiFine行/LiteLoader行)，Padding 0,4，Margin top 10
│  │  每行 = SepRow(Row(...))：Grid cols "Auto,*,Auto" 高 42，行内 Margin 16,0，顶 1px B.RowLine
│  │  ├─ 版本行 _verRow：「{加载器} 版本」13.5 + 右侧 ComboBox 宽 220（仅 i>0 显示）
│  │  ├─ Fabric API 行：「Fabric API 13.5」+ hint Small「大多数 Fabric 模组都需要」+ Switch
│  │  ├─ OptiFine 行：13.5 + _ofWhy Small（不兼容原因）+ Switch
│  │  └─ LiteLoader 行：13.5 + Small「仅老版本可用」+ Switch（默认 Collapsed）
│  └─ （PREMIUM 分支的 _pathHint / _apiVer / Fabric API 版本号行：Linux compact 不做）
└─ footBar（Border，Padding 22,12，顶 1px B.RowLine，背景 B.GlassStrong）
   Grid cols "Auto,*,Auto"：
   ├─ HStack(6)：Muted「将安装」+ _sum 13 bold + _size Muted（「· 约 N MB」，ToolTip=估算说明）
   └─ 开始安装钮 Btn.Primary 高 38 Padding 20,0，Glyph \ue896
```

## 2. 交互清单与 Core 调用点

### 2.1 下载页

| # | 交互 | 行为 | Windows 调用点 | Linux 落点 / 缺口 |
|---|------|------|----------------|-------------------|
| D1 | 分段切换 | 7 段互斥，切换时调旧子页 `OnHidden`、新子页 `EnsureLoadedAsync+OnShown`，懒建 CatalogPage | 纯 UI | 需 Linux 版 PageBase 生命周期约定（M2 框架代理定） |
| D2 | 任务角标 | `TaskStore.RunningCount` 实时刷新（Added/Finished 事件，Dispatcher 回 UI 线程） | TaskStore | **缺口：TaskStore/BridgeClient 未搬**（M1-A4 报告：BridgeServer/RpcDispatcher/BackendApi 未搬，留 M3）。M2 需一个轻量任务中心抽象或桩 |
| D3 | 搜索 | 输入即滤 | 纯 UI | 无缺口 |
| D4 | 三段筛选 | 正式版/快照/远古，单选高亮 | 纯 UI | 无缺口 |
| D5 | 版本清单 | 两段式加载（本地→远程刷新） | `Api.TryCallAsync("get_version_list")`、`Api.CallAsync("fetch_version_list", token 9s)` | **缺口**：Linux Core 无 BackendApi/RPC。数据层应改为直调 `TipCL.Core/Launcher/Manifest.cs`（版本清单）+ `Net.cs`（远程拉取）；返回模型对齐 Windows `VersionRow`(Version/Type/TypeLabel/Date) |
| D6 | 点「安装」 | `OpenInstall(row)` → 整页切 InstallPage | 纯 UI | 无缺口 |

### 2.2 安装页

| # | 交互 | 行为 | Windows 调用点 | Linux 落点 / 缺口 |
|---|------|------|----------------|-------------------|
| I1 | 加载器 5 卡选择 | 不安装/Fabric/Forge/NeoForge/Quilt；加载后按支持性禁用（Opacity 0.45）；默认选卡 0 | 纯 UI | 无缺口 |
| I2 | 加载器版本探测 | 并发 6 请求（fabric/forge/neoforge/quilt/optifine/liteloader × 当前 MC 版本）；卡副文本=推荐版本 `Best(l)`（首个 Stable 否则 [0]）；无可装→「不支持此版本」并禁卡；OptiFine 无→开关禁用+说明文案；LiteLoader 无→藏行 | `TryCallAsync("list_loader_versions", {mc_version, loader})` | **缺口**：直调 `TipCL.Core/Launcher/LoaderMeta.cs`（已搬）；返回 `LoaderVer(Id/Stable)` |
| I3 | 版本下拉 | 选中卡才显示；列表=全部版本，推荐项尾缀「 · 推荐」；默认选中 Best；列表为空兜底「最新」 | 纯 UI + Ui.Combo(220) | 无缺口 |
| I4 | Fabric API 开关 | 仅卡 1(Fabric)/4(Quilt) 显示且自动勾上；换卡重置 | 纯 UI | 无缺口 |
| I5 | OptiFine 开关 | 仅卡 0(不安装)/2(Forge) 且探测有货时可用；不可用置灰并写原因：无货→「此版本没有可用的 OptiFine」，冲突→「与 {加载器} 不兼容，可改装 Sodium + Iris」 | 纯 UI | 无缺口 |
| I6 | 实例名联动 | 自动命名：卡 0=版本号，否则 `{加载器} {版本}`；用户手改后不再自动覆盖（_autoName）；hint 实时提示装入/新建 | `get_instances`（名字列表） | **缺口**：直调 `TipCL.Core/Launcher/Instances.cs`，模型 `InstanceInfo(Name/Path)` |
| I7 | 底部汇总栏 Sync() | 拼 `版本 + 加载器 ver + Fabric API + OptiFine + LiteLoader`；体积估算表 Mb=[30,1,15,15,2]，Fabric API +2、OptiFine +7、LiteLoader +1 | 纯 UI | 无缺口 |
| I8 | 开始安装 | 名为空→Toast；名字不存在先 `create_instance {name}`；`StartTaskAsync("install_game", {version, loader(卡0=""), loader_version, instance, extra:{optifine, liteloader}})`；Fabric API 勾选时挂 TaskStore.Updated 等主任务 Finished&&Success 再追加 `install_mod {name:"Fabric API", instance, extra:{source:"Modrinth", slug:"fabric-api", mc_version}}`；随后 `FlyToTasks` + Toast「已加入队列」+ `Navigate("tasks")`；期间 _go 禁用，finally 恢复 | `install_game` / `create_instance` / `install_mod` | **缺口最大**：任务管线未搬。install_game 实现在 Windows `Core/BackendApi.cs:2720`（`_install_game_impl`），Linux Core 已有 `GameInstall.cs`/`Installer.cs`/`Optifine.cs`/`Liteloader.cs`/`Instances.cs` 可支撑；M2 建议由 Core 包一个 `InstallFlow` 门面（create → install_game → 可选 fabric-api），任务进度用本地 TaskStore 抽象 |
| I9 | 离线降级 | 全部探测 TryCall 静默失败：各卡保留可点（版本下拉兜底「最新」）；Fabric API 版本号（PREMIUM）离线不显示 | — | compact 不做，无缺口 |

### 2.3 需 M2 框架先行定案的公共件（本页依赖）

- `PageBase`（Title/LoadAsync/OnShown/OnHidden/OnEvent/Run/Toast）——Windows `Pages/PageBase.cs` 70+ 行，A3 的 Placeholder 页结构可参考。
- `BlockHost/BlockSpec`（布局自定义持久化，Windows `Pages/BlockHost.cs`）——M2 有独立「页面布局自定义框架」工作包，本页只约定两个 Block：`filters`、`list`，宿主边距 0。
- `TaskStore`（TaskRow/Added/Updated/RunningCount）+ `StartTaskAsync`——任务页/下载角标/安装队列三方共用。
- `I18n.L`（中文直出即可，键=中文原文）。
- 窗口服务：`Window.Navigate("tasks")`、`Window.Toast(title, body, kind)`、`Window.FlyToTasks(btn, text)`（FlyToTasks 是飞行动画，Linux 可先降级为直接 Navigate）。

## 3. 加载器官方图标：Avalonia/Linux 落地建议

Windows compact 路线：不嵌图，`LoaderIcons.Url()` 给出 5 个官方源（github.com/{FabricMC,MinecraftForge,neoforged,QuiltMC}.png?size=64 + minecraft.wiki 草方块），`Thumbs` 服务（内存 LRU + 后端 `ensure_thumb` 落盘 cache_dir/thumbs + 8 并发闸门 + 失败 5 分钟冷却 + inflight 去重），`ThumbTile` 显示前异步换图，未命中显示**名称首字母方块**（B.AccentSoft 底 / B.AccentDeep 字 / 字号 max(12, size*0.4) / 圆角）。

Linux 建议（全部 Avalonia 原生，无 WPF 依赖）：

1. **缓存目录**：`%XDG_DATA_HOME | ~/.local/share/TipCL/thumbs/`，文件名 = `sha256(url)|px 的 hex .png`（沿用"url|px"键语义）。可先用 `Environment.GetFolderPath(SpecialFolder.ApplicationData)`（Avalonia/.NET 在 Linux 下已映射到 XDG 路径）。
2. **实现形态**：一个 `ThumbService`（仿 Thumbs.cs 的 8 信号量 + ConcurrentDictionary 内存缓存 + 失败冷却 + inflight 去重），下载用 `TipCL.Core/Launcher/Net.cs` 已有能力；解码用 `ImageSharp` 不可用（红线：M2 不加包的话）→ 用 Avalonia 自带 `Bitmap.DecodeToWidth(stream, px)`（Skia 内置 PNG/SVG 解码，够用且零新依赖）。
3. **ThumbTile 移植**：`Panel{ 首字母 TextBlock, Image(初始 Collapsed) }`，`SetUrl` 拿 token 防竞态；图像解码在 `Task.Run` + 回 UI 线程赋值。
4. **离线回退**：磁盘未命中且网络失败 → 永久保持首字母方块（草方块 vanilla 用字母 "V" 或 "M" 可接受；mockup 里 vanilla 是草方块图，离线回退无碍）。失败冷却 5 分钟，避免列表滚动时反复打网络。
5. **可选增强（报备后再做）**：把 mockups 里的 5 张小图（grass.png、fabric_gh.png、forge_gh.png、neoforge_gh.png、quilt.svg，均在 `docs/mockups-hark/v2/`）作为**嵌入式兜底**资源进 App 工程（共 ~20KB），首启即有图、离线也有图——这等效 Windows PREMIUM 路线且免去运行时下载；与 compact 的差异只在体积红线（Linux 无此红线）。**建议采用**：省掉 §2 大半复杂度，ThumbService 退化为纯内存缓存。SVG（quilt.svg）Avalonia 需 Avalonia.Svg 包——若不想加包，可仅嵌 4 张 PNG、quilt 走首字母方块或转 PNG。

## 4. 工作量估计与建议拆包

| 工作包 | 内容 | 估计 |
|--------|------|------|
| R2-P1 | 下载页：分段轨道 + 筛选行 + 版本列表（虚拟化/行 hover）+ 任务角标；依赖 PageBase/BlockHost 契约 | 1.5~2 人日 |
| R2-P2 | 安装页：5 卡 + 开关联动 + 汇总栏 + 安装流（I1-I9） | 2~2.5 人日 |
| R2-P3 | ThumbTile + 图标方案（嵌图兜底 or 运行时下载缓存） | 0.5 人日 |
| R2-P4 | Core 桥：VersionRow/LoaderVer/InstanceInfo 模型 + Manifest/LoaderMeta/Instances/InstallFlow 门面 + 本地 TaskStore 抽象 | 1~1.5 人日（与 M2-Core 工作包合并可省） |
| 前置 | CatalogPage（Mod/整合包等 6 分段）可后接，本页先空占位或藏段 | 另计（R3 范围） |

建议：P4 并进 M2 的 Core 桥工作包（任务中心属公共件）；P1/P3 可并行；P2 在 P4 之后。风险项：任务管线（I8）是全 M2 最厚的依赖，若任务中心延后，安装页可先做到「Sync 汇总 + 生成参数」并留 TaskStore 桩。

## 5. 移植注意细节（易漏）

- 分段是 `ToggleButton` 组，切页不重载数据（_subs 缓存）；CatalogPage 子页要接 `OnEvent(BridgeEvent)` 广播转发。
- 版本行首行无顶部分隔线（`BorderThickness(0,1,0,0)` 条件判断 `_list.Items[0]==r`）；Avalonia ListBox 虚拟化下建议改用 ItemsControl.ItemContainerTheme 或在 DataTemplate 里用 `ItemsControl.Index` 等价物——需验证 Avalonia 11 虚拟化方案（`VirtualizingStackPanel`）。
- 行 hover 换按钮样式（Soft→Primary）在 Avalonia 用 `:pointerover` 伪类选择器实现，别用 C# MouseEnter。
- 安装页 `Select(0)` 在构造末尾先调一次（此时 _vers[0] 为 null，版本行隐藏，不炸）；LoadAsync 完成后再 `Select(_sel)` 刷新支持性。
- `_name.TextChanged` 用 `IsKeyboardFocusWithin` 区分程序赋值与用户输入——Avalonia 等价 `IsKeyboardFocusWithin` 同名存在，或比较新旧值。
- 字体/字号常量：正文 13、行版本号 13.5、日期 12.5、卡副文本 11.5、角标 11、页标题 20(ExtraBold)、卡名 14(bold)。
