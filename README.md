<div align="center">

<img src="docs/icon.png" width="140" alt="TipCL" />

# TipCL

**轻量、好看、开箱即用的 Minecraft 启动器**

<sub>A lightweight Minecraft launcher for Windows</sub>

<br/>

<img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows" />
<img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET Framework 4.8" />
<img src="https://img.shields.io/badge/UI-WPF-6CB33E?style=flat-square" alt="WPF" />
<img src="https://img.shields.io/badge/%E8%AF%AD%E8%A8%80-%E4%B8%AD%E6%96%87%20%7C%20English-F4A93B?style=flat-square" alt="中文 | English" />

[功能](#-功能) · [下载与构建](#-下载与构建) · [MCP Server](#-mcp-server) · [目录结构](#-目录结构) · [开发者选项](#-开发者选项)

</div>

---

## ✨ 功能

| | |
|---|---|
| 🎮 **版本安装** | 原版、Forge、NeoForge、Fabric、Quilt、OptiFine，自动选择镜像源 |
| 🧩 **模组与整合包** | Modrinth / CurseForge 搜索安装，mrpack 与 CurseForge 包导入导出 |
| 🗂️ **实例与存档** | 多实例隔离、存档管理、Java 自动检测 |
| 👤 **账号** | 微软正版、外置登录（authlib-injector）、离线皮肤 |
| 🌐 **联机** | 内置 Terracotta 局域网联机 |
| 🤖 **AI 助手** | 读懂崩溃日志，给出一键修复建议；跨对话记住你的配置和偏好；可作为 MCP Server 供外部 AI 调用 |
| 🏠 **仪表盘首页** | 启动配置、实时日志、快捷入口、游戏时长、新闻，布局随心拖拽 |
| 🎨 **外观** | 浅色 / 深色主题，中英双语界面 |

## 📦 下载与构建

**直接使用**：在 Releases 下载 `TipCL.exe`，双击运行即可。只需要系统自带的 .NET Framework 4.8，无需安装任何运行时。

**自己编译**：需要 .NET SDK 8 或更高版本。

```bat
git clone https://github.com/wfwwq/TipCL.git
cd TipCL
dotnet build src/TipCL/TipCL.csproj -c Release
```

产物在 `src/TipCL/bin/Release/net48/TipCL.exe`，依赖已通过 Costura 打进单个 exe。

## 📁 目录结构

```
src/TipCL/
├─ App.xaml / MainWindow.xaml   应用入口与主窗口
├─ Themes/                      浅色、深色调色板与控件样式
├─ Pages/                       各功能页面（含 AI 页）
├─ Services/                    动画、对话框、壁纸、输入法守卫等界面服务
├─ Models/                      界面数据模型
├─ Core/                        启动器内核：下载、安装、启动、账号、模组、联机、AI
└─ Resources/                   内置词表与 AI 模型规则
```

## 🔌 MCP Server

TipCL 可以作为本机 MCP Server，让 Claude、Cursor 等外部 AI 直接调用启动器的工具（查实例、装版本/模组/整合包/光影、启动游戏、诊断崩溃）。

在 **设置 → AI 助手 → MCP Server** 打开开关即可，地址：`http://127.0.0.1:25585/mcp`（只监听本机）。

Claude Code：

```bat
claude mcp add --transport http tipcl http://127.0.0.1:25585/mcp
```

Cursor（`~/.cursor/mcp.json`）：

```json
{ "mcpServers": { "tipcl": { "url": "http://127.0.0.1:25585/mcp" } } }
```

Claude Desktop 等只支持 stdio 的客户端可通过 `npx mcp-remote http://127.0.0.1:25585/mcp` 接入。端口可在数据目录的 `mcp_server.json` 里改。

## 🛠️ 开发者选项

| 启动参数 | 作用 |
|---|---|
| `--nav-dump` | 打印侧栏编排结果 |
| `--i18n-check` | 扫描未本地化的中文字面量 |
| `--smoke` | 冒烟模式，跳过需要手动点击的弹窗 |
| `--lang xx` | 指定界面语言 |

环境变量：`TIPCL_HOME`（数据目录）、`TIPCL_LANG`、`TIPCL_WPF_DEBUG`。

## 🔄 更新机制

启动器默认读取本仓库的 [`update.json`](update.json) 检查更新。发布新版本时修改其中的 `version`、`url`（下载地址）和 `sha256` 即可。

---

<div align="center"><sub>Made with ❤️ for Minecraft players</sub></div>
