# WP-C 下载页 v2 + 安装页 + 加载器图标缓存（2026-11-06，状态：完成待验，未编译）

## 改动文件
| 文件 | 说明 |
|---|---|
| `src/TipCL/Pages/DownloadPage.cs`（新） | 下载页 v2。顶部 `Seg.Rail` 灰轨道 + 7 个 `Seg.Item`（原版/Mod/整合包/数据包/资源包/光影/世界）；右上「任务 N」白卡按钮（Btn.Normal 无边框，N=TaskStore.RunningCount，点击 `Navigate("tasks")`）。原版分段：搜索框 + 正式版/快照/远古 三选一筛选（选中 Btn.Soft 粗体，未选 Btn.Ghost）；列表在一张 `Card` 里（虚拟化 `Ui.VirtualList`），行高 46、左右 18、行间 `B.RowLine` 分隔线，悬停行底 `B.RowHover` 且「安装」按钮 Btn.Soft→Btn.Primary（实心）。数据走既有 `get_version_list`（缓存）+ `fetch_version_list`（超时用 `VersionPage.FetchTimeoutMs`）。其余 6 个分段各自懒建一个 `new CatalogPage(CatalogKind.X)` 内嵌（EnsureLoadedAsync/OnShown/OnHidden/OnEvent 都转发），业务零改动。「安装」→ 本页内容切到 InstallPage 子视图，返回键切回。 |
| `src/TipCL/Pages/InstallPage.cs`（新） | 安装页（UserControl，作为 DownloadPage 子视图，不进导航）。头部：32×32 白底返回键 + 「安装 Minecraft X」20px ExtraBold + 类型标签。实例名称输入（右侧提示「装入已有实例 / 将新建实例」；默认名随加载器自动变化，用户手动改过就不再覆盖）。5 张加载器卡（不安装/Fabric/Forge/NeoForge/Quilt）：图标 + 名称 + 推荐版本；选中 `B.Accent` 1.5 描边 + `B.AccentTint` 底 + 右上主题色圆形勾；该 MC 版本无此加载器时卡片 0.45 透明并禁用、副标题「不支持此版本」。下方白卡：所选加载器版本下拉（推荐版本标「· 推荐」，推荐 = 第一个 Stable，否则第一个）、Fabric API 开关（选 Fabric/Quilt 自动打开，其余隐藏）、OptiFine 开关（仅 不安装/Forge 且该版本有 OptiFine 构建时可用；否则置灰并写原因：「与 X 不兼容，可改装 Sodium + Iris」或「此版本没有可用的 OptiFine」）、LiteLoader 行（仅当 `list_loader_versions(liteloader)` 对该版本非空时显示，即老版本如 1.12.2）。底部固定栏：「将安装 X + Y + Z · 约 N MB」+ 主按钮「开始安装」。 |
| `src/TipCL/Services/LoaderIcons.cs`（新） | 加载器官方图标 URL 表（交接必读里的 5 个 URL）+ `Tile(loader, name, size)`：返回既有 `ThumbTile`（先显示名称首字母方块，异步经 `Thumbs.LoadAsync` → 后端 `thumb_path/ensure_thumb` 下载并缓存到 `Config.cache_dir/thumbs`，即本地数据目录；失败/离线吞异常，5 分钟冷却）。不嵌入任何图片。 |
| `src/TipCL/Pages/VersionPage.cs`（删减） | 删掉被替代的旧下载 UI：远程版本列表、类型复选、安装设置卡（实例/加载器/加载器版本/OptiFine/LiteLoader/安装按钮）、拉取/取消/失败横幅、LoadLoaderVersionsAsync、InstallAsync（约 -330 行）。保留「已安装版本」管理（独立模组开关、隔离细分、更多菜单、拖放）—— 这些功能别处没有（`toggle_version_isolation` 等只在此页）。实例下拉移到「已安装」标题行；新增「下载新版本」主按钮 → `Navigate("download")`。保留 `public static int FetchTimeoutMs`（Smoke.cs 引用，DownloadPage 也用）。 |
| `src/TipCL/Pages/TasksPage.cs`（删减） | 删掉 `HubRow()`（旧下载页 9 个带图标描边 chip），空状态文案改为「到「下载」页装个版本或模组试试」。 |

## 安装逻辑（全部复用既有后端，无业务改动）
1. 实例名不在 `get_instances` 里 → 先 `create_instance(name)`（既有 API）；在 → 直接装入该实例。
2. `install_game(version, loader=""/Fabric/Forge/NeoForge/Quilt, loader_version=所选Id, instance=name, extra={optifine, liteloader})`，与旧 VersionPage.InstallAsync 参数完全一致。
3. Fabric API 开着时：监听 `TaskStore.Updated`，等 install_game 任务成功结束后再发 `install_mod(name="Fabric API", instance, extra={source:"Modrinth", slug:"fabric-api", mc_version})`（既有 API；失败只 toast）。
4. 然后 FlyToTasks + toast + `Navigate("tasks")`（同旧行为）。

## ⚠ 必须由 WP-A / 主 agent 接线（我不能改 MainWindow）
DownloadPage 目前**没有注册**，下载页签仍指向 TasksPage。需在 `MainWindow.xaml.cs`：
```csharp
// NavDefs 里加一行
new NavDef("download", I18n.L("下载"), "\ue896", () => new DownloadPage()),
// TabTasks.Click 里 Navigate("tasks") → Navigate("download")
// SyncTabs 里 TabTasks.IsChecked = (_currentId == "download" || _currentId == "tasks");
```
（id 用 "download"：VersionPage 的「下载新版本」按钮已按此 id 跳转。"download" 同时是 NavModel 的分区键，但 GetPage 只查 NavDefs、PageForNavKey 无此键，不冲突。）
另：`LaunchPage.cs` 约 1490 行「没有版本」时 `Navigate("version")`，建议 WP-B 改为 `Navigate("download")`（现在会落到「已安装」页，页上有「下载新版本」按钮，仍可用）。
**接线前，原版安装入口只能经 VersionPage「下载新版本」按钮→（未注册时无反应）**，所以这三行必须在同一版本里落地。

## 未完成 / 取舍
- 体积估算「约 N MB」是启发式常量（原版 30、Fabric 1、Quilt 2、Forge/NeoForge 15、Fabric API 2、OptiFine 7、LiteLoader 1），不含共享资源文件，tooltip 已注明。后端没有体积查询 API。
- Fabric API 行不显示具体版本号（效果图有 0.92.2），需要额外请求，省体积未做。
- 「安装到 E:\.minecraft」路径提示未做，改为「装入已有实例 / 将新建实例」提示。
- 加载器卡图标用 ThumbTile 的 UniformToFill 铺满 36×36（圆角 9），不是效果图里“彩色底 + 30px 图”。
- 旧分区里的「模组管理(mods)」「Java」不在 v2 分段里：模组管理归实例详情(WP-E)，Java 归设置(WP-D)，其 NavDef 仍在，未删。
- 原版列表“新”标签规则：快照 + 最新正式版用主题色软底标签，其余灰标签。

## 编译风险点（无 dotnet，靠阅读）
- 资源键均已 grep 确认在当前 Controls.xaml / Palette.Light/Dark.xaml 存在：`Seg.Rail`、`Seg.Item`、`Card`、`Btn.Soft/Primary/Normal/Ghost`、`B.RowLine`、`B.RowHover`、`B.Card`、`B.CardBorder`、`B.AccentTint`、`B.Accent`、`B.AccentStrong`、`B.OnAccent(Strong)`、`B.AccentText/Soft`、`B.GlassStrong`、`B.Ok`、`B.LineSoft`、`B.InkMuted/Faint`。其中 `Seg.Rail/Seg.Item/B.Card/B.CardBorder/B.RowLine/B.RowHover` 是 WP-A 新增（基线没有），其余为基线已有——**WP-A 若改名需同步**。FindResource 找不到会在运行时抛异常，不是编译错误。
- 图标字形只用了源码里已有的 `\ue896 \ue72b \ue73e \ue7b8 \ue8b7`，IconFontTests 不受影响。
- `PageBase.Run(() => api.StartTaskAsync(...))`：Task<string> lambda 转 Func<Task>，合法。
- `Task.WhenAll(IEnumerable<Task<List<LoaderVer>>>)` 返回数组，赋给 `List<LoaderVer>[]`。
- InstallPage 非 PageBase，用 `AppServices.Client/Window` 和 `PageBase.Run`（public static）。
- `using CancellationTokenSource cts = ...;`（C# 8 using 声明，csproj LangVersion=12，可用）。
- VersionPage 保留 `using System.Threading;` 等可能未用的 using（只是警告）。

## 体积估计
删：VersionPage 约 330 行（含 4 个 async 状态机、~15 个闭包、Row 类）+ TasksPage HubRow。增：DownloadPage ~230 行 + InstallPage ~370 行 + LoaderIcons ~30 行（2+2 个 async 状态机、~15 个闭包）。`install_game`/`list_loader_versions` 的匿名类型与被删代码同形，可复用；新增匿名类型约 2 个。粗估净变化 **-1 ~ +3 KB**，有超红线风险，必须在 Windows 上实测 exe 大小；若超，可先删 InstallPage 的 LiteLoader 行/体积 tooltip，或把 DownloadPage 的筛选按钮改用 Ui.Check。
