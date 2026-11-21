# AI3-linux-real · 在真 Linux(WSL2)上验证

分支 `ai3/linux-real`(基于 main@b6df4bb),worktree `D:\项目\TipCL\wt-ai3-linux-real`。

## 1. 环境探测(只读)

- `wsl.exe` 2.7.14.0,内核 6.18.33.2-2,WSLg 1.0.73.2;无 docker。
- **`wsl -l -v` / `wsl --status` / `wsl -d <名字>` 全部报错**:
  `未能从分发内容 DistributionName 读取属性"{da4fc5ad-06df-41b0-a42e-f2a917471ae3}"`,
  错误代码 `Wsl/EnumerateDistros/Service/ReadDistroConfig/E_UNEXPECTED`。
  原因(只读查注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss`):有一条**没装完的残留登记**
  `{da4fc5ad-…}`(State=3、没有 DistributionName,目录里只有 12 MB 的 ext4.vhdx,2026-11-15),
  WSL 枚举发行版时读它失败,于是所有「按名字」的命令都挂。
- 真正已装好的是 **Ubuntu-24.04**(`{76890ffd-c3ad-4515-b04e-35dd6ebf4515}`,State=1,WSL2,默认发行版)。
  **按 GUID 调用可以绕过**:`wsl.exe --distribution-id {76890ffd-…} --exec /bin/bash …` 正常。
- 按任务书要求**没有动注册表、没有 unregister、没有装新发行版、没改任何系统设置**。
  建议用户自己处理那条残留(例如删掉 `HKCU\…\Lxss\{da4fc5ad-06df-41b0-a42e-f2a917471ae3}` 键及其空目录),
  之后 `wsl -l -v` 就会恢复。
- WSL 里:Ubuntu 24.04.5 LTS x86_64,用户 `ubuntu`;**没有** dotnet、lspci(pciutils)、xdg-open;
  curl / wget / git / rsync 有;WSLg 提供了 `DISPLAY=:0`、`WAYLAND_DISPLAY=wayland-0`。

## 2. 防弹窗措施

- 所有 Windows 侧进程 `-WindowStyle Hidden`,长命令 `Start-Process … -WindowStyle Hidden` 放后台写日志。
- WSL 里每条命令都经 `run.sh`:**清掉 `DISPLAY` / `WAYLAND_DISPLAY`**(否则 WSLg 会把 Linux 窗口画到 Windows 桌面上),
  **把 `/mnt/*` 的 Windows 互操作目录从 PATH 去掉**(避免任何 Windows 程序被从 WSL 拉起)。
- PATH 最前面放一个**只记录参数的假 `xdg-open`**(另有同款假 `open`);全量测试期间它被调用 **0 次**。
- 全程没启动 TipCL 界面、没启动 Minecraft。

## 3. WSL 里装 .NET 8 SDK

- 只用微软官方 `https://dot.net/v1/dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet --no-path`,无 sudo、不改系统包。
- 结果:SDK **8.0.428**,运行时 Microsoft.NETCore.App 8.0.34(系统自带 libicu74)。
- 坑:第一次从 `builds.dotnet.microsoft.com` 拉 217 MB 包时那条连接被限到 ~3 KB/s(新连接实测 14 MB/s),
  杀掉重下就好;安装脚本里给 curl 包了一层 `--speed-limit 300000 --speed-time 20`,让卡住的连接由脚本自己的 `--retry` 重试。

## 4. 构建 / 测试 / 自检(WSL 与 Windows 两边分别实测)

worktree 用 rsync 拷到 WSL 自己的 ext4(`~/tipcl-ai3`,排除 `.git`、`bin/`、`obj/`),不在 `/mnt/d` 上构建。

| 项 | 基线 b6df4bb · Windows | 基线 b6df4bb · WSL2 | 最终 HEAD · Windows | 最终 HEAD · WSL2 |
|---|---|---|---|---|
| `dotnet build TipCL.Linux.sln -c Debug --no-incremental` | exit 0,3 条基线 xUnit 警告 | exit 0,同样 3 条 | exit 0,3 条(同基线) | exit 0,3 条(同基线) |
| 全量测试 | 1358 过 / 7 跳 / 0 失败(1365) | **1358 / 7 / 0(1365)** | **1366 / 7 / 0(1373)** | **1366 / 7 / 0(1373)** |
| `--ai-selftest` | exit 0,108 项 | exit 0,108 项 | exit 0,108 项 | exit 0,108 项 |
| 测试期间 xdg-open 被调用 | — | 0 次 | — | 0 次 |

- 7 个跳过两边相同,都是仓库里写死的 `[Fact(Skip=…)]`(5 个 CliSmokeTests、1 个 LayoutMotionAndHeightTests、1 个 SmokeIntegrationTests),
  **没有因为缺显示服务器而额外跳过或失败的**:界面测试走 Avalonia.Headless,不需要 X / Wayland。
- WSL 上全量测试 1 分 10 秒左右(Windows 视并行负载 1~8 分钟)。
- 最终 HEAD 两边的数字在 §8。

## 5. 硬件探测核对(`SystemInfo.Snapshot()` vs /proc、lspci、nvidia-smi)

探针 `tools/linux-real/probe`(独立小程序,引用 App 工程)在 WSL 里打印快照并逐项核对:

| 字段 | SystemInfo | 参照 | 结果 |
|---|---|---|---|
| cpu.name | AMD Ryzen 9 7845HX with Radeon Graphics | /proc/cpuinfo `model name`、lscpu | 一致 |
| cpu.cores_logical | 24 | /proc/cpuinfo processor 数 24、nproc 24 | 一致 |
| cpu.cores_physical | 12 | (physical id, core id) 去重 12;lscpu 12 核 × 1 插槽 | 一致 |
| total_mb | 11561 | MemTotal 11839156 kB / 1024 | 一致 |
| avail_mb | ≈10740 | MemAvailable(随时间浮动,±512 MB 内) | 一致 |
| memory.source / os | /proc/meminfo · Linux · Ubuntu 24.04.5 LTS | — | 一致 |
| 显卡(修复前,无 lspci) | **空**(`/sys/class/drm` 只有 version,没有 card*) | nvidia-smi:RTX 5060 Laptop GPU 8151 MiB | **缺陷** |
| 显卡(修复前,有 lspci) | 2 × `Microsoft Corporation Basic Render Driver`,vram 0 | lspci:两个 GPU-PV 占位 3D controller | **缺陷**(见下) |
| 显卡(修复后,无 lspci) | `NVIDIA GeForce RTX 5060 Laptop GPU` 8151 MB | nvidia-smi | 一致 |
| 显卡(修复后,有 lspci) | RTX 5060 8151 MB + 1 个 Basic Render 占位 | lspci + nvidia-smi | 一致 |
| 显卡(修复后,PATH 上没有 /usr/lib/wsl/lib) | 同上,走绝对路径兜底 | — | 一致 |

(lspci 来自 `apt-get download pciutils libpci3 pci.ids` + `dpkg-deb -x` 解到 `~/pciroot`,**没安装、没 sudo**,只在探针运行时临时放进 PATH。
Windows 侧参照:`Win32_VideoController` = RTX 5060 Laptop GPU + AMD Radeon 610M + 3 个虚拟显示适配器;
WSL 里 Radeon 610M 只以 Basic Render 占位出现,Linux 侧没有接口能拿到它的真名,如实保留。)

## 6. xdg-open 路径(假 xdg-open,只记录 argv,不真打开)

探针把 `Crash.ShellOpenHook` / `Auth.OpenBrowserHook` / `ShellOpen.Hook` 置空后逐个调用,假 xdg-open 按
`argc\0argv…\0` 记录,逐条比对:

- `Crash.ShellOpen`、`ShellOpen.Folder`:中文 + 空格目录、`a "quoted" name`、结尾反斜杠、`back\\slash\" mix`、制表符、换行、
  `$HOME;\`id\`&&|*?'single'`、`-n`、空串 —— **全部 argc=1 且原样到达**(`Utils.List2CmdLine` 与 .NET 在 Linux 上的拆参规则互逆,不经 shell)。
- `ShellOpen.Url` / `TryUrl`:带空格、`&`、中文、`#` 的 URL 原样到达。
- `Crash.OpenPath`:存在的目录 → true 且调用一次;不存在 → false 且不调用。
- `Auth.OpenBrowser`:**修复前在 Linux 上一次都不调用**(缺陷,见下);修复后调用一次、argv 正确。
- 共 21 次调用 = 期望 21 次,无多余调用。

## 7. 发现并修掉的 Linux 专属缺陷

1. **显卡显存 / WSL2 真实独显探测不到**(`SystemInfo.Gpus`):NVIDIA 专有驱动下 lspci、/sys、/proc 都没有显存(vram 恒 0);
   WSL2 里 lspci 只有 GPU-PV 占位,真实独显只有 `/usr/lib/wsl/lib/nvidia-smi` 看得到。
   → 新增纯函数 `SystemInfo.ParseNvidiaSmi`、`IsWslPlaceholderGpu`,`MergeLinuxGpus` 加 4 参重载叠加 nvidia-smi
   (按顺序并入 NVIDIA 条目的型号 / 显存 / `driver_version`;多出来的先顶替 WSL 占位、再追加);原 3 参重载行为不变。
   `Gpus()` 先找 PATH 上的 nvidia-smi,再找 `/usr/lib/wsl/lib/nvidia-smi`;起不来立刻返回,不等超时。
   `VendorFromName` 认出 Microsoft。
2. **WSL 占位设备被当成独显**(`ShaderAdvisor.IsIntegrated`):lspci 拼出的名字是「Microsoft Corporation Basic Render Driver」,
   原来只匹配「microsoft basic」,于是按「独显 + 常规内存 → 中配」推荐光影。→ 加「basic render」。
3. **微软登录页在 Linux / macOS 上从不自动打开**(`Auth.OpenBrowser`,与 Windows 参考版同样只有 ShellExecute 一支):
   Windows 上设备码登录会自动弹浏览器,Linux 用户只能在登录框里手动点「打开浏览器」。
   → 抽出纯函数 `Auth.BrowserStartInfo(url, windows, mac)`:只认 http(s);Windows 照旧 ShellExecute,
   macOS `open`、其余 `xdg-open`,地址经 `ArgumentList` 作为唯一参数传入(不经 shell)。测试钩子优先级不变。
   (仍在 `MicrosoftAuthenticator.Login` 的 try/catch 里:没有 xdg-open 的无头机器上只是不打开。)

没发现需要改但改不了的 Linux 缺陷。

## 8. 验收(最终 HEAD,见 DONE 文件)

见 `D:\项目\TipCL\_verify_ai3\linux-real\DONE`。在代码最后一次提交 **73fd39c** 上两边实测(之后只提交了本文档):
Windows 与 WSL2 两边 Debug `--no-incremental` 构建 0 错误、
只有 3 条基线 xUnit 警告;全量测试 1366 / 7 / 0(1373);`--ai-selftest` exit 0 · 108 项;
`git diff b6df4bb HEAD -- windos` 为空;git status 干净。

## 9. 文件清单

- `src/TipCL.Core/Launcher/SystemInfo.cs` —— nvidia-smi 解析与合并、WSL 占位识别、Microsoft 厂商、`NvidiaSmiText()`。
- `src/TipCL.Core/Launcher/Auth.cs` —— `OpenBrowser` 补 Linux/macOS,`BrowserStartInfo` 纯函数。
- `src/TipCL.Core/Launcher/Ai/NewFeatures/ShaderAdvisor.cs` —— `IsIntegrated` 加一个关键词(一行)。
- `tests/TipCL.Tests/Core/LinuxRealTests.cs` —— **新增 8 个测试**(nvidia-smi 解析、WSL 占位替换、原生 NVIDIA 合并、
  只有 nvidia-smi、占位算核显、BrowserStartInfo 三平台 + 非 http(s) 拒绝、OpenBrowser 只到钩子、Linux 上对照 /proc 只读核对)。
- `tools/linux-real/verify.sh`、`tools/linux-real/README.md`、`tools/linux-real/probe/{LinuxProbe.csproj,Program.cs}` ——
  可复跑的真 Linux 验证(不进 sln,不参与产品构建)。
- `progress/AI3-linux-real.md` —— 本文件。

## 10. 合并冲突点

- `SystemInfo.cs` 的 `Gpus()` 末行、`MergeLinuxGpus`(拆成重载 + 私有 `MergeLinuxGpusRaw`)、`VendorFromName`。
  别的包若也改显卡合并,以本包的 4 参重载为准合。
- `Auth.cs` 的 `OpenBrowser` 整段。
- `ShaderAdvisor.cs` 第 115 行一处关键词。
- 不碰 i18n 文件、ToolMeta、ToolRunner、界面。

## 11. 建议改的文档行(本包不改,留给集成)

- `docs/PARITY.md` §11(或新 §12)加一条:「Linux 真机验证:WSL2 Ubuntu 24.04 + .NET 8.0.428 上 Debug 构建 / 全量测试
  1366/7/0 / `--ai-selftest` 108 项通过;显卡探测合并 nvidia-smi(含 WSL2);微软登录页 Linux/macOS 自动打开。复跑:`tools/linux-real/verify.sh`。」
- `PROGRESS.md` 本轮小节记上面同样的数字。
- `README.md` 的 Linux 说明可补一句:「装了 NVIDIA 专有驱动时,显卡显存从 nvidia-smi 读取」(可选)。

## 12. 遗留

- 这台机器 WSL 有一条残留发行版登记导致按名字的 `wsl` 命令全挂(§1),需用户自行清理;本包只用 `--distribution-id` 绕过。
- WSL2 里核显(Radeon 610M)只能显示为 Basic Render 占位;AMD/Intel 独显在 WSL2 里同样拿不到型号(没有 nvidia-smi 的对等物)。
- 真 X11/Wayland 桌面下的 xdg-open 行为(由桌面环境决定打开哪个程序)没测 —— 按要求不真打开;参数传递已逐字核对。
- 没在原生(非 WSL)Linux、没在 ARM、没在 macOS 上跑;原生 NVIDIA 的合并逻辑只有单元测试覆盖。
