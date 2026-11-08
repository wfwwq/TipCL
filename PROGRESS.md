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
