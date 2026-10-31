# TipCL Icons 内置图标字体

`src/TipCL/Resources/fonts/TipCLIcons.ttf` 取代系统的 Segoe MDL2 Assets / Segoe Fluent Icons，
让 Win7/8.1、精简版 Win10、Wine 等缺少这些字体的环境也能显示图标（之前会显示成方框）。

- 字形来自 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)（MIT License, © Microsoft）。
- 码位与 Segoe MDL2 Assets 保持一致，代码里的 `"\ue710"` 等写法不用改。
- `map.py`：MDL2 码位 → Fluent 图标名的对照表。

## 新增图标
1. 在 `map.py` 加一行码位映射。
2. 下载 `FluentSystemIcons-Regular.ttf/.json`、`FluentSystemIcons-Filled.ttf/.json`（仓库 `fonts/` 目录）到一个目录。
3. `pip install fonttools && python tools/iconfont/build.py <该目录>`，把生成的 `TipCLIcons.ttf` 复制到 `src/TipCL/Resources/fonts/`。
4. `dotnet test tests/TipCL.Tests` —— `IconFontTests` 会检查源码里用到的每个图标码位都在字体里。
