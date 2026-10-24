# TipCL

轻量的 Minecraft 启动器（Windows，WPF / .NET Framework 4.8）。

启动器内核与界面都在同一个程序里，编出来就是一个独立的 `TipCL.exe`，不需要额外的运行时或后端进程。

## 功能

- 版本安装：原版、Forge、NeoForge、Fabric、Quilt、OptiFine，支持 BMCLAPI 等镜像源
- 模组 / 整合包：Modrinth 与 CurseForge 搜索、安装、导入导出（mrpack / CurseForge 包）
- 实例与存档管理、Java 自动检测
- 账号：微软正版、外置登录（authlib-injector）、离线皮肤
- 联机：基于 Terracotta 的局域网联机
- 内置 AI 助手：诊断崩溃、一键修复建议
- 首页仪表盘：启动配置、实时日志、快捷入口、游戏时长、新闻主页，可自由编辑布局
- 浅色 / 深色主题，中英双语

## 构建

需要 .NET SDK 8 或更高版本（目标框架 `net48`）。

```bat
dotnet build src/TipCL/TipCL.csproj -c Release
```

输出在 `src/TipCL/bin/Release/net48/TipCL.exe`，依赖已经通过 Costura 打进 exe，单文件即可分发。运行只需要系统自带的 .NET Framework 4.8。

## 目录

```
src/TipCL/
  App.xaml, MainWindow.xaml   主窗口与应用入口
  Themes/                     浅色、深色调色板与控件样式
  Pages/                      各功能页面（含 AI 页）
  Services/                   界面层服务：动画、对话框、壁纸、输入法守卫等
  Models/                     界面数据模型
  Core/                       启动器内核：下载、安装、启动、账号、模组、联机、AI
  Resources/                  内置词表（locales）与 AI 模型规则
```

## 自检开关

| 开关 | 作用 |
|---|---|
| `--nav-dump` | 打印侧栏编排结果 |
| `--i18n-check` | 扫描未本地化的中文字面量 |
| `--smoke` | 冒烟模式（跳过需要人工点击的弹窗） |
| `--lang xx` | 指定界面语言 |

环境变量：`TIPCL_HOME`（数据目录）、`TIPCL_LANG`、`TIPCL_WPF_DEBUG`。

## 更新

启动器默认从本仓库的 `update.json` 检查更新。发布新版本时修改其中的 `version`、`url`（下载地址）和 `sha256`。
