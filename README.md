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
| `docs/布局使用指南.md` | **布局 v2(macOS 树)使用指南** —— 入口 / 四个标记 / 手势 / 分栏 / 导出导入 / `ui_layout_v2.json` 字段 / 故障排查 / 当前未覆盖范围 |

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

已确立的基准(**截至 `a4e76d7`,由 R32-MAC-BASE-REFRESH 在 `r32-mac-base` 上 `-t:Rebuild` 后实跑 `r32-capture.ps1 -Style mac -Batch mbase` 重刷**;改动像素就要在提交说明和 `PROGRESS.md` 里写明画了什么、为什么):

| `--page` | light(字节 / SHA256) | dark(字节 / SHA256) |
|---|---|---|
| 不带(= 首屏,启动页) | 37679 `52C715487D6A249DBCC6CFA0D048663E6FC1C30D221B3D981B69D1A25AB75C7D` | 33576 `B6115D6643CB1E51B110F6431CB50BC4A661BFBC0860239FA1A48C59908AF371` |
| `launch` | 同首屏(同一帧) | 同首屏 dark |
| `library` | 45577 `BA84D2EF69274416CA5F06E5F3A25476EB4FEF7DE764BA08BA9A3B3BD4E34A99` | 42261 `CD200EA9BBF1CA6577A51F0E1E162881C3B1DBC77852F487640A152BEEBEB5FD` |
| `instance` | 44186 `B46A9161B4C54D9E458A3AA1621CB977470DA93E932CFE69261CA006AF86164C` | 40798 `EEB6F7EC7C5F69E30EBC6C45E9D667B447546AA4714747237C00BA693A104275` |
| `plugins` | 25115 `A48E426D29D421A4F58AA463368453634287F014DF73C5E8929E5AA997FA9BF2` | 21410 `95E2BF47A5F35DA6617D797A06EDB57F6D916D8A21536A15AFE8E25E2AFEE9E7` |
| `settings` | 46034 `F4772F58E2C1273C28B50D15AEEC0AB5CA264D557A537386C26C0FF7C67162E5` | 44125 `EC9A55B87A0F76438D968C746D031A15CB746894429DC0537DEE91F888445C71` |
| `tasks` | 27719 `0E591B87A2B2B9E3CAC7968F1BBFD399DF9A704657506CFE38B3A5EB4E5590E9` | 24171 `B72F631FE3630A50216D9553313BFCFC7DD727FF4C971FE82651598CB01F71F2` |
| `account` | 33170 `79E1251282633EAF9A48472D56379D36F41461BE862034F5EAC6F7C9A82BAA9A` | 29984 `148EB2066C1009D5CB28D78169605A4311DCC62627DB615AD6661F0274794B88` |
| `download` | 30372 `9DDD2E143C52FB5E3647197742F2E6C1D7331EB5B767209CE61FA12E9B582AAB` | 26718 `CC2F336588CF1517C6D51DEF25973A50E8D19D26E70A621D4746F77201A532AB` |
| `multiplayer` | 35958 `83E9F228B2BE78CE96D786E10B6763F4347EC3CAFA81A006A762A8DA11426926` | 32395 `20F4CA584484CB0EB956E66731AA847375FDB96B7065E5568A853C34E267E320` |
| `ai`(**2026-11-14 M 轮注册后首次出真帧**) | 30904 `6746DB18F262AAF871CF2A5F247421068DCD1D7F14A8A685202EE40C311AAD06` | 30488 `86960B982DB1A79E64FF9902CA50FD617B3766DE5DDB5B4630BEFF986D205DD6` |
| `--theme blue`(非法) | 退出码 **2**,不产出文件 | — |

以上每一帧都**复跑过一次逐字节相同**(确定性成立)。历史:空壳阶段 light 21823 / dark 18200;启动页落地后变为 37681 / 33579;实例库与实例页落地后新增两帧;**C5c 修掉两处窄页面布局缺陷后,`library` 与 `instance` 两帧合法变化**(由 42417/39268 与 27484/24511 变为 43775/40686 与 44163/40739);**2026-11-14 M 轮续做后又有 4 帧合法变化**:`library` 变为 43810 / 40699、`instance` 变为 44186 / 40798、`settings` 变为 45632 / 43723、`multiplayer` 变为 35958 / 32396,并且 `ai` 在 `247db6d` 注册后**首次出真帧**(30854 / 30431)。**这些变化不是渲染缺陷**:该轮把这几页的**用户可见字面量**改回 Windows 字节(`888fbbb` 改 `SettingsPage.cs` 11 处 + `DropVeil.cs` 1 处,`6894810`/`22ceb1e` 覆盖其余 `Pages\*.cs` / `NavModel.cs`)⇒ 文本重排 ⇒ 像素变;所有帧都 `exit=0` / `issues=0`。**其余 5 行(首屏 / `launch`、`account`、`tasks`、`download`、`plugins`)逐字节未动**(已用完整 SHA256 复核)。**哪一处字面量造成哪一页的差未逐提交二分**(共享树不允许 checkout),如实标注。`--ai-seed` 的两帧(旧注 43130 / 42725,现已过期 —— 现值为 **44517 / 44218**,变化原因见下节)是带种子的额外证据,**不作为本表条目**。原「尚未移植的 id(如 `plugins`、`ai`)与首屏同帧」一行已**作废**:`ai` 注册后 10 个 nav id 全部有工厂,只有**未注册的 id**(例如拼错的那个)才会静默不切页。新页面落地时请把新帧补进这张表。
历史:空壳阶段是 light 21823 / dark 18200;启动页落地后变为上表(空壳第一次有真实内容)。

### 纪律注记(读这张基线表前先看三条,不然会误判)

1. **表行数 ≠ 帧级真值**。上表按页分行(`launch` 与"不带页参数"是同一帧),**帧级要按「每页 × 明暗」计**。
   R32 的**首次跳变**(`59f2d27`「BlockHost → LayoutEditor 薄壳」)实测是 **8 帧 / 152 px**
   (`progress/R32-MACFRAME.md` / `R32-MACFRAME-ACCEPT.md`):5 帧是**亚像素 AA**(1–15 px、通道差 ≤2),
   `settings` 两主题是**新出现的滚动条滑块**(方向上是"溢出内容变得可滚"的修复)。
   **不要把"表里对不上的行数"当成"只有那几帧变了"** —— 也不要把 `59f2d27` 那一笔的 152 px 与后来几笔的漂移混算。
2. **`TipCL.dll` 的 SHA256 不可跨树 / 跨构建比较**(R32-D34)。该 dll 的 `RSDS` 调试目录段**内嵌了绝对 PDB 路径**
   (含构建树目录名),所以换一棵**目录名不同**的树重编,`TipCL.dll` 的 sha256 **必然不同**(字节数可以相同);
   只有**同一棵树**重编才逐字节相同。跨树引 dll sha 不算证据(帧 PNG 与树路径无关,可以跨树比)。
3. **引用 Linux 帧时必须写明 `SMOKE_*` 变量**(R32-D28)。Linux 通道**没有** `--page/--theme/--out`,
   页面 / 深色 / 分段分别靠环境变量 `SMOKE_PAGE` / `SMOKE_DARK` / `SMOKE_SEG`(下载页)与 `SMOKE_SETTINGS_TAB`(设置页);
   而 Linux 对**未知参数是静默忽略**的 —— 不写变量的"同一帧"复现不出来。

### 2026-11-16 基准确认(截至 `a4e76d7`):旧值 → 新值 + 逐行归因

> 本表由 **R32-MAC-BASE-REFRESH** 产出:自己建树 `wt-mac-base`(`r32-mac-base`,基线 `a4e76d7` = `r32-mac-final`),
> 删 `bin/obj` 后 `-t:Rebuild` 再用 `progress\scripts\r32-capture.ps1 -Tree wt-mac-base -Style mac -Batch mbase` 跑满
> 20 帧(10 页 × 明暗),并**复跑**一次(`stability.json` = `maxDiffPixels=0`、`bytesIdenticalAllRepeats=true`,
> 不是抖动)。旧值列 = 上面那张表在 `ad2b04b` 树(2026-11-14)的原始值;"变化 px" = 逐像素比对掩码像素数
> (`max(axis=2)>0`)。**每一行都给了归因提交,没有"应当无影响"。**

| `--page` | 旧值 light / dark | 实测(a4e76d7)light / dark | 变化 px light/dark | 归因(提交 / 性质) |
|---|---|---|---|---|
| 不带(首屏)/ `launch` | 37681 / 33579 | **37679 / 33576** | 15 / 4 | `59f2d27`(R32-MWG 换渲染宿主)⇒ **亚像素 AA,通道差 ≤2,无位移** |
| `library` | 43810 / 40699 | **45577 / 42261** | 45658 / 45780 | `08a6990`(R32-MACMORE,经合并 `a8db1a4` 并入)⇒ **内容变化**:「版本选择」头部动作簇新增「**实例详情**」按钮,头部行随之重排 |
| `settings` | 45632 / 43723 | **46034 / 44125** | 390 / 389 | **两笔**:`08a6990` ⇒ 左栏**新增「布局」导航项**(172 px,区域 `x=227..252,y=318..329`);`18bd14c`(R32-SCROLLBAR-FIX,经合并 `764d856`)⇒ **滚动条滑块 1px→4px**(216 px + 端部 AA,区域 `x=892..898,y=63..120`) |
| `tasks` | 27719 / 24171 | 27719 / **24171** | 0 / 1 | `59f2d27` ⇒ **亚像素 AA**(dark 仅 1 px;PNG 压缩后字节数不变、只有 SHA 变) |
| `account` | 33173 / 29984 | **33170** / 29984 | 1 / 0 | `59f2d27` ⇒ **亚像素 AA** |
| `download` | 30372 / 26725 | 30372 / **26718** | 0 / 11 | `59f2d27` ⇒ **亚像素 AA**(进度/轨道横条端部圆角) |
| `multiplayer` | 35958 / 32396 | 35958 / **32395** | 0 / 1 | `59f2d27` ⇒ **亚像素 AA** |
| `ai` | 30854 / 30431 | **30904 / 30488** | 9647 / 9655 | `99a67d3`(R32-MACMORE,经合并 `a8db1a4` 并入)⇒ **布局变化**:ai 分栏间隙 18→10(对齐 Windows 站点声明的 `gap: 10`) |
| `instance` / `plugins` | 44186/40798、25115/21410 | **同旧值** | 0 / 0 | **逐字节未动** |
| `--ai-seed`(ai 页,**非表内条目**) | 43130 / 42725 | **44517 / 44218** | — | **三笔**:`08ce0f3`(43130→44404;SmoothScroll 补 `StyleKeyOverride` 后 `SelfTestScrollEnd()` 才真正生效 ⇒ 帧从"停在对话顶部"变成"滚到底",**与 seed 语料随机性无关**)+ `99a67d3`(44404→44461,ai 间隙)+ `18bd14c`(44461→44517,滚动条) |

- **帧级口径**:相对旧表**变了 12 帧**(launch±、library±、settings±、tasks-dark、account-light、download-dark、multiplayer-dark、ai±),
  **8 帧逐字节未动**(instance±、plugins±、tasks-light、account-dark、download-light、multiplayer-light)。合计 20 帧。
- 其中 `59f2d27` 那一笔**单独**就是 **8 帧 / 152 px**(见上面纪律注记第 1 条);`library` / `settings` 的「实例详情」「布局」是
  R32-MACMORE **有意加入的入口**,`settings` 的滑块是 R32-SCROLLBAR-FIX 的**修复**,都不是渲染回归。
- **完整性佐证**:`677c198`(R32-DEGFIX)与 `36040e6`(R32-DOC)对 20 帧**零影响**(实测),`ba48b6a`(R32-MAC-TABS)、
  `648f950`(R32-SYS01,纯测试)、`4d04837`(纯测试)同样零影响。
- 已知前提:`--shot / --size / --width / --height` 在本树**不存在**(实测 **exit 2** + stderr「未知参数」);
  窗口逻辑尺寸固定 900×550,离屏帧同为 900×550。

### 离屏帧解读须知(不知道这三条会误判)

1. **没有后端**:`--smoke` 不启动后端(`BridgeReady` 为假),页面停在**载入前状态**(D-96)。所以帧里有骨架、没有数据。
2. **列表行不落地**:离屏时列表的容器(`ListBoxItem`)会建出来,但**容器是空的** —— 行模板不实例化,行里的文案既不在可视树也不在逻辑树(实测钉住)。后果是:**任何列表页在离屏帧里只有"计数 / 空态",看不到行**。真机不受影响。
3. **分段是切段才建**:设置页这类"分段式"页面,未切到的分段内容不在帧里;所以 `settings` 帧不变**并不代表**分段接线失效 —— **接线只有用例能证明**。

**结论:离屏帧证明的是"外壳 + 页面骨架 + 主题色正确",不是"页面内容正确"。** 内容正确性靠两条测试通道,观感与真机行为靠 `docs/MAC-VERIFY.md`。

## 自定义布局(布局 v2)

R32 起,mac 树有了布局 v2 的核心:**页面右键 →「自定义布局...」** 进编辑态,编辑态有四个标记
(**完成编辑 / 重置本页 / 导出布局 / 导入布局**)+ 撤销/重做/吸附/全部折叠 + 「已隐藏的区块」托盘;
改动**即时保存**到 `<数据目录>/ui_layout_v2.json`(v1 `ui_page_layouts.json` 只是单向写通的兼容投影);
6 处左右分栏可拖、双击复位,比例也存进同一份 v2 文档。

- 完整手册:[`docs/布局使用指南.md`](docs/布局使用指南.md)(含 12 个区块宿主清单、文件字段、回退到 v1 的步骤)
- **当前未覆盖**:设置页整页「布局」视图、实例库 / 资源目录 / 插件页的按页布局视图(正在移植中);
  「布局设置...」菜单只跳设置页;自建区块没有图形化新建入口;块上的拖拽图标**不可点**(按住整卡拖动);
  页签编辑、AI 排版(`layout_apply`)、「AI 备份」未移植。详见该手册 §10。

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
