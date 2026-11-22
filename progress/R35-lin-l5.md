# R35 · lin-l5 —— 布局 v3 WP-L5 落地到 Linux 树(Avalonia 11 / net8)

> 分支 `r35/lin-l5`(从 `r35/lin-base` 7039719 出发),工作区 `D:\项目\TipCL\_r35\lin-l5`。
> 规格:`windos/GOAL-layout-v3.md` §3 Q2/Q4/Q5、§5.5、§6.3「WP-L5」;效果图 `windos/docs/mockups-hark/v3/pages3.html`(实例库页按 Q2 作废)。
> 语义参照 Windows `r34/l5` 6277714(`PageDefaultLayouts` / `BlockHostRules` / 各页改名)。没有启动程序,没有截图。

## 1. 做了什么

| GOAL 条目 | 落地点(文件:行) | 说明 |
|---|---|---|
| §5.5 改名:联机 状态卡 / 房间卡 / 操作区 / 玩家列表 → 联机状态 / 房间 / 快捷操作 / 玩家 | `src/TipCL.App/Pages/MultiplayerPage.cs:101-104` | 只改 Title,id 不变 |
| §5.5 改名:游戏时长 累计卡 / 范围卡 / 明细列表 → 累计时长 / 时间范围 / 明细 | `src/TipCL.App/Pages/Settings/SettingsSubs.cs:679-682`(`PlaytimeView`) | Linux 游戏时长页是设置内层子视图 |
| §5.5 改名:Java 已检测列表 → 已检测的 Java | `SettingsSubs.cs:280-283`(`JavaView`) | |
| §5.5 改名:服务器 实例行 → 实例 | `SettingsSubs.cs:492-494`(`ServersView`) | 默认布局不变 |
| §5.5 改名:账号 账号卡 → 账号列表 | `src/TipCL.App/Pages/AccountPage.cs:100` | 默认布局不变 |
| Q4 默认布局(只在文档里**没有**该页时生效) | `src/TipCL.App/Pages/Layout/BlockHost.cs:60`(`PageDefaultLayouts`) | 4 份 12 列网格(= `LayoutSchema.ColsDefault`,与实例库同制;效果图 6 列只是示意);可见块写显式 x/y/w,默认隐藏块只给 span、坐标保持契约默认 0,0,1,1(显示时 `LayoutGrid.Pack` 自动流到末尾空位),并排在最后 |
| 联机:说明整行 / 联机状态 + 房间各半 / 快捷操作整行 / 玩家 + 节点说明 8:4 | `MultiplayerPage.cs:106` 挂 `PageDefaultLayouts.Multiplayer()` | 页边距 = 文档默认(原来就是) |
| 游戏时长:累计时长 + 时间范围各半 / 明细整行;**概览默认隐藏** | `SettingsSubs.cs:679` 挂 `PageDefaultLayouts.Playtime()` | 刷新 / 清空统计本来就在「时间范围」卡里,隐藏概览不丢按钮;页边距沿用 18,6,18,16 |
| Java:安装方式 + 实例默认各半 / 已检测的 Java 整行;**说明默认隐藏**,版本对应关系挪到「实例默认」提示 | `SettingsSubs.cs:226-231`(计数 / 刷新 / 扫描从标题行右列挪到列表块顶)、`:266`(版本对应句进「实例默认」卡)、`:280` 挂 `PageDefaultLayouts.Java()` | 同 Windows r34/l5:否则说明一隐藏,扫描按钮就找不到了 |
| 启动(只管下方内容区):新闻整行;其余四块仍默认隐藏,显示后两两并排、日志整行 | `src/TipCL.App/Pages/Launch/LaunchPage.cs:370` 挂 `PageDefaultLayouts.LaunchContent()` | contentMargin 56,0,24,18 不变 |
| 默认布局入口:`UseBlockHost` 增加带 defaultPage 的重载 | `src/TipCL.App/Pages/Settings/SettingsSubView.Layout.cs:38` | 默认里标 hidden 的块同步 `BlockSpec.DefaultVisible=false`;不带 defaultPage 的老重载行为逐字不变(服务器页仍走它) |
| Q5 ≥2 块才有「自定义布局…」 | `BlockHost.cs:159`(`BlockHostRules`)、`:318`(`EnterEdit` 先过 `OffersLayoutMenu`)、`:336`(`BlockHost.OffersLayoutMenu`) | 口径同 Windows:数**页面交给宿主的内置块**(含默认隐藏的,不含文档里的自建/插件块);入口表 `SingleBlock` 的页一律 false |
| 启动页编辑态:英雄区 45% + 「🔒 主区域固定，不能拖动或隐藏」 | `LaunchPage.cs:373`(订阅 `EditingChanged`)、`:393`(标签放在 root 第 0 行右上)、`:440-505`(`HeroEditOpacity` / `BuildHeroLock` / `ApplyHeroEditLook`) | 英雄区仍不进布局系统;编辑态英雄区 `Opacity=0.45`、`IsHitTestVisible=false`(防误点启动);标签不接点击,深色玻璃配色走 `B.EditDim` / `B.EditOutlineOnDark`(`TryGetResource` + 代码兜底色,找不到不抛) |
| 编辑态进出通知 | `BlockHost.cs:348`(`EditingChanged`)、`SyncEditing()`;构造里挂 `LayoutUpdated` | Linux `LayoutEditor` 没有编辑态事件,且「完成」/Esc/全局右键「完成编辑」都直接调编辑器、绕过 BlockHost;进出编辑必然重排(编辑条显隐 + 重渲染),所以在 `LayoutUpdated` 里比对 `IsEditing`;`EnterEdit` 与 Unloaded 收尾时也显式同步一次 |
| Q2 实例库:只改布局菜单文案、去掉内部说法 | **无需改动**(`Pages/Library/LibraryPage.cs`) | Linux 实例库**没有**自带的布局菜单(Windows `LibraryPage.cs:231-235` 那个 `LayoutMenu()` 在 Linux 不存在),右键走全局 `LayoutShellHooks`;两个块标题「文件夹列表 / 版本选择」无内部说法;grep 界面字符串无 `library:main` / 文件名 / 编号。故未动该文件 |

## 2. 文件清单

- 改:`src/TipCL.App/Pages/Layout/BlockHost.cs`(L5 独占)
- 改:`src/TipCL.App/Pages/MultiplayerPage.cs`、`src/TipCL.App/Pages/AccountPage.cs`(L5 独占)
- 改:`src/TipCL.App/Pages/Launch/LaunchPage.cs`(只动区块宿主段 + 新增编辑态英雄区几个成员)
- 改:`src/TipCL.App/Pages/Settings/SettingsSubs.cs`(Java / 服务器 / 游戏时长子页所在文件,§3 L5 行点名;只动这三个子视图的区块标题 / 默认布局 / Java 列表块组装)
- 改:`src/TipCL.App/Pages/Settings/SettingsSubView.Layout.cs`(不在任何包清单里;它就是这三页的区块宿主接线,只**新增**一个重载,老重载委托过去、行为不变)。合并时请留意。
- 新建:`tests/TipCL.Tests/Layout/PageDefaultsV3Tests.cs`(13 条)
- 新建:`progress/R35-lin-l5.md`(本文件)
- **未改**:`LayoutShellHooks.cs` / `LayoutEditor*.cs`(L12)、`LayoutBlockEntryCatalog.cs`(L4)、`LibraryPage.cs`(见上)、`locales/*`

## 3. 测试

| | 通过 | 失败 | 跳过 | 合计 |
|---|---|---|---|---|
| 开工基线(lin-base 代码,`-NoBuild -Test`) | 978 | 0 | 7 | 985 |
| 收尾(本分支) | 991 | 0 | 7 | 998 |

新增 13 条(`PageDefaultsV3ModelTests` 11 条纯数据 + `PageDefaultsV3HostTests` 2 条无头真宿主):
- 4 页默认的形状(12 列、坐标、默认隐藏只给 span、隐藏块排最后、页边距沿用);
- 默认 Pack 不重叠且可见块就在写定的位置;启动页把四块全显示后 = 任务/时长并排、便签下一行、日志整行;游戏时长/Java 显示被隐藏的概览/说明后落到第 3 行整行、不压块;
- `ForPage` 只映射这 4 页,每次返回新实例;
- `LoadState`:无文档 → 默认网格 + 隐藏;有文档 → 照文档(stack、顺序、显隐、`ExistedInDoc`);
- **无头真宿主**:临时布局目录里 4 页(`MultiplayerPage` / `PlaytimeView` / `JavaView` / `LaunchPage`)没有条目时渲染新默认、并且不会被默默写进文档;写入 4 页 stack 条目(自定顺序、各藏一块)后重建页面 → 完全照文档(默认隐藏的概览/说明在文档里可见就显示);
- 单块页:`settings:network`(1 块)、任意 1 块页、`feedback`(Linux 的「反馈与帮助」= Windows settings:about 的对应页,入口表标了单块)→ `OffersLayoutMenu=false`、`Editor.CanEdit=false`、`EnterEdit()=false`,`LayoutShellHooks.FindHosts` 找不到它们 ⇒ 全局右键不会出「自定义布局...」;2 块页可以;
- 改名后的 Title 逐条核对;启动页内容区 5 块仍可编辑;`EnterEdit` → 英雄区 0.45、不接点击、标签可见;`ExitEdit` + 同步 → 还原,事件只在真变化时触发。
- 现有 `LayoutMoreTests`(L4 文件)的 `WiredPages_ExposeBlockHostsWithWindowsPageIdsAndBlocks`、`AddBlockToNewlyWiredPageHeadlessTests`(往 java 页加块)都照旧通过,没有改动别人的测试;没有删任何断言。

## 4. 新增 / 废弃词表 key(中文 → 英文;本包不改 `locales/*`,合并时统一补)

新增:
- 联机状态 → Connection status
- 房间 → Room
- 快捷操作 → Quick actions
- 玩家 → Players
- 累计时长 → Total playtime
- 时间范围 → Time range
- 明细 → Details
- 已检测的 Java → Detected Java
- 账号列表 → Account list
- 主区域固定，不能拖动或隐藏 → Main area is fixed and can't be moved or hidden
- (「实例」已有;「游戏按版本挑 Java:26.1+ 要 25,…」这句现在额外经 `I18nCore.Tr` 出现在「实例默认」卡里,若词表里该句 key 不存在请按原句补一条:→ Pick Java by game version: 26.1+ needs 25, 1.20.5+ needs 21, 1.18+ needs 17, older versions use 8)

可废弃(界面上不再使用):状态卡、房间卡、操作区、玩家列表、累计卡、范围卡、明细列表、已检测列表、实例行、账号卡(注意「玩家列表」「操作区」等若别处还在用请保留,合并时 grep 一下)。
i18n 守卫测试(`I18nBaselineTests` 等)本分支全绿。

## 5. 跨包接线 / 与他包的语义对照

1. **Q5 与 `LayoutShellHooks.CanEditPage`(lin-l12 文件)的语义**:
   - 全局右键(`OnPageRightButtonUp` → `FindHosts`)按 `Editor.CanEdit` 过滤;`CanEdit = specs≥1 && !IsSingleBlockPage()`,而 `IsSingleBlockPage` = 入口表 `SingleBlock` **或** `_state.Order.Count < 2`。
   - 本包的 `BlockHost.OffersLayoutMenu` = 入口表 `SingleBlock` → false,否则 **页面内置块数** ≥ 2。
   - **唯一出入**:`Order.Count` 含文档里的自建 / 插件块。一个只有 1 个内置块的页,若文档里被(导入 / JSON 编辑)塞了一个自建组件,`CanEdit` 为 true(全局右键会给菜单),而 `OffersLayoutMenu` 为 false → 点了 `EnterEdit` 返回 false,落到 `EnterEditAt` 的「这个页面没有可拖动的区块」提示。Windows 的口径是只数内置块(`_specCount`)。建议合并时由 L12 在 `FindHosts` / `CanEditPage` 里改成 `host.OffersLayoutMenu && host.Editor.CanEdit`(`OffersLayoutMenu` 是 public,lin-l12 可直接用),这样菜单与 EnterEdit 完全一致。本包没碰 L12 的文件。
   - 「可见块」口径:任务书里写的是「至少 2 个可见块」,但 GOAL Q5 / Windows 实现都是「至少 2 个块」(含默认隐藏的)。若按可见块数,启动页内容区默认只有新闻可见 → 右键菜单会消失,与 GOAL「启动页宿主里有 5 块,不受影响」冲突。**本包按块数(含隐藏)实现**,请主 agent 确认。
2. **深色表面**:启动页内容区的编辑控件深色玻璃配色由 L12 在 `LayoutEditor` 里按 `pageId == "launch"` 判断(§6.4-2),本包不加属性;英雄区那张「主区域固定」标签由本包画,用 L0 的 `B.EditDim` / `B.EditOutlineOnDark`(没有就用兜底色)。
3. **`B.EditDim` 遮罩**:若 L12 在启动页编辑态另外铺了 `B.EditDim` 压暗层,与本包英雄区 45% 叠加后可能过暗,真机看一下再定(见待真机)。
4. **`BlockHost` 新公开成员**:`OffersLayoutMenu`、`SpecCount`、`IsEditing`、`EditingChanged`、`SyncEditing()`;静态类 `PageDefaultLayouts`、`BlockHostRules`。构造签名 / Root / Changed / EnterEdit 原样不变。
5. 入口表(L4):Linux 的单块页是 `settings:network` 与 `feedback`(Windows 是 settings:network / settings:about),本包直接读 `LayoutBlockEntryCatalog.Find(..).SingleBlock`,L4 改表后自动跟随。

## 6. 决定(合并时编号 D-82 起)

- R35-L5-1:默认网格一律 12 列(= `LayoutSchema.ColsDefault`)。Linux 这 4 页原来是 stack(无「现有 cols」可保留),12 列与实例库、与编辑态 stack→grid 折算一致。
- R35-L5-2:默认隐藏块只给 span、不给坐标(契约默认矩形 = 自动流),排在最后 —— 显示出来时接在末尾两两并排,不压块。
- R35-L5-3:Q5 按「页面内置块数 ≥ 2(含默认隐藏)+ 入口表单块标记」判定(同 Windows),不按可见块数(理由见 §5-1)。
- R35-L5-4:Java「说明」默认隐藏后,计数 / 刷新 / 扫描挪进「已检测的 Java」块、版本对应句挪进「实例默认」,与 Windows r34/l5 一致。
- R35-L5-5:编辑态英雄区「去饱和」—— Avalonia 11 没有内置的饱和度 / 颜色矩阵效果(只有 Blur / DropShadow),不加依赖就做不了真正的去饱和;本包只做 45% 不透明 + 不接点击。真机看效果后若需要再加一层中性灰罩(不在本包)。
- R35-L5-6:实例库(Q2)Linux 无自带布局菜单,不改 `LibraryPage.cs`。

## 7. 待真机(不启动程序,全部留给用户在场时跑)

截图(明 / 暗各一张,常态 + 编辑态):
1. `--shot multiplayer` / `--shot multiplayer --edit`:说明整行、联机状态 + 房间各半、快捷操作整行、玩家 + 节点说明 8:4;编辑态块名显示新名字。
2. 设置 → 游戏时长(`--shot settings:playtime` 或 Linux 对应写法)常态 + `--edit`:累计时长 + 时间范围各半、明细整行;概览不显示,「添加」抽屉的「已隐藏的内容」里有「概览」;刷新 / 清空统计在时间范围卡右侧可用。
3. 设置 → Java 常态 + `--edit`:安装方式 + 实例默认各半(实例默认顶部有版本对应句)、已检测的 Java 整行(顶上有计数 / 刷新 / 扫描系统 Java);说明不显示,可从「添加」找回;安装方式卡里 5 张下载卡在半宽下的换行是否好看。
4. `--shot launch` / `--shot launch --edit`:常态只有新闻整行;编辑态英雄区 45%、右上角「🔒 主区域固定，不能拖动或隐藏」;从「添加」依次显示任务 / 时长 / 便签 / 日志 → 两两并排、日志整行;与 L12 深色玻璃 / `B.EditDim` 叠加后的明暗是否合适;英雄区编辑态点不动启动按钮。
5. 设置 → 下载与网络、设置 → 反馈与帮助:页面空白处右键**没有**「自定义布局...」(只有「布局设置...」)。
6. 老用户回归:拿一份已存过 multiplayer / playtime / java / launch 的 `ui_layout_v2.json`,确认四页外观与改动前一致(完全照文档)。
7. 服务器页、账号页:编辑态块名为「实例」「账号列表」,布局不变。

## 8. 风险

- `EditingChanged` 的「退出」靠 `LayoutUpdated` 那一拍;退出编辑必然重排,真机应无问题,但若某条退出路径不触发重排,英雄区会停在 45% 直到下一次布局刷新(切页、改窗大小都会触发)。L12 若给 `LayoutEditor` 加编辑态事件,合并时可改成直接订阅。
- 启动页内容区默认改成 grid 后非编辑态走画布 Panel(`RenderBox`)而不是纵向流;新闻卡 `VerticalAlignment=Top`,62% 限高照旧。真机确认新闻行与英雄区的垂直间距和改动前一致(GOAL 允许本页改默认,但请看一眼)。
- Java 安装方式卡在半宽(6/12)下 5 张 158px 宽卡会折行,卡片变高;按效果图就是这样排,真机看观感。
- 构建警告从 4 条变 5 条(构建脚本只打印错误,未能看到是哪条;疑为测试文件里某个可空性提示),不影响编译与测试。
