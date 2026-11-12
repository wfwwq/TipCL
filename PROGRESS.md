# PROGRESS — TipCL Linux 版 M1

> 工作包状态表。各代理收尾后在自己行内更新状态；详细报告见 `progress/M1-<包>.md`。
> 共享文件编辑纪律见 GOAL.md §6：各包只追加自己的小节，禁止重写他人内容。

| 工作包 | 内容 | 独占地盘 | 状态 | 报告 |
|---|---|---|---|---|
| M1-A1 ✅ | 工程脚手架（git init、sln、App 空壳、根文档、tools 骨架） | 仓库根、src/TipCL.App、tools/ | 已完成（本包范围；整体 build 待 A4 落地后复验） | progress/M1-A1.md |
| M1-A2 ✅ | 设计令牌与主题库（Palette 明暗、Controls、UiHelpers、ThemeGalleryPage） | src/TipCL.App/Themes/、src/TipCL.App/Ui/ | 进行中 | progress/M1-A2.md |
| M1-A3 ✅ | 窗口外壳与导航框架（Shell/：MainWindow、TitleBar、SideNav、NavRegistry） | src/TipCL.App/Shell/、Pages/ | 进行中 | progress/M1-A3.md |
| M1-A4 ✅ | Core 业务复用层（Windows 版 Core/Services 跨平台搬运 + 复用清单） | src/TipCL.Core/ | 进行中 | progress/M1-A4.md |
| M1-A5 ✅ | 测试与构建基建 + 冒烟（xunit、build 脚本、docs/、smoke 文档化） | tests/TipCL.Tests/、tools/（充实）、docs/ | 进行中 | progress/M1-A5.md |

## 状态约定

- 待开始 / 进行中 / 已完成（自证 build/test 通过后才能标"已完成"）。
- 冲突与依赖申请写各自 progress/M1-*.md，由主 agent 裁决。

## 2026-11-08 M1 集成验收（主 agent）
- 五个工作包全部完成：A1 7979b2b / A4 7d3dc30 / A3 415bd95+845bb75 / A2 adb558b / A5 由主 agent 代为收尾。
- M2 先遣侦察 3 份报告入库：docs/m2-recon/R1/R2/R3（commit 7e07387 / 30b7358 / 29b2292）。
- 集成期修复见 DECISIONS D-6；整体 sln 构建 0 警告 0 错误，测试 34/35（1 个 Skip 为占位）。
- M1 验收标准（GOAL §4）逐条核对：①构建 ✅ ②测试 ✅ ③外壳窗口（1180×740、标题栏/页签/主题切换/窗口按钮、侧栏分组导航）✅ ④Core 复用层 + 复用清单（progress/M1-A4.md）✅ ⑤--smoke 出图 ✅（明暗两帧）⑥tools/build.sh+ps1 ✅ ⑦文档（PROGRESS/DECISIONS/交接必读/README）✅。
- M2 开局共识（来自侦察报告）：先做 R0 公共件（IApi 门面对齐 BackendApi 契约 + TaskStore + PageBase）与 Core 续搬（Ai/FileMemory、Sync、Plugins 市场文件），再并行铺页面；图标方案（沿用 TTF glyph vs 嵌 SVG）待用户拍板。

## 2026-11-08 M2 集成验收（主 agent）

- 六个工作包全部完成：B0（12b9d92+7ad24da，IApi 20 方法/TaskStore/PageBase/ThemeService/IconLib 51 图标）、B2（846054a 实例库+详情）、B4（f9b9b6a 插件页+布局自定义框架）、B1/B3/B5（代码落盘后代理中断，主 agent 代为收尾：e4f6936/c91c7a8/fd37c6f）。
- 集成期修复（934→0 错误）：全页面 UiClass 别名（Ui 双重名）、BoxShadow 构造、TextElement/Path/Config/Py 缺 using、_statusRow 类型、toggle/send 自引用两步赋值、ComboBox.Text/IsEditable→SelectedItem、LauncherConfig.data.ContainsKey、PageBase.ShellAttach 系替换 protected 生命周期直调、IApi.ToDict 公开。
- 验收：整体构建 0 警告 0 错误；测试 66/67（1 Skip 冒烟占位）；SMOKE_PAGE=library/download/plugins/settings 逐页冒烟人工核对通过，SMOKE_DARK 深色正常。
- 遗留给 M3：启动链路（真正拉起 Minecraft）、B2 报告的 9 个 IApi 方法补齐、启动按钮内版本号视觉微调、29 条编译警告清理（多为 nullable 与过时 API）、安装页 csproj 资源项合入确认。

## 2026-11-09 M3 验收（主 agent；用户指示本轮起禁用子代理）

- 工作包：C0 启动链路与 Java 管理（c2e4494）、C1 插件宿主与 AI 运行时（856f75b）、C2 打包交付（主 agent 收尾提交）、C3 总验收（主 agent 亲自执行）。
- C2 与 C0/C1 的首次运行均被手动中断，产物落盘后由主 agent（C2）或重启代理（C0/C1）接续完成；C3 起全部由主 agent 直接执行。
- 验收：全量重建 0 警告 0 错误（清理 29 条 nullable 警告）；测试 108/109（1 Skip）；冒烟矩阵五项目通过；版本统一 0.1.0-m3。
- 交付物：dist/linux-x64/TipCL.App（42.5 MB 单文件）、dist/win-x64/TipCL.App.exe（43.3 MB，冒烟通过）、tools/package.ps1|sh、tools/AppImage/、docs/DELIVERY.md。
- M3 遗留见 progress/M3-C3.md §6。

## 2026-11-09 真机验证（主 agent，盒子 Ubuntu 24.04）

- 通过临时文件中转把 linux-x64 单文件送进 Ubuntu 24.04 真机：无头冒烟矩阵 5 项全过，真实 GUI 窗口启动、页签导航、明暗切换均验证。
- 真机发现并修复缺陷（commit 见下）：`SetDark` 未同步 Avalonia `RequestedThemeVariant`，导致「先浅色构建、后切深色」的页面文字不可读；同时补启动读回已保存主题。修复后真机复验通过。
- 修复后重发双端产物：linux-x64 44,641,606 B / win-x64 45,487,941 B（win 冒烟通过）。

## 2026-11-09 M3 回归修复(主 agent,续上一次会话)

- 页面生命周期(下载页版本列表恒空的根因):`MainWindow.Navigate` 现在调用 `PageBase.ShellDetach/ShellAttach`,OnCreate/OnActivate 才真正执行;LaunchPage 的临时 OnAttachedToVisualTree 首载清理掉,统一改回 OnCreate。
- 跨线程崩溃:InProcessApi.RaiseEvent 与 TaskStore 的 Added/Updated/Changed 统一经 Dispatcher.UIThread 封送,下载 worker 不再因非 UI 线程碰控件打断安装任务。
- 下载页远程清单超时:FetchTimeoutMs=9000 由 CannotCancel 的假取消改为 Task.WhenAny UI 兜底——超时只提示「仍在后台刷新」,结果到达后照常渲染,慢网不再丢清单。
- 文件日志:新增 src/TipCL.App/Common/AppLog.cs,写入 $TIPCL_HOME/logs/tipcl-YYYYMMDD.log 并镜像 Console,接管 AppDomain / TaskScheduler / UI 线程未处理异常,同时替换 Avalonia Logger.Sink;真机 nohup 场景 app*.log 为 0 字节时可用该文件排查。
- 测试:移除未跟踪的联网调试探针 tests/TipCL.Tests/App/ProbeInstall.cs(会导致 dotnet test 长时间挂起);结果 108 通过 / 1 跳过 / 0 失败,构建 0 警告 0 错误。
- 冒烟复验:启动页(实例/版本/内存/新闻)与下载页 922 条版本列表均正常渲染(smoke-launch-after.png / smoke-dl-after3.png)。
- Windows 开发机已知噪声:WriteJson 写 cache 的 .tmp 偶发 [Errno 13](疑似 Defender 占锁),清单自动回落旧缓存;Linux 真机未复现,暂不改 Core 行为。

### 2026-11-09 续:真机 GUI 安装链路打通(主 agent)

- 真机(Ubuntu 24.04 盒子)点「开始安装」后进程直接消失,真因有二,均已在盒子复现并修复:
  1. 任务体读控件:InstallPage.InstallAsync 的 StartTaskAsync 回调里读 `_of.IsChecked`/`_liteRow.IsVisible` → Avalonia VerifyAccess 抛「Call from invalid thread」,任务瞬间失败且无日志。修法:所有控件状态在 UI 线程先取值,再进任务体(SettingsPage 手机同步卡同类问题一并修,含 PhoneLink 回调回 UI 线程)。
  2. 进度事件递归:页面上的 Pump 把 BridgeEvent 里的 progress 又喂回 progress.Report,Report 再发事件 → Report→事件→Pump→Report 无限递归,栈溢出(SIGRTMIN)直接杀进程。修法:Pump 忽略带 taskId 的事件(那是自己 Report 出去的回声)。
- 任务可观测性:StartTaskAsync 现在把任务开始/完成/失败(含异常栈)写进文件日志,Core 的 log 事件也落盘——此前失败是完全静默的。
- 真机验收(新产物 tipcl11):下载页 → 26.3 → 开始安装 → 14:38:20 任务开始 / 14:38:41 任务完成,594 MB 落盘到 `~/.tipcl/.minecraft/26.3/versions/26.3`,进程存活、实例库页显示「26.3 · 1 个版本 · 正式版 26.3 · 完全独立」。
- 新增回归测试 `tests/TipCL.Tests/App/NavigateTests.cs`(每页签导航 + 回「启动」),并抽出 `TestApp.Ensure()` 供无头初始化复用;测试 109 通过 / 1 跳过 / 0 失败。

## 2026-11-10 M4-A 外壳对齐 Windows v2(主 agent)

- **纠偏**:上一轮「必须造 6 分组侧栏」的差距判断有误——`NavModel.TopKeys/GroupedNav` 只服务排序与 `NavDump`,不是 UI 结构。真实 Windows v2 外壳**无侧栏**,是「顶部 5 页签 + 设置子导航」;详见 DECISIONS D-11 的三重证据。
- 改动:删 `Shell/SideNav.axaml(.cs)`;`NavRegistry` 收敛为 5 页签;`MainWindow.axaml` 改 900×550 / 标题栏 46px / 单列全幅 + `B.PageWash`;`TitleBar` 补 AI 药丸钮与账号钮(默认「未登录」)、`Tab.Top` 去悬停底块;`MainWindow.axaml.cs` 接线 `AiClicked/AccountClicked` 并整理 `GetPage` 键。
- 清理:删死占位 `PlaceholderDashboard/Java/About`,新增 `PlaceholderAi`。
- 验收:`dotnet build` 0 警告 0 错误;`dotnet test` 109 通过 / 1 跳过 / 0 失败;无头冒烟浅色帧 `smoke-shell-light.png` 与 `_eval_shots/final/launch-light.png` 结构一致。
- 下一步:启动页内容细节对齐(如版本/Java/内存胶囊行)、`tasks` 页真实化、长尾页与 AI 对话子系统(见 `docs/PARITY.md`)。

### 2026-11-10 M4-B 启动页补 Java 支持(主 agent)

- 缺口:Linux 启动页**完全没有 Java**(无下拉项、无 Java 胶囊)。参考版有「更多设置 → Java / 内存 / 分辨率 / 直连」四字段与 chip 行的 Java 标签。
- 改动:
  - `Common/Api.cs` 补 4 个契约方法(照 Windows `Core/BackendApi.cs`):`java_combo_options`(首项「自动选择」+ 去重已装 Java + 实例已存但不在列表者标注「已保存 (…)」)、`java_combo_label_for`、`get_instance_java`、`set_instance_java`(经 `LaunchCore.NormalizeJavaPref` 归一化)。
  - `Pages/Launch/LaunchPage.cs`:加 `_java` 下拉 + `_javaOpts` 标签/取值表;`SelectedJava()`、`ReloadJavaAsync(scan)`、`SaveJavaAsync()`;实例切换/首载后刷新 Java 列表(先 scan:false 再后台 scan:true);换 Java 即存;`SyncHero` 增加 Java 胶囊(`自动 → Java 自动`;`Java 17 (…)`/`jdk1.8.0_301` → `Java 17`;认不出原样)。
  - `Pages/Launch/LaunchPopups.cs`:「更多设置」内加 `Java` 字段(顺序 Java / 内存 / 直连)。
- 验收:`dotnet build` 0 警告 0 错误;`dotnet test` **111 通过 / 1 跳过 / 0 失败**(新增 `JavaComboOptions_StartsWithAutoChoice`、`InstanceJava_RoundTrips_AndAppearsInComboAsSaved`);无头冒烟(带实例 + 已保存 Java 的夹具)chip 行渲染为 `26.3 | Java 17 | 4 GB`,与参考版同形。
- 仍缺(后续):「分辨率」字段、新闻行线上内容、`tasks` 页。

### 2026-11-10 M4-C 任务页真实化 + 整站取色修复(主 agent)

- **任务页**:新增 `Pages/TasksPage.cs`(移植 Windows `TasksPage.cs`):概览(进行中 N · 共 M + 清除已完成)+ 任务列表;卡片 = 标题 / 状态 / 进度条 / 速度 + 取消(完成后变「清除」)+ 日志折叠;数据源 `TaskStore`(Added/Updated/Cleared 增量刷新),行内 `BlockHost("tasks", 概览/任务列表)`。`GetPage("tasks")` 接入,删 `PlaceholderTasks`。差异:崩溃分析钮(Windows 走 CrashUi)未移植 → 不呈现,不做死按钮。
- **整站取色修复(D-12,本轮最大发现)**:`Ui.Dyn`/`Res.Bind`/`Ui.Theme`/`Ui.Res` 原先全解析为 null(主题资源从未挂到 Application 级 + `Ui.ApplyTheme` 里的 `ResourceInclude(Uri)` 写法是 D-6 记录过的坑 + `Controls.axaml` 的 ComboBox 主题含跨两层模板选择器而无法加载)。修复后:启动页绿按钮/绿点/下拉箭头/新闻底色回来、设置页滑块变主题绿、库页版本行图标不再是字面文字。
- **冒烟基建**:`--smoke` 现在先关动画(FadeIn 把子项初值设 Opacity=0 靠过渡补回,无头单帧捕获不跑过渡 → 内容整片不可见);新增 `SMOKE_TASKS=1` 塞两条假任务供任务页视觉回归。
- **测试基建**:`TestApp` 改用 `HeadlessUnitTestSession`(测试体派发到会话专属 UI 线程),31 个测试方法改造;`AssemblyInfo.cs` 关闭并行。根治既有的「换线程就随机失败」。
- **验收**:`dotnet build` 0 警告 0 错误;`dotnet test` 连续 3 次 **113 通过 / 1 跳过 / 0 失败**;冒烟矩阵六页 × 明暗 12 帧全出图,启动页与 `_eval_shots/final/launch-*.png` 结构一致。
- 仍缺:壁纸/整窗沉浸层(Windows `Wallpaper` 服务)、`Services` 视觉件(SPanel/SideFrost/SmoothScroll/LucideAssets 等)、长尾页与 AI 对话子系统。

### 2026-11-11 M4-D 契约补齐 + 账号链路(主 agent)

- **修掉两处「静默失效」的调用约定缺陷(见 D-13)**:① `ToDict` 不递归 → `save_settings(new { data = … })` 的嵌套字典判不成 `PyDict`,**设置保存全是空操作且不落盘**;② `CallAsync/TryCallAsync` 不把 `PyDict` 落 POCO → 清理预览/智能推荐/语言表这类按 POCO 取结果的地方永远拿默认值。两者都已修并加测试锁定。
- **Core 补搬**(纯 C#,无 WPF/Win32 依赖):`Servers / Cleaner / Feedback(+Defaults/Error) / Themes / Repair / Worlds / Thumbnails / UiLayout(+LayoutDoc/LayoutItem) / ContentExport / ExportPack / HelpContent / Updater / Terracotta` 及缺失的错误类型。
- **Api 契约 +25**(UI 已在调用但原先落桩):账号行/离线账号/删除/设为当前、`authlib_presets`、`skin_urls`、`get·set_account_skin`、三种登录 `start_microsoft|authlib|nide8_login`(后台任务 + `login_code`/`login_status` 事件,`ApiEvent` 增 `Code`/`Uri`)、全局 Java `list_java`/`scan_java`/`add_java`/`set_default_java`、服务器 `list/add/update/delete/import/export_servers`、`cleaner_preview|apply`、`get_smart_recommendation`、反馈 `feedback_has_consent|consent|submit|history`、语言 `get_language`/`set_language`/`available_languages`/`translate`、`allow|set_multi_instance`、`test_ai_connection`、主题包 `list|save|load|delete|import|export_theme`、`clear_playtime`。
- **账号页**:新增 `Pages/AccountPage.cs`(移植 Windows `AccountPage.cs`:操作区 + 英雄卡 + 账号行 + 皮肤编辑弹窗 + 设备码/皮肤站/统一通行证登录),`GetPage("account")` 接入,删 `PlaceholderAccount`。配套新增 `Pages/SkinPng.cs`(64×64 → 128×256 正面视图**纯像素合成**,不依赖绘图 API)、`Common/Thumbs.cs`(远程图 + 内存缓存)、`Common/AvatarTile.cs`(圆形头像块)。
- **`Ui.Icon`**(见 D-13):`IconBtn`/`Empty` 都改走它,图标名不再被当文字画出来。
- 验收:`dotnet build` 0 警告 0 错误(仅 NU1900 联网警告);`dotnet test` **120 通过 / 1 跳过 / 0 失败**(新增 `ApiContractsTests` 4 项、`AccountPageTests` 3 项);账号页冒烟出图正常。
- 仍缺:MultiplayerPage、CatalogPage(六分段)、VersionManagePage/InstancePage、FeedbackPage/FirstRunWizard/CrashUi/各 Dialog、壁纸服务、AI 对话子系统。

### 2026-11-11 M4-E 联机页 + 账号钮联动(主 agent)

- **联机页**:新增 `Pages/MultiplayerPage.cs`(移植 Windows `MultiplayerPage.cs`:陶瓦联机)。说明 + 局域网地址、状态卡(状态点/详情/防火墙钮)、房间卡(邀请码 + 复制)、按 16 种状态渲染的操作卡、房间成员、会合节点;1.2s 轮询 `terracotta_snapshot`,进页在「已装但未运行」时自动准备内核。`GetPage("multiplayer")` 接入(此前启动页「联机」钮点了无反应)。
- **联机契约**:`lan_hint` / `local_ips` / `terracotta_player|snapshot|prepare|host|join|idle|allow_firewall|open_firewall_settings|shutdown|enter_world|direct_connect`;`terracotta_enter_world`/`direct_connect` 走 `LaunchIntoLobby`(记住大厅 → 取最新版本 → `--server host --port N` 直连启动,返回 `task-` 任务 id,与 Windows 语义一致)。
- **账号钮联动**:`TitleBar.SetAccount` + `MainWindow` 订阅 `ui_changed` 事件 → 标题栏显示当前账号名与首字母圆(此前恒为「未登录」)。
- **`TryCallAsync` 不再静默**(修可观测性):吞异常时改为 `AppLog.Warn`,并说明「页面用 TryCall 的地方失败只表现为什么都不显示」的排查痛点。
- 过程中自查出并修掉两个自己的疏漏:联机页漏调 `ReloadAsync`(状态卡恒空)、快照键名写成 camelCase 而 Core 实际输出 snake_case(`error_hint`/`difficulty_hint`)。
- 验收:`dotnet build` **0 警告 0 错误**(仅 NU1900 联网警告);`dotnet test` 连续 2 次 **121 通过 / 1 跳过 / 0 失败**;冒烟 8 页(launch/library/download/plugins/settings/tasks/account/multiplayer)全部出图。

### 2026-11-11 M4-F 反馈链路打通(用户点名:必须真发出去、不被拒)

- 发现两处让「点了发送等于没发」的问题:未过同意门(`Feedback.Submit` 未同意直接抛错,而 UI 从没问过)+ `TryCallAsync` 吞错把 403/400 伪装成「后端 M3 接入」。
- 对真实服务器(`114.66.28.184:53611`)做无副作用探测,确认门禁顺序:**无 `X-PyMCL-Client` → 403「缺少客户端标识」**;带标识但空载荷 → 400「请填写标题或内容」;`/health` 200 `pymcl-ingest`。客户端头/UA 本就正确。
- 落地:同意门(说明上送内容 → `feedback_consent`)→ `CallAsync` 发送并把服务器原话显示出来 → 带 category(bug/feature/other,桌面端特有分类归并为 other)与 sysinfo(「key: value」多行文本);新增 `feedback_submit_crash`。
- 验证:新增 `FeedbackTests`(4 项)用本机 TCP sink **复刻服务器规则**,断言请求形状 + 两道门 + 200/id,并锁「未同意不发送」与「403 原样抛出」。
- 验收:`dotnet build` 0 警告 0 错误;`dotnet test` **125 通过 / 1 跳过 / 0 失败**。
- **仍待用户拍板的一项**:向生产 ingest 发一条**真实**反馈以取得 200 + 真实 id(需用户同意,属对外动作)。

### 2026-11-11 M4-G 逐页细节对照 + 测试真正跑起来(主 agent,续上一次会话)

- **逐页细节对照**(用户:「再抓一下各种细节」):按 900×550 渲 Linux 明暗帧,与 `windos/_eval_shots/final/*.png` 做分块差分 + 像素量测,只改量得出的差异:
  - 漏图标:库页「实例详情 / 刷新」等按钮补图标(参考为带图标 chip),再按 Windows「按钮文案 → 图标」对表补齐其余漏图标;`IconLib` 增 `ChevronDown/More/Gear`;标题栏主题图标线宽 1.3 → 1.7。
  - 设置页左导航项:Padding 对齐 (12,8,12,8)、去掉多余外边距 → 154×34(参考 156×33;此前 146×36,同屏少放一项)。
  - 输入框高 45(参考实测 45,原 32);库页两栏 `1*:18:2*`;行尾图标 13px。
  - 启动页:底部改由 `BlockHost` 承载、标签垂直居中 → 标题 / 启动钮 / 新闻标签纵向位置与参考差 1–2px(原差 12 / 13 / 27px)。
  - 页签横向差 1px 与中文字形差异来自 D-5 的系统字体回退,不修。
- **日志可读**:`AppLog` 按序填充 Avalonia 日志的具名占位符(`{Property}` 之类,`string.Format` 不认,日志原样输出模板)→ 由此揪出 `Controls.axaml` ComboBox 弹层沿用 WPF 的 `ActualWidth` 绑定(每页 10 条绑定错误),修后 8 页日志清零。
- **`TaskStore`**:Added / Updated / Changed / ClearFinished / Remove 统一经 `Emit` 派发到 UI 线程,新增「工作线程上报进度」回归测试。
- **测试框架缺陷(D-16)**:`TestApp.Run/RunAsync` 没等 `Session.Dispatch` 返回的 Task → 测试体还没执行就判通过、断言失败全被吞掉(`TestApp` 自 M4-C 引入起即如此)。改为阻塞等待。
- **测试数据隔离(D-16)**:新增 `tests/TipCL.Tests/TestIsolation.cs`;已核实真实 `%LOCALAPPDATA%\TipCL\config.json` 从未被测试改过。
- **测试体真正执行后暴露 4 个失败,逐个修掉**:
  1. `Api.Deep()` 参数数值归一:int 系 → `long`、float → `double`(D-15)。
  2. 反馈分类:设置页「功能建议」→ 桌面键 `suggest`,Core 上送时转服务端的 `feature`(D-15)。
  3. `FeedbackTests` 本机 sink 补 `EnforceIngestRules`(复刻 403 / 400 / 200)。
  4. `AccountSkin_SetAndClear_RoundTrip`:① 产品缺陷——`SkinPng.TryRead` 漏了 `bmp.CopyPixels`,解码后像素全空,正面视图恒透明;② 测试会话用的是默认无头绘图桩(实测 `Bitmap.Save` 写出 0 字节、任何 PNG 都解码成 1×1),`TestApp` 改由自带的 `BuildAvaloniaApp()`(`UseSkia()` + `UseHeadlessDrawing = false`)起会话(D-16)。
- **标题栏明暗切换不落盘(收尾时发现,D-17)**:月牙钮只换主题不写 `ui_dark`,而设置页加载以配置为准 → 切深色后一打开设置页就被改回浅色,重启也不记得(此前深色冒烟的 settings 帧实为浅色即此)。`MainWindow.ToggleTheme` 照 Windows `PushDarkAsync` 经 `save_settings` 写回;`SmokeRunner` 的明暗改为先写进内存配置再构造窗口。新增 `ThemeToggleTests`(修前红、修后绿)。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;`dotnet test` 连续 3 次 **127 通过 / 1 跳过 / 0 失败**;冒烟 8 页 + 3 深色页全部出图,日志 WARN/ERROR 0 行(深色 settings 帧现为深色);真实 `config.json` / `accounts.json` 修改时间未变。
- **环境注意(Windows 开发机)**:`D:\项目\tipcl` 带可继承的**低完整性标签**(沙箱所设),仓库里构建出的 exe(`testhost.exe`、`TipCL.App.exe`)一律以低完整性启动、写不了 `%TEMP%` → 普通终端里直接 `dotnet test` 会全红(`TestIsolation` 建目录被拒)。规避:`dotnet test ... -- RunConfiguration.DotNetHostPath=<dotnet.exe 绝对路径>`;冒烟用 `dotnet <TipCL.App.dll> --smoke`。

### 2026-11-12 M4-H P1 下载页六分段 + CatalogPage(移植代理)

- **缺口**:下载页 Mod / 整合包 / 数据包 / 资源包 / 光影 / 世界 六分段此前全是占位空态(「该分段内容将在目录页移植后提供」),目录页整文件缺失。
- **页面**:新增 `Pages/Download/CatalogPage.cs`(移植 Windows `Pages/CatalogPage.cs`,1309 行)+ `Pages/Download/CatalogModels.cs`(CatalogKind 六类别 / CatalogItem / CatalogFile / ModEntry / ModsTarget / SaveRow + `CatalogFmt.Downloads`);新增 `Common/ThumbTile.cs`(方形缩略图块)。
  - 三态:**搜索下载 / 已安装 / 收藏**;筛选:来源(全部·Modrinth·CurseForge)/ 类型 / 游戏版本 / 实例 / 已装于版本;搜索框 320ms 防抖 + 回车即时搜 + 9s 超时兜底(超时只提示,不丢结果)。
  - 行操作:结果卡(缩略图 + 名称/作者/下载量/描述/标签 + 收藏星 + 安装)、已装行(启停开关 / 删除 / 导出 / 移除整合包标记)、顶栏「链接安装 / 导入本地文件 / 检查更新 / 全部导出 / 重置条件 / 刷新」。
  - 安装流程:版本文件对话框(游戏版本 + 加载器双筛选;取消 / 安装最新 / 安装所选)→ 数据包问「装进哪个存档」→ 世界解压到实例 saves → 起后台任务并跳「任务」页。
  - 剪贴板 Modrinth / CurseForge 链接自动识别(照 Windows `SniffClipboard`)。
- **Core 补搬**:`Core/Launcher/Modpack.cs`(1843 行,整合包安装引擎)+ `Core/Launcher/ModUpdate.cs`(312 行,模组更新检查),均纯 C#、无 WPF/Win32 依赖;`CatalogFiles._hit_row` 补 `icon_url` / `website_url`(结果卡缩略图与「打开介绍页」要用)。
- **Api 契约 +33**:`search_mods|modpacks|datapacks|resourcepacks|shaders|worlds`、`list_catalog_files`、`install_mod|shader|resourcepack|datapack|world|modpack`(后台任务,返回 task_id)、`get_installed_mods|modpacks|datapacks`、`delete_mod|datapack|modpack`、`get_mods_targets`、`open_mods_folder|open_global_mods`、`catalog_favorites|toggle_favorite`、`default_export_dir|remember_export_dir|export_content|export_contents`、`check_mod_updates`、`list_global_mods|set_global_mod_enabled`、`wait_task`;`enable_mod / disable_mod / delete_mod / get_installed_mod_entries / get_installed_resourcepacks / get_installed_shaders` 一并支持 `version`(指向隔离版本的目录)。
- **冒烟基建**:`SMOKE_SEG=1..6` 让无头冒烟停在下载页某个目录分段(配套新增 `DownloadPage.SelectSegment(int)` 与 `MainWindow.CurrentPage`)。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;`dotnet test` **134 通过 / 1 跳过 / 0 失败**(新增 `CatalogApiTests` 7 项:六类别契约表、六种页面构造、分段宿主、已装模组启停删、数据包删、收藏往返、导出单件+批量);冒烟八帧(六分段 + 原版分段 + 深色资源包)全部出图,六个分段均拉到并渲染真实搜索结果,日志 WARN/ERROR 0 行;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间未变。
- **遗留**:(1)「导入本地文件」只支持单文件选择(Windows 支持多选 + 拖放安装,Linux 侧无 FileDrop 服务);(2)数据包「装进存档」只把存档名交给 Core,落盘位置未与 Windows 逐路径核对;(3)整合包/世界的完整安装链路未在真机跑通(需联网 + Ubuntu 盒子)。

### 2026-11-12 M4-H P2 版本管理页 + 版本设置/模组/存档对话框(移植代理)

- **缺口**:Windows NavDefs 的 "instance"(标题「版本管理」)整页缺失——启动页版本浮层的「管理」点了没反应;版本设置 / 模组管理 / 存档管理 / 全局模组四个对话框全缺。
- **页面**:新增 `Pages/Version/VersionManagePage.cs`(移植 Windows 1110 行:左栏文件夹列表卡 + 右栏按来源分组的版本卡(收藏 / Fabric / Forge / Quilt / NeoForge / OptiFine / LiteLoader / 原版 / 不常用 / 隐藏),组可折叠并记住折叠态;行内收藏星、隔离开关、卸载、版本设置;双击启动;版本右键菜单 9 项;文件夹右键菜单 8 项)。
- **对话框**:`Pages/Version/VersionSetupDialog.cs`(14 个字段:隔离 / 内存 / JVM / GC / 直连 / 端口 / 启动前命令与等待 / 退出后命令 / 窗口标题与模式 / 绑定账号 / 统一通行证 / 隐藏)、`Pages/Version/ModsDialog.cs`(目标目录切换 + 启停 / 删除 + 导入 jar + 勾选式更新 + 全局模组入口,含 `CheckUpdatesAsync`)、`Pages/Version/SavesDialog.cs`(存档 备份 / 导出 / 打开 / 装数据包 / 删除 + 备份列表 还原·删除 + 截图·崩溃报告·日志)、`Pages/Version/GlobalModsDialog.cs`。
- **接线**:`MainWindow.GetPage("instance")`;`MainWindow.PreferLaunch(inst, ver)` + `LaunchPage.Prefer(...)`(照 Windows PreferInstance:预选实例与版本后切启动页);启动页实例浮层补「管理」按钮(照 Windows ManageLink);新增共用件 `Pages/Layout/ContextMenuFx.cs`、`Dlg.Panel`(只有「关闭」的交互面板)、`Dlg.Choose`(多分支选择对话框)。
- **Api 契约 +31**:`get_version_settings / save_version_settings / get·set·toggle_version_isolation / rename_version / copy_version / hide_version / open_version_folder / repair_version / export_launch_script / export_modpack / start_mod_updates / apply_mod_update / get_accounts / instance_java_label / game_root_name / game_root_path / open_media / detect_official_launcher / official_launcher_dir / scan_official_versions / migrate_official_launcher / delete_save / open_save / export_save / install_datapack_into_save / list_media / backup_save / list_save_backups / restore_save_backup / delete_save_backup / get_saves_targets`;`build_launch_command` 抽出共用实现供导出启动脚本复用。
- **平台适配**:`export_launch_script` 在 Linux 侧写可执行 `#!/bin/sh` 脚本(Windows 写 .bat,同一调用链);`create_desktop_shortcut` 不移植(理由见 D-19)。
- **验收**:Debug / Release 全量重编 0 警告 0 错误;`dotnet test` 连续 3 次 **141 通过 / 1 跳过 / 0 失败**(新增 `VersionManageTests` 7 项:版本设置往返+落盘+版本行带出、隔离切换、重命名·复制·隐藏、Java 标签与游戏根、存档目标与媒体、页面构造、外壳导航到 instance);冒烟:版本管理页明暗两帧 + 启动页回归帧出图,日志 WARN/ERROR 0 行;真实 `config.json` 修改时间未变。
- **遗留**:(1)折叠态与版本收藏在 Windows 落 wpf-ui.json(UiPrefs),Linux 侧 UiPrefs 未落地 → 先进程内静态表;(2)「创建桌面快捷方式」不呈现(启动器无 `--launch` CLI,做了也是死按钮);(3)`export_launch_script` 产出的 .sh 未在真机执行验证(需 Ubuntu 盒子)。

### 2026-11-12 M4-H P3 Java 子页真实化(移植代理)

- **缺口**:设置页内层导航第 3 项(Java)只有一个「扫描 + 手动添加 + 设为默认」的空壳——缺 Windows `JavaPage.cs` 的下载卡、发行版选择、实例默认区与已检测列表。
- **落地**:按 Windows `Pages/JavaPage.cs`(413 行)重写 `Pages/Settings/SettingsSubs.cs` 的 `JavaView`:
  - 头部操作区(计数 / 刷新 / 扫描系统 Java)改挂到子视图标题行右列(**修掉重复标题**——`SettingsSubView` 基类已给出标题与副标题,另插一个 `UiClass.Section` 会出现两个「Java」);
  - 「下载新运行时」卡:发行版下拉(`java_vendor_list` + `java_vendor_label`)+ 5 张下载卡(Java 8 / 11 / 17 / 21 / 25,含各自适用版本说明);
  - 「实例默认」卡:实例下拉 / 默认 Java 下拉(`java_combo_options` + `java_combo_label_for`)/ 自定义路径 + 「用这个路径」(`normalize_java_pref` 先归一)/ 当前记录行(`instance_java_label`);
  - 「已检测列表」:大号 major 方块 + 名称 + mono 路径 + 设为默认 / 给本实例 / 打开所在目录;任务完成后(`OnEvent finished` 且标题含 Java)自动重载。
- **Api 契约 +4**:`java_vendor_list`、`java_vendor_label`、`download_java`(adoptium 走 `Java.InstallAdoptium`,其它发行版转 `install_java`)、`install_java`(`Java.InstallJavaVendor`),都是后台任务 + 立刻返回 task_id。
- **冒烟基建**:新增 `SMOKE_SETTINGS_TAB=0..6` 让无头冒烟停在设置页某个内层导航项(配套 `SettingsPage.SelectTab`)。
- **顺带修掉一类静默失效**:`CallAsync<List<PyDict>>` / `TryCallAsync<List<PyDict>>` 会因为 `Coerce` 把 `List<object>` 反序列化成 `List<PyDict>` 失败而**整体回默认值**(不报错)——`java_combo_options` 与 `get_java_list` 两处中招(前者会让「选择 Java」对话框直接显示「没找到 Java」)。全部改成 `List<object?>` + `OfType<PyDict>()`,并加测试锁住 `java_combo_options` 的形状。
- **验收**:Debug / Release 全量重编 0 警告 0 错误;`dotnet test` **145 通过 / 1 跳过 / 0 失败**(新增 `JavaPageTests` 4 项:发行版列表与标签、Java 列表行字段、自动选择首项、设置页 Java 内层项切换);冒烟:Java 页明暗两帧 + 版本管理页回归帧出图,日志 WARN/ERROR 0 行。
- **遗留**:Java 下载(走网络下载几百 MB 运行时)未在真机跑通;「打开所在目录」在 Linux 上交给 `xdg-open`。

### 2026-11-12 M4-H P4 反馈独立页 + 首启向导 + 崩溃界面(移植代理)

- **缺口**:`FeedbackPage` / `FeedbackConsent` / `FirstRunWizard` / `CrashUi` 四个文件全缺;设置页「反馈与关于」是一张内联提交卡,没有提交历史与帮助文章;游戏崩溃只在任务卡里显示一行错误。
- **反馈页**:新增 `Pages/Feedback/FeedbackView.cs`(挂设置页内层导航第 6 项,替换原内联卡):提交反馈(类型 / 标题 / 正文 / 联系方式 / 附带系统信息 / 附件)+ 我提交过的(历史,含状态标签)+ 常见问题(搜索 + 点标题展开,`help_articles`/`help_article`)+ 关于(版本 / 数据目录 / 重新运行向导)。提交链路沿用 M4-F 打通的两道门:同意门(`feedback_has_consent` → 说明 → `feedback_consent`)→ `submit_feedback` → **把服务器原话显示出来**(403/400 可见)。
- **同意门纯逻辑**:新增 `Common/FeedbackConsent.cs`(`Raw` / `ParseRaw` / `ReadRaw` / `ShouldAsk` / `MaybeAskAsync`),照 Windows 语义:**config 文件里没有 `feedback_consent` 键才问一次**(文件优先于后端值),问过就不再打扰;Windows 那份 CLI 自检用例表搬成了 xunit。
- **首启向导**:新增 `Pages/Feedback/FirstRunWizard.cs`(4 步:说明 / 游戏目录与下载源 / 默认内存与隔离 / 容易错过的功能),`MaybeShowAsync` 按 `first_run` 决定是否弹;走完写回 `download_source` / `default_memory_mb` / `default_isolation` / `first_run`,「重新运行向导」也能手动再跑。启动顺序照 Windows:先向导,再同意门(`App.OnFrameworkInitializationCompleted`;`--smoke` 下不打扰无头截图)。
- **崩溃界面**:新增 `Pages/Feedback/CrashUi.cs` —— 标题 / 摘要 / 退出码+提示 / 帮助 / 详情日志 + 报告自带修复动作按钮 + 打开日志文件 / 导出报告 / 上传给开发者 / 附说明反馈 / 复制全文;返回「重新启动」时外壳重跑该版本。接线:Core 抛 `GameCrashError` 时 `InProcessApi` 缓存报告(按 task_id,最多 40 份)并发 `crash` 事件,`MainWindow` 订阅后弹界面(照 Windows 的 `crash` event + CrashUi)。
- **Api 契约 +16**:`submit_feedback`(归一成 `{ok,message,id}`)、`help_articles` / `help_article`(标题本地化)、`collect_sysinfo` / `sysinfo_text`、`stash_upload`(base64 → `$TIPCL_HOME/uploads`)、`get_crash` / `export_crash_report` / `open_crash_file` / `apply_crash_action`(8 个分支)/ `submit_crash_report` / `submit_crash_feedback`、`set_game_dir`(写测试后落 `instances_dir`)。
- **降级项**:Windows 的「交给 AI 修复」依赖 AiFixWindow(P5 AI 子系统),Linux 侧暂不呈现(不做死按钮);`Dlg.Layer` 自绘浮层 → `Dlg.OpenPanel` 非模态窗口。
- **验收**:Debug / Release 全量重编 0 警告 0 错误;`dotnet test` **155 通过 / 1 跳过 / 0 失败**(新增 `FeedbackConsentTests` 4 项 + `FeedbackApiTests` 5 项:帮助文章、系统信息、附件落盘与非法 base64、崩溃动作表与 bump_memory、get_crash 空态、配置键存在);冒烟:反馈页明暗两帧出图,日志 WARN/ERROR 0 行;真实 config.json 修改时间未变。
- **遗留**:(1)`CrashUi` 只在游戏异常退出路径上有真实数据,需真机跑一次崩溃复现(需 Ubuntu 盒子/真实游戏);(2)首启向导与同意门在冒烟里被 `--smoke` 跳过,真机首次启动才走;(3)「交给 AI 修复」等 P5 落地后补。

### 2026-11-12 M4-H P5(部分完成)AI 对话子系统 · Markdown 链 + 对话页(移植代理)

- **已完成**:
  - **Markdown 解析链**(从 Windows `Pages/Ai/Md*.cs` 原样搬运,纯 C#):`MdBlockKind / MdBlock / MdInlineKind / MdInline / MdParser`(465 行,块级:标题·段落·有序/无序列表·引用·代码围栏·表格·分隔线;行内:粗体·斜体·删除线·行内码·链接·换行;转义与 `PlainText` 扁平化)。
  - **`Pages/Ai/MdView.cs`**:Avalonia 渲染器(Windows 是 WPF `RichTextBox`/`FlowDocument`,Avalonia 没有对应物)→ 段落用 `TextBlock.Inlines`(Run/Bold/Italic/Span+TextDecorations/LineBreak),列表按深度缩进、引用带左边框、代码块深底 + 语言标签 + 复制钮 + 横向滚动、表格用 `Grid` 并支持左/中/右对齐;`SetMarkdown(text, live)` 带 80ms 节流(流式输出用)。
  - **`Pages/Ai/AiBubble.cs`**:气泡(角色头像 + 名字 + Markdown 正文 + 复制 + 思考折叠块)。
  - **`Pages/Ai/ChatInput.cs`**:输入区(多行 / Enter 发送 / Shift+Enter 换行 / 发送·停止切换 / 提示行)。
  - **`Pages/Ai/AiPage.cs`**:真实对话页 —— 头部(接入方式 · 模型 + AI 设置 + 清空对话)、消息列表、空态 + 4 条常用提问建议、流式增量写入最后一个气泡、`<think>…</think>` 收进折叠块、停止、错误提示、未配置时给引导文案。`GetPage("ai")` 由 `PlaceholderAi` 换成 `AiPage`(标题栏 AI 药丸钮终于有落点)。
- **测试**:`MdParserTests` 10 项(标题 / 段落换行 / 列表深度与起始序号 / 引用递归 / 代码围栏与 `~~~` / 表格对齐与参差补齐 / 分隔线 / 行内六种 + 转义 + 合并 / 空输入 / MdView 真渲染);`AiPageTests` 3 项(页面构造、外壳导航到 ai、端点键 mode/model/url)。测试可见性:给 `TipCL.App` 加 `InternalsVisibleTo("TipCL.Tests")`(与 Core 同款,只放宽可见性,见 D-22)。
- **顺手修掉的缺陷**:`AiPage` 起初按 `label`/`configured` 读端点,而 `Client.ResolveEndpoint` 真实返回的是 `mode`/`model`/`url` → 头部永远显示「未配置」;已按真实键改写并在无端点时兜底(`AIClientError` → null → 显示引导)。另把 `ThemeToggleTests.SavedDark` 改成带重试读取(本机 Defender 偶发占锁会让单次读拿到半截文件,曾两次把环境噪声报成失败)。
- **验收**:Debug / Release 全量重编 0 警告 0 错误;`dotnet test` 连续 3 次 **169 通过 / 1 跳过 / 0 失败**;冒烟 AI 页明暗两帧出图,日志 WARN/ERROR 0 行。
- **遗留(P5 剩余项,下一轮继续)**:
  1. **工具调用链**:`ToolLine` / `ToolState` / `StepGroup` / `ChatInput` 的工具状态、`AiChatOps` 的工具卡呈现;Core 侧 `Launcher/Ai/Agent|Tools|ToolCall|ToolMeta` 尚未搬。
  2. **权限与确认**:`AiPermissionPanel` / `ConfirmCard` / `AskBlock` / `AskCard`(ask_user 结构化选择题)、权限模式(plan/edit/auto…)、风险分级 `AiRisk`。
  3. **计划与任务卡**:`PlanCard` / `TaskCard`。
  4. **上下文与自检**:`AiSelfTest`、`AiUiText`、会话列表与记忆面板(`Launcher/Ai` 的 Store/Checkpoint/Rewind/Memory 已搬一部分)。
  5. **视觉细节**:`AiIcons`(Windows 的 SVG 图标集)、AiViewStrip(插件 AI 视图条,与 P6 一起做)。
  6. 未接:模型切换下拉、附件/图片输入(`AiPage.Images.cs`)、Markdown 里的链接点击(Avalonia 的 `Span` 不是控件,没有 Click/ToolTip → 链接目前只有主题色 + 下划线)。

### 2026-11-12 M4-H P7 + P8 收尾:Services 逐项判定 + 全页冒烟矩阵 + PARITY 对齐(移植代理)

- **P7 Services 视觉/交互件逐项判定**(结论写进 `docs/PARITY.md` §5,不移植的写明理由):
  - **不移植**:`SPanel`(Avalonia 的 StackPanel/WrapPanel 已覆盖)、`SmoothScroll`(无等价的 OffsetProperty 动画挂点,收益低于风险)、`SideFrost/SideFrostEx`(v2 外壳没有侧栏)、`WebViewHost` 三件 871 行(**Linux 没有 WebView2 等价宿主,引 CEF/WebKitGTK 属依赖白名单外**)、`Backdrop`(窗口级亚克力是 Win32 DwmSetWindowAttribute,桌面合成器决定)、`ImeGuard`(Avalonia 自处理 IME)。
  - **已有等价物**:`LucideAssets/Lucide` → `Common/IconLib.cs`(51 个语义键的矢量 Path);`PhoneSyncPanel` → 设置页手机同步卡;`Wallpaper` → 启动页静态背景 + 遮罩(B1),mp4/轮播不做。
  - **部分**:`FileDrop/FileKinds` → 目录页用「链接安装 / 导入本地文件」替代,统一拖放入口记为遗留。
- **P8 决策件对齐**:`DashCard/DashLayout` 数据模型已在 `Pages/Library/DashModels.cs`;**`DashHost` 画布宿主不移植** —— 核查确认 Windows v2 的 `LaunchPage` 只 `new DashHost()` + `RefreshThemeVisuals()`,**从未把 `_dash` 加进可视树**,属死代码。
- **PARITY 收尾**:关键缺口汇总里两条早已完成的项(外壳 IA、任务页)改写成「已完成」并注明 v1/v2 设计线;AI 小节标题与说明更新为「部分完成」并列清单;Dash 行改为 🔷 并写明宿主不移植的理由。
- **全页冒烟矩阵(Release 产物)**:浅色 10 页(`launch / library / download / plugins / settings / tasks / account / multiplayer / ai / instance`)+ 深色 6 页(`launch / library / download / settings / ai / instance`)全部 **exit=0 且出图**,`_cmp/logs` 当日日志 **WARN/ERROR 0 行**;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍是 `2026-11-08 17:13:58`。
- **清理**:删掉已无引用的 `Pages/PlaceholderAi.cs`(AI 页真实化后它成了死代码;对照 Windows 也没有这个文件)。
- **验收**:Debug / Release 全量重编 0 警告 0 错误;`dotnet test` 连续 3 次 **169 通过 / 1 跳过 / 0 失败**。
- **仍未完成(留给下一轮,顺序即优先级)**:P5 剩余(AI 工具调用链 / 权限确认与 ask_user 卡 / 计划卡与任务卡 / 自检 / 图片输入 / AiIcons / AiViewStrip)、P6 插件宿主全链(市场·已安装·设置·开发者四分段拆分、PluginActionBar/Overlays/DashCards/Events/PluginUi、MainWindow.Plugins)、P7 的拖放入口、P8 的插件动态顶部页签。

### 2026-11-12 M4-H P3.5 桌面快捷方式 + CLI 入口(移植代理;用户复核否决了上一棒「不移植」的裁决)

- **背景(参考实现缺陷)**:Windows `Core/Launcher/Shortcut.cs:79` 生成的目标参数是 `-i <实例> launch <版本> [--account X|--username Y]`,该文件甚至**自带 `_create_linux` 分支**(Shortcut.cs:180)会写 `.desktop`;但整个 Windows 工程**没有任何地方消费这些参数**——`App.xaml.cs` 的 `OnStartup` 只认 `--smoke/--shot/--nav-dump/--ime-check/--i18n-check/--consent-check/--ai-ui-selftest`,全仓库搜 `"-i"` 只有 Shortcut.cs 那一处。即 **Windows 的「创建桌面快捷方式」本身就是坏的**(双击只开启动器,不启动那个版本)。Linux 侧本轮把它真的做通。
- **Core**:新增 `src/TipCL.Core/Launcher/Shortcut.cs`(移植参考版,只保留 Linux/macOS 分支)+ `src/TipCL.Core/Launcher/ShortcutIcon.cs`(程序化生成 128×128 PNG 图标,自带最小 PNG 编码器,只依赖 `System.IO.Compression.ZLibStream`)。修掉参考版自带 Linux 分支的两个缺陷:`Chmod755` 是空方法(.desktop 无可执行位)、`Path=`/`Icon=`/`Exec=` 直接沿用 Windows 反斜杠路径;另补 XDG 桌面目录(`$XDG_DESKTOP_DIR` → `~/.config/user-dirs.dirs` → 候选目录)与真实图标文件。
- **CLI 入口**:`src/TipCL.App/Session/CommandLine.cs` 实现 `-i <实例> launch <版本> [--account X|--username Y]`(另接受 `--force` / `--print-command`),`Program.cs` 在 `--smoke` 之后接上,**走与 UI 完全相同的启动链路**(`LaunchCore.RunAsync`,与 `launch_game` 同一个方法),日志与崩溃回传打到 stdout,退出码:0 正常 / 3 启动失败或崩溃 / 4 用法错误。参数形状与 `Shortcut.LaunchArgs` 的输出严格对齐并加测试锁定。
- **UI**:版本管理页版本右键菜单恢复「创建桌面快捷方式」(已存在时变为「删除桌面快捷方式」),成功后把 `.desktop` 路径弹给用户(照 Windows `VersionManagePage.cs:869` 的 Alert 文案)。
- **Api 契约 +2**(名字以 Windows `BackendApi.cs:1099` 为准):`create_desktop_shortcut(instance, version, username?, account?, name?)` → 返回 `.desktop` 绝对路径;`delete_desktop_shortcut(name)` → `{ok, path}`。
- **测试**(`tests/TipCL.Tests/App/ShortcutTests.cs`,`ShortcutTests` 12 项):.desktop 内容逐键断言(首行/Type/Name/Comment/Terminal/Categories/Exec 尾部 4 参严格等于 `-i <实例> launch <版本>`/Path/Icon 且 Icon 文件真实存在且是合法 PNG)、可执行位、**临时 HOME 下真实生成再清理**、XDG 目录重定向、缺版本抛 `ShortcutError`、删除往返与「再删一次 ok=false」、PNG 头与尺寸、CLI 解析形状与坏用法、`--print-command`、未知实例退出码 3、以及**端到端**:照着 .desktop 里那条命令行跑 CLI,用假 java 记录收到的参数,断言真的把版本启动起来。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;`dotnet test` **181 通过 / 1 跳过 / 0 失败**(169 → 181,新增 12);冒烟(版本管理页)exit=0 出图、日志 WARN/ERROR 0 行(`_cmp/logs/tipcl-20261112.log`);`--smoke` 优先级未破坏(带 `-i foo launch 1.0` 仍走冒烟);进程外 CLI 实测:缺参数 exit=4、未知实例 exit=3(**该结论当时判错了**:这个 3 其实来自「没有离线账号」,未知实例根本没被拒绝;已在本轮 P0 修成 exit=4 + 明确原因)(把预检原因原样打出来)、`--print-command` exit=0 且回显 `-i foo launch 1.0`;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍是 `2026-11-08 17:13:58.504`。
- **遗留**:`.desktop` 未经真机(Ubuntu 桌面双击)验证,需盒子;`Icon=` 用的是程序化生成的 PNG(`$TIPCL_HOME/tipcl.png`)而不是 .ico 风格的多尺寸图标;`TIPCL_SHORTCUT_POSIX=1` 是把「Windows 开发机上跑 Linux 分支」这条测试路径打开用的开关(见 D-23),仅影响 Shortcut 的路径判定。
### 2026-11-12 M4-H P5(主体完成)AI 工具链 + 权限 + 三张卡 + 自检 + 图片输入 + AiFixWindow(移植代理)

- **背景**:上一棒把「对话能跑起来」做实了(Markdown 链 / 气泡 / 输入区 / 流式),本轮把 AI 子系统的**行为层**补齐:工具调用、权限确认、结构化选择题、计划卡、任务卡、崩溃修复、自检、图片输入。参考版的核心 Agent 在 Python 侧(约 8000 行,依赖 Mcp/Checkpoint/Compact/Store 等 20 余个模块),Linux 侧没有 Python 桥,故按同一批**工具名**与**事件名**在进程内重实现,而不是照搬那套依赖。
- **Core**:新增 `Launcher/Ai/AiToolTypes.cs`(ToolMetaInfo / ToolMeta / ToolCall / ToolCallStatus)、`AiData.cs`(系统提示词逐字照搬 + 关键词工具表)、`Tools.cs`(34 个工具的 schema、标签、参数解析、错误码)、`ToolRunner.cs`(工具执行 + 权限裁决)、`AiAgent.cs`(带工具的会话循环)、`AiUiText.cs`(界面文案小工具)、`AiRisk.cs`、`AiMemory.cs`(remember/forget 落 `$TIPCL_HOME/ai-memory.json`)、`AiFixChat.cs`(崩溃修复开场上下文)、`AiSelfTest.cs`、`Conflict.cs`(从参考版原样搬,供 scan_mod_conflicts/inspect_mod)。
- **工具集口径**:名字与 schema 与 Windows 版一致(`ask_user / get_launcher_state / list_instances / list_installed_versions / search_* / list_mods / get_java_list / diagnose_launch / scan_mod_conflicts / inspect_mod / list_mod_configs / read_mod_config / write_mod_config / install_game / install_mod / install_modpack / install_shader / install_resourcepack / install_datapack / install_world / download_java / launch_game / create_instance / delete_instance / delete_mod / disable_mod / enable_mod / update_plan / remember / forget`),**Linux 没落地的能力(tune_jvm / backup_save / dispatch_subagent / read_artifact / MCP 工具)不写进表**,避免出现「点了报未知工具」的假条目。
- **权限**:五档与 Windows 同键同值(`config.json` 的 `ai_permission_mode`:default/acceptEdits/plan/yolo/custom);自定义规则 `ai_permission_rules` 也共用键。裁决顺序:自定义规则 → 只读直接允许 → 档位(plan 拒写、yolo 全允许、acceptEdits 只在删除类问)。删除类工具不提供「本对话都允许」(照参考版)。
- **事件链**:`delta / think / tool / tool_done / status / error / done` + `update_plan`/任务卡回调;页面按事件渲染步骤组、计划卡、任务卡、确认卡、选择题卡。
- **App 件**:`AiIcons`(参考版 25 个 SVG 常量 + 工具图标映射)、`ToolLine`/`StepGroup`(步骤行与折叠组,含进度条/失败计数/「用时」)、`AskCard`+`AskBlock`(单选/多选/「其他」补充)、`PlanCard`、`TaskCard`(完成给「启动游戏/打开文件夹」、失败给「重试」)、`ConfirmCard`(风险标签 + 预览 + 允许/本对话都允许/拒绝)、`AiPermissionPanel`(五档 + 规则增删)、`AiImages`(选图/粘贴/拖入,最多 4 张)、`AiFixView`(崩溃修复会话)。
- **`AiPage` 重写**:带工具的会话循环(同一 `_history` 跨轮保留 assistant/tool 消息)、步骤组/计划卡/任务卡渲染、底部停靠区放确认与选择题、图片附件行、权限档位标签。
- **崩溃界面接线**:`CrashUi` 的「交给 AI 修复」按钮(上一棒因缺 AiFixWindow 而暂缺)现在打开 `AiFixView`(Core 的 `AiFixChat.BuildCrashContext` 把实例/版本/退出码/摘要/详情/日志文件与「自己有工具、别拿没接口当借口、能修就动手」的指令一起给模型)。
- **自检**:`AiSelfTest.VerifyAsync` 32 项纯逻辑/功能检查(工具表、参数解析、风险分级、文案、选择题归一化、权限档位裁决、只读工具执行、写工具无确认被拒、未知工具、崩溃上下文、会话可构造),`--ai-selftest` 的 CLI 入口**尚未接**(当前由 xunit `SelfTest_AllChecksPass` 覆盖);参考版那个 WPF 界面自检无法照搬。
- **测试**(`tests/TipCL.Tests/App/AiToolsTests.cs` 15 项):自检全绿、schema 与元信息一一对应、关键词选工具、只读直接跑/写工具无确认被拒/「都允许」只问一次、权限档位与自定义规则、任务回调、计划工具、ask_user 归一化与答案回填、文案工具、崩溃上下文、卡片构造、图片附件限制。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;`dotnet test` **196 通过 / 1 跳过 / 0 失败**(181 → 196,新增 15 项);P5 收尾时复跑**全页冒烟矩阵:浅色 10 页 + 深色 10 页共 20 次会话全部 exit=0 出图**,11:30 之后日志 **WARN/ERROR 0 行**;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍是 `2026-11-08 17:13:58.504`。
- **遗留**:(1)真模型端到端未跑(需要真实 API key,属对外动作);(2)`--ai-selftest` CLI 未接;(3)会话记忆面板 / 上下文占用环 / 斜杠命令补全 / 附件预览缩略图未做;(4)`AiUiText` 与 `ToolRunner` 的权限判定尚无 Windows 侧对照数据(参考版在 Python 侧),按语义对齐而非逐行对照。
### 2026-11-12 M4-H P6 + P7 + P5 尾巴:插件宿主全链 + 统一拖放入口 + 自检入口(移植代理)

- **P0(主 agent 复核 A 节:CLI 未拒绝不存在的实例)**:现状复现成功 —— `-i bogus_inst launch 26.3` 与 `-i .minecraft ...` 输出逐行相同,**exit=3 来自「没有离线账号」**(与复核结论一致)。
  - 根因:`InstancesApi.EnsureInstance` 对未知名字「目录不存在就 `Create()`」;单根模式下 `Instances.ResolveName` 还会把未知名字直接吞成根实例。
  - 修法:**只在 CLI 入口**加前置校验(新增 `InstancesApi.FindInstance`,只查不改、不吞名字),不存在时打印「没有这个实例:<名字>」并以 `CliExit.Usage`(4)退出;`EnsureInstance` 的「不存在就创建」语义未动(安装/导入流程依赖它)。`--print-command` 也走同一校验(打印出来的命令必须是真能跑的)。
  - 实测:未知实例 **exit=4** 且不创建实例目录;`-i .minecraft launch 26.3 --print-command` 仍 exit=0;`-i` 单独出现 **exit=4**(原本就是 4,现在有测试锁死)。
  - 测试:`Cli_UnknownInstance_ExitsWithUsage`(断言 exit=4 + 输出含中文原因 + 目录未被创建)、`Cli_MissingInstanceValue_ExitsWithUsage`、`Cli_UnknownInstance_PrintOnly_AlsoFails`,替换掉旧那条「只要非零码就行」的用例。
  - 顺手清掉一个真实的编译警告(`AiToolsTests` xUnit2012),本轮 Debug/Release 均为 0 警告 0 错误。
- **P6 判定表(先判定再动手)**:`docs/PARITY.md` 新增 **§7 插件宿主全链逐项判定**,覆盖页面 11 件 + Core 17 件,逐项给「行数 / 结论 / 依据」。总判定:**文件级能力全真做;需要在本进程里跑插件代码或渲染插件网页的那一半不移植并写明理由**。
  - **更正一条前提**:简报里「DotNet 插件通常可以真做出来」不成立 —— SDK 契约 `TipCL.Plugins.*` 定义在 **net48 的 `TipCL` 程序集**里(`TipCL.csproj: AssemblyName=TipCL; TargetFramework=net48`),界面工厂签名是 `System.Windows.FrameworkElement`(`Sdk/PluginSdk.cs:4/481`)。Linux 侧既引用不到也不能加载按该契约编译的二进制,「真做」只能另立一套**不兼容**的新 SDK(没有现成插件可用),故判不移植。
  - **修掉一处假成功**:旧 `PluginHost.Start` 对 .NET 插件只做一次 `Assembly.LoadFrom` 校验就把状态标成 running(插件的 `Apply` 从未被调用)。现在 Node / .NET 两条路都返回 `ok=false` + 具体原因;启用开关改为「落盘 enabled + 回传 `runnable=false` + reason」,页面启用时用警告 Toast 如实说明。
- **P6 落地(文件级真做)**:
  - **Core 新增 `Plugins/PluginInstaller.cs`**:镜像候选下载 → sha256 → 包内清单 id/version 与索引一致 → engines(`SemVer.Satisfies(APP_VERSION, range)`)→ 依赖检查 → 解压 `.staging/<id>` → 旧版挪 `.backup/<id>` → 原子替换 → 写 `plugins.json` → 失败回滚。**去掉两步**:`npm install`(无 Node)与宿主加载(`SwapAndLoad`)。
  - `Plugins/PluginHost.cs`:新增 `Installer / InstallFromMarket / InstallLocal / Uninstall / Reload / SetEnabled / NoRuntimeReason`;`PluginMarket.ExtractWithPrefix` 由 private 改 public 复用(不再有第二份解压实现)。
  - **插件页四分段拆分**:`Pages/Plugins/PluginsPage.cs`(983 行单文件)→ `PluginsPage.cs` + `.Market/.Installed/.Settings/.Dev.cs`,与 Windows 同名同分段。
  - **市场分段**:改用 `PluginMarket.Rows`(带 installed/updatable/permissionRows),安装/更新按钮**真的装**(未审核门 → 权限确认 → 后台安装 → 刷新);市场地址读取失败会在搜索栏旁如实显示条数。
  - **已安装分段**:启停走 `SetEnabled`(如实提示)、卸载走 `PluginInstaller`(可选删数据)、新增「权限」面板与「从市场更新」;页顶固定一条「Linux 版没有插件运行时」的说明,不把这件事埋在开发者分段里。
  - **设置分段**:按 manifest 的 `config`(string/boolean/integer/number/enum)自动生成表单,值经 `PluginManifest.CoerceConfig` 校验后落 `plugins.json`;`secret` 与 `contributes.credentials` **不呈现**(凭据只有插件进程会读,且 Linux 无 SecretStore 实现)。
  - **开发者分段**:本地 zip/文件夹安装与开发链接改走 `PluginInstaller`(同一条暂存+原子替换链);运行环境卡如实写「Node 无宿主 / .NET 契约是 WPF / 无内置网页视图 / 文件管理可用」;删掉「添加供应商」假按钮(参考版那个列表由插件宿主消费)。
  - **壳侧与页面宿主**:新增 `Pages/Plugins/PluginUi.cs`(界面贡献快照 + `Changed`)、`Pages/Plugins/PluginHostPage.cs`(`plugin:` 页面);`Shell/MainWindow` 新增 `HookPlugins/RefreshPluginUi/OnPluginUiChanged/SyncPluginTabs` 与 `plugin:` 页面路由;**P8 动态顶部页签**由 `TitleBar.SetPluginTabs` 落地(`group:"top"` 按 Order 追加在 5 个内置页签之后)。
- **P7 统一拖放入口**:新增 `Common/FileDrop.cs` + `Common/FileKinds.cs`(对齐 Windows `Services/FileDrop` + `FileKinds`,kind 常量/文案/`Verdict` 形状一致):窗口级兜底(`MainWindow`)+ 目录页(带实例上下文)+ 插件页(偏好 `plugin`)+ 实例库 / 版本管理页;识别 → 分桶 → 认不准问一次 → 落到真实安装链(插件走 `PluginInstaller`,整合包确认后装成新版本,模组/资源包/光影/数据包/存档走 `install_*` 任务,皮肤走 `set_account_skin`)。**不列入壁纸**(Linux 启动页没有 `ui_background` 消费方),不做嵌套包递归探测 —— 见 `docs/PARITY.md` §8。插件页原来的私有 `HookDrop` 换成统一入口。
- **冒烟里又抓到一处小缺陷(已修)**:`UiContribution.Url` 只读贡献行顶层的 `url`,而 Linux 的贡献来自 manifest,地址在 `spec.url` 里 —— 导致插件页面框里「在浏览器打开」按钮不出现(参考版这一行是内核算好带 pageToken 的)。已补 `spec.url` 回落,并用「临时夹具 + 真冒烟」验证:`group:"top"` 的插件页签出现在标题栏「设置」之后,点进去是 `plugin:` 页面,网页地址给的是可点的浏览器入口。
- **P5 尾巴**:`--ai-selftest` CLI 入口接上(`Session/AiSelfTestCli.cs`, `Program.cs`),不起 GUI、不联网,报告打 stdout 并落日志,退出码 0/4;显式 `--ai-selftest-send` 也只打印「真模型端到端未跑(需要 API key)」。实测 32 项全过、exit=0。
- **测试**:新增 37 项(`Core/PluginInstallerTests` 16、`App/PluginUiTests` 7、`App/FileKindsTests` 9、`App/AiSelfTestCliTests` 2、P0 的 CLI 3 项),替换 1 项;全量 **232 通过 / 1 跳过 / 0 失败**(本轮起点 196)。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;冒烟 `plugins/launch/settings/library/ai` 明暗共 7 次会话全部 **exit=0 出图**,12:22 之后 `_cmp/logs` **WARN/ERROR 0 行**;`--ai-selftest` exit=0;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍为 `2026-11-08 17:13:58.504`。
- **遗留**:(1)插件代码仍然跑不起来(Node 宿主/.NET 契约两条硬约束,属判定为「不移植」而不是欠账,插件页已如实告知);(2)市场详情页的截图只列链接、不内嵌缩略图(无 WebView/缩略图解码链);(3)已安装页不显示「有更新」徽标(只在市场页显示),更新入口在卡片菜单里现取索引;(4)`PluginEvents` 的 `plugins.confirm/ui_request/error` 与 Overlays/ActionBar/DashCards/AiViewStrip 不移植(无宿主来源);(5)拖放未做嵌套整合包递归探测;(6)真机(Ubuntu 桌面)拖放与双击 `.desktop` 仍待盒子验证;(7)真模型端到端仍留给用户。

### 2026-11-12 M4-H R1~R4 收尾:AI 服务层 + AI 界面残留 + PhoneSyncPanel + 两处小尾巴(移植代理)

- **R1 AI 服务层(对照 Windows 同名件逐个补齐)**
  - **检查点与回滚**:新增 `Core/Launcher/Ai/Checkpoint.cs`(内容寻址快照落 `$TIPCL_HOME/cache/ai_checkpoints/<chat>/`,journal 记 seq/turn_id/files/dirs/bytes;回滚按 seq 逆序重放 + 删掉「当时不存在」的新增项 + 还原内容与 mtime;上限照参考版:单轮 512MB、单会话 1GB、单目录 20000 文件)与 `Rewind.cs`(回退上一轮:对话退到最后一条非插队用户消息之前 + 检查点还原 + 事件流标 `TurnRewound`,同一次退过的轮次不会重复退)。
  - **上下文压缩**:`Compact.cs` + `CompactTypes.cs`(`AutoConfig/MicroConfig/MicroResult/CompactState/CompactionResult` + `RapidRefillBlocked`):整段历史摘要(`[历史摘要]` 标记带 `compact_<id>` 与 keep)、微压缩(旧工具结果换占位串,够省才动手)、触发判定(窗口 - 预留余量)、连续失败与「压完又满」双熔断;切点不会落在 tool 消息上(否则接口会拒)。
  - **令牌与用量**:`Tokens.cs` + `TokenState.cs` + `Usage.cs`(CJK≈1 / 其余≈0.25 的估算;provider 回传 usage 就校准基准,之后只估算新增;按对话累计 input/output/requests 与 `real_requests`,页面据此画占用环)。
  - **MCP 客户端**:`Mcp.cs` + `McpClient.cs` + `McpError.cs`,**自己实现的 stdio JSON-RPC**(一行一条、initialize → notifications/initialized、tools/list、tools/call;连接 10s / 调用 60s,超时杀子进程;server 名只允许字母数字-_;工具名 `mcp_<server>_<tool>`,按最长 server 名反查);**不引第三方 MCP 包**。`McpServer`(把工具开放给外部 agent)判不移植,理由见 D-28。
  - **子代理**:`dispatch_subagent` 派一个**只读**子代理(子执行器固定 plan 档、最多 4 轮、不许再派),结论以 `{ok,answer,stop_reason}` 带回。
  - **产物回读**:`Artifacts.cs` + `AiFs.cs`,超长工具结果(>8000 字)落 `cache/ai_results/` 并只回「文件名 + 总行数 + 前 120 行」,模型用 `read_artifact` 分段读回;7 天 / 300 个的回收照参考版。
  - **其余按需件**:`Store.cs`(`ai_chats.json` + `cache/ai_sessions/*.jsonl` 会话事件流)、`Trace.cs`(`cache/ai_trace/*.jsonl`,14 天)、`StopReason.cs`;`Prompt/Diagnose/Permission/ModConfig` 已是等价实现(见 D-28),`Hooks/AgentPresets/ChatEventLog` 不移植。
  - **接线**:`AiAgent` 写操作前按 `Tools.AffectedPaths` 做快照、记 TurnStarted/ToolStarted/ToolCompleted/TurnFailed、每轮前判压缩(回传 `compact` 事件)、usage 事件带用量快照、每轮连/关 MCP;`Tools` 补两条 schema 与 `AffectedPaths`,`ToolRunner` 收 `read_artifact`/`dispatch_subagent`/MCP 路由(写档与超长结果落盘都在这一层)。
- **R2 AI 界面残留**
  - `AiChatOps.cs`(斜杠命令纯逻辑,原样搬):7 条命令 + 别名、前缀优先的匹配、上下移动、撤回轮次计算、工具结果文案解析;`SlashMenu.cs` 候选条 + `ChatInput` 的 `PreKey/TextChanged` 钩子(上下键/Tab/Esc/点击;Enter 直接执行)。
  - `AiMemoryPanel.cs`:记忆查看/编辑/删除/清空,走 Core `FileMemory`(与设置页「记忆」卡同一份 `ai_memory.md`);AI 页头部加「记忆」入口。
  - 头部**上下文占用环**(`AiIcons.Ring` + `Usage` + `AiUiText.ContextRatio`,窗口取 `ai_context_window`,tooltip 给输入/输出与「(估算)」标记)。
  - **对话持久化**:首次用到时读 `Store`,每轮结束写回,回页读回并重画;`/新对话`、`/清空`、**`/撤回`(真接 `Rewind`:对话截断 + 文件还原 + 重画)**。
- **R3 PhoneSyncPanel**:新增 `App/Pages/Settings/PhoneSyncPanel.cs`(205 行 Windows 版的世界同步/对比面板:电脑实例×手机存档根、逐行裁决、传到手机/传到电脑(覆盖前确认)、一键同步(冲突逐个问)、日志框),数据面复用 Core `PhoneLink`/`SaveSync`;设置页手机同步卡补「同步面板」入口(没连接只提示,不开空面板)。PARITY 第 85 行由 ❌ 改 ✅。
- **R4 两处小尾巴**:已安装插件卡片补**「有更新 → vX」徽标**(读市场索引的 `updatable` + 版本号,菜单项同步变「更新到 vX」;索引后台读一次,失败静默);市场详情**截图真的显示**(`Common/Thumbs` 远程图 + 内存缓存,横排缩略图,失败给「点击用浏览器打开」占位)。
- **测试**:新增 **67 项** —— `Core/AiServicesTests` 15(Tokens / Compact / Usage)+ `Core/AiServiceFilesTests` 29(Checkpoint / Rewind / Store / Artifacts / Mcp)、`App/AiChatOpsTests` 14(斜杠命令 + 记忆面板 + 占用环)、`App/PhoneSyncTests` 6、`App/PluginUpdateBadgeTests` 3。全量 **299 通过 / 1 跳过 / 0 失败**(本轮起点 232)。`--ai-selftest` 由 32 项扩到 **50 项**(新增压缩/检查点/回退/产物/用量/MCP/新工具),实测 exit=0。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;冒烟:AI 页明暗 2 次、设置页内层 3 次、插件页明暗 2 次全部 **exit=0 出图**,`_cmp/logs` **WARN/ERROR 0 行**;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间未变(冒烟夹具 `TIPCL_HOME=_cmp`)。
- **遗留**:(1)**真模型端到端**仍需真实 API key(对外调用,留给用户);(2)`McpServer` 不移植(依赖 `BackendApi`,且外部客户端没有用户点确认);(3)确认卡的**改前改后 diff 预览**(参考版 `Preview.cs`)未做,确认卡目前给工具名 + 中文动作说明 + 参数;(4)参考版 `ExtraTools` 的通用 fs 工具面不移植(只保留 `config/` 白名单的 `write_mod_config`);(5)手机同步面板的对比/传输要真机(连着手机)才能端到端验证,本轮只覆盖「未连接时的诚实提示 + 裁决文案 + 本地目标枚举」;(6)市场截图在真实网络下的显示效果需用户环境确认(本机冒烟时市场索引/图片可能取不到)。

### 2026-11-12 M4-H R5:权限确认卡的「变更预览」接通(移植代理)

- **问题(主 agent 复核发现)**:Linux 侧的确认卡**界面早就会画 diff**(`Pages/Ai/ConfirmCard.cs` 的 `PreviewBox` + `AiUiText.LooksLikeDiff`),但两个调用点都写死 `new List<string>()`,预览永远不会出现;根因是 **Core 侧缺 `Preview`**(参考版 `Core/Launcher/Ai/Preview.cs`,由 `BackendApi.ConfirmFn` 调用)。也就是「写文件前先看清改了什么」这个安全能力在 Linux 上是空的 —— 代码路径都在,只是永远收不到数据。
- **新增 `Core/Launcher/Ai/Preview.cs`**(照参考版等价改造,结构键名逐字对齐):
  - `ChangePreview(name, args, instance)` → `{kind, head, lines, too_large}`;实例目录按 Linux 既有的一条解析(`Instances.GetInstancePath`,与 `Tools.AffectedPaths` 同源,参数 `instance` 优先);
  - `write_mod_config` → 逐行 unified diff(自实现 `MatchingBlocks`/`Opcodes`,等价 `difflib`:含热门行剔除、`@@ -1 +1,2 @@` 区间写法、`--- a/` `+++ b/` 头);新建文件给「(新建文件)」;超 200 行落盘(`Artifacts.Save`)并写明去向;
  - `delete_mod` / `disable_mod` / `enable_mod` → 将被删除/改名的逐文件清单 + 总大小(文件不存在时如实写「没找到这个文件」);
  - `delete_instance` → 目录 + 文件数/总字节(超 5000 文件标 `>`)+ 不可恢复;`install_*` / `download_java` / `create_instance` → 安装目标 + 预计新增文件数;`launch_game` → 启动目标(info);
  - `Flatten(preview)` = 参考版界面那一步(head 以 `@@ ` 开头、丢空串),**两个页面共用这一处**,不再由调用点自己拼行。
- **接线**:`ToolRunner.Confirm` 回调增第三参(预览字典),`RunAsync` 在 `WAITING_PERMISSION` 之后、执行之前算好预览再回调(算失败就当没有,绝不挡这一步);`AiPage.ConfirmAsync` / `AiFixView.ConfirmAsync` 把 `Preview.Flatten(preview)` 交给 `ConfirmCard`。确认卡的风险标签、「本对话都允许」(删除类不给)、拒绝/接受的语义**一行未改**。
- **顺手清掉的同类断链**:`TaskCard` 的工具名两个页面原先都写死 `""`(卡片靠它决定后续动作),现在由 `TaskStarted(taskId, label, tool)` 带回,并加测试锁定;`ConfirmCard` 第三参(参考版从权限裁决带出的理由)在 Linux 侧仍留空,**原因写在调用点注释里**:`ToolRunner.Decide` 只给 allow/ask/deny、没有理由文本,不为此编一句假话。AI 页其余 `""` 占位是设计如此(斜杠命令的 prompt / local 二选一),不是断链。
- **测试:新增 18 项**(本轮起点 299 → 全量 **317 通过 / 1 跳过 / 0 失败**):
  - `Core/AiPreviewTests` 13 项:新建 / 追加 / 修改(含删行)/ 路径越界 / 大文件截断(200 行 + 落盘去向)/ 删除模组(含找不到)/ 启停改名 / 删实例 / 安装与启动 / 只读工具无预览 / `Flatten` 规则;每条都断言 **`AiUiText.LooksLikeDiff` 认得出**(认不出就不会着色,等于白算)。
  - `App/AiConfirmChainTests` 5 项:①`ToolRunner` 的确认回调真收到非空 diff,且**此时还没落盘**;②删除类收到清单;③只读工具不触发确认;④**驱动页面自己的确认回调**,在页面控件树里取到那张卡,断言 `PreviewLines` 非空可识别、且 `-a = 1` / `+a = 2` 两行各自框的**底色就是确认卡的 diff 红/绿**(连渲染一起验);⑤任务卡拿到工具名。④做过反证:把调用点改回 `new List<string>()`,它立刻变红。
- **验收**:Debug / Release 全量重编 **0 警告 0 错误**;`ai` 页冒烟 **exit=0 出图**(`_cmp/smoke.png`,15:21:57),`_cmp/logs` **WARN/ERROR 0 行**;真实 `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍为 `2026-11-08 17:13:58.504`(`ai_chats.json` 亦未动)。视觉证据:`_cmp/R5-preview-card.png`(无头渲染的确认卡:红/绿着色逐行 diff + `@@ 将要写入的变更:` + 风险标签「写入」+ 允许/本对话都允许/拒绝)。
- **遗留**:(1)**真模型端到端**仍需真实 API key(对外调用,留给用户);(2)确认卡第三参(权限裁决理由)留空,原因见上,要补就得先在 `ToolRunner.Decide` 里给出理由文本;(3)`Preview` 只覆盖 Linux 侧真实存在的写工具,以后新增写工具要同步加一条分支(参考版同样是一张 switch),已由「认不出就返回 null」兜住;(4)`_cmp/` 仍未进 `.gitignore`(前几轮起就是未跟踪状态,本轮未动)。

### 2026-11-12 M4-H R6 真模型端到端验证(移植代理)

- **做法**:一次性探针(临时 xunit 文件,**验完即删、不入库**)在**隔离 HOME**(`%TEMP%\tipcl-r6-e2e\home`,进程内自建的空目录 + `Utils.SetRoot` + `Config.Reset()`)里按应用正常用法走真实链路 —— `ai_mode=builtin` → `Client.ResolveEndpoint` 取 `Defaults.DEFAULT_API_BASE` 与内置令牌(全程不打印、不落盘、不进提交)。真实 `%LOCALAPPDATA%\TipCL` 未读未改(见文末验收)。报告留在 `%TEMP%\tipcl-r6-e2e\report.md`,另存一份到 gitignore 的 `_cmp/r6-probe-report.md` 作证据。
- **Phase A 端点与连通**:`mode=builtin`、`model=deepseek-v4-flash`、`X-TipCL-Client=TipCL/1.0.10`、`GET /v1/models` → `内置网关正常,模型 deepseek-v4-flash`(238 ms)。
- **Phase B 流式对话**(`Client.ChatStream` 原样事件):delta 事件 **1 条**、最终文本 `收到`(**非空**)、**usage 事件带 provider 真值** `prompt_tokens=46 / completion_tokens=2`、`finish_reason=stop`;首增量 2062 ms,总耗时 2070 ms。
- **Phase C 工具链闭环**(`AiAgent` + `ToolRunner` 真跑一轮):本轮事件共 51 条 —— delta 43 条 + think 2 条,其余是 `tool` / `tool_done` / `status` / `usage` / `done`;`tool` 与 `tool_done` 都指向 `list_instances`,后端 `get_instances` 真被调用(1 次)→ 工具结果回填成 `role=tool` 消息 → **模型在下一轮把只存在于工具结果里的随机口令 `PROBE-6DA4AB4F` 原样说了出来** → 闭环**成立**。`stop_reason=no_tool_call`,`finish_reason=stop`,用量 `input=5804 / output=68 / real_requests=2`(`Usage.source=provider_usage`),耗时 3562 ms。
- **Phase D 上下文压缩**(把 `ai_context_window` 临时设成 8192 → 阈值 `max(0, 8192-13000)` = 0,必然触发):判定 `should=True / above_threshold`;`compact` 事件 1 条;**消息 26 → 9 条**,估算 token **2879 → 2500**,压缩后第 2 条是**真模型写的 `[历史摘要]`**(含 `<think>` 推理段;探针断言其长度 > 60 字,报告里只显示前 220 字),随后本轮照常出 34 条 delta 增量,`stop_reason=no_tool_call`,并新增一条 `CompactBoundary` 会话事件。**压缩在真链路上真的生效、不崩**。
- **Phase E 写操作 → 检查点 → 回滚**(写由**真模型发起**):模型调用 `write_mod_config`(`instance=probe`,`path=probe.toml`,`content=A=2`);确认回调**真收到预览** `diff/6 行` + 标签 `改配置 probe.toml @ probe`;工具落盘路径与 `Tools.AffectedPaths` 快照清单**完全一致**;`Checkpoint.TurnHasOps(本轮)=True`;本轮开始前 `A=1` → 写入后 `A=2` → 探针弄脏文件 → `Rollback={"ok":true,"restored_files":1,"restored_bytes":3,"failures":0}` → **回到 `A=1`**。另做一次不走模型的对照:快照路径 == 落盘路径 且 `B=1 → B=2` → 回滚 → `B=1`。**快照/回滚闭环成立**。
- **Phase F 会话事件流**:`cache/ai_sessions/*.jsonl` 与事件一一对上(`TurnStarted/ToolStarted/ToolCompleted/TurnCompleted`;压缩那轮多一条 `CompactBoundary`)。
- **网关侧观察(外部依赖,非本仓 bug)**:该网关对上游渠道限速很紧,实测报错原文 —— `HTTP 429 渠道 #42 达到限速上限(RPM=12 TPM=200000),排队 0.0s(max_wait=5s)仍无令牌,切换其他渠道 (request id: …) new_api_error`。第一轮探针(请求挨着发)Phase B/D/E 连续撞 429;把阶段间隔放到 12s、开头冷 15s、失败**只重试 1 次**后一次跑通。末尾那次「兜底再压一次」仍撞了 429(不影响 Phase D 本身已成立)。另有一次流式请求在 15s 内拿不到响应头,被 `STREAM_CONNECT_TIMEOUT=15` 判超时(`连不上接口: Read timed out. (read timeout=15)`),与限速排队同时出现。
- **验收**:探针文件已删除;`git status` 只有本轮两份文档改动;`dotnet build` Debug + Release 均 **0 警告 0 错误**;`dotnet test`(带 `DotNetHostPath`)**317 通过 / 1 跳过 / 0 失败**;真实 `%LOCALAPPDATA%\TipCL\config.json`(`2026-11-08 17:13:58`)与 `ai_chats.json`(`2026-11-08 17:12:29`)修改时间未变。
- **遗留**:真链路只覆盖了「只读工具 + 写配置」两条;**长任务**(`install_*` / `launch_game`,要真下载/真启动)、`dispatch_subagent`、MCP 外部 server 仍未在真链路上跑(分别需要真实游戏数据与外部进程);网关限速是复现真链路时必须考虑的外部变量。

### 2026-11-12 M4-H R7 限流 / 5xx 重试(移植代理;主 agent 实测发现缺口后追加)

- **缺口来源(实测)**:主 agent 直接打内置公益网关,`POST /v1/chat/completions` **第一次调用就 429**(网关原文:`渠道 #42 达到限速上限(RPM=12 TPM=200000)`),重试第二次才 200。对照参考版发现:`AIClientError.Retryable()` 与整套退避重发在 Windows 侧是有的(`Agent.cs:829` 的判据、`:1697` 的 `_sleep_backoff`),而 **Linux 的 `AiAgent` 一个重试都没有**(`Task.Delay`/`attempt` 全无,`rate_limited` 只被标记类别没人消费)→ Windows 遇限流自动重发,Linux 直接把错误抛给用户。
- **落地**(`src/TipCL.Core/Launcher/Ai/AiAgent.cs`):`StreamOnceAsync` 改成「重试驱动循环」,原发流式请求抽成 `StreamAttemptAsync`。触发口径照参考版 `Retryable()`:`429/500/502/503/504` 与 `Category == "rate_limited"`,以及参考版口径里的 `provider_timeout / provider_network_error / provider_stream_error`;401/403(认证)、上下文超长、参数错**一次都不重试**。每轮最多重试 3 次(合计最多 4 次请求),退避 `min(4, 2^(n-1))` 秒 × 0.8~1.2 抖动;退避用 `await Task.Delay(wait, ct)`,点「停止」立刻断。**已经吐出正文/思考段或拿到 `tool_calls` 就绝不重发**(`state.Text.Length == 0 && state.Calls.Count == 0 && attempt.PendingTools.Count == 0`),避免用户看到重复内容。
- **可观察**:重试时发 `status` 事件(状态行显示「接口HTTP 429,正在重试(第 1/3 次,约 1.2 秒后)」)+ 会话事件 `ModelRetry` + `Trace.Record("retry", ...)`,界面不会静默卡住。
- **三处有意偏离**(报告写明理由):1 不搬参考版的 `_maybe_fallback`(换模型兜底)——Linux 没有 bundled 备用模型配置,只搬「原地重发」这一半,用尽后仍 `stream_failed`;2 `Thread.Sleep` → `Task.Delay`(尊重取消);3 先算抖动再报,状态文案里的秒数与真实等待一致。
- **测试**:新建 `tests/TipCL.Tests/Core/AiRetryTests.cs`(6 项 + 本机 `TcpListener` 假网关、随机空闲端口,不联网):429→200 且请求数=2;连续 429 请求数=4、最终 `Category == "rate_limited"`;401 请求数=1(不重试);503→200 重试;已吐正文再报限流请求数=1(不重发);退避期间取消 <700ms 抛出且不再发第二次。
- **验收(主 agent 复跑)**:Debug / Release 全新重编 **0 代码警告 0 错误**(出现 8 条 `NU1900`——NuGet 拉华为镜像漏洞库失败,属既有环境噪声,文档此前已注明);`dotnet test` 连续 2 次 **323 通过 / 1 跳过 / 0 失败**(基线 317+1,新增 6);真实 `%LOCALAPPDATA%\TipCL` 数据未改。
- **已知遗留**:`StopReason.RATE_LIMITED` 仍无人消费(参考版同样只在定义处出现,不参与流程;要区分限流失败可读 `ModelRetry` 事件的 `category`);网关单渠道限流很紧(RPM=12),排队时 15s 的流式连接超时偏紧——现在这类超时已能重试,但仍有首次即超时的可能。

### 2026-11-12 M4-H R8 真机验证(Ubuntu 云盒子)(移植代理)

> ⚠️ **2026-11-13 更正**:本小节「点版本设置齿轮把应用打崩」的**归因有误** ——真正崩的是**实例详情页的首次布局**(`InstanceDetailPage` 的孤儿 Border 把 `_rows` 收成子级),而实例库那枚齿轮的提示文字写着「版本设置」、点击实际是打开实例详情页。根因与修法见 `M4-H R9`。

- **本轮目的**:把产物送到真机(盒子 `main`,Ubuntu/Xfce,`DISPLAY=:0`)跑三件只有真机能验的事 —— 发布产物能不能跑、桌面快捷方式双击能不能启动、拖放导入能不能进;做不了的如实写清为什么。全程**不改产品代码、不动版本号、不 commit / 不 push、不碰 Windows 参考仓库、盒子凭据不落任何文件**;证据统一放 `_cmp/`(gitignore)。
- **A 发布产物真的能跑(完成)**:用项目自己的发布命令 `tools/package.ps1 -SkipWin` 出 `linux-x64` 自包含单文件 `dist/TipCL-linux-x64/TipCL.App`(**44,965,425 B**,ELF,sha256 `881b9be9…`);tar 上传到盒子**新目录** `/home/user/tipcl/tipcl12`(既有 `tipcl` / `tipcl8`–`tipcl11` 与旧 `smoke.png`、`smokehome/` 一律没动),`chmod +x` 后以**隔离 HOME** `TIPCL_HOME=/home/user/tipcl/smoke12` 跑 `--smoke`:8 页(launch / library / download / plugins / settings / ai + library-dark / plugins-dark)**8/8 退出码 0 且都产出 PNG**,`$TIPCL_HOME/logs/tipcl-20261112.log` **0 WARN / 0 ERROR**(78 行全 INFO)。证据:`_cmp/box-smoke-*.png`(逐页)、`_cmp/box-smoke12-log.txt`。
- **A 的两个真机观察**:(1)**首批执行的 4 页截图是「上一帧残留」** —— tipcl12 头 4 次冒烟把 launch 页的帧原样写进了 library / download / plugins / settings 四个文件名里(四份 PNG 字节完全相同 `bbf203ca…`);随后 **20 次复跑(含清掉自解压缓存 `~/.net/tipcl12` 后按原顺序重跑)再没复现**,每次都是正确且各自不同的页面。残留帧存 `_cmp/box-smoke-stale-batch1-*.png`,正确帧存 `_cmp/box-smoke-*.png`。(2)**同一页面两次渲染会差 14~40 字节**(如 plugins 53637 / 53623),所以**PNG 字节哈希不能当「页面身份」判据**;要稳需显式加 `SMOKE_WAIT`。
- **B 桌面快捷方式双击(完成,但入口走的是等价路径)**:`.desktop → 应用自身 CLI → 启动预检`这条链**真的跑起来了、不是「点了没反应」** —— `gio launch` 后应用日志与 stdout 打出:`实例: 26.3 | 版本: 26.3`、`实例 Java 设置: 自动选择`、`启动游戏前请先创建一个离线用户(「账号」页 → 添加离线账号)`、`=== CLI 退出(Launch,退出码 3)===`。**但 GUI 自带的「创建桌面快捷方式」入口在真机上打不开**(见下),所以 `.desktop` 是用**应用自己的 Core 代码**在开发机上以 `TIPCL_SHORTCUT_POSIX=1` 真跑 `Shortcut.CreateLaunchShortcut` 生成,只把 `Exec=` / `Path=` 两行改成盒子上的真实值;`Icon=` 指向的 `$TIPCL_HOME/tipcl.png` 由 `ShortcutIcon.Render()` 真跑生成(839 B,sha256 `84a7a3e4…`)。落到盒子上的两份:`~/Desktop/26.3 - 26.3.desktop`(220 B,mode **755**,sha256 `6c5fb4b2…`)与 `~/Desktop/1.20.1 - 26.3.desktop`(226 B,sha256 `4b0623db…`),后者 `gio launch` 得到的是同一个链路的另一条预检结论 `版本未安装: 找不到 1.20.1 的版本 JSON`(退出码 3)。证据:`_cmp/box-gui12-log.txt`、`_cmp/box-desktop-*.png`。
- **B 的一个环境事实**:盒子桌面上**不显示 `~/Desktop` 里的图标**(截图 `_cmp/box-desktop-40-desktop-with-file.png` 里桌面是空的),所以没有「桌面上看得见图标」的截图;快捷方式文件本身改用「内容 + 权限位 + sha256」核实。
- **真机顺带暴露的三个问题(都已复现,本轮只记录不改)**:①**点版本行的齿轮(版本设置)直接把应用打崩** —— `System.InvalidOperationException: The control StackPanel already has a visual parent Border while trying to add it as a child of ScrollContentPresenter (Name = PART_ContentPresenter, Host = ScrollViewer)`,`unhandled(UI)` 后进程退出,**2/2 复现**(日志 `~/.tipcl/logs/tipcl-20261112.log`)。②**版本行右键菜单在真机上打不开** —— 同一坐标左键双击能切到启动页(证明命中该行)、悬停高亮也生效,但注入 button-3 后既不弹菜单、`xwininfo -root -tree` 里也没有新窗口(前后共 7 次,含按像素网格量准的行内坐标);而实例卡的「•••」按钮走**同一个** `ContextMenuFx` 能正常弹出。③**界面里看得见的版本,启动时报「版本未安装」** —— 界面「版本选择」列的是 `<TIPCL_HOME>/.minecraft/versions`(全局目录)里的 `1.20.1`,而 CLI 预检找的是实例目录 `<TIPCL_HOME>/.minecraft/26.3/versions`。
- **C 真机拖放导入(未完成,根因已定位)**:launcher 窗口**没有 `XdndAware` 属性**(`xprop` 实测;同一脚本查 Chrome 窗口 `XdndAware(ATOM) = BITMAP` 正常),按 X11 拖放协议**任何程序都无法向它发起拖放**;再用**真实拖放源**(Thunar 打开 `~/drop-test`,放 `example-mod.jar`,内含 `fabric.mod.json`,按 `FileKinds` 应判「模组 jar」)实拖一次:截图 `_cmp/box-c-52-dragover.png` 里能看到拖影落在应用窗口内、Thunar 图标变蓝选中,松手后**应用毫无反应**(实例 `mods/` 目录无变化、应用日志无新行,`_cmp/box-c-53-dropped.png`)。所以这条真机路径**不可用**,不是自动化脚本的问题;没有伪造任何数据。证据:`_cmp/box-c-50-setup.png`、`-51-before-drag.png`、`-52-dragover.png`、`-53-dropped.png`。
- **D 手机同步真机传文件(未验证 / 条件不具备)**:需要手机与盒子在同一网络、且手机侧同步服务在跑,本轮无手机、无该服务,**不验证、不编数据**(按方案的「未验证/条件不具备」记)。
- **验收**:`_cmp/` 里证据齐(文件名见上各条);本节保持 CRLF;`DECISIONS.md` 追加 D-34~D-37 并保持 LF;未 commit / 未 push / 未改版本号;盒子用完 `sleep`。

### 2026-11-13 M4-H R9 崩溃根因修复(实例详情页)+ 版本列表语义(移植代理;主 agent 复核)

- **缺陷 A:真机必崩的根因不是「版本设置」对话框,而是「实例详情」页的首次布局。** `Pages/Library/InstanceDetailPage.cs` 构造函数遗留 `var card = UiClass.Card(_rows, 0);` —— 这个 Border **从未进可视树**,却把页面唯一的行 StackPanel `_rows` 收成自己的子级;随后 `ShowSeg()` 又把**同一个** `_rows` 赋给 `new ScrollViewer { Content = _rows }` → 一个控件两个父级。给未进可视树的 ScrollViewer 赋 `Content` **不当场报错**,要等它第一次 `Measure → ApplyTemplate → ScrollContentPresenter.AttachToScrollViewer` 绑 Content 才抛;异常从 `MediaContext.Render()` 的布局回调冒到 Dispatcher 就是 `unhandled(UI)` → **进程直接退出**(所以真机现象是「点完按钮进程突然没了」,不是弹错误框)。
- **触发路径(解释了此前的误判)**:实例库 `LibraryPage.cs:536-537` 那枚 Tooltip 写「版本设置」的齿轮,点击实际是 `OpenDetailAsync` → 打开实例详情页;版本管理页的同名齿轮才走 `VersionSetupDialog`(不崩)。真机那次点的正是前者 → 被误记为「版本设置对话框打崩应用」(已在 R8 小节与 D-36 处加更正标记)。
- **为什么以前的无头测试抓不到(两件事必须同时成立)**:1 无头会话不走渲染计时器,`await Task.Delay` 不触发 `Measure`,模板永不 ApplyTemplate,非法父级一直潜伏 —— 必须显式推一次渲染(`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` + `Dispatcher.UIThread.RunJobs()` + `CaptureRenderedFrame()`,即冒烟截图那条路);2 旧复现台 `Navigate("instance")` 构造的是版本管理页,压根没构造 `InstanceDetailPage`。
- **修法(根因级)**:删掉孤儿 Border;`_rows` 的容器改为字段 `_rowsHost`(只创建一次、只挂一次),`ShowSeg` 只切分段状态与数据、不再重建容器 —— 顺带堵住「切分段第二发」时旧容器 presenter 仍持有面板的同类隐患。
- **缺陷 B:同一语义被算成两个路径。** `Instance("")` 落根实例,所以 `get_version_rows` 不传 `instance` 时返回的是**全局** `<TIPCL_HOME>/.minecraft/versions`;而页面兜底扫描、行操作、CLI 启动预检看的都是**实例目录** → 界面里看得见的版本、启动时报「版本未安装」。已修三个调用点(`LibraryPage` / `VersionManagePage` / `InstanceDetailPage`)并加测试。如实说明:3 条页面级渲染断言写后被删(共享无头会话下页面异步灌数据时机不稳),**不留假测试**。
- **缺陷 C 结论**:不是 XTest 注入伪象 —— `LibraryPage` 本来就没挂版本行菜单(菜单挂在实例卡上);主 agent 复核 Windows 参考版 `Pages/LibraryPage.cs` 后确认**它的版本行同样没有右键菜单**(行尾是 收藏/卸载/版本设置 三个内联按钮,右键菜单是实例卡的 `打开文件夹 / 设为默认实例 / 选择 Java... / 管理模组 / 存档管理... / 重命名... / 删除实例`,`:899` 起)→ 两边一致,**不是移植缺口**。
- **新增测试**:`tests/TipCL.Tests/App/InstanceDetailCrashTests.cs`(含「打开→切分段→再开」与强制渲染 pass)、`tests/TipCL.Tests/App/VersionListScopeTests.cs`;**负向对照**把孤儿 Border 那行注回 → 5 条回归 5/5 红,删掉即全绿。
- **验收(主 agent 复跑)**:Debug / Release 全新重编 **0 代码警告 0 错误**(8 条 `NU1900` 为 NuGet 镜像联网噪声);`dotnet test` 连续 2 次 **329 通过 / 1 跳过 / 0 失败**(基线 323+1,新增 6);`library` / `instance` 冒烟 exit=0 出图、日志 0 WARN/0 ERROR;真实 `%LOCALAPPDATA%\TipCL` 未动;上一棒的 `scratch-out.txt` 与 `ScratchReproTests.cs` 已删除。
- **遗留**:真机**差分复验**(旧产物 `tipcl12` 应崩、含修复的新产物应不崩)由 R11 在盒子上执行;本轮未上盒子。

### 2026-11-13 M4-H R11 真机差分验收(实例详情页崩溃:修复前必崩 / 修复后不崩;主 agent)

- **目的**:R9 已在宿主侧用无头测试证明根因并做了负向对照,但**没上真机**;本轮补的是同一台真机上两个产物的差异。
- **环境**:盒子 `main`(Ubuntu 24.04.5 / XFCE4 / `DISPLAY=:0` / 1280×800),`TIPCL_HOME=/home/user/tipcl/smoke13`;两个自包含单文件产物 `tipcl12`(修复前,**44,965,425 B**)与 `tipcl13`(修复后,**44,965,362 B**)。
- **路径**:`xdotool` 点顶栏「实例库」(370,158)→ 点版本行行尾那枚 tooltip 写「版本设置」的齿轮(1026,350)——就是 `LibraryPage` 里执行 `OpenDetailAsync` 的那一枚(`:539-540`)。
- **结果**:`tipcl12` **进程直接没了**(`alive=0`),日志 `17:55:17.867 [ERROR] unhandled(UI)` + `System.InvalidOperationException: The control StackPanel already has a visual parent Border while trying to add it as a child of ScrollContentPresenter (Name = PART_ContentPresenter, Host = ScrollViewer)`,堆栈一路到 `Program.Main`;`tipcl13` **`alive=1` 且 0 条 unhandled**,同一坐标打开的是实例详情页(头部「26.3 / 1个版本」+ 分段条 + 空态)。R9 的根因归因在真机上得到同型日志确认。
- **判据**(不看 PNG 哈希):进程是否存活 + 日志 `unhandled` 计数 + 截图内容。同页两次渲染字节可差 14~40 B,哈希不能当页面身份判据。
- **顺带的独立佐证**:同一个实例面板,`tipcl12` 列的是全局目录里的 `1.20.1`,`tipcl13` 列的是实例目录里的 `26.3` —— 与 R9 对 `Instance("")` 落根实例的分析一致(缺陷 B)。
- **顺带发现**:两个产物**每次启动**都有一条 `[WARN] 启动提示失败: Cannot show window with non-visible owner.` → 立 R12。
- **归档**:`progress/M4-H-R11-真机差分验收.md`(含坐标怎么量准、`pgrep` 该用产物路径、rsh 脚本行尾必须 LF、取图别用跨行宽正则这些教训);证据 `_cmp/r11i-*.jpg`、`_cmp/r11i-out.txt`。

### 2026-11-13 M4-H R12 启动提示时序 + 版本行齿轮对位(移植代理;主 agent 复核 + 真机验收)

- **缺陷 1(启动提示在窗口可见之前触发)**:`App.OnFrameworkInitializationCompleted` 在 `desktop.MainWindow = win` 之后**立刻**调 `RunStartupPromptsAsync()`,此刻窗口还没 `Show()`;现场每次启动都带一条 `[WARN] 启动提示失败: Cannot show window with non-visible owner.`。真机差分(`first_run=true` 的全新 HOME)把精度钉死了:抛的是**第一个**提示(首启向导,无延迟),异常中断整个 `RunStartupPromptsAsync`,于是排在后面的同意门(自带 400ms 延迟,单独跑其实弹得出来)一次都轮不到 ⇒ **真实首启时向导和同意门双双没出现**,用户只看到界面正常、日志一条 WARN。照 Windows 参考版(`MainWindow.xaml.cs:362-369`)是在窗口显示 + bridge 连接完成之后才 await 这两个提示。
- **修法**:`RunStartupPromptsAsync` 先等主窗口可见(`Opened` 或 `Visual.IsVisible` 变真,上限 15s)再依次跑两个提示;窗口一直不可见时记一条**文案不同**的 WARN(「主窗口 15000ms 内没有可见,启动提示已跳过」),以便与「弹窗仍然抛异常」区分。`--smoke` 语义不变(冒烟路径根本不进这里)。
- **真机验收(盒子 `main`,`first_run=true` 的全新 HOME)**:修复前 `tipcl12` → 日志 1 条 WARN、「欢迎使用 TipCL」向导**没出现**;修复后新产物 `tipcl14`(**44,966,962 B**,sha256 `a95423e2…`,上传后双向核对一致)→ **0 WARN**、向导**正常弹出**(第 1/4 步,含「跳过向导」)。另用 `first_run=false` 的 fixture 做对照:两版都会弹同意门(说明那条 400ms 延迟对旧实现是有效的,问题在向导那一步把整串中断了)。
- **缺陷 2(版本行齿轮挂羊头卖狗肉)**:`LibraryPage` 版本行齿轮 tooltip 写「版本设置」,点击却执行 `OpenDetailAsync`(Windows 参考版 `:713-720` 是 `VersionSetupDialog.ShowAsync(inst, id)` + `LoadAsync()`)。这不只是文案问题 —— 真机上正是它把用户送进实例详情页撞上 R9 的崩溃,并在排查初期造成归因错误(R8)。已改为与 Windows 对位;实例详情页仍由「实例详情」chip 与实例卡片右键菜单进入。
- **测试**:新增 `tests/TipCL.Tests/App/StartupPromptVisibilityTests.cs`(4 条:未 Show 的窗口不可见 / 未 Show 时提示一次都不跑、Show 后才跑 / 已可见立刻跑 / 永不显示时在上限内收摊且不跑提示);`InstanceDetailCrashTests` 的入口改用「实例详情」chip(同一个 `OpenDetailAsync`),并新增齿轮断言(弹的是版本设置对话框、`Dlg.AnyOpen`、`Detail(win)==null`)。
- **主 agent 复核**:Release 重编 **0 CS/CA 警告**(2 条 `NU1900` 为镜像噪声);`dotnet test` 独立复跑 **334 通过 / 1 跳过 / 0 失败**(基线 329+1,新增 5);真机上向导确实弹出。
- **遗留(下一棒)**:冒烟截图的「首批执行残留帧」问题(R8 观察,之后 20 次复跑未再现)。

### 2026-11-13 M4-H R12 补验 + R13 冒烟确定性(主 agent + 移植代理)

**R12 缺陷 2(齿轮归位)真机验收**:最终产物 `tipcl14` 上点实例库版本行齿轮(窗口相对坐标 `(1026,350)`)→ 打开的是 **「版本设置 · 26.3」对话框**(版本隔离 / 独占内存 / JVM 参数 / 垃圾回收器 / 直连服务器 / 端口 / 启动前命令 + 取消/保存),不再是实例详情页;`unhandled=0`、`ERROR=0`。截图 `_cmp/r14-gear.jpg`。

**实例详情页的另一个入口也真机验过**:齿轮归位后,实例详情页由「实例详情」chip 进入 —— **窗口相对坐标 `(780,88)` 命中**(`xdotool mousemove --window $WID` + `import -window $WID`),详情页正常打开(「26.3 / 1个版本」+ 分段条 + 空态),`unhandled=0`。截图 `_cmp/r14y-after.jpg`。**方法论教训**:之前几轮用整屏截图反算坐标,误差 20~50px 导致连点 4 轮都没命中(窗口客户区原点在 X11 下与 `getwindowgeometry` 的 X/Y 并不一致);**只用窗口相对坐标**(窗口自身截图量位置 + `--window $WID` 点击)才可靠。

**盒子 8 页 ×2 批冒烟(冷启动,`rm -rf ~/.net/tipcl14`)**:16/16 次 exit=0,同批 8 张 PNG 两两不同,`$TIPCL_HOME/logs` **0 WARN / 0 ERROR**;R8 那条「冷启动首批把上一页帧写进新文件名」**未复现**(与 R8 自己 20 次复跑未复现一致)。

**R13(冒烟捕获确定性,移植代理;主 agent 逐字节复核)**:只改 `src/TipCL.App/Session/SmokeRunner.cs`(+58/−11)——

- 导航 / `SMOKE_SEG` / `SMOKE_SETTINGS_TAB` 之后显式推一次渲染 pass(`AvaloniaHeadlessPlatform.ForceRenderTimerTick(1)` + `Dispatcher.UIThread.RunJobs()`,即 D-39 那条先例);
- `SMOKE_WAIT` **显式给了**就仍等这么久(原语义),**未给**时补 400ms 最小 settle(每页 +~0.4s,8 页 5.3s → 8.7s,远低于 30s 上限);
- 捕获前再推一次渲染 pass,然后**捕获两次、丢弃首帧、只保存第二帧**;首帧捕获失败只记 stderr,不让冒烟崩。

**R13 的关键副产物(改前代码在本机就会抓到未重合成帧)**:把 R13 改动临时 stash 后重跑同一批并逐像素比 —— **plugins / plugins-dark 差 2301 px(0.465%,x318..573 y249..318),改前那帧该区域整片空白,缺了插件页空状态**(`U.Empty("◇","还没有安装插件",…)`);改后与 11/12 盒子参考帧同区域一致。这把「capture 早于渲染完成」从猜测变成了本机因果证据。**但 R8 那次「与上一页完全相同」仍未复现,不能声称已修掉它**:本机是框架依赖产物,没有单文件自解压那一段。

**主 agent 复核 R13**:Release 重编 **0 条 CS/CA 警告**;自己连跑 8 页 ×2 批,**16 张 PNG 的 sha256 与字节数逐页、逐批与 R13 报告完全一致**(launch `27bc9fe4…` / 563098B,library `4e0c364a…` / 45829B,download `30f195d2…` / 23655B,plugins `856adbc1…` / 35012B,settings `318920d4…` / 37893B,ai `ecb78e19…` / 37553B,library-dark `2d075f90…` / 43859B,plugins-dark `6390b2c0…` / 33574B),同批 8/8 互不相同 ⇒ 证据为真且流程确定。

**事故与处置(必读)**:R13 与主 agent 的冒烟脚本里用了 `$home` 变量名,与 PowerShell **只读变量 `$HOME`** 撞名(赋值失败但不报错退出),于是 `TIPCL_HOME` 实际指向 `C:\Users\Administrator`,两次冒烟各在**用户目录根**写下 `config.json` / `ai_chats.json` / `ui-theme.json` / `logs\tipcl-20261113.log`。已按「CreationTime 为当天 + 路径精确匹配」的守卫逐个删除并复核;**真实用户数据目录 `%LOCALAPPDATA%\TipCL` 未被触碰**(`config.json` 仍是 `2026-11-08 17:13:58`、`ai_chats.json` 仍是 `2026-11-08 17:12:29`)。以后冒烟/无头运行一律显式给**隔离的 `TIPCL_HOME`**,且变量名不要叫 `$home`(见 D-43)。

**新发现:测试套件偶发失败(与本次改动无关,立 R14)**:当天在**未改动测试**的情况下连跑,出现两次不同的用例失败,栈都是无头会话初始化竞态:

- `ApiContractsTests.Cleaner_Language_Themes_Accounts_AreWired`:`System.InvalidOperationException: Call from invalid thread`,栈在 `AvaloniaHeadlessPlatform.Initialize` → `Compositor..ctor` → `RenderLoop.Add`;
- `AiPageResidualTests.SlashMenu_ShowsAndHides`(同一栈,`tests/TipCL.Tests/App/TestApp.cs:50`)。

两者都不经过 `SmokeRunner`(测试里只有 `Skip` 的 `SmokeIntegrationTests` 提到它),所以不是 R13 引入的;但它让「全绿」这个验收判据本身不可靠,必须修。

### 2026-11-13 M4-H R13 数字口径(便于以后对齐)

- 8 页冒烟总时长:改前 5330ms → 改后 8642 / 8685ms(每页 +~410ms,来自 400ms 最小 settle)。
- `SMOKE_WAIT=1` 时实测 1640ms ≈ `1050 − 400 + 1000`,即显式等待仍是原语义、没有被叠加。
- 参数兼容性抽查全部 exit=0:`SMOKE_TASKS=1 + SMOKE_PAGE=tasks`、`download + SMOKE_SEG=2`、`settings + SMOKE_SETTINGS_TAB=1`(31957B ≠ 默认 37893B,切页生效)、`library + SMOKE_PROBE=1`(239 行 PROBE)。

### 2026-11-13 M4-H R14 测试套件偶发 `Call from invalid thread`:根因与修复(移植代理;主 agent 复核)

- **现象**:不动任何测试连跑 `dotnet test`,偶发**单条**失败。主 agent 当天亲见 1 次 `AiPageResidualTests.SlashMenu_ShowsAndHides`;代理统计改动前 32 次运行里 **4 次失败、4 个不同用例**,栈同型:`TestApp.Run(TestApp.cs:50)` → `EnsureApplication/SetupUnsafe` → `AvaloniaHeadlessPlatform.Initialize` → `Compositor..ctor` → `RenderLoop.Add` → `VerifyAccess` → `Call from invalid thread`;另一形态是 `PushFrame` → `PlatformNotSupportedException`。
- **先排除并行(重要)**:`tests/TipCL.Tests/AssemblyInfo.cs:6` **早已** `[assembly: CollectionBehavior(DisableTestParallelization = true)]`,探针在 10 次运行 ~1820 次派发里记录到 **0 次并发 `TestApp.Run`** ⇒ 「加集合 / xunit.runner.json / assembly 特性」三种并行改法都是 no-op,**根因不在并行**。(主 agent 起初就是按并行假设开的单,被这一条否掉了。)
- **真根因(Avalonia IL + 最小复现证实)**:`Dispatcher.UIThread` 是**进程级缓存的静态实例**(`s_uiThread ??= CreateUIThreadDispatcher()`),其线程亲和来自构造它的那份 `ManagedDispatcherImpl`(`_loopThread == new 它的线程`);而 `HeadlessUnitTestSession` **每执行一次派发**都会 `EnterScope → Dispatcher.ResetForUnitTests()`(把该静态置空、旧调度器就地改成 Dummy)+ `AppBuilder.SetupUnsafe()`,派发结束再置空 ⇒ **每个派发边界都存在一段 `UIThread == null` 的窗口**。窗口里先读到它的线程就成为「UI 线程」:最常见是会话线程自己(`SetupUnsafe → Application.RegisterServices → SetValue → VerifyAccess` 在 headless 绑实现之前就读),其次是产品码后台线程(`src/TipCL.App/Common/Api.cs`、`Common/TaskStore.cs` 在任意线程 `CheckAccess/Post`)。谁抢到,装进去的调度器就归谁 ⇒ 会话线程随后在 `RenderLoop.Add`/`VerifyAccess` 上报 `Call from invalid thread`;窗口里若解析不到实现则装 `NullDispatcherImpl`(不是 `IControlledDispatcherImpl`)⇒ `PushFrame` → `PlatformNotSupportedException`。**毒源**:`IconLibTests` 原静态构造在**测试线程**上另做了一次无头初始化,把一份「归测试线程所有」的实现留在**根** locator 作用域(`AvaloniaLocator.Current/CurrentMutable` 是进程级静态、非线程本地)。
- **修复(只动 `tests/**`)**:① `tests/TipCL.Tests/App/IconLibTests.cs:13` 删掉那次多余的第二次无头初始化(该类 4 个用例本来就 `TestApp.Run` 落在会话上,`Geometry.Parse` 在会话里已就绪);② `tests/TipCL.Tests/App/TestApp.cs` 新增 `StartSession()` + `PinSessionDispatcher()`:起会话后先做一次**同步预热派发**,在会话线程上把自己那份 `IDispatcherImpl` 发布到**根** locator 作用域当兜底 ⇒ 窗口里无论谁抢先创建 UI 调度器,创建出来的都归**会话线程**,`VerifyAccess/PushFrame/Post` 全部照常。用例语义不变(每派发仍 `EnterScope + Reset + SetupUnsafe`,子作用域优先级更高),预热失败被吞掉、退化为原行为。
- **验收**:代理连跑 **22/22 全绿**;主 agent **独立连跑 8/8 全绿**(每次 `失败: 0,通过: 334,已跳过: 1,总计: 335`、exit 0,平均 wall **24.2s**;改动前平均 30.7s ⇒ **没有变慢**,修复不含任何串行化)。累计 30 次连续全绿,对照改动前 4/32 ≈ 12.5% 的失败率,「纯靠运气」的概率约 **1.8%**。用例总数前后一致 **335 = 334 passed + 1 skipped**(仍是 `SmokeIntegrationTests.SmokeArg_Renders_MainWindow_To_SmokePng` 的 `[Fact(Skip=…)]`),**没有删用例、没有加 skip、没有放宽断言**。Release 构建 0 条 CS/CA 警告。
- **证据**:`_cmp/r14-report.md`(现象表 / IL 摘录 / 探针日志 / 最小复现 / 22 次逐次表)、`_cmp/r14-{pre,probe,fix}-test*.txt`、`_cmp/r14-fix-summary*.txt`、`_cmp/r14-repro-{baseline,fixed}.txt`(基线 13/13 失败、修复 9/9 通过,100μs 时间线显示 ~50μs 窗口与调度器归属被纠正)、`_cmp/r14-probe-log-FAIL2.txt`(`FOREIGN-OWNER owner=#10 (session=#0)`)。
- **留下的权衡/风险**:兜底用反射访问 Avalonia 内部(`Avalonia.AvaloniaLocator.Current` / `_parentScope` / `_registry` / `GetService`);将来升级 Avalonia 若改名会**静默失效**,视作退回「偶发失败」而不是新增失败。`PushFrame` 那一形态只复现出同族机制、未逐帧复现原现场。
