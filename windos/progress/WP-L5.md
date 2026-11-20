# WP-L5 · 各页改名与默认布局（R34 布局 v3 §5.5）

- 分支 / 工作区：`r34/l5` · `D:\项目\TipCL\_r34\l5\`（基于 `8cad7d0`）
- 执行者：第二次派出的接替子代理。开工时 `git status` 干净、`r34/base..HEAD` 为空，上一任没有留下改动。
- 按 §6.0：**没有启动过 TipCL.exe**（任何参数都没有），没有做界面自动化；只跑了 `dotnet build`、`dotnet test`、git 和文件读写。
- 施工中本机工具掉线过一次（约 18:27–18:32），掉线前已在后台启动的 premium 构建正常跑完，恢复后取回了结果，没有丢东西。

## 1. 做了什么

### 1.1 改名（只改 Title，id 一个没动）

| 文件 | id | 原来 | 现在 |
|---|---|---|---|
| `Pages/MultiplayerPage.cs` | state / room / actions / players | 状态卡 / 房间卡 / 操作区 / 玩家列表 | 联机状态 / 房间 / 快捷操作 / 玩家 |
| `Pages/PlaytimePage.cs` | total / range / list | 累计卡 / 范围卡 / 明细列表 | 累计时长 / 时间范围 / 明细 |
| `Pages/JavaPage.cs` | list | 已检测列表 | 已检测的 Java |
| `Pages/ServersPage.cs` | instance | 实例行 | 实例 |
| `Pages/AccountPage.cs` | list | 账号卡 | 账号列表 |

### 1.2 四页的新默认布局（按 Q4，只在布局文档里**没有**这一页时生效）

新增纯数据类 `PageDefaultLayouts`（放在 `Pages/BlockHost.cs`，不引用 WPF 类型，net8 测试可以直接调用）。四个页面把它作为 `defaultPage` 交给 `BlockHost`，走的是实例库已经在用的同一条路径：`LayoutEditor.LoadFromStore` 里 `page == null && _defaultPage != null` 时才用页面默认。

关于第八轮的「原生默认排版（BlockNativeLayout）」：这四页都**没有**传 `native`（只有 AI 页、版本管理页用），所以文档里没有该页时 `_nativeActive = false`，新默认确实会生效。文档里有该页时完全照文档渲染。这两种情况都有单测，见 §3。

默认都是 12 列网格。各页列制沿用 12 列，效果图里的 6 列只是示意。

| 页 | 默认（id@x,y,宽） | 默认隐藏 |
|---|---|---|
| 联机 `multiplayer` | header@0,0,12 · state@0,1,6 · room@6,1,6 · actions@0,2,12 · players@0,3,8 · nodes@8,3,4 | — |
| 游戏时长 `playtime` | total@0,0,6 · range@6,0,6 · list@0,1,12 | header（概览） |
| Java `java` | install@0,0,6 · defaults@6,0,6 · list@0,1,12 | intro（说明） |
| 启动 `launch`（只管下方内容区） | news@0,0,12 | tasks / playtime / notes / log |

- 可见块写显式坐标，和 `LibraryPage.DefaultLibraryPage` 是同一种写法。
- 默认隐藏的块**只给列跨度、不给坐标**（契约默认矩形 = 自动流），并且排在最后。显示出来时按顺序接在已有块后面、两两并排，日志占整行。单测覆盖了三种组合：「显示任务 + 时长」「再显示便签 + 日志」「只显示日志」。如果给隐藏块写显式坐标，显示时会全挤到第 0 列、竖着排。
- 「概览」「说明」两块的 `BlockSpec.DefaultVisible` 同步改成 `false`，和默认布局里的 `hidden` 一致。单测按源码核对这两处说法相同，否则重置或补齐缺块时会互相打架。

### 1.3 默认隐藏之后，原来放在这些块里的按钮要挪走

- **游戏时长**：「刷新」「清空统计」原来在页头（概览块）右侧。概览默认隐藏后，默认界面上就找不到这两个按钮了。现在挪到「时间范围」卡右侧：`范围 [实例下拉] …… [刷新] [清空统计]`。概览块只剩标题和说明文字。
- **Java**：计数、「刷新」「扫描系统 Java」原来在说明块右侧，现在挪到「已检测的 Java」块顶部。效果图里列表块的头部也是「已检测到 N 个 + 重新扫描」。
- **Java 版本对应关系**：那句话（复用原有 key，没加新 key）放进「实例默认」卡顶部，作为提示文字。说明块保留标题和同一句话，在「添加」里可以找回。

### 1.4 启动页

只改了区块宿主那一段：传入 `PageDefaultLayouts.LaunchContent()`，注释同步更新。英雄区仍然不进布局系统。**编辑态英雄区压暗、锁定提示没有做**，因为要改宿主段以外的代码，还需要编辑器提供进出编辑态的事件，见 §6。

### 1.5 单块页没有右键菜单（Q5）

- `BlockHost.cs`：`specs.Count >= 2` 才挂「自定义布局...」。规则抽成纯函数 `BlockHostRules.OffersLayoutMenu`，注释也改回来了。
- 单块页（`settings:network`、`settings:about`）另外挂一个**空的占位菜单，并用 `ContextMenuService.IsEnabled=false` 禁用**：
  - 壳级全局右键 `LayoutShellHooks.OnRightUp` 看到「自带菜单」（`HasOwnMenu`）就让路，不会替单块页弹「自定义布局...」；
  - 占位菜单自己不会弹出（IsEnabled 不继承，WPF 选目标元素时会跳过它），右键事件照常往外层冒泡；
  - 内容里自带菜单的控件离鼠标更近，不受影响。

  不这么做的话，只改 BlockHost 那一行等于没改：全局右键照样会给单块页弹出菜单。
- 新增 `internal bool BlockHost.OffersLayoutMenu`（不动 WP-U1 冻结的公共 API），留给合并时修 `LayoutShellHooks` 的兜底路径，见 §4。

### 1.6 实例库自带的布局菜单（Q2：保留区块编辑，只整理文案）

- 文件夹栏改成一个固定的勾选项「显示文件夹栏」，勾上 = 正在显示。原来标题在「隐藏 / 显示」之间来回变，勾选状态又是反着标的，隐藏后会看到一个打了勾的「显示文件夹栏」。
- 「导出布局」建议的文件名不再带内部页面 id：原来是 `library:main.json`，而冒号在 Windows 文件名里本来就不合法；现在是 `library.json`。导出的内容不变：`PageLayoutIO` 导出的一直是全部页面（LoadAll），传进去的名字只影响建议的文件名，外加一次结果相同的多余读取。

## 2. 文件清单（相对仓库根）

- `windos/src/TipCL/Pages/BlockHost.cs`
- `windos/src/TipCL/Pages/MultiplayerPage.cs`
- `windos/src/TipCL/Pages/PlaytimePage.cs`
- `windos/src/TipCL/Pages/JavaPage.cs`
- `windos/src/TipCL/Pages/ServersPage.cs`
- `windos/src/TipCL/Pages/AccountPage.cs`
- `windos/src/TipCL/Pages/LaunchPage.cs`（只改了约 479–525 行的区块宿主段）
- `windos/src/TipCL/Pages/LibraryPage.cs`（只改了 `LayoutMenu`）
- `windos/tests/TipCL.Tests/PageDefaultsV3Tests.cs`（新建，51 个用例）
- `windos/tests/TipCL.Tests/LayoutBlockHostTests.cs`（HostTable 里 playtime / java 的默认可见集合跟着改，注释说明白名单默认和页面默认的区别）
- `windos/tests/TipCL.Tests/LayoutDefaultTests.cs`（新增 `PageBlockTitleTests`，10 个用例，按源码断言改名完成、id 没变、旧名字不再出现）
- `windos/progress/WP-L5.md`（本文件）

`LayoutNativeBlockTests.cs` 在清单里，但它测的是插件 kind `block` 的注册表，和本包无关，所以没改。
所有文件都保持原编码：无 BOM、LF 换行。

## 3. 构建与测试（§7，只在自己的工作区里跑）

| 项 | 基线（resume note，WP-L3 实测） | 收尾 |
|---|---|---|
| compact 构建 | 0 错误 / 8 警告 | 0 错误 / 8 警告（同一组：6×NU1900 NuGet 缓存目录权限 + 2×xUnit1031 `BridgeMutationAndLaunchGuardTests.cs`，没有新增） |
| premium 构建（`-p:Premium=true`） | — | 0 错误 / 8 警告（同上） |
| 全量测试 | 593 通过 / 0 失败 / 0 跳过 | **654 通过 / 0 失败 / 0 跳过**（+61） |
| compact 体积 | 5,599,232 B | **5,600,768 B**（+1,536 B；上限 5,757,264） |
| premium 体积 | — | 5,621,248 B |

命令（环境变量照 resume note 设置，脚本在 `windos\scratch\l5run.ps1`，scratch 目录被 .gitignore 忽略）：

```powershell
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1 -nodeReuse:false
dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1 -nodeReuse:false -p:Premium=true
dotnet test TipCL.sln -c Release --no-build -nodeReuse:false   # 最后一次是在 compact 重编之后跑的
```

PageDefaultsV3Tests 覆盖的验收项：

- 4 页默认的块集合 = 页面白名单（按源码抓 BlockSpec）；默认隐藏 = `DefaultVisible=false`；页面确实把 `PageDefaultLayouts.X()` 交给了 BlockHost；
- 用 `LayoutGrid.Pack` 核对每页的格子位置；隐藏块显示后的落位；
- **文档里没有该页 → 新默认**（真实 LayoutStoreV2 读写，临时目录）；**有该页 → 完全照文档**（纵向列表倒序 + 藏一块；以及 6 列网格、默认隐藏的块在文档里是显示状态）；
- 默认布局经过一次「什么都不动就点完成」的落盘再读回，几何、显隐、span 不变；
- 按源码钉住 `LayoutEditor.LoadFromStore` 的取页规则（规则一变这里就会红）；
- `BlockHostRules` 的 0/1/2/5 块；`settings:network` / `settings:about` 宿主确实只有 1 块，而且在入口表里标着 SingleBlock；BlockHost 只在门槛分支里建「自定义布局...」，单块分支只挂禁用的占位菜单；
- `LayoutBlockHostTests.HostTable` 是手抄的白名单，本包改过的 8 个宿主逐行和源码对一遍，防止表和代码分叉。

## 4. 合并时要主 agent 做的事

1. **词表**：按 §7 的表合入 `zh_CN.json` / `en.json`，再重新打包 `.json.gz`。
2. **全局右键的兜底路径（Q5 收尾）**：`Pages/Layout/LayoutShellHooks.cs` 的 `OnRightUp` 在鼠标不在任何宿主里时，会挑「包含鼠标的最小宿主 / 最大的宿主」，这条路径仍可能挑中单块宿主（比如在设置的「下载与网络」视图里右键左侧空白）。建议在 `Hosts()` 收集时或 `FindTargetHost` 里过滤 `h.OffersLayoutMenu`，过滤后为空就按 `hosts.Count == 0` 那条分支处理。需要一行到几行改动，`OffersLayoutMenu` 已经是 internal，同程序集可直接用。
3. **`LayoutEditor.CanEdit` 仍是 `_specs.Count >= 1`**（WP-L12 的文件）。设置 → 布局的入口按钮以它为准，L4 按 `SingleBlock` 不列卡片就够了；如果想让 CanEdit 也收紧到 ≥2，改成 `BlockHostRules.OffersLayoutMenu(_specs.Count)`。注意 `TIPCL_DEV_LAYOUT_EDIT` 和 DevShot `--edit` 也走 CanEdit。
4. **可能的冲突点**：
   - `PageDefaultsV3Tests.EditorStillPicksDefaultOnlyWhenDocHasNoPage` 按源码断言 `LayoutEditor.LoadFromStore` 里的四个片段（`LayoutVariants.PageOf(doc.Layout, _pageId`、`if (page == null && _defaultPage != null)`、`page = _defaultPage.Clone();`、`LayoutEngine.LoadState(_pageId, page, _refs)`）。WP-L12 / WP-L3 如果改了这几行的写法（语义不变），把断言改成新写法即可；如果语义变了，就说明「文档里没有该页才用新默认」不再成立，需要回头看。
   - `LayoutBlockHostTests.HostTable` 的 playtime / java 两行改了。其它包如果也改了这张表（比如 L4 补 `library:main`），合并时两边都保留。
5. **设置 → 布局的「默认 / 已自定义」判定**（WP-L4）：这 4 页现在和实例库一样带页面默认。进编辑态什么都不动就点「完成」，`ExitEdit → Persist` 会把默认写进文档（现有行为，实例库早就这样），之后卡片会显示「已自定义」。如果 L4 想让这种情况仍显示「默认」，可以拿文档里的页和 `PageDefaultLayouts.ForPage(pageId)` 比较（`ForPage` 就是为此准备的，对其它页返回 null）。卡片缩略图想画出厂样子时也可以用它。
6. **文档**（WP-L6）：`docs/布局使用指南.md` 里如果还写着旧的区块名（状态卡、累计卡…），要跟着改；本包没碰 `docs/`。

## 5. 决定（合并时编号 D-82 起）

- **L5-a 页面默认布局集中放在 `PageDefaultLayouts`（BlockHost.cs）**，不像实例库那样各自写在页面里的 private 方法中。原因：页面类是 WPF 控件，net8 测试宿主加载不了，放在页面里就测不到。实例库的默认没有搬，保持原样。
- **L5-b 默认隐藏的块只给 span、不给坐标，并排在最后。** 这样在「添加」里显示出来时会自动流到末尾、两两并排；显式坐标会让它们全挤在第 0 列。
- **L5-c 「概览」「说明」的 `DefaultVisible` 同步改成 false**，和默认布局的 hidden 保持一致。影响：如果某份已有文档里这一页**缺**这个块（正常存档不会缺，WP-U3 起就有这两个 id），补齐时会按隐藏补。
- **L5-d 默认隐藏块上的操作按钮跟着挪**：游戏时长的刷新 / 清空统计挪到「时间范围」卡；Java 的计数 / 刷新 / 扫描挪到「已检测的 Java」块。原则：默认界面上不能少功能。
- **L5-e 单块页挂一个禁用的占位 ContextMenu**，让壳级全局右键让路。这样不改 `LayoutShellHooks` 也能在宿主内部做到 Q5；宿主外部的兜底路径留给合并时处理（§4-2）。
- **L5-f 实例库导出建议文件名改成 `library.json`**（去掉内部 id、去掉 Windows 不允许的冒号）；文件夹栏菜单改成固定标题的勾选项。

## 6. 越界需求（独占清单外确实需要改、但本包没改）

- `windos/src/TipCL/Pages/Layout/LayoutShellHooks.cs`：全局右键兜底路径过滤单块宿主（见 §4-2）。
- **启动页编辑态压暗英雄区 + 「主区域固定，不能拖动或隐藏」锁定标签**（§5.5）：需要两样东西，本包都拿不到。
  - (a) `LayoutEditor.cs`（WP-L12）提供进 / 出编辑态的通知，例如 `public event Action<bool>? EditingChanged`，在 `EnterEdit` 置 `_edit = true` 之后和 `ExitEdit` 末尾各触发一次。现在没有这个事件：设置页的入口按钮直接调 `host.Editor.EnterEdit()`、编辑条上的「完成」直接调编辑器，都不经过 BlockHost，所以 BlockHost 自己猜不准。
  - (b) `LaunchPage.cs` 区块宿主段**以外**的代码：给 `_hero` 设 `Opacity = 0.45` 并去饱和，在英雄区右上角叠一个锁定标签。图标用 WP-L0 的 `Geo.Lock`，配色用 `B.EditDim`、`B.EditBarBg`，都通过 `TryFindResource` 取并给兜底值。
  
  合并后由主 agent 接：BlockHost 转发 `_editor.EditingChanged`（或者 LaunchPage 直接订阅 `_blocks.Editor.EditingChanged`），LaunchPage 收到后切换压暗状态。新文案「主区域固定，不能拖动或隐藏」要进词表（见 §7）。

## 7. 词表（离线核对：本包改过的 8 个 .cs 里所有 `I18n.L("…")` 字面量共 443 个，对照 `zh_CN.json` / `en.json` 各 3,033 个 key）

**新增（本包引入，两边都缺）：**

| 中文 key | 建议英文 |
|---|---|
| 联机状态 | Connection status |
| 房间 | Room |
| 快捷操作 | Quick actions |
| 累计时长 | Total playtime |
| 时间范围 | Time range |
| 明细 | Details |
| 已检测的 Java | Detected Java |
| 账号列表 | Accounts |

（「玩家」「实例」两个 key 本来就有，直接复用。）

**基线就缺（不是本包引入的，在本包改过的文件里顺带扫出来，建议一起补）：**

| 中文 key | 建议英文 | 位置 |
|---|---|---|
| 1.20.5 – 1.21.11 | 1.20.5 – 1.21.11 | JavaPage.cs:21 |
| 保存 Java 选择失败 | Failed to save the Java choice | LaunchPage.cs:1035 |
| 停止失败 | Failed to stop | LaunchPage.cs:1545 |
| 取消登录失败 | Failed to cancel sign-in | AccountPage.cs:454 |
| 陶瓦联机暂未提供此架构的官方内核;项目地址可以直接复制。 | Terracotta has no official core for this architecture yet; you can copy the project link instead. | MultiplayerPage.cs:405（词表里只有不带后半句的「…官方内核。」） |
| 复制项目地址 | Copy project link | MultiplayerPage.cs:405 |
| 已复制项目地址 | Project link copied | MultiplayerPage.cs:407 |

**废弃（本包改完后 `windos/src` 里已经没有任何地方用到；只扫了 Windows 树）：**
状态卡、房间卡、操作区、玩家列表、累计卡、范围卡、明细列表、已检测列表、实例行、账号卡、隐藏文件夹栏

**合并 §6 越界项时会新增：** 主区域固定，不能拖动或隐藏 → "Main area is fixed and can't be moved or hidden"

## 8. 待真机（需要启动 TipCL.exe，用户在场时跑；在 `D:\项目\TipCL\_r34\base\windos` 合并、编译之后执行）

注意：截图用的是本机真实的布局文档。本机 `ui_layout_v2.json` 里如果已经存过这几页，截到的是文档里的样子（这也是 Q4 要的行为）。要看新默认，就先把该页从文档里去掉（设置 → 布局 → 该页「恢复默认」不行，那会把默认写进文档；要用干净的数据目录，或者手动删掉 `pages.<id>` 条目）。

```powershell
$exe = '.\src\TipCL\bin\Release\net48\TipCL.exe'
foreach ($p in 'multiplayer','playtime','java','launch') {
  & $exe --shot $p --theme light --out "docs\ui-audit\layout-v3\$p-light.png"
  & $exe --shot $p --theme dark  --out "docs\ui-audit\layout-v3\$p-dark.png"
  & $exe --shot $p --theme light --edit --out "docs\ui-audit\layout-v3\$p-edit-light.png"   # --edit 由 WP-L0 提供
  & $exe --shot $p --theme dark  --edit --out "docs\ui-audit\layout-v3\$p-edit-dark.png"
}
& $exe --i18n-check     # 词表合入后,缺失应为 0
```

人工核对项：

1. **联机**：「房间」卡在没有房间时是 `Collapsed`，网格里「联机状态」右边会空出半行。「玩家」块没人时也是空的。看是否能接受；不能接受的话，可以考虑在页面上按状态 `MutatePage`，或者让 L12 的网格对 Collapsed 内容压缩行高。
2. **游戏时长**：「时间范围」卡半宽时，`范围 + 190px 下拉 + 刷新 + 清空统计` 在 1100px 以下的窗口和 150% 缩放下会不会被挤出卡片。
3. **Java**：「实例默认」卡半宽时，Field 的标签列是 140px，下拉和路径输入框够不够宽；顶部那句版本对应提示换行是否正常。
4. **启动页**：网格模式下新闻卡（左对齐、Glass）的高度和位置与原来一致；显示任务 / 时长 / 便签 / 日志后两两并排；62% 限高内滚仍然正常。
5. **单块页**：设置 → 「下载与网络」「反馈与关于」在内容区右键**不出现**「自定义布局...」；在其中的文本框里右键仍是复制粘贴菜单；在设置左侧导航空白处右键的现状要记下来（§4-2 修之前可能还会弹）。
6. **实例库**：右键菜单里「显示文件夹栏」的勾选状态和实际一致；点「导出布局」时建议的文件名是 `library.json`，能直接保存。
7. **多块页回归**：下载、任务、账号、服务器、联机等页右键仍有「自定义布局...」。

## 9. 风险

- **默认网格的观感没有实测**：半宽卡片的拥挤程度、Collapsed 的房间卡留下的空位、启动页网格的高度，都只在代码层面推过，要等 §8 的截图确认。
- **单块页的占位菜单**依赖 WPF 的 ContextMenuService 语义：IsEnabled 不继承，目标元素被禁用时会跳过。按 Microsoft 文档和源码行为推断，没有实机验证；最坏情况是右键弹出一个空的小菜单框，不会崩。
- **已存过这些页的用户看不到新默认**：这是 Q4 的既定结果。另外，旧版本里只要进过一次编辑态再点完成（哪怕什么都没动），文档里就已经有该页，所以「看到新默认」的用户可能比预想的少。
- **源码断言型测试**（PageBlockTitleTests、PageDefaultsV3Tests 里抓 BlockSpec 的部分）对代码格式敏感：要求 BlockSpec 一行一个、`Id = "…", Title = I18n.L("…")` 的顺序。以后有人改写格式，测试会红，并给出明确的提示信息。
