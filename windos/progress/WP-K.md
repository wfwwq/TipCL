# WP-K 启动页实例内版本选择 —— 完成待验（2026-11-07，双 flavor 已编译，未 commit→已 commit）

## 根因（代码核实）
1. **数据模型**：版本挂在实例目录下（`<实例>/versions/<版本>`）。后端 `Core/BackendApi.cs:2396 get_installed_versions(instance)`：带 instance 返回该实例 `InstalledIds()`；不带则返回全部 "实例 / 版本" 串。`InstanceInfo` 只有 `Versions` 计数。UI 侧"所选实例的版本列表"的既有数据源就是 `get_installed_versions {instance}` → `List<string>`，启动页 `ReloadVersionsAsync()`（LaunchPage.cs:1125）已经调它灌进 `_ver` ComboBox。
2. **启动链路**：`LaunchPage.LaunchAsync()`（原 :1433）取 `version = _ver.Str()`，同值传入 `preflight_launch` 与 `launch_game` 任务；`LaunchNowAsync(name, version)`（原 :1212）经 FillCardData/ApplyPreferenceAsync 选中版本后同样走 LaunchAsync。
3. **Bug 所在**：v2 启动屏唯一入口 `_titleRow`（实例名+箭头）打开 `OpenSwitchPopup()` → `RenderSwitchRows()`（原 :903）只按 `_instances` 渲染实例行、点击=切实例；装了多个版本的实例没法在启动屏选要启动的哪个版本（`_ver` 藏在「启动配置」浮层里，用户不可见，默认拿列表第一项启动）。

## 方案（最小改动，贴 v2 浮层风格）
实例切换浮层里、**当前实例行下展开该实例的版本子列表**；hero 小标签行加当前版本 chip。
- 数据零新增后端调用：直接用 `ReloadVersionsAsync` 维护的 `_ver.Items`（即 `get_installed_versions` 结果）。
- 点版本 → 关浮层 + `_ver.SelectedItem = v` → 走既有 `_ver.SelectionChanged → SyncBanner`（版本 chip/模组数/背景全刷新）→ 「启动游戏」按 `_ver.Str()` 启动所选版本。`preflight_launch`/`launch_game` 同值。
- 实例详情页顺带生效（无需改动）：InstanceDetailPage「启动」→ `LaunchNowAsync(_inst)` → FillCardData 的 `_ver.Fill(list)` 保留仍在列表里的当前选中版本 → 启动所选版本。
- 浮层搜索框现在也匹配版本名：实例名命中显示其全部版本，仅版本命中显示匹配版本（实例不命中且无版本命中则隐藏该实例）。

## 改动文件
| 文件 | 行数变化 | 内容 |
|---|---|---|
| `src/TipCL/Pages/LaunchPage.cs` | +76 / −3 | ① `RenderSwitchRows()`：当前实例行下追加版本子行；无版本时显示空态文案（复用既有 I18n 串「这个实例还没有版本，先去「原版游戏」装一个」）；② 新增 `VerRow(v, sel)`：MinHeight 46、圆角 6、左缩进 26、悬停 `B.RowHover`、选中 = `B.AccentText` 粗体 + `Ico.Check`（`B.Accent`）勾、点击选中并关浮层；③ `SyncHero()`：chip 行首位加当前版本 chip。全部在共用代码，未动 `#if PREMIUM` 结构，未新增资源键/I18n 键。 |

## 视觉核对（--shot 实拍，compact exe）
- 浮层：PopShell 白卡圆角 12（既有）不变；版本子行 46 高、当前版本主题色+勾（截图见验收记录；主题色随用户主题=红）。
- hero：chip 行变为「版本 / Java / 内存(/模组数)」。
- 截图验证方式：`--shot launch --pop instances`（DevShot 把浮层内容摘进页面根渲染，可拍）；`--shot launch` 看 chip。临时 png 已清理。

## 构建/测试/体积（Windows 实测）
| 项 | 结果 |
|---|---|
| compact `dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1` | **0 错误**（1050 警告均为既有；CS4014 36 条与基线一致，仅行号平移） |
| compact exe | **5,706,240 B** ≤ 5,707,264 B，**余量 1,024 B**（本次改动耗 1,536 B） |
| premium（加 `-p:Premium=true`） | **0 错误**，exe 6,001,152 B（仅记录） |
| `dotnet test TipCL.sln -c Release` | **127/128**，唯一失败 = 既有基线 `PhoneLinkTests.FullFlow` |

## 遗留风险
1. 体积余量仅剩 1,024 B，后续包再加代码需先回收（WP-A：删 `Card.Shadow`/`Seg.Item` DropShadowEffect；WP-C：删 InstallPage LiteLoader 行/体积 tooltip）。
2. 版本子列表只展开在"当前实例"下；其它实例仍需先切换（点击实例→关浮层→再点开才有它的版本）。未做"每实例常驻子列表"是为省体积（每实例一次 API + 常驻行渲染）。如监工要求全展开，改用无参 `get_installed_versions`（返回 "实例 / 版本" 串）一次拉全即可。
3. 浮层 MaxHeight 430/滚动 300 不变，版本很多的实例在浮层内滚动查看。
4. `_ver.Items` 依赖 ReloadVersionsAsync 已完成；启动页刚加载、版本未回前打开浮层会暂时没有版本子行（异步回来后下次打开即有，实机影响极小）。
