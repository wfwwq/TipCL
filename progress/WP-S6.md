# WP-S6 实例库双栏重做 —— 完成（2026-11-07，双 flavor 编译+截图+测试通过）

基于 77bdb0e（WP-S5 后）。用户 2026-11-07 实例库新效果图（priority 最高，取代 library2 单列卡片方案）。

## 布局实现（LibraryPage.cs 全量重写，约 660 行）
- **左栏白卡 420 宽**：标题「文件夹列表」18px ExtraBold + 当前文件夹路径灰字（悬停 Tooltip 全路径）；文件夹行 = 32×32 主题色软底文件夹图标 + 名称粗 13.5 + 「N 个版本」灰 12 + 选中态（B.AccentSoft 底 + B.Accent 红描边圆角 10）+「默认」红字软底小标签（B.AccentStrong 底）+ ⋯ 菜单（打开文件夹/设为默认/选择 Java/管理模组/存档管理/重命名/删除实例，与 VersionManagePage.FolderMenu 同项）。
- 分隔线下「添加或导入」组标题 + 4 按钮竖排 Stretch：添加已有文件夹(Chip)/导入整合包(Chip)/**新建实例(Soft 主题色软底强调)**/导入官方启动器(Chip)，各带图标。
- **右栏**：头部「版本选择」21px ExtraBold + 副说明灰字 + 右侧「共 N 个版本」+ 搜索框（placeholder「过滤版本名」，实时过滤）+ 刷新钮（\ue72c）。
- **加载器分组折叠**：组头 = LoaderIcons.Tile 官方图标 22（premium 嵌入图 fabric 布卷/grass 草方块；compact 首字母方块/Thumbs 下载）+ 组名（B.AccentText 主题色）+ 计数 (N) + 弹簧 + 折叠时「已折叠」灰字 + 箭头（\ue70e/\ue70d）；展开态存 Prefs.Expanded["lib:key"]。组序 fabric/forge/quilt/neoforge/optifine/liteloader/vanilla，组内按名称排序。
- **版本行 MinHeight 72**：36×36 圆角 9 图标（LoaderIcons.Tile，loader 归组同源）+ 名称粗 15 + 灰软底小标签（快照/模组 N/完全独立 IsolationLabel）+ 副行 12.5 灰（正式版 X，Loader · Java Y）+ 行尾三钮：收藏星（\ue735 实心 B.Accent / \ue734 空心灰）/ 卸载垃圾桶 / 设置齿轮；行间 1px B.RowLine 分隔线；悬停 B.RowHover。

## 数据/行为（零新后端，全部与 VersionManagePage 同源）
- 文件夹 = `get_instances`（版本数用 InstanceInfo.Versions）；当前文件夹路径 = InstanceInfo.Path，缺则 game_root_path 拼接。
- 版本 = `get_version_rows {include_hidden:false}`（隐藏版本不进实例库）；**切文件夹 = SetDefaultAsync 设默认后重拉**（VMP 既有模式，get_version_rows 返回当前实例）。
- 收藏 = **复用 Prefs.VmFavorites（与版本管理页同一存储，互相同步）**，未降级。
- 双击行 = LaunchVerAsync：SetDefault + LaunchPage.PreferInstance(inst, ver) + Navigate("launch")（与 VMP/实例详情同链路）。
- 卸载 = Dlg.Confirm + `uninstall_version`；设置 = VersionSetupDialog.ShowAsync 后重载。
- 「管理」入口：新布局无卡片无管理钮；实例详情 InstanceDetailPage 未动（详情页仍可从其它入口达，后续如需可在 ⋯ 菜单加项）。

## 复用/降级点
- **上次游玩时间：后端 VersionRowDto 无该字段，副行未显示该段**（按要求未发新请求）；Java 字段有（VersionRowDto.Java），已显示「Java 自动选择/17」等。
- 组头/行图标 compact 走既有 Thumbs 下载缓存，离线回落首字母方块（LoaderIcons 既有行为，未改）。
- 副说明/组名等在窄窗可能 CharacterEllipsis 截断（Trim），拉宽窗口即完整。
- 大标题「实例库」取消（效果图要求），页签导航仍显示实例库。

## 验证
| 项 | 结果 |
|---|---|
| compact 构建 | 0 错误，**TipCL.exe 5,727,232 B ≤ 5,757,264 B（余量 30,032 B）** |
| premium 构建 | 0 错误，6,024,192 B（仅记录） |
| 测试 | **127/128**（唯一失败 = 既有基线 PhoneLinkTests.FullFlow） |
| 截图 | `D:\项目\tipcl\windos\_wph_run\wps6\`：compact/premium × dark/light 共 4 张。双栏、红描边选中、默认标签、主题色组名+计数、官方图标（premium 嵌入图确认）、72 行+标签+副行+星/删/设三钮、明暗两主题均成立 |

## 自评贴图度
效果图逐项（双栏结构/文件夹行四要素/添加或导入 4 钮/右栏头部/分组折叠+已折叠/72 版本行+三钮+分隔线/无大标题）全部落实；差异仅「上次游玩」字段（无数据源，降级省略）与窄窗副说明截断。
