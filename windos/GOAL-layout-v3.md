# GOAL — R34 布局 v3：自定义界面重做（Windows 桌面版）

> 交接口令：**「TipCL 布局 v3 落地，按 GOAL-layout-v3 继续」**
> 收到口令后先读完本文件，再读 `windos/交接必读.md` §0（工作规则）和 `docs/布局使用指南.md`（现有行为），最后从 §6 进度表里第一个未完成的包开始。
> 路径都相对于仓库根 `D:\项目\TipCL\TipCL\`，写成 `windos/...` 的指 Windows 树。

---

## 1. 使命

布局 v2 的功能已经齐了：引擎、编辑器、整机方案、自建区块和 AI 排版都能用。问题出在**界面**，现在的样子像开发者调试面板：

- **编辑态**：每个块套着主题色实线框，操作条上显示 `w_note1` 这类内部 id，底部 7 个文字按钮挤成一排，「已隐藏的区块」托盘单独占一条，单块页也能拖 8 个手柄。
- **设置 → 布局**：页面上出现 `ui_layout_v2.json`、「标题栏:桥接」、`#10 plugins / cols`、「(L6)」这类内部说法；12 个一样的「自定义布局...」按钮；13 行分栏表里有 7 行写着「没有左右分栏」；JSON 编辑框常驻在页面上。
- **区块名**：「状态卡」「房间卡」「操作区」「累计卡」「范围卡」「已检测列表」这类名字直接显示给用户。

本轮目标：**不改布局引擎的数据模型、不丢用户已有布局**，把编辑态、设置 → 布局和各页区块的外观与默认值，按 v3 效果图重做。

## 2. 视觉基准（效果图）

| 文件 | 内容 | 用法 |
|---|---|---|
| `windos/docs/mockups-hark/v3/layout3.html` | ① 「下载」页编辑态 ② 设置 → 布局 | 编辑态和设置页**以这张为准**；右上角能切到「现状」对照 |
| `windos/docs/mockups-hark/v3/pages3.html` | 启动 / 实例库 / 联机 / 游戏时长 / Java 的常态和编辑态 | 各页默认布局和改名**以这张为准**；**实例库那一页除外，见 Q2** |
| `windos/docs/mockups-hark/v3/now-*.png` | 现状截图（来自 `docs/images/layout/`） | 对照用 |

- 效果图是可以操作的 HTML，用浏览器打开就能拖动、切换。数值以 HTML 里的 CSS 为准。
- 效果图里网格画的是 6 列，那只是示意。**各页现有的 `cols` 不变**，例如实例库是 12 列（`windos/src/TipCL/Pages/LibraryPage.cs:249-275`）。
- 效果图里的间距三档画的是 6/12/18，那也只是示意。**沿用现有的紧凑 6 / 标准 10 / 宽松 14**（`SettingsLayoutPage.cs:125`）。
- 不在效果图范围内的：`launch3.html`（基于过时截图画的，已作废）、插件页、下载子页、账号页、AI 页。这些页只跟着全局规则变，不做专门设计。

## 3. 已拍板的事（2026-11-20 用户「按建议拍板」，结论见 DECISIONS D-81）

| # | 问题 | 结论（= 原建议） | 不这么做的后果 |
|---|---|---|---|
| Q1 | **自由画布（canvas）模式要不要留** | 从界面上去掉，只留「列表 / 网格」两种。已存的 canvas 页**按网格渲染**（由 x/w 推出列宽和顺序），JSON 原样保留，用户在编辑器里提交一次之后才改写成 grid。AI 的 `layout_apply` 收到 `mode=canvas` 时按 grid 处理并提示 | WP-L3 做不了 |
| Q2 | **实例库**：效果图画成「只能拖分隔线」，但现在的代码里它是 3 个区块的宿主（文件夹列表 / 版本选择 / 实例状态，12 列网格 4+8，实例状态默认隐藏，`LibraryPage.cs:207-229`） | **保留区块编辑**，只套用新的编辑态外观。在网格里拖右边缘改宽度，效果就等于拖分隔线。效果图的实例库页作废，按其它页同样的样子来 | 按效果图做等于删掉现有功能 |
| Q3 | **网格里的块能不能改高度**：效果图只画了右边缘。现在网格有 e/s/se 三个手柄，下边缘双击清除固定高度（`LayoutEditor.cs:3235-3247`） | 保留右边缘和下边缘，去掉右下角那个。两条边都只在悬停或选中时出现 | 影响 WP-L1 的手柄数量 |
| Q4 | **改默认布局**：以前几轮的原则是「默认外观逐像素等于现状」，本轮要改联机 / 游戏时长 / Java / 启动下方内容区的默认值（默认网格、默认隐藏「说明 / 概览」） | 只改这 4 页。已经存过该页布局的用户不受影响，因为文档里有该页就以文档为准 | WP-L5 只能做改名 |
| Q5 | **只有一块内容的页面能不能编辑**：现在只要有 1 个块就有右键菜单（`BlockHost.cs:98-114`） | 改回至少 2 个块才有。设置 → 布局的页面卡片里也不列这些页，只在下方用一行文字说明。入口表里已经有现成标记 `Entry.SingleBlock`（`SettingsLayoutPage.cs:1107`，目前标了 `settings:network`、`settings:about` 两页），直接用它判断。当初改成 ≥1 是因为启动页宿主里只有一块，现在启动页宿主里有 5 块，不受影响 | 「下载与网络」这类页继续能拖整页 |
| Q6 | **网页 / 按钮组件的开关**：效果图里是一个全局开关，现在是每个组件各有一个 `settings.enabled` | 新增全局开关（存在 UiPrefs 里，默认关），它只决定这两种组件会不会出现在「添加」抽屉和组件表单里。每个组件自己的 `enabled` 保持原样，导入时强制关掉的规则也不变 | WP-L4 的「高级」区不知道放什么 |
| Q7 | **Linux 树要不要同步** | 本轮只做 Windows。Linux 树（仓库根 `src/TipCL.App`）放到下一轮 WP-L7，等 Windows 版真机验收之后再移植 | — |

## 4. 红线（任何包都不得违反）

1. **布局 JSON 向后兼容**：读进来的字段一个都不能丢，未知字段原样写回；v1 导出继续可用；`ui_layout_v2.json` 的 `kind / version` 不变。旧文档读进来之后渲染必须正常，不报错、块不丢、顺序不乱。
2. **不改数据模型**：`LayoutDocV2` / `LayoutSchema` 只能新增可选字段，不能删字段、不能改字段的含义（Q1 的 canvas 渲染属于「读取时推导」，不是改模型）。
3. **框架和依赖不变**：保持 net48 和单 exe（Costura），不加 NuGet 包，不加字体或图片资源。图标只复用现有字形（`Services/Ico.cs`）：垃圾桶 `Ico.Trash`、更多 `Ico.More`、添加 `Ico.Add`、拖拽 `Ico.DragHandle`、列表 `Ico.List`、网格 `Ico.Grid`、关闭 `Ico.Close`。**`Ico` 里没有眼睛、眼睛划线、锁、撤销、重做**，这几个用矢量 Path 画（参照窗口按钮的画法：10–14px、线宽 1.1–1.6、圆头），放进 WP-L0 的样式里统一提供。
4. **色板成对改**：`Palette.Light.xaml` 和 `Palette.Dark.xaml` 一边加了键，另一边也必须加。
5. **界面文案**：一律走 `I18n.L()`。新 key 在 `src/TipCL/Resources/locales/zh_CN.json` 和 `en.json` 里**成对**补上（合入方式见 §6.2），改完重新打包 `.json.gz`（`tools/pack-core-resources.ps1`）。**界面上不许出现**文件名、包编号（L4/L6/WP-…）、`#编号`、块 id、「桥接」这类内部说法。
6. **体积**：compact 版不超过 **5,757,264 B**（D-19）。当前 dist 是 5,604,352 B，大约还剩 150 KB。每个包收尾时记录实测体积。
7. **范围**：只动 `windos/`，不碰 Linux 树（仓库根 `src/`、`tests/`），不碰 `Core/` 里的业务逻辑（`Core/Launcher/Ai/LayoutTools.cs` 只按 Q1 改参数说明和校验），不碰 `.github/`。
8. **不改版本号、不打 tag、不发 release、不 push。**
9. **工作方式**：沿用 `windos/交接必读.md` §0。主 agent 只派活、验收、记录；子代理只写自己包里独占的文件；PROGRESS / DECISIONS 只能**追加**自己的小节。

## 5. 改动规格

### 5.1 区块编辑态外观（对应 layout3 第一张）

| 项 | 现在 | 改成 |
|---|---|---|
| 块边框 | 主题色实线（`LayoutCard` 构造里的 `B.Accent`，`LayoutEditor.cs:3179-3180`） | 常态：1.5px 灰色虚线，外扩 4px。悬停：加深。选中：2px 主题色实线。启动页这类深色背景：白色半透明 |
| 块标题 | 编辑态顶部常驻一条操作条（拖拽图标 + 标题 + 折叠 + ✕，`:3184-3201`） | 悬停或选中时才出现，是浮在块左上角的小标签（拖拽点 + 标题）。自建组件后面加灰字「· 组件」 |
| 自建块标题 | 没有页面 spec 时显示块 id，比如 `w_note1`（`SpecOf`，`:1198`） | 用 `widgets[]` 里的 `title`；没有 title 就用类型名（便签 / 时钟…）。插件块用插件给的标题。**永远不显示 id** |
| 块操作 | ✕ 统一表示「隐藏」 | 内置块：「眼睛划线」图标（矢量 Path），表示隐藏（可在「添加」里找回）。`custom:` / `plugin:` 块：`Ico.Trash`，表示删除。都放在块右上角，悬停或选中时出现 |
| 折叠 | 每块一个折叠按钮，外加「全部折叠」 | 删掉（只是显示状态，不写进布局，删了不影响数据） |
| 手柄 | 网格 e/s/se；canvas 8 个方向 | 网格：右边缘 + 下边缘（按 Q3），悬停或选中时出现。拖右边缘时显示「N / 列数」提示，只能按整列变化，并且受 `MinCols` 限制 |
| 拖动 | 浮起跟手 + 落点框 + FLIP（第八轮已做） | 行为不变，落点框改成主题色浅底 + 虚线框；拖动时显示淡色列底纹 |

### 5.2 编辑条与「添加」抽屉

- **位置**：从页面底部挪到**内容区顶部**，单行，圆角 12 的白色浮条。设置子页的编辑条从左侧导航右边开始，不盖住导航。
- **内容**：从左到右依次是：
  - 「编辑「页名」」
  - 「列表 / 网格」分段切换，写 `pages.<id>.mode`
  - 一句灰色提示：列表模式写「拖动调整上下顺序」；网格模式写「拖动换位置 · 拖右边缘改宽度」
  - 撤销、重做两个图标按钮，没有可撤销 / 可重做时置灰
  - 分隔线
  - 「添加」按钮（主题色软底），有隐藏项时右上角带数字角标
  - 「重置本页」
  - 「完成」（主题色实心）
  - 「⋯」菜单：「导出本页… / 导入到本页… / 整套布局与方案…」，最后一项跳到设置 → 布局
- **删掉**：「吸附 N」「自动靠拢」「全部折叠 / 全部展开」，以及按钮下面那行提示。吸附档固定为默认值，自动靠拢固定为开。`LayoutEditPrefs.AutoPack` 继续能读，但界面上不再提供修改。
- **「添加」抽屉**：从右侧滑出，宽 316px，取代底部托盘（`LayoutEditor.cs:235-244`）。
  - 上半部分「已隐藏的内容」：本页被隐藏的内置块，每项是图标 + 名称 + 一句说明 + 「显示」。没有隐藏项时显示一个虚线空状态框。
  - 下半部分「组件」：`WidgetLibrary` 里的类型，每项是图标 + 名称 + 一句说明 + 「添加」。不允许重复的类型已加过就显示「已添加」并置灰。网页 / 按钮只在 Q6 的开关打开后出现。
  - 新块加到页面末尾，滚过去并闪一下主题色光晕。
- **快捷键**（已有的保持）：Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z、Esc 先关抽屉再退出编辑、Delete 对内置块是隐藏、对组件是删除。
- **常态**：页面上没有任何编辑痕迹，进入方式是右键空白处的「自定义布局…」。

### 5.3 模式收敛（按 Q1）

- 界面上只有「列表（stack）/ 网格（grid）」两种。
- 读到 `mode=canvas` 的页时，渲染按 grid 推导：顺序先按 y 再按 x；`span = clamp(round(w × cols), MinCols, cols)`。**文档原样不动**，进编辑器提交一次之后才写成 grid。推导方法写成纯函数，并补单测。
- `Steps={0,4,8,16,24}`、`CycleSnapStep` 和画布 8 向手柄退出界面；相关代码能删的删，删不干净的留着但不可达，并在 DECISIONS 里写明。
- AI `layout_apply`：`mode` 参数说明改成只列 stack / grid；收到 canvas 时按 grid 处理，并在结果里返回一条提示。

### 5.4 设置 → 布局（对应 layout3 第二张）

页面分成五组，组标题是卡片外面的灰色小字：

1. **外观**：「区块间距」做成紧凑 / 标准 / 宽松三段切换（数值沿用 6/10/14），说明写「所有可以自定义的页面一起生效」。
2. **导航**：
   - **顶部页签**：做成一排可以拖动排序的标签，每个后面带眼睛图标。「设置」显示锁形图标，不能隐藏。右侧有「恢复默认」。改完标题栏**马上**变（`ShellTabs.SetTabs`）。
   - **标题栏**：现在有 4 个开关：桥接 / AI / 主题 / 账号（`SettingsLayoutPage.cs:191-207`，每行都带「只隐藏 / 显示,不改形态」的说明）。改成一排勾选项「AI 按钮 / 主题切换 / 账号」，**去掉「桥接」**（按用户既定决定，C 桥只在断开时显示；`ShellTabs.SetTitlebar(bridge:)` 和已存的值保留，只是界面上不再提供）。说明只写一句「隐藏 AI 按钮后仍可按 Ctrl+K 打开」。注意：效果图里写的是「账号名称」，但代码里这个开关控制的是整个账号芯片，以代码为准写成「账号」。
3. **页面**：
   - 卡片网格，每行 3 张。数据来自 `LayoutBlockEntryCatalog.All`（`SettingsLayoutPage.cs:1116-1233`，共 17 项）：去掉 `SingleBlock` 的 2 项，去掉「布局」页自己（`settings:layout`），**补上实例库**（`library:main` 是区块宿主，`LibraryPage.cs:207`，但不在入口表里），合计 15 张。
   - 每张卡片：缩略图（按该页当前布局画出各块的位置和宽度，组件块用主题色浅底）+ 页名 + 状态标签（「默认」或主题色的「已自定义」）+ 一行摘要（比如「网格 · 1 个组件 · 隐藏 1 项」，默认页写「和出厂时一样」）+ 「编辑」按钮；已自定义的再加「恢复默认」。
   - 默认显示 6 张（启动、下载、实例库、联机、账号、AI 助手），下面是「显示全部 15 个页面」。
   - 组标题右边有「全部恢复默认」。
   - 子视图（安装页、实例详情内嵌的版本管理）保留现有 Hint：找不到页面时提示怎么打开。
4. **方案**：
   - 卡片，每行 4 张：标签（正在使用 / 方案 / AI 备份）+ 名称 + 一行信息（保存时间、几个页面自定义过）+ 「应用」+ 「⋯」（重命名 / 复制一份 / 删除）。正在使用的方案用 1.5px 主题色描边，不能删除。
   - 组标题右边有「导入… / 导出整套布局… / 存为新方案」。导出时仍然先问导出 v2 还是 v1 兼容格式。
   - AI 备份靠名字前缀 `AI 备份 ` 识别，显示名改成「AI 改布局前」+ 时间。
5. **高级**（默认折叠）：标题写「分栏、网页与按钮组件、布局文件」，副标题写「一般不需要动」。展开后：
   - **分栏宽度**：一句话写「已调整 N 处：〈页面名〉…。在页面上拖两栏中间的分隔线即可调整，双击复位」+ 「全部复位」。**13 行的分栏表删掉。**
   - **网页和按钮组件**：Q6 的全局开关。打开后下面出现一条琥珀色提示：「导入的布局里如果带网页或按钮组件，仍然默认关闭，需要你逐个打开」。
   - **组件管理**：「已创建 N 个组件 · 管理…」，展开现有的内联管理面板（`LayoutWidgetRows`），文案里去掉 L6。
   - **布局文件**：「打开所在文件夹」+ 「在编辑器中打开…」。后者用独立窗口承载现有的 `LayoutJsonEditor`（校验 / 应用 / 刷新的逻辑不变），**页面上不再常驻 JSON 框**。

页面顶部的说明改成：「调整导航、各页面的排列，或者保存成方案。改动即时生效，随时可以恢复默认。」

### 5.5 各页区块改名与默认布局（对应 pages3）

**改名**：只改 `Title`，**不改 id**。

| 文件 | 现在 | 改成 |
|---|---|---|
| `Pages/MultiplayerPage.cs:110-113` | 状态卡 / 房间卡 / 操作区 / 玩家列表 | 联机状态 / 房间 / 快捷操作 / 玩家 |
| `Pages/PlaytimePage.cs:63-65` | 累计卡 / 范围卡 / 明细列表 | 累计时长 / 时间范围 / 明细 |
| `Pages/JavaPage.cs:113` | 已检测列表 | 已检测的 Java |
| `Pages/ServersPage.cs:47` | 实例行 | 实例 |
| `Pages/AccountPage.cs:91` | 账号卡 | 账号列表 |

**默认布局**（按 Q4；只在文档里没有这一页时生效）：

| 页 | 默认 |
|---|---|
| 联机 `multiplayer` | 网格：说明（整行）/ 联机状态 + 房间（各半）/ 快捷操作（整行）/ 玩家 + 节点说明（4:2） |
| 游戏时长 `playtime` | 网格：累计时长 + 时间范围（各半）/ 明细（整行）；**概览默认隐藏** |
| Java `java` | 网格：安装方式 + 实例默认（各半）/ 已检测的 Java（整行）；**说明默认隐藏**，版本对应关系挪到「实例默认」的提示文字里 |
| 启动 `launch`（只管下方内容区） | 网格：新闻整行。下载任务 / 游戏时长 / 便签 / 日志仍然默认隐藏；显示后两两并排，日志占整行 |

**启动页**：英雄区仍然不进布局系统。编辑态英雄区降到 45% 透明度并去饱和，右上角显示「🔒 主区域固定，不能拖动或隐藏」。下方内容区的编辑控件用深色玻璃配色（见 pages3 启动页）。

**只有一块内容的页**：右键菜单改成至少 2 个块才出现（按 Q5，`BlockHost.cs:101`）。

**实例库**：按 Q2，只套用新的编辑态外观，`LibraryPage.cs` 自带的布局菜单（`:231-235`）文案同步去掉内部说法。

## 6. 工作包

### 6.0 执行方式（2026-11-20 用户追加，覆盖本文件和 `交接必读` 里与之冲突的条款）

- 用户原话：「直接使用 5 个子代理并行开始跑，确保可回滚，实机没有我的运行不要跑」。
- **5 个子代理同时开工**，每个子代理一个包（L1 和 L2 合并成 WP-L12，L6 留给主 agent 在合并后做）。
- **回滚点**：
  - 开工前给 `main` 的当前提交打标签 `r34-baseline`（= `4277a02`）。`main` 分支和主工作区 `D:\项目\TipCL\TipCL\` 本轮**一个字节都不动**。
  - 集成分支 `r34/base`（工作区 `D:\项目\TipCL\_r34\base\`）= `r34-baseline` + 本任务书 + v3 效果图 + D-81。
  - 每个子代理在自己的分支和工作区里干活（见 6.1），只往自己的分支提交。
  - 想撤销某个包：删掉它的工作区和分支（`git worktree remove` + `git branch -D`）。想全部撤销：删掉 `_r34\` 下全部工作区和 `r34/*` 分支，`main` 原样不变。
- **不跑实机**：用户不在场时**不启动 `TipCL.exe`**，包括 `--shot`、`--smoke`、`--i18n-check`、`--design-sheet`、`--nav-dump` 等所有开关，也不做任何界面自动化。允许的只有 `dotnet build` 和 `dotnet test`。截图和自检全部列进「待真机」清单，等用户在场时再跑。

### 6.1 总表（填进 `windos/PROGRESS.md` 新的「R34」一节）

| 包 | 分支 / 工作区 | 内容 | 独占文件（路径相对 `windos/src/TipCL/`，测试相对 `windos/tests/`） | 状态 |
|---|---|---|---|---|
| WP-L0 | `r34/l0` · `_r34\l0` | 令牌、矢量图标、截图开关（只加代码，不运行） | `Themes/Controls.xaml`、`Themes/Palette.Light.xaml`、`Themes/Palette.Dark.xaml`、`DevShot.cs`、`windos/docs/design-spec.md`（追加一节） | 进行中 |
| WP-L12 | `r34/l12` · `_r34\l12` | 区块卡片外观 §5.1 + 编辑条与「添加」抽屉 §5.2 | `Pages/Layout/LayoutEditor.cs`、`Pages/Layout/GuideLayer.cs`、新建 `Pages/Layout/LayoutAddDrawer.cs`；测试 `LayoutEditorTests.cs`、`LayoutAutoPackTests.cs`、新建 `LayoutEditorV3Tests.cs` | 进行中 |
| WP-L3 | `r34/l3` · `_r34\l3` | 模式收敛 §5.3 | `Pages/Layout/LayoutEngine.cs`、`Pages/Layout/GridCanvasPanel.cs`、`Core/Launcher/Layout/LayoutSchema.cs`、`Core/Launcher/Ai/LayoutTools.cs`；测试 `LayoutCanvasTests.cs`、`LayoutEngineTests.cs`、`LayoutGridCompactTests.cs`、`LayoutV2RpcTests.cs`、新建 `LayoutCanvasMigrationTests.cs` | 进行中 |
| WP-L4 | `r34/l4` · `_r34\l4` | 设置 → 布局 §5.4 | `Pages/SettingsLayoutPage.cs`、`Pages/Layout/ShellTabs.cs`、`Pages/Layout/LayoutEditPrefs.cs`、`UiPrefs.cs`、新建 `Pages/Layout/LayoutJsonWindow.cs`；测试 `LayoutProfileTests.cs`、`LayoutShellTests.cs`、新建 `SettingsLayoutV3Tests.cs` | 进行中 |
| WP-L5 | `r34/l5` · `_r34\l5` | 各页改名与默认 §5.5 | `Pages/MultiplayerPage.cs`、`PlaytimePage.cs`、`JavaPage.cs`、`ServersPage.cs`、`AccountPage.cs`、`LaunchPage.cs`（只动区块宿主那一段）、`LibraryPage.cs`（只动菜单文案）、`Pages/BlockHost.cs`；测试 `LayoutDefaultTests.cs`、`LayoutBlockHostTests.cs`、`LayoutNativeBlockTests.cs`、新建 `PageDefaultsV3Tests.cs` | 进行中 |
| WP-L6 | `r34/base` | 合并、接线、词表合入、文档、总回归 | 合并冲突处理；`locales/*`；`docs/布局使用指南.md`、`docs/布局快速上手.md` | 主 agent，五包交回后做 |
| WP-L7 | — | Linux 树同步（下一轮，只登记） | 仓库根 `src/TipCL.App/Pages/Layout/**` | 只登记 |

**清单之外的文件一律不改。** 确实非改不可，就停下来写进报告，交给主 agent 合并时处理。

### 6.2 共享文件的规则

- **词表**（`zh_CN.json` / `en.json`）：各包**不直接改**，在自己的 `windos/progress/WP-Lx.md` 末尾列出「新增 / 废弃 key（中文 → 英文）」。主 agent 合并时统一合入、重新打包 `.json.gz`。
- **测试**：各包只改或新建自己清单里的测试文件。别的包的测试被自己的改动弄红了，停下来写进报告，由主 agent 裁决。
- **DECISIONS**：D-81 已由主 agent 写进 `r34/base`。各包需要新的决定时**不要**改 `DECISIONS.md`（5 个分支同时追加必然冲突），写在自己报告的「决定」一节，主 agent 合并时统一编号为 D-82 起。
- **PROGRESS.md**：各包不改，主 agent 合并时汇总。

### 6.4 并行接口约定（5 个分支各自独立编译，靠这些约定在合并后接上）

各分支互相看不到对方的代码，所以**只能通过运行时查找的资源键和下面这几个约定对接**，不许在 C# 里直接引用别的包新增的类型或成员。

1. **资源键（WP-L0 提供，两个 Palette 文件成对出现）**。别的包一律用 `SetResourceReference` 或 `TryFindResource` 加代码内的兜底值，**禁止**用 `FindResource` 或 `(Style)Resources[...]` 这种找不到就抛异常的写法：
   - 画刷：`B.EditOutline`（虚线框灰）、`B.EditOutlineHover`、`B.EditOutlineOnDark`（深色背景用的白色半透明）、`B.EditDropFill`（落点框浅底）、`B.EditColShade`（拖动时的列底纹）、`B.EditBarBg`（编辑条和浮动标签的底色）、`B.EditDim`（深色启动页编辑态的压暗遮罩）、`B.WarnSoft`（琥珀浅底）、`B.WarnInk`（琥珀字色）。
   - 矢量图标（`Geometry`，16×16 视框，用于 `Path.Data`，描边绘制）：`Geo.Eye`、`Geo.EyeOff`、`Geo.Lock`、`Geo.Undo`、`Geo.Redo`、`Geo.Grip`（六点拖拽）。
   - 样式：`Layout.EditBar`（Border）、`Layout.EditLabel`（Border）、`Layout.IconAct`（Button，块上的图标按钮）、`Layout.Drawer`（Border）、`Layout.SegItem`（列表 / 网格分段里的 ToggleButton 或 RadioButton）。
2. **深色表面**：WP-L12 在 `LayoutEditor` 内部按 `pageId == "launch"` 判断要不要用深色玻璃配色，不新增给外部调用的属性（WP-L5 管 `LaunchPage.cs`，碰不到这个属性）。
3. **网页 / 按钮组件的全局开关（Q6）**：
   - WP-L4 在 `UiPrefs.cs` 加 `LayoutDangerWidgets`（bool，默认 false），在 `LayoutEditPrefs.cs` 加 `public static bool AllowDangerWidgets { get; set; }`（照 `AutoPack` 的写法，读写 UiPrefs）。
   - WP-L12 在新建的 `LayoutAddDrawer.cs` 里放一个钩子 `public static Func<bool> DangerWidgetsAllowed = () => false;`，抽屉按它过滤网页 / 按钮类型。
   - 合并后由主 agent 加一行接线：`LayoutAddDrawer.DangerWidgetsAllowed = () => LayoutEditPrefs.AllowDangerWidgets;`。
4. **`LayoutEditPrefs.cs` 归 WP-L4**。WP-L12 只读 `AutoPack`，不改这个文件；界面上不再提供修改 `AutoPack` 的入口。
5. **WP-L3 不许删除或改名 `LayoutEditor.cs` 用到的任何成员**（比如 `LayoutCanvas.HandleNames`、`Steps`、`ModeCanvas`、`CycleSnapStep` 依赖的东西）。不再需要的成员加 `[Obsolete("R34: …")]` 注释或只在注释里说明，真正的删除留到合并后。
6. **WP-L12 不许改 `LayoutEngine.cs` 的公开签名**。需要引擎新增能力时写进报告，不要自己加。
7. **`BlockHost.cs` 归 WP-L5**（「至少 2 块才有右键菜单」）。WP-L12 不改它。
8. **入口表 `LayoutBlockEntryCatalog`（在 `SettingsLayoutPage.cs`）归 WP-L4**，补 `library:main` 也是 WP-L4 做。

### 6.3 每个包的验收

**WP-L0**
- `Controls.xaml` 新增样式：`Layout.EditOutline`（虚线框）、`Layout.EditLabel`（浮动标签）、`Layout.EditBar`（编辑条）、`Layout.Drawer`（抽屉）、`Layout.IconAct`（块上的图标按钮），以及深色玻璃版本。颜色键在两个 Palette 文件里**成对**出现。
- `DevShot.cs` 新增 `--edit`（截图前进入编辑态）、`--pop layout-drawer`（打开「添加」抽屉）、`--select <blockId>`（模拟选中，用来拍出标签和手柄）。`--shot settings:layout --expand advanced` 能拍出「高级」展开的样子。
- `--design-sheet` 样张页能渲染出上面这些样式（只写代码；明暗各截一张存到 `windos/docs/ui-audit/sheet/` 这一步放进「待真机」清单）。

**WP-L12（卡片部分）**
- 对照 layout3 第一张「编辑态」逐项核对代码：边框、标签、图标按钮、右边缘手柄。截图命令 `--shot download --edit --select list` 写进「待真机」清单。
- 新增单测：自建块标题取 widget 的 `title`；没有 title 时取类型名；插件块取插件给的标题；断言块 id 不会出现在任何显示文本里。

**WP-L12（编辑条与抽屉部分）**
- 编辑条、「添加」抽屉、⋯ 菜单都和效果图一致。底部托盘和吸附 / 自动靠拢 / 折叠的界面全部消失。
- 撤销 / 重做按钮的可用状态跟着历史栈变。抽屉里点「显示」或「添加」之后，页面滚到新块。
- 现有的 `LayoutEditorTests` 和 `LayoutAutoPackTests` 全部通过（只允许删掉针对已删界面的断言，并在报告里一条条列出）。

**WP-L3**
- 新增迁移单测：用 3 份 canvas 文档（含隐藏块、自建组件、插件块）验证——渲染出的块集合不变；顺序按「先 y 后 x」；span 在合法范围内；**读进来再原样写出去，文件逐字节相同**；在编辑器里提交一次后变成 grid，id / hidden / widgets 都在。
- `LayoutCanvasTests` 里属于界面行为的用例改为 Skip 并写明原因；纯模型的用例继续通过。
- `layout_apply` 收到 `mode=canvas` 的用例：返回成功，带一条提示，落盘的是 grid。

**WP-L4**
- 对照 layout3 第二张：五组齐全；「高级」默认折叠；界面上搜不到「ui_layout_v2」「桥接」「L4」「L6」「#」开头的编号。按这个清单写一条单测，扫描 `SettingsLayoutPage.cs` 里所有 `I18n.L("…")` 的字符串字面量。
- 页签拖动排序、隐藏之后，标题栏马上变；「设置」不能隐藏。
- 页面卡片的「已自定义」判定和 `LayoutStoreV2` 里有没有这一页的条目一致（单测）。
- 入口表补上 `library:main` 后，`LayoutBlockEntryCatalog` 相关测试里的数量（17 → 18）跟着改；页面卡片恰好 15 张，不含 `SingleBlock` 的页和 `settings:layout`（单测）。
- 方案的应用 / 重命名 / 复制 / 删除继续通过 `LayoutProfileTests`。

**WP-L5**
- 5 个文件的改名完成；`LayoutDefaultTests` / `LayoutBlockHostTests` 跟着更新。
- 4 个页面在文档里**没有**该页条目时，渲染出的是新的默认布局；**有**条目时完全照文档渲染（单测，两种情况都要覆盖）。
- 只有一块内容的页（`settings:network`、`settings:about`）右键没有「自定义布局…」（单测）。
- 截图命令 `--shot multiplayer`、`--shot playtime`、`--shot java`、`--shot launch`（各拍常态和 `--edit`）写进「待真机」清单。

**WP-L6（主 agent，合并后；截图部分等用户在场）**
- 截图矩阵存到 `windos/docs/ui-audit/layout-v3/`：下载、启动、联机、游戏时长、Java、实例库、设置 → 布局 共 7 页 × 常态 / 编辑态 × 明 / 暗；另加「添加」抽屉打开、高级展开各一张。
- 更新 `docs/布局使用指南.md` 和 `docs/布局快速上手.md`：三个入口、手势表、模式说明（只剩两种）、编辑态截图换成新的。§9.1「坐标到不了渲染器」那一节**按代码现状复核后改写**——`RebuildPage` 现在会保留几何和外观（`LayoutEngine.cs:595-625`）。
- 两个版本各跑一遍 §7 的全部命令。

## 7. 构建与验证（每个包收尾都要跑，只在自己的工作区里跑）

```powershell
# 7.0 进入自己的工作区，例如
cd D:\项目\TipCL\_r34\l0\windos

# 7.1 构建（防文件锁参数，D-14；5 个分支同时在编，所以：
#      不要跑 dotnet build-server shutdown（会掐掉别人的编译），一律加 -nodeReuse:false）
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1 -nodeReuse:false
#   premium 版再加 -p:Premium=true（两版都要编过）

# 7.2 测试：开工后先在自己的工作区跑一次全量，记成基线；收尾再跑一次，不许低于基线
dotnet test TipCL.sln -c Release --no-build -nodeReuse:false

# 7.3 体积（compact）
$len=(Get-Item src/TipCL/bin/Release/net48/TipCL.exe).Length
if ($len -gt 5757264) { throw "SIZE FAIL: $len" } else { "SIZE OK: $len" }
```

- **不启动 `TipCL.exe`**（见 6.0）。`--i18n-check`、`--smoke`、`--shot` 留到真机阶段由用户在场时跑。
- 词表缺失用离线方式检查：从自己改过的 `.cs` 里提取所有 `I18n.L("…")` 字面量，和 `zh_CN.json` / `en.json` 的 key 比对，把缺的列进报告。

**收尾三件套**（`交接必读` §0）：
- 自证两版构建 0 错误、0 新增警告，测试不低于基线，compact 体积达标；
- 写 `windos/progress/WP-Lx.md`：做了什么、文件清单（逐个列出）、测试前后的数字、新增或废弃的词表 key、要主 agent 合并时处理的事、「决定」、「待真机」清单、风险；
- 在自己的分支上 `git commit`，提交信息以 `WP-Lx` 开头。可以分多次提交，但每一次都要能编过。

## 8. 完成定义（全部满足才算完成）

- [x] Q1–Q7 的结论已写进 D-81
- [ ] L0、L12、L3、L4、L5 全部交回并合并到 `r34/base`，L6 完成
- [ ] Release 版本构建 0 错误、0 新增警告；测试不低于基线；`--i18n-check` 缺失 = 0
- [ ] 旧布局兼容：用 `docs/images/layout/` 截图时用的那几份种子文档（stack / grid / canvas / 带自建块 / 带 3 个方案）原样读入，渲染正常，原样写出时逐字节相同
- [ ] 界面上搜不到内部说法（L4 的单测 + 人工翻一遍截图矩阵）
- [ ] compact ≤ 5,757,264 B；premium 记录实测体积
- [ ] 截图矩阵齐全，两份布局文档已更新
- [ ] 真机验收清单交给用户：拖动手感、抽屉动画、深色启动页编辑态、标题栏随页签实时变化（沙箱里跑不出窗口的部分，注明「未实测」）
- [ ] 没有改版本号、没有 push、没有发 release

---

### 附：D-81 原文（已追加到 `r34/base` 分支的仓库根 `DECISIONS.md`）

```
### D-81 · 布局 v3:编辑界面重做的七条边界(R34,2026-11-20,用户拍板)
- Q1 canvas:界面只留列表/网格;canvas 文档读时按网格推导,提交前不改写原文。
- Q2 实例库:保留区块编辑,只换编辑态外观。
- Q3 网格手柄:右边缘 + 下边缘,去掉右下角。
- Q4 默认布局:联机 / 游戏时长 / Java / 启动内容区改新默认;已存过的页不受影响。
- Q5 单块页:≥2 块才有「自定义布局…」。
- Q6 网页/按钮组件:新增全局开关(默认关),每个组件自己的 enabled 不变。
- Q7 Linux:本轮不同步,登记 WP-L7。
```
