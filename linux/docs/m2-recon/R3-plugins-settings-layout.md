# M2-R3 · 插件页 / 设置页 / 页面布局自定义框架 移植规格书

> 侦察对象（Windows 版，只读）：`D:\项目\tipcl\windos\tipcl\src\TipCL\`
> 设计基准：`docs/mockups-hark/v2/plugins2.png`、`settings2.png`
> 目标：Avalonia 11 / .NET 8，`src/TipCL.App/` + `src/TipCL.Core/`
> 本文只做规格，不含源码。行号均指 Windows 版源文件。

---

## 0. 总览与规模

| 板块 | Windows 版文件 | 行数 | 说明 |
|---|---|---|---|
| 布局框架 | Pages/PageBlock.cs, PageLayoutStore.cs, PageLayoutIO.cs（+ Pages/DashLayout.cs 供 JSON 写法对齐） | ~460 | 纯模型层无 WPF，可直接搬 Core |
| 区块宿主 | Pages/BlockHost.cs | 646 | WPF 交互层，需重写为 Avalonia |
| 设置页 | Pages/SettingsPage.cs / .Themes.cs / .PhoneSync.cs | ~2480 | 7 项左导航 + 分组卡 + BlockHost |
| 插件页 | Pages/Plugins/*（15 个文件） | ~3900 | 已安装/市场/开发者三分段 + 设置子视图 |
| 契约测试 | tests/TipCL.Tests/PageLayoutTests.cs | 264 | 15 个 [Fact]，应随模型层一起移植 |

三块里**优先级最高、风险最低的是布局框架**（纯模型 + 契约测试），建议先落；BlockHost 其次；设置页和插件页可并行。

---

## 1. 页面布局自定义框架（WP-U1 冻结契约）

### 1.1 数据模型（可直接复制进 TipCL.Core）

**PageBlock.cs（127 行，零 WPF）**：
- `BlockState { string Id; bool Hidden }` —— Id 是区块稳定英文 id，Blocks 数组顺序即显示顺序；隐藏项也占位（恢复时回原位）。
- `PageLayoutDoc { int Version=1; List<BlockState> Blocks }`
  - `Default(ids, defaultVisible)`：defaultVisible 之外的标 Hidden。
  - `Normalize(knownIds, defaultVisible)`：剔除未知 id；缺失 id 按 defaultVisible 补到尾部；已有项的顺序/Hidden 一律不动。
  - `FromJson(e, knownIds, defaultVisible)`：version≠1 / 根不是对象 → null；`hidden` 只认 JSON 布尔 true（其余一律可见）；id 去重。
  - `ToDict()/ToJson()`：`WriteIndented + UnsafeRelaxedJsonEscaping`（中文不转义），与 DashLayout.ToJson 写法对齐。

**PageLayoutStore.cs（256 行，仅依赖 I18n + BridgeHost.FindRoot）**：
- 文件 `ui_page_layouts.json`，结构 `{"version":1,"kind":"tipcl-page-layout","pages":{pageId: doc}}`，Kind 常量 `"tipcl-page-layout"`。
- `Load(pageId) → doc?`（缺失/损坏 → null = 全默认）；`LoadAll()`；`Save()` 原子落盘（写 .tmp 再 File.Replace/Move）并**吞掉全部异常**（写不进去不崩 UI，内存缓存保留最新值）。
- `ExportJson(pages)` / `ParseImport(json)`：导入校验 kind+version，不符抛 `InvalidOperationException`（中文消息「布局文件无效」）；单页损坏跳过该页。
- `DirOverride` 静态属性供测试注入临时目录；缓存跟着目录走。
- 移植注意：`BridgeHost.FindRoot()` 是 Windows Services 层；Linux 版数据目录定位换成 App 自己的路径服务（`AppContext.BaseDirectory` 回退语义保留）。

**PageLayoutIO.cs（76 行，WPF 仅文件对话框 + Toast）**：
- `ExportToFileDialog(preferredName)`：导出全部页 + 确保当前页最新；成功/失败 Toast。
- `ImportFromFileDialog()`：返回导入页数；当前页立即生效，其他页是缓存实例、下次打开生效。
- Linux 替换点：`Dlg.SaveFile/PickFile` → Avalonia `StorageProvider.SaveFilePickerAsync/OpenFilePickerAsync`；`AppServices.Window?.Toast` → Linux 版 Toast 服务。

### 1.2 BlockHost（WPF 646 行 → Avalonia 重写）

**职责**：页面把可重排内容切成 `BlockSpec { Id, Title(已 I18n), Content(UIElement), DefaultVisible }` 交给宿主；宿主负责顺序渲染、编辑模式（拖拽重排/显隐）、持久化。

结构树（常态）：
```
BlockHost : UserControl（零装饰：Border 无底色、厚度 0 透明描边、CornerRadius 10）
└─ Grid ["*,Auto"]，内容边距 (18,14,18,16)，对齐 PageBase.ScrollBody
   ├─ [0] ScrollViewer
   │  └─ SPanel(Vertical, Gap=10)  ← 每个可见块一张 BlockCard，按 _order 顺序
   │     └─ BlockCard : Border（常态零装饰，编辑态见下）
   │        ├─ 操作条（仅编辑态可见）：H(8) [IconBtn(拖拽手柄 Ico.Sort, Cursor=SizeAll),
   │        │    Txt(块标题, 12.5 bold, 省略号截断), Spring, IconBtn(隐藏 Ico.Close)]
   │        │    Margin(6,6,6,2)
   │        └─ ContentControl(块内容)（编辑态 Padding(6,0,6,8)、IsHitTestVisible=false）
   └─ [1] 编辑态底部区 V(8)，Margin 对齐内容边距
      ├─ 托盘 Border（B.Paper2 底、CornerRadius 8、Padding 10）：Muted("已隐藏的区块") +
      │  WrapPanel —— 每个隐藏块一枚「+ 标题」Chip（BtnKind.Chip + Ico.Add），点击恢复
      └─ 悬浮工具条 Border（B.Paper 底 + B.Line 1px 描边、CornerRadius 10、阴影 16/0.07/3）：
         右对齐 H(8) [完成编辑(Primary) / 重置本页 / 导出布局 / 导入布局]
```

**编辑模式入口**：Root 右键菜单「自定义布局…」，仅 `specs.Count >= 2` 时注册。页面自带 ContextMenu 时 WPF 取最近属性值、天然对方优先——Avalonia 需注意 Avalonia 的 ContextMenu 不做属性继承链合并，M2 版要在 BlockHost 内部处理「内层已有菜单则不覆盖」（挂到内容根部检测，或文档化为「挂 BlockHost 的页面不得再挂根 ContextMenu」）。

**拖拽重排算法**（与 WPF 版逐行对应，Avalonia 下照搬逻辑）：
1. 手柄按下：缓存各可视块 ActualHeight（`_dragHeights`，>1px 才算，兜底 120），被拖卡 Opacity=0.75，捕获指针。
2. MouseMove：取指针相对列表 Y；**逐格**与相邻块中点比较（MidY 用缓存高度 + Gap=10 累加），跨多格循环交换直到不满足；不做动画插值。
3. 松开/失去捕获：恢复 Opacity、Persist()。
4. `MoveVisible(k, dir)`：_order 中把块插到目标可见块旁（dir>0 插到后面），可视树同步搬家（InsertVisual 按 _order 中前面的可见块数算可视索引）。

**持久化**：任何变更（隐藏/恢复/交换/结束拖拽）→ 组装 PageLayoutDoc → `PageLayoutStore.Save` → 触发 `Changed` 事件；ExitEdit 自动保存 + Toast「布局已保存」；ResetPage 保存 Default 文档 + Toast「布局已重置」；导入后 ApplyStored + Render。

**编辑态视觉**：卡描边 1.4px B.Accent + 背景 B.Accent 色 10 透明度蒙版（跟随主题刷新）；列表间距常态与旧版逐像素等价。

**BlockHost 接入模式（11 页）**：Windows 版当前接了 3 个视图（settings:general 9 块 / settings:network 1 块 / settings:about 1 块）；DashHost（首页）是同族独立实现（567 行，网格拖拽，超出本规格但同构）。M2 其余页（launch/library/download/install/instances 等）按同一模式接入：页面构造时把「分组小标题 + 卡片」包成 BlockSpec（标题留在块内，重排跟着走），BlockHost(pageId, specs) 替换原 ScrollBody。**pageId 命名约定：`<pageKey>` 或 `<pageKey>:<viewKey>`**（已定例：`settings:general`、`settings:network`、`settings:about`）。

### 1.3 契约测试（15 个 Fact，随模型层移植）

`tests/TipCL.Tests/PageLayoutTests.cs`：Default 保序标隐、Normalize 剔未知/补尾/保已有、FromJson 布尔语义/坏版本返 null/去重、ToJson 往返、Save→Load 经 DirOverride 临时目录往返、坏文件 → null/空、ExportImport 往返、ParseImport 跳坏页、错 kind/version 抛异常、空 pageId 忽略。**这些测试不依赖 UI，搬进 `tests/TipCL.Tests/` 后应全绿**——这是 M2 布局框架的验收线。

---

## 2. 设置页（SettingsPage，7 项左导航 + 分组卡 + 布局自定义）

### 2.1 页面骨架（对照 settings2.png）

```
SettingsPage : PageBase
└─ Grid [null,"168,*"]                          ← 左导航 168px，右内容区
   ├─ 左导航 SPanel(V, 2)：Margin(12,16,0,12)
   │  ├─ 7 枚 NavBtn（ToggleButton, Style="Seg.Item"，Text 宽 120，Padding(12,8)）
   │  │    0 通用 / 1 账号 / 2 Java / 3 下载与网络 / 4 服务器 / 5 游戏时长 / 6 反馈与关于
   │  └─ _saveState（自动保存状态小字，Margin(12,10,0,0)）
   └─ ContentControl _view
```

视图切换：`Select(i)` 单选互斥；子页（账号/Java/服务器/游戏时长/反馈）是**本页自持的 PageBase 实例**（不与 MainWindow 共享，避免同一控件两个父级），懒创建 + `EnsureLoadedAsync/OnShown/OnHidden` 转发；`OnEvent(BridgeEvent)` 转发给当前子页。

### 2.2 「通用」视图 = BlockHost("settings:general")，9 个区块

每个区块内容 = `V(10) [PTitle(仅首块)/Gt(分组小标题), 卡片]`；卡片内行 = `Ui.Field(标签, 控件, 说明小字)`，行间由 `Rows()` 插入 1px 通到卡片两端的分隔线（B.SegRail，Margin(-12,1,-12,1)），行 MinHeight 40（设计图上实测约 48 观感，控件行高由 Field 决定）。**字段结构：左侧竖排「标签 + 说明」、右侧控件右对齐（对照 settings2.png 的「启动后/最小化」行）。**

| # | BlockSpec.Id | 标题 | 内容 |
|---|---|---|---|
| 1 | appearance | 外观（PTitle 通用） | 深色模式 Switch / 主题色 6 色板（26px 圆片 CornerRadius 10，选中 2px B.AccentDeep 描边）/ 界面背景 4 预设 Combo（default/eye/warm/mist）/ 界面动画 Switch / 下载飞入动画 Switch / 飞入时长 Slider 200–1200ms |
| 2 | wallpaper | 壁纸 | 预设背景 4 缩略图（64×40，pack 资源释放到数据目录）/ 壁纸文件 / 轮播文件夹 / 随机顺序 / 轮播间隔 / 模糊 0–40 / 遮罩浓度 0–80 / 关闭-撤销-恢复出厂 |
| 3 | themePack | 主题包 | Combo 已存主题 + 保存/加载/删除/导入/导出 5 钮 |
| 4 | launcher | 启动器 | 启动器可见性 5 选 / 启动页主页 3 选 / 自定义主页路径 / 自动检查更新 / 语言下拉 + 实时预览 / 重新运行启动向导 |
| 5 | game | 游戏 | 游戏目录（TextBox+浏览）/ 默认内存 512–32768 Slider / 查看推荐 / 分辨率 W×H / 默认实例 Combo / 新版本隔离 4 选 / GC 5 选 / 默认窗口 2 选 / JVM 参数 / 允许多开 |
| 6 | storage | 存储 | 共享 libraries / 共享 assets / 扫描并清理（预览→勾选→确认三段式） |
| 7 | ai | AI 助手 | 接入方式 builtin/custom；自定义行：端点地址 / API Key（秘密行+清除）/ 模型（可编辑 Combo+刷新，地址或令牌变更自动拉取）/ 上下文窗口 / 备用模型 / 识图（自动检测） / MCP Server 开关+复制地址 / 测试 AI 连接 |
| 8 | memory | 记忆 | ai_memory.md 条目列表（日期+正文+编辑/删除），清空全部（Danger 确认），并发哈希防并发写（Same(index,entry) 校验 + Toast「记忆已变化」） |
| 9 | phoneSync | 手机同步 | IP+配对码连接 / 扫描局域网 / USB 自动连接（adb）/ 打开同步面板 |

「下载与网络」视图 = BlockHost("settings:network") 单块：下载并发 1–64 / 分片 1–16 / 文件下载源 3 选 / 社区资源源 3 选 / 跟随系统代理 / 限速 KB/s / CurseForge Key（秘密行）。
「反馈与关于」视图 = Grid["Auto,*"]：上 BlockHost("settings:about") 单块（宿主边距传 0 防叠加），下 FeedbackPage。

### 2.3 关键交互机制

- **自动保存**：所有控件变更 → `QueueSave()` → 500ms 防抖 `DispatcherTimer` → `AutoSaveAsync`（_saving/_savePending 重入合并）→ `save_settings` 全量字典 + `set_multi_instance` + `set_language`。文本框 LostFocus/回车才存（防半截路径）；页面 Unloaded 时立即补存。左下角 `_saveState` 显示「保存中…/已自动保存/保存失败」。Avalonia：DispatcherTimer → `DispatcherTimer`（Avalonia.Threading 同名）。
- **秘密行**（CF Key / AI Key）：TextBox 只显示 Tag 占位提示，真实值回显走 `*_set/*_hint` 状态字段；「清除/恢复内置」置 `_cfClear/_aiKeyClear`，保存时非空才提交、否则提交 `*_clear=true`。
- **主题色动态派生**（SettingsPage.Themes.cs，390 行，**纯 Color 数学、无 WPF 控件依赖，仅 ColorConverter/ResourceDictionary 两处小替换**）：`ApplyAccent()` 由用户 hex 派生 24 个主题键（AccentKeys 数组，含对比度EnsureWhiteSurface/EnsureTextContrast迭代、Glass 透明度、Banner/Logo 渐变）；`ApplyVisuals()` 派生背景预设 3 键（C.Canvas/B.Canvas/B.PageWash 渐变，eye/warm/mist 四套明暗各 4 色值全在源码里）。**建议整文件搬进 App 层的 ThemeService，Avalonia 用 `TryGetResource`/`Resources[key]=` 替换 ResourceDictionary 写法**——这是 M2 设置页对 A2 主题库的功能性扩展，需与 A2 键名表核对（Linux 版 Palette 已有全部 24+3 键，见 §5）。
- **Extras**（wpf-theme.json）：ThemeColor/Background/FlyAnimation/FlyDurationMs 落数据目录；静态 `SyncPersistedThemeColor/SyncPersistedBackground` 双写（后端设置 + 本地文件）。
- **语言切换**：available_languages/get_language/set_language/translate 预览 3 条样例文案；确认后拉起自身进程重启。

### 2.4 后端 RPC 依赖（get/save_settings 键全表见源码 SaveAsync ~L1549）

设置页是全应用 RPC 面最大的页面：`get_settings / save_settings / get_setting / set_game_dir / get_instances / allow_multi_instance / set_multi_instance / available_languages / get_language / set_language / translate / list_themes / save_theme / load_theme / delete_theme / import_theme / export_theme / check_update / start_self_update / cleaner_preview / cleaner_apply / get_smart_recommendation / test_ai_connection / ai_list_models / ai_vision_status / ai_vision_set_mode / background_history / can_undo_background / undo_background / reset_background`。Linux 版后端（Python/RPC 桥）M2 阶段尚未接通的部分必须留桩：**PageBase.Api 的 TryCallAsync 失败路径已设计为「回默认值不报错」，M2 可先接 UI + 本地设置存取，后端键值语义照本表**。

---

## 3. 插件页（PluginsPage，对照 plugins2.png）

### 3.1 页面骨架

```
PluginsPage : PageBase
└─ Grid ["Auto,*"]
   ├─ head Grid[null,"*,Auto"] Margin(22,16,22,12)
   │  ├─ [0] Seg.Rail 内 H(0)：[已安装 | 市场] ToggleButton(Seg.Item)
   │  └─ [1] H(6)：[从文件安装(Normal)] [⋯ IconBtn(\ue712)] —— ⋯ 菜单：刷新/开发者/插件设置
   └─ ContentControl _body（三分段 + 子视图切换）
```

- **已安装**：UniformGrid Columns=3（空列表时 1 列显示空态），Margin(15,0,15,16)；卡片 Margin(7)、Top 对齐。
- **市场**：`ScrollBody(搜索行, 标签条 WrapPanel, 卡片列表 SPanel(V,10))`；搜索框 300px + 放大镜 glyph；右侧「市场地址」「刷新市场」。
- **开发者**（⋯ 菜单进入）：本地安装卡（zip/文件夹/链接开发文件夹 3 钮）/ 运行环境卡（Node 宿主 + WebView2 状态逐行）/ 模型供应商卡 / 插件日志卡（Combo 选插件 + LogBox）。
- **插件设置子视图**（ShowSubView）：返回箭头 + H2 标题 + `PluginSettingsCard.Build()`（同卡片也复用于设置页）。
- **拖放安装**：整页 AllowDrop，zip/.tipcl/文件夹拖入即 `plugins_install_local`。Avalonia：`DragDrop.AllowDrop` + `DragDrop.DragOver/Drop` 事件，`e.Data.GetFiles()`（见 §6）。

### 3.2 已安装卡片（InstalledCard，结构树）

```
Card(V(0)) Padding(18,16)               ← 设计图：白卡 CornerRadius 12
├─ top Grid[null,"Auto,*,Auto"]
│  ├─ logo Border 36×36 CornerRadius 9，底色=LogoPalette 按 id 哈希取色
│  │  （#7C5CFF/#F59E0B/#64748B/#0EA5E9/#10B981/#E0559D，白字首字母 ExtraBold 14）
│  ├─ 名称 15 ExtraBold（ToolTip=id）
│  └─ 启用/禁用 Switch（失败回滚）
├─ desc Muted 12.5，固定高 36，两行省略
├─ foot Grid[null,"*,Auto"]
│  ├─ H(6)：版本 v1.2.0 文本 ｜ 状态 Tag（运行中 B.Ok/出错 B.Danger/重启后生效 B.Warn/
│  │  加载中 B.Info/已禁用/已停止）｜「开发」Tag(DevPath Tip)｜「本地」Tag｜「.NET」Tag｜「有更新」Tag
│  └─ ⋯ IconBtn → 菜单：更新(有更新时)/设置/重新加载/打开数据目录/查看日志/权限/
│       打开开发文件夹(dev)/插件主页/─分隔─/卸载或取消链接
├─ 错误行（B.Danger）/ restart_required 提示行（条件）
└─ 进度条 4px（隐藏，安装/更新任务期间显示，随 progress/finished 事件同步）
```

交互 RPC：`plugins_list / plugins_set_enabled / plugins_reload / plugins_uninstall(id, delete_data) / plugins_install(id) / plugins_install_local(path, dev) / plugins_log(id, lines)`；任务进度用 `task_added/progress/finished` 事件 + `_taskPlugin[taskId]=pluginId` 映射 + FlyToTasks 飞入动画。

### 3.3 市场卡片与详情（MarketCard / ShowMarketDetail）

- RowCard 行卡 14：48×48 图标框（B.AccentTint 底、CornerRadius 10）｜标题行 H(6)[名称 14 bold, vTag, 已审核 Tag(Ok/AccentSoft) 或 未审核(Danger/DangerSoft), .NET Tag]｜meta 行（作者 · 下载量 · 大小）｜desc MaxHeight 40｜tags 前 6 枚｜进度条｜右侧 [详情 IconBtn, 安装/更新/已安装]。
- 排序：可更新 > 已审核 > 下载量 > 名称。过滤：标签单选 + 搜索（名称/id/作者/简介，OrdinalIgnoreCase）。
- 标签条：全部 Tag 出现频次 Top 24，「全部」+ 各标签 Chip（选中 Primary）。
- 未审核安装护栏：未开 `plugin_allow_unverified` 时弹危险确认，可代为打开设置再继续。
- 详情弹窗 760px：头部（44 图标+名称+版本+审核 Tag+作者/id/大小）、简介、截图横滚（高 180，点击开浏览器）、需要的权限（PluginPerms.List）、更新日志；安装按钮钉在滚动区外底部。
- 市场地址错误聚合显示（「{0} 个市场地址读取失败」ToolTip 列出）。

### 3.4 PluginSettingsCard（设置卡，两处复用）

字段：市场地址（编辑弹 Form：多行校验 http(s) 前缀、去重）/ **加速镜像列表**（每行 Switch+名称+Raw 前缀+↑↓交换 priority+自定义镜像可删；持久化到 `plugin_mirrors`，语义照 MaiBot git_mirror_service：顺序即优先级）/ 允许安装未审核插件（危险确认）/ [管理插件|模型供应商] 入口（后者 Navigate("plugins")+ShowTab("dev")）。依赖 Core 的 `PluginMarket.GhMirror/DefaultMirrors()/CustomMirrorPrefix`（Windows 版 `Core/Plugins/PluginMarket.cs`，**未搬，见 §4**）。

### 3.5 插件框架层（PluginUi / PluginModels / PluginEvents / 宿主）

- **J 工具**（PluginModels.cs L9-157）：camelCase/snake_case 双认读 JSON；`PluginInfo/MarketItem/UiContribution` 模型（字段表见源码）。纯模型层可直接搬。
- **PluginUi**（快照单例）：`plugins_ui` + `plugins_list` 合并并发刷新（_inflight/_again），diff 后发 `Changed/PluginsChanged`；`OfKind/Find/FindPage/PluginName`。UI 线程事件派发在 Avalonia 下改为 `Dispatcher.UIThread.Post`。
- **UiContribution.Kind 分发**（插件界面贡献五类）：
  | kind | 落点 | Windows 实现 |
  |---|---|---|
  | page | 侧栏/顶部页签页面 `plugin:<plugin>:<id>` | PluginHostPage + MainWindow.RebuildPluginTabs（group "top" 追加为顶部页签，Order 排序） |
  | dashCard | 首页卡片 `plugin:<plugin>:<id>` | PluginDashCards（外壳缓存 + DashHost 集成） |
  | aiView | AI 对话视图条 | AiViewStrip（455 行） |
  | action | 动作按钮条 where∈instance/launch/ai.input/tray | PluginActionBar（WrapPanel，点按 plugins_action） |
  | overlay | 主窗口浮层 | PluginOverlays（无边框子窗口 + WebView2 透明 + 点击穿透，**Win32/DWM 强依赖，Linux 版 M2 不移植，标 M3/砍**） |
- **NativePluginUi**：反射找 `TipCL.Plugins.PluginUiRegistry`（.NET 插件原生界面），找不到退 WebView。Avalonia 版 WebView 候选为 **NativeAot 不友好的 CefGlue / Avalonia 官方未内置**——M2 建议：先落 UI 骨架与 RPC 卡片交互，WebView 承载面（page/aiView/overlay）用「占位空态 + 在浏览器打开」降级（源码已有等价文案），WebView 选型报 DECISIONS。
- **PluginEvents**（EventBus 转发，272 行）：`plugins.ui_changed/plugins.changed → PluginUi.RefreshAsync`；`plugins.confirm`（权限确认弹窗，回 plugins_confirm）；`plugins.ui_request`（toast/导航/浮层更新等）；`plugins.error`（Toast）。
- **PluginIcon**：Fluent 字形（≤2 字符）/data URL base64（WebpFallback 转 PNG）/http 图（Thumbs 缓存）。Avalonia 用 Bitmap.DecodeToWidth 替换 BitmapImage。

---

## 4. Core 依赖与续搬清单（对照 M1-A4 复用清单）

A4 已搬 Launcher 基础（下载/安装/Java/实例/账号/JSON），**未搬且 M2 页面直接引用的**：

**必须续搬（M2 前置，纯跨平台）**：
| Windows 源 | 用途 | 引用页 |
|---|---|---|
| Core/Launcher/Ai/FileMemory.cs | 记忆卡读改删 | SettingsPage.BuildMemoryCard |
| Core/Launcher/Sync/{PhoneClient,PhoneLink,SaveSync,LevelDat}.cs | 手机同步卡 | SettingsPage.PhoneSync.cs |
| Core/Plugins/PluginMarket.cs | 加速镜像模型 + 默认镜像 | PluginSettingsCard |
| Core/Plugins/{PluginManifest,PluginDirs,SemVer,PluginStateStore}.cs | 插件清单/目录/版本比较/状态存储 | 插件页与后续宿主 |
| Core/Plugins/PluginManager.cs（按依赖裁剪） | 插件安装/卸载/启停落盘 | 插件页（若 M2 走 UI+桩后端，可只搬 Manifest/Dirs） |

**按需续搬（设置页相关 AI 运行时，M2 可桩）**：`Core/Launcher/Ai/McpServer.cs`（MCP 开关卡）、`Ai/FileMemory` 之外的 Ai/*（测试连接、拉模型走 RPC，不直接依赖）。

**M2 不搬（标 M3/砍）**：`Core/Plugins/` 宿主运行时（NodeHostProcess/JsonRpcPeer/PluginHostApi/DotNetPluginContext/PluginAgentBridge/PluginTools/PluginLlm/PluginSessions/SecretStore(DPAPI)/Sdk/*）——插件真正运行依赖 M3；`PluginOverlays` 对应的 Win32 浮层。

**Services 层 M2 需要的（A2 只搬了 Ui 门面）**：I18n.L、Dlg（对话框：Confirm/Ask/Form/Prompt/Panel/Busy）、Toast、SPanel（Avalonia 重写为附加属性或布局面板）、Motion（Stagger/Shake/Shadow/Progress，可先空实现）、Thumbs（图片缓存）、TaskStore/TaskRow（任务进度）、Fmt/Clamp/FileKinds、BridgeHost.FindRoot（→Linux 数据目录）、WebViewHost（降级为不可用态）。Windows 版这些在 `src/TipCL/Services/`，M2 按此清单补齐 App 层门面。

---

## 5. 主题键对照（A2 已就位，无需待映射）

Linux `src/TipCL.App/Themes/Palette.*.axaml` 共 61 键，本规格涉及键**全部存在**：C.Accent/C.AccentStrong/C.AccentText/C.AccentDeep/C.AccentLite/C.Canvas/C.Paper；B.Accent 系（Accent/AccentStrong/OnAccentStrong/AccentText/AccentTint/AccentDeep/AccentLite/AccentSoft/AccentSoft2/AccentBorder/OnAccent）、B.Paper/B.Paper2/B.Line/B.LineSoft/B.SegRail/B.Ok/B.OnOk/B.Danger(+Soft)/B.Warn(+Soft)/B.Info(+Soft)/B.Ink/B.InkSoft/B.InkMuted/B.InkFaint/B.Glass/GlassStrong/GlassBorder/B.BannerFill/B.OnBanner/B.LogoFill/B.OnLogo/B.PageWash/B.Canvas/B.Card/B.CardBorder/B.Hover/B.RowLine/B.RowHover/B.Mask/B.Scroll(+Hover)。
未在 Linux 版出现、Windows 版用到需补的：**无**（设置页 Rows 分隔线用 B.SegRail ✓、编辑蒙版用 B.Accent ✓、托盘 B.Paper2 ✓）。样式类名（T.PageTitle/T.GroupTitle/Seg.Item/Seg.Rail/Tab.Top 等）归 A2/A3 样式库，M2 落页面时按 ThemeGalleryPage 现有命名对齐。`progress/M1-A2.md` 未产出，键名冲突裁决仍按 GOAL §5-A3 规则由主 agent 处理。

---

## 6. Avalonia 拖拽重排实现要点（WPF → Avalonia 差异）

1. **指针 API 换代**：WPF 的 `CaptureMouse/MouseMove/PreviewMouseLeftButtonDown` → Avalonia `e.Pointer.Capture(control)` + `PointerPressed/PointerMoved/PointerReleased/CaptureLost`（InputElement 路由事件）。BeginDrag 挂在 `PointerPressed`（`e.GetCurrentPoint(...).Properties.IsLeftButtonPressed`），EndDrag 同时挂 `PointerReleased` 与 `PointerReleased` 失败兜底 `PointerCaptureLost`——对应 WPF 的 LostMouseCapture。
2. **坐标**：`e.GetPosition(_list)` → `e.GetPosition(_list)`（Avalonia 同名，取相对视觉位置）；跨 DPI 用 `VisualRoot.RenderScaling` 换算（BlockHost 全程在同一控件坐标系内，无需手动换算）。
3. **中点交换算法**（缓存高度 + 逐格比较）与 UI 无关，**原样照搬**；`ActualHeight` → `Bounds.Height`。
4. **文件拖放**（插件页 zip 安装）：WPF `AllowDrop+PreviewDragOver/Drop+DataFormats.FileDrop` → Avalonia `DragDrop.SetAllowDrop(this,true)` + `DragDrop.DragOver`（设 `e.DragEffects = Copy`）+ `DragDrop.Drop`（`e.Data.GetFiles()` 返回 `IEnumerable<IStorageItem>`，取 `.Path.LocalPath`）。文件路径语义与 WPF 等价。
5. **右键菜单**：WPF ContextMenu 属性继承「最近优先」语义在 Avalonia 没有；BlockHost 的「自定义布局…」入口建议改为 `PointerReleased`（RightButton）+ `ContextMenu` 附加在 BlockHost 根 Border 上，并文档化与页面自带菜单的冲突处理（见 §1.2）。
6. **Cursor.SizeAll** → `Cursor.Parse("SizeAll")`（Avalonia 标准光标名 sizeAll）。
7. 动画插值不需要（Windows 版明确不做，交换即落位，Motion.Enabled=false 行为一致）。

---

## 7. 工作量估计与建议拆包

| 包 | 内容 | 预估 |
|---|---|---|
| M2-R3a | 布局框架模型层（PageBlock/PageLayoutStore/PageLayoutIO 模型 + 15 个契约测试，进 Core/Tests） | 0.5 天 |
| M2-R3b | BlockHost Avalonia 版 + 设置页通用视图接入（首个 BlockHost 消费页） | 1.5–2 天 |
| M2-R3c | 设置页全量（7 导航 + 9 区块卡片 + 自动保存 + 主题派生 ThemeService + 秘密行 + 子页挂接） | 3–4 天 |
| M2-R3d | 插件页（骨架/三分段/卡片/市场/设置卡/开发者 + J/PluginUi/PluginEvents 模型层） | 2.5–3 天 |
| M2-R3e | Core 续搬（§4 必搬清单）+ Services 门面补齐（Dlg/Toast/Thumbs/TaskStore 等） | 1–1.5 天 |

依赖序：R3a → (R3b, R3e) → (R3c ∥ R3d)。建议并行拆法：R3a+R3b+R3e 一人（框架线），R3c 一人，R3d 一人；三线共享 A2 主题键与 UiHelpers 门面，接口先冻结 BlockSpec/BlockHost/RPC 表（§2.4/§3.2）。

**明确降级项（M2 报备 DECISIONS）**：WebView2 承载面（page/aiView）用占位 + 浏览器打开降级；PluginOverlays 浮层不移植；插件宿主运行时（Node/.NET）不落地，插件页对着桩后端可跑 UI 流程；MCP Server 卡与 AI 测试连接走桩返回。
