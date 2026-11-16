# TipCL R30 发版草案(RELEASE-DRAFT)

> **状态:仍是草案;版本号一项已由用户拍板生效(R31,见 §0)。** 本文件只记录"发版那天要做什么、要填什么",**不构成任何发布动作**。
> 本轮(R30-WP1)**没有** push、**没有**打 tag、**没有**发 release,**没有**改 `update.json` / `update-linux.json` 的任何一个字节。
> 落笔:`2026-11-15`(本地 +08:00),`R30-WP1` 工作包;工作树基线 HEAD = `25c4e64c0104d83514dfbcf31b684f7e302f0349`(见 §1.5 的"演练产物不可复现"说明)。
> **`R30-WP6c` 补记(2026-11-15,落笔时工作树 HEAD = `34006c6`)**:按总验收报告 `progress/R30-ACCEPT.md` §4 的返工单 `R-WP5-03 / R-WP5-04 / R-WP5-05 / R-WP5-07` 修正了本文件里若干**过期或与 HEAD 不符的口径**,改动集中在 §1.2 / §1.2.1 / §1.2.2 / §2.1(W5)/ §3.2 / §3.5 / §4 / §5。**本轮没有改任何版本号生效值,清单三行仍是 `<<...>>` 占位。** **⚠️ HEAD 会随并行提交继续前进**(核对期间已从 `3fdee8a` 走到 `09ea6eb`);全文凡"HEAD = xxx"的表述一律以**发版那天冻结的那个 commit**为准,见 §1.5 末条。
> **`R31` 补记(2026-11-15,施工成员 `rel-version`)**:用户**已拍板版本号 = `1.2.0`**(口径与依据见 §0),本文件里"版本号是占位 / 待拍板 / 拍板后再替换一次"的写法**全部改成已定稿生效**;**版本号的值一个都没改**(§1.1 的 13 条本来就是 `1.2.0`)。同一轮按拍板落地了 §1.2.1 的硬编码版本串:**形态 A 与 B 的活字面量已改为派生自 `AppInfo.APP_VERSION`**(提交 `5056f5b` / `6ea4a24`,逐处清单见 §1.2.3),**形态 C(`-Dminecraft.launcher.version=1.0.10`)按拍板明确保留不改**;**清单三行仍未填**(那是发版当天在真资产上算出来的,见 §2.3)。

---

## 0. 先把口径钉死:版本号**已定稿生效 = `1.2.0`**(用户 2026-11-15 拍板)

| 项 | 值 | 说明 |
|---|---|---|
| **生效版本号(已定稿)** | **`1.2.0`** | 用户 **2026-11-15 明确拍板**取 `1.2.0`,依据就是下面 5 条推荐理由(+ 同号约束)。§1.1 的 13 个落点已经是这个值 ⇒ 本文件原先写的"占位号 / 待拍板 / 拍板后再替换一次"**全部作废**,改为**生效值**。 |
| 这个号的来源 | WP-1 推荐号 `1.2.0` + 用户 2026-11-15 拍板 | 推荐理由见下。 |
| 硬约束(不可违反) | **必须严格大于 `1.1.0`** | 更新判据是 `Updater.Newer(remote, local = AppInfo.APP_VERSION)`,**严格大于**;填成等于则预览永远是「已是最新版本」(实测见 §3.4 case 2)。`1.2.0 > 1.1.0` ✅ |
| 拍板后还要不要再改一次 | **不要** | 13 个落点**已是**生效值,与拍板号一致;R31 只把口径从"占位"改成"生效",**一个版本号的值都没动**。清单见 §1.1 表(13 行)+ §1.2。(**`R-WP5-04`/`R30-WP6c` 更正**:此处原写"12 个落点",与 §1.1 的 13 行、§1.2 的"13 条"自相矛盾;实测落点 = **13**,逐行核对见 `progress/evidence/wp5c/veri-docs.md` §3.1。) |

**推荐 `1.2.0` 的理由**

1. 这是**加功能但不破坏格式**的版本:布局 v2 把 v1 文件语义**冻结**为兼容子集(`ui_page_layouts.json` 继续原样写),老客户端读得懂、老用户的布局不丢 ⇒ 按语义化版本该走 **MINOR**(1.2.0),不是 MAJOR。
2. 不该走 PATCH(`1.1.1`):本次带的是一整套新布局系统与 AI 排版能力,不是修补。
3. 两棵树保持**同号**:2026-11-14 刚把 Windows 与 Linux 统一到 `1.1.0`,本轮两个仓库一起抬到 `1.2.0`,继续维持"一个产品一个号"。
4. 与"是否移植 Linux"**不冲突**:布局 v2 只在 Windows(§4 口径),但 Linux 侧本轮同样有自更新、账号闸门等 NET CHANGE;即便用户以后决定移植,`1.2.0` 依然成立(Linux 只是多一个功能),不需要再换号。
5. 用户想换号也没有阻碍,**但已经不需要换了**:唯一硬约束就是 > `1.1.0`;用户 2026-11-15 已拍板 `1.2.0`,13 个落点就是这个值(万一以后要换,才需要按 §1.2 做一次替换)。

### 0.1 两步走,不许合成一步(硬约束)

- **第 1 步(本轮已做)**:把**产品版本号**抬到 `1.2.0`。
- **第 2 步(本轮明确不做)**:把 `update.json` / `update-linux.json` 的清单 `version` 抬到 `1.2.0` 以上。
- **为什么必须分开**:`tests/TipCL.Tests/Core/UpdaterManifestGuardTests.cs` 钉住了「清单 `version` **≤** 客户端版本时,`sha256` 必须是**格式非法**的占位值」。在没真发版的前提下把清单 `version` 抬到客户端之上,等于**伪造发布状态**,该守卫会当场判红。所以本轮只做第 1 步。

---

## 1. 版本号:落点、演练实测、以及"发版那天还要改什么"

> 本章有三类落点,**别混**:
> 1 **§1.1 的 13 条** —— 已经是 `1.2.0`,并且**已定稿生效**(2026-11-15 用户拍板,见 §0)⇒ 不再需要任何替换;
> 2 **§1.2.1 的 22 处硬编码版本串**(每棵树 11 处:UA / 客户端头 / `-Dminecraft.launcher.version`)**已按用户拍板处置**:形态 A/B 的**活**字面量改成派生自 `AppInfo.APP_VERSION`(R31 两笔提交,见 §1.2.3),形态 C **刻意保留 `1.0.10`**、死常量按原样保留;
> 3 **§1.2.2 的布局档 `app` 字段**(程序集版本派生,只登记提示,不改)。

### 1.1 本轮实际改掉的 13 个落点(已提交,见 §6 提交号;值 = 拍板生效值)

| # | 文件:行 | 现值(改前) | 目标值(改后) | 消费者 |
|---|---|---|---|---|
| 1 | `src/TipCL.Core/Launcher/AppInfo.cs:10` | `APP_VERSION = "1.1.0"` | `"1.2.0"` | **更新判据**、关于页、插件兼容、反馈 `app_version`、sysinfo |
| 2 | `src/TipCL.Core/Launcher/AppInfo.cs:16` | `LAUNCHER_VERSION = "1.1.0"` | `"1.2.0"` | 拼 `--launcher_version` 传给 Minecraft |
| 3 | `src/TipCL.Core/Launcher/AppInfo.cs:18` | `__version__ = "1.1.0"` | `"1.2.0"` | 兼容字段,无消费者(R31 前被记作"占位";值本身已是生效值) |
| 4 | `src/TipCL.Core/CoreInfo.cs:8` | `Version = "1.1.0"` | `"1.2.0"` | **启动横幅**(`AppLog.cs:67`);**`tools/deb.sh:49-51,64` 从它取 .deb 版本号** |
| 5 | `src/TipCL.Core/TipCL.Core.csproj:10` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 |
| 6 | `src/TipCL.App/TipCL.App.csproj:8` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 |
| 7 | `tests/TipCL.Tests/SmokeTests.cs:17` | `Assert.Equal("1.1.0", CoreInfo.Version)` | `"1.2.0"` | **守卫用例**(不是 bug,必须与新号同一个 commit) |
| 8 | `windos/src/TipCL/TipCL.csproj:17` | `<Version>1.1.0</Version>` | `1.2.0` | 程序集版本 + `AssemblyInformationalVersion` + **第 4 个消费者 `LayoutStoreV2.AppVersion()`**(它会写进用户布局档的 `app` 字段,见 §1.2.2) |
| 9 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:9` | `APP_VERSION = "1.1.0"` | `"1.2.0"` | **更新判据**、关于页(`SettingsPage.cs:2030`)、插件版本、反馈、sysinfo |
| 10 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:15` | `LAUNCHER_VERSION = "1.1.0"` | `"1.2.0"` | 拼 `--launcher_version` |
| 11 | `windos/src/TipCL/Core/Launcher/AppInfo.cs:17` | `__version__ = "1.1.0"` | `"1.2.0"` | 兼容字段,无消费者 |
| 12 | `src/TipCL.App/Session/SmokeRunner.cs:592` | 字面量 `"TipCL/1.1.0"` | **派生** `"TipCL/" + AppInfo.APP_VERSION` | 冒烟网络探针的 HTTP UA |
| 13 | `src/TipCL.Core/Plugins/PluginHost.cs:43` | 默认参数字面量 `"1.1.0"` | **派生** `AppInfo.APP_VERSION` | 插件宿主默认 `tipclVersion`(喂 `PluginManifest.EngineOk`) |

> 12 / 13 号是 WP-0 点名的两个"隐身硬编码"。本轮按"派生"处理(不加 `using`、只改那一行,用全限定名),派生等价性有独立实测,见 §3.3。

### 1.2 版本号落点(**值已定稿,不再需要替换**;含 R30 当时没动、也不该由 WP-1 动的项)

> **`R-WP5-04` / `R30-WP6c` 更正**:这张表原先有三行写"仍写 `1.1.0` / 未动",**现在都已过期** —— 提交 `7f7cab9`(`R30-WP1(补2)`)已经把那三处的版本数字改成 `1.2.0`。下表按 **落笔时 HEAD(`34006c6`,之后仓库仍在推进)的当前事实**重写,并把已闭合的项标出来。

| 落点 | 现状(HEAD 实测) | 谁改 |
|---|---|---|
| §1.1 的 13 条 | 已写成 `1.2.0` = 拍板生效值 | 已闭合(R31 只改口径,没改值) |
| `tests/TipCL.Tests/SmokeTests.cs:14`(注释) | **已由 `7f7cab9` 处理**:原句"2026-11-14 起移植侧产品版本统一为 1.1.0"后面补上了",2026-11-15 抬到 `1.2.0`"(把 2026-11-14 那次统一保留为历史事实)⇒ **无需再动** | 已闭合 |
| `tests/TipCL.Tests/Core/UpdaterManifestGuardTests.cs:66,90,93`(注释) | **已由 `7f7cab9` 处理**:`:66` 现写"`AppInfo.APP_VERSION` 已经到 `1.2.0`"、`:90`/`:93` 现写 `1.2.0` ⇒ **无需再动** | 已闭合 |
| `docs/README.md:19-20` | **已由 `7f7cab9` 处理**:现写"版本号 `1.2.0` 必须三处一致",括注里同时保留了 `0.1.0-m3`→`1.1.0`→`1.2.0` 的沿革 ⇒ **无需再动** | 已闭合 |
| `tests/TipCL.Tests/Core/UpdaterTests.cs:71` 的 `[InlineData("1.1.0","1.0.99",true)]` | **测试数据**(不是产品常量):`1.1.0` 只是"随便一个旧号" | 不必改 |
| `windos/tests/TipCL.Tests/LayoutV2CompatTests.cs:214` 夹具里的 `"app": "1.1.0"` | **测试夹具**,三段号;`app` 只被解析、不被比较(见 §1.2.2) | 不必改 |
| `DECISIONS.md:132`「`APP_VERSION=1.1.0`」 | 历史决策记录里的**已过期陈述**(文档漂移,不是活常量) | 不在本包地盘;是否回填由 lead 定 |
| `update.json` / `update-linux.json` 的 `version` | `1.1.0` / `0.1.0-m3`(活值,**故意未改**) | **只有真发版时才改**,见 §2 |
| `windos/src/TipCL/app.manifest:3` 的 `version="3.2.0.0"` | **不要动** | 这是 Win32 清单 schema 版本,不是产品版本 |

⇒ 上面"已闭合"的三处都是**注释/文档里的数字**,`7f7cab9` 只换数字、零代码语义改动。所以:要替换的**活落点**只有 §1.1 的 13 条 + 两份清单(且 13 条**已是拍板生效值,不用再替换**);**§1.2.1 那 22 处硬编码版本串按用户拍板单独处置,R31 的结果见 §1.2.3。**

### 1.2.1 ⚠️ `R-WP5-03`:两棵树各有 **11 处硬编码版本串**(范围里共 22 处),既不在 §1.1,也不在 WP-0 §2.5 的普查里

> **本节结论(标题口径,已按用户拍板更新)**:这 22 处**已处置完毕** —— 形态 A/B 的**活**字面量改成派生自 `AppInfo.APP_VERSION`(R31,提交 `5056f5b` / `6ea4a24`,清单见 §1.2.3),形态 C **由用户拍板刻意保留 `1.0.10`**,死常量按"不动"处理。下面是 R30 当时登记建议的原口径(保留为历史记录)。
> **R30 当时的结论(历史)**:这 22 处**发版时必须一并处理,或由用户明确批准刻意保留 `1.0.10`**;当时 lead 裁定"产品行为变更一律不在无人值守期间动手"⇒ 本节只做"登记 + 建议",列出的每一处**当时一个字节没改**;不处理的后果是发版后发往外部服务的 UA / 客户端头里仍是 `TipCL/1.0.10`、传给游戏 JVM 的启动参数里仍是 `1.0.10`,而关于页与更新判据显示新号 —— 又是一个"两个源打架"的形状(与 §1.4 讲的 1.0.15 事故同类)。**这个后果已在 R31 消除(A/B 部分);形态 C 是拍板刻意保留。**

**我自己查到的确切清单**(命令与原文,均在本 HEAD 上跑;`docs/RELEASE-DRAFT.md` 自身不在范围内):

```
$ cd TipCL; git grep -n '1\.0\.10' HEAD -- src windos/src          # 改前(R30):命中 10 行(每棵树 5 行)
$ cd TipCL; git grep -n 'TipCL/1\.0' HEAD -- src windos/src        # 改前(R30):命中 20 行(每棵树 10 行)
```

**R31 改后实测(HEAD = `6ea4a24`,范围同上)**:

```
$ cd TipCL; git grep -n 'TipCL/1\.0' HEAD -- src windos/src        # 命中 6 行(每棵树 3 行)
$ cd TipCL; git grep -n '1\.0\.10' HEAD -- src windos/src          # 命中 4 行:FeedbackDefaults ×2(死常量)+ McLauncher ×2(形态 C,刻意保留)
```

⇒ 改后剩下的 6 行**全是 0 调用点的死常量**(`FeedbackDefaults.CLIENT_HEADER`、`Net.SHORT_USER_AGENT`、`MicrosoftAuthenticator.UA`,各树各 1),外加形态 C 的 2 行;**没有任何一条请求路径再发 `1.0.10`。**

去重后**每棵树 11 处、两棵树共 22 处**,形态三种:

| 形态 | 特征 | Linux 树(`src/...`)确切 `文件:行号` | Windows 树(`windos/src/...`)确切 `文件:行号` |
|---|---|---|---|
| **A. `"TipCL/1.0.10"`**(各 4 处) | UA / 客户端头 | `TipCL.Core/Launcher/Ai/Defaults.cs:37`(`CLIENT_HEADER`)、`TipCL.Core/Launcher/FeedbackDefaults.cs:7`(`CLIENT_HEADER`)、`TipCL.Core/Launcher/Mods.cs:181`(`User-Agent` 头)、`TipCL.Core/Launcher/Net.cs:66`(`USER_AGENT`) | `TipCL/Core/Launcher/Ai/Defaults.cs:36`、`TipCL/Core/Launcher/FeedbackDefaults.cs:7`、`TipCL/Core/Launcher/Mods.cs:180`、`TipCL/Core/Launcher/Net.cs:65` |
| **B. `"TipCL/1.0"`**(各 6 处) | 短 UA(短标识 + Microsoft 登录链) | `TipCL.Core/Launcher/Net.cs:43`(`SHORT_USER_AGENT`)、`TipCL.Core/Launcher/AuthHttp.cs:93`、`TipCL.Core/Launcher/MicrosoftAuthenticator.cs:15`(`const UA`)、`:25`、`:30`、`:39` | `TipCL/Core/Launcher/Net.cs:42`、`TipCL/Core/Launcher/AuthHttp.cs:92`、`TipCL/Core/Launcher/MicrosoftAuthenticator.cs:14`、`:24`、`:29`、`:38` |
| **C. `-Dminecraft.launcher.version=1.0.10`**(各 1 处) | **传给游戏 JVM 的启动参数** | `TipCL.Core/Launcher/McLauncher.cs:650` | `TipCL/Core/Launcher/McLauncher.cs:649` |

**确实随包发出去(我复算)**:对演练产物 `progress/evidence/wp1/TipCL-compact.exe` 按 UTF-16LE 解码后作非重叠计数,`TipCL/1.0.10` 命中 **3 处**、`TipCL/1.0` 命中 **5 处**;ASCII 解码下两者都是 **0 处**(这些字面量在 exe 里是 UTF-16 形式)。⇒ 形态 A/B 至少有一部分**真的进了交付件**。(`TipCL/1.0` 是 5 而不是 6,原因见下表 B 行:源码里有一个**死常量**被编译器折掉了。)

**逐形态:现值 / 目标值 / 建议改法(一行 diff 示例)/ 风险**(两类风险必须分开看):

| 形态 | 现值 | 目标值(建议) | 建议改法(一行 diff 示例) | 风险 |
|---|---|---|---|---|
| **A**(各 4 处) | `"TipCL/1.0.10"` | `TipCL/<最终版本号>` | `- public const string CLIENT_HEADER = "TipCL/1.0.10";`<br>`+ public const string CLIENT_HEADER = "TipCL/" + AppInfo.APP_VERSION;`(`Mods.cs:181` 与 `Net.cs:66` 同形,只是字段名/上下文不同) | **低风险,但会改变对外可见字符串** —— 实测的对外调用点:`Ai/Client.cs:176`(AI 网关的 `X-TipCL-Client` 头)、`Ai/VisionCaps.cs:265`(视觉接口的 `User-Agent`)、`Ai/McpServer.cs:269`(MCP 客户端看到的 serverInfo `version` = `Defaults.CLIENT_HEADER.Replace("TipCL/","")`)、`Net.cs:284` / `Source.cs:143`(通用 HTTP 请求)、`Mods.cs:181`(模组源请求)。若对方按 UA 统计或限流,发版后那边会看到口径跳变一次。**还要分开说清**:`FeedbackDefaults.cs:7` 那个同名 `CLIENT_HEADER` **目前 0 调用点**(反馈实际走 `Feedback.cs:126-127` 的 `X-PyMCL-Client: PyMCL/<APP_VERSION>`,已经是派生值;代码注释还写明 ingest 服务器只认这个头、`X-TipCL-Client` 会被 403)⇒ 它是**死常量**,改不改都不改变对外行为。**保留不动的话,"外部永远看到 `1.0.10`"这个事实必须写进发版说明** |
| **B**(各 6 处) | `"TipCL/1.0"` | `TipCL/<最终版本号>`(**或明确批准保留短号**) | 同 A 的派生写法。⚠️ 两处要注意:1 `MicrosoftAuthenticator.cs:15`(Win `:14`)是 `const`,改成派生会引入对 `AppInfo` 的依赖(或需改成 `static readonly`);2 `Net.cs:43`(Win `:42`)的 `SHORT_USER_AGENT` **目前 0 调用点**(我全树 grep 只有它自己的声明)⇒ 它是**死常量**,真正要改的只有 **5 处**(这也正是 exe 里 `TipCL/1.0` 只命中 5 处的原因) | **低风险,但更"对外"**:`AuthHttp.cs` / `MicrosoftAuthenticator.cs` 是 **Microsoft 登录链**,UA 是登录请求的一部分。协议语义不受影响,但"动登录链上对外的字面量"建议**单独一笔提交 + 一次真机登录验证**,不要塞在发版前最后一刻 | (**R31 更正**:这里的"真正要改的只有 **5 处**"把 `MicrosoftAuthenticator.cs:15`(Win `:14`)的 `const UA` 算成"活的";实测它也是 **0 调用点**的死常量 ⇒ B 的**活落点 = 4 处/树**,见 §1.2.3) |
| **C**(各 1 处) | `-Dminecraft.launcher.version=1.0.10` | `1.2.0`(与 `AppInfo.APP_VERSION` 同号) | `- list3.Add("-Dminecraft.launcher.version=1.0.10");`<br>`+ list3.Add("-Dminecraft.launcher.version=" + AppInfo.APP_VERSION);` | **⚠️ 必须单独提示:这是传给游戏 JVM 的启动参数** —— 游戏、模组、整合包脚本、以及玩家贴出来的启动日志/崩溃报告都会读到它。它**不发给外部 HTTP 服务**,但会改变"游戏侧看到的启动器版本"(模组按启动器版本做分支的情形真实存在)。⇒ **这一条只能由用户明确拍板**:要么改成与版本号同号,要么在这里写明"**刻意保留 `1.0.10`**",否则发版后没人知道这是有意为之。**R31 结果:用户拍板"刻意保留 `1.0.10`"(代码不动),本行即定案口径** |

**给 lead 的推荐口径(当时"仅建议,未拍板";**已被用户采纳**)**:形态 A 与 B 都改成**与 `AppInfo.APP_VERSION` 派生**,和 §1.1 #12 的 `SmokeRunner` UA 完全同形(`src/TipCL.App/Session/SmokeRunner.cs:592`:`"TipCL/" + TipCL.Core.Launcher.AppInfo.APP_VERSION`)—— 这样"一个产品一个号"才闭环,且**不新增任何依赖**(用全限定名,不加 `using`);形态 C **单独拍板**,排在"做完 A/B 之后、发版之前";若用户不想在发版日动登录链,可先做 A,把 B 显式记为"刻意保留"、C 显式记为"刻意保留"。

> **R31 落地结果(2026-11-15)**:用户采纳了上面的推荐 —— **A 与 B 都已改派生**(A 每树 3 处活落点、B 每树 4 处活落点;`Ai/Defaults.cs` 的 `CLIENT_HEADER` 由 `const` 改成 `static readonly`),**形态 C 由用户拍板刻意保留 `1.0.10`**,**死常量保持原样**;逐处清单、提交号与"未验证项"见 §1.2.3。上表其余文字保持 R30 当时的原文(除上面两处 R31 更正标记)。

### 1.2.2 已知口径提示(`R-WP5-07`):用户布局档里的 `app` 字段来自**程序集版本**,不是 `APP_VERSION`

- **写侧**:`windos/src/TipCL/Core/Launcher/Layout/LayoutStoreV2.cs:139`(v1→v2 迁移)与 `:166`(保存)都写 `doc.App = AppVersion()`;`AppVersion()` 在 `:531-549`,取的是 `typeof(LayoutStoreV2).Assembly.GetName().Version`(即 csproj `<Version>` 生成的**程序集版本**),**不是** `AppInfo.APP_VERSION`。落盘在 `windos/src/TipCL/Core/Launcher/Layout/LayoutDocV2.cs:115`(`dict["app"] = App;`)。
- **读侧**:`LayoutDocV2.cs:167-169` 只 `LayoutJson.Text(app, "")` **解析**、`LayoutSchema.cs:350` 只做 `doc.App ??= ""` 的空值兜底,**全树没有任何一处比较它** ⇒ **不影响更新判据,也不影响关于页**。
- **形态(三段还是四段)**:`AppVersion()` 在 `version.Revision > 0` 时返回**四段** `ToString()`,否则返回 `Major.Minor.Build` **三段**。本机实测演练产物 `TipCL-compact.exe` 的 `AssemblyName.GetName().Version = 1.2.0.0`,**`Revision = 0`** ⇒ 按代码路径走的是**三段**分支 ⇒ 导出 / 落盘的就是 **`"app": "1.2.0"`**;这与 `windos/tests/TipCL.Tests/LayoutV2CompatTests.cs:214` 夹具的三段写法(`"app": "1.1.0"`)一致。
- ✅ **已定案:三段号 `1.2.0`**(由返工复验定案，`R30-WP6c-03` 归档)。依据就是 `AppVersion()` 的**函数体本身**:`version.Revision > 0` 才 `return version.ToString()`(四段)，否则 `return version.Major + "." + version.Minor + "." + version.Build`(三段)；本版程序集版本是 `1.2.0.0` 而 **`Revision == 0`** ⇒ 走三段分支 ⇒ 导出 / 落盘是 **`"app": "1.2.0"`**(与产品号同形)。
- 📌 **口径注记(免得下一个人再翻案)**:`progress/R30-ACCEPT.md` §4 `R-WP5-07` **原先**写"导出的 `"app"` 是**四段号 `1.2.0.0`**"，那是只读了赋值行 `Assembly.GetName().Version`(类型是四段)**而没读 `AppVersion()` 的分支**；该报告已在自身"自我更正"一节里改成三段，总验收的返工复验也按三段定案。`R30-WP6c` 当时把它标成"待复核、我复现不出来"是**从保守出发**(我没真跑 GUI、没真导出布局档)，现按复验定案 —— **不要再按"四段"写发版说明。**
- **提示本身(降级为"已澄清")**:`app` 是 Windows csproj `<Version>` 的**第 4 个消费者**(另三个:程序集版本、`AssemblyInformationalVersion`、`FileVersion`)；值形是**三段 `1.2.0`、与产品号同形**，所以"一个产品一个号"**没有被破坏**，不再有"多一个形状"的问题。**本轮只登记，不改代码。**

### 1.2.3 R31 落地记录(2026-11-15):形态 A/B 已改派生,形态 C 保留

**依据(用户 2026-11-15 拍板)**:版本号 = `1.2.0`;硬编码版本串**形态 A 与 B 都改成派生自 `AppInfo.APP_VERSION`**,**形态 C(`-Dminecraft.launcher.version`)明确保留不动**。

**两笔提交(本地,未 push)**

| 提交 | 内容 | 落点(改后行号不变) |
|---|---|---|
| `5056f5b` | 形态 A:活字面量改派生(每树 3 处,共 6 处) | Linux `Ai/Defaults.cs:37`、`Mods.cs:181`、`Net.cs:66`;Windows `Ai/Defaults.cs:36`、`Mods.cs:180`、`Net.cs:65` |
| `6ea4a24` | 形态 B:**登录链**,活字面量改派生(每树 4 处,共 8 处);**单独一笔,便于单独 revert** | Linux `AuthHttp.cs:93`、`MicrosoftAuthenticator.cs:25`/`:30`/`:39`;Windows `AuthHttp.cs:92`、`MicrosoftAuthenticator.cs:24`/`:29`/`:38` |

**派生写法**(与 `SmokeRunner.cs:592` 完全同形,**不新增 `using`**):`"TipCL/" + TipCL.Core.Launcher.AppInfo.APP_VERSION`。

**类型只变了 1 处**:`Ai/Defaults.cs` 的 `CLIENT_HEADER`(`public const string` → `public static readonly string`)。它原来的 3 个消费者 `Ai/Client.cs:176`、`Ai/VisionCaps.cs:265`、`Ai/McpServer.cs:269` **都是运行时取值**,没有 `const` 上下文消费者。其余各处声明类型未变(`Mods.cs:181` 是字典字面量;`Net.cs:66` 赋给的 `USER_AGENT` 本来就是 `static readonly`)。

**改后源码里 `TipCL/1.0` 只剩 6 行,全部是 0 调用点的死常量 ⇒ 按"死常量不动"处理**

| 死常量 | 处 | 为什么不动 |
|---|---|---|
| `FeedbackDefaults.CLIENT_HEADER`(形态 A) | 两树各 1 | 0 调用点(反馈实际走 `Feedback.cs:127` 的 `X-PyMCL-Client`,已是派生值);且是 `public const`,改派生会牵动类型 |
| `Net.SHORT_USER_AGENT`(形态 B) | 两树各 1 | 0 调用点 |
| `MicrosoftAuthenticator.UA`(形态 B) | 两树各 1 | **0 调用点(本次复算确认)**,见 §1.2.1 的 R31 更正 |

> ⚠️ 死常量虽然不再被任何请求路径读取,但 `const` 的字面量**仍可能以元数据形式留在产物里** ⇒ 发版说明里**不要**写"产物里已经没有任何 `TipCL/1.0` 字样",要写"**没有任何请求路径会发出旧号**"。

**⇒ 这一版对外显示什么号**:形态 A(通用 HTTP UA、AI 网关的 `X-TipCL-Client`、视觉接口 UA)与形态 B(Microsoft 登录链 UA)在运行时发 **`TipCL/1.2.0`**(MCP 客户端看到的 serverInfo `version` 是去掉 `TipCL/` 前缀后的 **`1.2.0`**);`Net.USER_AGENT` 的完整值是 `TipCL/1.2.0 (python; +minecraft launcher)`;**传给游戏 JVM 的仍是 `-Dminecraft.launcher.version=1.0.10`(刻意保留)**。

**⚠️ 未验证项**:形态 B 动的是**登录链**上的 UA,本机**没有可用账号/真机** ⇒ 这一笔**未经真机登录验证**;现有证据只到"两棵树编译 0 错误 + 两个产物(`TipCL.exe` / `TipCL.Core.dll`)里都能字节级扫到折叠后的 `TipCL/1.2.0` 字面量 + 两套全量测试全绿(487 / 465)",**没有真正走过 device-code 登录**(见 `progress/R31-A.md`)。**单独 revert 这一笔**:`git revert --no-edit 6ea4a24`(该笔只碰 `AuthHttp.cs` / `MicrosoftAuthenticator.cs` 两个文件,不依赖本笔之外的任何改动)。

### 1.3 `1.0.15` 残留复核(1.1 出过"应用内显示 1.0.15、反复提示更新"的事故)

全库 `git grep -n '1\.0\.15' HEAD` 命中 **9 行 / 3 文件**,其中 **5 行是本草案自己在引述**(§1.3 / §1.4);**排除草案自身后是 4 处,全部在历史文档里,0 处代码**(复现时请排除本文件,否则会数出 9):

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
- ⚠️ **最终件必须在"冻结且干净"的那个 commit 上出**：并行成员会把 HEAD 一直往前推(核对期间就从 `3fdee8a` 走到 `09ea6eb`)，**别在被追着的树上出包**；出包前先确认 `git status` 干净、HEAD = 你打算写进清单的那个 commit，再按 §2.1 在同一份产物上算 sha256。

---

## 2. 两条发版链路(照这个走)

> 前置:**版本号已由用户拍板生效 = `1.2.0`**(§1.1 的 13 条就是这个值,**不需要再替换**;口径见 §0),并且**已提交**;下面每一步都在该 commit 上执行。

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

**⚠️ W5 的口径(`R-WP5-05` 已改写;旧口径「CI 产物比标准件大 512 B」在本 HEAD 不成立)**
`release.yml` 的 windows job 是 `dotnet build windos/src/TipCL/TipCL.csproj -c Release`(第 39 行),**没有** D-14 的 `-p:DebugType=none -p:DebugSymbols=false`。
而 `windos/docs/DELIVERY-2026-11-14.md` §6 第 8 条据此说:这样出来的 `bin` 里那个 exe 比标准件**大 512 B**(且同目录带 `TipCL.pdb`),**不能拿来切交付件**。**这条在本 HEAD 复测不成立**(独立子代理实测,见 `progress/R30-ACCEPT.md` §2.11):

```
(1) dotnet build windos/src/TipCL/TipCL.csproj -c Release                        → 5,534,208 B  sha256 15C1D963FB93...D833
(2) powershell -File windos\tools\build-both.ps1 -Compact(清 bin/obj + D-14 参数) → 5,534,208 B  sha256 F58E9AB87273...BB8C
(3) 紧接着再 dotnet build(增量)                                                  → 5,534,208 B  sha256 15C1D963FB93...D833(与 (1) 逐字节相同)
```

⇒ **两条路径产物体积相同(都是 5,534,208 B)但字节不同(sha256 不同)**;而**同一条构建路径重跑是逐字节可复现的**。所以真正的风险不是"哪份更大",而是:
**清单里的 `sha256` 必须钉死在"这次真正发布所用的那一份产物"上,不许事后另跑一次构建去取哈希。**
⇒ 两条路径仍然**只能选一条,不要混**:1 **发布时由人把 W1/W3 出来的标准件手工上传**(推荐,零改动)—— sha256 就在这份手工产物上算,**天然自洽,这条推荐依然安全**;2 改 `release.yml` 给 Windows job 加上那两个参数(需 `.github/` 的属主动手 —— **不是本包的地盘**);即使这么改,也必须在**该次 CI 真正跑出来、真正上传的那两个文件**上算 sha256(`release.yml:47` 就是 `sha256sum TipCL.exe`),**绝不能拿本机另跑一份的哈希去填**。
**本轮不碰 `release.yml`**,只把口径记在这里(R-WP5-05)。
> 附:`release.yml:46` 只 `cp` 了 exe(同目录的 `TipCL.pdb` 不入发布件)—— 与体积差无关;"标准件"的定义仍以 W1/W3 为准。
> 附:`windos\tools\build-both.ps1` 自己的注释里仍有 `±512 B` 的说法(`:9`、`:39`、`:57`,例如"`-p:DebugType=none` — 不产出 pdb(D-14:使 exe 再 -512 B)"),**与本 HEAD 实测(体积相同)不符**;该脚本不是本包地盘,**未改**,登记为待复核项。

**⚠️ 可选清理项(低优先级,发版前不要动):`build-both.ps1` 的 `-Dist` 用了硬编码绝对路径 + 旧布局名。**
`windos\tools\build-both.ps1:103,126` 把产物复制到 `D:\项目\tipcl\windos\dist`。
**这里更正一处此前的误述**:`D:\项目\tipcl` 与 `D:\项目\TipCL` 在大小写不敏感的文件系统上是**同一个目录**
(`(Get-Item 'D:\项目\tipcl').FullName -eq (Get-Item 'D:\项目\TipCL').FullName` → `True`),
所以它就是**仓库内**的 `<repo>\windos\dist`,与相对路径 `windos/dist` 是同一落点;
仓库根 `.gitignore` 的 `dist/`(无前导斜杠 ⇒ 匹配任意层级)也覆盖它,**不会污染 `git status`**。
⇒ 它是「**硬编码绝对路径 + 旧布局名**」的**可移植性瑕疵**(换台机器/换 clone 路径带 `-Dist` 跑,会在 `D:\项目\tipcl\...` 这个陌生位置建目录),
**不是脏写、不是红线风险**;而且本草案的 Windows 链路**不依赖它**(§2.1 的 W2/W3 是手工留档,§2.1 顶部也明确"发布重切不带 `-Dist`")。
⇒ **发版前不改这个脚本**;是否清理(改成 `$Repo\windos\dist` / 去掉开关 / 保持现状)由用户拍板。

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

### 2.3 待填的清单三行(**发版当天在真资产上填**;版本号已定稿 `1.2.0`,只剩 `sha256` / `url` 要回填)

**A. `update.json`(Windows)** —— 现值 `version = 1.1.0` / `sha256 = 8d5f266bbf9d9c3f4231479d4c4e06a692ea5423863f63a7242126c6aa9b759f`(v1.1.0 的真值,**未改**)

```json
"version": "1.2.0",                                                                // = 已拍板生效号(硬约束仍是 > 1.1.0;R31 起不再叫"占位")
"url":     "https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL.exe",    // 待填:tag 名要跟着改
"sha256":  "<<发版那天用最终 commit 重新出包后再算,见 §1.5>>"
```

**B. `update-linux.json`(Linux)** —— 现值 `version = 0.1.0-m3` / `sha256 = REPLACE-WITH-THE-REAL-SHA256`(**故意格式非法**,未改)

```json
"version": "1.2.0",                                                                          // = 已拍板生效号(硬约束仍是 > 1.1.0)
"url":     "https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL-linux-x64.AppImage", // 待填(tag 名跟着改)
"sha256":  "<<Actions 日志里打印的那一行,见 §2.2 L4>>"
```

> 两份清单都**留在仓库根**、都没被 R30/R31 改动(红线)。
> `sha256` **绝不允许**写成 64 个 0 那种"格式合法但不存在"的假值 —— 那正是 R22 修掉的洞(`UpdaterManifestGuardTests` 守着)。

### 2.3.1 实际回填记录(2026-11-15)

由 R31 发版执行成员 `rel-release` 真机回填。tag `v1.2.0` 打在 `39056c8e596cb77e0e71bb2f8507e90f95aadf47`,
release id `445878011`:https://github.com/wfwwq/TipCL/releases/tag/v1.2.0

**资产真值(哈希均为回下载校验后取的值):**

| 资产 | 字节数 | SHA-256 |
|---|---|---|
| `TipCL.exe`(premium 标准件,人工上传) | 5,554,176 | `fcc10d90cda88f404047172ed147a9848ebac06739554237e4f913ec00f78857` |
| `TipCL-linux-x64.AppImage` | 39,090,680 | `a98e7d35930315258fb5980b042a56957c06a24a41576c9225d976132cd2f178` |
| `tipcl_1.2.0_amd64.deb` | 37,641,702 | 未单独校验(仅 `curl -sIL` 核 200) |

**两份清单实际填的值(§2.3 A/B 两处已按上表落地):**

- `update.json`:`version = 1.2.0`;`url = https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL.exe`;
  `sha256 = fcc10d90cda88f404047172ed147a9848ebac06739554237e4f913ec00f78857`。
- `update-linux.json`:`version = 1.2.0`;`url = https://github.com/wfwwq/TipCL/releases/download/v1.2.0/TipCL-linux-x64.AppImage`;
  `sha256 = a98e7d35930315258fb5980b042a56957c06a24a41576c9225d976132cd2f178`。

AppImage 的 sha256 与 `release.yml` linux job 日志里打印的清单行一致(两者各自独立取得:日志一行、回下载 `Get-FileHash` 一行)。
Windows 侧 `release.yml` 自建的 `TipCL.exe`(5,534,720 B,sha256 `eb7a369b26f6c3ec3696b112653bbd00a61e360ff1c5b8febc34c567429a8f8c`)
已按 §2.1 推荐口径 1 **删除并替换**为 premium 标准件。`README.md` 的 v1.1.0 SHA 行(`:203`)同步改为本次 `TipCL.exe` 的 sha256。

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
- 文件版本资源实测:`FileVersion = 1.2.0.0`、`ProductVersion = 1.2.0+25c4e64c...`、`AssemblyName.GetName().Version = 1.2.0.0`(`Revision = 0`,见 §1.2.2)。exe 内的版本串计数**必须先写明编码口径**:按 **UTF-16LE 整文件解码后作非重叠 ordinal 计数**,`1.2.0` = **4 处**、`1.1.0` = **0 处**、`TipCL/1.0.10` = **3 处**;按 **ASCII 解码**,`1.2.0` = **4 处**、`1.1.0` = 0 处、`TipCL/1.0.10` = 0 处。⚠️ 若改成"在**任意字节偏移**上匹配 UTF-16LE 模式",`1.2.0` 会数出 **6 处** —— 多出的 2 处是**跨字符边界的错位假命中**(奇数偏移单独解码只有 2 处);独立核对成员 `veri-docs` 报的 6 就是这个口径(`progress/evidence/wp5c/veri-docs.md` §3.4)。**本草案采用对齐计数 = 4 处**;两边完全一致的部分是:**`1.1.0` 在 exe 里 0 命中**。
- 与 `windos/docs/DELIVERY-2026-11-14.md` §3.1 记录的上一版最终件(**5,533,696 / 5,553,664**,commit `6b77220`,版本号 `1.1.0`)相比,本次两支各多 **512 B**(算出来的:`5,534,208 − 5,533,696 = 512`、`5,554,176 − 5,553,664 = 512`)。原因:**不同 commit**(本仓库 HEAD 是 `25c4e64`)+ 版本串内容变化;属"体积基本不变"的范围,但**发版时请以那一次实测为准**,不要引用本行。**注意:这里的 512 B 是"跨 commit 的体积差",与已被证伪的旧口径"同一 commit 下 CI 产物比标准件大 512 B"不是同一件事 —— 后者见 §2.1 W5 与 `R-WP5-05`。**
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

Windows 本机走 **无头** `--smoke`(`Program.cs:17` 是 `--smoke` 判定,调用在 **`:18`**——`Session.SmokeRunner.Run(BuildAvaloniaApp())`;`SmokeRunner.cs:65-66` 里显式 `.UseHeadless(...)`,**不会真的开窗**),`TIPCL_HOME` 指向隔离目录:

```
2026-11-15 09:53:51.854 [INFO] === TipCL 1.2.0 启动 pid=23696 os=Microsoft Windows NT 10.0.19045.0 ===
2026-11-15 09:53:51.855 [INFO] home=D:\项目\TipCL\progress\evidence\wp1\smoke-home cwd=...\smoke-out
```

- 冒烟退出码 `0`,`smoke.png` = 570,903 B(渲染链路也活着)。
- 横幅打印点 `src/TipCL.App/Common/AppLog.cs:67`;日志落在 `$TIPCL_HOME/logs/tipcl-<yyyyMMdd>.log`。
- **真实用户目录 `%LOCALAPPDATA%\TipCL` 全程零改写**(实测:该目录下**最新**文件 mtime 仍是 2026-11-10;会话期间(09:40 之后)新增/改写的文件**0 个**)。

---

## 4. Release Notes 定稿(中文,发布时可直接用)

> **口径声明**:下面标「**仅 Windows**」的条目在 Linux 版**没有**。原因见本节末尾的"待决口径"。 **（2026-11-16 补注:以下是 1.2.0 发版的历史定稿,R32 起 Linux 移植树已落地布局 v2 核心,v2 文档 / 引擎 / 编辑器 / 右键入口 / 自建区块可用;设置页布局视图、分栏 UI、整机方案、JSON 卡片、AI 排版仍没有。当前树状态见 `docs/README.md`,本节的「仅 Windows」只对 1.2.0 那次发版成立。）**

### TipCL 1.2.0

**主要是两件事:界面可以自己排,以及体积收回来一大截。**

#### 🧩 布局 v2:界面从"只能上下排"变成"想怎么摆就怎么摆"(仅 Windows)

- **三种摆放模式(逐页生效)**：`stack`(原来的纵向排列,保持默认)、**12 列网格**的 `grid`(可跨列、可拖拽定位、按行自适应高度)、按归一化坐标自由摆放的 `canvas`。**本版切模式的入口只有两个**：AI 排版(`layout_apply` 传 `mode`,`LayoutTools.cs:275-277`)与页面底部的「布局 JSON」卡片里改 `mode` 字段(`SettingsLayoutPage.cs:1476-1481`)—— GUI 里**没有**逐页的模式选择控件。
- **`canvas` 的适用范围是设计约定,不是代码门禁**：约定"只在声明了固定高度的页 / 块上用"(见 `windos/docs/layout-v2-design.md` Q2)，代码里对 canvas **只做归一化钳位**(`LayoutSchema.cs:562-567`)，不检查块是否声明了固定高度。
- **每块都能调外观**:跨列数、卡片/无边框/分隔线、自定义边距与间距、是否常显块标题。
- **左右分栏的比例可拖可记**：设计清单里 13 条"写死分栏"中，**6 条是左右两栏**；这 6 处的分隔条现在可以拖、松手就记住、双击复位(`PageLayoutHost.cs:276-288` 的 `TwoColumnCount()`，`windos/tests/TipCL.Tests/LayoutSplitTests.cs:92` 钉的就是 6；其余 7 条是 rail / 限宽限高 / 列表行模板 / 网格列数等，本来就没有左右分隔条)。
- **顶栏页签也能自定义**:顺序、显隐自己定。
- **窄窗 / 宽窗**：窗口宽度**跨过 760 px 阈值时运行时自动切到 `narrow` 变体**(阈值常量 `LayoutEngine.cs:1899`，接线 `MainWindow.xaml.cs:211,1677`)。**本版没有"在编辑哪一套"的选择器**：全树没有任何写入 `variants` 的路径(只有解析 / 克隆 / 规范化 / 读取，`LayoutSchema.cs:161` 上限 4 个)，编辑器落盘恒写主 body(`LayoutEditor.cs:685-700` → `LayoutEngine.cs:719-730`)⇒ **只有手工在布局 JSON 里写 `variants.narrow` 才会生效**。
- **整机布局方案**：页签 + 各页区块 + 分栏比例 + 自建区块 + 外观打包成**命名方案**，一键切换；**导出 / 导入**就是"落一份 `.json`、自己把文件传给对方、对方导入即可套用"(也支持只导出兼容旧版的"页面布局")—— **没有内置的分享 / 云同步通道**；导入时会先校验，不合法的字段会逐条说明原因。
- **向后兼容**:旧版布局文件(`ui_page_layouts.json`)的语义**一个字都没改**,继续按老格式写;升级不会丢你已有的布局。
- 设置页新增**「布局」管理视图**(方案增删改查 / 导入导出；页面底部还有一张「布局 JSON」卡片，装配点 `SettingsLayoutPage.cs:59`)。**注意：自建区块的专用入口本版未启用**(见下一节)。

#### 🤖 AI 也能排版(仅 Windows)

- 新增两个 AI 工具:`layout_get`(看当前布局:每页区块顺序与显隐、页面模式、自建区块、分栏比例、顶栏页签、已存方案名)与 `layout_apply`(改布局)。
- **改之前一定先存一份**:`layout_apply` 会先把当前布局备份成一个命名方案(**`AI 备份 <时间>`**,最多保留最近 5 份),随时可以切回去。
- 支持组合:改某一页的区块、应用某个已存方案、直接提交整份布局 JSON、新增/覆盖自建区块、改顶栏页签。

#### 🧱 自建区块:把你自己的东西放到任意一页(仅 Windows)

- 首期的五类都有渲染实现：**便签、时钟、快捷方式、分隔线、游戏时长**(类型常量 `LayoutSchema.cs:66-78`，渲染分派 `WidgetUI.cs:23-40`)。
- ⚠️ **界面入口本版未启用**：设置 → 布局 里那一行只是说明文字 —— **「添加自建区块」按钮是禁用的**(`SettingsLayoutPage.cs:427-428`，`add.IsEnabled = false` 且 handler 传 `null`；同页文案自己写着"由下一包(L6)落地后在这里增删改")。⇒ **本版能创建 / 修改自建区块的路径只有三条**：1 设置 → 布局 → 页面底部的「布局 JSON」卡片里手写 `layout.widgets`,再点卡片上的「校验 / 应用」(`SettingsLayoutPage.cs:59` 装配、`:693-743` 卡片实现)；2 **导入一份含 `widgets` 的整机布局方案 JSON**(`:606-660`;应用时的落盘点是 `LayoutProfiles.ApplySnapshot` `:1070-1084`，可"应用为当前布局"或"存为方案")；3 让 **AI 用 `layout_apply(widgets=[...])`** 写(`LayoutTools.cs:290-343`)。**别让用户到处找一个灰按钮。**
- **网页(`web`)与按钮(`button`)默认不出现**，要渲染必须在布局文档里**写 `settings.enabled = true`**(`WidgetCommon.cs:72-76`，默认 `false`)。**本版没有设置界面开关，也没有那道"启用确认"弹窗** —— 设置页那行同样只是说明文字(`SettingsLayoutPage.cs:432`)，全仓 34 处 `Dlg.Confirm` 没有一处与启用危险类型相关 ⇒ 只能按上面三条路径手写 / 让 AI 写。开启后仍有限制：网页只允许 `https`(禁 `file://` / `javascript:`)，按钮只允许**内部动作白名单**(切页 / 启动指定实例 / 打开启动器数据目录 / 只读查询)，其它一律拒绝并给出中文原因(`WidgetCommon.cs:82-207`、`DangerousWidgets.cs`)。

#### 🔌 插件也能贡献区块(Node 与 .NET 都支持)

- 插件的 UI 贡献新增 `block` 类型:插件可以往布局里注册一个**自己的控件**当区块。
- Node 插件:`tipcl-plugin.d.ts` 里新增 `block(spec): Dispose`。
- .NET 插件：提供了最小真实样例(`windos/samples/dotnet-block-plugin/`)与端到端驱动(走启动器自己的启动路径 + 真实 127.0.0.1 桥)，实测 **PASS 13 / FAIL 0**(口径：.NET 插件 `kind=block` 的端到端驱动项；`windos/docs/verify-wp2-2026-11-15.md:106`;原始日志 `progress/evidence/wp2/native-block-e2e.log` **第 90 行**的 `RESULT: PASS  (通过 13 / 失败 0)` 行(该文件共 94 行,这一行之后还有 `driver exit code = 0` 与两行路径 ⇒ **不是末行**)。另有 2 条 `[WARN]` 是独立驱动没有 `App.xaml` 资源字典所致，非产品缺陷；插件经真加载器进注册表、停用后条目与 RPC 行同时消失)。
- 插件卸载后它在布局里的块**不会被删**,界面上显示「不可用」,装回来即恢复。

#### 🐧 Linux 版(AppImage / `.deb` / 自包含 / 自更新)

> **本节与上面的布局无关**：布局 v2 / AI 排版 / 自建区块**不在**本次 Linux 版内(Linux 树对 `LayoutStoreV2` / `layout_get` / `ModeGrid` / `ModeCanvas` 全树 0 命中，仍是 v1 页面布局)。

- 提供 **AppImage**(`TipCL-linux-x64.AppImage`)与 **`.deb`**(`tipcl_<版本>_amd64.deb`),均为 **self-contained 单文件**,机器上**不需要装 .NET**。
- **自更新的分支在代码层是明确的**：清单地址可被 `TIPCL_UPDATE_URL` 覆盖(`Config.cs:136`)；接管目标只认 `$APPIMAGE`(`Updater.cs:111-137`)；就地替换是同目录写 `*.new` 再原子 rename(`:167-177`)；**目标目录不可写时自动降级 `mode=download-only`**(`:174-175`)，外壳据此提示「**需要手动安装**」(`MainWindow.axaml.cs:256-258`)。版本号没超过客户端时显示「已是最新版本」，**占位 / 格式非法的 sha256 会被当场拒绝并说明原因**(`Updater.cs:100-102`)。
- ⚠️ **措辞等级(别写成"已在真机验证")**：「真机端到端走过『立即重启 / 稍后重启』两条分支」「下载失败在下载任务页有失败行」这两条目前**只有交接文档的自述**(`docs/HANDOFF-d95a9472.md:31-39,58-59`)，其原始日志与截图(`scratch/update-e2e2/` 等)**不在本仓库** ⇒ 对外只能写「**按设计：AppImage 就地替换 / `.deb` 提示手动安装**」；要写成"真机验证过"，得先把证据贴回仓库或重跑一次。
- **`.deb` 安装形态**只下载、不就地替换(装在 `/opt`),会提示"需要手动安装"。
- 安装账号闸门、权限档位说明等若干体验修补。

#### 📦 体积回收(compact,红线 5,757,264 B)

| 阶段 | compact |
|---|---|
| 基线 `9a3cc52` | 6,447,616 B |
| + **A**(Core 资源 gzip 内嵌) | 6,061,056 B(−386,560) |
| + **B1**(排除 `System.Text.Encoding.CodePages`) | **5,533,184 B** |

⇒ 相对基线**累计 −914,432 B / −14.2%**；改造前**超红线 690,352 B**，改完回到线内、**余量 224,080 B**。出处：`windos/docs/DELIVERY-2026-11-14.md:160-164` 与 `DECISIONS.md`(D-38)；两笔提交 `0c0f975`(基线 → A)、`b19012c`(A → B1)自带同样数字。

> ⚠️ 上表是 **11-14 那一版**的实测；**1.2.0 本版 compact 的实测是 5,534,208 B / 余量 223,056 B**(§3.2，可逐字节复核)。两个数只差 1,024 B，但**别混用**。

做法两条:① Core 资源(locales / ai / plugin-host 共 15 个源文件)改**构建期 gzip 内嵌**,读取侧按 gzip 魔数**透明解压**,运行时释放的字节与改造前**逐字节相同**(15 例漂移守卫 `CoreResourcesPackTests` 钉住)**;② 排除 ImageSharp 的传递依赖 `System.Text.Encoding.CodePages`(它是唯一用到 `Encoding.GetEncoding(437)` 的路径的替代实现,已用**隔离单 exe + 真实有损 VP8 WebP** 的反射夹具验证:能解码、`codePagesLoaded = False`、WebView2 的三个 loader **仍在**)。
⇒ 体积回收后 compact 从"超红线"回到"线内"(数字与出处见上表与注；1.2.0 本版实测余量 223,056 B)。

#### 🛠 修复项

| 修复 | 影响 |
|---|---|
| 反馈弹窗的「AI 修复」在 `chat_id` 不存在时按 id 建对话 | 不再把 AI 修复的回合错误地落到主界面当前对话里 |
| `-p:Premium=true` 干净 HEAD 下编译报 `CS0103(_immersive)` | 此前 **premium 交付件不可复现**,已修 |
| 受限宿主里**测试夹具**的假手机端 `HttpListener` 被拒导致用例必失败 | 改用裸 socket;验收口径从"排除 `PhoneLinkTests`"**恢复为全量**(纯测试基建修复,**不代表手机同步已端到端可用**,见下方「已知问题」第 1 条) |
| 插件页 `plugins_ui` 重复行 | 已修(含 7 例单测) |
| i18n:两处"只差标点宽度"的近似重复 key，以及 3 处因此查不到译文的源码字面量 | **移植树**词表 en/zh 各 **2995** 条、键集相等、缺键 0；**Windows 树**(1.2.0 Windows 交付件所用的那一份)是 **2952 / 2952**、键集同样相等(`R30-WP6c-02` 复算两份 json：移植树 − Windows 树 = 43 个键，反向差 0。`progress/R30-ACCEPT.md` §2.8 的数字只覆盖移植树)。**旧口径 2951 已作废** |
| 体积回收引入的两个隐蔽坑(见上) | 已带漂移守卫用例(15 例) |
| 仓库卫生:`.gitignore` 补 `artifacts/`、`*_wpftmp.csproj`、`_wpf_run/` | 生成物不再长期挂脏 |

#### ⚠️ 已知问题(本轮**都不修**;发版说明请按这里的口径措辞)

1. **「手机同步」目前只有桌面端一半 —— 不要写成端到端可用的新功能。**
   桌面端(`windos/src/TipCL/Core/Launcher/Sync/`、设置页「手机同步」与同步面板)在仓库里;
   **手机端服务端在 `TipCL-Android` 里找不到对应实现**(WP-4 全库取证:19,336 个 smali + Java 源码 + `android-app`,
   `X-PyMCL-Token` / `/api/ping` / `/api/worlds` / `deviceTimeMs` / `配对码` **全部 0 命中**;`27531` 的唯一命中是 Compose
   编译期 sourceInformation 字面量,属假阳性)。
   ⇒ 措辞只能是「**桌面端协议与 UI 已就绪,手机端待配套**」,**别写「现在就能和手机互传存档」**;
   `README.md` 的「📱 手机同步」一节若要改,**只能减少承诺、不能增加**;「手机端代码在哪个仓库」= **待用户确认**。
2. **桌面 `PhoneClient` 从不校验 `/api/ping` 返回的 `protocol`**(本轮独立复核:全树 `protocol` 命中只有
   `Sync/PhoneClient.cs:15`(注释)、`:28-29`(`public int Protocol;`)、`:217`(`Protocol = (int)Num(j, "protocol")`)——
   **写进去之后再没有任何一处读它、更没有任何比较**)⇒ 将来手机端上到「协议 2」会被**静默当协议 1 用**。
3. **`windos/src/TipCL/Core/Launcher/Utils.cs:790`:`WriteJson` 自称的 6 轮重试覆盖不到 `MkStemp` 抛的异常**
   (本轮独立复核源码:`MkStemp(...)` 在 `:790`,包住它的 `try` 从 `:791` 才开始;而 `MkStemp` 内部 `:768` **只 catch `IOException`**,
   `FileMode.CreateNew` 撞上杀软拦截抛的 `UnauthorizedAccessException` 会**直接穿出 `WriteJson`**)⇒
   杀软拦"新建 tmp 文件"时,保存配置/存档会**偶发失败**,注释里承诺的 6 轮 + 直写兜底全部失效。(WP-0 的 R2 与 WP-4 各自报过,本轮复核一致)
4. **安卓版本号现状(不要替用户宣布"与桌面同号")**:新线 `android-app` = `versionName 0.1.0` / `versionCode 1`;
   旧线 `app/Android` = `versionName 0.9.3` / `versionCode 14`;包名也不同(`com.tipcl.android` vs `com.pymcl.android`)。
   **是否与桌面统一到 `1.2.0` 是拍板项**,发版说明里只写现状。
5. **界面上的自建区块入口本版未启用**：设置 → 布局 里的「添加自建区块」按钮禁用(`SettingsLayoutPage.cs:427-428`)，且没有启用危险类型的开关 ⇒ 只能走布局 JSON 编辑器 / 导入方案 JSON / AI 工具三条路径(见上「自建区块」小节)。**发版说明里不要写成「在设置里开一下就行」。**
6. **布局变体(`variants`)本版没有编辑入口**：能读、能在窄窗下生效，但全树没有写入路径(编辑器落盘恒写主 body)⇒ 只有手写布局 JSON 才生效。另：`ApplyPage` 恒写主 body 这一点，将来开放变体编辑前先要修(`LayoutEditor.cs:685-700` → `LayoutEngine.cs:719-730`)。

#### ⚠️ 待决口径(发版前必须由用户/维护者确认,别默认)

1. **「仅 Windows」的那几项**:布局 v2 / AI 排版 / 自建区块**目前只在 Windows 有**,Linux 仍是旧区块布局。
   本稿按"**仅 Windows**"口径写。**若用户选择本轮就把布局 v2 移植到 Linux,本节标题里的「仅 Windows」必须去掉,并补一份 Linux 侧的验收证据**;移植评估以 WP-3 的结论为准。
2. **Windows 交付件到底用哪一份**:见 §2.1 的 W5。**旧口径"CI 的 `dotnet build` 产物比标准件大 512 B"已证伪**(两条路径产物同为 `5,534,208 B`,只是 sha256 不同)⇒ 拍板点其实是"**用哪一份、就必须在那一份产物上算 sha256**",而不是"哪一份更小";推荐口径仍是 1(手工上传 W1/W3 的标准件)。
3. **`README.md` 里的 SHA256 行**:本轮**没有**把 v1.2.0 的哈希写进 README(没发版就不能写),发版那天要补。
4. **「手机同步」的措辞与手机端归属**:见上「已知问题」第 1 条。**手机端在哪个仓库、本轮是否随发** = 用户拍板项;
   桌面 README 现在那句"先在手机启动器里打开 存档管理 → 从 zip 导入 → 启动同步服务"在本仓库**找不到对应实现**,
   属**应当减少承诺**的句子 —— 本轮**没有**改它(本包对 `README.md` 的授权只有"版本相关段落"),
   已上报 lead,请指派。
5. **安卓是否与桌面同号**:见上「已知问题」第 4 条,`1.2.0` 只覆盖桌面(Windows + Linux)两棵树,**不含安卓**。

---

## 5. README 改动清单(本轮实际改了什么)

| # | 段落 | 改动 |
|---|---|---|
| R1 | 标题下的英文副标题(`README.md:11`) | `An AI-native Minecraft launcher for Windows` → `... for Windows and Linux` |
| R2 | 平台徽章(`README.md:15-22`) | 在 WPF 徽章后**新增一行** Linux/Avalonia x64 徽章(`FCC624` + `logo=linux`);其余徽章不动 |
| R3 | 「📦 下载与构建」(`README.md:83-118`;原 Windows 段 `:85-101` 未改,新增的 Linux 三段在 `:103-118`) | 新增 **Linux 的三段**:1 Releases 里的 `TipCL-linux-x64.AppImage`(自包含、免安装)与 `tipcl_<版本>_amd64.deb`;2 两者各自的安装/运行命令;3 自己编译 Linux(`dotnet build TipCL.Linux.sln`)、Linux 数据目录 `~/.tipcl`(可用 `TIPCL_HOME` 覆盖)。原有 Windows 段落**未改**。(行号校正:`R30-WP6c`,原写 `:82-100`) |
| R4 | 「🛡️ 安全说明」:标题 `README.md:199`、v1.1.0 真 SHA 行 `:203`(保留)、新增的 Linux 行 `:204` | **保留** v1.1.0 那行真实 SHA256(`:203` 是**故意保留的活值**,不能删也不能改);**新增一行**(`:204`)说明 Linux 侧的校验方式 —— Linux 的两个资产**不带 `.sha256` 附件**(实测 `release.yml:47-48` 只给 Windows 传了 `TipCL.exe.sha256`),AppImage 的 SHA-256 **只出现在 Actions 日志里**(`release.yml:95-106` 的三条 `echo`;**那一步没有任何写 release notes 的动作**),可用 `sha256sum` 自行比对。(**R30-WP6c 修正**:原写 `:185`,实为标题 `:199`、SHA 行 `:203`、新增行 `:204`;口径也校正了 —— 原写"发行说明/Actions 日志"两种,与 §2.2 L4 自相矛盾,现统一为"Actions 日志",`README.md:204` 同步改了这半句) |

**刻意没做的**:没有给 README 加 v1.2.0 的 SHA256(未发版,写了就是伪造发布状态);没有改「更新机制」「仓库结构」等与版本无关的段落;没有把 README 的 Windows 主体改写成双平台文档(那是更大的编辑决定,留给用户/维护者)。 **`R30-WP6c` 补记**:本轮在 `docs/RELEASE-DRAFT.md` 之外**只动了 `README.md:204` 那半句措辞**(AppImage 的 SHA-256 只进 Actions 日志、不写 release notes),其余仓库文件零改动。

---

## 6. 关联

- 证据目录:`progress/evidence/wp1/`(构建日志、两份 trx、冒烟 applog、探针输出、两个演练 exe)
- 工作包报告:`progress/R30-WP1.md`(含自证命令与原始输出、测试逐条定性、偏离说明)
- 发版相关既有文档:`windos/docs/DELIVERY-2026-11-14.md`(体积/可追溯性/9 条隐蔽坑)、`docs/HANDOFF-d95a9472.md`(自更新闭环判定与"必须 > 1.1.0")、`windos/docs/layout-v2-design.md`(布局 v2 契约与已拍板的 7 个决策)
- 返工单处置:本文件的口径修正由 `R30-WP6c` 完成,自证命令与改前/改后片段见 `progress/R30-WP6c.md`
- 独立核对证据:`progress/evidence/wp5c/veri-docs.md`(**只读**核对:13 个落点逐行核对、`TipCL/1.0` 家族清单、`app` 字段、行号失真清单)
- R31(版本号转生效 + UA 派生)施工报告:`progress/R31-A.md`(提交 `5056f5b` / `6ea4a24`,证据 `progress/evidence/r31/`);**未 push、未打 tag、未发 release**
