# WP-S8b 实例库头部收尾 + 共享模式隐藏自有资源分段

日期：2026-11-07。前一代理僵死无产出，从零接盘（工作树遗留 T1 半成品 diff）。

## T1 实例库右栏头部两行 + 左栏收窄（LibraryPage.cs）

审读结论：半成品 diff **方向正确但有 2 个 bug，已补**：
1. `Ui.G(rows, cols)` 参数序写反：`Ui.G("Auto,Auto,*,Auto,Auto", "Auto,Auto")` 会生成 5 行 2 列；改为 `Ui.G("Auto,Auto", "Auto,Auto,*,Auto,Auto")`（2 行 5 列，第 3 列 `*` 吸收余量把搜索+刷新推到右侧）。
2. 副说明 `head.Add(subHead, 0, 1, 1, 5)` 落在第 0 行第 1 列（与计数重叠）；改为 `head.Add(subHead, 1, 0, 1, 5)`（第 1 行横跨 5 列）。

其余保留：左栏 420 固定宽 → MinWidth 300 / MaxWidth 380；body 列 `"*,18,2*"`（左:右 ≈ 1:2，左窄右宽）；副说明 12.5 / B.InkMuted / TextWrapping.Wrap（窄窗换行不截断，不 Trim）。截图 library-dark.png 验证：头部两行、副说明完整、左栏收窄。

## T2 共享（大锅饭）版本隐藏自有资源分段（InstanceDetailPage.cs）

结构决策（先读码确认）：InstanceDetailPage 原本**没有版本维度**（按实例打开，分段 模组/存档/资源包/光影/设置，设置内嵌 VersionManagePage）；且该页目前**没有任何入口构造它**（实例库「管理」实际走 ModsDialog/SavesDialog，页为预留）。按交接指定方案落地：详情页分段轨道右侧加**版本下拉**（数据源 `get_version_rows`，include_hidden=false，返回当前默认实例的版本——详情页由实例库进入时目标实例即默认实例），默认选中第一行：

- 选中版本 `isolated=false`（共享/大锅饭）：隐藏模组/存档/资源包/光影四个分段按钮，只留「设置」，轨道与下拉之间显示灰字 12.5 / B.InkMuted 说明「该版本为共享（大锅饭）模式，模组/存档使用实例共享目录；开启独立隔离请见版本管理页」（TextWrapping 换行，MaxWidth 420），若当前停在资源分段强制切到「设置」。
- `isolated=true` 或无版本行（安全兜底按独立处理）：五个分段照旧。
- 下拉项显示 `id · isolation_label`（如「26.3 · 完全独立」），切版本即时生效。

实现位置：InstanceDetailPage.cs —— 字段 `_verBox/_vers/_sharedNote`（~33-38 行）；bar 网格由 `*,Auto` 改 `Auto,*,Auto,Auto`（rail/说明/下拉/更多管理，~105-120 行）；`LoadVersionsAsync`（~178 行）；`ApplyVersionSegs`（~204 行）；构造尾部 `Run(LoadVersionsAsync)`。

验证：本机两个版本均 isolated=true，为拍共享分支在 DevShot.cs 加 dev-only `--detail`（详情页未注册导航的截图入口）与 `--forceshared`（注入假 isolated=false 行）钩子，不影响产品代码路径。

## 构建 / 测试 / 体积

- compact 构建 0 错误；premium 构建 0 错误。
- compact exe **5,741,568 B ≤ 5,757,264 B**（余量 15,696 B）。
- `dotnet test`：128 通过 / 1 失败（PhoneLinkTests.FullFlow，网络流程测试，环境性失败，与本改动无关）。

## 截图（_wph_run/wps8b/）

- `library-dark.png`：头部两行（标题+计数+搜索+刷新 / 副说明整行），左栏收窄，无截断。
- `instance-dark.png`：独立版本——五分段齐全 + 版本下拉「26.3 · 完全独立」。
- `instance-shared-dark.png`：共享版本——只余「设置」分段，灰字说明两行完整显示，下拉「26.3 · 共用」。

## 改动文件

- src/TipCL/Pages/LibraryPage.cs（T1 收尾）
- src/TipCL/Pages/InstanceDetailPage.cs（T2）
- src/TipCL/DevShot.cs（dev-only 截图钩子）
