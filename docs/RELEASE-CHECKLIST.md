# 发版前的元数据检查单(RELEASE-CHECKLIST)

> 适用对象:**桌面端**(Windows 的 `TipCL.exe` + Linux 的 `TipCL-linux-x64.AppImage` / `tipcl_<版本>_amd64.deb`)。
> 安卓两条构建线**不在同一个号上**,见 §6。历史草案见 [RELEASE-DRAFT.md](RELEASE-DRAFT.md)(那是 1.2.0 那次的记录,口径以本文件为准)。
> 最后核对:2026-11-20,`main@880deff`。

---

## 0. 先记住两条硬约束

1. **新版本号必须"严格大于"客户端当前号**。更新判据是 `Updater.Newer(remote, local = AppInfo.APP_VERSION)`(`src/TipCL.Core/Launcher/Updater.cs:56`、`windos/.../Updater.cs:56`),只认严格大于;**清单 `version` 等于客户端号 = 永远显示「已是最新版本」**。
   - 反例(2026-11-19 的真实事故):v1.2.0 发过一次之后**替换了** Windows 附件、版本号没变 ⇒ 装着旧 1.2.0 的用户**永远收不到**那次修复,只能手动重下。**同一个号只允许对应同一个二进制。**
2. **版本号形状必须是三段 `X.Y.Z`**。两条守卫用例钉死:
   - `tests/TipCL.Tests/SmokeTests.cs:21,26`:`CoreInfo.Version == AppInfo.APP_VERSION` 且匹配 `^[0-9]+\.[0-9]+\.[0-9]+$`;
   - `windos/tests/TipCL.Tests/VersionLandingPointsTests.cs:105`:产物 `FileVersion == APP_VERSION + ".0"`。

---

## 1. 六处版本落点(一次全改,漏一处必红)

| # | 文件 | 字段 | 谁在读它 | 漏改的后果 |
|---|---|---|---|---|
| 1 | `src/TipCL.Core/Launcher/AppInfo.cs:10` | `APP_VERSION` | **更新判据**、关于页、插件兼容检查、反馈 `app_version`、sysinfo | 客户端不认新版本(自更新失效) |
| 2 | `src/TipCL.Core/Launcher/AppInfo.cs:16` | `LAUNCHER_VERSION` | 传给 Minecraft 的 `--launcher_version` | 游戏侧看到的启动器版本是旧的;`VersionLandingPointsTests` 红 |
| 3 | `src/TipCL.Core/Launcher/AppInfo.cs:18` | `__version__` | 兼容字段(无消费者,但守卫要求同号) | `VersionLandingPointsTests` 红 |
| 4 | `src/TipCL.Core/CoreInfo.cs:8` | `Version` | **启动横幅**(`src/TipCL.App/Common/AppLog.cs`)、**`.deb` 包名**(`tools/deb.sh` 从它抽版本)、CI 的 deb 三方一致校验(`.github/workflows/ci.yml:316-336`) | deb 包名/安装版本对不上,CI 红;横幅显示旧号 |
| 5 | `src/TipCL.App/TipCL.App.csproj:8`、`src/TipCL.Core/TipCL.Core.csproj:10` | `<Version>` | Linux 程序集版本 / 布局档 `app` 字段 | Linux 产物内嵌号与运行时号分叉 |
| 6 | `windos/src/TipCL/TipCL.csproj:17`、`windos/src/TipCL/Core/Launcher/AppInfo.cs:9,15,17` | `<Version>` + `APP_VERSION` / `LAUNCHER_VERSION` / `__version__` | Windows 程序集版本、`LayoutStoreV2.AppVersion()`(写进用户布局档的 `app`)、更新判据 | `VersionLandingPointsTests` 4 条全红(它读磁盘 csproj 文本 + 加载的程序集,故意不抄第二份常量) |

> 说明:第 6 行是**一个文件对**:`TipCL.csproj` 的号与 `AppInfo.APP_VERSION` 必须同号,测试分别从"磁盘文本"和"加载的程序集"两侧断言。

### 1.1 改完立刻自检(两边都要跑)

```powershell
# 环境(便携 SDK)
$env:PATH="D:\项目\TipCL\_tools\dotnet;$env:PATH"; $env:DOTNET_ROOT="D:\项目\TipCL\_tools\dotnet"
$env:NUGET_PACKAGES="D:\项目\TipCL\_tools\nuget"; $env:TEMP="D:\项目\TipCL\_tools\tmp-E"; $env:TMP=$env:TEMP

# Linux 树:版本守卫 + 布局/核心
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --nologo

# Windows 树:4 条版本落点守卫 + 更新边界
dotnet test windos/tests/TipCL.Tests/TipCL.Tests.csproj --nologo --filter "FullyQualifiedName~VersionLandingPoints|FullyQualifiedName~Updater"

# 兜底:还有没有漏网的旧号(应只剩注释/历史文档)
git grep -n "1\.2\.0" -- src windos/src
```

---

## 2. 出包(两个平台各一份,别混)

| 平台 | 命令 | 产物 | 备注 |
|---|---|---|---|
| Windows | `dotnet build windos/src/TipCL/TipCL.csproj -c Release` | `windos/src/TipCL/bin/Release/net48/TipCL.exe` | 这就是 `release.yml` 的 windows job 用的原命令;要出 compact/premium 两支用 `windos\tools\build-both.ps1`(**compact 红线 5,757,264 B**,脚本内有 `$CompactLimit`) |
| Linux | `dotnet publish src/TipCL.App -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true` → `bash tools/package.sh --appimage-only --deb` | `dist/TipCL-linux-x64.AppImage`、`dist/tipcl_<版本>_amd64.deb` | 与 `release.yml` 的 linux job 同命令;`.deb` 名里的版本来自 `CoreInfo.cs`,**必须先改完 §1 再出包** |

> 也可以不打本地包:打 `v<版本>` tag 发 release(`release.yml` 由 `release: published` 触发),两个 job 自行出包上传。**但清单回填仍是人工**(见 §3)。

---

## 3. 要重算的 sha256(4 个地方,全部以"已上传的资产"为准)

| # | 算什么 | 怎么算 | 填到哪 |
|---|---|---|---|
| 1 | `TipCL.exe` | `sha256sum TipCL.exe`(或 GitHub API 资产对象的 `digest` 字段:`sha256:...`) | `update.json` 的 `sha256` |
| 2 | `TipCL-linux-x64.AppImage` | `sha256sum dist/TipCL-linux-x64.AppImage`(`release.yml` 那一步会在日志里打印现成的三行) | `update-linux.json` 的 `sha256` |
| 3 | `TipCL.exe` 的旁证附件 | 与 #1 同一个值 | Release 资产 `TipCL.exe.sha256` 的内容(每次替换 exe 都要一起换) |
| 4 | `TipCL.exe` 的人类可读副本 | 与 #1 同一个值 | 根 `README.md`「🛡️ 安全说明」里 `v<版本> SHA-256:` 那一行 |

`.deb` **不进任何清单**,只在发行说明里记一句大小 + sha256 即可(它不被自动更新使用:`Updater.ApplyStaged` 对 `/opt` 下不可写的安装形态只走 `mode=download-only`)。

> ⚠️ 顺序不能反:先上传资产 → 再对**已上传的那个文件**算 sha → 再回填清单。先算后传(或本地算完换个构建再传)就会出现"清单与资产不一致",客户端会**拒绝下载并报 `更新包 SHA-256 校验失败`**。

---

## 4. 回填两份清单(字段名逐字,别自创)

`update.json`(Windows,客户端固定读 `https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json`):

```json
{
  "version": "<X.Y.Z>",
  "notes": "<一段中文更新说明;客户端只显示这一串>",
  "url": "https://github.com/wfwwq/TipCL/releases/download/v<X.Y.Z>/TipCL.exe",
  "sha256": "<§3 #1 的值,64 位小写十六进制>"
}
```

`update-linux.json`(Linux,同理读 `.../update-linux.json`):

```json
{
  "version": "<X.Y.Z>",
  "notes": "<中文说明>",
  "url": "https://github.com/wfwwq/TipCL/releases/download/v<X.Y.Z>/TipCL-linux-x64.AppImage",
  "sha256": "<§3 #2 的值>"
}
```

- 解析器认的字段名就是这四个(`version` / `notes` / `url` / `sha256`;兼容别名 `latest` / `changelog` / `download`,别用新的别名)。
- `sha256` 必须能被 `\A[0-9a-fA-F]{64}\z` 匹配,否则客户端**当场拒绝并提示「更新清单缺少有效 SHA-256,已拒绝自动更新」**(`Updater.Check`)。
- 两份清单**必须留在仓库根**,挪进子目录会让已安装的客户端 404。
- 清单 `version` 与 tag 名要一致(去掉 `v` 前缀),`url` 里的 tag 段也要跟着改。

---

## 5. 发完复核(4 条,逐条留证)

```bash
# 1) 两份清单真的能拉到、字段齐全
curl -s https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json | python -m json.tool
curl -s https://raw.githubusercontent.com/wfwwq/TipCL/main/update-linux.json | python -m json.tool

# 2) 线上资产的 digest 与清单逐字相同(不复算整包也能核)
curl -s https://api.github.com/repos/wfwwq/TipCL/releases/tags/v<X.Y.Z> \
  | python -c "import json,sys;d=json.load(sys.stdin);[print(a['name'],a['digest'],a['size']) for a in d['assets']]"

# 3) 客户端行为真值表(单位测试已钉,人工确认一遍方向没反)
#    远端 == 本地  -> has_update=false,「已是最新版本」
#    远端 >  本地  -> has_update=true,「发现 <版本>」
#    远端 <  本地  -> has_update=false
#    sha 非法      -> ok=false,「已拒绝自动更新」
```

4. **tag**:`git tag -a v<X.Y.Z> -m "TipCL <X.Y.Z>"` 并在 release 说明里写清「本次重建了哪些资产、各自 sha256」;若只是替换某个平台的附件,**必须同时决定是否换号**(见 §0 第 1 条)。

---

## 6. 不在这份清单里的东西(免得连锁改错)

| 东西 | 口径 |
|---|---|
| **安卓两条构建线** | A 线 `android-app/`(`com.tipcl.android`,当前 `versionName 1.2.0`)与 B 线 `app/Android/`(`com.pymcl.android`,当前 `0.9.3`)各自独立;**不与桌面同号**是现状,要不要统一是拍板项,不是发版检查项 |
| **macOS** | 本仓库没有 mac 树 |
| **布局档里的 `app` 字段** | 由程序集版本派生(`LayoutStoreV2.AppVersion()`),改 csproj 就跟着变,**不要手改用户文件** |
| **`--Dminecraft.launcher.version=1.0.10`** | 传给游戏 JVM 的常量,按 R31 拍板**刻意保留**,不属于版本落点(DECISIONS/RELEASE-DRAFT §1.2.1 形态 C) |
| **`update.json` / `update-linux.json` 的文件位置** | 仓库根,永不挪动 |
| **发布资产名** | `TipCL.exe`、`TipCL.exe.sha256`、`TipCL-linux-x64.AppImage`、`tipcl_<版本>_amd64.deb` —— 与 `release.yml`、两份清单的 `url`、README 的表格必须逐字一致 |

---

## 7. 本轮(1.2.1)待填表

| 项 | 值 |
|---|---|
| 新版本号 | `<X.Y.Z>`(必须 > `1.2.0`,三段) |
| §1 六处已改 | ☐ AppInfo(Linux)☐ CoreInfo ☐ App.csproj ☐ Core.csproj ☐ AppInfo(Windows)☐ TipCL.csproj |
| Linux 测试 | `<通过数>` / 失败 0 |
| Windows 测试 | `<通过数>` / 失败 0 |
| `TipCL.exe` 大小 / sha256 | `<B>` / `<sha>` |
| `TipCL-linux-x64.AppImage` 大小 / sha256 | `<B>` / `<sha>` |
| `tipcl_<版本>_amd64.deb` 大小 / sha256 | `<B>` / `<sha>` |
| `update.json` 已回填 | ☐ version ☐ notes ☐ url ☐ sha256 |
| `update-linux.json` 已回填 | ☐ version ☐ notes ☐ url ☐ sha256 |
| `README.md` 安全说明 SHA 行已改 | ☐ |
| tag / release | `v<X.Y.Z>` / `<release url>` |
| 附录:本次是否替换了旧资产 | ☐ 否(推荐)☐ 是(必须换号,见 §0) |