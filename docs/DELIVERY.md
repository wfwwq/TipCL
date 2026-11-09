# TipCL Linux 版交付说明（M3-C2）

版本 `0.1.0-m3`（TipCL.App.csproj）。产物均为 **self-contained 单文件**，目标机**无需安装 .NET 运行时**。

## 一、交付物清单

打包入口：Windows `tools/package.ps1`，Linux `tools/package.sh`。

| 产物 | 路径 | 实测体积 |
|---|---|---|
| Linux x64 单文件 | `dist/TipCL-linux-x64/TipCL.App` | 42.5 MB（72 MB 原始 + 11 MB 原生库，压缩后） |
| Windows x64 单文件 | `dist/TipCL-win-x64/TipCL.App.exe` | 43.3 MB |
| AppImage | `dist/TipCL-linux-x64.AppImage` | 由 linuxdeploy 产出（见下） |

注：`dist/` 另有对应 `.zip` 压缩包（package.ps1 产出）；发布目录中不再携带 pdb（脚本已剔除）。

### AppImage

- 结构模板与约定在 `tools/AppImage/`：`tipcl.desktop`（桌面项）、`tipcl.png`（256×256 图标，按主题令牌 C.Accent #2E9B6B 生成的圆角"T"字标）。
- `tools/package.sh` 在 Linux 上自动组装 `dist/TipCL.AppDir`（AppRun + usr/bin/TipCL + desktop + hicolor 图标）；本机装有 linuxdeploy 时直接出 `.AppImage`，没装时打印现成命令（AppDir 已就绪，补一条 linuxdeploy 即可）。

## 二、发布参数（双端同源）

```
dotnet publish src/TipCL.App -c Release -r <RID> --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

- RID：`linux-x64` / `win-x64`。
- `IncludeNativeLibrariesForSelfExtract`：SkiaSharp/HarfBuzzSharp 原生库并入单文件，否则散落在 exe 旁（实测 linux-x64 会多出 `libSkiaSharp.so` 8.9 MB、`libHarfBuzzSharp.so` 2.4 MB）。
- 全部参数经命令行传递，**未改 src/ 下任何 csproj**（仅 TipCL.App.csproj 的 `<Version>` 一行升为 `0.1.0-m3`，属任务书允许项）。

### Trimming 取舍：**不 trim**

试过 `-p:PublishTrimmed=true -p:TrimMode=partial`，编译能过但产生成片 IL2026/IL2104 警告，均为真实运行时风险：

- `TipCL.Core` 的 `System.Text.Json` 反射序列化路径（`Layout/PageBlock.cs`、`Layout/PageLayoutStore.cs`、`Launcher/Sync/PhoneLink.cs` 等）——trim 后布局存档/同步会静默丢字段或抛错；
- `Avalonia.DesignerSupport` IL2104——Avalonia 11 官方亦不建议盲目 trim。

且 trim 后行为无法在本机（Windows 交叉编译）验证 Linux 端运行时表现。结论：M3 交付不 trim，换取确定性；体积红线无压力（任务书明示 Linux 无体积红线）。未来若要 trim，需先把 Core 的 JSON 序列化迁到 `JsonSerializerContext`（source-gen）。

## 三、运行方式

### Linux（单文件）

```bash
chmod +x TipCL.App
./TipCL.App            # 直接跑，self-contained 无需 .NET
```

依赖说明：

- self-contained，**无需安装 .NET**；
- GUI 依赖系统库：libX11、libxcb、fontconfig/freetype、libGL（Avalonia/Skia 标准依赖，主流桌面发行版默认具备）；
- 字体不内嵌，按回退链取系统 CJK 字体（Noto Sans CJK SC / WenQuanYi 等，见 GOAL §2）；
- AppImage 双击/`./TipCL-linux-x64.AppImage` 均可（AppRun 已写好）。

### Windows（单文件）

双击 `TipCL.App.exe` 或命令行运行，无需 .NET。

## 四、双端冒烟矩阵

冒烟入口：`--smoke`（无头渲染截图 `smoke.png` 后退出，退出码 0=成功）。页面选择用环境变量 `SMOKE_PAGE`，深色用 `SMOKE_DARK=1`。

七项矩阵（页面键与 `NavRegistry`/`MainWindow.GetPage` 对应）：

| # | 命令 | 页面 | 类型 | Windows 结果 |
|---|---|---|---|---|
| 1 | `TipCL --smoke` | launch 启动页 | 真实页（M2-B1） | 通过（单文件 exe 实跑，exit 0） |
| 2 | `SMOKE_PAGE=library TipCL --smoke` | 实例库 | 真实页（M2-B2） | 通过（M2 验收，见 PROGRESS.md） |
| 3 | `SMOKE_PAGE=download TipCL --smoke` | 下载 | 真实页（M2-B3） | 通过（M2 验收） |
| 4 | `SMOKE_PAGE=plugins TipCL --smoke` | 插件 | 真实页（M2-B4） | 通过（M2 验收） |
| 5 | `SMOKE_PAGE=settings TipCL --smoke` | 设置 | 真实页（M2-B5） | 通过（M2 验收） |
| 6 | `SMOKE_PAGE=dashboard TipCL --smoke` | 仪表盘 | 占位页（M1-A3） | 通过（M1 验收） |
| 7 | `SMOKE_DARK=1 SMOKE_PAGE=settings TipCL --smoke` | 设置·深色 | 深色主题 | 通过（M2 验收） |

- Linux 产物在本机（Windows 交叉编译）**无法运行**，已验证产物存在、结构完整（单文件、无外置原生库/pdb）；Linux 端七项矩阵须在真机执行后回填（M3-C3 总验收）。
- `tools/package.ps1` 收尾自动跑第 1 项作为 Windows 产物出包冒烟门禁。

## 五、已知限制

1. **插件 WebView 降级**：插件宿主走进程内 PluginStateStore，WebView 面在 Linux 下降级（无 WebView2/WebKit 集成），当前状态见 progress/M3-C1.md（撰写时该包并行进行中，以其最新记录为准；M2-B4 报告的降级行为仍是基线）。
2. **AI / 启动链路**：AI 运行时最小落地与启动链（LaunchCore/Java 探测 Linux 分支）状态见 progress/M3-C0.md 与 progress/M3-C1.md（并行包，以最新记录为准）；M2 阶段启动按钮为占位反馈（PROGRESS.md M2 验收遗留项），M3-C0 落地后自动切换为真实链路。
3. **不 trim**（见上）：体积换取确定性；如需瘦身先做 JSON source-gen 改造。
4. **csproj 未加发布段**：单文件参数全部命令行传入；若后续希望 `dotnet publish -r linux-x64` 裸命令即出单文件，需要依赖申请在 App csproj 加 `<PublishSingleFile>` 等段。
5. AppImage 产出依赖 linuxdeploy（脚本不强制安装，AppDir 已按约定组装，缺工具时给出手工命令）。
6. 版本号 `0.1.0-m3` 只升在 TipCL.App.csproj；TipCL.Core.csproj 仍为 `0.1.0-m1`（非本包地盘，未动；tests/SmokeTests 断言 Core 版本 = m1，改动须连带测试，留 C3 裁决）。


---

## Linux 真机自检（M3-C3 补充，2026-11-09）

本机为 Windows，无 WSL/Docker，Linux 产物只做了结构验证（ELF + 真单文件 + 42.5 MB）。真机验证请在有 Linux 的机器上执行：

```bash
chmod +x tools/verify-linux.sh && tools/verify-linux.sh        # 默认 dist/linux-x64
```

脚本会检查：平台/ELF 头/真单文件 → 运行权限 → `--smoke` 出图 → 逐页冒烟矩阵（library/download/plugins/settings + 深色）→ fontconfig 与图形会话提示 → 输出截图目录供人工核对界面。

已在本机验证的部分：
- `dist/linux-x64/TipCL.App`：`7f 45 4c 46` ELF 头、无散落 `.so`、42.5 MB。
- `dist/win-x64/TipCL.App.exe --smoke`：exit 0，截图核对通过。
