# PARITY — Windows → Linux 对齐矩阵

> 基准:Windows 版 `D:\项目\tipcl\windos\tipcl\src\TipCL`(**只读参考**)。
> 目标:Linux/Avalonia 版 `D:\项目\tipcl\linux\src` 100% UI 对齐。
> 统计口径:按文件基名(basename)比对,已排除 `obj/`;已移植但改名/改目录的按语义归并。
> 生成时间:2026-11-10(本轮重新校准)。

## 0. 规模

| 区域 | Windows | Linux 现有 | 覆盖 |
|---|---|---|---|
| Pages(`src/TipCL/Pages` → `src/TipCL.App/Pages`) | 79 文件 / 28,826 行 | 61 文件 / 17,908 行 | ~62% |
| Core(`Core/` 含 Launcher+Ai+Plugins+Platform) | ~236 文件 / 65,882 行 | 186 文件 / 46,179 行 | ~70% |
| Services(`Services/` → `Common/`) | 35 文件 | 部分并入 `Common/`、`Pages/Layout/`、`Shell/`(逐项判定见 §5) | 部分 |

## 1. 外壳信息架构(IA)—— 目标 #1(2026-11-10 纠偏后)

**权威真相 = Windows v2 外壳,不是 `NavModel.cs`**(`NavModel.TopKeys/GroupedNav` 只服务页面排序 `VisibleFlatOrder` 与 `NavDump` CLI,不是 UI 结构;详见 DECISIONS D-11):

- **标题栏 46px**:Logo(`TipCL`)+ 顶部 **5 页签**:`启动 / 实例库 / 下载 / 插件 / 设置`
  + 右侧簇:`AiBtn`(AI 药丸钮,→ `ai` 页)/ `ThemeBtn` / `AccBtn`(账号钮,→ `account` 页)/ 分隔线 / 最小化 · 最大化 · 关闭。
- **内容区全幅,无侧栏**;`PageWash` 垫底。
- **窗口**:900×550,Min 720×480(Windows `MainWindow.xaml`)。
- `Java`、`关于`、`账号`、`游戏时长`、`服务器`、`反馈与关于`、`下载与网络` **都是设置页内层 7 项左导航**,不是顶级页/侧栏项。
- `tasks`(下载任务)不是顶级页,由下载/安装/目录页内跳 `Navigate("tasks")` 进入。
- 「更多」分区/分组侧栏是 **v1 旧设计线**(`docs/mockups-hark/`),v2 已废弃,不再作为对齐目标。

| 项 | Linux 现状 | 状态 |
|---|---|---|
| 顶栏 5 页签文案/顺序/选中下划线 | `NavRegistry.TopTabs` + `TitleBar` `Tab.Top` | ✅ |
| 标题栏 AI 药丸钮 / 账号钮 / 主题 / 窗口钮 | `TitleBar.axaml`(2026-11-10 补齐) | ✅ |
| 无侧栏 + 900×550 + `PageWash` | `MainWindow.axaml`(2026-11-10 删除 SideNav) | ✅ |
| 设置页内层 7 项导航 | `Pages/Settings/SettingsPage.cs` | ✅ |
| `launch`/`library`/`download`/`plugins`/`settings` 页 | 真实页已接 | ✅ |
| `ai` 页 | `Pages/Ai/AiPage.cs`(2026-11-12 真实对话页:流式 + Markdown + 思考折叠 + 工具步骤组 / 权限确认 / 选择题 / 计划卡 / 任务卡 / 图片附件);`--ai-selftest` CLI 入口已接(2026-11-12 P6 轮) | ✅ |
| `account` 页(账号) | `Pages/AccountPage.cs`(2026-11-11 真实化) | ✅ |
| `multiplayer` 页(联机) | `Pages/MultiplayerPage.cs`(2026-11-11 真实化) | ✅ |
| `tasks` 页(下载任务) | `Pages/TasksPage.cs`(2026-11-10 真实化:概览 + 卡片/进度/日志/取消) | ✅ |
| 插件动态顶部页签(`group:"top"` 追加) | `Shell/TitleBar.SetPluginTabs` + `MainWindow.SyncPluginTabs`(2026-11-12 P8) | ✅ |
| 标题栏桥接状态红点(`BridgeDot`) | Linux 为进程内 API,无桥接态 | n/a |

> **2026-11-10 补充(D-12)**:整站「代码侧取色」(`Ui.Dyn`/`Res.Bind`/`Ui.Theme`/`Ui.Res`)原先因主题资源从未挂到 Application 级而全部失效,已修复;同时修掉 `Controls.axaml` ComboBox 主题的跨两层模板选择器、滑块 Fluent 蓝、`IconBtn` 图标名渲染成文字三类问题。细节见 `DECISIONS.md` D-12。


## 2. 页面(按功能域)

图例:✅ 已移植 · 🔷 改名/改目录已移植 · 🟡 仍为占位 · ❌ 缺失

### 启动 / 实例库 / 实例详情
| Windows | Linux | 状态 |
|---|---|---|
| `LaunchPage.cs` / `LaunchPage.Popups.cs` | `Pages/Launch/LaunchPage.cs` / `LaunchPopups.cs` | ✅ Java 下拉/chip 已补(2026-11-10);「分辨率」字段与线上新闻已补(R16,2026-11-13,见 §9.3) |
| `LibraryPage.cs` | `Pages/Library/LibraryPage.cs` | ✅ |
| `InstanceDetailPage.cs` | `Pages/Library/InstanceDetailPage.cs` | ✅ |
| `InstancePage.cs`(版本管理) | 不移植:Windows 侧该页**未注册进 NavDefs**(只剩静态 `SetDefaultAsync` 被引用),顶级页由 `VersionManagePage` 承担 | n/a |
| `VersionManagePage.cs` | `Pages/Version/VersionManagePage.cs`(2026-11-12 真实化,`GetPage("instance")`) | 🔷 改名/改目录已移植 |
| `VersionSetupDialog.cs` | `Pages/Version/VersionSetupDialog.cs` | 🔷 |
| `DashCard/DashHost/DashLayout.cs` | 数据模型 `Pages/Library/DashModels.cs`(DashCard + DashLayout);`DashHost` 画布宿主**不移植**——Windows v2 的 `LaunchPage` 只 `new DashHost()` 并调 `RefreshThemeVisuals()`,**从未把 `_dash` 加进可视树**,属死代码 | 🔷 |

### 下载 / 安装
| Windows | Linux | 状态 |
|---|---|---|
| `DownloadPage.cs` | `Pages/Download/DownloadPage.cs` | ✅ |
| `InstallPage.cs` | `Pages/Install/InstallPage.cs` | ✅ |
| `LoaderIcons`(Services) | `Pages/Download/LoaderIcons.cs` | 🔷 |
| `CatalogPage.cs`(六分段内容目录) | `Pages/Download/CatalogPage.cs` | 🔷 改名/改目录已移植 |

### 插件
| Windows | Linux | 状态 |
|---|---|---|
| `Plugins/PluginsPage.cs` | `Pages/Plugins/PluginsPage.cs`(四分段 partial 外壳) | ✅ |
| `Plugins/PluginModels.cs` | `Pages/Plugins/PluginModels.cs` | ✅ |
| `Plugins/PluginSettingsCard.cs` | `Pages/Plugins/PluginSettingsCard.cs`(设置页里的插件卡) | ✅ |
| `Plugins/PluginHostPage.cs` | `Pages/Plugins/PluginHostPage.cs`(2026-11-12 P6:`plugin:` 页面宿主,网页承载降级为占位 + 浏览器打开) | 🔷 |
| `Plugins/PluginsPage.Market/Installed/Settings/Dev.cs` | `Pages/Plugins/PluginsPage.Market.cs / .Installed.cs / .Settings.cs / .Dev.cs`(2026-11-12 P6 同分段拆分;设置段按 manifest config 自动生成表单) | 🔷 |
| `Plugins/PluginActionBar/Ui/Overlays/DashCards/Events/AiViewStrip` | 不移植:`ActionBar`/`Overlays`/`DashCards`/`AiViewStrip` 都需要插件进程或 WebView 承载面;`PluginUi` 以进程内快照 + 变更通知等价实现,`PluginEvents` 只保留等价物(逐项理由见 §7.1) | n/a |
| `Plugins/MainWindow.Plugins.cs` | `Shell/MainWindow.axaml.cs`(+ `HookPlugins/OnPluginUiChanged/SyncPluginTabs`);浮层挂接不移植 | 🔷 |

### 设置
| Windows | Linux | 状态 |
|---|---|---|
| `SettingsPage.cs` | `Pages/Settings/SettingsPage.cs` | ✅ |
| `SettingsPage.Themes.cs` | `Pages/Settings/SettingsSubs.cs`(部分) | 🔷 |
| `SettingsPage.PhoneSync.cs` | `Pages/Settings/SettingsPage.Cards.cs`(部分) | 🔷 |
| `PhoneSyncPanel.cs` | `Pages/Settings/PhoneSyncPanel.cs`(2026-11-12 M4-H R3:选电脑实例与手机存档根、对比两边世界、逐项传 / 一键同步) | ✅ |
| 设置页内 Java / 关于 子项(索引 2 / 7) | Java:`Pages/Settings/SettingsSubs.cs` 的 `JavaView` 按 Windows `JavaPage` 全量重写(2026-11-12,含下载卡/发行版/实例默认/已检测列表);关于:设置页「反馈与关于」内联 | ✅ |

### 长尾页面(2026-11-11 更新)
| Windows | Linux | 状态 |
|---|---|---|
| `AccountPage.cs` | `Pages/AccountPage.cs` + `SkinPng` / `Thumbs` / `AvatarTile` | ✅ |
| `MultiplayerPage.cs` | `Pages/MultiplayerPage.cs`(陶瓦联机全状态 + 直连启动) | ✅ |
| `TasksPage.cs` / `TaskCard.cs` | `Pages/TasksPage.cs` | ✅ |
| `CatalogPage.cs`(Mod/整合包/数据包/资源包/光影/世界) | `Pages/Download/CatalogPage.cs` + `CatalogModels.cs`(2026-11-12 六分段真实化:搜索/已安装/收藏三态 + 版本文件选择 + 收藏/导出/启停删) | ✅ |
| `VersionManagePage.cs` / `InstancePage.cs` / `VersionSetupDialog.cs` | 见上:版本管理已移植;`InstancePage` 为 Windows 死代码,不移植 | 🔷 |
| `PlaytimePage.cs` / `ServersPage.cs` | 已由设置页内层导航覆盖(未做独立页) | 🔷 |
| `FeedbackPage.cs` / `FeedbackConsent.cs` | `Pages/Feedback/FeedbackView.cs`(设置页内层第 6 项,含提交/历史/帮助)+ `Common/FeedbackConsent.cs`(纯逻辑,同意门) | ✅ |
| `FirstRunWizard.cs` | `Pages/Feedback/FirstRunWizard.cs`(2026-11-12,首启自动弹 + 可重跑) | 🔷 |
| `DesignSheetPage.cs` | 无(设计样张,不进导航) | n/a |
| `AiFixWindow.cs` / `SkinPng.cs` | `SkinPng` 已移植;`AiFixWindow`(AI 修复会话)Linux 侧是 `Pages/Ai/AiFixView.cs` + Core 的 `AiFixChat`,由崩溃界面的「交给 AI 修复」打开(2026-11-12) | ✅ |
| 对话框:`ModsDialog / GlobalModsDialog / SavesDialog / VersionSetupDialog` | `Pages/Version/ModsDialog.cs` / `GlobalModsDialog.cs` / `SavesDialog.cs` / `VersionSetupDialog.cs`(2026-11-12) | 🔷 |
| `CrashUi.cs` | `Pages/Feedback/CrashUi.cs`(2026-11-12;`crash` 事件自动弹;**「交给 AI 修复」已接** `AiFixView`) | ✅ |
| `Shortcut.cs`(桌面快捷方式)→ `create/delete_desktop_shortcut` | `Core/Launcher/Shortcut.cs` + `Launcher/ShortcutIcon.cs` + App 的 CLI 入口 `Session/CommandLine.cs`(2026-11-12 P3.5:`.desktop` 真的能双击启动该版本) | ✅ 已移植(参考版这条链是坏的,见 §6) |
| `PhoneSyncPanel.cs` | 见设置小节:`Pages/Settings/PhoneSyncPanel.cs` | ✅ |

### AI 对话子系统(2026-11-12 主体完成)
`AiPage.cs` / `AiPage.Images.cs` / `AiChatOps / AiIcons / AiRisk / AiSelfTest / AiUiText / AiPermissionPanel`;
Markdown 渲染链 `MdParser/MdBlock/MdInline/MdView/BlockHost`;
消息块 `Bubble / AskBlock / AskCard / ConfirmCard / PlanCard / ChatInput / StepGroup / ThinkFold / ToolLine / ToolState / TaskCard`。
→ Linux(2026-11-12 M4-H P5 更新):Markdown 链(`Pages/Ai/MdParser.cs` / `MdBlock.cs` / `MdInline.cs` / `MdView.cs`)+ 对话页(`AiPage.cs` / `AiBubble.cs` / `ChatInput.cs`)+ **消息块与交互件**(`ToolLine.cs` / `StepGroup.cs` / `ConfirmCard.cs` / `AskCard.cs`(含 `AskBlock`)/ `PlanCard.cs` / `TaskCard.cs` / `AiIcons.cs` / `AiPermissionPanel.cs` / `AiImages.cs`)+ **Core 工具链**(`Launcher/Ai/Tools.cs` / `ToolRunner.cs` / `AiAgent.cs` / `AiToolTypes.cs` / `AiUiText.cs` / `AiRisk.cs` / `AiMemory.cs` / `AiFixChat.cs` / `AiSelfTest.cs` / `Conflict.cs`)已移植;**崩溃界面的「交给 AI 修复」**(参考版 `AiFixWindow.cs`)在 Linux 侧是 `Pages/Ai/AiFixView.cs`(非模态面板)。图片输入由 `AiImages` 提供:**剪贴板粘贴与文件选择器可用**;「拖入图片」在 Linux/X11 上不可用(同 §8 首段的平台限制,见 D-38)。**确认卡的「改前改后」预览**(参考版 `Core/Launcher/Ai/Preview.cs`)已于 M4-H R5 接通:写工具在执行前算出逐行 diff / 删除清单 / 安装目标,经 `ToolRunner.Confirm` 回调交给 `ConfirmCard` 着色显示(见 §3 与该轮 DECISIONS D-31)。

**AI 服务层(2026-11-12 M4-H R1 补齐)**:之前「仍缺」的那几件已在 Core 里做成真件,名字与参考版一致 ——
`Checkpoint`(检查点快照/回滚,内容寻址 + journal)、`Rewind`(回退上一轮:对话截断 + 文件还原 + `TurnRewound`)、
`Compact`(整段摘要 + 微压缩 + 熔断,`CompactState/CompactionResult/MicroConfig/MicroResult/AutoConfig`)、
`Tokens/TokenState/Usage`(上下文占用与用量,provider 给真值就校准)、`Mcp/McpClient`(**自己实现的 stdio JSON-RPC,不引第三方包**)、
`Artifacts`(超长工具结果落盘 + `read_artifact` 回读)、`AiFs`、`Store`(`ai_chats.json` + `cache/ai_sessions/*.jsonl` 会话事件流)、
`Trace`、`StopReason`;工具表补 `read_artifact` 与 `dispatch_subagent`(**只读子代理**:plan 档、4 轮、不许再派);
`AiAgent` 在写操作前按 `Tools.AffectedPaths` 做检查点、记 TurnStarted/ToolStarted/TurnCompleted/TurnFailed、按 `ai_context_window` 自动压缩、回传 usage。

**AI 界面残留(2026-11-12 M4-H R2)**:`AiChatOps` 斜杠命令(7 条:崩溃分析/扫模组/新实例/装Fabric/新对话/清空/撤回,含别名与上下键选择)、
会话记忆面板(`AiMemoryPanel`,与设置页「记忆」卡同一份 `ai_memory.md`)、上下文占用环(`AiIcons.Ring` + `Usage` + `AiUiText.ContextRatio`)、
对话持久化到 `ai_chats.json` 并在回页时读回、**`/撤回` 真的接上 `Rewind`**(对话截断 + 文件还原)。

**仍未做(如实)**:1**真模型端到端**(需要真实 API key,属对外调用,留给用户);2`McpServer`(把启动器工具开放给外部 agent)不移植 —— 它依赖 `BackendApi`(D-2 已判无宿主),且 Linux 的写工具要过界面确认回调,外部客户端没有用户点确认只会被自动拒;3`Hooks/AgentPresets`(无插件宿主,同 §7);4`Prompt/Diagnose/Permission/ModConfig` 参考版有独立文件,Linux 侧早在 P5 就并进 `AiData.SYSTEM_PROMPT` / `Crash` + `ToolRunner.Diagnose` / `ToolRunner.Decide` + 权限档位与规则键 / `ToolRunner` 的 `list/read/write_mod_config`,不再另建同名类。`--ai-selftest` 自检由 32 项扩到 **52 项**(压缩/检查点/回退/产物/用量/MCP 各条,R19 再 +2 条权限理由)。

## 3. Core / Services

### 缺失 Core(按 2026-11-12 复核;已搬走的不再列在这里)
- **AI Agent 子系统的「服务层」部分**:工具链那一半(`Tools` 表 / `ToolCall` / `ToolMeta` / `ToolRunner` / `AiAgent` / `Conflict`)已在 M4-H P5 移植;**服务层那一半已在 M4-H R1 补齐**(`Checkpoint / Rewind / Compact(+CompactState/CompactionResult/AutoConfig/MicroConfig/MicroResult/RapidRefillBlocked) / Tokens / TokenState / Usage / Store / Trace / Artifacts / AiFs / Mcp / McpClient / McpError / StopReason`,见 §2 AI 小节)。仍不移植:`Scheduler`(Linux 的工具循环按顺序执行,没有并发组)、`McpServer`(对外暴露,依赖 `BackendApi`)、`Permission`/`Prompt`/`Diagnose`/`ModConfig`(Linux 侧已有等价实现,不另建同名类;其中 `Permission` 的**裁决理由文案**已按 R19 逐字移植进 `ToolRunner.Decide`,唯一没搬的半部是 `PermissionNote` —— 把当前档位写进系统提示,见 §9.4)、`Hooks`/`AgentPresets`/`ChatEventLog`(无插件宿主)、`ExtraTools` 的通用 fs 工具面(只留 `config/` 白名单的 `write_mod_config`,不放开任意路径读写)。(`Preview` 原在本清单里,M4-H R5 已补齐:**确认卡的改前改后 diff 预览**见 §2 AI 小节与 D-31。)
- **`Shortcut(ShortcutError)` 已完成**:M4-H P3.5 搬运 `Core/Launcher/Shortcut.cs` 并补了真的 CLI 入口(见 DECISIONS D-23)。
- **插件宿主运行时不移植(M4-H P6 判定,理由见 §7.2)**:`PluginManager`(宿主运行时半部)`/ PluginHostApi / PluginSessions / PluginTools / PluginLlm / PluginAgentBridge / PluginHooks / LaunchHooks / DotNetPluginContext / JsonRpcPeer / NodeHostProcess / NodeRuntime / SecretStore / Sdk/PluginSdk / Sdk/PluginUiRegistry / BackendApi.Plugins` —— 都需要 Node 宿主或 WPF 契约。**文件级链路已真做**:`PluginDirs / PluginStateStore / PluginManifest / PluginMarket` + 新增 `PluginInstaller`(安装/更新/卸载)+ `PluginHost`(启停落盘/日志/UI 贡献汇总)。
- **RPC/后端**:`BackendApi / BridgeServer / RpcDispatcher`
- **不可移植(WPF/Windows 专属,按 D-2 用桩/条件编译)**:`Platform/WinCredSecretStore / Platform/Wpf/WpfDecodedImage / Platform/Wpf/WpfImageCodec`

### 已移植 Core(Linux-only 命名)
`CoreInfo / HttpLite`(替代部分 Net);`Layout/PageBlock / PageLayoutIO / PageLayoutStore`(从 Pages 迁入 Core);`Plugins/PluginHost`(进程内宿主:启停落盘/日志/UI 贡献)+ `Plugins/PluginInstaller`(市场/本地安装与卸载,2026-11-12 P6);`Launcher/Ai/Preview`(确认卡的改前改后 diff 预览,M4-H R5)。

### Services 归并(Linux)
- `Services/BridgeEvent / TaskStore / Dlg / Motion / Fmt / I18n / Ui / UiExt / Payload / BtnKind / ToastKind / Ico / Lucide / LoaderIcons / Thumbs / ThumbTile / TaskRow / SmoothScroll / SideFrost / Backdrop / Wallpaper / FileDrop / FileKinds / AccountGate / ImeGuard / SPanel / WebViewHost / WebpFallback / LucideAssets / SideFrostEx`
- Linux 侧对应物:`Common/Dlg / Motion / TaskStore / ThemeService / IconLib / Toast / Res`;`Pages/Layout/U / UiExt / LayoutFx / BlockHost`;`Shell/*`。其余(WebViewHost、SPanel、Wallpaper、SideFrost、SmoothScroll、ImeGuard、LucideAssets…) **尚未移植**。

## 4. 关键缺口汇总(按优先级)

1. ~~外壳 IA 重建~~:已按 Windows v2 落地(顶部 5 页签 + 无侧栏,见 D-11),`NavModel` 的 6 分组是 v1 旧设计线,不再作为目标。
2. ~~任务/进度页真实化~~:已完成(`Pages/TasksPage.cs`,2026-11-10)。
3. **长尾页面**(2026-11-12 P3.5/P5 复核后):已完成 AccountPage / Multiplayer / Tasks / Catalog(六分段)/ VersionManage + 版本设置·模组·存档对话框 / **JavaPage**(设置页 Java 子页,P4)/ **Feedback 独立页**(设置页内层第 6 项,P4)/ **FirstRunWizard**(P4)/ **CrashUi**(P4)/ **桌面快捷方式链**(P3.5)。剩 Playtime、Servers 独立页(现由设置页内层导航覆盖,未做独立页 —— 这两项在 Windows v2 也不是顶级页)。
4. **AI 对话子系统**:2026-11-12 P5 主体完成(工具链 / 权限确认 / 结构化选择题 / 计划卡与任务卡 / 自检 / 图片输入 / 崩溃「交给 AI 修复」),**R1+R2(同日)补齐服务层与界面残留** —— 检查点回滚 / 上下文压缩 / 令牌与用量 / MCP 客户端 / 只读子代理 / 产物回读 / 斜杠命令 / 记忆面板 / 上下文占用环全部落地(见 §2 AI 小节);剩真模型端到端(需要 API key)与 `McpServer`(依赖 `BackendApi`)。**R5 接通了确认卡的变更预览**(逐行 diff / 删除清单 / 安装目标,见 §2 AI 小节与 D-31)。
5. **Core 业务件**:`News`、`Worlds`、`Servers`、`Feedback`、`Repair`、`Themes`、`Thumbnails`、`UiLayout`、`Updater`、`Cleaner`、`ContentExport`、`HelpContent` —— 这些在 M4-D/P1/P2/P4 已陆续搬入 `src/TipCL.Core`(见 §3「已移植」);真正还缺的是 `Shortcut`(P3.5 已完成)与上表所列的 AI 服务层、插件宿主。
6. ~~插件宿主全链~~:2026-11-12 P6 判定并落地 —— **文件级链路(市场/安装/更新/卸载/启停/日志/权限/开发链接)+ 壳侧(`plugin:` 页面、动态顶部页签、界面贡献快照)已真做**;需要在进程内跑插件代码或渲染插件网页的那一半不移植(无 Node 宿主、参考 .NET 契约基于 WPF),理由与逐项判定见 §7。
7. **Services 视觉/交互件**:WebViewHost、SPanel、Wallpaper、SideFrost、SmoothScroll、LucideAssets(逐项结论见 §5);**`FileDrop/FileKinds` 已于 P6/P7 落地为代码级统一入口(见 §8)**;但 Linux/X11 上收不到系统拖放(平台限制,见 §8 首段与 D-38),真机可用入口是各页的显式导入按钮。

> 注:「真机 E2E(Fabric 安装、启动游戏)」依赖 Ubuntu 盒子(`mcp__box__*`),当前会话未接入,需盒子可用时执行。

## 5. Services 视觉/交互件逐项判定(2026-11-12,M4-H P7)

> Windows 版 `src/TipCL/Services/` 下与「视觉/交互」相关的件,逐项给结论。判据:**Linux 侧有没有对应的宿主/机制**;
> 没有宿主的一律不移植并写清理由,不做「看起来像」的空壳。

| Windows 件 | 行数 | Linux 结论 | 理由 |
|---|---|---|---|
| `SPanel.cs` | 70 | 不移植 | WPF 的自适应面板(横向/纵向/换行切换)。Avalonia 的 `StackPanel` / `WrapPanel` 已覆盖同样用途,项目内直接用它们(B1 起就是这么写的)。 |
| `SmoothScroll.cs` | 71 | 不移植(可用现有滚动替代) | 平滑滚轮(滚轮事件转 230ms 缓动)。Avalonia 的 `ScrollViewer` 没有 `CanContentScroll`/`OffsetProperty` 的等价动画挂点,要做得靠 PointerWheel 自己插值,收益低于风险;现有页面用原生滚动。 |
| `SideFrost.cs` / `SideFrostEx.cs` | 160 / 244 | 不移植 | 侧栏毛玻璃底。**Linux 外壳按 v2 就没有侧栏**(D-11),没有宿主。 |
| `Wallpaper.cs` | 453 | 部分等价,不另移植 | 沉浸壁纸(静态图 / mp4 / 目录轮播 + 模糊遮罩)。Linux 启动页已实现「截图/hero 背景 + 遮罩」的静态部分(B1);mp4 与目录轮播依赖 WPF MediaElement,Linux 侧无等价播放宿主,明确不做。 |
| `WebViewHost.cs`(+`.Loader`/`.View` 共 871) | 871 | **不移植** | 宿主是 WPF `WebView2`(Chromium Edge Runtime)。Linux 侧没有 WebView2 等价宿主(要引 CEF/WebKitGTK 第三方包,属依赖白名单外),插件 UI 面板因此降级为「有数据、无网页渲染」;已在插件页说明。 |
| `LucideAssets.cs` / `Lucide.cs` | 36 / 237 | 已由 `Common/IconLib.cs` 替代 | Windows 用 Lucide 的 SVG path 资源;Linux 侧把用到的图标整理成 `IconLib` 的 Geometry 常量(51 个语义键),同一套「矢量 Path + 主题色描边」思路,不引入字体或图片资源。 |
| `Backdrop.cs` | 250 | 不移植 | 窗口级亚克力/毛玻璃(Win32 `DwmSetWindowAttribute`)。Linux 桌面环境的模糊由合成器决定,应用侧没有可移植的等价 API。 |
| `PhoneSyncPanel.cs` | 205 | **已移植(2026-11-12 M4-H R3)** | 世界同步/对比面板:`Pages/Settings/PhoneSyncPanel.cs`(电脑实例×手机存档根、逐行裁决、传到手机/传到电脑、一键同步含冲突逐个问、日志框),数据面全走 Core `PhoneLink`/`SaveSync`;设置页手机同步卡补「同步面板」入口(没连接只提示,不开空面板)。 |
| `ImeGuard.cs` | 88 | 不移植 | WPF 输入法(IME)组合态修正;Avalonia 的 TextBox 自己处理 IME 组合,Linux/ibus 路径不适用。 |
| `FileDrop.cs` / `FileKinds.cs` | 291 / 671 | **已移植(代码);Linux/X11 上收不到系统拖放** | 拖放导入(插件/整合包/模组/资源包/光影/数据包/存档/皮肤)。Linux 侧 `Common/FileDrop.cs` + `Common/FileKinds.cs`:窗口级 + 页面级(目录页带实例上下文、插件页偏好 plugin)`DragDrop` 接入,识别 → 分桶 → 认不准的问一次 → 落到真实安装链。**不做**壁纸(没有自定义壁纸消费方)、不做嵌套包递归探测,理由见 §8。 |

## 6. 对照中发现的 Windows 参考实现缺陷(汇总)

> 这一节汇总**参考实现本身的问题**(不是 Linux 移植的欠账)。做法一律是:Linux 侧按真实语义实现,并在 DECISIONS 里登记偏离理由;参考仓库只读,不改。
> 这一节可以直接给用户看。

| # | 位置 | 现象(用户能感知的) | 根因 | Linux 侧处理 |
|---|---|---|---|---|
| 1 | `Pages/CatalogPage.cs` 批量导出 | 点「全部导出」**没有任何反应**,既不导出也不报错 | `ContentExport.ExportMany` 返回 `{dir, exported[], failed[{name,error}]}`,而页面的 `ExportResult` POCO 把 `failed` 当 `List<string>` 取 → 反序列化失败 → 整块判空 | 按 `PyDict` 直读真实结构,失败项逐条列出(D-18) |
| 2 | `Pages/SavesDialog.cs` 存档列表 | 存档行的「大小 / 上次游玩 / 模式」三列**恒为空** | POCO 期望 `Size/LastPlayed/Mode`,而 Core 实际返回 `{name,path,icon,bytes,mtime}` | 直接读真实键(字节数格式化 + mtime 本地时间),不假装有那三列(D-19) |
| 3 | `Core/Launcher/Shortcut.cs:79` 桌面快捷方式 | 双击快捷方式**只打开启动器**,不会启动那个版本 | 生成的参数 `-i <实例> launch <版本>` 在整个工程里**没有任何消费者**(`App.xaml.cs` 的 `OnStartup` 只认自检开关;全仓库搜 `"-i"` 只有 Shortcut.cs 一处) | Linux 侧补了真的 CLI 入口 `-i <实例> launch <版本>`,走与 UI 相同的启动链路,双击真的启动版本(P3.5 / D-23) |
| 4 | 同上文件自带的 Linux 分支(`Shortcut.cs:176/180`) | 即便在 Linux 上生成,写出来的 `.desktop` 也点不动 | `Chmod755` 是**空方法**(没有可执行位);`Path=`/`Icon=`/`Exec=` 直接沿用 Windows 反斜杠路径;图标给的是 `icon.ico` | Linux 版真设可执行位、路径规整、图标改为程序化生成的 PNG(D-23) |
| 5 | `Pages/CatalogPage.cs` 项目介绍页 | 资源包 / 光影 / 数据包的「打开介绍页」跳到**错的网页地址** | `_content_row` 一律按 `kind="mod"` 拼 `modrinth.com/mod/<slug>` | 按真实 kind 拼地址;CurseForge 优先用 `website_url`(D-18) |
| 6 | `Pages/CatalogPage.cs` 启停模组 | 连点两次启停会报「模组文件不存在」 | 禁用会把文件改名为 `*.disabled`(Core 语义),而页面启停后不重新列一遍,手里还是旧文件名 | 启停成功后重载已装列表(D-18) |
| 7 | `Pages/LaunchPage.cs` + `Pages/DashHost.cs` | —(不可见) | v2 的 `LaunchPage` 只 `new DashHost()` + `RefreshThemeVisuals()`,**从未把 `_dash` 加进可视树** | Linux 侧不移植 `DashHost` 画布宿主,保留数据模型(P7 / D-19) |
| 8 | `Pages/CatalogPage.cs:753` + `Core/BackendApi.cs:1606`(下载页「装进存档」) | 装了数据包后**并没有进任何存档** —— 下拉里选「装进哪个存档」等于白选,用户得自己去存档目录复制 | 页面把选择写成 `extra["save"]`,而 Core 侧**没有任何地方读这个键**(`install_datapack` 只把 extra 转给 `_install_content_impl`;全仓库搜 `["save"]` 只有这一处写入,没有读取) | Linux 侧按界面写明的意图落地:装完就地复制进该存档的 `datapacks/`;进不去只记一条带原因的日志(数据包本身已装好)(R18 / D-50) |
| 9 | `Core/Launcher/Saves.cs:85-86`(`InstallDatapackIntoSave`) | 版本隔离时,列表里明明有这个数据包,点「装进存档」却报「**数据包不存在**」 | 源路径硬编码 `<实例>/datapacks`,无视传进来的 `version_id`;而带 `version` 的 `get_installed_datapacks` 列的是 `<实例>/versions/<版本>/datapacks` | 源目录改为「版本目录优先、实例根回退」,与列表同一个目录(R18 / D-50) |
| 10 | `Core/Launcher/Saves.cs:92-93` | 选一个不存在的存档名,会**凭空造出一个只有 `datapacks/` 的空壳存档** —— 游戏里是个打不开的假世界 | 无条件 `Utils.EnsureDir(<游戏目录>/saves/<存档>/datapacks)`;而同文件的 `DeleteSave` / `OpenSave` 是会检查存在性的 | 存档目录不存在直接抛 `存档不存在: X`,一个目录都不建(R18 / D-50) |
| 11 | `Services/TaskStore.cs:9/13/15/17/27`(参考版;Linux 初版逐行照搬) | 有任务在跑的时候打开「任务」页,偶发**整块崩一下**:UI 线程抛 `Collection was modified; enumeration operation may not execute`(栈:`TaskStore.RunningCount` ← `TasksPage.Sync`) | `_rows` 是普通 `List<TaskRow>`,而 `Emit` 的设计本身说明「任务工作体跑在线程池上」:工作线程在 `Add` / `ClearFinished` / `Remove` 里增删,UI 线程在 `RunningCount` / `Rows` / `Newest` / `Get` 里遍历,**两边都没有锁** | Linux 侧改成「私有锁 + `ToArray()` 快照」(遍历期间不受增删影响),并把 `TaskRow.Append` 的 `StringBuilder` 一并加行内锁、新增 `LogText` 快照属性给页面读(R21b / D-54);**参考仓库保持只读** |

> 认定方式:以上 1–2、5–6 由「按 Core 真实返回结构取值」的复核发现;3–4 由 P3.5 的移植复核发现(用户点名要求);7 由 P7 的可视树核对发现;8–10 由 R18 的「数据包装进存档」逐条路径对位发现(页面传下去的键**没人读** / 源路径写死不看版本 / 无条件建目录造空壳存档)。11 由 R21b 的最终验收发现(连跑两遍 `dotnet test` 的第二遍真炸了) —— 红/绿各 3 次的单变量对照见 D-54。
> 若参考仓库后续修好这些点,本表与相应 DECISIONS 条目应同步更新。

## 7. 插件宿主全链逐项判定(2026-11-12,M4-H P6)

> 判据与 §5 同一口径:**Linux 侧有没有对应的宿主/机制**;没有宿主的一律不移植并写清理由,不做「看起来像」的空壳。
>
> 参考版的插件体系有两条运行路径,两条都要宿主:
> 1. **Node 插件**:内核拉起一个 Node 子进程(`NodeHostProcess`/`NodeRuntime`),用 stdio 上的 JSON-RPC(`JsonRpcPeer`)对话;
>    插件的网页界面(页面/AI 视图/浮层/首页卡片)由宿主起本地 http 服务,再用 **WPF WebView2** 承载。
> 2. **.NET 插件**:把插件程序集 `Assembly.LoadFrom` **加载进启动器进程**,直接调插件的 WPF 界面工厂
>    (SDK 契约 `TipCL.Plugins.*` 定义在 **net48 的 `TipCL` 程序集**里,工厂签名是 `System.Windows.FrameworkElement`)。
>
> Linux 侧的两条硬约束:(a) 不引入 CEF/WebKitGTK/Node 宿主等第三方包(P7 已就同一约束判过 `WebViewHost`);
> (b) 参考契约的 UI 类型是 WPF、.NET 插件是 net48 + 程序集标识 `TipCL`。
>
> **总判定**:凡是**文件级**的插件管理能力(市场、安装/更新/卸载、启停落盘、日志、权限、数据目录、开发链接)
> 都能真做且已在 M4-H P6 做出来;凡是**需要在本进程里跑插件代码或渲染插件网页**的能力一律不移植,
> 插件页与开发者页如实告知用户,不留假按钮。

### 7.1 页面件(Windows `Pages/Plugins/`)

| Windows 件 | 行数 | Linux 结论 | 依据 / 理由 |
|---|---|---|---|
| `PluginsPage.cs` | 365 | 已移植 → 本轮拆成四分段 partial | 外壳/分段切换/⋯菜单/子视图(带返回)结构照搬;`Pages/Plugins/PluginsPage.cs` + 四个 `.Market/.Installed/.Settings/.Dev.cs` 与 Windows 同名同分段。 |
| `PluginsPage.Market.cs` | 399 | **已实现(真装)** | 市场数据面是纯 HTTP+JSON,Linux 已有 `Core/Plugins/PluginMarket`(索引/镜像/合并/权限行);安装链本轮补齐(`PluginInstaller`:镜像候选→sha256→清单 id/version 一致→engines→依赖→暂存→原子替换→回滚)。**截图在详情里真的加载**(`Common/Thumbs` 的远程图 + 内存缓存,横排缩略图;下不动时给「点击用浏览器打开」的占位,不显示破图 —— 2026-11-12 R4),骨架屏用文案替代。 |
| `PluginsPage.Installed.cs` | 366 | **已实现** | 卡片/启停/卸载(可选删数据)/日志/数据目录/权限面板齐;**「有更新 → vX」徽标**在 2026-11-12 R4 接上(读市场索引的 `updatable` + 版本号,菜单项也随之变成「更新到 vX」);**差异**:参考版的「重新加载」是让宿主重启插件进程,Linux 无宿主 → 菜单里不呈现该项(不给点了没反应的按钮),卡片上给「重启后生效」之外的如实说明。 |
| `PluginsPage.Settings.cs` | 366 | **已实现(自动表单)** | 按 manifest 的 `config`(string/boolean/integer/number/enum/secret)自动生成表单,值写 `plugins.json` 的 `config`;「自定义设置页」是 WebView 承载的插件界面 → **不呈现**(无宿主、无 WebView),右上角也不放「插件设置页」切换钮。 |
| `PluginsPage.Dev.cs` | 331 | **已实现(如实降级)** | 本地 zip/文件夹安装、开发文件夹链接、插件日志(读 `plugin-logs/*.log`)、模型供应商列表照搬;**运行环境卡如实写「Linux 版不内置 Node 宿主」**,参考版那两条「Node 宿主 已安装 / 网页视图 可用」的状态在 Linux 上不存在。 |
| `PluginHostPage.cs` | 114 | **已实现(文字占位)** | `kind:"page"` 的宿主页:原生界面工厂不移植(见 7.2),网页地址在 Linux 无 WebView2 → 页面壳 + 贡献信息 + 「在浏览器打开」;插件被禁用/卸载后由外壳标成不可用(对齐 `MarkGone`)。`plugins_ui_event`(shown/hidden 通知插件)不移植:通知要投递给插件进程,无宿主。 |
| `PluginActionBar.cs` | 63 | **不移植** | `kind:"action"` 的按钮点击要发 `plugins_action`,再由内核投 `action.invoke` 给插件进程。Linux 侧没有可收事件的插件 → 按钮点了无处可去,按「不做假按钮」不呈现。 |
| `PluginOverlays.cs` | 10.5KB | **不移植** | 主窗口上的无边框子窗口(`WS_EX_LAYERED \| WS_EX_TRANSPARENT` 点击穿透 + DWM 玻璃 + WebView2 透明底)。Win32/窗口管理器专属,Linux 的合成器语义不同;R3 已判砍,本轮维持。 |
| `PluginDashCards.cs` | 202 | **不移植** | 首页卡片属于 `DashHost` 画布宿主(卡片类型注册表 + 编辑布局拖拽),而 P7 已核对:`DashHost` 在 v2 是死代码(从未加进可视树),Linux 侧不移植宿主 → 卡片无处可摆。 |
| `PluginEvents.cs` | 272 | **部分(等价物已存在)** | `plugins.changed / plugins.ui_changed` → Linux 是进程内事件(`Core/Plugins/PluginHost.Changed`),插件页与外壳直接订阅,等价物已实现;`plugins.confirm / ui_request / error / overlay` 都由宿主在插件调用时发出 → **不移植**(无宿主即无来源)。 |
| `PluginUi.cs` | 494 | **部分** | 快照(`All/OfKind/Find/Changed`)+ 变化比较 + 由变化驱动外壳重建页签 → **已实现**(`Pages/Plugins/PluginUi.cs`,数据源是 `PluginHost.UiContributions()`);`NativePluginUi`(反射查 .NET 插件的 WPF 原生界面)与 `PluginIcon` 的 data URL/缩略图解码 → **不移植**(见 7.2 的 .NET 判定)。 |
| `AiViewStrip.cs` | 11.5KB | **不移植** | AI 页对话上方的插件视图条:每个「视图×对话」一个 WebView,覆盖消息列表与输入框。无 WebView 宿主 + 无插件 aiView 进程 → 条上没有可显示的内容。 |
| `MainWindow.Plugins.cs` | 125 | **已实现** | `group:"top"` 的插件页面追加成顶部页签(`RebuildPluginTabs` 的等价物在 `Shell/TitleBar` + `MainWindow`)+ `plugin:<plugin>:<id>` 页面的懒建与失效处理;`PluginOverlays.Attach(this)` 不移植(见上)。 |

### 7.2 Core 件(Windows `Core/Plugins/`)

| Windows 件 | 行数 | Linux 结论 | 依据 / 理由 |
|---|---|---|---|
| `PluginManager.cs` | 55KB | **部分(文件级真做,运行时不做)** | 已搬:`PluginDirs`(含 staging/backup 目录)、`PluginStateStore`(plugins.json 原子写)、`PluginManifest`(清单校验/engines/依赖)、`PluginMarket`(索引+镜像+安装包处理),以及 Linux 侧 `PluginHost`(发现/启停落盘/日志/UI 贡献汇总)。不搬:Node 宿主启停、host hello 握手、页面 token、事件总线、热重载、服务/工具注册表——这些只有「进程内真的跑插件」才有意义。 |
| `PluginHostApi.cs` | 15KB | **不移植** | 宿主对**插件侧**暴露的 API 面(launcher.call / ui.register / 凭据 / AI / 工具…)。插件进程不存在时,这一层没有调用方。 |
| `PluginSessions.cs` | 9.4KB | **不移植** | 让插件新建/驱动 AI 会话;依赖插件进程与 Linux 不存在的会话宿主(参考版那套会话在 Python 侧)。 |
| `PluginTools.cs` | 13KB | **不移植** | 插件注册进 AI 的工具表。Linux 的 AI 工具表在 `Core/Launcher/Ai/Tools.cs`(34 项内置),没有插件来源;接上只会得到「点了报未知工具」的死入口。 |
| `PluginLlm.cs` | 12.8KB | **不移植** | 插件直接调模型(流式回传分片给插件进程)。无插件进程 + 无凭据托管方。 |
| `PluginAgentBridge.cs` | 12KB | **不移植** | 插件 ↔ AI Agent 的桥(子代理、产物回读、检查点)。依赖 MCP/子代理/产物那套宿主,P5 已判 Linux 不做。 |
| `PluginHooks.cs` / `LaunchHooks.cs` | 4KB / 4KB | **不移植** | 插件钩子表(`launch.before/after` 等)与启动链上的钩子点。Linux 启动链(`Common/Launch/LaunchCore`)没有插件钩子调用方,注册了也不会被调用。 |
| `DotNetPluginContext.cs` | 575 | **不移植** | 契约类型 `TipCL.Plugins.*` 定义在 **net48 的 `TipCL` 程序集**里,UI 工厂签名是 `System.Windows.FrameworkElement`/`Window`。Linux 侧既不能引用这套契约,也不可能加载按它编译的插件二进制(**程序集标识 + WPF 依赖两条都不满足**);要「真做」只能另立一套与参考契约**不兼容**的新 SDK,而那样没有任何现成插件可用 —— 那属于新造生态,不是移植。因此 `PluginHost.Start` 对 `.NET` 插件**如实报错**(此前只做「程序集加载校验」就把状态标成 running,插件其实从未拿到 `Apply` 回调,属假成功,本轮修掉)。 |
| `JsonRpcPeer.cs` | 11KB | **不移植** | Node 宿主 stdio 上的 JSON-RPC 对端;没有 Node 宿主就没有这条管道。 |
| `NodeHostProcess.cs` / `NodeRuntime.cs` | 6.2KB / 14.3KB | **不移植** | 需要 Node 运行时(参考版会自动下载/内置一个)与 npm(`npm install` 装插件依赖)。Linux 侧不内置 Node、不引第三方宿主包、也不该在用户机器上静默拉 node/npm —— 这是本轮最硬的一条红线。 |
| `SecretStore.cs` | 3.8KB | **不移植** | 插件凭据加密存储(Windows 走机器绑定的加密)。Linux Core 里有 `Platform/ISecretStore` 接口但没有实现,且**没有消费者**(插件进程不存在,没人读这些密钥)。等真的有插件宿主时再落地。 |
| `Sdk/PluginSdk.cs` | 321 | **不移植** | 插件 SDK(`using System.Windows`):WPF 类型进契约,见 `DotNetPluginContext` 的判定。 |
| `Sdk/PluginUiRegistry.cs` | 4.8KB | **不移植** | 登记 .NET 插件的原生 WPF 界面(壳侧再反射取用)。UI 类型是 WPF,理由同上。 |
| `PluginMarket.cs` | 947 → Linux 477(索引/镜像/合并)+ 新增 `PluginInstaller.cs` | **已实现(去掉 npm 与加载两步)** | 下载候选(镜像优先级)→ sha256 校验 → 包内清单 id/version 与索引一致 → engines(`SemVer.Satisfies(APP_VERSION, range)`)→ 依赖检查 → 暂存 `.staging/<id>` → 旧版本挪 `.backup/<id>` → 原子替换 → 写 `plugins.json`(source/market/version/granted/installedAt)→ 失败回滚。**不做**:`npm install`(无 Node)、`SwapAndLoad` 的宿主加载/重启(`restart_required` 在 Linux 的意思是「装了但没宿主可跑」,页面如实标注)。 |
| `AgentPresets.cs` / `ChatEventLog.cs` | 7.9KB / 7KB | **不移植** | 同目录但属于 AI 子系统:插件注册的 Agent 预设、对话事件日志。Linux 的 AI 是进程内最小循环(P5 / D-24),没有预设宿主与事件日志消费者。 |
| `BackendApi.Plugins.cs` | 10.8KB | **不移植** | 后端 RPC 方法面(`plugins_install / plugins_ui / plugins_action …`)。Linux 没有 RPC 桥(D-2),插件页直接调 Core,等价能力已在页面与 `PluginRuntime` 里。 |

### 7.3 本轮做出来的东西(与上面「不移植」互补的部分)

- `Core/Plugins/PluginInstaller.cs`(新):市场安装/更新/本地安装/卸载的真实现(见上表 `PluginMarket` 行),不依赖 NuGet 新包,只用 `Net.Get` + `System.IO.Compression`。
- `App/Common/Plugins/PluginRuntime.cs`:补 `InstallFromMarket / InstallLocal / Uninstall / Reload / ReadLog / UiContributions` 门面。
- `App/Pages/Plugins/PluginUi.cs`(新):界面贡献快照 + `Changed` 事件(外壳与插件页据此重建页签/刷新列表)。
- `App/Pages/Plugins/PluginHostPage.cs`(新):`plugin:<plugin>:<id>` 页面的宿主(网页承载降级为占位 + 浏览器打开)。
- `App/Shell/TitleBar` + `Shell/MainWindow`:动态顶部页签(P8)与 `plugin:` 页面路由。
- 插件页四分段拆分:`PluginsPage.Market.cs` / `.Installed.cs` / `.Settings.cs` / `.Dev.cs`(与 Windows 同名)。

> 用户可见的诚实口径(插件页开发者分段 + 未安装时的空态):**Linux 版可以浏览、安装、更新、卸载、启停插件,但没有内置插件运行时**
> —— Node 插件没有 Node 宿主、.NET 插件的契约基于 WPF/.NET Framework,两者都无法在本进程运行;插件网页界面在浏览器里打开。

## 8. 统一拖放入口(2026-11-12,M4-H P7)

> **2026-11-12 主 agent 复核(真机 + 程序集取证):Linux/X11 上收不到系统拖放。**
> 真机实测:launcher 窗口**没有 `XdndAware` 属性**(`xprop`;同脚本查 Chrome 窗口 `XdndAware(ATOM) = BITMAP` 正常,证明检查有效),
> 而没有该属性时 X11 拖放源不会发 `XdndEnter` → 任何程序都拖不进来(Thunar 真拖一次:拖影落入窗口、松手后应用毫无反应)。
> 根因再往下一层:**Avalonia 11.3.2 的 X11 后端根本没有实现 XDND** —— 对 149 个 Avalonia 程序集做 UTF-16 搜索,`XdndAware` 等协议符号 **0 命中**
> (同一 `Avalonia.X11.dll` 内 `_NET_WM_STATE` 命中 9 次,可证搜索方法有效)。
> 所以这是**平台/后端不支持**,不是本项目的接线问题,也不是自动化脚本问题。下表描述的接入点与识别逻辑**代码层面成立且有测试覆盖**,
> 但在 Linux 上永远收不到 OS 拖放事件;真机上可用的是各页已有的显式入口(「从文件安装」「导入本地文件」「链接安装」)。
> 详见 `DECISIONS.md` D-38 与 `PROGRESS.md` 的 `M4-H R8`。

> Windows 参考版:`Services/FileDrop.cs`(291)+ `Services/FileKinds.cs`(671),由 `PageBase.EnableDrop` 在页面级接入,
> 主窗口再挂一层兜底(`MainWindow.xaml.cs:1175`)。Linux 侧按同一形状落地,逐项差异如下。

| 项 | Windows | Linux | 依据 / 理由 |
|---|---|---|---|
| 接入点 | 页面级 `EnableDrop`(目录/实例库/任务/版本管理/主窗口) | **窗口级兜底**(`Shell/MainWindow`)+ 目录页(带实例上下文)+ 插件页(偏好 `plugin`)+ 实例库 / 版本管理页 | Avalonia 的 `DragDrop` 与 WPF 同形;页面自己的处理会 `e.Handled = true`,窗口层不会重复处理。`Attach` 幂等(页面在 `OnActivate` 里挂也不会重复订阅)。 |
| 识别 | 后缀 + 扫包内成员 + mrpack/CurseForge 清单 + 逐层嵌套探测(`MaxNest=3`/`ScanLimit=4000`) | 后缀 + 扫包内成员 + 三个关键清单标记;不递归解包 | 主类型(插件/整合包/资源包/光影/数据包/存档/模组/皮肤)判定一致;嵌套包结构在 Linux 侧按「认不准就问一次」处理,不静默猜。 |
| 落到哪 | `install_mod / install_shader / install_resourcepack / install_datapack / install_world / install_modpack`(`StartTask`)+ `plugins_install_local` + 皮肤 | 同一批方法名(`IApi`,已经存在)+ `PluginRuntime.InstallLocal` + `set_account_skin` | 安装类一律「后台任务 + 立刻返回 task_id」,装完跳「下载任务」;插件安装后刷新插件页快照与页签。 |
| `wallpaper`(壁纸) | 图片/视频拖进来即设壁纸(`ui_background`) | **不列入受理类型** | Linux 启动页用的是「实例截图 / 内置 hero」背景,没有 `ui_background` 的消费方;`Wallpaper` 服务在 §5 已判 mp4/轮播不做。列一个写了配置却没人读的入口 = 假按钮。 |
| 皮肤 | `AccountPage.EditSkinExternalAsync` | `set_account_skin`(离线账号) | 只对离线账号有效(与参考版一致:正版/皮肤站的皮肤在各自网站上改)。 |

> 可受理类型:`plugin / modpack / mod / resourcepack / shaderpack / datapack / world / skin`(见 `Common/FileKinds.AllKinds`)。

## 9. 真机验收发现的 Linux 侧缺陷与修正(2026-11-13,M4-H R11 / R12)

盒子 `main`(Ubuntu 24.04.5 / XFCE4 / X11)上的验收发现三条**只在真机暴露**的 Linux 侧缺陷,均已修,并按真机差分验收:

| # | 现象 | 根因 | 处置 | 真机 / 测试验收 |
|---|---|---|---|---|
| 1 | 实例库版本行齿轮(提示「版本设置」)一点,**进程直接消失** | `InstanceDetailPage` 构造里遗留的孤儿 `Border` 把唯一的行 `StackPanel` 收成子级 → 首次布局时一个控件两个父级 → `unhandled(UI)` | 删孤儿 Border,行容器改字段 `_rowsHost`(只挂一次),`get_version_rows` 补 `instance`(R9) | `tipcl12`(修复前)`alive=0` + `unhandled(UI)` + 同型异常堆栈;`tipcl13`(修复后)`alive=1`、0 unhandled、实例详情页正常渲染(R11) |
| 2 | 每次启动一条 `[WARN] 启动提示失败: Cannot show window with non-visible owner.`;真实首启时**首启向导与同意门都不出现** | 启动提示在主窗口 `Show()` 之前触发,Avalonia 对不可见 owner 的 `ShowDialog` 直接抛;第一个提示(向导)的异常中断了整串提示 | 先等主窗口可见(状态判断 + 15s 上限)再依次跑两个提示;`--smoke` 不进该路径(见 D-40) | `tipcl12` → 1 条 WARN、向导没出现;`tipcl14` → 0 WARN、向导正常弹出(R12) |
| 3 | 版本行齿轮提示「版本设置」,点击实际打开实例详情页 | 移植时挂错处理器(Windows 是 `VersionSetupDialog`) | 改为与 Windows 对位:`VersionSetupDialog.ShowAsync(inst, id)` + `LoadAsync()`(见 D-41) | 单元测试断言弹的是版本设置对话框、`Dlg.AnyOpen` 且 `Detail(win) == null`(R12) |

> 这三条都**不影响**「页面是否移植完成」的判定(§2 / §4 的页面件覆盖不变),但都属于「无头测试全绿、真机一看就废」的类型:布局类缺陷必须显式推一次渲染 pass 才抓得住(见 D-39),提示 / 弹窗类缺陷必须等窗口真实可见(见 D-40)。
>
> 另有一条平台限制与上述缺陷无关:Linux/X11 收不到系统拖放(Avalonia 11.3.2 无 XDND,见 §8 首段与 D-38),真机可用入口是各页的显式导入按钮。

### 9.1 真机入口逐项复核(2026-11-13 补充,最终产物 `tipcl14`)

| 入口 | 真机结果 | 证据 |
|---|---|---|
| 实例库 → 版本行齿轮(提示「版本设置」) | 打开**「版本设置 · 26.3」对话框**(版本隔离 / 独占内存 / JVM 参数 / 垃圾回收器 / 直连服务器 / 端口 / 启动前命令 + 取消/保存),`unhandled=0` | `_cmp/r14-gear.jpg` |
| 实例库 → 「实例详情」chip | **实例详情页正常打开**(「26.3 / 1个版本」+ 分段条 + 空态),`unhandled=0` | `_cmp/r14y-after.jpg` |
| 8 页 `--smoke`(冷启动,8 页 ×2 批) | **16/16 exit=0**,同批 8 张 PNG 两两不同,`$TIPCL_HOME/logs` **0 WARN / 0 ERROR** | `_cmp/r13-box-out.txt` |

> 定位方法见 D-43:真机点击必须用**窗口相对坐标**(`import -window $WID` 量位置 + `xdotool mousemove --window $WID x y click 1`);用整屏截图反算绝对坐标会偏 20~50px,连点四轮都可能全部落空。
>
> 冒烟截图的确定性已按 D-42 加固(捕获前推渲染 pass + 丢弃首帧 + 最小 settle);R8 那条「冷启动首批把上一页帧写进新文件名」在本轮冷启动 16 次里**未复现**,属「已加防御性确定性、未复现原现象」。

### 9.2 「缺口 B」已补:UiPrefs(折叠态 / 版本收藏 / 下载页当前实例)落 config.json(2026-11-13,R15)

| 项 | Windows | Linux(补前) | Linux(补后) | 依据 |
|---|---|---|---|---|
| 分组折叠态 | `wpf-ui.json` 的 `Expanded["lib:"/"vm:"+key]` | 进程内静态表(重启即丢) | `config.json` 的 `ui_prefs.expanded`(两种前缀都接) | D-45 |
| 版本收藏 | `wpf-ui.json` 的 `VmFavorites`(实例 → 版本 id 数组) | 进程内静态表 | `ui_prefs.vm_favorites`,实例库与版本管理**共用同一张表** | D-45 |
| 下载页当前实例 | `CatalogInstance` | 每次进页都回到第一个 | `ui_prefs.catalog_instance`(记录里的实例不存在时退回第一个) | D-45 |
| `LastUser` / `Notes` / `Dark` / `Anim` | 同一个 wpf-ui.json | **本来就有等价键**(`last_user` / `ui_notes` / `ui_dark` / `ui_motion`),不重复造 | 同左 | 避免两个真源 |
| `AnimScale` / `SideWidth` / `SideFrost*` | wpf-ui.json | Linux `Motion` 无缩放因子;v2 外壳无侧栏 | **不适用**(不造没人读的键) | D-45 / §5 |

> 落点为什么不另造 `wpf-ui.json`:Linux 侧只有一个用户配置文件 `config.json`,界面键(`ui_dark` / `ui_motion` / `ui_layout` / `ui_notes` …)都在里面;再开一个文件会让「配置在哪」有两个答案,也拿不到既有的原子写与迁移逻辑。

### 9.3 启动页补全:分辨率字段 + 线上新闻(2026-11-13,R16)

| # | 项 | 补前 | 补后 | 依据 |
|---|---|---|---|---|
| 1 | 启动页「分辨率」入口(§2 第 52 行的 🔷) | 无入口;启动写死 `width=854 / height=480` | 配置浮层「更多设置」里的 `宽 × 高`(Java → 内存 → 分辨率 → 直连),与设置页「默认分辨率」共享 `config.json` 的 `default_resolution`;启动页取框里的值,联机直连 / 崩溃页重试按该键兜底 | D-46 |
| 2 | 线上新闻(`cached_news` / `fetch_news`) | 空桩 `return new List<object>();`(启动页永远「暂无新闻」) | 真抓 mojang 新闻源(v2 → 老版兜底)+ 落盘缓存 `cache/news.json`,最多 12 条、正文 160 字截断,行字段与参考版一致 | §2 第 52 行 |

**同期修掉一条只在点开浮层时才暴露的移植缺陷**(与 §9 表同性质,但属 Linux 侧新发现):

| # | 现象 | 根因 | 处置 | 验收 |
|---|---|---|---|---|
| 4 | 启动页点齿轮:进程抛 `InvalidCastException`,**「启动配置」浮层(实例 / 版本 / 用户 / 内存)一个都打不开** | `LaunchPopups.OpenConfigPopup` 里 `_configBody = (Border)BuildConfig();`,而 `BuildConfig()` 返回的是 `Control`(实际 `StackPanel`) | 字段类型改 `Control?`、去掉强转 | `App/LaunchResolutionTests.ConfigPopup_OpensAndExposesTheResolutionField`(先红后绿,红色证据 `_cmp/r16-test-popup-red.txt`);主 agent 12 场冒烟含该页 0 WARN |

**有意保留的差异(不是缺陷,别当回归修掉)**:Linux `News.Fetch` 在「两条源都失败且缓存为空」时返回空列表,而 Windows 版会把最后一个异常抛出(`Core/Launcher/News.cs:89-92`)。原因见 D-47:Linux 侧的调用方是启动页那条 fire-and-forget 的新闻行,抛出等于整页崩。

> 真机(盒子)上的端到端验证——包括点开这个浮层看「分辨率」是否可编辑、真的拉起游戏窗口——并入 R21 的最终 AppImage 验收。
>
> **R21b 补记(2026-11-13,见 §9.7 / §9.8)**:这两条在 R21 的真机验收里落实了 —— 「点开这个浮层看「分辨率」是否可编辑」**已在真机证到并落盘**(在浮层里改成 1600 × 900,`config.json` 的 `default_resolution` 逐次变化,最后复原 `854 × 480`;设置页那条路径另测到 `1920 × 1080`);但同一轮真机先撞出两条**只在真机暴露的 P0** —— 对话框点任何按钮都不关窗(§9.7)、浮层里除文字外的控件全画不出来(§9.8)—— 都已修掉并在真机复验。**「真的拉起游戏窗口」仍未做**(判据口径见 D-35):只做到「按 `default_resolution` 拼出启动命令并过 CLI 预检」。

### 9.4 权限确认卡的「为什么」补上了(2026-11-13,R19 / R19b)

| # | 项 | 补前 | 补后 | 依据 |
|---|---|---|---|---|
| 1 | 确认卡上的裁决理由(参考版 `Permission.cs:187-252` 的 12 句) | `ToolRunner.Decide` 只给 `allow/ask/deny`;调用点把**标签**塞进 label 槽、第三参恒空 → 卡片那行从不渲染,旧注释把缺陷写成「有意留空」 | `PermissionVerdict(decision, reason)` 一次产出;`Confirm(call, label, reason, preview)` 一路传到 `ConfirmCard`;11 句与参考版逐字相同,`default/custom` 按 `risk == "high"` 分句(**裁决未变**) | D-48 |
| 2 | `ai_permission_dont_ask` | Linux 的 `Config` 写着默认值,**全仓无消费点**(参考版在 `Agent.cs:571` / `:1149-1152` 消费) | `ToolRunner.ApplyDontAsk`:需要确认的写操作直接拒绝,理由「用户开启了「不询问」模式」(只作用于 `ask`) | D-48 |
| 3 | 被拒的步骤行 | 泛泛一句「当前权限档位不允许」 | 「已拒绝:&lt;裁决理由&gt;」,由 `AiPage.Summarize` 显示(照参考版把 reason 写进 denied 工具状态);**模型可见文本未变** | D-48 |
| 4 | 参考版 `Permission.PermissionNote`(`Permission.cs:256-276`:把当前档位写成 6 句 `[权限设置] ...`;消费点 `Agent.cs:348` / `:368`) | 模型**不知道**自己处在哪个档位,只能从「刚才那次操作被拒了」反推 ⇒ 两种坏行为:该直接动手时反复问、被拒之后换个工具名重试同一件事 | **已补(R21d)**:`ToolRunner.PermissionNote()` 与参考版 6 句**逐字对位**(独立核对:从参考仓源码正则抽字面量比 SHA256,**6/6 相等**),作为独立段落插在系统消息的**运行时上下文之后、长期记忆之前**;崩溃修复会话同位置一并注入;归一化提出成 `ToolRunner.NormalizeMode`,裁决 / 提示 / 自检共用一份;`--ai-selftest` 52 → **54** 项 | D-57 |

> **仍未做(如实)**:「不询问」在 Linux 侧是**独立开关**(`ai_permission_dont_ask`,见 D-48)而不是档位名,所以那句 dontAsk 提示只在「开关打开 && 档位不是 `yolo` / `plan`」时用,其余档位保持原句 —— 这样提示与真实裁决自洽。
>
> **如实登记的两处偏离**:1**别名档位只影响提示文本、不影响裁决** —— 参考版的 `NormalizePermissionMode` 归一化结果**也用于裁决**(`auto` / `acceptEdits` / `autoEdit` / `build` 等 = 写操作直接执行),而 Linux 的裁决只认 `default` / `acceptEdits` / `plan` / `yolo` / `custom` 五档,这些别名走默认分支 = 每次问。只有手改配置或从 Windows 迁过来的配置才会撞上,届时模型读到的描述(「写操作会直接执行」)与真实裁决(ask)不一致。修它必须让 Linux 的裁决也认别名 —— **那是改裁决,没批之前不动**;2`ai_confirm_writes` **刻意不读**:Linux 的 `Decide` 根本不看这个键,读了它这句话就会与真实裁决对着说。
>
> 另一处**有意不搬**:参考版 `PermissionResult` 还带 `rule_id`,Linux 的「一直允许」是按工具名记的进程内集合,没有「按规则记住」的界面入口,搬 `rule_id` 是无消费方的死重(见 D-48)。

### 9.5 Markdown 链接从「画得像」变成「真能点」(2026-11-13,R17)

| 项 | 参考版 | Linux(补前) | Linux(补后) | 依据 |
|---|---|---|---|---|
| 回复正文里的链接 | WPF `Hyperlink`(`MdView.cs:367`):可点、点开浏览器、悬停有光标 | 主题色 + 下划线的 `Span` —— **不可点、无光标、无 URL 提示**(D-22 记为平台限制) | `InlineUIContainer` 包一个真 `TextBlock`:手型光标 + URL tooltip + 点击开浏览器(`xdg-open` / `open` / `ShellExecute`) | D-49 |
| 行内码底色 | WPF `Run` + `Background`(`MdView.cs:340`:`SetResourceReference(TextElement.BackgroundProperty, "B.Hover")`) | 「Avalonia 的 `Run` 没有 `Background`」→ 退成等宽 + 主题色近似 | **已补(R21c)**:那句平台前提在 Avalonia 11.3.2 上**不成立** —— `TextElement` 有 `BackgroundProperty`(Run 是 Inline → TextElement)**而且真会被画出来**(无头 Skia 抓帧数像素:改动前 0 个 `B.Hover` 像素、改动后 100+)。于是照参考版原样:同一个属性、同一个键 `B.Hover`(浅 `#F2F2F2` / 深 `#2A2A2A`,上压 `B.AccentText` 对比 4.7:1 / 7.3:1)。**没有**走 `InlineUIContainer` 包 Border:外壳按 natural 宽度 arrange,320 宽段落里 80 字符标识符外壳宽 536 会被裁且不参与折行,而 `Run` 照常折行、底色只画不改布局(见 D-56) | D-56 |
| 链接悬停态 | WPF `Hyperlink` 自带 MouseOver 变色 | 无(下划线常亮) | **已补(R21c)**:`PointerEntered` / `PointerExited` 在 `B.AccentText` ↔ `B.Accent` 之间换前景色(浅色档 5.3:1 → 3.5:1、深色档 8.4:1 → 6.1:1,两套主题都看得出变化又都还看得清);用**重新挂 `Res.Bind`** 而不是直接赋画刷,离开时能干净回到主题色(实测重挂会顶掉旧绑定)。手型光标 / URL tooltip / 点击开浏览器(R17 那三条)一字未动 | D-56 |

> R21c 补记:§9.5 表里最后两条「仍未做」都已收口(D-56)。取证方式是**抓帧数像素**而不是「属性挂上没挂上」:改动前整帧里 `B.Hover` 像素 = 0,改动后 = 100+(并且先断言「没写行内码的对照段落里一个 `B.Hover` 像素都不该有」,免得把表格线 `B.LineSoft` `#F0F0F0` 误数进来)。另外如实记一条覆盖缺口:**AI 页冒烟帧改动前后逐位相同**(`smoke.png` 37,553 B,sha256 一致)—— 那份冒烟内容里没有行内码,所以这条特性只有单测与抓帧用例覆盖,冒烟覆盖不到。

### 9.6 导入多选 + 数据包装进存档(2026-11-13,R18)

**一、多选导入(补上「参考版能多选、Linux 只能单选」的差)**

| 入口 | 参考版 | Linux(补前) | Linux(补后) |
|---|---|---|---|
| 下载页「导入本地」(`CatalogPage`) | `Dlg.PickFiles`(`Multiselect = true`,`CatalogPage.cs:952`) | 单选 `PickFile`(`CatalogPage.cs:856`) | 多选:逐文件 `install_*`(path=文件 / source=本地 / name=文件名),多文件时追加汇总「导入 N 个:成功 X,失败 Y」+ 逐条「文件名:原因」 |
| 反馈页「添加附件」(`FeedbackView`) | 同上(`FeedbackPage.cs:167`) | 单选(`FeedbackView.cs:224`) | 多选:逐文件 `stash_upload` + 同样的汇总口径 |
| 模组对话框「导入模组」(`ModsDialog`) | 同上(`ModsDialog.cs:36`) | 单选(`ModsDialog.cs:141`) | 多选:逐文件 `install_mod` + 同样的汇总口径 |

> 口径:**取消 = 返回空数组**,三处都「什么都不做、也不报错」;**单选(N=1)逐字沿用改动前的提示 / 状态 / 重载**,只有 N>1 才追加汇总(`Common/ImportBatch.cs` 里 `Total <= 1` 直接返回空串,物理上不可能给单文件加提示)—— 既有测试一条没改。

**二、数据包装进存档:三条参考版缺陷上去之后,这个入口才真的有用**

| # | 补前(Linux = 参考版逐行复制) | 补后 | 依据 |
|---|---|---|---|
| 1 | 「装进哪个存档」的 `extra["save"]` 传下去后**没人读** → 选了等于没选(§6 第 8 行) | 装完就地复制进该存档的 `datapacks/`,失败只记一条带原因的日志 | D-50 |
| 2 | 源路径硬编码 `<实例>/datapacks` → 隔离版本下「列表里有、点下去说没有」(§6 第 9 行) | 版本目录(`<实例>/versions/<版本>/datapacks`)优先、实例根回退 —— 与列表同一个目录 | D-50 |
| 3 | 存档不存在时凭空造只含 `datapacks/` 的空壳存档(§6 第 10 行) | 抛 `存档不存在: X`,不建任何目录 | D-50 |

**保持不动(两版一致,不算遗漏)**:同名文件就地覆盖(`overwrite: true`,`LocalFs.Copy2`)不产生副本;目标 `<存档>/datapacks` 目录不存在就创建;目标路径与版本隔离游戏目录的算法(`<实例>/versions/<版本>` vs `<实例>`)两版逐行相同。

**未在真机验证(如实)**:系统文件对话框本身(GTK/portal 的 `AllowMultiple`)与下载页「装进存档」下拉**都没在真机点过** —— 无头环境点不了系统对话框;这两处只用 API 契约测试覆盖(`install_datapack` 带 `extra.save` → 等任务 → 断言文件既在实例 `datapacks` 也在存档里,隔离/非隔离两条)。存档管理页的「装数据包」列表**仍按实例根**列(与参考版一致,未改):隔离版本只能从实例共享数据包里选,要让列表也列版本目录需给 `get_installed_datapacks` 带上 `version`,属独立条目。

### 9.7 对话框点按钮不关窗:移植时漏掉的一步(2026-11-13,R21b 真机发现;移植代理修,主 agent 复核)

真机首启的「同意门」点「同意」/「暂不同意」**窗口纹丝不动**(只能 `xdotool windowclose` 硬关,调用方还因此把结果当成「取消」写进配置)。三条同源缺陷一并修掉:

| # | 现象 | 根因 | 处置 | 验收 |
|---|---|---|---|---|
| 1 | `Ask` / `Choose` / `Prompt` 点任何按钮都不关窗,`await ShowDialog(host)` 永不返回 | Windows 参考版用 WinForms 图层:设 `DialogResult` / `layer.Close()` 会顺带关掉图层。移植成 Avalonia 独立 `Window` + `ShowDialog` 后**只 `TrySetResult`、没有人关窗**;`win.Closed` 上那条「兜底 = 取消」还会把结果覆盖成取消 | 新增私有 `Finish<T>(tcs, value, win)`:先 `TrySetResult`(**返回 false 即「已收尾」直接返回**,键盘 Enter 同时触发输入框与默认按钮时不会重复关窗),再 `win.Close()`;三处点击全部改走它 | 真机:点「同意」→ 对话框消失、`config.json` 写 `feedback_consent: true`、进程存活、无功能异常(唯一噪声见 PROGRESS 本轮遗留:XFCE 缺 AppMenu DBus 服务的无主任务异常);`SMOKE_PROBE=dialog-close` 三种收尾(点确定 → `Task=True` / 点取消 → `Task=False` / 只关窗 → `Task=False`)exit 0;`App/DlgCloseTests` 6 条 |
| 2 | `Dlg.Prompt` 只有「确定」,用户没有反悔出口(Esc 也无效) | 移植时 footer 只放了确定按钮;参考版是 `Ui.H(8, button2, button)` —— **取消 + 确定** | 补「取消」(→ `null`)+ `cancelBtn.IsCancel = true`,`box` 的 Enter 走 `Finish` | 同上(单元测试覆盖 Prompt 的确定 / 取消两条路) |
| 3 | `Dlg.Alert` 多渲染一个「取消」,关掉还被当成取消 | 实现是 `Ask(..., cancel: null)`;参考版 `Alert` 只有**一个「知道了」按钮**,内部结果**恒 `true`**(Esc / 直接关窗都算「知道了」) | `Alert` 改走私有 `AskCore(..., cancelLabel: null, dismissIsOk: true)`;**`Ask` 的公开签名一字未改** | 6 个调用点(`CatalogPage` / `VersionManagePage` / `SavesDialog` / `ModsDialog` / `FirstRunWizard` / `FeedbackView`)全是 `await Dlg.Alert(...)`,返回类型本就是 `Task`、值不外露 |

> 顺序不能反:`win.Closed` 上挂着「兜底 = 取消」的 `TrySetResult`,而 TCS **只认第一次设置** —— 先 `Close()` 会让兜底抢答(点「确定」却拿到 `false`)。`Finish` 里「先设结果、再关窗」正是为此。
>
> `Panel` / `OpenPanel` 不受这条影响(它们本来就是 `close.Click += … win.Close()`),只补了 `close.IsCancel = true`(Esc = 关闭);**不设 `IsDefault`** —— 面板体里常有输入框,Enter 不该等于「关闭」。

### 9.8 启动配置浮层里的输入控件全画不出来:`Popup` 没挂进页面树(2026-11-13,R21b 真机发现;移植代理修,主 agent 复核)

**现象(真机)**:启动页点齿轮,浮层**打开了**(标题「启动配置」、分组标签、`4096 MB`、`×` 都在),但**实例 / 版本 / 用户 / Java / 内存滑块 / 分辨率两个输入框一片空白**;同一份 AppImage 的主窗口里 ComboBox / Slider / TextBox 全部正常(设置页「通用」逐项可见)。

**根因**:`MakePopup` 只 `new Popup { PlacementTarget, Placement, IsLightDismissEnabled, Child }` 然后 `IsOpen = true`,**从没挂进任何视觉 / 逻辑树**。Avalonia 11 的**隐式 `ControlTheme`**(FluentTheme 里的 `{x:Type TextBox}` / `{x:Type ComboBox}` / `{x:Type Slider}`)走 `TryFindResource`,要沿「控件 → 逻辑/可视父链 → TopLevel → `Application.Styles`」查宿主链;没挂树的 Popup 没有宿主 ⇒ 模板为空、量出 0×0。而**显式**赋 `ControlTheme` 的控件(`Ui.Btn` / `Ui.Switch` / `Ui.Multi`)与走 `Application.GetResourceObservable` 的 `Res.Bind` 都不需要这条链,所以照常显示 —— 「显式主题画得出、隐式主题画不出」正是宿主链断掉的指纹,与真机截图逐条吻合。

**修法**:`MakePopup` 把 `Popup` 加进页面根面板(`Content is Panel host → host.Children.Add(p)`);拿不到面板时记一条 WARN,**不静默退化成「浮层又能点又空白」**。一份改动覆盖启动页三个浮层(它是全仓唯一的 `new Popup`)。

**结构级证据(`SMOKE_PROBE=popup-theme`,主 agent 独立复跑 exit 0)**:

| 组 | 是什么 | 修复前 | 修复后 |
|---|---|---|---|
| A | 同形裸控件挂进主窗口已有面板 | 5/5 有模板 + 有尺寸 | 5/5 |
| B | **真实**「启动配置」浮层里的数据控件 | **0/10**(模板=无、Bounds=0×0) | **10/10**(如 分辨率宽/高 = 74×45) |
| C | 同形控件放进**不挂树**的 Popup(修复前的形状) | 0/5 | 0/5(**故意保留的反证组**) |
| D | 同形控件放进**挂树**的 Popup | 5/5 | 5/5 |

**不占位、不动布局(主 agent 单变量对照)**:把 `LaunchPopups.cs` / `SmokeRunner.cs` 还原到 `HEAD` 构建后跑 4 次冒烟,再装回修复版跑 4 次,8/8 张 `SMOKE_PAGE=launch` 的 `smoke.png` **逐位相同**(`070d6ad6682d6a86…`,564,149 B)。

**真机像素级证据(R21b 最终 AppImage)**:`_cmp/r22-popup-fixed.png`(实例 `.minecraft` / 版本 / 用户 `Player` 占位 / 更多设置 / 微软登录 / 皮肤站)、`_cmp/r22-popup-more-fixed.png`(展开后 Java `自动选择`、内存滑块 + `4096 MB`、分辨率 `854 × 480`、直连 `ip:port` 占位);浮层内改分辨率 → `config.json` 的 `default_resolution` 真的变了:`[854,900] → 1600 → [1600,480] → 900 → [1600,900] → 复原 [854,480]`(见 §9.3 的真机补证)。

> **真机观感的一处变化(如实记,不是缺陷)**:浮层仍然是一个**独立的 override-redirect X 窗口**(`xdotool` 能看到 374×493 的无名窗口),但它的控件现在挂在主窗口的宿主链上;点浮层外(如顶部「设置」页签)**light-dismiss 正常关闭**且该次点击照常落到页签上。此前那种「浮层里除 TextBlock 外一片空白」的形态已不复现。

### 9.9 `TaskStore` 的队列竞态:参考版同款的坑,Linux 侧先修了(2026-11-13,R21b)

**发现方式值得记一笔**:不是真机点出来的,是**最终验收把 `dotnet test` 连跑两遍** —— 第一遍绿、第二遍炸,而且炸在一条看起来完全无关的用例上(多选导入):

```
失败 TipCL.Tests.App.MultiPickImportTests.Catalog_ImportLocal_TwoFiles_ImportsEach
System.InvalidOperationException : Collection was modified; enumeration operation may not execute.
   at System.Linq.Enumerable.Count[TSource](...)
   at TipCL.App.Common.TaskStore.get_RunningCount() in Common/TaskStore.cs:line 114
   at TipCL.App.Pages.TasksPage.Sync(TaskRow row) in Pages/TasksPage.cs:line 202
```

**性质**:`TaskStore._rows` 是普通 `List<TaskRow>`,而 `Emit` 的设计(「任务工作体跑在线程池上」)决定了它会被两条线程同时碰 —— 工作线程增删、UI 线程遍历。**参考版是同一份无锁写法**(Windows `Services/TaskStore.cs:9/13/15/17/27`),所以这不是移植引入的回归,而是**参考版同款的缺陷**(记进 §6 第 11 行);Linux 侧先修,参考仓库不动(D-54)。

**修法与证据**:`Rows` / `RunningCount` / `Newest` / `Get` / `Add` / `ClearFinished` / `Remove` 全部收进一把私有锁,`Rows` 改为返回 `ToArray()` 快照;`TaskRow.Append` 的 `StringBuilder` 另加一把行内锁并新增 `LogText`(页面原来是在 UI 线程 `row.Log.ToString()`,撞工作线程的 `Append`,同一类问题、更隐蔽)。

| 项 | 单变量证据 |
|---|---|
| 新回归用例 | `TaskStoreTests.Concurrent_Mutations_And_Reads_Do_Not_Throw`:4 个工作线程 × 120 轮 `task_added → progress → log → finished → Remove`,UI 线程同时读角标 / 快照 / `Newest` / `Get` / 每行 `LogText` |
| 红(把 `TaskStore.cs` 单独还原到改动前) | **3/3 失败**,异常就是 `Collection was modified`,栈顶 `TaskStore.Get` ← `Handle` |
| 绿(装回加锁版) | **3/3 通过**(每次约 110 ms) |
| 全量 | `dotnet test` 连跑 **3 遍全绿**:404 = 403 通过 + 1 跳过 + 0 失败(上一轮基线 403 = 402 + 1) |

> 附带记一条工具坑(与源码无关,但会让人误判结论):`Copy-Item` 会把源文件的 LastWriteTime **一起带过去**,于是「把备份拷回来」得到的文件可能比上一次构建还旧 ⇒ MSBuild 判定「最新」直接跳过编译,`dotnet test` 仍在跑旧 DLL —— 症状是「加锁版」还报同一处 `FirstOrDefault`,而那一行源码早已不存在。判断「到底在跑哪份代码」要看异常栈里的行号与符号,不能只看命令成功。
