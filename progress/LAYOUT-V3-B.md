# LAYOUT-V3-B · Windows「设置 → 布局」v3 落地（代理 B / win-settings）

日期：2026-11-20　范围：任务书 `tmp/v3_spec.md` §1.B 全部　**本机未编译、未跑测试**（硬约束 §3.1），所有调用的 API 已逐个 grep 核对签名，LangVersion = 12 / net48 / WPF。

## 改了哪些文件
| 文件 | 改动 |
|---|---|
| `windos/src/TipCL/Pages/SettingsLayoutPage.cs` | `SettingsPage` 的「布局」视图整段重写（v3 分组：外观 / 导航 / 页面 / 方案 / 高级）；下半部纯逻辑类 `LayoutBlockEntryCatalog` / `LayoutJsonIssue` / `LayoutJsonEditor` 原样保留，`LayoutProfiles` 只**追加** 4 个方法 |
| `windos/src/TipCL/Pages/Layout/ShellTabs.cs` | 追加 chip 纯逻辑：`CanHide` / `ChipOrder` / `IsDefaultTabs` / `ResetTabs` / `SetChipOrder`（不含中文字面量，守 `LayoutShellTests.TabLabelsLiveInShellTabsOnly`） |
| `windos/src/TipCL/Pages/Layout/PageLayoutHost.cs` | 追加 `SplitCatalog.StoredSites()` / `SplitCatalog.DisplayName()`；分隔条 ToolTip 改「拖动调整宽度 · 双击复位」 |
| `windos/src/TipCL/Pages/Layout/LayoutJsonWindow.cs` | **新建**：独立的布局 JSON 编辑窗口 |
| `windos/src/TipCL/Pages/Layout/LayoutSettingsPrefs.cs` | **新建**：「网页和按钮组件」全局开关（存 `UiPrefs.Expanded["layout.v3.danger_widgets"]`） |

csproj 是 SDK 风格、默认通配编译，两个新文件**不需要**登记，csproj 未改。没有改 locales / tests / docs，也没有动 B 名下以外的文件。

## §1.B 逐条落地（行号 = `SettingsLayoutPage.cs`，除非另注）
- **页头**：「布局」+「调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。」，删掉 `ui_layout_v2.json…` 那句 —— L142。
- **1 外观**：「区块间距 · 所有可以自定义的页面一起生效」+ 三档分段（`Seg.Rail` + `Seg.Item`，与下载页同款）替换下拉 —— `LayoutAppearanceRows` L277。存储仍是 `shell.density`（compact/standard/loose）。改完重建视图，缩略图块间隔随档位变（`ThumbGap` 2/3/5px）；各页面 BlockHost 经 `LayoutChanged` 自己重排（编辑态同步变）。
- **2 导航 · 顶部页签**：「拖动调整顺序，点眼睛隐藏；「设置」不能隐藏」+「恢复默认」（已是默认时禁用）—— `LayoutShellRows` L351；chip 行 `BuildTabChips` L430 / `TabChip` L457：拖动手柄 + 名字 + 眼睛；隐藏的排在后面变淡；「设置」显示锁、禁用（老存档里若已被藏，仍允许点开找回）。拖动：chip 行抓鼠标、按指针位置在行内换位（`OnChipDragMove` L543），松手 `ShellTabs.SetChipOrder` 一次落盘（`EndChipDrag` L584）；眼睛 → `ToggleTab` L624。存储语义不变（`shell.tabs` = 显示的页签 + 顺序）。标题栏由 MainWindow 订阅 `LayoutChanged` → `RebuildShellTabs`，**马上变**。
- **2 导航 · 标题栏**：只留「AI 按钮 / 主题切换 / 账号」三个勾选 + 说明「隐藏 AI 按钮后仍可按 Ctrl+K 打开」—— L385；「桥接」与「标题栏:xxx」「只隐藏/显示,不改形态」都去掉（`TitlebarLayout.Bridge` 字段与 `SetTitlebar(bridge:)` 保留，存档读写不变）。
- **3 页面**：缩略图卡片网格（3 列 `UniformGrid`）—— `LayoutPagesSection` L697 / `PageCard` L753：缩略图 + 页名 +「默认 / 已自定义」标签 + 说明（「和出厂时一样」或「网格 · N 个组件 · 隐藏了 N 项 / 调整了顺序」）+「编辑」（→ `LayoutShellHooks.BeginEditPage`，L784）+ 已自定义时「恢复默认」。右上「全部恢复默认」（确认后 `LayoutProfiles.ResetAllPages`，先按「应用方案」同一规则留底「上一次布局」，L808）。底部「「下载与网络」「反馈与关于」只有一块内容，不提供编辑。」（页名取自入口表单块页，L740）+「显示全部 N 个页面 / 收起」，默认只显示 启动 / 下载 / 账号 / 通用 / 游戏时长 / 联机。缩略图数据：已构建的页面宿主用它当前 `LayoutPageState`，否则用布局文件里存的这一页，再不行画通用示意（`ThumbPlanOf` L842 / `BuildThumb` L964）。卡片列表 = 入口表里非单块、无 Hint、非本视图、且 `LayoutShellHooks.CanEditPage` 为真的页（`LayoutCardEntries` L660）。
- **4 方案**：分组头「导入… / 导出整套布局… / 存为新方案」—— `LayoutProfilesSection` L1112；卡片 `ProfileCard` L1182：标签「正在使用 / AI 备份 / 自动留底 / 方案」，正在使用的描边（`B.Accent` 1.5px）且按钮为禁用的「当前方案」，其它为「应用」；说明「N 个页面自定义 · 标准间距」，AI 备份为「AI 改布局前自动保存，保留最近 5 份」；「⋯」菜单「重命名 / 复制一份 / 删除」（`ShowProfileMenu` L1227）。排序：普通方案 → 上一次布局 → AI 备份（新的在前）。增删改查 / 原子替换 / 留底全部仍走原 `LayoutProfiles`。
- **5 高级（默认折叠）**：`LayoutAdvancedParts` L1473。标题「高级」+ 头部「分栏、网页与按钮组件、布局文件 · 一般不需要动」，点击展开 / 收起（状态跨重建保留）。
  - 分栏宽度：一行「已调整 N 处：设置左侧导航、实例库文件夹栏。在页面上拖两栏中间的分隔线即可调整，双击复位。」/ N=0「都是默认宽度…」并禁用「全部复位」—— `LayoutSplitRow` L1519（名字来自 `PageLayoutHost.cs` `SplitCatalog.DisplayName` L309，列表来自 `StoredSites` L293）。
  - 网页和按钮组件：开关 +「开启后可以添加嵌入网页（只接受 https）和执行动作的按钮」，开启时显示警示「导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开。」—— `LayoutWidgetRows` L1606（开关存储：`LayoutSettingsPrefs.cs`）。
  - 布局文件：「用 JSON 编辑整套布局，适合批量修改或分享给别人」+「打开所在文件夹」（explorer /select 定位 `ui_layout_v2.json`，L1576）+「在编辑器中打开…」→ `LayoutJsonWindow.ShowFor`（L1563）。
- **6 内部说法**：文件名、「桥接」、`#编号 pageId / splitId`、「(L6)」「危险类型」「契约钳位」全部从界面文案移除（源码注释里仍有说明性文字）。
- **7 两组导出改名**：这里是「导出整套布局…」（对话框选项「只导出页面顺序 / 导出整套布局」，L1352）；编辑态「导出本页」归 A。
- **8 两张联动**：改间距 / 改页签 → `LayoutStoreV2.Save` → `LayoutChanged` → 标题栏与各 BlockHost 当场更新；「编辑」→ `LayoutShellHooks.BeginEditPage(pageId)` 直进那一页的编辑态。

## 跨代理（详见 `tmp/v3_handoff.md`「B → A」小节）
- B **按约定签名调用、依赖 A 提供**：`LayoutShellHooks.CanEditPage(string)`、`LayoutShellHooks.BeginEditPage(string)`。写报告时仓库里还没有这两个方法（A 正在写 `LayoutEditorV3.cs`），**A 不补则编译不过**。B 新增了现成实现 `SettingsPage.OpenPageForLayoutEdit(pageId)` 供 A 的 `BeginEditPage` 直接复用。
- 「打开设置→布局」现有方法：`SettingsPage.OpenLayoutView()`（不变）；新增 `SettingsPage.OpenLayoutAdvanced()`（展开高级并滚过去，给「添加」抽屉底部提示用）。
- 新增全局开关 `LayoutSettingsPrefs.DangerWidgetsEnabled` / `Changed`，需要 A 的「添加」抽屉读取它来决定是否列出网页 / 按钮。

## 降级 / 偏离效果图及原因
1. **「高级」是三张相连的卡片而不是一张**：测试 `LayoutPageSplitTests` 钉着 7 个区块 id（含 splits / widgets / json），不能改测试，所以高级拆成三块共用一个折叠开关；折叠时后两块 Collapsed，宿主仍会留一点块间距空白（≤20px）。
2. **「自建组件」管理（改名 / 改参数 / 删除定义 / 新建）收进「高级」里的一行「管理…」**，效果图没有这一节；为了不丢现有功能保留，文案去掉了 L6 / 危险类型等说法、「自建区块」统一叫「组件」。
3. **标题栏第三项叫「账号」而非效果图的「账号名称」**：现有开关隐藏的是整个账号按钮，不是只藏名字，按真实语义命名。
4. **页面卡片数量是动态的**（按入口表 + `CanEditPage` 算，当前约 12 个，不是写死的 10）；需要先手动打开子视图的「版本安装」「实例详情 · 设置」不出卡片（以前也只是给一句提示），右键「自定义布局…」入口照旧。若 A 判定启动页这类「宿主里只有一块」的页 `CanEditPage=false`，它也不会出卡片。
5. **正在使用的方案不允许从菜单删除**（照效果图提示「先切到别的方案」）；纯逻辑 `LayoutProfiles.Delete` 本身不变，测试语义不受影响。
6. **页签「恢复默认」只复位页签**，不碰标题栏开关；老存档若把「桥接」藏了，界面上已无入口，只能在布局文件里改（字段保留）。
7. 缩略图块高是示意（不按真实高度），未构建过的页且没存过布局时画通用三条。
8. 方案卡片没有「保存时间」：方案存档里没有这个字段，不新造字段。

## 风险
- 编译依赖 A 的两个方法（见上）。
- 新增 117 条字符串（`tmp/v3_strings/B.txt`）由 E 入词表；若有「src 每个 I18n.L key 必须在词表」类守卫测试，入表前会红。
- 原 JSON 编辑框用的 `Input.Log` 样式自带 `IsReadOnly=True`——旧版设置页里的 JSON 框其实打不了字；新窗口已显式 `IsReadOnly=false`。
- chip 拖动在 WrapPanel 换行（窗口很窄时）下用「指针在下一行之下 / 同行越过中线」判位，极窄窗口可能有跳动感。
- `RefreshLayoutView` 重建视图后按 Loaded 恢复滚动位置；若 `SmoothScroll` 有自己的动画目标，可能出现轻微回弹。
- 缩略图读取 `BlockHost.Editor.State`（`LayoutPageState.Order/Hidden/Spans/Page`，A 名下类型，只读使用）；A 若改名需同步。
- 「网页和按钮组件」开关借用 `UiPrefs.Expanded` 字典的一个专用 key（没改 UiPrefs 结构）。

## Windows 真机回归步骤
1. 设置 → 布局：页头文案正确，无 `ui_layout_v2.json`、「桥接」、`#数字`、「(L6)」字样。
2. 区块间距点「紧凑 / 宽松 / 标准」：提示「间距：xx」，缩略图块间隔变化；切到下载页 / 右键进编辑态，块间距同步变化；重启后保持。
3. 顶部页签：拖「插件」到「启动」前面，松手后标题栏页签顺序立刻变；点「下载」的眼睛 → 标题栏立即少了「下载」、chip 变淡排到后面；再点恢复；「设置」的眼睛是锁、不可点；「恢复默认」后五项默认顺序、按钮变灰。重启后保持。
4. 标题栏三项勾选：取消「AI 按钮」→ 标题栏 AI 消失，按 Ctrl+K 仍能打开 AI 页；主题、账号同理。
5. 页面卡片：默认 6 张，点「显示全部 N 个页面」展开 / 「收起」；「下载与网络」「反馈与关于」不出卡片且底部有说明。点「下载」卡片「编辑」→ 跳到下载页并直接进入编辑态（依赖 A）。在某页改过布局后回来：该卡片显示「已自定义」+ 说明 +「恢复默认」，点后变回「默认」、页面复原。「全部恢复默认」→ 确认框 → 所有页复原，方案里出现「上一次布局」可切回。
6. 方案：「存为新方案」输入名字 → 新卡片带描边「正在使用」；另一卡片「应用」→ 提示已切换、页面重排、描边移过去；「⋯」重命名 / 复制一份 / 删除（对正在使用的删除给提示不删）；让 AI 改一次布局后出现「AI 备份」标签卡片。「导入…」选整套布局文件 → 「存为新方案 / 应用为当前布局」；「导出整套布局…」两种导出都能写文件且能再导入。
7. 高级：默认折叠；展开后
   - 分栏：在实例库拖左栏 → 回来显示「已调整 1 处：实例库文件夹栏…」，「全部复位」后变「都是默认宽度」且按钮禁用；页面上双击分隔线复位，分隔条悬停提示「拖动调整宽度 · 双击复位」。
   - 网页和按钮组件：打开显示黄色警示，重启后保持；进编辑态「添加」应多出网页 / 按钮（依赖 A 读取开关）。
   - 自建组件「管理…」：新建 / 改名 / 改内容 / 删除仍可用。
   - 「打开所在文件夹」在资源管理器里选中布局文件；「在编辑器中打开…」弹出单独窗口：能打字、「校验」显示结果、故意改错 kind 后「应用」不保存且标红、改对后「应用」/ Ctrl+S 生效并刷新设置页；有未应用修改时关闭会询问；窗口开着时在别处改布局会提示；重复点「在编辑器中打开…」只会把已开窗口提到前面。
8. 设置 → 布局页空白处右键仍有「自定义布局…」，编辑态 7 个区块可重排（id 未变）。
9. 跑 `windos/tests/TipCL.Tests`（尤其 `LayoutShellTests`、`LayoutPageSplitTests`、`LayoutProfileTests`、`LayoutSplitTests`）。
