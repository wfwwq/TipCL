# PROGRESS — TipCL Linux 版 M1

> 工作包状态表。各代理收尾后在自己行内更新状态；详细报告见 `progress/M1-<包>.md`。
> 共享文件编辑纪律见 GOAL.md §6：各包只追加自己的小节，禁止重写他人内容。

| 工作包 | 内容 | 独占地盘 | 状态 | 报告 |
|---|---|---|---|---|
| M1-A1 | 工程脚手架（git init、sln、App 空壳、根文档、tools 骨架） | 仓库根、src/TipCL.App、tools/ | 已完成（本包范围；整体 build 待 A4 落地后复验） | progress/M1-A1.md |
| M1-A2 | 设计令牌与主题库（Palette 明暗、Controls、UiHelpers、ThemeGalleryPage） | src/TipCL.App/Themes/、src/TipCL.App/Ui/ | 进行中 | progress/M1-A2.md |
| M1-A3 | 窗口外壳与导航框架（Shell/：MainWindow、TitleBar、SideNav、NavRegistry） | src/TipCL.App/Shell/、Pages/ | 进行中 | progress/M1-A3.md |
| M1-A4 | Core 业务复用层（Windows 版 Core/Services 跨平台搬运 + 复用清单） | src/TipCL.Core/ | 进行中 | progress/M1-A4.md |
| M1-A5 | 测试与构建基建 + 冒烟（xunit、build 脚本、docs/、smoke 文档化） | tests/TipCL.Tests/、tools/（充实）、docs/ | 进行中 | progress/M1-A5.md |

## 状态约定

- 待开始 / 进行中 / 已完成（自证 build/test 通过后才能标"已完成"）。
- 冲突与依赖申请写各自 progress/M1-*.md，由主 agent 裁决。
