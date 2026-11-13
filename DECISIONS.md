# DECISIONS — TipCL Linux 版

> 技术决定记录。D-1 起编号；各包只追加自己的小节（Edit 在文件末尾追加）。
> 已定结论（GOAL §2）不重新讨论，此处仅登记出处与落地细节；新增偏离需报备。

## D-1 · 框架与运行时（GOAL §2，A1 登记）

- UI 框架：Avalonia UI 11（C#），MVVM 不强制，允许 code-behind 贴近 Windows 版。落地包版本 11.3.2（Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent / Avalonia.Headless；Diagnostics 仅 Debug 引入）。
- TFM：net8.0（App、Core、Tests 统一；如 Core 遇 API 缺口可降 netstandard2.0，由 A4 报备）。
- 版本号冻结 0.1.0-m1（csproj `Version` 已写入）。

## D-2 · Core 复用策略（GOAL §2，A1 登记）

- 从 Windows 版 `src/TipCL/Core/` 与 `src/TipCL/Services/` **复制**可跨平台文件进 `src/TipCL.Core/`，文件头注释注明来源路径。
- Windows 专属 API（注册表、WPF 类型、Win32）不搬；缺口用 `#if WINDOWS_DESKTOP` 条件编译或桩实现，由 A4 在 PROGRESS 记录。
- 复用清单交付物见 GOAL §4.4。

## D-3 · 方案与工程布局（A1 登记）

- `TipCL.Linux.sln`（经典 .sln 格式，非 slnx；SDK 10 的 `dotnet new sln` 默认生成 slnx，已用 `--format sln` 重造）。
- 三工程：`src/TipCL.App`（WinExe，Avalonia 主工程）、`src/TipCL.Core`（类库）、`tests/TipCL.Tests`（xunit）。三者均已预登记入 sln 并配了最小可编译骨架，A4/A5 在各自 csproj 与目录内充实，不与 sln 冲突（sln 编辑权归 A1）。
- csproj 一律默认 glob：新 .cs/.axaml 自动编入，后续代理不碰 csproj（A4/A5 各自新建/维护的除外，见 GOAL §5）。

## D-4 · 冒烟方案（A1 登记，A5 充实）

- `--smoke`：Avalonia.Headless 无头平台启动 → `MainWindow.CaptureRenderedFrame()` → 存 `smoke.png` 退出。骨架在 `src/TipCL.App/Session/SmokeRunner.cs`，输出路径/退出码约定由 A5 定稿。

## D-5 · 字体（GOAL §2，A1 登记）

- 不嵌字体；窗口 FontFamily 系统回退 `"Microsoft YaHei UI, Noto Sans CJK SC, WenQuanYi Micro Hei, sans-serif"`，当前在 App.axaml 临时样式内，A2 主题库就位后可迁移至 Themes/。

### D-6 · M1 集成期修复（主 agent 验收，2026-11-08）
- **ResourceInclude 运行时挂载**：Avalonia 11.3 的 `ResourceInclude(Uri)` 构造参数是 baseUri 而非 Source，运行时重挂主题字典必须写 `new ResourceInclude((Uri?)null){ Source = new Uri(...) }`（MainWindow.SetDark）。
- **跨字典 StaticResource 在运行时重挂不可靠**：A3 的 ShellTokens 转发层（StaticResource 转发 A2 的 B.* 键）在 SetDark 重挂时抛 KeyNotFoundException，DynamicResource 别名又会让消费者拿到扩展对象——最终删除转发层，外壳直接引用 B.* 键（全部 DynamicResource，明暗切换即时生效），ShellTokens.*.axaml 删除。
- **模板 ContentPresenter 必须显式 `Content="{TemplateBinding Content}"`**（WPF 隐式绑定在 Avalonia 不存在）：Shell 4 处 + A2 Controls.axaml 6 处补齐。曾导致全部按钮内容不渲染（探针显示 ContentPresenter cls=[:empty]）。
- 冒烟增强：`SMOKE_DARK=1` 深色帧、`SMOKE_PROBE=1` 可视化树探针（含每元素 classes 与 bounds）。
- 冒烟截图 `smoke.png`/`smoke-dark.png` 与 `_screen.png` 不入库（.gitignore 由本次提交补充）。

### D-7 · M3 收尾裁决（2026-11-09，主 agent，用户指示禁用子代理）
- 版本统一 `0.1.0-m3`：App/Core csproj + CoreInfo 常量 + SmokeTests 断言四处同步（C2 只改了 App，Core 一处会漏造成测试红）。
- 不 trim 发布：`PublishTrimmed` 产生成片 IL2026（Core 的 System.Text.Json 反射序列化），Linux 产物本机无法验证运行，M3 优先交付正确性；瘦身路径 = Core JSON 迁 JsonSerializerContext 后再评估。
- 单文件真单文件：必须加 `IncludeNativeLibrariesForSelfExtract=true`，否则 Skia/HarfBuzz 原生库散落在旁（实测 11 MB）。
- 插件页启停改走 `PluginRuntime.Start/Stop`（宿主单例）：页面不再自建 store/dirs，避免页面与宿主两份状态。

### D-8 · 主题切换必须同步 Fluent 变体（2026-11-09，真机复现）
- 现象：浅色下构建页面 → 切深色后，未显式设前景色的文字与默认 Button 仍是浅色变体（深字压深底），真机 Ubuntu 上清晰可见。
- 修复：`MainWindow.SetDark` 同时设置 `Application.RequestedThemeVariant`；启动时从 Config.CONFIG 读 `ui_dark` 恢复主题。仅换 B.* 色板不足以覆盖 Fluent 内部主题资源。

### D-9 · M3 回归修复约定(2026-11-09,主 agent)
- 事件一律回到 UI 线程:后端工作体跑线程池,`InProcessApi.RaiseEvent` 与 `TaskStore` 公共事件统一 `Dispatcher.UIThread.CheckAccess()/Post`;页面订阅者不再各自做封送(LaunchPage 保留二次 Post 无害,新页面不要复制)。
- 页面首载只放生命周期钩子:外壳 `Navigate` 负责 ShellDetach/ShellAttach,页面不得再用 OnAttachedToVisualTree 等隐式时机自触发,否则外壳换页后行为不可预期。
- `FetchTimeoutMs` 是 UI 兜底而非硬超时:core 侧每个镜像自带 4s/20s 连接/读超时并会换源重试;UI 超时后继续等结果,禁止用 CancellationToken 假装能中断(API 层不透传 ct 到下载器)。
- 文件日志固定 `$TIPCL_HOME/logs/tipcl-YYYYMMDD.log`(UTF-8,按日 append,不自动清理);排障优先看它,其次是 stdout。

### D-10 · 任务体不得触碰控件 + 进度事件不得自环(2026-11-09,真机栈溢出)
- `IApi.StartTaskAsync` 的 work 回调跑在线程池:任何 Avalonia 控件属性(IsChecked/IsVisible/Text...)都必须先在 UI 线程取出成普通值再进回调;同类规矩适用于 `Task.Run` 里读控件(SettingsPage 手机同步卡)。后台回调要更新 UI 一律走 `Dispatcher.UIThread`。
- 页面订阅 BridgeEvent 做「事件转任务进度」时必须忽略 `TaskId` 非空的事件:`TaskProgress.Report` 自己会再抛出带 taskId 的事件,不过滤就是无限递归(实测栈溢出杀进程)。
- 任务生命周期必须落日志(`AppLog`):任务开始/完成/失败(含异常栈)。此前 GUI 安装失败在所有通道上都不可见,只能靠猜。

### D-11 · 外壳以 Windows v2 为准:删侧栏、900×550、标题栏补 AI/账号钮(2026-11-10,主 agent)

**背景(推翻上一轮的差距判断)**:上一轮 handoff 记录「Linux 必须替换成顶栏 启动/游戏/AI助手/更多/下载任务 + 6 分组侧栏」,依据是 `NavModel.cs`。该判断**有误**:`NavModel.TopKeys/GroupedNav` 只服务于页面排序(`VisibleFlatOrder`)与 `NavDump` CLI,不是 UI 结构。

**证据(三重吻合)**:
1. `windos/tipcl/src/TipCL/MainWindow.xaml`(main @ 803dd38):标题栏 = Logo + **5 页签 启动/实例库/下载/插件/设置** + 右侧 `AiBtn(AI 药丸钮) / ThemeBtn / AccBtn(账号) / 分隔线 / 最小化·最大化·关闭`;内容区全幅,**无侧栏**。`Width=900 Height=550 MinWidth=720 MinHeight=480`、CaptionHeight=46。
2. `windos/tipcl/docs/design-spec.md` §11 v1→v2 差异表:`导航 侧栏分组 6-7 项 → 顶部 5 页签`;`窗口 1180×740 → 900×550,Min 720×480`;§10 裁决 1「v2 收敛为顶部 5 页签 + 设置子导航」。
3. 参考截图 `windos/_eval_shots/final/*.png`(明暗 × launch/library/download/plugins/settings/ai 共 10 张)全部无侧栏。
- 另:`git log -S 'SideNav' -- src/TipCL/MainWindow.xaml` 为空(Windows 外壳从未有侧栏);`docs/mockups-hark/` 的「侧栏分组」是 **v1 旧设计线**。

**落地(M4-A 外壳收官)**:
- 删 `Shell/SideNav.axaml(.cs)`;`NavRegistry` 只留 `TopTabs`(5 项,文案与 Windows 同)。
- `MainWindow.axaml`:900×550 / Min 720×480 / 标题栏 46px / 单列全幅内容 + `B.PageWash` 垫底。
- `TitleBar.axaml(.cs)`:46px;右侧簇 = AI 药丸钮(`Button.titleChip`,玻璃底 + ✳ 线条图标 + "AI") → 主题 → 账号钮(24px 首字母圆 `B.AccentStrong` + 名称,默认「未登录」)→ 分隔线 → 46×46 窗口钮;`Tab.Top` 去掉悬停底色块(Windows 只换前景色)。
- `MainWindow.axaml.cs`:接线 `AiClicked→Navigate("ai")`、`AccountClicked→Navigate("account")`(对应 Windows `AiBtn/AccBtn`);`GetPage` 键对齐 Windows NavDefs:`launch/library/download/plugins/settings/ai/account/tasks`。
- `Java`、`关于` **不是顶级页** → 已在 `SettingsPage` 内层 7 项导航里(通用/账号/Java/下载与网络/服务器/游戏时长/反馈与关于),先前已就绪,本轮未改。
- 删除死占位 `PlaceholderDashboard/Java/About`;新增 `PlaceholderAi`;保留 `PlaceholderAccount`、`PlaceholderTasks`(后者由下载/安装页内跳 `Navigate("tasks")` 进入,对齐 Windows)。
- 验收:`dotnet build` 0 警告 0 错误;`dotnet test` 109 通过 / 1 跳过 / 0 失败;无头冒烟 `smoke-shell-light.png` 与参考 `_eval_shots/final/launch-light.png` 结构逐项一致(5 页签 + 选中下划线 + AI 药丸 + 月牙主题钮 + 账号钮 + 窗口钮 + 全幅壁纸)。

### D-12 · 代码侧取色失效:主题资源必须挂到 Application 级(2026-11-10,主 agent)

**现象(整站性)**:`Ui.Dyn(control, prop, "B.X")` / `Res.Bind("B.X")` / `Ui.Theme("Btn.*")` / `Ui.Res(...)` 全部解析为 null。可见表征:

- 启动页「启动游戏」按钮不变绿(透明底)、kicker 前绿点不可见、实例标题右侧下拉箭头不可见、新闻标签没有底色;
- 设置页滑块仍是 Fluent 蓝(不是主题绿);
- 库页版本行图标显示成字面文字 `Star Trash Gear`。

**根因(三个叠加缺陷)**:

1. **没人挂 Application 级资源**:A2 的 `Ui.ApplyTheme(dark)`(= `EnsureMounted()` 挂 `Themes/Controls.axaml` + 换 `Palette.*`)是代码侧取色的唯一真源,但全app只有 `SettingsPage` 切主题时才调它 → 启动时 `Application.Resources` 是空的。而 `Ui.Dyn`/`Res.Bind`/`Ui.Theme` 都锚定 `Application.Current`,于是全部落空(页面里 XAML 写的 `{DynamicResource B.*}` 走的是窗口级合并,所以「大部分地方看着正常」)。
2. **D-6 的坑复发**:`EnsureMounted`/`ApplyTheme` 用的是 Avalonia 11.3 的 `ResourceInclude(Uri)` 构造(= baseUri 而非 Source),一调用就抛 `ResourceInclude.Source must be set`。因为从没人调用过,这个缺陷一直潜伏,直到本轮首次调用才炸出来。
3. **`Controls.axaml` 自身加载不了**:ComboBox 主题里有跨两层模板的选择器 `^:pointerover /template/ ToggleButton#PART_Toggle /template/ Border#Ov` → `ControlTemplate styles cannot contain multiple template selectors`。Avalonia 选择器一次只能跨一层 `/template/`,且 `ControlTemplate` 既没有 `Styles` 属性也不接受多个内容子元素。

**落地**:

- `MainWindow.SetDark` 现在调 `UiClass.ApplyTheme(dark)`(App 级)后仍保留窗口级合并(向后兼容),两处同步换板。
- `Ui.ApplyTheme`/`EnsureMounted` 改为**幂等 + 加锁**(`ThemeGate`):挂载的是全局字典,并行测试/多窗口构造会并发进来;已是目标色板时直接返回,不再重挂(避免全量资源失效与字典撕裂)。
- `Controls.axaml` ComboBox:内层 `Border#Bd` 改为绑定 `ToggleButton` 自身 `Background`/`BorderBrush`,悬停/展开态改为只跨一层的 `^:pointerover|:dropdownopen /template/ ToggleButton#PART_Toggle`。
- 滑块:Fluent 未自绘,改为在调色板里覆盖 Fluent 的 `SliderTrackFill` / `SliderTrackValueFill*` / `SliderThumbBackground*` 键(明暗两套),不动模板 → 不碰拖拽行为。
- `Ui.IconBtn` 同时接受 `IconLib` 图标名(渲染矢量 Path,描边跟随按钮前景色)与字体字形字符(`"↑"`/`"✕"`/`"\uE712"`),修掉「传图标名却渲染成文字」的整类问题。
- 启动页:`launchBtn` 前景显式设 `B.OnAccentStrong`(按钮主题前景会盖掉英雄区白字,不设就是深字压绿底);背景层 `Grid.SetRowSpan(_bgImage/_bgShade, 2)` 铺满整页(原先只铺英雄区那一行,新闻行落在页面底色上却是白字 → 白纸白字)。

**测试基建(顺带根治既有的顺序依赖)**:测试体原先直接在线程池线程上碰 Avalonia,而 `AvaloniaObject` 构造有线程亲和、`TaskStore`/`InProcessApi` 按 `Dispatcher.UIThread.CheckAccess()` 决定同步执行或 Post → xUnit 换线程就跑出「Call from invalid thread」/「事件还没跑断言拿到 null」两类随机失败(被本轮改动放大)。现改为 `TestApp.Run/RunAsync`:经 `HeadlessUnitTestSession.StartNew(typeof(App))` 把测试体派发到会话专属 UI 线程,31 个测试方法改造完毕;`AssemblyInfo.cs` 关闭并行(全局态共享)。**连续 3 次 `dotnet test` 均 113 通过 / 1 跳过 / 0 失败**。

### D-13 · 两处「静默失效」的调用约定缺陷(2026-11-11,主 agent)

**A. `ToDict` 不递归 → 所有设置的保存都是空操作**

页面普遍用匿名对象传参:`Api.CallAsync("save_settings", new { data = new { ui_dark = true } })`。原 `ToDict` 只做一层属性反射,嵌套的匿名对象仍是普通对象,于是 `CallCore` 里 `args.Get("data") is PyDict` 判定失败 → 设置**既没进配置也没落盘**(重启即丢)。另外 `save_settings` 分支只 `CONFIG.Set` 不 `Save()`,Windows 版是 `CONFIG.Update` + `CONFIG.Save`。

修:`ToDict` 新增递归 `Deep()`,把嵌套匿名对象 / `IDictionary<string,*>` / `IEnumerable` 一律落成 `PyDict` / `List<object>`;`save_settings`/`update_settings` 合并后显式 `Config.CONFIG.Save()`。

**B. `CallAsync/TryCallAsync` 不做结构转换 → 按 POCO 取结果的地方永远拿默认值**

`CallCore` 统一返回 `PyDict`,而页面有的地方按 `List<object>` 取(能对上)、有的按 POCO 取(`CleanerPreviewStub`/`SmartRecStub`/`Dictionary<string,string>` 的语言表)→ `result is T` 不成立 → 一律回默认值,TryCall 甚至静默返回 fallback。

修:新增 `Coerce(Type, object)`,`is T` 未命中时把 `PyDict` 序列化成 JSON 再按 `PropertyNameCaseInsensitive` 反序列化到目标类型(`Plain()` 负责 PyDict → 纯字典/数组)。`is T` 命中的老路径不受影响。

**同批修正的同类问题**:`Ui.IconBtn` 与 `Ui.Empty` 收图标名却按字体字形渲染 → 传 `"Star"`/`"User"` 会把名字原样画成文字。新增 `Ui.Icon(nameOrGlyph)`:命中 `IconLib` 键走矢量 Path,否则回落字体字形;`IconBtn`/`Empty` 都改用它。

### D-14 · 反馈链路必须「真发得出去、且不被服务器拒」(2026-11-11,主 agent;用户点名要求)

**背景**:设置页「反馈与关于」的提交卡原先用 `TryCallAsync("feedback_submit", new { text })`。这条路有两个致命问题:① `Feedback.Submit` 在**未同意上传诊断数据时会直接抛错**(同意门),而 UI 从没问过同意 → 点了发送根本没发出去;② `TryCallAsync` 吞异常,UI 把任何失败都显示成「反馈通道后端 M3 接入」→ **403/400 被伪装成「后端没做」,无从排查**。

**对真实服务器的探测结论**(`http://114.66.28.184:53611`,只做无副作用探测,未写入任何数据):

| 请求 | 响应 |
|---|---|
| `GET /health` | 200 `{"ok":true,"service":"pymcl-ingest","role":"ingest"}` |
| `POST /api/v1/feedback` **不带** `X-PyMCL-Client` | **403** `{"ok":false,"error":"缺少客户端标识"}` |
| `POST /api/v1/feedback` 带 `X-PyMCL-Client: PyMCL/1.1.0` + 同 UA、空载荷 | **400** `{"ok":false,"error":"请填写标题或内容"}`(过了标识门,载荷校验被拒) |

即:服务端先卡 `X-PyMCL-Client`(与 Android 端共用;`X-TipCL-Client` 会被 403),再过载荷校验。客户端 `Feedback._headers()` 与 `AuthHttp.Send(..., userAgent: "PyMCL/" + APP_VERSION)` 已经正确,`APP_VERSION=1.1.0` 与桌面端一致。

**落地**:
1. 反馈卡改为:先过同意门(`feedback_has_consent` → 未同意则 `Dlg.Confirm` 说明上送内容 → `feedback_consent`)→ 再发;
2. 发送用 `CallAsync`(会抛)并把**服务器原话**显示在卡片与 Toast 上(403/400 可见),不再假报「后端没做」;
3. 补 `category`(问题/建议/其他 → bug/feature/other,服务端只认 bug/feature/crash/other,桌面端特有分类由 Core 归并为 other)与 `include_sysinfo`(sysinfo 按服务端约定转「key: value」多行文本);
4. 新增 `feedback_submit_crash` 契约(崩溃报告走同一通道,category 固定 crash)。

**验证**:`tests/TipCL.Tests/App/FeedbackTests.cs` 用本机 TCP sink(免 Windows HttpListener URL ACL)顶替 ingest,**复刻服务器那两条规则**,断言:路径 `/api/v1/feedback`、`X-PyMCL-Client == User-Agent == PyMCL/1.1.0`、`Content-Type: application/json`、必需字段齐全、category 归并、sysinfo 多行「key: value」、整链路 200 + id;并锁住「未同意不发送」与「403 原样抛出」。

### D-15 · API 边界数值归一 + 反馈分类键(2026-11-11,主 agent)

- **数值**:`InProcessApi.ToDict/Deep` 把参数里的 `int/short/byte/sbyte/ushort/uint` 一律转 `long`,`float` 转 `double`。Core 按 Python 语义移植,只认 `long` / `double`(如 `Servers.IndexArg` 见到 `int` 直接抛 TypeError);页面用匿名对象传 `int` 是常态,统一在 API 边界归一,不在各 Core 函数里各自兼容。
- **反馈分类**:服务端只认 `bug/feature/crash/other`,桌面端分类键是 `FeedbackDefaults.CATEGORIES`(含 `suggest`、`download`、`ui` 等)。设置页反馈卡把标签映射到桌面键(问题反馈 → `bug`、功能建议 → `suggest`、其他 → `other`);`Feedback.Submit` 上送前再转服务端键:`suggest → feature`,其余桌面特有分类 → `other`;调用方直接传 `feature` 也兼容(先归到 `suggest`)。

### D-16 · 测试基建三条硬约束(2026-11-11,主 agent)

1. **测试体必须真的跑完**:`TestApp.Run/RunAsync` 阻塞等待 `HeadlessUnitTestSession.Dispatch` 返回的 Task。`Dispatch` 只是把工作排进会话队列,不等它就是「测试体还没执行就判通过」——`TestApp` 自 M4-C 引入起即如此,真正执行后立刻暴露 4 个失败(见 PROGRESS M4-G)。
2. **测试会话用真 Skia**:`TestApp` 自带 `BuildAvaloniaApp()`(`UseSkia()` + `UseHeadlessDrawing = false`,与 `SmokeRunner` 一致),`HeadlessUnitTestSession.StartNew(typeof(TestApp))` 按名字反射调用它。默认的无头绘图是桩:`Bitmap.Save` 写出 0 字节、任何 PNG 都解码成 1×1,凡是读写位图的断言都无从成立。
3. **测试数据根隔离**:`TestIsolation` 模块初始化器在任何用例读 `Utils.ROOT` / `Config.CONFIG` 之前,把根目录切到本进程自建的临时目录(`TIPCL_HOME` + `Utils.SetRoot`),进程退出时删除。不隔离时 ROOT 就是与 Windows 版共用的真实 `%LOCALAPPDATA%\TipCL`。
- Windows 开发机上仓库目录带低完整性标签,`testhost.exe` 会以低完整性启动而写不了 `%TEMP%`:用 `dotnet test ... -- RunConfiguration.DotNetHostPath=<dotnet.exe>` 跑(见 PROGRESS M4-G 环境注意)。

### D-17 · 明暗以配置 `ui_dark` 为唯一真源(2026-11-11,主 agent)

- 外壳构造与设置页加载都从配置读 `ui_dark`;改明暗的入口必须同时写回配置,否则下一次加载就会把界面改回去。标题栏月牙钮照 Windows(`SetDark` → `PushDarkAsync` → `save_settings`)异步写回,失败弹 Toast;设置页开关走自己的自动保存。
- 冒烟的明暗只由 `SMOKE_DARK` 决定:构造窗口前把 `ui_dark` 写进**内存**配置(不落盘,不污染 `TIPCL_HOME` 夹具),而不是构造后只调 `SetDark`。

### D-18 · 下载页六分段与 CatalogPage 移植口径(2026-11-12,移植代理)

- **Core 补搬**:`Modpack.cs` / `ModUpdate.cs` 从 Windows 版原样搬(纯 C#,无 WPF/Win32 依赖),文件头注明来源;`CatalogFiles._hit_row` 增补 `icon_url` / `website_url` 两个键——Windows 侧这两项由 `BackendApi._content_row` 在 API 层补齐,Linux 侧在行构造处一次带出,少一层重复映射。
- **收藏键与落盘**:照 Windows 用 `source|slug(id)|name` 拼键(大小写不敏感),存进 `config.json` 的 `catalog_favorites`,与 Windows 版共用同一配置键;`toggle_favorite` 返回合并后的完整列表(页面据此刷星)。
- **安装类一律「后台任务 + 立刻返回 task_id」**:`install_mod / install_shader / install_resourcepack / install_datapack / install_world / install_modpack` 走 `StartDetached`(登记 TaskStore、事件流回灌进度),页面拿到 id 后跳「任务」页——与 Windows `BackendApi.StartTask` 语义一致,也与 Linux 侧既有的 `terracotta_prepare` / `launch_game` 一致。
- **项目介绍页地址按真实类别拼**:Windows `_content_row` 一律按 `kind="mod"` 拼 `modrinth.com/mod/<slug>`,对资源包 / 光影 / 数据包是错地址;Linux 侧按真实 kind 拼(`modpack` / `resourcepack` / `shader` / `datapack` / `world`),CurseForge 侧优先用 `website_url`,否则按 `mc-mods|modpacks|worlds` 段拼。只影响点击行打开的网页,不影响任何布局。
- **批量导出的返回结构**:**这是一个 Windows 侧的静默缺陷**——`ContentExport.ExportMany` 返回 `{dir, exported[], failed[{name,error}]}`,而 Windows 页面的 `ExportResult` POCO 把 `failed` 当 `List<string>` 取,反序列化失败 → 整块判空 → 「全部导出」点了没有任何反馈。Linux 侧按 `PyDict` 直读实际结构并把失败项逐条列出来,不再造一个对不上的 POCO。
- **启停模组后重新列一遍**:禁用会把文件改名为 `*.disabled`(Core 语义),行里显示的文件名随之变化;Windows 不刷新,连续点两次会拿着旧文件名报「模组文件不存在」。Linux 侧启停成功后重载已装列表。属修正,不属视觉偏离。
- **降级项(明确不呈现)**:(1)Windows 的 `SPanel / VirtualList / SmoothScroll / Ui.Skeleton / Dlg.Busy / FileDrop` 在 Linux 侧无对应件——用 `StackPanel + ScrollViewer + 圆角占位块` 等价实现,拖放安装(EnableDrop)不呈现;(2)「游戏版本」筛选在 Windows 是可编辑下拉,Avalonia 侧保持不可编辑(候选表固定,行为等价);(3)取版本文件时的忙碌遮罩(`Dlg.Busy`)不做,进度由任务页呈现。
- **冒烟**:新增 `SMOKE_SEG=1..6`(下载页分段选择),配套新增 `DownloadPage.SelectSegment(int)` 与 `MainWindow.CurrentPage` 两个公开入口——无头冒烟要能停在任意分段才做得了视觉回归。

### D-19 · 版本管理移植口径与「不移植」裁决(2026-11-12,移植代理)

- **NavDefs 对齐**:Windows `MainWindow.NavDefs` 有 10 个键,其中只有 5 个是顶部页签;`instance`(标题「版本管理」)不在页签里,由启动页版本浮层的「管理」内跳。Linux 侧 `GetPage("instance")` 补上同一语义;`VersionManagePage` 落在 `Pages/Version/`(Windows 平铺在 `Pages/` 下,Linux 按功能分子目录,PARITY 已标注改名)。
- **PreferInstance 语义**:Windows 直接 `Win.GetPage("launch") as LaunchPage` 调 `PreferInstance`;Linux 侧页面是懒建的,改为 `LaunchPage.Prefer(inst, ver)` 静态待办 + `MainWindow.PreferLaunch` 负责「先 Navigate、页面填充数据时消费」,避免页面还没建就问它要状态。
- **`create_desktop_shortcut` 明确不移植**:Windows 用 WScript.Shell 建 .lnk,指向启动器并带上实例/版本参数;Linux 侧没有任何 CLI 启动入口(`Program.cs` 只认 `--smoke`),写出来的 `.desktop` 只会是点了没反应的死按钮。菜单里**不呈现**这一项,PARITY 记为「不移植」并写明理由;等 M3 真机阶段补 `--launch <实例> <版本>` 之后再评估。
- **`export_launch_script` 换脚本方言**:同一调用链(`build_launch_command` 抽出的 `BuildLaunchCmd`),Windows 由 `VersionOps.ExportLaunchBat` 产 `.bat`,Linux 侧改写 `#!/bin/sh` + `cd` + `exec` 并 `chmod +x`。属平台适配,不是行为偏离——导出脚本是排障工具,不参与启动。
- **按 Core 的真实返回结构取值,不造假列**:Windows 的 `SaveRow` POCO 期望 `Size/LastPlayed/Mode`、`BackupRow` 期望 `Size/Date`,而 Core 实际返回 `{name,path,icon,bytes,mtime}` —— Windows 侧那几列一直是空的。Linux 侧直接读 PyDict 的实际键(字节数格式化 + mtime 本地时间),不假装有「上次游玩 / 模式」。同类问题还有 `ExportResult`(见 D-18)。
- **隔离默认值**:`Config` 的默认配置里 `default_isolation = "all"`(新版本默认隔离),测试断言按这个真实默认写,不硬编码 "none"。
- **降级项(明确不呈现)**:Windows 的 `SPanel / SmoothScroll / Dlg.Busy / wpf-ui.json(UiPrefs) / PluginActionBar` 在 Linux 侧无对应件——滚动用 `ScrollViewer`+`MaxHeight`,忙碌遮罩不做(进度由任务页呈现),折叠态与版本收藏先进程内静态表,插件动作栏等 P6 插件宿主落地后再接;拖放导入整合包(FileDrop)不呈现。

> ⚠️ **2026-11-13 更正(见 D-45)**:这条里的「折叠态与版本收藏先进程内静态表」已不再成立 —— UiPrefs 已按 Windows 语义补齐并落 `config.json` 的 `ui_prefs` 键(`lib:`/`vm:` 折叠态、`vm_favorites` 版本收藏、`catalog_instance` 下载页当前实例)。其余(SPanel / SmoothScroll / Dlg.Busy / PluginActionBar 不移植、拖放导入不呈现)仍按原判定。

### D-20 · Java 子页按 Windows JavaPage 对齐 + `Coerce` 的 POCO 陷阱(2026-11-12,移植代理)

- **宿主方式**:Windows 的 `JavaPage` 是独立 PageBase,由设置页内层导航第 3 项挂进内容区;Linux 侧沿用既有做法,把它实现为 `SettingsSubView` 的子类 `JavaView`(标题/副标题由基类给出),页面结构与 Windows 一致。
- **子视图标题只能有一个**:`SettingsSubView` 基类构造时就插了「标题 + 副标题」,子类若再 `UiClass.Section(同标题)` 会出现两个重复标题(本轮冒烟里可见)。修法:基类把标题行 `Grid`(两列 `*,Auto`)暴露成 `Header`,子类只往右列补自己的操作区。
- **`CallAsync<List<PyDict>>` 是陷阱,不要用**:`Coerce` 走的是「PyDict → 纯字典 → JSON → 目标类型」,`PyDict` 本身不可 JSON 反序列化,于是**整块回默认值/抛 InvalidCastException**。取对象列表一律 `List<object?>` + `OfType<PyDict>()`(或 `Py.D` 逐项转);`TryCallAsync<List<PyDict>>` 更隐蔽——它静默回 null,页面表现为「什么都找不到」(本轮 `java_combo_options` 会让「选择 Java」直接说「没找到 Java」)。已在本轮全部改正并加测试。
- **`export_launch_script` 之外的平台差异**:Java 运行时下载(`download_java` / `install_java`)是纯下载链路,Linux 与 Windows 共用 Core 实现,无平台分支;「打开所在目录」在 Linux 交给 `Crash.OpenPath`(内部 xdg-open)。
- **冒烟**:新增 `SMOKE_SETTINGS_TAB=0..6`(设置页内层导航项选择),配套 `SettingsPage.SelectTab(int)`;与下载页的 `SMOKE_SEG` 一起构成「页面 + 内部视图」两级冒烟能力。

### D-21 · P4 反馈/向导/崩溃界面的接线与降级(2026-11-12,移植代理)

- **同意门以 config 文件为判据**:Linux 侧照 Windows 语义实现「`config.json` 里没有 `feedback_consent` 键才问一次」(文件优先于内存里的后端值),而不是简单看配置有没有值——Core 默认里该键就是 `null`,只看值会每次启动都问。用例表搬成 `FeedbackConsentTests`。
- **崩溃事件接线**:Core 的启动链在崩溃时抛 `GameCrashError`(报告字典随异常回传),Linux 侧没有 Windows 的 `crash` 事件总线,遂在 `InProcessApi.StartDetached` 捕获该异常时**缓存报告(按 task_id)+ 补发一个 `crash` 事件**,`MainWindow` 订阅后弹 `CrashUi`。这样崩溃界面与 Windows 一样「自动弹」,而不是只在任务卡上留一行错误。
- **`apply_crash_action` 的平台化**:Windows 的 `repair_version` / `need_java` 两个分支会直接起后台任务;Linux 侧改为返回 `ok=true` + 一句指向「版本管理 → 修复」/「设置 → Java」的指引(不在崩溃界面里偷偷起长任务),`disable_mods` / `bump_memory` / `open_mods_folder` / `open_crash_file` / `reset_jvm_args` / `open_gpu_hint` 行为与 Windows 一致。
- **暂不呈现**:崩溃界面的「交给 AI 修复」(依赖 AiFixWindow,属 P5 AI 子系统)、首启向导里的「拖放安装」亮点文案改写为 Linux 已有的能力(版本管理/轮播壁纸/AI 药丸钮),不写做不到的功能。
- **`Dlg.OpenPanel`**:Windows 的 `Dlg.Panel` 是非模态浮层(带 `layer.Close()`),Linux 侧新增 `OpenPanel` 返回句柄(`Close()` + `Closed` Task),向导据此在「开始使用 / 跳过 / 直接关窗 = 跳过」三种结束方式下都能正确收尾。
- **启动提示**:首启向导与同意门在 `App.OnFrameworkInitializationCompleted` 里异步触发(不到阻塞首屏),`--smoke` 下跳过——无头截图不该被模态窗口挡住。

### D-22 · AI 子系统移植口径(M4-H P5,2026-11-12,移植代理)

- **Markdown 渲染换宿主**:Windows 的 `MdView` 是 WPF `RichTextBox` + `FlowDocument`(段落/列表/表格都是文档对象);Avalonia 没有对应物 → 改为「解析成块 → 逐个渲染成控件」:段落用 `TextBlock.Inlines`(Avalonia 11 支持 `Run/Bold/Italic/Span/LineBreak/InlineUIContainer`),列表按 `ItemDepth` 缩进、引用用左边框容器、表格用 `Grid`。**只换渲染宿主,不改 Markdown 语义**——`MdParser` 原样搬运,行为由 10 个 xunit 用例锁定。
- **行内元素的两处差距**:①`Span` 在 Avalonia 里不是 `Control`,没有 `Cursor`/`ToolTip`/`Click` → 链接目前只有主题色 + 下划线,URL 不弹提示、不可点(AI 页把整条回复的纯文本复制留给气泡的复制钮;**2026-11-13 更正:R17 已用 `InlineUIContainer` 把链接做成可点,见 D-49**);②`Run` 没有 Background → 行内码没有底色,用等宽 + `B.AccentText` 近似。两条都记在 PROGRESS 遗留里,不假装做过。
- **`InternalsVisibleTo`**:给 `TipCL.App` 加 `<InternalsVisibleTo Include="TipCL.Tests" />`(Core 侧早已有同样一条)。AI 子系统整族都是 `internal`(与 Windows 一致),不开这条就没法给解析链/气泡写测试。只放宽可见性:不加依赖、不改行为、不改打包产物。
- **思考段**:Core 的流式把推理内容包成 `<think>…</think>`(`AssembleStreamWrapsReasoningInThinkTags` 已锁),页面按这两个标签切分增量:标签内进「思考」折叠块,标签外进正文 Markdown。
- **端点键名以 Core 为准**:`Client.ResolveEndpoint` 返回 `mode`(builtin|custom)/`model`/`url`/`headers`,没有 `label`/`configured`;页面按 `mode` 显示「内置网关 / 自定义端点」,并用「能否解析出端点」判断是否已配置(解析抛 `AIClientError` → 显示引导而不是点了没反应)。这类「按想象中的键读后端」是本轮第二次踩(前一次是 `List<PyDict>`),统一按 Core 真实输出取键。
- **本轮不做的**:工具调用 / 权限确认 / 计划卡 / 步骤组 / 自检 / 附件与图片输入 —— 见 PROGRESS M4-H P5 的遗留清单;宁可留着占位说明,不做假按钮。

### D-23 · Linux 移植与 Windows 参考版的四处有意偏离(P3.5,2026-11-12,移植代理)

- **Shortcut 只保留 Linux/macOS 分支**:Windows 参考版 `_create_windows` 用 PowerShell + WScript.Shell 写 `.lnk`(而且生成的参数无人消费,见 PARITY §6「参考实现缺陷」)。Linux 版把这条「本机平台不适用」的分支换成抛 `ShortcutError("当前平台不支持创建桌面快捷方式")`,不搬 PowerShell 脚本。
- **`TIPCL_SHORTCUT_POSIX=1`**:Windows 开发机上跑本工程的无头测试时,.desktop 分支永远走不到(而 .desktop 才是交付物),该环境变量把 Shortcut 的「Windows 路径判定」关掉,让测试能断言 .desktop 的键值与可执行位。只影响 `Shortcut` 内部的路径/后缀判定,不影响产品行为(Linux 上本来就走这条)。
- **`.desktop` 的 Exec/Path/Icon 一律做 Unix 路径规整**:参考版自带的 `_create_linux` 直接把 `ProcessPath` 与工作目录写进去,本机是 Windows 路径时(交叉验证/测试)写出的就是反斜杠,Linux 上不可执行。Linux 版把这三处 `\` → `/`(参数不动,`-i foo` 里的分隔符不是路径)。
- **图标改为程序化生成的 PNG**:参考版给的是 `icon.ico` / exe 自身(Windows 认 exe 内嵌图标),Linux 桌面环境不认。Linux 侧 `ShortcutIcon` 用 `System.IO.Compression.ZLibStream` 直接生成 128×128 PNG 落在 `$TIPCL_HOME/tipcl.png`,不引入图片资源也不加依赖;生成失败则省略 `Icon=` 键,不写指向不存在文件的图标。
### D-24 · AI 工具链的移植口径(P5,2026-11-12,移植代理)

- **Agent 在进程内重实现,不搬 Python 侧那套依赖**:参考版 `Core/Launcher/Ai`(~8000 行)的 Agent 依赖 Mcp / Checkpoint / Compact / Store / Trace / Scheduler / Rewind / Artifacts 等 20 余个模块,而这些能力在 Linux 侧(无 Python 桥、无 MCP 客户端)没有等价物。Linux 版按**同一批工具名 + 同一批事件名**(`delta/think/tool/tool_done/status/error/done`)实现最小可用的会话循环:流式 → 有 `tool_calls` 就逐个执行 → 把 `assistant(tool_calls)` 与 `tool(结果)` 追加进对话 → 再发起下一轮。轮数上限 12,超了就让模型不带工具收尾。
- **工具集只写 Linux 真正有的**:`tune_jvm / backup_save / dispatch_subagent / read_artifact / MCP 工具` 不写进 schema —— 与 P7「逐项判定」同一口径:宁可工具少,不做点了报未知工具的死入口。
- **权限键与 Windows 共用**:`config.json` 的 `ai_permission_mode`(default/acceptEdits/plan/yolo/custom)与 `ai_permission_rules`,值也一致,便于两边共用/迁移配置。裁决顺序:自定义规则 → 只读直接允许 → 档位(plan 拒写 / yolo 全允许 / acceptEdits 只在删除类问);删除类工具不给「本对话都允许」。规则匹配只按工具名(参考版还有 `content` 参数限定,Linux 侧记在规则里但暂不参与判定,已在面板文案里避免暗示支持)。
- **确认 / 选择题改成「停靠区」而不是浮层**:Windows 用 `Dlg.Layer` 在窗口上叠浮层;Avalonia 侧没有等价的模态浮层,改成**贴在输入区上方的非模态停靠区**(输入区禁用),用户在卡上决定后工具才继续执行 —— 交互结果与参考版一致,视觉位置不同。`AiFixView` 同理由参考版的独立 Window 改为 `Dlg.OpenPanel` 非模态面板。
- **图片输入的可用路径**:Avalonia 11.3 的 `IClipboard` 只有文本/文件两组格式(没有位图 API),所以只支持「剪贴板里是图片文件」与「拖入/选择文件」;复制一张截图再粘贴这种场景**暂不可用**,不写提示假装支持。
- **`Conflict.cs` 原样搬运 + 调用点改名**:参考版里它调 `Agent.DGet`(Agent 的静态工具函数),Linux 侧 Agent 是会话语义,故把该纯函数落到 `Tools.DGet`,搬来的文件只改 3 处调用点,其余不动。
- **自检换形态**:参考版 `AiSelfTest` 是 WPF 界面自检(要开窗、点按钮、截图);Linux 侧改成不依赖界面也不联网的功能自检,由 xunit 与 `AiSelfTest.VerifyAsync` 覆盖,`--ai-selftest` 的 CLI 入口留待后续。
- **不做的**:会话记忆面板、上下文占用环、`/斜杠命令`补全、附件缩略图预览、子代理(dispatch_subagent)与 MCP 插件工具 —— 见 PROGRESS P5 遗留,不造假按钮。
### D-25 · 插件宿主全链的移植边界:文件级真做,运行时不假装(P6,2026-11-12,移植代理)

- **两条运行路径都要宿主**:参考版的插件体系里,Node 插件是「内核拉起 Node 子进程 + stdio 上的 JSON-RPC + 本地服务 + WPF WebView2 承载网页界面」,.NET 插件是「把插件程序集 `Assembly.LoadFrom` 进启动器进程,直接调它的 WPF 界面工厂」。两者在 Linux 侧都没有宿主,且都不允许用第三方包补(不引 CEF/WebKitGTK/Node 宿主,与 P7 判 `WebViewHost` 同一条红线)。
- **判定口径(逐项表见 `docs/PARITY.md` §7)**:凡是**文件级**能力(市场索引与镜像、安装/更新/卸载、清单与 engines/依赖校验、启停落盘、日志、权限展示、开发链接、数据目录)一律**真做**;凡是需要在进程里跑插件代码或渲染插件网页的能力一律**不移植并写清理由**,不做「看起来像」的空壳。
- **`DotNetPluginContext` 判不移植的依据(更正一条交接假设)**:SDK 契约 `TipCL.Plugins.*` 定义在 **net48 的 `TipCL` 程序集**里(`TipCL.csproj`: `TargetFramework=net48` / `AssemblyName=TipCL`),界面工厂签名是 `System.Windows.FrameworkElement`(`Sdk/PluginSdk.cs`:4/481)。Linux 上既引用不到这套类型,也不可能加载按它编译的插件二进制(程序集标识 + WPF 依赖都不满足);要「真做」只能另立一套与参考契约**不兼容**的新 SDK,而那样没有任何现成插件可用 —— 那属于新造生态,不是移植。
- **安装器去掉两步**:`PluginInstaller` 对齐 `PluginMarket.InstallFromMarket/InstallPackage/SwapAndLoad`,但不做 `npm install`(无 Node/npm)与宿主加载/重启。装完之后插件状态是「已安装、已启用、不会被执行」,页面用固定说明 + 启用时的 Toast 如实告知。
- **修掉参考版遗留的一处「假成功」**:`PluginHost.Start` 原先对 .NET 插件只做一次 `Assembly.LoadFrom` 校验就把状态标成 `running`,而插件的 `Apply(ctx)` 从未被调用。现在 Node/.NET 都返回 `ok=false` + 具体原因;`SetEnabled` 只改落盘状态并回传 `runnable=false` + `reason`(文件级管理是真的,运行不是)。`NoRuntimeReason()` 是这条口径的唯一出处(页面与测试都读它)。
- **壳侧等价物**:`PluginUi`(进程内快照 + 变更通知)替代参考版从 RPC 拉的 `plugins_ui/plugins_list`;`plugin:<plugin>:<id>` 页面 + `group:"top"` 动态顶部页签(P8)照搬参考版结构;`PluginHostPage` 在插件被禁用/卸载后标成「不可用」(与参考版 `MarkGone` 同语义:当前页保留提示、非当前页从缓存删掉)。

### D-26 · CLI 的实例存在性前置校验(P0,2026-11-12,移植代理)

- **问题**:桌面快捷方式写的是 `-i <实例> launch <版本>`,而 `-i <不存在的实例>` 既不报错也不拒绝 —— `InstancesApi.EnsureInstance` 的语义是「目录不存在就 `Create()`」,单根模式下 `Instances.ResolveName` 还会把未知名字吞成根实例,于是游戏会在**另一个实例的游戏目录**里启动(存档/模组全不对)。主 agent 复核里看到的 exit=3 其实来自「没有离线账号」。
- **修法**:新增 `InstancesApi.FindInstance`(只查不改、不吞名字、绝不创建),**只在 CLI 入口**(`Session/CommandLine.cs`)做前置校验:不存在时打印「没有这个实例:<名字>」并以 `CliExit.Usage`(4)退出。`--print-command` 同样先校验(打印出来的命令必须真能跑)。UI 路径上的 `EnsureInstance`「不存在就创建」语义**保持不动**,安装/导入流程依赖它。
- **测试口径**:直接断言「未知实例 → exit=4 且输出含该中文原因,且没有凭空创建实例目录」,不再用「没有离线账号」这类别的失败间接换一个非零码。`-i` 缺值那条路也显式断言 exit=4。

### D-27 · 统一拖放入口(P7,2026-11-12,移植代理)

- **做成统一入口**:`Common/FileDrop.cs` + `Common/FileKinds.cs`,形状对齐参考版 `Services/FileDrop.cs` / `FileKinds.cs`(kind 常量、中文标签与动作说明、`Verdict(路径/名字/候选/是否确定/说明)`、`DropContext{Instance,Version,PreferKind}`)。接入点:主窗口兜底、目录页(带实例上下文)、插件页(偏好 `plugin`)、实例库与版本管理页;`Attach` 幂等(页面可在 `OnActivate` 里挂)。
- **落到真实链路**:插件包 → `PluginRuntime.InstallLocal`;整合包 → 确认后 `install_modpack`(装成新版本);模组/资源包/光影/数据包/存档 → 同名 `install_*` 任务(装完跳「下载任务」);皮肤 → `set_account_skin`(只对离线账号)。
- **不列入壁纸**:参考版把图片/视频识别成 `wallpaper` 并写 `ui_background`;Linux 启动页用的是「实例截图 / 内置 hero」背景,这个配置键没有消费方(§5 已判 Wallpaper 的 mp4/轮播不做)。写一个没人读的入口等于假按钮,故 `wallpaper` 不进 `AllKinds`,拖图片时按「可能是皮肤」处理并给候选让用户选。
- **不做嵌套包递归探测**:参考版会逐层解包找清单(`MaxNest=3`/`ScanLimit=4000`);Linux 侧只读包内成员名与三个关键清单标记(mrpack / CurseForge / tipcl-plugin.json),认不准就问一次,不静默猜。
### D-28 · AI 服务层的移植口径(M4-H R1,2026-11-12,移植代理)

- **服务层按参考版的形状真做**:`Checkpoint`(内容寻址快照 + journal + 回滚)、`Rewind`(回退上一轮)、`Store`(`ai_chats.json` + `cache/ai_sessions/*.jsonl`)、`Compact` + 五个配置/结果类型、`Tokens/TokenState/Usage`、`Artifacts`、`AiFs`、`Trace`、`StopReason` 全部照参考版语义落地,常量(512MB/1GB/20000 文件、200 条消息、40 个对话、14 天/30 天保留、8000 字落盘阈值)与落盘结构逐项对齐 —— 这样两边的数据目录可以互相搬,排查问题的口径也一致。
- **MCP 客户端自己实现**:`McpClient` 用 `Process` + stdin/stdout 上的 JSON-RPC 2.0(一行一条、Windows 上 \r\n)、握手 `initialize` → `notifications/initialized`、工具面 `tools/list` / `tools/call`,连接 10s / 调用 60s,超时就杀子进程并抛 `McpError`。**不引第三方 MCP 包**(依赖白名单外);配置键沿用 `ai_mcp_servers`(与参考版同一处、同一形状,参考版也没有 UI,是手改 config 的键)。
- **`McpServer`(把启动器工具开放给外部 agent)不移植**:它把自己的工具面接到 `BackendApi`(Linux 无 RPC 桥,D-2 已判不移植);更要紧的是 Linux 的写工具在执行前要过界面的确认回调,外部 MCP 客户端没有用户点确认,只会得到一串 `permission_denied` —— 与其开一个只能读不能改的半残接口,不如如实不做。
- **子代理只读**:`dispatch_subagent` 派生一个独立会话跑调研,子执行器固定 `plan` 档(只能查、不能改玩家目录)、最多 4 轮、**不许再派子代理**(sub-runner 的 `Subagent` 回调留空),结论以 `{ok,answer,stop_reason}` 回给主对话(照参考版 `RunSubagent` 的返回形状)。
- **写操作前建检查点**:`AiAgent` 在跑写工具前用 `Tools.AffectedPaths(name,args,instance)` 算出可能被改的路径(改配置→config 下那个文件;启停模组→`x.jar` 与 `x.jar.disabled` 两个名字;装模组/光影/资源包/数据包/地图→对应目录;删实例/装整合包→实例根;装游戏→`versions`;下 Java→`java_dir`),再 `Checkpoint.Snapshot`;快照失败不阻断操作,但会在步骤行如实写「这一步的改动无法回退:…」。
- **等价实现不再另建同名类**:`Prompt` → `AiData.SYSTEM_PROMPT`(P5 已逐字搬);`Diagnose` → `Crash.AnalyzeInstance` + `ToolRunner.Diagnose`;`Permission` → `ToolRunner.Decide` + 五档 + `ai_permission_rules`;`ModConfig` → `ToolRunner` 的 `list/read/write_mod_config`;`AiRule` 不单独建类型(Linux 的规则就存在 config.json 的数组里,用同一批键,面板直接读写 PyDict)。`Hooks/AgentPresets/ChatEventLog` 不移植(无插件宿主;会话事件流已由 `Store.LogEvent` 覆盖),`Preview`(确认卡 diff 预览)与 `ExtraTools` 的通用 fs 工具面记在 PROGRESS 遗留(不放开任意路径读写)。

### D-29 · AI 界面残留:斜杠命令 / 记忆面板 / 占用环 / 对话持久化(M4-H R2,2026-11-12,移植代理)

- **斜杠命令**:`AiChatOps` 照参考版原样搬(名字、别名、提示词、匹配规则),界面侧不做浮层,改成输入区上方的**候选条**(与确认卡/选择题卡同一套「停靠区」思路,见 D-24):上下键移动、Tab 或点击选中、Esc 收起、Enter 直接执行;本地命令只有三种(`new_chat / clear / rewind`),其余把提示词灌进输入框发出去。
- **对话持久化**:AI 页第一次用到对话时才读 `Store`(页面打开本身不落盘),每轮结束 `UpsertMessages` 写回,回到页面时读回并重画气泡 —— 这样 `/撤回`、`/新对话`、`/清空` 才有真实语义。标题仍按参考版规则取第一句用户消息前 24 字。
- **`/撤回` 接 `Rewind`**:先落盘当前对话,再 `Rewind.RewindLastRound(chat_id)`(对话截断 + 检查点还原文件 + 标 `TurnRewound` + 清计划),然后按存档重画;Toast 如实报「已还原 N 个改过的文件 / 这一轮没有磁盘改动」,退不动时明确说原因。
- **上下文占用环**:头部一个 18px 的环 + `百分比 · token 数`,数据是 `Usage.Get(chat_id)`(provider 给过真值就按真值,否则标「(估算)」),窗口取 config 的 `ai_context_window`(经 `Compact.AutoConfigFromSettings` 夹到 8192..2000000),比例按「每轮平均输入 / 窗口」算(照参考版 `AiUiText.ContextRatio`)。
- **记忆面板**:`AiMemoryPanel` 用 Core `FileMemory` 的 `Entries/Remember/Update/RemoveAt/Clear`(与设置页「AI 助手 → 记忆」卡同一份 `ai_memory.md`),编辑走 `Dlg.Prompt`、删除/清空前确认,写完重新渲染;编辑/删除前核对下标(文件可能刚被别的窗口改过)。

### D-30 · 手机同步面板与两处小尾巴(M4-H R3/R4,2026-11-12,移植代理)

- **`PhoneSyncPanel` 做成真面板**:数据面全部复用 Core 已有的 `PhoneLink`/`SaveSync`(没有第二份实现),面板只做界面与流程:电脑位置(实例/版本)× 手机存档根选择、逐行裁决标签(一致 / 电脑较新 / 手机较新 / 两边都改过 / 只在电脑 / 只在手机)、「传到手机/传到电脑」(覆盖前确认,被覆盖的一边 Core 会先备份)、「一键同步」(冲突项用 `Dlg.Choose` 逐个问:跳过/保留手机的/保留电脑的)、日志框。承载面与 AI 权限面板一致:`Dlg.OpenPanel` 非模态面板。
- **没连接手机时不打开空面板**:设置页的「同步面板」按钮先查 `PhoneLink.Current`,没连接只给提示 —— 空面板会让人以为能用,而对比/传输全都要先有连接(照参考版 `open.IsEnabled = PhoneLink.Current != null` 的同一意图,Linux 侧改成「点了给原因」而不是把按钮置灰,灰按钮用户不知道去哪)。
- **插件「有更新」徽标**:已安装卡片读市场索引(`updatable` + 版本号)显示「有更新 → vX」,菜单项同步变成「更新到 vX」;索引只在进已安装分段时后台读一次(失败静默,不拿网络问题打扰用户),读不到就不显示徽标(与参考版「徽标只在市场页」相比是更进一步的实做)。
- **市场详情截图真的显示**:用 `Common/Thumbs`(远程图 + 进程内缓存)横排 200×118 缩略图,点击在浏览器打开;下载失败或防盗链时显示「图片加载失败 / 点击用浏览器打开」的占位,不显示破图、也不静默留空。

### D-31 · 确认卡的「改前改后」预览:算在 Core、摊平在 Core、页面只转发(M4-H R5,2026-11-12,移植代理)

- **为什么移植 `Preview`**:R1~R4 把它记成「不移植」的遗留,但确认卡**界面早就支持 diff**(`ConfirmCard.PreviewBox` + `AiUiText.LooksLikeDiff`),两边都在、中间没人传 —— 结果就是「写文件前先看清改了什么」永远是个空框。这是安全能力的断链而不是可选装饰,故本轮补齐,并把 `Preview` 从「不移植」清单里摘掉(`docs/PARITY.md` §2/§3/§4 已更新)。
- **没有 BackendApi 怎么等价**:参考版由 `BackendApi.ConfirmFn` 算预览再发事件;Linux 侧算在 `ToolRunner.RunAsync`(权限裁决之后、真正执行之前),回调签名扩成 `Confirm(call, label, preview)` 把结构体带下去。选这里是因为:只有一份实现(页面拿不到「另一种算法」)、与参考版「后端算好再给界面」同一条口径、测试能直接驱动回调。
- **结构键名照抄**:`{kind, head, lines, too_large}` 与参考版逐字一致,两边判据可以复用;`head` 到界面时统一以 `@@ ` 开头(照参考版 `ShowConfirm`),这一步做成 `Preview.Flatten` 由两个页面共用 —— **界面要的行不再由调用点自己拼**,杜绝「一个页面记得拼、另一个忘了」再犯。
- **预览不能挡路**:算预览抛异常就当没有(参考版也用 try 包住);预览只读不写(`write_mod_config` 的旧内容用 `AiFs.ReadText(replace: true)` 读,读不出来按空文件处理);大 diff 落盘走 `Artifacts.Save`(注意 Linux 的 `Artifacts.Store` 返回的是「回给模型的那段话」,要路径得用 `Save`,参考版等同 `Save` 的路径返回)。
- **页面第三参为什么留空**:参考版那句 `reason` 来自 `Permission` 的裁决理由(「写入本机文件需要确认」之类),而 Linux 的 `ToolRunner.Decide` 只给 allow/ask/deny、没有理由文本。宁可留空并在调用点写明原因,也不为卡片编一句看似权威的假话;卡片仍有风险标签 + 工具标签 + 预览三样真实信息。
- **测试要盯「链」不是盯「件」**:只测 `Preview` 正是这次断链能藏住的原因(界面逻辑与 `Preview` 都「有」,中间没人传,两边测试全绿)。故回归用例从执行器一路断到卡片:先断言回调真的拿到非空预览,再**驱动页面自己的 `Confirm` 回调**、在页面控件树里取卡、断言其中一行与它的**底色**(连渲染一起验)。为让测试能驱动,两个页面加 `internal ToolRunner Runner`、`ConfirmCard` 加 `internal IReadOnlyList<string> PreviewLines` —— 只放可见性,不加依赖、不改行为、不改打包产物。
- **顺手清掉的同类项**:`TaskCard(tool)` 在两个页面原先都写死 `""`(任务卡靠它决定后续动作),现由 `TaskStarted` 回调补带工具名(参考版的任务事件本来就带 tool),并加断言锁定。

### D-32 · 真模型端到端用「一次性探针 + 隔离 HOME」验,验完即删(R6,2026-11-12,移植代理)

- **怎么验**:不新增长期测试、不改产品代码 —— 写一个**临时 xunit 文件**,在进程内自建的空 HOME(`TIPCL_HOME=%TEMP%\tipcl-r6-e2e\home`,配 `Utils.SetRoot` + `Config.Reset()`)里跑真实往返。这样既满足「走应用正常用法」(`ai_mode=builtin` 走 `Defaults` 的端点与令牌),又不碰用户的真实数据目录(实测两份真实文件的 mtime 未变)。用完**删除**,避免给 `dotnet test` 留下需要联网/额度的用例。
- **不打印任何密钥**:内置令牌只在进程内被 `Client` 使用;报告里连网关主机名都做了掩码(`http://<已掩码>/v1/chat/completions`),只留 scheme + 路径 + 状态码 + token 数。没有反混淆、没有打印、没有写进任何提交或日志。
- **什么才算「真链路 OK」**:**(1)** 流式增量 > 0 且最终文本非空;**(2)** `usage` 事件带 **provider 真值**(不是本地估算);**(3)** `finish_reason` 正常;**(4)****工具链闭环** —— 让只读工具(`list_instances`)返回一个**只存在于工具结果里的随机口令**,模型必须在下一轮把这个口令说出来才算数(只看 `tool`/`tool_done` 事件会被「事件发了但结果没回填」骗过);**(5)****压缩**要看到真模型写的摘要真的替换了旧历史(只看到 `compact` 事件不算 —— 该事件在摘要请求之前就发了);**(6)****回滚**要看见文件被还原成**本轮开始前的内容**(「新建文件回滚成不存在」虽然也是正确行为,但那是「没留脏改动」,证明不了还原能力,所以探针先把文件写成 `A=1` 再让模型改成 `A=2`)。
- **结论**:六项**全部成立**(证据见 `PROGRESS.md` 的 R6 节)。唯一失败源是网关对上游渠道的限速(原文 `HTTP 429 渠道 #42 达到限速上限(RPM=12 TPM=200000)…`),属外部依赖:如实记下状态码与原文后按约定停手 —— **没有换 key、没有换端点硬试**;把阶段间隔放长(12s)+ 开头冷 15s + 失败只重试 1 次即可跑通。
- **顺手证到的一件事**:`Checkpoint` 的快照清单由 `Tools.AffectedPaths` 给出、写操作由 `ToolRunner` 落盘,两处各算一次路径 —— 真链路上实测**两者完全一致**(`…\.minecraft\probe\config\probe.toml`),这个「两处会不会算岔」的隐患本轮用真链路排掉了。

### D-33 · 限流/5xx 重试必须与参考版同口径,且「已产出内容绝不重发」(M4-H R7,2026-11-12,移植代理)

- **背景**:内置公益网关会给上游渠道限速(实测原文 `RPM=12 TPM=200000`),首次调用就可能 429。参考版有完整的原地重试(`AIClientError.Retryable()` + `Agent.cs:829` 判据 + `:1697` 退避),Linux 侧在 P5/R1 搬运时**漏掉了这一层**——不是判定不移植,是漏搬。
- **触发口径照抄参考版**:`429/500/502/503/504` 与 `Category == "rate_limited"`,以及参考版 `Retryable()` 里的 `provider_timeout / provider_network_error / provider_stream_error`;`Fatal()`(401/403 认证)与上下文超长、参数错一律不重试。
- **硬约束:已经产出内容就不重发。** 判据是「本轮正文与思考段为空、没有 `tool_calls`、没有待执行工具」。一旦开始吐字给用户,重发会让用户看到重复内容;此时宁可失败,也不重发。
- **必须可取消**:退避等待走 `Task.Delay(wait, ct)`,用户在「停止」按钮上的取消立刻生效,不允许睡满退避。参考版的 `Thread.Sleep` 写法不搬。
- **必须有上限并可见**:每轮最多 3 次重试;重试要发 `status` 文案 + `ModelRetry` 会话事件 + 轨迹记录,不允许静默卡住。
- **不搬「换模型兜底」**:参考版 `_maybe_fallback` 依赖 bundled 备用模型配置,Linux 没有这套配置,只搬原地重发;用尽后按 `stream_failed` 收尾,并在文档写明这是有意偏离。

### D-34 · 真机验证怎么驱动、冒烟判据盯什么(R8,2026-11-12,移植代理)

- **怎么驱动盒子**:人不在机器跟前,一律经 `rsh --box main bash -s` 送脚本执行。两个坑写在这里备用:①`rsh` 会在命令行上吃掉双引号 → 只用 `bash -s` + stdin 送脚本;②PowerShell 管道给原生进程会补 CRLF(远端报 `bash: line N: $'\r': command not found`)→ 本地先把脚本规范成 LF,再用 `cmd /c "… < file"` 喂进去。偶发 `tls handshake eof (through proxy 127.0.0.1:7890)` 属传输抖动,重试即可。工具与脚本留在 `_cmp/box/`(gitignore),盒子凭据不写进任何文件或报告。
- **冒烟判据不看字节哈希**:同一页面两次渲染会差 14~40 字节(文本次像素、时间戳等),所以「两次 PNG 字节不同」**不等于**「页面不同」。本轮判据改成三条:逐页人眼看截图 + `--smoke` 退出码 0 + `$TIPCL_HOME/logs/*.log` 0 WARN / 0 ERROR。要在 CI 里做字节级断言,必须显式给 `SMOKE_WAIT` 留出布局时间。
- **「首批结果不可信」要当成制度**:tipcl12 头 4 次冒烟把上一页的帧写进了别的页面文件名(4 份 PNG 字节全等,`bbf203ca…`),之后 20 次复跑(含清自解压缓存 `~/.net/tipcl12`、按原顺序重跑)再没出现。故真机冒烟要么跑两轮,要么第一批结果丢弃 —— 首轮「看起来对」不等于对。
- **真机验证不改产品代码**:遇到问题只记录事实 + 证据(见 D-36),把定性和修复留回主 agent,避免在「已验收产物」上顺手改东西。

### D-35 · 桌面快捷方式真机验证:入口打不开就走「应用自己那套 Core 代码」的等价路径,判据是 CLI 预检输出(R8,2026-11-12,移植代理)

- **首选路径**:按方案先试 GUI(实例库页 → 版本行右键 → 「创建桌面快捷方式」)。**真机上右键菜单打不开**(证据见 D-36 ②),GUI 自动化做不到这一步 —— 如实记录,不硬凑。
- **等价路径(明确写清走的是哪条)**:`.desktop` 由**产品自己的代码**生成,不是我手抄 —— 在开发机上以 `TIPCL_SHORTCUT_POSIX=1` 真跑 `TipCL.Core.Shortcut.CreateLaunchShortcut`,只把 `Exec=` / `Path=` 两行改成盒子上的真实值(`/home/user/tipcl/tipcl12`、`/home/user/tipcl`);`Icon=` 指向的 `tipcl.png` 由 `ShortcutIcon.Render()` 真跑生成。于是「字段顺序、`Name=`/`Comment=` 文案、引号规则、`Terminal=false`、`Categories=Game;`、LF 结尾、755 权限」全部来自产品代码;`-i <实例> launch <版本>` 这段参数还另外用**应用自己的 CLI** `--print-command` 对过一遍(`启动参数: -i 26.3 launch 1.20.1`)。
- **判据选「链路 + 结论」而不是「游戏起来没起来」**:盒子上没有账号、也不该真去下 JVM,所以「游戏没起来」不是失败。真正的判据是 **`.desktop → CLI → 预检` 真的执行并给出可执行结论**:实测 `实例: 26.3 | 版本: 26.3` → `实例 Java 设置: 自动选择` → `启动游戏前请先创建一个离线用户(「账号」页 → 添加离线账号)` → `=== CLI 退出(Launch,退出码 3)===`。**只有「点了没反应」才算不通过。**
- **没验到的部分必须写明**:「GUI 自己把文件写出来」这一步真机没做到(入口打不开),所以结论只能写成「等价路径下的链路已验证」,不能写成「GUI 生成的快捷方式已验证」。

### D-36 · 真机上发现的三个界面问题:本轮只记录与留证,不改代码(R8,2026-11-12,移植代理)

> ⚠️ **2026-11-13 更正**:第 1 条的归因有误 —— 崩的不是「版本设置」对话框,而是**实例详情页首次布局**(`InstanceDetailPage` 的孤儿 Border);实例库那枚齿轮提示写「版本设置」、实际打开实例详情页。见 D-39 与 `M4-H R9`。

- **①点版本行齿轮(版本设置)打崩应用**:`System.InvalidOperationException: The control StackPanel already has a visual parent Border while trying to add it as a child of ScrollContentPresenter (Name = PART_ContentPresenter, Host = ScrollViewer)`;`[ERROR] unhandled(UI)` 后进程直接退出,**2/2 复现**(日志 `~/.tipcl/logs/tipcl-20261112.log` 内有完整栈;异常发生在模板应用/布局阶段,`ScrollContentPresenter.AttachToScrollViewer` → 某个 `StackPanel` 已经是别处的 `Border` 子节点)。
- **②版本行右键菜单真机打不开**:同坐标左键双击能切到启动页(证明命中该行)、悬停高亮也生效,但注入 button-3 后既不弹菜单、`xwininfo -root -tree` 也无新窗口(7 次,含按像素网格量准的行内坐标);而实例卡的「•••」按钮走**同一个** `ContextMenuFx.Show` 能正常弹出 → 菜单门面本身没问题,问题在「谁能触发它」。**定性未完成**:不能排除 Avalonia/X11 侧对右键 `PointerReleased.InitialPressMouseButton` 的处理,也不能排除是 XTest 注入的差异(盒子上 `xev` 不可用,没能在应用内取证),故只记录现象,不下结论。
- **③界面「版本选择」与 CLI 预检看的版本目录不一致**:界面列的是 `<TIPCL_HOME>/.minecraft/versions`(全局目录)里的 `1.20.1`,CLI 预检找的是实例目录 `<TIPCL_HOME>/.minecraft/26.3/versions` → 界面里看得见的版本,启动时报「版本未安装」。代码里 `Instance.VersionsDir()` 是两处同源(`PPID` 路径 + `"versions"`),所以**根因本轮没定**,只把现象与目录证据留下(盒子实例 `26.3` 自己的版本是 `<…>/26.3/versions/26.3/`,含 41 MB 的 `26.3.jar`,并不在界面列表里)。
- **为什么不在本轮修**:R8 的任务是「真机验证 + 如实记录」,而这三处都涉及「哪个行为才算对」的取舍(②要先定判据归属,③要先定全局/实例 versions 的语义),在已验收的产物上顺手改会污染验证结论。故只记录。

### D-37 · 拖放导入真机不可用:根因是窗口没有 XdndAware,不写含糊话(R8,2026-11-12,移植代理)

- **代码侧是接了的**:`FileDrop.Attach`(`DragDrop.SetAllowDrop` + `DragOver`/`Drop` → `FileKinds.Identify` → 导入链)挂在 MainWindow / 实例库 / 版本管理 / 下载 / 插件页。
- **真机上为什么不行**:`xprop` 查 launcher 窗口**没有 `XdndAware`**(同一脚本查 Chrome 窗口 `XdndAware(ATOM) = BITMAP` 正常,说明检查本身有效)。X11 拖放协议要求**目标窗口先声明 XdndAware**,源端才会发 `XdndEnter`;缺这个属性,任何程序都拖不进来 —— 这不是我脚本的问题。
- **实测补证**:用真实拖放源 Thunar(打开 `~/drop-test`,里面有 `example-mod.jar`)做了一次真拖:按下 → 16 步移动到应用窗口内 → 松开;截图里能看到拖影落在应用窗口内、Thunar 图标变蓝选中,松手后应用**毫无反应**(`26.3/mods/` 目录无变化、应用日志无新行)。
- **写法要求**:这条只能写成「**未完成 + 根因是缺 XdndAware**」,**不能**写成「拖放已在真机验证通过」,也不能含糊成「自动化不可靠」;证据文件 `_cmp/box-c-50-setup.png`、`-51-before-drag.png`、`-52-dragover.png`、`-53-dropped.png`。

### D-38 · Linux/X11 无 XDND:拖放不可用是平台限制,不许含糊成「接线问题」(R8 复核,2026-11-12,主 agent)

- **事实**:Avalonia 11.3.2 的 X11 后端**没有实现 XDND 协议**。取证:对 `~/.nuget/packages` 下 **149 个 Avalonia 程序集**做 UTF-16 字符串搜索,`XdndAware / XdndEnter / XdndPosition / XdndDrop / XdndSelection / XdndStatus / XdndFinished / XdndTypeList / XdndActionCopy` **全部 0 命中**;方法有效性由同目录 `Avalonia.X11.dll` 内 `_NET_WM_STATE` 命中 9 次佐证。
- **真机现象与之吻合**:launcher 窗口 `xprop` 无 `XdndAware`(Chrome 窗口有),因此拖放源不发 `XdndEnter`,任何程序都拖不进去;用真实拖放源(Thunar)实测松手后应用毫无反应。
- **结论与措辞要求**:P7 的 `FileDrop / FileKinds` **代码已经写好且逻辑有测试**,但**入口在 Linux 上不可用**;文档与汇报一律写成「代码已实现,**Linux/X11 平台不支持**」,禁止写成「已做成统一入口」或「自动化不可靠」。Windows 参考版不受影响(那边是 WPF/Win32 的 OLE 拖放)。
- **不做的事**:不为了实现拖放去引 X11 互操作手写 XDND,也不升级 Avalonia(版本冻结在 11.3.2)。要恢复这个能力,只能等 Avalonia 在 X11 后端实现 XDND 后重新评估。
- **可用替代**:各页已有的显式入口(插件页「从文件安装」、「链接安装 / 导入本地文件」、AI 页的剪贴板粘贴与文件选择器),这些是真机可用的路径;若后续要加一句界面提示引导用户用按钮,属可选改进。

### D-39 · 一个控件一个父级:容器单持有,且无头测试必须真跑一次布局 pass(R9,2026-11-13,移植代理;主 agent 复核)

- **规则**:页面/对话框里的「行容器」(StackPanel 等)只能有**一个**宿主,并且**只赋值一次**。禁止「每次渲染 `new` 一个 `ScrollViewer` 把同一个 StackPanel 塞进去」——旧宿主被换下时它的 `ScrollContentPresenter` 仍持有该子级,下一次布局就抛 `already has a visual parent`。正确形状:`_rowsHost` 字段创建一次 → `_rowsHost.Content = _rows` → `_host.Content = _rowsHost`;切换分段只改状态与数据。
- **Avalonia 的报错时机很阴**:给**尚未进可视树**的 `ScrollViewer` 赋 `Content` 不报错,要等它第一次 `Measure → ApplyTemplate → AttachToScrollViewer` 才抛;异常从 `MediaContext.Render()` 的布局回调冒出,Dispatcher 视为 `unhandled(UI)` → **进程直接退出**(用户看到的是「点完按钮进程没了」,没有错误框,极难自查)。
- **所以无头测试要挡住这类回归,必须显式推一次渲染**:`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` + `Dispatcher.UIThread.RunJobs()` + `TopLevel.CaptureRenderedFrame()`(冒烟截图用的同一条路)。只 `await Task.Delay(...)` 不会触发 `Measure`,非法父级可以在测试里一直潜伏 —— 这就是「宿主机全绿、真机一布局就崩」的根因,以后写任何布局相关回归测试都要照这条来。
- **版本列表的实例上下文必须显式传**:`Instance("")` 会落到根实例,于是「不传 `instance`」= 读**全局** `<TIPCL_HOME>/.minecraft/versions`;凡是要展示或操作某个实例的版本,一律显式 `instance = <实例名>`,不要依赖这个隐性兜底。
- **真机归因纪律**:出现「点 X 崩了」时,先确认 X 这个按钮**实际**做了什么。本轮就是提示文字写「版本设置」、点击实际打开「实例详情」,导致归因跑偏到对话框上;提示/标题与实际行为不一致**本身就是缺陷**,要单独记录。

### D-40 · 启动提示必须等主窗口真正可见(Avalonia 对不可见 owner 的 ShowDialog 直接抛)(R12,2026-11-13,移植代理;主 agent 复核 + 真机验收)

- **事实**:`ShowDialog(owner)` 与 `Dlg` 系列对话框对 `IsVisible == false` 的 owner 抛 `InvalidOperationException: Cannot show window with non-visible owner.`;而 `App.OnFrameworkInitializationCompleted` 里 `desktop.MainWindow = win` 之后窗口还没 `Show()`,任何在这一刻发起的提示都会失败。
- **真机现象与精度**:修复前每次启动一条 `[WARN] 启动提示失败: …`;更重要的是**真实首启时首启向导与同意门都不出现** —— 抛的是第一个提示(向导,无延迟),异常中断了整个 `RunStartupPromptsAsync`,同意门(自带 400ms 延迟,单独跑其实弹得出来)因此永远轮不到。所以「同意门在 Linux 上弹不出来」只在 `first_run=true` 时成立;`first_run=false` 时旧产物也弹得出来(实测两版都弹)。
- **规则**:凡启动阶段要弹的窗口(向导 / 同意门 / 更新提示…)都必须先等主窗口可见,判断用窗口状态(`Opened` 或 `IsVisible` 变化),并给等待上限;窗口一直不可见时记一条**文案不同**的 WARN,别把「没等到可见」和「弹窗失败」混成同一条日志。无头路径(`--smoke`)不进启动提示。
- **不许的写法**:固定 `Sleep` 硬等;把 owner 传 null 改成非模态绕开(会丢模态语义);或在 `ShowDialog` 外面 catch 一下就当前面没发生 —— 这次事故就是「异常被吞成 WARN」把首启向导悄无声息地吃掉的。

### D-41 · 版本行齿轮归位:文案与行为以 Windows 参考版为准(R12,2026-11-13,移植代理;主 agent 复核)

- **事实**:Linux 侧版本行齿轮 tooltip 写「版本设置」,点击执行 `OpenDetailAsync`(切到实例详情页);Windows 参考版同一位置(`Pages/LibraryPage.cs:713-720`)执行 `VersionSetupDialog.ShowAsync(inst, id)` + `LoadAsync()`。
- **代价**:真机上用户点「版本设置」被送进实例详情页,撞上 R9 的崩溃,还让 R8 把崩溃归因到了「版本设置」对话框 —— 归因跑偏的直接原因就是文案与行为不一致(规则已在 D-39 立下)。
- **规则**:同一入口的文案 / 图标 / 动作三者必须一致,以 Windows 参考版为准;确要偏离必须同时改文案并在 `DECISIONS.md` 立条目,禁止「借壳」。
- **实例详情页的正当入口**:版本选择区头部的「实例详情」chip(`LibraryPage.cs:130`)与实例卡片右键菜单(`FolderMenu`),两者都走同一个 `OpenDetailAsync`,不因本次归位而丢失。

### D-42 · 冒烟捕获:捕获前必须推一次渲染 pass,并丢弃首帧(R13,2026-11-13,移植代理;主 agent 逐字节复核)

- **事实**:无头会话里只跑 `Dispatcher.UIThread.RunJobs()` **不会**触发 `Measure/Arrange/重合成`(与 D-39 同源);`CaptureRenderedFrame()` 因此可能抓到「尚未重合成的上一帧」。R13 用 stash 前后逐像素对照证明:改前那一帧的插件页空状态整片缺失(2301 px,0.465%),改后与真机参考帧一致。
- **规则**:冒烟类截图必须在**捕获前**显式推一次渲染 pass(`AvaloniaHeadlessPlatform.ForceRenderTimerTick` + `RunJobs`),再**捕获两次、丢弃首帧、只保存第二帧**;`SMOKE_WAIT` 未显式给时要有最小 settle(现为 400ms),显式给了就仍按原语义等,不许叠加。
- **不许**:把「截图看着对」当成渲染完成的证据;也不要因为哈希抖动就把 PNG 字节相等当页面身份判据(见 R11 小节:同页两次渲染可差 14~40 B)。
- **仍未解释的现象**:R8 在真机冷启动首批见过「连续 4 页写成上一页(launch)的帧且字节完全相同」,之后 20 次复跑 + 本轮冷启动 16 次都**没复现**。本轮只把它降级为「已加防御性确定性」,不声称已修掉。

### D-43 · 真机定位控件只用窗口相对坐标;无头/冒烟运行必须用隔离的 TIPCL_HOME(R11/R12/R13,2026-11-13,主 agent)

- **窗口相对坐标**:X11 下 `xdotool getwindowgeometry` 报的 X/Y 与窗口客户区在整屏截图里的位置**并不一致**(实测差 18~50px),所以「整屏截图量位置再反算绝对坐标」连点四轮都可能全部落空。正确做法:`import -window <WID>` 只截窗口 → 在窗口图像里量位置 → `xdotool mousemove --window <WID> x y click 1`。本轮用这套方法一次命中(Y 方向偏 10px 以内)。
- **隔离 HOME**:跑 `--smoke` / 无头验证必须显式给一个**隔离的 `TIPCL_HOME`**;脚本里别用 `$home` 这类会与 PowerShell 只读变量 `$HOME` 撞名的名字(赋值失败不报错,`TIPCL_HOME` 会静默落到用户目录根,把 `config.json` / `logs` / `ai_chats.json` 写进 `C:\Users\Administrator`)。出事后按「CreationTime 当天 + 路径精确匹配」逐个清理,并复核真实数据基线(`%LOCALAPPDATA%\TipCL\config.json` 的 mtime / sha)。
- **测活与判据**:进程测活用**产物自己的路径**(改名后的单文件产物 cmdline 里没有 `TipCL.App` 子串,用 `pgrep -f TipCL.App` 会得出「进程没了」的假结论);页面身份看内容/像素,不看 PNG 字节哈希。

### D-44 · 测试套件偶发 `Call from invalid thread` 的真根因:进程级 `Dispatcher.UIThread` 被跨线程抢占(R14,2026-11-13,移植代理;主 agent 复核)

- **别先怀疑并行**:`tests/TipCL.Tests/AssemblyInfo.cs` 早已 `DisableTestParallelization = true`;「关并行 / 加 Collection / runner.json」对本问题全是 no-op(探针在 ~1820 次派发里 0 次并发)。遇到这类 flaky,先查**进程级静态状态**,再查并发。
- **机制**:`HeadlessUnitTestSession` 每次派发都 `Dispatcher.ResetForUnitTests()`(置空进程级 `Dispatcher.UIThread`)+ `AppBuilder.SetupUnsafe()`,于是每个派发边界都有一个「谁先读谁赢」的窗口;而 `Dispatcher.UIThread` 的线程亲和由**构造它的那份 `IDispatcherImpl`** 决定。赢家可能是会话线程(正常),也可能是**测试里自己另起的一次无头初始化**留下的实现(归测试线程 ⇒ 之后必失配),或产品码的后台线程。
- **纪律**:① 测试里**禁止**再自己 `AppBuilder.Configure<…>().UseHeadless(...).SetupWithoutStarting()` 另起初始化 —— 必须统一走 `TestApp` 的会话,否则会把「归错误线程所有」的实现留在**根** locator 作用域(`AvaloniaLocator` 是进程级静态,不是线程本地);② 会话起来后把「会话线程自己那份 `IDispatcherImpl`」钉进根作用域当兜底(测试代码内、失败可退化为旧行为);③ 修 flaky **必须给机制级证据**(IL / 探针时间线 / 最小复现对照,如基线 13/13 失败 vs 修复 9/9 通过),**不许**用「跑了几次没失败」当结论。
- **代价与风险**:兜底走反射访问 Avalonia 内部类型与私有字段(`AvaloniaLocator.Current` / `_parentScope` / `_registry`、`GetService`);升级 Avalonia 后改名会**静默失效**,后果是退回偶发失败、不会新增失败。若将来 Avalonia 给出公开入口(或 headless 会话自身保证 UIThread 归属),应改回公开 API。
- **判据(以后照用)**:验收这类修复要同时给「改动前后的失败率/机制」与「用例总数不变(335 = 334 + 1 skip)、耗时不变差」,三者缺一不可。

### D-45 · UiPrefs 落 config.json(补上 D-19 那条「缺口 B」:折叠态 / 版本收藏 / 下载页当前实例)(R15,2026-11-13,移植代理;主 agent 复核)

- **背景**:Windows 把界面偏好落独立文件 `wpf-ui.json`(`src/TipCL/UiPrefs.cs` + `MainWindow.xaml.cs:1427 LoadUiPrefs` / `:1442 SaveUiPrefs`);Linux 侧当时无对应件,折叠态与版本收藏先进程内静态表(即 D-19 的降级项)。R15 补齐:语义照 Windows,**落点改为同一份 `config.json` 的 `ui_prefs` 对象键**(Linux 只有一个用户配置文件,不另造第二个)。
- **键布局**(snake_case,与既有界面键同风格):
  - `ui_prefs.expanded`:`{"<前缀>:<分组键>": bool}`,前缀 `lib:`(实例库)/ `vm:`(版本管理),对应 Windows `Expanded` 的两组键名;
  - `ui_prefs.vm_favorites`:`{"<实例名>": ["<版本 id>", ...]}`,对应 Windows `VmFavorites`;**实例库与版本管理两页共用同一张表**;
  - `ui_prefs.catalog_instance`:字符串,对应 Windows `CatalogInstance`。
- **硬约束**:① 读一律**现读** config.json 的内存投影,**不许在静态字段留副本**(否则又退化成缺口 B);② 缺键 / 空字典 / 类型不符 / config.json 损坏 → 取默认值,**绝不抛**,启动流程不受影响;③ 写失败只记一行日志,不影响交互。
- **不做镜像的项(避免两个真源)**:`LastUser` → Linux 本就落 `last_user`(`LaunchPage.cs:616/927/1031`);`Notes` → 本就落 `ui_notes`(`:338/341`);`Dark`/`Anim` → 本就有 `ui_dark`/`ui_motion`;`AnimScale` → Linux `Motion` 只有 `Enabled`(全仓无全局动画缩放因子),造一个没人读的键=死配置;`SideWidth`/`SideFrost*` → Linux 无侧栏(同 PARITY §5)。
- **代价(如实记)**:每次用户手势(点星标 / 切分组 / 换下载页实例)都会**同步整份重写** config.json;正常几毫秒,极端占锁(杀软扫描,本仓库测试曾因 Defender 占锁出过假失败)最坏约 2.5s 会阻塞 UI 线程。Windows 版是关窗时统一保存,没有这个窗口。**本轮未做防抖/后台写,已列为遗留**。
- **验收**:Release 0 CS/CA 警告;`dotnet test` 由 335(334+1)→ **343(342+1)**,净增 8 条(prefs 层 6 条 + 页面接线 2 条),连跑 3 次全绿(主 agent 独立复跑 2 次亦全绿);8 页冒烟 8/8 exit=0、日志 0 WARN/0 ERROR;**负控**:把 `Load` 临时改回进程内静态缓存 → 6 条里 3 条失败(含「收藏必须来自磁盘」那条),按字节还原并 SHA256 核对后 6/6 通过 —— 证明回归用例有牙。
- **测试覆盖的边界(如实)**:页面接线用反射调私有方法(改名会编译失败,属有意契约);**没有点击级 UI 测试**,「点星 → 重启 → 收藏还在」这条链由「prefs 层往返(含用全新 Config 实例从磁盘读回)+ 页面接线 + harness 实操」三处拼起来证,**不是** GUI 端到端验过。

### D-46 · 启动分辨率只留一个真源:`config.json` 的 `default_resolution`(R16,2026-11-13,移植代理;主 agent 复核)

- **背景**:Windows 把这对值最终落到 config 的 `width` / `height`,再由 `get_settings` 派生出 `default_resolution`(`Core/BackendApi.cs:4125` 读 / `:4339` 写),启动页读写的是这个**派生键**(`Pages/LaunchPage.cs:881` / `:1002`);Linux 侧设置页读写的本来就是 `default_resolution` 本身(`Pages/Settings/SettingsPage.SaveLoad.cs:99` / `:322`,形如 `[宽, 高]`)。
- **决定**:Linux 侧只保留 `default_resolution` 一个真源,启动页与设置页读写同一个键、同一份 `config.json`;**不**再派生 / 回写 `width` / `height`(两个键并存必然有一处先过期,「双改」时互相覆盖)。`Config.cs:107` 把 `default_resolution = [854,480]` 写进 DEFAULT_CONFIG,Windows 的 `width` / `height` 键保留但无人读。
- **入口位置**:照 Windows 放「启动配置」浮层的「更多设置」折叠区(Java → 内存 → 分辨率 → 直连)。不放 chip 行(那行是只读摘要,Windows 也没有)、不放设置页(那儿没有「启动前顺手改一下」的位置)。
- **读值语义**:设置里的值不合法(缺键 / 非两元序列 / 非数字 / ≤0)一律回落 854×480(Windows 默认值);半坏值**逐槽位**回落(`[1280,"x"]` → 1280×480)。手输非法文本同样回落并**把归一化结果写回输入框**。
- **不做双向监听**:启动页在 `OnActivate` 与首次填充时重读设置。设置页与启动页同屏同时改,后写覆盖前写 —— 与 Windows 同(它也在页面激活 / 保存时取一次)。
- **验收**:`tests/TipCL.Tests/App/LaunchResolutionTests.cs` 5 条 —— 两页共用同一个键(含「换全新 Config 实例从磁盘读回」)、坏值回落、页面框 ↔ 设置键、启动链边界(版本设置的 `window_width/height` 优先)、**配置浮层必须打得开且「分辨率」在「更多设置」组里**(就是抓那个强转崩溃的回归)。

### D-47 · 新闻抓取:Linux 侧一律降级不抛(与 Windows 的有意差异);`LaunchPopups` 强转崩溃去掉强转而不是改返回类型(R16,2026-11-13,移植代理;主 agent 复核)

- **新闻的差异**:Windows `Core/Launcher/News.cs:89-92` 在「两条源都失败且缓存为空」时 `ExceptionDispatchInfo.Capture(ex).Throw()`,由调用方(后端 RPC)兜。Linux 侧调用方是启动页那条 fire-and-forget 的新闻行,**抛出会把整页带崩**;故 `News.Fetch` 改为「两条源 → 缓存 → 空列表」全程不抛,只 `Utils.log.Info` 记一笔(具体原因由下层 `DownloadManager.FetchJson` 记 WARNING),`_rows_from` 遇坏结构也返回空(Windows 在那里抛 KeyError / TypeError)。这是**有意的降级差异**,已写进 `docs/PARITY.md` §9.3。
- **修法选择**:`LaunchPopups._configBody` 的类型从 `Border?` 改成 `Control?` 并去掉 `(Border)` 强转,而不是把 `BuildConfig()` 的返回类型改成 `Border` —— 后者要动那条构造链上的所有 `UiClass` 返回值,收益为零、风险不小;前者是最小改动 + 一条回归用例钉住「浮层能打开、分辨率在里面」。

### D-48 · 权限裁决必须「裁决 + 理由」一起产出;「不询问」开关与拒绝理由都落在 Core 一侧(R19 / R19b,2026-11-13,移植代理;主 agent 复核并补 R19b)

- **背景**:参考版 `Permission.Decide` 返回 `PermissionResult(decision, reason, rule_id)`,界面把 `reason` 显示在确认卡上(`Pages/AiPage.cs:2583-2620`);Linux 侧 `ToolRunner.Decide` 只返回 `allow / ask / deny`,调用点把**标签**当理由传进卡片,卡片那行永远空着(旧注释还写着「第三参留空」,属把缺陷写成设计)。
- **决定一(接口形状)**:理由与裁决**必须由一次调用一起产出** —— `ToolRunner.PermissionVerdict(decision, reason)`。**不留**「无理由的 `Decide(string)` 重载」:分开返回就是这条链断掉的成因,留着旧重载等于给下次再断一遍留门。调用点只有 `RunAsync` / `AiSelfTest` / 测试,同步改干净。
- **决定二(不带 rule_id)**:Linux 的「一直允许」是按**工具名**记的进程内集合(`_alwaysAllowed`),界面没有「按某条规则记住」的入口;参考版的 `rule_id` 在 Linux 无消费方,不搬。
- **决定三(「不询问」开关落点)**:`ai_permission_dont_ask` 在 Linux 的 `Config` 里有默认值却没人读,参考版在 `Agent` 里消费(需要确认的写操作直接拒绝)。修法是把语义叠加在 `ToolRunner.ApplyDontAsk`(Core 侧,裁决的唯一出口),而不是塞进界面 —— 否则「谁在问、谁在拒」又会出现两个真源。只作用于 `ask`:自定义规则说的 `allow`、只读与 `yolo` 的 `allow` 都不受影响。
- **决定四(拒绝理由只进界面)**:被拒时把理由写进 `ToolCall.error`(「已拒绝:&lt;理由&gt;」)并由 `AiPage.Summarize` 显示;**模型可见**的文本保持原样(仍是 `permission_denied` 的结构化错误)。理由给用户看,不给模型当指令 —— 与「工具结果只是数据」这条硬规矩一致。
- **未做(如实)**:参考版的 `PermissionNote`(`Permission.cs:256-276`,把当前档位写进系统提示,消费点 `Agent.cs:348` / `:368`)Linux 侧仍然没有 —— 属独立条目(要随每轮把档位注入系统消息,会改模型可见文本),已记进 `docs/PARITY.md` §9.4。
- **验收**:`dotnet test` **371 = 370 通过 + 1 跳过 + 0 失败**(R16 的 355 → +16),`--ai-selftest` **52 / 0**,8 场冒烟 8/8 exit=0 且 0 WARN/0 ERROR;移植代理的红证据见 `_cmp/r19-*-red.txt`,主 agent 对自己的 `ApplyDontAsk` 做了变异(2 条红)并按 SHA256 字节级还原。

### D-49 · Markdown 链接改用 `InlineUIContainer` 做成可点(D-22 那条平台限制不再成立)(R17,2026-11-13,主 agent)

- **背景**:参考版 `Pages/Ai/MdView.cs:367` 用 WPF `Hyperlink`(可点、`RequestNavigate` 开浏览器);P5 移植时判定「Avalonia 的 `Span` 不是 `Control`,没有 `Cursor`/`ToolTip`/`Click`」,链接只能主题色 + 下划线(D-22 第 2 条)。
- **更正**:该判定只对 `Span` 成立 —— Avalonia 的 **`Inline` 可以是控件宿主**(`InlineUIContainer`)。链接改成 `InlineUIContainer` + 内部 `TextBlock`:下划线/主题色/字号照旧,另加手型光标、URL tooltip(`ToolTip.SetTip`)、`PointerReleased` → 打开外链。**视觉不变,只增加交互**。
- **打开外链的落点**:`MdView.UrlOpener`(`internal static Action<string>`,默认 `OpenExternal`:Windows `ShellExecute` / macOS `open` / Linux `xdg-open`,失败只记不抛)。做成可替换的钩子是**为了测试**:用例里换成收集器,断言「点哪个链接就开哪个 URL」,而不会真的弹浏览器。仓库里各页原本各有一份私有 `OpenUrl`(AccountPage / MultiplayerPage / PluginsPage / PluginHostPage / SettingsPage.Cards),这次**不去统一**(改动面大、收益只是少几份重复),`MdView` 自带一份最小的。
- **仍然不成立的部分(如实)**:D-22 第 2 条的另一半(`Run` 没有 `Background` → 行内码没有底色)依然存在,没动;链接的悬停态(变色/常亮下划线)也没做 —— 参考版的 `Hyperlink` 自带,这里保持静态样式。
- **测试边界(如实)**:`App/MdViewLinkTests`(4 条)断言结构(是 `InlineUIContainer` 而不是 `Span`、tooltip 是 URL、光标/下划线/字号在、粗体与列表里的链接也在)与打开链(`OpenLink` → `UrlOpener`),但**没有**真的构造 `PointerReleasedEventArgs` 抬事件;也**没有像素级截图证据**(冒烟态没有任何页面渲染 Markdown 链接,AI 页冒烟是空态)—— 判据是结构与数值断言,不声称「真机看着没问题」。

### D-50 · 多选导入的统一口径;「数据包装进存档」按界面写明的意图落地(三条参考版缺陷不照抄)(R18,2026-11-13,移植代理;主 agent 复核并裁定源目录优先级)

- **多选口径**:参考版有 `Dlg.PickFiles`(`Multiselect = true`,三处使用:下载页导入本地 / 反馈页附件 / 模组对话框导入模组),Linux 侧原先只有单选。补齐时定了三条:
  1. **取消 = 空数组**(不是 `null`),调用方「什么都不做、也不报错」;
  2. **单选(N=1)必须逐字沿用改动前的提示 / 状态 / 重载** —— 只有 N>1 才追加汇总,`ImportBatch.Total <= 1` 直接返回空串,从实现上堵住「给单文件加提示」;
  3. 汇总口径收敛到一处(`Common/ImportBatch.cs`):「导入 N 个:成功 X,失败 Y」+ 逐条「文件名:原因」,三处入口共用,免得各写一套文案。
  系统对话框本身(`AllowMultiple` 在 GTK/portal 上的表现)**未在真机验证**(无头环境点不了系统对话框),测到的是「多选循环把每个文件都处理了」这一层。
- **`extra["save"]` 按界面意图落地**:下载页问「装进哪个存档」并把它写进 `extra["save"]`(`Pages/CatalogPage.cs:753`),而参考版 Core **从不读这个键**(主 agent 独立核实:全仓库只有这一处写入) —— 用户选了等于没选。Linux 侧按界面写明的意图实现:装完就地复制进该存档的 `datapacks/`;**失败只记一条带原因的日志**,不把已经装好的数据包报成失败(数据包已经进实例目录,复制只是附加动作)。
- **源目录优先级(主 agent 裁定)**:`InstallDatapackIntoSave` 的源改为**版本目录优先、实例根回退** —— 带 `version` 的 `get_installed_datapacks` 列的就是 `<实例>/versions/<版本>/datapacks`,「列表里有的包点下去必须能装上」;实例根(`<实例>/datapacks`)是存档管理页列的那个目录,作为回退保留。参考版写死实例根(无视 `version_id`),所以隔离版本下会出现「列表里有、点下去说数据包不存在」。防路径穿越的判据(`_find_datapack` 里「解析后父目录必须仍是该目录」)原样保留。
- **不凭空造存档**:参考版在存档目录不存在时无条件 `EnsureDir(<游戏目录>/saves/<存档>/datapacks)`,会造出一个只有 `datapacks/` 的空壳存档(游戏里是个打不开的假世界);同文件的 `DeleteSave` / `OpenSave` 是会检查存在性的。Linux 侧改为显式抛 `存档不存在: X`,一个目录都不建 —— 与同文件其它存档操作一个口径。
- **不动的地方(与参考版一致,避免扩大改动面)**:同名文件就地覆盖(`overwrite: true`)不产生副本;目标 `datapacks` 目录不存在就创建;目标路径与版本隔离游戏目录算法不变;**存档管理页「装数据包」列表仍按实例根列**(要让隔离版本也能从版本目录里选,需要给 `get_installed_datapacks` 带 `version`,属独立条目,已记进 `docs/PARITY.md` §9.6)。
- **验收**:`dotnet test` **397 = 396 通过 + 1 跳过 + 0 失败**(新增 22 条:多选 13 + 数据包 9);Release App/Core 构建 0 CS/CA;10 场冒烟 exit=0、0 WARN/0 ERROR;红证据 `_cmp/r18-a-red.txt`(多选未接循环时 `Expected 2 Actual 1`)、`_cmp/r18-b-red.txt`(`SaveError : 数据包不存在: dp-iso.zip`)。三条参考版缺陷已进 `docs/PARITY.md` §6 第 8–10 行。

### D-51 · 对话框点击必须「先落结果、再关窗」:一行 `Finish` 收口 Ask / Choose / Prompt(R21b,2026-11-13,移植代理;主 agent 复核 + 真机复验)

- **事实**:真机首启的「同意门」点「同意」/「暂不同意」窗口不动,`await win.ShowDialog(host)` 永不返回;此前只能 `xdotool windowclose` 硬关,而关窗会走 `win.Closed` 上那条「兜底 = 取消」——于是**用户点了「同意」,配置里却写 `feedback_consent: false`**。根因:Windows 参考版 `Dlg` 是 WinForms 图层,设 `DialogResult` / `layer.Close()` 顺带关掉图层;移植成 Avalonia 独立 `Window` 后,三处点击只 `TrySetResult`、**没有任何人关窗**。
- **修法**:私有 `Finish<T>(tcs, value, win)` —— 先 `TrySetResult`(**返回值 false 即「已经收尾过」直接 return**,键盘 Enter 同时触发输入框 `KeyDown` 与 `IsDefault` 按钮时不会重复关窗),再 `win.Close()`。顺序不可反:TCS 只认第一次设置,先 `Close()` 会让兜底抢答。`Ask` 的公开签名一字未改(实现体下沉到私有 `AskCore`)。
- **同批对齐的两处**:1`Prompt` 补「取消」(→ `null`)+ `IsCancel`,与参考版 footer「取消 + 确定」一致(此前只有确定,没有反悔出口);2`Alert` 改**单按钮「知道了」+ 内部结果恒 `true`**(照参考版:Esc / 直接关窗都算「知道了」),此前是 `Ask(..., cancel: null)` 会多画一个取消按钮、关窗还被当成取消。6 个调用点全是 `await Dlg.Alert(...)`,返回类型本就是 `Task`、值不外露,不存在兼容问题。
- **`Panel` / `OpenPanel` 为什么不用改**:它们本来就是 `close.Click += … win.Close()`,只补 `IsCancel = true`(Esc = 关闭);**不设 `IsDefault`** —— 面板体里常有输入框,Enter 不该等于「关闭」。
- **验收(真机 + 测试双层)**:真机点「同意」→ 对话框消失 + `config.json` 写 `feedback_consent: true` + 进程存活 + 0 unhandled;`SMOKE_PROBE=dialog-close`(点确定 `Task=True` / 点取消 `Task=False` / 只关窗 `Task=False`)exit 0;`App/DlgCloseTests` 6 条(含 Alert 单按钮)。**红证据**:临时删掉 `Finish` 里的 `win.Close()` → 4/5 红;临时把 `Alert` 还原成 `Ask(..., cancel: null)` → Alert 那条红。
- **教训**:凡是「平台自带语义代劳了一步」的地方,移植时要显式问一句「这一步在新平台上谁做」。WinForms 图层的 `DialogResult` 是隐式副作用,换成独立 `Window` 后必须手动补,而**症状会伪装成「用户没作答」**(await 不返回 ⇒ 调用方超时/默认值),极难从表面现象归因。

### D-52 · 浮层里的隐式主题控件画不出来 = 宿主链断了:`Popup` 必须挂进页面树(R21b,2026-11-13,移植代理;主 agent 复核 + 真机复验)

- **事实**:启动页齿轮打开「启动配置」浮层后,**除 TextBlock 与外,实例 / 版本 / 用户 / Java / 内存滑块 / 分辨率输入框全部空白**(真机像素扫描:整行只剩 `×` 与 `4096 MB` 这类文字),而同一份 AppImage 的主窗口与设置页里同批控件完全正常。
- **根因(Avalonia 机制)**:隐式 `ControlTheme`(`FluentTheme` 里的 `{x:Type TextBox}` / `ComboBox` / `Slider`)走 **`TryFindResource`**,要沿「控件 → 逻辑/可视父链 → `TopLevel` → `Application.Styles`」查宿主;`MakePopup` 造的 `Popup` **从没挂进任何视觉/逻辑树**,它的 `Child` 继承不到宿主 ⇒ 模板为空、量出 0×0。反过来说:显式赋 `ControlTheme` 的控件(`Ui.Btn` / `Ui.Switch` / `Ui.Multi`)与走 `Application.GetResourceObservable` 的 `Res.Bind` **不需要**这条链,所以照常显示 —— **「显式主题画得出、隐式主题画不出」是宿主链断掉的指纹**,按这个指纹才定位到 `MakePopup`(全仓唯一的 `new Popup`)。
- **修法**:`MakePopup` 把 `Popup` 加进页面根面板(`Content is Panel host → host.Children.Add(p)`),并**在拿不到面板时记一条 WARN**——不许静默退化成「浮层又能点又空白」。一份改动覆盖启动页三个浮层。
- **不动布局的证明**:`Popup` 挂进树后由主窗口 `OverlayLayer` 承载,自身不占布局格。用**单变量对照**验:还原 `LaunchPopups.cs` / `SmokeRunner.cs` 到 `HEAD` 构建跑 4 次、装回修复版再跑 4 次,8/8 张 launch 页 `smoke.png` **逐位相同**(`070d6ad6682d6a86…`,564,149 B)。
- **真机观感变化(如实)**:浮层仍是独立的 override-redirect X 窗口(374×493),但控件挂在主窗口宿主链上;点浮层外 light-dismiss 正常关闭且那次点击照常落到被点的页签;按 `Esc` 仍不关浮层(沿用 R21 记录,未改)。
- **真机测试纪律(本轮踩出来的两条)**:1盒子是非 UTF-8 locale 时 `xdotool search --name '<中文>'` **静默返回空**(窗口明明在屏幕上)→ 改用 `--onlyvisible --name ''` 枚举 + shell `case` glob 做字节匹配;2点「非可聚焦区域」(浮层标题、设置页标签)**不会**让 TextBox 失焦 ⇒ 验证「离开输入框即落盘」必须点**另一个输入框**;3rsh 分片传输 **6 MB 会 `h2 protocol error`,2 MB 可用**。
- **教训**:把「控件画不出来」先当**样式/宿主链**问题查,不要先怀疑数据或可见性;取证方式用「同形控件 × 在树/不在树」的**单变量对照**(本轮的 A/C 与 B/D 两组),比在真机上反复点更早、更准地定住根因。

### D-53 · 本仓的 `.ps1` 有「必须留着 BOM」的:`tools/package.ps1` 掉 BOM 就会语法错(R21b,2026-11-13,主 agent 实测)

- **事实**:`tools/package.ps1` 是**带 UTF-8 BOM** 的(HEAD 首三字节 `EF BB BF`),而这不是历史遗留 —— Windows PowerShell 5.1 对**无 BOM** 的 `.ps1` 一律按系统 ANSI 码页解码;文件里的中文让字节流错位之后,末行的引号被吞进未闭合字符串,整份脚本从「**0 个语法错误**」变成「**18 个**」。实测三段:`[Parser]::ParseFile` 在 HEAD 上 **0**;同一份文件把 BOM 去掉后 **18**;把 BOM 放回去又回到 **0**。也就是说 **BOM 就是这份脚本能在 `powershell -File` 下跑起来的前提**。
- **怎么踩上的**:收尾时想给它补两行使用提示,用了编辑工具 —— 工具**静默丢掉了 BOM**(单独实测:一个 `EF BB BF` 开头的文件被编辑工具改一次后,首三字节变成 `41 41 41`,LF 与内容都没变)。结果脚本在 PS 5.1 下直接语法错,而 `git diff` 看起来只有「加了两行」。
- **规矩**:1需要 BOM 的文件(**现在只有 `PROGRESS.md` 与 `tools/package.ps1`**)只用**追加式**写入(`[IO.File]::AppendAllText` + `UTF8Encoding($false)`;追加不会碰文件已有的 BOM),或者改完**逐字节验首三字节**;2凡是被编辑工具动过的文件,收尾时都要回头对一遍 BOM 与 EOL —— 本轮把 8 个改动文件(3 个源码 + 1 个测试 + 1 个脚本 + 3 个文档)逐个与 HEAD 比过,确认一个都没掉;3`.ps1` 的编码风格别动:`tools/*.ps1` 保留 BOM,`_cmp/*.ps1`(辅助脚本,盒子/pwsh 两边跑)**一律纯 ASCII** —— 它们本来就没有 BOM,加中文反而会踩同一个坑。
- **验收**:`tools/package.ps1` 追加两行提示后,`ParseFile` 在 PS 5.1 默认解码下 **0 错**(UTF-8 显式解码也 0 错),`powershell -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1 -SkipWin -SkipLinux` **exit 0** 且三行收尾提示都正常打印(中文不乱码 ⇒ 确实是按 UTF-8 解码的)。
- **教训**:「文件编码」这件事在 Windows PowerShell 5.1 上不是风格问题而是**可执行性问题**;而**编辑工具的静默规范化**(掉 BOM、可能还有 EOL)是这类问题的放大器 —— 结论不是「别用编辑工具」,而是「改完必须用字节级断言回查」,和文档追加时验 EOL 是同一套纪律。

### D-54 · `TaskStore` 的队列必须「锁 + 快照」:UI 线程遍历撞上工作线程增删就是 `Collection was modified`(R21b,2026-11-13,主 agent 在验收里抓到并修)

- **事实**:R21b 最终验收把 `dotnet test` 连跑两遍,第二遍炸了 —— 失败的是一条看起来完全无关的用例(`MultiPickImportTests.Catalog_ImportLocal_TwoFiles_ImportsEach`),异常 `System.InvalidOperationException: Collection was modified; enumeration operation may not execute`,栈是 `TaskStore.RunningCount` ← `TasksPage.Sync` ← `TaskStore.Handle` 的 `Emit` 回调。第一遍是绿的 ⇒ **概率命中的真竞态**,不是测试自身的毛病。
- **根因**:`TaskStore._rows` 是普通 `List<TaskRow>`,而它**同时被两条线程碰**:`Emit` 的设计本身说明「任务工作体跑在线程池上」(下载 / 安装 / AI 都在后台线程上 `Handle`),`Add` / `ClearFinished` / `Remove` 就地增删;UI 线程则在 `RunningCount` / `Rows` / `Newest` / `Get` 里遍历(任务角标、任务页、启动页任务卡都读)。`List<T>` 的枚举器只在「结构未变」时有效,撞上就抛。
- **这是参考版同款的缺陷**:Windows `Services/TaskStore.cs:9/13/15/17/27` 是**同一份无锁写法**(`Rows => _rows`、`RunningCount => _rows.Count(...)`),所以不是移植引入的回归;Linux 侧先修,参考仓库保持只读(记进 PARITY §6 第 11 行)。
- **修法**:`TaskStore` 加一把私有锁 `_gate`,`Rows` / `RunningCount` / `Newest` / `Get` / `Add` / `ClearFinished` / `Remove` 全部收进去;`Rows` 改为返回 **`ToArray()` 快照**(调用方遍历期间不再受增删影响;`.Reverse().ToList()` / `.Count` / `Assert.Contains` 这些既有用法语义不变);`Emit` 一律放在**锁外**(不持锁去碰调度器)。顺带把 `TaskRow.Append` 的 `StringBuilder` 用行内锁护住,新增 `LogText` 快照属性 —— 页面原来是 `row.Log.ToString()`(UI 线程)撞工作线程的 `Append`,同一类问题、更隐蔽(`TasksPage` 两处改走 `LogText`)。行内标量(`Status` / `Speed` / `Progress` / `Finished`)不动:字符串引用与 `double` 在 x64 上写入原子,读到的只是「旧值或新值」。
- **红/绿证据(单变量)**:新增 `TaskStoreTests.Concurrent_Mutations_And_Reads_Do_Not_Throw`(4 工作线程 × 120 轮 `task_added → progress → log → finished → Remove`,UI 线程同时循环读角标 / 快照 / `Newest` / `Get` / 每行 `LogText`)。把 `TaskStore.cs` **单独还原**到改动前(只补一个 `LogText` 转发属性让它能编译):**3/3 红**,全是 `Collection was modified`,栈顶落在 `TaskStore.Get` ← `Handle`;装回加锁版:**3/3 绿**(每次约 110 ms);全量 `dotnet test` **连跑 3 遍全绿** 404 = 403 通过 + 1 跳过 + 0 失败(上一轮基线 403 = 402 + 1)。
- **踩到的第二个坑(记下来免得再上当)**:`Copy-Item` 会**继承源文件的 LastWriteTime** —— 把备份拷回来得到的文件可能比上一次构建还旧,MSBuild 判定「最新」直接跳过编译,`dotnet test` 于是仍在跑旧 DLL(症状:加锁版还报同一处 `FirstOrDefault`,而该行源码早已不存在,差点让我把「红」当成「绿跑不过」)。规矩:改完源码把 LastWriteTime 置为当前时间(或 `-t:Rebuild`)再验;**判断在跑哪份代码要看异常栈的行号/符号,不能只看命令成功**。
- **教训**:「UI 线程读、后台线程写」的进程内共享集合,必须是**锁 + 快照**两件套;测试里出现一次这类异常别当「偶发」放过 —— 它离产品里的崩溃只差一次真的下载任务。另外:**一次绿不等于稳**,连续两遍/三遍才看得出概率性缺陷。

### D-55 · 没被执行过的分支就是没验证过的分支:`package.sh` 的 linuxdeploy 分支自 M3-C2 起就是坏的(R21b,2026-11-13,主 agent 在盒子上真跑一次才发现)

- **事实**:`tools/package.sh` 从 M3-C2 写下来起,AppImage 那一段(`linuxdeploy --appdir dist/TipCL.AppDir --desktop-file=tipcl.desktop --icon-file=tipcl.png --output appimage`)**从来没有被真正执行过** —— 本地不是 Linux、盒子上没有 dotnet,所以一直只有「注释里那串手工命令」在真机上被用过。R21b 收尾时给脚本加了 `--appimage-only`(跳过 publish,用现成的单文件组装),第一次在盒子上真跑:
  `linuxdeploy` 一路走到最后一步,然后 `ERROR: No such file or directory: tipcl.png`,**exit 1**。
- **根因**:脚本是 `(cd dist && "$LD_BIN" …)` 之后才调 linuxdeploy 的,而 `tipcl.desktop` / `tipcl.png` 被放在 `dist/TipCL.AppDir/` 里 —— 相对 `dist` 根本不存在。手工那份命令之所以能成,是因为它是从 AppDir 的**父目录**跑、并且写的是 `TipCL.AppDir/tipcl.desktop`(见 `_cmp/box/r22-assemble.sh`)。
- **修法**:两处调用(直接跑 + extract-and-run 后备)与「未检测到 linuxdeploy」时打印的命令,全部改成 `TipCL.AppDir/tipcl.desktop` / `TipCL.AppDir/tipcl.png`(注释里的出包命令同步改成 `dist/TipCL.AppDir/...`)。
- **验收**:盒子上 `bash tools/package.sh --appimage-only` → **exit 0**、`linuxdeploy` 末尾 `Success`、产物 `dist/TipCL-linux-x64.AppImage` = **38,967,800 B**(与归档件同尺寸)、随后裸 FUSE `--smoke` **exit 0** / `smoke.png` 574,566 B / **0 WARN 0 ERROR**;本地用假 linuxdeploy 把四条路径(直接成功 / 失败→extract-and-run / 缺单文件 / 坏参数)也各跑了一遍。
- **教训**:1**文档里、脚本里写下的命令,必须至少照着跑通一次** —— 「代码写了」和「命令可执行」是两件事,而这类缺陷只会以「照抄命令能成、跑脚本不成」的形式藏在缝隙里;2**给一条从没跑过的分支加一个「我能跑它」的入口(这里是 `--appimage-only`),本身就是一种验证手段**;3反过来,凡是「只在某个环境里才走得到」的分支,记得把它变成**在任何环境都能被跑到**的形状(拆参数、分离 publish 与组装),否则它永远是「看起来有」。



### D-56 · 行内码底色与链接悬停态:「Avalonia 的 `Run` 没有 `Background`」这句前提是**错的**,以抓帧为准(R21c,2026-11-13,子代理改、主 agent 复核 + 红绿复跑)

- **被推翻的前提**:P5 的代码注释与 PARITY §9.5、D-22 第 2 条都写着「Avalonia 的行内不支持背景色(`Run` 无 `Background`)」,据此把行内码退成「等宽 + 主题色」。收口这条欠账时实测:**Avalonia 11.3.2 的 `TextElement` 有 `BackgroundProperty`**(`Run` 是 `Inline` → `TextElement`),**而且真会被画出来** —— 无头 Skia 抓帧数像素,改动前整帧里 `B.Hover` 像素 **0** 个、挂上绑定后 **100+** 个。参考版 WPF 本来就是这么做的(`Pages/Ai/MdView.cs:340`:`SetResourceReference(TextElement.BackgroundProperty, "B.Hover")`),所以最终实现与参考版是**同一个属性、同一个键**。
- **为什么不用 `InlineUIContainer` 包 Border**(那条路能做出圆角药丸,子代理先做通了才换掉):`InlineUIContainer` 里的控件被 `TextLayout` 按**无限宽** measure、再按 natural 尺寸 arrange,320 宽段落里 80 字符的长标识符外壳宽到 **536 px 并被裁掉**,而且它不参与折行。`Run` 的底色只画、不参与布局 ⇒ 段落行距与折行行为与改动前**逐位相同**(用例:`InlineCode_BackgroundDoesNotAffectLayout`:摘掉底色后 `DesiredSize` 一字不差、行距仍是 22、长行内码不把段落撑宽)。
- **链接悬停态**:参考版用 WPF `Hyperlink` 自带 MouseOver 变色,这边手挂 `PointerEntered` / `PointerExited`,在 `B.AccentText` ↔ `B.Accent` 之间换前景色(两套主题下都有变化、都还留可读对比)。**关键细节**:用「重新挂 `Res.Bind(...)`」而不是直接赋画刷 —— 实测重挂会顶掉旧绑定,因此离开悬停时能干净回到主题色,而不会把浅色画刷钉在深色主题里。R17 的三条行为(手型光标 / URL tooltip / 点击开浏览器)未动,并有用例锁住。
- **红/绿证据(主 agent 独立复跑)**:把 `run[!TextElement.BackgroundProperty] = Res.Bind("B.Hover");` 这一行摘掉 → `--filter ~MdView` **3 失败 / 7 通过**(两条 `Assert.NotNull() Failure: Value is null` + 像素用例「只数到 0 个 #F2F2F2 像素」);装回 → **10/10 通过**。全量 `dotnet test` **连跑 2 遍:409 = 408 通过 + 1 跳过 + 0 失败**(上一轮基线 404);Release 构建 0 CS/CA(只 2 条 NU1900 镜像噪声);`SMOKE_PAGE=ai` 冒烟 exit 0 / 0 WARN 0 ERROR。
- **教训**:1**「平台不支持」这类结论必须落到可复核的取证上** —— 这里的正确取证是「抓帧数像素」,而它当场把一句流传了两轮的前提推翻;写进文档的平台限制尤其要标清「当时是怎么验的」,否则它会变成一个不再被质疑的借口(D-22 第 1 条「链接不可点」当年也是同一个毛病的反面:那条是**被推翻后落实了**,这条是**从一开始就错了**);2**视觉改动的验收要钉住「不动布局」这一侧**,否则「看起来更好了」可能在别处把折行改坏;3同一族功能(行内码 / 链接)用**同一条取证套路**(抓帧 + 主题键断言 + 布局不变断言)比逐个手工点更早发现问题。

### D-57 · 权限档位要「告知模型」:补上 `PermissionNote`,并且只补告知这一面(R21d,2026-11-13,子代理改、主 agent 复核)

- **事实**:参考版会把当前档位写成一句话注入系统消息(`Core/Launcher/Ai/Permission.cs:256-276` 的 6 句;消费点 `Agent.cs:348` / `:368`),而 Linux 侧从 R19 起只做了「裁决 + 理由 + 拒绝文案」那一面 —— 模型**不知道**自己处在哪个档位。缺这一面会把模型推向两种坏行为:该直接动手时反复问、被拒之后换个工具名重试同一件事。
- **做法**:1把档位归一化从 `ToolRunner.Mode` 里**原样提出**成 `public static string NormalizeMode(object?)`,裁决 / 提示 / 自检共用一份(**不造第二个真源** —— 两个真源一定会漂);2`PermissionNote(mode, dontAsk)` 用 `switch` 与参考版 6 分支逐字对位,核对方式是**从参考仓源码正则抽字面量再比 SHA256**(6/6 相等),**不是**照着渲染出来的文本抄(读工具会把全角标点显示成半角,照抄必错);3插入点 = `AiAgent.SystemMessage` 里「运行时上下文之后、长期记忆之前」(参考版是三条独立 system 消息,Linux 侧向来合并成一条,位置对位);`AiFixView` 的崩溃修复会话抽出 `FixSystemMessage` 同位置注入(抽成方法也是为了让「位置」可测)。
- **只补告知、不动裁决**:`Decide` 的裁决、理由文案、拒绝文案一律未动(D-48 冻结),并专门有一条用例锁住「同档位下裁决与本轮之前一致」。
- **两处如实登记的偏离**:1「不询问」在 Linux 是**独立开关**而非档位名(D-48),所以那句 dontAsk 只在「开关打开 && 档位不是 `yolo` / `plan`」时使用,其余档位保持原句;2**别名档位只影响提示文本、不影响裁决** —— 参考版的归一化结果**也用于裁决**,而 Linux 的裁决只认本地五档,别名走默认分支 = 每次问 ⇒ 只有手改配置或 Windows 迁来的配置才会「提示与裁决不一致」。修它必须让 Linux 裁决也认别名 = **改裁决**,没批之前不做(已记进 PARITY §9.4)。
- **附带保留的口径**:`ai_confirm_writes` 刻意不读(裁决不看它,读了就会与真实裁决对着说);子代理路径(`AiAgent.cs:404` 强制 plan)读到的是用户配置档位而不是它自己的 plan 档 —— 与参考版同源(读 settings),未改。
- **验收(主 agent 独立复跑)**:6 句 6/6 SHA256 与参考版相等;全量 `dotnet test` **416 = 415 通过 + 1 跳过 + 0 失败**(基线 409);`--ai-selftest` **54 = 52 + 2** exit 0;Release 构建 0 CS/CA(仅 2 条 NU1900 镜像噪声);`SMOKE_PAGE=ai` 冒烟 exit 0 / 0 WARN 0 ERROR;五个改动文件均 UTF-8 **无 BOM / LF**。
- **教训**:1**「模型可见文本」的对位必须用脚本抽字面量比哈希** —— 这是唯一能保证「逐字」的办法;2同一份语义只能有一个归一化入口,把它提出来给裁决与提示共用,是「告知」与「执行」不漂的前提;3「只补告知、不动裁决」这种边界要**用一条锁裁决的用例**守着,而不是写在注释里。

### D-58 · 裁决与「告知」必须同源同序:补别名档位、并把「只看不动」挪到规则之前(R21e,2026-11-13,子代理改、主 agent 复核与补刀)

- **背景**:R21d 补上 `PermissionNote` 后暴露出两处「告知」与「裁决」不一致:1参考版 `PermissionMode.Values` 的 10 档里,Linux 裁决只认 `yolo` / `plan` / `acceptEdits` 三档,`auto` / `autoEdit` / `build` / `edit` / `bypassPermissions` / `dontAsk` 全落默认分支(每次问)⇒ 手改 config 或从 Windows 迁来的配置会出现「提示说会直接执行、实际每次弹确认」(用户批准后本轮拉平);2**参考版把 plan 判断放在规则之前**(`Permission.cs:186-190`:`if (mode == "plan" && !readonly) return deny`),Linux 把规则放在前面 ⇒ **一条 allow 规则能把「只看不动」压过去**。
- **做法**:1`Decide` 的档位 switch 按参考版逐条对位(`yolo`/`bypassPermissions`→allow「免确认模式」;`auto`/`acceptEdits`/`autoEdit`→除 `delete` 外 allow;`build`→除 `delete`/`launch` 外 allow;`edit`→只放行 `write_local`;`dontAsk`→deny),理由文案与参考版**逐字**相同(核对方式:脚本从两仓源码抽 `(裁决, 理由)` 字面量做集合比对,Linux 16 条全部能在参考版里找到);2在 `DecideRaw` 开头加「plan 硬停」(非只读直接拒),switch 里的 plan 分支保留为兜底;3`McpVerdict`(Linux 独有的一支)把 `bypassPermissions` 与 `yolo` 同等放行,并注明这是 Linux 自定。
- **没动的**:`RuleFor` 与 `readonly` 两步的相对位置(参考版规则在 readonly 之后、Linux 在之前,实测两者对最终裁决无差别)、`ApplyDontAsk` 独立开关、`PermissionNote` 6 句、面板可选档位集合(仍 5 档)。
- **验收**:别名用例 **红 8/10 → 绿 10/10**;plan 硬停用例(我加的)在主 agent 手上**红 1/1 → 绿**,全量 `dotnet test` 连跑 2 次 **431 = 430 通过 + 1 跳过 + 0 失败**;Release 构建 0 警告 0 错误;`--ai-selftest` 54 项 exit 0。
- **教训(这一轮真踩了)**:改「模型可见/用户可见的文案」时,**必须从源文件的字节里取那句话**,不能凭 `read` 看到的字符照打 —— `read` 会把全角标点渲染成半角,肉眼一模一样、`Assert.Equal` 却报「Expected 与 Actual 看起来相同」。我插的那句 `plan` 理由就是手打成了半角逗号,被既有用例当场抓住(同理,子代理的测试期望也先红过一次)。已记规矩:**文案常量只有一处真源,别处一律引过去**。

### D-59 · 静默 `catch` 是缺陷放大器:一个字段类型错掉,整块功能无声消失(R21e,2026-11-13,真机验证发现,子代理修、主 agent 复核)

- **事实**:真机上「下载页 → 数据包 → 装进存档」的下拉**永远打不开**,而数据包本身能装(只是不进存档)。链路四步:`list_saves` 的 `mtime` 是 **JSON 数字**(Core `LocalFs.MTimeInt` → `long`)→ `CatalogModels.SaveRow.Mtime` 声明成 **string** → `Api.Coerce`(序列化再反序列化)抛 `JsonException` → **被一条裸 `catch { return null; }` 吞掉** → `TryCallAsync` 回落空列表 → `CatalogPage` 的 `if (saves.Count == 0) return extra;` 提前返回。
- **为什么要记一条决策**:1它**在任何平台都可能存在**,却因为静默而只在真机上「点一下」才露出来(页面表现得像「这台实例没有存档」);2日志里**一行都没有**:`TryCallAsync` 自己那条 WARN 从来没机会跑,因为 `Coerce` 先把异常吃掉了 —— 这是**可观测性缺口**,不是单个字段的手误;3这是 PARITY §6 第 1/2 行同一类(「POCO 形状与后端返回不一致 → 界面静默空掉」)的第三例,说明这类缺陷值得一条**通用**处置。
- **处置**:1`SaveRow.Mtime` 改成与真实返回一致的**数字**类型(数字字符串也能接住 —— `JsonOpts` 开了 `AllowReadingFromString`,但那只解决「字符串→数字」,**不解决「数字→字符串」**,这正是原缺陷的成因);2`Coerce` 的裸 `catch` 改成记录 `AppLog.Warn($"API 结果转换失败({target.FullName}): {类型}: {消息}")` —— 以后同类失败一眼能定位到字段(`JsonException` 会直接点名 `$[0].mtime`);3新增 5 条用例:钉 Core 契约(`mtime` 是数字)、`TryCallAsync<List<SaveRow>>("list_saves")` 必须非空、`CallAsync` 抛异常那条路、下拉判定那步的非空分支、以及「转换失败必须留日志」。
- **红/绿**:加日志但**不改**字段类型时 `--filter ~CatalogSavesCoerceTests` **红 3/5**(失败信息里带 `JsonException ... Path: $[0].mtime`);改类型后 **5/5 绿**。
- **教训**:1**「转换失败一律静默」是一条比任何单个字段都值钱的缺陷源** —— 页面拿到默认值,用户看到空白,开发者看不到日志,三者都不知道彼此存在;2POCO 与后端返回的**形状**要有契约用例钉着(`mtime` 是数字这件事以前没有测试守),这类「形状漂移」在移植项目里尤其容易发生(照参考版的 POCO 抄,而参考版的后端与本地后端未必同形);3**真机点一下**的价值在这一轮被反复证明:两条 P0(R21b)、这条下拉、以及 GTK/managed chooser 的误判,全都是「无头全绿但真机不对」。
