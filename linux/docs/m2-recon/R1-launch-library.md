# M2-R1 侦察规格 · 启动页 / 实例库 / 实例详情 / DashLayout

> 侦察日期 2026-11-08。Windows 参照：`D:\项目\tipcl\windos\tipcl\src\TipCL\Pages\{LaunchPage.cs(1922行), LaunchPage.Popups.cs(338), LibraryPage.cs(1075), InstanceDetailPage.cs(306), DashLayout.cs(167)}`。
> 设计基准：`docs/mockups-hark/v2/launch2.png + launch2.html + style2.css`、`library2.png + library2.html`。
> 主题键现状：A2 的 `src/TipCL.App/Themes/`（Palette.Light/Dark.axaml、Controls.axaml）**已落地**，`B.*` 颜色键与 `Btn.* / Seg.* / T.* / Card` 样式键齐全（progress/M1-A2.md 尚未写出，但键可直接引用）。

---

## 1. 结构树与视觉规格

### 1.1 LaunchPage（启动页，launch2）

根：`Grid`，行定义 `* , Auto`（英雄区吃满余量，区块行 Auto），`ClipToBounds=true`。

```
Grid root（行: *, Auto）
├─ [背景层, RowSpan=2] Image _bgImage（UniformToFill, IsHitTestVisible, 默认 Collapsed）
├─ [背景层, RowSpan=2] Grid _bgShade（两块线性渐变矩形, rgba(10,14,22,·)）
│   ├─ 左→右: alpha 199@0 / 115@0.42 / 0@0.75   （HTML .sh 第一段）
│   └─ 下→上: alpha 140@0 / 0@0.35              （HTML .sh 第二段）
├─ [Row0] SPanel _hero（垂直, 左对齐, MaxWidth 680, Margin 56,0,24,0, 垂直居中）
│   ├─ kicker 行（间距7）: 7×7 圆点(B.Ok) + _kickerText(12.5px, 截断省略)
│   ├─ 标题行（间距10, Margin 0,6,0,2, 手型光标→开实例切换浮层）
│   │   ├─ _heroTitle: 34px ExtraBold（HTML .t 同款）, MaxWidth 620, 截断
│   │   └─ chevron 字形 \uE70D 18px, Opacity .7
│   ├─ _chipRow（水平, 间距6, Margin 0,10,0,24）: Chip×N（圆角8, 边1, Padding 10,3,10,4, 字12）
│   ├─ launchRow（水平, 间距10）
│   │   ├─ _launchShell: Border 高48 圆角12 Padding 30,0, 背景键 B.AccentStrong，
│   │   │   DropShadow 柔光(Blur20/Depth6/Dir270, 色取主题主色, onImage 时 Opacity .45 否则 .3)，悬停上浮 1.02
│   │   │   └─ Button(Btn.Ghost 去壳): 字形\uE768 17 + 「启动游戏」16px ExtraBold
│   │   │       + 1×20 竖线分隔(Op .45) + 版本全名 13px Op .85（有版本时显示）
│   │   ├─ _stopShell: 同尺寸 Border, 背景键 B.DangerSoft, 内「停止」15px Bold 字形\uE71A, 默认 Collapsed
│   │   ├─ _configPill: 48×48 玻璃图标钮 \uE713（配置浮层锚点）
│   │   └─ 联机玻璃钮: \uE701 + 「联机」14px Bold → Navigate("multiplayer")
│   └─ statusRow（Margin 0,14,0,0, 间距10, 空闲自动收起）: ProgressBar(高4 宽320, B.Accent, Op .92) + _status 12px
└─ [Row1] BlockHost("launch") 内容区块, contentMargin 56,0,24,18, MaxHeight=窗口高×0.62
    ├─ news    「新闻」    _newsCard（玻璃块, 默认可见）
    ├─ tasks   「下载任务」 Ui.Card(_taskHost),   默认收起
    ├─ playtime「游戏时长」 Ui.Card(_playTotal 20px SemiBold), 默认收起
    ├─ notes   「便签」     Ui.Card(_notes 多行),  默认收起
    └─ log     「日志」     Ui.Card(_log) 高240,   默认收起
```

**玻璃块规则**（GlassButton / Glass / Chip，HTML `.gl .chip .news`）：有截图背景时 白系——底 rgba(255,255,255,36)、悬停 56、边框 56、字白；无截图时回落主题键 `B.Paper / B.Hover / B.Line / B.Ink`。双态切换集中在 `ApplyHeroTone(bool onImage)`。

**hero 色调**：白字 (W100/W80)、标题投影(Blur12/Depth2/Op .3)；无截图时全部资源引用回 `B.Ink / B.InkMuted` 并去投影。

### 1.2 启动页浮层（LaunchPage.Popups.cs）

统一外壳 `PopShell`：Border 圆角12、边1（B.Paper/B.Line）、Padding 12、投影(22, .16, 4)；`MakePopup` 把 MaxHeight 绑到 `窗口高×0.86`（Avalonia 用 `Popup` + 绑定 Window Bounds, 见 §3）。

| 浮层 | 宽 | 锚点 | 内容 |
|---|---|---|---|
| 配置 | 360 | _configPill 下方 | 标题「启动配置」+关闭钮；`BuildConfig`：Field(实例/版本/用户) + 「更多设置」折叠区(Java/内存滑条/分辨率 854×480/直连) + 分隔线 + 微软登录(Soft)/皮肤站(Chip)/更多操作(Icon) |
| 实例切换 | 400 | 标题行下方 | 搜索框(放大镜\uE721) + 实例行(名13.5 Bold+副行11; 当前行展开版本子列表) + 版本子行(高46, 缩进26, 选中=主题色字+勾) + 「打开实例库」Ghost 钮 |
| 新闻 | 420 | _newsCard 上方 | Scroll(_newsHost)：新闻列表(12.5 Bold 标题 + 12 正文) |

### 1.3 LibraryPage（实例库，library2）

根：`PageBase.ScrollBody(Grid _body 行高Auto)`；`_body` 双栏 `*, 18, 2*`（可经布局文档改为单栏 `*`）。

```
_body (Grid, 列 *,18,2*)
├─ 左栏 _leftCard: Ui.Card(V(10), Padding 14), MinWidth 300 / MaxWidth 380, 顶对齐
│   ├─ 「文件夹列表」18px ExtraBold
│   ├─ _folderPath 小字(B.InkMuted, Tooltip=完整路径)
│   ├─ _folders (StackPanel): FolderRow×N —— 圆角10, Padding 10,9, 边1, Margin 底6
│   │   ├─ 32×32 圆角8 图标块(B.AccentSoft 底 + \uE8B7 14px B.AccentText)
│   │   ├─ 名称13.5 Bold + 「N 个版本」小字
│   │   └─ 尾部: 默认 Tag(OnAccentStrong/AccentStrong) + ⋮ 图标钮(实例操作菜单)
│   │   选中/悬停态: 底 B.AccentSoft; 边 B.Accent / B.AccentLite; 常态 B.Paper2 / B.Line
│   ├─ Sep + 「添加或导入」13 Bold
│   └─ 四钮(等宽): 添加已有文件夹(Chip)/导入整合包(Chip)/新建实例(Soft)/导入官方启动器(Chip)
└─ 右栏 _rightCol: V(12), 顶对齐
    ├─ 头部两行 Grid(行2, 列6): 「版本选择」21 ExtraBold + 计数 + 搜索框(170) + 「实例详情」Chip + 「刷新」Chip
    │                          第二行副说明 12.5 B.InkMuted 换行
    ├─ _groupsHost: 按加载器分组卡片（7 组: fabric/forge/quilt/neoforge/optifine/liteloader/vanilla, 空组不画）
    │   ├─ 组卡 Ui.Card(V(0), 圆角0) Margin 底10: 组头(Padding 12,8, 手型) + Sep + 行体(Padding 8,2,8,8)
    │   │   组头: LoaderIcons.Tile(22) + 「XX 版本 (N)」13.5 Bold B.AccentText + Spring + 「已折叠」小字 + chevron
    │   │   折叠态持久化: Prefs.Expanded["lib:"+key]
    │   └─ 版本行 MinHeight 72, Padding 10,8: 图标 Tile 36 + 名称15 Bold + 标签Tag(快照/模组N/隔离) +
    │       副行12.5(正式版/快照/旧测试/旧阿尔法 + Mc + 加载器 + Java + 上次游玩) +
    │       尾部 ☆收藏 / 卸载\uE74D / 版本设置\uE713（行间 B.RowLine 细线, 悬停 B.RowHover）
    └─ _empty: Ui.Empty(\uE80A, 「这个文件夹还没有版本」, 「点左侧…」)
```

设计图 library2 的三列卡片网格是旧概念稿，**现行实现以双栏列表为准**（代码注释明确 2026-11 效果图为准、左栏 420→300/380 弹性），移植照 LibraryPage.cs 而非 library2.html 的 .grid。

### 1.4 InstanceDetailPage（实例详情，页内切入，不注册导航）

```
Grid shell（行 Auto, Auto, *）
├─ head (Margin 22,16,22,12, 列 Auto,*,Auto): 返回IconBtn\uE72B + 标题(T.PageTitle)+meta 12px + 「启动」Primary(→LaunchNowAsync)
├─ bar (Margin 22,0,22,12, 列 Auto,*,Auto,Auto): Seg.Rail 包 5×ToggleButton(Seg.Item):
│   模组/存档/资源包/光影/设置 ｜ 共享/隔离灰字说明(12.5, MaxWidth 420) ｜ 版本ComboBox(MinWidth 200) ｜ 「更多管理」Chip
└─ _host: ContentControl —— 4 个内容分段 = Scroll(白卡 Card(_rows) 圆角0, Padding 0,4, Margin 22,0,22,16)
    行 Row(): MinHeight 46, Padding 18,4, 行间 B.RowLine, 悬停 B.RowHover, 左名称13.5 Bold+副行11.5 B.InkFaint, 右操作
```

- 分段行为：选中版本 `Isolated=false`（共享/大锅饭）→ 前 4 个分段隐藏只留「设置」，显示共享说明；有隔离则显示隔离说明。
- 「设置」分段内嵌 `VersionManagePage { Folder = _inst }`（M2 需一并移植或先占位，见 §4）。
- 「更多管理」→ seg0 开 ModsDialog、seg1 开 SavesDialog（这两个对话框 M2 另包处理）。

### 1.5 DashLayout（旧卡片画布布局文档）

`DashLayout.cs` 只剩**数据模型**（Grid 吸附 0/4/8/16/24、Items=DashCard{Id,Type,X,Y,W,H,Z,Hidden}、To/FromJson、Normalize、Default 3 卡）。可视树已不挂（LaunchPage 注释明示），M2 只需搬数据类供 PageLayoutStore/BlockHost 兼容旧布局文档，**不需要移植画布拖拽 UI**。

---

## 2. 交互清单与 Core 调用点

所有数据走 `PageBase.Api`（BridgeClient，HTTP JSON-RPC 到后端），后端方法定义在 Windows 版 `src/TipCL/Core/BackendApi.cs`（4851 行）。**A4 已明确不搬 BridgeServer/RpcDispatcher/BackendApi*（progress/M1-A4.md "不搬清单"），留 M3 评估**——这是 M2 最大缺口：页面编译可过，但没有 Api 门面时所有 `Api.TryCallAsync` 无着落。

### 2.1 需要的 RPC 方法（按页面）

**LaunchPage**：`get_instances`、`get_installed_versions`、`get_settings`/`get_setting`/`update_settings`、`java_combo_options`、`java_combo_label_for`、`set_instance_java`、`get_installed_mod_entries`（模组数 chip）、`cached_news`/`fetch_news`、`get_total_playtime`、`preflight_launch`、`launch_game`（StartTask）、`cancel_task`、`is_game_running`、`allow_multi_instance`、`set_multi_instance`、`start_microsoft_login`（+事件 `login_code`/`login_status`）、`start_authlib_login`、`authlib_presets`、`build_launch_command`/`get_launch_command`、`export_launch_script`、`create_desktop_shortcut`、`repair_version`、`open_instance_folder`、`homepage_mode`/`custom_homepage` 设置项。

**LibraryPage**：`get_instances`、`get_setting(default_instance)`、`game_root_path`、`get_version_rows`、`get_playtime`（sessions 派生上次游玩）、`create_instance`、`rename_instance`、`delete_instance`、`uninstall_version`、`import_existing_folder`（StartTask）、`migrate_official_launcher`（StartTask）、`detect_official_launcher`、`official_launcher_dir`、`scan_official_versions`、`set_instance_java`、`instance_java_label`、`open_instance_folder`。

**InstanceDetailPage**：`get_version_rows`、`get_mods_targets`、`get_installed_mod_entries`、`enable_mod`/`disable_mod`、`list_saves`、`open_save`、`get_installed_resourcepacks`、`get_installed_shaders`、`delete_resourcepack`/`delete_shader`。

### 2.2 Core 侧现状与缺口

- **已可用（A4 已搬，`src/TipCL.Core/Launcher/`）**：Instances.cs/Instance.cs（实例与版本扫描）、Mods.cs（模组条目）、Java.cs、Config.cs（设置存取）、Catalog/Installer/Downloader（下载安装）、MicrosoftAuthenticator/Authlib（登录算法）、Crash.cs。
- **缺口 A（最大）：RPC 门面与事件流**。Windows 版 UI 只认 `BridgeClient.CallAsync/StartTaskAsync/TryCallAsync` + `BridgeEvent` 流（`finished/progress/log/game_started/game_exited/crash/login_code`）。M2 必须先落一个等效门面（可以直接在 App 内做 `IApi` 进程内实现，把 BackendApi.cs 的方法映射到 Core/Launcher 的 C# 类），否则页面只能桩跑。建议 M2 开设工作包或向主 agent 报备接口契约：方法名/参数一律 snake_case JSON，与 BackendApi.cs 签名一致。
- **缺口 B：UI Preferences**（Win.Prefs：LastUser/Notes/Expanded/VmFavorites + SaveUiPrefs）——Windows 版挂在 MainWindow，M2 需在 App 侧建 `UiPrefs` 存储（JSON 落盘即可）。
- **缺口 C：TaskStore/TaskRow**（下载任务卡数据源）——`Services/TaskStore.cs` 是纯 C# 可直接搬。
- **缺口 D：PageBase 骨架**（LoadAsync/OnShown/OnHidden/OnEvent/Run/Toast/Navigate/EnsureLoadedAsync、ScrollBody、EnableDrop）——M2 依赖 A3 的导航框架；若 A3 未提供等价生命周期，M2 第一周先补齐 PageBase。

### 2.3 事件清单（LaunchPage.OnEvent 要复刻的分支）

`login_code`、`login_status`、`task_added`（刷任务卡）、`progress`（进度条+状态文案+速度）、`log`（日志追加, 上限2500行, 底部吸附）、`finished`（按钮态复原/失败弹崩溃 UI）、`crash`（CrashUi.ShowAsync→可选重启）、`game_started`（状态+进度≥92）、`game_exited`（退出码）。

---

## 3. 特殊依赖与 Avalonia 实现建议

1. **截图背景**：`LoadShot` 扫 `实例/screenshots` 与 `versions/<v>/screenshots` 最新 png/jpg，FileStream(ReadShare) + DecodePixelWidth 1600。Avalonia：`Bitmap.DecodeToWidth(stream, 1600)` 后台线程解码，UI 线程赋值即可（Avalonia Bitmap 天生可跨线程）。去重 key = 全路径+LastWriteTimeUtc.Ticks 原样保留。
2. **hero.jpg**：Windows 版 pack URI 嵌入 `PremiumAssets/hero.jpg`。Avalonia 用 `avares://TipCL.App/Assets/hero.jpg`（AssetLoader），静态缓存一份。源图取 `docs/mockups-hark/v2/hero.jpg` 拷入 App Assets（只读参照仓库拷出，不写回）。
3. **渐变遮罩 _bgShade**：WPF 两块 LinearGradientBrush 矩形 → Avalonia `LinearGradientBrush`（RelativePoint 百分比起点终点与 stops 一比一搬）；或单 Border 两层 Brush 叠加。数值照 §1.1。
4. **Popup 浮层**：WPF `Popup(PlacementMode.Bottom, StaysOpen=false, AllowsTransparency)` → Avalonia `Popup`（同样支持 Placement Bottom/Top、LightDismiss = `IsLightDismissEnabled=true` 对应 StaysOpen=false）。MaxHeight 绑窗口高×0.86：Avalonia 绑 `Popup.HostWindow.Bounds.Height` 需经 attached 行为或代码订阅 `Window.SizeChanged`——建议简化为 `Popup` 放在 `OverlayLayer` 并用代码设 MaxHeight，或写一个小 `WindowPctConverter` 挂窗口 Bounds。
5. **DropShadowEffect**：Avalonia 无 WPF Effect；用 `BoxShadow`（Border.BoxShadow="0 6 20 0 #B3C9403F" 之类）实现主按钮柔光与浮层阴影；标题 TextShadow 用 Avalonia 的 `TextBlock` 无原生投影 → 可用两层 TextBlock 或省略（建议省略, 视觉影响小）。
6. **HoverLift/Stagger/Motion 动画**：Windows 版 `Services/Motion.cs` 是 WPF 动画。Avalonia 用 `Transitions`（ScaleTransform + TranslateTransform）重写同名门面：HoverLift=悬停 Scale 1.02、Stagger=入场位移淡入、Progress=ProgressBar 平滑到目标值。M2 建议把 Motion 移植列为前置小包。
7. **DispatcherTimer**（内存 650ms 防抖保存）→ `DispatcherTimer` Avalonia 同名类，直接搬。
8. **DependencyPropertyDescriptor 监听**（statusRow 显隐联动）→ Avalonia 无等价物；改为在设置 `_status.Text`/进度条显隐处显式调 `UpdateStatusRow()`（约 3 处调用点）。
9. **DragDrop**（LibraryPage EnableDrop 整合包拖入）→ Avalonia `DragDrop` 事件（DataObject 文件列表），FileDrop 服务需移植。
10. **ContextMenu**：Avalonia `ContextMenu` + `MenuItem`，IsCheckable 对应 `ToggleType="CheckBox"`；Clipboard、`TopLevel.Launcher.OpenUriAsync`（替代 Process.Start 打 URL）、文件对话框用 Avalonia `StorageProvider`（替代 Dlg.PickFile/SaveFile/PickFolder）。
11. **版本浮层选择 / 实例切换**：纯代码构建列表，无特殊控件；注意 WPF `MouseLeftButtonUp` → Avalonia `PointerReleased`（并保留 `HitButton` 命中测试逻辑：`e.Source` 向上找 `ButtonBase`）。
12. **分段 ToggleButton**：`Seg.Rail`/`Seg.Item` 样式 A2 已有，直接用；共享/隔离显隐逻辑照搬。
13. **Wallpaper / IMMERSIVE 沉浸**：`#if IMMERSIVE` 全部代码（SyncImmersive/GlassTopBar）是 premium 构建专属，M2 **不搬**；`Wallpaper.Active` 依赖同样不搬，背景逻辑恒走「截图→hero」。
14. **WebViewHost / custom homepage HTML**：`homepage_mode=custom` 时 Windows 版只是读文本显示 Muted，非 WebView——照搬文本方案即可。
15. **字体图标**：Windows 版字形 `\uE7xx` 是 Segoe MDL2。Linux 无该字体。建议 M2 统一映射到 A2/A3 已定的图标方案（若外壳用矢量 Path，则页面把 \uE768/\uE71A/\uE713/\uE70D/\uE735/\uE8B7 等各处换 Ico 常量表）。**这是全局横切点，需主 agent 拍板**（约 20+ 个字形）。

---

## 4. 工作量估计与拆包建议

| 包 | 内容 | 估时 | 前置 |
|---|---|---|---|
| M2-R0（建议增设） | Api 门面（进程内 IApi，snake_case 契约对齐 BackendApi.cs）+ UiPrefs + TaskStore 搬运 + PageBase 生命周期 + Motion 门面 | 1.5~2 天 | A3 导航框架 |
| M2-R1a | LaunchPage 主体（背景/hero/主按钮行/BlockHost 五区块） | 1.5 天 | R0、图标方案拍板 |
| M2-R1b | LaunchPage 三个浮层 + 配置体 + 微软/皮肤站登录流程 + OnEvent 全部分支 | 1 天 | R1a |
| M2-R2a | LibraryPage 双栏 + 文件夹行 + 分组列表 + 文件夹菜单 | 1 天 | R0 |
| M2-R2b | InstanceDetailPage（5 分段）+ 依赖的 VersionManagePage 最小内嵌 | 1 天 | R2a；VersionManagePage 可先做只读占位 |
| 收尾 | --smoke 截图三页、对齐 mockups、遗留记录 | 0.5 天 | 全部 |

风险与备注：
- 最大风险是 §2.2 缺口 A（无 Api 门面则两页全部数据为空）。R0 不落，R1/R2 只能做出纯视觉骨架——建议主 agent 优先排 R0。
- `InstanceDetailPage` 依赖 `VersionManagePage` 与 `ModsDialog/SavesDialog`（其他侦察兵范围），需在 M2 总排期上对齐接口（`VersionSetupDialog.ShowAsync(inst, ver)` 也被两页引用）。
- LibraryPage 的收藏/折叠持久化挂 `Win.Prefs`，R0 的 UiPrefs 字段名保持一致（LastUser/Notes/Expanded/VmFavorites）以便以后接 Windows 配置迁移。
- 字号阶梯（12.5/13/13.5/15/18/21/34）超出 A2 阶梯表（13/14/17/21/24）——启动页 34px 大标题与 12.5 小字按页面局部值处理，不必进主题。

## 5. 交互清单速查（事件→动作）

LaunchPage: 启动游戏→LaunchAsync(预检→确认→StartTask launch_game)；停止→cancel_task；标题行→实例切换浮层；配置钮→配置浮层；新闻卡→新闻浮层；联机→Navigate；实例/版本/Java ComboBox→联动重载；内存滑条 650ms 防抖→update_settings；宽高/便签 LostFocus→保存。
LibraryPage: 文件夹行点击→设默认+重载版本；双击版本行→SetDefault+PreferInstance+Navigate(launch)；收藏星→VmFolders 切换；卸载→Confirm+uninstall_version；版本设置→VersionSetupDialog；⋮ 菜单（打开/设默认/选Java/管理模组/存档/重命名/删除）；添加文件夹/导入整合包/新建/导入官方；右键→自定义布局（显隐文件夹栏+导入导出）。
InstanceDetailPage: 分段点击→切换内容；版本下拉→ApplyVersionSegs；更多管理→Mods/Saves 对话框；启动→LaunchNowAsync；返回→back()。
