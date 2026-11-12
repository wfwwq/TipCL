# M4-H R9:版本设置齿轮崩溃(缺陷 A)+ 版本目录不一致(缺陷 B)+ 右键菜单(缺陷 C)

- 轮次:M4-H R9(第十棒,接第九棒遗留的临时复现台)
- 环境:开发机 Windows(无头 Avalonia + 真布局 pass),真机证据来自 R8 的 Ubuntu 24.04 / X11 / XFCE 盒子 `main`
- 范围:只改 `src/TipCL.App/**` + 新增测试 + 本报告;未 commit / 未 push / 未动版本号 / 未碰 Windows 参考仓库;
  未改 `PROGRESS.md` / `DECISIONS.md` / `docs/PARITY.md`(留给主 agent 统一合并)

---

## 1. 缺陷 A:点版本行齿轮把应用打崩 —— 根因

**崩溃点不是「版本设置」对话框,是「实例详情」页的首次布局。**

现场异常(盒子 `~/.tipcl/logs/tipcl-20261112.log` 第 4-5 行 / 157-158 行):

```
[ERROR] unhandled(UI)
[ERROR] System.InvalidOperationException: The control StackPanel already has a visual parent Border
        while trying to add it as a child of ScrollContentPresenter (Name = PART_ContentPresenter, Host = ScrollViewer)
```

**根因**:`src/TipCL.App/Pages/Library/InstanceDetailPage.cs` 构造函数里遗留的

```csharp
var card = UiClass.Card(_rows, 0);   // 这个 Border 从来没有被加进可视树
card.Padding = ...; card.Margin = ...; card.VerticalAlignment = ...;
```

这个孤儿 `Border` 把 `_rows`(页面里唯一那个行 StackPanel)收成了自己的子级,于是
`_rows.VisualParent == 那个 Border`;而 `ShowSeg()` 又把**同一个** `_rows` 赋给了
`_host.Content = new ScrollViewer { Content = _rows }` —— 一个控件两个父级。

Avalonia 报错的时机很隐蔽:**给一个还没进可视树的 `ScrollViewer` 赋 `Content` 不会当场报错**
(那时它的模板还没 ApplyTemplate,`ScrollContentPresenter` 还不存在)。等到这个 ScrollViewer
第一次 `Measure` → `TemplatedControl.ApplyTemplate()` → `ScrollContentPresenter.OnAttachedToVisualTree`
→ `AttachToScrollViewer()` 才把 `Content` 绑上去 → `ContentPresenter.UpdateChild` →
`AvaloniaList<Visual>.Add(_rows)` → 校验「已有可视父级」失败 → 抛。异常从 `MediaContext.Render()`
的布局回调用出来,跑到 `Dispatcher` 就是 `unhandled(UI)` → **进程直接退出**。所以真机上表现为
「点了某个按钮之后进程突然没了」,而不是弹一个错误框。

**真机触发路径**(这解释了为什么第九棒的复现台点「版本管理」页的齿轮怎么点都不崩):

| 页面 | 那枚 ToolTip 为「版本设置」的齿轮点下去做什么 | 会不会崩 |
| --- | --- | --- |
| 实例库 `LibraryPage` | `LibraryPage.cs:537` → `OpenDetailAsync(id)` → `Content = new InstanceDetailPage(...)` | **崩**(构造详情页 + 首次布局) |
| 版本管理 `VersionManagePage` | `VersionManagePage.cs:406` → `VersionSetupDialog.ShowAsync` | 不崩 |

### 为什么无头测试以前抓不到(两件事必须同时成立)

1. **必须真的跑一次布局 pass。** 无头会话不会自己走渲染计时器,`await Task.Delay(...)` 不触发
   `Measure`,模板永远不 ApplyTemplate,那个非法父级就一直潜伏着 —— 这也正是「宿主机上怎么点都没事、
   真机一布局就炸」的原因。补上这一步:`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` +
   `Dispatcher.UIThread.RunJobs()` + `TopLevel.CaptureRenderedFrame()`(冒烟截图用的同一条渲染路径)。
2. **必须走到那条路径。** 复现台直接 `MainWindow.Navigate("instance")` 点的是版本管理页,
   压根没构造 `InstanceDetailPage`;真机上崩的那一页是实例库(`LibraryPage`)。

### 证据(三条,互相独立)

1. **栈逐帧一致。** 把修好后的详情页首次布局在无头里跑出来,异常栈与盒子日志自
   `AvaloniaList.Add` 起逐帧相同:`ContentPresenter.UpdateChild ← ContentPresenter.ContentChanged ←
   ScrollContentPresenter.AttachToScrollViewer ← … ← TemplatedControl.ApplyTemplate ←
   Layoutable.MeasureCore ← ContentPresenter.MeasureOverride ← Grid.MeasureCell ← Grid.MeasureOverride ←
   ContentPresenter.MeasureOverride ← … ← LayoutManager.Measure`(那两层 `ContentPresenter` + `Grid`
   正是 `_host`(ContentControl)与 `shell`(Grid)的结构,版本管理页的 ScrollViewer 结构不是这样)。
2. **负向对照。** 把那一行 `UiClass.Card(_rows, 0)` 注回修好的代码,`InstanceDetailCrashTests` **5/5 全红**
   (报的就是上面那条异常),删掉即 **5/5 全绿**。测试是真能红的。
3. **页面身份。** R8 截图 `_cmp/crop-row.png`(在右键探测那一刻截的版本行)行副标题是
   「正式版 1.20.1 · 完全独立」——`" · " + IsolationLabel` 这种拼法**只有 `LibraryPage.SubLine` 有**
   (`VersionManagePage.SubLine` 不拼隔离档位,它把隔离做成一个 Tag chip)。所以当时屏幕上就是实例库页,
   与上面的路径表一致。

---

## 2. 缺陷 A:修法

`src/TipCL.App/Pages/Library/InstanceDetailPage.cs`(根因级,不吞异常、不靠延时):

1. **删掉那个孤儿 Border** —— 它不在可视树里,渲染上从来没有生效(它那三行 Padding/Margin 是死代码);
2. **让 `_rows` 的容器变成唯一且只挂一次**:新增字段
   ```csharp
   private readonly ScrollViewer _rowsHost = new() { HorizontalScrollBarVisibility = ..., Margin = new Thickness(22, 0, 22, 0) };
   ```
   构造期 `_rowsHost.Content = _rows; _host.Content = _rowsHost;`,`ShowSeg(k)` 只切分段状态与数据。
   这同时堵住**第二发**(同一族缺陷):以前每次切分段都会 `new` 一个 ScrollViewer 去装同一个 `_rows`,
   换下来的旧 ScrollViewer 的 `ScrollContentPresenter` 仍然持有 `_rows`,
   第二次切换就是下一个「already has a visual parent」。

## 3. 缺陷 A:正式回归测试

`tests/TipCL.Tests/App/InstanceDetailCrashTests.cs`(5 条;自带 `Window` 承载页面,自己做布局驱动,
断言 `Dispatcher.UIThread.UnhandledException` 捕获到的异常列表为空):

| 用例 | 覆盖 |
| --- | --- |
| `InstanceDetail_FirstLayout_DoesNotCrash` | 崩溃现场本身(还断言行容器真的被布局过,防止「没布局所以没崩」的假绿) |
| `InstanceDetail_OpenBackOpenAgain_DoesNotCrash` | 打开 → 关闭 → 再打开 ×3,每次都重新布局 |
| `InstanceDetail_SwitchEverySegment_DoesNotCrash` | 五个分段来回切(同一族缺陷的第二发) |
| `InstanceDetail_RowHost_HasExactlyOneParent` | 结构断言:`_rows` 只被一个 ScrollViewer 认领,父级就是它模板里的 ScrollContentPresenter |
| `LibraryGear_OpensDetail_FirstLayout_DoesNotCrash` | **真机路径**:实例库版本行齿轮 → `OpenDetailAsync` → 详情页首次布局 |

临时复现台 `ScratchReproTests.cs` 与 `scratch-out.txt` 已删除;`_cmp/r9-negative-control.log` 留作负向对照证据。

---

## 4. 缺陷 B:界面列出的版本 ≠ 启动预检看的目录 —— 同一个语义被算成两个路径

**语义核对**(结论:**是同一语义走岔了,不是两种不同的版本集合**,因此修调用点,不动语义):

- Core 的 `Instance(name)`:`name` 为空 → 落到 `Instances.RootName()`(即 `<TIPCL_HOME>/.minecraft`),
  所以 `get_version_rows` **不传 `instance`** 拿到的是**根实例**的 versions;
- 页面自己的兜底扫描用的是实例目录:`LibraryPage.ScanLocalVersionsAsync(_folder)`、
  `InstanceDetailPage.LoadVersionsAsync` 里的 `new Instance(_inst).InstalledIds()`;
- 版本行的**操作**也用实例目录:`uninstall_version` / `rename_version` / `copy_version` / `hide_version`
  传的都是 `instance = _folder` / `_inst`;
- CLI 启动预检同样只看实例目录(`.../<实例>/versions`)。

只有 `get_version_rows` 的三个调用点漏了 `instance` → 界面列全局目录、操作与预检看实例目录,
于是「界面上看得见的版本,启动时报版本未安装」(R8 真机原样复现:`1.20.1` 在界面里,
`26.3/versions` 里没有它;而该实例自己的 `26.3` 不在界面里)。

**修**(3 个调用点,都补上 instance):

- `Pages/Library/LibraryPage.cs` `ReloadVersionsAsync` → `new { instance = _folder, include_hidden = false }`
- `Pages/Version/VersionManagePage.cs` `ReloadVersionsAsync` → `new { instance = _folder, include_hidden = true }`
- `Pages/Library/InstanceDetailPage.cs` `LoadVersionsAsync` → `new { instance = _inst, include_hidden = false }`

**测试**:`tests/TipCL.Tests/App/VersionListScopeTests.cs`
`GetVersionRows_IsInstanceScoped` —— 锁死不传 `instance` = 根实例、传 `instance` = 实例目录,
并断言 `new Instance(_inst).InstalledIds()`(预检那条路)看到的就是同一份。

**如实说明(未做完的部分)**:我先写了 3 条**页面级**断言(断言页面上出现实例的版本、不出现根实例的版本),
在全量套件里**不稳定**:所有测试类共享同一个无头 UI 会话,页面数据是异步灌的,
同一份夹具单独跑必过、全量跑等 15s 仍可能一行都没有(实测日志里页面文本只有标题栏那一层)。
为了让 `dotnet test` 稳定 0 失败,我把它们删了,而不是改成放宽断言 —— 缺陷 B 由上面那条 API 语义测试
＋ 三处调用点改动共同锁定;页面级渲染要在真机上补验更合适。

## 5. 缺陷 C:版本行右键菜单在真机打不开 —— **不是注入伪象,是该页本来就没挂菜单**

依据:

1. **坐标确实命中该行**:同一坐标左键双击切到了启动页(命中行、悬停高亮也生效);
2. **实例库页的版本行没有任何右键接线**:`LibraryPage` 里 `ContextMenuFx` / `PointerReleased` /
   `ContextMenu =` **0 命中**(grep 实测),版本行只有 `PointerEntered/Exited` 悬停 + 收藏/卸载/设置三个按钮;
   右键菜单只挂在 `VersionManagePage.BuildVerRow` 的
   `rowCard.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right …) VerMenu(...) }`;
3. **实例卡「•••」能弹不能当反证**:那是个**左键按钮**点击后调 `ContextMenuFx.Show(...)`,
   与「右键注入是否到达控件」不是同一个挂载点。

所以「注入 button-3 不弹菜单」与代码行为一致 —— 不是 XTest 伪象;**但这也是产品面缺陷**:
同一个应用里两个页面的版本行行为不一致(实例库缺右键菜单)。
**未改行为**(给实例库补菜单属产品决策,且不在本次「修真机崩溃」的范围)。

## 6. 同类写法清单(全仓扫描)

扫描口径:同一文件内,某个标识符既作为 `ScrollViewer.Content`,又作为 `Border` 的子
(`.Child = x` / `Card(x)` / `Pane(x)` / `RowCard(x)`)——即「一个面板两个容器」。

- **真问题(已修)**:`InstanceDetailPage._rows`(唯一命中)。
- **潜在同类(现在只调用一次,重复调用就会崩)**:`PluginsPage.BuildMarketChrome()`
  (`_marketHost`、`_search` 被塞进一个新的 `U.V(...)` 容器)、`PluginsPage.BuildDev()`
  (`_providerHost`、`_logBox`、`_logPick`)。两者都由构造函数各调一次兜着(实测调用点只有
  `PluginsPage.cs:110-111`),暂未发作;要防的是以后有人在刷新路径里再调一次。
- **同族提醒(不属于上面口径,但同一类错)**:「每次 `new` 一个容器去装同一个常驻面板」——
  旧容器的 presenter 仍然持有那个面板。本次已在 `InstanceDetailPage` 堵住;
  `PluginHostPage.Build()` 每次新建 `body`,安全。

---

## 7. 验收与遗留

- `dotnet build` Debug / Release:**0 错误、0 代码警告**(只剩 8 条 `NU1900` NuGet 镜像联网噪声)。
- `dotnet test`(带 `DotNetHostPath`):**329 通过 / 1 跳过 / 0 失败**(基线 323 + 新增 6)。
- 相关页冒烟:`--smoke` 跑 `library` / `instance` **exit=0 且出图**(44,470 B / 41,712 B),
  应用日志 0 WARN / 0 ERROR;冒烟用隔离 `TIPCL_HOME`(`%TEMP%`),真实
  `%LOCALAPPDATA%\TipCL\config.json` 修改时间仍为 `2026-11-08 17:13:58`。
- 工作区:`scratch-out.txt`、`ScratchReproTests.cs` 已删;证据统一在 `_cmp/`(gitignore)。

**遗留**:

1. **未再上盒子复跑**。归因依据是本机无头逐帧对齐真机栈 + 负向对照 + R8 截图;要在真机确认,
   跑「实例库 → 版本行「版本设置」齿轮」看进程是否存活即可(用完 `sleep main`)。
2. 缺陷 B 的**页面级**渲染断言被删(原因见 §4),要补得先解决「全量套件下页面异步数据 + 共享无头会话」的
   时序问题(例如给页面测试专用的会话/串行进程)。
3. 「版本管理」页齿轮 → 版本设置对话框 → 关闭 → 再打开那条交互用例也因同样的时序问题没有留下
   (第九棒的结论本来就是「这条不崩」,与本次修复无关)。
4. `_cmp/` 仍未进 `.gitignore`(前几轮遗留,本轮未动)。
5. 给下一位的坑:做负向对照(注回缺陷再改回)时,`Copy-Item` 还原会保留备份的**旧时间戳**,
   MSBuild 会认为产物是新的而**跳过重编** → `dotnet test` 跑的还是注了缺陷的旧 dll(本轮踩过:
   源码已修、测试却仍 5 条红)。还原后先 `(Get-Item 文件).LastWriteTime = Get-Date` 再 build。
