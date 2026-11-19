# AUDIT-B · App 层审计报告(C# / Avalonia UI)

- 分支:`audit/B`,基线 main `b86afe3`(`_aud/B` 独立 worktree)
- 审计范围:`src/TipCL.App/**`(布局引擎与编辑器 / 页面生命周期与异步 / AI 页 / 设置与持久化 / 启动与命令行 / 主题与帮助层)
- 范围外(只报告):`src/TipCL.Core/**`、`tests/**`、`windos/**`
- 级别口径:P0 崩溃·启动失败·数据丢失·主流程中断;P1 功能行为错误·逻辑 bug·异步竞态·异常被吞;P2 边界·资源泄漏·提示不当·i18n·无障碍
- 构建基线:`dotnet build src/TipCL.App/TipCL.App.csproj -c Release` = 0 错误 0 警告
- 测试基线:`dotnet test --filter "FullyQualifiedName~TipCL.Tests.App"` = 355 通过 / 0 失败

---

## 1. 问题表(已修)

| ID | 级别 | 文件:行 | 根因 | 修复 | 验证证据 | 状态 |
|----|------|---------|------|------|----------|------|
| B-01 | **P0** | `src/TipCL.App/Pages/Ai/AiPage.cs:1016`(Clear)、`:442`(NewChat) | 空态控件 `_empty` 从构造函数起就一直挂在 `_messages.Children` 上,只在 `_bubbles` 列表里被移除时不会被摘;`Clear()` / `NewChat()` 又无条件 `_messages.Children.Add(_empty)`。Avalonia 11 对「已有 visual parent 的控件再加进同一个 Panel」**抛 InvalidOperationException**(实测见 §4-B);`Program.cs:52-56` 把 UI 线程未处理异常记日志后 `e.Handled=false`,进程直接退出 | 新增 `ShowEmptyState()`:先判 `_messages.Children.Contains(_empty)` 再放,两处调用改成它 | ① 探针程序(引用 TipCL.App 的 net8 控制台)实测:`ADD-TWICE-THREW InvalidOperationException: The control TextBlock (Text = x) already has a visual parent StackPanel while trying to add it as a child of StackPanel.`;② 修复后 `dotnet build` 0 错误、355 条 App 测试全绿 | 已修 |
| B-02 | **P1** | `src/TipCL.App/Pages/Launch/LaunchPage.cs:976`(原 `LaunchAsync`) | 「启动游戏」按钮在**第一个 await(账号闸门 `AccountGate.EnsureAsync`)之后**才 `SetLaunchMode(running:true)` 收起按钮;闸门内部走 `InProcessApi.TryCallAsync` → `Task.Run`(`Api.cs:122-126`)是真异步,期间按钮仍可点。连点两次即两次 `StartTaskAsync("启动游戏")`,而 `StartTaskAsync` 是**分离任务**(`Api.cs:140-141` `Task.FromResult(StartDetached(...))`)⇒ 两个启动任务并行、两个游戏进程 | 拆成 `LaunchAsync()`(守卫)+ `LaunchCoreAsync()`(原实现):进入时同步占位 `_launching`,`finally` 放开 | `dotnet build` 0 错误;355 条 App 测试全绿(含 LaunchPage 相关用例) | 已修 |
| B-03 | P2 | `src/TipCL.App/Pages/Layout/LayoutMotion.cs:214` | 补间表用 `ReferenceEqualityComparer.Instance` 做键,但落位动画的键是 `("land", id)` 这种**每次新装箱的 ValueTuple** ⇒ `Cancel(("land", id), …)` / 重开同一块时 `Cancel(key,…)` 永远匹配不上。结果:①「同一个 key 再开会先取消旧的」不成立,旧 180ms 补间继续跑;②旧补间的 `Done` 会 `_landing.Remove(id)`,把**新**落位记录删掉 ⇒ 拖完松手的卡片可能停在半路(不回到吸附矩形);③`FinishAnimations()` 里对落位的 `Cancel` 是空操作 | 改用默认比较器(`new Dictionary<object, Entry>()`):Control 没重写 Equals ⇒ 仍按引用;ValueTuple ⇒ 按值 | `dotnet build` 0 错误;355 条测试全绿(LayoutMotion 的纯函数用例不受影响) | 已修 |
| B-04 | P2 | `src/TipCL.App/Pages/Layout/Widgets/PluginBlocks.cs:29,41` | 插件区块外壳表只按 `blockId` 缓存,`Build(blockId, pageId)` 忽略 `pageId`。同一个 `plugin:<plugin>:<blockId>` 出现在两页(手编布局 JSON / 插件写文档)时,两张 `LayoutCard` 拿到**同一个 Shell 控件**;第二张卡 `_body.Content = shell` 时该 Shell 仍挂在第一张卡的 ContentPresenter 里 ⇒ Avalonia 抛 "already has a visual parent"(或第二页空白) | 缓存键改成 `pageId + "\u0001" + blockId`,各页各一份 | `dotnet build` 0 错误;355 条测试全绿 | 已修(防御性) |

修复计数:**P0 = 1,P1 = 1,P2 = 2**,合计 4 条。

---

## 2. 重点审计过的实现(结论:未发现问题,列出关键判定依据)

| 部位 | 结论与依据 |
|------|-----------|
| `LayoutEngine`(排布 / grid / canvas / 引导线 / 变体 / 编辑会话,3162 行) | 纯模型、零 UI,所有入口都对 null / 空 id / 非法数值做了兜底;`ClampInt` / `Clamp` 覆盖 `double.NaN` / `Infinity`(`LayoutSchema.Clamp`);`Occupancy` 的行表惰性增长无越界;`Compact` / `Pack` / `Resize` 的上下限口径与 `Sanitize` 一致 |
| `LayoutEditor`(2955 行)+ `LayoutCard` | 卡片常驻缓存 + `Detach()` 先摘旧父再挂新父(避开「一个控件两个父级」);`_saving` 守卫挡住自触发重排;`_gestureTarget` 指纹避免每帧重排;手势收尾统一走 `EndBoxGesture` 且先清 `_gesture` 再 `Capture(null)`(重入安全);`SetFixedHeight` 的父子搬运顺序正确 |
| `GridCanvasPanel` / `FillPanel` / `FitBox` / `GuideLayer` | 两趟测量(需求高 → 行高 → Arrange)无 `MeasureOverride` 内 setter 死循环;`_boundFillHeight` 只在有限高时启用;`FitBox` 显式把 `RenderTransformOrigin` 钉到左上角(Avalonia 缺省中心,已处理) |
| `LayoutPageState` / `PageLayoutState.Reset` / `FactoryPage` | 「重置本页」保留 custom:/plugin: 块数据(只清几何),不会整页覆盖丢用户数据(与 Windows 6c520ca 同口径) |
| `PageLayoutHost` / `SplitRatioStore` / `SplitCatalog` | 折叠分支的列宽计算三种组合都正确;写比例一律经 `LayoutStoreV2`,失败返回 false 不抛;`SyncLeftMinWidth` 只看 `_leftVisible` 一处不一致,但 `_leftVisible` 只有 `SetLeftVisible` 会改(全树无第二调用点),当前不可达 ⇒ 不计问题 |
| `MdParser`(畸形输入) | 未闭合 ``` / ~~~ 栅栏:内层 for 走到行尾后 `i++` 越界即退出外层,不抛;表格分隔行列数与表头不等:渲染侧 `Align(b,i)` / `Cell` 都有 `i < Count` 兜底;深嵌套 `[a](u)` 不会无限递归(链接文字里不可能再出现 `](`);`FindClose` 遇反引号成对跳过,不会越界 |
| `MdView` / `AiBubble` / `ChatInput` | 流式渲染 80ms 节流(`SetMarkdown(live)` 自停),`Render` 前比对 `_rendered` 去重;解析异常有整段回退;`Send`/`Stop` 是实例属性(不是共享静态),不会串页 |
| `AiPage` 会话循环 | `_run != null` 挡住重复发送;`Stop()` → `FinishRun` 与 `catch(OperationCanceledException)` → `FinishRun` 双次调用是幂等的(第二次 `_run`/`_pending` 已 null);确认卡 / 选择题卡的 `TaskCompletionSource` 都有 `TrySetResult` + `ClearDock` 兜底,切页 `OnDeactivate` 会收尾 |
| `TaskStore` / `FileDrop` / `Dlg` / `ToastHost` | `TaskStore` 的 `_rows` 读写全在 `_gate` 内,事件统一回 UI 线程;`FileDrop.Attach` 用 `ConditionalWeakTable` 做幂等;`Dlg` 明确「先 TrySetResult 再 Close」并解释了顺序;Toast 定时器自停、动画收尾吞异常 |
| `LayoutProfiles` / `LayoutJsonEditor` / `SettingsPage.Layout` | 方案应用是「原子替换 layout + 先留底『上一次布局』」;JSON 编辑器先校验后落盘,`kind`/`version` 不符直接拒;布局视图重建用 `_layoutRebuilding` 挡住构造期 `SelectionChanged` 误写 |
| `TitleBar.SetClass` | 用 `Classes.Add/Remove`。实测 Avalonia 的 `Classes` **去重**(重复 Add 只留一份、Remove 一次即清空)⇒ 页签高亮不会残留("on" 双份导致高亮不清的怀疑被证伪) |
| `App.axaml.cs` / `Program.cs` / `CommandLine.cs` | `I18nCore.InitLanguage()` 与 `WidgetRegistry.EnsureRegistered()` 都在 lifetime 判断**之外**(--smoke 也覆盖);CLI 先校验实例存在再走启动链,错误码分 Ok/LaunchFailed/Usage |

## 3. 跨域问题(Core 侧,仅报告,未改)

| ID | 级别 | 位置 | 说明 |
|----|------|------|------|
| X-01 | 文档漂移(P3) | `src/TipCL.App/Pages/Layout/WidgetRegistry.cs:17`(App 侧注释) | 注释写「Linux 侧目前没有任何生产代码调 EnsureRegistered()」,实际 `App.axaml.cs:34` 与 `SmokeRunner.cs:66` 都已调用 ⇒ 建议把这句注释删掉(在 App 侧,但不在本轮修复取舍内,避免与 Windows 逐行对照件产生 diff) |
| X-02 | 待 Core 侧确认(P2) | `src/TipCL.Core/Launcher/Layout/LayoutStoreV2.LayoutChanged` | 静态事件在 `Save()` 内同步触发,订阅方(BlockHost)必须自己回 UI 线程(已按此实现)。若将来 Core 改成异步派发,App 侧 `BlockHost.OnLayoutStoreChanged` 的 `_ui.CheckAccess()` 判定仍安全,无需改 App |

## 4. 验证与取证

### 4-A 环境(每次新 shell 先执行)
```powershell
$env:PATH="D:\项目\TipCL\_tools\dotnet;$env:PATH"
$env:DOTNET_ROOT="D:\项目\TipCL\_tools\dotnet"          # 不加这句 dotnet test 会报 hostfxr.dll 找不到
$env:DOTNET_CLI_HOME="D:\项目\TipCL\_tools\home-B"
$env:NUGET_PACKAGES="D:\项目\TipCL\_tools\nuget"
$env:TEMP="D:\项目\TipCL\_tools\tmp-B"; $env:TMP=$env:TEMP
$env:TIPCL_HOME="D:\项目\TipCL\_tools\tmp-B\tipcl-home"
```

### 4-B Avalonia 11.3.2 行为探针(放在 `%TEMP%` / `_tools\tmp-B\probe`,未进仓库)
引用 `src/TipCL.App`,验证三件事:

```
ADD-TWICE-THREW  InvalidOperationException: The control TextBlock (Text = x) already has a
                 visual parent StackPanel while trying to add it as a child of StackPanel.
AFTER-REMOVE-CHILD count=1                       # 重复 Add 失败后原有一个仍在
CLASS-ADD-TWICE-OK count=1 has=True              # Classes 去重 ⇒ 高亮不会残留
AFTER-REMOVE count=0 has=False
REPARENT-THREW   InvalidOperationException: ... already has a visual parent ...   # Panel→Panel 换父
```

结论:B-01 / B-04 的「第二次挂同一控件」是真崩溃路径,不是理论推测。

### 4-C 构建与测试
```
dotnet build src/TipCL.App/TipCL.App.csproj -c Release   → 已成功生成,0 个警告,0 个错误
dotnet test tests/TipCL.Tests --filter "~TipCL.Tests.App" → 已通过:355,失败:0,跳过:0
```

### 4-D 无头启动冒烟(修复后)
```
TIPCL_HOME=D:\项目\TipCL\_tools\tmp-B\tipcl-smoke
dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --smoke
→ [INFO] === TipCL 1.2.0 启动 pid=... ===
→ --smoke:已输出 smoke.png        (退出码 0)
```
说明:整窗装配(标题栏 + 布局钩子 + 首个页面 launch)在 Release 下仍能起来;`smoke.png` 被 `.gitignore` 覆盖,工作树保持干净(`git status --porcelain` 为空)。

## 5. 未修 / 剩余未审

- **未修(报告不修的理由)**:无 P0/P1 遗留。上面 4 条全部落地。
- **已知但按口径不修(纯视觉 / 平台差异,不计问题)**:
  - `MdView.BuildTable` 每行用独立内部 Grid ⇒ 表头与各行的列宽各自 auto,长内容行会让列不齐(纯视觉,Windows 用 RichTextBox 表格);
  - `MdView` / `AiBubble` 的流式正文是 `_md.Markdown + delta` 字符串拼接,超长回复是 O(n²)(性能,非正确性);
  - `MainWindow` 构造里 `LibraryPage.NavRequested += …`、`InProcessApi.BridgeEvent += …` 未退订(静态事件攥着窗口)。生产单窗口下无影响,`Closed` 已退掉 `LayoutShellHooks`;若要彻底干净需要 Core 侧提供退订句柄,属于跨域讨论项;
  - `LaunchPage.WireEvents` 用静态 `_eventWired` 只订阅一次 `Api.BridgeEvent`,`_memSaveTimer` / `Motion` 过渡对象在控件生命周期内不释放 —— 页面本身随进程常驻,可接受。
- **剩余未审(时间/优先级取舍)**:
  - `Pages/Download/CatalogPage.cs`(952 行)、`LoaderIcons.cs`、`Pages/Plugins/PluginsPage.*`(4 个分部 ~1000 行)、`Pages/Version/*Dialog.cs`、`Pages/Feedback/*`、`Pages/Settings/PhoneSyncPanel.cs` 只做了 grep 级扫描(`async void` / `catch` / `Task.Run` 点已逐个确认都有 try-catch 或状态机兜底),未逐行读;
  - `Common/Api.cs`(2116 行)只审了调用入口(`CallAsync` / `TryCallAsync` / `StartTaskAsync` 的线程与异常语义)与 `save_settings` / 任务取消路径,方法表其余分支未逐条核对;
  - `Session/SmokeRunner.cs`(824 行,仅冒烟测试用,不在用户主流程)未审;
  - `windos/**` 与 `tests/**` 不在范围。