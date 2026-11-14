# TipCL (macOS 版)

把 Windows 上的 TipCL(Minecraft 启动器)移植到 macOS。

- **Windows 原版**:`D:\项目\tipcl\windos\tipcl` —— WPF / .NET Framework 4.8,**只读基线**(本仓库不含它,HEAD `803dd38`)。
- **本仓库**:`D:\项目\tipcl\mac` —— **Avalonia 11.3.23 + .NET 8**(`net8.0`)。UI 层整体重写,逻辑层(Core)整体移植。

## 目录

| 路径 | 内容 |
|---|---|
| `src/TipCL.App` | Avalonia 应用(程序集名 `TipCL`):外壳、导航、页面、服务门面、主题色板与样式表、图标 |
| `src/TipCL.Core` | 逻辑层(启动器 / 安装 / Java 运行时 / 插件 / 同步 / 版本管理等)。对 Windows 源:238 个文件有对应,其中 **216 个逐字节一致**,22 个因平台差异有改动,10 个是 mac 侧新增 |
| `tests/TipCL.Tests` | xunit 测试 + 一个**自带的进程内执行器**(两条通道都必须跑,见下) |
| `tools/TipCL.Packager` | 打包器:组装 `.app`、`Info.plist`、`.icns`、确定性 `tar.gz`,并校验 Mach-O / 权限位 |
| `tools/package.ps1` | 一键出包(发布两个架构 → 打包 → 校验) |
| `tools/with-build-lock.ps1` | **多个代理/终端共用本仓库时必须用它包住 `dotnet` 命令**(见"并发构建") |
| `GOAL.md` | 任务书:目标、红线、技术选型、路线图 A–D |
| `PROGRESS.md` | 逐包进度 + 每包末尾的 `### Lead 验收`(含原始数字与裁决) |
| `DECISIONS.md` | 决策记录 `D-01`…(每条都写"为什么",含被否掉的方案) |
| `docs/MAC-VERIFY.md` | **真机验收清单** —— 本机是 Windows,macOS 相关的一切都要在那里过一遍 |

## 构建与测试

```powershell
dotnet build TipCL.Mac.sln -c Release
```

两条测试通道**都要绿**(单看一条证明不了什么 —— 两条通道的调度、时序、并行度都不同,本项目已经两次出现"进程内绿、VSTest 红"):

```powershell
# 通道 1:VSTest
dotnet test TipCL.Mac.sln -c Release --no-build

# 通道 2:进程内执行器(按声明顺序跑,自动释放 IDisposable/IAsyncLifetime)
dotnet exec tests\TipCL.Tests\bin\Release\net8.0\TipCL.Tests.dll
```

验收标准(每个工作包都按这个标准收):构建 **0 错误**;上述两条通道**各连跑 2 次**全部通过、总数相等、"不支持 0";`--smoke` 的既有帧**逐字节不变**。

测试隔离:测试启动时把 `TIPCL_HOME` 与 `Utils.Root` 重定向到 `bin\...\test-home\<guid>`。**任何测试都不许写 `%LOCALAPPDATA%\TipCL`、桌面、图片目录**,测试临时文件一律落在这个隔离根下。

## 冒烟(离屏渲染)

```powershell
src\TipCL.App\bin\Release\net8.0\TipCL.exe --smoke [--page <id>] [--theme light|dark] [--out x.png]
```

退出码:`0` 正常 / `1` 渲染失败 / `2` **参数错误**(例如 `--theme` 给了 `light`、`dark` 以外的值)。

Avalonia.Headless + Skia 离屏渲染,900×550。**注意离屏时后端不会自动启动**(`BridgeReady` 为假),所以页面截图是**载入前状态** —— 这是设计如此,不是 bug。

已确立的基准(改动像素就要在提交说明和 `PROGRESS.md` 里写明画了什么、为什么):

| `--page` | light(字节 / SHA256) | dark(字节 / SHA256) |
|---|---|---|
| 不带(= 首屏,启动页) | 37681 `2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD` | 33579 `7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893` |
| `launch` | 同首屏(同一帧) | 同首屏 dark |
| `library` | 43775 `8FBA4C9FD03199F3DA2A83E06CA36B72BFF9711B017125C2F35E70817DD3FF8E` | 40686 `A77F3E6A287A17787023E40CC725AA52F017579CC68152939407EDE427F9202D` |
| `instance` | 44163 `EBB237FB4E20BDDB22B7223C6C3FDB47F1CDF64DE95D86A1D3CB92DBAEB68889` | 40739 `74ADDEC9EB438181BE03DDCA1212E05CFDA7A3B2047646491624CCF9904AA298` |
| `plugins` | 25115 `A48E426D29D421A4F58AA463368453634287F014DF73C5E8929E5AA997FA9BF2` | 21410 `95E2BF47A5F35DA6617D797A06EDB57F6D916D8A21536A15AFE8E25E2AFEE9E7` |
| `settings` | 45594 `C19BF1C465C95116A0BBC7B4F09DF55B00CFB588FB8D258EE2C40F68C8DD4ECF` | 43745 `CE33BEFE6841EDB5B38A564EF203F7BF8D61BFCEA5FC0BE3A41208CF70465691` |
| `tasks` | 27719 `0E591B87A2B2B9E3CAC7968F1BBFD399DF9A704657506CFE38B3A5EB4E5590E9` | 24171 `C3EE486EB65766D1A5D59B040ACA6B6AE0E102438A5FC1B0B89203FC42F36B72` |
| `account` | 33173 `D3F593398D737BE9512A35D4AEBB080AB87379C6EC86C52139A8426E9253BAF6` | 29984 `148EB2066C1009D5CB28D78169605A4311DCC62627DB615AD6661F0274794B88` |
| `download` | 30372 `9DDD2E143C52FB5E3647197742F2E6C1D7331EB5B767209CE61FA12E9B582AAB` | 26725 `D602CC36868978B8E5A3DD0420B84846CA3C304B539D98A929C396359367E1B1` |
| `multiplayer` | 36078 `95F5DC9F9B7C9CFAC10D2F3E47BCB238E88ADF8AEA4652B38FAA02EE99A7B1DB` | 32559 `A6B1AF5D9E4B6FAC892423150EFB5DF7392CD3D957F1EBAB31ECCE1E10CF9B13` |
| 尚未移植的 id(如 `plugins`、`ai`) | 与首屏同帧(没有注册工厂 ⇒ `Navigate` 静默不动) | 同左 |
| `--theme blue`(非法) | 退出码 **2**,不产出文件 | — |

以上每一帧都**复跑过一次逐字节相同**(确定性成立)。历史:空壳阶段 light 21823 / dark 18200;启动页落地后变为 37681 / 33579;实例库与实例页落地后新增两帧;**C5c 修掉两处窄页面布局缺陷后,`library` 与 `instance` 两帧合法变化**(由 42417/39268 与 27484/24511 变为上表值),其余帧**逐字节未动**。新页面落地时请把新帧补进这张表。
历史:空壳阶段是 light 21823 / dark 18200;启动页落地后变为上表(空壳第一次有真实内容)。

### 离屏帧解读须知(不知道这三条会误判)

1. **没有后端**:`--smoke` 不启动后端(`BridgeReady` 为假),页面停在**载入前状态**(D-96)。所以帧里有骨架、没有数据。
2. **列表行不落地**:离屏时列表的容器(`ListBoxItem`)会建出来,但**容器是空的** —— 行模板不实例化,行里的文案既不在可视树也不在逻辑树(实测钉住)。后果是:**任何列表页在离屏帧里只有"计数 / 空态",看不到行**。真机不受影响。
3. **分段是切段才建**:设置页这类"分段式"页面,未切到的分段内容不在帧里;所以 `settings` 帧不变**并不代表**分段接线失效 —— **接线只有用例能证明**。

**结论:离屏帧证明的是"外壳 + 页面骨架 + 主题色正确",不是"页面内容正确"。** 内容正确性靠两条测试通道,观感与真机行为靠 `docs/MAC-VERIFY.md`。
## 出包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
```

流程:停构建服务 → 发布 `osx-arm64` 与 `osx-x64`(self-contained)→ 由 `tools\TipCL.Packager` 组装 → 校验。

产物:`dist\TipCL-macos-universal.tar.gz`(通用二进制:两个架构各一份 `TipCL.app`,另含 6 个通用 dylib 的两个切片;USTAR 格式、固定 mtime `2000-01-01`、按序排序 ⇒ **两次打包逐字节相同**)。

上次已知产物:`85812352` 字节 / 443 个条目 / SHA256 `66802328B2B2279E4CADB13362A0705899C54070E3F2DB43B097E5F538C06CDC`。

**产物是未签名的**,用户侧首次运行需要:

```bash
xattr -cr TipCL.app
open TipCL.app
```

签名 / 公证 / 打 dmg 都需要在 macOS 上做,清单见 `docs/MAC-VERIFY.md`。

## 数据与平台差异(mac 侧)

- 数据目录 `~/.tipcl`(Windows 是 `%APPDATA%\TipCL` 等)。
- 插件凭据走 **Keychain**(`/usr/bin/security`),Windows 走凭据管理器。
- **平台键**:Mojang java-runtime 用 `mac-os` / `mac-os-arm64`。
- **目标架构**:Apple 芯片上跑旧版本时,由 `LaunchArch.For(mergedJson)` 决定本次启动的目标架构(x64 仅在该版本只有 x64 natives 时),并驱动 natives / 规则匹配 / classpath。
- **Rosetta 只提示不代跑**:需要时提示用户执行 `softwareupdate --install-rosetta`,**不会**用 `arch -x86_64` 代跑。
- **自更新在非 Windows 上只做提示**(`can_self_update`),不做自我替换。
- 主题色板:`Themes\Palette.Light.axaml` / `Palette.Dark.axaml`,各 61 个键,键名与取值对齐 Windows 的 `Themes\Palette.*.xaml`。

## 并发构建(多代理 / 多终端)

**两个 `dotnet` 进程同时构建或测试本解决方案会互相踩 `obj/bin`** —— 本项目已经因此产出过一次"115 条假失败"。所以共用本仓库时:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 `
    -What "dotnet test TipCL.Mac.sln -c Release --no-build --nologo" -TimeoutSec 1800
```

它内部用命名互斥量串行化,退出码透传;拿不到锁时**退出码 3**。文档追加(`PROGRESS.md`)可以复用同一脚本、换一个互斥量名:`-Name TipCLMacDocs`。

顺带一条经验:**并发负载会让"按固定轮数泵"的异步测试偶发假红**。遇到不属于自己范围的失败,先单独 `--filter` 复跑;复跑绿了就是并发干扰,不要因此放宽断言。

## 红线(改代码前先看)

1. **只在 `D:\项目\tipcl\mac` 内写文件**。`..\windos\tipcl` 是只读基线。
2. **绝不读取/搜索/引用 `..\linux\` 或 `..\handoff-*`**(那是别的产品线/会话的产物)。
3. 测试**不许**实例化 `BackendApi` / `BridgeServer` / `PluginManager`(构造函数会预热 `login.live.com`);不许联网、不许起外部进程、不许真开文件选择器或启动游戏。
4. 触碰 Avalonia 的用例体一律走 `AvaloniaSession.Run(...)`(专用 Dispatcher 线程会话,见 `tests\TipCL.Tests\AvaloniaSession.cs`);**不要**再加第二处 `SetupWithoutStarting`。
5. 测试不许用 `HttpListener`(VSTest 宿主里起不来);需要回环服务用 `TcpListener` + `127.0.0.1:0`。
6. 不许删测试、不许放宽断言、不许加 `Skip`、不许放宽 `NoWarn`、不许改 TFM。
7. 含中文的文件**只用编辑工具改**,**绝不要** `Get-Content | Set-Content`(会写出 BOM/CRLF 并把字符改坏 —— 本项目已踩三次)。
8. 不 `git push`、不打 tag、不改全局 git 配置。

## 当前进度

见 `PROGRESS.md` 顶部的状态表。已验收的包:`A1–A6`、`B1a–B3`(含可复现的通用 `.app` 出包)、`C1a/C1b/C1b-fix/C1c`(主题 / 样式表 / `Ui` 门面)、`C2a–C2d`(桥层、外壳、Toast 与对话框卡片、页面级视觉服务、**进程内后端真实启动验证**)、`C3a–C3e`(块/仪表盘框架、启动页依赖的两个对话框、**启动页**、外壳 `FlyToTasks`、`Motion` 变换动画修复)。

测试数:1796(包刚起)→ **3352**(2026-11-14,C5 在途)。

**仍然没有验证的**:一切 macOS 真机行为。清单见 `docs/MAC-VERIFY.md`。
