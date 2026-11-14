# TipCL

AI 原生的 Minecraft 启动器。本仓库有两个实现:

| 目录 | 版本 | 说明 |
|---|---|---|
| `src/` `tests/` `tools/`(**仓库根**) | **Linux / Avalonia 移植**(当前主线) | 自包含单文件产物 + AppImage + `.deb`;流水线见 [`.github/workflows/ci.yml`](.github/workflows/ci.yml);文档见 [`PROGRESS.md`](PROGRESS.md) / [`DECISIONS.md`](DECISIONS.md) / [`docs/PARITY.md`](docs/PARITY.md) |
| [`windos/`](windos/) | **Windows 桌面版**(WPF / .NET Framework 4.8) | 源码在 `windos/src/TipCL/`,说明见 [`windos/README.md`](windos/README.md);流水线见 [`.github/workflows/ci-windows.yml`](.github/workflows/ci-windows.yml),发版见 [`release-windows.yml`](.github/workflows/release-windows.yml) |

**目录约定**

* 仓库根 = Linux/Avalonia 移植;Windows 桌面版整棵树在 `windos/` 子目录(2026-11-14 调整,原 Windows 版在根)。
* [`update.json`](update.json) **留在仓库根**:两个实现的启动器都按固定 URL
  (`https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json`)读取它检查更新 ——
  把它挪进子目录会让**已安装的客户端**更新失效,所以它不属于任何一个子项目,而属于"这个仓库的发布通道"。
* `LICENSE` 同理留在根(仓库级)。
* 移植的开发过程记录在 [`PROGRESS.md`](PROGRESS.md) / [`DECISIONS.md`](DECISIONS.md) /
  [`progress/`](progress/) / [`docs/PARITY.md`](docs/PARITY.md)(与参考版的逐项对照)。
