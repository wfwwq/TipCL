# DECISIONS — TipCL Linux 版

> 技术决定记录。D-1 起编号；各包只追加自己的小节（Edit 在文件末尾追加）。
> 已定结论（GOAL §2）不重新讨论，此处仅登记出处与落地细节；新增偏离需报备。

## D-1 · 框架与运行时（GOAL §2，A1 登记）

- UI 框架：Avalonia UI 11（C#），MVVM 不强制，允许 code-behind 贴近 Windows 版。落地包版本 11.3.2（Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent / Avalonia.Headless；Diagnostics 仅 Debug 引入）。
- TFM：net8.0（App、Core、Tests 统一；如 Core 遇 API 缺口可降 netstandard2.0，由 A4 报备）。
- 版本号冻结 0.1.0-m1（csproj `Version` 已写入）。

## D-2 · Core 复用策略（GOAL §2，A1 登记）

- 从 Windows 版 `src/TipCL/Core/` 与 `src/TipCL/Services/` **复制**可跨平台文件进 `src/TipCL.Core/`，文件头注释注明来源路径。
- Windows 专属 API（注册表、WPF 类型、Win32）不搬；缺口用 `#if WINDOWS_DESKTOP` 条件编译或桩实现，由 A4 在 PROGRESS 记录。
- 复用清单交付物见 GOAL §4.4。

## D-3 · 方案与工程布局（A1 登记）

- `TipCL.Linux.sln`（经典 .sln 格式，非 slnx；SDK 10 的 `dotnet new sln` 默认生成 slnx，已用 `--format sln` 重造）。
- 三工程：`src/TipCL.App`（WinExe，Avalonia 主工程）、`src/TipCL.Core`（类库）、`tests/TipCL.Tests`（xunit）。三者均已预登记入 sln 并配了最小可编译骨架，A4/A5 在各自 csproj 与目录内充实，不与 sln 冲突（sln 编辑权归 A1）。
- csproj 一律默认 glob：新 .cs/.axaml 自动编入，后续代理不碰 csproj（A4/A5 各自新建/维护的除外，见 GOAL §5）。

## D-4 · 冒烟方案（A1 登记，A5 充实）

- `--smoke`：Avalonia.Headless 无头平台启动 → `MainWindow.CaptureRenderedFrame()` → 存 `smoke.png` 退出。骨架在 `src/TipCL.App/Session/SmokeRunner.cs`，输出路径/退出码约定由 A5 定稿。

## D-5 · 字体（GOAL §2，A1 登记）

- 不嵌字体；窗口 FontFamily 系统回退 `"Microsoft YaHei UI, Noto Sans CJK SC, WenQuanYi Micro Hei, sans-serif"`，当前在 App.axaml 临时样式内，A2 主题库就位后可迁移至 Themes/。

### D-6 · M1 集成期修复（主 agent 验收，2026-11-08）
- **ResourceInclude 运行时挂载**：Avalonia 11.3 的 `ResourceInclude(Uri)` 构造参数是 baseUri 而非 Source，运行时重挂主题字典必须写 `new ResourceInclude((Uri?)null){ Source = new Uri(...) }`（MainWindow.SetDark）。
- **跨字典 StaticResource 在运行时重挂不可靠**：A3 的 ShellTokens 转发层（StaticResource 转发 A2 的 B.* 键）在 SetDark 重挂时抛 KeyNotFoundException，DynamicResource 别名又会让消费者拿到扩展对象——最终删除转发层，外壳直接引用 B.* 键（全部 DynamicResource，明暗切换即时生效），ShellTokens.*.axaml 删除。
- **模板 ContentPresenter 必须显式 `Content="{TemplateBinding Content}"`**（WPF 隐式绑定在 Avalonia 不存在）：Shell 4 处 + A2 Controls.axaml 6 处补齐。曾导致全部按钮内容不渲染（探针显示 ContentPresenter cls=[:empty]）。
- 冒烟增强：`SMOKE_DARK=1` 深色帧、`SMOKE_PROBE=1` 可视化树探针（含每元素 classes 与 bounds）。
- 冒烟截图 `smoke.png`/`smoke-dark.png` 与 `_screen.png` 不入库（.gitignore 由本次提交补充）。
