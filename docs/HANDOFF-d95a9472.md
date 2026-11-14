# 交接:session-`d95a9472` 欠下的「最终汇报」(R21 交付清单 + R22 遗留)

> **这份文件补的是一句没发出来的话。** session `d95a9472`(Linux/Avalonia 全量移植)最后一条可见输出是
> 2026-11-14T05:01:39Z 的「**等复验结论回来我再收尾(并给出最终清单:包括仍如实留白的「立即重启」分支、公网真实清单、真发一次版)**」;
> turn 66 随即 `completed`,之后 52 分钟无记录,05:53:45Z `session/end-seed` —— 连同被派去复验的子代理 `1ecb5b5c` 一起被杀。
> 复验结论与 `scratch/update-e2e2/REPORT.md` 由此欠账;本轮由 **task-8 / `linux-updater`** 补齐:
> 复验报告 = **`scratch/update-e2e2/REPORT.md`**,本文件 = **那份"最终清单"**。
>
> - 核验时间:2026-11-14 14:45–15:20 CST
> - 仓库:`D:\项目\tipcl\linux`,HEAD `32c0b0b`(`main` = `origin/main`)
> - 标注约定:**〔本轮实测〕** = 我亲自读了盘/跑了产物;**〔转述〕** = 只来自会话记录或他人报告,我未独立复核。

---

## 一、R21 的最终交付清单(逐条给状态)

### 1.1 归档产物 `dist/`

| 产物 | 大小 | sha256 | mtime | 状态 |
|---|---|---|---|---|
| `dist/TipCL-linux-x64.AppImage` | **38,984,184 B** | `48C3DCEADA0E35D7C25B181F639FA9B66DC1F2E2F7A0635E856F5C36BE97A6E0` | 2026-11-14 13:00:57 | 〔本轮实测〕与会话/PROGRESS/PARITY 声称**逐字一致** |
| `dist/tipcl_0.1.0-m3_amd64.deb` | **37,544,610 B** | `109CFD55062048B1FD6296D237F747014AF3D1998A4C939CB12FCCCE88DD7130` | 2026-11-14 12:56:29 | 〔本轮实测〕同上 |
| `dist/TipCL-linux-x64.zip` | 38,342,375 B | `8F89A185C50F01DB1CBD7681DEFC5470FA02A1DAA406A4F05712BC51282193BE` | 2026-11-13 19:50:14 | 〔本轮实测〕**不是**本轮 R22 重打的产物(早于 R22),R22 只重打了 AppImage + `.deb` |
| `dist/TipCL-linux-x64/`(publish 目录) | — | — | 2026-11-13 19:50 | 〔本轮实测〕同上,属 R21 期产物 |
| `dist/{linux-x64,win-x64}/` | — | — | 2026-11-09 | M3 期产物 |

- **同一支 AppImage 也在盒子上**:`/home/user/tipcl/linuxsrc/src/dist/TipCL-linux-x64.AppImage`,sha256 = `48c3dcea…`(与本地逐字节相同)〔本轮实测〕。
  ⇒ 盒子上的真机 E2E(round-2 的 T1/T2/T3,以及本轮 T4/T5)**跑的就是归档里这一支产物**,不是临时重建的替身。
- 归档口径:`PULL_ALL_OK`(盒子上 hash 与本地一致,04:52–05:00Z)〔转述,但本轮把同名文件哈希对上了〕。

### 1.2 真机端到端(盒子 `main`,Ubuntu 24.04.5 + XFCE)

| 轮次 | 内容 | 证据 |
|---|---|---|
| R21b / R21e / R21f / R21g | 两条真机 P0、裁决档位 +「装进存档」下拉缺陷、新产物复测、下载页→存档下拉→复制进存档全链路(存档内那份 md5 与源文件逐字节相同) | `scratch/box2/REPORT.md`、`scratch/box2/*.png` |
| R21i | 在 Linux 上装 .NET SDK **真跑测试**(修 3 个真问题)+ 补齐启动前账号闸门 | `f60720c` |
| R21j | 出包链路真跑 + 存档管理 chip 6/6 | `bade01d`、`3a63fbb` |
| R21–R21n 文档/CI | `4bf9473`(AppImage 裸跑 FUSE 实测)、`d0f15e2`(CI workflow + `.deb`)、`c00a475`(根 workflow 不断链)、`df87ace`(HTTPS→SSH 推送凭据) | `git log` |
| **R22 自更新全链路** | 本地 8123 清单 + 假包 + `TIPCL_UPDATE_URL` 真点一遍;`$APPIMAGE` 指向磁盘那支文件;下载 →「更新已下载」→「稍后重启」→ 当场替换(三处 sha256 一致、无 `.new`);负例(格式非法 sha 被拒、值错 sha 下载失败);`.deb` 装 `/opt` → `download-only` +「需要手动安装」 | `scratch/update-e2e/REPORT.md`(23,208 B) |

### 1.3 `PermissionNote` —— **已补做,不是缺口**

`ea57b74`(R21d 权限档位注入系统提示)+ `DECISIONS.md` **D-57**;`700cd21` 把 PARITY 里"唯一没搬的半部是 PermissionNote"改成 R21d 口径。
⇒ todo 里那条 `[pending] 决定 PermissionNote 是补做还是如实记为缺口` 是**陈旧标签**,事项本身闭环〔转述+提交可查〕。

### 1.4 发布/推送

R21k 首次真跑 CI 与修复(`cc4c39d`/`44e9917`)、`ac62394` 把 Linux/Avalonia 移植**合并进 main**、`c00a475` 根 workflow。当前 `main` = `origin/main` = `32c0b0b`〔本轮实测〕。

---

## 二、R22 / §9.14 的四条"如实留白"——现在是这个状态

原话在 `PROGRESS.md:1106-1107` 与 `docs/PARITY.md:663`(两处措辞一致):

| # | 原留白 | 现状态 | 依据 |
|---|---|---|---|
| ① | 「立即重启」分支(替换后顺手拉起新版)未验 | ✅ **已验,通过** | 本轮 T5:`t5.log:56-105` —— 点「立即重启」后 0.2 s 内老进程 `=== 正常退出 ===`、新版 `启动 pid=221929` 起来、磁盘文件 sha256 = 清单里那支包、无 `.new`、新窗口在。脚本 `scratch/update-e2e2/scripts/r3-20-t5-restart-branch.sh`,截图 `t5-03/04/05*.png` |
| ② | "失败在任务页是否也可见"未验 | ✅ **已验:可见** | 本轮 T4:失败任务在「下载任务」页列成卡片 `更新启动器` + 红字 `下载失败 TipCL-99.0.0.bin: sha256 校验失败: …`;截图 `scratch/update-e2e2/t4-30-tasks-page.png`。**注意**:下载页那个「任务 N」角标显示的是"进行中"数(`DownloadPage.cs:137-143`),失败后回 0 属预期,别拿它当判据 |
| 3 | 公网真实清单(而不是本机 8123)未验 | ❌ **仍不闭环**(原因见下) | `update-linux.json:5` 现值 = `"sha256": "REPLACE-WITH-THE-REAL-SHA256"`(**故意格式非法**,**保持不改**);`version` 仍是 `0.1.0-m3` —— ⚠️ **但客户端的更新判据比的不是这个号,而是 `AppInfo.APP_VERSION = 1.1.0`,所以任何 ≤ `1.1.0` 的版本号(含 `0.1.0-m3`、`v0.1.0-m4`)都不可能闭环**(详见 §3.1);`url` 指向 `releases/download/v0.1.0-m3/...`,而**这个 release 并不存在**(实测 404;仓库里 **0 个 Linux 资产**) |
| ④ | **真发一次版**(release 事件触发 `release.yml` 的 linux job、再手工改清单)未验 | ❌ **仍未做** | 需要真的切 tag / 发 release,属对外动作,需用户/lead 拍板 |
| ⑤ | (附加条)上一轮盒子上那支旧 `dist/` 不含本轮接线 | ✅ **已由 R22 重打解决** | PROGRESS:1108 自己记的;本轮实测 `dist/` 与盒子上那支同哈希,且真机 E2E 证明了「检查更新」按钮与静默检查都在 |

### ③ 为什么不能"顺手闭环"(写给拍板的人)

要让 3 成立,**必须切一个版本号严格大于 `1.1.0` 的 tag(例如 `v1.1.1`)** —— 更新判据是 `Updater.Newer(remote, local = AppInfo.APP_VERSION)`,清单 `version` 不**严格大于** `APP_VERSION`(`1.1.0`)就永远 `has_update=false`、界面只会显示「已是最新版本」;而且 `ValidSha256` **只在"版本更新"那条分支上才会被检查**,版本没超过就根本轮不到它。完整链路(拍板后照这个走):

> 切 tag(**> 1.1.0**,如 `v1.1.1`)→ release published → `release.yml` 的 linux job 上传 AppImage 并打印清单三行 → 把这三行填进 `update-linux.json` → `curl` 复核清单与资产都真的能拉到。
本轮**没有**把占位值改成真值是**有意的**:在 release 资产不存在的情况下填上 `48c3dcea…`,等于**伪造发布状态**,
下一个人拿到的会是"看起来配好了、其实拉到 404"的清单 —— 这比老实留着格式非法的占位值更坏。
另外这条**业已证明过一半**:占位值是"格式非法"这件事本身已经被真机验证 —— 客户端会当场拒绝并说明原因(本轮 T2,`t2-10-toast-02.png`)。
⇒ 需要拍板的只有一句:"要不要现在切一个 **版本号 > 1.1.0** 的 tag 发版"。

### 3.1 版本号:2026-11-14 已统一为 `1.1.0`(在此之前确实是两个常量打架)

| 常量 | 现值 | 谁在用 | 位置 |
|---|---|---|---|
| `AppInfo.APP_VERSION` | **`1.1.0`** | **更新判据**(`Updater.Newer(remote, local = AppInfo.APP_VERSION)`)、设置页「关于」里显示的版本、插件引擎兼容性检查 | `src/TipCL.Core/Launcher/AppInfo.cs:10` |
| `CoreInfo.Version` | **`1.1.0`** | **启动横幅**「=== TipCL 1.1.0 启动 pid=...」、HTTP UA、插件宿主默认版本 | `src/TipCL.Core/CoreInfo.cs:8`;横幅打印点 `src/TipCL.App/Common/AppLog.cs:67` |

两个 csproj 的 `<Version>` 也已是 `1.1.0`(`src/TipCL.Core/TipCL.Core.csproj:10`、`src/TipCL.App/TipCL.App.csproj:8`)。

**历史(别被旧日志误导)**:在这之前 `CoreInfo.Version` 与两个 csproj 都是 `0.1.0-m3`,而 `AppInfo.APP_VERSION` 是 `1.1.0` —— 也就是"横幅说 `0.1.0-m3`、更新判据说 `1.1.0`"。**2026-11-14 已统一为 `1.1.0`**(顺带把 `SmokeRunner` 的 HTTP UA 与 `PluginHost` 的插件宿主默认版本一起对齐)。所以:**旧记录里打 `0.1.0-m3` 不等于"构建过期";新构建一律打 `1.1.0`。** 发版说明里写版本号,以 `APP_VERSION`(现 `1.1.0`)为准。

⇒ ⚠️ **`update-linux.json` 里的清单 `version` 仍是占位 `0.1.0-m3`,不要动它**:它是"发布清单的版本",而 `Updater.Check` 比的对象是 `APP_VERSION`(现 `1.1.0`)⇒ 清单 `version` 必须**严格大于 `1.1.0`** 才有意义,否则永远是「已是最新版本」(详见 §2 第 3 条)。

---

## 三、R22 两处修复的复验结论(一句话版)

见 **`scratch/update-e2e2/REPORT.md`**(完整版)。摘要:

- **「失败要有界面反馈」生效**:T1(盘上原始 toast 截图)+ T4(本轮重跑,同一支产物,toast 帧与 T1 **逐字节相同**);
- **「占位 sha 改成格式非法」达成**:`update-linux.json` 现值非 64 位十六进制;真机 T2 当场拒绝 + toast「更新被拒绝 / 更新清单缺少有效 SHA-256,已拒绝自动更新」且**不弹**「发现新版本」;
- **正常路径没坏**:T3(稍后重启)+ T5(立即重启)双分支均通过;
- **测试**:`32c0b0b` 上独立复跑 **更新器 20/20**、**全量 466 = 245 + 220 + 1 跳过,0 失败**;此后 task-21/22 又补了 10 条用例,**现在是 476 = 248 + 227 + 1 跳过,0 失败**(见 `scratch/update-e2e2/REPORT.md §9`);
- **两处修复的证据层级(已补齐)**:第二处有单元测试兜底(`UpdaterTests`);第一处(`SelfUpdate.cs`)**原先只有真机证据** —— `ed419c2` 一个 `tests/` 文件都没动 —— **task-21 已补上 6 条用例**(`tests/TipCL.Tests/App/SelfUpdateFeedbackTests.cs`、`tests/TipCL.Tests/Core/UpdaterManifestGuardTests.cs`),并用「把 `SelfUpdate.cs` 里那行 toast 注释掉 ⇒ 失败用例立刻变红」做过 red/green 证明。

---

## 四、本轮明确**没有**做、留给别人的项

| 项 | 归属/原因 |
|---|---|
| 8 页无头冒烟矩阵(R22 之后未重跑) | 与本焦点无关;`tools/verify-linux.sh` 只覆盖 `library/download/plugins/settings` + 深色,"8 页"的确切集合记录里没定死。本轮在真机 GUI 上实际渲染过 设置/下载/下载任务 三页(0 意外 WARNING/ERROR),不能顶替矩阵 |
| 「立即重启」在 `.deb` / `download-only` 形态下的表现 | 该形态 `ApplyStaged` 直接返回 `download-only`、不替换文件;上一轮验过对话框,`restart=true` 未验 |
| 失败任务在**启动页底部「下载任务」区块**是否可见 | 该区块 `DefaultVisible = false`(`LaunchPage.cs:358`),未点开核对 |
| 更新失败 toast 的单元测试 | 需改 `tests/**`,不在本轮授权内 |
| 真模型端到端(需 API key)、手机同步、`CrashUi` 真实数据、Java 大下载、媒体=截图的「打开」、Windows CI job 根因、两条 GitHub 平台级提示 | 会话早先就记的"条件不具备/未定位"项,**本轮未动** |
| `PROGRESS.md` / `docs/PARITY.md` / `README.md` 的措辞更新 | **不是我的地盘**(task-9 / task-10)。本节只给事实口径,见 `scratch/update-e2e2/REPORT.md §7` |

---

## 五、下一位接手要注意的四条(踩过的坑)

1. **对话框宽度随清单 `notes` 变化** ⇒ 固定比例点「下载更新」会静默点到「以后再说」。正确判据 = `对话框右边缘 - 52px`,并用 `http.log` 的 `GET /pkg.AppImage` 或日志 `任务开始` **验证命中**(详见复验报告 §4.1)。
2. **本机 `dotnet test` 会假红**:测试宿主的 `TestIsolation` 在 `%LOCALAPPDATA%\Temp` 建目录会被沙箱拒,当时 466 条全 `UnauthorizedAccessException`(现在用例更多,坑一样);把 `TEMP`/`TMP` 指到工作区内即全绿(复验报告 §3.3;task-21/22 按这个办法实测 476 全绿)。
3. **盒子 `rsh` 会掐长任务**(`h2 protocol error`):脚本自己 `exec > 日志`,所以"本地无回显"不等于"没跑";长脚本请 detached + `*.done` 标记轮询。
4. **本机没有 PowerShell 7**、默认 `ExecutionPolicy = Restricted`:`_cmp/box/*.ps1` 一律用 `powershell -NoProfile -ExecutionPolicy Bypass -File …`;`run.ps1`/`push.ps1`/`pull.ps1` 的绝对路径坑已在本轮修掉(行号见复验报告 §6.2)。

---

## 六、交付物路径

| 内容 | 路径 |
|---|---|
| 复验报告(替 `update-e2e2/REPORT.md`) | `scratch/update-e2e2/REPORT.md` |
| 复验原始证据 | `scratch/update-e2e2/`(round-2 的 T1/T2/T3 + 本轮 T4/T5 日志与截图) |
| 上一轮(round-1)R22 自更新 E2E 报告 | `scratch/update-e2e/REPORT.md` |
| 复现脚本 | `scratch/update-e2e2/scripts/{20-t1,20-t2,20-t3,r3-20-t4-…,r3-20-t5-…}.sh` |
| 归档产物 | `dist/TipCL-linux-x64.AppImage`、`dist/tipcl_0.1.0-m3_amd64.deb` |
| 本文件 | `docs/HANDOFF-d95a9472.md` |
