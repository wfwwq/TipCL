# WP-E 实例详情页 —— 完成待验（2026-11-06，未编译，未 commit）

## 改动
| 文件 | 内容 |
|---|---|
| `src/TipCL/Pages/InstanceDetailPage.cs`（新，约 220 行） | 顶部：返回(IconBtn \ue72b) + 实例名(`T.PageTitle`) + 元信息一行 + 启动(`BtnKind.Primary`→Btn.Primary，接 `LaunchPage.LaunchNowAsync`，与实例库卡片同法)。`Seg.Rail`/`Seg.Item` 分段：模组/存档/资源包/光影/设置。列表放一张 `Card`（圆角 12）白卡，行 MinHeight 46、行间 `B.RowLine`、悬停 `B.RowHover`（同 DownloadPage 行）。 |
| `src/TipCL/Pages/LibraryPage.cs` | “管理”不再 `Navigate("instance")`，改为页内切换 `base.Content = new InstanceDetailPage(i, 返回→恢复 _shell)`（仿 DownloadPage→InstallPage）；新增字段 `_shell`。 |
| `src/TipCL/Pages/VersionManagePage.cs` | 仅加一个只写属性 `Folder`（预选实例），供“设置”分段内嵌。 |

## 各分段接的现有业务（未改逻辑）
- 模组：`get_mods_targets`（选 Value=="" 否则第一个，与 ModsDialog 默认一致）→ `get_installed_mod_entries`；开关 Checked/Unchecked → `enable_mod`/`disable_mod`（与 ModsDialog 同）。右上“更多管理”打开既有 `ModsDialog`（导入 jar、检查更新、目标切换、删除）。
- 存档：`list_saves`(version "") 行尾“打开”→`open_save`；“更多管理”打开既有 `SavesDialog`（备份/导出/删除/数据包）。
- 资源包/光影：`get_installed_resourcepacks`/`get_installed_shaders`，行尾“删除”(确认)→`delete_resourcepack`/`delete_shader`。
- 设置：内嵌 `new VersionManagePage { Folder = 实例名 }`（版本列表 + 每版本“设置”=VersionSetupDialog，文件夹菜单含 Java/重命名/导出/删除）。

## 未完成
- 资源包/光影无“打开目录/导入”（后端没有对应 UI 方法，只有 AI 工具 open_folder），未加。
- 模组行未做图标/版本号/作者；存档行未做缩略图（v1 效果图有，需额外接口，省体积）。
- 实例库切到详情后，若离开再回到实例库页签，仍停留在详情页（与 DownloadPage 安装页行为一致）。
- 旧导航 `instance`(VersionManagePage) 仍被 LaunchPage:844 使用，保留未删。
- 实例库卡片本身样式（`B.Paper`/`B.Line`/圆角 10）不在本包，未动。

## 风险
1. “设置”分段内嵌 VersionManagePage：它加载时会把选中实例设为**默认实例**（其既有 EnsureCurrentAsync 行为），且左栏仍可切到其它实例；两栏布局在详情页内较宽。
2. 分段切换时异步加载用 `_seg` 比对丢弃过期结果；开关事件在初值设置后才挂，不会误触发。
3. 依赖：`Ui.IconBtn/Btn/Switch/Card/Scroll/Empty/G/V/H/Txt/S`、`UiExt.M/VCenter/Trim/Show/Add`、`Dlg.Confirm`、`PageBase.Win.GetPage`、模型 `ModsTarget/ModEntry/SaveRow/InstanceInfo` 均已 grep 确认存在；样式键 `T.PageTitle/Seg.Rail/Seg.Item/Card/Btn.Icon`、画刷 `B.RowLine/B.RowHover/B.InkFaint/B.InkMuted` 均存在。csproj 为 SDK 风格，新文件自动编入。
4. 体积：新增一个 ~7 KB 源文件（IL 估计 +4~6 KB），**可能超出约 3 KB 余量**，需实测；超了可先删“更多管理”按钮或资源包/光影删除功能。
