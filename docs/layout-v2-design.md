# TipCL 高度自定义布局系统 —— 设计方案 v1(待确认)

> 状态:**设计草案,未写一行业务代码**。目标树 = `windos/tipcl`(WPF / net48 主线,HEAD `803dd38`,工作树脏:3 改 + 1 未跟踪)。
> 需求来源:用户 2026-11-14 明确"给这个项目增加自定义程度更高的自定义布局",范围 A–G 全要、**全部页面统一**、硬约束 = **布局 JSON 必须向后兼容(不丢用户已有布局)**。
> 本文件只讲设计与契约;每一条现状结论都带 `文件:行号` 证据,标注「推断」的才是推断。

---

## 0. 先看这个:需要你拍板的 7 件事

| # | 问题 | 我的建议 | 为什么要你定 |
|---|---|---|---|
| Q1 | **"侧栏自定义"对象不存在了** —— 侧栏已从可视树删除(`MainWindow.xaml.cs:105-106` 注释),今天导航 = 顶栏 5 个写死页签;`UiPrefs.SideWidth` / `SideMinWidth/MaxWidth` / `ui_sidebar_width` / `SideFrost` 全是**孤儿配置**(无人读,无人写到可视元素)。D 项该怎么做? | **(c) 顶栏页签自定义 + 新增"侧栏模式"二选一可切换**:NavModel/NavConfig/分组表都还在,复活侧栏比新造便宜,且你要的"左右位置/宽度/仅图标/拖拽重排/自定义分组"只有侧栏形态才成立 | 影响 D 的整个形态与工作量(中 vs 大) |
| Q2 | **自由画布用哪种几何?** 普通页区块高度由内容决定(`SPanel` 用 `PositiveInfinity` 测量,`Services/SPanel.cs:13-47`;`BlockCard` 无 MinHeight) | **网格画布(grid:12 列 × 自动行,拖拽定位 + 跨列/跨行缩放)** 作为默认;`canvas`(像素自由)只对**声明了固定高度**的页/块开放 | 决定 A 的交互与"块高不定"这个硬障碍怎么解 |
| Q3 | **compact 体积红线现在是破的**:实测 `6,238,720 B > 5,757,264 B`(D-19 红线,`DECISIONS.md:140-144`),超 481,456 B,且**不是本功能造成的**(`DECISIONS.md:184` 自认镜像+词表导致)。D-11(布局)有先例:用户当年明确"本功能不受 compact 体积红线约束"(`DECISIONS.md:146-152`) | **沿用 D-11 先例:布局功能豁免体积红线**,收尾只如实记录实测字节;体积回收(ImageSharp/CodePages ≈1.15 MB,`docs/size-report.md:52-61`)另立包 | `tools/build-both.ps1:90-97` 超线直接 `exit 1`,不豁免就**无法构建交付** |
| Q4 | **允许改 Core 吗?** 整机方案落盘、AI 改布局、插件区块口子都必须动 `src/TipCL/Core/` | **允许**,但严格限定:只加新文件/新 RPC 分部/白名单项,**不改既有 Core 行为** | `GOAL.md:58-59` 原文禁止动 Core,但该条已被后续轮次实际放宽(R4 报告 §5.1) |
| Q5 | **F 自建组件的安全边界**:`web`(WebView2 内嵌网页)与 `button`(执行动作)是危险类型 | **默认不进候选列表**:自建区块首期只放 `note / shortcut / clock / divider / playtime`;`web` 与 `button` 需在设置里显式开启,`button` 只允许**内部动作白名单**(切页/启动指定实例/打开文件夹),不允许任意命令 | 属于安全边界,必须你点头 |
| Q6 | **窄窗/宽窗两套布局(响应式断点)现在做还是二期?** | **二期**:首期文档里预留 `variants`/`min_width` 字段但不实现切换 UI,避免首期过大 | 影响 G 的范围 |
| Q7 | **落地命名**:新文件 `<数据目录>/ui_layout_v2.json`(kind=`tipcl-layout`,version=2),设计文档留在 `docs/layout-v2-design.md` | 建议就这么定 | 名字一旦落地后面难改 |

## 0.1 决策记录(2026-11-14 你已拍板;§4/§5/§6 中与此冲突的表述以本节为准)

| 编号 | 你的决策 | 对设计的影响 |
|---|---|---|
| **Q1 = (b)** | **只自定义顶栏页签,不复活侧栏** | D 收敛为:页签顺序/显隐 + 标题栏元素开关(§4.D 的侧栏各项**已否决**);`ui_nav_*` / `ui_sidebar_width` / `UiPrefs.SideWidth` / `SideFrost` 等孤儿键**保留但不接线**(不改不删,留作未来);§5.2 里 `NavModel.cs`/`NavConfig.cs`/`UiPrefs.cs` 一行改为**不动**;§6 的 L7 随之缩小 |
| **Q2** | **grid 与 canvas 两种都实现**,逐页切换 | §4.A 两个模式都是交付项(不是二选一);L2 工期按两者计;`canvas` 只对声明了 `row_h` 或固定高的块开放,避免可变高块被裁切 |
| **Q3** | **沿用 D-11 先例:布局功能不受 compact 体积红线约束** | 收尾只如实记录实测字节;体积回收(ImageSharp/CodePages ≈1.15 MB)不在本功能范围 |
| **Q4** | **授权改 Core,限定纯新增** | 允许:新文件、新 RPC 分部、3 处插件 kind 白名单、AI 工具注册;**不允许改既有 Core 行为**;老画布链路(`UiLayout.cs`/`LayoutDoc.cs`/`LayoutItem.cs`/`DashHost.cs`)继续不动 |
| **Q5 已定** | 自建组件的 `web`/`button` **默认关闭** | 首期候选只有 `note`/`shortcut`/`clock`/`divider`/`playtime`;`web`(限 https,禁 `file://`)与 `button`(只允许内部动作白名单:切页/启动指定实例/打开目录)需在设置里显式开启并二次确认 |
| **Q6 已定** | 窄窗/宽窗两套布局**首期就做** | `variants` 不是预留字段而是交付项:窗口宽度跨过阈值时切换 `narrow` 变体;编辑态提供"编辑哪一套"的切换;工期并入 L2 |
| 默认 **Q7** | 新文件 `ui_layout_v2.json`(kind=`tipcl-layout`,version=2);设计文档 `docs/layout-v2-design.md` | 已按此写作 |

---

## 1. 目标与非目标

### 1.1 目标(对应你选的 A–G)

| 项 | 目标一句话 |
|---|---|
| **A** | 页面区块从"只能上下排"升级为**可自由摆放**:三模式 `stack`(现状)/ `grid`(多列网格,可跨列跨行)/ `canvas`(像素自由,限固定高块) |
| **B** | 每块可调**外观**:跨列数、密度、卡片/无边框/分隔线、自定义边距间距、常态是否显示块标题 |
| **C** | **整机布局方案**:侧栏/页签 + 各页区块 + 分栏比例 + 仪表盘 + 外观参数打包成命名方案,一键切换/导入/导出/分享;设置页新增「布局」管理界面 |
| **D** | **外壳与导航自定义**:页签顺序/显隐 + (若 Q1 选 c)侧栏模式、左右位置、宽度拖拽记忆、仅图标模式、拖拽重排与自定义分组改名 |
| **E** | **分栏比例持久化**:13 处写死的分栏全部可拖 + 记忆 + 双击复位 + 进布局文档 |
| **F** | **自建区块/组件**:快捷启动某版本、自定义按钮、便签、网页、时钟等,可放到任意页;并给插件开同一口子 |
| **G** | **编辑体验**:对齐辅助线、吸附、多选、撤销/重做、复制粘贴布局、JSON 实时预览(响应式断点见 Q6) |

### 1.2 非目标(本期不做)

- 不改 net48 / 不加 NuGet 依赖 / 不破坏单 exe(Costura)(`GOAL.md:47-49`)。
- 不改版本号、不发 release、不 push(`交接必读.md:20`)。
- **不复活旧卡片画布 `DashHost` 作为运行时**(它是死代码,`Pages/LaunchPage.cs:21`);只把它的吸附/手柄数学**移植进新引擎**。
- 不碰 mac/linux 树;**但保持 v1 文件语义冻结**,让 mac/linux 端口继续照读(见 §3.3)。
- 不做像素级截图回归基建(仓库现在没有,`R2 报告 §5.2`)——本期仍靠 `--shot` 人工对照。

---

## 2. 现状基线(事实,带证据)

### 2.1 今天真正生效的布局体系只有一套

| 项 | 事实 | 证据 |
|---|---|---|
| 唯一可视入口 | `BlockHost` 右键「自定义布局...」→ 一维纵向拖拽重排 + 显隐 + 底部托盘恢复 + 工具条(完成/重置本页/导出/导入) | `Pages/BlockHost.cs:25-32,93-114,290-313,403-523` |
| 落盘 | `<数据目录>/ui_page_layouts.json` = `{"version":1,"kind":"tipcl-page-layout","pages":{pageId:{version:1,blocks:[{id,hidden}]}}}`,写盘原子(tmp + `File.Replace`) | `Pages/PageLayoutStore.cs:11-19,114-135,238-255` |
| 单页模型 | **只有 `id` + `hidden` 两个字段** | `Pages/PageBlock.cs:17-26,105-116` |
| 覆盖范围 | **12 个宿主 / 10 个页面类 / 13 个 pageId**;其中 `settings:network`、`settings:about` 只有 1 个块 → **因 `specs.Count >= 2` 门槛连右键入口都没有** | `Pages/BlockHost.cs:122-136`;`Pages/SettingsPage.cs:732-744` |
| 特殊页 | 实例库不走 BlockHost,自管双栏 + Store 直写,只能整栏收起文件夹栏 | `Pages/LibraryPage.cs:19,205,250-252,268-321` |

### 2.2 三套"看起来能自定义、实际不可达"的残留

| 残留 | 状态 | 证据 |
|---|---|---|
| 启动页卡片画布(`DashHost`/`DashLayout`/`DashCard`/`LayoutState`) | 构造于 `LaunchPage` 但**从不加入可视树**;`EditMode` 全仓 0 处赋值;`Changed` **0 个订阅者**;`MinSizes` 无人填充;`AddCard` 无调用者 | `Pages/LaunchPage.cs:21,81,1899`;`Pages/DashHost.cs:305,393-431`;`Pages/LayoutState.cs:7-63` |
| 老画布契约与方案化 | `config.json` 的 `ui_layout`/`ui_layouts`/`ui_layout_profile` + **7 个 RPC**(get/save/save_profile/activate/delete/reset/import_layout)**在 WPF 侧 0 调用方** | `Core/BackendApi.cs:3351-3420`;`Core/Launcher/Config.cs:147-149` |
| 侧栏与导航配置 | 侧栏不在可视树;`ui_nav_style/order/pinned/hidden/groups` 仅被解析 + 只影响 Ctrl+1..9 顺序;`ui_sidebar_width`(后端钳 140..320)、`UiPrefs.SideWidth`、`SideMinWidth/MaxWidth`、`SideFrost` 全部无消费者;孤儿文案 `"侧栏排法"` 无代码引用 | `MainWindow.xaml.cs:99-106,1488-1529`;`UiPrefs.cs:7,21-23`;`Core/Launcher/NavModel.cs:12-22` |

### 2.3 分栏:13 处,0 个可拖分隔条,0 处记忆

`grep GridSplitter|Splitter` 于 `src/` → **零命中**。全部写死:

| # | 位置 | 写死值 | 证据 |
|---|---|---|---|
| 1 | 实例库左栏 | `"*,18,2*"` + 左卡 300..380 | `LibraryPage.cs:250-252,156-157` |
| 2 | 设置页左导航 | `"168,*"` | `SettingsPage.cs:747` |
| 3 | 实例详情分段 | `"Auto,Auto,*"` | `InstanceDetailPage.cs:129` |
| 4 | 启动页英雄区/内容区 | 英雄区 MaxWidth 680 + 内容区 MaxHeight=页高 62% | `LaunchPage.cs:441-445,483-489` |
| 5 | 下载页原版段 | 行内 `"130,*,110,Auto"` | `DownloadPage.cs:180` |
| 6 | 版本管理页 | `"272,18,*"`(内嵌态列宽置 0) | `VersionManagePage.cs:175,206-208` |
| 7 | AI 页历史栏 | 固定 `Width=220` | `AiPage.cs:332` |
| 8 | 反馈页 | `"*,360"` | `FeedbackPage.cs:66` |
| 9 | 账号页头像卡 | `Width=240` | `AccountPage.cs:75` |
| 10 | 插件页已安装网格 | `UniformGrid Columns=3` | `PluginsPage.cs:91-95` |
| 11 | 设置页「反馈与关于」 | `"Auto,*"` | `SettingsPage.cs:738` |
| 12 | 下载页其它 6 段 | CatalogPage 单列 26 边距 | `CatalogPage.cs:279-284` |
| 13 | 安装页 | `"Auto,*,Auto"` | `InstallPage.cs:204-210` |

### 2.4 今天能/不能自定义(逐条)

**能**:区块顺序(一维)、区块显隐、单页重置、全量导出/导入、实例库文件夹栏显隐、老画布(RPC 独有:x/y/w/h + grid{0,4,8,16,24} + z + hidden + settings + 方案增删)。
**不能**:区块位置/尺寸、多列/跨列、区块外观、块级最小/最大高度、新增块类型、块级 z 手调、分栏比例、加入/移除卡片入口、布局变更的跨视图通知(布局保存 0 处 `Emit`)、撤销/重做、对齐辅助线、侧栏/标题栏自定义。

### 2.5 工程事实与红线

| 项 | 事实 | 证据 |
|---|---|---|
| 构建 | `dotnet build TipCL.sln -c Release` **本机实测 0 错误 0 警告**;net48+WPF 靠 `EnableWindowsTargeting` + `Microsoft.NETFramework.ReferenceAssemblies`,不需要 VS/msbuild | 本轮 Lead 实测;`TipCL.csproj:15,53` |
| 测试 | **实测基线 = 176/176 全绿**(排除 `PhoneLinkTests`,1 秒);文档旧口径 157/158,唯一既有失败 `PhoneLinkTests.FullFlow`(**不修**);**本环境两处坑**:①受限沙箱里 `dotnet test` 会挂起(vstest testhost 通道被限制);②`%TEMP%` 受进程级限制,直接跑会让 **40 个用例**报 `UnauthorizedAccessException`(建临时目录失败)→ **把 `TEMP/TMP` 指到工作区内即全绿**。根因**不是 Windows 文件权限**(Temp 的权限项完好、无外来包权限项,诊断技能判定 `NOT_THIS_CLASS`,报告 `_acl_diag/acl-report-ae880372bf8346f7829085acd5374569.jsonl`) | 本轮实测;`DECISIONS.md:183,32-38` |
| 体积 | compact 实测 **6,238,720 B > 5,757,264**(超 481,456);premium/Immersive 开关**已实现** | 本轮实测;`DECISIONS.md:140-144,184`;`TipCL.csproj:27,29` |
| 默认布局约束 | "默认布局逐像素等价于现状"是 **D-11 的口头约束**,**没有任何自动化像素断言**;被钉住的是默认卡片类型/坐标一致性 + `Normalize`/`FromJson` 语义 | `DECISIONS.md:149`;`tests/TipCL.Tests/LayoutDefaultTests.cs:7-35`;`PageLayoutTests.cs:17-133,149-249`;`NavCompatTests.cs:12-77` |
| 冒烟 | `--smoke` 会点按钮,但 `NeverClick` 含「重置/导出/撤销/移除/删除/保存设置」→ **编辑态新按钮不会被自动点到** | `Smoke.cs:60-72,565-581` |
| 截图 | `--shot <pageId> --out x.png --theme light|dark`,人工对照;仓库无 PNG hash 回归 | `DevShot.cs:17-68` |
| i18n | 新增中文串必须 `L()` 包裹且 key 同时进 `en.json`/`zh_CN.json`;`--i18n-check` 现状已必然 exit 1(2897 处历史未包裹,D-23 记录不修) | `I18nCheck.cs:99,186-194`;`DECISIONS.md:173-178` |
| 插件口子 | UI 贡献 kind 仅 6 种,白名单三处一致;**没有任何"区块"级贡献点**;`dashCard` 只进已死的卡片画布 | `Core/Plugins/PluginManifest.cs:77`;`PluginHostApi.cs:232`;`PluginManager.cs:1259` |
| AI 口子 | AI **零布局能力**;工具表 38+11 项无 layout/ui;唯一间接路径 `fs_write`(白名单根含 `Utils.ROOT`)不可靠(进程内 `_cache` 察觉不到外部改文件) | `Core/Launcher/Ai/Tools.cs:18-43,83`;`ExtraTools.cs:16-20,509-524`;`PageLayoutStore.cs:197-206` |

---

## 3. 设计总纲:一套引擎 + 两份文档 + 三种模式

### 3.1 六条核心决定

| # | 决定 | 理由(证据) |
|---|---|---|
| **D1** | **v1 文件冻结为"兼容子集"**:`ui_page_layouts.json` 继续**原样写**(只有 `version/kind/pages[id].{version,blocks[{id,hidden}]}`),永不写新字段 | 老程序对单页 `blocks` 的**任何一次保存**都会用 `ToDict()` 重写,新字段必丢(`PageBlock.cs:105-116` + `PageLayoutStore.cs:94-112,238-255`);而只要 v1 字段语义一个字不改,**老程序永远读得懂**,mac/linux 端口也零破坏 |
| **D2** | **新格式全部进新文件** `<数据目录>/ui_layout_v2.json`(`kind=tipcl-layout`,`version=2`) | 老程序**不读不写不认**任何新文件名(全树只拼固定文件名:`Config.cs:52`、`PageLayoutStore.cs:19`、`MainWindow.xaml.cs:141`),不会崩、不会误删;`config.json` 也可只加顶层新键(老程序保留未知顶层键,`LauncherConfig.cs:49-55`),但我们原则上**优先用新文件** |
| **D3** | **v2 为唯一真源,v1 为写通投影**:任何布局变更 → 写 v2 + 由 v2 重新生成 v1 子集 | 升级路径:首次运行若只有 v1 → 导入 v1 生成 v2(v1 原样保留作为安全网);这让"绝不丢已有布局"成为**可测试命题**(§3.3) |
| **D4** | 抽出 `LayoutEngine`,把 `BlockHost` 降级为引擎的 **stack 模式适配器**;`DashHost` 的吸附/手柄数学**移植**进引擎,不复活 DashHost 运行时 | 现状两套编辑态唯一共享的只有 `Gap=10`(`BlockHost.cs:34-35`);可复用 11 项 / 必须新写 9 项清单见 R2 报告 §3.1(吸附公式 `DashHost.cs:522-530`、8 向手柄 `:16-26,224-254`) |
| **D5** | **布局变更发事件**:新增 `LayoutChanged`,BlockHost/引擎挂上;布局保存同时触发 `ui_changed`(复用既有 EventBus→SSE 链路) | 今天布局保存 0 处 `Emit`,`_cache` 以目录为键进程内只读一次、无 FileSystemWatcher(`PageLayoutStore.cs:197-206`)→ 跨视图/跨进程刷新今天根本不存在 |
| **D6** | **块的新字段一律走 v2**;v1 只保留 `id/hidden` 且顺序不变 | 兼容性(见 D1/D2) |

### 3.2 数据契约 v2(逐字段)

**文件**:`<数据目录>/ui_layout_v2.json`(数据目录 = `BridgeHost.FindRoot()`,与 `ui_page_layouts.json` 同目录)
**信封**:

```json
{
  "version": 2,
  "kind": "tipcl-layout",
  "saved_at": "2026-11-14T09:10:00+08:00",
  "app": "1.1.0",
  "active_profile": "",
  "layout": { /* LAYOUT 对象,见下 */ },
  "profiles": { "我的布局": { /* LAYOUT 对象 */ } }
}
```

**LAYOUT 对象**:

| 字段 | 类型 | 默认 | 取值/钳位 | 说明 |
|---|---|---|---|---|
| `shell` | object | `{}` | 见下表 | 外壳:页签/侧栏/标题栏 |
| `pages` | object | `{}` | key = pageId(13 个既有 + 未来新增) | 每页一块布局,见下 |
| `splits` | object | `{}` | `pageId → { splitId → ratio(0..1,夹 0.15..0.85) }` | 分栏比例(E) |
| `widgets` | array | `[]` | 见下 | 自建区块定义(F) |
| `variants` | object | `{}` | `narrow → LAYOUT 子集` | 响应式(二期用,首期只写不读) |
| `meta` | object | `{}` | 自由 | 备注/来源,仅供分享识别 |

**`shell`**:

| 字段 | 类型 | 默认 | 钳位 | 说明 |
|---|---|---|---|---|
| `nav_mode` | string | `"tabs"` | `tabs` \| `sidebar` | 顶栏页签 / 左侧栏(Q1 决定是否启用) |
| `nav_side` | string | `"left"` | `left` \| `right` | 侧栏位置 |
| `nav_width` | number | `196` | 夹 `140..320`(沿用现有后端钳位) | 侧栏宽度,可拖拽记忆 |
| `nav_icon_only` | bool | `false` | — | 仅图标模式 |
| `nav_style` | string | `"grouped"` | `compact` \| `grouped`(沿用 `NavModel.cs:12-22`) | 侧栏分组风格 |
| `nav_order` | string[] | 见 `NavModel.DefaultNavOrder` | 只认合法 key,未知丢弃 | 顺序 |
| `nav_hidden` | string[] | `[]` | — | 隐藏项 |
| `nav_groups` | array | 见 `NavModel.GroupedNav` | `[{title, keys[]}]`,title 允许用户改名 | 自定义分组 |
| `tabs` | string[] | `["launch","library","download","plugins","settings"]` | 子集,空则回落默认 | 顶栏页签顺序/显隐 |
| `titlebar` | object | `{"bridge":true,"ai":true,"theme":true,"account":true}` | 逐项 bool | 标题栏元素开关 |
| `density` | string | `"standard"` | `compact` \| `standard` \| `loose` | 全局密度(↓ 影响 4/6/8/10/12/16 间距档) |

**`pages[pageId]`**:

| 字段 | 类型 | 默认 | 钳位 | 说明 |
|---|---|---|---|---|
| `version` | int | `2` | 必须 == 2,否则该页回默认(与 v1 同思路) | 单页版本门禁 |
| `mode` | string | `"stack"` | `stack` \| `grid` \| `canvas` | 页面布局模式 |
| `cols` | int | `12` | `{2,3,4,6,12}` | grid 模式列数 |
| `row_h` | number | `0` | `0`=按内容自适应;`>0`=固定行高 px | grid/canvas 行高 |
| `gap` | number | `10` | `0..32` | 块间距(现状硬编码 `BlockHost.cs:34-35`) |
| `pad` | number[4] | `[18,14,18,16]` | 0..64 | 内容边距(现状 `PageBase.cs:146-151`) |
| `blocks` | array | — | 顺序即显示顺序,**未知 id 由页面白名单过滤** | 见下 |

**`pages[pageId].blocks[i]`**(v1 的 `{id,hidden}` 是它的子集):

| 字段 | 类型 | 默认 | 钳位 | v1 兼容 | 说明 |
|---|---|---|---|---|---|
| `id` | string | — | 非空,去重 | ✅ 同名同义 | 块 id(现有 13 页的 id 清单见 R3 报告 §4.3) |
| `hidden` | bool | `false` | 只认 JSON `true` | ✅ 同名同义 | 隐藏 |
| `x`,`y` | int | `0` | grid: `0..cols-1` / `canvas`: 归一化 0..1 | — | 位置(仅 grid/canvas) |
| `w`,`h` | int/number | `1` | grid: `1..cols` / `canvas`: 0.04..1 | — | 尺寸 |
| `span` | int | `1` | `1..cols` | — | stack 模式下"占几列"(多列栈模式) |
| `style` | string | `"plain"` | `plain`(现状零装饰) \| `card` \| `divider` | — | 外观(B) |
| `title` | bool | `false` | — | — | 常态是否显示块标题(`BlockSpec.Title` 已存在) |
| `pad` | number[4]? | `null` | 0..64 | — | 每块边距覆盖 |
| `min_h` | number | `0` | ≥0 | — | 块最小高度(拖拽缩放下限;`0`=用类型默认) |
| `z` | int | 顺序索引 | 0..n-1 | — | 叠放序(canvas 模式用) |
| `settings` | object | `{}` | 自由 | — | 每块自由参数(自建区块/插件块用;沿用 `DashCard.Settings` 既有做法 `DashCard.cs:25,58-61`) |

**`widgets[i]`(自建区块定义,F)**:

| 字段 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `id` | string | 自动 `w-<时间戳>` | 引用 id 形如 `custom:<id>`(与插件块 `plugin:<plugin>:<id>` 区分命名空间) |
| `type` | string | — | 首期白名单:`note` \| `shortcut` \| `clock` \| `divider` \| `playtime`;Q5 通过后追加 `web` \| `button` |
| `title` | string | — | 块标题(用户可改) |
| `settings` | object | `{}` | 各类型自有参数,例如 `shortcut:{instance:"1.20.1-Fabric"}`、`note:{text:"..."}`、`clock:{format:"HH:mm"}` |

**兼容命名空间一览**(避免与既有约定撞车):

| 前缀 | 归属 | 既有证据 |
|---|---|---|
| `plugin:<plugin>:<cardId>` | 老画布插件卡(**保持不动**) | `PluginDashCards.cs:29-32`、`UiLayout.cs:30-38` |
| `plugin:<plugin>:<blockId>` | **新增**插件区块(Q7/§4.F) | 需新增 kind `block`,见 §5 |
| `custom:<widgetId>` | 用户自建区块 | 本设计新增 |

### 3.3 兼容与迁移矩阵(核心,必须逐条可测)

| 场景 | 期望行为 | 机制 | 可测断言 |
|---|---|---|---|
| 只有 v1,没有 v2(升级首跑) | v2 由 v1 **导入**生成:每个 v1 page 的 `blocks` 顺序与 `hidden` **逐项保留**;新增字段填默认(`mode=stack`、`style=plain`、`title=false`…) | `LayoutMigrator.FromV1(v1Doc)` | 迁移后 `v2.pages[id].blocks[i] == v1.pages[id].blocks[i]` 的 `{id,hidden}`,且顺序全等 |
| v1 与 v2 都在 | **v2 优先**,不再导入;仅在 v1 更新鲜(外部工具改过)时提示"检测到旧格式更新,是否并入" | 比较 mtime + v1 子集是否 ⊂ v2 | — |
| 用户在 v2 里改任何东西 | 立刻写 v2 → **由 v2 重建 v1 子集并原子写 v1** | `LayoutStoreV2.Save()` → `WriteThroughV1()` | 任何一次 v2 保存后,用**冻结的 v1 解析器**(`PageLayoutStore.Load`)读回,`{id,hidden}` 序列 + 顺序与 v2 投影**全等** |
| **旧版本程序(本仓库现成 exe `windos/TipCL.exe`)在改动后运行** | 仍能读到正确的顺序/显隐;不会崩;不会抹掉 v2 | v1 语义与结构未变;v2 文件名它不认识 | 降级回归:把新旧 exe 指向同一数据目录,新 exe 改布局 → 旧 exe 启动 → `PageLayoutStore.Load` 结果与 v2 投影一致 |
| v2 文件损坏/半截 | **回落到 v1 子集**(再不行回默认),不崩 UI,不静默清空 | 读失败 → null → v1 / 默认;写失败吞掉但内存保留最新(照抄 `PageLayoutStore.cs:107-110,231-234` 范式) | 截断/非 JSON 用例 |
| 分享/导入一份整机方案 | 校验 `kind`+`version`,逐字段白名单过滤 + 钳位;未知字段**保留但不生效**;支持"仅导入某几页/仅外观" | `LayoutSchema.Sanitize()` | 非法 kind/version → 抛中文错误并 Toast(照抄 `PageLayoutIO.cs:64-68`) |
| 用户从新版降级到旧版后又回新版 | v1 被旧版重写(仍是合法 v1)→ 新版以 v2 为准,v2 里的新字段**不丢** | v2 从未被旧版写过 | 用例:模拟"旧版写回 v1"后再读 v2,新字段仍在 |
| mac/linux 端口 | 继续读 v1,行为完全不变 | v1 冻结 | 端口侧无需改动 |

> **一句话**:向后兼容不是靠"小心地改旧文件",而是靠**旧文件永不承载新数据**。这条让"绝不丢用户已有布局"从口号变成 §8 里几条可执行的断言。

### 3.4 引擎与宿主的职责划分

```
LayoutEngine(纯几何,PagedLayout)
├─ StackMode   : 一维流(现状语义:顺序 + 显隐 + gap)                ← BlockHost 现有行为 1:1
├─ GridMode    : cols × 自动行,块可跨列/跨行;拖拽改 (x,y)、缩放改 (w,h)
└─ CanvasMode  : 归一化像素矩形(仅声明 row_h 或固定高块开放);吸附/手柄数学移植自 DashHost
LayoutEditor(交互层)
├─ 命中/拖拽/缩放/多选/对齐辅助线/吸附/撤销重做/托盘/工具条
└─ 全部改动只作用于内存中的 LayoutDocV2 快照 → 提交时 LayoutStoreV2.Save()
LayoutStoreV2(持久化)
├─ 原子写(优先复用 public 的 Utils.WriteJson:原子+重试+锁,Utils.cs:775-856)
├─ 迁移(FromV1)/ 校验(Sanitize)/ 写通 v1(WriteThroughV1)
└─ 事件 LayoutChanged
宿主
├─ BlockHost(pageId,specs,margin)   ← 公共契约不变,内部改为"创建 Engine + 选择模式"
└─ PageLayoutHost(page)             ← 新增:给双栏/分段页(库页、设置页、详情页…)提供 splits 接入
```

---

## 4. 能力设计(A–G)

### 4.A 页面区块升级为自由画布 / 多列网格

| 模式 | 交互 | 适用页 | 备注 |
|---|---|---|---|
| `stack`(默认) | 现状 + **`span` 跨列**:页面可切成 1/2/3 列,块声明占几列;拖拽仍按"相邻块中点"换序(保留 `BlockHost.cs:485-503` 算法) | 现有 12 个宿主全部零改动保持现状 | 默认值 = 现状,保证首屏逐像素不变 |
| `grid` | 12 列栅格 + 自动行:拖拽改 `(x,y)`,右下角手柄改 `(w,h)`,跨列跨行;编辑态显示栅格底纹(条件 = 编辑态 && `row_h>0`,对齐 `DashHost.cs:475-497`) | 5 个低成本的单列页(tasks/servers/playtime/java/multiplayer)+ 设置页 3 视图 | **块高不定用"行高自适应"解**:`row_h=0` 时行高 = 该行最高块的自适应高度 |
| `canvas` | 归一化像素矩形 + 8 向手柄 + 网格吸附(`grid` 档沿用 {0,4,8,16,24},`DashLayout.cs:13`) | 仅"声明了固定高"的块/页(启动页仪表盘若复活) | 迁移/缩放的数学直接移植 `DashHost.cs:224-254,270-280`(可复用,见 R2 §3.1) |

> **Q2 已定(2026-11-14)**:`grid` 与 `canvas` **两种都实现**,按页(`pages[].mode`)选择;`canvas` 只对声明了 `row_h` 或固定高的块开放。

**硬障碍与对策**(R2 §3.2 的 10 条,逐条对应):

| 障碍 | 对策 |
|---|---|
| 块高不定、布局前测不到 | `row_h=0` 的"自适应行":先按 `Infinity` 测量、再按行高最大值排布(两趟:`MeasureOverride` 记需求高,`ArrangeOverride` 用行高)。**禁止**给内容加速度未知的虚拟高度 |
| 内容可滚动 + 嵌套滚动抢手势 | 编辑态下由引擎接管指针(`PointerPressed` 阶段吞掉滚动),并在拖到边缘时**自动滚动**(现无,需新写);非编辑态滚动行为完全不变 |
| Canvas vs 自绘 Panel | 用**自绘 Panel**(相对坐标 0..1 或网格单位),不用 `Canvas`;外层虚拟高度由引擎算 |
| 每块最小尺寸无数据 | `blocks[i].min_h` + `BlockSpec` 新增可选 `MinW/MinH`;类型默认表沿用 `UiLayout.CARD_MIN_SIZE` 的规模感(200×120 兜底) |
| 含 WebView2 的块 | 编辑态必须临时隐藏 WebView(照抄既有做法 `PluginDashCards.cs:122-158`);文本/列表块冻结 `IsHitTestVisible`(`BlockHost.cs:628`) |
| 页面不是纯纵向流 | 6 个特殊页(启动页/实例库/版本管理/实例详情/下载页/设置页)走 §4.E 的 `PageLayoutHost` 或保持自管,**不强行塞进画布** |

### 4.B 区块级外观参数

| 参数 | 取值 | 落地方式(挂现有 token) |
|---|---|---|
| `style` | `plain`(现状零装饰)/ `card` / `divider` | `card` = 复用 `Themes/Controls.xaml` 的 `Card` 样式语义(`B.Card` + 1px `B.CardBorder` + 圆角 12);`divider` = `Ui.Sep()` 的 `B.LineSoft` 1px 上分隔线;`plain` = 现状(`BlockHost.cs:630` 厚度 0、无底) |
| `title` | 常态显示块标题 | 新增常态标题元素;文案沿用 `BlockSpec.Title`(已是 `I18n.L` 过的现成文案,`BlockHost.cs:18`) |
| `span` / `(w,h)` | 跨列/尺寸 | §4.A |
| `pad` | 每块边距 | 现状只有"整页容器边距"(`BlockHost` 构造第 3 参,`BlockHost.cs:68,80`),新增每块覆盖 |
| `density`(全局) | `compact/standard/loose` → 间距档 {6,10,14} | 现状间距**全是字面量、无资源键**(R2 §4.1);新增 `Thickness`/`Double` 资源键或引擎内常量表,**不要**散落新魔数 |
| 圆角/阴影 | 沿用 `CR.Card=12` / `Motion.Shadow` | 注意 `Controls.xaml:69` 的性能告警:长列表内卡片不用 `DropShadowEffect` |

**约定**:`style` 默认 `plain`,`density` 默认 `standard` → **默认渲染与现状等价**(D-11 的既有约束,`DECISIONS.md:149`)。

### 4.C 整机布局方案 + 设置页「布局」管理界面

- **方案 = 一份 LAYOUT 快照**(`ui_layout_v2.json` 的 `profiles[name]`),`active_profile==""` 表示"当前是直接编辑态"。
- 操作:新建(以当前为基线)/ 应用 / 重命名 / 删除 / 复制 / 导出(可选导出为**兼容 v1 的页面布局**)/ 导入(整机方案或 v1 页面布局)。
- **应用方案 = 原子替换**:`layout` ← 方案快照 → 写 v2 → 写通 v1 + 写通 nav 子集(`ui_nav_*`,老程序与 Ctrl+1..9 都靠它)→ 广播 `LayoutChanged` + `ui_changed`。
- 设置页新增第 8 个视图「布局」(`SettingsPage.cs` 的视图表,现有 7 个见 `SettingsPage.cs:745-751`):
  1. 外观与密度;2. 侧栏/页签(Q1);3. 页面区块(逐页"自定义布局..."入口 = 复用现有右键入口的等价按钮,顺带**修掉单块页没有入口**的问题,`BlockHost.cs:123`);4. 分栏比例(逐页复位);5. 自建区块管理;6. 方案(列表 + 导入导出);7. 高级:布局 JSON 编辑器 + 立即应用 + 校验错误提示(G)。
- 与既有「主题包」的关系:`SettingsPage.Themes.cs` 只管配色/背景(`ui_dark`/`ui_bg_preset`);**布局方案与主题包解耦**,但允许方案记录"推荐主题"字段(`meta.theme`,仅提示不强制)。

### 4.D 外壳与导航自定义(**Q1 已定 2026-11-14:只做顶栏页签,不复活侧栏**)

| 能力 | 落地 | 证据/代价 |
|---|---|---|
| 顶栏页签顺序/显隐 | `shell.tabs` 驱动 `TopTabPanel` 重建(现在 5 个页签写死在 XAML 且**文案未走 i18n**,`MainWindow.xaml:33-39`) | 低;顺手把页签文案接 `I18n.L` |
| ~~侧栏模式~~ | **已否决(2026-11-14)**:不复活侧栏;`ui_nav_*` / `ui_sidebar_width` / `UiPrefs.SideWidth` / `SideFrost` 等孤儿键保留不接线;`shell.nav_mode/nav_side/nav_width/nav_icon_only/nav_style/nav_groups` 字段**保留在契约里但本期不实现**(为将来留位) | — |
| 宽度拖拽记忆 | 侧栏右边缘 `Thumb` 拖拽 → `shell.nav_width`(夹 140..320,沿用后端钳位 `BackendApi.cs:4612-4623`) | 低;顺手把 `UiPrefs.SideWidth` 与 `ui_sidebar_width` 两个孤儿键**收敛成一个** |
| 仅图标模式 | `shell.nav_icon_only` | 低 |
| 拖拽重排 + 自定义分组改名 | 侧栏内拖拽 + 分组标题就地编辑;写 `nav_order`/`nav_groups` | 中;`NavModel` 已有 `ReorderNavConfig`/`GroupedLayoutPatch` 的补丁算法可复用(`NavModel.cs:185-232,490-571`) |
| 标题栏元素开关 | `shell.titlebar.*` → 控制 `BridgePill`/`AiBtn`/`ThemeBtn`/`AccBtn` 显隐 | 低;**注意红线**:AI 入口形态(D-16:右侧玻璃药丸钮 + Ctrl+K)是**用户定案不可回退**(`DECISIONS.md:124-128`)→ 只允许"隐藏/显示",不允许改形态 |

### 4.E 分栏比例持久化(13 处)

- 新增 `splits` 段 + `PageLayoutHost`(给页面外壳提供"可拖分隔条 + 比例恢复"的统一实现):
  - 在每处两栏之间插入 `GridSplitter`(宽度 18 → 分隔条 + 左右各留白,视觉与现状 `"*,18,2*"` 的 18px 对齐);拖拽结束写 `splits[pageId][splitId]`;
  - 双击分隔条 = 复位默认;设置页「布局」里有逐页复位;
  - 比例夹 `0.15..0.85`,并且**保留既有列的 Min/MaxWidth**(例如实例库左卡 300..380,`LibraryPage.cs:156-157`)。
- 逐处施工清单 = §2.3 的表;其中 6 个特殊页(库页/版本管理/实例详情/下载页/设置页/AI 页)必须单独处理(R3 §5.1):
  - 实例库:把 `"*,18,2*"` 换成"左栏 = 比例 × 可用宽(夹 300..380)" + Splitter;
  - 版本管理:双栏与**内嵌单栏**两种形态并存(`VersionManagePage.cs:175,206-208`),内嵌态必须隐藏 Splitter 且不写比例;
  - 实例详情:分段是横向 rail,不是左右栏 → 该页 `splits` 只用于第 4 段内嵌的版本管理;
  - 下载页/AI 页/设置页:UI 内部是自带双栏,需在**不改变视觉**的前提下插入 Splitter(默认比例 = 现值,保证首屏等价)。

### 4.F 自建区块 / 组件

- 定义在 `widgets[]`,块引用 id = `custom:<widgetId>`(v2 `pages[].blocks[].id`)。
- 首期类型(Q5):`note`(便签,复用 `UiPrefs` 便签数据的思路)、`shortcut`(一键启动指定实例/版本)、`clock`(时钟)、`divider`(分隔)、`playtime`(时长概览)。
- **默认关闭**:`web`(WebView2 嵌页,复用 `Services/WebViewHost`)、`button`(执行动作,只允许**内部动作白名单**:切页 / 启动指定实例 / 打开目录 / 触发既有 RPC 白名单);开启需在设置里勾选并二次确认。
- 插件口子:新增贡献 kind `block`(**3 处白名单**:`PluginManifest.cs:77`、`PluginHostApi.cs:232`、`PluginManager.cs:1259`),渲染方式与 `dashCard` 一致(WebView 或原生 WPF,`docs/plugins/DESIGN.md:169-181,233`);块 id = `plugin:<plugin>:<blockId>`;插件卸载后块**保留并显示"不可用"**(照抄 `PluginDashCards.cs:160-172` 的既有语义)。
- 安全红线:自建块**不得**读写任意文件路径(走既有白名单根 `Utils.ROOT`/实例目录,`ExtraTools.cs:509-524`);`web` 类型禁止 `file://`。

### 4.G 编辑体验

| 能力 | 设计 | 现状证据 |
|---|---|---|
| 对齐辅助线 | 宿主级 `AdornerLayer`/覆盖层绘制:拖动时显示与其它块的**左/右/上/下/中线**对齐线,命中阈值 4px 并磁性吸附 | 全仓 0 命中(无 Adorner/辅助线) |
| 吸附 | 栅格吸附(编辑态可切 `{0,4,8,16,24}`,沿用 `DashLayout.cs:13`)+ 块间吸附 | `DashHost.cs:522-530` 的 `SnapV` 移植 |
| 多选 | `Ctrl/Shift` 点选 + 框选;多选后可整体拖拽/对齐/删除 | 新写 |
| 撤销/重做 | **内存快照栈**(每次提交前压栈整份 LAYOUT;`Ctrl+Z`/`Ctrl+Y`),落盘仍即时(保持崩溃安全:`PageLayoutStore.cs:107-110` 的"内存保留最新"精神) | 全仓无布局撤销(仅壁纸/AI 记忆有 undo) |
| 复制/粘贴布局 | 复制当前页 → 粘贴到另一页(块 id 需过滤到目标页白名单);整页复制/单块复制 | 新写 |
| JSON 实时预览 | 设置页「布局」里的 JSON 编辑器:左边文本框 + 校验(白名单/钳位/版本)+ "应用"按钮;非法时给出**具体字段**的中文错误 | `PageLayoutStore.ParseImport` 已有 kind/version 校验范式 |
| 键盘 | `Esc` 取消当前拖拽/退出编辑、`Delete` 移除(自建块)、`Ctrl+Z/Y`、`Ctrl+C/V`、`方向键` 微调(1 格 / Shift = 10px) | 现状 `EnterEdit/ExitEdit` 无键盘分支(`BlockHost.cs:290-313`) |
| 响应式断点 | 二期(Q6):`variants.narrow` + 窗口宽度阈值 | 新写 |

---

## 5. 文件清单与改动点

### 5.1 新增文件

| 文件 | 职责 | 规模预估 |
|---|---|---|
| `src/TipCL/Pages/Layout/LayoutDocV2.cs` | v2 模型:LayoutV2 / ShellLayout / PageLayout / BlockLayout / SplitState / WidgetDef / Profile + `ToDict/FromJson/Clone/Normalize` | 400–500 行 |
| `src/TipCL/Pages/Layout/LayoutSchema.cs` | 字段白名单、类型门禁、钳位表、`Sanitize()`、`CurrentVersion`;唯一"契约真相" | 250–350 行 |
| `src/TipCL/Pages/Layout/LayoutStoreV2.cs` | 读写 `ui_layout_v2.json`(原子写,优先复用 `Utils.WriteJson`)、`DirOverride` 测试注入、迁移 `FromV1`、**写通 v1**、`LayoutChanged` 事件 | 300–400 行 |
| `src/TipCL/Pages/Layout/LayoutEngine.cs` | 三模式几何:测量/排布/命中/吸附/最小尺寸/行高自适应 | 400–500 行 |
| `src/TipCL/Pages/Layout/GridCanvasPanel.cs` | 自绘 `Panel`(grid/canvas 排布)+ 手柄层 + 栅格底纹 | 250–350 行 |
| `src/TipCL/Pages/Layout/LayoutEditor.cs` | 编辑态:托拽/缩放/对齐线/多选/撤销重做/工具条/托盘/自动滚动/键盘 | 500–700 行 |
| `src/TipCL/Pages/Layout/GuideLayer.cs` | 对齐辅助线绘制 | 120–200 行 |
| `src/TipCL/Pages/Layout/PageLayoutHost.cs` | 双栏/分段页的 splits 接入(Splitter + 比例恢复 + 复位) | 200–300 行 |
| `src/TipCL/Pages/Layout/WidgetRegistry.cs` + `Layout/Widgets/*.cs` | 自建块类型注册与渲染(note/shortcut/clock/divider/playtime;web/button 默认关闭) | 300–500 行 |
| `src/TipCL/Pages/SettingsLayoutPage.cs`(+ `.Themes` 风格的分部可选) | 设置页「布局」视图(7 个小节,见 §4.C) | 400–600 行 |
| `src/TipCL/Core/Launcher/LayoutV2Core.cs` + `Core/BackendApi.Layout.cs`(分部) | Core 侧读写 + 新 RPC(供 AI / 插件 / 未来端口) | 200–300 行 |
| `src/TipCL/Core/Launcher/Ai/LayoutTools.cs` | AI 工具:`layout_get` / `layout_apply`(写操作带确认元数据) | 150–250 行 |
| `tests/TipCL.Tests/LayoutV2ModelTests.cs` | 模型/钳位/白名单 | 150–250 行 |
| `tests/TipCL.Tests/LayoutV2CompatTests.cs` | **v1↔v2 双向**:写通全等、迁移逐项保留、旧版写回后 v2 不丢、损坏回落 | 150–250 行 |
| `tests/TipCL.Tests/LayoutEngineTests.cs` | 三模式排布/吸附/命中/最小尺寸 | 150–250 行 |
| `tests/TipCL.Tests/LayoutEditorTests.cs` | 拖拽/多选/撤销重做/键盘 | 120–200 行 |
| `tests/TipCL.Tests/LayoutSplitTests.cs` | 13 处比例读写与复位 | 80–150 行 |
| `tests/TipCL.Tests/LayoutWidgetTests.cs` | 自建块增删改查 + 危险类型默认关闭 | 100–180 行 |
| `docs/layout-v2-design.md` | 本文件 | — |
| `progress/WP-L1..L7.md` | 每期证据(照仓库惯例) | — |

### 5.2 修改文件(逐处改动点)

| 文件 | 改动 | 风险 |
|---|---|---|
| `src/TipCL/Pages/BlockHost.cs` | 内部改为"创建 `LayoutEditor` + 选择模式";**公共契约 `BlockSpec` / `BlockHost(pageId,specs,margin)` / `Root` / `Changed` 一字不改** | 中:12 个站点全部依赖它,必须用现有用例(`PageLayoutTests` 15 例)兜底 |
| `src/TipCL/Pages/PageBlock.cs` | **不动**(v1 类型冻结,是兼容性的锚) | — |
| `src/TipCL/Pages/PageLayoutStore.cs` | 只加一个"写通投影由 `LayoutStoreV2` 负责"的注释 + 可选的 `Reload()`(清缓存);**读写语义与格式不变** | 低 |
| `src/TipCL/Pages/PageLayoutIO.cs` | 导出增加"整机方案 v2 / 兼容 v1"两选项;导入识别 v2 | 低 |
| `src/TipCL/Pages/LaunchPage.cs` | 英雄区保持固定;内容区块改由引擎 stack 模式承载;删除死字段 `_dash`(仅保留数据逻辑) | 中(该文件工作树本来就有改动) |
| `src/TipCL/Pages/LibraryPage.cs` | 双栏接 `PageLayoutHost` + splits;`folders/list` 仍是 Store 直写但改为经 v2 | 高(自管双栏) |
| `src/TipCL/Pages/VersionManagePage.cs` | 两种形态(顶级双栏 / 内嵌单栏)分别处理 splits | 高 |
| `src/TipCL/Pages/InstanceDetailPage.cs` | 分段竖向/横向不变;第 4 段内嵌版本管理时分隔条禁用 | 高 |
| `src/TipCL/Pages/SettingsPage.cs` | 视图表新增「布局」;左导航 `"168,*"` 接 splits;单块视图补"自定义布局"入口 | 高(102 KB 大文件 + `StaticInitOrderTests` 守卫) |
| `src/TipCL/Pages/DownloadPage.cs` / `CatalogPage.cs` / `InstallPage.cs` | 分段选择记忆 + splits + (可选)CatalogPage 给 pageId | 中 |
| `src/TipCL/Pages/AiPage.cs` | 历史栏宽度改比例化 + 记忆 | 中 |
| `src/TipCL/Pages/PluginsPage.cs` | 已安装网格列数进布局文档;插件块 kind 消费 | 中 |
| `src/TipCL/MainWindow.xaml` / `.cs` | 页签改为按 `shell.tabs` 重建 + 文案接 i18n(现在 5 个页签写死且未走 i18n,`MainWindow.xaml:33-39`);标题栏元素开关;**不做侧栏**(Q1) | 中高(XAML 首屏基准) |
| `src/TipCL/NavModel.cs` / `NavConfig.cs` / `UiPrefs.cs` | 把孤儿键收敛进 `shell`;保留旧键读兼容;补"应用到可视元素"的调用点 | 中 |
| `src/TipCL/Core/Plugins/PluginManifest.cs:77`、`PluginHostApi.cs:232`、`PluginManager.cs:1259` | kind 白名单加 `block` | 低(3 行,三处必须一致) |
| `src/TipCL/Core/Launcher/Ai/Tools.cs` / `ExtraTools.cs` | 注册 + 分发 2 个布局工具(带 `ro:false`/`kind=write_local`/风险元数据) | 中 |
| `src/TipCL/Resources/locales/en.json` + `zh_CN.json` | 新增 key(**必须成对**,保留 `{0}` 占位符) | 低 |
| `tests/TipCL.Tests/LayoutDefaultTests.cs` / `PageLayoutTests.cs` / `NavCompatTests.cs` | **不动,必须保持全绿**(它们是兼容性回归的第一道闸) | — |

### 5.3 明确不动的

`src/TipCL/Core/Launcher/UiLayout.cs`、`LayoutDoc.cs`、`LayoutItem.cs`、`Pages/DashHost.cs`、`DashLayout.cs`、`DashCard.cs`、`LayoutState.cs`(老画布整条链路原样冻结,连 RPC 都不改,避免影响任何既有存档)。

---

## 6. 分期工作包与验收

| 期 | 内容 | 交付/验收 |
|---|---|---|
| **L0** | 契约冻结 + 骨架:新增 `LayoutDocV2/LayoutSchema/LayoutStoreV2` + 迁移 + 写通 v1 + 兼容测试 | `LayoutV2CompatTests` 全绿;`LayoutDefaultTests`/`PageLayoutTests`/`NavCompatTests` **仍全绿**;`dotnet build` 0 错 |
| **L1** | 引擎 + BlockHost 改造(stack 模式等价迁移)+ `span` 跨列 | 12 个宿主 `--shot` 与改动前**逐像素对照**(人工);`--smoke` issues 空 |
| **L2** | grid/canvas 模式 + 手柄/吸附/辅助线 + 撤销重做/多选/键盘 | `LayoutEngineTests`/`LayoutEditorTests` 绿;每模式 d0/d1 截图 |
| **L3** | 外观参数(style/title/pad/density)+ 密度资源键 | 默认渲染等价的截图证据;`--shot design-sheet` 不回归 |
| **L4** | 分栏比例(13 处)+ `PageLayoutHost` + 特殊 6 页 | `LayoutSplitTests` 绿;逐页 `--shot` 对照 |
| **L5** | 整机方案 + 设置页「布局」视图 + JSON 编辑器 | 方案增删改/导入导出用例;真机手工验证 |
| **L6** | 自建区块 + 插件 kind `block` | `LayoutWidgetTests` 绿;危险类型默认关闭用例;插件样例(用 `samples/hello-plugin`) |
| **L7** | 外壳(**顶栏页签自定义 + 标题栏元素开关,不复活侧栏**)+ AI 工具 + 收尾文档 | `NavCompatTests` 绿;AI 改布局端到端;`PROGRESS.md`/`DECISIONS.md` 追加 |

**每期收尾的固定动作**(沿用 D-14 标准参数):

```powershell
# 工作目录 = D:\项目\tipcl\windos\tipcl
dotnet build-server shutdown
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1
dotnet test  TipCL.sln -c Release --no-build --filter "FullyQualifiedName!~PhoneLinkTests"
(Get-Item src\TipCL\bin\Release\net48\TipCL.exe).Length   # 如实记录(超红线按 Q3 结论处理)
.\src\TipCL\bin\Release\net48\TipCL.exe --smoke --smoke-timeout 600
.\src\TipCL\bin\Release\net48\TipCL.exe --shot <pageId> --out artifacts\shot-<pageId>-dark.png --theme dark
```

**注意**:编辑态的按钮不能靠 `--smoke` 覆盖(`NeverClick` 含"重置/导出/撤销/移除/删除",`Smoke.cs:60-72`),编辑交互必须**人工截图 + 单元测试**双证据。

**验收通道(本轮实测,可直接照抄)**:

```powershell
$env:TEMP = 'D:\项目\tipcl\_tmp'; $env:TMP = $env:TEMP   # 必须:否则 40 个用例会因建不了临时目录而假失败
dotnet test TipCL.sln -c Release --no-build --filter "FullyQualifiedName!~PhoneLinkTests"
# → 实测 176/176 通过,1 秒
```

- `PhoneLinkTests` 不参与本机验收(既有失败 `FullFlow`,`DECISIONS.md:183`;跑整包会卡住)。
- `%TEMP%` 建目录被拒是**进程级限制,不是 Windows 文件权限**(诊断技能判定 `NOT_THIS_CLASS`,报告在 `_acl_diag/`),所以不要试图去改权限;照上面把 TEMP 指进工作区即可。
- 构建会顺带重写 `TipCL.exe`(测试工程引用主工程)→ **量字节必须在构建之后立刻做**。

---

## 7. 风险登记册

| # | 风险 | 影响 | 对策 |
|---|---|---|---|
| R1 | **compact 已超红线 481,456 B**(非本功能造成) | `build-both.ps1` 直接 exit 1,交付流程断 | Q3 决策:按 D-11 先例豁免布局功能;体积回收(ImageSharp/CodePages ≈1.15 MB)另立包 |
| R2 | **默认布局等价无自动化像素断言** | "首屏不变"只能靠人眼 | 每期对 12 个宿主逐页 `--shot` 对照 `docs/mockups-hark/v2/`;新增"默认值 = 现状"的模型级断言 |
| R3 | 12 个 BlockHost 站点共享公共契约 | 改 `BlockHost` 会波及全站 | 契约冻结 + 现有 15 个 `PageLayoutTests` 兜底 + L1 单独一期只做"等价迁移" |
| R4 | 6 个特殊页结构各异(自管双栏/分段/内嵌页) | 工时与回归面大 | 单列清单 + 逐页验收;实在接不进的自管页**先只接 splits**,不强推画布 |
| R5 | 网格画布 + 滚动区手势冲突 | 手感差、误操作 | 编辑态才接管指针;边界自动滚动;非编辑态零改动 |
| R6 | 撤销/重做与"即时落盘"冲突 | 可能丢历史或丢崩溃安全 | 保持即时落盘 + 内存快照栈(不引入延迟提交) |
| R7 | v2 引入的双写分叉 | 用户看到"改了又回退" | v2 为唯一真源;v1 只由 v2 单向生成;新增"外部改动检测"提示 |
| R8 | 两个成员同时构建会撞 obj(D-14) | CS2012 锁文件 | 构建串行化:同一时刻只允许一个成员跑 `dotnet build`(本轮已实测 10 个 dotnet 进程) |
| R9 | 单块页(settings:network/about)没有入口 | 与"全部页面统一"矛盾 | L5 在设置页补按钮入口;引擎对 1 块页也开放编辑(不再靠 `specs.Count>=2`) |
| R10 | 插件块 + WebView2 的 airspace/冻结问题 | 编辑态盖不住、点不到 | 编辑态隐藏 WebView(照抄 `PluginDashCards.cs:122-158`) |
| R11 | i18n 新增串漏包裹 | `--i18n-check` 报错(它本来就 exit 1) | 新增中文串一律 `L()` + en/zh_CN 成对 |
| R12 | AI 工具误改布局 | 用户布局被意外覆盖 | 工具带写确认元数据;`layout_apply` 自动备份到 `profiles["AI 备份 <时间>"]` |

---

## 8. 待你确认的问题(汇总)

1. **Q1 侧栏**:今天"侧栏"已不存在。选 (a) 复活可自定义侧栏 / (b) 只自定义顶栏页签 / (c) 两种模式可切换(**建议**)。
2. **Q2 画布形态**:网格画布 grid(建议)还是像素自由 canvas。
3. **Q3 体积红线**:沿用 D-11 先例豁免(建议)还是先做体积回收。
4. **Q4 是否授权改 Core**(新文件 + 新 RPC + 3 处插件白名单 + AI 工具)。
5. **Q5 自建组件的 `web`/`button` 是否默认关闭**(建议默认关闭 + 内部动作白名单)。
6. **Q6 响应式断点现在做还是二期**(建议二期)。
7. **Q7 命名**:`ui_layout_v2.json`(kind=`tipcl-layout`,version=2)、文档路径 `docs/layout-v2-design.md`。

确认后我按 §6 的 L0→L7 分期开工,每期交付测试 + 冒烟 + 截图证据,并在 `PROGRESS.md`/`DECISIONS.md` 追加记录。

---

## 9. 实施进展与接口约定(Lead 维护)

| 包 | 状态 | 验收证据 |
|---|---|---|
| **L0** 契约骨架 + v1 双向兼容 | ✅ **已验收** | Lead 独立复跑:构建 0 错 0 警;**211/211** 通过;兼容闸门(LayoutDefault+PageLayout+NavCompat)**22/22**;`git status` 只有预期新文件;新增代码 `File.WriteAllText` 零命中 |
| **L1** 引擎 + BlockHost 接入 v2 | ✅ **已验收** | 静态面:公共契约(`BlockSpec`/构造签名/`Root`/`Changed`)逐字未变;`BlockHost.cs` +36/−590;`PageLayoutStore.cs` +13/−0(只加 `Reload()`)。执行者实证:289/289;六页 `--shot` 前后 **SHA256 全等**;编辑态与 HEAD 版控制组**逐字节相同** |
| **L4** 分栏比例持久化 | ✅ **已验收** | 6 处真接入 + 7 处逐条说明"为什么不是左右栏";像素级证据:复位后 vs 首屏 library/account **0 像素差**、ai 仅 18 px(输入框光标条),注入比例后 8.6~10.8 万像素变化;三个复用 key 在 zh_CN/en 各命中;数据目录 `ui_*` 数为 0(只读不写)。裁定:分隔条宽度**保持逐处**(18/18/18/14/14/10),不统一成 18 |
| **L7a** 顶栏页签 + 标题栏开关 | ✅ **已验收** | 默认等价:launch `--shot` 与改动前 **PNG SHA256 完全相同**(`D212F428…8B97FD`,900×550,709,862 B)+ 整图 495,000 像素 diff = 0;`LayoutShellTests` 27/27;`locales` 零改动(复用既有 key);`SyncTabs` 一字未改 |
| **L2** 三模式画布 + 外观参数 + 编辑体验 | ✅ **已验收** | 构建 0/0;**349/349** 全绿(290 旧 + 59 新);5 个自测失败的根因全是**用例期望写错**(无一处弱化断言,还补了更强的不变量);默认等价 launch/tasks/settings/account/download **SHA256 逐位相同**,`library` 与 L4 时期逐字节相同;multiplayer 差异经裁图判定为**局域网 IP 枚举顺序**,非布局;**grid/canvas 强模式编辑态截图由 Lead 按配方实拍成功**(`artifacts/l2/edit/`) |
| **L5** 整机布局方案 + 设置页「布局」管理视图 + JSON 编辑器 | ⏳ 待发(依赖 L4,`SettingsPage.cs` 串行) | — |
| **L6** 自建区块 + 插件 kind `block` + AI 布局工具 | ⏳ 待发(依赖 L1;`locales` 需等 L7a 落地) | — |

**已冻结的接口约定(下游包按此对接,不要改这些签名)**

1. **内容工厂**:`ILayoutContentProvider { bool CanBuild(string blockId); object? Build(string blockId, string pageId); }` + `LayoutContentRegistry.Register/Unregister/Clear`;块 id 命名空间 `custom:<widgetId>` / `plugin:<plugin>:<blockId>`;查不到 → 显示「区块不可用」占位,且**数据保留不删**(`Reset` 也不删)。L6 只需实现接口 + 注册,**不要改 `BlockHost` / `LayoutEditor`**。
2. **持久化唯一入口 = `LayoutStoreV2`**(load → 改 → Save);v1 **只由 v2 单向写通**,禁止把新字段写进 v1 文件(D1)。
3. **`PageLayoutStore.Reload()`**(L1 新增)用于写通后让旧路径立刻看到新值;L1 已在每次提交/导入后调用。它是 v1 语义锚点上**只加不删**的例外,谁再改它都要按 §7 的风险流程走。
4. **dev 钩子 `TIPCL_DEV_LAYOUT_EDIT=1` 保留**:仅当环境变量恰为 `1` 且该页可编辑时自动进编辑态,正常启动零影响。保留理由 = §6 已认定"编辑态按钮不在 `--smoke` 覆盖里",它是编辑态自动截图的唯一手段。
5. **已知待补**:locales 缺 `区块不可用` 键(L1 新串;因 `Resources/locales/*` 属 L7a 的写范围而没当场补)—— 由 L6/L7 落地时成对补齐 `en.json` / `zh_CN.json`。

6. **环境坑:`--smoke` 在本会话环境里跑不动**(重要)。表现:在**创建报告目录之前**卡死,连 180s 看门狗都不触发,`_wpf_smoke\run-*` 全盘不存在。根因:**进程级限制禁止在工作区外新建目录**,而 `Smoke.cs:273` 要在 `%LOCALAPPDATA%\TipCL` 下建 `_wpf_smoke\run-<时间戳>\`。**对策(2026-11-14 Lead 已实测验证)**:把 `TIPCL_HOME` 指到工作区内目录,并拷一份真实 `config.json` 进去(避免首启弹窗)——这样跑出来 `state=finished`、`exit_code=0`、**10 页全 ok、`issues: []`、`i18n_missing: []`**、clicks 15/阈值 9、dialogs 8/阈值 5、`elapsed_ms≈18s`、17 张截图。**注意**:该证据目录 `_smoke_home` 里当初被 L6 种过自建区块的布局文档,所以那一次冒烟顺带覆盖了"自建区块 + 危险类型拦截在真实冒烟里不产生 issue",但**不能当"默认布局"的冒烟证据**;最终验收另用一个干净 home 重跑。
7. **既有缺陷(不是本次引入)**:`en.json` 有 `自定义布局`(1 处)而 `zh_CN.json` 没有(0 处),HEAD 即如此(`Core/Launcher/UiLayout.cs:269` 走 `I18nCore.Tr`;WPF 侧用的是带省略号的 `自定义布局...`)。词表统一补齐时一并修。另外 `artifacts/l7a-i18n.json` 是**空文件**,不可当证据引用。

8. **L2 的编辑态取证配方已实测可用**(谁要取证据照它抄):①把真实 `config.json` 拷进隔离 `TIPCL_HOME`(消掉首启诊断同意弹窗);②在 `TIPCL_HOME` 根种 `ui_layout_v2.json`(grid 页给每块写 `x/y/w/h`;**canvas 页必须给每个可见块写矩形**,缺省矩形是满画布 1×1,没写的块会叠在 (0,0));③`TIPCL_DEV_LAYOUT_EDIT=1` 后跑 `--shot <pageId>`。Lead 已出图:`artifacts/l2/edit/tasks-grid-edit-dark.png`(grid,概念跨列 + 卡外观 + 常态标题 + 拖拽/8 向手柄 + 撤销/重做/吸附工具条)、`artifacts/l2/edit/account-canvas-edit-dark.png`(canvas + 栅格底纹)。
9. **验收规范补充**:联机页 `multiplayer` 的 `--shot` 回归**必须先归一化局域网地址列表的顺序**,否则必然假报警(同一构建连拍两次一致,但与基准不同,差异只在 `172.31.*` / `192.168.*` 的枚举次序;`artifacts/l2/multiplayer-line-crop.png` 是裁图证据)。
10. **欠账清单(收尾必做)**:①locales 成对补齐 —— 11 条 L2 新串(撤销/重做/吸附/吸附到栅格/已撤销/已重做/已复制到剪贴板/没有可粘贴的区块/内置区块不能移除/没有可撤销的操作/没有可重做的操作)+ L1 的 `区块不可用` + HEAD 就缺的 `自定义布局`(补 `zh_CN`);**注意本仓库词表 key 就是中文原文**(`en.json` 给英文值、`zh_CN.json` 自映射),不是 `layout.undo` 这类点号 key。②**L2b**:`MainWindow` 接 `LayoutVariants`(宽度来源 + 跨阈值重建当前页)+ `LayoutEditor.ReloadFromStore()`(供 L5 改完立即生效)。③**L5b**:单块页(`settings:network`/`settings:about`)编辑入口(要动 `LayoutEditor.CanEdit` 门槛)。④**人工验收**:真机手拖分隔条 + 编辑态手势(拖拽 / 8 向缩放 / Ctrl-Shift 多选 / 框选 / 辅助线手感 / 键盘),`--smoke` 的 `NeverClick` 含"撤销/重置/导出",这些按钮与手势不会被冒烟点到。

11. **交付裁决(2026-11-14 用户已定)**:①**两个 exe 都出**,compact 超线**如实记录**(`build-both.ps1` 的体积门禁会超线退出,收尾时临时跳门禁或单独构建,并在交付说明里写清实测字节与"超线非本功能引入");②**补 `PROGRESS.md`(逐包一节)与 `DECISIONS.md`(新增 D-xx 决策条目)**,不能只留本设计文档;③**git 只提交布局相关文件、按包切分**(用户原有的未提交改动 `Core/BackendApi.cs`、`Core/Launcher/Ai/Store.cs`、`Pages/LaunchPage.cs`、`tests/TipCL.Tests/AiFixChatStoreTests.cs` 保持未提交,不混入);全程不 push、不动版本号。
12. **收尾派活计划(按此顺序,勿并发改同一文件)**:
    - **D1 = L2b + L5b 接线**(待 L2 与 L5 落地后):`MainWindow` 吃 `LayoutVariants`(内容区宽度 + 跨阈值重建当前页)、`LayoutEditor.ReloadFromStore()`、单块页(`settings:network`/`settings:about`)编辑入口(动 `LayoutEditor.CanEdit` 门槛);写范围 = `MainWindow.xaml.cs`、`LayoutEditor.cs`、`LayoutEngine.cs`、`BlockHost.cs`。**要求它列出所需新中文串但不改 locales。**
    - **D2 = 词表补齐 + 工程记录**:`en.json`/`zh_CN.json` 成对补(L2 的 11 条 + L1 的 `区块不可用` + HEAD 缺的 `自定义布局` + D1 新增;key = 中文原文);`PROGRESS.md` 逐包追加一节;`DECISIONS.md` 新增 D-xx(布局系统 v2 契约、v1 冻结与写通、体积豁免、Q1 否决侧栏、Q5 危险类型默认关闭)。
    - **D3 = 最终验收(Lead 本人)**:构建(compact + premium,`-p:Premium=true`)、全量测试、逐页 `--shot` 对照、`--smoke`(先试 `TIPCL_HOME` 工作区内;不成则把口径改成 `--shot` + 单测并写进文档)、人工手拖与手势清单交付给用户、最后按包切 git 提交。

13. **移植可移植性债务(重要,收尾必记)**:L6 把 AI 布局工具落在 `Core/Launcher/Ai/LayoutTools.cs`,于是 **Core 引用了 `Pages`**(`LayoutStoreV2` 等)。Windows 树里两者同属 `TipCL.exe` 一个程序集,编译无碍;但 mac/linux 移植的既有做法是把 `Core/**` **逐字节复制**进独立的 `TipCL.Core` 项目、`Pages/**` 留在 `TipCL.App` —— 那样这个引用方向会**直接编不过**。
    - **推荐修法(移植时执行,不必现在做)**:L0 的 4 个文件(`LayoutDocV2.cs` / `LayoutSchema.cs` / `LayoutStoreV2.cs` / `LayoutJson.cs`)本来就是**纯模型、零 WPF 依赖**,把它们移到 `Core/Launcher/Layout/` 并把命名空间从 `TipCL.Pages.Layout` 改为 `TipCL.Core.Launcher.Layout`,依赖方向即变成 Pages→Core(与既有 `UiLayout`/`LayoutDoc` 一致)。代价是 LayoutEngine/LayoutEditor/BlockHost/ShellTabs/SettingsLayoutPage/Widgets/tests 里的 `using` 要跟着改(机械改动,一次可完成)。
    - **过渡措施**:本条已记入 `DECISIONS.md`(D2 包)与 `PROGRESS.md` 的遗留项;在真正开始移植 mac/linux 之前必须先做这一步。
