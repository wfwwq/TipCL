# WP-S10：版本带回主页后实例选择框表现为"空实例"——名字语义线排查

日期：2026-11-07 · master=09c726d · 结论：**名字语义线无失配；症状根因是 S9 已在 09c726d 修复的填充竞态，另有 1 处静默吞错值得后续加固**。

## 证据链（每步实际值，本机实测 + 源码推演）

本机配置（%LOCALAPPDATA%\TipCL\config.json）：`instances_dir = E:\.minecraft`，`default_instance = ".minecraft"`；`E:\.minecraft\.instance.json` 存在（单根模式），`versions/` 下有 `26.3`、`fabric-loader-0.20.1-26.3` 两个有效版本目录（各自有 `<id>.json`）；无任何子实例目录。

1. 实例名来源唯一：LibraryPage/VersionManagePage/LaunchPage 三页的实例名全部取自 `get_instances` → `Instances.ListInstances()`（BackendApi.cs get_instances：`PyDict.Of("name", item, ...)`，item 即 ListInstances 原名）→ 本机为 `[".minecraft"]`。没有任何调用点传 Meta 别名或 "instance / version" 组合串（组合串只出现在不带 instance 参数的 get_installed_versions 返回值和 uninstall_version 的 spec 解析里）。
2. LibraryPage `BuildVerRow`：`inst = _folder`（src/TipCL/Pages/LibraryPage.cs:487）；`_folder` 由 LoadAsync 校准为 `_instances` 中的规范名（LibraryPage.cs:187-189）。双击 → `LaunchVerAsync(inst, id)`（:652）= `SetDefaultAsync(".minecraft")`（save_settings default_instance）→ `launchPage.PreferInstance(".minecraft", "26.3")`。VersionManagePage 同构（VersionManagePage.cs:507/760）。
3. save_settings 对 default_instance 仅 Strip、非空才写入（BackendApi.cs:4463-4469），不会改名/清空。
4. 启动页 `ApplyPreferenceAsync`（LaunchPage.cs:1302）：`_instances.All(i => i.Name != name)` 用同一份 get_instances 名单比对 ".minecraft" → 匹配 → ReloadVersionsAsync 传 `instance = ".minecraft"`。
5. 后端 `get_installed_versions{instance}`（BackendApi.cs:2396）→ `_instance(".minecraft")` → `Instance.ctor` → `Instances.ResolveName(".minecraft")`（Instances.cs:89）：`text == RootName()` → 返回 "" → 折叠回 `RootName()` = ".minecraft" → `GetInstancePath` → `E:\.minecraft` → `InstalledIds()` 扫 versions/ → 返回 2 个版本。
6. 与实例库右栏 `get_version_rows`（BackendApi.cs:2502，`_instance()`=default_instance=".minecraft"）走的是**同一个 Instance、同一个目录**，结果必然一致。hidden 过滤两边同源，右栏能显示说明两个版本均非 hidden，排除。
7. 名字折叠（ResolveName 把不在 ListInstances 里的名字静默折叠到根实例）是真实存在的潜在坑，但当前所有 UI 调用点都传规范名，**未被触发**。

## 根因结论

名字线干净。现象（浮层"这个实例还没有版本"、chips 无版本）只能由 `_ver.Items` 为空产生（LaunchPage.cs:988-990 RenderSwitchRows 直接读 `_ver.Items`），来源两条：
- **填充竞态**（主因）：`PreferInstance` 的偏好应用与首次 `FillCardData` 竞争——这正是 **S9 已在 09c726d 提交修复的内容**（"实例库双击版本竞态丢失（PreferInstance 等首次填充+版本偏好保留重试）"，2026-11-07 11:57）。用户复现所用构建早于该提交（今日 guard 日志 11:22-11:58 均为修复前会话；bin/Release/net48/TipCL.exe 时间戳 12:27 已含修复）。
- **静默吞错**（次要、未修）：`BridgeClient.TryCallAsync` 捕获一切异常返回 fallback（BridgeClient.cs:129-141），`ReloadVersionsAsync` 用它调 `get_installed_versions`——后端一旦抛错（如某版本 json 损坏导致 VersionSettings.Load 失败），UI 与"真空实例"完全不可区分。

## 修复

- 竞态：**已在 09c726d 落地（S9），无需重复修**；LaunchPage.cs 归 S9，本工单未改。
- 吞错加固（建议，≤15 行，落点 LaunchPage.ReloadVersionsAsync：把 `TryCallAsync` 换成 `CallAsync` + catch 区分错误与空列表，错误时 status 显示错误文案且不清空 `_ver.Items`）——因 LaunchPage.cs 归 S9，留待 S9 或下一工单，本工单未落。

## 产出

本文件；无代码改动、无 commit（除本文件）。
