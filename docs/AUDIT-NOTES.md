# 审计与实现核对笔记(AUDIT-NOTES)

> 本文件收纳原先放在仓库根 [`README.md`](../README.md) 里、只对开发者与 agent 有用的段落:**两版逐条核对、实现差异表、平台口径、目录与仓库结构、插件契约、开发者启动参数**。
> 2026-11-28 由 README 原样搬入,**内容未删未改**;只把相对链接按本文件的新位置补齐了 `../`。
> 相关文档:立项任务书 [`docs/dev/GOAL.md`](dev/GOAL.md)· 逐轮过程 [`docs/dev/PROGRESS.md`](dev/PROGRESS.md)· 技术决策 [`docs/DECISIONS.md`](DECISIONS.md)· 接手流程 [`docs/dev/交接必读.md`](dev/交接必读.md)· 与 Windows 版的对照矩阵 [`docs/PARITY.md`](PARITY.md)。

---

## 两版功能差异逐条核对

> 原 README「✨ 功能」节的逐条核对说明,整段迁入。

> **两条实现在这张表上的差异（2026-11-20 逐条核对）**：上表对 **Windows 桌面版**逐条成立；Linux / Avalonia 版（`src/`）除下面两处外也都已落地（启动链、Java 探测、模组/整合包、实例与存档、账号、Terracotta、AI 助手、MCP Server、插件市场与钩子、仪表盘、主题与双语）。MCP Server 自 2026-11-21 起两版都有，外部 agent 发起的写操作每次都会在启动器里弹确认（Windows 版自 v1.3 起与 Linux 对齐，见[README 的「🔗 MCP Server」节](../README.md#-mcp-server)）：
>
> | 能力 | Windows | Linux 树 | 事实 |
> |---|---|---|---|
> | **插件网页界面**（插件页签 / 浮层 / 网页区块） | ✅ WebView2 内嵌；Win7 / 8.1 上自动改用浏览器 | ⚠️ **无网页宿主**：按既有先例恒走「当前系统不能内嵌网页 + 在浏览器中打开」的兜底分支（`src/TipCL.App/Pages/Layout/Widgets/DangerousWidgets.cs`、`Pages/Layout/PluginBlocks.cs` 文件头降级说明） | 插件的**逻辑**（AI 工具 / 钩子 / 首页卡片数据）在 Linux 可用，网页形态的 UI 面会外开浏览器 |
> | **手机同步** | ✅ 桌面端（`Core/Launcher/Sync/PhoneClient.cs`） | ✅ 桌面端（`src/TipCL.Core/Launcher/Sync/`、设置页 `PhoneSyncPanel.cs`） | **两端都只是「桌面半边」**：手机端服务端在 `TipCL-Android` 仓库里找不到对应实现（`X-PyMCL-Token` / `/api/ping` / `deviceTimeMs` 全库 **0 命中**）⇒ 别把它当"现在就能互传存档" |

---

## 自定义布局:平台口径

> 原 README「🧱 自定义布局」节的平台口径说明。

> **平台口径(2026-11-20 复核,Linux 树代码尖 `b86afe3`)**:上面这段描述的是 **Windows 桌面版**(功能全量)。**Linux 移植版**已落地布局 v2 的文档 + 引擎 + 编辑器 + 右键「自定义布局...」入口 + 自建区块渲染,设置页「布局」视图的 7 个小节(外观与密度 / 页签与标题栏 / 页面区块 / 分栏比例 / 自建区块 / 布局方案 / 布局 JSON)也已在 Linux 树(`src/TipCL.App/Pages/Settings/SettingsPage.Layout.cs`,分栏引擎在 `src/TipCL.App/Controls/PageLayoutHost.cs`);AI 排版工具也已在 2026-11-21 落地(`src/TipCL.Core/Launcher/Ai/LayoutTools.cs` 的 `layout_get` / `layout_apply`,应用前同样先存一份「AI 备份 <时间>」方案;Linux 版对网页 / 按钮区块多执行一道 D-30 门禁,AI 写入或改过地址 / 动作的这类区块一律默认关闭,要用户自己去「设置 → 布局 → 自建区块」开启)。逐条清单见 [`docs/README.md`](README.md)「布局 v2:Linux 移植版的真实进度」。本节后面链接的两份手册写的是 **Windows 版**行为。

---

## 自定义布局:自建区块入口(实现路径)

> 原 README「🧱 自定义布局」节的实现说明。

> **自建区块入口**:设置 →「布局」→「自建区块」里的**「添加自建区块」按钮已启用**(2026-11-19 的 L6 起:内联管理面板可新建 / 改名 / 删除,页面的编辑态里也能用「+ 自建区块」把块加到本页;`windos/src/TipCL/Pages/SettingsLayoutPage.cs` 的 `LayoutWidgetRows()` / `WidgetCreateForm()`)。**布局 JSON 卡片 / 导入整机方案 / AI 排版**三条路径仍然有效,适合批量改或分享。窄窗 / 宽窗两套布局由运行时按窗口宽度自动切换,编辑器对它是只读的,不能手动编辑。

---

## 开发者自查(构建与冒烟)

> 原 README「📦 下载与构建」节末段。

开发者自查（都不起界面、不启动游戏）：`tools/linux-real/verify.sh` 在真 Linux / WSL2 上构建、跑全量测试与硬件探针（装了 NVIDIA 专有驱动时，显卡显存从 `nvidia-smi` 读取）；`--visual-tour <剧本.json> <输出目录>` 按剧本无窗口点击并截图，配合 `tools/visual/` 生成与效果图的对照报告，见 [`tools/visual/README.md`](../tools/visual/README.md)。

---

## 📁 目录结构

> 原 README「📁 目录结构」节(Windows 桌面版源码树)。

<details>
<summary>展开完整目录树</summary>

```
windos/src/TipCL/
├─ App.xaml / MainWindow.xaml   应用入口与主窗口
├─ Themes/                      浅色、深色调色板与控件样式
├─ Pages/                       各功能页面（含 AI 页、插件页）
├─ Services/                    动画、对话框、壁纸、WebView、输入法守卫等界面服务
├─ Models/                      界面数据模型
├─ Core/                        启动器内核：下载、安装、启动、账号、模组、联机
│  ├─ Launcher/Ai/              AI 工具、记忆、看图、MCP Server
│  └─ Plugins/                  插件系统：宿主、市场、会话、钩子、SDK
└─ Resources/                   内置词表、AI 模型规则、图标字体、插件宿主脚本
windos/tests/TipCL.Tests/       单元测试（xUnit）
windos/samples/                 示例插件（Node）与宿主测试
windos/docs/plugins/DESIGN.md   插件系统接口契约文档
windos/tools/iconfont/          内置图标字体的映射表与生成脚本
```

</details>

---

## MCP Server:平台口径

> 原 README「🔗 MCP Server」节的平台口径说明。

> **平台口径(2026-11-21 更新)**:两版都有 MCP Server(Windows `windos/src/TipCL/Core/Launcher/Ai/McpServer.cs`,Linux `src/TipCL.Core/Launcher/Ai/McpServer.cs`),地址、端口 `25585`、协议版本、`mcp_server.json` 的键名都一样,默认都是关闭。**两版外部 agent 发起的写操作每次都会在启动器里弹确认**(Linux 版即使权限档是全自动,「以后都允许」对外部调用也不生效;Windows 版自 v1.3 起同样弹确认;没有确认界面、拒绝或 120 秒超时都返回 `permission_denied`)。两版都只收 `Content-Type: application/json` 的 POST,`Origin: null` 与非本机 `Host` 一律 403,防止网页借浏览器调用本机接口。

---

## 手机同步:口径

> 原 README「📱 手机同步」节的现状口径。

> **口径(2026-11-20 核对,别写成"现在就能用")**:本仓库与 Linux 树只有**桌面端**(客户端 + 设置页面板 + 同步面板);**手机端服务端不在本仓库**,在 `TipCL-Android` 里也**找不到对应实现**(`X-PyMCL-Token` / `/api/ping` / `/api/worlds` / `deviceTimeMs` 全库 0 命中)⇒ 现状是「桌面端协议与 UI 已就绪,手机端待配套」。

---

## 插件系统:给插件开发者

> 原 README「🔌 插件系统」节末行。

对开发者：清单 `tipcl-plugin.json` + Node SDK，契约文档见 [windos/docs/plugins/DESIGN.md](../windos/docs/plugins/DESIGN.md)，示例插件和宿主测试在 [windos/samples/](../windos/samples/)。

---

## 🛠️ 开发者选项

> 原 README「🛠️ 开发者选项」节(启动参数与环境变量)。

| 启动参数 | 作用 | 实现树 |
|---|---|---|
| `--nav-dump` | 打印导航编排结果 | 仅 Windows |
| `--i18n-check` | 扫描未本地化的中文字面量 | 仅 Windows |
| `--smoke` | Windows：冒烟模式，跳过需要手动点击的弹窗 / **Linux：无头离屏渲染一帧后退出** | 两端语义不同 |
| `--lang xx` | 指定界面语言 | 仅 Windows |

环境变量：`TIPCL_HOME`（数据目录）、`TIPCL_LANG`、`TIPCL_WPF_DEBUG`。

> **平台口径（2026-11-16 实测，Linux 树 `1823c3c`）**：上表是 **Windows 桌面版**的启动参数。Linux 移植版
> （`src/TipCL.App/Program.cs:17-32`）的解析器只认 **`--smoke`（无头截图，固定写当前工作目录的
> `smoke.png`）**、`--ai-selftest` 与 `-i <实例> launch <版本>`；`--nav-dump` / `--i18n-check` / `--lang`
> 在 Linux 树**没有实现**，而且**不会报错，是被静默忽略**（实测：`--smoke --nav-dump`、`--smoke --lang en`
> 都 exit 0，出的还是首屏帧）。`TIPCL_LANG` / `TIPCL_WPF_DEBUG` 在 Linux 树 `grep` 零命中（不读）。
> Linux 侧“选页 / 选主题 / 选输出”的真实通道是环境变量 `SMOKE_PAGE` / `SMOKE_DARK=1` / `SMOKE_SEG` /
> `SMOKE_SETTINGS_TAB`，见 [`docs/README.md`](README.md)「运行与冒烟」。

---

## 仓库结构

> 原 README「仓库结构」节。

本仓库有两个实现，各自独立成树：

| 目录 | 版本 | 说明 |
|---|---|---|
| [`windos/`](../windos/) | **Windows 桌面版**(WPF / .NET Framework 4.8) | 就是本文档介绍的这个；源码在 `windos/src/TipCL/`；流水线见 [`.github/workflows/ci-windows.yml`](../.github/workflows/ci-windows.yml) |
| `src/` `tests/` `tools/`（**仓库根**） | **Linux / Avalonia 移植**（当前主线） | 自包含单文件产物 + AppImage + `.deb`；流水线见 [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)；文档见 [`PROGRESS.md`](dev/PROGRESS.md) / [`DECISIONS.md`](DECISIONS.md) / [`docs/PARITY.md`](PARITY.md) |

* 发版：[`.github/workflows/release.yml`](../.github/workflows/release.yml) 由 release 事件（或手动指定 tag）触发，两个 job 把 Windows 的 `TipCL.exe` 与 Linux 的 AppImage / `.deb` 上传到同一个 Release。
* 仓库根 = Linux/Avalonia 移植；Windows 桌面版整棵树在 `windos/` 子目录（2026-11-14 调整，原 Windows 版在根）。
* 这份 README 放在仓库根：GitHub 首页只渲染根 README，而它是 Windows 桌面版的说明 —— 所以里面所有指向 Windows 树的路径都带 `windos/` 前缀。
* [`update.json`](../update.json) **留在仓库根**:Windows 版启动器按固定 URL
  (`https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json`)读取它检查更新；移植侧对应 [`update-linux.json`](../update-linux.json) ——
  两个清单都按固定 URL 读，挪进子目录会让**已安装的客户端**更新失效，所以它们不属于任何一个子项目，而属于“这个仓库的发布通道”。
* `LICENSE` 同理留在根（仓库级）。
* 移植的开发过程记录在 [`PROGRESS.md`](dev/PROGRESS.md) / [`DECISIONS.md`](DECISIONS.md) / [`progress/`](../progress/) / [`docs/PARITY.md`](PARITY.md)（与参考版的逐项对照）。

---

## 附:仓库根文档搬迁后的新位置(2026-11-28)

| 原位置(仓库根) | 新位置 |
|---|---|
| `GOAL.md` | [`docs/dev/GOAL.md`](dev/GOAL.md) |
| `PROGRESS.md` | [`docs/dev/PROGRESS.md`](dev/PROGRESS.md) |
| `交接必读.md` | [`docs/dev/交接必读.md`](dev/交接必读.md) |
| `DECISIONS.md` | [`docs/DECISIONS.md`](DECISIONS.md) |
