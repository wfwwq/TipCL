# TipCL R30 发版草案(RELEASE-DRAFT)

> **状态:全部未生效。** 本文件只记录"发版那天要做什么、要填什么",**不构成任何发布动作**。
> 本轮(R30-WP1)**没有** push、**没有**打 tag、**没有**发 release,**没有**改 `update.json` / `update-linux.json` 的任何一个字节。
> 落笔:`2026-11-15`(本地 +08:00),`R30-WP1` 工作包;工作树基线 HEAD = `25c4e64c0104d83514dfbcf31b684f7e302f0349`(见 §1.5 的"演练产物不可复现"说明)。

---

## 0. 先把口径钉死:版本号是**占位**,不是生效值

| 项 | 值 | 说明 |
|---|---|---|
| 本轮演练用的占位号 | **`1.2.0`** | 用户尚未拍板。这只是为了让"版本号统一 + 构建 + 测试"这条链路能被真跑一遍。 |
| **WP-1 的推荐号** | **`1.2.0`** | 理由见下面 5 条。 |
| 硬约束(不可违反) | **必须严格大于 `1.1.0`** | 更新判据是 `Updater.Newer(remote, local = AppInfo.APP_VERSION)`,**严格大于**;填成等于则预览永远是「已是最新版本」(实测见 §3.4 case 2)。 |
| 用户拍板后要不要再改一次 | **要**。一次纯文本替换,共 **12 个落点** | 清单见 §1.2。这是一次机械替换,没有逻辑改动。 |

**推荐 `1.2.0` 的理由**

1. 这是**加功能但不破坏格式**的版本:布局 v2 把 v1 文件语义**冻结**为兼容子集(`ui_page_layouts.json` 继续原样写),老客户端读得懂、老用户的布局不丢 ⇒ 按语义化版本该走 **MINOR**(1.2.0),不是 MAJOR。
2. 不该走 PATCH(`1.1.1`):本次带的是一整套新布局系统与 AI 排版能力,不是修补。
3. 两棵树保持**同号**:2026-11-14 刚把 Windows 与 Linux 统一到 `1.1.0`,本轮两个仓库一起抬到 `1.2.0`,继续维持"一个产品一个号"。
4. 与"是否移植 Linux"**不冲突**:布局 v2 只在 Windows(§4 口径),但 Linux 侧本轮同样有自更新、账号闸门等 NET CHANGE;即便用户以后决定移植,`1.2.0` 依然成立(Linux 只是多一个功能),不需要再换号。
5. 用户想自由挑号也没有阻碍:**唯一硬约束就是 > `1.1.0`**。任何满足该约束的号都只需按 §1.2 做一次替换。

### 0.1 两步走,不许合成一步(硬约束)

- **第 1 步(本轮已做)**:把**产品版本号**抬到 `1.2.0`。
- **第 2 步(本轮明确不做)**:把 `update.json` / `update-linux.json` 的清单 `version` 抬到 `1.2.0` 以上。
- **为什么必须分开**:`tests/TipCL.Tests/Core/UpdaterManifestGuardTests.cs` 钉住了「清单 `version` **≤** 客户端版本时,`sha256` 必须是**格式非法**的占位值」。在没真发版的前提下把清单 `version` 抬到客户端之上,等于**伪造发布状态**,该守卫会当场判红。所以本轮只做第 1 步。

---

## 1. 版本号:落点、演练实测、以及"发版那天还要改什么"

### 1.1 本轮实际改掉的 12 个落点(已提交,见 §6 提交号)

| # | 文件:行 | 现值(改前) | 目标值(改后) | 消费者 |
|---|---|---|---|---|
| 1 | `src/TipCL.Core/Launcher/AppInfo.cs:10` | `APP_VERSION = "1.1.0"` | `"1.2.0"` | **更新判据**、关于页、插件兼容、反馈 `app_version`、sysinfo |
| 2 | `src/TipCL.Core/Launcher/AppInfo.cs:16` | `LAUNCHER_VERSION = "1.1.0"` | `"1.2.0"` | 拼 `--launcher_version` 传给 Minecraft |
| 3 | `src/TipCL.Core/Launcher/AppInfo.cs:18` | `__version__ = "1.1.0"` | `"1.2.0"` | 占位,无消费者 |
| 4 | `src/TipCL.Core/CoreInfo.cs:8` | `Version = "1.1.0"` | `"1.2.0"` | **启动横幅**(`AppLog.cs:67`);**`tools/deb.sh:49-51,64` 从它取 .deb 版本号** |
| 5 | `src/TipCL.Core/TipCL.Core.csproj:10` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 |
| 6 | `src/TipCL.App/TipCL.App.csproj:8` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 |
| 7 | `tests/TipCL.Tests/SmokeTests.cs:17` | `Assert.Equal("1.1.0", CoreInfo.Version)` | `"1.2.0"` | **守卫用例**(不是 bug,必须与新号同一个 commit) |
| 8 | `windos/src/TipCL/TipCL.csproj:17` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 + `AssemblyInformationalVersion` |
| 9 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:9` | `APP_VERSION = "1.1.0"` | `"1.2.0"` | **更新判据**、关于页(`SettingsPage.cs:2030`)、插件版本、反馈、sysinfo |
| 10 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:15` | `LAUNCHER_VERSION = "1.1.0"` | `"1.2.0"` | 拼 `--launcher_version` |
| 11 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:17` | `__version__ = "1.1.0"` | `"1.2.0"` | 占位 |
| 12 | `src/TipCL.App/Session/SmokeRunner.cs:592` | 字面量 `"TipCL/1.1.0"` | **派生** `"TipCL/" + AppInfo.APP_VERSION` | 冒烟网络探针的 HTTP UA |
| 13 | `src/TipCL.Core/Plugins/PluginHost.cs:43` | 默认参数字面量 `"1.1.0"` | **派生** `AppInfo.APP_VERSION` | 插件宿主默认 `tipclVersion`(喂 `PluginManifest.EngineOk`) |

> 12 / 13 号是 WP-0 点名的两个"隐身硬编码"。本轮按"派生"处理(不加 `using`、只改那一行,用全限定名),派生等价性有独立实测,见 §3.3。

### 1.2 用户拍板后要再改一次的落点(**含本轮没改、也不该由 WP-1 改的**)

| 落点 | 现状 | 谁改 |
|---|---|---|
| §1.1 的 13 条 | 已写成 `1.2.0` | 一次文本替换 |
| `tests/TipCL.Tests/SmokeTests.cs:14-15`(注释) | 仍写"版本统一为 1.1.0" | **WP-1 授权只许改第 17 行**,故未动;建议收尾统一替换 |
| `tests/TipCL.Tests/Core/UpdaterManifestGuardTests.cs:66,90,93`(注释) | 仍写"`APP_VERSION` 已经是 `1.1.0`" | 不在 WP-1 地盘;建议收尾替换 |
| `docs/README.md:19-20` | 写"版本号 `1.1.0` 必须三处一致" | 不在 WP-1 地盘;建议收尾替换(该句本身描述的是约定,约定没变,只有数字要跟) |
| `update.json` / `update-linux.json` 的 `version` | `1.1.0` / `0.1.0-m3` | **只有真发版时才改**,见 §2 |
| `windos/src/TipCL/app.manifest:3` 的 `version="3.2.0.0"` | **不要动** | 这是 Win32 清单 schema 版本,不是产品版本 |

### 1.3 `1.0.15` 残留复核(1.1 出过"应用内显示 1.0.15、反复提示更新"的事故)

全库 `git grep '1\.0\.15' HEAD` 命中 **4 处,全部在历史文档里,0 处代码**:

```
windos/GOAL.md:11,51,56            (历史目标文档:v1.0.15 是当年的体积基线)
windos/docs/size-report.md:10      (历史体积报表)
```

⇒ **产品代码里 0 命中**,本轮又把它复核了一遍(`progress/evidence/wp1/round-check-1.0.15.txt`)。

### 1.4 "关于页显示的版本号从哪来"(这是当年 1.0.15 事故的验尸点)

- **Windows**:`windos/src/TipCL/Pages/SettingsPage.cs:2030`
  `Ui.Field(I18n.L("版本"), Ui.Muted(TipCL.Core.Launcher.AppInfo.APP_VERSION))`
  ⇒ 关于页**直接读常量 `AppInfo.APP_VERSION`**,与更新判据**同一个源**。所以"应用内显示 1.0.15、却反复提示更新"这种事故在当前 HEAD 上**不可能**由两个常量打架引起。
- **Linux / Avalonia**:`src/TipCL.App/Pages/Settings/SettingsPage.Cards.cs:155`
  `UiClass.Field("版本", UiClass.Muted(AppInfo.APP_VERSION + "(Linux 移植版)"))` ⇒ 同源。
- 本轮对该常量的实测(反射,见 §3.1):Windows 与 Linux 两棵树都是 `1.2.0`。

### 1.5 ⚠️ 演练产物不可复现(发版那天必须重新构建)

本轮演练构建发生在**改动后的工作树**,当时 HEAD **仍是 `25c4e64c...`**(尚未提交),所以:

- 两个 exe 内嵌 `AssemblyInformationalVersion = 1.2.0+25c4e64c0104d83514dfbcf31b684f7e302f0349`(实测读出,见 §3.2);
- **提交之后 HEAD 变了**,再构建一次内嵌的 `+sha` 就不同 ⇒ **exe 哈希不同**(体积基本不变)。
- 依据:`windos/docs/DELIVERY-2026-11-14.md` §6 第 4 条("换 commit 换哈希、体积基本不变";同 commit 换目录可复现)。
- ⇒ **§2.3 的 SHA256 只用于"演练取数"演示,不能直接填进清单**。真发版时必须:先定 commit → 在那个 commit 上重新出包 → 用**那次**的 sha256。

---

## 2. 两条发版链路(照这个走)

> 前置:**版本号已由用户拍板**(把 §1.1 的 13 条替换成最终号),并且**已提交**;下面每一步都在该 commit 上执行。

### 2.1 Windows 链路

| # | 动作 | 命令 / 说明 |
|---|---|---|
| W1 | 出 **compact** 标准件 | `powershell -NoProfile -ExecutionPolicy Bypass -File windos\tools\build-both.ps1 -Compact` — 脚本内置 D-14 标准参数(`-c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`)并**先删 bin/obj 强制全量**;compact 超红线(5,757,264 B)会 `exit 1` |
| W2 | 立刻留档 compact | **构建完马上**把 `windos\src\TipCL\bin\Release\net48\TipCL.exe` 复制出去改名留档 —— 下一步 premium 会覆盖同一个路径 |
| W3 | 出 **premium** 标准件 | `... -File windos\tools\build-both.ps1 -Premium`,同样立刻复制留档 |
| W4 | 生成 `.sha256` | 对**要发布的那一份**做 `Get-FileHash -Algorithm SHA256`(Windows 侧 `release.yml` 用的名字是 `TipCL.exe.sha256`) |
| W5 | 产出/上传 | 走 release.yml 的 `windows` job,或人工 `gh release upload <tag> TipCL.exe TipCL.exe.sha256` |
| W6 | 填清单三行 | 把 §2.3 的三行填进**仓库根** `update.json`(**不要挪位置**:客户端按固定 URL 读) |
| W7 | 复核 | `curl -L https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json` 与 `curl -sIL <url>` 都要能拿到 |

**⚠️ 本轮发现的口径冲突(W5,必须由人拍板,不在 WP-1 地盘)**
`release.yml` 的 windows job 是 `dotnet build windos/src/TipCL/TipCL.csproj -c Release`(第 39 行),**没有** D-14 的 `-p:DebugType=none -p:DebugSymbols=false`。
而 `windos/docs/DELIVERY-2026-11-14.md` §6 第 8 条明确说:这样出来的 `bin` 里那个 exe 比标准件**大 512 B**(且同目录带 `TipCL.pdb`),**不能拿来切交付件**。
⇒ 两条路选一条,**不要混**:① 发布时由人把 W1/W3 的标准件**手工上传**(推荐,零改动);② 改 `release.yml` 给 Windows job 加上那两个参数(需 `.github/` 的属主动手 —— **不是 WP-1 的地盘**)。
**本轮不碰 `release.yml`**,只把冲突如实记在这里。

### 2.2 Linux / Avalonia 链路

| # | 动作 | 命令 / 说明 |
|---|---|---|
| L1 | 切 tag | **版本号必须严格大于客户端版本**(如 `v1.2.0`);`Updater.Newer` 只认严格大于 |
| L2 | 建 release(published) | release 事件触发 `.github/workflows/release.yml` 的 `linux` job(也可 `workflow_dispatch` 手填 tag) |
| L3 | job 自动做 | `dotnet publish src/TipCL.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` → `tools/package.sh --appimage-only --deb` → `xvfb` 冒烟 `--smoke` → 上传 `dist/TipCL-linux-x64.AppImage` + `dist/*.deb` |
| L4 | 取清单三行 | job 最后一步会把三行**打印进 Actions 日志**(`version` = tag 去掉 `v`,`url`,`sha256`)—— 照抄,别自己算 |
| L5 | 填清单 | 填进**仓库根** `update-linux.json` |
| L6 | 复核 | `curl` 清单 + `curl -sIL <AppImage url>` 都要能拉到(历史教训:清单指向的 release **不存在**、实测 404,见 §5) |

**产物名**:`TipCL-linux-x64.AppImage`、`tipcl_<版本>_amd64.deb`(`tools/deb.sh:64`)。
**⚠️ `.deb` 的版本号来自 `src/TipCL.Core/CoreInfo.cs`**(`tools/deb.sh:49-51`),所以本轮把 `CoreInfo.Version` 改成 `1.2.0` 也会让包名变成 `tipcl_1.2.0_amd64.deb`。
**⚠️ Linux 资产没有 `.sha256` 附件**:`release.yml` 只给 Windows 传了 `TipCL.exe.sha256`,Linux 的 sha256 只出现在 Actions 日志里。README 的写法已按这个事实写(§5)。

### 2.3 待填的清单三行(**未生效·待用户拍板**)

**A. `update.json`(Windows)** —— 现值 `version = 1.1.0` / `sha256 = 8d5f266bbf9d9c3f4231479d4c4e06a692ea5423863f63a7242126c6aa9b759f`(v1.1.0 的真值,**未改**)

```json
"version": "1.2.0",                                                                // 占位:必须 > 1.1.0,以用户拍板号为准
"url":     "https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL.exe",    // 占位:tag 名要跟着改
"sha256":  "<<发版那天用最终 commit 重新出包后再算,见 §1.5>>"
```

**B. `update-linux.json`(Linux)** —— 现值 `version = 0.1.0-m3` / `sha256 = REPLACE-WITH-THE-REAL-SHA256`(**故意格式非法**,未改)

```json
"version": "1.2.0",                                                                          // 占位:必须 > 1.1.0
"url":     "https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL-linux-x64.AppImage", // 占位
"sha256":  "<<Actions 日志里打印的那一行,见 §2.2 L4>>"
```

> 两份清单都**留在仓库根**、都没被本轮改动(红线)。
> `sha256` **绝不允许**写成 64 个 0 那种"格式合法但不存在"的假值 —— 那正是 R22 修掉的洞(`UpdaterManifestGuardTests` 守着)。

---

## 3. 本轮演练实测(全部真跑,原始输出在 `progress/evidence/wp1/`)

### 3.1 版本常量(反射读已构建的 `TipCL.Core`)

```
AppInfo.APP_VERSION      = 1.2.0        (Windows 与 Linux 两棵树)
AppInfo.LAUNCHER_VERSION = 1.2.0
AppInfo.__version__      = 1.2.0
CoreInfo.Version         = 1.2.0
```

### 3.2 演练产物 + SHA256

| 产物 | 字节 | SHA256 | 内嵌 `AssemblyInformationalVersion` |
|---|---|---|---|
| compact(`windos\tools\build-both.ps1 -Compact`) | **5,534,208** | `F93918F9FCF0E524CBFF9121EAFE53ECED447DFAF2BC2DD2448DBE8B01D82226` | `1.2.0+25c4e64c0104d83514dfbcf31b684f7e302f0349` |
| premium(`windos\tools\build-both.ps1 -Premium`) | **5,554,176** | `94AEBF4B6D36845A5DB31F419500D87DDA1A9E4EA05AF9ADF9EC6DED61BD0F63` | 同上 |

- 构建时间 2026-11-15 09:48(本地),两次都 `0 错误 0 警告`,compact 合规(红线 5,757,264,余量 **223,056 B**)。
- 两 flavor **确实是两份不同的文件**(哈希不同)。
- 文件版本资源实测:`FileVersion = 1.2.0.0`、`ProductVersion = 1.2.0+25c4e64c...`;exe 内 UTF-16 搜索 `1.2.0` 命中 4 处、**`1.1.0` 命中 0 处**。
- 与 `windos/docs/DELIVERY-2026-11-14.md` §3.1 记录的上一版最终件(**5,533,696 / 5,553,664**,commit `6b77220`,版本号 `1.1.0`)相比,本次 **+512 B**。原因:**不同 commit**(本仓库 HEAD 是 `25c4e64`)+ 版本串内容变化;属"体积基本不变"的范围,但**发版时请以那一次实测为准**,不要引用本行。
- 演练产物**留档**在 `progress/evidence/wp1/TipCL-compact.exe` / `TipCL-premium.exe`(仅作证据,不可直接发布,理由见 §1.5)。

### 3.3 两个"隐身"字面量的派生等价性(单独一次验证)

- **`PluginHost` 默认版本**:反射读构造函数元数据 + 实跑一个实例:
  `ctor param 'tipclVersion' DefaultValue = 1.2.0`、`IsOptional = True`、`new PluginHost(null, <default>)` 后私有字段 `_tipclVersion = 1.2.0`。
  全仓**没有**任何调用点省略第二个参数(唯一的调用方 `tests/TipCL.Tests/Core/PluginHostTests.cs:22,96` 都显式传 `"0.1.0-m3"`),所以这次改动**不改变任何现有调用行为**。
- **`SmokeRunner` 的 HTTP UA**:`"TipCL/" + AppInfo.APP_VERSION` 是编译期常量折叠,直接在被构建的 `src/TipCL.App/bin/Debug/net8.0/TipCL.App.dll` 里按 UTF-16LE **字节级**搜到字面量 `TipCL/1.2.0`(命中 1 处,偏移 1323575),`TipCL/1.1.0` **0 命中**。
- 两个落点都**没有**新增 `using`、**没有**新增依赖:只改了那一行。

### 3.4 更新判据演练(只在本机,**没碰仓库根的清单**)

用 `progress/evidence/wp1/probe/`(一个临时 net8.0 控制台工程,只引用 `TipCL.Core`,**不在仓库内**)起了一个 127.0.0.1 的本地清单服务,把 `TIPCL_UPDATE_URL` 指过去,真调 `Updater.Check()`:

| 场景(本地客户端 = `1.2.0`) | 实测 |
|---|---|
| 远端 `1.3.0` + 合法 sha64 | `ok=true has_update=true message=发现 1.3.0` ✅ |
| 远端 `1.2.0`(**等于**本地)+ 合法 sha64 | `ok=true has_update=false message=已是最新版本` ✅ |
| 远端 `1.1.0`(**低于**本地)+ 合法 sha64 | `ok=true has_update=false message=已是最新版本` ✅ |
| 远端 `1.3.0` + 格式非法占位 sha | `ok=false has_update=false message=更新清单缺少有效 SHA-256,已自动拒绝` ✅ |
| 仓库 `update-linux.json` **原样** | `has_update=false / 已是最新版本`(中性,既不伪造更新也不误报被拒)✅ |
| 仓库清单 + version 抬到 `99.9.9` | `ok=false` + 同一句 sha 拒绝文案 ✅ |

`Updater.Newer` 真值表(实测):`1.3.0>1.2.0 = True`、`1.2.1>1.2.0 = True`、`1.2.0>1.2.0 = **False**`、`1.1.9/1.1.0/0.1.0-m3 > 1.2.0 = False`。

### 3.5 启动横幅(版本号在运行时的落点)

Windows 本机走 **无头** `--smoke`(`Program.cs:17` → `SmokeRunner.Run`,`SmokeRunner.cs:65-66` 里显式 `.UseHeadless(...)`,**不会真的开窗**),`TIPCL_HOME` 指向隔离目录:

```
2026-11-15 09:53:51.854 [INFO] === TipCL 1.2.0 启动 pid=23696 os=Microsoft Windows NT 10.0.19045.0 ===
2026-11-15 09:53:51.855 [INFO] home=D:\项目\TipCL\progress\evidence\wp1\smoke-home cwd=...\smoke-out
```

- 冒烟退出码 `0`,`smoke.png` = 570,903 B(渲染链路也活着)。
- 横幅打印点 `src/TipCL.App/Common/AppLog.cs:67`;日志落在 `$TIPCL_HOME/logs/tipcl-<yyyyMMdd>.log`。
- **真实用户目录 `%LOCALAPPDATA%\TipCL` 全程零改写**(实测:该目录下**最新**文件 mtime 仍是 2026-11-10;会话期间(09:40 之后)新增/改写的文件**0 个**)。

---

## 4. Release Notes 定稿(中文,发布时可直接用)

> **口径声明**:下面标「**仅 Windows**」的条目在 Linux 版**没有**。原因见本节末尾的"待决口径"。

### TipCL 1.2.0

**主要是两件事:界面可以自己排,以及体积收回来一大截。**

#### 🧩 布局 v2:界面从"只能上下排"变成"想怎么摆就怎么摆"(仅 Windows)

- **三种摆放模式,逐页切换**:`stack`(原来的纵向排列,保持默认)、`grid`(**12 列网格**,可跨列、可拖拽定位、按行自适应高度)、`canvas`(像素自由摆放,对声明了固定高度的页/块开放)。
- **每块都能调外观**:跨列数、卡片/无边框/分隔线、自定义边距与间距、是否常显块标题。
- **分栏比例可拖可记**:原来 13 处写死的分栏全部可以拖,松手就记住,双击复位。
- **顶栏页签也能自定义**:顺序、显隐自己定。
- **窄窗 / 宽窗两套布局**:窗口宽度跨过阈值时自动切到 `narrow` 变体;编辑时可以选"正在编辑哪一套"。
- **整机布局方案**:页签 + 各页区块 + 分栏比例 + 自建区块 + 外观打包成**命名方案**,一键切换;可以**导出成一份 `.json` 发给别人、对方导入**即可套用(也支持只导出兼容旧版的"页面布局");导入时会先校验,不合法的字段会逐条说明原因。
- **向后兼容**:旧版布局文件(`ui_page_layouts.json`)的语义**一个字都没改**,继续按老格式写;升级不会丢你已有的布局。
- 设置页新增**「布局」管理视图**(方案增删改查 / 导入导出 / 布局 JSON 编辑器)。

#### 🤖 AI 也能排版(仅 Windows)

- 新增两个 AI 工具:`layout_get`(看当前布局:每页区块顺序与显隐、页面模式、自建区块、分栏比例、顶栏页签、已存方案名)与 `layout_apply`(改布局)。
- **改之前一定先存一份**:`layout_apply` 会先把当前布局备份成一个命名方案(**`AI 备份 <时间>`**,最多保留最近 5 份),随时可以切回去。
- 支持组合:改某一页的区块、应用某个已存方案、直接提交整份布局 JSON、新增/覆盖自建区块、改顶栏页签。

#### 🧱 自建区块:把你自己的东西放到任意一页(仅 Windows)

- 首期可用:**便签、时钟、快捷方式、分隔线、游戏时长**。
- **网页(`web`)与按钮(`button`)默认不出现**,要在设置里**显式开启**并二次确认。开启后也有限制:网页只允许 `https`(禁 `file://` / `javascript:`),按钮只允许**内部动作白名单**(切页 / 启动指定实例 / 打开启动器数据目录 / 只读查询),其它一律拒绝并给出中文原因。

#### 🔌 插件也能贡献区块(Node 与 .NET 都支持)

- 插件的 UI 贡献新增 `block` 类型:插件可以往布局里注册一个**自己的控件**当区块。
- Node 插件:`tipcl-plugin.d.ts` 里新增 `block(spec): Dispose`。
- .NET 插件:提供了最小真实样例(`windos/samples/dotnet-block-plugin/`)与端到端驱动(走启动器自己的启动路径 + 真实 127.0.0.1 桥),实测 **PASS 10 / FAIL 0**(插件经真加载器进注册表、停用后条目与 RPC 行同时消失)。
- 插件卸载后它在布局里的块**不会被删**,界面上显示「不可用」,装回来即恢复。

#### 🐧 Linux 版(AppImage / `.deb` / 自包含 / 自更新)

- 提供 **AppImage**(`TipCL-linux-x64.AppImage`)与 **`.deb`**(`tipcl_<版本>_amd64.deb`),均为 **self-contained 单文件**,机器上**不需要装 .NET**。
- **自动更新已闭环**并做过真机端到端:检查更新 → 下载 → 校验 sha256 → 「立即重启」当场就地替换 / 「稍后重启」两条分支都验过;版本号没超过客户端时显示「已是最新版本」,**占位/格式非法的 sha256 会被当场拒绝并说明原因**(不再出现"提示有新版本、下载到一半才失败");下载失败在**下载任务页**有明确失败行。
- **`.deb` 安装形态**只下载、不就地替换(装在 `/opt`),会提示"需要手动安装"。
- 安装账号闸门、权限档位说明等若干体验修补。

#### 📦 体积回收(compact,红线 5,757,264 B)

| 阶段 | compact |
|---|---|
| 起点 | **6,061,056 B** |
| 终点 | **5,533,184 B**(累计 **−527,872 B**,相对起点 **−8.7%**) |

做法两条:① Core 资源(locales / ai / plugin-host 共 15 个源文件)改**构建期 gzip 内嵌**,读取侧按 gzip 魔数**透明解压**,运行时释放的字节与改造前**逐字节相同**;② 排除 ImageSharp 的传递依赖 `System.Text.Encoding.CodePages`(它是唯一用到 `Encoding.GetEncoding(437)` 的路径的替代实现,已用**隔离单 exe + 真实有损 VP8 WebP** 的反射夹具验证:能解码、`codePagesLoaded = False`、WebView2 的三个 loader **仍在**)。
⇒ compact 从"超红线 690,352 B"回到"**余量 224,080 B**"。

#### 🛠 修复项

| 修复 | 影响 |
|---|---|
| 反馈弹窗的「AI 修复」在 `chat_id` 不存在时按 id 建对话 | 不再把 AI 修复的回合错误地落到主界面当前对话里 |
| `-p:Premium=true` 干净 HEAD 下编译报 `CS0103(_immersive)` | 此前 **premium 交付件不可复现**,已修 |
| 受限宿主里假手机端的 `HttpListener` 被拒导致用例必失败 | 改用裸 socket;验收口径从"排除 `PhoneLinkTests`"**恢复为全量** |
| 插件页 `plugins_ui` 重复行 | 已修(含 7 例单测) |
| i18n:两处"只差标点宽度"的近似重复 key,以及 3 处因此查不到译文的源码字面量 | 词表 en/zh 各 2951 条、差集 0 |
| 体积回收引入的两个隐蔽坑(见上) | 已带漂移守卫用例(15 例) |
| 仓库卫生:`.gitignore` 补 `artifacts/`、`*_wpftmp.csproj`、`_wpf_run/` | 生成物不再长期挂脏 |

#### ⚠️ 待决口径(发版前必须由用户/维护者确认,别默认)

1. **「仅 Windows」的那几项**:布局 v2 / AI 排版 / 自建区块**目前只在 Windows 有**,Linux 仍是旧区块布局。
   本稿按"**仅 Windows**"口径写。**若用户选择本轮就把布局 v2 移植到 Linux,本节标题里的「仅 Windows」必须去掉,并补一份 Linux 侧的验收证据**;移植评估以 WP-3 的结论为准。
2. **Windows 交付件到底用哪一份**:见 §2.1 的 W5 冲突(CI 的 `dotnet build` 产物 vs 标准件差 512 B)。
3. **`README.md` 里的 SHA256 行**:本轮**没有**把 v1.2.0 的哈希写进 README(没发版就不能写),发版那天要补。

---

## 5. README 改动清单(本轮实际改了什么)

| # | 段落 | 改动 |
|---|---|---|
| R1 | 标题下的英文副标题(`README.md:11`) | `An AI-native Minecraft launcher for Windows` → `... for Windows and Linux` |
| R2 | 平台徽章(`README.md:15-22`) | 在 WPF 徽章后**新增一行** Linux/Avalonia x64 徽章(`FCC624` + `logo=linux`);其余徽章不动 |
| R3 | 「📦 下载与构建」(`README.md:82-100`) | 新增 **Linux 的三段**:① Releases 里的 `TipCL-linux-x64.AppImage`(自包含、免安装)与 `tipcl_<版本>_amd64.deb`;② 两者各自的安装/运行命令;③ 自己编译 Linux(`dotnet build TipCL.Linux.sln`)、Linux 数据目录 `~/.tipcl`(可用 `TIPCL_HOME` 覆盖)。原有 Windows 段落**未改** |
| R4 | 「🛡️ 安全说明」(`README.md:185`) | **保留** v1.1.0 那行真实 SHA256(它是既成事实,不能删也不能改);**新增一行**说明 Linux 侧的校验方式 —— 与 Windows 不同,Linux 的两个资产**不带 `.sha256` 附件**(实测 `release.yml` 只给 Windows 传了 `TipCL.exe.sha256`),AppImage 的 SHA-256 出现在发行说明/Actions 日志里,可用 `sha256sum` 自行比对 |

**刻意没做的**:没有给 README 加 v1.2.0 的 SHA256(未发版,写了就是伪造发布状态);没有改「更新机制」「仓库结构」等与版本无关的段落;没有把 README 的 Windows 主体改写成双平台文档(那是更大的编辑决定,留给用户/维护者)。

---

## 6. 关联

- 证据目录:`progress/evidence/wp1/`(构建日志、两份 trx、冒烟 applog、探针输出、两个演练 exe)
- 工作包报告:`progress/R30-WP1.md`(含自证命令与原始输出、测试逐条定性、偏离说明)
- 发版相关既有文档:`windos/docs/DELIVERY-2026-11-14.md`(体积/可追溯性/9 条隐蔽坑)、`docs/HANDOFF-d95a9472.md`(自更新闭环判定与"必须 > 1.1.0")、`windos/docs/layout-v2-design.md`(布局 v2 契约与已拍板的 7 个决策)
