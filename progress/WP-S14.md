# WP-S14 实例库两处布局精修 —— 完成（2026-11-07）

基于 main 4d765d0。改动仅 LibraryPage.cs（独占）。

## T1 左栏卡片高度自适应

**根因排查结论**：`left.VerticalAlignment = Top`（LibraryPage.cs:144，WP-S6 起就有）本身有效——修前用当前代码实际截图（1280x800 dark / 1600x1000+1920x1080+900x520 light，含用户手上的 dist exe `TipCL-premium-20261107-1308`）逐像素采样：白卡底边均止于内容末尾（约 y=392），**未复现"占满整列高度"**。用户截图的拉伸应来自更早的发布版（WP-S6 之前的旧实例库）。但代码里确实存在一个拉伸向量：body 网格 `Ui.G(null, "*,18,2*")` 行参数为 null → Grid 隐式单行是 **star 行**，一旦任何外层（如 ScrollViewer 内容在个别环境被拉伸到视口高度）给出多于内容的高度，star 行会把它分给内容，卡片视觉上被撑高。

**修法（LibraryPage.cs:165-167）**：`Ui.G(null, "*,18,2*")` → `Ui.G("Auto", "*,18,2*")`，行高显式 Auto，网格自身高度恒等于内容，拉伸向量根除。`left.VerticalAlignment = Top` 保留作为双保险。页面级 ScrollBody 的 ScrollViewer 保留（右栏版本分组需要整页滚动），左栏内部本就无独立 ScrollViewer，无需移除。

## T2 版本选择区加「实例详情」入口

**位置决策**：右栏头部（"版本选择"标题行）动作簇——搜索框与「刷新」钮之间加「实例详情」Chip 钮（\ue946 图标，LibraryPage.cs:156-159、head 网格 5 列→6 列 :166，detail 占 col4、刷新移 col5，副说明跨 6 列 :175）。理由：该行是"版本选择"区域唯一的页级动作区，与刷新并排语义一致（页级操作 vs 行级操作在行尾三钮），视觉贴合现有 Chip 风格，不新增层级。

**实现（LibraryPage.cs）**：
- `OpenDetailAsync`（~676-699 行）：进入前若 `_folder != _defaultInstance` 先 `InstancePage.SetDefaultAsync(_folder)`（get_version_rows 只认默认实例，与双击启动 LaunchVerAsync 同模式；因切文件夹时 EnsureCurrentAsync 已设默认，此处通常为 no-op）；取 `_instances` 中选中实例（缺则按路径构造 InstanceInfo 兜底）；页内切换 `base.Content = new InstanceDetailPage(info, back)`，`_shell` 字段缓存原内容，返回键恢复并 `Run(LoadAsync)` 刷新（详情页内可能改过版本/模组）。
- **双实例状态防护**：仅"进入前设默认、返回后不回滚"（按拍板执行）。实例详情页内嵌 VersionManagePage（设置分段）读的也是同一 default_instance，与详情页版本下拉同源，无打架面；详情页自身不做任何默认实例写入。

## 构建 / 测试 / 体积

| 项 | 结果 |
|---|---|
| compact | 0 错误，**TipCL.exe 5,743,104 B ≤ 5,757,264 B（余量 14,160 B）** |
| premium | 0 错误，6,039,552 B（仅记录） |
| 测试 | **128/129**（唯一失败 = 既有基线 PhoneLinkTests.FullFlow，环境性） |

## 截图（_wph_run/wps14/）

- `library-before-{dark,light,max,small}.png` + `library-dist-light.png`：修前逐像素采样，卡片均内容高（根因排查依据）。
- `library-after-dark.png` / `library-after-light.png`：左栏卡片高度 = 文件夹列表 + 添加或导入 4 钮即止，下方留空与右栏顶对齐；「实例详情」Chip 可见于刷新钮左侧。
- `detail-after-dark.png`：详情页正常渲染（分段轨道+版本下拉"26.3 · 完全独立"+模组列表），无 NRE。

## 改动文件

- src/TipCL/Pages/LibraryPage.cs（T1 行高 Auto；T2 入口钮 + OpenDetailAsync + _shell 字段）

## 遗留

- 用户截图中的"整列拉伸"在当前 main 与今日 dist exe 上均未复现（疑似旧版发布包）；若用户在新版仍见到拉伸，请提供其运行 exe 的构建时间再查。
- InstanceDetailPage 仍无真实点击路径的自动化验证（DevShot --detail 走 PageHost 直挂，绕过了新入口钮的点击链路）；入口链路为代码审读 + 详情页独立截图验证。
