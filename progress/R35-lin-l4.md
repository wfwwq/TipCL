# R35 lin-l4 — 布局 v3 WP-L4「设置 → 布局」落地到 Linux 树（Avalonia 11 / net8）

日期：2026-11-20 · 分支 `r35/lin-l4`（从 `r35/lin-base` 7039719 出发）· 工作区 `D:\项目\TipCL\_r35\lin-l4`
规格：`windos/GOAL-layout-v3.md` §3 Q5/Q6、§5.4、§6.3 WP-L4、§6.4 第 3、8 条；效果图 `windos/docs/mockups-hark/v3/layout3.html` 第二张。
基线已含 quick-v3 代理 D 的实现（`progress/LAYOUT-V3-D.md`），这次是对照 GOAL 逐条补齐和修正，不是推倒重写。

## 1. 测试数字

| | 通过 | 失败 | 跳过 | 合计 |
|---|---|---|---|---|
| 基线（7039719）| 978 | 0 | 7 | 985 |
| 收尾（本分支）| 991 | 1 | 7 | 999 |

- 基线数字的来历：我自己 22:28 起跑的基线全量在 22:30 和 lin-l12 / lin-l3 / lin-l5 的首轮全量同时中断，进程没了，日志停在中途。所以基线用同一基线提交的兄弟包首轮全量（lin-l0 222902、lin-l3 223925、lin-l5 224851 三份结果一样：978 / 0 / 7）。
- 新增 14 条测试：`Layout/SettingsLayoutV3Tests.cs` 13 条，`LayoutMoreTests` 1 条。没有删除任何断言。
- **唯一的失败**是 `LayoutV3I18nTests.EveryLayoutTrLiteral_HasKeysInBothTables`。原因是新增文案还没进词表，按任务书 §3-6 等合并时统一补（清单见 §6）。这个测试在本包清单之外，我没改它。
- 第一次收尾全量（230039）的镜像里还是旧版 `ClickSweepTests.cs`（主 agent 的修改在我启动那次运行之后才落到 D:）。那一次可能又调起过一次浏览器，特此说明。之后的运行都带上了修改，见 §8。

## 2. 逐条对照 GOAL §5.4（行号按提交时的 `SettingsPage.Layout.cs`，简写 SPL）

| GOAL | 落地 | 位置 |
|---|---|---|
| 页头说明「调整导航、各页面的排列…随时可以恢复默认。」 | D 已做，核对无误 | SPL `BuildLayoutView` header |
| 1 外观：区块间距三段（6/10/14），说明「所有可以自定义的页面一起生效」 | D 已做，核对无误 | SPL `LayoutAppearanceRows` / `DensityGap` |
| 2 导航·页签：chip 拖动排序、眼睛隐藏、「设置」锁、恢复默认、标题栏马上变 | D 做了拖动、眼睛和恢复默认。**补**：「设置」原来只是把眼睛置灰，现在换成锁形图标（优先用 L0 的 `Geo.Lock`，找不到用内置路径）；眼睛图标也优先用 `Geo.Eye` / `Geo.EyeOff`。资源找不到不抛异常 | SPL `TabChip`、`EyeIcon` / `LockIcon` / `StrokeIcon` |
| 2 导航·标题栏：三项勾选，去掉「桥接」；只写一句 Ctrl+K 说明；文字写「账号」 | **改**：「账号名称」改成「账号」（这个开关控制整个账号芯片，以代码为准）。没有「桥接」，`SetTitlebar(bridge:)` 和已存的值保留 | SPL `LayoutNavSection` |
| 3 页面：卡片每行 3 张，数据来自入口表，去掉 SingleBlock 和 `settings:layout`，含实例库 | **改**：原来 D 按 `CanEditPage` 运行时判断，现在按入口表纯函数 `LayoutPageCardEntries()` 判断（GOAL Q5：「直接用 SingleBlock 判断」）。`library:main` 本来就在 Linux 入口表里 | SPL `LayoutPageCardEntries` / `LayoutSingleBlockEntries` / `IsPageCardEntry` |
| 3 张数 | **Linux 实际 21 张**（入口表 24 条，减 2 条 SingleBlock：`settings:network`、`feedback`，再减 `settings:layout`）。比 Windows 的 15 张多出来的是插件市场、6 个资源目录、版本安装、版本管理、下载任务、AI 助手等，都是 Linux 实际存在的区块宿主。另外 Linux 没有 `settings:about`，「反馈与帮助」是单块页 | 单测 `PageCards_ExcludeSingleBlockAndLayoutSelf_AndIncludeLibrary` |
| 3 默认 6 张：启动、下载、实例库、联机、账号、AI 助手 +「显示全部 N 个页面」 | **改**：D 用的常用页是「启动 / 下载 / 账号 / 通用 / 游戏时长 / 联机」，现在按 GOAL 改成 `LayoutDefaultCardPages`。补了实例库（4+8）和 AI 的示意缩略图 | SPL `LayoutDefaultCardPages`、`LayoutDefaultThumbs` |
| 3「已自定义」= `LayoutStoreV2` 里有该页条目 | 抽成 `IsPageCustomized(doc, pageId)`，卡片和「全部恢复默认」都用它 | SPL `IsPageCustomized`；单测 `Customized_MatchesStoreEntries` |
| 3 编辑按钮走 `LayoutShellHooks.BeginEditPage` / `CanEditPage` | 保持不变；`CanEditPage` 现在决定「编辑」能不能点（卡片集合不再由它决定） | SPL `PageCard`、`CanEditLayoutPage`、`BeginLayoutPageEdit` |
| 3 单块页在下方用一行文字说明 | 保持（名单改为从入口表取） | SPL `LayoutPagesSection` foot |
| 3 子视图找不到页面时的 Hint | 保持：`install` 的 `NeedsContext` 由 `LayoutShellHooks.BeginEditPage` 弹出（那个文件不在本包清单内，没改）| 单测 `InstallSubView_KeepsItsHint` |
| 4 方案：每行 4 张，标签（正在使用 / 方案 / AI 备份），正在使用的方案 1.5px 主题色描边 | D 已做 | SPL `ProfileCard` / `SchemeCardShell` |
| 4 一行信息（保存时间、几个页面自定义过） | **改**成 `LayoutProfiles.CardInfo`：「N 个页面自定义过 · X间距」；「上一次布局」写「切换方案或恢复默认之前自动留的底」；AI 备份写「AI 改布局之前自动存的一份」。**降级**：Linux 的 `LayoutBody` 没有保存时间字段（`saved_at` 只记在整份文档上），所以普通方案不写时间。不用往 meta 里补写时间戳的办法，因为那样会让 `KeepPrevious` 的 `SameBody` 比较永远不相等，每次应用都会多留一份底。AI 备份的时间写在显示名里 | `LayoutProfiles.CardInfo` |
| 4 AI 备份按前缀 `AI 备份 ` 识别，显示成「AI 改布局前」+ 时间 | **补**：`AiBackupPrefix`、`DisplayName`（能解析 `yyyy-MM-dd HH:mm[:ss]` 的就写成 `MM-dd HH:mm`）。`IsAiBackup` 沿用 D 的超集识别 | `LayoutProfiles.AiBackupPrefix` / `DisplayName` |
| 4 「⋯」菜单：重命名 / 复制一份 / 删除，**正在使用的方案不能删除** | **补**：菜单里「删除」对正在使用的方案置灰并给提示；`DeleteProfileAsync` 里再用 `LayoutProfiles.CanDelete` 守一道。`LayoutProfiles.Delete` 本身的语义没改（现有测试会删除激活方案） | SPL `OpenProfileMenu` / `DeleteProfileAsync`；`LayoutProfiles.CanDelete` |
| 4 卡片顺序 | **补**：普通方案 →「上一次布局」→ AI 备份（新的在前），与 Windows 一致 | `LayoutProfiles.OrderForCards` |
| 4 导入… / 导出整套布局…（先问 v2 还是 v1 兼容）/ 存为新方案 | D 已做，核对无误 | SPL `ExportLayoutAsync` / `ImportLayoutFileAsync` |
| 5 高级（默认折叠），标题和副标题 | D 已做；无头单测确认默认折叠 | SPL `LayoutAdvancedSection` |
| 5 分栏：一句话 + 全部复位 | D 已做 | SPL `LayoutSplitRow` |
| 5 网页和按钮组件开关（沿用 `LayoutProfiles.DangerousWidgetsEnabled`，不改名）+ 琥珀色提示 | **改**：字色用 `B.WarnInk`（不存在时退回本树已有的 `B.Warn`），底色用 `B.WarnSoft`（不存在时用固定 #FDF3E2）；查资源用 `TryGetResource`，不会抛异常 | SPL `LayoutDangerRow` / `LayoutBrushKey` / `SetWarnSoft` |
| 5 **组件管理「已创建 N 个组件 · 管理…」** | **恢复**（D 删掉了）：一行「组件管理 / 已创建 N 个组件 / 管理…」，点开是内联面板，每个组件一行：标题 + 类型 · 用在几个页面 + 重命名 / 删除（删除前确认，定义和所有引用一起移除）。界面上不显示 id。新组件仍在编辑态「添加」抽屉里建（GOAL §5.2），面板里有说明 | SPL `LayoutWidgetManageRow` / `WidgetAdminRow` / `LayoutWidgetUsage` / `RenameWidgetAsync` / `DeleteWidgetAsync` |
| 5 布局文件：打开所在文件夹 / 在编辑器中打开…（独立窗口） | D 已做 | SPL `LayoutFileRow`、`LayoutJsonWindow.cs` |
| §6.3 界面上没有 ui_layout_v2 / 桥接 / L4 / L6 / # 编号 | 新单测扫描 SPL 里所有 `I18nCore.Tr("…")` 字面量 | 单测 `LayoutPageStrings_HaveNoInternalWords` |
| `OpenLayoutView()` / `OpenLayoutAdvanced()` 不改名、不删除 | 保持 | SPL |

## 3. 文件清单（都在 L4 独占范围内）
- `src/TipCL.App/Pages/Settings/SettingsPage.Layout.cs`（改）
- `src/TipCL.App/Pages/Layout/LayoutProfiles.cs`（只追加：`AiBackupPrefix` / `DisplayName` / `CardInfo` / `CanDelete` / `OrderForCards`）
- `tests/TipCL.Tests/Layout/SettingsLayoutV3Tests.cs`（新建）
- `tests/TipCL.Tests/App/LayoutMoreTests.cs`（`SingleBlockIds_MatchEntries` 加了 feedback 断言，新增 `Catalog_Has24Entries_IncludingLibraryMain`）
- `tests/TipCL.Tests/App/ClickSweepTests.cs`：**主 agent 改的**（SideEffectWords 加了 主页 / GitHub / issue / 网页 / 浏览器 / 官网 / 链接 / 文档，防止点开浏览器），按要求原样随本包提交
- 不需要改：`LayoutBlockEntryCatalog.cs`（`library:main` 本来就在）、`ShellTabs.cs`、`LayoutEditPrefs.cs`、`LayoutJsonEditor.cs`、`LayoutJsonWindow.cs`、`PageLayoutHost.cs`、`UiPrefs.cs`
- 没有改 `LayoutPagesSplitTests.cs`：它对 `settings:layout` 区块 id 的断言（header / appearance / shell / blocks / profiles / advanced）本来就和现状一致，跑通过了

## 4. 新测试 `SettingsLayoutV3Tests`（13 条，全部通过）
卡片集合与张数；默认 6 张的顺序；单块页单独列出；install 的 Hint；已自定义判定 = 存档里有该页；AI 备份前缀和显示名；CardInfo；正在使用的方案不能删；方案排序；方案 应用 / 重命名 / 复制 / 删除 整链；界面字符串扫描；无头测试 1（默认 6 张卡片 +「显示全部 21 个页面」、高级默认折叠、组件管理在里面、标题栏只有三项勾选且没有「桥接」）；无头测试 2（点眼睛立刻落盘、「设置」的眼睛不能点）。
**无头测试只点页签眼睛按钮，不点任何会打开浏览器、文件管理器或外部程序的按钮**（「打开所在文件夹」「在编辑器中打开…」「去 GitHub 提 issue」都不点）。

## 5. 决定（合并时请编 D-82 起）
1. Linux 页面卡片 21 张，不硬凑 Windows 的 15 张：卡片按入口表纯函数决定，单测钉的是公式「入口表 − SingleBlock − settings:layout」，同时钉了 21。
2. 卡片集合按入口表的 SingleBlock 判断，不再按 `CanEditPage` 运行时判断（GOAL Q5）；`CanEditPage` 只决定「编辑」能不能点。
3. 方案没有保存时间就不显示时间，不往 meta 里写时间戳（理由见 §2 第 4 组）。
4. 「正在使用不能删」放在界面层（`CanDelete`），`LayoutProfiles.Delete` 不变，保持现有 API 和测试语义。
5. 组件管理面板只做改名和删除，不放「新建」：新建统一走编辑态抽屉，避免出现建了却不在任何页面上的组件。

## 6. 新增词表 key（中文 → 英文，共 18 条；都缺 zh_CN 和 en 两边；`LayoutV3I18nTests` 因此变红。测试报「19 条」是因为「正在使用的方案不能删除…」在源码里出现了两次，去重后是 18 条）
| 中文 | English |
|---|---|
| AI 改布局前 {0} | Before AI change {0} |
| AI 改布局之前自动存的一份 | Saved automatically before AI changed the layout |
| 切换方案或恢复默认之前自动留的底 | Kept automatically before switching profile or resetting |
| {0} 个页面自定义过 | {0} pages customized |
| 各页面都是默认排列 | All pages use the default arrangement |
| 正在使用的方案不能删除，先切到别的方案 | The profile in use can't be deleted. Switch to another one first |
| 管理… | Manage… |
| 管理已创建的组件 | Manage created widgets |
| 组件管理 | Widgets |
| 已创建 {0} 个组件 | {0} widgets created |
| 还没有组件。在页面的编辑态里点「添加」就能放便签、时钟等组件。 | No widgets yet. In a page's edit mode, click "Add" to place notes, clocks and more. |
| 删除组件会把它从所有页面上移走；要新加组件，在页面的编辑态里点「添加」。 | Deleting a widget removes it from every page. To add a new one, click "Add" in a page's edit mode. |
| {0} · 用在 {1} 个页面 | {0} · used on {1} pages |
| {0} · 还没放到页面上 | {0} · not placed on any page yet |
| 重命名组件:{0} | Rename widget: {0} |
| 删除组件:{0} | Delete widget: {0} |
| 组件名 | Widget name |
| 删除组件「{0}」？它会从所有页面上移走。 | Delete widget "{0}"? It will be removed from every page. |

废弃 key：「账号名称」（设置页不再使用；其他地方是否还在用请合并时 grep 确认）。

## 7. 跨包接线需求
- L0：`B.WarnInk`、`Geo.Eye` / `Geo.EyeOff` / `Geo.Lock` 有了就自动生效（`TryGetResource`，缺了用兜底）。
- L12：抽屉的危险组件过滤继续读 `LayoutProfiles.DangerousWidgetsEnabled`（没改名）。

## 8. 待真机 / 风险
- 待真机：设置 → 布局 明暗截图；高级展开截图；锁形图标和眼睛图标的视觉；琥珀提示的颜色；组件管理面板的排版；方案卡片的 AI 备份显示名；点眼睛和拖 chip 之后标题栏马上变（`RefreshShell` → `ApplyShellLayout`，无头测试只验证了落盘）。
- 风险：组件管理用 `Dlg.Prompt` / `Dlg.Confirm`，无头测试没覆盖；`Geometry.Clone()` 只在 L0 资源存在时才会走到。
- 带上 ClickSweep 修改之后的全量复跑结果写在下面的「附」里。

## 附：带 ClickSweep 修改、完整编译后的全量复跑
- 231534（先完整编译再测试，没有带 -NoBuild）：**通过 991 / 失败 1 / 跳过 7 / 合计 999**，耗时 47 s。唯一失败仍是 `LayoutV3I18nTests`（缺词表，见 §6）。
- 中间那次 230638 跑到一半卡住，我杀掉了它，测试进程是 lin-l4 自己的。那次里 `InstanceDetailCrashTests.LibraryDetailChip_OpensDetail_FirstLayout_DoesNotCrash` 红过一次：报错 `Res.Bind` 的 `ArgumentNullException`，调用栈来自前一个用例残留的 `AiPage.RunAgentAsync` 派发任务，与本包改动无关。之后复跑通过，属于偶发，请合并时留意。
